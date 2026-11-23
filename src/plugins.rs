//! Native plugin host.
//!
//! Drop a plugin DLL into `%APPDATA%\Barbican\plugins\`. It is listed under Settings → Add-ons but NOT loaded or called
//! until the user switches it on (allow-list pref `enabledPlugins`).
//! A plugin is any dynamic library exporting this C ABI (all strings UTF-8, NUL-terminated):
//!
//! ```c
//! // Manifest JSON: {"id","name","version","description","hooks":["email.details", ...]}
//! const char* barbican_plugin_manifest(void);
//! // Handle a hook. `input` is JSON; returns JSON allocated by the plugin (or NULL).
//! char* barbican_plugin_call(const char* hook, const char* input);
//! // Free a string returned by barbican_plugin_call.
//! void barbican_plugin_free(char* s);
//! ```
//!
//! Hooks (API version 1):
//! * `email.details`  input: the full JMAP Email object (with `headers`)
//!   output: `{"rows":[{"label","value","tone":"neutral|success|warning|danger"}]}`
//!   shown in the reader under the sender, like Bulwark's `ui:email-details` slot.
//! * `compose.check`  input: `{"to","cc","bcc","subject","text","attachments":[names]}`
//!   output: `{"warning": "text"}` to hold the send once (like the attachment reminder).
//!   Names are Barbican's own. Hook names borrow Bulwark's as a reference vocabulary (not compatibility):
//!   `plugin_catalog::HOOK_ALIASES` maps them to `onEmailOpen` and `onBeforeEmailSend`, and a manifest may list either
//!   spelling; the plugin is called with the spelling its manifest declared.
//! * `settings`       input: `{}`; output: `{"rows":[...settings rows like ui/settings-schema.json...]}`
//!
//! Safety:
//! * A native library runs code the moment it is loaded (its `DllMain`/constructors), and the C ABI only exposes the
//!   manifest after loading. So the manifest is read from an optional sidecar `<name>.manifest.json` (or `manifest.json`
//!   when the folder holds a single library). Without a sidecar the plugin is listed by file name, unverified, and its
//!   real manifest is read only after the user enables it. Enabling it still runs its code in this process: only
//!   enable plugins you trust. A sidecar that disagrees with the library's own manifest id is rejected.
//! * A plugin is enabled by key AND bound to its file (path + SHA-256, `pluginBindings` pref); a replaced file must be
//!   switched off and on again. Manifest ids starting with `file:` or holding control characters are rejected, and a
//!   library whose id duplicates another plugin's is refused (after loading: the ABI gives no earlier look).
//! * Loading (`Library::new` + the manifest call) runs on a worker thread with a budget; an overrun marks the plugin failed.
//! * At most one call is in flight per plugin: while an abandoned call has not returned, new calls fail at once
//!   (the ABI makes no thread-safety promise), which counts toward the failure limit.
//! * Every call runs on a worker thread with the hook's budget from the catalogue (2000 ms when the hook is not in it,
//!   at most [`MAX_BUDGET_MS`] because the UI waits). A call that overruns is abandoned (its thread may linger) and
//!   counts as a failure. Panics are caught. A panic inside a plugin's own `extern "C"` function aborts at the C
//!   boundary before the host sees it; that cannot be caught from here.
//! * `compose.check` fails closed (see [`compose_check`]).
//! * [`MAX_FAILURES`] failures in a row switch a plugin off for the session; the user is told through [`drain_notices`].
use crate::plugin_catalog::{lookup_hook, resolve_alias};
use serde_json::{json, Value};
use std::ffi::{c_char, CStr, CString};
use std::sync::atomic::{AtomicBool, AtomicU32, Ordering};
use sha2::{Digest, Sha256};
use std::sync::{mpsc, Arc, Mutex};
use std::time::Duration;

pub const API_VERSION: u32 = 1;
/// Budget for a hook the catalogue does not list.
pub const DEFAULT_BUDGET_MS: u32 = 2000;
/// Upper bound for native calls: the UI thread waits for them, so Bulwark's 10-minute send budget is cut down.
pub const MAX_BUDGET_MS: u32 = 10_000;
/// Consecutive failures that switch a plugin off for the session.
pub const MAX_FAILURES: u32 = 3;

type ManifestFn = unsafe extern "C" fn() -> *const c_char;
type CallFn = unsafe extern "C" fn(*const c_char, *const c_char) -> *mut c_char;
type FreeFn = unsafe extern "C" fn(*mut c_char);

/// What a plugin does with a call, so the host can be tested without a DLL.
/// `Ok(None)` is "nothing to say"; `Err` is a failure (error, panic, bad output).
pub trait PluginBackend: Send + Sync {
    fn call(&self, hook: &str, input: &Value) -> Result<Option<Value>, String>;
}

struct Native {
    call: CallFn,
    free: FreeFn,
    _lib: libloading::Library,
}

impl PluginBackend for Native {
    fn call(&self, hook: &str, input: &Value) -> Result<Option<Value>, String> {
        let h = CString::new(hook).map_err(|e| e.to_string())?;
        let i = CString::new(input.to_string()).map_err(|e| e.to_string())?;
        // A panicking plugin must not take the app down with it.
        let out = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| unsafe { (self.call)(h.as_ptr(), i.as_ptr()) }))
            .map_err(|_| "plugin panicked".to_string())?;
        if out.is_null() { return Ok(None); }
        let text = unsafe { CStr::from_ptr(out) }.to_string_lossy().to_string();
        unsafe { (self.free)(out) };
        serde_json::from_str(&text).map(Some).map_err(|e| format!("invalid JSON from plugin: {e}"))
    }
}

pub struct Plugin {
    /// Id the user toggles by: the manifest id when a sidecar manifest exists, else `file:<name>`.
    pub key: String,
    pub id: String,
    pub name: String,
    pub version: String,
    pub description: String,
    pub hooks: Vec<String>,
    pub path: String,
    /// The manifest came from a sidecar file (otherwise it is read from the library once enabled).
    verified: bool,
    /// SHA-256 of the library file when it was discovered.
    fingerprint: String,
    backend: Option<Arc<dyn PluginBackend>>,
    /// Set while a call is in flight (also after it was abandoned on timeout).
    busy: Arc<AtomicBool>,
    load_failed: bool,
    failures: AtomicU32,
    tripped: AtomicBool,
}

static NOTICES: Mutex<Vec<String>> = Mutex::new(Vec::new());

/// Messages for the user (a plugin was switched off); the UI shows them as toasts.
pub fn drain_notices() -> Vec<String> {
    std::mem::take(&mut *NOTICES.lock().unwrap_or_else(|e| e.into_inner()))
}

impl Plugin {
    pub fn handles(&self, hook: &str) -> bool {
        let want = resolve_alias(hook);
        self.hooks.iter().any(|h| resolve_alias(h) == want)
    }

    pub fn is_tripped(&self) -> bool { self.tripped.load(Ordering::SeqCst) }

    /// Loaded, switched on and not switched off by failures.
    fn active(&self, enabled: &[String]) -> bool {
        self.backend.is_some() && !self.is_tripped() && enabled.contains(&self.key)
    }

    /// The spelling of `hook` this plugin's manifest declared (its aliases are interchangeable).
    fn declared_name<'a>(&'a self, hook: &'a str) -> &'a str {
        let want = resolve_alias(hook);
        self.hooks.iter().find(|h| resolve_alias(h) == want).map_or(hook, |h| h.as_str())
    }

    /// What enabling pins: the file's path and content hash.
    pub fn binding(&self) -> String { format!("{}|{}", self.path, self.fingerprint) }

    /// Call a hook on a worker thread; give up after `budget`. Counts toward the failure limit.
    /// Never runs two calls into one plugin at once: if an earlier call has not returned, this one fails at once.
    pub fn call(&self, hook: &str, input: &Value, budget: Duration) -> Result<Option<Value>, String> {
        let Some(backend) = self.backend.clone() else { return Err("not loaded".into()) };
        if self.busy.swap(true, Ordering::SeqCst) {
            let r = Err("the previous call has not returned yet".to_string());
            self.record(&r);
            return r;
        }
        let busy = self.busy.clone();
        let (tx, rx) = mpsc::channel();
        let (h, i) = (self.declared_name(hook).to_string(), input.clone());
        // a fixed thread name: manifest text is untrusted
        let spawned = std::thread::Builder::new().name("barbican-plugin".into()).spawn(move || {
            let r = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| backend.call(&h, &i)))
                .unwrap_or_else(|_| Err("plugin panicked".to_string()));
            busy.store(false, Ordering::SeqCst);
            let _ = tx.send(r);
        });
        let r = match spawned {
            Err(e) => { self.busy.store(false, Ordering::SeqCst); Err(format!("could not start worker: {e}")) }
            Ok(_) => match rx.recv_timeout(budget) {
                Ok(r) => r,
                Err(mpsc::RecvTimeoutError::Timeout) => Err(format!("timed out after {} ms", budget.as_millis())),
                Err(mpsc::RecvTimeoutError::Disconnected) => Err("plugin worker died".to_string()),
            },
        };
        self.record(&r);
        r
    }

    fn record(&self, r: &Result<Option<Value>, String>) {
        match r {
            Ok(_) => self.failures.store(0, Ordering::SeqCst),
            Err(e) => {
                let n = self.failures.fetch_add(1, Ordering::SeqCst) + 1;
                if n >= MAX_FAILURES && !self.tripped.swap(true, Ordering::SeqCst) {
                    NOTICES.lock().unwrap_or_else(|e| e.into_inner())
                        .push(format!("Plugin {} was switched off for this session after {MAX_FAILURES} failures in a row (last: {e}).", self.name));
                }
            }
        }
    }
}

/// Time a hook may take: the catalogue's budget (aliases resolved), else 2000 ms, never over [`MAX_BUDGET_MS`].
pub fn budget(hook: &str) -> Duration {
    Duration::from_millis(lookup_hook(hook).map_or(DEFAULT_BUDGET_MS, |h| h.budget_ms).min(MAX_BUDGET_MS) as u64)
}

pub fn dir() -> std::path::PathBuf {
    crate::config_dir().join("plugins")
}

struct Meta {
    id: String,
    name: String,
    version: String,
    description: String,
    hooks: Vec<String>,
}

fn parse_manifest(v: &Value) -> Result<Meta, String> {
    if v["api"].as_u64().unwrap_or(1) > API_VERSION as u64 { return Err(format!("needs plugin API {} (this app has {API_VERSION})", v["api"])); }
    Ok(Meta {
        id: v["id"].as_str().filter(|s| !s.is_empty() && !s.starts_with("file:") && !s.chars().any(char::is_control))
            .ok_or("manifest has no usable id (empty, starts with file:, or has control characters)")?.to_string(),
        name: v["name"].as_str().unwrap_or("Plugin").to_string(),
        version: v["version"].as_str().unwrap_or("").to_string(),
        description: v["description"].as_str().unwrap_or("").to_string(),
        hooks: v["hooks"].as_array().map(|a| a.iter().filter_map(|h| h.as_str().map(String::from)).collect()).unwrap_or_default(),
    })
}

fn is_lib(p: &std::path::Path) -> bool {
    p.extension().is_some_and(|x| x.eq_ignore_ascii_case(std::env::consts::DLL_EXTENSION))
}

/// List the plugins in the plugins folder WITHOUT loading any library.
pub fn discover() -> (Vec<Plugin>, Vec<String>) {
    let _ = std::fs::create_dir_all(dir());
    discover_in(&dir())
}

pub fn discover_in(folder: &std::path::Path) -> (Vec<Plugin>, Vec<String>) {
    let mut out: Vec<Plugin> = vec![];
    let mut errors = vec![];
    let Ok(entries) = std::fs::read_dir(folder) else { return (out, errors) };
    let mut libs: Vec<std::path::PathBuf> = entries.flatten().map(|e| e.path()).filter(|p| is_lib(p)).collect();
    libs.sort();
    for p in &libs {
        let file = p.file_name().map(|f| f.to_string_lossy().to_string()).unwrap_or_default();
        // `<name>.manifest.json`, or a plain `manifest.json` when this is the only library in the folder
        let mut candidates = vec![p.with_extension("manifest.json")];
        if libs.len() == 1 { candidates.push(folder.join("manifest.json")); }
        let meta = match candidates.into_iter().find(|c| c.is_file()) {
            None => None,
            Some(c) => match std::fs::read_to_string(&c).map_err(|e| e.to_string())
                .and_then(|t| serde_json::from_str::<Value>(&t).map_err(|e| format!("bad manifest.json: {e}")))
                .and_then(|v| parse_manifest(&v)) {
                Ok(m) => Some(m),
                Err(e) => { errors.push(format!("{file}: {e}")); continue; }
            },
        };
        let verified = meta.is_some();
        let m = meta.unwrap_or_else(|| Meta {
            id: format!("file:{file}"), name: file.clone(), version: String::new(), hooks: vec![],
            description: "No manifest next to the file, so nothing is known about it until you switch it on. Only switch on plugins you trust.".into(),
        });
        if out.iter().any(|x| x.key == m.id) { errors.push(format!("{file}: duplicate id {}", m.id)); continue; }
        out.push(Plugin { key: m.id.clone(), id: m.id, name: m.name, version: m.version, description: m.description, hooks: m.hooks,
            path: p.display().to_string(), verified, fingerprint: fingerprint(p), backend: None, busy: Arc::new(AtomicBool::new(false)),
            load_failed: false, failures: AtomicU32::new(0), tripped: AtomicBool::new(false) });
    }
    (out, errors)
}

fn fingerprint(p: &std::path::Path) -> String {
    std::fs::read(p).map(|b| Sha256::digest(&b).iter().map(|x| format!("{x:02x}")).collect()).unwrap_or_default()
}

/// Keep the state (loaded library, failure count) of entries that are unchanged; take new or replaced files as found.
fn merge_discovered(mut old: Vec<Plugin>, new: Vec<Plugin>) -> Vec<Plugin> {
    new.into_iter().map(|n| match old.iter().position(|o| o.key == n.key && o.path == n.path && o.fingerprint == n.fingerprint) {
        Some(i) => old.swap_remove(i),
        None => n,
    }).collect()
}

/// Re-read the plugins folder (Settings → Add-ons) without loading anything new.
pub fn refresh(list: &mut Vec<Plugin>) -> Vec<String> {
    let (found, errors) = discover();
    *list = merge_discovered(std::mem::take(list), found);
    errors
}

/// The keys that are both switched on and still bound to the file they were enabled for.
pub fn bound_enabled(plugins: &[Plugin], enabled: &[String], bindings: &Value) -> Vec<String> {
    enabled.iter().filter(|k| plugins.iter().any(|p| &p.key == *k && bindings[k.as_str()].as_str() == Some(p.binding().as_str()))).cloned().collect()
}

/// Installed plugins that are not switched on: worth a one-time notice that plugins are off until enabled.
pub fn has_unenabled(plugins: &[Plugin], enabled: &[String]) -> bool {
    plugins.iter().any(|p| !enabled.contains(&p.key))
}

type LoadResult = Result<(Meta, Arc<dyn PluginBackend>), String>;
type Loader = Arc<dyn Fn(&std::path::Path) -> LoadResult + Send + Sync>;
/// How long loading a library (and reading its manifest) may take.
pub const LOAD_BUDGET_MS: u64 = 3000;

/// Load the plugins the user enabled and unload the ones switched off. Cheap when nothing changed.
/// Returns load errors (each reported once).
pub fn sync(plugins: &mut [Plugin], enabled: &[String]) -> Vec<String> {
    let loader: Loader = Arc::new(|p: &std::path::Path| unsafe { load(p) });
    sync_with(plugins, enabled, &loader, Duration::from_millis(LOAD_BUDGET_MS))
}

fn sync_with(plugins: &mut [Plugin], enabled: &[String], loader: &Loader, budget: Duration) -> Vec<String> {
    let mut errors = vec![];
    for i in 0..plugins.len() {
        if !enabled.contains(&plugins[i].key) {
            // switching off forgets an earlier load failure, so on again tries again
            plugins[i].backend = None;
            plugins[i].load_failed = false;
            continue;
        }
        if plugins[i].backend.is_some() || plugins[i].load_failed || plugins[i].is_tripped() { continue; }
        let path = plugins[i].path.clone();
        // Library::new and manifest() run foreign code: never on the UI thread without a limit
        let (tx, rx) = mpsc::channel();
        let (l, pp) = (loader.clone(), std::path::PathBuf::from(&path));
        let started = std::thread::Builder::new().name("barbican-plugin-load".into()).spawn(move || {
            let r = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| l(&pp))).unwrap_or_else(|_| Err("plugin panicked while loading".to_string()));
            let _ = tx.send(r); // if nobody waits any more the library is dropped (unloaded) here
        });
        let res = match started {
            Err(e) => Err(format!("could not start loader: {e}")),
            Ok(_) => match rx.recv_timeout(budget) {
                Ok(r) => r,
                Err(mpsc::RecvTimeoutError::Timeout) => Err(format!("did not finish loading within {} ms", budget.as_millis())),
                Err(mpsc::RecvTimeoutError::Disconnected) => Err("loader died".to_string()),
            },
        };
        let (m, b) = match res {
            Ok(x) => x,
            Err(e) => { plugins[i].load_failed = true; errors.push(format!("{path}: {e}")); continue; }
        };
        if plugins[i].verified && m.id != plugins[i].id {
            plugins[i].load_failed = true;
            errors.push(format!("{path}: manifest.json says id {} but the library says {}", plugins[i].id, m.id));
        } else if plugins.iter().enumerate().any(|(j, o)| j != i && o.id == m.id) {
            plugins[i].load_failed = true;
            errors.push(format!("{path}: another plugin already uses the id {}", m.id));
        } else {
            let p = &mut plugins[i];
            if !p.verified { (p.id, p.name, p.version, p.description, p.hooks) = (m.id, m.name, m.version, m.description, m.hooks); }
            p.backend = Some(b);
        }
    }
    errors
}

unsafe fn load(p: &std::path::Path) -> LoadResult {
    let lib = libloading::Library::new(p).map_err(|e| e.to_string())?;
    let manifest: libloading::Symbol<ManifestFn> = lib.get(b"barbican_plugin_manifest\0").map_err(|_| "missing barbican_plugin_manifest")?;
    let call: libloading::Symbol<CallFn> = lib.get(b"barbican_plugin_call\0").map_err(|_| "missing barbican_plugin_call")?;
    let free: libloading::Symbol<FreeFn> = lib.get(b"barbican_plugin_free\0").map_err(|_| "missing barbican_plugin_free")?;
    let m = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| manifest())).map_err(|_| "manifest call panicked")?;
    if m.is_null() { return Err("empty manifest".into()); }
    let v: Value = serde_json::from_str(&CStr::from_ptr(m).to_string_lossy()).map_err(|e| format!("bad manifest: {e}"))?;
    let meta = parse_manifest(&v)?;
    let (call, free) = (*call, *free);
    Ok((meta, Arc::new(Native { call, free, _lib: lib })))
}

/// Is this plugin id loaded, enabled and not switched off? (The built-in spam panel steps aside for `spam-score`.)
pub fn is_active(plugins: &[Plugin], enabled: &[String], id: &str) -> bool {
    plugins.iter().any(|p| p.id == id && p.active(enabled))
}

/// Rows from every enabled plugin for the open message.
pub fn email_details(plugins: &[Plugin], enabled: &[String], email: &Value) -> Vec<(String, String, String)> {
    let mut rows = vec![];
    let b = budget("email.details");
    for p in plugins.iter().filter(|p| p.active(enabled) && p.handles("email.details")) {
        if let Ok(Some(v)) = p.call("email.details", email, b) {
            for r in v["rows"].as_array().cloned().unwrap_or_default() {
                rows.push((r["label"].as_str().unwrap_or("").to_string(), r["value"].as_str().unwrap_or("").to_string(), r["tone"].as_str().unwrap_or("neutral").to_string()));
            }
        }
    }
    rows
}

/// First warning any enabled plugin raises before sending. Fails CLOSED: a plugin that handles `compose.check` and
/// panics, errors or times out holds the send with a warning (shown once, like the attachment reminder).
pub fn compose_check(plugins: &[Plugin], enabled: &[String], draft: &Value) -> Option<String> {
    compose_check_with(plugins, enabled, draft, budget("compose.check"))
}

pub fn compose_check_with(plugins: &[Plugin], enabled: &[String], draft: &Value, budget: Duration) -> Option<String> {
    let mut first = None;
    for p in plugins.iter().filter(|p| p.active(enabled) && p.handles("compose.check")) {
        let w = match p.call("compose.check", draft, budget) {
            Ok(v) => v.and_then(|v| v["warning"].as_str().filter(|w| !w.is_empty()).map(String::from)),
            Err(e) => Some(format!("The {} plugin could not check this message ({e}).", p.name)),
        };
        // keep calling the rest so every failing plugin is counted
        if first.is_none() { first = w; }
    }
    first
}

pub fn describe(p: &Plugin) -> Value {
    let note = if p.is_tripped() { " Switched off for this session after repeated failures." } else { "" };
    json!({"id": p.key, "name": p.name, "version": p.version, "description": format!("{}{note}", p.description), "hooks": p.hooks, "path": p.path})
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::AtomicUsize;

    enum Mode { Reply(Value), Nothing, Fail, Panic, Sleep(u64) }
    struct Fake { mode: Mode, calls: AtomicUsize }
    impl PluginBackend for Fake {
        fn call(&self, _: &str, _: &Value) -> Result<Option<Value>, String> {
            self.calls.fetch_add(1, Ordering::SeqCst);
            match &self.mode {
                Mode::Reply(v) => Ok(Some(v.clone())),
                Mode::Nothing => Ok(None),
                Mode::Fail => Err("boom".into()),
                Mode::Panic => panic!("fake plugin panic"),
                Mode::Sleep(ms) => { std::thread::sleep(Duration::from_millis(*ms)); Ok(Some(json!({}))) }
            }
        }
    }

    fn fake(name: &str, mode: Mode) -> (Plugin, Arc<Fake>) {
        let f = Arc::new(Fake { mode, calls: AtomicUsize::new(0) });
        let p = Plugin { key: name.into(), id: name.into(), name: name.into(), version: "1".into(), description: String::new(),
            hooks: vec!["compose.check".into(), "email.details".into()], path: String::new(), verified: true,
            fingerprint: String::new(), busy: Arc::new(AtomicBool::new(false)), backend: Some(f.clone()), load_failed: false, failures: AtomicU32::new(0), tripped: AtomicBool::new(false) };
        (p, f)
    }
    fn on(p: &Plugin) -> Vec<String> { vec![p.key.clone()] }
    const MS: Duration = Duration::from_millis(500);

    #[test]
    fn budgets_come_from_the_catalogue() {
        assert_eq!(budget("email.details"), Duration::from_millis(5000));
        assert_eq!(budget("onEmailOpen"), Duration::from_millis(5000));
        assert_eq!(budget("settings"), Duration::from_millis(2000));
        assert_eq!(budget("compose.check"), Duration::from_millis(MAX_BUDGET_MS as u64)); // catalogue 600000, capped
    }

    #[test]
    fn handles_either_spelling() {
        let (mut p, _) = fake("a", Mode::Nothing);
        p.hooks = vec!["onBeforeEmailSend".into()];
        assert!(p.handles("compose.check") && p.handles("onBeforeEmailSend") && !p.handles("email.details"));
    }

    #[test]
    fn healthy_plugin_warns_or_passes() {
        let (p, _) = fake("warns", Mode::Reply(json!({"warning": "Looks odd"})));
        assert_eq!(compose_check_with(&[p], &["warns".into()], &json!({}), MS).as_deref(), Some("Looks odd"));
        let (p, _) = fake("quiet", Mode::Nothing);
        assert_eq!(compose_check_with(std::slice::from_ref(&p), &on(&p), &json!({}), MS), None);
        let (p, _) = fake("empty", Mode::Reply(json!({"warning": ""})));
        assert_eq!(compose_check_with(std::slice::from_ref(&p), &on(&p), &json!({}), MS), None);
    }

    #[test]
    fn compose_check_fails_closed() {
        for (name, mode) in [("errs", Mode::Fail), ("panics", Mode::Panic), ("hangs", Mode::Sleep(400))] {
            let (p, _) = fake(name, mode);
            let w = compose_check_with(std::slice::from_ref(&p), &on(&p), &json!({}), Duration::from_millis(60));
            assert!(w.as_deref().is_some_and(|w| w.contains(name)), "{name}: {w:?}");
        }
    }

    #[test]
    fn timeout_is_abandoned_and_counted() {
        let (p, _) = fake("slow", Mode::Sleep(400));
        let t = std::time::Instant::now();
        let r = p.call("email.details", &json!({}), Duration::from_millis(50));
        assert!(t.elapsed() < Duration::from_millis(300));
        assert!(r.unwrap_err().contains("timed out"));
        assert_eq!(p.failures.load(Ordering::SeqCst), 1);
    }

    #[test]
    fn panic_is_caught_and_counted() {
        let (p, _) = fake("pan", Mode::Panic);
        assert!(p.call("x", &json!({}), MS).unwrap_err().contains("panicked"));
        assert_eq!(p.failures.load(Ordering::SeqCst), 1);
    }

    #[test]
    fn three_failures_in_a_row_switch_it_off() {
        let (p, f) = fake("flaky-plugin-name", Mode::Fail);
        let en = on(&p);
        for _ in 0..5 { compose_check_with(std::slice::from_ref(&p), &en, &json!({}), MS); }
        assert!(p.is_tripped());
        assert_eq!(f.calls.load(Ordering::SeqCst), MAX_FAILURES as usize, "no calls once switched off");
        assert!(drain_notices().iter().any(|n| n.contains("flaky-plugin-name") && n.contains("switched off")));
        // off for the session: the send is no longer held by it
        assert_eq!(compose_check_with(std::slice::from_ref(&p), &en, &json!({}), MS), None);
        assert!(describe(&p)["description"].as_str().unwrap().contains("Switched off"));
    }

    #[test]
    fn a_success_resets_the_count() {
        let (p, _) = fake("ok", Mode::Nothing);
        p.failures.store(MAX_FAILURES - 1, Ordering::SeqCst);
        assert!(p.call("x", &json!({}), MS).is_ok());
        assert_eq!(p.failures.load(Ordering::SeqCst), 0);
        assert!(!p.is_tripped());
    }

    #[test]
    fn not_enabled_means_not_called() {
        let (p, f) = fake("off", Mode::Reply(json!({"warning": "x", "rows": [{"label": "a", "value": "b"}]})));
        assert_eq!(compose_check_with(std::slice::from_ref(&p), &[], &json!({}), MS), None);
        assert!(email_details(std::slice::from_ref(&p), &[], &json!({})).is_empty());
        assert!(!is_active(std::slice::from_ref(&p), &[], "off"));
        assert_eq!(f.calls.load(Ordering::SeqCst), 0);
        let rows = email_details(std::slice::from_ref(&p), &on(&p), &json!({}));
        assert_eq!(rows, vec![("a".to_string(), "b".to_string(), "neutral".to_string())]);
    }

    fn temp_folder(tag: &str) -> std::path::PathBuf {
        let d = std::env::temp_dir().join(format!("barbican-plugin-test-{tag}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&d);
        std::fs::create_dir_all(&d).unwrap();
        d
    }
    fn lib_name(stem: &str) -> String { format!("{stem}.{}", std::env::consts::DLL_EXTENSION) }

    #[test]
    fn discovery_reads_the_sidecar_and_loads_nothing() {
        let d = temp_folder("sidecar");
        // not a real library: loading it would fail, so any error proves a load was attempted
        std::fs::write(d.join(lib_name("one")), b"not a library").unwrap();
        std::fs::write(d.join("one.manifest.json"), br#"{"id":"one","name":"One","hooks":["compose.check"]}"#).unwrap();
        std::fs::write(d.join(lib_name("two")), b"not a library").unwrap();
        let (mut list, errors) = discover_in(&d);
        assert!(errors.is_empty(), "{errors:?}");
        assert_eq!(list.len(), 2);
        let one = list.iter().find(|p| p.key == "one").unwrap();
        assert!(one.handles("compose.check") && one.backend.is_none());
        let two = list.iter().find(|p| p.key.starts_with("file:")).unwrap();
        assert!(two.hooks.is_empty() && !two.verified);
        // default off: nothing enabled, nothing loaded, no errors
        assert!(sync(&mut list, &[]).is_empty());
        // enabling is what triggers the load attempt (which fails for the dummy file)
        let errs = sync(&mut list, &["one".to_string()]);
        assert_eq!(errs.len(), 1, "{errs:?}");
        assert!(sync(&mut list, &["one".to_string()]).is_empty(), "a failed load is reported once");
        let _ = std::fs::remove_dir_all(&d);
    }

    #[test]
    fn single_library_may_use_plain_manifest_json() {
        let d = temp_folder("plain");
        std::fs::write(d.join(lib_name("only")), b"x").unwrap();
        std::fs::write(d.join("manifest.json"), br#"{"id":"only-id","hooks":[]}"#).unwrap();
        let (list, errors) = discover_in(&d);
        assert!(errors.is_empty() && list.len() == 1 && list[0].key == "only-id");
        std::fs::write(d.join(lib_name("second")), b"x").unwrap();
        let (list, _) = discover_in(&d);
        assert!(list.iter().all(|p| p.key.starts_with("file:")), "manifest.json is ambiguous with two libraries");
        let _ = std::fs::remove_dir_all(&d);
    }

    #[test]
    fn loads_spam_score_plugin() {
        let dll = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("plugins/spam-score/target/release/barbican_spam_score.dll");
        if !dll.exists() { eprintln!("example plugin not built; skipping"); return; }
        let (meta, backend) = unsafe { super::load(&dll) }.expect("load");
        assert_eq!(meta.id, "spam-score");
        let p = Plugin { key: meta.id.clone(), id: meta.id, name: meta.name, version: meta.version, description: meta.description, hooks: meta.hooks,
            path: String::new(), verified: true, fingerprint: String::new(), busy: Arc::new(AtomicBool::new(false)), backend: Some(backend), load_failed: false, failures: AtomicU32::new(0), tripped: AtomicBool::new(false) };
        assert!(p.handles("email.details"));
        let email = json!({"headers": [{"name": "X-Spam-Status", "value": "Yes, score=7.5 required=5.0"}]});
        let rows = email_details(std::slice::from_ref(&p), &on(&p), &email);
        assert_eq!(rows.len(), 1);
        assert_eq!(rows[0].2, "danger");
        assert!(rows[0].1.contains("7.50 / 5.00"), "{}", rows[0].1);
    }

    struct Recorder(Mutex<Vec<String>>);
    impl PluginBackend for Recorder {
        fn call(&self, hook: &str, _: &Value) -> Result<Option<Value>, String> { self.0.lock().unwrap().push(hook.to_string()); Ok(None) }
    }

    fn bare(key: &str, path: &str, fp: &str) -> Plugin {
        Plugin { key: key.into(), id: key.into(), name: key.into(), version: String::new(), description: String::new(), hooks: vec![],
            path: path.into(), verified: true, fingerprint: fp.into(), backend: None, busy: Arc::new(AtomicBool::new(false)),
            load_failed: false, failures: AtomicU32::new(0), tripped: AtomicBool::new(false) }
    }

    fn stub_meta(id: &str) -> Meta { Meta { id: id.into(), name: id.into(), version: String::new(), description: String::new(), hooks: vec![] } }
    fn stub_loader(f: impl Fn(&std::path::Path) -> LoadResult + Send + Sync + 'static) -> Loader { Arc::new(f) }

    #[test]
    fn plugin_is_called_with_the_spelling_its_manifest_declared() {
        let rec = Arc::new(Recorder(Mutex::new(vec![])));
        let mut p = bare("rec", "", "");
        p.backend = Some(rec.clone());
        p.hooks = vec!["onBeforeEmailSend".into(), "onEmailOpen".into()];
        p.call("compose.check", &json!({}), MS).unwrap();
        p.call("email.details", &json!({}), MS).unwrap();
        p.call("settings", &json!({}), MS).unwrap();
        assert_eq!(*rec.0.lock().unwrap(), ["onBeforeEmailSend", "onEmailOpen", "settings"]);
        p.hooks = vec!["compose.check".into()];
        p.call("onBeforeEmailSend", &json!({}), MS).unwrap();
        assert_eq!(rec.0.lock().unwrap().last().unwrap(), "compose.check");
    }

    #[test]
    fn no_second_call_while_an_abandoned_one_runs() {
        let (p, f) = fake("busy", Mode::Sleep(300));
        assert!(p.call("x", &json!({}), Duration::from_millis(30)).unwrap_err().contains("timed out"));
        let t = std::time::Instant::now();
        let e = p.call("x", &json!({}), MS).unwrap_err();
        assert!(e.contains("not returned") && t.elapsed() < Duration::from_millis(100), "{e}");
        assert_eq!(f.calls.load(Ordering::SeqCst), 1, "the library was entered once");
        assert_eq!(p.failures.load(Ordering::SeqCst), 2);
        std::thread::sleep(Duration::from_millis(400));
        assert!(p.call("x", &json!({}), MS).is_ok(), "free again once the old call returned");
    }

    #[test]
    fn a_hung_load_is_abandoned_and_can_be_retried_after_switching_off() {
        let mut list = vec![bare("slow", "x", "")];
        let hang = stub_loader(|_| { std::thread::sleep(Duration::from_millis(400)); Err("late".into()) });
        let en = vec!["slow".to_string()];
        let t = std::time::Instant::now();
        let errs = sync_with(&mut list, &en, &hang, Duration::from_millis(40));
        assert!(t.elapsed() < Duration::from_millis(300));
        assert!(errs.len() == 1 && errs[0].contains("did not finish loading"), "{errs:?}");
        assert!(sync_with(&mut list, &en, &hang, Duration::from_millis(40)).is_empty(), "reported once");
        // switch off, then on again: tries again
        sync_with(&mut list, &[], &hang, Duration::from_millis(40));
        assert_eq!(sync_with(&mut list, &en, &hang, Duration::from_millis(40)).len(), 1);
    }

    #[test]
    fn a_panicking_loader_is_a_load_failure() {
        let mut list = vec![bare("p", "x", "")];
        let l = stub_loader(|_| panic!("manifest exploded"));
        let errs = sync_with(&mut list, &["p".to_string()], &l, Duration::from_millis(500));
        assert!(errs[0].contains("panicked"), "{errs:?}");
    }

    #[test]
    fn duplicate_ids_never_load() {
        let mut a = bare("one", "a", "");
        a.backend = Some(Arc::new(Recorder(Mutex::new(vec![]))));
        let mut b = bare("file:b.dll", "b", "");
        b.verified = false;
        let mut list = vec![a, b];
        let l = stub_loader(|_| Ok((stub_meta("one"), Arc::new(Recorder(Mutex::new(vec![]))) as Arc<dyn PluginBackend>)));
        let errs = sync_with(&mut list, &["one".to_string(), "file:b.dll".to_string()], &l, MS);
        assert!(errs.len() == 1 && errs[0].contains("already uses the id one"), "{errs:?}");
        assert!(list[1].backend.is_none() && list[1].load_failed);
    }

    #[test]
    fn sidecar_id_must_match_the_library() {
        let mut list = vec![bare("claimed", "x", "")];
        let l = stub_loader(|_| Ok((stub_meta("other"), Arc::new(Recorder(Mutex::new(vec![]))) as Arc<dyn PluginBackend>)));
        let errs = sync_with(&mut list, &["claimed".to_string()], &l, MS);
        assert!(errs[0].contains("library says other") && list[0].backend.is_none());
    }

    #[test]
    fn manifest_ids_are_checked() {
        for bad in [json!({"id": "file:evil"}), json!({"id": "a\u{0}b"}), json!({"id": "a\nb"}), json!({"id": ""}), json!({})] {
            assert!(parse_manifest(&bad).is_err(), "{bad}");
        }
        assert!(parse_manifest(&json!({"id": "fine-id.1"})).is_ok());
    }

    #[test]
    fn api_version_does_not_truncate() {
        assert!(parse_manifest(&json!({"id": "x", "api": 4294967297u64})).is_err());
        assert!(parse_manifest(&json!({"id": "x", "api": 1})).is_ok());
    }

    #[test]
    fn enabling_is_bound_to_the_file() {
        let list = vec![bare("a", "C:/p/a.dll", "h1"), bare("b", "C:/p/b.dll", "h2")];
        let en = vec!["a".to_string(), "b".to_string(), "gone".to_string()];
        let bindings = json!({"a": "C:/p/a.dll|h1", "b": "C:/p/b.dll|OLD", "gone": "x|y"});
        assert_eq!(bound_enabled(&list, &en, &bindings), ["a"]);
        assert!(bound_enabled(&list, &en, &json!({})).is_empty(), "no binding, no consent");
    }

    #[test]
    fn rediscovery_keeps_state_and_drops_replaced_files() {
        let mut keep = bare("k", "p", "h");
        keep.failures.store(2, Ordering::SeqCst);
        let old = vec![keep, bare("r", "p2", "old")];
        let new = vec![bare("k", "p", "h"), bare("r", "p2", "new"), bare("n", "p3", "z")];
        let merged = merge_discovered(old, new);
        assert_eq!(merged.len(), 3);
        assert_eq!(merged[0].failures.load(Ordering::SeqCst), 2, "unchanged entry keeps its state");
        assert_eq!(merged[1].fingerprint, "new");
    }

    #[test]
    fn upgrade_notice_when_a_plugin_is_installed_but_off() {
        let list = vec![bare("a", "p", "")];
        assert!(has_unenabled(&list, &[]));
        assert!(!has_unenabled(&list, &["a".to_string()]));
        assert!(!has_unenabled(&[], &[]));
    }

    #[test]
    fn fingerprint_changes_with_the_file() {
        let d = temp_folder("fp");
        let f = d.join("x.bin");
        std::fs::write(&f, b"one").unwrap();
        let a = fingerprint(&f);
        std::fs::write(&f, b"two").unwrap();
        assert_ne!(a, fingerprint(&f));
        let _ = std::fs::remove_dir_all(&d);
    }
}
