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
/// Users whose push stream is being watched for the unified view (other than the active one),
/// with the token of the watcher thread that owns it (an older thread for the same user stops).
static WATCHING: Mutex<Option<HashMap<String, u64>>> = Mutex::new(None);
static WATCH_TOKEN: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);
/// Logins whose connect failed recently (not retried on every call).
static FAILED: Mutex<Option<HashMap<String, std::time::Instant>>> = Mutex::new(None);
const RETRY_AFTER: std::time::Duration = std::time::Duration::from_secs(60);

/// Logins the server turned down. Unlike a timeout (retried after `RETRY_AFTER`), a rejected
/// sign-in is not tried again until the user signs in to that login again, and the user is told
/// once.
#[derive(Default)]
pub struct Rejections { users: HashMap<String, bool> }

impl Rejections {
    /// Remember that `user` was turned down.
    pub fn reject(&mut self, user: &str) { self.users.entry(user.to_string()).or_insert(false); }
    pub fn is_rejected(&self, user: &str) -> bool { self.users.contains_key(user) }
    /// True the first time it is asked for a rejected login: the caller shows the notice.
    pub fn take_notice(&mut self, user: &str) -> bool {
        match self.users.get_mut(user) { Some(n) if !*n => { *n = true; true } _ => false }
    }
    /// The user signed in again.
    pub fn clear(&mut self, user: &str) { self.users.remove(user); }
}

static REJECTED: Mutex<Option<Rejections>> = Mutex::new(None);

fn with_rejections<T>(f: impl FnOnce(&mut Rejections) -> T) -> T {
    f(REJECTED.lock().unwrap().get_or_insert_with(Rejections::default))
}

// ---------------- row ids ----------------
// Several sources share short JMAP ids ("b", "c", …), so a unified row is keyed by its
// login, the shared JMAP account it was read from (empty for the login's own) and its id.
const ROW_ACCT: char = '\u{1c}';
const ROW_ID: char = '\u{1d}';

/// The list id of a unified row.
pub fn row_id(user: &str, shared: Option<&str>, id: &str) -> String {
    format!("{user}{ROW_ACCT}{}{ROW_ID}{id}", shared.unwrap_or(""))
}

/// (login, shared JMAP account, server id) of a unified row id; None for a plain id.
pub fn parse_row(id: &str) -> Option<(&str, Option<&str>, &str)> {
    let (prefix, real) = id.rsplit_once(ROW_ID)?;
    let (user, acct) = prefix.split_once(ROW_ACCT)?;
    Some((user, Some(acct).filter(|a| !a.is_empty()), real))
}

/// The server id of a list id (unified rows carry their source in front).
pub fn real_id(id: &str) -> &str { parse_row(id).map(|r| r.2).unwrap_or(id) }

/// Another message of the same source as the row `like` (thread members).
pub fn same_source(like: &str, id: &str) -> String {
    match parse_row(like) { Some((u, a, _)) => row_id(u, a, id), None => id.to_string() }
}
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
            user: user.to_string(),
        };
        Client::connect_oauth(server, t)
    } else {
        Client::connect(server, user, v["pass"].as_str().unwrap_or(""), "")
    }
}

/// Register the active session's client under its remembered user.
pub fn set_active(user: &str, c: Arc<Client>) {
    *ACTIVE.lock().unwrap() = user.to_string();
    // the badge's other accounts change with the active one
    OTHER_AT.lock().unwrap().take();
    // a login that is active signed in just now
    with_rejections(|r| r.clear(user));
    POOL.lock().unwrap().get_or_insert_with(HashMap::new).insert(user.to_string(), c);
    // the previous active login now needs its own watcher; the new one has main's
    if let Some(w) = WATCHING.lock().unwrap().as_mut() { w.remove(user); }
}

pub fn active_user() -> String { ACTIVE.lock().unwrap().clone() }

/// The remembered login (what the account switcher lists and a toast carries) of a connected
/// client. It can differ from the session's own `username`: Stalwart answers with the principal
/// name, which need not be the address that was typed to sign in.
pub fn login_of(c: &Client) -> String {
    let found = POOL.lock().unwrap().as_ref().and_then(|p| p.iter().find(|(_, x)| std::ptr::eq(x.as_ref(), c)).map(|(k, _)| k.clone()));
    found.unwrap_or_else(|| c.username.clone())
}

/// How many signed-in sessions other than `c` this run holds (remembered or not).
pub fn sessions_besides(c: &Client) -> usize {
    POOL.lock().unwrap().as_ref().map_or(0, |p| p.values().filter(|x| !std::ptr::eq(x.as_ref(), c)).count())
}

pub fn is_active(c: &Client) -> bool {
    let a = active_user();
    POOL.lock().unwrap().as_ref().and_then(|p| p.get(&a)).is_some_and(|x| std::ptr::eq(x.as_ref(), c))
}

/// Client for a remembered user, connecting it (blocking) if needed.
pub fn client(user: &str) -> Option<Arc<Client>> {
    let pooled = POOL.lock().unwrap().as_ref().and_then(|p| p.get(user).cloned());
    if let Some(c) = pooled {
        // a secondary login the server turned down while it was watched: drop it and mark it, so
        // the user hears about it once (the active login's rejection is shown by the main window)
        if c.is_rejected() && user != active_user() {
            if let Some(p) = POOL.lock().unwrap().as_mut() { p.remove(user); }
            with_rejections(|r| r.reject(user));
            return None;
        }
        return Some(c);
    }
    // a login the server turned down waits for the user to sign in again
    if with_rejections(|r| r.is_rejected(user)) { return None; }
    // a login that just failed is not retried on every refresh (each try waits for the timeout)
    if FAILED.lock().unwrap().as_ref().and_then(|f| f.get(user)).is_some_and(|t| t.elapsed() < RETRY_AFTER) { return None; }
    let v = crate::oauth::accounts().into_iter().find(|a| a["user"].as_str() == Some(user))?;
    let c = match connect(&v) {
        Ok(c) => Arc::new(c),
        Err(e) => {
            if crate::jmap::auth_rejected(&e) {
                with_rejections(|r| r.reject(user));
            } else {
                FAILED.lock().unwrap().get_or_insert_with(HashMap::new).insert(user.to_string(), std::time::Instant::now());
            }
            return None;
        }
    };
    if let Some(f) = FAILED.lock().unwrap().as_mut() { f.remove(user); }
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
            } else if let Some(w) = w {
                if with_rejections(|r| r.take_notice(&user)) {
                    let msg = format!("{user} needs to sign in again.");
                    let _ = w.upgrade_in_event_loop(move |u| crate::mail::show_toast(&u, &msg, false));
                }
            }
        });
    }
}

/// Inbox unread of every other remembered login (own accounts, not shared ones), from the last
/// refresh: what Bulwark's useOtherAccountsUnread adds to the badge with unreadBadgeAllAccounts.
static OTHER_UNREAD: Mutex<Option<HashMap<String, i64>>> = Mutex::new(None);
static OTHER_AT: Mutex<Option<std::time::Instant>> = Mutex::new(None);
const OTHER_EVERY: std::time::Duration = std::time::Duration::from_secs(60);

/// Sum of the other logins' inbox unread counts (`counts` by login), leaving out `active`.
pub fn sum_other_logins(counts: &HashMap<String, i64>, active: &str) -> i64 {
    counts.iter().filter(|(u, _)| u.as_str() != active).map(|(_, n)| (*n).max(0)).sum()
}

/// Unread in the inboxes of the signed-in accounts that are not on screen (last refresh).
pub fn other_logins_unread() -> i64 {
    let active = active_user();
    OTHER_UNREAD.lock().unwrap().as_ref().map(|m| sum_other_logins(m, &active)).unwrap_or(0)
}

/// Re-read the other logins' inbox counts in the background (at most once a minute unless
/// `force`), then redraw the badge. Read-only: one Mailbox/get per login.
pub fn refresh_other_logins(w: slint::Weak<crate::AppWindow>, force: bool) {
    if !several_accounts() || !crate::settings::Settings::load().bool("unreadBadgeAllAccounts", true) { return; }
    {
        let mut at = OTHER_AT.lock().unwrap();
        if !force && at.is_some_and(|t| t.elapsed() < OTHER_EVERY) { return; }
        *at = Some(std::time::Instant::now());
    }
    let gen = WATCH_GEN.load(std::sync::atomic::Ordering::SeqCst);
    std::thread::spawn(move || {
        let active = active_user();
        let users: Vec<String> = crate::oauth::accounts().iter().filter_map(|a| a["user"].as_str().map(String::from)).filter(|u| *u != active).collect();
        let handles: Vec<_> = users.into_iter().map(|user| std::thread::spawn(move || {
            let n = client(&user).and_then(|c| mailboxes_of(&c, &c.account).ok()).and_then(|b| inbox_of(&b).map(|i| i["unreadEmails"].as_i64().unwrap_or(0)));
            (user, n)
        })).collect();
        let mut m = HashMap::new();
        for h in handles {
            if let Ok((u, Some(n))) = h.join() { m.insert(u, n); }
        }
        // signed out meanwhile: the counts belong to nobody
        if WATCH_GEN.load(std::sync::atomic::Ordering::SeqCst) != gen { return; }
        *OTHER_UNREAD.lock().unwrap() = Some(m);
        let _ = w.upgrade_in_event_loop(|u| crate::mail::rebuild_folders(&u));
    });
}

/// Drop every pooled client (sign-out).
pub fn clear() {
    *POOL.lock().unwrap() = None;
    OTHER_UNREAD.lock().unwrap().take();
    OTHER_AT.lock().unwrap().take();
    ACTIVE.lock().unwrap().clear();
    UNREAD.lock().unwrap().take();
    WATCHING.lock().unwrap().take();
    FAILED.lock().unwrap().take();
    REJECTED.lock().unwrap().take();
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
    dedupe(handles.into_iter().flat_map(|h| h.join().unwrap_or_default()).collect())
}

/// Bulwark dedupeUnifiedAccounts, per account: a JMAP account reachable through several
/// logins (a group shared with two of them, or a login's own account shared with another)
/// is read once. Accounts are identified by server and JMAP id (Stalwart reuses short ids
/// on every server); a login's own account always wins, then the first login reaching it.
fn dedupe(srcs: Vec<Source>) -> Vec<Source> {
    let key = |s: &Source| (s.client.base.trim_end_matches('/').to_lowercase(), s.account.clone());
    dedupe_by(srcs, |s| s.shared, key)
}

fn dedupe_by<T, K: std::hash::Hash + Eq>(items: Vec<T>, shared: impl Fn(&T) -> bool, key: impl Fn(&T) -> K) -> Vec<T> {
    let mut seen: HashSet<K> = items.iter().filter(|s| !shared(s)).map(&key).collect();
    items.into_iter().filter(|s| !shared(s) || seen.insert(key(s))).collect()
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
    let shared = src.shared.then_some(src.account.as_str());
    let threads = r[2][1]["list"].as_array().map(|l| l.iter().filter_map(|t| {
        Some((row_id(&src.user, shared, t["id"].as_str()?), t["emailIds"].as_array()?.len()))
    }).collect()).unwrap_or_default();
    let key = src.key();
    for m in emails.iter_mut() { tag_row(m, &src.user, shared, &key); }
    Ok((emails, total, threads, unread))
}

/// Give a fetched message its unified row id (the server id stays in `__id`) and source.
fn tag_row(m: &mut Value, user: &str, shared: Option<&str>, key: &str) {
    let real = m["id"].as_str().unwrap_or("").to_string();
    m["__id"] = json!(real);
    m["id"] = json!(row_id(user, shared, &real));
    if let Some(t) = m["threadId"].as_str().map(|t| row_id(user, shared, t)) { m["__tkey"] = json!(t); }
    m["__account"] = json!(user);
    m["__src"] = json!(key);
    if let Some(a) = shared { m["__jmap"] = json!(a); }
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
    let token = WATCH_TOKEN.fetch_add(1, std::sync::atomic::Ordering::SeqCst) + 1;
    {
        let mut g = WATCHING.lock().unwrap();
        let g = g.get_or_insert_with(HashMap::new);
        if g.contains_key(user) { return; }
        g.insert(user.to_string(), token);
    }
    let gen = WATCH_GEN.load(std::sync::atomic::Ordering::SeqCst);
    let user = user.to_string();
    std::thread::spawn(move || { loop {
        // this thread owns the watch only while its token is the registered one: after sign-out,
        // the login becoming active, or a newer watcher, it stops
        let live = || WATCH_GEN.load(std::sync::atomic::Ordering::SeqCst) == gen
            && WATCHING.lock().unwrap().as_ref().and_then(|s| s.get(&user)) == Some(&token)
            && enabled() && cross_account();
        let Some(c) = cached(&user) else { break };
        if !live() { break; }
        let w2 = w.clone();
        // every event is seen (a ping every 30 s), so a stopped watcher closes its connection
        // within half a minute
        let res = c.watch(&["Email", "Mailbox"], &mut |ev| {
            if !live() { return false; }
            if let crate::jmap::PushEvent::State(_) = ev {
                let w3 = w2.clone();
                std::thread::spawn(move || {
                    refresh_counts();
                    let _ = w3.upgrade_in_event_loop(|u| crate::mail::on_unified_push(&u));
                });
            }
            true
        });
        if !live() { break; }
        // a rejected sign-in is final: stop watching, the login waits for the user
        if res.as_ref().err().is_some_and(|e| crate::jmap::auth_rejected(e)) {
            with_rejections(|r| r.reject(&user));
            break;
        }
        if res.is_err() { std::thread::sleep(std::time::Duration::from_secs(20)); }
    }
        // free the slot (unless a newer watcher holds it) so the login can be watched again
        if let Some(g) = WATCHING.lock().unwrap().as_mut() { if g.get(&user) == Some(&token) { g.remove(&user); } }
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
    fn a_rejected_login_waits_for_a_new_sign_in_and_is_announced_once() {
        let mut r = Rejections::default();
        assert!(!r.is_rejected("a@example.test"));
        assert!(!r.take_notice("a@example.test"));
        r.reject("a@example.test");
        r.reject("a@example.test");
        assert!(r.is_rejected("a@example.test"));
        assert!(!r.is_rejected("b@example.test"));
        assert!(r.take_notice("a@example.test"));
        assert!(!r.take_notice("a@example.test"));
        // still rejected after the notice
        assert!(r.is_rejected("a@example.test"));
        // signing in again clears it, and a later rejection is announced again
        r.clear("a@example.test");
        assert!(!r.is_rejected("a@example.test"));
        r.reject("a@example.test");
        assert!(r.take_notice("a@example.test"));
    }

    #[test]
    fn other_logins_badge_sum() {
        let m: HashMap<String, i64> = [("a@x".to_string(), 3), ("b@y".to_string(), 4), ("c@z".to_string(), -1)].into_iter().collect();
        assert_eq!(sum_other_logins(&m, "a@x"), 4);
        assert_eq!(sum_other_logins(&m, "nobody"), 7);
        assert_eq!(sum_other_logins(&HashMap::new(), "a@x"), 0);
    }

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
    fn row_ids_keep_sources_apart() {
        let own = row_id("me@x", None, "b");
        let grp = row_id("me@x", Some("g1"), "b");
        assert_ne!(own, grp);
        assert_eq!(parse_row(&own), Some(("me@x", None, "b")));
        assert_eq!(parse_row(&grp), Some(("me@x", Some("g1"), "b")));
        assert_eq!(real_id(&grp), "b");
        assert_eq!(real_id("plain"), "plain");
        assert_eq!(parse_row("plain"), None);
        assert_eq!(same_source(&grp, "c"), row_id("me@x", Some("g1"), "c"));
        assert_eq!(same_source("plain", "c"), "c");
        // the same id from two sources stays two rows
        let mut a = json!({"id": "b", "threadId": "t"});
        let mut g = json!({"id": "b", "threadId": "t"});
        tag_row(&mut a, "me@x", None, "me@x");
        tag_row(&mut g, "me@x", Some("g1"), "me@x#g1");
        assert_ne!(a["id"], g["id"]);
        assert_ne!(a["__tkey"], g["__tkey"]);
        assert_eq!(g["__id"], "b");
        assert_eq!(g["__jmap"], "g1");
    }

    #[test]
    fn dedupe_reads_each_account_once() {
        // (login, server, account, shared)
        let v = vec![
            ("b", "s1", "G", true),   // group via login b
            ("a", "s1", "A", false),  // a's own
            ("b", "s1", "B", false),  // b's own
            ("b", "s1", "A", true),   // a's account shared with b: a's own wins
            ("a", "s1", "G", true),   // the same group via login a: dropped
            ("c", "s2", "G", true),   // same id on another server: kept
        ];
        let out = dedupe_by(v, |x| x.3, |x| (x.1, x.2));
        let got: Vec<(&str, &str)> = out.iter().map(|x| (x.0, x.2)).collect();
        assert_eq!(got, vec![("b", "G"), ("a", "A"), ("b", "B"), ("c", "G")]);
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
