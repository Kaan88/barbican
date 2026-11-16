//! Bulwark-parity settings: the page/section/row schema extracted from Bulwark's settings
//! components (ui/settings-schema.json, see tools/extract_settings.py), the user's values
//! (prefs.json, same keys as Bulwark's settings store) and the desktop add-on pages.
use crate::ai::Config;
use crate::{SRow, STab};
use serde_json::{json, Map, Value};
use slint::{ModelRc, SharedString, VecModel};

const SCHEMA: &str = include_str!("../ui/settings-schema.json");

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
    crate::config_dir().join("prefs.json")
}

/// Keyless controls in the extracted schema that map to a known setting.
fn key_for_label(label: &str) -> Option<&'static str> {
    match label {
        "Light or dark" => Some("theme"),
        "Check spelling and grammar while I type" => Some("proofreading"),
        _ => None,
    }
}

/// Options the extractor could not resolve (built from constants in Bulwark's code).
fn option_override(key: &str) -> Option<Vec<(Value, &'static str)>> {
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
        _ => return None,
    })
}

/// Bulwark ALL_HOVER_ACTIONS that Barbican's rows can run (the tag picker needs a menu).
pub const HOVER_ACTIONS: [(&str, &str); 5] = [("delete", "Delete"), ("star", "Star"), ("markRead", "Mark as read"), ("archive", "Archive"), ("spam", "Spam")];

fn s(v: &str) -> SharedString {
    SharedString::from(v)
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

fn text(kind: &str, key: &str, label: &str, desc: &str, value: &str) -> SRow {
    let mut r = row(kind, key, label, desc);
    r.text = s(value);
    r
}

impl Settings {
    pub fn load() -> Settings {
        let prefs = std::fs::read_to_string(path()).ok()
            .and_then(|t| serde_json::from_str::<Value>(&t).ok())
            .and_then(|v| v.as_object().cloned())
            .unwrap_or_default();
        Settings { schema: schema(), prefs }
    }

    /// Settings over the given values (no file), for tests and previews.
    #[allow(dead_code)]
    pub fn with_prefs(prefs: Map<String, Value>) -> Settings {
        Settings { schema: schema(), prefs }
    }

    fn save(&self) {
        let _ = std::fs::create_dir_all(crate::config_dir());
        let _ = std::fs::write(path(), Value::Object(self.prefs.clone()).to_string());
    }

    /// Everything worth backing up: Bulwark-keyed prefs plus the desktop/assistant settings file.
    pub fn export(&self) -> Value {
        let desktop: Value = std::fs::read_to_string(crate::config_dir().join("settings.json")).ok()
            .and_then(|t| serde_json::from_str(&t).ok()).unwrap_or(json!({}));
        json!({"app": "barbican", "version": 1, "settings": Value::Object(self.prefs.clone()), "desktop": desktop})
    }

    /// Accepts Barbican exports and Bulwark's own settings export (a flat settings object).
    pub fn import(&mut self, v: &Value) -> Result<(), String> {
        let prefs = if v["app"] == "barbican" { v["settings"].clone() } else if v["settings"].is_object() { v["settings"].clone() } else { v.clone() };
        let Some(obj) = prefs.as_object() else { return Err("This file has no settings.".into()) };
        self.prefs = obj.clone();
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

    /// Store one value (None removes it) and save, outside the settings page's own controls.
    pub fn put(key: &str, value: Option<Value>) {
        let mut s = Settings::load();
        match value { Some(v) => { s.prefs.insert(key.into(), v); } None => { s.prefs.remove(key); } }
        s.save();
    }

    pub fn bool(&self, key: &str, default: bool) -> bool {
        self.prefs.get(key).and_then(|v| v.as_bool()).unwrap_or(default)
    }

    pub fn str(&self, key: &str, default: &str) -> String {
        self.prefs.get(key).and_then(|v| v.as_str()).unwrap_or(default).to_string()
    }

    /// Numeric pref; tolerates numbers stored as strings.
    pub fn int(&self, key: &str, default: i64) -> i64 {
        match self.prefs.get(key) {
            Some(Value::Number(n)) => n.as_i64().or_else(|| n.as_f64().map(|f| f as i64)).unwrap_or(default),
            Some(Value::String(s)) => s.trim().parse().unwrap_or(default),
            _ => default,
        }
    }

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
        ModelRc::new(VecModel::from(v))
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
                "toggle" if !key.is_empty() => {
                    let invert = r["invert"].as_bool() == Some(true);
                    let dflt = r["default"].as_bool().unwrap_or(false);
                    let v = self.bool(key, dflt);
                    out.push(toggle(key, label, desc, if invert { !v } else { v }));
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
                    out.push(select(key, label, desc, &labels, idx as i32));
                }
                "text" if !key.is_empty() => out.push(text("text", key, label, desc, &self.str(key, r["default"].as_str().unwrap_or("")))),
                "button" if !key.is_empty() => out.push(text("button", key, label, desc, r["text"].as_str().unwrap_or("Reset"))),
                // Contacts > Import / Export (Bulwark contact-import-dialog, contact-export)
                "info" if label == "Import contacts" => out.push(text("button", "contacts.import", label, "Adds the contacts in a vCard (.vcf) file. Contacts whose email address you already have can be skipped.", "Import…")),
                "info" if label == "Export contacts" => out.push(text("button", "contacts.export", label, "Saves every contact as a vCard 4.0 file, photos included.", "Export…")),
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
                out.push(row("section", "", "Plugins", "Native plugins dropped into the plugins folder are loaded when Barbican starts."));
                let disabled: Vec<String> = self.prefs.get("disabledPlugins").and_then(|v| v.as_array())
                    .map(|a| a.iter().filter_map(|x| x.as_str().map(String::from)).collect()).unwrap_or_default();
                let list = crate::plugin_list();
                if list.is_empty() {
                    out.push(text("value", "", "No plugins installed", "Copy a plugin .dll into the folder and restart Barbican.", ""));
                }
                for p in list {
                    let id = p["id"].as_str().unwrap_or("");
                    let hooks = p["hooks"].as_array().map(|a| a.iter().filter_map(|h| h.as_str()).collect::<Vec<_>>().join(", ")).unwrap_or_default();
                    out.push(toggle(&format!("plugin:{id}"), &format!("{} {}", p["name"].as_str().unwrap_or(id), p["version"].as_str().unwrap_or("")),
                        &format!("{}  (hooks: {hooks})", p["description"].as_str().unwrap_or("")), !disabled.iter().any(|d| d == id)));
                }
                for e in crate::plugin_errors() {
                    out.push(text("value", "", "Could not load", &e, ""));
                }
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
                    out.insert(1, select("desktop.remoteImages", "Load remote images", "", &["Ask first (blocked until you choose)", "Always load"], if cfg.remote_images { 1 } else { 0 }));
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
            "desktop.remoteImages" => cfg.remote_images = value.as_i64() == Some(1),
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
            "a11y.reset" => {
                for k in crate::a11y::reset_keys() { self.prefs.remove(k); }
                self.save();
                return;
            }
            k if k.starts_with("plugin:") => {
                let id = &k[7..];
                let mut list: Vec<Value> = self.prefs.get("disabledPlugins").and_then(|v| v.as_array().cloned()).unwrap_or_default();
                list.retain(|x| x.as_str() != Some(id));
                if value.as_bool() == Some(false) { list.push(json!(id)); }
                self.prefs.insert("disabledPlugins".into(), Value::Array(list));
                self.save();
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
    use super::*;

    // One test so the APPDATA override can't race other tests touching config_dir().
    #[test]
    fn import_export_reset_roundtrip() {
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

        s.reset();
        assert!(s.prefs.is_empty());
        assert!(!crate::config_dir().join("settings.json").exists());
        assert!(Settings::load().prefs.is_empty());
        let _ = std::fs::remove_dir_all(&dir);
    }
}

/// Bulwark's timeFormat pref ("12h" | "24h"), read straight from prefs.json so hot paths
/// (admin tables, dashboard tooltips) need not parse the schema.
pub fn h12() -> bool {
    std::fs::read_to_string(path()).ok()
        .and_then(|t| serde_json::from_str::<Value>(&t).ok())
        .map(|v| v["timeFormat"].as_str() == Some("12h"))
        .unwrap_or(false)
}

/// Write one Bulwark-keyed pref (a value with no settings row, such as sharedCalendarColors).
pub fn set_pref(key: &str, value: Value) {
    let mut s = Settings::load();
    s.prefs.insert(key.into(), value);
    s.save();
}
