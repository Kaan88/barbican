//! Native plugin host.
//!
//! Drop a plugin DLL into `%APPDATA%\Barbican\plugins\`. It is listed under Settings → Add-ons but NOT loaded or called
//! until the user switches it on (allow-list pref `enabledPlugins`).
//! A plugin is any dynamic library exporting this C ABI (all strings UTF-8, NUL-terminated):
//!
//! ```c
//! // Manifest JSON: {"id","name","version","description","hooks":["email.details", ...]}
//! const char* barbican_plugin_manifest(void);
//! // Handle a hook. `input` is JSON; returns JSON allocated by the plugin (or NULL).
//! char* barbican_plugin_call(const char* hook, const char* input);
//! // Free a string returned by barbican_plugin_call.
//! void barbican_plugin_free(char* s);
//! ```
//!
//! Hooks (API version 1):
//! * `email.details`  input: the full JMAP Email object (with `headers`)
//!   output: `{"rows":[{"label","value","tone":"neutral|success|warning|danger"}]}`
//!   shown in the reader under the sender, like Bulwark's `ui:email-details` slot.
//! * `compose.check`  input: `{"to","cc","bcc","subject","text","attachments":[names]}`
//!   output: `{"warning": "text"}` to hold the send once (like the attachment reminder).
//!   Names are Barbican's own. Hook names borrow Bulwark's as a reference vocabulary (not compatibility):
//!   `plugin_catalog::HOOK_ALIASES` maps them to `onEmailOpen` and `onBeforeEmailSend`, and a manifest may list either
//!   spelling; the plugin is called with the spelling its manifest declared.
//! * `settings`       input: `{}`; output: `{"rows":[...settings rows like ui/settings-schema.json...]}`
//!
//! Safety:
//! * A native library runs code the moment it is loaded (its `DllMain`/constructors), and the C ABI only exposes the
//!   manifest after loading. So the manifest is read from an optional sidecar `<name>.manifest.json` (or `manifest.json`
//!   when the folder holds a single library). Without a sidecar the plugin is listed by file name, unverified, and its
//!   real manifest is read only after the user enables it. Enabling it still runs its code in this process: only
//!   enable plugins you trust. A sidecar that disagrees with the library's own manifest id is rejected.
//! * A plugin is enabled by key AND bound to its file (path + SHA-256, `pluginBindings` pref); a replaced file is no
//!   longer enabled until it is switched off and on again, and if the old version was loaded in this session a restart
//!   is needed (see below). Manifest ids starting with `file:` or holding control characters are rejected, and a
//!   library whose id duplicates another plugin's is refused (after loading: the ABI gives no earlier look).
//! * Loading (`Library::new` + the manifest call) runs on a worker thread with a budget; an overrun marks the plugin
//!   failed. The budget cannot contain a plugin that hangs inside its `DllMain`: that code holds the Windows loader
//!   lock, so threads created afterwards (including the next plugin's loader thread) can block until it returns.
//!   That is inherent to running foreign code in this process and cannot be recovered from here.
//! * A loaded library is never unloaded for the rest of the session: its code may have started threads or registered
//!   callbacks that outlive the handle, and unloading it under them crashes the app. Switching a plugin off only
//!   stops calling it; the library and its backend object stay alive until the process exits. Updating a plugin
//!   therefore requires restarting Barbican: the SHA-256 each path was first loaded with is remembered, and a
//!   different file at the same path is refused with "restart Barbican to load the new version".
//! * At most one call is in flight per plugin: while an abandoned call has not returned, new calls fail at once
//!   (the ABI makes no thread-safety promise), which counts toward the failure limit, and the plugin is not loaded
//!   again until that call returns.
//! * Every call runs on a worker thread with the hook's budget from the catalogue (2000 ms when the hook is not in it,
//!   at most [`MAX_BUDGET_MS`] because the UI waits). A call that overruns is abandoned (its thread may linger) and
//!   counts as a failure. Panics are caught. A panic inside a plugin's own `extern "C"` function aborts at the C
//!   boundary before the host sees it; that cannot be caught from here.
//! * API 2 packages (`plugins\<id>\manifest.json` plus a library, installed by [`crate::plugin_package`]) are checked by
//!   [`validate_manifest`] and listed off. Switching any plugin on needs a [`Grant`] (`pluginGrants`; API 1's
//!   `pluginBindings` still count): the consent dialog ([`Plugin::consent_dialog`]) is shown whenever [`decide`] says the
//!   grant no longer covers the file (another path or SHA-256) or what its manifest asks for.
//! * `compose.check` fails closed (see [`compose_check`]).
//! * [`MAX_FAILURES`] failures in a row switch a plugin off for the session; the user is told through [`drain_notices`].
use crate::plugin_catalog::{lookup_hook, resolve_alias};
use serde_json::{json, Value};
use std::ffi::{c_char, CStr, CString};
use std::sync::atomic::{AtomicBool, AtomicU32, Ordering};
use sha2::{Digest, Sha256};
use std::sync::{mpsc, Arc, Mutex};
use std::time::Duration;

/// Budget for a hook the catalogue does not list.
pub const DEFAULT_BUDGET_MS: u32 = 2000;
/// Upper bound for native calls: the UI thread waits for them, so Bulwark's 10-minute send budget is cut down.
pub const MAX_BUDGET_MS: u32 = 10_000;
/// Consecutive failures that switch a plugin off for the session.
pub const MAX_FAILURES: u32 = 3;

type ManifestFn = unsafe extern "C" fn() -> *const c_char;
type CallFn = unsafe extern "C" fn(*const c_char, *const c_char) -> *mut c_char;
type FreeFn = unsafe extern "C" fn(*mut c_char);

/// What a plugin does with a call, so the host can be tested without a DLL.
/// `Ok(None)` is "nothing to say"; `Err` is a failure (error, panic, bad output).
pub trait PluginBackend: Send + Sync {
    fn call(&self, hook: &str, input: &Value) -> Result<Option<Value>, String>;
}

struct Native {
    call: CallFn,
    free: FreeFn,
    _lib: Arc<libloading::Library>,
}

impl PluginBackend for Native {
    fn call(&self, hook: &str, input: &Value) -> Result<Option<Value>, String> {
        let h = CString::new(hook).map_err(|e| e.to_string())?;
        let i = CString::new(input.to_string()).map_err(|e| e.to_string())?;
        // A panicking plugin must not take the app down with it.
        let out = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| unsafe { (self.call)(h.as_ptr(), i.as_ptr()) }))
            .map_err(|_| "plugin panicked".to_string())?;
        if out.is_null() { return Ok(None); }
        let text = unsafe { CStr::from_ptr(out) }.to_string_lossy().to_string();
        unsafe { (self.free)(out) };
        serde_json::from_str(&text).map(Some).map_err(|e| format!("invalid JSON from plugin: {e}"))
    }
}

pub struct Plugin {
    /// Id the user toggles by: the manifest id when a sidecar manifest exists, else `file:<name>`.
    pub key: String,
    pub id: String,
    pub name: String,
    pub version: String,
    pub description: String,
    pub hooks: Vec<String>,
    pub path: String,
    /// What the manifest declares beyond API 1 (permissions, origins, the package folder).
    pub decl: Declared,
    /// The manifest came from a sidecar file (otherwise it is read from the library once enabled).
    verified: bool,
    /// SHA-256 of the library file when it was discovered.
    fingerprint: String,
    backend: Option<Arc<dyn PluginBackend>>,
    /// Set while a call is in flight (also after it was abandoned on timeout).
    busy: Arc<AtomicBool>,
    load_failed: bool,
    failures: AtomicU32,
    tripped: AtomicBool,
}

static NOTICES: Mutex<Vec<String>> = Mutex::new(Vec::new());

/// Every library loaded this session. Its `Drop` would call `FreeLibrary`, which can pull the code out from
/// under a plugin's threads or callbacks; the process keeps them mapped instead (a bounded, deliberate leak).
static LIBRARIES: Mutex<Vec<Arc<libloading::Library>>> = Mutex::new(Vec::new());

/// Backends of plugins that were switched off or replaced by rediscovery. Keeping the object alive keeps its
/// library handles and plugin-side state intact even while it is disabled and not callable.
static RETIRED_BACKENDS: Mutex<Vec<Arc<dyn PluginBackend>>> = Mutex::new(Vec::new());

/// `(path, SHA-256)` of every library file this session has tried to load. Windows keeps a module mapped, so a
/// different file at the same path cannot actually be loaded; it is refused until the process restarts.
static LOADED_FILES: Mutex<Vec<(String, String)>> = Mutex::new(Vec::new());

fn retire(backend: Option<Arc<dyn PluginBackend>>) {
    if let Some(b) = backend {
        RETIRED_BACKENDS.lock().unwrap_or_else(|e| e.into_inner()).push(b);
    }
}

/// Remember (or check) the hash of a file about to be loaded. The same path with a different hash is refused.
fn load_allowed(path: &str, hash: &str) -> Result<(), String> {
    let mut loaded = LOADED_FILES.lock().unwrap_or_else(|e| e.into_inner());
    match loaded.iter().find(|(p, _)| p == path) {
        Some((_, h)) if h != hash => Err(format!(
            "{path}: this plugin file changed after it was loaded this session; restart Barbican to load the new version")),
        Some(_) => Ok(()),
        None => { loaded.push((path.to_string(), hash.to_string())); Ok(()) }
    }
}

/// Messages for the user (a plugin was switched off); the UI shows them as toasts.
pub fn drain_notices() -> Vec<String> {
    // the last error is the plugin's own text
    std::mem::take(&mut *NOTICES.lock().unwrap_or_else(|e| e.into_inner())).iter().map(|n| shown(n, MAX_SHOWN)).collect()
}

impl Plugin {
    pub fn handles(&self, hook: &str) -> bool {
        let want = resolve_alias(hook);
        self.hooks.iter().any(|h| resolve_alias(h) == want)
    }

    pub fn is_tripped(&self) -> bool { self.tripped.load(Ordering::SeqCst) }

    /// Loaded, switched on and not switched off by failures.
    fn active(&self, enabled: &[String]) -> bool {
        self.backend.is_some() && !self.is_tripped() && enabled.contains(&self.key)
    }

    /// Stop calling this plugin. The library and backend object stay alive for the process lifetime.
    fn park(&mut self) {
        retire(self.backend.take());
    }

    /// The spelling of `hook` this plugin's manifest declared (its aliases are interchangeable).
    fn declared_name<'a>(&'a self, hook: &'a str) -> &'a str {
        let want = resolve_alias(hook);
        self.hooks.iter().find(|h| resolve_alias(h) == want).map_or(hook, |h| h.as_str())
    }


    /// Call a hook on a worker thread; give up after `budget`. Counts toward the failure limit.
    /// Never runs two calls into one plugin at once: if an earlier call has not returned, this one fails at once.
    pub fn call(&self, hook: &str, input: &Value, budget: Duration) -> Result<Option<Value>, String> {
        let Some(backend) = self.backend.clone() else { return Err("not loaded".into()) };
        if self.busy.swap(true, Ordering::SeqCst) {
            let r = Err("the previous call has not returned yet".to_string());
            self.record(&r);
            return r;
        }
        let busy = self.busy.clone();
        let (tx, rx) = mpsc::channel();
        let (h, i) = (self.declared_name(hook).to_string(), input.clone());
        // a fixed thread name: manifest text is untrusted
        let spawned = std::thread::Builder::new().name("barbican-plugin".into()).spawn(move || {
            let r = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| backend.call(&h, &i)))
                .unwrap_or_else(|_| Err("plugin panicked".to_string()));
            busy.store(false, Ordering::SeqCst);
            let _ = tx.send(r);
        });
        let r = match spawned {
            Err(e) => { self.busy.store(false, Ordering::SeqCst); Err(format!("could not start worker: {e}")) }
            Ok(_) => match rx.recv_timeout(budget) {
                Ok(r) => r,
                Err(mpsc::RecvTimeoutError::Timeout) => Err(format!("timed out after {} ms", budget.as_millis())),
                Err(mpsc::RecvTimeoutError::Disconnected) => Err("plugin worker died".to_string()),
            },
        };
        self.record(&r);
        r
    }

    fn record(&self, r: &Result<Option<Value>, String>) {
        match r {
            Ok(_) => self.failures.store(0, Ordering::SeqCst),
            Err(e) => {
                let n = self.failures.fetch_add(1, Ordering::SeqCst) + 1;
                if n >= MAX_FAILURES && !self.tripped.swap(true, Ordering::SeqCst) {
                    NOTICES.lock().unwrap_or_else(|e| e.into_inner())
                        .push(format!("Plugin {} was switched off for this session after {MAX_FAILURES} failures in a row (last: {e}).", self.name));
                }
            }
        }
    }
}

impl Drop for Plugin {
    fn drop(&mut self) {
        // Rediscovery drops old entries (replaced files): keep their loaded code alive too.
        self.park();
    }
}

/// Time a hook may take: the catalogue's budget (aliases resolved), else 2000 ms, never over [`MAX_BUDGET_MS`].
pub fn budget(hook: &str) -> Duration {
    Duration::from_millis(lookup_hook(hook).map_or(DEFAULT_BUDGET_MS, |h| h.budget_ms).min(MAX_BUDGET_MS) as u64)
}

pub fn dir() -> std::path::PathBuf {
    crate::config_dir().join("plugins")
}

struct Meta {
    id: String,
    name: String,
    version: String,
    description: String,
    hooks: Vec<String>,
    decl: Declared,
}

/// What an API 2 manifest declares that API 1 has no place for. API 1 plugins have the default (no permissions).
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Declared {
    /// The manifest's `api` (1 for API 1 plugins and sidecars without one).
    pub api: u32,
    pub author: String,
    pub permissions: Vec<String>,
    pub optional_permissions: Vec<String>,
    pub http_origins: Vec<String>,
    pub slots: Vec<String>,
    /// The package folder (`plugins\<id>\`) for an installed package; `None` for a library dropped into the folder.
    pub package: Option<std::path::PathBuf>,
}

fn parse_manifest(v: &Value) -> Result<Meta, String> {
    let api = v["api"].as_u64().unwrap_or(1);
    if api > MANIFEST_API { return Err(format!("needs plugin API {} (this app has {MANIFEST_API})", v["api"])); }
    if api == 2 {
        let m = validate_manifest(v)?;
        return Ok(Meta { id: m.id, name: m.name, version: m.version, description: m.description, hooks: m.hooks,
            decl: Declared { api: 2, author: m.author, permissions: m.permissions, optional_permissions: m.optional_permissions,
                http_origins: m.http_origins, slots: m.slots, package: None } });
    }
    // API 1 manifests are read as Barbican 0.2.0 read them, so a plugin that loaded then keeps loading: text is
    // cleaned for display (control, direction and zero-width characters, length) instead of refused, a number given
    // as the version counts as text, and hooks that are not plain names (no hook has such a name) are left out
    let id = v["id"].as_str().filter(|s| !s.is_empty() && !s.starts_with("file:") && !s.chars().any(char::is_control))
        .ok_or("manifest has no usable id (empty, starts with file:, or has control characters)")?.to_string();
    let text = |k: &str, max: usize| match &v[k] {
        Value::String(s) => shown(s, max).trim().to_string(),
        Value::Number(n) => n.to_string(),
        _ => String::new(),
    };
    let name = text("name", 100);
    let mut seen = std::collections::HashSet::new();
    let hooks: Vec<String> = v["hooks"].as_array().map(|a| a.iter().take(MAX_LIST_ENTRIES).filter_map(|h| h.as_str())
        .filter(|h| !h.is_empty() && h.chars().count() <= 100 && is_plain(h) && seen.insert(*h)).map(String::from).collect()).unwrap_or_default();
    Ok(Meta { id, name: if name.is_empty() { "Plugin".into() } else { name }, version: text("version", 64),
        description: text("description", 1000), hooks, decl: Declared { api: 1, ..Declared::default() } })
}

// ---------------------------------------------------------------------------------------------------------------
// API 2 manifest, permissions and consent (PLUGINS.md "Manifest", "Permissions", "Consent and storage"; ROADMAP B2)
// ---------------------------------------------------------------------------------------------------------------

/// The highest manifest `api` this build reads: 1 (a library dropped into the folder) or 2 (an installed package).
/// API 1 keeps loading until Barbican 1.0 (PLUGINS.md "Decisions").
pub const MANIFEST_API: u64 = 2;
/// Largest `manifest.json` read (Bulwark `MAX_MANIFEST_BYTES`, `lib/extension-package.ts:34`).
pub const MAX_MANIFEST_BYTES: u64 = 512 * 1024;
/// Most `httpOrigins` a manifest may list (each is resolved when the consent dialog is built).
pub const MAX_HTTP_ORIGINS: usize = 32;

/// One permission as the consent dialog shows it. The wording follows Bulwark's `plugins.permissions.*` texts
/// (`locales/en/common.json`, keyed as `lib/plugin-sandbox/permission-text.ts` says), changed where Barbican differs.
pub struct PermissionText {
    pub name: &'static str,
    /// Listed first and marked "Needs care" (Bulwark `HIGH_RISK_PERMISSIONS` plus the ones PLUGINS.md marks).
    pub high_risk: bool,
    pub title: &'static str,
    pub body: &'static str,
}

const fn perm(name: &'static str, high_risk: bool, title: &'static str, body: &'static str) -> PermissionText {
    PermissionText { name, high_risk, title, body }
}

/// Every permission a Barbican plugin may ask for: Bulwark's `ALL_PERMISSIONS` (`lib/plugin-types.ts:1636-1722`, with
/// the assistant areas) without the three PLUGINS.md drops ([`DROPPED_PERMISSIONS`]).
pub const PERMISSIONS: [PermissionText; 69] = [
    perm("crypto:full", true, "Use your encryption keys", "Use your Stalwart encryption keys and encrypted storage. It can read encrypted mail and act for you. Only allow this for plugins you fully trust."),
    perm("email:read", true, "Read your email", "See senders, recipients, subjects and the full text of any message in your account, and your folders."),
    perm("email:send", true, "Change mail before it is sent", "Read and change the recipients, subject, text and attachments of messages you write, and stop a message from being sent."),
    perm("email:raw-send", true, "Send messages it built itself", "Send complete messages, for example signed or encrypted ones, from your account."),
    perm("email:forward", true, "Forward your mail", "Forward messages from your account with their attachments, without opening the composer. You are asked before the first forward to each address."),
    perm("email:blob-read", true, "Read attachments and message sources", "Read attachments and the full source of messages you act on, and every file you attach before it is sent."),
    perm("email:blob-write", true, "Replace attachments before sending", "Upload files to your mail server and replace a file you attached with another one before the message is sent."),
    perm("email:render-takeover", true, "Change how messages are shown", "Replace the text of messages you open and of the message list, for example to show decrypted content."),
    perm("http:fetch", true, "Connect to other websites", "Send requests to the sites listed below. Anything the plugin can read may be sent there."),
    perm("links:check", true, "Check the links you open", "See every web link you open, with its text, and stop it or open another address instead."),
    perm("filters:write", true, "Change your mail filters", "Add rules to your server-side filters. Such rules can move, forward or delete incoming mail."),
    perm("compose:check", true, "Check what you write", "Read what you write in the composer while you type, and possibly send it to the service it uses."),
    perm("identity:write", true, "Change your sender addresses", "Create, change and delete sender addresses and signatures."),
    perm("vacation:write", true, "Change your out-of-office reply", "Turn your automatic reply on or off and change its text and dates."),
    perm("calendar:respond", true, "Answer invitations", "Accept or decline calendar invitations. Your answer goes to the organiser by email."),
    perm("settings:change", true, "Change your settings", "Change how Barbican looks and works, such as the theme, the time format or the reading pane."),
    perm("admin:config", true, "Use the server administration", "Read and change the settings of your Stalwart server that your administrator account may change."),
    perm("email:write", false, "Change and organise your email", "Add or remove tags, flags and other keywords on your messages, move them between your folders and to the archive, and save drafts."),
    perm("compose:open", false, "Open the composer for you", "Open a new message with recipients, text or attachments filled in. You still decide whether to send it."),
    perm("compose:read", false, "Read the message you are writing", "Read a message while you write it, when you use the plugin in the composer: its text, subject, recipients including Bcc, and the names and sizes of attachments."),
    perm("compose:write", false, "Change the message you are writing", "Replace or add text in a message while you write it, when you use the plugin in the composer. You still decide whether to send."),
    perm("calendar:read", false, "Read your calendar", "See your calendars and their events."),
    perm("calendar:write", false, "Change your calendar", "Create, change and delete events."),
    perm("contacts:read", false, "Read your contacts", "Search and read your address books."),
    perm("contacts:write", false, "Change your contacts", "Create, change and delete contacts and address books."),
    perm("files:read", false, "Read your files", "Find, open and read the files in your account."),
    perm("files:write", false, "Change your files", "Save, create, rename and move files in your account."),
    perm("identity:read", false, "See your sender addresses", "See the addresses you send from and their signatures."),
    perm("filters:read", false, "Read your mail filters", "See the rules that sort your incoming mail."),
    perm("tasks:read", false, "Read your tasks", "See your task lists and tasks."),
    perm("tasks:write", false, "Change your tasks", "Create, change, complete and delete tasks."),
    perm("templates:read", false, "Read your templates", "See your saved message templates."),
    perm("templates:write", false, "Change your templates", "Create, change and delete message templates."),
    perm("vacation:read", false, "See your out-of-office reply", "See whether your automatic reply is on and what it says."),
    perm("settings:read", false, "Read your settings", "See your preferences and tag definitions."),
    perm("settings:write", false, "Change your tags", "Add, rename and reorder your tags."),
    perm("security:read", false, "See your account security", "See whether two-factor sign-in and encryption are on, without access to secrets."),
    perm("auth:observe", false, "Know when you sign in", "Be told when you sign in, switch accounts or your session is renewed. Never your password or tokens."),
    perm("auth:emit", false, "Sign you out", "Sign you out of Barbican."),
    perm("auth:impersonation-return", false, "Take you back to your own account", "After an administrator opened a mailbox through impersonation, end that session and sign back in to the account it started from."),
    perm("account:read", false, "See your accounts", "See the accounts you added with their addresses and servers, and which ones are shared mailboxes."),
    perm("directory:read", false, "Look up people", "Look up people in your organisation's directory."),
    perm("ui:observe", false, "Follow where you are in the app", "Know which page is open and react to links you click."),
    perm("app:lifecycle", false, "Know when the app starts and stops", "Be told when the app starts, goes offline or you sign out."),
    perm("ui:notifications", false, "Show notifications", "Show notifications on your device, or in the app. Each one names the plugin."),
    perm("ui:keyboard", false, "Add keyboard shortcuts", "React to keyboard shortcuts it defines."),
    perm("ui:download-file", false, "Save files to your device", "Offer files it created for saving."),
    perm("ui:commands", false, "Add commands", "Add its own entries to the command palette."),
    perm("ui:message-list-tabs", false, "Add tabs to your inbox", "Sort your inbox into tabs such as Updates or Promotions."),
    perm("ui:toolbar", false, "Add buttons to toolbars", "Show its own buttons in the message toolbar, on an open message and in the top bar."),
    perm("ui:context-menu", false, "Add to the message menu", "Add entries to the menu that opens when you right-click a message."),
    perm("ui:navigation-rail", false, "Add to the navigation bar", "Show its own button in the navigation bar."),
    perm("ui:app-page", false, "Add pages", "Add its own pages to the app, each with a button in the navigation bar."),
    perm("ui:side-panel", false, "Add a panel to other views", "Show its own panel next to Calendar, Contacts, Files and Settings."),
    perm("ui:sidebar-widget", false, "Add to the sidebar", "Show its own boxes and sections in the mail sidebar."),
    perm("ui:settings-section", false, "Add a settings page", "Show its own page in Settings."),
    perm("ui:app-top-banner", false, "Show a banner at the top", "Show a notice across the top of the app."),
    perm("ui:email-banner", false, "Show notices on messages", "Show its own notices and summaries above and inside an open message."),
    perm("ui:email-footer", false, "Add content below messages", "Show its own content under an open message."),
    perm("ui:email-details", false, "Add panels to message details", "Show its own panel next to or below an open message."),
    perm("ui:message-list", false, "Add to the message list", "Show its own content above the message list, and buttons on messages and in the selection toolbar."),
    perm("ui:composer-toolbar", false, "Add to the composer", "Show its own buttons, menu entries and sections while you write a message."),
    perm("ui:composer-sidebar", false, "Add a panel to the composer", "Show its own panel next to the message you write."),
    perm("ui:calendar-action", false, "Add to the calendar", "Show its own buttons and sections on events, and short labels and dots on calendar days."),
    perm("ui:contacts", false, "Add to contacts", "Show its own sections in a contact's details and buttons in the contact list."),
    perm("ui:contact-cryptokeys", false, "Show encryption keys of contacts", "Show and manage the encryption keys on a contact."),
    perm("ui:tasks", false, "Add to tasks", "Show its own sections in a task's details and buttons in the task list."),
    perm("ui:files", false, "Add to files", "Show its own sections in the file preview and entries in a file's menu."),
    perm("ui:admin-page", false, "Add an admin page", "Show its own page in the admin settings."),
];

/// Bulwark permissions Barbican does not offer (PLUGINS.md "Permissions"): `http:post` and `http:proxy` need Bulwark's
/// server, `keystore` waits for a sandbox. Asking for one of them is an install error.
pub const DROPPED_PERMISSIONS: [&str; 3] = ["http:post", "http:proxy", "keystore"];
/// Always granted, never asked for (Bulwark `IMPLICIT_PERMISSIONS`, `lib/plugin-types.ts:1735`).
pub const IMPLICIT_PERMISSIONS: [&str; 2] = ["ui:observe", "app:lifecycle"];
/// Hooks that a second permission also unlocks (hooks.csv `also_permission`; the generated catalogue carries only
/// the first). Pinned against the CSV by `plugin_catalog_check`.
pub const ALSO_PERMISSION: [(&str, &str); 4] = [
    ("onComposeFromLink", "compose:write"),
    ("onBeforeCalendarAlert", "tasks:write"),
    ("onCalendarAlert", "tasks:read"),
    ("onCalendarAlertAcknowledge", "tasks:read"),
];

/// Every consent dialog starts with this line (PLUGINS.md "Permissions").
pub const NATIVE_CODE_LINE: &str = "This plugin is a program. It runs with your Windows rights. The permissions below limit what Barbican lets it do, not what the program itself can do.";

pub fn permission_text(name: &str) -> Option<&'static PermissionText> {
    PERMISSIONS.iter().find(|p| p.name == name)
}

/// A validated API 2 manifest (only the checked fields; unknown ones are dropped).
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Manifest {
    pub id: String,
    pub name: String,
    pub version: String,
    pub author: String,
    pub description: String,
    pub homepage: String,
    pub min_app_version: String,
    /// `library`: platform key (`windows-x86_64`) to file name in the package folder.
    pub libraries: Vec<(String, String)>,
    pub permissions: Vec<String>,
    pub optional_permissions: Vec<String>,
    pub http_origins: Vec<String>,
    pub hooks: Vec<String>,
    pub slots: Vec<String>,
    pub settings: Value,
}

impl Manifest {
    /// The library for the platform Barbican runs on.
    pub fn library_here(&self) -> Option<&str> {
        let key = platform_key();
        self.libraries.iter().find(|(k, _)| *k == key).map(|(_, f)| f.as_str())
    }
}

/// The manifest's `library` key for this build: `<os>-<arch>`, as `windows-x86_64`.
pub fn platform_key() -> String {
    format!("{}-{}", std::env::consts::OS, std::env::consts::ARCH)
}

/// Device names Windows reserves in every folder (`con`, `con.txt`, `com1`, ...), compared without case.
pub fn is_reserved_windows_name(name: &str) -> bool {
    let stem = name.split('.').next().unwrap_or("").trim_end_matches(' ').to_ascii_lowercase();
    matches!(stem.as_str(), "con" | "prn" | "aux" | "nul" | "conin$" | "conout$" | "clock$")
        || ((stem.starts_with("com") || stem.starts_with("lpt")) && stem.len() == 4
            && (stem.as_bytes()[3].is_ascii_digit()))
        || ((stem.starts_with("com") || stem.starts_with("lpt")) && matches!(&stem[3..], "\u{b9}" | "\u{b2}" | "\u{b3}"))
}

/// PLUGINS.md: 3-64 characters of `[a-z0-9._-]`, starting with a letter. The id is also the package folder's name, so
/// it may not end in a dot (Windows drops it) or be a Windows device name.
pub fn valid_id(id: &str) -> bool {
    (3..=64).contains(&id.len())
        && id.starts_with(|c: char| c.is_ascii_lowercase())
        && id.chars().all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || matches!(c, '.' | '_' | '-'))
        && !id.ends_with('.')
        && !is_reserved_windows_name(id)
        // the id is a folder name next to dropped-in libraries and their sidecars: it must never look like one
        && !["dll", "so", "dylib", "json"].iter().any(|e| id.ends_with(&format!(".{e}")))
}

/// Semantic version (semver.org 2.0.0), at most 64 characters.
pub fn valid_semver(v: &str) -> bool {
    static RE: std::sync::OnceLock<regex::Regex> = std::sync::OnceLock::new();
    let re = RE.get_or_init(|| regex::Regex::new(
        r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?(\+[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$").unwrap());
    v.len() <= 64 && re.is_match(v)
}

/// `major.minor.patch` of a version, ignoring pre-release and build parts; `None` when it has not three numbers or
/// one of them does not fit in 64 bits (it is never read as 0).
fn version_triple(v: &str) -> Option<(u64, u64, u64)> {
    let core = v.split(['-', '+']).next().unwrap_or("");
    let n: Vec<u64> = core.split('.').map(|x| x.parse::<u64>().ok()).collect::<Option<_>>()?;
    match n[..] { [a, b, c] => Some((a, b, c)), _ => None }
}

/// True when `host` is an IP address in any spelling a URL parser accepts: IPv6 in brackets (or with a colon), and
/// every name that ends in a number (WHATWG URL: `127.0.0.1`, `127.1`, `2130706433`, `0x7f.1`, `0177.0.0.1`).
pub fn is_ip_literal(host: &str) -> bool {
    let h = host.trim_end_matches('.');
    if h.contains(':') || h.starts_with('[') { return true; }
    let last = h.rsplit('.').next().unwrap_or("");
    !last.is_empty() && (last.bytes().all(|b| b.is_ascii_digit())
        || ((last.starts_with("0x") || last.starts_with("0X")) && last[2..].bytes().all(|b| b.is_ascii_hexdigit())))
}

/// Names that point inside the user's network without asking DNS: `localhost`, a single label (`nas`) and the
/// suffixes used for home and company networks.
pub fn is_private_name(host: &str) -> bool {
    let h = host.trim_end_matches('.').to_ascii_lowercase();
    const SUFFIXES: [&str; 10] = [".localhost", ".local", ".lan", ".home", ".home.arpa", ".internal", ".intranet", ".corp", ".private", ".localdomain"];
    h == "localhost" || !h.contains('.') || SUFFIXES.iter().any(|s| h.ends_with(s))
}

/// A public address: not private, loopback, link-local, CGNAT, metadata, multicast, documentation or reserved, in
/// either family (IPv4 inside IPv6 is checked as IPv4).
pub fn ip_is_public(ip: std::net::IpAddr) -> bool {
    use std::net::{IpAddr, Ipv4Addr};
    fn v4(a: Ipv4Addr) -> bool {
        let o = a.octets();
        !(o[0] == 0 || o[0] == 10 || o[0] == 127 || o[0] >= 224
            || (o[0] == 100 && (64..128).contains(&o[1]))
            || (o[0] == 169 && o[1] == 254)
            || (o[0] == 172 && (16..32).contains(&o[1]))
            || (o[0] == 192 && o[1] == 168)
            || (o[0] == 192 && o[1] == 0 && (o[2] == 0 || o[2] == 2))
            || (o[0] == 198 && (o[1] == 18 || o[1] == 19))
            || (o[0] == 198 && o[1] == 51 && o[2] == 100)
            || (o[0] == 203 && o[1] == 0 && o[2] == 113))
    }
    match ip {
        IpAddr::V4(a) => v4(a),
        IpAddr::V6(a) => {
            let s = a.segments();
            if let Some(m) = a.to_ipv4_mapped() { return v4(m); }
            // NAT64 (64:ff9b::/96) and 6to4 (2002::/16) carry an IPv4 address
            if s[0] == 0x64 && s[1] == 0xff9b && s[2..6] == [0, 0, 0, 0] {
                return v4(Ipv4Addr::new((s[6] >> 8) as u8, s[6] as u8, (s[7] >> 8) as u8, s[7] as u8));
            }
            if s[0] == 0x2002 { return v4(Ipv4Addr::new((s[1] >> 8) as u8, s[1] as u8, (s[2] >> 8) as u8, s[2] as u8)); }
            !(a.is_unspecified() || a.is_loopback() || a.is_multicast()
                || s[0] & 0xfe00 == 0xfc00         // unique local (also the cloud metadata fd00:ec2::254)
                || s[0] & 0xffc0 == 0xfe80         // link-local
                || s[0] & 0xffc0 == 0xfec0         // site-local
                || (s[0] == 0x2001 && s[1] == 0x0db8) // documentation
                || s[0..6] == [0, 0, 0, 0, 0, 0]) // IPv4-compatible and other reserved low addresses
        }
    }
}

/// Is `host` inside the user's network? Its name says so, or it resolves to an address that is not public.
/// Resolution runs on a worker thread and gives up after `budget` (an unanswered name counts as public here: the
/// fetch checks every address again before it connects).
pub fn resolves_private(host: &str, budget: Duration) -> bool {
    if is_private_name(host) { return true; }
    let (tx, rx) = mpsc::channel();
    let h = host.to_string();
    let spawned = std::thread::Builder::new().name("barbican-plugin-dns".into()).spawn(move || {
        use std::net::ToSocketAddrs;
        let r = (h.as_str(), 443).to_socket_addrs().map(|a| a.map(|s| s.ip()).collect::<Vec<_>>()).unwrap_or_default();
        let _ = tx.send(r);
    });
    if spawned.is_err() { return false; }
    rx.recv_timeout(budget).map(|ips| ips.into_iter().any(|ip| !ip_is_public(ip))).unwrap_or(false)
}

/// Which of these hosts are private ([`resolves_private`]), all looked up at the same time under one shared deadline.
pub fn resolve_private_many(hosts: &[String], budget: Duration) -> std::collections::HashSet<String> {
    std::thread::scope(|sc| {
        let jobs: Vec<_> = hosts.iter().map(|h| (h, sc.spawn(move || resolves_private(h, budget)))).collect();
        jobs.into_iter().filter_map(|(h, j)| j.join().unwrap_or(false).then(|| h.clone())).collect()
    })
}

/// `(id, name, author)` as written in an installed package's `manifest.json`, read without validating it; `None`
/// when `dir` is not a real folder (a link or a file) or has no readable manifest.
pub fn installed_identity(dir: &std::path::Path) -> Option<(String, String, String)> {
    let md = std::fs::symlink_metadata(dir).ok()?;
    if md.file_type().is_symlink() || !md.is_dir() { return None; }
    let mf = dir.join("manifest.json");
    if std::fs::metadata(&mf).ok()?.len() > MAX_MANIFEST_BYTES { return None; }
    let v: Value = serde_json::from_str(&std::fs::read_to_string(&mf).ok()?).ok()?;
    let f = |k: &str| v[k].as_str().unwrap_or("").to_string();
    Some((f("id"), f("name"), f("author")))
}

/// Check one `httpOrigins` entry: `https://host[:port]` or `https://*.host[:port]`; no path, no `http:`, no IP
/// literal, no bare `*`, ASCII (punycode) names. Returns it in lower case.
pub fn check_origin(raw: &str) -> Result<String, String> {
    let s = raw.trim().to_ascii_lowercase();
    let bad = |why: &str| Err(format!("httpOrigins entry \"{}\": {why}", quoted(raw)));
    let Some(rest) = s.strip_prefix("https://") else {
        return bad(if s.starts_with("http://") { "plain http is not allowed, use https" } else { "must start with https://" });
    };
    if !raw.is_ascii() { return bad("use the ASCII (xn--) form of the name"); }
    if rest.contains(['/', '?', '#', '@', '\\', ' ']) { return bad("only scheme, host and port, no path"); }
    if rest.starts_with('[') { return bad("IP addresses are not allowed"); }
    let (host, port) = match rest.rsplit_once(':') {
        Some((h, p)) => (h, Some(p)),
        None => (rest, None),
    };
    if let Some(p) = port {
        if p.is_empty() || !p.bytes().all(|b| b.is_ascii_digit()) || !matches!(p.parse::<u32>(), Ok(1..=65535)) {
            return bad("the port must be a number from 1 to 65535");
        }
    }
    if host == "*" || host.is_empty() { return bad("a bare * is not allowed"); }
    let (wild, name) = match host.strip_prefix("*.") { Some(n) => (true, n), None => (false, host) };
    if name.contains('*') { return bad("* is only allowed as the first label (https://*.example.org)"); }
    if is_ip_literal(name) { return bad("IP addresses are not allowed"); }
    if name.len() > 253 || name.ends_with('.') { return bad("not a valid host name"); }
    let labels: Vec<&str> = name.split('.').collect();
    let label_ok = |l: &&str| !l.is_empty() && l.len() <= 63 && !l.starts_with('-') && !l.ends_with('-')
        && l.bytes().all(|b| b.is_ascii_lowercase() || b.is_ascii_digit() || b == b'-');
    if !labels.iter().all(label_ok) { return bad("not a valid host name"); }
    if wild && labels.len() < 2 { return bad("a wildcard needs a domain with at least two labels (https://*.example.org)"); }
    Ok(s)
}

/// The host part of a checked origin (`https://*.a.b:8443` gives `*.a.b`).
pub fn origin_host(origin: &str) -> &str {
    let rest = origin.strip_prefix("https://").unwrap_or(origin);
    rest.rsplit_once(':').map_or(rest, |(h, _)| h)
}

/// Most entries in one manifest list (`permissions`, `hooks`, ...): more than any real plugin needs.
const MAX_LIST_ENTRIES: usize = 500;

fn string_list(v: &Value, key: &str, errors: &mut Vec<String>) -> Vec<String> {
    match &v[key] {
        Value::Null => vec![],
        Value::Array(a) if a.len() > MAX_LIST_ENTRIES => { errors.push(format!("\"{key}\" has more than {MAX_LIST_ENTRIES} entries")); vec![] }
        Value::Array(a) if a.iter().all(Value::is_string) => {
            let mut seen = std::collections::HashSet::new();
            a.iter().filter_map(|x| x.as_str()).filter(|s| seen.insert(*s)).map(String::from).collect()
        }
        _ => { errors.push(format!("\"{key}\" must be a list of strings")); vec![] }
    }
}

/// Direction marks and overrides, zero-width and other invisible format characters: they make text read differently
/// from what it is (a name that looks reversed, two names that look the same). The zero-width non-joiner and joiner
/// (U+200C, U+200D) are allowed: Persian and Indic writing and joined emoji need them. The line and paragraph
/// separators (U+2028, U+2029) are in too: they would start a new line in a one-line text.
pub fn is_invisible(c: char) -> bool {
    matches!(c, '\u{00AD}' | '\u{061C}' | '\u{180E}' | '\u{200B}' | '\u{200E}' | '\u{200F}' | '\u{202A}'..='\u{202E}'
        | '\u{2028}' | '\u{2029}' | '\u{2060}'..='\u{2064}' | '\u{2066}'..='\u{206F}' | '\u{FEFF}' | '\u{FFF9}'..='\u{FFFB}')
}

/// Text that is safe to show as one line: no control characters and no [`is_invisible`] ones.
fn is_plain(s: &str) -> bool { !s.chars().any(|c| c.is_control() || is_invisible(c)) }

/// Longest text from a plugin or its manifest shown in a toast or in the Add-ons errors.
pub const MAX_SHOWN: usize = 300;

/// Text that came from a plugin (a manifest value, an error message) made fit to show: control characters become
/// spaces, invisible ones are dropped, and it is cut to `max` characters.
pub fn shown(s: &str, max: usize) -> String {
    let clean: String = s.chars().filter(|c| !is_invisible(*c)).map(|c| if c.is_control() { ' ' } else { c }).collect();
    if clean.chars().count() <= max { clean } else { format!("{}…", clean.chars().take(max).collect::<String>()) }
}

/// A manifest value quoted inside an error message: [`shown`], at most 64 characters.
fn quoted(s: &str) -> String { shown(s, 64) }

fn plain_text(v: &Value, key: &str, max: usize, required: bool, errors: &mut Vec<String>) -> String {
    match &v[key] {
        Value::String(s) if s.chars().count() <= max && is_plain(s) && (!required || !s.trim().is_empty()) => s.clone(),
        Value::Null if !required => String::new(),
        Value::Null => { errors.push(format!("\"{key}\" is missing")); String::new() }
        _ => { errors.push(format!("\"{key}\" must be text of at most {max} characters without control, direction or zero-width characters")); String::new() }
    }
}

/// A settings key or view id: 1-64 characters of `[A-Za-z0-9._-]`.
fn valid_key(k: &str) -> bool {
    (1..=64).contains(&k.len()) && k.bytes().all(|b| b.is_ascii_alphanumeric() || matches!(b, b'.' | b'_' | b'-'))
}

/// Bulwark's `SettingFieldSchema` (`lib/plugin-types.ts:209`) plus `secret`.
fn check_settings(v: &Value, errors: &mut Vec<String>) -> Value {
    let Some(obj) = v.as_object() else {
        if !v.is_null() { errors.push("\"settings\" must be an object".into()); }
        return json!({});
    };
    if obj.len() > 100 { errors.push("\"settings\" has more than 100 fields".into()); return json!({}); }
    let mut out = serde_json::Map::new();
    for (k, f) in obj {
        let e0 = errors.len();
        let bad = |why: &str| format!("setting \"{}\": {why}", quoted(k));
        if !valid_key(k) { errors.push(bad("the key must be 1-64 characters of A-Z a-z 0-9 . _ -")); continue; }
        let kind = f["type"].as_str().unwrap_or("");
        if !matches!(kind, "boolean" | "string" | "number" | "select" | "secret") {
            errors.push(bad("type must be boolean, string, number, select or secret"));
            continue;
        }
        if !f["label"].as_str().is_some_and(|l| !l.trim().is_empty() && l.chars().count() <= 200) { errors.push(bad("needs a label")); }
        if !f["description"].is_null() && !f["description"].is_string() { errors.push(bad("description must be text")); }
        let d = &f["default"];
        match kind {
            "boolean" => if !d.is_null() && !d.is_boolean() { errors.push(bad("the default must be true or false")); },
            "string" => if !d.is_null() && !d.is_string() { errors.push(bad("the default must be text")); },
            "secret" => if !d.is_null() && d.as_str() != Some("") { errors.push(bad("a secret has no default")); },
            "number" => {
                let (min, max) = (f["min"].as_f64(), f["max"].as_f64());
                if (!f["min"].is_null() && min.is_none()) || (!f["max"].is_null() && max.is_none()) { errors.push(bad("min and max must be numbers")); }
                if let (Some(a), Some(b)) = (min, max) { if a > b { errors.push(bad("min is above max")); } }
                match d {
                    Value::Null => {}
                    Value::Number(n) => {
                        let n = n.as_f64().unwrap_or(0.0);
                        if min.is_some_and(|m| n < m) || max.is_some_and(|m| n > m) { errors.push(bad("the default is outside min..max")); }
                    }
                    _ => errors.push(bad("the default must be a number")),
                }
            }
            _ => { // select
                let opts: Vec<&str> = f["options"].as_array().map(|a| a.iter().filter_map(|o| o.as_str()).collect()).unwrap_or_default();
                if opts.is_empty() || f["options"].as_array().is_some_and(|a| a.len() != opts.len()) { errors.push(bad("a select needs a list of text options")); }
                if !d.is_null() && !d.as_str().is_some_and(|d| opts.contains(&d)) { errors.push(bad("the default must be one of the options")); }
            }
        }
        if errors.len() == e0 { out.insert(k.clone(), f.clone()); }
    }
    Value::Object(out)
}

/// Check an API 2 manifest against PLUGINS.md "Manifest" (Bulwark `checkPluginManifest`,
/// `lib/extension-package.ts:403`). Every problem is reported, joined with "; ".
pub fn validate_manifest(v: &Value) -> Result<Manifest, String> {
    use crate::plugin_catalog::{Support, SLOTS};
    if !v.is_object() { return Err("the manifest must be a JSON object".into()); }
    let mut errors: Vec<String> = vec![];
    match v["api"].as_u64() {
        Some(2) => {}
        Some(n) if n > MANIFEST_API => return Err(format!("needs plugin API {n} (this app has {MANIFEST_API})")),
        _ => errors.push("\"api\" must be 2 (an API 1 plugin is a single library dropped into the plugins folder)".into()),
    }
    let id = v["id"].as_str().unwrap_or("").to_string();
    if !valid_id(&id) { errors.push(format!("\"id\" \"{}\" must be 3-64 characters of a-z 0-9 . _ -, start with a letter and not end with a dot", quoted(&id))); }
    let name = plain_text(v, "name", 100, true, &mut errors);
    let version = v["version"].as_str().unwrap_or("").to_string();
    if !valid_semver(&version) { errors.push(format!("\"version\" \"{}\" must be a semantic version such as 1.2.3", quoted(&version))); }
    let author = plain_text(v, "author", 200, false, &mut errors);
    let description = plain_text(v, "description", 1000, false, &mut errors);
    let homepage = plain_text(v, "homepage", 500, false, &mut errors);
    if !homepage.is_empty() && !homepage.starts_with("https://") { errors.push("\"homepage\" must be an https:// address".into()); }
    let min_app_version = plain_text(v, "minAppVersion", 64, false, &mut errors);
    if !min_app_version.is_empty() {
        if !valid_semver(&min_app_version) { errors.push("\"minAppVersion\" must be a semantic version such as 0.5.0".into()); }
        else if !version_triple(&min_app_version).zip(version_triple(env!("CARGO_PKG_VERSION"))).is_some_and(|(need, have)| need <= have) {
            errors.push(format!("needs Barbican {min_app_version} or later (this is {})", env!("CARGO_PKG_VERSION")));
        }
    }

    let mut libraries = vec![];
    match v["library"].as_object() {
        Some(map) if !map.is_empty() => for (k, f) in map {
            let file = f.as_str().unwrap_or("");
            let ext = match k.split('-').next().unwrap_or("") { "windows" => Some("dll"), "linux" => Some("so"), "macos" => Some("dylib"), _ => None };
            let plain = !file.is_empty() && file.len() <= 128 && !file.contains(['/', '\\', ':']) && !file.starts_with('.')
                && !file.chars().any(char::is_control) && !is_reserved_windows_name(file);
            if !valid_key(k) { errors.push(format!("library key \"{}\" must look like windows-x86_64", quoted(k))); }
            else if !plain { errors.push(format!("library \"{k}\" must be a plain file name in the package folder")); }
            else if ext.is_some_and(|e| !file.to_ascii_lowercase().ends_with(&format!(".{e}"))) {
                errors.push(format!("library \"{k}\" must be a .{} file", ext.unwrap_or("")));
            } else { libraries.push((k.clone(), file.to_string())); }
        },
        _ => errors.push("\"library\" must map a platform (windows-x86_64) to the library file".into()),
    }

    let known = |p: &str| PERMISSIONS.iter().any(|x| x.name == p);
    let mut permissions = string_list(v, "permissions", &mut errors);
    for p in &permissions {
        if DROPPED_PERMISSIONS.contains(&p.as_str()) { errors.push(format!("the permission {p} is not available in Barbican")); }
        else if !known(p) { errors.push(format!("unknown permission {}", quoted(p))); }
    }
    permissions.retain(|p| known(p));
    // like Bulwark: optional permissions this version does not offer are left out, so one package suits several versions
    let mut optional_permissions = string_list(v, "optionalPermissions", &mut errors);
    optional_permissions.retain(|p| known(p) && !permissions.contains(p));
    let holds = |p: &str| IMPLICIT_PERMISSIONS.contains(&p) || permissions.iter().any(|x| x == p) || optional_permissions.iter().any(|x| x == p);

    let hooks = string_list(v, "hooks", &mut errors);
    for h in &hooks {
        match lookup_hook(h) {
            None => errors.push(format!("unknown hook {}", quoted(h))),
            Some(c) if c.support == Support::Skip => errors.push(format!("the hook {h} is not available in Barbican")),
            Some(c) => {
                let also = ALSO_PERMISSION.iter().find(|(n, _)| *n == c.name).map(|(_, p)| *p);
                if !holds(c.permission) && !also.is_some_and(holds) {
                    errors.push(format!("the hook {h} needs the permission {}", c.permission));
                }
            }
        }
    }
    let slots = string_list(v, "slots", &mut errors);
    for s in &slots {
        match SLOTS.iter().find(|x| x.name == s) {
            None => errors.push(format!("unknown slot {}", quoted(s))),
            Some(c) if c.support == Support::Skip => errors.push(format!("the slot {s} is not available in Barbican")),
            // Bulwark `mayOfferSlot` (lib/plugin-sandbox/permissions.ts:113): any one of the slot's permissions
            Some(c) => if !c.permissions.split(';').any(holds) {
                errors.push(format!("the slot {s} needs the permission {}", c.permissions.replace(';', " or ")));
            },
        }
    }

    let mut http_origins = vec![];
    let listed = string_list(v, "httpOrigins", &mut errors);
    if listed.len() > MAX_HTTP_ORIGINS { errors.push(format!("\"httpOrigins\" lists {} entries; at most {MAX_HTTP_ORIGINS} are allowed", listed.len())); }
    for o in listed.into_iter().take(MAX_HTTP_ORIGINS) {
        match check_origin(&o) {
            Ok(o) => if !http_origins.contains(&o) { http_origins.push(o) },
            Err(e) => errors.push(e),
        }
    }
    if !http_origins.is_empty() && !holds("http:fetch") { errors.push("\"httpOrigins\" needs the permission http:fetch".into()); }
    let settings = check_settings(&v["settings"], &mut errors);

    if !errors.is_empty() { return Err(errors.join("; ")); }
    Ok(Manifest { id, name, version, author, description, homepage, min_app_version, libraries, permissions,
        optional_permissions, http_origins, hooks, slots, settings })
}

/// What switching a plugin on records (PLUGINS.md "Consent"), kept in the `pluginGrants` pref keyed by id. It pins the
/// library file (path and SHA-256); `privateHosts` are the declared hosts inside the user's network that the dialog
/// named separately and the user accepted.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Grant {
    pub id: String,
    pub path: String,
    pub sha256: String,
    pub granted: Vec<String>,
    pub origins: Vec<String>,
    pub private_hosts: Vec<String>,
    pub at: String,
    /// The author and version of what was accepted; `None` in grants written before they were recorded (and in API
    /// 1's bindings), which then count as from an unknown author.
    pub author: Option<String>,
    pub version: Option<String>,
}

impl Grant {
    pub fn to_json(&self) -> Value {
        let mut v = json!({"id": self.id, "path": self.path, "sha256": self.sha256, "granted": self.granted, "origins": self.origins,
            "privateHosts": self.private_hosts, "at": self.at});
        if let Some(a) = &self.author { v["author"] = a.as_str().into(); }
        if let Some(x) = &self.version { v["version"] = x.as_str().into(); }
        v
    }

    pub fn from_json(v: &Value) -> Option<Grant> {
        let list = |k: &str| v[k].as_array().map(|a| a.iter().filter_map(|x| x.as_str().map(String::from)).collect()).unwrap_or_default();
        Some(Grant { id: v["id"].as_str()?.to_string(), path: v["path"].as_str()?.to_string(), sha256: v["sha256"].as_str()?.to_string(),
            granted: list("granted"), origins: list("origins"), private_hosts: list("privateHosts"),
            at: v["at"].as_str().unwrap_or("").to_string(),
            author: v["author"].as_str().map(String::from), version: v["version"].as_str().map(String::from) })
    }
}

/// The grants in the prefs: `pluginGrants`, and API 1's `pluginBindings` (`"path|sha256"`, no permissions) for plugins
/// switched on before grants existed.
pub fn grants_from_prefs(prefs: &serde_json::Map<String, Value>) -> Vec<Grant> {
    let mut out: Vec<Grant> = prefs.get("pluginGrants").and_then(|v| v.as_object())
        .map(|m| m.values().filter_map(Grant::from_json).collect()).unwrap_or_default();
    if let Some(b) = prefs.get("pluginBindings").and_then(|v| v.as_object()) {
        for (id, v) in b {
            let Some((path, sha)) = v.as_str().and_then(|s| s.rsplit_once('|')) else { continue };
            if !out.iter().any(|g| &g.id == id) {
                out.push(Grant { id: id.clone(), path: path.into(), sha256: sha.into(), ..Grant::default() });
            }
        }
    }
    out
}

/// What switching a plugin on must ask.
#[derive(Clone, Debug, PartialEq)]
pub enum Consent {
    /// The grant still covers this file and what it asks for: no dialog.
    Granted,
    /// An update by the same author that asks for exactly what was accepted (new files, the same permissions and
    /// sites): the dialog says so and lists only private hosts that were not accepted as such.
    SamePermissions,
    /// Ask, listing `permissions` and `origins` (everything it asks for). `update` is true for an update by the same
    /// author that asks for anything else than was accepted (more, fewer or other): what is new is marked. `update` is
    /// false for a first switch-on and for a package whose author differs from the accepted one or is not known
    /// (empty, or not recorded in the grant).
    Ask { update: bool, permissions: Vec<String>, origins: Vec<String> },
}

/// The rule of PLUGINS.md "Consent": a grant counts only for the same path and SHA-256 and only while the manifest
/// asks for nothing beyond it. A file with another hash is always off until the user says yes again. Nothing of an
/// old grant is taken over for another author (or one the grant does not name), and a same-author update that asks
/// for anything else than before lists all of it, never just the difference.
pub fn decide(path: &str, sha256: &str, author: &str, permissions: &[String], origins: &[String], grant: Option<&Grant>) -> Consent {
    let all = |update: bool| Consent::Ask { update, permissions: permissions.to_vec(), origins: origins.to_vec() };
    let Some(g) = grant else { return all(false) };
    let added_p = permissions.iter().any(|p| !g.granted.contains(p));
    let added_o = origins.iter().any(|o| !g.origins.contains(o));
    if g.path == path && g.sha256 == sha256 && !added_p && !added_o { return Consent::Granted; }
    // an empty author names nobody: two packages without one are not known to be by the same author
    if author.is_empty() || g.author.as_deref() != Some(author) { return all(false); }
    let same = |a: &[String], b: &[String]| a.iter().all(|x| b.contains(x)) && b.iter().all(|x| a.contains(x));
    if same(permissions, &g.granted) && same(origins, &g.origins) { return Consent::SamePermissions; }
    all(true)
}

impl Plugin {
    /// What switching it on grants: the manifest's `permissions` (implicit ones left out; optional ones are asked for
    /// at run time) and, with `http:fetch`, its `httpOrigins`.
    pub fn requested(&self) -> (Vec<String>, Vec<String>) {
        let perms: Vec<String> = self.decl.permissions.iter().filter(|p| !IMPLICIT_PERMISSIONS.contains(&p.as_str())).cloned().collect();
        let origins = if perms.iter().any(|p| p == "http:fetch") { self.decl.http_origins.clone() } else { vec![] };
        (perms, origins)
    }

    pub fn consent(&self, grants: &[Grant]) -> Consent {
        let (p, o) = self.requested();
        decide(&self.path, &self.fingerprint, &self.decl.author, &p, &o, grants.iter().find(|g| g.id == self.key))
    }

    /// The grant recorded when the user says yes. `private_hosts` are the hosts the dialog listed as private.
    pub fn grant(&self, private_hosts: Vec<String>, at: &str) -> Grant {
        let (granted, origins) = self.requested();
        Grant { id: self.key.clone(), path: self.path.clone(), sha256: self.fingerprint.clone(), granted, origins, private_hosts, at: at.into(),
            author: Some(self.decl.author.clone()), version: Some(self.version.clone()) }
    }

    /// The consent dialog (title and plain text) and the private hosts it names. `is_private` says whether a declared
    /// host is inside the user's network ([`resolves_private`] in the app). Wildcards never cover a private host, so
    /// only exact hosts are asked about.
    pub fn consent_dialog(&self, consent: &Consent, grants: &[Grant], is_private: &dyn Fn(&str) -> bool) -> (String, String, Vec<String>) {
        let (_, all_origins) = self.requested();
        let private: Vec<String> = all_origins.iter().filter(|o| !origin_host(o).starts_with("*.") && is_private(origin_host(o))).cloned().collect();
        let old = grants.iter().find(|g| g.id == self.key);
        // an update by the same author asking for exactly what was accepted: only private hosts not accepted then
        // are listed. Otherwise everything is listed (what an update adds is marked).
        let same_set = matches!(consent, Consent::SamePermissions);
        let (update, perms, origins) = match consent {
            Consent::Granted | Consent::SamePermissions => (true, vec![], vec![]),
            Consent::Ask { update, permissions, origins } => (*update, permissions.clone(), origins.clone()),
        };
        let new_private: Vec<String> = private.iter().filter(|o| !same_set || !old.is_some_and(|g| g.private_hosts.contains(o))).cloned().collect();
        let public: Vec<String> = origins.iter().filter(|o| !private.contains(o)).cloned().collect();
        let is_new = |granted: &[String], x: &String| update && !granted.contains(x);
        let mut t = format!("{NATIVE_CODE_LINE}\n\n{}", self.name);
        if !self.version.is_empty() { t += &format!(" {}", self.version); }
        if !self.decl.author.is_empty() { t += &format!(" by {}", self.decl.author); }
        t += "\n";
        // only reached for an update (same, named author)
        let changed = match old.and_then(|g| g.version.as_deref()) {
            Some(v) if v != self.version => format!("Updated from version {v} to {}, naming the same author (packages are not signed, so Barbican cannot prove it)", self.version),
            Some(_) => format!("Its files changed since you switched it on (still version {})", self.version),
            None => format!("Updated to version {}", self.version),
        };
        if self.decl.api < 2 {
            t += "\nIt uses plugin API 1, which has no permissions: Barbican lets it see what these hooks hand it.\n";
            let hooks = if self.hooks.is_empty() { "none declared (it is asked once it is loaded)".to_string() } else { self.hooks.join(", ") };
            t += &format!("Hooks: {hooks}\n");
            if old.is_some() { t += "\nThe file changed since you switched it on.\n"; }
        } else if same_set && new_private.is_empty() {
            t += &format!("\n{changed}. It asks for the same permissions as before.\n");
        } else {
            if let (Some(g), false) = (old, update) {
                t += &match g.author.as_deref() {
                    Some(a) if !a.is_empty() && !self.decl.author.is_empty() => format!(
                        "\nYou had switched on a plugin with this id by {a}. This one is by {}, so nothing you accepted then carries over.\n",
                        self.decl.author),
                    _ => "\nYou had switched on an earlier plugin with this id. Barbican cannot tell whether this one has the same author, \
                        so nothing you accepted then carries over.\n".to_string(),
                };
            }
            t += &(if same_set { format!("\n{changed}. It asks for the same permissions as before, and:\n") }
                else if update { format!("\n{changed}. What it asks for changed; all of it is listed, [New] marks what was not accepted before. It asks to:\n") }
                else { "\nIt asks to:\n".to_string() });
            let mut sorted: Vec<&String> = perms.iter().collect();
            sorted.sort_by_key(|p| !permission_text(p).is_some_and(|x| x.high_risk));
            let granted = old.map_or(&[][..], |g| &g.granted[..]);
            for p in sorted {
                let new = if is_new(granted, p) { "[New] " } else { "" };
                match permission_text(p) {
                    Some(x) => t += &format!("- {new}{}{}: {}\n", if x.high_risk { "[Needs care] " } else { "" }, x.title, x.body),
                    None => t += &format!("- {new}{p}\n"),
                }
            }
            if perms.is_empty() && !same_set { t += "- (no permissions)\n"; }
            let accepted = old.map_or(&[][..], |g| &g.origins[..]);
            if !public.is_empty() {
                t += "\nSites it may connect to (http:fetch):\n";
                for o in &public { t += &format!("  {}{o}\n", if is_new(accepted, o) { "[New] " } else { "" }); }
            }
            if !new_private.is_empty() {
                t += "\nHosts inside your network (private, loopback or LAN). These let it reach machines on your own network:\n";
                for o in &new_private { t += &format!("  {}{o}\n", if is_new(accepted, o) { "[New] " } else { "" }); }
            }
        }
        if !self.decl.optional_permissions.is_empty() {
            let names: Vec<&str> = self.decl.optional_permissions.iter().map(|p| permission_text(p).map_or(p.as_str(), |x| x.title)).collect();
            t += &format!("\nIt may ask later for: {}.\n", names.join(", "));
        }
        t += "\nSwitch it on?";
        let title = if update { format!("Switch on {} again?", self.name) } else { format!("Switch on {}?", self.name) };
        (title, t, private)
    }
}

/// Plugin data folder (`plugin-data\<id>\` holds a plugin's storage and blobs; PLUGINS.md "Decisions").
pub fn data_dir() -> std::path::PathBuf {
    crate::config_dir().join("plugin-data")
}

/// Has this session loaded a library from inside `folder`? Windows keeps such a file locked until Barbican exits.
pub fn loaded_under(folder: &std::path::Path) -> bool {
    LOADED_FILES.lock().unwrap_or_else(|e| e.into_inner()).iter().any(|(p, _)| std::path::Path::new(p).starts_with(folder))
}

fn is_lib(p: &std::path::Path) -> bool {
    p.extension().is_some_and(|x| x.eq_ignore_ascii_case(std::env::consts::DLL_EXTENSION))
}

/// List the plugins in the plugins folder WITHOUT loading any library.
pub fn discover() -> (Vec<Plugin>, Vec<String>) {
    let _ = std::fs::create_dir_all(dir());
    discover_in(&dir())
}

pub fn discover_in(folder: &std::path::Path) -> (Vec<Plugin>, Vec<String>) {
    let mut out: Vec<Plugin> = vec![];
    let mut errors = vec![];
    let Ok(entries) = std::fs::read_dir(folder) else { return (out, errors) };
    let paths: Vec<std::path::PathBuf> = entries.flatten().map(|e| e.path()).collect();
    // installed packages first: `plugins\<id>\manifest.json` (an API 2 manifest) and its library
    let mut dirs: Vec<&std::path::PathBuf> = paths.iter()
        .filter(|p| p.is_dir() && !is_lib(p) && !p.file_name().is_some_and(|n| n.to_string_lossy().starts_with('.'))).collect();
    dirs.sort();
    for d in dirs {
        let dir_name = d.file_name().map(|f| f.to_string_lossy().to_string()).unwrap_or_default();
        // `is_dir` follows links: a package folder that is a symbolic link or junction is refused, as the installer and
        // `installed_identity` refuse it (it could point anywhere, and uninstall would not remove what it points to)
        if std::fs::symlink_metadata(d).map_or(true, |md| md.file_type().is_symlink() || !md.is_dir()) {
            if d.join("manifest.json").is_file() { errors.push(format!("{dir_name}: a plugin folder may not be a link or junction")); }
            continue;
        }
        let mf = d.join("manifest.json");
        if !mf.is_file() { continue; }
        let m = match std::fs::metadata(&mf).map_err(|e| e.to_string())
            .and_then(|m| if m.len() > MAX_MANIFEST_BYTES { Err("manifest.json is too large".into()) } else { Ok(()) })
            .and_then(|_| std::fs::read_to_string(&mf).map_err(|e| e.to_string()))
            .and_then(|t| serde_json::from_str::<Value>(&t).map_err(|e| format!("bad manifest.json: {e}")))
            .and_then(|v| validate_manifest(&v)) {
            Ok(m) => m,
            Err(e) => { errors.push(format!("{dir_name}: {e}")); continue; }
        };
        if m.id != dir_name { errors.push(format!("{dir_name}: the folder name must be the plugin id {}", m.id)); continue; }
        let Some(lib) = m.library_here() else { errors.push(format!("{dir_name}: no library for {}", platform_key())); continue };
        let lib = d.join(lib);
        let hash = match std::fs::symlink_metadata(&lib) {
            // a package is pinned as a whole (every file), not only its library
            Ok(md) if md.is_file() => package_fingerprint(d, true),
            _ => Err(format!("its library {} is missing", lib.display())),
        };
        let hash = match hash { Ok(h) => h, Err(e) => { errors.push(format!("{dir_name}: {e}")); continue; } };
        if out.iter().any(|x| x.key == m.id) { errors.push(format!("{dir_name}: duplicate id {}", m.id)); continue; }
        out.push(Plugin { key: m.id.clone(), id: m.id, name: m.name, version: m.version, description: m.description, hooks: m.hooks,
            path: lib.display().to_string(),
            decl: Declared { api: 2, author: m.author, permissions: m.permissions, optional_permissions: m.optional_permissions,
                http_origins: m.http_origins, slots: m.slots, package: Some(d.clone()) },
            verified: true, fingerprint: hash, backend: None, busy: Arc::new(AtomicBool::new(false)),
            load_failed: false, failures: AtomicU32::new(0), tripped: AtomicBool::new(false) });
    }
    // libraries dropped straight into the folder (API 1, and API 2 with a sidecar)
    let mut libs: Vec<std::path::PathBuf> = paths.iter().filter(|p| is_lib(p)).cloned().collect();
    libs.sort();
    for p in &libs {
        let file = p.file_name().map(|f| f.to_string_lossy().to_string()).unwrap_or_default();
        // `<name>.manifest.json`, or a plain `manifest.json` when this is the only library in the folder
        let mut candidates = vec![p.with_extension("manifest.json")];
        if libs.len() == 1 { candidates.push(folder.join("manifest.json")); }
        let meta = match candidates.into_iter().find(|c| c.is_file()) {
            None => None,
            Some(c) => match std::fs::read_to_string(&c).map_err(|e| e.to_string())
                .and_then(|t| serde_json::from_str::<Value>(&t).map_err(|e| format!("bad manifest.json: {e}")))
                .and_then(|v| parse_manifest(&v)) {
                Ok(m) => Some(m),
                Err(e) => { errors.push(format!("{file}: {e}")); continue; }
            },
        };
        let verified = meta.is_some();
        let m = meta.unwrap_or_else(|| Meta {
            id: format!("file:{file}"), name: shown(&file, MAX_SHOWN), version: String::new(), hooks: vec![],
            description: "No manifest next to the file, so nothing is known about it until you switch it on. Only switch on plugins you trust.".into(),
            decl: Declared { api: 1, ..Declared::default() },
        });
        if out.iter().any(|x| x.key == m.id) { errors.push(format!("{file}: duplicate id {}", m.id)); continue; }
        let hash = match fingerprint(p) {
            Ok(h) => h,
            // no hash means no grant, so the plugin could never be enabled: report it instead of listing it
            Err(e) => { errors.push(format!("{file}: {e}")); continue; }
        };
        out.push(Plugin { key: m.id.clone(), id: m.id, name: m.name, version: m.version, description: m.description, hooks: m.hooks,
            path: p.display().to_string(), decl: m.decl, verified, fingerprint: hash, backend: None, busy: Arc::new(AtomicBool::new(false)),
            load_failed: false, failures: AtomicU32::new(0), tripped: AtomicBool::new(false) });
    }
    (out, errors)
}

/// The ids of libraries dropped straight into `folder`, as their sidecar manifests give them (`<name>.manifest.json`,
/// or `manifest.json` next to the only library), the way [`discover_in`] reads them. A package must not take one of
/// these ids: it would hide that plugin, and uninstalling it would forget that plugin's grant.
pub fn dropped_in_ids(folder: &std::path::Path) -> Vec<String> {
    let Ok(rd) = std::fs::read_dir(folder) else { return vec![] };
    let libs: Vec<std::path::PathBuf> = rd.flatten().map(|e| e.path()).filter(|p| is_lib(p)).collect();
    libs.iter().filter_map(|p| {
        let mut candidates = vec![p.with_extension("manifest.json")];
        if libs.len() == 1 { candidates.push(folder.join("manifest.json")); }
        let c = candidates.into_iter().find(|c| c.is_file())?;
        if std::fs::metadata(&c).ok()?.len() > MAX_MANIFEST_BYTES { return None; }
        let v: Value = serde_json::from_str(&std::fs::read_to_string(&c).ok()?).ok()?;
        v["id"].as_str().map(String::from)
    }).collect()
}

type FileStamp = (u64, std::time::SystemTime);
static HASHES: std::sync::OnceLock<Mutex<std::collections::HashMap<std::path::PathBuf, (FileStamp, String)>>> =
    std::sync::OnceLock::new();

fn hash_cache() -> &'static Mutex<std::collections::HashMap<std::path::PathBuf, (FileStamp, String)>> {
    HASHES.get_or_init(|| Mutex::new(std::collections::HashMap::new()))
}

/// SHA-256 of the library file. A file that cannot be read is an error (the plugin is unusable, never bindable).
/// For discovery the hash is reused while the file's size and modification time are unchanged, because Settings
/// rebuilds the Add-ons rows on every keystroke and must not re-read and re-hash every library; a load hashes again
/// without the cache ([`fresh_fingerprint`]).
fn fingerprint(p: &std::path::Path) -> Result<String, String> { fingerprint_with(p, true) }

/// Feed a file into `sha` in 64 KiB pieces, never more than `len + 1` bytes (so a file that grows is noticed without
/// reading all of it). Returns how many bytes were read.
fn hash_into(sha: &mut Sha256, path: &std::path::Path, len: u64) -> std::io::Result<u64> {
    use std::io::Read;
    let mut f = std::fs::File::open(path)?.take(len.saturating_add(1));
    let mut buf = vec![0u8; 64 * 1024];
    let mut n = 0u64;
    loop {
        let k = f.read(&mut buf)?;
        if k == 0 { return Ok(n); }
        sha.update(&buf[..k]);
        n += k as u64;
    }
}

/// Largest package folder that is hashed (all files together; the installer writes at most 32 MB). A larger folder
/// is reported and not listed, so it can never be switched on, and discovery never reads more than this per package.
pub const MAX_PACKAGE_BYTES: u64 = 64 * 1024 * 1024;

fn fingerprint_with(p: &std::path::Path, use_cache: bool) -> Result<String, String> {
    let meta = std::fs::metadata(p).map_err(|e| format!("cannot read the file: {e}"))?;
    let stamp = meta.modified().ok().map(|t| (meta.len(), t));
    if let (Some(stamp), true) = (stamp, use_cache) {
        let hit = hash_cache().lock().unwrap_or_else(|e| e.into_inner())
            .get(p).filter(|(s, _)| *s == stamp).map(|(_, h)| h.clone());
        if let Some(h) = hit { return Ok(h); }
    }
    let mut sha = Sha256::new();
    let n = hash_into(&mut sha, p, meta.len()).map_err(|e| format!("cannot read the file: {e}"))?;
    if n != meta.len() { return Err("the file changed while it was read".into()); }
    let hash: String = sha.finalize().iter().map(|x| format!("{x:02x}")).collect();
    if let Some(stamp) = stamp {
        hash_cache().lock().unwrap_or_else(|e| e.into_inner()).insert(p.to_path_buf(), (stamp, hash.clone()));
    }
    Ok(hash)
}

/// SHA-256 over a whole package folder: every file's relative path, length and bytes, in path order. Links are not
/// followed: a link, an entry that cannot be listed or read, or one that is neither a file nor a folder is an error
/// (the package is then not listed, so it can never be switched on). With `use_cache` the result is reused while no
/// file's size or time changed (discovery and the Add-ons rows); a load hashes again without it.
///
/// Every file in the folder counts, so anything else that writes there (Explorer's desktop.ini, a plugin writing next
/// to its own library) changes the hash and switches the plugin off until it is accepted again: plugins must keep
/// their data in `plugin-data\<id>\` (PLUGINS.md).
pub fn package_fingerprint(dir: &std::path::Path, use_cache: bool) -> Result<String, String> {
    fn walk(dir: &std::path::Path, rel: &str, depth: usize, out: &mut Vec<(String, std::path::PathBuf, u64, Option<std::time::SystemTime>)>) -> Result<(), String> {
        if depth > 16 || out.len() > 1000 { return Err("the package folder is too large or too deep".into()); }
        for e in std::fs::read_dir(dir).map_err(|e| format!("cannot read {}: {e}", dir.display()))? {
            let e = e.map_err(|e| format!("cannot list {}: {e}", dir.display()))?;
            let name = e.file_name().to_string_lossy().to_string();
            let r = if rel.is_empty() { name } else { format!("{rel}/{name}") };
            let md = std::fs::symlink_metadata(e.path()).map_err(|e| format!("{r}: {e}"))?;
            if md.file_type().is_symlink() { return Err(format!("{r}: a link inside a package")); }
            if md.is_dir() { walk(&e.path(), &r, depth + 1, out)?; }
            else if md.is_file() { out.push((r, e.path(), md.len(), md.modified().ok())); }
            else { return Err(format!("{r}: neither a file nor a folder")); }
            if out.len() > 1000 { return Err("the package folder is too large or too deep".into()); }
        }
        Ok(())
    }
    let mut files = vec![];
    walk(dir, "", 0, &mut files)?;
    files.sort_by(|a, b| a.0.cmp(&b.0));
    let stamp = format!("{:?}", files.iter().map(|f| (&f.0, f.2, f.3)).collect::<Vec<_>>());
    if use_cache && !files.iter().any(|f| f.3.is_none()) {
        let hit = hash_cache().lock().unwrap_or_else(|e| e.into_inner()).get(dir).filter(|(_, h)| h.starts_with(&stamp)).map(|(_, h)| h[stamp.len()..].to_string());
        if let Some(h) = hit { return Ok(h); }
    }
    let total = files.iter().fold(0u64, |a, f| a.saturating_add(f.2));
    if total > MAX_PACKAGE_BYTES {
        return Err(format!("the package folder holds more than {} MB, so it is not loaded", MAX_PACKAGE_BYTES >> 20));
    }
    let mut sha = Sha256::new();
    for (rel, path, len, _) in &files {
        sha.update((rel.len() as u64).to_le_bytes());
        sha.update(rel.as_bytes());
        sha.update(len.to_le_bytes());
        let n = hash_into(&mut sha, path, *len).map_err(|e| format!("{rel}: cannot read the file: {e}"))?;
        if n != *len { return Err(format!("{rel}: changed while it was read")); }
    }
    let hash: String = sha.finalize().iter().map(|x| format!("{x:02x}")).collect();
    if !files.iter().any(|f| f.3.is_none()) {
        hash_cache().lock().unwrap_or_else(|e| e.into_inner()).insert(dir.to_path_buf(), ((0, std::time::UNIX_EPOCH), format!("{stamp}{hash}")));
    }
    Ok(hash)
}

/// Keep the state (loaded library, failure count) of entries that are unchanged; take new or replaced files as found.
fn merge_discovered(mut old: Vec<Plugin>, new: Vec<Plugin>) -> Vec<Plugin> {
    // the manifest counts too: a package whose manifest changed (new permissions) is a new entry even when its
    // library is the same file. A plugin listed without a manifest (unverified) takes its name, version, hooks and
    // description from the library once it is loaded, while discovery lists it by file name again: for it only the
    // file counts, or every refresh would replace a loaded entry and lose its state (switched off for the session, a
    // call still running).
    let same = |o: &Plugin, n: &Plugin| o.key == n.key && o.path == n.path && o.fingerprint == n.fingerprint
        && o.decl == n.decl && o.verified == n.verified
        && (!n.verified || (o.version == n.version && o.name == n.name && o.hooks == n.hooks && o.description == n.description));
    new.into_iter().map(|n| match old.iter().position(|o| same(o, &n)) {
        Some(i) => old.swap_remove(i),
        None => n,
    }).collect()
}

/// Re-read the plugins folder (Settings → Add-ons) without loading anything new.
pub fn refresh(list: &mut Vec<Plugin>) -> Vec<String> {
    let (found, errors) = discover();
    *list = merge_discovered(std::mem::take(list), found);
    errors
}

/// The keys that are switched on and whose grant still covers the file (same path and SHA-256) and everything its
/// manifest asks for ([`decide`]). A replaced or changed plugin is off until the user says yes again.
pub fn granted_enabled(plugins: &[Plugin], enabled: &[String], grants: &[Grant]) -> Vec<String> {
    enabled.iter().filter(|k| plugins.iter().any(|p| &p.key == *k && p.consent(grants) == Consent::Granted)).cloned().collect()
}

/// Installed plugins that are not switched on: worth a one-time notice that plugins are off until enabled.
pub fn has_unenabled(plugins: &[Plugin], enabled: &[String]) -> bool {
    plugins.iter().any(|p| !enabled.contains(&p.key))
}

type LoadResult = Result<(Meta, Arc<dyn PluginBackend>), String>;
type Loader = Arc<dyn Fn(&std::path::Path) -> LoadResult + Send + Sync>;
/// How long loading a library (and reading its manifest) may take.
pub const LOAD_BUDGET_MS: u64 = 3000;

/// Load the plugins the user enabled and unload the ones switched off. Cheap when nothing changed.
/// Returns load errors (each reported once).
pub fn sync(plugins: &mut [Plugin], enabled: &[String]) -> Vec<String> {
    let loader: Loader = Arc::new(|p: &std::path::Path| unsafe { load(p) });
    sync_hashed(plugins, enabled, &loader, Duration::from_millis(LOAD_BUDGET_MS), &fresh_fingerprint)
}

/// The plugin's hash read again from disk, without the size-and-time cache: what is about to be loaded.
fn fresh_fingerprint(p: &Plugin) -> Result<String, String> {
    match &p.decl.package {
        Some(dir) => package_fingerprint(dir, false),
        None => fingerprint_with(std::path::Path::new(&p.path), false),
    }
}

/// Are the plugin's files on disk still the ones discovery hashed (read again, without the cache)? Checked before the
/// consent dialog, so the user never accepts a hash that no longer describes the files.
/// An error when the files cannot be read (or hashed) now.
pub fn unchanged_on_disk(p: &Plugin) -> Result<bool, String> {
    fresh_fingerprint(p).map(|h| h == p.fingerprint)
}

/// Drop every cached hash, so the next discovery reads every file again.
pub fn forget_cached_hashes() {
    hash_cache().lock().unwrap_or_else(|e| e.into_inner()).clear();
}

/// [`sync`] with a stub loader, the discovered hash standing in for the fresh one (tests).
#[cfg(test)]
fn sync_with(plugins: &mut [Plugin], enabled: &[String], loader: &Loader, budget: Duration) -> Vec<String> {
    sync_hashed(plugins, enabled, loader, budget, &|p: &Plugin| Ok(p.fingerprint.clone()))
}

fn sync_hashed(plugins: &mut [Plugin], enabled: &[String], loader: &Loader, budget: Duration,
    rehash: &dyn Fn(&Plugin) -> Result<String, String>) -> Vec<String> {
    let mut errors = vec![];
    for i in 0..plugins.len() {
        if !enabled.contains(&plugins[i].key) {
            // switching off forgets an earlier load failure, so on again tries again; the backend is parked,
            // not dropped, because dropping it would unload the library under any thread still inside it
            plugins[i].park();
            plugins[i].load_failed = false;
            continue;
        }
        // A timed-out call may still be running inside the library: loading the file while it is busy would
        // enter the plugin again (and on Windows could not reload a replaced file anyway).
        if plugins[i].busy.load(Ordering::SeqCst) { continue; }
        if plugins[i].backend.is_some() || plugins[i].load_failed || plugins[i].is_tripped() { continue; }
        // API 2 plugins are installed and consented to (B2), but the API 2 runtime (init, handle, the host table)
        // comes with B3/B4: until then nothing of theirs is loaded.
        if plugins[i].decl.api >= 2 { continue; }
        let path = plugins[i].path.clone();
        // the grant was checked against the hash from the last discovery, which may come from the cache: hash the
        // files again right before loading, and load only while they are still the ones the user accepted
        let hash = match rehash(&plugins[i]) {
            Ok(h) if h == plugins[i].fingerprint => h,
            Ok(_) => {
                plugins[i].load_failed = true;
                // the cached hashes no longer describe the files: the next discovery reads them all again
                forget_cached_hashes();
                errors.push(format!("{path}: this plugin changed since the plugins folder was read; open Settings > Add-ons and switch it on again"));
                continue;
            }
            Err(e) => { plugins[i].load_failed = true; errors.push(format!("{path}: {e}")); continue; }
        };
        if let Err(e) = load_allowed(&path, &hash) {
            plugins[i].load_failed = true;
            errors.push(e);
            continue;
        }
        // Known gap: the file can still be swapped between this hash and the loader opening it (Windows lets a file
        // that is not open be replaced). Holding it open with a share mode that forbids writes is not done here.
        // Library::new and manifest() run foreign code: never on the UI thread without a limit
        let (tx, rx) = mpsc::channel();
        let (l, pp) = (loader.clone(), std::path::PathBuf::from(&path));
        let started = std::thread::Builder::new().name("barbican-plugin-load".into()).spawn(move || {
            let r = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| l(&pp))).unwrap_or_else(|_| Err("plugin panicked while loading".to_string()));
            let _ = tx.send(r); // if nobody waits any more, the library stays loaded anyway (see LIBRARIES)
        });
        let res = match started {
            Err(e) => Err(format!("could not start loader: {e}")),
            Ok(_) => match rx.recv_timeout(budget) {
                Ok(r) => r,
                Err(mpsc::RecvTimeoutError::Timeout) => Err(format!("did not finish loading within {} ms", budget.as_millis())),
                Err(mpsc::RecvTimeoutError::Disconnected) => Err("loader died".to_string()),
            },
        };
        let (m, b) = match res {
            Ok(x) => x,
            Err(e) => { plugins[i].load_failed = true; errors.push(format!("{path}: {e}")); continue; }
        };
        if m.decl.api >= 2 && plugins[i].decl.package.is_none() {
            plugins[i].load_failed = true;
            errors.push(format!("{path}: an API 2 plugin must be installed as a package (Settings > Add-ons > Install plugin)"));
        } else if plugins[i].verified && m.id != plugins[i].id {
            plugins[i].load_failed = true;
            errors.push(format!("{path}: manifest.json says id {} but the library says {}", plugins[i].id, m.id));
        } else if plugins.iter().enumerate().any(|(j, o)| j != i && o.id == m.id) {
            plugins[i].load_failed = true;
            errors.push(format!("{path}: another plugin already uses the id {}", m.id));
        } else {
            let p = &mut plugins[i];
            if !p.verified { (p.id, p.name, p.version, p.description, p.hooks) = (m.id, m.name, m.version, m.description, m.hooks); }
            p.backend = Some(b);
        }
    }
    errors
}

unsafe fn load(p: &std::path::Path) -> LoadResult {
    let lib = libloading::Library::new(p).map_err(|e| e.to_string())?;
    // The library's DllMain has already run and may have started threads: keep the mapping for the whole
    // process even if the symbol lookup below or a later check rejects the plugin.
    let lib = Arc::new(lib);
    LIBRARIES.lock().unwrap_or_else(|e| e.into_inner()).push(lib.clone());
    let manifest: libloading::Symbol<ManifestFn> = lib.get(b"barbican_plugin_manifest\0").map_err(|_| "missing barbican_plugin_manifest")?;
    let call: libloading::Symbol<CallFn> = lib.get(b"barbican_plugin_call\0").map_err(|_| "missing barbican_plugin_call")?;
    let free: libloading::Symbol<FreeFn> = lib.get(b"barbican_plugin_free\0").map_err(|_| "missing barbican_plugin_free")?;
    let m = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| manifest())).map_err(|_| "manifest call panicked")?;
    if m.is_null() { return Err("empty manifest".into()); }
    let v: Value = serde_json::from_str(&CStr::from_ptr(m).to_string_lossy()).map_err(|e| format!("bad manifest: {e}"))?;
    let meta = parse_manifest(&v)?;
    let (call, free) = (*call, *free);
    Ok((meta, Arc::new(Native { call, free, _lib: lib })))
}

/// Is this plugin id loaded, enabled and not switched off? (The built-in spam panel steps aside for `spam-score`.)
pub fn is_active(plugins: &[Plugin], enabled: &[String], id: &str) -> bool {
    plugins.iter().any(|p| p.id == id && p.active(enabled))
}

/// Rows from every enabled plugin for the open message.
pub fn email_details(plugins: &[Plugin], enabled: &[String], email: &Value) -> Vec<(String, String, String)> {
    let mut rows = vec![];
    let b = budget("email.details");
    for p in plugins.iter().filter(|p| p.active(enabled) && p.handles("email.details")) {
        if let Ok(Some(v)) = p.call("email.details", email, b) {
            for r in v["rows"].as_array().cloned().unwrap_or_default() {
                rows.push((r["label"].as_str().unwrap_or("").to_string(), r["value"].as_str().unwrap_or("").to_string(), r["tone"].as_str().unwrap_or("neutral").to_string()));
            }
        }
    }
    rows
}

/// First warning any enabled plugin raises before sending. Fails CLOSED: a plugin that handles `compose.check` and
/// panics, errors or times out holds the send with a warning (shown once, like the attachment reminder).
pub fn compose_check(plugins: &[Plugin], enabled: &[String], draft: &Value) -> Option<String> {
    compose_check_with(plugins, enabled, draft, budget("compose.check"))
}

pub fn compose_check_with(plugins: &[Plugin], enabled: &[String], draft: &Value, budget: Duration) -> Option<String> {
    let mut first = None;
    for p in plugins.iter().filter(|p| p.active(enabled) && p.handles("compose.check")) {
        let w = match p.call("compose.check", draft, budget) {
            Ok(v) => v.and_then(|v| v["warning"].as_str().filter(|w| !w.is_empty()).map(String::from)),
            Err(e) => Some(format!("The {} plugin could not check this message ({}).", p.name, shown(&e, MAX_SHOWN))),
        };
        // keep calling the rest so every failing plugin is counted
        if first.is_none() { first = w; }
    }
    first
}

pub fn describe(p: &Plugin) -> Value {
    let mut note = String::new();
    if p.is_tripped() { note += " Switched off for this session after repeated failures."; }
    let (perms, _) = p.requested();
    let titles: Vec<&str> = perms.iter().map(|x| permission_text(x).map_or(x.as_str(), |t| t.title)).collect();
    if !titles.is_empty() { note += &format!(" Asks to: {}.", titles.join(", ").to_lowercase()); }
    if p.decl.api >= 2 { note += " Uses plugin API 2: switching it on records your consent, but this version does not run API 2 plugins yet."; }
    json!({"id": p.key, "name": p.name, "version": p.version, "author": p.decl.author, "description": format!("{}{note}", p.description).trim(),
        "hooks": p.hooks, "path": p.path, "packaged": p.decl.package.is_some(), "permissions": perms})
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::AtomicUsize;

    enum Mode { Reply(Value), Nothing, Fail, Panic, Sleep(u64) }
    struct Fake { mode: Mode, calls: AtomicUsize }
    impl PluginBackend for Fake {
        fn call(&self, _: &str, _: &Value) -> Result<Option<Value>, String> {
            self.calls.fetch_add(1, Ordering::SeqCst);
            match &self.mode {
                Mode::Reply(v) => Ok(Some(v.clone())),
                Mode::Nothing => Ok(None),
                Mode::Fail => Err("boom".into()),
                Mode::Panic => panic!("fake plugin panic"),
                Mode::Sleep(ms) => { std::thread::sleep(Duration::from_millis(*ms)); Ok(Some(json!({}))) }
            }
        }
    }

    fn fake(name: &str, mode: Mode) -> (Plugin, Arc<Fake>) {
        let f = Arc::new(Fake { mode, calls: AtomicUsize::new(0) });
        let p = Plugin { key: name.into(), id: name.into(), name: name.into(), version: "1".into(), description: String::new(),
            hooks: vec!["compose.check".into(), "email.details".into()], path: String::new(), decl: Declared::default(), verified: true,
            fingerprint: String::new(), busy: Arc::new(AtomicBool::new(false)), backend: Some(f.clone()), load_failed: false, failures: AtomicU32::new(0), tripped: AtomicBool::new(false) };
        (p, f)
    }
    fn on(p: &Plugin) -> Vec<String> { vec![p.key.clone()] }
    const MS: Duration = Duration::from_millis(500);

    #[test]
    fn budgets_come_from_the_catalogue() {
        assert_eq!(budget("email.details"), Duration::from_millis(5000));
        assert_eq!(budget("onEmailOpen"), Duration::from_millis(5000));
        assert_eq!(budget("settings"), Duration::from_millis(2000));
        assert_eq!(budget("compose.check"), Duration::from_millis(MAX_BUDGET_MS as u64)); // catalogue 600000, capped
    }

    #[test]
    fn handles_either_spelling() {
        let (mut p, _) = fake("a", Mode::Nothing);
        p.hooks = vec!["onBeforeEmailSend".into()];
        assert!(p.handles("compose.check") && p.handles("onBeforeEmailSend") && !p.handles("email.details"));
    }

    #[test]
    fn healthy_plugin_warns_or_passes() {
        let (p, _) = fake("warns", Mode::Reply(json!({"warning": "Looks odd"})));
        assert_eq!(compose_check_with(&[p], &["warns".into()], &json!({}), MS).as_deref(), Some("Looks odd"));
        let (p, _) = fake("quiet", Mode::Nothing);
        assert_eq!(compose_check_with(std::slice::from_ref(&p), &on(&p), &json!({}), MS), None);
        let (p, _) = fake("empty", Mode::Reply(json!({"warning": ""})));
        assert_eq!(compose_check_with(std::slice::from_ref(&p), &on(&p), &json!({}), MS), None);
    }

    #[test]
    fn compose_check_fails_closed() {
        for (name, mode) in [("errs", Mode::Fail), ("panics", Mode::Panic), ("hangs", Mode::Sleep(400))] {
            let (p, _) = fake(name, mode);
            let w = compose_check_with(std::slice::from_ref(&p), &on(&p), &json!({}), Duration::from_millis(60));
            assert!(w.as_deref().is_some_and(|w| w.contains(name)), "{name}: {w:?}");
        }
    }

    #[test]
    fn timeout_is_abandoned_and_counted() {
        let (p, _) = fake("slow", Mode::Sleep(400));
        let t = std::time::Instant::now();
        let r = p.call("email.details", &json!({}), Duration::from_millis(50));
        assert!(t.elapsed() < Duration::from_millis(300));
        assert!(r.unwrap_err().contains("timed out"));
        assert_eq!(p.failures.load(Ordering::SeqCst), 1);
    }

    #[test]
    fn panic_is_caught_and_counted() {
        let (p, _) = fake("pan", Mode::Panic);
        assert!(p.call("x", &json!({}), MS).unwrap_err().contains("panicked"));
        assert_eq!(p.failures.load(Ordering::SeqCst), 1);
    }

    #[test]
    fn three_failures_in_a_row_switch_it_off() {
        let (p, f) = fake("flaky-plugin-name", Mode::Fail);
        let en = on(&p);
        for _ in 0..5 { compose_check_with(std::slice::from_ref(&p), &en, &json!({}), MS); }
        assert!(p.is_tripped());
        assert_eq!(f.calls.load(Ordering::SeqCst), MAX_FAILURES as usize, "no calls once switched off");
        assert!(drain_notices().iter().any(|n| n.contains("flaky-plugin-name") && n.contains("switched off")));
        // off for the session: the send is no longer held by it
        assert_eq!(compose_check_with(std::slice::from_ref(&p), &en, &json!({}), MS), None);
        assert!(describe(&p)["description"].as_str().unwrap().contains("Switched off"));
    }

    #[test]
    fn a_success_resets_the_count() {
        let (p, _) = fake("ok", Mode::Nothing);
        p.failures.store(MAX_FAILURES - 1, Ordering::SeqCst);
        assert!(p.call("x", &json!({}), MS).is_ok());
        assert_eq!(p.failures.load(Ordering::SeqCst), 0);
        assert!(!p.is_tripped());
    }

    #[test]
    fn not_enabled_means_not_called() {
        let (p, f) = fake("off", Mode::Reply(json!({"warning": "x", "rows": [{"label": "a", "value": "b"}]})));
        assert_eq!(compose_check_with(std::slice::from_ref(&p), &[], &json!({}), MS), None);
        assert!(email_details(std::slice::from_ref(&p), &[], &json!({})).is_empty());
        assert!(!is_active(std::slice::from_ref(&p), &[], "off"));
        assert_eq!(f.calls.load(Ordering::SeqCst), 0);
        let rows = email_details(std::slice::from_ref(&p), &on(&p), &json!({}));
        assert_eq!(rows, vec![("a".to_string(), "b".to_string(), "neutral".to_string())]);
    }

    /// Tests that plant entries in the shared hash cache, or make the code clear it, take turns.
    static CACHE_TESTS: Mutex<()> = Mutex::new(());
    fn cache_test() -> std::sync::MutexGuard<'static, ()> { CACHE_TESTS.lock().unwrap_or_else(|e| e.into_inner()) }

    fn temp_folder(tag: &str) -> std::path::PathBuf {
        let d = std::env::temp_dir().join(format!("barbican-plugin-test-{tag}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&d);
        std::fs::create_dir_all(&d).unwrap();
        d
    }
    fn lib_name(stem: &str) -> String { format!("{stem}.{}", std::env::consts::DLL_EXTENSION) }

    #[test]
    fn discovery_reads_the_sidecar_and_loads_nothing() {
        let d = temp_folder("sidecar");
        // not a real library: loading it would fail, so any error proves a load was attempted
        std::fs::write(d.join(lib_name("one")), b"not a library").unwrap();
        std::fs::write(d.join("one.manifest.json"), br#"{"id":"one","name":"One","hooks":["compose.check"]}"#).unwrap();
        std::fs::write(d.join(lib_name("two")), b"not a library").unwrap();
        let (mut list, errors) = discover_in(&d);
        assert!(errors.is_empty(), "{errors:?}");
        assert_eq!(list.len(), 2);
        let one = list.iter().find(|p| p.key == "one").unwrap();
        assert!(one.handles("compose.check") && one.backend.is_none());
        let two = list.iter().find(|p| p.key.starts_with("file:")).unwrap();
        assert!(two.hooks.is_empty() && !two.verified);
        // default off: nothing enabled, nothing loaded, no errors
        assert!(sync(&mut list, &[]).is_empty());
        // enabling is what triggers the load attempt (which fails for the dummy file)
        let errs = sync(&mut list, &["one".to_string()]);
        assert_eq!(errs.len(), 1, "{errs:?}");
        assert!(sync(&mut list, &["one".to_string()]).is_empty(), "a failed load is reported once");
        let _ = std::fs::remove_dir_all(&d);
    }

    #[test]
    fn single_library_may_use_plain_manifest_json() {
        let d = temp_folder("plain");
        std::fs::write(d.join(lib_name("only")), b"x").unwrap();
        std::fs::write(d.join("manifest.json"), br#"{"id":"only-id","hooks":[]}"#).unwrap();
        let (list, errors) = discover_in(&d);
        assert!(errors.is_empty() && list.len() == 1 && list[0].key == "only-id");
        std::fs::write(d.join(lib_name("second")), b"x").unwrap();
        let (list, _) = discover_in(&d);
        assert!(list.iter().all(|p| p.key.starts_with("file:")), "manifest.json is ambiguous with two libraries");
        let _ = std::fs::remove_dir_all(&d);
    }

    #[test]
    fn loads_spam_score_plugin() {
        let dll = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("plugins/spam-score/target/release/barbican_spam_score.dll");
        if !dll.exists() { eprintln!("example plugin not built; skipping"); return; }
        let (meta, backend) = unsafe { super::load(&dll) }.expect("load");
        assert_eq!(meta.id, "spam-score");
        let p = Plugin { key: meta.id.clone(), id: meta.id, name: meta.name, version: meta.version, description: meta.description, hooks: meta.hooks,
            path: String::new(), decl: meta.decl, verified: true, fingerprint: String::new(), busy: Arc::new(AtomicBool::new(false)), backend: Some(backend), load_failed: false, failures: AtomicU32::new(0), tripped: AtomicBool::new(false) };
        assert!(p.handles("email.details"));
        let email = json!({"headers": [{"name": "X-Spam-Status", "value": "Yes, score=7.5 required=5.0"}]});
        let rows = email_details(std::slice::from_ref(&p), &on(&p), &email);
        assert_eq!(rows.len(), 1);
        assert_eq!(rows[0].2, "danger");
        assert!(rows[0].1.contains("7.50 / 5.00"), "{}", rows[0].1);
    }

    struct Recorder(Mutex<Vec<String>>);
    impl PluginBackend for Recorder {
        fn call(&self, hook: &str, _: &Value) -> Result<Option<Value>, String> { self.0.lock().unwrap().push(hook.to_string()); Ok(None) }
    }

    fn bare(key: &str, path: &str, fp: &str) -> Plugin {
        Plugin { key: key.into(), id: key.into(), name: key.into(), version: String::new(), description: String::new(), hooks: vec![],
            path: path.into(), decl: Declared::default(), verified: true, fingerprint: fp.into(), backend: None, busy: Arc::new(AtomicBool::new(false)),
            load_failed: false, failures: AtomicU32::new(0), tripped: AtomicBool::new(false) }
    }

    fn stub_meta(id: &str) -> Meta { Meta { id: id.into(), name: id.into(), version: String::new(), description: String::new(), hooks: vec![], decl: Declared::default() } }
    fn stub_loader(f: impl Fn(&std::path::Path) -> LoadResult + Send + Sync + 'static) -> Loader { Arc::new(f) }

    #[test]
    fn plugin_is_called_with_the_spelling_its_manifest_declared() {
        let rec = Arc::new(Recorder(Mutex::new(vec![])));
        let mut p = bare("rec", "", "");
        p.backend = Some(rec.clone());
        p.hooks = vec!["onBeforeEmailSend".into(), "onEmailOpen".into()];
        p.call("compose.check", &json!({}), MS).unwrap();
        p.call("email.details", &json!({}), MS).unwrap();
        p.call("settings", &json!({}), MS).unwrap();
        assert_eq!(*rec.0.lock().unwrap(), ["onBeforeEmailSend", "onEmailOpen", "settings"]);
        p.hooks = vec!["compose.check".into()];
        p.call("onBeforeEmailSend", &json!({}), MS).unwrap();
        assert_eq!(rec.0.lock().unwrap().last().unwrap(), "compose.check");
    }

    #[test]
    fn no_second_call_while_an_abandoned_one_runs() {
        let (p, f) = fake("busy", Mode::Sleep(300));
        assert!(p.call("x", &json!({}), Duration::from_millis(30)).unwrap_err().contains("timed out"));
        let t = std::time::Instant::now();
        let e = p.call("x", &json!({}), MS).unwrap_err();
        assert!(e.contains("not returned") && t.elapsed() < Duration::from_millis(100), "{e}");
        assert_eq!(f.calls.load(Ordering::SeqCst), 1, "the library was entered once");
        assert_eq!(p.failures.load(Ordering::SeqCst), 2);
        std::thread::sleep(Duration::from_millis(400));
        assert!(p.call("x", &json!({}), MS).is_ok(), "free again once the old call returned");
    }

    #[test]
    fn a_hung_load_is_abandoned_and_can_be_retried_after_switching_off() {
        let mut list = vec![bare("slow", "x", "")];
        let hang = stub_loader(|_| { std::thread::sleep(Duration::from_millis(400)); Err("late".into()) });
        let en = vec!["slow".to_string()];
        let t = std::time::Instant::now();
        let errs = sync_with(&mut list, &en, &hang, Duration::from_millis(40));
        assert!(t.elapsed() < Duration::from_millis(300));
        assert!(errs.len() == 1 && errs[0].contains("did not finish loading"), "{errs:?}");
        assert!(sync_with(&mut list, &en, &hang, Duration::from_millis(40)).is_empty(), "reported once");
        // switch off, then on again: tries again
        sync_with(&mut list, &[], &hang, Duration::from_millis(40));
        assert_eq!(sync_with(&mut list, &en, &hang, Duration::from_millis(40)).len(), 1);
    }

    #[test]
    fn a_panicking_loader_is_a_load_failure() {
        let mut list = vec![bare("p", "x", "")];
        let l = stub_loader(|_| panic!("manifest exploded"));
        let errs = sync_with(&mut list, &["p".to_string()], &l, Duration::from_millis(500));
        assert!(errs[0].contains("panicked"), "{errs:?}");
    }

    #[test]
    fn duplicate_ids_never_load() {
        let mut a = bare("one", "a", "");
        a.backend = Some(Arc::new(Recorder(Mutex::new(vec![]))));
        let mut b = bare("file:b.dll", "b", "");
        b.verified = false;
        let mut list = vec![a, b];
        let l = stub_loader(|_| Ok((stub_meta("one"), Arc::new(Recorder(Mutex::new(vec![]))) as Arc<dyn PluginBackend>)));
        let errs = sync_with(&mut list, &["one".to_string(), "file:b.dll".to_string()], &l, MS);
        assert!(errs.len() == 1 && errs[0].contains("already uses the id one"), "{errs:?}");
        assert!(list[1].backend.is_none() && list[1].load_failed);
    }

    #[test]
    fn sidecar_id_must_match_the_library() {
        let mut list = vec![bare("claimed", "x", "")];
        let l = stub_loader(|_| Ok((stub_meta("other"), Arc::new(Recorder(Mutex::new(vec![]))) as Arc<dyn PluginBackend>)));
        let errs = sync_with(&mut list, &["claimed".to_string()], &l, MS);
        assert!(errs[0].contains("library says other") && list[0].backend.is_none());
    }

    #[test]
    fn manifest_ids_are_checked() {
        for bad in [json!({"id": "file:evil"}), json!({"id": "a\u{0}b"}), json!({"id": "a\nb"}), json!({"id": ""}), json!({})] {
            assert!(parse_manifest(&bad).is_err(), "{bad}");
        }
        assert!(parse_manifest(&json!({"id": "fine-id.1"})).is_ok());
    }

    fn spec_manifest() -> Value {
        // the example of PLUGINS.md "Manifest"
        json!({
            "api": 2, "id": "dev.example.spam-score", "name": "Spam Score", "version": "2.0.0", "author": "Example",
            "description": "Shows the server's spam verdict under the sender.", "homepage": "https://example.dev/spam-score",
            "minAppVersion": "0.0.1",
            "library": { "windows-x86_64": "spam_score.dll", "linux-x86_64": "libspam_score.so" },
            "permissions": ["email:read", "ui:email-details"], "optionalPermissions": ["http:fetch"],
            "httpOrigins": ["https://api.example.com", "https://*.example.org"],
            "hooks": ["onEmailOpen"], "slots": ["email-details-section"],
            "settings": {
                "threshold": { "type": "number", "label": "Warn above", "default": 5, "min": 0, "max": 20 },
                "mode": { "type": "select", "label": "Show", "default": "warn", "options": ["always", "warn"] },
                "apiKey": { "type": "secret", "label": "API key", "default": "" }
            },
            "locales": { "en": { "title": "Spam score" } },
            "entrypoint": "index.js", "tier": "privileged", "somethingNew": 1
        })
    }
    fn with(mut v: Value, key: &str, val: Value) -> Value { v[key] = val; v }
    fn refused(v: Value, want: &str) {
        match validate_manifest(&v) {
            Ok(_) => panic!("accepted, wanted an error with {want:?}: {v}"),
            Err(e) => assert!(e.contains(want), "wanted {want:?}, got {e:?}"),
        }
    }

    #[test]
    fn the_spec_example_manifest_is_valid() {
        let m = validate_manifest(&spec_manifest()).unwrap();
        assert_eq!(m.id, "dev.example.spam-score");
        assert_eq!(m.permissions, ["email:read", "ui:email-details"]);
        assert_eq!(m.optional_permissions, ["http:fetch"]);
        assert_eq!(m.http_origins, ["https://api.example.com", "https://*.example.org"]);
        assert_eq!(m.libraries.len(), 2);
        assert_eq!(m.settings.as_object().unwrap().len(), 3);
        // the sidecar path takes API 2 too
        let meta = parse_manifest(&spec_manifest()).unwrap();
        assert_eq!(meta.decl.api, 2);
        assert_eq!(meta.decl.permissions, ["email:read", "ui:email-details"]);
    }

    #[test]
    fn manifest_ids_versions_and_api_are_checked() {
        let long = "a".repeat(65);
        for bad in ["ab", "1abc", "Abc", "a b", "a/b", "file:x", "abc.", "con", "nul.plugin", long.as_str(), ""] {
            refused(with(spec_manifest(), "id", json!(bad)), "\"id\"");
        }
        let max = "a".repeat(64);
        for good in ["abc", "a-b_c.d9", max.as_str()] { assert!(valid_id(good), "{good}"); }
        for bad in ["1.0", "1.0.0.0", "01.0.0", "v1.0.0", "1.0.0-", ""] {
            refused(with(spec_manifest(), "version", json!(bad)), "\"version\"");
        }
        for good in ["0.0.1", "1.2.3-beta.1", "1.2.3+build.5", "10.20.30-rc.1+x"] { assert!(valid_semver(good), "{good}"); }
        refused(with(spec_manifest(), "api", json!(3)), "needs plugin API 3");
        refused(with(spec_manifest(), "api", json!(1)), "\"api\" must be 2");
        refused(with(spec_manifest(), "api", json!("2")), "\"api\" must be 2");
        refused(with(spec_manifest(), "minAppVersion", json!("999.0.0")), "needs Barbican 999.0.0");
        refused(with(spec_manifest(), "name", json!("")), "\"name\"");
        refused(with(spec_manifest(), "name", json!("a\nb")), "\"name\"");
        refused(with(spec_manifest(), "homepage", json!("http://example.dev")), "homepage");
        assert!(validate_manifest(&json!([])).is_err());
    }

    #[test]
    fn manifest_libraries_are_plain_files() {
        refused(with(spec_manifest(), "library", json!({})), "\"library\"");
        refused(with(spec_manifest(), "library", json!("x.dll")), "\"library\"");
        for bad in ["../x.dll", "sub/x.dll", "C:x.dll", ".x.dll", "x.exe", "con.dll"] {
            refused(with(spec_manifest(), "library", json!({"windows-x86_64": bad})), "library \"windows-x86_64\"");
        }
        let m = validate_manifest(&with(spec_manifest(), "library", json!({ platform_key(): format!("x.{}", std::env::consts::DLL_EXTENSION) }))).unwrap();
        assert!(m.library_here().is_some());
    }

    #[test]
    fn manifest_permissions_are_checked() {
        refused(with(spec_manifest(), "permissions", json!(["email:read", "made:up"])), "unknown permission made:up");
        for dropped in DROPPED_PERMISSIONS {
            refused(with(spec_manifest(), "permissions", json!(["email:read", "ui:email-details", dropped])), "not available in Barbican");
        }
        refused(with(spec_manifest(), "permissions", json!("email:read ui:email-details")), "list of strings");
        // unknown optional permissions are left out, not refused (one package for several versions)
        let m = validate_manifest(&with(spec_manifest(), "optionalPermissions", json!(["http:fetch", "future:thing", "email:read"]))).unwrap();
        assert_eq!(m.optional_permissions, ["http:fetch"], "unknown and duplicate optional permissions are dropped");
        assert_eq!(PERMISSIONS.len() + DROPPED_PERMISSIONS.len(), 72, "Bulwark's ALL_PERMISSIONS");
        let mut names: Vec<&str> = PERMISSIONS.iter().map(|p| p.name).collect();
        names.sort();
        names.dedup();
        assert_eq!(names.len(), PERMISSIONS.len(), "no permission listed twice");
    }

    #[test]
    fn manifest_hooks_and_slots_are_checked() {
        refused(with(spec_manifest(), "hooks", json!(["onNoSuchHook"])), "unknown hook onNoSuchHook");
        refused(with(spec_manifest(), "hooks", json!(["onViewChange"])), "the hook onViewChange is not available");
        refused(with(spec_manifest(), "hooks", json!(["onBeforeEmailSend"])), "the hook onBeforeEmailSend needs the permission email:send");
        // old aliases are accepted and checked like the name they stand for
        assert!(validate_manifest(&with(spec_manifest(), "hooks", json!(["email.details"]))).is_ok());
        refused(with(spec_manifest(), "hooks", json!(["compose.check"])), "needs the permission email:send");
        // a permission in optionalPermissions counts; the implicit ones always do
        let v = with(with(spec_manifest(), "hooks", json!(["onBeforeEmailSend"])), "optionalPermissions", json!(["email:send", "http:fetch"]));
        assert!(validate_manifest(&v).is_ok());
        // also_permission: the reminder hook takes tasks:read instead of calendar:read
        let v = with(with(spec_manifest(), "hooks", json!(["onCalendarAlert"])), "permissions", json!(["tasks:read", "ui:email-details"]));
        assert!(validate_manifest(&v).is_ok());
        refused(with(spec_manifest(), "slots", json!(["no-such-slot"])), "unknown slot no-such-slot");
        refused(with(spec_manifest(), "slots", json!(["reader-above-quote"])), "the slot reader-above-quote is not available");
        refused(with(spec_manifest(), "slots", json!(["composer-above-body"])), "needs the permission ui:composer-toolbar or ui:composer-sidebar");
        // any one of a slot's permissions is enough
        let v = with(with(spec_manifest(), "slots", json!(["composer-above-body"])), "permissions", json!(["email:read", "ui:composer-sidebar"]));
        assert!(validate_manifest(&v).is_ok());
        assert!(validate_manifest(&with(spec_manifest(), "slots", json!(["plugin-dialog"]))).is_ok(), "ui:observe is implicit");
    }

    #[test]
    fn http_origins_are_checked() {
        for good in ["https://api.example.com", "https://*.example.org", "https://api.example.com:8443", "HTTPS://API.Example.COM", "https://nas"] {
            assert!(check_origin(good).is_ok(), "{good}: {:?}", check_origin(good));
        }
        assert_eq!(check_origin("HTTPS://API.Example.COM").unwrap(), "https://api.example.com");
        for (bad, why) in [
            ("http://api.example.com", "plain http"), ("ftp://x.example", "https://"), ("https://example.com/path", "no path"),
            ("https://example.com/", "no path"), ("https://example.com?q", "no path"), ("https://user@example.com", "no path"),
            ("https://*", "bare *"), ("https://*.com", "two labels"), ("https://a.*.example.com", "first label"),
            ("https://127.0.0.1", "IP addresses"), ("https://[::1]", "IP addresses"), ("https://127.1", "IP addresses"),
            ("https://2130706433", "IP addresses"), ("https://0x7f000001", "IP addresses"), ("https://0177.0.0.1", "IP addresses"),
            ("https://example.com:0", "port"), ("https://example.com:99999", "port"), ("https://example.com:", "port"),
            ("https://exa_mple.com", "host name"), ("https://-a.example.com", "host name"), ("https://b\u{fc}cher.example", "ASCII"),
            ("https://example.com.", "host name"),
        ] {
            let e = check_origin(bad).unwrap_err();
            assert!(e.contains(why), "{bad}: wanted {why:?}, got {e:?}");
        }
        refused(with(spec_manifest(), "httpOrigins", json!(["http://x.example.com"])), "plain http");
        let no_fetch = with(with(spec_manifest(), "optionalPermissions", json!([])), "httpOrigins", json!(["https://a.example.com"]));
        refused(no_fetch, "\"httpOrigins\" needs the permission http:fetch");
    }

    #[test]
    fn manifest_settings_are_checked() {
        let bad = [
            (json!({"x": {"type": "color", "label": "X"}}), "type must be"),
            (json!({"x": {"type": "boolean"}}), "needs a label"),
            (json!({"x": {"type": "boolean", "label": "X", "default": "yes"}}), "true or false"),
            (json!({"x": {"type": "number", "label": "X", "default": 30, "max": 20}}), "outside min..max"),
            (json!({"x": {"type": "number", "label": "X", "min": 5, "max": 1}}), "min is above max"),
            (json!({"x": {"type": "select", "label": "X"}}), "list of text options"),
            (json!({"x": {"type": "select", "label": "X", "options": ["a"], "default": "b"}}), "one of the options"),
            (json!({"x": {"type": "secret", "label": "X", "default": "hunter2"}}), "no default"),
            (json!({"bad key": {"type": "string", "label": "X"}}), "the key must be"),
            (json!("x"), "\"settings\" must be an object"),
        ];
        for (settings, want) in bad { refused(with(spec_manifest(), "settings", settings), want); }
    }

    #[test]
    fn ip_addresses_are_classified() {
        use std::net::IpAddr;
        for private in ["10.0.0.1", "127.0.0.1", "169.254.169.254", "100.64.0.1", "172.16.5.4", "192.168.1.1", "0.0.0.0",
            "224.0.0.1", "255.255.255.255", "::1", "::", "fe80::1", "fc00::1", "fd00:ec2::254", "::ffff:127.0.0.1",
            "64:ff9b::a00:1", "2002:c0a8:101::1", "2001:db8::1", "::127.0.0.1"] {
            assert!(!ip_is_public(private.parse::<IpAddr>().unwrap()), "{private}");
        }
        for public in ["1.1.1.1", "8.8.8.8", "93.184.216.34", "2606:4700:4700::1111", "::ffff:8.8.8.8", "64:ff9b::808:808"] {
            assert!(ip_is_public(public.parse::<IpAddr>().unwrap()), "{public}");
        }
        for name in ["localhost", "a.localhost", "nas", "printer.lan", "box.local", "x.home.arpa", "git.internal", "nas.LAN."] {
            assert!(is_private_name(name), "{name}");
        }
        for name in ["example.com", "api.example.org", "lan.example.com"] { assert!(!is_private_name(name), "{name}"); }
        assert!(resolves_private("nas.lan", Duration::from_millis(1)), "a private name needs no lookup");
    }

    fn v2_plugin(perms: &[&str], origins: &[&str], fp: &str) -> Plugin {
        let mut p = bare("dev.example.demo", "C:/p/dev.example.demo/demo.dll", fp);
        p.name = "Demo".into();
        p.version = "1.1.0".into();
        p.decl = Declared { api: 2, author: "Example".into(), permissions: perms.iter().map(|s| s.to_string()).collect(),
            http_origins: origins.iter().map(|s| s.to_string()).collect(), package: Some("C:/p/dev.example.demo".into()), ..Declared::default() };
        p
    }
    fn strings(v: &[&str]) -> Vec<String> { v.iter().map(|s| s.to_string()).collect() }

    #[test]
    fn consent_for_a_new_install_lists_everything() {
        let p = v2_plugin(&["ui:email-details", "email:read", "http:fetch", "ui:observe"], &["https://api.example.com", "https://nas.lan", "https://*.example.org"], "h1");
        let c = p.consent(&[]);
        assert_eq!(c, Consent::Ask { update: false, permissions: strings(&["ui:email-details", "email:read", "http:fetch"]),
            origins: strings(&["https://api.example.com", "https://nas.lan", "https://*.example.org"]) }, "implicit permissions are not asked for");
        let (title, body, private) = p.consent_dialog(&c, &[], &is_private_name);
        assert_eq!(title, "Switch on Demo?");
        assert!(body.starts_with(NATIVE_CODE_LINE), "{body}");
        assert!(body.contains("Demo 1.1.0 by Example"));
        // high risk first and marked, in plain words
        let (read, panel) = (body.find("[Needs care] Read your email").unwrap(), body.find("Add panels to message details").unwrap());
        assert!(read < panel, "{body}");
        assert!(!body.contains("email:read"), "plain words, not permission names: {body}");
        // public origins under http:fetch, the private host apart
        assert!(body.contains("Sites it may connect to (http:fetch):\n  https://api.example.com\n  https://*.example.org\n"), "{body}");
        assert!(body.contains("Hosts inside your network (private, loopback or LAN)") && body.contains("  https://nas.lan\n"), "{body}");
        assert_eq!(private, ["https://nas.lan"], "a wildcard never counts as a private host");
        let g = p.grant(private, "2026-11-24T10:00:00Z");
        assert_eq!((g.path.as_str(), g.sha256.as_str()), ("C:/p/dev.example.demo/demo.dll", "h1"));
        assert_eq!(g.private_hosts, ["https://nas.lan"]);
        assert_eq!(Grant::from_json(&g.to_json()), Some(g));
    }

    #[test]
    fn consent_is_not_asked_again_for_the_same_file() {
        let p = v2_plugin(&["email:read"], &[], "h1");
        let g = p.grant(vec![], "t");
        assert_eq!(p.consent(std::slice::from_ref(&g)), Consent::Granted);
        assert_eq!(granted_enabled(std::slice::from_ref(&p), &strings(&["dev.example.demo"]), std::slice::from_ref(&g)), ["dev.example.demo"]);
        // the grant counts only while it is switched on
        assert!(granted_enabled(std::slice::from_ref(&p), &[], std::slice::from_ref(&g)).is_empty());
        // another path is another file
        let mut moved = v2_plugin(&["email:read"], &[], "h1");
        moved.path = "C:/elsewhere/demo.dll".into();
        assert_eq!(moved.consent(std::slice::from_ref(&g)), Consent::SamePermissions);
    }

    #[test]
    fn a_new_hash_with_the_same_permissions_asks_again_saying_so() {
        let old = v2_plugin(&["email:read", "http:fetch"], &["https://api.example.com"], "h1");
        let g = old.grant(vec![], "t");
        let new = v2_plugin(&["email:read", "http:fetch"], &["https://api.example.com"], "h2");
        let c = new.consent(std::slice::from_ref(&g));
        assert_eq!(c, Consent::SamePermissions, "off until the user says yes again");
        assert!(granted_enabled(std::slice::from_ref(&new), &strings(&["dev.example.demo"]), std::slice::from_ref(&g)).is_empty());
        let (title, body, _) = new.consent_dialog(&c, std::slice::from_ref(&g), &|_| false);
        assert_eq!(title, "Switch on Demo again?");
        assert!(body.contains("Its files changed since you switched it on (still version 1.1.0). It asks for the same permissions as before."), "{body}");
        assert!(!body.contains("Read your email"), "nothing old is listed again: {body}");
        // with a version recorded in the grant, the dialog names both
        let mut older = g.clone();
        older.version = Some("1.0.0".into());
        let (_, body, _) = new.consent_dialog(&c, std::slice::from_ref(&older), &|_| false);
        assert!(body.contains("Updated from version 1.0.0 to 1.1.0, naming the same author (packages are not signed, so Barbican cannot prove it). It asks for the same permissions as before."), "{body}");
        // a yes pins the new file
        let g2 = new.grant(vec![], "t2");
        assert_eq!(new.consent(&[g2]), Consent::Granted);
    }

    #[test]
    fn a_new_hash_with_other_permissions_lists_everything_marking_the_additions() {
        let old = v2_plugin(&["email:read", "http:fetch"], &["https://api.example.com"], "h1");
        let g = old.grant(vec![], "t");
        let new = v2_plugin(&["email:read", "http:fetch", "email:send"], &["https://api.example.com", "https://new.example.com", "https://nas.lan"], "h2");
        let c = new.consent(std::slice::from_ref(&g));
        assert_eq!(c, Consent::Ask { update: true, permissions: strings(&["email:read", "http:fetch", "email:send"]),
            origins: strings(&["https://api.example.com", "https://new.example.com", "https://nas.lan"]) });
        let (_, body, private) = new.consent_dialog(&c, std::slice::from_ref(&g), &is_private_name);
        assert!(body.contains("What it asks for changed; all of it is listed, [New] marks what was not accepted before."), "{body}");
        assert!(body.contains("- [New] [Needs care] Change mail before it is sent") && body.contains("- [Needs care] Read your email"), "{body}");
        assert!(body.contains("Sites it may connect to (http:fetch):\n  https://api.example.com\n  [New] https://new.example.com\n"), "{body}");
        assert!(body.contains("Hosts inside your network") && body.contains("  [New] https://nas.lan\n"), "{body}");
        assert_eq!(private, ["https://nas.lan"]);
        // fewer permissions in a new file: everything that is left is listed again, nothing marked new
        let fewer_new = v2_plugin(&["email:read"], &[], "h2");
        let c = fewer_new.consent(std::slice::from_ref(&g));
        assert_eq!(c, Consent::Ask { update: true, permissions: strings(&["email:read"]), origins: vec![] });
        let (_, body, _) = fewer_new.consent_dialog(&c, std::slice::from_ref(&g), &|_| false);
        assert!(body.contains("- [Needs care] Read your email") && !body.contains("- [New]"), "{body}");
        // the same file asking for more (an edited manifest) asks again too
        let edited = v2_plugin(&["email:read", "http:fetch", "contacts:read"], &["https://api.example.com"], "h1");
        assert_eq!(edited.consent(std::slice::from_ref(&g)),
            Consent::Ask { update: true, permissions: strings(&["email:read", "http:fetch", "contacts:read"]), origins: strings(&["https://api.example.com"]) });
        // fewer permissions with the same file need no new consent
        let fewer = v2_plugin(&["email:read"], &[], "h1");
        assert_eq!(fewer.consent(std::slice::from_ref(&g)), Consent::Granted);
    }

    #[test]
    fn a_private_host_already_accepted_is_not_listed_again() {
        let old = v2_plugin(&["http:fetch"], &["https://nas.lan"], "h1");
        let g = old.grant(vec!["https://nas.lan".into()], "t");
        let new = v2_plugin(&["http:fetch"], &["https://nas.lan"], "h2");
        let c = new.consent(std::slice::from_ref(&g));
        let (_, body, private) = new.consent_dialog(&c, std::slice::from_ref(&g), &is_private_name);
        assert!(body.contains("the same permissions as before") && !body.contains("Hosts inside your network"), "{body}");
        assert_eq!(private, ["https://nas.lan"], "still recorded in the new grant");
    }

    #[test]
    fn another_author_takes_nothing_over_from_the_old_grant() {
        let old = v2_plugin(&["email:read", "http:fetch"], &["https://nas.lan"], "h1");
        let g = old.grant(vec!["https://nas.lan".into()], "t");
        let mut other = v2_plugin(&["email:read", "http:fetch"], &["https://nas.lan"], "h2");
        other.decl.author = "Someone Else".into();
        let c = other.consent(std::slice::from_ref(&g));
        assert_eq!(c, Consent::Ask { update: false, permissions: strings(&["email:read", "http:fetch"]), origins: strings(&["https://nas.lan"]) });
        let (title, body, _) = other.consent_dialog(&c, std::slice::from_ref(&g), &is_private_name);
        assert_eq!(title, "Switch on Demo?");
        assert!(body.contains("You had switched on a plugin with this id by Example. This one is by Someone Else, so nothing you accepted then carries over."), "{body}");
        assert!(body.contains("Read your email") && body.contains("Hosts inside your network") && body.contains("  https://nas.lan\n"), "{body}");
        assert!(!body.contains("[New]") && !body.contains("Updated"), "{body}");
        // a grant that does not record its author (written before authors were recorded) counts as another author
        let mut unknown = g.clone();
        unknown.author = None;
        let same_author = v2_plugin(&["email:read", "http:fetch"], &["https://nas.lan"], "h2");
        let c = same_author.consent(std::slice::from_ref(&unknown));
        assert!(matches!(c, Consent::Ask { update: false, .. }), "{c:?}");
        let (_, body, _) = same_author.consent_dialog(&c, std::slice::from_ref(&unknown), &is_private_name);
        assert!(body.contains("cannot tell whether this one has the same author"), "{body}");
        // the very same files under an old grant stay on
        assert_eq!(old.consent(std::slice::from_ref(&unknown)), Consent::Granted);
    }

    #[test]
    fn an_empty_author_is_never_the_same_author() {
        let mut old = v2_plugin(&["email:read"], &[], "h1");
        old.decl.author = String::new();
        let g = old.grant(vec![], "t");
        let mut new = v2_plugin(&["email:read"], &[], "h2");
        new.decl.author = String::new();
        let c = new.consent(std::slice::from_ref(&g));
        assert_eq!(c, Consent::Ask { update: false, permissions: strings(&["email:read"]), origins: vec![] });
        let (_, body, _) = new.consent_dialog(&c, std::slice::from_ref(&g), &|_| false);
        assert!(body.contains("cannot tell whether this one has the same author") && !body.contains("by the same author"), "{body}");
        // the very same files still count
        assert_eq!(old.consent(std::slice::from_ref(&g)), Consent::Granted);
    }

    #[test]
    fn an_update_asking_for_fewer_permissions_is_not_the_same_set() {
        let old = v2_plugin(&["email:read", "http:fetch"], &["https://api.example.com"], "h1");
        let g = old.grant(vec![], "t");
        let none = v2_plugin(&[], &[], "h2");
        let c = none.consent(std::slice::from_ref(&g));
        assert_eq!(c, Consent::Ask { update: true, permissions: vec![], origins: vec![] });
        let (_, body, _) = none.consent_dialog(&c, std::slice::from_ref(&g), &|_| false);
        assert!(!body.contains("the same permissions as before") && body.contains("What it asks for changed") && body.contains("- (no permissions)"), "{body}");
    }

    #[test]
    fn a_flat_package_folder_with_too_many_files_is_refused() {
        let d = temp_folder("flat-many");
        let pkg = d.join("dev.example.flat");
        std::fs::create_dir_all(&pkg).unwrap();
        for i in 0..1002 { std::fs::write(pkg.join(format!("f{i}")), b"").unwrap(); }
        assert!(package_fingerprint(&pkg, false).unwrap_err().contains("too large"));
        let _ = std::fs::remove_dir_all(&d);
    }

    #[test]
    fn a_loaded_unverified_plugin_keeps_its_state_across_rediscovery() {
        let mut old = bare("file:x.dll", "C:/p/x.dll", "h1");
        old.verified = false;
        old.decl.api = 1;
        // what the library's own manifest said once it was loaded
        (old.id, old.name, old.version, old.hooks) = ("x".into(), "X".into(), "2.0".into(), strings(&["email.details"]));
        old.backend = Some(Arc::new(Recorder(Mutex::new(vec![]))));
        old.tripped.store(true, Ordering::SeqCst);
        old.busy.store(true, Ordering::SeqCst);
        old.failures.store(3, Ordering::SeqCst);
        let mut found = bare("file:x.dll", "C:/p/x.dll", "h1");
        found.verified = false;
        found.decl.api = 1;
        let merged = merge_discovered(vec![old], vec![found]);
        assert_eq!(merged.len(), 1);
        assert!(merged[0].is_tripped() && merged[0].busy.load(Ordering::SeqCst) && merged[0].backend.is_some());
        assert_eq!((merged[0].name.as_str(), merged[0].failures.load(Ordering::SeqCst)), ("X", 3));
        // another file at that path is a new entry
        let mut replaced = bare("file:x.dll", "C:/p/x.dll", "h2");
        replaced.verified = false;
        replaced.decl.api = 1;
        let merged = merge_discovered(merged, vec![replaced]);
        assert!(!merged[0].is_tripped() && merged[0].backend.is_none());
    }

    #[test]
    fn text_from_plugins_is_shown_plain_and_short() {
        assert_eq!(shown("a\nb\u{202E}c\u{200B}d", 300), "a bcd");
        let long = "x".repeat(400);
        let s = shown(&long, MAX_SHOWN);
        assert_eq!(s.chars().count(), MAX_SHOWN + 1);
        assert!(s.ends_with('…'));
        // manifest values are quoted short and plain in errors
        let e = validate_manifest(&json!({"api": 2, "id": format!("Bad\u{202E}{}", "y".repeat(200)), "name": "N", "version": "1.0.0",
            "library": {platform_key(): "a.dll"}})).unwrap_err();
        assert!(!e.contains('\u{202E}') && !e.contains(&"y".repeat(100)), "{e}");
    }

    #[test]
    fn names_with_direction_or_zero_width_characters_are_refused() {
        let base = json!({"api": 2, "id": "dev.example.demo", "name": "Demo", "version": "1.0.0", "library": {platform_key(): "a.dll"}});
        assert!(validate_manifest(&base).is_ok());
        for bad in ["De\u{202E}mo", "De\u{200B}mo", "De\u{2066}mo", "\u{FEFF}Demo"] {
            assert!(validate_manifest(&with(base.clone(), "name", json!(bad))).is_err(), "{bad:?}");
            assert!(validate_manifest(&with(base.clone(), "author", json!(bad))).is_err(), "{bad:?}");
        }
        // the joiners Persian, Indic and emoji text needs are allowed
        assert!(validate_manifest(&with(base.clone(), "name", json!("\u{0645}\u{06cc}\u{200c}\u{062e}\u{0648}\u{0627}\u{0647}\u{0645}"))).is_ok());
        assert!(validate_manifest(&with(base.clone(), "description", json!("A \u{1F469}\u{200D}\u{1F4BB} plugin"))).is_ok());
    }

    #[test]
    fn api1_manifests_that_loaded_before_are_cleaned_not_refused() {
        // what Barbican 0.2.0 accepted keeps loading: a line break in the description, a number as version, a hook that
        // is not text, direction characters in the name
        let m = parse_manifest(&json!({"id": "spam-score", "name": "Spam\u{202E}Score", "version": 2,
            "description": "Scores spam.\nSecond line.", "hooks": ["email.details", 5, "a\u{200B}b", "email.details"]})).unwrap();
        assert_eq!(m.name, "SpamScore");
        assert_eq!(m.version, "2");
        assert_eq!(m.description, "Scores spam. Second line.");
        assert_eq!(m.hooks, ["email.details"], "plain hook names only, once each");
        let long = parse_manifest(&json!({"id": "x", "name": "n".repeat(500)})).unwrap();
        assert!(long.name.chars().count() <= 101);
        assert_eq!(parse_manifest(&json!({"id": "x", "name": 7})).unwrap().name, "7");
        assert_eq!(parse_manifest(&json!({"id": "x"})).unwrap().name, "Plugin");
        // the id rules are those of 0.2.0
        assert!(parse_manifest(&json!({"id": "x\ny"})).is_err());
        assert!(parse_manifest(&json!({"id": "file:x"})).is_err());
    }

    #[test]
    fn versions_that_overflow_are_not_read_as_zero() {
        assert_eq!(version_triple("1.2.3-beta+x"), Some((1, 2, 3)));
        assert_eq!(version_triple("99999999999999999999999.0.0"), None);
        let m = json!({"api": 2, "id": "dev.example.demo", "name": "Demo", "version": "1.0.0", "library": {platform_key(): "a.dll"},
            "minAppVersion": "0.99999999999999999999999.0"});
        assert!(validate_manifest(&m).unwrap_err().contains("needs Barbican"), "a version too large to read is not 0.0.0");
        assert!(validate_manifest(&with(m, "minAppVersion", json!("0.0.1"))).is_ok());
    }

    #[test]
    fn manifest_lists_are_capped_and_deduplicated() {
        let mut errors = vec![];
        let v = json!({"hooks": ["a", "b", "a", "c", "b"]});
        assert_eq!(string_list(&v, "hooks", &mut errors), strings(&["a", "b", "c"]));
        let many = json!({"hooks": (0..MAX_LIST_ENTRIES + 1).map(|i| format!("h{i}")).collect::<Vec<_>>()});
        assert!(string_list(&many, "hooks", &mut errors).is_empty());
        assert!(errors.iter().any(|e| e.contains("more than")), "{errors:?}");
    }

    #[test]
    fn a_package_over_the_size_cap_is_not_listed() {
        let d = temp_folder("big-package");
        let pkg = d.join("dev.example.big");
        std::fs::create_dir_all(&pkg).unwrap();
        // the size is checked before anything is read, so a file that only claims the size is enough
        std::fs::File::create(pkg.join("big.bin")).unwrap().set_len(MAX_PACKAGE_BYTES + 1).unwrap();
        let e = package_fingerprint(&pkg, false).unwrap_err();
        assert!(e.contains("more than 64 MB"), "{e}");
        let _ = std::fs::remove_dir_all(&d);
    }

    #[test]
    fn the_hash_streams_the_same_as_reading_the_whole_file() {
        let d = temp_folder("stream");
        let f = d.join("x.bin");
        let data: Vec<u8> = (0..200_000u32).map(|i| (i % 251) as u8).collect();
        std::fs::write(&f, &data).unwrap();
        let want: String = Sha256::digest(&data).iter().map(|x| format!("{x:02x}")).collect();
        assert_eq!(fingerprint_with(&f, false).unwrap(), want);
        let _ = std::fs::remove_dir_all(&d);
    }

    #[test]
    fn dropped_in_ids_come_from_the_sidecars() {
        let d = temp_folder("dropped-ids");
        std::fs::write(d.join(lib_name("spam")), b"x").unwrap();
        std::fs::write(d.join("spam.manifest.json"), json!({"id": "spam-score"}).to_string()).unwrap();
        std::fs::write(d.join(lib_name("plain")), b"x").unwrap();
        assert_eq!(dropped_in_ids(&d), ["spam-score"]);
        let _ = std::fs::remove_dir_all(&d);
    }

    #[test]
    fn api1_plugins_get_a_dialog_without_permissions() {
        let mut p = bare("spam-score", "C:/p/spam.dll", "h1");
        p.name = "Spam Score".into();
        p.hooks = strings(&["email.details"]);
        p.decl.api = 1;
        let c = p.consent(&[]);
        assert_eq!(c, Consent::Ask { update: false, permissions: vec![], origins: vec![] });
        let (_, body, _) = p.consent_dialog(&c, &[], &|_| false);
        assert!(body.starts_with(NATIVE_CODE_LINE) && body.contains("plugin API 1") && body.contains("Hooks: email.details"), "{body}");
        assert_eq!(p.consent(&[p.grant(vec![], "t")]), Consent::Granted);
    }

    #[test]
    fn grants_are_read_from_prefs_with_the_old_bindings() {
        let g = v2_plugin(&["email:read"], &[], "h1").grant(vec![], "t");
        let prefs = json!({"pluginGrants": {"dev.example.demo": g.to_json(), "broken": {"id": 1}},
            "pluginBindings": {"dev.example.demo": "C:/old.dll|x", "legacy": "C:/p/l.dll|h9", "junk": 5}});
        let grants = grants_from_prefs(prefs.as_object().unwrap());
        assert_eq!(grants.len(), 2, "{grants:?}");
        assert_eq!(grants[0], g, "a grant wins over an old binding");
        assert_eq!((grants[1].id.as_str(), grants[1].path.as_str(), grants[1].sha256.as_str()), ("legacy", "C:/p/l.dll", "h9"));
    }

    #[test]
    fn api2_plugins_are_not_loaded_yet() {
        let mut list = vec![v2_plugin(&["email:read"], &[], "h1")];
        let loads = Arc::new(AtomicUsize::new(0));
        let n = loads.clone();
        let l = stub_loader(move |_| { n.fetch_add(1, Ordering::SeqCst); Err("no".into()) });
        assert!(sync_with(&mut list, &strings(&["dev.example.demo"]), &l, MS).is_empty());
        assert_eq!(loads.load(Ordering::SeqCst), 0);
        assert!(describe(&list[0])["description"].as_str().unwrap().contains("does not run API 2 plugins yet"));
    }

    #[test]
    fn discovery_reports_bad_packages() {
        let d = temp_folder("packages");
        let lib = lib_name("demo");
        let good = json!({"api": 2, "id": "dev.example.good", "name": "Good", "version": "1.0.0", "library": {platform_key(): lib},
            "permissions": ["email:read"], "hooks": ["onEmailOpen"]});
        std::fs::create_dir_all(d.join("dev.example.good")).unwrap();
        std::fs::write(d.join("dev.example.good").join("manifest.json"), good.to_string()).unwrap();
        std::fs::write(d.join("dev.example.good").join(&lib), b"x").unwrap();
        std::fs::create_dir_all(d.join("wrong-name")).unwrap();
        std::fs::write(d.join("wrong-name").join("manifest.json"), with(good.clone(), "id", json!("dev.example.other")).to_string()).unwrap();
        std::fs::write(d.join("wrong-name").join(&lib), b"x").unwrap();
        std::fs::create_dir_all(d.join("dev.example.nolib")).unwrap();
        std::fs::write(d.join("dev.example.nolib").join("manifest.json"), with(good.clone(), "id", json!("dev.example.nolib")).to_string()).unwrap();
        std::fs::create_dir_all(d.join(".install-x")).unwrap();
        std::fs::write(d.join(".install-x").join("manifest.json"), b"{").unwrap();
        std::fs::create_dir_all(d.join("not-a-plugin")).unwrap();
        let (list, errors) = discover_in(&d);
        assert_eq!(list.iter().map(|p| p.key.as_str()).collect::<Vec<_>>(), ["dev.example.good"]);
        assert_eq!(list[0].decl.permissions, ["email:read"]);
        assert_eq!(errors.len(), 2, "{errors:?}");
        assert!(errors.iter().any(|e| e.contains("wrong-name") && e.contains("folder name")), "{errors:?}");
        assert!(errors.iter().any(|e| e.contains("dev.example.nolib") && e.contains("missing")), "{errors:?}");
        let _ = std::fs::remove_dir_all(&d);
    }

    #[test]
    fn api_version_does_not_truncate() {
        assert!(parse_manifest(&json!({"id": "x", "api": 4294967297u64})).is_err());
        assert!(parse_manifest(&json!({"id": "x", "api": 1})).is_ok());
    }

    #[test]
    fn enabling_is_bound_to_the_file() {
        let list = vec![bare("a", "C:/p/a.dll", "h1"), bare("b", "C:/p/b.dll", "h2")];
        let en = vec!["a".to_string(), "b".to_string(), "gone".to_string()];
        // API 1's pluginBindings still count, as grants without permissions
        let prefs = json!({"pluginBindings": {"a": "C:/p/a.dll|h1", "b": "C:/p/b.dll|OLD", "gone": "x|y"}});
        let grants = grants_from_prefs(prefs.as_object().unwrap());
        assert_eq!(granted_enabled(&list, &en, &grants), ["a"]);
        assert!(granted_enabled(&list, &en, &[]).is_empty(), "no grant, no consent");
    }

    #[test]
    fn rediscovery_keeps_state_and_drops_replaced_files() {
        let mut keep = bare("k", "p", "h");
        keep.failures.store(2, Ordering::SeqCst);
        let old = vec![keep, bare("r", "p2", "old")];
        let new = vec![bare("k", "p", "h"), bare("r", "p2", "new"), bare("n", "p3", "z")];
        let merged = merge_discovered(old, new);
        assert_eq!(merged.len(), 3);
        assert_eq!(merged[0].failures.load(Ordering::SeqCst), 2, "unchanged entry keeps its state");
        assert_eq!(merged[1].fingerprint, "new");
    }

    #[test]
    fn upgrade_notice_when_a_plugin_is_installed_but_off() {
        let list = vec![bare("a", "p", "")];
        assert!(has_unenabled(&list, &[]));
        assert!(!has_unenabled(&list, &["a".to_string()]));
        assert!(!has_unenabled(&[], &[]));
    }

    #[test]
    fn fingerprint_changes_with_the_file() {
        let d = temp_folder("fp");
        let f = d.join("x.bin");
        std::fs::write(&f, b"one").unwrap();
        let a = fingerprint(&f).unwrap();
        // a different length changes the cached stamp even if the clock tick is the same
        std::fs::write(&f, b"two-two").unwrap();
        assert_ne!(a, fingerprint(&f).unwrap());
        let _ = std::fs::remove_dir_all(&d);
    }

    #[test]
    fn a_rebuild_reuses_the_hash_while_size_and_time_are_unchanged() {
        let _turn = cache_test();
        let d = temp_folder("hash-cache");
        let f = d.join("x.bin");
        std::fs::write(&f, b"one").unwrap();
        let first = fingerprint(&f).unwrap();
        // Put a marker where the cache would store this file's hash for its current stamp.
        let meta = std::fs::metadata(&f).unwrap();
        let stamp = (meta.len(), meta.modified().unwrap());
        hash_cache().lock().unwrap().insert(f.clone(), (stamp, "from-cache".into()));
        assert_eq!(fingerprint(&f).unwrap(), "from-cache", "same size and time: the hash is reused");
        // A different file changes the stamp, so the hash is computed again.
        std::fs::write(&f, b"one-and-more").unwrap();
        let again = fingerprint(&f).unwrap();
        assert_ne!(again, "from-cache");
        assert_ne!(again, first);
        let _ = std::fs::remove_dir_all(&d);
    }

    #[test]
    fn a_file_that_cannot_be_read_is_a_discovery_error() {
        let d = temp_folder("unreadable");
        assert!(fingerprint(&d.join("missing.dll")).is_err());
        // A directory with a library name cannot be hashed: the plugin must not be listed as bindable.
        std::fs::create_dir(d.join(lib_name("locked"))).unwrap();
        std::fs::write(d.join(lib_name("fine")), b"x").unwrap();
        let (list, errors) = discover_in(&d);
        assert_eq!(list.len(), 1, "{:?}", list.iter().map(|p| &p.key).collect::<Vec<_>>());
        assert!(!list.iter().any(|p| p.name.contains("locked")));
        assert!(errors.iter().any(|e| e.contains("locked") && e.contains("cannot read")), "{errors:?}");
        let _ = std::fs::remove_dir_all(&d);
    }

    struct Tracked { dropped: Arc<AtomicBool> }
    impl PluginBackend for Tracked {
        fn call(&self, _: &str, _: &Value) -> Result<Option<Value>, String> { Ok(None) }
    }
    impl Drop for Tracked {
        fn drop(&mut self) { self.dropped.store(true, Ordering::SeqCst); }
    }

    #[test]
    fn switching_off_keeps_the_loaded_backend_alive() {
        let dropped = Arc::new(AtomicBool::new(false));
        let d = dropped.clone();
        let l = stub_loader(move |_| Ok((stub_meta("keepme"), Arc::new(Tracked { dropped: d.clone() }) as Arc<dyn PluginBackend>)));
        let mut list = vec![bare("keepme", "x", "")];
        let en = vec!["keepme".to_string()];
        assert!(sync_with(&mut list, &en, &l, MS).is_empty());
        assert!(list[0].active(&en));
        sync_with(&mut list, &[], &l, MS);
        assert!(list[0].backend.is_none() && !list[0].active(&en), "switched off: not callable");
        assert!(!dropped.load(Ordering::SeqCst), "the backend object is not dropped (that would unload the library)");
    }

    #[test]
    fn a_plugin_with_an_abandoned_call_is_not_loaded_again() {
        let loads = Arc::new(AtomicUsize::new(0));
        let n = loads.clone();
        let dropped = Arc::new(AtomicBool::new(false));
        let l = stub_loader(move |_| {
            n.fetch_add(1, Ordering::SeqCst);
            Ok((stub_meta("busy-plugin"), Arc::new(Tracked { dropped: dropped.clone() }) as Arc<dyn PluginBackend>))
        });
        let mut list = vec![bare("busy-plugin", "x", "")];
        list[0].busy.store(true, Ordering::SeqCst);
        let en = vec!["busy-plugin".to_string()];
        assert!(sync_with(&mut list, &en, &l, MS).is_empty());
        assert_eq!(loads.load(Ordering::SeqCst), 0, "an abandoned call is still inside the library");
        assert!(list[0].backend.is_none());
        list[0].busy.store(false, Ordering::SeqCst);
        assert!(sync_with(&mut list, &en, &l, MS).is_empty());
        assert_eq!(loads.load(Ordering::SeqCst), 1, "loaded once the old call returned");
        assert!(list[0].active(&en));
    }

    #[test]
    fn a_changed_file_at_a_loaded_path_needs_a_restart() {
        let path = format!("C:/plugins/versioned-{}.dll", std::process::id());
        let loads = Arc::new(AtomicUsize::new(0));
        let n = loads.clone();
        let l = stub_loader(move |_| {
            n.fetch_add(1, Ordering::SeqCst);
            Ok((stub_meta("versioned"), Arc::new(Recorder(Mutex::new(vec![]))) as Arc<dyn PluginBackend>))
        });
        let en = vec!["versioned".to_string()];
        let mut first = vec![bare("versioned", &path, "hash-one")];
        assert!(sync_with(&mut first, &en, &l, MS).is_empty());
        assert_eq!(loads.load(Ordering::SeqCst), 1);

        // rediscovery of a replaced file at the same path: the old module is still mapped
        let mut replaced = vec![bare("versioned", &path, "hash-two")];
        let errs = sync_with(&mut replaced, &en, &l, MS);
        assert_eq!(errs.len(), 1, "{errs:?}");
        assert!(errs[0].contains("restart Barbican to load the new version"), "{errs:?}");
        assert_eq!(loads.load(Ordering::SeqCst), 1, "the new file is not handed to the loader");
        assert!(replaced[0].backend.is_none() && replaced[0].load_failed);

        // the same file at the same path still loads (for example a retry after switching off)
        let mut same = vec![bare("versioned", &path, "hash-one")];
        assert!(sync_with(&mut same, &en, &l, MS).is_empty());
        assert_eq!(loads.load(Ordering::SeqCst), 2);
        assert!(same[0].active(&en));
    }

    #[test]
    fn a_load_hashes_the_file_again_without_the_cache() {
        let _turn = cache_test();
        let d = temp_folder("fresh-load");
        let f = d.join(lib_name("cached"));
        std::fs::write(&f, b"one").unwrap();
        fingerprint(&f).unwrap();
        // the cache says something else for the file's current size and time (as after an edit that kept both)
        let meta = std::fs::metadata(&f).unwrap();
        hash_cache().lock().unwrap().insert(f.clone(), ((meta.len(), meta.modified().unwrap()), "from-cache".into()));
        assert_eq!(fingerprint(&f).unwrap(), "from-cache");
        let p = bare("cached", &f.display().to_string(), "from-cache");
        assert_eq!(unchanged_on_disk(&p), Ok(false), "the switch-on check reads the file itself");
        let loads = Arc::new(AtomicUsize::new(0));
        let n = loads.clone();
        let l = stub_loader(move |_| { n.fetch_add(1, Ordering::SeqCst); Ok((stub_meta("cached"), Arc::new(Recorder(Mutex::new(vec![]))) as Arc<dyn PluginBackend>)) });
        let mut list = vec![p];
        let errs = sync_hashed(&mut list, &strings(&["cached"]), &l, MS, &fresh_fingerprint);
        assert!(errs.len() == 1 && errs[0].contains("changed since"), "{errs:?}");
        assert_eq!(loads.load(Ordering::SeqCst), 0, "a file that is not the accepted one is never loaded");
        assert!(!hash_cache().lock().unwrap().contains_key(&f), "a mismatch forgets the cached hashes");
        // the real hash loads
        let real = fingerprint_with(&f, false).unwrap();
        let mut list = vec![bare("cached", &f.display().to_string(), &real)];
        assert_eq!(unchanged_on_disk(&list[0]), Ok(true));
        assert!(unchanged_on_disk(&bare("gone", &d.join("missing.dll").display().to_string(), "h")).is_err(), "a read error is its own outcome");
        assert!(sync_hashed(&mut list, &strings(&["cached"]), &l, MS, &fresh_fingerprint).is_empty());
        assert_eq!(loads.load(Ordering::SeqCst), 1);
        let _ = std::fs::remove_dir_all(&d);
    }

    #[test]
    fn a_package_hash_without_the_cache_reads_every_file() {
        let _turn = cache_test();
        let d = temp_folder("fresh-package");
        let pkg = d.join("dev.example.p");
        std::fs::create_dir_all(&pkg).unwrap();
        std::fs::write(pkg.join("a.bin"), b"a").unwrap();
        let real = package_fingerprint(&pkg, true).unwrap();
        let stamp = hash_cache().lock().unwrap().get(&pkg).map(|(_, h)| h[..h.len() - real.len()].to_string()).unwrap();
        hash_cache().lock().unwrap().insert(pkg.clone(), ((0, std::time::UNIX_EPOCH), format!("{stamp}from-cache")));
        assert_eq!(package_fingerprint(&pkg, true).unwrap(), "from-cache");
        assert_eq!(package_fingerprint(&pkg, false).unwrap(), real);
        let _ = std::fs::remove_dir_all(&d);
    }

    #[test]
    fn a_linked_package_folder_is_not_listed() {
        let d = temp_folder("linked-package");
        let real = d.join("elsewhere");
        let plugins = d.join("plugins");
        std::fs::create_dir_all(&real).unwrap();
        std::fs::create_dir_all(&plugins).unwrap();
        let lib = lib_name("demo");
        let mf = json!({"api": 2, "id": "dev.example.linked", "name": "Linked", "version": "1.0.0", "library": {platform_key(): lib},
            "permissions": ["email:read"], "hooks": ["onEmailOpen"]});
        std::fs::write(real.join("manifest.json"), mf.to_string()).unwrap();
        std::fs::write(real.join(&lib), b"x").unwrap();
        let link = plugins.join("dev.example.linked");
        #[cfg(windows)]
        let made = std::os::windows::fs::symlink_dir(&real, &link).is_ok()
            || std::process::Command::new("cmd").args(["/C", "mklink", "/J"]).arg(&link).arg(&real)
                .stdout(std::process::Stdio::null()).stderr(std::process::Stdio::null()).status().is_ok_and(|s| s.success());
        #[cfg(unix)]
        let made = std::os::unix::fs::symlink(&real, &link).is_ok();
        if !made { eprintln!("skipped: cannot make a link here"); let _ = std::fs::remove_dir_all(&d); return; }
        assert!(link.is_dir(), "the link resolves");
        let (list, errors) = discover_in(&plugins);
        assert!(list.is_empty(), "{:?}", list.iter().map(|p| &p.key).collect::<Vec<_>>());
        assert!(errors.iter().any(|e| e.contains("link or junction")), "{errors:?}");
        // removing the link leaves its target alone
        let _ = std::fs::remove_dir(&link).or_else(|_| std::fs::remove_file(&link));
        assert!(real.join("manifest.json").is_file());
        let _ = std::fs::remove_dir_all(&d);
    }
    #[test]
    fn line_separators_never_show_in_a_one_line_text() {
        assert!(!is_plain("a\u{2028}b") && !is_plain("a\u{2029}b"));
        assert_eq!(shown("x\u{202E}lld.exe\u{2028}.dll", 64), "xlld.exe.dll");
    }
}
