//! Reader actions and banners (Bulwark email-viewer.tsx and friends): view source, the
//! List-Unsubscribe banner (RFC 2369 / RFC 8058 one-click), read receipts (lib/mdn.ts, the
//! `readReceiptResponse` setting), forward as attachment, rules from a message
//! (quick-rules.ts), save an attachment to Files, download all attachments as a zip, calendar
//! invitations (CalendarEvent/parse), attached messages (Email/parse) and the notice for
//! bodies the server cut off (`isTruncated`).
//!
//! The banners live in `RX.banners` (ui/reader.slint); main.rs calls `clear` when a message
//! starts loading and `loaded` once it is shown. Menus come through ctxmenu.rs (kind "reader",
//! the attachment menu) and `act` (actions "rx:*" from the list's More menu).
use crate::invite::{self, Method};
use crate::jmap::Client;
use crate::{mail, AppWindow, CtxItem, RxAction, RxBannerData, Shared, RX};
use serde_json::{json, Value};
use slint::{ComponentHandle, Model, ModelRc, VecModel};
use std::cell::RefCell;
use std::sync::Arc;

/// Bulwark SOURCE_DISPLAY_LIMIT: more is cut in the view (the whole message is one Save away).
pub const SOURCE_DISPLAY_LIMIT: usize = 2 * 1024 * 1024;
const FILES_CAP: &str = "urn:ietf:params:jmap:filenode";
const SIEVE_CAP: &str = "urn:ietf:params:jmap:sieve";

// ---------------------------------------------------------------- large pages

/// Pages up to this size go to the WebView as a string (NavigateToString's limit is 2 MB).
pub const INLINE_HTML_MAX: usize = 1_500_000;

/// Write a large reader page to this process's own file in the temp folder and return its
/// file:// URL (the page keeps its CSP; one file per process, overwritten by the next page).
pub fn page_file(html: &str) -> Result<String, String> {
    let dir = std::env::temp_dir().join("Barbican");
    std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    let path = dir.join(format!("reader-{}.html", std::process::id()));
    std::fs::write(&path, html).map_err(|e| e.to_string())?;
    Ok(file_url(&path.to_string_lossy()))
}

/// A file:// URL of a local path (Windows separators turned, unsafe characters escaped).
pub fn file_url(path: &str) -> String {
    let p = path.replace('\\', "/");
    let enc: String = p.chars().map(|c| match c {
        ' ' => "%20".to_string(), '#' => "%23".to_string(), '%' => "%25".to_string(), '?' => "%3F".to_string(),
        c => c.to_string(),
    }).collect();
    if enc.starts_with('/') { format!("file://{enc}") } else { format!("file:///{enc}") }
}

// ---------------------------------------------------------------- headers

/// The first header `name` (case-insensitive) of a JMAP `headers` list, unfolded and trimmed.
pub fn header(headers: &[Value], name: &str) -> Option<String> {
    headers.iter().find(|h| h["name"].as_str().is_some_and(|n| n.eq_ignore_ascii_case(name)))
        .and_then(|h| h["value"].as_str()).map(unfold)
}

/// Every header `name`, unfolded.
pub fn headers_all(headers: &[Value], name: &str) -> Vec<String> {
    headers.iter().filter(|h| h["name"].as_str().is_some_and(|n| n.eq_ignore_ascii_case(name)))
        .filter_map(|h| h["value"].as_str()).map(unfold).collect()
}

fn unfold(v: &str) -> String {
    let mut out = String::new();
    let mut it = v.chars().peekable();
    while let Some(c) = it.next() {
        if c == '\r' { continue; }
        if c == '\n' { while it.peek().is_some_and(|c| *c == ' ' || *c == '\t') { it.next(); } out.push(' '); continue; }
        out.push(c);
    }
    out.trim().to_string()
}

// ---------------------------------------------------------------- unsubscribe

/// The List-Unsubscribe choices of a message (Bulwark parseUnsubscribeUrls) and whether the
/// sender offers RFC 8058 one-click.
#[derive(Clone, Debug, PartialEq, Default)]
pub struct Unsub {
    pub http: Option<String>,
    pub mailto: Option<String>,
    pub one_click: bool,
}

impl Unsub {
    /// Bulwark prefers the web link, else the mail.
    pub fn prefers_http(&self) -> bool { self.http.is_some() }
}

/// A plain address check (Bulwark isValidEmail): one @, no spaces, a dotted domain.
pub fn valid_email(s: &str) -> bool {
    let s = s.trim();
    let Some((local, domain)) = s.split_once('@') else { return false };
    !local.is_empty() && !domain.is_empty() && !domain.contains('@') && domain.contains('.') && !domain.starts_with('.')
        && !domain.ends_with('.') && !domain.contains("..") && !s.chars().any(|c| c.is_whitespace() || c.is_control() || "<>(),;:\"[]\\".contains(c))
}

/// Only http, https and mailto with a valid address (Bulwark isValidUnsubscribeUrl).
pub fn valid_unsub_url(u: &str) -> bool {
    let u = u.trim();
    if let Some(rest) = u.strip_prefix("mailto:") { return valid_email(&pct_decode(rest.split('?').next().unwrap_or(""))); }
    let lower = u.to_ascii_lowercase();
    let host_ok = |r: &str| r.split(['/', '?', '#']).next().is_some_and(|h| !h.is_empty() && !h.contains(char::is_whitespace));
    lower.strip_prefix("https://").or(lower.strip_prefix("http://")).is_some_and(host_ok)
}

/// Bulwark parseUnsubscribeUrls plus RFC 8058: `post` is List-Unsubscribe-Post.
pub fn parse_unsubscribe(header: &str, post: Option<&str>) -> Option<Unsub> {
    let mut urls = vec![];
    let mut rest = header;
    while let Some(a) = rest.find('<') {
        let Some(b) = rest[a..].find('>') else { break };
        urls.push(rest[a + 1..a + b].trim().to_string());
        rest = &rest[a + b + 1..];
    }
    let http = urls.iter().find(|u| { let l = u.to_ascii_lowercase(); (l.starts_with("http://") || l.starts_with("https://")) && valid_unsub_url(u) }).cloned();
    let mailto = urls.iter().find(|u| u.starts_with("mailto:") && valid_unsub_url(u) && unsubscribe_mailto(u).is_some()).cloned();
    if http.is_none() && mailto.is_none() { return None; }
    let one_click = http.as_deref().is_some_and(|h| h.to_ascii_lowercase().starts_with("https://"))
        && post.is_some_and(|p| p.split(',').any(|x| x.trim().eq_ignore_ascii_case("List-Unsubscribe=One-Click")));
    Some(Unsub { http, mailto, one_click })
}

/// RFC 6068 percent-decoding (a "+" stays a plus); invalid UTF-8 is replaced.
pub fn pct_decode(s: &str) -> String {
    let b = s.as_bytes();
    let mut out = Vec::with_capacity(b.len());
    let mut i = 0;
    let hex = |c: u8| (c as char).to_digit(16).map(|d| d as u8);
    while i < b.len() {
        if b[i] == b'%' && i + 2 < b.len() {
            if let (Some(h), Some(l)) = (hex(b[i + 1]), hex(b[i + 2])) { out.push(h * 16 + l); i += 3; continue; }
        }
        out.push(b[i]);
        i += 1;
    }
    String::from_utf8_lossy(&out).to_string()
}

/// A List-Unsubscribe mailto: exactly one recipient from the address part, a one-line subject
/// (200 chars) and a short body (500 chars) (Bulwark parseUnsubscribeMailto).
pub fn unsubscribe_mailto(url: &str) -> Option<(String, Option<String>, Option<String>)> {
    let rest = url.strip_prefix("mailto:")?;
    let (addr, query) = match rest.split_once('?') { Some((a, q)) => (a, q), None => (rest, "") };
    let addrs: Vec<&str> = addr.split(',').filter(|a| !a.trim().is_empty()).collect();
    if addrs.len() != 1 { return None; }
    let to = pct_decode(addrs[0]).trim().to_string();
    if !valid_email(&to) { return None; }
    let (mut subject, mut body) = (None, None);
    for pair in query.split('&') {
        let Some((k, v)) = pair.split_once('=') else { continue };
        match k.to_ascii_lowercase().as_str() {
            "subject" => subject = Some(pct_decode(v)),
            "body" => body = Some(pct_decode(v)),
            _ => {}
        }
    }
    let subject = subject.map(|s| s.split(['\r', '\n']).filter(|x| !x.is_empty()).collect::<Vec<_>>().join(" ").trim().chars().take(200).collect::<String>()).filter(|s| !s.is_empty());
    let body = body.map(|b| b.chars().take(500).collect::<String>()).filter(|b| !b.is_empty());
    Some((to, subject, body))
}

/// RFC 8058 §4 wants a DKIM signature over the list headers before a one-click POST: the
/// receiving server's (topmost) Authentication-Results must report dkim=pass.
pub fn dkim_pass(headers: &[Value]) -> bool {
    headers_all(headers, "Authentication-Results").first().is_some_and(|ar| {
        ar.to_ascii_lowercase().split(';').any(|p| p.trim().starts_with("dkim=pass"))
    })
}

/// The RFC 8058 POST: `List-Unsubscribe=One-Click` as a form body, no cookies or credentials.
pub fn one_click_post(url: &str) -> Result<(), String> {
    if !url.to_ascii_lowercase().starts_with("https://") { return Err("One-click unsubscribe needs an https link.".into()); }
    post_form(url)
}

fn post_form(url: &str) -> Result<(), String> {
    let http = reqwest::blocking::Client::builder()
        .redirect(reqwest::redirect::Policy::limited(5))
        .timeout(std::time::Duration::from_secs(20))
        .user_agent("Barbican")
        .build().map_err(|e| e.to_string())?;
    let r = http.post(url).header("Content-Type", "application/x-www-form-urlencoded").body("List-Unsubscribe=One-Click").send().map_err(|e| e.to_string())?;
    if r.status().is_success() { Ok(()) } else { Err(format!("The sender's server answered {}.", r.status())) }
}

// ---------------------------------------------------------------- names

/// The subject without Re:/Fwd:/AW:/WG: prefixes (Bulwark stripSubjectPrefixes).
pub fn strip_subject_prefixes(s: &str) -> String {
    let mut s = s.trim().to_string();
    loop {
        let lower = s.to_ascii_lowercase();
        let mut cut = None;
        for p in ["re", "fwd", "fw", "aw", "wg"] {
            if let Some(r) = lower.strip_prefix(p) {
                let r2 = r.trim_start();
                // optional [n]
                let r3 = if r2.starts_with('[') { r2.find(']').map(|i| r2[i + 1..].trim_start()).unwrap_or(r2) } else { r2 };
                if let Some(r4) = r3.strip_prefix(':') { cut = Some(s.len() - r4.len()); break; }
            }
        }
        match cut { Some(i) => s = s[i..].trim().to_string(), None => break }
    }
    s
}

/// "Fwd: <subject>" with its prefixes stripped; empty for a message without subject.
pub fn forward_subject(s: &str) -> String {
    let st = strip_subject_prefixes(s);
    if s.trim().is_empty() { String::new() } else if st.is_empty() { "Fwd:".into() } else { format!("Fwd: {st}") }
}

/// Bulwark sanitizePart: characters files can't have become "_", runs collapse, edges trimmed.
pub fn sanitize_part(s: &str, max: usize) -> String {
    let mut out = String::new();
    for c in s.chars() {
        let c = if "<>:\"/\\|?*".contains(c) || c.is_control() { '_' } else { c };
        if c == '_' && out.ends_with('_') { continue; }
        if c.is_whitespace() { if !out.ends_with(' ') { out.push(' '); } continue; }
        out.push(c);
    }
    let t = out.trim().trim_matches(|c| c == '.' || c == '_' || c == '-').to_string();
    t.chars().take(max).collect()
}

/// The name of a forwarded message: "{date}-{subject}.eml" (Bulwark emailExportFilename with
/// the fixed template, so no sender or recipient names leak to whoever receives it).
pub fn eml_export_name(m: &Value) -> String {
    let date = mail::parse_time(&m["receivedAt"]).or_else(|| mail::parse_time(&m["sentAt"]))
        .map(|t| t.format("%Y-%m-%d %H.%M.%S").to_string()).unwrap_or_else(|| "0000-00-00 00.00.00".into());
    let stem = sanitize_part(&format!("{date}-{}", m["subject"].as_str().unwrap_or("")), 200);
    format!("{}.eml", if stem.is_empty() { "email".into() } else { stem })
}

/// "attachments_<subject>.zip" (Bulwark attachmentsBundleFilename).
pub fn bundle_name(subject: &str) -> String {
    let stem = sanitize_part(subject.trim(), 200);
    if stem.is_empty() { "attachments.zip".into() } else { format!("attachments_{stem}.zip") }
}

/// Whether the shown body part was cut off by maxBodyValueBytes (Bulwark hasTruncatedDisplayedBody).
pub fn displayed_truncated(m: &Value) -> bool {
    ["htmlBody", "textBody"].iter().any(|k| m[*k][0]["partId"].as_str().is_some_and(|p| m["bodyValues"][p]["isTruncated"] == true))
}

/// The source as shown: at most SOURCE_DISPLAY_LIMIT bytes, cut on a character boundary.
pub fn source_shown(text: &str) -> (&str, bool) {
    if text.len() <= SOURCE_DISPLAY_LIMIT { return (text, false); }
    let mut cut = SOURCE_DISPLAY_LIMIT;
    while !text.is_char_boundary(cut) { cut -= 1; }
    (&text[..cut], true)
}

// ---------------------------------------------------------------- state

#[derive(Default, Clone, PartialEq, Debug)]
enum Step { #[default] Idle, Confirm, Busy, Done(String), Failed(String) }

struct Invite {
    loading: bool,
    error: Option<String>,
    ev: Value,
    method: Method,
    step: Step,
    status: String,
    organizer_me: bool,
}

#[derive(Default)]
enum View {
    #[default]
    Message,
    Source { full: String, cut: bool, stash: Option<crate::Current> },
    Eml { subject: String },
}

#[derive(Default)]
struct State {
    id: String,
    msg: Value,
    client: Option<Arc<Client>>,
    unsub: Option<Unsub>,
    unsub_step: Step,
    unsub_hidden: bool,
    mdn: bool,
    mdn_step: Step,
    trunc: bool,
    trunc_step: Step,
    invite: Option<Invite>,
    view: View,
    /// Save to Files: the Files account, its draft-18 shape and own folders (id, name, depth).
    files: Option<(String, bool, Vec<(String, String, i32)>)>,
    files_loading: bool,
}

thread_local! {
    static ST: RefCell<State> = RefCell::new(State::default());
    static SHARED: RefCell<Option<Shared>> = const { RefCell::new(None) };
    /// Test messages made through the automation channel (example-org only), for clean-up.
    static TEST_IDS: RefCell<Vec<String>> = const { RefCell::new(Vec::new()) };
}

/// Wire the banner callback (once, at start-up).
pub fn wire(u: &AppWindow, st: Shared) {
    SHARED.with(|s| *s.borrow_mut() = Some(st.clone()));
    let w = u.as_weak();
    u.global::<RX>().on_action(move |b, a| { if let Some(u) = w.upgrade() { banner_action(&u, &st, &b, &a); } });
}

/// A message starts loading: drop the previous one's banners and views.
pub fn clear(u: &AppWindow) {
    ST.with(|s| *s.borrow_mut() = State::default());
    refresh(u);
}

fn is_open(id: &str) -> bool { ST.with(|s| s.borrow().id == id) }

/// The open message is shown: work out its banners and start what loads in the background.
pub fn loaded(u: &AppWindow, c: Arc<Client>, m: &Value, id: &str) {
    let prefs = crate::settings::Settings::load();
    let headers = m["headers"].as_array().cloned().unwrap_or_default();
    let roles: Vec<String> = m["mailboxIds"].as_object().map(|o| o.keys().map(|k| mail::mailbox_role(k)).collect()).unwrap_or_default();
    let shared = mail::shared_account(id).is_some();
    let response = prefs.str("readReceiptResponse", "ask");
    let other_login = mail::owner(id).is_some_and(|o| o != crate::unified::active_user());
    let offer = !shared && crate::mdn::should_offer(m, &roles, &response);
    // "Always send" is the active login's setting; another login's message asks instead
    let auto = offer && response == "always" && !other_login;
    let unsub = header(&headers, "List-Unsubscribe").and_then(|h| parse_unsubscribe(&h, header(&headers, "List-Unsubscribe-Post").as_deref()));
    let invite_part = (prefs.bool("calendarInvitationParsingEnabled", true) && c.has(invite::CAP)).then(|| invite::find_part(m)).flatten();
    ST.with(|s| {
        let mut s = s.borrow_mut();
        *s = State { id: id.to_string(), msg: m.clone(), client: Some(c.clone()), unsub, mdn: offer && !auto, trunc: displayed_truncated(m), ..Default::default() };
        if invite_part.is_some() {
            s.invite = Some(Invite { loading: true, error: None, ev: Value::Null, method: Method::Unknown, step: Step::Idle, status: String::new(), organizer_me: false });
        }
    });
    refresh(u);
    if auto {
        let (c2, m2, w) = (c.clone(), m.clone(), u.as_weak());
        std::thread::spawn(move || {
            if let Err(e) = crate::mdn::send(&c2, &m2, true) {
                let _ = w.upgrade_in_event_loop(move |u| mail::show_toast(&u, &format!("Read receipt could not be sent: {e}"), false));
            }
        });
    }
    if let Some(part) = invite_part {
        let (c2, w, id2) = (c.clone(), u.as_weak(), id.to_string());
        let blob = part["blobId"].as_str().unwrap_or("").to_string();
        let mine = crate::my_addrs_cached().into_iter().chain([c.username.clone()]).collect::<Vec<_>>();
        std::thread::spawn(move || {
            let res = invite::parse(&c2, &blob).and_then(|(ev, m)| {
                // the stored copy carries the answer already given
                let stored = ev["uid"].as_str().and_then(|uid| invite::stored(&c2, uid).ok().flatten());
                let status = stored.as_ref().and_then(|s| invite::my_participant(s, &mine)).or_else(|| invite::my_participant(&ev, &mine)).map(|p| p.1).unwrap_or_default();
                Ok((ev, m, status))
            });
            let _ = w.upgrade_in_event_loop(move |u| {
                if !is_open(&id2) { return; }
                ST.with(|s| if let Some(inv) = s.borrow_mut().invite.as_mut() {
                    inv.loading = false;
                    match res {
                        Ok((ev, m, status)) => { inv.organizer_me = invite::i_organize(&ev, &mine); inv.ev = ev; inv.method = m; inv.status = status; }
                        Err(e) => inv.error = Some(e),
                    }
                });
                refresh(&u);
            });
        });
    }
    if m["attachments"].as_array().is_some_and(|a| !a.is_empty()) && c.has(FILES_CAP) { load_files(u, c); }
}

// ---------------------------------------------------------------- banners

fn act(id: &str, label: &str, style: i32) -> RxAction { RxAction { id: id.into(), label: label.into(), style, enabled: true } }
fn act_off(id: &str, label: &str, style: i32) -> RxAction { RxAction { enabled: false, ..act(id, label, style) } }

fn banner(id: &str, tone: &str, icon: &str, kicker: &str, title: &str, detail: &str, extra: &str, actions: Vec<RxAction>) -> RxBannerData {
    RxBannerData { id: id.into(), tone: tone.into(), icon: icon.into(), kicker: kicker.into(), title: title.into(), detail: detail.into(), extra: extra.into(),
        actions: ModelRc::new(VecModel::from(actions)) }
}

fn banners(s: &State) -> Vec<RxBannerData> {
    let mut v = vec![];
    match &s.view {
        View::Source { cut, .. } => {
            let detail = if *cut { "Showing the first 2 MB. Save the .eml for the whole message." } else { "The message exactly as the server holds it." };
            v.push(banner("source", "neutral", "\u{E943}", "Message source", detail, "", "",
                vec![act("copy", "Copy source", 0), act("save", "Save .eml", 0), act("close", "Close", 1)]));
            return v;
        }
        View::Eml { subject } => {
            v.push(banner("eml", "neutral", "\u{E8A5}", "Attached message", if subject.is_empty() { "(no subject)" } else { subject }, "", "",
                vec![act("back", "Back to message", 1)]));
            return v;
        }
        View::Message => {}
    }
    if s.trunc {
        let busy = s.trunc_step == Step::Busy;
        let detail = match &s.trunc_step { Step::Failed(e) => format!("Could not load it: {e}"), _ => "The server sent the first 2 MB of this long message.".into() };
        v.push(banner("trunc", "amber", "\u{E7BA}", "Long message", "Only part of this message is shown.", &detail, "",
            vec![if busy { act_off("full", "Loading…", 1) } else { act("full", "Show whole message", 1) }]));
    }
    if let Some(inv) = &s.invite { v.push(invite_banner(inv)); }
    if s.mdn {
        let by = crate::mdn::requested_by(s.msg["headers"].as_array().map(|a| a.as_slice()).unwrap_or(&[])).unwrap_or_default();
        match &s.mdn_step {
            Step::Done(t) => v.push(banner("mdn", "mint", "\u{E73E}", "Read receipt", t, "", "", vec![])),
            step => {
                let busy = *step == Step::Busy;
                let detail = match step { Step::Failed(e) => format!("Requested by {by}. Not sent: {e}"), _ => format!("Requested by {by}") };
                v.push(banner("mdn", "sky", "\u{E8C3}", "Read receipt", "The sender asked to be notified when you open this message.", &detail, "",
                    vec![if busy { act_off("send", "Sending…", 0) } else { act("send", "Send receipt", 0) }, act("ignore", "Ignore", 0)]));
            }
        }
    }
    if let (Some(u), false) = (&s.unsub, s.unsub_hidden) { v.push(unsub_banner(u, &s.unsub_step)); }
    v
}

fn unsub_banner(u: &Unsub, step: &Step) -> RxBannerData {
    let how = if u.prefers_http() {
        if u.one_click { "Unsubscribes with one click, without opening a page." } else { "The unsubscribe page opens in your browser." }
    } else { "An unsubscribe email will be sent from your account." };
    match step {
        Step::Confirm | Step::Busy => {
            let busy = *step == Step::Busy;
            // the sender wrote this: show exactly what goes where before anything is sent
            let (detail, extra) = match (u.prefers_http(), u.mailto.as_deref().and_then(unsubscribe_mailto)) {
                (false, Some((to, subj, body))) => (format!("To: {to}{}", subj.map(|s| format!("  ·  Subject: {s}")).unwrap_or_default()), body.unwrap_or_default()),
                _ => (how.to_string(), String::new()),
            };
            banner("unsub", "amber", "\u{E8F8}", "Newsletter", "Unsubscribe from this sender?", &detail, &extra,
                vec![if busy { act_off("confirm", "Unsubscribing…", 1) } else { act("confirm", "Confirm", 1) }, act("cancel", "Cancel", 0)])
        }
        Step::Done(t) => banner("unsub", "mint", "\u{E73E}", "Newsletter", t, "", "", vec![act("dismiss", "Dismiss", 2)]),
        Step::Failed(e) => banner("unsub", "amber", "\u{E7BA}", "Newsletter", "Unable to unsubscribe", e, "", vec![act("dismiss", "Dismiss", 2)]),
        Step::Idle => banner("unsub", "neutral", "\u{E715}", "Newsletter", "This message came from a mailing list.", how, "",
            vec![act("start", "Unsubscribe", 0), act("dismiss", "Dismiss", 2)]),
    }
}

fn invite_banner(inv: &Invite) -> RxBannerData {
    if inv.loading { return banner("invite", "sky", "\u{E787}", "Calendar invitation", "Loading event details…", "", "", vec![]); }
    if let Some(e) = &inv.error { return banner("invite", "amber", "\u{E787}", "Calendar invitation", "Could not read invitation", e, "", vec![]); }
    let ev = &inv.ev;
    let title = ev["title"].as_str().filter(|t| !t.trim().is_empty()).unwrap_or("(untitled event)");
    let zone = iana_time_zone::get_timezone().unwrap_or_default();
    let when = invite::when(ev, &zone);
    let mut extra: Vec<String> = vec![];
    let loc = invite::location(ev);
    if !loc.is_empty() { extra.push(loc); }
    if let Some((name, email)) = invite::organizer(ev) {
        extra.push(if inv.organizer_me { "You organize this event".into() } else if name != email && !email.is_empty() { format!("Organized by {name} <{email}>") } else { format!("Organized by {name}") });
    }
    let answerable = matches!(inv.method, Method::Request | Method::Add) && !inv.organizer_me && !inv.status.is_empty();
    if answerable { extra.push(format!("Your response: {}", invite::status_label(&inv.status))); }
    let (kicker, tone) = match inv.method {
        Method::Cancel => ("Event cancelled", "amber"),
        Method::Reply => ("Event response", "mint"),
        Method::Publish => ("Published event", "sky"),
        Method::Counter => ("Counter proposal", "amber"),
        Method::Refresh => ("Refresh request", "sky"),
        Method::DeclineCounter => ("Counter proposal declined", "amber"),
        _ => ("Calendar invitation", "sky"),
    };
    let mut detail = when;
    match &inv.step {
        Step::Done(t) => detail = format!("{detail}  ·  {t}"),
        Step::Failed(e) => detail = format!("{detail}  ·  Not sent: {e}"),
        _ => {}
    }
    if inv.method == Method::Reply {
        if let Some((who, st)) = ev["participants"].as_object().and_then(|o| o.values().find(|p| p["roles"]["owner"] != true && p["participationStatus"].as_str().is_some_and(|s| s != "needs-action"))
            .map(|p| (p["name"].as_str().map(String::from).or_else(|| invite::participant_email(p)).unwrap_or_default(), p["participationStatus"].as_str().unwrap_or("").to_string()))) {
            extra.push(format!("{who} responded {}", invite::status_label(&st).to_lowercase()));
        }
    }
    let busy = inv.step == Step::Busy;
    let actions = if answerable {
        let b = |id: &str, label: &str, st: &str| if busy { act_off(id, label, 0) } else { act(id, label, if inv.status == st { 1 } else { 0 }) };
        vec![b("accepted", "Accept", "accepted"), b("tentative", "Maybe", "tentative"), b("declined", "Decline", "declined")]
    } else { vec![] };
    banner("invite", tone, "\u{E787}", kicker, title, &detail, &extra.join("  ·  "), actions)
}

/// Put the banners of the open message on screen.
pub fn refresh(u: &AppWindow) {
    let v = ST.with(|s| banners(&s.borrow()));
    u.global::<RX>().set_banners(ModelRc::new(VecModel::from(v)));
}

fn set_step(u: &AppWindow, f: impl FnOnce(&mut State)) {
    ST.with(|s| f(&mut s.borrow_mut()));
    refresh(u);
}

fn banner_action(u: &AppWindow, st: &Shared, b: &str, a: &str) {
    let id = ST.with(|s| s.borrow().id.clone());
    match (b, a) {
        ("att", "zip") => download_all(u, st),
        ("source", "copy") => {
            let t = ST.with(|s| if let View::Source { full, .. } = &s.borrow().view { full.clone() } else { String::new() });
            let ok = mail::copy_to_clipboard(&t);
            mail::show_toast(u, if ok { "Source copied" } else { "Could not copy the source" }, false);
        }
        ("source", "save") => save_eml(u, st),
        ("source", "close") => close_view(u),
        ("eml", "back") => { let cur = u.get_current_mail(); if !cur.is_empty() { u.invoke_open_mail(cur); } }
        ("trunc", "full") => load_full_body(u),
        ("mdn", "ignore") => {
            // $MDNSent is the RFC 3503 flag every client honours: the request is suppressed everywhere
            let c = ST.with(|s| s.borrow().client.clone());
            set_step(u, |s| s.mdn = false);
            if let Some(c) = c { std::thread::spawn(move || { let _ = c.set_keyword(&[crate::unified::real_id(&id).to_string()], "$mdnsent", true); }); }
        }
        ("mdn", "send") => {
            let (c, m) = ST.with(|s| (s.borrow().client.clone(), s.borrow().msg.clone()));
            let Some(c) = c else { return };
            set_step(u, |s| s.mdn_step = Step::Busy);
            let w = u.as_weak();
            std::thread::spawn(move || {
                let mut m = m;
                m["id"] = json!(crate::unified::real_id(&id));
                let res = crate::mdn::send(&c, &m, false);
                let _ = w.upgrade_in_event_loop(move |u| {
                    if !is_open(&id) { return; }
                    set_step(&u, |s| s.mdn_step = match res { Ok(()) => Step::Done("Read receipt sent.".into()), Err(e) => Step::Failed(e) });
                });
            });
        }
        ("unsub", "start") => set_step(u, |s| s.unsub_step = Step::Confirm),
        ("unsub", "cancel") => set_step(u, |s| s.unsub_step = Step::Idle),
        ("unsub", "dismiss") => set_step(u, |s| s.unsub_hidden = true),
        ("unsub", "confirm") => unsubscribe(u, st),
        ("invite", status) if matches!(status, "accepted" | "tentative" | "declined") => {
            let ev = ST.with(|s| s.borrow().invite.as_ref().map(|i| i.ev.clone()));
            let Some(ev) = ev else { return };
            let Some(c) = st.lock().unwrap().clone() else { return };
            let mine = crate::my_addrs(st);
            let status = status.to_string();
            set_step(u, |s| if let Some(i) = s.invite.as_mut() { i.step = Step::Busy; });
            let w = u.as_weak();
            std::thread::spawn(move || {
                let res = invite::answer(&c, &ev, &mine, &status);
                let _ = w.upgrade_in_event_loop(move |u| {
                    if !is_open(&id) { return; }
                    set_step(&u, |s| if let Some(i) = s.invite.as_mut() {
                        match res {
                            Ok(_) => { i.status = status.clone(); i.step = Step::Done("Response sent".into()); }
                            Err(e) => i.step = Step::Failed(e),
                        }
                    });
                    if u.get_app() == 1 { u.invoke_cal_nav("".into()); }
                });
            });
        }
        _ => {}
    }
}

fn unsubscribe(u: &AppWindow, st: &Shared) {
    let (unsub, m, id) = ST.with(|s| (s.borrow().unsub.clone(), s.borrow().msg.clone(), s.borrow().id.clone()));
    let Some(un) = unsub else { return };
    let headers = m["headers"].as_array().cloned().unwrap_or_default();
    if un.prefers_http() {
        let url = un.http.clone().unwrap_or_default();
        if !valid_unsub_url(&url) { set_step(u, |s| s.unsub_step = Step::Failed("The link is not a web address.".into())); return; }
        if un.one_click && dkim_pass(&headers) {
            set_step(u, |s| s.unsub_step = Step::Busy);
            let w = u.as_weak();
            std::thread::spawn(move || {
                let res = one_click_post(&url);
                let _ = w.upgrade_in_event_loop(move |u| {
                    if !is_open(&id) { return; }
                    set_step(&u, |s| s.unsub_step = match res { Ok(()) => Step::Done("Unsubscribed".into()), Err(e) => Step::Failed(e) });
                });
            });
        } else {
            crate::oauth::open_browser(&url);
            set_step(u, |s| s.unsub_step = Step::Done("Unsubscribe page opened in your browser".into()));
        }
        return;
    }
    let Some((to, subject, body)) = un.mailto.as_deref().and_then(unsubscribe_mailto) else {
        set_step(u, |s| s.unsub_step = Step::Failed("The unsubscribe address is not valid.".into()));
        return;
    };
    let Some(c) = crate::login_for(st, &id) else { return };
    set_step(u, |s| s.unsub_step = Step::Busy);
    let w = u.as_weak();
    std::thread::spawn(move || {
        // from the identity the newsletter reached
        let from = c.identities().ok().and_then(|ids| crate::mdn::pick_identity(&ids, &m).and_then(|i| i["email"].as_str().map(String::from))).unwrap_or_default();
        let o = crate::jmap::Outgoing { from, to, subject: subject.unwrap_or_else(|| "Unsubscribe".into()), text: body.unwrap_or_default(), ..Default::default() };
        let res = c.send_message(&o).map(|_| ());
        let _ = w.upgrade_in_event_loop(move |u| {
            if !is_open(&id) { return; }
            set_step(&u, |s| s.unsub_step = match res { Ok(()) => Step::Done("Unsubscribe email sent".into()), Err(e) => Step::Failed(e) });
        });
    });
}

fn load_full_body(u: &AppWindow) {
    let (c, id) = ST.with(|s| (s.borrow().client.clone(), s.borrow().id.clone()));
    let Some(c) = c else { return };
    set_step(u, |s| s.trunc_step = Step::Busy);
    let w = u.as_weak();
    std::thread::spawn(move || {
        let real = crate::unified::real_id(&id).to_string();
        // maxBodyValueBytes 0: no truncation (RFC 8621 §4.2)
        let res = c.call(json!([["Email/get", {"accountId": c.account, "ids": [real], "properties": ["bodyValues"],
            "fetchTextBodyValues": true, "fetchHTMLBodyValues": true, "maxBodyValueBytes": 0}, "g"]]))
            .and_then(|r| r[0][1]["list"][0]["bodyValues"].as_object().cloned().ok_or_else(|| "Message not found.".to_string()));
        let _ = w.upgrade_in_event_loop(move |u| {
            if !is_open(&id) { return; }
            match res {
                Ok(values) => {
                    let mut m = ST.with(|s| s.borrow().msg.clone());
                    m["bodyValues"] = Value::Object(values);
                    let (c2, w2, id2) = (c.clone(), u.as_weak(), id.clone());
                    std::thread::spawn(move || {
                        crate::inline_cid_images(&c2, &mut m);
                        let _ = w2.upgrade_in_event_loop(move |u| {
                            if !is_open(&id2) { return; }
                            show_message_body(&u, &m);
                            crate::CUR_MSG.with(|x| if let Some((cm, _)) = x.borrow_mut().as_mut() { cm["bodyValues"] = m["bodyValues"].clone(); });
                            set_step(&u, |s| { s.trunc = displayed_truncated(&m); s.trunc_step = Step::Idle; s.msg = m; });
                        });
                    });
                }
                Err(e) => set_step(&u, |s| s.trunc_step = Step::Failed(e)),
            }
        });
    });
}

/// Show a message's body in the reader (as main.rs does when a message opens).
fn show_message_body(u: &AppWindow, m: &Value) {
    let (html, text) = crate::body_text(m);
    let is_html = m["htmlBody"].as_array().is_some_and(|a| a.iter().any(|p| p["type"] == "text/html"));
    let html = (is_html && !html.is_empty()).then_some(html);
    let remote = html.as_deref().is_some_and(crate::has_remote_images);
    let allow = u.get_auto_images();
    crate::CUR.with(|c| *c.borrow_mut() = Some(crate::Current { html, text, remote, allow }));
    crate::render_current(u);
}

// ---------------------------------------------------------------- source and attached messages

fn view_source(u: &AppWindow) {
    let (c, m, id) = ST.with(|s| (s.borrow().client.clone(), s.borrow().msg.clone(), s.borrow().id.clone()));
    let (Some(c), Some(blob)) = (c, m["blobId"].as_str().map(String::from)) else { mail::show_toast(u, "The source is not available.", false); return };
    mail::show_toast(u, "Loading the source…", false);
    let w = u.as_weak();
    std::thread::spawn(move || {
        let res = c.download(&blob, "message.eml", "message/rfc822").map(|b| String::from_utf8_lossy(&b).to_string());
        let _ = w.upgrade_in_event_loop(move |u| {
            if !is_open(&id) { return; }
            match res {
                Ok(full) => {
                    if u.get_toast().as_str() == "Loading the source…" { u.set_toast("".into()); }
                    let (shown, cut) = source_shown(&full);
                    let html = format!("<pre style=\"font-family:Consolas,'Cascadia Mono',monospace;font-size:12px;white-space:pre-wrap;word-break:break-all;margin:0\">{}</pre>", crate::esc(shown));
                    let shown = shown.to_string();
                    let stash = crate::CUR.with(|c| c.borrow_mut().take());
                    ST.with(|s| {
                        let mut s = s.borrow_mut();
                        // a second "View source" keeps the message's own body stashed
                        let stash = match std::mem::take(&mut s.view) { View::Source { stash: old, .. } => old, _ => stash };
                        s.view = View::Source { full, cut, stash };
                    });
                    crate::CUR.with(|c| *c.borrow_mut() = Some(crate::Current { html: Some(html), text: shown, remote: false, allow: false }));
                    crate::render_current(&u);
                    refresh(&u);
                }
                Err(e) => mail::show_toast(&u, &format!("Could not load the source: {e}"), false),
            }
        });
    });
}

fn close_view(u: &AppWindow) {
    let view = ST.with(|s| std::mem::take(&mut s.borrow_mut().view));
    match view {
        View::Source { stash, .. } => {
            crate::CUR.with(|c| *c.borrow_mut() = stash);
            crate::render_current(u);
            refresh(u);
        }
        View::Eml { .. } => { let cur = u.get_current_mail(); if !cur.is_empty() { u.invoke_open_mail(cur); } }
        View::Message => {}
    }
}

/// Open an attached message (message/rfc822 or .eml) in the reader with Email/parse.
pub fn is_message_attachment(a: &Value) -> bool {
    let t = a["type"].as_str().unwrap_or("").to_ascii_lowercase();
    t == "message/rfc822" || t == "message/global" || a["name"].as_str().is_some_and(|n| n.to_lowercase().ends_with(".eml"))
}

pub fn open_eml(u: &AppWindow, c: Arc<Client>, a: Value) {
    let Some(blob) = a["blobId"].as_str().map(String::from) else { return };
    let id = u.get_current_mail().to_string();
    mail::show_toast(u, "Opening the attached message…", false);
    let w = u.as_weak();
    std::thread::spawn(move || {
        let res = parse_eml(&c, &blob).map(|mut m| { crate::inline_cid_images(&c, &mut m); m });
        let _ = w.upgrade_in_event_loop(move |u| {
            if u.get_current_mail().as_str() != id { return; }
            if u.get_toast().as_str() == "Opening the attached message…" { u.set_toast("".into()); }
            match res {
                Ok(m) => show_parsed(&u, &m),
                Err(e) => mail::show_toast(&u, &format!("Could not open the attached message: {e}"), false),
            }
        });
    });
}

/// Email/parse of a message blob (RFC 8621 §4.9) with what the reader shows.
pub fn parse_eml(c: &Client, blob: &str) -> Result<Value, String> {
    let r = c.call(json!([["Email/parse", {"accountId": c.account, "blobIds": [blob],
        "properties": ["size", "sentAt", "from", "to", "cc", "bcc", "replyTo", "subject", "preview", "textBody", "htmlBody", "bodyValues",
            "hasAttachment", "attachments", "messageId", "inReplyTo", "references", "headers", "blobId"],
        "bodyProperties": crate::jmap::BODY_PROPERTIES,
        "fetchTextBodyValues": true, "fetchHTMLBodyValues": true, "maxBodyValueBytes": 2_000_000}, "p"]]))?;
    if r[0][0] == "error" { return Err(crate::jmap::method_error(&r[0][1])); }
    if r[0][1]["notParsable"].as_array().is_some_and(|a| a.iter().any(|x| x == blob)) { return Err("The attachment is not a readable message.".into()); }
    let mut m = r[0][1]["parsed"][blob].clone();
    if !m.is_object() { return Err("The attachment is not a readable message.".into()); }
    // a message read from a file has no place in the account: no id, keywords or mailboxes
    m["receivedAt"] = m["sentAt"].clone();
    m["keywords"] = json!({"$seen": true});
    Ok(m)
}

fn show_parsed(u: &AppWindow, m: &Value) {
    let f = &m["from"][0];
    let from = mail::sender(m);
    let email = f["email"].as_str().unwrap_or("").to_string();
    u.set_r_email(email.into());
    u.set_r_initials(crate::initials(&from).into());
    u.set_r_color(mail::tint(&from));
    u.set_r_flagged(false);
    let names = |k: &str| m[k].as_array().map(|a| a.iter().map(|x| x["name"].as_str().filter(|n| !n.is_empty()).or(x["email"].as_str()).unwrap_or("").to_string()).collect::<Vec<_>>().join(", ")).unwrap_or_default();
    let mut to = names("to");
    let cc = names("cc");
    if !cc.is_empty() { to = format!("{to}  ·  Cc: {cc}"); }
    u.set_r_to(to.into());
    let subject = m["subject"].as_str().unwrap_or("").to_string();
    u.set_r_subject(if subject.is_empty() { "(no subject)".into() } else { subject.clone().into() });
    u.set_r_from(from.into());
    u.set_r_date(mail::parse_time(&m["sentAt"]).map(|t| t.format("%a %d %b %Y, %H:%M").to_string()).unwrap_or_default().into());
    u.set_thread_msgs(ModelRc::default());
    u.set_plugin_rows(ModelRc::default());
    u.set_spam_verdict("".into());
    u.set_spam_detail("".into());
    let hide_inline = crate::settings::Settings::load().bool("hideInlineImageAttachments", true);
    let atts: Vec<Value> = m["attachments"].as_array().cloned().unwrap_or_default().into_iter().filter(|a| !(hide_inline && mail::is_inline_image(a))).collect();
    let rows: Vec<crate::Att> = atts.iter().enumerate().map(|(i, a)| crate::Att {
        name: a["name"].as_str().unwrap_or("attachment").into(), size: crate::human_size(a["size"].as_u64().unwrap_or(0)).into(), index: i as i32,
    }).collect();
    u.set_attachments(ModelRc::new(VecModel::from(rows)));
    crate::CUR_MSG.with(|x| *x.borrow_mut() = Some((m.clone(), atts)));
    show_message_body(u, m);
    ST.with(|s| s.borrow_mut().view = View::Eml { subject });
    refresh(u);
}

// ---------------------------------------------------------------- saving

fn save_eml(u: &AppWindow, st: &Shared) {
    let (c, m, id) = ST.with(|s| (s.borrow().client.clone(), s.borrow().msg.clone(), s.borrow().id.clone()));
    let Some(c) = c.or_else(|| crate::client_for(st, &id)) else { return };
    let subj = m["subject"].as_str().filter(|s| !s.is_empty()).unwrap_or("message");
    let name = format!("{}.eml", crate::sanitize_name(subj));
    let Some(path) = crate::automation::save_file(&name) else { return };
    let blob = m["blobId"].as_str().unwrap_or("").to_string();
    // the copy shown in the source view saves without a second download
    let have = ST.with(|s| if let View::Source { full, cut: false, .. } = &s.borrow().view { Some(full.clone()) } else { None });
    let w = u.as_weak();
    std::thread::spawn(move || {
        let res = match have { Some(t) => Ok(t.into_bytes()), None => c.download(&blob, &name, "message/rfc822") }
            .and_then(|b| std::fs::write(&path, b).map_err(|e| e.to_string()));
        let msg = match res { Ok(()) => format!("Saved {name}"), Err(e) => format!("Could not save: {e}") };
        let _ = w.upgrade_in_event_loop(move |u| mail::show_toast(&u, &msg, false));
    });
}

/// Download every listed attachment into one zip (Bulwark handleDownloadAllAttachments): a
/// failed attachment is left out and counted, the rest still bundle.
fn download_all(u: &AppWindow, st: &Shared) {
    let Some((m, atts)) = crate::CUR_MSG.with(|x| x.borrow().clone()) else { return };
    if atts.is_empty() { return; }
    let id = u.get_current_mail().to_string();
    let Some(c) = ST.with(|s| s.borrow().client.clone()).or_else(|| crate::client_for(st, &id)) else { return };
    let Some(path) = crate::automation::save_file(&bundle_name(m["subject"].as_str().unwrap_or(""))) else { return };
    mail::show_toast(u, &format!("Downloading {} attachments…", atts.len()), false);
    let w = u.as_weak();
    std::thread::spawn(move || {
        let names = crate::zip::unique_names(&atts.iter().map(|a| crate::sanitize_name(a["name"].as_str().unwrap_or("attachment"))).collect::<Vec<_>>(), "attachment");
        let mut entries = vec![];
        for (a, name) in atts.iter().zip(names) {
            if let Ok(data) = c.download(a["blobId"].as_str().unwrap_or(""), &name, a["type"].as_str().unwrap_or("")) {
                entries.push(crate::zip::Entry { name, data });
            }
        }
        let omitted = atts.len() - entries.len();
        let res = if entries.is_empty() { Err("Nothing could be downloaded.".to_string()) } else {
            crate::zip::build(&entries, chrono::Local::now().naive_local()).and_then(|z| std::fs::write(&path, z).map_err(|e| e.to_string()))
        };
        let file = path.file_name().map(|f| f.to_string_lossy().to_string()).unwrap_or_default();
        let msg = match res {
            Ok(()) if omitted > 0 => format!("Saved {file}. {omitted} attachment{} could not be added.", if omitted == 1 { "" } else { "s" }),
            Ok(()) => format!("Saved {file}"),
            Err(e) => format!("Could not download the attachments: {e}"),
        };
        let _ = w.upgrade_in_event_loop(move |u| mail::show_toast(&u, &msg, false));
    });
}

/// The Files account's own folders, for "Save to Files" (hidden ones and the trash left out).
fn load_files(u: &AppWindow, c: Arc<Client>) {
    if ST.with(|s| s.borrow().files_loading || s.borrow().files.is_some()) { return; }
    ST.with(|s| s.borrow_mut().files_loading = true);
    let (w, id) = (u.as_weak(), u.get_current_mail().to_string());
    std::thread::spawn(move || {
        let acc = c.account_for(FILES_CAP);
        let draft = c.session["accounts"][&acc]["accountCapabilities"][FILES_CAP].get("supportedNodeTypes").is_some()
            || c.session["capabilities"][FILES_CAP].get("supportedNodeTypes").is_some();
        let res = c.query_all("FileNode/query", &acc, json!({})).and_then(|ids| {
            let ids: Vec<Value> = ids.into_iter().collect();
            c.get_all("FileNode/get", &acc, &ids, Some(&json!(["id", "parentId", "name", "nodeType", "@type", "blobId"])))
        });
        let folders = res.map(|nodes| folder_tree(&nodes));
        let _ = w.upgrade_in_event_loop(move |_| {
            if !is_open(&id) { return; }
            ST.with(|s| {
                let mut s = s.borrow_mut();
                s.files_loading = false;
                if let Ok(f) = folders { s.files = Some((acc, draft, f)); }
            });
        });
    });
}

fn is_dir(n: &Value) -> bool {
    match n["nodeType"].as_str().or(n["@type"].as_str().map(|t| match t { "Directory" => "directory", "File" => "file", _ => "other" })) {
        Some(t) => t == "directory",
        None => n["blobId"].is_null(),
    }
}

/// Own folders in tree order (id, name, depth), skipping hidden folders and everything in them.
pub fn folder_tree(nodes: &[Value]) -> Vec<(String, String, i32)> {
    let dirs: Vec<&Value> = nodes.iter().filter(|n| is_dir(n)).collect();
    let mut out = vec![];
    fn add(dirs: &[&Value], parent: Option<&str>, depth: i32, out: &mut Vec<(String, String, i32)>) {
        let mut kids: Vec<&&Value> = dirs.iter().filter(|n| n["parentId"].as_str().filter(|p| !p.is_empty()) == parent).collect();
        kids.sort_by_key(|n| n["name"].as_str().unwrap_or("").to_lowercase());
        for k in kids {
            let name = k["name"].as_str().unwrap_or("");
            if name.starts_with('.') || depth > 20 { continue; }
            let id = k["id"].as_str().unwrap_or("").to_string();
            out.push((id.clone(), name.to_string(), depth));
            add(dirs, Some(&id), depth + 1, out);
        }
    }
    add(&dirs, None, 0, &mut out);
    out
}

/// File an attachment into a Files folder by blob id (no download or upload); from another
/// account (a shared mailbox) it is copied over first with Blob/copy (RFC 8620 §6.3).
fn save_to_files(u: &AppWindow, st: &Shared, index: i32, folder: &str) {
    let Some(a) = crate::CUR_MSG.with(|x| x.borrow().as_ref().and_then(|(_, atts)| atts.get(index as usize).cloned())) else { return };
    let id = u.get_current_mail().to_string();
    let Some(c) = ST.with(|s| s.borrow().client.clone()).or_else(|| crate::client_for(st, &id)) else { return };
    let Some((acc, draft, folders)) = ST.with(|s| s.borrow().files.clone()) else { return };
    let folder_name = if folder.is_empty() { "My files".to_string() } else { folders.iter().find(|f| f.0 == folder).map(|f| f.1.clone()).unwrap_or_default() };
    let parent = (!folder.is_empty()).then(|| folder.to_string());
    let w = u.as_weak();
    std::thread::spawn(move || {
        let res: Result<(), String> = (|| {
            let mut blob = a["blobId"].as_str().ok_or("The attachment has no content.")?.to_string();
            if c.account != acc {
                let r = c.call(json!([["Blob/copy", {"fromAccountId": c.account, "accountId": acc, "blobIds": [blob]}, "b"]]))?;
                blob = r[0][1]["copied"][&blob].as_str().map(String::from).ok_or_else(|| crate::jmap::set_error(&r[0][1]["notCopied"][&blob]))?;
            }
            let name = a["name"].as_str().filter(|n| !n.trim().is_empty()).unwrap_or("attachment").to_string();
            let mime = a["type"].as_str().unwrap_or("application/octet-stream").to_string();
            let node = file_node(draft, parent.as_deref(), &name, &blob, &mime);
            let r = c.call(json!([["FileNode/set", {"accountId": acc, "onExists": "rename", "create": {"n": node}}, "s"]]))?;
            if r[0][0] == "error" { return Err(crate::jmap::method_error(&r[0][1])); }
            r[0][1]["created"]["n"]["id"].as_str().map(|_| ()).ok_or_else(|| crate::jmap::set_error(&r[0][1]["notCreated"]["n"]))
        })();
        let msg = match res { Ok(()) => format!("Saved to Files › {folder_name}"), Err(e) => format!("Not saved to Files: {e}") };
        let _ = w.upgrade_in_event_loop(move |u| mail::show_toast(&u, &msg, false));
    });
}

/// A FileNode create object in the server's shape (Stalwart 0.16 or draft 18 / 1.0).
pub fn file_node(draft: bool, parent: Option<&str>, name: &str, blob: &str, mime: &str) -> Value {
    let p = parent.map(|p| json!(p)).unwrap_or(Value::Null);
    if draft { json!({"@type": "File", "parentId": p, "name": name, "blobId": blob, "mediaType": mime}) }
    else { json!({"parentId": p, "name": name, "blobId": blob, "type": mime}) }
}

// ---------------------------------------------------------------- menus

const G_SOURCE: &str = "\u{E943}";
const G_FORWARD: &str = "\u{E72D}";
const G_DOWNLOAD: &str = "\u{E896}";
const G_FOLDER: &str = "\u{E8B7}";
const G_READ: &str = "\u{E8C3}";
const G_TAG: &str = "\u{E8EC}";
const G_BLOCK: &str = "\u{E8F8}";
const G_SETTINGS: &str = "\u{E713}";
const G_ZIP: &str = "\u{F012}";
const G_HOME: &str = "\u{E80F}";

fn it(icon: &str, label: &str, action: &str) -> CtxItem {
    CtxItem { icon: icon.into(), label: label.into(), hint: "".into(), action: action.into(), kind: 0, sub: false, danger: false, enabled: true, checked: false, color: Default::default() }
}
fn sub(icon: &str, label: &str, action: &str) -> CtxItem { CtxItem { sub: true, ..it(icon, label, action) } }
fn off(mut i: CtxItem, on: bool) -> CtxItem { i.enabled = on; i }
fn sep() -> CtxItem { CtxItem { kind: 1, ..it("", "", "") } }
fn head(label: &str) -> CtxItem { CtxItem { kind: 2, ..it("", label, "") } }
fn hinted(mut i: CtxItem, hint: &str) -> CtxItem { i.hint = hint.into(); i }

/// What rules can be made from the open message: None when its account takes no rules.
fn rule_subject(u: &AppWindow) -> Option<crate::quick_rules::Subject> {
    let (m, id, c) = ST.with(|s| (s.borrow().msg.clone(), s.borrow().id.clone(), s.borrow().client.clone()));
    if m.is_null() || mail::shared_account(&id).is_some() || !c.is_some_and(|c| c.has(SIEVE_CAP)) { return None; }
    let _ = u;
    Some(crate::quick_rules::subject_of(&m, &crate::my_addrs_cached()))
}

/// The reader's More menu (kind "reader").
pub fn menu(u: &AppWindow) -> Vec<CtxItem> {
    let (m, eml_view) = ST.with(|s| (s.borrow().msg.clone(), matches!(s.borrow().view, View::Eml { .. })));
    if m.is_null() { return vec![]; }
    let has_blob = m["blobId"].is_string();
    let shared = m["__jmap"].is_string();
    let n_atts = crate::CUR_MSG.with(|x| x.borrow().as_ref().map(|(_, a)| a.len()).unwrap_or(0));
    let mut v = vec![
        off(it(G_SOURCE, "View source", "rx:source"), has_blob && !eml_view),
        off(it(G_FORWARD, "Forward as attachment", "rx:fwd-att"), has_blob && !shared && !eml_view),
        off(it(G_DOWNLOAD, "Download (.eml)", "rx:eml"), has_blob && !eml_view),
        off(it(G_ZIP, "Download all attachments (.zip)", "rx:zip"), n_atts > 0),
    ];
    if let Some(s) = rule_subject(u).filter(|_| !eml_view) {
        v.push(sep());
        v.push(head("Rules"));
        let one = !s.senders.is_empty();
        let who = crate::quick_rules::sender_name(&s);
        v.push(off(sub(G_FOLDER, &if one { format!("Always move messages from {who}") } else { "Always move messages from the sender".into() }, "sub:rx-move-sender"), one));
        if let Some(d) = &s.domain { v.push(sub(G_FOLDER, &format!("Always move messages from @{d}"), "sub:rx-move-domain")); }
        if s.list_id.is_some() { v.push(sub(G_FOLDER, "Always move messages from this list", "sub:rx-move-list")); }
        v.push(off(it(G_READ, &if one { format!("Always mark as read from {who}") } else { "Always mark as read from the sender".into() }, "rx:rule-read"), one));
        v.push(off(sub(G_TAG, "Always tag with", "sub:rx-tag"), one));
        let junk = junk_folder().is_some();
        v.push(if junk { off(it(G_BLOCK, "Block sender", "rx:rule-block"), one) } else { hinted(off(it(G_BLOCK, "Block sender", ""), false), "No Junk folder") });
        v.push(it(G_SETTINGS, "Manage rules", "rx:rules-manage"));
    }
    v
}

/// (id, name, parentId, role) of the account's folders.
fn folder_rows() -> Vec<(String, String, Option<String>, String)> {
    mail::mailboxes().iter().map(|m| (m["id"].as_str().unwrap_or("").to_string(), m["name"].as_str().unwrap_or("").to_string(),
        m["parentId"].as_str().map(String::from), m["role"].as_str().unwrap_or("").to_string())).collect()
}

fn junk_folder() -> Option<crate::quick_rules::Folder> {
    let rows = folder_rows();
    rows.iter().find(|f| f.3 == "junk").map(|f| crate::quick_rules::Folder { id: f.0.clone(), path: crate::quick_rules::sieve_path(&rows, &f.0), name: f.1.clone() })
}

/// Flyouts of the reader menu and of the attachment menu ("rx-…").
pub fn submenu(u: &AppWindow, which: &str, target_id: &str) -> Vec<CtxItem> {
    let mut v = vec![];
    match which {
        "rx-move-sender" | "rx-move-domain" | "rx-move-list" => {
            // the account's folders that take mail, without Drafts (Bulwark ruleTargetMailboxIds)
            let allowed: std::collections::HashSet<String> = mail::mailboxes().iter()
                .filter(|m| m["role"] != "drafts" && m["myRights"]["mayAddItems"] != false)
                .filter_map(|m| m["id"].as_str().map(String::from)).collect();
            let kind = &which[8..];
            let m = u.get_folders();
            for f in (0..m.row_count()).filter_map(|i| m.row_data(i)) {
                if f.id.starts_with("__") || !allowed.contains(f.id.as_str()) { continue; }
                let indent = "   ".repeat(f.depth.max(0) as usize);
                v.push(it(&f.icon, &format!("{indent}{}", f.name), &format!("rx:rule-move:{kind}:{}", f.id)));
            }
            if v.is_empty() { v.push(off(it("", "No folders", ""), false)); }
        }
        "rx-tag" => {
            let m = u.get_tags();
            for t in (0..m.row_count()).filter_map(|i| m.row_data(i)) {
                let mut i = it(G_TAG, &t.name, &format!("rx:rule-tag:{}", t.id));
                i.color = t.color;
                v.push(i);
            }
            if v.is_empty() { v.push(off(it("", "No tags", ""), false)); }
        }
        "rx-files" => {
            let files = ST.with(|s| s.borrow().files.clone());
            match files {
                None => v.push(off(it("", "Loading folders…", ""), false)),
                Some((_, _, folders)) => {
                    v.push(it(G_HOME, "My files", &format!("rxfile:{target_id}:")));
                    for (id, name, depth) in folders {
                        v.push(it(G_FOLDER, &format!("{}{name}", "   ".repeat(depth as usize + 1)), &format!("rxfile:{target_id}:{id}")));
                    }
                }
            }
        }
        _ => {}
    }
    v
}

/// Extra rows of the attachment menu (index `idx`).
pub fn att_items(idx: &str) -> Vec<CtxItem> {
    let (files, loading) = ST.with(|s| (s.borrow().files.is_some(), s.borrow().files_loading));
    let i: usize = idx.parse().unwrap_or(0);
    let is_msg = crate::CUR_MSG.with(|x| x.borrow().as_ref().and_then(|(_, a)| a.get(i).map(is_message_attachment))).unwrap_or(false);
    let mut v = vec![];
    if files || loading { v.push(sub(G_FOLDER, "Save to Files", "sub:rx-files")); }
    let n_atts = crate::CUR_MSG.with(|x| x.borrow().as_ref().map(|(_, a)| a.len()).unwrap_or(0));
    if n_atts > 1 { v.push(it(G_ZIP, "Download all (.zip)", "rx:zip")); }
    if is_msg { v.insert(0, it(G_SOURCE, "Open message", "rx:open-eml")); }
    v
}

/// A pick in the reader menu, the attachment menu (actions "rx…") or the list's More menu.
pub fn pick(u: &AppWindow, st: &Shared, action: &str, target_id: &str) {
    if let Some(rest) = action.strip_prefix("rxfile:") {
        let (idx, folder) = rest.split_once(':').unwrap_or((rest, ""));
        save_to_files(u, st, idx.parse().unwrap_or(-1), folder);
        return;
    }
    let Some(a) = action.strip_prefix("rx:") else { return };
    match a {
        "source" => view_source(u),
        "eml" => save_eml(u, st),
        "zip" => download_all(u, st),
        "open-eml" => {
            let i: usize = target_id.parse().unwrap_or(0);
            let Some(att) = crate::CUR_MSG.with(|x| x.borrow().as_ref().and_then(|(_, at)| at.get(i).cloned())) else { return };
            let Some(c) = ST.with(|s| s.borrow().client.clone()).or_else(|| crate::reader_client(st)) else { return };
            open_eml(u, c, att);
        }
        "fwd-att" => {
            let id = u.get_current_mail().to_string();
            let Some(c) = crate::login_for(st, &id) else { return };
            let Some((m, _)) = crate::CUR_MSG.with(|x| x.borrow().clone()) else { return };
            crate::COMPOSE_CLIENT.with(|r| *r.borrow_mut() = Some(c.clone()));
            if !crate::compose::open_forward_as_attachment(u, c, &m, &eml_export_name(&m), &forward_subject(m["subject"].as_str().unwrap_or(""))) {
                mail::show_toast(u, "This message can't be attached.", false);
            }
        }
        "rules-manage" => { u.set_settings_tab("filters".into()); u.invoke_open_settings(); }
        "rule-read" | "rule-block" => {
            let Some(s) = rule_subject(u) else { return };
            let preset = if a == "rule-read" { crate::quick_rules::Preset::MarkRead } else {
                let Some(j) = junk_folder() else { return };
                crate::quick_rules::Preset::Block(j)
            };
            save_rule(u, st, preset, s);
        }
        r if r.starts_with("rule-tag:") => {
            let Some(s) = rule_subject(u) else { return };
            let tid = &r[9..];
            let m = u.get_tags();
            let name = (0..m.row_count()).filter_map(|i| m.row_data(i)).find(|t| t.id == tid).map(|t| t.name.to_string()).unwrap_or_else(|| tid.to_string());
            save_rule(u, st, crate::quick_rules::Preset::Tag { id: tid.to_string(), name }, s);
        }
        r if r.starts_with("rule-move:") => {
            let Some(s) = rule_subject(u) else { return };
            let Some((kind, fid)) = r[10..].split_once(':') else { return };
            let rows = folder_rows();
            let Some(f) = rows.iter().find(|f| f.0 == fid) else { return };
            let folder = crate::quick_rules::Folder { id: f.0.clone(), path: crate::quick_rules::sieve_path(&rows, fid), name: f.1.clone() };
            let preset = match kind {
                "domain" => crate::quick_rules::Preset::MoveDomain(folder),
                "list" => crate::quick_rules::Preset::MoveList(folder),
                _ => crate::quick_rules::Preset::MoveSender(folder),
            };
            save_rule(u, st, preset, s);
        }
        _ => {}
    }
}

fn save_rule(u: &AppWindow, st: &Shared, preset: crate::quick_rules::Preset, s: crate::quick_rules::Subject) {
    let id = u.get_current_mail().to_string();
    let Some(c) = crate::login_for(st, &id) else { return };
    let rule = crate::quick_rules::preset_rule(&preset, &s, &crate::quick_rules::new_id());
    mail::show_toast(u, "Saving the rule…", false);
    let w = u.as_weak();
    std::thread::spawn(move || {
        let msg = match crate::quick_rules::save(&c, &rule) { Ok(m) => m, Err(e) => format!("Rule not saved: {e}") };
        let _ = w.upgrade_in_event_loop(move |u| mail::show_toast(&u, &msg, false));
    });
}

// ---------------------------------------------------------------- automation (testing only)

/// Banners as text, for the automation probe `rx-banners`.
pub fn probe() -> String {
    let v = ST.with(|s| banners(&s.borrow()));
    let rows: Vec<Value> = v.iter().map(|b| json!({"id": b.id.as_str(), "tone": b.tone.as_str(), "kicker": b.kicker.as_str(), "title": b.title.as_str(),
        "detail": b.detail.as_str(), "extra": b.extra.as_str(),
        "actions": (0..b.actions.row_count()).filter_map(|i| b.actions.row_data(i)).map(|a| format!("{}{}", a.label, if a.enabled { "" } else { " (off)" })).collect::<Vec<_>>()})).collect();
    json!({"id": ST.with(|s| s.borrow().id.clone()), "banners": rows}).to_string()
}

/// The test account the automation channel may write to.
pub const TEST_ACCOUNT: &str = "mail@example.org";

fn test_client() -> Result<Arc<Client>, String> {
    let c = SHARED.with(|s| s.borrow().as_ref().and_then(|st| st.lock().unwrap().clone())).ok_or("not signed in")?;
    if !c.username.eq_ignore_ascii_case(TEST_ACCOUNT) { return Err(format!("refused: the active account is not {TEST_ACCOUNT}")); }
    Ok(c)
}

/// `rx-import PATH`: import an .eml into the test account's Inbox (Email/import, nothing sent).
pub fn automation_import(path: &str) -> String {
    let res = (|| -> Result<String, String> {
        let c = test_client()?;
        let data = std::fs::read(path).map_err(|e| e.to_string())?;
        let (blob, _) = c.upload(data, "message/rfc822")?;
        let inbox = c.role_ids()?.get("inbox").cloned().ok_or("no inbox")?;
        let r = c.call(json!([["Email/import", {"accountId": c.account, "emails": {"m": {"blobId": blob, "mailboxIds": {inbox: true}, "keywords": {}}}}, "i"]]))?;
        let id = r[0][1]["created"]["m"]["id"].as_str().map(String::from).ok_or_else(|| crate::jmap::set_error(&r[0][1]["notCreated"]["m"]))?;
        TEST_IDS.with(|t| t.borrow_mut().push(id.clone()));
        Ok(id)
    })();
    match res { Ok(id) => format!("imported {id}"), Err(e) => format!("error {e}") }
}

/// `rx-find TEXT`: ids of the test account's messages whose subject contains TEXT (read-only).
pub fn automation_find(text: &str) -> String {
    let res = (|| -> Result<Vec<String>, String> {
        let c = test_client()?;
        let r = c.call(json!([["Email/query", {"accountId": c.account, "filter": {"subject": text}, "limit": 50}, "q"]]))?;
        Ok(r[0][1]["ids"].as_array().map(|a| a.iter().filter_map(|x| x.as_str().map(String::from)).collect()).unwrap_or_default())
    })();
    match res { Ok(v) => v.join(" "), Err(e) => format!("error {e}") }
}

/// `rx-destroy ID…`: delete test messages for good (test account only).
pub fn automation_destroy(ids: &str) -> String {
    let res = (|| -> Result<usize, String> {
        let c = test_client()?;
        let ids: Vec<String> = ids.split_whitespace().map(String::from).collect();
        if ids.is_empty() { return Ok(0); }
        c.destroy(&ids)
    })();
    match res { Ok(n) => format!("destroyed {n}"), Err(e) => format!("error {e}") }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn headers_unfold() {
        let h = vec![json!({"name": "List-Unsubscribe", "value": " <https://x.org/u?a=1>,\r\n\t<mailto:u@x.org>"}), json!({"name": "list-unsubscribe", "value": " second"})];
        assert_eq!(header(&h, "LIST-UNSUBSCRIBE").as_deref(), Some("<https://x.org/u?a=1>, <mailto:u@x.org>"));
        assert_eq!(headers_all(&h, "list-unsubscribe").len(), 2);
        assert_eq!(header(&h, "List-Id"), None);
    }

    #[test]
    fn unsubscribe_parsing() {
        let u = parse_unsubscribe("<https://news.example/unsub?u=4f2a9c>, <mailto:unsubscribe@news.example?subject=unsubscribe>", Some("List-Unsubscribe=One-Click")).unwrap();
        assert_eq!(u.http.as_deref(), Some("https://news.example/unsub?u=4f2a9c"));
        assert_eq!(u.mailto.as_deref(), Some("mailto:unsubscribe@news.example?subject=unsubscribe"));
        assert!(u.one_click && u.prefers_http());
        // one-click needs https and the exact token
        assert!(!parse_unsubscribe("<http://n.example/u>", Some("List-Unsubscribe=One-Click")).unwrap().one_click);
        assert!(!parse_unsubscribe("<https://n.example/u>", None).unwrap().one_click);
        let m = parse_unsubscribe("<mailto:leave@list.example?subject=Leave%20me+now&body=bye>", None).unwrap();
        assert!(!m.prefers_http());
        assert_eq!(unsubscribe_mailto(m.mailto.as_deref().unwrap()), Some(("leave@list.example".into(), Some("Leave me+now".into()), Some("bye".into()))));
        // refused: javascript links, no brackets, several or invalid recipients
        assert!(parse_unsubscribe("<javascript:alert(1)>", None).is_none());
        assert!(parse_unsubscribe("https://n.example/u", None).is_none());
        assert!(parse_unsubscribe("<mailto:a@x.org,b@x.org>", None).is_none());
        assert!(unsubscribe_mailto("mailto:not-an-address").is_none());
        // subject made one line and capped, body capped
        let (_, s, b) = unsubscribe_mailto(&format!("mailto:a@x.org?subject=a%0D%0Ab&body={}", "x".repeat(600))).unwrap();
        assert_eq!(s.as_deref(), Some("a b"));
        assert_eq!(b.unwrap().len(), 500);
        assert!(valid_email("a.b+c@sub.example.org"));
        assert!(!valid_email("a@b") && !valid_email("a b@c.org") && !valid_email("@c.org") && !valid_email("a@@c.org"));
    }

    #[test]
    fn dkim_gate() {
        let pass = vec![json!({"name": "Authentication-Results", "value": " mx.x.org; dkim=pass header.d=news.example; spf=pass"}),
            json!({"name": "Authentication-Results", "value": " evil; dkim=fail"})];
        assert!(dkim_pass(&pass));
        let fail = vec![json!({"name": "Authentication-Results", "value": " mx.x.org; dkim=fail; spf=pass"})];
        assert!(!dkim_pass(&fail));
        assert!(!dkim_pass(&[]));
        assert!(one_click_post("http://insecure.example/u").is_err());
    }

    #[test]
    fn one_click_posts_the_form_body() {
        use std::io::{Read, Write};
        let l = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
        let port = l.local_addr().unwrap().port();
        let srv = std::thread::spawn(move || {
            let (mut s, _) = l.accept().unwrap();
            s.set_read_timeout(Some(std::time::Duration::from_secs(5))).unwrap();
            let mut buf = vec![0u8; 4096];
            let mut got = String::new();
            while !got.contains("One-Click") {
                let n = s.read(&mut buf).unwrap();
                if n == 0 { break; }
                got.push_str(&String::from_utf8_lossy(&buf[..n]));
            }
            s.write_all(b"HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n").unwrap();
            got
        });
        post_form(&format!("http://127.0.0.1:{port}/unsub?u=1")).unwrap();
        let req = srv.join().unwrap();
        assert!(req.starts_with("POST /unsub?u=1 HTTP/1.1"));
        assert!(req.to_lowercase().contains("content-type: application/x-www-form-urlencoded"));
        assert!(req.ends_with("List-Unsubscribe=One-Click"));
        assert!(!req.to_lowercase().contains("cookie") && !req.to_lowercase().contains("authorization"));
    }

    #[test]
    fn names() {
        assert_eq!(strip_subject_prefixes("Re: Fwd: AW: hello"), "hello");
        assert_eq!(strip_subject_prefixes("RE[2]: x"), "x");
        assert_eq!(strip_subject_prefixes("Regarding: x"), "Regarding: x");
        assert_eq!(forward_subject("Re: Lunch"), "Fwd: Lunch");
        assert_eq!(forward_subject(""), "");
        assert_eq!(sanitize_part("a/b:c??  d.", 80), "a_b_c_ d");
        assert_eq!(bundle_name("Q3 report: final"), "attachments_Q3 report_ final.zip");
        assert_eq!(bundle_name("  "), "attachments.zip");
        let m = json!({"subject": "Invoice 12/2026", "receivedAt": "2026-11-16T09:05:07Z"});
        let n = eml_export_name(&m);
        assert!(n.ends_with("-Invoice 12_2026.eml") && n.starts_with("2026-11-16 "), "{n}");
    }

    #[test]
    fn truncation_and_source_cut() {
        let m = json!({"htmlBody": [{"partId": "2"}], "textBody": [{"partId": "1"}], "bodyValues": {"1": {"value": "a", "isTruncated": false}, "2": {"value": "b", "isTruncated": true}}});
        assert!(displayed_truncated(&m));
        let ok = json!({"textBody": [{"partId": "1"}], "bodyValues": {"1": {"value": "a", "isTruncated": false}}, "attachments": [{"partId": "9"}]});
        assert!(!displayed_truncated(&ok));
        let small = "abc";
        assert_eq!(source_shown(small), ("abc", false));
        let big = format!("{}é", "x".repeat(SOURCE_DISPLAY_LIMIT - 1));
        let (s, cut) = source_shown(&big);
        assert!(cut && s.len() == SOURCE_DISPLAY_LIMIT - 1);
    }

    #[test]
    fn files_tree_and_nodes() {
        let nodes = vec![
            json!({"id": "a", "name": "Work", "nodeType": "directory", "parentId": null}),
            json!({"id": "b", "name": "2026", "nodeType": "directory", "parentId": "a"}),
            json!({"id": "t", "name": ".Trash", "nodeType": "directory", "parentId": null}),
            json!({"id": "t1", "name": "old", "nodeType": "directory", "parentId": "t"}),
            json!({"id": "f", "name": "x.pdf", "nodeType": "file", "parentId": "a", "blobId": "B"}),
            json!({"id": "c", "@type": "Directory", "name": "archive", "parentId": null}),
        ];
        assert_eq!(folder_tree(&nodes), vec![("c".into(), "archive".into(), 0), ("a".into(), "Work".into(), 0), ("b".into(), "2026".into(), 1)]);
        assert_eq!(file_node(true, Some("a"), "x.pdf", "B", "application/pdf"), json!({"@type": "File", "parentId": "a", "name": "x.pdf", "blobId": "B", "mediaType": "application/pdf"}));
        assert_eq!(file_node(false, None, "x", "B", "t"), json!({"parentId": null, "name": "x", "blobId": "B", "type": "t"}));
    }

    #[test]
    fn file_urls() {
        assert_eq!(file_url(r"C:\Users\a b\Temp\Barbican\reader-1.html"), "file:///C:/Users/a%20b/Temp/Barbican/reader-1.html");
        assert_eq!(file_url("/tmp/x#1.html"), "file:///tmp/x%231.html");
    }

    #[test]
    fn message_attachments() {
        assert!(is_message_attachment(&json!({"type": "message/rfc822"})));
        assert!(is_message_attachment(&json!({"type": "application/octet-stream", "name": "Fwd.EML"})));
        assert!(!is_message_attachment(&json!({"type": "application/pdf", "name": "a.pdf"})));
    }
}
