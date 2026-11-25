//! Barbican's own settings sync. Bulwark keeps a login's settings as one encrypted blob on its
//! own web server (lib/settings-sync.ts, app/api/settings/route.ts) and pulls / pushes it from
//! the settings store (stores/settings-store.ts loadFromServer, enableSync, the auto-sync block).
//! Barbican has no web server, so the blob lives in the account itself: the Inbox's JMAP metadata
//! (`urn:ietf:params:jmap:metadata`, as the templates in src/templates.rs), under Barbican's own
//! namespace `org.barbican.settings` (not Bulwark's: the two apps' settings differ).
//!
//! * The blob is split over entries of at most 8 KB each (Stalwart's per-entry limit), named by a
//!   random generation (`part.<gen>.<i>`), plus a manifest naming the generation, the part count
//!   and the SHA-256 of the parts. Parts, manifest and the removal of the old generation go in one
//!   update of the Inbox (all or nothing), only in the Mailbox state the Inbox was read at; a
//!   reader that finds parts not matching the manifest takes it as "not ready" and keeps the
//!   local settings.
//! * What syncs is an allowlist: the settings schema's preferences and a few Bulwark-keyed values
//!   with no row ([`EXTRA`]), minus [`DENY`]: plugin consent, sign-ins, the device's state, the
//!   desktop and assistant settings, and whatever loads content or sends data somewhere.
//!   Pulled values are checked against their kind and bounded; anything else is dropped.
//! * The owner logic is Bulwark's loadFromServer: the local settings mirror one account's server
//!   copy; another account's pull starts from defaults (never pushes the first account's state
//!   under its name), and a push goes only to the account whose pull settled.
//! * Last writer wins on the blob (its `updatedAt`), except that a local change the server has not
//!   got yet is kept over an older server copy (Bulwark lib/settings-sync-stamps).
//! * `settingsSyncDisabled` (Bulwark's key) switches it off: no pull, no push.
use crate::jmap::Client;
use serde_json::{json, Map, Value};
use sha2::{Digest, Sha256};
use std::collections::HashMap;

const NS: &str = "org.barbican.settings";
const FORMAT: i64 = 1;
/// Stalwart's limit for one metadata value (the value's JSON in UTF-8).
pub const MAX_ENTRY_BYTES: usize = crate::templates::MAX_ENTRY_BYTES;
/// A mailbox holds at most 64 KB of shared metadata, shared with Bulwark's templates in the same
/// Inbox (lib/jmap/bulwark-metadata.ts:8), and a push briefly holds two generations: the blob is
/// kept to 20 KB in all (its parts' JSON), in at most 4 parts. Pulled data is held to the same.
pub const MAX_PARTS: usize = 4;
pub const MAX_BLOB_BYTES: usize = 20 * 1024;
const MAX_TEXT_CHARS: usize = 4096;
const MAX_VALUE_BYTES: usize = 32 * 1024;
const MAX_ITEMS: usize = 1000;
/// Bulwark waits 2 s (SYNC_DEBOUNCE_MS); a little longer here, as typing saves on every key.
#[cfg(not(test))]
const DEBOUNCE: std::time::Duration = std::time::Duration::from_secs(3);
pub const DISABLED_KEY: &str = "settingsSyncDisabled";

fn manifest_key() -> String { format!("{NS}.manifest") }
fn part_prefix() -> String { format!("{NS}.part.") }
fn part_key(gen: &str, i: usize) -> String { format!("{NS}.part.{gen}.{i}") }

// ---------------------------------------------------------------- what syncs (pure)

/// Never synced, and never applied from a server copy: code and plugin consent, the sync switch
/// itself, device state (Bulwark DEVICE_LOCAL_SETTING_KEYS), and settings that make this
/// computer send data, load remote content, drop a warning or a confirmation, or delete for good.
/// Whoever can write the account's metadata (a sharee of a shared Inbox too) could otherwise
/// switch them on this computer.
const DENY: &[&str] = &[
    // code, consent, the switch, device state
    "enabledPlugins", "pluginBindings", "pluginGrants", "pluginsOffNoticeShown", DISABLED_KEY, "proInterface", "doNotDisturb",
    "vacationCalendarBlocks", "pinnedFolders", "subAddressTags", "replyDockShare", "explicitSettings", "debugMode",
    // sends data somewhere, or tells senders something
    "pushRelayUrl", "phishingReportAddress", "readReceiptResponse", "requestReadReceiptDefault", "proofreading",
    // loads remote content
    "trustedSenders", "trustedSendersFrom", "trustedSendersAddressBook", "externalContentPolicy",
    "gravatarAvatars", "senderFavicons", "showAvatarsInJunk",
    // warnings and confirmations
    "externalRecipientConfirm", "externalSenderLabel", "sendConfirmation", "sendDelaySeconds", "confirmDelete",
    "confirmArchive", "confirmBulkDelete", "attachmentReminderEnabled", "emptySubjectWarningEnabled", "placeholderWarningEnabled",
    // deletes for good
    "deleteAction", "permanentlyDeleteJunk",
];

/// Whether `key` may never sync, whatever the lists say: any plugin key, any key of Barbican's
/// own desktop, assistant or server pages (they have a dot), and [`DENY`].
fn denied(key: &str) -> bool {
    key.is_empty() || key.contains('.') || key.contains(':') || key.to_ascii_lowercase().starts_with("plugin") || DENY.contains(&key)
}

/// Kept as this device has them once it holds several logins (Bulwark CROSS_ACCOUNT_VIEW_KEYS).
const CROSS_ACCOUNT: &[&str] = &["enableUnifiedMailbox", "includeGroupInUnified", "unifiedCrossAccount"];
/// Maps keyed by Bulwark's account id: a pull takes only the pulling account's entry, a push
/// carries only it (Bulwark PER_ACCOUNT_MAP_KEYS).
const PER_ACCOUNT: &[&str] = &["preferredIdentityIds"];

#[derive(Clone, Debug, PartialEq)]
enum Kind { Bool, Text, Num, Scalar, OneOf(Vec<Value>), List(&'static [&'static str]), Days, Retention, Delimiter, Obj, Arr }

/// Bulwark-keyed values Barbican keeps that have no schema row of their own.
const EXTRA: &[(&str, &str)] = &[
    ("theme", "oneof"), ("hoverActions", "hover"), ("hoverActionsMode", "oneof"), ("hoverActionsCorner", "oneof"),
    ("calendarWorkingDays", "days"), ("calendarDayStartHour", "oneof"),
    ("showAdvancedSettings", "bool"), ("showAccountDot", "bool"),
    ("calendarNarrowWeekAllDays", "bool"), ("vacationBlockCalendar", "bool"), ("spellCheck", "bool"),
    ("birthdayCalendarColor", "text"),
    ("emailKeywords", "arr"), ("sharedCalendarColors", "obj"), ("folderIcons", "obj"), ("folderCountModes", "obj"),
    ("notificationMutes", "obj"), ("preferredIdentityIds", "obj"),
    // Settings > Files (src/files_view.rs normalises what it reads)
    ("filesSettings", "obj"),
];

fn kind_of_default(d: &Value) -> Kind {
    match d { Value::String(_) => Kind::Text, Value::Number(_) => Kind::Num, Value::Bool(_) => Kind::Bool, _ => Kind::Scalar }
}

/// Every synced key and what its value must be: the schema's rows (a toggle a boolean, a select
/// one of Barbican's own options or the type of its default, a text a string), then [`EXTRA`]
/// and the accessibility keys, minus everything [`denied`].
fn kinds() -> &'static HashMap<String, Kind> {
    static K: std::sync::OnceLock<HashMap<String, Kind>> = std::sync::OnceLock::new();
    K.get_or_init(|| {
        let mut m = HashMap::new();
        let schema = crate::settings::schema_value();
        for t in schema["tabs"].as_array().into_iter().flatten() {
            for r in t["rows"].as_array().into_iter().flatten() {
                let Some(key) = r["key"].as_str().filter(|k| !denied(k)) else { continue };
                let d = r.get("default").cloned().unwrap_or(Value::Null);
                let k = match r["kind"].as_str().unwrap_or("") {
                    "toggle" => if d.is_string() { Kind::Text } else { Kind::Bool },
                    "select" => match crate::settings::option_values(key) { Some(o) => Kind::OneOf(o), None => kind_of_default(&d) },
                    "text" if crate::settings::is_retention_key(key) => Kind::Retention,
                    "text" | "info" => Kind::Text,
                    _ => continue,
                };
                let k = if key == "subAddressDelimiter" { Kind::Delimiter } else { k };
                m.entry(key.to_string()).or_insert(k);
            }
        }
        for (key, t) in EXTRA {
            if denied(key) { continue; }
            let k = match *t {
                "bool" => Kind::Bool, "text" => Kind::Text, "days" => Kind::Days, "obj" => Kind::Obj, "arr" => Kind::Arr,
                "hover" => Kind::List(&["delete", "star", "markRead", "archive", "spam", "tag"]),
                _ if *key == "theme" => Kind::OneOf(vec![json!("light"), json!("dark"), json!("system")]),
                _ => crate::settings::option_values(key).map(Kind::OneOf).unwrap_or(Kind::Scalar),
            };
            m.insert(key.to_string(), k);
        }
        for (key, d) in crate::a11y::DEFAULTS {
            if denied(key) { continue; }
            let k = match *d { "true" | "false" => Kind::Bool, n if n.parse::<f64>().is_ok() => Kind::Num, _ => Kind::Text };
            m.entry(key.to_string()).or_insert(k);
        }
        m
    })
}

/// Whether `key` is carried by the sync.
pub fn is_synced(key: &str) -> bool { !denied(key) && kinds().contains_key(key) }

fn same_number(a: &Value, b: &Value) -> bool { matches!((a.as_f64(), b.as_f64()), (Some(x), Some(y)) if x == y) }

fn text_ok(v: &Value) -> bool { v.as_str().is_some_and(|s| s.chars().count() <= MAX_TEXT_CHARS) }

/// Whether a pulled value has the shape its key needs (bounded).
fn fits(k: &Kind, v: &Value) -> bool {
    match k {
        Kind::Bool => v.is_boolean(),
        Kind::Text => text_ok(v),
        Kind::Num => v.as_f64().is_some_and(f64::is_finite),
        Kind::Scalar => v.is_boolean() || v.as_f64().is_some_and(f64::is_finite) || text_ok(v),
        Kind::OneOf(o) => o.iter().any(|x| x == v || same_number(x, v) || (x.is_string() && v.is_number() && x.as_str() == Some(v.to_string().as_str()))),
        Kind::List(ids) => v.as_array().is_some_and(|a| a.len() <= ids.len() && a.iter().all(|x| x.as_str().is_some_and(|s| ids.contains(&s)))),
        Kind::Days => v.as_array().is_some_and(|a| a.len() <= 7 && a.iter().all(|x| x.as_i64().is_some_and(|d| (0..7).contains(&d)))),
        Kind::Retention => v.as_i64().is_some_and(|n| (0..=crate::settings::RETENTION_MAX).contains(&n)),
        Kind::Delimiter => v.as_str().is_some_and(crate::identities::valid_delimiter),
        Kind::Obj => v.as_object().is_some_and(|o| o.len() <= MAX_ITEMS) && crate::templates::value_bytes(v) <= MAX_VALUE_BYTES,
        Kind::Arr => v.as_array().is_some_and(|a| a.len() <= MAX_ITEMS) && crate::templates::value_bytes(v) <= MAX_VALUE_BYTES,
    }
}

/// A pulled settings object with only synced keys of the right kind. `null` stays: it means the
/// setting is at its default on the device that wrote it.
pub fn validate(raw: &Map<String, Value>) -> Map<String, Value> {
    let mut out = Map::new();
    for (k, v) in raw {
        let Some(kind) = kinds().get(k.as_str()).filter(|_| !denied(k)) else { continue };
        if v.is_null() || fits(kind, v) { out.insert(k.clone(), v.clone()); }
    }
    out
}

/// The synced keys `prefs` holds, as they are (what a local change is measured against).
fn view(prefs: &Map<String, Value>) -> Map<String, Value> {
    prefs.iter().filter(|(k, _)| is_synced(k)).map(|(k, v)| (k.clone(), v.clone())).collect()
}

/// What a push carries for `acct` (Bulwark's account id): every synced key, `null` for one at
/// its default, a per-account map with only `acct`'s entry.
pub fn blob_for(prefs: &Map<String, Value>, acct: &str) -> Map<String, Value> {
    let mut keys: Vec<&String> = kinds().keys().collect();
    keys.sort();
    let mut out = Map::new();
    for k in keys {
        if denied(k) { continue; }
        if PER_ACCOUNT.contains(&k.as_str()) {
            if let Some(e) = prefs.get(k).and_then(|m| m.get(acct)) { out.insert(k.clone(), json!({ acct: e })); }
            continue;
        }
        out.insert(k.clone(), prefs.get(k).cloned().unwrap_or(Value::Null));
    }
    out
}

/// Put a server copy over `prefs`. A key changed here after `server_ms` (and not pushed yet) keeps
/// the local value; one the server changed later wins and is no longer pending. A per-account
/// map takes only `acct`'s entry; with several logins the cross-account views stay as they are.
/// Returns the keys whose value changed.
fn merge(prefs: &mut Map<String, Value>, incoming: &Map<String, Value>, dirty: &mut Map<String, Value>, server_ms: i64, acct: &str, keep_cross: bool) -> Vec<String> {
    let mut changed = vec![];
    for (k, v) in incoming {
        if denied(k) || !is_synced(k) { continue; }
        if keep_cross && CROSS_ACCOUNT.contains(&k.as_str()) { continue; }
        if dirty.get(k).and_then(Value::as_i64).is_some_and(|t| t > server_ms) { continue; }
        dirty.remove(k);
        if PER_ACCOUNT.contains(&k.as_str()) {
            let Some(e) = v.get(acct) else { continue };
            let mut m = prefs.get(k).cloned().filter(Value::is_object).unwrap_or_else(|| json!({}));
            if m.get(acct) != Some(e) { m[acct] = e.clone(); prefs.insert(k.clone(), m); changed.push(k.clone()); }
            continue;
        }
        let before = prefs.get(k).cloned();
        if v.is_null() { prefs.remove(k); } else { prefs.insert(k.clone(), v.clone()); }
        if prefs.get(k) != before.as_ref() { changed.push(k.clone()); }
    }
    changed
}

/// Bulwark resetSyncedState: another account's synced settings go back to their defaults (the
/// per-account maps keep the other logins' entries, which are never pushed elsewhere).
fn reset_synced(prefs: &mut Map<String, Value>, keep_cross: bool) -> bool {
    let before = prefs.len();
    prefs.retain(|k, _| !is_synced(k) || PER_ACCOUNT.contains(&k.as_str()) || (keep_cross && CROSS_ACCOUNT.contains(&k.as_str())));
    prefs.len() != before
}

// ---------------------------------------------------------------- the stored blob (pure)

/// One character's size inside a JSON string (serde_json's escapes).
fn escaped_len(c: char) -> usize {
    match c { '"' | '\\' | '\u{08}' | '\u{0c}' | '\n' | '\r' | '\t' => 2, c if (c as u32) < 0x20 => 6, c => c.len_utf8() }
}

/// `text` split into strings whose JSON (quotes and escapes included) fits one entry.
pub fn chunk(text: &str) -> Vec<String> {
    let budget = MAX_ENTRY_BYTES - 2;
    let mut out = vec![];
    let mut cur = String::new();
    let mut used = 0;
    for c in text.chars() {
        let n = escaped_len(c);
        if used + n > budget { out.push(std::mem::take(&mut cur)); used = 0; }
        cur.push(c);
        used += n;
    }
    if !cur.is_empty() || out.is_empty() { out.push(cur); }
    out
}

fn sha256_hex(s: &str) -> String { Sha256::digest(s.as_bytes()).iter().map(|b| format!("{b:02x}")).collect() }

fn ms_of(at: &str) -> Option<i64> { chrono::DateTime::parse_from_rfc3339(at).ok().map(|d| d.timestamp_millis()) }

fn iso(ms: i64) -> String {
    chrono::DateTime::from_timestamp_millis(ms).unwrap_or_default().to_rfc3339_opts(chrono::SecondsFormat::Millis, true)
}

/// A generation id fits Bulwark's metadata key rule (src/templates.rs valid_key) and is short.
fn good_gen(g: &str) -> bool { (1..=16).contains(&g.len()) && g.bytes().all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b)) }

/// The entries to write for `settings`: the parts (written first) and the manifest.
pub fn encode(settings: &Map<String, Value>, updated: &str, gen: &str) -> Result<(Vec<(String, Value)>, Value), String> {
    let text = json!({"format": FORMAT, "settings": settings}).to_string();
    if text.len() > MAX_BLOB_BYTES { return Err(format!("Your settings are too big to sync ({} of {} bytes).", text.len(), MAX_BLOB_BYTES)); }
    let parts = chunk(&text);
    let stored: usize = parts.iter().map(|p| crate::templates::value_bytes(&json!(p))).sum();
    if parts.len() > MAX_PARTS || stored > MAX_BLOB_BYTES { return Err(format!("Your settings are too big to sync ({stored} of {MAX_BLOB_BYTES} bytes).")); }
    let manifest = json!({"format": FORMAT, "updatedAt": updated, "gen": gen, "parts": parts.len(), "bytes": text.len(), "sha256": sha256_hex(&text)});
    Ok((parts.into_iter().enumerate().map(|(i, p)| (part_key(gen, i), Value::String(p))).collect(), manifest))
}

#[derive(Clone, Debug, PartialEq)]
pub struct Manifest { pub updated: String, pub updated_ms: i64, pub gen: String }

#[derive(Clone, Debug, PartialEq)]
pub enum Remote {
    /// No manifest: nothing was stored yet.
    Nothing,
    /// A manifest whose parts are missing, do not match its hash, or are not a blob this version
    /// reads (a write in progress, or a newer format): the local settings stay.
    NotReady,
    /// A manifest of a newer format (a newer Barbican wrote it): neither applied nor overwritten.
    Newer,
    Blob { manifest: Manifest, settings: Map<String, Value> },
}

/// The blob in an Inbox metadata object, checked against its manifest and validated.
pub fn decode(meta: &Value) -> Remote {
    let m = &meta[manifest_key()];
    if m.is_null() { return Remote::Nothing; }
    if m["format"].as_i64().is_some_and(|f| f > FORMAT) { return Remote::Newer; }
    let (Some(gen), Some(parts), Some(updated), Some(sha)) = (m["gen"].as_str(), m["parts"].as_u64(), m["updatedAt"].as_str(), m["sha256"].as_str()) else { return Remote::NotReady };
    let Some(updated_ms) = ms_of(updated) else { return Remote::NotReady };
    if m["format"].as_i64() != Some(FORMAT) || !good_gen(gen) || parts == 0 || parts as usize > MAX_PARTS { return Remote::NotReady; }
    let mut text = String::new();
    for i in 0..parts as usize {
        let Some(p) = meta[part_key(gen, i)].as_str() else { return Remote::NotReady };
        text.push_str(p);
        if text.len() > MAX_BLOB_BYTES { return Remote::NotReady; }
    }
    if sha256_hex(&text) != sha.to_ascii_lowercase() { return Remote::NotReady; }
    let Ok(v) = serde_json::from_str::<Value>(&text) else { return Remote::NotReady };
    let Some(s) = v["settings"].as_object().filter(|_| v["format"].as_i64() == Some(FORMAT)) else { return Remote::NotReady };
    Remote::Blob { manifest: Manifest { updated: updated.to_string(), updated_ms, gen: gen.to_string() }, settings: validate(s) }
}

/// Our part entries in an Inbox metadata object (to drop the old ones once a new blob is in).
fn part_keys(meta: &Value) -> Vec<String> {
    let p = part_prefix();
    meta.as_object().map(|o| o.keys().filter(|k| k.starts_with(&p)).cloned().collect()).unwrap_or_default()
}


// ---------------------------------------------------------------- this device's sync state (pure)

/// Kept in settings-sync.json next to prefs.json (Bulwark keeps the same in localStorage:
/// `settings-sync-owner` and `settings-sync-stamps`).
#[derive(Clone, Debug, Default, PartialEq)]
pub struct DeviceState {
    /// The account whose server copy the local settings mirror (`Client::state_key`); None on a
    /// device that never synced, whose settings are its own and are adopted by the first sign-in.
    pub owner: Option<String>,
    /// The local settings are no clean copy of the owner's: its pull failed while another
    /// account's settings were here, or a synced setting changed while an account they are not
    /// of was signed in. Nothing is pushed, and the next pull starts over from defaults.
    pub blocked: bool,
    /// The `updatedAt` of the owner's server copy last applied or written here.
    pub base: Option<String>,
    /// Synced keys changed here that the server has not taken yet: key -> when (ms).
    pub dirty: Map<String, Value>,
    /// The account signed in now (in memory only).
    active: Option<String>,
    /// The synced values as last seen (in memory only): what a save is compared with.
    seen: Option<Map<String, Value>>,
    disabled: Option<bool>,
}

/// What a local save changed.
#[derive(Debug, Default, PartialEq)]
pub struct Saved {
    /// A change of the owner's was recorded (to push).
    pub dirty: bool,
    /// The settings stopped being the owner's clean copy (the holder or the block changed).
    pub holder: bool,
    pub toggled: Option<bool>,
}

/// What a settled pull did, and what comes next.
#[derive(Debug, Default, PartialEq)]
pub struct Settled { pub changed: bool, pub push: bool, pub blocked: bool, pub owner_changed: bool }

/// A push ready to write.
#[derive(Clone, Debug, PartialEq)]
pub struct Push { pub blob: Map<String, Value>, pub sent: Map<String, Value>, pub updated: String, pub changed: bool }

impl DeviceState {
    pub fn from_json(v: &Value) -> DeviceState {
        DeviceState {
            owner: v["owner"].as_str().map(String::from), blocked: v["blocked"] == true, base: v["base"].as_str().map(String::from),
            dirty: v["dirty"].as_object().map(|d| d.iter().filter(|(k, t)| is_synced(k) && t.is_i64()).map(|(k, t)| (k.clone(), t.clone())).collect()).unwrap_or_default(),
            ..Default::default()
        }
    }

    pub fn to_json(&self) -> Value { json!({"owner": self.owner, "blocked": self.blocked, "base": self.base, "dirty": self.dirty}) }

    /// The values the device starts with (no change is recorded for them).
    pub fn start(&mut self, prefs: &Map<String, Value>) {
        self.seen = Some(view(prefs));
        self.disabled = Some(prefs.get(DISABLED_KEY) == Some(&json!(true)));
    }

    /// The account signed in now (`Client::state_key`), None when nobody is.
    pub fn activate(&mut self, who: Option<&str>) { self.active = who.map(String::from); }

    /// A change made now is the owner's: its account is signed in and the settings are its clean copy.
    pub fn tracking(&self) -> bool { self.active.is_some() && self.active == self.owner && !self.blocked }

    /// A save of `prefs` at `now_ms`. A changed synced key is the owner's change (to push) only
    /// while [`tracking`](Self::tracking); else the settings are blocked (never pushed anywhere;
    /// the next pull starts over from defaults), held by the signed-in account if there is one.
    pub fn saved(&mut self, prefs: &Map<String, Value>, now_ms: i64) -> Saved {
        let mut out = Saved::default();
        let disabled = prefs.get(DISABLED_KEY) == Some(&json!(true));
        if self.disabled.is_some_and(|d| d != disabled) { out.toggled = Some(disabled); }
        self.disabled = Some(disabled);
        let now = view(prefs);
        let Some(seen) = self.seen.replace(now.clone()) else { return out };
        let changed: Vec<String> = seen.keys().chain(now.keys()).filter(|k| seen.get(*k) != now.get(*k)).cloned().collect();
        if changed.is_empty() { return out; }
        if self.tracking() {
            for k in changed { self.dirty.insert(k, json!(now_ms)); }
            out.dirty = true;
            return out;
        }
        match self.active.clone() {
            Some(a) if self.owner.as_deref() != Some(a.as_str()) => {
                self.owner = Some(a);
                self.base = None;
                self.dirty.clear();
            }
            Some(_) => return out, // already blocked for this account
            // nobody signed in (after "add account", a sign-out, before a restore): the owner
            // stays, blocked, so whichever account pulls next starts from defaults; the waiting
            // changes stay as they were (the reset of that pull drops them)
            None if self.blocked => return out,
            None => {}
        }
        self.blocked = true;
        out.holder = true;
        out
    }

    /// Bulwark loadFromServer for `owner` (and its Bulwark account id `acct`): `remote` is the
    /// pull's outcome. The caller saves `prefs` when `changed`, then pushes when `push`.
    pub fn settle(&mut self, prefs: &mut Map<String, Value>, owner: &str, acct: &str, remote: Result<Remote, String>, keep_cross: bool) -> Settled {
        let before = self.owner.clone();
        // the settings here are another account's, or no clean copy of this one's
        let foreign = self.owner.as_deref().is_some_and(|o| o != owner) || self.blocked;
        let mut out = Settled::default();
        if foreign {
            out.changed = reset_synced(prefs, keep_cross);
            self.dirty.clear();
            self.base = None;
        }
        self.owner = Some(owner.to_string());
        match remote {
            // this one's could not be read: defaults, and no push for it, rather than pushing the
            // other account's settings (or changes made while they were here) under its name
            Err(_) | Ok(Remote::NotReady | Remote::Newer) => { self.blocked = foreign; out.blocked = foreign; }
            Ok(Remote::Nothing) => {
                // a new account starts from defaults, not from what another left here (#1185)
                self.blocked = false;
                self.base = None;
                out.push = !foreign;
            }
            Ok(Remote::Blob { manifest, settings }) => {
                self.blocked = false;
                let newer = self.base.as_deref().and_then(ms_of).is_none_or(|b| b < manifest.updated_ms);
                if newer {
                    out.changed |= !merge(prefs, &settings, &mut self.dirty, manifest.updated_ms, acct, keep_cross).is_empty();
                    self.base = Some(manifest.updated);
                }
                out.push = !self.dirty.is_empty();
            }
        }
        out.owner_changed = before != self.owner;
        self.seen = Some(view(prefs));
        out
    }

    /// Before a push for `owner`: a server copy newer than [`base`](Self::base) is merged in first
    /// (local unsent changes stay). None when the local settings are not `owner`'s clean copy
    /// (never push one account's state under another), or the server holds a newer format.
    pub fn prepare(&mut self, prefs: &mut Map<String, Value>, owner: &str, acct: &str, remote: &Remote, now_ms: i64, keep_cross: bool) -> Option<Push> {
        if self.owner.as_deref() != Some(owner) || self.blocked || *remote == Remote::Newer { return None; }
        let mut changed = false;
        let mut floor = self.base.as_deref().and_then(ms_of).unwrap_or(0);
        if let Remote::Blob { manifest, settings } = remote {
            if self.base.as_deref().and_then(ms_of).is_none_or(|b| b < manifest.updated_ms) {
                changed = !merge(prefs, settings, &mut self.dirty, manifest.updated_ms, acct, keep_cross).is_empty();
                self.base = Some(manifest.updated.clone());
            }
            floor = floor.max(manifest.updated_ms);
        }
        self.seen = Some(view(prefs));
        // never older than the copy it replaces, so other devices take it
        Some(Push { blob: blob_for(prefs, acct), sent: self.dirty.clone(), updated: iso(now_ms.max(floor + 1)), changed })
    }

    /// The server took a push: its changes are no longer only here (Bulwark acknowledgeChanges).
    pub fn pushed(&mut self, owner: &str, p: &Push) {
        if self.owner.as_deref() != Some(owner) || self.blocked { return; }
        self.base = Some(p.updated.clone());
        for (k, t) in &p.sent { if self.dirty.get(k) == Some(t) { self.dirty.remove(k); } }
    }

    /// The last account signed out with the sync on (Bulwark forgetSyncedSettings): its synced
    /// settings go back to defaults and the device is nobody's. Whether anything was done.
    pub fn forget(&mut self, prefs: &mut Map<String, Value>, owner: &str, disabled: bool) -> bool {
        if disabled || self.owner.as_deref() != Some(owner) { return false; }
        reset_synced(prefs, false);
        *self = DeviceState { active: self.active.take(), disabled: self.disabled, ..Default::default() };
        self.seen = Some(view(prefs));
        true
    }
}

// ---------------------------------------------------------------- requests

/// Whether the server keeps metadata (the sync needs it).
pub fn supported(c: &Client) -> bool { c.has("urn:ietf:params:jmap:metadata") }

fn inbox(c: &Client) -> Result<String, String> { c.role_ids()?.get("inbox").cloned().ok_or_else(|| "No Inbox.".into()) }

/// What a read of the Inbox found: the blob, our part entries there now, and the Mailbox state
/// it was read at (the write that follows goes only in that state).
#[derive(Debug)]
pub struct Fetched { pub remote: Remote, pub parts: Vec<String>, pub state: Option<String> }

/// The stored blob. An Inbox that is not returned, or returned without its metadata, is an
/// error, not "nothing stored".
pub fn fetch(c: &Client) -> Result<Fetched, String> {
    let id = inbox(c)?;
    let r = c.call(json!([["Mailbox/get", {"accountId": c.account, "ids": [id.clone()], "properties": ["metadata"]}, "m"]]))?;
    let Some(item) = r.first().and_then(|a| a[1]["list"].as_array()).and_then(|l| l.iter().find(|m| m["id"] == id.as_str())) else {
        return Err("The server did not return the Inbox.".into());
    };
    let Some(meta) = item.get("metadata") else { return Err("The server did not return the Inbox's metadata.".into()) };
    Ok(Fetched { remote: decode(meta), parts: part_keys(meta), state: r[0][1]["state"].as_str().map(String::from) })
}

fn new_gen() -> String {
    let mut b = [0u8; 4];
    let _ = getrandom::getrandom(&mut b);
    b.iter().map(|x| format!("{x:02x}")).collect()
}

#[derive(Debug, PartialEq)]
enum StoreError {
    /// The Inbox changed since it was read (another computer, ifInState).
    Changed,
    Failed(String),
}

/// One Mailbox/set answer, or why the Inbox was not updated.
fn set_answer(r: &[Value], inbox: &str) -> Result<(), StoreError> {
    let Some(a) = r.first() else { return Err(StoreError::Failed("The server did not answer.".into())) };
    if a[0] == "error" {
        return Err(if a[1]["type"] == "stateMismatch" { StoreError::Changed } else { StoreError::Failed(crate::jmap::method_error(&a[1])) });
    }
    if let Some(e) = a[1]["notUpdated"].get(inbox) { return Err(StoreError::Failed(crate::jmap::set_error(e))); }
    Ok(())
}

/// Write `settings` as a new generation in one update of the Inbox: its parts, the manifest
/// naming them and the removal of the parts in `old` (another generation's). One object's update
/// is all or nothing (RFC 8620 §5.3), so only one generation is ever left. `guard`: the Mailbox
/// state the Inbox was read at (ifInState), so a write from another computer since is not
/// overwritten unseen.
fn store(c: &Client, settings: &Map<String, Value>, updated: &str, old: &[String], guard: Option<&str>) -> Result<(), StoreError> {
    store_gen(c, settings, updated, old, &new_gen(), guard)
}

fn store_gen(c: &Client, settings: &Map<String, Value>, updated: &str, old: &[String], gen: &str, guard: Option<&str>) -> Result<(), StoreError> {
    let (parts, manifest) = encode(settings, updated, gen).map_err(StoreError::Failed)?;
    let inbox = inbox(c).map_err(StoreError::Failed)?;
    let mut patch = Map::new();
    let fresh = format!("{}{gen}.", part_prefix());
    for k in old { if !k.starts_with(&fresh) { patch.insert(format!("metadata/{k}"), Value::Null); } }
    for (k, v) in &parts { patch.insert(format!("metadata/{k}"), v.clone()); }
    patch.insert(format!("metadata/{}", manifest_key()), manifest);
    let mut args = json!({"accountId": c.account, "update": { inbox.clone(): patch }});
    if let Some(s) = guard { args["ifInState"] = json!(s); }
    let r = c.request(json!([["Mailbox/set", args, "s"]])).map_err(StoreError::Failed)?;
    set_answer(&r, &inbox)
}

const NEWER: &str = "Another computer synced these settings with a newer Barbican. Update Barbican here to sync again.";
/// Writes made only in the state that was read; the one after them goes regardless.
const GUARDED_TRIES: usize = 4;

/// Run `f` on the settings and this device's sync state under their locks; `f` says whether it
/// changed the settings (to save them).
type Access<'a> = dyn FnMut(&mut dyn FnMut(&mut Map<String, Value>, &mut DeviceState) -> bool) + 'a;

/// One push for `owner`: read the server's copy (a newer format is never overwritten), merge a
/// newer one in, write in the state it was read at, and acknowledge what was sent. On a
/// stateMismatch it reads, merges and tries again, up to 3 more times; then it writes without
/// the guard (last writer wins). Ok(false): nothing to push for `owner` (its settings are not here).
fn push_core(c: &Client, owner: &str, acct: &str, keep_cross: bool, now: &dyn Fn() -> i64, access: &mut Access) -> Result<bool, String> {
    for attempt in 0..=GUARDED_TRIES {
        let f = fetch(c)?;
        if f.remote == Remote::Newer { return Err(NEWER.into()); }
        let mut job = None;
        access(&mut |prefs, st| {
            job = st.prepare(prefs, owner, acct, &f.remote, now(), keep_cross);
            job.as_ref().is_some_and(|p| p.changed)
        });
        let Some(p) = job else { return Ok(false) };
        let guard = if attempt < GUARDED_TRIES { f.state.as_deref() } else { None };
        match store(c, &p.blob, &p.updated, &f.parts, guard) {
            Ok(()) => {
                access(&mut |_, st| { st.pushed(owner, &p); false });
                return Ok(true);
            }
            Err(StoreError::Changed) => continue,
            Err(StoreError::Failed(e)) => return Err(e),
        }
    }
    Err("The server kept refusing the settings.".into())
}

// ---------------------------------------------------------------- the running program

/// The account signed in now, for the sync.
#[derive(Clone)]
struct Active { client: std::sync::Arc<Client>, owner: String, acct: String, gen: u64, settled: bool, blocked: bool }

static STATE: std::sync::Mutex<Option<DeviceState>> = std::sync::Mutex::new(None);
static ACTIVE: std::sync::Mutex<Option<Active>> = std::sync::Mutex::new(None);
static NOTE: std::sync::Mutex<Option<&'static str>> = std::sync::Mutex::new(None);
static LAST_ERROR: std::sync::Mutex<Option<String>> = std::sync::Mutex::new(None);
static GEN: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);
#[cfg(not(test))]
static PUSH_SEQ: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);
static PUSHING: std::sync::Mutex<()> = std::sync::Mutex::new(());
/// Flushes of an outgoing account still running: a pull waits for them.
static FLUSHING: std::sync::Mutex<u32> = std::sync::Mutex::new(0);
static FLUSHED: std::sync::Condvar = std::sync::Condvar::new();
type Applied = Box<dyn Fn() + Send + Sync>;
static ON_APPLIED: std::sync::OnceLock<Applied> = std::sync::OnceLock::new();

const UNSUPPORTED: &str = "Not available: this server keeps no account metadata (Stalwart 1.0 or later does), so your settings stay on this computer.";
const BLOCKED: &str = "Paused: this account's settings could not be read while another account's were here. Barbican shows the default settings and tries again at the next sign-in.";
const ABOUT: &str = "Your other computers signed in to this account get the same Barbican settings, kept in your account on the server (anyone you share your Inbox with can read them). Plugins, sign-ins, trusted senders, remote content, warnings and this computer's desktop settings stay here.";

/// The row's text: what the sync does (or why it cannot), and the last push's error.
fn describe(note: Option<&str>, err: Option<&str>) -> String {
    let base = note.unwrap_or(ABOUT).to_string();
    match err { Some(e) => format!("{base} Last sync failed: {e}"), None => base }
}

/// The Settings > About row's description.
pub fn row_desc() -> String {
    let note = *NOTE.lock().unwrap_or_else(|e| e.into_inner());
    let err = LAST_ERROR.lock().unwrap_or_else(|e| e.into_inner()).clone();
    describe(note, err.as_deref())
}

fn state_path() -> std::path::PathBuf { crate::config_dir().join("settings-sync.json") }

/// Write the state file whole or not at all (a temporary file renamed over it).
fn write_state(v: &Value) {
    #[cfg(not(test))]
    {
        let _ = std::fs::create_dir_all(crate::config_dir());
        let tmp = crate::config_dir().join("settings-sync.json.tmp");
        if std::fs::write(&tmp, v.to_string()).is_ok() { let _ = std::fs::rename(&tmp, state_path()); }
    }
    #[cfg(test)]
    let _ = v;
}

fn persist(st: &DeviceState) { write_state(&st.to_json()); }

/// What the file says while changed settings are not on disk yet: nobody's and blocked, with the
/// base from before, so after a crash in between any pull resets first and nothing counts as
/// newer. The real state is written after `Settings::flush`.
fn interim(old_base: Option<String>) -> Value { json!({"owner": null, "blocked": true, "base": old_base, "dirty": {}}) }

fn persist_interim(old_base: Option<String>) { write_state(&interim(old_base)); }

/// Run `f` on this device's sync state (loaded on first use). Lock order: the live settings, then
/// this; never take the live settings inside `f`.
fn with_state<T>(f: impl FnOnce(&mut DeviceState) -> T) -> T {
    let mut g = STATE.lock().unwrap_or_else(|e| e.into_inner());
    let st = g.get_or_insert_with(|| {
        let v = std::fs::read_to_string(state_path()).ok().and_then(|t| serde_json::from_str::<Value>(&t).ok()).unwrap_or(Value::Null);
        DeviceState::from_json(&v)
    });
    f(st)
}

fn now_ms() -> i64 { chrono::Utc::now().timestamp_millis() }

fn disabled() -> bool { crate::settings::bool_pref(DISABLED_KEY, false) }

/// Whether an account's settings are pulled: the server keeps metadata and the switch is on.
fn may_pull(supported: bool, disabled: bool) -> bool { supported && !disabled }

/// Whether a push may go to the account signed in: its pull settled without being blocked, the
/// server keeps metadata and the switch is on (the owner check is [`DeviceState::prepare`]'s).
fn may_push(settled: bool, blocked: bool, supported: bool, disabled: bool) -> bool { settled && !blocked && may_pull(supported, disabled) }

/// At start, with the settings as loaded; and what redraws the window after a pull changed them.
pub fn init(prefs: &Map<String, Value>, on_applied: Applied) {
    with_state(|st| st.start(prefs));
    let _ = ON_APPLIED.set(on_applied);
}

fn applied() { if let Some(f) = ON_APPLIED.get() { f(); } }

/// Every settings save (under the live settings' lock): a change to a synced key is recorded and
/// pushed a few seconds later; switching the sync back on pulls.
#[cfg_attr(test, allow(dead_code))]
pub fn local_saved(prefs: &Map<String, Value>) {
    let s = with_state(|st| {
        let s = st.saved(prefs, now_ms());
        if s.dirty || s.holder { persist(st); }
        s
    });
    if s.toggled == Some(false) { std::thread::spawn(restart); }
    #[cfg(not(test))]
    if s.dirty { schedule_push(); }
}

fn keep_cross() -> bool { crate::oauth::accounts().len() > 1 }

/// A sign-in, a restored session or an account switch: pull for `c` in the background. Nothing
/// is pushed for it until this pull has settled.
pub fn begin(c: std::sync::Arc<Client>) {
    let gen = GEN.fetch_add(1, std::sync::atomic::Ordering::SeqCst) + 1;
    let ok = supported(&c);
    *NOTE.lock().unwrap_or_else(|e| e.into_inner()) = if ok { None } else { Some(UNSUPPORTED) };
    *LAST_ERROR.lock().unwrap_or_else(|e| e.into_inner()) = None;
    let a = Active { owner: c.state_key(), acct: crate::identities::account_key(&c.username, &c.base), client: c, gen, settled: false, blocked: false };
    with_state(|st| st.activate(Some(&a.owner)));
    *ACTIVE.lock().unwrap_or_else(|e| e.into_inner()) = Some(a.clone());
    if !may_pull(ok, disabled()) { return; }
    std::thread::spawn(move || pull(a));
}

/// Nothing more goes to (or comes from) the account that was in.
fn end() {
    GEN.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
    *ACTIVE.lock().unwrap_or_else(|e| e.into_inner()) = None;
    with_state(|st| st.activate(None));
}

fn current(gen: u64) -> bool { GEN.load(std::sync::atomic::Ordering::SeqCst) == gen }

/// Run `f` on a worker thread that pulls wait for (see [`wait_flushes`]).
fn counted(f: impl FnOnce() + Send + 'static) {
    *FLUSHING.lock().unwrap_or_else(|e| e.into_inner()) += 1;
    std::thread::spawn(move || {
        let _ = std::panic::catch_unwind(std::panic::AssertUnwindSafe(f));
        let mut n = FLUSHING.lock().unwrap_or_else(|e| e.into_inner());
        *n = n.saturating_sub(1);
        FLUSHED.notify_all();
    });
}

/// Wait (at most `max`) until the flushes of accounts being left are done.
pub fn wait_flushes(max: std::time::Duration) {
    let g = FLUSHING.lock().unwrap_or_else(|e| e.into_inner());
    let _ = FLUSHED.wait_timeout_while(g, max, |n| *n > 0);
}

/// The account signed in, when it has changes waiting to be pushed and may get them.
fn pending_push() -> Option<Active> {
    let a = ACTIVE.lock().unwrap_or_else(|e| e.into_inner()).clone()?;
    if !may_push(a.settled, a.blocked, supported(&a.client), disabled()) { return None; }
    with_state(|st| st.owner.as_deref() == Some(a.owner.as_str()) && !st.blocked && !st.dirty.is_empty()).then_some(a)
}

/// Before an account switch (Bulwark flushSync): the changes still waiting for the debounce go
/// to the account being left now, on a worker thread; the next account's pull waits for it.
pub fn flush() {
    if let Some(a) = pending_push() { counted(move || push_as(&a, false)); }
}

/// Sign-out or "add account": flush the waiting changes to the account being left, then nothing
/// more goes to it. `last`: the last account signed out, whose synced settings then go back to
/// defaults (Bulwark forgetSyncedSettings), after the flush.
pub fn leave(last: bool) {
    let a = ACTIVE.lock().unwrap_or_else(|e| e.into_inner()).clone();
    let pending = pending_push();
    end();
    if pending.is_none() && !last { return; }
    counted(move || {
        if let Some(p) = pending { push_as(&p, false); }
        if let Some(a) = a.filter(|_| last) { forget(&a.owner); }
    });
}

fn forget(owner: &str) {
    let mut done = false;
    crate::settings::with_live(|s| {
        let off = s.prefs.get(DISABLED_KEY) == Some(&json!(true));
        done = with_state(|st| {
            let old = st.base.clone();
            let d = st.forget(&mut s.prefs, owner, off);
            if d { persist_interim(old); }
            d
        });
        if done { s.save(); }
    });
    if done {
        crate::settings::Settings::flush();
        with_state(|st| persist(st));
        applied();
    }
}

/// The sync was switched back on: pull again for the account signed in.
fn restart() {
    let a = ACTIVE.lock().unwrap_or_else(|e| e.into_inner()).clone();
    if let Some(a) = a { begin(a.client); }
}

fn pull(a: Active) {
    // a restored session can sign in before the window set up the live settings
    for _ in 0..600 {
        if ON_APPLIED.get().is_some() { break; }
        std::thread::sleep(std::time::Duration::from_millis(50));
    }
    // the account just left gets its waiting changes first
    wait_flushes(std::time::Duration::from_secs(10));
    let remote = fetch(&a.client).map(|f| f.remote);
    let keep = keep_cross();
    let mut out = None;
    crate::settings::with_live(|s| {
        if !current(a.gen) { return; }
        let r = with_state(|st| {
            let old = st.base.clone();
            let r = st.settle(&mut s.prefs, &a.owner, &a.acct, remote, keep);
            if r.owner_changed || r.changed { persist_interim(old); } else { persist(st); }
            r
        });
        if r.changed { s.save(); }
        out = Some(r);
    });
    let Some(r) = out else { return };
    if r.owner_changed || r.changed {
        // the owner is written only once the settings it describes are on disk
        crate::settings::Settings::flush();
        with_state(|st| persist(st));
    }
    if let Some(x) = ACTIVE.lock().unwrap_or_else(|e| e.into_inner()).as_mut().filter(|x| x.gen == a.gen) {
        x.settled = true;
        x.blocked = r.blocked;
    }
    if r.blocked { *NOTE.lock().unwrap_or_else(|e| e.into_inner()) = Some(BLOCKED); }
    if r.changed { applied(); }
    if r.push && !disabled() { push_as(&a, true); }
}

#[cfg(not(test))]
fn schedule_push() {
    let seq = PUSH_SEQ.fetch_add(1, std::sync::atomic::Ordering::SeqCst) + 1;
    std::thread::spawn(move || {
        std::thread::sleep(DEBOUNCE);
        if PUSH_SEQ.load(std::sync::atomic::Ordering::SeqCst) == seq { push_now(); }
    });
}

/// The debounced push to the account signed in, if its pull settled.
#[cfg_attr(test, allow(dead_code))]
fn push_now() {
    let Some(a) = ACTIVE.lock().unwrap_or_else(|e| e.into_inner()).clone() else { return };
    if !may_push(a.settled, a.blocked, supported(&a.client), disabled()) { return; }
    push_as(&a, true);
}

/// Push to `a` (one push at a time). `check_gen`: only while `a` is still the account signed in
/// (a flush of the account being left goes regardless; the owner check still holds).
fn push_as(a: &Active, check_gen: bool) {
    let _one = PUSHING.lock().unwrap_or_else(|e| e.into_inner());
    if check_gen && !current(a.gen) { return; }
    let keep = keep_cross();
    let gen = a.gen;
    let mut redraw = false;
    let mut access = |f: &mut dyn FnMut(&mut Map<String, Value>, &mut DeviceState) -> bool| {
        let mut changed = false;
        crate::settings::with_live(|s| {
            if check_gen && !current(gen) { return; }
            changed = with_state(|st| {
                let old = st.base.clone();
                let c = f(&mut s.prefs, st);
                if c { persist_interim(old); } else { persist(st); }
                c
            });
            if changed { s.save(); }
        });
        if changed {
            redraw = true;
            crate::settings::Settings::flush();
            with_state(|st| persist(st));
        }
    };
    let res = push_core(&a.client, &a.owner, &a.acct, keep, &now_ms, &mut access);
    if redraw { applied(); }
    // a failed write keeps the changes marked: the next change or sign-in sends them
    *LAST_ERROR.lock().unwrap_or_else(|e| e.into_inner()) = res.err();
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::{Arc, Mutex};

    fn obj(v: Value) -> Map<String, Value> { v.as_object().cloned().unwrap() }
    fn blob(settings: Value, updated: &str) -> Remote {
        Remote::Blob { manifest: Manifest { updated: updated.into(), updated_ms: ms_of(updated).unwrap(), gen: "ab".into() }, settings: validate(&obj(settings)) }
    }
    fn meta_of(entries: &[(String, Value)], manifest: &Value) -> Value {
        let mut m = Map::new();
        for (k, v) in entries { m.insert(k.clone(), v.clone()); }
        m.insert(manifest_key(), manifest.clone());
        Value::Object(m)
    }
    /// A device whose settings are `owner`'s, with `owner` signed in.
    fn state(owner: Option<&str>, base: Option<&str>) -> DeviceState {
        let mut st = DeviceState { owner: owner.map(String::from), base: base.map(String::from), ..Default::default() };
        st.activate(owner);
        st.start(&Map::new());
        st
    }

    #[test]
    fn a_blob_round_trips_through_parts_that_each_fit_an_entry() {
        // quotes, backslashes, control characters and non-ASCII all count as JSON spells them
        let mut s = Map::new();
        s.insert("emailKeywords".into(), json!((0..120).map(|i| json!({"id": format!("t{i}"), "label": format!("\"Tag\\ {i}\n\u{1}ünï ✓"), "color": "#ff0000"})).collect::<Vec<_>>()));
        s.insert("theme".into(), json!("dark"));
        let (parts, manifest) = encode(&s, "2026-11-25T10:00:00.000Z", "0a1b").unwrap();
        assert!(parts.len() > 1, "{}", parts.len());
        for (k, v) in &parts {
            assert!(crate::templates::value_bytes(v) <= MAX_ENTRY_BYTES, "{k}");
            assert!(crate::templates::valid_key(k.strip_prefix("org.barbican.").unwrap()), "{k}");
        }
        assert_eq!(manifest["parts"], parts.len());
        let Remote::Blob { manifest: m, settings } = decode(&meta_of(&parts, &manifest)) else { panic!() };
        assert_eq!(m.gen, "0a1b");
        assert_eq!(settings, s);
        assert_eq!(chunk(""), vec![String::new()]);
        // a piece never splits inside the budget wrongly: the exact budget fits, one more does not
        let exact = "a".repeat(MAX_ENTRY_BYTES - 2);
        assert_eq!(chunk(&exact).len(), 1);
        assert_eq!(chunk(&format!("{exact}\"")).len(), 2);
    }

    #[test]
    fn a_blob_over_20_kb_is_not_pushed_and_the_row_says_why() {
        // 64 KB of shared metadata per mailbox, with Bulwark's templates and two generations
        let mut big = Map::new();
        big.insert("x".into(), json!("y".repeat(MAX_BLOB_BYTES)));
        assert!(encode(&big, "2026-11-25T10:00:00.000Z", "aa").unwrap_err().contains("too big"));
        // escapes count: text under the cap whose stored JSON is over it is refused too
        let mut quoted = Map::new();
        quoted.insert("x".into(), json!("\"".repeat(MAX_BLOB_BYTES / 3)));
        assert!(encode(&quoted, "2026-11-25T10:00:00.000Z", "aa").is_err());
        let mut ok = Map::new();
        ok.insert("x".into(), json!("y".repeat(MAX_BLOB_BYTES - 200)));
        let (parts, _) = encode(&ok, "2026-11-25T10:00:00.000Z", "aa").unwrap();
        assert!(parts.len() <= MAX_PARTS && parts.iter().map(|p| crate::templates::value_bytes(&p.1)).sum::<usize>() <= MAX_BLOB_BYTES);
        let d = describe(None, Some("Your settings are too big to sync (25000 of 20480 bytes)."));
        assert!(d.contains("Plugins") && d.ends_with("Last sync failed: Your settings are too big to sync (25000 of 20480 bytes)."), "{d}");
        assert!(describe(Some(UNSUPPORTED), None).starts_with("Not available"));
        assert!(ABOUT.contains("share your Inbox"));
    }

    #[test]
    fn parts_that_do_not_match_the_manifest_are_not_applied() {
        let s = obj(json!({"theme": "light"}));
        let (parts, manifest) = encode(&s, "2026-11-25T10:00:00.000Z", "abcd").unwrap();
        assert!(matches!(decode(&meta_of(&parts, &manifest)), Remote::Blob { .. }));
        let mut other = parts.clone();
        other[0].1 = json!(other[0].1.as_str().unwrap().replace("light", "dark!"));
        assert_eq!(decode(&meta_of(&other, &manifest)), Remote::NotReady);
        assert_eq!(decode(&meta_of(&[], &manifest)), Remote::NotReady);
        let mut m = manifest.clone();
        m["parts"] = json!(MAX_PARTS + 1);
        assert_eq!(decode(&meta_of(&parts, &m)), Remote::NotReady);
        let mut m = manifest.clone();
        m["format"] = json!(0);
        assert_eq!(decode(&meta_of(&parts, &m)), Remote::NotReady);
        let mut m = manifest.clone();
        m["gen"] = json!("../x");
        assert_eq!(decode(&meta_of(&parts, &m)), Remote::NotReady);
        // a newer format, whatever else its manifest holds
        assert_eq!(decode(&meta_of(&[], &json!({"format": 2, "layout": "other"}))), Remote::Newer);
        assert_eq!(decode(&json!({"org.bulwarkmail.templates": {}})), Remote::Nothing);
        assert_eq!(decode(&Value::Null), Remote::Nothing);
    }

    #[test]
    fn only_allowlisted_keys_of_the_right_kind_are_taken() {
        let v = validate(&obj(json!({
            "theme": "dark", "density": "compact", "showPreview": false, "firstDayOfWeek": 0, "retentionTrashDays": 30,
            "calendarWorkingDays": [1, 2, 3], "hoverActions": ["delete", "star"], "subAddressDelimiter": "-",
            "fontSize": "large", "mailTextScale": 1.25, "reduceTransparency": "system", "showRowActionButton": null,
            // never applied, whatever a server copy says
            "enabledPlugins": ["evil"], "pluginBindings": {"evil": "C:/x.dll|h"}, "pluginGrants": {"evil": ["net"]}, "plugin:evil": true,
            "settingsSyncDisabled": true, "trustedSenders": ["a@example.test"], "externalContentPolicy": "allow",
            "pushRelayUrl": "https://relay.example.test", "proInterface": true, "ai.key": "k", "desktop.startAtLogin": true,
            "readReceiptResponse": "always", "externalRecipientConfirm": false, "externalSenderLabel": false, "showAvatarsInJunk": true,
            "deleteAction": "permanent", "permanentlyDeleteJunk": true, "sendConfirmation": false, "sendDelaySeconds": 0,
            "confirmBulkDelete": false, "debugMode": true,
            "accounts": [], "windowState": {}, "unknownThing": 1,
        })));
        for k in ["theme", "density", "showPreview", "firstDayOfWeek", "retentionTrashDays", "calendarWorkingDays", "hoverActions",
            "subAddressDelimiter", "fontSize", "mailTextScale", "reduceTransparency", "showRowActionButton"] {
            assert!(v.contains_key(k), "{k} dropped");
        }
        assert_eq!(v.len(), 12, "{v:?}");
        for k in ["enabledPlugins", "pluginBindings", "pluginGrants", "settingsSyncDisabled", "trustedSenders", "externalContentPolicy",
            "pushRelayUrl", "gravatarAvatars", "senderFavicons", "doNotDisturb", "readReceiptResponse", "requestReadReceiptDefault",
            "externalRecipientConfirm", "externalSenderLabel", "showAvatarsInJunk", "deleteAction", "permanentlyDeleteJunk",
            "sendConfirmation", "sendDelaySeconds", "confirmDelete", "confirmArchive", "confirmBulkDelete", "attachmentReminderEnabled",
            "emptySubjectWarningEnabled", "placeholderWarningEnabled", "phishingReportAddress", "debugMode", "proofreading"] {
            assert!(!is_synced(k), "{k}");
            assert!(!blob_for(&obj(json!({k: true})), "a").contains_key(k), "{k} pushed");
        }
        assert!(is_synced("showPreview") && is_synced("preferredIdentityIds") && is_synced("theme"));
    }

    #[test]
    fn wrong_types_and_oversized_values_are_dropped() {
        let v = validate(&obj(json!({
            "showPreview": "yes", "theme": "neon", "density": 3, "firstDayOfWeek": "x", "retentionTrashDays": 99999,
            "calendarWorkingDays": [1, 9], "hoverActions": ["delete", "rm -rf"], "subAddressDelimiter": "ab",
            "timeZone": "z".repeat(MAX_TEXT_CHARS + 1), "folderIcons": [1],
            "emailKeywords": (0..MAX_ITEMS + 1).map(|i| json!(i)).collect::<Vec<_>>(), "toastDuration": 10,
        })));
        assert_eq!(v, obj(json!({"toastDuration": 10})));
        assert!(fits(&Kind::OneOf(vec![json!("10")]), &json!(10)));
        assert!(fits(&Kind::OneOf(vec![json!(1.0)]), &json!(1)));
    }

    #[test]
    fn a_push_carries_only_this_accounts_entry_and_nulls_for_defaults() {
        let prefs = obj(json!({"theme": "light", "enabledPlugins": ["p"], "preferredIdentityIds": {"ann@mail.example.test": "i1", "bob@other.example.test": "i2"}}));
        let b = blob_for(&prefs, "ann@mail.example.test");
        assert_eq!(b["theme"], "light");
        assert_eq!(b["preferredIdentityIds"], json!({"ann@mail.example.test": "i1"}));
        assert_eq!(b["showPreview"], Value::Null);
        assert!(!b.contains_key("enabledPlugins") && !b.contains_key(DISABLED_KEY));
        assert!(!blob_for(&prefs, "carl@example.test").contains_key("preferredIdentityIds"));
        // the blob of every synced key at a value fits the cap with room for tags
        assert!(encode(&b, "2026-11-25T10:00:00.000Z", "aa").is_ok());
    }

    #[test]
    fn a_server_copy_with_enabled_plugins_does_not_switch_them_on() {
        let mut st = state(Some("A"), None);
        let mut prefs = obj(json!({"enabledPlugins": [], "theme": "dark"}));
        let s = obj(json!({"enabledPlugins": ["evil"], "pluginBindings": {"evil": "x"}, "pluginGrants": {"evil": ["fs"]}, "theme": "light"}));
        let (parts, manifest) = encode(&s, "2026-11-25T10:00:00.000Z", "cd").unwrap();
        let r = st.settle(&mut prefs, "A", "ann@x", Ok(decode(&meta_of(&parts, &manifest))), false);
        assert!(r.changed);
        assert_eq!(prefs["theme"], "light");
        assert_eq!(prefs["enabledPlugins"], json!([]));
        assert!(!prefs.contains_key("pluginBindings") && !prefs.contains_key("pluginGrants"));
        let mut d = Map::new();
        assert!(merge(&mut prefs, &obj(json!({"enabledPlugins": ["evil"], "settingsSyncDisabled": true, "deleteAction": "permanent"})), &mut d, 0, "a", false).is_empty());
    }

    #[test]
    fn nothing_stored_pushes_the_local_settings_of_a_device_that_never_synced() {
        let mut st = state(None, None);
        st.activate(Some("A"));
        let mut prefs = obj(json!({"theme": "light"}));
        let r = st.settle(&mut prefs, "A", "a", Ok(Remote::Nothing), false);
        assert_eq!(r, Settled { changed: false, push: true, blocked: false, owner_changed: true });
        assert_eq!(st.owner.as_deref(), Some("A"));
        let p = st.prepare(&mut prefs, "A", "a", &Remote::Nothing, 1_000, false).unwrap();
        assert_eq!(p.blob["theme"], "light");
    }

    #[test]
    fn another_accounts_settings_are_never_pushed_under_this_one() {
        // the device mirrors A; B signs in and has nothing stored: defaults, owner B, no push
        let mut st = state(Some("A"), Some("2026-11-20T00:00:00.000Z"));
        st.activate(Some("B"));
        let mut prefs = obj(json!({"theme": "light", "density": "compact", "enabledPlugins": ["p"], "preferredIdentityIds": {"a@x": "i1"}}));
        st.dirty.insert("theme".into(), json!(5));
        let r = st.settle(&mut prefs, "B", "b@x", Ok(Remote::Nothing), false);
        assert_eq!(r, Settled { changed: true, push: false, blocked: false, owner_changed: true });
        assert!(!prefs.contains_key("theme") && !prefs.contains_key("density"));
        assert_eq!(prefs["enabledPlugins"], json!(["p"]), "device settings stay");
        assert_eq!(prefs["preferredIdentityIds"], json!({"a@x": "i1"}));
        assert!(st.dirty.is_empty() && st.base.is_none());
        // B's pull failed while A's settings were here: defaults, B holds them blocked
        let mut st = state(Some("A"), None);
        let mut prefs = obj(json!({"theme": "light"}));
        let r = st.settle(&mut prefs, "B", "b@x", Err("offline".into()), false);
        assert_eq!(r, Settled { changed: true, push: false, blocked: true, owner_changed: true });
        assert_eq!((st.owner.as_deref(), st.blocked), (Some("B"), true));
        assert!(prefs.is_empty());
        assert!(st.prepare(&mut prefs, "B", "b@x", &Remote::Nothing, 1, false).is_none(), "blocked: no push");
        assert!(st.prepare(&mut prefs, "A", "a@x", &Remote::Nothing, 1, false).is_none(), "not A's");
        // the same account's failed pull keeps everything (nothing pushed now)
        let mut st = state(None, None);
        let mut prefs = obj(json!({"theme": "light"}));
        let r = st.settle(&mut prefs, "A", "a", Ok(Remote::NotReady), false);
        assert!(!r.changed && !r.push && !r.blocked);
        assert_eq!((st.owner.as_deref(), prefs["theme"].as_str()), (Some("A"), Some("light")));
    }

    #[test]
    fn a_change_made_while_blocked_never_reaches_the_other_account() {
        // A's settings here; B signs in and its pull fails: blocked for B
        let mut st = state(Some("A"), Some("2026-11-20T00:00:00.000Z"));
        let mut prefs = obj(json!({"theme": "light"}));
        st.start(&prefs);
        st.activate(Some("B"));
        st.settle(&mut prefs, "B", "b@x", Err("offline".into()), false);
        // a change on B's session: not recorded as anyone's to push
        prefs.insert("density".into(), json!("compact"));
        let s = st.saved(&prefs, 10);
        assert!(!s.dirty && st.dirty.is_empty());
        // back on A: its copy (older than nothing) is taken over defaults, the change is gone
        st.activate(Some("A"));
        let r = st.settle(&mut prefs, "A", "a@x", Ok(blob(json!({"theme": "dark"}), "2026-11-21T00:00:00.000Z")), false);
        assert!(!r.push && !r.blocked);
        assert_eq!(prefs, obj(json!({"theme": "dark"})));
        let p = st.prepare(&mut prefs, "A", "a@x", &Remote::Nothing, 1, false).unwrap();
        assert_eq!(p.blob["density"], Value::Null);
        assert!(p.sent.is_empty());

        // B could not pull at all (no metadata, or the switch off): a change on B's session
        // makes the settings B's, blocked, and A's next pull starts over too
        let mut st = state(Some("A"), Some("2026-11-20T00:00:00.000Z"));
        let mut prefs = obj(json!({"theme": "light"}));
        st.start(&prefs);
        st.activate(Some("B"));
        prefs.insert("density".into(), json!("compact"));
        let s = st.saved(&prefs, 10);
        assert_eq!(s, Saved { dirty: false, holder: true, toggled: None });
        assert_eq!((st.owner.as_deref(), st.blocked), (Some("B"), true));
        st.activate(Some("A"));
        st.settle(&mut prefs, "A", "a@x", Ok(blob(json!({"theme": "dark"}), "2026-11-19T00:00:00.000Z")), false);
        assert!(!prefs.contains_key("density"), "{prefs:?}");
        assert_eq!(prefs["theme"], "dark");
        // and with nobody signed in, a change blocks the settings; the owner and what waits stay
        let mut st = state(Some("A"), None);
        st.dirty.insert("theme".into(), json!(1));
        st.activate(None);
        let mut prefs = obj(json!({"theme": "x"}));
        assert!(st.saved(&prefs, 1).holder);
        assert_eq!((st.owner.as_deref(), st.blocked, st.dirty.len()), (Some("A"), true, 1));
        prefs.insert("theme".into(), json!("y"));
        assert_eq!(st.saved(&prefs, 2), Saved::default());
    }

    #[test]
    fn a_change_with_nobody_signed_in_is_never_pushed_to_the_next_account() {
        // A's settings, then "add account" (nobody signed in) and a change
        let mut st = state(Some("A"), Some("2026-11-20T00:00:00.000Z"));
        let mut prefs = obj(json!({"theme": "light", "density": "compact"}));
        st.start(&prefs);
        st.activate(None);
        prefs.insert("theme".into(), json!("dark"));
        assert!(st.saved(&prefs, 5).holder);
        // B signs in, nothing stored on its server: it starts from defaults, nothing of A's goes up
        st.activate(Some("B"));
        let r = st.settle(&mut prefs, "B", "b@x", Ok(Remote::Nothing), false);
        assert!(r.changed && !r.blocked, "{r:?}");
        assert!(!prefs.contains_key("theme") && !prefs.contains_key("density"), "{prefs:?}");
        assert_eq!(st.owner.as_deref(), Some("B"));
        assert!(st.prepare(&mut prefs, "A", "a@x", &Remote::Nothing, 9, false).is_none(), "not A's any more");
        if let Some(p) = st.prepare(&mut prefs, "B", "b@x", &Remote::Nothing, 9, false) {
            assert!(p.blob["theme"].is_null() && p.blob["density"].is_null(), "defaults only");
        }
        // and the same account signing in again also starts over
        let mut st = state(Some("A"), Some("2026-11-20T00:00:00.000Z"));
        let mut prefs = obj(json!({"theme": "light"}));
        st.start(&prefs);
        st.activate(None);
        prefs.insert("theme".into(), json!("dark"));
        st.saved(&prefs, 5);
        st.activate(Some("A"));
        st.settle(&mut prefs, "A", "a@x", Ok(blob(json!({"theme": "system"}), "2026-11-19T00:00:00.000Z")), false);
        assert_eq!(prefs["theme"], "system");
    }

    #[test]
    fn until_the_settings_are_on_disk_the_state_file_says_nobodys_and_blocked() {
        let v = interim(Some("2026-11-20T00:00:00.000Z".into()));
        let st = DeviceState::from_json(&v);
        assert_eq!((st.owner, st.blocked, st.base.as_deref(), st.dirty.len()), (None, true, Some("2026-11-20T00:00:00.000Z"), 0));
    }

    #[test]
    fn another_accounts_copy_replaces_the_local_one_except_cross_account_views() {
        let mut st = state(Some("A"), None);
        let mut prefs = obj(json!({"theme": "light", "density": "compact", "enableUnifiedMailbox": true}));
        let r = st.settle(&mut prefs, "B", "b@x", Ok(blob(json!({"theme": "dark", "enableUnifiedMailbox": false}), "2026-11-25T00:00:00.000Z")), true);
        assert!(r.changed && !r.push);
        assert_eq!(prefs, obj(json!({"enableUnifiedMailbox": true, "theme": "dark"})));
        assert_eq!(st.owner.as_deref(), Some("B"));
    }

    #[test]
    fn the_newest_copy_wins_but_an_unsent_local_change_is_kept() {
        let mut st = state(Some("A"), Some("2026-11-20T00:00:00.000Z"));
        let mut prefs = obj(json!({"theme": "light", "density": "compact", "showPreview": false}));
        st.start(&prefs);
        prefs.insert("density".into(), json!("regular"));
        let late = ms_of("2026-11-24T00:00:00.000Z").unwrap();
        assert!(st.saved(&prefs, late).dirty);
        prefs.insert("showPreview".into(), json!(true));
        st.saved(&prefs, ms_of("2026-11-21T00:00:00.000Z").unwrap());
        let server = blob(json!({"theme": "dark", "density": "comfortable", "showPreview": null}), "2026-11-22T00:00:00.000Z");
        let r = st.settle(&mut prefs, "A", "a", Ok(server.clone()), false);
        assert_eq!(r, Settled { changed: true, push: true, blocked: false, owner_changed: false });
        assert_eq!(prefs, obj(json!({"theme": "dark", "density": "regular"})));
        assert_eq!(st.dirty.keys().collect::<Vec<_>>(), vec!["density"]);
        assert_eq!(st.base.as_deref(), Some("2026-11-22T00:00:00.000Z"));
        prefs.insert("theme".into(), json!("system"));
        st.saved(&prefs, 1);
        let r = st.settle(&mut prefs, "A", "a", Ok(server), false);
        assert!(!r.changed);
        assert_eq!(prefs["theme"], "system");
        let p = st.prepare(&mut prefs, "A", "a", &blob(json!({}), "2026-11-22T00:00:00.000Z"), 0, false).unwrap();
        assert_eq!(p.updated, "2026-11-22T00:00:00.001Z");
        prefs.insert("density".into(), json!("compact"));
        st.saved(&prefs, late + 1);
        st.pushed("A", &p);
        assert_eq!(st.dirty.keys().collect::<Vec<_>>(), vec!["density"]);
        assert_eq!(st.base.as_deref(), Some("2026-11-22T00:00:00.001Z"));
    }

    #[test]
    fn a_push_first_takes_in_a_newer_copy_from_another_device() {
        let mut st = state(Some("A"), Some("2026-11-20T00:00:00.000Z"));
        let mut prefs = obj(json!({"theme": "light"}));
        st.start(&prefs);
        prefs.insert("density".into(), json!("compact"));
        st.saved(&prefs, ms_of("2026-11-23T00:00:00.000Z").unwrap());
        let p = st.prepare(&mut prefs, "A", "a", &blob(json!({"theme": "dark", "density": "regular"}), "2026-11-22T00:00:00.000Z"), ms_of("2026-11-23T00:00:01.000Z").unwrap(), false).unwrap();
        assert!(p.changed);
        assert_eq!((p.blob["theme"].as_str(), p.blob["density"].as_str()), (Some("dark"), Some("compact")));
        assert_eq!(p.updated, "2026-11-23T00:00:01.000Z");
    }

    #[test]
    fn per_account_maps_take_only_this_accounts_entry() {
        let mut st = state(Some("A"), None);
        let mut prefs = obj(json!({"preferredIdentityIds": {"a@x": "old", "b@y": "keep"}}));
        st.settle(&mut prefs, "A", "a@x", Ok(blob(json!({"preferredIdentityIds": {"a@x": "new", "b@y": "evil", "c@z": "evil"}}), "2026-11-25T00:00:00.000Z")), false);
        assert_eq!(prefs["preferredIdentityIds"], json!({"a@x": "new", "b@y": "keep"}));
    }

    #[test]
    fn local_saves_record_changed_synced_keys_and_the_switch() {
        let mut st = DeviceState { owner: Some("A".into()), ..Default::default() };
        st.activate(Some("A"));
        let mut prefs = obj(json!({"theme": "dark"}));
        // before the start nothing is compared
        assert_eq!(st.saved(&prefs, 1), Saved::default());
        prefs.insert("theme".into(), json!("light"));
        assert!(st.saved(&prefs, 2).dirty);
        prefs.insert("enabledPlugins".into(), json!(["p"]));
        prefs.insert("winX".into(), json!(1));
        assert!(!st.saved(&prefs, 3).dirty, "not synced keys are not changes");
        prefs.remove("theme");
        assert!(st.saved(&prefs, 4).dirty);
        assert_eq!(st.dirty, obj(json!({"theme": 4})));
        prefs.insert(DISABLED_KEY.into(), json!(true));
        assert_eq!(st.saved(&prefs, 5).toggled, Some(true));
        prefs.insert(DISABLED_KEY.into(), json!(false));
        assert_eq!(st.saved(&prefs, 6).toggled, Some(false));
        let back = DeviceState::from_json(&json!({"owner": "A", "blocked": true, "base": "2026-11-25T00:00:00.000Z", "dirty": {"theme": 4, "enabledPlugins": 1, "density": "x"}}));
        assert_eq!((back.owner.as_deref(), back.blocked, back.base.as_deref(), back.dirty.clone()), (Some("A"), true, Some("2026-11-25T00:00:00.000Z"), obj(json!({"theme": 4}))));
        assert_eq!(DeviceState::from_json(&back.to_json()), back);
    }

    #[test]
    fn the_last_sign_out_forgets_the_synced_settings() {
        let mut st = state(Some("A"), Some("2026-11-25T00:00:00.000Z"));
        let mut prefs = obj(json!({"theme": "light", "enabledPlugins": ["p"], "winX": 3}));
        assert!(!st.forget(&mut prefs, "A", true), "sync off: nothing was uploaded, nothing forgotten");
        assert!(!st.forget(&mut prefs, "B", false), "not B's");
        assert!(st.forget(&mut prefs, "A", false));
        assert_eq!(prefs, obj(json!({"enabledPlugins": ["p"], "winX": 3})));
        assert_eq!((st.owner.clone(), st.base.clone(), st.blocked), (None, None, false));
        // the next account adopts what is here (a device that never synced)
        st.activate(Some("B"));
        let r = st.settle(&mut prefs, "B", "b", Ok(Remote::Nothing), false);
        assert!(r.push && !r.changed);
    }

    #[test]
    fn the_disabled_switch_stops_pull_and_push() {
        assert!(may_pull(true, false) && !may_pull(true, true) && !may_pull(false, false));
        assert!(may_push(true, false, true, false));
        assert!(!may_push(true, false, true, true), "switched off");
        assert!(!may_push(false, false, true, false), "pull not settled");
        assert!(!may_push(true, true, true, false), "blocked after another account's failed pull");
        assert!(!may_push(true, false, false, false), "no metadata");
        let mut st = state(Some("A"), None);
        let mut prefs = obj(json!({DISABLED_KEY: false}));
        st.start(&prefs);
        prefs.insert(DISABLED_KEY.into(), json!(true));
        assert_eq!(st.saved(&prefs, 1), Saved { dirty: false, holder: false, toggled: Some(true) });
        assert!(st.dirty.is_empty(), "the switch itself is not synced");
        assert!(!blob_for(&prefs, "a").contains_key(DISABLED_KEY));
    }

    // ---------------------------------------------------------------- against the fake server

    type Log = Arc<Mutex<Vec<Value>>>;

    /// The fake's Inbox: its metadata and Mailbox state (`s<n>`, one up per write), copies other
    /// computers write right after each of our reads (one per read, as long as there are any),
    /// and what Mailbox/get of the metadata answers (None: the usual).
    #[derive(Default)]
    struct Srv { meta: Value, n: u64, others: Vec<Value>, get: Option<Value> }

    fn fake(caps: &[&str], srv: Srv, log: &Log) -> (Client, Arc<Mutex<Srv>>) {
        let (log, srv) = (log.clone(), Arc::new(Mutex::new(srv)));
        let s2 = srv.clone();
        let c = Client::fake("https://mail.example.test", "A1", caps, move |calls| {
            Ok(calls.as_array().unwrap().iter().map(|c| {
                log.lock().unwrap_or_else(|e| e.into_inner()).push(c.clone());
                let m = c[0].as_str().unwrap_or("");
                let a = &c[1];
                let mut s = s2.lock().unwrap();
                let r = match m {
                    "Mailbox/get" if a["properties"] == json!(["metadata"]) => {
                        let r = s.get.clone().unwrap_or_else(|| json!({"state": format!("s{}", s.n), "list": [{"id": "in1", "metadata": s.meta.clone()}]}));
                        if !s.others.is_empty() {
                            let o = s.others.remove(0);
                            s.meta = o;
                            s.n += 1;
                        }
                        r
                    }
                    "Mailbox/get" => json!({"state": format!("s{}", s.n), "list": [{"id": "in1", "role": "inbox", "name": "Inbox"}]}),
                    "Mailbox/set" => {
                        if a.get("ifInState").is_some_and(|x| *x != json!(format!("s{}", s.n))) {
                            return json!(["error", {"type": "stateMismatch"}, c[2]]);
                        }
                        if !s.meta.is_object() { s.meta = json!({}); }
                        for (k, v) in a["update"]["in1"].as_object().unwrap() {
                            let k = k.strip_prefix("metadata/").unwrap();
                            if v.is_null() { s.meta.as_object_mut().unwrap().remove(k); } else { s.meta[k] = v.clone(); }
                        }
                        let old = format!("s{}", s.n);
                        s.n += 1;
                        json!({"oldState": old, "newState": format!("s{}", s.n), "updated": {"in1": null}})
                    }
                    _ => json!({}),
                };
                json!([m, r, c[2]])
            }).collect())
        });
        (c, srv)
    }

    const CAPS: &[&str] = &["urn:ietf:params:jmap:core", "urn:ietf:params:jmap:mail", "urn:ietf:params:jmap:metadata"];

    fn sets(log: &Log) -> Vec<Value> { log.lock().unwrap().iter().filter(|c| c[0] == "Mailbox/set").cloned().collect() }

    #[test]
    fn one_update_writes_the_parts_and_manifest_and_drops_the_old_generation() {
        let log: Log = Default::default();
        let (c, srv) = fake(CAPS, Srv { meta: json!({"org.barbican.settings.part.old.0": "x", "org.barbican.settings.manifest": {"gen": "old"}, "org.bulwarkmail.template.t": {"name": "T"}}), n: 7, ..Default::default() }, &log);
        assert!(supported(&c));
        let f = fetch(&c).unwrap();
        assert_eq!((f.remote.clone(), f.parts.clone(), f.state.as_deref()), (Remote::NotReady, vec!["org.barbican.settings.part.old.0".to_string()], Some("s7")));
        let mut s = Map::new();
        s.insert("theme".into(), json!("light"));
        s.insert("emailKeywords".into(), json!((0..160).map(|i| json!({"id": format!("k{i}"), "label": "x".repeat(40)})).collect::<Vec<_>>()));
        log.lock().unwrap().clear();
        store_gen(&c, &s, "2026-11-25T10:00:00.000Z", &f.parts, "beef", f.state.as_deref()).unwrap();
        let calls = sets(&log);
        assert_eq!(calls.len(), 1, "one update of the Inbox");
        assert_eq!(calls[0][1]["accountId"], "A1");
        assert_eq!(calls[0][1]["ifInState"], "s7", "the state the Inbox was read at");
        let patch = calls[0][1]["update"]["in1"].as_object().unwrap();
        let parts: Vec<&String> = patch.keys().filter(|k| k.starts_with("metadata/org.barbican.settings.part.beef.")).collect();
        assert!(parts.len() > 1, "{patch:?}");
        assert_eq!(patch["metadata/org.barbican.settings.manifest"]["gen"], "beef");
        assert_eq!(patch["metadata/org.barbican.settings.manifest"]["parts"], parts.len());
        assert_eq!(patch["metadata/org.barbican.settings.part.old.0"], Value::Null);
        assert_eq!(patch.len(), parts.len() + 2);
        // read back: one generation left, the same settings; Bulwark's template is untouched
        let f = fetch(&c).unwrap();
        let Remote::Blob { settings, manifest } = f.remote else { panic!() };
        assert_eq!(settings, s);
        assert_eq!(manifest.updated, "2026-11-25T10:00:00.000Z");
        assert!(f.parts.iter().all(|k| k.contains(".beef.")));
        assert!(srv.lock().unwrap().meta["org.bulwarkmail.template.t"].is_object());
        // written without a guard when there is none
        log.lock().unwrap().clear();
        store_gen(&c, &s, "2026-11-25T10:00:00.000Z", &f.parts, "cafe", None).unwrap();
        assert!(sets(&log)[0][1].get("ifInState").is_none());
        let (c, _) = fake(&["urn:ietf:params:jmap:core", "urn:ietf:params:jmap:mail"], Srv::default(), &log);
        assert!(!supported(&c));
    }

    #[test]
    fn a_missing_inbox_or_metadata_is_an_error_not_nothing_stored() {
        let log: Log = Default::default();
        for get in [json!({"state": "s1", "list": [], "notFound": ["in1"]}), json!({"state": "s1", "list": [{"id": "in1"}]}), json!({"state": "s1", "list": [{"id": "other", "metadata": {}}]})] {
            let (c, _) = fake(CAPS, Srv { get: Some(get.clone()), ..Default::default() }, &log);
            assert!(fetch(&c).is_err(), "{get}");
        }
        // metadata that is there and empty is "nothing stored"
        let (c, _) = fake(CAPS, Srv { get: Some(json!({"state": "s1", "list": [{"id": "in1", "metadata": null}]})), ..Default::default() }, &log);
        assert_eq!(fetch(&c).unwrap().remote, Remote::Nothing);
        // and a pull that fails so keeps the settings (no "nothing stored" push over them)
        let mut st = state(Some("A"), Some("2026-11-20T00:00:00.000Z"));
        let mut prefs = obj(json!({"theme": "light"}));
        let r = st.settle(&mut prefs, "A", "a", fetch(&fake(CAPS, Srv { get: Some(json!({"list": []})), ..Default::default() }, &log).0).map(|f| f.remote), false);
        assert!(!r.push && !r.changed);
    }

    /// The program's access, over a plain pair.
    fn run(c: &Client, owner: &str, pair: &Mutex<(Map<String, Value>, DeviceState)>) -> Result<bool, String> {
        let mut access = |f: &mut dyn FnMut(&mut Map<String, Value>, &mut DeviceState) -> bool| {
            let mut g = pair.lock().unwrap();
            let (p, s) = &mut *g;
            f(p, s);
        };
        push_core(c, owner, "ann@mail.example.test", false, &|| ms_of("2026-11-25T12:00:00.000Z").unwrap(), &mut access)
    }

    #[test]
    fn a_flush_sends_the_waiting_change_to_the_account_being_left_only() {
        // the debounce has not run yet: theme changed on A, then A is left
        let log: Log = Default::default();
        let (c, srv) = fake(CAPS, Srv::default(), &log);
        let mut st = state(Some("A"), None);
        let mut prefs = obj(json!({"theme": "dark"}));
        st.start(&prefs);
        prefs.insert("theme".into(), json!("light"));
        st.saved(&prefs, 5);
        st.activate(None); // signed out / switching: the flush goes regardless of who is in now
        let pair = Mutex::new((prefs, st));
        assert_eq!(run(&c, "A", &pair), Ok(true));
        assert_eq!(sets(&log).len(), 1);
        assert!(pair.lock().unwrap().1.dirty.is_empty());
        let Remote::Blob { settings, .. } = decode(&srv.lock().unwrap().meta) else { panic!() };
        assert_eq!(settings["theme"], "light");
        // a flush for another account than the settings are of sends nothing
        log.lock().unwrap().clear();
        assert_eq!(run(&c, "B", &pair), Ok(false));
        assert!(sets(&log).is_empty());
    }

    #[test]
    fn a_newer_format_on_the_server_is_never_overwritten() {
        let log: Log = Default::default();
        let (c, _) = fake(CAPS, Srv { meta: json!({"org.barbican.settings.manifest": {"format": 2, "updatedAt": "2026-11-25T00:00:00.000Z"}}), ..Default::default() }, &log);
        let mut st = state(Some("A"), None);
        st.dirty.insert("theme".into(), json!(1));
        let pair = Mutex::new((obj(json!({"theme": "light"})), st));
        assert_eq!(run(&c, "A", &pair), Err(NEWER.to_string()));
        assert!(sets(&log).is_empty());
        // and a pull of it keeps the local settings
        let mut st = state(Some("A"), None);
        let mut prefs = obj(json!({"theme": "light"}));
        let r = st.settle(&mut prefs, "A", "a", fetch(&c).map(|f| f.remote), false);
        assert!(!r.changed && !r.push);
        assert_eq!(prefs["theme"], "light");
    }

    fn other_copy(settings: Value, updated: &str, gen: &str) -> Value {
        let (parts, manifest) = encode(&obj(settings), updated, gen).unwrap();
        meta_of(&parts, &manifest)
    }

    #[test]
    fn a_write_from_another_computer_since_the_read_is_merged_and_retried() {
        let log: Log = Default::default();
        // right after our first read, another computer writes its copy
        let (c, srv) = fake(CAPS, Srv { others: vec![other_copy(json!({"density": "compact", "theme": "system"}), "2026-11-25T10:00:00.000Z", "0123")], ..Default::default() }, &log);
        let mut st = state(Some("A"), None);
        let mut prefs = obj(json!({"theme": "dark"}));
        st.start(&prefs);
        prefs.insert("theme".into(), json!("light"));
        st.saved(&prefs, ms_of("2026-11-25T11:00:00.000Z").unwrap());
        let pair = Mutex::new((prefs, st));
        assert_eq!(run(&c, "A", &pair), Ok(true));
        let calls = sets(&log);
        assert_eq!(calls.len(), 2, "refused, then written in the new state");
        assert_eq!((calls[0][1]["ifInState"].as_str(), calls[1][1]["ifInState"].as_str()), (Some("s0"), Some("s1")));
        // the other computer's density came in; this computer's later theme stayed
        let g = pair.lock().unwrap();
        assert_eq!((g.0["density"].as_str(), g.0["theme"].as_str()), (Some("compact"), Some("light")));
        drop(g);
        let m = srv.lock().unwrap().meta.clone();
        let Remote::Blob { settings, .. } = decode(&m) else { panic!() };
        assert_eq!((settings["density"].as_str(), settings["theme"].as_str()), (Some("compact"), Some("light")));
        // one generation: the other computer's parts went with our write
        assert!(m.as_object().unwrap().keys().all(|k| !k.contains(".0123.")), "{m}");
    }

    #[test]
    fn after_three_retries_the_last_writer_wins() {
        let log: Log = Default::default();
        // another computer writes after every one of our reads
        let others = (0..10).map(|i| other_copy(json!({"density": "compact"}), &format!("2026-11-25T10:00:0{i}.000Z"), &format!("a{i}"))).collect();
        let (c, srv) = fake(CAPS, Srv { others, ..Default::default() }, &log);
        let mut st = state(Some("A"), None);
        st.dirty.insert("theme".into(), json!(ms_of("2026-11-25T11:00:00.000Z").unwrap()));
        let pair = Mutex::new((obj(json!({"theme": "light"})), st));
        assert_eq!(run(&c, "A", &pair), Ok(true));
        let calls = sets(&log);
        assert_eq!(calls.len(), 5, "four guarded tries, then one without");
        assert!(calls[..4].iter().all(|c| c[1]["ifInState"].is_string()));
        assert!(calls[4][1].get("ifInState").is_none());
        assert!(pair.lock().unwrap().1.dirty.is_empty());
        // ours is the copy left (the other computer's copy came in before it: newest wins)
        let m = srv.lock().unwrap().meta.clone();
        let Remote::Blob { settings, .. } = decode(&m) else { panic!("{m}") };
        assert_eq!((settings["theme"].as_str(), settings["density"].as_str()), (Some("light"), Some("compact")));
    }

    #[test]
    fn flushes_are_waited_for() {
        let t = std::time::Instant::now();
        counted(|| std::thread::sleep(std::time::Duration::from_millis(150)));
        wait_flushes(std::time::Duration::from_secs(5));
        assert!(t.elapsed() >= std::time::Duration::from_millis(140));
        // nothing running: no wait
        let t = std::time::Instant::now();
        wait_flushes(std::time::Duration::from_secs(5));
        assert!(t.elapsed() < std::time::Duration::from_secs(1));
    }
}
