//! Server notifications and the notification centre (G-045).
//!
//! * ShareNotification (RFC 9670 §3): someone shared a collection with the user, changed their
//!   access or took it away. CalendarEventNotification (draft-ietf-jmap-calendars §7): another
//!   participant invited the user, changed or cancelled an event. Both are pulled at sign-in (the
//!   push stream's open) and on their push, shown as toasts and then destroyed on the server
//!   ("acknowledged"), as Bulwark does (lib/pending-notification-store.ts:44-93,
//!   components/layout/share-notification-toaster.tsx:91-144,
//!   components/layout/calendar-event-notification-toaster.tsx:17-58,
//!   stores/email-store.ts:7566-7579, lib/jmap/client.ts:8085-8133 and 8441-8471).
//! * The centre behind the bell in the rail (components/notifications/notification-center.tsx,
//!   stores/notification-center-store.ts): the last 50 warnings and errors, newest first, kept on
//!   this device until the last account signs out; an entry is unread until "Mark all read",
//!   "Clear" empties the list. Bulwark records error and warning toasts there
//!   (stores/toast-store.ts:57-63): of ours, a revoked share and a cancelled event.
use crate::jmap::Client;
use crate::{AppWindow, NoticeRow, Notices};
use serde_json::{json, Value};
use slint::{ComponentHandle, Model, ModelRc, VecModel};
use std::collections::HashSet;
use std::sync::{Arc, Mutex};

/// Bulwark MAX_ENTRIES (stores/notification-center-store.ts:10).
pub const MAX_ENTRIES: usize = 50;
pub const FILE: &str = "notification-center.json";
const PRINCIPALS: &str = "urn:ietf:params:jmap:principals";

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Level { Info, Warning, Error }

impl Level {
    fn name(self) -> &'static str { match self { Level::Info => "info", Level::Warning => "warning", Level::Error => "error" } }
    fn parse(s: &str) -> Option<Level> { match s { "info" => Some(Level::Info), "warning" => Some(Level::Warning), "error" => Some(Level::Error), _ => None } }
}

#[derive(Clone, Debug, PartialEq)]
pub struct Entry {
    pub id: String,
    /// Unix ms.
    pub time: i64,
    pub level: Level,
    pub title: String,
    pub body: String,
    pub read: bool,
}

/// The list behind the bell.
#[derive(Default, Debug, PartialEq)]
pub struct Center { pub entries: Vec<Entry>, next: u64 }

impl Center {
    /// Newest first, the last [`MAX_ENTRIES`] kept.
    pub fn add(&mut self, level: Level, title: &str, body: &str, time: i64) -> String {
        self.next += 1;
        let id = format!("{time:x}-{}", self.next);
        self.entries.insert(0, Entry { id: id.clone(), time, level, title: title.into(), body: body.into(), read: false });
        self.entries.truncate(MAX_ENTRIES);
        id
    }
    /// Bulwark marks one entry read when a plugin's entry is clicked (notification-center.tsx:54-59);
    /// app entries are not clickable, and plugin notifications are not supported here yet.
    #[allow(dead_code)]
    pub fn mark_read(&mut self, id: &str) { for e in self.entries.iter_mut().filter(|e| e.id == id) { e.read = true; } }
    pub fn mark_all_read(&mut self) { for e in &mut self.entries { e.read = true; } }
    pub fn clear(&mut self) { self.entries.clear(); }
    pub fn unread(&self) -> usize { self.entries.iter().filter(|e| !e.read).count() }

    pub fn to_json(&self) -> Value {
        Value::Array(self.entries.iter().map(|e| json!({"id": e.id, "time": e.time, "level": e.level.name(), "title": e.title, "body": e.body, "read": e.read})).collect())
    }
    /// What was saved; malformed entries are dropped (Bulwark isEntry).
    pub fn from_json(v: &Value) -> Center {
        let entries: Vec<Entry> = v.as_array().map(|a| a.iter().filter_map(|e| Some(Entry {
            id: e["id"].as_str()?.into(), time: e["time"].as_i64()?, level: Level::parse(e["level"].as_str()?)?,
            title: e["title"].as_str()?.into(), body: e["body"].as_str()?.into(), read: e["read"].as_bool()?,
        })).take(MAX_ENTRIES).collect()).unwrap_or_default();
        Center { next: entries.len() as u64, entries }
    }
}

/// "99+" past two digits (Bulwark formatBadgeCount).
pub fn badge(n: usize) -> String { if n > 99 { "99+".into() } else { n.to_string() } }

/// How long ago, short.
pub fn ago(now: i64, t: i64) -> String {
    let s = (now - t).max(0) / 1000;
    match s {
        0..=59 => "now".into(),
        60..=3599 => format!("{} min ago", s / 60),
        3600..=86399 => format!("{} h ago", s / 3600),
        _ => format!("{} d ago", s / 86400),
    }
}

// ---------------- requests ----------------

/// ShareNotification/query + /get of the user's own account, oldest first (lib/jmap/client.ts:8441-8460).
pub fn share_list_calls(account: &str) -> Value {
    json!([
        ["ShareNotification/query", {"accountId": account, "sort": [{"property": "created", "isAscending": true}]}, "0"],
        ["ShareNotification/get", {"accountId": account, "#ids": {"resultOf": "0", "name": "ShareNotification/query", "path": "/ids"}}, "1"],
    ])
}

pub const EVENT_PROPERTIES: [&str; 8] = ["id", "created", "changedBy", "comment", "type", "calendarEventId", "isDraft", "event"];

/// One page of CalendarEventNotification, oldest first (lib/jmap/client.ts:8085-8121).
pub fn event_page_calls(account: &str, position: usize, limit: usize) -> Value {
    json!([
        ["CalendarEventNotification/query", {"accountId": account, "sort": [{"property": "created", "isAscending": true}], "position": position, "limit": limit}, "0"],
        ["CalendarEventNotification/get", {"accountId": account, "#ids": {"resultOf": "0", "name": "CalendarEventNotification/query", "path": "/ids"}, "properties": EVENT_PROPERTIES}, "1"],
    ])
}

/// `type`/set destroy, maxObjectsInSet at a time (client.ts:8124-8133, 8463-8471).
pub fn destroy_calls(ty: &str, account: &str, ids: &[String], max: usize) -> Vec<Value> {
    ids.chunks(max.max(1)).map(|b| json!([[format!("{ty}/set"), {"accountId": account, "destroy": b}, "0"]])).collect()
}

/// The /get list of a list response, or the server's error.
fn listed(r: &[Value], get: &str) -> Result<(Vec<Value>, usize), String> {
    let g = r.iter().find(|x| x[0] == get);
    let q = r.iter().find(|x| x[0].as_str().is_some_and(|m| m.ends_with("/query")));
    match (q, g) {
        (Some(q), Some(g)) => Ok((g[1]["list"].as_array().cloned().unwrap_or_default(), q[1]["ids"].as_array().map(|a| a.len()).unwrap_or(0))),
        _ => Err(r.iter().find(|x| x[0] == "error").map(|e| crate::jmap::method_error(&e[1])).unwrap_or_else(|| "Failed to load notifications".into())),
    }
}

/// Bulwark caps the pages as listPrincipals does, so a server ignoring `position` cannot loop.
const MAX_NOTIFICATIONS: usize = 20000;

pub fn list_shares(c: &Client) -> Result<Vec<Value>, String> {
    Ok(listed(&c.request(share_list_calls(&c.account))?, "ShareNotification/get")?.0)
}

pub fn list_events(c: &Client) -> Result<Vec<Value>, String> {
    let acc = c.account_for(crate::calendar::CAL);
    let limit = c.max_get();
    let mut out = vec![];
    let mut position = 0;
    while position < MAX_NOTIFICATIONS {
        let (list, n) = listed(&c.request(event_page_calls(&acc, position, limit))?, "CalendarEventNotification/get")?;
        out.extend(list);
        if n < limit { break; }
        position += limit;
    }
    Ok(out)
}

/// Acknowledge shown notifications; a failure only means they show again later (Bulwark logs it).
fn acknowledge(c: &Client, ty: &str, account: &str, ids: &[String]) {
    for calls in destroy_calls(ty, account, ids, c.max_set()) { let _ = c.request(calls); }
}

// ---------------- what is shown ----------------

fn who(n: &Value) -> String {
    let b = &n["changedBy"];
    b["name"].as_str().filter(|s| !s.is_empty()).or(b["email"].as_str().filter(|s| !s.is_empty())).unwrap_or("Someone").to_string()
}

/// What happened to the user's rights (share-notification-toaster.tsx:15-19).
pub fn share_kind(n: &Value) -> &'static str {
    let empty = |v: &Value| v.as_object().is_none_or(|o| o.is_empty());
    if empty(&n["oldRights"]) { "shared" } else if empty(&n["newRights"]) { "revoked" } else { "changed" }
}

/// From this many notifications by one person, one toast says how many (SUMMARY_FROM).
pub const SUMMARY_FROM: usize = 3;

/// The toasts of a batch of share notifications: one each, unless one person changed
/// [`SUMMARY_FROM`] or more at once or an object's name is unknown (Stalwart sends an empty name):
/// then one per person with the count (share-notification-toaster.tsx:42-66).
pub fn share_toasts(notes: &[Value], name_of: impl Fn(&Value) -> Option<String>) -> Vec<(Level, String)> {
    let mut people: Vec<(String, Vec<&Value>)> = vec![];
    for n in notes {
        let b = &n["changedBy"];
        let key = [&b["principalId"], &b["email"], &b["name"]].iter().find_map(|v| v.as_str().filter(|s| !s.is_empty())).unwrap_or("").to_string();
        match people.iter_mut().find(|p| p.0 == key) { Some(p) => p.1.push(n), None => people.push((key, vec![n])) }
    }
    let mut out = vec![];
    for (_, group) in people {
        let named: Vec<(&Value, String)> = group.iter().map(|n| (*n, n["name"].as_str().filter(|s| !s.is_empty()).map(String::from).or_else(|| name_of(n)).unwrap_or_default())).collect();
        if group.len() < SUMMARY_FROM && named.iter().all(|(_, o)| !o.is_empty()) {
            for (n, object) in named {
                let kind = share_kind(n);
                let what = match n["objectType"].as_str().unwrap_or("") { "Mailbox" => "folder", "Calendar" => "calendar", "AddressBook" => "address book", "FileNode" => "file folder", t => t };
                let name = who(n);
                let text = match kind {
                    "shared" => format!("{name} shared the {what} \"{object}\" with you"),
                    "revoked" => format!("{name} removed your access to the {what} \"{object}\""),
                    _ => format!("{name} changed your access to the {what} \"{object}\""),
                };
                out.push((if kind == "revoked" { Level::Warning } else { Level::Info }, text));
            }
            continue;
        }
        let count = group.len();
        let items = if count == 1 { "1 shared item".to_string() } else { format!("{count} shared items") };
        let revoked = group.iter().all(|n| share_kind(n) == "revoked");
        out.push((if revoked { Level::Warning } else { Level::Info }, format!("{} changed your access to {items}", who(group[0]))));
    }
    out
}

/// More notifications at once than this come as one summary (MAX_SEPARATE_TOASTS).
pub const MAX_SEPARATE: usize = 3;

/// The toasts (level, text, comment) of event notifications: drafts are the user's own unsent
/// changes and are not announced (calendar-event-notification-toaster.tsx:30-50).
pub fn event_toasts(notes: &[Value]) -> Vec<(Level, String, String)> {
    let announced: Vec<&Value> = notes.iter().filter(|n| n["isDraft"] != true).collect();
    if announced.len() > MAX_SEPARATE {
        return vec![(Level::Info, format!("Changes to events you take part in: {}", announced.len()), String::new())];
    }
    announced.iter().map(|n| {
        let title = n["event"]["title"].as_str().filter(|s| !s.is_empty()).unwrap_or("Untitled event");
        let name = who(n);
        let comment = n["comment"].as_str().unwrap_or("").to_string();
        match n["type"].as_str() {
            Some("created") => (Level::Info, format!("{name} invited you to \"{title}\""), comment),
            Some("destroyed") => (Level::Warning, format!("{name} cancelled \"{title}\""), comment),
            _ => (Level::Info, format!("{name} updated \"{title}\""), comment),
        }
    }).collect()
}

/// Ids already shown for a login: a fetch racing an acknowledge must not show them again
/// (pending-notification-store.ts:47-49).
#[derive(Default, Debug)]
pub struct Seen { owner: String, ids: HashSet<String> }

impl Seen {
    pub fn fresh(&mut self, owner: &str, list: Vec<Value>) -> Vec<Value> {
        if self.owner != owner { self.owner = owner.into(); self.ids.clear(); }
        list.into_iter().filter(|n| n["id"].as_str().is_some_and(|id| self.ids.insert(id.to_string()))).collect()
    }
}

// ---------------- the app ----------------

/// The app lock's hook: while it says locked, the bell shows no count and its panel no entries,
/// and notifications stay on the server (fetched again once [`fetch`] runs after unlocking).
pub fn locked() -> bool { false }

static CENTER: Mutex<Option<Center>> = Mutex::new(None);
/// Shown ids of share and of event notifications.
static SEEN: Mutex<Option<(Seen, Seen)>> = Mutex::new(None);
static BUSY: Mutex<(bool, bool)> = Mutex::new((false, false));

fn path() -> std::path::PathBuf { crate::config_dir().join(FILE) }

fn center<T>(f: impl FnOnce(&mut Center) -> T, save: bool) -> T {
    let mut g = CENTER.lock().unwrap_or_else(|e| e.into_inner());
    let c = g.get_or_insert_with(|| std::fs::read_to_string(path()).ok().and_then(|s| serde_json::from_str(&s).ok()).map(|v| Center::from_json(&v)).unwrap_or_default());
    let out = f(c);
    if save {
        let _ = if c.entries.is_empty() { std::fs::remove_file(path()) } else { std::fs::write(path(), c.to_json().to_string()) };
    }
    out
}

/// Add an entry to the centre (warnings and errors, as Bulwark's toasts do) and redraw the bell.
pub fn record(u: &AppWindow, level: Level, title: &str, body: &str) {
    center(|c| c.add(level, title, body, chrono::Utc::now().timestamp_millis()), true);
    render(u);
}

/// The last account signed out: the list goes (Bulwark onFullSignOut), the file with the wipe.
pub fn forget(u: &AppWindow) {
    *CENTER.lock().unwrap_or_else(|e| e.into_inner()) = Some(Center::default());
    render(u);
}

pub fn render(u: &AppWindow) {
    let g = u.global::<Notices>();
    if locked() {
        g.set_unread(0);
        g.set_badge("".into());
        g.set_rows(ModelRc::new(VecModel::from(Vec::<NoticeRow>::new())));
        return;
    }
    let now = chrono::Utc::now().timestamp_millis();
    let (rows, unread) = center(|c| (c.entries.iter().map(|e| NoticeRow {
        id: e.id.as_str().into(), title: e.title.as_str().into(), body: e.body.as_str().into(), when: ago(now, e.time).into(),
        level: match e.level { Level::Info => 0, Level::Warning => 1, Level::Error => 2 }, unread: !e.read,
    }).collect::<Vec<_>>(), c.unread()), false);
    g.set_unread(unread as i32);
    g.set_badge(badge(unread).into());
    let same = g.get_rows().row_count() == rows.len() && rows.iter().enumerate().all(|(i, r)| g.get_rows().row_data(i).as_ref() == Some(r));
    if !same { g.set_rows(ModelRc::new(VecModel::from(rows))); }
}

/// The bell's buttons.
pub fn wire(ui: &AppWindow) {
    let g = ui.global::<Notices>();
    let w = ui.as_weak();
    g.on_opened(move || if let Some(u) = w.upgrade() { render(&u) });
    let w = ui.as_weak();
    g.on_mark_all_read(move || { center(|c| c.mark_all_read(), true); if let Some(u) = w.upgrade() { render(&u) } });
    let w = ui.as_weak();
    g.on_clear(move || { center(|c| c.clear(), true); if let Some(u) = w.upgrade() { render(&u) } });
    render(ui);
}

/// Toasts one after another in the one toast slot.
fn toast_all(u: &AppWindow, texts: Vec<String>) {
    let secs = crate::a11y::A11y::load().toast_secs.max(3);
    for (i, t) in texts.into_iter().enumerate() {
        if i == 0 { crate::mail::show_toast(u, &t, false); continue; }
        let w = u.as_weak();
        slint::Timer::single_shot(std::time::Duration::from_secs(secs * i as u64), move || if let Some(u) = w.upgrade() { crate::mail::show_toast(&u, &t, false) });
    }
}

/// Pull both notification kinds, show what is new, refresh what it changed, acknowledge it.
/// A push arriving while a pull runs joins it (one more pull after it).
pub fn fetch(w: slint::Weak<AppWindow>, c: Arc<Client>) {
    if locked() { return; }
    let (shares_on, events_on) = (c.has(PRINCIPALS), c.has(crate::calendar::CAL));
    if !shares_on && !events_on { return; }
    {
        let mut b = BUSY.lock().unwrap_or_else(|e| e.into_inner());
        if b.0 { b.1 = true; return; }
        b.0 = true;
    }
    std::thread::spawn(move || {
        let shares = if shares_on { list_shares(&c).unwrap_or_default() } else { vec![] };
        let events = if events_on { list_events(&c).unwrap_or_default() } else { vec![] };
        let owner = c.state_key();
        let (shares, events) = {
            let mut g = SEEN.lock().unwrap_or_else(|e| e.into_inner());
            let s = g.get_or_insert_with(Default::default);
            (s.0.fresh(&owner, shares), s.1.fresh(&owner, events))
        };
        let (w2, c2) = (w.clone(), c.clone());
        let _ = w.upgrade_in_event_loop(move |u| {
            show(&u, &c2, &shares, &events);
            let ids = |l: &[Value]| l.iter().filter_map(|n| n["id"].as_str().map(String::from)).collect::<Vec<_>>();
            let (sid, eid) = (ids(&shares), ids(&events));
            let c3 = c2.clone();
            std::thread::spawn(move || {
                if !sid.is_empty() { acknowledge(&c3, "ShareNotification", &c3.account, &sid); }
                if !eid.is_empty() { acknowledge(&c3, "CalendarEventNotification", &c3.account_for(crate::calendar::CAL), &eid); }
            });
            let again = { let mut b = BUSY.lock().unwrap_or_else(|e| e.into_inner()); b.0 = false; std::mem::take(&mut b.1) };
            if again { fetch(w2, c2); }
        });
    });
}

/// The name of a shared collection as the loaded lists have it (share-notification-toaster.tsx:69-82).
fn stored_name(n: &Value) -> Option<String> {
    let (id, acc) = (n["objectId"].as_str()?, n["objectAccountId"].as_str().unwrap_or(""));
    let same = |x: &Value| x["_rid"].as_str().or(x["id"].as_str()) == Some(id) && (acc.is_empty() || x["_acc"].as_str().is_none_or(|a| a == acc));
    let found = match n["objectType"].as_str()? {
        "Mailbox" => crate::mail::mailboxes().into_iter().find(|m| m["id"] == id),
        "Calendar" => crate::calendar::calendars().into_iter().find(same),
        "AddressBook" => crate::contacts::books().into_iter().find(same),
        _ => None,
    };
    found.and_then(|x| x["name"].as_str().filter(|s| !s.is_empty()).map(String::from))
}

fn show(u: &AppWindow, c: &Arc<Client>, shares: &[Value], events: &[Value]) {
    let mut texts = vec![];
    if !shares.is_empty() {
        // the list the change belongs to, so a new share appears without a reload
        let touched: HashSet<&str> = shares.iter().filter_map(|n| n["objectType"].as_str()).collect();
        let w = u.as_weak();
        let key = c.state_key();
        use crate::livesync::{owner, View};
        if touched.contains("Mailbox") { crate::mail::load_mailboxes(w.clone(), c.clone(), false); }
        if touched.contains("Calendar") && c.has(crate::calendar::CAL) { crate::calendar::load(w.clone(), c.clone()); }
        if touched.contains("AddressBook") && owner(View::Contacts) == key { crate::contacts::load(w.clone(), c.clone()); }
        if touched.contains("FileNode") && owner(View::Files) == key { crate::files::reload(u, c.clone()); }
        for (level, text) in share_toasts(shares, stored_name) {
            if level != Level::Info { record(u, level, &text, ""); }
            texts.push(text);
        }
    }
    if !events.is_empty() {
        for (level, text, comment) in event_toasts(events) {
            if level != Level::Info { record(u, level, &text, &comment); }
            texts.push(if comment.is_empty() { text } else { format!("{text}: {comment}") });
        }
        // the events in view show the change
        crate::livesync::calendar(u.as_weak(), c.clone());
    }
    toast_all(u, texts);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_center_keeps_fifty_newest_first_and_counts_unread() {
        let mut c = Center::default();
        let first = c.add(Level::Warning, "a", "", 1);
        for i in 0..60 { c.add(Level::Error, &format!("e{i}"), "b", 2 + i); }
        assert_eq!(c.entries.len(), MAX_ENTRIES);
        assert_eq!(c.entries[0].title, "e59");
        assert!(c.entries.iter().all(|e| e.id != first), "the oldest went");
        assert_eq!(c.unread(), 50);
        let id = c.entries[3].id.clone();
        c.mark_read(&id);
        assert_eq!(c.unread(), 49);
        c.mark_all_read();
        assert_eq!(c.unread(), 0);
        c.add(Level::Info, "new", "", 99);
        assert_eq!(c.unread(), 1);
        c.clear();
        assert!(c.entries.is_empty());
        assert_eq!(c.unread(), 0);
    }

    #[test]
    fn the_center_round_trips_and_drops_malformed_entries() {
        let mut c = Center::default();
        c.add(Level::Warning, "w", "body", 5);
        c.add(Level::Error, "e", "", 6);
        c.mark_all_read();
        let back = Center::from_json(&c.to_json());
        assert_eq!(back.entries, c.entries);
        let mixed = json!([{"id": "x", "time": 1, "level": "bogus", "title": "t", "body": "", "read": false}, {"id": "y"}, c.to_json()[0].clone()]);
        assert_eq!(Center::from_json(&mixed).entries.len(), 1);
        // an id after a reload does not repeat a kept one
        let mut back = back;
        let id = back.add(Level::Info, "n", "", 6);
        assert!(back.entries.iter().filter(|e| e.id == id).count() == 1);
    }

    #[test]
    fn badge_and_ago() {
        assert_eq!(badge(0), "0");
        assert_eq!(badge(99), "99");
        assert_eq!(badge(100), "99+");
        assert_eq!(ago(10_000, 9_000), "now");
        assert_eq!(ago(10 * 60_000, 0), "10 min ago");
        assert_eq!(ago(3 * 3_600_000, 0), "3 h ago");
        assert_eq!(ago(2 * 86_400_000, 0), "2 d ago");
    }

    #[test]
    fn list_and_destroy_request_shapes() {
        assert_eq!(share_list_calls("A"), json!([
            ["ShareNotification/query", {"accountId": "A", "sort": [{"property": "created", "isAscending": true}]}, "0"],
            ["ShareNotification/get", {"accountId": "A", "#ids": {"resultOf": "0", "name": "ShareNotification/query", "path": "/ids"}}, "1"]]));
        let e = event_page_calls("C", 500, 500);
        assert_eq!(e[0][1], json!({"accountId": "C", "sort": [{"property": "created", "isAscending": true}], "position": 500, "limit": 500}));
        assert_eq!(e[1][1]["#ids"]["name"], "CalendarEventNotification/query");
        assert_eq!(e[1][1]["properties"], json!(EVENT_PROPERTIES));
        let ids: Vec<String> = (0..5).map(|i| format!("n{i}")).collect();
        let d = destroy_calls("ShareNotification", "A", &ids, 2);
        assert_eq!(d.len(), 3);
        assert_eq!(d[0], json!([["ShareNotification/set", {"accountId": "A", "destroy": ["n0", "n1"]}, "0"]]));
        assert_eq!(d[2][0][1]["destroy"], json!(["n4"]));
    }

    #[test]
    fn event_notifications_are_paged_and_a_failed_listing_is_an_error() {
        let calls = Arc::new(Mutex::new(0));
        let n = calls.clone();
        let c = Client::fake("https://mail.example.test", "A", &[], move |req| {
            *n.lock().unwrap() += 1;
            let pos = req[0][1]["position"].as_u64().unwrap() as usize;
            let limit = req[0][1]["limit"].as_u64().unwrap() as usize;
            let count = if pos == 0 { limit } else { 1 };
            let ids: Vec<Value> = (0..count).map(|i| json!(format!("e{}", pos + i))).collect();
            let list: Vec<Value> = ids.iter().map(|i| json!({"id": i, "type": "created"})).collect();
            Ok(vec![json!(["CalendarEventNotification/query", {"ids": ids}, "0"]), json!(["CalendarEventNotification/get", {"list": list}, "1"])])
        });
        let got = list_events(&c).unwrap();
        assert_eq!(got.len(), c.max_get() + 1);
        assert_eq!(*calls.lock().unwrap(), 2);
        let c = Client::fake("https://mail.example.test", "A", &[], |_| Ok(vec![json!(["error", {"type": "unknownMethod"}, "0"])]));
        assert!(list_shares(&c).is_err());
    }

    fn share(by: &str, ty: &str, name: &str, old: Value, new: Value) -> Value {
        json!({"id": format!("{by}{ty}{name}"), "changedBy": {"name": by, "email": null, "principalId": by}, "objectType": ty, "objectAccountId": "B", "objectId": "x", "name": name, "oldRights": old, "newRights": new})
    }

    #[test]
    fn share_kinds_and_texts() {
        let r = json!({"mayRead": true});
        let notes = [share("Bob", "Calendar", "Team", Value::Null, r.clone()), share("Cy", "Mailbox", "Ops", r.clone(), json!({}))];
        assert_eq!(share_kind(&notes[0]), "shared");
        assert_eq!(share_kind(&notes[1]), "revoked");
        assert_eq!(share_kind(&share("D", "FileNode", "f", r.clone(), json!({"mayWrite": true}))), "changed");
        assert_eq!(share_toasts(&notes, |_| None), vec![
            (Level::Info, "Bob shared the calendar \"Team\" with you".to_string()),
            (Level::Warning, "Cy removed your access to the folder \"Ops\"".to_string()),
        ]);
    }

    #[test]
    fn many_by_one_person_or_unknown_names_are_summed() {
        let r = json!({"mayRead": true});
        let many: Vec<Value> = (0..3).map(|i| share("Bob", "AddressBook", &format!("b{i}"), r.clone(), Value::Null)).collect();
        assert_eq!(share_toasts(&many, |_| None), vec![(Level::Warning, "Bob changed your access to 3 shared items".to_string())]);
        // Stalwart's empty name: looked up, else summed
        let unnamed = [share("Ann", "Calendar", "", Value::Null, r.clone())];
        assert_eq!(share_toasts(&unnamed, |_| Some("Work".into()))[0].1, "Ann shared the calendar \"Work\" with you");
        assert_eq!(share_toasts(&unnamed, |_| None), vec![(Level::Info, "Ann changed your access to 1 shared item".to_string())]);
        let mut anon = share("x", "FileNode", "Docs", Value::Null, r);
        anon["changedBy"] = json!({"name": "", "email": "", "principalId": null});
        assert_eq!(share_toasts(&[anon], |_| None)[0].1, "Someone shared the file folder \"Docs\" with you");
    }

    #[test]
    fn event_texts_skip_drafts_and_sum_past_three() {
        let ev = |t: &str, title: &str, draft: bool| json!({"id": t, "type": t, "isDraft": draft, "changedBy": {"name": "Bob", "email": "bob@example.test"}, "comment": if t == "destroyed" { "Sorry" } else { "" }, "event": {"title": title}});
        let got = event_toasts(&[ev("created", "Sync", false), ev("updated", "", false), ev("destroyed", "Lunch", false), ev("created", "Mine", true)]);
        assert_eq!(got, vec![
            (Level::Info, "Bob invited you to \"Sync\"".to_string(), String::new()),
            (Level::Info, "Bob updated \"Untitled event\"".to_string(), String::new()),
            (Level::Warning, "Bob cancelled \"Lunch\"".to_string(), "Sorry".to_string()),
        ]);
        let many: Vec<Value> = (0..4).map(|_| ev("updated", "x", false)).collect();
        assert_eq!(event_toasts(&many), vec![(Level::Info, "Changes to events you take part in: 4".to_string(), String::new())]);
        assert!(event_toasts(&[ev("created", "d", true)]).is_empty());
    }

    #[test]
    fn seen_ids_are_shown_once_per_login() {
        let mut s = Seen::default();
        let l = |ids: &[&str]| ids.iter().map(|i| json!({"id": i})).collect::<Vec<_>>();
        assert_eq!(s.fresh("ann", l(&["1", "2"])).len(), 2);
        assert_eq!(s.fresh("ann", l(&["1", "2", "3"])), l(&["3"]));
        // another login starts over
        assert_eq!(s.fresh("bob", l(&["1"])).len(), 1);
    }
}
