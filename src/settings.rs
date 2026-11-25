//! Bulwark-parity settings: the page/section/row schema extracted from Bulwark's settings
//! components (ui/settings-schema.json, see tools/extract_settings.py), the user's values
//! (prefs.json, same keys as Bulwark's settings store) and the desktop add-on pages.
use crate::ai::Config;
use crate::{SRow, STab};
use serde_json::{json, Map, Value};
use slint::{ModelRc, SharedString, VecModel};

const SCHEMA: &str = include_str!("../ui/settings-schema.json");

/// The parsed prefs, shared by every [`Settings::load`]; each save makes a new one.
pub(crate) type Snapshot = std::sync::Arc<Map<String, Value>>;

/// The prefs as last saved, or as read from the file the first time they were needed: `load`
/// serves from here and never goes back to the disk. Every write of prefs.json goes through
/// [`Settings::save`] (and [`Settings::flush`]), which puts the new values here first, so the
/// cache is never older than the file.
struct Cache {
    /// The file these values belong to (fixed for a process: the profile is read once).
    path: std::path::PathBuf,
    prefs: Snapshot,
    /// Number of the newest save; a write of an older one is superseded.
    seq: u64,
    /// The newest save has not landed in the file yet ([`Settings::flush`] writes it at exit).
    pending: bool,
    /// The file could not be read when the session started: the values are the defaults, not
    /// the user's, so this session never writes the file (saves change the memory only).
    untrusted: bool,
}

type CacheCell = std::sync::Mutex<Option<Cache>>;

fn lock(cell: &CacheCell) -> std::sync::MutexGuard<'_, Option<Cache>> {
    cell.lock().unwrap_or_else(|e| e.into_inner())
}

/// Held around every write of the file (the background writes and the flush at exit).
static WRITE: std::sync::Mutex<()> = std::sync::Mutex::new(());

/// The program's one cache.
#[cfg(not(test))]
fn cache() -> std::sync::Arc<CacheCell> {
    static CACHE: std::sync::OnceLock<std::sync::Arc<CacheCell>> = std::sync::OnceLock::new();
    CACHE.get_or_init(|| std::sync::Arc::new(std::sync::Mutex::new(None))).clone()
}

#[cfg(test)]
thread_local! {
    /// Tests: this thread's cache while a [`CacheScope`] is alive; without one every load reads
    /// the file (into a throwaway cache), as tests in parallel share the process's prefs.json.
    static TEST_CACHE: std::cell::RefCell<Option<std::sync::Arc<CacheCell>>> = const { std::cell::RefCell::new(None) };
    /// Tests: how often this thread read prefs.json.
    pub static DISK_READS: std::cell::Cell<u32> = const { std::cell::Cell::new(0) };
    /// Tests: the prefs file of this thread's [`CacheScope`] (its own, so it races no other test).
    static TEST_PATH: std::cell::RefCell<Option<std::path::PathBuf>> = const { std::cell::RefCell::new(None) };
}

#[cfg(test)]
fn cache() -> std::sync::Arc<CacheCell> {
    TEST_CACHE.with(|t| t.borrow().clone()).unwrap_or_else(|| std::sync::Arc::new(std::sync::Mutex::new(None)))
}

/// Tests that write the test process's shared settings files (settings.json, prefs.json) or
/// register the live settings hold this, so they never run into each other.
#[cfg(test)]
pub(crate) static TEST_FILES: std::sync::Mutex<()> = std::sync::Mutex::new(());

/// Tests: the settings cache is on for this thread, over a prefs file of its own, until the scope
/// is dropped; then the thread's previous cache and file are back (scopes nest), so no test sees
/// another's cached values.
#[cfg(test)]
pub(crate) struct CacheScope {
    file: std::path::PathBuf,
    prev: Option<(Option<std::path::PathBuf>, Option<std::sync::Arc<CacheCell>>)>,
}

#[cfg(test)]
impl CacheScope {
    pub(crate) fn new() -> CacheScope {
        static N: std::sync::atomic::AtomicU32 = std::sync::atomic::AtomicU32::new(0);
        let n = N.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
        let _ = std::fs::create_dir_all(crate::config_dir());
        let file = crate::config_dir().join(format!("prefs-scope-{n}.json"));
        let _ = std::fs::remove_file(&file);
        for b in bad_copies(&file) { let _ = std::fs::remove_file(b); }
        let path = TEST_PATH.with(|t| t.replace(Some(file.clone())));
        let cell = TEST_CACHE.with(|t| t.replace(Some(std::sync::Arc::new(std::sync::Mutex::new(None)))));
        CacheScope { file, prev: Some((path, cell)) }
    }

    /// The scope's prefs file.
    pub(crate) fn file(&self) -> &std::path::Path { &self.file }
}

#[cfg(test)]
impl Drop for CacheScope {
    fn drop(&mut self) {
        debug_assert!(TEST_PATH.with(|t| t.borrow().as_deref() == Some(self.file.as_path())), "scopes end in the order they began");
        if let Some((path, cell)) = self.prev.take() {
            TEST_CACHE.with(|t| *t.borrow_mut() = cell);
            TEST_PATH.with(|t| *t.borrow_mut() = path);
        }
        let _ = std::fs::remove_file(&self.file);
        for b in bad_copies(&self.file) { let _ = std::fs::remove_file(b); }
    }
}

/// The copies [`back_up`] made of `p` (`prefs.json.<time>.bad`), oldest first.
#[cfg(test)]
fn bad_copies(p: &std::path::Path) -> Vec<std::path::PathBuf> {
    let (Some(dir), Some(name)) = (p.parent(), p.file_name()) else { return vec![] };
    let pre = format!("{}.", name.to_string_lossy());
    let mut v: Vec<std::path::PathBuf> = std::fs::read_dir(dir).map(|rd| rd.flatten()
        .filter(|e| { let n = e.file_name().to_string_lossy().to_string(); n.starts_with(&pre) && n.ends_with(".bad") })
        .map(|e| e.path()).collect()).unwrap_or_default();
    v.sort_by_key(|p| std::fs::metadata(p).and_then(|m| m.modified()).ok());
    v
}

/// Keep `bytes`, a damaged prefs file, beside it before anything can save over it: each one
/// under a name of its own (`prefs.json.<unix time>.bad`, `-2`, `-3`... in the same second), so
/// an older copy never makes a newer one go. Whether these bytes are on disk now.
fn back_up(p: &std::path::Path, bytes: &[u8]) -> bool {
    use std::io::Write;
    let secs = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map(|d| d.as_secs()).unwrap_or(0);
    let mut name = p.as_os_str().to_owned();
    name.push(format!(".{secs}"));
    for i in 1..100 {
        let mut b = name.clone();
        if i > 1 { b.push(format!("-{i}")); }
        b.push(".bad");
        match std::fs::OpenOptions::new().write(true).create_new(true).open(std::path::PathBuf::from(b)) {
            Ok(mut f) => return f.write_all(bytes).and_then(|_| f.sync_all()).is_ok(),
            Err(e) if e.kind() == std::io::ErrorKind::AlreadyExists => continue,
            Err(_) => return false,
        }
    }
    false
}

/// What reading the prefs file gave.
enum Read {
    /// The prefs, or none yet (no file).
    Prefs(Map<String, Value>),
    /// Not a settings object; a copy was kept (see [`back_up`]): reading again gives the same.
    Damaged,
    /// Could not be read (locked by another program, access denied), or a damaged file that no
    /// copy could be made of: nothing may be written over it.
    Unusable,
}

fn read_file(p: &std::path::Path) -> Read {
    #[cfg(test)]
    DISK_READS.with(|n| n.set(n.get() + 1));
    let bytes = match std::fs::read(p) {
        Ok(b) => b,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Read::Prefs(Map::new()),
        Err(_) => return Read::Unusable,
    };
    match serde_json::from_slice::<Value>(&bytes) {
        Ok(Value::Object(m)) => Read::Prefs(m),
        _ if back_up(p, &bytes) => Read::Damaged,
        _ => Read::Unusable,
    }
}

/// Tries of a prefs file that cannot be read when the session starts, and the pause between.
const READ_TRIES: u32 = 3;
const READ_PAUSE: std::time::Duration = std::time::Duration::from_millis(50);

/// The prefs in `cell` for the file `p`, read from it once, when the cache has nothing for it yet
/// (under the cache's lock, so a save meanwhile is never replaced by the older file). That one
/// read decides the session: every copy of the settings comes from it.
/// - A damaged file: the defaults, the file kept as a `.bad` copy (the next save replaces it).
/// - A file that cannot be read, tried [`READ_TRIES`] times (a virus scanner or a sync tool may
///   hold it a moment): the defaults, and the session is untrusted, so nothing ever writes the
///   file: saving the defaults would replace the user's real settings. [`unsaved_session`] says so.
fn fill(cell: &CacheCell, p: &std::path::Path) -> Snapshot {
    let mut c = lock(cell);
    if let Some(c) = c.as_ref().filter(|c| c.path == p) { return c.prefs.clone(); }
    remove_stale_temp(p);
    let mut read = read_file(p);
    for _ in 1..READ_TRIES {
        if !matches!(read, Read::Unusable) { break; }
        std::thread::sleep(READ_PAUSE);
        read = read_file(p);
    }
    let (m, untrusted) = match read { Read::Prefs(m) => (m, false), Read::Damaged => (Map::new(), false), Read::Unusable => (Map::new(), true) };
    let prefs: Snapshot = std::sync::Arc::new(m);
    *c = Some(Cache { path: p.to_path_buf(), prefs: prefs.clone(), seq: 0, pending: false, untrusted });
    prefs
}

/// The settings file could not be read when this session started: changes are kept until the
/// program quits but never saved (the app says so once).
pub(crate) fn unsaved_session() -> bool {
    let p = path();
    let cell = cache();
    let _ = fill(&cell, &p);
    let c = lock(&cell);
    c.as_ref().is_some_and(|c| c.path == p && c.untrusted)
}

/// Remove the temporary files an earlier run left when it stopped mid-write
/// (`prefs.json.<pid>-<n>.tmp` of another process, untouched for a minute; this one's may be in use).
fn remove_stale_temp(p: &std::path::Path) {
    const STALE_AFTER: std::time::Duration = std::time::Duration::from_secs(60);
    let (Some(dir), Some(name)) = (p.parent(), p.file_name()) else { return };
    let pre = format!("{}.", name.to_string_lossy());
    let Ok(rd) = std::fs::read_dir(dir) else { return };
    for e in rd.flatten() {
        let n = e.file_name().to_string_lossy().to_string();
        let Some(mid) = n.strip_prefix(&pre).and_then(|r| r.strip_suffix(".tmp")) else { continue };
        let Some((pid, seq)) = mid.split_once('-') else { continue };
        if pid.parse::<u32>().is_err() || seq.parse::<u64>().is_err() { continue; }
        // a write of another instance may be under way: only files untouched for a minute go
        let old = e.metadata().and_then(|m| m.modified()).ok().and_then(|t| t.elapsed().ok()).is_some_and(|age| age >= STALE_AFTER);
        if !old { continue; }
        if pid != std::process::id().to_string() { let _ = std::fs::remove_file(e.path()); }
    }
}

/// The prefs as last saved.
fn current() -> Snapshot { fill(&cache(), &path()) }

/// The prefs as last saved, without copying them (read-only; a later save makes a new snapshot).
pub(crate) fn snapshot() -> Snapshot { current() }

/// Writes a prefs file (the real one: [`write_file`]; tests pass their own).
type Writer<'a> = &'a dyn Fn(&std::path::Path, &Map<String, Value>) -> std::io::Result<()>;

/// Start a save: from now on every load gets `prefs`; the file is still to be written. The save's
/// number, for [`write_save`], and whether the session is untrusted (the file is never written).
/// The caller has filled the cache for `path` first ([`fill`]), so the session's one read
/// decided that.
fn begin_save(cell: &CacheCell, path: std::path::PathBuf, prefs: Snapshot) -> (u64, bool) {
    let mut c = lock(cell);
    let seq = c.as_ref().map_or(0, |c| c.seq) + 1;
    let untrusted = c.as_ref().is_some_and(|c| c.path == path && c.untrusted);
    *c = Some(Cache { path, prefs, seq, pending: true, untrusted });
    (seq, untrusted)
}

/// Write save `seq` to its file (the caller holds [`WRITE`]): nothing when a newer save
/// superseded it (that one's own write follows), it has landed already (a flush) or the session
/// is untrusted. A write that fails stays pending, for the flush at exit. Whether it wrote.
fn write_save(cell: &CacheCell, seq: u64, write: Writer) -> bool {
    let job = lock(cell).as_ref().filter(|c| c.seq == seq && c.pending && !c.untrusted).map(|c| (c.path.clone(), c.prefs.clone()));
    let Some((p, prefs)) = job else { return false };
    if write(&p, &prefs).is_err() { return false; }
    if let Some(c) = lock(cell).as_mut().filter(|c| c.seq == seq) { c.pending = false; }
    true
}

/// Write the newest save if it has not landed yet (the caller holds [`WRITE`]). Whether it wrote.
fn flush_save(cell: &CacheCell, write: Writer) -> bool {
    let seq = lock(cell).as_ref().filter(|c| c.pending).map(|c| c.seq);
    seq.is_some_and(|s| write_save(cell, s, write))
}

pub(crate) fn get_bool(p: &Map<String, Value>, key: &str, default: bool) -> bool {
    p.get(key).and_then(|v| v.as_bool()).unwrap_or(default)
}

pub(crate) fn get_str(p: &Map<String, Value>, key: &str, default: &str) -> String {
    p.get(key).and_then(|v| v.as_str()).unwrap_or(default).to_string()
}

fn get_int(p: &Map<String, Value>, key: &str, default: i64) -> i64 {
    match p.get(key) {
        Some(Value::Number(n)) => n.as_i64().or_else(|| n.as_f64().map(|f| f as i64)).unwrap_or(default),
        Some(Value::String(s)) => s.trim().parse().unwrap_or(default),
        _ => default,
    }
}

/// [`Settings::bool`] on the prefs as last saved, without copying them (one value, read once).
pub(crate) fn bool_pref(key: &str, default: bool) -> bool { get_bool(&current(), key, default) }

/// [`Settings::str`] on the prefs as last saved, without copying them.
pub(crate) fn str_pref(key: &str, default: &str) -> String { get_str(&current(), key, default) }

/// [`Settings::int`] on the prefs as last saved, without copying them.
#[allow(dead_code)]
pub(crate) fn int_pref(key: &str, default: i64) -> i64 { get_int(&current(), key, default) }

/// Write the prefs file whole or not at all: a temporary file in the same folder, renamed over it
/// (a crash or a full disk mid-write leaves the old file, never half of one).
fn write_file(p: &std::path::Path, prefs: &Map<String, Value>) -> std::io::Result<()> {
    use std::io::Write;
    static N: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);
    let dir = p.parent().ok_or_else(|| std::io::Error::other("no folder"))?;
    std::fs::create_dir_all(dir)?;
    let text = serde_json::to_string(prefs).map_err(std::io::Error::other)?;
    let name = p.file_name().map(|n| n.to_string_lossy().to_string()).unwrap_or_else(|| "prefs.json".into());
    // a name of its own per write: tests write the shared file from several threads
    let tmp = dir.join(format!("{name}.{}-{}.tmp", std::process::id(), N.fetch_add(1, std::sync::atomic::Ordering::SeqCst)));
    let res = std::fs::File::create(&tmp).and_then(|mut f| { f.write_all(text.as_bytes())?; f.sync_all() })
        .and_then(|_| rename_over(&tmp, p));
    if res.is_err() { let _ = std::fs::remove_file(&tmp); }
    res
}

/// Rename `from` over `to`, again after a moment while another program (a virus scanner, the
/// search indexer) has `to` open, which Windows reports as access denied.
fn rename_over(from: &std::path::Path, to: &std::path::Path) -> std::io::Result<()> {
    let mut tries = 0;
    loop {
        match std::fs::rename(from, to) {
            // access denied, or ERROR_SHARING_VIOLATION (32) from a program that opened it without sharing
            Err(e) if (e.kind() == std::io::ErrorKind::PermissionDenied || e.raw_os_error() == Some(32)) && tries + 1 < READ_TRIES => {
                tries += 1;
                std::thread::sleep(READ_PAUSE);
            }
            r => return r,
        }
    }
}

pub struct Account {
    pub email: String,
    pub server: String,
    pub auth: String,
}

pub struct Settings {
    /// Parsed once per process: settings are loaded on hot paths (every message opened).
    schema: std::sync::Arc<Value>,
    pub prefs: Map<String, Value>,
}

fn schema() -> std::sync::Arc<Value> {
    static S: std::sync::OnceLock<std::sync::Arc<Value>> = std::sync::OnceLock::new();
    S.get_or_init(|| std::sync::Arc::new(serde_json::from_str(SCHEMA).expect("settings schema"))).clone()
}

fn path() -> std::path::PathBuf {
    #[cfg(test)]
    if let Some(p) = TEST_PATH.with(|p| p.borrow().clone()) { return p; }
    crate::config_dir().join("prefs.json")
}

/// The extracted schema, for settings sync's type checks.
pub(crate) fn schema_value() -> std::sync::Arc<Value> { schema() }

/// The values a select row offers when they come from Barbican's own list (see
/// `option_override`), for settings sync's type checks.
pub(crate) fn option_values(key: &str) -> Option<Vec<Value>> { option_override(key).map(|o| o.into_iter().map(|(v, _)| v).collect()) }

/// Keyless controls in the extracted schema that map to a known setting.
fn key_for_label(label: &str) -> Option<&'static str> {
    match label {
        "Light or dark" => Some("theme"),
        "Check spelling and grammar while I type" => Some("proofreading"),
        // Settings > Files: fields of Bulwark's `filesSettings` object (src/files_view.rs)
        l => crate::files_view::row_key(l),
    }
}

/// Options the extractor could not resolve (built from constants in Bulwark's code).
fn option_override(key: &str) -> Option<Vec<(Value, &'static str)>> {
    if key.starts_with("files:") { return crate::files_view::row_options(key); }
    Some(match key {
        "sendDelaySeconds" => vec![(json!(0), "Off"), (json!(5), "5 seconds"), (json!(10), "10 seconds"), (json!(20), "20 seconds"), (json!(30), "30 seconds")],
        // Bulwark UNREAD_SECTION_SIZES
        "mvUnreadSectionSize" => vec![(json!(5), "5 messages"), (json!(10), "10 messages"), (json!(15), "15 messages"), (json!(20), "20 messages"), (json!(25), "25 messages"), (json!(50), "50 messages")],
        "composePlacement" => vec![(json!("sidebar"), "In the sidebar"), (json!("rail"), "On the navigation rail"), (json!("floating"), "Floating button")],
        "theme" => vec![(json!("light"), "Light"), (json!("dark"), "Dark"), (json!("system"), "System")],
        // Bulwark FONT_SIZES
        "fontSize" => vec![(json!("small"), "Small (87%)"), (json!("medium"), "Normal (100%)"), (json!("large"), "Large (112%)"), (json!("x-large"), "Larger (125%)"),
            (json!("xx-large"), "Extra large (150%)"), (json!("xxx-large"), "Huge (175%)"), (json!("huge"), "Maximum (200%)")],
        // Bulwark MAIL_TEXT_SCALES, MAIL_MIN_FONT_SIZES, TOAST_DURATIONS
        "mailTextScale" => vec![(json!(0.0), "Same as the app"), (json!(0.875), "87%"), (json!(1.0), "100%"), (json!(1.125), "112%"),
            (json!(1.25), "125%"), (json!(1.5), "150%"), (json!(1.75), "175%"), (json!(2.0), "200%")],
        "mailMinFontSize" => vec![(json!(0), "No minimum"), (json!(12), "12 px"), (json!(14), "14 px"), (json!(16), "16 px"), (json!(18), "18 px")],
        "toastDuration" => vec![(json!(5), "5 seconds"), (json!(10), "10 seconds"), (json!(20), "20 seconds"), (json!(60), "1 minute"), (json!(0), "Until I close them")],
        "hoverActionsMode" => vec![(json!("inline"), "In the row"), (json!("floating"), "Floating in a corner")],
        "hoverActionsCorner" => vec![(json!("top-right"), "Top right"), (json!("top-left"), "Top left"), (json!("bottom-right"), "Bottom right"), (json!("bottom-left"), "Bottom left")],
        "calendarDayStartHour" => (0..13).map(|h| (json!(h), ["0:00","1:00","2:00","3:00","4:00","5:00","6:00","7:00","8:00","9:00","10:00","11:00","12:00"][h as usize])).collect(),
        "calendarDayEndHour" => (13..=24).map(|h| (json!(h), ["13:00","14:00","15:00","16:00","17:00","18:00","19:00","20:00","21:00","22:00","23:00","24:00"][(h - 13) as usize])).collect(),
        // Bulwark ReadReceiptResponse (privacy-settings.tsx)
        "readReceiptResponse" => vec![(json!("ask"), "Ask each time"), (json!("always"), "Always send"), (json!("never"), "Never send")],
        "writingLanguage" => vec![(json!("auto"), "Detect automatically"), (json!("en"), "English"), (json!("de"), "German"), (json!("fr"), "French"), (json!("es"), "Spanish"), (json!("tr"), "Turkish")],
        // Bulwark NOTIFICATION_SOUNDS (lib/notification-sound.ts) with its English labels
        // (locales/en: notification_settings.sounds.*); notify::Tone plays them
        crate::notify::TONE_KEY => crate::notify::Tone::ALL.iter().map(|t| (json!(t.id()), t.label())).collect(),
        _ => return None,
    })
}

/// Bulwark ALL_HOVER_ACTIONS that Barbican's rows can run (the tag picker needs a menu).
pub const HOVER_ACTIONS: [(&str, &str); 5] = [("delete", "Delete"), ("star", "Star"), ("markRead", "Mark as read"), ("archive", "Archive"), ("spam", "Spam")];

fn s(v: &str) -> SharedString {
    SharedString::from(v)
}

/// Largest retention (days) the Trash / Junk note accepts.
pub const RETENTION_MAX: i64 = 3650;

pub fn is_retention_key(key: &str) -> bool {
    matches!(key, "retentionTrashDays" | "retentionJunkDays")
}

#[derive(Debug, PartialEq)]
pub enum Retention { Days(i64), Unset, Invalid }

/// What the user typed in a retention field: empty clears it, digits are capped at 3650
/// (a minus sign counts as 0), anything else is ignored.
pub fn parse_retention(s: &str) -> Retention {
    let s = s.trim();
    if s.is_empty() { return Retention::Unset; }
    match s.parse::<i64>() {
        Ok(n) => Retention::Days(n.clamp(0, RETENTION_MAX)),
        Err(_) if s.strip_prefix('-').is_some_and(|d| !d.is_empty() && d.bytes().all(|b| b.is_ascii_digit())) => Retention::Days(0),
        Err(_) if s.bytes().all(|b| b.is_ascii_digit()) => Retention::Days(RETENTION_MAX), // overflow
        Err(_) => Retention::Invalid,
    }
}

fn row(kind: &str, key: &str, label: &str, desc: &str) -> SRow {
    SRow { kind: s(kind), key: s(key), label: s(label), desc: s(desc), options: ModelRc::default(), index: 0, on: false, text: s("") }
}

fn select(key: &str, label: &str, desc: &str, opts: &[&str], index: i32) -> SRow {
    let mut r = row("select", key, label, desc);
    r.options = ModelRc::new(VecModel::from(opts.iter().map(|o| s(o)).collect::<Vec<_>>()));
    r.index = index;
    r
}

fn toggle(key: &str, label: &str, desc: &str, on: bool) -> SRow {
    let mut r = row("toggle", key, label, desc);
    r.on = on;
    r
}

/// Password & sign-in on a server without account management: the schema has nothing to show
/// there any more (the app lock's rows are gone), so the page says so instead of staying blank.
pub fn security_or_note(rows: Vec<SRow>) -> Vec<SRow> {
    if !rows.is_empty() { return rows; }
    vec![text("value", "", "Nothing to manage on this server.", "", "")]
}

fn text(kind: &str, key: &str, label: &str, desc: &str, value: &str) -> SRow {
    let mut r = row(kind, key, label, desc);
    r.text = s(value);
    r
}

/// Forget the package installed in `package_dir`: the `pluginGrants` and `pluginBindings` entries for `id` go only when
/// they pin a file inside that folder (a library dropped into the plugins folder under the same id keeps its own), and
/// `id` leaves the switched-on list once nothing is left that could switch it on. An empty `package_dir` forgets
/// nothing.
pub fn forget_package(prefs: &mut serde_json::Map<String, Value>, id: &str, package_dir: &std::path::Path) {
    if package_dir.as_os_str().is_empty() { return; }
    // the package's own settings go with it (only API 2 packages have any)
    if let Some(Value::Object(m)) = prefs.get_mut("pluginSettings") { m.remove(id); }
    let inside = |path: &str| std::path::Path::new(path).starts_with(package_dir);
    if let Some(Value::Object(m)) = prefs.get_mut("pluginGrants") {
        if m.get(id).and_then(|g| g["path"].as_str()).is_some_and(inside) { m.remove(id); }
    }
    if let Some(Value::Object(m)) = prefs.get_mut("pluginBindings") {
        if m.get(id).and_then(|b| b.as_str()).and_then(|b| b.rsplit_once('|')).is_some_and(|(p, _)| inside(p)) { m.remove(id); }
    }
    let kept = ["pluginGrants", "pluginBindings"].iter().any(|k| prefs.get(*k).and_then(|m| m.get(id)).is_some());
    if !kept {
        if let Some(Value::Array(l)) = prefs.get_mut("enabledPlugins") { l.retain(|x| x.as_str() != Some(id)); }
    }
}

impl Settings {
    /// The prefs as last saved, served from memory (the file is read once per process).
    pub fn load() -> Settings {
        Settings { schema: schema(), prefs: (*current()).clone() }
    }

    /// Settings over the given values (no file), for tests and previews.
    #[allow(dead_code)]
    pub fn with_prefs(prefs: Map<String, Value>) -> Settings {
        Settings { schema: schema(), prefs }
    }

    /// Blocks until a pending background save has landed (call before the process exits).
    pub fn flush() {
        let _w = WRITE.lock();
        flush_save(&cache(), &write_file);
    }

    /// Store one pref value and save.
    pub fn remember(&mut self, key: &str, value: Value) {
        self.prefs.insert(key.into(), value);
        self.save();
    }

    /// Saves the settings: the cache serves every later `load` at once, and the file is written
    /// off the UI thread, the newest save always winning. Tests write synchronously.
    pub(crate) fn save(&self) {
        let (cell, p) = (cache(), path());
        // the session's one read of the file comes first: it decides whether the file may be written
        let _ = fill(&cell, &p);
        let (seq, untrusted) = begin_save(&cell, p, std::sync::Arc::new(self.prefs.clone()));
        #[cfg(test)]
        {
            let _ = untrusted;
            let _w = WRITE.lock();
            write_save(&cell, seq, &write_file);
        }
        #[cfg(not(test))]
        {
            // a change to a synced setting goes to the account (debounced); after the cache has
            // it, so whatever that reads back is the new values. Not in an untrusted session:
            // its values started from the defaults, not from the user's settings.
            if !untrusted { crate::settings_sync::local_saved(&self.prefs); }
            std::thread::spawn(move || {
                let _w = WRITE.lock();
                write_save(&cell, seq, &write_file);
            });
        }
    }

    /// Everything worth backing up: Bulwark-keyed prefs plus the desktop/assistant settings file.
    pub fn export(&self) -> Value {
        let desktop: Value = std::fs::read_to_string(crate::config_dir().join("settings.json")).ok()
            .and_then(|t| serde_json::from_str(&t).ok()).unwrap_or(json!({}));
        // a plugin's own settings stay on this computer (they can hold what the plugin was told about a server)
        let mut prefs = self.prefs.clone();
        prefs.remove("pluginSettings");
        json!({"app": "barbican", "version": 1, "settings": Value::Object(prefs), "desktop": desktop})
    }

    /// Accepts Barbican exports and Bulwark's own settings export (a flat settings object).
    pub fn import(&mut self, v: &Value) -> Result<(), String> {
        let prefs = if v["app"] == "barbican" { v["settings"].clone() } else if v["settings"].is_object() { v["settings"].clone() } else { v.clone() };
        let Some(obj) = prefs.as_object() else { return Err("This file has no settings.".into()) };
        let mut imported = obj.clone();
        // Plugin consent is per computer and per file: never copy an allow-list or its file bindings
        // from a settings file, or an import could switch on code this user has not agreed to run.
        // The current local values stay, so an import does not switch off plugins enabled here either.
        for key in ["enabledPlugins", "pluginBindings", "pluginGrants", "pluginSettings"] {
            match self.prefs.get(key) {
                Some(v) => { imported.insert(key.into(), v.clone()); }
                None => { imported.remove(key); }
            }
        }
        // Bulwark's own export: a `false` it never recorded as chosen means "not decided" (on),
        // and a `default` tone it never recorded as chosen was the old default before Involved
        if v["app"] != "barbican" { crate::trustbook::migrate_book_default(&mut imported); crate::notify::migrate_sound_default(&mut imported); }
        self.prefs = imported;
        self.save();
        if v["desktop"].is_object() {
            let _ = std::fs::write(crate::config_dir().join("settings.json"), v["desktop"].to_string());
        }
        Ok(())
    }

    pub fn reset(&mut self) {
        self.prefs.clear();
        self.save();
        let _ = std::fs::remove_file(crate::config_dir().join("settings.json"));
    }

    /// Store one value (None removes it) and save, outside the settings page's own controls:
    /// through the live settings, so the program's next save keeps it (and the sync sees it).
    pub fn put(key: &str, value: Option<Value>) {
        with_live(|s| {
            match value { Some(v) => { s.prefs.insert(key.into(), v); } None => { s.prefs.remove(key); } }
            s.save();
        });
    }

    pub fn bool(&self, key: &str, default: bool) -> bool { get_bool(&self.prefs, key, default) }

    pub fn str(&self, key: &str, default: &str) -> String { get_str(&self.prefs, key, default) }

    /// Numeric pref; tolerates numbers stored as strings.
    pub fn int(&self, key: &str, default: i64) -> i64 { get_int(&self.prefs, key, default) }

    fn tab_list(&self) -> Vec<(String, String, String, String, i32)> {
        // id, label, group, icon, indent
        let mut out = vec![];
        for t in self.schema["tabs"].as_array().unwrap() {
            let id = t["id"].as_str().unwrap_or("");
            out.push((id.into(), t["label"].as_str().unwrap_or("").into(), t["group"].as_str().unwrap_or("").into(), t["icon"].as_str().unwrap_or("").into(), 0));
            // distribution: the desktop app's own page (Bulwark desktop-app-settings.tsx)
            if id == "general" {
                out.push(("desktop".into(), "Desktop app".into(), "top".into(), "\u{E7F4}".into(), 0));
            }
            if id == "backup" {
                out.push(("admin".into(), "Stalwart admin".into(), "bottom".into(), "\u{E83D}".into(), 0));
            }
            if id == "calendar" {
                out.push(("calendars".into(), "Calendars".into(), "apps".into(), "\u{E8F1}".into(), 1));
            }
            if id == "contacts" {
                out.push(("addressbooks".into(), "Address books".into(), "apps".into(), "\u{E82D}".into(), 1));
            }
            if id == "addons" {
                out.push(("ai".into(), "AI Assistant".into(), "apps".into(), "\u{E945}".into(), 1));
                out.push(("spam".into(), "Spam Score".into(), "apps".into(), "\u{E7BA}".into(), 1));
            }
        }
        out
    }

    pub fn tabs(&self, advanced: bool) -> ModelRc<STab> {
        ModelRc::new(VecModel::from(self.tab_models(advanced)))
    }

    pub fn tab_models(&self, advanced: bool) -> Vec<STab> {
        let mut v = vec![];
        let mut last = String::new();
        for (id, label, group, icon, indent) in self.tab_list() {
            if id == "troubleshooting" && !advanced { continue; }
            let header = if group != last {
                let h = match group.as_str() { "mail" => "Mail", "apps" => "Apps", "bottom" => "-", _ => "" };
                last = group.clone();
                h.to_string()
            } else { String::new() };
            v.push(STab { id: s(&id), label: s(&label), icon: s(&icon), header: s(&header), indent });
        }
        v
    }

    pub fn title(&self, tab: &str) -> String {
        self.tab_list().into_iter().find(|t| t.0 == tab).map(|t| t.1).unwrap_or_default()
    }

    fn schema_rows(&self, tab: &str) -> Vec<SRow> {
        let Some(t) = self.schema["tabs"].as_array().unwrap().iter().find(|t| t["id"] == tab) else { return vec![] };
        let mut out = vec![];
        for r in t["rows"].as_array().unwrap() {
            let kind = r["kind"].as_str().unwrap_or("");
            let label = &r["label"].as_str().unwrap_or("").replace("{appName}", "Barbican");
            let desc = &r["desc"].as_str().unwrap_or("").replace("{appName}", "Barbican");
            let key = r["key"].as_str().filter(|k| !k.is_empty()).or_else(|| key_for_label(label)).unwrap_or("");
            let (label, desc) = (label.as_str(), desc.as_str());
            match kind {
                "section" => out.push(row("section", "", label, desc)),
                // Settings > Files: one synced object (Bulwark filesSettings, src/files_view.rs)
                "toggle" if key.starts_with("files:") => out.push(toggle(key, label, desc, crate::files_view::row_value(&self.prefs, key) == json!(true))),
                "select" if key.starts_with("files:") => {
                    let opts = crate::files_view::row_options(key).unwrap_or_default();
                    let cur = crate::files_view::row_value(&self.prefs, key);
                    let labels: Vec<&str> = opts.iter().map(|o| o.1).collect();
                    out.push(select(key, label, desc, &labels, opts.iter().position(|o| o.0 == cur).unwrap_or(0) as i32));
                }
                "toggle" if !key.is_empty() => {
                    let invert = r["invert"].as_bool() == Some(true);
                    let dflt = r["default"].as_bool().unwrap_or(false);
                    let v = self.bool(key, dflt);
                    // Barbican keeps it in the account's metadata, not on Bulwark's web server
                    let desc = if key == crate::settings_sync::DISABLED_KEY { crate::settings_sync::row_desc() } else { desc.to_string() };
                    out.push(toggle(key, label, &desc, if invert { !v } else { v }));
                    // desktop: local spell checking (Windows spell checker) next to Bulwark's proofreading add-on toggle
                    if key == "proofreading" {
                        out.push(toggle("spellCheck", "Underline misspelled words",
                            &format!("Checks spelling on this computer with {}, in your writing language. Right-click a word for suggestions. Nothing is sent anywhere.", crate::platform::spell_engine_name()),
                            self.bool("spellCheck", true)));
                    }
                }
                "select" if !key.is_empty() => {
                    let opts: Vec<(Value, String)> = match option_override(key) {
                        Some(o) => o.into_iter().map(|(v, l)| (v, l.to_string())).collect(),
                        None => r["options"].as_array().map(|a| a.iter().filter(|o| !o[1].as_str().unwrap_or("").contains("${"))
                            .map(|o| (o[0].clone(), o[1].as_str().unwrap_or("").to_string())).collect()).unwrap_or_default(),
                    };
                    if opts.is_empty() { continue; }
                    let cur = self.prefs.get(key).cloned().or_else(|| r.get("default").cloned())
                        .unwrap_or(if key == "theme" { json!("dark") } else { Value::Null });
                    let idx = opts.iter().position(|(v, _)| v == &cur || v.as_str().is_some_and(|x| Some(x.to_string()) == cur.as_i64().map(|n| n.to_string()) || Some(x) == cur.as_str())).unwrap_or(0);
                    let labels: Vec<&str> = opts.iter().map(|(_, l)| l.as_str()).collect();
                    // no speaker button yet (a toast is the only way this build can play a tone):
                    // Bulwark's line promises one it can't keep
                    let desc = if key == crate::notify::TONE_KEY { "The sound a new message or a calendar reminder plays." } else { desc };
                    out.push(select(key, label, desc, &labels, idx as i32));
                }
                "text" if is_retention_key(key) => {
                    let v = self.prefs.get(key).map(|_| self.int(key, 0).clamp(0, RETENTION_MAX).to_string()).unwrap_or_default();
                    out.push(text("text", key, label, desc, &v));
                }
                "text" if !key.is_empty() => out.push(text("text", key, label, desc, &self.str(key, r["default"].as_str().unwrap_or("")))),
                "button" if !key.is_empty() => out.push(text("button", key, label, desc, r["text"].as_str().unwrap_or("Reset"))),
                // Contacts > Import / Export (Bulwark contact-import-dialog, contact-export)
                "info" if label == "Import contacts" => out.push(text("button", "contacts.import", label, "Adds the contacts in a vCard (.vcf) file. Contacts whose email address you already have can be skipped.", "Import…")),
                "info" if label == "Export contacts" => out.push(text("button", "contacts.export", label, "Saves every contact as a vCard 4.0 file, photos included.", "Export…")),
                // About > Fix problems > Refresh cached data (Bulwark clearCachedData, S-keyless-063)
                "info" if label == "Refresh cached data" => out.push(text("button", "cache.refresh", label,
                    "Reload contacts, calendars and folders from the server. You stay signed in.", "Refresh")),
                // Calendar > Working days (Bulwark calendarWorkingDays: 0 = Sunday .. 6 = Saturday)
                "info" if label == "Working days" => {
                    out.push(row("section", "", label, desc));
                    let days = self.working_days();
                    let first = self.int("firstDayOfWeek", 1).clamp(0, 6);
                    for i in 0..7 {
                        let d = (first + i) % 7;
                        let name = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"][d as usize];
                        out.push(toggle(&format!("workday:{d}"), name, "", days.contains(&d)));
                    }
                }
                _ => {}
            }
        }
        // drop sections left empty
        let mut clean: Vec<SRow> = vec![];
        for (i, r) in out.iter().enumerate() {
            if r.kind == "section" && out.get(i + 1).is_none_or(|n| n.kind == "section") { continue; }
            clean.push(r.clone());
        }
        clean
    }

    pub fn rows(&self, tab: &str, cfg: &Config, acc: &Account) -> Vec<SRow> {
        let mut out = vec![];
        match tab {
            "account" => {
                out.push(row("section", "", "Account", "The account you are signed in with."));
                out.push(text("value", "", "Email address", "", &acc.email));
                out.push(text("value", "", "JMAP server", "", &acc.server));
                out.push(text("value", "", "Authentication", "", &acc.auth));
                out.push(text("button", "signout", "Sign out", "Signs this computer out and forgets the remembered session.", "Sign out"));
            }
            "ai" => {
                out.push(row("section", "", "AI service", "Nothing goes to the AI service until you ask the assistant something."));
                out.push(select("ai.provider", "Service", "", &["OpenAI-compatible (OpenAI, Ollama, LM Studio, vLLM)", "Anthropic"], cfg.provider));
                out.push(text("text", "ai.base", "API address", "Ends in /v1.", &cfg.base));
                out.push(text("password", "ai.key", "API key", &format!("Kept in {}. Ollama needs any non-empty key.", crate::platform::keystore_name()), &cfg.key));
                out.push(text("text", "ai.model", "Model", "Leave empty to use the default model.", &cfg.model));
                out.push(toggle("ai.visible", "Show the assistant next to messages", "The assistant panel and the quick actions above an open message.", !cfg.hidden));
                out.push(select("ai.actions", "Actions", "Let the assistant archive, move and mark messages, add events and contacts, and open replies for you. Ask me first shows each action for your OK; Automatic does it right away. Sending always asks.", &["Off", "Ask me first", "Automatic"], cfg.actions));
                out.push(row("section", "", "Standing instructions", "Added to every request, so the assistant knows how you like things done."));
                out.push(text("textarea", "ai.instructions", "Your instructions", "For example: I am a teacher; keep my emails short and warm.", &cfg.instructions));
                out.push(row("section", "", "Saved prompts", "One per line as  Name: what to ask.  They show as chips and as /name in the chat."));
                out.push(text("textarea", "ai.prompts", "Prompts", "", &cfg.prompts));
                out.push(row("section", "", "Memory", "Short notes the assistant keeps about your preferences."));
                out.push(toggle("ai.memory", "Remember notes", "", cfg.memory_on));
                out.push(text("textarea", "ai.memoryText", "Remembered notes", "Edit or clear them.", &Config::memory()));
                out.push(row("section", "", "Translation", ""));
                out.push(text("text", "ai.lang", "Translate into", "", &cfg.lang));
            }
            "addons" => {
                out.push(row("section", "", "Plugins", "Installed plugins stay off until you switch them on and accept what they ask for. A plugin runs code on your computer: switch on only the ones you trust."));
                crate::refresh_plugins();
                let list = crate::plugin_list();
                if list.is_empty() {
                    out.push(text("value", "", "No plugins installed", "Install a plugin package below, or copy a plugin .dll into the folder and reopen this page.", ""));
                }
                for p in list {
                    let id = p["id"].as_str().unwrap_or("");
                    let hooks = p["hooks"].as_array().map(|a| a.iter().filter_map(|h| h.as_str()).collect::<Vec<_>>().join(", ")).unwrap_or_default();
                    out.push(toggle(&format!("plugin:{id}"), &format!("{} {}", p["name"].as_str().unwrap_or(id), p["version"].as_str().unwrap_or("")),
                        &format!("{}  (hooks: {hooks})", p["description"].as_str().unwrap_or("")), p["enabled"].as_bool().unwrap_or(false)));
                    // an API 2 plugin's own settings (its manifest's schema), stored per plugin in `pluginSettings`
                    let stored = self.prefs.get("pluginSettings").and_then(|m| m.get(id)).cloned().unwrap_or(Value::Null);
                    let name = p["name"].as_str().unwrap_or(id);
                    for r in crate::plugin_host::setting_rows(id, &p["settings"], &stored) {
                        use crate::plugin_host::SettingRow;
                        out.push(match r {
                            SettingRow::Toggle { key, label, desc, on } => toggle(&key, &format!("{name}: {label}"), &desc, on),
                            SettingRow::Text { key, label, desc, value } => text("text", &key, &format!("{name}: {label}"), &desc, &value),
                            SettingRow::Select { key, label, desc, options, index } =>
                                select(&key, &format!("{name}: {label}"), &desc, &options.iter().map(String::as_str).collect::<Vec<_>>(), index),
                        });
                    }
                    if p["packaged"].as_bool() == Some(true) {
                        out.push(text("button", &format!("plugin-uninstall:{id}"), &format!("Uninstall {}", p["name"].as_str().unwrap_or(id)),
                            "Removes the plugin and everything it stored on this computer.", "Uninstall"));
                    }
                }
                for e in crate::plugin_errors() {
                    out.push(text("value", "", "Could not load", &e, ""));
                }
                out.push(text("button", "plugins.install", "Install plugin", "A plugin package (.zip) with a manifest.json and its library. It is checked, unpacked and listed here, switched off.", "Choose ZIP..."));
                out.push(text("button", "plugins.installFolder", "Install from a folder", "An unpacked plugin package.", "Choose folder..."));
                out.push(text("button", "plugins.folder", "Plugins folder", &crate::plugins::dir().display().to_string(), "Open folder"));
            }
            "backup" => {
                out.push(row("section", "", "Backup", "Save your settings to a file, or bring them back on another computer."));
                out.push(text("button", "backup.export", "Export settings", "Downloads a JSON file with your Barbican and Bulwark settings (API keys are not included).", "Export"));
                out.push(text("button", "backup.import", "Import settings", "Replaces your settings with the ones in a file you exported.", "Import"));
                out.push(row("section", "", "Fix problems", ""));
                out.push(text("button", "backup.reset", "Reset settings", "Puts every setting back to its default. Your mail is not touched.", "Reset"));
            }
            "admin" => {
                out.push(row("section", "", "Stalwart admin", "Enter an administrator API key (or admin access token) to open the Stalwart admin tab: dashboard, accounts, domains, delivery queue, logs and DMARC reports."));
                out.push(text("password", "admin.key", "Admin API key", &format!("Kept in {}. Leave empty to hide the admin tab.", crate::platform::keystore_name()), &crate::admin::key()));
            }
            "spam" => {
                out.push(row("section", "", "Spam Score", "Shows the spam analysis of a message from its spam headers."));
                out.push(toggle("spam.showClean", "Show for clean mail too", "When off, the badge only appears on suspicious or spam mail.", cfg.show_clean_spam));
            }
            _ => {
                out = self.schema_rows(tab);
                let sel_pref = |key: &str, label: &str, desc: &str| -> SRow {
                    let opts = option_override(key).unwrap_or_default();
                    let cur = self.prefs.get(key).cloned();
                    let idx = opts.iter().position(|(v, _)| Some(v) == cur.as_ref()).unwrap_or(match key { "fontSize" => 1, "calendarDayStartHour" => 8, _ => 0 });
                    select(key, label, desc, &opts.iter().map(|(_, l)| *l).collect::<Vec<_>>(), idx as i32)
                };
                if tab == "appearance" {
                    out.insert(1.min(out.len()), sel_pref("fontSize", "Text size", "Size of text across the app."));
                }
                if tab == "message_list" {
                    // Bulwark hoverActions / hoverActionsMode / hoverActionsCorner
                    out.push(row("section", "", "Hover actions", "The buttons that appear on a message when the pointer is over it."));
                    let on = self.hover_actions();
                    for (id, name) in HOVER_ACTIONS {
                        out.push(toggle(&format!("hoverAction:{id}"), name, "", on.iter().any(|a| a == id)));
                    }
                    out.push(sel_pref("hoverActionsMode", "Show them", "In the row replaces the time; floating shows them over a corner of the row."));
                    if self.str("hoverActionsMode", "inline") == "floating" {
                        out.push(sel_pref("hoverActionsCorner", "Corner", ""));
                    }
                }
                if tab == "composing" {
                    out.push(row("section", "", "Sending", ""));
                    out.push(toggle("sendConfirmation", "Confirm before sending", "Press Send twice to send, so a message doesn't go out by accident.", self.bool("sendConfirmation", false)));
                    out.push(toggle("requestReadReceiptDefault", "Ask for a read receipt", "New messages ask the recipient to confirm they read them.", self.bool("requestReadReceiptDefault", false)));
                    // Bulwark protocol-handler-settings / desktop "Default mail app"
                    out.push(row("section", "", "Default mail app", "mailto: links in messages always open here. Make Barbican the app for mailto: links in other programs too."));
                    if crate::mailto::is_default() {
                        out.push(text("value", "", "mailto: links", "", "Open in Barbican"));
                    } else {
                        out.push(text("button", "mailapp.register", "Use Barbican for mailto: links", if cfg!(windows) { "Registers Barbican for your Windows account, then opens Default apps so you can choose it." } else { "Adds barbican.desktop and sets it as the mailto: handler." }, "Set as default"));
                    }
                    if crate::mailto::is_registered() {
                        out.push(text("button", "mailapp.unregister", "Stop handling mailto: links", "Removes Barbican's registration for mailto: links from this account.", "Remove"));
                    }
                }
                if tab == "calendar" {
                    out.insert(1.min(out.len()), sel_pref("calendarDayStartHour", "Day starts at", "Where the day and week views open."));
                }
                if tab == "reading" {
                    out.push(row("section", "", "Rendering", "How the content of an email is drawn."));
                    out.push(select("desktop.renderMode", "Show email content with", "WebView2 uses the Edge engine. Blitz is a light native Rust engine that never loads anything from the network.", &["WebView2 (full HTML)", "Blitz (light, native)", "Plain text"], cfg.render_mode));
                }
                // Desktop app page, and Updates under About (src/desktop.rs)
                out.extend(crate::desktop::settings_rows(tab, self));
                if tab == "privacy" {
                    out.insert(0, row("section", "", "Remote content", "Images and other content loaded from the internet can tell senders when you read their mail."));
                    // Bulwark externalContentPolicy (stores/settings-store.ts:444): ask / block / allow
                    let policy = crate::trust::policy(&self.prefs, cfg.remote_images);
                    out.insert(1, select(crate::trust::POLICY_KEY, "Remote content", "Ask: blocked until you load a message's images or trust its sender. Always block: only trusted senders load. Always load: never blocked.",
                        &crate::trust::ContentPolicy::ALL.map(|p| p.label()), crate::trust::ContentPolicy::ALL.iter().position(|p| *p == policy).unwrap_or(0) as i32));
                    // Bulwark trustedSendersAddressBook (default on): the list lives in the server's
                    // "Trusted Senders" address book; off keeps it on this computer only
                    let in_book = crate::trustbook::in_book(&self.prefs);
                    let mut trust_rows: Vec<SRow> = vec![];
                    if in_book {
                        let shown = crate::trustbook::view_list(&self.prefs);
                        // what this drawing shows is what a later "Save changes" is compared with
                        crate::trustbook::note_shown(&shown);
                        trust_rows.push(text("textarea", crate::trustbook::TEXT_KEY, "Trusted senders", "Images from these addresses always load. One address per line, from your \"Trusted Senders\" address book and this computer. Change the lines, then choose Save changes: a new line trusts that address, a removed line stops trusting it.", &crate::trust::list_text(&shown)));
                        trust_rows.push(text("button", crate::trustbook::SAVE_KEY, "Save changes to trusted senders", "", "Save changes"));
                    } else {
                        trust_rows.push(text("textarea", crate::trust::TRUSTED_KEY, "Trusted senders", "Images from these addresses always load. One address per line; \"Trust sender\" in a message adds it here, and removing a line stops trusting it.", &crate::trust::list_text(&crate::trust::trusted_list(&self.prefs))));
                    }
                    let book_desc = if crate::trustbook::unavailable() && crate::trustbook::setting_on(&self.prefs) {
                        "Not available: this account can't keep address books on the server. The list stays on this computer."
                    } else {
                        "Store trusted senders in a dedicated \"Trusted Senders\" address book so they sync across all your devices"
                    };
                    trust_rows.push(toggle(crate::trustbook::SETTING_KEY, "Sync with address book", book_desc, crate::trustbook::setting_on(&self.prefs)));
                    let mut at = 2;
                    for r in trust_rows { out.insert(at, r); at += 1; }
                    out.insert(at, row("section", "", "Outside your organisation", "Your own domains are those of your identities."));
                    out.insert(at + 1, toggle(crate::trust::SENDER_LABEL_KEY, "Label external senders", "Shows \"External\" next to a sender outside your own domains when you read a message.", self.bool(crate::trust::SENDER_LABEL_KEY, true)));
                    out.insert(at + 2, toggle(crate::trust::RECIPIENT_CONFIRM_KEY, "Confirm before sending outside", "Asks before sending to a recipient outside your own domains, and lists them.", self.bool(crate::trust::RECIPIENT_CONFIRM_KEY, false)));
                    out.push(row("section", "", "Read receipts", ""));
                    out.push(sel_pref("readReceiptResponse", "Respond to read-receipt requests", "What to do when an incoming message asks for a read receipt."));
                }
            }
        }
        out
    }

    pub fn search(&self, q: &str, cfg: &Config, acc: &Account, advanced: bool) -> Vec<SRow> {
        let q = q.to_lowercase();
        let mut out = vec![];
        for (id, label, ..) in self.tab_list() {
            if id == "troubleshooting" && !advanced { continue; }
            let hits: Vec<SRow> = self.rows(&id, cfg, acc).into_iter()
                .filter(|r| r.kind != "section" && (r.label.to_lowercase().contains(&q) || r.desc.to_lowercase().contains(&q)))
                .collect();
            if !hits.is_empty() {
                out.push(row("section", "", &label, ""));
                out.extend(hits);
            }
        }
        out
    }

    /// Apply a change. Desktop keys go to the assistant/reader config, others to prefs.json.
    pub fn set(&mut self, key: &str, value: Value, cfg: &mut Config) {
        match key {
            "ai.provider" => {
                let p = value.as_i64().unwrap_or(0) as i32;
                if p != cfg.provider {
                    cfg.provider = p;
                    cfg.base = crate::ai::DEFAULTS[p as usize].0.into();
                    cfg.model = crate::ai::DEFAULTS[p as usize].1.into();
                }
            }
            "ai.base" => cfg.base = value.as_str().unwrap_or("").trim().into(),
            "ai.key" => cfg.key = value.as_str().unwrap_or("").trim().into(),
            "ai.model" => {
                let m = value.as_str().unwrap_or("").trim();
                cfg.model = if m.is_empty() { crate::ai::DEFAULTS[cfg.provider as usize].1.into() } else { m.into() };
            }
            "ai.visible" => cfg.hidden = !value.as_bool().unwrap_or(true),
            "ai.actions" => cfg.actions = value.as_i64().unwrap_or(1) as i32,
            "ai.instructions" => cfg.instructions = value.as_str().unwrap_or("").into(),
            "ai.prompts" => cfg.prompts = value.as_str().unwrap_or("").into(),
            "ai.memory" => cfg.memory_on = value.as_bool().unwrap_or(true),
            "ai.memoryText" => { Config::set_memory(value.as_str().unwrap_or("")); return; }
            "admin.key" => { crate::admin::set_key(value.as_str().unwrap_or("")); return; }
            "ai.lang" => cfg.lang = value.as_str().unwrap_or("").into(),
            "spam.showClean" => cfg.show_clean_spam = value.as_bool().unwrap_or(true),
            "desktop.renderMode" => cfg.render_mode = value.as_i64().unwrap_or(0) as i32,
            // Bulwark's list is an array of lower-cased addresses; the page edits it as text
            "trustedSenders" => {
                let list = crate::trust::parse_list(value.as_str().unwrap_or(""));
                self.prefs.insert(key.into(), crate::trust::trusted_json(&list));
                if let Some(tags) = self.prefs.get_mut(crate::trustbook::TAGS_KEY).and_then(|v| v.as_object_mut()) { tags.retain(|a, _| list.contains(a)); }
                self.save();
                return;
            }
            k if k.starts_with("files:") => {
                if crate::files_view::set_field(&mut self.prefs, k, &value) { self.save(); }
                return;
            }
            k if k.starts_with("hoverAction:") => {
                let id = &k[12..];
                let mut list = self.hover_actions();
                list.retain(|a| a != id);
                if value.as_bool() == Some(true) {
                    // keep Bulwark's order
                    list.push(id.to_string());
                    list.sort_by_key(|a| HOVER_ACTIONS.iter().position(|h| h.0 == a).unwrap_or(99));
                }
                self.prefs.insert("hoverActions".into(), json!(list));
                self.save();
                return;
            }
            k if k.starts_with("workday:") => {
                let d: i64 = k[8..].parse().unwrap_or(-1);
                let mut days = self.working_days();
                days.retain(|x| *x != d);
                if value.as_bool() == Some(true) { days.push(d); }
                days.sort();
                self.prefs.insert("calendarWorkingDays".into(), json!(days));
                self.save();
                return;
            }
            k if is_retention_key(k) => {
                match parse_retention(value.as_str().unwrap_or("")) {
                    Retention::Days(n) => { self.prefs.insert(key.into(), json!(n)); }
                    Retention::Unset => { self.prefs.remove(key); }
                    Retention::Invalid => return, // half-typed: keep the last good value
                }
                self.save();
                return;
            }
            "a11y.reset" => {
                for k in crate::a11y::reset_keys() { self.prefs.remove(k); }
                self.save();
                return;
            }
            k if k.starts_with("pluginForget:") => {
                // an uninstalled (or replaced) package: its own grant and binding gone, and off
                forget_package(&mut self.prefs, &k[13..], std::path::Path::new(value.as_str().unwrap_or("")));
                self.save();
                return;
            }
            k if k.starts_with("pluginSetting:") => {
                // `pluginSetting:<id>:<field>`: kept only when it is valid for the field (a half-typed number keeps the
                // last good value); a secret field is never kept here
                let Some((id, field)) = k[14..].split_once(':') else { return };
                let Some(schema) = crate::plugin_settings_schema(id) else { return };
                let Some(v) = crate::plugin_host::setting_value(&schema, field, &value) else { return };
                let mut all = self.prefs.get("pluginSettings").and_then(|v| v.as_object().cloned()).unwrap_or_default();
                let mut mine = all.get(id).and_then(|v| v.as_object().cloned()).unwrap_or_default();
                mine.insert(field.to_string(), v);
                all.insert(id.to_string(), Value::Object(mine));
                self.prefs.insert("pluginSettings".into(), Value::Object(all));
                self.save();
                crate::plugin_settings_changed(id);
                return;
            }
            k if k.starts_with("plugin:") => {
                let id = &k[7..];
                let mut list: Vec<Value> = self.prefs.get("enabledPlugins").and_then(|v| v.as_array().cloned()).unwrap_or_default();
                list.retain(|x| x.as_str() != Some(id));
                // switching on records the grant the consent dialog accepted (path + SHA-256 + permissions); without
                // one the plugin stays off. Switching off keeps the grant, so the same file comes back without asking.
                let mut grants = self.prefs.get("pluginGrants").and_then(|v| v.as_object().cloned()).unwrap_or_default();
                if value.as_bool() == Some(true) {
                    if let Some(g) = crate::take_plugin_grant(id) {
                        grants.insert(id.to_string(), g);
                        list.push(json!(id));
                    }
                }
                self.prefs.insert("enabledPlugins".into(), Value::Array(list));
                self.prefs.insert("pluginGrants".into(), Value::Object(grants));
                // API 1's bindings are read as grants until a grant replaces them
                if let Some(Value::Object(b)) = self.prefs.get_mut("pluginBindings") { if value.as_bool() == Some(true) { b.remove(id); } }
                self.save();
                crate::plugin_toggled();
                return;
            }
            _ => {
                self.prefs.insert(key.into(), value);
                self.save();
                return;
            }
        }
        cfg.save();
    }

    /// Bulwark hoverActions (default delete, star, markRead, archive).
    pub fn hover_actions(&self) -> Vec<String> {
        match self.prefs.get("hoverActions").and_then(|v| v.as_array()) {
            Some(a) => a.iter().filter_map(|x| x.as_str().map(String::from)).collect(),
            None => ["delete", "star", "markRead", "archive"].iter().map(|s| s.to_string()).collect(),
        }
    }

    /// Bulwark calendarWorkingDays (default Monday to Friday).
    pub fn working_days(&self) -> Vec<i64> {
        match self.prefs.get("calendarWorkingDays").and_then(|v| v.as_array()) {
            Some(a) => a.iter().filter_map(|x| x.as_i64()).filter(|d| (0..7).contains(d)).collect(),
            None => vec![1, 2, 3, 4, 5],
        }
    }

    pub fn is_inverted(&self, key: &str) -> bool {
        self.schema["tabs"].as_array().unwrap().iter()
            .flat_map(|t| t["rows"].as_array().unwrap().iter())
            .any(|r| r["key"] == key && r["invert"].as_bool() == Some(true))
    }

    /// Value to store for a select row's chosen index.
    pub fn select_value(&self, key: &str, index: i32) -> Value {
        if key.starts_with("ai.") || key.starts_with("desktop.") { return json!(index); }
        // a plugin's select setting: `set` turns the index into the option through the plugin's own schema
        if key.starts_with("pluginSetting:") { return json!(index); }
        if key == crate::trust::POLICY_KEY { return crate::trust::ContentPolicy::ALL.get(index as usize).map_or(Value::Null, |p| json!(p.as_str())); }
        if let Some(o) = option_override(key) { return o.get(index as usize).map(|x| x.0.clone()).unwrap_or(Value::Null); }
        for t in self.schema["tabs"].as_array().unwrap() {
            for r in t["rows"].as_array().unwrap() {
                if r["key"] == key {
                    let opts: Vec<&Value> = r["options"].as_array().map(|a| a.iter().filter(|o| !o[1].as_str().unwrap_or("").contains("${")).collect()).unwrap_or_default();
                    if let Some(o) = opts.get(index as usize) {
                        let v = o[0].as_str().unwrap_or("");
                        // keep numeric settings numeric
                        if r["default"].is_number() { if let Ok(n) = v.parse::<i64>() { return json!(n); } }
                        return json!(v);
                    }
                }
            }
        }
        Value::Null
    }
}

#[cfg(test)]
mod tests {
    #[test]
    fn a_plugin_select_setting_is_stored_as_the_chosen_option() {
        use serde_json::json;
        crate::TEST_PLUGIN_SCHEMAS.with(|m| m.borrow_mut().insert("demo".into(),
            json!({"mode": {"type": "select", "label": "Mode", "options": ["fast", "slow"], "default": "fast"}})));
        let mut s = super::Settings::with_prefs(Default::default());
        let mut cfg = crate::ai::Config::default();
        let v = s.select_value("pluginSetting:demo:mode", 1);
        assert_eq!(v, json!(1));
        s.set("pluginSetting:demo:mode", v, &mut cfg);
        assert_eq!(s.prefs["pluginSettings"]["demo"]["mode"], json!("slow"));
        // out of range: the last good value stays
        let v = s.select_value("pluginSetting:demo:mode", 7);
        s.set("pluginSetting:demo:mode", v, &mut cfg);
        assert_eq!(s.prefs["pluginSettings"]["demo"]["mode"], json!("slow"));
        // never exported, and an import keeps the local values
        assert!(s.export()["settings"].get("pluginSettings").is_none());
        s.import(&json!({"app": "barbican", "version": 1, "settings": {"pluginSettings": {"demo": {"mode": "fast"}}}})).unwrap();
        assert_eq!(s.prefs["pluginSettings"]["demo"]["mode"], json!("slow"));
        crate::TEST_PLUGIN_SCHEMAS.with(|m| m.borrow_mut().clear());
    }

    #[test]
    fn the_trusted_list_changes_through_the_helper_only_when_it_differs() {
        let mut p = serde_json::Map::new();
        assert!(!apply_trusted(&mut p, |_| {}));
        assert!(p.is_empty());
        assert!(apply_trusted(&mut p, |l| { l.push("Ann@Example.Test".into()); l.push("ann@example.test".into()); }));
        assert_eq!(p["trustedSenders"], json!(["ann@example.test"]));
        assert!(!apply_trusted(&mut p, |l| l.push("ann@example.test".into())));
        assert!(apply_trusted(&mut p, |l| l.clear()));
        assert_eq!(p["trustedSenders"], json!([]));
    }

    use super::*;

    #[test]
    fn the_settings_sync_row_is_live() {
        let s = Settings::with_prefs(Map::new());
        let rows = s.schema_rows("about");
        let r = rows.iter().find(|r| r.key == crate::settings_sync::DISABLED_KEY).expect("row");
        assert!(r.on, "on by default (Bulwark settingsSyncDisabled: false)");
        assert!(s.is_inverted(crate::settings_sync::DISABLED_KEY));
        assert!(r.desc.contains("Plugins"), "{}", r.desc);
    }

    #[test]
    fn password_and_sign_in_is_never_blank() {
        // without the server's account security rows the page has no rows of its own
        assert!(Settings::with_prefs(Map::new()).schema_rows("security").is_empty());
        let r = security_or_note(vec![]);
        assert_eq!((r.len(), r[0].label.as_str()), (1, "Nothing to manage on this server."));
        let some = vec![row("section", "", "Account security", "")];
        assert_eq!(security_or_note(some.clone()).len(), 1);
        assert_eq!(security_or_note(some)[0].label.as_str(), "Account security");
    }

    #[test]
    fn refresh_cached_data_is_a_button_under_fix_problems() {
        let rows = Settings::with_prefs(Map::new()).schema_rows("about");
        let i = rows.iter().position(|r| r.key == "cache.refresh").expect("row");
        assert_eq!(rows[i].label.as_str(), "Refresh cached data");
        assert_eq!(rows[i].kind.as_str(), "button");
        assert_eq!(rows[i].text.as_str(), "Refresh");
        // in Bulwark's "Fix problems" section
        assert!(rows[..i].iter().any(|r| r.kind == "section" && r.label == "Fix problems"));
    }

    #[test]
    fn the_tone_select_lists_bulwarks_tones() {
        use slint::Model as _;
        // the extracted schema has no options for it (Bulwark builds them from NOTIFICATION_SOUNDS)
        let rows = Settings::with_prefs(Map::new()).schema_rows("notifications");
        let r = rows.iter().find(|r| r.key == crate::notify::TONE_KEY).expect("row");
        let labels: Vec<String> = r.options.iter().map(|o| o.to_string()).collect();
        assert_eq!(labels, ["Beep", "Cheerful", "Involved", "Swift Gesture", "Relax"]);
        // nothing stored: Bulwark's default is selected
        assert_eq!(r.index, 2);
        assert!(!r.desc.contains("speaker"), "no preview button yet: {}", r.desc);
        // a stored choice is selected, and is stored under Bulwark's name
        let mut prefs = Map::new();
        prefs.insert(crate::notify::TONE_KEY.into(), json!("swift"));
        let s = Settings::with_prefs(prefs);
        let rows = s.schema_rows("notifications");
        let r = rows.iter().find(|r| r.key == crate::notify::TONE_KEY).unwrap();
        assert_eq!(r.index, 3);
        assert_eq!(s.select_value(crate::notify::TONE_KEY, 0), json!("default"));
        assert_eq!(s.select_value(crate::notify::TONE_KEY, 4), json!("relax"));
    }

    // One test so the APPDATA override can't race other tests touching config_dir().
    #[test]
    fn retention_parse_and_set() {
        assert_eq!(parse_retention(" 30 "), Retention::Days(30));
        assert_eq!(parse_retention("0"), Retention::Days(0));
        assert_eq!(parse_retention("99999"), Retention::Days(3650));
        assert_eq!(parse_retention("99999999999999999999"), Retention::Days(3650));
        assert_eq!(parse_retention("-5"), Retention::Days(0));
        assert_eq!(parse_retention(""), Retention::Unset);
        assert_eq!(parse_retention("abc"), Retention::Invalid);
        assert_eq!(parse_retention("1.5"), Retention::Invalid);
        assert!(is_retention_key("retentionJunkDays") && !is_retention_key("theme"));

        // the rows exist under Message list with the owner's labels, and show the stored number
        let mut prefs = Map::new();
        prefs.insert("retentionTrashDays".into(), json!(30));
        let s = Settings::with_prefs(prefs);
        let rows = s.schema_rows("message_list");
        let t = rows.iter().find(|r| r.key == "retentionTrashDays").unwrap();
        assert_eq!(t.label.as_str(), "Trash retention (days)");
        assert_eq!(t.text.as_str(), "30");
        assert_eq!(t.desc.as_str(), "Shown as a note in that folder. Your server's retention policy decides; 0 hides the note.");
        let j = rows.iter().find(|r| r.key == "retentionJunkDays").unwrap();
        assert_eq!(j.label.as_str(), "Junk retention (days)");
        assert_eq!(j.text.as_str(), "");
        // what the folder line reads back
        assert_eq!(s.int("retentionTrashDays", 0), 30);
        assert_eq!(s.int("retentionJunkDays", 0), 0);
    }

    #[test]
    fn import_export_reset_roundtrip() {
        let _files = TEST_FILES.lock().unwrap_or_else(|e| e.into_inner());
        let dir = std::env::temp_dir().join(format!("barbican-settings-test-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        #[allow(unused_unsafe)]
        unsafe { std::env::set_var("APPDATA", &dir); }
        let mut s = Settings { schema: schema(), prefs: Default::default() };

        // Bulwark-shaped: flat settings object.
        s.import(&json!({"theme": "dark", "showPreview": false})).unwrap();
        assert_eq!(s.str("theme", ""), "dark");
        assert!(!s.bool("showPreview", true));
        assert!(!crate::config_dir().join("settings.json").exists());

        // Bulwark's v14 migration: an older false nobody chose turns on; a chosen one stays
        s.import(&json!({"trustedSendersAddressBook": false})).unwrap();
        assert!(s.bool("trustedSendersAddressBook", false));
        s.import(&json!({"trustedSendersAddressBook": false, "explicitSettings": ["trustedSendersAddressBook"]})).unwrap();
        assert!(!s.bool("trustedSendersAddressBook", true));

        // Bulwark's v9 tone migration: the old `default` (beep) nobody chose becomes Involved;
        // a beep the file records as chosen stays, and Barbican's own export is not migrated
        s.import(&json!({"notificationSoundChoice": "default"})).unwrap();
        assert_eq!(s.str(crate::notify::TONE_KEY, ""), "involved");
        s.import(&json!({"notificationSoundChoice": "default", "explicitSettings": ["notificationSoundChoice"]})).unwrap();
        assert_eq!(s.str(crate::notify::TONE_KEY, ""), "default");
        let own = json!({"app": "barbican", "settings": {"notificationSoundChoice": "default"}});
        s.import(&own).unwrap();
        assert_eq!(s.str(crate::notify::TONE_KEY, ""), "default");

        // Bulwark-shaped with a "settings" wrapper.
        s.import(&json!({"version": 2, "settings": {"theme": "light"}})).unwrap();
        assert_eq!(s.str("theme", ""), "light");
        assert!(s.prefs.get("showPreview").is_none());

        // Barbican export, including the desktop file.
        let exp = json!({"app": "barbican", "version": 1, "settings": {"density": "compact"}, "desktop": {"ai_model": "x"}});
        s.import(&exp).unwrap();
        assert_eq!(s.str("density", ""), "compact");
        let desktop: Value = serde_json::from_str(&std::fs::read_to_string(crate::config_dir().join("settings.json")).unwrap()).unwrap();
        assert_eq!(desktop["ai_model"], "x");
        let reloaded = Settings::load();
        assert_eq!(reloaded.str("density", ""), "compact");
        let out = s.export();
        assert_eq!(out["app"], "barbican");
        assert_eq!(out["settings"]["density"], "compact");
        assert_eq!(out["desktop"]["ai_model"], "x");

        // Rejects non-objects.
        assert!(s.import(&json!([1, 2])).is_err());
        assert!(s.import(&json!("nope")).is_err());
        assert_eq!(s.str("density", ""), "compact");

        // Plugin consent is not imported: with no local consent, the file's allow-list and bindings are dropped.
        s.import(&json!({"theme": "light", "enabledPlugins": ["spam-score"], "pluginBindings": {"spam-score": "C:/p/a.dll|abc"},
            "pluginGrants": {"spam-score": {"id": "spam-score", "path": "C:/p/a.dll", "sha256": "abc", "granted": ["email:read"]}}})).unwrap();
        assert_eq!(s.str("theme", ""), "light");
        assert!(s.prefs.get("enabledPlugins").is_none() && s.prefs.get("pluginBindings").is_none() && s.prefs.get("pluginGrants").is_none());
        assert!(Settings::load().prefs.get("enabledPlugins").is_none());

        // Local consent survives an import instead of switching the user's plugins off.
        s.prefs.insert("enabledPlugins".into(), json!(["local-plugin"]));
        s.prefs.insert("pluginBindings".into(), json!({"local-plugin": "C:/p/local.dll|hash"}));
        s.import(&json!({"theme": "dark", "enabledPlugins": ["spam-score"], "pluginBindings": {"spam-score": "C:/p/a.dll|abc"}})).unwrap();
        assert_eq!(s.str("theme", ""), "dark");
        assert_eq!(s.prefs["enabledPlugins"], json!(["local-plugin"]));
        assert_eq!(s.prefs["pluginBindings"], json!({"local-plugin": "C:/p/local.dll|hash"}));
        let reloaded = Settings::load();
        assert_eq!(reloaded.prefs["enabledPlugins"], json!(["local-plugin"]));
        assert_eq!(reloaded.prefs["pluginBindings"], json!({"local-plugin": "C:/p/local.dll|hash"}));

        // sign-out forgets only the trusted senders, on the live settings and in the file
        let live = std::sync::Arc::new(std::sync::Mutex::new(Settings { schema: schema(), prefs: Default::default() }));
        live.lock().unwrap().remember("trustedSenders", json!(["a@x.test"]));
        live.lock().unwrap().remember("externalContentPolicy", json!("block"));
        register_live(live.clone());
        forget_trusted_senders();
        assert_eq!(live.lock().unwrap().prefs, json!({"externalContentPolicy": "block"}).as_object().unwrap().clone());
        // a later save from the live settings does not bring the list back
        live.lock().unwrap().remember("theme", json!("dark"));
        let on_disk = Settings::load();
        assert!(on_disk.prefs.get("trustedSenders").is_none());
        assert_eq!(on_disk.str("externalContentPolicy", ""), "block");
        assert_eq!(on_disk.str("theme", ""), "dark");
        // a value put from elsewhere (the tag list) goes through the live settings: their next save keeps it
        Settings::put("emailKeywords", Some(json!([{"id": "work", "label": "Work"}])));
        assert_eq!(live.lock().unwrap().prefs["emailKeywords"], json!([{"id": "work", "label": "Work"}]));
        live.lock().unwrap().remember("theme", json!("light"));
        assert_eq!(Settings::load().prefs["emailKeywords"], json!([{"id": "work", "label": "Work"}]));
        Settings::put("emailKeywords", None);
        assert!(live.lock().unwrap().prefs.get("emailKeywords").is_none());
        if let Ok(mut l) = LIVE.lock() { *l = None; }

        s.reset();
        assert!(s.prefs.is_empty());
        assert!(!crate::config_dir().join("settings.json").exists());
        assert!(Settings::load().prefs.is_empty());
        let _ = std::fs::remove_dir_all(&dir);
    }
}

/// Bulwark's timeFormat pref ("12h" | "24h"), read from the cached prefs without copying them, for
/// hot paths (admin tables, dashboard tooltips).
pub fn h12() -> bool {
    current().get("timeFormat").and_then(|v| v.as_str()) == Some("12h")
}

/// The running program's settings (the one object every settings change goes through), so a change
/// made from another thread can be made on it instead of on the file.
static LIVE: std::sync::Mutex<Option<std::sync::Arc<std::sync::Mutex<Settings>>>> = std::sync::Mutex::new(None);

#[cfg(test)]
thread_local! {
    /// Tests: how often this thread went through the live settings (and so took their lock).
    pub static LIVE_USES: std::cell::Cell<u32> = const { std::cell::Cell::new(0) };
}

/// Called once at start with the program's settings.
pub fn register_live(s: std::sync::Arc<std::sync::Mutex<Settings>>) {
    if let Ok(mut l) = LIVE.lock() { *l = Some(s); }
}

/// Remove the trusted senders from `prefs`; whether there were any.
fn drop_trusted(prefs: &mut Map<String, Value>) -> bool {
    let tags = prefs.remove(crate::trustbook::TAGS_KEY).is_some();
    prefs.remove(crate::trust::TRUSTED_KEY).is_some() || tags
}

/// Sign-out of the last account: forget the trusted senders, keep every other preference. Done on
/// the live settings and saved through them (the writer's lock and sequence), so the program's
/// next save cannot bring the list back; without live settings (not started) on a fresh load.
pub fn forget_trusted_senders() {
    let live = LIVE.lock().ok().and_then(|l| l.clone());
    match live {
        Some(s) => {
            let mut s = s.lock().unwrap_or_else(|e| e.into_inner());
            if drop_trusted(&mut s.prefs) { s.save(); }
        }
        None => {
            let mut s = Settings::load();
            if drop_trusted(&mut s.prefs) { s.save(); }
        }
    }
}

/// Change the device's trusted list from another thread: the live settings under their lock (the
/// writer's lock and sequence), or a fresh load when not started. `f` sees the list as it is now,
/// so a change never overwrites an edit made meanwhile.
pub fn trusted_local_update(f: impl FnOnce(&mut Vec<String>)) {
    trusted_state_update(|l, _| f(l));
}

/// Like [`trusted_local_update`], with the account tags of the list (`trustedSendersFrom`) too.
pub fn trusted_state_update(f: impl FnOnce(&mut Vec<String>, &mut Map<String, Value>)) {
    let live = LIVE.lock().ok().and_then(|l| l.clone());
    match live {
        Some(s) => {
            let mut s = s.lock().unwrap_or_else(|e| e.into_inner());
            if apply_trusted_state(&mut s.prefs, f) { s.save(); }
        }
        None => {
            let mut s = Settings::load();
            if apply_trusted_state(&mut s.prefs, f) { s.save(); }
        }
    }
}

/// Run `f` on the trusted list in `prefs`; whether the list or its tags changed (and was stored).
/// The list is stored lower-cased without duplicates, and a tag of an address that left it goes.
fn apply_trusted_state(prefs: &mut Map<String, Value>, f: impl FnOnce(&mut Vec<String>, &mut Map<String, Value>)) -> bool {
    let before = crate::trust::trusted_list(prefs);
    let tags_before = prefs.get(crate::trustbook::TAGS_KEY).and_then(|v| v.as_object().cloned()).unwrap_or_default();
    let mut list = before.clone();
    let mut tags = tags_before.clone();
    f(&mut list, &mut tags);
    let mut seen: Vec<String> = vec![];
    for a in list { let k = crate::trust::sender_key(&a); if !k.is_empty() && !seen.contains(&k) { seen.push(k); } }
    tags.retain(|a, _| seen.contains(a));
    if seen == before && tags == tags_before { return false; }
    prefs.insert(crate::trust::TRUSTED_KEY.into(), crate::trust::trusted_json(&seen));
    if tags.is_empty() { prefs.remove(crate::trustbook::TAGS_KEY); } else { prefs.insert(crate::trustbook::TAGS_KEY.into(), Value::Object(tags)); }
    true
}

#[cfg(test)]
fn apply_trusted(prefs: &mut Map<String, Value>, f: impl FnOnce(&mut Vec<String>)) -> bool {
    apply_trusted_state(prefs, |l, _| f(l))
}

/// Write one Bulwark-keyed pref (a value with no settings row, such as sharedCalendarColors).
pub fn set_pref(key: &str, value: Value) {
    update_pref(key, |_| value);
}

/// Change one pref from its current value (`f` gets it, or None): on the live settings under their
/// lock when the program runs, so the settings page's next save cannot bring the old value back,
/// else on a fresh load.
pub fn update_pref(key: &str, f: impl FnOnce(Option<&Value>) -> Value) {
    #[cfg(test)]
    LIVE_USES.with(|n| n.set(n.get() + 1));
    let live = LIVE.lock().ok().and_then(|l| l.clone());
    match live {
        Some(s) => {
            let mut s = s.lock().unwrap_or_else(|e| e.into_inner());
            let v = f(s.prefs.get(key));
            s.prefs.insert(key.into(), v);
            s.save();
        }
        None => {
            let mut s = Settings::load();
            let v = f(s.prefs.get(key));
            s.prefs.insert(key.into(), v);
            s.save();
        }
    }
}

/// Run `f` on the live settings under their lock (the program's one settings object), or on a
/// fresh load when not started, so its saves never race the settings page's.
pub fn with_live(f: impl FnOnce(&mut Settings)) {
    #[cfg(test)]
    LIVE_USES.with(|n| n.set(n.get() + 1));
    let live = LIVE.lock().ok().and_then(|l| l.clone());
    match live {
        Some(s) => f(&mut s.lock().unwrap_or_else(|e| e.into_inner())),
        None => f(&mut Settings::load()),
    }
}

#[cfg(test)]
mod forget_package_tests {
    use serde_json::json;

    #[test]
    fn uninstalling_a_package_keeps_a_dropped_in_plugins_grant() {
        let dir = std::path::Path::new("C:/cfg/plugins/spam-score");
        let mut prefs = json!({
            "enabledPlugins": ["spam-score", "other"],
            "pluginGrants": {"spam-score": {"id": "spam-score", "path": "C:/cfg/plugins/spam.dll", "sha256": "h"}},
            "pluginBindings": {"spam-score": "C:/cfg/plugins/spam.dll|h"}
        }).as_object().unwrap().clone();
        super::forget_package(&mut prefs, "spam-score", dir);
        assert!(prefs["pluginGrants"].get("spam-score").is_some() && prefs["pluginBindings"].get("spam-score").is_some());
        assert_eq!(prefs["enabledPlugins"], json!(["spam-score", "other"]), "still switched on: its own grant is left");
        // the package's own grant and binding go, and it is switched off
        prefs["pluginGrants"]["spam-score"]["path"] = json!("C:/cfg/plugins/spam-score/spam.dll");
        prefs["pluginBindings"]["spam-score"] = json!("C:/cfg/plugins/spam-score/spam.dll|h");
        super::forget_package(&mut prefs, "spam-score", dir);
        assert!(prefs["pluginGrants"].get("spam-score").is_none() && prefs["pluginBindings"].get("spam-score").is_none());
        assert_eq!(prefs["enabledPlugins"], json!(["other"]));
        // no folder given: nothing is forgotten
        prefs["pluginGrants"]["other"] = json!({"id": "other", "path": "", "sha256": "h"});
        super::forget_package(&mut prefs, "other", std::path::Path::new(""));
        assert!(prefs["pluginGrants"].get("other").is_some());
    }
}

#[cfg(test)]
mod cache_tests {
    use super::*;
    use serde_json::json;

    fn reads() -> u32 { DISK_READS.with(|n| n.get()) }

    /// What the scope's prefs file holds now.
    fn on_disk(scope: &CacheScope) -> Map<String, Value> { file_prefs(scope.file()) }

    #[test]
    fn a_load_after_a_save_comes_from_memory() {
        let scope = CacheScope::new();
        let mut s = Settings::load(); // fills the cache from the (missing) file: one read
        assert!(s.prefs.is_empty());
        let before = reads();
        s.remember("density", json!("compact"));
        s.remember("timeFormat", json!("12h"));
        assert_eq!(Settings::load().str("density", ""), "compact");
        assert_eq!(str_pref("density", ""), "compact");
        assert!(h12());
        assert!(std::sync::Arc::ptr_eq(&snapshot(), &snapshot()), "a load shares the cached values instead of parsing them again");
        assert_eq!(reads(), before, "no load went back to the file");
        // and the file has the same values
        assert_eq!(on_disk(&scope)["density"], "compact");
        // a value changed on the file behind the program's back is not read again
        std::fs::write(scope.file(), r#"{"density":"comfortable"}"#).unwrap();
        assert_eq!(Settings::load().str("density", ""), "compact");
        assert_eq!(reads(), before + 1, "only the check above read the file");
    }

    #[test]
    fn every_writer_is_seen_by_the_next_load() {
        let _files = TEST_FILES.lock().unwrap_or_else(|e| e.into_inner());
        let scope = CacheScope::new();
        let _ = Settings::load();
        let before = reads();
        let seen = |key: &str, want: Value| {
            assert_eq!(Settings::load().prefs.get(key).cloned().unwrap_or(Value::Null), want, "load after writing {key}");
            assert_eq!(snapshot().get(key).cloned().unwrap_or(Value::Null), want, "snapshot after writing {key}");
        };

        // save and remember on a loaded copy
        let mut s = Settings::load();
        s.prefs.insert("a".into(), json!(1));
        s.save();
        seen("a", json!(1));
        Settings::load().remember("b", json!(2));
        seen("b", json!(2));
        // a settings row
        Settings::load().set("density", json!("compact"), &mut crate::ai::Config::default());
        seen("density", json!("compact"));
        // import (Bulwark's flat file) replaces the values
        let mut s = Settings::load();
        s.import(&json!({"theme": "light"})).unwrap();
        seen("theme", json!("light"));
        seen("a", Value::Null);
        // the helpers outside the settings page (fresh load: no live settings registered)
        Settings::put("emailKeywords", Some(json!([{"id": "work", "label": "Work"}])));
        seen("emailKeywords", json!([{"id": "work", "label": "Work"}]));
        assert_eq!(crate::tags::defs().len(), 1);
        Settings::put("emailKeywords", None);
        seen("emailKeywords", Value::Null);
        set_pref("sharedCalendarColors", json!({"c1": "#ff0000"}));
        seen("sharedCalendarColors", json!({"c1": "#ff0000"}));
        update_pref("counter", |v| json!(v.and_then(|v| v.as_i64()).unwrap_or(0) + 1));
        update_pref("counter", |v| json!(v.and_then(|v| v.as_i64()).unwrap_or(0) + 1));
        seen("counter", json!(2));
        // settings sync writes what it pulled through with_live
        with_live(|s| { s.prefs.insert("pulled".into(), json!(true)); s.save(); });
        seen("pulled", json!(true));
        assert!(bool_pref("pulled", false));
        // the trusted list, and the sign-out that forgets it
        trusted_local_update(|l| l.push("Someone@Example.test".into()));
        seen("trustedSenders", crate::trust::trusted_json(&["someone@example.test".to_string()]));
        trusted_state_update(|l, _| l.clear());
        trusted_local_update(|l| l.push("other@example.test".into()));
        forget_trusted_senders();
        seen("trustedSenders", Value::Null);
        // the live settings: a change made on them is what the next load sees
        let live = std::sync::Arc::new(std::sync::Mutex::new(Settings::load()));
        register_live(live.clone());
        live.lock().unwrap().remember("liveKey", json!("x"));
        seen("liveKey", json!("x"));
        set_pref("viaLive", json!(3));
        seen("viaLive", json!(3));
        trusted_local_update(|l| l.push("live@example.test".into()));
        forget_trusted_senders();
        seen("trustedSenders", Value::Null);
        if let Ok(mut l) = LIVE.lock() { *l = None; }
        // flush (a no-op in tests: their saves are written at once) keeps the values
        Settings::flush();
        seen("viaLive", json!(3));
        // reset empties them
        Settings::load().reset();
        assert!(Settings::load().prefs.is_empty());
        assert!(snapshot().is_empty());

        assert_eq!(reads(), before, "every load came from memory");
        assert!(on_disk(&scope).is_empty(), "and the file has what was last saved");
    }

    #[test]
    fn a11y_follows_each_save() {
        let _scope = CacheScope::new();
        let mut s = Settings::load();
        assert!(crate::a11y::A11y::load().single_keys);
        s.remember("singleKeyShortcuts", json!(false));
        assert!(!crate::a11y::A11y::load().single_keys);
        assert!(!crate::a11y::A11y::load().single_keys);
        s.remember("singleKeyShortcuts", json!(true));
        assert!(crate::a11y::A11y::load().single_keys);
        assert_eq!(crate::a11y::System::detect(), crate::a11y::System::detect());
    }

    #[test]
    fn the_cache_does_not_outlive_its_scope_or_cross_threads() {
        {
            let _scope = CacheScope::new();
            Settings::load().remember("leak", json!("yes"));
            assert_eq!(str_pref("leak", ""), "yes");
            // another thread has no cache: it reads its own file (the process's test prefs.json)
            std::thread::spawn(|| {
                let before = reads();
                let _ = Settings::load();
                let _ = Settings::load();
                assert_eq!(reads(), before + 2, "without a scope every load reads the file");
            }).join().unwrap();
        }
        let scope = CacheScope::new();
        let before = reads();
        assert_eq!(str_pref("leak", ""), "", "a new scope starts from its own file");
        assert_eq!(reads(), before + 1);
        assert!(!scope.file().exists());
    }
}

/// The save state machine the program runs (begin, background write, flush), with writers that
/// record what they were given instead of touching a file.
#[cfg(test)]
mod save_order_tests {
    use super::*;
    use serde_json::json;
    use std::cell::RefCell;

    fn prefs(v: Value) -> Snapshot { std::sync::Arc::new(v.as_object().unwrap().clone()) }
    fn cell() -> CacheCell { std::sync::Mutex::new(None) }
    fn begin(c: &CacheCell, p: std::path::PathBuf, s: Snapshot) -> u64 { begin_save(c, p, s).0 }
    fn file() -> std::path::PathBuf { std::path::PathBuf::from("prefs.json") }
    fn pending(c: &CacheCell) -> bool { lock(c).as_ref().is_some_and(|c| c.pending) }
    fn cached(c: &CacheCell) -> Value { Value::Object((*lock(c).as_ref().unwrap().prefs).clone()) }

    #[test]
    fn two_saves_written_out_of_order_keep_the_newest() {
        let written = RefCell::new(vec![]);
        let w = |_: &std::path::Path, m: &Map<String, Value>| { written.borrow_mut().push(Value::Object(m.clone())); Ok(()) };
        let c = cell();
        let first = begin(&c, file(), prefs(json!({"v": 1})));
        let second = begin(&c, file(), prefs(json!({"v": 2})));
        assert_eq!(cached(&c), json!({"v": 2}), "a load sees the newest save at once");
        // the second save's thread runs first, then the first one's
        assert!(write_save(&c, second, &w));
        assert!(!write_save(&c, first, &w), "the older save is superseded");
        assert_eq!(*written.borrow(), vec![json!({"v": 2})]);
        assert!(!pending(&c));

        // in order: the older write finds itself superseded, the newer one writes
        let c = cell();
        written.borrow_mut().clear();
        let first = begin(&c, file(), prefs(json!({"v": 1})));
        let second = begin(&c, file(), prefs(json!({"v": 2})));
        assert!(!write_save(&c, first, &w));
        assert!(pending(&c), "the newest save has not landed yet");
        assert!(write_save(&c, second, &w));
        assert_eq!(*written.borrow(), vec![json!({"v": 2})]);
    }

    #[test]
    fn flush_while_a_write_is_pending_writes_it_once() {
        let written = RefCell::new(vec![]);
        let w = |_: &std::path::Path, m: &Map<String, Value>| { written.borrow_mut().push(Value::Object(m.clone())); Ok(()) };
        let c = cell();
        let seq = begin(&c, file(), prefs(json!({"v": 1})));
        assert!(flush_save(&c, &w));
        assert!(!pending(&c));
        // the background write comes after the flush: nothing left to write
        assert!(!write_save(&c, seq, &w));
        assert_eq!(*written.borrow(), vec![json!({"v": 1})]);
        assert_eq!(cached(&c), json!({"v": 1}));
    }

    #[test]
    fn flush_after_the_write_landed_writes_nothing() {
        let written = RefCell::new(0);
        let w = |_: &std::path::Path, _: &Map<String, Value>| { *written.borrow_mut() += 1; Ok(()) };
        let c = cell();
        assert!(!flush_save(&c, &w), "nothing saved, nothing to flush");
        let seq = begin(&c, file(), prefs(json!({"v": 1})));
        assert!(write_save(&c, seq, &w));
        assert!(!flush_save(&c, &w));
        assert_eq!(*written.borrow(), 1);
    }

    #[test]
    fn a_failed_write_stays_pending_for_the_flush() {
        let c = cell();
        let seq = begin(&c, file(), prefs(json!({"v": 1})));
        let fail = |_: &std::path::Path, _: &Map<String, Value>| Err(std::io::Error::other("disk full"));
        assert!(!write_save(&c, seq, &fail));
        assert!(pending(&c));
        assert_eq!(cached(&c), json!({"v": 1}), "loads keep the saved values");
        let written = RefCell::new(vec![]);
        let w = |_: &std::path::Path, m: &Map<String, Value>| { written.borrow_mut().push(Value::Object(m.clone())); Ok(()) };
        assert!(flush_save(&c, &w));
        assert_eq!(*written.borrow(), vec![json!({"v": 1})]);
        assert!(!pending(&c));
    }

    #[test]
    fn the_newest_save_is_what_lands_on_disk() {
        // the real writer, through Settings::save and flush
        let scope = CacheScope::new();
        for i in 0..5 { Settings::load().remember("n", json!(i)); }
        Settings::flush();
        assert_eq!(file_prefs(scope.file())["n"], 4);
    }
}

/// What a readable prefs file holds (tests).
#[cfg(test)]
fn file_prefs(p: &std::path::Path) -> Map<String, Value> {
    match read_file(p) { Read::Prefs(m) => m, _ => panic!("{} is not a readable prefs file", p.display()) }
}

/// A prefs file that cannot be read is never replaced by the defaults.
#[cfg(test)]
mod bad_file_tests {
    use super::*;
    use serde_json::json;

    fn reads() -> u32 { DISK_READS.with(|n| n.get()) }

    #[test]
    fn a_missing_file_is_no_prefs_and_leaves_no_copy() {
        let scope = CacheScope::new();
        assert!(Settings::load().prefs.is_empty());
        let before = reads();
        let _ = Settings::load();
        assert_eq!(reads(), before, "an empty start is kept");
        assert!(bad_copies(scope.file()).is_empty());
        assert!(!unsaved_session());
    }

    #[test]
    fn each_damaged_file_is_kept_before_a_save_replaces_it() {
        let scope = CacheScope::new();
        std::fs::write(scope.file(), "{\"theme\": \"dark\"").unwrap();
        assert!(Settings::load().prefs.is_empty(), "the session starts from the defaults");
        let copies = bad_copies(scope.file());
        assert_eq!(copies.len(), 1);
        assert_eq!(std::fs::read_to_string(&copies[0]).unwrap(), "{\"theme\": \"dark\"");
        let before = reads();
        let _ = Settings::load();
        assert_eq!(reads(), before, "and keeps them: reading the same file again gives the same");
        assert!(!unsaved_session(), "a damaged file that was kept may be saved over");
        Settings::load().remember("density", json!("compact"));
        assert_eq!(file_prefs(scope.file())["density"], "compact");
        assert_eq!(std::fs::read_to_string(&copies[0]).unwrap(), "{\"theme\": \"dark\"", "the copy is untouched");
        // a later damaged file gets a copy of its own (also within the same second)
        std::fs::write(scope.file(), "[1, 2]").unwrap();
        assert!(matches!(read_file(scope.file()), Read::Damaged));
        let copies = bad_copies(scope.file());
        assert_eq!(copies.len(), 2);
        let texts: Vec<String> = copies.iter().map(|c| std::fs::read_to_string(c).unwrap()).collect();
        assert!(texts.contains(&"{\"theme\": \"dark\"".to_string()) && texts.contains(&"[1, 2]".to_string()), "{texts:?}");
    }

    #[test]
    fn an_unreadable_file_at_the_start_is_never_written() {
        let scope = CacheScope::new();
        // a folder where the file should be: reading it fails with something other than NotFound
        std::fs::create_dir_all(scope.file()).unwrap();
        let before = reads();
        assert!(Settings::load().prefs.is_empty());
        assert_eq!(reads(), before + READ_TRIES, "tried a few times at the start");
        assert!(unsaved_session());
        // the session goes on from the defaults, in memory only, and never reads again
        Settings::load().remember("theme", json!("light"));
        assert_eq!(str_pref("theme", ""), "light");
        assert_eq!(reads(), before + READ_TRIES);
        std::fs::remove_dir(scope.file()).unwrap();
        Settings::load().remember("theme", json!("dark"));
        Settings::flush();
        assert!(!scope.file().exists(), "neither a save nor the flush wrote the file");
        assert!(bad_copies(scope.file()).is_empty());
    }

    /// The real case: another program holds the file open (no sharing) while the session starts.
    #[cfg(windows)]
    #[test]
    fn a_file_locked_at_the_start_keeps_its_content() {
        use std::os::windows::fs::OpenOptionsExt;
        let scope = CacheScope::new();
        let original = r#"{"theme":"dark","density":"compact"}"#;
        std::fs::write(scope.file(), original).unwrap();
        let held = std::fs::OpenOptions::new().read(true).share_mode(0).open(scope.file()).unwrap();
        assert!(Settings::load().prefs.is_empty());
        assert!(unsaved_session());
        Settings::load().remember("theme", json!("light"));
        drop(held); // the other program lets go: the file is readable again
        Settings::load().remember("density", json!("regular"));
        Settings::flush();
        assert_eq!(std::fs::read_to_string(scope.file()).unwrap(), original, "the real settings are still there");
        assert_eq!(str_pref("theme", ""), "light", "the session keeps its changes in memory");
        assert!(bad_copies(scope.file()).is_empty());
    }

    #[test]
    fn writes_leave_no_temporary_file() {
        let scope = CacheScope::new();
        let mut m = Map::new();
        m.insert("a".into(), json!(1));
        write_file(scope.file(), &m).unwrap();
        m.insert("a".into(), json!(2));
        write_file(scope.file(), &m).unwrap();
        assert_eq!(file_prefs(scope.file())["a"], 2);
        let name = scope.file().file_name().unwrap().to_string_lossy().to_string();
        let left: Vec<String> = std::fs::read_dir(scope.file().parent().unwrap()).unwrap().flatten()
            .map(|e| e.file_name().to_string_lossy().to_string())
            .filter(|n| n.starts_with(&name) && n.ends_with(".tmp")).collect();
        assert!(left.is_empty(), "{left:?}");
    }

    #[test]
    fn the_first_read_removes_temporary_files_of_earlier_runs() {
        let scope = CacheScope::new();
        let dir = scope.file().parent().unwrap().to_path_buf();
        let name = scope.file().file_name().unwrap().to_string_lossy().to_string();
        let other = dir.join(format!("{name}.{}-7.tmp", std::process::id().wrapping_add(1)));
        let fresh = dir.join(format!("{name}.{}-8.tmp", std::process::id().wrapping_add(1)));
        let mine = dir.join(format!("{name}.{}-7.tmp", std::process::id()));
        let unrelated = dir.join(format!("{name}.notes.tmp"));
        for f in [&other, &fresh, &mine, &unrelated] { std::fs::write(f, "{}").unwrap(); }
        // the leftover is two minutes old; `fresh` may be another instance's write under way
        let past = std::time::SystemTime::now() - std::time::Duration::from_secs(120);
        std::fs::OpenOptions::new().write(true).open(&other).unwrap().set_modified(past).unwrap();
        let _ = Settings::load();
        assert!(!other.exists(), "another run's leftover goes");
        assert!(fresh.exists(), "a file written in the last minute stays");
        assert!(mine.exists(), "this run's may be in use");
        assert!(unrelated.exists(), "a name that is not ours stays");
        for f in [&fresh, &mine, &unrelated] { let _ = std::fs::remove_file(f); }
    }

    #[test]
    fn scopes_nest() {
        let outer = CacheScope::new();
        Settings::load().remember("where", json!("outer"));
        {
            let inner = CacheScope::new();
            assert_ne!(inner.file(), outer.file());
            assert_eq!(str_pref("where", ""), "", "the inner scope has its own file and cache");
            Settings::load().remember("where", json!("inner"));
        }
        assert_eq!(str_pref("where", ""), "outer", "the outer scope's cache is back");
        assert_eq!(path(), outer.file(), "and its file");
    }
}
