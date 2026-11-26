//! The assistant's own id for each login (docs/AI-ASSISTANT-SPEC.md 2.0): a random 128-bit id per
//! login, kept in a small local file beside the settings (`ai-logins.json`), written whole or not
//! at all. It is not a secret and never leaves this computer except as the id itself, so it is
//! keyed by [`login_key`] — the server address and the user — and only the id is ever sent to the
//! provider.
//!
//! An id is made the first time the assistant needs one for that login, and kept. The file is read
//! once per run; a write that fails leaves the map in memory as it is, so an id stays the same for
//! the rest of the run. Every write merges over the file as it is then, so another instance of
//! Barbican never loses its entries and an id it made is adopted rather than replaced.
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

/// The key the map is keyed by: the server address as [`crate::jmap::normalize_base`] gives it and
/// the user, one line each. It names a login, not a JMAP account (an account inside one login is
/// told apart by its account id in [`MessageRef`](super::context::MessageRef)), so a login keeps
/// one id whatever accounts it reaches.
pub fn login_key(base: &str, user: &str) -> String {
    format!("{}\n{}", crate::jmap::normalize_base(base), user.trim())
}

/// Write the map whole or not at all, merged over the file as it is now (another instance of
/// Barbican may have written entries since this run read it), and with the settings' own writer:
/// its temporary file, its Windows retry and its `sync_all`. An entry only that instance has is
/// never erased; ours win for a key both have. The merged map (theirs included) stays in memory.
fn write(map: &mut Map<String, Value>) {
    let mut merged = read();
    for (k, v) in map.iter() {
        merged.insert(k.clone(), v.clone());
    }
    let p = path();
    if let Some(dir) = p.parent() {
        let _ = std::fs::create_dir_all(dir);
    }
    // a temporary file of a run that stopped mid-write, as the settings clean up theirs
    crate::settings::remove_stale_temp(&p);
    let _ = crate::settings::write_file(&p, &merged);
    *map = merged;
}

/// The assistant's id for one login, made the first time it is needed and kept. `key` is
/// [`login_key`]: the server address and the user, never the address alone. The id is random, so
/// nothing of the login can be read from it or checked against it; a write that fails still keeps
/// it for the rest of the run, and an id another instance of Barbican made meanwhile is adopted
/// rather than replaced.
pub fn assistant_login_id(key: &str) -> String {
    let cell = cache();
    let mut guard = cell.lock().unwrap_or_else(|e| e.into_inner());
    let map = guard.get_or_insert_with(read);
    if let Some(id) = map.get(key).and_then(Value::as_str).filter(|s| is_id(s)) {
        return id.to_string();
    }
    // another instance may have made one since this run read the file: take its id
    if let Some(id) = read().get(key).and_then(Value::as_str).filter(|s| is_id(s)).map(String::from) {
        map.insert(key.to_string(), Value::String(id.clone()));
        return id;
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

    fn work() -> String {
        login_key("https://mail.work.example.test", "ann@example.test")
    }
    fn home() -> String {
        login_key("https://mail.home.example.test", "ann@example.test")
    }

    #[test]
    fn one_login_one_key() {
        // the same login is one key, however its address is written
        assert_eq!(login_key("https://mail.example.test/jmap", "ann@example.test"), login_key("https://mail.example.test/jmap/", "ann@example.test"));
        assert_eq!(login_key("mail.example.test", "ann@example.test"), login_key("https://mail.example.test", "ann@example.test"));
        // another port is another server, another user another login
        assert_ne!(login_key("https://mail.example.test", "ann@example.test"), login_key("https://mail.example.test:8443", "ann@example.test"));
        assert_ne!(login_key("http://mail.example.test", "ann@example.test"), login_key("https://mail.example.test", "ann@example.test"));
        assert_ne!(login_key("https://mail.example.test", "ann@example.test"), login_key("https://mail.example.test", "bob@example.test"));
        // and it holds the user and the server, so it names exactly one login
        assert!(work().starts_with("https://mail.work.example.test\nann@example.test"));
    }

    #[test]
    fn an_id_is_made_once_and_kept_across_a_reload() {
        let scope = TestScope::new();
        let id = assistant_login_id(&work());
        assert_eq!(id.len(), ID_LEN);
        assert!(is_id(&id), "{id}");
        // on disk, keyed by the login key
        let saved: Value = serde_json::from_slice(&std::fs::read(scope.file()).unwrap()).unwrap();
        assert_eq!(saved[work().as_str()].as_str(), Some(id.as_str()));
        // asked again in the same run: the same id, and only one record
        assert_eq!(assistant_login_id(&work()), id);
        assert_eq!(serde_json::from_slice::<Value>(&std::fs::read(scope.file()).unwrap()).unwrap().as_object().unwrap().len(), 1);
        // a restart reads the file: the id is the one that was made
        scope.reload();
        assert_eq!(assistant_login_id(&work()), id);
    }

    #[test]
    fn two_logins_get_different_ids() {
        let _scope = TestScope::new();
        let a = assistant_login_id(&work());
        let b = assistant_login_id(&home());
        let other = assistant_login_id(&login_key("https://mail.work.example.test", "bob@example.test"));
        assert_ne!(a, b);
        assert_ne!(a, other);
        assert_ne!(b, other);
        assert_eq!(assistant_login_id(&work()), a);
    }

    #[test]
    fn nothing_of_the_key_is_in_the_id() {
        let _scope = TestScope::new();
        let id = assistant_login_id(&work());
        // hex only: no address, no server, no '@', no separator
        assert!(id.chars().all(|c| c.is_ascii_hexdigit() && !c.is_ascii_uppercase()), "{id}");
        assert!(!id.contains("ann") && !id.contains("example") && !id.contains('@') && !id.contains('\n'));
        // a stored value that is not an id (an address pasted in) is replaced, not used
        let scope = TestScope::new();
        let mut m = Map::new();
        m.insert(work(), Value::String("ann@example.test".into()));
        std::fs::write(scope.file(), serde_json::to_vec(&Value::Object(m)).unwrap()).unwrap();
        let fresh = assistant_login_id(&work());
        assert!(is_id(&fresh), "{fresh}");
        assert_ne!(fresh, "ann@example.test");
        assert_eq!(assistant_login_id(&work()), fresh);
    }

    #[test]
    fn an_entry_another_instance_wrote_survives_and_is_adopted() {
        let scope = TestScope::new();
        let mine = assistant_login_id(&work());
        // a second Barbican writes its own login and makes an id for ours before we ask for it
        let theirs = "0123456789abcdef0123456789abcdef";
        let adopted = "fedcba9876543210fedcba9876543210";
        let mut other = Map::new();
        other.insert(work(), Value::String(mine.clone()));
        other.insert("https://mail.theirs.example.test\nkim@example.test".to_string(), Value::String(theirs.into()));
        other.insert(home(), Value::String(adopted.into()));
        std::fs::write(scope.file(), serde_json::to_vec(&Value::Object(other)).unwrap()).unwrap();
        // its id for our other login is taken as it is, not replaced
        assert_eq!(assistant_login_id(&home()), adopted);
        // and a new id of ours is written beside its entries, not over them
        let third = assistant_login_id(&login_key("https://mail.work.example.test", "cy@example.test"));
        let saved: Value = serde_json::from_slice(&std::fs::read(scope.file()).unwrap()).unwrap();
        assert_eq!(saved["https://mail.theirs.example.test\nkim@example.test"].as_str(), Some(theirs));
        assert_eq!(saved[home().as_str()].as_str(), Some(adopted));
        assert_eq!(saved[work().as_str()].as_str(), Some(mine.as_str()));
        assert_eq!(saved[login_key("https://mail.work.example.test", "cy@example.test").as_str()].as_str(), Some(third.as_str()));
        // and the merge stays in memory: another restart keeps every one of them
        scope.reload();
        assert_eq!(assistant_login_id(&home()), adopted);
        assert_eq!(assistant_login_id(&work()), mine);
    }

    #[test]
    fn a_failed_save_keeps_the_id_in_this_run() {
        let scope = TestScope::new();
        let first = assistant_login_id(&work());
        // every later write fails: the file's place is taken by a folder
        std::fs::remove_file(scope.file()).unwrap();
        std::fs::create_dir(scope.file()).unwrap();
        let home_id = assistant_login_id(&home());
        assert!(is_id(&home_id));
        // nothing could be written ...
        assert!(std::fs::read_to_string(scope.file()).is_err());
        // ... but the ids stay what they were for the rest of the run
        assert_eq!(assistant_login_id(&home()), home_id);
        assert_eq!(assistant_login_id(&work()), first);
        std::fs::remove_dir(scope.file()).ok();
    }
}
