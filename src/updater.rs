//! Updates (Bulwark: lib/desktop/types.ts DesktopUpdateState, components/settings/
//! desktop-update-settings.tsx, app-update-prompt.tsx).
//!
//! The release workflow publishes one manifest per channel (`stable.json`, `beta.json`) and
//! its Ed25519 signature (`<channel>.json.sig`, base64 of the raw 64-byte signature over the
//! file's bytes). The public key is built into the app (`BARBICAN_UPDATE_PUBKEY`, base64 of
//! the 32-byte key) together with the feed address (`BARBICAN_UPDATE_FEED`, with `{channel}`
//! standing for the channel). A build without them never checks.
//!
//! A newer version is downloaded in the background into the data folder and checked
//! against the SHA-256 in the signed manifest. Nothing is installed until the user says so:
//! "Restart now" installs at once, "Install next time" installs when Barbican next starts.
//! Inno Setup (per-user), MSI (per-user, on hold) and AppImage installs replace themselves; the
//! portable zip, a .deb and a development build only say that a version is available.
use serde_json::{json, Value};
use std::path::{Path, PathBuf};
use std::sync::Mutex;

pub const VERSION: &str = env!("CARGO_PKG_VERSION");
const BUILT_PUBKEY: Option<&str> = option_env!("BARBICAN_UPDATE_PUBKEY");
const BUILT_FEED: Option<&str> = option_env!("BARBICAN_UPDATE_FEED");
/// Where "Download" and release notes go when the manifest has no link.
const BUILT_RELEASES: Option<&str> = option_env!("BARBICAN_RELEASES_URL");
/// How often a running app checks (with "Check for updates automatically" on).
pub const CHECK_EVERY: std::time::Duration = std::time::Duration::from_secs(4 * 60 * 60);

// ---------------- versions ----------------

/// A semantic version: 1.2.3 or 1.2.3-beta.4 (build metadata ignored).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Version {
    pub nums: [u64; 3],
    pub pre: Vec<String>,
}

impl Version {
    pub fn parse(s: &str) -> Option<Version> {
        let s = s.trim().trim_start_matches('v');
        let s = s.split('+').next()?;
        let (core, pre) = match s.split_once('-') { Some((c, p)) => (c, Some(p)), None => (s, None) };
        let mut it = core.split('.');
        let mut nums = [0u64; 3];
        for n in &mut nums { *n = it.next()?.parse().ok()?; }
        if it.next().is_some() { return None; }
        let pre = pre.map(|p| p.split('.').map(String::from).collect::<Vec<_>>()).unwrap_or_default();
        if pre.iter().any(|p| p.is_empty()) { return None; }
        Some(Version { nums, pre })
    }

    pub fn is_pre(&self) -> bool { !self.pre.is_empty() }
}

impl PartialOrd for Version {
    fn partial_cmp(&self, o: &Self) -> Option<std::cmp::Ordering> { Some(self.cmp(o)) }
}

impl Ord for Version {
    fn cmp(&self, o: &Self) -> std::cmp::Ordering {
        use std::cmp::Ordering::*;
        match self.nums.cmp(&o.nums) {
            Equal => {}
            x => return x,
        }
        // a pre-release sorts before its release; identifiers compare numerically when both are numbers
        match (self.pre.is_empty(), o.pre.is_empty()) {
            (true, true) => return Equal,
            (true, false) => return Greater,
            (false, true) => return Less,
            _ => {}
        }
        for (a, b) in self.pre.iter().zip(&o.pre) {
            let x = match (a.parse::<u64>(), b.parse::<u64>()) {
                (Ok(x), Ok(y)) => x.cmp(&y),
                (Ok(_), Err(_)) => Less,
                (Err(_), Ok(_)) => Greater,
                _ => a.cmp(b),
            };
            if x != Equal { return x; }
        }
        self.pre.len().cmp(&o.pre.len())
    }
}

// ---------------- install kinds ----------------

#[derive(Debug, Clone, Copy, PartialEq)]
pub enum Kind {
    /// Inno Setup, per user: runs the new setup silently.
    Inno,
    /// Windows Installer, per user (on hold, see wix/main.wxs): installs the new MSI.
    Msi,
    /// Windows portable zip: says a version is available.
    Zip,
    /// Linux AppImage: replaces the file in place.
    AppImage,
    /// Debian package: the package manager owns the files, so only a notice.
    Deb,
    /// cargo run / an unpacked build.
    Dev,
}

impl Kind {
    pub fn asset(self) -> &'static str {
        match self {
            Kind::Inno => "windows-x86_64-inno",
            Kind::Msi => "windows-x86_64-msi",
            Kind::Zip => "windows-x86_64-zip",
            Kind::AppImage => "linux-x86_64-appimage",
            Kind::Deb => "linux-x86_64-deb",
            Kind::Dev => if cfg!(windows) { "windows-x86_64-inno" } else { "linux-x86_64-appimage" },
        }
    }

    pub fn can_install(self) -> bool { matches!(self, Kind::Inno | Kind::Msi | Kind::AppImage) }
}

/// The installers leave `install-kind` next to the program ("inno" / "msi" / "zip" / "deb").
pub fn detect_kind(exe: &Path, appimage: Option<&str>, marker: Option<&str>) -> Kind {
    if appimage.is_some_and(|a| !a.is_empty()) { return Kind::AppImage; }
    match marker.map(str::trim) {
        Some("inno") => Kind::Inno,
        Some("msi") => Kind::Msi,
        Some("zip") => Kind::Zip,
        Some("deb") => Kind::Deb,
        _ if exe.starts_with("/usr") => Kind::Deb,
        _ => Kind::Dev,
    }
}

pub fn current_kind() -> Kind {
    if crate::automation::enabled() {
        if let Ok(k) = std::env::var("BARBICAN_UPDATE_KIND") {
            return match k.as_str() { "inno" => Kind::Inno, "msi" => Kind::Msi, "zip" => Kind::Zip, "appimage" => Kind::AppImage, "deb" => Kind::Deb, _ => Kind::Dev };
        }
    }
    let exe = std::env::current_exe().unwrap_or_default();
    let marker = exe.parent().and_then(|d| std::fs::read_to_string(d.join("install-kind")).ok());
    detect_kind(&exe, std::env::var("APPIMAGE").ok().as_deref(), marker.as_deref())
}

// ---------------- manifest ----------------

#[derive(Debug, Clone, PartialEq)]
pub struct Asset {
    pub url: String,
    pub sha256: String,
    pub size: u64,
}

#[derive(Debug, Clone, PartialEq)]
pub struct Manifest {
    pub version: Version,
    pub version_text: String,
    pub channel: String,
    pub notes_url: String,
    pub assets: Vec<(String, Asset)>,
    /// Unix seconds: when the release tool signed it, and after when it is no longer trusted.
    pub published: u64,
    pub expires: u64,
}

pub fn now_secs() -> u64 {
    std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map(|d| d.as_secs()).unwrap_or(0)
}

/// Against replay and freeze attacks: an expired manifest, or one older (by version or by
/// signing time) than one this install already accepted, is refused.
pub fn check_fresh(m: &Manifest, now: u64, seen: Option<&Seen>) -> Result<(), String> {
    if m.expires == 0 || m.expires < now { return Err("The update information has expired.".into()); }
    if let Some(s) = seen {
        let older = Version::parse(&s.version).is_some_and(|sv| m.version < sv) || m.published < s.published;
        if older { return Err("The update information is older than what this computer has seen.".into()); }
    }
    Ok(())
}

/// The newest manifest accepted per channel (kept beside, not inside, the downloads folder,
/// which is deleted after an install).
#[derive(Debug, Clone, PartialEq)]
pub struct Seen {
    pub version: String,
    pub published: u64,
}

fn seen_file(channel: &str) -> PathBuf {
    updates_dir().with_file_name(format!("update-seen-{channel}.json"))
}

pub fn load_seen(f: &Path) -> Option<Seen> {
    let v: Value = serde_json::from_str(&std::fs::read_to_string(f).ok()?).ok()?;
    Some(Seen { version: v["version"].as_str()?.into(), published: v["published"].as_u64()? })
}

fn save_seen(f: &Path, s: &Seen) {
    if let Some(d) = f.parent() { let _ = std::fs::create_dir_all(d); }
    let _ = std::fs::write(f, json!({"version": s.version, "published": s.published}).to_string());
}

/// Only https links from a manifest are ever opened or fetched.
fn https_only(u: &str) -> String {
    if u.starts_with("https://") && !u.chars().any(|c| c.is_whitespace() || c.is_control()) { u.into() } else { String::new() }
}

/// Verify the signature over the exact bytes, then read them. Nothing unsigned is trusted.
pub fn verify_manifest(bytes: &[u8], sig_b64: &str, pubkey_b64: &str, channel: &str) -> Result<Manifest, String> {
    use base64::Engine;
    use ed25519_dalek::{Signature, VerifyingKey};
    let b64 = base64::engine::general_purpose::STANDARD;
    let key: [u8; 32] = b64.decode(pubkey_b64.trim()).map_err(|_| "bad public key")?.try_into().map_err(|_| "bad public key")?;
    let key = VerifyingKey::from_bytes(&key).map_err(|_| "bad public key")?;
    let sig: [u8; 64] = b64.decode(sig_b64.trim()).map_err(|_| "bad signature")?.try_into().map_err(|_| "bad signature")?;
    key.verify_strict(bytes, &Signature::from_bytes(&sig)).map_err(|_| "The update's signature does not match.")?;
    let v: Value = serde_json::from_slice(bytes).map_err(|e| e.to_string())?;
    let version_text = v["version"].as_str().unwrap_or("").to_string();
    let version = Version::parse(&version_text).ok_or("bad version in manifest")?;
    // a signed manifest of another channel must not be served in this one's place
    let ch = v["channel"].as_str().unwrap_or("");
    if ch != channel { return Err(format!("manifest is for the {ch} channel")); }
    let mut assets = vec![];
    if let Some(m) = v["assets"].as_object() {
        for (k, a) in m {
            let sha = a["sha256"].as_str().unwrap_or("").to_lowercase();
            if sha.len() != 64 || !sha.chars().all(|c| c.is_ascii_hexdigit()) { continue; }
            let url = a["url"].as_str().unwrap_or("");
            if https_only(url).is_empty() { continue; }
            assets.push((k.clone(), Asset { url: url.into(), sha256: sha, size: a["size"].as_u64().unwrap_or(0) }));
        }
    }
    Ok(Manifest {
        version, version_text, channel: ch.into(), notes_url: https_only(v["notes_url"].as_str().unwrap_or("")), assets,
        published: v["published"].as_u64().unwrap_or(0), expires: v["expires"].as_u64().unwrap_or(0),
    })
}

// ---------------- state ----------------

#[derive(Debug, Clone, PartialEq)]
pub enum Status {
    Idle,
    /// No feed or key in this build.
    Off,
    Checking,
    UpToDate,
    Downloading(u8),
    /// Downloaded and verified, waiting for the user.
    Ready,
    /// Newer version for an install that can't replace itself.
    Available,
    /// A beta runs and the stable channel's newest is older.
    Older,
    Error(String),
}

#[derive(Debug, Clone)]
pub struct State {
    pub status: Status,
    pub target: String,
    pub notes_url: String,
    /// Bumped on every change, so the UI knows when to redraw.
    pub rev: u64,
}

static STATE: Mutex<State> = Mutex::new(State { status: Status::Idle, target: String::new(), notes_url: String::new(), rev: 0 });
static BUSY: Mutex<bool> = Mutex::new(false);

pub fn state() -> State { STATE.lock().unwrap().clone() }

fn set(status: Status, target: Option<&str>, notes: Option<&str>) {
    let mut s = STATE.lock().unwrap();
    s.status = status;
    if let Some(t) = target { s.target = t.into(); }
    if let Some(n) = notes { s.notes_url = n.into(); }
    s.rev += 1;
}

/// Feed address and key: built in, or (automation runs only) from the environment.
pub fn config() -> Option<(String, String)> {
    let env = |k: &str| crate::automation::enabled().then(|| std::env::var(k).ok()).flatten().filter(|v| !v.is_empty());
    let feed = env("BARBICAN_UPDATE_FEED_OVERRIDE").or(BUILT_FEED.map(String::from)).filter(|f| !f.is_empty())?;
    let key = env("BARBICAN_UPDATE_PUBKEY_OVERRIDE").or(BUILT_PUBKEY.map(String::from)).filter(|k| !k.is_empty())?;
    Some((feed, key))
}

/// Downloads and pending.json. Automation runs may point it elsewhere, so a test never
/// leaves an update for the user's own Barbican.
pub fn updates_dir() -> PathBuf {
    if crate::automation::enabled() {
        if let Some(d) = std::env::var_os("BARBICAN_UPDATES_DIR").filter(|d| !d.is_empty()) { return PathBuf::from(d); }
    }
    crate::platform::data_dir().join("updates")
}

pub fn channel() -> &'static str {
    if crate::settings::Settings::load().int("desktop.updateChannel", 0) == 1 { "beta" } else { "stable" }
}

pub fn auto_check() -> bool { crate::settings::Settings::load().bool("desktop.autoUpdate", true) }

pub fn feed_url(feed: &str, channel: &str) -> String {
    if feed.contains("{channel}") { feed.replace("{channel}", channel) } else { format!("{}/{channel}.json", feed.trim_end_matches('/')) }
}

/// The page "Download" opens: the manifest's release notes, else the releases page.
pub fn download_page() -> String {
    let s = state();
    if !s.notes_url.is_empty() { return s.notes_url; }
    BUILT_RELEASES.unwrap_or("").to_string()
}

fn http() -> Result<reqwest::blocking::Client, String> {
    reqwest::blocking::Client::builder().user_agent(format!("Barbican/{VERSION}")).timeout(std::time::Duration::from_secs(60))
        .build().map_err(|e| e.to_string())
}

fn fetch(c: &reqwest::blocking::Client, url: &str) -> Result<Vec<u8>, String> {
    let r = c.get(url).send().map_err(|e| e.to_string())?;
    if !r.status().is_success() { return Err(format!("HTTP {}", r.status().as_u16())); }
    r.bytes().map(|b| b.to_vec()).map_err(|e| e.to_string())
}

/// What a manifest means for this install.
#[derive(Debug, PartialEq)]
pub enum Decision { UpToDate, Older, Install, Notify }

pub fn decide(current: &Version, latest: &Version, channel: &str, kind: Kind) -> Decision {
    if latest > current { return if kind.can_install() { Decision::Install } else { Decision::Notify }; }
    if latest < current && channel == "stable" && current.is_pre() { return Decision::Older; }
    Decision::UpToDate
}

/// Check the feed (background thread), then download when this install can replace itself.
pub fn check(manual: bool) {
    {
        let mut b = BUSY.lock().unwrap();
        if *b { return; }
        *b = true;
    }
    std::thread::spawn(move || {
        run_check(manual);
        *BUSY.lock().unwrap() = false;
    });
}

fn run_check(_manual: bool) {
    let Some((feed, key)) = config() else { set(Status::Off, None, None); return };
    // a verified download already waits: nothing to do
    if let Some(p) = Pending::load() {
        if Version::parse(&p.version).is_some_and(|v| v > Version::parse(VERSION).unwrap()) && Path::new(&p.path).exists() {
            set(Status::Ready, Some(&p.version), None);
            return;
        }
    }
    set(Status::Checking, None, None);
    let ch = channel();
    let res = (|| -> Result<(), String> {
        let c = http()?;
        let url = feed_url(&feed, ch);
        let body = fetch(&c, &url)?;
        let sig = String::from_utf8(fetch(&c, &format!("{url}.sig"))?).map_err(|_| "bad signature")?;
        let m = verify_manifest(&body, &sig, &key, ch)?;
        let sf = seen_file(ch);
        check_fresh(&m, now_secs(), load_seen(&sf).as_ref())?;
        save_seen(&sf, &Seen { version: m.version_text.clone(), published: m.published });
        let kind = current_kind();
        let cur = Version::parse(VERSION).ok_or("bad own version")?;
        match decide(&cur, &m.version, ch, kind) {
            Decision::UpToDate => { set(Status::UpToDate, Some(&m.version_text), Some(&m.notes_url)); }
            Decision::Older => { set(Status::Older, Some(&m.version_text), Some(&m.notes_url)); }
            Decision::Notify => { set(Status::Available, Some(&m.version_text), Some(&m.notes_url)); }
            Decision::Install => {
                let asset = m.assets.iter().find(|(k, _)| k == kind.asset()).map(|(_, a)| a.clone())
                    .ok_or("This release has no package for this computer.")?;
                set(Status::Downloading(0), Some(&m.version_text), Some(&m.notes_url));
                let dir = updates_dir();
                let path = download(&c, &asset, &dir, &m.version_text, kind, |p| set(Status::Downloading(p), None, None))?;
                Pending { version: m.version_text.clone(), path: path.display().to_string(), sha256: asset.sha256.clone(), kind: format!("{kind:?}"), approved: false }.save()?;
                set(Status::Ready, Some(&m.version_text), None);
            }
        }
        Ok(())
    })();
    if let Err(e) = res { set(Status::Error(e), None, None); }
}

fn file_name(url: &str, version: &str, kind: Kind) -> String {
    let last = url.rsplit('/').next().unwrap_or("").split('?').next().unwrap_or("");
    let safe: String = last.chars().filter(|c| c.is_ascii_alphanumeric() || matches!(c, '.' | '-' | '_')).collect();
    if safe.is_empty() || safe.starts_with('.') {
        format!("barbican-{version}.{}", match kind { Kind::Inno => "exe", Kind::Msi => "msi", Kind::AppImage => "AppImage", Kind::Deb => "deb", _ => "zip" })
    } else { safe }
}

/// Stream the asset to `dir/<version>/<file>`, hashing as it goes; a wrong hash or size
/// deletes it. Returns the verified file.
pub fn download(c: &reqwest::blocking::Client, a: &Asset, dir: &Path, version: &str, kind: Kind, progress: impl Fn(u8)) -> Result<PathBuf, String> {
    use sha2::Digest;
    use std::io::{Read, Write};
    let folder = dir.join(version.replace(['/', '\\'], "_"));
    std::fs::create_dir_all(&folder).map_err(|e| e.to_string())?;
    let path = folder.join(file_name(&a.url, version, kind));
    let part = path.with_extension("part");
    let mut r = c.get(&a.url).timeout(std::time::Duration::from_secs(30 * 60)).send().map_err(|e| e.to_string())?;
    if !r.status().is_success() { return Err(format!("HTTP {}", r.status().as_u16())); }
    let total = r.content_length().unwrap_or(a.size).max(1);
    let mut f = std::fs::File::create(&part).map_err(|e| e.to_string())?;
    let mut h = sha2::Sha256::new();
    let (mut got, mut last) = (0u64, 255u8);
    let mut buf = vec![0u8; 64 * 1024];
    loop {
        let n = match r.read(&mut buf) {
            Ok(n) => n,
            Err(e) => { drop(f); let _ = std::fs::remove_file(&part); return Err(e.to_string()); }
        };
        if n == 0 { break; }
        h.update(&buf[..n]);
        f.write_all(&buf[..n]).map_err(|e| e.to_string())?;
        got += n as u64;
        if a.size > 0 && got > a.size { break; }
        let p = (got * 100 / total).min(100) as u8;
        if p != last { last = p; progress(p); }
    }
    drop(f);
    let hex: String = h.finalize().iter().map(|b| format!("{b:02x}")).collect();
    if hex != a.sha256 || (a.size > 0 && got != a.size) {
        let _ = std::fs::remove_file(&part);
        return Err("The download does not match the signed manifest.".into());
    }
    std::fs::rename(&part, &path).map_err(|e| e.to_string())?;
    Ok(path)
}

pub fn sha256_file(p: &Path) -> Option<String> {
    use sha2::Digest;
    use std::io::Read;
    let mut f = std::fs::File::open(p).ok()?;
    let mut h = sha2::Sha256::new();
    let mut buf = vec![0u8; 64 * 1024];
    loop {
        let n = f.read(&mut buf).ok()?;
        if n == 0 { break; }
        h.update(&buf[..n]);
    }
    Some(h.finalize().iter().map(|b| format!("{b:02x}")).collect())
}

// ---------------- pending install ----------------

/// A verified download waiting for the user's OK (updates/pending.json).
#[derive(Debug, Clone, PartialEq)]
pub struct Pending {
    pub version: String,
    pub path: String,
    pub sha256: String,
    pub kind: String,
    /// "Install next time I start": set by the user only.
    pub approved: bool,
}

impl Pending {
    fn file() -> PathBuf { updates_dir().join("pending.json") }

    pub fn load() -> Option<Pending> { Self::load_from(&Self::file()) }

    pub fn load_from(p: &Path) -> Option<Pending> {
        let v: Value = serde_json::from_str(&std::fs::read_to_string(p).ok()?).ok()?;
        Some(Pending {
            version: v["version"].as_str()?.into(), path: v["path"].as_str()?.into(), sha256: v["sha256"].as_str()?.into(),
            kind: v["kind"].as_str().unwrap_or("").into(), approved: v["approved"].as_bool().unwrap_or(false),
        })
    }

    pub fn save(&self) -> Result<(), String> { self.save_to(&Self::file()) }

    pub fn save_to(&self, p: &Path) -> Result<(), String> {
        if let Some(d) = p.parent() { std::fs::create_dir_all(d).map_err(|e| e.to_string())?; }
        std::fs::write(p, json!({"version": self.version, "path": self.path, "sha256": self.sha256, "kind": self.kind, "approved": self.approved}).to_string())
            .map_err(|e| e.to_string())
    }

    pub fn clear() {
        let _ = std::fs::remove_file(Self::file());
    }
}

/// What starting Barbican should do with a pending update.
#[derive(Debug, PartialEq)]
pub enum StartAction {
    Nothing,
    /// Leftover (installed already, older, missing or altered): delete it.
    Discard,
    /// Approved and still verified: install it.
    Install,
}

pub fn start_action(p: Option<&Pending>, current: &str, hash_now: impl Fn(&Path) -> Option<String>) -> StartAction {
    let Some(p) = p else { return StartAction::Nothing };
    let (Some(v), Some(cur)) = (Version::parse(&p.version), Version::parse(current)) else { return StartAction::Discard };
    if v <= cur { return StartAction::Discard; }
    if hash_now(Path::new(&p.path)).as_deref() != Some(p.sha256.as_str()) { return StartAction::Discard; }
    if p.approved { StartAction::Install } else { StartAction::Nothing }
}

/// The user chose "Install next time I start".
pub fn approve_next_start() -> Result<(), String> {
    let mut p = Pending::load().ok_or("No update is waiting.")?;
    p.approved = true;
    p.save()
}

/// Called first thing in main(): an approved update installs now and Barbican exits (the
/// installer starts it again). Returns true when the caller should exit.
pub fn on_start() -> bool {
    let p = Pending::load();
    match start_action(p.as_ref(), VERSION, sha256_file) {
        StartAction::Nothing => {
            if let Some(p) = p { set(Status::Ready, Some(&p.version), None); }
            false
        }
        StartAction::Discard => {
            let _ = std::fs::remove_dir_all(updates_dir());
            false
        }
        StartAction::Install => {
            let p = p.unwrap();
            Pending::clear();
            match install(&p) { Ok(()) => true, Err(e) => { set(Status::Error(e), None, None); false } }
        }
    }
}

/// "Restart now": install the verified download and quit.
pub fn install_now() -> Result<(), String> {
    let p = Pending::load().ok_or("No update is waiting.")?;
    if start_action(Some(&p), VERSION, sha256_file) == StartAction::Discard {
        Pending::clear();
        return Err("The downloaded update is gone or was changed. Check again.".into());
    }
    Pending::clear();
    install(&p)
}

/// The installer helper: waits until this Barbican (`BARBICAN_PID`) has exited so no file is
/// in use, runs msiexec on `BARBICAN_MSI`, then starts `BARBICAN_EXE` again (the new one, or
/// the old one when the install failed or was cancelled; a reboot-required result 3010 also
/// relaunches). It never ends the app itself: Barbican writes the open draft on quit (up to 15 s,
/// `quit_saving_draft`) and a kill would lose it, so when the process is still there after 120 s
/// the helper exits 1, without installing and without a relaunch. The app keeps running; nothing
/// is pending by then (`install_now` and `on_start` clear it before starting this helper), so the
/// next check downloads the update again. The paths come in as environment variables, never
/// spliced into the script, so nothing in them is run as a command.
pub const MSI_SCRIPT: &str = "$ErrorActionPreference='SilentlyContinue'; \
if (Get-Process -Id ([int]$env:BARBICAN_PID) -ErrorAction SilentlyContinue) { \
Wait-Process -Id ([int]$env:BARBICAN_PID) -Timeout 120 -ErrorAction SilentlyContinue; \
if (Get-Process -Id ([int]$env:BARBICAN_PID) -ErrorAction SilentlyContinue) { exit 1 } }; \
$p = Start-Process -FilePath msiexec.exe -ArgumentList @('/i', ('\"' + $env:BARBICAN_MSI + '\"'), '/passive', '/norestart') -Wait -PassThru; \
Start-Process -FilePath $env:BARBICAN_EXE";

/// The same helper for the Inno setup (packaging/inno/barbican.iss): waits for this Barbican to
/// exit, runs `BARBICAN_SETUP` silently (no window, no restart; /CLOSEAPPLICATIONS for any other
/// copy of the program still open), then starts `BARBICAN_EXE` again, the new program or the old
/// one when the setup failed. It never ends the app either: still running after 120 s means exit 1
/// and no install, so the draft Barbican is saving is not lost; the pending download is already
/// cleared, so the next check fetches the update again. The setup's own [Run] entry is skipped
/// when silent, so Barbican starts once. No path is spliced into the script: the arguments are
/// fixed, the paths are environment variables.
pub const INNO_SCRIPT: &str = "$ErrorActionPreference='SilentlyContinue'; \
if (Get-Process -Id ([int]$env:BARBICAN_PID) -ErrorAction SilentlyContinue) { \
Wait-Process -Id ([int]$env:BARBICAN_PID) -Timeout 120 -ErrorAction SilentlyContinue; \
if (Get-Process -Id ([int]$env:BARBICAN_PID) -ErrorAction SilentlyContinue) { exit 1 } }; \
$p = Start-Process -FilePath $env:BARBICAN_SETUP -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CLOSEAPPLICATIONS') -Wait -PassThru; \
Start-Process -FilePath $env:BARBICAN_EXE";

/// Program, arguments and environment of the helper (built here so it can be tested).
pub fn msi_command(msi: &str, exe: &str, pid: u32) -> (String, Vec<String>, Vec<(String, String)>) {
    helper_command(MSI_SCRIPT, "BARBICAN_MSI", msi, exe, pid)
}

pub fn inno_command(setup: &str, exe: &str, pid: u32) -> (String, Vec<String>, Vec<(String, String)>) {
    helper_command(INNO_SCRIPT, "BARBICAN_SETUP", setup, exe, pid)
}

fn helper_command(script: &str, var: &str, package: &str, exe: &str, pid: u32) -> (String, Vec<String>, Vec<(String, String)>) {
    (powershell_exe(),
     ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-Command", script].map(String::from).to_vec(),
     vec![(var.into(), package.into()), ("BARBICAN_EXE".into(), exe.into()), ("BARBICAN_PID".into(), pid.to_string())])
}

/// Windows PowerShell by full path: `%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe`.
/// `%SystemRoot%` is expanded here because `Command::new` does not expand environment variables;
/// an install without that file (or without the variable) falls back to a PATH lookup.
pub fn powershell_exe() -> String {
    let root = std::env::var("SystemRoot").unwrap_or_else(|_| r"C:\Windows".into());
    let p = format!(r"{root}\System32\WindowsPowerShell\v1.0\powershell.exe");
    if Path::new(&p).is_file() { p } else { "powershell.exe".into() }
}

/// pending.json is not signed, so the file it names is only run when it lies inside the
/// updates folder, has the package's extension and no quote or shell character.
pub fn pending_path_ok(path: &Path, dir: &Path, kind: &str) -> bool {
    let ext = match kind { "Inno" => "exe", "Msi" => "msi", "AppImage" => "AppImage", _ => return false };
    let text = path.to_string_lossy();
    if text.chars().any(|c| matches!(c, '"' | '%' | '&' | '|' | '<' | '>' | '^' | '`' | '$' | ';') || c.is_control()) { return false; }
    let (Ok(p), Ok(d)) = (path.canonicalize(), dir.canonicalize()) else { return false };
    p.starts_with(&d) && p.extension().is_some_and(|e| e == ext) && p.is_file()
}

fn install(p: &Pending) -> Result<(), String> {
    if !pending_path_ok(Path::new(&p.path), &updates_dir(), &p.kind) {
        return Err("The downloaded update is not where Barbican put it. Check again.".into());
    }
    match p.kind.as_str() {
        "Inno" | "Msi" => {
            let exe = std::env::current_exe().map_err(|e| e.to_string())?;
            let build = if p.kind == "Inno" { inno_command } else { msi_command };
            let (prog, args, env) = build(&p.path, &exe.display().to_string(), std::process::id());
            let mut cmd = std::process::Command::new(prog);
            cmd.args(args).envs(env);
            #[cfg(windows)]
            { use std::os::windows::process::CommandExt; cmd.creation_flags(0x0800_0000); }
            cmd.spawn().map(|_| ()).map_err(|e| e.to_string())
        }
        "AppImage" => {
            let target = std::env::var("APPIMAGE").map_err(|_| "Not running from an AppImage.")?;
            replace_file(Path::new(&p.path), Path::new(&target))?;
            std::process::Command::new(&target).spawn().map(|_| ()).map_err(|e| e.to_string())
        }
        k => Err(format!("{k} installs can't update themselves.")),
    }
}

/// Copy next to the target, make it executable, then rename over it (atomic on one disk).
pub fn replace_file(new: &Path, target: &Path) -> Result<(), String> {
    let tmp = target.with_extension("update-tmp");
    std::fs::copy(new, &tmp).map_err(|e| e.to_string())?;
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        std::fs::set_permissions(&tmp, std::fs::Permissions::from_mode(0o755)).map_err(|e| e.to_string())?;
    }
    std::fs::rename(&tmp, target).map_err(|e| { let _ = std::fs::remove_file(&tmp); e.to_string() })
}

/// The status line (Bulwark desktop_updates.status_*).
pub fn status_text(s: &State, kind: Kind) -> String {
    let v = &s.target;
    match &s.status {
        Status::Idle => "Barbican checks for updates by itself.".into(),
        Status::Off => "Updates are not set up in this build.".into(),
        Status::Checking => "Checking for updates…".into(),
        Status::UpToDate => "You have the latest version.".into(),
        Status::Downloading(p) => format!("Downloading version {v}… {p}%"),
        Status::Ready => format!("Version {v} is ready. It installs when you restart Barbican, or restart now."),
        Status::Available => match kind {
            Kind::Deb => format!("Version {v} is available. Your package manager installs it."),
            Kind::Dev => format!("Version {v} is available. This development build does not update itself."),
            _ => format!("Version {v} is available."),
        },
        Status::Older => format!("You have a beta. Stable {v} is older, so Barbican stays on this version until stable catches up."),
        Status::Error(_) => "Couldn't check for updates. Barbican tries again later.".into(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use base64::Engine;
    use ed25519_dalek::{Signer, SigningKey};

    fn v(s: &str) -> Version { Version::parse(s).unwrap() }

    #[test]
    fn versions() {
        assert!(v("1.2.0") > v("1.1.9"));
        assert!(v("1.2.0") > v("1.2.0-beta.9"));
        assert!(v("1.2.0-beta.10") > v("1.2.0-beta.9"));
        assert!(v("1.2.0-beta.2") > v("1.2.0-alpha.5"));
        assert!(v("1.2.0-rc") > v("1.2.0-beta.1"));
        assert!(v("1.2.0-beta.1.1") > v("1.2.0-beta.1"));
        assert_eq!(v("v0.3.1+build.5"), v("0.3.1"));
        assert!(Version::parse("1.2").is_none());
        assert!(Version::parse("1.2.3.4").is_none());
        assert!(Version::parse("1.2.x").is_none());
        assert!(Version::parse("1.2.3-").is_none());
    }

    #[test]
    fn decisions() {
        assert_eq!(decide(&v("1.0.0"), &v("1.1.0"), "stable", Kind::Msi), Decision::Install);
        assert_eq!(decide(&v("1.0.0"), &v("1.1.0"), "stable", Kind::Inno), Decision::Install);
        assert_eq!(decide(&v("1.0.0"), &v("1.1.0"), "stable", Kind::Deb), Decision::Notify);
        assert_eq!(decide(&v("1.0.0"), &v("1.1.0"), "stable", Kind::Zip), Decision::Notify);
        assert_eq!(decide(&v("1.0.0"), &v("1.0.0"), "stable", Kind::Msi), Decision::UpToDate);
        // never downgrades by itself
        assert_eq!(decide(&v("1.1.0"), &v("1.0.0"), "stable", Kind::Msi), Decision::UpToDate);
        assert_eq!(decide(&v("1.2.0-beta.1"), &v("1.1.0"), "stable", Kind::AppImage), Decision::Older);
        assert_eq!(decide(&v("1.2.0-beta.1"), &v("1.2.0"), "stable", Kind::AppImage), Decision::Install);
    }

    #[test]
    fn kinds() {
        assert_eq!(detect_kind(Path::new("/tmp/.mount_x/usr/bin/barbican"), Some("/home/a/Barbican.AppImage"), None), Kind::AppImage);
        assert_eq!(detect_kind(Path::new("/usr/bin/barbican"), None, None), Kind::Deb);
        assert_eq!(detect_kind(Path::new(r"C:\Users\a\AppData\Local\Programs\Barbican\barbican.exe"), None, Some("msi\n")), Kind::Msi);
        assert_eq!(detect_kind(Path::new(r"D:\tools\barbican\barbican.exe"), None, Some("zip")), Kind::Zip);
        assert_eq!(detect_kind(Path::new(r"C:\src\target\release\barbican.exe"), None, None), Kind::Dev);
        assert_eq!(detect_kind(Path::new(r"C:\Users\a\AppData\Local\Programs\Barbican\barbican.exe"), None, Some("inno")), Kind::Inno);
        assert_eq!(Kind::Inno.asset(), "windows-x86_64-inno");
        assert!(Kind::Inno.can_install());
        assert!(Kind::Msi.can_install() && Kind::AppImage.can_install() && !Kind::Deb.can_install() && !Kind::Zip.can_install());
    }

    fn keys() -> (SigningKey, String) {
        let sk = SigningKey::from_bytes(&[7u8; 32]);
        let pk = base64::engine::general_purpose::STANDARD.encode(sk.verifying_key().to_bytes());
        (sk, pk)
    }

    fn signed(sk: &SigningKey, body: &str) -> String {
        base64::engine::general_purpose::STANDARD.encode(sk.sign(body.as_bytes()).to_bytes())
    }

    const SHA: &str = "a3f1c5b6e8d9a0b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e6f7a8b9c0d1e2f3a4b5";

    fn manifest(channel: &str) -> String {
        json!({"version": "2.0.0", "channel": channel, "notes_url": "https://example.org/r/v2.0.0", "published": 100, "expires": 200, "assets": {
            "windows-x86_64-msi": {"url": "https://example.org/b.msi", "sha256": SHA, "size": 10},
            "windows-x86_64-inno": {"url": "https://example.org/Barbican-Setup-2.0.0.exe", "sha256": SHA, "size": 10},
            "linux-x86_64-appimage": {"url": "ftp://example.org/b.AppImage", "sha256": SHA, "size": 10},
            "linux-x86_64-deb": {"url": "https://example.org/b.deb", "sha256": "nothex", "size": 10}}}).to_string()
    }

    #[test]
    fn signed_manifest() {
        let (sk, pk) = keys();
        let body = manifest("stable");
        let m = verify_manifest(body.as_bytes(), &signed(&sk, &body), &pk, "stable").unwrap();
        assert_eq!(m.version, v("2.0.0"));
        assert_eq!(m.notes_url, "https://example.org/r/v2.0.0");
        assert_eq!((m.published, m.expires), (100, 200));
        // links that are not https are dropped, even when signed
        for bad in ["file:///C:/Windows/system32/calc.exe", "ms-settings:", "http://127.0.0.1/x", "https://a b"] {
            let body = manifest("stable").replace("https://example.org/r/v2.0.0", bad);
            assert_eq!(verify_manifest(body.as_bytes(), &signed(&sk, &body), &pk, "stable").unwrap().notes_url, "");
        }
        let body = manifest("stable").replace("https://example.org/b.msi", "http://127.0.0.1:9/b.msi");
        let kept = verify_manifest(body.as_bytes(), &signed(&sk, &body), &pk, "stable").unwrap().assets;
        assert_eq!(kept.iter().map(|(k, _)| k.as_str()).collect::<Vec<_>>(), ["windows-x86_64-inno"]);
        // only https assets with a real hash are kept
        assert_eq!(m.assets.len(), 2);
        assert_eq!(m.assets[0].0, "windows-x86_64-msi");
        assert_eq!(m.assets[1].0, "windows-x86_64-inno");
        // one changed byte, another key, another channel, garbage: all refused
        let tampered = body.replace("2.0.0", "9.0.0");
        assert!(verify_manifest(tampered.as_bytes(), &signed(&sk, &body), &pk, "stable").is_err());
        let other = SigningKey::from_bytes(&[8u8; 32]);
        assert!(verify_manifest(body.as_bytes(), &signed(&other, &body), &pk, "stable").is_err());
        assert!(verify_manifest(body.as_bytes(), &signed(&sk, &body), &pk, "beta").is_err());
        assert!(verify_manifest(body.as_bytes(), "AAAA", &pk, "stable").is_err());
        assert!(verify_manifest(body.as_bytes(), &signed(&sk, &body), "not a key", "stable").is_err());
    }

    #[test]
    fn feed_urls() {
        assert_eq!(feed_url("https://x/releases/download/update-feed/{channel}.json", "beta"), "https://x/releases/download/update-feed/beta.json");
        assert_eq!(feed_url("https://x/feed/", "stable"), "https://x/feed/stable.json");
    }

    #[test]
    fn start_actions() {
        let p = Pending { version: "9.0.0".into(), path: "x".into(), sha256: "abc".into(), kind: "Msi".into(), approved: false };
        let good = |_: &Path| Some("abc".to_string());
        assert_eq!(start_action(None, "1.0.0", good), StartAction::Nothing);
        // waits for the user's OK
        assert_eq!(start_action(Some(&p), "1.0.0", good), StartAction::Nothing);
        let ok = Pending { approved: true, ..p.clone() };
        assert_eq!(start_action(Some(&ok), "1.0.0", good), StartAction::Install);
        // changed on disk, already installed, or gone
        assert_eq!(start_action(Some(&ok), "1.0.0", |_| Some("zzz".into())), StartAction::Discard);
        assert_eq!(start_action(Some(&ok), "9.0.0", good), StartAction::Discard);
        assert_eq!(start_action(Some(&ok), "1.0.0", |_| None), StartAction::Discard);
    }

    #[test]
    fn pending_round_trip_and_replace() {
        let dir = std::env::temp_dir().join(format!("barbican-upd-test-{}", std::process::id()));
        let f = dir.join("pending.json");
        let p = Pending { version: "1.2.3".into(), path: "C:/x/y.msi".into(), sha256: SHA.into(), kind: "Msi".into(), approved: true };
        p.save_to(&f).unwrap();
        assert_eq!(Pending::load_from(&f), Some(p));
        let (a, b) = (dir.join("new.bin"), dir.join("App.AppImage"));
        std::fs::write(&a, b"new").unwrap();
        std::fs::write(&b, b"old").unwrap();
        replace_file(&a, &b).unwrap();
        assert_eq!(std::fs::read(&b).unwrap(), b"new");
        assert!(!b.with_extension("update-tmp").exists());
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn msi_cmd() {
        // paths with spaces and quotes stay data: they only travel as environment values
        let (prog, args, env) = msi_command(r#"C:\u s\b".msi"#, r"C:\p q\barbican.exe", 42);
        assert!(prog.ends_with(r"\WindowsPowerShell\v1.0\powershell.exe") || prog == "powershell.exe", "{prog}");
        assert_eq!(args.last().unwrap(), MSI_SCRIPT);
        assert!(!args.iter().any(|a| a.contains("b\".msi") || a.contains("p q")));
        assert_eq!(env, vec![("BARBICAN_MSI".to_string(), r#"C:\u s\b".msi"#.to_string()),
            ("BARBICAN_EXE".to_string(), r"C:\p q\barbican.exe".to_string()), ("BARBICAN_PID".to_string(), "42".to_string())]);
        // waits for this process before installing, relaunches afterwards
        assert!(MSI_SCRIPT.find("Wait-Process").unwrap() < MSI_SCRIPT.find("msiexec").unwrap());
        assert!(MSI_SCRIPT.rfind("BARBICAN_EXE").unwrap() > MSI_SCRIPT.find("msiexec").unwrap());
        // a process that outlives the wait stops the helper before msiexec: nothing is killed
        assert!(MSI_SCRIPT.find("exit 1").unwrap() < MSI_SCRIPT.find("msiexec").unwrap());
        assert!(!MSI_SCRIPT.contains("Stop-Process") && !MSI_SCRIPT.contains("-Force"));
    }

    #[test]
    fn inno_cmd() {
        let (prog, args, env) = inno_command(r#"C:\u s\b".exe"#, r"C:\p q\barbican.exe", 42);
        assert!(prog.ends_with(r"\WindowsPowerShell\v1.0\powershell.exe") || prog == "powershell.exe", "{prog}");
        assert_eq!(args.last().unwrap(), INNO_SCRIPT);
        assert!(!args.iter().any(|a| a.contains("b\".exe") || a.contains("p q")));
        assert_eq!(env, vec![("BARBICAN_SETUP".to_string(), r#"C:\u s\b".exe"#.to_string()),
            ("BARBICAN_EXE".to_string(), r"C:\p q\barbican.exe".to_string()), ("BARBICAN_PID".to_string(), "42".to_string())]);
        // waits for this process, then runs the setup silently, then relaunches
        let (wait, run, relaunch) = (INNO_SCRIPT.find("Wait-Process").unwrap(), INNO_SCRIPT.find("BARBICAN_SETUP").unwrap(), INNO_SCRIPT.rfind("BARBICAN_EXE").unwrap());
        assert!(wait < run && run < relaunch);
        for flag in ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS"] { assert!(INNO_SCRIPT.contains(flag), "{flag}"); }
        // the arguments are fixed text: no environment value is spliced into them
        assert!(!INNO_SCRIPT.contains("ArgumentList $env"));
        // Barbican saving its draft is never cut short: still running after the wait, the helper
        // gives up before the setup runs, and the app is left running
        assert!(INNO_SCRIPT.find("exit 1").unwrap() < run);
        assert!(!INNO_SCRIPT.contains("Stop-Process") && !INNO_SCRIPT.contains("-Force"));
    }

    #[test]
    fn pending_paths() {
        let dir = std::env::temp_dir().join(format!("barbican-pp-test-{}", std::process::id()));
        std::fs::create_dir_all(dir.join("2.0.0")).unwrap();
        let good = dir.join("2.0.0").join("Barbican 2.0.0.msi");
        std::fs::write(&good, b"x").unwrap();
        std::fs::write(dir.join("evil.exe"), b"x").unwrap();
        assert!(pending_path_ok(&good, &dir, "Msi"));
        assert!(!pending_path_ok(&good, &dir, "Zip"));
        assert!(!pending_path_ok(&good, &dir, "AppImage"));
        // each kind only runs its own package type
        let setup = dir.join("2.0.0").join("Barbican-Setup-2.0.0.exe");
        std::fs::write(&setup, b"x").unwrap();
        assert!(pending_path_ok(&setup, &dir, "Inno"));
        assert!(!pending_path_ok(&setup, &dir, "Msi"));
        assert!(!pending_path_ok(&good, &dir, "Inno"));
        assert!(!pending_path_ok(&dir.join("evil.exe"), &dir, "Msi"));
        assert!(!pending_path_ok(&dir.join("missing.msi"), &dir, "Msi"));
        assert!(!pending_path_ok(Path::new(r#"C:\x" & calc & ".msi"#), &dir, "Msi"));
        let outside = std::env::temp_dir().join(format!("barbican-pp-out-{}.msi", std::process::id()));
        std::fs::write(&outside, b"x").unwrap();
        assert!(!pending_path_ok(&outside, &dir, "Msi"));
        assert!(!pending_path_ok(&dir.join("2.0.0").join("..").join("..").join(outside.file_name().unwrap()), &dir, "Msi"));
        let _ = std::fs::remove_file(&outside);
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn freshness() {
        let m = |ver: &str, published: u64, expires: u64| Manifest { version: v(ver), version_text: ver.into(), channel: "stable".into(),
            notes_url: String::new(), assets: vec![], published, expires };
        assert!(check_fresh(&m("2.0.0", 100, 1000), 500, None).is_ok());
        assert!(check_fresh(&m("2.0.0", 100, 1000), 1001, None).is_err());
        assert!(check_fresh(&m("2.0.0", 100, 0), 500, None).is_err());
        let seen = Seen { version: "2.1.0".into(), published: 200 };
        // an older but validly signed manifest is a replay
        assert!(check_fresh(&m("2.0.0", 300, 1000), 500, Some(&seen)).is_err());
        assert!(check_fresh(&m("2.1.0", 150, 1000), 500, Some(&seen)).is_err());
        assert!(check_fresh(&m("2.1.0", 200, 1000), 500, Some(&seen)).is_ok());
        assert!(check_fresh(&m("2.2.0", 250, 1000), 500, Some(&seen)).is_ok());
        let f = std::env::temp_dir().join(format!("barbican-seen-{}.json", std::process::id()));
        save_seen(&f, &seen);
        assert_eq!(load_seen(&f), Some(seen));
        let _ = std::fs::remove_file(&f);
    }

    #[test]
    fn names() {
        assert_eq!(file_name("https://x/a/Barbican-1.2.0-x86_64.AppImage?x=1", "1.2.0", Kind::AppImage), "Barbican-1.2.0-x86_64.AppImage");
        assert_eq!(file_name("https://x/a/", "1.2.0", Kind::Msi), "barbican-1.2.0.msi");
        assert_eq!(file_name("https://x/a/", "1.2.0", Kind::Inno), "barbican-1.2.0.exe");
        assert_eq!(file_name("https://x/a/Barbican-Setup-1.2.0.exe", "1.2.0", Kind::Inno), "Barbican-Setup-1.2.0.exe");
        assert_eq!(file_name("https://x/../..", "1.2.0", Kind::Msi), "barbican-1.2.0.msi");
    }

    /// The whole path over HTTP: a tiny local server hands out the asset; a good hash is
    /// kept, a bad one is deleted.
    #[test]
    fn download_verifies_hash() {
        use sha2::Digest;
        use std::io::{Read, Write};
        let payload = b"barbican update payload".to_vec();
        let sha: String = sha2::Sha256::digest(&payload).iter().map(|b| format!("{b:02x}")).collect();
        let l = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
        let port = l.local_addr().unwrap().port();
        let p2 = payload.clone();
        std::thread::spawn(move || {
            for s in l.incoming().flatten().take(2) {
                let mut s = s;
                let mut buf = [0u8; 2048];
                let _ = s.read(&mut buf);
                let _ = write!(s, "HTTP/1.1 200 OK\r\nContent-Length: {}\r\nConnection: close\r\n\r\n", p2.len());
                let _ = s.write_all(&p2);
            }
        });
        let dir = std::env::temp_dir().join(format!("barbican-dl-test-{}", std::process::id()));
        let c = http().unwrap();
        let url = format!("http://127.0.0.1:{port}/Barbican.msi");
        let got = download(&c, &Asset { url: url.clone(), sha256: sha, size: payload.len() as u64 }, &dir, "2.0.0", Kind::Msi, |_| {}).unwrap();
        assert_eq!(std::fs::read(&got).unwrap(), payload);
        assert!(download(&c, &Asset { url, sha256: SHA.into(), size: payload.len() as u64 }, &dir.join("bad"), "2.0.0", Kind::Msi, |_| {}).is_err());
        assert!(!dir.join("bad").join("2.0.0").join("Barbican.part").exists());
        let _ = std::fs::remove_dir_all(&dir);
    }

    /// A server that sends more than the manifest's size, or closes early: both refused,
    /// nothing left behind.
    #[test]
    fn download_size_limits() {
        use sha2::Digest;
        use std::io::{Read, Write};
        let payload = vec![b'z'; 100_000];
        let sha: String = sha2::Sha256::digest(&payload).iter().map(|b| format!("{b:02x}")).collect();
        let l = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
        let port = l.local_addr().unwrap().port();
        let p2 = payload.clone();
        std::thread::spawn(move || {
            for (i, s) in l.incoming().flatten().take(2).enumerate() {
                let mut s = s;
                let mut buf = [0u8; 2048];
                let _ = s.read(&mut buf);
                if i == 0 {
                    // twice the promised size, no length header
                    let _ = write!(s, "HTTP/1.1 200 OK\r\nConnection: close\r\n\r\n");
                    let _ = s.write_all(&p2);
                    let _ = s.write_all(&p2);
                } else {
                    // announced in full, cut short
                    let _ = write!(s, "HTTP/1.1 200 OK\r\nContent-Length: {}\r\nConnection: close\r\n\r\n", p2.len());
                    let _ = s.write_all(&p2[..1000]);
                }
            }
        });
        let dir = std::env::temp_dir().join(format!("barbican-dl-size-{}", std::process::id()));
        let c = http().unwrap();
        let a = Asset { url: format!("http://127.0.0.1:{port}/Barbican.msi"), sha256: sha, size: payload.len() as u64 };
        assert!(download(&c, &a, &dir.join("over"), "2.0.0", Kind::Msi, |_| {}).is_err());
        assert!(!dir.join("over").join("2.0.0").join("Barbican.msi").exists());
        assert!(!dir.join("over").join("2.0.0").join("Barbican.part").exists());
        assert!(download(&c, &a, &dir.join("short"), "2.0.0", Kind::Msi, |_| {}).is_err());
        assert!(!dir.join("short").join("2.0.0").join("Barbican.msi").exists());
        let _ = std::fs::remove_dir_all(&dir);
    }
}
