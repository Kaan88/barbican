//! Import .eml and .mbox files into a folder (Bulwark mail-transfer/mail-import-dialog and
//! lib/mail-import: mbox.ts, mbox-status.ts, engine.ts): each message is uploaded as a blob
//! and filed with Email/import (RFC 8621 §4.8), keeping its read / answered / flagged state
//! from the mbox status headers and its arrival time from its Date.
use crate::jmap::Client;
use serde_json::{json, Map, Value};

/// One message to import: its bytes, keywords and arrival time (RFC 3339).
#[derive(Debug, Clone, PartialEq)]
pub struct Item { pub bytes: Vec<u8>, pub keywords: Vec<String>, pub received_at: Option<String> }

fn lines(bytes: &[u8]) -> Vec<&[u8]> {
    let mut out = vec![];
    let mut start = 0;
    for (i, b) in bytes.iter().enumerate() {
        if *b == b'\n' { out.push(&bytes[start..=i]); start = i + 1; }
    }
    if start < bytes.len() { out.push(&bytes[start..]); }
    out
}

fn is_empty_line(l: &[u8]) -> bool { l == b"\n" || l == b"\r\n" }

fn strip_eol(l: &[u8]) -> &[u8] {
    let mut e = l.len();
    if e > 0 && l[e - 1] == b'\n' { e -= 1; }
    if e > 0 && l[e - 1] == b'\r' { e -= 1; }
    &l[..e]
}

/// "From sender Tue May  5 10:11:12 2020" (optionally a zone before the year, or "remote from
/// host" after it): an envelope line, not a body line "From now on…" (Bulwark isEnvelopeLine).
pub fn is_envelope_line(l: &[u8]) -> bool {
    let t = String::from_utf8_lossy(strip_eol(l)).to_string();
    let Some(rest) = t.strip_prefix("From ") else { return false };
    let mut words: Vec<&str> = rest.split_whitespace().collect();
    if words.len() < 3 { return false; }
    if words.len() >= 3 && words[words.len() - 3] == "remote" && words[words.len() - 2] == "from" { words.truncate(words.len() - 3); }
    let Some(year) = words.last() else { return false };
    if year.len() != 4 || !year.chars().all(|c| c.is_ascii_digit()) { return false; }
    // a time hh:mm(:ss) among the words after the sender
    words[1..].iter().any(|w| {
        let p: Vec<&str> = w.split(':').collect();
        (p.len() == 2 || p.len() == 3) && p[0].len() <= 2 && p.iter().all(|x| !x.is_empty() && x.chars().all(|c| c.is_ascii_digit()))
    })
}

/// Split an mbox file into messages (mboxo / mboxrd): a separator is a `From ` envelope line at
/// the start or after an empty line; that empty line belongs to the format and is dropped, and
/// `>From ` lines lose one `>`. Bytes are otherwise kept as they are.
pub fn split_mbox(bytes: &[u8]) -> Vec<Vec<u8>> {
    let mut out: Vec<Vec<u8>> = vec![];
    let mut cur: Option<Vec<u8>> = None;
    let mut pending_empty: Option<&[u8]> = None;
    for l in lines(bytes) {
        let separator = l.starts_with(b"From ") && (cur.is_none() || pending_empty.is_some()) && is_envelope_line(l);
        if separator {
            pending_empty = None;
            if let Some(m) = cur.take() { out.push(m); }
            cur = Some(vec![]);
            continue;
        }
        let Some(m) = cur.as_mut() else { continue };
        if let Some(e) = pending_empty.take() { m.extend_from_slice(e); }
        if is_empty_line(l) { pending_empty = Some(l); continue; }
        let quotes = l.iter().take_while(|b| **b == b'>').count();
        if quotes > 0 && l[quotes..].starts_with(b"From ") { m.extend_from_slice(&l[1..]); } else { m.extend_from_slice(l); }
    }
    if let Some(m) = cur { out.push(m); }
    out.into_iter().filter(|m| !m.iter().all(|b| b.is_ascii_whitespace())).collect()
}

/// The header block of a message (up to the first empty line), unfolded, by lowercased name.
pub fn header_fields(bytes: &[u8]) -> Vec<(String, String)> {
    let text = String::from_utf8_lossy(bytes);
    let head = text.split("\r\n\r\n").next().unwrap_or("").split("\n\n").next().unwrap_or("").to_string();
    let mut out: Vec<(String, String)> = vec![];
    for line in head.replace("\r\n", "\n").split('\n') {
        if line.starts_with(' ') || line.starts_with('\t') {
            if let Some(last) = out.last_mut() { last.1.push(' '); last.1.push_str(line.trim()); }
            continue;
        }
        if let Some((n, v)) = line.split_once(':') {
            if !n.trim().is_empty() && !n.contains(' ') { out.push((n.trim().to_lowercase(), v.trim().to_string())); }
        }
    }
    out
}

fn field<'a>(f: &'a [(String, String)], name: &str) -> Option<&'a str> { f.iter().find(|(n, _)| n == name).map(|(_, v)| v.as_str()) }

/// Keywords from the status headers mail programs write into mbox files, and whether the
/// message was deleted (Thunderbird's expunged flag) (Bulwark mboxState). With none of them it
/// is imported as read.
pub fn mbox_state(f: &[(String, String)]) -> (Vec<String>, bool) {
    let (status, xstatus, moz, gmail) = (field(f, "status"), field(f, "x-status"), field(f, "x-mozilla-status"), field(f, "x-gmail-labels"));
    let mut kw: Vec<String> = vec![];
    let mut add = |k: &str| { let k = k.to_lowercase(); if !kw.contains(&k) { kw.push(k); } };
    let mut expunged = false;
    if status.is_none() && xstatus.is_none() && moz.is_none() && gmail.is_none() { add("$seen"); }
    if status.is_some_and(|s| s.to_uppercase().contains('R')) { add("$seen"); }
    if let Some(x) = xstatus.map(str::to_uppercase) {
        if x.contains('A') { add("$answered"); }
        if x.contains('F') { add("$flagged"); }
        if x.contains('T') { add("$draft"); }
    }
    if let Some(flags) = moz.filter(|m| m.len() <= 8).and_then(|m| u32::from_str_radix(m, 16).ok()) {
        if flags & 0x1 != 0 { add("$seen"); }
        if flags & 0x2 != 0 { add("$answered"); }
        if flags & 0x4 != 0 { add("$flagged"); }
        if flags & 0x8 != 0 { expunged = true; }
        if flags & 0x1000 != 0 { add("$forwarded"); }
    }
    if let Some(g) = gmail {
        let labels: Vec<String> = g.split(',').map(|l| l.trim().to_lowercase()).collect();
        if !labels.iter().any(|l| l == "unread") { add("$seen"); }
        if labels.iter().any(|l| l == "starred") { add("$flagged"); }
    }
    for v in [field(f, "x-keywords"), field(f, "x-mozilla-keys")].into_iter().flatten() {
        for k in v.split(|c: char| c.is_whitespace() || c == ',').filter(|k| !k.is_empty()) {
            let k = match k.to_lowercase().as_str() { "\\seen" => "$seen".to_string(), "\\flagged" => "$flagged".into(), "\\answered" => "$answered".into(), "\\draft" => "$draft".into(), o => o.to_string() };
            if crate::jmap::valid_keyword(&k) && !["$recent", "\\recent", "$hasattachment", "$hasnoattachment"].contains(&k.as_str()) { add(&k); }
        }
    }
    (kw, expunged)
}

/// The arrival time from the Date header (RFC 5322), as RFC 3339 UTC.
pub fn received_at(f: &[(String, String)]) -> Option<String> {
    let d = field(f, "date")?;
    // drop a trailing comment such as "(UTC)"
    let d = d.split(" (").next().unwrap_or(d).trim();
    chrono::DateTime::parse_from_rfc2822(d).ok().map(|t| t.with_timezone(&chrono::Utc).format("%Y-%m-%dT%H:%M:%SZ").to_string())
}

/// The messages of one file: an .mbox (or anything starting with an envelope line) is split,
/// anything else is one message. Deleted (expunged) messages are left out.
pub fn items_of(name: &str, bytes: &[u8]) -> Vec<Item> {
    let lower = name.to_lowercase();
    let mbox = lower.ends_with(".mbox") || lower.ends_with(".mbx") || lower.ends_with(".mbs") || bytes.starts_with(b"From ") && lines(bytes).first().is_some_and(|l| is_envelope_line(l));
    let msgs = if mbox { split_mbox(bytes) } else { vec![bytes.to_vec()] };
    msgs.into_iter().filter_map(|m| {
        let f = header_fields(&m);
        let (keywords, expunged) = mbox_state(&f);
        (!expunged).then(|| Item { received_at: received_at(&f), keywords, bytes: m })
    }).collect()
}

/// The Email/import entry of an uploaded message.
pub fn import_entry(blob: &str, mailbox: &str, it: &Item) -> Value {
    let kw: Map<String, Value> = it.keywords.iter().map(|k| (k.clone(), json!(true))).collect();
    let mut e = json!({"blobId": blob, "mailboxIds": { mailbox: true }, "keywords": kw});
    if let Some(r) = &it.received_at { e["receivedAt"] = json!(r); }
    e
}

/// Import `items` into `mailbox`: upload each, then Email/import in batches the server takes.
/// `progress(done, total)` reports along the way. Returns (imported, refusals).
pub fn import(c: &Client, mailbox: &str, items: &[Item], progress: &dyn Fn(usize, usize)) -> (usize, Vec<String>) {
    let mut ok = 0;
    let mut errors: Vec<String> = vec![];
    let batch = c.max_set().clamp(1, 50);
    for (n, chunk) in items.chunks(batch).enumerate() {
        let mut entries = Map::new();
        for (i, it) in chunk.iter().enumerate() {
            if it.bytes.len() > c.max_upload() { errors.push("A message is larger than the server takes.".into()); continue; }
            match c.upload(it.bytes.clone(), "message/rfc822") {
                Ok((blob, _)) => { entries.insert(format!("m{i}"), import_entry(&blob, mailbox, it)); }
                Err(e) => errors.push(e),
            }
        }
        if !entries.is_empty() {
            let count = entries.len();
            match c.call(json!([["Email/import", {"accountId": c.account, "emails": entries}, "i"]])) {
                Ok(r) => {
                    ok += r[0][1]["created"].as_object().map(|o| o.len()).unwrap_or(0);
                    if let Some(bad) = r[0][1]["notCreated"].as_object() {
                        errors.extend(bad.values().map(crate::jmap::set_error));
                    }
                }
                Err(e) => errors.extend(std::iter::repeat_n(e, count)),
            }
        }
        progress(((n + 1) * batch).min(items.len()), items.len());
    }
    (ok, errors)
}

/// "Import messages…" on a folder: pick .eml / .mbox files, then import them in the background
/// with a running count, and bring the folder list and the message list up to date.
pub fn start(u: &crate::AppWindow, c: std::sync::Arc<Client>, mailbox: &str) {
    use slint::ComponentHandle;
    let Some(files) = crate::automation::pick_files() else { return };
    if files.is_empty() { return; }
    let (w, mailbox) = (u.as_weak(), mailbox.to_string());
    crate::mail::show_toast(u, "Reading the files…", false);
    std::thread::spawn(move || {
        let mut items = vec![];
        let mut unreadable = 0;
        for f in &files {
            let name = f.file_name().map(|n| n.to_string_lossy().to_string()).unwrap_or_default();
            match std::fs::read(f) { Ok(b) => items.extend(items_of(&name, &b)), Err(_) => unreadable += 1 }
        }
        if items.is_empty() {
            let _ = w.upgrade_in_event_loop(|u| crate::mail::show_toast(&u, "No messages found in those files.", false));
            return;
        }
        let w2 = w.clone();
        let (ok, errors) = import(&c, &mailbox, &items, &|done, total| {
            let msg = format!("Importing… {done} of {total}");
            let _ = w2.upgrade_in_event_loop(move |u| u.set_toast(msg.into()));
        });
        let failed = errors.len() + unreadable;
        let msg = summary(ok, failed, errors.first().map(String::as_str));
        let w3 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| {
            crate::mail::show_toast(&u, &msg, false);
            crate::mail::load_mailboxes(w3.clone(), c.clone(), false);
            crate::mail::sync(w3, c);
        });
    });
}

/// "Imported 12 messages", with what went wrong when something did.
pub fn summary(ok: usize, failed: usize, first_error: Option<&str>) -> String {
    let n = |k: usize| if k == 1 { "1 message".to_string() } else { format!("{k} messages") };
    match (failed, first_error) {
        (0, _) => format!("Imported {}", n(ok)),
        (f, Some(e)) => format!("Imported {}, {} not imported: {e}", n(ok), f),
        (f, None) => format!("Imported {}, {} not imported", n(ok), f),
    }
}

#[cfg(test)]
mod tests {
    #[test]
    fn summaries() {
        assert_eq!(super::summary(1, 0, None), "Imported 1 message");
        assert_eq!(super::summary(3, 1, Some("The message is invalid.")), "Imported 3 messages, 1 not imported: The message is invalid.");
    }

    use super::*;

    const MBOX: &[u8] = b"From a@b.c Tue May  5 10:11:12 2020\nSubject: one\nStatus: RO\nX-Status: F\nDate: Tue, 05 May 2020 10:11:12 +0200\n\nHello\n>From the start\nFrom now on, unquoted\n\nFrom c@d.e Wed May  6 08:00:00 2020\r\nSubject: two\r\nX-Mozilla-Status: 0008\r\n\r\nGone\r\n\r\nFrom x@y.z Thu May  7 09:00 +0000 2020\nSubject: three\n\nBody\n";

    #[test]
    fn splits_mbox() {
        let m = split_mbox(MBOX);
        assert_eq!(m.len(), 3);
        let one = String::from_utf8(m[0].clone()).unwrap();
        assert!(one.starts_with("Subject: one\n"));
        // one ">" comes off, a body "From " after a line of text is not a separator, the empty
        // line before the next separator is dropped
        assert!(one.ends_with("Hello\nFrom the start\nFrom now on, unquoted\n"), "{one:?}");
        assert!(String::from_utf8_lossy(&m[1]).ends_with("Gone\r\n"));
        assert!(String::from_utf8_lossy(&m[2]).contains("Subject: three"));
    }

    #[test]
    fn envelope_lines() {
        assert!(is_envelope_line(b"From a@b.c Tue May  5 10:11:12 2020\n"));
        assert!(is_envelope_line(b"From MAILER-DAEMON Thu May  7 09:00 +0000 2020"));
        assert!(is_envelope_line(b"From a Tue May  5 10:11:12 2020 remote from host"));
        assert!(!is_envelope_line(b"From now on, we meet at 10:00 every day\n"));
        assert!(!is_envelope_line(b"Subject: x"));
    }

    #[test]
    fn states_and_dates() {
        let items = items_of("box.mbox", MBOX);
        // the expunged one is left out
        assert_eq!(items.len(), 2);
        assert_eq!(items[0].keywords, vec!["$seen".to_string(), "$flagged".to_string()]);
        assert_eq!(items[0].received_at.as_deref(), Some("2020-05-05T08:11:12Z"));
        // no status headers: read
        assert_eq!(items[1].keywords, vec!["$seen".to_string()]);
        let f = header_fields(b"X-Mozilla-Status: 0001\nX-Keywords: Work, \\Flagged $Junk\n\nbody");
        let (kw, gone) = mbox_state(&f);
        assert!(!gone);
        assert_eq!(kw, vec!["$seen", "work", "$flagged", "$junk"]);
        let unread = header_fields(b"Status: O\nDate: Mon, 16 Nov 2026 17:00:00 +0000 (UTC)\n\n");
        assert!(mbox_state(&unread).0.is_empty());
        assert_eq!(received_at(&unread).as_deref(), Some("2026-11-16T17:00:00Z"));
    }

    #[test]
    fn eml_is_one_message_and_entries() {
        let eml = b"Subject: hi\r\nFrom: a@b.c\r\n\r\nFrom here on it is body\r\n";
        let items = items_of("note.eml", eml);
        assert_eq!(items.len(), 1);
        assert_eq!(items[0].bytes, eml.to_vec());
        let e = import_entry("B1", "M1", &items[0]);
        assert_eq!(e["blobId"], "B1");
        assert_eq!(e["mailboxIds"]["M1"], true);
        assert_eq!(e["keywords"]["$seen"], true);
        assert!(e.get("receivedAt").is_none());
        // folded headers are unfolded
        let f = header_fields(b"Subject: a\n  b\n\n");
        assert_eq!(f[0], ("subject".to_string(), "a b".to_string()));
    }
}
