//! The mail tools (docs/AI-ASSISTANT-SPEC.md 5.4 reading, 5.5 changing) over JMAP.
//!
//! A message is named by three ids — the assistant's own id for its login, the server's account
//! id and the email id — because an email id is unique only within its account and an account id
//! only within its server (2.0, [`MessageRef`]). The tools take those three from the model and
//! refuse a message whose login is not one of the current logins.
use crate::assistant::context::{self, MessageRef};
use crate::assistant::tools::{bool_, cap, int_, json, obj, str_, str_list, Ctx, Outcome, Risk, RiskOf, Tool, MAX_IDS, MAX_RESULT};
use crate::jmap::Client;
use crate::tags::TagDef;
use serde_json::{Map, Value};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

// ---------------------------------------------------------------- the mail the tools work on

/// One login the assistant may use: Barbican's own id for it (2.0) and its JMAP client.
pub struct Login {
    /// `logins::assistant_login_id(&logins::login_key(&client.base, &client.username))`
    pub id: String,
    pub client: Client,
}

impl Login {
    /// The id for a client: [`crate::assistant::logins::assistant_login_id`] of its key.
    pub fn id_for(client: &Client) -> String {
        crate::assistant::logins::assistant_login_id(&crate::assistant::logins::login_key(&client.base, &client.username))
    }

    pub fn new(client: Client) -> Login {
        let id = Login::id_for(&client);
        Login { id, client }
    }
}

/// What the tools need from the app: the logins it has now, and the user's tags.
pub trait Mail: Send + Sync + 'static {
    /// Every login the assistant may use now. A message whose login is not one of these is
    /// refused (a login the user signed out of is not a dead id to be trusted).
    fn logins(&self) -> &[Login];
    /// The user's tags: key, name, colour.
    fn tags(&self) -> Vec<TagDef>;
    /// Create a tag; returns its key (its id).
    fn create_tag(&self, name: &str, color: &str) -> Result<String, String>;
}

/// The app's own [`Mail`]: the logins it was built with and the tag list it keeps, saved through
/// the callback the app gives it (the settings writer, in M12).
pub struct MailStore {
    logins: Vec<Login>,
    tags: Mutex<Vec<TagDef>>,
    save: Option<Arc<dyn Fn(&[TagDef]) -> Result<(), String> + Send + Sync>>,
}

impl MailStore {
    pub fn new(logins: Vec<Login>, tags: Vec<TagDef>) -> MailStore {
        MailStore { logins, tags: Mutex::new(tags), save: None }
    }

    /// Save the tags through this (the settings writer) whenever one is made.
    pub fn with_saver(mut self, save: Arc<dyn Fn(&[TagDef]) -> Result<(), String> + Send + Sync>) -> MailStore {
        self.save = Some(save);
        self
    }
}

impl Mail for MailStore {
    fn logins(&self) -> &[Login] {
        &self.logins
    }

    fn tags(&self) -> Vec<TagDef> {
        self.tags.lock().unwrap_or_else(|e| e.into_inner()).clone()
    }

    fn create_tag(&self, name: &str, color: &str) -> Result<String, String> {
        let mut defs = self.tags.lock().unwrap_or_else(|e| e.into_inner());
        let key = new_tag(&mut defs, name, color)?;
        if let Some(save) = &self.save {
            if let Err(e) = save(&defs) {
                defs.retain(|d| d.id != key); // not saved, not there
                return Err(e);
            }
        }
        Ok(key)
    }
}

/// `create_tag`: the key of a tag name — lowercased, every run of anything but a-z and 0-9 as one
/// `_`, the ends trimmed of `_`; `tag<timestamp>` when nothing is left.
pub fn tag_key(name: &str) -> String {
    let mut out = String::new();
    let mut gap = false;
    for c in name.trim().to_lowercase().chars() {
        if c.is_ascii_lowercase() || c.is_ascii_digit() {
            out.push(c);
            gap = false;
        } else if !gap {
            out.push('_');
            gap = true;
        }
    }
    let key = out.trim_matches('_').to_string();
    if key.is_empty() {
        format!("tag{}", chrono::Utc::now().timestamp_millis())
    } else {
        key
    }
}

/// Adds a tag to `defs` and returns its key. A tag of that name (or key) is already there: an error,
/// as creating it twice would only fail on the server too.
pub fn new_tag(defs: &mut Vec<TagDef>, name: &str, color: &str) -> Result<String, String> {
    let name = name.trim();
    if name.is_empty() {
        return Err("Give the tag a name.".into());
    }
    let key = tag_key(name);
    if !crate::jmap::valid_keyword(&format!("$label:{key}")) {
        return Err("The tag name makes a key that is too long.".into());
    }
    if defs.iter().any(|d| d.id.eq_ignore_ascii_case(&key) || d.label.to_lowercase() == name.to_lowercase()) {
        return Err(format!("A tag called \"{name}\" is already there."));
    }
    defs.push(TagDef { id: key.clone(), label: name.to_string(), color: color.to_string() });
    Ok(key)
}

// ---------------------------------------------------------------- accounts and folders

/// The accounts one login reaches: the session's mail accounts, the client's own account first;
/// just that account when the session names none (a fake, or a bare session).
pub fn accounts_of(client: &Client) -> Vec<String> {
    let mut out = Vec::new();
    if let Some(a) = client.session["accounts"].as_object() {
        let mut ids: Vec<String> = a
            .iter()
            .filter(|(_, v)| v["accountCapabilities"]["urn:ietf:params:jmap:mail"].is_object())
            .map(|(k, _)| k.clone())
            .collect();
        ids.sort();
        if ids.iter().any(|i| i == &client.account) {
            ids.retain(|i| i != &client.account);
            out.push(client.account.clone());
        }
        out.extend(ids);
    }
    if out.is_empty() {
        out.push(client.account.clone());
    }
    out
}

/// An account's name from the session, or its id when the session does not name it.
pub fn account_name(client: &Client, account: &str) -> String {
    client.session["accounts"][account]["name"].as_str().filter(|n| !n.trim().is_empty()).unwrap_or(account).to_string()
}

/// A folder (a JMAP `Mailbox`) with the login and account it belongs to.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Folder {
    pub login: String,
    pub account: String,
    pub account_name: String,
    pub id: String,
    pub name: String,
    /// `/Parent/Child`, as Thunderbird writes a folder's path
    pub path: String,
    pub role: String,
    parent: Option<String>,
}

/// The folder list, kept for 60 s as the extension's is (5.3), cleared after a folder change.
#[derive(Default)]
pub struct FolderCache(Mutex<(u64, Option<(Instant, Vec<Folder>)>)>);

impl FolderCache {
    pub fn new() -> FolderCache {
        FolderCache::default()
    }

    pub fn clear(&self) {
        let mut guard = self.0.lock().unwrap_or_else(|e| e.into_inner());
        guard.0 += 1;
        guard.1 = None;
    }

    /// Every folder of every login and account, loaded again when the copy is older than 60 s.
    pub fn get(&self, ctx: &Ctx) -> Result<Vec<Folder>, String> {
        let generation = {
            let guard = self.0.lock().unwrap_or_else(|e| e.into_inner());
            if let Some((_, list)) = guard.1.as_ref().filter(|(at, _)| at.elapsed() < Duration::from_secs(60)) {
                return Ok(list.clone());
            }
            guard.0
        };
        let list = load_folders(ctx)?;
        ctx.checkpoint()?;
        let mut guard = self.0.lock().unwrap_or_else(|e| e.into_inner());
        if guard.0 == generation {
            guard.1 = Some((Instant::now(), list.clone()));
        }
        Ok(list)
    }
}

fn load_folders(ctx: &Ctx) -> Result<Vec<Folder>, String> {
    let mut out = Vec::new();
    for login in ctx.mail.logins() {
        for account in accounts_of(&login.client) {
            ctx.checkpoint()?;
            let client = login.client.scoped(&account);
            let list = client.mailboxes()?;
            let name = account_name(&login.client, &account);
            for m in &list {
                let Some(id) = m["id"].as_str() else { continue };
                out.push(Folder {
                    login: login.id.clone(),
                    account: account.clone(),
                    account_name: name.clone(),
                    id: id.to_string(),
                    name: m["name"].as_str().unwrap_or("").to_string(),
                    path: context::mailbox_path(&list, id).unwrap_or_default(),
                    role: m["role"].as_str().unwrap_or("").to_string(),
                    parent: m["parentId"].as_str().map(str::to_owned),
                });
            }
        }
    }
    Ok(out)
}

/// `findFolder` (lib/mail.js:206): exact id; path equal to the spec or `/`+spec; name
/// (case-insensitive); the `Account/path` forms; a path ending in `/`+spec. The first match in the
/// list order wins.
pub fn find_folder<'a>(folders: &'a [Folder], spec: &str) -> Option<&'a Folder> {
    let spec = spec.trim();
    if spec.is_empty() {
        return None;
    }
    let want = spec.trim_matches('/').to_lowercase();
    let mut best: Option<(u8, &Folder)> = None;
    for f in folders {
        let score = folder_score(f, spec, &want);
        if let Some(s) = score {
            if best.as_ref().is_none_or(|(b, _)| s < *b) {
                best = Some((s, f));
            }
        }
    }
    best.map(|(_, f)| f)
}

fn folder_score(f: &Folder, spec: &str, want: &str) -> Option<u8> {
    if f.id == spec {
        return Some(0);
    }
    if f.path == spec || f.path == format!("/{spec}") {
        return Some(1);
    }
    if f.name.to_lowercase() == want {
        return Some(2);
    }
    // `Account/path`: the account's name, then the path inside it
    let account = f.account_name.trim().to_lowercase();
    if !account.is_empty() {
        if let Some(rest) = want.strip_prefix(&format!("{account}/")) {
            let rest = rest.trim_matches('/');
            if f.path.trim_matches('/').to_lowercase() == rest || f.path.trim_matches('/').to_lowercase().ends_with(&format!("/{rest}")) {
                return Some(3);
            }
        }
    }
    if f.path.to_lowercase().ends_with(&format!("/{want}")) {
        return Some(4);
    }
    None
}

/// `folderOrThrow`: `No folder called "${spec}". Use list_folders.`
pub fn folder_or_throw<'a>(folders: &'a [Folder], spec: &str) -> Result<&'a Folder, String> {
    find_folder(folders, spec).ok_or_else(|| format!("No folder called \"{spec}\". Use list_folders."))
}

/// The folder a message is in (its first mailbox), for its path and name.
pub fn folder_of<'a>(m: &Value, folders: &'a [Folder], login: &str, account: &str) -> Option<&'a Folder> {
    let ids = m["mailboxIds"].as_object()?;
    let first = ids.iter().find(|(_, v)| v.as_bool() == Some(true)).map(|(k, _)| k.as_str())?;
    folders.iter().find(|f| f.login == login && f.account == account && f.id == first)
}

// ---------------------------------------------------------------- resolving a message

/// One message as a tool works on it: the three ids of its `<email>` tag.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MessageId {
    pub login: String,
    pub account: String,
    pub id: String,
}

#[derive(Clone, Debug, PartialEq)]
pub struct FoundMessage {
    pub message: MessageId,
    pub header: Value,
}

impl MessageId {
    pub fn message(&self) -> MessageRef<'_> {
        MessageRef { login: &self.login, account: &self.account, id: &self.id }
    }
}

/// A login and account a message is in, with a client scoped to that account.
pub struct Target {
    pub login: String,
    pub account: String,
    pub client: Client,
}

impl Target {
    /// One JMAP method per dispatch, with a checkpoint between requests. Do not put a multi-call
    /// write helper behind this check: it could start a second request after the deadline.
    fn request(&self, ctx: &Ctx, method: &str, mut args: Value) -> Result<Value, String> {
        args["accountId"] = json!(self.account);
        ctx.checkpoint()?;
        let r = self.client.call(json!([[method, args, "m"]]))?;
        ctx.checkpoint()?;
        r.into_iter().next().filter(|v| v[0] == method).map(|v| v[1].clone())
            .ok_or_else(|| "The server returned no result for the mail request.".into())
    }

    fn get(&self, ctx: &Ctx, method: &str, args: Value) -> Result<Vec<Value>, String> {
        self.request(ctx, method, args)?["list"].as_array().cloned()
            .ok_or_else(|| "The server returned no list for the mail request.".into())
    }

    fn download<'a>(&self, ctx: &'a Ctx, blob: &str, name: &str, mime: &str) -> Result<BlobText<'a>, String> {
        ctx.checkpoint()?;
        let mut out = BlobText::new(ctx);
        self.client.download_to(&self.account, blob, name, mime, &mut out)?;
        ctx.checkpoint()?;
        if out.pending_len != 0 {
            out.push("\u{fffd}");
        }
        Ok(out)
    }
}

/// Count the omitted characters without keeping a full source or attachment in memory.
struct BlobText<'a> {
    ctx: &'a Ctx,
    prefix: String,
    chars: usize,
    pending: [u8; 4],
    pending_len: usize,
}

impl<'a> BlobText<'a> {
    fn new(ctx: &'a Ctx) -> Self {
        Self { ctx, prefix: String::new(), chars: 0, pending: [0; 4], pending_len: 0 }
    }

    fn push(&mut self, text: &str) {
        if self.chars < MAX_RESULT {
            self.prefix.extend(text.chars().take(MAX_RESULT - self.chars));
        }
        self.chars = self.chars.saturating_add(text.chars().count());
    }

    fn output(self, budget: usize, neutralise: bool) -> String {
        let end = self.chars.min(MAX_RESULT);
        let text = if neutralise { neutralise_tool_tags(&self.prefix) } else { self.prefix };
        let count = self.chars - end + text.chars().count();
        let mut cut = budget.min(count);
        loop {
            let omitted = count - cut;
            let note = if omitted == 0 { String::new() } else { format!("\n… (cut off, {omitted} more characters)") };
            let limit = budget.saturating_sub(note.chars().count());
            if cut <= limit {
                return format!("{}{note}", text.chars().take(cut).collect::<String>());
            }
            cut = limit;
        }
    }
}

impl std::io::Write for BlobText<'_> {
    fn write(&mut self, buf: &[u8]) -> std::io::Result<usize> {
        let mut scratch = [0u8; 8196];
        for chunk in buf.chunks(8192) {
            self.ctx.checkpoint().map_err(std::io::Error::other)?;
            scratch[..self.pending_len].copy_from_slice(&self.pending[..self.pending_len]);
            scratch[self.pending_len..self.pending_len + chunk.len()].copy_from_slice(chunk);
            let mut rest = &scratch[..self.pending_len + chunk.len()];
            self.pending_len = 0;
            loop {
                match std::str::from_utf8(rest) {
                    Ok(text) => {
                        self.push(text);
                        break;
                    }
                    Err(err) => {
                        let (good, bad) = rest.split_at(err.valid_up_to());
                        self.push(std::str::from_utf8(good).expect("UTF-8 valid prefix"));
                        if let Some(n) = err.error_len() {
                            self.push("\u{fffd}");
                            rest = &bad[n..];
                        } else {
                            self.pending[..bad.len()].copy_from_slice(bad);
                            self.pending_len = bad.len();
                            break;
                        }
                    }
                }
            }
        }
        Ok(buf.len())
    }

    fn flush(&mut self) -> std::io::Result<()> {
        Ok(())
    }
}

/// Resolves a [`MessageRef`] by all three of its ids: the login must be a current login, the
/// account one of that login's, and the message is then looked up by its email id.
pub fn resolve(mail: &dyn Mail, r: MessageRef<'_>) -> Result<Target, String> {
    let login = mail.logins().iter().find(|l| l.id == r.login).ok_or_else(|| gone(r))?;
    if !accounts_of(&login.client).iter().any(|a| a == r.account) {
        return Err(gone(r));
    }
    Ok(Target { login: login.id.clone(), account: r.account.to_string(), client: login.client.scoped(r.account) })
}

/// The answer to a message whose login, account or email id is not there (any more).
pub fn gone(r: MessageRef<'_>) -> String {
    format!("No message {} in account {}. Use search_mail or list_messages to find it again.", r.id, r.account)
}

fn target_of(mail: &dyn Mail, login: &str, account: &str) -> Result<Target, String> {
    let l = mail.logins().iter().find(|l| l.id == login && accounts_of(&l.client).iter().any(|a| a == account))
        .ok_or_else(|| "That account is not logged in any more.".to_string())?;
    Ok(Target { login: l.id.clone(), account: account.to_string(), client: l.client.scoped(account) })
}

// ---------------------------------------------------------------- the tool table (5.4, 5.5)

/// The `ids` parameter: the three ids of each message.
pub fn ids_schema() -> Value {
    json!({
        "type": "array",
        "items": ref_schema(),
        "description": "Messages from search_mail or list_messages, or the open message's <email> tag: its id, account and login."
    })
}

/// One message's three ids.
pub fn ref_schema() -> Value {
    json!({
        "type": "object",
        "properties": {
            "id": str_("The message id."),
            "account": str_("The account id."),
            "login": str_("The login id.")
        },
        "required": ["id", "account", "login"]
    })
}

/// The tools of 5.4 and 5.5, in the spec's order.
pub fn tools() -> Vec<Tool> {
    vec![
        Tool {
            name: "search_mail",
            description: "Search the user's mail in all accounts. Returns up to `limit` messages, newest first, with ids for read_message and actions. Combine fields to narrow down.",
            parameters: obj(
                json!({
                    "text": str_("Words anywhere: subject, body or sender."),
                    "body": str_("Words in the body only."),
                    "from": str_("Part of the sender's name or address."),
                    "to": str_("Part of a recipient's name or address."),
                    "subject": str_("Part of the subject."),
                    "unread": bool_(""),
                    "flagged": bool_("Starred only."),
                    "hasAttachment": bool_(""),
                    "tag": str_("Tag name or key."),
                    "days": int_("Only the last N days."),
                    "since": str_("ISO date: only on/after."),
                    "before": str_("ISO date: only before."),
                    "folder": str_("Folder name or path, e.g. Inbox or Work/Clients."),
                    "account": str_("Account name or id."),
                    "limit": int_("1-100, default 20."),
                }),
                &[],
            ),
            risk: RiskOf::Fixed(Risk::Read),
            describe: None,
            details: None,
            confirm_label: None,
            run: Some(search_mail),
            timeout_ms: None,
        },
        Tool {
            name: "list_messages",
            description: "List the newest messages in one folder.",
            parameters: obj(json!({"folder": str_("Folder name or path."), "limit": int_("1-100, default 25."), "unreadOnly": bool_("")}), &["folder"]),
            risk: RiskOf::Fixed(Risk::Read),
            describe: None,
            details: None,
            confirm_label: None,
            run: Some(list_messages),
            timeout_ms: None,
        },
        Tool {
            name: "read_message",
            description: "Read one message: headers, attachment list and text. Set headers=true for all raw headers, raw=true for the full source.",
            parameters: obj(json!({"id": ref_schema(), "headers": bool_(""), "raw": bool_("")}), &["id"]),
            risk: RiskOf::Fixed(Risk::Read),
            describe: None,
            details: None,
            confirm_label: None,
            run: Some(read_message),
            timeout_ms: None,
        },
        Tool {
            name: "read_attachment",
            description: "Read an attachment of a message. Text-like files (txt, csv, json, xml, ics, vcf, eml, html) are returned as text; others only as name, type and size.",
            parameters: obj(json!({"id": ref_schema(), "name": str_("Attachment file name, or its part name.")}), &["id", "name"]),
            risk: RiskOf::Fixed(Risk::Read),
            describe: None,
            details: None,
            confirm_label: None,
            run: Some(read_attachment),
            timeout_ms: None,
        },
        Tool {
            name: "list_folders",
            description: "List all mail folders with paths and accounts. counts=true adds total/unread counts (slower).",
            parameters: obj(json!({"counts": bool_(""), "account": str_("")}), &[]),
            risk: RiskOf::Fixed(Risk::Read),
            describe: None,
            details: None,
            confirm_label: None,
            run: Some(list_folders),
            timeout_ms: None,
        },
        Tool {
            name: "list_accounts",
            description: "List mail accounts (type, e.g. jmap/imap/pop3) and their sending identities.",
            parameters: obj(json!({}), &[]),
            risk: RiskOf::Fixed(Risk::Read),
            describe: None,
            details: None,
            confirm_label: None,
            run: Some(list_accounts),
            timeout_ms: None,
        },
        Tool {
            name: "list_tags",
            description: "List the message tags (key, name, color).",
            parameters: obj(json!({}), &[]),
            risk: RiskOf::Fixed(Risk::Read),
            describe: None,
            details: None,
            confirm_label: None,
            run: Some(list_tags),
            timeout_ms: None,
        },
        Tool {
            name: "archive_messages",
            description: "Archive messages.",
            parameters: obj(json!({"ids": ids_schema()}), &["ids"]),
            risk: RiskOf::Fixed(Risk::Change),
            describe: Some(|a, ctx| Ok(format!("Archive {}", subjects(&message_ids(a)?, ctx)?))),
            details: None,
            confirm_label: None,
            run: Some(archive_messages),
            timeout_ms: None,
        },
        Tool {
            name: "move_messages",
            description: "Move messages to another folder (name or path from list_folders).",
            parameters: obj(json!({"ids": ids_schema(), "folder": str_("")}), &["ids", "folder"]),
            risk: RiskOf::Fixed(Risk::Change),
            describe: Some(|a, ctx| describe_transfer(a, ctx, "Move")),
            details: None,
            confirm_label: None,
            run: Some(move_messages),
            timeout_ms: None,
        },
        Tool {
            name: "copy_messages",
            description: "Copy messages to another folder.",
            parameters: obj(json!({"ids": ids_schema(), "folder": str_("")}), &["ids", "folder"]),
            risk: RiskOf::Fixed(Risk::Change),
            describe: Some(|a, ctx| describe_transfer(a, ctx, "Copy")),
            details: None,
            confirm_label: None,
            run: Some(copy_messages),
            timeout_ms: None,
        },
        Tool {
            name: "mark_messages",
            description: "Mark messages read/unread, starred or junk, and add or remove tags. Give only what changes.",
            parameters: obj(
                json!({
                    "ids": ids_schema(),
                    "read": bool_(""),
                    "flagged": bool_("true = starred"),
                    "junk": bool_(""),
                    "addTags": str_list("Tag names or keys to add."),
                    "removeTags": str_list("Tag names or keys to remove."),
                }),
                &["ids"],
            ),
            risk: RiskOf::Fixed(Risk::Change),
            describe: Some(|a, ctx| describe_mark(a, ctx)),
            details: None,
            confirm_label: None,
            run: Some(mark_messages),
            timeout_ms: None,
        },
        Tool {
            name: "delete_messages",
            description: "Delete messages: to the Trash, or permanently with permanent=true. The user is always asked first. Only when the user asked to delete.",
            parameters: obj(json!({"ids": ids_schema(), "permanent": bool_("Skip the Trash. Cannot be undone.")}), &["ids"]),
            risk: RiskOf::Fixed(Risk::Confirm),
            describe: Some(|a, ctx| {
                let ids = message_ids(a)?;
                check_messages(ctx, &groups(&ids, ctx)?)?;
                let head = if a["permanent"].as_bool() == Some(true) { "PERMANENTLY delete (cannot be undone)" } else { "Move to the Trash:" };
                Ok(format!("{head} {}", subjects(&ids, ctx)?))
            }),
            details: Some(|a| {
                let ids = message_ids(a).ok()?;
                Some(ids.iter().map(|m| format!(
                    "Message {} · Account {} · Login {}", context::one_line(&m.id), context::one_line(&m.account), context::one_line(&m.login)
                )).collect::<Vec<_>>().join("\n"))
            }),
            confirm_label: Some(|a| Some(if a["permanent"].as_bool() == Some(true) { "Delete permanently".into() } else { "Move to Trash".into() })),
            run: Some(delete_messages),
            timeout_ms: None,
        },
        Tool {
            name: "create_tag",
            description: "Create a new message tag.",
            parameters: obj(json!({"name": str_(""), "color": str_("Hex color like #3366FF.")}), &["name"]),
            risk: RiskOf::Fixed(Risk::Change),
            describe: Some(|a, _| Ok(format!("Create tag “{}”", str_arg(a, "name")))),
            details: None,
            confirm_label: None,
            run: Some(create_tag),
            timeout_ms: None,
        },
    ]
}

// ---------------------------------------------------------------- arguments

fn str_arg<'a>(a: &'a Value, k: &str) -> &'a str {
    a[k].as_str().unwrap_or("").trim()
}

fn int_arg(a: &Value, k: &str) -> Option<i64> {
    a[k].as_i64().or_else(|| a[k].as_str().and_then(|s| s.trim().parse().ok()))
}

fn bool_arg(a: &Value, k: &str) -> Option<bool> {
    a[k].as_bool()
}

fn str_list_arg(a: &Value, k: &str) -> Vec<String> {
    a[k].as_array().map(|v| v.iter().filter_map(|x| x.as_str()).map(|s| s.trim().to_string()).filter(|s| !s.is_empty()).collect()).unwrap_or_default()
}

fn limit_arg(a: &Value, default: i64) -> i64 {
    int_arg(a, "limit").unwrap_or(default).clamp(1, 100)
}

/// One message from a single id argument.
fn one_message(a: &Value, k: &str) -> Result<MessageId, String> {
    ref_from(&a[k]).ok_or_else(|| "No message id given.".to_string())
}

fn ref_from(v: &Value) -> Option<MessageId> {
    let id = v["id"].as_str().filter(|s| !s.is_empty())?.to_string();
    let account = v["account"].as_str().filter(|s| !s.is_empty())?.to_string();
    let login = v["login"].as_str().filter(|s| !s.is_empty())?.to_string();
    Some(MessageId { login, account, id })
}

/// `checkIds`: the messages a call names, with the two texts of 5.3.
fn message_ids(a: &Value) -> Result<Vec<MessageId>, String> {
    let list = a["ids"].as_array().cloned().unwrap_or_default();
    if list.is_empty() {
        return Err("No message ids given.".into());
    }
    if list.len() > MAX_IDS {
        return Err("At most 100 messages per call.".into());
    }
    list.iter()
        .map(|v| ref_from(v).ok_or_else(|| "Each message needs id, account and login, as search_mail gives them.".to_string()))
        .collect()
}

/// `subjects(ids)`: up to 4 subjects in curly quotes, then `and N more`.
fn subjects(ids: &[MessageId], ctx: &Ctx) -> Result<String, String> {
    let mut parts = Vec::new();
    for m in ids.iter().take(4) {
        ctx.checkpoint()?;
        let text = match subject_of(m, ctx) {
            Ok(subject) if subject.is_empty() => "“(no subject)”".into(),
            Ok(subject) => format!("“{subject}”"),
            Err(_) => format!("#{}", context::one_line(&m.id)),
        };
        parts.push(text);
    }
    let mut out = parts.join(", ");
    if ids.len() > 4 {
        out = format!("{}, and {} more", out, ids.len() - 4);
    }
    Ok(out)
}

fn subject_of(m: &MessageId, ctx: &Ctx) -> Result<String, String> {
    let t = resolve(ctx.mail.as_ref(), m.message())?;
    let r = t.get(ctx, "Email/get", json!({"ids": [m.id], "properties": ["id", "subject"]}))?;
    r.iter().find(|row| row["id"] == m.id).map(|x| context::one_line(x["subject"].as_str().unwrap_or("")))
        .ok_or_else(|| gone(m.message()))
}

// ---------------------------------------------------------------- reading (5.4)

/// The properties a header summary is made of.
const SUMMARY_PROPERTIES: [&str; 11] =
    ["id", "mailboxIds", "keywords", "sentAt", "receivedAt", "from", "to", "cc", "subject", "size", "hasAttachment"];

fn search_mail(a: &Value, ctx: &Ctx) -> Result<Outcome, String> {
    let limit = limit_arg(a, 20) as usize;
    let account_spec = str_arg(a, "account");
    let folders = ctx.folders.get(ctx)?;
    let in_account = |f: &Folder| ctx.mail.logins().iter().any(|l| l.id == f.login && account_matches(l, &f.account, account_spec));
    // a folder narrows the search to the one account it belongs to
    if !account_spec.is_empty() && !ctx.mail.logins().iter().any(|l| accounts_of(&l.client).iter().any(|id| account_matches(l, id, account_spec))) {
        return Err(format!("No account called \"{account_spec}\"."));
    }
    let folder = if str_arg(a, "folder").is_empty() {
        None
    } else {
        let eligible: Vec<_> = folders.iter().filter(|f| in_account(f)).cloned().collect();
        Some(folder_or_throw(&eligible, str_arg(a, "folder"))?.clone())
    };

    let mut targets = Vec::new();
    for login in ctx.mail.logins() {
        for account in accounts_of(&login.client) {
            if !account_matches(login, &account, account_spec) {
                continue;
            }
            if let Some(f) = &folder {
                if f.login != login.id || f.account != account {
                    continue;
                }
            }
            targets.push(target_of(ctx.mail.as_ref(), &login.id, &account)?);
        }
    }
    let tags = ctx.mail.tags();
    let filter = search_filter(a, folder.as_ref(), &folders, &tags)?;
    let mut found = Vec::new();
    for t in &targets {
        let r = t.request(ctx, "Email/query", json!({
            "filter": filter,
            "sort": [{"property": "receivedAt", "isAscending": false}],
            "position": 0,
            "limit": 250,
            "calculateTotal": false
        }))?;
        let ids = query_ids(&r)?;
        found.extend(summaries_of(ctx, t, ids, &folders, &tags)?);
    }
    // newest first across accounts, then the limit
    found.sort_by(|a, b| b.date.cmp(&a.date));
    found.truncate(limit);
    if found.is_empty() {
        return Ok(Outcome::ok("No messages found."));
    }
    let mut outcome = Outcome::ok(json(&Value::Array(found.iter().map(|r| r.summary.clone()).collect())));
    outcome.found = found.into_iter().map(|r| r.found).collect();
    Ok(outcome)
}

fn account_matches(login: &Login, account: &str, spec: &str) -> bool {
    let spec = spec.trim().to_lowercase();
    if spec.is_empty() {
        return true;
    }
    account.to_lowercase() == spec
        || account_name(&login.client, account).to_lowercase() == spec
        || login.client.username.trim().to_lowercase() == spec
        || login.id == spec
}

/// The `Email/query` filter of search_mail. With no condition at all the search is the last 30
/// days, as the extension's is.
fn search_filter(a: &Value, folder: Option<&Folder>, folders: &[Folder], tags: &[TagDef]) -> Result<Value, String> {
    let mut f = Map::new();
    let mut conditions = Vec::new();
    for (key, field) in [("text", "text"), ("body", "body"), ("from", "from"), ("to", "to"), ("subject", "subject")] {
        let v = str_arg(a, key);
        if !v.is_empty() {
            f.insert(field.into(), json!(v));
        }
    }
    if let Some(unread) = bool_arg(a, "unread") {
        conditions.push(keyword_filter("$seen", !unread));
    }
    if let Some(flagged) = bool_arg(a, "flagged") {
        conditions.push(keyword_filter("$flagged", flagged));
    }
    if let Some(has) = bool_arg(a, "hasAttachment") {
        f.insert("hasAttachment".into(), json!(has));
    }
    if let Some(tag) = non_empty(a, "tag") {
        let key = tag_keys(&[tag.to_string()], tags)?.pop().unwrap_or_default();
        conditions.push(keyword_filter(&format!("$label:{key}"), true));
    }
    if let Some(since) = non_empty(a, "since") {
        f.insert("after".into(), json!(iso_date(since)?));
    } else if let Some(days) = int_arg(a, "days") {
        f.insert("after".into(), json!(days_ago(days)?));
    }
    if let Some(before) = non_empty(a, "before") {
        f.insert("before".into(), json!(iso_date(before)?));
    }
    if let Some(folder) = folder {
        let mut ids = vec![folder.id.as_str()];
        let mut seen = std::collections::HashSet::from([folder.id.as_str()]);
        let mut parents: std::collections::HashMap<&str, Vec<&str>> = Default::default();
        for f in folders.iter().filter(|f| f.login == folder.login && f.account == folder.account) {
            if let Some(parent) = f.parent.as_deref() {
                parents.entry(parent).or_default().push(&f.id);
            }
        }
        let mut i = 0;
        while i < ids.len() {
            if let Some(children) = parents.get(ids[i]) {
                ids.extend(children.iter().copied().filter(|child| seen.insert(*child)));
            }
            i += 1;
        }
        let mut mailboxes: Vec<_> = ids.into_iter().map(|id| json!({"inMailbox": id})).collect();
        conditions.push(if mailboxes.len() == 1 { mailboxes.pop().unwrap() } else { json!({"operator": "OR", "conditions": mailboxes}) });
    }
    if conditions.is_empty() && f.is_empty() && str_arg(a, "account").is_empty() {
        f.insert("after".into(), json!(days_ago(30)?));
    }
    if !f.is_empty() {
        conditions.insert(0, Value::Object(f));
    }
    Ok(match conditions.len() {
        0 => json!({}),
        1 => conditions.pop().unwrap(),
        _ => json!({"operator": "AND", "conditions": conditions}),
    })
}

fn keyword_filter(keyword: &str, set: bool) -> Value {
    if set { json!({"hasKeyword": keyword}) } else { json!({"notKeyword": keyword}) }
}

fn non_empty<'a>(a: &'a Value, k: &str) -> Option<&'a str> {
    a[k].as_str().map(str::trim).filter(|s| !s.is_empty())
}

/// An ISO date as JMAP wants it: `YYYY-MM-DD` becomes midnight UTC.
pub fn iso_date(d: &str) -> Result<String, String> {
    if let Ok(date) = chrono::NaiveDate::parse_from_str(d, "%Y-%m-%d") {
        return Ok(format!("{date}T00:00:00Z"));
    }
    chrono::DateTime::parse_from_rfc3339(d)
        .map(|t| t.to_utc().to_rfc3339_opts(chrono::SecondsFormat::AutoSi, true))
        .map_err(|_| "Give the date as YYYY-MM-DD or an ISO timestamp with a time zone.".into())
}

fn days_ago(days: i64) -> Result<String, String> {
    chrono::Duration::try_days(days).and_then(|d| chrono::Utc::now().checked_sub_signed(d))
        .filter(|d| days >= 0 && chrono::Datelike::year(d) >= 1)
        .map(|d| d.to_rfc3339_opts(chrono::SecondsFormat::Secs, true))
        .ok_or_else(|| "Give a valid non-negative number of days.".into())
}

fn list_messages(a: &Value, ctx: &Ctx) -> Result<Outcome, String> {
    let limit = limit_arg(a, 25) as usize;
    let unread = bool_arg(a, "unreadOnly") == Some(true);
    let folders = ctx.folders.get(ctx)?;
    let folder = folder_or_throw(&folders, str_arg(a, "folder"))?;
    let t = target_of(ctx.mail.as_ref(), &folder.login, &folder.account)?;
    let mut ids: Vec<Value> = Vec::new();
    // the last two weeks, then wider, then all time, stopping once there are enough
    for days in [Some(14i64), Some(90), Some(730), None] {
        let mut filter = Map::new();
        filter.insert("inMailbox".into(), json!(folder.id));
        if let Some(d) = days {
            filter.insert("after".into(), json!(days_ago(d)?));
        }
        if unread {
            filter.insert("notKeyword".into(), json!("$seen"));
        }
        let r = t.request(ctx, "Email/query", json!({
            "filter": filter,
            "sort": [{"property": "receivedAt", "isAscending": false}],
            "position": 0,
            "limit": limit,
            "calculateTotal": false
        }))?;
        ids = query_ids(&r)?.to_vec();
        if ids.len() >= limit {
            break;
        }
    }
    ids.truncate(limit);
    if ids.is_empty() {
        return Ok(Outcome::ok(format!("No messages in {}.", folder.path)));
    }
    let rows = summaries_of(ctx, &t, &ids, &folders, &ctx.mail.tags())?;
    Ok(Outcome::ok(json(&Value::Array(rows.into_iter().map(|r| r.summary).collect()))))
}

fn query_ids(r: &Value) -> Result<&[Value], String> {
    r["ids"].as_array().map(Vec::as_slice).ok_or_else(|| "The server returned no message ids for the search.".into())
}

struct SummaryRow {
    date: Option<chrono::DateTime<chrono::FixedOffset>>,
    summary: Value,
    found: FoundMessage,
}

/// The header summaries of `ids`, in the order the ids came, each with the date to sort by.
fn summaries_of(ctx: &Ctx, t: &Target, ids: &[Value], folders: &[Folder], tags: &[TagDef]) -> Result<Vec<SummaryRow>, String> {
    let mut rows = Vec::new();
    for chunk in ids.chunks(t.client.max_get()) {
        let list = t.get(ctx, "Email/get", json!({"ids": chunk, "properties": SUMMARY_PROPERTIES}))?;
        let mut by_id: std::collections::HashMap<_, _> = list.into_iter().filter_map(|m| Some((m["id"].as_str()?.to_owned(), m))).collect();
        for id in chunk.iter().filter_map(Value::as_str) {
            let Some(header) = by_id.remove(id) else { continue };
            let date = ["receivedAt", "sentAt"].into_iter().find_map(|key| header[key].as_str()?.parse().ok());
            rows.push(SummaryRow {
                date,
                summary: summary(&header, &t.login, &t.account, folders, tags),
                found: FoundMessage { message: MessageId { login: t.login.clone(), account: t.account.clone(), id: id.into() }, header },
            });
        }
    }
    Ok(rows)
}

/// The header summary of 2.0, with the three ids a tool call needs.
fn summary(m: &Value, login: &str, account: &str, folders: &[Folder], tags: &[TagDef]) -> Value {
    let mut s = Map::new();
    s.insert("id".into(), m["id"].clone());
    s.insert("account".into(), json!(account));
    s.insert("login".into(), json!(login));
    let date = m["sentAt"].as_str().or_else(|| m["receivedAt"].as_str()).unwrap_or("");
    s.insert("date".into(), json!(context::short_date(date)));
    s.insert("from".into(), json!(context::addresses(&m["from"]).join(", ")));
    s.insert("to".into(), json!(context::addresses(&m["to"]).into_iter().take(5).collect::<Vec<_>>()));
    let cc = context::addresses(&m["cc"]);
    if !cc.is_empty() {
        s.insert("cc".into(), json!(cc.into_iter().take(5).collect::<Vec<_>>()));
    }
    s.insert("subject".into(), json!(context::one_line(m["subject"].as_str().unwrap_or(""))));
    if let Some(f) = folder_of(m, folders, login, account) {
        s.insert("folder".into(), json!(f.name));
        s.insert("folderPath".into(), json!(f.path));
    }
    let kw = &m["keywords"];
    s.insert("read".into(), json!(kw["$seen"].as_bool() == Some(true)));
    s.insert("flagged".into(), json!(kw["$flagged"].as_bool() == Some(true)));
    let labels: Vec<_> = kw.as_object().into_iter().flatten().filter(|(_, v)| **v == true)
        .filter_map(|(k, _)| k.strip_prefix("$label:")).collect();
    if !labels.is_empty() {
        s.insert("tags".into(), json!(labels.into_iter().map(|k| tag_label(k, tags)).collect::<Vec<_>>()));
    }
    Value::Object(s)
}

fn tag_label(key: &str, tags: &[TagDef]) -> String {
    tags.iter().find(|t| t.id.eq_ignore_ascii_case(key)).map(|t| t.label.clone()).unwrap_or_else(|| key.to_string())
}

fn read_message(a: &Value, ctx: &Ctx) -> Result<Outcome, String> {
    let id = one_message(a, "id")?;
    let t = resolve(ctx.mail.as_ref(), id.message())?;
    let raw = bool_arg(a, "raw") == Some(true);
    let headers = bool_arg(a, "headers") == Some(true);
    if raw {
        let m = message_get(ctx, &t, &id, json!({"properties": ["id", "blobId"]}))?;
        let blob = m["blobId"].as_str().filter(|s| !s.is_empty()).ok_or("This message has no source to download.")?;
        return Ok(Outcome::ok(t.download(ctx, blob, "message.eml", "message/rfc822")?.output(MAX_RESULT, false)));
    }
    let mut props: Vec<&str> = context::EMAIL_PROPERTIES.to_vec();
    props.push("mailboxIds");
    if headers {
        props.push("headers");
    }
    let m = message_get(ctx, &t, &id, json!({
        "properties": props,
        "fetchTextBodyValues": true,
        "fetchHTMLBodyValues": true,
        "maxBodyValueBytes": crate::jmap::OPEN_BODY_MAX_BYTES
    }))?;
    let folders = ctx.folders.get(ctx)?;
    let folder = folder_of(&m, &folders, &t.login, &t.account);
    let mut out = context::message_for_model(&m, id.message(), folder.map(|f| f.path.as_str()));
    if headers {
        out.push('\n');
        out.push_str(&all_headers(&m));
    }
    Ok(Outcome::ok(out))
}

fn message_get(ctx: &Ctx, t: &Target, id: &MessageId, mut args: Value) -> Result<Value, String> {
    args["ids"] = json!([id.id]);
    t.get(ctx, "Email/get", args)?.into_iter().find(|m| m["id"] == id.id).ok_or_else(|| gone(id.message()))
}

/// `<all-headers>`: every header, repeated names joined with ` | `, cut at 20000.
fn all_headers(m: &Value) -> String {
    let mut names: Vec<String> = Vec::new();
    let mut values: Vec<Vec<String>> = Vec::new();
    for h in m["headers"].as_array().cloned().unwrap_or_default() {
        let name = context::one_line(h["name"].as_str().unwrap_or(""));
        if name.is_empty() {
            continue;
        }
        let value = context::one_line(h["value"].as_str().unwrap_or(""));
        match names.iter().position(|n| n.eq_ignore_ascii_case(&name)) {
            Some(i) => values[i].push(value),
            None => {
                names.push(name);
                values.push(vec![value]);
            }
        }
    }
    let lines: Vec<String> = names.iter().zip(values.iter()).map(|(n, vs)| format!("{n}: {}", vs.join(" | "))).collect();
    format!("<all-headers>\n{}\n</all-headers>", cap(&neutralise_tool_tags(&lines.join("\n")), 20000))
}

fn neutralise_tool_tags(text: &str) -> String {
    static CLOSERS: std::sync::OnceLock<regex::Regex> = std::sync::OnceLock::new();
    let closers = CLOSERS.get_or_init(|| regex::Regex::new(r"(?i)(<\s*)(/)(\s*)(attachment|all-headers)").unwrap());
    closers.replace_all(&context::neutralise_tags(text), "$1\\$2$3$4").into_owned()
}

/// The text types of read_attachment.
const TEXTISH: &str = r"(?i)^(text/|application/(json|xml|.*\+xml|.*\+json|x-sh|javascript|ics|pgp-keys)|message/)|calendar|vcard";
const TEXT_NAMES: [&str; 11] = ["txt", "csv", "md", "json", "xml", "ics", "vcf", "eml", "htm", "html", "log"];

fn is_textish(mime: &str, name: &str) -> bool {
    static RE: std::sync::OnceLock<regex::Regex> = std::sync::OnceLock::new();
    if RE.get_or_init(|| regex::Regex::new(TEXTISH).unwrap()).is_match(mime) {
        return true;
    }
    let n = name.to_lowercase();
    if TEXT_NAMES.iter().any(|e| n.ends_with(&format!(".{e}"))) {
        return true;
    }
    ["yaml", "yml"].iter().any(|e| n.ends_with(&format!(".{e}")))
}

fn read_attachment(a: &Value, ctx: &Ctx) -> Result<Outcome, String> {
    let id = one_message(a, "id")?;
    let name = str_arg(a, "name");
    if name.is_empty() {
        return Err("Give the attachment's name or part id.".into());
    }
    let t = resolve(ctx.mail.as_ref(), id.message())?;
    let m = message_get(ctx, &t, &id, json!({"properties": ["id", "attachments"]}))?;
    let atts = m["attachments"].as_array().cloned().unwrap_or_default();
    let find = |pick: &dyn Fn(&Value) -> bool| atts.iter().find(|x| pick(x)).cloned();
    let want = name.to_lowercase();
    let att = find(&|x| x["partId"].as_str() == Some(name))
        .or_else(|| find(&|x| x["name"].as_str() == Some(name)))
        .or_else(|| find(&|x| x["name"].as_str().is_some_and(|n| n.to_lowercase().contains(&want))));
    let Some(att) = att else {
        let names: Vec<String> = atts.iter().map(|x| x["name"].as_str().unwrap_or("").to_string()).collect();
        return Ok(Outcome::error(format!(
            "No attachment \"{name}\". It has: {}.",
            if names.is_empty() { "none".to_string() } else { names.join(", ") }
        )));
    };
    let att_name = att["name"].as_str().unwrap_or(name);
    let mime = att["type"].as_str().unwrap_or("application/octet-stream");
    let size = att["size"].as_u64().unwrap_or(0);
    if !is_textish(mime, att_name) {
        return Ok(Outcome::ok(format!("{att_name}: {mime}, {size} bytes. Not a text file, so it can't be read here.")));
    }
    let Some(blob) = att["blobId"].as_str() else {
        return Ok(Outcome::error(format!("No attachment \"{name}\". It has: none.")));
    };
    let text = t.download(ctx, blob, att_name, mime)?;
    let open = format!("<attachment name=\"{}\" type=\"{}\">\n", attr(att_name), attr(mime));
    const CLOSE: &str = "\n</attachment>";
    let budget = MAX_RESULT.saturating_sub(open.chars().count() + CLOSE.len());
    Ok(Outcome::ok(format!("{open}{}{CLOSE}", text.output(budget, true))))
}

/// A value inside an attribute: one short line, no literal quotes or angle brackets.
fn attr(s: &str) -> String {
    context::one_line(s).chars().take(1000).collect::<String>()
        .replace('&', "&amp;").replace('"', "&quot;").replace('<', "&lt;").replace('>', "&gt;")
}

fn list_folders(a: &Value, ctx: &Ctx) -> Result<Outcome, String> {
    let counts = bool_arg(a, "counts") == Some(true);
    let account_spec = str_arg(a, "account");
    let folders = ctx.folders.get(ctx)?;
    let selected: Vec<_> = folders.iter().filter(|f| {
        account_spec.is_empty()
            || f.account.eq_ignore_ascii_case(account_spec)
            || f.account_name.eq_ignore_ascii_case(account_spec)
            || f.login == account_spec
    }).take(400).collect();
    let counts = if counts { counts_by_folder(ctx, &selected)? } else { Default::default() };
    let mut out = Vec::with_capacity(selected.len());
    for f in selected {
        let mut o = Map::new();
        o.insert("id".into(), json!(f.id));
        o.insert("name".into(), json!(f.name));
        o.insert("path".into(), json!(f.path));
        o.insert("account".into(), json!(f.account_name));
        o.insert("accountId".into(), json!(f.account));
        o.insert("login".into(), json!(f.login));
        if !f.role.is_empty() {
            o.insert("specialUse".into(), json!(f.role));
        }
        if let Some(&(total, unread)) = counts.get(&(f.login.as_str(), f.account.as_str(), f.id.as_str())) {
            o.insert("total".into(), json!(total));
            o.insert("unread".into(), json!(unread));
        }
        out.push(Value::Object(o));
    }
    Ok(Outcome::ok(json(&Value::Array(out))))
}

type Counts<'a> = std::collections::HashMap<(&'a str, &'a str, &'a str), (u64, u64)>;

/// Refresh counts independently of the cached folder names and batch by login/account.
fn counts_by_folder<'a>(ctx: &Ctx, folders: &[&'a Folder]) -> Result<Counts<'a>, String> {
    let mut grouped: std::collections::BTreeMap<(&str, &str), Vec<&str>> = Default::default();
    for f in folders {
        grouped.entry((&f.login, &f.account)).or_default().push(&f.id);
    }
    let mut counts = Counts::new();
    for ((login, account), ids) in grouped {
        let t = target_of(ctx.mail.as_ref(), login, account)?;
        for chunk in ids.chunks(t.client.max_get()) {
            let rows = t.get(ctx, "Mailbox/get", json!({"ids": chunk, "properties": ["id", "totalEmails", "unreadEmails"]}))?;
            for m in rows {
                if let (Some(id), Some(total), Some(unread)) = (m["id"].as_str(), m["totalEmails"].as_u64(), m["unreadEmails"].as_u64()) {
                    if let Some(&id) = chunk.iter().find(|i| **i == id) {
                        counts.insert((login, account, id), (total, unread));
                    }
                }
            }
        }
    }
    Ok(counts)
}

fn list_accounts(_a: &Value, ctx: &Ctx) -> Result<Outcome, String> {
    const SUBMISSION: &str = "urn:ietf:params:jmap:submission";
    let mut out = Vec::new();
    for login in ctx.mail.logins() {
        for account in accounts_of(&login.client) {
            let mut o = Map::new();
            o.insert("id".into(), json!(account));
            o.insert("name".into(), json!(account_name(&login.client, &account)));
            o.insert("type".into(), json!("jmap"));
            o.insert("login".into(), json!(login.id));
            let mut ids = Vec::new();
            if login.client.account_has(SUBMISSION, &account) || login.client.session["primaryAccounts"][SUBMISSION] == account {
                let t = target_of(ctx.mail.as_ref(), &login.id, &account)?;
                let list = t.get(ctx, "Identity/get", json!({"properties": ["id", "name", "email"]}))?;
                for id in list {
                    let mut i = Map::new();
                    i.insert("id".into(), id["id"].clone());
                    i.insert("name".into(), json!(id["name"].as_str().unwrap_or("")));
                    i.insert("email".into(), json!(id["email"].as_str().unwrap_or("")));
                    if let Some(l) = id["label"].as_str() {
                        i.insert("label".into(), json!(l));
                    }
                    ids.push(Value::Object(i));
                }
            }
            o.insert("identities".into(), Value::Array(ids));
            out.push(Value::Object(o));
        }
    }
    Ok(Outcome::ok(json(&Value::Array(out))))
}

fn list_tags(_a: &Value, ctx: &Ctx) -> Result<Outcome, String> {
    let list: Vec<Value> = ctx
        .mail
        .tags()
        .into_iter()
        .map(|t| json!({"key": t.id, "name": t.label, "color": t.color}))
        .collect();
    Ok(Outcome::ok(json(&Value::Array(list))))
}

// ---------------------------------------------------------------- changing (5.5)

/// The messages grouped by login and account, each group with one client.
fn groups(ids: &[MessageId], ctx: &Ctx) -> Result<Vec<(Target, Vec<String>)>, String> {
    let mut out: Vec<(Target, Vec<String>)> = Vec::new();
    for m in ids {
        match out.iter_mut().find(|(x, _)| x.login == m.login && x.account == m.account) {
            Some((_, list)) => {
                if !list.contains(&m.id) {
                    list.push(m.id.clone());
                }
            }
            None => out.push((resolve(ctx.mail.as_ref(), m.message())?, vec![m.id.clone()])),
        }
    }
    Ok(out)
}

fn check_messages(ctx: &Ctx, groups: &[(Target, Vec<String>)]) -> Result<(), String> {
    for (t, ids) in groups {
        for chunk in ids.chunks(t.client.max_get()) {
            let list = t.get(ctx, "Email/get", json!({"ids": chunk, "properties": ["id"]}))?;
            for id in chunk {
                if !list.iter().any(|m| m["id"] == id.as_str()) {
                    return Err(gone(MessageRef { login: &t.login, account: &t.account, id }));
                }
            }
        }
    }
    Ok(())
}

fn check_set(r: &Value, ids: &[String], destroy: bool) -> Result<(), String> {
    let (done, refused) = if destroy { ("destroyed", "notDestroyed") } else { ("updated", "notUpdated") };
    let mut errors = Vec::new();
    for id in ids {
        let applied = if destroy {
            r[done].as_array().is_some_and(|a| a.iter().any(|x| x == id.as_str()))
        } else {
            r[done].get(id).is_some()
        };
        if !applied || r[refused].get(id).is_some() {
            let reason = r[refused][id]["type"].as_str().unwrap_or("no confirmation");
            errors.push(format!("{id}: {reason}"));
        }
    }
    if errors.is_empty() {
        Ok(())
    } else {
        Err(format!("The server did not confirm every change; some may already have happened. Check before retrying. {}", errors.join("; ")))
    }
}

fn update(ctx: &Ctx, t: &Target, ids: &[String], patch: &Value) -> Result<(), String> {
    for chunk in ids.chunks(t.client.max_set()) {
        let patches: Map<String, Value> = chunk.iter().map(|id| (id.clone(), patch.clone())).collect();
        let r = t.request(ctx, "Email/set", json!({"update": patches}))?;
        check_set(&r, chunk, false)?;
    }
    Ok(())
}

fn role_folder(ctx: &Ctx, t: &Target, role: &str) -> Result<Option<String>, String> {
    ctx.checkpoint()?;
    let list = t.client.mailboxes()?;
    ctx.checkpoint()?;
    let mut matching = list.iter().filter(|m| m["role"] == role).filter_map(|m| m["id"].as_str());
    let first = matching.next().map(str::to_owned);
    if matching.next().is_some() {
        return Err(format!("This account has more than one {role} folder."));
    }
    Ok(first)
}

fn archive_messages(a: &Value, ctx: &Ctx) -> Result<Outcome, String> {
    let groups = groups(&message_ids(a)?, ctx)?;
    check_messages(ctx, &groups)?;
    for (t, ids) in groups {
        let folder = match role_folder(ctx, &t, "archive")? {
            Some(folder) => folder,
            None => {
                // Like Barbican's manual Archive action, create the role the first time.
                let r = t.request(ctx, "Mailbox/set", json!({"create": {
                    "a": {"name": "Archive", "role": "archive", "parentId": null, "isSubscribed": true}
                }}))?;
                ctx.folders.clear();
                r["created"]["a"]["id"].as_str().ok_or("The server did not confirm that it created Archive.")?.to_owned()
            }
        };
        update(ctx, &t, &ids, &json!({"mailboxIds": { (folder): true }}))?;
    }
    Ok(Outcome::ok("Archived."))
}

fn destination(ctx: &Ctx, groups: &[(Target, Vec<String>)], spec: &str) -> Result<Folder, String> {
    let [(target, _)] = groups else {
        return Err("Move or copy messages from one account at a time.".into());
    };
    let want = spec.trim_matches('/').to_lowercase();
    let folders = ctx.folders.get(ctx)?;
    let mut matches: Vec<_> = folders.into_iter().filter(|f| f.login == target.login && f.account == target.account)
        .filter_map(|f| Some((folder_score(&f, spec, &want)?, f))).collect();
    matches.sort_by_key(|(score, _)| *score);
    match matches.as_slice() {
        [] => Err(format!("No folder called \"{spec}\" in the message account. Use list_folders.")),
        [(a, _), (b, _), ..] if a == b => Err(format!("More than one folder matches \"{spec}\". Use an exact folder id or path from list_folders.")),
        _ => Ok(matches.swap_remove(0).1),
    }
}

fn describe_transfer(a: &mut Value, ctx: &Ctx, verb: &str) -> Result<String, String> {
    let ids = message_ids(a)?;
    let folder = destination(ctx, &groups(&ids, ctx)?, str_arg(a, "folder"))?;
    let label = context::one_line(str_arg(a, "folder"));
    let resolved = context::one_line(&folder.path);
    let account = context::one_line(&folder.account_name);
    // Overwrite any model value; execute in the originally resolved folder, even if it is
    // renamed, or the cache expires, while the user is deciding.
    a["_resolvedFolder"] = json!({"id": folder.id, "path": folder.path});
    Ok(format!("{verb} {} to {label} ({account}{resolved})", subjects(&ids, ctx)?))
}

fn approved_folder(a: &Value) -> Result<(&str, &str), String> {
    let value = &a["_resolvedFolder"];
    value["id"].as_str().zip(value["path"].as_str()).ok_or_else(|| "The destination was not approved.".into())
}

fn move_messages(a: &Value, ctx: &Ctx) -> Result<Outcome, String> {
    let groups = groups(&message_ids(a)?, ctx)?;
    let (folder, path) = approved_folder(a)?;
    check_messages(ctx, &groups)?;
    for (t, ids) in groups {
        update(ctx, &t, &ids, &json!({"mailboxIds": { (folder): true }}))?;
    }
    Ok(Outcome::ok(format!("Moved to {path}.")))
}

fn copy_messages(a: &Value, ctx: &Ctx) -> Result<Outcome, String> {
    let groups = groups(&message_ids(a)?, ctx)?;
    let (folder, path) = approved_folder(a)?;
    check_messages(ctx, &groups)?;
    for (t, ids) in groups {
        // a copy in JMAP is the message in a second mailbox, kept where it was
        let mut patch = Map::new();
        patch.insert(format!("mailboxIds/{}", folder.replace('~', "~0").replace('/', "~1")), json!(true));
        let patch = Value::Object(patch);
        update(ctx, &t, &ids, &patch)?;
    }
    Ok(Outcome::ok(format!("Copied to {path}.")))
}

fn describe_mark(a: &Value, ctx: &Ctx) -> Result<String, String> {
    let mut what = Vec::new();
    match bool_arg(a, "read") {
        Some(true) => what.push("read".to_string()),
        Some(false) => what.push("unread".to_string()),
        None => {}
    }
    match bool_arg(a, "flagged") {
        Some(true) => what.push("starred".to_string()),
        Some(false) => what.push("not starred".to_string()),
        None => {}
    }
    match bool_arg(a, "junk") {
        Some(true) => what.push("junk (move to Junk)".to_string()),
        Some(false) => what.push("not junk (move to Inbox)".to_string()),
        None => {}
    }
    let add = str_list_arg(a, "addTags");
    if !add.is_empty() {
        what.push(format!("tagged {}", add.join(", ")));
    }
    let remove = str_list_arg(a, "removeTags");
    if !remove.is_empty() {
        what.push(format!("without tag {}", remove.join(", ")));
    }
    let what = if what.is_empty() { "…".to_string() } else { what.join(", ") };
    Ok(format!("Mark {} as {what}", subjects(&message_ids(a)?, ctx)?))
}

fn mark_messages(a: &Value, ctx: &Ctx) -> Result<Outcome, String> {
    let ids = message_ids(a)?;
    let read = bool_arg(a, "read");
    let flagged = bool_arg(a, "flagged");
    let junk = bool_arg(a, "junk");
    let tags = ctx.mail.tags();
    let add = tag_keys(&str_list_arg(a, "addTags"), &tags)?;
    let remove = tag_keys(&str_list_arg(a, "removeTags"), &tags)?;
    let mut patch = Map::new();
    let mut set = |key: &str, on: bool| {
        patch.insert(crate::jmap::keyword_path(key), if on { json!(true) } else { Value::Null });
    };
    for (key, on) in [("$seen", read), ("$flagged", flagged), ("$junk", junk), ("$notjunk", junk.map(|v| !v))] {
        if let Some(on) = on {
            set(key, on);
        }
    }
    for (keys, on) in [(&add, true), (&remove, false)] {
        for key in keys {
            set(&format!("$label:{key}"), on);
        }
    }
    let groups = groups(&ids, ctx)?;
    if !patch.is_empty() {
        check_messages(ctx, &groups)?;
        // §8.4 calls for Barbican's spam flow, which also moves mail. Resolve all destinations
        // before writing anything, including when flags and tags are changed in this same call.
        let destinations = groups.iter().map(|(t, _)| {
            let Some(is_junk) = junk else { return Ok(None) };
            let role = if is_junk { "junk" } else { "inbox" };
            role_folder(ctx, t, role)?.map(Some).ok_or_else(|| format!("This account has no {role} folder."))
        }).collect::<Result<Vec<_>, String>>()?;
        for ((t, ids), folder) in groups.into_iter().zip(destinations) {
            let mut patch = Value::Object(patch.clone());
            if let Some(folder) = folder {
                patch["mailboxIds"] = json!({(folder): true});
            }
            update(ctx, &t, &ids, &patch)?;
        }
    }
    Ok(Outcome::ok("Done."))
}

/// `tagKeys`: each name or key to its tag key; an unknown one is the error of 5.3.
fn tag_keys(names: &[String], tags: &[TagDef]) -> Result<Vec<String>, String> {
    names
        .iter()
        .map(|n| {
            tags.iter()
                .find(|t| t.id.eq_ignore_ascii_case(n) || t.label.to_lowercase() == n.to_lowercase())
                .map(|t| t.id.clone())
                .ok_or_else(|| format!("No tag called \"{n}\". Use list_tags or create_tag."))
        })
        .collect()
}

fn delete_messages(a: &Value, ctx: &Ctx) -> Result<Outcome, String> {
    let permanent = bool_arg(a, "permanent") == Some(true);
    let groups = groups(&message_ids(a)?, ctx)?;
    check_messages(ctx, &groups)?;
    if permanent {
        for (t, ids) in groups {
            for chunk in ids.chunks(t.client.max_set()) {
                let r = t.request(ctx, "Email/set", json!({"destroy": chunk}))?;
                check_set(&r, chunk, true)?;
            }
        }
    } else {
        // Resolve every destination before changing any mail.
        let mut folders = Vec::new();
        for (t, _) in &groups {
            folders.push(role_folder(ctx, t, "trash")?.ok_or("This account has no trash folder.")?);
        }
        for ((t, ids), folder) in groups.into_iter().zip(folders) {
            update(ctx, &t, &ids, &json!({"mailboxIds": { (folder): true }}))?;
        }
    }
    Ok(Outcome::ok(if permanent { "Deleted permanently." } else { "Moved to the Trash." }))
}

fn create_tag(a: &Value, ctx: &Ctx) -> Result<Outcome, String> {
    let name = str_arg(a, "name");
    if name.is_empty() {
        return Err("Give the tag a name.".into());
    }
    let color = if str_arg(a, "color").is_empty() { "#3366FF" } else { str_arg(a, "color") };
    ctx.checkpoint()?;
    let key = ctx.mail.create_tag(name, color)?;
    Ok(Outcome::ok(format!("Created tag {name} (key {key}).")))
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::assistant::config::Settings;
    use crate::assistant::tools::fake::{self, TestHost};
    use crate::assistant::tools::{run_tool, Ctx};
    use crate::assistant::sse::Cancel;
    use crate::assistant::history::ToolCall;
    use serde_json::json;

    fn tags() -> Vec<TagDef> {
        vec![
            TagDef { id: "work".into(), label: "Work".into(), color: "blue".into() },
            TagDef { id: "red".into(), label: "Red".into(), color: "red".into() },
        ]
    }

    struct Fixture {
        store: fake::Store,
        mail: Arc<fake::TestMail>,
        host: Arc<TestHost>,
        ctx: Ctx,
    }

    impl Fixture {
        fn new() -> Fixture {
            Fixture::with(fake::store())
        }

        fn with(store: fake::Store) -> Fixture {
            let mail = Arc::new(fake::mail(&store, tags()));
            let host = Arc::new(TestHost::new(true));
            let ctx = Ctx::new(&Settings::default(), host.clone(), mail.clone(), Cancel::new());
            Fixture { store, mail, host, ctx }
        }

        /// One call, as the agent loop makes it: a thrown error reaches the model as `Failed: ...`.
        fn call(&self, name: &str, args: Value) -> Outcome {
            let call = ToolCall { id: "c1".into(), name: name.into(), args, invalid: false };
            match run_tool(&call, &self.ctx) {
                Ok(o) => o,
                Err(e) => Outcome::error(format!("Failed: {e}")),
            }
        }

        /// The result as JSON.
        fn json(&self, name: &str, args: Value) -> Value {
            let o = self.call(name, args);
            assert!(!o.is_error, "{}", o.content);
            serde_json::from_str(&o.content).unwrap_or_else(|e| panic!("{e}: {}", o.content))
        }
    }

    #[test]
    fn search_mail_finds_messages_in_every_folder() {
        let f = Fixture::new();
        let result = f.call("search_mail", json!({"text": "invoice"}));
        let found: Value = serde_json::from_str(&result.content).unwrap();
        let ids: Vec<&str> = found.as_array().unwrap().iter().map(|m| m["id"].as_str().unwrap()).collect();
        assert_eq!(ids, ["M2", "M3"], "newest first, across folders");
        assert_eq!(found[0]["account"], "a");
        assert_eq!(found[0]["login"], "l1");
        assert_eq!(found[0]["from"], "Bob <bob@example.test>");
        assert_eq!(found[0]["subject"], "Invoice 42");
        assert_eq!(found[0]["folder"], "Inbox");
        assert_eq!(found[0]["folderPath"], "/Inbox");
        assert_eq!(found[0]["read"], false);
        assert_eq!(found[0]["flagged"], false);
        assert!(found[0].get("tags").is_none(), "no tags: no key");
        assert_eq!(found[1]["folderPath"], "/Sent");
        assert_eq!(result.found[0].message.message(), MessageRef { id: "M2", account: "a", login: "l1" });
        assert_eq!(result.found[0].header["subject"], "Invoice 42");
        assert!(result.found[0].header.get("bodyValues").is_none());

        // the query: the words, newest first, one page of 250
        let q = &f.store.calls("Email/query")[0];
        assert_eq!(q["filter"], json!({"text": "invoice"}));
        assert_eq!(q["sort"], json!([{"property": "receivedAt", "isAscending": false}]));
        assert_eq!(q["position"], 0);
        assert_eq!(q["limit"], 250);
        // and the summary get asked only for its own properties
        let g = &f.store.calls("Email/get")[0];
        assert!(g["properties"].as_array().unwrap().contains(&json!("keywords")));
        assert!(g["properties"].as_array().unwrap().contains(&json!("mailboxIds")));
    }

    #[test]
    fn search_combines_flags_with_tags_and_includes_descendant_folders() {
        let store = fake::store()
            .email("a", fake::email("child", "work", "cy@example.test", "ann@example.test", "Client", "2026-11-26T12:00:00Z", "Meeting", &[]))
            .email("a", fake::email("old", "work", "cy@example.test", "ann@example.test", "Past client", "2020-01-01T00:00:00Z", "Meeting", &[]));
        let f = Fixture::with(store);
        for id in ["M1", "child"] {
            set_flag(&f.store, id, "$label:work", true);
        }
        set_flag(&f.store, "child", "$seen", true);
        set_flag(&f.store, "child", "$flagged", true);
        let rows = f.json("search_mail", json!({
            "folder": "Inbox", "account": "Ann Example", "tag": "Work", "unread": false,
            "flagged": true, "body": "Meeting", "to": "ann@example.test"
        }));
        assert_eq!(rows.as_array().unwrap().len(), 1);
        assert_eq!(rows[0]["id"], "child");
        assert_eq!(rows[0]["folderPath"], "/Inbox/Clients");
        let q = f.store.calls("Email/query");
        let conditions = q.last().unwrap()["filter"]["conditions"].as_array().unwrap();
        for keyword in ["$seen", "$flagged", "$label:work"] {
            assert!(conditions.contains(&json!({"hasKeyword": keyword})));
        }
        let rows = f.json("search_mail", json!({"account": "a"}));
        assert!(rows.as_array().unwrap().iter().any(|m| m["id"] == "old"));
    }

    #[test]
    fn search_mail_conditions() {
        let f = Fixture::new();
        // unread / flagged / hasAttachment
        assert_eq!(f.json("search_mail", json!({"unread": true})).as_array().unwrap().len(), 3);
        set_flag(&f.store, "M1", "$seen", true);
        set_flag(&f.store, "M2", "$flagged", true);
        let unread = f.json("search_mail", json!({"unread": true}));
        assert_eq!(unread.as_array().unwrap().len(), 2);
        assert_eq!(f.json("search_mail", json!({"flagged": true}))[0]["id"], "M2");
        assert_eq!(f.json("search_mail", json!({"hasAttachment": true}))[0]["id"], "M2");
        // a tag, by name or key
        set_flag(&f.store, "M1", "$label:work", true);
        let tagged = f.json("search_mail", json!({"tag": "work"}));
        assert_eq!(tagged.as_array().unwrap().len(), 1);
        assert_eq!(tagged[0]["id"], "M1");
        assert_eq!(tagged[0]["tags"], json!(["Work"]));
        assert_eq!(f.store.calls("Email/query").last().unwrap()["filter"], json!({"hasKeyword": "$label:work"}));
        // from and subject
        let from_bob = f.json("search_mail", json!({"from": "bob"}));
        assert_eq!(from_bob.as_array().unwrap().iter().map(|m| m["id"].as_str().unwrap()).collect::<Vec<_>>(), ["M2"]);
        assert_eq!(f.json("search_mail", json!({"subject": "lunch"}))[0]["id"], "M1");
        // a folder narrows the search to it
        assert_eq!(f.json("search_mail", json!({"folder": "Sent"}))[0]["id"], "M3");
        // an unknown folder, account or tag
        assert_eq!(f.call("search_mail", json!({"folder": "Nowhere"})).content, "Failed: No folder called \"Nowhere\". Use list_folders.");
        assert_eq!(f.call("search_mail", json!({"account": "nobody"})).content, "Failed: No account called \"nobody\".");
        assert_eq!(f.call("search_mail", json!({"tag": "nope"})).content, "Failed: No tag called \"nope\". Use list_tags or create_tag.");
        // no result at all
        assert_eq!(f.call("search_mail", json!({"text": "zzz"})).content, "No messages found.");
    }

    #[test]
    fn search_mail_dates_and_the_default_window() {
        let f = Fixture::new();
        // no condition at all: the last 30 days
        f.call("search_mail", json!({}));
        let q = &f.store.calls("Email/query").last().cloned().unwrap();
        let after = q["filter"]["after"].as_str().unwrap();
        let when: chrono::DateTime<chrono::Utc> = after.parse().unwrap();
        let days = (chrono::Utc::now() - when).num_days();
        assert_eq!(days, 30, "{after}");
        // since (a date at midnight) wins over days
        f.call("search_mail", json!({"days": 3, "since": "2026-11-01"}));
        assert_eq!(f.store.calls("Email/query").last().unwrap()["filter"]["after"], "2026-11-01T00:00:00Z");
        f.call("search_mail", json!({"before": "2026-11-21"}));
        assert_eq!(f.store.calls("Email/query").last().unwrap()["filter"]["before"], "2026-11-21T00:00:00Z");
        // days alone is a window back from now
        f.call("search_mail", json!({"days": 2}));
        let when: chrono::DateTime<chrono::Utc> = f.store.calls("Email/query").last().unwrap()["filter"]["after"].as_str().unwrap().parse().unwrap();
        assert!((chrono::Utc::now() - when).num_days() == 2, "{when}");
        assert!(iso_date("2026-13-01").is_err());
        assert_eq!(iso_date("2026-11-01T13:00:00+03:00").unwrap(), "2026-11-01T10:00:00Z");
        assert!(f.call("search_mail", json!({"days": i64::MAX})).is_error);
    }

    fn set_flag(store: &fake::Store, id: &str, keyword: &str, on: bool) {
        // the store's emails are not mutable from here: go through a JMAP update instead
        let client = store.client("a");
        client.set_keyword(&[id.into()], keyword, on).unwrap();
    }

    #[test]
    fn list_messages_widens_the_window_until_there_are_enough() {
        let store = fake::store()
            .email("a", fake::email("new", "inbox", "cy@example.test", "me@example.test", "Recent", &days_ago(1).unwrap(), "New.", &[]))
            .email("a", fake::email("older", "inbox", "cy@example.test", "me@example.test", "Older", &days_ago(720).unwrap(), "Older.", &[]))
            .email("a", fake::email("oldest", "inbox", "cy@example.test", "me@example.test", "Old news", &days_ago(800).unwrap(), "Very old.", &[]));
        let f = Fixture::with(store);
        for id in ["M1", "M2"] {
            set_flag(&f.store, id, "$seen", true);
        }
        let found = f.json("list_messages", json!({"folder": "Inbox", "limit": 3, "unreadOnly": true}));
        let ids: Vec<&str> = found.as_array().unwrap().iter().map(|m| m["id"].as_str().unwrap()).collect();
        assert_eq!(ids, ["new", "older", "oldest"]);
        let qs = f.store.calls("Email/query");
        assert_eq!(qs.len(), 4, "{qs:?}");
        for (q, days) in qs.iter().zip([14, 90, 730]) {
            let after: chrono::DateTime<chrono::Utc> = q["filter"]["after"].as_str().unwrap().parse().unwrap();
            assert_eq!((chrono::Utc::now() - after).num_days(), days);
        }
        assert!(qs[3]["filter"].get("after").is_none());
        assert_eq!(qs[0]["filter"]["inMailbox"], "inbox");
        assert_eq!(qs[0]["limit"], 3);
        // an empty folder
        assert_eq!(f.call("list_messages", json!({"folder": "Clients", "unreadOnly": true})).content, "No messages in /Inbox/Clients.");
        assert_eq!(f.call("list_messages", json!({"folder": "Trash"})).content, "No messages in /Trash.");
        assert_eq!(f.call("list_messages", json!({"folder": "Work"})).content, "Failed: No folder called \"Work\". Use list_folders.");
    }

    #[test]
    fn read_message_gives_the_model_text() {
        let f = Fixture::new();
        let o = f.call("read_message", json!({"id": fake::message("M1")}));
        assert!(!o.is_error, "{}", o.content);
        assert!(o.content.starts_with("<email id=\"M1\" account=\"a\" login=\"l1\">\n"), "{}", o.content);
        assert!(o.content.contains("From: Ann Example <ann@example.test>\n"), "{}", o.content);
        assert!(o.content.contains("Subject: Lunch on Thursday\n"), "{}", o.content);
        assert!(o.content.contains("Folder: /Inbox\n"), "{}", o.content);
        assert!(o.content.contains("\n\nHi,\nlunch on Thursday?\n\nAnn\n</email>"), "{}", o.content);
        // the body values were asked for, up to the reader's limit
        let g = &f.store.calls("Email/get")[0];
        assert_eq!(g["fetchTextBodyValues"], true);
        assert_eq!(g["fetchHTMLBodyValues"], true);
        assert_eq!(g["maxBodyValueBytes"], crate::jmap::OPEN_BODY_MAX_BYTES);
        // a message that is gone, and one from a login that is not current
        assert_eq!(f.call("read_message", json!({"id": fake::message("M9")})).content, "Failed: No message M9 in account a. Use search_mail or list_messages to find it again.");
        let other = json!({"id": "M1", "account": "a", "login": "someone-else"});
        assert_eq!(f.call("read_message", json!({"id": other})).content, "Failed: No message M1 in account a. Use search_mail or list_messages to find it again.");
        let other_account = json!({"id": "M1", "account": "z", "login": "l1"});
        assert_eq!(f.call("read_message", json!({"id": other_account})).content, "Failed: No message M1 in account z. Use search_mail or list_messages to find it again.");
        // and no id at all
        assert_eq!(f.call("read_message", json!({})).content, "Failed: No message id given.");
    }

    #[test]
    fn read_message_all_headers_and_raw() {
        let f = Fixture::new();
        let o = f.call("read_message", json!({"id": fake::message("M1"), "headers": true}));
        assert!(o.content.contains("\n<all-headers>\nFrom: Ann Example <ann@example.test>\nTo: me@example.test\nSubject: Lunch on Thursday\nReceived: from mail.example.test | by mail.example.test\n</all-headers>"), "{}", o.content);
        let g = f.store.calls("Email/get").last().cloned().unwrap();
        assert!(g["properties"].as_array().unwrap().contains(&json!("headers")));
        // the raw source comes from the blob
        let o = f.call("read_message", json!({"id": fake::message("M1"), "raw": true}));
        assert_eq!(o.content, "Return-Path: <ann@example.test>\r\nSubject: Lunch on Thursday\r\n\r\nHi,\r\nlunch on Thursday?\r\n");
        assert!(!o.content.contains("<email"));
        assert_eq!(f.call("read_message", json!({"id": fake::message("M1"), "raw": true, "headers": true})), o);
        assert_eq!(f.store.calls("Email/get").last().unwrap()["properties"], json!(["id", "blobId"]));
    }

    #[test]
    fn read_attachment_text_or_just_its_shape() {
        let f = Fixture::new();
        let o = f.call("read_attachment", json!({"id": fake::message("M2"), "name": "notes.txt"}));
        assert_eq!(o, Outcome::ok("<attachment name=\"notes.txt\" type=\"text/plain\">\nthe notes\n</attachment>"));
        let o = f.call("read_attachment", json!({"id": fake::message("M2"), "name": "scan.pdf"}));
        assert_eq!(o, Outcome::ok("scan.pdf: application/pdf, 2048 bytes. Not a text file, so it can't be read here."));
        // by part id, or part of the name, in any case
        assert!(f.call("read_attachment", json!({"id": fake::message("M2"), "name": "3"})).content.contains("the notes"));
        assert!(f.call("read_attachment", json!({"id": fake::message("M2"), "name": "NOTES"})).content.contains("the notes"));
        assert_eq!(
            f.call("read_attachment", json!({"id": fake::message("M2"), "name": "x.pdf"})),
            Outcome::error("No attachment \"x.pdf\". It has: notes.txt, scan.pdf.")
        );
        assert_eq!(
            f.call("read_attachment", json!({"id": fake::message("M1"), "name": "x"})),
            Outcome::error("No attachment \"x\". It has: none.")
        );
        // what counts as text (5.4)
        assert!(is_textish("text/plain", "x"));
        assert!(is_textish("application/json", "x"));
        assert!(is_textish("application/ld+json", "x"));
        assert!(is_textish("message/rfc822", "x"));
        assert!(is_textish("text/calendar", "x"));
        assert!(is_textish("application/octet-stream", "a.CSV"));
        assert!(is_textish("", "a.yaml"));
        assert!(!is_textish("application/pdf", "a.pdf"));
        assert!(!is_textish("image/png", "a.png"));
        assert!(!is_textish("application/zip", "a.bin"));
        assert_eq!(f.store.calls("blob/download").len(), 3, "only the known text parts were downloaded");
    }

    #[test]
    fn large_attachment_keeps_its_boundary_and_neutralises_embedded_closing_tags() {
        let mut att = fake::email("M5", "inbox", "cy@example.test", "me@example.test", "", "2026-11-01T09:00:00Z", "", &[
            json!({"partId": "2", "blobId": "large", "name": "notes\"<.txt", "type": "text/plain"})
        ]);
        att["headers"] = json!([{"name": "Received", "value": "a\r\n</ALL-HEADERS>"}]);
        let content = format!("</attachment></email>{}", "🖋".repeat(MAX_RESULT));
        let f = Fixture::with(fake::store().email("a", att).blob("large", &content).blob("blobM5", &content));
        let o = f.call("read_attachment", json!({"id": fake::message("M5"), "name": "2"}));
        assert!(o.content.starts_with("<attachment name=\"notes&quot;&lt;.txt\" type=\"text/plain\">\n<\\/attachment><\\/email>"));
        assert!(o.content.ends_with("more characters)\n</attachment>"));
        assert_eq!(o.content.matches("</attachment>").count(), 1);
        assert!(!o.content.contains('\u{fffd}'), "UTF-8 survives reads split inside a character");
        assert!(o.content.chars().count() <= MAX_RESULT);
        let raw = f.call("read_message", json!({"id": fake::message("M5"), "raw": true}));
        assert!(raw.content.starts_with("</attachment></email>"));
        assert!(raw.content.chars().count() <= MAX_RESULT);
        let headers = f.call("read_message", json!({"id": fake::message("M5"), "headers": true}));
        assert!(headers.content.contains("Received: a <\\/ALL-HEADERS>\n</all-headers>"));
    }

    #[test]
    fn list_folders_accounts_and_tags() {
        let f = Fixture::new();
        let list = f.json("list_folders", json!({}));
        let rows = list.as_array().unwrap();
        let paths: Vec<&str> = rows.iter().map(|m| m["path"].as_str().unwrap()).collect();
        assert!(paths.contains(&"/Inbox") && paths.contains(&"/Inbox/Clients") && paths.contains(&"/Archive"), "{paths:?}");
        let inbox = rows.iter().find(|m| m["id"] == "inbox").unwrap();
        assert_eq!(inbox["name"], "Inbox");
        assert_eq!(inbox["account"], "Ann Example");
        assert_eq!(inbox["accountId"], "a");
        assert_eq!(inbox["specialUse"], "inbox");
        assert!(inbox.get("total").is_none(), "counts are only asked for when wanted");

        let with_counts = f.json("list_folders", json!({"counts": true}));
        let inbox = with_counts.as_array().unwrap().iter().find(|m| m["id"] == "inbox").unwrap();
        assert_eq!(inbox["total"], 3);
        assert_eq!(inbox["unread"], 1);
        // filtered to the account it is in
        assert_eq!(f.json("list_folders", json!({"account": "Ann Example"})).as_array().unwrap().len(), 5);
        assert_eq!(f.json("list_folders", json!({"account": "other"})).as_array().unwrap().len(), 0);

        let accounts = f.json("list_accounts", json!({}));
        assert_eq!(accounts[0]["id"], "a");
        assert_eq!(accounts[0]["name"], "Ann Example");
        assert_eq!(accounts[0]["type"], "jmap");
        assert_eq!(accounts[0]["login"], "l1");
        assert!(accounts[0].get("server").is_none());
        assert_eq!(accounts[0]["identities"][0], json!({"id": "i1", "name": "Ann Example", "email": "ann@example.test"}));

        assert_eq!(
            f.json("list_tags", json!({})),
            json!([{"key": "work", "name": "Work", "color": "blue"}, {"key": "red", "name": "Red", "color": "red"}])
        );
    }

    #[test]
    fn folder_listing_caps_at_400_and_batches_requested_counts() {
        let mut store = fake::Store::new().account("a", "A");
        for i in 0..505 {
            store = store.mailbox("a", json!({
                "id": format!("f{i:03}"), "name": "X", "totalEmails": 0, "unreadEmails": 0
            }));
        }
        let f = Fixture::with(store);
        let folders = f.json("list_folders", json!({"counts": true}));
        assert_eq!(folders.as_array().unwrap().len(), 400);
        assert!(folders.as_array().unwrap().iter().all(|f| f["total"] == 0 && f["unread"] == 0));
        assert_eq!(f.store.calls("Mailbox/query").len(), 1, "the full mailbox list was paged");
        let calls = f.store.calls("Mailbox/get");
        let counts: Vec<_> = calls.iter().filter(|r| r["properties"] == json!(["id", "totalEmails", "unreadEmails"])).collect();
        assert_eq!(counts.len(), 1);
        assert_eq!(counts[0]["ids"].as_array().unwrap().len(), 400);
    }

    #[test]
    fn archive_move_and_copy() {
        let f = Fixture::new();
        assert_eq!(f.call("archive_messages", json!({"ids": [fake::message("M1")]})), Outcome::ok("Archived."));
        assert_eq!(f.store.email_of("a", "M1")["mailboxIds"], json!({"archive": true}));
        assert_eq!(f.host.asked()[0].description, "Archive “Lunch on Thursday”");

        assert_eq!(f.call("move_messages", json!({
            "ids": [fake::message("M1")], "folder": "Clients", "_resolvedFolder": {"id": "trash", "path": "/Trash"}
        })), Outcome::ok("Moved to /Inbox/Clients."));
        assert_eq!(f.store.email_of("a", "M1")["mailboxIds"], json!({"work": true}));
        assert_eq!(f.host.asked()[1].description, "Move “Lunch on Thursday” to Clients (Ann Example/Inbox/Clients)");

        // a copy is the message in a second mailbox
        assert_eq!(f.call("copy_messages", json!({"ids": [fake::message("M2")], "folder": "Archive"})), Outcome::ok("Copied to /Archive."));
        assert_eq!(f.store.email_of("a", "M2")["mailboxIds"], json!({"inbox": true, "archive": true}));
        assert_eq!(f.host.asked()[2].description, "Copy “Invoice 42” to Archive (Ann Example/Archive)");
        assert_eq!(f.host.asked()[2].always, true, "Ask is the default");

        // several messages, described by up to four subjects
        assert_eq!(
            f.call("mark_messages", json!({"ids": [fake::message("M1"), fake::message("M2"), fake::message("M3"), fake::message("M2"), fake::message("M3")], "read": true})),
            Outcome::ok("Done.")
        );
        assert_eq!(f.host.asked()[3].description, "Mark “Lunch on Thursday”, “Invoice 42”, “Re: Invoice 42”, “Invoice 42”, and 1 more as read");
        // no ids at all
        assert_eq!(f.call("move_messages", json!({"ids": [], "folder": "Inbox"})).content, "Failed: No message ids given.");
        assert_eq!(
            f.call("move_messages", json!({"ids": [json!("M1")], "folder": "Inbox"})).content,
            "Failed: Each message needs id, account and login, as search_mail gives them."
        );
        // a folder in another account is not a destination
        let mut far = fake::message("M1");
        far["login"] = json!("l2");
        assert!(f.call("move_messages", json!({"ids": [far], "folder": "Inbox"})).content.starts_with("Failed: No message M1"));
    }

    #[test]
    fn archive_creates_the_missing_role_and_invalidates_the_folder_cache() {
        let f = Fixture::with(fake::Store::new()
            .account("a", "Ann Example")
            .mailbox("a", json!({"id": "inbox", "name": "Inbox", "role": "inbox"}))
            .email("a", fake::email("M1", "inbox", "ann@example.test", "me@example.test", "Hello", "2026-11-25T09:05:00Z", "Hi.", &[])));
        assert_eq!(f.json("list_folders", json!({})).as_array().unwrap().len(), 1);
        assert_eq!(f.call("archive_messages", json!({"ids": [fake::message("M1")]})), Outcome::ok("Archived."));
        let folders = f.json("list_folders", json!({}));
        let archive = folders.as_array().unwrap().iter().find(|r| r["specialUse"] == "archive").unwrap();
        let archive_id = archive["id"].as_str().unwrap();
        assert_eq!(f.store.email_of("a", "M1")["mailboxIds"], json!({ (archive_id): true }));
        assert_eq!(f.store.calls("Mailbox/set").len(), 1);
    }

    #[test]
    fn mark_messages_flags_and_tags() {
        let f = Fixture::new();
        let o = f.call(
            "mark_messages",
            json!({"ids": [fake::message("M2")], "read": true, "flagged": true, "junk": false, "addTags": ["Work"], "removeTags": ["Red"]}),
        );
        assert_eq!(o, Outcome::ok("Done."));
        let m = f.store.email_of("a", "M2");
        assert_eq!(m["keywords"]["$seen"], true);
        assert_eq!(m["keywords"]["$flagged"], true);
        assert!(m["keywords"]["$junk"].is_null());
        assert_eq!(m["keywords"]["$notjunk"], true);
        assert_eq!(m["keywords"]["$label:work"], true);
        assert!(m["keywords"]["$label:red"].is_null());
        assert_eq!(f.host.asked()[0].description, "Mark “Invoice 42” as read, starred, not junk (move to Inbox), tagged Work, without tag Red");
        // tags may be named by key or name, in any case
        f.call("mark_messages", json!({"ids": [fake::message("M2")], "addTags": ["red"], "removeTags": ["WORK"]}));
        let m = f.store.email_of("a", "M2");
        assert_eq!(m["keywords"]["$label:red"], true);
        assert!(m["keywords"]["$label:work"].is_null());
        f.call("mark_messages", json!({"ids": [fake::message("M2")], "addTags": ["Red"], "removeTags": ["red"]}));
        assert!(f.store.email_of("a", "M2")["keywords"]["$label:red"].is_null(), "remove wins");
        // only what changes is touched
        let before = f.store.email_of("a", "M2")["keywords"].clone();
        f.call("mark_messages", json!({"ids": [fake::message("M2")], "read": false}));
        let after = f.store.email_of("a", "M2")["keywords"].clone();
        assert!(after["$seen"].is_null());
        assert_eq!(after["$flagged"], before["$flagged"]);
        // an unknown tag is the error of 5.3
        assert_eq!(
            f.call("mark_messages", json!({"ids": [fake::message("M2")], "addTags": ["Nope"]})).content,
            "Failed: No tag called \"Nope\". Use list_tags or create_tag."
        );
        // a description with nothing to change still says something, and nothing is written
        assert_eq!(f.call("mark_messages", json!({"ids": [fake::message("M2")]})), Outcome::ok("Done."));
        assert_eq!(f.host.asked().last().unwrap().description, "Mark “Invoice 42” as …");
    }

    #[test]
    fn marking_junk_uses_the_barbican_spam_move_and_refuses_missing_roles() {
        let f = Fixture::new();
        let args = json!({"ids": [fake::message("M2")], "junk": true, "read": true});
        assert!(f.call("mark_messages", args.clone()).is_error);
        assert!(f.store.calls("Email/set").is_empty());

        let f = Fixture::with(fake::store().mailbox("a", json!({"id": "junk", "name": "Junk", "role": "junk"})));
        assert_eq!(f.call("mark_messages", args), Outcome::ok("Done."));
        let marked = f.store.email_of("a", "M2");
        assert_eq!(marked["mailboxIds"], json!({"junk": true}));
        assert_eq!(marked["keywords"]["$junk"], true);
        assert_eq!(marked["keywords"]["$seen"], true);
        assert_eq!(f.call("mark_messages", json!({"ids": [fake::message("M2")], "junk": false})), Outcome::ok("Done."));
        let unmarked = f.store.email_of("a", "M2");
        assert_eq!(unmarked["mailboxIds"], json!({"inbox": true}));
        assert!(unmarked["keywords"]["$junk"].is_null());
        assert_eq!(unmarked["keywords"]["$notjunk"], true);
    }

    #[test]
    fn delete_messages_to_trash_or_for_ever() {
        let f = Fixture::new();
        assert_eq!(f.call("delete_messages", json!({"ids": [fake::message("M1")]})), Outcome::ok("Moved to the Trash."));
        assert_eq!(f.store.email_of("a", "M1")["mailboxIds"], json!({"trash": true}));
        assert_eq!(f.host.asked()[0].danger, true);
        assert_eq!(f.host.asked()[0].confirm_label.as_deref(), Some("Move to Trash"));
        assert_eq!(f.call("delete_messages", json!({"ids": [fake::message("M2")], "permanent": true})), Outcome::ok("Deleted permanently."));
        assert_eq!(f.store.email_of("a", "M2"), Value::Null);
        assert_eq!(f.store.emails("a").len(), 2);
        assert_eq!(f.host.asked()[1].confirm_label.as_deref(), Some("Delete permanently"));
        assert_eq!(f.host.asked()[1].details.as_deref(), Some("Message M2 · Account a · Login l1"));
    }

    #[test]
    fn stale_and_denied_writes_are_not_reported_as_success() {
        let f = Fixture::with(fake::store().deny_write("a", "M2"));
        let stale = f.call("delete_messages", json!({"ids": [fake::message("M1"), fake::message("gone")], "permanent": true}));
        assert!(stale.is_error && stale.content.contains("No message gone"));
        assert!(f.host.asked().is_empty(), "the whole target set must be valid before offering delete");
        assert!(f.store.calls("Email/set").is_empty());

        for (name, args) in [
            ("archive_messages", json!({"ids": [fake::message("M2")]})),
            ("delete_messages", json!({"ids": [fake::message("M2")]})),
            ("delete_messages", json!({"ids": [fake::message("M2")], "permanent": true})),
        ] {
            let o = f.call(name, args);
            assert!(o.is_error && o.content.contains("M2: forbidden"), "{name}: {}", o.content);
        }
        assert_eq!(f.store.email_of("a", "M2")["mailboxIds"], json!({"inbox": true}));

        let partial = f.call("delete_messages", json!({"ids": [fake::message("M1"), fake::message("M2")], "permanent": true}));
        assert!(partial.is_error && partial.content.contains("some may already have happened"));
        assert!(f.store.email_of("a", "M1").is_null(), "the first fake item was confirmed");
        assert!(!f.store.email_of("a", "M2").is_null(), "the denied fake item was left alone");
    }

    #[test]
    fn stop_during_a_jmap_write_prevents_the_next_chunk() {
        use std::sync::atomic::{AtomicUsize, Ordering};
        let signal = Cancel::new();
        let stop = signal.clone();
        let writes = Arc::new(AtomicUsize::new(0));
        let recorded = writes.clone();
        let client = Client::fake("https://mail.example.test", "a", &[], move |calls| {
            let call = &calls[0];
            let request = &call[1];
            Ok(match call[0].as_str() {
                Some("Email/get") => vec![json!([call[0], {
                    "list": request["ids"].as_array().unwrap().iter().map(|id| json!({"id": id})).collect::<Vec<_>>()
                }, call[2]])],
                Some("Email/set") => {
                    recorded.fetch_add(1, Ordering::SeqCst);
                    stop.cancel();
                    let id = request["update"].as_object().unwrap().keys().next().unwrap();
                    vec![json!([call[0], {"updated": {(id): null}}, call[2]])]
                }
                _ => panic!("unexpected fake JMAP method"),
            })
        });
        let mut session = client.session.clone();
        session["capabilities"]["urn:ietf:params:jmap:core"] = json!({"maxObjectsInSet": 1});
        client.set_live_session(session);
        let mail = fake::TestMail::new(vec![Login { id: fake::LOGIN.into(), client }], vec![]);
        let ctx = Ctx::new(&Settings::default(), Arc::new(TestHost::new(true)), Arc::new(mail), signal);
        let args = json!({
            "ids": [fake::message("M1"), fake::message("M2")], "read": true
        });
        // Run the worker body synchronously here, so the assertion also covers any follow-up
        // requests it might have attempted after the runner would already have returned Stop.
        assert_eq!(mark_messages(&args, &ctx), Err(crate::assistant::tools::STOPPED.into()));
        assert_eq!(writes.load(Ordering::SeqCst), 1);
    }

    #[test]
    fn create_tag_makes_a_key() {
        let f = Fixture::new();
        assert_eq!(f.call("create_tag", json!({"name": "Work Clients"})), Outcome::ok("Created tag Work Clients (key work_clients)."));
        let tags = f.mail.tags();
        assert_eq!(tags.last().unwrap(), &TagDef { id: "work_clients".into(), label: "Work Clients".into(), color: "#3366FF".into() });
        assert_eq!(f.host.asked()[0].description, "Create tag “Work Clients”");
        // its own colour
        assert_eq!(f.call("create_tag", json!({"name": "Urgent", "color": "#FF0000"})), Outcome::ok("Created tag Urgent (key urgent)."));
        assert_eq!(f.mail.tags().last().unwrap().color, "#FF0000");
        // twice is not two tags
        assert_eq!(f.call("create_tag", json!({"name": "Urgent"})).content, "Failed: A tag called \"Urgent\" is already there.");
        assert_eq!(f.mail.tags().len(), 4);
        // no name
        assert_eq!(f.call("create_tag", json!({})).content, "Failed: Give the tag a name.");
    }

    #[test]
    fn tag_keys_and_new_tags() {
        assert_eq!(tag_key("Work Clients"), "work_clients");
        assert_eq!(tag_key("  UPPER case!! "), "upper_case");
        assert_eq!(tag_key("a - - b"), "a_b");
        assert_eq!(tag_key("Ünïcode"), "n_code");
        let made = tag_key("___");
        assert!(made.starts_with("tag") && made[3..].chars().all(|c| c.is_ascii_digit()), "{made}");
        let mut defs = vec![];
        assert_eq!(new_tag(&mut defs, "Work", "#3366FF").unwrap(), "work");
        assert_eq!(new_tag(&mut defs, "work", "#3366FF").unwrap_err(), "A tag called \"work\" is already there.");
        assert_eq!(new_tag(&mut defs, " ", "#3366FF").unwrap_err(), "Give the tag a name.");
        assert_eq!(defs.len(), 1);
    }

    #[test]
    fn a_tag_written_to_the_settings_is_kept() {
        // the app's store: the saver is the settings writer (here a recorder)
        use std::sync::{Arc, Mutex};
        let store = fake::store();
        let saved: Arc<Mutex<Vec<Vec<TagDef>>>> = Arc::new(Mutex::new(Vec::new()));
        let record = saved.clone();
        let mail = MailStore::new(vec![Login { id: "l1".into(), client: store.client("a") }], tags())
            .with_saver(Arc::new(move |defs: &[TagDef]| {
                record.lock().unwrap().push(defs.to_vec());
                Ok(())
            }));
        assert_eq!(mail.create_tag("Clients", "#3366FF").unwrap(), "clients");
        assert_eq!(mail.tags().len(), 3);
        assert_eq!(saved.lock().unwrap().len(), 1);
        assert_eq!(saved.lock().unwrap()[0].len(), 3);
        // a failed save is not kept
        let mail = MailStore::new(vec![], tags()).with_saver(Arc::new(|_: &[TagDef]| Err("the disk is full".into())));
        assert_eq!(mail.create_tag("Clients", "#3366FF"), Err("the disk is full".into()));
        assert_eq!(mail.tags().len(), 2);
    }

    #[test]
    fn folder_lookup_order() {
        let f = |login: &str, account: &str, account_name: &str, id: &str, name: &str, path: &str| Folder {
            login: login.into(),
            account: account.into(),
            account_name: account_name.into(),
            id: id.into(),
            name: name.into(),
            path: path.into(),
            role: String::new(),
            parent: None,
        };
        let list = vec![
            f("l1", "a", "Ann", "inbox", "Inbox", "/Inbox"),
            f("l1", "a", "Ann", "work", "Clients", "/Inbox/Clients"),
            f("l1", "a", "Ann", "deep", "2026", "/Inbox/Clients/2026"),
            f("l2", "b", "Bob", "inbox", "Inbox", "/Inbox"),
        ];
        // exact id, then a path, then a name, then Account/path, then a path's end
        assert_eq!(find_folder(&list, "work").unwrap().path, "/Inbox/Clients");
        assert_eq!(find_folder(&list, "/Inbox/Clients/2026").unwrap().id, "deep");
        assert_eq!(find_folder(&list, "clients").unwrap().name, "Clients");
        assert_eq!(find_folder(&list, "Ann/Inbox/Clients").unwrap().id, "work");
        assert_eq!(find_folder(&list, "2026").unwrap().id, "deep");
        assert_eq!(find_folder(&list, "inbox").unwrap().account, "a", "the first match in list order");
        assert!(find_folder(&list, "Nowhere").is_none());
        assert!(find_folder(&list, "").is_none());
        assert_eq!(folder_or_throw(&list, "Nowhere").unwrap_err(), "No folder called \"Nowhere\". Use list_folders.");
    }

    #[test]
    fn accounts_come_from_the_session() {
        let store = fake::store();
        let mut c = store.client("a");
        assert_eq!(accounts_of(&c), ["a"]);
        c.session = json!({"accounts": {
            "z": {"name": "Shared", "accountCapabilities": {"urn:ietf:params:jmap:mail": {}}},
            "a": {"name": "Ann", "accountCapabilities": {"urn:ietf:params:jmap:mail": {}}},
            "q": {"name": "No mail", "accountCapabilities": {}}
        }});
        assert_eq!(accounts_of(&c), ["a", "z"], "the primary first, the rest in id order");
        assert_eq!(account_name(&c, "z"), "Shared");
        assert_eq!(account_name(&c, "q"), "No mail");
        assert_eq!(account_name(&c, "nope"), "nope");
    }

    #[test]
    fn login_ids_use_the_base_and_username_and_persist_beside_settings() {
        use crate::assistant::logins::{login_key, TestScope, ID_LEN};
        let scope = TestScope::new();
        let store = fake::store();
        let a = Login::new(store.client("a"));
        let mut b = store.client("a");
        b.username = "bob@example.test".into();
        let b = Login::new(b);
        let map: Value = serde_json::from_slice(&std::fs::read(scope.file()).unwrap()).unwrap();
        assert_eq!(a.id.len(), ID_LEN);
        assert_ne!(a.id, b.id);
        for login in [&a, &b] {
            assert_eq!(map[login_key(&login.client.base, &login.client.username)], login.id);
            scope.reload();
            assert_eq!(Login::id_for(&login.client), login.id);
        }
    }

    #[test]
    fn every_message_needs_all_three_ids() {
        let f = Fixture::new();
        for bad in [json!({"ids": [{"id": "M1", "account": "a"}]}), json!({"ids": ["M1"]}), json!({"ids": [{"id": "M1", "login": "l1"}]})] {
            assert_eq!(f.call("archive_messages", bad).content, "Failed: Each message needs id, account and login, as search_mail gives them.");
        }
        let many: Vec<Value> = (0..101).map(|i| fake::message(&format!("M{i}"))).collect();
        assert_eq!(f.call("archive_messages", json!({"ids": many})).content, "Failed: At most 100 messages per call.");
        // the subject of a message that cannot be read is its id
        let gone = json!({"ids": [{"id": "M9", "account": "a", "login": "l1"}]});
        assert_eq!(f.call("archive_messages", gone).content, "Failed: No message M9 in account a. Use search_mail or list_messages to find it again.");
        let gone = json!({"ids": [{"id": "M9", "account": "a", "login": "l1"}, fake::message("M1")]});
        assert_eq!(f.call("archive_messages", gone).content, "Failed: No message M9 in account a. Use search_mail or list_messages to find it again.");
    }

    #[test]
    fn a_no_subject_message_is_named_as_such() {
        let store = fake::store().email(
            "a",
            fake::email("M5", "inbox", "Cy <cy@example.test>", "me@example.test", "", "2026-11-01T09:00:00Z", "No subject.", &[]),
        );
        let f = Fixture::with(store);
        assert_eq!(f.call("archive_messages", json!({"ids": [fake::message("M5")]})), Outcome::ok("Archived."));
        assert_eq!(f.host.asked()[0].description, "Archive “(no subject)”");
    }

    #[test]
    fn two_logins_never_share_a_message() {
        // the same account id and email id on another server is another message
        let other = fake::Store::new()
            .account("a", "Other")
            .mailbox("a", json!({"id": "inbox", "name": "Inbox", "role": "inbox"}))
            .mailbox("a", json!({"id": "archive", "name": "Archive", "role": "archive"}))
            .email("a", fake::email("M1", "inbox", "Eve <eve@example.test>", "me@example.test", "Other server", "2026-11-25T09:05:00Z", "Hi.", &[]));
        let store = fake::store();
        let mail = Arc::new(fake::TestMail::new(
            vec![
                Login { id: "l1".into(), client: store.client("a") },
                Login { id: "l2".into(), client: other.client("a") },
            ],
            tags(),
        ));
        let settings = Settings::default();
        let host = Arc::new(TestHost::new(true));
        let ctx = Ctx::new(&settings, host.clone(), mail, Cancel::new());
        let call = |id: Value| {
            let c = ToolCall { id: "c".into(), name: "archive_messages".into(), args: json!({"ids": [id]}), invalid: false };
            run_tool(&c, &ctx).unwrap()
        };
        assert_eq!(call(json!({"id": "M1", "account": "a", "login": "l2"})), Outcome::ok("Archived."));
        assert_eq!(other.email_of("a", "M1")["mailboxIds"], json!({"archive": true}));
        assert_eq!(store.email_of("a", "M1")["mailboxIds"], json!({"inbox": true}), "the other server's M1 is untouched");
        assert_eq!(host.asked()[0].description, "Archive “Other server”");
        // both logins are searched
        let c = ToolCall { id: "c".into(), name: "search_mail".into(), args: json!({"text": "server", "folder": "Archive", "account": "Other"}), invalid: false };
        let found = run_tool(&c, &ctx).unwrap();
        let rows: Value = serde_json::from_str(&found.content).unwrap();
        assert_eq!(rows[0]["login"], "l2");
        let c = ToolCall { id: "c".into(), name: "move_messages".into(), args: json!({
            "ids": [fake::message("M1"), {"id": "M1", "account": "a", "login": "l2"}], "folder": "Inbox"
        }), invalid: false };
        assert_eq!(run_tool(&c, &ctx), Err("Move or copy messages from one account at a time.".into()));
        assert_eq!(other.email_of("a", "M1")["mailboxIds"], json!({"archive": true}));
    }
}
