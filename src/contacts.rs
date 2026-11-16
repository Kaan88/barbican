//! Contacts app: JMAP for Contacts (AddressBook / ContactCard, JSContact RFC 9553).
//! List grouped by letter, search, detail, create / edit / delete, write an email.
use crate::jmap::Client;
use crate::{AppWindow, ContactRow};
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
}

thread_local! { static ST: RefCell<State> = RefCell::new(State::default()); }

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

fn first(c: &Value, map: &str, field: &str) -> Option<String> {
    c[map].as_object().and_then(|o| o.values().find_map(|v| v[field].as_str().map(String::from)))
}

fn all(c: &Value, map: &str, field: &str) -> Vec<String> {
    c[map].as_object().map(|o| o.values().filter_map(|v| v[field].as_str().map(String::from)).collect()).unwrap_or_default()
}

pub fn load(w: slint::Weak<AppWindow>, c: Arc<Client>) {
    if !c.has("urn:ietf:params:jmap:contacts") { return; }
    std::thread::spawn(move || {
        let acc = c.account_for("urn:ietf:params:jmap:contacts");
        let res: Result<(Vec<Value>, Vec<Value>), String> = (|| {
            let r = c.call(json!([
                ["AddressBook/get", {"accountId": acc, "ids": null}, "a"],
                ["ContactCard/query", {"accountId": acc, "limit": 5000}, "q"],
            ]))?;
            let books = r[0][1]["list"].as_array().cloned().unwrap_or_default();
            let ids: Vec<Value> = r[1][1]["ids"].as_array().cloned().unwrap_or_default();
            let mut cards = vec![];
            for chunk in ids.chunks(c.max_get()) {
                let g = c.call(json!([["ContactCard/get", {"accountId": acc, "ids": chunk}, "g"]]))?;
                cards.extend(g[0][1]["list"].as_array().cloned().unwrap_or_default());
            }
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
            }
            Err(e) => crate::mail::show_toast(&u, &format!("Contacts: {e}"), false),
        });
    });
}

pub fn render(u: &AppWindow) {
    let p = crate::settings::Settings::load();
    let by_letter = p.bool("groupContactsByLetter", true);
    let by_last = p.bool("sortContactsByLastName", false);
    ST.with(|s| {
        let s = s.borrow();
        let q = s.query.to_lowercase();
        let mut last = String::new();
        let mut shown: Vec<(String, &Value)> = s.cards.iter().filter(|c| {
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
    let uids: Vec<String> = g["members"].as_object().map(|m| m.iter().filter(|(_, v)| **v == Value::Bool(true)).map(|(k, _)| k.clone()).collect()).unwrap_or_default();
    ST.with(|s| s.borrow().cards.iter().filter(|c| c["uid"].as_str().is_some_and(|u| uids.iter().any(|x| x == u))).cloned().collect())
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
    let card = ST.with(|s| s.borrow().cards.iter().find(|c| c["id"] == id).cloned());
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
    let w = u.as_weak();
    u.set_ct_status("Saving…".into());
    std::thread::spawn(move || {
        let acc = c.account_for("urn:ietf:params:jmap:contacts");
        let res: Result<(), String> = if id.is_empty() {
            let book = ST.with(|s| s.borrow().books.iter().find(|b| b["isDefault"] == true).or(s.borrow().books.first()).and_then(|b| b["id"].as_str().map(String::from)));
            // a fresh account has no address book until a client creates one
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
