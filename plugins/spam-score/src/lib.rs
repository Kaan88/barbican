//! Spam Score — a Barbican native plugin (port of Bulwark's spam-score plugin).
//! Build: `cargo build --release`, then copy target/release/barbican_spam_score.dll
//! into %APPDATA%\Barbican\plugins\ and restart Barbican.
use serde_json::{json, Value};
use std::ffi::{c_char, CStr, CString};

mod spam;

static MANIFEST: &str = concat!(
    r#"{"api":1,"id":"spam-score","name":"Spam Score","version":""#,
    env!("CARGO_PKG_VERSION"),
    r#"","description":"Shows the spam analysis of a message from its spam headers.","hooks":["email.details"]}"#,
    "\0"
);

#[no_mangle]
pub extern "C" fn barbican_plugin_manifest() -> *const c_char {
    MANIFEST.as_ptr() as *const c_char
}

#[no_mangle]
pub extern "C" fn barbican_plugin_call(hook: *const c_char, input: *const c_char) -> *mut c_char {
    if hook.is_null() || input.is_null() { return std::ptr::null_mut(); }
    let hook = unsafe { CStr::from_ptr(hook) }.to_string_lossy();
    let input: Value = serde_json::from_str(&unsafe { CStr::from_ptr(input) }.to_string_lossy()).unwrap_or(Value::Null);
    let out = match hook.as_ref() {
        "email.details" => {
            let headers = input["headers"].as_array().cloned().unwrap_or_default();
            match spam::analyze(&headers) {
                Some(s) => json!({"rows": [{
                    "label": "Spam analysis",
                    "value": format!("{}  ·  {}", match s.verdict { "spam" => "Spam", "suspicious" => "Suspicious", _ => "Clean" }, s.detail),
                    "tone": match s.verdict { "spam" => "danger", "suspicious" => "warning", _ => "success" }
                }]}),
                None => json!({"rows": []}),
            }
        }
        _ => return std::ptr::null_mut(),
    };
    CString::new(out.to_string()).map(|c| c.into_raw()).unwrap_or(std::ptr::null_mut())
}

#[no_mangle]
pub extern "C" fn barbican_plugin_free(s: *mut c_char) {
    if !s.is_null() { unsafe { drop(CString::from_raw(s)) }; }
}
