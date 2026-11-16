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
    else if f.ends_with("-windows-x86_64.zip") { Some("windows-x86_64-zip") }
    else if f.ends_with(".appimage") { Some("linux-x86_64-appimage") }
    else if f.ends_with(".deb") { Some("linux-x86_64-deb") }
    else { None }
}

/// Stable for plain versions, beta for pre-releases (1.2.0-beta.1).
pub fn channel_for(version: &str) -> &'static str {
    if version.trim_start_matches('v').split('+').next().unwrap_or("").contains('-') { "beta" } else { "stable" }
}

pub fn build(version: &str, channel: &str, notes_url: &str, assets: &[ReleaseAsset]) -> String {
    let mut m = Map::new();
    for a in assets {
        m.insert(a.key.clone(), json!({"url": a.url, "sha256": a.sha256.to_lowercase(), "size": a.size}));
    }
    let v: Value = json!({"version": version.trim_start_matches('v'), "channel": channel, "notes_url": notes_url, "assets": m});
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
        assert_eq!(asset_key("barbican-1.0.0-windows-x86_64.zip"), Some("windows-x86_64-zip"));
        assert_eq!(asset_key("Barbican-1.0.0-x86_64.AppImage"), Some("linux-x86_64-appimage"));
        assert_eq!(asset_key("barbican_1.0.0-1_amd64.deb"), Some("linux-x86_64-deb"));
        assert_eq!(asset_key("SHA256SUMS"), None);
        assert_eq!(channel_for("v1.2.0"), "stable");
        assert_eq!(channel_for("1.2.0-beta.1"), "beta");
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
