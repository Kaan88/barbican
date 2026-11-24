//! Trusted senders kept in the server address book "Trusted Senders", shared with Bulwark
//! (parity G-053). The pure parts (names, card shape, parsing, combining, plans, request shapes)
//! are functions with tests; the blocking JMAP parts run on worker threads and never on the UI
//! thread, and the reader only reads a cache.
//!
//! Bulwark references (1.21.2):
//! * `stores/contact-store.ts:399` book name `Trusted Senders`; `:1732-1793` `loadTrustedSendersBook`
//!   (books whose name is exactly that, lowest id wins, created when missing, cards read with
//!   `inAddressBook`, the device's list filed into it); `:477-517` `mergeLocalTrustedSenders`;
//!   `:1795-1826` `addToTrustedSendersBook` (card = `name.full` only for `Name <a@b>`, `emails.email.address`,
//!   address lower-cased; falls back to the device's list when the book is unavailable);
//!   `:1828-1858` `removeFromTrustedSendersBook`; `:1860` `isTrustedAddressBookSender`.
//! * `stores/settings-store.ts:795,1160` setting `trustedSendersAddressBook`, default true;
//!   `:2645` v14 migration (an older false the user never chose turns on).
//! * `components/email/email-viewer.tsx:3136` a sender is trusted when the device's list holds it,
//!   or the setting is on and the book holds it; `:6375` "Trust sender" adds to the book when the
//!   setting is on, else to the device's list. `components/trusted-senders-modal.tsx:63` the list
//!   shown is the book and the device's list together while the book is in use.
//!   `components/settings/privacy-settings.tsx:83-100` the switch: on files the device's list,
//!   off keeps the book's senders trusted on the device.
//!
//! Several accounts: the preferences (and so the device's list) belong to the computer, the book to
//! one account. The device's list is filed into a book only when the one account remembered here is
//! the one being read and no other session is signed in (`sole_remembered`). When the setting is
//! turned off, each address copied from a book is tagged with a hash of its account in
//! `trustedSendersFrom` (the tag travels in settings exports, so it holds no server or user name), and a tagged address is only ever filed
//! into that account's book; an address with no tag follows the one-account rule.
//! Two Barbican processes (or Barbican and Bulwark) that load at the same moment can each create
//! the book; the next load reads both and uses the lowest id, and nothing is merged or deleted.
//!
//! Where Barbican is more careful than Bulwark (it writes to the user's server data):
//! * the book is created only when there is something to put in it, not on every load;
//! * duplicate "Trusted Senders" books and duplicate cards are never merged or deleted: every
//!   book of that name is read, new cards go to the lowest id;
//! * untrusting deletes a card only when it is filed in no other book, holds no other address
//!   and has nothing but the fields this module writes; otherwise the card is unfiled from the
//!   book or loses just that address.
use crate::jmap::Client;
use crate::trust::{is_address, sender_key, trusted_list};
use serde_json::{json, Map, Value};
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Mutex};

pub const SETTING_KEY: &str = "trustedSendersAddressBook";
pub const BOOK_NAME: &str = "Trusted Senders";
/// Settings page: the combined list as text (edited, applied with the Save button), and the button.
pub const TEXT_KEY: &str = "trusted.bookText";
pub const SAVE_KEY: &str = "trusted.save";
/// Prefs: address -> the account (`Client::state_key`) whose book it was copied from.
pub const TAGS_KEY: &str = "trustedSendersFrom";
const CONTACTS: &str = "urn:ietf:params:jmap:contacts";

// ---------------------------------------------------------------- pure parts

/// Bulwark's default is on (settings-store.ts:1160). A value that is not a boolean is on too.
pub fn setting_on(prefs: &Map<String, Value>) -> bool {
    prefs.get(SETTING_KEY).and_then(|v| v.as_bool()).unwrap_or(true)
}

/// The display name of `Name <a@b>` (Bulwark's `^(.+?)\s*<([^>]+)>$`); None for a bare address.
pub fn display_name(entry: &str) -> Option<String> {
    let t = entry.trim();
    if !t.ends_with('>') { return None; }
    let i = t.rfind('<')?;
    let inner = &t[i + 1..t.len() - 1];
    let name = t[..i].trim();
    (!name.is_empty() && !inner.is_empty()).then(|| name.to_string())
}

/// The card written for a trusted sender: the Bulwark shape (`name.full` only when the entry had
/// a name, `emails.email.address` lower-cased, a `urn:uuid:` uid, the book), plus the `@type`
/// and `version` every JSContact card carries.
pub fn card_for(book_id: &str, entry: &str, uid: &str) -> Value {
    let mut card = json!({
        "@type": "Card", "version": "1.0", "uid": uid,
        "addressBookIds": { book_id: true },
        "emails": { "email": { "address": sender_key(entry) } },
    });
    if let Some(n) = display_name(entry) { card["name"] = json!({ "full": n }); }
    card
}

/// Every address on the cards, lower-cased, trimmed, without duplicates, in order of appearance
/// (Bulwark `collectContactEmails`).
pub fn card_addresses(cards: &[Value]) -> Vec<String> {
    let mut out: Vec<String> = vec![];
    for c in cards {
        for e in c["emails"].as_object().into_iter().flat_map(|m| m.values()) {
            let a = e["address"].as_str().unwrap_or("").trim().to_lowercase();
            if !a.is_empty() && !out.contains(&a) { out.push(a); }
        }
    }
    out
}

/// The ids of the books named exactly "Trusted Senders", lowest id first (Bulwark sorts with
/// `localeCompare` so every client picks the same one).
pub fn book_ids(books: &[Value]) -> Vec<String> {
    let mut ids: Vec<String> = books.iter().filter(|b| b["name"].as_str() == Some(BOOK_NAME)).filter_map(|b| b["id"].as_str().map(String::from)).collect();
    ids.sort();
    ids
}

/// The list the reader and the settings page work with: while the book is in use the book's
/// senders and the device's list together (book first, no duplicates), else the device's list.
pub fn combined(local: &[String], book: &[String], in_book: bool) -> Vec<String> {
    let mut out: Vec<String> = vec![];
    let all: Vec<&String> = if in_book { book.iter().chain(local.iter()).collect() } else { local.iter().collect() };
    for a in all {
        let k = sender_key(a);
        if !k.is_empty() && !out.contains(&k) { out.push(k); }
    }
    out
}

/// email-viewer.tsx:3136: the device's list holds the sender, or the setting is on and the book
/// holds it.
pub fn trusted(local: &[String], book: &[String], setting: bool, address: &str) -> bool {
    crate::trust::is_trusted(local, address) || (setting && crate::trust::is_trusted(book, address))
}

/// The migration plan: the device's addresses the book lacks, to be filed as cards
/// (`mergeLocalTrustedSenders`). Only real addresses, each once.
pub fn to_file(book: &[String], local: &[String]) -> Vec<String> {
    let mut out: Vec<String> = vec![];
    for l in local {
        let k = sender_key(l);
        if is_address(&k) && !book.contains(&k) && !out.contains(&k) { out.push(k); }
    }
    out
}

/// The device's addresses that may be filed into the book of the account `key`: tagged for that
/// account (`tag`), or untagged while this account is the only one there is (`single`).
pub fn eligible_local(local: &[String], tags: &Map<String, Value>, tag: &str, single: bool) -> Vec<String> {
    local.iter().map(|a| sender_key(a)).filter(|a| match tags.get(a).and_then(|v| v.as_str()) {
        Some(t) => t == tag,
        None => single,
    }).collect()
}

/// What leaves the device's list once the book holds it (contact-store.ts:1777-1780): the
/// `eligible` addresses the book has. Anything else stays: addresses the book lacks (not filed)
/// and addresses that belong to another account.
pub fn remaining_local(local: &[String], book: &[String], eligible: &[String]) -> Vec<String> {
    local.iter().filter(|a| { let k = sender_key(a); !(book.contains(&k) && eligible.contains(&k)) }).cloned().collect()
}

/// Turning the setting off: the book's addresses join the device's list, each tagged with the
/// account it came from (an address already on the list keeps what it has).
pub fn copy_with_tags(local: &mut Vec<String>, tags: &mut Map<String, Value>, book: &[String], key: &str) {
    for e in book {
        if !local.contains(e) { local.push(e.clone()); tags.insert(e.clone(), json!(key)); }
    }
}

/// What is stored in `trustedSendersFrom` for an account: a hash of its `Client::state_key`.
pub fn account_tag(state_key: &str) -> String {
    use sha2::{Digest, Sha256};
    Sha256::digest(state_key.as_bytes()).iter().take(16).map(|b| format!("{b:02x}")).collect()
}

fn same_name(a: &str, b: &str) -> bool {
    let local = |s: &str| s.split('@').next().unwrap_or("").to_lowercase();
    !a.is_empty() && !b.is_empty() && (a.eq_ignore_ascii_case(b) || local(a) == local(b))
}

/// Whether the device's untagged list may be filed into this account's book: the one account
/// remembered on this computer is this one (same server, and a name that matches the typed or the
/// reported one: `ann` and `ann@example.test` are the same login), and no other session is signed
/// in. A session that is not remembered counts through `others`.
pub fn sole_remembered(entries: &[Value], names: &[&str], server: &str, others: usize) -> bool {
    if others != 0 || entries.len() != 1 { return false; }
    let e = &entries[0];
    let s = e["server"].as_str().unwrap_or("");
    let same_server = s.is_empty() || crate::jmap::normalize_base(s).eq_ignore_ascii_case(&crate::jmap::normalize_base(server));
    same_server && names.iter().any(|n| same_name(n, e["user"].as_str().unwrap_or("")))
}

/// Bulwark's v14 migration (settings-store.ts:2645-2649) for a settings file that is not
/// Barbican's own: `false` that the file does not list in `explicitSettings` means "not decided",
/// which is on.
pub fn migrate_book_default(state: &mut Map<String, Value>) {
    if state.get(SETTING_KEY).is_none_or(|v| *v == json!(true)) { return; }
    let chosen = state.get("explicitSettings").and_then(|v| v.as_array()).is_some_and(|a| a.iter().any(|x| x == SETTING_KEY));
    if !chosen || !state[SETTING_KEY].is_boolean() { state.insert(SETTING_KEY.into(), json!(true)); }
}

/// One change to a card of the book.
#[derive(Debug, PartialEq, Clone)]
pub enum Op { Destroy(String), Patch(String, Value) }

fn ptr(k: &str) -> String { k.replace('~', "~0").replace('/', "~1") }

/// The properties this module writes (and the server adds); a card with others is somebody's
/// contact and is never destroyed.
const BARE: [&str; 9] = ["@type", "version", "uid", "id", "addressBookIds", "emails", "name", "created", "updated"];

fn blank(v: &Value) -> bool {
    match v { Value::Null => true, Value::String(s) => s.is_empty(), Value::Array(a) => a.is_empty(), Value::Object(o) => o.is_empty(), _ => false }
}

/// A card nobody added anything to. Stalwart also writes `kind`, `prodId`, `language` and a
/// `vCardProps` list when it converts a card from vCard: those count as bare while they hold only
/// their default or empty values (`kind` "individual", `prodId` any text, an empty `language`
/// and `vCardProps`); a non-empty `vCardProps`, or any other property, makes the card someone's.
/// What Stalwart really adds is not verified against a live server.
fn is_bare(card: &Value) -> bool {
    let Some(o) = card.as_object() else { return false };
    let name_ok = card["name"].as_object().is_none_or(|n| n.keys().all(|k| k == "full" || k == "@type"));
    let rest_ok = o.iter().all(|(k, v)| match k.as_str() {
        "kind" => blank(v) || v.as_str() == Some("individual"),
        "prodId" => v.is_null() || v.is_string(),
        "language" | "vCardProps" => blank(v),
        k => BARE.contains(&k),
    });
    name_ok && rest_ok
}

/// Untrusting `address`: how each card of the book that holds it changes. A card filed in another
/// book too is only unfiled from this one; one with other addresses (or other content) loses just
/// this address; only a bare card with this address alone is destroyed.
pub fn removal_plan(cards: &[Value], book_id: &str, address: &str) -> Vec<Op> {
    let addr = sender_key(address);
    let mut ops = vec![];
    for c in cards {
        let Some(id) = c["id"].as_str() else { continue };
        let emails = c["emails"].as_object().cloned().unwrap_or_default();
        let (mine, others): (Vec<_>, Vec<_>) = emails.iter().partition(|(_, e)| e["address"].as_str().is_some_and(|a| a.trim().to_lowercase() == addr));
        if mine.is_empty() { continue; }
        let elsewhere = c["addressBookIds"].as_object().is_some_and(|m| m.keys().any(|k| k != book_id));
        if elsewhere {
            ops.push(Op::Patch(id.into(), json!({ format!("addressBookIds/{}", ptr(book_id)): null })));
        } else if others.is_empty() && is_bare(c) {
            ops.push(Op::Destroy(id.into()));
        } else {
            let mut p = Map::new();
            for (k, _) in &mine { p.insert(format!("emails/{}", ptr(k)), Value::Null); }
            ops.push(Op::Patch(id.into(), Value::Object(p)));
        }
    }
    ops
}

pub fn books_call(acc: &str) -> Value {
    json!([["AddressBook/get", {"accountId": acc, "ids": null, "properties": ["id", "name"]}, "b"]])
}

pub fn create_book_call(acc: &str) -> Value {
    json!([["AddressBook/set", {"accountId": acc, "create": {"b": {"name": BOOK_NAME}}}, "c"]])
}

pub fn cards_filter(book_id: &str) -> Value { json!({"filter": {"inAddressBook": book_id}}) }

/// One ContactCard/set creating a card per address (keys `t0`, `t1`...).
pub fn file_call(acc: &str, book_id: &str, addrs: &[String], mut uid: impl FnMut() -> String) -> Value {
    let create: Map<String, Value> = addrs.iter().enumerate().map(|(i, a)| (format!("t{i}"), card_for(book_id, a, &uid()))).collect();
    json!([["ContactCard/set", {"accountId": acc, "create": create}, "f"]])
}

/// Which of `addrs` a `file_call` response created, and the others with the server's reason.
pub fn filed_result(resp: &Value, addrs: &[String]) -> (Vec<String>, Vec<(String, String)>) {
    let (mut ok, mut bad) = (vec![], vec![]);
    for (i, a) in addrs.iter().enumerate() {
        let k = format!("t{i}");
        if resp["created"][&k]["id"].is_string() { ok.push(a.clone()); } else { bad.push((a.clone(), crate::jmap::set_error(&resp["notCreated"][&k]))); }
    }
    (ok, bad)
}

pub fn ops_call(acc: &str, ops: &[Op]) -> Value {
    let mut update = Map::new();
    let mut destroy = vec![];
    for o in ops {
        match o { Op::Destroy(id) => destroy.push(json!(id)), Op::Patch(id, p) => { update.insert(id.clone(), p.clone()); } }
    }
    let mut a = json!({"accountId": acc});
    if !update.is_empty() { a["update"] = Value::Object(update); }
    if !destroy.is_empty() { a["destroy"] = json!(destroy); }
    json!([["ContactCard/set", a, "r"]])
}

/// The ids a `ops_call` response refused, with the reason.
pub fn refused(resp: &Value) -> Vec<String> {
    let mut out = vec![];
    for k in ["notUpdated", "notDestroyed"] {
        for (id, e) in resp[k].as_object().into_iter().flatten() { out.push(format!("{id}: {}", crate::jmap::set_error(e))); }
    }
    out
}

/// What the settings page's text edit asks for: the addresses to trust and to stop trusting,
/// by comparing the new text with `shown`, the list as the page drew it. An address that joined
/// the list after that (a push) is in neither list, so it is never removed.
pub fn text_changes(current: &[String], text: &str) -> (Vec<String>, Vec<String>) {
    let new = crate::trust::parse_list(text);
    let add = new.iter().filter(|a| !current.contains(a)).cloned().collect();
    let remove = current.iter().filter(|a| !new.contains(a)).cloned().collect();
    (add, remove)
}

// ---------------------------------------------------------------- the cache

#[derive(Default, Clone)]
struct Cache {
    /// Who the cache belongs to (`Client::state_key`); empty until a book was read.
    key: String,
    book_ids: Vec<String>,
    emails: Vec<String>,
    loaded: bool,
    /// No contacts capability, or the book could not be read or created: the device's list is used.
    unavailable: bool,
    at: Option<std::time::Instant>,
}

/// The key of the session this run is in (set at sign-in, emptied at sign-out). Anything that
/// belongs to another account, such as a timer armed by the one before, is refused.
static SESSION: Mutex<String> = Mutex::new(String::new());

fn session() -> String { SESSION.lock().unwrap_or_else(|e| e.into_inner()).clone() }
fn session_is(key: &str) -> bool { !key.is_empty() && session() == key }

static CACHE: Mutex<Cache> = Mutex::new(Cache { key: String::new(), book_ids: Vec::new(), emails: Vec::new(), loaded: false, unavailable: false, at: None });
/// Bumped by sign-out and account change. The UI thread reads it before it starts a worker and the
/// worker is handed that value: a worker from before the bump writes nothing (no card, no cache,
/// no preference) afterwards.
static GEN: AtomicU64 = AtomicU64::new(0);
/// One load or write at a time, so two threads here can never both create the book.
static FLIGHT: Mutex<()> = Mutex::new(());
/// A load is running; a burst of pushes starts one. A forced refresh asked for meanwhile runs once
/// more afterwards, with what it was asked with.
static LOADING: AtomicBool = AtomicBool::new(false);
type Again = (u64, bool, Arc<Client>);
static AGAIN: Mutex<Option<Again>> = Mutex::new(None);
/// (account key, address) that could not be filed this session: not tried (or told about) again
/// on every push. Forgotten at sign-out and when the user turns the setting off and on.
static FAILED: Mutex<Vec<(String, String)>> = Mutex::new(Vec::new());

const STALE: &str = "stale";

fn cache() -> std::sync::MutexGuard<'static, Cache> { CACHE.lock().unwrap_or_else(|e| e.into_inner()) }
fn failed() -> std::sync::MutexGuard<'static, Vec<(String, String)>> { FAILED.lock().unwrap_or_else(|e| e.into_inner()) }

pub fn generation() -> u64 { GEN.load(Ordering::SeqCst) }
fn live(gen: u64) -> bool { GEN.load(Ordering::SeqCst) == gen }
/// Whether the cache was read for this account.
fn mine(k: &Cache, c: &Client) -> bool { mine_key(k, &c.state_key()) }
fn mine_key(k: &Cache, key: &str) -> bool { !k.key.is_empty() && k.key == key }
/// The client belongs to the session this run is in.
fn current(c: &Client) -> bool { session_is(&c.state_key()) }

/// Sign-out or account change: forget what was read, what was staged and what failed (the
/// server's book stays where it is).
pub fn clear() {
    GEN.fetch_add(1, Ordering::SeqCst);
    SESSION.lock().unwrap_or_else(|e| e.into_inner()).clear();
    *cache() = Cache::default();
    failed().clear();
    *stage() = Stage::default();
}

/// A new session begins (sign-in or account switch): forget the last one, then remember this one.
pub fn begin(c: &Client) { begin_key(&c.state_key()); }

fn begin_key(key: &str) {
    clear();
    *SESSION.lock().unwrap_or_else(|e| e.into_inner()) = key.to_string();
}

/// The cache's addresses, only while it was read for the current session.
fn session_emails(k: &Cache) -> Vec<String> {
    if k.key.is_empty() || session_is(&k.key) { k.emails.clone() } else { vec![] }
}

/// Whether the trusted list lives in the book right now: setting on and the account can keep it.
pub fn in_book(prefs: &Map<String, Value>) -> bool { setting_on(prefs) && !cache().unavailable }

/// Whether the account cannot keep the book (settings page note).
pub fn unavailable() -> bool { cache().unavailable }

/// The list to show: see [`combined`].
pub fn view_list(prefs: &Map<String, Value>) -> Vec<String> {
    // one lock: a second `cache()` in the same statement would wait for the first
    let (emails, usable) = { let k = cache(); (session_emails(&k), !k.unavailable) };
    combined(&trusted_list(prefs), &emails, setting_on(prefs) && usable)
}

/// Reader decision (cache and preferences only, no network).
pub fn is_trusted(prefs: &Map<String, Value>, address: &str) -> bool {
    let emails = { let k = cache(); session_emails(&k) };
    trusted(&trusted_list(prefs), &emails, setting_on(prefs), address)
}

// ---------------------------------------------------------------- the settings page's edit

/// The list as the page drew it, and the text typed since.
#[derive(Default)]
struct Stage { shown: Option<Shown>, text: Option<String> }
struct Shown { key: String, gen: u64, list: Vec<String> }

static STAGE: Mutex<Stage> = Mutex::new(Stage { shown: None, text: None });

fn stage() -> std::sync::MutexGuard<'static, Stage> { STAGE.lock().unwrap_or_else(|e| e.into_inner()) }

impl Stage {
    fn note(&mut self, key: String, gen: u64, list: &[String]) { *self = Stage { shown: Some(Shown { key, gen, list: list.to_vec() }), text: None }; }

    /// The staged edit with the list it was typed against, when it still belongs to this account
    /// and session. Err: `None` for nothing typed, `Some(reason)` for an edit that no longer applies.
    fn take(&mut self, key: &str, gen: u64) -> Result<(Vec<String>, String), Option<&'static str>> {
        match (self.shown.take(), self.text.take()) {
            (_, None) => Err(None),
            (None, Some(_)) => Err(Some("the page was redrawn")),
            (Some(sh), Some(t)) if sh.key == key && sh.gen == gen => Ok((sh.list, t)),
            (Some(_), Some(_)) => Err(Some("the account or the list changed")),
        }
    }
}

/// The page draws the list: remember exactly what it showed, and drop any text typed into an
/// earlier drawing.
pub fn note_shown(list: &[String]) {
    let (key, gen) = (cache().key.clone(), generation());
    stage().note(key, gen, list);
}

/// The page's text, kept until "Save changes" (typing writes nothing to the server).
pub fn set_pending(text: &str) { stage().text = Some(text.to_string()); }

fn take_staged(key: &str, gen: u64) -> Result<(Vec<String>, String), Option<&'static str>> { stage().take(key, gen) }

// ---------------------------------------------------------------- blocking JMAP (worker threads)

fn uid() -> String { crate::contacts_x::new_uid() }

fn books_of(c: &Client, acc: &str) -> Result<Vec<Value>, String> {
    let r = c.call(books_call(acc))?;
    Ok(r[0][1]["list"].as_array().cloned().unwrap_or_default())
}

fn cards_of(c: &Client, acc: &str, book_id: &str) -> Result<Vec<Value>, String> {
    let ids = c.query_all("ContactCard/query", acc, cards_filter(book_id))?;
    c.get_all("ContactCard/get", acc, &ids, None)
}

/// Read the books and their cards into the cache. Creates the book (one AddressBook/set) only
/// when `create` and none exists. Returns the book to write to, if there is one.
fn resolve(c: &Client, create: bool, gen: u64) -> Result<Option<String>, String> {
    let acc = c.account_for(CONTACTS);
    let mut ids = book_ids(&books_of(c, &acc)?);
    if ids.is_empty() && create {
        if !live(gen) { return Err(STALE.into()); }
        let r = c.call(create_book_call(&acc))?;
        let id = r[0][1]["created"]["b"]["id"].as_str().map(String::from).ok_or_else(|| crate::jmap::set_error(&r[0][1]["notCreated"]["b"]))?;
        ids.push(id);
    }
    let mut cards = vec![];
    for id in &ids { cards.extend(cards_of(c, &acc, id)?); }
    let emails = card_addresses(&cards);
    if live(gen) {
        *cache() = Cache { key: c.state_key(), book_ids: ids.clone(), emails, loaded: true, unavailable: false, at: Some(std::time::Instant::now()) };
    }
    Ok(ids.first().cloned())
}

/// The account has no contacts capability: it cannot keep the book. (A read or write that merely
/// failed does not come here: what was read for the same account is kept, as Bulwark keeps
/// `trustedSenderEmails`.)
fn mark_unavailable(c: &Client, gen: u64) {
    if !live(gen) { return; }
    let mut k = cache();
    *k = Cache { key: c.state_key(), book_ids: vec![], emails: vec![], loaded: true, unavailable: true, at: Some(std::time::Instant::now()) };
}

/// Read the book; with `file_local`, file the device's list into it (Bulwark's migration, limited
/// to what [`eligible_local`] allows for `single`) and take the filed
/// addresses off the device's list. A card that fails stays on the device and is not tried again
/// this session. `gen` is the value read on the UI thread.
/// JMAP writes: AddressBook/set create (once, only when something is to be filed and no book exists),
/// ContactCard/set create (one request per `max_set` addresses).
pub fn load(c: &Client, file_local: bool, gen: u64, single: bool) -> Result<(), String> {
    let _f = FLIGHT.lock().unwrap_or_else(|e| e.into_inner());
    if !live(gen) || !current(c) { return Ok(()); }
    if !c.has(CONTACTS) { mark_unavailable(c, gen); return Ok(()); }
    let key = c.state_key();
    let tag = account_tag(&key);
    let eligible = if file_local {
        let prefs = crate::settings::Settings::load().prefs;
        let tags = prefs.get(TAGS_KEY).and_then(|v| v.as_object().cloned()).unwrap_or_default();
        eligible_local(&trusted_list(&prefs), &tags, &tag, single)
    } else { vec![] };
    let tried = |a: &String| failed().iter().any(|(k, x)| *k == key && x == a);
    let fresh: Vec<String> = eligible.iter().filter(|a| !tried(a)).cloned().collect();
    let need_book = !to_file(&[], &fresh).is_empty();
    let book = match resolve(c, need_book, gen) {
        Ok(b) => b,
        Err(e) if e == STALE => return Ok(()),
        Err(e) => return Err(e),
    };
    if !file_local { return Ok(()); }
    let book_emails = cache().emails.clone();
    let todo = to_file(&book_emails, &fresh);
    let mut filed: Vec<String> = vec![];
    let mut newly_failed: Vec<String> = vec![];
    let mut failure: Option<String> = None;
    if let Some(book) = book.filter(|_| !todo.is_empty() && live(gen)) {
        let acc = c.account_for(CONTACTS);
        for chunk in todo.chunks(c.max_set()) {
            if !live(gen) { return Ok(()); }
            match c.call(file_call(&acc, &book, chunk, uid)) {
                Ok(r) => {
                    let (ok, bad) = filed_result(&r[0][1], chunk);
                    filed.extend(ok);
                    for (a, why) in bad { newly_failed.push(a); failure.get_or_insert(why); }
                }
                Err(e) => { newly_failed.extend(chunk.iter().cloned()); failure.get_or_insert(e); break; }
            }
        }
    }
    if !live(gen) { return Ok(()); }
    for a in &newly_failed { failed().push((key.clone(), a.clone())); }
    let book_now = {
        let mut k = cache();
        if !mine(&k, c) { return Ok(()); }
        for a in &filed { if !k.emails.contains(a) { k.emails.push(a.clone()); } }
        k.emails.clone()
    };
    // the eligible addresses the book holds leave the device's list; the rest stays
    crate::settings::trusted_state_update(|l, tags| { *l = remaining_local(l, &book_now, &eligible); tags.retain(|a, _| l.contains(a)); });
    match failure { Some(e) => Err(e), None => Ok(()) }
}

pub enum Added { Already, Filed, Local, LocalAfter(String) }

/// Trust a sender. With the book in use the card goes to the book (AddressBook/set create when no
/// book exists, ContactCard/set create); where the account cannot keep a book, or the write
/// fails, the address goes to the device's list so the sender is trusted anyway. Nothing at all
/// is written when `gen` is no longer the session's (Err("stale")).
pub fn add(c: &Client, entry: &str, gen: u64) -> Result<Added, String> {
    let addr = sender_key(entry);
    if !is_address(&addr) { return Err("That is not an email address.".into()); }
    let local_add = |addr: &str| if live(gen) { crate::settings::trusted_local_update(|l| { if !l.contains(&addr.to_string()) { l.push(addr.to_string()); } }); };
    let _f = FLIGHT.lock().unwrap_or_else(|e| e.into_inner());
    if !live(gen) || !current(c) { return Err(STALE.into()); }
    let (known, unavailable, have) = {
        let k = cache();
        if mine(&k, c) { (k.book_ids.first().cloned(), k.unavailable, k.emails.contains(&addr)) } else { (None, false, false) }
    };
    if have { return Ok(Added::Already); }
    if unavailable || !c.has(CONTACTS) { local_add(&addr); return Ok(Added::Local); }
    let book = match known {
        Some(b) => Some(b),
        None => match resolve(c, true, gen) {
            Ok(b) => b,
            Err(e) if e == STALE => return Err(e),
            Err(e) => { local_add(&addr); return Ok(Added::LocalAfter(e)); }
        },
    };
    let Some(book) = book else { local_add(&addr); return Ok(Added::Local) };
    if cache().emails.contains(&addr) { return Ok(Added::Already); }
    if !live(gen) { return Err(STALE.into()); }
    let acc = c.account_for(CONTACTS);
    let one = [addr.clone()];
    let res = c.call(file_call(&acc, &book, &one, uid)).and_then(|r| {
        let (ok, bad) = filed_result(&r[0][1], &one);
        if ok.is_empty() { Err(bad.into_iter().next().map(|b| b.1).unwrap_or_default()) } else { Ok(()) }
    });
    match res {
        Ok(()) => { let mut k = cache(); if live(gen) && mine(&k, c) { k.emails.push(addr); } Ok(Added::Filed) }
        Err(e) => { local_add(&addr); Ok(Added::LocalAfter(e)) }
    }
}

/// Stop trusting an address: the cards of every "Trusted Senders" book change as [`removal_plan`]
/// says (one ContactCard/set per book), then the address leaves the device's list. Refused (Err)
/// while this account's book has not been read, and when the server refuses; nothing is changed
/// then, here or on the server. Where the account cannot keep a book only the device's list changes.
pub fn remove(c: &Client, entry: &str, gen: u64) -> Result<(), String> {
    let addr = sender_key(entry);
    let _f = FLIGHT.lock().unwrap_or_else(|e| e.into_inner());
    if !live(gen) || !current(c) { return Err(STALE.into()); }
    let (books, unavailable) = {
        let k = cache();
        if !mine(&k, c) || !k.loaded { return Err("the address book has not been read yet".into()); }
        (k.book_ids.clone(), k.unavailable)
    };
    let acc = c.account_for(CONTACTS);
    if !unavailable && c.has(CONTACTS) {
        for b in &books {
            let ops = removal_plan(&cards_of(c, &acc, b)?, b, &addr);
            if ops.is_empty() { continue; }
            if !live(gen) { return Err(STALE.into()); }
            let r = c.call(ops_call(&acc, &ops))?;
            let bad = refused(&r[0][1]);
            if !bad.is_empty() { return Err(bad.join("; ")); }
        }
    }
    if !live(gen) { return Err(STALE.into()); }
    { let mut k = cache(); if mine(&k, c) { k.emails.retain(|e| *e != addr); } }
    crate::settings::trusted_local_update(|l| l.retain(|e| *e != addr));
    Ok(())
}

// ---------------------------------------------------------------- called from the UI thread

use crate::AppWindow;

fn finished(u: &AppWindow) {
    crate::redecide_remote(u);
    if u.get_show_settings() { u.invoke_settings_search(u.get_settings_query()); }
}

/// Whether the device's untagged list may be filed into `c`'s book (see [`sole_remembered`]).
fn single_account(c: &Client) -> bool {
    let login = crate::unified::login_of(c);
    sole_remembered(&crate::oauth::accounts(), &[c.username.as_str(), login.as_str()], &c.base, crate::unified::sessions_besides(c))
}

/// Clears `LOADING` when the worker ends, however it ends.
struct Running;
impl Drop for Running { fn drop(&mut self) { LOADING.store(false, Ordering::SeqCst); } }

/// Read the book in the background (sign-in, account switch, push, the settings switch turned on).
/// Does nothing while the setting is off (no server traffic then), for a client that is not the
/// current session's (a timer armed before a sign-out or switch), while a load is already running
/// (a forced one then runs once more afterwards) and, unless `force`, when this account's book
/// was read in the last ten seconds.
pub fn refresh(w: slint::Weak<AppWindow>, c: Arc<Client>, force: bool) {
    if !setting_on(&crate::settings::Settings::load().prefs) || !current(&c) { return; }
    if force { failed().clear(); }
    {
        let k = cache();
        if !force && mine(&k, &c) && k.at.is_some_and(|t| t.elapsed() < std::time::Duration::from_secs(10)) { return; }
    }
    let ask = (generation(), single_account(&c), c);
    if LOADING.swap(true, Ordering::SeqCst) {
        if force { *AGAIN.lock().unwrap_or_else(|e| e.into_inner()) = Some(ask); }
        return;
    }
    std::thread::spawn(move || {
        let mut guard = Some(Running);
        let mut ask = Some(ask);
        loop {
            while let Some((gen, single, c)) = ask.take() {
                let res = load(&c, true, gen, single);
                let w2 = w.clone();
                let _ = w2.upgrade_in_event_loop(move |u| {
                    if let Err(e) = res { crate::mail::show_toast(&u, &format!("Trusted senders address book: {e}"), false); }
                    finished(&u);
                });
                ask = AGAIN.lock().unwrap_or_else(|e| e.into_inner()).take();
            }
            // a forced refresh asked for just as the loop ended is not lost
            guard = None;
            let late = AGAIN.lock().unwrap_or_else(|e| e.into_inner()).take();
            match late {
                None => break,
                Some(a) => {
                    if LOADING.swap(true, Ordering::SeqCst) {
                        // another refresh took over; it runs this one when it ends
                        *AGAIN.lock().unwrap_or_else(|e| e.into_inner()) = Some(a);
                        break;
                    }
                    guard = Some(Running);
                    ask = Some(a);
                }
            }
        }
        drop(guard);
    });
}

/// "Trust sender" in the reader (and the settings page's additions): trusted at once for the open
/// message by the caller; this files the address in the background and says how it went.
pub fn trust_sender(w: slint::Weak<AppWindow>, c: Option<Arc<Client>>, entry: String, settings_on: bool) {
    let gen = generation();
    std::thread::spawn(move || {
        let addr = sender_key(&entry);
        let res = match (&c, settings_on) {
            (Some(c), true) => add(c, &entry, gen),
            _ => {
                if live(gen) { crate::settings::trusted_local_update(|l| { if !l.contains(&addr) { l.push(addr.clone()); } }); }
                Ok(Added::Local)
            }
        };
        let msg = match res {
            Ok(Added::LocalAfter(e)) => Some(format!("Trusted on this computer only: the Trusted Senders address book could not be written ({e}).")),
            Ok(_) => Some(format!("Images from {addr} will load from now on.")),
            Err(e) if e == STALE => None,
            Err(e) => Some(e),
        };
        let _ = w.upgrade_in_event_loop(move |u| {
            if let Some(m) = msg { crate::mail::show_toast(&u, &m, false); }
            finished(&u);
        });
    });
}

/// "Save changes": trust the added lines and untrust the removed ones, in the background. The
/// edit is compared with the list the page drew (not with the list as it is now), so an address
/// that joined meanwhile is never removed; an edit typed for another account or an earlier
/// session is dropped with a message. Removals need this account's book to have been read; without
/// that they are refused and said so.
pub fn apply_pending(w: slint::Weak<AppWindow>, c: Option<Arc<Client>>) {
    let (gen, key) = (generation(), cache().key.clone());
    let (snapshot, text) = match take_staged(&key, gen) {
        Ok(x) => x,
        Err(None) => return,
        Err(Some(why)) => {
            if let Some(u) = w.upgrade() {
                crate::mail::show_toast(&u, &format!("Not saved: {why}. Review the list and save again."), false);
                finished(&u);
            }
            return;
        }
    };
    let prefs = crate::settings::Settings::load().prefs;
    let on = in_book(&prefs);
    let loaded = c.as_ref().is_some_and(|c| !key.is_empty() && key == c.state_key());
    let (add_l, rem_l) = text_changes(&snapshot, &text);
    if add_l.is_empty() && rem_l.is_empty() { return; }
    std::thread::spawn(move || {
        let mut notes: Vec<String> = vec![];
        let mut stale = false;
        for a in &add_l {
            let r = match (&c, on) {
                (Some(c), true) => add(c, a, gen),
                _ => {
                    if live(gen) { crate::settings::trusted_local_update(|l| { if !l.contains(a) { l.push(a.clone()); } }); }
                    Ok(Added::Local)
                }
            };
            match r {
                Ok(Added::LocalAfter(e)) => notes.push(format!("{a}: trusted on this computer only ({e})")),
                Ok(_) => {}
                Err(e) if e == STALE => stale = true,
                Err(e) => notes.push(format!("{a}: {e}")),
            }
        }
        for a in &rem_l {
            let r = match (&c, on) {
                (Some(c), true) if loaded => remove(c, a, gen),
                (_, true) => Err("not removed: the address book has not been read yet, so it cannot be changed (try again when it is reachable)".into()),
                _ => {
                    if live(gen) { crate::settings::trusted_local_update(|l| l.retain(|x| x != a)); }
                    Ok(())
                }
            };
            match r {
                Err(e) if e == STALE => stale = true,
                Err(e) => notes.push(format!("{a}: {e}")),
                Ok(()) => {}
            }
        }
        let msg = if stale { None } else if notes.is_empty() { Some("Trusted senders saved".to_string()) } else { Some(format!("Some changes were not saved: {}", notes.join("; "))) };
        let _ = w.upgrade_in_event_loop(move |u| { if let Some(m) = msg { crate::mail::show_toast(&u, &m, false); } finished(&u); });
    });
}

/// The settings switch changed (called after the new value is stored). Off: the book's senders
/// are copied to the device's list, each tagged with its account (the book is not touched), after
/// reading the book if it has not been read yet; when that is not possible the user is told that
/// nothing was copied. On: read the book and file the device's list.
pub fn toggled(on: bool, w: slint::Weak<AppWindow>, c: Option<Arc<Client>>) {
    if on {
        if let Some(c) = c { refresh(w, c, true); }
        return;
    }
    let gen = generation();
    let Some(c) = c else {
        if let Some(u) = w.upgrade() { crate::mail::show_toast(&u, "Turned off. There is no connection to the address book, so its senders were not copied to this computer.", false); }
        return;
    };
    std::thread::spawn(move || {
        let read = { let k = cache(); mine(&k, &c) && k.loaded };
        let res = if read { Ok(()) } else { load(&c, false, gen, false) };
        let copied = live(gen) && {
            let (ok, emails, key) = { let k = cache(); (mine(&k, &c) && k.loaded && !k.unavailable, k.emails.clone(), k.key.clone()) };
            if ok { let tag = account_tag(&key); crate::settings::trusted_state_update(|l, tags| copy_with_tags(l, tags, &emails, &tag)); }
            ok
        };
        let msg = match (copied, res) {
            (true, _) => None,
            (false, Err(e)) if live(gen) => Some(format!("Turned off, but the address book could not be read ({e}), so its senders were not copied to this computer.")),
            _ => None,
        };
        let _ = w.upgrade_in_event_loop(move |u| { if let Some(m) = msg { crate::mail::show_toast(&u, &m, false); } finished(&u); });
    });
}

#[cfg(test)]
mod tests {
    use super::*;

    fn s(v: &[&str]) -> Vec<String> { v.iter().map(|x| x.to_string()).collect() }
    fn prefs(v: Value) -> Map<String, Value> { v.as_object().unwrap().clone() }
    /// The cache, the stage and the generation are process-wide: tests that touch them take turns.
    static GLOBALS: Mutex<()> = Mutex::new(());

    #[test]
    fn the_setting_defaults_to_on_as_in_bulwark() {
        assert!(setting_on(&prefs(json!({}))));
        assert!(setting_on(&prefs(json!({"trustedSendersAddressBook": true}))));
        assert!(setting_on(&prefs(json!({"trustedSendersAddressBook": "no"}))));
        assert!(!setting_on(&prefs(json!({"trustedSendersAddressBook": false}))));
    }

    #[test]
    fn a_name_is_kept_only_from_name_angle_form() {
        assert_eq!(display_name("Ann Lee <Ann@Example.Test>").as_deref(), Some("Ann Lee"));
        assert_eq!(display_name("ann@example.test"), None);
        assert_eq!(display_name("<ann@example.test>"), None);
        assert_eq!(display_name("  Ann  <a@b.test> ").as_deref(), Some("Ann"));
    }

    #[test]
    fn the_card_has_bulwarks_shape() {
        let c = card_for("b1", "Ann Lee <Ann@Example.Test>", "urn:uuid:x");
        assert_eq!(c, json!({"@type": "Card", "version": "1.0", "uid": "urn:uuid:x", "addressBookIds": {"b1": true},
            "emails": {"email": {"address": "ann@example.test"}}, "name": {"full": "Ann Lee"}}));
        let bare = card_for("b1", "bob@example.test", "urn:uuid:y");
        assert!(bare.get("name").is_none());
        assert_eq!(bare["emails"]["email"]["address"], "bob@example.test");
    }

    #[test]
    fn addresses_are_read_lower_cased_and_once() {
        let cards = vec![
            json!({"id": "1", "emails": {"a": {"address": " Ann@Example.Test "}, "b": {"address": "bob@example.test"}}}),
            json!({"id": "2", "emails": {"e": {"address": "ann@example.test"}}}),
            json!({"id": "3", "name": {"full": "No mail"}}),
            json!({"id": "4", "emails": {"e": {"address": ""}}}),
        ];
        assert_eq!(card_addresses(&cards), s(&["ann@example.test", "bob@example.test"]));
    }

    #[test]
    fn only_books_named_exactly_are_used_lowest_id_first() {
        let books = vec![
            json!({"id": "b9", "name": "Trusted Senders"}), json!({"id": "b2", "name": "Contacts"}),
            json!({"id": "b3", "name": "Trusted Senders"}), json!({"id": "b4", "name": "trusted senders"}),
            json!({"id": "b5", "name": "Trusted Senders "}),
        ];
        assert_eq!(book_ids(&books), s(&["b3", "b9"]));
        assert!(book_ids(&[json!({"id": "x", "name": "Contacts"})]).is_empty());
    }

    #[test]
    fn the_lists_combine_only_while_the_book_is_in_use() {
        let local = s(&["Zed@Example.Test", "ann@example.test"]);
        let book = s(&["ann@example.test", "bob@example.test"]);
        assert_eq!(combined(&local, &book, true), s(&["ann@example.test", "bob@example.test", "zed@example.test"]));
        assert_eq!(combined(&local, &book, false), s(&["zed@example.test", "ann@example.test"]));
    }

    #[test]
    fn trust_is_the_device_list_or_the_book_while_the_setting_is_on() {
        let local = s(&["loc@example.test"]);
        let book = s(&["bk@example.test"]);
        assert!(trusted(&local, &book, true, "Loc <LOC@example.test>"));
        assert!(trusted(&local, &book, true, "bk@example.test"));
        assert!(!trusted(&local, &book, false, "bk@example.test"), "setting off: the book is not read");
        assert!(trusted(&local, &book, false, "loc@example.test"));
        assert!(!trusted(&local, &book, true, "other@example.test"));
        assert!(!trusted(&local, &book, true, ""));
    }

    #[test]
    fn the_migration_files_what_the_book_lacks() {
        let book = s(&["ann@example.test"]);
        let local = s(&["ANN@example.test", "bob@example.test", "bob@example.test", "junk", "Cy <cy@example.test>"]);
        assert_eq!(to_file(&book, &local), s(&["bob@example.test", "cy@example.test"]));
        assert!(to_file(&book, &[]).is_empty());
        assert!(to_file(&s(&["bob@example.test"]), &s(&["bob@example.test"])).is_empty(), "a second run finds nothing");
        // what the book holds leaves the device's list; an address that failed to file stays
        let all = s(&["ann@example.test", "bob@example.test"]);
        assert_eq!(remaining_local(&local, &all, &all), s(&["junk", "Cy <cy@example.test>"]));
        // an address that belongs to another account's book stays, even when this book holds it
        assert_eq!(remaining_local(&s(&["bob@example.test"]), &all, &s(&[])), s(&["bob@example.test"]));
    }

    #[test]
    fn removal_destroys_only_a_bare_single_address_card() {
        let bare = json!({"id": "c1", "uid": "u", "addressBookIds": {"b1": true}, "emails": {"email": {"address": "Ann@example.test"}}, "name": {"full": "Ann"}});
        assert_eq!(removal_plan(std::slice::from_ref(&bare), "b1", "ann@example.test"), vec![Op::Destroy("c1".into())]);
        // filed in another book too: only unfiled
        let shared = json!({"id": "c2", "addressBookIds": {"b1": true, "b7": true}, "emails": {"e": {"address": "ann@example.test"}}});
        assert_eq!(removal_plan(&[shared], "b1", "ann@example.test"), vec![Op::Patch("c2".into(), json!({"addressBookIds/b1": null}))]);
        // another address on the card: only this address goes
        let two = json!({"id": "c3", "addressBookIds": {"b1": true}, "emails": {"e1": {"address": "ann@example.test"}, "e2": {"address": "ann2@example.test"}}});
        assert_eq!(removal_plan(&[two], "b1", "ann@example.test"), vec![Op::Patch("c3".into(), json!({"emails/e1": null}))]);
        // real contact data on the card: never destroyed
        let rich = json!({"id": "c4", "addressBookIds": {"b1": true}, "emails": {"e": {"address": "ann@example.test"}}, "notes": {"n": {"note": "x"}}});
        assert_eq!(removal_plan(&[rich], "b1", "ann@example.test"), vec![Op::Patch("c4".into(), json!({"emails/e": null}))]);
        // other addresses are left alone
        let other = json!({"id": "c5", "addressBookIds": {"b1": true}, "emails": {"e": {"address": "bob@example.test"}}});
        assert!(removal_plan(&[other], "b1", "ann@example.test").is_empty());
    }

    #[test]
    fn request_shapes() {
        assert_eq!(books_call("a1"), json!([["AddressBook/get", {"accountId": "a1", "ids": null, "properties": ["id", "name"]}, "b"]]));
        assert_eq!(create_book_call("a1"), json!([["AddressBook/set", {"accountId": "a1", "create": {"b": {"name": "Trusted Senders"}}}, "c"]]));
        assert_eq!(cards_filter("b1"), json!({"filter": {"inAddressBook": "b1"}}));
        let mut n = 0;
        let f = file_call("a1", "b1", &s(&["a@example.test", "b@example.test"]), || { n += 1; format!("urn:uuid:{n}") });
        assert_eq!(f[0][0], "ContactCard/set");
        assert_eq!(f[0][1]["accountId"], "a1");
        assert_eq!(f[0][1]["create"]["t1"]["emails"]["email"]["address"], "b@example.test");
        assert_eq!(f[0][1]["create"]["t0"]["uid"], "urn:uuid:1");
        assert_eq!(f[0][1]["create"]["t0"]["addressBookIds"], json!({"b1": true}));
        assert!(f[0][1].get("update").is_none() && f[0][1].get("destroy").is_none());
        let ops = ops_call("a1", &[Op::Destroy("c1".into()), Op::Patch("c2".into(), json!({"emails/e": null}))]);
        assert_eq!(ops[0][1], json!({"accountId": "a1", "update": {"c2": {"emails/e": null}}, "destroy": ["c1"]}));
        assert_eq!(ops_call("a1", &[Op::Destroy("c1".into())])[0][1], json!({"accountId": "a1", "destroy": ["c1"]}));
    }

    #[test]
    fn responses_are_read_per_card() {
        let addrs = s(&["a@example.test", "b@example.test"]);
        let resp = json!({"created": {"t0": {"id": "k1"}}, "notCreated": {"t1": {"type": "forbidden"}}});
        let (ok, bad) = filed_result(&resp, &addrs);
        assert_eq!(ok, s(&["a@example.test"]));
        assert_eq!(bad.len(), 1);
        assert_eq!(bad[0].0, "b@example.test");
        assert!(refused(&json!({"updated": {"c": null}})).is_empty());
        assert_eq!(refused(&json!({"notDestroyed": {"c1": {"type": "forbidden"}}})).len(), 1);
    }

    #[test]
    fn text_edits_become_adds_and_removes() {
        let cur = s(&["ann@example.test", "bob@example.test"]);
        let (add, rem) = text_changes(&cur, "ann@example.test\ncy@example.test; half-typed");
        assert_eq!(add, s(&["cy@example.test"]));
        assert_eq!(rem, s(&["bob@example.test"]));
        assert_eq!(text_changes(&cur, "bob@example.test\nANN@example.test"), (vec![], vec![]));
    }

    #[test]
    fn the_device_list_is_filed_only_for_its_own_account() {
        let local = s(&["a@example.test", "b@example.test", "c@example.test"]);
        let tags = prefs(json!({"b@example.test": "acct-B", "c@example.test": "acct-A"}));
        // one remembered account: untagged and its own tagged ones, never another account's
        assert_eq!(eligible_local(&local, &tags, "acct-A", true), s(&["a@example.test", "c@example.test"]));
        // several remembered accounts: only what came from this account's book
        assert_eq!(eligible_local(&local, &tags, "acct-A", false), s(&["c@example.test"]));
        assert!(eligible_local(&local, &tags, "acct-Z", false).is_empty());
        assert_eq!(eligible_local(&local, &prefs(json!({})), "acct-A", true), local);
        assert!(eligible_local(&local, &prefs(json!({})), "acct-A", false).is_empty());
    }

    #[test]
    fn turning_the_setting_off_tags_what_it_copies() {
        let mut local = s(&["own@example.test"]);
        let mut tags = prefs(json!({}));
        copy_with_tags(&mut local, &mut tags, &s(&["own@example.test", "bk@example.test"]), "acct-A");
        assert_eq!(local, s(&["own@example.test", "bk@example.test"]));
        assert_eq!(Value::Object(tags), json!({"bk@example.test": "acct-A"}));
    }

    #[test]
    fn an_old_bulwark_false_nobody_chose_turns_on() {
        let mut p = prefs(json!({"trustedSendersAddressBook": false}));
        migrate_book_default(&mut p);
        assert_eq!(p[SETTING_KEY], json!(true));
        let mut chosen = prefs(json!({"trustedSendersAddressBook": false, "explicitSettings": ["trustedSendersAddressBook"]}));
        migrate_book_default(&mut chosen);
        assert_eq!(chosen[SETTING_KEY], json!(false));
        let mut other = prefs(json!({"trustedSendersAddressBook": false, "explicitSettings": ["theme"]}));
        migrate_book_default(&mut other);
        assert_eq!(other[SETTING_KEY], json!(true));
        let mut absent = prefs(json!({"theme": "dark"}));
        migrate_book_default(&mut absent);
        assert!(absent.get(SETTING_KEY).is_none());
        let mut null = prefs(json!({"trustedSendersAddressBook": null}));
        migrate_book_default(&mut null);
        assert_eq!(null[SETTING_KEY], json!(true));
    }

    #[test]
    fn a_card_stalwart_converted_is_still_bare_unless_it_holds_data() {
        let conv = json!({"id": "c", "@type": "Card", "uid": "u", "version": "1.0", "kind": "individual", "prodId": "x", "language": "",
            "vCardProps": [], "addressBookIds": {"b1": true}, "name": {"@type": "Name", "full": "Ann"}, "emails": {"e": {"address": "ann@example.test"}}});
        assert_eq!(removal_plan(std::slice::from_ref(&conv), "b1", "ann@example.test"), vec![Op::Destroy("c".into())]);
        for (k, v) in [("vCardProps", json!([["x-foo", {}, "text", "bar"]])), ("kind", json!("group")), ("language", json!("tr")), ("nicknames", json!({"n": {"name": "A"}}))] {
            let mut c = conv.clone();
            c[k] = v;
            assert_eq!(removal_plan(&[c], "b1", "ann@example.test"), vec![Op::Patch("c".into(), json!({"emails/e": null}))], "{k}");
        }
        let mut comps = conv.clone();
        comps["name"] = json!({"full": "Ann", "components": [{"kind": "given", "value": "Ann"}]});
        assert!(matches!(removal_plan(&[comps], "b1", "ann@example.test")[0], Op::Patch(..)));
    }

    #[test]
    fn a_save_never_removes_what_joined_after_the_page_was_drawn() {
        // the page showed [a, b]; a push added x; the user deleted b and typed c
        let shown = s(&["a@example.test", "b@example.test"]);
        let (add, rem) = text_changes(&shown, "a@example.test
c@example.test");
        assert_eq!(add, s(&["c@example.test"]));
        assert_eq!(rem, s(&["b@example.test"]), "x was not in the snapshot, so it is not removed");
        // the text still holds a line for an address that is also new on the server: nothing to remove
        assert_eq!(text_changes(&shown, "a@example.test
b@example.test
x@example.test").1, Vec::<String>::new());
    }

    #[test]
    fn staged_text_is_applied_only_to_its_own_account_and_session() {
        let mut st = Stage::default();
        st.note("acct-A".into(), 7, &s(&["a@example.test"]));
        st.text = Some("a@example.test
b@example.test".into());
        // typed under account A, applied after a switch to B (or in a later session): refused, and dropped
        assert_eq!(st.take("acct-B", 7), Err(Some("the account or the list changed")));
        assert_eq!(st.take("acct-A", 7), Err(None), "nothing is left to apply");
        st.note("acct-A".into(), 7, &s(&["a@example.test"]));
        st.text = Some("x".into());
        assert_eq!(st.take("acct-A", 8), Err(Some("the account or the list changed")));
        // same account and session: the text comes back with the list it was typed against
        st.note("acct-A".into(), 7, &s(&["a@example.test"]));
        st.text = Some("x".into());
        assert_eq!(st.take("acct-A", 7), Ok((s(&["a@example.test"]), "x".into())));
        // a redraw drops the text typed into the earlier drawing
        st.note("acct-A".into(), 7, &s(&["a@example.test"]));
        st.text = Some("typed".into());
        st.note("acct-A".into(), 7, &s(&["a@example.test", "y@example.test"]));
        assert_eq!(st.take("acct-A", 7), Err(None));
        // text with no drawing behind it is never applied
        st.text = Some("orphan".into());
        assert_eq!(st.take("acct-A", 7), Err(Some("the page was redrawn")));
    }

    #[test]
    fn a_worker_from_before_sign_out_may_no_longer_write() {
        let _g = GLOBALS.lock().unwrap_or_else(|e| e.into_inner());
        clear();
        let before = generation();
        assert!(live(before));
        cache().key = "acct-A".into();
        stage().text = Some("late".into());
        clear();
        assert!(!live(before), "a worker handed the old generation writes nothing");
        assert!(live(generation()));
        assert!(stage().text.is_none() && cache().key.is_empty());
        // the cache is checked against the account on read and write
        let k = Cache { key: "acct-A".into(), ..Default::default() };
        assert!(mine_key(&k, "acct-A") && !mine_key(&k, "acct-B"));
        assert!(!mine_key(&Cache::default(), ""), "an empty cache belongs to nobody");
        // addresses that failed to file are remembered for the session only
        failed().push(("acct-A".into(), "x@example.test".into()));
        clear();
        assert!(failed().is_empty());
    }

    #[test]
    fn the_untagged_list_is_filed_only_for_the_one_account_there_is() {
        let a = json!({"user": "ann", "server": "https://mail.example.test/"});
        let names = ["ann@example.test", "ann@example.test"];
        // A is the only remembered account and the session is A's
        assert!(sole_remembered(std::slice::from_ref(&a), &names, "https://mail.example.test", 0));
        // an extra session that is not remembered (B added without "remember", OAuth without a refresh token)
        assert!(!sole_remembered(std::slice::from_ref(&a), &names, "https://mail.example.test", 1));
        // B is the current session, but the one remembered account is A
        assert!(!sole_remembered(std::slice::from_ref(&a), &["bob@example.test"], "https://mail.example.test", 0));
        // same name on another server is another account
        assert!(!sole_remembered(std::slice::from_ref(&a), &names, "https://other.example.test", 0));
        // two remembered, or none
        assert!(!sole_remembered(&[a.clone(), json!({"user": "bob", "server": "https://mail.example.test"})], &names, "https://mail.example.test", 0));
        assert!(!sole_remembered(&[], &names, "https://mail.example.test", 0));
    }

    #[test]
    fn the_tag_is_a_hash_that_holds_no_names() {
        let k = "https://mail.example.test\nann@example.test\nacc1";
        let t = account_tag(k);
        assert_eq!(t.len(), 32);
        assert!(t.chars().all(|c| c.is_ascii_hexdigit()));
        assert_eq!(t, account_tag(k));
        assert_ne!(t, account_tag("https://mail.example.test\nbob@example.test\nacc2"));
    }

    #[test]
    fn a_leftover_timer_of_the_last_session_is_refused() {
        let _g = GLOBALS.lock().unwrap_or_else(|e| e.into_inner());
        clear();
        assert!(!session_is("acct-A"), "no session after sign-out");
        begin_key("acct-A");
        assert!(session_is("acct-A") && !session_is("acct-B"));
        // A's book lands in the cache, then the user switches to B
        { let mut k = cache(); k.key = "acct-A".into(); k.emails = s(&["bk@example.test"]); k.loaded = true; }
        let p = prefs(json!({}));
        assert!(is_trusted(&p, "bk@example.test"));
        begin_key("acct-B");
        assert!(!session_is("acct-A"), "a refresh for A's client is refused: its key is not the session's");
        // a cache that belongs to another session is ignored even if one got in
        { let mut k = cache(); k.key = "acct-A".into(); k.emails = s(&["bk@example.test"]); }
        assert!(!is_trusted(&p, "bk@example.test"));
        assert!(view_list(&p).is_empty());
        clear();
        assert!(!session_is("acct-B"));
    }

    #[test]
    fn the_cache_is_forgotten_on_clear_and_decides_trust() {
        let _g = GLOBALS.lock().unwrap_or_else(|e| e.into_inner());
        clear();
        let p = prefs(json!({"trustedSenders": ["loc@example.test"]}));
        assert!(!is_trusted(&p, "bk@example.test"));
        {
            let mut k = cache();
            k.emails = s(&["bk@example.test"]);
            k.loaded = true;
        }
        assert!(is_trusted(&p, "bk@example.test"));
        assert!(is_trusted(&p, "loc@example.test"));
        assert_eq!(view_list(&p), s(&["bk@example.test", "loc@example.test"]));
        // setting off: the local list only, the book is not read
        let off = prefs(json!({"trustedSenders": ["loc@example.test"], "trustedSendersAddressBook": false}));
        assert!(!is_trusted(&off, "bk@example.test"));
        assert_eq!(view_list(&off), s(&["loc@example.test"]));
        // an account that cannot keep a book: the local list only
        cache().unavailable = true;
        assert!(!in_book(&p));
        assert_eq!(view_list(&p), s(&["loc@example.test"]));
        clear();
        assert!(!is_trusted(&p, "bk@example.test"));
        assert!(!unavailable());
    }
}
