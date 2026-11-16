//! The composer's recipient fields as chips with autocomplete (Bulwark email-composer.tsx
//! RecipientInput, handleAutocomplete, insertAutocomplete; contact-store getAutocomplete and
//! loadRecentRecipients). The field text (`c-to`, `c-cc`, `c-bcc`) stays the source of truth:
//! a comma list in which a contact group is written in RFC 5322 group syntax. Chips are
//! rebuilt from it whenever it changes; edits here write it back.
use crate::jmap::Client;
use crate::recipients::{self as r, Recipient, Sources, Suggestion};
use crate::{AppWindow, RChip, RSugg, Rcpt};
use serde_json::{json, Value};
use slint::{ComponentHandle, Model, ModelRc, VecModel};
use std::cell::RefCell;
use std::sync::Arc;

#[derive(Default)]
struct State {
    src: Sources,
    /// whose address book `src` holds, and when it was read
    loaded: Option<(String, std::time::Instant)>,
    loading: bool,
    /// the open list: field, results, query they answer, highlight moved by the arrows
    field: i32,
    results: Vec<Suggestion>,
    query: String,
    moved: Option<i32>,
    /// chip armed by Backspace, per field
    armed: [Option<usize>; 3],
    /// a pick that came between a blur and its delayed commit
    picked: u64,
}

thread_local! { static ST: RefCell<State> = RefCell::new(State { field: -1, ..Default::default() }); }

fn get_field(u: &AppWindow, f: i32) -> String {
    match f { 1 => u.get_c_cc(), 2 => u.get_c_bcc(), _ => u.get_c_to() }.to_string()
}

fn set_field(u: &AppWindow, f: i32, list: &[Recipient]) {
    let s = r::format_list(list);
    match f { 1 => u.set_c_cc(s.into()), 2 => u.set_c_bcc(s.into()), _ => u.set_c_to(s.into()) }
    // the changed handler rebuilds the chips later; do it now so the next key sees them
    sync(u, f);
}

fn get_input(u: &AppWindow, f: i32) -> String {
    let g = u.global::<Rcpt>();
    match f { 1 => g.get_cc_input(), 2 => g.get_bcc_input(), _ => g.get_to_input() }.to_string()
}

fn set_input(u: &AppWindow, f: i32, t: &str) {
    let g = u.global::<Rcpt>();
    match f { 1 => g.set_cc_input(t.into()), 2 => g.set_bcc_input(t.into()), _ => g.set_to_input(t.into()) }
}

fn chip(x: &Recipient, selected: bool) -> RChip {
    match &x.group {
        Some(m) => RChip {
            label: format!("{} ({})", if x.name.trim().is_empty() { "Group" } else { x.name.trim() }, m.len()).into(),
            full: m.iter().map(|m| r::format_recipient(&m.name, &m.email)).collect::<Vec<_>>().join(", ").into(),
            initials: r::initials(&x.name, "").into(),
            kind: if m.iter().any(|m| !r::is_valid_email(&m.email)) { 2 } else { 1 },
            selected,
        },
        None => RChip {
            label: (if x.name.trim().is_empty() { x.email.clone() } else { x.name.trim().to_string() }).into(),
            full: r::format_recipient(&x.name, &x.email).into(),
            initials: r::initials(&x.name, &x.email).into(),
            kind: if r::is_valid_email(&x.email) { 0 } else { 2 },
            selected,
        },
    }
}

/// Rebuild the chips of field `f` from its text.
pub fn sync(u: &AppWindow, f: i32) {
    let list = r::parse_list(&get_field(u, f));
    let armed = ST.with(|s| {
        let mut s = s.borrow_mut();
        let a = &mut s.armed[f.clamp(0, 2) as usize];
        if a.is_some_and(|i| i >= list.len()) { *a = None; }
        *a
    });
    let rows: Vec<RChip> = list.iter().enumerate().map(|(i, x)| chip(x, armed == Some(i))).collect();
    let m = ModelRc::new(VecModel::from(rows));
    let g = u.global::<Rcpt>();
    match f { 1 => g.set_cc(m), 2 => g.set_bcc(m), _ => g.set_to(m) }
}

fn disarm(u: &AppWindow, f: i32) {
    if ST.with(|s| s.borrow_mut().armed[f.clamp(0, 2) as usize].take()).is_some() { sync(u, f); }
}

fn close(u: &AppWindow) {
    ST.with(|s| { let mut s = s.borrow_mut(); s.field = -1; s.results.clear(); s.moved = None; s.query.clear(); });
    let g = u.global::<Rcpt>();
    g.set_active(-1);
    g.set_highlight(-1);
    g.set_suggestions(ModelRc::default());
}

/// The highlighted row: the one the arrows moved to, else Bulwark's default pick.
fn effective(s: &State, input: &str) -> i32 {
    if s.field < 0 || s.results.is_empty() { return -1; }
    match s.moved { Some(i) => i, None if s.query == input.trim() => r::default_suggestion_index(input, &s.results), None => -1 }
}

fn show(u: &AppWindow, f: i32, input: &str) {
    let q = input.trim().to_string();
    if q.is_empty() { close(u); return; }
    let on: Vec<String> = r::expand(&r::parse_list(&get_field(u, f))).into_iter().map(|x| x.email.to_lowercase()).collect();
    let res: Vec<Suggestion> = ST.with(|s| r::suggest(&q, &s.borrow().src)).into_iter()
        .filter(|x| x.group.is_some() || !on.contains(&x.email.to_lowercase())).collect();
    if res.is_empty() { close(u); return; }
    let rows: Vec<RSugg> = res.iter().map(|x| match &x.group {
        Some((_, n)) => RSugg { name: x.name.as_str().into(), detail: format!("Group · {n} member{}", if *n == 1 { "" } else { "s" }).into(), initials: "".into(), group: true },
        None => RSugg {
            name: (if x.name.is_empty() { r::to_unicode_email(&x.email) } else { x.name.clone() }).into(),
            detail: (if x.name.is_empty() { String::new() } else { r::to_unicode_email(&x.email) }).into(),
            initials: r::initials(&x.name, &x.email).into(), group: false,
        },
    }).collect();
    let hl = ST.with(|s| {
        let mut s = s.borrow_mut();
        s.field = f; s.results = res; s.query = q; s.moved = None;
        effective(&s, input)
    });
    let g = u.global::<Rcpt>();
    g.set_suggestions(ModelRc::new(VecModel::from(rows)));
    g.set_highlight(hl);
    g.set_active(f);
}

/// Text typed into field `f`. A pasted list becomes chips at once (what is no address stays
/// in the field, Bulwark handlePaste); otherwise the suggestions follow the text.
pub fn edited(u: &AppWindow, f: i32, text: &str) {
    disarm(u, f);
    let t = text.trim();
    let multi = t.contains(['\n', ';']) || (t.contains([',', ' ', '\t']) && t.split(|c: char| c.is_whitespace() || c == ',').filter(|w| r::is_valid_email(w.trim_matches(['<', '>']))).count() >= 2);
    if multi {
        let mut list = r::parse_list(&get_field(u, f));
        let have: Vec<String> = list.iter().map(|x| x.email.clone()).collect();
        let (valid, invalid) = r::split_pasted(t, &have);
        if !valid.is_empty() {
            list.extend(valid);
            set_field(u, f, &list);
            let rest = invalid.join(" ");
            set_input(u, f, &rest);
            show(u, f, &rest);
            return;
        }
    }
    show(u, f, text);
}

/// Put whatever is typed in field `f` into chips (on Enter, Tab, blur and before sending).
/// Entries that are not addresses still become chips, marked invalid.
pub fn commit(u: &AppWindow, f: i32) {
    let input = get_input(u, f);
    if input.trim().is_empty() { return; }
    let mut list = r::parse_list(&get_field(u, f));
    if input.contains([',', ';']) {
        let have: Vec<String> = list.iter().map(|x| x.email.clone()).collect();
        let (valid, invalid) = r::split_pasted(&input, &have);
        list.extend(valid);
        list.extend(invalid.iter().map(|x| r::parse_recipient(x)));
    } else {
        list.push(r::parse_recipient(&input));
    }
    set_input(u, f, "");
    set_field(u, f, &list);
    if ST.with(|s| s.borrow().field == f) { close(u); }
}

/// Commit the typed text of every field (before a send or an explicit save).
pub fn commit_all(u: &AppWindow) {
    for f in 0..3 { commit(u, f); }
    close(u);
}

/// A new composer: empty inputs and no open list.
pub fn reset(u: &AppWindow) {
    for f in 0..3 { set_input(u, f, ""); }
    ST.with(|s| s.borrow_mut().armed = [None; 3]);
    close(u);
}

fn insert(u: &AppWindow, f: i32, idx: i32) {
    let Some(sug) = ST.with(|s| s.borrow().results.get(idx.max(0) as usize).cloned()) else { return };
    let mut list = r::parse_list(&get_field(u, f));
    match &sug.group {
        Some((id, _)) => {
            let members = ST.with(|s| s.borrow().src.groups.iter().find(|g| &g.0 == id).map(|g| g.2.clone())).unwrap_or_default();
            if members.is_empty() { return; }
            list.push(Recipient { name: sug.name.clone(), email: String::new(), group: Some(members) });
        }
        None => list.push(Recipient::addr(&sug.name, &sug.email)),
    }
    ST.with(|s| s.borrow_mut().picked += 1);
    set_input(u, f, "");
    set_field(u, f, &list);
    close(u);
}

/// A suggestion clicked.
pub fn pick(u: &AppWindow, i: i32) {
    let f = ST.with(|s| s.borrow().field);
    if f >= 0 { insert(u, f, i); }
}

pub fn remove(u: &AppWindow, f: i32, i: i32) {
    let mut list = r::parse_list(&get_field(u, f));
    if (i as usize) < list.len() { list.remove(i as usize); }
    ST.with(|s| s.borrow_mut().armed[f.clamp(0, 2) as usize] = None);
    set_field(u, f, &list);
}

/// Double-click: a group opens up into its members; a person goes back into the field to edit.
pub fn edit_chip(u: &AppWindow, f: i32, i: i32) {
    let mut list = r::parse_list(&get_field(u, f));
    let i = i as usize;
    if i >= list.len() { return; }
    let x = list.remove(i);
    match x.group {
        Some(m) => {
            let have: Vec<String> = list.iter().map(|y| y.email.to_lowercase()).collect();
            let add: Vec<Recipient> = m.into_iter().filter(|y| !have.contains(&y.email.to_lowercase())).collect();
            for (k, y) in add.into_iter().enumerate() { list.insert(i + k, y); }
        }
        None => {
            commit(u, f);
            list = r::parse_list(&get_field(u, f));
            if i < list.len() { list.remove(i); }
            set_input(u, f, &r::format_recipient(&x.name, &x.email));
        }
    }
    set_field(u, f, &list);
}

/// Focus left field `f`: commit its text, unless a suggestion was clicked meanwhile.
pub fn blurred(u: &AppWindow, f: i32) {
    let mark = ST.with(|s| s.borrow().picked);
    let w = u.as_weak();
    slint::Timer::single_shot(std::time::Duration::from_millis(180), move || {
        let Some(u) = w.upgrade() else { return };
        if ST.with(|s| s.borrow().picked) != mark { return; }
        commit(&u, f);
        disarm(&u, f);
        if ST.with(|s| s.borrow().field == f) { close(&u); }
    });
}

/// Keys in field `f` (Bulwark RecipientInput handleKeyDown). True when handled here.
pub fn key(u: &AppWindow, f: i32, text: &str, shift: bool, ctrl: bool) -> bool {
    if ctrl { return false; }
    let input = get_input(u, f);
    let open = ST.with(|s| s.borrow().field == f && !s.borrow().results.is_empty());
    let hl = ST.with(|s| effective(&s.borrow(), &input));
    let n = ST.with(|s| s.borrow().results.len() as i32);
    if text != "\u{8}" { disarm(u, f); }
    match text {
        "\u{1b}" if open => { close(u); true }
        "\u{F701}" if open => {
            let i = (hl + 1).min(n - 1);
            ST.with(|s| s.borrow_mut().moved = Some(i));
            u.global::<Rcpt>().set_highlight(i);
            true
        }
        "\u{F700}" if open => {
            let i = if hl < 0 { -1 } else { (hl - 1).max(0) };
            ST.with(|s| s.borrow_mut().moved = Some(i));
            u.global::<Rcpt>().set_highlight(i);
            true
        }
        "\n" | "\r" => {
            if open && hl >= 0 { insert(u, f, hl); true }
            else if !input.trim().is_empty() { commit(u, f); true }
            else { false }
        }
        "\t" if !shift => {
            // take the suggestion or the text, then let the focus move on
            if open && hl >= 0 { insert(u, f, hl); } else { commit(u, f); }
            false
        }
        "," | ";" if r::is_valid_email(&r::parse_recipient(input.trim()).email) => {
            if open && hl >= 0 { insert(u, f, hl); } else { commit(u, f); }
            true
        }
        " " if r::is_valid_email(input.trim()) => { commit(u, f); true }
        // no leading blank after a chip was added with a comma
        " " if input.is_empty() => true,
        "\u{8}" if input.is_empty() => {
            let len = r::parse_list(&get_field(u, f)).len();
            if len == 0 { return false; }
            let armed = ST.with(|s| s.borrow().armed[f.clamp(0, 2) as usize]);
            if armed == Some(len - 1) { remove(u, f, (len - 1) as i32); }
            else { ST.with(|s| s.borrow_mut().armed[f.clamp(0, 2) as usize] = Some(len - 1)); sync(u, f); }
            true
        }
        _ => false,
    }
}

// ---------------------------------------------------------------- sources

fn card_name(c: &Value) -> String {
    if let Some(f) = c["name"]["full"].as_str().filter(|s| !s.trim().is_empty()) { return f.trim().to_string(); }
    let comps: Vec<&str> = c["name"]["components"].as_array().map(|a| a.iter()
        .filter(|x| x["kind"].as_str() != Some("separator")).filter_map(|x| x["value"].as_str()).collect()).unwrap_or_default();
    comps.join(" ").trim().to_string()
}

/// Addresses of a card, the preferred one (lowest `pref`) first.
fn card_emails(c: &Value) -> Vec<String> {
    let mut v: Vec<(i64, String)> = c["emails"].as_object().map(|o| o.values().filter_map(|e| {
        e["address"].as_str().filter(|a| !a.trim().is_empty()).map(|a| (e["pref"].as_i64().unwrap_or(100), a.trim().to_string()))
    }).collect()).unwrap_or_default();
    v.sort_by_key(|x| x.0);
    v.into_iter().map(|x| x.1).collect()
}

fn bare(k: &str) -> &str { k.strip_prefix("urn:uuid:").unwrap_or(k) }

/// Contacts and groups (with their members' first addresses) from ContactCard objects.
pub fn sources_from_cards(cards: &[Value]) -> (Vec<(String, String)>, Vec<(String, String, Vec<Recipient>)>) {
    let mut people = vec![];
    for c in cards.iter().filter(|c| c["kind"].as_str() != Some("group")) {
        let n = card_name(c);
        for e in card_emails(c) { people.push((n.clone(), e)); }
    }
    let mut groups = vec![];
    for g in cards.iter().filter(|c| c["kind"].as_str() == Some("group")) {
        let keys: Vec<&str> = g["members"].as_object().map(|m| m.iter().filter(|(_, v)| v.as_bool() == Some(true)).map(|(k, _)| bare(k)).collect()).unwrap_or_default();
        let mut seen = std::collections::HashSet::new();
        let members: Vec<Recipient> = cards.iter().filter(|c| c["kind"].as_str() != Some("group")).filter(|c| {
            let id = c["id"].as_str().unwrap_or("");
            let uid = c["uid"].as_str().map(bare).unwrap_or("");
            keys.iter().any(|k| *k == id || (!uid.is_empty() && *k == uid))
        }).filter_map(|c| {
            let e = card_emails(c).into_iter().next()?;
            seen.insert(e.to_lowercase()).then(|| { let n = card_name(c); Recipient::addr(if n == e { "" } else { &n }, &e) })
        }).collect();
        groups.push((g["id"].as_str().unwrap_or("").to_string(), card_name(g), members));
    }
    (people, groups)
}

/// People written to, newest first, one entry per address (Bulwark loadRecentRecipients).
pub fn recent_from_sent(emails: &[Value]) -> Vec<(String, String)> {
    let mut out: Vec<(String, String)> = vec![];
    for m in emails {
        for x in ["to", "cc"].iter().flat_map(|k| m[*k].as_array().cloned().unwrap_or_default()) {
            let Some(e) = x["email"].as_str().map(str::trim).filter(|e| !e.is_empty()) else { continue };
            if out.iter().any(|o| o.1.eq_ignore_ascii_case(e)) { continue; }
            out.push((x["name"].as_str().unwrap_or("").trim().to_string(), e.to_string()));
        }
    }
    out
}

/// Read the address book and the Sent folder for suggestions (again after 10 minutes, or
/// for another account).
pub fn prefetch(u: &AppWindow, c: Arc<Client>) {
    let who = c.username.clone();
    let fresh = ST.with(|s| {
        let s = s.borrow();
        s.loading || s.loaded.as_ref().is_some_and(|(w, t)| *w == who && t.elapsed().as_secs() < 600)
    });
    if fresh { return; }
    ST.with(|s| s.borrow_mut().loading = true);
    let w = u.as_weak();
    std::thread::spawn(move || {
        let cards: Vec<Value> = (|| -> Result<Vec<Value>, String> {
            if !c.has("urn:ietf:params:jmap:contacts") { return Ok(vec![]); }
            let acc = c.account_for("urn:ietf:params:jmap:contacts");
            let ids = c.query_all("ContactCard/query", &acc, json!({}))?;
            c.get_all("ContactCard/get", &acc, &ids, Some(&json!(["id", "uid", "kind", "name", "emails", "members"])))
        })().unwrap_or_default();
        let sent: Vec<Value> = (|| -> Result<Vec<Value>, String> {
            let roles = c.role_ids()?;
            let Some(sent) = roles.get("sent") else { return Ok(vec![]) };
            let r = c.call(json!([
                ["Email/query", {"accountId": c.account, "filter": {"inMailbox": sent}, "sort": [{"property": "receivedAt", "isAscending": false}], "limit": 300}, "q"],
                ["Email/get", {"accountId": c.account, "#ids": {"resultOf": "q", "name": "Email/query", "path": "/ids"}, "properties": ["to", "cc"]}, "g"]
            ]))?;
            Ok(r[1][1]["list"].as_array().cloned().unwrap_or_default())
        })().unwrap_or_default();
        let (people, groups) = sources_from_cards(&cards);
        let recent = recent_from_sent(&sent);
        let _ = w.upgrade_in_event_loop(move |_| {
            ST.with(|s| {
                let mut s = s.borrow_mut();
                s.src = Sources { contacts: people, groups, recent };
                s.loaded = Some((who, std::time::Instant::now()));
                s.loading = false;
            });
        });
    });
}

/// Hook the Rcpt callbacks up.
pub fn wire(ui: &AppWindow) {
    let g = ui.global::<Rcpt>();
    let w = ui.as_weak();
    g.on_sync(move |f| { if let Some(u) = w.upgrade() { sync(&u, f); } });
    let w = ui.as_weak();
    g.on_edited(move |f, t| { if let Some(u) = w.upgrade() { edited(&u, f, &t); } });
    let w = ui.as_weak();
    g.on_key(move |f, t, shift, ctrl| w.upgrade().is_some_and(|u| key(&u, f, &t, shift, ctrl)));
    let w = ui.as_weak();
    g.on_remove(move |f, i| { if let Some(u) = w.upgrade() { remove(&u, f, i); } });
    let w = ui.as_weak();
    g.on_edit_chip(move |f, i| { if let Some(u) = w.upgrade() { edit_chip(&u, f, i); } });
    let w = ui.as_weak();
    g.on_blurred(move |f| { if let Some(u) = w.upgrade() { blurred(&u, f); } });
    let w = ui.as_weak();
    g.on_pick(move |i| { if let Some(u) = w.upgrade() { pick(&u, i); } });
}

/// Test hook: the chips of a field as text (automation `chips` probe).
pub fn describe(u: &AppWindow) -> String {
    let g = u.global::<Rcpt>();
    let f = |m: ModelRc<RChip>| m.iter().map(|c| format!("{}{}", if c.kind == 2 { "!" } else if c.kind == 1 { "#" } else { "" }, c.full)).collect::<Vec<_>>().join(" | ");
    format!("to=[{}] cc=[{}] bcc=[{}] input={:?} sugg={} hl={}", f(g.get_to()), f(g.get_cc()), f(g.get_bcc()), g.get_to_input().as_str(),
        g.get_suggestions().iter().map(|s| s.name.to_string()).collect::<Vec<_>>().join(","), g.get_highlight())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn cards_to_sources() {
        let cards = vec![
            json!({"id": "c1", "uid": "urn:uuid:u1", "name": {"full": "Ann Lee"}, "emails": {"a": {"address": "ann@x.io", "pref": 2}, "b": {"address": "lee@y.io", "pref": 1}}}),
            json!({"id": "c2", "name": {"components": [{"kind": "given", "value": "Bo"}, {"kind": "surname", "value": "Ek"}]}, "emails": {"a": {"address": "bo@x.io"}}}),
            json!({"id": "c3", "name": {"full": "No Mail"}}),
            json!({"id": "g1", "kind": "group", "name": {"full": "Team"}, "members": {"urn:uuid:u1": true, "c2": true, "c3": true, "zz": false}}),
        ];
        let (people, groups) = sources_from_cards(&cards);
        assert_eq!(people, vec![("Ann Lee".into(), "lee@y.io".into()), ("Ann Lee".into(), "ann@x.io".into()), ("Bo Ek".into(), "bo@x.io".into())]);
        assert_eq!(groups.len(), 1);
        assert_eq!(groups[0].1, "Team");
        assert_eq!(groups[0].2.iter().map(|m| m.email.as_str()).collect::<Vec<_>>(), vec!["lee@y.io", "bo@x.io"]);
    }

    #[test]
    fn recent_dedup() {
        let sent = vec![
            json!({"to": [{"name": "A", "email": "a@x.io"}], "cc": [{"email": "b@x.io"}]}),
            json!({"to": [{"name": "Old A", "email": "A@X.IO"}, {"email": " "}]}),
        ];
        assert_eq!(recent_from_sent(&sent), vec![("A".into(), "a@x.io".into()), ("".into(), "b@x.io".into())]);
    }
}
