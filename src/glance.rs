//! The mail screen's at-a-glance data (design/mockups BNMail / NordMail): the list header
//! (folder title, unread count, greeting), the Bento summary tiles (today's events, flagged
//! mail, open tasks), the storage meter and the sender panel beside the reader (contact
//! details, recent mail from the sender, files they sent, the next event with them).
//! Everything here is read-only and comes from the account; nothing is made up.
use crate::jmap::Client;
use crate::{AppWindow, PRow};
use chrono::{Local, Timelike};
use serde_json::{json, Value};
use slint::{Model, ModelRc};
use std::cell::RefCell;
use std::sync::Arc;

thread_local! {
    /// First name from the sending identity, for "Good morning, Alex."
    static FIRST_NAME: RefCell<String> = const { RefCell::new(String::new()) };
}

/// Fill a panel list in place (rows with the same message + label are not touched).
fn sync_rows(cur: &ModelRc<PRow>, rows: Vec<PRow>, install: impl FnOnce(ModelRc<PRow>)) {
    crate::listsync::sync_into(cur, rows, |r| (r.tone.clone(), r.label.clone()), |a, b| a == b, install);
}

fn prow(label: &str, value: &str, tone: &str) -> PRow {
    PRow { label: label.into(), value: value.into(), tone: tone.into() }
}

/// Remember the user's first name (from an identity's display name).
pub fn set_name(u: &AppWindow, identities: &[Value]) {
    let name = identities.iter().filter_map(|i| i["name"].as_str()).find(|n| !n.trim().is_empty() && !n.contains('@'))
        .and_then(|n| n.split_whitespace().next()).unwrap_or("").to_string();
    FIRST_NAME.with(|f| *f.borrow_mut() = name);
    title(u);
}

fn greeting() -> String {
    let part = match Local::now().hour() { 5..=11 => "Good morning", 12..=17 => "Good afternoon", _ => "Good evening" };
    let name = FIRST_NAME.with(|f| f.borrow().clone());
    if name.is_empty() { format!("{part}.") } else { format!("{part}, {name}.") }
}

/// Days mail is kept in Trash / Junk, as set by whoever runs the mail server (prefs.json
/// `retentionTrashDays` / `retentionJunkDays`; Bulwark takes them from the admin's policy.json
/// `organisation.retention`, since a normal user can't read the server's own value).
/// 0 or absent: nothing to say.
pub fn retention_days(role: &str, trash: i64, junk: i64) -> i64 {
    match role { "trash" => trash, "junk" => junk, _ => 0 }.clamp(0, 3650)
}

/// Bulwark retention_notice.line: the quiet line in the header of Trash and Junk.
pub fn retention_line(days: i64) -> String {
    if days <= 0 { return String::new(); }
    format!("Messages here are deleted {days} {} after they were moved here.", if days == 1 { "day" } else { "days" })
}

/// List header: the open folder (or tag) name, its unread count, and the line under it.
pub fn title(u: &AppWindow) {
    let tag = u.get_current_tag();
    let (name, unread) = if !tag.is_empty() {
        let tags = u.get_tags();
        let n = (0..tags.row_count()).filter_map(|i| tags.row_data(i)).find(|t| t.id == tag).map(|t| t.name.to_string()).unwrap_or_default();
        (n, 0)
    } else {
        let cur = u.get_current_folder();
        let f = u.get_folders();
        (0..f.row_count()).filter_map(|i| f.row_data(i)).find(|f| f.id == cur)
            .map(|f| (f.name.to_string(), f.unread)).unwrap_or_default()
    };
    u.set_list_title(if name.is_empty() { "Mail".into() } else { name.clone().into() });
    u.set_list_unread(unread);
    let sub = match unread {
        0 => format!("{} Nothing unread here.", greeting()),
        1 => format!("{} 1 unread message.", greeting()),
        n => format!("{} {n} unread messages.", greeting()),
    };
    u.set_list_sub(sub.into());
    let role = if tag.is_empty() { crate::mail::mailbox_role(&u.get_current_folder()) } else { String::new() };
    let s = crate::settings::Settings::load();
    u.set_list_retention(retention_line(retention_days(&role, s.int("retentionTrashDays", 0), s.int("retentionJunkDays", 0))).into());
}

/// Today's events and open tasks, from what the calendar has loaded.
pub fn calendar(u: &AppWindow) {
    if let Some((n, next)) = crate::calendar::glance_today() {
        u.set_sum_has_cal(true);
        u.set_sum_today(n as i32);
        u.set_sum_today_next(next.into());
    }
    if let Some((n, next)) = crate::calendar::glance_tasks() {
        u.set_sum_has_tasks(true);
        u.set_sum_tasks(n as i32);
        u.set_sum_tasks_next(next.into());
    }
}

fn gb(n: u64) -> String {
    let g = n as f64 / 1e9;
    if g >= 10.0 { format!("{g:.0} GB") } else if g >= 0.1 { format!("{g:.1} GB") } else { format!("{} MB", (n as f64 / 1e6).round()) }
}

/// Flagged mail (count and who sent the latest) and the storage quota.
pub fn refresh(w: crate::ui_types::Weak<AppWindow>, c: Arc<Client>) {
    std::thread::spawn(move || {
        let quota = c.quota().ok().flatten();
        let flagged = c.call(json!([
            ["Email/query", {"accountId": c.account, "filter": {"hasKeyword": "$flagged"}, "sort": [{"property": "receivedAt", "isAscending": false}], "limit": 3, "calculateTotal": true}, "q"],
            ["Email/get", {"accountId": c.account, "#ids": {"resultOf": "q", "name": "Email/query", "path": "/ids"}, "properties": ["from"]}, "g"]
        ])).ok().map(|r| {
            let total = r[0][1]["total"].as_u64().unwrap_or(0);
            let mut who: Vec<String> = vec![];
            for m in r[1][1]["list"].as_array().cloned().unwrap_or_default() {
                let f = &m["from"][0];
                // a first name, or the address's local part when there is no display name
                let n = match f["name"].as_str().filter(|s| !s.trim().is_empty() && !s.contains('@')) {
                    Some(name) => name.split_whitespace().next().unwrap_or("").to_string(),
                    None => f["email"].as_str().unwrap_or("").split('@').next().unwrap_or("").to_string(),
                };
                if !n.is_empty() && !who.contains(&n) { who.push(n); }
            }
            (total, who.into_iter().take(2).collect::<Vec<_>>().join(", "))
        });
        let _ = w.upgrade_in_event_loop(move |u| {
            match quota {
                Some((used, limit)) if limit > 0 => {
                    u.set_quota_text(format!("{} of {}", gb(used), gb(limit)).into());
                    u.set_quota_frac((used as f64 / limit as f64).clamp(0.0, 1.0) as f32);
                }
                Some((used, _)) => { u.set_quota_text(format!("{} used", gb(used)).into()); u.set_quota_frac(-1.0); }
                None => { u.set_quota_text("".into()); u.set_quota_frac(-1.0); }
            }
            if let Some((n, who)) = flagged {
                u.set_sum_flagged(n as i32);
                u.set_sum_flagged_who(who.into());
            }
        });
    });
}

/// Clear the sender panel (a new message is opening).
pub fn clear_sender(u: &AppWindow) {
    u.set_sx_org("".into());
    u.set_sx_phone("".into());
    u.set_sx_next("".into());
    u.set_sx_next_title("".into());
    u.set_sx_recent(ModelRc::default());
    u.set_sx_files(ModelRc::default());
}

fn short_date(v: &Value) -> String {
    crate::mail::parse_time(v).map(|t| {
        let days = (Local::now().date_naive() - t.date_naive()).num_days();
        match days { 0 => t.format("%H:%M").to_string(), 1..=6 => t.format("%a").to_string(), _ => t.format("%-d %b").to_string() }
    }).unwrap_or_default()
}

/// Fill the sender panel for `email` (the open message is `current`): the contact card if
/// there is one, the latest other messages from them, files they sent, the next shared event.
pub fn sender(w: crate::ui_types::Weak<AppWindow>, c: Arc<Client>, email: String, current: String) {
    if email.is_empty() { return; }
    if let Some((when, title)) = crate::calendar::next_with(&email) {
        if let Some(u) = w.upgrade() { u.set_sx_next(when.into()); u.set_sx_next_title(title.into()); }
    }
    std::thread::spawn(move || {
        let recent = c.call(json!([
            ["Email/query", {"accountId": c.account, "filter": {"from": email}, "sort": [{"property": "receivedAt", "isAscending": false}], "limit": 12}, "q"],
            ["Email/get", {"accountId": c.account, "#ids": {"resultOf": "q", "name": "Email/query", "path": "/ids"}, "properties": ["id", "subject", "receivedAt", "attachments"]}, "g"]
        ])).ok().map(|r| r[1][1]["list"].as_array().cloned().unwrap_or_default()).unwrap_or_default();
        let card = if c.has("urn:ietf:params:jmap:contacts") {
            let acc = c.account_for("urn:ietf:params:jmap:contacts");
            c.call(json!([
                ["ContactCard/query", {"accountId": acc, "filter": {"email": email}, "limit": 1}, "q"],
                ["ContactCard/get", {"accountId": acc, "#ids": {"resultOf": "q", "name": "ContactCard/query", "path": "/ids"}}, "g"]
            ])).ok().and_then(|r| r[1][1]["list"].as_array().and_then(|l| l.first().cloned()))
        } else { None };
        let real = crate::unified::real_id(&current).to_string();
        let _ = w.upgrade_in_event_loop(move |u| {
            if u.get_r_email().as_str() != email { return; }
            let mut rows = vec![];
            let mut files = vec![];
            for m in &recent {
                let id = m["id"].as_str().unwrap_or("");
                let date = short_date(&m["receivedAt"]);
                for a in m["attachments"].as_array().cloned().unwrap_or_default() {
                    let Some(name) = a["name"].as_str().filter(|n| !n.is_empty()) else { continue };
                    if crate::mail::is_inline_image(&a) || files.iter().any(|f: &PRow| f.label == name) { continue; }
                    if files.len() < 4 { files.push(prow(name, &date, id)); }
                }
                if id == real || rows.len() >= 3 { continue; }
                let subject = m["subject"].as_str().filter(|s| !s.trim().is_empty()).unwrap_or("(no subject)");
                rows.push(prow(subject, &date, id));
            }
            sync_rows(&u.get_sx_recent(), rows, |m| u.set_sx_recent(m));
            sync_rows(&u.get_sx_files(), files, |m| u.set_sx_files(m));
            if let Some(card) = card {
                let org = crate::contacts::first(&card, "organizations", "name").unwrap_or_default();
                let title = crate::contacts::first(&card, "titles", "name").unwrap_or_default();
                let line = [org, title].into_iter().filter(|s| !s.is_empty()).collect::<Vec<_>>().join(" · ");
                u.set_sx_org(line.into());
                u.set_sx_phone(crate::contacts::first(&card, "phones", "number").unwrap_or_default().into());
            }
        });
    });
}

/// The contact detail tiles (design/mockups BNContacts): recent mail from the contact, files
/// they sent and the next event with them. `email` is the contact's first address; the
/// results are dropped if another contact was opened meanwhile.
pub fn contact(w: crate::ui_types::Weak<AppWindow>, c: Arc<Client>, email: String) {
    if let Some(u) = w.upgrade() {
        u.set_cx_recent(ModelRc::default());
        u.set_cx_files(ModelRc::default());
        let next = crate::calendar::next_with(&email);
        u.set_cx_next(next.as_ref().map(|n| n.0.clone()).unwrap_or_default().into());
        u.set_cx_next_title(next.map(|n| n.1).unwrap_or_default().into());
        u.set_cx_email(email.clone().into());
    }
    if email.is_empty() { return; }
    std::thread::spawn(move || {
        let recent = c.call(json!([
            ["Email/query", {"accountId": c.account, "filter": {"operator": "AND", "conditions": [{"from": email}, {"notKeyword": "$draft"}]}, "sort": [{"property": "receivedAt", "isAscending": false}], "limit": 12}, "q"],
            ["Email/get", {"accountId": c.account, "#ids": {"resultOf": "q", "name": "Email/query", "path": "/ids"}, "properties": ["id", "subject", "receivedAt", "attachments"]}, "g"]
        ])).ok().map(|r| r[1][1]["list"].as_array().cloned().unwrap_or_default()).unwrap_or_default();
        let _ = w.upgrade_in_event_loop(move |u| {
            if u.get_cx_email().as_str() != email { return; }
            let mut rows = vec![];
            let mut files = vec![];
            for m in &recent {
                let id = m["id"].as_str().unwrap_or("");
                let date = short_date(&m["receivedAt"]);
                for a in m["attachments"].as_array().cloned().unwrap_or_default() {
                    let Some(name) = a["name"].as_str().filter(|n| !n.is_empty()) else { continue };
                    if crate::mail::is_inline_image(&a) || files.iter().any(|f: &PRow| f.label == name) { continue; }
                    if files.len() < 4 { files.push(prow(name, &date, id)); }
                }
                if rows.len() >= 4 { continue; }
                let subject = m["subject"].as_str().filter(|s| !s.trim().is_empty()).unwrap_or("(no subject)");
                rows.push(prow(subject, &date, id));
            }
            sync_rows(&u.get_cx_recent(), rows, |m| u.set_cx_recent(m));
            sync_rows(&u.get_cx_files(), files, |m| u.set_cx_files(m));
        });
    });
}

#[cfg(test)]
mod retention_tests {
    use super::*;

    #[test]
    fn the_retention_line_is_for_trash_and_junk_only() {
        assert_eq!(retention_days("trash", 30, 7), 30);
        assert_eq!(retention_days("junk", 30, 7), 7);
        assert_eq!(retention_days("inbox", 30, 7), 0);
        assert_eq!(retention_days("", 30, 7), 0);
        assert_eq!(retention_days("trash", -5, 7), 0);
        assert_eq!(retention_days("trash", 99999, 7), 3650);
        assert_eq!(retention_line(0), "");
        assert_eq!(retention_line(1), "Messages here are deleted 1 day after they were moved here.");
        assert_eq!(retention_line(30), "Messages here are deleted 30 days after they were moved here.");
    }
}
