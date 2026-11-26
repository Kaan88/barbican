//! The tool registry and the runner (docs/AI-ASSISTANT-SPEC.md 5.1, 5.2, 5.3): what the model may
//! call, what each call risks, the card the user sees before a change, the time limits and the
//! result cap. The tools themselves are in [`mail`].
//!
//! A tool is a table row: a name and description for the model, a JSON Schema, a risk, an optional
//! `describe`/`details`/`confirmLabel` for the approval card, and a `run`. Blocking work runs off
//! the assistant's worker, so Stop and the deadline need not wait for an I/O call to return.
use crate::assistant::config::{Actions, Settings};
use crate::assistant::history::{ToolCall, ToolDef};
use crate::assistant::sse::Cancel;
use serde_json::{json, Map, Value};
use std::sync::{mpsc, Arc};
use std::time::{Duration, Instant};

pub mod mail;

#[cfg(test)]
pub(crate) mod fake;

/// `Stopped by the user.`: what a call left after Stop got back (2.0).
pub const STOPPED: &str = "Stopped by the user.";

/// `The tool input was not valid JSON. Try again.`
pub const INVALID_JSON: &str = "The tool input was not valid JSON. Try again.";

/// `MAX_RESULT`: a result is cut to this many characters.
pub const MAX_RESULT: usize = 60000;

/// `MAX_IDS`: the most messages one call may name.
pub const MAX_IDS: usize = 100;

/// The limit when neither the tool nor its risk gives one.
pub const DEFAULT_LIMIT_MS: u64 = 60000;

/// The risk levels (5.1). `risk` may be a function of the arguments, for tools that can read or
/// write depending on the call (the JMAP one).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Risk {
    /// looks something up (needs "look things up" on)
    Read,
    /// only changes what the user is shown
    Ui,
    /// changes data
    Change,
    /// sends mail or destroys data: always asked
    Confirm,
}

impl Risk {
    pub fn id(self) -> &'static str {
        match self {
            Risk::Read => "read",
            Risk::Ui => "ui",
            Risk::Change => "change",
            Risk::Confirm => "confirm",
        }
    }

    /// `LIMIT_MS`: how long a call of this risk may take.
    pub fn limit_ms(self) -> u64 {
        match self {
            Risk::Read => 90000,
            Risk::Ui => 20000,
            Risk::Change => 20000,
            Risk::Confirm => 30000,
        }
    }

    /// Whether the call is one of the lookups (`isLookup`): read, ui, or a risk that depends on
    /// the arguments.
    pub fn is_lookup(self) -> bool {
        matches!(self, Risk::Read | Risk::Ui)
    }

    /// Whether the call changes something (and so needs the action setting).
    pub fn is_action(self) -> bool {
        matches!(self, Risk::Change | Risk::Confirm)
    }
}

/// The risk of a tool: fixed, or worked out from the call's arguments.
#[derive(Clone, Copy)]
pub enum RiskOf {
    Fixed(Risk),
    PerCall(fn(&Value) -> Risk),
}

impl RiskOf {
    pub fn of(&self, args: &Value) -> Risk {
        match self {
            RiskOf::Fixed(r) => *r,
            RiskOf::PerCall(f) => f(args),
        }
    }

    /// `isLookup`: a per-call risk is always a lookup (it may be one), a fixed one when read or ui.
    pub fn is_lookup(&self) -> bool {
        match self {
            RiskOf::Fixed(r) => r.is_lookup(),
            RiskOf::PerCall(_) => true,
        }
    }
}

/// `ctx.settings`, `ctx.approve`, `ctx.mail`: everything a tool call may use.
#[derive(Clone)]
pub struct Ctx {
    /// the conversation's frozen settings
    pub settings: Arc<Settings>,
    pub host: Arc<dyn Host>,
    pub mail: Arc<dyn mail::Mail>,
    pub signal: Cancel,
    /// the 60 s folder list cache shared by the calls of one conversation
    pub folders: Arc<mail::FolderCache>,
    deadline: Option<Instant>,
}

impl Ctx {
    pub fn new(settings: &Settings, host: Arc<dyn Host>, mail: Arc<dyn mail::Mail>, signal: Cancel) -> Self {
        Self {
            settings: Arc::new(settings.clone()),
            host,
            mail,
            signal,
            folders: Arc::new(mail::FolderCache::new()),
            deadline: None,
        }
    }

    /// Check before each dispatch, not just before the tool starts. A blocking request already
    /// dispatched may still finish; no further request should start after Stop or the deadline.
    pub(crate) fn checkpoint(&self) -> Result<(), String> {
        if self.signal.is_cancelled() || self.deadline.is_some_and(|d| Instant::now() >= d) {
            Err(STOPPED.into())
        } else {
            Ok(())
        }
    }
}

/// What the panel is asked to do for a tool (5.2, 2.7): the approval card, and the four things the
/// UI tools do (M7 and M10 use the rest).
#[allow(unused_variables)]
pub trait Host: Send + Sync + 'static {
    /// `approve(description, opts)`: true = do it. With `always: false` the panel shows a `Did:`
    /// card and answers true at once. On Stop the host must dismiss the card and release its wait.
    fn approve(&self, description: &str, opts: Approve<'_>, signal: &Cancel) -> bool;
    /// Show a message in the reader.
    fn open_message(&self, message: crate::assistant::context::MessageRef<'_>) -> Result<(), String> {
        Err("Opening a message is not available here.".into())
    }
    /// Select a mail folder.
    fn show_folder(&self, login: &str, account: &str, folder: &str) -> Result<(), String> {
        Err("Showing a folder is not available here.".into())
    }
    /// Switch to Contacts.
    fn open_contacts(&self) -> Result<(), String> {
        Err("Opening the address book is not available here.".into())
    }
    /// Open the composer with these arguments (`composeParams`, 5.7).
    fn compose(&self, args: &Value) -> Result<(), String> {
        Err("The composer is not available here.".into())
    }
}

/// The approval card's inputs (2.9).
pub struct Approve<'a> {
    /// The user is asked even in Automatic mode.
    pub always: bool,
    /// The tool's name, shown on the card.
    pub name: &'a str,
    /// The details box: pre-wrap, scrolls (5.7).
    pub details: Option<String>,
    /// The primary button's label (`confirmLabel`), else `Do it`.
    pub confirm_label: Option<String>,
    /// The danger style (red): a `confirm` tool.
    pub danger: bool,
}

/// One registry row.
pub struct Tool {
    pub name: &'static str,
    pub description: &'static str,
    pub parameters: Value,
    pub risk: RiskOf,
    /// `describe`: the card's text; it can pin a resolved target into the execution arguments.
    /// `None` means `${name} ${json(args).slice(0, 300)}`.
    pub describe: Option<fn(&mut Value, &Ctx) -> Result<String, String>>,
    /// `details`: the box under the card's text.
    pub details: Option<fn(&Value) -> Option<String>>,
    /// `confirmLabel`: the primary button's label.
    pub confirm_label: Option<fn(&Value) -> Option<String>>,
    /// The work. `Ok` is the result text (or an error result), `Err` is a thrown error.
    pub run: Option<fn(&Value, &Ctx) -> Result<Outcome, String>>,
    /// `tool.timeout`: overrides the risk's limit.
    pub timeout_ms: Option<u64>,
}

impl Tool {
    pub fn risk_of(&self, args: &Value) -> Risk {
        self.risk.of(args)
    }

    pub fn is_lookup(&self) -> bool {
        self.risk.is_lookup()
    }

    /// The time limit of one call: `tool.timeout ?? LIMIT_MS[risk] ?? 60000`.
    pub fn limit_ms(&self, risk: Risk) -> u64 {
        self.timeout_ms.unwrap_or_else(|| risk.limit_ms())
    }
}

/// A tool's answer for the model.
#[derive(Clone, Debug, PartialEq)]
pub struct Outcome {
    pub content: String,
    pub is_error: bool,
    /// Search step metadata for the host, never put into the provider's tool history.
    pub found: Vec<mail::FoundMessage>,
}

impl Outcome {
    pub fn ok(text: impl Into<String>) -> Outcome {
        Outcome { content: text.into(), is_error: false, found: Vec::new() }
    }

    pub fn error(text: impl Into<String>) -> Outcome {
        Outcome { content: text.into(), is_error: true, found: Vec::new() }
    }
}

/// `json`: `JSON.stringify(v, null, 1)`, one-space indent.
pub fn json(v: &Value) -> String {
    let mut out = String::new();
    write_json(v, 0, &mut out);
    out
}

fn write_json(v: &Value, depth: usize, out: &mut String) {
    match v {
        Value::Null => out.push_str("null"),
        Value::Bool(b) => out.push_str(if *b { "true" } else { "false" }),
        Value::Number(n) => out.push_str(&n.to_string()),
        Value::String(s) => out.push_str(&serde_json::to_string(s).unwrap_or_default()),
        Value::Array(a) => {
            if a.is_empty() {
                out.push_str("[]");
                return;
            }
            out.push('[');
            for (i, x) in a.iter().enumerate() {
                if i > 0 {
                    out.push(',');
                }
                out.push('\n');
                for _ in 0..=depth {
                    out.push(' ');
                }
                write_json(x, depth + 1, out);
            }
            out.push('\n');
            for _ in 0..depth {
                out.push(' ');
            }
            out.push(']');
        }
        Value::Object(o) => {
            if o.is_empty() {
                out.push_str("{}");
                return;
            }
            out.push('{');
            for (i, (k, x)) in o.iter().enumerate() {
                if i > 0 {
                    out.push(',');
                }
                out.push('\n');
                for _ in 0..=depth {
                    out.push(' ');
                }
                out.push_str(&serde_json::to_string(k).unwrap_or_default());
                out.push_str(": ");
                write_json(x, depth + 1, out);
            }
            out.push('\n');
            for _ in 0..depth {
                out.push(' ');
            }
            out.push('}');
        }
    }
}

/// `cap(text, max)`: the text, or its first `max` characters and how many were left out.
pub fn cap(text: &str, max: usize) -> String {
    let n = text.chars().count();
    if n <= max {
        text.to_string()
    } else {
        format!("{}\n… (cut off, {} more characters)", text.chars().take(max).collect::<String>(), n - max)
    }
}

// ---------------------------------------------------------------- schema helpers (5.3)

pub fn str_(description: &str) -> Value {
    if description.is_empty() {
        json!({"type": "string"})
    } else {
        json!({"type": "string", "description": description})
    }
}

pub fn bool_(description: &str) -> Value {
    if description.is_empty() {
        json!({"type": "boolean"})
    } else {
        json!({"type": "boolean", "description": description})
    }
}

pub fn int_(description: &str) -> Value {
    if description.is_empty() {
        json!({"type": "integer"})
    } else {
        json!({"type": "integer", "description": description})
    }
}

pub fn str_list(description: &str) -> Value {
    if description.is_empty() {
        json!({"type": "array", "items": {"type": "string"}})
    } else {
        json!({"type": "array", "items": {"type": "string"}, "description": description})
    }
}

/// `obj(properties, required)`: an object schema, with a `required` list only when there is one.
pub fn obj(properties: Value, required: &[&str]) -> Value {
    let mut o = Map::new();
    o.insert("type".into(), json!("object"));
    o.insert("properties".into(), properties);
    if !required.is_empty() {
        o.insert("required".into(), json!(required));
    }
    Value::Object(o)
}

// ---------------------------------------------------------------- the registry

/// The tools in the order the model gets them: 5.4's reading tools, then 5.5's.
pub fn all() -> &'static [Tool] {
    static R: std::sync::OnceLock<Vec<Tool>> = std::sync::OnceLock::new();
    R.get_or_init(mail::tools)
}

pub fn find(name: &str) -> Option<&'static Tool> {
    all().iter().find(|t| t.name == name)
}

/// `toolsFor(settings)`: the tool definitions the model is offered. The lookups need "look things
/// up" or actions; the actions need actions.
pub fn tools_for(s: &Settings) -> Vec<ToolDef> {
    let actions = s.actions != Actions::Off;
    all()
        .iter()
        .filter(|t| if t.is_lookup() { s.lookups || actions } else { actions })
        .map(|t| ToolDef { name: t.name.to_string(), description: t.description.to_string(), parameters: t.parameters.clone() })
        .collect()
}

// ---------------------------------------------------------------- running one call

/// `runTool(call, ctx)`: the risk checks, the card, the time limit and the result cap. `Err` is a
/// thrown error, which the agent loop (2.0) hands to the model as `Failed: ${message}`.
pub fn run_tool(call: &ToolCall, ctx: &Ctx) -> Result<Outcome, String> {
    if ctx.signal.is_cancelled() {
        return Ok(Outcome::error(STOPPED));
    }
    if call.invalid {
        return Ok(Outcome::error(INVALID_JSON));
    }
    let Some(tool) = find(&call.name) else {
        return Ok(Outcome::error(cap(&format!("Unknown tool {}.", call.name), MAX_RESULT)));
    };
    let args = if call.args.is_null() { Value::Object(Map::new()) } else { call.args.clone() };
    run_registered(tool, args, ctx)
}

fn run_registered(tool: &Tool, mut args: Value, ctx: &Ctx) -> Result<Outcome, String> {
    let risk = tool.risk_of(&args);
    let cancel = &ctx.signal;

    if risk.is_action() {
        if ctx.settings.actions == Actions::Off {
            return Ok(Outcome::error("Actions are switched off in the settings; tell the user what you would do instead."));
        }
        let description = if let Some(describe) = tool.describe {
            let ms = Risk::Read.limit_ms();
            let mut prepared = args;
            match wait_for_work(ctx, Some(ms), move |c| describe(&mut prepared, c).map(|text| (text, prepared)))? {
                Wait::Done(result) => {
                    let (text, prepared) = result?;
                    args = prepared;
                    text
                }
                Wait::Stopped => return Ok(Outcome::error(STOPPED)),
                Wait::TimedOut => return Ok(Outcome::error(timeout_text(tool.name, Risk::Read, ms))),
            }
        } else {
            default_description(tool.name, &args)
        };
        let opts = Approve {
            always: risk == Risk::Confirm || ctx.settings.actions != Actions::Auto,
            name: tool.name,
            details: tool.details.and_then(|f| f(&args)),
            confirm_label: tool.confirm_label.and_then(|f| f(&args)),
            danger: risk == Risk::Confirm,
        };
        // The time the user takes to decide is not part of the tool execution budget.
        match wait_for_work(ctx, None, move |c| c.host.approve(&description, opts, &c.signal))? {
            Wait::Done(true) => {}
            Wait::Done(false) => return Ok(Outcome::ok("The user skipped this action. Don't do it again unless asked.")),
            Wait::Stopped => return Ok(Outcome::error(STOPPED)),
            Wait::TimedOut => unreachable!("an approval has no deadline"),
        }
    } else if !ctx.settings.lookups && risk == Risk::Read {
        return Ok(Outcome::error("Looking things up is switched off in the settings."));
    }

    if cancel.is_cancelled() {
        return Ok(Outcome::error(STOPPED));
    }
    let Some(run) = tool.run else { return Ok(Outcome::error(format!("Unknown tool {}.", tool.name))) };
    run_limited(tool, risk, run, args, ctx)
}

/// Runs a tool's work with the limit of 5.2, and caps the result.
fn run_limited(tool: &Tool, risk: Risk, run: fn(&Value, &Ctx) -> Result<Outcome, String>, args: Value, ctx: &Ctx) -> Result<Outcome, String> {
    let limit = tool.limit_ms(risk);
    match wait_for_work(ctx, Some(limit), move |c| run(&args, c))? {
        Wait::Done(out) => out.map(|mut o| {
            o.content = cap(&o.content, MAX_RESULT);
            o
        }),
        Wait::Stopped => Ok(Outcome::error(STOPPED)),
        Wait::TimedOut => Ok(Outcome::error(timeout_text(tool.name, risk, limit))),
    }
}

enum Wait<T> {
    Done(T),
    Stopped,
    TimedOut,
}

fn wait_for_work<T: Send + 'static>(
    ctx: &Ctx, ms: Option<u64>, f: impl FnOnce(&Ctx) -> T + Send + 'static,
) -> Result<Wait<T>, String> {
    const POLL: Duration = Duration::from_millis(20);
    let mut worker = ctx.clone();
    worker.signal = ctx.signal.child();
    worker.deadline = ms.and_then(|n| Instant::now().checked_add(Duration::from_millis(n)));
    let signal = worker.signal.clone();
    let deadline = worker.deadline;
    let (tx, rx) = mpsc::channel();
    let status = || {
        if ctx.signal.is_cancelled() {
            Some(Wait::Stopped)
        } else if deadline.is_some_and(|d| Instant::now() >= d) {
            Some(Wait::TimedOut)
        } else {
            None
        }
    };
    if let Some(result) = status() {
        return Ok(result);
    }
    // Dropping the join handle is intentional: a blocking transport call cannot be interrupted.
    // Its owned context is safe to keep alive; checkpoints prevent follow-on requests.
    std::thread::Builder::new().name("assistant-tool".into()).spawn(move || {
        if worker.checkpoint().is_ok() {
            let _ = tx.send(f(&worker));
        }
    }).map_err(|_| "Could not start the tool worker.".to_string())?;
    loop {
        if let Some(result) = status() {
            signal.cancel();
            return Ok(result);
        }
        let poll = deadline.map_or(POLL, |d| d.saturating_duration_since(Instant::now()).min(POLL));
        match rx.recv_timeout(poll) {
            Ok(out) => {
                if let Some(result) = status() {
                    signal.cancel();
                    return Ok(result);
                }
                return Ok(Wait::Done(out));
            }
            Err(mpsc::RecvTimeoutError::Disconnected) => {
                return status().ok_or_else(|| "The tool worker ended without a result.".into());
            }
            Err(mpsc::RecvTimeoutError::Timeout) => {}
        }
    }
}

/// The error a call that took longer than its limit gets (5.2).
pub fn timeout_text(name: &str, risk: Risk, ms: u64) -> String {
    let seconds = ms as f64 / 1000.0;
    match risk {
        Risk::Read | Risk::Ui => format!("{name} got no answer from Thunderbird within {seconds} seconds."),
        Risk::Change | Risk::Confirm => format!(
            "Thunderbird started this but didn't confirm it within {seconds} seconds. It has usually been done anyway: tell the user briefly to check, and don't repeat it."
        ),
    }
}


/// The description of a tool without a `describe`: `${name} ${json(args).slice(0, 300)}`.
fn default_description(name: &str, args: &Value) -> String {
    let args: String = json(args).chars().take(300).collect();
    format!("{name} {args}")
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::assistant::tools::fake::{self, TestHost};
    use serde_json::Value;

    fn ctx(s: &Settings, h: Arc<TestHost>) -> Ctx {
        Ctx::new(s, h, Arc::new(fake::mail(&fake::store(), Vec::new())), Cancel::new())
    }

    fn settings(lookups: bool, actions: Actions) -> Settings {
        let mut s = Settings::default();
        s.lookups = lookups;
        s.actions = actions;
        s
    }

    fn call(name: &str, args: Value) -> ToolCall {
        ToolCall { id: "c1".into(), name: name.into(), args, invalid: false }
    }

    fn refs() -> Value {
        json!({"ids": [fake::message("M1")]})
    }

    #[test]
    fn the_offering_matrix() {
        // lookups on, actions off: the lookups only
        let t = tools_for(&settings(true, Actions::Off));
        assert!(t.iter().any(|x| x.name == "search_mail"));
        assert!(!t.iter().any(|x| x.name == "move_messages"));
        // lookups off, actions on: everything (the read tools refuse at run time)
        assert_eq!(tools_for(&settings(false, Actions::Ask)).len(), all().len());
        assert_eq!(tools_for(&settings(true, Actions::Auto)).len(), all().len());
        // both off: none
        assert!(tools_for(&settings(false, Actions::Off)).is_empty());
        // and the definitions are the registry's
        let def = &tools_for(&settings(true, Actions::Off))[0];
        assert_eq!(def.name, "search_mail");
        assert_eq!(def.parameters["properties"]["text"]["type"], "string");
    }

    #[test]
    fn the_registry_has_the_m6_tools_and_their_risks() {
        let names: Vec<&str> = all().iter().map(|t| t.name).collect();
        assert_eq!(
            names,
            [
                "search_mail", "list_messages", "read_message", "read_attachment", "list_folders", "list_accounts", "list_tags",
                "archive_messages", "move_messages", "copy_messages", "mark_messages", "delete_messages", "create_tag"
            ]
        );
        let risk = |n: &str| find(n).unwrap().risk_of(&Value::Null);
        for n in ["search_mail", "list_messages", "read_message", "read_attachment", "list_folders", "list_accounts", "list_tags"] {
            assert_eq!(risk(n), Risk::Read, "{n}");
        }
        for n in ["archive_messages", "move_messages", "copy_messages", "mark_messages", "create_tag"] {
            assert_eq!(risk(n), Risk::Change, "{n}");
        }
        assert_eq!(risk("delete_messages"), Risk::Confirm);
        for t in all() {
            assert!(t.run.is_some(), "{}", t.name);
            assert_eq!(t.parameters["type"], "object", "{}", t.name);
            assert!(!t.description.is_empty(), "{}", t.name);
        }
        assert!(find("delete_messages").unwrap().confirm_label.is_some());
        assert_eq!(find("search_mail").unwrap().limit_ms(Risk::Read), 90000);
        assert_eq!(find("create_tag").unwrap().limit_ms(Risk::Change), 20000);
        assert_eq!(find("delete_messages").unwrap().limit_ms(Risk::Confirm), 30000);
        // a per-call risk is a lookup whatever it answers
        let per = Tool {
            name: "jmap_request",
            description: "",
            parameters: obj(json!({}), &[]),
            risk: RiskOf::PerCall(|_| Risk::Change),
            describe: None,
            details: None,
            confirm_label: None,
            run: None,
            timeout_ms: None,
        };
        assert!(per.is_lookup());
        assert_eq!(per.risk_of(&Value::Null), Risk::Change);
    }

    #[test]
    fn the_approval_matrix() {
        let store = fake::store();
        let mail = Arc::new(fake::mail(&store, Vec::new()));
        let run = |s: &Settings, h: &Arc<TestHost>, name: &str, args: Value| {
            let ctx = Ctx::new(s, h.clone(), mail.clone(), Cancel::new());
            run_tool(&call(name, args), &ctx).unwrap()
        };

        // read: never asked, runs when lookups are on, refused when they are off
        for actions in [Actions::Off, Actions::Ask, Actions::Auto] {
            let h = Arc::new(TestHost::new(true));
            assert_eq!(run(&settings(true, actions), &h, "list_tags", json!({})), Outcome::ok("[]"));
            assert_eq!(
                run(&settings(false, actions), &h, "list_tags", json!({})),
                Outcome::error("Looking things up is switched off in the settings.")
            );
            assert!(h.asked().is_empty(), "{actions:?}: a lookup is never asked");
        }

        // change: off refuses, ask asks, auto runs with a `Did:` card
        let s = settings(true, Actions::Off);
        let h = Arc::new(TestHost::new(true));
        for name in ["archive_messages", "delete_messages"] {
            assert_eq!(
                run(&s, &h, name, refs()),
                Outcome::error("Actions are switched off in the settings; tell the user what you would do instead.")
            );
        }
        assert!(h.asked().is_empty());
        assert!(store.requests().is_empty(), "disabled actions cannot fetch approval details");
        assert_eq!(store.email_of("a", "M1")["mailboxIds"]["inbox"], true);

        for (actions, always) in [(Actions::Ask, true), (Actions::Auto, false)] {
            let s = settings(true, actions);
            let h = Arc::new(TestHost::new(true));
            assert_eq!(run(&s, &h, "archive_messages", refs()), Outcome::ok("Archived."), "{actions:?}");
            let asked = h.asked();
            assert_eq!(asked.len(), 1);
            assert_eq!(asked[0].description, "Archive “Lunch on Thursday”");
            assert_eq!(asked[0].always, always, "{actions:?}");
            assert!(!asked[0].danger, "{actions:?}");
            assert_eq!(asked[0].confirm_label, None);
            // it moved, as a user action
            assert_eq!(store.email_of("a", "M1")["mailboxIds"]["archive"], true);
        }

        // confirm: always asked, with the danger style, in auto too
        for actions in [Actions::Ask, Actions::Auto] {
            let s = settings(true, actions);
            let h = Arc::new(TestHost::new(false));
            let o = run(&s, &h, "delete_messages", refs());
            assert_eq!(o, Outcome::ok("The user skipped this action. Don't do it again unless asked."));
            let asked = h.asked();
            assert_eq!(asked[0].description, "Move to the Trash: “Lunch on Thursday”");
            assert!(asked[0].always && asked[0].danger, "{actions:?}");
            assert_eq!(asked[0].confirm_label.as_deref(), Some("Move to Trash"));
        }
        // and a permanent delete says so
        let s = settings(true, Actions::Auto);
        let h = Arc::new(TestHost::new(false));
        run(&s, &h, "delete_messages", json!({"ids": [fake::message("M1")], "permanent": true}));
        assert_eq!(h.asked()[0].description, "PERMANENTLY delete (cannot be undone) “Lunch on Thursday”");
        assert_eq!(h.asked()[0].confirm_label.as_deref(), Some("Delete permanently"));
        // skipped: nothing was deleted
        assert_eq!(store.emails("a").len(), 3);

        // a skipped change did not run
        let s = settings(true, Actions::Ask);
        let h = Arc::new(TestHost::new(false));
        assert_eq!(
            run(&s, &h, "mark_messages", json!({"ids": [fake::message("M2")], "read": true})),
            Outcome::ok("The user skipped this action. Don't do it again unless asked.")
        );
        assert!(store.email_of("a", "M2")["keywords"]["$seen"].is_null());
    }

    #[test]
    fn per_call_risk_controls_execution_and_approval_metadata() {
        let host = Arc::new(TestHost::new(true));
        let ctx = ctx(&settings(false, Actions::Auto), host.clone());
        let tool = Tool {
            name: "mixed",
            description: "",
            parameters: obj(json!({}), &[]),
            risk: RiskOf::PerCall(|a| if a["write"] == true { Risk::Confirm } else { Risk::Read }),
            describe: Some(|a, _| {
                // A prepared argument doesn't reclassify the original call.
                a["write"] = json!(false);
                Ok("Delete this".into())
            }),
            details: Some(|_| Some("Message M1".into())),
            confirm_label: Some(|_| Some("Delete".into())),
            run: Some(|_, _| Ok(Outcome::ok("ran"))),
            timeout_ms: None,
        };
        assert_eq!(
            run_registered(&tool, json!({}), &ctx).unwrap(),
            Outcome::error("Looking things up is switched off in the settings.")
        );
        assert!(host.asked().is_empty());
        assert_eq!(run_registered(&tool, json!({"write": true}), &ctx).unwrap(), Outcome::ok("ran"));
        let asked = host.asked();
        assert_eq!(asked.len(), 1);
        assert!(asked[0].always && asked[0].danger);
        assert_eq!(asked[0].details.as_deref(), Some("Message M1"));
        assert_eq!(asked[0].confirm_label.as_deref(), Some("Delete"));
    }

    #[test]
    fn unknown_tools_are_an_error_result() {
        let s = settings(true, Actions::Ask);
        let ctx = ctx(&s, Arc::new(TestHost::new(true)));
        assert_eq!(run_tool(&call("nope", json!({})), &ctx).unwrap(), Outcome::error("Unknown tool nope."));
        // args that came as no object at all are still a call
        assert!(!run_tool(&call("list_tags", Value::Null), &ctx).unwrap().is_error);
    }

    #[test]
    fn the_timeout_texts() {
        assert_eq!(timeout_text("search_mail", Risk::Read, 90000), "search_mail got no answer from Thunderbird within 90 seconds.");
        assert_eq!(timeout_text("show_folder", Risk::Ui, 20000), "show_folder got no answer from Thunderbird within 20 seconds.");
        assert_eq!(
            timeout_text("move_messages", Risk::Change, 20000),
            "Thunderbird started this but didn't confirm it within 20 seconds. It has usually been done anyway: tell the user briefly to check, and don't repeat it."
        );
        assert_eq!(
            timeout_text("delete_messages", Risk::Confirm, 30000),
            "Thunderbird started this but didn't confirm it within 30 seconds. It has usually been done anyway: tell the user briefly to check, and don't repeat it."
        );
        assert_eq!(timeout_text("x", Risk::Read, 750), "x got no answer from Thunderbird within 0.75 seconds.");
    }

    #[test]
    fn a_blocking_call_times_out_before_io_returns() {
        fn slow(_: &Value, ctx: &Ctx) -> Result<Outcome, String> {
            ctx.mail.create_tag("test", "#3366FF")?;
            Ok(Outcome::ok("too late"))
        }
        let (done_tx, done_rx) = mpsc::channel();
        let (release_tx, release_rx) = mpsc::channel();
        let release_rx = std::sync::Mutex::new(release_rx);
        let mail = mail::MailStore::new(vec![], vec![]).with_saver(Arc::new(move |_| {
            // If the caller incorrectly blocks, release this test stub instead of hanging the suite.
            let _ = release_rx.lock().unwrap().recv_timeout(Duration::from_secs(3));
            done_tx.send(()).unwrap();
            Ok(())
        }));
        let ctx = Ctx::new(&settings(true, Actions::Ask), Arc::new(TestHost::new(true)), Arc::new(mail), Cancel::new());
        let tool = Tool {
            name: "slow",
            description: "slow",
            parameters: obj(json!({}), &[]),
            risk: RiskOf::Fixed(Risk::Change),
            describe: None,
            details: None,
            confirm_label: None,
            run: Some(slow),
            timeout_ms: Some(100),
        };
        assert_eq!(
            run_limited(&tool, Risk::Change, slow, json!({}), &ctx).unwrap(),
            Outcome::error(timeout_text("slow", Risk::Change, 100))
        );
        assert!(matches!(done_rx.try_recv(), Err(mpsc::TryRecvError::Empty)));
        assert!(!ctx.signal.is_cancelled());
        release_tx.send(()).unwrap();
        done_rx.recv_timeout(Duration::from_secs(3)).unwrap();
    }

    #[test]
    fn deadline_cancels_late_follow_up_operations() {
        let ctx = ctx(&settings(true, Actions::Ask), Arc::new(TestHost::new(true)));
        let (release_tx, release_rx) = mpsc::channel();
        let (status_tx, status_rx) = mpsc::channel();
        let result = wait_for_work(&ctx, Some(100), move |c| {
            let _ = release_rx.recv_timeout(Duration::from_secs(3));
            let _ = status_tx.send(c.checkpoint());
        }).unwrap();
        assert!(matches!(result, Wait::TimedOut));
        release_tx.send(()).unwrap();
        assert_eq!(status_rx.recv_timeout(Duration::from_secs(3)).unwrap(), Err(STOPPED.into()));
        assert!(ctx.checkpoint().is_ok());
    }

    #[test]
    fn a_stopped_call_is_never_run() {
        let s = settings(true, Actions::Ask);
        let h = Arc::new(TestHost::new(true));
        let store = fake::store();
        let cancel = Cancel::new();
        cancel.cancel();
        let ctx = Ctx::new(&s, h.clone(), Arc::new(fake::mail(&store, Vec::new())), cancel);
        assert_eq!(run_tool(&call("delete_messages", refs()), &ctx).unwrap(), Outcome::error(STOPPED));
        assert!(store.requests().is_empty(), "nothing went to the server");
        assert!(h.asked().is_empty());
    }

    #[test]
    fn stop_on_an_approval_is_not_a_skip_and_never_executes_the_write() {
        for answer in [false, true] {
            let cancel = Cancel::new();
            let mut host = TestHost::new(answer);
            host.cancel_on_approve = Some(cancel.clone());
            let store = fake::store();
            let ctx = Ctx::new(&settings(true, Actions::Auto), Arc::new(host), Arc::new(fake::mail(&store, vec![])), cancel);
            assert_eq!(run_tool(&call("delete_messages", refs()), &ctx).unwrap(), Outcome::error(STOPPED));
            assert!(store.calls("Email/set").is_empty());
        }
    }

    #[test]
    fn results_are_cut_at_the_cap() {
        assert_eq!(cap("abc", 10), "abc");
        assert_eq!(cap("abcdef", 3), "abc\n… (cut off, 3 more characters)");
        // one-space-indented JSON, as `JSON.stringify(v, null, 1)`
        assert_eq!(json(&json!({"a": 1, "b": [true, null]})), "{\n \"a\": 1,\n \"b\": [\n  true,\n  null\n ]\n}");
        assert_eq!(json(&json!({})), "{}");
        assert_eq!(json(&json!([1, "x"])), "[\n 1,\n \"x\"\n]");
        assert_eq!(json(&json!("a\"b")), "\"a\\\"b\"");
        // a tool without describe gets its name and one-space JSON, cut at 300
        assert_eq!(default_description("x", &json!({"a": 1})), "x {\n \"a\": 1\n}");
        let long = default_description("x", &json!({"a": "y".repeat(400)}));
        assert_eq!(long.chars().count(), 302);
    }

    #[test]
    fn the_schema_helpers_of_5_3() {
        assert_eq!(str_("Words"), json!({"type": "string", "description": "Words"}));
        assert_eq!(str_(""), json!({"type": "string"}));
        assert_eq!(bool_(""), json!({"type": "boolean"}));
        assert_eq!(int_("1-100"), json!({"type": "integer", "description": "1-100"}));
        assert_eq!(str_list(""), json!({"type": "array", "items": {"type": "string"}}));
        // no `required` key without a list
        assert!(obj(json!({}), &[]).get("required").is_none());
        assert_eq!(obj(json!({}), &["a"])["required"], json!(["a"]));
    }

    #[test]
    fn results_are_handed_to_the_model_capped() {
        // a run that returns something huge is cut at MAX_RESULT
        fn huge(_: &Value, _: &Ctx) -> Result<Outcome, String> {
            Ok(Outcome::ok("é".repeat(MAX_RESULT + 10)))
        }
        let s = settings(true, Actions::Ask);
        let ctx = ctx(&s, Arc::new(TestHost::new(true)));
        let tool = Tool {
            name: "huge",
            description: "",
            parameters: obj(json!({}), &[]),
            risk: RiskOf::Fixed(Risk::Read),
            describe: None,
            details: None,
            confirm_label: None,
            run: Some(huge),
            timeout_ms: None,
        };
        let o = run_limited(&tool, Risk::Read, huge, json!({}), &ctx).unwrap();
        assert!(o.content.ends_with("\n… (cut off, 10 more characters)"), "{}", o.content.len());
        assert_eq!(o.content.chars().count(), MAX_RESULT + "\n… (cut off, 10 more characters)".chars().count());
    }

    #[test]
    fn a_thrown_error_is_the_loops_business() {
        fn boom(_: &Value, _: &Ctx) -> Result<Outcome, String> {
            Err("the server said no".into())
        }
        let s = settings(true, Actions::Ask);
        let ctx = ctx(&s, Arc::new(TestHost::new(true)));
        let tool = Tool {
            name: "boom",
            description: "",
            parameters: obj(json!({}), &[]),
            risk: RiskOf::Fixed(Risk::Read),
            describe: None,
            details: None,
            confirm_label: None,
            run: Some(boom),
            timeout_ms: None,
        };
        assert_eq!(run_limited(&tool, Risk::Read, boom, json!({}), &ctx), Err("the server said no".into()));
    }
}
