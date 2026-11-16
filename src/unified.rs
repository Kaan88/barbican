//! Bulwark's unified mailbox ("All inboxes"): a pool of connected clients for every
//! remembered account, and a merged Inbox query across them.
//! Clients connect lazily on background threads; the active session's client is reused.
//!
//! Scope follows Bulwark's settings: `unifiedCrossAccount` (default off) spans every
//! remembered login, otherwise only the active one; `includeGroupInUnified` (default on)
//! adds the shared/group mail accounts each login can reach. Paging is per source: the
//! next page asks each source for the messages after the ones of it already loaded
//! (Bulwark's positionsByAccount), and the merged list is ordered by parsed timestamps.
use crate::jmap::Client;
use serde_json::{json, Value};
use std::collections::{HashMap, HashSet};
use std::sync::{Arc, Mutex};

pub const FOLDER_ID: &str = "__unified";

/// Connected clients keyed by remembered account user (the `user` of oauth::accounts()).
static POOL: Mutex<Option<HashMap<String, Arc<Client>>>> = Mutex::new(None);
/// Remembered user of the active session.
static ACTIVE: Mutex<String> = Mutex::new(String::new());
/// Inbox unread count per source key, from the last fetch or count refresh.
static UNREAD: Mutex<Option<HashMap<String, i64>>> = Mutex::new(None);
/// Users whose push stream is being watched for the unified view (other than the active one).
static WATCHING: Mutex<Option<HashSet<String>>> = Mutex::new(None);
/// Bumped on sign-out so old watch threads stop.
static WATCH_GEN: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);

/// Connect a client from a remembered account Value (server/user/kind/pass or refresh/endpoint/client).
pub fn connect(v: &Value) -> Result<Client, String> {
    let server = v["server"].as_str().unwrap_or("");
    let user = v["user"].as_str().unwrap_or("");
    if v["kind"] == "oauth" {
        let t = crate::oauth::Tokens {
            access: String::new(),
            refresh: v["refresh"].as_str().map(String::from),
            expires: None,
            token_endpoint: v["endpoint"].as_str().unwrap_or("").into(),
            client_id: v["client"].as_str().unwrap_or("").into(),
        };
        Client::connect_oauth(server, t)
    } else {
        Client::connect(server, user, v["pass"].as_str().unwrap_or(""), "")
    }
}

/// Register the active session's client under its remembered user.
pub fn set_active(user: &str, c: Arc<Client>) {
    *ACTIVE.lock().unwrap() = user.to_string();
    POOL.lock().unwrap().get_or_insert_with(HashMap::new).insert(user.to_string(), c);
    // the previous active login now needs its own watcher; the new one has main's
    if let Some(w) = WATCHING.lock().unwrap().as_mut() { w.remove(user); }
}

pub fn active_user() -> String { ACTIVE.lock().unwrap().clone() }

pub fn is_active(c: &Client) -> bool {
    let a = active_user();
    POOL.lock().unwrap().as_ref().and_then(|p| p.get(&a)).is_some_and(|x| std::ptr::eq(x.as_ref(), c))
}

/// Client for a remembered user, connecting it (blocking) if needed.
pub fn client(user: &str) -> Option<Arc<Client>> {
    if let Some(c) = POOL.lock().unwrap().as_ref().and_then(|p| p.get(user).cloned()) { return Some(c); }
    let v = crate::oauth::accounts().into_iter().find(|a| a["user"].as_str() == Some(user))?;
    let c = Arc::new(connect(&v).ok()?);
    POOL.lock().unwrap().get_or_insert_with(HashMap::new).entry(user.to_string()).or_insert(c.clone());
    Some(c)
}

/// Client for a remembered user only if already connected (never blocks).
pub fn cached(user: &str) -> Option<Arc<Client>> {
    POOL.lock().unwrap().as_ref().and_then(|p| p.get(user).cloned())
}

/// Connect every remembered account in background threads; with a window, also watch
/// each one's push stream so the unified list and its count follow every account.
pub fn warm_up(w: Option<slint::Weak<crate::AppWindow>>) {
    for v in crate::oauth::accounts() {
        let Some(user) = v["user"].as_str().map(String::from) else { continue };
        let w = w.clone();
        std::thread::spawn(move || {
            if client(&user).is_some() {
                if let Some(w) = w { watch(&user, w); }
            }
        });
    }
}

/// Drop every pooled client (sign-out).
pub fn clear() {
    *POOL.lock().unwrap() = None;
    ACTIVE.lock().unwrap().clear();
    UNREAD.lock().unwrap().take();
    WATCHING.lock().unwrap().take();
    WATCH_GEN.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
}

/// Whether the "All inboxes" folder is shown (Settings > Appearance > Unified mailbox).
pub fn enabled() -> bool { crate::settings::Settings::load().bool("enableUnifiedMailbox", false) }

/// Bulwark unifiedCrossAccount (default off): merge every login, not only the active one.
pub fn cross_account() -> bool { crate::settings::Settings::load().bool("unifiedCrossAccount", false) }

/// Bulwark includeGroupInUnified (default on): add shared/group mail accounts.
pub fn include_group() -> bool { crate::settings::Settings::load().bool("includeGroupInUnified", true) }

/// Stable colour for an account (Bulwark's account dot): by sorted position, so it does
/// not change when the most recently used account moves to the front.
pub fn account_color(user: &str) -> u32 {
    const C: [u32; 8] = [0x3b82f6, 0x22c55e, 0xf97316, 0xa855f7, 0xec4899, 0x14b8a6, 0xeab308, 0xef4444];
    let mut users: Vec<String> = crate::oauth::accounts().iter().filter_map(|a| a["user"].as_str().map(String::from)).collect();
    users.sort();
    let i = users.iter().position(|a| a == user).unwrap_or_else(|| user.bytes().map(|b| b as usize).sum());
    C[i % C.len()]
}

/// Bulwark shows the account dot only once two or more accounts are signed in.
pub fn several_accounts() -> bool { crate::oauth::accounts().len() > 1 }

/// One mailbox owner the unified view reads: a login's own account or a shared one it reaches.
#[derive(Clone)]
pub struct Source {
    pub user: String,
    pub client: Arc<Client>,
    /// JMAP account id read (the login's primary one, or the shared owner's).
    pub account: String,
    pub shared: bool,
}

impl Source {
    pub fn key(&self) -> String { source_key(&self.user, if self.shared { Some(&self.account) } else { None }) }
}

/// Key used for paging and counts: the user, plus the JMAP id for a shared account.
pub fn source_key(user: &str, shared_account: Option<&str>) -> String {
    match shared_account { Some(a) => format!("{user}#{a}"), None => user.to_string() }
}

/// Shared/group mail accounts a login can reach (session accounts other than its own).
pub fn shared_accounts(session: &Value, own: &str) -> Vec<String> {
    let mut out: Vec<String> = session["accounts"].as_object().map(|o| o.iter().filter(|(id, a)| {
        id.as_str() != own && a["isPersonal"].as_bool() != Some(true)
            && a["accountCapabilities"].get("urn:ietf:params:jmap:mail").is_some()
    }).map(|(id, _)| id.clone()).collect()).unwrap_or_default();
    out.sort();
    out
}

/// Users the unified view spans (Bulwark: every connected login with unifiedCrossAccount,
/// else only the active one).
fn users() -> Vec<String> {
    let active = active_user();
    if !cross_account() { return if active.is_empty() { vec![] } else { vec![active] }; }
    let mut all: Vec<String> = crate::oauth::accounts().iter().filter_map(|a| a["user"].as_str().map(String::from)).collect();
    if !active.is_empty() && !all.contains(&active) { all.insert(0, active); }
    all
}

/// Every source of the unified view (connects logins that are not yet pooled).
pub fn sources() -> Vec<Source> {
    let group = include_group();
    let handles: Vec<_> = users().into_iter().map(|user| std::thread::spawn(move || {
        let Some(c) = client(&user) else { return vec![] };
        let mut v = vec![Source { user: user.clone(), client: c.clone(), account: c.account.clone(), shared: false }];
        if group {
            for a in shared_accounts(&c.session, &c.account) {
                v.push(Source { user: user.clone(), client: c.clone(), account: a, shared: true });
            }
        }
        v
    })).collect();
    handles.into_iter().flat_map(|h| h.join().unwrap_or_default()).collect()
}

const MAILBOX_PROPS: [&str; 7] = ["id", "name", "role", "parentId", "unreadEmails", "totalEmails", "myRights"];

/// Mailboxes of a JMAP account through a login's client.
pub fn mailboxes_of(c: &Client, account: &str) -> Result<Vec<Value>, String> {
    if account == c.account { return c.mailboxes(); }
    let r = c.call(json!([["Mailbox/get", {"accountId": account, "ids": null, "properties": MAILBOX_PROPS}, "0"]]))?;
    Ok(r[0][1]["list"].as_array().cloned().unwrap_or_default())
}

/// A source's inbox (by role, else a folder named Inbox).
fn inbox_of(list: &[Value]) -> Option<&Value> {
    list.iter().find(|m| m["role"] == "inbox")
        .or_else(|| list.iter().find(|m| m["name"].as_str().is_some_and(|n| n.eq_ignore_ascii_case("inbox"))))
}

type SourcePage = (Vec<Value>, usize, HashMap<String, usize>, i64);

/// One page of a source's inbox (optionally narrowed by a text search).
fn source_page(src: &Source, text: Option<&str>, position: usize, limit: usize) -> Result<SourcePage, String> {
    let boxes = mailboxes_of(&src.client, &src.account)?;
    let inbox = inbox_of(&boxes).ok_or_else(|| format!("{}: no inbox", src.user))?;
    let unread = inbox["unreadEmails"].as_i64().unwrap_or(0);
    let inbox_id = inbox["id"].as_str().unwrap_or("").to_string();
    let filter = match text {
        Some(t) => json!({"operator": "AND", "conditions": [{"inMailbox": inbox_id}, {"text": t}]}),
        None => json!({"inMailbox": inbox_id}),
    };
    let r = src.client.call(json!([
        ["Email/query", {"accountId": src.account, "filter": filter, "sort": [{"property": "receivedAt", "isAscending": false}],
            "position": position, "limit": limit, "calculateTotal": true}, "0"],
        ["Email/get", {"accountId": src.account, "#ids": {"resultOf": "0", "name": "Email/query", "path": "/ids"},
            "properties": crate::jmap::EMAIL_LIST_PROPERTIES}, "1"],
        ["Thread/get", {"accountId": src.account, "#ids": {"resultOf": "1", "name": "Email/get", "path": "/list/*/threadId"}}, "2"]
    ]))?;
    let total = r[0][1]["total"].as_u64().unwrap_or(0) as usize;
    let mut emails = r[1][1]["list"].as_array().cloned().unwrap_or_default();
    let threads = r[2][1]["list"].as_array().map(|l| l.iter().filter_map(|t| {
        Some((t["id"].as_str()?.to_string(), t["emailIds"].as_array()?.len()))
    }).collect()).unwrap_or_default();
    let key = src.key();
    for m in emails.iter_mut() {
        m["__account"] = json!(src.user);
        m["__src"] = json!(key);
        if src.shared { m["__jmap"] = json!(src.account); }
    }
    Ok((emails, total, threads, unread))
}

fn ts(m: &Value) -> i64 {
    m["receivedAt"].as_str().and_then(|s| chrono::DateTime::parse_from_rfc3339(s).ok()).map(|d| d.timestamp_millis()).unwrap_or(i64::MIN)
}

/// Order a merged list newest first by parsed receivedAt (offsets differ between servers,
/// so the strings alone do not sort), dropping repeats of the same source+id.
pub fn merge_sorted(list: &mut Vec<Value>) {
    let mut seen = HashSet::new();
    list.retain(|m| seen.insert((m["__src"].as_str().unwrap_or("").to_string(), m["id"].as_str().unwrap_or("").to_string())));
    list.sort_by_key(|m| std::cmp::Reverse(ts(m)));
}

/// Per-source counts of loaded rows: the position of each source's next page.
pub fn positions(emails: &[Value]) -> HashMap<String, usize> {
    let mut p = HashMap::new();
    for m in emails {
        if let Some(k) = m["__src"].as_str() { *p.entry(k.to_string()).or_insert(0) += 1; }
    }
    p
}

pub type Page = (Vec<Value>, usize, HashMap<String, usize>);

/// One merged page: each source from `pos[key]` (0 when absent), `limit[key]` messages
/// (`default_limit` when absent). Returns (emails, summed total, thread sizes).
pub fn fetch(text: Option<String>, pos: &HashMap<String, usize>, limit: &HashMap<String, usize>, default_limit: usize) -> Result<Page, String> {
    let srcs = sources();
    if srcs.is_empty() { return Err("No account is connected.".into()); }
    let handles: Vec<_> = srcs.into_iter().map(|s| {
        let p = pos.get(&s.key()).copied().unwrap_or(0);
        let l = limit.get(&s.key()).copied().unwrap_or(default_limit).max(1);
        let text = text.clone();
        std::thread::spawn(move || (s.key(), source_page(&s, text.as_deref(), p, l)))
    }).collect();
    let (mut all, mut total, mut threads, mut errs) = (vec![], 0, HashMap::new(), vec![]);
    let mut counts = HashMap::new();
    for h in handles {
        let Ok((key, res)) = h.join() else { errs.push("thread panicked".to_string()); continue };
        match res {
            Ok((e, t, th, unread)) => { all.extend(e); total += t; threads.extend(th); counts.insert(key, unread); }
            Err(e) => errs.push(e),
        }
    }
    if all.is_empty() && !errs.is_empty() { return Err(errs.join("; ")); }
    UNREAD.lock().unwrap().get_or_insert_with(HashMap::new).extend(counts);
    merge_sorted(&mut all);
    Ok((all, total, threads))
}

/// Refresh the inbox unread count of every source (for the folder badge).
pub fn refresh_counts() {
    let handles: Vec<_> = sources().into_iter().map(|s| std::thread::spawn(move || {
        let n = mailboxes_of(&s.client, &s.account).ok().and_then(|b| inbox_of(&b).map(|i| i["unreadEmails"].as_i64().unwrap_or(0)));
        (s.key(), n)
    })).collect();
    let mut m = HashMap::new();
    for h in handles {
        if let Ok((k, Some(n))) = h.join() { m.insert(k, n); }
    }
    *UNREAD.lock().unwrap() = Some(m);
}

/// Unread across the unified inboxes. The active login's own inbox count is passed in
/// (it is fresher, from the sidebar's mailbox list); other sources come from the last refresh.
pub fn unread_total(active_inbox_unread: i64) -> i64 {
    let active = active_user();
    let cross = cross_account();
    let group = include_group();
    let others: i64 = UNREAD.lock().unwrap().as_ref().map(|m| m.iter().filter(|(k, _)| {
        let (user, shared) = match k.split_once('#') { Some((u, _)) => (u, true), None => (k.as_str(), false) };
        (cross || user == active) && (group || !shared) && !(user == active && !shared)
    }).map(|(_, n)| *n).sum()).unwrap_or(0);
    active_inbox_unread + others
}

/// Watch a pooled login's push stream (not the active one: main's start_push has it) and
/// refresh the unified list and its count on changes.
pub fn watch(user: &str, w: slint::Weak<crate::AppWindow>) {
    if user == active_user() { return; }
    {
        let mut g = WATCHING.lock().unwrap();
        if !g.get_or_insert_with(HashSet::new).insert(user.to_string()) { return; }
    }
    let gen = WATCH_GEN.load(std::sync::atomic::Ordering::SeqCst);
    let user = user.to_string();
    std::thread::spawn(move || loop {
        let live = || WATCH_GEN.load(std::sync::atomic::Ordering::SeqCst) == gen
            && WATCHING.lock().unwrap().as_ref().is_some_and(|s| s.contains(&user));
        let Some(c) = cached(&user) else { break };
        if !live() { break; }
        let w2 = w.clone();
        let res = c.watch(&|_| {
            if !live() || !enabled() || !cross_account() { return; }
            let w3 = w2.clone();
            std::thread::spawn(move || {
                refresh_counts();
                let _ = w3.upgrade_in_event_loop(|u| crate::mail::on_unified_push(&u));
            });
        });
        if !live() { break; }
        if res.is_err() { std::thread::sleep(std::time::Duration::from_secs(20)); }
    });
}

/// Resolve a destination in another account by the folder the user picked in the active
/// one: the same role, else the same name (case-insensitive).
pub fn match_mailbox(list: &[Value], role: &str, name: &str) -> Option<String> {
    let by_role = if role.is_empty() { None } else { list.iter().find(|m| m["role"] == role) };
    by_role.or_else(|| list.iter().find(|m| m["name"].as_str().is_some_and(|n| !name.is_empty() && n.eq_ignore_ascii_case(name))))
        .and_then(|m| m["id"].as_str().map(String::from))
}

/// Encode a "move to the matching folder" action (role, name) for mail::apply.
pub fn moveto_action(role: &str, name: &str) -> String { format!("moveto:{role}\u{1f}{name}") }

/// The destination of a `moveto:` action in the account `c` reaches as `account`.
pub fn resolve_moveto(c: &Client, account: &str, spec: &str) -> Result<String, String> {
    let (role, name) = spec.split_once('\u{1f}').unwrap_or(("", spec));
    match_mailbox(&mailboxes_of(c, account)?, role, name)
        .ok_or_else(|| format!("No folder \"{name}\" in that account."))
}

/// Mail actions on rows of a shared account (the login's client methods address its own
/// account, so these go out with the shared account id).
pub fn shared_apply(c: &Client, account: &str, ids: &[String], action: &str) -> Result<(), String> {
    let set = |patch: Value| -> Result<(), String> {
        let update: serde_json::Map<String, Value> = ids.iter().map(|i| (i.clone(), patch.clone())).collect();
        let r = c.call(json!([["Email/set", {"accountId": account, "update": update}, "s"]]))?;
        match r[0][1]["notUpdated"].as_object() {
            Some(o) if !o.is_empty() => Err(o.values().map(crate::jmap::set_error).collect::<Vec<_>>().join("; ")),
            _ => Ok(()),
        }
    };
    let role_target = |role: &str| -> Result<String, String> {
        match_mailbox(&mailboxes_of(c, account)?, role, "").ok_or_else(|| format!("The shared account has no {role} folder."))
    };
    match action {
        "read" => set(json!({"keywords/$seen": true})),
        "unread" => set(json!({"keywords/$seen": null})),
        "star" => set(json!({"keywords/$flagged": true})),
        "unstar" => set(json!({"keywords/$flagged": null})),
        a if a.starts_with("tag:") => set(json!({format!("keywords/$label:{}", &a[4..]): true})),
        a if a.starts_with("untag:") => set(json!({format!("keywords/$label:{}", &a[6..]): null})),
        "delete" | "archive" | "spam" | "notspam" => {
            let role = match action { "delete" => "trash", "archive" => "archive", "spam" => "junk", _ => "inbox" };
            let t = role_target(role)?;
            set(json!({"mailboxIds": {t: true}}))
        }
        a if a.starts_with("move:") => set(json!({"mailboxIds": {&a[5..]: true}})),
        a if a.starts_with("moveto:") => { let t = resolve_moveto(c, account, &a[7..])?; set(json!({"mailboxIds": {t: true}})) }
        a => Err(format!("Unknown action {a}")),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn merge_orders_by_parsed_time_and_dedupes() {
        let mut v = vec![
            json!({"id": "a", "__src": "x", "receivedAt": "2024-05-01T10:00:00Z"}),
            // 11:30+02:00 is 09:30Z: older than "a" though its string sorts higher
            json!({"id": "b", "__src": "y", "receivedAt": "2024-05-01T11:30:00+02:00"}),
            json!({"id": "c", "__src": "y", "receivedAt": "2024-05-02T00:00:00Z"}),
            json!({"id": "a", "__src": "x", "receivedAt": "2024-05-01T10:00:00Z"}),
            json!({"id": "a", "__src": "y", "receivedAt": "2023-01-01T00:00:00Z"}),
        ];
        merge_sorted(&mut v);
        let ids: Vec<String> = v.iter().map(|m| format!("{}{}", m["__src"].as_str().unwrap(), m["id"].as_str().unwrap())).collect();
        assert_eq!(ids, ["yc", "xa", "yb", "ya"]);
    }

    #[test]
    fn positions_count_per_source() {
        let v = vec![json!({"__src": "a"}), json!({"__src": "b"}), json!({"__src": "a"}), json!({})];
        let p = positions(&v);
        assert_eq!(p.get("a"), Some(&2));
        assert_eq!(p.get("b"), Some(&1));
        assert_eq!(p.len(), 2);
    }

    #[test]
    fn shared_accounts_from_session() {
        let s = json!({"accounts": {
            "own": {"isPersonal": true, "accountCapabilities": {"urn:ietf:params:jmap:mail": {}}},
            "grp": {"isPersonal": false, "accountCapabilities": {"urn:ietf:params:jmap:mail": {}}},
            "cal": {"isPersonal": false, "accountCapabilities": {"urn:ietf:params:jmap:calendars": {}}},
            "other": {"isPersonal": true, "accountCapabilities": {"urn:ietf:params:jmap:mail": {}}}
        }});
        assert_eq!(shared_accounts(&s, "own"), vec!["grp".to_string()]);
        assert_eq!(source_key("u", Some("grp")), "u#grp");
        assert_eq!(source_key("u", None), "u");
    }

    #[test]
    fn match_by_role_then_name() {
        let l = vec![json!({"id": "1", "role": "inbox", "name": "Inbox"}), json!({"id": "2", "name": "Receipts"}), json!({"id": "3", "role": "archive", "name": "Old"})];
        assert_eq!(match_mailbox(&l, "archive", "Archive").as_deref(), Some("3"));
        assert_eq!(match_mailbox(&l, "", "receipts").as_deref(), Some("2"));
        assert_eq!(match_mailbox(&l, "junk", "Spam"), None);
        let a = moveto_action("", "Receipts");
        assert_eq!(a.strip_prefix("moveto:").unwrap().split_once('\u{1f}'), Some(("", "Receipts")));
    }
}
