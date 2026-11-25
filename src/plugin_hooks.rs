//! Plugin API 2 hooks for mail and the composer (ROADMAP Phase 5 step B4, first part; PLUGINS.md "What B4 built").
//!
//! [`crate::plugin_host`] delivers messages and enforces their deadlines; this module says what each hook means to
//! the app and builds what the plugins get:
//! * **observers** ([`observe`]) are fired and forgotten: nothing waits, their answers are ignored;
//! * **intercepts** ([`ask_all`]) ask every plugin at once and hand every answer back together. For the send check
//!   (`onBeforeEmailSend`, fail closed) [`send_verdict`] blocks the send on `false` and on every way of not answering
//!   (timeout, error, panic, malformed reply, switched off on the way);
//! * **transforms** ([`chain`]) run one plugin after another in the order they were switched on, each getting the
//!   previous one's result; an answer is checked ([`check_outgoing`]: its shape, its size, only the fields a plugin may
//!   change) and one that fails the check is a failure and changes nothing.
//!
//! Payloads carry only what the hook's permission covers: never a token, password or storage key, attachment blob
//! ids only with `email:blob-read`, and the text being typed (`onDraftChange`) only with `compose:check`.
//! The UI-thread glue at the bottom keeps the last open message, folder, selection and composer, so each event fires
//! once per change.
use crate::jmap::Outgoing;
use crate::plugin_host::{hook_budget, HostCtx, Outcome};
use crate::plugins::shown;
use serde_json::{json, Map, Value};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;

fn lock<T>(m: &Mutex<T>) -> std::sync::MutexGuard<'_, T> { m.lock().unwrap_or_else(|e| e.into_inner()) }

/// Longest message text a payload carries (the envelope is at most 4 MiB in all).
pub const MAX_TEXT_BYTES: usize = 1 << 20;
/// Longest subject a transform may set.
pub const MAX_SUBJECT: usize = 998;
/// Most recipients per field a transform may set.
pub const MAX_RECIPIENTS: usize = 500;
/// Most keywords a transform may set.
pub const MAX_KEYWORDS: usize = 100;

/// `s` cut to at most `max` bytes, on a character boundary.
fn cut(s: &str, max: usize) -> &str {
    if s.len() <= max { return s; }
    let mut i = max;
    while !s.is_char_boundary(i) { i -= 1; }
    &s[..i]
}

// ---------------------------------------------------------------------------------------------------------------
// dispatch: observers, intercepts, transforms
// ---------------------------------------------------------------------------------------------------------------

/// The messages of one intercept or transform in flight, so the user can cancel them.
#[derive(Clone, Default)]
pub struct Pending {
    msgs: Arc<Mutex<Vec<(Arc<HostCtx>, u64)>>>,
    cancelled: Arc<AtomicBool>,
}

impl Pending {
    /// Give up: every message still in flight ends as cancelled (the plugin gets `cancel`), and a transform chain
    /// stops without calling its `done`.
    pub fn cancel(&self) {
        self.cancelled.store(true, Ordering::SeqCst);
        let msgs = std::mem::take(&mut *lock(&self.msgs));
        for (ctx, id) in msgs { ctx.cancel(id); }
    }
    pub fn is_cancelled(&self) -> bool { self.cancelled.load(Ordering::SeqCst) }
    fn add(&self, ctx: &Arc<HostCtx>, id: Option<u64>) { if let Some(id) = id { lock(&self.msgs).push((ctx.clone(), id)); } }
}

/// Fire an observer at every target, each with its own payload; nothing waits for them.
pub fn observe(targets: &[Arc<HostCtx>], hook: &str, payload: &dyn Fn(&HostCtx) -> Value) {
    let budget = hook_budget(hook);
    for t in targets { t.fire(hook, payload(t), budget, None); }
}

/// Ask every target at once (an intercept). `done` gets every answer with the plugin's name, in target order, once
/// the last one ended (at once with nothing when there are no targets). `budget`: the catalogue's when None.
pub fn ask_all(targets: &[Arc<HostCtx>], hook: &str, budget: Option<Duration>, payload: &dyn Fn(&HostCtx) -> Value,
    done: impl FnOnce(Vec<(String, Outcome)>) + Send + 'static) -> Pending {
    let pending = Pending::default();
    if targets.is_empty() { done(vec![]); return pending; }
    let budget = budget.unwrap_or_else(|| hook_budget(hook));
    type Gathered = (Vec<Option<(String, Outcome)>>, usize, Option<Box<dyn FnOnce(Vec<(String, Outcome)>) + Send>>);
    let state: Arc<Mutex<Gathered>> = Arc::new(Mutex::new((vec![None; targets.len()], targets.len(), Some(Box::new(done)))));
    for (i, t) in targets.iter().enumerate() {
        let (st, name) = (state.clone(), t.shown_name().to_string());
        let id = t.fire(hook, payload(t), budget, Some(Box::new(move |o: Outcome| {
            let finished = {
                let mut g = lock(&st);
                g.0[i] = Some((name, o));
                g.1 -= 1;
                if g.1 == 0 { g.2.take().map(|d| (d, std::mem::take(&mut g.0))) } else { None }
            };
            if let Some((d, all)) = finished { d(all.into_iter().flatten().collect()); }
        })));
        pending.add(t, id);
    }
    pending
}

/// Checks one plugin's transform answer against the value it was given.
pub type Checker = Arc<dyn Fn(&HostCtx, &Value, Value) -> Result<Value, String> + Send + Sync>;

/// Run a transform through `targets` one after another (in enable order), each getting the previous result. An
/// answer of null or `false`, a failure or an answer `check` refuses keeps the value as it was for the next one.
/// `done` gets the final value, unless the chain was cancelled.
pub fn chain(targets: Vec<Arc<HostCtx>>, hook: &str, budget: Option<Duration>, value: Value, check: Checker,
    done: impl FnOnce(Value) + Send + 'static) -> Pending {
    let pending = Pending::default();
    let budget = budget.unwrap_or_else(|| hook_budget(hook));
    step(Arc::new(targets), 0, hook.to_string(), budget, value, check, Box::new(done), pending.clone());
    pending
}

#[allow(clippy::too_many_arguments)]
fn step(targets: Arc<Vec<Arc<HostCtx>>>, i: usize, hook: String, budget: Duration, value: Value, check: Checker,
    done: Box<dyn FnOnce(Value) + Send>, pending: Pending) {
    if pending.is_cancelled() { return; }
    let Some(t) = targets.get(i).cloned() else { done(value); return };
    let before = value.clone();
    let (c2, t2) = (check.clone(), t.clone());
    let checker: crate::plugin_host::Check = Box::new(move |answer: Value| {
        if answer.is_null() || answer == json!(false) { Ok(before) } else { c2(&t2, &before, answer) }
    });
    let (targets2, hook2, check2, pending2, kept) = (targets.clone(), hook.clone(), check.clone(), pending.clone(), value.clone());
    let id = t.fire_checked(&hook, value, budget, Some(checker), Some(Box::new(move |o: Outcome| {
        let next = match o { Outcome::Answer(v) => v, _ => kept };
        step(targets2, i + 1, hook2, budget, next, check2, done, pending2);
    })));
    pending.add(&t, id);
}

// ---------------------------------------------------------------------------------------------------------------
// the send check and the outgoing transform
// ---------------------------------------------------------------------------------------------------------------

/// What the plugins' send check says.
#[derive(Clone, Debug, PartialEq)]
pub enum Verdict {
    Go,
    /// A plugin asked to look again (`{"warning": "..."}`): held once, like the built-in warnings.
    Warn(String),
    /// A plugin said no (`false`), or did not answer in a way that counts: the message is not sent.
    Block(String),
}

/// `onBeforeEmailSend` fails closed: `false` from any plugin stops the send, and so does every outcome but an answer
/// (a timeout, an error or panic, a malformed reply, the plugin switched off on the way, a message it never got).
/// A `{"warning"}` answer holds the send once unless the user already saw a warning for this draft (`acked`).
pub fn send_verdict(results: &[(String, Outcome)], acked: bool) -> Verdict {
    let mut warn = None;
    for (name, o) in results {
        match o {
            Outcome::Answer(Value::Bool(false)) => return Verdict::Block(format!("Not sent: the plugin {name} stopped this message.")),
            Outcome::Answer(v) => {
                let w = v["warning"].as_str().map(|w| shown(w, 300)).map(|w| w.trim().to_string()).filter(|w| !w.is_empty());
                if let (Some(w), None, false) = (w, &warn, acked) { warn = Some(format!("{name}: {w}")); }
            }
            Outcome::Tripped => return Verdict::Block(format!("Not sent: the plugin {name} checks outgoing mail, but it is not running (it \
                failed to start, or was switched off for this session after repeated failures). Switch {name} off in Settings > Add-ons \
                to send without its check, or restart Barbican to try it again.")),
            other => {
                let why = other.problem().unwrap_or_default();
                return Verdict::Block(format!("Not sent: the plugin {name} could not check this message ({why}). Press Send to try again, \
                    or switch {name} off in Settings > Add-ons to send without its check."));
            }
        }
    }
    warn.map_or(Verdict::Go, Verdict::Warn)
}

/// What a send is besides the message: how the composer was opened, what the user wrote, who is outside the
/// organisation, the composer's id.
#[derive(Clone, Debug, Default)]
pub struct SendContext {
    pub mode: String,
    pub identity_id: String,
    pub authored_text: String,
    pub external: Vec<String>,
    pub composer_id: String,
}

fn recipient_list(field: &str) -> Vec<Value> {
    crate::recipients::parse_list(field).iter().filter(|r| !r.email.is_empty())
        .map(|r| json!(crate::compose::format_recipient(&r.name, &r.email))).collect()
}

/// Bulwark's `OutgoingEmail` for `onBeforeEmailSend` and `onTransformOutgoingEmail` (`email:send` covers all of it:
/// "read and change the recipients, subject, text and attachments of messages you write"). Attachments are names,
/// types and sizes; their bytes and text excerpts are not handed over. A body over [`MAX_TEXT_BYTES`] is cut: the
/// payload then says `truncated: true` and names the cut fields in `truncatedFields` (a transform may not replace
/// those, [`check_outgoing`]; the send check is still asked, on the cut text).
pub fn outgoing_payload(o: &Outgoing, cx: &SendContext) -> Value {
    let from = o.sub_address.clone().or_else(|| o.from_override.as_ref().map(|f| f.1.clone())).unwrap_or_else(|| o.from.clone());
    let cut_fields: Vec<&str> = [("textBody", o.text.len()), ("htmlBody", o.html.as_deref().map_or(0, str::len))]
        .into_iter().filter(|(_, n)| *n > MAX_TEXT_BYTES).map(|(k, _)| k).collect();
    let mut v = json!({
        "to": recipient_list(&o.to), "cc": recipient_list(&o.cc), "bcc": recipient_list(&o.bcc),
        "subject": o.subject, "htmlBody": cut(o.html.as_deref().unwrap_or(""), MAX_TEXT_BYTES), "textBody": cut(&o.text, MAX_TEXT_BYTES),
        "identityId": cx.identity_id, "fromEmail": from,
        "attachments": o.attachments.iter().map(|a| json!({"name": a.name, "type": a.mime, "size": a.size})).collect::<Vec<_>>(),
        "surface": "composer", "mode": cx.mode, "authoredText": cut(&cx.authored_text, MAX_TEXT_BYTES),
        "externalRecipients": cx.external, "keywords": o.keywords,
    });
    if let Some(r) = o.in_reply_to.first() { v["inReplyTo"] = json!(r); }
    if !cx.composer_id.is_empty() { v["composerId"] = json!(cx.composer_id); }
    if !cut_fields.is_empty() { v["truncated"] = json!(true); v["truncatedFields"] = json!(cut_fields); }
    v
}

/// The fields of a payload that were cut ([`outgoing_payload`]).
fn truncated_fields(v: &Value) -> Vec<String> {
    v["truncatedFields"].as_array().into_iter().flatten().filter_map(|x| x.as_str().map(String::from)).collect()
}

/// The bare lowercase addresses of a payload's recipients, sorted.
fn address_set(v: &Value) -> Vec<String> {
    let mut out: Vec<String> = ["to", "cc", "bcc"].iter().flat_map(|k| v[*k].as_array().cloned().unwrap_or_default())
        .filter_map(|x| x.as_str().map(String::from))
        .flat_map(|s| crate::recipients::parse_list(&s)).map(|r| r.email.trim().to_lowercase()).filter(|e| !e.is_empty()).collect();
    out.sort();
    out.dedup();
    out
}

/// Did a transform change who the message goes to (Bulwark `recipientsChanged`)? Then the send checks run again.
pub fn recipients_changed(before: &Value, after: &Value) -> bool { address_set(before) != address_set(after) }

/// The bare addresses a transform added (sorted).
pub fn added_recipients(before: &Value, after: &Value) -> Vec<String> {
    let was = address_set(before);
    address_set(after).into_iter().filter(|a| !was.contains(a)).collect()
}

fn recipients_field(v: &Value, key: &str) -> Result<Value, String> {
    let a = v.as_array().ok_or_else(|| format!("{key} must be a list of addresses"))?;
    if a.len() > MAX_RECIPIENTS { return Err(format!("{key} has more than {MAX_RECIPIENTS} addresses")); }
    let mut out = vec![];
    for x in a {
        let s = x.as_str().ok_or_else(|| format!("{key} must hold text"))?;
        if s.len() > 998 || s.chars().any(|c| c.is_control()) { return Err(format!("{key} holds an address that is too long or has control characters")); }
        let parsed = crate::recipients::parse_list(s);
        if parsed.len() != 1 || !crate::recipients::invalid_addresses(&parsed).is_empty() || parsed[0].group.is_some() {
            return Err(format!("{key} holds something that is not one valid address: {}", shown(s, 80)));
        }
        out.push(json!(crate::compose::format_recipient(&parsed[0].name, &parsed[0].email)));
    }
    Ok(Value::Array(out))
}

/// Check one plugin's `onTransformOutgoingEmail` answer against the message it got (Bulwark `hookResultFor`): it
/// may change the recipients (each one valid address), the subject (one line), the bodies (at most 1 MiB each; the
/// HTML only when the message has one) and, with `email:write`, the keywords for the Sent copy (RFC 8621 keywords,
/// no system ones but `$flagged`). Everything else (sender, identity, attachments, threading, the composer id) stays
/// as it was, whatever the answer says. An answer that is not an object, or a field of the wrong shape or size, is
/// refused as a whole; so is a new body for a field the plugin got cut (`truncatedFields`): it never saw all of it.
pub fn check_outgoing(may_write: bool, before: &Value, answer: Value) -> Result<Value, String> {
    let Value::Object(a) = answer else { return Err("the answer must be the message (an object)".into()) };
    let cut = truncated_fields(before);
    let mut next = before.clone();
    for key in ["to", "cc", "bcc"] {
        if let Some(v) = a.get(key) { next[key] = recipients_field(v, key)?; }
    }
    if let Some(v) = a.get("subject") {
        let s = v.as_str().ok_or("subject must be text")?;
        if s.chars().count() > MAX_SUBJECT || s.chars().any(|c| c.is_control()) { return Err("subject must be one line of at most 998 characters".into()); }
        next["subject"] = json!(s);
    }
    for key in ["textBody", "htmlBody"] {
        if let Some(v) = a.get(key) {
            let s = v.as_str().ok_or_else(|| format!("{key} must be text"))?;
            if cut.iter().any(|c| c == key) {
                // the cut text handed back unchanged is no change
                if s == before[key].as_str().unwrap_or("") { continue; }
                return Err(format!("{key} was cut to 1 MiB for the plugin, so it cannot replace it"));
            }
            if s.len() > MAX_TEXT_BYTES { return Err(format!("{key} is larger than 1 MiB")); }
            if key == "htmlBody" && before["htmlBody"].as_str().unwrap_or("").is_empty() && !s.is_empty() {
                return Err("htmlBody cannot be added to a plain-text message".into());
            }
            next[key] = json!(s);
        }
    }
    if let (true, Some(v)) = (may_write, a.get("keywords")) {
        let list = v.as_array().ok_or("keywords must be a list")?;
        if list.len() > MAX_KEYWORDS { return Err(format!("more than {MAX_KEYWORDS} keywords")); }
        let mut out = vec![];
        for k in list {
            let k = k.as_str().ok_or("keywords must be text")?;
            let system = k.starts_with('$') && k != "$flagged" && !k.starts_with("$label:");
            if !crate::jmap::valid_keyword(k) || system { return Err(format!("{} is not a keyword a plugin may set", shown(k, 60))); }
            out.push(json!(k));
        }
        next["keywords"] = Value::Array(out);
    }
    Ok(next)
}

/// Put a checked transform result back into the message: recipients, subject, bodies, keywords. A body that was
/// cut for the plugins (`truncatedFields`) stays as the user wrote it.
pub fn apply_outgoing(o: &mut Outgoing, v: &Value) {
    let cut = truncated_fields(v);
    let whole = |k: &str| !cut.iter().any(|c| c == k);
    let join = |k: &str| v[k].as_array().map(|a| a.iter().filter_map(|x| x.as_str()).collect::<Vec<_>>().join(", ")).unwrap_or_default();
    o.to = join("to");
    o.cc = join("cc");
    o.bcc = join("bcc");
    if let Some(s) = v["subject"].as_str() { o.subject = s.to_string(); }
    if let (Some(s), true) = (v["textBody"].as_str(), whole("textBody")) { o.text = s.to_string(); }
    if let (Some(s), true, true) = (v["htmlBody"].as_str(), o.html.is_some(), whole("htmlBody")) { o.html = Some(s.to_string()); }
    if let Some(k) = v["keywords"].as_array() { o.keywords = k.iter().filter_map(|x| x.as_str().map(String::from)).collect(); }
}

/// The [`Checker`] of `onTransformOutgoingEmail`.
pub fn outgoing_checker() -> Checker {
    Arc::new(|ctx: &HostCtx, before: &Value, answer: Value| check_outgoing(ctx.holds_permission("email:write"), before, answer))
}

// ---------------------------------------------------------------------------------------------------------------
// payloads of the mail and composer observers
// ---------------------------------------------------------------------------------------------------------------

fn people(v: &Value) -> Value {
    Value::Array(v.as_array().into_iter().flatten().map(|x| json!({"name": x["name"].as_str().unwrap_or(""), "email": x["email"].as_str().unwrap_or("")})).collect())
}

fn keys_true(v: &Value) -> Vec<String> {
    v.as_object().into_iter().flatten().filter(|(_, x)| x.as_bool() == Some(true)).map(|(k, _)| k.clone()).collect()
}

/// Bulwark's `EmailReadView` for `onEmailOpen` (`email:read`: senders, recipients, subject, the full text, the
/// headers). Attachment blob ids only with `email:blob-read` (they name the bytes); no message source.
pub fn email_read_view(m: &Value, holds: &dyn Fn(&str) -> bool) -> Value {
    let blobs = holds("email:blob-read");
    let kw = &m["keywords"];
    let attachments: Vec<Value> = m["attachments"].as_array().into_iter().flatten().map(|a| {
        let mut x = json!({"name": a["name"].as_str().unwrap_or("attachment"), "type": a["type"].as_str().unwrap_or("application/octet-stream"),
            "size": a["size"].as_u64().unwrap_or(0), "inline": a["disposition"] == "inline" || a["cid"].is_string()});
        if blobs { x["blobId"] = a["blobId"].clone(); }
        if let Some(c) = a["cid"].as_str() { x["cid"] = json!(c.trim_matches(['<', '>'])); }
        x
    }).collect();
    let mut headers = Map::new();
    for h in m["headers"].as_array().into_iter().flatten() {
        let (Some(n), Some(v)) = (h["name"].as_str(), h["value"].as_str()) else { continue };
        let v = json!(v.trim());
        match headers.get_mut(n) {
            Some(Value::Array(a)) => a.push(v),
            Some(old) => { let first = old.take(); *old = json!([first, v]); }
            None => { headers.insert(n.to_string(), v); }
        }
    }
    let (_, text) = crate::body_text(m);
    let mut out = json!({
        "id": m["id"].as_str().unwrap_or(""), "threadId": m["threadId"].as_str().unwrap_or(""),
        "mailboxIds": keys_true(&m["mailboxIds"]), "from": people(&m["from"]), "to": people(&m["to"]), "cc": people(&m["cc"]),
        "subject": m["subject"].as_str().unwrap_or(""), "receivedAt": m["receivedAt"].as_str().unwrap_or(""),
        "isRead": kw["$seen"].as_bool() == Some(true), "isFlagged": kw["$flagged"].as_bool() == Some(true),
        "hasAttachment": m["hasAttachment"].as_bool().unwrap_or(!attachments.is_empty()), "attachments": attachments,
        "preview": m["preview"].as_str().unwrap_or(""), "keywords": keys_true(kw), "text": cut(&text, MAX_TEXT_BYTES),
        "source": "",
    });
    if !headers.is_empty() { out["headers"] = Value::Object(headers); }
    // a message of another signed-in login (the unified view): its parts cannot be read through this one
    if !m["__jmap"].is_null() { out["blobsReadable"] = json!(false); }
    out
}

/// Bulwark's `DraftView` for `onDraftChange`. The text being typed (`htmlBody`, `textBody`) goes only to a plugin
/// with `compose:check` ("read what you write in the composer while you type"); `email:read` alone gets the
/// recipients, subject and attachment names, and `bodyWithheld: true`.
pub fn draft_view(o: &Outgoing, identity_id: &str, holds: &dyn Fn(&str) -> bool) -> Value {
    let typing = holds("compose:check");
    let mut v = json!({
        "to": recipient_list(&o.to), "cc": recipient_list(&o.cc), "bcc": recipient_list(&o.bcc), "subject": o.subject,
        "htmlBody": if typing { cut(o.html.as_deref().unwrap_or(""), MAX_TEXT_BYTES) } else { "" },
        "textBody": if typing { cut(&o.text, MAX_TEXT_BYTES) } else { "" },
        "identityId": identity_id,
        "attachments": o.attachments.iter().map(|a| json!({"name": a.name, "type": a.mime, "size": a.size})).collect::<Vec<_>>(),
    });
    if !typing { v["bodyWithheld"] = json!(true); }
    if let Some(r) = o.in_reply_to.first() { v["inReplyTo"] = json!(r); }
    v
}

/// `onAfterEmailSend`: ids and addresses, no text.
pub fn sent_payload(o: &Outgoing, account: &str, submission: Option<&str>, scheduled: Option<i64>, composer_id: &str) -> Value {
    let when = scheduled.and_then(chrono::DateTime::from_timestamp_millis).map(|d| d.to_rfc3339_opts(chrono::SecondsFormat::Secs, true));
    json!({"emailId": Value::Null, "accountId": account, "submissionId": submission, "to": recipient_list(&o.to), "cc": recipient_list(&o.cc),
        "bcc": recipient_list(&o.bcc), "subject": o.subject, "scheduledFor": when, "composerId": composer_id})
}

// ---------------------------------------------------------------------------------------------------------------
// the app's event points (UI thread)
// ---------------------------------------------------------------------------------------------------------------

#[derive(Default)]
struct Seen {
    /// the message `onEmailOpen` last fired for, until it closes
    open: Option<String>,
    folder: Option<String>,
    selection: Option<Vec<String>>,
    /// the composer that is open (its id), whether it was sent, its numbering
    composer: Option<String>,
    sent: bool,
    seq: u64,
    /// the composer content `onDraftChange` last saw, and since when it changed without being told
    draft_key: Option<u64>,
    draft_since: Option<std::time::Instant>,
}

thread_local! { static SEEN: std::cell::RefCell<Seen> = std::cell::RefCell::new(Seen::default()); }

fn with<R>(f: impl FnOnce(&mut Seen) -> R) -> R { SEEN.with(|s| f(&mut s.borrow_mut())) }

/// The running API 2 plugins that hear `hook`.
fn targets(hook: &str) -> Vec<Arc<HostCtx>> { crate::plugin_hook_targets(hook) }

/// How long the composer must be quiet before `onDraftChange` (Bulwark debounces it).
pub const DRAFT_QUIET: Duration = Duration::from_millis(1000);

/// A message is shown in the reader (`onEmailOpen`, once per message until it closes).
pub fn email_opened(m: &Value) {
    let Some(id) = m["id"].as_str() else { return };
    let prev = with(|s| s.open.replace(id.to_string()));
    if prev.as_deref() == Some(id) { return; }
    if let Some(p) = prev { observe(&targets("onEmailClose"), "onEmailClose", &|_| json!({"emailId": p})); }
    let t = targets("onEmailOpen");
    if t.is_empty() { return; }
    observe(&t, "onEmailOpen", &|c| email_read_view(m, &|p| c.holds_permission(p)));
}

/// A watched property changed (`app.slint` `plugin-watch`): the open message, the folder, the composer.
pub fn watch(u: &crate::AppWindow, what: &str) {
    match what {
        "mail" => {
            let now = u.get_current_mail().to_string();
            let closed = with(|s| s.open.take_if(|o| *o != now));
            if let Some(p) = closed { observe(&targets("onEmailClose"), "onEmailClose", &|_| json!({"emailId": p})); }
        }
        "folder" => {
            let now = u.get_current_folder().to_string();
            let prev = with(|s| s.folder.replace(now.clone()));
            if prev.as_deref() == Some(now.as_str()) { return; }
            let t = targets("onMailboxChange");
            if t.is_empty() { return; }
            let role = crate::mail::mailbox_role(&now);
            observe(&t, "onMailboxChange", &|_| json!({"mailboxId": now, "role": if role.is_empty() { Value::Null } else { json!(role) }, "previousMailboxId": prev}));
        }
        "composing" if !u.get_composing() => {
            crate::compose::cancel_send_check(u, false);
            composer_closed(crate::compose::current_draft_id());
        }
        _ => {}
    }
}

/// The ticked messages of the list changed (`onEmailSelectionChange`, ids only, sorted).
pub fn selection(ids: &std::collections::HashSet<String>) {
    let mut now: Vec<String> = ids.iter().cloned().collect();
    now.sort();
    let changed = with(|s| {
        if s.selection.as_ref() == Some(&now) { return false; }
        // the first list after start (or sign-in) with nothing ticked is no change
        let first = s.selection.is_none() && now.is_empty();
        s.selection = Some(now.clone());
        !first
    });
    if changed { observe(&targets("onEmailSelectionChange"), "onEmailSelectionChange", &|_| json!({"emailIds": now})); }
}

/// A composer opened (`onComposerOpen`). One still open is closed first.
pub fn composer_opened(mode: &str, in_reply_to: Option<&str>, original_subject: Option<&str>) {
    let old = with(|s| s.composer.clone());
    if old.is_some() { composer_closed(None); }
    let id = with(|s| {
        s.seq += 1;
        let id = format!("composer-{}", s.seq);
        (s.composer, s.sent, s.draft_key, s.draft_since) = (Some(id.clone()), false, None, None);
        id
    });
    let t = targets("onComposerOpen");
    if t.is_empty() { return; }
    let mut v = json!({"mode": mode, "composerId": id});
    if let Some(r) = in_reply_to { v["inReplyToId"] = json!(r); }
    if let Some(s) = original_subject { v["originalSubject"] = json!(s); }
    observe(&t, "onComposerOpen", &|_| v.clone());
}

/// The open composer's id ("" when none is open).
pub fn composer_id() -> String { with(|s| s.composer.clone().unwrap_or_default()) }

/// The open composer's message is on its way (its close says `sent: true`).
pub fn composer_sent() { with(|s| s.sent = true); }

/// The composer closed (`onComposerClose`, once).
pub fn composer_closed(draft_id: Option<String>) {
    let Some((id, sent)) = with(|s| s.composer.take().map(|c| (c, s.sent))) else { return };
    observe(&targets("onComposerClose"), "onComposerClose", &|_| json!({"composerId": id, "draftId": draft_id, "sent": sent}));
}

/// Autosave's tick (every 500 ms while the composer is open) with the content's key: `onDraftChange` once the
/// content changed and then stayed for [`DRAFT_QUIET`]. `view` builds the payload for one plugin.
pub fn draft_tick(key: u64, view: &dyn Fn(&HostCtx) -> Value) {
    let due = with(|s| {
        if s.composer.is_none() { return false; }
        match s.draft_key {
            None => { s.draft_key = Some(key); false }
            Some(k) if k != key => { s.draft_key = Some(key); s.draft_since = Some(std::time::Instant::now()); false }
            Some(_) => s.draft_since.is_some_and(|t| t.elapsed() >= DRAFT_QUIET) && { s.draft_since = None; true },
        }
    });
    if !due { return; }
    let t = targets("onDraftChange");
    if !t.is_empty() { observe(&t, "onDraftChange", view); }
}

/// A draft was saved (`onDraftAutoSave`).
pub fn draft_saved(draft_id: &str, subject: &str) {
    observe(&targets("onDraftAutoSave"), "onDraftAutoSave", &|_| json!({"draftId": draft_id, "subject": subject, "composerId": composer_id()}));
}

/// The server took the message (`onAfterEmailSend`).
pub fn email_sent(o: &Outgoing, account: &str, submission: Option<&str>, scheduled: Option<i64>, composer_id: &str) {
    let t = targets("onAfterEmailSend");
    if t.is_empty() { return; }
    let v = sent_payload(o, account, submission, scheduled, composer_id);
    observe(&t, "onAfterEmailSend", &|_| v.clone());
}

/// Does a plugin check or change outgoing mail? Then the send waits for them. A switched-on plugin with the send check
/// that does not run counts too: the send check fails closed, so it stops the send ([`check_send`]).
pub fn send_hooked() -> bool {
    !targets("onBeforeEmailSend").is_empty() || !targets("onTransformOutgoingEmail").is_empty() || !guards().is_empty()
}

/// The plugins switched on with the send check that do not run: switched off by the failure limit, or failed to load
/// or start (their names).
fn guards() -> Vec<String> { crate::plugin_send_guards("onBeforeEmailSend") }

/// Run the plugins' send check on `payload`; `done` runs on the UI thread with every answer. A switched-on plugin with
/// the check that does not run answers [`Outcome::Tripped`]: not sent until the user switches it off.
pub fn check_send(payload: Value, done: impl FnOnce(Vec<(String, Outcome)>) + Send + 'static) -> Pending {
    let tripped: Vec<(String, Outcome)> = guards().into_iter().map(|n| (n, Outcome::Tripped)).collect();
    ask_all(&targets("onBeforeEmailSend"), "onBeforeEmailSend", None, &|_| payload.clone(),
        move |mut all| { all.extend(tripped); let _ = slint::invoke_from_event_loop(move || done(all)); })
}

/// Run the plugins' outgoing transforms on `payload`; `done` runs on the UI thread with the result (the payload
/// itself when nobody changed it). With no plugin it runs at once.
pub fn transform_send(payload: Value, done: impl FnOnce(Value) + Send + 'static) -> Pending {
    let t = targets("onTransformOutgoingEmail");
    if t.is_empty() { done(payload); return Pending::default(); }
    chain(t, "onTransformOutgoingEmail", None, payload, outgoing_checker(), move |v| { let _ = slint::invoke_from_event_loop(move || done(v)); })
}

/// A plugin's settings changed: it gets the `settings` message.
pub fn settings_changed(ctx: Option<Arc<HostCtx>>) { if let Some(c) = ctx { c.notify_settings(); } }

#[cfg(test)]
mod tests {
    use super::*;

    fn outgoing() -> Outgoing {
        Outgoing { from: "ann@example.test".into(), to: "Bob <bob@example.test>, carl@example.org".into(), cc: String::new(), bcc: "audit@example.test".into(),
            subject: "Plan".into(), text: "See the plan.\n> quoted".into(), html: Some("<p>See the plan.</p>".into()),
            attachments: vec![crate::jmap::Attachment { blob_id: "B1".into(), name: "plan.pdf".into(), mime: "application/pdf".into(), size: 1200 }],
            in_reply_to: vec!["<m1@example.test>".into()], keywords: vec!["$label:work".into()], ..Outgoing::default() }
    }

    #[test]
    fn verdict_fails_closed() {
        let ok = |s: &str| (s.to_string(), Outcome::Answer(Value::Null));
        assert_eq!(send_verdict(&[ok("A"), ok("B")], false), Verdict::Go);
        assert_eq!(send_verdict(&[], false), Verdict::Go);
        for (o, needle) in [(Outcome::TimedOut, "did not answer in time"), (Outcome::Failed("boom".into()), "boom"), (Outcome::Disabled, "switched off"),
            (Outcome::Dropped("it is still busy".into()), "still busy"), (Outcome::Cancelled, "cancelled")] {
            let v = send_verdict(&[ok("A"), ("Guard".into(), o.clone())], true);
            let Verdict::Block(m) = v else { panic!("{o:?} must block") };
            assert!(m.contains("Guard") && m.contains(needle) && m.contains("Settings > Add-ons"), "{m}");
        }
        assert!(matches!(send_verdict(&[ok("A"), ("No".into(), Outcome::Answer(json!(false)))], true), Verdict::Block(m) if m.contains("No stopped")));
        // a warning holds once; seen already, it lets the send go, but a failure still blocks
        let w = ("W".to_string(), Outcome::Answer(json!({"warning": "No attachment\u{202E}?"})));
        assert_eq!(send_verdict(&[w.clone()], false), Verdict::Warn("W: No attachment?".into()));
        assert_eq!(send_verdict(&[w.clone()], true), Verdict::Go);
        assert!(matches!(send_verdict(&[w, ("X".into(), Outcome::TimedOut)], false), Verdict::Block(_)));
    }

    #[test]
    fn the_outgoing_payload_and_what_a_transform_may_change() {
        let o = outgoing();
        let p = outgoing_payload(&o, &SendContext { mode: "reply".into(), composer_id: "composer-1".into(), ..Default::default() });
        assert_eq!(p["to"], json!(["Bob <bob@example.test>", "carl@example.org"]));
        assert_eq!(p["bcc"], json!(["audit@example.test"]));
        assert_eq!(p["attachments"], json!([{"name": "plan.pdf", "type": "application/pdf", "size": 1200}]), "no blob id");
        assert_eq!((p["inReplyTo"].as_str(), p["composerId"].as_str(), p["mode"].as_str()), (Some("<m1@example.test>"), Some("composer-1"), Some("reply")));
        // allowed fields change, others are put back
        let ans = json!({"subject": "Plan v2", "textBody": "new", "fromEmail": "evil@example.net", "identityId": "x", "composerId": "other",
            "attachments": [], "to": ["Dan <dan@example.test>"]});
        let next = check_outgoing(false, &p, ans).unwrap();
        assert_eq!((next["subject"].as_str(), next["textBody"].as_str()), (Some("Plan v2"), Some("new")));
        assert_eq!((next["fromEmail"].clone(), next["composerId"].clone(), next["attachments"].clone()), (p["fromEmail"].clone(), p["composerId"].clone(), p["attachments"].clone()));
        assert_eq!(next["to"], json!(["Dan <dan@example.test>"]));
        assert!(recipients_changed(&p, &next));
        assert!(!recipients_changed(&p, &json!({"to": ["carl@EXAMPLE.org", "bob@example.test"], "bcc": ["Audit <audit@example.test>"]})), "same addresses, other spelling");
        // keywords need email:write
        let kw = json!({"keywords": ["$flagged", "proj-x"]});
        assert_eq!(check_outgoing(false, &p, kw.clone()).unwrap()["keywords"], json!(["$label:work"]));
        assert_eq!(check_outgoing(true, &p, kw).unwrap()["keywords"], json!(["$flagged", "proj-x"]));
        assert!(check_outgoing(true, &p, json!({"keywords": ["$seen"]})).is_err(), "a system keyword");
        // refused as a whole
        for bad in [json!("text"), json!([1]), json!({"to": "bob@example.test"}), json!({"to": ["not an address"]}), json!({"cc": ["a@b.test, c@d.test"]}),
            json!({"subject": "two\nlines"}), json!({"subject": 5}), json!({"textBody": "x".repeat(MAX_TEXT_BYTES + 1)}),
            json!({"to": vec!["a@example.test"; MAX_RECIPIENTS + 1]})] {
            assert!(check_outgoing(true, &p, bad.clone()).is_err(), "{}", shown(&bad.to_string(), 80));
        }
        // a body over 1 MiB is cut for the plugins, said so, and cannot be replaced; the user's text is sent
        let mut long = outgoing();
        long.text = "y".repeat(MAX_TEXT_BYTES + 10);
        let lp = outgoing_payload(&long, &SendContext::default());
        assert_eq!((lp["truncated"].as_bool(), lp["truncatedFields"].clone()), (Some(true), json!(["textBody"])));
        assert_eq!(lp["textBody"].as_str().map(str::len), Some(MAX_TEXT_BYTES));
        assert!(check_outgoing(false, &lp, json!({"textBody": "short"})).is_err(), "a cut body cannot be replaced");
        let same = check_outgoing(false, &lp, json!({"textBody": lp["textBody"].clone(), "subject": "S"})).unwrap();
        let mut sent = long.clone();
        apply_outgoing(&mut sent, &same);
        assert_eq!((sent.text.len(), sent.subject.as_str()), (MAX_TEXT_BYTES + 10, "S"), "the whole text is kept");
        assert!(p.get("truncated").is_none());
        let plain = json!({"htmlBody": ""});
        assert!(check_outgoing(false, &plain, json!({"htmlBody": "<b>x</b>"})).is_err(), "no HTML into a plain-text message");
        // applied back
        let mut o2 = outgoing();
        apply_outgoing(&mut o2, &next);
        assert_eq!((o2.to.as_str(), o2.bcc.as_str(), o2.subject.as_str(), o2.text.as_str()), ("Dan <dan@example.test>", "audit@example.test", "Plan v2", "new"));
        assert_eq!(o2.from, "ann@example.test");
    }

    use crate::plugin_host::hook_fake::{self, run, until, LOCK, SEEN};
    use std::sync::mpsc;

    fn ask(targets: &[Arc<HostCtx>], payload: Value, budget: Duration) -> Vec<(String, Outcome)> {
        let (tx, rx) = mpsc::channel();
        ask_all(targets, "onBeforeEmailSend", Some(budget), &|_| payload.clone(), move |all| { let _ = tx.send(all); });
        rx.recv_timeout(Duration::from_secs(3)).expect("every answer within 3 s")
    }

    #[test]
    fn the_send_check_fails_closed_for_every_way_of_not_answering() {
        let _g = lock(&LOCK);
        let rt = run("hooks-send.dll", &["email:send"]);
        let t = vec![rt.ctx.clone()];
        let s = Duration::from_secs(2);
        assert_eq!(send_verdict(&ask(&t, json!({"result": true}), s), false), Verdict::Go);
        for (act, budget) in [("sleep", Duration::from_millis(40)), ("error", s), ("garbage", s)] {
            let all = ask(&t, json!({"act": act, "ms": 300}), budget);
            assert!(matches!(send_verdict(&all, true), Verdict::Block(m) if m.contains("could not check")), "{act}: {all:?}");
            // let a hung fake return before the next round (its answer is dropped)
            assert!(until(|| send_verdict(&ask(&t, json!({}), s), false) == Verdict::Go), "{act}");
        }
        assert!(matches!(send_verdict(&ask(&t, json!({"result": false}), s), true), Verdict::Block(m) if m.contains("stopped this message")));
        assert!(matches!(send_verdict(&ask(&t, json!({"result": {"warning": "Really?"}}), s), false), Verdict::Warn(m) if m == "Hooks: Really?"));
        // switched off while it checks: not sent
        let (tx, rx) = mpsc::channel();
        ask_all(&t, "onBeforeEmailSend", Some(Duration::from_secs(5)), &|_| json!({"act": "sleep", "ms": 300}), move |all| { let _ = tx.send(all); });
        std::thread::sleep(Duration::from_millis(30));
        rt.switch_off();
        let all = rx.recv_timeout(Duration::from_secs(1)).unwrap();
        assert!(matches!(send_verdict(&all, true), Verdict::Block(m) if m.contains("switched off")), "{all:?}");
        // asked anyway once it is off (the app would not: it is no target then), it answers at once that it is off,
        // and that blocks too
        assert!(matches!(send_verdict(&ask(&t, json!({}), s), false), Verdict::Block(_)));
        // what the app does for a plugin with the check that the failure limit switched off: it blocks, and says how
        // to send without it
        let Verdict::Block(m) = send_verdict(&[("Guard".into(), Outcome::Tripped)], true) else { panic!("a tripped guard must block") };
        assert!(m.contains("not running") && m.contains("failed to start") && m.contains("Settings > Add-ons"), "{m}");
    }

    #[test]
    fn cancel_stops_the_check_without_an_answer() {
        let _g = lock(&LOCK);
        let rt = run("hooks-send-cancel.dll", &["email:send"]);
        let (tx, rx) = mpsc::channel();
        let p = ask_all(&[rt.ctx.clone()], "onBeforeEmailSend", Some(Duration::from_secs(5)), &|_| json!({"act": "sleep", "ms": 200}), move |all| { let _ = tx.send(all); });
        std::thread::sleep(Duration::from_millis(20));
        p.cancel();
        let all = rx.recv_timeout(Duration::from_secs(1)).unwrap();
        assert_eq!(all[0].1, Outcome::Cancelled);
        assert!(until(|| lock(&SEEN).iter().any(|m| m["type"] == "cancel")));
        rt.switch_off();
    }

    #[test]
    fn transforms_chain_are_checked_and_a_recipient_change_is_checked_again() {
        let _g = lock(&LOCK);
        let a = run("hooks-tr-a.dll", &["email:send"]);
        let b = run("hooks-tr-b.dll", &["email:send"]);
        let o = outgoing();
        let base = outgoing_payload(&o, &SendContext::default());
        let go = |targets: Vec<Arc<HostCtx>>, v: Value| {
            let (tx, rx) = mpsc::channel();
            chain(targets, "onTransformOutgoingEmail", Some(Duration::from_secs(2)), v, outgoing_checker(), move |v| { let _ = tx.send(v); });
            rx.recv_timeout(Duration::from_secs(3)).expect("the chain ends")
        };
        // one after another, each on the previous result
        let mut p = base.clone();
        p["act"] = json!("append");
        let r = go(vec![a.ctx.clone(), b.ctx.clone()], p.clone());
        assert_eq!(r["subject"], "Plan++");
        assert!(!recipients_changed(&p, &r));
        // an invalid answer keeps the value and counts as a failure
        let mut bad = base.clone();
        bad["act"] = json!("answer");
        bad["result"] = json!({"to": ["not an address"]});
        let r = go(vec![a.ctx.clone()], bad.clone());
        assert_eq!(r["to"], base["to"]);
        assert!(until(|| hook_fake::fails(&a.ctx) == 1));
        // a timeout keeps it too
        let mut slow = base.clone();
        slow["act"] = json!("sleep");
        slow["ms"] = json!(200);
        let (tx, rx) = mpsc::channel();
        chain(vec![b.ctx.clone()], "onTransformOutgoingEmail", Some(Duration::from_millis(30)), slow.clone(), outgoing_checker(), move |v| { let _ = tx.send(v); });
        assert_eq!(rx.recv_timeout(Duration::from_secs(1)).unwrap(), slow);
        // a plugin adds an outside Bcc: the recipients changed, so the send check runs again on the new message and
        // a check that refuses outside recipients stops it
        assert!(until(|| matches!(ask(&[b.ctx.clone()], json!({}), Duration::from_secs(2))[0].1, Outcome::Answer(_))), "b returned from the slow one");
        let mut add = base.clone();
        add["act"] = json!("addbcc");
        let r = go(vec![a.ctx.clone()], add);
        assert!(recipients_changed(&base, &r));
        assert_eq!(r["bcc"], json!(["Outside <eve@outside.example>"]));
        let mut again = r.clone();
        again["act"] = json!("blockoutside");
        assert!(matches!(send_verdict(&ask(&[b.ctx.clone()], again, Duration::from_secs(2)), false), Verdict::Block(m) if m.contains("stopped")));
        a.switch_off();
        b.switch_off();
    }

    #[test]
    fn payloads_hold_only_what_the_permission_covers() {
        let m = json!({"id": "M1", "threadId": "T1", "mailboxIds": {"inbox": true}, "keywords": {"$seen": true, "$flagged": false},
            "from": [{"name": "Bob", "email": "bob@example.test"}], "to": [{"email": "ann@example.test"}], "subject": "Hi", "preview": "Hello",
            "attachments": [{"blobId": "B9", "name": "a.png", "type": "image/png", "size": 5, "cid": "<c1>"}],
            "headers": [{"name": "Received", "value": " a"}, {"name": "Received", "value": "b"}, {"name": "X-Spam", "value": "no"}],
            "bodyValues": {"1": {"value": "Hello there"}}, "textBody": [{"partId": "1", "type": "text/plain"}]});
        let none = email_read_view(&m, &|_| false);
        assert_eq!((none["id"].as_str(), none["isRead"].as_bool(), none["isFlagged"].as_bool()), (Some("M1"), Some(true), Some(false)));
        assert_eq!(none["keywords"], json!(["$seen"]));
        assert_eq!(none["attachments"][0].get("blobId"), None, "no blob id without email:blob-read");
        assert_eq!(none["attachments"][0]["cid"], "c1");
        assert_eq!(none["headers"]["Received"], json!(["a", "b"]));
        assert_eq!(none["source"], "");
        assert_eq!(email_read_view(&m, &|p| p == "email:blob-read")["attachments"][0]["blobId"], "B9");
        // the text being typed needs compose:check
        let o = outgoing();
        let d = draft_view(&o, "I1", &|_| false);
        assert_eq!((d["textBody"].as_str(), d["htmlBody"].as_str(), d["bodyWithheld"].as_bool()), (Some(""), Some(""), Some(true)));
        assert_eq!(d["subject"], "Plan");
        let d = draft_view(&o, "I1", &|p| p == "compose:check");
        assert_eq!((d["textBody"].as_str(), d.get("bodyWithheld")), (Some("See the plan.\n> quoted"), None));
        // nothing secret anywhere
        let s = sent_payload(&o, "a1", Some("S1"), Some(0), "composer-2");
        assert_eq!(s["scheduledFor"], "1970-01-01T00:00:00Z");
        for v in [none, d, s, outgoing_payload(&o, &SendContext::default())] {
            let t = v.to_string().to_lowercase();
            assert!(!t.contains("token") && !t.contains("password") && !t.contains("b1\""), "{t}");
        }
    }
}
