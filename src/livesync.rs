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
//! loaded for ([`Ledger`]); an account whose read failed records nothing and is not followed. A
//! delta runs for exactly the recorded accounts and is applied only while the session is still
//! that login's and the view still holds the states it started from. Losing that race to a full
//! load runs the delta again from the new states; a delta that asks for the full load keeps the
//! view busy until the load lands, and pushes meanwhile run once after it.
use crate::jmap::Client;
use crate::AppWindow;
use serde_json::{json, Value};
use slint::ComponentHandle;
use std::collections::{HashMap, HashSet};
use std::sync::{Arc, Mutex, OnceLock};
use std::time::{Duration, Instant};

/// A view that follows push with deltas.
#[derive(Clone, Copy, PartialEq, Eq, Hash, Debug)]
pub enum View { Calendar, Contacts, Files, Identities }

/// (type, account, since) of one delta.
pub type Step = (String, String, String);
/// A delta's result: (type, account, since, what changed).
pub type Applied = (String, String, String, Delta);

/// What a view was loaded at: its login ([`Client::state_key`]), the state of each
/// (type, account) it read, whether that read was whole (nothing failed or was left out) and
/// whether a delta since then found that its changes cannot be read.
#[derive(Default, Debug)]
struct Book { owner: String, states: HashMap<(String, String), String>, complete: bool, unfollowed: bool }

/// A full load a delta asked for that has not landed after this long no longer holds the view.
const LOAD_STALE: Duration = Duration::from_secs(90);

/// The views' recorded states, and which deltas are running.
#[derive(Default, Debug)]
pub struct Ledger {
    books: HashMap<View, Book>,
    running: HashSet<View>,
    again: HashSet<View>,
    /// Views waiting for the full load a delta asked for (since when).
    loading: HashMap<View, Instant>,
    /// Views whose push type the server refused: nothing keeps them fresh this session, however
    /// they were loaded (cleared by a sign-out or another account).
    unfollowable: HashSet<View>,
    /// The newest full load of each view: a load that lands or fails after a newer one started
    /// changes nothing.
    loads: HashMap<View, u64>,
}

/// What to do once a delta's outcome is in (see [`Ledger::settle`]).
#[derive(Debug, Default, PartialEq)]
pub struct Settle { pub full: bool, pub apply: bool, pub rerun: bool }

/// Whether a load's result may be drawn or must be dropped (see [`Ledger::landing`]).
#[derive(Clone, Copy, Debug, PartialEq)]
pub enum Landing { Draw, Drop }

impl Ledger {
    /// A full load of `view` starts: its number. A result is applied only while it is the newest
    /// load of its view, so a slow load that lands — or errors — after a newer one changes nothing.
    pub fn begin_load(&mut self, view: View) -> u64 {
        let n = self.loads.get(&view).copied().unwrap_or(0).wrapping_add(1);
        self.loads.insert(view, n);
        n
    }

    /// Whether load `gen` of `view` may install its data: only when its login is still the current
    /// one (`current`) and no newer load of the view has started since. An overtaken load is
    /// dropped whole — installing it would show data that the recorded newer states do not
    /// describe, and deltas would then miss the difference for good.
    pub fn landing(&self, view: View, gen: u64, current: bool) -> Landing {
        if current && self.newest_load(view, gen) { Landing::Draw } else { Landing::Drop }
    }

    /// Whether load `gen` is still the newest one of `view`.
    pub fn newest_load(&self, view: View, gen: u64) -> bool { self.loads.get(&view).copied() == Some(gen) }

    /// A full load of `view` for `owner`, number `gen`, finished: it now holds exactly these
    /// states, and whether the read was whole (every account it lists was read and gave its
    /// states). A load that a newer one has overtaken changes nothing and returns `false`.
    pub fn loaded(&mut self, view: View, owner: &str, states: &[Step], complete: bool, gen: u64) -> bool {
        if !self.newest_load(view, gen) { return false; }
        let states = states.iter().map(|(t, a, s)| ((t.clone(), a.clone()), s.clone())).collect();
        self.books.insert(view, Book { owner: owner.to_string(), states, complete, unfollowed: false });
        true
    }

    /// The state `view` holds `ty` of `account` at, when it holds `owner`'s data.
    #[cfg(test)]
    pub fn since(&self, view: View, owner: &str, ty: &str, account: &str) -> Option<String> {
        let b = self.books.get(&view).filter(|b| b.owner == owner)?;
        b.states.get(&(ty.to_string(), account.to_string())).cloned()
    }

    /// The deltas `view` needs: one per recorded (type, account), when it holds `owner`'s data.
    pub fn steps(&self, view: View, owner: &str) -> Option<Vec<Step>> {
        let b = self.books.get(&view).filter(|b| b.owner == owner && !b.states.is_empty())?;
        let mut v: Vec<Step> = b.states.iter().map(|((t, a), s)| (t.clone(), a.clone(), s.clone())).collect();
        v.sort();
        Some(v)
    }

    /// Whose data `view` holds ("" none).
    pub fn owner(&self, view: View) -> String { self.books.get(&view).map(|b| b.owner.clone()).unwrap_or_default() }

    /// What livesync can say about keeping `view` fresh while it holds `owner`'s data.
    pub fn health(&self, view: View, owner: &str) -> Health {
        let Some(b) = self.books.get(&view).filter(|b| b.owner == owner) else { return Health::default() };
        Health { states: !b.states.is_empty(), complete: b.complete, followed: !b.unfollowed && !self.unfollowable.contains(&view) }
    }

    /// A delta found that the view's changes cannot be read from the server: push can no longer
    /// keep this load fresh, so the next visit loads it again (a later load may work).
    pub fn unfollow(&mut self, view: View) {
        if let Some(b) = self.books.get_mut(&view) { b.unfollowed = true; }
    }

    /// The server refused a push type of `view` before the stream opened: nothing keeps it fresh
    /// for the rest of the session, however it is loaded.
    pub fn unfollow_for_the_session(&mut self, view: View) { self.unfollowable.insert(view); }

    /// A full load of `view` failed. What the view holds stays (pushes keep following it from its
    /// recorded states), but it is no longer reused: the next visit loads it in full. A failure
    /// that arrives after a newer load started says nothing about that one and changes nothing.
    pub fn load_failed(&mut self, view: View, owner: &str, gen: u64) {
        if !self.newest_load(view, gen) { return; }
        if let Some(b) = self.books.get_mut(&view).filter(|b| b.owner == owner) { b.complete = false; }
    }

    /// A login attempt ended: `Some(owner)` is the login that came up, so it takes the views over
    /// (the recorded states are the previous login's and are dropped). `None` is a failed attempt,
    /// which leaves the login that is still signed in — and with it its views — as it was.
    /// Returns whether the views were dropped.
    pub fn session_takeover(&mut self, owner: Option<&str>) -> bool {
        match owner {
            Some(_) => { self.clear(); true }
            None => false,
        }
    }

    /// Every view forgets what it holds (sign-out, another account).
    pub fn clear(&mut self) { *self = Ledger::default(); }

    /// Move `view` on to the new states of a delta, all or nothing: `false` (and nothing changes)
    /// when it no longer holds `owner`'s data or any state is not the one the delta started from.
    pub fn advance(&mut self, view: View, owner: &str, steps: &[(String, String, String, String)]) -> bool {
        let Some(b) = self.books.get_mut(&view).filter(|b| b.owner == owner) else { return false };
        if !steps.iter().all(|(t, a, since, _)| b.states.get(&(t.clone(), a.clone())) == Some(since)) { return false; }
        for (t, a, _, new) in steps { b.states.insert((t.clone(), a.clone()), new.clone()); }
        true
    }

    /// The view's data is gone: no delta applies to it any more.
    #[cfg(test)]
    pub fn forget(&mut self, view: View) { self.books.remove(&view); }

    /// A delta of `view` may start; `false` when one runs or its full load has not landed (it
    /// runs again once that ends, so a push that came in meanwhile is not lost and two deltas
    /// never race from one state). A full load that never landed stops holding it after a while.
    pub fn begin(&mut self, view: View, now: Instant) -> bool {
        if self.loading.get(&view).is_some_and(|t| now.duration_since(*t) > LOAD_STALE) {
            self.loading.remove(&view);
            self.running.remove(&view);
        }
        if self.running.insert(view) { return true; }
        self.again.insert(view);
        false
    }

    /// The delta of `view` ended; `true` when another was asked for meanwhile.
    pub fn finish(&mut self, view: View) -> bool {
        self.running.remove(&view);
        self.loading.remove(&view);
        self.again.remove(&view)
    }

    /// A delta's outcome is in. `current`: the session is still `owner`'s. Applies only while the
    /// view holds `owner`'s data at the delta's starting states; losing to a full load that landed
    /// meanwhile reruns it from the new states; asking for the full load keeps the view busy.
    pub fn settle(&mut self, view: View, owner: &str, current: bool, outcome: &Outcome, now: Instant) -> Settle {
        let holds = current && self.owner(view) == owner;
        let mut s = Settle::default();
        if holds {
            match outcome {
                Outcome::Full => { self.loading.insert(view, now); s.full = true; return s; }
                Outcome::Nothing => {}
                // the changes could not be read: push no longer keeps the view fresh, the next
                // visit loads it in full ("opening the view catches up")
                Outcome::Failed => self.unfollow(view),
                Outcome::Apply(ds) => {
                    let moves: Vec<(String, String, String, String)> = ds.iter().map(|(t, a, f, d)| (t.clone(), a.clone(), f.clone(), d.state.clone())).collect();
                    if self.advance(view, owner, &moves) { s.apply = true; } else { s.rerun = true; }
                }
            }
        }
        if self.finish(view) { s.rerun = true; }
        s
    }
}

static LEDGER: Mutex<Option<Ledger>> = Mutex::new(None);

fn with<T>(f: impl FnOnce(&mut Ledger) -> T) -> T {
    let mut g = LEDGER.lock().unwrap_or_else(|e| e.into_inner());
    f(g.get_or_insert_with(Ledger::default))
}

// ---------------- the session ----------------

/// The signed-in session; a delta, a pull or a toast of another login does nothing.
static CURRENT: Mutex<Option<Arc<Client>>> = Mutex::new(None);

/// A session starts (sign-in, account switch) or ends (`None`).
pub fn set_current(c: Option<Arc<Client>>) { *CURRENT.lock().unwrap_or_else(|e| e.into_inner()) = c; }
pub fn current() -> Option<Arc<Client>> { CURRENT.lock().unwrap_or_else(|e| e.into_inner()).clone() }
/// The current session's [`Client::state_key`].
pub fn current_key() -> Option<String> { current().map(|c| c.state_key()) }
pub fn is_current(owner: &str) -> bool { current_key().as_deref() == Some(owner) }

/// Whether a view that holds `owner`'s data is still current for the session `current`: a switch
/// to it can render from the cache and rely on push to keep it fresh, but another login's data
/// (or an empty view) needs a full load.
pub fn holds(owner: &str, current: Option<&str>) -> bool {
    !owner.is_empty() && current == Some(owner)
}

/// Whether livesync keeps a loaded view fresh: the load recorded the states of what it read
/// (a failed `state_of` records none and the view is not followed), the read was whole (no
/// account failed or was left out), and no delta since found that its changes cannot be read.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct Health { pub states: bool, pub complete: bool, pub followed: bool }

impl Health {
    /// Every part is in place: the view is followed and complete.
    pub fn ok(&self) -> bool { self.states && self.complete && self.followed }
}

/// Whether a view may be shown from its cache on a visit instead of being loaded again: it holds
/// the session's data, and push can keep it fresh ([`Health`]). Anything less loads as before,
/// which is also what catches a view up after a failed read or a delta that could not run.
pub fn can_reuse(owner: &str, current: Option<&str>, health: Health) -> bool {
    holds(owner, current) && health.ok()
}

/// Whether `view` already holds the current session's data and is kept fresh by push (so a
/// switch to it need not reload).
pub fn cache_current(view: View, c: &Client) -> bool {
    let key = c.state_key();
    let (book, health) = with(|l| (l.owner(view), l.health(view, &key)));
    is_current(&key) && can_reuse(&book, Some(&key), health)
}

/// Whether the current session's `view` is loaded and followed: another view that reads the same
/// data (the birthday calendar and the contacts) may rely on push to keep it fresh.
pub fn followed(view: View) -> bool {
    let Some(key) = current_key() else { return false };
    let (book, health) = with(|l| (l.owner(view), l.health(view, &key)));
    can_reuse(&book, Some(&key), health)
}

/// A full load of `view` for `owner` started: its number, to pass to [`loaded`],
/// [`load_failed`] and [`landing`].
pub fn begin_load(view: View) -> u64 { with(|l| l.begin_load(view)) }

/// Whether the load numbered `gen` for `owner` may install what it read. Checked before anything
/// is put on screen (`is_current` plus the newest load of the view), so a load that another login
/// or a newer load has overtaken installs nothing and records nothing.
pub fn landing(view: View, owner: &str, gen: u64) -> bool {
    with(|l| l.landing(view, gen, is_current(owner))) == Landing::Draw
}

/// The newest load of `view` for `owner` failed (UI thread). What the view holds stays and pushes
/// keep following it, but it is no longer reused: the next visit loads it in full. A failure for a
/// login that is no longer current, or one a newer load has overtaken, changes nothing.
pub fn load_failed(view: View, owner: &str, gen: u64) {
    if !is_current(owner) { return; }
    with(|l| l.load_failed(view, owner, gen));
}

/// `view`'s changes cannot be read from the server (a refused push type): nothing keeps it fresh
/// for the rest of the session, so every visit loads it in full.
pub fn unfollow(view: View) { with(|l| l.unfollow_for_the_session(view)) }

/// A login came up (sign-in, account switch): it takes the views over — the recorded states are
/// the login that left's, so nothing of them is reused. Called only once the attempt succeeded, so
/// a failed switch (B unreachable, a changed password) leaves the session that is still signed in,
/// with its views, exactly as it was. The notifications and identities kept for the bell stay.
pub fn start_session(c: Arc<Client>) {
    let key = c.state_key();
    with(|l| { l.session_takeover(Some(&key)); });
    set_current(Some(c));
    // the birthday cards are the login that left's: cleared (and their revision moved on, so a
    // read still in flight cannot put them back)
    crate::cal_extra::forget_people();
}

/// A full load of `view` for `owner` landed (UI thread): record its states and whether the read
/// was whole, tell the notifications waiting for that list, and run a delta asked for while it
/// loaded.
pub fn loaded(u: &AppWindow, view: View, owner: &str, states: &[Step], complete: bool, gen: u64) {
    // a load for a login that is no longer the current one, or one a newer load has overtaken:
    // nothing of it is recorded (the loaders do not install its data either)
    if !is_current(owner) { return; }
    let again = with(|l| {
        // a load a newer one has overtaken changes nothing; either way it no longer holds the view
        let seen = l.newest_load(view, gen);
        if seen { l.loaded(view, owner, states, complete, gen); }
        if seen { l.finish(view) } else { false }
    });
    crate::notices::landed(u, view);
    if again { if let Some(c) = current() { follow(view, u.as_weak(), c); } }
}

/// Whose data `view` holds ("" none).
pub fn owner(view: View) -> String { with(|l| l.owner(view)) }

/// Data types the app follows over push (RFC 8620 §7.3 `types`).
const PUSH_TYPES: [&str; 5] = ["Email", "Mailbox", "Thread", "EmailDelivery", "EmailSubmission"];

/// The push types of a login: mail and identities, calendar and contacts objects where it has
/// them. `more` adds files and the server's notifications; a stream that does not open with
/// them is opened again without them (see [`retry_without_more`]).
pub fn push_types(c: &Client, more: bool) -> Vec<&'static str> {
    let mut v = PUSH_TYPES.to_vec();
    if c.has("urn:ietf:params:jmap:submission") { v.push("Identity"); }
    if c.has(crate::calendar::CAL) { v.extend(["CalendarEvent", "Calendar"]); }
    if c.has("urn:ietf:params:jmap:contacts") { v.extend(["ContactCard", "AddressBook"]); }
    if more {
        if c.has("urn:ietf:params:jmap:filenode") { v.push("FileNode"); }
        if c.has("urn:ietf:params:jmap:principals") { v.push("ShareNotification"); }
        if c.has(crate::calendar::CAL) { v.push("CalendarEventNotification"); }
    }
    v
}

/// A stream asked with the newer types that the server refused (a 4xx answer) before it opened,
/// but not a refused sign-in: a server that does not know a type turns the request down. A
/// transport error (sleep, a Wi-Fi drop) or a server error says nothing about the types, so the
/// next reconnect keeps them.
pub fn retry_without_more(more: bool, opened: bool, err: Option<&str>) -> bool {
    more && !opened && err.is_some_and(|e| e.starts_with("Push failed (4") && !crate::jmap::auth_rejected(e))
}

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

/// Sign-out: every view forgets its states, deltas stop, the session and its identities and
/// notifications go.
pub fn forget_all() {
    with(|l| l.clear());
    set_current(None);
    // another login's birthdays must not stay on the calendar
    crate::cal_extra::forget_people();
    *IDENTITIES.lock().unwrap_or_else(|e| e.into_inner()) = (String::new(), Vec::new());
    crate::notices::forget_session();
}

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

/// The outcome of a view's deltas.
#[derive(Debug)]
pub enum Outcome { Full, Nothing, Failed, Apply(Vec<Applied>) }

/// Run the deltas of `steps` (type, account, since) with each type's properties.
fn run(c: &Client, steps: &[Step], props: impl Fn(&str) -> Option<Value>) -> Outcome {
    let mut out = vec![];
    for (ty, acc, since) in steps {
        match delta(c, ty, acc, since, props(ty).as_ref()) {
            Ok(Some(d)) => out.push((ty.clone(), acc.clone(), since.clone(), d)),
            Ok(None) => return Outcome::Full,
            // offline or a refused request: push cannot vouch for the view any more, so the next
            // visit loads it (see Ledger::settle)
            Err(_) => return Outcome::Failed,
        }
    }
    if out.iter().all(|(_, _, since, d)| d.state == *since && d.destroyed.is_empty() && d.fetched.is_empty()) { return Outcome::Nothing; }
    Outcome::Apply(out)
}

/// How a view is followed: its properties per type, what its delta needs from the worker thread
/// (`extra`, e.g. settings read off the UI thread), its full load and its delta's application.
struct Spec {
    props: fn(&str) -> Option<Value>,
    extra: fn(&Client) -> Value,
    full: fn(&AppWindow, Arc<Client>),
    apply: fn(&AppWindow, &Arc<Client>, &[Applied], &Value),
}

fn spec(view: View) -> Spec {
    match view {
        View::Calendar => Spec {
            props: |ty| Some(if ty == "Calendar" { json!(crate::calendar::CAL_PROPS.to_vec()) } else { json!(crate::calendar::EVENT_PROPS.to_vec()) }),
            extra: |_| crate::settings::snapshot().get("sharedCalendarColors").cloned().unwrap_or(json!({})),
            full: |u, c| crate::calendar::load(u.as_weak(), c),
            apply: |u, c, ds, colors| crate::calendar::apply_live(u, c, ds, colors),
        },
        View::Contacts => Spec {
            props: |_| None, extra: |_| Value::Null,
            full: |u, c| crate::contacts::load(u.as_weak(), c),
            apply: |u, _, ds, _| crate::contacts::apply_live(u, ds),
        },
        View::Files => Spec {
            props: |_| Some(json!(crate::files::PROPS.to_vec())), extra: |_| Value::Null,
            full: |u, c| crate::files::reload(u, c),
            apply: |u, _, ds, _| crate::files::apply_live(u, ds),
        },
        View::Identities => Spec {
            props: |_| None, extra: |_| Value::Null,
            full: |u, c| {
                let w = u.as_weak();
                let gen = begin_load(View::Identities);
                std::thread::spawn(move || {
                    let r = load_identities(&c);
                    let _ = w.upgrade_in_event_loop(move |u| match r {
                        Ok((list, state)) => identities_loaded(&u, &c, list, state, gen),
                        // nothing landed: the view is free for the next push
                        Err(_) => { if with(|l| l.finish(View::Identities)) { if let Some(c) = current() { follow(View::Identities, u.as_weak(), c); } } }
                    });
                });
            },
            apply: |u, c, ds, _| {
                let owner = c.state_key();
                let list = {
                    let mut g = IDENTITIES.lock().unwrap_or_else(|e| e.into_inner());
                    if g.0 != owner { return; }
                    for (_, acc, _, d) in ds { apply(&mut g.1, acc, d, |x| (acc.clone(), x["id"].as_str().unwrap_or("").to_string()), |v| v); }
                    g.1.clone()
                };
                identities_changed(u, c, list);
            },
        },
    }
}

/// One view's push: the deltas of its recorded (type, account) states on a thread, then on the UI
/// thread the full load or the delta's application, as [`Ledger::settle`] decides. Reruns use the
/// session current at that moment.
fn follow(view: View, w: slint::Weak<AppWindow>, c: Arc<Client>) {
    let owner = c.state_key();
    if !is_current(&owner) { return; }
    let Some(steps) = with(|l| l.steps(view, &owner)) else { return };
    if !with(|l| l.begin(view, Instant::now())) { return; }
    std::thread::spawn(move || {
        let sp = spec(view);
        let outcome = run(&c, &steps, sp.props);
        let extra = if matches!(outcome, Outcome::Apply(_)) { (sp.extra)(&c) } else { Value::Null };
        let _ = w.upgrade_in_event_loop(move |u| {
            let s = with(|l| l.settle(view, &owner, is_current(&owner), &outcome, Instant::now()));
            if s.full { (sp.full)(&u, c.clone()); }
            if s.apply { if let Outcome::Apply(ds) = &outcome { (sp.apply)(&u, &c, ds, &extra); } }
            if s.rerun { if let Some(c) = current() { follow(view, u.as_weak(), c); } }
        });
    });
}

// ---------------- the views ----------------

/// Calendar and CalendarEvent of every calendar account the view read.
pub fn calendar(w: slint::Weak<AppWindow>, c: Arc<Client>) { follow(View::Calendar, w, c); }

/// AddressBook and ContactCard of the contacts account.
pub fn contacts(w: slint::Weak<AppWindow>, c: Arc<Client>) { follow(View::Contacts, w, c); }

/// FileNode of every account with file storage the view read.
pub fn files(w: slint::Weak<AppWindow>, c: Arc<Client>) { follow(View::Files, w, c); }

/// Identity push: the delta is applied to the login's list, which then goes to the open
/// settings page (when it shows this login) and to the own-address checks.
pub fn identities(w: slint::Weak<AppWindow>, c: Arc<Client>) { follow(View::Identities, w, c); }

// ---------------- identities ----------------

/// The signed-in login's identities as last read, for the "is this me" checks and the settings
/// pages (Identity/changes keeps it current).
static IDENTITIES: Mutex<(String, Vec<Value>)> = Mutex::new((String::new(), Vec::new()));
static SETTINGS: OnceLock<crate::server_settings::Shared> = OnceLock::new();

/// The settings pages' cache, so an identity change reaches an open Identities page.
pub fn set_settings_cache(srv: crate::server_settings::Shared) { let _ = SETTINGS.set(srv); }

/// Identity/get of every identity, and its state for later deltas.
pub fn load_identities(c: &Client) -> Result<(Vec<Value>, Option<String>), String> {
    let acc = crate::identities::account(c);
    let r = c.call(json!([["Identity/get", {"accountId": acc, "ids": null}, "i"]]))?;
    Ok((r[0][1]["list"].as_array().cloned().unwrap_or_default(), r[0][1]["state"].as_str().map(String::from)))
}

/// Identities read for `c` landed (UI thread): kept, recorded and shown, unless the session is no
/// longer `c`'s (or a newer load has overtaken it).
pub fn identities_loaded(u: &AppWindow, c: &Arc<Client>, list: Vec<Value>, state: Option<String>, gen: u64) {
    let owner = c.state_key();
    if !is_current(&owner) { return; }
    let acc = crate::identities::account(c);
    *IDENTITIES.lock().unwrap_or_else(|e| e.into_inner()) = (owner.clone(), list.clone());
    let states: Vec<Step> = state.into_iter().map(|s| ("Identity".to_string(), acc.clone(), s)).collect();
    loaded(u, View::Identities, &owner, &states, !states.is_empty(), gen);
    identities_changed(u, c, list);
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

    /// A whole load of `view` for `owner` lands, as its own (newest) revision.
    fn load(l: &mut Ledger, v: View, o: &str, s: &[Step], complete: bool) {
        let gen = l.begin_load(v);
        assert!(l.loaded(v, o, s, complete, gen), "the newest load is applied");
    }

    #[test]
    fn a_cached_view_is_reused_only_when_it_is_followed_and_complete() {
        let h = |states, complete, followed| Health { states, complete, followed };
        assert!(can_reuse("ann", Some("ann"), h(true, true, true)));
        // another login's data, or none at all
        assert!(!can_reuse("ann", Some("bob"), h(true, true, true)));
        assert!(!can_reuse("", Some("ann"), h(true, true, true)));
        assert!(!can_reuse("ann", None, h(true, true, true)));
        // a failed state_of records no state: nothing to run a delta from
        assert!(!can_reuse("ann", Some("ann"), h(false, true, true)));
        // an account that failed or was skipped leaves the read incomplete
        assert!(!can_reuse("ann", Some("ann"), h(true, false, true)));
        // a delta that could not be read, or a refused push type: push cannot vouch for the view
        assert!(!can_reuse("ann", Some("ann"), h(true, true, false)));
    }

    #[test]
    fn a_delta_that_could_not_be_read_or_a_failed_load_leaves_nothing_to_reuse() {
        let now = Instant::now();
        let mut l = Ledger::default();
        load(&mut l, View::Files, "ann", &[st("FileNode", "A", "f1")], true);
        assert!(l.health(View::Files, "ann").ok());
        // the changes could not be read (offline, or the server refused): the view is not followed
        assert!(l.begin(View::Files, now));
        assert_eq!(l.settle(View::Files, "ann", true, &Outcome::Failed, now), Settle::default());
        assert_eq!(l.health(View::Files, "ann"), Health { states: true, complete: true, followed: false });
        // a whole load lands again and it may be reused
        load(&mut l, View::Files, "ann", &[st("FileNode", "A", "f2")], true);
        assert!(l.health(View::Files, "ann").ok());
        // a load that read one account and skipped another (or lost its state) is not complete
        load(&mut l, View::Files, "ann", &[st("FileNode", "A", "f3")], false);
        assert!(!l.health(View::Files, "ann").ok());
        // a load that recorded no state at all cannot be followed
        load(&mut l, View::Contacts, "ann", &[], true);
        assert_eq!(l.health(View::Contacts, "ann"), Health { states: false, complete: true, followed: true });
    }

    #[test]
    fn a_refused_push_type_keeps_every_load_of_that_view_unfollowed() {
        let mut l = Ledger::default();
        l.unfollow_for_the_session(View::Files);
        // a whole load read afterwards cannot be trusted to stay fresh either
        load(&mut l, View::Files, "ann", &[st("FileNode", "A", "f1")], true);
        assert!(!l.health(View::Files, "ann").followed);
        // the other views are untouched
        load(&mut l, View::Calendar, "ann", &[st("CalendarEvent", "A", "e1")], true);
        assert!(l.health(View::Calendar, "ann").ok());
        // a sign-out or another account starts over
        l.clear();
        load(&mut l, View::Files, "ann", &[st("FileNode", "A", "f1")], true);
        assert!(l.health(View::Files, "ann").ok());
    }

    #[test]
    fn a_failed_read_and_a_switch_leave_nothing_in_the_cache() {
        let mut l = Ledger::default();
        let gen = l.begin_load(View::Calendar);
        assert!(l.loaded(View::Calendar, "ann", &[st("CalendarEvent", "A", "e1")], true, gen));
        assert!(can_reuse(&l.owner(View::Calendar), Some("ann"), l.health(View::Calendar, "ann")));
        // the read failed: what the view holds is not reused, though push can still follow it
        l.load_failed(View::Calendar, "ann", gen);
        assert!(!can_reuse(&l.owner(View::Calendar), Some("ann"), l.health(View::Calendar, "ann")));
        assert_eq!(l.owner(View::Calendar), "ann", "what it holds is kept");
        assert!(l.steps(View::Calendar, "ann").is_some(), "pushes still run from its states");
        // a newer load puts it back
        load(&mut l, View::Calendar, "ann", &[st("CalendarEvent", "A", "e2")], true);
        assert!(l.health(View::Calendar, "ann").ok());
        // A to B to A: the switch clears the ledger, so A's states from before are not reused
        load(&mut l, View::Calendar, "ann", &[st("CalendarEvent", "A", "e1")], true);
        load(&mut l, View::Files, "ann", &[st("FileNode", "A", "f1")], true);
        assert!(l.session_takeover(Some("bob")));
        for v in [View::Calendar, View::Files] {
            assert_eq!(l.owner(v), "");
            assert!(!can_reuse(&l.owner(v), Some("ann"), l.health(v, "ann")), "{v:?} is loaded again after a switch");
        }
    }

    #[test]
    fn a_switch_that_fails_keeps_the_login_and_its_views() {
        let mut l = Ledger::default();
        load(&mut l, View::Calendar, "A", &[st("CalendarEvent", "x", "e1")], true);
        // the attempt failed (B unreachable, a changed password): livesync is never told a session
        // started, so nothing is dropped and the login that is still signed in keeps its views —
        // its pushes and its loads still count as current
        assert!(!l.session_takeover(None));
        assert_eq!(l.owner(View::Calendar), "A");
        assert!(can_reuse(&l.owner(View::Calendar), Some("A"), l.health(View::Calendar, "A")));
        // the switch that comes up takes the views over: nothing of A's is reused for B
        assert!(l.session_takeover(Some("B")));
        assert_eq!(l.owner(View::Calendar), "");
        assert!(!can_reuse(&l.owner(View::Calendar), Some("B"), l.health(View::Calendar, "B")));
    }

    #[test]
    fn a_load_that_arrives_after_a_newer_one_changes_nothing() {
        let mut l = Ledger::default();
        let older = l.begin_load(View::Files);
        let newer = l.begin_load(View::Files);
        assert!(!l.newest_load(View::Files, older));
        assert!(l.newest_load(View::Files, newer));
        // the newer load lands; the older one then errors: its failure says nothing about this one
        assert!(l.loaded(View::Files, "ann", &[st("FileNode", "A", "f2")], true, newer));
        l.load_failed(View::Files, "ann", older);
        assert!(l.health(View::Files, "ann").ok(), "the newer load is untouched");
        // the older one landing late is refused: it does not replace the newer states
        assert!(!l.loaded(View::Files, "ann", &[st("FileNode", "A", "f1")], true, older));
        assert_eq!(l.since(View::Files, "ann", "FileNode", "A").as_deref(), Some("f2"));
        // another login's failure at the newest revision is not theirs to mark
        l.load_failed(View::Files, "bob", newer);
        assert!(l.health(View::Files, "ann").ok());
    }

    #[test]
    fn an_overtaken_load_installs_nothing_and_keeps_the_newer_data() {
        let mut l = Ledger::default();
        let older = l.begin_load(View::Calendar);
        let newer = l.begin_load(View::Calendar);
        // the newer load lands and installs
        assert_eq!(l.landing(View::Calendar, newer, true), Landing::Draw);
        assert!(l.loaded(View::Calendar, "ann", &[st("CalendarEvent", "A", "e2")], true, newer));
        // the older one arrives afterwards: dropped whole, before it could install anything
        assert_eq!(l.landing(View::Calendar, older, true), Landing::Drop);
        assert_eq!(l.since(View::Calendar, "ann", "CalendarEvent", "A").as_deref(), Some("e2"));
        // and its error cannot mark the view either
        l.load_failed(View::Calendar, "ann", older);
        assert!(l.health(View::Calendar, "ann").ok());
        // a load for a login that is no longer current is dropped, newest or not
        let third = l.begin_load(View::Calendar);
        assert_eq!(l.landing(View::Calendar, third, false), Landing::Drop);
        assert_eq!(l.landing(View::Calendar, third, true), Landing::Draw);
    }

    #[test]
    fn a_view_is_current_only_when_it_holds_the_sessions_data() {
        assert!(!holds("", Some("ann")));
        assert!(!holds("", None));
        assert!(holds("ann", Some("ann")));
        assert!(!holds("ann", Some("bob")));
        assert!(!holds("ann", None));
        let mut l = Ledger::default();
        load(&mut l, View::Contacts, "ann", &[], true);
        assert_eq!(l.owner(View::Contacts), "ann");
        assert!(holds(&l.owner(View::Contacts), Some("ann")));
        assert!(!holds(&l.owner(View::Contacts), Some("bob")), "another login's data is not current");
    }

    #[test]
    fn states_are_kept_per_view_type_account_and_login() {
        let mut l = Ledger::default();
        load(&mut l, View::Calendar, "srv\nann\nA", &[st("CalendarEvent", "A", "s1"), st("CalendarEvent", "B", "t1"), st("Calendar", "A", "c1")], true);
        load(&mut l, View::Contacts, "srv\nann\nA", &[st("ContactCard", "A", "k1")], true);
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
        load(&mut l, View::Files, "ann", &[st("FileNode", "A", "f1")], true);
        // bob's full load replaces the book; a delta ann computed meanwhile does not apply
        load(&mut l, View::Files, "bob", &[st("FileNode", "A", "g1")], true);
        assert!(!l.advance(View::Files, "ann", &[mv("FileNode", "A", "f1", "f2")]));
        assert_eq!(l.since(View::Files, "bob", "FileNode", "A").as_deref(), Some("g1"));
        assert_eq!(l.since(View::Files, "ann", "FileNode", "A"), None);
    }

    #[test]
    fn advance_is_all_or_nothing_and_needs_the_starting_state() {
        let mut l = Ledger::default();
        load(&mut l, View::Calendar, "o", &[st("Calendar", "A", "c1"), st("CalendarEvent", "A", "e1")], true);
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
        let now = Instant::now();
        assert!(l.begin(View::Contacts, now));
        assert!(!l.begin(View::Contacts, now));
        assert!(!l.begin(View::Contacts, now));
        assert!(l.begin(View::Files, now), "views run independently");
        assert!(l.finish(View::Contacts), "asked again while running");
        assert!(l.begin(View::Contacts, now));
        assert!(!l.finish(View::Contacts));
    }

    fn applied(t: &str, a: &str, since: &str, new: &str) -> Applied {
        (t.into(), a.into(), since.into(), Delta { state: new.into(), destroyed: vec!["x".into()], fetched: vec![] })
    }

    #[test]
    fn settle_applies_only_for_the_current_login_holding_the_starting_state() {
        let now = Instant::now();
        let mut l = Ledger::default();
        load(&mut l, View::Files, "ann", &[st("FileNode", "A", "f1")], true);
        let ok = Outcome::Apply(vec![applied("FileNode", "A", "f1", "f2")]);
        // the session switched to someone else meanwhile: dropped, no rerun, the view is free
        assert!(l.begin(View::Files, now));
        assert_eq!(l.settle(View::Files, "ann", false, &ok, now), Settle::default());
        assert_eq!(l.since(View::Files, "ann", "FileNode", "A").as_deref(), Some("f1"));
        // another login's full load landed: the view is not ann's any more
        load(&mut l, View::Files, "bob", &[st("FileNode", "A", "g1")], true);
        assert!(l.begin(View::Files, now));
        assert_eq!(l.settle(View::Files, "ann", true, &ok, now), Settle::default());
        // ann's again, current: applied and moved on
        load(&mut l, View::Files, "ann", &[st("FileNode", "A", "f1")], true);
        assert!(l.begin(View::Files, now));
        assert_eq!(l.settle(View::Files, "ann", true, &ok, now), Settle { apply: true, ..Default::default() });
        assert_eq!(l.since(View::Files, "ann", "FileNode", "A").as_deref(), Some("f2"));
    }

    #[test]
    fn a_delta_that_lost_to_a_full_load_runs_again_from_the_new_state() {
        let now = Instant::now();
        let mut l = Ledger::default();
        load(&mut l, View::Contacts, "ann", &[st("ContactCard", "A", "k1")], true);
        assert!(l.begin(View::Contacts, now));
        let steps = l.steps(View::Contacts, "ann").unwrap();
        // a full load (opening the view) lands while the delta from k1 runs
        load(&mut l, View::Contacts, "ann", &[st("ContactCard", "A", "k5")], true);
        let out = Outcome::Apply(vec![applied("ContactCard", "A", &steps[0].2, "k2")]);
        assert_eq!(l.settle(View::Contacts, "ann", true, &out, now), Settle { rerun: true, ..Default::default() });
        assert_eq!(l.steps(View::Contacts, "ann").unwrap(), vec![st("ContactCard", "A", "k5")], "the rerun starts from the new state");
    }

    #[test]
    fn a_full_load_asked_for_holds_the_view_until_it_lands() {
        let now = Instant::now();
        let mut l = Ledger::default();
        load(&mut l, View::Calendar, "ann", &[st("CalendarEvent", "A", "e1")], true);
        assert!(l.begin(View::Calendar, now));
        assert_eq!(l.settle(View::Calendar, "ann", true, &Outcome::Full, now), Settle { full: true, ..Default::default() });
        // pushes while it loads wait
        assert!(!l.begin(View::Calendar, now));
        assert!(!l.begin(View::Calendar, now + Duration::from_secs(5)));
        // it lands (livesync::loaded): recorded, and the push that waited runs once
        load(&mut l, View::Calendar, "ann", &[st("CalendarEvent", "A", "e9")], true);
        assert!(l.finish(View::Calendar));
        assert!(l.begin(View::Calendar, now));
        assert!(!l.finish(View::Calendar));
        // a load that never lands stops holding the view
        assert!(l.begin(View::Calendar, now));
        l.settle(View::Calendar, "ann", true, &Outcome::Full, now);
        assert!(!l.begin(View::Calendar, now + Duration::from_secs(10)));
        assert!(l.begin(View::Calendar, now + LOAD_STALE + Duration::from_secs(1)));
    }

    #[test]
    fn deltas_cover_exactly_the_accounts_the_load_recorded() {
        // a shared calendar account whose read failed (or that appeared later) has no state: the
        // delta runs for the others and never turns into a full load on every push
        let mut l = Ledger::default();
        load(&mut l, View::Calendar, "ann", &[st("Calendar", "A", "c1"), st("CalendarEvent", "A", "e1")], true);
        assert_eq!(l.steps(View::Calendar, "ann").unwrap(), vec![st("Calendar", "A", "c1"), st("CalendarEvent", "A", "e1")]);
        assert_eq!(l.steps(View::Calendar, "bob"), None);
        // a load that read nothing follows nothing
        load(&mut l, View::Files, "ann", &[], true);
        assert_eq!(l.steps(View::Files, "ann"), None);
        let c = server(vec![json!({"oldState": "c1", "newState": "c1", "hasMoreChanges": false, "created": [], "updated": [], "destroyed": []}),
            json!({"oldState": "e1", "newState": "e1", "hasMoreChanges": false, "created": [], "updated": [], "destroyed": []})], json!({}), Arc::new(Mutex::new(vec![])));
        let steps = l.steps(View::Calendar, "ann").unwrap();
        assert!(matches!(run(&c, &steps, |_| None), Outcome::Nothing));
    }

    #[test]
    fn push_types_and_the_retry_without_the_newer_ones() {
        let all = ["urn:ietf:params:jmap:mail", "urn:ietf:params:jmap:submission", "urn:ietf:params:jmap:calendars",
            "urn:ietf:params:jmap:contacts", "urn:ietf:params:jmap:filenode", "urn:ietf:params:jmap:principals"];
        let c = Client::fake("https://mail.example.test", "A", &all, |_| Ok(vec![]));
        assert_eq!(push_types(&c, true), ["Email", "Mailbox", "Thread", "EmailDelivery", "EmailSubmission", "Identity", "CalendarEvent", "Calendar",
            "ContactCard", "AddressBook", "FileNode", "ShareNotification", "CalendarEventNotification"]);
        assert_eq!(push_types(&c, false), ["Email", "Mailbox", "Thread", "EmailDelivery", "EmailSubmission", "Identity", "CalendarEvent", "Calendar",
            "ContactCard", "AddressBook"]);
        let mail = Client::fake("https://mail.example.test", "A", &["urn:ietf:params:jmap:mail"], |_| Ok(vec![]));
        assert_eq!(push_types(&mail, true), PUSH_TYPES.to_vec());
        assert!(retry_without_more(true, false, Some("Push failed (400 Bad Request)")));
        assert!(!retry_without_more(true, false, Some("Push stream closed.")), "a closed stream is no refusal");
        assert!(!retry_without_more(true, false, Some("error sending request for url (https://mail.example.test/push)")), "a transport error keeps the types");
        assert!(!retry_without_more(true, false, Some("Push failed (503 Service Unavailable)")), "a server error keeps the types");
        assert!(!retry_without_more(true, true, Some("Push stream closed.")), "it opened: the types were taken");
        assert!(!retry_without_more(false, false, Some("Push failed (400 Bad Request)")), "already without them");
        assert!(!retry_without_more(true, false, Some("Wrong username, password or 2FA code.")));
        assert!(!retry_without_more(true, false, None));
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
