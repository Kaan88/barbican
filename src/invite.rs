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
}
