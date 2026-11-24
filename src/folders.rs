//! Folder preferences and tree logic (Bulwark folder settings, sidebar and notification mutes).
//!
//! The values live in prefs.json under Bulwark's own keys and formats, so a settings export moves
//! between the two apps: `folderIcons` {mailboxId: icon name}, `folderCountModes` {mailboxId:
//! "unread" | "all" | "none"} and `notificationMutes` {accounts: [accountId], mailboxes:
//! {"accountId/mailboxId": bool}} (lib/notification-mutes.ts, lib/folder-counts.ts,
//! stores/settings-store.ts:946-947). Pinned folders have no Bulwark key; they use Barbican's own
//! `pinnedFolders` (a list of mailbox ids).
use serde_json::{json, Map, Value};
use std::collections::{BTreeMap, HashMap, HashSet};

/// Bulwark folder-settings.tsx STANDARD_ROLES (:49), in its order.
pub const STANDARD_ROLES: [(&str, &str); 6] = [
    ("inbox", "Inbox"), ("drafts", "Drafts"), ("sent", "Sent"), ("trash", "Trash"), ("junk", "Spam"), ("archive", "Archive"),
];

pub const KEY_ICONS: &str = "folderIcons";
pub const KEY_COUNT_MODES: &str = "folderCountModes";
pub const KEY_MUTES: &str = "notificationMutes";
pub const KEY_PINNED: &str = "pinnedFolders";

// ---------------- count modes (lib/folder-counts.ts) ----------------

/// A folder's own counter (Bulwark FolderCountMode, folder-counts.ts:9).
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum CountMode { Unread, All, None }

impl CountMode {
    pub const ALL: [CountMode; 3] = [CountMode::Unread, CountMode::All, CountMode::None];
    /// isFolderCountMode (folder-counts.ts:14): anything else is "not set".
    pub fn parse(s: &str) -> Option<CountMode> {
        match s { "unread" => Some(CountMode::Unread), "all" => Some(CountMode::All), "none" => Some(CountMode::None), _ => None }
    }
    pub fn name(self) -> &'static str {
        match self { CountMode::Unread => "unread", CountMode::All => "all", CountMode::None => "none" }
    }
    /// Bulwark's count_mode_* labels (folder-settings.tsx:779-781).
    pub fn label(self) -> &'static str {
        match self { CountMode::Unread => "Unread only", CountMode::All => "Unread and total", CountMode::None => "No counter" }
    }
}

/// The per-folder modes of a `folderCountModes` value; entries from another version are ignored
/// ("treat those as unset", folder-counts.ts:34).
pub fn decode_count_modes(v: Option<&Value>) -> HashMap<String, CountMode> {
    v.and_then(Value::as_object).map(|o| o.iter()
        .filter_map(|(id, m)| Some((id.clone(), CountMode::parse(m.as_str()?)?))).collect()).unwrap_or_default()
}

/// `folderCountModes` after setting (or with None clearing) one folder (setCountMode,
/// folder-settings.tsx:784-787: the entry is dropped first, then added when it is a valid mode).
pub fn encode_count_mode(v: Option<&Value>, id: &str, mode: Option<CountMode>) -> Value {
    let mut o: Map<String, Value> = v.and_then(Value::as_object).cloned().unwrap_or_default();
    o.remove(id);
    if let Some(m) = mode { o.insert(id.into(), json!(m.name())); }
    Value::Object(o)
}

/// The counter text of a folder with its own mode (resolveFolderCounts, folder-counts.ts:39-56,
/// the non-compact layout): "unread" shows the unread count, "all" unread and total, "none"
/// nothing. A folder without a mode keeps mail.rs's default text.
pub fn count_text(mode: CountMode, unread: i64, total: i64) -> String {
    match mode {
        CountMode::None => String::new(),
        CountMode::Unread => if unread > 0 { unread.to_string() } else { String::new() },
        CountMode::All => match (unread > 0, total > 0) {
            (true, true) => format!("{unread} / {total}"),
            (true, false) => unread.to_string(),
            (false, true) => total.to_string(),
            (false, false) => String::new(),
        },
    }
}

// ---------------- icons (folderIcons) ----------------

/// The icons Bulwark's folder menu offers (folder-settings.tsx ICON_CHOICES :64-82) with the glyph
/// Barbican draws for each (see Icons.for-glyph in ui/theme.slint).
pub const ICON_CHOICES: [(&str, &str); 18] = [
    ("Folder", "\u{E8B7}"), ("Star", "\u{E734}"), ("Heart", "\u{EB51}"), ("Bookmark", "\u{E8A4}"), ("Tag", "\u{E8EC}"),
    ("Flag", "\u{E7C1}"), ("Briefcase", "\u{E821}"), ("Users", "\u{E716}"), ("Bell", "\u{E7ED}"), ("Zap", "\u{E945}"),
    ("Globe", "\u{E774}"), ("Lock", "\u{E72E}"), ("Eye", "\u{E7B3}"), ("MessageSquare", "\u{E8BD}"), ("Mail", "\u{E119}"),
    ("Inbox", "\u{E715}"), ("Archive", "\u{E7B8}"), ("FileText", "\u{E70B}"),
];

/// The glyph of a stored icon name; None for a name Barbican has no picture for (a Tabler name
/// from Bulwark's newer picker), which then falls back to the role icon.
pub fn icon_glyph(name: &str) -> Option<&'static str> {
    ICON_CHOICES.iter().find(|(n, _)| *n == name).map(|(_, g)| *g)
}

pub fn decode_icons(v: Option<&Value>) -> HashMap<String, String> {
    v.and_then(Value::as_object).map(|o| o.iter().filter_map(|(id, n)| Some((id.clone(), n.as_str()?.to_string()))).collect()).unwrap_or_default()
}

/// `folderIcons` after setting or (None) removing one folder's icon (settings-store.ts:2153-2158).
pub fn encode_icon(v: Option<&Value>, id: &str, icon: Option<&str>) -> Value {
    let mut o: Map<String, Value> = v.and_then(Value::as_object).cloned().unwrap_or_default();
    o.remove(id);
    if let Some(i) = icon { o.insert(id.into(), json!(i)); }
    Value::Object(o)
}

// ---------------- pins ----------------

pub fn decode_pinned(v: Option<&Value>) -> Vec<String> {
    v.and_then(Value::as_array).map(|a| a.iter().filter_map(|x| x.as_str().map(String::from)).collect()).unwrap_or_default()
}

/// `pinnedFolders` with one folder pinned or unpinned (a pin is kept once).
pub fn encode_pin(v: Option<&Value>, id: &str, pin: bool) -> Value {
    let mut l = decode_pinned(v);
    l.retain(|x| x != id);
    if pin { l.push(id.to_string()); }
    json!(l)
}

// ---------------- notification mutes (lib/notification-mutes.ts) ----------------

#[derive(Clone, PartialEq, Debug, Default)]
pub struct Mutes {
    /// JMAP account ids muted as a whole.
    pub accounts: Vec<String>,
    /// "accountId/mailboxId" -> muted or unmuted; missing follows the account.
    pub mailboxes: BTreeMap<String, bool>,
}

fn mailbox_key(account: &str, mailbox: &str) -> String { format!("{account}/{mailbox}") }

impl Mutes {
    /// normalizeNotificationMutes (notification-mutes.ts:26-37): repairs any stored value.
    pub fn decode(v: Option<&Value>) -> Mutes {
        let Some(o) = v.and_then(Value::as_object) else { return Mutes::default() };
        let accounts = o.get("accounts").and_then(Value::as_array)
            .map(|a| a.iter().filter_map(|x| x.as_str().filter(|s| !s.is_empty()).map(String::from)).collect()).unwrap_or_default();
        let mailboxes = o.get("mailboxes").and_then(Value::as_object)
            .map(|m| m.iter().filter_map(|(k, b)| Some((k.clone(), b.as_bool()?))).collect()).unwrap_or_default();
        Mutes { accounts, mailboxes }
    }

    pub fn encode(&self) -> Value {
        json!({"accounts": self.accounts, "mailboxes": self.mailboxes})
    }

    pub fn account_muted(&self, account: &str) -> bool { self.accounts.iter().any(|a| a == account) }

    /// The folder's own setting, or None when it follows its account (mailboxMuteOverride).
    pub fn override_of(&self, account: &str, mailbox: &str) -> Option<bool> {
        self.mailboxes.get(&mailbox_key(account, mailbox)).copied()
    }

    /// isMailboxMuted (:56-58): its own setting always wins over the account's.
    pub fn muted(&self, account: &str, mailbox: &str) -> bool {
        self.override_of(account, mailbox).unwrap_or_else(|| self.account_muted(account))
    }

    /// setMailboxMuted (:88-100): setting it back to what the account says drops the folder's own
    /// entry, so it follows the account again later.
    pub fn set_muted(&mut self, account: &str, mailbox: &str, muted: bool) {
        let key = mailbox_key(account, mailbox);
        if muted == self.account_muted(account) { self.mailboxes.remove(&key); } else { self.mailboxes.insert(key, muted); }
    }
}

/// The folders of a new message that count for a notification: just the Inbox with
/// pushNotifyInboxOnly, else every one but Junk, Trash, Sent and Drafts (new-mail.ts:44-46).
pub fn counted_mailboxes(mailbox_ids: &[String], inbox_only: bool, inbox: &str, skip: &[String]) -> Vec<String> {
    mailbox_ids.iter().filter(|m| if inbox_only { m.as_str() == inbox } else { !skip.contains(m) }).cloned().collect()
}

/// Whether a new message stays silent: every folder that counts is muted. Mail filed in a muted
/// folder and an unmuted one still notifies (new-mail.ts:47).
pub fn silenced(mutes: &Mutes, account: &str, counted: &[String]) -> bool {
    !counted.is_empty() && counted.iter().all(|m| mutes.muted(account, m))
}

/// The folder ids of an Email/get object (`mailboxIds` is {id: true}).
pub fn mailbox_ids(m: &Value) -> Vec<String> {
    m["mailboxIds"].as_object().map(|o| o.iter().filter(|(_, v)| v.as_bool() == Some(true)).map(|(k, _)| k.clone()).collect()).unwrap_or_default()
}

// ---------------- tree ----------------

/// `id` and every folder below it, parents before children. A cycle in the server's data ends
/// the walk instead of looping.
pub fn subtree(list: &[Value], id: &str) -> Vec<String> {
    let mut out = vec![id.to_string()];
    let mut seen: HashSet<String> = out.iter().cloned().collect();
    let mut i = 0;
    while i < out.len() {
        let parent = out[i].clone();
        for m in list.iter().filter(|m| m["parentId"].as_str() == Some(parent.as_str())) {
            if let Some(c) = m["id"].as_str() {
                if seen.insert(c.to_string()) { out.push(c.to_string()); }
            }
        }
        i += 1;
    }
    out
}

/// Whether any folder has `id` as its parent.
pub fn has_children(list: &[Value], id: &str) -> bool { list.iter().any(|m| m["parentId"].as_str() == Some(id)) }

/// The ids in groups of at most `max` (the server's maxObjectsInSet; at least one per group).
pub fn batches<T>(items: &[T], max: usize) -> Vec<&[T]> { items.chunks(max.max(1)).collect() }

/// The Mailbox/set updates that give `id` the standard role `role` (None removes its role): the
/// folder of the same account that holds the role loses it first, one role per account
/// (setMailboxRole, email-store.ts:8373-8390). The updates run in order, one request each.
pub fn role_patches(list: &[Value], id: &str, role: Option<&str>) -> Vec<(String, Value)> {
    let mut out = vec![];
    if let Some(r) = role {
        for m in list.iter().filter(|m| m["role"].as_str() == Some(r) && m["id"].as_str() != Some(id)) {
            if let Some(h) = m["id"].as_str() { out.push((h.to_string(), json!({"role": Value::Null}))); }
        }
    }
    out.push((id.to_string(), json!({"role": role})));
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    fn boxes() -> Vec<Value> {
        vec![
            json!({"id": "in", "role": "inbox"}),
            json!({"id": "a", "parentId": null, "name": "A"}),
            json!({"id": "a1", "parentId": "a"}),
            json!({"id": "a2", "parentId": "a"}),
            json!({"id": "a1x", "parentId": "a1"}),
            json!({"id": "b", "parentId": "", "role": "archive"}),
        ]
    }

    #[test]
    fn count_mode_values_round_trip_and_ignore_strangers() {
        let v = json!({"a": "all", "b": "unread", "c": "none", "d": "bogus", "e": 3});
        let m = decode_count_modes(Some(&v));
        assert_eq!(m.len(), 3);
        assert_eq!(m["a"], CountMode::All);
        assert_eq!(m["c"], CountMode::None);
        assert!(decode_count_modes(Some(&json!([1]))).is_empty());
        assert!(decode_count_modes(None).is_empty());
        for mode in CountMode::ALL { assert_eq!(CountMode::parse(mode.name()), Some(mode)); }
    }

    #[test]
    fn setting_a_count_mode_replaces_or_clears_only_that_folder() {
        let v = json!({"a": "all", "b": "none"});
        assert_eq!(encode_count_mode(Some(&v), "a", Some(CountMode::Unread)), json!({"a": "unread", "b": "none"}));
        assert_eq!(encode_count_mode(Some(&v), "b", None), json!({"a": "all"}));
        assert_eq!(encode_count_mode(None, "x", Some(CountMode::None)), json!({"x": "none"}));
    }

    #[test]
    fn counter_text_follows_the_mode() {
        assert_eq!(count_text(CountMode::None, 5, 9), "");
        assert_eq!(count_text(CountMode::Unread, 5, 9), "5");
        assert_eq!(count_text(CountMode::Unread, 0, 9), "");
        assert_eq!(count_text(CountMode::All, 5, 9), "5 / 9");
        assert_eq!(count_text(CountMode::All, 0, 9), "9");
        assert_eq!(count_text(CountMode::All, 2, 0), "2");
        assert_eq!(count_text(CountMode::All, 0, 0), "");
    }

    #[test]
    fn icons_round_trip_in_bulwarks_shape() {
        let v = encode_icon(None, "a", Some("Star"));
        assert_eq!(v, json!({"a": "Star"}));
        let v = encode_icon(Some(&v), "b", Some("Heart"));
        assert_eq!(decode_icons(Some(&v))["b"], "Heart");
        assert_eq!(encode_icon(Some(&v), "a", None), json!({"b": "Heart"}));
        assert_eq!(icon_glyph("Star"), Some("\u{E734}"));
        // a name from a newer Bulwark picker has no glyph here: the role icon stays
        assert_eq!(icon_glyph("tabler:rocket"), None);
        let glyphs: HashSet<_> = ICON_CHOICES.iter().map(|c| c.1).collect();
        assert_eq!(glyphs.len(), ICON_CHOICES.len());
    }

    #[test]
    fn pins_are_kept_once() {
        let v = encode_pin(None, "a", true);
        let v = encode_pin(Some(&v), "b", true);
        let v = encode_pin(Some(&v), "a", true);
        assert_eq!(decode_pinned(Some(&v)), vec!["b".to_string(), "a".to_string()]);
        assert_eq!(decode_pinned(Some(&encode_pin(Some(&v), "b", false))), vec!["a".to_string()]);
        assert!(decode_pinned(Some(&json!("x"))).is_empty());
    }

    #[test]
    fn mutes_decode_repairs_the_value() {
        let m = Mutes::decode(Some(&json!({"accounts": ["u1", "", 5], "mailboxes": {"u1/x": true, "u1/y": "no", "u2/z": false}})));
        assert_eq!(m.accounts, vec!["u1".to_string()]);
        assert_eq!(m.mailboxes.len(), 2);
        assert_eq!(Mutes::decode(Some(&json!([1]))), Mutes::default());
        assert_eq!(Mutes::decode(None), Mutes::default());
        assert_eq!(Mutes::decode(Some(&m.encode())), m);
    }

    #[test]
    fn a_folders_own_mute_wins_over_its_account() {
        let mut m = Mutes::default();
        assert!(!m.muted("u1", "in"));
        m.set_muted("u1", "in", true);
        assert!(m.muted("u1", "in"));
        assert!(!m.muted("u2", "in"), "another account's folder of the same id is not muted");
        m.accounts.push("u1".into());
        assert!(m.muted("u1", "other"));
        m.set_muted("u1", "other", false);
        assert!(!m.muted("u1", "other"));
        assert_eq!(m.override_of("u1", "other"), Some(false));
        // back to what the account says: the entry goes
        m.set_muted("u1", "other", true);
        assert_eq!(m.override_of("u1", "other"), None);
        m.set_muted("u1", "in", false);
        m.accounts.clear();
        m.set_muted("u1", "in", false);
        assert!(m.mailboxes.is_empty());
    }

    #[test]
    fn mute_format_matches_bulwarks_export() {
        let mut m = Mutes::default();
        m.set_muted("acc", "mb1", true);
        assert_eq!(m.encode(), json!({"accounts": [], "mailboxes": {"acc/mb1": true}}));
    }

    #[test]
    fn mail_in_a_muted_and_an_unmuted_folder_still_notifies() {
        let mut m = Mutes::default();
        m.set_muted("u1", "in", true);
        let skip = vec!["sent".to_string()];
        let only_in = counted_mailboxes(&["in".into()], false, "in", &skip);
        assert!(silenced(&m, "u1", &only_in));
        let both = counted_mailboxes(&["in".into(), "news".into()], false, "in", &skip);
        assert!(!silenced(&m, "u1", &both));
        m.set_muted("u1", "news", true);
        assert!(silenced(&m, "u1", &both));
        // Sent does not count, so a mute of the Inbox silences a copy kept in Sent
        let with_sent = counted_mailboxes(&["in".into(), "sent".into()], false, "in", &skip);
        assert_eq!(with_sent, vec!["in".to_string()]);
        assert!(silenced(&m, "u1", &with_sent));
        // pushNotifyInboxOnly: only the Inbox counts
        let inbox_only = counted_mailboxes(&["news".into(), "in".into()], true, "in", &skip);
        assert_eq!(inbox_only, vec!["in".to_string()]);
        // nothing counts: not silenced here (the caller's other rules decide)
        assert!(!silenced(&m, "u1", &[]));
        // the mute is per account
        assert!(!silenced(&m, "u2", &only_in));
    }

    #[test]
    fn mailbox_ids_read_the_true_entries() {
        assert_eq!(mailbox_ids(&json!({"mailboxIds": {"a": true, "b": false}})), vec!["a".to_string()]);
        assert!(mailbox_ids(&json!({})).is_empty());
    }

    #[test]
    fn subtree_walks_parents_before_children() {
        let l = boxes();
        assert_eq!(subtree(&l, "a"), vec!["a", "a1", "a2", "a1x"]);
        assert_eq!(subtree(&l, "a1"), vec!["a1", "a1x"]);
        assert_eq!(subtree(&l, "in"), vec!["in"]);
        assert_eq!(subtree(&l, "gone"), vec!["gone"]);
        assert!(has_children(&l, "a"));
        assert!(!has_children(&l, "a2"));
    }

    #[test]
    fn subtree_survives_a_cycle() {
        let l = vec![json!({"id": "x", "parentId": "y"}), json!({"id": "y", "parentId": "x"})];
        assert_eq!(subtree(&l, "x"), vec!["x", "y"]);
    }

    #[test]
    fn batches_respect_the_server_limit() {
        let ids: Vec<u32> = (0..10).collect();
        let b = batches(&ids, 4);
        assert_eq!(b.iter().map(|c| c.len()).collect::<Vec<_>>(), vec![4, 4, 2]);
        assert_eq!(batches(&ids, 500).len(), 1);
        assert_eq!(batches(&ids, 0).len(), 10, "a limit of 0 still moves forward");
        assert!(batches::<u32>(&[], 4).is_empty());
    }

    #[test]
    fn a_role_moves_to_one_folder_per_account() {
        let l = boxes();
        let p = role_patches(&l, "a", Some("archive"));
        assert_eq!(p, vec![("b".to_string(), json!({"role": null})), ("a".to_string(), json!({"role": "archive"}))]);
        // the folder that already has it just keeps it
        assert_eq!(role_patches(&l, "b", Some("archive")), vec![("b".to_string(), json!({"role": "archive"}))]);
        // removing a role touches only that folder
        assert_eq!(role_patches(&l, "b", None), vec![("b".to_string(), json!({"role": null}))]);
    }
}
