#![cfg_attr(not(test), windows_subsystem = "windows")]

mod a11y;
mod vacation;
mod winstate;
mod auth;
mod attachments;
mod avatars;
mod bimi;
mod phish;
mod phishflow;
mod quota;
mod crashguard;
mod tags;
mod mailexport;
mod mailimport;
mod tnef;
mod admin;
mod dashboard;
mod automation;
mod otp;
mod agent;
mod ai;
mod bodycache;
mod allday;
mod calendar;
mod cal_editor;
mod cal_extra;
mod feed_guard;
mod recurrence;
mod reminders;
mod capture;
mod compose;
mod contacts;
mod contacts_x;
mod filters_x;
mod contact_merge;
mod vcard;
mod pim;
mod ctxmenu;
mod folders;
mod copymsg;
mod files;
mod glance;
mod jmap;
mod lite;
mod mailview;
mod msgload;
mod rte;
mod mail;
mod unified;
mod oauth;
mod profile;
mod redirect;
mod restore;
mod secrets;
mod plugins;
mod plugin_catalog;
#[cfg(test)]
mod plugin_catalog_check;
mod server_settings;
mod settings;
mod sieve;
mod templates;
mod tplflow;
mod spam;
// composer extras (p2-composer)
mod autosave;
mod chips;
mod instance;
mod mailto;
mod recipients;
mod schedule;
mod dark_mail;
mod invite;
mod mdn;
mod quick_rules;
mod reader;
mod zip;
mod shortcuts;
mod gsearch;
mod palette;
mod search_history;
mod signout;
mod trust;
mod trustbook;
mod search_ui;
mod dnd;
// distribution: platform layer, tray, notifications, updater
mod desktop;
mod icon;
mod notify;
mod platform;
mod updater;
#[cfg(test)]
mod release_manifest;
mod listsync;
mod motion;

use chrono::{DateTime, Local};
use serde_json::{json, Value};
use slint::{ComponentHandle, Model, ModelRc, SharedString, VecModel};
use std::sync::{Arc, Mutex};

slint::include_modules!();

thread_local! {
    pub(crate) static WV: std::cell::RefCell<Option<wry::WebView>> = const { std::cell::RefCell::new(None) };
    static REPLY: std::cell::RefCell<Option<(String, String, Option<String>, String, String)>> = const { std::cell::RefCell::new(None) };
}

/// The reader's CSP; `script` (our own accessibility script) is allowed by its hash only.
fn csp_with(images: bool, script: Option<&str>) -> String {
    use base64::Engine;
    use sha2::Digest;
    let scripts = match script {
        Some(s) => format!("'sha256-{}'", base64::engine::general_purpose::STANDARD.encode(sha2::Sha256::digest(s.as_bytes()))),
        None => "'none'".into(),
    };
    // with remote content blocked, a remote stylesheet or font is a tracking beacon like an image
    let (style, font, img) = if images { ("'unsafe-inline' http: https:", "http: https: data:", "http: https: data:") } else { ("'unsafe-inline'", "data:", "data:") };
    format!(
        "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src {style}; font-src {font}; img-src {img}; script-src {scripts}; object-src 'none'; base-uri 'none'; frame-src 'none'; form-action 'none'\">"
    )
}

#[cfg(test)]
mod csp_tests {
    #[test]
    fn blocked_remote_content_includes_stylesheets_and_fonts() {
        let c = super::csp_with(false, None);
        assert!(c.contains("style-src 'unsafe-inline';") && c.contains("font-src data:;") && c.contains("img-src data:;"), "{c}");
        assert!(c.contains("script-src 'none'") && c.contains("base-uri 'none'"));
        let c = super::csp_with(true, Some("x"));
        assert!(c.contains("style-src 'unsafe-inline' http: https:;") && c.contains("font-src http: https: data:;") && c.contains("img-src http: https: data:;"));
        assert!(c.contains("script-src 'sha256-"));
    }
}

/// The open message's body, kept so it can be re-rendered (mode change, resize, "Load images").
struct Current {
    html: Option<String>,
    text: String,
    remote: bool,
    allow: bool,
    /// Reported as phishing (`$phishing`): its links are turned off (phish.rs).
    links_off: bool,
}

thread_local! {
    static CUR: std::cell::RefCell<Option<Current>> = const { std::cell::RefCell::new(None) };
    static LITE_W: std::cell::Cell<f32> = const { std::cell::Cell::new(0.0) };
}

fn has_remote_images(h: &str) -> bool {
    let l = h.to_lowercase().replace(['"', '\''], "");
    ["src=http", "url(http", "background=http", "srcset=http", "src=//", "url(//"].iter().any(|p| l.contains(p))
}

/// The user's own addresses (identities and login): their domains are the organisation, for the
/// "External" chip and the send confirm alike.
pub fn own_addresses() -> Vec<String> {
    match msgload::SHARED.with(|s| s.borrow().clone()) {
        Some(st) => my_addrs(&st),
        None => my_addrs_cached(),
    }
}

/// The "External" chip for message `m`: any From address is outside the own domains and the
/// setting is on (Bulwark `externalSenders`: a message is external when any of them is).
pub fn message_external(m: &Value) -> bool {
    let label_on = settings::Settings::load().bool(trust::SENDER_LABEL_KEY, true);
    let own = trust::own_domains(&own_addresses());
    m["from"].as_array().is_some_and(|a| a.iter().any(|f| {
        let e = f["email"].as_str().unwrap_or("");
        // an entry with no address at all has nothing to label
        !e.is_empty() && trust::external_sender(e, &own, label_on)
    }))
}

/// Whether the remote content of message `m` loads when it opens: policy `allow`, or a trusted
/// sender (Bulwark email-viewer.tsx:3129).
pub fn allow_remote(u: &AppWindow, m: &Value) -> bool {
    let prefs = settings::Settings::load().prefs;
    let from = m["from"][0]["email"].as_str().unwrap_or("");
    trust::loads_on_open(trust::policy(&prefs, u.get_auto_images()), trustbook::is_trusted(&prefs, from))
}

/// What the "remote images blocked" banner offers for the open message.
fn images_offers(u: &AppWindow) -> trust::Offers {
    let prefs = settings::Settings::load().prefs;
    let (from, forged) = CUR_MSG.with(|x| x.borrow().as_ref().map(|(m, _)| {
        let from = m["from"][0]["email"].as_str().unwrap_or("").to_string();
        let forged = trust::forged(&reader::open_trust(m), &from);
        (from, forged)
    }).unwrap_or_default());
    trust::offers(trust::policy(&prefs, u.get_auto_images()), &from, trustbook::is_trusted(&prefs, &from), forged)
}

/// The policy or the trusted list changed: decide again whether the open message's remote content loads.
fn redecide_remote(u: &AppWindow) {
    let Some(m) = CUR_MSG.with(|x| x.borrow().as_ref().map(|(m, _)| m.clone())) else { return };
    let decided = allow_remote(u, &m);
    // under Ask a one-off "Load images" grant for this message stays
    let ask = trust::policy(&settings::Settings::load().prefs, u.get_auto_images()) == trust::ContentPolicy::Ask;
    let changed = CUR.with(|c| c.borrow_mut().as_mut().is_some_and(|c| { let was = c.allow; c.allow = decided || (ask && was); c.remote && was != c.allow }));
    if changed { render_current(u); }
}

fn render_current(u: &AppWindow) {
    render_current_then(u, None);
}

/// The open message was just reported as phishing: draw it again with its links off.
pub(crate) fn links_off_now(u: &AppWindow) {
    CUR.with(|c| if let Some(c) = c.borrow_mut().as_mut() { c.links_off = true; });
    render_current(u);
}

/// Render the open message's body; `on_ready` runs when it is about to appear (with the
/// WebView: once the new page is laid out, right before it is swapped in).
/// `on_ready` returning false means the page is no longer wanted (another message was opened
/// meanwhile): it is dropped instead of revealed.
fn render_current_then(u: &AppWindow, on_ready: Option<Box<dyn FnOnce() -> bool>>) {
    let mode = u.get_render_mode();
    let mut on_ready = on_ready;
    CUR.with(|c| {
        let c = c.borrow();
        let Some(cur) = c.as_ref() else { return };
        u.set_images_blocked(cur.remote && !cur.allow && mode != 2);
        // every renderer below reads this: the page (links lose their address), the navigation
        // handlers and the light renderer's click
        mailview::set_links_off(cur.links_off);
        if cur.remote && !cur.allow && mode != 2 {
            let o = images_offers(u);
            u.set_images_can_load(o.load_once);
            u.set_images_can_trust(o.trust);
        }
        // Settings > Accessibility > Show messages as: plain text drops the layout
        let plain_view = a11y::A11y::load().plain_view;
        let body = match &cur.html {
            Some(h) if !plain_view => h.clone(),
            Some(h) if cur.text.trim().is_empty() => format!("<pre>{}</pre>", esc(&html_to_text(h))),
            _ => format!("<pre>{}</pre>", esc(&cur.text)),
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
            _ => {
                let (doc, script, bg) = page(&body, cur.allow, true);
                let wk = u.as_weak();
                let then = on_ready.take();
                // the body area takes the page's colour as the page appears
                let cb: Box<dyn FnOnce() -> bool> = Box::new(move || {
                    if let Some(f) = then { if !f() { return false; } }
                    if let Some(u) = wk.upgrade() { u.set_body_bg(mailview::color(bg)); }
                    true
                });
                mailview::show(&doc, bg, script, Some(cb));
            }
        }
    });
    // the native renderers show the body at once
    if let Some(f) = on_ready { let _ = f(); }
}

/// HTML without colours, backgrounds or a stylesheet: simple prose (most personal mail).
fn html_uncolored(h: &str) -> bool {
    let l = h.to_ascii_lowercase();
    !["color", "background", "<style", "<font", "<svg", "<body"].iter().any(|k| l.contains(k))
}

fn esc(s: &str) -> String {
    s.replace('&', "&amp;").replace('<', "&lt;").replace('>', "&gt;")
}

thread_local! { static DARK: std::cell::Cell<bool> = const { std::cell::Cell::new(true) }; }

/// The app is drawn dark (UI thread).
pub fn is_dark() -> bool { DARK.with(|d| d.get()) }

fn wv_bg() -> (u8, u8, u8, u8) {
    if DARK.with(|d| d.get()) { (0x14, 0x14, 0x14, 255) } else { (255, 255, 255, 255) }
}

/// Automation only: run JS in the email WebView; the result lands in WV_EVAL for `wv-result`.
pub static WV_EVAL: Mutex<Option<String>> = Mutex::new(None);
pub fn wv_eval(js: &str) -> bool {
    *WV_EVAL.lock().unwrap() = None;
    WV.with(|w| match &*w.borrow() {
        Some(v) => v.evaluate_script_with_callback(js, |r| { *WV_EVAL.lock().unwrap() = Some(r); }).is_ok(),
        None => false,
    })
}

fn wrap_html_opts(body: &str, images: bool) -> String {
    page(body, images, false).0
}

/// The reader page, whether it carries a script of ours (then, and only then, its frame may
/// run scripts: the CSP admits our scripts by their hashes) and the colour it paints (the shell,
/// the body area and the page agree on it, so nothing flashes as it is swapped in).
///
/// In the dark theme (unless "Always show emails in light mode"): plain text and HTML that sets
/// no colours of its own take the reader's colours; other HTML gets Bulwark's dark conversion
/// when `recolor` (dark_mail.rs, email_dark.js) and the accessibility colour override is off;
/// anything else is on white.
fn page(body: &str, images: bool, recolor: bool) -> (String, bool, &'static str) {
    // no <base>, no meta refresh, no names that shadow `document` (mailview::sanitize_mail_html);
    // in a message reported as phishing also no link address (mailview::links_off)
    let links_off = mailview::links_off();
    let body = &mailview::sanitize_mail_html_opts(body, links_off);
    let p = settings::Settings::load();
    // Settings > Accessibility: spacing, width, links, motion, text size and fitting
    let a = a11y::A11y::from(&p, a11y::System::detect());
    let dark = DARK.with(|d| d.get()) && !p.bool("emailAlwaysLightMode", false);
    let plain = body.starts_with("<pre>");
    let themed = dark && (plain || html_uncolored(body));
    let recolor = recolor && dark && !themed && !a.override_active();
    let script = match (a.mail_script(), recolor) {
        (Some(s), true) => Some(format!("{s}\n{}", dark_mail::SCRIPT)),
        (None, true) => Some(dark_mail::SCRIPT.to_string()),
        (s, false) => s,
    };
    // the HTML parser turns CRLF into LF before the CSP hashes a script: hash (and send) it in that
    // form, or a script read from a CRLF checkout (include_str!) is blocked
    let script = script.map(|s| s.replace("\r\n", "\n").replace('\r', "\n"));
    let has_script = script.is_some();
    // the accessibility override paints its own page colour (a11y.rs)
    let bg = if dark && a.override_active() { "#0a0a0a" } else if themed { mailview::reader_bg() } else if recolor { dark_mail::BACKGROUND } else { "#ffffff" };
    let csp = csp_with(images, script.as_deref());
    // a link with no address (a reported message) reads as plain text, and says it is off
    let off_css = if links_off { "a:not([href]),area:not([href]){color:inherit!important;text-decoration:underline dotted!important;cursor:not-allowed!important}" } else { "" };
    let extra = format!("<style>{}{off_css}</style>{}", a.mail_css(dark), script.map(|s| format!("<script>{s}</script>")).unwrap_or_default());
    let csp = format!("{csp}{extra}");
    // Bulwark plainTextFont: sans (default) or mono for text/plain bodies
    let pre_font = if p.str("plainTextFont", "sans") == "mono" { "ui-monospace,Consolas,'Cascadia Mono',monospace" } else { "inherit" };
    // Bulwark messageSpacing: auto drops the gutter for full-bleed HTML layouts, edge always, always never
    let full_bleed = || { let l = body.to_ascii_lowercase(); l.contains("<style") && (l.contains("width=\"100%\"") || l.contains("width:100%") || l.contains("width: 100%")) };
    let margin = match p.str("messageSpacing", "auto").as_str() {
        "edge" => 0,
        "always" => 16,
        _ => if !plain && full_bleed() { 0 } else { 16 },
    };
    if themed {
        // plain text, and HTML that sets no colours of its own, take the dark reader's colours
        return (format!("<!doctype html><html><head><meta charset=utf-8>{csp}<base target=_blank><style>html,body{{background:#141414;color:#d4d4d4}}body{{margin:{margin}px;font-family:'Segoe UI',sans-serif;font-size:14px;word-wrap:break-word}}pre{{white-space:pre-wrap;font-family:{pre_font};margin:0}}a{{color:#f0577a}}img{{max-width:100%;height:auto}}</style></head><body>{body}</body></html>"), has_script, bg);
    }
    if recolor {
        // the mail's own dark styles follow the app's theme, not the OS (resolveColorSchemeMedia)
        let scheme = dark_mail::color_scheme(true, dark_mail::has_native_dark(body));
        let body = dark_mail::resolve_color_scheme_media(body, true);
        return (format!("<!doctype html><html class=\"{}\" style=\"color-scheme:{scheme}\"><head><meta charset=utf-8>{csp}<base target=_blank><style>body{{margin:{margin}px;font-family:'Segoe UI',sans-serif;font-size:14px;word-wrap:break-word}}pre{{white-space:pre-wrap;font-family:{pre_font}}}img{{max-width:100%;height:auto}}{}</style></head><body>{body}</body></html>", dark_mail::PENDING_CLASS, dark_mail::css()), has_script, bg);
    }
    (format!("<!doctype html><html><head><meta charset=utf-8>{csp}<base target=_blank><style>body{{margin:{margin}px;font-family:'Segoe UI',sans-serif;font-size:14px;color:#111;background:#fff;word-wrap:break-word}}pre{{white-space:pre-wrap;font-family:{pre_font}}}img{{max-width:100%;height:auto}}</style></head><body>{body}</body></html>"), has_script, bg)
}

fn sync_webview(ui: &AppWindow) {
    // light renderer: re-layout when the reading pane width changes
    if ui.get_render_mode() == 1 && ui.get_current_mail() != "" {
        let w = ui.get_bw();
        if (LITE_W.with(|x| x.get()) - w).abs() > 4.0 && w > 50.0 { render_current(ui); }
    }
    mailview::sync(ui);
}


pub(crate) type Shared = Arc<Mutex<Option<Arc<jmap::Client>>>>;

pub fn config_dir() -> std::path::PathBuf {
    platform::config_dir()
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

thread_local! { pub(crate) static WV_UNAVAILABLE: std::cell::Cell<bool> = const { std::cell::Cell::new(false) }; }
thread_local! { static RESET_ARMED: std::cell::Cell<bool> = const { std::cell::Cell::new(false) }; }
thread_local! { static SERVER_DEBOUNCE: slint::Timer = slint::Timer::default(); }
thread_local! { static TEMPLATES: std::cell::RefCell<(String, Vec<Value>)> = const { std::cell::RefCell::new((String::new(), Vec::new())) }; }
thread_local! { static THREAD_ID: std::cell::RefCell<Option<String>> = const { std::cell::RefCell::new(None) }; }

/// Load the open message's conversation (unless threading is turned off, like Bulwark's disableThreading).
fn load_thread(w: slint::Weak<AppWindow>, c: Arc<jmap::Client>, id: String) {
    if settings::Settings::load().bool("disableThreading", false) { return; }
    std::thread::spawn(move || {
        let Ok(mut list) = c.thread_emails(unified::real_id(&id)) else { return };
        list.sort_by_key(|m| m["receivedAt"].as_str().unwrap_or("").to_string());
        let tid = list.first().and_then(|m| m["threadId"].as_str().map(String::from));
        let rows: Vec<ThreadMsg> = list.iter().map(|m| ThreadMsg {
            id: unified::same_source(&id, m["id"].as_str().unwrap_or("")).into(),
            sender: mail::sender(m).into(),
            date: mail::parse_time(&m["receivedAt"]).map(|t| t.format("%d %b %H:%M").to_string()).unwrap_or_default().into(),
            preview: mail::clean_preview(m["preview"].as_str().unwrap_or("")).into(),
            current: m["id"] == unified::real_id(&id),
            unread: m["keywords"]["$seen"].as_bool() != Some(true),
        }).collect();
        let _ = w.upgrade_in_event_loop(move |u| {
            if u.get_current_mail().as_str() != id { return; }
            THREAD_ID.with(|t| *t.borrow_mut() = tid);
            listsync::sync_into(&u.get_thread_msgs(), rows, |r| r.id.clone(), |a, b| a == b, |m| u.set_thread_msgs(m));
        });
    });
}
/// Reload the open message's conversation strip: a reply was just sent from it, or push
/// reported mail changes (the sent reply joins the thread). UI thread.
pub fn refresh_open_thread(u: &AppWindow) {
    let id = u.get_current_mail().to_string();
    if id.is_empty() || u.get_app() != 0 { return; }
    let Some(c) = READER_CLIENT.with(|r| r.borrow().clone()) else { return };
    load_thread(u.as_weak(), c, id);
}

thread_local! {
    /// Client of the account the open message belongs to (unified inbox) and of the open composer.
    static READER_CLIENT: std::cell::RefCell<Option<Arc<jmap::Client>>> = const { std::cell::RefCell::new(None) };
    static COMPOSE_CLIENT: std::cell::RefCell<Option<Arc<jmap::Client>>> = const { std::cell::RefCell::new(None) };
}

/// Login that owns a listed message: another account's client for unified-inbox rows, else the active one.
fn login_for(st: &Shared, id: &str) -> Option<Arc<jmap::Client>> {
    if let Some(o) = mail::owner(id) {
        if o != unified::active_user() { return unified::cached(&o); }
    }
    st.lock().unwrap().clone()
}

/// Client that reads a listed message: its login, addressing the shared/group account the
/// row came from (unified inbox) when there is one.
fn client_for(st: &Shared, id: &str) -> Option<Arc<jmap::Client>> {
    let c = login_for(st, id)?;
    Some(match mail::shared_account(id) { Some(a) => Arc::new(c.scoped(&a)), None => c })
}

/// The open message's client (falls back to the active one).
fn reader_client(st: &Shared) -> Option<Arc<jmap::Client>> {
    READER_CLIENT.with(|r| r.borrow().clone()).or_else(|| st.lock().unwrap().clone())
}

/// The composer's client (a reply from the unified inbox sends from that account).
fn compose_client(st: &Shared) -> Option<Arc<jmap::Client>> {
    COMPOSE_CLIENT.with(|r| r.borrow().clone()).or_else(|| st.lock().unwrap().clone())
}

/// mail::apply, split per owning account so unified-inbox rows use their own client.
pub(crate) fn apply_routed(u: &AppWindow, st: &Shared, ids: Vec<String>, action: &str) {
    if ids.is_empty() { return; }
    if action.starts_with("copy-code:") {
        if let Some(c) = st.lock().unwrap().clone() { mail::apply(u, c, ids, action); }
        return;
    }
    // confirm and resolve toggles once for the whole selection, then run it per source
    if !mail::confirm_action(u, ids.len(), action) { return; }
    let action = mail::resolve_toggle(&ids, action);
    // keep the reader's star in step when the open message is starred from any menu or key
    if matches!(action.as_str(), "star" | "unstar") && ids.iter().any(|i| i.as_str() == u.get_current_mail().as_str()) {
        u.set_r_flagged(action == "star");
    }
    let selection = ids.clone();
    // one group per login and, for group inboxes, per shared JMAP account
    let mut groups: Vec<((Option<String>, Option<String>), Vec<String>)> = vec![];
    for id in ids {
        let o = mail::owner(&id).filter(|o| *o != unified::active_user());
        let key = (o, mail::shared_account(&id));
        match groups.iter_mut().find(|g| g.0 == key) { Some(g) => g.1.push(id), None => groups.push((key, vec![id])) }
    }
    for ((o, _), ids) in groups {
        let c = match &o { Some(o) => unified::cached(o), None => st.lock().unwrap().clone() };
        if let Some(c) = c { mail::apply_confirmed(u, c, ids, &action, &selection); }
    }
}

/// Move messages to a folder of the active account: other accounts' rows (and group inboxes)
/// go to their own folder with the same role, else the same name (Bulwark move targets).
pub(crate) fn move_ids(u: &AppWindow, st: &Shared, ids: Vec<String>, fid: &str) {
    let ids: Vec<String> = ids.into_iter().filter(|i| !i.is_empty()).collect();
    let (own, other): (Vec<String>, Vec<String>) = ids.into_iter()
        .partition(|i| mail::owner(i).is_none_or(|o| o == unified::active_user()) && mail::shared_account(i).is_none());
    if let Some(c) = st.lock().unwrap().clone() { mail::apply(u, c, own, &format!("move:{fid}")); }
    if !other.is_empty() {
        let (role, name) = mail::mailbox_info(fid);
        apply_routed(u, st, other, &unified::moveto_action(&role, &name));
    }
}

thread_local! { static MY_ADDRS: std::cell::RefCell<Vec<String>> = const { std::cell::RefCell::new(Vec::new()) }; }

/// The identities' addresses loaded at sign-in (UI thread).
pub fn my_addrs_cached() -> Vec<String> { MY_ADDRS.with(|a| a.borrow().clone()) }

/// The user's own addresses (login and identities), for "is this me" checks.
pub fn my_addrs(st: &Shared) -> Vec<String> {
    let mut v = MY_ADDRS.with(|a| a.borrow().clone());
    if let Some(c) = st.lock().unwrap().as_ref() { v.push(c.username.clone()); }
    v
}

thread_local! {
    /// Loaded native plugins (UI thread only).
    static PLUGINS: std::cell::RefCell<Vec<plugins::Plugin>> = const { std::cell::RefCell::new(Vec::new()) };
    static PLUGIN_ERRORS: std::cell::RefCell<Vec<String>> = const { std::cell::RefCell::new(Vec::new()) };
    static PLUGIN_LOAD_ERRORS: std::cell::RefCell<Vec<String>> = const { std::cell::RefCell::new(Vec::new()) };
}

/// Plugins the user switched on (allow-list; a plugin that is not in it is neither loaded nor called).
pub fn enabled_plugins() -> Vec<String> {
    let p = settings::Settings::load();
    p.prefs.get("enabledPlugins").and_then(|v| v.as_array()).map(|a| a.iter().filter_map(|x| x.as_str().map(String::from)).collect()).unwrap_or_default()
}

/// The allow-list as it counts: switched on AND still bound to the file it was enabled for.
fn effective_plugins() -> Vec<String> {
    let bindings = settings::Settings::load().prefs.get("pluginBindings").cloned().unwrap_or_default();
    PLUGINS.with(|p| plugins::bound_enabled(&p.borrow(), &enabled_plugins(), &bindings))
}

/// Load what was switched on, unload what was switched off; returns the effective allow-list.
fn plugins_synced() -> Vec<String> {
    let en = effective_plugins();
    let errs = PLUGINS.with(|p| plugins::sync(&mut p.borrow_mut(), &en));
    PLUGIN_LOAD_ERRORS.with(|e| { let mut e = e.borrow_mut(); for x in errs { if !e.contains(&x) { e.push(x); } } });
    en
}

/// A plugin was switched on or off in Settings.
pub fn plugin_toggled() { plugins_synced(); }

/// What enabling a plugin pins (its file path and hash).
pub fn plugin_binding(key: &str) -> Option<String> {
    PLUGINS.with(|p| p.borrow().iter().find(|x| x.key == key).map(|x| x.binding()))
}

/// Re-read the plugins folder (Settings → Add-ons opened).
pub fn refresh_plugins() {
    let errs = PLUGINS.with(|p| plugins::refresh(&mut p.borrow_mut()));
    PLUGIN_ERRORS.with(|e| *e.borrow_mut() = errs);
}

/// Rows from the enabled plugins for the open message.
pub fn plugin_email_details(m: &serde_json::Value) -> Vec<(String, String, String)> {
    let en = plugins_synced();
    PLUGINS.with(|p| plugins::email_details(&p.borrow(), &en, m))
}

/// True when the `spam-score` plugin is on, so the built-in spam panel steps aside.
pub fn plugin_spam_active() -> bool {
    let en = plugins_synced();
    PLUGINS.with(|p| plugins::is_active(&p.borrow(), &en, "spam-score"))
}

/// Tell the user about plugins that were switched off for failing.
pub fn plugin_notices(u: &AppWindow) {
    for n in plugins::drain_notices() { mail::show_toast(u, &n, false); }
}

pub fn plugin_list() -> Vec<serde_json::Value> {
    let (raw, eff) = (enabled_plugins(), effective_plugins());
    PLUGINS.with(|p| p.borrow().iter().map(|x| {
        let mut d = plugins::describe(x);
        let was_on = raw.contains(&x.key);
        d["enabled"] = (was_on && eff.contains(&x.key)).into();
        if was_on && !eff.contains(&x.key) {
            d["description"] = format!("{} The file changed since you switched it on: switch it off and on again.", d["description"].as_str().unwrap_or("")).into();
        }
        d
    }).collect())
}

pub fn plugin_errors() -> Vec<String> {
    let mut v = PLUGIN_ERRORS.with(|e| e.borrow().clone());
    v.extend(PLUGIN_LOAD_ERRORS.with(|e| e.borrow().clone()));
    v
}

pub fn plugin_compose_check(draft: &serde_json::Value) -> Option<String> {
    let en = plugins_synced();
    PLUGINS.with(|p| plugins::compose_check(&p.borrow(), &en, draft))
}

thread_local! {
    /// The open message and its listed attachments.
    static CUR_MSG: std::cell::RefCell<Option<(Arc<Value>, Vec<Value>)>> = const { std::cell::RefCell::new(None) };
}

fn sanitize_name(n: &str) -> String { attachments::safe_file_name(n) }

static SESSION_GEN: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);

/// Data types the app follows over push (RFC 8620 §7.3 `types`).
const PUSH_TYPES: [&str; 5] = ["Email", "Mailbox", "Thread", "EmailDelivery", "EmailSubmission"];

/// The push types of a login: mail, plus calendar objects where it has calendars.
fn push_types(c: &jmap::Client) -> Vec<&'static str> {
    let mut v = PUSH_TYPES.to_vec();
    if c.has(calendar::CAL) { v.extend(["CalendarEvent", "Calendar"]); }
    if c.has("urn:ietf:params:jmap:contacts") { v.extend(["ContactCard", "AddressBook"]); }
    v
}

/// Live updates: listen to the JMAP event stream and sync what a StateChange names.
/// The stream is re-opened with exponential backoff (2 s .. 5 min, with jitter) after it drops
/// or goes silent. Each time it opens everything is synced once (cheap: /changes deltas), since
/// changes made before it was listening (or while it was down) are never pushed.
fn start_push(w: slint::Weak<AppWindow>, c: Arc<jmap::Client>) {
    let gen = SESSION_GEN.fetch_add(1, std::sync::atomic::Ordering::SeqCst) + 1;
    let live = move || SESSION_GEN.load(std::sync::atomic::Ordering::SeqCst) == gen;
    std::thread::spawn(move || {
        let mut backoff = 2u64;
        loop {
            if !live() { return; }
            let started = std::time::Instant::now();
            let types_wanted = push_types(&c);
            let res = c.watch(&types_wanted, &mut |ev| {
                if !live() { return false; }
                let types: std::collections::HashSet<String> = match ev {
                    jmap::PushEvent::Open => types_wanted.iter().map(|t| t.to_string()).collect(),
                    jmap::PushEvent::State(v) => jmap::changed_types(&v, &c.account).into_keys().collect(),
                    _ => Default::default(),
                };
                if !types.is_empty() {
                    let (w2, c2) = (w.clone(), c.clone());
                    let _ = w.upgrade_in_event_loop(move |_| schedule_refresh(w2, c2, types));
                }
                true
            });
            if !live() { return; }
            // a rejected sign-in is final: no reconnecting with dead credentials, the user is
            // told once and signs in again
            if res.as_ref().err().is_some_and(|e| auth_rejected(e)) {
                let _ = w.upgrade_in_event_loop(|u| mail::show_toast(&u, "Your sign-in is no longer valid. Sign in again.", false));
                return;
            }
            if started.elapsed() > std::time::Duration::from_secs(120) { backoff = 2; }
            let nanos = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map(|d| d.subsec_nanos() as u64).unwrap_or(0);
            let wait = backoff * 1000 + nanos % (backoff * 500);
            std::thread::sleep(std::time::Duration::from_millis(wait));
            backoff = (backoff * 2).min(300);
        }
    });
}

thread_local! {
    static REFRESH: slint::Timer = slint::Timer::default();
    static PUSHED: std::cell::RefCell<std::collections::HashSet<String>> = std::cell::RefCell::new(Default::default());
}

/// Coalesce pushes for 700 ms, then sync only what changed: folders (Mailbox/changes), the list
/// (Email/queryChanges + Email/changes), new-mail notifications, the Scheduled view.
fn schedule_refresh(w: slint::Weak<AppWindow>, c: Arc<jmap::Client>, types: std::collections::HashSet<String>) {
    PUSHED.with(|p| p.borrow_mut().extend(types));
    REFRESH.with(|t| t.start(slint::TimerMode::SingleShot, std::time::Duration::from_millis(700), move || {
        let types = PUSHED.with(|p| std::mem::take(&mut *p.borrow_mut()));
        let has = |t: &str| types.contains(t);
        if has("Mailbox") || has("Email") || has("Thread") { mail::load_mailboxes(w.clone(), c.clone(), false); }
        let scheduled = w.upgrade().is_some_and(|u| u.get_current_folder() == "__scheduled");
        if has("Email") || has("Thread") || (scheduled && has("EmailSubmission")) { mail::sync(w.clone(), c.clone()); }
        if has("Email") || has("Thread") { if let Some(u) = w.upgrade() { refresh_open_thread(&u); } }
        if has("Email") || has("EmailDelivery") { check_new_mail(c.clone(), Some(w.clone())); }
        if has("ContactCard") || has("AddressBook") { trustbook::refresh(w.clone(), c.clone(), false); }
        if has("CalendarEvent") || has("Calendar") {
            reminders::changed(w.clone(), c.clone());
            if w.upgrade().is_some_and(|u| u.get_app() == 1) { calendar::load(w.clone(), c.clone()); }
        }
    }));
}

static NOTIFIED: Mutex<Option<std::collections::HashSet<String>>> = Mutex::new(None);

/// Desktop notification for newly arrived unread inbox mail (Bulwark: emailNotificationsEnabled,
/// lib/desktop/mail-notifications.ts): mail that arrives together is told about once, one
/// message with Archive / Mark as read / Delete, several as a count that opens the Inbox.
fn check_new_mail(c: Arc<jmap::Client>, w: Option<slint::Weak<AppWindow>>) {
    let prefs = settings::Settings::load();
    if !prefs.bool("emailNotificationsEnabled", true) { return; }
    // Bulwark: Do not disturb keeps every notification quiet
    let dnd = prefs.bool("doNotDisturb", false);
    // Bulwark emailNotificationSound (default on) and pushNotifyInboxOnly (default off: every
    // folder except Junk, Trash, Sent and Drafts)
    let sound = prefs.bool("emailNotificationSound", true);
    let inbox_only = prefs.bool("pushNotifyInboxOnly", false);
    let prefs_mutes = prefs.prefs.get(folders::KEY_MUTES).cloned();
    std::thread::spawn(move || {
        let Ok(roles) = c.role_ids() else { return };
        let Some(inbox) = roles.get("inbox") else { return };
        let place = if inbox_only {
            json!({"inMailbox": inbox})
        } else {
            let skip: Vec<String> = ["junk", "trash", "sent", "drafts", "archive"].iter().filter_map(|r| roles.get(*r).cloned()).collect();
            json!({"inMailboxOtherThan": skip})
        };
        let Ok((list, _, _)) = c.list(json!({"operator": "AND", "conditions": [place, {"notKeyword": "$seen"}]}), 0, 10) else { return };
        let mut guard = NOTIFIED.lock().unwrap();
        let first_run = guard.is_none();
        let seen = guard.get_or_insert_with(Default::default);
        let mut batch = vec![];
        // per-folder notification mutes (Bulwark notificationMutes, lib/desktop/new-mail.ts:42-47)
        let mutes = folders::Mutes::decode(prefs_mutes.as_ref());
        let skip: Vec<String> = ["junk", "trash", "sent", "drafts", "archive"].iter().filter_map(|r| roles.get(*r).cloned()).collect();
        for m in list.iter().rev() {
            let id = m["id"].as_str().unwrap_or("").to_string();
            // a muted message is still marked as told about: unmuting later must not replay it
            let counted = folders::counted_mailboxes(&folders::mailbox_ids(m), inbox_only, inbox, &skip);
            if seen.insert(id.clone()) && !first_run && !folders::silenced(&mutes, &c.account, &counted) {
                batch.push(notify::NewMail {
                    id,
                    mailbox: gsearch::hit_folder(m, std::slice::from_ref(inbox)),
                    sender: mail::sender(m),
                    subject: m["subject"].as_str().unwrap_or("").to_string(),
                    preview: mail::clean_preview(m["preview"].as_str().unwrap_or("")),
                });
            }
        }
        drop(guard);
        let arrived = !batch.is_empty();
        batch.reverse();
        if !dnd {
            if let Some(n) = notify::mail_note(&batch, &unified::login_of(&c), sound, roles.contains_key("archive")) { notify::show(n); }
        }
        // Settings > Accessibility > Visual alerts: flash the taskbar button
        if arrived && a11y::A11y::load().visual_alerts {
            if let Some(w) = w {
                let _ = w.upgrade_in_event_loop(|u| {
                    use slint::winit_030::{winit::window::UserAttentionType, WinitWindowAccessor};
                    u.window().with_winit_window(|ww| ww.request_user_attention(Some(UserAttentionType::Informational)));
                });
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
        // keep the cid: original: a reply/forward quotes it and sends the parts inline again
        m["__cidBodyValues"][pid] = json!(html.clone());
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
        (Some(m), Some(t)) if m == unified::FOLDER_ID => mail::folder_filter(&m, Some(&t)),
        (_, Some(t)) => gsearch::mailbox_search_filter(&t, None, mail::role_id),
        (Some(m), None) if m == "__scheduled" => json!({"scheduled": true}),
        (Some(m), None) if m == unified::FOLDER_ID => json!({"unified": true}),
        (Some(m), None) => json!({"inMailbox": m}),
        _ => json!({}),
    };
    mail::load_list(ui, client, filter);
}

fn load_mailboxes(ui: slint::Weak<AppWindow>, client: Arc<jmap::Client>, select_inbox: bool) {
    mail::load_mailboxes(ui, client, select_inbox);
}

fn main() -> Result<(), slint::PlatformError> {
    // an update the user approved ("Install next time") installs before anything else
    // (only when no other Barbican runs: the installer would replace it while it is open)
    // first_instance() takes the instance mutex for the life of the process (the check cannot run
    // twice: the second call would see this process's own lock). Only the default profile installs
    // a pending update (test, automation and dev profiles never do).
    let first = platform::first_instance();
    if first && profile::is_default(profile::current()) && updater::on_start() { return Ok(()); }
    // Skia: the same text/graphics engine Chrome uses, so text matches Bulwark in the browser
    // (and gets system font fallback for CJK and other scripts).
    let _ = slint::BackendSelector::new().renderer_name("skia".into()).select();
    // a mailto: link from the OS while Barbican already runs: hand it over and quit
    let link = instance::mailto_arg(&std::env::args().skip(1).collect::<Vec<_>>());
    if link.as_deref().is_some_and(instance::forward) { return Ok(()); }
    // a start that never got cleared last time (it crashed) runs without network pictures
    // (the first Barbican of the profile guards: a second one neither sees nor clears its marker)
    let first_of_profile = platform::first_of_profile();
    let last_start_failed = crashguard::start(&config_dir(), first_of_profile);
    if last_start_failed { crashguard::enter_safe_mode(); }
    // what the last sign-out could not delete (locked then), before any WebView exists; not
    // while another Barbican of this profile runs: its mail view may have that profile open
    // (every path swept is this profile's own, so another profile running does not matter)
    if first_of_profile { signout::sweep_pending(); }
    let ui = AppWindow::new()?;
    apply_theme(&ui);
    apply_ui_prefs(&ui);
    motion::watch_system(&ui);
    automation::start(&ui);
    avatars::set_window(ui.as_weak());
    rte::wire_drop(&ui);
    chips::wire(&ui);
    {
        // listed, not loaded: a plugin runs only after the user switches it on
        let (found, errors) = plugins::discover();
        PLUGINS.with(|p| *p.borrow_mut() = found);
        PLUGIN_ERRORS.with(|e| *e.borrow_mut() = errors);
        plugins_synced();
        // plugins used to be on by default: tell upgraders once why theirs are off now
        let st = settings::Settings::load();
        if st.prefs.get("pluginsOffNoticeShown").is_none() {
            let en = enabled_plugins();
            if PLUGINS.with(|p| plugins::has_unenabled(&p.borrow(), &en)) {
                mail::show_toast(&ui, "Plugins are now off until you switch them on in Settings > Add-ons.", false);
                settings::set_pref("pluginsOffNoticeShown", serde_json::json!(true));
            }
        }
    }
    let state: Shared = Arc::new(Mutex::new(None));
    {
        // mailto: links (clicked in a message, or handed over by the OS) open the composer
        let st = state.clone();
        mailto::install(&ui, Box::new(move |u, m| {
            if st.lock().unwrap().is_none() { return false; }
            // keep what an open composer holds before it is replaced
            if u.get_composing() && !autosave::unedited(u) { u.invoke_save_draft(false); }
            u.invoke_compose_new();
            if !u.get_composing() { return false; }
            mailto::fill(u, m);
            true
        }));
        instance::listen();
        // Barbican moved or updated into a new folder: point the registration at this exe
        mailto::repair_registration();
        if let Some(l) = link { mailto::request(l); }
    }
    cal_extra::wire(&ui, state.clone());
    desktop::init(&ui, state.clone());

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
        u.set_retry_available(false);
        u.set_status("".into());
        let (server, user, pass, totp) = (server.to_string(), user.to_string(), pass.to_string(), totp.to_string());
        std::thread::spawn(move || {
            let res = jmap::Client::connect(&server, &user, &pass, &totp);
            // a 2FA code is single-use, so only the password itself can be remembered
            let rem = (remember && totp.trim().is_empty()).then(|| serde_json::json!({"kind": "password", "server": server, "user": user, "pass": pass}));
            finish_login(w, st, res, server, user, rem, oauth::Origin::SignIn);
        });
    });

    let w = ui.as_weak();
    let st = state.clone();
    ui.on_sso_login(move |server, user, remember| {
        let (w, st) = (w.clone(), st.clone());
        let u = w.unwrap();
        if server.trim().is_empty() { u.set_status("Enter your server address first.".into()); return; }
        u.set_busy(true);
        u.set_retry_available(false);
        u.set_status("Continue in your browser…".into());
        let client = u.get_client_id().trim().to_string();
        let client = if client.is_empty() { "barbican".to_string() } else { client };
        let (server, user) = (server.to_string(), user.to_string());
        std::thread::spawn(move || {
            let base = jmap::normalize_base(&server);
            let res = oauth::sign_in(&base, &user, &client).and_then(|t| jmap::Client::connect_oauth(&base, t));
            finish_login(w, st, res, server, user, remember.then(|| serde_json::json!({"kind": "oauth"})), oauth::Origin::SignIn);
        });
    });

    let w = ui.as_weak();
    let st = state.clone();
    ui.on_sign_out(move || {
        let u = w.unwrap();
        SESSION_GEN.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
        *RESTORE_PENDING.lock().unwrap() = None;
        let account = u.get_account().to_string();
        let mut owner = None;
        let mut server = String::new();
        if let Some(c) = st.lock().unwrap().take() {
            server = c.base.clone();
            owner = Some(reminders::owner_key(&c.base, &c.username));
            if let Some(t) = c.tokens() { std::thread::spawn(move || oauth::revoke(&c.base, &t)); }
        }
        // the reminder runner would write its state again after the wipe
        reminders::stop();
        // the account list is keyed by the name typed on the login form
        let login = std::mem::take(&mut *LOGIN_USER.lock().unwrap());
        let forgot = signout::forget_keyring(&[login.as_str(), account.as_str()], &server);
        let last = forgot.last;
        if last { avatars::cancel_pending(); }
        // a chat answer still coming must not write the account's file again
        CHAT_USER.lock().unwrap().clear();
        bodycache::with(|b| b.clear());
        unified::clear();
        // cookies, cache and storage of the mail view, and the account's files on disk
        mailview::clear_browsing_data();
        // live, now: this process's view, or another Barbican of this profile (same WebView folder)
        // synchronously: the wipe's thread retries locked files and may not finish before the program quits
        if last { settings::forget_trusted_senders(); }
        // what was read from the account's address book; the book itself stays on the server
        trustbook::clear();
        signout::wipe(signout::Who { user: account, owner, last, webview_live: mailview::is_built() || platform::other_of_profile_running() });
        // nobody at this computer finds the last person's password or code in the form
        u.set_login_password("".into());
        u.set_login_otp("".into());
        u.set_chat(display_chat(&[]));
        u.set_accounts(accounts_model());
        u.set_current_mail("".into());
        u.set_folders(ModelRc::new(VecModel::from(Vec::<Folder>::new())));
        u.set_mails(ModelRc::new(VecModel::from(Vec::<Mail>::new())));
        // a credential that stayed in the keychain is not "signed out"
        u.set_status(if forgot.cleared { "Signed out.".into() } else {
            format!("Signed out here, but your saved sign-in could not be removed from {}. Remove the Barbican entries there yourself.", platform::keystore_name()).into()
        });
        u.set_logged_in(false);
    });

    // "Try again" on the login form after a restore that could not reach the server
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_retry_restore(move || {
        let u = w.unwrap();
        // the stored value as it is now (another window may have rotated it), else the one held
        // (a stored entry that is gone was signed out, perhaps by another window: it is not
        // brought back from the copy held here)
        let v = match oauth::remembered_status() {
            oauth::Remembered::Some(v) => Some(v),
            oauth::Remembered::Unreadable => RESTORE_PENDING.lock().unwrap().clone(),
            oauth::Remembered::Missing => { *RESTORE_PENDING.lock().unwrap() = None; None }
        };
        let Some(v) = v else { u.set_retry_available(false); return };
        u.set_retry_available(false);
        u.set_busy(true);
        u.set_status("Signing in…".into());
        restore_session(w.clone(), st.clone(), v, false, None);
    });

    // restore a remembered session
    match oauth::remembered_status_with_source() {
        (oauth::Remembered::Some(v), source) => {
            ui.set_busy(true);
            ui.set_status("Signing in…".into());
            restore_session(ui.as_weak(), state.clone(), v, false, Some(source));
        }
        (oauth::Remembered::Unreadable, source) => {
            // kept as it is: only a new sign-in replaces it
            restore::log(&restore::line(source.label(), "unknown", false, "unreadable"));
            ui.set_status("Your saved sign-in could not be read; sign in again.".into());
        }
        (oauth::Remembered::Missing, source) => restore::log(&restore::line(source.label(), "none", false, "none")),
    }
    ui.set_accounts(accounts_model());

    // account switcher (Bulwark's account menu)
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_switch_account(move |user| {
        let Some(v) = oauth::accounts().into_iter().find(|a| a["user"].as_str() == Some(user.as_str())) else { return };
        let u = w.unwrap();
        // the current session (and its push stream) stays until the new one is up: a failed
        // switch keeps it; start_push on success retires the old stream
        u.set_current_mail("".into());
        u.set_show_settings(false);
        u.set_app(0);
        u.set_busy(true);
        restore_session(w.clone(), st.clone(), v, true, None);
    });
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_add_account(move || {
        let u = w.unwrap();
        SESSION_GEN.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
        *st.lock().unwrap() = None;
        u.set_user("".into());
        u.set_login_password("".into());
        u.set_login_otp("".into());
        u.set_status("Sign in to another account. Your other accounts stay signed in.".into());
        u.set_logged_in(false);
    });

    let w = ui.as_weak();
    let st = state.clone();
    ui.on_select_folder(move |id| {
        let Some(c) = st.lock().unwrap().clone() else { return };
        let u = w.unwrap();
        u.set_current_mail("".into());
        glance::title(&u);
        // Bulwark clearSearchOnFolderChange (default off): keep the query and re-run it in the new folder
        let q = u.get_search().trim().to_string();
        if !q.is_empty() && id != "__scheduled" && !settings::Settings::load().bool("clearSearchOnFolderChange", false) {
            mail::load_list(w.clone(), c, mail::folder_filter(&id, Some(&q)));
            return;
        }
        u.set_search("".into());
        load_folder(w.clone(), c, Some(id.to_string()), None);
    });

    let w = ui.as_weak();
    let st = state.clone();
    ui.on_do_search(move |text| {
        let Some(c) = st.lock().unwrap().clone() else { return };
        let u = w.unwrap();
        // Enter on a highlighted suggestion runs that one instead
        if search_ui::accept(&u) { return; }
        search_ui::searched(&u, &text);
        u.set_current_mail("".into());
        if text.trim().is_empty() {
            load_folder(w.clone(), c, Some(u.get_current_folder().to_string()), None);
        } else {
            // in All inboxes the search stays within the merged inboxes (Bulwark searchUnifiedEmails)
            let f = u.get_current_folder().to_string();
            load_folder(w.clone(), c, (f == unified::FOLDER_ID).then_some(f), Some(text.to_string()));
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
    msgload::SHARED.with(|s| *s.borrow_mut() = Some(state.clone()));
    ui.on_open_mail(move |id| msgload::open(&w.unwrap(), &st, id.to_string()));

    let cfg = Arc::new(Mutex::new(ai::Config::load()));
    ui.set_spam_show_clean(cfg.lock().unwrap().show_clean_spam);

    let prefs = Arc::new(Mutex::new(settings::Settings::load()));
    settings::register_live(prefs.clone());
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
            listsync::sync_into(&u.get_settings_tabs(), p.tab_models(adv), |t| t.id.clone(), |a, b| a == b, |m| u.set_settings_tabs(m));
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
            // in place: toggling a switch or typing keeps the other rows (and the scroll position)
            listsync::sync_into(&u.get_settings_rows(), rows, |r| format!("{}{}{}", r.kind, r.key, r.label),
                |a, b| a.kind == b.kind && a.key == b.key && a.label == b.label && a.desc == b.desc && a.index == b.index && a.on == b.on && a.text == b.text && listsync::rows_eq(&a.options, &b.options),
                |m| u.set_settings_rows(m));
            u.set_ai_hidden(c.hidden);
            u.set_render_mode(c.render_mode);
            u.set_auto_images(c.remote_images);
            u.set_spam_show_clean(c.show_clean_spam);
            u.set_chips(chips_model(&c));
        })
    };

    {
        let lsp = load_server_page.clone();
        let reload: filters_x::Reload = Arc::new(move |tab: String| lsp(tab));
        filters_x::wire(&ui, state.clone(), server_cache.clone(), reload);
    }
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

    let (pf, cf, rs, srv, sw, wt, stg) = (prefs.clone(), cfg.clone(), refresh_settings.clone(), server_cache.clone(), server_write.clone(), ui.as_weak(), state.clone());
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
        // after the lock is released: it files or copies the trusted list through the same settings
        if key == trustbook::SETTING_KEY { trustbook::toggled(on, wt.clone(), stg.lock().unwrap().clone()); }
        rs();
        if key == "enableUnifiedMailbox" && on { unified::warm_up(Some(wt.clone())); }
        if let Some(u) = wt.upgrade() {
            let open = u.get_current_folder() == unified::FOLDER_ID;
            if key == "enableUnifiedMailbox" && !on && open {
                // the folder is gone: clear its list and fall back to the Inbox
                mail::clear_list(&u);
                u.set_current_mail("".into());
                if let Some(c) = unified::cached(&unified::active_user()) { mail::load_mailboxes(wt.clone(), c, true); }
            } else if open && (key == "unifiedCrossAccount" || key == "includeGroupInUnified") {
                if key == "unifiedCrossAccount" && on { unified::warm_up(Some(wt.clone())); }
                if let Some(c) = unified::cached(&unified::active_user()) { load_folder(wt.clone(), c, Some(unified::FOLDER_ID.into()), None); }
            }
            if key == "unifiedCrossAccount" || key == "includeGroupInUnified" {
                let w2 = wt.clone();
                std::thread::spawn(move || { unified::refresh_counts(); let _ = w2.upgrade_in_event_loop(|u| mail::rebuild_folders(&u)); });
            }
        }
        if key == "spellCheck" { rte::spell::reload(); if let Some(u) = wt.upgrade() { rte::refresh(&u); } }
        if key.starts_with("desktop.") { desktop::setting_changed(&key, on); }
        if key == "unreadBadgeAllAccounts" && on { unified::refresh_other_logins(wt.clone(), true); }
        if let Some(u) = wt.upgrade() { apply_ui_prefs(&u); mail::rebuild_folders(&u); mail::rebuild_list(&u); mail::refresh_avatars(&u); if key == "nestedTags" { mail::rebuild_tags(&u); } }
    });

    let (w, pf, cf, rs, srv3, sw3) = (ui.as_weak(), prefs.clone(), cfg.clone(), refresh_settings.clone(), server_cache.clone(), server_write.clone());
    ui.on_settings_set_select(move |key, idx| {
        if key.starts_with("server.") {
            if let Some(job) = server_settings::edit(&key, &serde_json::json!(idx), &srv3) { sw3(job, 10); }
            rs();
            return;
        }
        let mut p = pf.lock().unwrap();
        let v = p.select_value(&key, idx);
        p.set(&key, v, &mut cf.lock().unwrap());
        drop(p);
        rs();
        if key == "desktop.renderMode" { render_current(&w.unwrap()); }
        if key == trust::POLICY_KEY { redecide_remote(&w.unwrap()); }
        if key == "theme" || key == "uiStyle" { apply_theme(&w.unwrap()); }
        if key == "writingLanguage" { rte::spell::reload(); }
        if key == "desktop.updateChannel" { desktop::setting_changed(&key, true); }
        apply_ui_prefs(&w.unwrap());
        mail::rebuild_folders(&w.unwrap());
        mail::rebuild_list(&w.unwrap());
    });

    // text edits are saved as typed, without rebuilding the page (keeps the caret)
    let (pf, cf, srv, sw, wk) = (prefs.clone(), cfg.clone(), server_cache.clone(), server_write.clone(), ui.as_weak());
    ui.on_settings_set_text(move |key, t| {
        if key.starts_with("server.") {
            if let Some(job) = server_settings::edit(&key, &serde_json::json!(t.to_string()), &srv) { sw(job, 900); }
            return;
        }
        // the book's list is applied by "Save changes", not while typing
        if key == trustbook::TEXT_KEY { trustbook::set_pending(&t); return; }
        pf.lock().unwrap().set(&key, serde_json::json!(t.to_string()), &mut cf.lock().unwrap());
        if key == trust::TRUSTED_KEY { if let Some(u) = wk.upgrade() { redecide_remote(&u); } }
        if key == "admin.key" { if let Some(u) = wk.upgrade() { u.set_admin_enabled(!t.trim().is_empty()); } }
        if settings::is_retention_key(&key) { if let Some(u) = wk.upgrade() { glance::title(&u); } }
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
        if desktop::settings_button(&u, &key) { return; }
        if key == "signout" { u.set_show_settings(false); u.invoke_sign_out(); }
        if key == "mailapp.register" {
            // user-triggered only: writes the HKCU registration (or the Linux .desktop file)
            match mailto::register_default() { Ok(m) => mail::show_toast(&u, &m, false), Err(e) => mail::show_toast(&u, &e, false) }
            u.invoke_settings_search(u.get_settings_query());
        }
        if key == "mailapp.unregister" {
            // user-triggered only: removes what mailapp.register wrote
            match mailto::unregister() { Ok(m) => mail::show_toast(&u, &m, false), Err(e) => mail::show_toast(&u, &e, false) }
            u.invoke_settings_search(u.get_settings_query());
        }
        if key == trustbook::SAVE_KEY { trustbook::apply_pending(w.clone(), st.lock().unwrap().clone()); return; }
        if contacts_x::settings_button(&u, st.lock().unwrap().clone(), &key) { return; }
        if key == "filters.new" { u.global::<FilterX>().invoke_new_rule(); return; }
        if key == "a11y.reset" {
            pf2.lock().unwrap().set("a11y.reset", Value::Null, &mut cf2.lock().unwrap());
            apply_ui_prefs(&u);
            mail::rebuild_folders(&u);
            mail::rebuild_list(&u);
            mail::show_toast(&u, "Accessibility settings reset", false);
            u.invoke_settings_search(u.get_settings_query());
        }
        if key == "plugins.folder" { oauth::open_browser(&plugins::dir().to_string_lossy()); }
        if key == "avatars.refresh" { avatars::refresh(); mail::refresh_avatars(&u); mail::show_toast(&u, "Sender icons will be fetched again", false); }
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
                        u.set_c_to(to.into());
                        u.set_c_subject(subj.into());
                        let qhtml: String = orig.lines().map(|l| format!("<div>{}</div>", if l.is_empty() { "<br>".to_string() } else { rte::html::esc(l) })).collect();
                        let mut blocks = rte::html::import_text(text.trim_end());
                        blocks.push(rte::model::Block::para(""));
                        blocks.push(rte::model::Block::para(&format!("On {who} wrote:")));
                        blocks.push(rte::model::Block::new(rte::model::Kind::Quoted { html: qhtml, text: orig.clone(), open: false, cite: true }));
                        rte::set_doc(&u, rte::model::Doc { blocks });
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
        if u.get_ai_busy() { return; } // one rewrite at a time (toolbar and right-click menu)
        let (draft, whole) = rte::ai_source();
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
                        rte::ai_replace(&u, &text, whole);
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
        if UNDO.with(|x| x.borrow_mut().take()).is_some() { rte::undo(&u); }
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
    let (wt, stt) = (ui.as_weak(), state.clone());
    ui.on_trust_sender(move || {
        let u = wt.unwrap();
        let from = CUR_MSG.with(|x| x.borrow().as_ref().map(|(m, _)| m["from"][0]["email"].as_str().unwrap_or("").to_string())).unwrap_or_default();
        // this message's images load at once; the address is filed in the background (the
        // Trusted Senders address book, or this computer's list), see trustbook.rs
        let on = trustbook::setting_on(&settings::Settings::load().prefs);
        trustbook::trust_sender(wt.clone(), stt.lock().unwrap().clone(), from, on);
        CUR.with(|c| if let Some(c) = c.borrow_mut().as_mut() { c.allow = true; });
        render_current(&u);
    });

    ui.on_load_images(move || {
        let u = w.unwrap();
        CUR.with(|c| if let Some(c) = c.borrow_mut().as_mut() { c.allow = true; });
        render_current(&u);
    });

    let w = ui.as_weak();
    ui.on_rerender(move || render_current(&w.unwrap()));

    ui.on_lite_click(move |x, y| {
        if mailview::links_off() { return; }
        if let Some(href) = lite::link_at(x, y) {
            if mailto::parse(&href).is_some() { mailto::request(href); }
            else if href.starts_with("http://") || href.starts_with("https://") { oauth::open_browser(&href); }
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
            open_message: Some(u.get_current_mail().to_string()).filter(|s| !s.is_empty())
                .filter(|s| mail::shared_account(s).is_none() && mail::owner(s).is_none_or(|o| o == unified::active_user()))
                .map(|s| unified::real_id(&s).to_string()),
            open_subject: u.get_r_subject().to_string(),
        };
        hist.lock().unwrap().push(agent::Msg::User(prompt));
        push_chat(&u, "user", &text);
        u.set_chat_busy(true);
        can.store(false, std::sync::atomic::Ordering::SeqCst);
        let (w, hist, can) = (w.clone(), hist.clone(), can.clone());
        let gen = SESSION_GEN.load(std::sync::atomic::Ordering::SeqCst);
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
            // signed out or switched meanwhile: the answer belongs to an account that is gone
            if SESSION_GEN.load(std::sync::atomic::Ordering::SeqCst) == gen {
                let _ = std::fs::write(chat_path(), agent::to_json(&h).to_string());
                *hist.lock().unwrap() = h;
            }
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
        COMPOSE_CLIENT.with(|r| *r.borrow_mut() = None);
        compose::open(&w.unwrap(), c, compose::ComposeKind::New, None);
    });

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_reply(move || {
        let Some(c) = login_for(&st, &w.unwrap().get_current_mail()) else { return };
        COMPOSE_CLIENT.with(|r| *r.borrow_mut() = Some(c.clone()));
        let Some((m, _)) = CUR_MSG.with(|x| x.borrow().clone()) else { return };
        compose::open(&w.unwrap(), c, compose::ComposeKind::Reply, Some(&*m));
    });

    rte::wire(&ui);
    schedule::wire(&ui, state.clone(), prefs.clone());
    let w = ui.as_weak();
    ui.on_c_toggle_tag(move |i| compose::toggle_tag(&w.unwrap(), i));
    let w = ui.as_weak();
    ui.on_c_from_edit(move || compose::start_from_override(&w.unwrap()));
    let w = ui.as_weak();
    ui.on_toggle_tag_open(move |id| mail::toggle_tag_open(&w.unwrap(), &id));
    let w = ui.as_weak();
    ui.on_tag_new(move || ctxmenu::new_tag_prompt(&w.unwrap(), None));
    // the share of the pane a docked reply takes, once dragged (Bulwark replyDockShare)
    ui.set_dock_share(settings::Settings::load().prefs.get("replyDockShare").and_then(|v| v.as_f64()).filter(|f| *f > 0.1 && *f < 0.95).unwrap_or(-1.0) as f32);
    ui.on_dock_share_save(|share| {
        let v = (share > 0.1 && share < 0.95).then(|| json!((share as f64 * 1000.0).round() / 1000.0));
        settings::Settings::put("replyDockShare", v);
    });

    let (w, st, pf) = (ui.as_weak(), state.clone(), prefs.clone());
    ui.on_send_now(move || {
        let Some(c) = compose_client(&st) else { return };
        compose::send(&w.unwrap(), c, &pf.lock().unwrap());
    });

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_undo_toast(move || {
        // the toast of a phishing report has its own Undo; otherwise it is the send's
        if phishflow::undo(&w.unwrap()) { return; }
        let Some(c) = compose_client(&st) else { return };
        compose::undo_send(&w.unwrap(), c);
    });

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_attach_file(move || {
        let Some(c) = compose_client(&st) else { return };
        compose::attach(&w.unwrap(), c);
    });

    let w = ui.as_weak();
    ui.on_remove_attachment(move |i| compose::remove_attachment(&w.unwrap(), i));

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_save_draft(move |close| {
        let Some(c) = compose_client(&st) else { compose::note_save_failed(); return };
        compose::save_draft(&w.unwrap(), c, close);
    });

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_discard_draft(move || {
        let Some(c) = compose_client(&st) else { return };
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
    ui.on_cal_allday_toggle(move || calendar::toggle_allday(&w.unwrap()));
    let w = ui.as_weak();
    ui.on_cal_search(move |q| calendar::search(&w.unwrap(), &q));
    let w = ui.as_weak();
    ui.on_cal_edit(move |id, date, hour| calendar::edit(&w.unwrap(), &id, &date, hour));
    let w = ui.as_weak();
    ui.on_cal_select(move |id| calendar::select(&w.unwrap(), &id));
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_cal_rsvp(move |status| {
        let Some(c) = st.lock().unwrap().clone() else { return };
        calendar::rsvp_selected(&w.unwrap(), c, &status, &my_addrs(&st));
    });
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
    ui.on_ct_nav_select(move |kind, id| {
        let u = w.unwrap();
        let f = match kind.as_str() { "all" => String::new(), "uncat" => "uncat".into(), "keyword" => format!("kw:{id}"), k => format!("{k}:{id}") };
        contacts::set_filter(&u, &f);
        u.set_ct_editing(false);
        if kind == "group" { contacts::select_group(&u, &id); } else { u.set_ct_group("".into()); }
    });
    let w = ui.as_weak();
    ui.on_ct_new_group(move || ctxmenu::new_group_prompt(&w.unwrap(), None));
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_ct_select(move |id| {
        let u = w.unwrap();
        u.set_ct_editing(false);
        contacts::select(&u, &id);
        let email = u.get_ct_emails().split(',').next().unwrap_or("").trim().to_string();
        if let Some(c) = st.lock().unwrap().clone() { glance::contact(w.clone(), c, email); }
    });
    let w = ui.as_weak();
    ui.on_ct_new(move || { let u = w.unwrap(); contacts::select(&u, ""); u.set_ct_id("".into()); u.set_ct_editing(true); });
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_ct_save(move || { let Some(c) = st.lock().unwrap().clone() else { return }; contacts::save(&w.unwrap(), c); });
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_ct_delete(move || { let Some(c) = st.lock().unwrap().clone() else { return }; contacts::delete(&w.unwrap(), c); });
    contacts_x::wire(&ui, state.clone());
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_ct_email(move || {
        let Some(c) = st.lock().unwrap().clone() else { return };
        let u = w.unwrap();
        // a group mails every member; a contact its first address
        let to = if u.get_ct_group() != "" && u.get_ct_id() == "" { u.get_ct_emails().to_string() }
                 else { u.get_ct_emails().split(',').next().unwrap_or("").trim().to_string() };
        compose::open(&u, c, compose::ComposeKind::New, None);
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
    let w = ui.as_weak();
    ui.on_fl_toggle_folder(move |id| files::toggle_expand(&w.unwrap(), &id));
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_fl_rename(move |id, n| { let Some(c) = st.lock().unwrap().clone() else { return }; files::rename(&w.unwrap(), c, &id, &n); });
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_fl_delete(move |id| { let Some(c) = st.lock().unwrap().clone() else { return }; files::delete(&w.unwrap(), c, &id); });
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_fl_upload_folder(move || { let Some(c) = st.lock().unwrap().clone() else { return }; files::upload_folder(&w.unwrap(), c); });
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_fl_restore(move |id| { let Some(c) = st.lock().unwrap().clone() else { return }; files::restore(&w.unwrap(), c, &id); });
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_fl_empty_trash(move || { let Some(c) = st.lock().unwrap().clone() else { return }; files::empty_trash(&w.unwrap(), c); });

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_admin_load(move |sec| { let Some(c) = st.lock().unwrap().clone() else { return }; admin::load(&w.unwrap(), c, &sec); });
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_admin_delete(move |id| { let Some(c) = st.lock().unwrap().clone() else { return }; admin::delete_queued(&w.unwrap(), c, &id); });
    let w = ui.as_weak();
    ui.on_admin_action_open(move |id| admin::action_open(&w.unwrap(), &id));
    ui.on_admin_action_edit(move |k, v| admin::action_edit(&k, &v));
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_admin_action_execute(move || { let Some(c) = st.lock().unwrap().clone() else { return }; admin::action_execute(&w.unwrap(), c); });
    ui.set_admin_enabled(!admin::key().is_empty());

    // ---------------- keyboard shortcuts (Bulwark's single-key set) ----------------
    let (w, st, pf) = (ui.as_weak(), state.clone(), prefs.clone());
    ui.on_shortcut(move |text, shift, ctrl| {
        let u = w.unwrap();
        let t = text.as_str();
        // composer shortcuts
        if u.get_composing() {
            // Mod+Shift+Enter: Schedule send (Bulwark); Mod+Enter: send
            if ctrl && shift && (t == "\r" || t == "\n") { u.global::<CompX>().invoke_sched_open_compose(); return; }
            if ctrl && (t == "\r" || t == "\n") { u.invoke_send_now(); }
            if ctrl && t == "s" { u.invoke_save_draft(false); }
            // Esc in the link popover cancels the popover, not the whole message
            if t == "\u{1b}" && u.get_rte_link_open() { u.set_rte_link_open(false); rte::focus(&u); return; }
            if t == "\u{1b}" { u.invoke_save_draft(true); }
            return;
        }
        if ctrl {
            match t {
                "1" | "2" | "3" | "4" => { let a = t.parse::<i32>().unwrap() - 1; u.set_app(a); u.invoke_app_changed(a); }
                "n" => u.invoke_compose_new(),
                "a" => u.invoke_select_all(),
                "r" => u.invoke_act(if shift { "reply-all".into() } else { "reply".into() }),
                "u" => u.invoke_act("unread!".into()),
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
                if !unread_only || r.unread {
                    // a held key: row feedback snaps instead of trailing (Motion.rapid)
                    motion::step(&u);
                    u.set_current_mail(r.id.clone());
                    u.invoke_open_mail(r.id);
                    return;
                }
            }
        };
        let _ = (&st, &pf);
        // Settings > Accessibility > Single-key shortcuts (Ctrl/Alt ones keep working)
        let single = t.chars().count() == 1 && t.chars().next().is_some_and(|ch| ch.is_ascii_graphic());
        if single && !a11y::A11y::load().single_keys { return; }
        match t {
            "j" => move_sel(1, false),
            "k" => move_sel(-1, false),
            "\u{F701}" => move_sel(1, false),   // down arrow
            "\u{F700}" => move_sel(-1, false),  // up arrow
            "n" => move_sel(1, true),
            "\u{1b}" => u.set_current_mail("".into()),
            _ if shortcuts::mail_key_action(t).is_some() => u.invoke_act(shortcuts::mail_key_action(t).unwrap_or_default().into()),
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
        if text.trim().is_empty() { compose::open(&u, c, compose::ComposeKind::Reply, Some(&*m)); return; }
        compose::open(&u, c.clone(), compose::ComposeKind::Reply, Some(&*m));
        rte::with(|e| { e.set_caret(rte::model::Pos::new(0, 0)); e.insert_text(&text); });
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
        search_ui::refresh_chips(&u);
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
        search_ui::refresh_chips(&u);
        u.invoke_select_folder(u.get_current_folder());
    });

    // templates in the composer
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_tpl_refresh(move || {
        let Some(c) = compose_client(&st) else { return };
        let w = w.clone();
        // the list on show belongs to another account (or none yet): empty it until this one's arrives
        let key = format!("{}|{}", c.username, c.account);
        if TEMPLATES.with(|t| t.borrow().0 != key) {
            TEMPLATES.with(|t| *t.borrow_mut() = (String::new(), Vec::new()));
            if let Some(u) = w.upgrade() { u.set_tpl_names(ModelRc::new(VecModel::from(vec![slint::SharedString::from("Insert template…")]))); u.set_tpl_index(0); }
        }
        if let Some(u) = w.upgrade() { if TEMPLATES.with(|t| t.borrow().0 != key) { u.set_tpl_note("Loading…".into()); } }
        let st = st.clone();
        std::thread::spawn(move || {
            let list = match templates::list(&c) { Ok(l) => l, Err(e) => { let _ = w.upgrade_in_event_loop(move |u| u.set_tpl_note(format!("Could not load templates: {e}").into())); return; } };
            let _ = w.upgrade_in_event_loop(move |u| {
                // results can arrive out of order: only the composing account's list is kept
                if compose_client(&st).map(|c| format!("{}|{}", c.username, c.account)).as_deref() != Some(key.as_str()) { return; }
                u.set_tpl_note("".into());
                TEMPLATES.with(|t| *t.borrow_mut() = (key, list.clone()));
                let mut names: Vec<slint::SharedString> = vec!["Insert template…".into()];
                names.extend(list.iter().map(|t| t["name"].as_str().unwrap_or("(unnamed)").into()));
                u.set_tpl_names(ModelRc::new(VecModel::from(names)));
                u.set_tpl_index(0);
            });
        });
    });
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_tpl_insert(move |i| {
        let u = w.unwrap();
        if i <= 0 { return; }
        // only a list loaded for the account the draft is written in
        let key = compose_client(&st).map(|c| format!("{}|{}", c.username, c.account)).unwrap_or_default();
        let Some(t) = TEMPLATES.with(|x| { let x = x.borrow(); if x.0 == key { x.1.get(i as usize - 1).cloned() } else { None } }) else { u.set_tpl_index(0); return };
        u.set_tpl_index(0);
        tplflow::insert(&u, t);
    });
    tplflow::wire(&ui, state.clone());

    // ---------------- mail actions ----------------
    let w = ui.as_weak();
    ui.on_toggle_folder(move |id| mail::toggle_folder(&w.unwrap(), &id));

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_select_tag(move |id| {
        let Some(c) = st.lock().unwrap().clone() else { return };
        let u = w.unwrap();
        u.set_current_mail("".into());
        glance::title(&u);
        mail::load_list(w.clone(), c, json!({"hasKeyword": format!("$label:{id}")}));
    });

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_row_action(move |id, a| {
        apply_routed(&w.unwrap(), &st, vec![id.to_string()], &a);
    });

    let w = ui.as_weak();
    ui.on_toggle_check(move |id| mail::toggle_check(&w.unwrap(), &id));
    let w = ui.as_weak();
    ui.on_select_all(move || mail::select_all(&w.unwrap()));

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_bulk(move |a| {
        let u = w.unwrap();
        if a == "clear" { mail::select_all(&u); return; }
        apply_routed(&u, &st, mail::checked_ids(), &a);
    });

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_load_more(move || {
        let Some(c) = st.lock().unwrap().clone() else { return };
        mail::load_more(w.clone(), c);
    });

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_move_to(move |fid| {
        let u = w.unwrap();
        let ids = { let ch = mail::checked_ids(); if ch.is_empty() { vec![u.get_current_mail().to_string()] } else { ch } };
        move_ids(&u, &st, ids, &fid);
    });

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_tag_toggle(move |tid| {
        let u = w.unwrap();
        let id = u.get_current_mail().to_string();
        let ids = { let ch = mail::checked_ids(); if ch.is_empty() { vec![id.clone()] } else { ch } };
        if ids.iter().all(|i| i.is_empty()) { return; }
        let has = mail::email(&ids[0]).is_some_and(|m| m["keywords"][format!("$label:{tid}")].as_bool() == Some(true));
        apply_routed(&u, &st, ids, &format!("{}:{tid}", if has { "untag" } else { "tag" }));
    });

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_act(move |a| {
        let u = w.unwrap();
        let id = u.get_current_mail().to_string();
        if id.is_empty() { return; }
        match a.as_str() {
            "reply" => u.invoke_reply(),
            "reply-all" | "forward" => {
                let Some(c) = login_for(&st, &id) else { return };
                COMPOSE_CLIENT.with(|r| *r.borrow_mut() = Some(c.clone()));
                let Some((msg, _)) = CUR_MSG.with(|x| x.borrow().clone()) else { return };
                let kind = if a == "forward" { compose::ComposeKind::Forward } else { compose::ComposeKind::ReplyAll };
                compose::open(&u, c, kind, Some(&*msg));
            }
            "print" => {
                let printed = u.get_render_mode() == 0 && mailview::print();
                if !printed {
                    if let Some(h) = CUR.with(|c| c.borrow().as_ref().map(|c| c.html.clone().unwrap_or_else(|| format!("<pre>{}</pre>", esc(&c.text))))) {
                        let p = platform::print_file();
                        let page = wrap_html_opts(&format!("<h2>{}</h2>{h}<script>print()</script>", esc(&u.get_r_subject())), false)
                            .replace("script-src 'none'", "script-src 'unsafe-inline'");
                        let _ = std::fs::write(&p, page);
                        oauth::open_browser(&p.to_string_lossy());
                    }
                }
            }
            a if a.starts_with("rx:") => reader::pick(&u, &st, a, ""),
            other => {
                apply_routed(&u, &st, vec![id], other);
            }
        }
    });

    let (st, wa) = (state.clone(), ui.as_weak());
    ui.on_open_attachment(move |i| {
        // an attached message (message/rfc822, .eml) opens in the reader through Email/parse
        if let (Some(u), Some(c), Some(a)) = (wa.upgrade(), reader_client(&st), CUR_MSG.with(|x| x.borrow().as_ref().and_then(|(_, atts)| atts.get(i as usize).cloned()))) {
            // (a file decoded from a winmail.dat has no blob of its own: it opens like any attachment)
            if reader::is_message_attachment(&a) && a["blobId"].is_string() { reader::open_eml(&u, c, a); return; }
        }
        // Bulwark mailAttachmentAction: preview (open) or download (save)
        if settings::Settings::load().str("mailAttachmentAction", "preview") == "download" {
            if let Some(u) = wa.upgrade() { u.invoke_save_attachment(i); }
            return;
        }
        let Some(c) = reader_client(&st) else { return };
        let Some(a) = CUR_MSG.with(|x| x.borrow().as_ref().and_then(|(_, atts)| atts.get(i as usize).cloned())) else { return };
        std::thread::spawn(move || {
            let name = attachments::save_name(&a);
            if let Ok(bytes) = tnef::fetch(&c, &a) {
                let dir = platform::mail_temp_dir();
                let _ = std::fs::create_dir_all(&dir);
                let p = dir.join(&name);
                if std::fs::write(&p, bytes).is_ok() { oauth::open_browser(&p.to_string_lossy()); }
            }
        });
    });

    let w = ui.as_weak();
    let st_req = state.clone();
    ui.on_ctx_request(move |k, id| { ctxmenu::set_mine(my_addrs(&st_req)); ctxmenu::set_client(st_req.lock().unwrap().clone()); ctxmenu::request(&w.unwrap(), &k, &id) });
    let w = ui.as_weak();
    ui.on_ctx_hover(move |a, i| ctxmenu::hover(&w.unwrap(), &a, i));
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_ctx_pick(move |a| ctxmenu::pick(&w.unwrap(), &st, &a));
    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_prompt_ok(move |t| ctxmenu::prompt_ok(&w.unwrap(), &st, &t));
    reader::wire(&ui, state.clone());

    let (w, st) = (ui.as_weak(), state.clone());
    ui.on_save_attachment(move |i| {
        let Some(c) = st.lock().unwrap().clone() else { return };
        let Some(a) = CUR_MSG.with(|x| x.borrow().as_ref().and_then(|(_, atts)| atts.get(i as usize).cloned())) else { return };
        let name = attachments::save_name(&a);
        let Some(path) = rfd::FileDialog::new().set_file_name(&name).save_file() else { return };
        let w = w.clone();
        std::thread::spawn(move || {
            let res = tnef::fetch(&c, &a)
                .and_then(|b| std::fs::write(&path, b).map_err(|e| e.to_string()));
            let msg = match res { Ok(()) => format!("Saved {name}"), Err(e) => format!("Could not save: {e}") };
            let _ = w.upgrade_in_event_loop(move |u| mail::show_toast(&u, &msg, false));
        });
    });

    // ---------------- productivity: shortcuts, command palette, search, drag and drop ----------------
    palette::wire(&ui, state.clone());
    search_ui::wire(&ui);
    dnd::wire(&ui, state.clone());
    let (w, st) = (ui.as_weak(), state.clone());
    ui.global::<Pd>().on_key_hook(move |t, shift, ctrl, alt| {
        let Some(u) = w.upgrade() else { return false };
        shortcuts::handle(&u, &st, &t, shift, ctrl, alt)
    });
    let w = ui.as_weak();
    ui.global::<Pd>().on_keys_set_tab(move |i| { if let Some(u) = w.upgrade() { shortcuts::show_help(&u, i.max(0) as usize); } });
    let w = ui.as_weak();
    ui.global::<Pd>().on_refocus(move || { if let Some(u) = w.upgrade() { u.invoke_focus_keys(); } });

    mailview::install_focus_hook(&ui);
    // "Start in the tray" at sign-in leaves the window closed
    // the window comes back where it was (before it is shown, so it never jumps)
    winstate::restore(&ui);
    if !desktop::start_hidden() { ui.show()?; }
    if last_start_failed { mail::show_toast(&ui, crashguard::NOTICE, false); }
    // This start was a good one once the first list has been rendered (mail.rs rebuild_list), or
    // here when the login form is what is shown (nothing remembered to sign in with), or on a
    // clean quit; the timer is only the fallback (see crashguard.rs)
    if oauth::remembered().is_none() { crashguard::done(&config_dir()); }
    let guard_timer = slint::Timer::default();
    guard_timer.start(slint::TimerMode::SingleShot, crashguard::UP_FOR, || crashguard::done(&config_dir()));
    if automation::enabled() {
        ui.window().set_position(slint::PhysicalPosition::new(-6000, 0));
    }
    // the calendar's current-time line
    let set_now = |u: &AppWindow| { use chrono::Timelike; let n = chrono::Local::now(); u.set_now_minutes((n.hour() * 60 + n.minute()) as i32); };
    set_now(&ui);
    let w = ui.as_weak();
    let now_timer = slint::Timer::default();
    now_timer.start(slint::TimerMode::Repeated, std::time::Duration::from_secs(30), move || {
        if let Some(u) = w.upgrade() { set_now(&u); }
    });
    let w = ui.as_weak();
    ui.on_webview_sync(move || { if let Some(u) = w.upgrade() { mailview::sync(&u); } });
    let w = ui.as_weak();
    let timer = slint::Timer::default();
    timer.start(slint::TimerMode::Repeated, std::time::Duration::from_millis(50), move || {
        if let Some(u) = w.upgrade() { sync_webview(&u); }
    });
    // Bulwark unreadBadgeAllAccounts: the other signed-in accounts' inbox counts for the
    // title badge, refreshed now and then (the active account's come with its push)
    let w = ui.as_weak();
    let badge_timer = slint::Timer::default();
    badge_timer.start(slint::TimerMode::Repeated, std::time::Duration::from_secs(120), move || {
        if w.upgrade().is_some_and(|u| u.get_logged_in()) { unified::refresh_other_logins(w.clone(), false); }
    });
    // the window can be closed to the tray: the loop runs until Quit
    slint::run_event_loop_until_quit()?;
    winstate::save(&ui);
    crashguard::done(&config_dir());
    settings::Settings::flush();
    instance::cleanup();
    // No sign-in is written back here: every rotated refresh token is stored the moment it is
    // issued (oauth::refresh, under the cross-process lock). Writing this instance's copy at exit
    // could overwrite a newer one another instance stored since.
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
            rte::set_doc(&u, rte::model::Doc { blocks: rte::html::import_text(&body) });
            u.set_c_status("".into());
            u.set_can_undo(false);
            u.set_composing(true);
        });
    }
    fn changed(&self) {
        let (w, c) = (self.w.clone(), self.client.clone());
        load_mailboxes(w.clone(), c.clone(), false);
        let _ = self.w.upgrade_in_event_loop(move |u| {
            // settings the assistant changed take effect right away
            apply_theme(&u);
            mail::rebuild_list(&u);
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
/// `origin`: a new sign-in, or a session restored from the store (see `oauth::Origin`).
fn remember_session(c: &jmap::Client, mut v: Value, server: &str, user: &str, origin: oauth::Origin) {
    v["server"] = serde_json::json!(server);
    v["user"] = serde_json::json!(user);
    if v["kind"] == "oauth" {
        c.set_token_user(user);
        let Some(t) = c.tokens() else { return };
        if t.refresh.is_none() { return; }
        v["refresh"] = serde_json::json!(t.refresh);
        v["endpoint"] = serde_json::json!(t.token_endpoint);
        v["client"] = serde_json::json!(t.client_id);
    }
    oauth::remember(&v, origin);
}

fn accounts_model() -> ModelRc<SharedString> {
    ModelRc::new(VecModel::from(oauth::accounts().iter().filter_map(|a| a["user"].as_str().map(SharedString::from)).collect::<Vec<_>>()))
}

use jmap::auth_rejected;

/// The stored session a start-up restore could not use for lack of a network: "Try again" runs it again.
static RESTORE_PENDING: Mutex<Option<Value>> = Mutex::new(None);

/// Sign in with a remembered session (startup and account switching).
/// `switching`: an account switch from a running session, which a failure must not end.
/// `source`: at start-up, where the stored entry came from (for the one diagnostic line).
/// A start-up failure that is not the server turning the sign-in down is retried with backoff
/// (`restore::restore_outcome`) and then shown with "Try again"; whatever happens the stored
/// entry stays until the user signs in again or out.
fn restore_session(w: slint::Weak<AppWindow>, st: Shared, v: Value, switching: bool, source: Option<secrets::Source>) {
    let gen = SESSION_GEN.load(std::sync::atomic::Ordering::SeqCst);
    std::thread::spawn(move || {
        let server = v["server"].as_str().unwrap_or("").to_string();
        let user = v["user"].as_str().unwrap_or("").to_string();
        let kind = v["kind"].as_str().unwrap_or("").to_string();
        // the stored value the current attempt uses: a retry starts from the store as it is
        // then, because an attempt that refreshed and failed afterwards has rotated the token
        let mut v = v;
        let mut attempt = 0;
        let res = loop {
            let res = if v["kind"] == "oauth" {
                let t = oauth::Tokens {
                    access: String::new(),
                    refresh: v["refresh"].as_str().map(String::from),
                    expires: None,
                    token_endpoint: v["endpoint"].as_str().unwrap_or("").into(),
                    client_id: v["client"].as_str().unwrap_or("").into(),
                    user: user.clone(),
                };
                jmap::Client::connect_oauth(&server, t)
            } else {
                jmap::Client::connect(&server, &user, v["pass"].as_str().unwrap_or(""), "")
            };
            let e = match res {
                Ok(c) => break Ok(c),
                Err(e) => e,
            };
            if switching {
                let rejected = auth_rejected(&e);
                break Err((if rejected { restore::turned_down(&restore::who(&user, &server), &e) } else { e }, rejected));
            }
            match restore::restore_outcome(&e, attempt, &user, &server) {
                restore::RestoreOutcome::Retry(d) => {
                    std::thread::sleep(d);
                    // another sign-in or sign-out meanwhile: this one is over
                    if SESSION_GEN.load(std::sync::atomic::Ordering::SeqCst) != gen { return; }
                    v = restore::retry_value(&v, &oauth::remembered_status());
                    attempt += 1;
                }
                restore::RestoreOutcome::ShowForm(msg) => break Err((msg, auth_rejected(&e))),
            }
        };
        if let Some(source) = source {
            let outcome = match &res { Ok(_) => "ok", Err((_, true)) => "rejected", Err(_) => "transient" };
            restore::log(&restore::line(source.label(), &kind, user.is_empty(), outcome));
        }
        let res = match res {
            Ok(c) => Ok(c),
            Err((msg, rejected)) => {
                // never drop stored credentials over a timeout or a server hiccup; when switching,
                // the remembered session belongs to the account still in use; an account the
                // server turned down is only marked, the stored entry stays until the user signs
                // out or signs in again
                if switching {
                    let who = restore::who(&user, &server);
                    // a turned-down message already names the account
                    let msg = if rejected { format!("Could not switch. {msg}") } else { format!("Could not switch to {who}: {msg}") };
                    let _ = w.upgrade_in_event_loop(move |u| {
                        desktop::drop_pending();
                        u.set_busy(false);
                        u.set_accounts(accounts_model());
                        mail::show_toast(&u, &msg, false);
                    });
                    return;
                }
                // start-up: the form with the reason, and "Try again" when the server was not asked
                if !rejected { *RESTORE_PENDING.lock().unwrap() = Some(v.clone()); }
                let _ = w.upgrade_in_event_loop(move |u| {
                    u.set_busy(false);
                    u.set_status(msg.into());
                    u.set_retry_available(!rejected);
                });
                return;
            }
        };
        let origin = oauth::Origin::Restore { read: v["refresh"].as_str().map(String::from) };
        finish_login(w, st, res, server, user, Some(serde_json::json!({"kind": kind, "pass": v["pass"]})), origin);
    });
}

fn finish_login(w: slint::Weak<AppWindow>, st: Shared, res: Result<jmap::Client, String>, server: String, user: String, remember: Option<Value>, origin: oauth::Origin) {
    match res {
        Ok(c) => {
            // an SSO sign-in may leave the user blank: the server's own name for the login then
            // ties the remembered session (and its rotated tokens) to one account entry
            let user = if user.trim().is_empty() { c.username.clone() } else { user };
            let c = Arc::new(c);
            *RESTORE_PENDING.lock().unwrap() = None;
            *st.lock().unwrap() = Some(c.clone());
            // the new session starts now: a "Trust sender" click from here on is this account's
            trustbook::begin(&c);
            unified::set_active(&user, c.clone());
            if unified::enabled() { unified::warm_up(Some(w.clone())); }
            let p = config_path();
            let _ = std::fs::create_dir_all(p.parent().unwrap());
            let _ = std::fs::write(&p, format!("{server}\n{user}\n"));
            if let Some(v) = remember { remember_session(&c, v, &server, &user, origin); }
            let name = c.username.clone();
            *CHAT_USER.lock().unwrap() = name.clone();
            *LOGIN_USER.lock().unwrap() = user.clone();
            // host of the server URL for the admin nav subtitle
            let host = restore::server_host(&server);
            let w2 = w.clone();
            let _ = w.upgrade_in_event_loop(move |u| {
                u.invoke_chat_reload();
                u.set_accounts(accounts_model());
                u.set_busy(false);
                u.set_retry_available(false);
                u.set_status("".into());
                // the password is not kept in the form once it has been used
                u.set_login_password("".into());
                u.set_login_otp("".into());
                u.set_account(name.clone().into());
                u.set_initials(initials(&name).into());
                u.set_server_host(host.into());
                u.set_logged_in(true);
            });
            load_mailboxes(w2.clone(), c.clone(), true);
            {
                let (w3, c3) = (w2.clone(), c.clone());
                std::thread::spawn(move || {
                    let ids = c3.identities().unwrap_or_default();
                    let addrs: Vec<String> = ids.iter().filter_map(|i| i["email"].as_str().map(String::from)).collect();
                    let _ = w3.upgrade_in_event_loop(move |u| { MY_ADDRS.with(|a| *a.borrow_mut() = addrs); glance::set_name(&u, &ids); });
                });
            }
            // today's events and open tasks for the mail screen's summary tiles
            calendar::load(w2.clone(), c.clone());
            check_new_mail(c.clone(), None);
            {
                // calendar reminders for this login (Bulwark useCalendarAlerts)
                let c4 = c.clone();
                let _ = w2.upgrade_in_event_loop(move |u| reminders::start(&u, c4));
            }
            // the trusted senders address book (reads it; files this computer's list into it)
            trustbook::refresh(w2.clone(), c.clone(), true);
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


/// Bulwark's Appearance > Light or dark: "light" | "dark" | "system" (the system's app theme).
pub fn apply_theme(ui: &AppWindow) {
    let pref = settings::Settings::load().str("theme", "dark");
    let dark = match pref.as_str() {
        "light" => false,
        // Windows app theme or the XDG portal colour scheme (platform layer)
        "system" => platform::system_dark().unwrap_or(true),
        _ => true,
    };
    set_theme_dark(ui, dark);
}

/// Switch the app between dark and light (in memory; `apply_theme` reads the setting).
pub fn set_theme_dark(ui: &AppWindow, dark: bool) {
    // the email view paints this colour before a message loads; keep it in step with the
    // theme so opening a message does not flash white
    let changed = DARK.with(|d| d.replace(dark)) != dark;
    WV.with(|w| if let Some(v) = &*w.borrow() { let _ = v.set_background_color(wv_bg()); });
    ui.global::<Theme>().set_dark(dark);
    // the open message's page follows (plain text and the reader are themed); swapped in, no reload
    if changed && ui.get_current_mail() != "" { render_current(ui); }
    if changed && ui.get_current_mail() == "" { ui.set_body_bg(mailview::color(mailview::reader_bg())); mailview::theme_changed(); }
    // Settings > Appearance > Style: Bento (tiles) or Nordic (one surface)
    ui.global::<Theme>().set_style(if settings::Settings::load().str("uiStyle", "bento") == "nordic" { 1 } else { 0 });
    search_ui::set_accent(ui.global::<Theme>().get_accent());
}


static CHAT_USER: Mutex<String> = Mutex::new(String::new());
/// The user name as typed on the login form (the key of the account switcher's list).
static LOGIN_USER: Mutex<String> = Mutex::new(String::new());

/// The assistant's chat history is kept per account (chat-<account>.json).
fn chat_path() -> std::path::PathBuf {
    config_dir().join(chat_file(&CHAT_USER.lock().unwrap()))
}

/// The chat file's name for an account (signing out removes it).
pub(crate) fn chat_file(user: &str) -> String {
    let safe: String = user.chars().map(|c| if c.is_alphanumeric() || c == '.' || c == '@' || c == '-' { c } else { '_' }).collect();
    if safe.is_empty() { "chat.json".to_string() } else { format!("chat-{safe}.json") }
}


/// Settings that change how the native UI looks (Bulwark: fontSize, quickReplyEnabled,
/// showToolbarLabels, showTagChips, calendarDayStartHour).
pub fn apply_ui_prefs(ui: &AppWindow) {
    let p = settings::Settings::load();
    ui.set_pref_quick_reply(p.bool("quickReplyEnabled", true));
    ui.global::<Prefs>().set_toolbar_labels(p.bool("showToolbarLabels", true));
    ui.global::<Prefs>().set_tag_chips(p.bool("showTagChips", true));
    let a = a11y::A11y::load();
    // Bulwark FONT_SIZE_SCALE on the 13px base
    ui.global::<Prefs>().set_font_size(13.0 * a.font_scale);
    ui.global::<Theme>().set_more_contrast(a.more_contrast);
    search_ui::set_accent(ui.global::<Theme>().get_accent());
    ui.global::<Prefs>().set_reduce_motion(a.reduce_motion);
    // Motion tokens: every duration 0 with reduce motion (or Windows animations off on "System")
    motion::apply(ui, &p.str("reduceMotion", "system"), a.reduce_motion);
    ui.global::<Prefs>().set_large_targets(a.large_targets);
    // Bulwark focus outline widths: normal 2px, thick 3px, extra 5px
    ui.global::<Prefs>().set_focus_ring([0.0, 2.0, 3.0, 5.0][a.focus_ring.clamp(0, 3) as usize]);
    ui.global::<Prefs>().set_row_action(a.show_row_action);
    // Bulwark hoverActionsMode (inline | floating) and hoverActionsCorner
    let corner = p.str("hoverActionsCorner", "top-right");
    ui.global::<Prefs>().set_hover_floating(p.str("hoverActionsMode", "inline") == "floating");
    ui.global::<Prefs>().set_hover_left(corner.ends_with("left"));
    ui.global::<Prefs>().set_hover_bottom(corner.starts_with("bottom"));
    ui.set_pref_day_start(p.int("calendarDayStartHour", 8).clamp(0, 23) as i32);
    // Bulwark hoverActions (delete, star, markRead, archive, tag, spam), in the user's order
    let hover: Vec<slint::SharedString> = match p.prefs.get("hoverActions").and_then(|v| v.as_array()) {
        Some(a) => a.iter().filter_map(|x| x.as_str()).filter_map(|x| match x {
            "delete" => Some("delete"), "star" => Some("star"), "markRead" => Some("read"), "archive" => Some("archive"), "spam" => Some("spam"), _ => None,
        }).map(slint::SharedString::from).collect(),
        None => ["delete", "star", "read", "archive"].into_iter().map(slint::SharedString::from).collect(),
    };
    ui.global::<Prefs>().set_hover_actions(slint::ModelRc::new(slint::VecModel::from(hover)));
    // the open message picks up reading and accessibility changes
    if ui.get_current_mail() != "" { render_current(ui); }
    calendar::render(ui);
    contacts::render(ui);
}
