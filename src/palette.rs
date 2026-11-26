//! The command palette (Ctrl+K) and "Search everything".
//!
//! Bulwark: components/command-palette/command-palette.tsx (the commands, grouped: the current
//! app's own first, then Go to, Actions, Account), lib/command-registry.ts (commandMatches,
//! every word in the label or the keywords), lib/app-commands.ts (commands that need the mail
//! page), components/global-search/global-search-palette.tsx (one query across mail, contacts,
//! calendar and files; scope chips; recent searches; five rows per kind; Enter opens a hit).

use crate::gsearch::{self, Hit, Parsed, Scope};
use crate::{AppWindow, PaletteRow, Pd, Shared};
use slint::{ComponentHandle, Model, ModelRc, VecModel};
use std::cell::RefCell;
use std::collections::HashMap;

// stroke icons, 24 x 24 path data in the style of design/mockups (`I`)
const I_MAIL: &str = "M4 4h16v16H4z M22 6l-10 7L2 6";
const I_CAL: &str = "M3 4h18v18H3z M16 2v4 M8 2v4 M3 10h18";
const I_PEOPLE: &str = "M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2 M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8z";
const I_FOLDER: &str = "M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z";
const I_SETTINGS: &str = "M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z";
const I_ADMIN: &str = "M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z";
const I_COMPOSE: &str = "M12 20h9 M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z";
const I_SEARCH: &str = "M11 19a8 8 0 1 0 0-16 8 8 0 0 0 0 16z M21 21l-4.35-4.35";
const I_KEYS: &str = "M2 6h20v12H2z M6 10h.01 M10 10h.01 M14 10h.01 M18 10h.01 M7 14h10";
const I_SUN: &str = "M12 17a5 5 0 1 0 0-10 5 5 0 0 0 0 10z M12 1v2 M12 21v2 M4.2 4.2l1.4 1.4 M18.4 18.4l1.4 1.4 M1 12h2 M21 12h2 M4.2 19.8l1.4-1.4 M18.4 5.6l1.4-1.4";
const I_MOON: &str = "M21 12.8A9 9 0 1 1 11.2 3 7 7 0 0 0 21 12.8z";
const I_USER: &str = "M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2 M12 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8z";
const I_SIGNOUT: &str = "M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4 M16 17l5-5-5-5 M21 12H9";
const I_REPLY: &str = "M9 17l-5-5 5-5 M20 18v-2a4 4 0 0 0-4-4H4";
const I_REPLYALL: &str = "M7 17l-5-5 5-5 M12 17l-5-5 5-5 M22 18v-2a4 4 0 0 0-4-4H7";
const I_FORWARD: &str = "M15 17l5-5-5-5 M4 18v-2a4 4 0 0 1 4-4h12";
const I_ARCHIVE: &str = "M21 8v13H3V8 M1 3h22v5H1z M10 12h4";
const I_DELETE: &str = "M3 6h18 M19 6l-1 14H6L5 6 M8 6V4h8v2";
const I_STAR: &str = "M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01z";
const I_READ: &str = "M3 9l9 6 9-6 M3 9v11h18V9l-9-6z";
const I_SPAM: &str = "M12 9v4 M12 17h.01 M10.3 3.9 1.8 18h20.4L13.7 3.9a2 2 0 0 0-3.4 0z";
const I_ADD: &str = "M12 5v14 M5 12h14";
const I_REFRESH: &str = "M23 4v6h-6 M1 20v-6h6 M3.51 9a9 9 0 0 1 14.85-3.36L23 10 M1 14l4.64 4.36A9 9 0 0 0 20.49 15";
const I_UPLOAD: &str = "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 M17 8l-5-5-5 5 M12 3v12";
const I_TRASH: &str = "M3 6h18 M19 6l-1 14H6L5 6 M8 6V4h8v2";
const I_A11Y: &str = "M12 6a2 2 0 1 0 0-4 2 2 0 0 0 0 4z M5 8l7 2 7-2 M12 10v5 M9 22l3-7 3 7";
const I_FILE: &str = "M14 2H6v20h12V8z M14 2v6h6";
const I_CLOCK: &str = "M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20z M12 6v6l4 2";
const I_TODAY: &str = "M3 4h18v18H3z M16 2v4 M8 2v4 M3 10h18 M10 15h4";

#[derive(Clone, Debug, PartialEq)]
pub struct Cmd {
    pub id: String,
    pub group: String,
    pub icon: &'static str,
    pub label: String,
    pub keywords: Vec<String>,
    pub shortcut: String,
}

fn cmd(id: &str, group: &str, icon: &'static str, label: &str, shortcut: &str) -> Cmd {
    Cmd { id: id.into(), group: group.into(), icon, label: label.into(), keywords: vec![], shortcut: shortcut.into() }
}

/// Bulwark commandMatches: every word of the needle in the label or the keywords.
pub fn command_matches(needle: &str, label: &str, keywords: &[String]) -> bool {
    let words: Vec<String> = needle.split_whitespace().map(|w| w.to_lowercase()).collect();
    if words.is_empty() { return true; }
    let hay = std::iter::once(label.to_string()).chain(keywords.iter().cloned()).collect::<Vec<_>>().join(" ").to_lowercase();
    words.iter().all(|w| hay.contains(w.as_str()))
}

/// What the palette offers depends on where it was opened.
#[derive(Clone, Debug, Default)]
pub struct Ctx {
    pub app: i32,
    pub settings_open: bool,
    pub settings_tab: String,
    pub settings_tabs: Vec<(String, String)>,
    pub has_mail: bool,
    pub mail_unread: bool,
    pub mail_flagged: bool,
    pub in_junk: bool,
    pub cal_view: i32,
    pub in_trash: bool,
    pub calendars: bool,
    pub contacts: bool,
    pub files: bool,
    pub admin: bool,
    pub dark: bool,
    pub single_keys: bool,
    pub accounts: Vec<String>,
    pub account: String,
}

/// The palette's commands in Bulwark's order: the open app's own, Go to, Actions, Account.
pub fn commands(x: &Ctx) -> Vec<Cmd> {
    let mut v: Vec<Cmd> = vec![];
    // single-key hints only while single-key shortcuts are on
    let k = |s: &str| if x.single_keys || !crate::shortcuts::is_single_key(s) { s.to_string() } else { String::new() };
    if x.settings_open {
        for (id, label) in &x.settings_tabs {
            if *id == x.settings_tab { continue; }
            let mut c = cmd(&format!("settings:{id}"), "Settings", I_SETTINGS, &format!("Go to {label} settings"), "");
            c.keywords = vec!["settings".into()];
            v.push(c);
        }
    } else {
        match x.app {
            0 if x.has_mail => {
                let g = "Mail";
                v.push(cmd("mail:reply", g, I_REPLY, "Reply", &k("r")));
                v.push(cmd("mail:reply-all", g, I_REPLYALL, "Reply all", &k("a")));
                v.push(cmd("mail:forward", g, I_FORWARD, "Forward", &k("f")));
                v.push(cmd("mail:archive", g, I_ARCHIVE, "Archive", &k("e")));
                v.push(cmd("mail:delete", g, I_DELETE, "Delete", &k("#")));
                if x.mail_unread { v.push(cmd("mail:read", g, I_READ, "Mark as read", "Shift + I")); }
                else { v.push(cmd("mail:unread", g, I_MAIL, "Mark as unread", &k("u"))); }
                v.push(cmd("mail:star", g, I_STAR, if x.mail_flagged { "Unstar" } else { "Star" }, &k("s")));
                v.push(cmd("mail:spam", g, I_SPAM, if x.in_junk { "Not spam" } else { "Mark as spam" }, &k("!")));
            }
            1 => {
                let g = "Calendar";
                v.push(cmd("cal:new", g, I_ADD, "New event", &k("n")));
                v.push(cmd("cal:search", g, I_SEARCH, "Search events", &k("/")));
                v.push(cmd("cal:today", g, I_TODAY, "Go to today", &k("t")));
                for (i, (key, label)) in [("d", "Day view"), ("w", "Week view"), ("m", "Month view")].iter().enumerate() {
                    if x.cal_view != i as i32 { v.push(cmd(&format!("cal:view{i}"), g, I_CAL, label, &k(key))); }
                }
                v.push(cmd("cal:refresh", g, I_REFRESH, "Refresh", "F5"));
            }
            2 => {
                let g = "Contacts";
                v.push(cmd("ct:new", g, I_ADD, "New contact", ""));
                v.push(cmd("ct:group", g, I_PEOPLE, "New group", ""));
            }
            3 => {
                let g = "Files";
                if !x.in_trash {
                    v.push(cmd("fl:upload", g, I_UPLOAD, "Upload files", ""));
                    v.push(cmd("fl:upload-folder", g, I_UPLOAD, "Upload folder", ""));
                    v.push(cmd("fl:new-folder", g, I_FOLDER, "New folder", ""));
                    v.push(cmd("fl:trash", g, I_TRASH, "Trash", ""));
                } else {
                    v.push(cmd("fl:empty-trash", g, I_TRASH, "Empty trash", ""));
                }
                v.push(cmd("fl:refresh", g, I_REFRESH, "Refresh", ""));
            }
            _ => {}
        }
    }
    let nav = "Go to";
    v.push(cmd("go:mail", nav, I_MAIL, "Go to Mail", ""));
    if x.calendars { v.push(cmd("go:calendar", nav, I_CAL, "Go to Calendar", "")); }
    if x.contacts { v.push(cmd("go:contacts", nav, I_PEOPLE, "Go to Contacts", "")); }
    if x.files { v.push(cmd("go:files", nav, I_FOLDER, "Go to Files", "")); }
    v.push(cmd("go:settings", nav, I_SETTINGS, "Go to Settings", ""));
    if x.admin { v.push(cmd("go:admin", nav, I_ADMIN, "Go to Stalwart admin", "")); }
    // "c" and "/" compose and search only in Mail (another app gives the same keys its own meaning)
    let in_mail = x.app == 0 && !x.settings_open;
    let act = "Actions";
    v.push(cmd("compose", act, I_COMPOSE, "Compose", &if in_mail { k("c") } else { String::new() }));
    v.push(cmd("search", act, I_SEARCH, "Search mail", &if in_mail { k("/") } else { String::new() }));
    let mut all = cmd("search-everything", act, I_SEARCH, "Search everything", "");
    all.keywords = vec!["Contacts".into(), "Calendar".into(), "Files".into()];
    v.push(all);
    v.push(cmd("theme", act, if x.dark { I_SUN } else { I_MOON }, if x.dark { "Switch to light theme" } else { "Switch to dark theme" }, ""));
    v.push(cmd("shortcuts", act, I_KEYS, "Show keyboard shortcuts", &k("?")));
    let mut a11y = cmd("a11y", "Accessibility", I_A11Y, "Open the accessibility options", "Alt + Shift + A");
    a11y.keywords = vec!["contrast".into(), "font".into(), "motion".into(), "focus".into()];
    v.push(a11y);
    let acct = "Account";
    for a in &x.accounts {
        if *a == x.account { continue; }
        let mut c = cmd(&format!("account:{a}"), acct, I_USER, &format!("Switch to {a}"), "");
        c.keywords = vec!["Switch account".into(), a.clone()];
        v.push(c);
    }
    v.push(cmd("sign-out", acct, I_SIGNOUT, &if x.account.is_empty() { "Sign out".to_string() } else { format!("Sign out of {}", x.account) }, ""));
    v
}

pub fn filter(cmds: &[Cmd], needle: &str) -> Vec<Cmd> {
    cmds.iter().filter(|c| command_matches(needle, &c.label, &c.keywords)).cloned().collect()
}

// ---------------- UI state ----------------

#[derive(Default)]
struct State {
    mode: String,
    cmds: Vec<Cmd>,
    /// rows on screen: what each runs (command id, or "hit:<n>", or "recent:<query>")
    actions: Vec<String>,
    hits: HashMap<&'static str, Vec<Hit>>,
    loading: Vec<&'static str>,
    errors: Vec<String>,
    parsed: Parsed,
    generation: u64,
    shown: Vec<Hit>,
}

thread_local! {
    static ST: RefCell<State> = RefCell::new(State::default());
    static DEBOUNCE: crate::ui_types::Timer = crate::ui_types::Timer::default();
    static STATE: RefCell<Option<Shared>> = const { RefCell::new(None) };
}

fn kind_key(s: Scope) -> &'static str { s.name() }

fn row(header: &str, icon: &str, label: &str, hint: &str, sub: &str, action: &str) -> PaletteRow {
    PaletteRow { header: header.into(), icon: icon.into(), label: label.into(), hint: hint.into(), sub: sub.into(), action: action.into() }
}

fn context(u: &AppWindow, st: &Shared) -> Ctx {
    let c = st.lock().unwrap().clone();
    let has = |cap: &str| c.as_ref().is_some_and(|c| c.has(cap));
    let cur = u.get_current_mail().to_string();
    let m = crate::mail::email(&cur);
    let in_junk = m.as_ref().and_then(|m| m["mailboxIds"].as_object().map(|o| o.keys().any(|k| crate::mail::mailbox_role(k) == "junk"))).unwrap_or(false);
    let adv = u.get_show_advanced_settings();
    let tabs = u.get_settings_tabs();
    let mut settings_tabs: Vec<(String, String)> = (0..tabs.row_count()).filter_map(|i| tabs.row_data(i)).map(|t| (t.id.to_string(), t.label.to_string())).collect();
    if settings_tabs.is_empty() {
        let t = crate::settings::Settings::load().tabs(adv);
        settings_tabs = (0..t.row_count()).filter_map(|i| t.row_data(i)).map(|t| (t.id.to_string(), t.label.to_string())).collect();
    }
    Ctx {
        app: u.get_app(),
        settings_open: u.get_show_settings(),
        settings_tab: u.get_settings_tab().to_string(),
        settings_tabs,
        has_mail: !cur.is_empty(),
        mail_unread: m.as_ref().is_some_and(|m| m["keywords"]["$seen"].as_bool() != Some(true)),
        mail_flagged: u.get_r_flagged(),
        in_junk,
        cal_view: u.get_cal_view(),
        in_trash: u.get_fl_in_trash(),
        calendars: has("urn:ietf:params:jmap:calendars"),
        contacts: has("urn:ietf:params:jmap:contacts"),
        files: has("urn:ietf:params:jmap:filenode"),
        admin: u.get_admin_enabled(),
        dark: u.global::<crate::Theme>().get_dark(),
        single_keys: crate::a11y::A11y::load().single_keys,
        accounts: crate::oauth::accounts().iter().filter_map(|a| a["user"].as_str().map(String::from)).collect(),
        account: u.get_account().to_string(),
    }
}

/// Open the palette: "commands" (Ctrl+K) or "search" (Search everything).
pub fn open(u: &AppWindow, mode: &str) {
    let Some(st) = STATE.with(|s| s.borrow().clone()) else { return };
    let pd = u.global::<Pd>();
    let cmds = commands(&context(u, &st));
    ST.with(|s| { let mut s = s.borrow_mut(); s.mode = mode.into(); s.cmds = cmds; s.generation += 1; s.hits.clear(); s.loading.clear(); s.errors.clear(); s.parsed = Parsed::default(); });
    pd.set_palette_mode(mode.into());
    pd.set_palette_query("".into());
    pd.set_palette_scope("all".into());
    pd.set_palette_status("".into());
    pd.set_palette_active(if mode == "commands" { 0 } else { -1 });
    rebuild(u);
    pd.set_palette_open(true);
    pd.set_palette_focus(pd.get_palette_focus() + 1);
}

pub fn close(u: &AppWindow) {
    let pd = u.global::<Pd>();
    if !pd.get_palette_open() { return; }
    pd.set_palette_open(false);
    ST.with(|s| s.borrow_mut().generation += 1);
    u.invoke_focus_keys();
}

fn rebuild(u: &AppWindow) {
    let pd = u.global::<Pd>();
    let q = pd.get_palette_query().to_string();
    let mut rows: Vec<PaletteRow> = vec![];
    let mut actions: Vec<String> = vec![];
    ST.with(|s| {
        let mut s = s.borrow_mut();
        if s.mode == "commands" {
            let mut last = String::new();
            for c in filter(&s.cmds, &q) {
                let header = if c.group != last { last = c.group.clone(); c.group.clone() } else { String::new() };
                rows.push(row(&header, c.icon, &c.label, &c.shortcut, "", &c.id));
                actions.push(c.id.clone());
            }
            pd.set_palette_empty(if rows.is_empty() { "No matching commands".into() } else { "".into() });
        } else {
            let trimmed = q.trim();
            let mut shown = vec![];
            if trimmed.is_empty() {
                let recent = crate::search_history::load();
                for (i, r) in recent.iter().enumerate() {
                    rows.push(row(if i == 0 { "Recent searches" } else { "" }, I_CLOCK, r, "", "", &format!("recent:{r}")));
                    actions.push(format!("recent:{r}"));
                }
                pd.set_palette_empty(if recent.is_empty() { "Search mail, contacts, events and files. Try from:, subject:, has:attachment, after:2026-01-01.".into() } else { "".into() });
            } else {
                let scope = s.parsed.scope;
                for kind in [Scope::Mail, Scope::Contacts, Scope::Calendar, Scope::Files] {
                    if scope != Scope::All && scope != kind { continue; }
                    let key = kind_key(kind);
                    let hits = s.hits.get(key).cloned().unwrap_or_default();
                    let loading = s.loading.contains(&key);
                    if hits.is_empty() && !loading { continue; }
                    let label = match kind { Scope::Mail => "Mail", Scope::Contacts => "Contacts", Scope::Calendar => "Calendar", _ => "Files" };
                    let head = format!("{label}  {}{}", hits.len(), if loading { "  …" } else { "" });
                    let icon = match kind { Scope::Mail => I_MAIL, Scope::Contacts => I_PEOPLE, Scope::Calendar => I_CAL, _ => I_FILE };
                    if hits.is_empty() {
                        rows.push(row(&head, "", "", "", "", ""));
                        actions.push(String::new());
                        continue;
                    }
                    for (i, h) in hits.iter().take(gsearch::ROWS_PER_KIND).enumerate() {
                        let date = h.date.get(..10).unwrap_or("");
                        let icon = if kind == Scope::Files && h.place == h.id { I_FOLDER } else { icon };
                        rows.push(row(if i == 0 { &head } else { "" }, icon, &h.title, if kind == Scope::Mail || kind == Scope::Files { date } else { "" }, &h.subtitle, &format!("hit:{}", shown.len())));
                        actions.push(format!("hit:{}", shown.len()));
                        shown.push(h.clone());
                    }
                }
                let busy = !s.loading.is_empty();
                pd.set_palette_empty(if rows.is_empty() && !busy { format!("No results for “{trimmed}”").into() } else { "".into() });
                pd.set_palette_status(if busy { "Searching…".into() } else if !s.errors.is_empty() { s.errors.join(" · ").into() } else { "".into() });
            }
            s.shown = shown;
        }
        s.actions = actions;
    });
    let n = rows.len() as i32;
    if pd.get_palette_active() >= n { pd.set_palette_active(n - 1); }
    pd.set_palette_rows(ModelRc::new(VecModel::from(rows)));
}

/// The text changed: filter the commands, or search (debounced, like the server pass).
pub fn edited(u: &AppWindow, text: &str) {
    let pd = u.global::<Pd>();
    pd.set_palette_query(text.into());
    let mode = ST.with(|s| s.borrow().mode.clone());
    if mode == "commands" {
        pd.set_palette_active(0);
        rebuild(u);
        return;
    }
    pd.set_palette_active(-1);
    let scope = gsearch::parse(text).scope;
    pd.set_palette_scope(scope.name().into());
    let w = u.as_weak();
    let text = text.to_string();
    DEBOUNCE.with(|t| t.start(crate::ui_types::TimerMode::SingleShot, std::time::Duration::from_millis(250), move || {
        if let Some(u) = w.upgrade() { search(&u, &text); }
    }));
    if text_is_empty(&pd.get_palette_query()) { ST.with(|s| { let mut s = s.borrow_mut(); s.hits.clear(); s.loading.clear(); s.errors.clear(); }); rebuild(u); }
}

fn text_is_empty(s: &str) -> bool { s.trim().is_empty() }

/// Fan the query out to each kind on its own thread; each publishes when it lands.
fn search(u: &AppWindow, text: &str) {
    let Some(st) = STATE.with(|s| s.borrow().clone()) else { return };
    let Some(c) = st.lock().unwrap().clone() else { return };
    let p = gsearch::parse(text);
    let kinds: Vec<Scope> = [Scope::Mail, Scope::Contacts, Scope::Calendar, Scope::Files].into_iter()
        .filter(|k| p.scope == Scope::All || p.scope == *k)
        .filter(|k| match k {
            Scope::Contacts => c.has("urn:ietf:params:jmap:contacts"),
            Scope::Calendar => c.has("urn:ietf:params:jmap:calendars"),
            Scope::Files => c.has("urn:ietf:params:jmap:filenode"),
            _ => true,
        })
        .filter(|k| if *k == Scope::Files { !p.terms.is_empty() } else { gsearch::has_remote_query(&p, *k) })
        .collect();
    let gen = ST.with(|s| {
        let mut s = s.borrow_mut();
        s.generation += 1;
        s.parsed = p.clone();
        s.hits.clear();
        s.errors.clear();
        s.loading = kinds.iter().map(|k| kind_key(*k)).collect();
        s.generation
    });
    rebuild(u);
    // folder names for mail hits, read on this thread
    let names: HashMap<String, String> = {
        let f = u.get_folders();
        (0..f.row_count()).filter_map(|i| f.row_data(i)).map(|f| (f.id.to_string(), f.name.to_string())).collect()
    };
    let roles: HashMap<String, String> = ["inbox", "trash", "junk", "sent", "drafts", "archive"].iter()
        .filter_map(|r| crate::mail::role_id(r).map(|id| (r.to_string(), id))).collect();
    let prefer: Vec<String> = vec![u.get_current_folder().to_string(), roles.get("inbox").cloned().unwrap_or_default()];
    for kind in kinds {
        let (c, p, w, names, roles, prefer) = (c.clone(), p.clone(), u.as_weak(), names.clone(), roles.clone(), prefer.clone());
        std::thread::spawn(move || {
            let res = match kind {
                Scope::Mail => gsearch::search_mail(&c, &p, &roles, &prefer, |id| names.get(id).cloned().unwrap_or_default()),
                Scope::Contacts => gsearch::search_contacts(&c, &p),
                Scope::Calendar => gsearch::search_events(&c, &p),
                _ => gsearch::file_nodes(&c).map(|n| gsearch::filter_files(&n, &p)),
            };
            let _ = w.upgrade_in_event_loop(move |u| {
                ST.with(|s| {
                    let mut s = s.borrow_mut();
                    if s.generation != gen { return; }
                    let key = kind_key(kind);
                    s.loading.retain(|k| *k != key);
                    match res {
                        Ok(h) => { let ranked = gsearch::rank(h, &s.parsed); s.hits.insert(key, ranked); }
                        Err(e) => s.errors.push(format!("{key}: {e}")),
                    }
                });
                rebuild(&u);
            });
        });
    }
}

/// A scope chip: rewrite the query's `in:` operator and search again.
pub fn set_scope(u: &AppWindow, scope: &str) {
    let pd = u.global::<Pd>();
    let q = gsearch::with_scope(&pd.get_palette_query(), Scope::from_name(scope));
    pd.set_palette_scope(scope.into());
    pd.set_palette_query(q.clone().into());
    pd.set_palette_text_req(pd.get_palette_text_req() + 1);
    if q.trim().is_empty() { rebuild(u); } else { search(u, &q); }
}

pub fn move_active(u: &AppWindow, d: i32) {
    let pd = u.global::<Pd>();
    let actions = ST.with(|s| s.borrow().actions.clone());
    let n = actions.len() as i32;
    if n == 0 { return; }
    let mut i = pd.get_palette_active();
    for _ in 0..n {
        i = if i < 0 { if d > 0 { 0 } else { n - 1 } } else { (i + d).rem_euclid(n) };
        if !actions[i as usize].is_empty() { break; }
    }
    pd.set_palette_active(i);
}

/// Enter: run the highlighted row; with none highlighted in search, open the best hit.
pub fn enter(u: &AppWindow) {
    let pd = u.global::<Pd>();
    let i = pd.get_palette_active();
    let actions = ST.with(|s| s.borrow().actions.clone());
    let pick = if i >= 0 { actions.get(i as usize).cloned() } else { actions.iter().find(|a| a.starts_with("hit:")).cloned() };
    match pick {
        Some(a) if !a.is_empty() => run(u, &a),
        _ => {
            let q = pd.get_palette_query().to_string();
            if ST.with(|s| s.borrow().mode == "search") { crate::search_history::add(&q); }
            close(u);
        }
    }
}

pub fn run_row(u: &AppWindow, i: i32) {
    let a = ST.with(|s| s.borrow().actions.get(i.max(0) as usize).cloned());
    if let Some(a) = a.filter(|a| !a.is_empty()) { run(u, &a); }
}

fn run(u: &AppWindow, action: &str) {
    let Some(st) = STATE.with(|s| s.borrow().clone()) else { return };
    let pd = u.global::<Pd>();
    if let Some(q) = action.strip_prefix("recent:") {
        // a recent search fills the box and runs
        pd.set_palette_query(q.into());
        pd.set_palette_text_req(pd.get_palette_text_req() + 1);
        search(u, q);
        return;
    }
    if let Some(n) = action.strip_prefix("hit:") {
        let hit = ST.with(|s| s.borrow().shown.get(n.parse::<usize>().unwrap_or(usize::MAX)).cloned());
        let q = pd.get_palette_query().to_string();
        crate::search_history::add(&q);
        close(u);
        if let Some(h) = hit { open_hit(u, &st, &h); }
        return;
    }
    close(u);
    run_command(u, &st, action);
}

fn go_app(u: &AppWindow, a: i32) {
    u.set_show_settings(false);
    u.set_app(a);
    u.invoke_app_changed(a);
}

pub fn run_command(u: &AppWindow, st: &Shared, id: &str) {
    let c = st.lock().unwrap().clone();
    if let Some(tab) = id.strip_prefix("settings:") { u.invoke_settings_open_tab(tab.into()); return; }
    if let Some(user) = id.strip_prefix("account:") { u.invoke_switch_account(user.into()); return; }
    if let Some(a) = id.strip_prefix("mail:") { u.invoke_act(a.into()); return; }
    match id {
        "go:mail" => go_app(u, 0),
        "go:calendar" => go_app(u, 1),
        "go:contacts" => go_app(u, 2),
        "go:files" => go_app(u, 3),
        "go:admin" => go_app(u, 4),
        "go:settings" => u.invoke_open_settings(),
        "compose" => u.invoke_compose_new(),
        "search" => { if u.get_app() != 0 || u.get_show_settings() { go_app(u, 0); } u.invoke_focus_search(); }
        "search-everything" => open(u, "search"),
        "theme" => {
            let dark = u.global::<crate::Theme>().get_dark();
            // the same setting Settings > Appearance writes (options: light, dark, system)
            u.invoke_settings_set_select("theme".into(), if dark { 0 } else { 1 });
        }
        "shortcuts" => {
            let tab = if u.get_show_settings() { 0 } else { match u.get_app() { 1 => 1, 2 => 2, 3 => 3, _ => 0 } };
            crate::shortcuts::show_help(u, tab);
        }
        "a11y" => { u.set_settings_tab("accessibility".into()); u.invoke_open_settings(); }
        "sign-out" => u.invoke_sign_out(),
        "cal:new" => u.invoke_cal_edit("".into(), "".into(), -1),
        "cal:search" => pd_focus(u),
        "cal:today" => u.invoke_cal_nav("today".into()),
        "cal:view0" => u.invoke_cal_set_view(0),
        "cal:view1" => u.invoke_cal_set_view(1),
        "cal:view2" => u.invoke_cal_set_view(2),
        "cal:refresh" => if let Some(c) = c { crate::calendar::load(u.as_weak(), c); },
        "ct:new" => u.invoke_ct_new(),
        "ct:group" => u.invoke_ct_new_group(),
        "fl:upload" => u.invoke_fl_upload(),
        "fl:upload-folder" => u.invoke_fl_upload_folder(),
        "fl:new-folder" => { crate::ctxmenu::request(u, "files-bg", ""); crate::ctxmenu::pick(u, st, "new-folder"); }
        "fl:trash" => if let Some(c) = c { crate::files::open(u, c, crate::files::TRASH_LOC); },
        "fl:empty-trash" => { crate::ctxmenu::request(u, "files-bg", ""); crate::ctxmenu::pick(u, st, "ask:empty-trash"); }
        "fl:refresh" => if let Some(c) = c { crate::files::reload(u, c); },
        _ => {}
    }
}

fn pd_focus(u: &AppWindow) {
    let pd = u.global::<Pd>();
    pd.set_focus_req(pd.get_focus_req() + 1);
}

thread_local! { static PENDING: crate::ui_types::Timer = crate::ui_types::Timer::default(); }

/// Run `f` once `ready()` holds (an app loading its data), checking every 150 ms for 8 s.
fn when_ready(u: &AppWindow, ready: impl Fn() -> bool + 'static, f: impl Fn(&AppWindow) + 'static) {
    let w = u.as_weak();
    let started = std::time::Instant::now();
    if ready() { f(u); return; }
    PENDING.with(|t| t.start(crate::ui_types::TimerMode::Repeated, std::time::Duration::from_millis(150), move || {
        let Some(u) = w.upgrade() else { return };
        if ready() { PENDING.with(|t| t.stop()); f(&u); }
        else if started.elapsed() > std::time::Duration::from_secs(8) { PENDING.with(|t| t.stop()); }
    }));
}

/// Open a hit where it lives (lib/global-search/open-hit.ts).
fn open_hit(u: &AppWindow, st: &Shared, h: &Hit) {
    let Some(c) = st.lock().unwrap().clone() else { return };
    match h.kind {
        Scope::Mail => {
            if u.get_app() != 0 || u.get_show_settings() { go_app(u, 0); }
            // show its folder, then the message
            if !h.place.is_empty() && u.get_current_folder().as_str() != h.place {
                u.set_current_tag("".into());
                u.set_current_folder(h.place.clone().into());
                u.invoke_select_folder(h.place.clone().into());
            }
            // open it once the folder's list has it (or right away when already listed)
            let id = h.id.clone();
            let id2 = id.clone();
            let w = u.as_weak();
            let listed = move || w.upgrade().is_some_and(|u| { let m = u.get_mails(); (0..m.row_count()).any(|i| m.row_data(i).is_some_and(|r| r.id == id.as_str())) });
            let opened = std::rc::Rc::new(std::cell::Cell::new(false));
            let o2 = opened.clone();
            when_ready(u, listed, move |u| { o2.set(true); u.set_current_mail(id2.clone().into()); u.invoke_open_mail(id2.clone().into()); });
            // not in the list at all (e.g. beyond the first page): open it anyway
            if !opened.get() { let (w, id) = (u.as_weak(), h.id.clone()); crate::ui_types::Timer::single_shot(std::time::Duration::from_secs(9), move || {
                if let (false, Some(u)) = (opened.get(), w.upgrade()) { u.set_current_mail(id.clone().into()); u.invoke_open_mail(id.into()); }
            }); }
        }
        Scope::Contacts => {
            if u.get_app() != 2 { go_app(u, 2); }
            let id = h.id.clone();
            let id2 = id.clone();
            when_ready(u, move || crate::contacts::has_card(&id), move |u| { u.set_ct_editing(false); u.invoke_ct_select(id2.clone().into()); });
        }
        Scope::Calendar => {
            if u.get_app() != 1 { go_app(u, 1); }
            if !h.place.is_empty() { crate::calendar::nav(u, c, &h.place); }
            let id = h.id.clone();
            let id2 = id.clone();
            when_ready(u, move || !crate::calendar::event_title(&id).is_empty(), move |u| u.invoke_cal_edit(id2.clone().into(), "".into(), -1));
        }
        _ => {
            if u.get_app() != 3 { u.set_show_settings(false); u.set_app(3); }
            crate::files::open(u, c, &h.place);
            if h.place != h.id { u.global::<Pd>().set_file_sel(h.id.clone().into()); }
        }
    }
}

pub fn wire(u: &AppWindow, st: Shared) {
    STATE.with(|s| *s.borrow_mut() = Some(st));
    let pd = u.global::<Pd>();
    let w = u.as_weak();
    pd.on_palette_edited(move |t| { if let Some(u) = w.upgrade() { edited(&u, &t); } });
    let w = u.as_weak();
    pd.on_palette_move(move |d| { if let Some(u) = w.upgrade() { move_active(&u, d); } });
    let w = u.as_weak();
    pd.on_palette_enter(move || { if let Some(u) = w.upgrade() { enter(&u); } });
    let w = u.as_weak();
    pd.on_palette_run(move |i| { if let Some(u) = w.upgrade() { run_row(&u, i); } });
    let w = u.as_weak();
    pd.on_palette_close(move || { if let Some(u) = w.upgrade() { close(&u); } });
    let w = u.as_weak();
    pd.on_palette_set_scope(move |s| { if let Some(u) = w.upgrade() { set_scope(&u, &s); } });
    let w = u.as_weak();
    pd.on_open_search_everything(move || { if let Some(u) = w.upgrade() { open(&u, "search"); } });
}

#[cfg(test)]
mod tests {
    use super::*;

    fn base() -> Ctx {
        Ctx { calendars: true, contacts: true, files: true, single_keys: true, dark: true, account: "me@x.org".into(),
              accounts: vec!["me@x.org".into(), "other@y.org".into()], ..Default::default() }
    }

    #[test]
    fn matches_every_word() {
        assert!(command_matches("go cal", "Go to Calendar", &[]));
        assert!(command_matches("", "Anything", &[]));
        assert!(command_matches("files", "Search everything", &["Contacts".into(), "Files".into()]));
        assert!(!command_matches("go mail x", "Go to Mail", &[]));
    }

    #[test]
    fn app_commands_come_first() {
        let mut x = base();
        x.app = 1;
        x.cal_view = 1;
        let v = commands(&x);
        assert_eq!(v[0].group, "Calendar");
        assert_eq!(v[0].label, "New event");
        // the current view is not offered
        assert!(!v.iter().any(|c| c.label == "Week view"));
        assert!(v.iter().any(|c| c.label == "Day view" && c.shortcut == "d"));
        // compose has no "c" hint outside Mail (Calendar's n / c means new event)
        assert_eq!(v.iter().find(|c| c.id == "compose").unwrap().shortcut, "");
    }

    #[test]
    fn mail_commands_need_an_open_message() {
        let mut x = base();
        assert!(!commands(&x).iter().any(|c| c.group == "Mail"));
        x.has_mail = true;
        x.mail_unread = true;
        let v = commands(&x);
        assert!(v.iter().any(|c| c.id == "mail:read"));
        assert!(!v.iter().any(|c| c.id == "mail:unread"));
        assert_eq!(v.iter().find(|c| c.id == "compose").unwrap().shortcut, "c");
        x.single_keys = false;
        assert_eq!(commands(&x).iter().find(|c| c.id == "mail:reply").unwrap().shortcut, "");
    }

    #[test]
    fn accounts_and_capabilities() {
        let mut x = base();
        let v = commands(&x);
        assert!(v.iter().any(|c| c.label == "Switch to other@y.org"));
        assert!(!v.iter().any(|c| c.label == "Switch to me@x.org"));
        assert_eq!(v.last().unwrap().label, "Sign out of me@x.org");
        x.files = false;
        assert!(!commands(&x).iter().any(|c| c.id == "go:files"));
        assert_eq!(filter(&commands(&x), "theme")[0].label, "Switch to light theme");
    }

    #[test]
    fn settings_pages() {
        let mut x = base();
        x.settings_open = true;
        x.settings_tab = "general".into();
        x.settings_tabs = vec![("general".into(), "General".into()), ("appearance".into(), "Appearance".into())];
        let v = commands(&x);
        assert_eq!(v[0].label, "Go to Appearance settings");
        assert!(!v.iter().any(|c| c.label == "Go to General settings"));
    }
}
