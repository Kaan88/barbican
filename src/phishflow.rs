//! "Report phishing" for one or several messages, from the message list's menu, the selection
//! bar and the reader (Bulwark components/email/message-actions.ts and mv-list-toolbar.tsx offer
//! it for the selection, phishing-report-host.tsx asks, lib/phishing-report-flow.ts reports and
//! undoes, locales phishing_report.*).
//!
//! * Asks once for the whole selection ("Report phishing?" with the address), then sends one
//!   report per message (phish.rs); the reports never go as one mail.
//! * Each reported message moves to Junk with `$junk $phishing` and read; a message whose move
//!   failed keeps the keyword alone, so its links are still off.
//! * The toast offers Undo while it shows: the messages go back to the folder they were in with
//!   `$junk` and `$phishing` cleared; the reports already sent are not recalled.
//! * A message in a shared account is not reported (its report would go out in the login's own
//!   name); the user is told, not silently refused.
//!
//! The safety rules of phish.rs stay: the address is only the one in Settings, nothing is sent
//! to an address a message names, and links of a reported message are turned off.
use crate::jmap::Client;
use crate::phish::{self, Failure, Setup};
use crate::{mail, AppWindow, Shared};
use serde_json::Value;
use std::cell::{Cell, RefCell};
use slint::ComponentHandle;
use std::sync::Arc;

/// What a selection holds, for "Report phishing".
#[derive(Debug, Default, PartialEq)]
pub struct Plan {
    /// Rows that can be reported.
    pub ok: Vec<String>,
    /// Rows of a shared account.
    pub shared: usize,
    /// Rows that cannot be reported: sent, drafts, scheduled, already reported, gone from the list.
    pub skipped: usize,
}

/// Sort `rows` (list row id, the message if the list still holds it, the roles of its folders).
pub fn plan(rows: &[(String, Option<Value>, Vec<String>)]) -> Plan {
    let mut p = Plan::default();
    for (id, m, roles) in rows {
        if mail::shared_account(id).is_some() { p.shared += 1; }
        else if m.as_ref().is_some_and(|m| phish::is_reportable(m, roles)) { p.ok.push(id.clone()); }
        else { p.skipped += 1; }
    }
    p
}

fn plural<'a>(n: usize, one: &'a str, other: &'a str) -> &'a str { if n == 1 { one } else { other } }

pub const NO_ADDRESS: &str = "Set an address under Settings > Privacy & security to report phishing.";

/// Why nothing is reported (the toast of a click that has nothing to report).
pub fn nothing_text(p: &Plan) -> String {
    let shared = format!("{} in a shared account can't be reported from here.", plural(p.shared, "A message", "Messages"));
    let skipped = "Sent, draft and already reported messages can't be reported.";
    match (p.shared > 0, p.skipped > 0) {
        (true, true) => format!("{shared} {skipped}"),
        (true, false) => shared,
        _ => skipped.to_string(),
    }
}

/// The question (Bulwark `confirm_message`).
pub fn confirm_text(address: &str, count: usize) -> String {
    if count == 1 { format!("The message goes to {address} and moves to Junk.") }
    else { format!("Each of the {count} messages goes to {address} as its own report and moves to Junk.") }
}

/// What the confirmation says is left out of a selection (nothing: empty).
pub fn left_out_text(p: &Plan) -> String {
    let n = p.shared + p.skipped;
    if n == 0 { return String::new(); }
    format!("{n} {} left out (shared account, sent, draft or already reported).", plural(n, "message is", "messages are"))
}

/// The toast of a finished run (Bulwark `done`, `not_moved`, `failed`). `not_moved` and `failed`
/// are the reasons, one per message.
pub fn outcome_text(address: &str, moved: usize, not_moved: &[String], failed: &[String]) -> String {
    let mut parts: Vec<String> = vec![];
    if moved > 0 {
        parts.push(if moved == 1 { format!("Reported to {address} and moved to Junk.") }
                   else { format!("{moved} messages reported to {address} and moved to Junk.") });
    }
    if let Some(first) = not_moved.first() {
        let n = not_moved.len();
        parts.push(if n == 1 { format!("Reported to {address}, but the message couldn't be moved to Junk: {first}") }
                   else { format!("Reported to {address}, but {n} messages couldn't be moved to Junk: {first}") });
    }
    if let Some(first) = failed.first() {
        let n = failed.len();
        parts.push(if n == 1 { format!("The message couldn't be reported: {first}") } else { format!("{n} messages couldn't be reported: {first}") });
    }
    parts.join(" ")
}

/// The toast after Undo (Bulwark `undone`, `undo_failed`).
pub fn undone_text(back: usize) -> String {
    match back {
        0 => "Couldn't move it back.".to_string(),
        1 => "Moved back. The report was already sent.".to_string(),
        _ => "Moved back. The reports were already sent.".to_string(),
    }
}

/// The rows of `ids` the list holds, with the roles of their folders.
fn rows_of(ids: &[String]) -> Vec<(String, Option<Value>, Vec<String>)> {
    ids.iter().map(|id| {
        let m = mail::email(id);
        let roles = m.as_ref().and_then(|m| m["mailboxIds"].as_object()).map(|o| o.keys().map(|k| mail::mailbox_role(k)).collect()).unwrap_or_default();
        (id.clone(), m, roles)
    }).collect()
}

/// What a click on "Report phishing" does for `ids`.
pub enum Ask {
    /// Nothing to ask: say this.
    Say(String),
    /// Ask whether to report (address, how many, what is left out).
    Confirm { address: String, count: usize, left_out: String },
}

pub fn prepare(ids: &[String]) -> Ask {
    let Some(setup) = phish::setup_from_settings() else { return Ask::Say(NO_ADDRESS.into()) };
    let p = plan(&rows_of(ids));
    if p.ok.is_empty() { return Ask::Say(nothing_text(&p)); }
    Ask::Confirm { address: setup.address, count: p.ok.len(), left_out: left_out_text(&p) }
}

// ---------------------------------------------------------------- Undo

/// A reported message to take back.
pub struct UndoItem { pub client: Arc<Client>, pub id: String, pub origin: String }

thread_local! {
    /// The Undo the toast on screen offers: its text and what it takes back.
    static UNDO: RefCell<Option<(String, Vec<UndoItem>)>> = const { RefCell::new(None) };
    static BUSY: Cell<bool> = const { Cell::new(false) };
}

/// A new toast replaced the one that offered Undo.
pub fn clear_undo() { UNDO.with(|x| *x.borrow_mut() = None); }

/// Show `text` with an Undo that moves `items` back.
pub fn offer_undo(u: &AppWindow, text: &str, items: Vec<UndoItem>) {
    mail::show_toast(u, text, !items.is_empty());
    if !items.is_empty() { UNDO.with(|x| *x.borrow_mut() = Some((text.to_string(), items))); }
}

/// The Undo button (or key) was used: takes the reported messages back when the toast of a report
/// is the one on screen. Returns whether it was one (otherwise the send's Undo applies).
pub fn undo(u: &AppWindow) -> bool {
    let Some((text, items)) = UNDO.with(|x| x.borrow_mut().take()) else { return false };
    // the toast ran out, was closed or replaced: this Undo is gone, the send's applies
    if u.get_toast().as_str() != text { return false; }
    u.set_toast("".into());
    let clients: Vec<Arc<Client>> = items.iter().map(|i| i.client.clone()).collect();
    let w = u.as_weak();
    std::thread::spawn(move || {
        let back = items.iter().filter(|i| undo_one(i).is_ok()).count();
        let _ = w.upgrade_in_event_loop(move |u| {
            mail::show_toast(&u, &undone_text(back), false);
            if back > 0 { mail::refresh_after(&u, clients); }
        });
    });
    true
}

fn undo_one(i: &UndoItem) -> Result<(), String> {
    let junk = i.client.role_ids()?.get("junk").cloned();
    // only a message that is still in Junk goes back: one the user filed elsewhere since stays
    let got = i.client.call(serde_json::json!([["Email/get", {"accountId": i.client.account, "ids": [i.id], "properties": ["mailboxIds"]}, "g"]]))?;
    let still = got.first().map(|x| &x[1]["list"][0]["mailboxIds"]).and_then(|m| junk.as_ref().map(|j| m[j.as_str()].as_bool() == Some(true))).unwrap_or(false);
    if !still { return Err("The message is no longer in Junk.".into()); }
    let r = i.client.call(phish::undo_call(&i.client.account, &i.id, &i.origin, junk.as_deref()))?;
    match r.first().and_then(|x| x[1]["notUpdated"].get(&i.id)).filter(|e| !e.is_null()) {
        Some(e) => Err(crate::jmap::set_error(e)),
        None => Ok(()),
    }
}

// ---------------------------------------------------------------- the run

struct Job { row: String, client: Arc<Client>, msg: Value, origin: Option<String> }

/// The confirmed report of `ids`: one report per message, each filed in Junk, one toast.
pub fn run(u: &AppWindow, st: &Shared, ids: Vec<String>) {
    let Some(setup) = phish::setup_from_settings() else { mail::show_toast(u, NO_ADDRESS, false); return };
    if BUSY.with(|b| b.get()) { mail::show_toast(u, "Still reporting. Wait for it to finish.", false); return; }
    let p = plan(&rows_of(&ids));
    if p.ok.is_empty() { mail::show_toast(u, &nothing_text(&p), false); return; }
    let mut jobs = vec![];
    let mut failed: Vec<String> = vec![];
    for row in p.ok {
        let (Some(mut msg), Some(client)) = (mail::email(&row), crate::client_for(st, &row)) else { failed.push("The account is not connected.".into()); continue };
        let origin = phish::origin_mailbox(&msg);
        msg["id"] = Value::String(crate::unified::real_id(&row).to_string());
        jobs.push(Job { row, client, msg, origin });
    }
    BUSY.with(|b| b.set(true));
    mail::show_toast(u, &format!("Reporting {} message{}…", jobs.len(), if jobs.len() == 1 { "" } else { "s" }), false);
    let w = u.as_weak();
    std::thread::spawn(move || {
        let done = report_jobs(&setup, jobs);
        let _ = w.upgrade_in_event_loop(move |u| finish(&u, &setup, done, failed));
    });
}

/// One report's result.
struct Done { row: String, client: Arc<Client>, id: String, origin: Option<String>, res: Result<(), Failure>, skipped: bool }

fn report_jobs(setup: &Setup, jobs: Vec<Job>) -> Vec<Done> {
    jobs.into_iter().map(|j| {
        let id = j.msg["id"].as_str().unwrap_or("").to_string();
        // judged by this login's own folders, before anything is sent (the list's roles are the
        // open account's): a Sent, Drafts or Scheduled message of another login is not reported
        if j.client.role_ids().is_ok_and(|roles| phish::in_own_mail(&j.msg, &roles)) {
            return Done { row: j.row, client: j.client, id, origin: j.origin, res: Ok(()), skipped: true };
        }
        let res = phish::report(&j.client, &j.msg, setup);
        // reported but not filed: the keyword alone still turns the links off
        if matches!(res, Err(Failure::NotMoved(_))) { let _ = j.client.set_keyword(std::slice::from_ref(&id), phish::KEYWORD, true); }
        Done { row: j.row, client: j.client, id, origin: j.origin, res, skipped: false }
    }).collect()
}

fn finish(u: &AppWindow, setup: &Setup, done: Vec<Done>, mut failed: Vec<String>) {
    BUSY.with(|b| b.set(false));
    let (mut filed, mut undo, mut not_moved, mut skipped) = (vec![], vec![], vec![], 0usize);
    for d in done {
        if d.skipped { skipped += 1; continue; }
        match d.res {
            Ok(()) => {
                filed.push((d.client.clone(), d.row.clone()));
                if let Some(origin) = d.origin { undo.push(UndoItem { client: d.client, id: d.id, origin }); }
            }
            Err(Failure::NotMoved(e)) => {
                mail::mark_phishing_local(&d.row);
                crate::reader::phishing_reported(u, &d.row);
                not_moved.push(e);
            }
            Err(Failure::NotReported(e)) => failed.push(e),
        }
    }
    let moved = filed.len();
    if !filed.is_empty() { mail::phishing_filed_all(u, &filed); }
    let mut text = outcome_text(&setup.address, moved, &not_moved, &failed);
    if skipped > 0 {
        let note = format!("{skipped} sent, draft or scheduled message{} left out.", if skipped == 1 { " was" } else { "s were" });
        text = if text.is_empty() { note } else { format!("{text} {note}") };
    }
    offer_undo(u, &text, undo);
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    fn msg(blob: bool) -> Value {
        let mut m = json!({"id": "m", "keywords": {"$seen": true}, "mailboxIds": {"i": true}});
        if blob { m["blobId"] = json!("b"); }
        m
    }
    fn row(id: &str, m: Option<Value>, roles: &[&str]) -> (String, Option<Value>, Vec<String>) {
        (id.to_string(), m, roles.iter().map(|r| r.to_string()).collect())
    }

    #[test]
    fn a_selection_is_sorted_into_what_can_be_reported() {
        let shared = crate::unified::row_id("ann@example.test", Some("acct-g"), "s1");
        let mut reported = msg(true);
        reported["keywords"]["$phishing"] = json!(true);
        let p = plan(&[
            row("a", Some(msg(true)), &["inbox"]),
            row(&shared, Some(msg(true)), &["inbox"]),
            row("sent", Some(msg(true)), &["sent"]),
            row("again", Some(reported), &["inbox"]),
            row("gone", None, &[]),
            row("noblob", Some(msg(false)), &["inbox"]),
            row("b", Some(msg(true)), &["junk"]),
        ]);
        assert_eq!(p.ok, vec!["a".to_string(), "b".to_string()]);
        assert_eq!((p.shared, p.skipped), (1, 4));
        assert_eq!(plan(&[]), Plan::default());
    }

    #[test]
    fn what_is_said_when_nothing_can_be_reported() {
        assert_eq!(nothing_text(&Plan { ok: vec![], shared: 1, skipped: 0 }), "A message in a shared account can't be reported from here.");
        assert_eq!(nothing_text(&Plan { ok: vec![], shared: 3, skipped: 0 }), "Messages in a shared account can't be reported from here.");
        assert_eq!(nothing_text(&Plan { ok: vec![], shared: 0, skipped: 2 }), "Sent, draft and already reported messages can't be reported.");
        assert!(nothing_text(&Plan { ok: vec![], shared: 1, skipped: 1 }).contains("shared account") && nothing_text(&Plan { ok: vec![], shared: 1, skipped: 1 }).contains("Sent, draft"));
        assert_eq!(left_out_text(&Plan { ok: vec!["a".into()], shared: 0, skipped: 0 }), "");
        assert_eq!(left_out_text(&Plan { ok: vec!["a".into()], shared: 1, skipped: 1 }), "2 messages are left out (shared account, sent, draft or already reported).");
        assert!(left_out_text(&Plan { ok: vec![], shared: 1, skipped: 0 }).starts_with("1 message is left out"));
    }

    #[test]
    fn the_question_names_the_address_and_the_count() {
        assert_eq!(confirm_text("sec@example.test", 1), "The message goes to sec@example.test and moves to Junk.");
        assert_eq!(confirm_text("sec@example.test", 3), "Each of the 3 messages goes to sec@example.test as its own report and moves to Junk.");
    }

    #[test]
    fn the_toast_follows_bulwark() {
        let a = "sec@example.test";
        assert_eq!(outcome_text(a, 1, &[], &[]), "Reported to sec@example.test and moved to Junk.");
        assert_eq!(outcome_text(a, 3, &[], &[]), "3 messages reported to sec@example.test and moved to Junk.");
        assert_eq!(outcome_text(a, 0, &["No Junk folder".into()], &[]), "Reported to sec@example.test, but the message couldn't be moved to Junk: No Junk folder");
        assert!(outcome_text(a, 1, &["x".into(), "y".into()], &[]).contains("but 2 messages couldn't be moved to Junk: x"));
        assert_eq!(outcome_text(a, 0, &[], &["Refused".into()]), "The message couldn't be reported: Refused");
        // some of each: everything is said
        let t = outcome_text(a, 2, &[], &["a".into(), "b".into()]);
        assert!(t.starts_with("2 messages reported") && t.ends_with("2 messages couldn't be reported: a"));
        assert_eq!(undone_text(0), "Couldn't move it back.");
        assert_eq!(undone_text(1), "Moved back. The report was already sent.");
        assert_eq!(undone_text(4), "Moved back. The reports were already sent.");
    }
}
