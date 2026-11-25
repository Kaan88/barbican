//! Live updates outside mail (G-044): calendars and events, address books and cards, files and
//! identities follow push with `/changes` deltas (RFC 8620 §5.2) instead of a full re-read, and
//! fall back to the full load when the server cannot give one (cannotCalculateChanges, a state
//! that went back, too many changes).
//!
//! Bulwark keeps a `/changes` loop per type in its local store (lib/store/engine/changes.ts:61-101,
//! used for ContactCard in lib/store/contacts/sync.ts:152-180 and for FileNode in
//! lib/store/files/sync.ts:133) and re-reads calendars on Calendar/CalendarEvent pushes
//! (stores/email-store.ts:7549-7564, hooks/use-calendar-push-refresh.ts:16-41).
//!
//! Each view's full load records the state of every (type, account) it read, captured before the
//! read (a delta from it can only repeat, never miss, what the read saw), under the login it was
//! loaded for ([`Ledger`]). A delta is applied only when the view still holds that login's data and
//! its recorded state is still the one the delta started from: a reload or an account switch that
//! happened meanwhile wins, and no account's changes reach another's view.
use crate::jmap::Client;
use crate::AppWindow;
use serde_json::{json, Value};
use slint::ComponentHandle;
use std::collections::{HashMap, HashSet};
use std::sync::{Arc, Mutex, OnceLock};

/// A view that follows push with deltas.
#[derive(Clone, Copy, PartialEq, Eq, Hash, Debug)]
pub enum View { Calendar, Contacts, Files, Identities }

/// What a view was loaded at: its login ([`Client::state_key`]) and the state of each
/// (type, account) it read.
#[derive(Default, Debug)]
struct Book { owner: String, states: HashMap<(String, String), String> }

/// The views' recorded states, and which deltas are running.
#[derive(Default, Debug)]
pub struct Ledger {
    books: HashMap<View, Book>,
    running: HashSet<View>,
    again: HashSet<View>,
}

impl Ledger {
    /// A full load of `view` for `owner` finished: it now holds exactly these states.
    pub fn loaded(&mut self, view: View, owner: &str, states: &[(String, String, String)]) {
        let states = states.iter().map(|(t, a, s)| ((t.clone(), a.clone()), s.clone())).collect();
        self.books.insert(view, Book { owner: owner.to_string(), states });
    }

    /// The state `view` holds `ty` of `account` at, when it holds `owner`'s data.
    pub fn since(&self, view: View, owner: &str, ty: &str, account: &str) -> Option<String> {
        let b = self.books.get(&view).filter(|b| b.owner == owner)?;
        b.states.get(&(ty.to_string(), account.to_string())).cloned()
    }

    /// Whose data `view` holds ("" none).
    pub fn owner(&self, view: View) -> String { self.books.get(&view).map(|b| b.owner.clone()).unwrap_or_default() }

    /// Move `view` on to the new states of a delta, all or nothing: `false` (and nothing changes)
    /// when it no longer holds `owner`'s data or any state is not the one the delta started from.
    pub fn advance(&mut self, view: View, owner: &str, steps: &[(String, String, String, String)]) -> bool {
        let Some(b) = self.books.get_mut(&view).filter(|b| b.owner == owner) else { return false };
        if !steps.iter().all(|(t, a, since, _)| b.states.get(&(t.clone(), a.clone())) == Some(since)) { return false; }
        for (t, a, _, new) in steps { b.states.insert((t.clone(), a.clone()), new.clone()); }
        true
    }

    /// The view's data is gone (sign-out): no delta applies to it any more.
    pub fn forget(&mut self, view: View) { self.books.remove(&view); }

    /// A delta of `view` may start; `false` when one runs (it runs again once it ends, so a
    /// push that came in meanwhile is not lost and two deltas never race from one state).
    pub fn begin(&mut self, view: View) -> bool {
        if self.running.insert(view) { return true; }
        self.again.insert(view);
        false
    }

    /// The delta of `view` ended; `true` when another was asked for meanwhile.
    pub fn finish(&mut self, view: View) -> bool {
        self.running.remove(&view);
        self.again.remove(&view)
    }
}

static LEDGER: Mutex<Option<Ledger>> = Mutex::new(None);

fn with<T>(f: impl FnOnce(&mut Ledger) -> T) -> T {
    let mut g = LEDGER.lock().unwrap_or_else(|e| e.into_inner());
    f(g.get_or_insert_with(Ledger::default))
}

/// Record a full load (see [`Ledger::loaded`]).
pub fn loaded(view: View, owner: &str, states: &[(String, String, String)]) { with(|l| l.loaded(view, owner, states)); }

/// Whose data `view` holds ("" none).
pub fn owner(view: View) -> String { with(|l| l.owner(view)) }

/// Types that live in the calendars, contacts and files accounts too (shared ones included), not
/// only in the mail account a StateChange is otherwise read for.
pub const SHARED_TYPES: [&str; 7] = ["Calendar", "CalendarEvent", "AddressBook", "ContactCard", "FileNode", "ShareNotification", "CalendarEventNotification"];

/// The types a StateChange reports: every type of the mail `account`, and those of
/// [`SHARED_TYPES`] in any account (a shared calendar's account, a calendars account of its own).
pub fn pushed_types(change: &Value, account: &str) -> HashSet<String> {
    let mut out: HashSet<String> = crate::jmap::changed_types(change, account).into_keys().collect();
    if let Some(all) = change["changed"].as_object() {
        for (acc, types) in all {
            if acc == account { continue; }
            if let Some(t) = types.as_object() { out.extend(t.keys().filter(|k| SHARED_TYPES.contains(&k.as_str())).cloned()); }
        }
    }
    out
}

/// Every view forgets its data's states (sign-out).
pub fn forget_all() { with(|l| for v in [View::Calendar, View::Contacts, View::Files, View::Identities] { l.forget(v) }); }

/// The current state of `ty` in `account` (`X/get` with no ids), read before a full load.
pub fn state_of(c: &Client, ty: &str, account: &str) -> Option<String> {
    let r = c.request(json!([[format!("{ty}/get"), {"accountId": account, "ids": [], "properties": ["id"]}, "s"]])).ok()?;
    r.first().filter(|x| x[0] != "error").and_then(|x| x[1]["state"].as_str().map(String::from))
}

/// What changed in one type of one account since a state.
#[derive(Debug, Default, Clone, PartialEq)]
pub struct Delta {
    pub destroyed: Vec<String>,
    /// Fresh, whole copies of the created and updated objects.
    pub fetched: Vec<Value>,
    pub state: String,
}

/// Fold /changes pages into the ids to fetch and those to drop: an object created or updated and
/// then destroyed in the same window is only dropped.
pub fn fold(pages: &[crate::jmap::Changes]) -> (Vec<String>, Vec<String>) {
    let destroyed: Vec<String> = pages.iter().flat_map(|p| p.destroyed.iter().cloned()).collect();
    let gone: HashSet<&String> = destroyed.iter().collect();
    let mut fetch = vec![];
    for id in pages.iter().flat_map(|p| p.created.iter().chain(p.updated.iter())) {
        if !gone.contains(id) && !fetch.contains(id) { fetch.push(id.clone()); }
    }
    (fetch, destroyed)
}

/// Pages of up to this many changes, and at most this many pages before a full load is cheaper.
const MAX_CHANGES: usize = 500;
const MAX_PAGES: usize = 20;

/// `ty`/changes of `account` since `since` and the `ty`/get of what was created or updated
/// (with `props`, the properties the view loads). `Ok(None)`: no delta possible, load in full.
pub fn delta(c: &Client, ty: &str, account: &str, since: &str, props: Option<&Value>) -> Result<Option<Delta>, String> {
    let mut pages = vec![];
    let mut state = since.to_string();
    for _ in 0..MAX_PAGES {
        let Some(ch) = c.changes(ty, account, &state, Some(MAX_CHANGES))? else { return Ok(None) };
        state = ch.new_state.clone();
        let more = ch.has_more;
        pages.push(ch);
        if !more {
            let (fetch, destroyed) = fold(&pages);
            let ids: Vec<Value> = fetch.iter().map(|i| json!(i)).collect();
            let fetched = if ids.is_empty() { vec![] } else { c.get_all(&format!("{ty}/get"), account, &ids, props)? };
            return Ok(Some(Delta { destroyed, fetched, state }));
        }
    }
    Ok(None)
}

/// Apply `d` (of `account`) to a list that may hold several accounts' objects: `ident` gives an
/// item's (account, server id), `prep` turns a fetched object into an item. Objects of other
/// accounts are never touched; an updated object replaces its item whole (a property removed on
/// the server goes too), a new one is appended.
pub fn apply(have: &mut Vec<Value>, account: &str, d: &Delta, ident: impl Fn(&Value) -> (String, String), prep: impl Fn(Value) -> Value) {
    have.retain(|x| { let (a, id) = ident(x); a != account || !d.destroyed.contains(&id) });
    for f in &d.fetched {
        let Some(id) = f["id"].as_str() else { continue };
        let item = prep(f.clone());
        match have.iter().position(|x| { let (a, i) = ident(x); a == account && i == id }) {
            Some(p) => have[p] = item,
            None => have.push(item),
        }
    }
}

/// The deltas a view needs: `None` when any (type, account) has no recorded state for `owner`
/// (the view holds another login's data, or none).
pub fn plan(view: View, owner: &str, wanted: &[(String, String)]) -> Option<Vec<(String, String, String)>> {
    with(|l| wanted.iter().map(|(t, a)| l.since(view, owner, t, a).map(|s| (t.clone(), a.clone(), s))).collect())
}

/// The outcome of a view's deltas.
enum Outcome { Full, Nothing, Apply(Vec<(String, String, String, Delta)>) }

/// Run the deltas of `steps` (type, account, since) with each type's properties.
fn run(c: &Client, steps: &[(String, String, String)], props: impl Fn(&str) -> Option<Value>) -> Outcome {
    let mut out = vec![];
    for (ty, acc, since) in steps {
        match delta(c, ty, acc, since, props(ty).as_ref()) {
            Ok(Some(d)) => out.push((ty.clone(), acc.clone(), since.clone(), d)),
            Ok(None) => return Outcome::Full,
            // offline or a refused request: the next push or opening the view catches up
            Err(_) => return Outcome::Nothing,
        }
    }
    if out.iter().all(|(_, _, since, d)| d.state == *since && d.destroyed.is_empty() && d.fetched.is_empty()) { return Outcome::Nothing; }
    Outcome::Apply(out)
}

/// One view's push: deltas on a thread, then `full` (the view's full load) or `apply` on the UI
/// thread, only while the view still holds `owner`'s data at the states the deltas started from.
fn follow(view: View, w: slint::Weak<AppWindow>, c: Arc<Client>, wanted: Vec<(String, String)>,
          props: fn(&str) -> Option<Value>, full: fn(&AppWindow, Arc<Client>), apply_ui: fn(&AppWindow, &Client, &[(String, String, String, Delta)])) {
    let owner = c.state_key();
    let Some(steps) = plan(view, &owner, &wanted) else { return };
    if !with(|l| l.begin(view)) { return; }
    std::thread::spawn(move || {
        let outcome = run(&c, &steps, props);
        let _ = w.clone().upgrade_in_event_loop(move |u| {
            match outcome {
                Outcome::Full => if with(|l| l.owner(view)) == owner { full(&u, c.clone()) },
                Outcome::Nothing => {}
                Outcome::Apply(ds) => {
                    let moves: Vec<(String, String, String, String)> = ds.iter().map(|(t, a, s, d)| (t.clone(), a.clone(), s.clone(), d.state.clone())).collect();
                    if with(|l| l.advance(view, &owner, &moves)) { apply_ui(&u, &c, &ds); }
                }
            }
            if with(|l| l.finish(view)) { follow(view, u.as_weak(), c, wanted, props, full, apply_ui); }
        });
    });
}

// ---------------- the views ----------------

/// Calendar and CalendarEvent of every calendar account.
pub fn calendar(w: slint::Weak<AppWindow>, c: Arc<Client>) {
    let accounts = crate::calendar::calendar_accounts(&c);
    let wanted: Vec<(String, String)> = accounts.iter().flat_map(|(a, _, _)| [("Calendar".to_string(), a.clone()), ("CalendarEvent".to_string(), a.clone())]).collect();
    // a view loaded before a new shared account appeared has no state for it: plan() says load in full
    let owner = c.state_key();
    if with(|l| l.owner(View::Calendar)) == owner && plan(View::Calendar, &owner, &wanted).is_none() {
        crate::calendar::load(w, c);
        return;
    }
    follow(View::Calendar, w, c, wanted,
        |ty| Some(if ty == "Calendar" { json!(crate::calendar::CAL_PROPS.to_vec()) } else { json!(crate::calendar::EVENT_PROPS.to_vec()) }),
        |u, c| crate::calendar::load(u.as_weak(), c),
        |u, c, ds| crate::calendar::apply_live(u, c, ds));
}

/// AddressBook and ContactCard of the contacts account.
pub fn contacts(w: slint::Weak<AppWindow>, c: Arc<Client>) {
    let acc = c.account_for("urn:ietf:params:jmap:contacts");
    follow(View::Contacts, w, c, vec![("AddressBook".into(), acc.clone()), ("ContactCard".into(), acc)],
        |_| None,
        |u, c| crate::contacts::load(u.as_weak(), c),
        |u, _, ds| crate::contacts::apply_live(u, ds));
}

/// FileNode of every account with file storage.
pub fn files(w: slint::Weak<AppWindow>, c: Arc<Client>) {
    let wanted: Vec<(String, String)> = crate::files::accounts(&c).into_iter().map(|a| ("FileNode".to_string(), a)).collect();
    follow(View::Files, w, c, wanted,
        |_| Some(json!(crate::files::PROPS.to_vec())),
        |u, c| crate::files::reload(u, c),
        |u, _, ds| crate::files::apply_live(u, ds));
}

// ---------------- identities ----------------

/// The signed-in login's identities as last read, for the "is this me" checks and the settings
/// pages (Identity/changes keeps it current).
static IDENTITIES: Mutex<(String, Vec<Value>)> = Mutex::new((String::new(), Vec::new()));
static SETTINGS: OnceLock<crate::server_settings::Shared> = OnceLock::new();

/// The settings pages' cache, so an identity change reaches an open Identities page.
pub fn set_settings_cache(srv: crate::server_settings::Shared) { let _ = SETTINGS.set(srv); }

/// Identity/get of every identity, recording its state for later deltas.
pub fn load_identities(c: &Client) -> Result<Vec<Value>, String> {
    let acc = crate::identities::account(c);
    let r = c.call(json!([["Identity/get", {"accountId": acc, "ids": null}, "i"]]))?;
    let list = r[0][1]["list"].as_array().cloned().unwrap_or_default();
    let owner = c.state_key();
    if let Some(s) = r[0][1]["state"].as_str() { loaded(View::Identities, &owner, &[("Identity".into(), acc, s.into())]); }
    *IDENTITIES.lock().unwrap_or_else(|e| e.into_inner()) = (owner, list.clone());
    Ok(list)
}

/// Identity push: the delta is applied to the login's list, which then goes to the open
/// settings page (when it shows this login) and to the own-address checks.
pub fn identities(w: slint::Weak<AppWindow>, c: Arc<Client>) {
    let acc = crate::identities::account(&c);
    follow(View::Identities, w, c, vec![("Identity".into(), acc)],
        |_| None,
        |u, c| {
            let w = u.as_weak();
            std::thread::spawn(move || if let Ok(list) = load_identities(&c) { let _ = w.upgrade_in_event_loop(move |u| identities_changed(&u, &c, list)); });
        },
        |u, c, ds| {
            let owner = c.state_key();
            let list = {
                let mut g = IDENTITIES.lock().unwrap_or_else(|e| e.into_inner());
                if g.0 != owner { return; }
                for (_, acc, _, d) in ds { apply(&mut g.1, acc, d, |x| (acc.clone(), x["id"].as_str().unwrap_or("").to_string()), |v| v); }
                g.1.clone()
            };
            identities_changed(u, c, list);
        });
}

fn identities_changed(u: &AppWindow, c: &Client, list: Vec<Value>) {
    if let Some(srv) = SETTINGS.get() {
        let mut k = srv.lock().unwrap_or_else(|e| e.into_inner());
        if k.owner == c.state_key() && k.ids_loaded { k.identities = list.clone(); }
    }
    crate::set_identities(u, c, &list);
    u.invoke_settings_search(u.get_settings_query());
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::jmap::Changes;

    fn st(t: &str, a: &str, s: &str) -> (String, String, String) { (t.into(), a.into(), s.into()) }
    fn mv(t: &str, a: &str, f: &str, n: &str) -> (String, String, String, String) { (t.into(), a.into(), f.into(), n.into()) }

    #[test]
    fn states_are_kept_per_view_type_account_and_login() {
        let mut l = Ledger::default();
        l.loaded(View::Calendar, "srv\nann\nA", &[st("CalendarEvent", "A", "s1"), st("CalendarEvent", "B", "t1"), st("Calendar", "A", "c1")]);
        l.loaded(View::Contacts, "srv\nann\nA", &[st("ContactCard", "A", "k1")]);
        assert_eq!(l.since(View::Calendar, "srv\nann\nA", "CalendarEvent", "A").as_deref(), Some("s1"));
        assert_eq!(l.since(View::Calendar, "srv\nann\nA", "CalendarEvent", "B").as_deref(), Some("t1"));
        assert_eq!(l.since(View::Calendar, "srv\nann\nA", "Calendar", "B"), None);
        assert_eq!(l.since(View::Contacts, "srv\nann\nA", "CalendarEvent", "A"), None);
        // another login (the same account id on another server, or after a switch) gets nothing
        assert_eq!(l.since(View::Calendar, "other\nann\nA", "CalendarEvent", "A"), None);
        assert_eq!(l.since(View::Calendar, "srv\nbob\nA", "CalendarEvent", "A"), None);
    }

    #[test]
    fn a_switch_drops_the_old_logins_states_and_its_deltas() {
        let mut l = Ledger::default();
        l.loaded(View::Files, "ann", &[st("FileNode", "A", "f1")]);
        // bob's full load replaces the book; a delta ann computed meanwhile does not apply
        l.loaded(View::Files, "bob", &[st("FileNode", "A", "g1")]);
        assert!(!l.advance(View::Files, "ann", &[mv("FileNode", "A", "f1", "f2")]));
        assert_eq!(l.since(View::Files, "bob", "FileNode", "A").as_deref(), Some("g1"));
        assert_eq!(l.since(View::Files, "ann", "FileNode", "A"), None);
    }

    #[test]
    fn advance_is_all_or_nothing_and_needs_the_starting_state() {
        let mut l = Ledger::default();
        l.loaded(View::Calendar, "o", &[st("Calendar", "A", "c1"), st("CalendarEvent", "A", "e1")]);
        // a full reload moved CalendarEvent on meanwhile: the delta from e1 is stale, nothing moves
        assert!(!l.advance(View::Calendar, "o", &[mv("Calendar", "A", "c1", "c2"), mv("CalendarEvent", "A", "e0", "e2")]));
        assert_eq!(l.since(View::Calendar, "o", "Calendar", "A").as_deref(), Some("c1"));
        assert!(l.advance(View::Calendar, "o", &[mv("Calendar", "A", "c1", "c2"), mv("CalendarEvent", "A", "e1", "e2")]));
        assert_eq!(l.since(View::Calendar, "o", "CalendarEvent", "A").as_deref(), Some("e2"));
        // the same delta twice: the second no longer starts from the held state
        assert!(!l.advance(View::Calendar, "o", &[mv("CalendarEvent", "A", "e1", "e2")]));
        l.forget(View::Calendar);
        assert!(!l.advance(View::Calendar, "o", &[mv("Calendar", "A", "c2", "c3")]));
    }

    #[test]
    fn a_push_during_a_delta_runs_it_again_once() {
        let mut l = Ledger::default();
        assert!(l.begin(View::Contacts));
        assert!(!l.begin(View::Contacts));
        assert!(!l.begin(View::Contacts));
        assert!(l.begin(View::Files), "views run independently");
        assert!(l.finish(View::Contacts), "asked again while running");
        assert!(l.begin(View::Contacts));
        assert!(!l.finish(View::Contacts));
    }

    fn page(created: &[&str], updated: &[&str], destroyed: &[&str]) -> Changes {
        let v = |x: &[&str]| x.iter().map(|s| s.to_string()).collect();
        Changes { created: v(created), updated: v(updated), destroyed: v(destroyed), ..Default::default() }
    }

    #[test]
    fn fold_drops_what_was_destroyed_in_the_window() {
        let (fetch, destroyed) = fold(&[page(&["n1", "n2"], &["u1"], &["d1"]), page(&[], &["n1", "u2"], &["n2", "u1"])]);
        assert_eq!(fetch, vec!["n1", "u2"]);
        assert_eq!(destroyed, vec!["d1", "n2", "u1"]);
    }

    fn ident(x: &Value) -> (String, String) { (x["_acc"].as_str().unwrap_or("").into(), x["_rid"].as_str().unwrap_or("").into()) }

    #[test]
    fn apply_creates_updates_and_destroys_in_one_account_only() {
        let mut have = vec![
            json!({"id": "e1", "_acc": "A", "_rid": "e1", "title": "keep", "location": "x"}),
            json!({"id": "e2", "_acc": "A", "_rid": "e2", "title": "gone"}),
            json!({"id": "B/e1", "_acc": "B", "_rid": "e1", "title": "other account"}),
            json!({"id": "B/e2", "_acc": "B", "_rid": "e2", "title": "other account 2"}),
        ];
        let d = Delta { destroyed: vec!["e2".into()], fetched: vec![json!({"id": "e1", "title": "renamed"}), json!({"id": "e3", "title": "new"})], state: "s2".into() };
        apply(&mut have, "A", &d, ident, |mut v| { let id = v["id"].clone(); v["_acc"] = json!("A"); v["_rid"] = id; v });
        let titles: Vec<&str> = have.iter().map(|x| x["title"].as_str().unwrap()).collect();
        assert_eq!(titles, ["renamed", "other account", "other account 2", "new"]);
        // replaced whole: a property removed on the server is gone
        assert!(have[0].get("location").is_none());
        // account B's e2 has the destroyed id but is not touched
        assert_eq!(have[2]["_acc"], "B");
    }

    /// A fake server for /changes and /get of one type: `pages` answer the /changes calls in turn.
    fn server(pages: Vec<Value>, objects: Value, log: Arc<Mutex<Vec<Value>>>) -> Client {
        let pages = Mutex::new(pages);
        Client::fake("https://mail.example.test", "A", &[], move |calls| {
            Ok(calls.as_array().unwrap().iter().map(|c| {
                log.lock().unwrap_or_else(|e| e.into_inner()).push(c.clone());
                let m = c[0].as_str().unwrap();
                if m.ends_with("/changes") {
                    let mut p = pages.lock().unwrap();
                    let r = p.remove(0);
                    if r["type"].is_string() { json!(["error", r, c[2]]) } else { json!([m, r, c[2]]) }
                } else {
                    let ids = c[1]["ids"].as_array().cloned().unwrap_or_default();
                    let list: Vec<Value> = ids.iter().filter_map(|i| objects.get(i.as_str().unwrap()).cloned()).collect();
                    json!([m, {"accountId": c[1]["accountId"], "state": "x", "list": list}, c[2]])
                }
            }).collect())
        })
    }

    #[test]
    fn delta_pages_through_changes_and_fetches_the_rest_with_the_views_properties() {
        let log = Arc::new(Mutex::new(vec![]));
        let c = server(vec![
            json!({"oldState": "s1", "newState": "s2", "hasMoreChanges": true, "created": ["c1"], "updated": [], "destroyed": ["d1"]}),
            json!({"oldState": "s2", "newState": "s3", "hasMoreChanges": false, "created": [], "updated": ["u1"], "destroyed": []}),
        ], json!({"c1": {"id": "c1", "title": "a"}, "u1": {"id": "u1", "title": "b"}}), log.clone());
        let props = json!(["id", "title"]);
        let d = delta(&c, "CalendarEvent", "A", "s1", Some(&props)).unwrap().unwrap();
        assert_eq!(d.state, "s3");
        assert_eq!(d.destroyed, vec!["d1"]);
        assert_eq!(d.fetched.len(), 2);
        let log = log.lock().unwrap();
        assert_eq!(log[0], json!(["CalendarEvent/changes", {"accountId": "A", "sinceState": "s1", "maxChanges": 500}, "c"]));
        assert_eq!(log[1][1]["sinceState"], "s2");
        let get = log.iter().find(|c| c[0] == "CalendarEvent/get").unwrap();
        assert_eq!(get[1]["ids"], json!(["c1", "u1"]));
        assert_eq!(get[1]["properties"], props);
    }

    #[test]
    fn cannot_calculate_changes_asks_for_the_full_load() {
        let log = Arc::new(Mutex::new(vec![]));
        let c = server(vec![json!({"type": "cannotCalculateChanges"})], json!({}), log.clone());
        assert_eq!(delta(&c, "ContactCard", "A", "s1", None).unwrap(), None);
        // and through the view runner: a full load, not an empty apply
        let log2 = Arc::new(Mutex::new(vec![]));
        let c = server(vec![json!({"type": "cannotCalculateChanges"})], json!({}), log2);
        assert!(matches!(run(&c, &[st("ContactCard", "A", "s1")], |_| None), Outcome::Full));
        assert!(log.lock().unwrap().iter().all(|c| c[0] != "ContactCard/get"), "nothing is fetched");
    }

    #[test]
    fn an_unchanged_state_applies_nothing() {
        let c = server(vec![json!({"oldState": "s1", "newState": "s1", "hasMoreChanges": false, "created": [], "updated": [], "destroyed": []})], json!({}), Arc::new(Mutex::new(vec![])));
        assert!(matches!(run(&c, &[st("FileNode", "A", "s1")], |_| None), Outcome::Nothing));
        let c = server(vec![json!({"oldState": "s1", "newState": "s2", "hasMoreChanges": false, "created": [], "updated": [], "destroyed": ["x"]})], json!({}), Arc::new(Mutex::new(vec![])));
        assert!(matches!(run(&c, &[st("FileNode", "A", "s1")], |_| None), Outcome::Apply(_)));
    }

    #[test]
    fn endless_changes_fall_back_to_the_full_load() {
        let more = json!({"oldState": "s", "newState": "s", "hasMoreChanges": true, "created": [], "updated": [], "destroyed": []});
        let c = server(vec![more; MAX_PAGES], json!({}), Arc::new(Mutex::new(vec![])));
        assert_eq!(delta(&c, "FileNode", "A", "s", None).unwrap(), None);
    }

    #[test]
    fn pushed_types_take_mail_from_the_mail_account_and_pim_types_from_any() {
        let ch = json!({"@type": "StateChange", "changed": {
            "A": {"Email": "e2", "Identity": "i2"},
            "B": {"Email": "x", "CalendarEvent": "c2", "FileNode": "f2"},
            "C": {"ShareNotification": "n1", "Mailbox": "m"}}});
        let mut got: Vec<String> = pushed_types(&ch, "A").into_iter().collect();
        got.sort();
        assert_eq!(got, ["CalendarEvent", "Email", "FileNode", "Identity", "ShareNotification"]);
        assert!(pushed_types(&json!({"changed": {}}), "A").is_empty());
    }

    #[test]
    fn state_of_reads_the_state_without_objects() {
        let log = Arc::new(Mutex::new(vec![]));
        let c = server(vec![], json!({}), log.clone());
        assert_eq!(state_of(&c, "ContactCard", "A").as_deref(), Some("x"));
        assert_eq!(log.lock().unwrap()[0], json!(["ContactCard/get", {"accountId": "A", "ids": [], "properties": ["id"]}, "s"]));
    }
}
