//! Calendar app: JMAP for Calendars (CalendarEvent / Calendar), Day / Week / Month views,
//! mini month, calendar visibility, client-side recurrence expansion (like Bulwark's
//! recurrence-expansion fallback when the server doesn't expand), event create/edit/delete.
use crate::jmap::Client;
use crate::{AppWindow, CalCell, CalEvent, CalItem, MiniDay};
use chrono::{Datelike, Duration, Local, NaiveDate, NaiveDateTime, NaiveTime, Timelike, Weekday};
use serde_json::{json, Value};
use slint::{Color, ComponentHandle, ModelRc, VecModel};
use std::cell::RefCell;
use std::collections::HashSet;
use std::sync::Arc;

pub const HOUR_PX: f32 = 56.0;

#[derive(Default)]
struct State {
    anchor: Option<NaiveDate>,
    view: i32, // 0 day, 1 week, 2 month
    calendars: Vec<Value>,
    hidden: HashSet<String>,
    events: Vec<Occ>,
    query: String,
    editing: Option<Value>,
}

#[derive(Clone)]
struct Occ {
    id: String,
    title: String,
    start: NaiveDateTime,
    end: NaiveDateTime,
    all_day: bool,
    calendar: String,
    location: String,
}

thread_local! { static ST: RefCell<State> = RefCell::new(State::default()); }

const PALETTE: [u32; 8] = [0x3b82f6, 0x22c55e, 0xf97316, 0xa855f7, 0xec4899, 0x14b8a6, 0xeab308, 0xef4444];

fn color_for(cals: &[Value], id: &str) -> Color {
    let i = cals.iter().position(|c| c["id"] == id).unwrap_or(0);
    let hex = cals.get(i).and_then(|c| c["color"].as_str()).and_then(|s| u32::from_str_radix(s.trim_start_matches('#'), 16).ok());
    Color::from_argb_encoded(0xff000000 | hex.unwrap_or(PALETTE[i % PALETTE.len()]))
}

fn anchor() -> NaiveDate {
    ST.with(|s| s.borrow().anchor).unwrap_or_else(|| Local::now().date_naive())
}

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
        tinted: p.bool("calendarTintedEvents", false),
        h12: p.str("timeFormat", "24h") == "12h",
        month_timed: month_timed(&p.str("calendarMonthTimedEvents", "bar")),
        free_scroll: p.bool("calendarFreeScroll", true),
        hover_delay: hover_delay(&p.str("calendarHoverPreview", "delay-500ms")),
        narrow_all_days: p.bool("calendarNarrowWeekAllDays", false),
    }
}

fn week_start(d: NaiveDate) -> NaiveDate {
    let first = cal_prefs().first_dow;
    let dow = d.weekday().num_days_from_sunday();
    d - Duration::days(((dow + 7 - first) % 7) as i64)
}

fn fmt_time(t: NaiveDateTime, h12: bool) -> String {
    if h12 { t.format("%-I:%M %p").to_string() } else { t.format("%H:%M").to_string() }
}

/// The day columns of the Day / Week view: every date of the range [from, to).
fn day_columns(from: NaiveDate, to: NaiveDate) -> Vec<NaiveDate> {
    (0..(to - from).num_days().max(1)).map(|i| from + Duration::days(i)).collect()
}

fn range() -> (NaiveDate, NaiveDate) {
    let (a, view) = (anchor(), ST.with(|s| s.borrow().view));
    match view {
        0 => (a, a + Duration::days(1)),
        1 if week_days_shown() < 7 => (a, a + Duration::days(week_days_shown())),
        1 => { let s = week_start(a); (s, s + Duration::days(7)) }
        _ => {
            let first = a.with_day(1).unwrap();
            let s = week_start(first);
            (s, s + Duration::days(42))
        }
    }
}

// ---------------- duration / recurrence ----------------

fn parse_duration(s: &str) -> Duration {
    // ISO 8601 PnDTnHnMnS / PnW
    let mut total = 0i64;
    let mut num = String::new();
    let mut in_time = false;
    for ch in s.chars() {
        match ch {
            'P' => {}
            'T' => in_time = true,
            '0'..='9' => num.push(ch),
            _ => {
                let n: i64 = num.parse().unwrap_or(0);
                num.clear();
                total += match (ch, in_time) {
                    ('W', _) => n * 7 * 86400, ('D', _) => n * 86400, ('H', true) => n * 3600,
                    ('M', true) => n * 60, ('S', true) => n, _ => 0,
                };
            }
        }
    }
    Duration::seconds(total)
}

fn parse_local(s: &str) -> Option<NaiveDateTime> {
    NaiveDateTime::parse_from_str(&s[..s.len().min(19)], "%Y-%m-%dT%H:%M:%S").ok()
}

fn weekday(s: &str) -> Option<Weekday> {
    Some(match s { "mo" => Weekday::Mon, "tu" => Weekday::Tue, "we" => Weekday::Wed, "th" => Weekday::Thu, "fr" => Weekday::Fri, "sa" => Weekday::Sat, "su" => Weekday::Sun, _ => return None })
}

/// Expand an event into occurrences overlapping [from, to).
fn expand(e: &Value, from: NaiveDateTime, to: NaiveDateTime) -> Vec<(NaiveDateTime, NaiveDateTime)> {
    let Some(start) = e["start"].as_str().and_then(parse_local) else { return vec![] };
    let dur = parse_duration(e["duration"].as_str().unwrap_or("PT1H"));
    let dur = if dur.num_seconds() <= 0 && e["showWithoutTime"] == true { Duration::days(1) } else { dur };
    let excluded: HashSet<String> = e["recurrenceOverrides"].as_object().map(|o| o.iter()
        .filter(|(_, v)| v["excluded"] == true).map(|(k, _)| k[..19.min(k.len())].to_string()).collect()).unwrap_or_default();
    let rules = e["recurrenceRules"].as_array().cloned().unwrap_or_default();
    let mut out = vec![];
    let push = |s: NaiveDateTime, out: &mut Vec<(NaiveDateTime, NaiveDateTime)>| {
        if s < to && s + dur > from && !excluded.contains(&s.format("%Y-%m-%dT%H:%M:%S").to_string()) { out.push((s, s + dur)); }
    };
    if rules.is_empty() { push(start, &mut out); return out; }
    for r in rules {
        let freq = r["frequency"].as_str().unwrap_or("weekly");
        let interval = r["interval"].as_i64().unwrap_or(1).max(1);
        let count = r["count"].as_i64();
        let until = r["until"].as_str().and_then(parse_local);
        let by_day: Vec<Weekday> = r["byDay"].as_array().map(|a| a.iter().filter_map(|d| d["day"].as_str().and_then(weekday)).collect()).unwrap_or_default();
        let mut n = 0i64;
        let mut cur = start;
        let mut guard = 0;
        while guard < 5000 {
            guard += 1;
            if until.is_some_and(|u| cur > u) || cur >= to { break; }
            let candidates: Vec<NaiveDateTime> = if freq == "weekly" && !by_day.is_empty() {
                let ws = cur.date() - Duration::days(cur.date().weekday().num_days_from_monday() as i64);
                let mut v: Vec<NaiveDateTime> = by_day.iter().map(|wd| (ws + Duration::days(wd.num_days_from_monday() as i64)).and_time(start.time())).filter(|d| *d >= start).collect();
                v.sort();
                v
            } else { vec![cur] };
            for c in candidates {
                if until.is_some_and(|u| c > u) { break; }
                if count.is_some_and(|k| n >= k) { break; }
                n += 1;
                push(c, &mut out);
            }
            if count.is_some_and(|k| n >= k) { break; }
            cur = match freq {
                "daily" => cur + Duration::days(interval),
                "weekly" => cur + Duration::weeks(interval),
                "monthly" => {
                    let m = cur.month0() as i64 + interval;
                    let (y, m) = (cur.year() + (m / 12) as i32, (m % 12) as u32 + 1);
                    NaiveDate::from_ymd_opt(y, m, start.day()).map(|d| d.and_time(start.time())).unwrap_or(cur + Duration::days(31))
                }
                "yearly" => NaiveDate::from_ymd_opt(cur.year() + interval as i32, start.month(), start.day()).map(|d| d.and_time(start.time())).unwrap_or(cur + Duration::days(366)),
                _ => cur + Duration::days(interval),
            };
        }
    }
    out
}

// ---------------- loading ----------------

pub fn load(w: slint::Weak<AppWindow>, c: Arc<Client>) {
    if !c.has("urn:ietf:params:jmap:calendars") { return; }
    let (from, to) = range();
    std::thread::spawn(move || {
        let acc = c.account_for("urn:ietf:params:jmap:calendars");
        let tz = crate::agent::local_tz();
        let res: Result<(Vec<Value>, Vec<Value>), String> = (|| {
            let cals = c.call(json!([["Calendar/get", {"accountId": acc, "ids": null, "properties": ["id", "name", "color", "isDefault", "isVisible", "sortOrder", "myRights"]}, "c"]]))?;
            let cals = cals[0][1]["list"].as_array().cloned().unwrap_or_default();
            // series overlapping the range (recurring series start earlier, so no lower bound for them)
            // every id (paged), then the events in maxObjectsInGet chunks: one back-referenced
            // CalendarEvent/get of 5000 ids is over Stalwart's limit (requestTooLarge)
            let ids = c.query_all("CalendarEvent/query", &acc, json!({"timeZone": tz}))?;
            let props = json!(["id", "@type", "title", "start", "duration", "timeZone", "showWithoutTime", "calendarIds", "locations",
                "recurrenceRules", "recurrenceOverrides", "description", "status", "due", "progress", "percentComplete", "uid", "participants", "updated", "excludedRecurrenceIds"]);
            Ok((cals, c.get_all("CalendarEvent/get", &acc, &ids, Some(&props))?))
        })();
        let _ = w.upgrade_in_event_loop(move |u| match res {
            Ok((cals, events)) => {
                let (f, t) = (from.and_hms_opt(0, 0, 0).unwrap(), to.and_hms_opt(0, 0, 0).unwrap());
                let mut occ = vec![];
                for e in &events {
                    if is_task(e) { continue; }
                    for (s, en) in expand(e, f, t) {
                        occ.push(Occ {
                            id: e["id"].as_str().unwrap_or("").into(),
                            title: e["title"].as_str().filter(|x| !x.is_empty()).unwrap_or("(no title)").into(),
                            start: s, end: en,
                            all_day: e["showWithoutTime"] == true,
                            calendar: e["calendarIds"].as_object().and_then(|o| o.keys().next().cloned()).unwrap_or_default(),
                            location: e["locations"].as_object().and_then(|o| o.values().next()).and_then(|l| l["name"].as_str()).unwrap_or("").into(),
                        });
                    }
                }
                occ.sort_by_key(|o| o.start);
                ST.with(|s| { let mut s = s.borrow_mut(); s.calendars = cals; s.events = occ; });
                ST.with(|s| s.borrow_mut().events_raw_set(events));
                render(&u);
                render_tasks(&u);
            }
            Err(e) => crate::mail::show_toast(&u, &format!("Calendar: {e}"), false),
        });
    });
}

thread_local! { static RAW: RefCell<Vec<Value>> = const { RefCell::new(Vec::new()) }; }

trait RawSet { fn events_raw_set(&mut self, v: Vec<Value>); }
impl RawSet for State { fn events_raw_set(&mut self, v: Vec<Value>) { RAW.with(|r| *r.borrow_mut() = v); } }

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
        let ws = week_start(today);
        let names: Vec<slint::SharedString> = (0..7).map(|i| (ws + Duration::days(i)).format("%a").to_string().into()).collect();
        u.set_cal_weekdays(ModelRc::new(VecModel::from(names)));
        u.set_cal_show_weeknums(cp.week_numbers);
        u.set_cal_hour_from(cp.hour_from);
        u.set_cal_hour_to(cp.hour_to);
        u.global::<crate::Prefs>().set_cal_tinted(cp.tinted);
        let mini_first = week_start(a.with_day(1).unwrap());
        let nums: Vec<slint::SharedString> = (0..6).map(|w| (mini_first + Duration::days(w * 7 + 3)).iso_week().week().to_string().into()).collect();
        u.set_cal_week_nums(ModelRc::new(VecModel::from(nums)));
    }
    ST.with(|s| {
        let s = s.borrow();
        let q = s.query.to_lowercase();
        let visible: Vec<&Occ> = s.events.iter()
            .filter(|o| !s.hidden.contains(&o.calendar))
            .filter(|o| q.is_empty() || o.title.to_lowercase().contains(&q) || o.location.to_lowercase().contains(&q))
            .collect();
        u.set_cal_view(s.view);
        u.set_cal_title(match s.view {
            0 => {
                let t = a.format("%A, %B %-d, %Y").to_string();
                if cp.week_numbers { format!("{t}  ·  Week {}", a.iso_week().week()) } else { t }
            }
            1 => format!("{} – {}", from.format("%b %-d"), (to - Duration::days(1)).format("%b %-d, %Y")),
            _ => a.format("%B %Y").to_string(),
        }.into());
        // calendars list
        let cals: Vec<CalItem> = s.calendars.iter().map(|c| {
            let id = c["id"].as_str().unwrap_or("");
            CalItem { id: id.into(), name: c["name"].as_str().unwrap_or("Calendar").into(), color: color_for(&s.calendars, id), visible: !s.hidden.contains(id) }
        }).collect();
        u.set_calendars(ModelRc::new(VecModel::from(cals)));
        // mini month
        let first = a.with_day(1).unwrap();
        let ms = week_start(first);
        let mini: Vec<MiniDay> = (0..42).map(|i| {
            let d = ms + Duration::days(i);
            MiniDay { day: d.day() as i32, in_month: d.month() == a.month(), today: d == today, selected: d >= from && d < to && s.view != 2,
                date: d.format("%Y-%m-%d").to_string().into(), busy: visible.iter().any(|o| o.start.date() <= d && o.end.date() >= d) }
        }).collect();
        u.set_mini_month(ModelRc::new(VecModel::from(mini)));
        u.set_mini_title(a.format("%B %Y").to_string().into());
        if s.view == 2 {
            let cells: Vec<CalCell> = (0..42).map(|i| {
                let d = from + Duration::days(i);
                let evs: Vec<&&Occ> = visible.iter().filter(|o| o.start.date() <= d && (o.end - Duration::seconds(1)).date() >= d).collect();
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
            u.set_cal_cells(ModelRc::new(VecModel::from(cells)));
        } else {
            // one column per day of the loaded range (three in a narrow pane)
            let mut dates: Vec<NaiveDate> = day_columns(from, to);
            if s.view == 1 && cp.hide_non_working {
                let kept: Vec<NaiveDate> = dates.iter().copied().filter(|d| cp.working.contains(&d.weekday().num_days_from_sunday())).collect();
                if !kept.is_empty() { dates = kept; }
            }
            let heads: Vec<CalCell> = dates.iter().enumerate().map(|(i, &d)| {
                let all: Vec<CalEvent> = visible.iter().filter(|o| (o.all_day || (o.end - o.start).num_hours() >= 24) && o.start.date() <= d && (o.end - Duration::seconds(1)).date() >= d)
                    .map(|o| ev(o, &s.calendars, i as i32, 0.0, 0.0, 1, cp.h12)).collect();
                CalCell { day: d.day() as i32, date: d.format("%a").to_string().into(), in_month: true, today: d == today,
                    events: ModelRc::new(VecModel::from(all)), more: "".into() }
            }).collect();
            u.set_cal_days(ModelRc::new(VecModel::from(heads)));
            // timed events with simple overlap columns
            let mut timed: Vec<CalEvent> = vec![];
            let off = cp.hour_from as f32 * HOUR_PX;
            for (i, &d) in dates.iter().enumerate() {
                let day_ev: Vec<&&Occ> = visible.iter().filter(|o| !o.all_day && (o.end - o.start).num_hours() < 24 && o.start.date() == d).collect();
                let mut lanes: Vec<NaiveDateTime> = vec![];
                let mut placed: Vec<(usize, &&Occ)> = vec![];
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
            u.set_cal_events(ModelRc::new(VecModel::from(timed)));
        }
    });
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
    }
}

// ---------------- navigation ----------------

pub fn nav(u: &AppWindow, c: Arc<Client>, how: &str) {
    NARROW.with(|n| n.set(u.get_cal_narrow()));
    ST.with(|s| {
        let mut s = s.borrow_mut();
        let a = s.anchor.unwrap_or_else(|| Local::now().date_naive());
        s.anchor = Some(match how {
            "today" => Local::now().date_naive(),
            "prev" => match s.view { 0 => a - Duration::days(1), 1 => a - Duration::days(week_days_shown()), _ => (a.with_day(1).unwrap() - Duration::days(1)).with_day(1).unwrap() },
            "next" => match s.view { 0 => a + Duration::days(1), 1 => a + Duration::days(week_days_shown()), _ => (a.with_day(28).unwrap() + Duration::days(7)).with_day(1).unwrap() },
            // the pane changed width (cal-narrow): same dates, drawn again
            "rerender" => a,
            "mini-prev" => (a.with_day(1).unwrap() - Duration::days(1)).with_day(1).unwrap(),
            "mini-next" => (a.with_day(28).unwrap() + Duration::days(7)).with_day(1).unwrap(),
            d => NaiveDate::parse_from_str(d, "%Y-%m-%d").unwrap_or(a),
        });
    });
    render(u);
    load(u.as_weak(), c);
}

pub fn set_view(u: &AppWindow, c: Arc<Client>, v: i32) {
    ST.with(|s| s.borrow_mut().view = v);
    render(u);
    load(u.as_weak(), c);
}

pub fn toggle_calendar(u: &AppWindow, id: &str) {
    ST.with(|s| { let mut s = s.borrow_mut(); if !s.hidden.remove(id) { s.hidden.insert(id.to_string()); } });
    render(u);
}

pub fn search(u: &AppWindow, q: &str) {
    ST.with(|s| s.borrow_mut().query = q.to_string());
    render(u);
}

// ---------------- editing ----------------

/// Open the event dialog: `id` empty for a new event at `date` (and optional hour).
pub fn edit(u: &AppWindow, id: &str, date: &str, hour: i32) {
    let raw = RAW.with(|r| r.borrow().iter().find(|e| e["id"] == id).cloned());
    let names: Vec<slint::SharedString> = ST.with(|s| s.borrow().calendars.iter().map(|c| c["name"].as_str().unwrap_or("Calendar").into()).collect());
    u.set_ev_calendars(ModelRc::new(VecModel::from(names)));
    match raw {
        Some(e) => {
            let start = e["start"].as_str().and_then(parse_local).unwrap_or_else(|| Local::now().naive_local());
            let dur = parse_duration(e["duration"].as_str().unwrap_or("PT1H"));
            u.set_ev_id(id.into());
            u.set_ev_title(e["title"].as_str().unwrap_or("").into());
            u.set_ev_date(start.format("%Y-%m-%d").to_string().into());
            u.set_ev_start(start.format("%H:%M").to_string().into());
            u.set_ev_end((start + dur).format("%H:%M").to_string().into());
            u.set_ev_all_day(e["showWithoutTime"] == true);
            u.set_ev_location(e["locations"].as_object().and_then(|o| o.values().next()).and_then(|l| l["name"].as_str()).unwrap_or("").into());
            u.set_ev_description(e["description"].as_str().unwrap_or("").into());
            let freq = e["recurrenceRules"][0]["frequency"].as_str().unwrap_or("");
            u.set_ev_repeat(match freq { "daily" => 1, "weekly" => 2, "monthly" => 3, "yearly" => 4, _ => 0 });
            let cal = e["calendarIds"].as_object().and_then(|o| o.keys().next().cloned()).unwrap_or_default();
            u.set_ev_calendar(ST.with(|s| s.borrow().calendars.iter().position(|c| c["id"] == cal.as_str()).unwrap_or(0) as i32));
            ST.with(|s| s.borrow_mut().editing = Some(e));
        }
        None => {
            let h = if hour >= 0 { hour } else { (Local::now().hour() as i32 + 1).min(23) };
            u.set_ev_id("".into());
            u.set_ev_title("".into());
            u.set_ev_date((if date.is_empty() { anchor().format("%Y-%m-%d").to_string() } else { date.to_string() }).into());
            u.set_ev_start(format!("{h:02}:00").into());
            u.set_ev_end(format!("{:02}:00", (h + 1).min(23)).into());
            u.set_ev_all_day(false);
            u.set_ev_location("".into());
            u.set_ev_description("".into());
            u.set_ev_repeat(0);
            u.set_ev_calendar(ST.with(|s| s.borrow().calendars.iter().position(|c| c["isDefault"] == true).unwrap_or(0) as i32));
            ST.with(|s| s.borrow_mut().editing = None);
        }
    }
    u.set_ev_status("".into());
    u.set_ev_open(true);
}

pub fn save(u: &AppWindow, c: Arc<Client>) {
    let date = NaiveDate::parse_from_str(u.get_ev_date().trim(), "%Y-%m-%d");
    let st = NaiveTime::parse_from_str(u.get_ev_start().trim(), "%H:%M");
    let en = NaiveTime::parse_from_str(u.get_ev_end().trim(), "%H:%M");
    let (Ok(date), Ok(st), Ok(en)) = (date, st, en) else { u.set_ev_status("Use YYYY-MM-DD and HH:MM.".into()); return };
    let all_day = u.get_ev_all_day();
    let start = date.and_time(if all_day { NaiveTime::MIN } else { st });
    let mins = if all_day { 24 * 60 } else { let m = (en - st).num_minutes(); if m <= 0 { m + 24 * 60 } else { m } };
    let cal_id = ST.with(|s| s.borrow().calendars.get(u.get_ev_calendar() as usize).and_then(|c| c["id"].as_str().map(String::from))).unwrap_or_default();
    let mut ev = json!({
        "@type": "Event",
        "title": u.get_ev_title().to_string(),
        "start": start.format("%Y-%m-%dT%H:%M:%S").to_string(),
        "duration": if all_day { "P1D".to_string() } else { format!("PT{}H{}M", mins / 60, mins % 60) },
        "showWithoutTime": all_day,
        "calendarIds": { cal_id.clone(): true },
        "description": u.get_ev_description().to_string(),
    });
    if !all_day { ev["timeZone"] = json!(crate::agent::local_tz()); } else { ev["timeZone"] = Value::Null; }
    let loc = u.get_ev_location().to_string();
    ev["locations"] = if loc.trim().is_empty() { Value::Null } else { json!({"l1": {"@type": "Location", "name": loc}}) };
    ev["recurrenceRules"] = match u.get_ev_repeat() {
        1 => json!([{"@type": "RecurrenceRule", "frequency": "daily"}]),
        2 => json!([{"@type": "RecurrenceRule", "frequency": "weekly"}]),
        3 => json!([{"@type": "RecurrenceRule", "frequency": "monthly"}]),
        4 => json!([{"@type": "RecurrenceRule", "frequency": "yearly"}]),
        _ => Value::Null,
    };
    let id = u.get_ev_id().to_string();
    let w = u.as_weak();
    u.set_ev_status("Saving…".into());
    std::thread::spawn(move || {
        let acc = c.account_for("urn:ietf:params:jmap:calendars");
        let res: Result<(), String> = if id.is_empty() {
            if ev["recurrenceRules"].is_null() { ev.as_object_mut().unwrap().remove("recurrenceRules"); }
            if ev["locations"].is_null() { ev.as_object_mut().unwrap().remove("locations"); }
            if ev["timeZone"].is_null() { ev.as_object_mut().unwrap().remove("timeZone"); }
            c.call(json!([["CalendarEvent/set", {"accountId": acc, "create": {"e": ev}}, "s"]]))
                .and_then(|r| if r[0][1]["created"]["e"].is_object() { Ok(()) } else { Err(crate::jmap::set_error(&r[0][1]["notCreated"]["e"])) })
        } else {
            ev.as_object_mut().unwrap().remove("@type");
            c.update("CalendarEvent/set", &acc, vec![(id, ev)]).and_then(|(_, bad)| if bad.is_empty() { Ok(()) } else { Err(bad.join("; ")) })
        };
        let w2 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| match res {
            Ok(()) => { u.set_ev_open(false); load(w2, c); }
            Err(e) => u.set_ev_status(e.into()),
        });
    });
}

pub fn delete(u: &AppWindow, c: Arc<Client>) {
    let id = u.get_ev_id().to_string();
    if id.is_empty() { u.set_ev_open(false); return; }
    let w = u.as_weak();
    std::thread::spawn(move || {
        let acc = c.account_for("urn:ietf:params:jmap:calendars");
        let res = c.call(json!([["CalendarEvent/set", {"accountId": acc, "destroy": [id]}, "d"]]));
        let w2 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| match res {
            Ok(_) => { u.set_ev_open(false); load(w2, c); }
            Err(e) => u.set_ev_status(e.into()),
        });
    });
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn day_columns_follow_range() {
        let d = NaiveDate::from_ymd_opt(2026, 11, 16).unwrap();
        assert_eq!(day_columns(d, d + Duration::days(3)).len(), 3);
        assert_eq!(day_columns(d, d + Duration::days(7)).last(), Some(&(d + Duration::days(6))));
        assert_eq!(day_columns(d, d + Duration::days(1)), vec![d]);
    }
    #[test]
    fn expands_weekly_and_count() {
        let e = json!({"start": "2026-11-03T10:00:00", "duration": "PT1H30M", "recurrenceRules": [{"frequency": "weekly", "count": 3}]});
        let f = NaiveDate::from_ymd_opt(2026, 11, 1).unwrap().and_hms_opt(0, 0, 0).unwrap();
        let t = NaiveDate::from_ymd_opt(2026, 12, 31).unwrap().and_hms_opt(0, 0, 0).unwrap();
        let occ = expand(&e, f, t);
        assert_eq!(occ.len(), 3);
        assert_eq!(occ[2].0.format("%Y-%m-%d %H:%M").to_string(), "2026-11-17 10:00");
        assert_eq!((occ[0].1 - occ[0].0).num_minutes(), 90);
        let e = json!({"start": "2026-11-02T09:00:00", "duration": "PT1H", "recurrenceRules": [{"frequency": "weekly", "byDay": [{"day": "mo"}, {"day": "we"}]}]});
        let occ = expand(&e, f, NaiveDate::from_ymd_opt(2026, 11, 15).unwrap().and_hms_opt(0, 0, 0).unwrap());
        assert_eq!(occ.len(), 4);
    }
}


// ---------------- tasks (JSCalendar Task via CalendarEvent, like Bulwark) ----------------

/// Bulwark's isTaskObject: @type Task, or task-only fields on a CalDAV VTODO.
fn is_task(e: &Value) -> bool {
    e["@type"].as_str().is_some_and(|t| t.eq_ignore_ascii_case("task"))
        || (e["@type"] != "Event" && (e["progress"].is_string() || !e["due"].is_null() || e.get("percentComplete").is_some()))
}

pub fn render_tasks(u: &AppWindow) {
    let mut tasks: Vec<Value> = RAW.with(|r| r.borrow().iter().filter(|e| is_task(e)).cloned().collect());
    tasks.sort_by_key(|t| (t["progress"] == "completed", t["due"].as_str().unwrap_or("9999").to_string()));
    let rows: Vec<crate::TaskRow> = tasks.iter().map(|t| crate::TaskRow {
        id: t["id"].as_str().unwrap_or("").into(),
        title: t["title"].as_str().filter(|x| !x.is_empty()).unwrap_or("(untitled task)").into(),
        due: t["due"].as_str().map(|d| d[..10.min(d.len())].to_string()).unwrap_or_default().into(),
        done: t["progress"] == "completed",
    }).collect();
    u.set_cal_tasks(ModelRc::new(VecModel::from(rows)));
}

pub fn add_task(u: &AppWindow, c: Arc<Client>, title: &str, due: &str) {
    let title = title.trim().to_string();
    if title.is_empty() { return; }
    let cal = ST.with(|s| { let s = s.borrow(); s.calendars.iter().find(|c| c["isDefault"] == true).or(s.calendars.first()).and_then(|c| c["id"].as_str().map(String::from)) });
    let Some(cal) = cal else { crate::mail::show_toast(u, "No calendar to add the task to.", false); return };
    let mut b = [0u8; 16];
    let _ = getrandom::getrandom(&mut b);
    let uid: String = b.iter().map(|x| format!("{x:02x}")).collect();
    let mut task = json!({"@type": "Task", "uid": uid, "title": title, "calendarIds": { cal: true }, "progress": "needs-action"});
    let due = due.trim();
    if due.len() == 10 { task["due"] = json!(format!("{due}T00:00:00")); task["timeZone"] = json!(crate::agent::local_tz()); }
    let w = u.as_weak();
    std::thread::spawn(move || {
        let acc = c.account_for("urn:ietf:params:jmap:calendars");
        let res = c.call(json!([["CalendarEvent/set", {"accountId": acc, "sendSchedulingMessages": false, "create": {"t": task}}, "s"]]))
            .and_then(|r| if r[0][1]["created"]["t"].is_object() { Ok(()) } else { Err(crate::jmap::set_error(&r[0][1]["notCreated"]["t"])) });
        let w2 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| match res {
            Ok(()) => load(w2, c),
            Err(e) => crate::mail::show_toast(&u, &format!("Task not added: {e}"), false),
        });
    });
}

pub fn toggle_task(u: &AppWindow, c: Arc<Client>, id: &str) {
    let done = RAW.with(|r| r.borrow().iter().find(|e| e["id"] == id).map(|e| e["progress"] == "completed")).unwrap_or(false);
    let progress = if done { "needs-action" } else { "completed" };
    RAW.with(|r| if let Some(e) = r.borrow_mut().iter_mut().find(|e| e["id"] == id) { e["progress"] = json!(progress); });
    render_tasks(u);
    let (w, id) = (u.as_weak(), id.to_string());
    std::thread::spawn(move || {
        let acc = c.account_for("urn:ietf:params:jmap:calendars");
        if let Err(e) = c.update("CalendarEvent/set", &acc, vec![(id, json!({"progress": progress}))]).and_then(|(_, b)| if b.is_empty() { Ok(()) } else { Err(b.join("; ")) }) {
            let _ = w.upgrade_in_event_loop(move |u| crate::mail::show_toast(&u, &format!("Task not updated: {e}"), false));
        }
    });
}

// ---------------- context-menu helpers ----------------

pub fn event_title(id: &str) -> String {
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

/// Run a CalendarEvent/Calendar set call in the background, then reload.
pub fn set_call(u: &AppWindow, c: Arc<Client>, method: &str, args: Value, what: &str) {
    let (w, method, what) = (u.as_weak(), method.to_string(), what.to_string());
    std::thread::spawn(move || {
        let acc = c.account_for("urn:ietf:params:jmap:calendars");
        let mut a = args;
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
            Err(e) => crate::mail::show_toast(&u, &format!("{what}: {e}"), false),
        });
    });
}

pub fn set_task_due(u: &AppWindow, c: Arc<Client>, id: &str, due: chrono::NaiveDate) {
    let d = format!("{}T00:00:00", due.format("%Y-%m-%d"));
    RAW.with(|r| if let Some(e) = r.borrow_mut().iter_mut().find(|e| e["id"] == id) { e["due"] = json!(d); });
    render_tasks(u);
    set_call(u, c, "CalendarEvent/set", json!({"update": {id: {"due": d, "timeZone": crate::agent::local_tz()}}}), "Task not updated");
}

pub fn delete_task(u: &AppWindow, c: Arc<Client>, id: &str) {
    RAW.with(|r| r.borrow_mut().retain(|e| e["id"] != id));
    render_tasks(u);
    set_call(u, c, "CalendarEvent/set", json!({"destroy": [id]}), "Task not deleted");
}

// ---------------- invitations & export ----------------

/// The user's own participant in an event and its answer (Bulwark getUserParticipantId):
/// matched by calendarAddress / email against the login and identities.
pub fn my_participation(id: &str, mine: &[String]) -> Option<(String, String)> {
    RAW.with(|r| {
        let r = r.borrow();
        let e = r.iter().find(|e| e["id"] == id)?;
        let ps = e["participants"].as_object()?;
        let is_me = |p: &Value| {
            let addr = p["calendarAddress"].as_str().or(p["sendTo"]["imip"].as_str()).map(|a| a.trim_start_matches("mailto:").to_string()).or(p["email"].as_str().map(String::from));
            addr.is_some_and(|a| mine.iter().any(|m| m.eq_ignore_ascii_case(&a)))
        };
        // organizers don't answer their own events
        let (pid, p) = ps.iter().find(|(_, p)| is_me(p) && p["roles"]["owner"] != true)?;
        Some((pid.clone(), p["participationStatus"].as_str().unwrap_or("needs-action").to_string()))
    })
}

/// Answer an invitation: patch the participant's participationStatus and let the server send the iTIP REPLY.
pub fn rsvp(u: &AppWindow, c: Arc<Client>, id: &str, pid: &str, status: &str) {
    let esc = pid.replace('~', "~0").replace('/', "~1");
    RAW.with(|r| if let Some(e) = r.borrow_mut().iter_mut().find(|e| e["id"] == id) { e["participants"][pid]["participationStatus"] = json!(status); });
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
            let d = parse_duration(e["duration"].as_str().unwrap_or("P1D"));
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
            l.push(format!("RRULE:{}", parts.join(";")));
        }
    }
    if let Some(ps) = e["participants"].as_object() {
        for p in ps.values() {
            let addr = p["calendarAddress"].as_str().or(p["sendTo"]["imip"].as_str()).map(String::from).or(p["email"].as_str().map(|a| format!("mailto:{a}")));
            let Some(addr) = addr else { continue };
            let addr = if addr.starts_with("mailto:") { addr } else { format!("mailto:{addr}") };
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
