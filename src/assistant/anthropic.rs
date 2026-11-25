//! The Anthropic Messages API client (spec 3.2): `POST <base>/messages`, streaming, with tool
//! calls, thinking blocks kept with their signatures, the effort level, model names and the
//! Models API list.
//!
//! The body has `model`, `max_tokens: 16000`, `stream: true`, `system`, `messages`,
//! `output_config: {effort}` (only when an effort is set and the model takes one) and `tools`
//! (only when there are any). Nothing else: no temperature, top_p, stop sequences, thinking config,
//! tool_choice, metadata or cache_control.
use super::error::{js_string, truthy, ProviderError};
use super::history::{AssistantTurn, ChatOutcome, Entry, Raw, ToolCall, ToolDef};
use super::http;
use super::openai::trim_url;
use super::sse::{read_sse, too_large, Cancel, IDLE, MAX_EVENT};
use serde_json::{json, Value};
use std::collections::{BTreeMap, HashSet};
use std::sync::OnceLock;
use std::time::Duration;

/// `ANTHROPIC_VERSION`, sent as `anthropic-version`.
pub const ANTHROPIC_VERSION: &str = "2026-10-21";
/// `MAX_TOKENS`
pub const MAX_TOKENS: u32 = 16000;
/// The settings' default address and model.
pub const DEFAULT_BASE_URL: &str = "https://api.anthropic.com/v1";
pub const DEFAULT_MODEL: &str = "claude-haiku-5-5";

/// A model as the panel lists it.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ModelInfo {
    pub id: String,
    pub name: String,
}

/// `CLAUDE_API_MODELS`: the built-in list (the Models API list replaces it once fetched).
pub const CLAUDE_API_MODELS: [(&str, &str); 4] = [
    ("claude-opus-5-5", "Opus 5.5"),
    ("claude-sonnet-5-5", "Sonnet 5.5"),
    ("claude-haiku-5-5", "Haiku 5.5"),
    ("claude-fable-5-1", "Fable 5.1"),
];

/// `EFFORTS`: the effort menu, `""` being the model's default.
pub const EFFORTS: [(&str, &str); 5] = [
    ("", "Default effort"),
    ("low", "Low effort"),
    ("medium", "Medium effort"),
    ("high", "High effort"),
    ("max", "Max effort"),
];

fn regex(cell: &'static OnceLock<regex::Regex>, pattern: &str) -> &'static regex::Regex {
    cell.get_or_init(|| regex::Regex::new(pattern).expect("a valid pattern"))
}

/// `modelName(id, known)`: a readable name for a Claude model id ("claude-opus-5-5" → "Opus 5.5").
/// The known list first, then the built-in one, then the id's own parts, else the id.
pub fn model_name(id: &str, known: &[ModelInfo]) -> String {
    let builtin = CLAUDE_API_MODELS.iter().map(|(i, n)| (*i, *n));
    let found = known.iter().map(|m| (m.id.as_str(), m.name.as_str())).chain(builtin).find(|(i, _)| *i == id);
    if let Some((_, name)) = found.filter(|(_, n)| !n.is_empty()) {
        return name.to_string();
    }
    static NAME: OnceLock<regex::Regex> = OnceLock::new();
    match regex(&NAME, r"^claude-([a-z]+)-([0-9]+)(?:-([0-9]+))?(?:-[0-9]{8})?$").captures(id) {
        Some(c) => {
            let family = &c[1];
            let minor = c.get(3).map(|m| format!(".{}", m.as_str())).unwrap_or_default();
            format!("{}{} {}{minor}", family[..1].to_uppercase(), &family[1..], &c[2])
        }
        None => id.to_string(),
    }
}

/// `supportsEffort(model)`: whether the Messages API takes `output_config.effort` for this model
/// (the Claude 5 family, Opus 4.6 and later, Sonnet 4.6, and Claude Code's aliases).
pub fn supports_effort(model: &str) -> bool {
    static FAMILY: OnceLock<regex::Regex> = OnceLock::new();
    static OPUS: OnceLock<regex::Regex> = OnceLock::new();
    static SONNET: OnceLock<regex::Regex> = OnceLock::new();
    static ALIAS: OnceLock<regex::Regex> = OnceLock::new();
    let id = model.to_lowercase();
    regex(&FAMILY, "claude-(fable|mythos|opus|sonnet|haiku)-5").is_match(&id)
        || regex(&OPUS, "claude-opus-4-[6-9]").is_match(&id)
        || regex(&SONNET, "claude-sonnet-4-6").is_match(&id)
        || regex(&ALIAS, "^(opus|sonnet|haiku|fable|default)$").is_match(&id)
}

/// Content as an array of blocks (a string becomes one text block).
fn as_blocks(content: Value) -> Vec<Value> {
    match content {
        Value::Array(a) => a,
        Value::String(s) => vec![json!({"type": "text", "text": s})],
        other => vec![other],
    }
}

/// Adds a message; two user messages in a row become one, so tool results and the next user
/// text share a message.
fn push(out: &mut Vec<Value>, role: &str, content: Value) {
    if role == "user" {
        if let Some(last) = out.last_mut().filter(|l| l["role"] == "user") {
            let mut a = as_blocks(last["content"].take());
            a.extend(as_blocks(content));
            last["content"] = Value::Array(a);
            return;
        }
    }
    out.push(json!({"role": role, "content": content}));
}

/// `toAnthropicMessages(history, {stripThinking})`.
pub fn to_messages(history: &[Entry], strip_thinking: bool) -> Vec<Value> {
    let mut out = Vec::new();
    for h in history {
        match h {
            Entry::User { text } => push(&mut out, "user", json!(text)),
            Entry::Assistant(t) => {
                let mut content: Vec<Value> = match &t.raw {
                    // Anthropic's own turn goes back unchanged, thinking blocks and signatures included
                    Raw::Anthropic { content } => content
                        .iter()
                        .filter(|b| !strip_thinking || !matches!(b["type"].as_str(), Some("thinking" | "redacted_thinking")))
                        .cloned()
                        .collect(),
                    _ => {
                        let mut c = Vec::new();
                        if !t.text.is_empty() {
                            c.push(json!({"type": "text", "text": t.text}));
                        }
                        for call in &t.tool_calls {
                            let input = if truthy(&call.args) { call.args.clone() } else { json!({}) };
                            c.push(json!({"type": "tool_use", "id": call.id, "name": call.name, "input": input}));
                        }
                        c
                    }
                };
                if content.is_empty() {
                    content = vec![json!({"type": "text", "text": "(no answer)"})];
                }
                out.push(json!({"role": "assistant", "content": content}));
            }
            Entry::Tool { results } => {
                let blocks = results
                    .iter()
                    .map(|r| {
                        let mut b = json!({"type": "tool_result", "tool_use_id": r.id, "content": r.content});
                        if r.is_error {
                            b["is_error"] = json!(true);
                        }
                        b
                    })
                    .collect();
                push(&mut out, "user", Value::Array(blocks));
            }
        }
    }
    out
}

/// The request body.
pub fn request_body(model: &str, system: &str, history: &[Entry], tools: &[ToolDef], strip_thinking: bool, effort: &str) -> Value {
    let mut body = json!({
        "model": model,
        "max_tokens": MAX_TOKENS,
        "stream": true,
        "system": system,
        "messages": to_messages(history, strip_thinking),
    });
    if !effort.is_empty() && supports_effort(model) {
        body["output_config"] = json!({"effort": effort});
    }
    if !tools.is_empty() {
        body["tools"] = tools.iter().map(|t| json!({"name": t.name, "description": t.description, "input_schema": t.parameters})).collect();
    }
    body
}

/// Appends to a string field of a block (`(block.f || "") + s`).
fn append(block: &mut Value, field: &str, s: &str) {
    let mut v = block[field].as_str().unwrap_or("").to_string();
    v.push_str(s);
    block[field] = Value::String(v);
}

/// Assembles a turn from the stream's events (`anthropicRequest` after the response is OK).
struct Assembler {
    blocks: BTreeMap<u64, Value>,
    json: BTreeMap<u64, String>,
    invalid: HashSet<u64>,
    stop_reason: Option<String>,
    wrote_text: bool,
    /// bytes taken in so far, and the most allowed ([`MAX_EVENT`])
    size: usize,
    limit: usize,
}

impl Assembler {
    fn new(limit: usize) -> Assembler {
        Assembler { blocks: BTreeMap::new(), json: BTreeMap::new(), invalid: HashSet::new(), stop_reason: None, wrote_text: false, size: 0, limit }
    }

    fn event(&mut self, data: &str, on_text: &mut dyn FnMut(&str)) -> Result<bool, ProviderError> {
        // unparsable data lines are skipped
        let Ok(ev) = serde_json::from_str::<Value>(data) else { return Ok(true) };
        let index = ev["index"].as_u64();
        match ev["type"].as_str() {
            Some("content_block_start") => {
                self.grow(data.len())?;
                let mut block = ev["content_block"].clone();
                if !block.is_object() {
                    return Ok(true);
                }
                if block["type"] == "text" {
                    if !truthy(&block["text"]) {
                        block["text"] = json!("");
                    }
                    if self.wrote_text {
                        on_text("\n\n");
                    }
                }
                if let Some(i) = index {
                    if block["type"] == "tool_use" {
                        self.json.insert(i, String::new());
                    }
                    self.blocks.insert(i, block);
                }
            }
            Some("content_block_delta") => {
                let Some(i) = index else { return Ok(true) };
                let Some(block) = self.blocks.get_mut(&i) else { return Ok(true) };
                let d = &ev["delta"];
                let field = |k: &str| d[k].as_str().unwrap_or("");
                match d["type"].as_str() {
                    Some("text_delta") => {
                        let t = field("text");
                        append(block, "text", t);
                        self.wrote_text = true;
                        if !t.is_empty() {
                            on_text(t);
                        }
                    }
                    Some("input_json_delta") => self.json.entry(i).or_default().push_str(field("partial_json")),
                    Some("thinking_delta") => append(block, "thinking", field("thinking")),
                    Some("signature_delta") => append(block, "signature", field("signature")),
                    _ => {}
                }
                self.grow(data.len())?;
            }
            Some("content_block_stop") => {
                let Some(i) = index else { return Ok(true) };
                if let Some(block) = self.blocks.get_mut(&i).filter(|b| b["type"] == "tool_use") {
                    let text = self.json.get(&i).map(String::as_str).unwrap_or("");
                    block["input"] = if text.is_empty() {
                        json!({})
                    } else {
                        match serde_json::from_str::<Value>(text) {
                            Ok(v) => v,
                            Err(_) => {
                                self.invalid.insert(i);
                                json!({})
                            }
                        }
                    };
                }
            }
            // `ev.delta?.stop_reason ?? stopReason`
            Some("message_delta") => match &ev["delta"]["stop_reason"] {
                Value::Null => {}
                Value::String(s) => self.stop_reason = Some(s.clone()),
                other => self.stop_reason = Some(other.to_string()),
            },
            Some("error") => {
                let m = &ev["error"]["message"];
                return Err(ProviderError::new(if truthy(m) { js_string(m) } else { "The service reported an error.".into() }));
            }
            // message_start, ping, message_stop and anything new
            _ => {}
        }
        Ok(true)
    }

    fn grow(&mut self, n: usize) -> Result<(), ProviderError> {
        self.size += n;
        if self.size > self.limit { Err(too_large()) } else { Ok(()) }
    }

    fn finish(self) -> Result<ChatOutcome, ProviderError> {
        if self.stop_reason.as_deref() == Some("refusal") {
            return Err(ProviderError::new("The model declined to answer this request."));
        }
        let tool_calls = self
            .blocks
            .iter()
            .filter(|(_, b)| b["type"] == "tool_use")
            .map(|(i, b)| ToolCall {
                id: b["id"].as_str().unwrap_or("").to_string(),
                name: b["name"].as_str().unwrap_or("").to_string(),
                args: b["input"].clone(),
                invalid: self.invalid.contains(i),
            })
            .collect();
        let text = self.blocks.values().filter(|b| b["type"] == "text").map(|b| b["text"].as_str().unwrap_or("")).collect::<Vec<_>>().join("\n\n");
        let content = self.blocks.into_values().collect();
        Ok(ChatOutcome { turn: AssistantTurn { text, tool_calls, raw: Raw::Anthropic { content } }, stop_reason: self.stop_reason })
    }
}

/// An Anthropic service as the settings describe it. Its `Debug` hides the key.
#[derive(Clone)]
pub struct Anthropic {
    /// default [`DEFAULT_BASE_URL`]
    pub base_url: String,
    pub model: String,
    /// sent trimmed as `x-api-key`
    pub api_key: String,
    /// `""` (the model's default), `low`, `medium`, `high` or `max`; sent only to models that take it
    pub effort: String,
    /// the stream's idle limit, [`IDLE`] (tests make it short)
    pub idle: Duration,
}

impl std::fmt::Debug for Anthropic {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Anthropic")
            .field("base_url", &self.base_url)
            .field("model", &self.model)
            .field("api_key", &if self.api_key.trim().is_empty() { "" } else { "<redacted>" })
            .field("effort", &self.effort)
            .field("idle", &self.idle)
            .finish()
    }
}

/// The `x-api-key` and `anthropic-version` headers (the browser-only
/// `anthropic-dangerous-direct-browser-access` is not sent).
fn auth_headers(key: &str) -> Result<reqwest::header::HeaderMap, ProviderError> {
    http::check_key(key)?;
    let mut h = reqwest::header::HeaderMap::new();
    h.insert("x-api-key", http::secret_header(key)?);
    h.insert("anthropic-version", reqwest::header::HeaderValue::from_static(ANTHROPIC_VERSION));
    Ok(h)
}

impl Anthropic {
    pub fn new(base_url: &str, model: &str, api_key: &str) -> Anthropic {
        Anthropic { base_url: trim_url(base_url), model: model.to_string(), api_key: api_key.to_string(), effort: String::new(), idle: IDLE }
    }

    /// One model turn (`anthropicChat`). Blocks until the turn is complete; `on_text` gets each
    /// piece of visible text as it streams in (and `"\n\n"` between text blocks). The same rules
    /// as [`OpenAi::chat`](super::openai::OpenAi::chat): call it from a plain worker thread, and
    /// keep `on_text` short and free of async or blocking HTTP.
    ///
    /// A 400 whose message mentions thinking is retried once with the thinking blocks of earlier
    /// turns left out (edited history can invalidate them); a 400 about `effort` or
    /// `output_config` is retried once without the effort. Nothing else is retried.
    pub fn chat(
        &self,
        system: &str,
        history: &[Entry],
        tools: &[ToolDef],
        cancel: &Cancel,
        on_text: &mut dyn FnMut(&str),
    ) -> Result<ChatOutcome, ProviderError> {
        super::block_on(self.chat_async(system, history, tools, cancel, on_text))?
    }

    async fn chat_async(
        &self,
        system: &str,
        history: &[Entry],
        tools: &[ToolDef],
        cancel: &Cancel,
        on_text: &mut dyn FnMut(&str),
    ) -> Result<ChatOutcome, ProviderError> {
        let url = format!("{}/messages", self.base_url);
        http::check_url(&url)?;
        let headers = auth_headers(self.api_key.trim())?;
        let client = http::client()?;
        let mut strip_thinking = false;
        let mut effort = self.effort.as_str();
        loop {
            let body = request_body(&self.model, system, history, tools, strip_thinking, effort);
            let req = client.post(&url).headers(headers.clone()).header("content-type", "application/json").body(body.to_string());
            match self.request(req, cancel, on_text).await {
                Err(e) if e.status == Some(400) => {
                    let m = e.message.to_lowercase();
                    if m.contains("thinking") && !strip_thinking {
                        strip_thinking = true;
                    } else if (m.contains("effort") || m.contains("output_config")) && !effort.is_empty() {
                        effort = "";
                    } else {
                        return Err(e);
                    }
                }
                other => return other,
            }
        }
    }

    async fn request(&self, req: reqwest::RequestBuilder, cancel: &Cancel, on_text: &mut dyn FnMut(&str)) -> Result<ChatOutcome, ProviderError> {
        let mut resp = http::send(req, cancel).await?;
        if !resp.status().is_success() {
            return Err(http::error_of(resp, cancel).await?);
        }
        let mut asm = Assembler::new(MAX_EVENT);
        read_sse(&mut resp, cancel, self.idle, |data| asm.event(&data, on_text)).await?;
        asm.finish()
    }
}

/// `encodeURIComponent`: everything but `A-Z a-z 0-9 - _ . ! ~ * ' ( )` percent-encoded.
fn encode_uri_component(s: &str) -> String {
    s.bytes()
        .map(|b| match b {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'!' | b'~' | b'*' | b'\'' | b'(' | b')' => (b as char).to_string(),
            _ => format!("%{b:02X}"),
        })
        .collect()
}

/// The most pages of the Models API that are read.
pub const MODEL_PAGES: usize = 5;

/// `fetchAnthropicModels`: `GET <base>/models?limit=100[&after_id=<last_id>]`, up to
/// [`MODEL_PAGES`] pages, each model as `{id, name: display_name || modelName(id)}`. A non-OK
/// answer fails with `The Models API answered <status>: <first 200 characters of the body>`.
/// (The settings page then keeps only the ids that start with `claude-`.)
pub fn fetch_models(base_url: &str, api_key: &str, cancel: &Cancel) -> Result<Vec<ModelInfo>, ProviderError> {
    super::block_on(fetch_models_async(trim_url(base_url), api_key.trim().to_string(), cancel))?
}

async fn fetch_models_async(base: String, key: String, cancel: &Cancel) -> Result<Vec<ModelInfo>, ProviderError> {
    let headers = auth_headers(&key)?;
    let client = http::client()?;
    let mut out = Vec::new();
    let mut after = String::new();
    for _ in 0..MODEL_PAGES {
        let mut url = format!("{base}/models?limit=100");
        if !after.is_empty() {
            url.push_str(&format!("&after_id={}", encode_uri_component(&after)));
        }
        http::check_url(&url)?;
        let mut resp = http::send(client.get(&url).headers(headers.clone()), cancel).await?;
        let status = resp.status().as_u16();
        if !resp.status().is_success() {
            let body = http::read_body(&mut resp, cancel, http::MAX_ERROR_BODY).await?.unwrap_or_default();
            let text: String = String::from_utf8_lossy(&body).chars().take(200).collect();
            return Err(ProviderError::new(format!("The Models API answered {status}: {text}")));
        }
        let not_json = || ProviderError::new("The Models API sent an answer that is not JSON.");
        let body = http::read_body(&mut resp, cancel, MAX_EVENT).await?.ok_or_else(ProviderError::network)?;
        let body: Value = serde_json::from_slice(&body).map_err(|_| not_json())?;
        for m in body["data"].as_array().into_iter().flatten() {
            let Some(id) = m["id"].as_str() else { continue };
            let name = m["display_name"].as_str().filter(|n| !n.is_empty()).map(String::from).unwrap_or_else(|| model_name(id, &[]));
            out.push(ModelInfo { id: id.to_string(), name });
        }
        let last = &body["last_id"];
        if !truthy(&body["has_more"]) || !truthy(last) {
            break;
        }
        after = js_string(last);
    }
    Ok(out)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::assistant::error::ErrorKind;
    use crate::assistant::fake::{FakeServer, Reply};
    use crate::assistant::history::ToolResult;
    use std::time::Instant;

    fn tool() -> ToolDef {
        ToolDef { name: "search".into(), description: "Search mail".into(), parameters: json!({"type": "object", "properties": {"q": {"type": "string"}}}) }
    }

    fn user(text: &str) -> Vec<Entry> {
        vec![Entry::User { text: text.into() }]
    }

    fn run(p: &Anthropic, history: &[Entry], tools: &[ToolDef], cancel: &Cancel) -> (Result<ChatOutcome, ProviderError>, String) {
        let mut shown = String::new();
        let r = p.chat("sys", history, tools, cancel, &mut |t| shown.push_str(t));
        (r, shown)
    }

    /// A recorded stream: each event as `event: <type>` plus its data, with CRLF line ends.
    fn sse(events: &[Value]) -> String {
        events.iter().map(|e| format!("event: {}\r\ndata: {e}\r\n\r\n", e["type"].as_str().unwrap())).collect()
    }

    fn start(i: u64, block: Value) -> Value {
        json!({"type": "content_block_start", "index": i, "content_block": block})
    }

    fn delta(i: u64, d: Value) -> Value {
        json!({"type": "content_block_delta", "index": i, "delta": d})
    }

    fn stop(i: u64) -> Value {
        json!({"type": "content_block_stop", "index": i})
    }

    fn end(reason: &str) -> Vec<Value> {
        vec![
            json!({"type": "message_delta", "delta": {"stop_reason": reason, "stop_sequence": null}, "usage": {"output_tokens": 9}}),
            json!({"type": "message_stop"}),
        ]
    }

    fn text_answer(text: &str) -> String {
        let mut ev = vec![
            json!({"type": "message_start", "message": {"id": "msg_1", "type": "message", "role": "assistant", "content": []}}),
            start(0, json!({"type": "text", "text": ""})),
            delta(0, json!({"type": "text_delta", "text": text})),
            stop(0),
        ];
        ev.extend(end("end_turn"));
        sse(&ev)
    }

    fn ok(text: &str) -> Reply {
        Reply::stream(200, vec![text_answer(text).into_bytes()])
    }

    /// thinking, text, a tool call and more text, as the API streams them
    fn full_stream() -> String {
        let mut ev = vec![
            json!({"type": "message_start", "message": {"id": "msg_1", "type": "message", "role": "assistant", "content": [], "model": "claude-opus-5-5"}}),
            start(0, json!({"type": "thinking", "thinking": "", "signature": ""})),
            delta(0, json!({"type": "thinking_delta", "thinking": "The user wants "})),
            json!({"type": "ping"}),
            delta(0, json!({"type": "thinking_delta", "thinking": "the invoice."})),
            delta(0, json!({"type": "signature_delta", "signature": "EqQBCkYI"})),
            delta(0, json!({"type": "signature_delta", "signature": "AhgCKkA="})),
            stop(0),
            start(1, json!({"type": "text", "text": ""})),
            delta(1, json!({"type": "text_delta", "text": "Let me "})),
            delta(1, json!({"type": "text_delta", "text": "lööк."})),
            stop(1),
            start(2, json!({"type": "tool_use", "id": "toolu_01", "name": "search", "input": {}})),
            delta(2, json!({"type": "input_json_delta", "partial_json": ""})),
            delta(2, json!({"type": "input_json_delta", "partial_json": "{\"q\": \"inv"})),
            delta(2, json!({"type": "input_json_delta", "partial_json": "oice\"}"})),
            stop(2),
            start(3, json!({"type": "text", "text": ""})),
            delta(3, json!({"type": "text_delta", "text": "Searching."})),
            stop(3),
        ];
        ev.extend(end("tool_use"));
        sse(&ev)
    }

    #[test]
    fn a_full_stream_split_at_odd_boundaries() {
        let pieces: Vec<Vec<u8>> = full_stream().as_bytes().chunks(7).map(|c| c.to_vec()).collect();
        let server = FakeServer::start(vec![Reply::stream(200, pieces)]);
        let mut p = Anthropic::new(&format!("{}/v1/", server.base), "claude-opus-5-5", "  sk-ant-test  ");
        p.effort = "high".into();
        let (r, shown) = run(&p, &user("find the invoice"), &[tool()], &Cancel::new());
        let r = r.unwrap();
        assert_eq!(shown, "Let me lööк.\n\nSearching.");
        assert_eq!(r.turn.text, "Let me lööк.\n\nSearching.");
        assert_eq!(r.stop_reason.as_deref(), Some("tool_use"));
        assert_eq!(r.turn.tool_calls, vec![ToolCall { id: "toolu_01".into(), name: "search".into(), args: json!({"q": "invoice"}), invalid: false }]);
        assert_eq!(
            r.turn.raw,
            Raw::Anthropic {
                content: vec![
                    json!({"type": "thinking", "thinking": "The user wants the invoice.", "signature": "EqQBCkYIAhgCKkA="}),
                    json!({"type": "text", "text": "Let me lööк."}),
                    json!({"type": "tool_use", "id": "toolu_01", "name": "search", "input": {"q": "invoice"}}),
                    json!({"type": "text", "text": "Searching."}),
                ]
            }
        );

        let req = &server.requests()[0];
        assert_eq!(req.method, "POST");
        assert_eq!(req.path, "/v1/messages");
        assert_eq!(req.header("x-api-key").as_deref(), Some("sk-ant-test"));
        assert_eq!(req.header("anthropic-version").as_deref(), Some("2026-10-21"));
        assert_eq!(req.header("content-type").as_deref(), Some("application/json"));
        assert_eq!(req.header("anthropic-dangerous-direct-browser-access"), None);
        assert_eq!(req.header("authorization"), None);
        let body: Value = serde_json::from_str(&req.body).unwrap();
        assert_eq!(
            body,
            json!({
                "model": "claude-opus-5-5", "max_tokens": 16000, "stream": true, "system": "sys",
                "messages": [{"role": "user", "content": "find the invoice"}],
                "output_config": {"effort": "high"},
                "tools": [{"name": "search", "description": "Search mail", "input_schema": {"type": "object", "properties": {"q": {"type": "string"}}}}]
            })
        );
        let keys: Vec<&String> = body.as_object().unwrap().keys().collect();
        assert_eq!(keys, ["model", "max_tokens", "stream", "system", "messages", "output_config", "tools"]);
    }

    #[test]
    fn same_turn_at_every_split_of_the_stream() {
        let whole = full_stream();
        let want = {
            let server = FakeServer::start(vec![Reply::stream(200, vec![whole.clone().into_bytes()])]);
            run(&Anthropic::new(&server.base, "m", "k"), &user("x"), &[], &Cancel::new()).0.unwrap()
        };
        // a few odd piece sizes rather than every split: each run is a real request
        for size in [1usize, 2, 3, 13, 64] {
            let pieces: Vec<Vec<u8>> = whole.as_bytes().chunks(size).map(|c| c.to_vec()).collect();
            let server = FakeServer::start(vec![Reply::stream(200, pieces).with_gap(0)]);
            let got = run(&Anthropic::new(&server.base, "m", "k"), &user("x"), &[], &Cancel::new()).0.unwrap();
            assert_eq!(got, want, "pieces of {size}");
        }
    }

    #[test]
    fn no_effort_and_no_tools_leave_their_fields_out() {
        let server = FakeServer::start(vec![ok("hi"), ok("hi")]);
        let mut p = Anthropic::new(&server.base, "claude-haiku-4-5", "k");
        p.effort = "high".into();
        assert_eq!(run(&p, &user("x"), &[], &Cancel::new()).0.unwrap().turn.text, "hi");
        p.effort = String::new();
        p.model = "claude-opus-5-5".into();
        run(&p, &user("x"), &[], &Cancel::new()).0.unwrap();
        for req in server.requests() {
            let body: Value = serde_json::from_str(&req.body).unwrap();
            assert!(body.get("output_config").is_none() && body.get("tools").is_none(), "{body}");
        }
    }

    #[test]
    fn bad_tool_json_marks_the_call_invalid_and_stays_out_of_raw() {
        let mut ev = vec![
            start(0, json!({"type": "tool_use", "id": "toolu_a", "name": "search", "input": {}})),
            delta(0, json!({"type": "input_json_delta", "partial_json": "{\"q\": "})),
            stop(0),
            start(1, json!({"type": "tool_use", "id": "toolu_b", "name": "list_folders", "input": {}})),
            stop(1),
        ];
        ev.extend(end("tool_use"));
        let server = FakeServer::start(vec![Reply::stream(200, vec![sse(&ev).into_bytes()])]);
        let r = run(&Anthropic::new(&server.base, "m", "k"), &user("x"), &[tool()], &Cancel::new()).0.unwrap();
        assert_eq!(r.turn.text, "");
        assert!(r.turn.tool_calls[0].invalid);
        assert_eq!(r.turn.tool_calls[0].args, json!({}));
        assert!(!r.turn.tool_calls[1].invalid, "no input at all is {{}}");
        assert_eq!(r.turn.tool_calls[1].args, json!({}));
        let Raw::Anthropic { content } = &r.turn.raw else { panic!() };
        assert_eq!(content[0], json!({"type": "tool_use", "id": "toolu_a", "name": "search", "input": {}}));
    }

    #[test]
    fn junk_unknown_events_and_deltas_for_missing_blocks_are_ignored() {
        let stream = format!(
            "data: not json\n\n{}{}",
            sse(&[delta(5, json!({"type": "text_delta", "text": "lost"})), json!({"type": "something_new"}), stop(9)]),
            text_answer("kept")
        );
        let server = FakeServer::start(vec![Reply::stream(200, vec![stream.into_bytes()])]);
        let (r, shown) = run(&Anthropic::new(&server.base, "m", "k"), &user("x"), &[], &Cancel::new());
        assert_eq!(r.unwrap().turn.text, "kept");
        assert_eq!(shown, "kept");
    }

    fn anthropic_turn(content: Vec<Value>) -> Entry {
        Entry::Assistant(AssistantTurn { text: String::new(), tool_calls: vec![], raw: Raw::Anthropic { content } })
    }

    #[test]
    fn thinking_blocks_go_back_unchanged_or_stripped() {
        let thinking = json!({"type": "thinking", "thinking": "plan", "signature": "sig=="});
        let redacted = json!({"type": "redacted_thinking", "data": "xyz"});
        let call = json!({"type": "tool_use", "id": "toolu_1", "name": "search", "input": {"q": "a"}});
        let history = vec![
            Entry::User { text: "q".into() },
            anthropic_turn(vec![thinking.clone(), redacted.clone(), call.clone()]),
            Entry::Tool { results: vec![ToolResult { id: "toolu_1".into(), name: "search".into(), content: "r".into(), is_error: false }] },
            anthropic_turn(vec![thinking.clone()]),
        ];
        let kept = to_messages(&history, false);
        assert_eq!(kept[1]["content"], json!([thinking, redacted, call]));
        assert_eq!(kept[3]["content"], json!([thinking]));
        let stripped = to_messages(&history, true);
        assert_eq!(stripped[1]["content"], json!([call]));
        assert_eq!(stripped[3]["content"], json!([{"type": "text", "text": "(no answer)"}]), "nothing left");
    }

    #[test]
    fn user_turns_merge_and_other_providers_turns_become_blocks() {
        let history = vec![
            Entry::User { text: "first".into() },
            Entry::User { text: "second".into() },
            Entry::Assistant(AssistantTurn {
                text: "I'll look.".into(),
                tool_calls: vec![
                    ToolCall { id: "call_1".into(), name: "search".into(), args: json!({"q": "x"}), invalid: false },
                    ToolCall { id: "call_2".into(), name: "list".into(), args: Value::Null, invalid: false },
                ],
                raw: Raw::OpenAi,
            }),
            Entry::Tool {
                results: vec![
                    ToolResult { id: "call_1".into(), name: "search".into(), content: "found".into(), is_error: false },
                    ToolResult { id: "call_2".into(), name: "list".into(), content: "Failed: no".into(), is_error: true },
                ],
            },
            Entry::User { text: "and now?".into() },
            Entry::Assistant(AssistantTurn { text: String::new(), tool_calls: vec![], raw: Raw::Local }),
        ];
        assert_eq!(
            Value::Array(to_messages(&history, false)),
            json!([
                {"role": "user", "content": [{"type": "text", "text": "first"}, {"type": "text", "text": "second"}]},
                {"role": "assistant", "content": [
                    {"type": "text", "text": "I'll look."},
                    {"type": "tool_use", "id": "call_1", "name": "search", "input": {"q": "x"}},
                    {"type": "tool_use", "id": "call_2", "name": "list", "input": {}}
                ]},
                {"role": "user", "content": [
                    {"type": "tool_result", "tool_use_id": "call_1", "content": "found"},
                    {"type": "tool_result", "tool_use_id": "call_2", "content": "Failed: no", "is_error": true},
                    {"type": "text", "text": "and now?"}
                ]},
                {"role": "assistant", "content": [{"type": "text", "text": "(no answer)"}]}
            ])
        );
        // a single user turn stays a string
        assert_eq!(to_messages(&user("hi"), false), vec![json!({"role": "user", "content": "hi"})]);
    }

    fn with_thinking() -> Vec<Entry> {
        vec![
            Entry::User { text: "q".into() },
            anthropic_turn(vec![json!({"type": "thinking", "thinking": "t", "signature": "s"}), json!({"type": "text", "text": "a"})]),
            Entry::User { text: "q2".into() },
        ]
    }

    fn bodies(server: &FakeServer) -> Vec<Value> {
        server.requests().iter().map(|r| serde_json::from_str(&r.body).unwrap()).collect()
    }

    #[test]
    fn a_400_about_thinking_is_retried_once_without_it() {
        let bad = || Reply::json(400, r#"{"type":"error","error":{"type":"invalid_request_error","message":"messages.1.content.0: Invalid `signature` in `thinking` block"}}"#);
        let server = FakeServer::start(vec![bad(), ok("fine")]);
        let r = run(&Anthropic::new(&server.base, "claude-opus-5-5", "k"), &with_thinking(), &[], &Cancel::new()).0.unwrap();
        assert_eq!(r.turn.text, "fine");
        let b = bodies(&server);
        assert_eq!(b[0]["messages"][1]["content"][0]["type"], "thinking");
        assert_eq!(b[1]["messages"][1]["content"], json!([{"type": "text", "text": "a"}]));
        // the same error again is not retried a second time
        let server = FakeServer::start(vec![bad(), bad(), ok("never")]);
        let e = run(&Anthropic::new(&server.base, "m", "k"), &with_thinking(), &[], &Cancel::new()).0.unwrap_err();
        assert_eq!(e.status, Some(400));
        assert!(e.message.starts_with("400: messages.1.content.0"), "{}", e.message);
        assert_eq!(server.requests().len(), 2);
    }

    #[test]
    fn a_400_about_effort_is_retried_once_without_it() {
        let bad = || Reply::json(400, r#"{"type":"error","error":{"type":"invalid_request_error","message":"output_config.effort: Input should be 'low', 'medium' or 'high'"}}"#);
        let server = FakeServer::start(vec![bad(), ok("fine")]);
        let mut p = Anthropic::new(&server.base, "claude-sonnet-5-5", "k");
        p.effort = "max".into();
        assert_eq!(run(&p, &user("x"), &[], &Cancel::new()).0.unwrap().turn.text, "fine");
        let b = bodies(&server);
        assert_eq!(b[0]["output_config"], json!({"effort": "max"}));
        assert!(b[1].get("output_config").is_none());
        // no effort set: nothing to drop, no retry
        let server = FakeServer::start(vec![bad(), ok("never")]);
        assert_eq!(run(&Anthropic::new(&server.base, "m", "k"), &user("x"), &[], &Cancel::new()).0.unwrap_err().status, Some(400));
        assert_eq!(server.requests().len(), 1);
    }

    #[test]
    fn both_retries_in_one_call() {
        let thinking = Reply::json(400, r#"{"error":{"message":"thinking blocks cannot be modified"}}"#);
        let effort = Reply::json(400, r#"{"error":{"message":"unknown effort level"}}"#);
        let server = FakeServer::start(vec![thinking, effort, ok("fine")]);
        let mut p = Anthropic::new(&server.base, "claude-opus-5-5", "k");
        p.effort = "low".into();
        assert_eq!(run(&p, &with_thinking(), &[], &Cancel::new()).0.unwrap().turn.text, "fine");
        let b = bodies(&server);
        assert_eq!(b.len(), 3);
        assert!(b[0].get("output_config").is_some() && b[0]["messages"][1]["content"].as_array().unwrap().len() == 2);
        assert!(b[1].get("output_config").is_some() && b[1]["messages"][1]["content"].as_array().unwrap().len() == 1);
        assert!(b[2].get("output_config").is_none() && b[2]["messages"][1]["content"].as_array().unwrap().len() == 1);
    }

    #[test]
    fn other_errors_are_not_retried() {
        for (status, body, want) in [
            (429, r#"{"type":"error","error":{"type":"rate_limit_error","message":"Number of request tokens has exceeded your rate limit"}}"#, "429: Number of request tokens has exceeded your rate limit"),
            (529, r#"{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}"#, "529: Overloaded"),
            (400, r#"{"type":"error","error":{"type":"invalid_request_error","message":"prompt is too long"}}"#, "400: prompt is too long"),
        ] {
            let server = FakeServer::start(vec![Reply::json(status, body), ok("never")]);
            let mut p = Anthropic::new(&server.base, "claude-opus-5-5", "k");
            p.effort = "high".into();
            let e = run(&p, &with_thinking(), &[], &Cancel::new()).0.unwrap_err();
            assert_eq!((e.status, e.message.as_str()), (Some(status), want));
            assert_eq!(server.requests().len(), 1);
        }
    }

    #[test]
    fn a_refusal_is_an_error() {
        let mut ev = vec![start(0, json!({"type": "text", "text": ""})), delta(0, json!({"type": "text_delta", "text": "I"})), stop(0)];
        ev.extend(end("refusal"));
        let server = FakeServer::start(vec![Reply::stream(200, vec![sse(&ev).into_bytes()])]);
        let e = run(&Anthropic::new(&server.base, "m", "k"), &user("x"), &[], &Cancel::new()).0.unwrap_err();
        assert_eq!(e.message, "The model declined to answer this request.");
        assert_eq!(e.status, None);
        // a later message_delta without a stop reason keeps the one before
        let mut ev = vec![start(0, json!({"type": "text", "text": ""})), stop(0)];
        ev.extend(end("refusal"));
        ev.push(json!({"type": "message_delta", "delta": {"stop_reason": null}}));
        let server = FakeServer::start(vec![Reply::stream(200, vec![sse(&ev).into_bytes()])]);
        assert!(run(&Anthropic::new(&server.base, "m", "k"), &user("x"), &[], &Cancel::new()).0.is_err());
    }

    #[test]
    fn error_events_in_the_stream() {
        let ev = [start(0, json!({"type": "text", "text": ""})), json!({"type": "error", "error": {"type": "overloaded_error", "message": "Overloaded"}})];
        let server = FakeServer::start(vec![Reply::stream(200, vec![sse(&ev).into_bytes()])]);
        assert_eq!(run(&Anthropic::new(&server.base, "m", "k"), &user("x"), &[], &Cancel::new()).0.unwrap_err().message, "Overloaded");
        let server = FakeServer::start(vec![Reply::stream(200, vec![b"data: {\"type\":\"error\"}\n\n".to_vec()])]);
        assert_eq!(run(&Anthropic::new(&server.base, "m", "k"), &user("x"), &[], &Cancel::new()).0.unwrap_err().message, "The service reported an error.");
    }

    #[test]
    fn http_errors_and_their_wording() {
        let server = FakeServer::start(vec![Reply::json(401, r#"{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}"#)]);
        let p = Anthropic::new(&server.base, "claude-haiku-5-5", "sk-wrong");
        let e = run(&p, &user("x"), &[], &Cancel::new()).0.unwrap_err();
        assert_eq!((e.status, e.message.as_str()), (Some(401), "401: invalid x-api-key"));
        assert_eq!(
            crate::assistant::friendly_error(&e, "Anthropic", &p.model, &p.base_url),
            "The Anthropic service refused the key (401: invalid x-api-key). Check the API key in the settings."
        );
        let server = FakeServer::start(vec![Reply::json(404, r#"{"type":"error","error":{"type":"not_found_error","message":"model: claude-nope"}}"#)]);
        let e = run(&Anthropic::new(&server.base, "claude-nope", "k"), &user("x"), &[], &Cancel::new()).0.unwrap_err();
        assert_eq!(e.message, "404: model: claude-nope");
    }

    #[test]
    fn keys_that_cannot_be_sent_and_debug_hides_the_key() {
        let server = FakeServer::start(vec![ok("never")]);
        for key in ["sk-a\nnt", "sk-\u{e4}nt", "sk ant"] {
            let e = run(&Anthropic::new(&server.base, "m", key), &user("x"), &[], &Cancel::new()).0.unwrap_err();
            assert_eq!(e.message, crate::assistant::http::KEY_CHARS, "{key:?}");
        }
        assert!(server.requests().is_empty());
        let shown = format!("{:?}", Anthropic::new("http://h/v1", "m", "sk-ant-secret"));
        assert!(!shown.contains("sk-ant-secret") && shown.contains("<redacted>"), "{shown}");
    }

    #[test]
    fn stop_mid_stream_and_the_idle_limit() {
        let first = sse(&[start(0, json!({"type": "text", "text": ""})), delta(0, json!({"type": "text_delta", "text": "Hel"}))]);
        let server = FakeServer::start(vec![Reply::stream(200, vec![first.clone().into_bytes()]).then_pause(3000)]);
        let cancel = Cancel::new();
        let c2 = cancel.clone();
        std::thread::spawn(move || {
            std::thread::sleep(Duration::from_millis(200));
            c2.cancel();
        });
        let t = Instant::now();
        let (r, shown) = run(&Anthropic::new(&server.base, "m", "k"), &user("x"), &[], &cancel);
        let e = r.unwrap_err();
        assert!(e.is_aborted());
        assert_eq!(shown, "Hel");
        assert!(t.elapsed() < Duration::from_millis(1500), "{:?}", t.elapsed());

        let server = FakeServer::start(vec![Reply::stream(200, vec![first.into_bytes()]).then_pause(2000)]);
        let mut p = Anthropic::new(&server.base, "m", "k");
        p.idle = Duration::from_millis(300);
        let e = run(&p, &user("x"), &[], &Cancel::new()).0.unwrap_err();
        assert_eq!(e.message, "The AI service stopped sending anything for 0.005 minutes. Try again.");
        assert_eq!(e.kind, ErrorKind::Service);
    }

    #[test]
    fn a_stream_past_the_limit_is_refused() {
        let mut asm = Assembler::new(300);
        let mut sink = |_: &str| {};
        let s = start(0, json!({"type": "thinking", "thinking": ""})).to_string();
        asm.event(&s, &mut sink).unwrap();
        let d = delta(0, json!({"type": "thinking_delta", "thinking": "x".repeat(60)})).to_string();
        asm.event(&d, &mut sink).unwrap();
        assert_eq!(asm.event(&d, &mut sink).unwrap_err().message, "The AI service sent an event that is too large.");
    }

    #[test]
    fn effort_support() {
        for m in ["claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5", "claude-fable-5-1", "claude-mythos-5", "Claude-Opus-4-6", "claude-opus-4-9-20260101", "claude-sonnet-4-6", "opus", "default", "us.anthropic.claude-sonnet-5-5-v1:0"] {
            assert!(supports_effort(m), "{m}");
        }
        for m in ["claude-haiku-4-5", "claude-opus-4-5", "claude-sonnet-4-5", "claude-opus-4", "claude-3-5-sonnet", "gpt-4o", "", "opus-x", "sonnet "] {
            assert!(!supports_effort(m), "{m}");
        }
    }

    #[test]
    fn model_names() {
        assert_eq!(model_name("claude-opus-5-5", &[]), "Opus 5.5");
        assert_eq!(model_name("claude-fable-5-1", &[]), "Fable 5.1");
        let known = [ModelInfo { id: "claude-opus-5-5".into(), name: "Claude Opus 5.5".into() }];
        assert_eq!(model_name("claude-opus-5-5", &known), "Claude Opus 5.5");
        // a known entry without a name falls through to the parts, not to the built-in list
        let unnamed = [ModelInfo { id: "claude-opus-5-5".into(), name: String::new() }];
        assert_eq!(model_name("claude-opus-5-5", &unnamed), "Opus 5.5");
        assert_eq!(model_name("claude-sonnet-6", &[]), "Sonnet 6");
        assert_eq!(model_name("claude-opus-4-5-20251101", &[]), "Opus 4.5");
        // the extension's pattern takes a bare date as the minor version; kept as it is
        assert_eq!(model_name("claude-sonnet-4-20250514", &[]), "Sonnet 4.20250514");
        assert_eq!(model_name("claude-3-5-sonnet", &[]), "claude-3-5-sonnet");
        assert_eq!(model_name("gpt-4o", &[]), "gpt-4o");
        assert_eq!(model_name("", &[]), "");
    }

    fn page(ids: &[(&str, &str)], has_more: bool) -> Reply {
        let data: Vec<Value> = ids.iter().map(|(id, name)| if name.is_empty() { json!({"id": id, "type": "model"}) } else { json!({"id": id, "display_name": name, "type": "model"}) }).collect();
        let last = ids.last().map(|(id, _)| json!(id)).unwrap_or(Value::Null);
        Reply::json(200, &json!({"data": data, "has_more": has_more, "first_id": ids.first().map(|(id, _)| *id), "last_id": last}).to_string())
    }

    #[test]
    fn the_models_api_list_and_its_pages() {
        let server = FakeServer::start(vec![
            page(&[("claude-opus-5-5", "Claude Opus 5.5"), ("claude-sonnet-4-6", "")], true),
            page(&[("model/x y", "Odd")], true),
            page(&[("claude-haiku-5-5", "Claude Haiku 5.5")], false),
            page(&[("never", "")], false),
        ]);
        let models = fetch_models(&format!("{}/v1/", server.base), " sk-ant-test ", &Cancel::new()).unwrap();
        assert_eq!(
            models,
            vec![
                ModelInfo { id: "claude-opus-5-5".into(), name: "Claude Opus 5.5".into() },
                ModelInfo { id: "claude-sonnet-4-6".into(), name: "Sonnet 4.6".into() },
                ModelInfo { id: "model/x y".into(), name: "Odd".into() },
                ModelInfo { id: "claude-haiku-5-5".into(), name: "Claude Haiku 5.5".into() },
            ]
        );
        let reqs = server.requests();
        assert_eq!(reqs.len(), 3);
        assert_eq!(reqs[0].method, "GET");
        assert_eq!(reqs[0].path, "/v1/models?limit=100");
        assert_eq!(reqs[1].path, "/v1/models?limit=100&after_id=claude-sonnet-4-6");
        assert_eq!(reqs[2].path, "/v1/models?limit=100&after_id=model%2Fx%20y");
        assert_eq!(reqs[0].header("x-api-key").as_deref(), Some("sk-ant-test"));
        assert_eq!(reqs[0].header("anthropic-version").as_deref(), Some("2026-10-21"));
    }

    #[test]
    fn the_models_api_reads_at_most_five_pages() {
        let replies = (0..6).map(|i| page(&[(["a", "b", "c", "d", "e", "f"][i], "")], true)).collect();
        let server = FakeServer::start(replies);
        let models = fetch_models(&server.base, "k", &Cancel::new()).unwrap();
        assert_eq!(models.iter().map(|m| m.id.as_str()).collect::<Vec<_>>(), ["a", "b", "c", "d", "e"]);
        assert_eq!(server.requests().len(), 5);
        // has_more without a last_id ends the list too
        let server = FakeServer::start(vec![Reply::json(200, r#"{"data":[{"id":"claude-x-1"}],"has_more":true}"#), page(&[("never", "")], false)]);
        assert_eq!(fetch_models(&server.base, "k", &Cancel::new()).unwrap().len(), 1);
        assert_eq!(server.requests().len(), 1);
    }

    #[test]
    fn the_models_api_errors() {
        let long = format!("{{\"error\":{{\"message\":\"{}\"}}}}", "y".repeat(300));
        let server = FakeServer::start(vec![Reply::json(401, &long)]);
        let e = fetch_models(&server.base, "k", &Cancel::new()).unwrap_err();
        assert_eq!(e.message, format!("The Models API answered 401: {}", &long[..200]));
        let server = FakeServer::start(vec![Reply::json(200, "<html>")]);
        assert_eq!(fetch_models(&server.base, "k", &Cancel::new()).unwrap_err().message, "The Models API sent an answer that is not JSON.");
        let cancel = Cancel::new();
        cancel.cancel();
        let server = FakeServer::start(vec![]);
        assert!(fetch_models(&server.base, "k", &cancel).unwrap_err().is_aborted());
        assert!(server.requests().is_empty());
    }

    #[test]
    fn encode_uri_component_matches_javascript() {
        assert_eq!(encode_uri_component("a-Z_0.!~*'()"), "a-Z_0.!~*'()");
        assert_eq!(encode_uri_component("a b/c?d&e=f+g#ä"), "a%20b%2Fc%3Fd%26e%3Df%2Bg%23%C3%A4");
    }
}
