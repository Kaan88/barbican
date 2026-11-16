//! The event editor (Bulwark components/calendar/event-modal.tsx and its parts): title, start
//! and end with an end date, all day, time zone, repeat with the custom recurrence editor
//! (recurrence-editor.tsx), calendar, show as (free/busy), visibility, location, guests with
//! suggestions from contacts and their free/busy (participant-input.tsx,
//! participant-availability.tsx), "Suggest times" (find-a-time.tsx, Principal/getAvailability),
//! reminders (alert-list-editor.tsx), description. Saving an occurrence of a series asks which
//! part of it (recurrence-scope-dialog.tsx, lib/recurrence-scope-edit.ts). Also the quick
//! create popover (quick-create-popover.tsx).
use crate::calendar::{self, server_ref};
use crate::jmap::Client;
use crate::recurrence::{self, duration_secs, duration_string, fmt_local, parse_local, Rule};
use crate::reminders::{self, EdRow, Row};
use crate::{AppWindow, CxAlert, CxAttendee, CxSlot, CxSuggest};
use chrono::{Datelike, Duration, Local, NaiveDate, NaiveDateTime, NaiveTime, TimeZone, Timelike, Weekday};
use serde_json::{json, Map, Value};
use slint::{ComponentHandle, ModelRc, VecModel};
use std::cell::RefCell;
use std::collections::HashMap;
use std::sync::Arc;

#[derive(Clone, Debug, Default, PartialEq)]
pub struct Person { pub name: String, pub email: String }

/// What a "this / following / all" choice will save or delete.
#[derive(Clone)]
enum Pending { Save { data: Map<String, Value>, send: bool }, Delete { send: bool } }

#[derive(Default)]
struct Ed {
    event: Option<Value>,
    rid: Option<String>,
    /// the occurrence opened, on the event's own clock
    occ_start: Option<NaiveDateTime>,
    occ_end: Option<NaiveDateTime>,
    attendees: Vec<Person>,
    organizer: Option<Person>,
    is_organizer: bool,
    rows: Vec<EdRow>,
    kept: Map<String, Value>,
    custom: Option<Rule>,
    repeat_before: i32,
    tz: Option<String>,
    /// start and end as last parsed, so a moved start moves the end along
    prev_start: Option<NaiveDateTime>,
    prev_end: Option<NaiveDateTime>,
    suggest: Vec<(Person, Vec<Person>)>,
    slots: Vec<(NaiveDateTime, NaiveDateTime)>,
    pending: Option<Pending>,
    readonly: bool,
    avail: HashMap<String, (String, i32)>,
    gen: u64,
    calendar_ids: Vec<String>,
}

#[derive(Default)]
struct Quick { start: Option<NaiveDateTime>, end: Option<NaiveDateTime>, all_day: bool, calendars: Vec<String> }

thread_local! {
    static ED: RefCell<Ed> = RefCell::new(Ed::default());
    static QC: RefCell<Quick> = RefCell::new(Quick::default());
}

const EMAIL_OK: fn(&str) -> bool = |s: &str| {
    let s = s.trim();
    let Some((a, d)) = s.split_once('@') else { return false };
    !a.is_empty() && d.contains('.') && !d.starts_with('.') && !d.ends_with('.') && !s.contains(char::is_whitespace) && s.matches('@').count() == 1
};

pub fn initials(name: &str, email: &str) -> String {
    let src = if name.trim().is_empty() { email.split('@').next().unwrap_or("") } else { name };
    let parts: Vec<&str> = src.split(|c: char| c.is_whitespace() || c == '.' || c == '_' || c == '-').filter(|p| !p.is_empty()).collect();
    let s: String = match parts.len() { 0 => "?".into(), 1 => parts[0].chars().take(2).collect(), _ => parts.iter().take(2).filter_map(|p| p.chars().next()).collect() };
    s.to_uppercase()
}

/// The user's addresses: the login first (the organizer of new invitations), then identities.
pub fn me(c: &Client) -> Vec<String> {
    let mut v = vec![];
    if c.username.contains('@') { v.push(c.username.clone()); }
    for a in crate::my_addrs_cached() { if !v.iter().any(|x: &String| x.eq_ignore_ascii_case(&a)) { v.push(a); } }
    if v.is_empty() { v.push(c.username.clone()); }
    v
}

fn is_me(email: &str, me: &[String]) -> bool { me.iter().any(|m| m.eq_ignore_ascii_case(email.trim())) }

// ---------------- participants (Bulwark lib/calendar-participants.ts) ----------------

/// The event's own organizer: the owner participant, else organizerCalendarAddress / replyTo.
pub fn event_organizer(e: &Value) -> Option<Person> {
    if let Some(ps) = e["participants"].as_object() {
        let org_addr = e["organizerCalendarAddress"].as_str().map(|a| a.trim_start_matches("mailto:").to_lowercase());
        for p in ps.values() {
            let email = calendar::participant_email(p);
            if email.is_empty() { continue; }
            if p["roles"]["owner"] == true || org_addr.as_deref() == Some(email.to_lowercase().as_str()) {
                return Some(Person { name: p["name"].as_str().unwrap_or("").to_string(), email });
            }
        }
    }
    let addr = e["organizerCalendarAddress"].as_str().or(e["replyTo"]["imip"].as_str())?;
    let email = addr.trim().trim_start_matches("mailto:").to_string();
    (!email.is_empty()).then_some(Person { name: String::new(), email })
}

/// Whether the user writes the event as its organizer (Bulwark userOrganizes).
pub fn user_organizes(e: &Value, me: &[String]) -> bool {
    match event_organizer(e) { None => true, Some(o) => is_me(&o.email, me) || e["isOrigin"] == true }
}

/// The attendees as the editor lists them: each address once, without the organizer.
pub fn attendees_of(e: &Value) -> Vec<Person> {
    let org = event_organizer(e).map(|o| o.email.to_lowercase());
    let mut seen: Vec<String> = org.iter().cloned().collect();
    let mut out = vec![];
    if let Some(ps) = e["participants"].as_object() {
        for p in ps.values() {
            let email = calendar::participant_email(p);
            let key = email.to_lowercase();
            if key.is_empty() || seen.contains(&key) || p["roles"]["owner"] == true { continue; }
            seen.push(key);
            out.push(Person { name: p["name"].as_str().unwrap_or("").to_string(), email });
        }
    }
    out
}

/// The participants a save writes (Bulwark buildParticipantMap): the organizer, then each
/// attendee once. Known addresses keep their participant id and answer, unless the user as the
/// organizer moved the event (`ask_again`): then attendees answer anew, but the user's own stays.
pub fn build_participant_map(organizer: &Person, attendees: &[Person], existing: Option<&Value>, ask_again: bool, me: &[String]) -> Map<String, Value> {
    let mut known: HashMap<String, (String, Option<String>)> = HashMap::new();
    if let Some(ps) = existing.and_then(|v| v.as_object()) {
        for (id, p) in ps {
            let key = calendar::participant_email(p).to_lowercase();
            if key.is_empty() { continue; }
            let st = p["participationStatus"].as_str().map(String::from);
            match known.get_mut(&key) {
                None => { known.insert(key, (id.clone(), st)); }
                Some(seen) => if seen.1.as_deref().is_none_or(|s| s == "needs-action") && st.is_some() { seen.1 = st; },
            }
        }
    }
    let mut out = Map::new();
    let id_for = |key: &str, out: &Map<String, Value>| -> String {
        match known.get(key).map(|k| k.0.clone()) { Some(id) if !out.contains_key(&id) => id, _ => calendar::new_uid() }
    };
    let named = |p: &mut Value, n: &str| if !n.trim().is_empty() { p["name"] = json!(n.trim()); };
    let ok = organizer.email.trim().to_lowercase();
    let ostatus = known.get(&ok).and_then(|k| k.1.clone()).filter(|s| s != "needs-action").unwrap_or_else(|| "accepted".into());
    let oid = id_for(&ok, &out);
    let mut o = json!({"@type": "Participant", "email": organizer.email.trim(), "calendarAddress": format!("mailto:{}", organizer.email.trim()),
        "roles": {"owner": true}, "participationStatus": ostatus, "scheduleAgent": "server", "expectReply": false, "kind": "individual"});
    named(&mut o, &organizer.name);
    out.insert(oid, o);
    let mut seen = vec![ok];
    for a in attendees {
        let email = a.email.trim();
        let key = email.to_lowercase();
        if key.is_empty() || seen.contains(&key) { continue; }
        seen.push(key.clone());
        let status = if ask_again && !is_me(email, me) { None } else { known.get(&key).and_then(|k| k.1.clone()) }.unwrap_or_else(|| "needs-action".into());
        let id = id_for(&key, &out);
        let mut p = json!({"@type": "Participant", "email": email, "calendarAddress": format!("mailto:{email}"), "roles": {"attendee": true},
            "participationStatus": status, "scheduleAgent": "server", "expectReply": true, "kind": "individual"});
        named(&mut p, &a.name);
        out.insert(id, p);
    }
    out
}

/// The participant fields of a save (Bulwark participantFieldsToSave).
pub fn participant_fields(event: Option<&Value>, attendees: &[Person], me: &[String], rescheduled: bool) -> Map<String, Value> {
    let mut f = Map::new();
    if !attendees.is_empty() && !me.is_empty() {
        let organizer = event.and_then(event_organizer).unwrap_or(Person { name: String::new(), email: me[0].clone() });
        let ask = rescheduled && event.is_none_or(|e| user_organizes(e, me));
        f.insert("participants".into(), Value::Object(build_participant_map(&organizer, attendees, event.map(|e| &e["participants"]), ask, me)));
        if event.is_none_or(|e| !e["organizerCalendarAddress"].is_string()) {
            f.insert("organizerCalendarAddress".into(), json!(format!("mailto:{}", organizer.email)));
        }
    } else if attendees.is_empty() && event.is_some_and(|e| e["participants"].as_object().is_some_and(|p| !p.is_empty())) {
        let e = event.unwrap();
        f.insert("participants".into(), Value::Null);
        if !e["replyTo"].is_null() && e.get("replyTo").is_some() { f.insert("replyTo".into(), Value::Null); }
        if e["organizerCalendarAddress"].is_string() { f.insert("organizerCalendarAddress".into(), Value::Null); }
    }
    f
}

/// Participants with every attendee asked to answer again (Bulwark participantsAskedAgain).
pub fn asked_again(participants: &Value, me: &[String]) -> Value {
    let Some(ps) = participants.as_object() else { return participants.clone() };
    let mut out = Map::new();
    for (id, p) in ps {
        let mut p = p.clone();
        let email = calendar::participant_email(&p);
        if p["roles"]["owner"] != true && !is_me(&email, me) && p["participationStatus"].as_str().is_some_and(|s| s != "needs-action") {
            p["participationStatus"] = json!("needs-action");
        }
        out.insert(id.clone(), p);
    }
    Value::Object(out)
}

// ---------------- the form ----------------

/// What the editor holds, as plain values (for building the save).
#[derive(Clone, Debug)]
pub struct Form {
    pub title: String,
    pub description: String,
    pub location: String,
    pub start: NaiveDateTime,
    /// all day: the last day; timed: the end, both on the form's clock
    pub end: NaiveDateTime,
    pub all_day: bool,
    pub tz: Option<String>,
    pub calendar: String,
    pub repeat: i32,
    pub custom: Option<Rule>,
    pub use_default_alerts: bool,
    pub default_alerts: Option<Value>,
    pub rows: Vec<EdRow>,
    pub kept: Map<String, Value>,
    pub reserved_alert_ids: Vec<String>,
    pub attendees: Vec<Person>,
    pub send: bool,
    pub organizer: bool,
    pub freebusy: i32,
    pub privacy: Option<i32>,
    pub me: Vec<String>,
}

const PRIVACY: [&str; 3] = ["public", "private", "secret"];

/// The repeat rules the form gives.
pub fn form_rules(f: &Form) -> Value {
    match f.repeat {
        1 => json!([Rule::simple("daily").to_json()]),
        2 => json!([Rule::simple("weekly").to_json()]),
        3 => json!([Rule::simple("monthly").to_json()]),
        4 => json!([Rule::simple("yearly").to_json()]),
        5 => f.custom.as_ref().map(|r| json!([r.to_json()])).unwrap_or(Value::Null),
        _ => Value::Null,
    }
}

/// The event data a save writes (Bulwark EventModal handleSave): every field the editor shows,
/// and only the status fields the user changed on an existing event. Returns the data and
/// whether scheduling messages go out.
pub fn build_data(f: &Form, event: Option<&Value>, occ: Option<(NaiveDateTime, NaiveDateTime)>) -> (Map<String, Value>, bool) {
    let mut d = Map::new();
    d.insert("title".into(), json!(f.title.trim()));
    let start = if f.all_day { f.start.date().and_hms_opt(0, 0, 0).unwrap() } else { f.start };
    d.insert("start".into(), json!(fmt_local(start)));
    let duration = if f.all_day {
        let last = if f.end.date() < f.start.date() { f.start.date() } else { f.end.date() };
        format!("P{}D", (last - f.start.date()).num_days() + 1)
    } else {
        let lasted_no_time = event.is_some_and(|e| e["showWithoutTime"] != true && duration_secs(e["duration"].as_str().unwrap_or("PT1H")) == Some(0));
        let mut end = f.end;
        if end < start || (end == start && !lasted_no_time) { end = start + Duration::hours(1); }
        let ms = recurrence::zone_to_utc_ms(end, f.tz.as_deref()) - recurrence::zone_to_utc_ms(start, f.tz.as_deref());
        duration_string(ms.max(0) / 1000)
    };
    d.insert("duration".into(), json!(duration));
    d.insert("timeZone".into(), if f.all_day { Value::Null } else { f.tz.as_ref().map(|t| json!(t)).unwrap_or(Value::Null) });
    d.insert("showWithoutTime".into(), json!(f.all_day));
    d.insert("calendarIds".into(), json!({ f.calendar.clone(): true }));
    // status, free/busy, privacy (Bulwark savedStatusFields)
    let fb = if f.freebusy == 1 { "free" } else { "busy" };
    match event {
        None => {
            d.insert("status".into(), json!("confirmed"));
            d.insert("freeBusyStatus".into(), json!(fb));
            d.insert("privacy".into(), json!(f.privacy.map(|p| PRIVACY[p.clamp(0, 2) as usize]).unwrap_or("public")));
            d.insert("uid".into(), json!(calendar::new_uid()));
        }
        Some(e) => {
            if fb != e["freeBusyStatus"].as_str().unwrap_or("busy") { d.insert("freeBusyStatus".into(), json!(fb)); }
            if let Some(p) = f.privacy.map(|p| PRIVACY[p.clamp(0, 2) as usize]) {
                if p != e["privacy"].as_str().unwrap_or("public") { d.insert("privacy".into(), json!(p)); }
            }
        }
    }
    let desc = f.description.trim();
    match event {
        None if !desc.is_empty() => { d.insert("description".into(), json!(desc)); }
        Some(e) if desc != description_text(e).trim() => {
            d.insert("description".into(), json!(desc));
            if e["descriptionContentType"].is_string() { d.insert("descriptionContentType".into(), json!("text/plain")); }
        }
        _ => {}
    }
    // where: a link is a virtual location
    let loc = f.location.trim();
    let had_loc = event.is_some_and(|e| e["locations"].as_object().is_some_and(|o| !o.is_empty()));
    let had_virtual = event.is_some_and(|e| e["virtualLocations"].as_object().is_some_and(|o| !o.is_empty()));
    let unchanged = event.is_some_and(|e| calendar::location_of(e) == loc);
    if !unchanged {
        if loc.starts_with("http://") || loc.starts_with("https://") {
            d.insert("virtualLocations".into(), json!({"vl1": {"@type": "VirtualLocation", "uri": loc}}));
            if had_loc { d.insert("locations".into(), Value::Null); }
        } else if !loc.is_empty() {
            // the place keeps its id (and any coordinates) when it had one
            let (lid, mut l) = event.and_then(|e| e["locations"].as_object()).and_then(|o| o.iter().next().map(|(k, v)| (k.clone(), v.clone())))
                .unwrap_or_else(|| ("l1".into(), json!({"@type": "Location"})));
            l["name"] = json!(loc);
            d.insert("locations".into(), json!({ lid: l }));
            if had_virtual { d.insert("virtualLocations".into(), Value::Null); }
        } else {
            if had_loc { d.insert("locations".into(), Value::Null); }
            if had_virtual { d.insert("virtualLocations".into(), Value::Null); }
        }
    }
    // repeat
    let rules = form_rules(f);
    if !rules.is_null() {
        d.insert("recurrenceRules".into(), rules.clone());
    } else if let Some(e) = event.filter(|e| e["recurrenceRules"].as_array().is_some_and(|a| !a.is_empty())) {
        d.insert("recurrenceRules".into(), Value::Null);
        if e["recurrenceOverrides"].is_object() { d.insert("recurrenceOverrides".into(), Value::Null); }
        if e["excludedRecurrenceRules"].is_array() { d.insert("excludedRecurrenceRules".into(), Value::Null); }
    }
    // reminders
    if f.use_default_alerts {
        d.insert("useDefaultAlerts".into(), json!(true));
        if event.is_some() { d.insert("alerts".into(), f.default_alerts.clone().unwrap_or(Value::Null)); }
    } else {
        d.extend(reminders::build_alerts_patch(event, &f.rows, &f.kept, &f.reserved_alert_ids));
    }
    // guests: only the organizer invites
    let rule_changed = event.is_some_and(|e| !recurrence::same_rules(&e["recurrenceRules"], &rules));
    let moved = match (event, occ) {
        (Some(e), Some((os, oe))) => {
            let new_dur = duration_secs(&duration).unwrap_or(0);
            os != start || (oe - os).num_seconds() != new_dur || (e["showWithoutTime"] == true) != f.all_day
        }
        (Some(e), None) => e["start"].as_str().and_then(parse_local) != Some(start) || e["duration"].as_str() != Some(duration.as_str()) || (e["showWithoutTime"] == true) != f.all_day,
        _ => false,
    };
    let tz_changed = event.is_some_and(|e| !f.all_day && e["timeZone"].as_str() != f.tz.as_deref());
    if f.organizer {
        d.extend(participant_fields(event, &f.attendees, &f.me, moved || tz_changed || rule_changed));
    }
    let send = f.organizer && !f.attendees.is_empty() && f.send;
    (d, send)
}

/// An event's description as the editor shows it (HTML as its text).
pub fn description_text(e: &Value) -> String {
    let d = e["description"].as_str().unwrap_or("");
    if e["descriptionContentType"].as_str().is_some_and(|t| t.contains("html")) || (d.contains("</") && d.contains('>')) {
        let mut out = String::new();
        let mut tag = false;
        for ch in d.replace("<br>", "\n").replace("<br/>", "\n").replace("</p>", "\n").chars() {
            match ch { '<' => tag = true, '>' => tag = false, c if !tag => out.push(c), _ => {} }
        }
        out.replace("&nbsp;", " ").replace("&amp;", "&").replace("&lt;", "<").replace("&gt;", ">").replace("&quot;", "\"").trim().to_string()
    } else { d.to_string() }
}

// ---------------- series edits (Bulwark lib/recurrence-scope-edit.ts) ----------------

const OVERRIDE_KEYS: [&str; 7] = ["title", "description", "locations", "virtualLocations", "freeBusyStatus", "participants", "alerts"];

/// The override one occurrence gets: what the editor changed against the series, its new start
/// and length on the series' clock, merged into an override it already had.
pub fn occurrence_override(master: &Value, rid: &str, data: &Map<String, Value>, new_start: NaiveDateTime) -> Value {
    let mut o = master["recurrenceOverrides"][rid].as_object().cloned().unwrap_or_default();
    for k in OVERRIDE_KEYS {
        if let Some(v) = data.get(k) {
            if master.get(k) != Some(v) && !(v.is_null() && master.get(k).is_none_or(|x| x.is_null())) { o.insert(k.into(), v.clone()); }
        }
    }
    let rid_t = parse_local(rid);
    if Some(new_start) != rid_t { o.insert("start".into(), json!(fmt_local(new_start))); } else { o.remove("start"); }
    if let Some(dur) = data.get("duration") {
        if master.get("duration") != Some(dur) { o.insert("duration".into(), dur.clone()); } else { o.remove("duration"); }
    }
    if let Some(sw) = data.get("showWithoutTime") {
        if (master["showWithoutTime"] == true) != (*sw == true) { o.insert("showWithoutTime".into(), sw.clone()); }
    }
    o.remove("excluded");
    Value::Object(o)
}

/// The update that writes one override (a pointer into the map, or the whole map when the
/// series has none: a pointer into a missing map fails).
pub fn override_patch(master: &Value, rid: &str, value: Value) -> Map<String, Value> {
    let mut p = Map::new();
    if master["recurrenceOverrides"].as_object().is_some_and(|m| !m.is_empty()) {
        p.insert(format!("recurrenceOverrides/{rid}"), value);
    } else {
        p.insert("recurrenceOverrides".into(), json!({ rid: value }));
    }
    p
}

/// End the series just before `rid` (Bulwark truncateSeries): its rules get an UNTIL a second
/// before, and the overrides from `rid` on go. Returns the patch and what puts the series back.
pub fn truncate_patch(master: &Value, rid: &str) -> (Map<String, Value>, Map<String, Value>) {
    let split = parse_local(rid).unwrap_or_default();
    let until = fmt_local(split - Duration::seconds(1));
    let rules: Vec<Value> = master["recurrenceRules"].as_array().cloned().unwrap_or_default().into_iter().map(|mut r| {
        r["until"] = json!(until);
        if let Some(o) = r.as_object_mut() { o.remove("count"); }
        r
    }).collect();
    let mut patch = Map::new();
    patch.insert("recurrenceRules".into(), Value::Array(rules));
    let mut restore = Map::new();
    restore.insert("recurrenceRules".into(), master["recurrenceRules"].clone());
    let mut removed = false;
    if let Some(m) = master["recurrenceOverrides"].as_object() {
        for k in m.keys() {
            if parse_local(k).is_some_and(|t| t >= split) { patch.insert(format!("recurrenceOverrides/{k}"), Value::Null); removed = true; }
        }
    }
    if removed { restore.insert("recurrenceOverrides".into(), master["recurrenceOverrides"].clone()); }
    (patch, restore)
}

/// The new series "this and following" becomes (Bulwark followingSeriesData): the old one's
/// details with the editor's changes, from the occurrence on, with what is left of its COUNT
/// (or the editor's new rule), a UID of its own and every attendee asked again.
pub fn following_series(master: &Value, rid: &str, data: &Map<String, Value>, me: &[String]) -> Map<String, Value> {
    const KEEP: [&str; 18] = ["title", "description", "descriptionContentType", "locations", "virtualLocations", "links", "keywords", "categories", "color",
        "locale", "status", "freeBusyStatus", "privacy", "calendarIds", "useDefaultAlerts", "duration", "timeZone", "showWithoutTime"];
    let mut n = Map::new();
    for k in KEEP { if let Some(v) = master.get(k).filter(|v| !v.is_null()) { n.insert(k.into(), v.clone()); } }
    if master["useDefaultAlerts"] != true && master["alerts"].is_object() { n.insert("alerts".into(), master["alerts"].clone()); }
    if master["participants"].is_object() { n.insert("participants".into(), master["participants"].clone()); }
    if master["organizerCalendarAddress"].is_string() { n.insert("organizerCalendarAddress".into(), master["organizerCalendarAddress"].clone()); }
    let rule_changed = data.get("recurrenceRules").is_some_and(|r| !recurrence::same_rules(r, &master["recurrenceRules"]));
    for (k, v) in data {
        if k == "recurrenceRules" || k == "recurrenceOverrides" || k == "excludedRecurrenceRules" { continue; }
        if v.is_null() { n.remove(k); } else { n.insert(k.clone(), v.clone()); }
    }
    let rules = if rule_changed { data["recurrenceRules"].clone() } else {
        let start = master["start"].as_str().and_then(parse_local).unwrap_or_default();
        let split = parse_local(rid).unwrap_or_default();
        Value::Array(master["recurrenceRules"].as_array().cloned().unwrap_or_default().into_iter().map(|mut r| {
            if let Some(c) = r["count"].as_i64() {
                r["count"] = json!((c - recurrence::occurrences_before(start, &Rule::parse(&r), split)).max(1));
            }
            r
        }).collect())
    };
    if !rules.is_null() { n.insert("recurrenceRules".into(), rules); }
    if !n.contains_key("start") { n.insert("start".into(), json!(rid)); }
    n.insert("uid".into(), json!(calendar::new_uid()));
    n.insert("@type".into(), json!("Event"));
    if let Some(p) = n.get("participants").cloned() { n.insert("participants".into(), asked_again(&p, me)); }
    n
}

/// The series update of "all events" (Bulwark seriesUpdatesFromOccurrence): the series moves by
/// as much as the occurrence did; a repeat the user left alone stays as it is.
pub fn series_update(master: &Value, occ_start: NaiveDateTime, data: &Map<String, Value>) -> Map<String, Value> {
    let mut d = data.clone();
    let new_start = d.get("start").and_then(|v| v.as_str()).and_then(parse_local);
    let mstart = master["start"].as_str().and_then(parse_local);
    match (new_start, mstart) {
        (Some(ns), Some(ms)) if ns != occ_start => { d.insert("start".into(), json!(fmt_local(ms + (ns - occ_start)))); }
        _ => { d.remove("start"); }
    }
    if d.get("recurrenceRules").is_some_and(|r| recurrence::same_rules(r, &master["recurrenceRules"])) {
        d.remove("recurrenceRules");
    }
    d
}

// ---------------- opening ----------------

fn calendars_for_editor() -> (Vec<String>, Vec<slint::SharedString>) {
    let cals = calendar::calendars();
    let ids = cals.iter().map(|c| c["id"].as_str().unwrap_or("").to_string()).collect();
    let names = cals.iter().map(|c| {
        let n = c["name"].as_str().unwrap_or("Calendar");
        match c["_owner"].as_str() { Some(o) if !o.is_empty() => format!("{n} ({o})"), _ => n.to_string() }.into()
    }).collect();
    (ids, names)
}

fn writable(cal: &Value) -> bool {
    if crate::cal_extra::is_subscription(cal["id"].as_str().unwrap_or("")) { return false; }
    let r = &cal["myRights"];
    !r.is_object() || r["mayWriteAll"] == true || r["mayWriteOwn"] == true || r["mayAddItems"] == true
}

fn set_common(u: &AppWindow) {
    let (ids, names) = calendars_for_editor();
    u.set_ev_calendars(ModelRc::new(VecModel::from(names)));
    ED.with(|e| e.borrow_mut().calendar_ids = ids);
    let x = u.global::<crate::CalX>();
    x.set_ed_tz_open(false);
    x.set_ed_tz_query("".into());
    x.set_ed_tz_matches(ModelRc::new(VecModel::from(Vec::<slint::SharedString>::new())));
    x.set_ed_att_query("".into());
    x.set_ed_att_suggest(ModelRc::new(VecModel::from(Vec::<CxSuggest>::new())));
    x.set_ed_find_open(false);
    x.set_ed_find_busy(false);
    x.set_ed_find_slots(ModelRc::new(VecModel::from(Vec::<CxSlot>::new())));
    x.set_rr_open(false);
    x.set_ed_send_invites(true);
    u.set_ev_status("".into());
}

/// A new event on `date` at `hour:minute` (hour < 0: the next full hour), all day or not.
pub fn open_new(u: &AppWindow, date: NaiveDate, hour: i32, minute: i32, all_day: bool, title: &str) {
    set_common(u);
    let h = if hour >= 0 { hour } else { (Local::now().hour() as i32 + 1).min(23) };
    let start = date.and_hms_opt(h as u32, minute.clamp(0, 59) as u32, 0).unwrap();
    let end = start + Duration::hours(1);
    let cal = calendar::calendars().iter().position(|c| c["isDefault"] == true && c["_shared"] != true).unwrap_or(0) as i32;
    let tz = crate::agent::local_tz();
    ED.with(|e| {
        let mut e = e.borrow_mut();
        let ids = std::mem::take(&mut e.calendar_ids);
        *e = Ed { tz: Some(tz.clone()), prev_start: Some(start), prev_end: Some(end), is_organizer: true, calendar_ids: ids, gen: e.gen + 1, ..Default::default() };
    });
    u.set_ev_id("".into());
    u.set_ev_title(title.into());
    u.set_ev_date(start.format("%Y-%m-%d").to_string().into());
    u.set_ev_start(start.format("%H:%M").to_string().into());
    u.set_ev_end(end.format("%H:%M").to_string().into());
    u.set_ev_all_day(all_day);
    u.set_ev_location("".into());
    u.set_ev_description("".into());
    u.set_ev_repeat(0);
    u.set_ev_calendar(cal);
    let x = u.global::<crate::CalX>();
    x.set_ed_end_date(end.format("%Y-%m-%d").to_string().into());
    x.set_ed_tz(tz.into());
    x.set_ed_readonly(false);
    x.set_ed_series_note("".into());
    x.set_ed_custom_label("Custom…".into());
    x.set_ed_freebusy(0);
    x.set_ed_privacy(0);
    x.set_ed_privacy_editable(true);
    x.set_ed_organizer(true);
    x.set_ed_organizer_name("".into());
    x.set_ed_alerts_default(true);
    refresh_default_alerts(u);
    render_people(u);
    render_alerts(u);
    update_your_time(u);
    u.set_ev_open(true);
}

/// Open an existing event, or one occurrence of a series ("id|recurrenceId").
pub fn open_existing(u: &AppWindow, id: &str) {
    if let Some(key) = id.strip_prefix("bday:") { open_birthday(u, key); return; }
    let base = calendar::base_id(id).to_string();
    let Some(e) = calendar::raw_event(&base) else { crate::mail::show_toast(u, "This event is no longer there.", false); return };
    set_common(u);
    let rid = calendar::rid_of(id).map(String::from);
    let all_day = e["showWithoutTime"] == true;
    let dur = recurrence::event_duration(&e);
    let (mut start, mut end) = {
        let s = e["start"].as_str().and_then(parse_local).unwrap_or_else(|| Local::now().naive_local());
        (s, s + dur)
    };
    let mut patch = None;
    if let Some(r) = rid.as_deref().and_then(parse_local) {
        if let Some(o) = recurrence::occurrences(&e, r - Duration::days(1), r + Duration::days(2)).into_iter().find(|o| o.rid.as_deref() == rid.as_deref()) {
            start = o.start;
            end = o.end;
            patch = o.patch;
        } else { start = r; end = r + dur; }
    }
    let field = |k: &str| patch.as_ref().and_then(|p| p.get(k)).filter(|v| !v.is_null()).cloned().unwrap_or_else(|| e[k].clone());
    let me = crate::my_addrs_cached();
    let mut me_all = me.clone();
    if let Some(c) = crate::cal_extra::client() { me_all = self::me(&c); }
    let organizer = event_organizer(&e);
    let is_org = user_organizes(&e, &me_all);
    let cal_id = calendar::first_calendar(&e);
    let cal = calendar::calendar(&cal_id);
    let readonly = cal.as_ref().is_some_and(|c| !writable(c));
    let tz = if all_day { None } else { e["timeZone"].as_str().filter(|z| !z.is_empty()).map(String::from) };
    let rule = e["recurrenceRules"].as_array().and_then(|a| a.first()).map(Rule::parse);
    let (repeat, custom) = match &rule {
        None => (0, None),
        Some(r) if r.is_simple() => (match r.freq.as_str() { "daily" => 1, "weekly" => 2, "monthly" => 3, _ => 4 }, None),
        Some(r) => (5, Some(r.clone())),
    };
    let use_default = e["useDefaultAlerts"] == true;
    let (rows, kept) = if use_default { (vec![], Map::new()) } else { reminders::rows_of(Some(&field("alerts"))) };
    let attendees = attendees_of(&Value::Object({
        let mut m = e.as_object().cloned().unwrap_or_default();
        if let Some(p) = patch.as_ref().and_then(|p| p.get("participants")) { m.insert("participants".into(), p.clone()); }
        m
    }));
    ED.with(|ed| {
        let mut ed = ed.borrow_mut();
        let ids = std::mem::take(&mut ed.calendar_ids);
        *ed = Ed {
            event: Some(e.clone()), rid: rid.clone(), occ_start: Some(start), occ_end: Some(end),
            attendees, organizer: organizer.clone(), is_organizer: is_org,
            rows, kept, custom: custom.clone(), repeat_before: repeat, tz: tz.clone(),
            prev_start: Some(start), prev_end: Some(end), readonly, calendar_ids: ids, gen: ed.gen + 1, ..Default::default()
        };
    });
    let title = field("title");
    u.set_ev_id(id.into());
    u.set_ev_title(title.as_str().unwrap_or("").into());
    u.set_ev_date(start.format("%Y-%m-%d").to_string().into());
    u.set_ev_start(start.format("%H:%M").to_string().into());
    u.set_ev_end(end.format("%H:%M").to_string().into());
    u.set_ev_all_day(all_day);
    let mut loc_holder = e.clone();
    if let Some(p) = &patch { for k in ["locations", "virtualLocations"] { if let Some(v) = p.get(k) { loc_holder[k] = v.clone(); } } }
    u.set_ev_location(calendar::location_of(&loc_holder).into());
    let mut desc_holder = e.clone();
    if let Some(d) = patch.as_ref().and_then(|p| p.get("description")) { desc_holder["description"] = d.clone(); }
    u.set_ev_description(description_text(&desc_holder).into());
    u.set_ev_repeat(repeat);
    u.set_ev_calendar(calendar::calendars().iter().position(|c| c["id"] == cal_id.as_str()).unwrap_or(0) as i32);
    let x = u.global::<crate::CalX>();
    let last = if all_day { (end - Duration::seconds(1)).max(start) } else { end };
    x.set_ed_end_date(last.format("%Y-%m-%d").to_string().into());
    x.set_ed_tz(tz.clone().unwrap_or_default().into());
    x.set_ed_readonly(readonly);
    x.set_ed_readonly_why(if crate::cal_extra::is_subscription(&cal_id) { "Events of a subscription come from its address and can't be changed here." } else { "This calendar is shared with you read-only." }.into());
    x.set_ed_series_note(if rule.is_some() { rule.as_ref().map(|r| r.summary()).unwrap_or_default() } else { String::new() }.into());
    x.set_ed_custom_label(custom.as_ref().map(|r| r.summary()).unwrap_or_else(|| "Custom…".into()).into());
    x.set_ed_freebusy(if field("freeBusyStatus") == "free" { 1 } else { 0 });
    x.set_ed_privacy(match field("privacy").as_str() { Some("private") => 1, Some("secret") => 2, _ => 0 });
    x.set_ed_privacy_editable(cal.as_ref().is_none_or(|c| c["_shared"] != true));
    x.set_ed_organizer(is_org);
    x.set_ed_organizer_name(match &organizer {
        Some(o) if !is_org || !is_me(&o.email, &me_all) => if o.name.is_empty() { o.email.clone() } else { format!("{} <{}>", o.name, o.email) },
        _ => String::new(),
    }.into());
    x.set_ed_alerts_default(use_default);
    refresh_default_alerts(u);
    render_people(u);
    render_alerts(u);
    update_your_time(u);
    u.set_ev_open(true);
    if let Some(c) = crate::cal_extra::client() { refresh_availability(u, c); }
}

fn open_birthday(u: &AppWindow, key: &str) {
    let Some(o) = crate::cal_extra::birthday(key) else { return };
    set_common(u);
    ED.with(|e| { let mut e = e.borrow_mut(); let g = e.gen + 1; *e = Ed { readonly: true, gen: g, ..Default::default() }; });
    u.set_ev_id(format!("bday:{key}").into());
    u.set_ev_title(o.title.clone().into());
    u.set_ev_date(o.start.format("%Y-%m-%d").to_string().into());
    u.set_ev_all_day(true);
    u.set_ev_location("".into());
    u.set_ev_description("".into());
    u.set_ev_repeat(4);
    let x = u.global::<crate::CalX>();
    x.set_ed_end_date(o.start.format("%Y-%m-%d").to_string().into());
    x.set_ed_readonly(true);
    x.set_ed_readonly_why("Birthdays come from your contacts. Change them there.".into());
    x.set_ed_series_note("".into());
    x.set_ed_organizer(false);
    x.set_ed_organizer_name("".into());
    x.set_ed_alerts_default(true);
    x.set_ed_default_alerts("No reminders".into());
    render_people(u);
    u.set_ev_open(true);
}

// ---------------- people ----------------

fn status_label(s: Option<&str>) -> (&'static str, i32) {
    match s { Some("accepted") => ("Accepted", 1), Some("tentative") => ("Maybe", 2), Some("declined") => ("Declined", 3), Some("delegated") => ("Delegated", 4), _ => ("No reply", 0) }
}

fn render_people(u: &AppWindow) {
    let rows: Vec<CxAttendee> = ED.with(|ed| {
        let ed = ed.borrow();
        let statuses: HashMap<String, String> = ed.event.as_ref().and_then(|e| e["participants"].as_object()).map(|ps| ps.values()
            .filter_map(|p| Some((calendar::participant_email(p).to_lowercase(), p["participationStatus"].as_str()?.to_string()))).collect()).unwrap_or_default();
        let mut out = vec![];
        if !ed.attendees.is_empty() {
            let org = ed.organizer.clone().or_else(|| crate::cal_extra::client().map(|c| Person { name: String::new(), email: me(&c)[0].clone() }));
            if let Some(o) = org {
                let av = ed.avail.get(&o.email.to_lowercase()).cloned().unwrap_or_default();
                out.push(CxAttendee { name: o.name.clone().into(), email: o.email.clone().into(), initials: initials(&o.name, &o.email).into(), status: "Organizer".into(), tone: 0,
                    avail: av.0.into(), avail_tone: av.1, organizer: true });
            }
        }
        for a in &ed.attendees {
            let (label, tone) = status_label(statuses.get(&a.email.to_lowercase()).map(|s| s.as_str()));
            let av = ed.avail.get(&a.email.to_lowercase()).cloned().unwrap_or_default();
            out.push(CxAttendee { name: a.name.clone().into(), email: a.email.clone().into(), initials: initials(&a.name, &a.email).into(), status: label.into(), tone,
                avail: av.0.into(), avail_tone: av.1, organizer: false });
        }
        out
    });
    u.global::<crate::CalX>().set_ed_attendees(ModelRc::new(VecModel::from(rows)));
}

pub fn att_query(u: &AppWindow, q: &str) {
    let q = q.trim().to_lowercase();
    let have: Vec<String> = ED.with(|e| e.borrow().attendees.iter().map(|a| a.email.to_lowercase()).collect());
    let hits: Vec<(Person, Vec<Person>)> = if q.len() < 2 { vec![] } else { crate::cal_extra::people_matching(&q, &have, 8) };
    let rows: Vec<CxSuggest> = hits.iter().map(|(p, members)| CxSuggest {
        name: if members.is_empty() { p.name.clone() } else { format!("{} ({} people)", p.name, members.len()) }.into(),
        email: p.email.clone().into(), initials: initials(&p.name, &p.email).into(), group: !members.is_empty(),
    }).collect();
    ED.with(|e| e.borrow_mut().suggest = hits);
    u.global::<crate::CalX>().set_ed_att_suggest(ModelRc::new(VecModel::from(rows)));
}

fn add_people(u: &AppWindow, people: Vec<Person>) {
    let mine = crate::cal_extra::client().map(|c| me(&c)).unwrap_or_default();
    let org = ED.with(|e| e.borrow().organizer.clone()).map(|o| o.email.to_lowercase());
    ED.with(|e| {
        let mut e = e.borrow_mut();
        for p in people {
            let key = p.email.trim().to_lowercase();
            if !EMAIL_OK(&key) || e.attendees.iter().any(|a| a.email.eq_ignore_ascii_case(&key)) { continue; }
            // the organizer is listed on its own; the user organizing isn't a guest
            if org.as_deref() == Some(key.as_str()) || (e.is_organizer && is_me(&key, &mine) && org.is_none()) { continue; }
            e.attendees.push(Person { name: p.name.trim().to_string(), email: p.email.trim().to_string() });
        }
    });
    let x = u.global::<crate::CalX>();
    x.set_ed_att_query("".into());
    x.set_ed_att_suggest(ModelRc::new(VecModel::from(Vec::<CxSuggest>::new())));
    render_people(u);
    if let Some(c) = crate::cal_extra::client() { refresh_availability(u, c); }
}

/// "Name <a@b.c>" or a bare address.
pub fn parse_person(t: &str) -> Option<Person> {
    let t = t.trim();
    if let (Some(l), Some(r)) = (t.rfind('<'), t.rfind('>')) {
        let email = t[l + 1..r].trim();
        return EMAIL_OK(email).then(|| Person { name: t[..l].trim().trim_matches('"').to_string(), email: email.to_string() });
    }
    EMAIL_OK(t).then(|| Person { name: String::new(), email: t.to_string() })
}

pub fn att_add(u: &AppWindow, t: &str) {
    if let Some(p) = parse_person(t) { add_people(u, vec![p]); return; }
    // Enter on a name picks the first suggestion
    let first = ED.with(|e| e.borrow().suggest.first().cloned());
    if let Some((p, members)) = first { add_people(u, if members.is_empty() { vec![p] } else { members }); }
}

pub fn att_pick(u: &AppWindow, i: i32) {
    let s = ED.with(|e| e.borrow().suggest.get(i.max(0) as usize).cloned());
    if let Some((p, members)) = s { add_people(u, if members.is_empty() { vec![p] } else { members }); }
}

pub fn att_remove(u: &AppWindow, email: &str) {
    ED.with(|e| e.borrow_mut().attendees.retain(|a| !a.email.eq_ignore_ascii_case(email)));
    render_people(u);
}

// ---------------- reminders ----------------

fn render_alerts(u: &AppWindow) {
    let (rows, kept) = ED.with(|e| { let e = e.borrow(); (e.rows.clone(), e.kept.len()) });
    let list: Vec<CxAlert> = rows.iter().map(|r| CxAlert { value: r.row.value.to_string().into(), unit: r.row.unit, action: r.row.action }).collect();
    let x = u.global::<crate::CalX>();
    x.set_ed_alerts(ModelRc::new(VecModel::from(list)));
    x.set_ed_alerts_kept(kept as i32);
}

/// The calendar's default reminders for the editor's calendar and all-day state.
fn current_defaults(u: &AppWindow) -> Option<Value> {
    let cal_id = ED.with(|e| e.borrow().calendar_ids.get(u.get_ev_calendar().max(0) as usize).cloned()).unwrap_or_default();
    let cal = calendar::calendar(&cal_id);
    let all_day = u.get_ev_all_day();
    // Stalwart copies the defaults into an event when it is created: an event that stays in its
    // calendar and keeps its all-day state shows that copy (Bulwark defaultAlerts)
    let ev = ED.with(|e| e.borrow().event.clone());
    if let Some(e) = ev.filter(|e| e["useDefaultAlerts"] == true && e["alerts"].as_object().is_some_and(|m| !m.is_empty())
        && calendar::first_calendar(e) == cal_id && (e["showWithoutTime"] == true) == all_day) {
        return Some(e["alerts"].clone());
    }
    reminders::calendar_defaults(cal.as_ref(), all_day)
}

fn refresh_default_alerts(u: &AppWindow) {
    let d = current_defaults(u);
    u.global::<crate::CalX>().set_ed_default_alerts(format!("Calendar default: {}", reminders::alerts_label(d.as_ref())).into());
}

pub fn alerts_customize(u: &AppWindow) {
    let (rows, kept) = reminders::rows_of(current_defaults(u).as_ref());
    // rows from the defaults are new alerts of the event's own
    let rows = rows.into_iter().map(|r| EdRow { row: r.row, source: None }).collect();
    ED.with(|e| { let mut e = e.borrow_mut(); e.rows = rows; e.kept = kept; });
    u.global::<crate::CalX>().set_ed_alerts_default(false);
    render_alerts(u);
}

pub fn alerts_use_default(u: &AppWindow) {
    u.global::<crate::CalX>().set_ed_alerts_default(true);
    refresh_default_alerts(u);
}

pub fn alert_add(u: &AppWindow) {
    ED.with(|e| e.borrow_mut().rows.push(EdRow { row: Row { value: 10, unit: 1, action: 0 }, source: None }));
    render_alerts(u);
}

pub fn alert_remove(u: &AppWindow, i: i32) {
    ED.with(|e| { let mut e = e.borrow_mut(); if (i as usize) < e.rows.len() { e.rows.remove(i as usize); } });
    render_alerts(u);
}

pub fn alert_set(u: &AppWindow, i: i32, value: &str, unit: i32, action: i32) {
    let unit_changed = ED.with(|e| {
        let mut e = e.borrow_mut();
        let Some(r) = e.rows.get_mut(i.max(0) as usize) else { return false };
        let changed = r.row.unit != unit || r.row.action != action;
        let v: i64 = value.trim().parse().unwrap_or(r.row.value);
        r.row = Row { value: if unit == 0 { 0 } else { v.clamp(1, 999) }, unit, action };
        changed
    });
    // typing in the amount must not rebuild the row under the caret
    if unit_changed { render_alerts(u); }
}

// ---------------- times and zones ----------------

fn form_times(u: &AppWindow) -> Option<(NaiveDateTime, NaiveDateTime)> {
    let x = u.global::<crate::CalX>();
    let sd = NaiveDate::parse_from_str(u.get_ev_date().trim(), "%Y-%m-%d").ok()?;
    let ed = NaiveDate::parse_from_str(x.get_ed_end_date().trim(), "%Y-%m-%d").ok()?;
    if u.get_ev_all_day() { return Some((sd.and_hms_opt(0, 0, 0)?, ed.and_hms_opt(0, 0, 0)?)); }
    let st = NaiveTime::parse_from_str(u.get_ev_start().trim(), "%H:%M").ok()?;
    let et = NaiveTime::parse_from_str(u.get_ev_end().trim(), "%H:%M").ok()?;
    Some((sd.and_time(st), ed.and_time(et)))
}

/// A field changed: a moved start moves the end along, keeping the length (Bulwark shiftedEnd).
pub fn times_changed(u: &AppWindow, which: &str) {
    let x = u.global::<crate::CalX>();
    if which == "start" {
        let sd = NaiveDate::parse_from_str(u.get_ev_date().trim(), "%Y-%m-%d").ok();
        let st = if u.get_ev_all_day() { Some(NaiveTime::MIN) } else { NaiveTime::parse_from_str(u.get_ev_start().trim(), "%H:%M").ok() };
        if let (Some(sd), Some(st)) = (sd, st) {
            let new_start = sd.and_time(st);
            let (ps, pe) = ED.with(|e| { let e = e.borrow(); (e.prev_start, e.prev_end) });
            if let (Some(ps), Some(pe)) = (ps, pe) {
                if pe >= ps && new_start != ps {
                    let ne = new_start + (pe - ps);
                    x.set_ed_end_date(ne.format("%Y-%m-%d").to_string().into());
                    if !u.get_ev_all_day() { u.set_ev_end(ne.format("%H:%M").to_string().into()); }
                    ED.with(|e| e.borrow_mut().prev_end = Some(ne));
                }
            }
            ED.with(|e| e.borrow_mut().prev_start = Some(new_start));
        }
    } else if let Some((s, e)) = form_times(u) {
        ED.with(|ed| { let mut ed = ed.borrow_mut(); ed.prev_start = Some(s); ed.prev_end = Some(e); });
    }
    if which == "allday" { refresh_default_alerts(u); }
    update_your_time(u);
}

/// "Your time: Tue 16:00–17:00" when the form's zone isn't the user's.
fn update_your_time(u: &AppWindow) {
    let x = u.global::<crate::CalX>();
    let tz = x.get_ed_tz().to_string();
    let local = crate::agent::local_tz();
    let label = match form_times(u) {
        Some((s, e)) if !u.get_ev_all_day() && !tz.is_empty() && tz != local => {
            let (ls, le) = (recurrence::zone_to_local(s, Some(&tz)), recurrence::zone_to_local(e, Some(&tz)));
            let h12 = calendar::h12();
            let day = if ls.date() != s.date() { ls.format("%a ").to_string() } else { String::new() };
            format!("Your time: {day}{}–{}", calendar::fmt_time(ls, h12), calendar::fmt_time(le, h12))
        }
        _ => String::new(),
    };
    x.set_ed_your_time(label.into());
}

pub fn tz_query(u: &AppWindow, q: &str) {
    let hits: Vec<slint::SharedString> = recurrence::search_zones(q, 8).into_iter().map(Into::into).collect();
    u.global::<crate::CalX>().set_ed_tz_matches(ModelRc::new(VecModel::from(hits)));
}

pub fn tz_pick(u: &AppWindow, z: &str) {
    let x = u.global::<crate::CalX>();
    x.set_ed_tz(z.into());
    x.set_ed_tz_open(false);
    x.set_ed_tz_query("".into());
    x.set_ed_tz_matches(ModelRc::new(VecModel::from(Vec::<slint::SharedString>::new())));
    ED.with(|e| e.borrow_mut().tz = Some(z.to_string()));
    update_your_time(u);
}

// ---------------- custom recurrence ----------------

const NTH: [i64; 5] = [1, 2, 3, 4, -1];

pub fn rr_begin(u: &AppWindow) {
    let x = u.global::<crate::CalX>();
    let start = form_times(u).map(|t| t.0).unwrap_or_else(|| Local::now().naive_local());
    let rule = ED.with(|e| e.borrow().custom.clone()).or_else(|| {
        let r = u.get_ev_repeat();
        (1..=4).contains(&r).then(|| Rule::simple(["daily", "weekly", "monthly", "yearly"][r as usize - 1]))
    }).unwrap_or_else(|| Rule::simple("weekly"));
    if u.get_ev_repeat() != 5 { ED.with(|e| e.borrow_mut().repeat_before = u.get_ev_repeat()); }
    x.set_rr_freq(match rule.freq.as_str() { "daily" => 0, "monthly" => 2, "yearly" => 3, _ => 1 });
    x.set_rr_interval(rule.interval.to_string().into());
    let mut days = [false; 7];
    if rule.freq == "weekly" && !rule.by_day.is_empty() { for (d, _) in &rule.by_day { days[d.num_days_from_monday() as usize] = true; } } else { days[start.weekday().num_days_from_monday() as usize] = true; }
    x.set_rr_days(ModelRc::new(VecModel::from(days.to_vec())));
    let nth = rule.nth_day();
    x.set_rr_mode(if nth.is_some() { 1 } else { 0 });
    x.set_rr_month_day(rule.by_month_day.first().copied().unwrap_or(start.day() as i64).to_string().into());
    x.set_rr_nth(nth.map(|(_, n)| NTH.iter().position(|x| *x == n).unwrap_or(0) as i32).unwrap_or(((start.day() as i32 - 1) / 7).min(3)));
    x.set_rr_nth_day(nth.map(|(d, _)| d.num_days_from_monday() as i32).unwrap_or(start.weekday().num_days_from_monday() as i32));
    x.set_rr_month(rule.by_month.first().map(|m| *m as i32 - 1).unwrap_or(start.month0() as i32));
    x.set_rr_ends(if rule.count.is_some() { 2 } else if rule.until.is_some() { 1 } else { 0 });
    x.set_rr_until(rule.until.map(|u| u.format("%Y-%m-%d").to_string()).unwrap_or_else(|| (start + Duration::days(365)).format("%Y-%m-%d").to_string()).into());
    x.set_rr_count(rule.count.unwrap_or(12).to_string().into());
    x.set_rr_open(true);
    rr_changed(u);
}

fn rr_rule(u: &AppWindow) -> Rule {
    let x = u.global::<crate::CalX>();
    let freq = ["daily", "weekly", "monthly", "yearly"][x.get_rr_freq().clamp(0, 3) as usize];
    let mut r = Rule::simple(freq);
    r.interval = x.get_rr_interval().trim().parse::<i64>().unwrap_or(1).clamp(1, 999);
    match freq {
        "weekly" => {
            use slint::Model;
            let days: Vec<bool> = x.get_rr_days().iter().collect();
            r.by_day = (0..7).filter(|i| days.get(*i).copied().unwrap_or(false)).map(|i| (Weekday::try_from(i as u8).unwrap(), None)).collect();
            if r.by_day.is_empty() {
                let wd = form_times(u).map(|t| t.0.weekday()).unwrap_or(Weekday::Mon);
                r.by_day = vec![(wd, None)];
            }
        }
        "monthly" | "yearly" => {
            if x.get_rr_mode() == 1 {
                r.by_day = vec![(Weekday::try_from(x.get_rr_nth_day().clamp(0, 6) as u8).unwrap(), Some(NTH[x.get_rr_nth().clamp(0, 4) as usize]))];
            } else {
                r.by_month_day = vec![x.get_rr_month_day().trim().parse::<i64>().unwrap_or(1).clamp(1, 31)];
            }
            if freq == "yearly" { r.by_month = vec![x.get_rr_month().clamp(0, 11) as u32 + 1]; }
        }
        _ => {}
    }
    match x.get_rr_ends() {
        1 => r.until = NaiveDate::parse_from_str(x.get_rr_until().trim(), "%Y-%m-%d").ok().and_then(|d| d.and_hms_opt(23, 59, 59)),
        2 => r.count = Some(x.get_rr_count().trim().parse::<i64>().unwrap_or(1).clamp(1, 9999)),
        _ => {}
    }
    r
}

pub fn rr_changed(u: &AppWindow) {
    let s = rr_rule(u).summary();
    u.global::<crate::CalX>().set_rr_summary(s.into());
}

pub fn rr_toggle_day(u: &AppWindow, i: i32) {
    use slint::Model;
    let x = u.global::<crate::CalX>();
    let mut days: Vec<bool> = x.get_rr_days().iter().collect();
    if let Some(d) = days.get_mut(i.max(0) as usize) {
        // at least one day stays on
        if !*d || days.iter().filter(|x| **x).count() > 1 { let v = !days[i as usize]; days[i as usize] = v; }
    }
    x.set_rr_days(ModelRc::new(VecModel::from(days)));
    rr_changed(u);
}

pub fn rr_save(u: &AppWindow) {
    let r = rr_rule(u);
    let x = u.global::<crate::CalX>();
    x.set_ed_custom_label(r.summary().into());
    x.set_rr_open(false);
    ED.with(|e| e.borrow_mut().custom = Some(r));
    u.set_ev_repeat(5);
}

pub fn rr_cancel(u: &AppWindow) {
    u.global::<crate::CalX>().set_rr_open(false);
    let (custom, before) = ED.with(|e| { let e = e.borrow(); (e.custom.is_some(), e.repeat_before) });
    if !custom { u.set_ev_repeat(before); }
}

// ---------------- saving ----------------

fn read_form(u: &AppWindow, c: &Client) -> Result<Form, String> {
    let (start, end) = form_times(u).ok_or("Use YYYY-MM-DD for dates and HH:MM for times.")?;
    let x = u.global::<crate::CalX>();
    // a typed address not yet added counts (Bulwark flush)
    let pending = x.get_ed_att_query().to_string();
    if let Some(p) = parse_person(&pending) { add_people(u, vec![p]); }
    let cal_id = ED.with(|e| e.borrow().calendar_ids.get(u.get_ev_calendar().max(0) as usize).cloned()).ok_or("Choose a calendar.")?;
    let cal = calendar::calendar(&cal_id).ok_or("Choose a calendar.")?;
    if !writable(&cal) { return Err("This calendar can't take new events.".into()); }
    if u.get_ev_title().trim().chars().count() > 500 { return Err("The title is too long.".into()); }
    let (ev_cals, raw_cal) = (calendar::calendars(), calendar::split_ns(&cal_id).1);
    let event = ED.with(|e| e.borrow().event.clone());
    let reserved: Vec<String> = ev_cals.iter().filter(|c| c["id"] == cal_id.as_str() || event.as_ref().is_some_and(|e| e["calendarIds"].get(c["id"].as_str().unwrap_or("")).is_some()))
        .flat_map(|c| [c["defaultAlertsWithTime"].as_object(), c["defaultAlertsWithoutTime"].as_object()].into_iter().flatten().flat_map(|m| m.keys().cloned()).collect::<Vec<_>>()).collect();
    let tz = x.get_ed_tz().to_string();
    Ok(ED.with(|e| {
        let e = e.borrow();
        Form {
            title: u.get_ev_title().to_string(),
            description: u.get_ev_description().to_string(),
            location: u.get_ev_location().to_string(),
            start, end,
            all_day: u.get_ev_all_day(),
            tz: if tz.is_empty() { None } else { Some(tz) },
            calendar: raw_cal,
            repeat: u.get_ev_repeat(),
            custom: e.custom.clone(),
            use_default_alerts: x.get_ed_alerts_default(),
            default_alerts: current_defaults(u),
            rows: e.rows.clone(),
            kept: e.kept.clone(),
            reserved_alert_ids: reserved,
            attendees: e.attendees.clone(),
            send: x.get_ed_send_invites(),
            organizer: e.is_organizer,
            freebusy: x.get_ed_freebusy(),
            privacy: x.get_ed_privacy_editable().then(|| x.get_ed_privacy()),
            me: me(c),
        }
    }))
}

pub fn save(u: &AppWindow, c: Arc<Client>) {
    if ED.with(|e| e.borrow().readonly) { u.set_ev_open(false); return; }
    let f = match read_form(u, &c) { Ok(f) => f, Err(e) => { u.set_ev_status(e.into()); return } };
    let id = u.get_ev_id().to_string();
    let (event, rid, occ) = ED.with(|e| { let e = e.borrow(); (e.event.clone(), e.rid.clone(), e.occ_start.zip(e.occ_end)) });
    let cal_id = ED.with(|e| e.borrow().calendar_ids.get(u.get_ev_calendar().max(0) as usize).cloned()).unwrap_or_default();
    // "Duplicate" opens an existing event and clears its id: a new event from its fields
    let event = if id.is_empty() { None } else { event };
    let (mut data, send) = build_data(&f, event.as_ref(), if rid.is_some() { occ } else { None });
    if event.is_none() {
        data.insert("@type".into(), json!("Event"));
        for k in ["recurrenceOverrides", "excludedRecurrenceRules", "replyTo"] { data.remove(k); }
        data.retain(|_, v| !v.is_null());
        u.set_ev_status("Saving…".into());
        run(u, c, "Event not saved", move |c| {
            let (acc, _) = server_ref(c, &cal_id);
            create(c, &acc, Value::Object(data), send).map(|_| ())
        });
        return;
    }
    let e = event.unwrap();
    if rid.is_some() {
        ED.with(|ed| ed.borrow_mut().pending = Some(Pending::Save { data, send }));
        let x = u.global::<crate::CalX>();
        x.set_scope_delete(false);
        x.set_scope_title(e["title"].as_str().unwrap_or("").into());
        x.set_scope_choice(0);
        x.set_scope_open(true);
        return;
    }
    let eid = e["id"].as_str().unwrap_or("").to_string();
    u.set_ev_status("Saving…".into());
    run(u, c, "Event not saved", move |c| update(c, &eid, data, send));
}

fn create(c: &Client, acc: &str, ev: Value, send: bool) -> Result<String, String> {
    let r = c.call(json!([["CalendarEvent/set", {"accountId": acc, "sendSchedulingMessages": send, "create": {"e": ev}}, "s"]]))?;
    r[0][1]["created"]["e"]["id"].as_str().map(String::from).ok_or_else(|| crate::jmap::set_error(&r[0][1]["notCreated"]["e"]))
}

/// Update an event (namespaced id) with a patch; calendarIds are written as server ids.
fn update(c: &Client, id: &str, mut patch: Map<String, Value>, send: bool) -> Result<(), String> {
    let (acc, sid) = server_ref(c, id);
    if let Some(ci) = patch.get("calendarIds").cloned() { patch.insert("calendarIds".into(), calendar::server_calendar_ids(&ci)); }
    let r = c.call(json!([["CalendarEvent/set", {"accountId": acc, "sendSchedulingMessages": send, "update": {sid.clone(): Value::Object(patch)}}, "s"]]))?;
    if r[0][1]["notUpdated"][&sid].is_object() { return Err(crate::jmap::set_error(&r[0][1]["notUpdated"][&sid])); }
    Ok(())
}

fn destroy(c: &Client, id: &str, send: bool) -> Result<(), String> {
    let (acc, sid) = server_ref(c, id);
    let r = c.call(json!([["CalendarEvent/set", {"accountId": acc, "sendSchedulingMessages": send, "destroy": [sid.clone()]}, "s"]]))?;
    if r[0][1]["notDestroyed"][&sid].is_object() { return Err(crate::jmap::set_error(&r[0][1]["notDestroyed"][&sid])); }
    Ok(())
}

/// Run server work in the background; close the editor and reload on success.
fn run(u: &AppWindow, c: Arc<Client>, what: &'static str, f: impl FnOnce(&Client) -> Result<(), String> + Send + 'static) {
    let w = u.as_weak();
    std::thread::spawn(move || {
        let res = f(&c);
        let w2 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| match res {
            Ok(()) => {
                u.set_ev_open(false);
                u.global::<crate::CalX>().set_scope_open(false);
                calendar::load(w2.clone(), c.clone());
                reminders::changed(w2, c);
            }
            Err(e) => {
                u.global::<crate::CalX>().set_scope_open(false);
                if u.get_ev_open() { u.set_ev_status(format!("{what}: {e}").into()); } else { crate::mail::show_toast(&u, &format!("{what}: {e}"), false); }
            }
        });
    });
}

/// The user picked this (0), this and following (1) or all (2) in the scope dialog.
pub fn scope_go(u: &AppWindow, c: Arc<Client>, choice: i32) {
    let (pending, master, rid, occ_start) = ED.with(|e| { let e = e.borrow(); (e.pending.clone(), e.event.clone(), e.rid.clone(), e.occ_start) });
    let (Some(p), Some(master), Some(rid)) = (pending, master, rid) else { u.global::<crate::CalX>().set_scope_open(false); return };
    let mid = master["id"].as_str().unwrap_or("").to_string();
    let me = me(&c);
    let first = master["start"].as_str().and_then(parse_local) == parse_local(&rid);
    let choice = if choice == 1 && first { 2 } else { choice };
    u.set_ev_status("Saving…".into());
    match p {
        Pending::Save { data, send } => {
            // the occurrence's new start on the series' own clock
            let series_zone = master["timeZone"].as_str().map(String::from);
            let form_start = data.get("start").and_then(|v| v.as_str()).and_then(parse_local).unwrap_or_default();
            let form_zone = data.get("timeZone").and_then(|v| v.as_str()).map(String::from);
            let new_start = if form_zone != series_zone && master["showWithoutTime"] != true && data.get("showWithoutTime") != Some(&json!(true)) {
                recurrence::local_to_zone(recurrence::zone_to_local(form_start, form_zone.as_deref()), series_zone.as_deref())
            } else { form_start };
            match choice {
                0 => {
                    let ov = occurrence_override(&master, &rid, &data, new_start);
                    let patch = override_patch(&master, &rid, ov);
                    run(u, c, "Event not saved", move |c| update(c, &mid, patch, send));
                }
                1 => {
                    let (trunc, restore) = truncate_patch(&master, &rid);
                    let mut next = following_series(&master, &rid, &data, &me);
                    next.insert("start".into(), json!(fmt_local(form_start)));
                    if let Some(ci) = next.get("calendarIds").cloned() { next.insert("calendarIds".into(), calendar::server_calendar_ids(&ci)); }
                    run(u, c, "Event not saved", move |c| {
                        update(c, &mid, trunc, send)?;
                        let (acc, _) = server_ref(c, &mid);
                        if let Err(e) = create(c, &acc, Value::Object(next), send) {
                            let _ = update(c, &mid, restore, false);
                            return Err(e);
                        }
                        Ok(())
                    });
                }
                _ => {
                    let patch = series_update(&master, occ_start.unwrap_or(new_start), &{
                        let mut d = data.clone();
                        d.insert("start".into(), json!(fmt_local(new_start)));
                        d
                    });
                    run(u, c, "Event not saved", move |c| update(c, &mid, patch, send));
                }
            }
        }
        Pending::Delete { send } => match choice {
            0 => {
                let patch = override_patch(&master, &rid, json!({"excluded": true}));
                run(u, c, "Event not deleted", move |c| update(c, &mid, patch, send));
            }
            1 => {
                let (trunc, _) = truncate_patch(&master, &rid);
                run(u, c, "Event not deleted", move |c| update(c, &mid, trunc, send));
            }
            _ => run(u, c, "Event not deleted", move |c| destroy(c, &mid, send)),
        },
    }
}

pub fn delete(u: &AppWindow, c: Arc<Client>) {
    let id = u.get_ev_id().to_string();
    if id.is_empty() || id.starts_with("bday:") { u.set_ev_open(false); return; }
    let base = calendar::base_id(&id).to_string();
    let Some(e) = calendar::raw_event(&base) else { u.set_ev_open(false); return };
    let me = me(&c);
    let send = user_organizes(&e, &me) && e["participants"].as_object().is_some_and(|p| p.len() > 1);
    if let Some(rid) = calendar::rid_of(&id) {
        // the context menu deletes without opening the editor first
        ED.with(|ed| {
            let mut ed = ed.borrow_mut();
            ed.event = Some(e.clone());
            ed.rid = Some(rid.to_string());
            ed.pending = Some(Pending::Delete { send });
        });
        let x = u.global::<crate::CalX>();
        x.set_scope_delete(true);
        x.set_scope_title(e["title"].as_str().unwrap_or("").into());
        x.set_scope_choice(0);
        x.set_scope_open(true);
        return;
    }
    run(u, c, "Event not deleted", move |c| destroy(c, &base, send));
}

// ---------------- free/busy and suggested times ----------------

/// Principal ids by address (Principal/query with an email filter), and whether the server
/// refused to list principals.
fn principal_ids(c: &Client, emails: &[String]) -> (HashMap<String, String>, bool) {
    let acc = c.account_for("urn:ietf:params:jmap:principals");
    let mut out = HashMap::new();
    let mut refused = false;
    let calls: Vec<Value> = emails.iter().enumerate().map(|(i, e)| json!(["Principal/query", {"accountId": acc, "filter": {"email": e}, "limit": 2}, format!("p{i}")])).collect();
    if calls.is_empty() { return (out, false); }
    match c.request(Value::Array(calls)) {
        Ok(rs) => for r in rs {
            if r[0] == "error" { refused = true; continue; }
            let i: usize = r[2].as_str().unwrap_or("p0")[1..].parse().unwrap_or(0);
            if let (Some(email), Some(id)) = (emails.get(i), r[1]["ids"][0].as_str()) { out.insert(email.to_lowercase(), id.to_string()); }
        },
        Err(_) => refused = true,
    }
    (out, refused)
}

/// Busy periods (utc start ms, utc end ms, tentative) per principal id, or the error type.
fn availability(c: &Client, ids: &[String], from_ms: i64, to_ms: i64) -> HashMap<String, Result<Vec<(i64, i64, bool)>, String>> {
    let acc = c.account_for("urn:ietf:params:jmap:principals");
    let fmt = |ms: i64| chrono::Utc.timestamp_millis_opt(ms).single().map(|t| t.format("%Y-%m-%dT%H:%M:%SZ").to_string()).unwrap_or_default();
    let calls: Vec<Value> = ids.iter().enumerate().map(|(i, id)| json!(["Principal/getAvailability",
        {"accountId": acc, "id": id, "utcStart": fmt(from_ms), "utcEnd": fmt(to_ms), "showDetails": false}, format!("a{i}")])).collect();
    let mut out = HashMap::new();
    let parse = |s: &str| chrono::DateTime::parse_from_rfc3339(s).ok().map(|t| t.timestamp_millis());
    match c.request(Value::Array(calls)) {
        Ok(rs) => for r in rs {
            let i: usize = r[2].as_str().unwrap_or("a0")[1..].parse().unwrap_or(0);
            let Some(id) = ids.get(i) else { continue };
            if r[0] == "error" { out.insert(id.clone(), Err(r[1]["type"].as_str().unwrap_or("serverFail").to_string())); continue; }
            let periods = r[1]["list"].as_array().cloned().unwrap_or_default().iter().filter_map(|p| {
                Some((parse(p["utcStart"].as_str()?)?, parse(p["utcEnd"].as_str()?)?, p["busyStatus"] == "tentative"))
            }).collect();
            out.insert(id.clone(), Ok(periods));
        },
        Err(e) => for id in ids { out.insert(id.clone(), Err(e.clone())); },
    }
    out
}

/// The status of one attendee for a window (Bulwark availabilityFor): busy wins over tentative.
pub fn availability_for(periods: &[(i64, i64, bool)], from: i64, to: i64) -> (&'static str, i32) {
    let mut st = ("Free", 1);
    for (s, e, tent) in periods {
        if *s < to && *e > from {
            if *tent { st = ("Tentative", 2); } else { return ("Busy", 3); }
        }
    }
    st
}

/// Free/busy of every guest for the form's time.
pub fn refresh_availability(u: &AppWindow, c: Arc<Client>) {
    if !c.has("urn:ietf:params:jmap:principals:availability") { return; }
    let emails: Vec<String> = ED.with(|e| e.borrow().attendees.iter().map(|a| a.email.clone()).collect());
    if emails.is_empty() { return; }
    let Some((s, e)) = form_times(u) else { return };
    let tz = u.global::<crate::CalX>().get_ed_tz().to_string();
    let zone = if u.get_ev_all_day() || tz.is_empty() { None } else { Some(tz) };
    let (from, to) = (recurrence::zone_to_utc_ms(s, zone.as_deref()), recurrence::zone_to_utc_ms(if u.get_ev_all_day() { e + Duration::days(1) } else { e }, zone.as_deref()));
    let gen = ED.with(|e| e.borrow().gen);
    let w = u.as_weak();
    std::thread::spawn(move || {
        let (ids, refused) = principal_ids(&c, &emails);
        let list: Vec<String> = ids.values().cloned().collect();
        let answers = availability(&c, &list, from, to.max(from + 60_000));
        let mut st: HashMap<String, (String, i32)> = HashMap::new();
        for e in &emails {
            let key = e.to_lowercase();
            let label = match ids.get(&key) {
                None => if refused { ("Not looked up", 0) } else { ("Not found", 0) },
                Some(id) => match answers.get(id) {
                    Some(Ok(p)) if p.is_empty() => ("No busy times", 0),
                    Some(Ok(p)) => availability_for(p, from, to),
                    Some(Err(t)) if t == "forbidden" => return,
                    _ => ("Unknown", 0),
                },
            };
            st.insert(key, (label.0.to_string(), label.1));
        }
        let _ = w.upgrade_in_event_loop(move |u| {
            if ED.with(|e| e.borrow().gen) != gen { return; }
            ED.with(|e| e.borrow_mut().avail = st);
            render_people(&u);
        });
    });
}

pub const SEARCH_WORKING_DAYS: usize = 10;
pub const MAX_SUGGESTIONS: usize = 5;
pub const GRID_MINUTES: i64 = 30;

/// Up to `max` meeting slots of `dur_ms` within the working hours of the next working days, on a
/// 30-minute grid, overlapping no busy period (Bulwark findFreeSlots). Times are local.
pub fn find_free_slots(now: NaiveDateTime, dur_ms: i64, working: &[u32], start_h: i64, end_h: i64, busy: &[(i64, i64)], max: usize) -> Vec<(NaiveDateTime, NaiveDateTime)> {
    let ms = |t: NaiveDateTime| recurrence::zone_to_utc_ms(t, None);
    let working: Vec<u32> = if working.is_empty() { vec![1, 2, 3, 4, 5] } else { working.to_vec() };
    let (sh, eh) = if end_h > start_h { (start_h, end_h) } else { (8, 20) };
    let mut out = vec![];
    if dur_ms <= 0 { return out; }
    let mut day = now.date();
    let mut days = 0;
    for _ in 0..366 {
        if days >= SEARCH_WORKING_DAYS { break; }
        if working.contains(&day.weekday().num_days_from_sunday()) {
            days += 1;
            let day_end = day.and_hms_opt(0, 0, 0).unwrap() + Duration::hours(eh);
            let mut m = sh * 60;
            while m < eh * 60 {
                let s = day.and_hms_opt(0, 0, 0).unwrap() + Duration::minutes(m);
                let e = s + Duration::milliseconds(dur_ms);
                m += GRID_MINUTES;
                if e > day_end { break; }
                if s < now { continue; }
                let (bs, be) = (ms(s), ms(e));
                if busy.iter().any(|(a, b)| *a < be && *b > bs) { continue; }
                out.push((s, e));
                if out.len() >= max { return out; }
            }
        }
        day += Duration::days(1);
    }
    out
}

/// "Suggest times": the next free slots for the organizer and every guest Barbican can check.
pub fn find(u: &AppWindow, c: Arc<Client>) {
    let x = u.global::<crate::CalX>();
    x.set_ed_find_open(true);
    if !c.has("urn:ietf:params:jmap:principals:availability") {
        x.set_ed_find_status("This server doesn't share free/busy times.".into());
        return;
    }
    let Some((s, e)) = form_times(u) else { x.set_ed_find_status("Fix the date and time first.".into()); return };
    let tz = x.get_ed_tz().to_string();
    let zone = if u.get_ev_all_day() || tz.is_empty() { None } else { Some(tz) };
    let dur = if u.get_ev_all_day() { 3_600_000 } else { (recurrence::zone_to_utc_ms(e, zone.as_deref()) - recurrence::zone_to_utc_ms(s, zone.as_deref())).max(15 * 60_000) };
    let guests: Vec<Person> = ED.with(|e| e.borrow().attendees.clone());
    let me = me(&c);
    let own_window = ED.with(|e| {
        let e = e.borrow();
        e.event.as_ref().zip(e.occ_start.zip(e.occ_end)).map(|(ev, (a, b))| {
            let z = if ev["showWithoutTime"] == true { None } else { ev["timeZone"].as_str() };
            (recurrence::zone_to_utc_ms(a, z), recurrence::zone_to_utc_ms(b, z))
        })
    });
    let p = crate::settings::Settings::load();
    let working: Vec<u32> = p.prefs.get("calendarWorkingDays").and_then(|v| v.as_array()).map(|a| a.iter().filter_map(|d| d.as_u64()).map(|d| d as u32 % 7).collect()).unwrap_or_default();
    let (sh, eh) = (p.int("calendarDayStartHour", 8), p.int("calendarDayEndHour", 20));
    x.set_ed_find_busy(true);
    x.set_ed_find_status("Checking calendars…".into());
    let gen = ED.with(|e| e.borrow().gen);
    let w = u.as_weak();
    std::thread::spawn(move || {
        let now = Local::now().naive_local();
        let window_end = now + Duration::days(20);
        let (from, to) = (recurrence::zone_to_utc_ms(now, None), recurrence::zone_to_utc_ms(window_end, None));
        let others: Vec<Person> = guests.into_iter().filter(|g| !is_me(&g.email, &me)).collect();
        let emails: Vec<String> = others.iter().map(|g| g.email.clone()).collect();
        let (ids, refused) = principal_ids(&c, &emails);
        let own_id = c.account_for("urn:ietf:params:jmap:principals");
        let mut all_ids = vec![own_id.clone()];
        all_ids.extend(ids.values().cloned());
        let answers = availability(&c, &all_ids, from, to);
        let mut busy: Vec<(i64, i64)> = vec![];
        let mut not_checked: Vec<String> = vec![];
        let mut status = String::new();
        if let Some(Err(t)) = answers.get(&own_id) { if t == "forbidden" { status = "Your server doesn't let you look up free/busy times.".into(); } }
        if answers.values().any(|a| matches!(a, Err(t) if t == "rateLimit")) { status = "Too many look-ups right now. Try again in a minute.".into(); }
        if status.is_empty() {
            if let Some(Ok(p)) = answers.get(&own_id) { busy.extend(p.iter().map(|(a, b, _)| (*a, *b))); }
            for g in &others {
                match ids.get(&g.email.to_lowercase()).and_then(|id| answers.get(id)) {
                    Some(Ok(p)) => busy.extend(p.iter().map(|(a, b, _)| (*a, *b))),
                    _ => not_checked.push(if g.name.is_empty() { g.email.clone() } else { g.name.clone() }),
                }
            }
            // the event's own saved time isn't busy for itself
            if let Some((a, b)) = own_window { busy.retain(|(x, y)| !(*x == a && *y == b)); }
        }
        let slots = if status.is_empty() { find_free_slots(now, dur, &working, sh, eh, &busy, MAX_SUGGESTIONS) } else { vec![] };
        if status.is_empty() {
            status = if slots.is_empty() { "No free time found in the next two working weeks.".into() } else { String::new() };
            if !not_checked.is_empty() {
                if !status.is_empty() { status.push(' '); }
                status += &format!("Not checked{}: {}.", if refused { " (directory look-ups are off)" } else { "" }, not_checked.join(", "));
            }
        }
        let _ = w.upgrade_in_event_loop(move |u| {
            if ED.with(|e| e.borrow().gen) != gen { return; }
            let h12 = calendar::h12();
            let rows: Vec<CxSlot> = slots.iter().map(|(s, e)| CxSlot {
                label: s.format("%a %b %-d").to_string().into(),
                sub: format!("{} – {}", calendar::fmt_time(*s, h12), calendar::fmt_time(*e, h12)).into(),
            }).collect();
            ED.with(|e| e.borrow_mut().slots = slots);
            let x = u.global::<crate::CalX>();
            x.set_ed_find_busy(false);
            x.set_ed_find_status(status.into());
            x.set_ed_find_slots(ModelRc::new(VecModel::from(rows)));
        });
    });
}

/// A suggested slot moves the event there (on the form's clock).
pub fn pick_slot(u: &AppWindow, i: i32) {
    let Some((s, e)) = ED.with(|ed| ed.borrow().slots.get(i.max(0) as usize).copied()) else { return };
    let x = u.global::<crate::CalX>();
    let tz = x.get_ed_tz().to_string();
    let zone = if tz.is_empty() { None } else { Some(tz.as_str()) };
    let (zs, ze) = (recurrence::local_to_zone(s, zone), recurrence::local_to_zone(e, zone));
    u.set_ev_all_day(false);
    u.set_ev_date(zs.format("%Y-%m-%d").to_string().into());
    u.set_ev_start(zs.format("%H:%M").to_string().into());
    x.set_ed_end_date(ze.format("%Y-%m-%d").to_string().into());
    u.set_ev_end(ze.format("%H:%M").to_string().into());
    ED.with(|ed| { let mut ed = ed.borrow_mut(); ed.prev_start = Some(zs); ed.prev_end = Some(ze); });
    x.set_ed_find_open(false);
    update_your_time(u);
    if let Some(c) = crate::cal_extra::client() { refresh_availability(u, c); }
}

// ---------------- quick create ----------------

/// A click on an empty slot: `day` is "#N" (day column N) or a date; hour < 0 is a whole day.
pub fn quick(u: &AppWindow, day: &str, hour: i32, minute: i32, x: f32, y: f32) {
    let date = match day.strip_prefix('#') { Some(n) => n.parse().ok().and_then(calendar::column_date), None => NaiveDate::parse_from_str(day, "%Y-%m-%d").ok() };
    let Some(date) = date else { return };
    let all_day = hour < 0;
    let start = date.and_hms_opt(if all_day { 0 } else { hour.clamp(0, 23) as u32 }, if all_day { 0 } else { minute.clamp(0, 59) as u32 }, 0).unwrap();
    let end = if all_day { start } else { start + Duration::hours(1) };
    let cals = calendar::calendars();
    let usable: Vec<&Value> = cals.iter().filter(|c| writable(c)).collect();
    if usable.is_empty() { return; }
    let names: Vec<slint::SharedString> = usable.iter().map(|c| c["name"].as_str().unwrap_or("Calendar").into()).collect();
    let def = usable.iter().position(|c| c["isDefault"] == true && c["_shared"] != true).unwrap_or(0) as i32;
    QC.with(|q| *q.borrow_mut() = Quick { start: Some(start), end: Some(end), all_day, calendars: usable.iter().map(|c| c["id"].as_str().unwrap_or("").to_string()).collect() });
    let h12 = calendar::h12();
    let label = if all_day { format!("{} · All day", date.format("%A, %B %-d")) } else { format!("{} · {} – {}", date.format("%A, %B %-d"), calendar::fmt_time(start, h12), calendar::fmt_time(end, h12)) };
    let cx = u.global::<crate::CalX>();
    cx.set_qc_title("".into());
    cx.set_qc_label(label.into());
    cx.set_qc_calendars(ModelRc::new(VecModel::from(names)));
    cx.set_qc_calendar(def);
    cx.set_qc_x(x + 12.0);
    cx.set_qc_y(y - 20.0);
    cx.set_qc_open(true);
}

pub fn qc_save(u: &AppWindow, c: Arc<Client>) {
    let x = u.global::<crate::CalX>();
    let q = QC.with(|q| std::mem::take(&mut *q.borrow_mut()));
    let (Some(start), Some(end)) = (q.start, q.end) else { x.set_qc_open(false); return };
    let Some(cal) = q.calendars.get(x.get_qc_calendar().max(0) as usize).cloned() else { return };
    let mut ev = json!({"@type": "Event", "uid": calendar::new_uid(), "title": x.get_qc_title().trim(), "start": fmt_local(start),
        "duration": if q.all_day { "P1D".to_string() } else { duration_string((end - start).num_seconds()) }, "showWithoutTime": q.all_day,
        "status": "confirmed", "freeBusyStatus": if q.all_day { "free" } else { "busy" }, "privacy": "public", "useDefaultAlerts": true});
    if !q.all_day { ev["timeZone"] = json!(crate::agent::local_tz()); }
    x.set_qc_open(false);
    calendar::create_in(u, c.clone(), &cal, ev, "Event not saved");
    reminders::changed(u.as_weak(), c);
}

/// "More options": the full editor with what the popover holds.
pub fn qc_more(u: &AppWindow) {
    let x = u.global::<crate::CalX>();
    let q = QC.with(|q| std::mem::take(&mut *q.borrow_mut()));
    x.set_qc_open(false);
    let Some(start) = q.start else { return };
    let cal = q.calendars.get(x.get_qc_calendar().max(0) as usize).cloned();
    open_new(u, start.date(), start.hour() as i32, start.minute() as i32, q.all_day, x.get_qc_title().as_str());
    if let Some(cal) = cal {
        if let Some(i) = calendar::calendars().iter().position(|c| c["id"] == cal.as_str()) { u.set_ev_calendar(i as i32); }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn p(n: &str, e: &str) -> Person { Person { name: n.into(), email: e.into() } }
    fn dt(s: &str) -> NaiveDateTime { parse_local(s).unwrap() }

    fn form() -> Form {
        Form { title: " Review ".into(), description: "".into(), location: "".into(), start: dt("2026-11-20T10:00:00"), end: dt("2026-11-20T11:30:00"),
            all_day: false, tz: Some("Europe/Berlin".into()), calendar: "a".into(), repeat: 0, custom: None, use_default_alerts: true, default_alerts: None,
            rows: vec![], kept: Map::new(), reserved_alert_ids: vec![], attendees: vec![], send: true, organizer: true, freebusy: 0, privacy: Some(0),
            me: vec!["me@x.org".into()] }
    }

    #[test]
    fn new_event_data() {
        let (d, send) = build_data(&form(), None, None);
        assert_eq!(d["title"], "Review");
        assert_eq!(d["start"], "2026-11-20T10:00:00");
        assert_eq!(d["duration"], "PT1H30M");
        assert_eq!(d["timeZone"], "Europe/Berlin");
        assert_eq!(d["status"], "confirmed");
        assert_eq!(d["useDefaultAlerts"], true);
        assert!(d.get("alerts").is_none() && d.get("participants").is_none() && d.get("description").is_none());
        assert!(d["uid"].as_str().unwrap().len() >= 32);
        assert!(!send);
        // an end before the start becomes an hour
        let mut f = form();
        f.end = dt("2026-11-20T09:00:00");
        assert_eq!(build_data(&f, None, None).0["duration"], "PT1H");
        // all day over three days
        let mut f = form();
        f.all_day = true;
        f.end = dt("2026-11-22T00:00:00");
        let (d, _) = build_data(&f, None, None);
        assert_eq!(d["duration"], "P3D");
        assert_eq!(d["start"], "2026-11-20T00:00:00");
        assert!(d["timeZone"].is_null());
    }

    #[test]
    fn dst_aware_duration() {
        // Europe/Berlin leaves DST on 2026-10-25 at 03:00: 01:00 -> 04:00 wall is 4 real hours
        let mut f = form();
        f.start = dt("2026-10-25T01:00:00");
        f.end = dt("2026-10-25T04:00:00");
        assert_eq!(build_data(&f, None, None).0["duration"], "PT4H");
    }

    #[test]
    fn guests_rules_alerts_location() {
        let mut f = form();
        f.attendees = vec![p("Ann", "ann@x.org"), p("", "ANN@x.org"), p("", "bob@y.org")];
        f.repeat = 5;
        f.custom = Some(Rule::parse(&json!({"frequency": "weekly", "interval": 2, "byDay": [{"day": "fr"}]})));
        f.use_default_alerts = false;
        f.rows = vec![EdRow { row: Row { value: 15, unit: 1, action: 0 }, source: None }];
        f.location = "https://meet.example/abc".into();
        f.description = "Agenda".into();
        let (d, send) = build_data(&f, None, None);
        assert!(send);
        let ps = d["participants"].as_object().unwrap();
        assert_eq!(ps.len(), 3);
        let org = ps.values().find(|p| p["roles"]["owner"] == true).unwrap();
        assert_eq!(org["email"], "me@x.org");
        assert_eq!(org["participationStatus"], "accepted");
        assert!(org.get("name").is_none());
        assert_eq!(d["organizerCalendarAddress"], "mailto:me@x.org");
        assert_eq!(d["recurrenceRules"][0]["interval"], 2);
        assert_eq!(d["useDefaultAlerts"], false);
        assert_eq!(d["alerts"].as_object().unwrap().values().next().unwrap()["trigger"]["offset"], "-PT15M");
        assert_eq!(d["virtualLocations"]["vl1"]["uri"], "https://meet.example/abc");
        assert_eq!(d["description"], "Agenda");
    }

    #[test]
    fn edits_keep_ids_answers_and_unchanged_fields() {
        let ev = json!({"id": "e", "title": "Review", "start": "2026-11-20T10:00:00", "duration": "PT1H30M", "timeZone": "Europe/Berlin",
            "freeBusyStatus": "busy", "privacy": "public", "organizerCalendarAddress": "mailto:me@x.org", "useDefaultAlerts": true,
            "locations": {"loc": {"@type": "Location", "name": "Room 1", "coordinates": "geo:1,2"}},
            "participants": {"o": {"email": "me@x.org", "roles": {"owner": true}, "participationStatus": "accepted"},
                "p1": {"email": "ann@x.org", "roles": {"attendee": true}, "participationStatus": "accepted"}}});
        let mut f = form();
        f.attendees = vec![p("", "ann@x.org")];
        f.location = "Room 2".into();
        let (d, _) = build_data(&f, Some(&ev), None);
        // not moved: Ann keeps her id and answer, the place its id and coordinates
        assert_eq!(d["participants"]["p1"]["participationStatus"], "accepted");
        assert!(d["participants"].get("o").is_some());
        assert_eq!(d["locations"]["loc"]["name"], "Room 2");
        assert_eq!(d["locations"]["loc"]["coordinates"], "geo:1,2");
        assert!(d.get("freeBusyStatus").is_none() && d.get("status").is_none() && d.get("uid").is_none());
        assert!(d.get("organizerCalendarAddress").is_none());
        // moved by the organizer: asked again
        let mut f2 = f.clone();
        f2.start = dt("2026-11-21T10:00:00");
        f2.end = dt("2026-11-21T11:30:00");
        let (d, _) = build_data(&f2, Some(&ev), None);
        assert_eq!(d["participants"]["p1"]["participationStatus"], "needs-action");
        // guests all removed: participants and organizer cleared
        let mut f3 = form();
        f3.location = "Room 1".into();
        let (d, _) = build_data(&f3, Some(&ev), None);
        assert!(d["participants"].is_null() && d["organizerCalendarAddress"].is_null());
        assert!(d.get("locations").is_none());
        // a repeat removed takes the overrides along
        let mut series = ev.clone();
        series["recurrenceRules"] = json!([{"frequency": "daily"}]);
        series["recurrenceOverrides"] = json!({"2026-11-21T10:00:00": {"excluded": true}});
        let (d, _) = build_data(&f3, Some(&series), None);
        assert!(d["recurrenceRules"].is_null() && d["recurrenceOverrides"].is_null());
    }

    #[test]
    fn organizer_detection() {
        let me = vec!["me@x.org".to_string()];
        assert!(user_organizes(&json!({}), &me));
        assert!(user_organizes(&json!({"participants": {"a": {"email": "ME@x.org", "roles": {"owner": true}}}}), &me));
        assert!(!user_organizes(&json!({"organizerCalendarAddress": "mailto:boss@y.org"}), &me));
        assert!(user_organizes(&json!({"organizerCalendarAddress": "mailto:boss@y.org", "isOrigin": true}), &me));
        let e = json!({"participants": {"a": {"calendarAddress": "mailto:boss@y.org", "roles": {"owner": true}, "name": "Boss"}, "b": {"email": "ann@x.org"}, "c": {"email": "ANN@x.org"}}});
        assert_eq!(event_organizer(&e), Some(p("Boss", "boss@y.org")));
        assert_eq!(attendees_of(&e), vec![p("", "ann@x.org")]);
        let again = asked_again(&json!({"o": {"email": "boss@y.org", "roles": {"owner": true}, "participationStatus": "accepted"},
            "m": {"email": "me@x.org", "participationStatus": "accepted"}, "a": {"email": "ann@x.org", "participationStatus": "declined"}}), &me);
        assert_eq!(again["o"]["participationStatus"], "accepted");
        assert_eq!(again["m"]["participationStatus"], "accepted");
        assert_eq!(again["a"]["participationStatus"], "needs-action");
    }

    #[test]
    fn scope_patches() {
        let master = json!({"id": "s", "title": "Stand-up", "start": "2026-11-02T09:00:00", "duration": "PT15M", "timeZone": "UTC",
            "recurrenceRules": [{"frequency": "daily", "count": 10}], "uid": "u1",
            "participants": {"o": {"email": "me@x.org", "roles": {"owner": true}, "participationStatus": "accepted"}, "a": {"email": "ann@x.org", "participationStatus": "accepted"}}});
        let mut data = Map::new();
        data.insert("title".into(), json!("Stand-up (moved)"));
        data.insert("duration".into(), json!("PT15M"));
        // this occurrence: an override under its recurrence id, the whole map when there is none
        let ov = occurrence_override(&master, "2026-11-04T09:00:00", &data, dt("2026-11-04T10:00:00"));
        assert_eq!(ov["title"], "Stand-up (moved)");
        assert_eq!(ov["start"], "2026-11-04T10:00:00");
        assert!(ov.get("duration").is_none());
        let p = override_patch(&master, "2026-11-04T09:00:00", ov.clone());
        assert!(p["recurrenceOverrides"]["2026-11-04T09:00:00"].is_object());
        let mut with = master.clone();
        with["recurrenceOverrides"] = json!({"2026-11-03T09:00:00": {"excluded": true}});
        assert!(override_patch(&with, "2026-11-04T09:00:00", ov).contains_key("recurrenceOverrides/2026-11-04T09:00:00"));
        // following: the series ends a second before, overrides from there on go
        let mut with2 = master.clone();
        with2["recurrenceOverrides"] = json!({"2026-11-03T09:00:00": {"title": "x"}, "2026-11-06T09:00:00": {"excluded": true}});
        let (t, restore) = truncate_patch(&with2, "2026-11-05T09:00:00");
        assert_eq!(t["recurrenceRules"][0]["until"], "2026-11-05T08:59:59");
        assert!(t["recurrenceRules"][0].get("count").is_none());
        assert!(t.contains_key("recurrenceOverrides/2026-11-06T09:00:00") && !t.contains_key("recurrenceOverrides/2026-11-03T09:00:00"));
        assert_eq!(restore["recurrenceRules"][0]["count"], 10);
        let n = following_series(&master, "2026-11-05T09:00:00", &data, &["me@x.org".into()]);
        assert_eq!(n["recurrenceRules"][0]["count"], 7);
        assert_eq!(n["title"], "Stand-up (moved)");
        assert_ne!(n["uid"], "u1");
        assert_eq!(n["participants"]["a"]["participationStatus"], "needs-action");
        assert_eq!(n["start"], "2026-11-05T09:00:00");
        // all: the series moves as far as the occurrence did; an untouched repeat stays out
        let mut d2 = data.clone();
        d2.insert("start".into(), json!("2026-11-04T09:30:00"));
        d2.insert("recurrenceRules".into(), json!([{"frequency": "daily", "count": 10, "interval": 1}]));
        let s = series_update(&master, dt("2026-11-04T09:00:00"), &d2);
        assert_eq!(s["start"], "2026-11-02T09:30:00");
        assert!(s.get("recurrenceRules").is_none());
    }

    #[test]
    fn free_slots_skip_busy_and_off_hours() {
        // Monday 2026-11-16 08:10 local
        let now = dt("2026-11-16T08:10:00");
        let ms = |s: &str| recurrence::zone_to_utc_ms(dt(s), None);
        let busy = vec![(ms("2026-11-16T08:30:00"), ms("2026-11-16T10:00:00"))];
        let slots = find_free_slots(now, 3_600_000, &[1, 2, 3, 4, 5], 8, 20, &busy, 3);
        let s: Vec<String> = slots.iter().map(|(a, _)| a.format("%d %H:%M").to_string()).collect();
        assert_eq!(s, ["16 10:00", "16 10:30", "16 11:00"]);
        // the last slot of a day ends by its end hour; weekends are skipped
        let late = dt("2026-11-20T19:10:00");
        let s = find_free_slots(late, 3_600_000, &[1, 2, 3, 4, 5], 8, 20, &[], 1);
        assert_eq!(s[0].0, dt("2026-11-23T08:00:00"));
        assert_eq!(availability_for(&[(0, 10, true)], 5, 20), ("Tentative", 2));
        assert_eq!(availability_for(&[(0, 10, true), (12, 30, false)], 5, 20), ("Busy", 3));
        assert_eq!(availability_for(&[(30, 40, false)], 5, 20), ("Free", 1));
    }

    #[test]
    fn people_parsing() {
        assert_eq!(parse_person("Ann Lee <ann@x.org>"), Some(p("Ann Lee", "ann@x.org")));
        assert_eq!(parse_person(" bob@y.org "), Some(p("", "bob@y.org")));
        assert_eq!(parse_person("not an address"), None);
        assert_eq!(parse_person("a@b"), None);
        assert_eq!(initials("Ann Lee", "x"), "AL");
        assert_eq!(initials("", "bob.smith@y.org"), "BS");
    }
}
