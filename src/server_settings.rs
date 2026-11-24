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
    /// Quota/get objects of the account (Account page, Storage usage).
    pub quota: Vec<Value>,
    pub new_folder: String,
    pub confirm_delete: String,
    pub error: String,
    pub archived: Vec<Value>,
    pub templates: Vec<Value>,
    pub tpl_name: String,
    pub tpl_subject: String,
    pub tpl_body: String,
    pub rules: Vec<Value>,
    pub external: String,
    pub rules_locked: bool,
    /// The filter script's whole metadata (vacation, forward, audience kept on save).
    pub filter_meta: Value,
    pub nr_from: String,
    pub nr_until: String,
    pub nr_name: String,
    pub nr_field: i32,
    pub nr_cmp: i32,
    pub nr_value: String,
    pub nr_action: i32,
    pub nr_action_value: String,
    pub nr_stop: bool,
    pub sieve_ext: Vec<String>,
    pub calendars: Vec<Value>,
    pub books: Vec<Value>,
    pub new_cal: String,
    pub new_book: String,
    /// Settings > Password & sign-in (src/security.rs).
    pub sec: crate::security::State,
}

/// Bulwark's calendar colour presets (lib/shared-calendar-colors.ts CALENDAR_COLORS).
pub const CAL_COLORS: [(&str, &str); 14] = [
    ("#3b82f6", "Blue"), ("#ef4444", "Red"), ("#22c55e", "Green"), ("#f59e0b", "Amber"), ("#8b5cf6", "Violet"), ("#ec4899", "Pink"), ("#14b8a6", "Teal"),
    ("#f97316", "Orange"), ("#06b6d4", "Cyan"), ("#84cc16", "Lime"), ("#6366f1", "Indigo"), ("#a855f7", "Purple"), ("#e11d48", "Rose"), ("#0ea5e9", "Sky"),
];

/// Calendars as the management page lists them: by sortOrder, then name.
pub fn sorted_calendars(list: &[Value]) -> Vec<Value> {
    let mut v = list.to_vec();
    v.sort_by_key(|c| (c["sortOrder"].as_i64().unwrap_or(0), c["name"].as_str().unwrap_or("").to_lowercase()));
    v
}

const NR_FIELDS: [&str; 7] = ["from", "to", "cc", "subject", "body", "attachment", "all"];
const NR_CMPS: [&str; 6] = ["contains", "not_contains", "is", "starts_with", "ends_with", "domain_is"];
const NR_ACTS: [&str; 7] = ["move", "copy", "mark_read", "star", "add_label", "forward", "discard"];

fn sel(key: &str, label: &str, opts: &[&str], idx: i32) -> SRow {
    let mut r = row("select", key, label, "", "", false);
    r.options = ModelRc::new(VecModel::from(opts.iter().map(|o| SharedString::from(*o)).collect::<Vec<_>>()));
    r.index = idx;
    r
}

/// Validate, upload and activate a Sieve script (shared by the rule editor and the raw editor).
pub fn save_script(c: &Client, text: &str, scripts: &[Value]) -> Result<(), String> {
    let acc = c.account_for("urn:ietf:params:jmap:sieve");
    let (blob, _) = c.upload(text.as_bytes().to_vec(), "application/sieve")?;
    let v = c.call(json!([["SieveScript/validate", {"accountId": acc, "blobId": blob}, "v"]]))?;
    if let Some(err) = v[0][1]["error"].as_object() {
        return Err(format!("Not saved: {}", err.get("description").and_then(|d| d.as_str()).unwrap_or("invalid script")));
    }
    let existing = scripts.iter().find(|x| x["isActive"] == true).or(scripts.first()).and_then(|x| x["id"].as_str().map(String::from));
    let r = match existing {
        Some(id) => c.call(json!([["SieveScript/set", {"accountId": acc, "update": { id.clone(): {"blobId": blob} }, "onSuccessActivateScript": id}, "s"]]))?,
        None => c.call(json!([["SieveScript/set", {"accountId": acc, "create": {"n": {"name": "barbican", "blobId": blob}}, "onSuccessActivateScript": "#n"}, "s"]]))?,
    };
    if r[0][1]["updated"].is_object() || r[0][1]["created"].is_object() { Ok(()) } else {
        Err(format!("Not saved: {}", crate::jmap::set_error(r[0][1]["notUpdated"].as_object().and_then(|o| o.values().next())
            .or(r[0][1]["notCreated"].as_object().and_then(|o| o.values().next())).unwrap_or(&Value::Null))))
    }
}


const ARCHIVE_USING: [&str; 2] = ["urn:ietf:params:jmap:core", "urn:stalwart:jmap"];

pub type Shared = Arc<Mutex<Cache>>;

/// Forget everything the pages cached, secrets first: Settings was closed, or the account signed
/// out or changed. The security page's epoch moves on, so a job or load still running for the old
/// account drops its answer instead of writing it into the next page.
pub fn reset(cache: &Shared) {
    let mut g = cache.lock().unwrap();
    let epoch = g.sec.epoch + 1;
    *g = Cache::default();
    g.sec.epoch = epoch;
}

fn s(v: &str) -> SharedString { SharedString::from(v) }
fn row(kind: &str, key: &str, label: &str, desc: &str, text: &str, on: bool) -> SRow {
    SRow { kind: s(kind), key: s(key), label: s(label), desc: s(desc), options: ModelRc::new(VecModel::from(Vec::<SharedString>::new())), index: 0, on, text: s(text) }
}

/// The Account page's storage lines (Bulwark account-settings.tsx "Storage usage"): a "meter" row
/// with the used / limit text, a percentage and a bar (`index` is the percent, -1: no bar).
fn quota_rows(list: &[Value]) -> Vec<SRow> {
    crate::quota::entries(list).into_iter().map(|e| {
        let mut r = row("meter", "", &e.label, &e.detail, &e.text, false);
        r.index = e.percent.map_or(-1, |p| p as i32);
        r
    }).collect()
}

/// Fetch what a page needs. Runs on a background thread.
pub fn load(tab: &str, c: &Client, cache: &Shared) {
    // typed passwords, codes and a secret just created are kept only while their page is open
    if tab != "security" { cache.lock().unwrap().sec.forget_secrets(); }
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
                let p = crate::sieve::parse(&text);
                let ext: Vec<String> = c.session["capabilities"]["urn:ietf:params:jmap:sieve"]["sieveExtensions"].as_array()
                    .or(c.session["accounts"][&acc]["accountCapabilities"]["urn:ietf:params:jmap:sieve"]["sieveExtensions"].as_array())
                    .map(|a| a.iter().filter_map(|x| x.as_str().map(String::from)).collect()).unwrap_or_default();
                let mut k = cache.lock().unwrap();
                k.sieve_ext = ext;
                k.scripts = scripts;
                k.rules = p.metadata["rules"].as_array().cloned().unwrap_or_default();
                k.external = p.external;
                k.rules_locked = p.locked;
                k.filter_meta = p.metadata;
                k.script_text = text;
                drop(k);
                // folders for the rule editor's move / copy targets
                let boxes = c.mailboxes().unwrap_or_default();
                cache.lock().unwrap().mailboxes = boxes;
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
            "calendars" => {
                let acc = c.account_for("urn:ietf:params:jmap:calendars");
                let r = c.call(json!([["Calendar/get", {"accountId": acc, "ids": null, "properties": ["id", "name", "color", "isDefault", "isVisible", "isSubscribed", "sortOrder", "myRights", "description"]}, "c"]]))?;
                if r[0][0] == "error" { return Err(crate::jmap::method_error(&r[0][1])); }
                cache.lock().unwrap().calendars = sorted_calendars(r[0][1]["list"].as_array().map(|a| a.as_slice()).unwrap_or(&[]));
            }
            "addressbooks" => {
                let acc = c.account_for("urn:ietf:params:jmap:contacts");
                let r = c.call(json!([["AddressBook/get", {"accountId": acc, "ids": null, "properties": ["id", "name", "isDefault", "isSubscribed", "sortOrder", "myRights", "description"]}, "a"]]))?;
                if r[0][0] == "error" { return Err(crate::jmap::method_error(&r[0][1])); }
                let mut v = r[0][1]["list"].as_array().cloned().unwrap_or_default();
                v.sort_by_key(|b| (b["isDefault"] != true, b["name"].as_str().unwrap_or("").to_lowercase()));
                cache.lock().unwrap().books = v;
            }
            "account" => {
                // the storage line is a courtesy: a server without quotas, or one that refuses, keeps the page
                let quota = c.quotas().unwrap_or_default();
                let ids = c.identities()?;
                let mut k = cache.lock().unwrap();
                k.quota = quota;
                k.identities = ids;
            }
            "security" => { crate::security::load(c, cache)?; }
            "composing" => {
                let ids = c.identities()?;
                let tpls = crate::templates::list(c).unwrap_or_default();
                let mut k = cache.lock().unwrap();
                k.identities = ids;
                k.templates = tpls;
            }
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
    let quota = if tab == "account" { quota_rows(&k.quota) } else { vec![] };
    match tab {
        // hidden (None) for an account without Stalwart's management capability
        "security" => return crate::security::rows(&k.sec).map(|mut r| { r.splice(0..0, out); r }),
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
            out.push(row("section", "", "Rules", "Rules are shared with Bulwark. Rules made by other apps are kept as they are.", "", false));
            if k.rules_locked {
                out.push(row("value", "", "Edit these rules in Bulwark", "Bulwark would treat this script as edited by hand (unknown version or options, or a date it cannot read). The script below still works.", "", false));
            } else {
                if k.rules.is_empty() { out.push(row("value", "", "No rules yet", "", "", false)); }
                // one row per rule (ui/pim-filters.slint FilterRuleRow): on/off, edit, reorder,
                // run on existing mail, delete; rules run top to bottom
                for r in &k.rules {
                    let id = r["id"].as_str().unwrap_or("");
                    out.push(row("rule", id, r["name"].as_str().unwrap_or("Rule"), &crate::filters_x::summary(r), "", r["enabled"] != false));
                }
                out.push(row("button", "filters.new", "New rule", "Conditions on the sender, recipients, subject, any header, size, body or attachments; one or more actions.", "New rule…", false));
            }
            out.push(row("section", "", "Script", "The Sieve script the server runs (advanced).", "", false));
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
                let when = it["archivedAt"].as_str().and_then(|d| crate::mail::parse_time(&json!(d))).map(|t| t.format(if crate::settings::h12() { "%d %b %Y %-I:%M %p" } else { "%d %b %Y %H:%M" }).to_string()).unwrap_or_default();
                let subj = it["subject"].as_str().filter(|s| !s.is_empty()).unwrap_or("(no subject)");
                out.push(row("button", &format!("server.restore:{id}"), subj, &format!("{}  ·  deleted {when}", it["from"].as_str().unwrap_or("")), "Restore", false));
            }
        }
        "calendars" => {
            out.push(row("section", "", "Calendars", "Create, rename, colour, show or hide and delete your calendars. Right-click a calendar for more.", "", false));
            out.push(row("text", "server.cal.newname", "New calendar", "Type a name, then press Create.", &k.new_cal, false));
            out.push(row("button", "server.cal.create", "", "", "Create", false));
            for cal in &k.calendars {
                let id = cal["id"].as_str().unwrap_or("");
                let name = cal["name"].as_str().unwrap_or("Calendar");
                let mut desc = vec![];
                if cal["isDefault"] == true { desc.push("Default"); }
                if cal["isVisible"] == false { desc.push("Hidden"); }
                out.push(row("section", &format!("calset:{id}"), name, &desc.join(" · "), "", false));
                out.push(row("text", &format!("server.cal.rename:{id}"), "Name", "", name, false));
                let color = cal["color"].as_str().unwrap_or("#3b82f6").to_lowercase();
                let mut labels: Vec<&str> = CAL_COLORS.iter().map(|c| c.1).collect();
                let idx = CAL_COLORS.iter().position(|c| c.0 == color).unwrap_or_else(|| { labels.push("Custom"); labels.len() - 1 });
                let mut r = sel(&format!("server.cal.color:{id}"), "Colour", &labels, idx as i32);
                r.text = s(&color);
                out.push(r);
                out.push(row("toggle", &format!("server.cal.visible:{id}"), "Show in calendar", "", "", cal["isVisible"] != false));
                if cal["isDefault"] != true {
                    out.push(row("button", &format!("server.cal.default:{id}"), "Default calendar", "New events go here.", "Set as default", false));
                    out.push(row("button", &format!("server.cal.delete:{id}"), "", "Deletes the calendar and all its events.", if k.confirm_delete == id { "Click again to delete" } else { "Delete calendar" }, false));
                }
            }
        }
        "addressbooks" => {
            out.push(row("section", "", "Address books", "Create, rename and delete your address books. Right-click an address book for more.", "", false));
            out.push(row("text", "server.book.newname", "New address book", "Type a name, then press Create.", &k.new_book, false));
            out.push(row("button", "server.book.create", "", "", "Create", false));
            for b in &k.books {
                let id = b["id"].as_str().unwrap_or("");
                let name = b["name"].as_str().unwrap_or("Address book");
                out.push(row("section", &format!("bookset:{id}"), name, if b["isDefault"] == true { "Default" } else { "" }, "", false));
                out.push(row("text", &format!("server.book.rename:{id}"), "Name", "", name, false));
                if b["isDefault"] != true {
                    out.push(row("button", &format!("server.book.default:{id}"), "Default address book", "New contacts go here.", "Set as default", false));
                    out.push(row("button", &format!("server.book.delete:{id}"), "", "Deletes the address book and its contacts.", if k.confirm_delete == id { "Click again to delete" } else { "Delete address book" }, false));
                }
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
        "account" | "composing" if !k.identities.is_empty() || !quota.is_empty() => {
            if tab == "account" { out.extend(quota); }
            if !k.identities.is_empty() {
                out.push(row("section", "", if tab == "account" { "Identities" } else { "Signatures" }, "The addresses you send from, with their names and signatures.", "", false));
            }
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
            if tab == "composing" {
                out.push(row("section", "", "Email templates", "Reusable messages, shared with Bulwark (kept in your Inbox's metadata). Insert them from the composer.", "", false));
                for t in &k.templates {
                    let id = t["id"].as_str().unwrap_or("");
                    out.push(row("button", &format!("server.tpl.delete:{id}"), t["name"].as_str().unwrap_or("(unnamed)"),
                        t["subject"].as_str().unwrap_or(""), if k.confirm_delete == id { "Click again to delete" } else { "Delete" }, false));
                }
                out.push(row("text", "server.tpl.name", "New template name", "", &k.tpl_name, false));
                out.push(row("text", "server.tpl.subject", "Subject", "", &k.tpl_subject, false));
                out.push(row("textarea", "server.tpl.body", "Text", "", &k.tpl_body, false));
                out.push(row("button", "server.tpl.create", "", "", "Save template", false));
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
    if let Some(field) = key.strip_prefix("server.sec.") {
        crate::security::edit(&mut k.sec, field, value);
        return None;
    }
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
    match key {
        "server.nr.name" => { k.nr_name = value.as_str().unwrap_or("").into(); return None; }
        "server.nr.value" => { k.nr_value = value.as_str().unwrap_or("").into(); return None; }
        "server.nr.avalue" => { k.nr_action_value = value.as_str().unwrap_or("").into(); return None; }
        "server.nr.field" => { k.nr_field = value.as_i64().unwrap_or(0) as i32; return None; }
        "server.nr.cmp" => { k.nr_cmp = value.as_i64().unwrap_or(0) as i32; return None; }
        "server.nr.action" => { k.nr_action = value.as_i64().unwrap_or(0) as i32; return None; }
        "server.nr.from" => { k.nr_from = value.as_str().unwrap_or("").into(); return None; }
        "server.nr.until" => { k.nr_until = value.as_str().unwrap_or("").into(); return None; }
        "server.nr.stop" => { k.nr_stop = value.as_bool().unwrap_or(false); return None; }
        _ => {}
    }
    if let Some(id) = key.strip_prefix("server.rule.toggle:") {
        let on = value.as_bool().unwrap_or(true);
        if let Some(r) = k.rules.iter_mut().find(|r| r["id"] == id) { r["enabled"] = json!(on); }
        let (script, scripts) = (crate::sieve::generate_meta(&crate::sieve::with_rules(&k.filter_meta, &k.rules), &k.external, &k.sieve_ext), k.scripts.clone());
        return Some(Box::new(move |c: &Client| save_script(c, &script, &scripts)));
    }
    match key {
        "server.tpl.name" => { k.tpl_name = value.as_str().unwrap_or("").into(); return None; }
        "server.tpl.subject" => { k.tpl_subject = value.as_str().unwrap_or("").into(); return None; }
        "server.tpl.body" => { k.tpl_body = value.as_str().unwrap_or("").into(); return None; }
        _ => {}
    }
    if key == "server.cal.newname" { k.new_cal = value.as_str().unwrap_or("").to_string(); return None; }
    if key == "server.book.newname" { k.new_book = value.as_str().unwrap_or("").to_string(); return None; }
    if let Some(rest) = key.strip_prefix("server.cal.").or(key.strip_prefix("server.book.")) {
        let book = key.starts_with("server.book.");
        let (field, id) = rest.split_once(':')?;
        let id = id.to_string();
        let patch = match field {
            "rename" => { let n = value.as_str().unwrap_or("").trim().to_string(); if n.is_empty() { return None; } json!({"name": n}) }
            "color" => { let i = value.as_i64().unwrap_or(-1); if i < 0 || i as usize >= CAL_COLORS.len() { return None; } json!({"color": CAL_COLORS[i as usize].0}) }
            "visible" => json!({"isVisible": value.as_bool().unwrap_or(true)}),
            _ => return None,
        };
        let list = if book { &mut k.books } else { &mut k.calendars };
        if let Some(x) = list.iter_mut().find(|x| x["id"] == id.as_str()) { for (pk, pv) in patch.as_object().unwrap() { x[pk] = pv.clone(); } }
        return Some(Box::new(move |c: &Client| collection_set(c, book, json!({"update": { id: patch }}))));
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
    if let Some(what) = key.strip_prefix("server.sec.") { return crate::security::button(&mut cache.lock().unwrap().sec, what); }
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
    if key == "server.cal.create" || key == "server.book.create" {
        let book = key == "server.book.create";
        let name = { let k = cache.lock().unwrap(); if book { k.new_book.trim().to_string() } else { k.new_cal.trim().to_string() } };
        if name.is_empty() { return None; }
        return Some(Box::new(move |c: &Client, k: &Shared| {
            let n = if book { k.lock().unwrap().books.len() } else { k.lock().unwrap().calendars.len() };
            let obj = if book { json!({"name": name}) } else { json!({"name": name, "color": CAL_COLORS[n % CAL_COLORS.len()].0, "isVisible": true}) };
            collection_set(c, book, json!({"create": {"n": obj}}))?;
            let mut g = k.lock().unwrap();
            if book { g.new_book.clear() } else { g.new_cal.clear() }
            Ok(())
        }));
    }
    if let Some(rest) = key.strip_prefix("server.cal.").or(key.strip_prefix("server.book.")) {
        let book = key.starts_with("server.book.");
        let (what, id) = rest.split_once(':')?;
        let id = id.to_string();
        match what {
            "default" => return Some(Box::new(move |c: &Client, _| default_collection(c, book, &id))),
            "delete" => {
                let mut k = cache.lock().unwrap();
                if k.confirm_delete != id { k.confirm_delete = id; return Some(Box::new(|_, _| Ok(()))); }
                k.confirm_delete.clear();
                let args = if book { json!({"destroy": [id], "onDestroyRemoveContents": true}) } else { json!({"destroy": [id], "onDestroyRemoveEvents": true}) };
                return Some(Box::new(move |c: &Client, _| collection_set(c, book, args)));
            }
            _ => {}
        }
    }
    if key == "server.rule.add" {
        let k = cache.lock().unwrap();
        if k.nr_field < 5 && k.nr_value.trim().is_empty() { drop(k); cache.lock().unwrap().script_status = "Add a value for the condition.".into(); return Some(Box::new(|_, _| Ok(()))); }
        let field = NR_FIELDS[k.nr_field.clamp(0, 6) as usize];
        let cmp = NR_CMPS[k.nr_cmp.clamp(0, 5) as usize];
        let vals: Vec<String> = k.nr_value.split(',').map(|v| v.trim().to_string()).filter(|v| !v.is_empty()).collect();
        let mut r = crate::sieve::rule(&k.nr_name, field, cmp, "", NR_ACTS[k.nr_action.clamp(0, 6) as usize], &k.nr_action_value, k.nr_stop);
        if field != "attachment" && field != "all" { r["conditions"][0]["value"] = if vals.len() == 1 { json!(vals[0]) } else { json!(vals) }; }
        let period = crate::sieve::day_boundary(&k.nr_from, false).and_then(|f| Ok((f, crate::sieve::day_boundary(&k.nr_until, true)?)));
        let period = match period {
            Ok((Some(f), Some(u))) if u <= f => Err("The period ends before it starts.".to_string()),
            p => p,
        };
        match period {
            Ok((f, u)) => {
                if let Some(f) = f { r["activeFrom"] = json!(f); }
                if let Some(u) = u { r["activeUntil"] = json!(u); }
            }
            Err(e) => { drop(k); cache.lock().unwrap().script_status = e; return Some(Box::new(|_, _| Ok(()))); }
        }
        let mut rules = k.rules.clone();
        rules.push(r);
        let (script, scripts) = (crate::sieve::generate_meta(&crate::sieve::with_rules(&k.filter_meta, &rules), &k.external, &k.sieve_ext), k.scripts.clone());
        drop(k);
        return Some(Box::new(move |c: &Client, k: &Shared| {
            save_script(c, &script, &scripts)?;
            let mut g = k.lock().unwrap();
            g.nr_name.clear(); g.nr_value.clear(); g.nr_action_value.clear(); g.nr_from.clear(); g.nr_until.clear(); g.script_status = "Rule added.".into();
            Ok(())
        }));
    }
    if let Some(id) = key.strip_prefix("server.rule.delete:") {
        let id = id.to_string();
        let mut k = cache.lock().unwrap();
        if k.confirm_delete != id { k.confirm_delete = id; return Some(Box::new(|_, _| Ok(()))); }
        k.confirm_delete.clear();
        let rules: Vec<Value> = k.rules.iter().filter(|r| r["id"] != id.as_str()).cloned().collect();
        let (script, scripts) = (crate::sieve::generate_meta(&crate::sieve::with_rules(&k.filter_meta, &rules), &k.external, &k.sieve_ext), k.scripts.clone());
        return Some(Box::new(move |c: &Client, _| save_script(c, &script, &scripts)));
    }
    if key == "server.tpl.create" {
        let (n, s, b) = { let k = cache.lock().unwrap(); (k.tpl_name.trim().to_string(), k.tpl_subject.clone(), k.tpl_body.clone()) };
        if n.is_empty() { return None; }
        return Some(Box::new(move |c: &Client, k: &Shared| {
            crate::templates::create(c, &n, &s, &b)?;
            let mut g = k.lock().unwrap();
            g.tpl_name.clear(); g.tpl_subject.clear(); g.tpl_body.clear();
            Ok(())
        }));
    }
    if let Some(id) = key.strip_prefix("server.tpl.delete:") {
        let id = id.to_string();
        let mut k = cache.lock().unwrap();
        if k.confirm_delete != id { k.confirm_delete = id; return Some(Box::new(|_, _| Ok(()))); }
        k.confirm_delete.clear();
        return Some(Box::new(move |c: &Client, _| crate::templates::delete(c, &id)));
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

/// Calendar/set or AddressBook/set with the account filled in; the first SetError is the error.
pub fn collection_set(c: &Client, book: bool, mut args: Value) -> Result<(), String> {
    let (method, cap) = if book { ("AddressBook/set", "urn:ietf:params:jmap:contacts") } else { ("Calendar/set", "urn:ietf:params:jmap:calendars") };
    args["accountId"] = json!(c.account_for(cap));
    let r = c.call(json!([[method, args, "s"]]))?;
    if r[0][0] == "error" { return Err(crate::jmap::method_error(&r[0][1])); }
    for k in ["notCreated", "notUpdated", "notDestroyed"] {
        if let Some(e) = r[0][1][k].as_object().and_then(|m| m.values().next()) { return Err(crate::jmap::set_error(e)); }
    }
    Ok(())
}

/// Make a calendar / address book the default. Stalwart sets it through the
/// `onSuccessSetIsDefault` argument (RFC 9553/8984 drafts); older servers take an isDefault patch.
pub fn default_collection(c: &Client, book: bool, id: &str) -> Result<(), String> {
    match collection_set(c, book, json!({"onSuccessSetIsDefault": id})) {
        Ok(()) => Ok(()),
        Err(_) => collection_set(c, book, json!({"update": { id: {"isDefault": true} }})),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn cache_with(quota: Vec<Value>, identities: Vec<Value>) -> Shared {
        let c = Shared::default();
        { let mut k = c.lock().unwrap(); k.quota = quota; k.identities = identities; }
        c
    }

    #[test]
    fn account_page_shows_storage_usage_even_without_identities() {
        let q = vec![
            json!({"id": "1", "resourceType": "octets", "scope": "account", "types": ["Email"], "used": 1024 * 1024, "hardLimit": 4 * 1024 * 1024}),
            json!({"id": "2", "resourceType": "count", "scope": "account", "types": ["ContactCard"], "used": 3, "hardLimit": 10}),
        ];
        let rows = rows("account", &cache_with(q, vec![])).expect("the account page has rows");
        assert_eq!(rows.len(), 2);
        assert_eq!(rows[0].kind.as_str(), "meter");
        assert_eq!(rows[0].label.as_str(), "Storage usage");
        assert_eq!(rows[0].desc.as_str(), "1 MB of 4 MB used");
        assert_eq!(rows[0].text.as_str(), "25% used");
        assert_eq!(rows[0].index, 25);
        assert_eq!(rows[1].label.as_str(), "Item count (ContactCard)");
        assert_eq!(rows[1].index, 30);
    }

    #[test]
    fn no_quota_adds_nothing_and_other_pages_never_show_it() {
        assert!(rows("account", &cache_with(vec![], vec![])).is_none());
        let q = vec![json!({"resourceType": "octets", "used": 1, "hardLimit": 2})];
        assert!(rows("composing", &cache_with(q.clone(), vec![])).is_none());
        // with identities the storage lines come first, then the identities
        let ids = vec![json!({"id": "i1", "email": "ann@example.test", "name": "Ann"})];
        let r = rows("account", &cache_with(q, ids)).unwrap();
        assert_eq!(r[0].kind.as_str(), "meter");
        assert!(r.iter().any(|x| x.label.as_str() == "ann@example.test"));
    }
}
