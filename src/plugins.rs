//! Native plugin host.
//!
//! Drop a plugin DLL into `%APPDATA%\Barbican\plugins\` and it is loaded at start-up.
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
//! * `settings`       input: `{}`; output: `{"rows":[...settings rows like ui/settings-schema.json...]}`
use serde_json::{json, Value};
use std::ffi::{c_char, CStr, CString};

pub const API_VERSION: u32 = 1;

type ManifestFn = unsafe extern "C" fn() -> *const c_char;
type CallFn = unsafe extern "C" fn(*const c_char, *const c_char) -> *mut c_char;
type FreeFn = unsafe extern "C" fn(*mut c_char);

pub struct Plugin {
    pub id: String,
    pub name: String,
    pub version: String,
    pub description: String,
    pub hooks: Vec<String>,
    pub path: String,
    call: CallFn,
    free: FreeFn,
    _lib: libloading::Library,
}

impl Plugin {
    pub fn handles(&self, hook: &str) -> bool {
        self.hooks.iter().any(|h| h == hook)
    }

    pub fn call(&self, hook: &str, input: &Value) -> Option<Value> {
        let h = CString::new(hook).ok()?;
        let i = CString::new(input.to_string()).ok()?;
        // A panicking or crashing plugin must not take the app down with it.
        let out = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| unsafe { (self.call)(h.as_ptr(), i.as_ptr()) })).ok()?;
        if out.is_null() { return None; }
        let text = unsafe { CStr::from_ptr(out) }.to_string_lossy().to_string();
        unsafe { (self.free)(out) };
        serde_json::from_str(&text).ok()
    }
}

pub fn dir() -> std::path::PathBuf {
    crate::config_dir().join("plugins")
}

/// Load every plugin DLL in the plugins folder.
pub fn load_all() -> (Vec<Plugin>, Vec<String>) {
    let mut out = vec![];
    let mut errors = vec![];
    let _ = std::fs::create_dir_all(dir());
    let Ok(entries) = std::fs::read_dir(dir()) else { return (out, errors) };
    for e in entries.flatten() {
        let p = e.path();
        if !p.extension().is_some_and(|x| x.eq_ignore_ascii_case(std::env::consts::DLL_EXTENSION)) { continue; }
        match unsafe { load(&p) } {
            Ok(pl) => {
                if out.iter().any(|x: &Plugin| x.id == pl.id) { errors.push(format!("{}: duplicate id {}", p.display(), pl.id)); continue; }
                out.push(pl);
            }
            Err(err) => errors.push(format!("{}: {err}", p.display())),
        }
    }
    (out, errors)
}

unsafe fn load(p: &std::path::Path) -> Result<Plugin, String> {
    let lib = libloading::Library::new(p).map_err(|e| e.to_string())?;
    let manifest: libloading::Symbol<ManifestFn> = lib.get(b"barbican_plugin_manifest\0").map_err(|_| "missing barbican_plugin_manifest")?;
    let call: libloading::Symbol<CallFn> = lib.get(b"barbican_plugin_call\0").map_err(|_| "missing barbican_plugin_call")?;
    let free: libloading::Symbol<FreeFn> = lib.get(b"barbican_plugin_free\0").map_err(|_| "missing barbican_plugin_free")?;
    let m = manifest();
    if m.is_null() { return Err("empty manifest".into()); }
    let v: Value = serde_json::from_str(&CStr::from_ptr(m).to_string_lossy()).map_err(|e| format!("bad manifest: {e}"))?;
    if v["api"].as_u64().unwrap_or(1) as u32 > API_VERSION { return Err(format!("needs plugin API {} (this app has {API_VERSION})", v["api"])); }
    let (call, free) = (*call, *free);
    Ok(Plugin {
        id: v["id"].as_str().ok_or("manifest has no id")?.to_string(),
        name: v["name"].as_str().unwrap_or("Plugin").to_string(),
        version: v["version"].as_str().unwrap_or("").to_string(),
        description: v["description"].as_str().unwrap_or("").to_string(),
        hooks: v["hooks"].as_array().map(|a| a.iter().filter_map(|h| h.as_str().map(String::from)).collect()).unwrap_or_default(),
        path: p.display().to_string(),
        call,
        free,
        _lib: lib,
    })
}

/// Rows from every enabled plugin for the open message.
pub fn email_details(plugins: &[Plugin], disabled: &[String], email: &Value) -> Vec<(String, String, String)> {
    let mut rows = vec![];
    for p in plugins.iter().filter(|p| p.handles("email.details") && !disabled.contains(&p.id)) {
        if let Some(v) = p.call("email.details", email) {
            for r in v["rows"].as_array().cloned().unwrap_or_default() {
                rows.push((r["label"].as_str().unwrap_or("").to_string(), r["value"].as_str().unwrap_or("").to_string(), r["tone"].as_str().unwrap_or("neutral").to_string()));
            }
        }
    }
    rows
}

/// First warning any enabled plugin raises before sending.
pub fn compose_check(plugins: &[Plugin], disabled: &[String], draft: &Value) -> Option<String> {
    plugins.iter().filter(|p| p.handles("compose.check") && !disabled.contains(&p.id))
        .find_map(|p| p.call("compose.check", draft).and_then(|v| v["warning"].as_str().filter(|w| !w.is_empty()).map(String::from)))
}

pub fn describe(p: &Plugin) -> Value {
    json!({"id": p.id, "name": p.name, "version": p.version, "description": p.description, "hooks": p.hooks, "path": p.path})
}

#[cfg(test)]
mod tests {
    #[test]
    fn loads_spam_score_plugin() {
        let dll = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("plugins/spam-score/target/release/barbican_spam_score.dll");
        if !dll.exists() { eprintln!("example plugin not built; skipping"); return; }
        let p = unsafe { super::load(&dll) }.expect("load");
        assert_eq!(p.id, "spam-score");
        assert!(p.handles("email.details"));
        let email = serde_json::json!({"headers": [{"name": "X-Spam-Status", "value": "Yes, score=7.5 required=5.0"}]});
        let rows = super::email_details(&[p], &[], &email);
        assert_eq!(rows.len(), 1);
        assert_eq!(rows[0].2, "danger");
        assert!(rows[0].1.contains("7.50 / 5.00"), "{}", rows[0].1);
    }
}
