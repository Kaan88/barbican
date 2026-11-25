//! Settings pages backed by server objects: Out of office (VacationResponse), Filters (Sieve),
//! Folders (Mailbox), Identities & signatures (Identity), masked addresses (x:MaskedEmail).
//! Values are cached here; edits are written back to the server (text edits are debounced).
//!
//! The identities and masked addresses are one account's: `owner` names it. A load for another
//! account clears them first, a load that finishes after another account's has started is
//! dropped, and a write checks it goes to the account the page showed, so nothing typed for one
//! account is saved in another.
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
    /// The account (`Client::state_key`) the identities, the identity form and the masked
    /// addresses belong to.
    pub owner: String,
    /// Bulwark's account id of `owner` (the `preferredIdentityIds` key) and its login.
    pub owner_key: String,
    pub owner_user: String,
    /// The identities of `owner` were read (an empty list is then "no addresses yet").
    pub ids_loaded: bool,
    /// The identity create / edit form, when open.
    pub id_form: Option<IdForm>,
    /// Bumped whenever the form opens, closes or is typed in, so a save that finishes later
    /// closes only the form it was started from.
    pub id_gen: u64,
    /// An identity save or delete is on its way: no second one, and the form takes no input,
    /// until it is answered (a double click must not add the address twice).
    pub id_busy: bool,
    /// The change on its way is a delete (the form, when open, says so).
    pub id_deleting: bool,
    pub masked: Vec<crate::masked::Masked>,
    /// 0 loading, 1 ready, 2 forbidden, 3 unavailable, 4 the load failed.
    pub masked_state: u8,
    /// The server answered anything but "not supported" for `owner`: the section shows.
    pub masked_shown: bool,
    pub masked_page: usize,
    /// The address whose details are open, and its description as typed.
    pub masked_open: String,
    pub masked_desc: String,
    pub masked_new_desc: String,
    pub masked_new_site: String,
    pub masked_notice: String,
    pub masked_busy: bool,
    pub masked_no_create: bool,
    pub masked_no_update: bool,
    pub masked_no_delete: bool,
    /// A delete that could not be confirmed: no delete until the list was read again.
    pub masked_needs_refresh: bool,
    /// Settings > Password & sign-in (src/security.rs).
    pub sec: crate::security::State,
}

/// The identity form (Bulwark identity-form.tsx): `id` "" while it adds an address.
#[derive(Clone, Debug, Default)]
pub struct IdForm {
    pub id: String,
    pub f: crate::identities::Form,
    /// The identity as it was loaded (what an edit is compared with).
    pub old: Option<Value>,
    pub error: String,
}

const ACCOUNT_CHANGED: &str = "The account changed; nothing was saved.";

fn plain_only() -> bool { crate::settings::Settings::load().bool("plainTextMode", false) }

/// Whether the open form holds input that is not saved.
fn form_dirty(k: &Cache) -> bool {
    k.id_form.as_ref().is_some_and(|x| x.f != crate::identities::form_of(x.old.as_ref(), plain_only()))
}

fn reset_masked(k: &mut Cache) {
    k.masked.clear();
    (k.masked_state, k.masked_shown, k.masked_page, k.masked_busy) = (0, false, 0, false);
    (k.masked_no_create, k.masked_no_update, k.masked_no_delete, k.masked_needs_refresh) = (false, false, false, false);
    k.masked_open.clear(); k.masked_desc.clear(); k.masked_new_desc.clear(); k.masked_new_site.clear(); k.masked_notice.clear();
}

/// Make `c`'s account the one the page holds; another account's identities, form and masked
/// addresses are dropped first. Returns the owner to check the load's result against.
pub fn claim(cache: &Shared, owner: &str, username: &str, server: &str) -> String {
    let mut k = cache.lock().unwrap_or_else(|e| e.into_inner());
    if k.owner != owner {
        k.owner = owner.to_string();
        k.owner_key = crate::identities::account_key(username, server);
        k.owner_user = username.to_string();
        k.identities.clear();
        k.quota.clear();
        k.ids_loaded = false;
        k.id_form = None;
        k.id_gen += 1;
        k.id_busy = false;
        k.id_deleting = false;
        k.confirm_delete.clear();
        reset_masked(&mut k);
    }
    owner.to_string()
}

/// The identities of the page in the user's order (the default sender first).
fn ordered_identities(k: &Cache) -> Vec<Value> {
    let p = crate::settings::Settings::load();
    let pref = crate::identities::decode_preferred(p.prefs.get(crate::identities::KEY_PREFERRED), &k.owner_key);
    crate::identities::ordered(&k.identities, &k.owner_user, pref.as_deref())
}

fn identity_form_rows(k: &Cache, out: &mut Vec<SRow>) {
    let Some(x) = k.id_form.as_ref() else { return };
    let editing = !x.id.is_empty();
    if k.id_busy {
        // while it is saved the form is shown, not editable
        out.push(row("section", "", if editing { "Edit address" } else { "Add address" }, "", "", false));
        for (label, v) in [("Your name", &x.f.name), ("Email address", &x.f.email), ("Reply-to", &x.f.reply_to), ("Bcc", &x.f.bcc)] {
            out.push(row("value", "", label, "", v, false));
        }
        let sig: String = (if x.f.html { crate::identities::text_from_html(&x.f.signature) } else { x.f.signature.clone() }).chars().take(300).collect();
        out.push(row("value", "", "Signature", &sig, "", false));
        out.push(row("value", "", if k.id_deleting { "Deleting…" } else { "Saving…" }, "", "", false));
        return;
    }
    let plain = plain_only();
    out.push(row("section", "", if editing { "Edit address" } else { "Add address" }, "", "", false));
    out.push(row("text", "server.ident.f.name", "Your name", "The name recipients see. It may be left empty.", &x.f.name, false));
    if editing {
        out.push(row("value", "", "Email address", "The address cannot be changed.", &x.f.email, false));
    } else {
        out.push(row("text", "server.ident.f.email", "Email address", "Required. The server decides which addresses you may send from.", &x.f.email, false));
    }
    out.push(row("text", "server.ident.f.replyTo", "Reply-to", "Optional; replies go here. Separate several with commas.", &x.f.reply_to, false));
    out.push(row("text", "server.ident.f.bcc", "Bcc", "Optional; every message sent from this address also goes to these, as a blind copy.", &x.f.bcc, false));
    if plain {
        out.push(row("value", "", "Plain-text signature", "Composing > Plain text only is on, so the signature is plain text. A formatted version stays as it is.", "", false));
    } else {
        out.push(row("toggle", "server.ident.f.html", "Formatted signature (HTML)", "On: the signature below is HTML (bold, links, a picture by web address). Scripts and the like are removed when it is saved, and a plain-text copy is made from it.", "", x.f.html));
    }
    out.push(row("textarea", "server.ident.f.sig", if x.f.html { "Signature (HTML)" } else { "Signature" }, "", &x.f.signature, false));
    if x.f.html && crate::identities::has_html_content(&x.f.signature) {
        let t: String = crate::identities::text_from_html(&x.f.signature).chars().take(300).collect();
        out.push(row("value", "", "Plain-text version", &t, "", false));
    }
    let bytes = crate::identities::form_signature_bytes(&x.f, x.old.as_ref(), plain);
    if let Some(n) = crate::identities::size_note(bytes) { out.push(row("value", "", "Signature size", &n, "", false)); }
    if !x.error.is_empty() { out.push(row("value", "", "Not saved", &x.error, "", false)); }
    out.push(row("button", "server.ident.save", "", "", if editing { "Save changes" } else { "Add address" }, false));
    out.push(row("button", "server.ident.cancel", "", "", if k.confirm_delete == "identcancel" { "Click again to discard what you typed" } else { "Cancel" }, false));
}

/// The masked addresses section of the Password & sign-in page, once the server has them.
fn masked_rows(k: &Cache) -> Vec<SRow> {
    use crate::masked::PAGE_SIZE;
    let mut out = vec![row("section", "", "Masked addresses", "Receive mail without sharing your main address. Disabling an address rejects new mail to it. These addresses cannot be used to send mail.", "", false)];
    match k.masked_state {
        0 => out.push(row("value", "", "Loading masked addresses…", "", "", false)),
        2 => out.push(row("value", "", "Not allowed", "Your current session is not allowed to list masked addresses.", "", false)),
        3 => out.push(row("value", "", "Not available", "Masked addresses aren't available for this account or server.", "", false)),
        4 => out.push(row("value", "", "Could not load", "Could not load the complete address list. Refresh to try again.", "", false)),
        _ => {
            let n = k.masked.len();
            out.push(row("value", "", &if n == 1 { "1 address".to_string() } else { format!("{n} addresses") }, "", "", false));
            if n == 0 { out.push(row("value", "", "No masked addresses yet.", "", "", false)); }
            let pages = n.div_ceil(PAGE_SIZE).max(1);
            let page = k.masked_page.min(pages - 1);
            for m in k.masked.iter().skip(page * PAGE_SIZE).take(PAGE_SIZE) {
                let desc = format!("{} · {} · {}", m.description.as_deref().filter(|d| !d.is_empty()).unwrap_or("No description"),
                    m.for_domain.as_deref().filter(|d| !d.is_empty()).unwrap_or("Not specified"), if m.enabled { "Enabled" } else { "Disabled" });
                let open = k.masked_open == m.id;
                out.push(row("button", &format!("server.masked.open:{}", m.id), &m.email, &desc, if open { "Close" } else { "Edit" }, false));
                out.push(row("button", &format!("server.masked.copy:{}", m.id), "", "", "Copy", false));
                if open {
                    if k.masked_no_update {
                        out.push(row("value", "", "Description", m.description.as_deref().unwrap_or(""), "", false));
                    } else {
                        out.push(row("text", "server.masked.desc", "Description (optional)", "", &k.masked_desc, false));
                        out.push(row("button", &format!("server.masked.savedesc:{}", m.id), "", "", "Save description", false));
                        out.push(row("button", &format!("server.masked.toggle:{}", m.id), "", if m.enabled { "New mail to this address is rejected while it is disabled." } else { "" }, if m.enabled { "Disable" } else { "Enable" }, false));
                    }
                    if !k.masked_no_delete && !k.masked_needs_refresh {
                        let ask = k.confirm_delete == format!("masked:{}", m.id);
                        out.push(row("button", &format!("server.masked.delete:{}", m.id), "",
                            if ask { "This cannot be undone. Mail sent to this address will no longer be delivered." } else { "" },
                            if ask { "Delete permanently" } else { "Delete" }, false));
                    }
                }
            }
            if pages > 1 {
                out.push(row("value", "", &format!("Page {} of {pages}", page + 1), "", "", false));
                if page > 0 { out.push(row("button", "server.masked.prev", "", "", "Previous", false)); }
                if page + 1 < pages { out.push(row("button", "server.masked.next", "", "", "Next", false)); }
            }
            if k.masked_no_update { out.push(row("value", "", "Editing not allowed", "The server did not allow editing masked addresses with this session. You can still copy addresses.", "", false)); }
            if k.masked_no_delete { out.push(row("value", "", "Deleting not allowed", "Your current session is not allowed to delete masked addresses. Other permitted actions remain available.", "", false)); }
            out.push(row("section", "", "Create masked address", "", "", false));
            if k.masked_no_create {
                out.push(row("value", "", "Creating not allowed", "The server did not allow creating a masked address with this session.", "", false));
            } else {
                out.push(row("text", "server.masked.newsite", "Website (optional)", "The site you will give the address to.", &k.masked_new_site, false));
                out.push(row("text", "server.masked.newdesc", "Description (optional)", "", &k.masked_new_desc, false));
                out.push(row("button", "server.masked.create", "", "", "Create masked address", false));
            }
        }
    }
    if !k.masked_notice.is_empty() { out.push(row("value", "", &k.masked_notice, "", "", false)); }
    if k.masked_state != 0 && k.masked_state != 3 { out.push(row("button", "server.masked.refresh", "", "", "Refresh", false)); }
    out
}

type Job = Box<dyn FnOnce(&Client, &Shared) -> Result<(), String> + Send>;

fn noop() -> Option<Job> { Some(Box::new(|_, _| Ok(()))) }

/// A masked-address change (Bulwark mutate): one at a time, for the account the page shows;
/// the outcome is a line on the page.
fn masked_job(cache: &Shared, kind: &'static str, op: impl FnOnce(&Client) -> Result<(), crate::masked::MErr> + Send + 'static) -> Option<Job> {
    let (owner, epoch) = {
        let mut k = cache.lock().unwrap_or_else(|e| e.into_inner());
        if k.masked_busy || k.masked_state != 1 { return None; }
        k.masked_busy = true;
        k.masked_notice.clear();
        (k.owner.clone(), k.sec.epoch)
    };
    Some(Box::new(move |c: &Client, k: &Shared| {
        if c.state_key() != owner {
            let mut g = k.lock().unwrap_or_else(|e| e.into_inner());
            if g.owner == owner && g.sec.epoch == epoch { g.masked_busy = false; }
            return Err(ACCOUNT_CHANGED.into());
        }
        let r = op(c);
        let mut g = k.lock().unwrap_or_else(|e| e.into_inner());
        // the page shows another account by now, or was reset (signed out, Settings closed and
        // opened again): nothing of this belongs there
        if g.owner != owner || g.sec.epoch != epoch { return Ok(()); }
        g.masked_busy = false;
        match r {
            Ok(()) => {
                g.masked_notice = if kind == "destroy" { "Address permanently deleted.".into() } else { "Change saved.".into() };
                if kind == "create" { g.masked_new_desc.clear(); g.masked_new_site.clear(); }
                if kind == "destroy" { g.masked_open.clear(); }
            }
            Err(e) => {
                if e.forbidden() {
                    match kind { "create" => g.masked_no_create = true, "update" => g.masked_no_update = true, _ => g.masked_no_delete = true }
                }
                if e.unavailable() { g.masked.clear(); g.masked_state = 3; }
                if kind == "destroy" && !e.forbidden() { g.masked_needs_refresh = true; }
                g.masked_notice = crate::masked::notice(kind, &e).into();
            }
        }
        Ok(())
    }))
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

/// Forget everything the pages cached, secrets first: Settings was closed, or the account signed
/// out or changed. The security page's epoch moves on, so a job or load still running for the old
/// account drops its answer instead of writing it into the next page.
pub fn reset(cache: &Shared) {
    let mut g = cache.lock().unwrap_or_else(|e| e.into_inner());
    let epoch = g.sec.epoch + 1;
    // the identity generation keeps counting too: a save answered after the reset, once this
    // account is claimed again, must not close or touch the new form
    let generation = g.id_gen + 1;
    *g = Cache::default();
    g.sec.epoch = epoch;
    g.id_gen = generation;
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

/// Settings > Password & sign-in's masked addresses (Bulwark masked-email-settings.tsx): hidden
/// on an account without them. Its result is dropped when the page changed account or was reset.
fn load_masked(c: &Client, cache: &Shared) {
    let owner = claim(cache, &c.state_key(), &c.username, &c.base);
    let epoch = cache.lock().unwrap_or_else(|e| e.into_inner()).sec.epoch;
    // no management capability on this account: no section (Bulwark returns null)
    if crate::masked::context(c).is_none() {
        let mut k = cache.lock().unwrap_or_else(|e| e.into_inner());
        if k.owner == owner && k.sec.epoch == epoch { k.masked_shown = false; k.masked_state = 3; }
        return;
    }
    let r = crate::masked::list(c);
    let mut k = cache.lock().unwrap_or_else(|e| e.into_inner());
    // another account, or the page was reset (signed out, Settings closed) while it ran
    if k.owner != owner || k.sec.epoch != epoch { return; }
    match r {
        Ok(list) => {
            let pages = list.len().div_ceil(crate::masked::PAGE_SIZE).max(1);
            k.masked_page = k.masked_page.min(pages - 1);
            if !list.iter().any(|m| m.id == k.masked_open) { k.masked_open.clear(); }
            k.masked = list;
            (k.masked_state, k.masked_shown, k.masked_needs_refresh) = (1, true, false);
        }
        Err(e) => {
            k.masked.clear();
            k.masked_state = if e.unavailable() { 3 } else if e.forbidden() { 2 } else { 4 };
            if !e.unavailable() { k.masked_shown = true; }
        }
    }
}

/// Fetch what a page needs. Runs on a background thread.
pub fn load(tab: &str, c: &Client, cache: &Shared) {
    // typed passwords, codes and a secret just created are kept only while their page is open
    if tab != "security" { cache.lock().unwrap_or_else(|e| e.into_inner()).sec.forget_secrets(); }
    let res: Result<(), String> = (|| {
        match tab {
            "vacation" => {
                let acc = c.account_for("urn:ietf:params:jmap:vacationresponse");
                let r = c.call(json!([["VacationResponse/get", {"accountId": acc, "ids": ["singleton"]}, "v"]]))?;
                cache.lock().unwrap_or_else(|e| e.into_inner()).vacation = Some(r[0][1]["list"][0].clone());
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
                let mut k = cache.lock().unwrap_or_else(|e| e.into_inner());
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
                cache.lock().unwrap_or_else(|e| e.into_inner()).mailboxes = boxes;
            }
            "organize" => { cache.lock().unwrap_or_else(|e| e.into_inner()).mailboxes = c.mailboxes()?; }
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
                cache.lock().unwrap_or_else(|e| e.into_inner()).archived = items;
            }
            "calendars" => {
                let acc = c.account_for("urn:ietf:params:jmap:calendars");
                let r = c.call(json!([["Calendar/get", {"accountId": acc, "ids": null, "properties": ["id", "name", "color", "isDefault", "isVisible", "isSubscribed", "sortOrder", "myRights", "description"]}, "c"]]))?;
                if r[0][0] == "error" { return Err(crate::jmap::method_error(&r[0][1])); }
                cache.lock().unwrap_or_else(|e| e.into_inner()).calendars = sorted_calendars(r[0][1]["list"].as_array().map(|a| a.as_slice()).unwrap_or(&[]));
            }
            "addressbooks" => {
                let acc = c.account_for("urn:ietf:params:jmap:contacts");
                let r = c.call(json!([["AddressBook/get", {"accountId": acc, "ids": null, "properties": ["id", "name", "isDefault", "isSubscribed", "sortOrder", "myRights", "description"]}, "a"]]))?;
                if r[0][0] == "error" { return Err(crate::jmap::method_error(&r[0][1])); }
                let mut v = r[0][1]["list"].as_array().cloned().unwrap_or_default();
                v.sort_by_key(|b| (b["isDefault"] != true, b["name"].as_str().unwrap_or("").to_lowercase()));
                cache.lock().unwrap_or_else(|e| e.into_inner()).books = v;
            }
            "account" => {
                let owner = claim(cache, &c.state_key(), &c.username, &c.base);
                // the storage line is a courtesy: a server without quotas, or one that refuses, keeps the page
                let quota = c.quotas().unwrap_or_default();
                let ids = c.identities()?;
                let mut k = cache.lock().unwrap_or_else(|e| e.into_inner());
                // another account's load started since: this one is out of date
                if k.owner != owner { return Ok(()); }
                k.quota = quota;
                k.identities = ids;
                k.ids_loaded = true;
            }
            "composing" => {
                let owner = claim(cache, &c.state_key(), &c.username, &c.base);
                let ids = c.identities()?;
                // a failed load is an error on the page, not an empty list
                let tpls = crate::templates::list(c)?;
                let mut k = cache.lock().unwrap_or_else(|e| e.into_inner());
                if k.owner != owner { return Ok(()); }
                k.identities = ids;
                k.ids_loaded = true;
                k.templates = tpls;
                k.tpl_state = if crate::templates::supported(c) { 1 } else { 2 };
            }
            "security" => {
                // the masked addresses load even when the account security part fails
                let sec = crate::security::load(c, cache);
                load_masked(c, cache);
                sec?;
            }
            _ => {}
        }
        Ok(())
    })();
    let mut k = cache.lock().unwrap_or_else(|e| e.into_inner());
    // a failed load of another account's page does not speak for this one
    if matches!(tab, "account" | "composing" | "security") && k.owner != c.state_key() { return; }
    k.error = res.err().unwrap_or_default();
}

/// Rows for server-backed pages; `None` for pages this module doesn't own.
pub fn rows(tab: &str, cache: &Shared) -> Option<Vec<SRow>> {
    let k = cache.lock().unwrap_or_else(|e| e.into_inner());
    let mut out = vec![];
    if !k.error.is_empty() { out.push(row("value", "", "Could not load from the server", &k.error, "", false)); }
    let quota = if tab == "account" { quota_rows(&k.quota) } else { vec![] };
    match tab {
        "security" => {
            // hidden (None) for an account without Stalwart's management capability, and the
            // masked addresses only once the server answered for them (most servers have none)
            let sec = crate::security::rows(&k.sec);
            let masked = k.masked_shown.then(|| masked_rows(&k));
            if sec.is_none() && masked.is_none() { return None; }
            out.extend(sec.unwrap_or_default());
            out.extend(masked.unwrap_or_default());
            return Some(out);
        }
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
        "account" | "composing" if !k.identities.is_empty() || !quota.is_empty() || k.ids_loaded => {
            if tab == "account" { out.extend(quota); }
            let list = ordered_identities(&k);
            let edit_label = |id: &str| -> &str {
                if k.id_form.as_ref().is_some_and(|x| x.id == id) { "Editing" }
                else if k.confirm_delete == format!("identedit:{id}") { "Click again to discard what you typed" }
                else { "Edit" }
            };
            if tab == "account" && (k.ids_loaded || !k.identities.is_empty()) {
                // Bulwark's identity manager: the first is the primary (default) sender
                out.push(row("section", "", "Your addresses", "The addresses you can send from, each with its own name and signature.", "", false));
                if list.is_empty() { out.push(row("value", "", "No addresses yet", "", "", false)); }
                for (n, i) in list.iter().enumerate() {
                    let id = i["id"].as_str().unwrap_or("");
                    let email = i["email"].as_str().unwrap_or("");
                    let title = i["name"].as_str().filter(|x| !x.trim().is_empty()).unwrap_or(email);
                    out.push(row("button", &format!("server.ident.edit:{id}"), title, &crate::identities::summary(i, n == 0), edit_label(id), false));
                    if n > 0 && list.len() > 1 && !k.id_busy {
                        out.push(row("button", &format!("server.ident.primary:{id}"), "", "", "Set as primary", false));
                    }
                    // not the primary address (mail goes out from it), nor one the server keeps
                    if n > 0 && crate::identities::may_delete(i) && !k.id_busy {
                        out.push(row("button", &format!("server.ident.delete:{id}"), "", "", if k.confirm_delete == format!("ident:{id}") { "Click again to delete" } else { "Delete" }, false));
                    }
                }
                if k.id_busy && k.id_form.is_none() { out.push(row("value", "", "Deleting…", "", "", false)); }
                if k.id_form.is_none() && !k.id_busy { out.push(row("button", "server.ident.new", "", "", "Add address", false)); }
                identity_form_rows(&k, &mut out);
            }
            if tab == "composing" && !list.is_empty() {
                out.push(row("section", "", "Signatures", "Each address has its own signature, plain or formatted. It is added to new messages, replies and forwards per the options above.", "", false));
                for i in &list {
                    let id = i["id"].as_str().unwrap_or("");
                    let sig = crate::identities::plain_signature(i);
                    let line: String = sig.lines().next().unwrap_or("").chars().take(80).collect();
                    let kind = if i["htmlSignature"].as_str().is_some_and(|h| !h.trim().is_empty()) { " (formatted)" } else { "" };
                    out.push(row("button", &format!("server.ident.edit:{id}"), &format!("Signature for {}", i["email"].as_str().unwrap_or("")),
                        &if sig.is_empty() { "No signature".to_string() } else { format!("{line}{kind}") }, edit_label(id), false));
                }
                identity_form_rows(&k, &mut out);
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
    let mut k = cache.lock().unwrap_or_else(|e| e.into_inner());
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
    if let Some(field) = key.strip_prefix("server.ident.f.") {
        // the form is typed into locally; Save writes it (Bulwark identity-form.tsx). While a
        // save is on its way it takes no input (Bulwark disables it while submitting).
        if k.id_busy { return None; }
        let text = value.as_str().unwrap_or("").to_string();
        let mut next = k.id_gen;
        if let Some(x) = k.id_form.as_mut() {
            match field {
                "name" => x.f.name = text,
                "email" => x.f.email = text,
                "replyTo" => x.f.reply_to = text,
                "bcc" => x.f.bcc = text,
                "sig" => x.f.signature = text,
                "html" => {
                    let on = value.as_bool().unwrap_or(false);
                    if on != x.f.html { x.f.signature = crate::identities::switch_format(&x.f.signature, on); }
                    x.f.html = on;
                }
                _ => return None,
            }
            x.error.clear();
            next += 1;
        }
        k.id_gen = next;
        if k.confirm_delete.starts_with("ident") { k.confirm_delete.clear(); }
        return None;
    }
    match key {
        "server.masked.desc" => { k.masked_desc = value.as_str().unwrap_or("").into(); }
        "server.masked.newdesc" => { k.masked_new_desc = value.as_str().unwrap_or("").into(); }
        "server.masked.newsite" => { k.masked_new_site = value.as_str().unwrap_or("").into(); }
        _ => {}
    }
    None
}

/// A save or delete of an identity was answered (or refused before it went out). Only the page
/// of the account it was made for is touched: it takes input again, a saved form closes (so a
/// second Save cannot add the address again), a refused one shows why. `closes`: the identity
/// whose form a delete closes. `generation`: the page's `id_gen` when the change went out; a
/// late answer of an older one (the account was left and opened again meanwhile, so another
/// form or another change may be on the page) changes nothing.
pub fn identity_done(g: &mut Cache, owner: &str, generation: u64, r: &Result<(), String>, save: bool, closes: Option<&str>) {
    if g.owner != owner || g.id_gen != generation { return; }
    g.id_busy = false;
    g.id_deleting = false;
    match r {
        Ok(()) => {
            if save || g.id_form.as_ref().is_some_and(|x| Some(x.id.as_str()) == closes) { g.id_form = None; g.id_gen += 1; }
        }
        Err(e) => { if save { if let Some(x) = g.id_form.as_mut() { x.error = e.clone(); } } }
    }
}

/// The identity and masked-address buttons. None: not one of them.
fn identity_button(key: &str, cache: &Shared) -> Option<Option<Job>> {
    use crate::identities as ids;
    // one identity change at a time: nothing else of the identities while one is on its way
    if key.starts_with("server.ident.") && cache.lock().unwrap_or_else(|e| e.into_inner()).id_busy { return Some(None); }
    if key == "server.ident.new" || key == "server.ident.cancel" {
        let mut k = cache.lock().unwrap_or_else(|e| e.into_inner());
        // what is half typed is not thrown away by one click (Bulwark confirmDiscard)
        let ask = if key == "server.ident.new" { "identnew" } else { "identcancel" };
        if form_dirty(&k) && k.confirm_delete != ask { k.confirm_delete = ask.into(); return Some(noop()); }
        k.confirm_delete.clear();
        k.id_gen += 1;
        k.id_form = (key == "server.ident.new").then(IdForm::default);
        return Some(noop());
    }
    if let Some(id) = key.strip_prefix("server.ident.edit:") {
        let mut k = cache.lock().unwrap_or_else(|e| e.into_inner());
        if k.id_form.as_ref().is_some_and(|x| x.id == id) { return Some(noop()); }
        let ask = format!("identedit:{id}");
        if form_dirty(&k) && k.confirm_delete != ask { k.confirm_delete = ask; return Some(noop()); }
        k.confirm_delete.clear();
        let Some(old) = k.identities.iter().find(|i| i["id"] == id).cloned() else { return Some(noop()) };
        k.id_gen += 1;
        k.id_form = Some(IdForm { id: id.to_string(), f: ids::form_of(Some(&old), plain_only()), old: Some(old), error: String::new() });
        return Some(noop());
    }
    if key == "server.ident.save" {
        let (owner, form, plain, generation) = {
            let mut k = cache.lock().unwrap_or_else(|e| e.into_inner());
            let plain = plain_only();
            let Some(x) = k.id_form.as_mut() else { return Some(None) };
            if let Some(e) = ids::validate(&x.f, x.old.as_ref().filter(|_| !x.id.is_empty()), plain) { x.error = e; return Some(noop()); }
            x.error.clear();
            k.id_busy = true;
            (k.owner.clone(), k.id_form.clone().unwrap(), plain, k.id_gen)
        };
        return Some(Some(Box::new(move |c: &Client, k: &Shared| {
            let r = if c.state_key() != owner { Err(ACCOUNT_CHANGED.to_string()) } else {
                match form.old.as_ref().filter(|_| !form.id.is_empty()) {
                    Some(old) => ids::update(c, &form.id, ids::update_patch(old, &form.f, plain)),
                    None if form.id.is_empty() => ids::create(c, &form.f).map(|_| ()),
                    None => Err("This address is no longer in the list; reload and try again.".into()),
                }
            };
            identity_done(&mut k.lock().unwrap_or_else(|e| e.into_inner()), &owner, generation, &r, true, None);
            r
        })));
    }
    if let Some(id) = key.strip_prefix("server.ident.delete:") {
        let id = id.to_string();
        let mut k = cache.lock().unwrap_or_else(|e| e.into_inner());
        let list = ordered_identities(&k);
        let Some(pos) = list.iter().position(|i| i["id"] == id.as_str()) else { return Some(noop()) };
        if pos == 0 || !ids::may_delete(&list[pos]) { return Some(Some(Box::new(|_, _| Err("This identity cannot be deleted".into())))); }
        let ask = format!("ident:{id}");
        if k.confirm_delete != ask { k.confirm_delete = ask; return Some(noop()); }
        k.confirm_delete.clear();
        (k.id_busy, k.id_deleting) = (true, true);
        let (owner, generation) = (k.owner.clone(), k.id_gen);
        return Some(Some(Box::new(move |c: &Client, k: &Shared| {
            let r = if c.state_key() != owner { Err(ACCOUNT_CHANGED.to_string()) } else { ids::destroy(c, &id) };
            identity_done(&mut k.lock().unwrap_or_else(|e| e.into_inner()), &owner, generation, &r, false, Some(&id));
            r
        })));
    }
    if let Some(id) = key.strip_prefix("server.ident.primary:") {
        let id = id.to_string();
        let owner = {
            let k = cache.lock().unwrap_or_else(|e| e.into_inner());
            if !k.identities.iter().any(|i| i["id"] == id.as_str()) { return Some(noop()); }
            k.owner.clone()
        };
        return Some(Some(Box::new(move |c: &Client, _| {
            if c.state_key() != owner { return Err(ACCOUNT_CHANGED.into()); }
            // per account, as Bulwark keys it (JMAP identity ids are the account's own)
            let account = ids::account_key(&c.username, &c.base);
            crate::settings::update_pref(ids::KEY_PREFERRED, |v| ids::encode_preferred(v, &account, &id));
            Ok(())
        })));
    }
    // ---- masked addresses ----
    if let Some(id) = key.strip_prefix("server.masked.open:") {
        let mut k = cache.lock().unwrap_or_else(|e| e.into_inner());
        if k.masked_open == id { k.masked_open.clear(); } else {
            k.masked_desc = k.masked.iter().find(|m| m.id == id).and_then(|m| m.description.clone()).unwrap_or_default();
            k.masked_open = id.to_string();
        }
        k.masked_notice.clear();
        return Some(noop());
    }
    if let Some(id) = key.strip_prefix("server.masked.copy:") {
        let mut k = cache.lock().unwrap_or_else(|e| e.into_inner());
        let Some(email) = k.masked.iter().find(|m| m.id == id).map(|m| m.email.clone()) else { return Some(noop()) };
        k.masked_notice = if crate::mail::copy_to_clipboard(&email) { "Address copied.".into() } else { "Could not copy. Select and copy the address above.".into() };
        return Some(noop());
    }
    if key == "server.masked.prev" || key == "server.masked.next" {
        let mut k = cache.lock().unwrap_or_else(|e| e.into_inner());
        let pages = k.masked.len().div_ceil(crate::masked::PAGE_SIZE).max(1);
        k.masked_page = if key.ends_with("prev") { k.masked_page.saturating_sub(1) } else { (k.masked_page + 1).min(pages - 1) };
        k.masked_open.clear();
        return Some(noop());
    }
    if key == "server.masked.refresh" {
        let mut k = cache.lock().unwrap_or_else(|e| e.into_inner());
        if k.masked_busy { return Some(None); }
        k.masked_notice.clear();
        // the page reloads after every button
        return Some(noop());
    }
    if key == "server.masked.create" {
        let (desc, site, no) = { let k = cache.lock().unwrap_or_else(|e| e.into_inner()); (k.masked_new_desc.clone(), k.masked_new_site.clone(), k.masked_no_create) };
        if no { return Some(None); }
        return Some(masked_job(cache, "create", move |c| crate::masked::create(c, &desc, &site).map(|_| ())));
    }
    for (prefix, kind) in [("server.masked.savedesc:", "update"), ("server.masked.toggle:", "update"), ("server.masked.delete:", "destroy")] {
        let Some(id) = key.strip_prefix(prefix) else { continue };
        let id = id.to_string();
        let mut k = cache.lock().unwrap_or_else(|e| e.into_inner());
        // only an address of the list just read (Bulwark ownedIds)
        let Some(m) = k.masked.iter().find(|m| m.id == id).cloned() else { return Some(noop()) };
        if kind == "update" && k.masked_no_update { return Some(None); }
        if prefix == "server.masked.delete:" {
            if k.masked_no_delete || k.masked_needs_refresh || k.masked_busy { return Some(None); }
            let ask = format!("masked:{id}");
            if k.confirm_delete != ask { k.confirm_delete = ask; return Some(noop()); }
            k.confirm_delete.clear();
            drop(k);
            return Some(masked_job(cache, "destroy", move |c| crate::masked::destroy(c, &id)));
        }
        let desc = (prefix == "server.masked.savedesc:").then(|| k.masked_desc.clone());
        if desc.as_deref().is_some_and(|d| d == m.description.as_deref().unwrap_or("")) { return Some(None); }
        let enabled = (prefix == "server.masked.toggle:").then_some(!m.enabled);
        drop(k);
        return Some(masked_job(cache, "update", move |c| crate::masked::update(c, &id, desc.as_deref(), enabled)));
    }
    None
}

/// Button actions on server pages. Returns true when the page should reload from the server.
pub fn button(key: &str, cache: &Shared) -> Option<Box<dyn FnOnce(&Client, &Shared) -> Result<(), String> + Send>> {
    if let Some(job) = identity_button(key, cache) { return job; }
    if let Some(what) = key.strip_prefix("server.sec.") { return crate::security::button(&mut cache.lock().unwrap_or_else(|e| e.into_inner()).sec, what); }
    if key == "server.folder.create" {
        let name = cache.lock().unwrap_or_else(|e| e.into_inner()).new_folder.trim().to_string();
        if name.is_empty() { return None; }
        return Some(Box::new(move |c: &Client, k: &Shared| { c.create_mailbox(&name, None)?; k.lock().unwrap_or_else(|e| e.into_inner()).new_folder.clear(); Ok(()) }));
    }
    if let Some(id) = key.strip_prefix("server.folder.delete:") {
        let id = id.to_string();
        let mut k = cache.lock().unwrap_or_else(|e| e.into_inner());
        if k.confirm_delete != id { k.confirm_delete = id; return Some(Box::new(|_, _| Ok(()))); }
        k.confirm_delete.clear();
        return Some(Box::new(move |c: &Client, _| c.delete_mailbox(&id)));
    }
    if key == "server.cal.create" || key == "server.book.create" {
        let book = key == "server.book.create";
        let name = { let k = cache.lock().unwrap_or_else(|e| e.into_inner()); if book { k.new_book.trim().to_string() } else { k.new_cal.trim().to_string() } };
        if name.is_empty() { return None; }
        return Some(Box::new(move |c: &Client, k: &Shared| {
            let n = if book { k.lock().unwrap_or_else(|e| e.into_inner()).books.len() } else { k.lock().unwrap_or_else(|e| e.into_inner()).calendars.len() };
            let obj = if book { json!({"name": name}) } else { json!({"name": name, "color": CAL_COLORS[n % CAL_COLORS.len()].0, "isVisible": true}) };
            collection_set(c, book, json!({"create": {"n": obj}}))?;
            let mut g = k.lock().unwrap_or_else(|e| e.into_inner());
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
                let mut k = cache.lock().unwrap_or_else(|e| e.into_inner());
                if k.confirm_delete != id { k.confirm_delete = id; return Some(Box::new(|_, _| Ok(()))); }
                k.confirm_delete.clear();
                let args = if book { json!({"destroy": [id], "onDestroyRemoveContents": true}) } else { json!({"destroy": [id], "onDestroyRemoveEvents": true}) };
                return Some(Box::new(move |c: &Client, _| collection_set(c, book, args)));
            }
            _ => {}
        }
    }
    if key == "server.rule.add" {
        let k = cache.lock().unwrap_or_else(|e| e.into_inner());
        if k.nr_field < 5 && k.nr_value.trim().is_empty() { drop(k); cache.lock().unwrap_or_else(|e| e.into_inner()).script_status = "Add a value for the condition.".into(); return Some(Box::new(|_, _| Ok(()))); }
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
            Err(e) => { drop(k); cache.lock().unwrap_or_else(|e| e.into_inner()).script_status = e; return Some(Box::new(|_, _| Ok(()))); }
        }
        let mut rules = k.rules.clone();
        rules.push(r);
        let (script, scripts) = (crate::sieve::generate_meta(&crate::sieve::with_rules(&k.filter_meta, &rules), &k.external, &k.sieve_ext), k.scripts.clone());
        drop(k);
        return Some(Box::new(move |c: &Client, k: &Shared| {
            save_script(c, &script, &scripts)?;
            let mut g = k.lock().unwrap_or_else(|e| e.into_inner());
            g.nr_name.clear(); g.nr_value.clear(); g.nr_action_value.clear(); g.nr_from.clear(); g.nr_until.clear(); g.script_status = "Rule added.".into();
            Ok(())
        }));
    }
    if let Some(id) = key.strip_prefix("server.rule.delete:") {
        let id = id.to_string();
        let mut k = cache.lock().unwrap_or_else(|e| e.into_inner());
        if k.confirm_delete != id { k.confirm_delete = id; return Some(Box::new(|_, _| Ok(()))); }
        k.confirm_delete.clear();
        let rules: Vec<Value> = k.rules.iter().filter(|r| r["id"] != id.as_str()).cloned().collect();
        let (script, scripts) = (crate::sieve::generate_meta(&crate::sieve::with_rules(&k.filter_meta, &rules), &k.external, &k.sieve_ext), k.scripts.clone());
        return Some(Box::new(move |c: &Client, _| save_script(c, &script, &scripts)));
    }
    if key == "server.tpl.cancel" {
        clear_tpl_form(&mut cache.lock().unwrap_or_else(|e| e.into_inner()));
        return Some(Box::new(|_, _| Ok(())));
    }
    if let Some(id) = key.strip_prefix("server.tpl.edit:") {
        let mut k = cache.lock().unwrap_or_else(|e| e.into_inner());
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
            let k = cache.lock().unwrap_or_else(|e| e.into_inner());
            if !k.tpl_edit.is_empty() && k.tpl_old.is_none() { return Some(Box::new(|_, k: &Shared| { let e = "This template is no longer in the list; reload and try again.".to_string(); k.lock().unwrap_or_else(|e| e.into_inner()).tpl_error = e.clone(); Err(e) })); }
            (k.tpl_old.clone().filter(|_| !k.tpl_edit.is_empty()), form_fields(&k), k.tpl_gen)
        };
        return Some(Box::new(move |c: &Client, k: &Shared| {
            match crate::templates::save(c, old.as_ref(), &f) {
                // only when the form still holds what was saved (the user may have moved to another template)
                Ok(()) => { let mut k = k.lock().unwrap_or_else(|e| e.into_inner()); if k.tpl_gen == generation { clear_tpl_form(&mut k); } Ok(()) }
                Err(e) => { k.lock().unwrap_or_else(|e| e.into_inner()).tpl_error = e.clone(); Err(e) }
            }
        }));
    }
    if let Some(id) = key.strip_prefix("server.tpl.delete:") {
        let id = id.to_string();
        let mut k = cache.lock().unwrap_or_else(|e| e.into_inner());
        if k.confirm_delete != id { k.confirm_delete = id; return Some(Box::new(|_, _| Ok(()))); }
        k.confirm_delete.clear();
        return Some(Box::new(move |c: &Client, k: &Shared| {
            crate::templates::delete(c, &id)?;
            // the form was editing the template just deleted
            let mut k = k.lock().unwrap_or_else(|e| e.into_inner());
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
            k.lock().unwrap_or_else(|e| e.into_inner()).archived.retain(|x| x["id"] != id.as_str());
            Ok(())
        }));
    }
    if key == "server.sieve.save" {
        let text = cache.lock().unwrap_or_else(|e| e.into_inner()).script_text.clone();
        return Some(Box::new(move |c: &Client, k: &Shared| {
            let acc = c.account_for("urn:ietf:params:jmap:sieve");
            let (blob, _) = c.upload(text.into_bytes(), "application/sieve")?;
            let v = c.call(json!([["SieveScript/validate", {"accountId": acc, "blobId": blob}, "v"]]))?;
            if let Some(err) = v[0][1]["error"].as_object() {
                let msg = err.get("description").and_then(|d| d.as_str()).unwrap_or("invalid script").to_string();
                k.lock().unwrap_or_else(|e| e.into_inner()).script_status = format!("Not saved: {msg}");
                return Ok(());
            }
            let existing = {
                let g = k.lock().unwrap_or_else(|e| e.into_inner());
                g.scripts.iter().find(|x| x["isActive"] == true).or(g.scripts.first()).and_then(|x| x["id"].as_str().map(String::from))
            };
            let r = match existing {
                Some(id) => c.call(json!([["SieveScript/set", {"accountId": acc, "update": { id.clone(): {"blobId": blob} }, "onSuccessActivateScript": id}, "s"]]))?,
                None => c.call(json!([["SieveScript/set", {"accountId": acc, "create": {"n": {"name": "barbican", "blobId": blob}}, "onSuccessActivateScript": "#n"}, "s"]]))?,
            };
            let ok = r[0][1]["updated"].is_object() || r[0][1]["created"].is_object();
            k.lock().unwrap_or_else(|e| e.into_inner()).script_status = if ok { "Saved and active.".into() } else {
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
        { let mut k = c.lock().unwrap_or_else(|e| e.into_inner()); k.quota = quota; k.identities = identities; }
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
        assert!(r.iter().any(|x| x.label.as_str() == "Ann" && x.desc.as_str().starts_with("ann@example.test")));
    }

    fn keys(r: &[SRow]) -> Vec<String> { r.iter().filter(|x| x.kind.as_str() == "button").map(|x| format!("{}={}", x.key, x.text)).collect() }

    fn identities_page() -> Shared {
        let c = Shared::default();
        {
            let mut k = c.lock().unwrap_or_else(|e| e.into_inner());
            (k.owner, k.owner_user, k.owner_key, k.ids_loaded) = ("A".into(), "ann@example.test".into(), "ann@example.test@mail.example.test".into(), true);
            k.identities = vec![
                json!({"id": "team", "email": "team@example.test", "name": "Team", "mayDelete": false}),
                json!({"id": "ann", "email": "ann@example.test", "name": "Ann", "mayDelete": false}),
                json!({"id": "alias", "email": "sales@example.test", "name": "", "mayDelete": true, "bcc": [{"email": "copy@example.test"}]}),
            ];
        }
        c
    }

    #[test]
    fn the_identity_list_puts_the_login_first_and_guards_the_primary() {
        let r = rows("account", &identities_page()).unwrap();
        let b = keys(&r);
        // the login's own address is the primary: no Set as primary, no Delete
        assert_eq!(b[0], "server.ident.edit:ann=Edit");
        assert!(r.iter().any(|x| x.key.as_str() == "server.ident.edit:ann" && x.desc.as_str().contains("Primary")));
        assert!(!b.iter().any(|x| x.starts_with("server.ident.primary:ann") || x.starts_with("server.ident.delete:ann")));
        // the group address may be made primary but the server keeps it
        assert!(b.contains(&"server.ident.primary:team=Set as primary".to_string()));
        assert!(!b.iter().any(|x| x.starts_with("server.ident.delete:team")));
        assert!(b.contains(&"server.ident.delete:alias=Delete".to_string()));
        assert!(r.iter().any(|x| x.key.as_str() == "server.ident.edit:alias" && x.label.as_str() == "sales@example.test" && x.desc.as_str().contains("Bcc copy@example.test")));
        assert_eq!(b.last().unwrap(), "server.ident.new=Add address");
        // deleting the primary is refused before anything is sent; another asks first
        let c = identities_page();
        assert!(button("server.ident.delete:alias", &c).is_some());
        assert_eq!(c.lock().unwrap_or_else(|e| e.into_inner()).confirm_delete, "ident:alias");
        assert!(keys(&rows("account", &c).unwrap()).contains(&"server.ident.delete:alias=Click again to delete".to_string()));
    }

    #[test]
    fn the_identity_form_checks_before_saving_and_keeps_what_is_typed() {
        let c = identities_page();
        assert!(button("server.ident.new", &c).is_some());
        assert!(c.lock().unwrap_or_else(|e| e.into_inner()).id_form.as_ref().is_some_and(|x| x.id.is_empty()));
        edit("server.ident.f.email", &json!("not an address"), &c);
        edit("server.ident.f.bcc", &json!("copy@example.test, nope"), &c);
        // a refused form writes nothing: the job does nothing and the error is on the page
        assert!(button("server.ident.save", &c).is_some());
        let err = c.lock().unwrap_or_else(|e| e.into_inner()).id_form.as_ref().unwrap().error.clone();
        assert!(err.contains("valid email address") && err.contains("nope"), "{err}");
        assert!(rows("account", &c).unwrap().iter().any(|x| x.label.as_str() == "Not saved"));
        // typing clears the error; one click on another identity does not throw the input away
        edit("server.ident.f.email", &json!("new@example.test"), &c);
        assert!(c.lock().unwrap_or_else(|e| e.into_inner()).id_form.as_ref().unwrap().error.is_empty());
        button("server.ident.edit:alias", &c);
        assert!(c.lock().unwrap_or_else(|e| e.into_inner()).id_form.as_ref().unwrap().id.is_empty(), "the first click only asks");
        assert!(keys(&rows("account", &c).unwrap()).contains(&"server.ident.edit:alias=Click again to discard what you typed".to_string()));
        button("server.ident.edit:alias", &c);
        let k = c.lock().unwrap_or_else(|e| e.into_inner());
        let x = k.id_form.as_ref().unwrap();
        assert_eq!((x.id.as_str(), x.f.email.as_str(), x.f.bcc.as_str()), ("alias", "sales@example.test", "copy@example.test"));
    }

    #[test]
    fn a_second_save_waits_for_the_first() {
        let c = identities_page();
        button("server.ident.new", &c);
        edit("server.ident.f.email", &json!("new@example.test"), &c);
        // the first Save goes out and the page waits for it
        assert!(button("server.ident.save", &c).is_some());
        assert!(c.lock().unwrap_or_else(|e| e.into_inner()).id_busy);
        // a double click, typing, another form or a delete do nothing meanwhile
        assert!(button("server.ident.save", &c).is_none());
        edit("server.ident.f.email", &json!("other@example.test"), &c);
        assert_eq!(c.lock().unwrap_or_else(|e| e.into_inner()).id_form.as_ref().unwrap().f.email, "new@example.test");
        assert!(button("server.ident.cancel", &c).is_none() && button("server.ident.edit:alias", &c).is_none() && button("server.ident.delete:alias", &c).is_none());
        let r = rows("account", &c).unwrap();
        assert!(r.iter().any(|x| x.label.as_str() == "Saving…"));
        assert!(!r.iter().any(|x| x.key.starts_with("server.ident.f.") || x.key.as_str() == "server.ident.save" || x.key.starts_with("server.ident.delete:")));
        // an answer for another account's page changes nothing here
        let g0 = c.lock().unwrap_or_else(|e| e.into_inner()).id_gen;
        identity_done(&mut c.lock().unwrap_or_else(|e| e.into_inner()), "B", g0, &Ok(()), true, None);
        assert!(c.lock().unwrap_or_else(|e| e.into_inner()).id_busy);
        // a refused save gives the form back with the reason
        identity_done(&mut c.lock().unwrap_or_else(|e| e.into_inner()), "A", g0, &Err("You are not authorized to send from this email address.".into()), true, None);
        { let k = c.lock().unwrap_or_else(|e| e.into_inner()); assert!(!k.id_busy); assert!(k.id_form.as_ref().unwrap().error.contains("not authorized")); }
        // a saved one closes the form: Save cannot add it again
        assert!(button("server.ident.save", &c).is_some());
        let g1 = c.lock().unwrap_or_else(|e| e.into_inner()).id_gen;
        identity_done(&mut c.lock().unwrap_or_else(|e| e.into_inner()), "A", g1, &Ok(()), true, None);
        { let k = c.lock().unwrap_or_else(|e| e.into_inner()); assert!(!k.id_busy && k.id_form.is_none()); }
        assert!(button("server.ident.save", &c).is_none());
        // the account changed while it was out: the page takes input again
        button("server.ident.new", &c);
        edit("server.ident.f.email", &json!("x@example.test"), &c);
        button("server.ident.save", &c);
        let g2 = c.lock().unwrap_or_else(|e| e.into_inner()).id_gen;
        identity_done(&mut c.lock().unwrap_or_else(|e| e.into_inner()), "A", g2, &Err(ACCOUNT_CHANGED.into()), true, None);
        assert!(!c.lock().unwrap_or_else(|e| e.into_inner()).id_busy);
        // switching account clears it as well
        c.lock().unwrap_or_else(|e| e.into_inner()).id_busy = true;
        claim(&c, "B", "bob@example.test", "https://other.example.test");
        assert!(!c.lock().unwrap_or_else(|e| e.into_inner()).id_busy);
    }

    #[test]
    fn a_delete_waits_too_and_a_missing_may_delete_is_kept() {
        let c = identities_page();
        c.lock().unwrap_or_else(|e| e.into_inner()).identities.push(json!({"id": "nokey", "email": "old@example.test", "name": ""}));
        let b = keys(&rows("account", &c).unwrap());
        assert!(!b.iter().any(|x| x.starts_with("server.ident.delete:nokey")), "{b:?}");
        assert!(rows("account", &c).unwrap().iter().any(|x| x.key.as_str() == "server.ident.edit:nokey" && x.desc.as_str().contains("cannot be deleted")));
        // refused before anything is sent
        c.lock().unwrap_or_else(|e| e.into_inner()).confirm_delete = "ident:nokey".into();
        assert!(button("server.ident.delete:nokey", &c).is_some());
        assert!(!c.lock().unwrap_or_else(|e| e.into_inner()).id_busy);
        button("server.ident.delete:alias", &c);
        assert!(button("server.ident.delete:alias", &c).is_some());
        assert!(c.lock().unwrap_or_else(|e| e.into_inner()).id_busy);
        assert!(rows("account", &c).unwrap().iter().any(|x| x.label.as_str() == "Deleting…"));
        let g = c.lock().unwrap_or_else(|e| e.into_inner()).id_gen;
        identity_done(&mut c.lock().unwrap_or_else(|e| e.into_inner()), "A", g, &Ok(()), false, Some("alias"));
        assert!(!c.lock().unwrap_or_else(|e| e.into_inner()).id_busy && !c.lock().unwrap_or_else(|e| e.into_inner()).id_deleting);
        // a delete with the form open: the form says Deleting…, not Saving…
        button("server.ident.edit:team", &c);
        button("server.ident.delete:alias", &c);
        button("server.ident.delete:alias", &c);
        let r = rows("account", &c).unwrap();
        assert!(r.iter().any(|x| x.label.as_str() == "Deleting…") && !r.iter().any(|x| x.label.as_str() == "Saving…"));
    }

    #[test]
    fn a_late_answer_of_an_older_change_leaves_the_page_alone() {
        let c = identities_page();
        button("server.ident.new", &c);
        edit("server.ident.f.email", &json!("first@example.test"), &c);
        assert!(button("server.ident.save", &c).is_some());
        let first = c.lock().unwrap_or_else(|e| e.into_inner()).id_gen;
        // to another account and back before the answer, then a new form and its own save
        claim(&c, "B", "bob@example.test", "https://other.example.test");
        claim(&c, "A", "ann@example.test", "https://mail.example.test");
        c.lock().unwrap_or_else(|e| e.into_inner()).identities = identities_page().lock().unwrap_or_else(|e| e.into_inner()).identities.clone();
        button("server.ident.new", &c);
        edit("server.ident.f.email", &json!("second@example.test"), &c);
        assert!(button("server.ident.save", &c).is_some());
        let second = c.lock().unwrap_or_else(|e| e.into_inner()).id_gen;
        assert_ne!(first, second);
        // the first save's late answer, whatever it says: the second keeps its busy page and form
        identity_done(&mut c.lock().unwrap_or_else(|e| e.into_inner()), "A", first, &Ok(()), true, None);
        identity_done(&mut c.lock().unwrap_or_else(|e| e.into_inner()), "A", first, &Err("refused".into()), true, None);
        {
            let k = c.lock().unwrap_or_else(|e| e.into_inner());
            assert!(k.id_busy);
            let x = k.id_form.as_ref().unwrap();
            assert_eq!((x.f.email.as_str(), x.error.as_str()), ("second@example.test", ""));
        }
        identity_done(&mut c.lock().unwrap_or_else(|e| e.into_inner()), "A", second, &Ok(()), true, None);
        let k = c.lock().unwrap_or_else(|e| e.into_inner());
        assert!(!k.id_busy && k.id_form.is_none());
    }

    #[test]
    fn a_reset_forgets_the_identities_and_a_late_save_stays_out() {
        let c = identities_page();
        button("server.ident.new", &c);
        edit("server.ident.f.email", &json!("first@example.test"), &c);
        assert!(button("server.ident.save", &c).is_some());
        let first = c.lock().unwrap_or_else(|e| e.into_inner()).id_gen;
        {
            let mut k = c.lock().unwrap_or_else(|e| e.into_inner());
            (k.masked_shown, k.masked_state, k.masked_busy) = (true, 1, true);
            k.masked_new_desc = "Shop".into();
        }
        // signed out (or Settings closed): nothing of the page stays, nor is anything on its way
        reset(&c);
        {
            let k = c.lock().unwrap_or_else(|e| e.into_inner());
            assert!(k.owner.is_empty() && k.identities.is_empty() && !k.ids_loaded && k.id_form.is_none());
            assert!(!k.id_busy && !k.id_deleting && !k.masked_busy && !k.masked_shown && k.masked_new_desc.is_empty());
            assert!(k.id_gen > first, "the generation keeps counting across a reset");
        }
        assert!(rows("security", &c).is_none());
        // the same account again, a new form; the old save answers only now
        claim(&c, "A", "ann@example.test", "https://mail.example.test");
        button("server.ident.new", &c);
        edit("server.ident.f.email", &json!("second@example.test"), &c);
        identity_done(&mut c.lock().unwrap_or_else(|e| e.into_inner()), "A", first, &Ok(()), true, None);
        identity_done(&mut c.lock().unwrap_or_else(|e| e.into_inner()), "A", first, &Err("refused".into()), true, None);
        let k = c.lock().unwrap_or_else(|e| e.into_inner());
        let x = k.id_form.as_ref().expect("the new form stays open");
        assert_eq!((x.f.email.as_str(), x.error.as_str()), ("second@example.test", ""));
    }

    #[test]
    fn the_formatted_switch_converts_the_signature() {
        let c = identities_page();
        button("server.ident.edit:ann", &c);
        edit("server.ident.f.sig", &json!("Ann\nSales"), &c);
        edit("server.ident.f.html", &json!(true), &c);
        let sig = c.lock().unwrap_or_else(|e| e.into_inner()).id_form.as_ref().unwrap().f.signature.clone();
        assert!(sig.contains('<') && sig.contains("Sales"), "{sig}");
        let r = rows("account", &c).unwrap();
        assert!(r.iter().any(|x| x.key.as_str() == "server.ident.f.sig" && x.label.as_str() == "Signature (HTML)"));
        assert!(r.iter().any(|x| x.label.as_str() == "Plain-text version" && x.desc.as_str().contains("Sales")));
        edit("server.ident.f.html", &json!(false), &c);
        assert!(!c.lock().unwrap_or_else(|e| e.into_inner()).id_form.as_ref().unwrap().f.signature.contains('<'));
    }

    #[test]
    fn another_account_never_sees_or_saves_this_ones_identities() {
        let c = identities_page();
        button("server.ident.new", &c);
        edit("server.ident.f.email", &json!("typed@example.test"), &c);
        { let mut k = c.lock().unwrap_or_else(|e| e.into_inner()); k.masked = vec![crate::masked::Masked { id: "m1".into(), email: "m1@mask.example.test".into(), for_domain: None, description: None, enabled: true }]; (k.masked_state, k.masked_shown) = (1, true); }
        // the same account keeps everything
        claim(&c, "A", "ann@example.test", "https://mail.example.test");
        assert!(c.lock().unwrap_or_else(|e| e.into_inner()).id_form.is_some());
        // another account: the list, the form and the masked addresses go
        claim(&c, "B", "bob@example.test", "https://other.example.test");
        let k = c.lock().unwrap_or_else(|e| e.into_inner());
        assert!(k.identities.is_empty() && k.id_form.is_none() && k.masked.is_empty() && !k.masked_shown && !k.ids_loaded);
        assert_eq!((k.owner.as_str(), k.owner_key.as_str()), ("B", "bob@example.test@other.example.test"));
        drop(k);
        // nothing of A's shows for B while B's list loads
        assert!(rows("account", &c).is_none());
        assert!(rows("security", &c).is_none());
    }

    fn masked_page(n: usize) -> Shared {
        let c = Shared::default();
        {
            let mut k = c.lock().unwrap_or_else(|e| e.into_inner());
            k.owner = "A".into();
            k.masked = (0..n).map(|i| crate::masked::Masked { id: format!("m{i}"), email: format!("m{i}@mask.example.test"), for_domain: (i == 0).then(|| "shop.example".into()), description: None, enabled: i != 1 }).collect();
            (k.masked_state, k.masked_shown) = (1, true);
        }
        c
    }

    #[test]
    fn masked_addresses_page_by_ten_and_open_one_at_a_time() {
        let c = masked_page(12);
        let r = rows("security", &c).unwrap();
        assert!(r.iter().any(|x| x.label.as_str() == "12 addresses"));
        assert_eq!(r.iter().filter(|x| x.key.starts_with("server.masked.open:")).count(), 10);
        assert!(r.iter().any(|x| x.key.as_str() == "server.masked.open:m0" && x.desc.as_str() == "No description · shop.example · Enabled"));
        assert!(r.iter().any(|x| x.key.as_str() == "server.masked.open:m1" && x.desc.as_str().ends_with("Disabled")));
        assert!(r.iter().any(|x| x.label.as_str() == "Page 1 of 2"));
        button("server.masked.next", &c);
        let r = rows("security", &c).unwrap();
        assert_eq!(r.iter().filter(|x| x.key.starts_with("server.masked.open:")).count(), 2);
        assert!(!keys(&r).contains(&"server.masked.next=Next".to_string()));
        button("server.masked.open:m10", &c);
        let b = keys(&rows("security", &c).unwrap());
        assert!(b.contains(&"server.masked.toggle:m10=Disable".to_string()) && b.contains(&"server.masked.delete:m10=Delete".to_string()));
        // an id that is not in the list just read is not touched
        assert!(button("server.masked.toggle:zzz", &c).is_some());
        assert!(!c.lock().unwrap_or_else(|e| e.into_inner()).masked_busy);
        // a delete asks first, then runs; one change at a time
        button("server.masked.delete:m10", &c);
        assert!(!c.lock().unwrap_or_else(|e| e.into_inner()).masked_busy);
        assert!(keys(&rows("security", &c).unwrap()).contains(&"server.masked.delete:m10=Delete permanently".to_string()));
        assert!(button("server.masked.delete:m10", &c).is_some());
        assert!(c.lock().unwrap_or_else(|e| e.into_inner()).masked_busy);
        assert!(button("server.masked.toggle:m10", &c).is_none(), "busy: no second change");
    }

    #[test]
    fn masked_addresses_hidden_until_the_server_has_them() {
        let c = Shared::default();
        assert!(rows("security", &c).is_none());
        let c = masked_page(0);
        let r = rows("security", &c).unwrap();
        assert!(r.iter().any(|x| x.label.as_str() == "No masked addresses yet."));
        assert!(keys(&r).contains(&"server.masked.create=Create masked address".to_string()));
        // a delete that could not be confirmed is not offered again before a fresh read
        let c = masked_page(1);
        { let mut k = c.lock().unwrap_or_else(|e| e.into_inner()); k.masked_needs_refresh = true; k.masked_open = "m0".into(); }
        assert!(!keys(&rows("security", &c).unwrap()).iter().any(|x| x.starts_with("server.masked.delete:")));
        assert!(button("server.masked.delete:m0", &c).is_none());
    }
}
