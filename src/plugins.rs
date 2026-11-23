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
//!   Names are Barbican's own; `plugin_catalog::HOOK_ALIASES` maps them to Bulwark's (`onEmailOpen`, `onBeforeEmailSend`),
//!   and a manifest may list either spelling.
//! * `settings`       input: `{}`; output: `{"rows":[...settings rows like ui/settings-schema.json...]}`
//!
//! Safety:
//! * A native library runs code the moment it is loaded (its `DllMain`/constructors), and the C ABI only exposes the
//!   manifest after loading. So the manifest is read from an optional sidecar `<name>.manifest.json` (or `manifest.json`
//!   when the folder holds a single library). Without a sidecar the plugin is listed by file name, unverified, and its
//!   real manifest is read only after the user enables it. Enabling it still runs its code in this process: only
//!   enable plugins you trust. A sidecar that disagrees with the library's own manifest id is rejected.
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
    backend: Option<Arc<dyn PluginBackend>>,
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

    /// Call a hook on a worker thread; give up after `budget`. Counts toward the failure limit.
    pub fn call(&self, hook: &str, input: &Value, budget: Duration) -> Result<Option<Value>, String> {
        let Some(backend) = self.backend.clone() else { return Err("not loaded".into()) };
        let (tx, rx) = mpsc::channel();
        let (h, i) = (hook.to_string(), input.clone());
        let spawned = std::thread::Builder::new().name(format!("plugin-{}", self.key)).spawn(move || {
            let r = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| backend.call(&h, &i)))
                .unwrap_or_else(|_| Err("plugin panicked".to_string()));
            let _ = tx.send(r);
        });
        let r = match spawned {
            Err(e) => Err(format!("could not start worker: {e}")),
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
    if v["api"].as_u64().unwrap_or(1) as u32 > API_VERSION { return Err(format!("needs plugin API {} (this app has {API_VERSION})", v["api"])); }
    Ok(Meta {
        id: v["id"].as_str().filter(|s| !s.is_empty()).ok_or("manifest has no id")?.to_string(),
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
            path: p.display().to_string(), verified, backend: None, load_failed: false, failures: AtomicU32::new(0), tripped: AtomicBool::new(false) });
    }
    (out, errors)
}

/// Load the plugins the user enabled and unload the ones switched off. Cheap when nothing changed.
/// Returns load errors (each reported once).
pub fn sync(plugins: &mut [Plugin], enabled: &[String]) -> Vec<String> {
    let mut errors = vec![];
    for p in plugins.iter_mut() {
        if !enabled.contains(&p.key) { p.backend = None; continue; }
        if p.backend.is_some() || p.load_failed || p.is_tripped() { continue; }
        match unsafe { load(std::path::Path::new(&p.path)) } {
            Ok((m, _)) if p.verified && m.id != p.id => {
                p.load_failed = true;
                errors.push(format!("{}: manifest.json says id {} but the library says {}", p.path, p.id, m.id));
            }
            Ok((m, b)) => {
                if !p.verified { (p.id, p.name, p.version, p.description, p.hooks) = (m.id, m.name, m.version, m.description, m.hooks); }
                p.backend = Some(b);
            }
            Err(e) => { p.load_failed = true; errors.push(format!("{}: {e}", p.path)); }
        }
    }
    errors
}

unsafe fn load(p: &std::path::Path) -> Result<(Meta, Arc<dyn PluginBackend>), String> {
    let lib = libloading::Library::new(p).map_err(|e| e.to_string())?;
    let manifest: libloading::Symbol<ManifestFn> = lib.get(b"barbican_plugin_manifest\0").map_err(|_| "missing barbican_plugin_manifest")?;
    let call: libloading::Symbol<CallFn> = lib.get(b"barbican_plugin_call\0").map_err(|_| "missing barbican_plugin_call")?;
    let free: libloading::Symbol<FreeFn> = lib.get(b"barbican_plugin_free\0").map_err(|_| "missing barbican_plugin_free")?;
    let m = manifest();
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
            backend: Some(f.clone()), load_failed: false, failures: AtomicU32::new(0), tripped: AtomicBool::new(false) };
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
            path: String::new(), verified: true, backend: Some(backend), load_failed: false, failures: AtomicU32::new(0), tripped: AtomicBool::new(false) };
        assert!(p.handles("email.details"));
        let email = json!({"headers": [{"name": "X-Spam-Status", "value": "Yes, score=7.5 required=5.0"}]});
        let rows = email_details(std::slice::from_ref(&p), &on(&p), &email);
        assert_eq!(rows.len(), 1);
        assert_eq!(rows[0].2, "danger");
        assert!(rows[0].1.contains("7.50 / 5.00"), "{}", rows[0].1);
    }
}
