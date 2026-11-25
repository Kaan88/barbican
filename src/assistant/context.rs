//! What the panel tells the model about the open message (docs/AI-ASSISTANT-SPEC.md 2.0): the
//! message as text (`messageForModel`, from the extension's `lib/mail.js`, here over a JMAP
//! `Email` object), the user's identity line and the user turn (`buildUserTurn`, from
//! `panel/panel.js`), with the set of messages already sent in this conversation
//! ([`SentContext`]).
use chrono::{DateTime, TimeZone};
use serde_json::Value;
use std::collections::HashSet;

/// `MAX_BODY`: the body is cut to this many characters.
pub const MAX_BODY: usize = 12000;

/// The passage the user highlighted is cut to this many characters.
pub const MAX_PASSAGE: usize = 4000;

/// The `Email/get` properties [`message_for_model`] reads (with `fetchTextBodyValues` and
/// `fetchHTMLBodyValues` on).
pub const EMAIL_PROPERTIES: [&str; 11] =
    ["id", "from", "to", "cc", "sentAt", "receivedAt", "subject", "textBody", "htmlBody", "bodyValues", "attachments"];

/// `shortDate`: the time in the local zone with its offset, `YYYY-MM-DDTHH:MM+HH:MM`, so "today"
/// and "10:00" mean what the user sees. Empty for none; a date that does not parse as it is.
pub fn short_date(d: &str) -> String {
    short_date_in(d, &chrono::Local)
}

/// [`short_date`] in a given zone.
pub fn short_date_in<Tz: TimeZone>(d: &str, tz: &Tz) -> String
where
    Tz::Offset: std::fmt::Display,
{
    if d.is_empty() {
        return String::new();
    }
    match DateTime::parse_from_rfc3339(d) {
        Ok(t) => t.with_timezone(tz).format("%Y-%m-%dT%H:%M%:z").to_string(),
        Err(_) => d.to_string(),
    }
}

/// An address list as Thunderbird shows a header: `Name <email>`, or the address alone.
fn addresses(v: &Value) -> Vec<String> {
    v.as_array()
        .map(|a| {
            a.iter()
                .map(|x| {
                    let (name, email) = (x["name"].as_str().unwrap_or(""), x["email"].as_str().unwrap_or(""));
                    if name.is_empty() {
                        email.to_string()
                    } else {
                        format!("{name} <{email}>")
                    }
                })
                .collect()
        })
        .unwrap_or_default()
}

/// `tidy`: no-break spaces to spaces, no spaces before a line break, at most one blank line,
/// trimmed.
fn tidy(text: &str) -> String {
    static SPACES: std::sync::OnceLock<regex::Regex> = std::sync::OnceLock::new();
    static BLANKS: std::sync::OnceLock<regex::Regex> = std::sync::OnceLock::new();
    let t = text.replace('\u{a0}', " ");
    let t = SPACES.get_or_init(|| regex::Regex::new(r"[ \t]+\n").unwrap()).replace_all(&t, "\n");
    let t = BLANKS.get_or_init(|| regex::Regex::new(r"\n{3,}").unwrap()).replace_all(&t, "\n\n");
    t.trim().to_string()
}

/// The parts of `key` (`textBody` or `htmlBody`) of the media type `mime`, with their text.
fn parts<'a>(m: &'a Value, key: &str, mime: &str) -> Vec<&'a str> {
    m[key]
        .as_array()
        .map(|list| {
            list.iter()
                .filter(|p| p["type"].as_str().is_some_and(|t| t.eq_ignore_ascii_case(mime)))
                .filter_map(|p| m["bodyValues"][p["partId"].as_str()?]["value"].as_str())
                .collect()
        })
        .unwrap_or_default()
}

/// `messageBody`: the plain-text parts joined by a blank line, or else every HTML part as text;
/// tidied, and cut to `max` characters with a note of how much was cut.
pub fn message_body(m: &Value, max: usize) -> String {
    let plain = parts(m, "textBody", "text/plain");
    let text = if plain.is_empty() {
        parts(m, "htmlBody", "text/html").iter().map(|h| crate::html_to_text(h) + "\n\n").collect::<String>()
    } else {
        plain.join("\n\n")
    };
    let text = tidy(&text);
    let len = text.chars().count();
    if len > max {
        format!("{}\n[… {} more characters cut]", text.chars().take(max).collect::<String>(), len - max)
    } else {
        text
    }
}

/// `attachmentsOf`: `name (type, N KB)` each.
pub fn attachments_of(m: &Value) -> Vec<String> {
    m["attachments"]
        .as_array()
        .map(|list| {
            list.iter()
                .map(|a| {
                    let kb = (a["size"].as_f64().unwrap_or(0.0) / 1024.0).round();
                    format!("{} ({}, {kb} KB)", a["name"].as_str().unwrap_or(""), a["type"].as_str().unwrap_or(""))
                })
                .collect()
        })
        .unwrap_or_default()
}

/// The path of a mailbox as Thunderbird writes a folder's: `/Parent/Child`, from the account's
/// `Mailbox/get` list; none for an id not in it.
pub fn mailbox_path(mailboxes: &[Value], id: &str) -> Option<String> {
    let mut names = Vec::new();
    let mut cur = Some(id.to_string());
    while let Some(id) = cur {
        let m = mailboxes.iter().find(|m| m["id"].as_str() == Some(&id))?;
        names.push(m["name"].as_str().unwrap_or("").to_string());
        cur = m["parentId"].as_str().map(String::from);
        if names.len() > mailboxes.len() {
            break; // a loop in the parents
        }
    }
    names.reverse();
    Some(format!("/{}", names.join("/")))
}

/// `messageForModel`: a message as text for the model, wrapped so it reads as data. `m` is a JMAP
/// `Email` with [`EMAIL_PROPERTIES`]; `folder` the path of its mailbox ([`mailbox_path`]).
pub fn message_for_model(m: &Value, folder: Option<&str>) -> String {
    message_for_model_in(m, folder, MAX_BODY, &chrono::Local)
}

/// [`message_for_model`] with a body limit and a zone.
pub fn message_for_model_in<Tz: TimeZone>(m: &Value, folder: Option<&str>, max: usize, tz: &Tz) -> String
where
    Tz::Offset: std::fmt::Display,
{
    let body = message_body(m, max);
    let att = attachments_of(m);
    let mut lines = vec![
        format!("<email id=\"{}\">", m["id"].as_str().unwrap_or("")),
        format!("From: {}", addresses(&m["from"]).join(", ")),
        format!("To: {}", addresses(&m["to"]).join(", ")),
    ];
    let cc = addresses(&m["cc"]);
    if !cc.is_empty() {
        lines.push(format!("Cc: {}", cc.join(", ")));
    }
    // the Date header, as Thunderbird's message date; the arrival when there is none
    let date = m["sentAt"].as_str().or(m["receivedAt"].as_str()).unwrap_or("");
    lines.push(format!("Date: {}", short_date_in(date, tz)));
    lines.push(format!("Subject: {}", m["subject"].as_str().unwrap_or("")));
    if let Some(f) = folder {
        lines.push(format!("Folder: {f}"));
    }
    if !att.is_empty() {
        lines.push(format!("Attachments: {}", att.join("; ")));
    }
    lines.push(String::new());
    lines.push(if body.is_empty() { "(no text)".into() } else { body });
    lines.push("</email>".into());
    lines.join("\n")
}

/// `identityLine`: the user as the system prompt names them, from the first identity of the
/// account that owns the open message (else of the first account): `Name <email>`, or the
/// address alone; empty without one.
pub fn identity_line(identity: Option<&Value>) -> String {
    let Some(id) = identity else { return String::new() };
    let (name, email) = (id["name"].as_str().unwrap_or(""), id["email"].as_str().unwrap_or(""));
    if email.is_empty() && name.is_empty() {
        String::new()
    } else if name.is_empty() {
        email.to_string()
    } else {
        format!("{name} <{email}>")
    }
}

/// The messages already sent to the model in this conversation (`sentContext`), by id. New chat
/// clears it.
#[derive(Clone, Debug, Default)]
pub struct SentContext(HashSet<String>);

impl SentContext {
    pub fn clear(&mut self) {
        self.0.clear();
    }

    pub fn contains(&self, id: &str) -> bool {
        self.0.contains(id)
    }

    pub fn is_empty(&self) -> bool {
        self.0.is_empty()
    }
}

/// The message open in the reader.
#[derive(Clone, Copy, Debug)]
pub struct OpenMessage<'a> {
    pub id: &'a str,
    pub subject: &'a str,
}

/// `buildUserTurn(text)`: the "Now:" line; the open message in full the first time in this
/// conversation (`load` gives its [`message_for_model`] text), later only by id and subject, or
/// that none is open any more; the highlighted passage; then the user's text. Parts are joined
/// with a blank line. An error from `load` is the send's error, and the message is not counted
/// as sent.
pub fn build_user_turn(
    sent: &mut SentContext,
    now: &str,
    open: Option<OpenMessage<'_>>,
    selection: &str,
    text: &str,
    load: impl FnOnce(&str) -> Result<String, String>,
) -> Result<String, String> {
    let mut parts = vec![now.to_string()];
    if let Some(msg) = open {
        if !sent.contains(msg.id) {
            let email = load(msg.id)?;
            parts.push(format!("The user has this message open:\n{email}"));
            sent.0.insert(msg.id.to_string());
        } else {
            parts.push(format!("The open message is still <email id=\"{}\"> “{}”.", msg.id, msg.subject));
        }
    } else if !sent.is_empty() {
        parts.push("No message is open now.".into());
    }
    if !selection.is_empty() {
        let passage: String = selection.chars().take(MAX_PASSAGE).collect();
        parts.push(format!("The user highlighted this passage in the open message:\n<passage>\n{passage}\n</passage>"));
    }
    parts.push(format!("The user asks:\n{text}"));
    Ok(parts.join("\n\n"))
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    fn istanbul() -> chrono_tz::Tz {
        "Europe/Istanbul".parse().unwrap()
    }

    fn email() -> Value {
        json!({
            "id": "M1",
            "from": [{"name": "Ann Example", "email": "ann@example.test"}],
            "to": [{"name": null, "email": "me@example.test"}, {"name": "Bob", "email": "bob@example.test"}],
            "cc": [],
            "sentAt": "2026-11-25T09:05:00Z",
            "receivedAt": "2026-11-25T09:06:00Z",
            "subject": "Lunch",
            "textBody": [{"partId": "1", "type": "text/plain"}],
            "htmlBody": [{"partId": "2", "type": "text/html"}],
            "bodyValues": {"1": {"value": "Hi,\u{a0}lunch on Thursday?  \n\n\n\nAnn\n"}, "2": {"value": "<p>ignored</p>"}},
            "attachments": [{"name": "menu.pdf", "type": "application/pdf", "size": 1536}, {"name": "map.png", "type": "image/png", "size": 500}]
        })
    }

    #[test]
    fn short_dates() {
        assert_eq!(short_date_in("2026-11-25T09:05:00Z", &istanbul()), "2026-11-25T12:05+03:00");
        let ny: chrono_tz::Tz = "America/St_Johns".parse().unwrap();
        assert_eq!(short_date_in("2026-01-05T12:00:00Z", &ny), "2026-01-05T08:30-03:30");
        assert_eq!(short_date_in("2026-01-05T12:00:00Z", &chrono::Utc), "2026-01-05T12:00+00:00");
        assert_eq!(short_date_in("", &chrono::Utc), "");
        assert_eq!(short_date_in("yesterday", &chrono::Utc), "yesterday");
    }

    #[test]
    fn a_message_for_the_model() {
        assert_eq!(
            message_for_model_in(&email(), Some("/Inbox"), MAX_BODY, &istanbul()),
            "<email id=\"M1\">\nFrom: Ann Example <ann@example.test>\nTo: me@example.test, Bob <bob@example.test>\nDate: 2026-11-25T12:05+03:00\nSubject: Lunch\nFolder: /Inbox\nAttachments: menu.pdf (application/pdf, 2 KB); map.png (image/png, 0 KB)\n\nHi, lunch on Thursday?\n\nAnn\n</email>"
        );
        // Cc, no folder, no attachments, HTML only, cut
        let mut m = email();
        m["cc"] = json!([{"name": "", "email": "cy@example.test"}]);
        m["attachments"] = json!([]);
        m["textBody"] = json!([{"partId": "2", "type": "text/html"}]);
        m["bodyValues"]["2"]["value"] = json!("<p>Hello <b>there</b></p><p>second</p>");
        assert_eq!(
            message_for_model_in(&m, None, 8, &chrono::Utc),
            "<email id=\"M1\">\nFrom: Ann Example <ann@example.test>\nTo: me@example.test, Bob <bob@example.test>\nCc: cy@example.test\nDate: 2026-11-25T09:05+00:00\nSubject: Lunch\n\nHello th\n[… 10 more characters cut]\n</email>"
        );
        // no body at all; the arrival date when there is no Date header
        let m = json!({"id": "M2", "subject": "", "receivedAt": "2026-11-25T09:06:00Z"});
        assert_eq!(message_for_model_in(&m, None, MAX_BODY, &chrono::Utc), "<email id=\"M2\">\nFrom: \nTo: \nDate: 2026-11-25T09:06+00:00\nSubject: \n\n(no text)\n</email>");
    }

    #[test]
    fn plain_parts_join_with_a_blank_line() {
        let m = json!({"textBody": [{"partId": "a", "type": "text/plain"}, {"partId": "i", "type": "image/png"}, {"partId": "b", "type": "TEXT/PLAIN"}],
            "bodyValues": {"a": {"value": "one \t\n"}, "b": {"value": "two"}}});
        assert_eq!(message_body(&m, MAX_BODY), "one\n\ntwo");
        assert_eq!(message_body(&json!({}), MAX_BODY), "");
        let long = json!({"textBody": [{"partId": "a", "type": "text/plain"}], "bodyValues": {"a": {"value": "ééééé"}}});
        assert_eq!(message_body(&long, 3), "ééé\n[… 2 more characters cut]");
    }

    #[test]
    fn mailbox_paths() {
        let boxes = [json!({"id": "a", "name": "Inbox"}), json!({"id": "b", "name": "Work", "parentId": "a"}), json!({"id": "c", "name": "2026", "parentId": "b"})];
        assert_eq!(mailbox_path(&boxes, "c").as_deref(), Some("/Inbox/Work/2026"));
        assert_eq!(mailbox_path(&boxes, "a").as_deref(), Some("/Inbox"));
        assert_eq!(mailbox_path(&boxes, "z"), None);
        let looped = [json!({"id": "a", "name": "A", "parentId": "b"}), json!({"id": "b", "name": "B", "parentId": "a"})];
        assert!(mailbox_path(&looped, "a").is_some());
    }

    #[test]
    fn identity_lines() {
        assert_eq!(identity_line(Some(&json!({"name": "Ann Example", "email": "ann@example.test"}))), "Ann Example <ann@example.test>");
        assert_eq!(identity_line(Some(&json!({"name": "", "email": "ann@example.test"}))), "ann@example.test");
        assert_eq!(identity_line(None), "");
    }

    #[test]
    fn user_turn_variants() {
        let now = "Now: Wednesday 25 November 2026 at 14:05 (Europe/Istanbul).";
        let mut sent = SentContext::default();
        // nothing open, nothing sent yet
        assert_eq!(build_user_turn(&mut sent, now, None, "", "Hi", |_| unreachable!()).unwrap(), format!("{now}\n\nThe user asks:\nHi"));
        // the message in full the first time
        let open = OpenMessage { id: "M1", subject: "Lunch" };
        let turn = build_user_turn(&mut sent, now, Some(open), "", "Summarize it", |id| Ok(format!("<email id=\"{id}\">…</email>"))).unwrap();
        assert_eq!(turn, format!("{now}\n\nThe user has this message open:\n<email id=\"M1\">…</email>\n\nThe user asks:\nSummarize it"));
        // then by id, with a passage
        let turn = build_user_turn(&mut sent, now, Some(open), "on Thursday", "When?", |_| unreachable!()).unwrap();
        assert_eq!(
            turn,
            format!("{now}\n\nThe open message is still <email id=\"M1\"> “Lunch”.\n\nThe user highlighted this passage in the open message:\n<passage>\non Thursday\n</passage>\n\nThe user asks:\nWhen?")
        );
        // none open any more
        assert_eq!(build_user_turn(&mut sent, now, None, "", "And?", |_| unreachable!()).unwrap(), format!("{now}\n\nNo message is open now.\n\nThe user asks:\nAnd?"));
        // a failed load is the error and the message is not counted
        let other = OpenMessage { id: "M2", subject: "x" };
        assert_eq!(build_user_turn(&mut sent, now, Some(other), "", "?", |_| Err("Message not found.".into())), Err("Message not found.".into()));
        assert!(!sent.contains("M2"));
        // a long passage is cut to 4000 characters
        let long = "ü".repeat(4100);
        let turn = build_user_turn(&mut SentContext::default(), now, None, &long, "?", |_| unreachable!()).unwrap();
        assert!(turn.contains(&format!("<passage>\n{}\n</passage>", "ü".repeat(4000))));
        sent.clear();
        assert!(sent.is_empty());
    }
}
