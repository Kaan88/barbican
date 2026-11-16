//! JSCalendar recurrence (RFC 8984 §4.3): expansion of `recurrenceRules` on the event's own
//! wall clock, overrides and exclusions, time-zone conversion to the user's clock, rule
//! building for the custom recurrence editor and Bulwark's English summary
//! (components/calendar/recurrence-editor.tsx `buildRecurrenceSummary`, lib/recurrence-rule.ts).
use chrono::{Datelike, Duration, Local, NaiveDate, NaiveDateTime, TimeZone, Weekday};
use serde_json::{json, Value};

pub const DAYS: [&str; 7] = ["mo", "tu", "we", "th", "fr", "sa", "su"];
const DAY_NAMES: [&str; 7] = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
const DAY_LONG: [&str; 7] = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
const MONTHS: [&str; 12] = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

pub fn weekday_of(s: &str) -> Option<Weekday> {
    DAYS.iter().position(|d| d.eq_ignore_ascii_case(s)).map(|i| Weekday::try_from(i as u8).unwrap())
}

/// A LocalDateTime ("YYYY-MM-DDTHH:MM:SS", maybe with a trailing Z or fraction).
pub fn parse_local(s: &str) -> Option<NaiveDateTime> {
    let s = s.trim().trim_end_matches('Z');
    NaiveDateTime::parse_from_str(&s[..s.len().min(19)], "%Y-%m-%dT%H:%M:%S").ok()
        .or_else(|| NaiveDate::parse_from_str(&s[..s.len().min(10)], "%Y-%m-%d").ok().map(|d| d.and_hms_opt(0, 0, 0).unwrap()))
}

pub fn fmt_local(t: NaiveDateTime) -> String { t.format("%Y-%m-%dT%H:%M:%S").to_string() }

/// ISO 8601 duration (PnW / PnDTnHnMnS, optional sign) in seconds.
pub fn duration_secs(s: &str) -> Option<i64> {
    let s = s.trim();
    let (neg, s) = match s.strip_prefix('-') { Some(r) => (true, r), None => (false, s.strip_prefix('+').unwrap_or(s)) };
    let s = s.strip_prefix('P')?;
    let (mut total, mut num, mut in_time, mut any) = (0i64, String::new(), false, false);
    for ch in s.chars() {
        match ch {
            'T' => in_time = true,
            '0'..='9' => num.push(ch),
            _ => {
                let n: i64 = num.parse().ok()?;
                num.clear();
                any = true;
                total += match (ch, in_time) {
                    ('W', false) => n * 7 * 86400, ('D', false) => n * 86400, ('H', true) => n * 3600,
                    ('M', true) => n * 60, ('S', true) => n, _ => return None,
                };
            }
        }
    }
    if !num.is_empty() || !any { return None; }
    Some(if neg { -total } else { total })
}

/// A duration as JSCalendar writes it: "P1D", "PT1H30M", "P2DT3H", "PT0S".
pub fn duration_string(secs: i64) -> String {
    let secs = secs.max(0);
    let (d, h, m, s) = (secs / 86400, secs % 86400 / 3600, secs % 3600 / 60, secs % 60);
    let mut out = String::from("P");
    if d > 0 { out += &format!("{d}D"); }
    if h > 0 || m > 0 || s > 0 {
        out.push('T');
        if h > 0 { out += &format!("{h}H"); }
        if m > 0 { out += &format!("{m}M"); }
        if s > 0 { out += &format!("{s}S"); }
    }
    if out == "P" { "PT0S".into() } else { out }
}

// ---------------- time zones ----------------

pub fn tz_of(name: &str) -> Option<chrono_tz::Tz> { name.parse::<chrono_tz::Tz>().ok() }

/// A wall clock in `zone` (None or unknown: floating) as the user's local wall clock.
pub fn zone_to_local(wall: NaiveDateTime, zone: Option<&str>) -> NaiveDateTime {
    let Some(z) = zone.and_then(tz_of) else { return wall };
    let utc = match z.from_local_datetime(&wall) {
        chrono::LocalResult::Single(t) => t.naive_utc(),
        chrono::LocalResult::Ambiguous(a, _) => a.naive_utc(),
        // a time skipped by a DST change: the hour after it
        chrono::LocalResult::None => z.from_local_datetime(&(wall + Duration::hours(1))).earliest().map(|t| t.naive_utc()).unwrap_or(wall),
    };
    Local.from_utc_datetime(&utc).naive_local()
}

/// A wall clock in `zone` as a UTC instant (milliseconds); floating times are on the local clock.
pub fn zone_to_utc_ms(wall: NaiveDateTime, zone: Option<&str>) -> i64 {
    match zone.and_then(tz_of) {
        Some(z) => z.from_local_datetime(&wall).earliest().or_else(|| z.from_local_datetime(&(wall + Duration::hours(1))).earliest())
            .map(|t| t.timestamp_millis()).unwrap_or_else(|| wall.and_utc().timestamp_millis()),
        None => Local.from_local_datetime(&wall).earliest().map(|t| t.timestamp_millis()).unwrap_or_else(|| wall.and_utc().timestamp_millis()),
    }
}

/// The local wall clock as the wall clock of `zone`.
pub fn local_to_zone(local: NaiveDateTime, zone: Option<&str>) -> NaiveDateTime {
    let Some(z) = zone.and_then(tz_of) else { return local };
    match Local.from_local_datetime(&local).earliest() {
        Some(t) => t.with_timezone(&z).naive_local(),
        None => local,
    }
}

/// Every IANA zone name, for the editor's time-zone picker.
pub fn all_zones() -> Vec<&'static str> { chrono_tz::TZ_VARIANTS.iter().map(|z| z.name()).collect() }

/// Zones matching a typed query (case-insensitive, "new york" finds America/New_York).
pub fn search_zones(q: &str, max: usize) -> Vec<String> {
    let q = q.trim().to_lowercase().replace(' ', "_");
    if q.is_empty() { return vec![]; }
    let mut hits: Vec<(usize, &str)> = all_zones().into_iter().filter_map(|z| {
        let l = z.to_lowercase();
        let city = l.rsplit('/').next().unwrap_or(&l).to_string();
        if city.starts_with(&q) { Some((0, z)) } else if l.starts_with(&q) { Some((1, z)) } else if l.contains(&q) { Some((2, z)) } else { None }
    }).collect();
    hits.sort();
    hits.into_iter().take(max).map(|(_, z)| z.to_string()).collect()
}

// ---------------- rules ----------------

#[derive(Clone, Debug, PartialEq)]
pub struct Rule {
    pub freq: String,
    pub interval: i64,
    /// (weekday, nthOfPeriod)
    pub by_day: Vec<(Weekday, Option<i64>)>,
    pub by_month_day: Vec<i64>,
    pub by_month: Vec<u32>,
    pub by_set_pos: Vec<i64>,
    pub count: Option<i64>,
    pub until: Option<NaiveDateTime>,
    pub first_dow: Weekday,
}

impl Rule {
    pub fn simple(freq: &str) -> Rule {
        Rule { freq: freq.into(), interval: 1, by_day: vec![], by_month_day: vec![], by_month: vec![], by_set_pos: vec![], count: None, until: None, first_dow: Weekday::Mon }
    }

    pub fn parse(r: &Value) -> Rule {
        Rule {
            freq: r["frequency"].as_str().unwrap_or("weekly").to_lowercase(),
            interval: r["interval"].as_i64().unwrap_or(1).max(1),
            by_day: r["byDay"].as_array().map(|a| a.iter().filter_map(|d| Some((weekday_of(d["day"].as_str()?)?, d["nthOfPeriod"].as_i64().filter(|n| *n != 0)))).collect()).unwrap_or_default(),
            by_month_day: r["byMonthDay"].as_array().map(|a| a.iter().filter_map(|x| x.as_i64()).collect()).unwrap_or_default(),
            // byMonth is a list of strings ("3", "3L" for a leap month)
            by_month: r["byMonth"].as_array().map(|a| a.iter().filter_map(|x| x.as_str().map(|s| s.trim_end_matches('L').to_string()).or(x.as_u64().map(|n| n.to_string())).and_then(|s| s.parse().ok())).filter(|m| (1..=12).contains(m)).collect()).unwrap_or_default(),
            by_set_pos: r["bySetPosition"].as_array().map(|a| a.iter().filter_map(|x| x.as_i64()).collect()).unwrap_or_default(),
            count: r["count"].as_i64().filter(|c| *c > 0),
            until: r["until"].as_str().and_then(parse_local),
            first_dow: r["firstDayOfWeek"].as_str().and_then(weekday_of).unwrap_or(Weekday::Mon),
        }
    }

    /// The rule as sent to the server: only the parts in use (Bulwark createRecurrenceRule; no
    /// rscale/skip, which made Stalwart write RSCALE=GREGORIAN;SKIP=OMIT that DAVx5 rejects).
    pub fn to_json(&self) -> Value {
        let mut v = json!({"@type": "RecurrenceRule", "frequency": self.freq, "interval": self.interval});
        if !self.by_day.is_empty() {
            v["byDay"] = Value::Array(self.by_day.iter().map(|(d, n)| {
                let mut o = json!({"@type": "NDay", "day": DAYS[d.num_days_from_monday() as usize]});
                if let Some(n) = n { o["nthOfPeriod"] = json!(n); }
                o
            }).collect());
        }
        if !self.by_month_day.is_empty() { v["byMonthDay"] = json!(self.by_month_day); }
        if !self.by_month.is_empty() { v["byMonth"] = Value::Array(self.by_month.iter().map(|m| json!(m.to_string())).collect()); }
        if !self.by_set_pos.is_empty() { v["bySetPosition"] = json!(self.by_set_pos); }
        if let Some(c) = self.count { v["count"] = json!(c); }
        if let Some(u) = self.until { v["until"] = json!(fmt_local(u)); }
        v
    }

    /// What the plain Daily/Weekly/Monthly/Yearly presets write (Bulwark isSimpleRecurrenceRule).
    pub fn is_simple(&self) -> bool {
        ["daily", "weekly", "monthly", "yearly"].contains(&self.freq.as_str()) && self.interval == 1 && self.by_day.is_empty()
            && self.by_month_day.is_empty() && self.by_month.is_empty() && self.by_set_pos.is_empty() && self.count.is_none() && self.until.is_none()
    }

    /// Bulwark buildRecurrenceSummary: "Every 2 weeks on Mon, Wed · 12 occurrences".
    pub fn summary(&self) -> String {
        let n = self.interval;
        let unit = |one: &str, many: &str| if n > 1 { format!("Every {n} {many}") } else { one.to_string() };
        let mut s = match self.freq.as_str() {
            "daily" => unit("Daily", "days"),
            "weekly" => unit("Weekly", "weeks"),
            "monthly" => unit("Monthly", "months"),
            "yearly" => unit("Yearly", "years"),
            "hourly" => unit("Hourly", "hours"),
            "minutely" => unit("Every minute", "minutes"),
            f => f.to_string(),
        };
        if self.freq == "weekly" && !self.by_day.is_empty() {
            let mut d: Vec<usize> = self.by_day.iter().map(|(w, _)| w.num_days_from_monday() as usize).collect();
            d.sort();
            d.dedup();
            s += &format!(" on {}", d.iter().map(|i| DAY_NAMES[*i]).collect::<Vec<_>>().join(", "));
        }
        if self.freq == "monthly" || self.freq == "yearly" {
            if self.freq == "yearly" {
                if let Some(m) = self.by_month.first() { s += &format!(" in {}", MONTHS[(*m as usize - 1).min(11)]); }
            }
            if let Some((d, nth)) = self.nth_day() {
                s += &format!(" on the {} {}", nth_label(nth), DAY_LONG[d.num_days_from_monday() as usize]);
            } else if let Some(md) = self.by_month_day.first() {
                s += &format!(" on day {md}");
            }
        }
        if let Some(c) = self.count {
            s += &format!(" · {c} occurrence{}", if c == 1 { "" } else { "s" });
        } else if let Some(u) = self.until {
            s += &format!(" · until {}", u.format("%b %-d, %Y"));
        }
        s
    }

    /// An "nth weekday" pattern: byDay with nthOfPeriod, or one byDay with one bySetPosition.
    pub fn nth_day(&self) -> Option<(Weekday, i64)> {
        if self.by_day.len() != 1 { return None; }
        let (d, n) = self.by_day[0];
        n.map(|n| (d, n)).or_else(|| (self.by_set_pos.len() == 1).then(|| (d, self.by_set_pos[0])))
    }
}

fn nth_label(n: i64) -> String {
    match n { -1 => "last".into(), 1 => "first".into(), 2 => "second".into(), 3 => "third".into(), 4 => "fourth".into(), 5 => "fifth".into(), n => n.to_string() }
}

/// Whether two rule lists describe the same occurrences (Bulwark sameRecurrenceRules): parts left
/// out and parts at their default are the same, by-lists in any order.
pub fn same_rules(a: &Value, b: &Value) -> bool {
    let norm = |v: &Value| -> Vec<String> {
        let mut out: Vec<String> = v.as_array().map(|a| a.iter().map(|r| {
            let mut r = Rule::parse(r);
            r.by_day.sort_by_key(|(d, n)| (d.num_days_from_monday(), *n));
            r.by_month_day.sort();
            r.by_month.sort();
            r.by_set_pos.sort();
            format!("{:?}", r)
        }).collect()).unwrap_or_default();
        out.sort();
        out
    };
    norm(a) == norm(b)
}

fn days_in_month(y: i32, m: u32) -> u32 {
    let (ny, nm) = if m == 12 { (y + 1, 1) } else { (y, m + 1) };
    (NaiveDate::from_ymd_opt(ny, nm, 1).unwrap() - NaiveDate::from_ymd_opt(y, m, 1).unwrap()).num_days() as u32
}

/// Days of month `m` the rule's byDay / byMonthDay give (or the start's day when neither is set).
fn month_days(r: &Rule, y: i32, m: u32, start_day: u32) -> Vec<NaiveDate> {
    let dim = days_in_month(y, m) as i64;
    let date = |d: i64| NaiveDate::from_ymd_opt(y, m, d as u32);
    let mut out: Vec<NaiveDate> = vec![];
    if !r.by_month_day.is_empty() {
        for &md in &r.by_month_day {
            let d = if md < 0 { dim + 1 + md } else { md };
            if (1..=dim).contains(&d) { out.extend(date(d)); }
        }
        if !r.by_day.is_empty() { out.retain(|d| r.by_day.iter().any(|(w, _)| *w == d.weekday())); }
    } else if !r.by_day.is_empty() {
        for &(w, nth) in &r.by_day {
            let all: Vec<NaiveDate> = (1..=dim).filter_map(date).filter(|d| d.weekday() == w).collect();
            match nth {
                Some(n) if n > 0 => out.extend(all.get(n as usize - 1).copied()),
                Some(n) if n < 0 => out.extend(all.len().checked_sub((-n) as usize).and_then(|i| all.get(i)).copied()),
                _ => out.extend(all),
            }
        }
    } else if (start_day as i64) <= dim {
        out.extend(date(start_day as i64));
    }
    out.sort();
    out.dedup();
    out
}

fn apply_set_pos(mut v: Vec<NaiveDateTime>, pos: &[i64]) -> Vec<NaiveDateTime> {
    v.sort();
    v.dedup();
    if pos.is_empty() { return v; }
    let n = v.len() as i64;
    let mut out: Vec<NaiveDateTime> = pos.iter().filter_map(|&p| {
        let i = if p > 0 { p - 1 } else { n + p };
        (0..n).contains(&i).then(|| v[i as usize])
    }).collect();
    out.sort();
    out.dedup();
    out
}

/// Occurrence starts (wall clock of the event's zone) of one rule from `start`, up to `to`
/// (exclusive). The start itself is always the first occurrence (RFC 5545 §3.8.5.3; COUNT counts it).
pub fn rule_starts(start: NaiveDateTime, r: &Rule, to: NaiveDateTime, max: usize) -> Vec<NaiveDateTime> {
    let t = start.time();
    let mut out = vec![start];
    let mut n = 1i64;
    if r.count == Some(1) { return out; }
    let mut period = 0i64;
    let mut guard = 0usize;
    'outer: loop {
        guard += 1;
        if guard > 20_000 || out.len() >= max { break; }
        let cands: Vec<NaiveDateTime> = match r.freq.as_str() {
            "daily" => {
                let d = start.date() + Duration::days(period * r.interval);
                let ok = (r.by_day.is_empty() || r.by_day.iter().any(|(w, _)| *w == d.weekday()))
                    && (r.by_month.is_empty() || r.by_month.contains(&d.month()))
                    && (r.by_month_day.is_empty() || r.by_month_day.iter().any(|&md| md == d.day() as i64 || md == d.day() as i64 - days_in_month(d.year(), d.month()) as i64 - 1));
                if ok { vec![d.and_time(t)] } else { vec![] }
            }
            "weekly" => {
                let off = (start.date().weekday().num_days_from_monday() + 7 - r.first_dow.num_days_from_monday()) % 7;
                let ws = start.date() - Duration::days(off as i64) + Duration::weeks(period * r.interval);
                let days: Vec<Weekday> = if r.by_day.is_empty() { vec![start.weekday()] } else { r.by_day.iter().map(|(w, _)| *w).collect() };
                let v: Vec<NaiveDateTime> = (0..7).map(|i| ws + Duration::days(i)).filter(|d| days.contains(&d.weekday()))
                    .filter(|d| r.by_month.is_empty() || r.by_month.contains(&d.month())).map(|d| d.and_time(t)).collect();
                apply_set_pos(v, &r.by_set_pos)
            }
            "monthly" => {
                let m0 = start.month0() as i64 + period * r.interval;
                let (y, m) = (start.year() + m0.div_euclid(12) as i32, (m0.rem_euclid(12) + 1) as u32);
                if !r.by_month.is_empty() && !r.by_month.contains(&m) { vec![] } else {
                    apply_set_pos(month_days(r, y, m, start.day()).into_iter().map(|d| d.and_time(t)).collect(), &r.by_set_pos)
                }
            }
            "yearly" => {
                let y = start.year() + (period * r.interval) as i32;
                let months: Vec<u32> = if r.by_month.is_empty() { vec![start.month()] } else { r.by_month.clone() };
                let mut v: Vec<NaiveDateTime> = vec![];
                if r.by_month.is_empty() && r.by_month_day.is_empty() && r.by_day.iter().any(|(_, n)| n.is_some()) {
                    // nth weekday of the year
                    let all: Vec<NaiveDate> = NaiveDate::from_ymd_opt(y, 1, 1).unwrap().iter_days().take_while(|d| d.year() == y).collect();
                    for &(w, nth) in &r.by_day {
                        let ds: Vec<&NaiveDate> = all.iter().filter(|d| d.weekday() == w).collect();
                        if let Some(n) = nth {
                            let i = if n > 0 { n - 1 } else { ds.len() as i64 + n };
                            if let Some(d) = (i >= 0).then(|| ds.get(i as usize)).flatten() { v.push(d.and_time(t)); }
                        }
                    }
                } else {
                    for m in months { v.extend(month_days(r, y, m, start.day()).into_iter().map(|d| d.and_time(t))); }
                }
                apply_set_pos(v, &r.by_set_pos)
            }
            "hourly" => vec![start + Duration::hours(period * r.interval)],
            "minutely" => vec![start + Duration::minutes(period * r.interval)],
            _ => break,
        };
        // a period that starts after `to` ends the expansion
        let period_past = cands.first().is_some_and(|c| *c >= to);
        for c in cands {
            if c <= start { continue; }
            if r.until.is_some_and(|u| c > u) || c >= to { break 'outer; }
            if r.count.is_some_and(|k| n >= k) { break 'outer; }
            n += 1;
            out.push(c);
        }
        if period_past || r.count.is_some_and(|k| n >= k) { break; }
        // runaway: no candidate for very long (a rule that never matches)
        if period > 50_000 { break; }
        period += 1;
    }
    out
}

/// One occurrence of an event: its start and end on the event's own clock, its recurrence id
/// (None for a single event) and the override that changes it, if any.
#[derive(Clone, Debug)]
pub struct Occurrence {
    pub start: NaiveDateTime,
    pub end: NaiveDateTime,
    pub rid: Option<String>,
    pub patch: Option<Value>,
}

/// The event's duration, an all-day event at least one day.
pub fn event_duration(e: &Value) -> Duration {
    let secs = duration_secs(e["duration"].as_str().unwrap_or("")).unwrap_or(if e["showWithoutTime"] == true { 86400 } else { 3600 });
    let secs = if secs <= 0 && e["showWithoutTime"] == true { 86400 } else { secs.max(0) };
    Duration::seconds(secs)
}

/// The occurrences of `e` whose wall-clock span overlaps [from, to) (wall clock of the event's
/// zone). Excluded overrides are left out, overrides that move an occurrence are applied and
/// override dates no rule gives are added.
pub fn occurrences(e: &Value, from: NaiveDateTime, to: NaiveDateTime) -> Vec<Occurrence> {
    let Some(start) = e["start"].as_str().and_then(parse_local) else { return vec![] };
    let dur = event_duration(e);
    let rules: Vec<Rule> = e["recurrenceRules"].as_array().map(|a| a.iter().map(Rule::parse).collect()).unwrap_or_default();
    let overrides = e["recurrenceOverrides"].as_object();
    if rules.is_empty() && overrides.is_none_or(|o| o.is_empty()) {
        return if start < to && start + dur > from { vec![Occurrence { start, end: start + dur, rid: None, patch: None }] } else { vec![] };
    }
    let mut starts: Vec<NaiveDateTime> = vec![];
    for r in &rules { starts.extend(rule_starts(start, r, to, 100_000)); }
    if rules.is_empty() { starts.push(start); }
    // excluded rules (RFC 8984 excludedRecurrenceRules)
    let excl: Vec<NaiveDateTime> = e["excludedRecurrenceRules"].as_array().map(|a| a.iter().flat_map(|r| rule_starts(start, &Rule::parse(r), to, 100_000)).collect()).unwrap_or_default();
    starts.retain(|s| !excl.contains(s));
    let mut keyed: Vec<(NaiveDateTime, Option<Value>)> = vec![];
    let ovr: Vec<(NaiveDateTime, &Value)> = overrides.map(|o| o.iter().filter_map(|(k, v)| parse_local(k).map(|t| (t, v))).collect()).unwrap_or_default();
    starts.sort();
    starts.dedup();
    for s in starts {
        let patch = ovr.iter().find(|(k, _)| *k == s).map(|(_, v)| (*v).clone());
        keyed.push((s, patch));
    }
    // RDATE-like override keys no rule gives
    for (k, v) in &ovr {
        if !keyed.iter().any(|(s, _)| s == k) && *k < to { keyed.push((*k, Some((*v).clone()))); }
    }
    let mut out = vec![];
    for (rid, patch) in keyed {
        if patch.as_ref().is_some_and(|p| p["excluded"] == true) { continue; }
        let s = patch.as_ref().and_then(|p| p["start"].as_str()).and_then(parse_local).unwrap_or(rid);
        let d = patch.as_ref().and_then(|p| p["duration"].as_str()).and_then(duration_secs).map(Duration::seconds).unwrap_or(dur);
        if s < to && s + d > from {
            out.push(Occurrence { start: s, end: s + d, rid: Some(fmt_local(rid)), patch });
        }
    }
    out.sort_by_key(|o| o.start);
    out
}

/// How many occurrences of `rule` from `series_start` come before `split` (Bulwark
/// occurrencesBefore), for the COUNT left to a series split off at `split`.
pub fn occurrences_before(series_start: NaiveDateTime, rule: &Rule, split: NaiveDateTime) -> i64 {
    let mut r = rule.clone();
    r.count = None;
    rule_starts(series_start, &r, split, 1_000_000).len() as i64
}

#[cfg(test)]
mod tests {
    use super::*;
    fn dt(s: &str) -> NaiveDateTime { parse_local(s).unwrap() }

    #[test]
    fn durations_round_trip() {
        assert_eq!(duration_secs("PT1H30M"), Some(5400));
        assert_eq!(duration_secs("P1D"), Some(86400));
        assert_eq!(duration_secs("-PT15M"), Some(-900));
        assert_eq!(duration_secs("P1W"), Some(604800));
        assert_eq!(duration_secs("P1DT2H"), Some(93600));
        assert_eq!(duration_secs("PT"), None);
        assert_eq!(duration_secs("bogus"), None);
        assert_eq!(duration_string(5400), "PT1H30M");
        assert_eq!(duration_string(86400 * 2), "P2D");
        assert_eq!(duration_string(0), "PT0S");
        assert_eq!(duration_string(93600), "P1DT2H");
    }

    #[test]
    fn weekly_by_day_interval_count() {
        let r = Rule::parse(&json!({"frequency": "weekly", "interval": 2, "byDay": [{"day": "mo"}, {"day": "we"}], "count": 5}));
        let v = rule_starts(dt("2026-11-02T09:00:00"), &r, dt("2027-12-01T00:00:00"), 100);
        let s: Vec<String> = v.iter().map(|d| d.format("%m-%d").to_string()).collect();
        assert_eq!(s, ["11-02", "11-04", "11-16", "11-18", "11-30"]);
    }

    #[test]
    fn until_is_inclusive_and_start_counts() {
        let r = Rule::parse(&json!({"frequency": "daily", "until": "2026-11-05T09:00:00"}));
        assert_eq!(rule_starts(dt("2026-11-01T09:00:00"), &r, dt("2027-01-01T00:00:00"), 100).len(), 5);
        let r = Rule::parse(&json!({"frequency": "daily", "count": 1}));
        assert_eq!(rule_starts(dt("2026-11-01T09:00:00"), &r, dt("2027-01-01T00:00:00"), 100).len(), 1);
    }

    #[test]
    fn monthly_nth_and_last_weekday() {
        let r = Rule::parse(&json!({"frequency": "monthly", "byDay": [{"day": "th", "nthOfPeriod": 3}], "count": 3}));
        let v = rule_starts(dt("2026-11-19T10:00:00"), &r, dt("2028-01-01T00:00:00"), 100);
        let s: Vec<String> = v.iter().map(|d| d.format("%Y-%m-%d").to_string()).collect();
        assert_eq!(s, ["2026-11-19", "2026-12-17", "2027-01-21"]);
        let r = Rule::parse(&json!({"frequency": "monthly", "byDay": [{"day": "fr", "nthOfPeriod": -1}], "count": 2}));
        let v = rule_starts(dt("2026-11-27T10:00:00"), &r, dt("2028-01-01T00:00:00"), 100);
        assert_eq!(v[1].format("%Y-%m-%d").to_string(), "2026-12-25");
        // bySetPosition spelling of the same pattern
        let r = Rule::parse(&json!({"frequency": "monthly", "byDay": [{"day": "fr"}], "bySetPosition": [-1], "count": 2}));
        assert_eq!(rule_starts(dt("2026-11-27T10:00:00"), &r, dt("2028-01-01T00:00:00"), 100)[1].format("%Y-%m-%d").to_string(), "2026-12-25");
    }

    #[test]
    fn monthly_day_31_skips_short_months() {
        let r = Rule::parse(&json!({"frequency": "monthly", "count": 3}));
        let v = rule_starts(dt("2027-01-31T08:00:00"), &r, dt("2028-01-01T00:00:00"), 100);
        let s: Vec<String> = v.iter().map(|d| d.format("%m-%d").to_string()).collect();
        assert_eq!(s, ["01-31", "03-31", "05-31"]);
    }

    #[test]
    fn yearly_in_month_on_day() {
        let r = Rule::parse(&json!({"frequency": "yearly", "byMonth": ["3"], "byMonthDay": [15]}));
        let v = rule_starts(dt("2026-03-15T00:00:00"), &r, dt("2029-01-01T00:00:00"), 100);
        assert_eq!(v.len(), 3);
        assert_eq!(v[2].format("%Y-%m-%d").to_string(), "2028-03-15");
    }

    #[test]
    fn overrides_exclusions_and_moves() {
        let e = json!({"start": "2026-11-02T09:00:00", "duration": "PT1H", "recurrenceRules": [{"frequency": "daily", "count": 4}],
            "recurrenceOverrides": {"2026-11-03T09:00:00": {"excluded": true}, "2026-11-04T09:00:00": {"start": "2026-11-04T15:00:00", "title": "Moved"}}});
        let o = occurrences(&e, dt("2026-11-01T00:00:00"), dt("2026-12-01T00:00:00"));
        assert_eq!(o.len(), 3);
        assert_eq!(o[1].start, dt("2026-11-04T15:00:00"));
        assert_eq!(o[1].rid.as_deref(), Some("2026-11-04T09:00:00"));
        assert_eq!(o[1].patch.as_ref().unwrap()["title"], "Moved");
        // a single event in range, and out of it
        let s = json!({"start": "2026-11-02T09:00:00", "duration": "PT1H"});
        assert_eq!(occurrences(&s, dt("2026-11-02T00:00:00"), dt("2026-11-03T00:00:00")).len(), 1);
        assert!(occurrences(&s, dt("2026-11-03T00:00:00"), dt("2026-11-04T00:00:00")).is_empty());
    }

    #[test]
    fn summaries() {
        assert_eq!(Rule::parse(&json!({"frequency": "weekly", "interval": 2, "byDay": [{"day": "we"}, {"day": "mo"}], "count": 12})).summary(), "Every 2 weeks on Mon, Wed · 12 occurrences");
        assert_eq!(Rule::parse(&json!({"frequency": "monthly", "byDay": [{"day": "th", "nthOfPeriod": 3}]})).summary(), "Monthly on the third Thursday");
        assert_eq!(Rule::parse(&json!({"frequency": "yearly", "byMonth": ["3"], "byMonthDay": [15], "until": "2030-03-15T23:59:59"})).summary(), "Yearly in March on day 15 · until Mar 15, 2030");
        assert!(Rule::parse(&json!({"frequency": "daily"})).is_simple());
        assert!(!Rule::parse(&json!({"frequency": "daily", "count": 3})).is_simple());
    }

    #[test]
    fn rule_json_round_trip_and_sameness() {
        let r = Rule::parse(&json!({"frequency": "weekly", "byDay": [{"day": "mo"}, {"day": "fr"}], "until": "2027-01-01T23:59:59"}));
        assert_eq!(Rule::parse(&r.to_json()), r);
        assert!(same_rules(&json!([{"frequency": "weekly", "interval": 1, "byDay": [{"day": "fr"}, {"day": "mo"}]}]),
            &json!([{"@type": "RecurrenceRule", "frequency": "weekly", "byDay": [{"day": "mo"}, {"day": "fr"}], "firstDayOfWeek": "mo"}])));
        assert!(!same_rules(&json!([{"frequency": "weekly"}]), &json!([{"frequency": "daily"}])));
    }

    #[test]
    fn count_left_after_split() {
        let r = Rule::parse(&json!({"frequency": "daily", "count": 10}));
        assert_eq!(occurrences_before(dt("2026-11-01T09:00:00"), &r, dt("2026-11-04T09:00:00")), 3);
    }

    #[test]
    fn zones_convert_and_search() {
        let wall = dt("2026-07-01T12:00:00");
        // UTC noon is 12:00 UTC whatever the local zone; round trip local -> zone -> local
        let utc = zone_to_utc_ms(wall, Some("UTC"));
        assert_eq!(utc, wall.and_utc().timestamp_millis());
        let local = zone_to_local(wall, Some("America/New_York"));
        assert_eq!(local_to_zone(local, Some("America/New_York")), wall);
        assert_eq!(zone_to_local(wall, None), wall);
        assert!(search_zones("new york", 5).contains(&"America/New_York".to_string()));
        assert_eq!(search_zones("berlin", 5)[0], "Europe/Berlin");
    }
}
