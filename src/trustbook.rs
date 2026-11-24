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
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};

pub const SETTING_KEY: &str = "trustedSendersAddressBook";
pub const BOOK_NAME: &str = "Trusted Senders";
/// Settings page: the combined list as text (edited, applied with the Save button), and the button.
pub const TEXT_KEY: &str = "trusted.bookText";
pub const SAVE_KEY: &str = "trusted.save";
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

/// What leaves the device's list once the book holds it (contact-store.ts:1777-1780): every
/// address the book has. Addresses the book lacks (not filed) stay.
pub fn remaining_local(local: &[String], book: &[String]) -> Vec<String> {
    local.iter().filter(|a| !book.contains(&sender_key(a))).cloned().collect()
}

/// One change to a card of the book.
#[derive(Debug, PartialEq, Clone)]
pub enum Op { Destroy(String), Patch(String, Value) }

fn ptr(k: &str) -> String { k.replace('~', "~0").replace('/', "~1") }

/// The properties this module writes (and the server adds); a card with others is somebody's
/// contact and is never destroyed.
const BARE: [&str; 9] = ["@type", "version", "uid", "id", "addressBookIds", "emails", "name", "created", "updated"];

fn is_bare(card: &Value) -> bool {
    let name_ok = card["name"].as_object().is_none_or(|n| n.keys().all(|k| k == "full" || k == "@type"));
    name_ok && card.as_object().is_some_and(|o| o.keys().all(|k| BARE.contains(&k.as_str())))
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
/// by comparing the new text with the list as it was shown.
pub fn text_changes(current: &[String], text: &str) -> (Vec<String>, Vec<String>) {
    let new = crate::trust::parse_list(text);
    let add = new.iter().filter(|a| !current.contains(a)).cloned().collect();
    let remove = current.iter().filter(|a| !new.contains(a)).cloned().collect();
    (add, remove)
}

// ---------------------------------------------------------------- the cache

#[derive(Default, Clone)]
struct Cache {
    /// Who the cache belongs to (`Client::state_key`).
    key: String,
    book_ids: Vec<String>,
    emails: Vec<String>,
    loaded: bool,
    /// No contacts capability, or the book could not be read or created: the device's list is used.
    unavailable: bool,
    at: Option<std::time::Instant>,
}

static CACHE: Mutex<Cache> = Mutex::new(Cache { key: String::new(), book_ids: Vec::new(), emails: Vec::new(), loaded: false, unavailable: false, at: None });
/// Bumped by sign-out: a worker that started before it writes nothing afterwards.
static GEN: AtomicU64 = AtomicU64::new(0);
/// One load or write at a time, so two threads can never both create the book.
static FLIGHT: Mutex<()> = Mutex::new(());

fn cache() -> std::sync::MutexGuard<'static, Cache> { CACHE.lock().unwrap_or_else(|e| e.into_inner()) }

/// Sign-out or account change: forget what was read (the server's book stays where it is).
pub fn clear() {
    GEN.fetch_add(1, Ordering::SeqCst);
    *cache() = Cache::default();
}

/// Whether the trusted list lives in the book right now: setting on and the account can keep it.
pub fn in_book(prefs: &Map<String, Value>) -> bool { setting_on(prefs) && !cache().unavailable }

/// Whether the account cannot keep the book (settings page note).
pub fn unavailable() -> bool { cache().unavailable }

/// The list to show: see [`combined`].
pub fn view_list(prefs: &Map<String, Value>) -> Vec<String> {
    // one lock: a second `cache()` in the same statement would wait for the first
    let (emails, usable) = { let k = cache(); (k.emails.clone(), !k.unavailable) };
    combined(&trusted_list(prefs), &emails, setting_on(prefs) && usable)
}

/// Reader decision (cache and preferences only, no network).
pub fn is_trusted(prefs: &Map<String, Value>, address: &str) -> bool {
    trusted(&trusted_list(prefs), &cache().emails, setting_on(prefs), address)
}

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
        let r = c.call(create_book_call(&acc))?;
        let id = r[0][1]["created"]["b"]["id"].as_str().map(String::from).ok_or_else(|| crate::jmap::set_error(&r[0][1]["notCreated"]["b"]))?;
        ids.push(id);
    }
    let mut cards = vec![];
    for id in &ids { cards.extend(cards_of(c, &acc, id)?); }
    let emails = card_addresses(&cards);
    if GEN.load(Ordering::SeqCst) == gen {
        *cache() = Cache { key: c.state_key(), book_ids: ids.clone(), emails, loaded: true, unavailable: false, at: Some(std::time::Instant::now()) };
    }
    Ok(ids.first().cloned())
}

fn mark_unavailable(c: &Client, gen: u64) {
    if GEN.load(Ordering::SeqCst) != gen { return; }
    let mut k = cache();
    k.key = c.state_key();
    k.loaded = true;
    k.unavailable = true;
    k.at = Some(std::time::Instant::now());
}

/// Read the book; with `file_local`, file the device's list into it (Bulwark's migration) and take
/// the filed addresses off the device's list. A failed card leaves its address on the device.
/// JMAP writes: AddressBook/set create (once, only when something is to be filed and no book exists),
/// ContactCard/set create (one request per `max_set` addresses).
pub fn load(c: &Client, file_local: bool) -> Result<(), String> {
    let _f = FLIGHT.lock().unwrap_or_else(|e| e.into_inner());
    let gen = GEN.load(Ordering::SeqCst);
    if !c.has(CONTACTS) { mark_unavailable(c, gen); return Ok(()); }
    let local = if file_local { trusted_list(&crate::settings::Settings::load().prefs) } else { vec![] };
    let need_book = !to_file(&[], &local).is_empty();
    let book = match resolve(c, need_book, gen) {
        Ok(b) => b,
        Err(e) => { mark_unavailable(c, gen); return Err(e); }
    };
    if !file_local { return Ok(()); }
    let book_emails = cache().emails.clone();
    let todo = to_file(&book_emails, &local);
    let mut filed: Vec<String> = vec![];
    let mut failure: Option<String> = None;
    if let Some(book) = book.filter(|_| !todo.is_empty()) {
        let acc = c.account_for(CONTACTS);
        for chunk in todo.chunks(c.max_set()) {
            match c.call(file_call(&acc, &book, chunk, uid)) {
                Ok(r) => { let (ok, bad) = filed_result(&r[0][1], chunk); filed.extend(ok); if failure.is_none() { failure = bad.into_iter().next().map(|b| b.1); } }
                Err(e) => { failure.get_or_insert(e); break; }
            }
        }
    }
    if GEN.load(Ordering::SeqCst) != gen { return Ok(()); }
    let mut book_now = cache().emails.clone();
    for a in &filed { if !book_now.contains(a) { book_now.push(a.clone()); } }
    cache().emails = book_now.clone();
    // everything the book holds leaves the device's list; what could not be filed stays
    crate::settings::trusted_local_update(|l| { *l = remaining_local(l, &book_now); });
    match failure { Some(e) => Err(e), None => Ok(()) }
}

pub enum Added { Already, Filed, Local, LocalAfter(String) }

/// Trust a sender. With the book in use the card goes to the book (AddressBook/set create when no
/// book exists, ContactCard/set create); where the account cannot keep a book, or the write
/// fails, the address goes to the device's list so the sender is trusted anyway.
pub fn add(c: &Client, entry: &str) -> Result<Added, String> {
    let addr = sender_key(entry);
    if !is_address(&addr) { return Err("That is not an email address.".into()); }
    let local_add = |addr: &str| crate::settings::trusted_local_update(|l| { if !l.contains(&addr.to_string()) { l.push(addr.to_string()); } });
    let _f = FLIGHT.lock().unwrap_or_else(|e| e.into_inner());
    let gen = GEN.load(Ordering::SeqCst);
    let snap = cache();
    if snap.emails.contains(&addr) { return Ok(Added::Already); }
    let (known, unavailable) = (snap.book_ids.first().cloned(), snap.unavailable);
    drop(snap);
    if unavailable || !c.has(CONTACTS) { local_add(&addr); return Ok(Added::Local); }
    let book = match known {
        Some(b) => Some(b),
        None => match resolve(c, true, gen) {
            Ok(b) => b,
            Err(e) => { mark_unavailable(c, gen); local_add(&addr); return Ok(Added::LocalAfter(e)); }
        },
    };
    let Some(book) = book else { local_add(&addr); return Ok(Added::Local) };
    if cache().emails.contains(&addr) { return Ok(Added::Already); }
    let acc = c.account_for(CONTACTS);
    let one = [addr.clone()];
    let res = c.call(file_call(&acc, &book, &one, uid)).and_then(|r| {
        let (ok, bad) = filed_result(&r[0][1], &one);
        if ok.is_empty() { Err(bad.into_iter().next().map(|b| b.1).unwrap_or_default()) } else { Ok(()) }
    });
    match res {
        Ok(()) => { if GEN.load(Ordering::SeqCst) == gen { cache().emails.push(addr); } Ok(Added::Filed) }
        Err(e) => { local_add(&addr); Ok(Added::LocalAfter(e)) }
    }
}

/// Stop trusting an address: the cards of every "Trusted Senders" book change as [`removal_plan`]
/// says (one ContactCard/set per book), then the address leaves the device's list. When the
/// server refuses, nothing is changed here and the error is returned.
pub fn remove(c: &Client, entry: &str) -> Result<(), String> {
    let addr = sender_key(entry);
    let _f = FLIGHT.lock().unwrap_or_else(|e| e.into_inner());
    let gen = GEN.load(Ordering::SeqCst);
    let books = cache().book_ids.clone();
    let acc = c.account_for(CONTACTS);
    if c.has(CONTACTS) {
        for b in &books {
            let ops = removal_plan(&cards_of(c, &acc, b)?, b, &addr);
            if ops.is_empty() { continue; }
            let r = c.call(ops_call(&acc, &ops))?;
            let bad = refused(&r[0][1]);
            if !bad.is_empty() { return Err(bad.join("; ")); }
        }
    }
    if GEN.load(Ordering::SeqCst) == gen { cache().emails.retain(|e| *e != addr); }
    crate::settings::trusted_local_update(|l| l.retain(|e| *e != addr));
    Ok(())
}

// ---------------------------------------------------------------- called from the UI thread

use crate::AppWindow;

fn finished(u: &AppWindow) {
    crate::redecide_remote(u);
    if u.get_show_settings() { u.invoke_settings_search(u.get_settings_query()); }
}

/// Read the book in the background (sign-in, account switch, push, the settings switch turned on).
/// Does nothing while the setting is off (no server traffic then) and, unless `force`, when this
/// account's book was read in the last ten seconds.
pub fn refresh(w: slint::Weak<AppWindow>, c: Arc<Client>, force: bool) {
    if !setting_on(&crate::settings::Settings::load().prefs) { return; }
    {
        let k = cache();
        if !force && k.key == c.state_key() && k.at.is_some_and(|t| t.elapsed() < std::time::Duration::from_secs(10)) { return; }
    }
    std::thread::spawn(move || {
        let res = load(&c, true);
        let _ = w.upgrade_in_event_loop(move |u| {
            if let Err(e) = res { crate::mail::show_toast(&u, &format!("Trusted senders address book: {e}"), false); }
            finished(&u);
        });
    });
}

/// "Trust sender" in the reader (and the settings page's additions): trusted at once for the open
/// message by the caller; this files the address in the background and says how it went.
pub fn trust_sender(w: slint::Weak<AppWindow>, c: Option<Arc<Client>>, entry: String, settings_on: bool) {
    std::thread::spawn(move || {
        let addr = sender_key(&entry);
        let res = match (&c, settings_on) {
            (Some(c), true) => add(c, &entry),
            _ => { crate::settings::trusted_local_update(|l| { if !l.contains(&addr) { l.push(addr.clone()); } }); Ok(Added::Local) }
        };
        let msg = match res {
            Ok(Added::LocalAfter(e)) => Some(format!("Trusted on this computer only: the Trusted Senders address book could not be written ({e}).")),
            Ok(_) => Some(format!("Images from {addr} will load from now on.")),
            Err(e) => Some(e),
        };
        let _ = w.upgrade_in_event_loop(move |u| {
            if let Some(m) = msg { crate::mail::show_toast(&u, &m, false); }
            finished(&u);
        });
    });
}

thread_local! { static PENDING: std::cell::RefCell<Option<String>> = const { std::cell::RefCell::new(None) }; }

/// The settings page's text, kept until "Save changes" (typing writes nothing to the server).
pub fn set_pending(text: &str) { PENDING.with(|p| *p.borrow_mut() = Some(text.to_string())); }

/// "Save changes": trust the added lines and untrust the removed ones, in the background.
pub fn apply_pending(w: slint::Weak<AppWindow>, c: Option<Arc<Client>>) {
    let Some(text) = PENDING.with(|p| p.borrow_mut().take()) else { return };
    let prefs = crate::settings::Settings::load().prefs;
    let on = in_book(&prefs);
    let (add_l, rem_l) = text_changes(&view_list(&prefs), &text);
    if add_l.is_empty() && rem_l.is_empty() { return; }
    std::thread::spawn(move || {
        let mut errors: Vec<String> = vec![];
        for a in &add_l {
            let r = match (&c, on) {
                (Some(c), true) => add(c, a).map(|_| ()),
                _ => { crate::settings::trusted_local_update(|l| { if !l.contains(a) { l.push(a.clone()); } }); Ok(()) }
            };
            if let Err(e) = r { errors.push(format!("{a}: {e}")); }
        }
        for a in &rem_l {
            let r = match (&c, on) {
                (Some(c), true) => remove(c, a),
                _ => { crate::settings::trusted_local_update(|l| l.retain(|x| x != a)); Ok(()) }
            };
            if let Err(e) = r { errors.push(format!("{a}: {e}")); }
        }
        let msg = if errors.is_empty() { "Trusted senders saved".to_string() } else { format!("Some changes were not saved: {}", errors.join("; ")) };
        let _ = w.upgrade_in_event_loop(move |u| { crate::mail::show_toast(&u, &msg, false); finished(&u); });
    });
}

/// The settings switch changed (called after the new value is stored). Off: what the book
/// holds stays trusted on this computer, as a copy on the device's list (the book is not touched).
/// On: read the book and file the device's list.
pub fn toggled(on: bool, w: slint::Weak<AppWindow>, c: Option<Arc<Client>>) {
    if on {
        if let Some(c) = c { refresh(w, c, true); }
    } else {
        let (loaded, unavailable, emails) = { let k = cache(); (k.loaded, k.unavailable, k.emails.clone()) };
        if loaded && !unavailable {
            crate::settings::trusted_local_update(|l| for e in &emails { if !l.contains(e) { l.push(e.clone()); } });
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn s(v: &[&str]) -> Vec<String> { v.iter().map(|x| x.to_string()).collect() }
    fn prefs(v: Value) -> Map<String, Value> { v.as_object().unwrap().clone() }

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
        assert_eq!(remaining_local(&local, &s(&["ann@example.test", "bob@example.test"])), s(&["junk", "Cy <cy@example.test>"]));
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
    fn the_cache_is_forgotten_on_clear_and_decides_trust() {
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
