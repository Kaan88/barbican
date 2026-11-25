//! Barbican's own settings sync. Bulwark keeps a login's settings as one encrypted blob on its
//! own web server (lib/settings-sync.ts, app/api/settings/route.ts) and pulls / pushes it from
//! the settings store (stores/settings-store.ts loadFromServer, enableSync, the auto-sync block).
//! Barbican has no web server, so the blob lives in the account itself: the Inbox's JMAP metadata
//! (`urn:ietf:params:jmap:metadata`, as the templates in src/templates.rs), under Barbican's own
//! namespace `org.barbican.settings` (not Bulwark's: the two apps' settings differ).
//!
//! * The blob is split over entries of at most 8 KB each (Stalwart's per-entry limit), named by a
//!   random generation (`part.<gen>.<i>`). The parts are written first, then the manifest naming
//!   the generation, the part count and the SHA-256 of the parts; a reader that finds parts not
//!   matching the manifest takes it as "not ready" and keeps the local settings.
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
/// At most this many parts, and this much JSON in all: pulled data is untrusted input.
pub const MAX_PARTS: usize = 16;
pub const MAX_BLOB_BYTES: usize = 96 * 1024;
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
/// computer load remote content or send data to a new place.
const DENY: &[&str] = &[
    "enabledPlugins", "pluginBindings", "pluginsOffNoticeShown", DISABLED_KEY, "proInterface", "doNotDisturb",
    "trustedSenders", "trustedSendersFrom", "trustedSendersAddressBook", "externalContentPolicy",
    "gravatarAvatars", "senderFavicons", "pushRelayUrl", "phishingReportAddress",
    "vacationCalendarBlocks", "pinnedFolders", "subAddressTags", "replyDockShare", "explicitSettings",
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
    ("calendarWorkingDays", "days"), ("calendarDayStartHour", "oneof"), ("readReceiptResponse", "oneof"),
    ("requestReadReceiptDefault", "bool"), ("showAdvancedSettings", "bool"), ("showAccountDot", "bool"),
    ("calendarNarrowWeekAllDays", "bool"), ("vacationBlockCalendar", "bool"), ("spellCheck", "bool"),
    ("externalSenderLabel", "bool"), ("externalRecipientConfirm", "bool"), ("birthdayCalendarColor", "text"),
    ("emailKeywords", "arr"), ("sharedCalendarColors", "obj"), ("folderIcons", "obj"), ("folderCountModes", "obj"),
    ("notificationMutes", "obj"), ("preferredIdentityIds", "obj"),
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
    if parts.len() > MAX_PARTS { return Err("Your settings are too big to sync.".into()); }
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
    Blob { manifest: Manifest, settings: Map<String, Value> },
}

/// The blob in an Inbox metadata object, checked against its manifest and validated.
pub fn decode(meta: &Value) -> Remote {
    let m = &meta[manifest_key()];
    if m.is_null() { return Remote::Nothing; }
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
    /// The `updatedAt` of the owner's server copy last applied or written here.
    pub base: Option<String>,
    /// Synced keys changed here that the server has not taken yet: key -> when (ms).
    pub dirty: Map<String, Value>,
    /// The synced values as last seen (in memory only): what a save is compared with.
    seen: Option<Map<String, Value>>,
    disabled: Option<bool>,
}

/// What a local save changed.
#[derive(Debug, Default, PartialEq)]
pub struct Saved { pub synced: bool, pub toggled: Option<bool> }

/// What a settled pull did, and what comes next.
#[derive(Debug, Default, PartialEq)]
pub struct Settled { pub changed: bool, pub push: bool, pub blocked: bool }

impl DeviceState {
    pub fn from_json(v: &Value) -> DeviceState {
        DeviceState {
            owner: v["owner"].as_str().map(String::from), base: v["base"].as_str().map(String::from),
            dirty: v["dirty"].as_object().map(|d| d.iter().filter(|(k, t)| is_synced(k) && t.is_i64()).map(|(k, t)| (k.clone(), t.clone())).collect()).unwrap_or_default(),
            seen: None, disabled: None,
        }
    }

    pub fn to_json(&self) -> Value { json!({"owner": self.owner, "base": self.base, "dirty": self.dirty}) }

    /// The values the device starts with (no change is recorded for them).
    pub fn start(&mut self, prefs: &Map<String, Value>) {
        self.seen = Some(view(prefs));
        self.disabled = Some(prefs.get(DISABLED_KEY) == Some(&json!(true)));
    }

    /// A save of `prefs` at `now_ms`: the synced keys whose value changed are marked as not sent.
    pub fn saved(&mut self, prefs: &Map<String, Value>, now_ms: i64) -> Saved {
        let mut out = Saved::default();
        let disabled = prefs.get(DISABLED_KEY) == Some(&json!(true));
        if self.disabled.is_some_and(|d| d != disabled) { out.toggled = Some(disabled); }
        self.disabled = Some(disabled);
        let now = view(prefs);
        let Some(seen) = self.seen.replace(now.clone()) else { return out };
        for k in seen.keys().chain(now.keys()) {
            if seen.get(k) != now.get(k) { self.dirty.insert(k.clone(), json!(now_ms)); out.synced = true; }
        }
        out
    }

    /// Bulwark loadFromServer for `owner` (and its Bulwark account id `acct`): `remote` is the
    /// pull's outcome. The caller saves `prefs` when `changed`, then pushes when `push`.
    pub fn settle(&mut self, prefs: &mut Map<String, Value>, owner: &str, acct: &str, remote: Result<Remote, String>, keep_cross: bool) -> Settled {
        let held_by_other = self.owner.as_deref().is_some_and(|o| o != owner);
        let mut out = Settled::default();
        if held_by_other {
            // whatever the pull found, the other account's synced settings and unsent changes go
            out.changed = reset_synced(prefs, keep_cross);
            self.dirty.clear();
            self.base = None;
        }
        match remote {
            Err(_) | Ok(Remote::NotReady) if held_by_other => {
                self.owner = None;
                out.blocked = true;
            }
            Err(_) | Ok(Remote::NotReady) => { self.owner = Some(owner.to_string()); }
            Ok(Remote::Nothing) => {
                // a new account starts from defaults, not from what another left here (#1185)
                self.owner = Some(owner.to_string());
                self.base = None;
                out.push = !held_by_other;
            }
            Ok(Remote::Blob { manifest, settings }) => {
                let newer = self.base.as_deref().and_then(ms_of).is_none_or(|b| b < manifest.updated_ms);
                if newer {
                    out.changed |= !merge(prefs, &settings, &mut self.dirty, manifest.updated_ms, acct, keep_cross).is_empty();
                    self.base = Some(manifest.updated);
                }
                self.owner = Some(owner.to_string());
                out.push = !self.dirty.is_empty();
            }
        }
        self.seen = Some(view(prefs));
        out
    }

    /// Before a push for `owner`: a server copy newer than [`base`](Self::base) is merged in first
    /// (local unsent changes stay). None when the local settings are not `owner`'s (never push
    /// one account's state under another) or the server could not be read.
    pub fn prepare(&mut self, prefs: &mut Map<String, Value>, owner: &str, acct: &str, remote: &Remote, now_ms: i64, keep_cross: bool) -> Option<Push> {
        if self.owner.as_deref() != Some(owner) { return None; }
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
        if self.owner.as_deref() != Some(owner) { return; }
        self.base = Some(p.updated.clone());
        for (k, t) in &p.sent { if self.dirty.get(k) == Some(t) { self.dirty.remove(k); } }
    }
}

/// A push ready to write.
#[derive(Clone, Debug, PartialEq)]
pub struct Push { pub blob: Map<String, Value>, pub sent: Map<String, Value>, pub updated: String, pub changed: bool }

// ---------------------------------------------------------------- requests

/// Whether the server keeps metadata (the sync needs it).
pub fn supported(c: &Client) -> bool { c.has("urn:ietf:params:jmap:metadata") }

fn inbox(c: &Client) -> Result<String, String> { c.role_ids()?.get("inbox").cloned().ok_or_else(|| "No Inbox.".into()) }

/// The stored blob and the part entries there now.
pub fn fetch(c: &Client) -> Result<(Remote, Vec<String>), String> {
    let id = inbox(c)?;
    let r = c.call(json!([["Mailbox/get", {"accountId": c.account, "ids": [id], "properties": ["metadata"]}, "m"]]))?;
    let meta = &r[0][1]["list"][0]["metadata"];
    Ok((decode(meta), part_keys(meta)))
}

fn new_gen() -> String {
    let mut b = [0u8; 4];
    let _ = getrandom::getrandom(&mut b);
    b.iter().map(|x| format!("{x:02x}")).collect()
}

/// Write `settings` as a new generation: the parts, then (a second request) the manifest, which
/// also removes the parts in `old` (another generation's) so they do not pile up.
pub fn store(c: &Client, settings: &Map<String, Value>, updated: &str, old: &[String]) -> Result<(), String> {
    store_gen(c, settings, updated, old, &new_gen())
}

fn store_gen(c: &Client, settings: &Map<String, Value>, updated: &str, old: &[String], gen: &str) -> Result<(), String> {
    let (parts, manifest) = encode(settings, updated, gen)?;
    let inbox = inbox(c)?;
    let mut first = Map::new();
    for (k, v) in &parts { first.insert(format!("metadata/{k}"), v.clone()); }
    let (_, bad) = c.update("Mailbox/set", &c.account, vec![(inbox.clone(), Value::Object(first))])?;
    if !bad.is_empty() { return Err(bad.join("; ")); }
    let mut second = Map::new();
    second.insert(format!("metadata/{}", manifest_key()), manifest);
    let fresh = format!("{}{gen}.", part_prefix());
    for k in old { if !k.starts_with(&fresh) { second.insert(format!("metadata/{k}"), Value::Null); } }
    let (_, bad) = c.update("Mailbox/set", &c.account, vec![(inbox, Value::Object(second))])?;
    if bad.is_empty() { Ok(()) } else { Err(bad.join("; ")) }
}

// ---------------------------------------------------------------- the running program

/// The account signed in now, for the sync.
#[derive(Clone)]
struct Active { client: std::sync::Arc<Client>, owner: String, acct: String, gen: u64, settled: bool, blocked: bool }

static STATE: std::sync::Mutex<Option<DeviceState>> = std::sync::Mutex::new(None);
static ACTIVE: std::sync::Mutex<Option<Active>> = std::sync::Mutex::new(None);
static NOTE: std::sync::Mutex<Option<&'static str>> = std::sync::Mutex::new(None);
static GEN: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);
#[cfg(not(test))]
static PUSH_SEQ: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);
static PUSHING: std::sync::Mutex<()> = std::sync::Mutex::new(());
type Applied = Box<dyn Fn() + Send + Sync>;
static ON_APPLIED: std::sync::OnceLock<Applied> = std::sync::OnceLock::new();

const UNSUPPORTED: &str = "Not available: this server keeps no account metadata (Stalwart 1.0 or later does), so your settings stay on this computer.";

/// The Settings > About row's description.
pub fn row_desc() -> String {
    match *NOTE.lock().unwrap_or_else(|e| e.into_inner()) {
        Some(n) => n.to_string(),
        None => "Your other computers signed in to this account get the same Barbican settings, kept in your account on the server. Plugins, sign-ins, trusted senders, remote content and this computer's desktop settings stay here.".into(),
    }
}

fn state_path() -> std::path::PathBuf { crate::config_dir().join("settings-sync.json") }

fn persist(st: &DeviceState) {
    #[cfg(not(test))]
    {
        let _ = std::fs::create_dir_all(crate::config_dir());
        let _ = std::fs::write(state_path(), st.to_json().to_string());
    }
    #[cfg(test)]
    let _ = st;
}

/// Run `f` on this device's sync state (loaded on first use). Never call while holding it: the
/// lock order is the live settings, then this.
fn with_state<T>(f: impl FnOnce(&mut DeviceState) -> T) -> T {
    let mut g = STATE.lock().unwrap_or_else(|e| e.into_inner());
    let st = g.get_or_insert_with(|| {
        let v = std::fs::read_to_string(state_path()).ok().and_then(|t| serde_json::from_str::<Value>(&t).ok()).unwrap_or(Value::Null);
        DeviceState::from_json(&v)
    });
    f(st)
}

fn now_ms() -> i64 { chrono::Utc::now().timestamp_millis() }

fn disabled() -> bool { crate::settings::Settings::load().bool(DISABLED_KEY, false) }

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
        if s.synced { persist(st); }
        s
    });
    if s.toggled == Some(false) { std::thread::spawn(restart); }
    #[cfg(not(test))]
    if s.synced { schedule_push(); }
}

fn keep_cross() -> bool { crate::oauth::accounts().len() > 1 }

/// A sign-in, a restored session or an account switch: pull for `c` in the background. Nothing
/// is pushed for it until this pull has settled.
pub fn begin(c: std::sync::Arc<Client>) {
    let gen = GEN.fetch_add(1, std::sync::atomic::Ordering::SeqCst) + 1;
    let ok = supported(&c);
    *NOTE.lock().unwrap_or_else(|e| e.into_inner()) = if ok { None } else { Some(UNSUPPORTED) };
    let a = Active { owner: c.state_key(), acct: crate::identities::account_key(&c.username, &c.base), client: c, gen, settled: false, blocked: false };
    *ACTIVE.lock().unwrap_or_else(|e| e.into_inner()) = Some(a.clone());
    if !may_pull(ok, disabled()) { return; }
    std::thread::spawn(move || pull(a));
}

/// Sign-out or "add account": nothing more goes to (or comes from) the account that was in.
pub fn end() {
    GEN.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
    *ACTIVE.lock().unwrap_or_else(|e| e.into_inner()) = None;
}

fn current(gen: u64) -> bool { GEN.load(std::sync::atomic::Ordering::SeqCst) == gen }

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
    let remote = fetch(&a.client).map(|r| r.0);
    let keep = keep_cross();
    let mut out = None;
    crate::settings::with_live(|s| {
        if !current(a.gen) { return; }
        let r = with_state(|st| {
            let r = st.settle(&mut s.prefs, &a.owner, &a.acct, remote, keep);
            persist(st);
            r
        });
        // the state saw these values already: the save records no change of ours
        if r.changed { s.save(); }
        out = Some(r);
    });
    let Some(r) = out else { return };
    if let Some(x) = ACTIVE.lock().unwrap_or_else(|e| e.into_inner()).as_mut().filter(|x| x.gen == a.gen) {
        x.settled = true;
        x.blocked = r.blocked;
    }
    if r.changed { applied(); }
    if r.push { push_now(); }
}

#[cfg(not(test))]
fn schedule_push() {
    let seq = PUSH_SEQ.fetch_add(1, std::sync::atomic::Ordering::SeqCst) + 1;
    std::thread::spawn(move || {
        std::thread::sleep(DEBOUNCE);
        if PUSH_SEQ.load(std::sync::atomic::Ordering::SeqCst) == seq { push_now(); }
    });
}

/// Push the synced settings to the account signed in, if its pull settled. One at a time.
fn push_now() {
    let _one = PUSHING.lock().unwrap_or_else(|e| e.into_inner());
    let Some(a) = ACTIVE.lock().unwrap_or_else(|e| e.into_inner()).clone() else { return };
    if !may_push(a.settled, a.blocked, supported(&a.client), disabled()) { return; }
    let Ok((remote, old)) = fetch(&a.client) else { return };
    let keep = keep_cross();
    let mut job = None;
    crate::settings::with_live(|s| {
        if !current(a.gen) { return; }
        let p = with_state(|st| {
            let p = st.prepare(&mut s.prefs, &a.owner, &a.acct, &remote, now_ms(), keep);
            persist(st);
            p
        });
        if p.as_ref().is_some_and(|p| p.changed) { s.save(); }
        job = p;
    });
    let Some(p) = job else { return };
    if p.changed { applied(); }
    if !current(a.gen) { return; }
    // a failed write keeps the changes marked: the next change or sign-in sends them
    if store(&a.client, &p.blob, &p.updated, &old).is_ok() {
        with_state(|st| { st.pushed(&a.owner, &p); persist(st); });
    }
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

    #[test]
    fn a_blob_round_trips_through_parts_that_each_fit_an_entry() {
        // quotes, backslashes, control characters and non-ASCII all count as JSON spells them
        let mut s = Map::new();
        s.insert("emailKeywords".into(), json!((0..150).map(|i| json!({"id": format!("t{i}"), "label": format!("\"Tag\\ {i}\n\u{1}ünï ✓"), "color": "#ff0000"})).collect::<Vec<_>>()));
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
        // an empty blob is one part; a too-big one is refused
        assert_eq!(chunk(""), vec![String::new()]);
        let mut big = Map::new();
        big.insert("x".into(), json!("y".repeat(MAX_BLOB_BYTES)));
        assert!(encode(&big, "2026-11-25T10:00:00.000Z", "aa").unwrap_err().contains("too big"));
        // a piece never splits inside the budget wrongly: the exact budget fits, one more does not
        let exact = "a".repeat(MAX_ENTRY_BYTES - 2);
        assert_eq!(chunk(&exact).len(), 1);
        assert_eq!(chunk(&format!("{exact}\"")).len(), 2);
    }

    #[test]
    fn parts_that_do_not_match_the_manifest_are_not_applied() {
        let s = obj(json!({"theme": "light"}));
        let (parts, manifest) = encode(&s, "2026-11-25T10:00:00.000Z", "abcd").unwrap();
        assert!(matches!(decode(&meta_of(&parts, &manifest)), Remote::Blob { .. }));
        // a part rewritten by another device's half-done write
        let mut other = parts.clone();
        other[0].1 = json!(other[0].1.as_str().unwrap().replace("light", "dark!"));
        assert_eq!(decode(&meta_of(&other, &manifest)), Remote::NotReady);
        // a part missing, more parts than allowed, a newer format, a bad generation
        assert_eq!(decode(&meta_of(&[], &manifest)), Remote::NotReady);
        let mut m = manifest.clone();
        m["parts"] = json!(MAX_PARTS + 1);
        assert_eq!(decode(&meta_of(&parts, &m)), Remote::NotReady);
        let mut m = manifest.clone();
        m["format"] = json!(2);
        assert_eq!(decode(&meta_of(&parts, &m)), Remote::NotReady);
        let mut m = manifest.clone();
        m["gen"] = json!("../x");
        assert_eq!(decode(&meta_of(&parts, &m)), Remote::NotReady);
        assert_eq!(decode(&json!({"org.bulwarkmail.templates": {}})), Remote::Nothing);
        assert_eq!(decode(&Value::Null), Remote::Nothing);
    }

    #[test]
    fn only_allowlisted_keys_of_the_right_kind_are_taken() {
        let v = validate(&obj(json!({
            "theme": "dark", "density": "compact", "showPreview": false, "firstDayOfWeek": 0, "retentionTrashDays": 30,
            "calendarWorkingDays": [1, 2, 3], "hoverActions": ["delete", "star"], "subAddressDelimiter": "-",
            "sendDelaySeconds": 10, "fontSize": "large", "mailTextScale": 1.25, "reduceTransparency": "system",
            "showRowActionButton": null,
            // never applied, whatever a server copy says
            "enabledPlugins": ["evil"], "pluginBindings": {"evil": "C:/x.dll|h"}, "pluginGrant.evil": true, "plugin:evil": true,
            "settingsSyncDisabled": true, "trustedSenders": ["a@example.test"], "externalContentPolicy": "allow",
            "pushRelayUrl": "https://relay.example.test", "proInterface": true, "ai.key": "k", "desktop.startAtLogin": true,
            "accounts": [], "windowState": {}, "unknownThing": 1,
        })));
        for k in ["theme", "density", "showPreview", "firstDayOfWeek", "retentionTrashDays", "calendarWorkingDays", "hoverActions",
            "subAddressDelimiter", "sendDelaySeconds", "fontSize", "mailTextScale", "reduceTransparency", "showRowActionButton"] {
            assert!(v.contains_key(k), "{k} dropped");
        }
        assert_eq!(v.len(), 13, "{v:?}");
        for k in ["enabledPlugins", "pluginBindings", "settingsSyncDisabled", "trustedSenders", "externalContentPolicy", "pushRelayUrl", "gravatarAvatars", "doNotDisturb"] {
            assert!(!is_synced(k), "{k}");
        }
        assert!(is_synced("showPreview") && is_synced("preferredIdentityIds") && is_synced("theme"));
    }

    #[test]
    fn wrong_types_and_oversized_values_are_dropped() {
        let v = validate(&obj(json!({
            "showPreview": "yes", "theme": "neon", "density": 3, "firstDayOfWeek": "x", "retentionTrashDays": 99999,
            "calendarWorkingDays": [1, 9], "hoverActions": ["delete", "rm -rf"], "subAddressDelimiter": "ab",
            "sendDelaySeconds": 7, "timeZone": "z".repeat(MAX_TEXT_CHARS + 1), "folderIcons": [1],
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
    }

    fn state(owner: Option<&str>, base: Option<&str>) -> DeviceState {
        let mut st = DeviceState { owner: owner.map(String::from), base: base.map(String::from), ..Default::default() };
        st.start(&Map::new());
        st
    }

    #[test]
    fn a_server_copy_with_enabled_plugins_does_not_switch_them_on() {
        let mut st = state(Some("A"), None);
        let mut prefs = obj(json!({"enabledPlugins": [], "theme": "dark"}));
        let mut raw = Map::new();
        raw.insert("settings".into(), json!({"enabledPlugins": ["evil"], "pluginBindings": {"evil": "x"}, "theme": "light"}));
        let (parts, manifest) = encode(raw["settings"].as_object().unwrap(), "2026-11-25T10:00:00.000Z", "cd").unwrap();
        let remote = decode(&meta_of(&parts, &manifest));
        let r = st.settle(&mut prefs, "A", "ann@x", Ok(remote), false);
        assert!(r.changed);
        assert_eq!(prefs["theme"], "light");
        assert_eq!(prefs["enabledPlugins"], json!([]));
        assert!(!prefs.contains_key("pluginBindings"));
        // and merging a hand-made map that slipped past decoding takes nothing denied either
        let mut d = Map::new();
        assert!(merge(&mut prefs, &obj(json!({"enabledPlugins": ["evil"], "settingsSyncDisabled": true})), &mut d, 0, "a", false).is_empty());
    }

    #[test]
    fn nothing_stored_pushes_the_local_settings_of_a_device_that_never_synced() {
        let mut st = state(None, None);
        let mut prefs = obj(json!({"theme": "light"}));
        let r = st.settle(&mut prefs, "A", "a", Ok(Remote::Nothing), false);
        assert_eq!(r, Settled { changed: false, push: true, blocked: false });
        assert_eq!(st.owner.as_deref(), Some("A"));
        assert_eq!(prefs["theme"], "light");
        let p = st.prepare(&mut prefs, "A", "a", &Remote::Nothing, 1_000, false).unwrap();
        assert_eq!(p.blob["theme"], "light");
    }

    #[test]
    fn another_accounts_settings_are_never_pushed_under_this_one() {
        // the device mirrors A; B signs in and has nothing stored: defaults, owner B, no push
        let mut st = state(Some("A"), Some("2026-11-20T00:00:00.000Z"));
        let mut prefs = obj(json!({"theme": "light", "density": "compact", "enabledPlugins": ["p"], "preferredIdentityIds": {"a@x": "i1"}}));
        st.dirty.insert("theme".into(), json!(5));
        let r = st.settle(&mut prefs, "B", "b@x", Ok(Remote::Nothing), false);
        assert_eq!(r, Settled { changed: true, push: false, blocked: false });
        assert!(!prefs.contains_key("theme") && !prefs.contains_key("density"));
        assert_eq!(prefs["enabledPlugins"], json!(["p"]), "device settings stay");
        assert_eq!(prefs["preferredIdentityIds"], json!({"a@x": "i1"}));
        assert!(st.dirty.is_empty() && st.base.is_none());
        // B's pull failed while A's settings were here: defaults, sync blocked, no owner
        let mut st = state(Some("A"), None);
        let mut prefs = obj(json!({"theme": "light"}));
        let r = st.settle(&mut prefs, "B", "b@x", Err("offline".into()), false);
        assert_eq!(r, Settled { changed: true, push: false, blocked: true });
        assert_eq!(st.owner, None);
        assert!(prefs.is_empty());
        // and a push for an account the settings are not of is refused
        let mut st = state(Some("A"), None);
        assert!(st.prepare(&mut obj(json!({"theme": "light"})), "B", "b@x", &Remote::Nothing, 1, false).is_none());
        // the same account's failed pull keeps everything (owner set, nothing pushed now)
        let mut st = state(None, None);
        let mut prefs = obj(json!({"theme": "light"}));
        assert_eq!(st.settle(&mut prefs, "A", "a", Ok(Remote::NotReady), false), Settled::default());
        assert_eq!((st.owner.as_deref(), prefs["theme"].as_str()), (Some("A"), Some("light")));
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
        // a local change made after the server copy was written, never pushed (a quit inside the debounce)
        prefs.insert("density".into(), json!("regular"));
        let late = ms_of("2026-11-24T00:00:00.000Z").unwrap();
        assert!(st.saved(&prefs, late).synced);
        // and one made before it: the other device changed that setting later
        prefs.insert("showPreview".into(), json!(true));
        st.saved(&prefs, ms_of("2026-11-21T00:00:00.000Z").unwrap());
        let server = blob(json!({"theme": "dark", "density": "comfortable", "showPreview": null}), "2026-11-22T00:00:00.000Z");
        let r = st.settle(&mut prefs, "A", "a", Ok(server.clone()), false);
        assert_eq!(r, Settled { changed: true, push: true, blocked: false });
        assert_eq!(prefs, obj(json!({"theme": "dark", "density": "regular"})));
        assert_eq!(st.dirty.keys().collect::<Vec<_>>(), vec!["density"]);
        assert_eq!(st.base.as_deref(), Some("2026-11-22T00:00:00.000Z"));
        // the same copy again (or an older one) is not applied over later local edits
        prefs.insert("theme".into(), json!("system"));
        st.saved(&prefs, 1);
        let r = st.settle(&mut prefs, "A", "a", Ok(server), false);
        assert!(!r.changed);
        assert_eq!(prefs["theme"], "system");
        // the push is never dated before the copy it replaces, and acknowledges what it sent
        let p = st.prepare(&mut prefs, "A", "a", &blob(json!({}), "2026-11-22T00:00:00.000Z"), 0, false).unwrap();
        assert_eq!(p.updated, "2026-11-22T00:00:00.001Z");
        prefs.insert("density".into(), json!("compact"));
        st.saved(&prefs, late + 1); // changed again while the push was out: stays pending
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
        let mut st = DeviceState::default();
        let mut prefs = obj(json!({"theme": "dark"}));
        // before the start nothing is compared
        assert_eq!(st.saved(&prefs, 1), Saved::default());
        assert!(st.dirty.is_empty());
        prefs.insert("theme".into(), json!("light"));
        assert!(st.saved(&prefs, 2).synced);
        prefs.insert("enabledPlugins".into(), json!(["p"]));
        prefs.insert("winX".into(), json!(1));
        assert!(!st.saved(&prefs, 3).synced, "not synced keys are not changes");
        prefs.remove("theme");
        assert!(st.saved(&prefs, 4).synced);
        assert_eq!(st.dirty, obj(json!({"theme": 4})));
        prefs.insert(DISABLED_KEY.into(), json!(true));
        assert_eq!(st.saved(&prefs, 5).toggled, Some(true));
        prefs.insert(DISABLED_KEY.into(), json!(false));
        assert_eq!(st.saved(&prefs, 6).toggled, Some(false));
        // the state file keeps owner, base and pending changes, and drops what is not synced
        let back = DeviceState::from_json(&json!({"owner": "A", "base": "2026-11-25T00:00:00.000Z", "dirty": {"theme": 4, "enabledPlugins": 1, "density": "x"}}));
        assert_eq!((back.owner.as_deref(), back.base.as_deref(), back.dirty.clone()), (Some("A"), Some("2026-11-25T00:00:00.000Z"), obj(json!({"theme": 4}))));
        assert_eq!(DeviceState::from_json(&back.to_json()), back);
    }

    type Log = Arc<Mutex<Vec<Value>>>;

    fn fake(caps: &[&str], meta: Value, log: &Log) -> Client {
        let (log, meta) = (log.clone(), Arc::new(Mutex::new(meta)));
        Client::fake("https://mail.example.test", "A1", caps, move |calls| {
            Ok(calls.as_array().unwrap().iter().map(|c| {
                log.lock().unwrap_or_else(|e| e.into_inner()).push(c.clone());
                let m = c[0].as_str().unwrap_or("");
                let a = &c[1];
                let r = match m {
                    "Mailbox/get" if a["properties"] == json!(["metadata"]) => json!({"state": "s1", "list": [{"id": "in1", "metadata": meta.lock().unwrap().clone()}]}),
                    "Mailbox/get" => json!({"state": "s1", "list": [{"id": "in1", "role": "inbox", "name": "Inbox"}]}),
                    "Mailbox/set" => {
                        let mut m = meta.lock().unwrap();
                        if !m.is_object() { *m = json!({}); }
                        for (k, v) in a["update"]["in1"].as_object().unwrap() {
                            let k = k.strip_prefix("metadata/").unwrap();
                            if v.is_null() { m.as_object_mut().unwrap().remove(k); } else { m[k] = v.clone(); }
                        }
                        json!({"updated": {"in1": null}})
                    }
                    _ => json!({}),
                };
                json!([m, r, c[2]])
            }).collect())
        })
    }

    const META: &str = "urn:ietf:params:jmap:metadata";

    #[test]
    fn requests_write_parts_then_the_manifest_and_drop_old_parts() {
        let log: Log = Default::default();
        let c = fake(&["urn:ietf:params:jmap:core", "urn:ietf:params:jmap:mail", META], json!({"org.barbican.settings.part.old.0": "x", "org.bulwarkmail.template.t": {"name": "T"}}), &log);
        assert!(supported(&c));
        let (r, old) = fetch(&c).unwrap();
        assert_eq!((r, old.clone()), (Remote::Nothing, vec!["org.barbican.settings.part.old.0".to_string()]));
        let mut s = Map::new();
        s.insert("theme".into(), json!("light"));
        s.insert("emailKeywords".into(), json!((0..200).map(|i| json!({"id": format!("k{i}"), "label": "x".repeat(40)})).collect::<Vec<_>>()));
        log.lock().unwrap().clear();
        store_gen(&c, &s, "2026-11-25T10:00:00.000Z", &old, "beef").unwrap();
        let calls: Vec<Value> = log.lock().unwrap().iter().filter(|c| c[0] == "Mailbox/set").cloned().collect();
        assert_eq!(calls.len(), 2);
        let first = calls[0][1]["update"]["in1"].as_object().unwrap();
        assert!(first.len() > 1 && first.keys().all(|k| k.starts_with("metadata/org.barbican.settings.part.beef.")), "{first:?}");
        assert_eq!(calls[0][1]["accountId"], "A1");
        let second = calls[1][1]["update"]["in1"].as_object().unwrap();
        assert_eq!(second["metadata/org.barbican.settings.manifest"]["gen"], "beef");
        assert_eq!(second["metadata/org.barbican.settings.part.old.0"], Value::Null);
        assert_eq!(second.len(), 2);
        // read back: the same settings; Bulwark's template entry is untouched
        let (r, keys) = fetch(&c).unwrap();
        let Remote::Blob { settings, manifest } = r else { panic!() };
        assert_eq!(settings, s);
        assert_eq!(manifest.updated, "2026-11-25T10:00:00.000Z");
        assert!(keys.iter().all(|k| k.contains(".beef.")));
        let meta_now = log.lock().unwrap().iter().rev().find(|c| c[0] == "Mailbox/get").cloned().unwrap();
        assert_eq!(meta_now[1]["properties"], json!(["metadata"]));
        // without the metadata capability the sync is off
        let c = fake(&["urn:ietf:params:jmap:core", "urn:ietf:params:jmap:mail"], Value::Null, &log);
        assert!(!supported(&c));
    }

    #[test]
    fn the_disabled_switch_stops_pull_and_push() {
        assert!(may_pull(true, false) && !may_pull(true, true) && !may_pull(false, false));
        assert!(may_push(true, false, true, false));
        assert!(!may_push(true, false, true, true), "switched off");
        assert!(!may_push(false, false, true, false), "pull not settled");
        assert!(!may_push(true, true, true, false), "blocked after another account's failed pull");
        assert!(!may_push(true, false, false, false), "no metadata");
        // switching it is noticed (switching back on pulls again), and is not a synced change
        let mut st = state(Some("A"), None);
        let mut prefs = obj(json!({DISABLED_KEY: false}));
        st.start(&prefs);
        prefs.insert(DISABLED_KEY.into(), json!(true));
        let s = st.saved(&prefs, 1);
        assert_eq!(s, Saved { synced: false, toggled: Some(true) });
        assert!(st.dirty.is_empty(), "the switch itself is not synced");
        assert!(!blob_for(&prefs, "a").contains_key(DISABLED_KEY));
    }
}
