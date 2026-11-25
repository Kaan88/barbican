//! The assistant's settings (spec 4): one object with a slot per provider kind, so the
//! OpenAI-compatible service, Anthropic and Claude on this computer can all be set up at once; the
//! provider list and the pick the panel's menu uses (4.3, 4.5); the connection test and the
//! Anthropic model list (4.4); the migration from Barbican's earlier assistant settings.
//!
//! Where things live:
//! * The object is the `"assistant"` entry of `settings.json` (the desktop and assistant settings
//!   file next to `prefs.json`, which settings export, import and reset already cover). It holds
//!   no key.
//! * The two keys are in the OS keystore (`crate::secrets`), as `{"key": …}` under
//!   [`OPENAI_SECRET`] and [`ANTHROPIC_SECRET`]. Claude on this computer has no key.
//! * Nothing here syncs: settings sync reads `prefs.json` only, and the settings page's rows for
//!   these fields use `ai.` keys, which sync refuses (`settings_sync::denied`: any dotted key).
//!   The addresses, the actions and the lookups switch decide where mail goes and what the
//!   assistant may do; the instructions and prompts steer it. A sharee of a shared Inbox, who can
//!   write the synced blob, must not be able to set any of them on this computer.
//!
//! * Each key is bound to the origin of the address it was saved with (`{"key", "origin"}`). A
//!   request goes out with a stored key only to that origin, so a settings file imported with
//!   another address cannot send a stored key there ([`key_check`]).
//!
//! Until the swap milestone (M12) nothing calls [`Settings::save`], so [`Settings::load`] keeps
//! migrating from the earlier settings, which the current settings page still edits. The first
//! save writes the new object; from then on it wins.
//!
//! Deferred to M12 (the reader settings are being reworked in parallel, and `ai.rs` still runs
//! the assistant until then):
//! * moving the reader settings `show_clean_spam`, `render_mode` and `remote_images` out of
//!   `ai::Config` (they are not assistant settings and have no place here);
//! * deleting the earlier key's entry [`OLD_SECRET`] once `ai.rs` is gone (the migration only
//!   reads it).
use super::anthropic::{self, ModelInfo};
use super::error::ProviderError;
use super::history::{ChatOutcome, Entry, ToolDef};
use super::openai::{trim_url, OpenAi};
use super::sse::Cancel;
use serde_json::{json, Map, Value};

/// `DEFAULT_ANTHROPIC_URL`, `DEFAULT_ANTHROPIC_MODEL`, `DEFAULT_CLAUDE_MODEL`.
pub const DEFAULT_ANTHROPIC_URL: &str = anthropic::DEFAULT_BASE_URL;
pub const DEFAULT_ANTHROPIC_MODEL: &str = anthropic::DEFAULT_MODEL;
pub const DEFAULT_CLAUDE_MODEL: &str = "claude-sonnet-5-5";

/// The keystore entries of the two keys.
pub const OPENAI_SECRET: &str = "ai-openai-key";
pub const ANTHROPIC_SECRET: &str = "ai-anthropic-key";
/// The single key of the earlier settings (read by the migration; `ai.rs` still uses it).
pub const OLD_SECRET: &str = "ai-api-key";

/// `CLAUDE_MODELS`: what Claude on this computer offers (`default` is Claude Code's own pick).
pub const CLAUDE_MODELS: [(&str, &str); 5] = [
    ("claude-fable-5-1", "Fable 5.1"),
    ("claude-opus-5-5", "Opus 5.5"),
    ("claude-sonnet-5-5", "Sonnet 5.5"),
    ("claude-haiku-5-5", "Haiku 5.5"),
    ("default", "Claude Code default"),
];

/// `claudeModelName(id)`
pub fn claude_model_name(id: &str) -> String {
    CLAUDE_MODELS.iter().find(|(i, _)| *i == id).map(|(_, n)| n.to_string()).unwrap_or_else(|| id.to_string())
}

/// A provider kind, and the id the settings and the menu use for it.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Kind {
    OpenAi,
    Anthropic,
    Claude,
}

impl Kind {
    pub fn id(self) -> &'static str {
        match self {
            Kind::OpenAi => "openai",
            Kind::Anthropic => "anthropic",
            Kind::Claude => "claude",
        }
    }

    pub fn parse(s: &str) -> Option<Kind> {
        match s {
            "openai" => Some(Kind::OpenAi),
            "anthropic" => Some(Kind::Anthropic),
            "claude" => Some(Kind::Claude),
            _ => None,
        }
    }

    /// The label in the panel and on the settings page.
    pub fn label(self) -> &'static str {
        match self {
            Kind::OpenAi => "OpenAI-compatible",
            Kind::Anthropic => "Anthropic",
            Kind::Claude => "Claude on this computer",
        }
    }
}

/// `actions`: what the assistant may do on its own.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Actions {
    /// the assistant only answers
    Off,
    /// each action waits for the user's OK
    Ask,
    /// actions run without asking (sending and deleting still ask)
    Auto,
}

impl Actions {
    pub fn id(self) -> &'static str {
        match self {
            Actions::Off => "off",
            Actions::Ask => "ask",
            Actions::Auto => "auto",
        }
    }

    /// An unknown value counts as the default, Ask first.
    pub fn parse(s: &str) -> Actions {
        match s {
            "off" => Actions::Off,
            "auto" => Actions::Auto,
            _ => Actions::Ask,
        }
    }
}

/// The settings object (`DEFAULTS` of `lib/settings.js`, plus Barbican's two extras at the end).
#[derive(Clone, PartialEq)]
pub struct Settings {
    /// `defaultProvider`: used when several services are set up (an unknown value reads as
    /// OpenAI-compatible, which picks the same as the extension: that entry comes first anyway)
    pub default_provider: Kind,
    /// `openaiBaseUrl`, up to and including `/v1`; empty means not used
    pub openai_base_url: String,
    /// `openaiKey`: in the keystore, optional (Ollama and LM Studio need none)
    pub openai_key: String,
    pub openai_model: String,
    pub anthropic_base_url: String,
    /// `anthropicKey`: in the keystore
    pub anthropic_key: String,
    pub anthropic_model: String,
    /// `claudeLocal`: use the Claude Code CLI on this computer
    pub claude_local: bool,
    pub claude_model: String,
    /// `claudePath`: empty means found automatically
    pub claude_path: String,
    /// `anthropicModelList`: fetched from the Models API (only `claude-` ids)
    pub anthropic_model_list: Vec<ModelInfo>,
    /// `hiddenModels`: `anthropic:<id>` and `claude:<id>` left out of the panel's menu
    pub hidden_models: Vec<String>,
    /// `effort`: `""` (model default), `low`, `medium`, `high` or `max`
    pub effort: String,
    pub instructions: String,
    pub actions: Actions,
    /// `lookups`: let the assistant look things up in mail, calendar and contacts
    pub lookups: bool,
    /// `prompts`: saved prompts, one per line, `Name: what to ask`
    pub prompts: String,
    /// `translateTo`
    pub translate_to: String,
    /// Barbican: keep remembered notes (`memory.txt`), as the earlier settings had
    pub memory: bool,
    /// Barbican: the assistant panel and quick actions are hidden (`ai.visible` off)
    pub hidden: bool,
}

/// The origin of an address (`scheme://host[:port]`), empty when it does not parse.
pub fn origin_of(base_url: &str) -> String {
    reqwest::Url::parse(&trim_url(base_url)).map(|u| u.origin().ascii_serialization()).unwrap_or_default()
}

// ---------------------------------------------------------------- keys bound to their origin
//
// Every key entry is `{"key", "origin"}`: the key and the origin (`scheme://host[:port]`) of the
// address it was saved with. The keystore entry is the only record of that binding: a settings
// file (imported, edited, or migrated from) can change an address but never the origin a key is
// bound to, so it can never send a stored key somewhere else.

/// A keystore entry by name: the key (trimmed, never empty) and its origin, none for an entry an
/// earlier build wrote.
fn read_entry(name: &str) -> Option<(String, Option<String>)> {
    let (key, origin) = entry_of(&crate::secrets::get(name)?);
    let key = key.trim().to_string();
    (!key.is_empty()).then_some((key, origin))
}

fn write_entry(name: &str, key: &str, origin: &str) -> bool {
    crate::secrets::set(name, &json!({"key": key, "origin": origin}).to_string())
}

/// Trust on first use: binds each key entry that has no origin yet (an earlier build wrote it) to
/// the address `settings.json` uses it with now. Called by every load, the first of them at
/// startup, before a settings file can be imported. Nothing is bound while the file is there but
/// does not parse, nor for a slot without an address: the next load that has one binds it.
pub(crate) fn bind_unbound_keys() {
    let Ok(file) = read_file() else { return };
    let a = file.get("assistant").filter(|a| a.is_object());
    // as `ai::Config::load` reads the earlier settings
    let old_default = if file["provider"].as_i64().unwrap_or(0) >= 1 { DEFAULT_ANTHROPIC_URL } else { OLD_OPENAI_URL };
    let slots = [
        (OLD_SECRET, Some(file["base"].as_str().unwrap_or(old_default))),
        (OPENAI_SECRET, a.and_then(|a| a["openaiBaseUrl"].as_str())),
        // a missing entry in a stored object is the default address, as `from_value` reads it
        (ANTHROPIC_SECRET, a.map(|a| a["anthropicBaseUrl"].as_str().unwrap_or(DEFAULT_ANTHROPIC_URL))),
    ];
    for (name, base) in slots {
        let Some(base) = base.filter(|b| !b.trim().is_empty()) else { continue };
        if let Some((key, None)) = read_entry(name) {
            write_entry(name, &key, &origin_of(base));
        }
    }
}

/// The user's own committed edit of an address (the settings page: imports, sync and the
/// assistant's `change_setting` never edit `ai.` settings): the stored key the slot uses, `key`,
/// moves with it to the new origin. An address with no origin moves nothing.
fn rebind(slot: &str, key: &str, base_url: &str) {
    let key = key.trim();
    let origin = origin_of(base_url);
    if origin.is_empty() {
        return;
    }
    for name in [slot, OLD_SECRET] {
        if read_entry(name).is_some_and(|(k, _)| k == key) {
            write_entry(name, key, &origin);
        }
    }
}

/// [`rebind`] for the earlier settings' key (`ai.rs`, entry [`OLD_SECRET`]).
pub(crate) fn rebind_old_key(key: &str, base_url: &str) {
    rebind(OLD_SECRET, key, base_url)
}

/// The origin `key` is stored with, when it is the key of the slot's entry or of the earlier
/// entry (a migrated key): `Some(None)` for a stored key without one; none for a key not stored
/// (typed since).
fn stored_origin(slot: &str, key: &str) -> Option<Option<String>> {
    [slot, OLD_SECRET].into_iter().filter_map(read_entry).find(|(k, _)| k == key).map(|(_, o)| o)
}

/// Whether `key` may go to `base_url`: refused when it is a stored key saved for another origin
/// (or for none); a key not stored is the one just typed.
fn check_stored(slot: &str, key: &str, base_url: &str, kind: Kind) -> Result<(), String> {
    let key = key.trim();
    let to = origin_of(base_url);
    match stored_origin(slot, key) {
        Some(Some(o)) if o == to => Ok(()),
        Some(o) if !key.is_empty() => Err(refusal(kind.label(), o.as_deref().filter(|o| !o.is_empty()), &to)),
        _ => Ok(()),
    }
}

/// The refusal: it never suggests moving the key to the new address by editing it (an import may
/// have put it there), only setting the old one back or entering the key again.
fn refusal(label: &str, from: Option<&str>, to: &str) -> String {
    match from {
        Some(from) => format!(
            "The {label} API key was saved for {from}, but the address is now {to}. If you did not change it (an import can), set it back to {from}. To use {to}, clear the key and enter it again."
        ),
        None => format!("The {label} API key was saved for another address, but the address is now {to}. To use {to}, clear the key and enter it again."),
    }
}

/// [`key_check`] for the earlier settings' key (`ai.rs`, entry [`OLD_SECRET`]).
pub(crate) fn check_old_key(key: &str, base_url: &str, kind: Kind) -> Result<(), String> {
    check_stored(OLD_SECRET, key, base_url, kind)
}

/// Saves `key` into `slot` (a blank key removes the entry). A key already stored keeps the origin
/// it is bound to; only a key that differs from the stored one is bound to `base_url` now.
fn save_key(slot: &str, key: &str, base_url: &str) -> bool {
    let key = key.trim();
    if key.is_empty() {
        crate::secrets::delete(slot);
        return true;
    }
    let origin = match stored_origin(slot, key) {
        Some(o) => o.unwrap_or_default(),
        None => origin_of(base_url),
    };
    write_entry(slot, key, &origin)
}

/// [`save_key`] for the earlier settings' key (`ai.rs`, entry [`OLD_SECRET`]).
pub(crate) fn save_old_key(key: &str, base_url: &str) -> bool {
    save_key(OLD_SECRET, key, base_url)
}

impl Default for Settings {
    fn default() -> Settings {
        Settings {
            default_provider: Kind::OpenAi,
            openai_base_url: String::new(),
            openai_key: String::new(),
            openai_model: String::new(),
            anthropic_base_url: DEFAULT_ANTHROPIC_URL.into(),
            anthropic_key: String::new(),
            anthropic_model: DEFAULT_ANTHROPIC_MODEL.into(),
            claude_local: false,
            claude_model: DEFAULT_CLAUDE_MODEL.into(),
            claude_path: String::new(),
            anthropic_model_list: Vec::new(),
            hidden_models: Vec::new(),
            effort: String::new(),
            instructions: String::new(),
            actions: Actions::Ask,
            lookups: true,
            prompts: String::new(),
            translate_to: "English".into(),
            memory: true,
            hidden: false,
        }
    }
}

impl std::fmt::Debug for Settings {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        let mut v = self.to_value();
        let hide = |k: &str| if k.trim().is_empty() { json!("") } else { json!("<redacted>") };
        v["openaiKey"] = hide(&self.openai_key);
        v["anthropicKey"] = hide(&self.anthropic_key);
        write!(f, "Settings {v}")
    }
}

/// The keys of the settings page's rows for these fields: `ai.<field>`.
pub fn row_key(field: &str) -> String {
    format!("ai.{field}")
}

/// Every field of the object by its name in the extension (the two keys included).
pub const FIELDS: [&str; 20] = [
    "defaultProvider", "openaiBaseUrl", "openaiKey", "openaiModel", "anthropicBaseUrl", "anthropicKey", "anthropicModel",
    "claudeLocal", "claudeModel", "claudePath", "anthropicModelList", "hiddenModels", "effort", "instructions", "actions",
    "lookups", "prompts", "translateTo", "memory", "hidden",
];

fn text(o: &Map<String, Value>, k: &str, d: &str) -> String {
    o.get(k).and_then(Value::as_str).unwrap_or(d).to_string()
}

fn flag(o: &Map<String, Value>, k: &str, d: bool) -> bool {
    o.get(k).and_then(Value::as_bool).unwrap_or(d)
}

impl Settings {
    /// The stored object merged over the defaults (`{...DEFAULTS, ...settings}`); a value of the
    /// wrong type counts as missing. Keys stored in the object are ignored: they come from the
    /// keystore.
    pub fn from_value(v: &Value, openai_key: &str, anthropic_key: &str) -> Settings {
        let d = Settings::default();
        let empty = Map::new();
        let o = v.as_object().unwrap_or(&empty);
        let list = |k: &str| o.get(k).and_then(Value::as_array).cloned().unwrap_or_default();
        Settings {
            default_provider: o.get("defaultProvider").and_then(Value::as_str).and_then(Kind::parse).unwrap_or(d.default_provider),
            openai_base_url: text(o, "openaiBaseUrl", &d.openai_base_url),
            openai_key: openai_key.to_string(),
            openai_model: text(o, "openaiModel", &d.openai_model),
            anthropic_base_url: text(o, "anthropicBaseUrl", &d.anthropic_base_url),
            anthropic_key: anthropic_key.to_string(),
            anthropic_model: text(o, "anthropicModel", &d.anthropic_model),
            claude_local: flag(o, "claudeLocal", d.claude_local),
            claude_model: text(o, "claudeModel", &d.claude_model),
            claude_path: text(o, "claudePath", &d.claude_path),
            anthropic_model_list: list("anthropicModelList")
                .iter()
                .filter_map(|m| Some(ModelInfo { id: m["id"].as_str()?.to_string(), name: m["name"].as_str().unwrap_or("").to_string() }))
                .collect(),
            hidden_models: list("hiddenModels").iter().filter_map(|m| m.as_str().map(String::from)).collect(),
            effort: text(o, "effort", &d.effort),
            instructions: text(o, "instructions", &d.instructions),
            actions: o.get("actions").and_then(Value::as_str).map(Actions::parse).unwrap_or(d.actions),
            lookups: flag(o, "lookups", d.lookups),
            prompts: text(o, "prompts", &d.prompts),
            translate_to: text(o, "translateTo", &d.translate_to),
            memory: flag(o, "memory", d.memory),
            hidden: flag(o, "hidden", d.hidden),
        }
    }

    /// The user's own committed edit of the OpenAI-compatible address (the settings page, once the
    /// field is accepted or left, never per keystroke): the stored key moves with it. A file (an
    /// import, a sync) never goes through here.
    pub fn set_openai_base_url(&mut self, url: &str) {
        self.openai_base_url = url.trim().to_string();
        rebind(OPENAI_SECRET, &self.openai_key, &self.openai_base_url);
    }

    /// [`set_openai_base_url`](Settings::set_openai_base_url) for the Anthropic address.
    pub fn set_anthropic_base_url(&mut self, url: &str) {
        self.anthropic_base_url = url.trim().to_string();
        rebind(ANTHROPIC_SECRET, &self.anthropic_key, &self.anthropic_base_url);
    }

    /// The object as stored: every field but the two keys.
    pub fn to_value(&self) -> Value {
        json!({
            "version": 2,
            "defaultProvider": self.default_provider.id(),
            "openaiBaseUrl": self.openai_base_url,
            "openaiModel": self.openai_model,
            "anthropicBaseUrl": self.anthropic_base_url,
            "anthropicModel": self.anthropic_model,
            "claudeLocal": self.claude_local,
            "claudeModel": self.claude_model,
            "claudePath": self.claude_path,
            "anthropicModelList": self.anthropic_model_list.iter().map(|m| json!({"id": m.id, "name": m.name})).collect::<Vec<_>>(),
            "hiddenModels": self.hidden_models,
            "effort": self.effort,
            "instructions": self.instructions,
            "actions": self.actions.id(),
            "lookups": self.lookups,
            "prompts": self.prompts,
            "translateTo": self.translate_to,
            "memory": self.memory,
            "hidden": self.hidden,
        })
    }

    /// Reads the settings: the `"assistant"` object of `settings.json` and the two keys, or (no
    /// object yet) the migration from the earlier settings.
    pub fn load() -> Settings {
        let file = read_file().unwrap_or(json!({}));
        bind_unbound_keys();
        Settings::load_from(&file, &|name| crate::secrets::get(name))
    }

    /// [`load`](Settings::load) over a given file and keystore (the keys only: the origins they
    /// are bound to stay in the keystore, see [`key_check`]).
    pub fn load_from(file: &Value, secret: &dyn Fn(&str) -> Option<String>) -> Settings {
        let key = |name: &str| secret(name).map(|s| entry_of(&s).0).unwrap_or_default();
        match file.get("assistant").filter(|a| a.is_object()) {
            Some(a) => Settings::from_value(a, &key(OPENAI_SECRET), &key(ANTHROPIC_SECRET)),
            None => migrate(file, &key(OLD_SECRET)),
        }
    }

    /// Saves the object into `settings.json` (keeping the file's other entries) and the keys into
    /// the keystore (a blank key removes its entry). Refuses to write over a `settings.json` that
    /// does not parse, so a damaged file is never replaced by one holding only this object.
    ///
    /// A key keeps the origin its keystore entry is bound to while it stays the same (a migrated
    /// key, the one of the earlier entry); only a key that differs from the stored one is bound to
    /// its slot's address now. So changing only the address does not move a stored key to it: the
    /// key has to be cleared and entered again.
    pub fn save(&mut self) -> Result<(), String> {
        let mut file = read_file()?;
        if !file.is_object() {
            file = json!({});
        }
        file["assistant"] = self.to_value();
        write_file(&file)?;
        for (name, key, base) in [
            (OPENAI_SECRET, &self.openai_key, &self.openai_base_url),
            (ANTHROPIC_SECRET, &self.anthropic_key, &self.anthropic_base_url),
        ] {
            if !save_key(name, key, base) {
                return Err(format!("The key could not be saved in {}.", crate::platform::keystore_name()));
            }
        }
        Ok(())
    }
}

/// Whether the settings' key for `kind` may go to `base_url`, read from the keystore: refused
/// when it is a stored key (its slot's, or the earlier entry's) saved for another origin (an
/// imported or edited settings file points the slot elsewhere). A key not stored is the one just
/// typed, and may go where it is tested.
pub fn key_check(s: &Settings, kind: Kind, base_url: &str) -> Result<(), ProviderError> {
    let (slot, key) = match kind {
        Kind::OpenAi => (OPENAI_SECRET, &s.openai_key),
        Kind::Anthropic => (ANTHROPIC_SECRET, &s.anthropic_key),
        Kind::Claude => return Ok(()),
    };
    check_stored(slot, key, base_url, kind).map_err(ProviderError::new)
}

/// A keystore entry: `{"key", "origin"}`, `{"key"}`, or an older bare key.
fn entry_of(text: &str) -> (String, Option<String>) {
    match serde_json::from_str::<Value>(text) {
        Ok(v) if v["key"].is_string() => (v["key"].as_str().unwrap_or("").to_string(), v["origin"].as_str().map(String::from)),
        _ => (text.to_string(), None),
    }
}

fn path() -> std::path::PathBuf {
    crate::config_dir().join("settings.json")
}

/// `settings.json` as JSON: `{}` when there is none; an error when it is there but unreadable.
pub(crate) fn read_file() -> Result<Value, String> {
    match std::fs::read_to_string(path()) {
        Ok(t) => serde_json::from_str(&t).map_err(|_| "The settings file could not be read, so nothing was saved.".to_string()),
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(json!({})),
        Err(e) => Err(format!("The settings file could not be read ({e}), so nothing was saved.")),
    }
}

/// Writes `settings.json` through a temporary file, so a crash never leaves half a file.
pub(crate) fn write_file(v: &Value) -> Result<(), String> {
    let p = path();
    let _ = std::fs::create_dir_all(crate::config_dir());
    let tmp = p.with_extension("json.tmp");
    std::fs::write(&tmp, v.to_string()).and_then(|_| std::fs::rename(&tmp, &p)).map_err(|e| {
        let _ = std::fs::remove_file(&tmp);
        format!("The settings could not be saved: {e}")
    })
}

/// The earlier OpenAI-compatible default address, which the earlier settings filled in for
/// everyone (and needed a key with).
const OLD_OPENAI_URL: &str = "https://api.openai.com/v1";
const OLD_OPENAI_MODEL: &str = "gpt-4o-mini";

/// The new settings from the earlier ones (`settings.json`'s top-level entries, read as
/// `ai::Config::load` reads them, and the key of `ai-api-key`):
///
/// | earlier | new |
/// |---|---|
/// | `provider` 0, `base`, `model`, key | the OpenAI-compatible slot, when a key was set or the address is not the earlier default (else left empty, as the extension starts) |
/// | `provider` 1, `base`, `model`, key | the Anthropic slot |
/// | `provider` | `defaultProvider` |
/// | `actions` 0 / 1 / 2 | `off` / `ask` / `auto` |
/// | `lang` | `translateTo` |
/// | `instructions`, `prompts` | as they are |
/// | `memory`, `hidden` | as they are (Barbican's extras) |
/// | `showCleanSpam`, `renderMode`, `remoteImages` | none: reader settings, left where they are |
///
/// The key keeps the origin its entry is bound to (never one from the file): see [`key_check`].
pub fn migrate(old: &Value, old_key: &str) -> Settings {
    let mut s = Settings::default();
    let o = old.as_object().cloned().unwrap_or_default();
    let provider = o.get("provider").and_then(Value::as_i64).unwrap_or(0).clamp(0, 1);
    let key = old_key.trim().to_string();
    let base = |d: &str| text(&o, "base", d);
    let model = |d: &str| Some(text(&o, "model", d)).filter(|m| !m.trim().is_empty()).unwrap_or_else(|| d.to_string());
    if provider == 1 {
        s.default_provider = Kind::Anthropic;
        s.anthropic_base_url = trim_url(&base(DEFAULT_ANTHROPIC_URL));
        s.anthropic_model = model(DEFAULT_ANTHROPIC_MODEL);
        s.anthropic_key = key;
    } else {
        s.default_provider = Kind::OpenAi;
        let url = trim_url(&base(OLD_OPENAI_URL));
        if !key.is_empty() || url != OLD_OPENAI_URL {
            s.openai_base_url = url;
            s.openai_model = model(OLD_OPENAI_MODEL);
            s.openai_key = key;
        }
    }
    s.actions = match o.get("actions").and_then(Value::as_i64).unwrap_or(1) {
        i64::MIN..=0 => Actions::Off,
        1 => Actions::Ask,
        _ => Actions::Auto,
    };
    s.translate_to = Some(text(&o, "lang", "English")).filter(|l| !l.trim().is_empty()).unwrap_or_else(|| "English".into());
    s.instructions = text(&o, "instructions", "");
    s.prompts = text(&o, "prompts", "");
    s.memory = flag(&o, "memory", true);
    s.hidden = flag(&o, "hidden", false);
    s
}

// ---------------------------------------------------------------- the provider list and the pick

/// One entry of the panel's menu (`availableProviders`).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Provider {
    /// `openai`, `anthropic:<model>` or `claude:<model>`
    pub id: String,
    pub kind: Kind,
    pub label: &'static str,
    /// `Anthropic · Opus 5.5`, `Claude · Sonnet 5.5`; none for the OpenAI-compatible entry
    pub title: Option<String>,
    pub model: String,
    /// empty for Claude on this computer
    pub base_url: String,
}

/// `hiddenKey(service, model)`
pub fn hidden_key(service: &str, model: &str) -> String {
    format!("{service}:{model}")
}

/// `anthropicModels(s)`: the fetched list, else the built-in one.
pub fn anthropic_models(s: &Settings) -> Vec<ModelInfo> {
    if !s.anthropic_model_list.is_empty() {
        return s.anthropic_model_list.clone();
    }
    anthropic::CLAUDE_API_MODELS.iter().map(|(id, name)| ModelInfo { id: id.to_string(), name: name.to_string() }).collect()
}

/// `visibleModels`: all known models plus the chosen default (put first when it is not known),
/// minus the hidden ones; just the default when everything is hidden.
pub fn visible_models(s: &Settings, service: &str, known: &[String], chosen: &str) -> Vec<String> {
    let mut all: Vec<String> = known.to_vec();
    if !all.iter().any(|m| m == chosen) {
        all.insert(0, chosen.to_string());
    }
    let shown: Vec<String> = all.into_iter().filter(|m| !s.hidden_models.contains(&hidden_key(service, m))).collect();
    if shown.is_empty() { vec![chosen.to_string()] } else { shown }
}

fn chosen_anthropic(s: &Settings) -> String {
    Some(s.anthropic_model.trim()).filter(|m| !m.is_empty()).unwrap_or(DEFAULT_ANTHROPIC_MODEL).to_string()
}

fn chosen_claude(s: &Settings) -> String {
    Some(s.claude_model.trim()).filter(|m| !m.is_empty()).unwrap_or(DEFAULT_CLAUDE_MODEL).to_string()
}

/// `availableProviders(s)`: the providers filled in enough to use, in menu order.
/// * OpenAI-compatible: an address and a model (the key is optional). One entry.
/// * Anthropic: an address and a key. One entry per visible model.
/// * Claude on this computer: when ticked. One entry per visible model.
pub fn available_providers(s: &Settings) -> Vec<Provider> {
    let mut list = Vec::new();
    let openai_url = trim_url(&s.openai_base_url);
    if !openai_url.is_empty() && !s.openai_model.trim().is_empty() {
        list.push(Provider {
            id: "openai".into(),
            kind: Kind::OpenAi,
            label: Kind::OpenAi.label(),
            title: None,
            model: s.openai_model.trim().into(),
            base_url: openai_url,
        });
    }
    let anthropic_url = trim_url(&s.anthropic_base_url);
    if !anthropic_url.is_empty() && !s.anthropic_key.trim().is_empty() {
        let known: Vec<String> = anthropic_models(s).into_iter().map(|m| m.id).collect();
        for model in visible_models(s, "anthropic", &known, &chosen_anthropic(s)) {
            list.push(Provider {
                id: format!("anthropic:{model}"),
                kind: Kind::Anthropic,
                label: Kind::Anthropic.label(),
                title: Some(format!("Anthropic · {}", anthropic::model_name(&model, &s.anthropic_model_list))),
                model,
                base_url: anthropic_url.clone(),
            });
        }
    }
    if s.claude_local {
        let known: Vec<String> = CLAUDE_MODELS.iter().map(|(id, _)| id.to_string()).collect();
        for model in visible_models(s, "claude", &known, &chosen_claude(s)) {
            list.push(Provider {
                id: format!("claude:{model}"),
                kind: Kind::Claude,
                label: Kind::Claude.label(),
                title: Some(format!("Claude · {}", claude_model_name(&model))),
                model,
                base_url: String::new(),
            });
        }
    }
    list
}

/// `pickProvider(s, preferred)`: the entry for `preferred` (a menu id, or a bare kind such as an
/// old saved `anthropic`), else one of the default service, else the first usable one.
pub fn pick_provider(s: &Settings, preferred: Option<&str>) -> Option<Provider> {
    let list = available_providers(s);
    let preferred = preferred.filter(|p| !p.is_empty());
    let default_model = match s.default_provider {
        Kind::Anthropic => Some(chosen_anthropic(s)),
        Kind::Claude => Some(chosen_claude(s)),
        Kind::OpenAi => None,
    };
    let pref_kind = preferred.map(|p| p.split(':').next().unwrap_or(""));
    let is_default_model = |p: &Provider| default_model.as_deref() == Some(p.model.as_str());
    let find = |f: &dyn Fn(&Provider) -> bool| list.iter().find(|p| f(p)).cloned();
    find(&|p| Some(p.id.as_str()) == preferred)
        // an old pick like "anthropic" (before per-model entries) or a hidden model
        .or_else(|| find(&|p| pref_kind == Some(p.kind.id()) && is_default_model(p)))
        .or_else(|| find(&|p| pref_kind == Some(p.kind.id())))
        .or_else(|| find(&|p| p.kind == s.default_provider && (default_model.is_none() || is_default_model(p))))
        .or_else(|| find(&|p| p.kind == s.default_provider))
        .or_else(|| list.first().cloned())
}

/// `savedPrompts(s)`: the `Name: what to ask` lines, as (name, prompt).
pub fn saved_prompts(s: &Settings) -> Vec<(String, String)> {
    s.prompts
        .split('\n')
        .filter_map(|line| {
            let line = line.strip_suffix('\r').unwrap_or(line);
            let i = line.find(':').filter(|&i| i > 0)?;
            let (name, prompt) = (line[..i].trim(), line[i + 1..].trim());
            (!name.is_empty() && !prompt.is_empty()).then(|| (name.to_string(), prompt.to_string()))
        })
        .collect()
}

// ---------------------------------------------------------------- one turn through a provider

/// `chat`: one model turn through the picked provider, with the settings' key and effort.
pub fn chat(
    s: &Settings,
    p: &Provider,
    system: &str,
    history: &[Entry],
    tools: &[ToolDef],
    cancel: &Cancel,
    on_text: &mut dyn FnMut(&str),
) -> Result<ChatOutcome, ProviderError> {
    key_check(s, p.kind, &p.base_url)?;
    match p.kind {
        Kind::Anthropic => {
            let mut a = anthropic::Anthropic::new(&p.base_url, &p.model, s.anthropic_key.trim());
            a.effort = s.effort.clone();
            a.chat(system, history, tools, cancel, on_text)
        }
        Kind::OpenAi => OpenAi::new(&p.base_url, &p.model, s.openai_key.trim()).chat(system, history, tools, cancel, on_text),
        // milestone M11
        Kind::Claude => Err(ProviderError::new("Claude on this computer is not available in Barbican yet.")),
    }
}

/// `complete`: a single prompt with no tools and no history; returns the text.
pub fn complete(s: &Settings, p: &Provider, system: &str, prompt: &str, cancel: &Cancel, on_text: &mut dyn FnMut(&str)) -> Result<String, ProviderError> {
    let history = [Entry::User { text: prompt.to_string() }];
    chat(s, p, system, &history, &[], cancel, on_text).map(|o| o.turn.text)
}

// ---------------------------------------------------------------- the settings page's buttons

pub const TEST_SYSTEM: &str = "You are a connection test.";
pub const TEST_PROMPT: &str = "Reply with the single word OK.";

/// `Test connection` for the OpenAI-compatible service or Anthropic, over the form's unsaved
/// values: the text to show and whether it worked.
pub fn test_connection(s: &Settings, kind: Kind, cancel: &Cancel) -> (String, bool) {
    let provider = pick_provider(s, Some(kind.id())).filter(|p| p.kind == kind);
    let Some(provider) = provider else {
        let text = if kind == Kind::Anthropic { "Fill in the address and the key first." } else { "Fill in the address and the model first." };
        return (text.into(), false);
    };
    match complete(s, &provider, TEST_SYSTEM, TEST_PROMPT, cancel, &mut |_| {}) {
        Ok(text) => {
            let said: String = text.trim().chars().take(40).collect();
            let said = if said.is_empty() { "nothing".to_string() } else { said };
            (format!("Works ({} said: {said}).", provider.model), true)
        }
        Err(e) => (e.message, false),
    }
}

/// The Models API list as the settings page keeps it: only `claude-` ids.
pub fn claude_only(list: Vec<ModelInfo>) -> Vec<ModelInfo> {
    list.into_iter().filter(|m| m.id.starts_with("claude-")).collect()
}

/// `Get the list from Anthropic`: fetches the list over the form's values and puts it into
/// `s.anthropic_model_list` (the caller saves `s`): the text to show and whether it worked.
pub fn refresh_models(s: &mut Settings, cancel: &Cancel) -> (String, bool) {
    let base = trim_url(&s.anthropic_base_url);
    if s.anthropic_key.trim().is_empty() || base.is_empty() {
        return ("Fill in the address and the key first.".into(), false);
    }
    if let Err(e) = key_check(s, Kind::Anthropic, &base) {
        return (e.message, false);
    }
    match anthropic::fetch_models(&base, &s.anthropic_key, cancel) {
        Ok(list) => {
            let list = claude_only(list);
            if list.is_empty() {
                return ("The list came back empty.".into(), false);
            }
            let n = list.len();
            s.anthropic_model_list = list;
            (format!("{n} models."), true)
        }
        Err(e) => (e.message, false),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::assistant::fake::{FakeServer, Reply};
    use std::collections::HashMap;

    fn with_anthropic(key: &str) -> Settings {
        Settings { anthropic_key: key.into(), ..Settings::default() }
    }

    fn ids(list: &[Provider]) -> Vec<&str> {
        list.iter().map(|p| p.id.as_str()).collect()
    }

    #[test]
    fn defaults_are_the_extensions() {
        let s = Settings::default();
        assert_eq!(s.default_provider, Kind::OpenAi);
        assert_eq!((s.openai_base_url.as_str(), s.openai_model.as_str()), ("", ""));
        assert_eq!(s.anthropic_base_url, "https://api.anthropic.com/v1");
        assert_eq!(s.anthropic_model, "claude-haiku-5-5");
        assert_eq!(s.claude_model, "claude-sonnet-5-5");
        assert!(!s.claude_local && s.lookups && s.memory && !s.hidden);
        assert_eq!((s.actions, s.effort.as_str(), s.translate_to.as_str()), (Actions::Ask, "", "English"));
        assert!(available_providers(&s).is_empty(), "nothing is usable out of the box");
        assert_eq!(pick_provider(&s, None), None);
    }

    #[test]
    fn stored_values_merge_over_the_defaults() {
        let v = json!({
            "defaultProvider": "claude", "openaiBaseUrl": "http://localhost:11434/v1", "openaiModel": "qwen3:8b",
            "claudeLocal": true, "hiddenModels": ["claude:default", 5], "effort": "max", "actions": "auto",
            "lookups": false, "translateTo": "Deutsch", "anthropicModelList": [{"id": "claude-x-1", "name": "X 1"}, {"name": "no id"}],
            "openaiKey": "sk-in-the-file", "anthropicKey": "sk-ant-in-the-file",
            "instructions": 7, "unknown": "kept out"
        });
        let s = Settings::from_value(&v, "sk-from-keystore", "");
        assert_eq!(s.default_provider, Kind::Claude);
        assert_eq!(s.openai_base_url, "http://localhost:11434/v1");
        assert_eq!(s.openai_key, "sk-from-keystore", "a key in the file is never used");
        assert_eq!(s.anthropic_key, "");
        assert!(s.claude_local);
        assert_eq!(s.hidden_models, ["claude:default"]);
        assert_eq!(s.anthropic_model_list, [ModelInfo { id: "claude-x-1".into(), name: "X 1".into() }]);
        assert_eq!((s.effort.as_str(), s.actions, s.lookups), ("max", Actions::Auto, false));
        assert_eq!(s.instructions, "", "a wrong type counts as missing");
        assert_eq!(s.anthropic_model, "claude-haiku-5-5");
        assert_eq!(Settings::from_value(&json!({"defaultProvider": "gemini", "actions": "sometimes"}), "", "").actions, Actions::Ask);
        // a round trip keeps everything, and the stored form has no key
        let back = Settings::from_value(&s.to_value(), &s.openai_key, &s.anthropic_key);
        assert_eq!(back, s);
        let stored = s.to_value();
        assert!(stored.get("openaiKey").is_none() && stored.get("anthropicKey").is_none());
        assert!(!stored.to_string().contains("sk-"));
        assert!(!format!("{s:?}").contains("sk-from-keystore"));
    }

    fn secrets(pairs: &[(&str, &str)]) -> impl Fn(&str) -> Option<String> {
        let m: HashMap<String, String> = pairs.iter().map(|(k, v)| (k.to_string(), v.to_string())).collect();
        move |name: &str| m.get(name).cloned()
    }

    #[test]
    fn migration_from_the_earlier_openai_compatible_settings() {
        let old = json!({"provider": 0, "base": "http://localhost:11434/v1/", "model": "llama3", "lang": "Français",
            "showCleanSpam": false, "actions": 2, "instructions": "Be brief.", "prompts": "Dates: List the dates.\n# a note",
            "memory": false, "hidden": true, "renderMode": 2, "remoteImages": true});
        let s = Settings::load_from(&old, &secrets(&[(OLD_SECRET, r#"{"key":"sk-old"}"#)]));
        assert_eq!(s.default_provider, Kind::OpenAi);
        assert_eq!((s.openai_base_url.as_str(), s.openai_model.as_str(), s.openai_key.as_str()), ("http://localhost:11434/v1", "llama3", "sk-old"));
        assert_eq!((s.anthropic_base_url.as_str(), s.anthropic_key.as_str()), (DEFAULT_ANTHROPIC_URL, ""));
        assert_eq!(s.actions, Actions::Auto);
        assert_eq!(s.translate_to, "Français");
        assert_eq!(s.instructions, "Be brief.");
        assert_eq!(s.prompts, "Dates: List the dates.\n# a note");
        assert!(!s.memory && s.hidden);
        assert_eq!(ids(&available_providers(&s)), ["openai"]);
        // an older bare key
        assert_eq!(Settings::load_from(&old, &secrets(&[(OLD_SECRET, "sk-bare")])).openai_key, "sk-bare");
    }

    #[test]
    fn migration_from_the_earlier_anthropic_settings() {
        let old = json!({"provider": 1, "base": "https://api.anthropic.com/v1", "model": "claude-opus-5-5", "actions": 0});
        let s = Settings::load_from(&old, &secrets(&[(OLD_SECRET, r#"{"key":" sk-ant-old "}"#)]));
        assert_eq!(s.default_provider, Kind::Anthropic);
        assert_eq!((s.anthropic_model.as_str(), s.anthropic_key.as_str()), ("claude-opus-5-5", "sk-ant-old"));
        assert_eq!(s.openai_base_url, "", "the other slot starts empty");
        assert_eq!(s.actions, Actions::Off);
        assert_eq!(pick_provider(&s, None).unwrap().id, "anthropic:claude-opus-5-5");
    }

    #[test]
    fn migration_leaves_an_untouched_openai_slot_empty() {
        // the earlier settings filled in api.openai.com for everyone; without a key it never worked
        for old in [json!({}), json!({"provider": 0, "base": "https://api.openai.com/v1", "model": "gpt-4o-mini", "renderMode": 1})] {
            let s = Settings::load_from(&old, &secrets(&[]));
            assert_eq!(s.openai_base_url, "");
            assert_eq!(s.openai_model, "");
            assert!(available_providers(&s).is_empty());
            assert_eq!((s.actions, s.translate_to.as_str(), s.memory), (Actions::Ask, "English", true));
        }
        // with a key it did
        let s = Settings::load_from(&json!({}), &secrets(&[(OLD_SECRET, "sk-x")]));
        assert_eq!((s.openai_base_url.as_str(), s.openai_model.as_str()), ("https://api.openai.com/v1", "gpt-4o-mini"));
        // an empty model meant the default one
        let s = Settings::load_from(&json!({"base": "http://h:1/v1", "model": ""}), &secrets(&[]));
        assert_eq!(s.openai_model, "gpt-4o-mini");
    }

    #[test]
    fn the_new_object_wins_over_the_earlier_settings() {
        let file = json!({"provider": 1, "model": "claude-opus-5-5", "assistant": {"version": 2, "defaultProvider": "openai", "openaiBaseUrl": "http://h/v1", "openaiModel": "m"}});
        let s = Settings::load_from(&file, &secrets(&[(OLD_SECRET, "sk-old"), (OPENAI_SECRET, r#"{"key":"sk-new"}"#), (ANTHROPIC_SECRET, r#"{"key":"sk-ant"}"#)]));
        assert_eq!(s.default_provider, Kind::OpenAi);
        assert_eq!((s.openai_key.as_str(), s.anthropic_key.as_str()), ("sk-new", "sk-ant"));
        assert_eq!(s.anthropic_model, DEFAULT_ANTHROPIC_MODEL);
    }

    #[test]
    fn save_and_load_through_the_file_and_the_test_keystore() {
        let _files = crate::settings::TEST_FILES.lock().unwrap_or_else(|e| e.into_inner());
        clean_store();
        let old = json!({"provider": 0, "base": "http://old/v1", "model": "m", "renderMode": 2});
        std::fs::create_dir_all(crate::config_dir()).unwrap();
        std::fs::write(path(), old.to_string()).unwrap();
        let mut s = Settings::load();
        s.openai_key = "sk-stored-openai".into();
        s.anthropic_key = "  sk-stored-ant-saved  ".into();
        s.claude_local = true;
        s.save().unwrap();
        let file: Value = serde_json::from_str(&std::fs::read_to_string(path()).unwrap()).unwrap();
        assert_eq!(file["renderMode"], 2, "the earlier entries are kept");
        assert_eq!(file["assistant"]["claudeLocal"], true);
        assert!(!file.to_string().contains("sk-"), "no key in the file");
        assert_eq!(crate::secrets::get(OPENAI_SECRET).as_deref(), Some(r#"{"key":"sk-stored-openai","origin":"http://old"}"#));
        let back = Settings::load();
        assert_eq!(back.anthropic_key, "sk-stored-ant-saved");
        assert!(back.claude_local);
        // the earlier settings page saving again keeps the new object
        crate::ai::Config::load().save();
        assert!(Settings::load().claude_local);
        // a blank key removes its entry
        s.openai_key = " ".into();
        s.save().unwrap();
        assert_eq!(crate::secrets::get(OPENAI_SECRET), None);
        // a damaged file is not replaced
        std::fs::write(path(), "{not json").unwrap();
        assert!(s.save().is_err());
        assert_eq!(std::fs::read_to_string(path()).unwrap(), "{not json");
        let _ = std::fs::remove_file(path());
        crate::secrets::delete(ANTHROPIC_SECRET);
    }

    #[test]
    fn none_of_the_assistant_settings_sync() {
        for f in FIELDS {
            assert!(!crate::settings_sync::is_synced(&row_key(f)), "{f}");
        }
        // the earlier page's rows too
        for k in ["ai.provider", "ai.base", "ai.key", "ai.model", "ai.actions", "ai.instructions", "ai.prompts", "ai.memory", "ai.lang", "ai.visible"] {
            assert!(!crate::settings_sync::is_synced(k), "{k}");
        }
    }

    #[test]
    fn providers_per_slot() {
        let s = Settings { openai_base_url: "http://localhost:11434/v1/".into(), openai_model: " qwen3 ".into(), ..Settings::default() };
        let list = available_providers(&s);
        assert_eq!(list, [Provider { id: "openai".into(), kind: Kind::OpenAi, label: "OpenAI-compatible", title: None, model: "qwen3".into(), base_url: "http://localhost:11434/v1".into() }]);
        // an address without a model, a model without an address: not usable
        assert!(available_providers(&Settings { openai_base_url: "http://h/v1".into(), ..Settings::default() }).is_empty());
        assert!(available_providers(&Settings { openai_model: "m".into(), ..Settings::default() }).is_empty());
        // Anthropic needs a key, then lists every built-in model
        assert!(available_providers(&with_anthropic(" ")).is_empty());
        let a = available_providers(&with_anthropic("k"));
        assert_eq!(ids(&a), ["anthropic:claude-opus-5-5", "anthropic:claude-sonnet-5-5", "anthropic:claude-haiku-5-5", "anthropic:claude-fable-5-1"]);
        assert_eq!(a[0].title.as_deref(), Some("Anthropic · Opus 5.5"));
        assert_eq!(a[0].base_url, DEFAULT_ANTHROPIC_URL);
        // all three at once, in menu order
        let all = Settings { openai_base_url: "http://h/v1".into(), openai_model: "m".into(), anthropic_key: "k".into(), claude_local: true, ..Settings::default() };
        let list = available_providers(&all);
        assert_eq!(list.len(), 1 + 4 + 5);
        assert_eq!(list[5].id, "claude:claude-fable-5-1");
        assert_eq!(list[9].title.as_deref(), Some("Claude · Claude Code default"));
        assert_eq!(list[9].base_url, "");
    }

    #[test]
    fn visible_models_and_hidden_ones() {
        // a chosen model not in the list goes first
        let s = Settings { anthropic_model: "claude-opus-4-6".into(), ..with_anthropic("k") };
        assert_eq!(available_providers(&s)[0].id, "anthropic:claude-opus-4-6");
        assert_eq!(available_providers(&s)[0].title.as_deref(), Some("Anthropic · Opus 4.6"));
        // hiding takes models out, the default included while another stays
        let s = Settings { hidden_models: vec!["anthropic:claude-haiku-5-5".into(), "anthropic:claude-fable-5-1".into()], ..with_anthropic("k") };
        assert_eq!(ids(&available_providers(&s)), ["anthropic:claude-opus-5-5", "anthropic:claude-sonnet-5-5"]);
        // everything hidden: just the default
        let all: Vec<String> = anthropic::CLAUDE_API_MODELS.iter().map(|(id, _)| hidden_key("anthropic", id)).collect();
        let s = Settings { hidden_models: all, ..with_anthropic("k") };
        assert_eq!(ids(&available_providers(&s)), ["anthropic:claude-haiku-5-5"]);
        // a fetched list replaces the built-in one and names the models
        let s = Settings { anthropic_model_list: vec![ModelInfo { id: "claude-haiku-5-5".into(), name: "Claude Haiku 5.5".into() }, ModelInfo { id: "claude-mythos-5".into(), name: "Claude Mythos 5".into() }], ..with_anthropic("k") };
        let list = available_providers(&s);
        assert_eq!(ids(&list), ["anthropic:claude-haiku-5-5", "anthropic:claude-mythos-5"]);
        assert_eq!(list[1].title.as_deref(), Some("Anthropic · Claude Mythos 5"));
        // an empty chosen model is the default
        let s = Settings { claude_local: true, claude_model: " ".into(), hidden_models: CLAUDE_MODELS.iter().map(|(id, _)| hidden_key("claude", id)).collect(), ..Settings::default() };
        assert_eq!(ids(&available_providers(&s)), ["claude:claude-sonnet-5-5"]);
    }

    #[test]
    fn the_pick() {
        let all = Settings { openai_base_url: "http://h/v1".into(), openai_model: "m".into(), anthropic_key: "k".into(), claude_local: true, ..Settings::default() };
        let pick = |s: &Settings, p: Option<&str>| pick_provider(s, p).map(|p| p.id);
        // the menu id itself
        assert_eq!(pick(&all, Some("claude:claude-opus-5-5")).as_deref(), Some("claude:claude-opus-5-5"));
        // nothing preferred: the default service (OpenAI-compatible)
        assert_eq!(pick(&all, None).as_deref(), Some("openai"));
        assert_eq!(pick(&all, Some("")).as_deref(), Some("openai"));
        // default service Anthropic: its default model, not the first of the list
        let a = Settings { default_provider: Kind::Anthropic, ..all.clone() };
        assert_eq!(pick(&a, None).as_deref(), Some("anthropic:claude-haiku-5-5"));
        // the one old-value rule: a bare "anthropic" maps to the default Anthropic model
        assert_eq!(pick(&a, Some("anthropic")).as_deref(), Some("anthropic:claude-haiku-5-5"));
        // ... which is the default service's model: with another default service it is the first
        assert_eq!(pick(&all, Some("anthropic")).as_deref(), Some("anthropic:claude-opus-5-5"));
        // a hidden or unknown model of a kind: that kind's default, else its first
        assert_eq!(pick(&a, Some("anthropic:claude-gone")).as_deref(), Some("anthropic:claude-haiku-5-5"));
        assert_eq!(pick(&all, Some("claude:old-model")).as_deref(), Some("claude:claude-fable-5-1"));
        let c = Settings { default_provider: Kind::Claude, ..all.clone() };
        assert_eq!(pick(&c, Some("claude:old-model")).as_deref(), Some("claude:claude-sonnet-5-5"));
        // a preferred kind that is not usable: the default service
        let no_claude = Settings { claude_local: false, ..a.clone() };
        assert_eq!(pick(&no_claude, Some("claude:claude-opus-5-5")).as_deref(), Some("anthropic:claude-haiku-5-5"));
        // the default service not usable: the first entry
        let only_claude = Settings { default_provider: Kind::Anthropic, claude_local: true, ..Settings::default() };
        assert_eq!(pick(&only_claude, None).as_deref(), Some("claude:claude-fable-5-1"));
        // default model hidden: another model of the default service
        let hidden = Settings { hidden_models: vec!["anthropic:claude-haiku-5-5".into()], ..a.clone() };
        assert_eq!(pick(&hidden, None).as_deref(), Some("anthropic:claude-opus-5-5"));
    }

    #[test]
    fn saved_prompts_rules() {
        let s = Settings { prompts: "Key dates: List every date.\r\n: no name\nno colon\nName:\n  Sum up : Summarise: briefly \n# Note: kept".into(), ..Settings::default() };
        assert_eq!(
            saved_prompts(&s),
            [("Key dates".into(), "List every date.".into()), ("Sum up".into(), "Summarise: briefly".into()), ("# Note".into(), "kept".into())]
        );
    }

    #[test]
    fn claude_model_names() {
        assert_eq!(claude_model_name("default"), "Claude Code default");
        assert_eq!(claude_model_name("opus"), "opus");
    }

    #[test]
    fn connection_test_texts() {
        let s = Settings::default();
        assert_eq!(test_connection(&s, Kind::OpenAi, &Cancel::new()), ("Fill in the address and the model first.".into(), false));
        assert_eq!(test_connection(&s, Kind::Anthropic, &Cancel::new()), ("Fill in the address and the key first.".into(), false));
        let answer = "data: {\"choices\":[{\"delta\":{\"content\":\"  OK, this connection works fine and the answer is long.  \"}}]}\n\ndata: [DONE]\n\n";
        let server = FakeServer::start(vec![Reply::stream(200, vec![answer.as_bytes().to_vec()]), Reply::stream(200, vec![b"data: [DONE]\n\n".to_vec()]), Reply::json(401, r#"{"error":{"message":"bad key"}}"#)]);
        let s = Settings { openai_base_url: format!("{}/v1", server.base), openai_model: "m1".into(), ..Settings::default() };
        assert_eq!(test_connection(&s, Kind::OpenAi, &Cancel::new()), ("Works (m1 said: OK, this connection works fine and the a).".into(), true));
        let body: Value = serde_json::from_str(&server.requests()[0].body).unwrap();
        assert_eq!(body["messages"], json!([{"role": "system", "content": TEST_SYSTEM}, {"role": "user", "content": TEST_PROMPT}]));
        assert!(body.get("tools").is_none());
        assert_eq!(test_connection(&s, Kind::OpenAi, &Cancel::new()), ("Works (m1 said: nothing).".into(), true));
        assert_eq!(test_connection(&s, Kind::OpenAi, &Cancel::new()), ("401: bad key".into(), false));
    }

    #[test]
    fn the_anthropic_test_uses_the_effort_and_the_picked_model() {
        let ok = "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}\n\ndata: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"OK\"}}\n\n";
        let server = FakeServer::start(vec![Reply::stream(200, vec![ok.as_bytes().to_vec()]), Reply::stream(200, vec![ok.as_bytes().to_vec()])]);
        // the default service is OpenAI-compatible: pickProvider(s, "anthropic") has no default
        // model to match (rule 2) and takes the first Anthropic entry (rule 3), not the chosen one
        let s = Settings { anthropic_base_url: format!("{}/v1", server.base), anthropic_model: "claude-sonnet-5-5".into(), effort: "low".into(), ..with_anthropic("sk-ant-test") };
        assert_eq!(available_providers(&s)[0].model, "claude-opus-5-5");
        assert_eq!(test_connection(&s, Kind::Anthropic, &Cancel::new()), ("Works (claude-opus-5-5 said: OK).".into(), true));
        let req = &server.requests()[0];
        assert_eq!(req.header("x-api-key").as_deref(), Some("sk-ant-test"));
        let body: Value = serde_json::from_str(&req.body).unwrap();
        assert_eq!(body["output_config"], json!({"effort": "low"}));
        assert_eq!(body["model"], "claude-opus-5-5");
        // with Anthropic as the default service, rule 2 takes its chosen model
        let s = Settings { default_provider: Kind::Anthropic, ..s };
        assert_eq!(test_connection(&s, Kind::Anthropic, &Cancel::new()), ("Works (claude-sonnet-5-5 said: OK).".into(), true));
        let body: Value = serde_json::from_str(&server.requests()[1].body).unwrap();
        assert_eq!(body["model"], "claude-sonnet-5-5");
    }

    #[test]
    fn a_stored_key_goes_only_to_the_origin_it_was_saved_for() {
        let _files = crate::settings::TEST_FILES.lock().unwrap_or_else(|e| e.into_inner());
        clean_store();
        let home = FakeServer::start(vec![answer()]);
        let elsewhere = FakeServer::start(vec![answer()]);
        let mut s = Settings { openai_base_url: format!("{}/v1", home.base), openai_model: "m".into(), openai_key: "sk-stored-bound".into(), ..Settings::default() };
        s.save().unwrap();
        let stored: Value = serde_json::from_str(&crate::secrets::get(OPENAI_SECRET).unwrap()).unwrap();
        assert_eq!(stored["origin"], home.base.as_str());
        assert!(test_connection(&Settings::load(), Kind::OpenAi, &Cancel::new()).1);
        // an imported settings file points the slot elsewhere: the stored key is not sent
        let mut file = read_file().unwrap();
        file["assistant"]["openaiBaseUrl"] = json!(format!("{}/v1", elsewhere.base));
        write_file(&file).unwrap();
        let mut moved = Settings::load();
        let (text, ok) = test_connection(&moved, Kind::OpenAi, &Cancel::new());
        assert!(!ok);
        assert_eq!(
            text,
            refusal("OpenAI-compatible", &home.base, &elsewhere.base)
        );
        assert!(elsewhere.requests().is_empty());
        // saving without typing the key again keeps it bound to the old origin
        moved.save().unwrap();
        assert!(test_connection(&Settings::load(), Kind::OpenAi, &Cancel::new()).0.starts_with("The OpenAI-compatible API key was saved for"));
        assert!(elsewhere.requests().is_empty());
        // typing it again binds it to the new address
        let mut retyped = Settings::load();
        retyped.openai_key = "sk-stored-retyped2".into();
        assert!(key_check(&retyped, Kind::OpenAi, &retyped.openai_base_url).is_ok(), "a typed key is not bound yet");
        retyped.save().unwrap();
        assert_eq!(test_connection(&Settings::load(), Kind::OpenAi, &Cancel::new()), ("Works (m said: OK).".into(), true));
        assert_eq!(elsewhere.requests()[0].header("authorization").as_deref(), Some("Bearer sk-stored-retyped2"));
        // back on the first address it is refused; another path on the same origin is fine
        let back = Settings::load();
        assert!(key_check(&back, Kind::OpenAi, &format!("{}/v1", home.base)).is_err());
        assert!(key_check(&back, Kind::OpenAi, &format!("{}/other/v1", elsewhere.base)).is_ok());
        assert_eq!(home.requests().len(), 1);
        let _ = std::fs::remove_file(path());
        crate::secrets::delete(OPENAI_SECRET);
    }

    // Keys a test stores start with `sk-stored-`, and every test that stores one or reaches
    // `key_check` with one holds `TEST_FILES`: the test keystore is shared, and a stored key is
    // refused for any other origin. Tests without the lock use other keys.

    /// Under `TEST_FILES`: no settings file and none of the three key entries.
    fn clean_store() {
        let _ = std::fs::remove_file(path());
        for name in [OLD_SECRET, OPENAI_SECRET, ANTHROPIC_SECRET] {
            crate::secrets::delete(name);
        }
    }

    fn origin_in_store(name: &str) -> Option<String> {
        read_entry(name).and_then(|(_, o)| o)
    }

    fn refusal(label: &str, from: &str, to: &str) -> String {
        format!("The {label} API key was saved for {from}, but the address is now {to}. If you did not change it (an import can), set it back to {from}. To use {to}, clear the key and enter it again.")
    }

    fn answer() -> Reply {
        Reply::stream(200, vec![b"data: {\"choices\":[{\"delta\":{\"content\":\"OK\"}}]}\n\ndata: [DONE]\n\n".to_vec()])
    }

    #[test]
    fn a_crafted_import_before_the_first_save_cannot_move_the_earlier_key() {
        let _files = crate::settings::TEST_FILES.lock().unwrap_or_else(|e| e.into_inner());
        clean_store();
        let home = FakeServer::start(vec![answer()]);
        let elsewhere = FakeServer::start(vec![answer(), answer()]);
        std::fs::create_dir_all(crate::config_dir()).unwrap();
        std::fs::write(path(), json!({"provider": 0, "base": format!("{}/v1", home.base), "model": "m"}).to_string()).unwrap();
        // an earlier build wrote the key without its origin
        assert!(crate::secrets::set(OLD_SECRET, r#"{"key":"sk-stored-old"}"#));
        // startup binds it to the address it is used with now
        let _ = crate::ai::Config::load();
        assert_eq!(origin_in_store(OLD_SECRET).as_deref(), Some(home.base.as_str()));
        // an imported earlier-style file points it elsewhere; the load after the import keeps the origin
        std::fs::write(path(), json!({"provider": 0, "base": format!("{}/v1", elsewhere.base), "model": "m"}).to_string()).unwrap();
        let _ = crate::ai::Config::load();
        assert_eq!(origin_in_store(OLD_SECRET).as_deref(), Some(home.base.as_str()));
        // the migration takes the key, never an origin from the file
        let mut moved = Settings::load();
        assert_eq!(moved.openai_key, "sk-stored-old");
        assert_eq!(test_connection(&moved, Kind::OpenAi, &Cancel::new()), (refusal("OpenAI-compatible", &home.base, &elsewhere.base), false));
        assert_eq!(
            check_old_key("sk-stored-old", &format!("{}/v1", elsewhere.base), Kind::OpenAi),
            Err(refusal("OpenAI-compatible", &home.base, &elsewhere.base))
        );
        // the earlier settings page saving it keeps the origin too
        crate::ai::Config::load().save();
        assert_eq!(origin_in_store(OLD_SECRET).as_deref(), Some(home.base.as_str()));
        // switching the provider in the import moves it to the Anthropic slot, refused the same way
        std::fs::write(path(), json!({"provider": 1, "base": format!("{}/v1", elsewhere.base)}).to_string()).unwrap();
        let s = Settings::load();
        assert_eq!(s.anthropic_key, "sk-stored-old");
        assert_eq!(key_check(&s, Kind::Anthropic, &s.anthropic_base_url).unwrap_err().message, refusal("Anthropic", &home.base, &elsewhere.base));
        // the first save of the new settings carries the stored origin into the new slot
        std::fs::write(path(), json!({"provider": 0, "base": format!("{}/v1", elsewhere.base), "model": "m"}).to_string()).unwrap();
        moved.save().unwrap();
        assert_eq!(origin_in_store(OPENAI_SECRET).as_deref(), Some(home.base.as_str()));
        assert!(!test_connection(&Settings::load(), Kind::OpenAi, &Cancel::new()).1);
        assert!(elsewhere.requests().is_empty());
        // on its own address it still works
        let back = Settings { openai_base_url: format!("{}/v1", home.base), ..Settings::load() };
        assert_eq!(test_connection(&back, Kind::OpenAi, &Cancel::new()), ("Works (m said: OK).".into(), true));
        clean_store();
    }

    #[test]
    fn a_crafted_import_after_the_first_save_cannot_move_a_key() {
        let _files = crate::settings::TEST_FILES.lock().unwrap_or_else(|e| e.into_inner());
        clean_store();
        let (home, elsewhere) = ("https://home.example.test", "https://elsewhere.example.test");
        let mut s = Settings { openai_base_url: format!("{home}/v1"), openai_key: "sk-stored-first".into(), ..Settings::default() };
        s.save().unwrap();
        assert_eq!(origin_in_store(OPENAI_SECRET).as_deref(), Some(home));
        // an import with an "assistant" object pointing the slot elsewhere
        let mut file = read_file().unwrap();
        file["assistant"]["openaiBaseUrl"] = json!(format!("{elsewhere}/v1"));
        write_file(&file).unwrap();
        assert_eq!(
            key_check(&Settings::load(), Kind::OpenAi, &format!("{elsewhere}/v1")).unwrap_err().message,
            refusal("OpenAI-compatible", home, elsewhere)
        );
        // an import without one: the migration of the earlier key, bound by the earlier page
        crate::secrets::delete(OPENAI_SECRET);
        assert!(save_old_key("sk-stored-earlier", &format!("{home}/v1")));
        write_file(&json!({"provider": 0, "base": format!("{elsewhere}/v1")})).unwrap();
        let s = Settings::load();
        assert_eq!(s.openai_key, "sk-stored-earlier");
        assert_eq!(key_check(&s, Kind::OpenAi, &s.openai_base_url).unwrap_err().message, refusal("OpenAI-compatible", home, elsewhere));
        clean_store();
    }

    #[test]
    fn a_new_slot_entry_without_an_origin_is_bound_on_load() {
        let _files = crate::settings::TEST_FILES.lock().unwrap_or_else(|e| e.into_inner());
        clean_store();
        let gateway = "https://gateway.example.test";
        write_file(&json!({"assistant": {"anthropicBaseUrl": format!("{gateway}/v1"), "openaiBaseUrl": "http://localhost:11434/v1"}})).unwrap();
        assert!(crate::secrets::set(ANTHROPIC_SECRET, r#"{"key":"sk-stored-ant"}"#));
        assert!(crate::secrets::set(OPENAI_SECRET, "sk-stored-bare"));
        let s = Settings::load();
        assert_eq!((s.anthropic_key.as_str(), s.openai_key.as_str()), ("sk-stored-ant", "sk-stored-bare"));
        assert_eq!(origin_in_store(ANTHROPIC_SECRET).as_deref(), Some(gateway));
        assert_eq!(origin_in_store(OPENAI_SECRET).as_deref(), Some("http://localhost:11434"));
        assert!(key_check(&s, Kind::Anthropic, &format!("{gateway}/v2")).is_ok(), "another path on the same origin");
        // an import then points both elsewhere
        write_file(&json!({"assistant": {"openaiBaseUrl": "https://api.openai.com/v1"}})).unwrap();
        let mut s = Settings::load();
        assert_eq!(origin_in_store(ANTHROPIC_SECRET).as_deref(), Some(gateway), "a load never rebinds");
        assert!(key_check(&s, Kind::OpenAi, &s.openai_base_url).is_err());
        assert!(key_check(&s, Kind::Anthropic, &s.anthropic_base_url).is_err());
        // the refresh of the model list checks it too, before anything is sent
        let (text, ok) = refresh_models(&mut s, &Cancel::new());
        assert!(!ok && text == refusal("Anthropic", gateway, "https://api.anthropic.com"), "{text}");
        clean_store();
    }

    #[test]
    fn an_address_the_user_edits_takes_the_key_with_it() {
        let _files = crate::settings::TEST_FILES.lock().unwrap_or_else(|e| e.into_inner());
        clean_store();
        let (home, moved, imported) = ("https://home.example.test", "https://moved.example.test", "https://imported.example.test");
        // the earlier settings page: the user edits `ai.base` and leaves the field; the same key
        // keeps working there
        let mut cfg = crate::ai::Config { base: format!("{home}/v1"), key: "sk-stored-dummy".into(), ..crate::ai::Config::default() };
        cfg.save();
        let mut page = crate::settings::Settings::load();
        for typed in ["https://moved", "https://moved.example.test", format!("{moved}/v1").as_str()] {
            page.set("ai.base", json!(typed), &mut cfg);
            assert_eq!(origin_in_store(OLD_SECRET).as_deref(), Some(home), "a keystroke moves nothing");
        }
        cfg.commit_base();
        assert_eq!(origin_in_store(OLD_SECRET).as_deref(), Some(moved));
        assert_eq!(check_old_key("sk-stored-dummy", &format!("{moved}/v1"), Kind::OpenAi), Ok(()));
        let s = Settings::load();
        assert_eq!(s.openai_base_url, format!("{moved}/v1"));
        assert!(key_check(&s, Kind::OpenAi, &s.openai_base_url).is_ok());
        // switching the service is an edit too: the key moves to that service's address
        crate::settings::Settings::load().set("ai.provider", json!(1), &mut cfg);
        assert_eq!(origin_in_store(OLD_SECRET).as_deref(), Some("https://api.anthropic.com"));
        crate::settings::Settings::load().set("ai.provider", json!(0), &mut cfg);
        crate::settings::Settings::load().set("ai.base", json!(format!("{moved}/v1")), &mut cfg);
        cfg.commit_base();
        assert_eq!(origin_in_store(OLD_SECRET).as_deref(), Some(moved));
        // an import that changes the address is still refused
        let mut file = read_file().unwrap();
        file["base"] = json!(format!("{imported}/v1"));
        write_file(&file).unwrap();
        let mut after = crate::ai::Config::load();
        assert_eq!(check_old_key(&after.key, &after.base, Kind::OpenAi), Err(refusal("OpenAI-compatible", moved, imported)));
        let s = Settings::load();
        assert_eq!(key_check(&s, Kind::OpenAi, &s.openai_base_url).unwrap_err().message, refusal("OpenAI-compatible", moved, imported));
        // entering and leaving the imported address without changing it moves nothing
        after.commit_base();
        assert_eq!(origin_in_store(OLD_SECRET).as_deref(), Some(moved));
        // nor does a keystroke there that is never committed, nor an address with no origin
        crate::settings::Settings::load().set("ai.base", json!(format!("{imported}/v1/")), &mut after);
        assert_eq!(origin_in_store(OLD_SECRET).as_deref(), Some(moved));
        let mut bad = after.clone();
        crate::settings::Settings::load().set("ai.base", json!("not an address"), &mut bad);
        bad.commit_base();
        assert_eq!(origin_in_store(OLD_SECRET).as_deref(), Some(moved));
        rebind(OPENAI_SECRET, "sk-stored-dummy", "");
        assert_eq!(origin_in_store(OLD_SECRET).as_deref(), Some(moved));
        // the new settings' setters do the same for their slots
        clean_store();
        let mut s = Settings { openai_base_url: format!("{home}/v1"), openai_key: "sk-stored-dummy".into(), ..with_anthropic("sk-stored-dummy-ant") };
        s.save().unwrap();
        s.set_openai_base_url(&format!(" {moved}/v1 "));
        s.set_anthropic_base_url(&format!("{moved}/anthropic/v1"));
        assert_eq!((origin_in_store(OPENAI_SECRET).as_deref(), origin_in_store(ANTHROPIC_SECRET).as_deref()), (Some(moved), Some(moved)));
        s.save().unwrap();
        assert_eq!(origin_in_store(OPENAI_SECRET).as_deref(), Some(moved), "saving keeps the edit");
        assert!(key_check(&Settings::load(), Kind::Anthropic, &format!("{moved}/anthropic/v1")).is_ok());
        let mut file = read_file().unwrap();
        file["assistant"]["openaiBaseUrl"] = json!(format!("{imported}/v1"));
        write_file(&file).unwrap();
        assert!(key_check(&Settings::load(), Kind::OpenAi, &format!("{imported}/v1")).is_err());
        clean_store();
    }

    #[test]
    fn unbound_keys_wait_for_a_readable_file_and_an_address() {
        let _files = crate::settings::TEST_FILES.lock().unwrap_or_else(|e| e.into_inner());
        clean_store();
        std::fs::create_dir_all(crate::config_dir()).unwrap();
        assert!(crate::secrets::set(OLD_SECRET, r#"{"key":"sk-stored-wait"}"#));
        assert!(crate::secrets::set(OPENAI_SECRET, r#"{"key":"sk-stored-wait-new"}"#));
        // a file that does not parse binds nothing
        std::fs::write(path(), "{\"base\": \"https://x.example.test/v1\", oops").unwrap();
        bind_unbound_keys();
        assert_eq!((read_entry(OLD_SECRET), read_entry(OPENAI_SECRET).map(|e| e.1)), (Some(("sk-stored-wait".into(), None)), Some(None)));
        // a readable one binds the earlier key; the new slot has no address yet
        write_file(&json!({"base": "https://x.example.test/v1", "assistant": {"openaiBaseUrl": " "}})).unwrap();
        bind_unbound_keys();
        assert_eq!(origin_in_store(OLD_SECRET).as_deref(), Some("https://x.example.test"));
        assert_eq!(read_entry(OPENAI_SECRET).map(|e| e.1), Some(None));
        // the next load with one binds it
        write_file(&json!({"assistant": {"openaiBaseUrl": "http://localhost:11434/v1"}})).unwrap();
        let _ = Settings::load();
        assert_eq!(origin_in_store(OPENAI_SECRET).as_deref(), Some("http://localhost:11434"));
        clean_store();
    }

    #[test]
    fn the_keystore_decides_for_settings_built_anywhere() {
        let _files = crate::settings::TEST_FILES.lock().unwrap_or_else(|e| e.into_inner());
        clean_store();
        let (home, imported) = ("https://home.example.test", "https://imported.example.test");
        assert!(save_key(OPENAI_SECRET, "sk-stored-truth", &format!("{home}/v1")));
        // built from a value or the defaults, with the stored key and an imported address
        let mut s = Settings::from_value(&json!({"openaiBaseUrl": format!("{imported}/v1")}), "sk-stored-truth", "");
        assert_eq!(key_check(&s, Kind::OpenAi, &s.openai_base_url).unwrap_err().message, refusal("OpenAI-compatible", home, imported));
        let d = Settings { openai_base_url: format!("{imported}/v1"), openai_key: " sk-stored-truth ".into(), ..Settings::default() };
        assert!(key_check(&d, Kind::OpenAi, &d.openai_base_url).is_err());
        // saving it keeps the old origin
        s.save().unwrap();
        assert_eq!(origin_in_store(OPENAI_SECRET).as_deref(), Some(home));
        assert!(key_check(&s, Kind::OpenAi, &s.openai_base_url).is_err());
        // an entry with no origin at all refuses every address
        assert!(crate::secrets::set(ANTHROPIC_SECRET, r#"{"key":"sk-stored-none","origin":""}"#));
        let a = with_anthropic("sk-stored-none");
        assert_eq!(
            key_check(&a, Kind::Anthropic, DEFAULT_ANTHROPIC_URL).unwrap_err().message,
            "The Anthropic API key was saved for another address, but the address is now https://api.anthropic.com. To use https://api.anthropic.com, clear the key and enter it again."
        );
        // only a key that differs from the stored one binds to the current address
        s.openai_key = "sk-stored-retyped".into();
        assert!(key_check(&s, Kind::OpenAi, &s.openai_base_url).is_ok(), "a typed key is not stored yet");
        s.save().unwrap();
        assert_eq!(origin_in_store(OPENAI_SECRET).as_deref(), Some(imported));
        assert!(key_check(&s, Kind::OpenAi, &format!("{home}/v1")).is_err());
        // no key, nothing to refuse
        assert!(key_check(&Settings::default(), Kind::OpenAi, "http://any/v1").is_ok());
        clean_store();
    }

    #[test]
    fn the_model_list_keeps_only_claude_ids() {
        let mut s = with_anthropic("");
        assert_eq!(refresh_models(&mut s, &Cancel::new()), ("Fill in the address and the key first.".into(), false));
        let page = |data: Value| Reply::json(200, &json!({"data": data, "has_more": false}).to_string());
        let server = FakeServer::start(vec![
            page(json!([{"id": "claude-opus-5-5", "display_name": "Claude Opus 5.5"}, {"id": "embed-1"}, {"id": "claude-haiku-5-5"}])),
            page(json!([{"id": "not-claude"}])),
        ]);
        let mut s = Settings { anthropic_base_url: format!("{}/v1/", server.base), ..with_anthropic("k") };
        assert_eq!(refresh_models(&mut s, &Cancel::new()), ("2 models.".into(), true));
        assert_eq!(s.anthropic_model_list, [ModelInfo { id: "claude-opus-5-5".into(), name: "Claude Opus 5.5".into() }, ModelInfo { id: "claude-haiku-5-5".into(), name: "Haiku 5.5".into() }]);
        let before = s.anthropic_model_list.clone();
        assert_eq!(refresh_models(&mut s, &Cancel::new()), ("The list came back empty.".into(), false));
        assert_eq!(s.anthropic_model_list, before, "an empty list keeps the one before");
    }

    #[test]
    fn claude_is_not_wired_yet() {
        let s = Settings { claude_local: true, ..Settings::default() };
        let p = pick_provider(&s, None).unwrap();
        assert!(complete(&s, &p, "s", "p", &Cancel::new(), &mut |_| {}).is_err());
    }
}
