#![cfg_attr(not(test), windows_subsystem = "windows")]

mod admin;
mod agent;
mod ai;
mod calendar;
mod compose;
mod contacts;
mod files;
mod jmap;
mod lite;
mod mail;
mod oauth;
mod plugins;
mod server_settings;
mod settings;
mod templates;
mod spam;

use chrono::{DateTime, Local};
use serde_json::{json, Value};
use slint::{ComponentHandle, Model, ModelRc, SharedString, VecModel};
use std::sync::{Arc, Mutex};

slint::include_modules!();

thread_local! {
    static WV: std::cell::RefCell<Option<wry::WebView>> = const { std::cell::RefCell::new(None) };
    static REPLY: std::cell::RefCell<Option<(String, String, Option<String>, String, String)>> = const { std::cell::RefCell::new(None) };
    static PENDING: std::cell::RefCell<Option<String>> = const { std::cell::RefCell::new(None) };
}

fn csp(images: bool) -> String {
    format!(
        "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline' *; font-src * data:; img-src {}; script-src 'none'; object-src 'none'; frame-src 'none'; form-action 'none'\">",
        if images { "* data:" } else { "data:" }
    )
}

/// The open message's body, kept so it can be re-rendered (mode change, resize, "Load images").
struct Current {
    html: Option<String>,
    text: String,
    remote: bool,
    allow: bool,
}

thread_local! {
    static CUR: std::cell::RefCell<Option<Current>> = const { std::cell::RefCell::new(None) };
    static LITE_W: std::cell::Cell<f32> = const { std::cell::Cell::new(0.0) };
}

fn has_remote_images(h: &str) -> bool {
    let l = h.to_lowercase().replace(['"', '\''], "");
    ["src=http", "url(http", "background=http", "srcset=http", "src=//", "url(//"].iter().any(|p| l.contains(p))
}

fn render_current(u: &AppWindow) {
    let mode = u.get_render_mode();
    CUR.with(|c| {
        let c = c.borrow();
        let Some(cur) = c.as_ref() else { return };
        u.set_images_blocked(cur.remote && !cur.allow && mode != 2);
        let body = match &cur.html {
            Some(h) => h.clone(),
            None => format!("<pre>{}</pre>", esc(&cur.text)),
        };
        match mode {
            1 => {
                let w = u.get_bw().max(200.0);
                LITE_W.with(|x| x.set(w));
                let scale = u.window().scale_factor();
                let (buf, pw, ph) = lite::render(&wrap_html_opts(&body, false), w, scale);
                let px = slint::SharedPixelBuffer::<slint::Rgba8Pixel>::clone_from_slice(&buf, pw, ph);
                u.set_lite_image(slint::Image::from_rgba8(px));
                u.set_lite_height(ph as f32 / scale);
            }
            2 => {
                let t = if cur.text.trim().is_empty() { cur.html.as_deref().map(html_to_text).unwrap_or_default() } else { cur.text.clone() };
                u.set_r_body(t.into());
            }
            _ => show_html(wrap_html_opts(&body, cur.allow)),
        }
    });
}

fn esc(s: &str) -> String {
    s.replace('&', "&amp;").replace('<', "&lt;").replace('>', "&gt;")
}

fn show_html(h: String) {
    WV.with(|w| match &*w.borrow() {
        Some(v) => { let _ = v.load_html(&h); }
        None => PENDING.with(|p| *p.borrow_mut() = Some(h)),
    });
}

fn wrap_html(body: &str) -> String {
    wrap_html_opts(body, false)
}

fn wrap_html_opts(body: &str, images: bool) -> String {
    let csp = csp(images);
    if body.starts_with("<pre>") && !settings::Settings::load().bool("emailAlwaysLightMode", false) {
        return format!("<!doctype html><html><head><meta charset=utf-8>{csp}<base target=_blank><style>html,body{{background:#0a0a0a;color:#e5e5e5}}body{{margin:16px;font-family:'Segoe UI',sans-serif;font-size:14px;word-wrap:break-word}}pre{{white-space:pre-wrap;font-family:inherit;margin:0}}a{{color:#f0577a}}</style></head><body>{body}</body></html>");
    }
    format!("<!doctype html><html><head><meta charset=utf-8>{csp}<base target=_blank><style>body{{margin:16px;font-family:'Segoe UI',sans-serif;font-size:14px;color:#111;background:#fff;word-wrap:break-word}}pre{{white-space:pre-wrap;font-family:inherit}}img{{max-width:100%;height:auto}}</style></head><body>{body}</body></html>")
}

fn rect(x: f32, y: f32, w: f32, h: f32) -> wry::Rect {
    wry::Rect {
        position: wry::dpi::LogicalPosition::new(x as f64, y as f64).into(),
        size: wry::dpi::LogicalSize::new(w.max(1.0) as f64, h.max(1.0) as f64).into(),
    }
}

fn sync_webview(ui: &AppWindow) {
    let r = rect(ui.get_bx(), ui.get_by(), ui.get_bw(), ui.get_bh());
    let vis = ui.get_body_visible();
    // light renderer: re-layout when the reading pane width changes
    if ui.get_render_mode() == 1 && ui.get_current_mail() != "" {
        let w = ui.get_bw();
        if (LITE_W.with(|x| x.get()) - w).abs() > 4.0 && w > 50.0 { render_current(ui); }
    }
    WV.with(|w| {
        let mut w = w.borrow_mut();
        if w.is_none() {


            let html = PENDING.with(|p| p.borrow_mut().take()).unwrap_or_else(|| wrap_html(""));
            match wry::WebViewBuilder::new()
                .with_html(html)
                .with_bounds(r)
                .with_visible(vis)
                .with_devtools(false)
                .with_navigation_handler(|url| {
                    if url.starts_with("http") { oauth::open_browser(&url); false } else { true }
                })
                .build_as_child(&ui.window().window_handle())
            {
                Ok(v) => { let _ = v.focus_parent(); *w = Some(v); }
                Err(_) => {}
            }
        } else if let Some(v) = &*w {
            let _ = v.set_bounds(r);
            // keep keyboard focus in the native UI while the email view is hidden
            if WV_VISIBLE.with(|x| x.replace(vis)) != vis && !vis { let _ = v.focus_parent(); }
            let _ = v.set_visible(vis);
        }
    });
}

type Shared = Arc<Mutex<Option<Arc<jmap::Client>>>>;

pub fn config_dir() -> std::path::PathBuf {
    let base = std::env::var("APPDATA").unwrap_or_else(|_| ".".into());
    std::path::Path::new(&base).join("Barbican")
}

fn config_path() -> std::path::PathBuf {
    config_dir().join("login.txt")
}

pub fn html_to_text(h: &str) -> String {
    let mut out = String::new();
    let lower = h.to_lowercase();
    let mut i = 0;
    let b = h.as_bytes();
    while i < b.len() {
        if b[i] == b'<' {
            let rest = &lower[i..];
            for (open, close) in [("<style", "</style>"), ("<script", "</script>"), ("<head", "</head>")] {
                if rest.starts_with(open) {
                    if let Some(e) = rest.find(close) { i += e; }
                }
            }
            let rest = &lower[i..];
            if ["<br", "</p", "</div", "</tr", "<li", "</h"].iter().any(|t| rest.starts_with(t)) { out.push('\n'); }
            match h[i..].find('>') { Some(e) => i += e + 1, None => break }
        } else {
            let next = h[i..].find('<').map(|e| i + e).unwrap_or(h.len());
            out.push_str(&h[i..next]);
            i = next;
        }
    }
    let out = out.replace("&nbsp;", " ").replace("&amp;", "&").replace("&lt;", "<").replace("&gt;", ">").replace("&quot;", "\"").replace("&#39;", "'");
    let mut res = String::new();
    let mut blank = 0;
    for l in out.lines().map(str::trim) {
        if l.is_empty() { blank += 1; if blank > 1 { continue; } } else { blank = 0; }
        res.push_str(l);
        res.push('\n');
    }
    res.trim().to_string()
}

thread_local! { static WV_VISIBLE: std::cell::Cell<bool> = const { std::cell::Cell::new(false) }; }
thread_local! { static RESET_ARMED: std::cell::Cell<bool> = const { std::cell::Cell::new(false) }; }
thread_local! { static SERVER_DEBOUNCE: slint::Timer = slint::Timer::default(); }
thread_local! { static TEMPLATES: std::cell::RefCell<Vec<Value>> = const { std::cell::RefCell::new(Vec::new()) }; }
thread_local! { static THREAD_ID: std::cell::RefCell<Option<String>> = const { std::cell::RefCell::new(None) }; }

/// Load the open message's conversation (unless threading is turned off, like Bulwark's disableThreading).
fn load_thread(w: slint::Weak<AppWindow>, c: Arc<jmap::Client>, id: String) {
    if settings::Settings::load().bool("disableThreading", false) { return; }
    std::thread::spawn(move || {
        let Ok(mut list) = c.thread_emails(&id) else { return };
        list.sort_by_key(|m| m["receivedAt"].as_str().unwrap_or("").to_string());
        let tid = list.first().and_then(|m| m["threadId"].as_str().map(String::from));
        let rows: Vec<ThreadMsg> = list.iter().map(|m| ThreadMsg {
            id: m["id"].as_str().unwrap_or("").into(),
            sender: mail::sender(m).into(),
            date: mail::parse_time(&m["receivedAt"]).map(|t| t.format("%d %b %H:%M").to_string()).unwrap_or_default().into(),
            preview: mail::clean_preview(m["preview"].as_str().unwrap_or("")).into(),
            current: m["id"] == id.as_str(),
            unread: m["keywords"]["$seen"].as_bool() != Some(true),
        }).collect();
        let _ = w.upgrade_in_event_loop(move |u| {
            if u.get_current_mail().as_str() != id { return; }
            THREAD_ID.with(|t| *t.borrow_mut() = tid);
            u.set_thread_msgs(ModelRc::new(VecModel::from(rows)));
        });
    });
}
thread_local! { static MY_ADDRS: std::cell::RefCell<Vec<String>> = const { std::cell::RefCell::new(Vec::new()) }; }

thread_local! {
    /// Loaded native plugins (UI thread only).
    static PLUGINS: std::cell::RefCell<Vec<plugins::Plugin>> = const { std::cell::RefCell::new(Vec::new()) };
    static PLUGIN_ERRORS: std::cell::RefCell<Vec<String>> = const { std::cell::RefCell::new(Vec::new()) };
}

pub fn disabled_plugins() -> Vec<String> {
    let p = settings::Settings::load();
    p.prefs.get("disabledPlugins").and_then(|v| v.as_array()).map(|a| a.iter().filter_map(|x| x.as_str().map(String::from)).collect()).unwrap_or_default()
}

pub fn plugin_list() -> Vec<serde_json::Value> {
    PLUGINS.with(|p| p.borrow().iter().map(plugins::describe).collect())
}

pub fn plugin_errors() -> Vec<String> {
    PLUGIN_ERRORS.with(|e| e.borrow().clone())
}

pub fn plugin_compose_check(draft: &serde_json::Value) -> Option<String> {
    let dis = disabled_plugins();
    PLUGINS.with(|p| plugins::compose_check(&p.borrow(), &dis, draft))
}

thread_local! {
    /// The open message and its listed attachments.
    static CUR_MSG: std::cell::RefCell<Option<(Value, Vec<Value>)>> = const { std::cell::RefCell::new(None) };
}

fn sanitize_name(n: &str) -> String {
    let bad = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
    let s: String = n.chars().map(|c| if bad.contains(&c) || c.is_control() { '_' } else { c }).collect();
    let s = s.trim().trim_matches('.').to_string();
    if s.is_empty() { "attachment".into() } else { s }
}

static SESSION_GEN: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);

/// Live updates: listen to the JMAP event stream and refresh on state changes.
fn start_push(w: slint::Weak<AppWindow>, c: Arc<jmap::Client>) {
    let gen = SESSION_GEN.fetch_add(1, std::sync::atomic::Ordering::SeqCst) + 1;
    std::thread::spawn(move || loop {
        if SESSION_GEN.load(std::sync::atomic::Ordering::SeqCst) != gen { return; }
        let (w2, c2) = (w.clone(), c.clone());
        let res = c.watch(&|_| {
            if SESSION_GEN.load(std::sync::atomic::Ordering::SeqCst) != gen { return; }
            let (w3, c3) = (w2.clone(), c2.clone());
            let _ = w2.upgrade_in_event_loop(move |_| schedule_refresh(w3, c3));
        });
        if res.is_err() { std::thread::sleep(std::time::Duration::from_secs(20)); }
    });
}

thread_local! { static REFRESH: slint::Timer = slint::Timer::default(); }

fn schedule_refresh(w: slint::Weak<AppWindow>, c: Arc<jmap::Client>) {
    REFRESH.with(|t| t.start(slint::TimerMode::SingleShot, std::time::Duration::from_millis(700), move || {
        mail::load_mailboxes(w.clone(), c.clone(), false);
        mail::reload(w.clone(), c.clone());
        check_new_mail(c.clone());
    }));
}

static NOTIFIED: Mutex<Option<std::collections::HashSet<String>>> = Mutex::new(None);

/// Desktop notification for newly arrived unread inbox mail (Bulwark: emailNotificationsEnabled).
fn check_new_mail(c: Arc<jmap::Client>) {
    let prefs = settings::Settings::load();
    if !prefs.bool("emailNotificationsEnabled", true) { return; }
    std::thread::spawn(move || {
        let Ok(roles) = c.role_ids() else { return };
        let Some(inbox) = roles.get("inbox") else { return };
        let Ok((list, _, _)) = c.list(json!({"operator": "AND", "conditions": [{"inMailbox": inbox}, {"notKeyword": "$seen"}]}), 0, 10) else { return };
        let mut guard = NOTIFIED.lock().unwrap();
        let first_run = guard.is_none();
        let seen = guard.get_or_insert_with(Default::default);
        for m in list.iter().rev() {
            let id = m["id"].as_str().unwrap_or("").to_string();
            if seen.insert(id) && !first_run {
                let _ = notify_rust::Notification::new()
                    .appname("Barbican")
                    .summary(&mail::sender(m))
                    .body(&format!("{}\n{}", m["subject"].as_str().unwrap_or("(no subject)"), mail::clean_preview(m["preview"].as_str().unwrap_or(""))))
                    .show();
            }
        }
    });
}

fn human_size(n: u64) -> String {
    if n >= 1_000_000 { format!("{:.1} MB", n as f64 / 1e6) } else if n >= 1000 { format!("{} KB", n / 1000) } else { format!("{n} B") }
}

/// Replace cid: references in the HTML body with data: URIs of the inline parts.
fn inline_cid_images(c: &jmap::Client, m: &mut Value) {
    use base64::Engine;
    let parts: Vec<Value> = m["attachments"].as_array().cloned().unwrap_or_default().into_iter()
        .filter(|a| a["cid"].is_string() && a["type"].as_str().is_some_and(|t| t.starts_with("image/"))).collect();
    if parts.is_empty() { return; }
    let Some(html_parts) = m["htmlBody"].as_array().cloned() else { return };
    for hp in html_parts {
        let Some(pid) = hp["partId"].as_str() else { continue };
        let Some(mut html) = m["bodyValues"][pid]["value"].as_str().map(String::from) else { continue };
        if !html.contains("cid:") { continue; }
        for p in &parts {
            let cid = p["cid"].as_str().unwrap_or("").trim_matches(['<', '>']);
            if !html.contains(&format!("cid:{cid}")) { continue; }
            let (blob, mime) = (p["blobId"].as_str().unwrap_or(""), p["type"].as_str().unwrap_or("image/png"));
            if p["size"].as_u64().unwrap_or(0) > 5_000_000 { continue; }
            if let Ok(bytes) = c.download(blob, p["name"].as_str().unwrap_or("image"), mime) {
                let uri = format!("data:{mime};base64,{}", base64::engine::general_purpose::STANDARD.encode(bytes));
                html = html.replace(&format!("cid:{cid}"), &uri);
            }
        }
        m["bodyValues"][pid]["value"] = json!(html);
    }
}

fn email_for_ai() -> Option<String> {
    REPLY.with(|r| r.borrow().as_ref().map(|(_, subj, _, text, who)| format!("From: {who}\nSubject: {subj}\n\n{text}")))
}

fn initials(s: &str) -> String {
    let letters: String = s.chars().filter(|c| c.is_alphanumeric()).take(2).collect();
    if letters.is_empty() { "?".into() } else { letters.to_uppercase() }
}







fn parse_time(v: &Value) -> Option<DateTime<Local>> {
    DateTime::parse_from_rfc3339(v.as_str()?).ok().map(|d| d.with_timezone(&Local))
}



fn body_text(m: &Value) -> (String, String) {
    let collect = |key: &str| -> String {
        m[key].as_array().map(|parts| {
            parts.iter().filter_map(|p| {
                let pid = p["partId"].as_str()?;
                m["bodyValues"][pid]["value"].as_str().map(|s| s.to_string())
            }).collect::<Vec<_>>().join("\n")
        }).unwrap_or_default()
    };
    let html = collect("htmlBody");
    let text = collect("textBody");
    (html, text)
}

#[allow(dead_code)]
fn body_html(m: &Value) -> String {
    let (html, text) = body_text(m);
    let is_html = m["htmlBody"].as_array().map(|a| a.iter().any(|p| p["type"] == "text/html")).unwrap_or(false);
    if is_html && !html.is_empty() { html } else { format!("<pre>{}</pre>", esc(&text)) }
}

fn load_folder(ui: slint::Weak<AppWindow>, client: Arc<jmap::Client>, mailbox: Option<String>, text: Option<String>) {
    let filter = match (mailbox, text) {
        (_, Some(t)) => json!({"text": t}),
        (Some(m), None) if m == "__scheduled" => json!({"scheduled": true}),
        (Some(m), None) => json!({"inMailbox": m}),
        _ => json!({}),
    };
    mail::load_list(ui, client, filter);
}

fn load_mailboxes(ui: slint::Weak<AppWindow>, client: Arc<jmap::Client>, select_inbox: bool) {
    mail::load_mailboxes(ui, client, select_inbox);
}

fn main() -> Result<(), slint::PlatformError> {
    // Skia: the same text/graphics engine Chrome uses, so text matches Bulwark in the browser
    // (and gets system font fallback for CJK and other scripts).
    let _ = slint::BackendSelector::new().renderer_name("skia".into()).select();
    let ui = AppWindow::new()?;
    apply_theme(&ui);
    {
        let (loaded, errors) = plugins::load_all();
        PLUGINS.with(|p| *p.borrow_mut() = loaded);
        PLUGIN_ERRORS.with(|e| *e.borrow_mut() = errors);
    }
    let state: Shared = Arc::new(Mutex::new(None));

    if let Ok(s) = std::fs::read_to_string(config_path()) {
        let mut l = s.lines();
        ui.set_server(l.next().unwrap_or("").into());
        ui.set_user(l.next().unwrap_or("").into());
    }

    let w = ui.as_weak();
    let st = state.clone();
    ui.on_login(move |server, user, pass, totp, remember| {
        let (w, st) = (w.clone(), st.clone());
        let u = w.unwrap();
        u.set_busy(true);
        u.set_status("".into());
        let (server, user, pass, totp) = (server.to_string(), user.to_string(), pass.to_string(), totp.to_string());
        std::thread::spawn(move || {
            let res = jmap::Client::connect(&server, &user, &pass, &totp);
            // a 2FA code is single-use, so only the password itself can be remembered
            let rem = (remember && totp.trim().is_empty()).then(|| serde_json::json!({"kind": "password", "server": server, "user": user, "pass": pass}));
            finish_login(w, st, res, server, user, rem);
        });
    });

    let w = ui.as_weak();
    let st = state.clone();
    ui.on_sso_login(move |server, user, remember| {
        let (w, st) = (w.clone(), st.clone());
        let u = w.unwrap();
        if server.trim().is_empty() { u.set_status("Enter your server address first.".into()); return; }
        u.set_busy(true);
        u.set_status("Continue in your browser…".into());
        let client = u.get_client_id().trim().to_string();
        let client = if client.is_empty() { "barbican".to_string() } else { client };
        let (server, user) = (server.to_string(), user.to_string());
        std::thread::spawn(move || {
            let base = jmap::normalize_base(&server);
            let res = oauth::sign_in(&base, &user, &client).and_then(|t| jmap::Client::connect_oauth(&base, t));
            finish_login(w, st, res, server, user, remember.then(|| serde_json::json!({"kind": "oauth"})));
        });
    });

    let w = ui.as_weak();
    let st = state.clone();
    ui.on_sign_out(move || {
        let u = w.unwrap();
        SESSION_GEN.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
        if let Some(c) = st.lock().unwrap().take() {
            if let Some(t) = c.tokens() { std::thread::spawn(move || oauth::revoke(&c.base, &t)); }
        }
        oauth::forget();
        REMEMBER_OAUTH.store(false, std::sync::atomic::Ordering::SeqCst);
        u.set_current_mail("".into());
        u.set_folders(ModelRc::new(VecModel::from(Vec::<Folder>::new())));
        u.set_mails(ModelRc::new(VecModel::from(Vec::<Mail>::new())));
        u.set_status("Signed out.".into());
        u.set_logged_in(false);
    });

    // restore a remembered session
    if let Some(v) = oauth::remembered() {
        let (w, st) = (ui.as_weak(), state.clone());
        ui.set_busy(true);
        ui.set_status("Signing in…".into());
        std::thread::spawn(move || {
            let server = v["server"].as_str().unwrap_or("").to_string();
            let user = v["user"].as_str().unwrap_or("").to_string();
            let res = if v["kind"] == "oauth" {
                let t = oauth::Tokens {
                    access: String::new(),
                    refresh: v["refresh"].as_str().map(String::from),
                    expires: None,
                    token_endpoint: v["endpoint"].as_str().unwrap_or("").into(),
                    client_id: v["client"].as_str().unwrap_or("").into(),
                };
                jmap::Client::connect_oauth(&server, t)
            } else {
                jmap::Client::connect(&server, &user, v["pass"].as_str().unwrap_or(""), "")
            };
            if res.is_err() { oauth::forget(); }
            let kind = v["kind"].as_str().unwrap_or("").to_string();
            finish_login(w, st, res, server, user, Some(serde_json::json!({"kind": kind, "pass": v["pass"]})));
        });
    }

    let w = ui.as_weak();
    let st = state.clone();
    ui.on_select_folder(move |id| {
        let Some(c) = st.lock().unwrap().clone() else { return };
        let u = w.unwrap();
        u.set_current_mail("".into());
        u.set_search("".into());
        load_folder(w.clone(), c, Some(id.to_string()), None);
    });

    let w = ui.as_weak();
    let st = state.clone();
    ui.on_do_search(move |text| {
        let Some(c) = st.lock().unwrap().clone() else { return };
        let u = w.unwrap();
        u.set_current_mail("".into());
        if text.trim().is_empty() {
            load_folder(w.clone(), c, Some(u.get_current_folder().to_string()), None);
        } else {
            load_folder(w.clone(), c, None, Some(text.to_string()));
        }
    });

    let w = ui.as_weak();
    let st = state.clone();
    ui.on_refresh(move || {
        let Some(c) = st.lock().unwrap().clone() else { return };
        let u = w.unwrap();
        load_mailboxes(w.clone(), c.clone(), false);
        load_folder(w.clone(), c, Some(u.get_current_folder().to_string()), None);
    });

    let w = ui.as_weak();
    let st = state.clone();
    ui.on_open_mail(move |id| {
        let Some(c) = st.lock().unwrap().clone() else { return };
        let u = w.unwrap();
        if settings::Settings::load().prefs.get("markAsReadDelay").and_then(|v| v.as_i64()).unwrap_or(0) == 0 { mail::mark_read_local(&u, &id); }
        u.set_plugin_rows(ModelRc::default());
        // keep the conversation strip when moving within the same thread
        let same_thread = mail::email(&id).and_then(|m| m["threadId"].as_str().map(String::from)) == THREAD_ID.with(|t| t.borrow().clone());
        if !same_thread { u.set_thread_msgs(ModelRc::default()); }
        u.set_attachments(ModelRc::default());
        u.set_r_to("".into());
        u.set_r_email("".into());
        u.set_r_subject("".into());
        u.set_spam_verdict("".into());
        u.set_spam_detail("".into());
        u.set_ai_result("".into());
        REPLY.with(|r| *r.borrow_mut() = None);
        u.set_r_from("".into());
        u.set_r_date("".into());
        CUR.with(|c| *c.borrow_mut() = None);
        lite::clear();
        u.set_images_blocked(false);
        u.set_lite_height(0.0);
        u.set_r_body("Loading…".into());
        show_html(wrap_html("<p style=\"color:#888\">Loading…</p>"));
        let w = w.clone();
        let id = id.to_string();
        std::thread::spawn(move || {
            let res = c.email_body(&id).map(|mut m| { inline_cid_images(&c, &mut m); m });
            // Bulwark markAsReadDelay (ms): 0 = instantly, N = after N ms if still open, -1 = never
            let delay = settings::Settings::load().prefs.get("markAsReadDelay").and_then(|v| v.as_i64()).unwrap_or(0);
            if delay >= 0 && res.as_ref().is_ok_and(|m| m["keywords"]["$seen"].as_bool() != Some(true)) {
                let (c3, id3, w3) = (c.clone(), id.clone(), w.clone());
                std::thread::spawn(move || {
                    std::thread::sleep(std::time::Duration::from_millis(delay as u64));
                    let still = std::sync::Arc::new(std::sync::atomic::AtomicBool::new(false));
                    let (s2, i2) = (still.clone(), id3.clone());
                    let _ = w3.upgrade_in_event_loop(move |u| s2.store(u.get_current_mail().as_str() == i2, std::sync::atomic::Ordering::SeqCst));
                    std::thread::sleep(std::time::Duration::from_millis(100));
                    if delay == 0 || still.load(std::sync::atomic::Ordering::SeqCst) {
                        let _ = c3.set_keyword(&[id3.clone()], "$seen", true);
                        let _ = w3.upgrade_in_event_loop(move |u| mail::mark_read_local(&u, &id3));
                    }
                });
            }
            let c2 = c.clone();
            let w2 = w.clone();
            let _ = w.upgrade_in_event_loop(move |u| {
                if u.get_current_mail().as_str() != id { return; }
                match res {
                    Ok(m) => {
                        let f = &m["from"][0];
                        if m["keywords"]["$draft"].as_bool() == Some(true) {
                            compose::open_draft(&u, c2.clone(), &m);
                        }
                        let from = mail::sender(&m);
                        let email = f["email"].as_str().unwrap_or("").to_string();
                        u.set_r_email(email.clone().into());
                        u.set_r_initials(initials(&from).into());
                        u.set_r_color(mail::tint(&from));
                        u.set_r_flagged(m["keywords"]["$flagged"].as_bool() == Some(true));
                        let names = |k: &str| m[k].as_array().map(|a| a.iter().map(|x| {
                            let e = x["email"].as_str().unwrap_or("");
                            if e.eq_ignore_ascii_case(&c2.username) || MY_ADDRS.with(|a| a.borrow().iter().any(|x| x.eq_ignore_ascii_case(e))) { "me".to_string() } else { x["name"].as_str().filter(|n| !n.is_empty()).unwrap_or(e).to_string() }
                        }).collect::<Vec<_>>().join(", ")).unwrap_or_default();
                        let mut to = names("to");
                        let cc = names("cc");
                        if !cc.is_empty() { to = format!("{to}  ·  Cc: {cc}"); }
                        u.set_r_to(to.into());
                        let atts: Vec<Value> = m["attachments"].as_array().cloned().unwrap_or_default().into_iter()
                            .filter(|a| !(a["cid"].is_string() && a["disposition"] != "attachment" && a["type"].as_str().is_some_and(|t| t.starts_with("image/"))))
                            .collect();
                        let rows: Vec<Att> = atts.iter().enumerate().map(|(i, a)| Att {
                            name: a["name"].as_str().unwrap_or("attachment").into(),
                            size: human_size(a["size"].as_u64().unwrap_or(0)).into(),
                            index: i as i32,
                        }).collect();
                        u.set_attachments(ModelRc::new(VecModel::from(rows)));
                        CUR_MSG.with(|x| *x.borrow_mut() = Some((m.clone(), atts)));
                        load_thread(w2.clone(), c2.clone(), m["id"].as_str().unwrap_or("").to_string());
                        u.set_r_subject(SharedString::from(m["subject"].as_str().unwrap_or("(no subject)")));
                        u.set_r_from(from.into());
                        u.set_r_date(parse_time(&m["receivedAt"]).map(|t| t.format("%a %d %b %Y, %H:%M").to_string()).unwrap_or_default().into());
                        {
                            let (html, text) = body_text(&m);
                            let is_html = m["htmlBody"].as_array().map(|a| a.iter().any(|p| p["type"] == "text/html")).unwrap_or(false);
                            let html = (is_html && !html.is_empty()).then_some(html);
                            let remote = html.as_deref().is_some_and(has_remote_images);
                            let allow = u.get_auto_images();
                            CUR.with(|c| *c.borrow_mut() = Some(Current { html, text, remote, allow }));
                            render_current(&u);
                        }
                        let (_, text) = body_text(&m);
                        let rt = (
                            f["email"].as_str().unwrap_or("").to_string(),
                            m["subject"].as_str().unwrap_or("").to_string(),
                            m["messageId"][0].as_str().map(String::from),
                            text,
                            u.get_r_date().to_string() + ", " + &u.get_r_from(),
                        );
                        REPLY.with(|r| *r.borrow_mut() = Some(rt));
                        let dis = disabled_plugins();
                        let rows = PLUGINS.with(|p| plugins::email_details(&p.borrow(), &dis, &m));
                        u.set_plugin_rows(ModelRc::new(VecModel::from(rows.into_iter().map(|(label, value, tone)| PRow { label: label.into(), value: value.into(), tone: tone.into() }).collect::<Vec<_>>())));
                        let spam_plugin = PLUGINS.with(|p| p.borrow().iter().any(|x| x.id == "spam-score")) && !dis.iter().any(|d| d == "spam-score");
                        let headers = m["headers"].as_array().cloned().unwrap_or_default();
                        if let Some(sp) = spam::analyze(&headers).filter(|_| !spam_plugin) {
                            if sp.verdict != "clean" || u.get_spam_show_clean() {
                                u.set_spam_verdict(sp.verdict.into());
                                u.set_spam_detail(sp.detail.into());
                            }
                        }
                        load_mailboxes(w2, c2, false);
                    }
                    Err(e) => show_html(wrap_html(&esc(&format!("Could not load message: {e}")))),
                }
            });
        });
    });

    let cfg = Arc::new(Mutex::new(ai::Config::load()));
    ui.set_spam_show_clean(cfg.lock().unwrap().show_clean_spam);

    let prefs = Arc::new(Mutex::new(settings::Settings::load()));
    ui.set_show_advanced_settings(prefs.lock().unwrap().bool("showAdvancedSettings", false));
    let st_acc = state.clone();
    let account_info = move || {
        let c = st_acc.lock().unwrap().clone();
        settings::Account {
            email: c.as_ref().map(|c| c.username.clone()).unwrap_or_default(),
            server: c.as_ref().map(|c| c.base.clone()).unwrap_or_default(),
            auth: c.as_ref().map(|c| if c.tokens().is_some() { "OAuth / single sign-on" } else { "Password" }.to_string()).unwrap_or_default(),
        }
    };
    let account_info = Arc::new(account_info);
    let server_cache: server_settings::Shared = Default::default();
    // load a server-backed settings page in the background, then redraw
    let load_server_page = {
        let (w, st, srv) = (ui.as_weak(), state.clone(), server_cache.clone());
        Arc::new(move |tab: String| {
            let Some(c) = st.lock().unwrap().clone() else { return };
            let (w, srv) = (w.clone(), srv.clone());
            std::thread::spawn(move || {
                server_settings::load(&tab, &c, &srv);
                let _ = w.upgrade_in_event_loop(|u| u.invoke_settings_search(u.get_settings_query()));
            });
        })
    };
    // rebuild the current settings page (or search results)
    let refresh_settings = {
        let (w, pf, cf, acc, srv) = (ui.as_weak(), prefs.clone(), cfg.clone(), account_info.clone(), server_cache.clone());
        Arc::new(move || {
            let u = w.unwrap();
            let p = pf.lock().unwrap();
            let c = cf.lock().unwrap().clone();
            let adv = u.get_show_advanced_settings();
            u.set_settings_tabs(p.tabs(adv));
            let q = u.get_settings_query().to_string();
            let rows = if q.trim().is_empty() {
                let tab = u.get_settings_tab().to_string();
                u.set_settings_title(p.title(&tab).into());
                let mut rows = p.rows(&tab, &c, &acc());
                if let Some(extra) = server_settings::rows(&tab, &srv) {
                    if tab == "account" || tab == "composing" { rows.extend(extra); } else { rows = extra; }
                }
                rows
            } else {
                u.set_settings_title(format!("Search: {q}").into());
                p.search(q.trim(), &c, &acc(), adv)
            };
            u.set_settings_rows(ModelRc::new(VecModel::from(rows)));
            u.set_ai_hidden(c.hidden);
            u.set_render_mode(c.render_mode);
            u.set_auto_images(c.remote_images);
            u.set_spam_show_clean(c.show_clean_spam);
            u.set_chips(chips_model(&c));
        })
    };

    let (w, rs, lsp) = (ui.as_weak(), refresh_settings.clone(), load_server_page.clone());
    ui.on_open_settings(move || {
        let u = w.unwrap();
        u.set_settings_query("".into());
        rs();
        u.set_show_settings(true);
        lsp(u.get_settings_tab().to_string());
    });

    let (w, rs, lsp) = (ui.as_weak(), refresh_settings.clone(), load_server_page.clone());
    ui.on_settings_open_tab(move |id| {
        let u = w.unwrap();
        u.set_settings_query("".into());
        u.set_settings_tab(id.clone());
        rs();
        lsp(id.to_string());
    });

    let rs = refresh_settings.clone();
    ui.on_settings_search(move |_| rs());

    // writes to the server, debounced for typing
    let server_write = {
        let (w, st) = (ui.as_weak(), state.clone());
        Arc::new(move |job: Box<dyn FnOnce(&jmap::Client) -> Result<(), String> + Send>, delay_ms: u64| {
            let Some(c) = st.lock().unwrap().clone() else { return };
            let w = w.clone();
            let job = std::sync::Mutex::new(Some(job));
            SERVER_DEBOUNCE.with(|t| t.start(slint::TimerMode::SingleShot, std::time::Duration::from_millis(delay_ms), move || {
                let Some(job) = job.lock().unwrap().take() else { return };
                let (w, c) = (w.clone(), c.clone());
                std::thread::spawn(move || {
                    if let Err(e) = job(&c) {
                        let _ = w.upgrade_in_event_loop(move |u| mail::show_toast(&u, &format!("Not saved: {e}"), false));
                    }
                });
            }));
        })
    };

    let (pf, cf, rs, srv, sw) = (prefs.clone(), cfg.clone(), refresh_settings.clone(), server_cache.clone(), server_write.clone());
    ui.on_settings_set_toggle(move |key, on| {
        if key.starts_with("server.") {
            if let Some(job) = server_settings::edit(&key, &serde_json::json!(on), &srv) { sw(job, 10); }
            rs();
            return;
        }
        let mut p = pf.lock().unwrap();
        let on = if p.is_inverted(&key) { !on } else { on };
        p.set(&key, serde_json::json!(on), &mut cf.lock().unwrap());
        drop(p);
        rs();
    });

    let (w, pf, cf, rs) = (ui.as_weak(), prefs.clone(), cfg.clone(), refresh_settings.clone());
    ui.on_settings_set_select(move |key, idx| {
        let mut p = pf.lock().unwrap();
        let v = p.select_value(&key, idx);
        p.set(&key, v, &mut cf.lock().unwrap());
        drop(p);
        rs();
        if key == "desktop.renderMode" || key == "desktop.remoteImages" { render_current(&w.unwrap()); }
        if key == "theme" { apply_theme(&w.unwrap()); }
    });

    // text edits are saved as typed, without rebuilding the page (keeps the caret)
    let (pf, cf, srv, sw, wk) = (prefs.clone(), cfg.clone(), server_cache.clone(), server_write.clone(), ui.as_weak());
    ui.on_settings_set_text(move |key, t| {
        if key.starts_with("server.") {
            if let Some(job) = server_settings::edit(&key, &serde_json::json!(t.to_string()), &srv) { sw(job, 900); }
            return;
        }
        pf.lock().unwrap().set(&key, serde_json::json!(t.to_string()), &mut cf.lock().unwrap());
        if key == "admin.key" { if let Some(u) = wk.upgrade() { u.set_admin_enabled(!t.trim().is_empty()); } }
    });

    let (w, st, srv, lsp, pf2, cf2) = (ui.as_weak(), state.clone(), server_cache.clone(), load_server_page.clone(), prefs.clone(), cfg.clone());
    ui.on_settings_button(move |key| {
        let u = w.unwrap();
        if key.starts_with("server.") {
            let Some(job) = server_settings::button(&key, &srv) else { return };
            let Some(c) = st.lock().unwrap().clone() else { return };
            let (w2, srv2, lsp2, tab) = (w.clone(), srv.clone(), lsp.clone(), u.get_settings_tab().to_string());
            u.invoke_settings_search(u.get_settings_query());
            std::thread::spawn(move || {
                let res = job(&c, &srv2);
                let _ = w2.upgrade_in_event_loop(move |u| {
                    if let Err(e) = res { mail::show_toast(&u, &e, false); }
                    lsp2(tab);
                });
            });
            return;
        }
        if key == "signout" { u.set_show_settings(false); u.invoke_sign_out(); }
        if key == "plugins.folder" { oauth::open_browser(&plugins::dir().to_string_lossy()); }
        if key == "backup.export" {
            if let Some(path) = rfd::FileDialog::new().set_file_name("barbican-settings.json").add_filter("JSON", &["json"]).save_file() {
                let data = serde_json::to_string_pretty(&pf2.lock().unwrap().export()).unwrap_or_default();
                let msg = match std::fs::write(&path, data) { Ok(()) => "Settings exported".to_string(), Err(e) => format!("Export failed: {e}") };
                mail::show_toast(&u, &msg, false);
            }
        }
        if key == "backup.import" {
            if let Some(path) = rfd::FileDialog::new().add_filter("JSON", &["json"]).pick_file() {
                let res = std::fs::read_to_string(&path).map_err(|e| e.to_string())
                    .and_then(|t| serde_json::from_str::<Value>(&t).map_err(|e| e.to_string()))
                    .and_then(|v| pf2.lock().unwrap().import(&v));
                match res {
                    Ok(()) => { *cf2.lock().unwrap() = ai::Config::load(); apply_theme(&u); mail::show_toast(&u, "Settings imported", false); u.invoke_settings_search(u.get_settings_query()); }
                    Err(e) => mail::show_toast(&u, &format!("Import failed: {e}"), false),
                }
            }
        }
        if key == "backup.reset" {
            if RESET_ARMED.with(|x| x.replace(true)) {
                RESET_ARMED.with(|x| x.set(false));
                pf2.lock().unwrap().reset();
                *cf2.lock().unwrap() = ai::Config::load();
                apply_theme(&u);
                mail::show_toast(&u, "Settings reset to defaults", false);
                u.invoke_settings_search(u.get_settings_query());
            } else {
                mail::show_toast(&u, "Press Reset again to put every setting back to its default.", false);
            }
        }
    });


    let w = ui.as_weak();
    let cf = cfg.clone();
    ui.on_ai_reader(move |action| {
        let u = w.unwrap();
        let Some(email) = email_for_ai() else { return };
        let c = cf.lock().unwrap().clone();
        let action = action.to_string();
        let prompt = ai::reader_prompt(&action, &email, &c.lang);
        u.set_ai_busy(true);
        let w = w.clone();
        std::thread::spawn(move || {
            let res = c.ask(ai::SYSTEM, &prompt);
            let _ = w.upgrade_in_event_loop(move |u| {
                u.set_ai_busy(false);
                match res {
                    Ok(text) if action == "Draft reply" => {
                        let Some((to, subj, _, orig, who)) = REPLY.with(|r| r.borrow().clone()) else { return };
                        let subj = if subj.to_lowercase().starts_with("re:") { subj } else { format!("Re: {subj}") };
                        let quoted: String = orig.lines().map(|l| format!("> {l}\n")).collect();
                        u.set_c_to(to.into());
                        u.set_c_subject(subj.into());
                        u.set_c_body(format!("{text}\n\nOn {who} wrote:\n{quoted}").into());
                        u.set_c_status("".into());
                        u.set_can_undo(false);
                        u.set_composing(true);
                    }
                    Ok(text) => u.set_ai_result(text.into()),
                    Err(e) => u.set_ai_result(e.into()),
                }
            });
        });
    });

    thread_local! { static UNDO: std::cell::RefCell<Option<String>> = const { std::cell::RefCell::new(None) }; }

    let w = ui.as_weak();
    let cf = cfg.clone();
    ui.on_ai_compose(move |action| {
        let u = w.unwrap();
        let draft = u.get_c_body().to_string();
        if draft.trim().is_empty() { u.set_c_status("Write something first.".into()); return; }
        let c = cf.lock().unwrap().clone();
        let prompt = ai::compose_prompt(&action, &draft, &c.lang);
        u.set_ai_busy(true);
        u.set_c_status("".into());
        let w = w.clone();
        std::thread::spawn(move || {
            let res = c.ask(ai::SYSTEM, &prompt);
            let _ = w.upgrade_in_event_loop(move |u| {
                u.set_ai_busy(false);
                match res {
                    Ok(text) => {
                        UNDO.with(|x| *x.borrow_mut() = Some(draft));
                        u.set_c_body(text.into());
                        u.set_can_undo(true);
                    }
                    Err(e) => u.set_c_status(e.into()),
                }
            });
        });
    });

    let w = ui.as_weak();
    ui.on_ai_undo(move || {
        let u = w.unwrap();
        if let Some(prev) = UNDO.with(|x| x.borrow_mut().take()) { u.set_c_body(prev.into()); }
        u.set_can_undo(false);
    });

    // ---------------- assistant chat ----------------
    let history: Arc<Mutex<Vec<agent::Msg>>> = Arc::new(Mutex::new(
        std::fs::read_to_string(chat_path()).ok()
            .and_then(|s| serde_json::from_str::<Value>(&s).ok())
            .map(|v| agent::from_json(&v)).unwrap_or_default(),
    ));
    ui.set_chat(display_chat(&history.lock().unwrap()));
    ui.set_chips(chips_model(&cfg.lock().unwrap()));
    ui.set_ai_hidden(cfg.lock().unwrap().hidden);
    ui.set_render_mode(cfg.lock().unwrap().render_mode);
    ui.set_auto_images(cfg.lock().unwrap().remote_images);

    let w = ui.as_weak();
    ui.on_load_images(move || {
        let u = w.unwrap();
        CUR.with(|c| if let Some(c) = c.borrow_mut().as_mut() { c.allow = true; });
        render_current(&u);
    });

    let w = ui.as_weak();
    ui.on_rerender(move || render_current(&w.unwrap()));

    ui.on_lite_click(move |x, y| {
        if let Some(href) = lite::link_at(x, y) {
            if href.starts_with("http://") || href.starts_with("https://") || href.starts_with("mailto:") { oauth::open_browser(&href); }
        }
    });
    let cancel = Arc::new(std::sync::atomic::AtomicBool::new(false));

    let w = ui.as_weak();
    let (st, cf, hist, can) = (state.clone(), cfg.clone(), history.clone(), cancel.clone());
    ui.on_chat_send(move |text| {
        let u = w.unwrap();
        let text = text.trim().to_string();
        if text.is_empty() || u.get_chat_busy() { return; }
        let Some(c) = st.lock().unwrap().clone() else { return };
        let conf = cf.lock().unwrap().clone();
        let prompt = expand_prompt(&text, &conf, c.has("urn:ietf:params:jmap:calendars"));
        u.set_chat_input("".into());
        let ctx = agent::Ctx {
            open_message: Some(u.get_current_mail().to_string()).filter(|s| !s.is_empty()),
            open_subject: u.get_r_subject().to_string(),
        };
        hist.lock().unwrap().push(agent::Msg::User(prompt));
        push_chat(&u, "user", &text);
        u.set_chat_busy(true);
        can.store(false, std::sync::atomic::Ordering::SeqCst);
        let (w, hist, can) = (w.clone(), hist.clone(), can.clone());
        std::thread::spawn(move || {
            let host = UiHost { w: w.clone(), client: c.clone(), cancel: can };
            let mut h = hist.lock().unwrap().clone();
            let res = agent::run(&conf, &c, &mut h, &ctx, &host);
            if let Err(e) = &res {
                // keep history consistent: drop a dangling tool-call turn
                while matches!(h.last(), Some(agent::Msg::Assistant { calls, .. }) if !calls.is_empty()) { h.pop(); }
                h.push(agent::Msg::Assistant { text: format!("(error: {e})"), calls: vec![] });
            }
            let keep = h.len().saturating_sub(80);
            let mut h: Vec<_> = h.split_off(keep);
            while matches!(h.first(), Some(agent::Msg::Results(_)) | Some(agent::Msg::Assistant { .. })) { h.remove(0); }
            let _ = std::fs::write(chat_path(), agent::to_json(&h).to_string());
            *hist.lock().unwrap() = h;
            let _ = w.upgrade_in_event_loop(move |u| {
                u.set_chat_busy(false);
                u.set_confirm_text("".into());
                match res {
                    Ok(t) => push_chat(&u, "assistant", if t.is_empty() { "Done." } else { &t }),
                    Err(e) => push_chat(&u, "error", &e),
                }
            });
        });
    });

    let (w, hist) = (ui.as_weak(), history.clone());
    ui.on_chat_reload(move || {
        let h = std::fs::read_to_string(chat_path()).ok()
            .and_then(|s| serde_json::from_str::<Value>(&s).ok())
            .map(|v| agent::from_json(&v)).unwrap_or_default();
        w.unwrap().set_chat(display_chat(&h));
        *hist.lock().unwrap() = h;
    });

    let w = ui.as_weak();
    let hist = history.clone();
    ui.on_chat_new(move || {
        hist.lock().unwrap().clear();
        let _ = std::fs::remove_file(chat_path());
        w.unwrap().set_chat(display_chat(&[]));
    });

    let can = cancel.clone();
    let w = ui.as_weak();
    ui.on_chat_stop(move || {
        can.store(true, std::sync::atomic::Ordering::SeqCst);
        if let Some(tx) = CONFIRM.lock().unwrap().take() { let _ = tx.send(false); }
        w.unwrap().set_confirm_text("".into());
    });

    let w = ui.as_weak();
    ui.on_confirm_action(move |ok| {
        if let Some(tx) = CONFIRM.lock().unwrap().take() { let _ = tx.send(ok); }
        w.unwrap().set_confirm_text("".into());
    });

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_compose_new(move || {
        let Some(c) = st.lock().unwrap().clone() else { return };
        compose::open(&w.unwrap(), c, compose::Kind::New, None);
    });

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_reply(move || {
        let Some(c) = st.lock().unwrap().clone() else { return };
        let Some((m, _)) = CUR_MSG.with(|x| x.borrow().clone()) else { return };
        compose::open(&w.unwrap(), c, compose::Kind::Reply, Some(&m));
    });

    let (w, st, pf) = (ui.as_weak(), state.clone(), prefs.clone());
    ui.on_send_now(move || {
        let Some(c) = st.lock().unwrap().clone() else { return };
        compose::send(&w.unwrap(), c, &pf.lock().unwrap());
    });

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_undo_toast(move || {
        let Some(c) = st.lock().unwrap().clone() else { return };
        compose::undo_send(&w.unwrap(), c);
    });

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_attach_file(move || {
        let Some(c) = st.lock().unwrap().clone() else { return };
        compose::attach(&w.unwrap(), c);
    });

    let w = ui.as_weak();
    ui.on_remove_attachment(move |i| compose::remove_attachment(&w.unwrap(), i));

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_save_draft(move |close| {
        let Some(c) = st.lock().unwrap().clone() else { return };
        compose::save_draft(&w.unwrap(), c, close);
    });

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_discard_draft(move || {
        let Some(c) = st.lock().unwrap().clone() else { return };
        compose::discard(&w.unwrap(), c);
    });

    // ---------------- apps ----------------
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_app_changed(move |app| {
        let Some(c) = st.lock().unwrap().clone() else { return };
        let u = w.unwrap();
        match app {
            1 => { calendar::render(&u); calendar::load(w.clone(), c); }
            2 => contacts::load(w.clone(), c),
            3 => files::open(&u, c, ""),
            4 => admin::load(&u, c, &u.get_admin_section()),
            _ => {}
        }
    });
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_cal_nav(move |how| { let Some(c) = st.lock().unwrap().clone() else { return }; calendar::nav(&w.unwrap(), c, &how); });
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_cal_set_view(move |v| { let Some(c) = st.lock().unwrap().clone() else { return }; calendar::set_view(&w.unwrap(), c, v); });
    let w = ui.as_weak();
    ui.on_cal_toggle(move |id| calendar::toggle_calendar(&w.unwrap(), &id));
    let w = ui.as_weak();
    ui.on_cal_search(move |q| calendar::search(&w.unwrap(), &q));
    let w = ui.as_weak();
    ui.on_cal_edit(move |id, date, hour| calendar::edit(&w.unwrap(), &id, &date, hour));
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_task_add(move |t, d| { let Some(c) = st.lock().unwrap().clone() else { return }; calendar::add_task(&w.unwrap(), c, &t, &d); });
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_task_toggle(move |id| { let Some(c) = st.lock().unwrap().clone() else { return }; calendar::toggle_task(&w.unwrap(), c, &id); });
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_ev_save(move || { let Some(c) = st.lock().unwrap().clone() else { return }; calendar::save(&w.unwrap(), c); });
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_ev_delete(move || { let Some(c) = st.lock().unwrap().clone() else { return }; calendar::delete(&w.unwrap(), c); });

    let w = ui.as_weak();
    ui.on_ct_search(move |q| contacts::search(&w.unwrap(), &q));
    let w = ui.as_weak();
    ui.on_ct_select_group(move |id| contacts::select_group(&w.unwrap(), &id));
    let w = ui.as_weak();
    ui.on_ct_select(move |id| { let u = w.unwrap(); u.set_ct_editing(false); contacts::select(&u, &id); });
    let w = ui.as_weak();
    ui.on_ct_new(move || { let u = w.unwrap(); contacts::select(&u, ""); u.set_ct_id("".into()); u.set_ct_editing(true); });
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_ct_save(move || { let Some(c) = st.lock().unwrap().clone() else { return }; contacts::save(&w.unwrap(), c); });
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_ct_delete(move || { let Some(c) = st.lock().unwrap().clone() else { return }; contacts::delete(&w.unwrap(), c); });
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_ct_email(move || {
        let Some(c) = st.lock().unwrap().clone() else { return };
        let u = w.unwrap();
        // a group mails every member; a contact its first address
        let to = if u.get_ct_group() != "" && u.get_ct_id() == "" { u.get_ct_emails().to_string() }
                 else { u.get_ct_emails().split(',').next().unwrap_or("").trim().to_string() };
        compose::open(&u, c, compose::Kind::New, None);
        u.set_c_to(to.into());
    });
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_fl_open(move |id| { let Some(c) = st.lock().unwrap().clone() else { return }; files::open(&w.unwrap(), c, &id); });
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_fl_activate(move |id| { let Some(c) = st.lock().unwrap().clone() else { return }; files::activate(&w.unwrap(), c, &id); });
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_fl_download(move |id| { let Some(c) = st.lock().unwrap().clone() else { return }; files::download(&w.unwrap(), c, &id); });
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_fl_upload(move || { let Some(c) = st.lock().unwrap().clone() else { return }; files::upload(&w.unwrap(), c); });
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_fl_new_folder(move |n| { let Some(c) = st.lock().unwrap().clone() else { return }; files::new_folder(&w.unwrap(), c, &n); });
    let w = ui.as_weak();
    ui.on_fl_start_rename(move |id| files::start_rename(&w.unwrap(), &id));
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_fl_rename(move |id, n| { let Some(c) = st.lock().unwrap().clone() else { return }; files::rename(&w.unwrap(), c, &id, &n); });
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_fl_delete(move |id| { let Some(c) = st.lock().unwrap().clone() else { return }; files::delete(&w.unwrap(), c, &id); });

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_admin_load(move |sec| { let Some(c) = st.lock().unwrap().clone() else { return }; admin::load(&w.unwrap(), c, &sec); });
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_admin_delete(move |id| { let Some(c) = st.lock().unwrap().clone() else { return }; admin::delete_queued(&w.unwrap(), c, &id); });
    ui.set_admin_enabled(!admin::key().is_empty());

    // ---------------- keyboard shortcuts (Bulwark's single-key set) ----------------
    let (w, st, pf) = (ui.as_weak(), state.clone(), prefs.clone());
    ui.on_shortcut(move |text, shift, ctrl| {
        let u = w.unwrap();
        let t = text.as_str();
        // composer shortcuts
        if u.get_composing() {
            if ctrl && (t == "\r" || t == "\n") { u.invoke_send_now(); }
            if ctrl && t == "s" { u.invoke_save_draft(false); }
            if t == "\u{1b}" { u.invoke_save_draft(true); }
            return;
        }
        if ctrl {
            match t {
                "1" | "2" | "3" | "4" => { let a = t.parse::<i32>().unwrap() - 1; u.set_app(a); u.invoke_app_changed(a); }
                "n" => u.invoke_compose_new(),
                "a" => u.invoke_select_all(),
                "r" => u.invoke_act(if shift { "reply-all".into() } else { "reply".into() }),
                "u" => u.invoke_act("unread".into()),
                _ => {}
            }
            return;
        }
        if u.get_app() != 0 || u.get_show_settings() { if t == "\u{1b}" { u.set_show_settings(false); } return; }
        let move_sel = |delta: i32, unread_only: bool| {
            let m = u.get_mails();
            let cur = u.get_current_mail();
            let n = m.row_count() as i32;
            let mut i = (0..n).find(|&i| m.row_data(i as usize).is_some_and(|x| x.id == cur)).unwrap_or(-1);
            loop {
                i += delta;
                if i < 0 || i >= n { return; }
                let r = m.row_data(i as usize).unwrap();
                if !unread_only || r.unread { u.set_current_mail(r.id.clone()); u.invoke_open_mail(r.id); return; }
            }
        };
        let _ = (&st, &pf);
        match t {
            "j" => move_sel(1, false),
            "k" => move_sel(-1, false),
            "\u{F701}" => move_sel(1, false),   // down arrow
            "\u{F700}" => move_sel(-1, false),  // up arrow
            "n" => move_sel(1, true),
            "\u{1b}" => u.set_current_mail("".into()),
            "r" => u.invoke_act("reply".into()),
            "R" => u.invoke_act("reply-all".into()),
            "f" => u.invoke_act("forward".into()),
            "s" => u.invoke_act("star".into()),
            "e" => u.invoke_act("archive".into()),
            "#" | "\u{7f}" => u.invoke_act("delete".into()),
            "u" => u.invoke_act("unread".into()),
            "I" => u.invoke_act("read".into()),
            "!" => u.invoke_act("spam".into()),
            "c" => u.invoke_compose_new(),
            "/" => u.invoke_focus_search(),
            "G" => u.invoke_refresh(),
            "z" => u.invoke_undo_toast(),
            _ => { let _ = shift; }
        }
    });

    let (w, st, pf) = (ui.as_weak(), state.clone(), prefs.clone());
    ui.on_quick_reply(move |text| {
        let Some(c) = st.lock().unwrap().clone() else { return };
        let Some((m, _)) = CUR_MSG.with(|x| x.borrow().clone()) else { return };
        let u = w.unwrap();
        if text.trim().is_empty() { compose::open(&u, c, compose::Kind::Reply, Some(&m)); return; }
        compose::open(&u, c.clone(), compose::Kind::Reply, Some(&m));
        let body = format!("{}{}", text, u.get_c_body());
        u.set_c_body(body.into());
        u.set_composing(false);
        compose::send(&u, c, &pf.lock().unwrap());
        u.set_quick_text("".into());
    });

    // ---------------- search filters (Bulwark SearchFilters) ----------------
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_sf_apply(move || {
        let Some(c) = st.lock().unwrap().clone() else { return };
        let u = w.unwrap();
        let mut conds: Vec<serde_json::Value> = vec![];
        let q = u.get_search().to_string();
        if !q.trim().is_empty() { conds.push(json!({"text": q.trim()})); }
        for (v, k) in [(u.get_sf_from(), "from"), (u.get_sf_to(), "to"), (u.get_sf_subject(), "subject"), (u.get_sf_body(), "body")] {
            if !v.trim().is_empty() { conds.push(json!({ k: v.trim() })); }
        }
        let day = |s: &str| if s.trim().len() == 10 { Some(format!("{}T00:00:00Z", s.trim())) } else { None };
        if let Some(d) = day(&u.get_sf_after()) { conds.push(json!({"after": d})); }
        if let Some(d) = day(&u.get_sf_before()) { conds.push(json!({"before": d})); }
        if let Ok(n) = u.get_sf_min().trim().parse::<u64>() { conds.push(json!({"minSize": n * 1024})); }
        if let Ok(n) = u.get_sf_max().trim().parse::<u64>() { conds.push(json!({"maxSize": n * 1024})); }
        if u.get_sf_unread() { conds.push(json!({"notKeyword": "$seen"})); }
        if u.get_sf_starred() { conds.push(json!({"hasKeyword": "$flagged"})); }
        if u.get_sf_attach() { conds.push(json!({"hasAttachment": true})); }
        let active = !conds.is_empty();
        u.set_sf_active(active);
        u.set_sf_open(false);
        u.set_current_mail("".into());
        if !active { u.invoke_select_folder(u.get_current_folder()); return; }
        // scope like Bulwark's searchDefaultScope: everywhere but Spam/Trash, or the open folder
        let scope = settings::Settings::load().str("searchDefaultScope", "all");
        let (w2, c2) = (w.clone(), c.clone());
        let folder = u.get_current_folder().to_string();
        std::thread::spawn(move || {
            let mut conds = conds;
            if scope == "current" || scope == "folder" {
                conds.push(json!({"inMailbox": folder}));
            } else if let Ok(roles) = c2.role_ids() {
                let skip: Vec<String> = ["junk", "trash"].iter().filter_map(|r| roles.get(*r).cloned()).collect();
                if !skip.is_empty() { conds.push(json!({"inMailboxOtherThan": skip})); }
            }
            let f = json!({"operator": "AND", "conditions": conds});
            mail::load_list(w2, c2, f);
        });
    });
    let w = ui.as_weak();
    ui.on_sf_clear(move || {
        let u = w.unwrap();
        for f in [AppWindow::set_sf_from, AppWindow::set_sf_to, AppWindow::set_sf_subject, AppWindow::set_sf_body,
                  AppWindow::set_sf_after, AppWindow::set_sf_before, AppWindow::set_sf_min, AppWindow::set_sf_max] { f(&u, "".into()); }
        u.set_sf_unread(false); u.set_sf_starred(false); u.set_sf_attach(false);
        u.set_sf_active(false);
        u.set_sf_open(false);
        u.invoke_select_folder(u.get_current_folder());
    });

    // templates in the composer
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_tpl_refresh(move || {
        let Some(c) = st.lock().unwrap().clone() else { return };
        let w = w.clone();
        std::thread::spawn(move || {
            let list = templates::list(&c).unwrap_or_default();
            let _ = w.upgrade_in_event_loop(move |u| {
                TEMPLATES.with(|t| *t.borrow_mut() = list.clone());
                let mut names: Vec<slint::SharedString> = vec!["Insert template…".into()];
                names.extend(list.iter().map(|t| t["name"].as_str().unwrap_or("(unnamed)").into()));
                u.set_tpl_names(ModelRc::new(VecModel::from(names)));
                u.set_tpl_index(0);
            });
        });
    });
    let w = ui.as_weak();
    ui.on_tpl_insert(move |i| {
        let u = w.unwrap();
        if i <= 0 { return; }
        let Some(t) = TEMPLATES.with(|x| x.borrow().get(i as usize - 1).cloned()) else { return };
        if u.get_c_subject().trim().is_empty() { u.set_c_subject(t["subject"].as_str().unwrap_or("").into()); }
        let body = t["body"].as_str().unwrap_or("");
        let body = if t["isHTML"] == true { html_to_text(body) } else { body.to_string() };
        u.set_c_body(format!("{body}{}", u.get_c_body()).into());
        u.set_tpl_index(0);
    });

    // ---------------- mail actions ----------------
    let w = ui.as_weak();
    ui.on_toggle_folder(move |id| mail::toggle_folder(&w.unwrap(), &id));

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_select_tag(move |id| {
        let Some(c) = st.lock().unwrap().clone() else { return };
        let u = w.unwrap();
        u.set_current_mail("".into());
        mail::load_list(w.clone(), c, json!({"hasKeyword": format!("$label:{id}")}));
    });

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_row_action(move |id, a| {
        let Some(c) = st.lock().unwrap().clone() else { return };
        mail::apply(&w.unwrap(), c, vec![id.to_string()], &a);
    });

    let w = ui.as_weak();
    ui.on_toggle_check(move |id| mail::toggle_check(&w.unwrap(), &id));
    let w = ui.as_weak();
    ui.on_select_all(move || mail::select_all(&w.unwrap()));

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_bulk(move |a| {
        let u = w.unwrap();
        if a == "clear" { mail::select_all(&u); return; }
        let Some(c) = st.lock().unwrap().clone() else { return };
        mail::apply(&u, c, mail::checked_ids(), &a);
    });

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_load_more(move || {
        let Some(c) = st.lock().unwrap().clone() else { return };
        mail::load_more(w.clone(), c);
    });

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_move_to(move |fid| {
        let Some(c) = st.lock().unwrap().clone() else { return };
        let u = w.unwrap();
        let ids = { let ch = mail::checked_ids(); if ch.is_empty() { vec![u.get_current_mail().to_string()] } else { ch } };
        mail::apply(&u, c, ids.into_iter().filter(|i| !i.is_empty()).collect(), &format!("move:{fid}"));
    });

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_tag_toggle(move |tid| {
        let Some(c) = st.lock().unwrap().clone() else { return };
        let u = w.unwrap();
        let id = u.get_current_mail().to_string();
        let ids = { let ch = mail::checked_ids(); if ch.is_empty() { vec![id.clone()] } else { ch } };
        if ids.iter().all(|i| i.is_empty()) { return; }
        let has = mail::email(&ids[0]).is_some_and(|m| m["keywords"][format!("$label:{tid}")].as_bool() == Some(true));
        mail::apply(&u, c, ids, &format!("{}:{tid}", if has { "untag" } else { "tag" }));
    });

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_act(move |a| {
        let u = w.unwrap();
        let id = u.get_current_mail().to_string();
        if id.is_empty() { return; }
        match a.as_str() {
            "reply" => u.invoke_reply(),
            "reply-all" | "forward" => {
                let Some(c) = st.lock().unwrap().clone() else { return };
                let Some((msg, _)) = CUR_MSG.with(|x| x.borrow().clone()) else { return };
                let kind = if a == "forward" { compose::Kind::Forward } else { compose::Kind::ReplyAll };
                compose::open(&u, c, kind, Some(&msg));
            }
            "print" => {
                let printed = u.get_render_mode() == 0 && WV.with(|v| v.borrow().as_ref().map(|v| v.print().is_ok()).unwrap_or(false));
                if !printed {
                    if let Some(h) = CUR.with(|c| c.borrow().as_ref().map(|c| c.html.clone().unwrap_or_else(|| format!("<pre>{}</pre>", esc(&c.text))))) {
                        let p = std::env::temp_dir().join("barbican-print.html");
                        let page = wrap_html_opts(&format!("<h2>{}</h2>{h}<script>print()</script>", esc(&u.get_r_subject())), false)
                            .replace("script-src 'none'", "script-src 'unsafe-inline'");
                        let _ = std::fs::write(&p, page);
                        oauth::open_browser(&p.to_string_lossy());
                    }
                }
            }
            other => {
                let Some(c) = st.lock().unwrap().clone() else { return };
                if other == "star" { u.set_r_flagged(!u.get_r_flagged()); }
                mail::apply(&u, c, vec![id], other);
            }
        }
    });

    let st = state.clone();
    ui.on_open_attachment(move |i| {
        let Some(c) = st.lock().unwrap().clone() else { return };
        let Some(a) = CUR_MSG.with(|x| x.borrow().as_ref().and_then(|(_, atts)| atts.get(i as usize).cloned())) else { return };
        std::thread::spawn(move || {
            let name = sanitize_name(a["name"].as_str().unwrap_or("attachment"));
            if let Ok(bytes) = c.download(a["blobId"].as_str().unwrap_or(""), &name, a["type"].as_str().unwrap_or("")) {
                let dir = std::env::temp_dir().join("Barbican");
                let _ = std::fs::create_dir_all(&dir);
                let p = dir.join(&name);
                if std::fs::write(&p, bytes).is_ok() { oauth::open_browser(&p.to_string_lossy()); }
            }
        });
    });

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_save_attachment(move |i| {
        let Some(c) = st.lock().unwrap().clone() else { return };
        let Some(a) = CUR_MSG.with(|x| x.borrow().as_ref().and_then(|(_, atts)| atts.get(i as usize).cloned())) else { return };
        let name = sanitize_name(a["name"].as_str().unwrap_or("attachment"));
        let Some(path) = rfd::FileDialog::new().set_file_name(&name).save_file() else { return };
        let w = w.clone();
        std::thread::spawn(move || {
            let res = c.download(a["blobId"].as_str().unwrap_or(""), &name, a["type"].as_str().unwrap_or(""))
                .and_then(|b| std::fs::write(&path, b).map_err(|e| e.to_string()));
            let msg = match res { Ok(()) => format!("Saved {name}"), Err(e) => format!("Could not save: {e}") };
            let _ = w.upgrade_in_event_loop(move |u| mail::show_toast(&u, &msg, false));
        });
    });

    ui.show()?;
    let w = ui.as_weak();
    let timer = slint::Timer::default();
    timer.start(slint::TimerMode::Repeated, std::time::Duration::from_millis(50), move || {
        if let Some(u) = w.upgrade() { sync_webview(&u); }
    });
    slint::run_event_loop()?;
    // refresh tokens may rotate while the app runs: keep the newest one
    if REMEMBER_OAUTH.load(std::sync::atomic::Ordering::SeqCst) {
        if let Some(c) = state.lock().unwrap().clone() { remember_session(&c, serde_json::json!({"kind": "oauth"}), &c.base, ""); }
    }
    ui.hide()
}


// ---------------- assistant helpers ----------------
static CONFIRM: Mutex<Option<std::sync::mpsc::Sender<bool>>> = Mutex::new(None);

struct UiHost {
    w: slint::Weak<AppWindow>,
    client: Arc<jmap::Client>,
    cancel: Arc<std::sync::atomic::AtomicBool>,
}

impl agent::Host for UiHost {
    fn progress(&self, line: String) {
        let _ = self.w.upgrade_in_event_loop(move |u| push_chat(&u, "tool", &line));
    }
    fn confirm(&self, what: String) -> bool {
        let (tx, rx) = std::sync::mpsc::channel();
        *CONFIRM.lock().unwrap() = Some(tx);
        let _ = self.w.upgrade_in_event_loop(move |u| { u.set_confirm_text(what.into()); u.set_chat_open(true); });
        let ok = rx.recv().unwrap_or(false);
        let _ = self.w.upgrade_in_event_loop(|u| u.set_confirm_text("".into()));
        ok
    }
    fn open_draft(&self, to: String, subject: String, body: String) {
        let _ = self.w.upgrade_in_event_loop(move |u| {
            u.set_c_to(to.into());
            u.set_c_subject(subject.into());
            u.set_c_body(body.into());
            u.set_c_status("".into());
            u.set_can_undo(false);
            u.set_composing(true);
        });
    }
    fn changed(&self) {
        let (w, c) = (self.w.clone(), self.client.clone());
        load_mailboxes(w.clone(), c.clone(), false);
        let _ = self.w.upgrade_in_event_loop(move |u| {
            let f = u.get_current_folder().to_string();
            if !f.is_empty() { load_folder(w, c, Some(f), None); }
        });
    }
    fn cancelled(&self) -> bool {
        self.cancel.load(std::sync::atomic::Ordering::SeqCst)
    }
}

const QUICK: [&str; 6] = ["Catch me up", "Summarize this email", "Summarize this conversation", "To-dos and dates", "Reply ideas", "Triage my inbox"];

fn chips_model(c: &ai::Config) -> ModelRc<SharedString> {
    let mut v: Vec<SharedString> = QUICK.iter().map(|s| SharedString::from(*s)).collect();
    v.extend(c.saved_prompts().into_iter().map(|(n, _)| SharedString::from(n)));
    ModelRc::new(VecModel::from(v))
}

fn expand_prompt(text: &str, c: &ai::Config, calendar: bool) -> String {
    let key = text.trim_start_matches('/').trim();
    let norm = |s: &str| s.to_lowercase().chars().filter(|c| c.is_alphanumeric()).collect::<String>();
    if let Some((_, t)) = c.saved_prompts().into_iter().find(|(n, _)| norm(n) == norm(key)) { return t; }
    let q = QUICK.iter().find(|n| norm(n) == norm(key) || (text.starts_with('/') && norm(n).starts_with(&norm(key)) && !key.is_empty()));
    match q.copied() {
        Some("Catch me up") => format!("Catch me up. Look at my unread inbox mail from the last 3 days{}. Then give me a short, prioritized briefing: what needs a reply or action first (with deadlines), what is FYI, and what can be ignored.",
            if calendar { " and my calendar for the next 3 days" } else { "" }),
        Some("Summarize this email") => "Summarize the email I have open in 3 to 5 bullet points.".into(),
        Some("Summarize this conversation") => "Read the whole conversation of the email I have open and summarize it: who said what, decisions made, and open questions.".into(),
        Some("To-dos and dates") => "List the action items, deadlines and dates in the email I have open.".into(),
        Some("Reply ideas") => "Suggest three short, distinct replies to the email I have open, numbered 1 to 3.".into(),
        Some("Triage my inbox") => "Look through my latest 50 inbox messages. Group them into: needs a reply, FYI, and newsletters or notifications. Then offer to archive the newsletters and notifications.".into(),
        _ => text.to_string(),
    }
}

fn push_chat(u: &AppWindow, role: &str, text: &str) {
    let m = u.get_chat();
    if let Some(vm) = m.as_any().downcast_ref::<VecModel<ChatMsg>>() {
        vm.push(ChatMsg { role: role.into(), text: text.into() });
    }
    let w = u.as_weak();
    slint::Timer::single_shot(std::time::Duration::from_millis(60), move || {
        if let Some(u) = w.upgrade() { u.invoke_scroll_chat(); }
    });
}

fn display_chat(h: &[agent::Msg]) -> ModelRc<ChatMsg> {
    let mut v = vec![];
    for m in h {
        match m {
            agent::Msg::User(t) => v.push(ChatMsg { role: "user".into(), text: t.as_str().into() }),
            agent::Msg::Assistant { text, calls } => {
                if !text.trim().is_empty() { v.push(ChatMsg { role: "assistant".into(), text: text.trim().into() }); }
                for c in calls { v.push(ChatMsg { role: "tool".into(), text: format!("{} {}", agent::icon(&c.name), agent::label(&c.name, &c.args)).into() }); }
            }
            agent::Msg::Results(_) => {}
        }
    }
    ModelRc::new(VecModel::from(v))
}


// ---------------- sign-in helpers ----------------
static REMEMBER_OAUTH: std::sync::atomic::AtomicBool = std::sync::atomic::AtomicBool::new(false);

fn remember_session(c: &jmap::Client, mut v: Value, server: &str, user: &str) {
    v["server"] = serde_json::json!(server);
    v["user"] = serde_json::json!(user);
    if v["kind"] == "oauth" {
        let Some(t) = c.tokens() else { return };
        if t.refresh.is_none() { return; }
        v["refresh"] = serde_json::json!(t.refresh);
        v["endpoint"] = serde_json::json!(t.token_endpoint);
        v["client"] = serde_json::json!(t.client_id);
        REMEMBER_OAUTH.store(true, std::sync::atomic::Ordering::SeqCst);
    }
    oauth::remember(&v);
}

fn finish_login(w: slint::Weak<AppWindow>, st: Shared, res: Result<jmap::Client, String>, server: String, user: String, remember: Option<Value>) {
    match res {
        Ok(c) => {
            let c = Arc::new(c);
            *st.lock().unwrap() = Some(c.clone());
            let p = config_path();
            let _ = std::fs::create_dir_all(p.parent().unwrap());
            let _ = std::fs::write(&p, format!("{server}\n{user}\n"));
            if let Some(v) = remember { remember_session(&c, v, &server, &user); }
            let name = c.username.clone();
            *CHAT_USER.lock().unwrap() = name.clone();
            let w2 = w.clone();
            let _ = w.upgrade_in_event_loop(move |u| {
                u.invoke_chat_reload();
                u.set_busy(false);
                u.set_status("".into());
                u.set_account(name.clone().into());
                u.set_initials(initials(&name).into());
                u.set_logged_in(true);
            });
            load_mailboxes(w2.clone(), c.clone(), true);
            {
                let (w3, c3) = (w2.clone(), c.clone());
                std::thread::spawn(move || {
                    let addrs: Vec<String> = c3.identities().unwrap_or_default().iter().filter_map(|i| i["email"].as_str().map(String::from)).collect();
                    let _ = w3.upgrade_in_event_loop(move |_| MY_ADDRS.with(|a| *a.borrow_mut() = addrs));
                });
            }
            check_new_mail(c.clone());
            start_push(w2, c);
        }
        Err(e) => {
            let _ = w.upgrade_in_event_loop(move |u| {
                u.set_busy(false);
                u.set_status(e.into());
            });
        }
    }
}


/// Bulwark's Appearance > Light or dark: "light" | "dark" | "system" (Windows app theme).
pub fn apply_theme(ui: &AppWindow) {
    let pref = settings::Settings::load().str("theme", "dark");
    let dark = match pref.as_str() {
        "light" => false,
        "system" => {
            // HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize AppsUseLightTheme
            let out = std::process::Command::new("reg")
                .args(["query", r"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "/v", "AppsUseLightTheme"])
                .output().ok().map(|o| String::from_utf8_lossy(&o.stdout).to_string()).unwrap_or_default();
            !out.contains("0x1")
        }
        _ => true,
    };
    ui.global::<Theme>().set_dark(dark);
}


static CHAT_USER: Mutex<String> = Mutex::new(String::new());

/// The assistant's chat history is kept per account (chat-<account>.json).
fn chat_path() -> std::path::PathBuf {
    let u = CHAT_USER.lock().unwrap().clone();
    let safe: String = u.chars().map(|c| if c.is_alphanumeric() || c == '.' || c == '@' || c == '-' { c } else { '_' }).collect();
    config_dir().join(if safe.is_empty() { "chat.json".to_string() } else { format!("chat-{safe}.json") })
}
