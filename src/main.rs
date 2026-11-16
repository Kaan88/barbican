#![cfg_attr(not(test), windows_subsystem = "windows")]

mod agent;
mod ai;
mod jmap;
mod lite;
mod oauth;
mod settings;
mod spam;

use chrono::{DateTime, Datelike, Local};
use serde_json::Value;
use slint::{ComponentHandle, Color, Model, ModelRc, SharedString, VecModel};
use std::collections::HashMap;
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
                Ok(v) => *w = Some(v),
                Err(_) => {}
            }
        } else if let Some(v) = &*w {
            let _ = v.set_bounds(r);
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

fn email_for_ai() -> Option<String> {
    REPLY.with(|r| r.borrow().as_ref().map(|(_, subj, _, text, who)| format!("From: {who}\nSubject: {subj}\n\n{text}")))
}

fn initials(s: &str) -> String {
    let letters: String = s.chars().filter(|c| c.is_alphanumeric()).take(2).collect();
    if letters.is_empty() { "?".into() } else { letters.to_uppercase() }
}

fn tint(s: &str) -> Color {
    const P: [u32; 6] = [0xe07a10, 0x22a55a, 0x6366f1, 0x84b31a, 0xd9467a, 0x0ea5b7];
    let h = s.bytes().fold(7u32, |a, b| a.wrapping_mul(31).wrapping_add(b as u32));
    Color::from_argb_encoded(0xff000000 | P[(h as usize) % P.len()])
}

fn role_icon(role: &str) -> &'static str {
    match role {
        "inbox" => "\u{E715}",
        "drafts" => "\u{E70B}",
        "sent" => "\u{E724}",
        "archive" => "\u{E7B8}",
        "junk" => "\u{E8F8}",
        "trash" => "\u{E74D}",
        _ => "\u{E8B7}",
    }
}

fn role_rank(role: &str) -> i64 {
    match role {
        "inbox" => 0, "drafts" => 1, "sent" => 2, "archive" => 3, "junk" => 4, "trash" => 5, _ => 10,
    }
}

fn build_folders(list: &[Value]) -> Vec<Folder> {
    let mut children: HashMap<String, Vec<&Value>> = HashMap::new();
    for m in list {
        let p = m["parentId"].as_str().unwrap_or("").to_string();
        children.entry(p).or_default().push(m);
    }
    for v in children.values_mut() {
        v.sort_by_key(|m| {
            (
                role_rank(m["role"].as_str().unwrap_or("")),
                m["sortOrder"].as_i64().unwrap_or(0),
                m["name"].as_str().unwrap_or("").to_lowercase(),
            )
        });
    }
    fn walk(p: &str, d: i32, ch: &HashMap<String, Vec<&Value>>, out: &mut Vec<Folder>) {
        if let Some(kids) = ch.get(p) {
            for m in kids {
                let id = m["id"].as_str().unwrap_or("");
                out.push(Folder {
                    id: id.into(),
                    name: m["name"].as_str().unwrap_or("").into(),
                    icon: role_icon(m["role"].as_str().unwrap_or("")).into(),
                    depth: d,
                    unread: m["unreadEmails"].as_i64().unwrap_or(0) as i32,
                    total: m["totalEmails"].as_i64().unwrap_or(0) as i32,
                });
                walk(id, d + 1, ch, out);
            }
        }
    }
    let mut out = vec![];
    walk("", 0, &children, &mut out);
    out
}

fn group_label(t: &DateTime<Local>) -> String {
    let today = Local::now().date_naive();
    let days = (today - t.date_naive()).num_days();
    match days {
        0 => "Today".into(),
        1 => "Yesterday".into(),
        2..=6 => t.format("%A").to_string(),
        _ if t.year() == today.year() => t.format("%B").to_string(),
        _ => t.format("%B %Y").to_string(),
    }
}

fn time_label(t: &DateTime<Local>) -> String {
    let days = (Local::now().date_naive() - t.date_naive()).num_days();
    match days {
        0 => t.format("%H:%M").to_string(),
        1..=6 => t.format("%a %H:%M").to_string(),
        _ => t.format("%d %b").to_string(),
    }
}

fn parse_time(v: &Value) -> Option<DateTime<Local>> {
    DateTime::parse_from_rfc3339(v.as_str()?).ok().map(|d| d.with_timezone(&Local))
}

fn sender(v: &Value) -> String {
    let f = &v["from"][0];
    f["name"].as_str().filter(|s| !s.is_empty()).or(f["email"].as_str()).unwrap_or("(unknown)").to_string()
}

fn build_mails(list: &[Value]) -> Vec<Mail> {
    let mut last = String::new();
    list.iter()
        .map(|m| {
            let t = parse_time(&m["receivedAt"]);
            let g = t.as_ref().map(group_label).unwrap_or_default();
            let header = if g != last { last = g.clone(); g } else { String::new() };
            let s = sender(m);
            Mail {
                id: m["id"].as_str().unwrap_or("").into(),
                initials: initials(&s).into(),
                color: tint(&s),
                sender: s.into(),
                subject: m["subject"].as_str().filter(|s| !s.is_empty()).unwrap_or("(no subject)").into(),
                preview: m["preview"].as_str().unwrap_or("No preview available").replace('\n', " ").into(),
                time: t.as_ref().map(time_label).unwrap_or_default().into(),
                unread: m["keywords"]["$seen"].as_bool() != Some(true),
                attach: m["hasAttachment"].as_bool().unwrap_or(false),
                header: header.into(),
            }
        })
        .collect()
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
    let _ = ui.upgrade_in_event_loop(|u| u.set_busy(true));
    std::thread::spawn(move || {
        let res = client.emails(mailbox.as_deref(), text.as_deref());
        let _ = ui.upgrade_in_event_loop(move |u| {
            u.set_busy(false);
            match res {
                Ok(list) => u.set_mails(ModelRc::new(VecModel::from(build_mails(&list)))),
                Err(e) => u.set_status(e.into()),
            }
        });
    });
}

fn load_mailboxes(ui: slint::Weak<AppWindow>, client: Arc<jmap::Client>, select_inbox: bool) {
    std::thread::spawn(move || {
        let Ok(list) = client.mailboxes() else { return };
        let inbox = list.iter().find(|m| m["role"] == "inbox").and_then(|m| m["id"].as_str()).map(String::from);
        let folders = build_folders(&list);
        let ui2 = ui.clone();
        let _ = ui.upgrade_in_event_loop(move |u| {
            u.set_folders(ModelRc::new(VecModel::from(folders)));
            if select_inbox {
                if let Some(id) = inbox {
                    u.set_current_folder(id.clone().into());
                    load_folder(ui2, client, Some(id), None);
                }
            }
        });
    });
}

fn main() -> Result<(), slint::PlatformError> {
    let ui = AppWindow::new()?;
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
        // mark read locally
        let model = u.get_mails();
        for i in 0..model.row_count() {
            if let Some(mut m) = model.row_data(i) {
                if m.id == id {
                    if m.unread { m.unread = false; model.set_row_data(i, m); }
                    break;
                }
            }
        }
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
            let res = c.email_body(&id);
            let c2 = c.clone();
            let w2 = w.clone();
            let _ = w.upgrade_in_event_loop(move |u| {
                if u.get_current_mail().as_str() != id { return; }
                match res {
                    Ok(m) => {
                        let f = &m["from"][0];
                        let from = match (f["name"].as_str(), f["email"].as_str()) {
                            (Some(n), Some(e)) if !n.is_empty() => format!("{n} <{e}>"),
                            (_, Some(e)) => e.to_string(),
                            _ => "(unknown)".into(),
                        };
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
                        let headers = m["headers"].as_array().cloned().unwrap_or_default();
                        if let Some(sp) = spam::analyze(&headers) {
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

    let w = ui.as_weak();
    let cf = cfg.clone();
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
    // rebuild the current settings page (or search results)
    let refresh_settings = {
        let (w, pf, cf, acc) = (ui.as_weak(), prefs.clone(), cfg.clone(), account_info.clone());
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
                p.rows(&tab, &c, &acc())
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

    let (w, rs) = (ui.as_weak(), refresh_settings.clone());
    ui.on_open_settings(move || {
        let u = w.unwrap();
        u.set_settings_query("".into());
        rs();
        u.set_show_settings(true);
    });

    let (w, rs) = (ui.as_weak(), refresh_settings.clone());
    ui.on_settings_open_tab(move |id| {
        let u = w.unwrap();
        u.set_settings_query("".into());
        u.set_settings_tab(id);
        rs();
    });

    let rs = refresh_settings.clone();
    ui.on_settings_search(move |_| rs());

    let (pf, cf, rs) = (prefs.clone(), cfg.clone(), refresh_settings.clone());
    ui.on_settings_set_toggle(move |key, on| {
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
    });

    // text edits are saved as typed, without rebuilding the page (keeps the caret)
    let (pf, cf) = (prefs.clone(), cfg.clone());
    ui.on_settings_set_text(move |key, t| {
        pf.lock().unwrap().set(&key, serde_json::json!(t.to_string()), &mut cf.lock().unwrap());
    });

    let w = ui.as_weak();
    ui.on_settings_button(move |key| {
        let u = w.unwrap();
        if key == "signout" { u.set_show_settings(false); u.invoke_sign_out(); }
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
        std::fs::read_to_string(config_dir().join("chat.json")).ok()
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
            let _ = std::fs::write(config_dir().join("chat.json"), agent::to_json(&h).to_string());
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

    let w = ui.as_weak();
    let hist = history.clone();
    ui.on_chat_new(move || {
        hist.lock().unwrap().clear();
        let _ = std::fs::remove_file(config_dir().join("chat.json"));
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

    let w = ui.as_weak();
    ui.on_compose_new(move || {
        let u = w.unwrap();
        REPLY.with(|r| *r.borrow_mut() = None);
        u.set_can_undo(false); u.set_c_to("".into()); u.set_c_subject("".into()); u.set_c_body("".into()); u.set_c_status("".into());
        u.set_composing(true);
    });

    let w = ui.as_weak();
    ui.on_reply(move || {
        let u = w.unwrap();
        let Some((to, subj, _, text, who)) = REPLY.with(|r| r.borrow().clone()) else { return };
        let subj = if subj.to_lowercase().starts_with("re:") { subj } else { format!("Re: {subj}") };
        let quoted: String = text.lines().map(|l| format!("> {l}
")).collect();
        u.set_c_to(to.into());
        u.set_c_subject(subj.into());
        u.set_c_body(format!("

On {who} wrote:
{quoted}").into());
        u.set_c_status("".into());
        u.set_can_undo(false);
        u.set_composing(true);
    });

    let w = ui.as_weak();
    let st = state.clone();
    ui.on_send_mail(move |to, subject, body| {
        let Some(c) = st.lock().unwrap().clone() else { return };
        let u = w.unwrap();
        let irt = if u.get_c_subject().starts_with("Re: ") { REPLY.with(|r| r.borrow().as_ref().and_then(|x| x.2.clone())) } else { None };
        u.set_sending(true);
        u.set_c_status("".into());
        let w = w.clone();
        let (to, subject, body) = (to.to_string(), subject.to_string(), body.to_string());
        std::thread::spawn(move || {
            let res = c.send(&to, &subject, &body, irt.as_deref());
            let w2 = w.clone();
            let ok = res.is_ok();
            let _ = w.upgrade_in_event_loop(move |u| {
                u.set_sending(false);
                match res {
                    Ok(()) => u.set_composing(false),
                    Err(e) => u.set_c_status(e.into()),
                }
            });
            if ok { load_mailboxes(w2, c, false); }
        });
    });

    let w = ui.as_weak();
    let st = state.clone();
    ui.on_delete_mail(move || {
        let Some(c) = st.lock().unwrap().clone() else { return };
        let u = w.unwrap();
        let id = u.get_current_mail().to_string();
        if id.is_empty() { return; }
        let model = u.get_mails();
        if let Some(vm) = model.as_any().downcast_ref::<VecModel<Mail>>() {
            if let Some(i) = (0..vm.row_count()).find(|&i| vm.row_data(i).map(|m| m.id == id).unwrap_or(false)) {
                let removed = vm.remove(i);
                if !removed.header.is_empty() {
                    if let Some(mut next) = vm.row_data(i) {
                        if next.header.is_empty() { next.header = removed.header; vm.set_row_data(i, next); }
                    }
                }
            }
        }
        u.set_current_mail("".into());
        let w = w.clone();
        std::thread::spawn(move || {
            let res = c.trash(&id);
            if let Err(e) = res { let _ = w.upgrade_in_event_loop(move |u| u.set_status(e.into())); }
            else { load_mailboxes(w, c, false); }
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
            let w2 = w.clone();
            let _ = w.upgrade_in_event_loop(move |u| {
                u.set_busy(false);
                u.set_status("".into());
                u.set_account(name.clone().into());
                u.set_initials(initials(&name).into());
                u.set_logged_in(true);
            });
            load_mailboxes(w2, c, true);
        }
        Err(e) => {
            let _ = w.upgrade_in_event_loop(move |u| {
                u.set_busy(false);
                u.set_status(e.into());
            });
        }
    }
}
