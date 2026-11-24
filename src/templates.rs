//! Email templates shared with Bulwark: kept in the Inbox's JMAP metadata
//! (`urn:ietf:params:jmap:metadata`), one entry per template under
//! `org.bulwarkmail.template.<id>`, deletions as tombstones in `org.bulwarkmail.templates`
//! (Bulwark lib/templates/metadata-templates.ts, lib/jmap/bulwark-metadata.ts). Where the
//! server has no metadata there are no templates (as before; Bulwark falls back to its browser
//! storage, which Barbican has no counterpart of).
//!
//! The pure parts (placeholders, the stored shape, the size limit, tombstones) are tested; the
//! requests are `list`, `save` and `delete`.
use crate::jmap::Client;
use serde_json::{json, Map, Value};
use std::collections::HashMap;

const NS: &str = "org.bulwarkmail";
/// Largest value Stalwart takes in one namespace (bulwark-metadata.ts MAX_METADATA_ENTRY_BYTES).
pub const MAX_ENTRY_BYTES: usize = 8192;
/// Bulwark template-utils.ts MAX_TEMPLATE_NAME_LENGTH.
const MAX_NAME_CHARS: usize = 200;
/// What a copy without a date gets (template-utils.ts UNKNOWN_TIME): it never wins a merge.
const UNKNOWN_TIME: &str = "1970-01-01T00:00:00.000Z";
/// Bulwark lib/template-types.ts BUILT_IN_PLACEHOLDERS, with the labels of the fill dialog.
pub const BUILT_IN: [(&str, &str); 9] = [
    ("recipient_name", "Recipient name"), ("company", "Company name"), ("date", "Current date"),
    ("short_date", "Date in numbers"), ("day", "Day (two digits)"), ("month", "Month (two digits)"),
    ("year", "Year"), ("day_of_week", "Day of the week"), ("sender_name", "Your name"),
];

pub fn built_in_label(name: &str) -> Option<&'static str> { BUILT_IN.iter().find(|b| b.0 == name).map(|b| b.1) }

// ---------------------------------------------------------------- placeholders (pure)

type Hit = (std::ops::Range<usize>, String, Option<String>);

/// Every `{{name}}` / `{{name:default}}` in `text`: (span, name, default). Names are `\w+`; a
/// default runs up to a `{`, `}` or newline, and in an HTML body also up to `<` or `>`
/// (template-utils.ts TEXT_ / HTML_PLACEHOLDER_REGEX).
fn scan(text: &str, html: bool) -> Vec<Hit> {
    let b: Vec<(usize, char)> = text.char_indices().collect();
    let at = |k: usize| b.get(k).map_or(text.len(), |x| x.0);
    let mut out = vec![];
    let mut i = 0;
    while i + 1 < b.len() {
        if b[i].1 != '{' || b[i + 1].1 != '{' { i += 1; continue; }
        let mut j = i + 2;
        while j < b.len() && (b[j].1.is_ascii_alphanumeric() || b[j].1 == '_') { j += 1; }
        if j == i + 2 { i += 1; continue; }
        let name: String = b[i + 2..j].iter().map(|x| x.1).collect();
        let mut def = None;
        if j < b.len() && b[j].1 == ':' {
            let mut k = j + 1;
            while k < b.len() && !matches!(b[k].1, '{' | '}' | '\n') && !(html && matches!(b[k].1, '<' | '>')) { k += 1; }
            def = Some(text[at(j + 1)..at(k)].to_string());
            j = k;
        }
        if j + 1 < b.len() && b[j].1 == '}' && b[j + 1].1 == '}' {
            out.push((at(i)..at(j + 2), name, def));
            i = j + 2;
        } else { i += 1; }
    }
    out
}

/// Distinct placeholder names in order of appearance (extractPlaceholders).
pub fn extract_placeholders(text: &str, html: bool) -> Vec<String> {
    let mut v: Vec<String> = vec![];
    for (_, n, _) in scan(text, html) { if !v.contains(&n) { v.push(n); } }
    v
}

/// A template's names: the subject's (as text) first, then the body's (getPlaceholdersFromTemplate).
pub fn template_placeholders(subject: &str, body: &str, html: bool) -> Vec<String> {
    let mut v = extract_placeholders(subject, false);
    for n in extract_placeholders(body, html) { if !v.contains(&n) { v.push(n); } }
    v
}

/// Bulwark escapeHtml (email-sanitization.ts): `& < > " '`.
fn esc_html(s: &str) -> String {
    s.replace('&', "&amp;").replace('<', "&lt;").replace('>', "&gt;").replace('"', "&quot;").replace('\'', "&#39;")
}

/// Fill in placeholders; a name without a value stays as written. Values are text: escaped in an
/// HTML body, as typed otherwise (substitutePlaceholders).
pub fn substitute(text: &str, values: &HashMap<String, String>, html: bool) -> String {
    let mut out = String::new();
    let mut at = 0;
    for (r, name, _) in scan(text, html) {
        out.push_str(&text[at..r.start]);
        match values.get(&name) {
            Some(v) if html => out.push_str(&esc_html(v)),
            Some(v) => out.push_str(v),
            None => out.push_str(&text[r.clone()]),
        }
        at = r.end;
    }
    out.push_str(&text[at..]);
    out
}

/// The first default written for each name, subject before body (getPlaceholderDefaults); an
/// HTML default is read as text.
pub fn placeholder_defaults(subject: &str, body: &str, html: bool) -> HashMap<String, String> {
    let mut m = HashMap::new();
    for (text, h) in [(subject, false), (body, html)] {
        for (_, n, d) in scan(text, h) {
            if let Some(d) = d { m.entry(n).or_insert_with(|| if h { crate::rte::html::decode_entities(&d) } else { d }); }
        }
    }
    m
}

/// Values the dialog starts with (getAutoFilledPlaceholders, English): the date in its long and
/// numeric forms and its parts, and the sender's name when the identity has one.
pub fn auto_fill<Tz: chrono::TimeZone>(now: chrono::DateTime<Tz>, sender_name: &str) -> HashMap<String, String>
where Tz::Offset: std::fmt::Display {
    let mut m = HashMap::new();
    m.insert("date".into(), now.format("%B %-d, %Y").to_string());
    m.insert("short_date".into(), now.format("%m/%d/%Y").to_string());
    m.insert("day".into(), now.format("%d").to_string());
    m.insert("month".into(), now.format("%m").to_string());
    m.insert("year".into(), now.format("%Y").to_string());
    m.insert("day_of_week".into(), now.format("%A").to_string());
    if !sender_name.is_empty() { m.insert("sender_name".into(), sender_name.to_string()); }
    m
}

/// validateTemplateName: `None` when fine, else the message.
pub fn validate_name(name: &str) -> Option<&'static str> {
    let t = name.trim();
    if t.is_empty() { Some("Give the template a name.") }
    else if t.chars().count() > MAX_NAME_CHARS { Some("The name is too long (200 characters at most).") }
    else { None }
}

// ---------------------------------------------------------------- stored shape (pure)

/// An HTML template body through the composer's own import and export: the mail sanitiser the
/// reader uses leaves scripts to the reader frame's CSP, which a stored template has no frame
/// for, so a body is rebuilt from the composer's document model (only what the composer can
/// show: text, lists, links, images, quotes), the same step that inserting it would take.
pub fn clean_html(html: &str) -> String {
    let first = crate::mailview::sanitize_mail_html_opts(html, false);
    let mut blocks = crate::rte::html::import(&first);
    for b in &mut blocks {
        for r in &mut b.runs {
            if r.marks.link.as_deref().is_some_and(|l| !safe_link(l)) { r.marks.link = None; }
        }
    }
    crate::rte::html::export(&crate::rte::model::Doc { blocks })
}

/// A link a template may keep: no scheme (relative), or http, https, mailto, tel or cid.
fn safe_link(href: &str) -> bool {
    let h: String = href.chars().filter(|c| !c.is_control() && !c.is_whitespace()).collect::<String>().to_ascii_lowercase();
    match h.find([':', '/', '?', '#']) {
        Some(i) if h.as_bytes()[i] == b':' => ["http", "https", "mailto", "tel", "cid"].contains(&&h[..i]),
        _ => true,
    }
}

/// The size Stalwart counts: the value's JSON in UTF-8 (metadataValueBytes).
pub fn value_bytes(v: &Value) -> usize { serde_json::to_string(v).map_or(0, |s| s.len()) }

/// The template is larger than one metadata entry may be (isTooLargeForMetadata).
pub fn too_large(t: &Value) -> bool { value_bytes(t) > MAX_ENTRY_BYTES }

/// `String(value || '')`.
fn text(v: &Value) -> String {
    match v { Value::String(s) => s.clone(), Value::Number(n) if n.as_f64() != Some(0.0) => n.to_string(), Value::Bool(true) => "true".into(), _ => String::new() }
}

/// JavaScript truthiness, for `Boolean(t.isHTML)`.
fn truthy(v: &Value) -> bool {
    match v { Value::Null => false, Value::Bool(b) => *b, Value::Number(n) => n.as_f64().is_some_and(|x| x != 0.0), Value::String(s) => !s.is_empty(), _ => true }
}

/// A stored entry read like parseSyncedTemplates: the key decides the id, a template needs a
/// name, an HTML body goes through the mail sanitiser, text stays text, a missing date is the
/// epoch. Fields Bulwark does not know are dropped.
pub fn parse_template(v: &Value, id: &str) -> Option<Value> {
    let o = v.as_object()?;
    let get = |k: &str| o.get(k).unwrap_or(&Value::Null);
    let name = text(get("name"));
    if name.trim().is_empty() { return None; }
    let html = truthy(get("isHTML"));
    let body = text(get("body"));
    let mut t = json!({
        "id": id, "name": name, "subject": text(get("subject")),
        "body": if html { clean_html(&body) } else { body },
        "isHTML": html, "category": text(get("category")), "isFavorite": truthy(get("isFavorite")),
        "createdAt": get("createdAt").as_str().unwrap_or(UNKNOWN_TIME),
        "updatedAt": get("updatedAt").as_str().unwrap_or(UNKNOWN_TIME),
    });
    if let Some(r) = get("defaultRecipients").as_object() {
        let mut m = Map::new();
        for k in ["to", "cc", "bcc"] {
            if let Some(a) = r.get(k).and_then(|a| a.as_array()) { m.insert(k.into(), json!(a.iter().map(text).collect::<Vec<_>>())); }
        }
        t["defaultRecipients"] = Value::Object(m);
    }
    if let Some(i) = get("identityId").as_str() { t["identityId"] = json!(i); }
    // the body as stored: an edit that leaves it alone writes it back as it was
    if html { t["rawBody"] = json!(text(get("body"))); }
    Some(t)
}

/// What the editor or the save-draft dialog hands over.
#[derive(Clone, Debug, Default)]
pub struct Fields {
    pub name: String,
    pub subject: String,
    pub body: String,
    pub html: bool,
    pub category: String,
    pub to: Vec<String>,
    pub cc: Vec<String>,
    pub bcc: Vec<String>,
    pub favorite: bool,
}

/// Comma separated addresses as the editor shows them.
pub fn split_addrs(s: &str) -> Vec<String> { s.split(',').map(|x| x.trim().to_string()).filter(|x| !x.is_empty()).collect() }

/// The editor's fields for a template as loaded: the body as stored (not the cleaned copy).
pub fn fields_of(t: &Value) -> Fields {
    let list = |k: &str| t["defaultRecipients"][k].as_array().map(|a| a.iter().filter_map(|s| s.as_str().map(String::from)).collect()).unwrap_or_default();
    Fields {
        name: t["name"].as_str().unwrap_or("").into(), subject: t["subject"].as_str().unwrap_or("").into(),
        body: t.get("rawBody").or(t.get("body")).and_then(|b| b.as_str()).unwrap_or("").into(), html: t["isHTML"] == true,
        category: t["category"].as_str().unwrap_or("").into(), to: list("to"), cc: list("cc"), bcc: list("bcc"), favorite: t["isFavorite"] == true,
    }
}

/// The stored template for `f`: new (`old` is None: a fresh id and `createdAt`) or edited (id,
/// `createdAt` and sending identity kept, `updatedAt` now). An HTML body goes through the mail
/// sanitiser first. Errors on a bad name or a template over the 8 KB entry limit.
pub fn build(old: Option<&Value>, f: &Fields, now: &str, new_id: impl FnOnce() -> String) -> Result<Value, String> {
    if let Some(e) = validate_name(&f.name) { return Err(e.into()); }
    // a body the user did not touch (same text, same format as stored) goes back verbatim: the
    // cleaned copy is for inserting and would flatten what another Bulwark device wrote
    let unchanged = old.is_some_and(|o| (o["isHTML"] == true) == f.html && o.get("rawBody").or(o.get("body")).and_then(|b| b.as_str()) == Some(f.body.as_str()));
    let body = if unchanged || !f.html { f.body.clone() } else { clean_html(&f.body) };
    // never older than the version being replaced, so other devices take the edit
    let now = match old.and_then(|o| o["updatedAt"].as_str()).and_then(|u| chrono::DateTime::parse_from_rfc3339(u).ok()).zip(chrono::DateTime::parse_from_rfc3339(now).ok()) {
        Some((prev, n)) if n <= prev => (prev + chrono::Duration::milliseconds(1)).with_timezone(&chrono::Utc).to_rfc3339_opts(chrono::SecondsFormat::Millis, true),
        _ => now.to_string(),
    };
    let now = now.as_str();
    let mut t = json!({
        "id": old.and_then(|o| o["id"].as_str()).map(String::from).unwrap_or_else(new_id),
        "name": f.name.trim(), "subject": f.subject, "body": body, "isHTML": f.html,
        "category": f.category.trim(), "isFavorite": f.favorite,
        "createdAt": old.and_then(|o| o["createdAt"].as_str()).unwrap_or(now),
        "updatedAt": now,
    });
    if !f.to.is_empty() || !f.cc.is_empty() || !f.bcc.is_empty() {
        let mut r = Map::new();
        for (k, v) in [("to", &f.to), ("cc", &f.cc), ("bcc", &f.bcc)] { if !v.is_empty() { r.insert(k.into(), json!(v)); } }
        t["defaultRecipients"] = Value::Object(r);
    }
    if let Some(i) = old.and_then(|o| o["identityId"].as_str()) { t["identityId"] = json!(i); }
    if too_large(&t) { return Err(format!("This template is too big for the server ({} of {} bytes).", value_bytes(&t), MAX_ENTRY_BYTES)); }
    Ok(t)
}

/// Whether `key` (`template.<id>`) fits Bulwark's metadata key rule: lowercase letters, digits
/// and dashes, dot-separated, 120 characters at most (isValidMetadataKey).
pub fn valid_key(key: &str) -> bool {
    key.len() <= 120 && key.split('.').all(|p| {
        let mut c = p.chars();
        c.next().is_some_and(|f| f.is_ascii_lowercase() || f.is_ascii_digit()) && c.all(|x| x.is_ascii_lowercase() || x.is_ascii_digit() || x == '-')
    })
}

/// A tombstone hides a template unless it was edited after the deletion (planPersonalSync).
/// A tombstone with a bad date, or older than 90 days, counts for nothing (template-merge.ts).
fn hidden(t: &Value, deleted: &Map<String, Value>, now: chrono::DateTime<chrono::Utc>) -> bool {
    let Some(at) = deleted.get(t["id"].as_str().unwrap_or("")).and_then(|d| d.as_str()) else { return false };
    let Ok(a) = chrono::DateTime::parse_from_rfc3339(at) else { return false };
    if a < now - chrono::Duration::days(90) { return false; }
    chrono::DateTime::parse_from_rfc3339(t["updatedAt"].as_str().unwrap_or(UNKNOWN_TIME)).map_or(true, |u| u <= a)
}

/// The templates in an Inbox's metadata object, tombstones applied, sorted by name.
pub fn from_metadata(meta: &Value) -> Vec<Value> { from_metadata_at(meta, chrono::Utc::now()) }

/// Whether the server's copy of the template `old` is still the one `old` was read from: the
/// entry exists with the same `updatedAt` and no newer tombstone covers it.
pub fn check_fresh(old: &Value, meta: &Value) -> Result<(), String> {
    const MSG: &str = "This template was changed elsewhere; reload and try again.";
    let id = old["id"].as_str().unwrap_or("");
    let entry = &meta[format!("{NS}.template.{id}")];
    if !entry.is_object() || entry["updatedAt"].as_str().unwrap_or(UNKNOWN_TIME) != old["updatedAt"].as_str().unwrap_or(UNKNOWN_TIME) { return Err(MSG.into()); }
    if let Some(at) = meta[format!("{NS}.templates")]["deleted"][id].as_str() {
        let (a, u) = (chrono::DateTime::parse_from_rfc3339(at), chrono::DateTime::parse_from_rfc3339(old["updatedAt"].as_str().unwrap_or(UNKNOWN_TIME)));
        if let (Ok(a), Ok(u)) = (a, u) { if a > u { return Err(MSG.into()); } }
    }
    Ok(())
}

pub fn from_metadata_at(meta: &Value, now: chrono::DateTime<chrono::Utc>) -> Vec<Value> {
    let deleted = meta[format!("{NS}.templates")]["deleted"].as_object().cloned().unwrap_or_default();
    let mut out: Vec<Value> = meta.as_object().map(|o| o.iter()
        .filter_map(|(k, v)| k.strip_prefix(&format!("{NS}.template.")).and_then(|tid| parse_template(v, tid)))
        .filter(|t| !hidden(t, &deleted, now)).collect()).unwrap_or_default();
    out.sort_by_key(|t| t["name"].as_str().unwrap_or("").to_lowercase());
    out
}

// ---------------------------------------------------------------- requests

fn inbox(c: &Client) -> Result<String, String> {
    c.role_ids()?.get("inbox").cloned().ok_or_else(|| "No Inbox.".into())
}

/// Whether the server keeps metadata (templates need it).
pub fn supported(c: &Client) -> bool { c.has("urn:ietf:params:jmap:metadata") }

/// All personal templates (sorted by name).
pub fn list(c: &Client) -> Result<Vec<Value>, String> {
    if !supported(c) { return Ok(vec![]); }
    let id = inbox(c)?;
    let r = c.call(json!([["Mailbox/get", {"accountId": c.account, "ids": [id], "properties": ["metadata"]}, "m"]]))?;
    Ok(from_metadata(&r[0][1]["list"][0]["metadata"]))
}

fn new_id() -> String {
    let mut b = [0u8; 16];
    let _ = getrandom::getrandom(&mut b);
    b[6] = (b[6] & 0x0f) | 0x40;
    b[8] = (b[8] & 0x3f) | 0x80;
    let h: String = b.iter().map(|x| format!("{x:02x}")).collect();
    format!("{}-{}-{}-{}-{}", &h[0..8], &h[8..12], &h[12..16], &h[16..20], &h[20..32])
}

fn now_iso() -> String { chrono::Utc::now().to_rfc3339_opts(chrono::SecondsFormat::Millis, true) }

/// Create (`old` None) or update a template: the whole entry is written, so another Bulwark
/// device picks it up by its newer `updatedAt`.
pub fn save(c: &Client, old: Option<&Value>, f: &Fields) -> Result<(), String> {
    if !supported(c) { return Err("This server keeps no metadata, so it cannot store templates (Stalwart 1.0 or later does).".into()); }
    let t = build(old, f, &now_iso(), new_id)?;
    let id = t["id"].as_str().unwrap_or("").to_string();
    if !valid_key(&format!("template.{id}")) { return Err("This template's id cannot be a metadata key.".into()); }
    let inbox = inbox(c)?;
    let Some(old) = old else {
        let (_, bad) = c.update("Mailbox/set", &c.account, vec![(inbox, json!({ format!("metadata/{NS}.template.{id}"): t }))])?;
        return if bad.is_empty() { Ok(()) } else { Err(bad.join("; ")) };
    };
    // an edit: the server's copy must still be the one this edit started from, and the write
    // carries the state that was checked (ifInState), so a Bulwark edit in between is not lost
    let r = c.call(json!([["Mailbox/get", {"accountId": c.account, "ids": [inbox.clone()], "properties": ["metadata"]}, "m"]]))?;
    check_fresh(old, &r[0][1]["list"][0]["metadata"])?;
    let mut args = json!({"accountId": c.account, "update": { inbox.clone(): { format!("metadata/{NS}.template.{id}"): t }}});
    if r[0][1]["state"].is_string() { args["ifInState"] = r[0][1]["state"].clone(); }
    let r = c.request(json!([["Mailbox/set", args, "s"]]))?;
    if r[0][0] == "error" {
        if r[0][1]["type"] == "stateMismatch" { return Err("This template was changed elsewhere; reload and try again.".into()); }
        return Err(crate::jmap::method_error(&r[0][1]));
    }
    match r[0][1]["notUpdated"].get(&inbox) { Some(err) => Err(crate::jmap::set_error(err)), None => Ok(()) }
}

/// Delete: remove the entry and record a tombstone so other Bulwark devices drop it too.
/// The tombstone map is read, changed and written back whole, so the write carries the state it
/// was read at (RFC 8620 §5.3 ifInState): if another device changed the Inbox meanwhile the
/// server answers stateMismatch and the delete starts over from a fresh read.
pub fn delete(c: &Client, id: &str) -> Result<(), String> {
    let inbox = inbox(c)?;
    for _ in 0..3 {
        let r = c.call(json!([["Mailbox/get", {"accountId": c.account, "ids": [inbox.clone()], "properties": ["metadata"]}, "m"]]))?;
        let state = r[0][1]["state"].clone();
        let marker = tombstone(&r[0][1]["list"][0]["metadata"][format!("{NS}.templates")], id);
        let mut args = json!({"accountId": c.account, "update": { inbox.clone(): {
            format!("metadata/{NS}.template.{id}"): Value::Null,
            format!("metadata/{NS}.templates"): marker,
        }}});
        if state.is_string() { args["ifInState"] = state; }
        let r = c.request(json!([["Mailbox/set", args, "s"]]))?;
        if r[0][0] == "error" {
            if r[0][1]["type"] == "stateMismatch" { continue; }
            return Err(crate::jmap::method_error(&r[0][1]));
        }
        return match r[0][1]["notUpdated"].get(&inbox) {
            Some(err) => Err(crate::jmap::set_error(err)),
            None => Ok(()),
        };
    }
    Err("The templates kept changing on another device; try again.".into())
}

/// The tombstone map with `id` marked deleted now.
fn tombstone(old: &Value, id: &str) -> Value {
    let mut marker = old.clone();
    if !marker.is_object() { marker = json!({"version": 1, "deleted": {}}); }
    if !marker["deleted"].is_object() { marker["deleted"] = json!({}); }
    marker["version"] = json!(1);
    marker["deleted"][id] = json!(now_iso());
    marker
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::TimeZone;

    fn vals(p: &[(&str, &str)]) -> HashMap<String, String> { p.iter().map(|(a, b)| (a.to_string(), b.to_string())).collect() }

    #[test]
    fn tombstone_keeps_other_deletions() {
        let m = tombstone(&json!({"version": 1, "deleted": {"a": "2026-01-01T00:00:00.000Z"}}), "b");
        assert!(m["deleted"]["a"].is_string() && m["deleted"]["b"].is_string());
        let m = tombstone(&Value::Null, "x");
        assert_eq!(m["version"], 1);
        assert!(m["deleted"]["x"].is_string());
    }

    #[test]
    fn extracts_names_once_in_order() {
        assert_eq!(extract_placeholders("Hi {{recipient_name}}, {{company}} {{recipient_name}} {{ nope }} {{}} {{a-b}}", false), vec!["recipient_name", "company"]);
        assert_eq!(extract_placeholders("{{x:one two}} and {{y:}}", false), vec!["x", "y"]);
        assert!(extract_placeholders("{{x", false).is_empty());
        assert!(extract_placeholders("{{x:a{b}}", false).is_empty());
        assert_eq!(template_placeholders("Re {{topic}}", "{{who}} {{topic}}", false), vec!["topic", "who"]);
    }

    #[test]
    fn defaults_may_hold_angle_brackets_in_text_but_not_in_html() {
        let t = "{{contact:Jane <jane@example.test>}}";
        assert_eq!(extract_placeholders(t, false), vec!["contact"]);
        assert_eq!(placeholder_defaults("", t, false)["contact"], "Jane <jane@example.test>");
        assert!(extract_placeholders(t, true).is_empty());
        assert_eq!(placeholder_defaults("", "<p>{{c:A &amp; B}}</p>", true)["c"], "A & B");
        assert_eq!(placeholder_defaults("{{n:S}}", "{{n:B}}", false)["n"], "S");
    }

    #[test]
    fn substitutes_text_and_escapes_html() {
        let v = vals(&[("name", "Smith <j@example.test>")]);
        assert_eq!(substitute("Dear {{name}}, {{other}}", &v, false), "Dear Smith <j@example.test>, {{other}}");
        assert_eq!(substitute("<p>{{name}}</p>", &v, true), "<p>Smith &lt;j@example.test&gt;</p>");
        assert_eq!(substitute("{{name:Friend}}!", &v, false), "Smith <j@example.test>!");
        assert_eq!(substitute("{{x:Friend}}!", &v, false), "{{x:Friend}}!");
        assert_eq!(substitute("ünï {{name}} ✓", &vals(&[("name", "é")]), false), "ünï é ✓");
    }

    #[test]
    fn auto_fill_has_dates_and_the_sender() {
        let d = chrono::Utc.with_ymd_and_hms(2026, 3, 5, 9, 0, 0).unwrap();
        let m = auto_fill(d, "Ann Example");
        assert_eq!(m["date"], "March 5, 2026");
        assert_eq!((m["short_date"].as_str(), m["day"].as_str(), m["month"].as_str(), m["year"].as_str()), ("03/05/2026", "05", "03", "2026"));
        assert_eq!(m["day_of_week"], "Thursday");
        assert_eq!(m["sender_name"], "Ann Example");
        assert!(!auto_fill(d, "").contains_key("sender_name"));
        assert!(built_in_label("sender_name").is_some() && built_in_label("nope").is_none());
    }

    #[test]
    fn names_are_validated() {
        assert!(validate_name("  ").is_some());
        assert!(validate_name(&"x".repeat(200)).is_none());
        assert!(validate_name(&"x".repeat(201)).is_some());
    }

    #[test]
    fn reads_an_entry_like_bulwark() {
        let e = json!({"name": "Hello", "subject": "S <a@b.test>", "body": "<p>Hi</p><script>alert(1)</script>", "isHTML": true,
            "defaultRecipients": {"to": ["a@example.test"], "cc": [1]}, "identityId": "i1", "isFavorite": true, "junk": 1});
        let t = parse_template(&e, "abc").unwrap();
        assert_eq!(t["id"], "abc");
        assert_eq!(t["subject"], "S <a@b.test>");
        assert!(!t["body"].as_str().unwrap().contains("<script") && t["body"].as_str().unwrap().contains("Hi"), "{}", t["body"]);
        assert_eq!(t["isHTML"], true);
        assert_eq!(t["updatedAt"], UNKNOWN_TIME);
        assert_eq!(t["defaultRecipients"]["to"][0], "a@example.test");
        assert!(t.get("junk").is_none());
        assert_eq!(parse_template(&json!({"name": "p", "body": "<b>x</b> & y"}), "i").unwrap()["body"], "<b>x</b> & y");
        assert!(parse_template(&json!({"name": "  "}), "i").is_none());
        assert!(parse_template(&json!("x"), "i").is_none());
    }

    #[test]
    fn metadata_lists_templates_and_applies_tombstones() {
        let meta = json!({
            "org.bulwarkmail.template.b": {"name": "Beta", "updatedAt": "2026-01-02T00:00:00.000Z"},
            "org.bulwarkmail.template.a": {"name": "alpha", "updatedAt": "2026-01-02T00:00:00.000Z"},
            "org.bulwarkmail.template.gone": {"name": "Gone", "updatedAt": "2026-01-01T00:00:00.000Z"},
            "org.bulwarkmail.template.back": {"name": "Back", "updatedAt": "2026-03-01T00:00:00.000Z"},
            "org.bulwarkmail.templates": {"version": 1, "deleted": {"gone": "2026-02-01T00:00:00.000Z", "back": "2026-02-01T00:00:00.000Z"}},
            "org.bulwarkmail.tags": {"x": 1},
            "org.other.template.z": {"name": "Z"},
        });
        let names: Vec<String> = from_metadata_at(&meta, chrono::Utc.with_ymd_and_hms(2026, 3, 10, 0, 0, 0).unwrap()).iter().map(|t| t["name"].as_str().unwrap().to_string()).collect();
        assert_eq!(names, vec!["alpha", "Back", "Beta"]);
        assert!(from_metadata(&Value::Null).is_empty());
    }

    #[test]
    fn builds_new_and_edited_entries() {
        let f = Fields { name: " Intro ".into(), subject: "Hi".into(), body: "<p>x</p><script>1</script>".into(), html: true, category: " Sales ".into(),
            to: vec!["a@example.test".into()], favorite: true, ..Default::default() };
        let t = build(None, &f, "2026-05-01T00:00:00.000Z", || "new-id".into()).unwrap();
        assert_eq!(t["id"], "new-id");
        assert_eq!((t["name"].as_str(), t["category"].as_str()), (Some("Intro"), Some("Sales")));
        assert_eq!(t["createdAt"], t["updatedAt"]);
        assert!(!t["body"].as_str().unwrap().contains("script"));
        assert_eq!(t["defaultRecipients"], json!({"to": ["a@example.test"]}));
        let mut old = t.clone();
        old["identityId"] = json!("i9");
        let f2 = Fields { name: "Intro 2".into(), body: "plain".into(), ..Default::default() };
        let e = build(Some(&old), &f2, "2026-06-01T00:00:00.000Z", || unreachable!()).unwrap();
        assert_eq!((e["id"].as_str(), e["createdAt"].as_str(), e["updatedAt"].as_str()), (Some("new-id"), Some("2026-05-01T00:00:00.000Z"), Some("2026-06-01T00:00:00.000Z")));
        assert_eq!(e["identityId"], "i9");
        assert_eq!(e["isHTML"], false);
        assert!(e.get("defaultRecipients").is_none());
        assert!(build(None, &Fields::default(), "t", || "i".into()).is_err());
    }

    #[test]
    fn an_entry_over_8kb_is_refused() {
        let at = |n: usize| build(None, &Fields { name: "Big".into(), body: "x".repeat(n), ..Default::default() }, "2026-05-01T00:00:00.000Z", || "id".into());
        let base = value_bytes(&at(0).unwrap());
        assert_eq!(value_bytes(&at(MAX_ENTRY_BYTES - base).unwrap()), MAX_ENTRY_BYTES);
        assert!(at(MAX_ENTRY_BYTES - base + 1).unwrap_err().contains("too big"));
        // bytes, not characters
        assert_eq!(value_bytes(&json!("é")), 4);
    }

    #[test]
    fn metadata_keys_follow_bulwarks_rule() {
        assert!(valid_key("template.0b9f1c2e-aaaa-4bbb-8ccc-1234567890ab"));
        assert!(!valid_key("template.Upper"));
        assert!(!valid_key("template.-x"));
        assert!(!valid_key("template.a b"));
        assert!(!valid_key(&format!("template.{}", "a".repeat(120))));
    }

    #[test]
    fn a_rename_keeps_a_rich_body_the_cleaner_would_change() {
        let raw = "<table><tr><td>Hi {{n}}</td></tr></table><p>x</p>";
        assert_ne!(clean_html(raw), raw);
        let stored = json!({"name": "T", "body": raw, "isHTML": true, "updatedAt": "2026-05-01T00:00:00.000Z", "createdAt": "2026-05-01T00:00:00.000Z"});
        let old = parse_template(&stored, "id1").unwrap();
        assert_eq!(old["rawBody"], raw);
        // the form is filled from rawBody; a rename-only save writes it back as it was
        let f = Fields { name: "Renamed".into(), body: old["rawBody"].as_str().unwrap().into(), html: true, ..Default::default() };
        let e = build(Some(&old), &f, "2026-06-01T00:00:00.000Z", || unreachable!()).unwrap();
        assert_eq!(e["body"], raw);
        assert!(e.get("rawBody").is_none());
        // a changed body is cleaned; switching the format is a change too
        let f2 = Fields { body: format!("{raw}<script>1</script>"), ..f.clone() };
        assert!(!build(Some(&old), &f2, "2026-06-01T00:00:00.000Z", || unreachable!()).unwrap()["body"].as_str().unwrap().contains("script"));
        let f3 = Fields { html: false, ..f };
        assert_eq!(build(Some(&old), &f3, "2026-06-01T00:00:00.000Z", || unreachable!()).unwrap()["body"], raw);
    }

    #[test]
    fn an_edit_is_never_older_than_what_it_replaces() {
        let old = json!({"id": "i", "createdAt": "2026-05-01T00:00:00.000Z", "updatedAt": "2026-07-01T00:00:00.000Z"});
        let f = Fields { name: "N".into(), ..Default::default() };
        let e = build(Some(&old), &f, "2026-06-01T00:00:00.000Z", || unreachable!()).unwrap();
        assert_eq!(e["updatedAt"], "2026-07-01T00:00:00.001Z");
    }

    #[test]
    fn a_stale_save_is_refused() {
        let old = json!({"id": "i", "updatedAt": "2026-05-01T00:00:00.000Z"});
        let meta = |u: &str| json!({"org.bulwarkmail.template.i": {"name": "N", "updatedAt": u}});
        assert!(check_fresh(&old, &meta("2026-05-01T00:00:00.000Z")).is_ok());
        assert!(check_fresh(&old, &meta("2026-05-02T00:00:00.000Z")).unwrap_err().contains("changed elsewhere"));
        assert!(check_fresh(&old, &json!({})).is_err());
        let mut gone = meta("2026-05-01T00:00:00.000Z");
        gone["org.bulwarkmail.templates"] = json!({"deleted": {"i": "2026-05-03T00:00:00.000Z"}});
        assert!(check_fresh(&old, &gone).is_err());
        // a tombstone older than the edit does not block it
        gone["org.bulwarkmail.templates"] = json!({"deleted": {"i": "2026-04-03T00:00:00.000Z"}});
        assert!(check_fresh(&old, &gone).is_ok());
    }

    #[test]
    fn tombstones_with_a_bad_date_or_older_than_90_days_count_for_nothing() {
        let now = chrono::Utc.with_ymd_and_hms(2026, 6, 1, 0, 0, 0).unwrap();
        let meta = json!({
            "org.bulwarkmail.template.a": {"name": "A", "updatedAt": "2026-01-01T00:00:00.000Z"},
            "org.bulwarkmail.template.b": {"name": "B", "updatedAt": "2026-01-01T00:00:00.000Z"},
            "org.bulwarkmail.template.c": {"name": "C", "updatedAt": "2026-01-01T00:00:00.000Z"},
            "org.bulwarkmail.templates": {"deleted": {"a": "not a date", "b": "2026-01-02T00:00:00.000Z", "c": "2026-05-20T00:00:00.000Z"}},
        });
        let names: Vec<String> = from_metadata_at(&meta, now).iter().map(|t| t["name"].as_str().unwrap().to_string()).collect();
        assert_eq!(names, vec!["A", "B"]);
    }

    #[test]
    fn names_are_ascii_and_html_values_escape_quotes() {
        assert_eq!(extract_placeholders("{{naïve}} {{ok_1}}", false), vec!["ok_1"]);
        assert_eq!(substitute("<a title=\"{{t}}\">", &vals(&[("t", "a\"b'c")]), true), "<a title=\"a&quot;b&#39;c\">");
    }

    #[test]
    fn links_keep_only_safe_schemes() {
        for ok in ["https://a.test", "HTTP://a.test", "mailto:a@example.test", "tel:+1", "cid:x", "/rel", "page.html?a=b:c", "#x"] { assert!(safe_link(ok), "{ok}"); }
        for bad in ["javascript:alert(1)", " JaVa\tScript:x", "data:text/html,x", "vbscript:x", "file:///c"] { assert!(!safe_link(bad), "{bad}"); }
        let c = clean_html("<p><a href=\"javascript:alert(1)\">x</a> <a href=\"https://a.test\">y</a></p>");
        assert!(!c.contains("javascript") && c.contains("https://a.test"));
    }

    #[test]
    fn splits_addresses() {
        assert_eq!(split_addrs(" a@example.test, ,b@example.test "), vec!["a@example.test", "b@example.test"]);
    }
}
