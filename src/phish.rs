//! Reporting phishing and what a reported message looks like (Bulwark lib/phishing-report.ts,
//! lib/phishing-report-arf.ts, lib/phishing-report-flow.ts, lib/phishing-links.ts,
//! components/email/phishing-mark-banner.tsx).
//!
//! "Report phishing" sends the message to an address and moves it to Junk with `$junk` and
//! `$phishing` (RFC 9051 §2.3.2: clients should warn and disable links in such a message). Bulwark
//! takes the address from the admin's policy (`organisation.phishingReport`); Barbican has no
//! policy file, so it is the user's own setting (Settings > Privacy & security > Phishing
//! reports); empty means reporting is off.
//!
//! * One report per message (RFC 5965 §2(e); report desks discard reports with several attached).
//! * The report is not the user's mail: no copy in Sent (Email/set in Drafts, then the
//!   submission with `onSuccessDestroyEmail`).
//! * Forward format: the original attached by its blob id (nothing uploaded). ARF format: a
//!   `multipart/report; report-type=feedback-report` with a note, the `message/feedback-report`
//!   (`Feedback-Type: fraud`) and the original unchanged as `message/rfc822`; JMAP cannot set the
//!   report-type parameter through Email/set, so it is raw MIME: uploaded, imported into Drafts
//!   and submitted.
//!
//! A message with `$phishing` shows the banner and has its links turned off (`mailview`:
//! `sanitize_mail_html_opts` removes every link's address, the navigation handlers refuse links
//! while the flag is set). Bulwark 1.21.2 has no link-mismatch warning, so there is none here.
use serde_json::{json, Value};

use crate::mdn::{base64_body, display_name, encode_header_word, header_atom, random_token, rfc5322_date};

/// RFC 9051 §2.3.2: the user reported the message as phishing.
pub const KEYWORD: &str = "$phishing";

/// What the banner says (Bulwark `phishing_report.banner`).
pub const BANNER: &str = "You reported this message as phishing. Its links are turned off.";

/// Whether the message carries the phishing mark.
pub fn marked(m: &Value) -> bool {
    m["keywords"][KEYWORD].as_bool() == Some(true)
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub enum Format { Forward, Arf }

#[derive(Debug, Clone, PartialEq)]
pub struct Setup {
    pub address: String,
    pub format: Format,
}

/// The setup from the two settings; None while reporting is off (no usable address). The address
/// is one plain mailbox: anything with a name part, a list or control characters is refused, so
/// a setting cannot send a report anywhere else than the one address it shows.
pub fn setup(address: &str, format: &str) -> Option<Setup> {
    let address = address.trim();
    (crate::reader::valid_email(address)).then(|| Setup { address: address.to_string(), format: if format == "arf" { Format::Arf } else { Format::Forward } })
}

/// The setup from the user's settings.
pub fn setup_from_settings() -> Option<Setup> {
    let s = crate::settings::Settings::load();
    setup(&s.str("phishingReportAddress", ""), &s.str("phishingReportFormat", "forward"))
}

/// Folders whose messages are the user's own: nothing to report there.
const OWN_MAIL_ROLES: &[&str] = &["sent", "drafts", "scheduled"];

/// Whether `m` (in folders of `roles`) can be reported: it has content to attach, is not a draft
/// or scheduled, is not the user's own mail and was not reported already.
pub fn is_reportable(m: &Value, roles: &[String]) -> bool {
    m["blobId"].is_string() && m["isScheduled"].as_bool() != Some(true) && m["keywords"]["$draft"].as_bool() != Some(true)
        && !roles.iter().any(|r| OWN_MAIL_ROLES.contains(&r.as_str())) && !marked(m)
}

/// The subject of the report: Microsoft's reporting mailboxes read "Phishing:" as the verdict.
pub fn report_subject(subject: Option<&str>) -> String {
    let clean = subject.unwrap_or("").replace(['\r', '\n'], " ");
    let clean = clean.trim();
    if clean.is_empty() { "Phishing".into() } else { format!("Phishing: {clean}") }
}

/// The short text of a forwarded report (English: the security team reads it, not the user).
pub fn report_text(name: &str, email: &str) -> String {
    let who = if name.trim().is_empty() { email.to_string() } else { format!("{} <{email}>", name.trim()) };
    format!("Reported as phishing by {who} with Barbican.\r\nThe reported message is attached unchanged.\r\n")
}

/// The identity a report goes out from: the login's own address first, never a wildcard.
pub fn reporter_identity<'a>(identities: &'a [Value], username: &str) -> Option<&'a Value> {
    let usable = |i: &&Value| i["email"].as_str().is_some_and(|e| !e.is_empty() && !e.contains('*') && e.contains('@'));
    identities.iter().filter(usable).find(|i| i["email"].as_str().is_some_and(|e| e.eq_ignore_ascii_case(username)))
        .or_else(|| identities.iter().find(usable))
}

/// `Name.eml` of an attached report: the subject made safe as a file name.
pub fn attachment_name(m: &Value) -> String {
    crate::reader::eml_export_name(m)
}

// ---------------------------------------------------------------- ARF (RFC 5965)

/// How the original goes in: message/rfc822 may not be base64-encoded (RFC 2046 §5.2.1).
#[derive(Debug, Clone, Copy, PartialEq)]
pub enum RawEncoding { SevenBit, EightBit, Binary }

impl RawEncoding {
    fn name(self) -> &'static str { match self { Self::SevenBit => "7bit", Self::EightBit => "8bit", Self::Binary => "binary" } }
}

/// 7bit or 8bit lines of at most 998 octets ending in CRLF (RFC 2045 §2.7, §2.8); anything else
/// (a NUL, a bare CR or LF, a longer line) is binary.
pub fn raw_encoding(bytes: &[u8]) -> RawEncoding {
    let (mut eight, mut line, mut i) = (false, 0usize, 0usize);
    while i < bytes.len() {
        let b = bytes[i];
        match b {
            0 => return RawEncoding::Binary,
            b'\r' => { if bytes.get(i + 1) != Some(&b'\n') { return RawEncoding::Binary; } i += 1; line = 0; }
            b'\n' => return RawEncoding::Binary,
            _ => { if b > 0x7f { eight = true; } line += 1; if line > 998 { return RawEncoding::Binary; } }
        }
        i += 1;
    }
    if eight { RawEncoding::EightBit } else { RawEncoding::SevenBit }
}

/// The domain of the original's From, when it has a plain one (the report's Reported-Domain).
fn sender_domain(m: &Value) -> Option<String> {
    let a = m["from"][0]["email"].as_str()?;
    let d = a.rsplit_once('@')?.1;
    (!d.is_empty() && d.chars().all(|c| c.is_ascii_alphanumeric() || c == '.' || c == '-')).then(|| d.to_ascii_lowercase())
}

pub struct ArfInput<'a> {
    /// The reported message (Email/get): subject, receivedAt, from.
    pub message: &'a Value,
    pub reporter_name: &'a str,
    pub reporter_email: &'a str,
    pub to: &'a str,
    pub encoding: RawEncoding,
    /// The address the original was delivered to (Original-Rcpt-To), when known.
    pub recipient: Option<&'a str>,
    pub now: chrono::DateTime<chrono::Utc>,
    pub token: &'a str,
}

/// The report around the original: everything before the original's bytes, and after them.
pub fn arf_parts(i: &ArfInput) -> (String, String) {
    let from_email = header_atom(i.reporter_email);
    let domain = from_email.split('@').nth(1).filter(|d| !d.is_empty()).unwrap_or("localhost").to_string();
    let boundary = format!("----=_ARF_{}", i.token);
    let from_name = if i.reporter_name.trim().is_empty() { String::new() } else { display_name(i.reporter_name.trim()) };
    let human = [format!("This is a phishing report (RFC 5965) sent by {from_email} with Barbican."), String::new(), "The reported message is attached unchanged.".into()].join("\r\n");
    let mut report = vec!["Feedback-Type: fraud".to_string(), format!("User-Agent: Barbican/{}", env!("CARGO_PKG_VERSION")), "Version: 1".into()];
    if let Some(t) = i.message["receivedAt"].as_str().and_then(|s| chrono::DateTime::parse_from_rfc3339(s).ok()) {
        report.push(format!("Arrival-Date: {}", rfc5322_date(t.with_timezone(&chrono::Utc))));
    }
    if let Some(r) = i.recipient { let r = header_atom(r); if !r.is_empty() { report.push(format!("Original-Rcpt-To: {r}")); } }
    if let Some(d) = sender_domain(i.message) { report.push(format!("Reported-Domain: {d}")); }
    let head = [
        format!("Date: {}", rfc5322_date(i.now)),
        format!("From: {}", if from_name.is_empty() { from_email.clone() } else { format!("{from_name} <{from_email}>") }),
        format!("To: {}", header_atom(i.to)),
        format!("Subject: {}", encode_header_word(&report_subject(i.message["subject"].as_str()))),
        format!("Message-ID: <arf.{}@{domain}>", i.token),
        "MIME-Version: 1.0".into(),
        "Content-Type: multipart/report; report-type=feedback-report;".into(),
        format!("\tboundary=\"{boundary}\""),
        String::new(),
        format!("--{boundary}"),
        "Content-Type: text/plain; charset=utf-8".into(),
        "Content-Transfer-Encoding: base64".into(),
        String::new(),
        base64_body(&human),
        String::new(),
        format!("--{boundary}"),
        "Content-Type: message/feedback-report".into(),
        "Content-Transfer-Encoding: 7bit".into(),
        String::new(),
        report.join("\r\n"),
        String::new(),
        format!("--{boundary}"),
        "Content-Type: message/rfc822".into(),
        "Content-Disposition: attachment; filename=\"reported-message.eml\"".into(),
        format!("Content-Transfer-Encoding: {}", i.encoding.name()),
        String::new(),
        String::new(),
    ].join("\r\n");
    (head, format!("\r\n--{boundary}--\r\n"))
}

/// The whole report: the original goes in byte for byte; the closing boundary needs a line break
/// before it.
pub fn build_arf(i: &ArfInput, original: &[u8]) -> Vec<u8> {
    let (head, tail) = arf_parts(i);
    let ends_with_break = original.ends_with(b"\r\n");
    let mut out = Vec::with_capacity(head.len() + original.len() + tail.len());
    out.extend_from_slice(head.as_bytes());
    out.extend_from_slice(original);
    out.extend_from_slice(if ends_with_break { &tail.as_bytes()[2..] } else { tail.as_bytes() });
    out
}

/// Where the original was delivered to, for Original-Rcpt-To: the first Delivered-To /
/// X-Original-To header, else the To address that is the reporter's own.
pub fn delivered_to(m: &Value, reporter_email: &str) -> Option<String> {
    let headers = m["headers"].as_array().map(|a| a.as_slice()).unwrap_or(&[]);
    ["Delivered-To", "X-Original-To"].iter().find_map(|n| crate::reader::header(headers, n).and_then(|v| crate::mdn::first_address(&v)))
        .or_else(|| m["to"].as_array().and_then(|a| a.iter().filter_map(|x| x["email"].as_str()).find(|e| e.eq_ignore_ascii_case(reporter_email)).map(String::from)))
}

// ---------------------------------------------------------------- JMAP request shapes

/// The Email/set create object of a forwarded report: the original attached by blob id.
pub fn forward_email(username: &str, ident: &Value, drafts: &str, setup: &Setup, m: &Value, blob_id: &str) -> Result<Value, String> {
    let name = ident["name"].as_str().unwrap_or("");
    let email = ident["email"].as_str().unwrap_or("");
    let out = crate::jmap::Outgoing {
        to: setup.address.clone(),
        subject: report_subject(m["subject"].as_str()),
        text: report_text(name, email),
        attachments: vec![crate::jmap::Attachment {
            blob_id: blob_id.to_string(), name: attachment_name(m), mime: "message/rfc822".into(), size: m["size"].as_u64().unwrap_or(0) as usize,
        }],
        ..Default::default()
    };
    let mut obj = crate::jmap::build_email_object(&out, ident, drafts, true, username)?;
    // a report is not read mail waiting in Drafts, and asks for no receipt
    obj["keywords"] = json!({"$draft": true, "$seen": true});
    Ok(obj)
}

/// The submission of the drafted report (`emailId` is `#r`, the creation id of the first call):
/// the envelope names the one address, and the draft is destroyed once submitted (no Sent copy).
pub fn submission(sub_account: &str, identity_id: &str, from_email: &str, to: &str) -> Value {
    json!(["EmailSubmission/set", {
        "accountId": sub_account,
        "create": {"s": {"emailId": "#r", "identityId": identity_id,
            "envelope": {"mailFrom": {"email": from_email}, "rcptTo": [{"email": to}]}}},
        "onSuccessDestroyEmail": ["#s"]
    }, "s"])
}

/// Forward format: Email/set in Drafts (the original attached by blob id), then the submission.
pub fn forward_calls(account: &str, sub_account: &str, email: Value, identity_id: &str, from_email: &str, to: &str) -> Value {
    json!([
        ["Email/set", {"accountId": account, "create": {"r": email}}, "e"],
        submission(sub_account, identity_id, from_email, to),
    ])
}

/// ARF format: the uploaded report imported into Drafts, then the submission.
pub fn arf_calls(account: &str, sub_account: &str, drafts: &str, blob_id: &str, identity_id: &str, from_email: &str, to: &str) -> Value {
    json!([
        ["Email/import", {"accountId": account, "emails": {"r": {"blobId": blob_id, "mailboxIds": {drafts: true}, "keywords": {"$draft": true, "$seen": true}}}}, "e"],
        submission(sub_account, identity_id, from_email, to),
    ])
}

/// The patch that files a reported message: Junk, `$junk`, `$phishing`, and read (it should not
/// count as new mail in Junk).
pub fn junk_patch(junk: &str) -> Value {
    json!({"mailboxIds": {junk: true}, "keywords/$junk": true, "keywords/$notjunk": null, "keywords/$phishing": true, "keywords/$seen": true})
}

/// The request that moves the reported message `id` to Junk.
pub fn move_call(account: &str, id: &str, junk: &str) -> Value {
    json!([["Email/set", {"accountId": account, "update": { id: junk_patch(junk) }}, "m"]])
}

/// The folder a reported message was in, for Undo (Bulwark undoes to the folder the store cached,
/// the first of the message's folders).
pub fn origin_mailbox(m: &Value) -> Option<String> {
    m["mailboxIds"].as_object()?.iter().find(|(_, v)| v.as_bool() == Some(true)).map(|(k, _)| k.clone())
}

/// The patch that takes a reported message back (Bulwark client.ts undoSpam with `$phishing` cleared):
/// back to its folder, `$junk` off and `$notjunk` on (moving alone would leave it flagged as junk
/// for every other client).
pub fn undo_patch(origin: &str) -> Value {
    json!({"mailboxIds": {origin: true}, "keywords/$junk": null, "keywords/$notjunk": true, "keywords/$phishing": null})
}

/// The request that moves the reported message `id` back to `origin`.
pub fn undo_call(account: &str, id: &str, origin: &str) -> Value {
    json!([["Email/set", {"accountId": account, "update": { id: undo_patch(origin) }}, "u"]])
}

/// What went wrong while reporting.
#[derive(Debug, PartialEq)]
pub enum Failure {
    /// Nothing went out.
    NotReported(String),
    /// Reported, but the move to Junk failed.
    NotMoved(String),
}

fn set_failure(r: &Value, create: &str) -> Option<String> {
    r["notCreated"].get(create).filter(|e| !e.is_null()).map(|e| crate::jmap::set_error(e))
}

/// Sends one report for `m` and files it in Junk. `m` is the message of the login `c`, in that
/// login's own account.
pub fn report(c: &crate::jmap::Client, m: &Value, setup: &Setup) -> Result<(), Failure> {
    send(c, m, setup).map_err(Failure::NotReported)?;
    let id = crate::unified::real_id(m["id"].as_str().unwrap_or("")).to_string();
    let junk = c.role_ids().map_err(Failure::NotMoved)?.get("junk").cloned().ok_or_else(|| Failure::NotMoved("This account has no Junk folder.".into()))?;
    let r = c.call(move_call(&c.account, &id, &junk)).map_err(Failure::NotMoved)?;
    match r[0][1]["notUpdated"].get(&id).filter(|e| !e.is_null()) {
        Some(e) => Err(Failure::NotMoved(crate::jmap::set_error(e))),
        None => Ok(()),
    }
}

fn send(c: &crate::jmap::Client, m: &Value, setup: &Setup) -> Result<(), String> {
    let blob = m["blobId"].as_str().ok_or("The message has no content to report")?;
    let identities = c.identities()?;
    let ident = reporter_identity(&identities, &c.username).ok_or("No sender address to report from")?;
    let (ident_id, from_email) = (ident["id"].as_str().unwrap_or("").to_string(), ident["email"].as_str().unwrap_or("").to_string());
    let roles = c.role_ids()?;
    let drafts = roles.get("drafts").ok_or("No Drafts folder.")?.clone();
    let sub_acc = c.account_for("urn:ietf:params:jmap:submission");
    let calls = match setup.format {
        Format::Forward => {
            let email = forward_email(&c.username, ident, &drafts, setup, m, blob)?;
            forward_calls(&c.account, &sub_acc, email, &ident_id, &from_email, &setup.address)
        }
        Format::Arf => {
            // one bigger than the server takes fails anyway, so it is not downloaded either
            let size = m["size"].as_u64().unwrap_or(0) as usize;
            if size + 8192 > c.max_upload() { return Err("The message is too big for an ARF report on this server".into()); }
            let original = c.download(blob, "message.eml", "message/rfc822")?;
            let recipient = delivered_to(m, &from_email);
            let token = random_token();
            let input = ArfInput {
                message: m, reporter_name: ident["name"].as_str().unwrap_or(""), reporter_email: &from_email, to: &setup.address,
                encoding: raw_encoding(&original), recipient: recipient.as_deref(), now: chrono::Utc::now(), token: &token,
            };
            let raw = build_arf(&input, &original);
            let (blob_id, _) = c.upload(raw, "message/rfc822")?;
            arf_calls(&c.account, &sub_acc, &drafts, &blob_id, &ident_id, &from_email, &setup.address)
        }
    };
    let r = c.call(calls)?;
    if let Some(e) = set_failure(&r[0][1], "r") { return Err(format!("The report could not be drafted: {e}")); }
    let err = match r.get(1) {
        Some(s) if s[0] == "EmailSubmission/set" && s[1]["created"]["s"].is_object() => return Ok(()),
        Some(s) => set_failure(&s[1], "s").unwrap_or_else(|| "The server did not take the report.".into()),
        None => "The server did not take the report.".into(),
    };
    // not sent: do not leave the drafted report behind in Drafts
    if let Some(id) = created_draft(&r[0][1]) { let _ = c.call(destroy_call(&c.account, &id)); }
    Err(err)
}

/// The id of the draft the first call created (`create` key "r"), when it did.
pub fn created_draft(set_response: &Value) -> Option<String> {
    set_response["created"]["r"]["id"].as_str().map(str::to_string)
}

/// Destroy a draft (the report that could not be submitted).
pub fn destroy_call(account: &str, id: &str) -> Value {
    json!([["Email/set", {"accountId": account, "destroy": [id]}, "x"]])
}

#[cfg(test)]
mod tests {
    use super::*;

    fn msg() -> Value {
        json!({"id": "M1", "blobId": "B1", "size": 1234, "subject": "Your account", "keywords": {"$seen": true},
            "from": [{"name": "Bank", "email": "alerts@bank.example"}], "to": [{"email": "me@example.test"}],
            "receivedAt": "2026-11-24T10:00:00Z", "mailboxIds": {"i": true},
            "headers": [{"name": "Delivered-To", "value": " me@example.test"}]})
    }
    fn now() -> chrono::DateTime<chrono::Utc> { chrono::DateTime::parse_from_rfc3339("2026-11-24T12:00:00Z").unwrap().with_timezone(&chrono::Utc) }

    #[test]
    fn the_address_is_one_plain_mailbox() {
        assert_eq!(setup(" report@security.example ", "arf"), Some(Setup { address: "report@security.example".into(), format: Format::Arf }));
        assert_eq!(setup("report@security.example", "forward").unwrap().format, Format::Forward);
        assert_eq!(setup("report@security.example", "anything").unwrap().format, Format::Forward);
        for bad in ["", "   ", "nobody", "a@b", "Name <a@b.example>", "a@b.example, c@d.example", "a@b.example\r\nBcc: x@y.example", "a@b.example;c@d.example", "\"a b\"@c.example"] {
            assert_eq!(setup(bad, "forward"), None, "{bad:?}");
        }
    }

    #[test]
    fn undo_goes_back_to_the_folder_and_clears_the_marks() {
        let m = json!({"mailboxIds": {"gone": false, "inbox1": true}});
        assert_eq!(origin_mailbox(&m).as_deref(), Some("inbox1"));
        assert_eq!(origin_mailbox(&json!({})), None);
        let patch = undo_patch("inbox1");
        assert_eq!(patch["mailboxIds"], json!({"inbox1": true}));
        assert!(patch["keywords/$junk"].is_null() && patch["keywords/$phishing"].is_null());
        assert_eq!(patch["keywords/$notjunk"], json!(true));
        // the reverse of what filing did: every key the report set is touched again
        let filed = junk_patch("junk1");
        for k in filed.as_object().unwrap().keys().filter(|k| k.starts_with("keywords/") && *k != "keywords/$seen") {
            assert!(patch.get(k).is_some(), "{k} is not undone");
        }
        assert_eq!(undo_call("A", "M1", "inbox1")[0][1]["update"]["M1"], patch);
    }

    #[test]
    fn who_can_be_reported() {
        let m = msg();
        let inbox = vec!["inbox".to_string()];
        assert!(is_reportable(&m, &inbox));
        assert!(!is_reportable(&m, &["sent".into()]));
        assert!(!is_reportable(&m, &["inbox".into(), "drafts".into()]));
        assert!(!is_reportable(&json!({"keywords": {}}), &inbox), "no content to attach");
        let mut d = msg(); d["keywords"]["$draft"] = json!(true);
        assert!(!is_reportable(&d, &inbox));
        let mut p = msg(); p["keywords"]["$phishing"] = json!(true);
        assert!(marked(&p) && !is_reportable(&p, &inbox), "reported already");
        let mut s = msg(); s["isScheduled"] = json!(true);
        assert!(!is_reportable(&s, &inbox));
        assert!(!marked(&msg()));
    }

    #[test]
    fn subject_and_text() {
        assert_eq!(report_subject(Some("Your account")), "Phishing: Your account");
        assert_eq!(report_subject(Some("  a\r\nBcc: x ")), "Phishing: a  Bcc: x");
        assert_eq!(report_subject(None), "Phishing");
        assert_eq!(report_subject(Some("  ")), "Phishing");
        assert_eq!(report_text("Ann", "ann@example.test"), "Reported as phishing by Ann <ann@example.test> with Barbican.\r\nThe reported message is attached unchanged.\r\n");
        assert!(report_text("", "ann@example.test").contains("by ann@example.test with"));
    }

    #[test]
    fn the_reporter_is_the_login_not_a_wildcard() {
        let ids = vec![json!({"id": "1", "email": "*@example.test"}), json!({"id": "2", "email": "alias@example.test"}), json!({"id": "3", "email": "Ann@example.test"})];
        assert_eq!(reporter_identity(&ids, "ann@example.test").unwrap()["id"], "3");
        assert_eq!(reporter_identity(&ids, "other@example.test").unwrap()["id"], "2");
        assert!(reporter_identity(&[json!({"id": "1", "email": "*@example.test"})], "x").is_none());
        assert!(reporter_identity(&[], "x").is_none());
    }

    #[test]
    fn the_arf_report_has_the_rfc_5965_shape() {
        let m = msg();
        let original = b"From: alerts@bank.example\r\nSubject: Your account\r\n\r\nClick here\r\n";
        let input = ArfInput { message: &m, reporter_name: "Ann Example", reporter_email: "ann@example.test", to: "report@security.example",
            encoding: raw_encoding(original), recipient: Some("me@example.test"), now: now(), token: "tok" };
        let raw = build_arf(&input, original);
        let text = String::from_utf8(raw.clone()).unwrap();
        assert!(text.starts_with("Date: Tue, 24 Nov 2026 12:00:00 +0000\r\nFrom: Ann Example <ann@example.test>\r\nTo: report@security.example\r\nSubject: Phishing: Your account\r\n"), "{text}");
        assert!(text.contains("Message-ID: <arf.tok@example.test>\r\nMIME-Version: 1.0\r\nContent-Type: multipart/report; report-type=feedback-report;\r\n\tboundary=\"----=_ARF_tok\"\r\n\r\n"));
        // three parts: a note for people, the feedback report, the original unchanged
        assert_eq!(text.matches("\r\n------=_ARF_tok\r\n").count() + text.starts_with("------=_ARF_tok").then_some(1).unwrap_or(0), 3);
        assert!(text.contains("Content-Type: message/feedback-report\r\nContent-Transfer-Encoding: 7bit\r\n\r\nFeedback-Type: fraud\r\nUser-Agent: Barbican/"));
        assert!(text.contains("\r\nVersion: 1\r\nArrival-Date: Tue, 24 Nov 2026 10:00:00 +0000\r\nOriginal-Rcpt-To: me@example.test\r\nReported-Domain: bank.example\r\n"));
        assert!(text.contains("Content-Type: message/rfc822\r\nContent-Disposition: attachment; filename=\"reported-message.eml\"\r\nContent-Transfer-Encoding: 7bit\r\n\r\nFrom: alerts@bank.example"));
        // the original goes in byte for byte and the closing boundary follows on its own line
        assert!(raw.windows(original.len()).any(|w| w == original));
        assert!(text.ends_with("Click here\r\n------=_ARF_tok--\r\n"), "{text}");
        assert!(!text.replace("\r\n", "").contains('\n'), "only CRLF line ends");
        // an original with no final line break gets one before the boundary
        let raw = build_arf(&input, b"x");
        assert!(String::from_utf8(raw).unwrap().ends_with("\r\n\r\nx\r\n------=_ARF_tok--\r\n"));
    }

    #[test]
    fn nothing_the_sender_wrote_can_break_the_report_headers() {
        let mut m = msg();
        m["subject"] = json!("Hi\r\nBcc: evil@x.example");
        m["from"][0]["email"] = json!("a@evil.example\r\nX-Evil: 1");
        let input = ArfInput { message: &m, reporter_name: "Ann\r\nBcc: v@x.example", reporter_email: "ann@example.test\r\nBcc: v@x.example", to: "report@security.example\r\nBcc: v@x.example",
            encoding: RawEncoding::SevenBit, recipient: Some("me@x.example\r\nX-Evil: 1"), now: now(), token: "t" };
        let (head, _) = arf_parts(&input);
        let top = head.split("\r\n\r\n").next().unwrap();
        assert!(!top.lines().any(|l| l.starts_with("Bcc:") || l.starts_with("X-Evil")), "{top}");
        assert!(!head.lines().any(|l| l.starts_with("X-Evil") || l.starts_with("Bcc:")), "{head}");
        assert!(!head.contains("Reported-Domain"), "a From domain with control characters is not repeated");
    }

    #[test]
    fn raw_encodings() {
        assert_eq!(raw_encoding(b"a\r\nb\r\n"), RawEncoding::SevenBit);
        assert_eq!(raw_encoding("é\r\n".as_bytes()), RawEncoding::EightBit);
        assert_eq!(raw_encoding(b"a\nb"), RawEncoding::Binary, "a bare LF");
        assert_eq!(raw_encoding(b"a\rb"), RawEncoding::Binary, "a bare CR");
        assert_eq!(raw_encoding(b"a\0b"), RawEncoding::Binary);
        assert_eq!(raw_encoding(&vec![b'a'; 999]), RawEncoding::Binary, "a line over 998");
        assert_eq!(raw_encoding(&[vec![b'a'; 998], b"\r\n".to_vec()].concat()), RawEncoding::SevenBit);
        assert_eq!(raw_encoding(b""), RawEncoding::SevenBit);
    }

    #[test]
    fn where_the_original_was_delivered() {
        assert_eq!(delivered_to(&msg(), "me@example.test").as_deref(), Some("me@example.test"));
        let mut m = msg(); m["headers"] = json!([]);
        assert_eq!(delivered_to(&m, "ME@example.test").as_deref(), Some("me@example.test"), "the To address that is the reporter's own");
        assert_eq!(delivered_to(&m, "other@example.test"), None);
    }

    #[test]
    fn the_forward_request_shape() {
        let ident = json!({"id": "I1", "email": "ann@example.test", "name": "Ann"});
        let st = Setup { address: "report@security.example".into(), format: Format::Forward };
        let email = forward_email("ann@example.test", &ident, "D1", &st, &msg(), "B1").unwrap();
        // in Drafts as a draft, to the one address, the original attached by blob id as message/rfc822
        assert_eq!(email["mailboxIds"], json!({"D1": true}));
        assert_eq!(email["keywords"], json!({"$draft": true, "$seen": true}));
        assert_eq!(email["to"], json!([{"email": "report@security.example"}]));
        assert_eq!(email["subject"], "Phishing: Your account");
        assert_eq!(email["from"], json!([{"name": "Ann", "email": "ann@example.test"}]));
        let parts = email["bodyStructure"]["subParts"].as_array().unwrap();
        assert_eq!(email["bodyStructure"]["type"], "multipart/mixed");
        assert_eq!((parts[1]["blobId"].as_str(), parts[1]["type"].as_str(), parts[1]["disposition"].as_str()), (Some("B1"), Some("message/rfc822"), Some("attachment")));
        assert!(parts[1]["name"].as_str().unwrap().ends_with("-Your account.eml"), "{}", parts[1]["name"]);
        assert!(email["bodyValues"]["t"]["value"].as_str().unwrap().starts_with("Reported as phishing by Ann <ann@example.test> with Barbican."));
        assert!(email.get("header:Disposition-Notification-To:asAddresses").is_none());
        let calls = forward_calls("A1", "S1", email, "I1", "ann@example.test", "report@security.example");
        assert_eq!(calls[0][0], "Email/set");
        assert_eq!(calls[0][1]["accountId"], "A1");
        assert_eq!(calls[0][1]["create"]["r"]["mailboxIds"], json!({"D1": true}));
        assert_eq!(calls[1], json!(["EmailSubmission/set", {
            "accountId": "S1",
            "create": {"s": {"emailId": "#r", "identityId": "I1", "envelope": {"mailFrom": {"email": "ann@example.test"}, "rcptTo": [{"email": "report@security.example"}]}}},
            "onSuccessDestroyEmail": ["#s"]
        }, "s"]), "no copy is kept in Sent: the draft is destroyed once submitted");
    }

    #[test]
    fn a_report_that_was_not_submitted_is_destroyed() {
        assert_eq!(created_draft(&json!({"created": {"r": {"id": "E9"}}})).as_deref(), Some("E9"));
        assert_eq!(created_draft(&json!({"notCreated": {"r": {"type": "x"}}})), None);
        assert_eq!(destroy_call("A1", "E9"), json!([["Email/set", {"accountId": "A1", "destroy": ["E9"]}, "x"]]));
    }

    #[test]
    fn the_arf_request_shape() {
        let calls = arf_calls("A1", "S1", "D1", "UPLOADED", "I1", "ann@example.test", "report@security.example");
        assert_eq!(calls[0], json!(["Email/import", {"accountId": "A1", "emails": {"r": {"blobId": "UPLOADED", "mailboxIds": {"D1": true}, "keywords": {"$draft": true, "$seen": true}}}}, "e"]));
        assert_eq!(calls[1][0], "EmailSubmission/set");
        assert_eq!(calls[1][1]["create"]["s"]["emailId"], "#r");
        assert_eq!(calls[1][1]["create"]["s"]["envelope"]["rcptTo"], json!([{"email": "report@security.example"}]));
        assert_eq!(calls[1][1]["onSuccessDestroyEmail"], json!(["#s"]));
    }

    #[test]
    fn the_move_to_junk_sets_the_phishing_keyword() {
        let call = move_call("A1", "M1", "J1");
        assert_eq!(call, json!([["Email/set", {"accountId": "A1", "update": {"M1": {
            "mailboxIds": {"J1": true}, "keywords/$junk": true, "keywords/$notjunk": null, "keywords/$phishing": true, "keywords/$seen": true
        }}}, "m"]]));
        assert_eq!(KEYWORD, "$phishing");
        assert!(crate::jmap::valid_keyword(KEYWORD));
    }
}
