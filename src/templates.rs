//! Email templates shared with Bulwark: kept in the Inbox's JMAP metadata
//! (`urn:ietf:params:jmap:metadata`), one entry per template under
//! `org.bulwarkmail.template.<id>`, deletions as tombstones in `org.bulwarkmail.templates`.
use crate::jmap::Client;
use serde_json::{json, Value};

const NS: &str = "org.bulwarkmail";

fn inbox(c: &Client) -> Result<String, String> {
    c.role_ids()?.get("inbox").cloned().ok_or_else(|| "No Inbox.".into())
}

/// All personal templates (sorted by name).
pub fn list(c: &Client) -> Result<Vec<Value>, String> {
    if !c.has("urn:ietf:params:jmap:metadata") { return Ok(vec![]); }
    let id = inbox(c)?;
    let r = c.call(json!([["Mailbox/get", {"accountId": c.account, "ids": [id], "properties": ["metadata"]}, "m"]]))?;
    let meta = r[0][1]["list"][0]["metadata"].clone();
    let deleted = meta[format!("{NS}.templates")]["deleted"].as_object().cloned().unwrap_or_default();
    let mut out: Vec<Value> = meta.as_object().map(|o| o.iter()
        .filter_map(|(k, v)| k.strip_prefix(&format!("{NS}.template.")).map(|tid| (tid.to_string(), v.clone())))
        .filter(|(tid, _)| !deleted.contains_key(tid))
        .map(|(_, v)| v).collect()).unwrap_or_default();
    out.sort_by_key(|t| t["name"].as_str().unwrap_or("").to_lowercase());
    Ok(out)
}

fn new_id() -> String {
    let mut b = [0u8; 16];
    let _ = getrandom::getrandom(&mut b);
    b[6] = (b[6] & 0x0f) | 0x40;
    b[8] = (b[8] & 0x3f) | 0x80;
    let h: String = b.iter().map(|x| format!("{x:02x}")).collect();
    format!("{}-{}-{}-{}-{}", &h[0..8], &h[8..12], &h[12..16], &h[16..20], &h[20..32])
}

/// Create a template (Bulwark's EmailTemplate shape).
pub fn create(c: &Client, name: &str, subject: &str, body: &str) -> Result<(), String> {
    let id = new_id();
    let now = chrono::Utc::now().to_rfc3339_opts(chrono::SecondsFormat::Millis, true);
    let t = json!({"id": id, "name": name, "subject": subject, "body": body, "isHTML": false, "category": "",
        "isFavorite": false, "createdAt": now, "updatedAt": now});
    let inbox = inbox(c)?;
    let (_, bad) = c.update("Mailbox/set", &c.account, vec![(inbox, json!({ format!("metadata/{NS}.template.{id}"): t }))])?;
    if bad.is_empty() { Ok(()) } else { Err(bad.join("; ")) }
}

/// Delete: remove the entry and record a tombstone so other Bulwark devices drop it too.
pub fn delete(c: &Client, id: &str) -> Result<(), String> {
    let inbox = inbox(c)?;
    let r = c.call(json!([["Mailbox/get", {"accountId": c.account, "ids": [inbox.clone()], "properties": ["metadata"]}, "m"]]))?;
    let mut marker = r[0][1]["list"][0]["metadata"][format!("{NS}.templates")].clone();
    if !marker.is_object() { marker = json!({"version": 1, "deleted": {}}); }
    marker["version"] = json!(1);
    marker["deleted"][id] = json!(chrono::Utc::now().to_rfc3339_opts(chrono::SecondsFormat::Millis, true));
    let (_, bad) = c.update("Mailbox/set", &c.account, vec![(inbox, json!({
        format!("metadata/{NS}.template.{id}"): Value::Null,
        format!("metadata/{NS}.templates"): marker,
    }))])?;
    if bad.is_empty() { Ok(()) } else { Err(bad.join("; ")) }
}
