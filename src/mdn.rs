//! Read receipts (RFC 8098 Message Disposition Notifications), as Bulwark lib/mdn.ts builds
//! them. Stalwart has no `urn:ietf:params:jmap:mdn` (RFC 9007), so the client writes the
//! multipart/report itself, uploads it, imports it into Sent (`Email/import`) and submits it
//! (`EmailSubmission/set` with an explicit envelope), then flags the original `$MDNSent`.
use base64::Engine;
use serde_json::{json, Value};

/// What the receipt is built from.
#[derive(Default, Clone, Debug)]
pub struct MdnOptions {
    /// The address that asked for the receipt (Disposition-Notification-To): the recipient.
    pub to: String,
    /// The identity it is sent from.
    pub from_email: String,
    pub from_name: String,
    /// The original Message-ID, with or without angle brackets.
    pub original_message_id: String,
    pub original_subject: String,
    /// The address the original was delivered to (Final-/Original-Recipient).
    pub original_recipient: String,
    /// true: "Always send" (automatic-action), false: the user clicked Send.
    pub automatic: bool,
}

/// RFC 5322 date in UTC, e.g. "Thu, 28 May 2026 14:23:00 +0000".
pub fn rfc5322_date(t: chrono::DateTime<chrono::Utc>) -> String {
    t.format("%a, %d %b %Y %H:%M:%S +0000").to_string()
}

/// CR, LF, the other C0 controls and DEL collapse to one space (a sender can smuggle a CRLF
/// through an RFC 2047 encoded word; interpolated as is it would split our header).
pub fn strip_header_controls(v: &str) -> String {
    let mut out = String::new();
    let mut gap = false;
    for c in v.chars() {
        if (c as u32) < 0x20 || c as u32 == 0x7f { if !gap { out.push(' '); } gap = true; } else { out.push(c); gap = false; }
    }
    out.trim().to_string()
}

/// Address-like atoms (mailboxes, message ids) keep no whitespace or controls at all.
pub fn header_atom(v: &str) -> String {
    v.chars().filter(|c| (*c as u32) > 0x20 && *c as u32 != 0x7f).collect()
}

/// A header value after "Name: ": controls stripped, RFC 2047-encoded when not plain ASCII.
pub fn encode_header_word(v: &str) -> String {
    let clean = strip_header_controls(v);
    if clean.chars().all(|c| (' '..='~').contains(&c)) { return clean; }
    format!("=?UTF-8?B?{}?=", base64::engine::general_purpose::STANDARD.encode(clean.as_bytes()))
}

/// A display name: encoded word when non-ASCII, quoted when it has specials.
pub fn display_name(v: &str) -> String {
    let e = encode_header_word(v);
    if e.starts_with("=?") || !e.chars().any(|c| "()<>[]:;@\\,.\"".contains(c)) { return e; }
    format!("\"{}\"", e.replace('\\', "\\\\").replace('"', "\\\""))
}

fn ensure_angles(id: &str) -> String {
    let t = header_atom(id);
    if t.is_empty() { return t; }
    if t.starts_with('<') { t } else { format!("<{t}>") }
}

/// UTF-8 text as base64 wrapped at 76 characters (RFC 2045).
pub fn base64_body(text: &str) -> String {
    let b = base64::engine::general_purpose::STANDARD.encode(text.as_bytes());
    b.as_bytes().chunks(76).map(|c| std::str::from_utf8(c).unwrap_or("")).collect::<Vec<_>>().join("\r\n")
}

pub fn random_token() -> String {
    let mut b = [0u8; 8];
    let _ = getrandom::getrandom(&mut b);
    let rnd: String = b.iter().map(|x| format!("{x:02x}")).collect();
    format!("{:x}.{rnd}", chrono::Utc::now().timestamp_millis())
}

/// The raw MDN message (CRLF line ends), Bulwark buildMdnMessage.
pub fn build(o: &MdnOptions, now: chrono::DateTime<chrono::Utc>, token: &str) -> String {
    let to = header_atom(&o.to);
    let from_email = header_atom(&o.from_email);
    let orig_rcpt = header_atom(&o.original_recipient);
    let final_rcpt = if orig_rcpt.is_empty() { from_email.clone() } else { orig_rcpt.clone() };
    let domain = from_email.split('@').nth(1).filter(|d| !d.is_empty()).unwrap_or("localhost").to_string();
    let message_id = format!("<mdn.{token}@{domain}>");
    let boundary = format!("----=_MDN_{token}");
    let orig_id = ensure_angles(&o.original_message_id);
    let from_header = if o.from_name.trim().is_empty() { from_email.clone() } else { format!("{} <{from_email}>", display_name(&o.from_name)) };
    let subject = encode_header_word(format!("Read: {}", o.original_subject).trim());
    let disposition = if o.automatic { "automatic-action/MDN-sent-automatically; displayed" } else { "manual-action/MDN-sent-manually; displayed" };
    let human = [
        format!("This is a return receipt for the message you sent to {final_rcpt}."),
        String::new(),
        "Note: This receipt only acknowledges that the message was displayed on the".into(),
        "recipient's computer. There is no guarantee that the recipient has read or".into(),
        "understood the message contents.".into(),
    ].join("\r\n");
    let mut fields = vec![format!("Reporting-UA: {domain}; Barbican"), format!("Final-Recipient: rfc822;{final_rcpt}")];
    if !orig_rcpt.is_empty() { fields.push(format!("Original-Recipient: rfc822;{orig_rcpt}")); }
    if !orig_id.is_empty() { fields.push(format!("Original-Message-ID: {orig_id}")); }
    fields.push(format!("Disposition: {disposition}"));
    let mut lines = vec![
        format!("Date: {}", rfc5322_date(now)),
        format!("From: {from_header}"),
        format!("To: {to}"),
        format!("Subject: {subject}"),
        format!("Message-ID: {message_id}"),
    ];
    if !orig_id.is_empty() { lines.push(format!("In-Reply-To: {orig_id}")); }
    lines.extend([
        "MIME-Version: 1.0".to_string(),
        "Content-Type: multipart/report; report-type=disposition-notification;".into(),
        format!("\tboundary=\"{boundary}\""),
        String::new(),
        format!("--{boundary}"),
        "Content-Type: text/plain; charset=utf-8".into(),
        "Content-Transfer-Encoding: base64".into(),
        String::new(),
        base64_body(&human),
        String::new(),
        format!("--{boundary}"),
        "Content-Type: message/disposition-notification".into(),
        "Content-Transfer-Encoding: 7bit".into(),
        String::new(),
        fields.join("\r\n"),
        String::new(),
        format!("--{boundary}--"),
        String::new(),
    ]);
    lines.join("\r\n")
}

/// The first address of an address-list header value: `Name <a@x>, b@y` gives `a@x`. Commas
/// inside quotes or angle brackets do not split; None if the first entry is not an address.
pub fn first_address(raw: &str) -> Option<String> {
    let line: String = raw.chars().take(1000).collect();
    let (mut q, mut ang, mut end) = (false, false, line.len());
    for (i, ch) in line.char_indices() {
        match ch {
            '"' => q = !q,
            '<' if !q => ang = true,
            '>' if !q => ang = false,
            ',' if !q && !ang => { end = i; break; }
            _ => {}
        }
    }
    let first = &line[..end];
    let addr = match (first.find('<'), first.rfind('>')) {
        (Some(a), Some(b)) if b > a + 1 => &first[a + 1..b],
        _ => first,
    };
    let addr = addr.trim();
    let ok = addr.matches('@').count() == 1 && !addr.starts_with('@') && !addr.ends_with('@')
        && !addr.contains(|c: char| c.is_whitespace() || matches!(c, '<' | '>' | ',' | '"' | ';'));
    ok.then(|| addr.to_string())
}

/// The address a message asks the receipt to go to (Disposition-Notification-To, RFC 8098):
/// the first address of the first such header.
pub fn requested_by(headers: &[Value]) -> Option<String> {
    first_address(&crate::reader::header(headers, "Disposition-Notification-To")?)
}

/// Whether a receipt may go out without asking (RFC 8098 §2.1): only when the address it is
/// requested for is the message's Return-Path (else, with no Return-Path, its From). Anything
/// else could be a third party tracking when mail is opened, so the user is asked.
pub fn auto_send_ok(m: &Value) -> bool {
    let headers = m["headers"].as_array().map(|a| a.as_slice()).unwrap_or(&[]);
    let Some(to) = requested_by(headers) else { return false };
    let origin = crate::reader::header(headers, "Return-Path").and_then(|r| first_address(&r))
        .or_else(|| m["from"][0]["email"].as_str().map(String::from));
    origin.is_some_and(|o| o.eq_ignore_ascii_case(&to))
}

/// Messages a receipt went out for in this run: `$MDNSent` is the durable record, this keeps a
/// failed flag write from sending a second receipt on the next open.
static SENT: std::sync::Mutex<Vec<String>> = std::sync::Mutex::new(Vec::new());

pub fn sent_this_run(id: &str) -> bool { SENT.lock().unwrap().iter().any(|x| x == crate::unified::real_id(id)) }

/// Whether the open message should offer a receipt (Bulwark shouldOfferReadReceipt): it asks for
/// one, has none sent yet, the user does not answer "never", it is not a draft and sits in a
/// received folder (not Sent, Drafts, Trash or Junk).
pub fn should_offer(m: &Value, roles: &[String], response: &str) -> bool {
    requested_by(m["headers"].as_array().map(|a| a.as_slice()).unwrap_or(&[])).is_some()
        && m["keywords"]["$mdnsent"].as_bool() != Some(true)
        && response != "never"
        && m["keywords"]["$draft"].as_bool() != Some(true)
        && !roles.iter().any(|r| matches!(r.as_str(), "sent" | "drafts" | "trash" | "junk"))
}

/// The identity a receipt goes out from: the one the message was addressed to (To or Cc), else
/// the first.
pub fn pick_identity<'a>(identities: &'a [Value], m: &Value) -> Option<&'a Value> {
    let rcpts: Vec<String> = ["to", "cc"].iter().flat_map(|k| m[*k].as_array().cloned().unwrap_or_default())
        .filter_map(|a| a["email"].as_str().map(|e| e.to_lowercase())).collect();
    identities.iter().find(|i| i["email"].as_str().is_some_and(|e| rcpts.contains(&e.to_lowercase()))).or(identities.first())
}

/// Send the receipt for `m` and flag it `$MDNSent`. Errors name the failing step.
pub fn send(c: &crate::jmap::Client, m: &Value, automatic: bool) -> Result<(), String> {
    let headers = m["headers"].as_array().cloned().unwrap_or_default();
    let to = requested_by(&headers).ok_or("The message asks for no receipt.")?;
    let identities = c.identities()?;
    let ident = pick_identity(&identities, m).ok_or("No identity to send the receipt from.")?;
    let opts = MdnOptions {
        to,
        from_email: ident["email"].as_str().unwrap_or("").into(),
        from_name: ident["name"].as_str().unwrap_or("").into(),
        original_message_id: m["messageId"][0].as_str().unwrap_or("").into(),
        original_subject: m["subject"].as_str().unwrap_or("").into(),
        original_recipient: ident["email"].as_str().unwrap_or("").into(),
        automatic,
    };
    let raw = build(&opts, chrono::Utc::now(), &random_token());
    let (blob, _) = c.upload(raw.into_bytes(), "message/rfc822")?;
    let roles = c.role_ids()?;
    let sent = roles.get("sent").ok_or("No Sent folder.")?.clone();
    let r = c.call(json!([["Email/import", {"accountId": c.account, "emails": {"m": {"blobId": blob, "mailboxIds": {sent: true}, "keywords": {"$seen": true}}}}, "i"]]))?;
    let eid = r[0][1]["created"]["m"]["id"].as_str().map(String::from)
        .ok_or_else(|| format!("Email/import: {}", crate::jmap::set_error(&r[0][1]["notCreated"]["m"])))?;
    let sub_acc = c.account_for("urn:ietf:params:jmap:submission");
    let r = c.call(json!([["EmailSubmission/set", {"accountId": sub_acc, "create": {"r": {
        "emailId": eid, "identityId": ident["id"],
        "envelope": {"mailFrom": {"email": opts.from_email}, "rcptTo": [{"email": opts.to}]}
    }}}, "s"]]));
    let failed = match &r {
        Ok(r) if r[0][1]["created"]["r"].is_object() => None,
        Ok(r) => Some(crate::jmap::set_error(&r[0][1]["notCreated"]["r"])),
        Err(e) => Some(e.clone()),
    };
    if let Some(e) = failed {
        // nothing went out: take the receipt back out of Sent, where it would pass for a sent one
        let _ = c.call(json!([["Email/set", {"accountId": c.account, "destroy": [eid]}, "x"]]));
        return Err(format!("Not sent: {e}"));
    }
    // the receipt is out; failing to flag that must not read as a failed send, but it is
    // remembered for this run so the message does not ask (or auto-send) again
    let id = crate::unified::real_id(m["id"].as_str().unwrap_or("")).to_string();
    SENT.lock().unwrap().push(id.clone());
    if let Err(e) = c.set_keyword(&[id], "$mdnsent", true) { eprintln!("read receipt sent, $MDNSent not set: {e}"); }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn dnt_addresses() {
        assert_eq!(first_address("a@x.org, b@y.org"), Some("a@x.org".into()));
        assert_eq!(first_address("\"Doe, Jane\" <jane@x.org>, b@y"), Some("jane@x.org".into()));
        assert_eq!(first_address("<a@x>"), Some("a@x".into()));
        assert_eq!(first_address("Jane Doe"), None);
        assert_eq!(first_address("a@b@c"), None);
        assert_eq!(first_address(""), None);
    }

    #[test]
    fn auto_send_only_to_the_return_path() {
        let m = |dnt: &str, rp: Option<&str>| {
            let mut h = vec![json!({"name": "Disposition-Notification-To", "value": dnt})];
            if let Some(r) = rp { h.push(json!({"name": "Return-Path", "value": r})); }
            json!({"headers": h, "from": [{"email": "boss@corp.com"}]})
        };
        assert!(auto_send_ok(&m("Boss <boss@corp.com>", Some("<BOSS@corp.com>"))));
        assert!(!auto_send_ok(&m("tracker@spy.net", Some("<boss@corp.com>"))), "a third party is asked for");
        assert!(auto_send_ok(&m("boss@corp.com", None)), "no Return-Path: From counts");
        assert!(!auto_send_ok(&m("tracker@spy.net", None)));
    }

    fn now() -> chrono::DateTime<chrono::Utc> { chrono::DateTime::parse_from_rfc3339("2026-05-28T14:23:00Z").unwrap().with_timezone(&chrono::Utc) }

    #[test]
    fn builds_two_part_report() {
        let o = MdnOptions { to: "anna@example.com".into(), from_email: "me@example.org".into(), from_name: "Me Myself".into(),
            original_message_id: "abc@x".into(), original_subject: "Hello".into(), original_recipient: "me@example.org".into(), automatic: false };
        let raw = build(&o, now(), "tok");
        assert!(raw.starts_with("Date: Thu, 28 May 2026 14:23:00 +0000\r\nFrom: Me Myself <me@example.org>\r\nTo: anna@example.com\r\nSubject: Read: Hello\r\n"));
        assert!(raw.contains("Message-ID: <mdn.tok@example.org>\r\nIn-Reply-To: <abc@x>\r\n"));
        assert!(raw.contains("Content-Type: multipart/report; report-type=disposition-notification;\r\n\tboundary=\"----=_MDN_tok\""));
        assert!(raw.contains("Reporting-UA: example.org; Barbican\r\nFinal-Recipient: rfc822;me@example.org\r\nOriginal-Recipient: rfc822;me@example.org\r\nOriginal-Message-ID: <abc@x>\r\nDisposition: manual-action/MDN-sent-manually; displayed"));
        assert!(raw.ends_with("\r\n------=_MDN_tok--\r\n"));
        assert!(!raw.replace("\r\n", "").contains('\n'), "only CRLF line ends");
        let auto = build(&MdnOptions { automatic: true, ..o }, now(), "t");
        assert!(auto.contains("automatic-action/MDN-sent-automatically; displayed"));
    }

    #[test]
    fn header_injection_is_neutralised() {
        // GHSA-w38p-hpqv-g89c: a CRLF smuggled in via the subject, Message-ID or requester
        let o = MdnOptions { to: "a@b.c\r\nBcc: victim@x".into(), from_email: "me@ex.org".into(), from_name: "Ä \"q\"".into(),
            original_message_id: "<id@x>\r\nX-Evil: 1".into(), original_subject: "Hi\r\nBcc: evil@x".into(), ..Default::default() };
        let raw = build(&o, now(), "t");
        let head = raw.split("\r\n\r\n").next().unwrap();
        assert!(!head.lines().any(|l| l.starts_with("Bcc:") || l.starts_with("X-Evil")));
        assert!(head.contains("To: a@b.cBcc:victim@x"));
        assert!(head.contains("Subject: Read: Hi Bcc: evil@x"));
        assert!(head.contains("From: =?UTF-8?B?"));
        assert_eq!(display_name("Smith, John"), "\"Smith, John\"");
        assert_eq!(encode_header_word("Grüße"), "=?UTF-8?B?R3LDvMOfZQ==?=");
    }

    #[test]
    fn base64_wraps_at_76() {
        let b = base64_body(&"x".repeat(200));
        assert!(b.split("\r\n").all(|l| l.len() <= 76));
        assert_eq!(b.split("\r\n").count(), 4);
    }

    #[test]
    fn requester_and_offer_rules() {
        let h = vec![json!({"name": "Subject", "value": " x"}), json!({"name": "disposition-notification-to", "value": " Anna <anna@example.com>"})];
        assert_eq!(requested_by(&h).as_deref(), Some("anna@example.com"));
        assert_eq!(requested_by(&[json!({"name": "Disposition-Notification-To", "value": " bob@example.com"})]).as_deref(), Some("bob@example.com"));
        assert_eq!(requested_by(&[json!({"name": "Disposition-Notification-To", "value": " nobody"})]), None);
        let m = json!({"headers": h, "keywords": {}});
        assert!(should_offer(&m, &["inbox".into()], "ask"));
        assert!(!should_offer(&m, &["inbox".into()], "never"));
        assert!(!should_offer(&m, &["junk".into()], "ask"));
        assert!(!should_offer(&m, &["sent".into()], "always"));
        let done = json!({"headers": m["headers"], "keywords": {"$mdnsent": true}});
        assert!(!should_offer(&done, &["inbox".into()], "ask"));
    }

    #[test]
    fn identity_follows_the_recipient() {
        let ids = vec![json!({"id": "1", "email": "main@x.org"}), json!({"id": "2", "email": "Alias@x.org"})];
        let m = json!({"to": [{"email": "alias@x.org"}]});
        assert_eq!(pick_identity(&ids, &m).unwrap()["id"], "2");
        assert_eq!(pick_identity(&ids, &json!({"to": [{"email": "list@y"}]})).unwrap()["id"], "1");
    }
}
