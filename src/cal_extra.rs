//! Calendar tools around the grid (Bulwark calendar-agenda-view.tsx, task-list-view.tsx,
//! task-modal.tsx, tasks-sidebar-filters.tsx, ical-import-modal.tsx, ical-subscription-modal.tsx
//! with lib/calendar-subscription-sync.ts, lib/birthday-calendar.ts): the agenda, the tasks
//! view and editor, .ics import, webcal subscriptions and the contact birthday calendar; and
//! the wiring of every calendar_ext.slint callback.
use crate::cal_editor::{self, Person};
use crate::calendar::{self, Occ, BIRTHDAY_CAL, CAL};
use crate::jmap::Client;
use crate::recurrence::{self, parse_local, Rule};
use crate::{AppWindow, CxAgendaRow, CxSub, CxTask};
use chrono::{Datelike, Duration, Local, NaiveDate, NaiveTime, Weekday};
use serde_json::{json, Map, Value};
use slint::{ComponentHandle, Model, ModelRc, VecModel};
use std::cell::RefCell;
use std::sync::Arc;

thread_local! {
    static SHARED: RefCell<Option<crate::Shared>> = const { RefCell::new(None) };
    /// contact cards (name, emails, birthdays, group members)
    static PEOPLE: RefCell<Vec<Value>> = const { RefCell::new(Vec::new()) };
    static IMPORT: RefCell<Option<(String, Vec<Value>)>> = const { RefCell::new(None) };
    static TASK_LISTS: RefCell<Vec<String>> = const { RefCell::new(Vec::new()) };
    static IMPORT_TARGETS: RefCell<Vec<String>> = const { RefCell::new(Vec::new()) };
}

/// The signed-in client (UI thread).
pub fn client() -> Option<Arc<Client>> {
    SHARED.with(|s| s.borrow().as_ref().and_then(|st| st.lock().unwrap().clone()))
}

// ---------------- people (contacts) ----------------

pub fn fetch_people(c: &Client) -> Result<Vec<Value>, String> {
    if !c.has("urn:ietf:params:jmap:contacts") { return Ok(vec![]); }
    let acc = c.account_for("urn:ietf:params:jmap:contacts");
    let ids = c.query_all("ContactCard/query", &acc, json!({}))?;
    c.get_all("ContactCard/get", &acc, &ids, Some(&json!(["id", "uid", "kind", "name", "emails", "anniversaries", "members"])))
}

pub fn set_people(p: Vec<Value>) { PEOPLE.with(|x| *x.borrow_mut() = p); }

fn card_name(c: &Value) -> String {
    if let Some(f) = c["name"]["full"].as_str().filter(|s| !s.is_empty()) { return f.to_string(); }
    let comps: Vec<&str> = c["name"]["components"].as_array().map(|a| a.iter().filter_map(|x| x["value"].as_str()).collect()).unwrap_or_default();
    comps.join(" ")
}

fn card_emails(c: &Value) -> Vec<String> {
    c["emails"].as_object().map(|o| o.values().filter_map(|e| e["address"].as_str().map(String::from)).collect()).unwrap_or_default()
}

/// Contacts and groups whose name or address contains `q`, without the addresses in `have`.
pub fn people_matching(q: &str, have: &[String], max: usize) -> Vec<(Person, Vec<Person>)> {
    PEOPLE.with(|p| {
        let p = p.borrow();
        let mut out: Vec<(Person, Vec<Person>)> = vec![];
        for c in p.iter() {
            let name = card_name(c);
            if c["kind"] == "group" {
                if !name.to_lowercase().contains(q) { continue; }
                let uids: Vec<&str> = c["members"].as_object().map(|m| m.keys().map(|k| k.as_str()).collect()).unwrap_or_default();
                let members: Vec<Person> = p.iter().filter(|m| m["uid"].as_str().is_some_and(|u| uids.contains(&u)))
                    .filter_map(|m| card_emails(m).first().map(|e| Person { name: card_name(m), email: e.clone() })).collect();
                if !members.is_empty() { out.push((Person { name, email: format!("{} members", members.len()), }, members)); }
                continue;
            }
            for e in card_emails(c) {
                if have.contains(&e.to_lowercase()) { continue; }
                if name.to_lowercase().contains(q) || e.to_lowercase().contains(q) { out.push((Person { name: name.clone(), email: e }, vec![])); }
            }
            if out.len() >= max * 3 { break; }
        }
        // names that start with the query first
        out.sort_by_key(|(p, _)| (!p.name.to_lowercase().starts_with(q) && !p.email.to_lowercase().starts_with(q), p.name.to_lowercase()));
        out.truncate(max);
        out
    })
}

// ---------------- birthday calendar ----------------

/// (month, day, year) of a JSContact anniversary date: "1990-05-15", "--05-15", a PartialDate or
/// a Timestamp (Bulwark parseBirthdayDate).
pub fn birthday_parts(d: &Value) -> Option<(u32, u32, Option<i32>)> {
    if let Some(s) = d.as_str() {
        if let Some(r) = s.strip_prefix("--") {
            let (m, dd) = r.split_once('-')?;
            return Some((m.parse().ok()?, dd.get(..2)?.parse().ok()?, None));
        }
        let t = NaiveDate::parse_from_str(s.get(..10)?, "%Y-%m-%d").ok()?;
        return Some((t.month(), t.day(), Some(t.year())));
    }
    if d["@type"] == "Timestamp" { return birthday_parts(&d["utc"]); }
    Some((d["month"].as_u64()? as u32, d["day"].as_u64()? as u32, d["year"].as_i64().map(|y| y as i32)))
}

/// The birthdays of the contacts in [from, to), one all-day item per year (Feb 29 on Feb 28 in
/// other years), with the age when the year is known (Bulwark generateBirthdayEvents).
pub fn birthdays_in(cards: &[Value], from: NaiveDate, to: NaiveDate) -> Vec<Occ> {
    let mut out = vec![];
    for c in cards {
        let Some(ann) = c["anniversaries"].as_object() else { continue };
        let name = card_name(c);
        if name.is_empty() { continue; }
        for (key, a) in ann {
            if a["kind"] != "birth" { continue; }
            let Some((m, d, y)) = birthday_parts(&a["date"]) else { continue };
            for year in from.year()..=to.year() {
                let date = NaiveDate::from_ymd_opt(year, m, d).or_else(|| if m == 2 && d == 29 { NaiveDate::from_ymd_opt(year, 2, 28) } else { None });
                let Some(date) = date else { continue };
                if date < from || date >= to { continue; }
                let age = y.map(|y| year - y).filter(|a| *a > 0).map(|a| format!(" ({a})")).unwrap_or_default();
                let start = date.and_hms_opt(0, 0, 0).unwrap();
                out.push(Occ {
                    id: format!("bday:{}:{key}:{year}", c["id"].as_str().unwrap_or("")), base: String::new(), rid: None,
                    title: format!("\u{1F382} {name}{age}"), start, end: start + Duration::days(1), all_day: true,
                    calendar: BIRTHDAY_CAL.into(), location: String::new(), kind: 2, done: false,
                });
            }
        }
    }
    out
}

pub fn birthday_occurrences(from: NaiveDate, to: NaiveDate) -> Vec<Occ> { PEOPLE.with(|p| birthdays_in(&p.borrow(), from, to)) }

/// One birthday by its key ("cardId:anniversaryKey:year").
pub fn birthday(key: &str) -> Option<Occ> {
    let year: i32 = key.rsplit(':').next()?.parse().ok()?;
    let (from, to) = (NaiveDate::from_ymd_opt(year, 1, 1)?, NaiveDate::from_ymd_opt(year + 1, 1, 1)?);
    birthday_occurrences(from, to).into_iter().find(|o| o.id == format!("bday:{key}"))
}

// ---------------- agenda ----------------

/// How a timed event reads on one day of the list (Bulwark agendaDayPart): its range on the day
/// it starts, "until 07:30" on the day an overnight one ends, "All day" on a day it covers.
pub fn agenda_time(o: &Occ, day: NaiveDate, h12: bool) -> String {
    if o.all_day { return if o.kind == 1 { "Due".into() } else { "All day".into() }; }
    let ds = day.and_hms_opt(0, 0, 0).unwrap();
    if o.start >= ds { return format!("{} – {}", calendar::fmt_time(o.start, h12), calendar::fmt_time(o.end, h12)); }
    if o.end >= ds + Duration::days(1) { return "All day".into(); }
    format!("until {}", calendar::fmt_time(o.end, h12))
}

pub fn render_agenda(u: &AppWindow, visible: &[Occ], from: NaiveDate, to: NaiveDate, cals: &[Value], h12: bool) {
    let today = Local::now().date_naive();
    let mut rows: Vec<CxAgendaRow> = vec![];
    let mut d = from;
    while d < to {
        let ds = d.and_hms_opt(0, 0, 0).unwrap();
        let mut day: Vec<&Occ> = visible.iter().filter(|o| {
            let last = if o.end > o.start { o.end - Duration::seconds(1) } else { o.start };
            o.start < ds + Duration::days(1) && last >= ds
        }).collect();
        // all-day first, then by when the item shows on this day
        day.sort_by_key(|o| (if o.all_day || o.start < ds { ds } else { o.start }, !o.all_day, o.title.to_lowercase()));
        if !day.is_empty() || d == today {
            let header = if d == today { "Today".to_string() } else if d == today + Duration::days(1) { "Tomorrow".to_string() } else { d.format("%A, %B %-d").to_string() };
            let mut first = true;
            if day.is_empty() {
                rows.push(CxAgendaRow { header: header.clone().into(), today: d == today, empty: true, ..Default::default() });
            }
            for o in day {
                let color = if o.calendar == BIRTHDAY_CAL { calendar::calendar_color_of(BIRTHDAY_CAL) } else {
                    cals.iter().position(|c| c["id"] == o.calendar.as_str()).map(|_| calendar::calendar_color_of(&o.calendar)).unwrap_or(slint::Color::from_rgb_u8(0x3b, 0x82, 0xf6))
                };
                rows.push(CxAgendaRow {
                    header: if first { header.clone().into() } else { "".into() }, today: d == today,
                    id: o.id.clone().into(), title: if o.kind == 1 { agenda_task_title(&o.title) } else { o.title.as_str() }.into(), time: agenda_time(o, d, h12).into(), location: o.location.clone().into(),
                    color, kind: if o.kind == 1 { 1 } else if o.kind == 2 || is_subscription(&o.calendar) { 2 } else { 0 }, done: o.done, empty: false,
                });
                first = false;
            }
        }
        d += Duration::days(1);
    }
    u.global::<crate::CalX>().set_agenda(ModelRc::new(VecModel::from(rows)));
}

// ---------------- tasks view ----------------

pub fn priority_level(p: i64) -> i32 { match p { 1..=4 => 1, 5 => 2, 6..=9 => 3, _ => 0 } }

/// Done and cancelled tasks are never overdue; one due today isn't yet (Bulwark isTaskOverdue).
pub fn is_overdue(t: &Value, today: NaiveDate) -> bool {
    if t["progress"] == "completed" || t["progress"] == "cancelled" { return false; }
    t["due"].as_str().and_then(parse_local).is_some_and(|d| d.date() < today)
}

/// "Today", "Tomorrow", "Overdue · Nov 3", "Nov 20" (+ time) with a tone (0 dim, 1 today, 2 overdue).
pub fn due_label(t: &Value, today: NaiveDate, h12: bool) -> (String, i32) {
    let Some(due) = t["due"].as_str().and_then(parse_local) else { return (String::new(), 0) };
    let done = t["progress"] == "completed" || t["progress"] == "cancelled";
    let time = if t["showWithoutTime"] == true || due.time() == NaiveTime::MIN { String::new() } else { format!(" {}", calendar::fmt_time(due, h12)) };
    let date = if due.year() == today.year() { due.format("%b %-d").to_string() } else { due.format("%b %-d, %Y").to_string() };
    if done { return (format!("{date}{time}"), 0); }
    if due.date() == today { return (format!("Today{time}"), 1); }
    if due.date() == today + Duration::days(1) { return (format!("Tomorrow{time}"), 0); }
    if due.date() < today { return (format!("Overdue · {date}"), 2); }
    (format!("{date}{time}"), 0)
}

/// Which tasks a filter lists: 0 all, 1 pending, 2 overdue, 3 completed.
pub fn task_matches(t: &Value, filter: i32, today: NaiveDate) -> bool {
    let finished = t["progress"] == "completed" || t["progress"] == "cancelled";
    match filter { 1 => !finished, 2 => is_overdue(t, today), 3 => t["progress"] == "completed", _ => true }
}

thread_local! {
    /// The day picked in the mini month while the Tasks view is showing: only tasks due that day.
    static TASK_DAY: std::cell::Cell<Option<NaiveDate>> = const { std::cell::Cell::new(None) };
}

pub fn task_day() -> Option<NaiveDate> { TASK_DAY.with(|d| d.get()) }

/// Picking the same day again (or "clear") lifts the filter.
pub fn toggle_task_day(how: &str) {
    let picked = NaiveDate::parse_from_str(how, "%Y-%m-%d").ok();
    TASK_DAY.with(|d| d.set(if picked.is_none() || d.get() == picked { None } else { picked }));
}

/// Whether a task is due on `day` (the list a mini-month pick shows).
pub fn task_due_on(t: &Value, day: NaiveDate) -> bool {
    t["due"].as_str().and_then(parse_local).is_some_and(|d| d.date() == day)
}

/// The agenda draws its own marker for a task, so the prefix the grid titles carry goes.
pub fn agenda_task_title(title: &str) -> &str {
    title.strip_prefix(['\u{2713}', '\u{25CB}']).map(str::trim_start).unwrap_or(title)
}

pub fn render_tasks_view(u: &AppWindow) {
    let x = u.global::<crate::CalX>();
    let filter = x.get_task_filter();
    let day = task_day();
    x.set_task_day_label(day.map(|d| d.format("Due %a, %b %-d").to_string()).unwrap_or_default().into());
    let today = Local::now().date_naive();
    let hidden = calendar::hidden();
    let h12 = calendar::h12();
    let tasks: Vec<Value> = calendar::with_raw(|r| r.iter().filter(|e| calendar::is_task(e) && !hidden.contains(&calendar::first_calendar(e))).cloned().collect());
    let counts: Vec<i32> = (0..4).map(|f| tasks.iter().filter(|t| task_matches(t, f, today)).count() as i32).collect();
    let mut list: Vec<&Value> = tasks.iter().filter(|t| task_matches(t, filter, today) && day.is_none_or(|d| task_due_on(t, d))).collect();
    list.sort_by_key(|t| (t["progress"] == "completed" || t["progress"] == "cancelled", t["due"].as_str().unwrap_or("9999").to_string(),
        match t["priority"].as_i64().unwrap_or(0) { 0 => 10, p => p }, t["title"].as_str().unwrap_or("").to_lowercase()));
    let cals = calendar::calendars();
    let rows: Vec<CxTask> = list.iter().map(|t| {
        let (due, tone) = due_label(t, today, h12);
        let cal = calendar::first_calendar(t);
        CxTask {
            id: t["id"].as_str().unwrap_or("").into(),
            title: t["title"].as_str().filter(|x| !x.is_empty()).unwrap_or("(untitled task)").into(),
            notes: t["description"].as_str().unwrap_or("").lines().next().unwrap_or("").into(),
            due: due.into(), due_tone: tone,
            done: t["progress"] == "completed",
            priority: priority_level(t["priority"].as_i64().unwrap_or(0)),
            list: cals.iter().find(|c| c["id"] == cal.as_str()).and_then(|c| c["name"].as_str()).unwrap_or("").into(),
            color: calendar::calendar_color_of(&cal),
        }
    }).collect();
    x.set_tasks(ModelRc::new(VecModel::from(rows)));
    x.set_task_counts(ModelRc::new(VecModel::from(counts)));
}

/// A date at the end of a quick task's title (Bulwark parseTaskDue): "today", "tomorrow", a
/// weekday ("fri", "friday": the next one), "next week", "in 3 days" or 2026-11-20. Returns the
/// title without it and the date.
pub fn parse_task_due(text: &str, today: NaiveDate) -> Option<(String, NaiveDate)> {
    let t = text.trim();
    let lower = t.to_lowercase();
    let words: Vec<&str> = lower.split_whitespace().collect();
    if words.len() < 2 { return None; }
    let keep = |n: usize| -> String {
        let all: Vec<&str> = t.split_whitespace().collect();
        let mut s = all[..all.len() - n].join(" ");
        for w in [" by", " due", " on"] { if s.to_lowercase().ends_with(w) && s.len() > w.len() { s.truncate(s.len() - w.len()); } }
        s.trim().to_string()
    };
    let last = *words.last()?;
    if words.len() >= 3 && words[words.len() - 3] == "in" && (last == "days" || last == "day") {
        let n: i64 = words[words.len() - 2].parse().ok()?;
        return Some((keep(3), today + Duration::days(n)));
    }
    if words.len() >= 3 && words[words.len() - 2] == "next" && last == "week" { return Some((keep(2), today + Duration::days(7))); }
    if let Ok(d) = NaiveDate::parse_from_str(last, "%Y-%m-%d") { return Some((keep(1), d)); }
    match last {
        "today" | "tonight" => return Some((keep(1), today)),
        "tomorrow" => return Some((keep(1), today + Duration::days(1))),
        _ => {}
    }
    let days = [("mon", Weekday::Mon), ("tue", Weekday::Tue), ("wed", Weekday::Wed), ("thu", Weekday::Thu), ("fri", Weekday::Fri), ("sat", Weekday::Sat), ("sun", Weekday::Sun)];
    let full = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];
    let wd = days.iter().find(|(s, _)| last == *s || last == format!("{s}.") || full.iter().any(|f| f.starts_with(*s) && *f == last)).map(|(_, w)| *w)?;
    let ahead = (wd.num_days_from_monday() as i64 - today.weekday().num_days_from_monday() as i64).rem_euclid(7);
    Some((keep(1), today + Duration::days(if ahead == 0 { 7 } else { ahead })))
}

pub fn task_quick_changed(u: &AppWindow, t: &str) {
    let label = parse_task_due(t, Local::now().date_naive()).map(|(_, d)| format!("Due {}", d.format("%a, %b %-d"))).unwrap_or_default();
    u.global::<crate::CalX>().set_task_quick_due(label.into());
}

pub fn task_quick_add(u: &AppWindow, c: Arc<Client>, t: &str) {
    let (title, due) = match parse_task_due(t, Local::now().date_naive()) { Some((t, d)) => (t, d.format("%Y-%m-%d").to_string()), None => (t.trim().to_string(), String::new()) };
    if title.is_empty() { return; }
    let x = u.global::<crate::CalX>();
    x.set_task_quick("".into());
    x.set_task_quick_due("".into());
    calendar::add_task(u, c, &title, &due);
}

const TASK_ALERT_MIN: [i64; 7] = [-1, 0, 5, 15, 30, 60, 1440];

/// Open the task editor (`id` empty: a new task).
pub fn open_task(u: &AppWindow, id: &str) {
    let x = u.global::<crate::CalX>();
    let cals = calendar::calendars();
    let lists: Vec<&Value> = cals.iter().filter(|c| c["_shared"] != true && !is_subscription(c["id"].as_str().unwrap_or(""))).collect();
    TASK_LISTS.with(|l| *l.borrow_mut() = lists.iter().map(|c| c["id"].as_str().unwrap_or("").to_string()).collect());
    x.set_te_lists(ModelRc::new(VecModel::from(lists.iter().map(|c| slint::SharedString::from(c["name"].as_str().unwrap_or("Calendar"))).collect::<Vec<_>>())));
    let t = if id.is_empty() { None } else { calendar::raw_event(id) };
    let def = calendar::default_task_list();
    let list_of = |cid: &str| lists.iter().position(|c| c["id"] == cid).unwrap_or(0) as i32;
    match t {
        Some(t) => {
            let due = t["due"].as_str().and_then(parse_local);
            x.set_te_id(id.into());
            x.set_te_title(t["title"].as_str().unwrap_or("").into());
            x.set_te_notes(t["description"].as_str().unwrap_or("").into());
            x.set_te_due_date(due.map(|d| d.format("%Y-%m-%d").to_string()).unwrap_or_default().into());
            let timed = due.is_some() && t["showWithoutTime"] != true && due.is_some_and(|d| d.time() != NaiveTime::MIN || t["showWithoutTime"] == false);
            x.set_te_has_time(timed);
            x.set_te_due_time(due.filter(|_| timed).map(|d| d.format("%H:%M").to_string()).unwrap_or_else(|| "09:00".into()).into());
            x.set_te_priority(priority_level(t["priority"].as_i64().unwrap_or(0)));
            x.set_te_progress(match t["progress"].as_str() { Some("in-process") => 1, Some("completed") => 2, Some("cancelled") => 3, _ => 0 });
            x.set_te_list(list_of(&calendar::first_calendar(&t)));
            let rule = t["recurrenceRules"][0]["frequency"].as_str().unwrap_or("");
            x.set_te_repeat(match rule { "daily" => 1, "weekly" => 2, "monthly" => 3, "yearly" => 4, _ => 0 });
            let alert = t["alerts"].as_object().and_then(|m| m.values().next()).and_then(|a| a["trigger"]["offset"].as_str()).and_then(crate::reminders::offset_ms);
            x.set_te_alert(alert.and_then(|ms| TASK_ALERT_MIN.iter().position(|m| *m >= 0 && *m * 60_000 == -ms)).unwrap_or(0) as i32);
        }
        None => {
            x.set_te_id("".into());
            x.set_te_title("".into());
            x.set_te_notes("".into());
            x.set_te_due_date("".into());
            x.set_te_has_time(false);
            x.set_te_due_time("09:00".into());
            x.set_te_priority(0);
            x.set_te_progress(0);
            x.set_te_list(def.as_deref().map(list_of).unwrap_or(0));
            x.set_te_repeat(0);
            x.set_te_alert(0);
        }
    }
    x.set_te_status("".into());
    x.set_te_open(true);
}

/// The task the editor holds, as JSCalendar Task fields (null clears on an edit).
pub fn task_fields(title: &str, notes: &str, due: &str, time: Option<&str>, priority: i32, progress: i32, repeat: i32, alert: i32, tz: &str) -> Result<Map<String, Value>, String> {
    let mut m = Map::new();
    m.insert("title".into(), json!(title.trim()));
    m.insert("description".into(), json!(notes.trim()));
    let due = due.trim();
    if due.is_empty() {
        m.insert("due".into(), Value::Null);
    } else {
        let d = NaiveDate::parse_from_str(due, "%Y-%m-%d").map_err(|_| "Use YYYY-MM-DD for the due date.")?;
        match time {
            Some(t) => {
                let t = NaiveTime::parse_from_str(t.trim(), "%H:%M").map_err(|_| "Use HH:MM for the time.")?;
                m.insert("due".into(), json!(recurrence::fmt_local(d.and_time(t))));
                m.insert("timeZone".into(), json!(tz));
                m.insert("showWithoutTime".into(), json!(false));
            }
            None => {
                m.insert("due".into(), json!(recurrence::fmt_local(d.and_hms_opt(0, 0, 0).unwrap())));
                m.insert("timeZone".into(), Value::Null);
                m.insert("showWithoutTime".into(), json!(true));
            }
        }
    }
    m.insert("priority".into(), json!([0, 1, 5, 9][priority.clamp(0, 3) as usize]));
    m.insert("progress".into(), json!(["needs-action", "in-process", "completed", "cancelled"][progress.clamp(0, 3) as usize]));
    m.insert("recurrenceRules".into(), match repeat { 1..=4 => json!([Rule::simple(["daily", "weekly", "monthly", "yearly"][repeat as usize - 1]).to_json()]), _ => Value::Null });
    let mins = TASK_ALERT_MIN[alert.clamp(0, 6) as usize];
    m.insert("alerts".into(), if mins < 0 || due.is_empty() { Value::Null } else {
        json!({"alert1": {"@type": "Alert", "trigger": {"@type": "OffsetTrigger", "offset": if mins == 0 { "PT0S".to_string() } else { format!("-PT{mins}M") }, "relativeTo": "end"}, "action": "display"}})
    });
    Ok(m)
}

pub fn task_save(u: &AppWindow, c: Arc<Client>) {
    let x = u.global::<crate::CalX>();
    if x.get_te_title().trim().is_empty() { x.set_te_status("Give the task a title.".into()); return; }
    let time = x.get_te_has_time().then(|| x.get_te_due_time().to_string());
    let fields = match task_fields(&x.get_te_title(), &x.get_te_notes(), &x.get_te_due_date(), time.as_deref(), x.get_te_priority(), x.get_te_progress(), x.get_te_repeat(), x.get_te_alert(), &crate::agent::local_tz()) {
        Ok(f) => f, Err(e) => { x.set_te_status(e.into()); return }
    };
    let Some(list) = TASK_LISTS.with(|l| l.borrow().get(x.get_te_list().max(0) as usize).cloned()) else { x.set_te_status("Choose a list.".into()); return };
    let id = x.get_te_id().to_string();
    x.set_te_open(false);
    if id.is_empty() {
        let mut t = Value::Object(fields);
        t["@type"] = json!("Task");
        t["uid"] = json!(calendar::new_uid());
        if let Some(o) = t.as_object_mut() { o.retain(|_, v| !v.is_null()); }
        calendar::create_in(u, c.clone(), &list, t, "Task not added");
    } else {
        let mut f = fields;
        let (_, raw) = calendar::split_ns(&list);
        f.insert("calendarIds".into(), json!({ raw: true }));
        calendar::set_call(u, c.clone(), "CalendarEvent/set", json!({"update": {id: Value::Object(f)}}), "Task not saved");
    }
    crate::reminders::changed(u.as_weak(), c);
}

pub fn task_delete(u: &AppWindow, c: Arc<Client>) {
    let x = u.global::<crate::CalX>();
    let id = x.get_te_id().to_string();
    x.set_te_open(false);
    if !id.is_empty() { calendar::delete_task(u, c, &id); }
}

// ---------------- import (.ics) ----------------

/// The fields an imported or subscribed event keeps (Bulwark importEventsUnhooked): no ids, the
/// target calendar, a zero duration where the file had none, participants cleaned up.
pub fn sanitize_import(src: &Value, cal: &str) -> Value { import_event(src, cal, true) }

/// Whether an event carries its own alerts (VALARM in the file): in the event or in an override
/// of one occurrence.
pub fn has_alerts(e: &Value) -> bool {
    e["alerts"].as_object().is_some_and(|a| !a.is_empty())
        || e["recurrenceOverrides"].as_object().is_some_and(|o| o.values().any(|p| p.as_object().is_some_and(|p| p.iter().any(|(k, v)| is_alert_key(k) && !is_removal(v)))))
}

/// A null (or empty) alerts value in an override takes reminders away: not a reminder of its own.
fn is_removal(v: &Value) -> bool { v.is_null() || v.as_object().is_some_and(|o| o.is_empty()) }

fn is_alert_key(k: &str) -> bool { k == "alerts" || k.starts_with("alerts/") }

/// The same event without its alerts, nor those of its overrides, and no calendar defaults in
/// their place.
fn drop_alerts(d: &mut Map<String, Value>) {
    d.remove("alerts");
    if let Some(o) = d.get_mut("recurrenceOverrides").and_then(Value::as_object_mut) {
        for p in o.values_mut() { if let Some(p) = p.as_object_mut() { p.retain(|k, _| !is_alert_key(k)); } }
    }
    d.insert("useDefaultAlerts".into(), json!(false));
}

/// `sanitize_import`, keeping the file's alerts or not (the choice of the import dialog; Bulwark
/// `importEventsUnhooked` always keeps them: its dialog has no such choice and keeping is the default here).
pub fn import_event(src: &Value, cal: &str, keep_alerts: bool) -> Value {
    const KEEP: [&str; 24] = ["@type", "uid", "title", "description", "descriptionContentType", "start", "timeZone", "showWithoutTime", "status", "freeBusyStatus",
        "privacy", "color", "keywords", "categories", "locale", "locations", "virtualLocations", "links", "recurrenceRules", "recurrenceOverrides",
        "excludedRecurrenceRules", "alerts", "due", "progress"];
    let mut d = Map::new();
    for k in KEEP { if let Some(v) = src.get(k).filter(|v| !v.is_null()) { d.insert(k.into(), v.clone()); } }
    let all_day = src["showWithoutTime"] == true;
    let dur = src["duration"].as_str().map(String::from);
    if src["@type"] != "Task" {
        d.insert("duration".into(), json!(match (all_day, dur) {
            (true, Some(x)) if recurrence::duration_secs(&x).unwrap_or(0) >= 86400 => x,
            (true, _) => "P1D".into(),
            (false, Some(x)) => x,
            (false, None) => "PT0S".into(),
        }));
    }
    if all_day { d.remove("timeZone"); }
    if let Some(o) = src["organizerCalendarAddress"].as_str().or(src["replyTo"]["imip"].as_str()) { d.insert("organizerCalendarAddress".into(), json!(o)); }
    if let Some(ps) = src["participants"].as_object() {
        const P: [&str; 14] = ["name", "email", "description", "kind", "roles", "participationStatus", "participationComment", "expectReply",
            "scheduleAgent", "scheduleForceSend", "scheduleId", "delegatedTo", "delegatedFrom", "memberOf"];
        let mut out = Map::new();
        for (k, p) in ps {
            let mut q = Map::new();
            q.insert("@type".into(), json!("Participant"));
            for f in P { if let Some(v) = p.get(f).filter(|v| !v.is_null()) { q.insert(f.into(), v.clone()); } }
            if let Some(a) = p["calendarAddress"].as_str().or(p["sendTo"]["imip"].as_str()) { q.insert("calendarAddress".into(), json!(a)); }
            out.insert(k.clone(), Value::Object(q));
        }
        d.insert("participants".into(), Value::Object(out));
    }
    if !d.contains_key("@type") { d.insert("@type".into(), json!("Event")); }
    if !keep_alerts { drop_alerts(&mut d); }
    d.insert("calendarIds".into(), json!({ cal: true }));
    Value::Object(d)
}

/// Upload an iCalendar file and let the server read it (CalendarEvent/parse).
pub fn parse_ics(c: &Client, data: Vec<u8>) -> Result<Vec<Value>, String> {
    let acc = c.account_for(CAL);
    let (blob, _) = c.upload_to(&acc, data, "text/calendar")?;
    let r = c.call(json!([["CalendarEvent/parse", {"accountId": acc, "blobIds": [blob]}, "p"]]))?;
    if r[0][1]["notParsable"].as_array().is_some_and(|a| !a.is_empty()) { return Err("This file isn't a calendar the server can read.".into()); }
    let list = r[0][1]["parsed"][&blob].clone();
    let list = if list.is_array() { list } else { r[0][1]["parsed"].as_object().and_then(|o| o.values().next().cloned()).unwrap_or(json!([])) };
    // one entry per event; a Group holds several
    let mut out = vec![];
    for e in list.as_array().cloned().unwrap_or_default() {
        if e["@type"] == "Group" { out.extend(e["entries"].as_array().cloned().unwrap_or_default()); } else { out.push(e); }
    }
    Ok(out.into_iter().filter(|e| e["@type"] == "Event" || e["@type"] == "Task" || e["start"].is_string()).collect())
}

/// The uids of every event the account holds, with their ids and calendars.
fn existing_uids(c: &Client, acc: &str) -> Result<Vec<(String, String, Value)>, String> {
    let ids = c.query_all("CalendarEvent/query", acc, json!({}))?;
    let got = c.get_all("CalendarEvent/get", acc, &ids, Some(&json!(["id", "uid", "calendarIds"])))?;
    Ok(got.into_iter().filter_map(|e| Some((e["uid"].as_str()?.to_string(), e["id"].as_str()?.to_string(), e["calendarIds"].clone()))).collect())
}

/// Create events in batches of 50; returns (created, first refusal).
fn create_batch(c: &Client, acc: &str, events: Vec<Value>) -> (usize, Option<String>) {
    let mut made = 0;
    let mut err = None;
    for chunk in events.chunks(50.min(c.max_set())) {
        let create: Map<String, Value> = chunk.iter().enumerate().map(|(i, e)| (format!("i{i}"), e.clone())).collect();
        match c.call(json!([["CalendarEvent/set", {"accountId": acc, "sendSchedulingMessages": false, "create": create}, "s"]])) {
            Ok(r) => {
                made += r[0][1]["created"].as_object().map(|o| o.len()).unwrap_or(0);
                if let Some(e) = r[0][1]["notCreated"].as_object().and_then(|o| o.values().next()) { err.get_or_insert_with(|| crate::jmap::set_error(e)); }
            }
            Err(e) => { err.get_or_insert(e); }
        }
    }
    (made, err)
}

pub fn import_begin(u: &AppWindow) {
    let x = u.global::<crate::CalX>();
    let cals = calendar::calendars();
    let targets: Vec<&Value> = cals.iter().filter(|c| c["_shared"] != true && !is_subscription(c["id"].as_str().unwrap_or(""))).collect();
    x.set_imp_calendars(ModelRc::new(VecModel::from(targets.iter().map(|c| slint::SharedString::from(c["name"].as_str().unwrap_or("Calendar"))).collect::<Vec<_>>())));
    x.set_imp_calendar(targets.iter().position(|c| c["isDefault"] == true).unwrap_or(0) as i32);
    IMPORT_TARGETS.with(|l| *l.borrow_mut() = targets.iter().map(|c| c["id"].as_str().unwrap_or("").to_string()).collect());
    IMPORT.with(|i| *i.borrow_mut() = None);
    x.set_imp_file("".into());
    x.set_imp_info("".into());
    x.set_imp_status("".into());
    x.set_imp_ready(false);
    x.set_imp_busy(false);
    x.set_imp_alerts("".into());
    x.set_imp_keep_alerts(true);
    x.set_imp_open(true);
}

/// The line that asks whether to keep the file's reminders; empty when no event in it has any.
pub fn alerts_note(list: &[Value]) -> String {
    match list.iter().filter(|e| has_alerts(e)).count() {
        0 => String::new(),
        1 => "1 event in this file has its own reminders.".into(),
        n => format!("{n} events in this file have their own reminders."),
    }
}

pub fn import_choose(u: &AppWindow, c: Arc<Client>) {
    let picked = if crate::automation::enabled() { crate::automation::pick_files() } else {
        rfd::FileDialog::new().add_filter("iCalendar", &["ics", "ical", "ifb", "icalendar"]).pick_file().map(|p| vec![p])
    };
    let Some(path) = picked.and_then(|v| v.into_iter().next()) else { return };
    let x = u.global::<crate::CalX>();
    let name = path.file_name().map(|n| n.to_string_lossy().to_string()).unwrap_or_default();
    x.set_imp_file(name.clone().into());
    x.set_imp_info("Reading…".into());
    x.set_imp_alerts("".into());
    x.set_imp_ready(false);
    x.set_imp_busy(true);
    let w = u.as_weak();
    std::thread::spawn(move || {
        let res = std::fs::read(&path).map_err(|e| e.to_string()).and_then(|b| parse_ics(&c, b));
        let _ = w.upgrade_in_event_loop(move |u| {
            let x = u.global::<crate::CalX>();
            x.set_imp_busy(false);
            match res {
                Ok(list) if !list.is_empty() => {
                    let n = list.len();
                    x.set_imp_alerts(alerts_note(&list).into());
                    x.set_imp_keep_alerts(true);
                    IMPORT.with(|i| *i.borrow_mut() = Some((name, list)));
                    x.set_imp_info(format!("{n} event{} found", if n == 1 { "" } else { "s" }).into());
                    x.set_imp_ready(true);
                }
                Ok(_) => { x.set_imp_info("".into()); x.set_imp_status("There are no events in this file.".into()); }
                Err(e) => { x.set_imp_info("".into()); x.set_imp_status(e.into()); }
            }
        });
    });
}

pub fn import_run(u: &AppWindow, c: Arc<Client>) {
    let x = u.global::<crate::CalX>();
    let Some((_, list)) = IMPORT.with(|i| i.borrow().clone()) else { return };
    let Some(target) = IMPORT_TARGETS.with(|l| l.borrow().get(x.get_imp_calendar().max(0) as usize).cloned()) else { return };
    // asked only when the file has reminders; the answer defaults to keeping them (D-M4-09)
    let keep_alerts = x.get_imp_alerts().is_empty() || x.get_imp_keep_alerts();
    x.set_imp_busy(true);
    x.set_imp_status("".into());
    let w = u.as_weak();
    std::thread::spawn(move || {
        let (acc, raw) = calendar::server_ref(&c, &target);
        let res: Result<(usize, usize, usize, Option<String>), String> = (|| {
            // a uid is unique across calendars on Stalwart: one in the target is skipped, one in
            // another calendar is linked to the target too, the rest is created
            let known = existing_uids(&c, &acc)?;
            let mut seen = std::collections::HashSet::new();
            let (mut skipped, mut linked, mut new) = (0, 0, vec![]);
            for e in list {
                let uid = e["uid"].as_str().unwrap_or("").to_string();
                if !uid.is_empty() && !seen.insert(uid.clone()) { skipped += 1; continue; }
                match known.iter().find(|(u, _, _)| !uid.is_empty() && *u == uid) {
                    Some((_, _, cals)) if cals.get(&raw).is_some() => skipped += 1,
                    Some((_, id, cals)) => {
                        let mut ci = cals.clone();
                        ci[&raw] = json!(true);
                        if c.update("CalendarEvent/set", &acc, vec![(id.clone(), json!({"calendarIds": ci}))]).is_ok_and(|(n, _)| n > 0) { linked += 1; }
                    }
                    None => new.push(import_event(&e, &raw, keep_alerts)),
                }
            }
            let (made, err) = create_batch(&c, &acc, new);
            Ok((made, linked, skipped, err))
        })();
        let w2 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| {
            let x = u.global::<crate::CalX>();
            x.set_imp_busy(false);
            match res {
                Ok((made, linked, skipped, err)) => {
                    let mut s = format!("Imported {made} event{}", if made == 1 { "" } else { "s" });
                    if linked > 0 { s += &format!(", added {linked} existing"); }
                    if skipped > 0 { s += &format!(", {skipped} already there"); }
                    if let Some(e) = err { s += &format!(". Some failed: {e}"); }
                    x.set_imp_status(format!("{s}.").into());
                    x.set_imp_ready(false);
                    calendar::load(w2, c);
                }
                Err(e) => x.set_imp_status(format!("Import failed: {e}").into()),
            }
        });
    });
}

// ---------------- webcal subscriptions ----------------

const INTERVALS: [i64; 5] = [15, 30, 60, 360, 1440];

fn owner(c: &Client) -> String { format!("{}|{}", c.base.trim_end_matches('/').to_lowercase(), c.username.to_lowercase()) }

/// Subscriptions of this login (kept in calendar.json, Bulwark ICalSubscription).
pub fn subscriptions(c: &Client) -> Vec<Value> {
    let o = owner(c);
    crate::reminders::load_state()["subscriptions"].as_array().cloned().unwrap_or_default().into_iter().filter(|s| s["owner"] == o.as_str()).collect()
}

fn save_subscriptions(c: &Client, mine: Vec<Value>) {
    let o = owner(c);
    let mut st = crate::reminders::load_state();
    let mut all: Vec<Value> = st["subscriptions"].as_array().cloned().unwrap_or_default().into_iter().filter(|s| s["owner"] != o.as_str()).collect();
    all.extend(mine);
    st["subscriptions"] = Value::Array(all);
    crate::reminders::save_state(&st);
}

thread_local! { static SUB_CALS: RefCell<Vec<String>> = const { RefCell::new(Vec::new()) }; }

pub fn is_subscription(cal_id: &str) -> bool { SUB_CALS.with(|s| s.borrow().iter().any(|x| x == cal_id)) }

/// webcal(s):// is fetched over https.
pub fn normalize_feed_url(url: &str) -> Option<String> {
    let u = url.trim();
    let lower = u.to_lowercase();
    let u = if lower.starts_with("webcals://") { format!("https://{}", &u[10..]) } else if lower.starts_with("webcal://") { format!("https://{}", &u[9..]) } else { u.to_string() };
    (u.starts_with("https://") || u.starts_with("http://")).then_some(u).filter(|x| x.len() > 10 && !x.contains(char::is_whitespace))
}

fn render_subs(u: &AppWindow, c: &Client) {
    let subs = subscriptions(c);
    SUB_CALS.with(|s| *s.borrow_mut() = subs.iter().filter_map(|x| x["calendarId"].as_str().map(String::from)).collect());
    let rows: Vec<CxSub> = subs.iter().map(|s| CxSub {
        id: s["id"].as_str().unwrap_or("").into(), name: s["name"].as_str().unwrap_or("").into(),
        color: calendar::hex_color(s["color"].as_str().unwrap_or("")).unwrap_or(slint::Color::from_rgb_u8(0x3b, 0x82, 0xf6)),
        info: s["lastRefreshed"].as_i64().map(|t| format!("Updated {}", chrono::DateTime::from_timestamp_millis(t).map(|d| d.with_timezone(&Local).format("%b %-d %H:%M").to_string()).unwrap_or_default())).unwrap_or_default().into(),
    }).collect();
    u.global::<crate::CalX>().set_subs(ModelRc::new(VecModel::from(rows)));
}

fn color_hex(c: slint::Color) -> String { format!("#{:02x}{:02x}{:02x}", c.red(), c.green(), c.blue()) }

pub fn sub_begin(u: &AppWindow, id: &str) {
    let x = u.global::<crate::CalX>();
    let s = client().map(|c| subscriptions(&c)).unwrap_or_default().into_iter().find(|s| s["id"] == id);
    x.set_sub_id(id.into());
    x.set_sub_url(s.as_ref().and_then(|s| s["url"].as_str()).unwrap_or("").into());
    x.set_sub_name(s.as_ref().and_then(|s| s["name"].as_str()).unwrap_or("").into());
    let colors: Vec<String> = x.get_sub_colors().iter().map(color_hex).collect();
    x.set_sub_color(s.as_ref().and_then(|s| s["color"].as_str()).and_then(|c| colors.iter().position(|x| x.eq_ignore_ascii_case(c))).unwrap_or(0) as i32);
    x.set_sub_interval(s.as_ref().and_then(|s| s["refreshInterval"].as_i64()).and_then(|m| INTERVALS.iter().position(|i| *i == m)).unwrap_or(2) as i32);
    x.set_sub_status(s.as_ref().and_then(|s| s["error"].as_str()).map(|e| format!("Last refresh failed: {e}")).unwrap_or_default().into());
    x.set_sub_busy(false);
    sub_url_changed(u, &x.get_sub_url());
    x.set_sub_open(true);
}

/// The address field changed: a plain http address gets its warning under the field at once.
pub fn sub_url_changed(u: &AppWindow, text: &str) {
    let plain = normalize_feed_url(text).is_some_and(|x| crate::feed_guard::is_plain_http(&x));
    u.global::<crate::CalX>().set_sub_warning(if plain { crate::feed_guard::PLAIN_HTTP_WARNING } else { "" }.into());
}

/// The feed's events into the subscription calendar: new ones created, gone ones removed,
/// changed ones updated (Bulwark refreshICalSubscription). Returns how many were created.
pub fn refresh_feed(c: &Client, sub: &Value) -> Result<usize, String> {
    let url = normalize_feed_url(sub["url"].as_str().unwrap_or("")).ok_or("This isn't a calendar address.")?;
    let cal = sub["calendarId"].as_str().unwrap_or("").to_string();
    let (acc, raw) = calendar::server_ref(c, &cal);
    let body = fetch_feed(&url)?;
    let mut feed = parse_ics(c, body)?;
    fill_uids(&url, &mut feed);
    let ids = c.query_all("CalendarEvent/query", &acc, json!({"filter": {"inCalendar": raw}}))
        .or_else(|_| c.query_all("CalendarEvent/query", &acc, json!({})))?;
    let props = json!(["id", "uid", "calendarIds", "title", "description", "start", "duration", "timeZone", "showWithoutTime", "locations", "recurrenceRules", "recurrenceOverrides", "status"]);
    let have: Vec<Value> = c.get_all("CalendarEvent/get", &acc, &ids, Some(&props))?.into_iter().filter(|e| e["calendarIds"].get(&raw).is_some()).collect();
    let feed_uids: std::collections::HashSet<String> = feed.iter().filter_map(|e| e["uid"].as_str().map(String::from)).collect();
    // gone from the feed: removed here (or unlinked when the user also keeps it elsewhere)
    let mut destroy = vec![];
    let mut unlink = vec![];
    for e in &have {
        if e["uid"].as_str().is_some_and(|u| feed_uids.contains(u)) { continue; }
        let others = e["calendarIds"].as_object().map(|o| o.keys().filter(|k| **k != raw).count()).unwrap_or(0);
        if others == 0 { destroy.push(e["id"].clone()); } else {
            let mut ci = e["calendarIds"].clone();
            if let Some(o) = ci.as_object_mut() { o.remove(&raw); }
            unlink.push((e["id"].as_str().unwrap_or("").to_string(), json!({"calendarIds": ci})));
        }
    }
    let mut failed: Vec<String> = vec![];
    for chunk in destroy.chunks(c.max_set()) {
        let r = c.call(json!([["CalendarEvent/set", {"accountId": acc, "destroy": chunk}, "d"]]))?;
        if let Some(nd) = r[0][1]["notDestroyed"].as_object() { failed.extend(nd.keys().map(|k| format!("{k}: not removed"))); }
    }
    if !unlink.is_empty() { failed.extend(c.update("CalendarEvent/set", &acc, unlink)?.1); }
    // still in the feed: take what the feed changed
    const FEED: [&str; 9] = ["title", "description", "start", "duration", "timeZone", "showWithoutTime", "locations", "recurrenceRules", "status"];
    let mut changes = vec![];
    for f in &feed {
        let Some(uid) = f["uid"].as_str() else { continue };
        let Some(cur) = have.iter().find(|e| e["uid"] == uid && e["calendarIds"].as_object().is_some_and(|o| o.len() == 1)) else { continue };
        let clean = sanitize_feed(f, &raw);
        let mut patch = Map::new();
        for k in FEED {
            let (a, b) = (clean.get(k).cloned().unwrap_or(Value::Null), cur.get(k).cloned().unwrap_or(Value::Null));
            if a != b && !(k == "recurrenceRules" && recurrence::same_rules(&a, &b)) { patch.insert(k.into(), a); }
        }
        if !patch.is_empty() { changes.push((cur["id"].as_str().unwrap_or("").to_string(), Value::Object(patch))); }
    }
    if !changes.is_empty() { failed.extend(c.update("CalendarEvent/set", &acc, changes)?.1); }
    let have_uids: std::collections::HashSet<&str> = have.iter().filter_map(|e| e["uid"].as_str()).collect();
    let mut seen = std::collections::HashSet::new();
    let new: Vec<Value> = feed.iter().filter(|e| e["uid"].as_str().is_none_or(|u| !have_uids.contains(u) && seen.insert(u.to_string()))) .map(|e| sanitize_feed(e, &raw)).collect();
    let want = new.len();
    let (made, err) = create_batch(c, &acc, new);
    if made < want { failed.push(err.unwrap_or_else(|| format!("{} events not added", want - made))); }
    if !failed.is_empty() { return Err(refresh_error(&failed)); }
    Ok(made)
}

const FEED_MAX_BYTES: u64 = 20_000_000;

/// GET the feed. Every address (the first and each redirect, at most 5) is checked by
/// `feed_guard` before it is requested: not this computer or a local network, and for a name every
/// address it resolves to; the connection is then made to exactly the addresses that were checked
/// (the name is resolved once), so a DNS answer that changes cannot redirect it (G-006). The body
/// is never read past FEED_MAX_BYTES (checked while reading). A system proxy is not used: it
/// would resolve the name itself, beyond the check.
fn fetch_feed(url: &str) -> Result<Vec<u8>, String> {
    use std::io::Read;
    crate::feed_guard::follow(url, &crate::feed_guard::system_resolve, |hop| {
        let mut b = reqwest::blocking::Client::builder().timeout(std::time::Duration::from_secs(60)).user_agent("Barbican calendar")
            .redirect(reqwest::redirect::Policy::none()).no_proxy();
        if let Some(pin) = &hop.pin { b = b.resolve_to_addrs(&hop.host, pin); }
        let http = b.build().map_err(|e| e.to_string())?;
        let mut req = http.get(hop.url.clone());
        if let Some((user, pass)) = &hop.auth { req = req.basic_auth(user, Some(pass)); }
        let resp = req.send().map_err(|e| format!("Could not fetch the calendar: {e}"))?;
        if crate::redirect::is_redirect(resp.status().as_u16()) {
            return Ok(crate::feed_guard::Step::Redirect(resp.headers().get(reqwest::header::LOCATION).and_then(|v| v.to_str().ok()).map(String::from)));
        }
        if !resp.status().is_success() { return Err(format!("The calendar address answered {}.", resp.status())); }
        if resp.content_length().is_some_and(|n| n > FEED_MAX_BYTES) { return Err("The calendar is too large.".into()); }
        let mut body = vec![];
        resp.take(FEED_MAX_BYTES + 1).read_to_end(&mut body).map_err(|e| e.to_string())?;
        if body.len() as u64 > FEED_MAX_BYTES { return Err("The calendar is too large.".into()); }
        if !String::from_utf8_lossy(&body[..body.len().min(4096)]).contains("BEGIN:VCALENDAR") { return Err("This address doesn't return a calendar (.ics).".into()); }
        Ok(crate::feed_guard::Step::Done(body))
    })
}

/// A feed event without a UID gets one from what identifies it in the feed (url, start, title;
/// FNV-1a, stable across builds), so each refresh matches it again instead of recreating it.
pub fn stable_uid(url: &str, e: &Value) -> String {
    let mut f: u64 = 0xcbf29ce484222325;
    for part in [url, e["start"].as_str().unwrap_or(""), e["title"].as_str().unwrap_or("")] {
        for b in part.bytes().chain(std::iter::once(0x1f)) { f = (f ^ b as u64).wrapping_mul(0x100000001b3); }
    }
    format!("barbican-feed-{f:016x}")
}

pub fn fill_uids(url: &str, feed: &mut [Value]) {
    for e in feed.iter_mut() {
        if e["uid"].as_str().is_none_or(|u| u.trim().is_empty()) { let u = stable_uid(url, e); e["uid"] = json!(u); }
    }
}

/// Subscriptions never carry the feed's own alarms (Bulwark does not fire remote alarms).
pub fn sanitize_feed(src: &Value, cal: &str) -> Value {
    let mut v = import_event(src, cal, false);
    if let Some(o) = v.as_object_mut() { o.insert("useDefaultAlerts".into(), json!(false)); }
    v
}

fn refresh_error(failed: &[String]) -> String {
    let n = failed.len();
    format!("{n} change{} could not be applied ({})", if n == 1 { "" } else { "s" }, failed.iter().take(3).cloned().collect::<Vec<_>>().join("; "))
}

pub fn sub_save(u: &AppWindow, c: Arc<Client>) {
    let x = u.global::<crate::CalX>();
    let Some(url) = normalize_feed_url(&x.get_sub_url()) else { x.set_sub_status("Enter a webcal:// or https:// address.".into()); return };
    // a local address or name is refused at once (the fetch checks the resolved addresses too)
    if let Err(r) = crate::feed_guard::precheck(&url) { x.set_sub_status(r.message().into()); return; }
    let name = x.get_sub_name().trim().to_string();
    if name.is_empty() { x.set_sub_status("Give the calendar a name.".into()); return; }
    let color = x.get_sub_colors().row_data(x.get_sub_color().max(0) as usize).map(color_hex).unwrap_or_else(|| "#3b82f6".into());
    let interval = INTERVALS[x.get_sub_interval().clamp(0, 4) as usize];
    let id = x.get_sub_id().to_string();
    x.set_sub_busy(true);
    let plain = crate::feed_guard::is_plain_http(&url);
    x.set_sub_status(if id.is_empty() { "Subscribing…" } else { "Saving…" }.into());
    let w = u.as_weak();
    std::thread::spawn(move || {
        let acc = c.account_for(CAL);
        let mut subs = subscriptions(&c);
        let res: Result<String, String> = (|| {
            if let Some(s) = subs.iter_mut().find(|s| s["id"] == id.as_str()) {
                let url_changed = s["url"] != url.as_str();
                let cal = s["calendarId"].as_str().unwrap_or("").to_string();
                let (cacc, craw) = calendar::server_ref(&c, &cal);
                c.call(json!([["Calendar/set", {"accountId": cacc, "update": {craw: {"name": name, "color": color}}}, "u"]]))?;
                s["url"] = json!(url); s["name"] = json!(name); s["color"] = json!(color); s["refreshInterval"] = json!(interval);
                if url_changed { let s2 = s.clone(); refresh_feed(&c, &s2)?; s["lastRefreshed"] = json!(chrono::Utc::now().timestamp_millis()); }
                return Ok("Subscription saved.".into());
            }
            // a calendar of its own; removed again when the first fetch fails
            let r = c.call(json!([["Calendar/set", {"accountId": acc, "create": {"c": {"name": name, "color": color, "isVisible": true, "isSubscribed": true}}}, "c"]]))?;
            let cal = r[0][1]["created"]["c"]["id"].as_str().map(String::from).ok_or_else(|| crate::jmap::set_error(&r[0][1]["notCreated"]["c"]))?;
            let sub = json!({"id": calendar::new_uid(), "url": url, "calendarId": cal, "accountId": acc, "owner": owner(&c), "name": name, "color": color,
                "refreshInterval": interval, "lastRefreshed": chrono::Utc::now().timestamp_millis()});
            match refresh_feed(&c, &sub) {
                Ok(n) => { subs.push(sub); Ok(format!("Subscribed: {n} event{} added.", if n == 1 { "" } else { "s" })) }
                Err(e) => {
                    let _ = c.call(json!([["Calendar/set", {"accountId": acc, "destroy": [cal], "onDestroyRemoveEvents": true}, "d"]]));
                    Err(e)
                }
            }
        })();
        if res.is_ok() { save_subscriptions(&c, subs); }
        let w2 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| {
            let x = u.global::<crate::CalX>();
            x.set_sub_busy(false);
            match res {
                Ok(msg) => {
                    x.set_sub_open(false);
                    // an address that is not encrypted is accepted, with the warning
                    crate::mail::show_toast(&u, &if plain { format!("{msg} Warning: {}", crate::feed_guard::PLAIN_HTTP_WARNING) } else { msg }, false);
                    render_subs(&u, &c);
                    calendar::load(w2, c);
                }
                Err(e) => x.set_sub_status(e.into()),
            }
        });
    });
}

pub fn sub_remove(u: &AppWindow, c: Arc<Client>, id: &str) {
    let x = u.global::<crate::CalX>();
    let subs = subscriptions(&c);
    let Some(s) = subs.iter().find(|s| s["id"] == id).cloned() else { x.set_sub_open(false); return };
    x.set_sub_busy(true);
    let w = u.as_weak();
    std::thread::spawn(move || {
        let (acc, raw) = calendar::server_ref(&c, s["calendarId"].as_str().unwrap_or(""));
        let _ = c.call(json!([["Calendar/set", {"accountId": acc, "destroy": [raw], "onDestroyRemoveEvents": true}, "d"]]));
        save_subscriptions(&c, subs.into_iter().filter(|x| x["id"] != s["id"]).collect());
        let w2 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| {
            let x = u.global::<crate::CalX>();
            x.set_sub_busy(false);
            x.set_sub_open(false);
            crate::mail::show_toast(&u, "Unsubscribed", false);
            render_subs(&u, &c);
            calendar::load(w2, c);
        });
    });
}

/// Refresh one subscription now, or (id empty) every one whose interval has passed.
pub fn sub_refresh(u: &AppWindow, c: Arc<Client>, id: &str) {
    let now = chrono::Utc::now().timestamp_millis();
    let subs = subscriptions(&c);
    let due: Vec<Value> = subs.iter().filter(|s| if id.is_empty() {
        now - s["lastRefreshed"].as_i64().unwrap_or(0) >= s["refreshInterval"].as_i64().unwrap_or(60) * 60_000
    } else { s["id"] == id }).cloned().collect();
    if due.is_empty() { return; }
    let manual = !id.is_empty();
    let w = u.as_weak();
    std::thread::spawn(move || {
        let mut subs = subscriptions(&c);
        let mut msg = String::new();
        for d in due {
            let r = refresh_feed(&c, &d);
            if let Some(s) = subs.iter_mut().find(|s| s["id"] == d["id"]) {
                s["lastRefreshed"] = json!(now);
                match &r { Ok(_) => { if let Some(o) = s.as_object_mut() { o.remove("error"); } } Err(e) => s["error"] = json!(e) }
            }
            msg = match r { Ok(n) => format!("{} refreshed ({n} new)", d["name"].as_str().unwrap_or("")), Err(e) => format!("{}: {e}", d["name"].as_str().unwrap_or("")) };
        }
        save_subscriptions(&c, subs);
        let w2 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| {
            if manual { crate::mail::show_toast(&u, &msg, false); }
            render_subs(&u, &c);
            calendar::load(w2, c);
        });
    });
}

thread_local! { static LAST_SUB_CHECK: std::cell::Cell<i64> = const { std::cell::Cell::new(0) }; }

/// After the calendars loaded: the subscription list, and the subscriptions due a refresh.
pub fn after_load(u: &AppWindow, _w: slint::Weak<AppWindow>, c: Arc<Client>) {
    render_subs(u, &c);
    // drop subscriptions whose calendar is gone (deleted elsewhere)
    let cals = calendar::calendars();
    let subs = subscriptions(&c);
    let kept: Vec<Value> = subs.iter().filter(|s| cals.iter().any(|x| x["id"] == s["calendarId"])).cloned().collect();
    if kept.len() != subs.len() && !cals.is_empty() { save_subscriptions(&c, kept); render_subs(u, &c); }
    let now = chrono::Utc::now().timestamp_millis();
    if now - LAST_SUB_CHECK.with(|l| l.get()) > 60_000 {
        LAST_SUB_CHECK.with(|l| l.set(now));
        sub_refresh(u, c, "");
    }
    if calendar::view() == 4 { render_tasks_view(u); }
}

// ---------------- wiring ----------------

/// Hook every calendar_ext.slint callback up (once, at start).
pub fn wire(ui: &AppWindow, st: crate::Shared) {
    SHARED.with(|s| *s.borrow_mut() = Some(st.clone()));
    let x = ui.global::<crate::CalX>();
    macro_rules! with_ui { ($w:ident, $body:expr) => {{ let $w = ui.as_weak(); $body }} }
    let cl = |st: &crate::Shared| st.lock().unwrap().clone();

    with_ui!(w, x.on_ed_att_query_changed(move |q| cal_editor::att_query(&w.unwrap(), &q)));
    with_ui!(w, x.on_ed_att_add(move |t| cal_editor::att_add(&w.unwrap(), &t)));
    with_ui!(w, x.on_ed_att_pick(move |i| cal_editor::att_pick(&w.unwrap(), i)));
    with_ui!(w, x.on_ed_att_remove(move |e| cal_editor::att_remove(&w.unwrap(), &e)));
    with_ui!(w, x.on_ed_alert_add(move || cal_editor::alert_add(&w.unwrap())));
    with_ui!(w, x.on_ed_alert_remove(move |i| cal_editor::alert_remove(&w.unwrap(), i)));
    with_ui!(w, x.on_ed_alert_set(move |i, v, unit, action| cal_editor::alert_set(&w.unwrap(), i, &v, unit, action)));
    with_ui!(w, x.on_ed_alerts_customize(move || cal_editor::alerts_customize(&w.unwrap())));
    with_ui!(w, x.on_ed_alerts_use_default(move || cal_editor::alerts_use_default(&w.unwrap())));
    with_ui!(w, x.on_ed_tz_query_changed(move |q| cal_editor::tz_query(&w.unwrap(), &q)));
    with_ui!(w, x.on_ed_tz_pick(move |z| cal_editor::tz_pick(&w.unwrap(), &z)));
    with_ui!(w, x.on_ed_times_changed(move |k| cal_editor::times_changed(&w.unwrap(), &k)));
    { let (w, st) = (ui.as_weak(), st.clone()); x.on_ed_find(move || { if let Some(c) = cl(&st) { cal_editor::find(&w.unwrap(), c); } }); }
    with_ui!(w, x.on_ed_pick_slot(move |i| cal_editor::pick_slot(&w.unwrap(), i)));
    with_ui!(w, x.on_rr_begin(move || cal_editor::rr_begin(&w.unwrap())));
    with_ui!(w, x.on_rr_changed(move || cal_editor::rr_changed(&w.unwrap())));
    with_ui!(w, x.on_rr_toggle_day(move |i| cal_editor::rr_toggle_day(&w.unwrap(), i)));
    with_ui!(w, x.on_rr_save(move || cal_editor::rr_save(&w.unwrap())));
    with_ui!(w, x.on_rr_cancel(move || cal_editor::rr_cancel(&w.unwrap())));
    { let (w, st) = (ui.as_weak(), st.clone()); x.on_scope_go(move |i| { if let Some(c) = cl(&st) { cal_editor::scope_go(&w.unwrap(), c, i); } }); }
    with_ui!(w, x.on_quick(move |d, h, m, px, py| cal_editor::quick(&w.unwrap(), &d, h, m, px, py)));
    { let (w, st) = (ui.as_weak(), st.clone()); x.on_qc_save(move || { if let Some(c) = cl(&st) { cal_editor::qc_save(&w.unwrap(), c); } }); }
    with_ui!(w, x.on_qc_more(move || cal_editor::qc_more(&w.unwrap())));
    with_ui!(w, x.on_reminder_act(move |k, a| crate::reminders::act(&w.unwrap(), &k, &a)));
    with_ui!(w, x.on_missed_done(move |open| crate::reminders::missed_done(&w.unwrap(), open)));
    with_ui!(w, x.on_agenda_open(move |id, _kind| calendar::edit(&w.unwrap(), &id, "", -1)));
    with_ui!(w, x.on_task_filter_set(move |f| { let u = w.unwrap(); u.global::<crate::CalX>().set_task_filter(f); render_tasks_view(&u); }));
    with_ui!(w, x.on_task_quick_changed(move |t| task_quick_changed(&w.unwrap(), &t)));
    { let (w, st) = (ui.as_weak(), st.clone()); x.on_task_quick_add(move |t| { if let Some(c) = cl(&st) { task_quick_add(&w.unwrap(), c, &t); } }); }
    with_ui!(w, x.on_task_open(move |id| open_task(&w.unwrap(), &id)));
    { let (w, st) = (ui.as_weak(), st.clone()); x.on_task_toggle(move |id| { if let Some(c) = cl(&st) { calendar::toggle_task(&w.unwrap(), c, &id); } }); }
    { let (w, st) = (ui.as_weak(), st.clone()); x.on_te_save(move || { if let Some(c) = cl(&st) { task_save(&w.unwrap(), c); } }); }
    { let (w, st) = (ui.as_weak(), st.clone()); x.on_te_delete(move || { if let Some(c) = cl(&st) { task_delete(&w.unwrap(), c); } }); }
    with_ui!(w, x.on_imp_begin(move || import_begin(&w.unwrap())));
    { let (w, st) = (ui.as_weak(), st.clone()); x.on_imp_choose(move || { if let Some(c) = cl(&st) { import_choose(&w.unwrap(), c); } }); }
    { let (w, st) = (ui.as_weak(), st.clone()); x.on_imp_run(move || { if let Some(c) = cl(&st) { import_run(&w.unwrap(), c); } }); }
    with_ui!(w, x.on_sub_begin(move |id| sub_begin(&w.unwrap(), &id)));
    with_ui!(w, x.on_sub_url_changed(move |t| sub_url_changed(&w.unwrap(), &t)));
    { let (w, st) = (ui.as_weak(), st.clone()); x.on_sub_save(move || { if let Some(c) = cl(&st) { sub_save(&w.unwrap(), c); } }); }
    { let (w, st) = (ui.as_weak(), st.clone()); x.on_sub_remove(move |id| { if let Some(c) = cl(&st) { sub_remove(&w.unwrap(), c, &id); } }); }
    { let (w, st) = (ui.as_weak(), st.clone()); x.on_sub_refresh(move |id| { if let Some(c) = cl(&st) { sub_refresh(&w.unwrap(), c, &id); } }); }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn agenda_titles_lose_marker_and_day_filter_toggles() {
        assert_eq!(agenda_task_title("\u{25CB} Pay rent"), "Pay rent");
        assert_eq!(agenda_task_title("\u{2713} Done thing"), "Done thing");
        assert_eq!(agenda_task_title("Plain"), "Plain");
        toggle_task_day("2026-11-18");
        assert_eq!(task_day(), NaiveDate::from_ymd_opt(2026, 11, 18));
        toggle_task_day("2026-11-18");
        assert_eq!(task_day(), None);
        toggle_task_day("2026-11-19");
        toggle_task_day("clear");
        assert_eq!(task_day(), None);
        let t = json!({"due": "2026-11-18T09:00:00"});
        assert!(task_due_on(&t, NaiveDate::from_ymd_opt(2026, 11, 18).unwrap()));
        assert!(!task_due_on(&t, NaiveDate::from_ymd_opt(2026, 11, 19).unwrap()));
    }
    #[test]
    fn feed_uid_is_stable_and_fills_only_missing() {
        let e = json!({"title": "Standup", "start": "2026-11-20T09:00:00"});
        let a = stable_uid("https://x/a.ics", &e);
        assert_eq!(a, stable_uid("https://x/a.ics", &e));
        assert_ne!(a, stable_uid("https://x/b.ics", &e));
        assert_ne!(a, stable_uid("https://x/a.ics", &json!({"title": "Standup", "start": "2026-11-21T09:00:00"})));
        let mut feed = vec![e.clone(), json!({"uid": "keep", "title": "K"}), json!({"uid": " ", "title": "B"})];
        fill_uids("https://x/a.ics", &mut feed);
        assert_eq!(feed[0]["uid"], a.as_str());
        assert_eq!(feed[1]["uid"], "keep");
        assert!(feed[2]["uid"].as_str().unwrap().starts_with("barbican-feed-"));
    }
    #[test]
    fn feed_events_drop_remote_alerts() {
        let src = json!({"@type": "Event", "uid": "u", "title": "T", "start": "2026-11-20T10:00:00", "alerts": {"a": {"trigger": {"offset": "-PT5M"}}}});
        let f = sanitize_feed(&src, "cal");
        assert!(f.get("alerts").is_none());
        assert_eq!(f["useDefaultAlerts"], false);
        assert!(sanitize_import(&src, "cal")["alerts"].is_object());
        assert_eq!(refresh_error(&["a: x".into()]), "1 change could not be applied (a: x)");
        assert!(refresh_error(&["a".into(), "b".into()]).starts_with("2 changes"));
    }
    #[test]
    fn import_asks_about_alerts_only_when_there_are_some() {
        let with = json!({"@type": "Event", "uid": "a", "title": "T", "start": "2026-11-20T10:00:00", "alerts": {"x": {"trigger": {"offset": "-PT5M"}}}});
        let none = json!({"@type": "Event", "uid": "b", "title": "T", "start": "2026-11-20T10:00:00"});
        let empty = json!({"@type": "Event", "uid": "c", "title": "T", "start": "2026-11-20T10:00:00", "alerts": {}});
        let in_override = json!({"@type": "Event", "uid": "d", "title": "T", "start": "2026-11-20T10:00:00",
            "recurrenceOverrides": {"2026-11-27T10:00:00": {"title": "Moved", "alerts/x/trigger/offset": "-PT1H"}}});
        assert!(has_alerts(&with) && has_alerts(&in_override));
        assert!(!has_alerts(&none) && !has_alerts(&empty));
        // overrides that only remove reminders do not count as having some
        let removal = json!({"@type": "Event", "uid": "r", "title": "T", "start": "2026-11-20T10:00:00",
            "recurrenceOverrides": {"2026-11-27T10:00:00": {"alerts": null, "alerts/x": null}, "2026-12-04T10:00:00": {"alerts": {}}}});
        assert!(!has_alerts(&removal));
        assert_eq!(alerts_note(&[removal]), "");
        assert_eq!(alerts_note(&[none.clone(), empty.clone()]), "");
        assert_eq!(alerts_note(&[with.clone(), none.clone()]), "1 event in this file has its own reminders.");
        assert_eq!(alerts_note(&[with.clone(), in_override.clone(), none]), "2 events in this file have their own reminders.");
    }

    #[test]
    fn import_keeps_alerts_by_default_and_drops_them_on_no() {
        let src = json!({"@type": "Event", "uid": "a", "title": "T", "start": "2026-11-20T10:00:00", "alerts": {"x": {"trigger": {"offset": "-PT5M"}}},
            "recurrenceRules": [{"frequency": "weekly"}],
            "recurrenceOverrides": {"2026-11-27T10:00:00": {"title": "Moved", "alerts": {"y": {"trigger": {"offset": "-PT1H"}}}, "alerts/x/trigger/offset": "-PT2H"}}});
        // the default (and sanitize_import) keeps them
        assert_eq!(sanitize_import(&src, "cal")["alerts"], src["alerts"]);
        assert_eq!(import_event(&src, "cal", true), sanitize_import(&src, "cal"));
        assert!(import_event(&src, "cal", true)["recurrenceOverrides"]["2026-11-27T10:00:00"]["alerts"].is_object());
        // "No": none in the event or in its overrides; the rest of the override stays
        let d = import_event(&src, "cal", false);
        assert!(d.get("alerts").is_none());
        assert_eq!(d["recurrenceOverrides"]["2026-11-27T10:00:00"], json!({"title": "Moved"}));
        assert_eq!(d["useDefaultAlerts"], false);
        assert!(import_event(&src, "cal", true).get("useDefaultAlerts").is_none());
        assert_eq!(d["title"], "T");
        assert_eq!(d["recurrenceRules"], src["recurrenceRules"]);
        assert_eq!(d["calendarIds"], json!({"cal": true}));
        assert!(!has_alerts(&d));
        // a feed never keeps them, nor the calendar's defaults
        let f = sanitize_feed(&src, "cal");
        assert!(f.get("alerts").is_none() && !has_alerts(&f));
        assert_eq!(f["useDefaultAlerts"], false);
    }

    fn d(s: &str) -> NaiveDate { NaiveDate::parse_from_str(s, "%Y-%m-%d").unwrap() }

    #[test]
    fn quick_task_dates() {
        let mon = d("2026-11-16");
        assert_eq!(parse_task_due("Buy milk tomorrow", mon), Some(("Buy milk".into(), d("2026-11-17"))));
        assert_eq!(parse_task_due("Report due fri", mon), Some(("Report".into(), d("2026-11-20"))));
        assert_eq!(parse_task_due("Call Ann monday", mon), Some(("Call Ann".into(), d("2026-11-23"))));
        assert_eq!(parse_task_due("Taxes in 3 days", mon), Some(("Taxes".into(), d("2026-11-19"))));
        assert_eq!(parse_task_due("Plan next week", mon), Some(("Plan".into(), d("2026-11-23"))));
        assert_eq!(parse_task_due("Ship by 2026-12-01", mon), Some(("Ship".into(), d("2026-12-01"))));
        assert_eq!(parse_task_due("tomorrow", mon), None);
        assert_eq!(parse_task_due("Read a book", mon), None);
    }

    #[test]
    fn task_labels_and_filters() {
        let today = d("2026-11-16");
        let t = |due: &str, p: &str| json!({"due": due, "progress": p, "showWithoutTime": true});
        assert_eq!(due_label(&t("2026-11-16T00:00:00", "needs-action"), today, false), ("Today".into(), 1));
        assert_eq!(due_label(&t("2026-11-17T00:00:00", "needs-action"), today, false), ("Tomorrow".into(), 0));
        assert_eq!(due_label(&t("2026-11-03T00:00:00", "needs-action"), today, false), ("Overdue · Nov 3".into(), 2));
        assert_eq!(due_label(&t("2026-11-03T00:00:00", "completed"), today, false), ("Nov 3".into(), 0));
        assert!(is_overdue(&t("2026-11-15T00:00:00", "in-process"), today));
        assert!(!is_overdue(&t("2026-11-15T00:00:00", "cancelled"), today));
        assert!(task_matches(&t("2026-11-15T00:00:00", "completed"), 3, today));
        assert!(!task_matches(&t("2026-11-15T00:00:00", "completed"), 1, today));
        assert_eq!(priority_level(1), 1);
        assert_eq!(priority_level(5), 2);
        assert_eq!(priority_level(9), 3);
        assert_eq!(priority_level(0), 0);
    }

    #[test]
    fn task_field_mapping() {
        let f = task_fields(" Pay ", "", "2026-11-20", Some("14:30"), 1, 0, 2, 3, "Europe/Istanbul").unwrap();
        assert_eq!(f["title"], "Pay");
        assert_eq!(f["due"], "2026-11-20T14:30:00");
        assert_eq!(f["timeZone"], "Europe/Istanbul");
        assert_eq!(f["priority"], 1);
        assert_eq!(f["recurrenceRules"][0]["frequency"], "weekly");
        assert_eq!(f["alerts"]["alert1"]["trigger"]["offset"], "-PT15M");
        assert_eq!(f["alerts"]["alert1"]["trigger"]["relativeTo"], "end");
        let f = task_fields("x", "", "", None, 0, 2, 0, 3, "UTC").unwrap();
        assert!(f["due"].is_null() && f["alerts"].is_null());
        assert_eq!(f["progress"], "completed");
        assert!(task_fields("x", "", "20-11", None, 0, 0, 0, 0, "UTC").is_err());
    }

    #[test]
    fn birthdays() {
        let cards = vec![
            json!({"id": "c1", "name": {"full": "Ann Lee"}, "anniversaries": {"b": {"kind": "birth", "date": {"@type": "PartialDate", "year": 1990, "month": 11, "day": 20}}}}),
            json!({"id": "c2", "name": {"full": "Leap Kid"}, "anniversaries": {"b": {"kind": "birth", "date": "2004-02-29"}, "w": {"kind": "wedding", "date": "2010-06-01"}}}),
            json!({"id": "c3", "name": {"full": "No Year"}, "anniversaries": {"b": {"kind": "birth", "date": "--12-24"}}}),
        ];
        let o = birthdays_in(&cards, d("2026-11-01"), d("2026-12-31"));
        assert_eq!(o.len(), 2);
        assert_eq!(o[0].title, "\u{1F382} Ann Lee (36)");
        assert_eq!(o[0].id, "bday:c1:b:2026");
        assert_eq!(o[1].title, "\u{1F382} No Year");
        let leap = birthdays_in(&cards, d("2027-02-01"), d("2027-03-01"));
        assert_eq!(leap[0].start.date(), d("2027-02-28"));
        assert_eq!(birthday_parts(&json!({"@type": "Timestamp", "utc": "1985-03-04T00:00:00Z"})), Some((3, 4, Some(1985))));
    }

    #[test]
    fn agenda_parts() {
        let mk = |s: &str, e: &str| Occ { id: "x".into(), base: "x".into(), rid: None, title: "t".into(), start: parse_local(s).unwrap(), end: parse_local(e).unwrap(),
            all_day: false, calendar: "c".into(), location: String::new(), kind: 0, done: false };
        let o = mk("2026-11-16T22:00:00", "2026-11-18T07:30:00");
        assert_eq!(agenda_time(&o, d("2026-11-16"), false), "22:00 – 07:30");
        assert_eq!(agenda_time(&o, d("2026-11-17"), false), "All day");
        assert_eq!(agenda_time(&o, d("2026-11-18"), false), "until 07:30");
    }

    #[test]
    fn imports_are_cleaned() {
        let src = json!({"@type": "Event", "id": "x", "uid": "u", "title": "T", "start": "2026-11-20T10:00:00", "timeZone": "UTC", "calendarIds": {"old": true},
            "replyTo": {"imip": "mailto:org@x.org"}, "participants": {"p": {"name": "A", "sendTo": {"imip": "mailto:a@x.org"}, "foo": 1, "roles": {"attendee": true}}}});
        let c = sanitize_import(&src, "cal");
        assert!(c.get("id").is_none());
        assert_eq!(c["calendarIds"], json!({"cal": true}));
        assert_eq!(c["duration"], "PT0S");
        assert_eq!(c["organizerCalendarAddress"], "mailto:org@x.org");
        assert_eq!(c["participants"]["p"]["calendarAddress"], "mailto:a@x.org");
        assert!(c["participants"]["p"].get("foo").is_none());
        let all_day = sanitize_import(&json!({"start": "2026-11-20T00:00:00", "showWithoutTime": true, "timeZone": "UTC", "duration": "PT0S"}), "c");
        assert_eq!(all_day["duration"], "P1D");
        assert!(all_day.get("timeZone").is_none());
        assert_eq!(normalize_feed_url("webcal://example.com/a.ics").as_deref(), Some("https://example.com/a.ics"));
        assert_eq!(normalize_feed_url("WEBCALS://example.com/a.ics").as_deref(), Some("https://example.com/a.ics"));
        assert_eq!(normalize_feed_url("ftp://x"), None);
    }

    #[test]
    fn people_search() {
        set_people(vec![
            json!({"id": "1", "uid": "u1", "name": {"full": "Ann Lee"}, "emails": {"e": {"address": "ann@x.org"}}}),
            json!({"id": "2", "uid": "u2", "name": {"full": "Bob Annex"}, "emails": {"e": {"address": "bob@x.org"}}}),
            json!({"id": "3", "uid": "g", "kind": "group", "name": {"full": "Annual team"}, "members": {"u1": true, "u2": true}}),
        ]);
        let hits = people_matching("ann", &[], 8);
        assert_eq!(hits[0].0.name, "Ann Lee");
        assert!(hits.iter().any(|(p, m)| p.name == "Annual team" && m.len() == 2));
        assert!(people_matching("ann", &["ann@x.org".into()], 8).iter().all(|(p, _)| p.email != "ann@x.org"));
        set_people(vec![]);
    }
}
