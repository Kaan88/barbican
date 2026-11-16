//! Calendar reminders (Bulwark lib/calendar-alerts.ts, hooks/use-calendar-alerts.ts,
//! missed-reminders-dialog.tsx): the alert rows of the event editor, which alerts an event
//! fires (its own, or its calendar's defaults while useDefaultAlerts is on), when, and the
//! runner that shows them in the window and as a desktop notification
//! (calendarNotificationsEnabled, calendarNotificationSound). What came due while the app was
//! closed is listed once in the missed-reminders dialog.
use crate::calendar::{self, CAL};
use crate::jmap::Client;
use crate::recurrence::{self, parse_local};
use crate::AppWindow;
use chrono::{Local, NaiveDateTime, TimeZone};
use serde_json::{json, Map, Value};
use slint::{ComponentHandle, Model, ModelRc, VecModel};
use std::cell::RefCell;
use std::collections::HashMap;
use std::sync::Arc;

/// Reminders older than this when first seen are stale, not shown (Bulwark STALE_THRESHOLD_MS).
pub const STALE_MS: i64 = 10 * 60 * 1000;
/// How far back the missed-reminders dialog looks.
pub const MISSED_WINDOW_MS: i64 = 7 * 24 * 3600 * 1000;
/// How far ahead events are read: at least 8 days, and as far as the longest calendar default
/// alert reaches (an event alert longer than that is caught by the start margin in fetch_ahead).
const FETCH_AHEAD_MS: i64 = 8 * 86_400_000;

/// The read-ahead window for these calendars: FETCH_AHEAD_MS, or the longest default alert plus a day.
pub fn fetch_ahead(cals: &[Value]) -> i64 {
    let longest = cals.iter().flat_map(|c| ["defaultAlertsWithTime", "defaultAlertsWithoutTime"].into_iter().filter_map(move |k| c[k].as_object()))
        .flat_map(|m| m.values()).filter_map(|a| a["trigger"]["offset"].as_str().and_then(offset_ms)).map(|x| x.abs()).max().unwrap_or(0);
    FETCH_AHEAD_MS.max(longest + 86_400_000)
}
const REFETCH_SECS: u64 = 300;

/// One editable reminder: an amount and unit before the start, and how it fires.
#[derive(Clone, Debug, PartialEq)]
pub struct Row {
    pub value: i64,
    /// 0 at time of event, 1 minutes, 2 hours, 3 days, 4 weeks
    pub unit: i32,
    /// 0 display (notification), 1 email
    pub action: i32,
}

/// An editor row and the event alert it was read from (id and row as read): a row left as it
/// was keeps that alert, under its id.
#[derive(Clone, Debug)]
pub struct EdRow { pub row: Row, pub source: Option<(String, Row)> }

pub fn offset_ms(s: &str) -> Option<i64> { recurrence::duration_secs(s).map(|x| x * 1000) }

/// Bulwark offsetToAlertUnit: offsets at or before the start as an amount and unit; offsets
/// after it and compound ones (-P1DT2H) can't be rows.
pub fn offset_to_unit(offset: &str) -> Option<(i64, i32)> {
    let o = offset.trim();
    let zero = o.trim_start_matches('-');
    if ["PT0S", "PT0M", "PT0H", "P0D", "P0W", "PT0D"].contains(&zero) { return Some((0, 0)); }
    let rest = o.strip_prefix('-')?;
    let num = |s: &str| s.parse::<i64>().ok();
    if let Some(w) = rest.strip_prefix('P').and_then(|r| r.strip_suffix('W')).and_then(num) { return Some((w, 4)); }
    if let Some(d) = rest.strip_prefix('P').and_then(|r| r.strip_suffix('D')).and_then(num) { return Some((d, 3)); }
    if let Some(h) = rest.strip_prefix("PT").and_then(|r| r.strip_suffix('H')).and_then(num) { return Some((h, 2)); }
    if let Some(m) = rest.strip_prefix("PT").and_then(|r| r.strip_suffix('M')).and_then(num) {
        if m > 0 && m % 1440 == 0 { return Some((m / 1440, 3)); }
        if m > 0 && m % 60 == 0 { return Some((m / 60, 2)); }
        return Some((m, 1));
    }
    None
}

/// An alert as a row, or None when the rows can't show it (absolute triggers, relative to the
/// end, after the start, or actions other than display/email).
pub fn describe(alert: &Value) -> Option<Row> {
    if alert["trigger"]["@type"].as_str().unwrap_or("OffsetTrigger") != "OffsetTrigger" { return None; }
    if alert["trigger"]["relativeTo"] == "end" { return None; }
    let action = match alert["action"].as_str().unwrap_or("display") { "display" => 0, "email" => 1, _ => return None };
    let (value, unit) = offset_to_unit(alert["trigger"]["offset"].as_str()?)?;
    Some(Row { value, unit, action })
}

pub fn row_offset(r: &Row) -> Option<String> {
    if r.unit == 0 { return Some("PT0S".into()); }
    let v = r.value;
    if v <= 0 { return None; }
    Some(match r.unit { 1 => format!("-PT{v}M"), 2 => format!("-PT{v}H"), 3 => format!("-P{v}D"), _ => format!("-P{v}W") })
}

/// "15 minutes before", "At time of event", "... by email".
pub fn row_label(r: &Row) -> String {
    let plural = |n: i64, w: &str| format!("{n} {w}{} before", if n == 1 { "" } else { "s" });
    let l = match r.unit { 0 => "At time of event".into(), 1 => plural(r.value, "minute"), 2 => plural(r.value, "hour"), 3 => plural(r.value, "day"), _ => plural(r.value, "week") };
    if r.action == 1 { format!("{l} by email") } else { l }
}

/// One line for an alert map (Bulwark AlertSummary / formatEventAlertsLabel).
pub fn alerts_label(alerts: Option<&Value>) -> String {
    let Some(m) = alerts.and_then(|a| a.as_object()).filter(|m| !m.is_empty()) else { return "No reminders".into() };
    let mut labels = vec![];
    let mut other = 0;
    for a in m.values() { match describe(a) { Some(r) => labels.push(row_label(&r)), None => other += 1 } }
    if other > 0 { labels.push(format!("{other} other")); }
    labels.join(", ")
}

/// The calendar's default alerts for a timed or an all-day event (Bulwark getCalendarDefaultAlerts).
pub fn calendar_defaults(cal: Option<&Value>, all_day: bool) -> Option<Value> {
    let a = cal?[if all_day { "defaultAlertsWithoutTime" } else { "defaultAlertsWithTime" }].clone();
    if a.as_object().is_some_and(|m| !m.is_empty()) { Some(a) } else { None }
}

/// The alerts an event fires: the calendar's defaults while it uses them, its own otherwise;
/// Stalwart copies the defaults into the event when it is created, and that copy wins
/// (Bulwark getEffectiveAlerts).
pub fn effective_alerts(e: &Value, cal: Option<&Value>) -> Option<Value> {
    let own = e["alerts"].as_object().filter(|m| !m.is_empty()).map(|_| e["alerts"].clone());
    if e["useDefaultAlerts"] != true { return own; }
    own.or_else(|| calendar_defaults(cal, e["showWithoutTime"] == true))
}

/// The rows of an alert map, and the alerts the rows can't show (kept as they are on save).
pub fn rows_of(alerts: Option<&Value>) -> (Vec<EdRow>, Map<String, Value>) {
    let mut rows = vec![];
    let mut kept = Map::new();
    if let Some(m) = alerts.and_then(|a| a.as_object()) {
        for (id, a) in m {
            match describe(a) {
                Some(r) => rows.push(EdRow { row: r.clone(), source: Some((id.clone(), r)) }),
                None => { kept.insert(id.clone(), a.clone()); }
            }
        }
    }
    (rows, kept)
}

/// `alerts` under new ids alert1, alert2, ... none of them in `taken` (Bulwark withOwnAlertIds).
pub fn with_own_ids(alerts: Vec<Value>, taken: &[String]) -> Map<String, Value> {
    let mut out = Map::new();
    let mut n = 0;
    for a in alerts {
        let id = loop {
            n += 1;
            let id = format!("alert{n}");
            if !taken.contains(&id) { break id; }
        };
        out.insert(id, a);
    }
    out
}

fn new_alert(offset: &str, action: i32) -> Value {
    json!({"@type": "Alert", "trigger": {"@type": "OffsetTrigger", "offset": offset, "relativeTo": "start"}, "action": if action == 1 { "email" } else { "display" }})
}

/// What a save sends for the reminders the user set themselves (Bulwark buildAlertsPatch):
/// nothing when they are as the event had them; unchanged rows keep their alert and id, new and
/// changed rows become new alerts. An event that used its calendar's defaults leaves them, and
/// its alerts get ids of their own (Stalwart 1.0 drops alerts under default-alert ids).
pub fn build_alerts_patch(event: Option<&Value>, rows: &[EdRow], preserved: &Map<String, Value>, reserved: &[String]) -> Map<String, Value> {
    let original = event.and_then(|e| e["alerts"].as_object()).cloned().unwrap_or_default();
    let mut kept = preserved.clone();
    let mut added = vec![];
    for r in rows {
        let Some(offset) = row_offset(&r.row) else { continue };
        if let Some((id, was)) = &r.source {
            if *was == r.row && original.contains_key(id) { kept.insert(id.clone(), original[id].clone()); continue; }
        }
        added.push(new_alert(&offset, r.row.action));
    }
    let uses_defaults = event.is_some_and(|e| e["useDefaultAlerts"] == true);
    let leaves_defaults = event.is_none() || uses_defaults;
    let kept_all = original.keys().all(|k| kept.contains_key(k));
    let mut patch = Map::new();
    if !leaves_defaults && added.is_empty() && kept_all { return patch; }
    let mut taken: Vec<String> = original.keys().cloned().collect();
    taken.extend(reserved.iter().cloned());
    let alerts = if uses_defaults {
        with_own_ids(kept.values().cloned().chain(added).collect(), &taken)
    } else {
        let mut m = kept.clone();
        m.extend(with_own_ids(added, &taken));
        m
    };
    if leaves_defaults { patch.insert("useDefaultAlerts".into(), json!(false)); }
    if !alerts.is_empty() { patch.insert("alerts".into(), Value::Object(alerts)); } else if event.is_some() { patch.insert("alerts".into(), Value::Null); }
    patch
}

// ---------------- when alerts fire ----------------

/// A reminder that comes due.
#[derive(Clone, Debug, PartialEq)]
pub struct Due {
    pub key: String,
    pub event: String,
    pub alert: String,
    pub fire_ms: i64,
    pub start_ms: Option<i64>,
    pub title: String,
    pub calendar: String,
    pub task: bool,
}

pub fn alert_key(event: &str, alert: &str, fire_ms: i64) -> String { format!("{event}:{alert}:{fire_ms}") }

fn trigger_time(trigger: &Value, start_ms: Option<i64>, end_ms: Option<i64>) -> Option<i64> {
    if trigger["@type"] == "AbsoluteTrigger" {
        return chrono::DateTime::parse_from_rfc3339(trigger["when"].as_str()?).ok().map(|t| t.timestamp_millis());
    }
    let off = offset_ms(trigger["offset"].as_str()?)?;
    let base = if trigger["relativeTo"] == "end" { end_ms } else { start_ms }?;
    Some(base + off)
}

/// The instant of a wall clock of `e` (its zone, or the user's for floating and all-day times).
fn instant(e: &Value, wall: NaiveDateTime) -> i64 {
    let zone = if e["showWithoutTime"] == true { None } else { e["timeZone"].as_str().filter(|z| !z.is_empty()) };
    recurrence::zone_to_utc_ms(wall, zone)
}

/// Every display alert of `events` (and tasks) that fires in [from_ms, to_ms).
pub fn due_between(events: &[Value], cals: &[Value], from_ms: i64, to_ms: i64, tasks: bool) -> Vec<Due> {
    let mut out = vec![];
    let utc = |ms: i64| chrono::Utc.timestamp_millis_opt(ms).single().map(|t| t.naive_utc()).unwrap_or_default();
    for e in events {
        if e["status"] == "cancelled" { continue; }
        let cal_id = calendar::first_calendar(e);
        let cal = cals.iter().find(|c| c["id"] == cal_id.as_str());
        let cal_name = cal.and_then(|c| c["name"].as_str()).unwrap_or("").to_string();
        let id = e["id"].as_str().unwrap_or("").to_string();
        let title = e["title"].as_str().unwrap_or("").to_string();
        if calendar::is_task(e) {
            if !tasks || e["progress"] == "completed" || e["progress"] == "cancelled" { continue; }
            let Some(alerts) = e["alerts"].as_object() else { continue };
            let due = e["due"].as_str().and_then(parse_local).map(|d| instant(e, d));
            let start = e["start"].as_str().and_then(parse_local).map(|d| instant(e, d));
            for (aid, a) in alerts {
                if a["action"].as_str().unwrap_or("display") != "display" || a["acknowledged"].is_string() { continue; }
                // RFC 8984: relativeTo start is a task's start, end its due; no start: its due
                let base = if a["trigger"]["relativeTo"] != "end" && start.is_some() { start } else { due };
                let Some(fire) = trigger_time(&a["trigger"], base, due) else { continue };
                if fire >= from_ms && fire < to_ms {
                    out.push(Due { key: alert_key(&id, aid, fire), event: id.clone(), alert: aid.clone(), fire_ms: fire, start_ms: due, title: title.clone(), calendar: cal_name.clone(), task: true });
                }
            }
            continue;
        }
        let Some(alerts) = effective_alerts(e, cal) else { continue };
        let Some(alerts) = alerts.as_object().cloned() else { continue };
        if !alerts.values().any(|a| a["action"].as_str().unwrap_or("display") == "display") { continue; }
        // occurrences whose start lies within a margin of the window (alerts can be weeks early)
        let longest = alerts.values().filter_map(|a| a["trigger"]["offset"].as_str().and_then(offset_ms)).map(|x| x.abs()).max().unwrap_or(0);
        let margin = longest + 2 * 86_400_000;
        let (wf, wt) = (utc(from_ms - margin), utc(to_ms + margin));
        let zone = if e["showWithoutTime"] == true { None } else { e["timeZone"].as_str().filter(|z| !z.is_empty()) };
        let (zf, zt) = (recurrence::local_to_zone(Local.from_utc_datetime(&wf).naive_local(), zone), recurrence::local_to_zone(Local.from_utc_datetime(&wt).naive_local(), zone));
        for o in recurrence::occurrences(e, zf, zt) {
            let (s, en) = (instant(e, o.start), instant(e, o.end));
            let occ_title = o.patch.as_ref().and_then(|p| p["title"].as_str()).map(String::from).unwrap_or_else(|| title.clone());
            for (aid, a) in &alerts {
                if a["action"].as_str().unwrap_or("display") != "display" || a["acknowledged"].is_string() { continue; }
                let Some(fire) = trigger_time(&a["trigger"], Some(s), Some(en)) else { continue };
                if fire >= from_ms && fire < to_ms {
                    let eid = match &o.rid { Some(r) => format!("{id}|{r}"), None => id.clone() };
                    out.push(Due { key: alert_key(&eid, aid, fire), event: eid, alert: aid.clone(), fire_ms: fire, start_ms: Some(s), title: occ_title.clone(), calendar: cal_name.clone(), task: false });
                }
            }
        }
    }
    out.sort_by_key(|d| d.fire_ms);
    out
}

/// "Now", "In 10 minutes", "Due" (Bulwark alert_now / alert_in_minutes / task_due).
pub fn when_label(d: &Due, now_ms: i64) -> String {
    if d.task { return "Task due".into(); }
    let Some(s) = d.start_ms else { return "Now".into() };
    let mins = ((s - now_ms) as f64 / 60000.0).round() as i64;
    if mins <= 0 { "Now".into() } else if mins == 1 { "In 1 minute".into() } else if mins < 120 { format!("In {mins} minutes") } else {
        Local.timestamp_millis_opt(s).single().map(|t| format!("At {}", calendar::fmt_time(t.naive_local(), calendar::h12()))).unwrap_or_default()
    }
}

// ---------------- what was shown / acknowledged (kept in calendar.json) ----------------

fn state_path() -> std::path::PathBuf { crate::config_dir().join("calendar.json") }

pub fn load_state() -> Value {
    std::fs::read_to_string(state_path()).ok().and_then(|t| serde_json::from_str(&t).ok()).filter(|v: &Value| v.is_object()).unwrap_or(json!({}))
}

pub fn save_state(v: &Value) {
    let _ = std::fs::create_dir_all(crate::config_dir());
    let _ = std::fs::write(state_path(), v.to_string());
}

/// Mark reminders as dealt with (shown, dismissed or listed as missed); entries older than the
/// missed window are dropped.
pub fn acknowledge(owner: &str, keys: &[(String, i64)], now_ms: i64) {
    let mut st = load_state();
    let mut acked = st["acked"][owner].as_object().cloned().unwrap_or_default();
    for (k, fire) in keys { acked.insert(k.clone(), json!(fire)); }
    acked.retain(|_, v| v.as_i64().is_some_and(|f| f > now_ms - MISSED_WINDOW_MS - 86_400_000));
    st["acked"][owner] = Value::Object(acked);
    save_state(&st);
}

pub fn acked(owner: &str) -> std::collections::HashSet<String> {
    load_state()["acked"][owner].as_object().map(|o| o.keys().cloned().collect()).unwrap_or_default()
}

fn set_last_check(owner: &str, ms: i64) {
    let mut st = load_state();
    st["lastCheck"][owner] = json!(ms);
    save_state(&st);
}

fn last_check(owner: &str) -> Option<i64> { load_state()["lastCheck"][owner].as_i64() }

/// The reminders the missed dialog lists: due after the last check (at most a week back) and
/// before the stale threshold, not dealt with yet.
pub fn missed(due: &[Due], acked: &std::collections::HashSet<String>, last: Option<i64>, now_ms: i64) -> Vec<Due> {
    let Some(last) = last else { return vec![] };
    let from = last.max(now_ms - MISSED_WINDOW_MS);
    due.iter().filter(|d| d.fire_ms >= from && d.fire_ms <= now_ms - STALE_MS && !acked.contains(&d.key)).cloned().collect()
}

// ---------------- runner (UI thread) ----------------

#[derive(Default)]
struct Run {
    owner: String,
    events: Vec<Value>,
    cals: Vec<Value>,
    fetched: Option<std::time::Instant>,
    /// reminders on screen: key -> (event id, fire ms)
    shown: HashMap<String, Due>,
    /// snoozed: key -> show again at (ms)
    snoozed: HashMap<String, i64>,
    checked_missed: bool,
    gen: u64,
}

thread_local! {
    static RUN: RefCell<Run> = RefCell::new(Run::default());
    static TIMER: slint::Timer = slint::Timer::default();
    static FETCH_TIMER: slint::Timer = slint::Timer::default();
}

fn now_ms() -> i64 { chrono::Utc::now().timestamp_millis() }

/// Start (or restart, after a sign-in) the reminder runner for this login.
pub fn start(u: &AppWindow, c: Arc<Client>) {
    if !c.has(CAL) { return; }
    let owner = format!("{}|{}", c.base.trim_end_matches('/').to_lowercase(), c.username.to_lowercase());
    RUN.with(|r| { let mut r = r.borrow_mut(); let gen = r.gen + 1; *r = Run { owner, gen, ..Default::default() }; });
    u.global::<crate::CalX>().set_reminders(ModelRc::new(VecModel::from(Vec::<crate::CxReminder>::new())));
    fetch(u.as_weak(), c.clone());
    let (w, c2) = (u.as_weak(), c);
    FETCH_TIMER.with(|t| t.start(slint::TimerMode::Repeated, std::time::Duration::from_secs(REFETCH_SECS), move || {
        // signed out or switched to another login: this runner is done
        if !crate::cal_extra::client().is_some_and(|x| Arc::ptr_eq(&x, &c2)) { stop(); return; }
        fetch(w.clone(), c2.clone());
    }));
}

/// Stop reminding (no login).
pub fn stop() {
    RUN.with(|r| { let mut r = r.borrow_mut(); let gen = r.gen + 1; *r = Run { gen, ..Default::default() }; });
    TIMER.with(|t| t.stop());
    FETCH_TIMER.with(|t| t.stop());
}

/// Read the events whose reminders can come due soon (or came due since the last check).
pub fn fetch(w: slint::Weak<AppWindow>, c: Arc<Client>) {
    let (owner, gen) = RUN.with(|r| { let r = r.borrow(); (r.owner.clone(), r.gen) });
    let now = now_ms();
    let from = last_check(&owner).map(|l| l.max(now - MISSED_WINDOW_MS)).unwrap_or(now - STALE_MS) - 86_400_000;
    std::thread::spawn(move || {
        let primary = c.account_for(CAL);
        let fmt = |ms: i64| chrono::Utc.timestamp_millis_opt(ms).single().map(|t| t.format("%Y-%m-%dT%H:%M:%SZ").to_string()).unwrap_or_default();
        let res: Result<(Vec<Value>, Vec<Value>), String> = (|| {
            let props = json!(["id", "@type", "uid", "title", "start", "duration", "timeZone", "showWithoutTime", "calendarIds", "recurrenceRules",
                "excludedRecurrenceRules", "recurrenceOverrides", "alerts", "useDefaultAlerts", "status", "due", "progress", "locations", "virtualLocations"]);
            let (mut all_cals, mut all_events) = (vec![], vec![]);
            // every account with calendars: the user's own and the ones shared with them
            for (acc, _, is_primary) in calendar::calendar_accounts(&c) {
                let one: Result<(), String> = (|| {
                    let r = c.call(json!([["Calendar/get", {"accountId": acc, "ids": null, "properties": ["id", "name", "color", "defaultAlertsWithTime", "defaultAlertsWithoutTime"]}, "c"]]))?;
                    let mut cals = r[0][1]["list"].as_array().cloned().unwrap_or_default();
                    let ahead = fetch_ahead(&cals);
                    let ids = c.query_all("CalendarEvent/query", &acc, json!({"filter": {"after": fmt(from), "before": fmt(now + ahead)}}))?;
                    let mut events = c.get_all("CalendarEvent/get", &acc, &ids, Some(&props))?;
                    for k in cals.iter_mut() { let id = calendar::ns(&primary, &acc, k["id"].as_str().unwrap_or("")); k["id"] = json!(id); }
                    for e in events.iter_mut() { calendar::namespace_event(e, &primary, &acc); }
                    all_cals.extend(cals); all_events.extend(events);
                    Ok(())
                })();
                // a shared account that fails must not hide the user's own reminders
                if is_primary { one?; }
            }
            Ok((all_cals, all_events))
        })();
        let _ = w.upgrade_in_event_loop(move |u| {
            let Ok((cals, events)) = res else { return };
            if RUN.with(|r| r.borrow().gen) != gen { return; }
            RUN.with(|r| { let mut r = r.borrow_mut(); r.cals = cals; r.events = events; r.fetched = Some(std::time::Instant::now()); });
            check(&u);
        });
    });
}

/// Show what is due now, list what was missed (once per run), and wake again at the next one.
pub fn check(u: &AppWindow) {
    let p = crate::settings::Settings::load();
    let on = p.bool("calendarNotificationsEnabled", true);
    let sound = p.bool("calendarNotificationSound", true) && !p.bool("doNotDisturb", false);
    let tasks = p.bool("enableCalendarTasks", true);
    let now = now_ms();
    let (owner, events, cals, first) = RUN.with(|r| { let r = r.borrow(); (r.owner.clone(), r.events.clone(), r.cals.clone(), !r.checked_missed) });
    if owner.is_empty() || RUN.with(|r| r.borrow().fetched.is_none()) { return; }
    let acked = acked(&owner);
    if on {
        let window_from = if first { last_check(&owner).map(|l| l.max(now - MISSED_WINDOW_MS)).unwrap_or(now - STALE_MS) } else { now - STALE_MS };
        let due = due_between(&events, &cals, window_from.min(now - STALE_MS), now + 1, tasks);
        // missed while closed: listed once, then dealt with
        if first {
            let m = missed(&due, &acked, last_check(&owner), now);
            if !m.is_empty() {
                let rows: Vec<crate::CxMissed> = m.iter().map(|d| crate::CxMissed {
                    title: if d.title.is_empty() { "(no title)".into() } else { d.title.clone().into() },
                    when: format!("{}{}", Local.timestamp_millis_opt(d.start_ms.unwrap_or(d.fire_ms)).single().map(|t| t.format("%a %b %-d, ").to_string() + &calendar::fmt_time(t.naive_local(), calendar::h12())).unwrap_or_default(),
                        if d.calendar.is_empty() { String::new() } else { format!(" · {}", d.calendar) }).into(),
                }).collect();
                acknowledge(&owner, &m.iter().map(|d| (d.key.clone(), d.fire_ms)).collect::<Vec<_>>(), now);
                let x = u.global::<crate::CalX>();
                x.set_missed(ModelRc::new(VecModel::from(rows)));
                x.set_missed_open(true);
            }
        }
        let mut fresh = vec![];
        for d in due.iter().filter(|d| d.fire_ms > now - STALE_MS && d.fire_ms <= now) {
            if acked.contains(&d.key) || RUN.with(|r| r.borrow().shown.contains_key(&d.key)) { continue; }
            fresh.push(d.clone());
        }
        // snoozed ones whose time has come back
        let back: Vec<String> = RUN.with(|r| r.borrow().snoozed.iter().filter(|(_, t)| **t <= now).map(|(k, _)| k.clone()).collect());
        for k in back {
            RUN.with(|r| r.borrow_mut().snoozed.remove(&k));
            if let Some(d) = RUN.with(|r| r.borrow().shown.get(&k).cloned()) { show(u, &d, sound, now); }
        }
        if !fresh.is_empty() {
            acknowledge(&owner, &fresh.iter().map(|d| (d.key.clone(), d.fire_ms)).collect::<Vec<_>>(), now);
            for d in &fresh {
                RUN.with(|r| r.borrow_mut().shown.insert(d.key.clone(), d.clone()));
                show(u, d, sound, now);
            }
        }
    }
    RUN.with(|r| r.borrow_mut().checked_missed = true);
    set_last_check(&owner, now);
    // wake at the next reminder (at most a minute away, so settings and new events count)
    let next = due_between(&events, &cals, now + 1, now + 3_600_000, tasks).first().map(|d| d.fire_ms);
    let snooze_next = RUN.with(|r| r.borrow().snoozed.values().min().copied());
    let wake = [next, snooze_next].into_iter().flatten().min().map(|t| (t - now).clamp(500, 60_000)).unwrap_or(60_000);
    let w = u.as_weak();
    TIMER.with(|t| t.start(slint::TimerMode::SingleShot, std::time::Duration::from_millis(wake as u64), move || {
        if let Some(u) = w.upgrade() { check(&u); }
    }));
}

/// Put one reminder on screen (the stack in the window) and on the desktop.
fn show(u: &AppWindow, d: &Due, sound: bool, now: i64) {
    let x = u.global::<crate::CalX>();
    let title = if d.title.is_empty() { "(no title)".to_string() } else { d.title.clone() };
    let when = when_label(d, now);
    let color = RUN.with(|r| r.borrow().events.iter().find(|e| e["id"] == calendar::base_id(&d.event)).map(calendar::first_calendar))
        .map(|cid| calendar::calendar_color_of(&cid)).unwrap_or(slint::Color::from_rgb_u8(0xf0, 0x57, 0x7a));
    let item = crate::CxReminder { key: d.key.clone().into(), title: title.clone().into(), when: when.clone().into(), calendar: d.calendar.clone().into(), color };
    let mut list: Vec<crate::CxReminder> = x.get_reminders().iter().filter(|r| r.key != d.key.as_str()).collect();
    list.insert(0, item);
    list.truncate(4);
    x.set_reminders(ModelRc::new(VecModel::from(list)));
    // the desktop notification (not while a test drives the hidden window)
    if !crate::automation::enabled() {
        let body = if d.calendar.is_empty() { when } else { format!("{when} · {}", d.calendar) };
        std::thread::spawn(move || {
            let mut n = notify_rust::Notification::new();
            if sound { n.sound_name("Reminder"); }
            let _ = n.appname("Barbican").summary(&title).body(&body).show();
        });
    }
    // Settings > Accessibility > Visual alerts: flash the taskbar button
    if crate::a11y::A11y::load().visual_alerts {
        use slint::winit_030::{winit::window::UserAttentionType, WinitWindowAccessor};
        u.window().with_winit_window(|ww| ww.request_user_attention(Some(UserAttentionType::Informational)));
    }
}

/// A button on a reminder: "dismiss", "snooze:N", "open".
pub fn act(u: &AppWindow, key: &str, action: &str) {
    let x = u.global::<crate::CalX>();
    let list: Vec<crate::CxReminder> = x.get_reminders().iter().filter(|r| r.key != key).collect();
    x.set_reminders(ModelRc::new(VecModel::from(list)));
    if let Some(m) = action.strip_prefix("snooze:") {
        let mins: i64 = m.parse().unwrap_or(5);
        RUN.with(|r| r.borrow_mut().snoozed.insert(key.to_string(), now_ms() + mins * 60_000));
        check(u);
        return;
    }
    let d = RUN.with(|r| { let mut r = r.borrow_mut(); r.snoozed.remove(key); r.shown.remove(key) });
    if action == "open" {
        u.set_show_settings(false);
        u.set_app(1);
        u.invoke_app_changed(1);
        if let Some(d) = d {
            if let Some(day) = d.start_ms.and_then(|s| Local.timestamp_millis_opt(s).single()) {
                u.invoke_cal_nav(day.format("%Y-%m-%d").to_string().into());
            }
        }
    }
}

/// The missed-reminders dialog closed; `open` goes to the calendar.
pub fn missed_done(u: &AppWindow, open: bool) {
    u.global::<crate::CalX>().set_missed_open(false);
    if open {
        u.set_show_settings(false);
        u.set_app(1);
        u.invoke_app_changed(1);
    }
}

/// Events changed (push, a save): read them again soon.
pub fn changed(w: slint::Weak<AppWindow>, c: Arc<Client>) {
    if RUN.with(|r| r.borrow().owner.is_empty()) { return; }
    fetch(w, c);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn fetch_ahead_covers_long_default_alerts() {
        assert_eq!(fetch_ahead(&[]), 8 * 86_400_000);
        let cals = [json!({"defaultAlertsWithoutTime": {"a": {"trigger": {"offset": "-P14D"}}}})];
        assert_eq!(fetch_ahead(&cals), 15 * 86_400_000);
        // a 2-days-before alert on an event 3 days out is inside the window
        assert!(fetch_ahead(&[]) > 3 * 86_400_000);
    }

    fn ms(s: &str) -> i64 { recurrence::zone_to_utc_ms(parse_local(s).unwrap(), Some("UTC")) }

    #[test]
    fn offsets_and_rows() {
        assert_eq!(offset_ms("-PT15M"), Some(-900_000));
        assert_eq!(offset_to_unit("-PT15M"), Some((15, 1)));
        assert_eq!(offset_to_unit("-PT120M"), Some((2, 2)));
        assert_eq!(offset_to_unit("-PT1440M"), Some((1, 3)));
        assert_eq!(offset_to_unit("-P1W"), Some((1, 4)));
        assert_eq!(offset_to_unit("PT0S"), Some((0, 0)));
        assert_eq!(offset_to_unit("PT15M"), None);
        assert_eq!(offset_to_unit("-P1DT2H"), None);
        assert_eq!(row_offset(&Row { value: 2, unit: 2, action: 0 }).as_deref(), Some("-PT2H"));
        assert_eq!(row_offset(&Row { value: 0, unit: 1, action: 0 }), None);
        assert_eq!(row_label(&Row { value: 1, unit: 3, action: 1 }), "1 day before by email");
        let a = json!({"trigger": {"@type": "OffsetTrigger", "offset": "-PT10M"}, "action": "display"});
        assert_eq!(describe(&a), Some(Row { value: 10, unit: 1, action: 0 }));
        assert_eq!(describe(&json!({"trigger": {"@type": "AbsoluteTrigger", "when": "2026-01-01T00:00:00Z"}})), None);
        assert_eq!(alerts_label(Some(&json!({"a": a}))), "10 minutes before");
    }

    #[test]
    fn effective_alerts_prefer_the_event_copy() {
        let cal = json!({"defaultAlertsWithTime": {"d1": {"trigger": {"offset": "-PT30M"}}}, "defaultAlertsWithoutTime": {}});
        let e = json!({"useDefaultAlerts": true});
        assert_eq!(effective_alerts(&e, Some(&cal)).unwrap()["d1"]["trigger"]["offset"], "-PT30M");
        let e = json!({"useDefaultAlerts": true, "alerts": {"x": {"trigger": {"offset": "-PT5M"}}}});
        assert!(effective_alerts(&e, Some(&cal)).unwrap().get("x").is_some());
        let e = json!({"useDefaultAlerts": false});
        assert!(effective_alerts(&e, Some(&cal)).is_none());
        assert!(effective_alerts(&json!({"useDefaultAlerts": true, "showWithoutTime": true}), Some(&cal)).is_none());
    }

    #[test]
    fn alerts_patch_keeps_unchanged_and_leaves_defaults() {
        let ev = json!({"alerts": {"a1": {"@type": "Alert", "trigger": {"@type": "OffsetTrigger", "offset": "-PT10M"}, "action": "display"}}});
        let (rows, kept) = rows_of(Some(&ev["alerts"]));
        // unchanged: nothing to send
        assert!(build_alerts_patch(Some(&ev), &rows, &kept, &[]).is_empty());
        // one more row: the old alert keeps its id, the new one gets its own
        let mut more = rows.clone();
        more.push(EdRow { row: Row { value: 1, unit: 3, action: 1 }, source: None });
        let p = build_alerts_patch(Some(&ev), &more, &kept, &[]);
        let a = p["alerts"].as_object().unwrap();
        assert!(a.contains_key("a1") && a.contains_key("alert1"));
        assert_eq!(a["alert1"]["trigger"]["offset"], "-P1D");
        assert_eq!(a["alert1"]["action"], "email");
        // all rows removed: alerts cleared
        assert_eq!(build_alerts_patch(Some(&ev), &[], &Map::new(), &[])["alerts"], Value::Null);
        // a new event says it leaves the defaults, and sends no empty map
        let p = build_alerts_patch(None, &[], &Map::new(), &[]);
        assert_eq!(p["useDefaultAlerts"], false);
        assert!(p.get("alerts").is_none());
        // an event on defaults gets ids away from the default ids
        let ev = json!({"useDefaultAlerts": true, "alerts": {"alert1": {"trigger": {"offset": "-PT10M"}}}});
        let rows = vec![EdRow { row: Row { value: 5, unit: 1, action: 0 }, source: None }];
        let p = build_alerts_patch(Some(&ev), &rows, &Map::new(), &["alert2".to_string()]);
        assert!(p["alerts"].as_object().unwrap().contains_key("alert3"));
        assert_eq!(p["useDefaultAlerts"], false);
    }

    #[test]
    fn due_alerts_of_series_and_tasks() {
        let cals = vec![json!({"id": "c", "name": "Work", "defaultAlertsWithTime": {"d": {"@type": "Alert", "trigger": {"@type": "OffsetTrigger", "offset": "-PT15M"}, "action": "display"}}})];
        let ev = json!({"id": "e", "title": "Stand-up", "start": "2026-11-16T10:00:00", "timeZone": "UTC", "duration": "PT15M", "calendarIds": {"c": true},
            "useDefaultAlerts": true, "recurrenceRules": [{"frequency": "daily"}]});
        let d = due_between(&[ev.clone()], &cals, ms("2026-11-17T00:00:00"), ms("2026-11-18T00:00:00"), true);
        assert_eq!(d.len(), 1);
        assert_eq!(d[0].fire_ms, ms("2026-11-17T09:45:00"));
        assert_eq!(d[0].event, "e|2026-11-17T10:00:00");
        assert_eq!(d[0].calendar, "Work");
        assert_eq!(when_label(&d[0], ms("2026-11-17T09:45:00")), "In 15 minutes");
        // cancelled and email-only events don't fire
        let mut c2 = ev.clone();
        c2["status"] = json!("cancelled");
        assert!(due_between(&[c2], &cals, ms("2026-11-17T00:00:00"), ms("2026-11-18T00:00:00"), true).is_empty());
        let task = json!({"id": "t", "@type": "Task", "title": "Pay", "due": "2026-11-20T12:00:00", "timeZone": "UTC", "calendarIds": {"c": true},
            "alerts": {"a": {"@type": "Alert", "trigger": {"@type": "OffsetTrigger", "offset": "-PT1H", "relativeTo": "end"}, "action": "display"}}});
        let d = due_between(&[task.clone()], &cals, ms("2026-11-20T00:00:00"), ms("2026-11-21T00:00:00"), true);
        assert_eq!(d.len(), 1);
        assert_eq!(d[0].fire_ms, ms("2026-11-20T11:00:00"));
        assert!(d[0].task);
        assert!(due_between(&[task], &cals, ms("2026-11-20T00:00:00"), ms("2026-11-21T00:00:00"), false).is_empty());
    }

    #[test]
    fn missed_window() {
        let mk = |k: &str, f: i64| Due { key: k.into(), event: "e".into(), alert: "a".into(), fire_ms: f, start_ms: None, title: "t".into(), calendar: String::new(), task: false };
        let now = 10 * 86_400_000;
        let due = vec![mk("old", now - 8 * 86_400_000), mk("a", now - 3_600_000), mk("b", now - 60_000), mk("acked", now - 7_200_000)];
        let acked: std::collections::HashSet<String> = ["acked".to_string()].into();
        let m = missed(&due, &acked, Some(now - 9 * 86_400_000), now);
        assert_eq!(m.iter().map(|d| d.key.as_str()).collect::<Vec<_>>(), ["a"]);
        assert!(missed(&due, &acked, None, now).is_empty());
    }
}
