//! Plugin API v2 host, core (ROADMAP Phase 5 step B3; the design is PLUGINS.md "API v2").
//!
//! An installed API 2 package ([`crate::plugin_package`], consented to by [`crate::plugins::Grant`]) is loaded on its
//! own host thread ([`start`]): the library is opened, its own manifest must agree with the package's `manifest.json`,
//! and `barbican_plugin_init` is called once with the host table ([`BarbicanHost`]) and `init_json`, and
//! `barbican_plugin_shutdown` when the plugin is switched off.
//!
//! Messages (B4): [`HostCtx::fire`] queues a hook message for a plugin that hears it ([`HostCtx::hears`]: a hook of
//! [`FIRED_HOOKS`] its manifest declares, with the hook's permission granted). The host thread hands messages to
//! `barbican_plugin_handle` one at a time; nobody waits on that thread. A deadline watcher ends every message that
//! waits in the queue past its budget (`Dropped`, not a failure), runs past it inside `handle` (`TimedOut`, a failure,
//! and the plugin is hung: what is queued or sent meanwhile is dropped at once), or answered `deferred` and did not
//! `post` its result in time; a plugin still inside one `handle` at the hard cap ([`HOLD_CAP`]) is switched off.
//! Error replies, panics and malformed or over-size replies are failures; [`MAX_FAILURES`] in a row switch it off.
//! The `settings` message ([`HostCtx::notify_settings`]) and `cancel` ([`HostCtx::cancel`]) go the same way. What a
//! hook means to the caller (observer, intercept, transform) is [`crate::plugin_hooks`]'s business.
//!
//! Every host call ([`HostCtx::call`]) goes through the same gate, in this order, and fails closed:
//! 1. the plugin is switched off (by the user, the failure limit or a missed init deadline): `disabled`;
//! 2. the rate budget, [`CALLS_PER_SECOND`] per plugin: `rate_limited`. A plugin over it in [`MAX_FAILURES`]
//!    one-second windows in a row is switched off for the session, as for 3 failed calls in a row;
//! 3. the catalogue (`spec/plugins/host-api.csv`): an unknown or skipped method is `unsupported`;
//! 4. the method's catalogue permission against what the user granted (implicit ones count): `denied`;
//! 5. the methods this build answers ([`METHODS`]); every other catalogued method is `unsupported` for now.
//!
//! The core calls of B3:
//! * `storage.get/set/remove/keys`: per plugin and per login (the login key of [`Account`]), JSON values, 1 MiB per
//!   plugin per login, `plugin-data\<id>\<login hash>.json`, written atomically ([`Storage`]). Uninstalling a package
//!   removes `plugin-data\<id>\`; signing out removes the login's files ([`crate::signout::targets`]).
//! * `plugin.getConfig`: the plugin's own settings ([`effective_settings`]); the user sets them in Settings > Add-ons
//!   ([`setting_rows`], [`setting_value`]); secret settings are not shown or handed over yet (`secrets.*` is later).
//! * `toast.info/success/warning/error`, `ui.alert`, `ui.confirm`: drawn by the host, text made plain and short with
//!   [`crate::plugins::shown`], always naming the plugin, rate-limited ([`TOASTS_PER_TEN_SECONDS`],
//!   [`DIALOGS_PER_MINUTE`], one dialog at a time, none during init because the UI thread waits for init).
//! * `http.fetch`: https only, to the origins the manifest declares and the user accepted, never to an IP literal;
//!   the name is resolved once and every address must be public unless the user accepted that exact origin as a
//!   private host ([`plan_hop`]); the connection goes to the checked addresses only; every redirect (at most
//!   [`MAX_REDIRECTS`]) is checked again ([`fetch_with`]); no proxy, no cookies, nothing of Barbican's sign-in;
//!   bodies at most [`FETCH_MAX_BYTES`], [`FETCH_TIMEOUT`] in all.
//! * `user.getSelf`, `user.getAccounts` (`account:read`): who is signed in (login, display name, server); never a
//!   token or password. `app.getInfo`: product and version.
use crate::plugins::{shown, Grant, IMPLICIT_PERMISSIONS, MAX_FAILURES};
use serde_json::{json, Value};
use slint::ComponentHandle;
use std::collections::VecDeque;
use std::ffi::{c_char, c_void, CStr, CString};
use std::net::{IpAddr, SocketAddr};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{mpsc, Arc, Mutex, MutexGuard, OnceLock};
use std::time::{Duration, Instant};

/// The host table's `abi`.
pub const ABI: u32 = 2;
/// Largest envelope either side sends (PLUGINS.md "Memory and ownership").
pub const MAX_ENVELOPE: usize = 4 << 20;
/// Host calls a plugin may make per second (PLUGINS.md "Threading": 50 by default).
pub const CALLS_PER_SECOND: u32 = 50;
/// Toasts per plugin in ten seconds (Bulwark `TOASTS_PER_TEN_SECONDS`, `lib/plugin-sandbox/host-api-toasts.ts`);
/// more are refused, not queued.
pub const TOASTS_PER_TEN_SECONDS: usize = 5;
/// Dialogs (`ui.alert`, `ui.confirm`) per plugin in a minute; one is open at a time.
pub const DIALOGS_PER_MINUTE: usize = 3;
/// Longest toast text (Bulwark `MAX_TOAST_LENGTH`).
pub const MAX_TOAST: usize = 300;
/// Storage per plugin per login (Bulwark `PLUGIN_STORAGE_QUOTA_BYTES`), counted as the stored JSON's bytes.
pub const STORAGE_QUOTA_BYTES: usize = 1 << 20;
/// Longest storage key, in characters (Bulwark `storageSet`).
pub const MAX_STORAGE_KEY: usize = 256;
/// Largest request or response body of `http.fetch`; larger data goes by blob handle (later).
pub const FETCH_MAX_BYTES: usize = 1 << 20;
/// Network host calls (PLUGINS.md, Bulwark `lib/plugin-sandbox/api-timeouts.ts`: 120 s), all hops together.
pub const FETCH_TIMEOUT: Duration = Duration::from_secs(120);
/// Redirects one `http.fetch` follows.
pub const MAX_REDIRECTS: usize = 5;
/// How long a dialog may wait for the user (the 10-minute hold cap).
pub const DIALOG_WAIT: Duration = Duration::from_secs(600);
/// Log lines a plugin may write per second; the rest are dropped.
pub const LOG_LINES_PER_SECOND: u32 = 20;

/// The host methods this build answers (`init_json.features.methods`). Every other catalogued method is `unsupported`.
pub const METHODS: [&str; 15] = ["app.getInfo", "plugin.getConfig", "storage.get", "storage.set", "storage.remove",
    "storage.keys", "toast.info", "toast.success", "toast.warning", "toast.error", "ui.alert", "ui.confirm",
    "http.fetch", "user.getSelf", "user.getAccounts"];

fn lock<T>(m: &Mutex<T>) -> MutexGuard<'_, T> { m.lock().unwrap_or_else(|e| e.into_inner()) }

// ---------------------------------------------------------------------------------------------------------------
// errors, the environment the calls reach
// ---------------------------------------------------------------------------------------------------------------

/// A refused or failed host call: `code` is one of PLUGINS.md's error codes; `message` is for the plugin's log.
#[derive(Clone, Debug, PartialEq)]
pub struct HostError {
    pub code: &'static str,
    pub message: String,
}

fn err(code: &'static str, message: impl Into<String>) -> HostError { HostError { code, message: message.into() } }

/// Who is signed in, as a plugin may see it. `login_key` is [`crate::reminders::owner_key`] (server and user name),
/// the key storage is kept under; it is never handed to the plugin.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Account {
    pub login_key: String,
    pub account_id: String,
    pub login: String,
    pub name: String,
    pub server: String,
}

/// One request `http.fetch` makes, after the checks.
#[derive(Clone, Debug, PartialEq)]
pub struct Outgoing {
    pub method: String,
    pub headers: Vec<(String, String)>,
    pub body: Option<Vec<u8>>,
}

/// One checked hop: the URL, its host and the only addresses the connection may go to.
#[derive(Clone, Debug, PartialEq)]
pub struct Hop {
    pub url: reqwest::Url,
    pub host: String,
    pub pin: Vec<SocketAddr>,
}

/// What one hop answered (redirects are not followed by the sender).
#[derive(Clone, Debug, Default, PartialEq)]
pub struct HopReply {
    pub status: u16,
    pub status_text: String,
    pub headers: Vec<(String, String)>,
    pub body: Vec<u8>,
}

/// What the host calls reach outside this module: the app ([`AppEnv`]) or a fake in tests.
pub trait HostEnv: Send + Sync {
    fn account(&self) -> Option<Account>;
    /// `plugin-data` (each plugin's folder is below it).
    fn data_dir(&self) -> PathBuf;
    /// The values the user set for this plugin (the `pluginSettings` pref), not checked yet.
    fn stored_settings(&self, plugin: &str) -> Value;
    /// Show a toast; `text` is already plain, short and names the plugin.
    fn toast(&self, tone: &str, text: &str);
    /// Show a dialog and wait for the user: OK (true) or Cancel / closed (false). `confirm` false: only OK.
    fn dialog(&self, confirm: bool, title: &str, text: &str, ok: &str, cancel: &str) -> Result<bool, HostError>;
    fn resolve(&self, host: &str, port: u16) -> std::io::Result<Vec<IpAddr>>;
    /// Make one checked request to `hop.pin` only, without following redirects, before `deadline`.
    fn send(&self, hop: &Hop, req: &Outgoing, deadline: Instant) -> Result<HopReply, HostError>;
    fn log(&self, line: &str) { eprintln!("{line}"); }
}

/// Before the app installs its environment: nobody is signed in, nothing is shown, nothing goes out.
struct NoEnv;

impl HostEnv for NoEnv {
    fn account(&self) -> Option<Account> { None }
    fn data_dir(&self) -> PathBuf { crate::plugins::data_dir() }
    fn stored_settings(&self, _: &str) -> Value { Value::Null }
    fn toast(&self, _: &str, _: &str) {}
    fn dialog(&self, _: bool, _: &str, _: &str, _: &str, _: &str) -> Result<bool, HostError> { Err(err("busy", "the app is not ready")) }
    fn resolve(&self, _: &str, _: u16) -> std::io::Result<Vec<IpAddr>> { Err(std::io::Error::other("the app is not ready")) }
    fn send(&self, _: &Hop, _: &Outgoing, _: Instant) -> Result<HopReply, HostError> { Err(err("offline", "the app is not ready")) }
}

static ENV: OnceLock<Arc<dyn HostEnv>> = OnceLock::new();

/// The app's environment, set once at start before any plugin is loaded.
pub fn install_env(env: Arc<dyn HostEnv>) { let _ = ENV.set(env); }

pub fn env() -> Arc<dyn HostEnv> { ENV.get().cloned().unwrap_or_else(|| Arc::new(NoEnv)) }

// ---------------------------------------------------------------------------------------------------------------
// the per-plugin context and the gate
// ---------------------------------------------------------------------------------------------------------------

/// What a plugin was granted, as the host calls see it ([`params_for`]).
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Params {
    pub id: String,
    pub name: String,
    pub version: String,
    /// Granted permissions (without the implicit ones, which always count).
    pub granted: Vec<String>,
    /// `httpOrigins` the user accepted (empty without `http:fetch`).
    pub origins: Vec<String>,
    /// The accepted origins the consent dialog listed as inside the user's network.
    pub private_hosts: Vec<String>,
    /// The manifest's `settings` schema.
    pub settings: Value,
    /// The hooks the manifest declares (a message is sent only for these, see [`HostCtx::hears`]).
    pub hooks: Vec<String>,
}

/// The [`Params`] of a plugin from its manifest and its grant: only what both name counts.
pub fn params_for(id: &str, name: &str, version: &str, permissions: &[String], origins: &[String], settings: &Value, g: &Grant) -> Params {
    let granted: Vec<String> = permissions.iter().filter(|p| g.granted.contains(p)).cloned().collect();
    let origins: Vec<String> = if granted.iter().any(|p| p == "http:fetch") {
        origins.iter().filter(|o| g.origins.contains(o)).cloned().collect()
    } else { vec![] };
    let private_hosts = g.private_hosts.iter().filter(|o| origins.contains(o)).cloned().collect();
    Params { id: id.into(), name: name.into(), version: version.into(), granted, origins, private_hosts, settings: settings.clone(), hooks: vec![] }
}

#[derive(Default)]
struct Rate {
    start: Option<Instant>,
    window: u64,
    count: u32,
    over: bool,
    row: u32,
}

/// One loaded API 2 plugin as the host calls see it. The host table points at it for the rest of the process.
pub struct HostCtx {
    p: Params,
    /// The plugin's name made fit for a toast or dialog line.
    shown_name: String,
    env: Arc<dyn HostEnv>,
    disabled: AtomicBool,
    tripped: AtomicBool,
    in_init: AtomicBool,
    dialog_open: AtomicBool,
    rate: Mutex<Rate>,
    toasts: Mutex<VecDeque<Instant>>,
    dialogs: Mutex<VecDeque<Instant>>,
    logs: Mutex<(Option<Instant>, u32)>,
    /// The messages to the plugin (B4): the queue to its host thread, what is in flight, the hung mark. Dropping the
    /// queue tells the host thread to call `shutdown` and end: whatever disables the plugin (the user, the failure
    /// limit, an init given up on) lets it go.
    msgs: Mutex<Msgs>,
    /// Woken when a message changes phase, the plugin hangs or is switched off (the deadline watcher waits on it).
    msgs_cv: std::sync::Condvar,
    /// Failed messages in a row (PLUGINS.md "the failure limit").
    fails: std::sync::atomic::AtomicU32,
    /// No message is held longer than this (ms) in all, counted from when it was queued (or its budget when longer).
    hold_cap_ms: std::sync::atomic::AtomicU64,
}

/// Where one message to the plugin is.
#[derive(Clone, Copy, Debug, PartialEq)]
enum Phase {
    /// waiting in the plugin's queue
    Queued,
    /// inside `barbican_plugin_handle` since then
    Running(Instant),
    /// the plugin answered `deferred` to the `handle` call that started then; its result comes with `post`
    Deferred(Instant),
    /// over its deadline (or cancelled) while `handle` still runs; its answer is dropped when it comes
    Overrun,
}

struct Live {
    budget: Duration,
    queued: Instant,
    phase: Phase,
    done: Option<Done>,
    /// Checks an answer before it counts: an answer it refuses is a failure (an invalid reply).
    check: Option<Check>,
    /// A hook message: only its answer shows the plugin works again (resets the failure count). The answer to a
    /// `cancel` or `settings` message does not, or a plugin could answer those to undo its own timeouts.
    hook: bool,
    /// An observer's message (a hook nobody waits for): the first to go when too many wait ([`MAX_QUEUED`]).
    droppable: bool,
    /// A `result` the plugin posted before its `handle` returned `deferred`: it ends the message when it does.
    early: Option<Value>,
    /// The envelope, until the host thread hands it to `handle` (the queue carries only the id, so a dropped
    /// message frees its text at once).
    text: Option<String>,
}

#[derive(Default)]
struct Msgs {
    next: u64,
    live: std::collections::HashMap<u64, Live>,
    /// The message whose `handle` ran past its deadline and has not returned (PLUGINS.md "Hung plugin").
    hung: Option<u64>,
    queue: Option<mpsc::Sender<u64>>,
    /// The deadline watcher runs (it ends once nothing is in flight).
    watching: bool,
}

/// How one message to a plugin ended (PLUGINS.md "Threading, deadlines and failures").
#[derive(Clone, Debug, PartialEq)]
pub enum Outcome {
    /// `ok: true`: its `result` (null for a NULL reply or a reply without one).
    Answer(Value),
    /// An error reply, a panic, a malformed or over-size reply: one failure.
    Failed(String),
    /// `handle` (or the deferred result) missed the deadline: one failure.
    TimedOut,
    /// Never handed to the plugin: it was busy with an earlier message past this one's budget, it is hung, or the
    /// message was too large. Not a failure: the plugin never saw it.
    Dropped(String),
    /// The plugin was switched off before it answered.
    Disabled,
    /// The plugin is switched on but does not run: the failure limit switched it off for the session, or it failed to
    /// load or start (the send check counts it as a plugin that cannot check: PLUGINS.md "What B4 built").
    Tripped,
    /// The host cancelled it (the user pressed Cancel).
    Cancelled,
}

impl Outcome {
    /// Why a message gave no usable answer, for the user (None for an answer).
    pub fn problem(&self) -> Option<String> {
        match self {
            Outcome::Answer(_) => None,
            Outcome::Failed(e) => Some(format!("it failed: {}", shown(e, 120))),
            Outcome::TimedOut => Some("it did not answer in time".into()),
            Outcome::Dropped(why) => Some(why.clone()),
            Outcome::Disabled => Some("it was switched off".into()),
            Outcome::Tripped => Some("it is not running: it failed to start, or repeated failures switched it off".into()),
            Outcome::Cancelled => Some("the check was cancelled".into()),
        }
    }
}

/// Told how a message ended; called once, on another thread (or at once on the caller's when it cannot be queued).
pub type Done = Box<dyn FnOnce(Outcome) + Send>;
/// Checks (and may reshape) a hook's answer: `Err` makes it an invalid reply, which counts as a failure.
pub type Check = Box<dyn FnOnce(Value) -> Result<Value, String> + Send>;

/// What `handle` (or a deferred `post`) answered, read.
enum Parsed { Answer(Value), Deferred, Failed(String) }

/// The longest time a message is held (Bulwark `HELD_HOOK_DEADLINE_MS`, PLUGINS.md "Hard cap").
pub const HOLD_CAP: Duration = Duration::from_secs(600);
/// Budget of the host's own messages that are not hooks (`settings`, `cancel`).
pub const MESSAGE_BUDGET: Duration = Duration::from_secs(5);
/// Most messages waiting for one plugin. Past it the oldest waiting observer is dropped (not a failure: the plugin
/// never saw it); with no observer waiting, the new message is.
pub const MAX_QUEUED: usize = 64;

/// The hooks this build fires (`init_json.features.hooks`); the rest are delivered from later steps on. Mail and the
/// composer (ROADMAP Phase 5 B4, first part).
pub const FIRED_HOOKS: [&str; 11] = ["onEmailOpen", "onEmailClose", "onEmailSelectionChange", "onMailboxChange",
    "onComposerOpen", "onComposerClose", "onDraftChange", "onDraftAutoSave", "onBeforeEmailSend", "onTransformOutgoingEmail",
    "onAfterEmailSend"];

/// A hook's budget for an API 2 message: the catalogue's (aliases resolved), else 5 s. Not capped at API 1's 10 s:
/// the UI does not wait for it.
pub fn hook_budget(hook: &str) -> Duration {
    let h = crate::plugin_catalog::lookup_hook(crate::plugin_catalog::resolve_alias(hook));
    Duration::from_millis(h.map_or(5000, |h| h.budget_ms) as u64)
}

impl HostCtx {
    pub fn new(p: Params, env: Arc<dyn HostEnv>) -> HostCtx {
        let n = shown(&p.name, 60).trim().to_string();
        let shown_name = if n.is_empty() { p.id.clone() } else { n };
        HostCtx { p, shown_name, env, disabled: AtomicBool::new(false), tripped: AtomicBool::new(false), in_init: AtomicBool::new(false),
            dialog_open: AtomicBool::new(false), rate: Mutex::new(Rate::default()), toasts: Mutex::new(VecDeque::new()),
            dialogs: Mutex::new(VecDeque::new()), logs: Mutex::new((None, 0)), msgs: Mutex::new(Msgs::default()),
            msgs_cv: std::sync::Condvar::new(), fails: std::sync::atomic::AtomicU32::new(0),
            hold_cap_ms: std::sync::atomic::AtomicU64::new(HOLD_CAP.as_millis() as u64) }
    }

    pub fn is_disabled(&self) -> bool { self.disabled.load(Ordering::SeqCst) }
    /// Switched off by the failure limit (not by the user).
    pub fn is_tripped(&self) -> bool { self.tripped.load(Ordering::SeqCst) }
    /// From now on every call answers `disabled` and `post` -3, and every message in flight ends as `Disabled`.
    /// The host thread (if any) is told to call `shutdown` and end.
    pub fn disable(&self) {
        self.disabled.store(true, Ordering::SeqCst);
        let dones: Vec<Done> = {
            let mut g = lock(&self.msgs);
            g.queue.take();
            g.hung = None;
            g.live.drain().filter_map(|(_, l)| l.done).collect()
        };
        self.msgs_cv.notify_all();
        for d in dones { d(Outcome::Disabled); }
    }

    /// The plugin's name as the host shows it.
    pub fn shown_name(&self) -> &str { &self.shown_name }
    /// The plugin's id.
    pub fn id(&self) -> &str { &self.p.id }
    /// Does the plugin hold this permission (implicit ones count)? Hook payloads are cut to what it holds.
    pub fn holds_permission(&self, perm: &str) -> bool { self.holds(perm) }

    /// Tests elsewhere: as if the failure limit was reached.
    #[cfg(test)]
    pub(crate) fn trip_for_test(&self) { self.trip("test"); }

    /// Tests: the envelope text the host holds for messages not yet handed to the plugin (bytes).
    #[cfg(test)]
    pub(crate) fn waiting_bytes(&self) -> usize { lock(&self.msgs).live.values().filter_map(|l| l.text.as_ref()).map(String::len).sum() }

    /// Tests: a shorter hard cap.
    #[cfg(test)]
    pub(crate) fn set_hold_cap(&self, cap: Duration) { self.hold_cap_ms.store(cap.as_millis() as u64, Ordering::SeqCst); }

    fn trip(&self, why: &str) {
        self.disable();
        if !self.tripped.swap(true, Ordering::SeqCst) {
            crate::plugins::push_notice(format!("Plugin {} was switched off for this session after {MAX_FAILURES} failures in a row (last: {why}).", self.p.name));
        }
    }

    fn holds(&self, perm: &str) -> bool {
        IMPLICIT_PERMISSIONS.contains(&perm) || self.p.granted.iter().any(|g| g == perm)
    }

    /// One `host->call` envelope in, the reply envelope out (never fails). A call made `in` a message that is no
    /// longer being handled (past its deadline, cancelled, answered) fails with `cancelled`.
    pub fn request(&self, raw: &[u8]) -> String {
        let reply = match self.parse(raw) {
            Ok((_, _, Some(m))) if !self.handling(m) => Err(err("cancelled", "the message this call was made in is over")),
            Ok((method, args, _)) => self.call(&method, &args),
            Err(e) => Err(e),
        };
        envelope(reply)
    }

    fn parse(&self, raw: &[u8]) -> Result<(String, Value, Option<u64>), HostError> {
        if self.is_disabled() { return Err(err("disabled", "the plugin is switched off")); }
        if raw.len() > MAX_ENVELOPE { return Err(err("bad_request", "the request is larger than 4 MiB")); }
        let text = std::str::from_utf8(raw).map_err(|_| err("bad_request", "the request is not UTF-8"))?;
        let v: Value = serde_json::from_str(text).map_err(|e| err("bad_request", format!("the request is not JSON: {e}")))?;
        if v["v"].as_u64() != Some(2) { return Err(err("bad_request", "\"v\" must be 2")); }
        let method = v["method"].as_str().ok_or_else(|| err("bad_request", "\"method\" is missing"))?.to_string();
        let args = match &v["args"] {
            Value::Null => json!([]),
            a @ Value::Array(_) => a.clone(),
            _ => return Err(err("bad_request", "\"args\" must be a list")),
        };
        let within = match &v["in"] {
            Value::Null => None,
            x => Some(x.as_u64().ok_or_else(|| err("bad_request", "\"in\" must be a message id"))?),
        };
        Ok((method, args, within))
    }

    /// One host call through the gate (module docs).
    pub fn call(&self, method: &str, args: &Value) -> Result<Value, HostError> { self.call_at(method, args, Instant::now()) }

    fn call_at(&self, method: &str, args: &Value, now: Instant) -> Result<Value, HostError> {
        use crate::plugin_catalog::{lookup_host_method, Support};
        if self.is_disabled() { return Err(err("disabled", "the plugin is switched off")); }
        self.rate_check(now)?;
        let m = lookup_host_method(method).filter(|m| m.support != Support::Skip)
            .ok_or_else(|| err("unsupported", format!("{} is not a Barbican host method", shown(method, 64))))?;
        if m.permission != "NO_STATIC_GATE" && !self.holds(m.permission) {
            return Err(err("denied", format!("{method} needs the permission {}, which was not granted", m.permission)));
        }
        let arg = |i: usize| args.get(i).unwrap_or(&Value::Null);
        match method {
            "app.getInfo" => Ok(json!({"product": "Barbican", "version": env!("CARGO_PKG_VERSION"), "os": std::env::consts::OS, "pluginApi": ABI})),
            "plugin.getConfig" => Ok(effective_settings(&self.p.settings, &self.env.stored_settings(&self.p.id))),
            "storage.get" | "storage.set" | "storage.remove" | "storage.keys" => {
                let a = self.env.account().ok_or_else(|| err("not_found", "nobody is signed in"))?;
                let env = &self.env;
                let signed_in = |k: &str| env.account().is_some_and(|x| x.login_key == k);
                let s = Storage { root: self.env.data_dir(), signed_in: &signed_in };
                let key = || arg(0).as_str().ok_or_else(|| err("bad_request", "the key must be text"));
                match method {
                    "storage.get" => s.get(&self.p.id, &a.login_key, key()?),
                    "storage.set" => s.set(&self.p.id, &a.login_key, key()?, arg(1)).map(|_| Value::Null),
                    "storage.remove" => s.remove(&self.p.id, &a.login_key, key()?).map(|_| Value::Null),
                    _ => s.keys(&self.p.id, &a.login_key).map(|k| json!(k)),
                }
            }
            "toast.info" | "toast.success" | "toast.warning" | "toast.error" => {
                let text = toast_text(arg(0));
                if text.is_empty() { return Err(err("bad_request", "a toast needs text")); }
                if !within(&self.toasts, now, Duration::from_secs(10), TOASTS_PER_TEN_SECONDS) {
                    return Err(err("rate_limited", format!("at most {TOASTS_PER_TEN_SECONDS} toasts in ten seconds")));
                }
                self.env.toast(&method[6..], &format!("{}: {text}", self.shown_name));
                Ok(Value::Null)
            }
            "ui.alert" | "ui.confirm" => self.dialog(method == "ui.confirm", arg(0), now),
            "http.fetch" => self.fetch(arg(0), arg(1)),
            "user.getSelf" => {
                let a = self.env.account().ok_or_else(|| err("not_found", "nobody is signed in"))?;
                Ok(json!({"accountId": a.account_id, "email": a.login, "name": a.name, "login": a.login, "server": a.server}))
            }
            "user.getAccounts" => {
                let a = self.env.account().ok_or_else(|| err("not_found", "nobody is signed in"))?;
                Ok(json!([{"id": a.account_id, "label": a.name, "serverUrl": a.server, "username": a.login, "displayName": a.name, "email": a.login}]))
            }
            _ => Err(err("unsupported", format!("{method} is not built in this version of Barbican"))),
        }
    }

    /// [`CALLS_PER_SECOND`] per one-second window. A plugin over it in [`MAX_FAILURES`] windows in a row is switched
    /// off for the session; a window within the budget (or without calls) ends the row.
    fn rate_check(&self, now: Instant) -> Result<(), HostError> {
        let mut r = lock(&self.rate);
        let start = *r.start.get_or_insert(now);
        let w = now.saturating_duration_since(start).as_secs();
        if w != r.window {
            if !(r.over && w == r.window + 1) { r.row = 0; }
            (r.window, r.count, r.over) = (w, 0, false);
        }
        r.count += 1;
        if r.count <= CALLS_PER_SECOND { return Ok(()); }
        if !r.over {
            r.over = true;
            r.row += 1;
            if r.row >= MAX_FAILURES {
                drop(r);
                self.trip(&format!("more than {CALLS_PER_SECOND} host calls a second"));
                return Err(err("disabled", "the plugin was switched off for making too many host calls"));
            }
        }
        Err(err("rate_limited", format!("more than {CALLS_PER_SECOND} host calls in a second")))
    }

    fn dialog(&self, confirm: bool, opts: &Value, now: Instant) -> Result<Value, HostError> {
        // the UI thread waits for init (the load budget), so a dialog then could never be answered in time
        if self.in_init.load(Ordering::SeqCst) { return Err(err("busy", "no dialogs during barbican_plugin_init")); }
        let d = dialog_text(opts, &self.shown_name, confirm);
        if self.dialog_open.swap(true, Ordering::SeqCst) { return Err(err("busy", "another dialog of this plugin is open")); }
        let r = if !within(&self.dialogs, now, Duration::from_secs(60), DIALOGS_PER_MINUTE) {
            Err(err("rate_limited", format!("at most {DIALOGS_PER_MINUTE} dialogs a minute")))
        } else {
            self.env.dialog(confirm, &d.title, &d.text, &d.ok, &d.cancel)
        };
        self.dialog_open.store(false, Ordering::SeqCst);
        let yes = r?;
        if self.is_disabled() { return Err(err("disabled", "the plugin was switched off")); }
        Ok(if confirm { json!(yes) } else { Value::Null })
    }

    fn fetch(&self, url: &Value, init: &Value) -> Result<Value, HostError> {
        let deadline = Instant::now() + FETCH_TIMEOUT;
        // the system resolver has no time limit of its own: it runs on a worker, and the fetch's deadline counts
        let resolve = |h: &str, p: u16| {
            let (env, h) = (self.env.clone(), h.to_string());
            before_deadline(deadline, move || env.resolve(&h, p)).unwrap_or_else(|| Err(std::io::Error::other("timed out")))
        };
        let send = |hop: &Hop, req: &Outgoing, deadline: Instant| self.env.send(hop, req, deadline);
        fetch_with(url, init, &self.p.origins, &self.p.private_hosts, &resolve, &send, deadline)
    }

    /// `host->log`: plain, one line, at most 300 characters, [`LOG_LINES_PER_SECOND`].
    fn log(&self, level: i32, text: &str) {
        let now = Instant::now();
        {
            let mut l = lock(&self.logs);
            if l.0.is_none_or(|t| now.duration_since(t) >= Duration::from_secs(1)) { *l = (Some(now), 0); }
            l.1 += 1;
            if l.1 > LOG_LINES_PER_SECOND { return; }
        }
        let lvl = ["debug", "info", "warn", "error"][level.clamp(0, 3) as usize];
        self.env.log(&format!("plugin {} [{lvl}]: {}", self.p.id, shown(text, 300)));
    }

    /// `host->post`: 0 accepted, -1 not an envelope, -3 switched off. A `result` for a message the plugin answered
    /// `deferred` ends that message; one for any other message (answered, past its deadline, cancelled, unknown) is
    /// accepted and dropped. Other types are accepted and dropped (nothing uses them yet).
    fn post(&self, raw: &[u8]) -> i32 {
        if self.is_disabled() { return -3; }
        if raw.len() > MAX_ENVELOPE { return -1; }
        let Some(v) = std::str::from_utf8(raw).ok().and_then(|t| serde_json::from_str::<Value>(t).ok()) else { return -1 };
        if v["v"].as_u64() != Some(2) || !v["type"].is_string() { return -1; }
        if v["type"] == "result" {
            if let Some(id) = v["id"].as_u64() { self.deferred_result(id, &v); }
        }
        0
    }

    // ----- messages to the plugin (B4) -----

    /// Does the plugin hear this hook? It is fired by this build ([`FIRED_HOOKS`]), catalogued and not skipped, the
    /// manifest declares it (or its alias) and the hook's permission (or its second one) was granted. A message for
    /// any other hook is never sent.
    pub fn hears(&self, hook: &str) -> bool { !self.is_disabled() && self.would_hear(hook) }

    /// [`HostCtx::hears`], switched off or not (a plugin the failure limit switched off still guards the send).
    pub fn would_hear(&self, hook: &str) -> bool {
        use crate::plugin_catalog::{lookup_hook, resolve_alias, Support};
        let want = resolve_alias(hook);
        let Some(h) = lookup_hook(want).filter(|h| h.support != Support::Skip) else { return false };
        if !FIRED_HOOKS.contains(&h.name) || !self.p.hooks.iter().any(|d| resolve_alias(d) == want) { return false; }
        self.holds(h.permission) || crate::plugins::ALSO_PERMISSION.iter().any(|(n, p)| *n == h.name && self.holds(p))
    }

    /// The spelling of `hook` the manifest declared (the plugin is called with it).
    fn declared(&self, hook: &str) -> String {
        let want = crate::plugin_catalog::resolve_alias(hook);
        self.p.hooks.iter().find(|h| crate::plugin_catalog::resolve_alias(h) == want).cloned().unwrap_or_else(|| want.to_string())
    }

    /// Fire `hook` with `payload` (already cut to what the plugin may see) within `budget` (the catalogue's, see
    /// [`hook_budget`]). `done` hears how it ended. Returns the message id, or None when it was not sent (the plugin
    /// does not hear the hook, is off, hung or the message is too large; `done` then hears why at once).
    pub fn fire(self: &Arc<Self>, hook: &str, payload: Value, budget: Duration, done: Option<Done>) -> Option<u64> {
        self.fire_checked(hook, payload, budget, None, done)
    }

    /// [`HostCtx::fire`], with `check` run on the answer before it counts (transforms: the shape, the size, the
    /// fields that may change).
    pub fn fire_checked(self: &Arc<Self>, hook: &str, payload: Value, budget: Duration, check: Option<Check>, done: Option<Done>) -> Option<u64> {
        if !self.hears(hook) {
            if let Some(d) = done { d(Outcome::Dropped("it does not handle this event".into())); }
            return None;
        }
        let account = (self.holds("email:read") || self.holds("account:read")).then(|| self.env.account()).flatten().map(|a| a.account_id);
        let mut fields = json!({"name": self.declared(hook), "payload": payload});
        if let Some(a) = account { fields["account"] = json!(a); }
        self.send("hook", fields, budget, check, done)
    }

    /// The `settings` message: the plugin's settings changed (`values`, as `plugin.getConfig` gives them).
    pub fn notify_settings(self: &Arc<Self>) -> Option<u64> {
        let values = effective_settings(&self.p.settings, &self.env.stored_settings(&self.p.id));
        self.send("settings", json!({"values": values}), MESSAGE_BUDGET, None, None)
    }

    /// Queue one message of `kind` with `fields` (merged into the envelope).
    fn send(self: &Arc<Self>, kind: &str, fields: Value, budget: Duration, check: Option<Check>, done: Option<Done>) -> Option<u64> {
        let refuse = |done: Option<Done>, o: Outcome| { if let Some(d) = done { d(o); } None };
        if self.is_disabled() { return refuse(done, Outcome::Disabled); }
        let mut g = lock(&self.msgs);
        if g.queue.is_none() { drop(g); return refuse(done, Outcome::Disabled); }
        if g.hung.is_some() {
            drop(g);
            return refuse(done, Outcome::Dropped("it is still busy with an earlier event it did not answer in time".into()));
        }
        g.next += 1;
        let id = g.next;
        let mut env = json!({"v": 2, "id": id, "type": kind, "deadline_ms": budget.as_millis() as u64});
        for (k, v) in fields.as_object().into_iter().flatten() { env[k] = v.clone(); }
        let text = env.to_string();
        if text.len() > MAX_ENVELOPE { drop(g); return refuse(done, Outcome::Dropped("the message is too large to hand to it".into())); }
        if !g.watching {
            let c = self.clone();
            if std::thread::Builder::new().name("barbican-plugin-watch".into()).spawn(move || c.watch()).is_err() {
                drop(g);
                return refuse(done, Outcome::Dropped("could not start a thread".into()));
            }
            g.watching = true;
        }
        let droppable = kind == "hook" && done.is_none();
        let mut bumped = None;
        if g.live.values().filter(|l| l.phase == Phase::Queued).count() >= MAX_QUEUED {
            let oldest = g.live.iter().filter(|(_, l)| l.phase == Phase::Queued && l.droppable).map(|(k, _)| *k).min();
            match oldest {
                Some(k) => bumped = Some(g.live.remove(&k).and_then(|l| l.done)),
                None => { drop(g); return refuse(done, Outcome::Dropped("too many of its events are waiting".into())); }
            }
        }
        g.live.insert(id, Live { budget, queued: Instant::now(), phase: Phase::Queued, done, check, hook: kind == "hook", droppable, early: None, text: Some(text) });
        let sent = g.queue.as_ref().is_some_and(|q| q.send(id).is_ok());
        if !sent {
            let d = g.live.remove(&id).and_then(|l| l.done);
            drop(g);
            return refuse(d, Outcome::Disabled);
        }
        drop(g);
        self.msgs_cv.notify_all();
        if let Some(Some(d)) = bumped { d(Outcome::Dropped("too many of its events were waiting".into())); }
        Some(id)
    }

    /// The host (the user) gives up on a message: it ends as `Cancelled` at once (not a failure). One the plugin is
    /// handling, or answered `deferred`, is followed by a `cancel` message naming it.
    pub fn cancel(self: &Arc<Self>, id: u64) {
        let (done, tell) = {
            let mut g = lock(&self.msgs);
            match g.live.get_mut(&id) {
                None => return,
                Some(l) => match l.phase {
                    Phase::Queued | Phase::Deferred(_) => { let tell = l.phase != Phase::Queued; (g.live.remove(&id).and_then(|l| l.done), tell) }
                    Phase::Running(_) => { l.phase = Phase::Overrun; (l.done.take(), true) }
                    Phase::Overrun => return,
                },
            }
        };
        self.msgs_cv.notify_all();
        if let Some(d) = done { d(Outcome::Cancelled); }
        if tell { self.send("cancel", json!({"ref": id}), MESSAGE_BUDGET, None, None); }
    }

    /// Is the plugin handling message `id` now (inside `handle`, or after `deferred` within its deadline)?
    fn handling(&self, id: u64) -> bool {
        lock(&self.msgs).live.get(&id).is_some_and(|l| matches!(l.phase, Phase::Running(_) | Phase::Deferred(_)))
    }

    /// One message ended for the plugin: a success clears the failure row, [`MAX_FAILURES`] in a row switch it off.
    fn count(&self, ok: bool, why: &str) {
        if ok { self.fails.store(0, Ordering::SeqCst); return; }
        if self.fails.fetch_add(1, Ordering::SeqCst) + 1 >= MAX_FAILURES { self.trip(why); }
    }

    /// The host thread, for message `id`: call `handle` unless the message ended while it waited.
    fn deliver(&self, n: &NativeV2, id: u64) {
        let start = Instant::now();
        let text = {
            let mut g = lock(&self.msgs);
            if self.is_disabled() { return; }
            match g.live.get_mut(&id) {
                Some(l) if l.phase == Phase::Queued => { l.phase = Phase::Running(start); l.text.take().unwrap_or_default() }
                _ => return,
            }
        };
        self.msgs_cv.notify_all();
        let msg = CString::new(text).unwrap_or_default();
        let reply = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| unsafe { (n.handle)(msg.as_ptr()) }));
        let parsed = match reply {
            Err(_) => Parsed::Failed("barbican_plugin_handle panicked".into()),
            Ok(p) if p.is_null() => Parsed::Answer(Value::Null),
            Ok(p) => {
                // SAFETY: a NUL-terminated string the plugin allocated; copied, then freed with its own free
                let bytes = unsafe { CStr::from_ptr(p) }.to_bytes().to_vec();
                let _ = std::panic::catch_unwind(|| unsafe { (n.free)(p) });
                parse_reply(&bytes, id, true)
            }
        };
        let ended = {
            let mut g = lock(&self.msgs);
            if g.hung == Some(id) { g.hung = None; }
            let running = g.live.get(&id).map(|l| matches!(l.phase, Phase::Running(_)));
            match (running, parsed) {
                (Some(true), Parsed::Deferred) => match g.live.get_mut(&id) {
                    // the result was posted before handle returned: it ends the message now
                    Some(l) if l.early.is_some() => {
                        let v = l.early.take().unwrap_or_default();
                        g.live.remove(&id).map(|l| (l, parse_reply(&v.to_string().into_bytes(), id, false)))
                    }
                    Some(l) => { l.phase = Phase::Deferred(start); None }
                    None => None,
                },
                (Some(true), p) => g.live.remove(&id).map(|l| (l, p)),
                // over its deadline or cancelled meanwhile: the answer is dropped
                (Some(false), _) => { g.live.remove(&id); None }
                (None, _) => None,
            }
        };
        self.msgs_cv.notify_all();
        if let Some((l, p)) = ended { self.end(l, p); }
    }

    /// A message the plugin answered: check the answer, count it, tell whoever waits.
    fn end(&self, l: Live, p: Parsed) {
        let o = match p {
            Parsed::Answer(v) => match l.check {
                Some(c) => c(v).map_or_else(|e| Outcome::Failed(format!("an invalid answer: {e}")), Outcome::Answer),
                None => Outcome::Answer(v),
            },
            Parsed::Failed(e) => Outcome::Failed(e),
            Parsed::Deferred => Outcome::Failed("a deferred result cannot be deferred again".into()),
        };
        let failed = matches!(o, Outcome::Failed(_));
        if failed || l.hook { self.count(!failed, &o.problem().unwrap_or_default()); }
        if let Some(d) = l.done { d(o); }
    }

    /// A deferred result arrived with `post`. One posted while `handle` still runs (before it returned `deferred`)
    /// is kept and ends the message when `handle` returns `deferred`; any other answer from `handle` wins over it.
    fn deferred_result(&self, id: u64, v: &Value) {
        let l = {
            let mut g = lock(&self.msgs);
            match g.live.get_mut(&id) {
                Some(l) if matches!(l.phase, Phase::Running(_)) => { if l.early.is_none() { l.early = Some(v.clone()); } return; }
                Some(l) if matches!(l.phase, Phase::Deferred(_)) => g.live.remove(&id),
                _ => return,
            }
        };
        self.msgs_cv.notify_all();
        if let Some(l) = l { self.end(l, parse_reply(&v.to_string().into_bytes(), id, false)); }
    }

    /// The deadline watcher: ends messages that wait or run past their budget, marks the plugin hung, switches it
    /// off at the hard cap. Runs while anything is in flight.
    fn watch(self: Arc<Self>) {
        let mut g = lock(&self.msgs);
        loop {
            let now = Instant::now();
            let off = self.is_disabled();
            let mut ended: Vec<(Option<Done>, Outcome)> = vec![];
            let (mut failed, mut tell, mut trip) = (vec![], vec![], false);
            let mut next: Option<Instant> = None;
            let mut soon = |t: Instant| { next = Some(next.map_or(t, |n: Instant| n.min(t))); };
            let ids: Vec<u64> = g.live.keys().copied().collect();
            for id in ids {
                let hung = g.hung;
                let Some(l) = g.live.get_mut(&id) else { continue };
                let cap = l.queued + Duration::from_millis(self.hold_cap_ms.load(Ordering::SeqCst)).max(l.budget);
                match l.phase {
                    _ if off => { ended.push((l.done.take(), Outcome::Disabled)); g.live.remove(&id); }
                    Phase::Queued if hung.is_some() => {
                        ended.push((l.done.take(), Outcome::Dropped("it is still busy with an earlier event it did not answer in time".into())));
                        g.live.remove(&id);
                    }
                    Phase::Queued if now >= l.queued + l.budget => {
                        ended.push((l.done.take(), Outcome::Dropped("it was still busy with an earlier event".into())));
                        g.live.remove(&id);
                    }
                    Phase::Queued => soon(l.queued + l.budget),
                    Phase::Running(s) if now >= s + l.budget => {
                        ended.push((l.done.take(), Outcome::TimedOut));
                        l.phase = Phase::Overrun;
                        failed.push(format!("did not answer within {} ms", l.budget.as_millis()));
                        g.hung = Some(id);
                        soon(cap);
                    }
                    Phase::Running(s) => soon(s + l.budget),
                    Phase::Deferred(s) if now >= s + l.budget => {
                        ended.push((l.done.take(), Outcome::TimedOut));
                        failed.push(format!("its deferred result did not come within {} ms", l.budget.as_millis()));
                        tell.push(id);
                        g.live.remove(&id);
                    }
                    Phase::Deferred(s) => soon(s + l.budget),
                    // still inside handle at the hard cap: a plugin that hangs cannot stay on
                    Phase::Overrun if now >= cap => { trip = true; g.live.remove(&id); }
                    Phase::Overrun => soon(cap),
                }
            }
            let idle = g.live.is_empty();
            if idle { g.watching = false; }
            if !ended.is_empty() || !failed.is_empty() || trip || idle {
                drop(g);
                self.msgs_cv.notify_all();
                for (d, o) in ended { if let Some(d) = d { d(o); } }
                for why in failed { self.count(false, &why); }
                for id in tell { self.send("cancel", json!({"ref": id}), MESSAGE_BUDGET, None, None); }
                if trip { self.trip("it stayed inside one event for 10 minutes"); }
                if idle { return; }
                g = lock(&self.msgs);
                continue;
            }
            let wait = next.map_or(Duration::from_secs(1), |t| t.saturating_duration_since(now)).max(Duration::from_millis(1));
            g = self.msgs_cv.wait_timeout(g, wait).map(|(g, _)| g).unwrap_or_else(|e| e.into_inner().0);
        }
    }
}

/// Read a reply envelope to message `id` (from `handle` when `from_handle`, else a deferred `post`).
fn parse_reply(bytes: &[u8], id: u64, from_handle: bool) -> Parsed {
    if bytes.len() > MAX_ENVELOPE { return Parsed::Failed("the reply is larger than 4 MiB".into()); }
    let Ok(text) = std::str::from_utf8(bytes) else { return Parsed::Failed("the reply is not UTF-8".into()) };
    let v: Value = match serde_json::from_str(text) {
        Ok(v @ Value::Object(_)) => v,
        _ => return Parsed::Failed("the reply is not a JSON envelope".into()),
    };
    if v["v"].as_u64() != Some(2) { return Parsed::Failed("the reply's \"v\" is not 2".into()); }
    if v["id"].as_u64() != Some(id) { return Parsed::Failed("the reply names another message".into()); }
    if from_handle && v["deferred"] == json!(true) { return Parsed::Deferred; }
    match v["ok"].as_bool() {
        Some(true) => Parsed::Answer(v.get("result").cloned().unwrap_or(Value::Null)),
        Some(false) => Parsed::Failed(format!("{}: {}", shown(v["error"]["code"].as_str().unwrap_or("error"), 40),
            shown(v["error"]["message"].as_str().unwrap_or(""), 200))),
        None => Parsed::Failed("the reply has no \"ok\"".into()),
    }
}

/// Keep at most `max` times within `span` before `now`; true (and `now` noted) when there is room.
fn within(q: &Mutex<VecDeque<Instant>>, now: Instant, span: Duration, max: usize) -> bool {
    let mut q = lock(q);
    while q.front().is_some_and(|t| now.saturating_duration_since(*t) >= span) { q.pop_front(); }
    if q.len() >= max { return false; }
    q.push_back(now);
    true
}

fn envelope(r: Result<Value, HostError>) -> String {
    match r {
        Ok(v) => json!({"v": 2, "ok": true, "result": v}).to_string(),
        Err(e) => json!({"v": 2, "ok": false, "error": {"code": e.code, "message": shown(&e.message, 300)}}).to_string(),
    }
}

// ---------------------------------------------------------------------------------------------------------------
// toast and dialog text
// ---------------------------------------------------------------------------------------------------------------

/// A toast's text from a plugin: one plain line, at most [`MAX_TOAST`] characters.
pub fn toast_text(v: &Value) -> String {
    let raw = match v { Value::String(s) => s.as_str(), _ => "" };
    // a short prefix only: a huge string is not walked in full
    let head: String = raw.chars().take(MAX_TOAST * 4).collect();
    shown(&head, MAX_TOAST).trim().to_string()
}

/// A dialog as the host draws it.
#[derive(Debug, PartialEq)]
pub struct DialogText {
    pub title: String,
    pub text: String,
    pub ok: String,
    pub cancel: String,
}

/// Bulwark's `ui.alert({title, message, confirmLabel})` and `ui.confirm({..., cancelLabel})` (a plain string is the
/// message). Every part is made plain; the message keeps at most 30 lines of 500 characters, 2000 in all, and ends
/// with a line naming the plugin that the plugin's text cannot reach.
pub fn dialog_text(opts: &Value, plugin: &str, confirm: bool) -> DialogText {
    let field = |k: &str| match opts { Value::String(s) if k == "message" => Some(s.as_str()), _ => opts[k].as_str() };
    let one = |k: &str, max: usize, default: &str| {
        let s = shown(&field(k).unwrap_or("").chars().take(max * 4).collect::<String>(), max).trim().to_string();
        if s.is_empty() { default.to_string() } else { s }
    };
    let title = one("title", 100, plugin);
    let msg: String = field("message").unwrap_or("").chars().take(8000).collect();
    // line by line: `shown` would turn the breaks into spaces
    let lines: Vec<String> = msg.lines().take(30).map(|l| shown(l, 500).trim_end().to_string()).collect();
    let mut text = lines.join("\n").trim().to_string();
    if text.chars().count() > 2000 { text = format!("{}…", text.chars().take(2000).collect::<String>()); }
    let ok = one("confirmLabel", 40, "OK");
    let mut cancel = one("cancelLabel", 40, "Cancel");
    if confirm && cancel == ok { cancel = "Cancel".into(); }
    let ok = if confirm && ok == cancel { "OK".to_string() } else { ok };
    let from = format!("This message is from the plugin {plugin}.");
    DialogText { title, text: if text.is_empty() { from } else { format!("{text}\n\n{from}") }, ok, cancel }
}

// ---------------------------------------------------------------------------------------------------------------
// storage
// ---------------------------------------------------------------------------------------------------------------

/// Storage of every plugin, below `root` (`plugin-data`).
pub struct Storage<'a> {
    pub root: PathBuf,
    /// Is this login still the one signed in? Asked under the lock before a write, so a write that waited for a
    /// sign-out's wipe does not put the file back.
    pub signed_in: &'a dyn Fn(&str) -> bool,
}

/// One storage operation at a time (reads, writes and the size check of a set are one step).
static STORAGE_LOCK: Mutex<()> = Mutex::new(());

/// Held while sign-out wipes `plugin-data`, so a write in flight finishes before the wipe and none lands after it
/// starts.
pub fn storage_lock() -> std::sync::MutexGuard<'static, ()> { lock(&STORAGE_LOCK) }

/// The file a login's storage is kept in: a hash of its login key, so the folder names no address.
pub fn login_file(login_key: &str) -> String {
    use sha2::{Digest, Sha256};
    let h = Sha256::digest(login_key.as_bytes());
    format!("{}.json", h.iter().take(16).map(|b| format!("{b:02x}")).collect::<String>())
}

impl Storage<'_> {
    fn path(&self, plugin: &str, login: &str) -> Result<PathBuf, HostError> {
        // the id names a folder: only a checked package id ever does
        if !crate::plugins::valid_id(plugin) { return Err(err("denied", "no storage for this plugin")); }
        if login.is_empty() { return Err(err("not_found", "nobody is signed in")); }
        Ok(self.root.join(plugin).join(login_file(login)))
    }

    /// The stored map. Only a missing file is empty storage: a file that cannot be read (locked by a virus scanner,
    /// say) or does not hold a JSON object is an error, so a following set or remove never writes over it.
    fn read(path: &Path) -> Result<serde_json::Map<String, Value>, HostError> {
        let bytes = match std::fs::read(path) {
            Ok(b) => b,
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Ok(Default::default()),
            Err(e) => return Err(err("internal", format!("could not read the plugin's storage: {e}"))),
        };
        match serde_json::from_slice::<Value>(&bytes) {
            Ok(Value::Object(m)) => Ok(m),
            _ => Err(err("internal", "the plugin's storage file is damaged")),
        }
    }

    fn check_key(key: &str) -> Result<(), HostError> {
        if (1..=MAX_STORAGE_KEY).contains(&key.chars().count()) { Ok(()) } else { Err(err("bad_request", "a key is 1-256 characters")) }
    }

    pub fn get(&self, plugin: &str, login: &str, key: &str) -> Result<Value, HostError> {
        Self::check_key(key)?;
        let p = self.path(plugin, login)?;
        let _g = lock(&STORAGE_LOCK);
        Ok(Self::read(&p)?.remove(key).unwrap_or(Value::Null))
    }

    pub fn set(&self, plugin: &str, login: &str, key: &str, value: &Value) -> Result<(), HostError> {
        Self::check_key(key)?;
        let p = self.path(plugin, login)?;
        let _g = lock(&STORAGE_LOCK);
        if !(self.signed_in)(login) { return Err(err("not_found", "nobody is signed in")); }
        let mut m = Self::read(&p)?;
        m.insert(key.to_string(), value.clone());
        let bytes = serde_json::to_vec(&Value::Object(m)).map_err(|e| err("internal", e.to_string()))?;
        if bytes.len() > STORAGE_QUOTA_BYTES {
            return Err(err("quota", format!("plugin storage is full ({} KiB per login)", STORAGE_QUOTA_BYTES >> 10)));
        }
        write_atomic(&p, &bytes).map_err(|e| err("internal", format!("could not save: {e}")))
    }

    pub fn remove(&self, plugin: &str, login: &str, key: &str) -> Result<(), HostError> {
        Self::check_key(key)?;
        let p = self.path(plugin, login)?;
        let _g = lock(&STORAGE_LOCK);
        if !(self.signed_in)(login) { return Err(err("not_found", "nobody is signed in")); }
        let mut m = Self::read(&p)?;
        if m.remove(key).is_none() { return Ok(()); }
        let bytes = serde_json::to_vec(&Value::Object(m)).map_err(|e| err("internal", e.to_string()))?;
        write_atomic(&p, &bytes).map_err(|e| err("internal", format!("could not save: {e}")))
    }

    pub fn keys(&self, plugin: &str, login: &str) -> Result<Vec<String>, HostError> {
        let p = self.path(plugin, login)?;
        let _g = lock(&STORAGE_LOCK);
        Ok(Self::read(&p)?.keys().cloned().collect())
    }
}

/// Write `bytes` to a temporary file next to `path`, flush it, then rename it over `path`: a crash leaves the old
/// file or the new one, never half of one.
pub fn write_atomic(path: &Path, bytes: &[u8]) -> std::io::Result<()> {
    use std::io::Write;
    let dir = path.parent().ok_or_else(|| std::io::Error::other("no folder"))?;
    std::fs::create_dir_all(dir)?;
    let name = path.file_name().map(|n| n.to_string_lossy().to_string()).unwrap_or_default();
    let tmp = dir.join(format!(".{name}.{}.tmp", std::process::id()));
    let r = (|| {
        let mut f = std::fs::File::create(&tmp)?;
        f.write_all(bytes)?;
        f.sync_all()?;
        drop(f);
        std::fs::rename(&tmp, path)
    })();
    if r.is_err() { let _ = std::fs::remove_file(&tmp); }
    r
}

// ---------------------------------------------------------------------------------------------------------------
// settings
// ---------------------------------------------------------------------------------------------------------------

/// Is `v` a valid value for the schema field `f` (secret fields never are: they are not kept here)?
fn valid_setting(f: &Value, v: &Value) -> bool {
    match f["type"].as_str().unwrap_or("") {
        "boolean" => v.is_boolean(),
        "string" => v.as_str().is_some_and(|s| s.chars().count() <= 1000),
        "number" => v.as_f64().is_some_and(|n| n.is_finite() && f["min"].as_f64().is_none_or(|m| n >= m) && f["max"].as_f64().is_none_or(|m| n <= m)),
        "select" => v.as_str().is_some_and(|s| f["options"].as_array().is_some_and(|o| o.iter().any(|x| x.as_str() == Some(s)))),
        _ => false,
    }
}

fn default_setting(f: &Value) -> Value {
    if valid_setting(f, &f["default"]) { return f["default"].clone(); }
    match f["type"].as_str().unwrap_or("") {
        "boolean" => json!(false),
        // the lowest allowed value, else 0 when it is allowed, else the highest
        "number" => json!(f["min"].as_f64().unwrap_or_else(|| f["max"].as_f64().map_or(0.0, |m| m.min(0.0)))),
        "select" => f["options"][0].clone(),
        _ => json!(""),
    }
}

/// The plugin's settings as it reads them (`plugin.getConfig`, `init_json.settings`): every non-secret field of the
/// schema, the user's value when it is valid for the field, else its default.
pub fn effective_settings(schema: &Value, stored: &Value) -> Value {
    let mut out = serde_json::Map::new();
    for (k, f) in schema.as_object().into_iter().flatten() {
        if f["type"] == "secret" { continue; }
        let v = stored.get(k).filter(|v| valid_setting(f, v)).cloned().unwrap_or_else(|| default_setting(f));
        out.insert(k.clone(), v);
    }
    Value::Object(out)
}

/// One Settings > Add-ons row for a plugin setting (the key is `pluginSetting:<id>:<field>`).
#[derive(Clone, Debug, PartialEq)]
pub enum SettingRow {
    Toggle { key: String, label: String, desc: String, on: bool },
    Text { key: String, label: String, desc: String, value: String },
    Select { key: String, label: String, desc: String, options: Vec<String>, index: i32 },
}

/// The rows of a plugin's settings, in schema order. Secret fields are left out until `secrets.*` exists.
pub fn setting_rows(id: &str, schema: &Value, stored: &Value) -> Vec<SettingRow> {
    let values = effective_settings(schema, stored);
    let mut rows = vec![];
    for (k, f) in schema.as_object().into_iter().flatten() {
        let key = format!("pluginSetting:{id}:{k}");
        let label = shown(f["label"].as_str().unwrap_or(k), 200);
        let mut desc = shown(f["description"].as_str().unwrap_or(""), 500);
        let v = &values[k];
        rows.push(match f["type"].as_str().unwrap_or("") {
            "boolean" => SettingRow::Toggle { key, label, desc, on: v.as_bool().unwrap_or(false) },
            "select" => {
                let options: Vec<String> = f["options"].as_array().into_iter().flatten().filter_map(|o| o.as_str()).map(|o| shown(o, 100)).collect();
                let raw: Vec<&str> = f["options"].as_array().into_iter().flatten().filter_map(|o| o.as_str()).collect();
                let index = raw.iter().position(|o| Some(*o) == v.as_str()).unwrap_or(0) as i32;
                SettingRow::Select { key, label, desc, options, index }
            }
            "number" => {
                let range = match (f["min"].as_f64(), f["max"].as_f64()) {
                    (Some(a), Some(b)) => format!("From {a} to {b}."),
                    (Some(a), None) => format!("At least {a}."),
                    (None, Some(b)) => format!("At most {b}."),
                    _ => String::new(),
                };
                if !range.is_empty() { desc = format!("{desc} {range}").trim().to_string(); }
                SettingRow::Text { key, label, desc, value: v.as_f64().map(fmt_number).unwrap_or_default() }
            }
            "string" => SettingRow::Text { key, label, desc, value: v.as_str().unwrap_or("").to_string() },
            _ => continue,
        });
    }
    rows
}

fn fmt_number(n: f64) -> String { if n.fract() == 0.0 && n.abs() < 1e15 { format!("{}", n as i64) } else { n.to_string() } }

/// The value to store for a settings row change (`raw` is what the row gives: true/false, an option index, text), or
/// `None` when it is not valid for the field (a half-typed number keeps the last good value).
pub fn setting_value(schema: &Value, key: &str, raw: &Value) -> Option<Value> {
    let f = schema.get(key)?;
    let v = match f["type"].as_str()? {
        "boolean" => json!(raw.as_bool()?),
        "select" => f["options"].get(usize::try_from(raw.as_i64()?).ok()?)?.clone(),
        "number" => json!(raw.as_str()?.trim().parse::<f64>().ok()?),
        "string" => json!(raw.as_str()?),
        _ => return None,
    };
    valid_setting(f, &v).then_some(v)
}

// ---------------------------------------------------------------------------------------------------------------
// http.fetch
// ---------------------------------------------------------------------------------------------------------------

/// Bulwark's `originMatchesAllowlist` (`lib/plugin-sandbox/host-api.ts:381`) for a checked origin entry: https, the
/// same port, and the same host, or for `https://*.example.org` exactly one more label (`a.example.org`, not
/// `example.org` or `a.b.example.org`).
pub fn origin_matches(url: &reqwest::Url, entry: &str) -> bool {
    if url.scheme() != "https" { return false; }
    let Some(host) = url.host_str().map(str::to_ascii_lowercase) else { return false };
    let Some(rest) = entry.strip_prefix("https://") else { return false };
    let (ehost, eport) = match rest.rsplit_once(':') {
        Some((h, p)) => (h, p.parse::<u16>().ok()),
        None => (rest, Some(443)),
    };
    if eport != url.port_or_known_default() { return false; }
    match ehost.strip_prefix("*.") {
        Some(suffix) => host.strip_suffix(suffix).and_then(|p| p.strip_suffix('.')).is_some_and(|p| !p.is_empty() && !p.contains('.')),
        None => host == ehost,
    }
}

/// An address `http.fetch` must not reach without the user's private-host consent.
fn is_private_ip(ip: IpAddr) -> bool { crate::feed_guard::is_blocked_ip(ip) || !crate::plugins::ip_is_public(ip) }

/// Check one hop of `http.fetch` (PLUGINS.md `http:fetch`): https, no user name or password, not an IP literal, an
/// accepted origin, the name resolved once. When the name is a private one (`localhost`, `nas`, `*.lan`) or any of
/// its addresses is not public, the hop is allowed only when that exact origin (never a wildcard) was accepted as a
/// private host. The hop then connects to these addresses only.
pub fn plan_hop(url: &reqwest::Url, origins: &[String], private_hosts: &[String], resolve: &dyn Fn(&str, u16) -> std::io::Result<Vec<IpAddr>>) -> Result<Hop, HostError> {
    use crate::feed_guard::{classify_host, HostClass};
    if url.scheme() != "https" { return Err(err("denied", "only https:// addresses can be fetched")); }
    if !url.username().is_empty() || url.password().is_some() { return Err(err("bad_request", "an address with a user name or password")); }
    let host = url.host_str().ok_or_else(|| err("bad_request", "the address has no host"))?.to_string();
    let local_name = match classify_host(&host) {
        HostClass::Ip(_) => return Err(err("denied", "IP addresses cannot be fetched")),
        HostClass::Invalid => return Err(err("bad_request", "not a valid host name")),
        HostClass::LocalName(_) => true,
        HostClass::Name(_) => crate::plugins::is_private_name(&host),
    };
    if !origins.iter().any(|o| origin_matches(url, o)) {
        return Err(err("denied", format!("{} is not one of the sites this plugin may connect to", url.origin().ascii_serialization())));
    }
    let port = url.port_or_known_default().unwrap_or(443);
    let ips = resolve(&host, port).map_err(|_| err("offline", format!("could not find {host}")))?;
    if ips.is_empty() { return Err(err("offline", format!("could not find {host}"))); }
    if local_name || ips.iter().any(|ip| is_private_ip(*ip)) {
        let exact = if port == 443 { format!("https://{host}") } else { format!("https://{host}:{port}") };
        // only an exact entry the user allowed inside the network counts (a wildcard never does), whichever entry
        // of the allow-list matched first
        let same = |o: &String| o == &exact || (port == 443 && o == &format!("{exact}:443"));
        if !private_hosts.iter().any(same) {
            return Err(err("denied", format!("{host} is inside your network, and you did not allow this plugin to reach it")));
        }
    }
    Ok(Hop { url: url.clone(), host, pin: ips.into_iter().map(|ip| SocketAddr::new(ip, port)).collect() })
}

/// Run `f` on a worker thread and wait for it until `deadline`; `None` when it did not finish in time (the worker
/// is left to end on its own).
pub fn before_deadline<T: Send + 'static>(deadline: Instant, f: impl FnOnce() -> T + Send + 'static) -> Option<T> {
    let (tx, rx) = mpsc::channel();
    std::thread::Builder::new().name("barbican-plugin-dns".into()).spawn(move || { let _ = tx.send(f()); }).ok()?;
    rx.recv_timeout(deadline.saturating_duration_since(Instant::now())).ok()
}

/// Request headers a plugin may not set: what the connection owns, and anything that could carry Barbican's or the
/// browser's sign-in.
const DROPPED_HEADERS: [&str; 13] = ["host", "cookie", "cookie2", "content-length", "transfer-encoding", "connection", "keep-alive",
    "upgrade", "te", "trailer", "proxy-authorization", "proxy-connection", "x-jmap-username"];

/// `http.fetch(url, {method, headers, body | bodyBase64})` hop by hop through [`plan_hop`]; `send` makes one request.
/// The answer is `{ok, status, statusText, url, headers, bodyText, bodyBase64}` (Bulwark's shape, with the bytes as
/// base64). A redirect to another origin drops `Authorization`; 303, and 301/302 after a POST, go on as GET without a
/// body.
pub fn fetch_with(url: &Value, init: &Value, origins: &[String], private_hosts: &[String],
    resolve: &dyn Fn(&str, u16) -> std::io::Result<Vec<IpAddr>>,
    send: &dyn Fn(&Hop, &Outgoing, Instant) -> Result<HopReply, HostError>, deadline: Instant) -> Result<Value, HostError> {
    use base64::Engine;
    let b64 = base64::engine::general_purpose::STANDARD;
    let raw = url.as_str().ok_or_else(|| err("bad_request", "the address must be text"))?;
    let mut url = reqwest::Url::parse(raw.trim()).map_err(|_| err("bad_request", "the address must be an absolute https:// address"))?;
    if !init.is_null() && !init.is_object() { return Err(err("bad_request", "the options must be an object")); }
    let mut method = init["method"].as_str().unwrap_or("GET").to_ascii_uppercase();
    if !matches!(method.as_str(), "GET" | "HEAD" | "POST" | "PUT" | "PATCH" | "DELETE" | "OPTIONS") {
        return Err(err("bad_request", "unsupported method"));
    }
    let mut headers: Vec<(String, String)> = vec![];
    if let Some(h) = init["headers"].as_object() {
        if h.len() > 50 { return Err(err("bad_request", "at most 50 headers")); }
        for (k, v) in h {
            let v = v.as_str().ok_or_else(|| err("bad_request", "header values must be text"))?;
            if reqwest::header::HeaderName::from_bytes(k.as_bytes()).is_err() || reqwest::header::HeaderValue::from_str(v).is_err() || v.len() > 8192 {
                return Err(err("bad_request", "a header name or value is not valid"));
            }
            let lower = k.to_ascii_lowercase();
            if DROPPED_HEADERS.contains(&lower.as_str()) || lower.starts_with("proxy-") || lower.starts_with("sec-") { continue; }
            headers.push((lower, v.to_string()));
        }
    } else if !init["headers"].is_null() { return Err(err("bad_request", "the headers must be an object")); }
    let mut body = match (&init["body"], &init["bodyBase64"]) {
        (Value::Null, Value::Null) => None,
        (Value::String(s), Value::Null) => Some(s.as_bytes().to_vec()),
        (Value::Null, Value::String(s)) => Some(b64.decode(s).map_err(|_| err("bad_request", "bodyBase64 is not base64"))?),
        _ => return Err(err("bad_request", "the body must be text (body) or base64 (bodyBase64), not both")),
    };
    if body.as_ref().is_some_and(|b| b.len() > FETCH_MAX_BYTES) { return Err(err("quota", "the request body is larger than 1 MiB")); }
    if body.is_some() && matches!(method.as_str(), "GET" | "HEAD") { return Err(err("bad_request", "a GET or HEAD has no body")); }
    for n in 0..=MAX_REDIRECTS {
        if Instant::now() >= deadline { return Err(err("timeout", "the request took too long")); }
        let hop = plan_hop(&url, origins, private_hosts, resolve)
            .map_err(|e| if n == 0 { e } else { HostError { message: format!("a redirect was refused: {}", e.message), ..e } })?;
        let reply = send(&hop, &Outgoing { method: method.clone(), headers: headers.clone(), body: body.clone() }, deadline)?;
        let location = reply.headers.iter().find(|(k, _)| k.eq_ignore_ascii_case("location")).map(|(_, v)| v.trim().to_string());
        if crate::redirect::is_redirect(reply.status) {
            if let Some(loc) = location.filter(|l| !l.is_empty()) {
                if n == MAX_REDIRECTS { return Err(err("denied", format!("more than {MAX_REDIRECTS} redirects"))); }
                let next = url.join(&loc).map_err(|_| err("denied", "a redirect to an address that cannot be used"))?;
                if reply.status == 303 || (matches!(reply.status, 301 | 302) && method == "POST") {
                    method = "GET".into();
                    body = None;
                    headers.retain(|(k, _)| k != "content-type");
                }
                if next.origin() != url.origin() { headers.retain(|(k, _)| k != "authorization"); }
                url = next;
                continue;
            }
        }
        let mut hs = serde_json::Map::new();
        for (k, v) in &reply.headers {
            let k = k.to_ascii_lowercase();
            if k == "set-cookie" || k == "set-cookie2" { continue; }
            let joined = match hs.get(&k).and_then(|x| x.as_str()) { Some(old) => format!("{old}, {v}"), None => v.clone() };
            hs.insert(k, json!(joined));
        }
        return Ok(json!({"ok": (200..300).contains(&reply.status), "status": reply.status, "statusText": reply.status_text,
            "url": url.as_str(), "headers": hs, "bodyText": String::from_utf8_lossy(&reply.body), "bodyBase64": b64.encode(&reply.body)}));
    }
    Err(err("denied", format!("more than {MAX_REDIRECTS} redirects")))
}

/// One hop with reqwest: no redirects, no proxy, no cookie store, connected only to `hop.pin`, the body read up to
/// [`FETCH_MAX_BYTES`].
pub fn send_reqwest(hop: &Hop, req: &Outgoing, deadline: Instant) -> Result<HopReply, HostError> {
    use std::io::Read;
    let left = deadline.saturating_duration_since(Instant::now());
    if left.is_zero() { return Err(err("timeout", "the request took too long")); }
    let http = reqwest::blocking::Client::builder().timeout(left).connect_timeout(left.min(Duration::from_secs(15)))
        .redirect(reqwest::redirect::Policy::none()).no_proxy().user_agent(concat!("Barbican/", env!("CARGO_PKG_VERSION"), " plugin"))
        .resolve_to_addrs(&hop.host, &hop.pin).build().map_err(|e| err("internal", e.to_string()))?;
    let method = reqwest::Method::from_bytes(req.method.as_bytes()).map_err(|_| err("bad_request", "unsupported method"))?;
    let mut r = http.request(method, hop.url.clone());
    for (k, v) in &req.headers { r = r.header(k.as_str(), v.as_str()); }
    if let Some(b) = &req.body { r = r.body(b.clone()); }
    let resp = r.send().map_err(|e| if e.is_timeout() { err("timeout", "the request took too long") } else { err("offline", format!("the request failed: {e}")) })?;
    if resp.content_length().is_some_and(|n| n > FETCH_MAX_BYTES as u64) { return Err(err("quota", "the answer is larger than 1 MiB")); }
    let status = resp.status();
    let headers = resp.headers().iter().filter_map(|(k, v)| v.to_str().ok().map(|v| (k.as_str().to_string(), v.to_string()))).collect();
    let mut body = vec![];
    resp.take(FETCH_MAX_BYTES as u64 + 1).read_to_end(&mut body).map_err(|e| err("offline", e.to_string()))?;
    if body.len() > FETCH_MAX_BYTES { return Err(err("quota", "the answer is larger than 1 MiB")); }
    Ok(HopReply { status: status.as_u16(), status_text: status.canonical_reason().unwrap_or("").to_string(), headers, body })
}

// ---------------------------------------------------------------------------------------------------------------
// the C ABI: the host table and the plugin's entry points
// ---------------------------------------------------------------------------------------------------------------

/// PLUGINS.md "The host table given at load". Fields are only ever appended; a plugin checks `size`.
#[repr(C)]
pub struct BarbicanHost {
    pub abi: u32,
    pub size: u32,
    pub ctx: *mut c_void,
    pub call: unsafe extern "C" fn(*mut c_void, *const c_char) -> *mut c_char,
    pub post: unsafe extern "C" fn(*mut c_void, *const c_char) -> i32,
    pub free_str: unsafe extern "C" fn(*mut c_void, *mut c_char),
    pub log: unsafe extern "C" fn(*mut c_void, i32, *const c_char),
}

unsafe fn ctx_of<'a>(ctx: *mut c_void) -> Option<&'a HostCtx> {
    // SAFETY: the only `ctx` the host hands out is `Arc::into_raw` of a HostCtx that is never released
    unsafe { (ctx as *const HostCtx).as_ref() }
}

fn into_c(s: String) -> *mut c_char {
    // serde_json escapes NUL, so a reply has none; a stray one would only cut it short
    CString::new(s).unwrap_or_else(|e| { let n = e.nul_position(); CString::new(e.into_vec()[..n].to_vec()).unwrap_or_default() }).into_raw()
}

unsafe extern "C" fn host_call(ctx: *mut c_void, request: *const c_char) -> *mut c_char {
    let reply = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        // SAFETY: see ctx_of; the request is a NUL-terminated string borrowed for this call
        match (unsafe { ctx_of(ctx) }, request.is_null()) {
            (Some(c), false) => c.request(unsafe { CStr::from_ptr(request) }.to_bytes()),
            _ => envelope(Err(err("bad_request", "no context or no request"))),
        }
    })).unwrap_or_else(|_| envelope(Err(err("internal", "the host failed on this call"))));
    into_c(reply)
}

unsafe extern "C" fn host_post(ctx: *mut c_void, message: *const c_char) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| match (unsafe { ctx_of(ctx) }, message.is_null()) {
        (Some(c), false) => c.post(unsafe { CStr::from_ptr(message) }.to_bytes()),
        _ => -1,
    })).unwrap_or(-1)
}

unsafe extern "C" fn host_free(_ctx: *mut c_void, s: *mut c_char) {
    // SAFETY: only strings host_call returned (CString::into_raw) are handed back here
    if !s.is_null() { drop(unsafe { CString::from_raw(s) }); }
}

unsafe extern "C" fn host_log(ctx: *mut c_void, level: i32, text: *const c_char) {
    let _ = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        if let (Some(c), false) = (unsafe { ctx_of(ctx) }, text.is_null()) {
            c.log(level, &String::from_utf8_lossy(unsafe { CStr::from_ptr(text) }.to_bytes()));
        }
    }));
}

/// The table handed to `barbican_plugin_init`. It and its context live for the rest of the process (the plugin may
/// keep both and call from its own threads; once switched off every call answers `disabled`).
fn host_table(ctx: &Arc<HostCtx>) -> &'static BarbicanHost {
    let raw = Arc::into_raw(ctx.clone()) as *mut c_void;
    Box::leak(Box::new(BarbicanHost { abi: ABI, size: std::mem::size_of::<BarbicanHost>() as u32, ctx: raw,
        call: host_call, post: host_post, free_str: host_free, log: host_log }))
}

pub type ManifestFn = unsafe extern "C" fn() -> *const c_char;
pub type InitFn = unsafe extern "C" fn(*const BarbicanHost, *const c_char) -> *mut c_char;
pub type HandleFn = unsafe extern "C" fn(*const c_char) -> *mut c_char;
pub type FreeFn = unsafe extern "C" fn(*mut c_char);
pub type ShutdownFn = unsafe extern "C" fn();

/// The entry points of an API 2 library (or of an in-process fake in tests).
#[derive(Clone, Copy)]
pub struct NativeV2 {
    pub manifest: ManifestFn,
    pub init: InitFn,
    /// Every host -> plugin message (B4: hooks, `settings`, `cancel`).
    pub handle: HandleFn,
    pub free: FreeFn,
    pub shutdown: Option<ShutdownFn>,
}

pub type V2Loader = Arc<dyn Fn(&Path) -> Result<NativeV2, String> + Send + Sync>;

/// Open an API 2 library and look up its entry points. The library is kept for the rest of the process.
///
/// # Safety
/// Loading a library runs its code (`DllMain`, constructors) in this process.
pub unsafe fn load_library(p: &Path) -> Result<NativeV2, String> {
    let lib = Arc::new(unsafe { libloading::Library::new(p) }.map_err(|e| e.to_string())?);
    crate::plugins::keep_library(lib.clone());
    // SAFETY: the symbols have the types PLUGINS.md "Exported symbols" gives; `lib` is never unloaded
    unsafe {
        let manifest = *lib.get::<ManifestFn>(b"barbican_plugin_manifest\0").map_err(|_| "missing barbican_plugin_manifest")?;
        let init = *lib.get::<InitFn>(b"barbican_plugin_init\0").map_err(|_| "missing barbican_plugin_init (an API 2 plugin needs it)")?;
        let handle = *lib.get::<HandleFn>(b"barbican_plugin_handle\0").map_err(|_| "missing barbican_plugin_handle (an API 2 plugin needs it)")?;
        let free = *lib.get::<FreeFn>(b"barbican_plugin_free\0").map_err(|_| "missing barbican_plugin_free")?;
        let shutdown = lib.get::<ShutdownFn>(b"barbican_plugin_shutdown\0").ok().map(|s| *s);
        Ok(NativeV2 { manifest, init, handle, free, shutdown })
    }
}

/// What the package's `manifest.json` said, which the library's own manifest must repeat.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Expect {
    pub id: String,
    pub version: String,
    pub permissions: Vec<String>,
    pub optional_permissions: Vec<String>,
    pub http_origins: Vec<String>,
    pub hooks: Vec<String>,
    pub slots: Vec<String>,
}

fn check_library_manifest(n: &NativeV2, e: &Expect) -> Result<(), String> {
    let p = std::panic::catch_unwind(|| unsafe { (n.manifest)() }).map_err(|_| "the manifest call panicked".to_string())?;
    if p.is_null() { return Err("the library has no manifest".into()); }
    // SAFETY: a static NUL-terminated string owned by the plugin (PLUGINS.md)
    let text = unsafe { CStr::from_ptr(p) }.to_bytes();
    if text.len() as u64 > crate::plugins::MAX_MANIFEST_BYTES { return Err("the library's manifest is too large".into()); }
    let v: Value = serde_json::from_slice(text).map_err(|e| format!("the library's manifest is not JSON: {e}"))?;
    let m = crate::plugins::validate_manifest(&v).map_err(|e| format!("the library's manifest: {e}"))?;
    if m.id != e.id { return Err(format!("manifest.json says id {} but the library says {}", e.id, m.id)); }
    let same = |a: &[String], b: &[String]| { let (mut a, mut b) = (a.to_vec(), b.to_vec()); a.sort(); b.sort(); a == b };
    let differs: Vec<&str> = [("version", m.version == e.version), ("permissions", same(&m.permissions, &e.permissions)),
        ("optionalPermissions", same(&m.optional_permissions, &e.optional_permissions)), ("httpOrigins", same(&m.http_origins, &e.http_origins)),
        ("hooks", same(&m.hooks, &e.hooks)), ("slots", same(&m.slots, &e.slots))].iter().filter(|(_, ok)| !ok).map(|(k, _)| *k).collect();
    if !differs.is_empty() { return Err(format!("the library's own manifest does not agree with manifest.json ({})", differs.join(", "))); }
    Ok(())
}

/// `init_json` (PLUGINS.md): what the plugin may know at start. Accounts are listed only with `account:read`.
pub fn init_json(ctx: &HostCtx) -> Value {
    let mut granted: Vec<String> = IMPLICIT_PERMISSIONS.iter().map(|s| s.to_string()).collect();
    granted.extend(ctx.p.granted.iter().cloned());
    let accounts = match (ctx.holds("account:read"), ctx.env.account()) {
        (true, Some(a)) => json!([{"id": a.account_id, "kind": "own", "active": true}]),
        _ => json!([]),
    };
    json!({"v": 2, "app": {"product": "Barbican", "version": env!("CARGO_PKG_VERSION"), "os": std::env::consts::OS},
        "plugin": {"id": ctx.p.id, "version": ctx.p.version}, "granted": granted, "httpOrigins": ctx.p.origins,
        "settings": effective_settings(&ctx.p.settings, &ctx.env.stored_settings(&ctx.p.id)), "accounts": accounts,
        "regional": {"locale": "en"}, "features": {"hooks": FIRED_HOOKS, "slots": [], "methods": METHODS}})
}

/// A started API 2 plugin: its context and the line to its host thread.
pub struct Runtime {
    pub ctx: Arc<HostCtx>,
}

impl Runtime {
    /// Switch the plugin off: every later call answers `disabled`, and `shutdown` runs on its host thread once it
    /// is idle (see [`wait_all_idle`]).
    pub fn switch_off(&self) { self.ctx.disable(); }
}

/// The libraries whose host thread has not ended: loading, inside `init`, running, or inside `shutdown`. A new
/// instance of a library never starts before the old one's thread ended, whichever list entry it came from, so its
/// `init` cannot overlap the old `shutdown` (or an `init` that overran its budget).
static IN_USE: Mutex<Vec<String>> = Mutex::new(Vec::new());
static IN_USE_FREED: std::sync::Condvar = std::sync::Condvar::new();

/// The key a library is held in use under: the file it was opened from.
fn library_key(path: &Path) -> String {
    let p = std::fs::canonicalize(path).unwrap_or_else(|_| path.to_path_buf()).to_string_lossy().into_owned();
    if cfg!(windows) { p.to_lowercase() } else { p }
}

/// Held by a host thread for its whole life; dropping it frees the library.
struct InUse(String);

impl Drop for InUse {
    fn drop(&mut self) {
        let mut g = lock(&IN_USE);
        if let Some(i) = g.iter().position(|k| *k == self.0) { g.swap_remove(i); }
        IN_USE_FREED.notify_all();
    }
}

/// Take `library` for a new host thread, waiting until `deadline` for an old one to end.
fn claim(library: &str, deadline: Instant) -> Option<InUse> {
    let mut g = lock(&IN_USE);
    while g.iter().any(|k| k == library) {
        let left = deadline.checked_duration_since(Instant::now()).filter(|d| !d.is_zero())?;
        g = IN_USE_FREED.wait_timeout(g, left).map(|(g, _)| g).unwrap_or_else(|e| e.into_inner().0);
    }
    g.push(library.to_string());
    Some(InUse(library.to_string()))
}

/// Is a host thread of this library still running?
#[cfg(test)]
pub fn library_in_use(path: &Path) -> bool { let k = library_key(path); lock(&IN_USE).contains(&k) }

/// Wait until no library has a host thread left, at most until `deadline` (the app's exit gives switched-off plugins a
/// moment to run `shutdown`). True when all ended.
pub fn wait_all_idle(deadline: Instant) -> bool {
    let mut g = lock(&IN_USE);
    loop {
        if g.is_empty() { return true; }
        let Some(left) = deadline.checked_duration_since(Instant::now()).filter(|d| !d.is_zero()) else { return false };
        g = IN_USE_FREED.wait_timeout(g, left).map(|(g, _)| g).unwrap_or_else(|e| e.into_inner().0);
    }
}

impl Drop for Runtime {
    fn drop(&mut self) { self.switch_off(); }
}

/// Load and initialise an API 2 plugin on its own host thread and wait up to `budget` in all (for an earlier instance
/// of the same library to end, then for `init` to return). The library stays in use ([`library_in_use`]) until the
/// host thread ends: after `shutdown` returned, after a failed load or init, or after a late `init` was shut down.
/// Only `manifest` and `init` of the plugin are called here; `shutdown` follows [`Runtime::switch_off`].
pub fn start(loader: &V2Loader, path: &Path, expect: Expect, params: Params, env: Arc<dyn HostEnv>, budget: Duration) -> Result<Arc<Runtime>, String> {
    let deadline = Instant::now() + budget;
    let library = library_key(path);
    let Some(held) = claim(&library, deadline) else {
        return Err("its previous instance is still shutting down; switch it off and on again in a moment".into());
    };
    let ctx = Arc::new(HostCtx::new(params, env));
    let (done_tx, done_rx) = mpsc::channel::<Result<(), String>>();
    let (queue_tx, queue_rx) = mpsc::channel::<u64>();
    lock(&ctx.msgs).queue = Some(queue_tx);
    let (l, p, c) = (loader.clone(), path.to_path_buf(), ctx.clone());
    // a fixed thread name: manifest text is untrusted
    let spawned = std::thread::Builder::new().name("barbican-plugin-host".into()).spawn(move || {
        let _held = held;
        let loaded = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| l(&p)))
            .unwrap_or_else(|_| Err("plugin panicked while loading".to_string()))
            .and_then(|n| check_library_manifest(&n, &expect).map(|_| n));
        let n = match loaded {
            Ok(n) => n,
            Err(e) => { let _ = done_tx.send(Err(e)); return; }
        };
        let init = CString::new(init_json(&c).to_string()).unwrap_or_default();
        let table = host_table(&c);
        c.in_init.store(true, Ordering::SeqCst);
        let out = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| unsafe { (n.init)(table, init.as_ptr()) }));
        c.in_init.store(false, Ordering::SeqCst);
        let r = match out {
            Err(_) => Err("barbican_plugin_init panicked".to_string()),
            Ok(p) if p.is_null() => Ok(()),
            Ok(p) => {
                // SAFETY: a NUL-terminated string the plugin allocated, freed with its own free
                let text = String::from_utf8_lossy(unsafe { CStr::from_ptr(p) }.to_bytes()).into_owned();
                let _ = std::panic::catch_unwind(|| unsafe { (n.free)(p) });
                let v: Value = serde_json::from_str(&text).unwrap_or(Value::Null);
                Err(format!("barbican_plugin_init failed: {}", v["error"]["message"].as_str().or(v["error"]["code"].as_str()).unwrap_or("no reason given")))
            }
        };
        let ok = r.is_ok();
        let late = done_tx.send(r).is_err();
        if !ok { c.disable(); return; }
        // the plugin's messages, one at a time, until it is switched off (or the start gave up waiting, which
        // disabled it: then at once)
        if !late {
            while let Ok(id) = queue_rx.recv() {
                if c.is_disabled() { break; }
                c.deliver(&n, id);
            }
        }
        c.disable();
        if let Some(s) = n.shutdown { let _ = std::panic::catch_unwind(|| unsafe { s() }); }
    });
    if let Err(e) = spawned { ctx.disable(); return Err(format!("could not start the plugin's thread: {e}")); }
    match done_rx.recv_timeout(deadline.saturating_duration_since(Instant::now())) {
        Ok(Ok(())) => Ok(Arc::new(Runtime { ctx })),
        Ok(Err(e)) => { ctx.disable(); Err(e) }
        Err(mpsc::RecvTimeoutError::Timeout) => {
            ctx.disable();
            Err(format!("did not finish loading within {} ms", budget.as_millis()))
        }
        Err(mpsc::RecvTimeoutError::Disconnected) => { ctx.disable(); Err("the plugin's thread died".into()) }
    }
}

// ---------------------------------------------------------------------------------------------------------------
// the app's environment
// ---------------------------------------------------------------------------------------------------------------

/// The running app: the signed-in session, the window for toasts and dialogs, the prefs, the OS resolver.
pub struct AppEnv {
    window: Mutex<slint::Weak<crate::AppWindow>>,
    session: crate::Shared,
}

impl AppEnv {
    pub fn new(window: slint::Weak<crate::AppWindow>, session: crate::Shared) -> AppEnv { AppEnv { window: Mutex::new(window), session } }
}

impl HostEnv for AppEnv {
    fn account(&self) -> Option<Account> {
        let c = lock(&self.session).clone()?;
        let name = crate::livesync::identity_name(&c.state_key()).filter(|n| !n.trim().is_empty()).unwrap_or_else(|| c.username.clone());
        Some(Account { login_key: crate::reminders::owner_key(&c.base, &c.username), account_id: c.account.clone(), login: c.username.clone(),
            name, server: c.base.clone() })
    }
    fn data_dir(&self) -> PathBuf { crate::plugins::data_dir() }
    fn stored_settings(&self, plugin: &str) -> Value {
        crate::settings::Settings::load().prefs.get("pluginSettings").and_then(|m| m.get(plugin)).cloned().unwrap_or(Value::Null)
    }
    fn toast(&self, _tone: &str, text: &str) {
        let t = text.to_string();
        let _ = lock(&self.window).upgrade_in_event_loop(move |u| crate::mail::show_toast(&u, &t, false));
    }
    fn dialog(&self, confirm: bool, title: &str, text: &str, ok: &str, cancel: &str) -> Result<bool, HostError> {
        let (tx, rx) = mpsc::channel();
        let (title, text, ok, cancel) = (title.to_string(), text.to_string(), ok.to_string(), cancel.to_string());
        lock(&self.window).upgrade_in_event_loop(move |u| {
            let buttons = if confirm { rfd::MessageButtons::OkCancelCustom(ok.clone(), cancel) } else { rfd::MessageButtons::OkCustom(ok.clone()) };
            let r = rfd::MessageDialog::new().set_parent(&u.window().window_handle()).set_level(rfd::MessageLevel::Info)
                .set_title(title.as_str()).set_description(text.as_str()).set_buttons(buttons).show();
            let _ = tx.send(matches!(&r, rfd::MessageDialogResult::Ok) || matches!(&r, rfd::MessageDialogResult::Custom(s) if *s == ok));
        }).map_err(|_| err("internal", "the window is gone"))?;
        rx.recv_timeout(DIALOG_WAIT).map_err(|_| err("timeout", "nobody answered the dialog"))
    }
    fn resolve(&self, host: &str, port: u16) -> std::io::Result<Vec<IpAddr>> { crate::feed_guard::system_resolve(host, port) }
    fn send(&self, hop: &Hop, req: &Outgoing, deadline: Instant) -> Result<HopReply, HostError> { send_reqwest(hop, req, deadline) }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::HashMap;

    struct Fake {
        account: Mutex<Option<Account>>,
        dir: PathBuf,
        settings: Mutex<Value>,
        toasts: Mutex<Vec<(String, String)>>,
        dialogs: Mutex<Vec<(bool, String, String, String, String)>>,
        answer: bool,
        dns: HashMap<String, Vec<IpAddr>>,
        sent: Mutex<Vec<(Hop, Outgoing)>>,
        replies: Mutex<VecDeque<HopReply>>,
        logs: Mutex<Vec<String>>,
    }

    fn temp(tag: &str) -> PathBuf {
        let d = std::env::temp_dir().join(format!("barbican-plugin-host-{tag}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&d);
        std::fs::create_dir_all(&d).unwrap();
        d
    }

    fn ann() -> Account {
        Account { login_key: "https://mail.example.test|ann@example.test".into(), account_id: "a1".into(), login: "ann@example.test".into(),
            name: "Ann Example".into(), server: "https://mail.example.test".into() }
    }

    fn fake(tag: &str) -> Arc<Fake> {
        let ip = |s: &str| s.parse::<IpAddr>().unwrap();
        let dns = HashMap::from([
            ("api.example.com".to_string(), vec![ip("93.184.216.34")]),
            ("a.example.org".to_string(), vec![ip("93.184.216.35")]),
            ("b.example.org".to_string(), vec![ip("10.0.0.5")]),
            ("nas.example.com".to_string(), vec![ip("192.168.1.10")]),
            ("mixed.example.com".to_string(), vec![ip("93.184.216.36"), ip("127.0.0.1")]),
            ("meta.example.com".to_string(), vec![ip("169.254.169.254")]),
            ("v6.example.com".to_string(), vec![ip("::ffff:10.1.2.3")]),
            ("nas".to_string(), vec![ip("192.168.1.11")]),
            ("other.example.net".to_string(), vec![ip("93.184.216.40")]),
        ]);
        Arc::new(Fake { account: Mutex::new(Some(ann())), dir: temp(tag), settings: Mutex::new(Value::Null), toasts: Mutex::new(vec![]),
            dialogs: Mutex::new(vec![]), answer: true, dns, sent: Mutex::new(vec![]), replies: Mutex::new(VecDeque::new()), logs: Mutex::new(vec![]) })
    }

    impl HostEnv for Fake {
        fn account(&self) -> Option<Account> { lock(&self.account).clone() }
        fn data_dir(&self) -> PathBuf { self.dir.clone() }
        fn stored_settings(&self, _: &str) -> Value { lock(&self.settings).clone() }
        fn toast(&self, tone: &str, text: &str) { lock(&self.toasts).push((tone.into(), text.into())); }
        fn dialog(&self, confirm: bool, title: &str, text: &str, ok: &str, cancel: &str) -> Result<bool, HostError> {
            lock(&self.dialogs).push((confirm, title.into(), text.into(), ok.into(), cancel.into()));
            Ok(self.answer)
        }
        fn resolve(&self, host: &str, _: u16) -> std::io::Result<Vec<IpAddr>> {
            self.dns.get(host).cloned().ok_or_else(|| std::io::Error::other("no such host"))
        }
        fn send(&self, hop: &Hop, req: &Outgoing, _: Instant) -> Result<HopReply, HostError> {
            lock(&self.sent).push((hop.clone(), req.clone()));
            Ok(lock(&self.replies).pop_front().unwrap_or(HopReply { status: 200, status_text: "OK".into(), headers: vec![], body: b"hi".to_vec() }))
        }
        fn log(&self, line: &str) { lock(&self.logs).push(line.into()); }
    }

    fn params(id: &str, granted: &[&str]) -> Params {
        Params { id: id.into(), name: "Demo".into(), version: "1.0.0".into(), granted: granted.iter().map(|s| s.to_string()).collect(),
            origins: vec!["https://api.example.com".into(), "https://*.example.org".into(), "https://nas.example.com".into(), "https://nas".into(),
                "https://mixed.example.com".into(), "https://meta.example.com".into(), "https://v6.example.com".into()],
            private_hosts: vec!["https://nas.example.com".into(), "https://nas".into()],
            settings: json!({"threshold": {"type": "number", "label": "Warn above", "default": 5, "min": 0, "max": 20},
                "mode": {"type": "select", "label": "Show", "default": "warn", "options": ["always", "warn"]},
                "on": {"type": "boolean", "label": "On"}, "apiKey": {"type": "secret", "label": "API key"}}), hooks: vec![] }
    }

    fn ctx(id: &str, granted: &[&str], env: &Arc<Fake>) -> HostCtx { HostCtx::new(params(id, granted), env.clone() as Arc<dyn HostEnv>) }
    fn code(r: Result<Value, HostError>) -> &'static str { r.err().map_or("ok", |e| e.code) }

    #[test]
    fn requests_and_replies_are_envelopes() {
        let env = fake("envelope");
        let c = ctx("dev.example.env", &[], &env);
        let r: Value = serde_json::from_str(&c.request(br#"{"v":2,"method":"app.getInfo","args":[]}"#)).unwrap();
        assert_eq!((r["v"].as_u64(), r["ok"].as_bool(), r["result"]["product"].as_str()), (Some(2), Some(true), Some("Barbican")));
        for bad in [&b"not json"[..], br#"{"v":1,"method":"app.getInfo"}"#, br#"{"v":2}"#, br#"{"v":2,"method":"app.getInfo","args":{}}"#, b"\xff\xfe"] {
            let r: Value = serde_json::from_str(&c.request(bad)).unwrap();
            assert_eq!((r["ok"].as_bool(), r["error"]["code"].as_str()), (Some(false), Some("bad_request")), "{bad:?}");
        }
        let big = format!(r#"{{"v":2,"method":"app.getInfo","args":["{}"]}}"#, "x".repeat(MAX_ENVELOPE));
        assert!(c.request(big.as_bytes()).contains("bad_request"));
    }

    #[test]
    fn unknown_skipped_and_unbuilt_methods_fail_closed() {
        let env = fake("unsupported");
        let c = ctx("dev.example.uns", &["email:read"], &env);
        assert_eq!(code(c.call("no.such", &json!([]))), "unsupported");
        assert_eq!(code(c.call("http.post", &json!([]))), "unsupported", "skipped in the catalogue");
        assert_eq!(code(c.call("keystore.list", &json!([]))), "unsupported");
        // catalogued but not built yet: its permission is still checked first
        assert_eq!(code(c.call("email.get", &json!([]))), "unsupported");
        assert_eq!(code(c.call("calendar.getEvents", &json!([]))), "denied", "calendar:read was not granted");
    }

    #[test]
    fn each_call_checks_its_permission() {
        let env = fake("perms");
        let none = ctx("dev.example.none", &[], &env);
        assert_eq!(code(none.call("user.getSelf", &json!([]))), "denied");
        assert_eq!(code(none.call("user.getAccounts", &json!([]))), "denied");
        assert_eq!(code(none.call("http.fetch", &json!(["https://api.example.com/x"]))), "denied");
        assert!(lock(&env.sent).is_empty(), "nothing went out");
        // no static gate: storage, settings, toasts and dialogs; each has its own gate below
        assert_eq!(code(none.call("plugin.getConfig", &json!([]))), "ok");
        let all = ctx("dev.example.all", &["account:read", "http:fetch"], &env);
        let me = all.call("user.getSelf", &json!([])).unwrap();
        assert_eq!(me, json!({"accountId": "a1", "email": "ann@example.test", "name": "Ann Example", "login": "ann@example.test", "server": "https://mail.example.test"}));
        assert!(!me.to_string().contains("login_key") && !me.to_string().contains('|'), "the storage key is not handed out");
        assert_eq!(all.call("user.getAccounts", &json!([])).unwrap()[0]["displayName"], "Ann Example");
        assert_eq!(code(all.call("http.fetch", &json!(["https://api.example.com/x"]))), "ok");
        // signed out: no account, no storage
        *lock(&env.account) = None;
        assert_eq!(code(all.call("user.getSelf", &json!([]))), "not_found");
        assert_eq!(code(all.call("storage.get", &json!(["k"]))), "not_found");
    }

    #[test]
    fn a_switched_off_plugin_gets_disabled_everywhere() {
        let env = fake("disabled");
        let c = ctx("dev.example.off", &["account:read"], &env);
        c.disable();
        for m in ["app.getInfo", "storage.get", "toast.info", "user.getSelf", "no.such"] {
            assert_eq!(code(c.call(m, &json!(["x"]))), "disabled", "{m}");
        }
        assert!(c.request(br#"{"v":2,"method":"app.getInfo"}"#).contains("disabled"));
        assert_eq!(c.post(br#"{"v":2,"type":"result","id":1}"#), -3);
    }

    #[test]
    fn post_accepts_envelopes_only() {
        let env = fake("post");
        let c = ctx("dev.example.post", &[], &env);
        assert_eq!(c.post(br#"{"v":2,"type":"result","id":1,"ok":true}"#), 0);
        assert_eq!(c.post(b"nope"), -1);
        assert_eq!(c.post(br#"{"v":2}"#), -1);
    }

    #[test]
    fn storage_is_per_plugin_and_per_login() {
        let env = fake("storage");
        let a = ctx("dev.example.one", &[], &env);
        let b = ctx("dev.example.two", &[], &env);
        a.call("storage.set", &json!(["k", {"n": 1}])).unwrap();
        assert_eq!(a.call("storage.get", &json!(["k"])).unwrap(), json!({"n": 1}));
        assert_eq!(b.call("storage.get", &json!(["k"])).unwrap(), Value::Null, "another plugin sees nothing");
        b.call("storage.set", &json!(["k", "two"])).unwrap();
        assert_eq!(a.call("storage.get", &json!(["k"])).unwrap(), json!({"n": 1}));
        // another login of the same plugin
        *lock(&env.account) = Some(Account { login_key: "https://mail.example.test|bob@example.test".into(), ..ann() });
        assert_eq!(a.call("storage.get", &json!(["k"])).unwrap(), Value::Null);
        assert_eq!(a.call("storage.keys", &json!([])).unwrap(), json!([]));
        *lock(&env.account) = Some(ann());
        a.call("storage.set", &json!(["z", 2])).unwrap();
        assert_eq!(a.call("storage.keys", &json!([])).unwrap(), json!(["k", "z"]));
        a.call("storage.remove", &json!(["k"])).unwrap();
        assert_eq!(a.call("storage.keys", &json!([])).unwrap(), json!(["z"]));
        // on disk: plugin-data\<id>\<login hash>.json, nothing else left behind, no address in the name
        let f = env.dir.join("dev.example.one").join(login_file(&ann().login_key));
        assert!(f.is_file());
        let names: Vec<String> = std::fs::read_dir(f.parent().unwrap()).unwrap().flatten().map(|e| e.file_name().to_string_lossy().to_string()).collect();
        assert_eq!(names.len(), 1, "one login wrote (the other only read), no temporary files: {names:?}");
        assert!(names.iter().all(|n| !n.contains("example") && n.ends_with(".json")));
        // a new context (a restart) reads the same values
        assert_eq!(ctx("dev.example.one", &[], &env).call("storage.get", &json!(["z"])).unwrap(), json!(2));
        let _ = std::fs::remove_dir_all(&env.dir);
    }

    #[test]
    fn storage_keys_and_quota_are_checked() {
        let env = fake("quota");
        let c = ctx("dev.example.quota", &[], &env);
        assert_eq!(code(c.call("storage.set", &json!(["", 1]))), "bad_request");
        assert_eq!(code(c.call("storage.set", &json!(["k".repeat(257), 1]))), "bad_request");
        assert_eq!(code(c.call("storage.set", &json!(["é".repeat(256), 1]))), "ok", "256 characters, more bytes");
        assert_eq!(code(c.call("storage.get", &json!([7]))), "bad_request");
        let half = "x".repeat(STORAGE_QUOTA_BYTES / 2);
        assert_eq!(code(c.call("storage.set", &json!(["a", half]))), "ok");
        assert_eq!(code(c.call("storage.set", &json!(["b", half]))), "quota", "over 1 MiB in all");
        assert_eq!(c.call("storage.get", &json!(["b"])).unwrap(), Value::Null, "a refused set changes nothing");
        // replacing a value counts its new size, not both
        assert_eq!(code(c.call("storage.set", &json!(["a", half]))), "ok");
        // another login has its own quota
        *lock(&env.account) = Some(Account { login_key: "other".into(), ..ann() });
        assert_eq!(code(c.call("storage.set", &json!(["b", half]))), "ok");
        let _ = std::fs::remove_dir_all(&env.dir);
    }

    #[test]
    fn unreadable_or_damaged_storage_is_an_error_and_never_overwritten() {
        let env = fake("damaged");
        let c = ctx("dev.example.damaged", &[], &env);
        let f = env.dir.join("dev.example.damaged").join(login_file(&ann().login_key));
        std::fs::create_dir_all(f.parent().unwrap()).unwrap();
        for bad in [&b"[1, 2]"[..], b"{\"half\": ", b"\"text\""] {
            std::fs::write(&f, bad).unwrap();
            for (m, a) in [("storage.get", json!(["k"])), ("storage.keys", json!([])), ("storage.set", json!(["k", 1])), ("storage.remove", json!(["k"]))] {
                assert_eq!(code(c.call(m, &a)), "internal", "{m} on {:?}", String::from_utf8_lossy(bad));
            }
            assert_eq!(std::fs::read(&f).unwrap(), bad, "the file is left as it was");
        }
        // a file that cannot be read at all (here: a folder in its place, as a lock would): an error, not empty
        std::fs::remove_file(&f).unwrap();
        std::fs::create_dir_all(f.join("x")).unwrap();
        assert_eq!(code(c.call("storage.set", &json!(["k", 1]))), "internal");
        assert!(f.join("x").is_dir(), "nothing written over it");
        // only a missing file is empty storage
        std::fs::remove_dir_all(&f).unwrap();
        assert_eq!(c.call("storage.get", &json!(["k"])).unwrap(), Value::Null);
        assert_eq!(code(c.call("storage.set", &json!(["k", 1]))), "ok");
        let _ = std::fs::remove_dir_all(&env.dir);
    }

    #[test]
    fn a_write_after_the_login_signed_out_is_refused() {
        let env = fake("signedout");
        let root = env.dir.clone();
        let other = |k: &str| k == "someone else";
        let s = Storage { root: root.clone(), signed_in: &other };
        assert_eq!(s.set("dev.example.gone", &ann().login_key, "k", &json!(1)).unwrap_err().code, "not_found");
        assert_eq!(s.remove("dev.example.gone", &ann().login_key, "k").unwrap_err().code, "not_found");
        assert!(!root.join("dev.example.gone").exists(), "nothing put back");
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn storage_needs_a_checked_plugin_id() {
        let s = Storage { root: temp("ids"), signed_in: &|_| true };
        assert_eq!(s.set("../evil", "k", "a", &json!(1)).unwrap_err().code, "denied");
        assert_eq!(s.set("dev.example.ok", "", "a", &json!(1)).unwrap_err().code, "not_found");
        let _ = std::fs::remove_dir_all(&s.root);
    }

    #[test]
    fn atomic_writes_replace_the_whole_file() {
        let d = temp("atomic");
        let f = d.join("sub").join("x.json");
        write_atomic(&f, b"one").unwrap();
        write_atomic(&f, b"two").unwrap();
        assert_eq!(std::fs::read(&f).unwrap(), b"two");
        assert_eq!(std::fs::read_dir(f.parent().unwrap()).unwrap().count(), 1);
        let _ = std::fs::remove_dir_all(&d);
    }

    #[test]
    fn toasts_are_plain_short_named_and_limited() {
        let env = fake("toast");
        let c = ctx("dev.example.toast", &[], &env);
        c.call("toast.warning", &json!(["Line one\nline\u{202E}two\u{200B} \u{7}bell"])).unwrap();
        let (tone, text) = lock(&env.toasts)[0].clone();
        assert_eq!(tone, "warning");
        assert_eq!(text, "Demo: Line one linetwo  bell");
        c.call("toast.info", &json!(["x".repeat(5000)])).unwrap();
        let long = lock(&env.toasts)[1].1.clone();
        assert_eq!(long.chars().count(), "Demo: ".len() + MAX_TOAST + 1, "cut to 300 and an ellipsis");
        assert_eq!(code(c.call("toast.error", &json!([""]))), "bad_request");
        assert_eq!(code(c.call("toast.error", &json!([{"a": 1}]))), "bad_request");
        for _ in 0..3 { c.call("toast.success", &json!(["ok"])).unwrap(); }
        assert_eq!(code(c.call("toast.success", &json!(["one too many"]))), "rate_limited");
        assert_eq!(lock(&env.toasts).len(), TOASTS_PER_TEN_SECONDS);
        let t0 = Instant::now();
        assert!(c.call_at("toast.info", &json!(["later"]), t0 + Duration::from_secs(11)).is_ok(), "ten seconds later there is room");
        // the plugin's name is made plain as well
        let mut p = params("dev.example.named", &[]);
        p.name = "Evil\u{202E}name\nhere".into();
        let n = HostCtx::new(p, env.clone() as Arc<dyn HostEnv>);
        n.call("toast.info", &json!(["hi"])).unwrap();
        assert_eq!(lock(&env.toasts).last().unwrap().1, "Evilname here: hi");
    }

    #[test]
    fn dialogs_are_plain_named_one_at_a_time_and_limited() {
        let env = fake("dialog");
        let c = ctx("dev.example.dialog", &[], &env);
        assert_eq!(c.call("ui.confirm", &json!([{"title": "Delete\u{202E}?", "message": "Line 1\nLine\u{200F} 2\u{7}", "confirmLabel": "Yes", "cancelLabel": "No"}])).unwrap(), json!(true));
        let (confirm, title, text, ok, cancel) = lock(&env.dialogs)[0].clone();
        assert!(confirm);
        assert_eq!((title.as_str(), ok.as_str(), cancel.as_str()), ("Delete?", "Yes", "No"));
        assert_eq!(text, "Line 1\nLine 2\n\nThis message is from the plugin Demo.");
        assert_eq!(c.call("ui.alert", &json!(["Just text"])).unwrap(), Value::Null);
        let (confirm, title, text, ok, _) = lock(&env.dialogs)[1].clone();
        assert!(!confirm && title == "Demo" && ok == "OK" && text.starts_with("Just text\n\n"));
        assert_eq!(code(c.call("ui.alert", &json!([{"message": "third"}]))), "ok");
        assert_eq!(code(c.call("ui.alert", &json!([{"message": "fourth"}]))), "rate_limited");
        // one at a time
        let d = ctx("dev.example.dialog2", &[], &env);
        d.dialog_open.store(true, Ordering::SeqCst);
        assert_eq!(code(d.call("ui.alert", &json!(["x"]))), "busy");
        d.dialog_open.store(false, Ordering::SeqCst);
        // never during init: the UI waits for it
        d.in_init.store(true, Ordering::SeqCst);
        assert_eq!(code(d.call("ui.confirm", &json!(["x"]))), "busy");
        // long text is cut
        let t = dialog_text(&json!({"message": "a\n".repeat(100), "title": "t".repeat(500)}), "P", false);
        assert_eq!(t.text.lines().filter(|l| *l == "a").count(), 30);
        assert_eq!(t.title.chars().count(), 101);
        // the same label twice would make the answer ambiguous
        let t = dialog_text(&json!({"confirmLabel": "Go", "cancelLabel": "Go"}), "P", true);
        assert_ne!(t.ok, t.cancel);
    }

    #[test]
    fn the_rate_budget_trips_a_plugin_after_three_windows_in_a_row() {
        let env = fake("rate");
        let c = ctx("dev.example.rate-plugin", &[], &env);
        let t0 = Instant::now();
        let burst = |at: Instant| (0..CALLS_PER_SECOND + 5).map(|_| code(c.call_at("app.getInfo", &json!([]), at))).collect::<Vec<_>>();
        let first = burst(t0);
        assert_eq!(first.iter().filter(|c| **c == "ok").count(), CALLS_PER_SECOND as usize);
        assert!(first.iter().skip(CALLS_PER_SECOND as usize).all(|c| *c == "rate_limited"));
        // a quiet second ends the row
        assert_eq!(code(c.call_at("app.getInfo", &json!([]), t0 + Duration::from_secs(1))), "ok");
        burst(t0 + Duration::from_secs(2));
        burst(t0 + Duration::from_secs(3));
        assert!(!c.is_tripped(), "two windows in a row");
        let last = burst(t0 + Duration::from_secs(4));
        assert!(c.is_tripped() && c.is_disabled(), "three windows in a row: {last:?}");
        assert!(last.contains(&"disabled"));
        // peeked, not drained: other tests drain the shared list for their own notices
        assert!(crate::plugins::peek_notices().iter().any(|n| n.contains("Demo") && n.contains("switched off")));
        assert_eq!(code(c.call_at("app.getInfo", &json!([]), t0 + Duration::from_secs(9))), "disabled");
    }

    #[test]
    fn settings_come_from_the_schema() {
        let schema = params("x", &[]).settings;
        assert_eq!(effective_settings(&schema, &Value::Null), json!({"threshold": 5, "mode": "warn", "on": false}), "secrets are left out");
        let stored = json!({"threshold": 50, "mode": "always", "on": true, "apiKey": "s3cret", "extra": 1});
        assert_eq!(effective_settings(&schema, &stored), json!({"threshold": 5, "mode": "always", "on": true}), "an out-of-range value gets the default");
        assert_eq!(setting_value(&schema, "threshold", &json!("7.5")), Some(json!(7.5)));
        assert_eq!(setting_value(&schema, "threshold", &json!("21")), None);
        assert_eq!(setting_value(&schema, "threshold", &json!("7.")), Some(json!(7.0)));
        assert_eq!(setting_value(&schema, "threshold", &json!("x")), None);
        assert_eq!(setting_value(&schema, "mode", &json!(0)), Some(json!("always")));
        assert_eq!(setting_value(&schema, "mode", &json!(5)), None);
        assert_eq!(setting_value(&schema, "on", &json!(true)), Some(json!(true)));
        assert_eq!(setting_value(&schema, "apiKey", &json!("k")), None, "a secret is never kept in the prefs");
        assert_eq!(setting_value(&schema, "nope", &json!(true)), None);
        let rows = setting_rows("dev.example.s", &schema, &json!({"mode": "always"}));
        assert_eq!(rows.len(), 3);
        assert!(matches!(&rows[0], SettingRow::Text { key, value, desc, .. } if key == "pluginSetting:dev.example.s:threshold" && value == "5" && desc == "From 0 to 20."));
        assert!(matches!(&rows[1], SettingRow::Select { index: 0, options, .. } if options == &["always", "warn"]));
        assert!(matches!(&rows[2], SettingRow::Toggle { on: false, .. }));
        // what plugin.getConfig answers
        let env = fake("settings");
        *lock(&env.settings) = json!({"on": true});
        assert_eq!(ctx("dev.example.s", &[], &env).call("plugin.getConfig", &json!([])).unwrap()["on"], json!(true));
    }

    fn u(s: &str) -> reqwest::Url { reqwest::Url::parse(s).unwrap() }

    #[test]
    fn origins_match_as_bulwark_matches_them() {
        assert!(origin_matches(&u("https://api.example.com/x?y"), "https://api.example.com"));
        assert!(origin_matches(&u("https://api.example.com:443/"), "https://api.example.com"));
        assert!(origin_matches(&u("https://api.example.com/"), "https://api.example.com:443"));
        assert!(!origin_matches(&u("https://api.example.com:8443/"), "https://api.example.com"));
        assert!(origin_matches(&u("https://api.example.com:8443/"), "https://api.example.com:8443"));
        assert!(!origin_matches(&u("http://api.example.com/"), "https://api.example.com"));
        assert!(!origin_matches(&u("https://api.example.com.evil.net/"), "https://api.example.com"));
        assert!(origin_matches(&u("https://a.example.org/"), "https://*.example.org"));
        assert!(!origin_matches(&u("https://example.org/"), "https://*.example.org"));
        assert!(!origin_matches(&u("https://a.b.example.org/"), "https://*.example.org"));
        assert!(!origin_matches(&u("https://aexample.org/"), "https://*.example.org"));
    }

    #[test]
    fn a_hop_is_checked_after_resolving() {
        let env = fake("hops");
        let p = params("x", &[]);
        let plan = |s: &str| plan_hop(&u(s), &p.origins, &p.private_hosts, &|h, port| env.resolve(h, port));
        let ok = plan("https://api.example.com/v1").unwrap();
        assert_eq!(ok.pin, vec!["93.184.216.34:443".parse::<SocketAddr>().unwrap()], "connects only to the checked address");
        assert_eq!(plan("http://api.example.com/").unwrap_err().code, "denied", "plain http");
        assert_eq!(plan("https://93.184.216.34/").unwrap_err().code, "denied", "an IP literal");
        assert_eq!(plan("https://[::1]/").unwrap_err().code, "denied");
        assert_eq!(plan("https://2130706433/").unwrap_err().code, "denied", "127.0.0.1 written as a number");
        assert_eq!(plan("https://other.example.net/").unwrap_err().code, "denied", "not declared");
        assert_eq!(plan("https://user:pw@api.example.com/").unwrap_err().code, "bad_request");
        assert_eq!(plan("https://missing.example.org/").unwrap_err().code, "offline", "does not resolve");
        // after DNS: private answers are refused unless the exact origin was accepted as a private host
        assert!(plan("https://a.example.org/").is_ok());
        assert_eq!(plan("https://b.example.org/").unwrap_err().code, "denied", "a wildcard never covers a private address");
        assert_eq!(plan("https://mixed.example.com/").unwrap_err().code, "denied", "one private answer among public ones");
        assert_eq!(plan("https://meta.example.com/").unwrap_err().code, "denied", "the cloud metadata address");
        assert_eq!(plan("https://v6.example.com/").unwrap_err().code, "denied", "IPv4 inside IPv6");
        assert_eq!(plan("https://nas.example.com/").unwrap().pin[0].ip().to_string(), "192.168.1.10", "accepted as a private host");
        assert!(plan("https://nas/").is_ok(), "a single-label name accepted as a private host");
        // the same host on the public list only: refused once it resolves privately
        let public_only = |s: &str| plan_hop(&u(s), &p.origins, &[], &|h, port| env.resolve(h, port));
        assert_eq!(public_only("https://nas.example.com/").unwrap_err().code, "denied");
        assert_eq!(public_only("https://nas/").unwrap_err().code, "denied", "a private name needs the consent even before DNS");
    }

    #[test]
    fn an_exact_private_consent_counts_behind_a_wildcard() {
        let env = fake("wildfirst");
        // the wildcard comes first in the list and matches too; the exact entry the user allowed inside the network wins
        let origins = vec!["https://*.example.org".to_string(), "https://b.example.org".to_string()];
        let plan = |private: &[String]| plan_hop(&u("https://b.example.org/x"), &origins, private, &|h, port| env.resolve(h, port));
        assert_eq!(plan(&["https://b.example.org".to_string()]).unwrap().pin[0].ip().to_string(), "10.0.0.5");
        assert_eq!(plan(&[]).unwrap_err().code, "denied", "without that consent it stays refused");
        assert_eq!(plan(&["https://*.example.org".to_string()]).unwrap_err().code, "denied", "a wildcard is never a private consent");
    }

    #[test]
    fn a_slow_lookup_is_given_up_at_the_deadline() {
        let t = Instant::now();
        let r = before_deadline(t + Duration::from_millis(50), || { std::thread::sleep(Duration::from_millis(1500)); 1 });
        assert!(r.is_none() && t.elapsed() < Duration::from_millis(1000));
        assert_eq!(before_deadline(Instant::now() + Duration::from_secs(5), || 7), Some(7));
        assert!(before_deadline(Instant::now(), || 7).is_none_or(|v| v == 7), "a spent deadline does not wait");
    }

    fn redirect(status: u16, to: &str) -> HopReply {
        HopReply { status, status_text: String::new(), headers: vec![("Location".into(), to.into())], body: vec![] }
    }

    fn fetch(env: &Arc<Fake>, url: &str, init: Value) -> Result<Value, HostError> {
        let p = params("x", &[]);
        let e2 = env.clone();
        fetch_with(&json!(url), &init, &p.origins, &p.private_hosts, &move |h, port| e2.resolve(h, port),
            &|hop, req, d| env.send(hop, req, d), Instant::now() + FETCH_TIMEOUT)
    }

    #[test]
    fn fetch_returns_the_answer_and_strips_what_is_not_the_plugins() {
        let env = fake("fetch");
        lock(&env.replies).push_back(HopReply { status: 201, status_text: "Created".into(),
            headers: vec![("Content-Type".into(), "text/plain".into()), ("Set-Cookie".into(), "s=1".into()), ("X-A".into(), "1".into()), ("x-a".into(), "2".into())],
            body: "héllo".as_bytes().to_vec() });
        let r = fetch(&env, "https://api.example.com/p", json!({"method": "post", "body": "{}", "headers": {"Content-Type": "application/json",
            "Authorization": "Bearer plugin-own", "Cookie": "steal=1", "Host": "evil", "Proxy-Authorization": "x", "X-Custom": "y"}})).unwrap();
        assert_eq!((r["ok"].as_bool(), r["status"].as_u64(), r["bodyText"].as_str()), (Some(true), Some(201), Some("héllo")));
        assert_eq!(r["headers"], json!({"content-type": "text/plain", "x-a": "1, 2"}), "no Set-Cookie");
        assert_eq!(r["bodyBase64"], "aMOpbGxv");
        let (_, req) = lock(&env.sent)[0].clone();
        assert_eq!(req.method, "POST");
        let names: Vec<&str> = req.headers.iter().map(|(k, _)| k.as_str()).collect();
        assert_eq!(names, ["content-type", "authorization", "x-custom"]);
        assert_eq!(req.body.as_deref(), Some(&b"{}"[..]));
        for bad in [json!({"method": "CONNECT"}), json!({"body": "x"}), json!({"method": "POST", "body": "x", "bodyBase64": "eA=="}),
            json!({"headers": {"X": "a\r\nb"}}), json!({"headers": []}), json!("str")] {
            assert_eq!(fetch(&env, "https://api.example.com/", bad.clone()).unwrap_err().code, "bad_request", "{bad}");
        }
        assert_eq!(fetch(&env, "https://api.example.com/", json!({"method": "PUT", "bodyBase64": "A".repeat(FETCH_MAX_BYTES * 2)})).unwrap_err().code, "quota");
    }

    #[test]
    fn every_redirect_is_checked_again() {
        let env = fake("redirects");
        // to an accepted origin: followed, and the plugin's Authorization does not go to another origin
        lock(&env.replies).extend([redirect(302, "https://a.example.org/next"), HopReply { status: 200, ..HopReply::default() }]);
        let r = fetch(&env, "https://api.example.com/", json!({"headers": {"Authorization": "Bearer t"}})).unwrap();
        assert_eq!(r["url"], "https://a.example.org/next");
        let sent = lock(&env.sent).clone();
        assert_eq!(sent.len(), 2);
        assert!(sent[1].1.headers.iter().all(|(k, _)| k != "authorization"));
        assert_eq!(sent[1].0.pin[0].ip().to_string(), "93.184.216.35");
        // to an origin that is not accepted, to a private address, to plain http: refused, never requested
        for to in ["https://other.example.net/", "https://b.example.org/", "http://api.example.com/", "https://127.0.0.1/", "file:///c:/x"] {
            lock(&env.sent).clear();
            lock(&env.replies).push_back(redirect(301, to));
            let e = fetch(&env, "https://api.example.com/", Value::Null).unwrap_err();
            assert_eq!(e.code, "denied", "{to}: {e:?}");
            assert_eq!(lock(&env.sent).len(), 1, "{to}: only the first hop was requested");
        }
        // 303 goes on as GET without a body
        lock(&env.sent).clear();
        lock(&env.replies).extend([redirect(303, "/done"), HopReply { status: 200, ..HopReply::default() }]);
        fetch(&env, "https://api.example.com/", json!({"method": "POST", "body": "x", "headers": {"Content-Type": "text/plain"}})).unwrap();
        let s = lock(&env.sent).clone();
        assert_eq!((s[1].1.method.as_str(), s[1].1.body.is_none(), s[1].1.headers.is_empty()), ("GET", true, true));
        // 307 keeps method and body
        lock(&env.sent).clear();
        lock(&env.replies).extend([redirect(307, "/again"), HopReply { status: 200, ..HopReply::default() }]);
        fetch(&env, "https://api.example.com/", json!({"method": "POST", "body": "x"})).unwrap();
        assert_eq!(lock(&env.sent)[1].1.body.as_deref(), Some(&b"x"[..]));
        // at most five redirects
        lock(&env.sent).clear();
        for i in 0..7 { lock(&env.replies).push_back(redirect(302, &format!("/{i}"))); }
        assert_eq!(fetch(&env, "https://api.example.com/", Value::Null).unwrap_err().code, "denied");
        assert_eq!(lock(&env.sent).len(), MAX_REDIRECTS + 1);
        // a redirect without a Location is the answer
        lock(&env.replies).clear();
        lock(&env.replies).push_back(HopReply { status: 302, ..HopReply::default() });
        assert_eq!(fetch(&env, "https://api.example.com/", Value::Null).unwrap()["status"], 302);
    }

    #[test]
    fn grants_limit_the_params() {
        let g = Grant { id: "p".into(), granted: vec!["email:read".into(), "http:fetch".into()], origins: vec!["https://a.example".into(), "https://nas.lan".into()],
            private_hosts: vec!["https://nas.lan".into(), "https://gone.lan".into()], ..Grant::default() };
        let perms = vec!["email:read".to_string(), "http:fetch".to_string(), "account:read".to_string()];
        let origins = vec!["https://a.example".to_string(), "https://nas.lan".to_string(), "https://new.example".to_string()];
        let p = params_for("p", "P", "1.0.0", &perms, &origins, &Value::Null, &g);
        assert_eq!(p.granted, ["email:read", "http:fetch"], "account:read was not accepted");
        assert_eq!(p.origins, ["https://a.example", "https://nas.lan"]);
        assert_eq!(p.private_hosts, ["https://nas.lan"]);
        let no_fetch = Grant { granted: vec!["email:read".into()], ..g };
        assert!(params_for("p", "P", "1.0.0", &perms, &origins, &Value::Null, &no_fetch).origins.is_empty());
    }

    // ----- an in-process plugin speaking the C ABI -----

    const MANIFEST: &[u8] = b"{\"api\":2,\"id\":\"dev.example.fake\",\"name\":\"Fake\",\"version\":\"1.0.0\",\"library\":{\"windows-x86_64\":\"fake.dll\",\"linux-x86_64\":\"libfake.so\"},\"permissions\":[\"account:read\"]}\0";
    const OTHER_MANIFEST: &[u8] = b"{\"api\":2,\"id\":\"dev.example.other\",\"name\":\"Other\",\"version\":\"1.0.0\",\"library\":{\"windows-x86_64\":\"o.dll\"},\"permissions\":[]}\0";
    unsafe extern "C" fn fake_manifest() -> *const c_char { MANIFEST.as_ptr() as *const c_char }
    unsafe extern "C" fn other_manifest() -> *const c_char { OTHER_MANIFEST.as_ptr() as *const c_char }
    unsafe extern "C" fn fake_handle(_: *const c_char) -> *mut c_char { HANDLED.store(true, Ordering::SeqCst); std::ptr::null_mut() }
    unsafe extern "C" fn fake_free(s: *mut c_char) { if !s.is_null() { drop(unsafe { CString::from_raw(s) }); } }
    unsafe extern "C" fn fake_shutdown() { SHUT_DOWN.store(true, Ordering::SeqCst); }
    static HANDLED: AtomicBool = AtomicBool::new(false);
    static SHUT_DOWN: AtomicBool = AtomicBool::new(false);
    static ROUND_TRIP: Mutex<Vec<String>> = Mutex::new(Vec::new());
    static HOST: Mutex<usize> = Mutex::new(0);

    unsafe extern "C" fn round_trip_init(host: *const BarbicanHost, init: *const c_char) -> *mut c_char {
        let h = unsafe { &*host };
        let mut out = vec![format!("abi={} size={}", h.abi, h.size), unsafe { CStr::from_ptr(init) }.to_string_lossy().into_owned()];
        for req in [r#"{"v":2,"method":"storage.set","args":["k",{"n":1}]}"#, r#"{"v":2,"method":"storage.get","args":["k"]}"#,
            r#"{"v":2,"method":"user.getSelf","args":[]}"#, r#"{"v":2,"method":"http.fetch","args":["https://api.example.com"]}"#,
            r#"{"v":2,"method":"ui.alert","args":["x"]}"#, "not json"] {
            let c = CString::new(req).unwrap();
            let r = unsafe { (h.call)(h.ctx, c.as_ptr()) };
            out.push(unsafe { CStr::from_ptr(r) }.to_string_lossy().into_owned());
            unsafe { (h.free_str)(h.ctx, r) };
        }
        let posted = CString::new(r#"{"v":2,"type":"result","id":1,"ok":true}"#).unwrap();
        out.push(format!("post={}", unsafe { (h.post)(h.ctx, posted.as_ptr()) }));
        let line = CString::new("hello\nfrom\u{202E}init").unwrap();
        unsafe { (h.log)(h.ctx, 9, line.as_ptr()) };
        *HOST.lock().unwrap() = host as usize;
        *ROUND_TRIP.lock().unwrap() = out;
        std::ptr::null_mut()
    }
    unsafe extern "C" fn failing_init(_: *const BarbicanHost, _: *const c_char) -> *mut c_char {
        CString::new(r#"{"v":2,"ok":false,"error":{"code":"internal","message":"model file missing"}}"#).unwrap().into_raw()
    }
    unsafe extern "C" fn slow_init(_: *const BarbicanHost, _: *const c_char) -> *mut c_char {
        std::thread::sleep(Duration::from_millis(300));
        std::ptr::null_mut()
    }

    fn native(init: InitFn, manifest: ManifestFn) -> V2Loader {
        Arc::new(move |_: &Path| Ok(NativeV2 { manifest, init, handle: fake_handle, free: fake_free, shutdown: Some(fake_shutdown) }))
    }
    fn expect() -> Expect { Expect { id: "dev.example.fake".into(), version: "1.0.0".into(), permissions: vec!["account:read".into()], ..Expect::default() } }
    fn fake_params() -> Params { Params { id: "dev.example.fake".into(), name: "Fake".into(), version: "1.0.0".into(), granted: vec!["account:read".into()], ..Params::default() } }

    /// The runtime tests share the fake plugin's statics.
    static RUNTIME_TESTS: Mutex<()> = Mutex::new(());

    #[test]
    fn abi_round_trip_with_an_in_process_plugin() {
        let _g = lock(&RUNTIME_TESTS);
        let env = fake("abi");
        SHUT_DOWN.store(false, Ordering::SeqCst);
        HANDLED.store(false, Ordering::SeqCst);
        let rt = start(&native(round_trip_init, fake_manifest), Path::new("fake.dll"), expect(), fake_params(), env.clone(), Duration::from_secs(5)).unwrap();
        let out = ROUND_TRIP.lock().unwrap().clone();
        assert_eq!(out[0], format!("abi=2 size={}", std::mem::size_of::<BarbicanHost>()));
        let init: Value = serde_json::from_str(&out[1]).unwrap();
        assert_eq!(init["plugin"], json!({"id": "dev.example.fake", "version": "1.0.0"}));
        assert_eq!(init["granted"], json!(["ui:observe", "app:lifecycle", "account:read"]));
        assert_eq!(init["accounts"], json!([{"id": "a1", "kind": "own", "active": true}]));
        assert_eq!(init["features"]["hooks"], json!(FIRED_HOOKS), "the hooks this build fires");
        assert!(!out[1].contains("ann@example.test"), "no address in init_json");
        let reply = |i: usize| serde_json::from_str::<Value>(&out[i]).unwrap();
        assert_eq!(reply(2), json!({"v": 2, "ok": true, "result": null}));
        assert_eq!(reply(3)["result"], json!({"n": 1}));
        assert_eq!(reply(4)["result"]["name"], "Ann Example");
        assert_eq!(reply(5)["error"]["code"], "denied", "http:fetch was not granted");
        assert_eq!(reply(6)["error"]["code"], "busy", "no dialog during init");
        assert_eq!(reply(7)["error"]["code"], "bad_request");
        assert_eq!(out[8], "post=0");
        assert_eq!(lock(&env.logs).as_slice(), ["plugin dev.example.fake [error]: hello frominit"]);
        assert!(!HANDLED.load(Ordering::SeqCst), "loading sends no message to the plugin");
        // after init the plugin may call from any thread
        let host = *HOST.lock().unwrap() as *const BarbicanHost;
        let call = |req: &str| -> String {
            let h = unsafe { &*host };
            let c = CString::new(req).unwrap();
            let r = unsafe { (h.call)(h.ctx, c.as_ptr()) };
            let s = unsafe { CStr::from_ptr(r) }.to_string_lossy().into_owned();
            unsafe { (h.free_str)(h.ctx, r) };
            s
        };
        let h2 = host as usize;
        let from_thread = std::thread::spawn(move || {
            let h = unsafe { &*(h2 as *const BarbicanHost) };
            let c = CString::new(r#"{"v":2,"method":"storage.keys"}"#).unwrap();
            let r = unsafe { (h.call)(h.ctx, c.as_ptr()) };
            let s = unsafe { CStr::from_ptr(r) }.to_string_lossy().into_owned();
            unsafe { (h.free_str)(h.ctx, r) };
            s
        }).join().unwrap();
        assert!(from_thread.contains(r#""result":["k"]"#), "{from_thread}");
        assert!(call(r#"{"v":2,"method":"ui.alert","args":["after init"]}"#).contains(r#""ok":true"#));
        // switched off: every call answers disabled and shutdown runs on the host thread
        rt.switch_off();
        assert!(call(r#"{"v":2,"method":"app.getInfo"}"#).contains("disabled"));
        let t = Instant::now();
        while !SHUT_DOWN.load(Ordering::SeqCst) && t.elapsed() < Duration::from_secs(2) { std::thread::sleep(Duration::from_millis(5)); }
        assert!(SHUT_DOWN.load(Ordering::SeqCst));
        let _ = std::fs::remove_dir_all(&env.dir);
    }

    #[test]
    fn an_init_error_or_another_manifest_keeps_it_off() {
        let _g = lock(&RUNTIME_TESTS);
        let env = fake("initerr");
        let e = start(&native(failing_init, fake_manifest), Path::new("x"), expect(), fake_params(), env.clone(), Duration::from_secs(5)).err().unwrap();
        assert!(e.contains("model file missing"), "{e}");
        let e = start(&native(round_trip_init, other_manifest), Path::new("x"), expect(), fake_params(), env.clone(), Duration::from_secs(5)).err().unwrap();
        assert!(e.contains("the library says dev.example.other"), "{e}");
        let mut more = expect();
        more.permissions.push("email:read".into());
        let e = start(&native(round_trip_init, fake_manifest), Path::new("x"), more, fake_params(), env.clone(), Duration::from_secs(5)).err().unwrap();
        assert!(e.contains("does not agree") && e.contains("permissions"), "{e}");
        let failing: V2Loader = Arc::new(|_: &Path| Err("missing barbican_plugin_init".into()));
        assert!(start(&failing, Path::new("x"), expect(), fake_params(), env.clone(), Duration::from_secs(5)).err().unwrap().contains("barbican_plugin_init"));
        let _ = std::fs::remove_dir_all(&env.dir);
    }

    static OVERLAP_SHUTTING: AtomicBool = AtomicBool::new(false);
    static OVERLAP_SEEN: AtomicBool = AtomicBool::new(false);
    unsafe extern "C" fn slow_shutdown() {
        OVERLAP_SHUTTING.store(true, Ordering::SeqCst);
        std::thread::sleep(Duration::from_millis(300));
        OVERLAP_SHUTTING.store(false, Ordering::SeqCst);
    }
    unsafe extern "C" fn checking_init(_: *const BarbicanHost, _: *const c_char) -> *mut c_char {
        if OVERLAP_SHUTTING.load(Ordering::SeqCst) { OVERLAP_SEEN.store(true, Ordering::SeqCst); }
        std::ptr::null_mut()
    }

    #[test]
    fn a_new_instance_waits_for_the_old_one_to_shut_down() {
        let _g = lock(&RUNTIME_TESTS);
        let env = fake("overlap");
        let lib: V2Loader = Arc::new(|_: &Path| Ok(NativeV2 { manifest: fake_manifest, init: checking_init, handle: fake_handle, free: fake_free, shutdown: Some(slow_shutdown) }));
        let path = Path::new("overlap.dll");
        OVERLAP_SEEN.store(false, Ordering::SeqCst);
        let old = start(&lib, path, expect(), fake_params(), env.clone(), Duration::from_secs(5)).unwrap();
        assert!(library_in_use(path));
        old.switch_off();
        drop(old);
        // another list entry of the same library (a refresh replaced it): its init waits for the old shutdown
        let new = start(&lib, path, expect(), fake_params(), env.clone(), Duration::from_secs(5)).unwrap();
        assert!(!OVERLAP_SEEN.load(Ordering::SeqCst), "init ran while the old instance was inside shutdown");
        // with too little time left the start is refused, not overlapped
        new.switch_off();
        let e = start(&lib, path, expect(), fake_params(), env.clone(), Duration::from_millis(20)).err().unwrap();
        assert!(e.contains("still shutting down"), "{e}");
        let t = Instant::now();
        while library_in_use(path) && t.elapsed() < Duration::from_secs(2) { std::thread::sleep(Duration::from_millis(5)); }
        assert!(!library_in_use(path), "shutdown returned and freed the library");
        let _ = std::fs::remove_dir_all(&env.dir);
    }

    #[test]
    fn an_init_over_its_budget_is_given_up_and_shut_down_later() {
        let _g = lock(&RUNTIME_TESTS);
        let env = fake("slow");
        SHUT_DOWN.store(false, Ordering::SeqCst);
        let t = Instant::now();
        let e = start(&native(slow_init, fake_manifest), Path::new("x"), expect(), fake_params(), env.clone(), Duration::from_millis(40)).err().unwrap();
        assert!(e.contains("did not finish loading") && t.elapsed() < Duration::from_millis(250), "{e}");
        assert!(library_in_use(Path::new("x")), "still inside init");
        while library_in_use(Path::new("x")) && t.elapsed() < Duration::from_secs(2) { std::thread::sleep(Duration::from_millis(5)); }
        while !SHUT_DOWN.load(Ordering::SeqCst) && t.elapsed() < Duration::from_secs(2) { std::thread::sleep(Duration::from_millis(5)); }
        assert!(SHUT_DOWN.load(Ordering::SeqCst), "a late init is shut down at once");
        let _ = std::fs::remove_dir_all(&env.dir);
    }
}

/// An in-process API 2 plugin that hears hooks, for the B4 dispatch tests (here and in `plugin_hooks`). What it does
/// with a hook message is in the payload: `act` (answer, null, error, garbage, wrongid, big, sleep, defer, call,
/// append, addbcc, blockoutside) and `ms`.
#[cfg(test)]
pub(crate) mod hook_fake {
    use super::*;

    pub const ID: &str = "dev.example.hooks";
    const MANIFEST: &[u8] = b"{\"api\":2,\"id\":\"dev.example.hooks\",\"name\":\"Hooks\",\"version\":\"1.0.0\",\"library\":{\"windows-x86_64\":\"h.dll\",\"linux-x86_64\":\"libh.so\"},\"permissions\":[\"email:read\",\"email:send\",\"email:write\"],\"hooks\":[\"onEmailOpen\",\"onEmailClose\",\"onBeforeEmailSend\",\"onTransformOutgoingEmail\"]}\0";
    pub const HOOKS: [&str; 4] = ["onEmailOpen", "onEmailClose", "onBeforeEmailSend", "onTransformOutgoingEmail"];
    /// Every message the fake was handed, in order.
    pub static SEEN: Mutex<Vec<Value>> = Mutex::new(Vec::new());
    /// Replies to the host calls it made from inside `handle` (`act: call`).
    pub static CALLS: Mutex<Vec<String>> = Mutex::new(Vec::new());
    static HOST: Mutex<usize> = Mutex::new(0);
    /// The tests that use the fake run one at a time (they share its statics).
    pub static LOCK: Mutex<()> = Mutex::new(());

    struct Env;
    impl HostEnv for Env {
        fn account(&self) -> Option<Account> {
            Some(Account { login_key: "k".into(), account_id: "a1".into(), login: "ann@example.test".into(), name: "Ann".into(), server: "https://mail.example.test".into() })
        }
        fn data_dir(&self) -> PathBuf { std::env::temp_dir().join(format!("barbican-hook-fake-{}", std::process::id())) }
        fn stored_settings(&self, _: &str) -> Value { json!({"mode": "always"}) }
        fn toast(&self, _: &str, _: &str) {}
        fn dialog(&self, _: bool, _: &str, _: &str, _: &str, _: &str) -> Result<bool, HostError> { Err(err("busy", "test")) }
        fn resolve(&self, _: &str, _: u16) -> std::io::Result<Vec<IpAddr>> { Err(std::io::Error::other("test")) }
        fn send(&self, _: &Hop, _: &Outgoing, _: Instant) -> Result<HopReply, HostError> { Err(err("offline", "test")) }
        fn log(&self, _: &str) {}
    }

    fn out(v: Value) -> *mut c_char { CString::new(v.to_string()).unwrap().into_raw() }
    unsafe extern "C" fn manifest() -> *const c_char { MANIFEST.as_ptr() as *const c_char }
    unsafe extern "C" fn init(host: *const BarbicanHost, _: *const c_char) -> *mut c_char { *lock(&HOST) = host as usize; std::ptr::null_mut() }
    unsafe extern "C" fn free(s: *mut c_char) { if !s.is_null() { drop(unsafe { CString::from_raw(s) }); } }
    fn host_call(req: &str) -> String {
        let h = unsafe { &*(*lock(&HOST) as *const BarbicanHost) };
        let c = CString::new(req).unwrap();
        let r = unsafe { (h.call)(h.ctx, c.as_ptr()) };
        let s = unsafe { CStr::from_ptr(r) }.to_string_lossy().into_owned();
        unsafe { (h.free_str)(h.ctx, r) };
        s
    }
    pub fn post(msg: &Value) -> i32 {
        let h = unsafe { &*(*lock(&HOST) as *const BarbicanHost) };
        let c = CString::new(msg.to_string()).unwrap();
        unsafe { (h.post)(h.ctx, c.as_ptr()) }
    }
    unsafe extern "C" fn handle(msg: *const c_char) -> *mut c_char {
        let v: Value = serde_json::from_str(&unsafe { CStr::from_ptr(msg) }.to_string_lossy()).unwrap_or(Value::Null);
        lock(&SEEN).push(v.clone());
        if v["type"] != "hook" { return std::ptr::null_mut(); }
        let id = v["id"].as_u64().unwrap_or(0);
        let p = &v["payload"];
        let ms = p["ms"].as_u64().unwrap_or(0);
        let answer = |r: &Value| out(json!({"v": 2, "id": id, "ok": true, "result": r}));
        match p["act"].as_str().unwrap_or("answer") {
            "null" => std::ptr::null_mut(),
            "error" => out(json!({"v": 2, "id": id, "ok": false, "error": {"code": "internal", "message": "model file missing"}})),
            "garbage" => CString::new("not json").unwrap().into_raw(),
            "wrongid" => out(json!({"v": 2, "id": id + 1, "ok": true})),
            "big" => out(json!({"v": 2, "id": id, "ok": true, "result": "x".repeat(MAX_ENVELOPE)})),
            "sleep" => { std::thread::sleep(Duration::from_millis(ms)); answer(&p["result"]) }
            "defer" => {
                if ms > 0 {
                    let r = p["result"].clone();
                    std::thread::spawn(move || { std::thread::sleep(Duration::from_millis(ms)); post(&json!({"v": 2, "type": "result", "id": id, "ok": true, "result": r})); });
                }
                out(json!({"v": 2, "id": id, "deferred": true}))
            }
            // the result is posted (and the host has it) before handle returns deferred
            "earlypost" => {
                post(&json!({"v": 2, "type": "result", "id": id, "ok": true, "result": p["result"].clone()}));
                std::thread::sleep(Duration::from_millis(ms));
                out(json!({"v": 2, "id": id, "deferred": true}))
            }
            "call" => {
                lock(&CALLS).push(host_call(&json!({"v": 2, "method": "app.getInfo", "in": id}).to_string()));
                std::thread::sleep(Duration::from_millis(ms));
                answer(&p["result"])
            }
            "append" => { let mut r = p.clone(); r["subject"] = json!(format!("{}+", p["subject"].as_str().unwrap_or(""))); answer(&r) }
            "addbcc" => { let mut r = p.clone(); r["bcc"] = json!(["Outside <eve@outside.example>"]); answer(&r) }
            "blockoutside" => answer(&json!(!p.to_string().contains("outside.example"))),
            _ => answer(&p["result"]),
        }
    }

    /// Start the fake as its own instance (`path` names it) with `granted` (of its manifest's permissions).
    pub fn run(path: &str, granted: &[&str]) -> Arc<Runtime> {
        let loader: V2Loader = Arc::new(|_: &Path| Ok(NativeV2 { manifest, init, handle, free, shutdown: None }));
        let strings = |a: &[&str]| a.iter().map(|s| s.to_string()).collect::<Vec<_>>();
        let expect = Expect { id: ID.into(), version: "1.0.0".into(), permissions: strings(&["email:read", "email:send", "email:write"]),
            hooks: strings(&HOOKS), ..Expect::default() };
        let params = Params { id: ID.into(), name: "Hooks".into(), version: "1.0.0".into(), granted: strings(granted), hooks: strings(&HOOKS),
            settings: json!({"mode": {"type": "select", "label": "Mode", "default": "warn", "options": ["always", "warn"]}}), ..Params::default() };
        start(&loader, Path::new(path), expect, params, Arc::new(Env), Duration::from_secs(5)).unwrap()
    }

    /// A [`Done`] and where its outcome arrives.
    pub fn outcome() -> (Done, mpsc::Receiver<Outcome>) {
        let (tx, rx) = mpsc::channel();
        (Box::new(move |o| { let _ = tx.send(o); }), rx)
    }

    /// The plugin's failures in a row.
    pub fn fails(c: &HostCtx) -> u32 { c.fails.load(Ordering::SeqCst) }

    /// Wait (at most 3 s) until `f` holds.
    pub fn until(f: impl Fn() -> bool) -> bool {
        let t = Instant::now();
        while !f() { if t.elapsed() > Duration::from_secs(3) { return false; } std::thread::sleep(Duration::from_millis(5)); }
        true
    }
}

#[cfg(test)]
mod hook_tests {
    use super::hook_fake::*;
    use super::*;

    fn fire(rt: &Runtime, hook: &str, payload: Value, budget: Duration) -> Outcome {
        let (d, rx) = outcome();
        rt.ctx.fire(hook, payload, budget, Some(d));
        rx.recv_timeout(Duration::from_secs(3)).expect("an outcome within 3 s")
    }

    #[test]
    fn hooks_are_delivered_with_the_envelope_and_answered() {
        let _g = lock(&LOCK);
        lock(&SEEN).clear();
        let rt = run("hooks-answer.dll", &["email:read", "email:send"]);
        assert_eq!(fire(&rt, "onEmailOpen", json!({"result": {"rows": 1}}), Duration::from_secs(2)), Outcome::Answer(json!({"rows": 1})));
        let m = lock(&SEEN).last().cloned().unwrap();
        assert_eq!((m["v"].as_u64(), m["type"].as_str(), m["name"].as_str(), m["deadline_ms"].as_u64(), m["account"].as_str()),
            (Some(2), Some("hook"), Some("onEmailOpen"), Some(2000), Some("a1")));
        assert_eq!(fire(&rt, "onEmailOpen", json!({"act": "null"}), Duration::from_secs(2)), Outcome::Answer(Value::Null), "NULL: nothing to say");
        // the old API 1 alias names the same hook
        assert!(matches!(fire(&rt, "email.details", json!({}), Duration::from_secs(2)), Outcome::Answer(_)));
        // the settings message carries what plugin.getConfig gives
        rt.ctx.notify_settings();
        assert!(until(|| lock(&SEEN).iter().any(|m| m["type"] == "settings" && m["values"] == json!({"mode": "always"}))));
        rt.switch_off();
    }

    #[test]
    fn only_declared_granted_fired_hooks_are_sent() {
        let _g = lock(&LOCK);
        let rt = run("hooks-hears.dll", &["email:read"]);
        assert!(rt.ctx.hears("onEmailOpen") && rt.ctx.hears("onEmailClose"));
        assert!(!rt.ctx.hears("onBeforeEmailSend"), "email:send was not granted");
        assert!(!rt.ctx.hears("onMailboxChange"), "not declared");
        assert!(!rt.ctx.hears("onThemeBeforeApply") && !rt.ctx.hears("no.such"), "skipped or unknown");
        lock(&SEEN).clear();
        assert!(matches!(fire(&rt, "onBeforeEmailSend", json!({}), Duration::from_secs(1)), Outcome::Dropped(_)));
        assert!(lock(&SEEN).is_empty(), "nothing was handed to the plugin");
        rt.switch_off();
        assert!(!rt.ctx.hears("onEmailOpen"), "a switched-off plugin hears nothing");
        assert_eq!(fire(&rt, "onEmailOpen", json!({}), Duration::from_secs(1)), Outcome::Dropped("it does not handle this event".into()));
    }

    #[test]
    fn a_deadline_missed_inside_handle_is_a_failure_and_marks_it_hung() {
        let _g = lock(&LOCK);
        let rt = run("hooks-slow.dll", &["email:read"]);
        let t = Instant::now();
        assert_eq!(fire(&rt, "onEmailOpen", json!({"act": "sleep", "ms": 400}), Duration::from_millis(60)), Outcome::TimedOut);
        assert!(t.elapsed() < Duration::from_millis(300), "the caller hears at the deadline, not when handle returns");
        // hung: what comes meanwhile fails at once, without waiting and without counting
        let t = Instant::now();
        assert!(matches!(fire(&rt, "onEmailOpen", json!({}), Duration::from_secs(2)), Outcome::Dropped(w) if w.contains("still busy")));
        assert!(t.elapsed() < Duration::from_millis(100));
        assert_eq!(rt.ctx.fails.load(Ordering::SeqCst), 1);
        // handle returned: the mark is cleared, its late answer dropped, and the next message is answered
        assert!(until(|| lock(&rt.ctx.msgs).hung.is_none()));
        assert_eq!(fire(&rt, "onEmailOpen", json!({"result": 7}), Duration::from_secs(2)), Outcome::Answer(json!(7)));
        assert_eq!(rt.ctx.fails.load(Ordering::SeqCst), 0, "a success ends the row");
        rt.switch_off();
    }

    #[test]
    fn a_message_waiting_behind_a_slow_one_is_dropped_at_its_own_budget() {
        let _g = lock(&LOCK);
        let rt = run("hooks-queue.dll", &["email:read"]);
        let (d1, rx1) = outcome();
        rt.ctx.fire("onEmailOpen", json!({"act": "sleep", "ms": 300, "result": 1}), Duration::from_secs(2), Some(d1));
        assert!(matches!(fire(&rt, "onEmailOpen", json!({}), Duration::from_millis(50)), Outcome::Dropped(w) if w.contains("still busy")));
        assert_eq!(rx1.recv_timeout(Duration::from_secs(3)).unwrap(), Outcome::Answer(json!(1)));
        assert_eq!(rt.ctx.fails.load(Ordering::SeqCst), 0, "a message the plugin never saw is not its failure");
        rt.switch_off();
    }

    #[test]
    fn bad_replies_fail_and_three_in_a_row_switch_it_off() {
        let _g = lock(&LOCK);
        let rt = run("hooks-bad.dll", &["email:read"]);
        for act in ["error", "garbage", "wrongid", "big"] {
            let o = fire(&rt, "onEmailOpen", json!({"act": act}), Duration::from_secs(2));
            assert!(matches!(o, Outcome::Failed(_)), "{act}: {o:?}");
            fire(&rt, "onEmailOpen", json!({}), Duration::from_secs(2));
        }
        assert!(matches!(fire(&rt, "onEmailOpen", json!({"act": "error"}), Duration::from_secs(2)), Outcome::Failed(e) if e.contains("model file missing")));
        fire(&rt, "onEmailOpen", json!({"act": "garbage"}), Duration::from_secs(2));
        assert!(!rt.ctx.is_tripped());
        fire(&rt, "onEmailOpen", json!({"act": "wrongid"}), Duration::from_secs(2));
        assert!(rt.ctx.is_tripped() && rt.ctx.is_disabled(), "three failures in a row");
        assert_eq!(fire(&rt, "onEmailOpen", json!({}), Duration::from_secs(2)), Outcome::Dropped("it does not handle this event".into()));
    }

    #[test]
    fn deferred_results_come_by_post_within_the_deadline() {
        let _g = lock(&LOCK);
        lock(&SEEN).clear();
        let rt = run("hooks-defer.dll", &["email:read"]);
        assert_eq!(fire(&rt, "onEmailOpen", json!({"act": "defer", "ms": 40, "result": "later"}), Duration::from_secs(2)), Outcome::Answer(json!("later")));
        // nothing posted in time: a failure, and the plugin hears that the message is over
        assert_eq!(fire(&rt, "onEmailOpen", json!({"act": "defer", "ms": 0}), Duration::from_millis(80)), Outcome::TimedOut);
        let id = lock(&SEEN).iter().rev().find(|m| m["type"] == "hook").unwrap()["id"].as_u64().unwrap();
        assert!(until(|| lock(&SEEN).iter().any(|m| m["type"] == "cancel" && m["ref"] == id)), "the plugin hears that its message is over");
        // a late result is accepted by post and dropped
        assert_eq!(post(&json!({"v": 2, "type": "result", "id": id, "ok": true})), 0);
        assert_eq!(rt.ctx.fails.load(Ordering::SeqCst), 1);
        rt.switch_off();
        assert_eq!(post(&json!({"v": 2, "type": "result", "id": id, "ok": true})), -3);
    }

    #[test]
    fn a_result_posted_before_handle_returns_deferred_ends_the_message() {
        let _g = lock(&LOCK);
        let rt = run("hooks-early.dll", &["email:read"]);
        assert_eq!(fire(&rt, "onEmailOpen", json!({"act": "earlypost", "ms": 30, "result": "first"}), Duration::from_secs(2)), Outcome::Answer(json!("first")));
        assert_eq!(fails(&rt.ctx), 0);
        rt.switch_off();
    }

    #[test]
    fn a_full_queue_drops_the_oldest_observer_first() {
        let _g = lock(&LOCK);
        lock(&SEEN).clear();
        let rt = run("hooks-queue.dll", &["email:read"]);
        // one slow message holds the host thread; the queue fills behind it
        let (slow, slow_rx) = outcome();
        rt.ctx.fire("onEmailOpen", json!({"act": "sleep", "ms": 300}), Duration::from_secs(5), Some(slow));
        assert!(until(|| lock(&SEEN).iter().any(|m| m["payload"]["act"] == "sleep")));
        let (waited, waited_rx) = outcome();
        rt.ctx.fire("onEmailOpen", json!({"n": "waited"}), Duration::from_secs(5), Some(waited));
        let big = "z".repeat(10_000);
        for i in 0..MAX_QUEUED + 5 { rt.ctx.fire("onEmailOpen", json!({"n": i, "pad": big}), Duration::from_secs(5), None); }
        // what waits is bounded: the dropped observers' text is freed, not left in the queue
        let held = rt.ctx.waiting_bytes();
        assert!(held < (MAX_QUEUED + 1) * (big.len() + 200), "{held} bytes wait");
        assert_eq!(slow_rx.recv_timeout(Duration::from_secs(2)).unwrap(), Outcome::Answer(Value::Null));
        // the message someone waits for was not dropped; the first observers were, and none counted as a failure
        assert_eq!(waited_rx.recv_timeout(Duration::from_secs(2)).unwrap(), Outcome::Answer(Value::Null));
        let last = json!(MAX_QUEUED + 4);
        assert!(until(|| lock(&SEEN).iter().any(|m| m["payload"]["n"] == last)), "the newest observer is delivered");
        assert!(!lock(&SEEN).iter().any(|m| m["payload"]["n"] == json!(0)), "the oldest observer was dropped");
        assert_eq!(fails(&rt.ctx), 0);
        // with only messages someone waits for in the queue, the new one is refused
        let (hold, _hold_rx) = outcome();
        rt.ctx.fire("onEmailOpen", json!({"act": "sleep", "ms": 300}), Duration::from_secs(5), Some(hold));
        assert!(until(|| lock(&SEEN).iter().filter(|m| m["payload"]["act"] == "sleep").count() == 2));
        let mut rxs = vec![];
        for _ in 0..MAX_QUEUED { let (d, rx) = outcome(); rt.ctx.fire("onEmailOpen", json!({}), Duration::from_secs(5), Some(d)); rxs.push(rx); }
        let (d, rx) = outcome();
        rt.ctx.fire("onEmailOpen", json!({}), Duration::from_secs(5), Some(d));
        assert!(matches!(rx.recv_timeout(Duration::from_millis(100)).unwrap(), Outcome::Dropped(w) if w.contains("too many")));
        rt.switch_off();
    }

    #[test]
    fn switching_off_or_cancelling_mid_call_ends_it_at_once() {
        let _g = lock(&LOCK);
        lock(&SEEN).clear();
        let rt = run("hooks-off.dll", &["email:read"]);
        let (d, rx) = outcome();
        rt.ctx.fire("onEmailOpen", json!({"act": "sleep", "ms": 300}), Duration::from_secs(5), Some(d));
        std::thread::sleep(Duration::from_millis(50));
        let (d2, rx2) = outcome();
        rt.ctx.fire("onEmailOpen", json!({}), Duration::from_secs(5), Some(d2));
        let t = Instant::now();
        rt.switch_off();
        assert_eq!(rx.recv_timeout(Duration::from_secs(1)).unwrap(), Outcome::Disabled);
        assert_eq!(rx2.recv_timeout(Duration::from_secs(1)).unwrap(), Outcome::Disabled, "queued ones too");
        assert!(t.elapsed() < Duration::from_millis(100));
        // cancel: the caller hears at once, the plugin gets `cancel` naming the message
        let rt = run("hooks-cancel.dll", &["email:read"]);
        let (d, rx) = outcome();
        let id = rt.ctx.fire("onEmailOpen", json!({"act": "sleep", "ms": 150}), Duration::from_secs(5), Some(d)).unwrap();
        assert!(until(|| lock(&SEEN).iter().any(|m| m["id"] == id && m["type"] == "hook" && m["payload"]["ms"] == 150)));
        rt.ctx.cancel(id);
        assert_eq!(rx.recv_timeout(Duration::from_secs(1)).unwrap(), Outcome::Cancelled);
        assert!(until(|| lock(&SEEN).iter().any(|m| m["type"] == "cancel" && m["ref"] == id)));
        assert_eq!(rt.ctx.fails.load(Ordering::SeqCst), 0, "a cancel is not the plugin's failure");
        rt.switch_off();
    }

    #[test]
    fn firing_never_waits_for_the_plugin() {
        let _g = lock(&LOCK);
        let rt = run("hooks-observer.dll", &["email:read"]);
        let t = Instant::now();
        for _ in 0..3 { rt.ctx.fire("onEmailOpen", json!({"act": "sleep", "ms": 200}), Duration::from_secs(5), None); }
        assert!(t.elapsed() < Duration::from_millis(50), "{:?}", t.elapsed());
        rt.switch_off();
    }

    #[test]
    fn calls_made_in_a_message_end_with_it() {
        let _g = lock(&LOCK);
        lock(&CALLS).clear();
        let rt = run("hooks-in.dll", &["email:read"]);
        let (d, rx) = outcome();
        let id = rt.ctx.fire("onEmailOpen", json!({"act": "call"}), Duration::from_secs(2), Some(d)).unwrap();
        assert!(matches!(rx.recv_timeout(Duration::from_secs(3)).unwrap(), Outcome::Answer(_)));
        assert!(lock(&CALLS)[0].contains(r#""ok":true"#), "{:?}", lock(&CALLS));
        let after = rt.ctx.request(json!({"v": 2, "method": "app.getInfo", "in": id}).to_string().as_bytes());
        assert!(after.contains("cancelled"), "{after}");
        assert!(rt.ctx.request(br#"{"v":2,"method":"app.getInfo","in":"x"}"#).contains("bad_request"));
        rt.switch_off();
    }

    #[test]
    fn a_plugin_stuck_in_one_message_past_the_hard_cap_is_switched_off() {
        let _g = lock(&LOCK);
        let rt = run("hooks-cap.dll", &["email:read"]);
        rt.ctx.set_hold_cap(Duration::from_millis(120));
        assert_eq!(fire(&rt, "onEmailOpen", json!({"act": "sleep", "ms": 600}), Duration::from_millis(30)), Outcome::TimedOut);
        assert!(until(|| rt.ctx.is_tripped()), "still inside handle at the cap");
        let _ = crate::plugins::drain_notices();
    }
}
