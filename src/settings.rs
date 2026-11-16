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
    schema: Value,
    pub prefs: Map<String, Value>,
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
        "mvUnreadSectionSize" => vec![(json!(3), "3"), (json!(5), "5"), (json!(10), "10"), (json!(20), "20")],
        "composePlacement" => vec![(json!("sidebar"), "In the sidebar"), (json!("rail"), "On the navigation rail"), (json!("floating"), "Floating button")],
        "theme" => vec![(json!("light"), "Light"), (json!("dark"), "Dark"), (json!("system"), "System")],
        "fontSize" => vec![(json!("small"), "Small"), (json!("medium"), "Medium"), (json!("large"), "Large"), (json!("x-large"), "Extra large")],
        "calendarDayStartHour" => (0..13).map(|h| (json!(h), ["0:00","1:00","2:00","3:00","4:00","5:00","6:00","7:00","8:00","9:00","10:00","11:00","12:00"][h as usize])).collect(),
        "writingLanguage" => vec![(json!("auto"), "Detect automatically"), (json!("en"), "English"), (json!("de"), "German"), (json!("fr"), "French"), (json!("es"), "Spanish"), (json!("tr"), "Turkish")],
        _ => return None,
    })
}

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
        Settings { schema: serde_json::from_str(SCHEMA).expect("settings schema"), prefs }
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

    pub fn bool(&self, key: &str, default: bool) -> bool {
        self.prefs.get(key).and_then(|v| v.as_bool()).unwrap_or(default)
    }

    pub fn str(&self, key: &str, default: &str) -> String {
        self.prefs.get(key).and_then(|v| v.as_str()).unwrap_or(default).to_string()
    }

    fn tab_list(&self) -> Vec<(String, String, String, String, i32)> {
        // id, label, group, icon, indent
        let mut out = vec![];
        for t in self.schema["tabs"].as_array().unwrap() {
            let id = t["id"].as_str().unwrap_or("");
            out.push((id.into(), t["label"].as_str().unwrap_or("").into(), t["group"].as_str().unwrap_or("").into(), t["icon"].as_str().unwrap_or("").into(), 0));
            if id == "backup" {
                out.push(("admin".into(), "Stalwart admin".into(), "bottom".into(), "\u{E83D}".into(), 0));
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
                out.push(text("password", "ai.key", "API key", "Kept in Windows Credential Manager. Ollama needs any non-empty key.", &cfg.key));
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
                out.push(text("password", "admin.key", "Admin API key", "Kept in Windows Credential Manager. Leave empty to hide the admin tab.", &crate::admin::key()));
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
                if tab == "composing" {
                    out.push(row("section", "", "Sending", ""));
                    out.push(toggle("sendConfirmation", "Confirm before sending", "Press Send twice to send, so a message doesn't go out by accident.", self.bool("sendConfirmation", false)));
                    out.push(toggle("requestReadReceiptDefault", "Ask for a read receipt", "New messages ask the recipient to confirm they read them.", self.bool("requestReadReceiptDefault", false)));
                }
                if tab == "calendar" {
                    out.insert(1.min(out.len()), sel_pref("calendarDayStartHour", "Day starts at", "Where the day and week views open."));
                }
                if tab == "reading" {
                    out.push(row("section", "", "Rendering", "How the content of an email is drawn."));
                    out.push(select("desktop.renderMode", "Show email content with", "WebView2 uses the Edge engine. Blitz is a light native Rust engine that never loads anything from the network.", &["WebView2 (full HTML)", "Blitz (light, native)", "Plain text"], cfg.render_mode));
                }
                if tab == "privacy" {
                    out.insert(0, row("section", "", "Remote content", "Images and other content loaded from the internet can tell senders when you read their mail."));
                    out.insert(1, select("desktop.remoteImages", "Load remote images", "", &["Ask first (blocked until you choose)", "Always load"], if cfg.remote_images { 1 } else { 0 }));
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
