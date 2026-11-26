//! Calendar app: JMAP for Calendars (CalendarEvent / Calendar), Day / Week / Month / Agenda /
//! Tasks views, mini month, calendar visibility, client-side recurrence expansion on each
//! event's own clock (recurrence.rs, like Bulwark's expansion fallback), calendars shared with
//! the user from other accounts (with per-viewer colours, Bulwark sharedCalendarColors), the
//! contact birthday calendar and task chips. The editor lives in cal_editor.rs, reminders in
//! reminders.rs, agenda / tasks / import / subscriptions in cal_extra.rs.
use crate::jmap::Client;
use crate::recurrence::{self, parse_local};
use crate::{AppWindow, CalCell, CalDetail, CalEvent, CalGuest, CalItem, CalSum, MiniDay};
use chrono::{Datelike, Duration, Local, NaiveDate, NaiveDateTime, Timelike};
use serde_json::{json, Value};
use crate::listsync::{sync_into, sync_seq};
use slint::{Color, ComponentHandle, ModelRc, VecModel};
use std::cell::RefCell;
use std::collections::{HashMap, HashSet};
use std::sync::Arc;

pub const HOUR_PX: f32 = 56.0;
pub const CAL: &str = "urn:ietf:params:jmap:calendars";
pub const BIRTHDAY_CAL: &str = "__birthday-calendar__";

#[derive(Default)]
struct State {
    anchor: Option<NaiveDate>,
    view: i32, // 0 day, 1 week, 2 month, 3 agenda, 4 tasks
    calendars: Vec<Value>,
    hidden: HashSet<String>,
    events: Vec<Occ>,
    /// the events (not tasks or birthdays) a year ahead, from the same expansion: the summary
    /// tiles and the context panel read this instead of expanding on the UI thread
    ahead: Vec<Occ>,
    query: String,
    /// the day columns on screen (day/week), for a click on column N
    columns: Vec<NaiveDate>,
    // the event shown in the context panel (design: guests / details beside the grid)
    selected: Option<String>,
    /// the all-day strip of the day and week header shows every row (see `allday`)
    allday_open: bool,
}

/// One thing on the grid: an occurrence of an event, a task's due date or a birthday.
#[derive(Clone, Debug, PartialEq)]
pub struct Occ {
    /// What the views hand back: the event id, "id|recurrenceId" for an occurrence of a series,
    /// "task:ID" or "bday:KEY".
    pub id: String,
    /// the event it comes from, and the occurrence's recurrence id
    #[allow(dead_code)]
    pub base: String,
    #[allow(dead_code)]
    pub rid: Option<String>,
    pub title: String,
    pub start: NaiveDateTime,
    pub end: NaiveDateTime,
    pub all_day: bool,
    pub calendar: String,
    pub location: String,
    /// 0 event, 1 task, 2 birthday
    pub kind: u8,
    pub done: bool,
}

thread_local! {
    static ST: RefCell<State> = RefCell::new(State::default());
    static RAW: RefCell<Arc<Vec<Value>>> = RefCell::new(Arc::new(Vec::new()));
    /// Which range expansions may still be drawn (see [`Landings`]).
    static LAND: RefCell<Landings> = RefCell::new(Landings::default());
}

/// Which range expansions may still be drawn. Each ask takes a number; a result is drawn only
/// while no newer ask has started (the user left that range), and an expansion invalidated by a
/// local change is never drawn (a new one follows it). The glance tile reads the drawn
/// occurrences, so it is refreshed exactly when a result is drawn.
#[derive(Default, Debug)]
pub struct Landings { newest: u64, drawn: u64, glance_due: bool }

impl Landings {
    /// A new expansion starts: only its result may be drawn.
    pub fn begin(&mut self) -> u64 { self.newest = self.newest.wrapping_add(1); self.newest }

    /// The events changed under the running expansion (a local move): drop its result, a new
    /// expansion follows.
    pub fn invalidate(&mut self) { self.newest = self.newest.wrapping_add(1); }

    /// A result lands: `true` (and the glance is now behind) when it is still the newest ask.
    pub fn land(&mut self, gen: u64) -> bool {
        if gen != self.newest { return false; }
        self.drawn = self.drawn.wrapping_add(1);
        self.glance_due = true;
        true
    }

    /// What was drawn reached the glance tile.
    pub fn glance_refreshed(&mut self) { self.glance_due = false; }
}

const PALETTE: [u32; 8] = [0x3b82f6, 0x22c55e, 0xf97316, 0xa855f7, 0xec4899, 0x14b8a6, 0xeab308, 0xef4444];

/// Calendar colours drawn as the design's meaning tints: reds and pinks become rose, oranges and
/// yellows amber, greens and teals mint, blues sky (Theme.tint-bg/fg map these four exactly).
/// Purples, greys and anything else keep their own colour and get the generic tint treatment.
fn snap(c: Color) -> Color {
    let (r, g, b) = (c.red() as f32 / 255.0, c.green() as f32 / 255.0, c.blue() as f32 / 255.0);
    let (mx, mn) = (r.max(g).max(b), r.min(g).min(b));
    if mx - mn < 0.15 { return c; }
    let d = mx - mn;
    let h = if mx == r { 60.0 * (((g - b) / d).rem_euclid(6.0)) } else if mx == g { 60.0 * ((b - r) / d + 2.0) } else { 60.0 * ((r - g) / d + 4.0) };
    let hex = match h as i32 {
        0..=14 | 320..=360 => 0xf0577a,
        15..=69 => 0xfbbf24,
        70..=189 => 0x34d399,
        190..=254 => 0x60a5fa,
        _ => return c,
    };
    Color::from_argb_encoded(0xff000000 | hex)
}

pub fn hex_color(s: &str) -> Option<Color> {
    let s = s.trim().trim_start_matches('#');
    let s = if s.len() == 3 { s.chars().flat_map(|c| [c, c]).collect::<String>() } else { s.to_string() };
    (s.len() == 6).then(|| u32::from_str_radix(&s, 16).ok()).flatten().map(|v| Color::from_argb_encoded(0xff000000 | v))
}

/// Bulwark sharedCalendarColorKey: `${localAccount}|${account}|${id}`.
pub fn shared_color_key(user: &str, account: &str, id: &str) -> String { format!("{user}|{account}|{id}") }

fn color_for(cals: &[Value], id: &str) -> Color {
    if id == BIRTHDAY_CAL {
        return hex_color(&crate::settings::str_pref("birthdayCalendarColor", "#eab308")).unwrap_or(Color::from_rgb_u8(0xea, 0xb3, 0x08));
    }
    let i = cals.iter().position(|c| c["id"] == id).unwrap_or(0);
    let c = cals.get(i);
    if let Some(over) = c.and_then(|c| c["_color"].as_str()).and_then(hex_color) { return over; }
    snap(c.and_then(|c| c["color"].as_str()).and_then(hex_color).unwrap_or_else(|| Color::from_argb_encoded(0xff000000 | PALETTE[i % PALETTE.len()])))
}

pub fn calendar_color_of(id: &str) -> Color { ST.with(|s| color_for(&s.borrow().calendars, id)) }

fn anchor() -> NaiveDate {
    ST.with(|s| s.borrow().anchor).unwrap_or_else(|| Local::now().date_naive())
}

pub fn view() -> i32 { ST.with(|s| s.borrow().view) }

/// Calendar display prefs (Bulwark keys: firstDayOfWeek, showWeekNumbers, calendarLimitHours,
/// calendarDayStartHour/EndHour, calendarHideNonWorkingDays + calendarWorkingDays,
/// showTimeInMonthView, calendarTintedEvents).
struct CalPrefs {
    first_dow: u32, // 0 = Sunday, 1 = Monday, 6 = Saturday (Date.getDay numbering)
    week_numbers: bool,
    hour_from: i32,
    hour_to: i32,
    hide_non_working: bool,
    working: Vec<u32>,
    time_in_month: bool,
    tinted: bool,
    h12: bool,
    /// calendarMonthTimedEvents: 0 bar, 1 coloured text, 2 dot and text
    month_timed: i32,
    /// calendarFreeScroll
    free_scroll: bool,
    /// calendarHoverPreview delay in ms; -1 = off
    hover_delay: i32,
    /// calendarNarrowWeekAllDays
    narrow_all_days: bool,
    /// enableCalendarTasks, showTasksOnCalendar, showTasksApp, showBirthdayCalendar
    tasks: bool,
    tasks_on_calendar: bool,
    tasks_app: bool,
    birthdays: bool,
}

thread_local! {
    /// The calendar pane is under 1000px wide (from the window, see `cal-narrow`).
    static NARROW: std::cell::Cell<bool> = const { std::cell::Cell::new(false) };
}

/// Days the week view shows: all seven, or three at a time in a narrow pane (Bulwark,
/// like Google Calendar) unless calendarNarrowWeekAllDays.
fn week_days_shown() -> i64 {
    if NARROW.with(|n| n.get()) && !cal_prefs().narrow_all_days { 3 } else { 7 }
}

/// Bulwark calendarHoverPreview values.
fn hover_delay(v: &str) -> i32 {
    match v { "off" => -1, "instant" => 0, "delay-1s" => 1000, "delay-2s" => 2000, _ => 500 }
}

/// Bulwark calendarMonthTimedEvents values.
fn month_timed(v: &str) -> i32 {
    match v { "text" => 1, "dot" => 2, _ => 0 }
}

fn cal_prefs() -> CalPrefs {
    let p = crate::settings::Settings::load();
    let limit = p.bool("calendarLimitHours", false);
    let start = p.int("calendarDayStartHour", 8).clamp(0, 23) as i32;
    let end = p.int("calendarDayEndHour", 20).clamp(1, 24) as i32;
    let (hour_from, hour_to) = if limit && end > start { (start, end) } else { (0, 24) };
    let working: Vec<u32> = p.prefs.get("calendarWorkingDays").and_then(|v| v.as_array())
        .map(|a| a.iter().filter_map(|d| d.as_u64()).map(|d| d as u32 % 7).collect())
        .unwrap_or_else(|| vec![1, 2, 3, 4, 5]);
    CalPrefs {
        first_dow: match p.int("firstDayOfWeek", 1) { 0 => 0, 6 => 6, _ => 1 },
        week_numbers: p.bool("showWeekNumbers", false),
        hour_from, hour_to,
        hide_non_working: p.bool("calendarHideNonWorkingDays", false),
        working,
        time_in_month: p.bool("showTimeInMonthView", false),
        tinted: p.bool("calendarTintedEvents", true),
        h12: p.str("timeFormat", "24h") == "12h",
        month_timed: month_timed(&p.str("calendarMonthTimedEvents", "bar")),
        free_scroll: p.bool("calendarFreeScroll", true),
        hover_delay: hover_delay(&p.str("calendarHoverPreview", "delay-500ms")),
        narrow_all_days: p.bool("calendarNarrowWeekAllDays", false),
        tasks: p.bool("enableCalendarTasks", true),
        tasks_on_calendar: p.bool("showTasksOnCalendar", true),
        tasks_app: p.bool("showTasksApp", false),
        birthdays: p.bool("showBirthdayCalendar", false),
    }
}

fn week_start(d: NaiveDate) -> NaiveDate {
    let first = cal_prefs().first_dow;
    let dow = d.weekday().num_days_from_sunday();
    d - Duration::days(((dow + 7 - first) % 7) as i64)
}

pub fn fmt_time(t: NaiveDateTime, h12: bool) -> String {
    if h12 { t.format("%-I:%M %p").to_string() } else { t.format("%H:%M").to_string() }
}

pub fn h12() -> bool { crate::settings::str_pref("timeFormat", "24h") == "12h" }

/// The day columns of the Day / Week view: every date of the range [from, to).
fn day_columns(from: NaiveDate, to: NaiveDate) -> Vec<NaiveDate> {
    (0..(to - from).num_days().max(1)).map(|i| from + Duration::days(i)).collect()
}

pub const AGENDA_DAYS: i64 = 30;

fn range() -> (NaiveDate, NaiveDate) {
    let (a, view) = (anchor(), ST.with(|s| s.borrow().view));
    match view {
        0 => (a, a + Duration::days(1)),
        1 if week_days_shown() < 7 => (a, a + Duration::days(week_days_shown())),
        1 => { let s = week_start(a); (s, s + Duration::days(7)) }
        3 => (a, a + Duration::days(AGENDA_DAYS)),
        _ => {
            let first = a.with_day(1).unwrap();
            let s = week_start(first);
            (s, s + Duration::days(42))
        }
    }
}

// ---------------- ids across accounts ----------------

/// Ids of other accounts (calendars shared with the user) carry their account: "ACC~id".
pub fn ns(primary: &str, acc: &str, id: &str) -> String {
    if acc == primary || acc.is_empty() { id.to_string() } else { format!("{acc}~{id}") }
}

/// (account, server id) of a namespaced id; the primary account for a plain one.
pub fn split_ns(id: &str) -> (Option<String>, String) {
    match id.split_once('~') { Some((a, i)) => (Some(a.to_string()), i.to_string()), None => (None, id.to_string()) }
}

/// The server account and id of an event or calendar id as the app holds it.
pub fn server_ref(c: &Client, id: &str) -> (String, String) {
    let (a, i) = split_ns(id);
    (a.unwrap_or_else(|| c.account_for(CAL)), i)
}

/// An event's calendarIds with the namespace taken off (for writing to its account).
pub fn server_calendar_ids(ids: &Value) -> Value {
    let m: serde_json::Map<String, Value> = ids.as_object().map(|o| o.iter().map(|(k, v)| (split_ns(k).1, v.clone())).collect()).unwrap_or_default();
    Value::Object(m)
}

/// The base event id of an id the views hand back ("id|recurrenceId" for an occurrence).
pub fn base_id(id: &str) -> &str { id.split('|').next().unwrap_or(id) }

/// The recurrence id in an id the views hand back.
pub fn rid_of(id: &str) -> Option<&str> { id.split_once('|').map(|(_, r)| r) }

/// The calendar accounts this login reaches: the primary one, then accounts shared with it.
pub fn calendar_accounts(c: &Client) -> Vec<(String, String, bool)> {
    let primary = c.account_for(CAL);
    let mut out = vec![(primary.clone(), c.session["accounts"][&primary]["name"].as_str().unwrap_or("").to_string(), true)];
    if let Some(accs) = c.session["accounts"].as_object() {
        for (id, a) in accs {
            if id == &primary || a["accountCapabilities"].get(CAL).is_none() { continue; }
            out.push((id.clone(), a["name"].as_str().unwrap_or(id).to_string(), false));
        }
    }
    out
}

pub const EVENT_PROPS: [&str; 34] = ["id", "@type", "uid", "title", "description", "start", "duration", "timeZone", "showWithoutTime", "calendarIds",
    "locations", "virtualLocations", "recurrenceRules", "excludedRecurrenceRules", "recurrenceOverrides", "recurrenceId", "status", "freeBusyStatus",
    "privacy", "participants", "organizerCalendarAddress", "replyTo", "isOrigin", "alerts", "useDefaultAlerts", "due", "progress", "percentComplete",
    "priority", "updated", "excludedRecurrenceIds", "keywords", "color", "sequence"];

// ---------------- loading ----------------

pub fn load(w: slint::Weak<AppWindow>, c: Arc<Client>) {
    if !c.has(CAL) { return; }
    // this load's number: a slower one that lands late changes nothing
    let gen = crate::livesync::begin_load(crate::livesync::View::Calendar);
    std::thread::spawn(move || {
        let tz = crate::agent::local_tz();
        let primary = c.account_for(CAL);
        let owner = c.state_key();
        let mut states: Vec<(String, String, String)> = vec![];
        // whether every calendar account was read and gave its states: push can only keep the view
        // fresh when it was (a skipped or failed account leaves it to a full load next visit)
        let mut complete = true;
        let res: Result<(Vec<Value>, Vec<Value>), String> = (|| {
            let mut cals = vec![];
            let mut events = vec![];
            let colors = crate::settings::snapshot().get("sharedCalendarColors").cloned().unwrap_or(json!({}));
            for (acc, name, is_primary) in calendar_accounts(&c) {
                let r = c.request(json!([["Calendar/get", {"accountId": acc, "ids": null, "properties": CAL_PROPS}, "c"]]))?;
                if r.first().is_none_or(|x| x[0] == "error") {
                    if is_primary { return Err(crate::jmap::method_error(&r.first().map(|x| x[1].clone()).unwrap_or(json!({})))); }
                    complete = false;
                    continue;
                }
                let mut list = r[0][1]["list"].as_array().cloned().unwrap_or_default();
                list.sort_by_key(|x| (x["sortOrder"].as_i64().unwrap_or(0), x["name"].as_str().unwrap_or("").to_lowercase()));
                for cal in list { cals.push(prepare_calendar(cal, &c.username, &primary, &acc, (!is_primary).then_some(name.as_str()), &colors)); }
                // live updates (livesync): the states the view is read at, the events' taken before
                // their read; kept only once this account's read went through
                let ev_state = crate::livesync::state_of(&c, "CalendarEvent", &acc);
                let cal_state = r[0][1]["state"].as_str().map(String::from);
                // every id (paged), then the events in maxObjectsInGet chunks: one back-referenced
                // CalendarEvent/get of 5000 ids is over Stalwart's limit (requestTooLarge)
                let ids = match c.query_all("CalendarEvent/query", &acc, json!({"timeZone": tz})) {
                    Ok(ids) => ids,
                    Err(e) if is_primary => return Err(e),
                    Err(_) => { complete = false; continue; }
                };
                let props = json!(EVENT_PROPS.to_vec());
                let got = match c.get_all("CalendarEvent/get", &acc, &ids, Some(&props)) {
                    Ok(v) => v,
                    Err(e) if is_primary => return Err(e),
                    Err(_) => { complete = false; continue; }
                };
                for mut e in got {
                    namespace_event(&mut e, &primary, &acc);
                    events.push(e);
                }
                if let (Some(cs), Some(es)) = (cal_state, ev_state) {
                    states.push(("Calendar".to_string(), acc.clone(), cs));
                    states.push(("CalendarEvent".to_string(), acc.clone(), es));
                } else {
                    complete = false;
                }
            }
            Ok((cals, events))
        })();
        let w2 = w.clone();
        let c2 = c.clone();
        let _ = w.upgrade_in_event_loop(move |u| match res {
            // signed out or switched meanwhile: another login's data is not shown
            Ok(_) if !crate::livesync::is_current(&owner) => {}
            Ok((cals, events)) => {
                ST.with(|s| s.borrow_mut().calendars = cals);
                RAW.with(|r| *r.borrow_mut() = Arc::new(events));
                rebuild(&u);
                render_tasks(&u);
                // the birthday cards (when the calendar is on) are read through one place, so a
                // slow read cannot overwrite newer cards set meanwhile
                crate::cal_extra::ensure_people(&u, c2.clone(), false);
                crate::cal_extra::after_load(&u, w2, c2);
                crate::livesync::loaded(&u, crate::livesync::View::Calendar, &owner, &states, complete, gen);
            }
            Err(e) => {
                // nothing could be read: the view is not reused again until a load lands (a newer
                // load, or another login's, is left alone)
                crate::livesync::load_failed(crate::livesync::View::Calendar, &owner, gen);
                crate::mail::show_error(&u, &format!("Calendar: {e}"));
            }
        });
    });
}

/// The calendar properties the view loads (and a live delta fetches).
pub const CAL_PROPS: [&str; 11] = ["id", "name", "color", "isDefault", "isVisible", "sortOrder", "myRights",
    "defaultAlertsWithTime", "defaultAlertsWithoutTime", "isSubscribed", "includeInAvailability"];

/// A calendar of `acc` as the view holds it: namespaced id, its account, and for a shared one
/// (`owner` is the account's name) the per-viewer colour.
fn prepare_calendar(mut cal: Value, username: &str, primary: &str, acc: &str, owner: Option<&str>, colors: &Value) -> Value {
    let raw = cal["id"].as_str().unwrap_or("").to_string();
    cal["_rid"] = json!(raw);
    cal["_acc"] = json!(acc);
    cal["id"] = json!(ns(primary, acc, &raw));
    if let Some(name) = owner {
        cal["_shared"] = json!(true);
        cal["_owner"] = json!(name);
        if let Some(col) = colors[shared_color_key(username, acc, &raw)].as_str() { cal["_color"] = json!(col); }
    }
    cal
}

/// Apply Calendar and CalendarEvent deltas of each account to calendars and events as the view
/// holds them. Calendars keep their account order (`accounts`, as [`calendar_accounts`]) and are
/// sorted in it as the full load sorts them.
pub fn apply_deltas(cals: &mut Vec<Value>, events: &mut Vec<Value>, accounts: &[(String, String, bool)], primary: &str, username: &str,
                    colors: &Value, ds: &[crate::livesync::Applied]) {
    let ident = |x: &Value| (x["_acc"].as_str().unwrap_or("").to_string(), x["_rid"].as_str().unwrap_or("").to_string());
    for (ty, acc, _, d) in ds {
        if ty == "Calendar" {
            let owner = accounts.iter().find(|a| a.0 == *acc && !a.2).map(|a| a.1.clone());
            crate::livesync::apply(cals, acc, d, ident, |v| prepare_calendar(v, username, primary, acc, owner.as_deref(), colors));
            cals.sort_by_key(|x| (accounts.iter().position(|a| x["_acc"] == a.0.as_str()).unwrap_or(usize::MAX),
                x["sortOrder"].as_i64().unwrap_or(0), x["name"].as_str().unwrap_or("").to_lowercase()));
        } else {
            crate::livesync::apply(events, acc, d, ident, |mut e| { namespace_event(&mut e, primary, acc); e });
        }
    }
}

/// Live update (livesync): apply the deltas, then redraw as a full load does (tasks, the Tasks
/// view, subscriptions of calendars that went). `colors` were read on the worker thread.
pub fn apply_live(u: &AppWindow, c: &Arc<Client>, ds: &[crate::livesync::Applied], colors: &Value) {
    let accounts = calendar_accounts(c);
    let primary = c.account_for(CAL);
    ST.with(|s| RAW.with(|r| apply_deltas(&mut s.borrow_mut().calendars, Arc::make_mut(&mut r.borrow_mut()), &accounts, &primary, &c.username, colors, ds)));
    rebuild(u);
    render_tasks(u);
    crate::cal_extra::after_load(u, u.as_weak(), c.clone());
}

/// Give an event of another account namespaced ids (its own and its calendars').
pub fn namespace_event(e: &mut Value, primary: &str, acc: &str) {
    let raw = e["id"].as_str().unwrap_or("").to_string();
    e["_rid"] = json!(raw);
    e["_acc"] = json!(acc);
    e["id"] = json!(ns(primary, acc, &raw));
    if acc != primary {
        if let Some(o) = e["calendarIds"].as_object() {
            let m: serde_json::Map<String, Value> = o.iter().map(|(k, v)| (ns(primary, acc, k), v.clone())).collect();
            e["calendarIds"] = Value::Object(m);
        }
    }
}

/// Every event (not task / birthday) occurrence between `f` and `t`, sorted by start: the summary
/// tiles and the context panel read a year of these, so it is built on the worker and kept in the
/// state (`State::ahead`) rather than expanded on the UI thread.
fn events_between(events: &[Value], people: &[Value], cp: &CalPrefs, f: NaiveDateTime, t: NaiveDateTime) -> Vec<Occ> {
    let end = if t.time() == chrono::NaiveTime::MIN { t.date() } else { t.date() + Duration::days(1) };
    let mut occ: Vec<Occ> = expand(events, people, cp, f.date(), end).into_iter().filter(|o| o.kind == 0).collect();
    occ.retain(|o| o.end > f && o.start < t);
    occ
}

pub fn first_calendar(e: &Value) -> String {
    e["calendarIds"].as_object().and_then(|o| o.iter().find(|(_, v)| **v == true).map(|(k, _)| k.clone()).or_else(|| o.keys().next().cloned())).unwrap_or_default()
}

pub fn location_of(e: &Value) -> String {
    e["locations"].as_object().and_then(|o| o.values().find_map(|l| l["name"].as_str().filter(|n| !n.is_empty())))
        .or_else(|| e["virtualLocations"].as_object().and_then(|o| o.values().find_map(|l| l["uri"].as_str())))
        .unwrap_or("").to_string()
}

/// The occurrences of every loaded event, task and birthday in [from, to) on the local clock.
pub fn occurrences_in(events: &[Value], from: NaiveDate, to: NaiveDate) -> Vec<Occ> {
    let cp = cal_prefs();
    let people = if cp.birthdays { crate::cal_extra::people() } else { Default::default() };
    expand(events, &people[..], &cp, from, to)
}

/// [`occurrences_in`] without reading any thread-local state: `people` are the birthday cards and
/// `cp` the calendar prefs, both read by the caller (off the UI thread), so a worker can run this
/// and the recurrence results (RRULE, EXDATE, overrides, time zones) stay identical.
fn expand(events: &[Value], people: &[Value], cp: &CalPrefs, from: NaiveDate, to: NaiveDate) -> Vec<Occ> {
    let (f, t) = (from.and_hms_opt(0, 0, 0).unwrap(), to.and_hms_opt(0, 0, 0).unwrap());
    let mut occ = vec![];
    for e in events {
        if is_task(e) {
            if !(cp.tasks && cp.tasks_on_calendar) { continue; }
            let Some(due) = e["due"].as_str().and_then(parse_local) else { continue };
            let due = if e["showWithoutTime"] == true { due } else { recurrence::zone_to_local(due, e["timeZone"].as_str()) };
            let day = due.date().and_hms_opt(0, 0, 0).unwrap();
            if day < f || day >= t { continue; }
            let done = e["progress"] == "completed";
            occ.push(Occ {
                id: format!("task:{}", e["id"].as_str().unwrap_or("")), base: e["id"].as_str().unwrap_or("").into(), rid: None,
                title: format!("{} {}", if done { "\u{2713}" } else { "\u{25CB}" }, e["title"].as_str().filter(|x| !x.is_empty()).unwrap_or("(untitled task)")),
                start: day, end: day + Duration::days(1), all_day: true, calendar: first_calendar(e), location: String::new(), kind: 1, done,
            });
            continue;
        }
        let all_day = e["showWithoutTime"] == true;
        let zone = if all_day { None } else { e["timeZone"].as_str().filter(|z| !z.is_empty()) };
        // the window on the event's own clock, a day wider for zones far from the user's
        let (zf, zt) = (recurrence::local_to_zone(f, zone) - Duration::days(1), recurrence::local_to_zone(t, zone) + Duration::days(1));
        let id = e["id"].as_str().unwrap_or("").to_string();
        let title = e["title"].as_str().filter(|x| !x.is_empty()).unwrap_or("(no title)").to_string();
        let location = location_of(e);
        let calendar = first_calendar(e);
        for o in recurrence::occurrences(e, zf, zt) {
            let (s, en) = if all_day { (o.start, o.end) } else { (recurrence::zone_to_local(o.start, zone), recurrence::zone_to_local(o.end, zone)) };
            if !(s < t && en > f) && !(s == en && s >= f && s < t) { continue; }
            let p = o.patch.as_ref();
            occ.push(Occ {
                id: match &o.rid { Some(r) => format!("{id}|{r}"), None => id.clone() },
                base: id.clone(), rid: o.rid.clone(),
                title: p.and_then(|p| p["title"].as_str()).filter(|x| !x.is_empty()).map(String::from).unwrap_or_else(|| title.clone()),
                start: s, end: en, all_day,
                calendar: calendar.clone(),
                location: p.filter(|p| p.get("locations").is_some()).map(location_of).unwrap_or_else(|| location.clone()),
                kind: 0, done: false,
            });
        }
    }
    if cp.birthdays {
        occ.extend(crate::cal_extra::birthdays_in(people, from, to));
    }
    occ.sort_by_key(|o| (o.start, !o.all_day));
    occ
}

/// Whether the range expansion numbered `gen` is still the newest ask: only its result is drawn.
/// The start and the landing of an expansion, and the glance tile that follows a landing.
pub fn rebuild(u: &AppWindow) {
    NARROW.with(|n| n.set(u.get_cal_narrow()));
    let (from, to) = range();
    let now = Local::now().naive_local();
    let gen = LAND.with(|l| l.borrow_mut().begin());
    // the snapshot is shared with the worker, not copied: a range change must not clone every event
    let events = RAW.with(|r| Arc::clone(&r.borrow()));
    // the birthday cards and the prefs are thread-local, so read them here (UI thread) first;
    // the cards are shared with the worker, not copied
    let cp = cal_prefs();
    let people = if cp.birthdays { crate::cal_extra::people() } else { Default::default() };
    let w = u.as_weak();
    std::thread::spawn(move || {
        // the range on screen, and the year the summary tiles look at: both off the UI thread
        let occ = expand(&events, &people[..], &cp, from, to);
        let ahead = events_between(&events, &people[..], &cp, now - Duration::days(1), now + Duration::days(366));
        let _ = w.upgrade_in_event_loop(move |u| {
            if !LAND.with(|l| l.borrow_mut().land(gen)) { return; }
            ST.with(|s| { let mut s = s.borrow_mut(); s.events = occ; s.ahead = ahead; });
            render(&u);
            // the mail tile counts what was just drawn (it is not part of the grid's render)
            crate::glance::calendar(&u);
            LAND.with(|l| l.borrow_mut().glance_refreshed());
        });
    });
}

/// The loaded events whose title contains `q`, as one line of JSON (automation probe).
pub fn dump(q: &str) -> String {
    let q = q.to_lowercase();
    RAW.with(|r| Value::Array(r.borrow().iter().filter(|e| e["title"].as_str().is_some_and(|t| t.to_lowercase().contains(&q))).cloned().collect()).to_string())
}

pub fn with_raw<T>(f: impl FnOnce(&[Value]) -> T) -> T { RAW.with(|r| f(&r.borrow())) }
pub fn raw_event(id: &str) -> Option<Value> { RAW.with(|r| r.borrow().iter().find(|e| e["id"] == id).cloned()) }
pub fn calendars() -> Vec<Value> { ST.with(|s| s.borrow().calendars.clone()) }
pub fn calendar(id: &str) -> Option<Value> { ST.with(|s| s.borrow().calendars.iter().find(|c| c["id"] == id).cloned()) }
pub fn hidden() -> HashSet<String> { ST.with(|s| s.borrow().hidden.clone()) }

// ---------------- rendering ----------------

pub fn render(u: &AppWindow) {
    NARROW.with(|n| n.set(u.get_cal_narrow()));
    let today = Local::now().date_naive();
    let a = anchor();
    let (from, to) = range();
    let cp = cal_prefs();
    u.set_cal_hover_delay(cp.hover_delay);
    u.set_cal_free_scroll(cp.free_scroll);
    {
        let x = u.global::<crate::CalX>();
        x.set_tasks_enabled(cp.tasks);
        x.set_tasks_app(cp.tasks && cp.tasks_app);
        let mut names: Vec<slint::SharedString> = ["Day", "Week", "Month", "Agenda"].into_iter().map(Into::into).collect();
        if cp.tasks && !cp.tasks_app { names.push("Tasks".into()); }
        x.set_view_names(ModelRc::new(VecModel::from(names)));
    }
    {
        let ws = week_start(today);
        let names: Vec<slint::SharedString> = (0..7).map(|i| (ws + Duration::days(i)).format("%a").to_string().into()).collect();
        let short: Vec<slint::SharedString> = names.iter().map(|n| n.chars().next().map(|c| c.to_string()).unwrap_or_default().into()).collect();
        sync_seq(&u.get_cal_weekdays(), names, |m| u.set_cal_weekdays(m));
        sync_seq(&u.get_cal_weekdays_short(), short, |m| u.set_cal_weekdays_short(m));
        u.set_cal_show_weeknums(cp.week_numbers);
        u.set_cal_hour_from(cp.hour_from);
        u.set_cal_hour_to(cp.hour_to);
        u.global::<crate::Prefs>().set_cal_tinted(cp.tinted);
        u.global::<crate::Prefs>().set_cal_h12(cp.h12);
        let mini_first = week_start(a.with_day(1).unwrap());
        let nums: Vec<slint::SharedString> = (0..6).map(|w| (mini_first + Duration::days(w * 7 + 3)).iso_week().week().to_string().into()).collect();
        sync_seq(&u.get_cal_week_nums(), nums, |m| u.set_cal_week_nums(m));
    }
    PENDING.with(|p| *p.borrow_mut() = pending_ids(&crate::my_addrs_cached()));
    // the agenda and tasks views read the calendar state themselves: they render after the
    // borrow below is released (a nested borrow panics)
    let mut deferred: Option<(i32, Vec<Occ>, Vec<Value>)> = None;
    ST.with(|s| {
        let mut s = s.borrow_mut();
        let q = s.query.to_lowercase();
        let visible: Vec<Occ> = s.events.iter()
            .filter(|o| !s.hidden.contains(&o.calendar))
            .filter(|o| q.is_empty() || o.title.to_lowercase().contains(&q) || o.location.to_lowercase().contains(&q))
            .cloned().collect();
        // the day buckets for the grid views, built once so the cells never scan every event
        let buckets: HashMap<NaiveDate, Vec<Occ>> = if matches!(s.view, 0 | 1 | 2) { bucket_by_day(&visible, from, to) } else { HashMap::new() };
        u.set_cal_view(s.view);
        u.set_cal_title(match s.view {
            0 => {
                let t = a.format("%A %-d %B").to_string();
                if cp.week_numbers { format!("{t}  ·  Week {}", a.iso_week().week()) } else { t }
            }
            // design: "16 – 22 November", "30 November – 6 December"
            1 => {
                let last = to - Duration::days(1);
                if from.month() == last.month() { format!("{} – {}", from.day(), last.format("%-d %B")) }
                else { format!("{} – {}", from.format("%-d %B"), last.format("%-d %B")) }
            }
            3 => format!("{} – {}", from.format("%b %-d"), (to - Duration::days(1)).format("%b %-d, %Y")),
            4 => "Tasks".to_string(),
            _ => a.format("%B %Y").to_string(),
        }.into());
        // calendars list (and the birthday calendar while it is on)
        let mut cals: Vec<CalItem> = s.calendars.iter().map(|c| {
            let id = c["id"].as_str().unwrap_or("");
            let name = c["name"].as_str().unwrap_or("Calendar");
            let name = match c["_owner"].as_str() { Some(o) if !o.is_empty() => format!("{name} ({o})"), _ => name.to_string() };
            CalItem { id: id.into(), name: name.into(), color: color_for(&s.calendars, id), visible: !s.hidden.contains(id) }
        }).collect();
        if cp.birthdays {
            cals.push(CalItem { id: BIRTHDAY_CAL.into(), name: "Birthdays".into(), color: color_for(&s.calendars, BIRTHDAY_CAL), visible: !s.hidden.contains(BIRTHDAY_CAL) });
        }
        sync_into(&u.get_calendars(), cals, |c| c.id.clone(), |a, b| a == b, |m| u.set_calendars(m));
        // mini month
        let first = a.with_day(1).unwrap();
        let ms = week_start(first);
        let mini: Vec<MiniDay> = (0..42).map(|i| {
            let d = ms + Duration::days(i);
            MiniDay { day: d.day() as i32, in_month: d.month() == a.month(), today: d == today, selected: if s.view == 4 { crate::cal_extra::task_day() == Some(d) } else { d >= from && d < to && s.view != 2 && s.view != 3 },
                date: d.format("%Y-%m-%d").to_string().into(), busy: visible.iter().any(|o| o.kind == 0 && o.start.date() <= d && o.end.date() >= d) }
        }).collect();
        sync_into(&u.get_mini_month(), mini, |d| d.date.clone(), |a, b| a == b, |m| u.set_mini_month(m));
        u.set_mini_title(a.format("%B %Y").to_string().into());
        match s.view {
            2 => {
                let cells: Vec<CalCell> = (0..42).map(|i| {
                    let d = from + Duration::days(i);
                    let evs: &[Occ] = buckets.get(&d).map(|v| v.as_slice()).unwrap_or(&[]);
                    let items: Vec<CalEvent> = evs.iter().take(4).map(|o| {
                        let mut e = ev(o, &s.calendars, 0, 0.0, 0.0, 1, cp.h12);
                        // Bulwark: only the start time, and only with showTimeInMonthView
                        e.time = if cp.time_in_month && !o.all_day && o.start.date() == d { fmt_time(o.start, cp.h12).into() } else { "".into() };
                        // calendarMonthTimedEvents: all-day and multi-day events stay bars
                        let timed = !o.all_day && (o.end - o.start).num_hours() < 24;
                        e.style = if timed { cp.month_timed } else { 0 };
                        e
                    }).collect();
                    CalCell {
                        day: d.day() as i32, date: d.format("%Y-%m-%d").to_string().into(), in_month: d.month() == a.month(), today: d == today,
                        events: ModelRc::new(VecModel::from(items)),
                        more: if evs.len() > 4 { format!("+{} more", evs.len() - 4).into() } else { "".into() },
                    }
                }).collect();
                sync_into(&u.get_cal_cells(), cells, |c| c.date.clone(), |a, b| a.day == b.day && a.date == b.date && a.in_month == b.in_month && a.today == b.today && a.more == b.more && crate::listsync::rows_eq(&a.events, &b.events), |m| u.set_cal_cells(m));
            }
            3 => deferred = Some((3, visible.clone(), s.calendars.clone())),
            4 => deferred = Some((4, vec![], vec![])),
            _ => {
                // one column per day of the loaded range (three in a narrow pane)
                let mut dates: Vec<NaiveDate> = day_columns(from, to);
                if s.view == 1 && cp.hide_non_working {
                    let kept: Vec<NaiveDate> = dates.iter().copied().filter(|d| cp.working.contains(&d.weekday().num_days_from_sunday())).collect();
                    if !kept.is_empty() { dates = kept; }
                }
                s.columns = dates.clone();
                let all_by_day: Vec<Vec<CalEvent>> = dates.iter().enumerate().map(|(i, &d)| {
                    buckets.get(&d).map(|v| v.as_slice()).unwrap_or(&[]).iter()
                        .filter(|o| o.all_day || (o.end - o.start).num_hours() >= 24)
                        .map(|o| ev(o, &s.calendars, i as i32, 0.0, 0.0, 1, cp.h12)).collect()
                }).collect();
                // at most three rows, the rest behind "+N more" until the strip is expanded (D-M4-08)
                let strip = crate::allday::layout(&all_by_day.iter().map(|v| v.len()).collect::<Vec<_>>(), s.allday_open);
                let heads: Vec<CalCell> = dates.iter().zip(all_by_day).enumerate().map(|(i, (&d, mut all))| {
                    all.truncate(strip.shown[i]);
                    CalCell { day: d.day() as i32, date: d.format("%a").to_string().into(), in_month: true, today: d == today,
                        events: ModelRc::new(VecModel::from(all)), more: crate::allday::more_label(strip.more[i]).into() }
                }).collect();
                u.set_cal_allday_h(strip.height);
                u.set_cal_allday_expandable(strip.expandable);
                u.set_cal_allday_open(s.allday_open && strip.expandable);
                u.set_cal_allday_extra(strip.extra_rows as i32);
                sync_into(&u.get_cal_days(), heads, |c| format!("{}{}", c.day, c.date), |a, b| a.day == b.day && a.date == b.date && a.in_month == b.in_month && a.today == b.today && a.more == b.more && crate::listsync::rows_eq(&a.events, &b.events), |m| u.set_cal_days(m));
                // timed events with simple overlap columns
                let mut timed: Vec<CalEvent> = vec![];
                let off = cp.hour_from as f32 * HOUR_PX;
                for (i, &d) in dates.iter().enumerate() {
                    let day_ev: Vec<&Occ> = buckets.get(&d).map(|v| v.as_slice()).unwrap_or(&[]).iter()
                        .filter(|o| !o.all_day && (o.end - o.start).num_hours() < 24 && o.start.date() == d).collect();
                    let mut lanes: Vec<NaiveDateTime> = vec![];
                    let mut placed: Vec<(usize, &Occ)> = vec![];
                    for o in &day_ev {
                        let lane = lanes.iter().position(|end| *end <= o.start).unwrap_or_else(|| { lanes.push(o.start); lanes.len() - 1 });
                        lanes[lane] = o.end;
                        placed.push((lane, o));
                    }
                    let n = lanes.len().max(1);
                    for (lane, o) in placed {
                        let top = (o.start.time().num_seconds_from_midnight() as f32 / 3600.0) * HOUR_PX - off;
                        let h = ((o.end - o.start).num_minutes().max(20) as f32 / 60.0) * HOUR_PX;
                        let mut e = ev(o, &s.calendars, i as i32, top, h, n as i32, cp.h12);
                        e.lane = lane as i32;
                        timed.push(e);
                    }
                }
                sync_into(&u.get_cal_events(), timed, |e| format!("{}|{}|{}", e.id, e.col, e.date), |a, b| a == b, |m| u.set_cal_events(m));
            }
        }
    });
    summary(u);
    detail(u);
    match deferred {
        Some((3, visible, cals)) => crate::cal_extra::render_agenda(u, &visible, from, to, &cals, cp.h12),
        Some((4, _, _)) => crate::cal_extra::render_tasks_view(u),
        _ => {}
    }
}

/// The last day an occurrence shows on (its start day when it ends where it starts).
fn covers_last(o: &Occ) -> NaiveDate {
    if o.end > o.start { (o.end - Duration::seconds(1)).date() } else { o.start.date() }
}

// Whether an occurrence shows on day `d` (an all-day or multi-day one on each day it covers).
// Test helper: the cells read their day's bucket instead, so this only asserts the buckets.
#[cfg(test)]
fn covers(o: &Occ, d: NaiveDate) -> bool {
    o.start.date() <= d && covers_last(o) >= d
}

/// The occurrences of each day of [from, to), in start order: an all-day or multi-day occurrence
/// appears under every day it covers (its last covered day is [`covers_last`]), a timed one under
/// its start day. The cells read their own day's bucket instead of scanning every occurrence.
pub fn bucket_by_day(occ: &[Occ], from: NaiveDate, to: NaiveDate) -> HashMap<NaiveDate, Vec<Occ>> {
    let mut m: HashMap<NaiveDate, Vec<Occ>> = HashMap::new();
    let last_day = to - Duration::days(1);
    for o in occ {
        let first = o.start.date().max(from);
        let last = covers_last(o).min(last_day);
        let mut d = first;
        while d <= last {
            m.entry(d).or_default().push(o.clone());
            d += Duration::days(1);
        }
    }
    m
}

fn ev(o: &Occ, cals: &[Value], col: i32, top: f32, h: f32, lanes: i32, h12: bool) -> CalEvent {
    CalEvent {
        id: o.id.as_str().into(),
        title: o.title.as_str().into(),
        time: if o.all_day { "".into() } else { format!("{}–{}", fmt_time(o.start, h12), fmt_time(o.end, h12)).into() },
        location: o.location.as_str().into(),
        color: color_for(cals, &o.calendar),
        col, top, height: h, lane: 0, lanes,
        date: o.start.format("%Y-%m-%d").to_string().into(),
        style: 0,
        invite: PENDING.with(|p| p.borrow().contains(&o.id)),
        past: o.end <= Local::now().naive_local(),
        when: when_text(o, h12).into(),
    }
}

/// The date of day column `col` on screen (day / week view).
pub fn column_date(col: i32) -> Option<NaiveDate> {
    ST.with(|s| s.borrow().columns.get(col.max(0) as usize).copied())
}

// ---------------- navigation ----------------

pub fn nav(u: &AppWindow, c: Arc<Client>, how: &str) {
    NARROW.with(|n| n.set(u.get_cal_narrow()));
    let reload = ST.with(|s| {
        let mut s = s.borrow_mut();
        let a = s.anchor.unwrap_or_else(|| Local::now().date_naive());
        if s.view == 4 && (how == "task-day-clear" || NaiveDate::parse_from_str(how, "%Y-%m-%d").is_ok()) { crate::cal_extra::toggle_task_day(how); }
        s.anchor = Some(match how {
            "today" => Local::now().date_naive(),
            "prev" => match s.view { 0 => a - Duration::days(1), 1 => a - Duration::days(week_days_shown()), 3 => a - Duration::days(AGENDA_DAYS), _ => (a.with_day(1).unwrap() - Duration::days(1)).with_day(1).unwrap() },
            "next" => match s.view { 0 => a + Duration::days(1), 1 => a + Duration::days(week_days_shown()), 3 => a + Duration::days(AGENDA_DAYS), _ => (a.with_day(28).unwrap() + Duration::days(7)).with_day(1).unwrap() },
            // the pane changed width (cal-narrow): same dates, drawn again
            "rerender" => a,
            "mini-prev" => (a.with_day(1).unwrap() - Duration::days(1)).with_day(1).unwrap(),
            "mini-next" => (a.with_day(28).unwrap() + Duration::days(7)).with_day(1).unwrap(),
            d => NaiveDate::parse_from_str(d, "%Y-%m-%d").unwrap_or(a),
        });
        // the events are all loaded; only a first visit or an explicit refresh fetches
        RAW.with(|r| r.borrow().is_empty())
    });
    rebuild(u);
    if reload { load(u.as_weak(), c); }
}

pub fn set_view(u: &AppWindow, c: Arc<Client>, v: i32) {
    ST.with(|s| s.borrow_mut().view = v);
    rebuild(u);
    load(u.as_weak(), c);
}

/// Draw the calendar and fetch only when the cache does not already hold this login's data kept
/// fresh by push (livesync keeps it fresh in between). App switch: a cached visit still does what
/// a load used to do beside reading the events — it expands the range again (the day or the view
/// may have moved), keeps the subscriptions due a refresh going and makes sure the birthday cards
/// are there.
pub fn switch_to(u: &AppWindow, c: Arc<Client>) {
    render(u);
    if crate::livesync::cache_current(crate::livesync::View::Calendar, &c) {
        rebuild(u);
        crate::cal_extra::after_load_light(u, c.clone());
        crate::cal_extra::ensure_people(u, c, false);
        return;
    }
    load(u.as_weak(), c);
}

/// The all-day strip's "+N" / "less" toggle (Bulwark `isAllDayExpanded`).
pub fn toggle_allday(u: &AppWindow) {
    ST.with(|s| { let mut s = s.borrow_mut(); s.allday_open = !s.allday_open; });
    render(u);
}

pub fn toggle_calendar(u: &AppWindow, id: &str) {
    ST.with(|s| { let mut s = s.borrow_mut(); if !s.hidden.remove(id) { s.hidden.insert(id.to_string()); } });
    render(u);
}

pub fn search(u: &AppWindow, q: &str) {
    ST.with(|s| s.borrow_mut().query = q.to_string());
    render(u);
}

// ---------------- editing (cal_editor.rs) ----------------

/// Open the event dialog: `id` empty for a new event at `date` (and optional hour); "#N" for a
/// new event in day column N; "task:ID" opens the task editor.
pub fn edit(u: &AppWindow, id: &str, date: &str, hour: i32) {
    if let Some(t) = id.strip_prefix("task:") { crate::cal_extra::open_task(u, t); return; }
    if let Some(col) = id.strip_prefix('#') {
        let d = col.parse().ok().and_then(column_date).unwrap_or_else(anchor);
        crate::cal_editor::open_new(u, d, hour, 0, false, "");
        return;
    }
    if id.is_empty() {
        let d = NaiveDate::parse_from_str(date, "%Y-%m-%d").unwrap_or_else(|_| anchor());
        crate::cal_editor::open_new(u, d, hour, 0, false, "");
        return;
    }
    crate::cal_editor::open_existing(u, id);
}

pub fn save(u: &AppWindow, c: Arc<Client>) { crate::cal_editor::save(u, c); }

pub fn delete(u: &AppWindow, c: Arc<Client>) { crate::cal_editor::delete(u, c); }

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn live_deltas_update_calendars_and_events_of_their_account_only() {
        use crate::livesync::Delta;
        let accounts = vec![("A".to_string(), "Me".to_string(), true), ("B".to_string(), "Bob".to_string(), false)];
        let colors = json!({});
        let mut cals = vec![prepare_calendar(json!({"id": "c1", "name": "Work", "sortOrder": 2}), "ann", "A", "A", None, &colors),
            prepare_calendar(json!({"id": "c1", "name": "Bob's", "sortOrder": 0}), "ann", "A", "B", Some("Bob"), &colors)];
        let mut events = vec![];
        for (acc, id) in [("A", "e1"), ("A", "e2"), ("B", "e1")] { let mut e = json!({"id": id, "title": format!("{acc}{id}"), "calendarIds": {"c1": true}}); namespace_event(&mut e, "A", acc); events.push(e); }
        let ds = vec![
            ("Calendar".to_string(), "A".to_string(), "c0".to_string(), Delta { destroyed: vec![], fetched: vec![json!({"id": "c2", "name": "Home", "sortOrder": 1})], state: "c9".into() }),
            ("Calendar".to_string(), "B".to_string(), "d0".to_string(), Delta { destroyed: vec![], fetched: vec![json!({"id": "c3", "name": "Team", "sortOrder": 0})], state: "d9".into() }),
            ("CalendarEvent".to_string(), "B".to_string(), "e0".to_string(), Delta { destroyed: vec!["e1".into()], fetched: vec![json!({"id": "e7", "title": "new", "calendarIds": {"c3": true}})], state: "e9".into() }),
            ("CalendarEvent".to_string(), "A".to_string(), "f0".to_string(), Delta { destroyed: vec![], fetched: vec![json!({"id": "e2", "title": "renamed", "calendarIds": {"c1": true}})], state: "f9".into() }),
        ];
        apply_deltas(&mut cals, &mut events, &accounts, "A", "ann", &colors, &ds);
        // the primary account's calendars first, sorted; a shared one keeps its owner and namespaced id
        let names: Vec<&str> = cals.iter().map(|c| c["name"].as_str().unwrap()).collect();
        assert_eq!(names, ["Home", "Work", "Bob's", "Team"]);
        assert_eq!(cals[3]["_owner"], "Bob");
        assert_eq!(cals[3]["id"], json!(ns("A", "B", "c3")));
        let titles: Vec<&str> = events.iter().map(|e| e["title"].as_str().unwrap()).collect();
        assert_eq!(titles, ["Ae1", "renamed", "new"], "B's e1 went, A's e1 has the same id and stays");
        assert_eq!(events[2]["_acc"], "B");
        assert!(events[2]["calendarIds"].get(ns("A", "B", "c3").as_str()).is_some());
    }

    #[test]
    fn sidebar_tasks_capped_and_overdue() {
        let open: Vec<Value> = (0..9).map(|_| json!({"progress": "needs-action"})).chain([json!({"progress": "completed"})]).collect();
        assert_eq!(sidebar_tasks(&open), (SIDEBAR_TASKS, 3));
        assert_eq!(sidebar_tasks(&open[..2]), (2, 0));
        assert!(task_overdue("2026-11-09", false, "2026-11-16"));
        assert!(!task_overdue("2026-11-16", false, "2026-11-16"));
        assert!(!task_overdue("2026-11-09", true, "2026-11-16"));
        assert!(!task_overdue("", false, "2026-11-16"));
    }
    #[test]
    fn day_columns_follow_range() {
        let d = NaiveDate::from_ymd_opt(2026, 11, 16).unwrap();
        assert_eq!(day_columns(d, d + Duration::days(3)).len(), 3);
        assert_eq!(day_columns(d, d + Duration::days(7)).last(), Some(&(d + Duration::days(6))));
        assert_eq!(day_columns(d, d + Duration::days(1)), vec![d]);
    }
    #[test]
    fn occurrences_carry_recurrence_ids_and_skip_tasks() {
        let evs = vec![
            json!({"id": "e1", "@type": "Event", "title": "Weekly", "start": "2026-11-03T10:00:00", "duration": "PT1H30M", "recurrenceRules": [{"frequency": "weekly", "count": 3}], "calendarIds": {"a": true}}),
            json!({"id": "e2", "@type": "Event", "title": "", "start": "2026-11-05T00:00:00", "duration": "P2D", "showWithoutTime": true, "calendarIds": {"a": true}}),
        ];
        let f = NaiveDate::from_ymd_opt(2026, 11, 1).unwrap();
        let o = occurrences_in(&evs, f, f + Duration::days(60));
        let weekly: Vec<&Occ> = o.iter().filter(|x| x.base == "e1").collect();
        assert_eq!(weekly.len(), 3);
        assert_eq!(weekly[2].id, "e1|2026-11-17T10:00:00");
        assert_eq!((weekly[0].end - weekly[0].start).num_minutes(), 90);
        let allday = o.iter().find(|x| x.base == "e2").unwrap();
        assert_eq!(allday.title, "(no title)");
        assert!(covers(allday, NaiveDate::from_ymd_opt(2026, 11, 6).unwrap()));
        assert!(!covers(allday, NaiveDate::from_ymd_opt(2026, 11, 7).unwrap()));
    }
    #[test]
    fn namespaced_ids() {
        assert_eq!(ns("p", "p", "a"), "a");
        assert_eq!(ns("p", "x", "a"), "x~a");
        assert_eq!(split_ns("x~a"), (Some("x".into()), "a".into()));
        assert_eq!(split_ns("a"), (None, "a".into()));
        let mut e = json!({"id": "e", "calendarIds": {"c": true}});
        namespace_event(&mut e, "p", "x");
        assert_eq!(e["id"], "x~e");
        assert_eq!(e["calendarIds"]["x~c"], true);
        assert_eq!(server_calendar_ids(&e["calendarIds"]), json!({"c": true}));
        assert_eq!(base_id("e|2026-01-01T00:00:00"), "e");
        assert_eq!(rid_of("e|2026-01-01T00:00:00"), Some("2026-01-01T00:00:00"));
        assert_eq!(hex_color("#fff"), Some(Color::from_rgb_u8(255, 255, 255)));
    }
    #[test]
    fn the_worker_expansion_matches_the_old_full_computation() {
        // the pure worker form (expand) and the UI-thread form (occurrences_in, which reads the
        // prefs and the cards itself) give the same list, and that list is written out by hand
        // rather than one form checked against itself
        let evs = vec![
            json!({"id": "e1", "@type": "Event", "title": "Weekly", "start": "2026-11-03T10:00:00", "duration": "PT1H30M", "recurrenceRules": [{"frequency": "weekly", "count": 3}], "calendarIds": {"a": true}}),
            json!({"id": "e2", "@type": "Event", "title": "", "start": "2026-11-05T00:00:00", "duration": "P2D", "showWithoutTime": true, "calendarIds": {"a": true}}),
            json!({"id": "t1", "@type": "Task", "title": "File taxes", "due": "2026-11-06T00:00:00", "showWithoutTime": true, "calendarIds": {"a": true}}),
        ];
        let f = NaiveDate::from_ymd_opt(2026, 11, 1).unwrap();
        let to = f + Duration::days(60);
        let cp = cal_prefs();
        let worker = expand(&evs, &[], &cp, f, to);
        assert_eq!(worker, occurrences_in(&evs, f, to));
        let events: Vec<(String, String, String)> = worker.iter().filter(|o| o.kind != 1)
            .map(|o| (o.title.clone(), o.start.to_string(), o.end.to_string())).collect();
        assert_eq!(events, vec![
            ("Weekly".to_string(), "2026-11-03 10:00:00".to_string(), "2026-11-03 11:30:00".to_string()),
            ("(no title)".to_string(), "2026-11-05 00:00:00".to_string(), "2026-11-07 00:00:00".to_string()),
            ("Weekly".to_string(), "2026-11-10 10:00:00".to_string(), "2026-11-10 11:30:00".to_string()),
            ("Weekly".to_string(), "2026-11-17 10:00:00".to_string(), "2026-11-17 11:30:00".to_string()),
        ]);
        // the task shows on its due date when the settings say so (the default does)
        if cp.tasks && cp.tasks_on_calendar {
            assert!(worker.iter().any(|o| o.kind == 1 && o.base == "t1" && o.start.date() == NaiveDate::from_ymd_opt(2026, 11, 6).unwrap()));
        }
    }
    #[test]
    fn the_year_the_summary_reads_holds_the_events_only() {
        // the worker builds the year the tile counts and "next up" read; it carries events only,
        // and drops what ends before the window starts
        let evs = vec![
            json!({"id": "next", "@type": "Event", "title": "Stand-up", "start": "2026-11-20T09:00:00", "duration": "PT15M", "calendarIds": {"a": true}}),
            json!({"id": "past", "@type": "Event", "title": "Old", "start": "2026-01-05T09:00:00", "duration": "PT15M", "calendarIds": {"a": true}}),
            json!({"id": "t1", "@type": "Task", "title": "File taxes", "due": "2026-11-20T00:00:00", "showWithoutTime": true, "calendarIds": {"a": true}}),
        ];
        let cp = cal_prefs();
        let f = NaiveDateTime::parse_from_str("2026-11-19T00:00:00", "%Y-%m-%dT%H:%M:%S").unwrap();
        let t = f + Duration::days(366);
        let ahead = events_between(&evs, &[], &cp, f, t);
        assert_eq!(ahead.len(), 1, "got {ahead:?}");
        assert_eq!(ahead[0].title, "Stand-up");
        assert!(ahead.iter().all(|o| o.kind == 0), "no tasks or birthdays");
        assert!(events_between(&evs, &[], &cp, f, f + Duration::days(1)).is_empty(), "a window that misses it holds nothing");
    }
    #[test]
    fn bucket_by_day_matches_the_full_scan_across_dst_and_all_day() {
        // a weekly event on America/New_York's own clock across the 2026-03-08 US DST change, a
        // two-day all-day event, and a one-off timed event
        let evs = vec![
            json!({"id": "dst", "@type": "Event", "title": "DST weekly", "start": "2026-03-02T09:30:00", "duration": "PT1H", "timeZone": "America/New_York", "recurrenceRules": [{"frequency": "weekly", "count": 3}], "calendarIds": {"a": true}}),
            json!({"id": "allday", "@type": "Event", "title": "Trip", "start": "2026-03-06T00:00:00", "duration": "P2D", "showWithoutTime": true, "calendarIds": {"a": true}}),
            json!({"id": "once", "@type": "Event", "title": "Once", "start": "2026-03-10T15:00:00", "duration": "PT30M", "calendarIds": {"a": true}}),
        ];
        let from = NaiveDate::from_ymd_opt(2026, 3, 1).unwrap();
        let to = NaiveDate::from_ymd_opt(2026, 3, 20).unwrap();
        let occ = occurrences_in(&evs, from, to);
        let bucket = bucket_by_day(&occ, from, to);
        // each day's bucket equals the old full scan of that day
        let mut d = from;
        while d < to {
            let old: Vec<Occ> = occ.iter().filter(|o| covers(o, d)).cloned().collect();
            assert_eq!(bucket.get(&d).cloned().unwrap_or_default(), old, "day {d}");
            d += Duration::days(1);
        }
        // the weekly event keeps its 09:30 wall clock in its own zone across the DST change and
        // lasts one real hour each time
        let weekly: Vec<&Occ> = occ.iter().filter(|o| o.base == "dst").collect();
        assert_eq!(weekly.len(), 3);
        for o in &weekly {
            let wall = recurrence::local_to_zone(o.start, Some("America/New_York"));
            assert_eq!(wall.format("%H:%M").to_string(), "09:30");
            assert_eq!((o.end - o.start).num_minutes(), 60);
        }
        // the all-day event covers exactly Mar 6 and Mar 7, and is bucketed once under each
        let trip = occ.iter().find(|o| o.base == "allday").unwrap();
        assert!(covers(trip, NaiveDate::from_ymd_opt(2026, 3, 6).unwrap()));
        assert!(covers(trip, NaiveDate::from_ymd_opt(2026, 3, 7).unwrap()));
        assert!(!covers(trip, NaiveDate::from_ymd_opt(2026, 3, 8).unwrap()));
        for day in [6u32, 7] {
            let day = NaiveDate::from_ymd_opt(2026, 3, day).unwrap();
            assert_eq!(bucket.get(&day).unwrap().iter().filter(|o| o.base == "allday").count(), 1);
        }
        assert!(!bucket.get(&NaiveDate::from_ymd_opt(2026, 3, 8).unwrap()).map(|v| v.iter().any(|o| o.base == "allday")).unwrap_or(false));
    }
    #[test]
    fn bucketing_clips_to_the_range_and_keeps_start_order() {
        // a week-long all-day event started before the window (it shows on each visible day it
        // covers, and on no day outside the window), plus two timed events of the same day
        let evs = vec![
            json!({"id": "week", "@type": "Event", "title": "Week", "start": "2026-03-01T00:00:00", "duration": "P7D", "showWithoutTime": true, "calendarIds": {"a": true}}),
            json!({"id": "late", "@type": "Event", "title": "Late", "start": "2026-03-06T11:00:00", "duration": "PT1H", "calendarIds": {"a": true}}),
            json!({"id": "early", "@type": "Event", "title": "Early", "start": "2026-03-06T08:00:00", "duration": "PT1H", "calendarIds": {"a": true}}),
        ];
        let from = NaiveDate::from_ymd_opt(2026, 3, 5).unwrap();
        let to = NaiveDate::from_ymd_opt(2026, 3, 8).unwrap();
        let occ = occurrences_in(&evs, from, to);
        let b = bucket_by_day(&occ, from, to);
        assert!(b.keys().all(|d| *d >= from && *d < to), "no day outside the window is bucketed");
        assert_eq!(b.len(), 3);
        let day = NaiveDate::from_ymd_opt(2026, 3, 6).unwrap();
        let ids: Vec<&str> = b[&day].iter().map(|o| o.base.as_str()).collect();
        assert_eq!(ids, ["week", "early", "late"], "in start order, as the full scan gave");
    }
    #[test]
    fn only_the_newest_expansion_is_drawn_and_the_glance_follows_it() {
        let mut l = Landings::default();
        let first = l.begin();
        assert!(l.land(first), "the only ask: drawn");
        assert!(l.glance_due, "a landing updates the mail tile with what was drawn");
        l.glance_refreshed();
        assert!(!l.glance_due);
        // the user leaves the range while it expands: its answer is dropped and touches nothing
        let left = l.begin();
        let now = l.begin();
        assert!(!l.land(left), "a worker's answer for a range the user already left is dropped");
        assert!(!l.glance_due, "a dropped answer does not update the tile");
        assert_eq!(l.drawn, 1);
        assert!(l.land(now));
        assert_eq!(l.drawn, 2);
        assert!(l.glance_due);
    }

    #[test]
    fn the_range_summary_counts_events_only() {
        // a timed event, an all-day event, a task's due date and a birthday in the same window: the
        // tile counts the two events and only the timed one's minutes (a due date and a birthday
        // are not events)
        let mk = |kind: u8, all_day: bool, s: &str, e: &str, cal: &str| Occ {
            id: format!("o{kind}{s}"), base: String::new(), rid: None, title: "t".into(),
            start: parse_local(s).unwrap(), end: parse_local(e).unwrap(), all_day,
            calendar: cal.into(), location: String::new(), kind, done: false,
        };
        let evs = vec![
            mk(0, false, "2026-11-02T09:00:00", "2026-11-02T10:30:00", "a"),
            mk(0, true, "2026-11-03T00:00:00", "2026-11-04T00:00:00", "a"),
            mk(1, true, "2026-11-02T00:00:00", "2026-11-03T00:00:00", "a"),
            mk(2, true, "2026-11-02T00:00:00", "2026-11-03T00:00:00", "bday"),
        ];
        let f = parse_local("2026-11-01T00:00:00").unwrap();
        let t = parse_local("2026-11-08T00:00:00").unwrap();
        assert_eq!(range_summary(&evs, &HashSet::new(), f, t), (2, 90), "two events, the timed one's 90 minutes");
        // a hidden calendar's events are left out
        let hidden: HashSet<String> = ["a".to_string()].into();
        assert_eq!(range_summary(&evs, &hidden, f, t), (0, 0));
        // and a window that holds none
        assert_eq!(range_summary(&evs, &HashSet::new(), t, t + Duration::days(7)), (0, 0));
    }

    #[test]
    fn a_move_during_an_expansion_drops_it_and_the_rebuild_draws() {
        // patch_local: the events change under the running expansion, so its answer must not draw
        // (it would put the event back where it was), and the rebuild patch_local starts replaces it
        let mut l = Landings::default();
        let running = l.begin();
        l.invalidate();
        let after_move = l.begin();
        assert!(!l.land(running), "the expansion from before the move is dropped");
        assert!(!l.glance_due);
        assert!(l.land(after_move));
        assert_eq!(l.drawn, 1);
        assert!(l.glance_due);
    }
}


// ---------------- tasks (JSCalendar Task via CalendarEvent, like Bulwark) ----------------

/// Bulwark's isTaskObject: @type Task, or task-only fields on a CalDAV VTODO.
pub fn is_task(e: &Value) -> bool {
    e["@type"].as_str().is_some_and(|t| t.eq_ignore_ascii_case("task"))
        || (e["@type"] != "Event" && (e["progress"].is_string() || !e["due"].is_null() || e.get("percentComplete").is_some()))
}

/// How many tasks the sidebar lists (Bulwark shows a few and links to the Tasks view).
pub const SIDEBAR_TASKS: usize = 6;

/// (rows to show, open tasks left out) for tasks sorted open-first.
pub fn sidebar_tasks(sorted: &[Value]) -> (usize, usize) {
    let open = sorted.iter().filter(|t| t["progress"] != "completed").count();
    let shown = open.min(SIDEBAR_TASKS);
    (shown, open - shown)
}

/// A due date (YYYY-MM-DD) before today on a task not yet done.
pub fn task_overdue(due: &str, done: bool, today: &str) -> bool { !done && !due.is_empty() && due < today }

pub fn render_tasks(u: &AppWindow) {
    let mut tasks: Vec<Value> = RAW.with(|r| r.borrow().iter().filter(|e| is_task(e)).cloned().collect());
    tasks.sort_by_key(|t| (t["progress"] == "completed", t["due"].as_str().unwrap_or("9999").to_string()));
    let today = chrono::Local::now().format("%Y-%m-%d").to_string();
    let (shown, more) = sidebar_tasks(&tasks);
    let rows: Vec<crate::TaskRow> = tasks.iter().take(shown).map(|t| {
        let due = t["due"].as_str().map(|d| d[..10.min(d.len())].to_string()).unwrap_or_default();
        let done = t["progress"] == "completed";
        crate::TaskRow {
            id: t["id"].as_str().unwrap_or("").into(),
            title: t["title"].as_str().filter(|x| !x.is_empty()).unwrap_or("(untitled task)").into(),
            overdue: task_overdue(&due, done, &today),
            due: due.into(),
            done,
        }
    }).collect();
    u.set_cal_open_tasks(rows.iter().filter(|t| !t.done).count() as i32);
    sync_into(&u.get_cal_tasks(), rows, |t| t.id.clone(), |a, b| a == b, |m| u.set_cal_tasks(m));
    crate::glance::calendar(u);
    u.global::<crate::CalX>().set_sidebar_more(more as i32);
    if view() == 4 { crate::cal_extra::render_tasks_view(u); }
}

pub fn new_uid() -> String {
    let mut b = [0u8; 16];
    let _ = getrandom::getrandom(&mut b);
    let h: String = b.iter().map(|x| format!("{x:02x}")).collect();
    format!("{}-{}-{}-{}-{}", &h[..8], &h[8..12], &h[12..16], &h[16..20], &h[20..])
}

/// Where a new task goes (Bulwark defaultTaskList): the default calendar of the user's own
/// account, else its first one.
pub fn default_task_list() -> Option<String> {
    ST.with(|s| {
        let s = s.borrow();
        let own: Vec<&Value> = s.calendars.iter().filter(|c| c["_shared"] != true && !crate::cal_extra::is_subscription(c["id"].as_str().unwrap_or(""))).collect();
        own.iter().find(|c| c["isDefault"] == true).or(own.first()).and_then(|c| c["id"].as_str().map(String::from))
    })
}

pub fn add_task(u: &AppWindow, c: Arc<Client>, title: &str, due: &str) {
    let title = title.trim().to_string();
    if title.is_empty() { return; }
    let Some(cal) = default_task_list() else { crate::mail::show_toast(u, "No calendar to add the task to.", false); return };
    let mut task = json!({"@type": "Task", "uid": new_uid(), "title": title, "progress": "needs-action"});
    let due = due.trim();
    if due.len() == 10 { task["due"] = json!(format!("{due}T00:00:00")); task["showWithoutTime"] = json!(true); }
    create_in(u, c, &cal, task, "Task not added");
}

/// Create one CalendarEvent (event or task) in calendar `cal` (namespaced id), then reload.
pub fn create_in(u: &AppWindow, c: Arc<Client>, cal: &str, mut obj: Value, what: &str) {
    let (acc, raw_cal) = server_ref(&c, cal);
    obj["calendarIds"] = json!({ raw_cal: true });
    let (w, what) = (u.as_weak(), what.to_string());
    std::thread::spawn(move || {
        let res = c.call(json!([["CalendarEvent/set", {"accountId": acc, "sendSchedulingMessages": false, "create": {"t": obj}}, "s"]]))
            .and_then(|r| if r[0][1]["created"]["t"].is_object() { Ok(()) } else { Err(crate::jmap::set_error(&r[0][1]["notCreated"]["t"])) });
        let w2 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| match res {
            Ok(()) => load(w2, c),
            Err(e) => crate::mail::show_error(&u, &format!("{what}: {e}")),
        });
    });
}

pub fn toggle_task(u: &AppWindow, c: Arc<Client>, id: &str) {
    let done = RAW.with(|r| r.borrow().iter().find(|e| e["id"] == id).map(|e| e["progress"] == "completed")).unwrap_or(false);
    let progress = if done { "needs-action" } else { "completed" };
    RAW.with(|r| if let Some(e) = Arc::make_mut(&mut r.borrow_mut()).iter_mut().find(|e| e["id"] == id) { e["progress"] = json!(progress); });
    render_tasks(u);
    rebuild(u);
    let (w, id) = (u.as_weak(), id.to_string());
    std::thread::spawn(move || {
        let (acc, sid) = server_ref(&c, &id);
        if let Err(e) = c.update("CalendarEvent/set", &acc, vec![(sid, json!({"progress": progress}))]).and_then(|(_, b)| if b.is_empty() { Ok(()) } else { Err(b.join("; ")) }) {
            let _ = w.upgrade_in_event_loop(move |u| crate::mail::show_toast(&u, &format!("Task not updated: {e}"), false));
        }
    });
}

// ---------------- context-menu helpers ----------------

pub fn event_title(id: &str) -> String {
    let id = base_id(id);
    RAW.with(|r| r.borrow().iter().find(|e| e["id"] == id).and_then(|e| e["title"].as_str().map(String::from))).unwrap_or_default()
}

pub fn task_done(id: &str) -> bool {
    RAW.with(|r| r.borrow().iter().find(|e| e["id"] == id).map(|e| e["progress"] == "completed")).unwrap_or(false)
}

/// (index in the editor's calendar list, name, visible, is default)
pub fn calendar_info(id: &str) -> Option<(usize, String, bool, bool)> {
    ST.with(|s| {
        let s = s.borrow();
        let i = s.calendars.iter().position(|c| c["id"] == id)?;
        let c = &s.calendars[i];
        Some((i, c["name"].as_str().unwrap_or("Calendar").to_string(), !s.hidden.contains(id), c["isDefault"] == true))
    })
}

pub fn calendar_color(id: &str) -> Option<String> {
    ST.with(|s| s.borrow().calendars.iter().find(|c| c["id"] == id).and_then(|c| c["_color"].as_str().or(c["color"].as_str()).map(|x| x.to_lowercase())))
}

/// A calendar's colour: on the server for the user's own calendars; for one shared with the
/// user only for them (Bulwark sharedCalendarColors, keyed by login, account and calendar).
pub fn set_color(u: &AppWindow, c: Arc<Client>, id: &str, color: &str) {
    let Some(cal) = calendar(id) else { return };
    if cal["_shared"] == true {
        let key = shared_color_key(&c.username, cal["_acc"].as_str().unwrap_or(""), cal["_rid"].as_str().unwrap_or(""));
        crate::settings::update_pref("sharedCalendarColors", |v| {
            let mut map = v.cloned().filter(|m| m.is_object()).unwrap_or(json!({}));
            map[key] = json!(color);
            map
        });
        ST.with(|s| if let Some(x) = s.borrow_mut().calendars.iter_mut().find(|x| x["id"] == id) { x["_color"] = json!(color); });
        rebuild(u);
        return;
    }
    set_call(u, c, "Calendar/set", json!({"update": {id: {"color": color}}}), "Colour not changed");
}

/// Run a CalendarEvent/Calendar set call in the background, then reload. Ids in
/// update/destroy may be namespaced: the call goes to their account.
pub fn set_call(u: &AppWindow, c: Arc<Client>, method: &str, args: Value, what: &str) {
    let (w, method, what) = (u.as_weak(), method.to_string(), what.to_string());
    std::thread::spawn(move || {
        let mut a = args;
        // the account of the first id named, and server ids for all of them
        let first = a["update"].as_object().and_then(|o| o.keys().next().cloned())
            .or_else(|| a["destroy"].as_array().and_then(|d| d.first()).and_then(|x| x.as_str().map(String::from)));
        let acc = first.as_deref().map(|f| server_ref(&c, f).0).unwrap_or_else(|| c.account_for(CAL));
        if let Some(o) = a["update"].as_object() {
            let m: serde_json::Map<String, Value> = o.iter().map(|(k, v)| (split_ns(k).1, v.clone())).collect();
            a["update"] = Value::Object(m);
        }
        if let Some(d) = a["destroy"].as_array() {
            a["destroy"] = Value::Array(d.iter().filter_map(|x| x.as_str()).map(|x| json!(split_ns(x).1)).collect());
        }
        a["accountId"] = json!(acc);
        let res = c.call(json!([[method, a, "s"]])).and_then(|r| {
            let o = &r[0][1];
            for k in ["notCreated", "notUpdated", "notDestroyed"] {
                if let Some(m) = o[k].as_object() { if let Some(e) = m.values().next() { return Err(crate::jmap::set_error(e)); } }
            }
            Ok(())
        });
        let w2 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| match res {
            Ok(()) => load(w2, c),
            // re-read on failure too, so an optimistic change the server refused is undone
            Err(e) => { crate::mail::show_error(&u, &format!("{what}: {e}")); load(w2, c); }
        });
    });
}

pub fn set_task_due(u: &AppWindow, c: Arc<Client>, id: &str, due: chrono::NaiveDate) {
    let d = format!("{}T00:00:00", due.format("%Y-%m-%d"));
    RAW.with(|r| if let Some(e) = Arc::make_mut(&mut r.borrow_mut()).iter_mut().find(|e| e["id"] == id) { e["due"] = json!(d); e["showWithoutTime"] = json!(true); });
    render_tasks(u);
    set_call(u, c, "CalendarEvent/set", json!({"update": {id: {"due": d, "showWithoutTime": true}}}), "Task not updated");
}

pub fn task_has_due(id: &str) -> bool {
    RAW.with(|r| r.borrow().iter().find(|e| e["id"] == id).is_some_and(|e| e["due"].is_string()))
}

pub fn clear_task_due(u: &AppWindow, c: Arc<Client>, id: &str) {
    RAW.with(|r| if let Some(e) = Arc::make_mut(&mut r.borrow_mut()).iter_mut().find(|e| e["id"] == id) { if let Some(o) = e.as_object_mut() { o.remove("due"); } });
    render_tasks(u);
    set_call(u, c, "CalendarEvent/set", json!({"update": {id: {"due": null}}}), "Task not updated");
}

pub fn delete_task(u: &AppWindow, c: Arc<Client>, id: &str) {
    RAW.with(|r| Arc::make_mut(&mut r.borrow_mut()).retain(|e| e["id"] != id));
    render_tasks(u);
    rebuild(u);
    set_call(u, c, "CalendarEvent/set", json!({"destroy": [id]}), "Task not deleted");
}

// ---------------- invitations & export ----------------

/// The user's own participant in an event and its answer (Bulwark getUserParticipantId):
/// matched by calendarAddress / email against the login and identities.
pub fn my_participation(id: &str, mine: &[String]) -> Option<(String, String)> {
    let id = base_id(id);
    RAW.with(|r| r.borrow().iter().find(|e| e["id"] == id).and_then(|e| participation_in(e, mine)))
}

fn participation_in(e: &Value, mine: &[String]) -> Option<(String, String)> {
    let ps = e["participants"].as_object()?;
    let is_me = |p: &Value| {
        let addr = participant_email(p);
        !addr.is_empty() && mine.iter().any(|m| m.eq_ignore_ascii_case(&addr))
    };
    // organizers don't answer their own events
    let (pid, p) = ps.iter().find(|(_, p)| is_me(p) && p["roles"]["owner"] != true)?;
    Some((pid.clone(), p["participationStatus"].as_str().unwrap_or("needs-action").to_string()))
}

/// A participant's address (calendarAddress, the retired sendTo.imip, or email), without mailto:.
pub fn participant_email(p: &Value) -> String {
    p["calendarAddress"].as_str().or(p["sendTo"]["imip"].as_str()).or(p["email"].as_str())
        .map(|a| a.trim().trim_start_matches("mailto:").trim_start_matches("MAILTO:").to_string()).unwrap_or_default()
}

/// Answer an invitation: patch the participant's participationStatus and let the server send the iTIP REPLY.
pub fn rsvp(u: &AppWindow, c: Arc<Client>, id: &str, pid: &str, status: &str) {
    let id = base_id(id).to_string();
    let esc = pid.replace('~', "~0").replace('/', "~1");
    RAW.with(|r| if let Some(e) = Arc::make_mut(&mut r.borrow_mut()).iter_mut().find(|e| e["id"] == id) { e["participants"][pid]["participationStatus"] = json!(status); });
    let mut patch = serde_json::Map::new();
    patch.insert(format!("participants/{esc}/participationStatus"), json!(status));
    set_call(u, c, "CalendarEvent/set", json!({"update": {id: Value::Object(patch)}, "sendSchedulingMessages": true}), "Answer not sent");
}

fn ics_escape(s: &str) -> String {
    s.replace('\\', "\\\\").replace(';', "\\;").replace(',', "\\,").replace("\r\n", "\\n").replace('\n', "\\n")
}

/// Fold content lines at 75 octets (RFC 5545 3.1).
fn ics_fold(line: &str) -> String {
    let mut out = String::new();
    let mut n = 0;
    for ch in line.chars() {
        let l = ch.len_utf8();
        if n + l > 75 { out.push_str("\r\n "); n = 1; }
        out.push(ch);
        n += l;
    }
    out.push_str("\r\n");
    out
}

fn ics_local(s: &str) -> String { s.replace(['-', ':'], "").chars().take(15).collect() }

/// One event as an iCalendar file (Bulwark downloadEventICS).
pub fn event_ics(id: &str) -> Option<(String, String)> {
    let id = base_id(id);
    let e = RAW.with(|r| r.borrow().iter().find(|e| e["id"] == id).cloned())?;
    let mut l: Vec<String> = vec!["BEGIN:VCALENDAR".into(), "VERSION:2.0".into(), "PRODID:-//Barbican//EN".into(), "CALSCALE:GREGORIAN".into(), "BEGIN:VEVENT".into()];
    l.push(format!("UID:{}", e["uid"].as_str().unwrap_or(id)));
    l.push(format!("DTSTAMP:{}", chrono::Utc::now().format("%Y%m%dT%H%M%SZ")));
    let start = e["start"].as_str().unwrap_or("");
    let all_day = e["showWithoutTime"] == true;
    let tz = e["timeZone"].as_str().filter(|t| !t.is_empty());
    if all_day {
        l.push(format!("DTSTART;VALUE=DATE:{}", ics_local(start).chars().take(8).collect::<String>()));
        if let Some(st) = parse_local(start) {
            let d = recurrence::event_duration(&e);
            let end = (st + if d.num_days() < 1 { Duration::days(1) } else { d }).date();
            l.push(format!("DTEND;VALUE=DATE:{}", end.format("%Y%m%d")));
        }
    } else {
        match tz {
            Some("Etc/UTC") | Some("UTC") => l.push(format!("DTSTART:{}Z", ics_local(start))),
            Some(tz) => l.push(format!("DTSTART;TZID={tz}:{}", ics_local(start))),
            None => l.push(format!("DTSTART:{}", ics_local(start))),
        }
        if let Some(d) = e["duration"].as_str() { l.push(format!("DURATION:{d}")); }
    }
    l.push(format!("SUMMARY:{}", ics_escape(e["title"].as_str().unwrap_or(""))));
    if let Some(d) = e["description"].as_str().filter(|d| !d.is_empty()) { l.push(format!("DESCRIPTION:{}", ics_escape(d))); }
    if let Some(loc) = e["locations"].as_object().and_then(|o| o.values().next()).and_then(|x| x["name"].as_str()) { l.push(format!("LOCATION:{}", ics_escape(loc))); }
    if let Some(s) = e["status"].as_str() { l.push(format!("STATUS:{}", s.to_uppercase())); }
    if let Some(rules) = e["recurrenceRules"].as_array() {
        for r in rules {
            let mut parts = vec![format!("FREQ={}", r["frequency"].as_str().unwrap_or("daily").to_uppercase())];
            if let Some(i) = r["interval"].as_u64().filter(|i| *i > 1) { parts.push(format!("INTERVAL={i}")); }
            if let Some(n) = r["count"].as_u64() { parts.push(format!("COUNT={n}")); }
            if let Some(u) = r["until"].as_str() { parts.push(format!("UNTIL={}", ics_local(u))); }
            if let Some(days) = r["byDay"].as_array() {
                let d: Vec<String> = days.iter().filter_map(|d| d["day"].as_str().map(|x| format!("{}{}", d["nthOfPeriod"].as_i64().map(|n| n.to_string()).unwrap_or_default(), x.to_uppercase()))).collect();
                if !d.is_empty() { parts.push(format!("BYDAY={}", d.join(","))); }
            }
            if let Some(md) = r["byMonthDay"].as_array() { parts.push(format!("BYMONTHDAY={}", md.iter().map(|x| x.to_string()).collect::<Vec<_>>().join(","))); }
            if let Some(m) = r["byMonth"].as_array() { parts.push(format!("BYMONTH={}", m.iter().filter_map(|x| x.as_str()).collect::<Vec<_>>().join(","))); }
            l.push(format!("RRULE:{}", parts.join(";")));
        }
    }
    if let Some(ps) = e["participants"].as_object() {
        for p in ps.values() {
            let addr = participant_email(p);
            if addr.is_empty() { continue; }
            let addr = format!("mailto:{addr}");
            let cn = p["name"].as_str().filter(|n| !n.is_empty()).map(|n| format!(";CN=\"{}\"", n.replace('"', ""))).unwrap_or_default();
            if p["roles"]["owner"] == true { l.push(format!("ORGANIZER{cn}:{addr}")); }
            if p["roles"]["attendee"] == true || p["roles"]["owner"] != true {
                let ps = p["participationStatus"].as_str().unwrap_or("needs-action").to_uppercase();
                l.push(format!("ATTENDEE{cn};PARTSTAT={ps}:{addr}"));
            }
        }
    }
    l.extend(["END:VEVENT".into(), "END:VCALENDAR".into()]);
    let title = e["title"].as_str().filter(|t| !t.is_empty()).unwrap_or("event");
    Some((format!("{}.ics", crate::sanitize_name(title)), l.iter().map(|x| ics_fold(x)).collect()))
}

// ---------------- mail glance (summary tiles, sender panel) ----------------

/// Today's events for the mail summary tile: how many, and the next one still to come
/// ("Stand-up 10:30"). None until the calendar has loaded a range that covers today.
pub fn glance_today() -> Option<(usize, String)> {
    let today = Local::now().date_naive();
    let (from, to) = range();
    if today < from || today >= to { return None; }
    let now = Local::now().naive_local();
    let h12 = cal_prefs().h12;
    ST.with(|s| {
        let s = s.borrow();
        if s.calendars.is_empty() { return None; }
        let on: Vec<&Occ> = s.events.iter()
            .filter(|o| !s.hidden.contains(&o.calendar))
            .filter(|o| o.start.date() <= today && (o.end.date() > today || o.start.date() == today))
            .collect();
        let next = on.iter().find(|o| o.end > now && !o.all_day).or(on.iter().find(|o| o.all_day))
            .map(|o| if o.all_day { o.title.clone() } else { format!("{} {}", o.title, fmt_time(o.start, h12)) })
            .unwrap_or_default();
        Some((on.len(), next))
    })
}

/// Open tasks for the summary tile: how many, and the first by due date ("Title · 2026-11-20").
pub fn glance_tasks() -> Option<(usize, String)> {
    if ST.with(|s| s.borrow().calendars.is_empty()) { return None; }
    let mut open: Vec<Value> = RAW.with(|r| r.borrow().iter().filter(|e| is_task(e) && e["progress"] != "completed").cloned().collect());
    open.sort_by_key(|t| t["due"].as_str().unwrap_or("9999").to_string());
    let next = open.first().map(|t| {
        let title = t["title"].as_str().filter(|x| !x.is_empty()).unwrap_or("(untitled task)");
        match t["due"].as_str().and_then(|d| NaiveDate::parse_from_str(&d[..10.min(d.len())], "%Y-%m-%d").ok()) {
            Some(d) => format!("{title} · {}", d.format("%a %-d %b")),
            None => title.to_string(),
        }
    }).unwrap_or_default();
    Some((open.len(), next))
}

/// The next loaded event (from now) with this address among its participants: ("Thu 14:00", title).
pub fn next_with(email: &str) -> Option<(String, String)> {
    if email.is_empty() { return None; }
    let e = email.to_lowercase();
    let with: HashSet<String> = RAW.with(|r| r.borrow().iter().filter(|ev| {
        ev["participants"].as_object().is_some_and(|ps| ps.values().any(|p| {
            p["email"].as_str().is_some_and(|x| x.eq_ignore_ascii_case(&e))
                || p["sendTo"]["imip"].as_str().is_some_and(|x| x.to_lowercase().trim_start_matches("mailto:") == e)
        }))
    }).filter_map(|ev| ev["id"].as_str().map(String::from)).collect());
    if with.is_empty() { return None; }
    let now = Local::now().naive_local();
    let h12 = cal_prefs().h12;
    ST.with(|s| s.borrow().events.iter().filter(|o| o.end > now && with.contains(&o.id)).min_by_key(|o| o.start)
        .map(|o| (format!("{} {}", o.start.format("%a"), if o.all_day { "all day".to_string() } else { fmt_time(o.start, h12) }), o.title.clone())))
}

// ---------------- design: summary tiles, meaning tints, the event context panel ----------------

thread_local! {
    /// Events with an invitation the user has not answered (drawn amber with a border).
    static PENDING: RefCell<HashSet<String>> = RefCell::new(HashSet::new());
}

fn pending_ids(mine: &[String]) -> HashSet<String> {
    if mine.is_empty() { return HashSet::new(); }
    RAW.with(|r| r.borrow().iter()
        .filter(|e| participation_in(e, mine).is_some_and(|(_, st)| st == "needs-action"))
        .filter_map(|e| e["id"].as_str().map(String::from)).collect())
}

fn dur_text(mins: i64) -> String {
    match (mins / 60, mins % 60) { (0, m) => format!("{m} m"), (h, 0) => format!("{h} h"), (h, m) => format!("{h} h {m} m") }
}

fn plural(n: usize, one: &str, many: &str) -> String { if n == 1 { format!("1 {one}") } else { format!("{n} {many}") } }

fn when_text(o: &Occ, h12: bool) -> String {
    let day = o.start.format("%a %-d %b").to_string();
    if o.all_day { format!("{day} · all day") } else { format!("{day} · {} – {}", fmt_time(o.start, h12), fmt_time(o.end, h12)) }
}

/// The range summary the tiles and the Nordic line show: how many events (not tasks or birthdays —
/// a due date or a birthday is not an event) are visible in the range, and how many minutes of them
/// are timed. Pure, so the counting is tested directly.
fn range_summary(events: &[Occ], hidden: &HashSet<String>, f: NaiveDateTime, t: NaiveDateTime) -> (usize, i64) {
    let inr: Vec<&Occ> = events.iter()
        .filter(|o| o.kind == 0 && !hidden.contains(&o.calendar) && o.start < t && o.end > f)
        .collect();
    let mins: i64 = inr.iter().filter(|o| !o.all_day).map(|o| (o.end.min(t) - o.start.max(f)).num_minutes().max(0)).sum();
    (inr.len(), mins)
}

/// The three Bento summary tiles (and the Nordic sidebar line), from the loaded range only:
/// the next event to come, what this day / week / month holds, and unanswered invitations.
fn summary(u: &AppWindow) {
    let now = Local::now().naive_local();
    let (from, to) = range();
    let h12 = cal_prefs().h12;
    let view = ST.with(|s| s.borrow().view);
    ST.with(|s| {
        let s = s.borrow();
        // the drawn range (the worker's expansion) and the year it also built: right straight
        // after a view / range change, and "next up" is the next event overall, not only here
        let (f, t) = (from.and_hms_opt(0, 0, 0).unwrap(), to.and_hms_opt(0, 0, 0).unwrap());
        let ahead = &s.ahead;
        // next up
        let next = ahead.iter().filter(|o| !s.hidden.contains(&o.calendar) && !o.all_day && o.start >= now).min_by_key(|o| o.start);
        u.set_cal_sum_next(match next {
            Some(o) => {
                let m = (o.start - now).num_minutes();
                let when = if m < 60 { format!("in {} min", m.max(1)) } else if o.start.date() == now.date() { format!("in {}", dur_text(m - m % 60)) } else { o.start.format("%a %-d %b").to_string() };
                CalSum { label: format!("Next up · {when}").into(), value: o.title.as_str().into(),
                    sub: if o.location.is_empty() { fmt_time(o.start, h12) } else { format!("{} · {}", fmt_time(o.start, h12), o.location) }.into(), id: o.id.as_str().into() }
            }
            None => CalSum { label: "Next up".into(), value: "Nothing coming".into(), sub: "No upcoming events".into(), id: "".into() },
        });
        // what the range holds (events only: tasks and birthdays are not events)
        let (count, mins) = range_summary(&s.events, &s.hidden, f, t);
        let label = match view { 0 => "This day", 1 => "This week", _ => "This month" };
        u.set_cal_sum_range(CalSum { label: label.into(), value: plural(count, "event", "events").into(),
            sub: if mins > 0 { format!("{} scheduled", dur_text(mins)) } else { "Nothing timed".to_string() }.into(), id: "".into() });
        let wk = if view == 1 { format!("Week {} · ", from.iso_week().week()) } else { String::new() };
        u.set_cal_side_line(format!("{wk}{}{}", plural(count, "event", "events"), if mins > 0 { format!(" · {} scheduled", dur_text(mins)) } else { String::new() }).into());
        // invitations still to answer (upcoming ones)
        let pend = PENDING.with(|p| p.borrow().clone());
        let mut inv: Vec<&Occ> = ahead.iter().filter(|o| !s.hidden.contains(&o.calendar) && o.end > now && pend.contains(&o.base)).collect();
        inv.sort_by_key(|o| o.start);
        let mut seen = HashSet::new();
        inv.retain(|o| seen.insert(o.id.clone()));
        u.set_cal_sum_inv(match inv.first() {
            Some(o) => CalSum { label: "Invitations".into(), value: format!("{} to answer", inv.len()).into(),
                sub: format!("{} · {}", o.title, o.start.format("%a")).into(), id: o.id.as_str().into() },
            None => CalSum { label: "Invitations".into(), value: "None to answer".into(), sub: "You're all caught up".into(), id: "".into() },
        });
    });
}

/// Show an event in the context panel (empty id: back to the automatic choice).
pub fn select(u: &AppWindow, id: &str) {
    ST.with(|s| s.borrow_mut().selected = if id.is_empty() { None } else { Some(id.to_string()) });
    // only the panel: the grid models stay, so a double click on the same event still opens it
    detail(u);
}

fn guest_status(p: &Value) -> (&'static str, i32) {
    if p["roles"]["owner"] == true { return ("Organizer", 0); }
    match p["participationStatus"].as_str().unwrap_or("needs-action") {
        "accepted" => ("Accepted", 1),
        "declined" => ("Declined", 0),
        "tentative" => ("Maybe", 0),
        "delegated" => ("Delegated", 0),
        _ => ("No reply", 2),
    }
}

/// Fill the context panel: the selected event, or else the next one to come in the loaded range.
fn detail(u: &AppWindow) {
    let now = Local::now().naive_local();
    let h12 = cal_prefs().h12;
    let mine = crate::my_addrs_cached();
    let pick: Option<Occ> = ST.with(|s| {
        let s = s.borrow();
        match &s.selected {
            Some(id) => {
                let all: Vec<&Occ> = s.events.iter().filter(|o| &o.id == id).collect();
                all.iter().find(|o| o.end > now).or(all.last()).map(|o| (*o).clone()).or_else(|| {
                    // chosen from a summary tile: may lie outside the loaded range
                    s.ahead.iter().find(|o| &o.id == id).cloned()
                })
            }
            // nothing chosen: the next event to come, else the last one that has ended
            None => {
                let vis = || s.events.iter().filter(|o| !s.hidden.contains(&o.calendar));
                vis().filter(|o| o.end > now).min_by_key(|o| o.start).or_else(|| vis().max_by_key(|o| o.end)).cloned()
            }
        }
    });
    let Some(o) = pick else {
        u.set_cal_has_sel(false);
        u.set_cal_guests(ModelRc::new(VecModel::from(Vec::<CalGuest>::new())));
        return;
    };
    let raw = RAW.with(|r| r.borrow().iter().find(|e| e["id"] == o.base.as_str()).cloned()).unwrap_or(Value::Null);
    let (cal_name, color) = ST.with(|s| {
        let s = s.borrow();
        let n = s.calendars.iter().find(|c| c["id"] == o.calendar.as_str()).and_then(|c| c["name"].as_str()).unwrap_or("Calendar").to_string();
        // "Stalwart Calendar (mail@example.com)": the account part is noise in the panel header
        let n = match n.rfind(" (") { Some(i) if n.ends_with(')') && i > 0 => n[..i].to_string(), _ => n };
        (n, color_for(&s.calendars, &o.calendar))
    });
    let my = participation_in(&raw, &mine);
    let mut guests: Vec<CalGuest> = vec![];
    let mut organizer = String::new();
    if let Some(ps) = raw["participants"].as_object() {
        let mut list: Vec<&Value> = ps.values().collect();
        list.sort_by_key(|p| (p["roles"]["owner"] != true, p["name"].as_str().unwrap_or("").to_lowercase()));
        for p in list {
            let addr = Some(participant_email(p)).unwrap_or_default();
            let me = !addr.is_empty() && mine.iter().any(|m| m.eq_ignore_ascii_case(&addr));
            let name = p["name"].as_str().filter(|n| !n.trim().is_empty()).map(String::from).unwrap_or_else(|| addr.clone());
            if p["roles"]["owner"] == true && organizer.is_empty() { organizer = if me { "you".into() } else { name.clone() }; }
            let (status, tone) = guest_status(p);
            let shown = if me { "You".to_string() } else { name.clone() };
            let ini: String = shown.split(|c: char| c.is_whitespace() || c == '@' || c == '.').filter(|w| !w.is_empty()).take(2)
                .filter_map(|w| w.chars().next()).flat_map(|c| c.to_uppercase()).collect();
            guests.push(CalGuest { name: shown.into(), email: addr.as_str().into(), initials: ini.into(), status: status.into(), tone, me, color: crate::mail::tint(&addr) });
        }
    }
    let repeat = match raw["recurrenceRules"][0]["frequency"].as_str() {
        Some("daily") => "Repeats every day", Some("weekly") => "Repeats every week", Some("monthly") => "Repeats every month",
        Some("yearly") => "Repeats every year", Some(_) => "Repeats", None => "Does not repeat",
    };
    // other visible events at the same time (an overlap check on what is loaded, not a free/busy lookup)
    let clash: Vec<String> = ST.with(|s| {
        let s = s.borrow();
        s.events.iter().filter(|x| !s.hidden.contains(&x.calendar) && x.id != o.id && !x.all_day && !o.all_day && x.start < o.end && x.end > o.start)
            .map(|x| x.title.clone()).collect()
    });
    let conflict = match clash.len() {
        0 if o.all_day => String::new(),
        0 => "No other events at this time.".to_string(),
        1 => format!("Overlaps with {}.", clash[0]),
        n => format!("Overlaps with {} and {} more.", clash[0], n - 1),
    };
    let desc = raw["description"].as_str().unwrap_or("").trim().lines().map(str::trim).filter(|l| !l.is_empty()).take(3).collect::<Vec<_>>().join(" ");
    u.set_cal_sel(CalDetail {
        id: o.id.as_str().into(), title: o.title.as_str().into(), when: when_text(&o, h12).into(),
        date: o.start.format("%A %-d %B").to_string().into(),
        time: if o.all_day { "All day".to_string() } else { format!("{} – {}", fmt_time(o.start, h12), fmt_time(o.end, h12)) }.into(),
        location: o.location.as_str().into(), calendar: cal_name.into(), color, repeat: repeat.into(),
        status: my.map(|(_, st)| st).unwrap_or_default().into(), organizer: organizer.into(), conflict: conflict.into(),
        description: desc.into(), past: o.end <= now,
    });
    sync_seq(&u.get_cal_guests(), guests, |m| u.set_cal_guests(m));
    u.set_cal_has_sel(true);
}

/// Answer the invitation shown in the context panel.
pub fn rsvp_selected(u: &AppWindow, c: Arc<Client>, status: &str, mine: &[String]) {
    let id = u.get_cal_sel().id.to_string();
    let Some((pid, _)) = my_participation(&id, mine) else { return };
    rsvp(u, c, &id, &pid, status);
    render(u);
}

// ---------------- drag and drop (src/dnd.rs) ----------------

/// An event's length (all-day events without one last a day; raw_event is above).
pub fn duration_of(e: &Value) -> Duration { crate::recurrence::event_duration(e) }

/// Apply a moved event to what is shown right away, then expand the range again so the summary
/// tiles and the next push agree with it (the reload after the server write confirms).
pub fn patch_local(u: &AppWindow, id: &str, patch: &Value) {
    // an expansion started before the move must not draw over it
    LAND.with(|l| l.borrow_mut().invalidate());
    RAW.with(|r| {
        if let Some(e) = Arc::make_mut(&mut r.borrow_mut()).iter_mut().find(|e| e["id"] == id) {
            if let Some(o) = patch.as_object() { for (k, v) in o { e[k] = v.clone(); } }
        }
    });
    let Some(e) = raw_event(id) else { return };
    let Some(start) = e["start"].as_str().and_then(parse_local) else { return };
    let end = start + duration_of(&e);
    ST.with(|s| {
        for o in s.borrow_mut().events.iter_mut().filter(|o| o.id == id) { o.start = start; o.end = end; }
    });
    // the expansion that was dropped is replaced by one from the moved events
    rebuild(u);
}
