//! Building and signing the update manifest that `src/updater.rs` reads. Shared by the app
//! (unit tests check that what is built here verifies there) and by the release tool
//! `examples/release_manifest.rs`, which the release workflow runs.
#![allow(dead_code)]
use serde_json::{json, Map, Value};

/// One file of a release: the updater's asset key (`windows-x86_64-msi`, ...) and where it is.
#[derive(Debug, Clone, PartialEq)]
pub struct ReleaseAsset {
    pub key: String,
    pub url: String,
    pub sha256: String,
    pub size: u64,
}

/// Which updater asset key a release file name stands for (None for checksums etc.).
pub fn asset_key(file: &str) -> Option<&'static str> {
    let f = file.to_ascii_lowercase();
    if f.ends_with(".msi") { Some("windows-x86_64-msi") }
    else if f.starts_with("barbican-setup-") && f.ends_with(".exe") { Some("windows-x86_64-inno") }
    else if f.ends_with("-windows-x86_64.zip") { Some("windows-x86_64-zip") }
    else if f.ends_with(".appimage") { Some("linux-x86_64-appimage") }
    else if f.ends_with(".deb") { Some("linux-x86_64-deb") }
    else { None }
}

/// Stable for plain versions, beta for pre-releases (1.2.0-beta.1).
pub fn channel_for(version: &str) -> &'static str {
    if version.trim_start_matches('v').split('+').next().unwrap_or("").contains('-') { "beta" } else { "stable" }
}

/// How long a signed manifest is trusted (src/updater.rs check_fresh). Releases, or a re-run
/// of the release tool, must come more often than this.
pub const VALID_SECS: u64 = 180 * 24 * 60 * 60;

/// The channels a release's manifest is written for: a beta only for beta; a stable release
/// for both, so beta testers are offered it as soon as it ships.
pub fn channels_for(version: &str) -> &'static [&'static str] {
    if channel_for(version) == "beta" { &["beta"] } else { &["stable", "beta"] }
}

/// MSI ProductVersion for a tag. Windows Installer takes only major.minor.build (255.255.65535)
/// and ignores a fourth field when it compares versions, so the pre-release goes into the
/// build number: patch * 1000 + a rank, alpha.N = 100+N, beta.N = 300+N, rc.N = 600+N, other
/// pre-releases 50, the stable release 999. So 1.2.0-beta.1 < 1.2.0-rc.2 < 1.2.0 < 1.2.1-beta.1.
pub fn msi_version(version: &str) -> Result<String, String> {
    let v = version.trim().trim_start_matches('v').split('+').next().unwrap_or("");
    let (core, pre) = match v.split_once('-') { Some((c, p)) => (c, Some(p)), None => (v, None) };
    let n: Vec<u64> = core.split('.').map(|x| x.parse::<u64>().map_err(|_| format!("bad version {version}"))).collect::<Result<_, _>>()?;
    let [major, minor, patch] = n[..] else { return Err(format!("bad version {version}")) };
    if major > 255 || minor > 255 || patch > 64 { return Err(format!("{version} does not fit an MSI version")); }
    let rank = match pre {
        None => 999,
        Some(p) => {
            let mut parts = p.split('.');
            let label = parts.next().unwrap_or("").to_ascii_lowercase();
            let num = parts.next().and_then(|x| x.parse::<u64>().ok()).unwrap_or(0);
            if num > 199 { return Err(format!("{version}: pre-release number too high for an MSI version")); }
            match label.as_str() { "alpha" => 100 + num, "beta" => 300 + num, "rc" => 600 + num, _ => 50 }
        }
    };
    Ok(format!("{major}.{minor}.{}", patch * 1000 + rank))
}

pub fn build(version: &str, channel: &str, notes_url: &str, assets: &[ReleaseAsset]) -> String {
    let now = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map(|d| d.as_secs()).unwrap_or(0);
    build_at(version, channel, notes_url, assets, now)
}

pub fn build_at(version: &str, channel: &str, notes_url: &str, assets: &[ReleaseAsset], now: u64) -> String {
    let mut m = Map::new();
    for a in assets {
        m.insert(a.key.clone(), json!({"url": a.url, "sha256": a.sha256.to_lowercase(), "size": a.size}));
    }
    let v: Value = json!({"version": version.trim_start_matches('v'), "channel": channel, "notes_url": notes_url,
        "published": now, "expires": now + VALID_SECS, "assets": m});
    serde_json::to_string_pretty(&v).unwrap()
}

/// base64 Ed25519 signature over `bytes`; the key is base64 of the 32-byte secret seed.
pub fn sign(seed_b64: &str, bytes: &[u8]) -> Result<String, String> {
    use base64::Engine;
    use ed25519_dalek::{Signer, SigningKey};
    let b64 = base64::engine::general_purpose::STANDARD;
    let seed: [u8; 32] = b64.decode(seed_b64.trim()).map_err(|_| "bad signing key")?.try_into().map_err(|_| "signing key must be 32 bytes")?;
    Ok(b64.encode(SigningKey::from_bytes(&seed).sign(bytes).to_bytes()))
}

/// base64 public key for a base64 seed (what goes into BARBICAN_UPDATE_PUBKEY).
pub fn public_key(seed_b64: &str) -> Result<String, String> {
    use base64::Engine;
    let b64 = base64::engine::general_purpose::STANDARD;
    let seed: [u8; 32] = b64.decode(seed_b64.trim()).map_err(|_| "bad signing key")?.try_into().map_err(|_| "signing key must be 32 bytes")?;
    Ok(b64.encode(ed25519_dalek::SigningKey::from_bytes(&seed).verifying_key().to_bytes()))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn keys_and_channels() {
        assert_eq!(asset_key("Barbican-1.0.0-x86_64.msi"), Some("windows-x86_64-msi"));
        assert_eq!(asset_key("Barbican-Setup-1.0.0.exe"), Some("windows-x86_64-inno"));
        assert_eq!(asset_key("Barbican-Setup-1.0.0-beta.1.exe"), Some("windows-x86_64-inno"));
        assert_eq!(asset_key("barbican.exe"), None);
        assert_eq!(asset_key("barbican-1.0.0-windows-x86_64.zip"), Some("windows-x86_64-zip"));
        assert_eq!(asset_key("Barbican-1.0.0-x86_64.AppImage"), Some("linux-x86_64-appimage"));
        assert_eq!(asset_key("barbican_1.0.0-1_amd64.deb"), Some("linux-x86_64-deb"));
        assert_eq!(asset_key("SHA256SUMS"), None);
        assert_eq!(channel_for("v1.2.0"), "stable");
        assert_eq!(channel_for("1.2.0-beta.1"), "beta");
        assert_eq!(channels_for("v1.2.0"), ["stable", "beta"]);
        assert_eq!(channels_for("v1.2.0-beta.3"), ["beta"]);
    }

    #[test]
    fn msi_versions() {
        let m = |v: &str| msi_version(v).unwrap();
        assert_eq!(m("v1.2.0"), "1.2.999");
        assert_eq!(m("1.2.0-beta.1"), "1.2.301");
        assert_eq!(m("1.2.3-rc.2"), "1.2.3602");
        // numeric order of the build field follows the release order
        let order = ["1.2.0-alpha.1", "1.2.0-beta.1", "1.2.0-beta.12", "1.2.0-rc.1", "1.2.0", "1.2.1-beta.1", "1.2.1"];
        let builds: Vec<u64> = order.iter().map(|v| m(v).rsplit('.').next().unwrap().parse().unwrap()).collect();
        assert!(builds.windows(2).all(|w| w[0] < w[1]), "{builds:?}");
        assert!(builds.iter().all(|b| *b <= 65535));
        assert!(msi_version("256.0.0").is_err());
        assert!(msi_version("1.2").is_err());
        assert!(msi_version("1.2.0-beta.500").is_err());
    }

    #[test]
    fn manifest_freshness_fields() {
        let body = build_at("1.0.0", "stable", "https://x", &[], 1000);
        let v: Value = serde_json::from_str(&body).unwrap();
        assert_eq!(v["published"], 1000);
        assert_eq!(v["expires"], 1000 + VALID_SECS);
    }

    /// A stable release is offered to beta testers too: its beta.json verifies there and
    /// beats the last beta.
    #[test]
    fn stable_release_reaches_beta_channel() {
        let seed = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";
        let pk = public_key(seed).unwrap();
        for ch in channels_for("v1.2.0") {
            let body = build("v1.2.0", ch, "https://example.org/r", &[]);
            let m = crate::updater::verify_manifest(body.as_bytes(), &sign(seed, body.as_bytes()).unwrap(), &pk, ch).unwrap();
            assert!(crate::updater::check_fresh(&m, crate::updater::now_secs(), None).is_ok());
            if *ch == "beta" {
                let cur = crate::updater::Version::parse("1.2.0-beta.3").unwrap();
                assert_eq!(crate::updater::decide(&cur, &m.version, "beta", crate::updater::Kind::Msi), crate::updater::Decision::Install);
            }
        }
    }

    #[test]
    fn built_manifest_verifies_in_updater() {
        let seed = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";
        let pk = public_key(seed).unwrap();
        let sha = "ab".repeat(32);
        let assets = vec![ReleaseAsset { key: "windows-x86_64-msi".into(), url: "https://example.org/b.msi".into(), sha256: sha.clone(), size: 7 }];
        let body = build("v1.4.0", "stable", "https://example.org/r", &assets);
        let sig = sign(seed, body.as_bytes()).unwrap();
        let m = crate::updater::verify_manifest(body.as_bytes(), &sig, &pk, "stable").unwrap();
        assert_eq!(m.version_text, "1.4.0");
        assert_eq!(m.assets[0].1.sha256, sha);
        assert_eq!(m.assets[0].1.size, 7);
        assert!(crate::updater::verify_manifest(body.as_bytes(), &sig, &pk, "beta").is_err());
        assert!(sign("short", b"x").is_err());
    }
}
