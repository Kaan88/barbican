//! Calendar invitations in mail (Bulwark lib/calendar-invitation.ts, calendar-rsvp.ts and
//! calendar-invitation-banner.tsx): find the text/calendar part, read it with
//! `CalendarEvent/parse` (JMAP Calendars), work out the iTIP method, summarise the event and
//! answer it. The answer goes through the calendar copy: the stored event with that uid when
//! there is one, else the invitation is added to the default calendar first; the participant's
//! participationStatus is then patched with `sendSchedulingMessages`, so the server sends the
//! iTIP REPLY (as calendar::rsvp does for events already in the calendar).
use serde_json::{json, Value};

pub const CAP: &str = "urn:ietf:params:jmap:calendars";

#[derive(Clone, Debug, PartialEq)]
pub enum Method { Publish, Request, Reply, Add, Cancel, Refresh, Counter, DeclineCounter, Unknown }

impl Method {
    pub fn from(s: &str) -> Method {
        match s.trim().to_ascii_lowercase().as_str() {
            "publish" => Method::Publish, "request" => Method::Request, "reply" => Method::Reply, "add" => Method::Add,
            "cancel" => Method::Cancel, "refresh" => Method::Refresh, "counter" => Method::Counter,
            "declinecounter" => Method::DeclineCounter, _ => Method::Unknown,
        }
    }
}

fn is_calendar_type(t: &str) -> bool {
    let m = t.split(';').next().unwrap_or("").trim().to_ascii_lowercase();
    m == "text/calendar" || m == "application/ics" || m == "application/icalendar"
}

fn is_calendar_part(p: &Value) -> bool {
    let name = p["name"].as_str().unwrap_or("").to_lowercase();
    is_calendar_type(p["type"].as_str().unwrap_or("")) || name.ends_with(".ics") || name.ends_with(".ical")
}

/// The message's calendar part: an attachment first, then a body part (Bulwark findCalendarAttachment).
pub fn find_part(m: &Value) -> Option<Value> {
    fn walk(parts: &Value) -> Option<Value> {
        for p in parts.as_array()? {
            if is_calendar_part(p) && p["blobId"].is_string() { return Some(p.clone()); }
            if let Some(n) = walk(&p["subParts"]) { return Some(n); }
        }
        None
    }
    m["attachments"].as_array().and_then(|a| a.iter().find(|p| is_calendar_part(p) && p["blobId"].is_string()).cloned())
        .or_else(|| walk(&m["textBody"])).or_else(|| walk(&m["htmlBody"]))
}

/// METHOD of the raw .ics (JMAP drops the Content-Type parameters, so this is the reliable source).
pub fn method_from_ics(raw: &str) -> Method {
    for line in raw.lines() {
        let l = line.trim_end_matches('\r');
        let Some(rest) = l.get(..6).filter(|p| p.eq_ignore_ascii_case("METHOD")).map(|_| &l[6..]) else { continue };
        let value = match rest.find(':') { Some(i) if rest.starts_with(':') || rest.starts_with(';') => &rest[i + 1..], _ => continue };
        let token: String = value.trim().chars().take_while(|c| c.is_ascii_alphabetic() || *c == '-').collect();
        if !token.is_empty() { return Method::from(&token); }
    }
    Method::Unknown
}

fn norm_addr(v: &str) -> String {
    let v = v.trim();
    let v = if v.len() >= 7 && v[..7].eq_ignore_ascii_case("mailto:") { &v[7..] } else { v };
    v.to_lowercase()
}

/// A participant's address: email, calendarAddress (Stalwart) or a sendTo value.
pub fn participant_email(p: &Value) -> Option<String> {
    if let Some(e) = p["email"].as_str().map(norm_addr).filter(|e| !e.is_empty()) { return Some(e); }
    if let Some(e) = p["calendarAddress"].as_str().map(norm_addr).filter(|e| !e.is_empty()) { return Some(e); }
    p["sendTo"].as_object().and_then(|o| o.values().filter_map(|v| v.as_str()).map(norm_addr).find(|e| !e.is_empty()))
}

fn is_organizer(p: &Value) -> bool { p["roles"]["owner"] == true || p["roles"]["chair"] == true }

/// The organizer's address: an owner/chair participant, else organizerCalendarAddress.
pub fn organizer(ev: &Value) -> Option<(String, String)> {
    let ps = ev["participants"].as_object();
    let by_role = ps.and_then(|o| o.values().find(|p| is_organizer(p)));
    let by_addr = ev["organizerCalendarAddress"].as_str().and_then(|a| ps.and_then(|o| o.values().find(|p| p["calendarAddress"].as_str() == Some(a))));
    if let Some(p) = by_role.or(by_addr) {
        let e = participant_email(p).unwrap_or_default();
        return Some((p["name"].as_str().filter(|n| !n.is_empty()).map(String::from).unwrap_or_else(|| e.clone()), e));
    }
    ev["organizerCalendarAddress"].as_str().map(norm_addr).map(|e| (e.clone(), e))
}

/// The user's own participant (id, status) unless the user only organises the event.
pub fn my_participant(ev: &Value, mine: &[String]) -> Option<(String, String)> {
    let mine: Vec<String> = mine.iter().map(|m| m.to_lowercase()).collect();
    ev["participants"].as_object()?.iter().find(|(_, p)| {
        participant_email(p).is_some_and(|e| mine.contains(&e)) && !(is_organizer(p) && p["roles"]["attendee"] != true)
    }).map(|(id, p)| (id.clone(), p["participationStatus"].as_str().unwrap_or("needs-action").to_string()))
}

/// Whether the user organises it.
pub fn i_organize(ev: &Value, mine: &[String]) -> bool {
    organizer(ev).is_some_and(|(_, e)| mine.iter().any(|m| m.eq_ignore_ascii_case(&e)))
}

fn looks_like_reply(ev: &Value) -> bool {
    ev["participants"].as_object().is_some_and(|o| o.values().any(|p| p["roles"]["attendee"] == true && !is_organizer(p)
        && (p["participationStatus"].as_str().is_some_and(|s| s != "needs-action") || p["participationComment"].is_string())))
}

/// The iTIP method: the event's own, then the .ics METHOD, then a guess from its contents
/// (Bulwark getInvitationMethod; the MIME method parameter is gone in JMAP).
pub fn method(ev: &Value, raw_ics: Option<&str>) -> Method {
    let m = Method::from(ev["method"].as_str().unwrap_or(""));
    if m != Method::Unknown { return m; }
    if let Some(m) = raw_ics.map(method_from_ics).filter(|m| *m != Method::Unknown) { return m; }
    if ev["status"] == "cancelled" { return Method::Cancel; }
    if looks_like_reply(ev) { return Method::Reply; }
    if ev["participants"].as_object().is_some_and(|o| o.values().any(is_organizer)) { return Method::Request; }
    Method::Unknown
}

/// ISO 8601 duration (PnW / PnDTnHnMnS) in seconds.
pub fn duration_secs(d: &str) -> Option<i64> {
    let d = d.strip_prefix('-').unwrap_or(d);
    let rest = d.strip_prefix('P')?;
    let (date, time) = match rest.find('T') { Some(i) => (&rest[..i], &rest[i + 1..]), None => (rest, "") };
    let mut total = 0i64;
    let mut num = String::new();
    for c in date.chars() {
        if c.is_ascii_digit() { num.push(c); continue; }
        let n: i64 = num.parse().ok()?;
        num.clear();
        total += n * match c { 'W' => 604_800, 'D' => 86_400, _ => return None };
    }
    for c in time.chars() {
        if c.is_ascii_digit() { num.push(c); continue; }
        let n: i64 = num.parse().ok()?;
        num.clear();
        total += n * match c { 'H' => 3600, 'M' => 60, 'S' => 1, _ => return None };
    }
    Some(total)
}

/// "Mon 17 Nov 2026, 10:00 – 11:00 (Europe/Berlin)". UTC events show in local time; events in
/// another zone keep their wall clock and name the zone (no tz database here).
pub fn when(ev: &Value, local_zone: &str) -> String {
    use chrono::{Duration, NaiveDateTime, TimeZone};
    let Some(start) = ev["start"].as_str().and_then(|s| NaiveDateTime::parse_from_str(s.get(..19).unwrap_or(s), "%Y-%m-%dT%H:%M:%S").ok()) else { return String::new() };
    let dur = ev["duration"].as_str().and_then(duration_secs).unwrap_or(0);
    let all_day = ev["showWithoutTime"] == true;
    let tz = ev["timeZone"].as_str().unwrap_or("");
    let (s, zone) = if tz.eq_ignore_ascii_case("utc") || tz.eq_ignore_ascii_case("etc/utc") || ev["start"].as_str().is_some_and(|x| x.ends_with('Z')) {
        (chrono::Utc.from_utc_datetime(&start).with_timezone(&chrono::Local).naive_local(), String::new())
    } else if tz.is_empty() || tz == local_zone {
        (start, String::new())
    } else { (start, format!(" ({tz})")) };
    let e = s + Duration::seconds(dur);
    if all_day {
        let last = if dur >= 86_400 { e - Duration::days(1) } else { s };
        return if last.date() > s.date() { format!("{} – {}", s.format("%a %d %b %Y"), last.format("%a %d %b %Y")) } else { format!("{}, all day", s.format("%a %d %b %Y")) };
    }
    if dur == 0 { return format!("{}{zone}", s.format("%a %d %b %Y, %H:%M")); }
    if e.date() == s.date() { format!("{} – {}{zone}", s.format("%a %d %b %Y, %H:%M"), e.format("%H:%M")) }
    else { format!("{} – {}{zone}", s.format("%a %d %b %Y, %H:%M"), e.format("%a %d %b, %H:%M")) }
}

/// The event's place: the first location's name (or its description).
pub fn location(ev: &Value) -> String {
    ev["locations"].as_object().and_then(|o| o.values().find_map(|l| l["name"].as_str().or(l["description"].as_str()).filter(|s| !s.trim().is_empty())))
        .map(|s| s.trim().to_string()).unwrap_or_default()
}

pub fn status_label(s: &str) -> &'static str {
    match s { "accepted" => "Accepted", "tentative" => "Tentative", "declined" => "Declined", "delegated" => "Delegated", _ => "Needs response" }
}

/// The event as CalendarEvent/set creates it: what parse returned without server-set ids.
pub fn importable(ev: &Value, calendar_id: &str) -> Value {
    let mut e = ev.clone();
    if let Some(o) = e.as_object_mut() {
        for k in ["id", "method", "blobId", "baseEventId", "calendarIds", "isOrigin", "utcStart", "utcEnd"] { o.remove(k); }
    }
    e["calendarIds"] = json!({ calendar_id: true });
    e
}

/// The patch that answers as participant `pid`.
pub fn rsvp_patch(pid: &str, status: &str) -> Value {
    let esc = pid.replace('~', "~0").replace('/', "~1");
    let mut p = serde_json::Map::new();
    p.insert(format!("participants/{esc}/participationStatus"), json!(status));
    Value::Object(p)
}

/// Parse the calendar part of a message. Returns the first event and the raw method.
pub fn parse(c: &crate::jmap::Client, blob: &str) -> Result<(Value, Method), String> {
    let acc = c.account_for(CAP);
    let r = c.call(json!([["CalendarEvent/parse", {"accountId": acc, "blobIds": [blob]}, "p"]]))?;
    if r[0][0] == "error" { return Err(crate::jmap::method_error(&r[0][1])); }
    if r[0][1]["notParsable"].as_array().is_some_and(|a| a.iter().any(|x| x == blob)) { return Err("Could not read invitation".into()); }
    let parsed = &r[0][1]["parsed"][blob];
    let ev = match parsed { Value::Array(a) => a.first().cloned(), Value::Object(_) => Some(parsed.clone()), _ => None }.ok_or("Could not read invitation")?;
    // the .ics METHOD line (the mail account holds the blob)
    let raw = c.download(blob, "invite.ics", "text/calendar").ok().map(|b| String::from_utf8_lossy(&b).to_string());
    let m = method(&ev, raw.as_deref());
    Ok((ev, m))
}

/// The stored copy of an event with this uid in the user's calendars (id, participants).
pub fn stored(c: &crate::jmap::Client, uid: &str) -> Result<Option<Value>, String> {
    let acc = c.account_for(CAP);
    let r = c.call(json!([
        ["CalendarEvent/query", {"accountId": acc, "filter": {"uid": uid}, "limit": 5}, "q"],
        ["CalendarEvent/get", {"accountId": acc, "#ids": {"resultOf": "q", "name": "CalendarEvent/query", "path": "/ids"}, "properties": ["id", "uid", "participants", "organizerCalendarAddress", "recurrenceId"]}, "g"]
    ]))?;
    if r[0][0] == "error" { return Err(crate::jmap::method_error(&r[0][1])); }
    let list = r[1][1]["list"].as_array().cloned().unwrap_or_default();
    Ok(list.iter().find(|e| e["recurrenceId"].is_null()).or(list.first()).cloned())
}

/// Answer: on the stored copy, else add the invitation to the default calendar and answer there.
/// Returns the event id answered.
pub fn answer(c: &crate::jmap::Client, ev: &Value, mine: &[String], status: &str) -> Result<String, String> {
    let acc = c.account_for(CAP);
    let uid = ev["uid"].as_str().unwrap_or("");
    let (eid, pid) = match stored(c, uid)? {
        Some(s) => {
            let pid = my_participant(&s, mine).or_else(|| my_participant(ev, mine)).map(|p| p.0).ok_or("You are not among this event's participants.")?;
            (s["id"].as_str().unwrap_or("").to_string(), pid)
        }
        None => {
            let pid = my_participant(ev, mine).map(|p| p.0).ok_or("You are not among this event's participants.")?;
            let cals = c.call(json!([["Calendar/get", {"accountId": acc, "ids": null, "properties": ["id", "isDefault", "myRights"]}, "c"]]))?;
            let list = cals[0][1]["list"].as_array().cloned().unwrap_or_default();
            let cal = list.iter().find(|x| x["isDefault"] == true).or(list.first()).and_then(|x| x["id"].as_str()).ok_or("No calendar to add the event to.")?.to_string();
            // one call: the copy is created with the answer already in it and the server sends the
            // reply, so a failure leaves nothing behind in the calendar
            let mut e = importable(ev, &cal);
            if let Some(p) = e["participants"].get_mut(&pid) { p["participationStatus"] = json!(status); }
            let r = c.call(json!([["CalendarEvent/set", {"accountId": acc, "create": {"e": e}, "sendSchedulingMessages": true}, "s"]]))?;
            if r[0][0] == "error" { return Err(crate::jmap::method_error(&r[0][1])); }
            return r[0][1]["created"]["e"]["id"].as_str().map(String::from).ok_or_else(|| crate::jmap::set_error(&r[0][1]["notCreated"]["e"]));
        }
    };
    let r = c.call(json!([["CalendarEvent/set", {"accountId": acc, "update": { eid.clone(): rsvp_patch(&pid, status) }, "sendSchedulingMessages": true}, "s"]]))?;
    if r[0][0] == "error" { return Err(crate::jmap::method_error(&r[0][1])); }
    if let Some(e) = r[0][1]["notUpdated"].get(&eid) { return Err(crate::jmap::set_error(e)); }
    Ok(eid)
}

// ---------------------------------------------------------------- cancellations (G-002)

/// `b`'s recurrence id as a local date-time in the zone of `a`'s, each read in its own zone
/// (`recurrenceIdTimeZone`, else the event's `timeZone`); without a usable zone on either side
/// it is taken as it is (Bulwark lib/calendar-cancellation.ts:6-16 recurrenceIdInZoneOf).
fn recurrence_id_in_zone_of(a: &Value, b: &Value) -> Option<String> {
    let rid = b["recurrenceId"].as_str()?;
    let local = rid.get(..19).unwrap_or(rid);
    let zone = |e: &Value| e["recurrenceIdTimeZone"].as_str().or(e["timeZone"].as_str()).filter(|z| !z.is_empty()).map(String::from);
    Some(match (zone(a), zone(b)) {
        (Some(za), Some(zb)) => move_local(local, &zb, &za),
        _ => local.to_string(),
    })
}

/// A local date-time moved from one zone's wall clock to another's (Bulwark lib/timezone.ts:196
/// moveLocalDateTime); unusable input or zones leave it as it is.
fn move_local(value: &str, from: &str, to: &str) -> String {
    use chrono::{NaiveDateTime, TimeZone};
    use chrono_tz::Tz;
    if from == to { return value.to_string(); }
    let (Ok(f), Ok(t)) = (from.parse::<Tz>(), to.parse::<Tz>()) else { return value.to_string() };
    let Ok(n) = NaiveDateTime::parse_from_str(value, "%Y-%m-%dT%H:%M:%S") else { return value.to_string() };
    match f.from_local_datetime(&n).earliest() {
        Some(dt) => dt.with_timezone(&t).naive_local().format("%Y-%m-%dT%H:%M:%S").to_string(),
        None => value.to_string(),
    }
}

/// Whether two events name the same occurrence: their recurrence ids at the same moment, each in
/// its own zone (Bulwark sameRecurrenceId, calendar-cancellation.ts:19-23).
pub fn same_recurrence_id(a: &Value, b: &Value) -> bool {
    let (Some(ra), Some(_)) = (a["recurrenceId"].as_str(), b["recurrenceId"].as_str()) else { return false };
    Some(ra.get(..19).unwrap_or(ra).to_string()) == recurrence_id_in_zone_of(a, b)
}

/// What removing a cancelled meeting takes (Bulwark CancellationRemoval).
#[derive(Clone, Debug, PartialEq)]
pub enum Removal {
    /// Destroy these stored events (the series, or an occurrence stored on its own).
    Delete(Vec<String>),
    /// Write `patch` on the series `id`, taking one occurrence out.
    Exclude { id: String, patch: serde_json::Map<String, Value> },
    /// The calendar holds a newer version than the cancellation.
    Outdated,
    /// Nothing in the calendar to remove.
    Nothing,
}

fn sequence(e: &Value) -> i64 { e["sequence"].as_i64().unwrap_or(0) }

/// How to remove what an iTIP CANCEL (RFC 5546 3.2.5) names from `stored`, the events stored
/// with its uid (Bulwark planCancellationRemoval, calendar-cancellation.ts:50-86). Stalwart only
/// marks the event cancelled and keeps it. A cancellation without a recurrence id is for the
/// whole meeting, one with is for that occurrence; one with a lower SEQUENCE than the event it
/// names is older and is not applied.
pub fn plan_cancellation(stored: &[Value], cancel: &Value) -> Removal {
    let Some(uid) = cancel["uid"].as_str().filter(|u| !u.is_empty()) else { return Removal::Nothing };
    let same: Vec<&Value> = stored.iter().filter(|e| e["uid"].as_str() == Some(uid)).collect();
    let cs = sequence(cancel);
    let newer = |e: &Value| sequence(e) > cs;
    let id = |e: &Value| e["id"].as_str().unwrap_or("").to_string();
    if cancel["recurrenceId"].is_null() {
        if same.is_empty() { return Removal::Nothing; }
        if same.iter().any(|e| newer(e)) { return Removal::Outdated; }
        return Removal::Delete(same.iter().map(|e| id(e)).collect());
    }
    if let Some(own) = same.iter().find(|e| same_recurrence_id(e, cancel)) {
        return if newer(own) { Removal::Outdated } else { Removal::Delete(vec![id(own)]) };
    }
    let series = same.iter().find(|e| e["recurrenceId"].is_null() && e["recurrenceRules"].as_array().is_some_and(|r| !r.is_empty()));
    // overrides not read: a whole-map write would drop the ones there are
    let Some(series) = series.filter(|s| s.get("recurrenceOverrides").is_some()) else { return Removal::Nothing };
    let Some(key) = recurrence_id_in_zone_of(series, cancel) else { return Removal::Nothing };
    let ov = &series["recurrenceOverrides"][&key];
    if ov["excluded"] == true { return Removal::Nothing; }
    if ov.is_object() && newer(ov) { return Removal::Outdated; }
    Removal::Exclude { id: id(series), patch: crate::cal_editor::override_patch(series, &key, json!({"excluded": true})) }
}

/// The stored events a removal would delete or change.
pub fn removal_targets(stored: &[Value], r: &Removal) -> Vec<Value> {
    match r {
        Removal::Delete(ids) => stored.iter().filter(|e| e["id"].as_str().is_some_and(|i| ids.iter().any(|x| x == i))).cloned().collect(),
        Removal::Exclude { id, .. } => stored.iter().filter(|e| e["id"].as_str() == Some(id.as_str())).cloned().collect(),
        _ => vec![],
    }
}

/// A stored event's organizer: its ORGANIZER address first, as the server keeps it.
fn stored_organizer(e: &Value) -> Option<String> {
    e["organizerCalendarAddress"].as_str().map(norm_addr).filter(|a| !a.is_empty())
        .or_else(|| organizer(e).map(|o| o.1).filter(|a| !a.is_empty()))
}

/// Whether the sender is the organizer of every one of `events`, the stored copies a
/// cancellation would remove (Bulwark isFromOrganizerOf, calendar-invitation.ts:221): only the
/// organizer cancels a meeting, and the organizer inside the cancellation is the sender's own
/// text, so the stored events decide.
pub fn from_organizer_of(events: &[Value], sender: Option<&str>) -> bool {
    let Some(s) = sender.map(norm_addr).filter(|s| !s.is_empty()) else { return false };
    !events.is_empty() && events.iter().all(|e| stored_organizer(e).as_deref() == Some(s.as_str()))
}

/// What the banner knows about removing a cancelled event.
#[derive(Clone, Debug, PartialEq)]
pub struct CancelPlan {
    pub removal: Removal,
    pub from_organizer: bool,
    /// The stored organizer, for the notice that names both addresses.
    pub organizer: Option<String>,
}

impl CancelPlan {
    pub fn removable(&self) -> bool { matches!(self.removal, Removal::Delete(_) | Removal::Exclude { .. }) }
}

pub fn cancel_plan(stored: &[Value], cancel: &Value, sender: Option<&str>) -> CancelPlan {
    let removal = plan_cancellation(stored, cancel);
    let targets = removal_targets(stored, &removal);
    CancelPlan { from_organizer: from_organizer_of(&targets, sender), organizer: targets.iter().find_map(stored_organizer), removal }
}

/// The notice under a cancellation that may not remove the meeting (Bulwark locales
/// tTrust.cancel_not_organizer / cancel_unverified); None when the removal is offered.
pub fn cancel_notice(p: &CancelPlan, trusted: bool, sender: &str) -> Option<String> {
    if !p.removable() || (p.from_organizer && trusted) { return None; }
    Some(match (&p.organizer, p.from_organizer) {
        (Some(o), false) => format!("This cancellation was sent from {sender}, but the event in your calendar is organized by {o}. Only the organizer can cancel it. Remove it yourself if you trust this message."),
        _ => "This cancellation could not be verified as coming from the organizer. Remove the event yourself if you trust this message.".into(),
    })
}

/// The properties read for the stored copies of an invitation's event: everything the calendar
/// loads, plus the two the parsed message can carry and the calendar does not load, so a
/// comparison against the stored event (Bulwark compares against the whole event) does not see
/// them as always changed.
pub fn stored_props() -> Vec<&'static str> {
    let mut v = crate::calendar::EVENT_PROPS.to_vec();
    for k in ["descriptionContentType", "recurrenceIdTimeZone"] { if !v.contains(&k) { v.push(k); } }
    v
}

/// All events stored with this uid (the series and any occurrence stored on its own).
pub fn stored_all(c: &crate::jmap::Client, uid: &str) -> Result<Vec<Value>, String> {
    let acc = c.account_for(CAP);
    let r = c.call(json!([
        ["CalendarEvent/query", {"accountId": acc, "filter": {"uid": uid}, "limit": 50}, "q"],
        ["CalendarEvent/get", {"accountId": acc, "#ids": {"resultOf": "q", "name": "CalendarEvent/query", "path": "/ids"}, "properties": stored_props()}, "g"]
    ]))?;
    if r[0][0] == "error" { return Err(crate::jmap::method_error(&r[0][1])); }
    if r[1][0] == "error" { return Err(crate::jmap::method_error(&r[1][1])); }
    Ok(r[1][1]["list"].as_array().cloned().unwrap_or_default())
}

/// The request that removes a cancelled event. No scheduling messages: the organizer cancelled
/// it, there is no one to tell (Bulwark handleRemoveCancelled, banner:994-1003).
pub fn removal_request(acc: &str, r: &Removal) -> Option<Value> {
    let set = match r {
        Removal::Delete(ids) if !ids.is_empty() => json!({"accountId": acc, "sendSchedulingMessages": false, "destroy": ids}),
        Removal::Exclude { id, patch } => json!({"accountId": acc, "sendSchedulingMessages": false, "update": {id: patch}}),
        _ => return None,
    };
    Some(json!([["CalendarEvent/set", set, "s"]]))
}

fn set_failure(r: &[Value]) -> Option<String> {
    if r[0][0] == "error" { return Some(crate::jmap::method_error(&r[0][1])); }
    for k in ["notUpdated", "notDestroyed"] {
        if let Some(e) = r[0][1][k].as_object().and_then(|o| o.values().next()) { return Some(crate::jmap::set_error(e)); }
    }
    None
}

pub const ERR_OUTDATED: &str = "Your calendar has a newer version of this event than this cancellation.";
pub const ERR_GONE: &str = "The event is no longer in your calendar.";

/// What a failed removal says about the plan the banner holds: when the event is gone or newer
/// the removal is no longer offered (Bulwark setCancelRemoval).
pub fn removal_after_error(e: &str) -> Option<Removal> {
    match e { ERR_OUTDATED => Some(Removal::Outdated), ERR_GONE => Some(Removal::Nothing), _ => None }
}

/// Remove the cancelled event from the calendar. Planned again on the events as they are now:
/// the calendar may have changed since the mail was opened.
pub fn remove_cancelled(c: &crate::jmap::Client, cancel: &Value, sender: Option<&str>) -> Result<String, String> {
    let uid = cancel["uid"].as_str().ok_or("This cancellation names no event.")?;
    let plan = cancel_plan(&stored_all(c, uid)?, cancel, sender);
    match &plan.removal {
        Removal::Outdated => return Err(ERR_OUTDATED.into()),
        Removal::Nothing => return Err(ERR_GONE.into()),
        _ => {}
    }
    if !plan.from_organizer { return Err("This cancellation did not come from the event's organizer.".into()); }
    let req = removal_request(&c.account_for(CAP), &plan.removal).ok_or("Nothing to remove.")?;
    let r = c.call(req)?;
    if let Some(e) = set_failure(&r) { return Err(e); }
    Ok("Removed from your calendar.".into())
}

// ---------------------------------------------------------------- counter proposals (G-001)

/// The event in the calendar a counter proposal is about: one for an occurrence names it by its
/// recurrence id, one for the whole meeting names none (Bulwark counterTarget, banner:258-262).
pub fn counter_target(events: &[Value], proposal: &Value) -> Option<Value> {
    let uid = proposal["uid"].as_str()?;
    let mut same = events.iter().filter(|e| e["uid"].as_str() == Some(uid));
    if proposal["recurrenceId"].is_null() { return same.find(|e| e["recurrenceId"].is_null()).cloned(); }
    same.find(|e| same_recurrence_id(e, proposal)).cloned()
}

/// What the proposal changes against the stored event (Bulwark buildProposalPatch, banner:264-312).
pub fn proposal_patch(cur: &Value, prop: &Value) -> serde_json::Map<String, Value> {
    let mut p = serde_json::Map::new();
    for k in ["title", "description", "descriptionContentType", "start", "duration"] {
        if prop[k].is_string() && prop[k] != cur[k] { p.insert(k.into(), prop[k].clone()); }
    }
    if prop["timeZone"] != cur["timeZone"] { p.insert("timeZone".into(), prop["timeZone"].clone()); }
    if (prop["showWithoutTime"] == true) != (cur["showWithoutTime"] == true) { p.insert("showWithoutTime".into(), json!(prop["showWithoutTime"] == true)); }
    for k in ["locations", "virtualLocations"] {
        if prop[k] != cur[k] { p.insert(k.into(), prop[k].clone()); }
    }
    p
}

/// The changes as the banner lists them: (label, before, after) (Bulwark buildInvitationChangeItems).
pub fn proposal_changes(cur: &Value, prop: &Value, zone: &str) -> Vec<(String, String, String)> {
    let mut v = vec![];
    let or_empty = |s: String| if s.is_empty() { "(none)".to_string() } else { s };
    let title = |e: &Value| e["title"].as_str().unwrap_or("").to_string();
    if title(cur) != title(prop) && !title(prop).is_empty() { v.push(("Title".into(), or_empty(title(cur)), title(prop))); }
    if prop["start"].is_string() {
        let (a, b) = (when(cur, zone), when(prop, zone));
        if a != b { v.push(("Time".into(), or_empty(a), b)); }
    }
    let (la, lb) = (location(cur), location(prop));
    if la != lb && !lb.is_empty() { v.push(("Location".into(), or_empty(la), lb)); }
    let (da, db) = (crate::cal_editor::description_text(cur), crate::cal_editor::description_text(prop));
    if da != db && !db.is_empty() { v.push(("Description".into(), or_empty(da), db)); }
    v
}

/// `participants` with every attendee asked to answer again, the organizer's entries as they are
/// (Bulwark participantsAskedAgain, calendar-participants.ts:303); None when no attendee has answered.
pub fn participants_asked_again(ev: &Value) -> Option<Value> {
    let ps = ev["participants"].as_object()?;
    let org = stored_organizer(ev);
    let mut asked = false;
    let mut out = serde_json::Map::new();
    for (id, p) in ps {
        let is_org = is_organizer(p) || (org.is_some() && participant_email(p) == org);
        let st = p["participationStatus"].as_str().unwrap_or("");
        if is_org || st.is_empty() || st == "needs-action" { out.insert(id.clone(), p.clone()); continue; }
        let mut p = p.clone();
        p["participationStatus"] = json!("needs-action");
        out.insert(id.clone(), p);
        asked = true;
    }
    asked.then(|| Value::Object(out))
}

/// A counter proposal read against the calendar.
#[derive(Clone, Debug, PartialEq)]
pub struct Proposal {
    pub id: String,
    pub patch: serde_json::Map<String, Value>,
    pub changes: Vec<(String, String, String)>,
    /// The sender is one of the stored event's participants.
    pub from_participant: bool,
    pub organizer_me: bool,
    /// Attendees to ask again when the proposal moves the event.
    pub asked: Option<Value>,
}

impl Proposal {
    /// Applying a proposal sends it to every attendee, so it has to come from one of the meeting's
    /// participants (anyone who learned the uid can write one, naming any attendee) and the
    /// user has to organise the stored event (Bulwark canApplyProposal, banner:947-954).
    pub fn can_apply(&self) -> bool { self.organizer_me && self.from_participant && !self.patch.is_empty() }

    /// The update to write: the patch, and the attendees asked again when it moves the event.
    pub fn update(&self) -> serde_json::Map<String, Value> {
        let mut p = self.patch.clone();
        if ["start", "duration", "timeZone", "showWithoutTime"].iter().any(|k| p.contains_key(*k)) {
            if let Some(a) = &self.asked { p.insert("participants".into(), a.clone()); }
        }
        p
    }
}

pub fn proposal(stored: &[Value], counter: &Value, sender: Option<&str>, mine: &[String], zone: &str) -> Option<Proposal> {
    let target = counter_target(stored, counter)?;
    let s = sender.map(norm_addr).filter(|s| !s.is_empty());
    let from_participant = s.is_some_and(|s| target["participants"].as_object().is_some_and(|o| o.values().any(|p| participant_email(p).as_deref() == Some(s.as_str()))));
    Some(Proposal {
        id: target["id"].as_str()?.to_string(),
        patch: proposal_patch(&target, counter),
        changes: proposal_changes(&target, counter, zone),
        from_participant,
        organizer_me: i_organize(&target, mine),
        asked: participants_asked_again(&target),
    })
}

/// The request that applies a proposal: scheduling messages go out, so the attendees hear of the
/// change (Bulwark handleApplyProposal, banner:957-978).
pub fn proposal_request(acc: &str, p: &Proposal) -> Value {
    json!([["CalendarEvent/set", {"accountId": acc, "sendSchedulingMessages": true, "update": {p.id.clone(): Value::Object(p.update())}}, "s"]])
}

/// Apply the proposal to the stored event, read again as it is now.
pub fn apply_proposal(c: &crate::jmap::Client, counter: &Value, sender: Option<&str>, mine: &[String], zone: &str) -> Result<String, String> {
    let uid = counter["uid"].as_str().ok_or("This proposal names no event.")?;
    let p = proposal(&stored_all(c, uid)?, counter, sender, mine, zone).ok_or("The event is not in your calendar.")?;
    if !p.organizer_me { return Err("You do not organize this event.".into()); }
    if !p.from_participant { return Err("This proposal did not come from one of the event's participants.".into()); }
    if p.patch.is_empty() { return Err("The event already has these changes.".into()); }
    let r = c.call(proposal_request(&c.account_for(CAP), &p))?;
    if let Some(e) = set_failure(&r) { return Err(e); }
    Ok("Proposed changes applied.".into())
}

/// How far the message can be trusted to come from who the calendar data names (Bulwark
/// getInvitationTrustAssessment): `Some((warning, text))` for a caution (false) or a warning
/// (true), None when trusted. `from` is the message's From address, `auth` its topmost
/// Authentication-Results, `mine` the account's own addresses (mail between them is internal).
pub fn trust(ev: &Value, method: &Method, from: Option<&str>, auth: &[crate::auth::AuthResult], has_auth: bool, mine: &[String]) -> Option<(bool, String)> {
    let sender = from.map(norm_addr).filter(|s| !s.is_empty());
    let from_attendee = matches!(method, Method::Reply | Method::Counter | Method::Refresh);
    let expected = if from_attendee {
        ev["participants"].as_object().and_then(|o| o.values().find(|p| p["roles"]["owner"] != true && p["participationStatus"].as_str().is_some_and(|s| s != "needs-action")).and_then(participant_email))
    } else { organizer(ev).map(|o| o.1).filter(|e| !e.is_empty()) }.map(|e| norm_addr(&e));
    let verified = sender.as_deref().is_some_and(|s| crate::auth::aligned_pass(auth, s));
    let failed = crate::auth::failure(auth, verified);
    let mismatch = matches!((&sender, &expected), (Some(s), Some(e)) if s != e);
    let sd = sender.as_deref().and_then(crate::auth::domain_of);
    let internal = !has_auth && sd.as_ref().is_some_and(|d| mine.iter().any(|m| crate::auth::domain_of(m).as_ref() == Some(d)));
    let (s, e) = (sender.clone().unwrap_or_default(), expected.clone().unwrap_or_default());
    let mismatch_text = if from_attendee { format!("This response was sent from {s}, while the attendee answering in the calendar data is {e}") }
        else { format!("This invitation was sent from {s}, while the organizer listed in the calendar data is {e}") };
    if mismatch && (failed || !verified) { return Some((true, format!("{mismatch_text}, and the message could not be verified."))); }
    if failed { return Some((true, "Mail authentication checks for this invitation failed. Treat calendar actions with caution.".into())); }
    if mismatch { return Some((false, format!("{mismatch_text}."))); }
    if *method != Method::Unknown && !internal && !verified {
        return Some((false, "This invitation does not include verified mail authentication. Confirm the details with the organizer if anything looks unusual.".into()));
    }
    None
}

#[cfg(test)]
mod tests {
    use super::*;

    fn ev() -> Value {
        json!({"@type": "Event", "uid": "u1", "title": "Planning", "start": "2026-11-17T10:00:00", "timeZone": "Europe/Berlin", "duration": "PT1H30M",
            "locations": {"l1": {"name": "Room 4"}},
            "participants": {
                "o": {"name": "Olga", "calendarAddress": "mailto:olga@acme.com", "roles": {"owner": true, "attendee": true}, "participationStatus": "accepted"},
                "m": {"calendarAddress": "mailto:Me@x.org", "roles": {"attendee": true}, "participationStatus": "needs-action"}
            }})
    }

    #[test]
    fn finds_the_calendar_part() {
        let m = json!({"attachments": [{"blobId": "b1", "type": "application/pdf", "name": "a.pdf"}, {"blobId": "b2", "type": "text/calendar", "name": "invite.ics"}]});
        assert_eq!(find_part(&m).unwrap()["blobId"], "b2");
        let inline = json!({"attachments": [], "textBody": [{"partId": "1", "blobId": "t", "type": "text/plain"}, {"partId": "2", "blobId": "c", "type": "TEXT/CALENDAR; method=REQUEST"}]});
        assert_eq!(find_part(&inline).unwrap()["blobId"], "c");
        assert_eq!(find_part(&json!({"attachments": [{"blobId": "x", "name": "MEETING.ICS", "type": "application/octet-stream"}]})).unwrap()["blobId"], "x");
        assert!(find_part(&json!({"attachments": []})).is_none());
    }

    #[test]
    fn method_order() {
        assert_eq!(method_from_ics("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nMETHOD:REQUEST\r\nBEGIN:VEVENT"), Method::Request);
        assert_eq!(method_from_ics("method;x=1:Cancel\n"), Method::Cancel);
        assert_eq!(method_from_ics("METHODICAL:REPLY\nSUMMARY:x"), Method::Unknown);
        assert_eq!(method(&ev(), Some("METHOD:CANCEL")), Method::Cancel);
        assert_eq!(method(&json!({"method": "reply"}), Some("METHOD:REQUEST")), Method::Reply);
        assert_eq!(method(&ev(), None), Method::Request);
        assert_eq!(method(&json!({"status": "cancelled"}), None), Method::Cancel);
        let reply = json!({"participants": {"a": {"roles": {"attendee": true}, "participationStatus": "accepted"}}});
        assert_eq!(method(&reply, None), Method::Reply);
    }

    #[test]
    fn people() {
        let e = ev();
        assert_eq!(organizer(&e), Some(("Olga".into(), "olga@acme.com".into())));
        assert_eq!(my_participant(&e, &["me@x.org".into()]), Some(("m".into(), "needs-action".into())));
        assert!(my_participant(&e, &["olga@acme.com".into()]).is_some(), "an organizer who also attends answers");
        assert!(i_organize(&e, &["OLGA@acme.com".into()]));
        let only_owner = json!({"participants": {"o": {"email": "o@x", "roles": {"owner": true}}}});
        assert!(my_participant(&only_owner, &["o@x".into()]).is_none());
        let stalwart = json!({"organizerCalendarAddress": "mailto:boss@x.org", "participants": {"b": {"calendarAddress": "mailto:boss@x.org", "name": "Boss"}}});
        assert_eq!(organizer(&stalwart), Some(("Boss".into(), "boss@x.org".into())));
    }

    #[test]
    fn summary_text() {
        assert_eq!(duration_secs("PT1H30M"), Some(5400));
        assert_eq!(duration_secs("P1DT2H"), Some(93_600));
        assert_eq!(duration_secs("P2W"), Some(1_209_600));
        assert_eq!(duration_secs("1H"), None);
        assert_eq!(when(&ev(), "Europe/Berlin"), "Tue 17 Nov 2026, 10:00 – 11:30");
        assert_eq!(when(&ev(), "Europe/Istanbul"), "Tue 17 Nov 2026, 10:00 – 11:30 (Europe/Berlin)");
        let all_day = json!({"start": "2026-11-17T00:00:00", "showWithoutTime": true, "duration": "P1D"});
        assert_eq!(when(&all_day, "X"), "Tue 17 Nov 2026, all day");
        let two = json!({"start": "2026-11-17T00:00:00", "showWithoutTime": true, "duration": "P2D"});
        assert_eq!(when(&two, "X"), "Tue 17 Nov 2026 – Wed 18 Nov 2026");
        assert_eq!(location(&ev()), "Room 4");
        assert_eq!(status_label("tentative"), "Tentative");
    }

    #[test]
    fn trust_assessment() {
        use crate::auth::parse;
        let e = ev();
        let good = parse("mx; dkim=pass header.d=acme.com; dmarc=pass");
        assert_eq!(trust(&e, &Method::Request, Some("olga@acme.com"), &good, true, &[]), None);
        let (warn, t) = trust(&e, &Method::Request, Some("olga@evil.net"), &parse("mx; spf=pass smtp.mailfrom=x@evil.net"), true, &[]).unwrap();
        assert!(!warn, "an aligned pass for a different sender is a caution: {t}");
        assert!(t.contains("olga@evil.net") && t.contains("olga@acme.com"));
        let (warn, t) = trust(&e, &Method::Request, Some("olga@evil.net"), &parse("mx; spf=none"), true, &[]).unwrap();
        assert!(warn && t.ends_with("could not be verified."), "spoofed organizer");
        assert!(trust(&e, &Method::Request, Some("olga@acme.com"), &parse("mx; dmarc=fail"), true, &[]).unwrap().0);
        let (warn, _) = trust(&e, &Method::Request, Some("olga@acme.com"), &[], false, &[]).unwrap();
        assert!(!warn, "no authentication at all is a caution");
        assert_eq!(trust(&e, &Method::Request, Some("olga@acme.com"), &[], false, &["me@acme.com".into()]), None, "internal mail");
    }

    #[test]
    fn import_and_patch_shapes() {
        let mut e = ev();
        e["id"] = json!("x");
        e["method"] = json!("request");
        let i = importable(&e, "cal1");
        assert!(i.get("id").is_none() && i.get("method").is_none());
        assert_eq!(i["calendarIds"], json!({"cal1": true}));
        assert_eq!(i["uid"], "u1");
        assert_eq!(rsvp_patch("a/b~c", "accepted"), json!({"participants/a~1b~0c/participationStatus": "accepted"}));
    }

    // ---- G-002: cancellations

    fn stored(id: &str, seq: i64) -> Value {
        json!({"id": id, "uid": "u1", "sequence": seq, "organizerCalendarAddress": "mailto:Olga@acme.com", "start": "2026-11-17T10:00:00", "timeZone": "Europe/Berlin",
            "participants": {"o": {"calendarAddress": "mailto:olga@acme.com", "roles": {"owner": true, "attendee": true}, "participationStatus": "accepted"},
                "a": {"calendarAddress": "mailto:ann@example.test", "roles": {"attendee": true}, "participationStatus": "accepted"}}})
    }

    #[test]
    fn cancellation_of_the_whole_meeting() {
        let cancel = json!({"uid": "u1", "sequence": 1});
        assert_eq!(plan_cancellation(&[stored("e1", 0)], &cancel), Removal::Delete(vec!["e1".into()]));
        assert_eq!(plan_cancellation(&[stored("e1", 1)], &cancel), Removal::Delete(vec!["e1".into()]), "the same sequence applies");
        assert_eq!(plan_cancellation(&[stored("e1", 2)], &cancel), Removal::Outdated, "a newer stored copy is kept");
        assert_eq!(plan_cancellation(&[], &cancel), Removal::Nothing);
        assert_eq!(plan_cancellation(&[stored("e1", 0)], &json!({"sequence": 1})), Removal::Nothing, "no uid");
        let mut other = stored("e2", 0);
        other["uid"] = json!("u2");
        assert_eq!(plan_cancellation(&[other, stored("e1", 0)], &cancel), Removal::Delete(vec!["e1".into()]));
    }

    #[test]
    fn cancellation_of_one_occurrence() {
        let mut series = stored("s1", 0);
        series["recurrenceRules"] = json!([{"frequency": "weekly"}]);
        series["recurrenceOverrides"] = Value::Null;
        let cancel = json!({"uid": "u1", "recurrenceId": "2026-11-24T10:00:00", "timeZone": "Europe/Berlin", "sequence": 1});
        // no overrides yet: the whole map is written
        match plan_cancellation(&[series.clone()], &cancel) {
            Removal::Exclude { id, patch } => {
                assert_eq!(id, "s1");
                assert_eq!(Value::Object(patch), json!({"recurrenceOverrides": {"2026-11-24T10:00:00": {"excluded": true}}}));
            }
            r => panic!("{r:?}"),
        }
        // overrides there: one pointer into the map
        series["recurrenceOverrides"] = json!({"2026-12-01T10:00:00": {"title": "x"}});
        match plan_cancellation(&[series.clone()], &cancel) {
            Removal::Exclude { patch, .. } => assert_eq!(Value::Object(patch), json!({"recurrenceOverrides/2026-11-24T10:00:00": {"excluded": true}})),
            r => panic!("{r:?}"),
        }
        // the cancellation's id is read in the series' zone
        let utc = json!({"uid": "u1", "recurrenceId": "2026-11-24T09:00:00", "timeZone": "UTC"});
        match plan_cancellation(&[series.clone()], &utc) {
            Removal::Exclude { patch, .. } => assert!(patch.contains_key("recurrenceOverrides/2026-11-24T10:00:00"), "{patch:?}"),
            r => panic!("{r:?}"),
        }
        // already excluded, newer override, overrides unknown
        series["recurrenceOverrides"] = json!({"2026-11-24T10:00:00": {"excluded": true}});
        assert_eq!(plan_cancellation(&[series.clone()], &cancel), Removal::Nothing);
        series["recurrenceOverrides"] = json!({"2026-11-24T10:00:00": {"sequence": 5}});
        assert_eq!(plan_cancellation(&[series.clone()], &cancel), Removal::Outdated);
        series.as_object_mut().unwrap().remove("recurrenceOverrides");
        assert_eq!(plan_cancellation(&[series.clone()], &cancel), Removal::Nothing, "overrides not read");
        // an occurrence stored on its own is deleted, a newer one is kept
        let mut own = stored("o1", 0);
        own["recurrenceId"] = json!("2026-11-24T10:00:00");
        assert_eq!(plan_cancellation(&[own.clone()], &cancel), Removal::Delete(vec!["o1".into()]));
        own["sequence"] = json!(3);
        assert_eq!(plan_cancellation(&[own], &cancel), Removal::Outdated);
        assert!(same_recurrence_id(&json!({"recurrenceId": "2026-11-24T10:00:00", "timeZone": "Europe/Berlin"}), &utc));
        assert!(!same_recurrence_id(&json!({"timeZone": "Europe/Berlin"}), &utc));
    }

    #[test]
    fn only_the_organizer_removes() {
        let e = stored("e1", 0);
        assert!(from_organizer_of(&[e.clone()], Some("OLGA@acme.com")));
        assert!(!from_organizer_of(&[e.clone()], Some("ann@example.test")), "an attendee cannot cancel");
        assert!(!from_organizer_of(&[e.clone()], None));
        assert!(!from_organizer_of(&[], Some("olga@acme.com")));
        let cancel = json!({"uid": "u1", "sequence": 1, "organizerCalendarAddress": "mailto:ann@example.test"});
        let p = cancel_plan(&[e.clone()], &cancel, Some("ann@example.test"));
        assert!(p.removable() && !p.from_organizer, "the organizer written in the cancellation does not count");
        assert!(cancel_notice(&p, true, "ann@example.test").unwrap().contains("organized by olga@acme.com"));
        let p = cancel_plan(&[e], &cancel, Some("olga@acme.com"));
        assert_eq!(cancel_notice(&p, true, "olga@acme.com"), None);
        assert!(cancel_notice(&p, false, "olga@acme.com").unwrap().contains("could not be verified"));
        assert_eq!(cancel_notice(&cancel_plan(&[], &cancel, Some("olga@acme.com")), false, "x"), None, "nothing to remove, nothing to say");
    }

    #[test]
    fn removal_request_shapes() {
        let del = removal_request("acc", &Removal::Delete(vec!["e1".into(), "e2".into()])).unwrap();
        assert_eq!(del[0][0], "CalendarEvent/set");
        assert_eq!(del[0][1]["sendSchedulingMessages"], false, "the organizer cancelled it, nobody to tell");
        assert_eq!(del[0][1]["destroy"], json!(["e1", "e2"]));
        let mut patch = serde_json::Map::new();
        patch.insert("recurrenceOverrides/k".into(), json!({"excluded": true}));
        let ex = removal_request("acc", &Removal::Exclude { id: "s1".into(), patch }).unwrap();
        assert_eq!(ex[0][1]["update"]["s1"]["recurrenceOverrides/k"], json!({"excluded": true}));
        assert_eq!(ex[0][1]["sendSchedulingMessages"], false);
        assert!(removal_request("acc", &Removal::Outdated).is_none() && removal_request("acc", &Removal::Nothing).is_none());
        assert_eq!(set_failure(&[json!(["CalendarEvent/set", {"destroyed": ["e1"]}, "s"])]), None);
        assert!(set_failure(&[json!(["CalendarEvent/set", {"notDestroyed": {"e1": {"type": "forbidden"}}}, "s"])]).is_some());
    }

    // ---- G-001: counter proposals

    fn counter() -> Value {
        json!({"uid": "u1", "title": "Planning", "start": "2026-11-17T14:00:00", "timeZone": "Europe/Berlin", "duration": "PT1H30M",
            "locations": {"l1": {"name": "Room 4"}}})
    }

    #[test]
    fn counter_proposal_patch_and_changes() {
        let cur = ev();
        let prop = counter();
        let patch = proposal_patch(&cur, &prop);
        assert_eq!(Value::Object(patch.clone()), json!({"start": "2026-11-17T14:00:00"}), "only what differs");
        let ch = proposal_changes(&cur, &prop, "Europe/Berlin");
        assert_eq!(ch, vec![("Time".to_string(), "Tue 17 Nov 2026, 10:00 – 11:30".to_string(), "Tue 17 Nov 2026, 14:00 – 15:30".to_string())]);
        assert!(proposal_patch(&cur, &cur).is_empty());
        let mut moved_zone = prop.clone();
        moved_zone["timeZone"] = Value::Null;
        assert_eq!(proposal_patch(&cur, &moved_zone)["timeZone"], Value::Null);
        let mut place = prop.clone();
        place["start"] = cur["start"].clone();
        place["locations"] = json!({"l2": {"name": "Cafe"}});
        place["title"] = json!("Planning II");
        assert!(proposal_patch(&cur, &place).contains_key("locations"));
        let labels: Vec<String> = proposal_changes(&cur, &place, "Europe/Berlin").into_iter().map(|c| c.0).collect();
        assert_eq!(labels, ["Title", "Location"]);
    }

    #[test]
    fn stored_copies_are_read_with_what_the_parsed_message_carries() {
        let props = stored_props();
        for k in ["descriptionContentType", "recurrenceIdTimeZone", "title", "sequence"] { assert!(props.contains(&k), "{k}"); }
        let mut seen = std::collections::HashSet::new();
        assert!(props.iter().all(|k| seen.insert(*k)), "no property twice");
        // a counter that repeats the stored event, content type included, changes nothing
        let mut cur = ev();
        cur["description"] = json!("Agenda");
        cur["descriptionContentType"] = json!("text/plain");
        let prop = cur.clone();
        assert!(proposal_patch(&cur, &prop).is_empty());
        // and read without the property (the bug) it would have looked like a change
        let mut unread = cur.clone();
        unread.as_object_mut().unwrap().remove("descriptionContentType");
        assert!(proposal_patch(&unread, &prop).contains_key("descriptionContentType"));
    }

    #[test]
    fn a_failed_removal_stops_being_offered_when_the_event_is_gone_or_newer() {
        assert_eq!(removal_after_error(ERR_GONE), Some(Removal::Nothing));
        assert_eq!(removal_after_error(ERR_OUTDATED), Some(Removal::Outdated));
        assert_eq!(removal_after_error("Server unavailable"), None);
    }

    #[test]
    fn counter_target_is_the_named_occurrence() {
        let mut series = ev();
        series["id"] = json!("s1");
        series["recurrenceRules"] = json!([{"frequency": "weekly"}]);
        let mut occ = ev();
        occ["id"] = json!("o1");
        occ["recurrenceId"] = json!("2026-11-24T10:00:00");
        let all = [occ.clone(), series.clone()];
        assert_eq!(counter_target(&all, &counter()).unwrap()["id"], "s1", "a proposal for the whole meeting");
        let mut one = counter();
        one["recurrenceId"] = json!("2026-11-24T10:00:00");
        assert_eq!(counter_target(&all, &one).unwrap()["id"], "o1");
        one["recurrenceId"] = json!("2026-12-01T10:00:00");
        assert!(counter_target(&all, &one).is_none(), "not applied to another occurrence");
        assert!(counter_target(&[occ], &counter()).is_none(), "series not in the calendar");
        assert!(counter_target(&all, &json!({"uid": "zz"})).is_none());
    }

    #[test]
    fn proposal_is_applied_only_by_the_organizer_for_a_participant() {
        let mut target = ev();
        target["id"] = json!("e1");
        target["organizerCalendarAddress"] = json!("mailto:me@x.org");
        target["participants"] = json!({
            "o": {"calendarAddress": "mailto:me@x.org", "roles": {"owner": true, "attendee": true}, "participationStatus": "accepted"},
            "a": {"calendarAddress": "mailto:ann@example.test", "roles": {"attendee": true}, "participationStatus": "accepted"},
            "b": {"calendarAddress": "mailto:bob@example.test", "roles": {"attendee": true}, "participationStatus": "needs-action"}});
        let mine = vec!["me@x.org".to_string()];
        let p = proposal(&[target.clone()], &counter(), Some("Ann@example.test"), &mine, "Europe/Berlin").unwrap();
        assert!(p.organizer_me && p.from_participant && p.can_apply());
        assert_eq!(p.id, "e1");
        // a stranger writes a proposal naming any attendee
        let s = proposal(&[target.clone()], &counter(), Some("eve@evil.net"), &mine, "Europe/Berlin").unwrap();
        assert!(!s.from_participant && !s.can_apply());
        // not the organizer: nothing to apply
        let n = proposal(&[target.clone()], &counter(), Some("ann@example.test"), &["ann@example.test".to_string()], "Europe/Berlin").unwrap();
        assert!(!n.can_apply());
        // nothing differs
        let mut same = counter();
        same["start"] = target["start"].clone();
        let z = proposal(&[target.clone()], &same, Some("ann@example.test"), &mine, "Europe/Berlin").unwrap();
        assert!(!z.can_apply());
        // no stored copy
        assert!(proposal(&[], &counter(), Some("ann@example.test"), &mine, "Europe/Berlin").is_none());
        // the request: a move asks the answered attendees again, the organizer and needs-action stay
        let req = proposal_request("acc", &p);
        assert_eq!(req[0][1]["sendSchedulingMessages"], true, "the attendees hear of it");
        let up = &req[0][1]["update"]["e1"];
        assert_eq!(up["start"], "2026-11-17T14:00:00");
        assert_eq!(up["participants"]["a"]["participationStatus"], "needs-action");
        assert_eq!(up["participants"]["o"]["participationStatus"], "accepted");
        assert_eq!(up["participants"]["b"]["participationStatus"], "needs-action");
        // a change that does not move the event keeps the answers
        let mut title = counter();
        title["start"] = target["start"].clone();
        title["title"] = json!("Planning II");
        let t = proposal(&[target], &title, Some("ann@example.test"), &mine, "Europe/Berlin").unwrap();
        let up = proposal_request("acc", &t);
        assert_eq!(up[0][1]["update"]["e1"], json!({"title": "Planning II"}));
    }

    #[test]
    fn asked_again_needs_an_answer_to_ask_for() {
        let mut e = ev();
        e["participants"]["m"]["participationStatus"] = json!("needs-action");
        assert!(participants_asked_again(&e).is_none(), "the organizer's own entry stays and nobody else has answered");
    }
}
