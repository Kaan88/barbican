//! Settings pages backed by server objects: Out of office (VacationResponse), Filters (Sieve),
//! Folders (Mailbox), Identities & signatures (Identity). Values are cached here; edits are
//! written back to the server (text edits are debounced).
use crate::jmap::Client;
use crate::SRow;
use serde_json::{json, Value};
use slint::{ModelRc, SharedString, VecModel};
use std::sync::{Arc, Mutex};

#[derive(Default)]
pub struct Cache {
    pub vacation: Option<Value>,
    pub scripts: Vec<Value>,
    pub script_text: String,
    pub script_status: String,
    pub mailboxes: Vec<Value>,
    pub identities: Vec<Value>,
    pub new_folder: String,
    pub confirm_delete: String,
    pub error: String,
    pub archived: Vec<Value>,
}

const ARCHIVE_USING: [&str; 2] = ["urn:ietf:params:jmap:core", "urn:stalwart:jmap"];

pub type Shared = Arc<Mutex<Cache>>;

fn s(v: &str) -> SharedString { SharedString::from(v) }
fn row(kind: &str, key: &str, label: &str, desc: &str, text: &str, on: bool) -> SRow {
    SRow { kind: s(kind), key: s(key), label: s(label), desc: s(desc), options: ModelRc::new(VecModel::from(Vec::<SharedString>::new())), index: 0, on, text: s(text) }
}

/// Fetch what a page needs. Runs on a background thread.
pub fn load(tab: &str, c: &Client, cache: &Shared) {
    let res: Result<(), String> = (|| {
        match tab {
            "vacation" => {
                let acc = c.account_for("urn:ietf:params:jmap:vacationresponse");
                let r = c.call(json!([["VacationResponse/get", {"accountId": acc, "ids": ["singleton"]}, "v"]]))?;
                cache.lock().unwrap().vacation = Some(r[0][1]["list"][0].clone());
            }
            "filters" => {
                let acc = c.account_for("urn:ietf:params:jmap:sieve");
                let r = c.call(json!([["SieveScript/get", {"accountId": acc, "ids": null, "properties": ["id", "name", "blobId", "isActive"]}, "s"]]))?;
                let scripts = r[0][1]["list"].as_array().cloned().unwrap_or_default();
                let active = scripts.iter().find(|x| x["isActive"] == true).or(scripts.first()).cloned();
                let text = match active.as_ref().and_then(|a| a["blobId"].as_str()) {
                    Some(b) => String::from_utf8_lossy(&c.download(b, "script.sieve", "application/sieve")?).to_string(),
                    None => String::new(),
                };
                let mut k = cache.lock().unwrap();
                k.scripts = scripts;
                k.script_text = text;
            }
            "organize" => { cache.lock().unwrap().mailboxes = c.mailboxes()?; }
            "recover" => {
                // Stalwart's deleted-item archive (x:ArchivedItem), as Bulwark's Recover deleted items
                let acc = c.account.clone();
                let mut q = c.request_using(&ARCHIVE_USING, json!([["x:ArchivedItem/query", {"accountId": acc, "filter": {"accountId": acc}, "limit": 500}, "q"]]))?;
                if q[0][0] == "error" && q[0][1]["type"] == "unsupportedFilter" {
                    q = c.request_using(&ARCHIVE_USING, json!([["x:ArchivedItem/query", {"accountId": acc, "limit": 500}, "q"]]))?;
                }
                if q[0][0] == "error" { return Err(crate::jmap::method_error(&q[0][1])); }
                let ids: Vec<Value> = q[0][1]["ids"].as_array().cloned().unwrap_or_default();
                let mut items = vec![];
                for chunk in ids.chunks(c.max_get()) {
                    let g = c.request_using(&ARCHIVE_USING, json!([["x:ArchivedItem/get", {"accountId": acc, "ids": chunk}, "g"]]))?;
                    if g[0][0] == "error" { return Err(crate::jmap::method_error(&g[0][1])); }
                    items.extend(g[0][1]["list"].as_array().cloned().unwrap_or_default());
                }
                items.sort_by(|a, b| b["archivedAt"].as_str().unwrap_or("").cmp(a["archivedAt"].as_str().unwrap_or("")));
                cache.lock().unwrap().archived = items;
            }
            "account" | "composing" => { cache.lock().unwrap().identities = c.identities()?; }
            _ => {}
        }
        Ok(())
    })();
    cache.lock().unwrap().error = res.err().unwrap_or_default();
}

/// Rows for server-backed pages; `None` for pages this module doesn't own.
pub fn rows(tab: &str, cache: &Shared) -> Option<Vec<SRow>> {
    let k = cache.lock().unwrap();
    let mut out = vec![];
    if !k.error.is_empty() { out.push(row("value", "", "Could not load from the server", &k.error, "", false)); }
    match tab {
        "vacation" => {
            out.push(row("section", "", "Out of office", "Sends an automatic reply to people who write to you while you are away.", "", false));
            let Some(v) = k.vacation.as_ref() else { out.push(row("value", "", "Loading…", "", "", false)); return Some(out) };
            out.push(row("toggle", "server.vacation.isEnabled", "Automatic reply", "On while the dates below apply (or always, without dates).", "", v["isEnabled"] == true));
            out.push(row("text", "server.vacation.subject", "Subject", "Leave empty to reuse the original subject.", v["subject"].as_str().unwrap_or(""), false));
            out.push(row("textarea", "server.vacation.textBody", "Message", "", v["textBody"].as_str().unwrap_or(""), false));
            out.push(row("text", "server.vacation.fromDate", "First day", "YYYY-MM-DD, empty for no start date.", v["fromDate"].as_str().map(|d| &d[..10.min(d.len())]).unwrap_or(""), false));
            out.push(row("text", "server.vacation.toDate", "Last day", "YYYY-MM-DD, empty for no end date.", v["toDate"].as_str().map(|d| &d[..10.min(d.len())]).unwrap_or(""), false));
        }
        "filters" => {
            out.push(row("section", "", "Filters", "Rules the server applies to incoming mail, written in Sieve (RFC 5228).", "", false));
            let active = k.scripts.iter().find(|x| x["isActive"] == true).and_then(|x| x["name"].as_str()).unwrap_or("none");
            out.push(row("value", "", "Active script", &format!("{} script(s) on the server", k.scripts.len()), active, false));
            out.push(row("textarea", "server.sieve.text", "Script", "Example:  require \"fileinto\";  if address :is \"from\" \"news@example.com\" { fileinto \"Newsletters\"; }", &k.script_text, false));
            out.push(row("button", "server.sieve.save", "Save and activate", &k.script_status, "Save", false));
        }
        "recover" => {
            out.push(row("section", "", "Recover deleted items", "Messages deleted for good stay in the server's archive for a while and can be put back.", "", false));
            if k.error.is_empty() && k.archived.is_empty() {
                out.push(row("value", "", "Nothing to recover", "The archive is empty, or this server keeps no deleted items.", "", false));
            }
            for it in k.archived.iter().take(300) {
                let id = it["id"].as_str().unwrap_or("");
                let when = it["archivedAt"].as_str().and_then(|d| crate::mail::parse_time(&json!(d))).map(|t| t.format("%d %b %Y %H:%M").to_string()).unwrap_or_default();
                let subj = it["subject"].as_str().filter(|s| !s.is_empty()).unwrap_or("(no subject)");
                out.push(row("button", &format!("server.restore:{id}"), subj, &format!("{}  ·  deleted {when}", it["from"].as_str().unwrap_or("")), "Restore", false));
            }
        }
        "organize" => {
            out.push(row("section", "", "Folders", "Create, rename and delete your folders.", "", false));
            out.push(row("text", "server.folder.newname", "New folder", "Type a name, then press Create.", &k.new_folder, false));
            out.push(row("button", "server.folder.create", "", "", "Create", false));
            let mut boxes: Vec<&Value> = k.mailboxes.iter().collect();
            boxes.sort_by_key(|m| (m["role"].is_null(), m["name"].as_str().unwrap_or("").to_lowercase()));
            for m in boxes {
                let id = m["id"].as_str().unwrap_or("");
                let role = m["role"].as_str().unwrap_or("");
                let desc = if role.is_empty() { format!("{} messages", m["totalEmails"]) } else { format!("System folder ({role}), {} messages", m["totalEmails"]) };
                out.push(row("text", &format!("server.folder.rename:{id}"), m["name"].as_str().unwrap_or(""), &desc, m["name"].as_str().unwrap_or(""), false));
                if role.is_empty() {
                    let label = if k.confirm_delete == id { "Click again to delete" } else { "Delete" };
                    out.push(row("button", &format!("server.folder.delete:{id}"), "", "", label, false));
                }
            }
        }
        "account" | "composing" if !k.identities.is_empty() => {
            out.push(row("section", "", if tab == "account" { "Identities" } else { "Signatures" }, "The addresses you send from, with their names and signatures.", "", false));
            for i in &k.identities {
                let id = i["id"].as_str().unwrap_or("");
                let email = i["email"].as_str().unwrap_or("");
                if tab == "account" {
                    out.push(row("text", &format!("server.identity.name:{id}"), email, "Name shown to recipients", i["name"].as_str().unwrap_or(""), false));
                    let rt = i["replyTo"].as_array().and_then(|a| a.first()).and_then(|x| x["email"].as_str()).unwrap_or("");
                    out.push(row("text", &format!("server.identity.replyTo:{id}"), "Reply-to", "Optional address replies go to", rt, false));
                } else {
                    out.push(row("textarea", &format!("server.identity.textSignature:{id}"), &format!("Signature for {email}"), "Added to new messages, replies and forwards per the options above.", i["textSignature"].as_str().unwrap_or(""), false));
                }
            }
            return Some(out);
        }
        _ => return None,
    }
    Some(out)
}

/// Apply an edit locally (instant) and return a server write to run in the background.
pub fn edit(key: &str, value: &Value, cache: &Shared) -> Option<Box<dyn FnOnce(&Client) -> Result<(), String> + Send>> {
    let mut k = cache.lock().unwrap();
    if let Some(field) = key.strip_prefix("server.vacation.") {
        let f = field.to_string();
        let mut v = value.clone();
        if (f == "fromDate" || f == "toDate") && v.is_string() {
            let d = v.as_str().unwrap_or("").trim().to_string();
            if d.is_empty() { v = Value::Null; } else if d.len() == 10 { v = json!(format!("{d}T00:00:00Z")); } else { return None; }
        }
        if let Some(vac) = k.vacation.as_mut() { vac[&f] = v.clone(); }
        return Some(Box::new(move |c: &Client| {
            let acc = c.account_for("urn:ietf:params:jmap:vacationresponse");
            let (_, bad) = c.update("VacationResponse/set", &acc, vec![("singleton".into(), json!({ f: v }))])?;
            if bad.is_empty() { Ok(()) } else { Err(bad.join("; ")) }
        }));
    }
    if key == "server.sieve.text" { k.script_text = value.as_str().unwrap_or("").to_string(); return None; }
    if key == "server.folder.newname" { k.new_folder = value.as_str().unwrap_or("").to_string(); return None; }
    if let Some(id) = key.strip_prefix("server.folder.rename:") {
        let (id, name) = (id.to_string(), value.as_str().unwrap_or("").trim().to_string());
        if name.is_empty() { return None; }
        return Some(Box::new(move |c: &Client| c.rename_mailbox(&id, &name)));
    }
    if let Some(rest) = key.strip_prefix("server.identity.") {
        let (field, id) = rest.split_once(':')?;
        let (field, id) = (field.to_string(), id.to_string());
        let patch = if field == "replyTo" {
            let e = value.as_str().unwrap_or("").trim().to_string();
            json!({"replyTo": if e.is_empty() { Value::Null } else { json!([{"email": e}]) }})
        } else { json!({ field.clone(): value }) };
        if let Some(i) = k.identities.iter_mut().find(|i| i["id"] == id.as_str()) {
            for (pk, pv) in patch.as_object().unwrap() { i[pk] = pv.clone(); }
        }
        return Some(Box::new(move |c: &Client| {
            let acc = c.account_for("urn:ietf:params:jmap:submission");
            let (_, bad) = c.update("Identity/set", &acc, vec![(id, patch)])?;
            if bad.is_empty() { Ok(()) } else { Err(bad.join("; ")) }
        }));
    }
    None
}

/// Button actions on server pages. Returns true when the page should reload from the server.
pub fn button(key: &str, cache: &Shared) -> Option<Box<dyn FnOnce(&Client, &Shared) -> Result<(), String> + Send>> {
    if key == "server.folder.create" {
        let name = cache.lock().unwrap().new_folder.trim().to_string();
        if name.is_empty() { return None; }
        return Some(Box::new(move |c: &Client, k: &Shared| { c.create_mailbox(&name, None)?; k.lock().unwrap().new_folder.clear(); Ok(()) }));
    }
    if let Some(id) = key.strip_prefix("server.folder.delete:") {
        let id = id.to_string();
        let mut k = cache.lock().unwrap();
        if k.confirm_delete != id { k.confirm_delete = id; return Some(Box::new(|_, _| Ok(()))); }
        k.confirm_delete.clear();
        return Some(Box::new(move |c: &Client, _| c.delete_mailbox(&id)));
    }
    if let Some(id) = key.strip_prefix("server.restore:") {
        let id = id.to_string();
        return Some(Box::new(move |c: &Client, k: &Shared| {
            let acc = c.account.clone();
            let r = c.request_using(&ARCHIVE_USING, json!([["x:ArchivedItem/set", {"accountId": acc, "update": { id.clone(): {"status": "requestRestore"} }}, "r"]]))?;
            if r[0][0] == "error" { return Err(crate::jmap::method_error(&r[0][1])); }
            if let Some(e) = r[0][1]["notUpdated"].get(&id) { return Err(crate::jmap::set_error(e)); }
            k.lock().unwrap().archived.retain(|x| x["id"] != id.as_str());
            Ok(())
        }));
    }
    if key == "server.sieve.save" {
        let text = cache.lock().unwrap().script_text.clone();
        return Some(Box::new(move |c: &Client, k: &Shared| {
            let acc = c.account_for("urn:ietf:params:jmap:sieve");
            let (blob, _) = c.upload(text.into_bytes(), "application/sieve")?;
            let v = c.call(json!([["SieveScript/validate", {"accountId": acc, "blobId": blob}, "v"]]))?;
            if let Some(err) = v[0][1]["error"].as_object() {
                let msg = err.get("description").and_then(|d| d.as_str()).unwrap_or("invalid script").to_string();
                k.lock().unwrap().script_status = format!("Not saved: {msg}");
                return Ok(());
            }
            let existing = {
                let g = k.lock().unwrap();
                g.scripts.iter().find(|x| x["isActive"] == true).or(g.scripts.first()).and_then(|x| x["id"].as_str().map(String::from))
            };
            let r = match existing {
                Some(id) => c.call(json!([["SieveScript/set", {"accountId": acc, "update": { id.clone(): {"blobId": blob} }, "onSuccessActivateScript": id}, "s"]]))?,
                None => c.call(json!([["SieveScript/set", {"accountId": acc, "create": {"n": {"name": "barbican", "blobId": blob}}, "onSuccessActivateScript": "#n"}, "s"]]))?,
            };
            let ok = r[0][1]["updated"].is_object() || r[0][1]["created"].is_object();
            k.lock().unwrap().script_status = if ok { "Saved and active.".into() } else {
                format!("Not saved: {}", crate::jmap::set_error(r[0][1]["notUpdated"].as_object().and_then(|o| o.values().next())
                    .or(r[0][1]["notCreated"].as_object().and_then(|o| o.values().next())).unwrap_or(&Value::Null)))
            };
            Ok(())
        }));
    }
    None
}
