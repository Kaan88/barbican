//! Contacts app: JMAP for Contacts (AddressBook / ContactCard, JSContact RFC 9553).
//! List grouped by letter, search, detail, create / edit / delete, write an email.
use crate::jmap::Client;
use crate::{AppWindow, ContactRow, NavRow};
use serde_json::{json, Value};
use slint::{ComponentHandle, ModelRc, VecModel};
use std::cell::RefCell;
use std::sync::Arc;

#[derive(Default)]
struct State {
    cards: Vec<Value>,
    groups: Vec<Value>,
    books: Vec<Value>,
    query: String,
    /// Sidebar selection (Bulwark ContactCategory): "" all, "book:ID", "group:ID", "kw:NAME", "uncat".
    filter: String,
}

thread_local! {
    static ST: RefCell<State> = RefCell::new(State::default());
    /// The client the contacts were loaded with (photos, directory).
    static CLIENT: RefCell<Option<Arc<Client>>> = const { RefCell::new(None) };
    static THEN: RefCell<Vec<Box<dyn FnOnce(&AppWindow)>>> = RefCell::new(vec![]);
}

pub fn cards() -> Vec<Value> { ST.with(|s| s.borrow().cards.clone()) }
pub fn groups() -> Vec<Value> { ST.with(|s| s.borrow().groups.clone()) }
pub fn books() -> Vec<Value> { ST.with(|s| s.borrow().books.clone()) }
pub fn display_name(c: &Value) -> String { full_name(c) }
pub fn current_filter() -> String { ST.with(|s| s.borrow().filter.clone()) }
pub fn client() -> Option<Arc<Client>> { CLIENT.with(|c| c.borrow().clone()) }

/// The cards the list shows (filter and search applied).
pub fn shown_cards() -> Vec<Value> {
    let dupes = crate::contacts_x::dupe_ids();
    ST.with(|s| {
        let s = s.borrow();
        let q = s.query.to_lowercase();
        s.cards.iter().filter(|c| in_filter_x(c, &s.filter, &s.groups, &dupes)).filter(|c| {
            q.is_empty() || full_name(c).to_lowercase().contains(&q) || all(c, "emails", "address").iter().any(|e| e.to_lowercase().contains(&q))
        }).cloned().collect()
    })
}

/// The default address book: the loaded list's, else the server's.
pub fn default_book_for(c: &Client) -> Option<String> {
    default_book().or_else(|| {
        let acc = c.account_for("urn:ietf:params:jmap:contacts");
        c.call(json!([["AddressBook/get", {"accountId": acc, "ids": null, "properties": ["id", "isDefault"]}, "g"]])).ok().and_then(|r| {
            let l = r[0][1]["list"].as_array().cloned().unwrap_or_default();
            l.iter().find(|b| b["isDefault"] == true).or(l.first()).and_then(|b| b["id"].as_str().map(String::from))
        })
    })
}

/// Reload, then run `then` once the new cards are in.
pub fn reload_then(u: &AppWindow, c: Arc<Client>, then: impl FnOnce(&AppWindow) + 'static) {
    THEN.with(|t| t.borrow_mut().push(Box::new(then)));
    load(u.as_weak(), c);
}

pub fn reload_then_select(u: &AppWindow, c: Arc<Client>, id: &str) {
    let id = id.to_string();
    reload_then(u, c, move |u| { u.set_ct_id(id.as_str().into()); select(u, &id); });
}

fn in_filter_x(c: &Value, f: &str, groups: &[Value], dupes: &std::collections::HashSet<String>) -> bool {
    if f == "dupes:" { return c["id"].as_str().is_some_and(|i| dupes.contains(i)); }
    if f == "directory:" { return false; }
    in_filter(c, f, groups)
}

fn full_name(c: &Value) -> String {
    if let Some(f) = c["name"]["full"].as_str().filter(|s| !s.is_empty()) { return f.to_string(); }
    let comps: Vec<&str> = c["name"]["components"].as_array().map(|a| a.iter().filter_map(|x| x["value"].as_str()).collect()).unwrap_or_default();
    if !comps.is_empty() { return comps.join(" "); }
    first(c, "emails", "address").unwrap_or_else(|| "(no name)".into())
}

/// Name the list is ordered and letter-grouped by (Bulwark getContactSortName): with
/// sortContactsByLastName the surname leads ("Smith, Alice").
fn sort_name(c: &Value, by_last: bool) -> String {
    let display = full_name(c);
    if !by_last { return display; }
    if let Some(comps) = c["name"]["components"].as_array().filter(|a| !a.is_empty()) {
        let pick = |kinds: &[&str]| comps.iter()
            .filter(|x| x["kind"].as_str().is_some_and(|k| kinds.contains(&k)))
            .filter_map(|x| x["value"].as_str().filter(|v| !v.is_empty())).collect::<Vec<_>>().join(" ");
        let surname = pick(&["surname", "surname2"]);
        if !surname.is_empty() {
            let rest = pick(&["given", "given2", "middle", "additional"]);
            return if rest.is_empty() { surname } else { format!("{surname}, {rest}") };
        }
    }
    if let Some(full) = c["name"]["full"].as_str().filter(|f| *f == display) {
        let words: Vec<&str> = full.split_whitespace().collect();
        if words.len() > 1 { return format!("{}, {}", words[words.len() - 1], words[..words.len() - 1].join(" ")); }
    }
    display
}

pub(crate) fn first(c: &Value, map: &str, field: &str) -> Option<String> {
    c[map].as_object().and_then(|o| o.values().find_map(|v| v[field].as_str().map(String::from)))
}

fn all(c: &Value, map: &str, field: &str) -> Vec<String> {
    c[map].as_object().map(|o| o.values().filter_map(|v| v[field].as_str().map(String::from)).collect()).unwrap_or_default()
}

pub fn load(w: slint::Weak<AppWindow>, c: Arc<Client>) {
    if !c.has("urn:ietf:params:jmap:contacts") { return; }
    crate::contacts_x::set_has_principals(c.has("urn:ietf:params:jmap:principals"));
    CLIENT.with(|x| *x.borrow_mut() = Some(c.clone()));
    std::thread::spawn(move || {
        let acc = c.account_for("urn:ietf:params:jmap:contacts");
        let res: Result<(Vec<Value>, Vec<Value>), String> = (|| {
            let r = c.call(json!([["AddressBook/get", {"accountId": acc, "ids": null}, "a"]]))?;
            let books = r[0][1]["list"].as_array().cloned().unwrap_or_default();
            // every card, paged past the server's query cap, fetched in maxObjectsInGet chunks
            let ids = c.query_all("ContactCard/query", &acc, json!({}))?;
            let cards = c.get_all("ContactCard/get", &acc, &ids, None)?;
            Ok((books, cards))
        })();
        let _ = w.upgrade_in_event_loop(move |u| match res {
            Ok((books, mut cards)) => {
                let mut groups: Vec<Value> = cards.iter().filter(|x| x["kind"].as_str() == Some("group")).cloned().collect();
                groups.sort_by_key(|x| full_name(x).to_lowercase());
                cards.retain(|x| x["kind"].as_str() != Some("group"));
                cards.sort_by_key(|x| full_name(x).to_lowercase());
                ST.with(|s| { let mut s = s.borrow_mut(); s.books = books; s.cards = cards; s.groups = groups; });
                render(&u);
                if current_filter() == "dupes:" { crate::contacts_x::render_dupes(&u); }
                for f in THEN.with(|t| std::mem::take(&mut *t.borrow_mut())) { f(&u); }
            }
            Err(e) => crate::mail::show_toast(&u, &format!("Contacts: {e}"), false),
        });
    });
}

fn bare(k: &str) -> &str { k.strip_prefix("urn:uuid:").unwrap_or(k) }

/// Whether `card` is a member of `group` (members keyed by uid, bare or urn:uuid:, or by id; Bulwark).
fn is_member(group: &Value, card: &Value) -> bool {
    let uid = card["uid"].as_str().map(bare).unwrap_or("");
    let id = card["id"].as_str().unwrap_or("");
    group["members"].as_object().is_some_and(|m| m.iter().any(|(k, v)| *v == Value::Bool(true) && (bare(k) == id || (!uid.is_empty() && bare(k) == uid))))
}

fn keywords(c: &Value) -> Vec<String> {
    c["keywords"].as_object().map(|o| o.iter().filter(|(_, v)| **v == Value::Bool(true)).map(|(k, _)| k.clone()).collect()).unwrap_or_default()
}

fn in_filter(c: &Value, f: &str, groups: &[Value]) -> bool {
    if f.is_empty() { return true; }
    if f == "uncat" { return keywords(c).is_empty(); }
    if let Some(k) = f.strip_prefix("kw:") { return keywords(c).iter().any(|x| x == k); }
    if let Some(b) = f.strip_prefix("book:") { return c["addressBookIds"][b] == true; }
    if let Some(g) = f.strip_prefix("group:") {
        let Some(g) = groups.iter().find(|x| x["id"] == g) else { return false };
        return is_member(g, c);
    }
    true
}

/// Every category (keyword) in use, with its contact count, by name.
pub fn all_keywords() -> Vec<(String, usize)> {
    ST.with(|s| {
        let mut m: std::collections::BTreeMap<String, usize> = Default::default();
        for c in &s.borrow().cards { for k in keywords(c) { *m.entry(k).or_default() += 1; } }
        m.into_iter().collect()
    })
}

pub fn set_filter(u: &AppWindow, f: &str) {
    ST.with(|s| s.borrow_mut().filter = f.to_string());
    u.set_ct_filter(f.into());
    if f == "dupes:" || f == "directory:" { u.set_ct_id("".into()); }
    if f == "dupes:" { crate::contacts_x::render_dupes(u); }
    if f == "directory:" { if let Some(c) = client() { crate::contacts_x::load_directory(u, c); } }
    render(u);
}

fn nav(kind: &str, id: &str, label: &str, icon: &str, count: usize, header: &str) -> NavRow {
    NavRow { kind: kind.into(), id: id.into(), label: label.into(), icon: icon.into(), count: if count > 0 || kind != "section" { count.to_string().into() } else { "".into() }, color: Default::default(), depth: 0, header: header.into() }
}

/// The sidebar: all contacts, address books, groups, categories (Bulwark contacts-sidebar).
fn render_nav(u: &AppWindow) {
    ST.with(|s| {
        let s = s.borrow();
        let mut v = vec![nav("all", "", "All contacts", "\u{E77B}", s.cards.len(), "")];
        let mut first = true;
        for b in &s.books {
            let id = b["id"].as_str().unwrap_or("");
            let n = s.cards.iter().filter(|c| c["addressBookIds"][id] == true).count();
            let name = b["name"].as_str().unwrap_or("Address book");
            v.push(nav("book", id, name, "\u{E82D}", n, if first { "Address books" } else { "" }));
            first = false;
        }
        let mut first = true;
        for g in &s.groups {
            let id = g["id"].as_str().unwrap_or("");
            let n = g["members"].as_object().map(|m| m.values().filter(|x| **x == Value::Bool(true)).count()).unwrap_or(0);
            v.push(nav("group", id, &full_name(g), "\u{E716}", n, if first { "Groups" } else { "" }));
            first = false;
        }
        if first { v.push(nav("section", "groups", "", "", 0, "Groups")); }
        let uncat = s.cards.iter().filter(|c| keywords(c).is_empty()).count();
        drop(s);
        // duplicates (Bulwark contact-duplicates-view) and the server directory
        let dupes = crate::contacts_x::dupe_ids().len();
        let mut tools_header = "Tools";
        if dupes > 0 || current_filter() == "dupes:" { v.push(nav("dupes", "", "Duplicates", "\u{E8C8}", dupes, tools_header)); tools_header = ""; }
        if crate::contacts_x::directory_enabled() { v.push(nav("directory", "", "Directory", "\u{E716}", crate::contacts_x::directory().len(), tools_header)); }
        v.push(nav("uncat", "", "No category", "\u{E8EC}", uncat, "Categories"));
        for (k, n) in all_keywords() { v.push(nav("keyword", &k, &k, "\u{E8EC}", n, "")); }
        u.set_ct_nav(ModelRc::new(VecModel::from(v)));
    });
}

pub fn render(u: &AppWindow) {
    render_nav(u);
    let p = crate::settings::Settings::load();
    let by_letter = p.bool("groupContactsByLetter", true);
    let by_last = p.bool("sortContactsByLastName", false);
    let dupes = crate::contacts_x::dupe_ids();
    if current_filter() == "directory:" {
        let q = ST.with(|s| s.borrow().query.to_lowercase());
        let rows: Vec<ContactRow> = crate::contacts_x::directory().iter().filter(|p| {
            q.is_empty() || p["name"].as_str().unwrap_or("").to_lowercase().contains(&q) || p["email"].as_str().unwrap_or("").to_lowercase().contains(&q)
        }).map(|p| {
            let name = p["name"].as_str().unwrap_or("").to_string();
            ContactRow { id: format!("dir:{}", p["id"].as_str().unwrap_or("")).into(), initials: name.split_whitespace().filter_map(|w| w.chars().next()).take(2).collect::<String>().to_uppercase().into(),
                color: crate::mail::tint(&name), email: p["email"].as_str().unwrap_or(p["type"].as_str().unwrap_or("")).into(), name: name.into(), header: "".into() }
        }).collect();
        u.set_contacts(ModelRc::new(VecModel::from(rows)));
        return;
    }
    ST.with(|s| {
        let s = s.borrow();
        let q = s.query.to_lowercase();
        let mut last = String::new();
        let mut shown: Vec<(String, &Value)> = s.cards.iter().filter(|c| in_filter_x(c, &s.filter, &s.groups, &dupes)).filter(|c| {
            q.is_empty() || full_name(c).to_lowercase().contains(&q) || all(c, "emails", "address").iter().any(|e| e.to_lowercase().contains(&q))
        }).map(|c| (sort_name(c, by_last), c)).collect();
        shown.sort_by_key(|(k, _)| k.to_lowercase());
        let rows: Vec<ContactRow> = shown.into_iter().map(|(key, c)| {
            let name = full_name(c);
            let letter = key.trim().chars().next().map(|ch| if ch.is_alphabetic() { ch.to_uppercase().to_string() } else { "#".into() }).unwrap_or_default();
            let header = if by_letter && letter != last { last = letter.clone(); letter } else { String::new() };
            ContactRow {
                id: c["id"].as_str().unwrap_or("").into(),
                initials: name.split_whitespace().filter_map(|w| w.chars().next()).take(2).collect::<String>().to_uppercase().into(),
                color: crate::mail::tint(&name),
                email: first(c, "emails", "address").unwrap_or_default().into(),
                name: name.into(),
                header: header.into(),
            }
        }).collect();
        u.set_contacts(ModelRc::new(VecModel::from(rows)));
        let groups: Vec<ContactRow> = s.groups.iter().map(|g| {
            let name = full_name(g);
            let n = g["members"].as_object().map(|m| m.len()).unwrap_or(0);
            ContactRow { id: g["id"].as_str().unwrap_or("").into(), initials: "\u{E716}".into(), color: crate::mail::tint(&name),
                email: format!("{n} member{}", if n == 1 { "" } else { "s" }).into(), name: name.into(), header: "".into() }
        }).collect();
        u.set_contact_groups(ModelRc::new(VecModel::from(groups)));
    });
}

/// Members of a group (JSContact `members` keyed by the member cards' `uid`).
fn members(g: &Value) -> Vec<Value> {
    ST.with(|s| s.borrow().cards.iter().filter(|c| is_member(g, c)).cloned().collect())
}

/// Show a group: its name and the members' addresses.
pub fn select_group(u: &AppWindow, id: &str) {
    let Some(g) = ST.with(|s| s.borrow().groups.iter().find(|x| x["id"] == id).cloned()) else { return };
    let ms = members(&g);
    u.set_ct_id("".into());
    u.set_ct_group(id.into());
    u.set_ct_name(full_name(&g).into());
    u.set_ct_emails(ms.iter().filter_map(|m| first(m, "emails", "address")).collect::<Vec<_>>().join(", ").into());
    u.set_ct_notes(ms.iter().map(full_name).collect::<Vec<_>>().join("\n").into());
}

pub fn search(u: &AppWindow, q: &str) {
    ST.with(|s| s.borrow_mut().query = q.to_string());
    render(u);
}

/// Show a contact in the detail pane (also used to start editing).
pub fn select(u: &AppWindow, id: &str) {
    if let Some(pid) = id.strip_prefix("dir:") {
        u.set_ct_id(id.into());
        let p = crate::contacts_x::directory().into_iter().find(|p| p["id"] == pid);
        let name = p.as_ref().and_then(|p| p["name"].as_str()).unwrap_or("").to_string();
        u.set_ct_initials(name.split_whitespace().filter_map(|w| w.chars().next()).take(2).collect::<String>().to_uppercase().into());
        u.set_ct_name(name.into());
        u.set_ct_emails(p.as_ref().and_then(|p| p["email"].as_str()).unwrap_or("").into());
        crate::contacts_x::show_principal(u, pid);
        return;
    }
    let card = ST.with(|s| s.borrow().cards.iter().find(|c| c["id"] == id).cloned());
    crate::contacts_x::show(u, card.as_ref(), client());
    u.set_ct_id(id.into());
    match card {
        Some(c) => {
            let n = full_name(&c);
            u.set_ct_initials(n.split_whitespace().filter_map(|w| w.chars().find(|ch| ch.is_alphanumeric())).take(2).collect::<String>().to_uppercase().into());
            u.set_ct_name(n.into());
            u.set_ct_emails(all(&c, "emails", "address").join(", ").into());
            u.set_ct_phones(all(&c, "phones", "number").join(", ").into());
            u.set_ct_company(first(&c, "organizations", "name").unwrap_or_default().into());
            u.set_ct_title(first(&c, "titles", "name").unwrap_or_default().into());
            u.set_ct_address(c["addresses"].as_object().and_then(|o| o.values().next()).map(|a| {
                a["full"].as_str().map(String::from).unwrap_or_else(|| a["components"].as_array().map(|x| x.iter().filter_map(|p| p["value"].as_str()).collect::<Vec<_>>().join(" ")).unwrap_or_default())
            }).unwrap_or_default().into());
            u.set_ct_notes(c["notes"].as_object().and_then(|o| o.values().next()).and_then(|n| n["note"].as_str()).unwrap_or("").into());
            u.set_ct_birthday(c["anniversaries"].as_object().and_then(|o| o.values().find(|a| a["kind"] == "birth"))
                .and_then(|a| a["date"]["utc"].as_str().or(a["date"].as_str()).map(|d| d[..10.min(d.len())].to_string())).unwrap_or_default().into());
        }
        None => {
            u.set_ct_name("".into()); u.set_ct_emails("".into()); u.set_ct_phones("".into()); u.set_ct_company("".into());
            u.set_ct_title("".into()); u.set_ct_address("".into()); u.set_ct_notes("".into()); u.set_ct_birthday("".into());
        }
    }
    u.set_ct_status("".into());
}

fn split(s: &str) -> Vec<String> {
    s.split([',', ';', '\n']).map(str::trim).filter(|x| !x.is_empty()).map(String::from).collect()
}

pub fn save(u: &AppWindow, c: Arc<Client>) {
    let id = u.get_ct_id().to_string();
    let name = u.get_ct_name().to_string();
    if name.trim().is_empty() && u.get_ct_emails().trim().is_empty() { u.set_ct_status("Add a name or an email.".into()); return; }
    let map = |vals: Vec<String>, prefix: &str, field: &str| -> Value {
        if vals.is_empty() { return Value::Null; }
        Value::Object(vals.into_iter().enumerate().map(|(i, v)| (format!("{prefix}{}", i + 1), json!({ field: v }))).collect())
    };
    let opt = |v: String, prefix: &str, field: &str| -> Value { if v.trim().is_empty() { Value::Null } else { json!({ format!("{prefix}1"): { field: v.trim() } }) } };
    let mut card = json!({
        "name": {"full": name.trim()},
        "emails": map(split(&u.get_ct_emails()), "e", "address"),
        "phones": map(split(&u.get_ct_phones()), "p", "number"),
        "organizations": opt(u.get_ct_company().to_string(), "o", "name"),
        "titles": opt(u.get_ct_title().to_string(), "t", "name"),
        "addresses": opt(u.get_ct_address().to_string(), "a", "full"),
        "notes": opt(u.get_ct_notes().to_string(), "n", "note"),
    });
    let bday = u.get_ct_birthday().trim().to_string();
    card["anniversaries"] = if bday.len() == 10 { json!({"b1": {"@type": "Anniversary", "kind": "birth", "date": {"@type": "Timestamp", "utc": format!("{bday}T00:00:00Z")}}}) } else { Value::Null };
    let chosen_book = crate::ctxmenu::BOOK_FOR_NEW.with(|b| b.borrow_mut().take()).filter(|_| id.is_empty());
    let w = u.as_weak();
    u.set_ct_status("Saving…".into());
    std::thread::spawn(move || {
        let acc = c.account_for("urn:ietf:params:jmap:contacts");
        let res: Result<(), String> = if id.is_empty() {
            let book = chosen_book.or_else(|| ST.with(|s| s.borrow().books.iter().find(|b| b["isDefault"] == true).or(s.borrow().books.first()).and_then(|b| b["id"].as_str().map(String::from))));
            // a fresh account has no address book until a client creates one
            // the local list may not be loaded yet: ask the server before creating a book
            let book = book.or_else(|| c.call(json!([["AddressBook/get", {"accountId": acc, "ids": null, "properties": ["id", "isDefault"]}, "g"]])).ok()
                .and_then(|r| { let l = r[0][1]["list"].as_array().cloned().unwrap_or_default();
                    l.iter().find(|b| b["isDefault"] == true).or(l.first()).and_then(|b| b["id"].as_str().map(String::from)) }));
            let book = match book {
                Some(b) => b,
                None => match c.call(json!([["AddressBook/set", {"accountId": acc, "create": {"b": {"name": "Contacts"}}, "onSuccessSetIsDefault": "#b"}, "a"]]))
                    .and_then(|r| r[0][1]["created"]["b"]["id"].as_str().map(String::from).ok_or_else(|| crate::jmap::set_error(&r[0][1]["notCreated"]["b"]))) {
                    Ok(b) => b,
                    Err(e) => { let _ = w.upgrade_in_event_loop(move |u| u.set_ct_status(format!("No address book: {e}").into())); return }
                },
            };
            let obj = card.as_object_mut().unwrap();
            obj.retain(|_, v| !v.is_null());
            obj.insert("@type".into(), json!("Card"));
            obj.insert("version".into(), json!("1.0"));
            obj.insert("addressBookIds".into(), json!({ book: true }));
            c.call(json!([["ContactCard/set", {"accountId": acc, "create": {"c": card}}, "s"]]))
                .and_then(|r| if r[0][1]["created"]["c"].is_object() { Ok(()) } else { Err(crate::jmap::set_error(&r[0][1]["notCreated"]["c"])) })
        } else {
            c.update("ContactCard/set", &acc, vec![(id, card)]).and_then(|(_, bad)| if bad.is_empty() { Ok(()) } else { Err(bad.join("; ")) })
        };
        let w2 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| match res {
            Ok(()) => { u.set_ct_editing(false); u.set_ct_status("Saved".into()); load(w2, c); }
            Err(e) => u.set_ct_status(e.into()),
        });
    });
}

pub fn delete(u: &AppWindow, c: Arc<Client>) {
    let id = u.get_ct_id().to_string();
    if id.is_empty() { return; }
    let w = u.as_weak();
    std::thread::spawn(move || {
        let acc = c.account_for("urn:ietf:params:jmap:contacts");
        let res = c.call(json!([["ContactCard/set", {"accountId": acc, "destroy": [id]}, "d"]]));
        let w2 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| match res {
            Ok(_) => { u.set_ct_id("".into()); u.set_ct_editing(false); load(w2, c); }
            Err(e) => u.set_ct_status(e.into()),
        });
    });
}

// ---------------- groups & categories (context menus, sidebar) ----------------

/// The groups (id, name) and whether the contact is a member of each.
pub fn groups_for(contact: &str) -> Vec<(String, String, bool)> {
    ST.with(|s| {
        let s = s.borrow();
        let card = s.cards.iter().find(|c| c["id"] == contact);
        s.groups.iter().map(|g| (g["id"].as_str().unwrap_or("").to_string(), full_name(g), card.is_some_and(|c| is_member(g, c)))).collect()
    })
}

pub fn keywords_of(contact: &str) -> Vec<String> {
    ST.with(|s| s.borrow().cards.iter().find(|c| c["id"] == contact).map(keywords).unwrap_or_default())
}

/// The key a group lists this contact under: its uid, else its id (Bulwark `contact.uid || id`).
pub fn uid_of(contact: &str) -> Option<String> {
    ST.with(|s| s.borrow().cards.iter().find(|c| c["id"] == contact).map(|c| c["uid"].as_str().filter(|u| !u.is_empty()).unwrap_or(contact).to_string()))
}

pub fn group_name(id: &str) -> String {
    ST.with(|s| s.borrow().groups.iter().find(|g| g["id"] == id).map(full_name).unwrap_or_default())
}

/// (name, is default) of an address book.
pub fn book_info(id: &str) -> Option<(String, bool)> {
    ST.with(|s| s.borrow().books.iter().find(|b| b["id"] == id).map(|b| (b["name"].as_str().unwrap_or("Address book").to_string(), b["isDefault"] == true)))
}

fn default_book() -> Option<String> {
    ST.with(|s| { let s = s.borrow(); s.books.iter().find(|b| b["isDefault"] == true).or(s.books.first()).and_then(|b| b["id"].as_str().map(String::from)) })
}

/// Contacts carrying a category.
fn with_keyword(k: &str) -> Vec<String> {
    ST.with(|s| s.borrow().cards.iter().filter(|c| keywords(c).iter().any(|x| x == k)).filter_map(|c| c["id"].as_str().map(String::from)).collect())
}

fn ptr(k: &str) -> String { k.replace('~', "~0").replace('/', "~1") }

/// A ContactCard/set call in the background, then reload the contacts.
pub fn card_set(u: &AppWindow, c: Arc<Client>, args: Value, done: &'static str) {
    let w = u.as_weak();
    std::thread::spawn(move || {
        let acc = c.account_for("urn:ietf:params:jmap:contacts");
        let mut a = args;
        a["accountId"] = json!(acc);
        let res = c.call(json!([["ContactCard/set", a, "s"]])).and_then(|r| {
            if r[0][0] == "error" { return Err(crate::jmap::method_error(&r[0][1])); }
            for k in ["notCreated", "notUpdated", "notDestroyed"] {
                if let Some(e) = r[0][1][k].as_object().and_then(|m| m.values().next()) { return Err(crate::jmap::set_error(e)); }
            }
            Ok(())
        });
        let w2 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| {
            crate::mail::show_toast(&u, &match res { Ok(()) => done.to_string(), Err(e) => e }, false);
            load(w2, c);
        });
    });
}

/// New group (JSContact Card of kind "group") in the default address book, optionally with a first member.
pub fn create_group(u: &AppWindow, c: Arc<Client>, name: &str, first_member: Option<String>) {
    let Some(book) = default_book() else { crate::mail::show_toast(u, "No address book to put the group in", false); return };
    let mut g = json!({"@type": "Card", "version": "1.0", "kind": "group", "name": {"full": name}, "addressBookIds": { book: true }, "members": {}});
    if let Some(m) = first_member { g["members"][m] = json!(true); }
    card_set(u, c, json!({"create": {"g": g}}), "Group created");
}

pub fn set_member(u: &AppWindow, c: Arc<Client>, group: &str, uid: &str, on: bool) {
    let mut patch = serde_json::Map::new();
    // removing: drop the key the group actually uses (bare uid, urn:uuid: or id)
    let keys: Vec<String> = if on { vec![uid.to_string()] } else {
        ST.with(|s| s.borrow().groups.iter().find(|g| g["id"] == group).and_then(|g| g["members"].as_object().map(|m| m.keys().filter(|k| bare(k) == bare(uid)).cloned().collect())).unwrap_or_default())
    };
    for k in keys { patch.insert(format!("members/{}", ptr(&k)), if on { json!(true) } else { Value::Null }); }
    card_set(u, c, json!({"update": { group: Value::Object(patch) }}), if on { "Added to group" } else { "Removed from group" });
}

/// The card's whole keywords map with `k` set or cleared; Stalwart refuses a `keywords/x`
/// patch when the card has no keywords yet, so Bulwark sends the whole map too.
fn keywords_with(contact: &str, set: &[&str], clear: &[&str]) -> Value {
    let mut m: serde_json::Map<String, Value> = ST.with(|s| s.borrow().cards.iter().find(|c| c["id"] == contact).and_then(|c| c["keywords"].as_object().cloned())).unwrap_or_default();
    for k in clear { m.remove(*k); }
    for k in set { m.insert(k.to_string(), json!(true)); }
    if m.is_empty() { Value::Null } else { Value::Object(m) }
}

pub fn set_keyword(u: &AppWindow, c: Arc<Client>, contact: &str, k: &str, on: bool) {
    let kw = if on { keywords_with(contact, &[k], &[]) } else { keywords_with(contact, &[], &[k]) };
    card_set(u, c, json!({"update": { contact: {"keywords": kw} }}), if on { "Category added" } else { "Category removed" });
}

/// Rename a category on every contact that has it (Bulwark renameKeyword).
pub fn rename_keyword(u: &AppWindow, c: Arc<Client>, old: &str, new: &str) {
    let mut upd = serde_json::Map::new();
    for id in with_keyword(old) { let kw = keywords_with(&id, &[new], &[old]); upd.insert(id, json!({"keywords": kw})); }
    if upd.is_empty() { return; }
    ST.with(|s| { let mut s = s.borrow_mut(); if s.filter == format!("kw:{old}") { s.filter = format!("kw:{new}"); } });
    card_set(u, c, json!({"update": upd}), "Category renamed");
}

/// Take a category off every contact (Bulwark deleteCategory).
pub fn delete_keyword(u: &AppWindow, c: Arc<Client>, k: &str) {
    let mut upd = serde_json::Map::new();
    for id in with_keyword(k) { let kw = keywords_with(&id, &[], &[k]); upd.insert(id, json!({"keywords": kw})); }
    if upd.is_empty() { return; }
    if ST.with(|s| s.borrow().filter == format!("kw:{k}")) { set_filter(u, ""); }
    card_set(u, c, json!({"update": upd}), "Category deleted");
}

/// Whether a card is loaded (the palette waits for this before showing a found contact).
pub fn has_card(id: &str) -> bool {
    ST.with(|s| s.borrow().cards.iter().any(|c| c["id"] == id))
}

/// (name, first address) of every loaded card with an address, for search suggestions.
pub fn people() -> Vec<(String, String)> {
    ST.with(|s| s.borrow().cards.iter().filter_map(|c| first(c, "emails", "address").map(|e| (full_name(c), e))).collect())
}
