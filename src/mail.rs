//! Mail view state (folders, tags, message list with paging and selection) and actions.
//! State lives on the UI thread; network work runs on background threads.
use crate::jmap::Client;
use crate::{AppWindow, Folder, Mail, Tag};
use chrono::{DateTime, Datelike, Local};
use serde_json::{json, Value};
use slint::{Color, ComponentHandle, Model, ModelRc, VecModel};
use std::cell::RefCell;
use std::collections::{HashMap, HashSet};
use std::sync::Arc;

#[derive(Default)]
struct ListState {
    filter: Value,
    emails: Vec<Value>,
    threads: HashMap<String, usize>,
    total: usize,
    checked: HashSet<String>,
    loading: bool,
    generation: u64,
    /// States of what is loaded, for Email/queryChanges + Email/changes + Thread/changes.
    sync: Option<SyncStates>,
    syncing: bool,
    sync_again: bool,
}

#[derive(Clone, PartialEq, Debug)]
struct SyncStates { account: String, query: String, email: String, thread: String }

fn states_of(c: &Client, p: &crate::jmap::ListPage) -> Option<SyncStates> {
    Some(SyncStates { account: c.state_key(), query: p.query_state.clone()?, email: p.email_state.clone()?, thread: p.thread_state.clone()? })
        .filter(|_| p.can_calculate_changes)
}

#[derive(Default)]
struct BoxState {
    list: Vec<Value>,
    collapsed: HashSet<String>,
    /// (Client::state_key, Mailbox state) of `list`, for Mailbox/changes.
    state: Option<(String, String)>,
}

thread_local! {
    static LIST: RefCell<ListState> = RefCell::new(ListState::default());
    static BOXES: RefCell<BoxState> = RefCell::new(BoxState::default());
}

pub const PAGE: usize = 50;

/// Bulwark list settings read from prefs (Settings > Message list / General).
struct ListPrefs {
    group_by_date: bool, show_preview: bool, date_format: String, h12: bool, per_page: usize,
    attachment_chips: bool, codes: bool, tint_rows: bool, hide_inline_images: bool,
}

fn list_prefs() -> ListPrefs {
    let p = crate::settings::Settings::load();
    ListPrefs {
        // Bulwark's default is false; Barbican kept its date groups until the user turns them off
        group_by_date: p.bool("groupListByDate", true),
        show_preview: p.bool("showPreview", true),
        date_format: p.str("dateFormat", "smart"),
        h12: p.str("timeFormat", "24h") == "12h",
        per_page: p.prefs.get("emailsPerPage").and_then(|v| v.as_u64()).unwrap_or(50).clamp(10, 200) as usize,
        // Bulwark shows attachment chips only in the regular and comfortable densities
        attachment_chips: p.bool("showAttachmentChips", true) && matches!(p.str("density", "regular").as_str(), "regular" | "comfortable"),
        codes: p.bool("showVerificationCodes", true),
        tint_rows: p.bool("tintListRowsByTag", false),
        hide_inline_images: p.bool("hideInlineImageAttachments", true),
    }
}

pub fn page_size() -> usize { list_prefs().per_page }

pub const TAGS: [(&str, &str, u32); 7] = [
    ("red", "Red", 0xef4444), ("orange", "Orange", 0xf97316), ("yellow", "Yellow", 0xeab308),
    ("green", "Green", 0x22c55e), ("blue", "Blue", 0x3b82f6), ("purple", "Purple", 0xa855f7), ("pink", "Pink", 0xec4899),
];

fn rgb(c: u32) -> Color {
    Color::from_argb_encoded(0xff000000 | c)
}

fn tag_color(id: &str) -> Color {
    TAGS.iter().find(|t| t.0 == id).map(|t| rgb(t.2)).unwrap_or(rgb(0x9ca3af))
}

// ---------------- folders ----------------

fn role_icon(role: &str) -> &'static str {
    match role {
        "inbox" => "\u{E715}", "drafts" => "\u{E70B}", "sent" => "\u{E724}", "archive" => "\u{E7B8}",
        "junk" => "\u{E8F8}", "trash" => "\u{E74D}", _ => "\u{E8B7}",
    }
}

fn role_color(role: &str) -> Color {
    rgb(match role {
        "inbox" => 0x818cf8, "drafts" => 0xa78bfa, "sent" => 0x34d399, "archive" => 0xf59e0b,
        "junk" => 0xf87171, "trash" => 0xa1a1aa, _ => 0xd4d4d4,
    })
}

fn role_rank(role: &str) -> i64 {
    match role { "inbox" => 0, "drafts" => 1, "sent" => 2, "archive" => 3, "junk" => 4, "trash" => 5, _ => 10 }
}

fn count_text(role: &str, unread: i64, total: i64) -> String {
    if unread > 0 && total > unread && role != "inbox" && role != "" { format!("{unread} / {total}") }
    else if unread > 0 { unread.to_string() }
    else if total > 0 && matches!(role, "drafts" | "sent" | "junk" | "") { total.to_string() }
    else { String::new() }
}

pub fn rebuild_folders(u: &AppWindow) {
    let prefs = crate::settings::Settings::load();
    let colorful = prefs.bool("colorfulSidebarIcons", true);
    let totals = prefs.bool("showFolderTotalCount", true);
    BOXES.with(|b| {
        let b = b.borrow();
        let mut children: HashMap<String, Vec<&Value>> = HashMap::new();
        for m in &b.list {
            children.entry(m["parentId"].as_str().unwrap_or("").to_string()).or_default().push(m);
        }
        for v in children.values_mut() {
            v.sort_by_key(|m| (role_rank(m["role"].as_str().unwrap_or("")), m["sortOrder"].as_i64().unwrap_or(0), m["name"].as_str().unwrap_or("").to_lowercase()));
        }
        fn walk(p: &str, d: i32, ch: &HashMap<String, Vec<&Value>>, col: &HashSet<String>, out: &mut Vec<Folder>, colorful: bool, totals: bool) {
            let Some(kids) = ch.get(p) else { return };
            for m in kids {
                let id = m["id"].as_str().unwrap_or("");
                let role = m["role"].as_str().unwrap_or("");
                let has_kids = ch.contains_key(id);
                let collapsed = col.contains(id);
                let (unread, total) = (m["unreadEmails"].as_i64().unwrap_or(0), m["totalEmails"].as_i64().unwrap_or(0));
                let name = match role {
                    "inbox" => "Inbox", "sent" => "Sent", "trash" => "Trash", "junk" => "Spam",
                    "drafts" => "Drafts", "archive" => "Archive", _ => m["name"].as_str().unwrap_or(""),
                };
                out.push(Folder {
                    id: id.into(),
                    name: name.into(),
                    icon: role_icon(role).into(),
                    depth: d,
                    unread: unread as i32,
                    total: total as i32,
                    color: if colorful { role_color(role) } else { rgb(0xa3a3a3) },
                    count: (if totals { count_text(role, unread, total) } else if unread > 0 { unread.to_string() } else { String::new() }).into(),
                    chevron: if has_kids { if collapsed { 2 } else { 1 } } else { 0 },
                    muted: m["isSubscribed"].as_bool() == Some(false),
                });
                if !collapsed { walk(id, d + 1, ch, col, out, colorful, totals); }
                if role == "drafts" && d == 0 {
                    out.push(Folder {
                        id: "__scheduled".into(), name: "Scheduled".into(), icon: "\u{E787}".into(), depth: 0,
                        unread: 0, total: 0, color: rgb(0x60a5fa), count: "".into(), chevron: 0, muted: false,
                    });
                }
            }
        }
        let mut out = vec![];
        if crate::unified::enabled() {
            out.push(Folder {
                id: crate::unified::FOLDER_ID.into(), name: "All inboxes".into(), icon: "\u{E715}".into(), depth: 0,
                unread: 0, total: 0, color: rgb(0xc8234a), count: "".into(), chevron: 0, muted: false,
            });
        }
        walk("", 0, &children, &b.collapsed, &mut out, colorful, totals);
        u.set_folders(ModelRc::new(VecModel::from(out)));
    });
}

pub fn toggle_folder(u: &AppWindow, id: &str) {
    BOXES.with(|b| {
        let mut b = b.borrow_mut();
        if !b.collapsed.remove(id) { b.collapsed.insert(id.to_string()); }
    });
    rebuild_folders(u);
}

pub fn mailbox_role(id: &str) -> String {
    BOXES.with(|b| b.borrow().list.iter().find(|m| m["id"] == id).and_then(|m| m["role"].as_str()).unwrap_or("").to_string())
}

/// Load the folder tree. Called on the UI thread with a known Mailbox state, it asks only for
/// what changed (Mailbox/changes) instead of re-reading every folder.
pub fn load_mailboxes(w: slint::Weak<AppWindow>, c: Arc<Client>, select_inbox: bool) {
    let since = if select_inbox { None } else {
        BOXES.with(|b| { let b = b.borrow(); b.state.clone().filter(|(a, _)| *a == c.state_key()).map(|(_, s)| (s, b.list.clone())) })
    };
    std::thread::spawn(move || {
        let base = since.as_ref().map(|(s, _)| (c.state_key(), s.clone()));
        let delta = since.and_then(|(s, have)| c.mailbox_delta(&s, &have).ok().flatten());
        let (list, state, base) = match delta {
            Some((list, state)) => (list, Some(state), base),
            None => match c.mailboxes_with_state() { Ok((l, s)) => (l, s, None), Err(_) => return },
        };
        let counts = tag_counts(&c);
        let w2 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| {
            let inbox = list.iter().find(|m| m["role"] == "inbox").and_then(|m| m["id"].as_str()).map(String::from);
            // a delta was merged into the list it started from; if another load replaced that
            // list meanwhile, the merge is stale and the newer list stays
            let stale = base.is_some() && BOXES.with(|b| b.borrow().state != base);
            if stale { return; }
            BOXES.with(|b| { let mut b = b.borrow_mut(); b.list = list; b.state = state.map(|s| (c.state_key(), s)); });
            rebuild_folders(&u);
            set_tags(&u, &counts);
            if select_inbox {
                if let Some(id) = inbox {
                    u.set_current_folder(id.clone().into());
                    load_list(w2, c, json!({"inMailbox": id}));
                }
            }
        });
    });
}

// ---------------- tags ----------------

fn tag_counts(c: &Client) -> HashMap<String, usize> {
    let calls: Vec<Value> = TAGS.iter().enumerate().map(|(i, t)| json!(["Email/query", {
        "accountId": c.account, "filter": {"hasKeyword": format!("$label:{}", t.0)}, "limit": 0, "calculateTotal": true
    }, format!("t{i}")])).collect();
    match c.request(Value::Array(calls)) {
        Ok(r) => TAGS.iter().zip(r.iter()).map(|(t, x)| (t.0.to_string(), x[1]["total"].as_u64().unwrap_or(0) as usize)).collect(),
        Err(_) => HashMap::new(),
    }
}

fn set_tags(u: &AppWindow, counts: &HashMap<String, usize>) {
    let v: Vec<Tag> = TAGS.iter().map(|t| Tag {
        id: t.0.into(), name: t.1.into(), color: rgb(t.2),
        count: counts.get(t.0).filter(|n| **n > 0).map(|n| n.to_string()).unwrap_or_default().into(),
    }).collect();
    u.set_tags(ModelRc::new(VecModel::from(v)));
}

// ---------------- list ----------------

fn initials(s: &str) -> String {
    let letters: String = s.chars().filter(|c| c.is_alphanumeric()).take(2).collect();
    if letters.is_empty() { "?".into() } else { letters.to_uppercase() }
}

pub fn tint(s: &str) -> Color {
    const P: [u32; 6] = [0xe07a10, 0x22a55a, 0x6366f1, 0x84b31a, 0xd9467a, 0x0ea5b7];
    let h = s.bytes().fold(7u32, |a, b| a.wrapping_mul(31).wrapping_add(b as u32));
    rgb(P[(h as usize) % P.len()])
}

fn group_label(t: &DateTime<Local>) -> String {
    let today = Local::now().date_naive();
    match (today - t.date_naive()).num_days() {
        0 => "Today".into(),
        1 => "Yesterday".into(),
        2..=6 => t.format("%A").to_string(),
        _ if t.year() == today.year() => t.format("%B").to_string(),
        _ => t.format("%B %Y").to_string(),
    }
}

fn time_label_with(t: &DateTime<Local>, lp: &ListPrefs) -> String {
    let hm = if lp.h12 { t.format("%-I:%M %p").to_string() } else { t.format("%H:%M").to_string() };
    let days = (Local::now().date_naive() - t.date_naive()).num_days();
    match lp.date_format.as_str() {
        "relative" => match days {
            0 => hm,
            1 => "Yesterday".into(),
            2..=6 => format!("{days} days ago"),
            _ => t.format("%d %b").to_string(),
        },
        "full" => format!("{} {hm}", t.format("%d/%m/%Y")),
        _ => match days { 0 => hm, 1..=6 => format!("{} {hm}", t.format("%a")), _ => t.format("%d %b").to_string() },
    }
}

#[allow(dead_code)]
fn time_label(t: &DateTime<Local>) -> String {
    match (Local::now().date_naive() - t.date_naive()).num_days() {
        0 => t.format("%H:%M").to_string(),
        1..=6 => t.format("%a %H:%M").to_string(),
        _ => t.format("%d %b").to_string(),
    }
}

pub fn parse_time(v: &Value) -> Option<DateTime<Local>> {
    DateTime::parse_from_rfc3339(v.as_str()?).ok().map(|d| d.with_timezone(&Local))
}

pub fn sender(v: &Value) -> String {
    let f = &v["from"][0];
    f["name"].as_str().filter(|s| !s.is_empty()).or(f["email"].as_str()).unwrap_or("(unknown)").to_string()
}

fn build_rows(st: &ListState) -> Vec<Mail> {
    let lp = list_prefs();
    let mut last = String::new();
    st.emails.iter().map(|m| {
        let t = parse_time(&m["receivedAt"]);
        let g = t.as_ref().map(group_label).unwrap_or_default();
        let header = if lp.group_by_date && g != last { last = g.clone(); g } else { String::new() };
        let s = sender(m);
        let kw = &m["keywords"];
        let id = m["id"].as_str().unwrap_or("").to_string();
        let tags: Vec<Color> = kw.as_object().map(|o| o.keys().filter_map(|k| k.strip_prefix("$label:")).map(tag_color).collect()).unwrap_or_default();
        let row_tint = if lp.tint_rows { tags.first().map(|c| c.with_alpha(0.12)).unwrap_or(Color::from_argb_u8(0, 0, 0, 0)) } else { Color::from_argb_u8(0, 0, 0, 0) };
        // one-time codes only for fresh mail (Bulwark: received within the last day)
        let fresh = t.as_ref().is_some_and(|t| { let age = Local::now().signed_duration_since(*t); age.num_minutes() >= -5 && age.num_hours() <= 24 });
        let code = if lp.codes && fresh {
            crate::otp::find(m["subject"].as_str().unwrap_or(""), m["preview"].as_str().unwrap_or("")).unwrap_or_default()
        } else { String::new() };
        let files: Vec<slint::SharedString> = if lp.attachment_chips {
            let names: Vec<String> = attachment_names(m, lp.hide_inline_images);
            let mut v: Vec<slint::SharedString> = names.iter().take(2).map(|n| n.as_str().into()).collect();
            if names.len() > 2 { v.push(format!("+{}", names.len() - 2).into()); }
            v
        } else { vec![] };
        Mail {
            initials: initials(&s).into(),
            color: tint(&s),
            sender: s.into(),
            subject: m["subject"].as_str().filter(|s| !s.is_empty()).unwrap_or("(no subject)").into(),
            preview: if lp.show_preview { clean_preview(m["preview"].as_str().unwrap_or("")).into() } else { "".into() },
            time: t.as_ref().map(|t| time_label_with(t, &lp)).unwrap_or_default().into(),
            unread: kw["$seen"].as_bool() != Some(true),
            attach: m["hasAttachment"].as_bool().unwrap_or(false),
            header: header.into(),
            threads: st.threads.get(m["threadId"].as_str().unwrap_or("")).copied().unwrap_or(1) as i32,
            flagged: kw["$flagged"].as_bool() == Some(true),
            answered: kw["$answered"].as_bool() == Some(true),
            draft: kw["$draft"].as_bool() == Some(true),
            checked: st.checked.contains(&id),
            tags: ModelRc::new(VecModel::from(tags)),
            account: m["__account"].as_str().unwrap_or("").into(),
            account_color: m["__account"].as_str().map(|a| rgb(crate::unified::account_color(a))).unwrap_or(rgb(0)),
            id: id.into(),
            code: code.into(),
            row_tint,
            files: ModelRc::new(VecModel::from(files)),
        }
    }).collect()
}

/// An image the body references by cid (not a real attachment).
pub fn is_inline_image(a: &Value) -> bool {
    a["cid"].is_string() && a["disposition"] != "attachment" && a["type"].as_str().is_some_and(|t| t.starts_with("image/"))
}

/// File names of a message's attachments; with `hide_inline` the inline images (cid-referenced
/// pictures in the body) are left out, like Bulwark's hideInlineImageAttachments.
pub fn attachment_names(m: &Value, hide_inline: bool) -> Vec<String> {
    m["attachments"].as_array().map(|a| a.iter().filter(|p| !(hide_inline && is_inline_image(p))).map(|p| p["name"].as_str().filter(|n| !n.is_empty()).unwrap_or("attachment").to_string()).collect()).unwrap_or_default()
}

pub fn rebuild_list(u: &AppWindow) {
    // Bulwark density: extra-compact / compact / regular / comfortable
    let h = match crate::settings::Settings::load().str("density", "regular").as_str() {
        "extra-compact" => 60.0, "compact" => 74.0, "comfortable" => 92.0, _ => 82.0,
    };
    u.set_mail_row_height(h);
    LIST.with(|l| {
        let l = l.borrow();
        u.set_mails(ModelRc::new(VecModel::from(build_rows(&l))));
        u.set_has_more(l.emails.len() < l.total);
        u.set_checked_count(l.checked.len() as i32);
    });
}

type Page = Result<crate::jmap::ListPage, String>;

fn is_virtual(filter: &Value) -> bool { filter["scheduled"] == true || filter["unified"] == true }

/// First `n` messages of a list filter (scheduled and unified are virtual).
fn fetch(c: &Client, filter: Value, n: usize) -> Page {
    let plain = |(emails, total, threads)| crate::jmap::ListPage { emails, total, threads, ..Default::default() };
    if filter["scheduled"] == true {
        c.scheduled().map(|e| { let n = e.len(); plain((e, n, HashMap::new())) })
    } else if filter["unified"] == true {
        crate::unified::fetch(n.min(PAGE.max(page_size()))).map(plain)
    } else { c.list_page(filter, 0, None, n) }
}

/// Load the first page of a list (folder, tag or search filter).
pub fn load_list(w: slint::Weak<AppWindow>, c: Arc<Client>, filter: Value) {
    let gen = LIST.with(|l| {
        let mut l = l.borrow_mut();
        l.generation += 1;
        l.filter = filter.clone();
        l.checked.clear();
        // the rows and states still belong to the previous list: a sync before the first page
        // arrives must not run the new filter against them
        l.sync = None;
        l.loading = true;
        l.generation
    });
    let _ = w.upgrade_in_event_loop(|u| u.set_busy(true));
    let page = page_size();
    std::thread::spawn(move || {
        let res = fetch(&c, filter, page);
        let _ = w.upgrade_in_event_loop(move |u| {
            u.set_busy(false);
            LIST.with(|l| {
                let mut l = l.borrow_mut();
                if l.generation != gen { return; }
                l.loading = false;
                match res {
                    Ok(p) => { l.sync = states_of(&c, &p); l.emails = p.emails; l.total = p.total; l.threads = p.threads; }
                    Err(e) => { l.emails.clear(); l.total = 0; l.sync = None; u.set_status(e.into()); }
                }
            });
            rebuild_list(&u);
        });
    });
}

pub fn load_more(w: slint::Weak<AppWindow>, c: Arc<Client>) {
    let Some((filter, pos, anchor, gen)) = LIST.with(|l| {
        let mut l = l.borrow_mut();
        if l.loading || l.emails.len() >= l.total { return None; }
        l.loading = true;
        let anchor = l.emails.last().and_then(|m| m["id"].as_str()).map(String::from);
        Some((l.filter.clone(), l.emails.len(), anchor, l.generation))
    }) else { return };
    std::thread::spawn(move || {
        // anchored after the last loaded message (RFC 8620 §5.5), so new mail cannot shift the page
        let res = c.list_page(filter, pos, anchor.as_deref(), page_size());
        let _ = w.upgrade_in_event_loop(move |u| {
            LIST.with(|l| {
                let mut l = l.borrow_mut();
                if l.generation != gen { return; }
                l.loading = false;
                if let Ok(p) = res {
                    // a page of another list state: the rows no longer form one snapshot, so the
                    // next sync re-queries in full
                    if l.sync.as_ref().map(|s| Some(&s.query)) != Some(p.query_state.as_ref()) { l.sync = None; }
                    let have: HashSet<String> = l.emails.iter().filter_map(|m| m["id"].as_str().map(String::from)).collect();
                    l.emails.extend(p.emails.into_iter().filter(|m| !have.contains(m["id"].as_str().unwrap_or(""))));
                    l.total = p.total;
                    l.threads.extend(p.threads);
                }
            });
            rebuild_list(&u);
        });
    });
}

/// Bring the loaded list up to date (after push or an action): only what changed
/// (Email/queryChanges, Email/changes, Thread/changes) when the states allow it, else a reload.
/// Must run on the UI thread.
pub fn sync(w: slint::Weak<AppWindow>, c: Arc<Client>) {
    enum Start { Skip, Full, Delta(Value, SyncStates, u64) }
    let start = LIST.with(|l| {
        let mut l = l.borrow_mut();
        if l.filter.is_null() { return Start::Skip; }
        if l.syncing { l.sync_again = true; return Start::Skip; }
        match l.sync.clone() {
            Some(s) if s.account == c.state_key() && !is_virtual(&l.filter) => { l.syncing = true; Start::Delta(l.filter.clone(), s, l.generation) }
            _ => Start::Full,
        }
    });
    let (filter, s, gen) = match start { Start::Skip => return, Start::Full => return reload(w, c), Start::Delta(f, s, g) => (f, s, g) };
    std::thread::spawn(move || {
        let res = c.list_delta(&filter, &s.query, &s.email, &s.thread);
        let w2 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| {
            let (full, again) = LIST.with(|l| {
                let mut l = l.borrow_mut();
                l.syncing = false;
                let again = std::mem::take(&mut l.sync_again);
                if l.generation != gen { return (false, again); }
                // the list was reloaded or paged meanwhile: start over from its new states
                if l.sync.as_ref() != Some(&s) { return (false, true); }
                match res {
                    Ok(Some(d)) => {
                        let st = &mut *l;
                        crate::jmap::apply_list_delta(&mut st.emails, &mut st.total, &mut st.threads, &d);
                        let ids: HashSet<String> = st.emails.iter().filter_map(|m| m["id"].as_str().map(String::from)).collect();
                        st.checked.retain(|i| ids.contains(i));
                        st.sync = Some(SyncStates { account: s.account.clone(), query: d.query_state, email: d.email_state, thread: d.thread_state });
                        (false, again)
                    }
                    _ => (true, false),
                }
            });
            rebuild_list(&u);
            if full { reload(w2, c) } else if again { sync(w2, c) }
        });
    });
}

/// Re-fetch what is loaded, keeping the scroll depth.
pub fn reload(w: slint::Weak<AppWindow>, c: Arc<Client>) {
    let (filter, n, gen) = LIST.with(|l| { let l = l.borrow(); (l.filter.clone(), l.emails.len().max(PAGE), l.generation) });
    if filter.is_null() { return; }
    std::thread::spawn(move || {
        let res = fetch(&c, filter, n);
        let _ = w.upgrade_in_event_loop(move |u| {
            LIST.with(|l| {
                let mut l = l.borrow_mut();
                if l.generation != gen { return; }
                if let Ok(p) = res {
                    let ids: HashSet<String> = p.emails.iter().filter_map(|m| m["id"].as_str().map(String::from)).collect();
                    l.checked.retain(|i| ids.contains(i));
                    l.sync = states_of(&c, &p);
                    l.emails = p.emails; l.total = p.total; l.threads = p.threads;
                }
            });
            rebuild_list(&u);
        });
    });
}

pub fn toggle_check(u: &AppWindow, id: &str) {
    LIST.with(|l| {
        let mut l = l.borrow_mut();
        if !l.checked.remove(id) { l.checked.insert(id.to_string()); }
    });
    rebuild_list(u);
}

pub fn select_all(u: &AppWindow) {
    LIST.with(|l| {
        let mut l = l.borrow_mut();
        if l.checked.is_empty() {
            l.checked = l.emails.iter().filter_map(|m| m["id"].as_str().map(String::from)).collect();
        } else {
            l.checked.clear();
        }
    });
    rebuild_list(u);
}

pub fn checked_ids() -> Vec<String> {
    LIST.with(|l| l.borrow().checked.iter().cloned().collect())
}

/// Remembered account a listed message belongs to (unified list), if any.
pub fn owner(id: &str) -> Option<String> {
    email(id).and_then(|m| m["__account"].as_str().map(String::from)).filter(|a| !a.is_empty())
}

/// The message after `cur` in the list that is not among `ids` (else the one before it).
fn next_after(cur: &str, ids: &[String]) -> Option<String> {
    LIST.with(|l| {
        let l = l.borrow();
        let pos = l.emails.iter().position(|m| m["id"] == cur)?;
        let keep = |m: &&Value| !ids.iter().any(|i| m["id"] == i.as_str());
        l.emails[pos + 1..].iter().find(keep).or_else(|| l.emails[..pos].iter().rev().find(keep))
            .and_then(|m| m["id"].as_str().map(String::from))
    })
}

pub fn email(id: &str) -> Option<Value> {
    LIST.with(|l| l.borrow().emails.iter().find(|m| m["id"] == id).cloned())
}

/// Update a message locally (optimistic UI).
fn patch_local(ids: &[String], f: impl Fn(&mut Value)) {
    LIST.with(|l| {
        for m in l.borrow_mut().emails.iter_mut() {
            if ids.iter().any(|i| m["id"] == i.as_str()) { f(m); }
        }
    });
}

fn remove_local(ids: &[String]) {
    LIST.with(|l| {
        let mut l = l.borrow_mut();
        let before = l.emails.len();
        l.emails.retain(|m| !ids.iter().any(|i| m["id"] == i.as_str()));
        let removed = before - l.emails.len();
        l.total = l.total.saturating_sub(removed);
        for i in ids { l.checked.remove(i); }
    });
}

/// Apply an action to messages: delete, archive, spam, notspam, star, unstar, read, unread,
/// tag:<id>, untag:<id>, move:<mailboxId>.
thread_local! { pub static CONFIRM_BULK: std::cell::Cell<bool> = const { std::cell::Cell::new(false) }; }

/// Put text on the Windows clipboard (clip.exe reads stdin).
pub fn copy_to_clipboard(text: &str) -> bool {
    use std::io::Write;
    let mut cmd = std::process::Command::new("clip.exe");
    #[cfg(windows)]
    { use std::os::windows::process::CommandExt; cmd.creation_flags(0x0800_0000); }
    let Ok(mut child) = cmd.stdin(std::process::Stdio::piped()).spawn() else { return false };
    let ok = child.stdin.take().is_some_and(|mut s| s.write_all(text.as_bytes()).is_ok());
    ok && child.wait().is_ok_and(|s| s.success())
}

pub fn apply(u: &AppWindow, c: Arc<Client>, ids: Vec<String>, action: &str) {
    if ids.is_empty() { return; }
    if let Some(code) = action.strip_prefix("copy-code:") {
        let ok = copy_to_clipboard(code);
        show_toast(u, &if ok { format!("Copied {code}") } else { "Could not copy the code.".to_string() }, false);
        return;
    }
    if action == "delete" && ids.len() > 1 && crate::settings::Settings::load().bool("confirmBulkDelete", true) && !CONFIRM_BULK.with(|x| x.get()) {
        CONFIRM_BULK.with(|x| x.set(true));
        show_toast(u, &format!("Delete {} messages? Press Delete again to confirm.", ids.len()), false);
        let w = u.as_weak();
        slint::Timer::single_shot(std::time::Duration::from_secs(6), move || { CONFIRM_BULK.with(|x| x.set(false)); let _ = w; });
        return;
    }
    CONFIRM_BULK.with(|x| x.set(false));
    let w = u.as_weak();
    let action = action.to_string();
    // in Trash delete is permanent; in Junk only with permanentlyDeleteJunk (Bulwark)
    let current_role = mailbox_role(&u.get_current_folder());
    let junk_permanent = crate::settings::Settings::load().bool("permanentlyDeleteJunk", false);
    let permanent = action == "delete" && (current_role == "trash" || (current_role == "junk" && junk_permanent));
    // resolve toggles from current state
    let first = email(&ids[0]);
    let action = match action.as_str() {
        "star" if first.as_ref().is_some_and(|m| m["keywords"]["$flagged"].as_bool() == Some(true)) => "unstar".to_string(),
        "read" if first.as_ref().is_some_and(|m| m["keywords"]["$seen"].as_bool() == Some(true)) => "unread".to_string(),
        a => a.to_string(),
    };
    let next_snapshot = next_after(u.get_current_mail().as_str(), &ids);
    // optimistic local change
    match action.as_str() {
        "star" => patch_local(&ids, |m| m["keywords"]["$flagged"] = json!(true)),
        "unstar" => patch_local(&ids, |m| { if let Some(o) = m["keywords"].as_object_mut() { o.remove("$flagged"); } }),
        "read" => patch_local(&ids, |m| m["keywords"]["$seen"] = json!(true)),
        "unread" => patch_local(&ids, |m| { if let Some(o) = m["keywords"].as_object_mut() { o.remove("$seen"); } }),
        a if a.starts_with("tag:") => { let k = format!("$label:{}", &a[4..]); patch_local(&ids, |m| m["keywords"][k.as_str()] = json!(true)) }
        a if a.starts_with("untag:") => { let k = format!("$label:{}", &a[6..]); patch_local(&ids, |m| { if let Some(o) = m["keywords"].as_object_mut() { o.remove(&k); } }) }
        "delete" | "archive" | "spam" | "notspam" => remove_local(&ids),
        a if a.starts_with("move:") => remove_local(&ids),
        _ => {}
    }
    let mut open_next: Option<String> = None;
    if ["delete", "archive", "spam", "notspam"].contains(&action.as_str()) || action.starts_with("move:") {
        let cur = u.get_current_mail().to_string();
        if ids.iter().any(|i| *i == cur) {
            // returnToListAfterAction off: open the next message (Bulwark), else go back to the list
            if !crate::settings::Settings::load().bool("returnToListAfterAction", true) {
                open_next = next_snapshot.clone();
            }
            u.set_current_mail("".into());
        }
    }
    rebuild_list(u);
    if let Some(next) = open_next { u.invoke_open_mail(next.into()); }
    std::thread::spawn(move || {
        let res: Result<(), String> = (|| {
            let check = |r: (usize, Vec<String>)| if r.1.is_empty() { Ok(()) } else { Err(r.1.join("; ")) };
            match action.as_str() {
                "delete" if permanent => c.destroy(&ids).map(|_| ()),
                "delete" => {
                    // Bulwark's deleteAction: trash | trash-read | permanent
                    match crate::settings::Settings::load().str("deleteAction", "trash").as_str() {
                        "permanent" => c.destroy(&ids).map(|_| ()),
                        "trash-and-read" => { check(c.set_keyword(&ids, "$seen", true)?)?; check(c.move_to_role(&ids, "trash")?) }
                        _ => check(c.move_to_role(&ids, "trash")?),
                    }
                }
                "archive" => check(c.move_to_role(&ids, "archive")?),
                "spam" => c.mark_spam(&ids, true),
                "notspam" => c.mark_spam(&ids, false),
                "star" => check(c.set_keyword(&ids, "$flagged", true)?),
                "unstar" => check(c.set_keyword(&ids, "$flagged", false)?),
                "read" => check(c.set_keyword(&ids, "$seen", true)?),
                "unread" => check(c.set_keyword(&ids, "$seen", false)?),
                a if a.starts_with("tag:") => check(c.set_keyword(&ids, &format!("$label:{}", &a[4..]), true)?),
                a if a.starts_with("untag:") => check(c.set_keyword(&ids, &format!("$label:{}", &a[6..]), false)?),
                a if a.starts_with("move:") => check(c.move_to(&ids, &a[5..])?),
                a => Err(format!("Unknown action {a}")),
            }
        })();
        let failed = res.is_err();
        if let Err(e) = res {
            let _ = w.upgrade_in_event_loop(move |u| show_toast(&u, &e, false));
        }
        if crate::unified::is_active(&c) { load_mailboxes(w.clone(), c.clone(), false); }
        // LIST lives on the UI thread; after a failure the optimistic change is undone in full
        let w2 = w.clone();
        let _ = w.upgrade_in_event_loop(move |_| if failed { reload(w2, c) } else { sync(w2, c) });
    });
}

pub fn show_toast(u: &AppWindow, text: &str, undo: bool) {
    u.set_toast(text.into());
    u.set_toast_undo(undo);
    let w = u.as_weak();
    let t = text.to_string();
    slint::Timer::single_shot(std::time::Duration::from_secs(if undo { 12 } else { 6 }), move || {
        if let Some(u) = w.upgrade() { if u.get_toast().as_str() == t { u.set_toast("".into()); } }
    });
}

/// Mark the row as read locally (reader opened it).
pub fn mark_read_local(u: &AppWindow, id: &str) {
    patch_local(&[id.to_string()], |m| m["keywords"]["$seen"] = json!(true));
    let model = u.get_mails();
    for i in 0..model.row_count() {
        if let Some(mut m) = model.row_data(i) {
            if m.id == id { if m.unread { m.unread = false; model.set_row_data(i, m); } break; }
        }
    }
}

/// Previews often pad with invisible characters (zero-width, soft hyphen, combining grapheme
/// joiner, Hangul fillers) that browsers draw as nothing; drop them and collapse whitespace.
pub fn clean_preview(p: &str) -> String {
    let invisible = |c: char| matches!(c as u32,
        0x00AD | 0x034F | 0x061C | 0x115F | 0x1160 | 0x17B4 | 0x17B5 | 0x180B..=0x180F | 0x200B..=0x200F |
        0x202A..=0x202E | 0x2060..=0x206F | 0x3164 | 0xFE00..=0xFE0F | 0xFEFF | 0xFFA0 | 0xE0000..=0xE007F);
    let t: String = p.chars().filter(|c| !invisible(*c)).map(|c| if c.is_whitespace() { ' ' } else { c }).collect();
    let t = t.split_whitespace().collect::<Vec<_>>().join(" ");
    if t.is_empty() { "No preview available".into() } else { t }
}
