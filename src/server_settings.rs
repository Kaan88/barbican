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
    /// The account the pages are for (`Client::state_key`), set as the signed-in account changes
    /// (see [`set_account`]).
    pub account: String,
    /// Whose out-of-office data the `vac*` / `fwd*` fields hold ("" none yet).
    pub vac_account: String,
    /// Bumped whenever the out-of-office fields are dropped, so a load or save that started before
    /// writes nothing back.
    pub vac_epoch: u64,
    pub vacation: Option<Value>,
    /// What the filters script holds for the out-of-office page (audience, forwarding).
    pub vac_filters: Option<crate::vacation::Filters>,
    /// The account's own domains (its identities), for "internal" / "external" senders.
    pub vac_domains: Vec<String>,
    pub fwd_enabled: bool,
    pub fwd_to: String,
    pub fwd_keep: bool,
    /// The reply as the server has it (the page's "on" includes the filters script's include).
    pub vac_saved: Option<crate::vacation::Reply>,
    /// What was typed last: 1 the plain text, 2 the HTML, 0 neither.
    pub vac_src: u8,
    /// The date fields as typed (shown as typed, saved once both read as dates).
    pub vac_from_txt: Option<String>,
    pub vac_to_txt: Option<String>,
    /// The typed date is not saved yet (not a date, or the end is not after the start). A saved
    /// one keeps its text as typed, so the field is never rewritten while the user types in it.
    pub vac_from_pending: bool,
    pub vac_to_pending: bool,
    pub vac_reply_dirty: bool,
    pub vac_forward_dirty: bool,
    pub vac_audience_dirty: Option<Option<Value>>,
    /// The filters script still has to follow the reply (its last write failed).
    pub vac_sync_dirty: bool,
    /// The forwarding address was typed and not stored yet (a save with forwarding off and
    /// nothing stored keeps it in the field only, as Bulwark does).
    pub fwd_typed: bool,
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
    /// Id of the template the form edits ("" while it makes a new one).
    pub tpl_edit: String,
    pub tpl_category: String,
    pub tpl_to: String,
    pub tpl_cc: String,
    pub tpl_bcc: String,
    pub tpl_html: bool,
    pub tpl_fav: bool,
    pub tpl_error: String,
    /// Whether the server keeps metadata: 0 not known yet (not loaded, or the load failed),
    /// 1 it does, 2 it does not.
    pub tpl_state: u8,
    /// Bumped whenever the form is cleared, switched to another template or typed in, so a save
    /// that finishes later clears only the form it was started from.
    pub tpl_gen: u64,
    /// The template the form edits, as it was loaded (its `updatedAt` is what a save checks).
    pub tpl_old: Option<Value>,
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
}

/// Bulwark's calendar colour presets (lib/shared-calendar-colors.ts CALENDAR_COLORS).
pub const CAL_COLORS: [(&str, &str); 14] = [
    ("#3b82f6", "Blue"), ("#ef4444", "Red"), ("#22c55e", "Green"), ("#f59e0b", "Amber"), ("#8b5cf6", "Violet"), ("#ec4899", "Pink"), ("#14b8a6", "Teal"),
    ("#f97316", "Orange"), ("#06b6d4", "Cyan"), ("#84cc16", "Lime"), ("#6366f1", "Indigo"), ("#a855f7", "Purple"), ("#e11d48", "Rose"), ("#0ea5e9", "Sky"),
];

fn form_fields(k: &Cache) -> crate::templates::Fields {
    crate::templates::Fields {
        name: k.tpl_name.clone(), subject: k.tpl_subject.clone(), body: k.tpl_body.clone(), html: k.tpl_html, category: k.tpl_category.clone(),
        to: crate::templates::split_addrs(&k.tpl_to), cc: crate::templates::split_addrs(&k.tpl_cc), bcc: crate::templates::split_addrs(&k.tpl_bcc), favorite: k.tpl_fav,
    }
}

fn clear_tpl_form(k: &mut Cache) {
    k.tpl_old = None;
    k.tpl_gen += 1;
    k.tpl_edit.clear(); k.tpl_name.clear(); k.tpl_subject.clear(); k.tpl_body.clear(); k.tpl_category.clear();
    k.tpl_to.clear(); k.tpl_cc.clear(); k.tpl_bcc.clear(); k.tpl_html = false; k.tpl_fav = false; k.tpl_error.clear();
}

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

/// The account the pages are for from now on (`Client::state_key`, "" when signed out). Another
/// account drops the out-of-office data held for the one before, so none of it is shown, edited
/// or written to the new one.
pub fn set_account(cache: &Shared, key: &str) {
    let mut k = cache.lock().unwrap();
    if k.account == key { return; }
    k.account = key.to_string();
    clear_vacation(&mut k);
}

fn clear_vacation(k: &mut Cache) {
    k.vac_epoch += 1;
    k.vac_account.clear();
    k.vacation = None;
    k.vac_filters = None;
    k.vac_domains.clear();
    (k.fwd_enabled, k.fwd_keep) = (false, false);
    k.fwd_to.clear();
    k.vac_saved = None;
    k.vac_src = 0;
    (k.vac_from_txt, k.vac_to_txt, k.vac_from_pending, k.vac_to_pending) = (None, None, false, false);
    (k.vac_reply_dirty, k.vac_forward_dirty, k.vac_audience_dirty) = (false, false, None);
    (k.vac_sync_dirty, k.fwd_typed) = (false, false);
}

/// The out-of-office fields are loaded and belong to the account the pages are for.
fn vac_current(k: &Cache) -> bool { k.vacation.is_some() && !k.vac_account.is_empty() && k.vac_account == k.account }

fn vac_dirty(k: &Cache) -> bool { k.vac_reply_dirty || k.vac_forward_dirty || k.vac_audience_dirty.is_some() || k.vac_sync_dirty }

/// Fetch what a page needs. Runs on a background thread.
pub fn load(tab: &str, c: &Client, cache: &Shared) {
    let key = c.state_key();
    let epoch = cache.lock().unwrap().vac_epoch;
    let res: Result<(), String> = (|| {
        match tab {
            // with the account's out-of-office writes held: a write taken from the page (its waiting
            // flags cleared) and not yet on the server would otherwise be overwritten by what the
            // server had before it, and sent back stale with the next edit
            "vacation" => crate::vacation::serialized(c, || -> Result<(), String> {
                let acc = c.account_for("urn:ietf:params:jmap:vacationresponse");
                let r = c.call(json!([["VacationResponse/get", {"accountId": acc, "ids": ["singleton"]}, "v"]]))?;
                let mut vac = r[0][1]["list"][0].clone();
                let emails: Vec<String> = c.identities().unwrap_or_default().iter().filter_map(|i| i["email"].as_str().map(String::from)).collect();
                let domains = crate::vacation::own_domains(&emails);
                let filters = crate::vacation::load_filters(c);
                // running from the filters script leaves the server's own script inactive, so the server reports "off"
                let on = vac["isEnabled"] == true || filters.as_ref().is_some_and(|f| f.includes_vacation);
                vac["isEnabled"] = json!(on);
                {
                    let mut k = cache.lock().unwrap();
                    // the pages moved on to another account (or signed out) meanwhile: not its data
                    if k.account != key || k.vac_epoch != epoch { return Ok(()); }
                    let ours = k.vac_account == key && k.vacation.is_some();
                    // what is typed and not written yet stays (a write still waiting, or one that failed)
                    let mut filters = filters;
                    if !(ours && k.vac_forward_dirty) {
                        k.vac_forward_dirty = false;
                        let f = filters.as_ref().and_then(|f| f.forward.as_ref());
                        k.fwd_enabled = f.is_some_and(|f| f["enabled"] == true);
                        k.fwd_keep = f.is_some_and(|f| f["keepCopy"] == true);
                        // an address typed while nothing is stored stays in the field (Bulwark keeps it
                        // too); once the server has one, the field shows that
                        if !(ours && k.fwd_typed && f.is_none()) {
                            k.fwd_typed = false;
                            k.fwd_to = f.and_then(|f| f["to"].as_str()).unwrap_or("").to_string();
                        }
                    }
                    if !ours { (k.vac_audience_dirty, k.vac_sync_dirty) = (None, false); }
                    if let (Some(a), Some(f)) = (k.vac_audience_dirty.clone(), filters.as_mut()) { f.audience = a; }
                    k.vac_filters = filters;
                    k.vac_domains = domains;
                    k.vac_saved = Some(reply_of(&vac, on));
                    if !(ours && k.vac_reply_dirty) {
                        k.vac_reply_dirty = false;
                        k.vac_src = 0;
                        k.vacation = Some(vac.clone());
                    }
                    // the date fields keep what was typed that is not a date (or not saved) yet
                    // a saved date shows as the server has it now; one not saved yet stays as typed
                    if !(ours && k.vac_from_pending) { (k.vac_from_txt, k.vac_from_pending) = (None, false); }
                    if !(ours && k.vac_to_pending) { (k.vac_to_txt, k.vac_to_pending) = (None, false); }
                    k.vac_account = key.clone();
                }
                // the reply may have been set in another client: bring the calendar block in line, once per run
                if first_load(&c.key_for(&acc)) {
                    let _ = crate::vacation::sync_block(c, &reply_of(&vac, on), crate::vacation::Trigger::Load, None);
                }
                Ok(())
            })?,
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
            "composing" => {
                let ids = c.identities()?;
                // a failed load is an error on the page, not an empty list
                let tpls = crate::templates::list(c)?;
                let mut k = cache.lock().unwrap();
                k.identities = ids;
                k.templates = tpls;
                k.tpl_state = if crate::templates::supported(c) { 1 } else { 2 };
            }
            _ => {}
        }
        Ok(())
    })();
    // a load for an account the pages are no longer for leaves their error line alone
    let mut k = cache.lock().unwrap();
    if k.account == key { k.error = res.err().unwrap_or_default(); }
}

/// Rows for server-backed pages; `None` for pages this module doesn't own. `prefs` are the
/// settings the caller holds: nothing here takes their lock again.
pub fn rows(tab: &str, cache: &Shared, prefs: &crate::settings::Settings) -> Option<Vec<SRow>> {
    let k = cache.lock().unwrap();
    let mut out = vec![];
    if !k.error.is_empty() { out.push(row("value", "", "Could not load from the server", &k.error, "", false)); }
    let quota = if tab == "account" { quota_rows(&k.quota) } else { vec![] };
    match tab {
        "vacation" => return Some(vacation_rows(&k, out, crate::vacation::block_setting_in(prefs))),
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
                if k.tpl_state == 2 {
                    out.push(row("value", "", "This server keeps no metadata", "Templates need it (Stalwart 1.0 or later has it), so none can be stored or shown here.", "", false));
                }
                for t in &k.templates {
                    let id = t["id"].as_str().unwrap_or("");
                    let mut desc = t["subject"].as_str().unwrap_or("").to_string();
                    if let Some(c) = t["category"].as_str().filter(|c| !c.is_empty()) { desc = if desc.is_empty() { c.into() } else { format!("{c} · {desc}") }; }
                    if t["isHTML"] == true { desc = if desc.is_empty() { "Rich text".into() } else { format!("{desc} · rich text") }; }
                    out.push(row("button", &format!("server.tpl.edit:{id}"), t["name"].as_str().unwrap_or("(unnamed)"), &desc, if k.tpl_edit == id { "Editing" } else if k.confirm_delete == format!("edit:{id}") { "Click again to discard what you typed" } else { "Edit" }, false));
                    out.push(row("button", &format!("server.tpl.delete:{id}"), "", "", if k.confirm_delete == id { "Click again to delete" } else { "Delete" }, false));
                }
                let editing = !k.tpl_edit.is_empty();
                out.push(row("section", "", if editing { "Edit template" } else { "New template" }, "", "", false));
                out.push(row("text", "server.tpl.name", "Name", "", &k.tpl_name, false));
                out.push(row("text", "server.tpl.category", "Category", "Optional", &k.tpl_category, false));
                out.push(row("text", "server.tpl.subject", "Subject", "", &k.tpl_subject, false));
                out.push(row("toggle", "server.tpl.html", "Rich text (HTML) body", "Off: plain text. On: the body below is HTML; scripts and the like are removed when it is saved.", "", k.tpl_html));
                out.push(row("textarea", "server.tpl.body", if k.tpl_html { "Body (HTML)" } else { "Body" },
                    "Placeholders: {{recipient_name}}, {{company}}, {{date}}, {{short_date}}, {{day}}, {{month}}, {{year}}, {{day_of_week}}, {{sender_name}}, or your own; {{name:default}} gives a default.", &k.tpl_body, false));
                out.push(row("text", "server.tpl.to", "To", "Comma separated; put in new messages", &k.tpl_to, false));
                out.push(row("text", "server.tpl.cc", "Cc", "", &k.tpl_cc, false));
                out.push(row("text", "server.tpl.bcc", "Bcc", "", &k.tpl_bcc, false));
                out.push(row("toggle", "server.tpl.fav", "Favourite", "", "", k.tpl_fav));
                if !k.tpl_error.is_empty() { out.push(row("value", "", "Not saved", &k.tpl_error, "", false)); }
                out.push(row("button", "server.tpl.create", "", "", if editing { "Save changes" } else { "Save template" }, false));
                if editing { out.push(row("button", "server.tpl.cancel", "", "", "Cancel", false)); }
            }
            return Some(out);
        }
        _ => return None,
    }
    Some(out)
}

/// Apply an edit locally (instant) and return a server write to run in the background.
pub fn edit(key: &str, value: &Value, cache: &Shared) -> Option<Box<dyn FnOnce(&Client) -> Result<(), String> + Send>> {
    // a device setting, through the live settings: before the cache's lock, never under it
    if key == "server.vacation.block" { crate::settings::set_pref("vacationBlockCalendar", json!(value.as_bool().unwrap_or(true))); }
    let mut k = cache.lock().unwrap();
    if let Some(field) = key.strip_prefix("server.vacation.") { return edit_vacation(field, value, &mut k, cache.clone()); }
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
    if key.starts_with("server.tpl.") { k.tpl_gen += 1; }
    match key {
        "server.tpl.name" => { k.tpl_name = value.as_str().unwrap_or("").into(); return None; }
        "server.tpl.subject" => { k.tpl_subject = value.as_str().unwrap_or("").into(); return None; }
        "server.tpl.body" => { k.tpl_body = value.as_str().unwrap_or("").into(); return None; }
        "server.tpl.category" => { k.tpl_category = value.as_str().unwrap_or("").into(); return None; }
        "server.tpl.to" => { k.tpl_to = value.as_str().unwrap_or("").into(); return None; }
        "server.tpl.cc" => { k.tpl_cc = value.as_str().unwrap_or("").into(); return None; }
        "server.tpl.bcc" => { k.tpl_bcc = value.as_str().unwrap_or("").into(); return None; }
        "server.tpl.html" => {
            // the body changes format with the switch (Bulwark template-form.tsx toggleHTML)
            let on = value.as_bool().unwrap_or(false);
            if on != k.tpl_html {
                k.tpl_body = if on { crate::rte::html::export(&crate::rte::model::Doc { blocks: crate::rte::html::import_text(&k.tpl_body) }) } else { crate::rte::html::html_text(&k.tpl_body) };
            }
            k.tpl_html = on;
            return None;
        }
        "server.tpl.fav" => { k.tpl_fav = value.as_bool().unwrap_or(false); return None; }
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
    if key == "server.vacation.retry" {
        // the filters part is written again (with whatever else waits)
        { let mut k = cache.lock().unwrap(); if vac_current(&k) { k.vac_sync_dirty = true; } }
        return Some(Box::new(|c: &Client, k: &Shared| vacation_run(c, k)));
    }
    if key == "server.vacation.forwardSave" {
        let edit = apply_edit("forwardSave", &Value::Null, &mut cache.lock().unwrap());
        return match edit {
            // what else is waiting still goes out
            Edit::Refuse(m) => Some(Box::new(move |c: &Client, k: &Shared| refused(c, k, m))),
            Edit::Save => Some(Box::new(|c: &Client, k: &Shared| vacation_run(c, k))),
            Edit::Local => None,
        };
    }
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
    if key == "server.tpl.cancel" {
        clear_tpl_form(&mut cache.lock().unwrap());
        return Some(Box::new(|_, _| Ok(())));
    }
    if let Some(id) = key.strip_prefix("server.tpl.edit:") {
        let mut k = cache.lock().unwrap();
        if k.tpl_edit == id { return Some(Box::new(|_, _| Ok(()))); }
        // what is half typed is not thrown away by one click
        let dirty = format!("{:?}", form_fields(&k)) != format!("{:?}", k.tpl_old.as_ref().map(crate::templates::fields_of).unwrap_or_default());
        let ask = format!("edit:{id}");
        if dirty && k.confirm_delete != ask { k.confirm_delete = ask; return Some(Box::new(|_, _| Ok(()))); }
        k.confirm_delete.clear();
        if let Some(t) = k.templates.iter().find(|t| t["id"] == id).cloned() {
            let f = crate::templates::fields_of(&t);
            k.tpl_edit = id.to_string();
            k.tpl_gen += 1;
            (k.tpl_name, k.tpl_subject, k.tpl_body, k.tpl_category, k.tpl_html, k.tpl_fav) = (f.name, f.subject, f.body, f.category, f.html, f.favorite);
            (k.tpl_to, k.tpl_cc, k.tpl_bcc) = (f.to.join(", "), f.cc.join(", "), f.bcc.join(", "));
            k.tpl_error.clear();
            k.tpl_old = Some(t);
        }
        return Some(Box::new(|_, _| Ok(())));
    }
    if key == "server.tpl.create" {
        let (old, f, generation) = {
            let k = cache.lock().unwrap();
            if !k.tpl_edit.is_empty() && k.tpl_old.is_none() { return Some(Box::new(|_, k: &Shared| { let e = "This template is no longer in the list; reload and try again.".to_string(); k.lock().unwrap().tpl_error = e.clone(); Err(e) })); }
            (k.tpl_old.clone().filter(|_| !k.tpl_edit.is_empty()), form_fields(&k), k.tpl_gen)
        };
        return Some(Box::new(move |c: &Client, k: &Shared| {
            match crate::templates::save(c, old.as_ref(), &f) {
                // only when the form still holds what was saved (the user may have moved to another template)
                Ok(()) => { let mut k = k.lock().unwrap(); if k.tpl_gen == generation { clear_tpl_form(&mut k); } Ok(()) }
                Err(e) => { k.lock().unwrap().tpl_error = e.clone(); Err(e) }
            }
        }));
    }
    if let Some(id) = key.strip_prefix("server.tpl.delete:") {
        let id = id.to_string();
        let mut k = cache.lock().unwrap();
        if k.confirm_delete != id { k.confirm_delete = id; return Some(Box::new(|_, _| Ok(()))); }
        k.confirm_delete.clear();
        return Some(Box::new(move |c: &Client, k: &Shared| {
            crate::templates::delete(c, &id)?;
            // the form was editing the template just deleted
            let mut k = k.lock().unwrap();
            if k.tpl_edit == id { clear_tpl_form(&mut k); }
            Ok(())
        }));
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

type Job = Box<dyn FnOnce(&Client) -> Result<(), String> + Send>;

/// The calendar block is brought in line once per run and account when the page is first loaded.
/// `account` is `Client::key_for` (server, login, account id): short ids repeat across servers.
fn first_load(account: &str) -> bool {
    static SEEN: std::sync::OnceLock<Mutex<std::collections::HashSet<String>>> = std::sync::OnceLock::new();
    SEEN.get_or_init(Default::default).lock().unwrap().insert(account.to_string())
}

fn vacation_rows(k: &Cache, mut out: Vec<SRow>, block_on: bool) -> Vec<SRow> {
    use crate::vacation as vac;
    out.push(row("section", "", "Out of office", "Sends an automatic reply to people who write to you while you are away. Shared with Bulwark.", "", false));
    // another account's reply is never shown (an edit would write it to this one)
    let Some(v) = k.vacation.as_ref().filter(|_| vac_current(k)) else { out.push(row("value", "", "Loading…", "", "", false)); return out };
    let f = k.vac_filters.clone().unwrap_or_default();
    let tz = vac::user_tz();
    out.push(row("toggle", "server.vacation.isEnabled", "Automatic reply", "On while the dates below apply (or always, without dates).", "", v["isEnabled"] == true));
    if v["isEnabled"] != true && f.includes_vacation {
        // switched off here, but the filters script still includes the server's vacation script
        out.push(row("button", "server.vacation.retry", "Still running", "The reply is off here, but the filters script still sends it: switching it off there did not go through. It is tried again with your next change.", "Try again", false));
    }
    if f.not_running {
        out.push(row("value", "", "Not running yet", "Forwarding or reply recipients are stored, but the server's own vacation script took over. Switch the reply off and on to set it right.", "", false));
    }
    if f.audience_available && !k.vac_domains.is_empty() {
        let opts = ["Everyone".to_string(), format!("Only people at {}", k.vac_domains.join(", ")), "Only people outside those domains".to_string()];
        let opts: Vec<&str> = opts.iter().map(String::as_str).collect();
        out.push(sel("server.vacation.audience", "Reply to", &opts, vac::audience_choice(f.audience.as_ref())));
    }
    let day = |key: &str, typed: &Option<String>| typed.clone().unwrap_or_else(|| v[key].as_str().map(|d| vac::show_moment(d, tz)).unwrap_or_default());
    let note = |pending: bool, hint: &str| if pending { "Not saved yet: the end must be after the start, and both must be dates.".to_string() } else { hint.to_string() };
    out.push(row("text", "server.vacation.fromDate", "From (optional)", &note(k.vac_from_pending, "YYYY-MM-DD, or YYYY-MM-DD HH:MM; empty for no start. Your time zone."), &day("fromDate", &k.vac_from_txt), false));
    out.push(row("text", "server.vacation.toDate", "Until (optional)", &note(k.vac_to_pending, "YYYY-MM-DD, or YYYY-MM-DD HH:MM; empty for no end."), &day("toDate", &k.vac_to_txt), false));
    if v["fromDate"].is_string() && v["toDate"].is_string() {
        out.push(row("toggle", "server.vacation.block", "Block my calendar", "Keeps a busy event in your default calendar for these dates (shared with Bulwark).", "", block_on));
    }
    out.push(row("section", "", "Message", "", "", false));
    out.push(row("text", "server.vacation.subject", "Subject", "Leave empty to reuse the original subject.", v["subject"].as_str().unwrap_or(""), false));
    out.push(row("textarea", "server.vacation.textBody", "Message", "Plain text. Editing it replaces the formatted version.", v["textBody"].as_str().unwrap_or(""), false));
    out.push(row("textarea", "server.vacation.htmlBody", "Message (HTML)", "The formatted version recipients see. Only basic formatting and http, https and mailto links are kept; the plain text is made from it.", v["htmlBody"].as_str().unwrap_or(""), false));
    if f.forward_available {
        out.push(row("section", "", "Forward while away", "Sends a copy of incoming mail to another address while the reply is on.", "", false));
        out.push(row("toggle", "server.vacation.forwardEnabled", "Forward messages", "", "", k.fwd_enabled));
        // the field differs from what the server has: say so
        let stored = f.forward.as_ref();
        let hint = if k.fwd_to == stored.and_then(|x| x["to"].as_str()).unwrap_or("") { "Type the address, then press Save forwarding." }
            else if stored.is_none() && !k.fwd_enabled { "Not stored yet: it is saved when you switch forwarding on." }
            else { "Not saved yet: press Save forwarding." };
        out.push(row("text", "server.vacation.forwardTo", "Forward to", hint, &k.fwd_to, false));
        out.push(row("button", "server.vacation.forwardSave", "", "", "Save forwarding", false));
        if k.fwd_enabled {
            out.push(row("toggle", "server.vacation.forwardKeep", "Keep in inbox", "Keep a copy here as well as forwarding it.", "", k.fwd_keep));
        }
    }
    out
}

/// The reply as the calendar block reads it.
fn reply_of(v: &Value, on: bool) -> crate::vacation::Reply {
    crate::vacation::Reply { is_enabled: on, from: v["fromDate"].as_str().map(String::from), to: v["toDate"].as_str().map(String::from), subject: v["subject"].as_str().unwrap_or("").into() }
}

/// A forwarding address that can be saved: a valid address whose domain has a dot.
pub fn forward_usable(to: &str) -> bool {
    crate::sieve::valid_forward(&json!({"enabled": true, "to": to, "keepCopy": false})) && to.rsplit_once('@').is_some_and(|(_, d)| d.contains('.'))
}

/// What the stored message parts are: `(textBody, htmlBody)` as a save sends them. `src` is what
/// was typed last (1 the plain text, 2 the HTML, 0 neither).
fn body_parts(cur: &Value, src: u8) -> (String, Option<String>) {
    use crate::vacation as vac;
    let (text, html) = (cur["textBody"].as_str().unwrap_or(""), cur["htmlBody"].as_str().filter(|h| !h.trim().is_empty()));
    match src {
        1 => vac::parts_from_text(text),
        2 => match html { Some(h) => { let (t, h) = vac::parts_from_html(h); (t, h) } None => (text.to_string(), None) },
        _ => (text.to_string(), html.map(String::from)),
    }
}

/// The result of an edit on the page.
#[derive(Debug, PartialEq)]
pub enum Edit {
    /// Kept in the cache only (typing that is not complete, or waiting for a Save).
    Local,
    /// Nothing is stored and the page goes back to what the server has.
    Refuse(String),
    /// The cache changed: write it.
    Save,
}

/// Apply one edit of the out-of-office page to the cache. Nothing here talks to the server: the
/// cache holds everything typed, and one write sends all of it (see [`take_run_plan`]).
pub fn apply_edit(field: &str, value: &Value, k: &mut Cache) -> Edit { apply_edit_in(field, value, k, crate::vacation::user_tz()) }

/// [`apply_edit`] with the dates read in `tz`.
fn apply_edit_in(field: &str, value: &Value, k: &mut Cache, tz: chrono_tz::Tz) -> Edit {
    use crate::vacation as vac;
    // not loaded, or another account's: nothing to edit
    if !vac_current(k) { return Edit::Local; }
    let Some(mut cur) = k.vacation.clone() else { return Edit::Local };
    let shown_on = cur["isEnabled"] == true;
    let text = value.as_str().unwrap_or("").to_string();
    let mut src = k.vac_src;
    match field {
        "isEnabled" => {
            let on = value.as_bool().unwrap_or(false);
            let (t, h) = body_parts(&cur, src);
            if let Err(e) = vac::check_message(on, cur["subject"].as_str().unwrap_or(""), &t, h.as_deref()) { return Edit::Refuse(e); }
            cur["isEnabled"] = json!(on);
        }
        "subject" => {
            if let Err(e) = vac::check_message(false, &text, "", None) { return Edit::Refuse(e); }
            cur["subject"] = json!(text);
        }
        "textBody" => {
            let (t, h) = vac::parts_from_text(&text);
            if let Err(e) = vac::check_message(shown_on, "", &t, h.as_deref()) { return Edit::Refuse(e); }
            cur["textBody"] = json!(text);
            cur["htmlBody"] = json!(h);
            src = 1;
        }
        "htmlBody" => {
            if text.trim().is_empty() {
                // the plain text alone is the message now: no HTML made from it again
                cur["htmlBody"] = Value::Null;
                src = 0;
            } else {
                let (t, h) = vac::parts_from_html(&text);
                if let Err(e) = vac::check_message(shown_on, "", &t, h.as_deref()) { return Edit::Refuse(e); }
                cur["textBody"] = json!(t);
                cur["htmlBody"] = json!(text);
                src = 2;
            }
        }
        "fromDate" | "toDate" => {
            let other_typed = if field == "fromDate" { k.vac_to_pending } else { k.vac_from_pending };
            if field == "fromDate" { (k.vac_from_txt, k.vac_from_pending) = (Some(text), true); } else { (k.vac_to_txt, k.vac_to_pending) = (Some(text), true); }
            // a field that was not typed in keeps its stored instant: its text can name two moments
            // (02:30 twice when the clocks go back), and reading it again could move it by an hour
            let moment = |typed: &Option<String>, key: &str| match (typed, cur[key].as_str()) {
                (Some(t), _) => vac::parse_moment_dt(t, tz),
                (None, None) => Ok(None),
                (None, Some(d)) => vac::moment_instant(d).map(|x| Ok(Some(x))).unwrap_or_else(|| vac::parse_moment_dt(&vac::show_moment(d, tz), tz)),
            };
            // text that is not a date yet stays in the field and is not saved
            let pending = |t: &Option<String>, p: bool| if p { t.clone() } else { None };
            let (Ok(f), Ok(t)) = (moment(&pending(&k.vac_from_txt, k.vac_from_pending), "fromDate"), moment(&pending(&k.vac_to_txt, k.vac_to_pending), "toDate")) else { return Edit::Local };
            if let (Some(f), Some(t)) = (f, t) {
                if t <= f {
                    // both typed: refused, and the page goes back to what is stored. Only this one typed:
                    // it waits (marked "not saved yet") until the other date is set to match
                    if other_typed {
                        (k.vac_from_txt, k.vac_to_txt, k.vac_from_pending, k.vac_to_pending) = (None, None, false, false);
                        return Edit::Refuse("The end must be after the start.".into());
                    }
                    return Edit::Local;
                }
            }
            let iso = |d: Option<chrono::DateTime<chrono::Utc>>| d.map(|d| json!(d.format("%Y-%m-%dT%H:%M:%S.000Z").to_string())).unwrap_or(Value::Null);
            cur["fromDate"] = iso(f);
            cur["toDate"] = iso(t);
            // saved: the fields show the stored dates again, and the next edit is judged on its own
            // the texts stay as typed (the field is not rewritten under the caret: "2026-10-05 " is
            // saved as the day, and typing goes on to the time)
            (k.vac_from_pending, k.vac_to_pending) = (false, false);
        }
        "audience" => {
            if !k.vac_filters.as_ref().is_some_and(|f| f.audience_available) || k.vac_domains.is_empty() { return Edit::Local; }
            let a = vac::audience_for(value.as_i64().unwrap_or(0) as i32, &k.vac_domains);
            if let Some(f) = k.vac_filters.as_mut() { f.audience = a.clone(); }
            k.vac_audience_dirty = Some(a);
            return Edit::Save;
        }
        "forwardEnabled" => {
            let on = value.as_bool().unwrap_or(false);
            if on && !forward_usable(&k.fwd_to) { return Edit::Refuse("Enter the address to forward to first.".into()); }
            k.fwd_enabled = on;
            k.vac_forward_dirty = true;
            return Edit::Save;
        }
        "forwardKeep" => {
            k.fwd_keep = value.as_bool().unwrap_or(false);
            k.vac_forward_dirty = true;
            return Edit::Save;
        }
        // the address is committed by "Save forwarding" (or by switching forwarding on), never per keystroke
        "forwardTo" => { k.fwd_to = text.trim().to_string(); k.fwd_typed = true; return Edit::Local; }
        "forwardSave" => {
            if !forward_usable(&k.fwd_to) { return Edit::Refuse("That is not an address mail can be forwarded to.".into()); }
            k.vac_forward_dirty = true;
            return Edit::Save;
        }
        _ => return Edit::Local,
    }
    k.vacation = Some(cur);
    k.vac_src = src;
    k.vac_reply_dirty = true;
    Edit::Save
}

/// Everything a write sends, taken from the cache when the write runs.
#[derive(Debug)]
pub struct RunPlan {
    pub reply_dirty: bool,
    /// The forwarding could not go out (no usable address): it waits, the rest is written.
    pub refused: Option<String>,
    pub patch: serde_json::Map<String, Value>,
    pub saved: crate::vacation::Reply,
    pub after: crate::vacation::Reply,
    pub forward: Option<Option<Value>>,
    pub audience: Option<Option<Value>>,
    pub touches_filters: bool,
    pub touches_block: bool,
}

/// The one write for all that is waiting: the whole reply as typed (so a write that replaced an
/// earlier timer loses nothing), the filters part, and whether the calendar block follows.
/// A plan that is refused leaves everything waiting (the next edit tries again).
pub fn take_run_plan(k: &mut Cache) -> Result<Option<RunPlan>, String> {
    use crate::vacation as vac;
    if !vac_dirty(k) { return Ok(None); }
    let (reply_dirty, forward_dirty, sync_dirty) = (k.vac_reply_dirty, k.vac_forward_dirty, k.vac_sync_dirty);
    let cur = k.vacation.clone().ok_or("The page is not loaded.")?;
    let on = cur["isEnabled"] == true;
    let saved = k.vac_saved.clone().unwrap_or_else(|| reply_of(&cur, false));
    let (text, html) = body_parts(&cur, k.vac_src);
    let subject = cur["subject"].as_str().unwrap_or("");
    vac::check_message(on, subject, &text, html.as_deref())?;
    let after = reply_of(&cur, on);
    let dates_changed = !vac::same_opt_moment(saved.from.as_deref(), after.from.as_deref()) || !vac::same_opt_moment(saved.to.as_deref(), after.to.as_deref());
    let mut patch = serde_json::Map::new();
    if reply_dirty {
        if on != saved.is_enabled { patch.insert("isEnabled".into(), json!(on)); }
        patch.insert("subject".into(), json!(subject));
        patch.insert("textBody".into(), json!(text));
        patch.insert("htmlBody".into(), json!(html));
        patch.insert("fromDate".into(), cur["fromDate"].clone());
        patch.insert("toDate".into(), cur["toDate"].clone());
    }
    // forwarding as a save stores it: an address that is off is kept for next time if it is usable
    let stored_forward = k.vac_filters.as_ref().is_some_and(|f| f.forward.is_some());
    let mut refused = None;
    let forward = if !forward_dirty { None } else if k.fwd_enabled || (forward_usable(&k.fwd_to) && stored_forward) {
        if forward_usable(&k.fwd_to) { Some(Some(json!({"enabled": k.fwd_enabled, "to": k.fwd_to, "keepCopy": k.fwd_keep}))) }
        else { refused = Some("Enter the address to forward to first.".to_string()); None }
    } else { Some(None) };
    // a half-typed address holds back the forwarding only, never the reply or the rest
    if let Some(m) = &refused {
        if !reply_dirty && !sync_dirty && k.vac_audience_dirty.is_none() { return Err(m.clone()); }
    }
    // every check passed: what is sent is no longer waiting
    let audience = k.vac_audience_dirty.take();
    (k.vac_reply_dirty, k.vac_forward_dirty, k.vac_sync_dirty) = (false, refused.is_some(), false);
    // an address that goes to the server is stored from now on; one a save drops stays typed
    if matches!(forward, Some(Some(_))) { k.fwd_typed = false; }
    let enabled_changed = on != saved.is_enabled;
    Ok(Some(RunPlan {
        reply_dirty,
        refused,
        touches_filters: (reply_dirty && (enabled_changed || dates_changed)) || forward.is_some() || audience.is_some() || sync_dirty,
        touches_block: reply_dirty && (enabled_changed || dates_changed || after.subject != saved.subject),
        patch, saved, after, forward, audience,
    }))
}

/// The out-of-office data is `key`'s and still the one a job started from.
fn same_vacation(k: &Cache, key: &str, epoch: u64) -> bool { k.vac_epoch == epoch && vac_current(k) && k.vac_account == key }

pub const OTHER_ACCOUNT: &str = "The account changed before the out-of-office change was written; nothing was sent.";

/// Write what is waiting in the cache: the reply, its filters part, the calendar block. Only to
/// the account the waiting data is of.
pub fn vacation_run(c: &Client, shared: &Shared) -> Result<(), String> {
    use crate::vacation as vac;
    vac::serialized(c, || {
        let key = c.state_key();
        let (plan, epoch) = {
            let mut k = shared.lock().unwrap();
            let epoch = k.vac_epoch;
            if !same_vacation(&k, &key, epoch) {
                // the page is still loading for `c`: nothing typed yet. Otherwise it holds another
                // account's data, and nothing of it goes to `c`
                return if k.account == key && k.vacation.is_none() { Ok(()) } else { Err(OTHER_ACCOUNT.into()) };
            }
            // a refused plan leaves what is typed waiting, and the page as it is
            match take_run_plan(&mut k) { Ok(Some(p)) => (p, epoch), Ok(None) => return Ok(()), Err(e) => return Err(e) }
        };
        if !plan.patch.is_empty() {
            let acc = c.account_for("urn:ietf:params:jmap:vacationresponse");
            let r = c.update("VacationResponse/set", &acc, vec![("singleton".into(), Value::Object(plan.patch.clone()))]);
            if let Err(e) = r.and_then(|(_, bad)| if bad.is_empty() { Ok(()) } else { Err(bad.join("; ")) }) {
                // nothing was written: all of it waits for the next try, and stays on the page
                {
                    let mut k = shared.lock().unwrap();
                    if same_vacation(&k, &key, epoch) { wait_again(&mut k, &plan, true); }
                }
                load("vacation", c, shared);
                return Err(e);
            }
        }
        let mut res = Ok(());
        if plan.touches_filters {
            let req = vac::SyncRequest { enabled: plan.after.is_enabled, forward: plan.forward.clone(), audience: plan.audience.clone(), from: plan.after.from.as_deref(), until: plan.after.to.as_deref() };
            res = vac::sync_filters(c, &req).map_err(|e| format!("The reply was saved, but its filters part was not: {e}"));
        }
        // read before the cache's lock is taken: the page never waits for the server
        let filters = vac::load_filters(c);
        if let Some(e) = res.as_ref().err().cloned() {
            // switched off, and the filters script still runs the reply: the page says so
            if !plan.after.is_enabled && filters.as_ref().is_some_and(|f| f.includes_vacation) {
                res = Err(format!("The reply is still being sent: switching it off in the filters script did not go through ({e}). It is tried again with your next change."));
            }
        }
        {
            let mut k = shared.lock().unwrap();
            if same_vacation(&k, &key, epoch) {
                k.vac_saved = Some(plan.after.clone());
                // the reply is on the server, its filters part is not: all of it waits to be written
                // again as set on the page (Bulwark keeps forwarding and recipients too), and a
                // reload keeps the page's "off" rather than showing what the script still does
                if res.is_err() { wait_again(&mut k, &plan, true); }
                if let Some(mut f) = filters {
                    // a reply-recipients choice made meanwhile is still the page's
                    if let Some(a) = k.vac_audience_dirty.clone() { f.audience = a; }
                    k.vac_filters = Some(f);
                }
            }
        }
        if plan.touches_block {
            let prev = Some((plan.saved.from.clone(), plan.saved.to.clone()));
            if let Err(e) = vac::sync_block(c, &plan.after, vac::Trigger::Save, prev) { eprintln!("out-of-office calendar block: {e}"); }
        }
        if let Some(m) = &plan.refused { res = res.and(Err(m.clone())); }
        res
    })
}

/// A write that did not go through: what it took from the page waits again. `reply`: the
/// reply itself too (it was not written).
fn wait_again(k: &mut Cache, plan: &RunPlan, reply: bool) {
    if reply { k.vac_reply_dirty |= plan.reply_dirty; }
    k.vac_forward_dirty |= plan.forward.is_some();
    if matches!(plan.forward, Some(Some(_))) { k.fwd_typed = true; }
    if k.vac_audience_dirty.is_none() { k.vac_audience_dirty = plan.audience.clone(); }
    k.vac_sync_dirty |= plan.touches_filters;
}

/// An edit on the out-of-office page: applied to the cache at once, written by one job that sends
/// the whole cache.
fn edit_vacation(field: &str, value: &Value, k: &mut Cache, shared: Shared) -> Option<Job> {
    use crate::vacation as vac;
    // every job runs the waiting write first: it replaces the debounce timer of a write still
    // waiting (typing, then a switch within the delay), which would then never run
    if field == "block" {
        // the setting itself was stored by `edit`
        if !vac_current(k) { return None; }
        return Some(Box::new(move |c: &Client| {
            vac::serialized(c, || {
                let run = vacation_run(c, &shared);
                // the reply as the server has it now (after the write above)
                let reply = { let k = shared.lock().unwrap(); if vac_current(&k) && k.vac_account == c.state_key() { k.vac_saved.clone() } else { None } };
                let Some(reply) = reply else { return run.and(Err(OTHER_ACCOUNT.into())) };
                let prev = Some((reply.from.clone(), reply.to.clone()));
                let block = vac::sync_block(c, &reply, vac::Trigger::Save, prev).map(|_| ());
                run.and(block)
            })
        }));
    }
    match apply_edit(field, value, k) {
        Edit::Local => None,
        Edit::Refuse(m) => Some(Box::new(move |c: &Client| refused(c, &shared, m))),
        Edit::Save => Some(Box::new(move |c: &Client| vacation_run(c, &shared))),
    }
}

/// A refused edit: what else is waiting is written, then the refusal is the error.
fn refused(c: &Client, shared: &Shared, m: String) -> Result<(), String> {
    match vacation_run(c, shared) { Ok(()) => Err(m), Err(e) => Err(format!("{m} {e}")) }
}

/// Text fields whose note changes while typing (a date that does not read as one yet, an end
/// before the start): the page is redrawn after each edit so the note shows at once.
pub fn redraw_on_text(key: &str) -> bool { matches!(key, "server.vacation.fromDate" | "server.vacation.toDate") }

/// Which waiting server write a new one replaces: the out-of-office page's edits are one write
/// (each sends the whole page), any other key only its own earlier keystrokes.
pub fn job_slot(key: &str) -> String {
    if key.starts_with("server.vacation.") { "server.vacation".into() } else { key.to_string() }
}

/// The longest a waiting server write is held back while other edits keep restarting the timer.
pub const MAX_WAIT: std::time::Duration = std::time::Duration::from_secs(5);

/// The debounce delay for a new edit when the oldest waiting write has waited `waited`: never
/// past [`MAX_WAIT`] in all.
pub fn debounce_delay(delay: std::time::Duration, waited: std::time::Duration) -> std::time::Duration {
    delay.min(MAX_WAIT.saturating_sub(waited))
}

/// Server writes waiting for the debounce: one per slot ([`job_slot`]), in the order they came;
/// a new one replaces only the one of its own slot, so an edit on another page never drops it.
pub struct PendingJobs<J> { items: Vec<(String, J)> }

impl<J> Default for PendingJobs<J> { fn default() -> Self { PendingJobs { items: vec![] } } }

impl<J> PendingJobs<J> {
    pub fn put(&mut self, slot: String, job: J) {
        match self.items.iter_mut().find(|(s, _)| *s == slot) { Some(x) => x.1 = job, None => self.items.push((slot, job)) }
    }
    pub fn take_all(&mut self) -> Vec<(String, J)> { std::mem::take(&mut self.items) }
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
        let rows = rows("account", &cache_with(q, vec![]), &crate::settings::Settings::with_prefs(Default::default())).expect("the account page has rows");
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
        assert!(rows("account", &cache_with(vec![], vec![]), &crate::settings::Settings::with_prefs(Default::default())).is_none());
        let q = vec![json!({"resourceType": "octets", "used": 1, "hardLimit": 2})];
        assert!(rows("composing", &cache_with(q.clone(), vec![]), &crate::settings::Settings::with_prefs(Default::default())).is_none());
        // with identities the storage lines come first, then the identities
        let ids = vec![json!({"id": "i1", "email": "ann@example.test", "name": "Ann"})];
        let r = rows("account", &cache_with(q, ids), &crate::settings::Settings::with_prefs(Default::default())).unwrap();
        assert_eq!(r[0].kind.as_str(), "meter");
        assert!(r.iter().any(|x| x.label.as_str() == "ann@example.test"));
    }

    fn vac_cache(on: bool, text: &str) -> Cache {
        let mut k = Cache::default();
        k.vacation = Some(json!({"isEnabled": on, "subject": "Away", "textBody": text, "htmlBody": null, "fromDate": null, "toDate": null}));
        k.vac_saved = Some(reply_of(k.vacation.as_ref().unwrap(), on));
        k.account = key_of("A");
        k.vac_account = key_of("A");
        k
    }

    /// The calls a fake server got: (method, arguments).
    type Log = Arc<Mutex<Vec<(String, Value)>>>;

    /// A client of account `acc` whose server answers from `answer` (method, arguments) and logs every call.
    fn fake(acc: &str, log: &Log, answer: impl Fn(&str, &Value) -> Value + Send + Sync + 'static) -> Client {
        let log = log.clone();
        Client::fake("https://mail.example.test", acc, &["urn:ietf:params:jmap:core", "urn:ietf:params:jmap:mail", "urn:ietf:params:jmap:vacationresponse"], move |calls| {
            Ok(calls.as_array().unwrap().iter().map(|c| {
                let m = c[0].as_str().unwrap_or("").to_string();
                log.lock().unwrap().push((m.clone(), c[1].clone()));
                json!([m, answer(&m, &c[1]), c[2]])
            }).collect())
        })
    }

    /// The usual answers: the set succeeds, the get returns `server`.
    fn server(server: Value) -> impl Fn(&str, &Value) -> Value + Send + Sync + 'static {
        move |m, _| match m {
            "VacationResponse/set" => json!({"updated": {"singleton": null}}),
            "VacationResponse/get" => json!({"list": [server.clone()]}),
            "Identity/get" => json!({"list": [{"id": "i1", "email": "ann@example.test"}]}),
            _ => json!({}),
        }
    }

    fn key_of(acc: &str) -> String { fake(acc, &Log::default(), |_, _| json!({})).state_key() }

    fn sets(log: &Log) -> Vec<Value> { log.lock().unwrap().iter().filter(|(m, _)| m == "VacationResponse/set").map(|(_, a)| a.clone()).collect() }

    #[test]
    fn the_page_reads_the_block_setting_from_the_settings_held() {
        // refresh_settings holds the program's settings while it asks for the rows; with both dates
        // set the page shows "Block my calendar", which once read them through the live settings
        // (the same lock again: a hang)
        let shared: Shared = Arc::new(Mutex::new(vac_cache(true, "x")));
        {
            let mut k = shared.lock().unwrap();
            let v = k.vacation.as_mut().unwrap();
            v["fromDate"] = json!("2026-10-01T00:00:00.000Z");
            v["toDate"] = json!("2026-10-08T00:00:00.000Z");
        }
        let prefs = Arc::new(Mutex::new(crate::settings::Settings::with_prefs(Default::default())));
        for on in [false, true] {
            let mut held = prefs.lock().unwrap();
            held.prefs.insert("vacationBlockCalendar".into(), json!(on));
            let before = crate::settings::LIVE_USES.with(|n| n.get());
            let r = rows("vacation", &shared, &held).unwrap();
            assert_eq!(crate::settings::LIVE_USES.with(|n| n.get()), before, "the rows went through the live settings");
            assert_eq!(r.iter().find(|r| r.key.as_str() == "server.vacation.block").map(|r| r.on), Some(on));
        }
    }

    #[test]
    fn another_accounts_reply_is_never_shown_edited_or_sent() {
        let shared: Shared = Arc::new(Mutex::new(vac_cache(false, "Away until Monday")));
        let prefs = crate::settings::Settings::with_prefs(Default::default());
        assert!(rows("vacation", &shared, &prefs).unwrap().iter().any(|r| r.key.as_str() == "server.vacation.textBody"));
        // switched to B: A's reply is gone at once, the page waits for B's
        set_account(&shared, &key_of("B"));
        let r = rows("vacation", &shared, &prefs).unwrap();
        assert!(r.iter().any(|r| r.label.as_str() == "Loading…") && !r.iter().any(|r| r.key.as_str() == "server.vacation.textBody"));
        assert_eq!(apply_edit("isEnabled", &json!(true), &mut shared.lock().unwrap()), Edit::Local);
        // A's data back in the cache (a load that finished late) still is not B's
        { let mut k = shared.lock().unwrap(); let a = vac_cache(false, "A's"); k.vacation = a.vacation; k.vac_account = key_of("A"); k.vac_reply_dirty = true; }
        assert!(rows("vacation", &shared, &prefs).unwrap().iter().any(|r| r.label.as_str() == "Loading…"));
        assert_eq!(apply_edit("textBody", &json!("x"), &mut shared.lock().unwrap()), Edit::Local);
        // a write for B with the cache holding A's: nothing is sent
        let log = Log::default();
        let b = fake("B", &log, server(json!({})));
        assert_eq!(vacation_run(&b, &shared), Err(OTHER_ACCOUNT.into()));
        assert!(log.lock().unwrap().is_empty());
        // a write for A after the switch: nothing is sent either
        let a = fake("A", &log, server(json!({})));
        assert!(vacation_run(&a, &shared).is_err());
        assert!(log.lock().unwrap().is_empty());
    }

    #[test]
    fn a_load_or_save_of_the_account_before_writes_nothing_back() {
        let shared: Shared = Arc::new(Mutex::new(vac_cache(false, "x")));
        // A's save is under way when the user switches to B (set_account runs while the request is out)
        let log = Log::default();
        let sh = shared.clone();
        let b_key = key_of("B");
        let a = fake("A", &log, move |m, _| {
            if m == "VacationResponse/set" { set_account(&sh, &b_key); }
            json!({"updated": {"singleton": null}})
        });
        assert_eq!(apply_edit("isEnabled", &json!(false), &mut shared.lock().unwrap()), Edit::Save);
        assert_eq!(apply_edit("textBody", &json!("Back on Monday"), &mut shared.lock().unwrap()), Edit::Save);
        assert!(vacation_run(&a, &shared).is_ok());
        assert_eq!(sets(&log).len(), 1);
        {
            let k = shared.lock().unwrap();
            assert!(k.vacation.is_none() && k.vac_saved.is_none() && k.vac_account.is_empty(), "A's save wrote into B's page");
        }
        // B's page loads; then A's late load finishes: B's stays
        let b = fake("B", &Log::default(), server(json!({"isEnabled": true, "subject": "B away", "textBody": "B", "fromDate": null, "toDate": null})));
        load("vacation", &b, &shared);
        let a = fake("A", &Log::default(), server(json!({"isEnabled": false, "subject": "A", "textBody": "A", "fromDate": null, "toDate": null})));
        load("vacation", &a, &shared);
        let k = shared.lock().unwrap();
        assert_eq!(k.vac_account, key_of("B"));
        assert_eq!(k.vacation.as_ref().unwrap()["subject"], "B away");
        assert_eq!(k.vac_saved.as_ref().map(|s| s.is_enabled), Some(true));
    }

    #[test]
    fn the_block_switch_and_a_refusal_send_what_is_waiting_first() {
        // typing starts a write 0.9 s later; the switch's (or a refusal's) job replaces that timer
        for refusal in [false, true] {
            let shared: Shared = Arc::new(Mutex::new(vac_cache(false, "")));
            {
                let mut k = shared.lock().unwrap();
                let v = k.vacation.as_mut().unwrap();
                v["fromDate"] = json!("2026-10-01T00:00:00.000Z");
                v["toDate"] = json!("2026-10-08T00:00:00.000Z");
                k.vac_saved = Some(reply_of(v, false));
            }
            assert!(edit_vacation("textBody", &json!("Back on Monday"), &mut shared.lock().unwrap(), shared.clone()).is_some());
            let job = if refusal {
                edit_vacation("subject", &json!("s".repeat(600)), &mut shared.lock().unwrap(), shared.clone())
            } else {
                edit_vacation("block", &json!(false), &mut shared.lock().unwrap(), shared.clone())
            }.expect("a job");
            let log = Log::default();
            let res = job(&fake("A", &log, server(json!({}))));
            if refusal { assert!(matches!(&res, Err(m) if m.contains("subject")), "{res:?}"); } else { assert_eq!(res, Ok(())); }
            let s = sets(&log);
            assert_eq!(s.len(), 1, "the typed message was not sent");
            assert_eq!(s[0]["update"]["singleton"]["textBody"], "Back on Monday");
            assert!(!shared.lock().unwrap().vac_reply_dirty);
        }
    }

    #[test]
    fn a_refused_or_failed_write_keeps_what_is_typed() {
        // forwarding switched on, then its address made unusable: the forwarding waits, the reply
        // still goes out (a half-typed address never holds back the rest)
        let make = || {
            let mut k = vac_cache(false, "x");
            k.vac_filters = Some(crate::vacation::Filters { forward_available: true, ..Default::default() });
            k.fwd_to = "ann@example.test".into();
            assert_eq!(apply_edit("forwardEnabled", &json!(true), &mut k), Edit::Save);
            assert_eq!(apply_edit("textBody", &json!("Back soon"), &mut k), Edit::Save);
            assert_eq!(apply_edit("forwardTo", &json!("ann@"), &mut k), Edit::Local);
            k
        };
        let mut k = make();
        let plan = take_run_plan(&mut k).unwrap().unwrap();
        assert_eq!(plan.patch["textBody"], "Back soon");
        assert!(plan.forward.is_none() && plan.refused.is_some());
        assert!(!k.vac_reply_dirty && k.vac_forward_dirty);
        // the forwarding alone: refused, still waiting
        assert!(take_run_plan(&mut k).is_err());
        assert!(k.vac_forward_dirty);
        // vacation_run: the reply is written, the refusal is the error, nothing reloaded over it
        let shared: Shared = Arc::new(Mutex::new(make()));
        let log = Log::default();
        let res = vacation_run(&fake("A", &log, server(json!({"textBody": "old"}))), &shared);
        assert!(matches!(&res, Err(e) if e.contains("address")), "{res:?}");
        assert_eq!(sets(&log).len(), 1);
        assert!(!log.lock().unwrap().iter().any(|(m, _)| m == "VacationResponse/get"));
        assert_eq!(shared.lock().unwrap().vacation.as_ref().unwrap()["textBody"], "Back soon");
        assert!(shared.lock().unwrap().vac_forward_dirty);
        assert!(vacation_run(&fake("A", &log, server(json!({}))), &shared).is_err());
        assert_eq!(sets(&log).len(), 1);
        // the server turns the write down: the text stays on the page and waits; the reload keeps it
        let shared: Shared = Arc::new(Mutex::new(vac_cache(false, "x")));
        assert_eq!(apply_edit("textBody", &json!("Back soon"), &mut shared.lock().unwrap()), Edit::Save);
        let log = Log::default();
        let old = json!({"isEnabled": false, "subject": "Away", "textBody": "old", "fromDate": null, "toDate": null});
        let refuse = move |m: &str, a: &Value| if m == "VacationResponse/set" { json!({"notUpdated": {"singleton": {"type": "invalidProperties"}}}) } else { server(old.clone())(m, a) };
        assert!(vacation_run(&fake("A", &log, refuse), &shared).is_err());
        assert_eq!(sets(&log).len(), 1);
        assert!(log.lock().unwrap().iter().any(|(m, _)| m == "VacationResponse/get"), "reloaded");
        let k = shared.lock().unwrap();
        assert!(k.vac_reply_dirty);
        assert_eq!(k.vacation.as_ref().unwrap()["textBody"], "Back soon");
        assert_eq!(k.vac_saved.as_ref().map(|s| s.subject.as_str()), Some("Away"));
    }

    #[test]
    fn a_load_keeps_waiting_edits_and_typed_dates() {
        let shared: Shared = Arc::new(Mutex::new(vac_cache(false, "x")));
        {
            let mut k = shared.lock().unwrap();
            k.vac_filters = Some(crate::vacation::Filters { forward_available: true, ..Default::default() });
            k.fwd_to = "ann@example.test".into();
            assert_eq!(apply_edit("textBody", &json!("Back soon"), &mut k), Edit::Save);
            assert_eq!(apply_edit("forwardSave", &Value::Null, &mut k), Edit::Save);
            assert_eq!(apply_edit("fromDate", &json!("2026-1"), &mut k), Edit::Local);
        }
        load("vacation", &fake("A", &Log::default(), server(json!({"isEnabled": true, "subject": "Server", "textBody": "old", "fromDate": null, "toDate": null}))), &shared);
        let k = shared.lock().unwrap();
        assert!(k.vac_reply_dirty && k.vac_forward_dirty);
        assert_eq!(k.vacation.as_ref().unwrap()["textBody"], "Back soon");
        assert_eq!(k.fwd_to, "ann@example.test");
        assert_eq!(k.vac_from_txt.as_deref(), Some("2026-1"));
        // what the server has is what the next write is judged against
        assert_eq!(k.vac_saved.as_ref().map(|s| (s.is_enabled, s.subject.as_str())), Some((true, "Server")));
        drop(k);
        // with nothing waiting the page shows the server's reply
        { let mut k = shared.lock().unwrap(); k.vac_reply_dirty = false; k.vac_forward_dirty = false; }
        load("vacation", &fake("A", &Log::default(), server(json!({"isEnabled": false, "subject": "S2", "textBody": "new", "fromDate": null, "toDate": null}))), &shared);
        assert_eq!(shared.lock().unwrap().vacation.as_ref().unwrap()["textBody"], "new");
    }

    #[test]
    fn editing_one_date_keeps_the_other_at_its_stored_instant() {
        // 2026-10-25 01:30Z is 02:30 CET, the second 02:30 of that night in Berlin; read again from
        // its text it would be the first (00:30Z)
        let berlin: chrono_tz::Tz = "Europe/Berlin".parse().unwrap();
        let shown = crate::vacation::show_moment("2026-10-25T01:30:00.000Z", berlin);
        assert_eq!(shown, "2026-10-25 02:30");
        assert_eq!(crate::vacation::parse_moment(&shown, berlin).unwrap().as_deref(), Some("2026-10-25T00:30:00.000Z"));
        let mut k = vac_cache(false, "x");
        k.vacation.as_mut().unwrap()["fromDate"] = json!("2026-10-25T01:30:00.000Z");
        k.vacation.as_mut().unwrap()["toDate"] = json!("2026-10-30T00:00:00.000Z");
        assert_eq!(apply_edit_in("toDate", &json!("2026-10-31"), &mut k, berlin), Edit::Save);
        let v = k.vacation.as_ref().unwrap();
        assert_eq!(v["fromDate"], "2026-10-25T01:30:00.000Z");
        assert_eq!(v["toDate"], "2026-10-30T23:00:00.000Z");
    }

    #[test]
    fn typed_text_and_the_switch_go_out_together() {
        // the switch's timer replaces the text's, so the one write has to carry both
        let mut k = vac_cache(false, "");
        assert_eq!(apply_edit("textBody", &json!("Back on Monday\n\nAnn"), &mut k), Edit::Save);
        assert_eq!(apply_edit("isEnabled", &json!(true), &mut k), Edit::Save);
        let plan = take_run_plan(&mut k).unwrap().unwrap();
        assert_eq!(plan.patch["isEnabled"], true);
        assert_eq!(plan.patch["textBody"], "Back on Monday\n\nAnn");
        assert_eq!(plan.patch["htmlBody"], "<p>Back on Monday</p><p>Ann</p>");
        assert!(plan.touches_filters && plan.touches_block && plan.after.is_enabled && !plan.saved.is_enabled);
        // nothing is left waiting
        assert!(take_run_plan(&mut k).unwrap().is_none());
    }

    #[test]
    fn an_empty_message_cannot_be_switched_on_or_left_empty_while_on() {
        let mut k = vac_cache(false, "");
        assert!(matches!(apply_edit("isEnabled", &json!(true), &mut k), Edit::Refuse(m) if m.contains("message")));
        assert_eq!(k.vacation.as_ref().unwrap()["isEnabled"], false);
        // the page shows the reply on although the server says off (the filters include): clearing the text is refused
        let mut k = vac_cache(true, "Away");
        assert!(matches!(apply_edit("textBody", &json!("  "), &mut k), Edit::Refuse(_)));
        assert_eq!(k.vacation.as_ref().unwrap()["textBody"], "Away");
        assert!(matches!(apply_edit("subject", &json!("s".repeat(600)), &mut k), Edit::Refuse(m) if m.contains("subject")));
    }

    #[test]
    fn html_typed_in_the_html_field_is_cleaned_and_the_text_follows() {
        let mut k = vac_cache(false, "old");
        assert_eq!(apply_edit("htmlBody", &json!("<p>Hi</p><script>x</script><p onclick=\"y\">there</p>"), &mut k), Edit::Save);
        let plan = take_run_plan(&mut k).unwrap().unwrap();
        assert_eq!(plan.patch["htmlBody"], "<p>Hi</p><p>there</p>");
        assert_eq!(plan.patch["textBody"], "Hi\n\nthere");
        // emptying the HTML field removes the formatted version only
        assert_eq!(apply_edit("htmlBody", &json!(""), &mut k), Edit::Save);
        let plan = take_run_plan(&mut k).unwrap().unwrap();
        assert_eq!(plan.patch["htmlBody"], Value::Null);
        assert_eq!(plan.patch["textBody"], "Hi\n\nthere");
    }

    #[test]
    fn from_and_until_are_checked_against_each_other() {
        let mut k = vac_cache(false, "x");
        // not a date yet: kept in the field, nothing to save
        assert_eq!(apply_edit("fromDate", &json!("2026-1"), &mut k), Edit::Local);
        assert_eq!(apply_edit("fromDate", &json!("2026-10-10"), &mut k), Edit::Save);
        // an end before the saved start waits, marked unsaved; once both are typed and still inverted the
        // edit is refused and the typed dates are dropped (the page redraws from the cache)
        assert_eq!(apply_edit("toDate", &json!("2026-10-05"), &mut k), Edit::Local);
        assert!(k.vac_to_txt.is_some());
        assert!(matches!(apply_edit("fromDate", &json!("2026-10-20"), &mut k), Edit::Refuse(m) if m.contains("after the start")));
        assert!(k.vac_from_txt.is_none() && k.vac_to_txt.is_none());
        assert_eq!(k.vacation.as_ref().unwrap()["toDate"], Value::Null);
        // both typed before either was saved: judged by what is typed, not by the old stored value
        let mut k = vac_cache(false, "x");
        k.vacation.as_mut().unwrap()["fromDate"] = json!("2026-01-01T00:00:00.000Z");
        k.vacation.as_mut().unwrap()["toDate"] = json!("2026-01-02T00:00:00.000Z");
        assert_eq!(apply_edit("fromDate", &json!("2026-12-01"), &mut k), Edit::Local); // waits for the end to match
        assert_eq!(k.vacation.as_ref().unwrap()["fromDate"], "2026-01-01T00:00:00.000Z");
        assert_eq!(apply_edit("toDate", &json!("2026-12-10"), &mut k), Edit::Save);
        let plan = take_run_plan(&mut k).unwrap().unwrap();
        let (f, t) = (plan.patch["fromDate"].as_str().unwrap().to_string(), plan.patch["toDate"].as_str().unwrap().to_string());
        assert!(f < t && f.starts_with("2026-1") && t.starts_with("2026-12"), "{f} {t}");
        assert!(!k.vac_from_pending && !k.vac_to_pending);
    }

    #[test]
    fn the_forward_address_is_saved_only_on_request() {
        let mut k = vac_cache(false, "x");
        k.vac_filters = Some(crate::vacation::Filters { forward_available: true, ..Default::default() });
        // typing never writes, whatever the text is
        for t in ["a", "ann@", "ann@example", "ann@example.te", "ann@example.test"] {
            assert_eq!(apply_edit("forwardTo", &json!(t), &mut k), Edit::Local);
            assert!(!k.vac_forward_dirty);
        }
        assert!(!forward_usable("ann@example") && !forward_usable("ann@") && forward_usable("ann@example.test"));
        // switching it on with no usable address is refused
        k.fwd_to = "ann@example".into();
        assert!(matches!(apply_edit("forwardEnabled", &json!(true), &mut k), Edit::Refuse(_)));
        assert!(matches!(apply_edit("forwardSave", &Value::Null, &mut k), Edit::Refuse(_)));
        k.fwd_to = "ann@example.test".into();
        assert_eq!(apply_edit("forwardEnabled", &json!(true), &mut k), Edit::Save);
        assert_eq!(apply_edit("forwardKeep", &json!(true), &mut k), Edit::Save);
        let plan = take_run_plan(&mut k).unwrap().unwrap();
        assert_eq!(plan.forward, Some(Some(json!({"enabled": true, "to": "ann@example.test", "keepCopy": true}))));
        assert!(plan.touches_filters && plan.patch.is_empty());
        // forwarding that is off and was never stored is removed, not saved with an address
        let mut k = vac_cache(false, "x");
        k.fwd_to = "ann@example.test".into();
        assert_eq!(apply_edit("forwardSave", &Value::Null, &mut k), Edit::Save);
        assert_eq!(take_run_plan(&mut k).unwrap().unwrap().forward, Some(None));
    }

    #[test]
    fn the_audience_choice_waits_for_the_write() {
        let mut k = vac_cache(false, "x");
        assert_eq!(apply_edit("audience", &json!(1), &mut k), Edit::Local); // not available on this server
        k.vac_filters = Some(crate::vacation::Filters { audience_available: true, ..Default::default() });
        k.vac_domains = vec!["example.test".into()];
        assert_eq!(apply_edit("audience", &json!(2), &mut k), Edit::Save);
        let plan = take_run_plan(&mut k).unwrap().unwrap();
        assert_eq!(plan.audience, Some(Some(json!({"only": "external", "domains": ["example.test"]}))));
        assert_eq!(apply_edit("audience", &json!(0), &mut k), Edit::Save);
        assert_eq!(take_run_plan(&mut k).unwrap().unwrap().audience, Some(None));
    }

    fn fake_with(acc: &str, caps: &[&str], log: &Log, answer: impl Fn(&str, &Value) -> Value + Send + Sync + 'static) -> Client {
        let log = log.clone();
        Client::fake("https://mail.example.test", acc, caps, move |calls| {
            Ok(calls.as_array().unwrap().iter().map(|c| {
                let m = c[0].as_str().unwrap_or("").to_string();
                log.lock().unwrap().push((m.clone(), c[1].clone()));
                json!([m, answer(&m, &c[1]), c[2]])
            }).collect())
        })
    }

    #[test]
    fn a_reload_during_a_write_waits_for_it() {
        // the page is reopened (a load starts) while the switch's write is out: the load must not
        // land with the reply as it was before the write
        let shared: Shared = Arc::new(Mutex::new(vac_cache(false, "Away")));
        let stored = Arc::new(Mutex::new(json!({"isEnabled": false, "subject": "Away", "textBody": "Away", "htmlBody": null, "fromDate": null, "toDate": null})));
        let (tx, rx) = std::sync::mpsc::channel::<()>();
        let tx = Mutex::new(tx);
        let st = stored.clone();
        let answer = Arc::new(move |m: &str, a: &Value| -> Value {
            match m {
                "VacationResponse/set" => {
                    let _ = tx.lock().unwrap().send(());
                    // the server takes its time
                    std::thread::sleep(std::time::Duration::from_millis(300));
                    let mut s = st.lock().unwrap();
                    for (k, v) in a["update"]["singleton"].as_object().unwrap() { s[k.as_str()] = v.clone(); }
                    json!({"updated": {"singleton": null}})
                }
                "VacationResponse/get" => json!({"list": [st.lock().unwrap().clone()]}),
                "Identity/get" => json!({"list": [{"id": "i1", "email": "ann@example.test"}]}),
                _ => json!({}),
            }
        });
        let log = Log::default();
        let (a1, a2) = (answer.clone(), answer.clone());
        let writer = fake("A", &log, move |m, a| a1(m, a));
        let reader = fake("A", &log, move |m, a| a2(m, a));
        assert_eq!(apply_edit("isEnabled", &json!(true), &mut shared.lock().unwrap()), Edit::Save);
        let sh = shared.clone();
        let t = std::thread::spawn(move || vacation_run(&writer, &sh));
        rx.recv().unwrap(); // the write is out, the page's waiting flags are cleared
        load("vacation", &reader, &shared);
        assert_eq!(t.join().unwrap(), Ok(()));
        let mut k = shared.lock().unwrap();
        assert_eq!(k.vacation.as_ref().unwrap()["isEnabled"], true, "the reload put the reply back to off");
        assert_eq!(k.vac_saved.as_ref().map(|s| s.is_enabled), Some(true));
        // and the next edit does not send the old reply back
        assert_eq!(apply_edit("subject", &json!("Out"), &mut k), Edit::Save);
        let plan = take_run_plan(&mut k).unwrap().unwrap();
        assert!(!plan.patch.contains_key("isEnabled"), "{:?}", plan.patch);
        assert_eq!(stored.lock().unwrap()["isEnabled"], true);
    }

    #[test]
    fn a_forward_address_saved_while_off_stays_in_the_field() {
        // Bulwark vacation-settings.tsx: off and nothing stored, a save stores no forwarding but the
        // field keeps the address, so switching forwarding on afterwards needs no retyping
        let shared: Shared = Arc::new(Mutex::new(vac_cache(false, "x")));
        {
            let mut k = shared.lock().unwrap();
            k.vac_filters = Some(crate::vacation::Filters { forward_available: true, ..Default::default() });
            assert_eq!(apply_edit("forwardTo", &json!("ann@example.test"), &mut k), Edit::Local);
            assert_eq!(apply_edit("forwardSave", &Value::Null, &mut k), Edit::Save);
        }
        let c = fake("A", &Log::default(), server(json!({"isEnabled": false, "subject": "Away", "textBody": "x", "fromDate": null, "toDate": null})));
        assert_eq!(vacation_run(&c, &shared), Ok(()));
        // "Save forwarding" reloads the page
        load("vacation", &c, &shared);
        let mut k = shared.lock().unwrap();
        assert_eq!(k.fwd_to, "ann@example.test");
        assert!(!k.fwd_enabled);
        assert_eq!(apply_edit("forwardEnabled", &json!(true), &mut k), Edit::Save);
        assert_eq!(take_run_plan(&mut k).unwrap().unwrap().forward, Some(Some(json!({"enabled": true, "to": "ann@example.test", "keepCopy": false}))));
    }

    #[test]
    fn a_failed_filters_write_keeps_forwarding_waiting() {
        // the server has Sieve without "include": the forwarding cannot be written
        let shared: Shared = Arc::new(Mutex::new(vac_cache(false, "x")));
        {
            let mut k = shared.lock().unwrap();
            k.vac_filters = Some(crate::vacation::Filters { forward_available: true, ..Default::default() });
            k.fwd_to = "ann@example.test".into();
            assert_eq!(apply_edit("forwardEnabled", &json!(true), &mut k), Edit::Save);
        }
        let log = Log::default();
        let caps = ["urn:ietf:params:jmap:core", "urn:ietf:params:jmap:mail", "urn:ietf:params:jmap:vacationresponse", "urn:ietf:params:jmap:sieve"];
        let c = fake_with("A", &caps, &log, |m, _| if m == "SieveScript/get" { json!({"list": []}) } else { json!({}) });
        let res = vacation_run(&c, &shared);
        assert!(matches!(&res, Err(e) if e.contains("filters part")), "{res:?}");
        // not reloaded over, and still waiting as set on the page
        assert!(!log.lock().unwrap().iter().any(|(m, _)| m == "VacationResponse/get"));
        let mut k = shared.lock().unwrap();
        assert!(k.vac_forward_dirty && k.vac_sync_dirty && k.fwd_enabled);
        assert_eq!(k.fwd_to, "ann@example.test");
        let plan = take_run_plan(&mut k).unwrap().unwrap();
        assert!(plan.touches_filters);
        assert_eq!(plan.forward, Some(Some(json!({"enabled": true, "to": "ann@example.test", "keepCopy": false}))));
        // a reload (reopening the page) keeps what waits
        k.vac_forward_dirty = true;
        drop(k);
        load("vacation", &fake("A", &Log::default(), server(json!({"isEnabled": false, "subject": "Away", "textBody": "x"}))), &shared);
        let k = shared.lock().unwrap();
        assert!(k.fwd_enabled && k.fwd_to == "ann@example.test");
    }

    #[test]
    fn emptying_the_html_field_leaves_the_plain_text_alone() {
        let mut k = vac_cache(false, "old");
        assert_eq!(apply_edit("textBody", &json!("Back on Monday"), &mut k), Edit::Save);
        assert_eq!(apply_edit("htmlBody", &json!("  "), &mut k), Edit::Save);
        let plan = take_run_plan(&mut k).unwrap().unwrap();
        assert_eq!(plan.patch["textBody"], "Back on Monday");
        assert_eq!(plan.patch["htmlBody"], Value::Null);
    }

    #[test]
    fn waiting_writes_of_different_pages_do_not_replace_each_other() {
        assert!(redraw_on_text("server.vacation.fromDate") && redraw_on_text("server.vacation.toDate") && !redraw_on_text("server.vacation.subject"));
        let mut p = PendingJobs::default();
        p.put(job_slot("server.vacation.textBody"), 1);
        p.put(job_slot("server.identity.name:i1"), 2);
        p.put(job_slot("server.identity.replyTo:i1"), 3);
        // the out-of-office page is one write: the switch replaces the typing (it sends all of it)
        p.put(job_slot("server.vacation.block"), 4);
        p.put(job_slot("server.identity.name:i1"), 5);
        let got: Vec<(String, i32)> = p.take_all();
        assert_eq!(got, vec![("server.vacation".into(), 4), ("server.identity.name:i1".into(), 5), ("server.identity.replyTo:i1".into(), 3)]);
        assert!(p.take_all().is_empty());
    }

    #[test]
    fn the_first_load_sync_is_per_server_and_login() {
        // two servers hand out the same short account id
        let a = Client::fake("https://one.example.test", "b", &[], |_| Ok(vec![]));
        let b = Client::fake("https://two.example.test", "b", &[], |_| Ok(vec![]));
        assert_ne!(a.key_for("b"), b.key_for("b"));
        assert!(first_load(&a.key_for("b")));
        assert!(!first_load(&a.key_for("b")));
        assert!(first_load(&b.key_for("b")));
    }

    const SIEVE_CAPS: [&str; 4] = ["urn:ietf:params:jmap:core", "urn:ietf:params:jmap:mail", "urn:ietf:params:jmap:vacationresponse", "urn:ietf:params:jmap:sieve"];

    /// A server with a filters script (active) and the vacation script; downloads give `script`,
    /// uploads fail (the fake has no upload URL), so a filters write never goes through.
    fn sieve_server(script: String, vacation: Value) -> impl Fn(&str, &Value) -> Value + Send + Sync + 'static {
        move |m, _| match m {
            "SieveScript/get" => json!({"list": [{"id": "f1", "name": "filters", "blobId": "b1", "isActive": true}, {"id": "v1", "name": "vacation", "blobId": "b2", "isActive": false}]}),
            "blob/download" => json!({"text": script}),
            "VacationResponse/set" => json!({"updated": {"singleton": null}}),
            "VacationResponse/get" => json!({"list": [vacation.clone()]}),
            "Identity/get" => json!({"list": [{"id": "i1", "email": "ann@example.test"}]}),
            _ => json!({}),
        }
    }

    #[test]
    fn switched_off_while_the_filters_script_still_runs_the_reply() {
        let script = crate::sieve::generate_meta(&json!({"version": 1, "rules": [], "includeVacation": true}), "", &[]);
        let shared: Shared = Arc::new(Mutex::new(vac_cache(true, "Away")));
        let log = Log::default();
        let c = fake_with("A", &SIEVE_CAPS, &log, sieve_server(script, json!({"isEnabled": false, "subject": "Away", "textBody": "Away", "fromDate": null, "toDate": null})));
        assert_eq!(apply_edit("isEnabled", &json!(false), &mut shared.lock().unwrap()), Edit::Save);
        let res = vacation_run(&c, &shared);
        assert!(matches!(&res, Err(e) if e.contains("still being sent")), "{res:?}");
        assert_eq!(sets(&log)[0]["update"]["singleton"]["isEnabled"], false);
        {
            let k = shared.lock().unwrap();
            assert_eq!(k.vacation.as_ref().unwrap()["isEnabled"], false);
            assert!(k.vac_filters.as_ref().is_some_and(|f| f.includes_vacation));
            assert!(k.vac_reply_dirty && k.vac_sync_dirty, "the off waits to be written again");
        }
        let prefs = crate::settings::Settings::with_prefs(Default::default());
        let r = rows("vacation", &shared, &prefs).unwrap();
        assert!(r.iter().any(|r| r.key.as_str() == "server.vacation.retry" && r.label.as_str() == "Still running"));
        // reopening the page keeps "off" (the server's include alone would read as on); the next
        // write sends it again with the filters part
        load("vacation", &c, &shared);
        let mut k = shared.lock().unwrap();
        assert_eq!(k.vacation.as_ref().unwrap()["isEnabled"], false);
        let plan = take_run_plan(&mut k).unwrap().unwrap();
        assert_eq!(plan.patch["isEnabled"], false);
        assert!(plan.touches_filters && !plan.after.is_enabled);
        // "Try again" asks for that write
        drop(k);
        assert!(button("server.vacation.retry", &shared).is_some());
        assert!(shared.lock().unwrap().vac_sync_dirty);
    }

    #[test]
    fn a_typed_forward_address_gives_way_to_the_stored_one() {
        let stored = json!({"enabled": false, "to": "old@example.test", "keepCopy": false});
        let shared: Shared = Arc::new(Mutex::new(vac_cache(false, "x")));
        {
            let mut k = shared.lock().unwrap();
            k.vac_filters = Some(crate::vacation::Filters { forward_available: true, forward: Some(stored.clone()), ..Default::default() });
            k.fwd_to = "old@example.test".into();
            assert_eq!(apply_edit("forwardTo", &json!("new@exam"), &mut k), Edit::Local);
        }
        let prefs = crate::settings::Settings::with_prefs(Default::default());
        let hint = |sh: &Shared| rows("vacation", sh, &prefs).unwrap().iter().find(|r| r.key.as_str() == "server.vacation.forwardTo").map(|r| r.desc.to_string());
        assert_eq!(hint(&shared).as_deref(), Some("Not saved yet: press Save forwarding."));
        // a reload with a forwarding stored on the server shows that one (the half-typed one is gone)
        let script = crate::sieve::generate_meta(&json!({"version": 1, "rules": [], "vacationForward": stored}), "", &[]);
        load("vacation", &fake_with("A", &SIEVE_CAPS, &Log::default(), sieve_server(script, json!({"isEnabled": false, "subject": "Away", "textBody": "x"}))), &shared);
        {
            let mut k = shared.lock().unwrap();
            assert_eq!(k.fwd_to, "old@example.test");
            assert!(!k.fwd_typed);
            k.vac_filters.as_mut().unwrap().forward_available = true;
        }
        assert_eq!(hint(&shared).as_deref(), Some("Type the address, then press Save forwarding."));
        // nothing stored, forwarding off: a typed address is kept, and marked
        let shared: Shared = Arc::new(Mutex::new(vac_cache(false, "x")));
        shared.lock().unwrap().vac_filters = Some(crate::vacation::Filters { forward_available: true, ..Default::default() });
        assert_eq!(apply_edit("forwardTo", &json!("ann@example.test"), &mut shared.lock().unwrap()), Edit::Local);
        assert_eq!(hint(&shared).as_deref(), Some("Not stored yet: it is saved when you switch forwarding on."));
    }

    #[test]
    fn an_event_whose_times_cannot_be_read_is_left_alone() {
        let log = Log::default();
        let caps = ["urn:ietf:params:jmap:core", "urn:ietf:params:jmap:mail", "urn:ietf:params:jmap:calendars"];
        let c = fake_with("A", &caps, &log, |m, _| match m {
            "CalendarEvent/query" => json!({"ids": ["e1"]}),
            "CalendarEvent/get" => json!({"list": [{"id": "e1", "start": "2026-10-05T00:00:00", "duration": "P99999999D", "showWithoutTime": true, "keywords": {crate::vacation::OOO_KEYWORD: true}}]}),
            _ => json!({}),
        });
        let reply = crate::vacation::Reply { is_enabled: true, from: Some("2026-10-05T00:00:00.000Z".into()), to: Some("2026-10-09T00:00:00.000Z".into()), subject: "Away".into() };
        for trigger in [crate::vacation::Trigger::Save, crate::vacation::Trigger::Load] {
            assert_eq!(crate::vacation::sync_block(&c, &reply, trigger, None), Ok("unreadable"));
        }
        assert!(!log.lock().unwrap().iter().any(|(m, _)| m == "CalendarEvent/set" || m == "Calendar/get"));
    }

    #[test]
    fn typing_a_date_and_time_is_never_rewritten_under_the_caret() {
        let berlin: chrono_tz::Tz = "Europe/Berlin".parse().unwrap();
        let shared: Shared = Arc::new(Mutex::new(vac_cache(false, "x")));
        let prefs = crate::settings::Settings::with_prefs(Default::default());
        let typed = "2026-10-05 08:30";
        for n in 1..=typed.len() {
            let t = &typed[..n];
            let _ = apply_edit_in("fromDate", &json!(t), &mut shared.lock().unwrap(), berlin);
            let r = rows("vacation", &shared, &prefs).unwrap();
            let field = r.iter().find(|r| r.key.as_str() == "server.vacation.fromDate").unwrap();
            assert_eq!(field.text.as_str(), t, "the field was rewritten after {t:?}");
        }
        let k = shared.lock().unwrap();
        assert_eq!(k.vacation.as_ref().unwrap()["fromDate"], "2026-10-05T06:30:00.000Z");
        assert!(!k.vac_from_pending);
    }

    #[test]
    fn typing_never_holds_waiting_writes_back_past_the_cap() {
        use std::time::Duration as D;
        assert_eq!(debounce_delay(D::from_millis(900), D::ZERO), D::from_millis(900));
        assert_eq!(debounce_delay(D::from_millis(900), D::from_millis(4500)), D::from_millis(500));
        assert_eq!(debounce_delay(D::from_millis(900), D::from_secs(7)), D::ZERO);
    }
}
