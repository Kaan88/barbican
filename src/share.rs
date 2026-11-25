//! Sharing a mail folder, calendar or address book with other users of the server (Bulwark
//! components/settings/share-collection-dialog.tsx, components/layout/mailbox-share-dialog.tsx).
//! The rights each preset grants are Bulwark's lib/jmap/share-rights.ts; a change is one
//! `shareWith/<principal>` patch (RFC 9670 §3, mail:share for folders), null to remove it.
//! Files keep their own share flyout (src/files.rs).
use crate::jmap::Client;
use crate::{AppWindow, ShareX};
use serde_json::{json, Map, Value};
use slint::{ComponentHandle, ModelRc, SharedString, VecModel};
use std::cell::RefCell;
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

pub const MAIL_SHARE: &str = "urn:ietf:params:jmap:mail:share";

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Kind { Mailbox, Calendar, AddressBook }

impl Kind {
    fn object(self) -> &'static str {
        match self { Kind::Mailbox => "Mailbox", Kind::Calendar => "Calendar", Kind::AddressBook => "AddressBook" }
    }
    /// What Bulwark sends as `using` for the type (client.ts getMailboxShareWith, calendarUsing, contactUsing).
    fn using(self) -> Vec<&'static str> {
        match self {
            Kind::Mailbox => vec!["urn:ietf:params:jmap:core", "urn:ietf:params:jmap:mail", MAIL_SHARE],
            Kind::Calendar => vec!["urn:ietf:params:jmap:core", "urn:ietf:params:jmap:calendars"],
            Kind::AddressBook => vec!["urn:ietf:params:jmap:core", "urn:ietf:params:jmap:contacts"],
        }
    }
    /// The presets the dropdown offers (share-collection-dialog.tsx:79-83; the admin's
    /// sharingRights restriction has no counterpart here, so the write presets are always there).
    pub fn presets(self) -> Vec<Preset> {
        let mut v = if self == Kind::Calendar { vec![Preset::FreeBusy] } else { vec![] };
        v.extend([Preset::Read, Preset::ReadWrite, Preset::Manager]);
        v
    }
}

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Preset { FreeBusy, Read, ReadWrite, Manager, Custom }

impl Preset {
    /// Bulwark's sharing.preset.* labels.
    pub fn label(self) -> &'static str {
        match self {
            Preset::FreeBusy => "Free/busy only",
            Preset::Read => "Read only",
            Preset::ReadWrite => "Read & write",
            Preset::Manager => "Manager",
            Preset::Custom => "Custom",
        }
    }
}

fn rights(pairs: &[(&str, bool)]) -> Value {
    Value::Object(pairs.iter().map(|(k, v)| (k.to_string(), Value::Bool(*v))).collect())
}

/// The rights a preset grants (share-rights.ts CALENDAR_PRESETS, ADDRESS_BOOK_PRESETS,
/// MAILBOX_PRESETS); None for Custom, and for free/busy outside calendars.
pub fn preset_rights(kind: Kind, p: Preset) -> Option<Value> {
    let (r, w, m) = (p == Preset::Read || p == Preset::ReadWrite || p == Preset::Manager, p == Preset::ReadWrite || p == Preset::Manager, p == Preset::Manager);
    match (kind, p) {
        (_, Preset::Custom) => None,
        (Kind::Calendar, _) => Some(rights(&[
            ("mayReadFreeBusy", true), ("mayReadItems", r), ("mayWriteAll", w), ("mayWriteOwn", w),
            ("mayUpdatePrivate", w), ("mayRSVP", w), ("mayShare", m), ("mayDelete", m),
        ])),
        (_, Preset::FreeBusy) => None,
        (Kind::AddressBook, _) => Some(rights(&[("mayRead", true), ("mayWrite", w), ("mayShare", m), ("mayDelete", m)])),
        (Kind::Mailbox, _) => Some(rights(&[
            ("mayReadItems", true), ("mayAddItems", w), ("mayRemoveItems", w), ("maySetSeen", true),
            ("maySetKeywords", w), ("mayCreateChild", m), ("mayRename", m), ("mayDelete", m),
            ("maySubmit", m), ("mayShare", m),
        ])),
    }
}

/// The preset a stored grant amounts to, else Custom (share-rights.ts detectPreset): compared on
/// the rights the preset names only (Stalwart 1.0 adds maySetMetadata), and a folder's keyword
/// right may follow its seen right (1.0 keeps the two as one).
pub fn detect_preset(kind: Kind, have: &Value) -> Preset {
    let all = if kind == Kind::Calendar { vec![Preset::FreeBusy, Preset::Read, Preset::ReadWrite, Preset::Manager] } else { vec![Preset::Read, Preset::ReadWrite, Preset::Manager] };
    for p in all {
        let Some(Value::Object(want)) = preset_rights(kind, p) else { continue };
        let ok = want.iter().all(|(k, w)| {
            let got = have[k].as_bool().unwrap_or(false);
            Some(got) == w.as_bool() || (kind == Kind::Mailbox && k == "maySetKeywords" && got && have["maySetSeen"] == true)
        });
        if ok { return p; }
    }
    Preset::Custom
}

/// `shareWith/<principal>` as a JSON pointer path (RFC 6901 escapes, as files.rs does).
fn share_path(principal: &str) -> String { format!("shareWith/{}", principal.replace('~', "~0").replace('/', "~1")) }

/// The method call that grants `rights` to `principal`, or takes them away (None).
pub fn set_call(kind: Kind, account: &str, id: &str, principal: &str, rights: Option<Value>) -> Value {
    let mut patch = Map::new();
    patch.insert(share_path(principal), rights.unwrap_or(Value::Null));
    json!([[format!("{}/set", kind.object()), {"accountId": account, "update": { id: Value::Object(patch) }}, "0"]])
}

/// The call that reads one collection's current shares: Stalwart leaves shareWith out of the
/// default property list (mailbox-share-dialog.tsx:20, client.ts:574).
pub fn get_call(kind: Kind, account: &str, id: &str) -> Value {
    json!([[format!("{}/get", kind.object()), {"accountId": account, "ids": [id], "properties": ["id", "shareWith", "myRights"]}, "0"]])
}

/// The set answer: Ok when the server confirmed the update (client.ts setMailboxShare).
pub fn parse_set(kind: Kind, r: &[Value], id: &str) -> Result<(), String> {
    let Some(first) = r.first() else { return Err("The server did not answer".into()) };
    if first[0] != format!("{}/set", kind.object()) { return Err(crate::jmap::method_error(&first[1])); }
    if let Some(e) = first[1]["notUpdated"].get(id) { return Err(crate::jmap::set_error(e)); }
    if !first[1]["updated"].as_object().is_some_and(|m| m.contains_key(id)) { return Err("The server did not confirm the share update".into()); }
    Ok(())
}

/// The get answer: the collection's shares, principal by principal, in the server's order.
pub fn parse_get(kind: Kind, r: &[Value], id: &str) -> Result<Vec<(String, Value)>, String> {
    let Some(first) = r.first() else { return Err("The server did not answer".into()) };
    if first[0] != format!("{}/get", kind.object()) { return Err(crate::jmap::method_error(&first[1])); }
    let item = first[1]["list"].as_array().and_then(|l| l.iter().find(|x| x["id"] == id))
        .ok_or_else(|| match kind { Kind::Mailbox => "Folder not found", Kind::Calendar => "Calendar not found", Kind::AddressBook => "Address book not found" }.to_string())?;
    Ok(item["shareWith"].as_object().map(|m| m.iter().map(|(k, v)| (k.clone(), v.clone())).collect()).unwrap_or_default())
}

// ---------------- where the menus offer it ----------------

/// A folder of one's own may be shared when the account has mail:share; a folder someone
/// shared with us only with their mayShare (mailbox-context-menu.tsx:328-330).
pub fn mailbox_can_share(supported: bool, shared_with_me: bool, my_rights: &Value) -> bool {
    supported && (!shared_with_me || my_rights["mayShare"] == true)
}

/// A calendar or address book needs the mayShare right and must be one's own
/// (calendar-sidebar-panel.tsx:475, contacts-sidebar.tsx:623, calendar-management-settings.tsx:655).
pub fn collection_can_share(item: &Value, subscription: bool) -> bool {
    item["myRights"]["mayShare"] == true && item["_shared"] != true && !subscription
}

/// The folder menu's Share…: the folder, when the account may share it; None hides the item.
pub fn folder_target(c: &Client, id: &str, name: &str) -> Option<Target> {
    let boxes = crate::mail::mailbox_list();
    let m = boxes.iter().find(|m| m["id"] == id)?;
    // the folder list is the user's own account: never a folder shared with them
    mailbox_can_share(c.account_has(MAIL_SHARE, &c.account), false, &m["myRights"])
        .then(|| Target { kind: Kind::Mailbox, account: c.account.clone(), id: id.to_string(), name: name.to_string() })
}

/// A calendar the user may share, by its id in the calendar list.
pub fn calendar_target(id: &str) -> Option<Target> {
    let cal = crate::calendar::calendar(id)?;
    if !collection_can_share(&cal, crate::cal_extra::is_subscription(id)) { return None; }
    Some(Target {
        kind: Kind::Calendar,
        account: cal["_acc"].as_str().unwrap_or("").to_string(),
        id: cal["_rid"].as_str().or(cal["id"].as_str()).unwrap_or("").to_string(),
        name: cal["name"].as_str().unwrap_or("Calendar").to_string(),
    }).filter(|t| !t.account.is_empty())
}

/// An address book the user may share.
pub fn book_target(c: &Client, id: &str) -> Option<Target> {
    let b = crate::contacts::book(id)?;
    collection_can_share(&b, false).then(|| Target {
        kind: Kind::AddressBook, account: c.account_for("urn:ietf:params:jmap:contacts"), id: id.to_string(),
        name: b["name"].as_str().unwrap_or("Address book").to_string(),
    })
}

// ---------------- the dialog ----------------

#[derive(Clone)]
pub struct Target {
    pub kind: Kind,
    pub account: String,
    pub id: String,
    pub name: String,
}

const DIR_TTL: Duration = Duration::from_secs(5 * 60);

/// One principal listing per login, kept a few minutes (lib/jmap/principal-directory.ts).
static DIRECTORY: Mutex<Option<(String, Instant, Vec<Value>, bool)>> = Mutex::new(None);

#[derive(Default)]
struct State {
    target: Option<Target>,
    client: Option<Arc<Client>>,
    /// Bumped on every open and close: an answer for an earlier dialog is dropped.
    gen: u64,
    shares: Vec<(String, Value)>,
    principals: Vec<Value>,
    dir_loading: bool,
    dir_note: String,
    saving: Option<String>,
}

thread_local! {
    static ST: RefCell<State> = RefCell::new(State::default());
}

fn strings(v: Vec<String>) -> ModelRc<SharedString> {
    ModelRc::new(VecModel::from(v.into_iter().map(SharedString::from).collect::<Vec<_>>()))
}

/// Principals that may be added: not oneself, not already shared with, matching the search
/// in name, address or description (share-collection-dialog.tsx:74-77, 129-137).
pub fn candidates<'a>(all: &'a [Value], own: &str, shared: &[(String, Value)], query: &str) -> Vec<&'a Value> {
    let q = query.trim().to_lowercase();
    let has = |p: &Value, k: &str| p[k].as_str().is_some_and(|s| s.to_lowercase().contains(&q));
    all.iter()
        .filter(|p| p["id"] != own && !shared.iter().any(|(id, _)| p["id"] == id.as_str()))
        .filter(|p| q.is_empty() || has(p, "name") || has(p, "email") || has(p, "description"))
        .collect()
}

/// How a sharee is named: their name or address, "Unknown user" (and the id below) when the
/// directory had nobody by that id (share-collection-dialog.tsx:169-193).
pub fn sharee_label(p: Option<&Value>, id: &str, dir_loading: bool) -> (String, String) {
    match p {
        Some(p) => {
            let name = p["name"].as_str().filter(|s| !s.is_empty()).or(p["email"].as_str()).unwrap_or(id).to_string();
            (name, p["description"].as_str().unwrap_or("").to_string())
        }
        None if dir_loading => (id.to_string(), String::new()),
        None => ("Unknown user".into(), id.to_string()),
    }
}

fn initial(s: &str) -> String { s.chars().find(|c| c.is_alphanumeric()).map(|c| c.to_uppercase().to_string()).unwrap_or_else(|| "?".into()) }

fn render(u: &AppWindow) {
    let x = u.global::<ShareX>();
    ST.with(|s| {
        let s = s.borrow();
        let Some(t) = &s.target else { return };
        let offered = t.kind.presets();
        let rows: Vec<crate::ShareRow> = s.shares.iter().map(|(pid, r)| {
            let p = s.principals.iter().find(|p| p["id"] == pid.as_str());
            let (name, sub) = sharee_label(p, pid, s.dir_loading);
            let now = detect_preset(t.kind, r);
            let mut opts: Vec<String> = offered.iter().map(|p| p.label().to_string()).collect();
            // a grant the dropdown does not offer stays named, but picking it changes nothing
            let index = offered.iter().position(|p| *p == now).unwrap_or_else(|| { opts.push(now.label().into()); opts.len() - 1 });
            crate::ShareRow {
                id: pid.as_str().into(), initial: initial(&name).into(), name: name.into(), sub: sub.into(),
                options: strings(opts), index: index as i32, saving: s.saving.as_deref() == Some(pid.as_str()),
            }
        }).collect();
        x.set_rows(ModelRc::new(VecModel::from(rows)));
        let own = s.client.as_ref().map(|c| own_id(c, t.kind)).unwrap_or_default();
        let q = x.get_search().to_string();
        let people: Vec<crate::SharePerson> = candidates(&s.principals, &own, &s.shares, &q).into_iter().map(|p| {
            let name = p["name"].as_str().unwrap_or("").to_string();
            let email = p["email"].as_str().unwrap_or("").to_string();
            let id = p["id"].as_str().unwrap_or("");
            crate::SharePerson {
                id: id.into(), initial: initial(if name.is_empty() { &email } else { &name }).into(),
                email: if email == name { SharedString::new() } else { email.into() }, name: name.into(),
                group: p["type"] == "group", saving: s.saving.as_deref() == Some(id),
            }
        }).collect();
        let empty = if s.dir_loading || !people.is_empty() { "" } else if !q.trim().is_empty() { "No matches." } else if s.dir_note.is_empty() { "No other users or groups found." } else { "" };
        x.set_people(ModelRc::new(VecModel::from(people)));
        x.set_people_loading(s.dir_loading);
        x.set_dir_note(s.dir_note.as_str().into());
        x.set_empty_note(empty.into());
    });
}

/// The principal id that is the user (Bulwark ownAccountId: the collection's own account).
fn own_id(c: &Client, kind: Kind) -> String {
    c.account_for(match kind { Kind::Mailbox => "urn:ietf:params:jmap:mail", Kind::Calendar => "urn:ietf:params:jmap:calendars", Kind::AddressBook => "urn:ietf:params:jmap:contacts" })
}

fn fetch(c: &Client, t: &Target) -> Result<Vec<(String, Value)>, String> {
    let r = c.request_using(&t.kind.using(), get_call(t.kind, &t.account, &t.id))?;
    parse_get(t.kind, &r, &t.id)
}

fn apply(c: &Client, t: &Target, principal: &str, rights: Option<Value>) -> Result<(), String> {
    let r = c.request_using(&t.kind.using(), set_call(t.kind, &t.account, &t.id, principal, rights))?;
    parse_set(t.kind, &r, &t.id)
}

/// The directory (contacts_x::fetch_directory), shared while fresh; a failed listing is not kept.
fn directory(c: &Client) -> Result<(Vec<Value>, bool), String> {
    let key = format!("{}|{}", c.username, c.account);
    if let Some((k, at, list, restricted)) = DIRECTORY.lock().unwrap_or_else(|e| e.into_inner()).as_ref() {
        if *k == key && at.elapsed() < DIR_TTL { return Ok((list.clone(), *restricted)); }
    }
    let (list, restricted) = crate::contacts_x::fetch_directory(c)?;
    *DIRECTORY.lock().unwrap_or_else(|e| e.into_inner()) = Some((key, Instant::now(), list.clone(), restricted));
    Ok((list, restricted))
}

/// Open the dialog: the shares are read first (a spinner until then), the people to add next.
pub fn open(u: &AppWindow, c: Arc<Client>, t: Target) {
    let gen = ST.with(|s| {
        let mut s = s.borrow_mut();
        s.gen += 1;
        *s = State { gen: s.gen, target: Some(t.clone()), client: Some(c.clone()), dir_loading: true, ..Default::default() };
        s.gen
    });
    let x = u.global::<ShareX>();
    x.set_title(format!("Share \u{201c}{}\u{201d}", t.name).into());
    x.set_search("".into());
    x.set_adding(false);
    x.set_loading(true);
    x.set_open(true);
    render(u);
    let (w, c1, t1) = (u.as_weak(), c.clone(), t.clone());
    std::thread::spawn(move || {
        let res = fetch(&c1, &t1);
        let _ = w.upgrade_in_event_loop(move |u| {
            if ST.with(|s| s.borrow().gen) != gen { return; }
            match res {
                Ok(shares) => {
                    ST.with(|s| s.borrow_mut().shares = shares);
                    u.global::<ShareX>().set_loading(false);
                    render(&u);
                }
                Err(e) => { close(&u); crate::mail::show_toast(&u, &e, false); }
            }
        });
    });
    let w = u.as_weak();
    std::thread::spawn(move || {
        let res = directory(&c);
        let _ = w.upgrade_in_event_loop(move |u| {
            ST.with(|s| {
                let mut s = s.borrow_mut();
                if s.gen != gen { return; }
                s.dir_loading = false;
                match res {
                    Ok((list, restricted)) => {
                        s.principals = list;
                        if restricted { s.dir_note = "Your server doesn't allow browsing its user directory, so only your groups and people who have shared with you are listed. An administrator can turn on directory queries in Stalwart's sharing settings.".into(); }
                    }
                    Err(_) => s.dir_note = "Couldn't load the list of users and groups.".into(),
                }
            });
            render(&u);
        });
    });
}

pub fn close(u: &AppWindow) {
    ST.with(|s| { let mut s = s.borrow_mut(); s.gen += 1; s.target = None; s.client = None; s.shares.clear(); s.saving = None; });
    let x = u.global::<ShareX>();
    x.set_open(false);
    x.set_loading(false);
    x.set_rows(ModelRc::new(VecModel::from(Vec::<crate::ShareRow>::new())));
    x.set_people(ModelRc::new(VecModel::from(Vec::<crate::SharePerson>::new())));
}

/// Signed out: nothing of the last account stays in the dialog or the directory cache.
pub fn reset(u: &AppWindow) {
    close(u);
    *DIRECTORY.lock().unwrap_or_else(|e| e.into_inner()) = None;
}

/// Grant, change or remove one principal's rights, then read the shares back from the server
/// (mailbox-share-dialog.tsx:80-84), with Bulwark's toasts.
fn change(u: &AppWindow, principal: String, rights: Option<Value>, done: &'static str, then_close_add: bool) {
    let Some((t, c, gen)) = ST.with(|s| {
        let mut s = s.borrow_mut();
        if s.saving.is_some() { return None; }
        let r = Some((s.target.clone()?, s.client.clone()?, s.gen));
        s.saving = Some(principal.clone());
        r
    }) else { return };
    render(u);
    let w = u.as_weak();
    std::thread::spawn(move || {
        let res = apply(&c, &t, &principal, rights).and_then(|()| fetch(&c, &t));
        let _ = w.upgrade_in_event_loop(move |u| {
            if ST.with(|s| s.borrow().gen) != gen { return; }
            let ok = res.is_ok();
            ST.with(|s| {
                let mut s = s.borrow_mut();
                s.saving = None;
                if let Ok(shares) = &res { s.shares = shares.clone(); }
            });
            if ok && then_close_add { let x = u.global::<ShareX>(); x.set_adding(false); x.set_search("".into()); }
            render(&u);
            crate::mail::show_toast(&u, &res.map(|_| done.to_string()).unwrap_or_else(|e| if e.is_empty() { "Failed to update sharing".into() } else { e }), false);
        });
    });
}

fn set_preset(u: &AppWindow, principal: &str, index: i32) {
    let Some(kind) = ST.with(|s| s.borrow().target.as_ref().map(|t| t.kind)) else { return };
    let offered = kind.presets();
    // the extra "Custom" (or no longer offered) entry is read-only (share-collection-dialog.tsx:86-87)
    let Some(rights) = offered.get(index.max(0) as usize).and_then(|p| preset_rights(kind, *p)).filter(|_| index >= 0) else { render(u); return };
    change(u, principal.to_string(), Some(rights), "Access updated", false);
}

fn add(u: &AppWindow, principal: &str) {
    let Some(kind) = ST.with(|s| s.borrow().target.as_ref().map(|t| t.kind)) else { return };
    change(u, principal.to_string(), preset_rights(kind, Preset::Read), "Access granted", true);
}

pub fn wire(ui: &AppWindow) {
    let x = ui.global::<ShareX>();
    let w = ui.as_weak();
    x.on_close(move || { if let Some(u) = w.upgrade() { close(&u); } });
    let w = ui.as_weak();
    x.on_set_preset(move |p, i| { if let Some(u) = w.upgrade() { set_preset(&u, &p, i); } });
    let w = ui.as_weak();
    x.on_remove(move |p| { if let Some(u) = w.upgrade() { change(&u, p.to_string(), None, "Access removed", false); } });
    let w = ui.as_weak();
    x.on_add(move |p| { if let Some(u) = w.upgrade() { add(&u, &p); } });
    let w = ui.as_weak();
    x.on_search_edited(move |q| { if let Some(u) = w.upgrade() { u.global::<ShareX>().set_search(q); render(&u); } });
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn presets_match_bulwark() {
        let cal = |p| preset_rights(Kind::Calendar, p).unwrap();
        assert_eq!(cal(Preset::FreeBusy), json!({"mayReadFreeBusy": true, "mayReadItems": false, "mayWriteAll": false, "mayWriteOwn": false, "mayUpdatePrivate": false, "mayRSVP": false, "mayShare": false, "mayDelete": false}));
        assert_eq!(cal(Preset::Read), json!({"mayReadFreeBusy": true, "mayReadItems": true, "mayWriteAll": false, "mayWriteOwn": false, "mayUpdatePrivate": false, "mayRSVP": false, "mayShare": false, "mayDelete": false}));
        assert_eq!(cal(Preset::ReadWrite), json!({"mayReadFreeBusy": true, "mayReadItems": true, "mayWriteAll": true, "mayWriteOwn": true, "mayUpdatePrivate": true, "mayRSVP": true, "mayShare": false, "mayDelete": false}));
        assert_eq!(cal(Preset::Manager), json!({"mayReadFreeBusy": true, "mayReadItems": true, "mayWriteAll": true, "mayWriteOwn": true, "mayUpdatePrivate": true, "mayRSVP": true, "mayShare": true, "mayDelete": true}));
        let book = |p| preset_rights(Kind::AddressBook, p).unwrap();
        assert_eq!(book(Preset::Read), json!({"mayRead": true, "mayWrite": false, "mayShare": false, "mayDelete": false}));
        assert_eq!(book(Preset::ReadWrite), json!({"mayRead": true, "mayWrite": true, "mayShare": false, "mayDelete": false}));
        assert_eq!(book(Preset::Manager), json!({"mayRead": true, "mayWrite": true, "mayShare": true, "mayDelete": true}));
        let mb = |p| preset_rights(Kind::Mailbox, p).unwrap();
        assert_eq!(mb(Preset::Read), json!({"mayReadItems": true, "mayAddItems": false, "mayRemoveItems": false, "maySetSeen": true, "maySetKeywords": false, "mayCreateChild": false, "mayRename": false, "mayDelete": false, "maySubmit": false, "mayShare": false}));
        assert_eq!(mb(Preset::ReadWrite), json!({"mayReadItems": true, "mayAddItems": true, "mayRemoveItems": true, "maySetSeen": true, "maySetKeywords": true, "mayCreateChild": false, "mayRename": false, "mayDelete": false, "maySubmit": false, "mayShare": false}));
        assert_eq!(mb(Preset::Manager), json!({"mayReadItems": true, "mayAddItems": true, "mayRemoveItems": true, "maySetSeen": true, "maySetKeywords": true, "mayCreateChild": true, "mayRename": true, "mayDelete": true, "maySubmit": true, "mayShare": true}));
        // free/busy is a calendar preset only; custom is never sent
        assert_eq!(preset_rights(Kind::Mailbox, Preset::FreeBusy), None);
        assert_eq!(preset_rights(Kind::AddressBook, Preset::FreeBusy), None);
        assert_eq!(preset_rights(Kind::Calendar, Preset::Custom), None);
        assert_eq!(Kind::Calendar.presets(), vec![Preset::FreeBusy, Preset::Read, Preset::ReadWrite, Preset::Manager]);
        assert_eq!(Kind::Mailbox.presets(), vec![Preset::Read, Preset::ReadWrite, Preset::Manager]);
        assert_eq!(Kind::AddressBook.presets(), vec![Preset::Read, Preset::ReadWrite, Preset::Manager]);
    }

    #[test]
    fn stored_grants_name_their_preset() {
        for kind in [Kind::Mailbox, Kind::Calendar, Kind::AddressBook] {
            for p in kind.presets() { assert_eq!(detect_preset(kind, &preset_rights(kind, p).unwrap()), p, "{kind:?} {p:?}"); }
        }
        // Stalwart 1.0 adds maySetMetadata, and keeps seen and keywords as one folder right
        assert_eq!(detect_preset(Kind::AddressBook, &json!({"mayRead": true, "mayWrite": false, "mayShare": false, "mayDelete": false, "maySetMetadata": false})), Preset::Read);
        let mut read = preset_rights(Kind::Mailbox, Preset::Read).unwrap();
        read["maySetKeywords"] = json!(true);
        assert_eq!(detect_preset(Kind::Mailbox, &read), Preset::Read);
        // a missing right counts as false; anything else is custom
        assert_eq!(detect_preset(Kind::AddressBook, &json!({"mayRead": true})), Preset::Read);
        assert_eq!(detect_preset(Kind::AddressBook, &json!({"mayRead": true, "mayDelete": true})), Preset::Custom);
        assert_eq!(detect_preset(Kind::Calendar, &json!({"mayReadFreeBusy": true, "mayReadItems": true, "mayWriteOwn": true})), Preset::Custom);
        assert_eq!(detect_preset(Kind::Calendar, &json!({"mayReadFreeBusy": true})), Preset::FreeBusy);
    }

    #[test]
    fn request_shapes() {
        let r = preset_rights(Kind::Calendar, Preset::Read);
        assert_eq!(set_call(Kind::Calendar, "A1", "c1", "p7", r.clone()),
            json!([["Calendar/set", {"accountId": "A1", "update": {"c1": {"shareWith/p7": r.unwrap()}}}, "0"]]));
        assert_eq!(set_call(Kind::Mailbox, "A1", "m1", "p7", None),
            json!([["Mailbox/set", {"accountId": "A1", "update": {"m1": {"shareWith/p7": null}}}, "0"]]));
        assert_eq!(set_call(Kind::AddressBook, "A1", "b1", "a/b~c", None)[0][1]["update"]["b1"], json!({"shareWith/a~1b~0c": null}));
        assert_eq!(get_call(Kind::AddressBook, "A1", "b1"),
            json!([["AddressBook/get", {"accountId": "A1", "ids": ["b1"], "properties": ["id", "shareWith", "myRights"]}, "0"]]));
        assert!(Kind::Mailbox.using().contains(&MAIL_SHARE));
        assert!(Kind::Calendar.using().contains(&"urn:ietf:params:jmap:calendars"));
        assert!(Kind::AddressBook.using().contains(&"urn:ietf:params:jmap:contacts"));
    }

    #[test]
    fn answers_and_errors() {
        let k = Kind::Mailbox;
        assert_eq!(parse_set(k, &[json!(["Mailbox/set", {"updated": {"m1": null}}, "0"])], "m1"), Ok(()));
        assert_eq!(parse_set(k, &[json!(["Mailbox/set", {"notUpdated": {"m1": {"type": "forbidden"}}}, "0"])], "m1"), Err("You do not have permission to do that.".into()));
        assert_eq!(parse_set(k, &[json!(["Mailbox/set", {"notUpdated": {"m1": {"type": "notFound"}}}, "0"])], "m1"), Err("It no longer exists.".into()));
        assert_eq!(parse_set(k, &[json!(["Mailbox/set", {"updated": {}}, "0"])], "m1"), Err("The server did not confirm the share update".into()));
        assert_eq!(parse_set(k, &[json!(["error", {"type": "forbidden"}, "0"])], "m1"), Err("You do not have permission to do that.".into()));
        assert!(parse_set(Kind::Calendar, &[json!(["error", {"type": "unknownCapability"}, "0"])], "c").unwrap_err().contains("does not support"));
        let got = parse_get(Kind::Calendar, &[json!(["Calendar/get", {"list": [{"id": "c1", "shareWith": {"p1": {"mayReadFreeBusy": true}}}]}, "0"])], "c1").unwrap();
        assert_eq!(got, vec![("p1".to_string(), json!({"mayReadFreeBusy": true}))]);
        assert_eq!(parse_get(Kind::AddressBook, &[json!(["AddressBook/get", {"list": [{"id": "b1", "shareWith": null}]}, "0"])], "b1").unwrap(), vec![]);
        assert_eq!(parse_get(k, &[json!(["Mailbox/get", {"list": [], "notFound": ["m1"]}, "0"])], "m1"), Err("Folder not found".into()));
        assert_eq!(parse_get(k, &[json!(["error", {"type": "forbidden"}, "0"])], "m1"), Err("You do not have permission to do that.".into()));
    }

    #[test]
    fn gating() {
        assert!(mailbox_can_share(true, false, &json!({})));
        assert!(!mailbox_can_share(false, false, &json!({"mayShare": true})));
        assert!(!mailbox_can_share(true, true, &json!({"mayShare": false})));
        assert!(mailbox_can_share(true, true, &json!({"mayShare": true})));
        assert!(collection_can_share(&json!({"myRights": {"mayShare": true}}), false));
        assert!(!collection_can_share(&json!({"myRights": {"mayShare": false}}), false));
        assert!(!collection_can_share(&json!({}), false));
        assert!(!collection_can_share(&json!({"myRights": {"mayShare": true}, "_shared": true}), false));
        assert!(!collection_can_share(&json!({"myRights": {"mayShare": true}}), true));
    }

    #[test]
    fn people_to_add() {
        let dir = vec![
            json!({"id": "me", "name": "Ann", "email": "ann@example.test"}),
            json!({"id": "b", "name": "Bob", "email": "bob@example.test", "type": "individual"}),
            json!({"id": "s", "name": "Sales", "email": "sales@example.test", "type": "group", "description": "Team"}),
            json!({"id": "c", "name": "Cy", "email": "cy@example.test"}),
        ];
        let shared = vec![("c".to_string(), json!({}))];
        let ids = |q| candidates(&dir, "me", &shared, q).iter().map(|p| p["id"].as_str().unwrap().to_string()).collect::<Vec<_>>();
        assert_eq!(ids(""), vec!["b", "s"]);
        assert_eq!(ids(" BOB "), vec!["b"]);
        assert_eq!(ids("team"), vec!["s"]);
        assert_eq!(ids("example.test"), vec!["b", "s"]);
        assert!(ids("zed").is_empty());
        assert_eq!(sharee_label(Some(&dir[1]), "b", false), ("Bob".into(), "".into()));
        assert_eq!(sharee_label(Some(&json!({"id": "x", "email": "x@example.test"})), "x", false).0, "x@example.test");
        assert_eq!(sharee_label(Some(&dir[2]), "s", false).1, "Team");
        assert_eq!(sharee_label(None, "p9", false), ("Unknown user".into(), "p9".into()));
        assert_eq!(sharee_label(None, "p9", true), ("p9".into(), "".into()));
    }

    #[test]
    fn round_trip_against_the_fake_server() {
        let sent: Arc<Mutex<Vec<Value>>> = Arc::default();
        let log = sent.clone();
        let c = Client::fake("https://mail.example.test", "A1", &["urn:ietf:params:jmap:core", "urn:ietf:params:jmap:calendars"], move |calls| {
            log.lock().unwrap_or_else(|e| e.into_inner()).push(calls.clone());
            let m = calls[0][0].as_str().unwrap_or("");
            Ok(match m {
                "Calendar/set" if calls[0][1]["update"]["c1"].as_object().is_some_and(|p| p.contains_key("shareWith/denied")) =>
                    vec![json!(["Calendar/set", {"notUpdated": {"c1": {"type": "forbidden"}}}, "0"])],
                "Calendar/set" => vec![json!(["Calendar/set", {"updated": {"c1": null}}, "0"])],
                "Calendar/get" => vec![json!(["Calendar/get", {"list": [{"id": "c1", "shareWith": {"p1": preset_rights(Kind::Calendar, Preset::Read).unwrap()}}]}, "0"])],
                _ => vec![json!(["error", {"type": "unknownMethod"}, "0"])],
            })
        });
        let t = Target { kind: Kind::Calendar, account: "A1".into(), id: "c1".into(), name: "Work".into() };
        assert_eq!(apply(&c, &t, "p1", preset_rights(Kind::Calendar, Preset::Read)), Ok(()));
        assert_eq!(apply(&c, &t, "denied", None), Err("You do not have permission to do that.".into()));
        let shares = fetch(&c, &t).unwrap();
        assert_eq!(detect_preset(Kind::Calendar, &shares[0].1), Preset::Read);
        let log = sent.lock().unwrap_or_else(|e| e.into_inner());
        assert_eq!(log[0][0][1]["update"]["c1"]["shareWith/p1"]["mayReadItems"], json!(true));
        assert_eq!(log[1][0][1]["update"]["c1"]["shareWith/denied"], Value::Null);
        assert_eq!(log[2][0][0], "Calendar/get");
    }
}
