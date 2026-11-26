//! The assistant's own id for each login (docs/AI-ASSISTANT-SPEC.md 2.0): a random 128-bit id per
//! login, kept in a small local file beside the settings (`ai-logins.json`), written whole or not
//! at all. It is not a secret and never leaves this computer except as the id itself, so it is
//! keyed by the app's own account key ([`crate::jmap::Client::state_key`] or
//! [`crate::identities::account_key`]) and only the id is ever sent to the provider.
//!
//! An id is made the first time the assistant needs one for that login, and kept. The file is read
//! once per run; a write that fails leaves the map in memory as it is, so an id stays the same for
//! the rest of the run.
use serde_json::{Map, Value};
use std::sync::{Arc, Mutex};

/// The file the map lives in, beside `prefs.json`.
const FILE: &str = "ai-logins.json";

/// The length of an id: 128 bits in hex.
pub const ID_LEN: usize = 32;

/// Whether `s` is one of our ids (lower-case hex, 128 bits). Anything else stored — a hand-edited
/// file, an address someone pasted in — is replaced by a new id.
fn is_id(s: &str) -> bool {
    s.len() == ID_LEN && s.bytes().all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
}

/// A new id: 128 random bits in hex.
fn new_id() -> String {
    let mut b = [0u8; 16];
    getrandom::getrandom(&mut b).expect("rng");
    b.iter().map(|x| format!("{x:02x}")).collect()
}

type Store = std::sync::Mutex<Option<Map<String, Value>>>;

fn path() -> std::path::PathBuf {
    #[cfg(test)]
    if let Some(p) = TEST_PATH.with(|p| p.borrow().clone()) {
        return p;
    }
    crate::config_dir().join(FILE)
}

fn cache() -> Arc<Store> {
    #[cfg(not(test))]
    {
        static C: std::sync::OnceLock<Arc<Store>> = std::sync::OnceLock::new();
        C.get_or_init(|| Arc::new(Mutex::new(None))).clone()
    }
    #[cfg(test)]
    {
        TEST_CACHE.with(|c| c.borrow().clone()).unwrap_or_else(|| Arc::new(Mutex::new(None)))
    }
}

/// The map as the file has it (empty for a missing or unreadable file).
fn read() -> Map<String, Value> {
    std::fs::read(path())
        .ok()
        .and_then(|b| serde_json::from_slice::<Value>(&b).ok())
        .and_then(|v| v.as_object().cloned())
        .unwrap_or_default()
}

/// Write the map whole or not at all: a temporary file beside it, renamed over it. A failure
/// leaves the old file (or none) and changes nothing.
fn write(map: &Map<String, Value>) {
    let p = path();
    if let Some(dir) = p.parent() {
        let _ = std::fs::create_dir_all(dir);
    }
    let Ok(text) = serde_json::to_vec(&Value::Object(map.clone())) else { return };
    let tmp = p.with_extension(format!("{}.tmp", std::process::id()));
    if std::fs::write(&tmp, &text).is_ok() && std::fs::rename(&tmp, &p).is_ok() {
        return;
    }
    let _ = std::fs::remove_file(&tmp);
}

/// The assistant's id for one login, made the first time it is needed and kept. `key` is
/// Barbican's own internal account key — the server, the login and the account, as
/// `state_key`/`account_key` write it — so two logins with the same account and message ids never
/// share one. The id is random: nothing of the address can be read from it or checked against it.
pub fn assistant_login_id(key: &str) -> String {
    let cell = cache();
    let mut guard = cell.lock().unwrap_or_else(|e| e.into_inner());
    let map = guard.get_or_insert_with(read);
    if let Some(id) = map.get(key).and_then(Value::as_str).filter(|s| is_id(s)) {
        return id.to_string();
    }
    let id = new_id();
    map.insert(key.to_string(), Value::String(id.clone()));
    write(map);
    id
}

#[cfg(test)]
thread_local! {
    /// Tests: the file of this thread's own map, so tests in parallel never share one.
    static TEST_PATH: std::cell::RefCell<Option<std::path::PathBuf>> = const { std::cell::RefCell::new(None) };
    /// Tests: this thread's own map while a [`TestScope`] is alive.
    static TEST_CACHE: std::cell::RefCell<Option<Arc<Store>>> = const { std::cell::RefCell::new(None) };
}

/// Tests: this thread's own file and map while the scope is alive; scopes nest and the previous
/// ones come back on drop.
#[cfg(test)]
pub(crate) struct TestScope {
    file: std::path::PathBuf,
    prev: Option<(Option<std::path::PathBuf>, Option<Arc<Store>>)>,
}

#[cfg(test)]
impl TestScope {
    pub(crate) fn new() -> TestScope {
        static N: std::sync::atomic::AtomicU32 = std::sync::atomic::AtomicU32::new(0);
        let n = N.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
        let _ = std::fs::create_dir_all(crate::config_dir());
        let file = crate::config_dir().join(format!("ai-logins-scope-{n}.json"));
        let _ = std::fs::remove_file(&file);
        let path = TEST_PATH.with(|t| t.replace(Some(file.clone())));
        let cell = TEST_CACHE.with(|t| t.replace(Some(Arc::new(Mutex::new(None)))));
        TestScope { file, prev: Some((path, cell)) }
    }

    /// The scope's file.
    pub(crate) fn file(&self) -> &std::path::Path {
        &self.file
    }

    /// Forget the in-memory map, as a restart does: the next call reads the file again.
    pub(crate) fn reload(&self) {
        if let Some(c) = TEST_CACHE.with(|t| t.borrow().clone()) {
            *c.lock().unwrap_or_else(|e| e.into_inner()) = None;
        }
    }
}

#[cfg(test)]
impl Drop for TestScope {
    fn drop(&mut self) {
        if let Some((path, cell)) = self.prev.take() {
            TEST_PATH.with(|t| *t.borrow_mut() = path);
            TEST_CACHE.with(|t| *t.borrow_mut() = cell);
        }
        let _ = std::fs::remove_file(&self.file);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const WORK: &str = "https://mail.work.example.test\nann@example.test\na1";
    const HOME: &str = "https://mail.home.example.test\nann@example.test\na1";

    #[test]
    fn an_id_is_made_once_and_kept_across_a_reload() {
        let scope = TestScope::new();
        let id = assistant_login_id(WORK);
        assert_eq!(id.len(), ID_LEN);
        assert!(is_id(&id), "{id}");
        // on disk, keyed by the login key
        let saved: Value = serde_json::from_slice(&std::fs::read(scope.file()).unwrap()).unwrap();
        assert_eq!(saved[WORK].as_str(), Some(id.as_str()));
        // asked again in the same run: the same id, and only one record
        assert_eq!(assistant_login_id(WORK), id);
        assert_eq!(serde_json::from_slice::<Value>(&std::fs::read(scope.file()).unwrap()).unwrap().as_object().unwrap().len(), 1);
        // a restart reads the file: the id is the one that was made
        scope.reload();
        assert_eq!(assistant_login_id(WORK), id);
    }

    #[test]
    fn two_logins_get_different_ids() {
        let _scope = TestScope::new();
        let work = assistant_login_id(WORK);
        let home = assistant_login_id(HOME);
        let other = assistant_login_id("https://mail.work.example.test\nbob@example.test\na1");
        assert_ne!(work, home);
        assert_ne!(work, other);
        assert_ne!(home, other);
        assert_eq!(assistant_login_id(HOME), home);
    }

    #[test]
    fn nothing_of_the_key_is_in_the_id() {
        let _scope = TestScope::new();
        let id = assistant_login_id(WORK);
        // hex only: no address, no server, no '@', no separator
        assert!(id.chars().all(|c| c.is_ascii_hexdigit() && !c.is_ascii_uppercase()), "{id}");
        assert!(!id.contains("ann") && !id.contains("example") && !id.contains('@') && !id.contains('\n'));
        // a stored value that is not an id (an address pasted in) is replaced, not used
        let scope = TestScope::new();
        let mut m = Map::new();
        m.insert(WORK.to_string(), Value::String("ann@example.test".into()));
        std::fs::write(scope.file(), serde_json::to_vec(&Value::Object(m)).unwrap()).unwrap();
        let fresh = assistant_login_id(WORK);
        assert!(is_id(&fresh), "{fresh}");
        assert_ne!(fresh, "ann@example.test");
        assert_eq!(assistant_login_id(WORK), fresh);
    }

    #[test]
    fn a_failed_save_keeps_the_id_in_this_run() {
        let scope = TestScope::new();
        let first = assistant_login_id(WORK);
        // every later write fails: the file's place is taken by a folder
        std::fs::remove_file(scope.file()).unwrap();
        std::fs::create_dir(scope.file()).unwrap();
        let home = assistant_login_id(HOME);
        assert!(is_id(&home));
        // nothing could be written ...
        assert!(std::fs::read_to_string(scope.file()).is_err());
        // ... but the ids stay what they were for the rest of the run
        assert_eq!(assistant_login_id(HOME), home);
        assert_eq!(assistant_login_id(WORK), first);
        std::fs::remove_dir(scope.file()).ok();
    }
}
