//! Schedule send (GAPS P0-2), after Bulwark lib/schedule-presets.ts,
//! components/email/schedule-send-dialog.tsx, lib/scheduled-send-label.ts and the Scheduled
//! view's Reschedule / Cancel and edit / Cancel send (message-actions.ts).
//!
//! The server holds the message with the FUTURERELEASE extension (RFC 4865). Like Bulwark the
//! hold is sent as HOLDFOR (seconds left, worked out right before the request) rather than
//! HOLDUNTIL: servers disagree on the HOLDUNTIL value format, every FUTURERELEASE server takes
//! HOLDFOR (Bulwark client.ts rescheduleEmailSubmission).
use crate::jmap::Client;
use crate::{AppWindow, CompX, SPreset};
use chrono::{DateTime, Datelike, Duration, Local, NaiveDate, NaiveTime, TimeZone, Timelike};
use slint::{ComponentHandle, ModelRc, VecModel};
use std::cell::RefCell;
use std::sync::Arc;

/// How far ahead a time for today must be to be offered.
const TODAY_LEAD: i64 = 5 * 60;

fn at(base: DateTime<Local>, days: i64, hour: u32) -> Option<DateTime<Local>> {
    let d = base.date_naive() + Duration::days(days);
    Local.from_local_datetime(&d.and_hms_opt(hour, 0, 0)?).earliest()
}

/// One-click times (Bulwark schedulePresets): today 8:00 and 13:00 while at least five minutes
/// ahead, tomorrow 8:00 and 13:00, and next Monday 8:00 unless that is tomorrow.
pub fn presets(now: DateTime<Local>) -> Vec<(&'static str, DateTime<Local>)> {
    let mut v: Vec<(&'static str, DateTime<Local>)> = [("This morning", at(now, 0, 8)), ("This afternoon", at(now, 0, 13))].into_iter()
        .filter_map(|(k, d)| d.filter(|d| (*d - now).num_seconds() >= TODAY_LEAD).map(|d| (k, d))).collect();
    v.extend([("Tomorrow morning", at(now, 1, 8)), ("Tomorrow afternoon", at(now, 1, 13))].into_iter().filter_map(|(k, d)| d.map(|d| (k, d))));
    let dow = now.weekday().num_days_from_sunday() as i64; // Sunday 0
    let to_monday = match (8 - dow) % 7 { 0 => 7, n => n };
    if to_monday > 1 { if let Some(d) = at(now, to_monday, 8) { v.push(("Monday morning", d)); } }
    v
}

/// Parse the dialog's date ("2026-11-17", also "17.11.2026" / "17/11/2026") and time
/// ("08:00", "8:00 pm", "20.15") as local time.
pub fn parse_local(date: &str, time: &str) -> Option<DateTime<Local>> {
    let date = date.trim();
    let d = NaiveDate::parse_from_str(date, "%Y-%m-%d").ok()
        .or_else(|| NaiveDate::parse_from_str(date, "%d.%m.%Y").ok())
        .or_else(|| NaiveDate::parse_from_str(date, "%d/%m/%Y").ok())?;
    let t = time.trim().to_lowercase().replace('.', ":");
    let (clock, pm, am) = match (t.strip_suffix("pm"), t.strip_suffix("am")) {
        (Some(c), _) => (c.trim().to_string(), true, false),
        (_, Some(c)) => (c.trim().to_string(), false, true),
        _ => (t.clone(), false, false),
    };
    let (h, m) = match clock.split_once(':') { Some((h, m)) => (h.trim().parse::<u32>().ok()?, m.trim().parse::<u32>().ok()?), None => (clock.trim().parse::<u32>().ok()?, 0) };
    let h = if pm || am {
        if !(1..=12).contains(&h) { return None; }
        (h % 12) + if pm { 12 } else { 0 }
    } else { h };
    let t = NaiveTime::from_hms_opt(h, m, 0)?;
    Local.from_local_datetime(&d.and_time(t)).earliest()
}

/// Bulwark's checks before a time is taken. `max` is maxDelayedSend in seconds (0: no hold).
pub fn validate(when: Option<DateTime<Local>>, now: DateTime<Local>, max: u64, filled: bool) -> Result<DateTime<Local>, &'static str> {
    if max == 0 { return Err("Scheduled send is not supported for this account."); }
    if !filled { return Err("Choose a date and time."); }
    let Some(w) = when else { return Err("Enter a valid date and time.") };
    if w <= now { return Err("Choose a future date and time."); }
    if (w - now).num_seconds() > max as i64 { return Err("This time is later than the server allows."); }
    Ok(w)
}

/// The release time for lists, banners and toasts (Bulwark formatScheduledSendAt): rounded to
/// the minute (the server can land a second early), with the date unless it is today.
pub fn label(when: DateTime<Local>, h12: bool, now: DateTime<Local>) -> String {
    let secs = when.timestamp() + if when.second() >= 30 { 60 } else { 0 };
    let r = Local.timestamp_opt(secs - secs.rem_euclid(60), 0).single().unwrap_or(when);
    let time = if h12 { r.format("%-I:%M %p").to_string() } else { r.format("%H:%M").to_string() };
    if r.date_naive() == now.date_naive() { time } else { format!("{}, {time}", r.format("%a %-d %b")) }
}

pub fn label_rfc3339(s: &str, h12: bool) -> String {
    DateTime::parse_from_rfc3339(s).map(|d| label(d.with_timezone(&Local), h12, Local::now())).unwrap_or_else(|_| s.to_string())
}

fn h12() -> bool { crate::settings::str_pref("timeFormat", "24h") == "12h" }

/// What the dialog is for.
#[derive(Clone)]
enum Mode { Compose, Reschedule { id: String, submission: String } }

thread_local! {
    static MODE: RefCell<Option<Mode>> = const { RefCell::new(None) };
    static PRESETS: RefCell<Vec<DateTime<Local>>> = const { RefCell::new(vec![]) };
    static CLIENT: RefCell<Option<Arc<Client>>> = const { RefCell::new(None) };
}

fn open_dialog(u: &AppWindow, mode: Mode, c: Arc<Client>, initial: Option<DateTime<Local>>) {
    let now = Local::now();
    let list = presets(now);
    let hh = h12();
    let rows: Vec<SPreset> = list.iter().map(|(k, d)| SPreset {
        label: (*k).into(),
        when: format!("{}, {}", d.format("%a %-d %b"), if hh { d.format("%-I:%M %p").to_string() } else { d.format("%H:%M").to_string() }).into(),
    }).collect();
    PRESETS.with(|p| *p.borrow_mut() = list.iter().map(|x| x.1).collect());
    let start = initial.filter(|d| *d > now).or_else(|| at(now, 1, 8)).unwrap_or(now);
    let x = u.global::<CompX>();
    let resched = matches!(mode, Mode::Reschedule { .. });
    x.set_sched_title(if resched { "Reschedule".into() } else { "Schedule send".into() });
    x.set_sched_confirm(if resched { "Reschedule".into() } else { "Schedule send".into() });
    x.set_sched_presets(ModelRc::new(VecModel::from(rows)));
    x.set_sched_date(start.format("%Y-%m-%d").to_string().into());
    x.set_sched_time(start.format("%H:%M").to_string().into());
    x.set_sched_error((if c.supports_delayed_send() { "" } else { "Scheduled send is not supported for this account." }).into());
    x.set_sched_busy(false);
    MODE.with(|m| *m.borrow_mut() = Some(mode));
    CLIENT.with(|x| *x.borrow_mut() = Some(c));
    x.set_sched_open(true);
}

/// The composer's "Schedule send" (menu or Ctrl+Shift+Enter).
pub fn open_compose(u: &AppWindow, c: Arc<Client>) {
    if !u.get_composing() { return; }
    open_dialog(u, Mode::Compose, c, None);
}

fn close(u: &AppWindow) {
    u.global::<CompX>().set_sched_open(false);
    MODE.with(|m| *m.borrow_mut() = None);
}

/// A preset (`i` >= 0) or the typed date and time (`i` = -1) was chosen.
pub fn choose(u: &AppWindow, i: i32, prefs: &crate::settings::Settings) {
    let x = u.global::<CompX>();
    let Some(c) = CLIENT.with(|c| c.borrow().clone()) else { close(u); return };
    let (when, filled) = if i >= 0 {
        (PRESETS.with(|p| p.borrow().get(i as usize).copied()), true)
    } else {
        let (d, t) = (x.get_sched_date().to_string(), x.get_sched_time().to_string());
        (parse_local(&d, &t), !d.trim().is_empty() && !t.trim().is_empty())
    };
    let when = match validate(when, Local::now(), c.max_delayed_send(), filled) {
        Ok(w) => w,
        Err(e) => { x.set_sched_error(e.into()); return; }
    };
    let Some(mode) = MODE.with(|m| m.borrow().clone()) else { return };
    close(u);
    match mode {
        Mode::Compose => crate::compose::send_scheduled(u, c, prefs, when.timestamp_millis()),
        Mode::Reschedule { id, submission } => reschedule(u, c, id, submission, when),
    }
}

pub fn cancel_dialog(u: &AppWindow) { close(u); }

// ---------------------------------------------------------------- Scheduled view

/// The Scheduled view's banner for the open message: "Scheduled to send at …".
pub fn banner(u: &AppWindow, m: Option<&serde_json::Value>) {
    let x = u.global::<CompX>();
    let text = m.filter(|m| m["submissionId"].is_string()).and_then(|m| m["receivedAt"].as_str())
        .map(|s| format!("Scheduled to send at {}", label_rfc3339(s, h12()))).unwrap_or_default();
    x.set_sched_banner(text.into());
}

fn submission_of(id: &str) -> Option<(String, Option<DateTime<Local>>)> {
    let m = crate::mail::email(id)?;
    let sub = m["submissionId"].as_str()?.to_string();
    let at = m["receivedAt"].as_str().and_then(|s| DateTime::parse_from_rfc3339(s).ok()).map(|d| d.with_timezone(&Local));
    Some((sub, at))
}

fn refresh(w: slint::Weak<AppWindow>, c: Arc<Client>) {
    let w2 = w.clone();
    let _ = w.upgrade_in_event_loop(move |u| {
        if u.get_current_folder() == "__scheduled" { u.set_current_mail("".into()); }
        crate::mail::load_mailboxes(w2.clone(), c.clone(), false);
        crate::mail::sync(w2, c);
    });
}

/// Scheduled-message actions from the banner or the context menu: "reschedule", "edit"
/// (cancel and edit) and "cancel".
pub fn action(u: &AppWindow, c: Arc<Client>, id: &str, what: &str) {
    let Some((sub, at)) = submission_of(id) else { crate::mail::show_toast(u, "This message is no longer scheduled.", false); return };
    match what {
        "reschedule" => open_dialog(u, Mode::Reschedule { id: id.to_string(), submission: sub }, c, at),
        "cancel" | "edit" => {
            let edit = what == "edit";
            let w = u.as_weak();
            std::thread::spawn(move || {
                let res = c.cancel_submission(&sub);
                let full = match (&res, edit) { (Ok(Some(eid)), true) => c.email_full(eid, false).ok(), _ => None };
                let (w2, c2) = (w.clone(), c.clone());
                let _ = w.upgrade_in_event_loop(move |u| match res {
                    Ok(_) if edit => match full {
                        Some(m) => crate::compose::open_draft(&u, c2.clone(), &m),
                        None => crate::mail::show_toast(&u, "Sending cancelled. The message is in Drafts.", false),
                    },
                    Ok(_) => crate::mail::show_toast(&u, "Sending cancelled. The message is in Drafts.", false),
                    Err(e) => crate::mail::show_error(&u, &format!("Couldn't cancel the scheduled send: {e}")),
                });
                refresh(w2, c);
            });
        }
        _ => {}
    }
}

fn reschedule(u: &AppWindow, c: Arc<Client>, _id: String, sub: String, when: DateTime<Local>) {
    let w = u.as_weak();
    crate::mail::show_toast(u, "Rescheduling…", false);
    std::thread::spawn(move || {
        let res = c.reschedule_submission(&sub, when.timestamp_millis());
        let w2 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| match res {
            Ok(_) => crate::mail::show_toast(&u, &format!("Rescheduled for {}", label(when, h12(), Local::now())), false),
            Err(e) => crate::mail::show_error(&u, &e),
        });
        refresh(w2, c);
    });
}

/// Hook the CompX schedule callbacks up.
pub fn wire(ui: &AppWindow, st: Arc<std::sync::Mutex<Option<Arc<Client>>>>, prefs: Arc<std::sync::Mutex<crate::settings::Settings>>) {
    let x = ui.global::<CompX>();
    let w = ui.as_weak();
    x.on_sched_choose(move |i| { if let Some(u) = w.upgrade() { choose(&u, i, &prefs.lock().unwrap()); } });
    let w = ui.as_weak();
    x.on_sched_cancel(move || { if let Some(u) = w.upgrade() { cancel_dialog(&u); } });
    let w = ui.as_weak();
    let st2 = st.clone();
    x.on_sched_open_compose(move || {
        let Some(u) = w.upgrade() else { return };
        let Some(c) = crate::compose_client(&st2) else { return };
        open_compose(&u, c);
    });
    let w = ui.as_weak();
    x.on_sched_action(move |what| {
        let Some(u) = w.upgrade() else { return };
        let id = u.get_current_mail().to_string();
        let Some(c) = crate::client_for(&st, &id) else { return };
        action(&u, c, &id, &what);
    });
}

#[cfg(test)]
mod tests {
    use super::*;

    fn local(y: i32, mo: u32, d: u32, h: u32, mi: u32) -> DateTime<Local> {
        Local.with_ymd_and_hms(y, mo, d, h, mi, 0).unwrap()
    }

    #[test]
    fn preset_times() {
        // Monday 2026-11-16 at 07:00: both of today's times, tomorrow's, next Monday
        let p = presets(local(2026, 11, 16, 7, 0));
        let keys: Vec<&str> = p.iter().map(|x| x.0).collect();
        assert_eq!(keys, vec!["This morning", "This afternoon", "Tomorrow morning", "Tomorrow afternoon", "Monday morning"]);
        assert_eq!(p[4].1, local(2026, 11, 23, 8, 0));
        // 07:58: this morning is less than 5 minutes away
        assert_eq!(presets(local(2026, 11, 16, 7, 58))[0].0, "This afternoon");
        // Sunday evening: Monday is tomorrow, so no Monday preset
        let s = presets(local(2026, 11, 22, 20, 0));
        assert_eq!(s.iter().map(|x| x.0).collect::<Vec<_>>(), vec!["Tomorrow morning", "Tomorrow afternoon"]);
        // Saturday: Monday in two days
        assert_eq!(presets(local(2026, 11, 21, 20, 0)).last().unwrap().1, local(2026, 11, 23, 8, 0));
    }

    #[test]
    fn parsing_and_checks() {
        assert_eq!(parse_local("2026-11-17", "08:00"), Some(local(2026, 11, 17, 8, 0)));
        assert_eq!(parse_local("17.11.2026", "8:30 pm"), Some(local(2026, 11, 17, 20, 30)));
        assert_eq!(parse_local("17/11/2026", "12 am"), Some(local(2026, 11, 17, 0, 0)));
        assert_eq!(parse_local("2026-11-17", "20.15"), Some(local(2026, 11, 17, 20, 15)));
        assert_eq!(parse_local("2026-02-30", "08:00"), None);
        assert_eq!(parse_local("2026-11-17", "25:00"), None);
        let now = local(2026, 11, 16, 12, 0);
        assert!(validate(Some(local(2026, 11, 17, 8, 0)), now, 7 * 86400, true).is_ok());
        assert_eq!(validate(None, now, 7 * 86400, false), Err("Choose a date and time."));
        assert_eq!(validate(None, now, 7 * 86400, true), Err("Enter a valid date and time."));
        assert_eq!(validate(Some(local(2026, 11, 16, 11, 0)), now, 7 * 86400, true), Err("Choose a future date and time."));
        assert_eq!(validate(Some(local(2026, 11, 30, 8, 0)), now, 7 * 86400, true), Err("This time is later than the server allows."));
        assert_eq!(validate(Some(local(2026, 11, 17, 8, 0)), now, 0, true), Err("Scheduled send is not supported for this account."));
    }

    #[test]
    fn labels() {
        let now = local(2026, 11, 16, 12, 0);
        // a second early rounds up to the minute picked
        let early = local(2026, 11, 17, 8, 0) - Duration::seconds(1);
        assert_eq!(label(early, false, now), "Tue 17 Nov, 08:00");
        assert_eq!(label(local(2026, 11, 16, 13, 0), true, now), "1:00 PM");
    }
}
