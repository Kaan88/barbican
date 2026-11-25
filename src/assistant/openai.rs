//! The OpenAI-compatible client (spec 3.3): `POST <base>/chat/completions`, streaming, with tool
//! calls. Also serves Ollama, LM Studio and vLLM, which are just base URLs with no key; the
//! `<think>` filter and the no-tools fallback exist for them.
//!
//! The body has `model`, `stream: true`, `messages` and `tools` (only when there are any and the
//! model is not known to reject them). Nothing else: no temperature, max_tokens, tool_choice,
//! stream_options or parallel_tool_calls.
use super::error::{js_string, truthy, ProviderError};
pub use super::history::ChatOutcome;
use super::history::{AssistantTurn, Entry, Raw, ToolCall, ToolDef};
use super::http;
use super::sse::{read_sse, too_large, Cancel, IDLE, MAX_EVENT};
use serde_json::{json, Value};
use std::collections::{BTreeMap, HashSet};
use std::sync::{Mutex, PoisonError};
use std::time::Duration;

/// `noToolModels`: `baseUrl|model` pairs that answered a request with tools with a 400 about
/// tools, this run. Their later requests go without tools.
static NO_TOOL_MODELS: Mutex<Option<HashSet<String>>> = Mutex::new(None);

fn no_tools(key: &str) -> bool {
    NO_TOOL_MODELS.lock().unwrap_or_else(PoisonError::into_inner).as_ref().is_some_and(|s| s.contains(key))
}

fn mark_no_tools(key: &str) {
    NO_TOOL_MODELS.lock().unwrap_or_else(PoisonError::into_inner).get_or_insert_with(HashSet::new).insert(key.to_string());
}

/// `trimUrl`: what the user typed, trimmed, without trailing slashes.
pub fn trim_url(url: &str) -> String {
    url.trim().trim_end_matches('/').to_string()
}

/// An OpenAI-compatible service as the settings describe it. Its `Debug` hides the key.
#[derive(Clone)]
pub struct OpenAi {
    /// up to and including `/v1` (no default)
    pub base_url: String,
    pub model: String,
    /// optional: `authorization: Bearer <key>` is sent only when it is not blank
    pub api_key: String,
    /// the stream's idle limit, [`IDLE`] (tests make it short)
    pub idle: Duration,
}

impl std::fmt::Debug for OpenAi {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("OpenAi")
            .field("base_url", &self.base_url)
            .field("model", &self.model)
            .field("api_key", &if self.api_key.trim().is_empty() { "" } else { "<redacted>" })
            .field("idle", &self.idle)
            .finish()
    }
}

/// `toOpenAIMessages(system, history)`.
pub fn to_messages(system: &str, history: &[Entry]) -> Vec<Value> {
    let mut out = vec![json!({"role": "system", "content": system})];
    for h in history {
        match h {
            Entry::User { text } => out.push(json!({"role": "user", "content": text})),
            Entry::Assistant(t) => {
                let mut msg = json!({"role": "assistant", "content": t.text});
                if !t.tool_calls.is_empty() {
                    msg["tool_calls"] = t
                        .tool_calls
                        .iter()
                        .map(|c| {
                            let args = if truthy(&c.args) { c.args.clone() } else { json!({}) };
                            json!({"id": c.id, "type": "function", "function": {"name": c.name, "arguments": args.to_string()}})
                        })
                        .collect();
                }
                out.push(msg);
            }
            Entry::Tool { results } => {
                for r in results {
                    out.push(json!({"role": "tool", "tool_call_id": r.id, "content": r.content}));
                }
            }
        }
    }
    out
}

/// The request body; `tools` is `None` when they are left out.
pub fn request_body(model: &str, system: &str, history: &[Entry], tools: Option<&[ToolDef]>) -> Value {
    let mut body = json!({"model": model, "stream": true, "messages": to_messages(system, history)});
    if let Some(tools) = tools {
        body["tools"] = tools
            .iter()
            .map(|t| json!({"type": "function", "function": {"name": t.name, "description": t.description, "parameters": t.parameters}}))
            .collect();
    }
    body
}

/// `makeThinkFilter`: hides `<think>…</think>` output of reasoning models served through Ollama.
/// It holds back a possible partial `<think>` at the end of a chunk, drops everything inside a
/// think block, and on [`flush`](ThinkFilter::flush) drops an unclosed one.
#[derive(Default)]
pub struct ThinkFilter {
    in_think: bool,
    pending: String,
}

const OPEN: &str = "<think>";
const CLOSE: &str = "</think>";

/// The last `n` characters of `s`.
fn tail(s: &str, n: usize) -> String {
    let start = s.char_indices().rev().nth(n.saturating_sub(1)).map(|(i, _)| i).unwrap_or(0);
    if n == 0 { String::new() } else { s[start..].to_string() }
}

impl ThinkFilter {
    /// Adds a chunk; returns the part that may be shown now.
    pub fn push(&mut self, chunk: &str) -> String {
        self.pending.push_str(chunk);
        let mut visible = String::new();
        loop {
            if self.in_think {
                match self.pending.find(CLOSE) {
                    None => {
                        self.pending = tail(&self.pending, 8);
                        break;
                    }
                    Some(end) => {
                        self.pending.drain(..end + CLOSE.len());
                        self.in_think = false;
                    }
                }
            } else {
                match self.pending.find(OPEN) {
                    None => {
                        // hold back a possible partial "<think>" at the end
                        let len = self.pending.len();
                        let hold = (1..=len.min(6))
                            .rev()
                            .find(|&k| self.pending.is_char_boundary(len - k) && OPEN.starts_with(&self.pending[len - k..]))
                            .unwrap_or(0);
                        visible.push_str(&self.pending[..len - hold]);
                        self.pending.drain(..len - hold);
                        break;
                    }
                    Some(start) => {
                        visible.push_str(&self.pending[..start]);
                        self.pending.drain(..start + OPEN.len());
                        self.in_think = true;
                    }
                }
            }
        }
        visible
    }

    /// The stream ended: what was held back, unless it is inside a think block.
    pub fn flush(&mut self) -> String {
        let rest = std::mem::take(&mut self.pending);
        if self.in_think { String::new() } else { rest }
    }
}

/// A tool call while its deltas arrive.
#[derive(Default)]
struct Partial {
    id: String,
    name: String,
    args: String,
}

/// Assembles a turn from the stream's events (`openaiRequest` after the response is OK).
struct Assembler {
    text: String,
    calls: BTreeMap<u64, Partial>,
    stop_reason: Option<String>,
    filter: ThinkFilter,
    /// the most the text, or one call's name or arguments, may hold ([`MAX_EVENT`])
    limit: usize,
}

impl Assembler {
    fn new(limit: usize) -> Assembler {
        Assembler { text: String::new(), calls: BTreeMap::new(), stop_reason: None, filter: ThinkFilter::default(), limit }
    }

    /// One event's data; `Ok(false)` at `[DONE]`.
    fn event(&mut self, data: &str, on_text: &mut dyn FnMut(&str)) -> Result<bool, ProviderError> {
        if data.trim() == "[DONE]" {
            return Ok(false);
        }
        let Ok(ev) = serde_json::from_str::<Value>(data) else { return Ok(true) };
        let err = &ev["error"];
        if truthy(err) {
            let m = &err["message"];
            return Err(ProviderError::new(if truthy(m) { js_string(m) } else { js_string(err) }));
        }
        let choice = &ev["choices"][0];
        if !truthy(choice) {
            return Ok(true);
        }
        let d = [&choice["delta"], &choice["message"]].into_iter().find(|v| truthy(v)).unwrap_or(&Value::Null);
        if let Some(c) = d["content"].as_str().filter(|c| !c.is_empty()) {
            let shown = self.filter.push(c);
            if !shown.is_empty() {
                on_text(&shown);
            }
            self.text.push_str(&shown);
            if self.text.len() > self.limit {
                return Err(too_large());
            }
        }
        if let Some(tcs) = d["tool_calls"].as_array() {
            for tc in tcs {
                // `tc.index ?? calls.length`: the length of a sparse array is its last index + 1
                let next = self.calls.keys().next_back().map_or(0, |k| k + 1);
                let idx = tc["index"].as_u64().unwrap_or(next);
                let c = self.calls.entry(idx).or_default();
                if let Some(id) = tc["id"].as_str().filter(|s| !s.is_empty()) {
                    c.id = id.to_string();
                }
                if let Some(name) = tc["function"]["name"].as_str() {
                    c.name.push_str(name);
                }
                let a = &tc["function"]["arguments"];
                if truthy(a) {
                    match a {
                        Value::String(s) => c.args.push_str(s),
                        other => c.args.push_str(&other.to_string()),
                    }
                }
                if c.args.len() > self.limit || c.name.len() > self.limit {
                    return Err(too_large());
                }
            }
        }
        if let Some(r) = choice["finish_reason"].as_str().filter(|s| !s.is_empty()) {
            self.stop_reason = Some(r.to_string());
        }
        Ok(true)
    }

    fn finish(mut self, on_text: &mut dyn FnMut(&str)) -> ChatOutcome {
        let rest = self.filter.flush();
        if !rest.is_empty() {
            on_text(&rest);
        }
        self.text.push_str(&rest);
        let tool_calls = self
            .calls
            .into_values()
            .map(|c| {
                let (args, invalid) = if c.args.is_empty() {
                    (json!({}), false)
                } else {
                    match serde_json::from_str::<Value>(&c.args) {
                        Ok(v) => (v, false),
                        Err(_) => (json!({}), true),
                    }
                };
                let id = if c.id.is_empty() { super::new_id() } else { c.id };
                ToolCall { id, name: c.name, args, invalid }
            })
            .collect();
        ChatOutcome {
            turn: AssistantTurn { text: self.text.trim().to_string(), tool_calls, raw: Raw::OpenAi },
            stop_reason: self.stop_reason,
        }
    }
}

impl OpenAi {
    pub fn new(base_url: &str, model: &str, api_key: &str) -> OpenAi {
        OpenAi { base_url: trim_url(base_url), model: model.to_string(), api_key: api_key.to_string(), idle: IDLE }
    }

    fn no_tools_key(&self) -> String {
        format!("{}|{}", self.base_url, self.model)
    }

    /// One model turn (`chat` for an OpenAI-compatible provider). Blocks until the turn is
    /// complete; `on_text` gets each visible piece of the answer as it streams in. Call it from a
    /// worker thread: from inside an async runtime it fails. `on_text` runs on the request's own
    /// runtime, so it must not block on async code or use `reqwest::blocking`; it should hand the
    /// text on (a channel, a queued UI update) and return.
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
        let url = format!("{}/chat/completions", self.base_url);
        http::check_url(&url)?;
        let key = self.api_key.trim();
        if !key.is_empty() {
            http::check_key(key)?;
        }
        let client = http::client(false)?;
        // the fallback without tools happens at most once per call
        let mut retried = false;
        loop {
            let use_tools = !tools.is_empty() && !no_tools(&self.no_tools_key());
            let body = request_body(&self.model, system, history, use_tools.then_some(tools));
            let mut req = client.post(&url).header("content-type", "application/json").body(body.to_string());
            if !key.is_empty() {
                // bearer_auth marks the header as sensitive
                req = req.bearer_auth(key);
            }
            let mut resp = http::send(req, cancel).await?;
            let status = resp.status().as_u16();
            if !resp.status().is_success() {
                let err = http::error_of(resp, cancel).await?;
                if use_tools && !retried && status == 400 && err.message.to_lowercase().contains("tool") {
                    mark_no_tools(&self.no_tools_key());
                    retried = true;
                    continue;
                }
                return Err(err);
            }
            let mut asm = Assembler::new(MAX_EVENT);
            read_sse(&mut resp, cancel, self.idle, |data| asm.event(&data, on_text)).await?;
            return Ok(asm.finish(on_text));
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::assistant::error::ErrorKind;
    use crate::assistant::fake::{FakeServer, Reply};
    use crate::assistant::history::ToolResult;
    use std::time::Instant;

    fn filtered(chunks: &[&str]) -> String {
        let mut f = ThinkFilter::default();
        let mut out: String = chunks.iter().map(|c| f.push(c)).collect();
        out.push_str(&f.flush());
        out
    }

    #[test]
    fn think_filter_at_every_split() {
        let s = "Hi <think>secret\nplan</think>there, <b>bold</b> <think>more</think>end";
        for i in 0..=s.len() {
            for j in i..=s.len() {
                if !s.is_char_boundary(i) || !s.is_char_boundary(j) {
                    continue;
                }
                assert_eq!(filtered(&[&s[..i], &s[i..j], &s[j..]]), "Hi there, <b>bold</b> end", "split at {i} {j}");
            }
        }
    }

    #[test]
    fn think_filter_holds_back_only_a_possible_tag() {
        let mut f = ThinkFilter::default();
        assert_eq!(f.push("a <thi"), "a ");
        assert_eq!(f.push("ng>"), "<thing>");
        assert_eq!(f.push("x <"), "x ");
        assert_eq!(f.flush(), "<", "a held-back partial tag is shown at the end");
        // an unclosed think block is dropped
        assert_eq!(filtered(&["ok <think>never ", "closed"]), "ok ");
        assert_eq!(filtered(&["é<think>ü", "ö</thi", "nk>ä"]), "éä");
    }

    #[test]
    fn messages_from_the_neutral_history() {
        let history = vec![
            Entry::User { text: "hi".into() },
            Entry::Assistant(AssistantTurn {
                text: "".into(),
                tool_calls: vec![
                    ToolCall { id: "c1".into(), name: "search".into(), args: json!({"q": "x"}), invalid: false },
                    ToolCall { id: "c2".into(), name: "list".into(), args: Value::Null, invalid: false },
                ],
                raw: Raw::Anthropic { content: vec![json!({"type": "thinking", "thinking": "t"})] },
            }),
            Entry::Tool {
                results: vec![
                    ToolResult { id: "c1".into(), name: "search".into(), content: "found".into(), is_error: false },
                    ToolResult { id: "c2".into(), name: "list".into(), content: "bad".into(), is_error: true },
                ],
            },
            Entry::Assistant(AssistantTurn { text: "done".into(), tool_calls: vec![], raw: Raw::OpenAi }),
        ];
        let m = to_messages("sys", &history);
        assert_eq!(
            Value::Array(m),
            json!([
                {"role": "system", "content": "sys"},
                {"role": "user", "content": "hi"},
                {"role": "assistant", "content": "", "tool_calls": [
                    {"id": "c1", "type": "function", "function": {"name": "search", "arguments": "{\"q\":\"x\"}"}},
                    {"id": "c2", "type": "function", "function": {"name": "list", "arguments": "{}"}}
                ]},
                {"role": "tool", "tool_call_id": "c1", "content": "found"},
                {"role": "tool", "tool_call_id": "c2", "content": "bad"},
                {"role": "assistant", "content": "done"}
            ])
        );
    }

    fn tool() -> ToolDef {
        ToolDef { name: "search".into(), description: "Search mail".into(), parameters: json!({"type": "object", "properties": {}}) }
    }

    fn user(text: &str) -> Vec<Entry> {
        vec![Entry::User { text: text.into() }]
    }

    /// Runs one chat against `server`, collecting the streamed text.
    fn run(p: &OpenAi, tools: &[ToolDef], cancel: &Cancel) -> (Result<ChatOutcome, ProviderError>, String) {
        let mut shown = String::new();
        let r = p.chat("sys", &user("hello"), tools, cancel, &mut |t| shown.push_str(t));
        (r, shown)
    }

    fn sse(events: &[Value]) -> String {
        let mut s: String = events.iter().map(|e| format!("data: {e}\r\n\r\n")).collect();
        s.push_str("data: [DONE]\r\n\r\n");
        s
    }

    #[test]
    fn openai_stream_split_at_odd_boundaries() {
        let stream = sse(&[
            json!({"choices": [{"index": 0, "delta": {"role": "assistant", "content": ""}}]}),
            json!({"choices": [{"index": 0, "delta": {"content": "Hello, "}}]}),
            json!({"choices": [{"index": 0, "delta": {"content": "wörld ✓ "}}]}),
            json!({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
        ]);
        // 7-byte pieces cut through the CRLFs and the UTF-8 sequences
        let pieces: Vec<Vec<u8>> = stream.as_bytes().chunks(7).map(|c| c.to_vec()).collect();
        let server = FakeServer::start(vec![Reply::stream(200, pieces)]);
        let p = OpenAi::new(&format!("{}/v1/", server.base), "gpt-4o-mini", "");
        let (r, shown) = run(&p, &[], &Cancel::new());
        let r = r.unwrap();
        assert_eq!(r.turn.text, "Hello, wörld ✓");
        assert_eq!(shown, "Hello, wörld ✓ ");
        assert_eq!(r.turn.raw, Raw::OpenAi);
        assert!(r.turn.tool_calls.is_empty());
        assert_eq!(r.stop_reason.as_deref(), Some("stop"));

        let req = &server.requests()[0];
        assert_eq!(req.method, "POST");
        assert_eq!(req.path, "/v1/chat/completions", "trailing slash trimmed");
        assert_eq!(req.header("content-type").as_deref(), Some("application/json"));
        assert_eq!(req.header("authorization"), None, "no key, no authorization header");
        let body: Value = serde_json::from_str(&req.body).unwrap();
        assert_eq!(
            body,
            json!({"model": "gpt-4o-mini", "stream": true, "messages": [
                {"role": "system", "content": "sys"}, {"role": "user", "content": "hello"}]})
        );
        let keys: Vec<&String> = body.as_object().unwrap().keys().collect();
        assert_eq!(keys, ["model", "stream", "messages"]);
    }

    #[test]
    fn key_is_trimmed_into_a_bearer_header_and_tools_are_sent() {
        let server = FakeServer::start(vec![Reply::stream(200, vec![sse(&[]).into_bytes()])]);
        let p = OpenAi::new(&server.base, "m", "  sk-test  ");
        let (r, _) = run(&p, &[tool()], &Cancel::new());
        assert_eq!(r.unwrap().turn.text, "");
        let req = &server.requests()[0];
        assert_eq!(req.header("authorization").as_deref(), Some("Bearer sk-test"));
        let body: Value = serde_json::from_str(&req.body).unwrap();
        assert_eq!(
            body["tools"],
            json!([{"type": "function", "function": {"name": "search", "description": "Search mail", "parameters": {"type": "object", "properties": {}}}}])
        );
        // a blank key is no key
        let server = FakeServer::start(vec![Reply::stream(200, vec![sse(&[]).into_bytes()])]);
        let (r, _) = run(&OpenAi::new(&server.base, "m", "   "), &[], &Cancel::new());
        r.unwrap();
        assert_eq!(server.requests()[0].header("authorization"), None);
    }

    #[test]
    fn ollama_think_blocks_across_chunks_and_tool_call_deltas() {
        let c = |s: &str| json!({"choices": [{"index": 0, "delta": {"content": s}}]});
        let stream = sse(&[
            c("<th"),
            c("ink>Let me think"),
            c(" about it.</thi"),
            c("nk>\n\nI'll search."),
            json!({"choices": [{"index": 0, "delta": {"tool_calls": [
                {"index": 0, "id": "call_a", "type": "function", "function": {"name": "sea", "arguments": ""}}]}}]}),
            json!({"choices": [{"index": 0, "delta": {"tool_calls": [{"index": 0, "function": {"name": "rch", "arguments": "{\"q\":"}}]}}]}),
            json!({"choices": [{"index": 0, "delta": {"tool_calls": [
                {"index": 0, "function": {"arguments": "\"inv"}},
                {"index": 1, "function": {"name": "list_folders", "arguments": {"all": true}}}]}}]}),
            json!({"choices": [{"index": 0, "delta": {"tool_calls": [{"index": 0, "function": {"arguments": "oice\"}"}}]}}]}),
            // no index: the next slot; no id: a generated one; broken JSON: invalid
            json!({"choices": [{"index": 0, "delta": {"tool_calls": [{"function": {"name": "broken", "arguments": "{\"a\": "}}]}}]}),
            json!({"choices": [{"index": 0, "delta": {}, "finish_reason": "tool_calls"}]}),
        ]);
        let pieces: Vec<Vec<u8>> = stream.as_bytes().chunks(5).map(|c| c.to_vec()).collect();
        let server = FakeServer::start(vec![Reply::stream(200, pieces)]);
        let p = OpenAi::new(&format!("{}/v1", server.base), "qwen3:8b", "");
        let (r, shown) = run(&p, &[tool()], &Cancel::new());
        let r = r.unwrap();
        assert_eq!(shown, "\n\nI'll search.");
        assert_eq!(r.turn.text, "I'll search.");
        assert_eq!(r.stop_reason.as_deref(), Some("tool_calls"));
        let calls = &r.turn.tool_calls;
        assert_eq!(calls.len(), 3);
        assert_eq!(calls[0], ToolCall { id: "call_a".into(), name: "search".into(), args: json!({"q": "invoice"}), invalid: false });
        assert_eq!(calls[1].name, "list_folders");
        assert_eq!(calls[1].args, json!({"all": true}), "object arguments are stringified, then parsed");
        assert!(calls[1].id.starts_with("call_") && calls[1].id.len() == 29);
        assert_eq!(calls[2].name, "broken");
        assert!(calls[2].invalid);
        assert_eq!(calls[2].args, json!({}));
        assert_ne!(calls[1].id, calls[2].id);
    }

    #[test]
    fn a_whole_message_instead_of_deltas_and_junk_lines() {
        let stream = "data: not json\n\n: keep-alive\n\ndata: {\"choices\":[]}\n\ndata: {\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"  Whole answer.  \"},\"finish_reason\":\"stop\"}]}\n\n";
        let server = FakeServer::start(vec![Reply::stream(200, vec![stream.as_bytes().to_vec()])]);
        let (r, shown) = run(&OpenAi::new(&server.base, "m", ""), &[], &Cancel::new());
        let r = r.unwrap();
        assert_eq!(r.turn.text, "Whole answer.");
        assert_eq!(shown, "  Whole answer.  ");
        assert_eq!(r.stop_reason.as_deref(), Some("stop"), "the stream may end without [DONE]");
    }

    #[test]
    fn done_ends_the_read_while_the_connection_stays_open() {
        let stream = sse(&[json!({"choices": [{"delta": {"content": "ok"}}]})]);
        let server = FakeServer::start(vec![Reply::stream(200, vec![stream.into_bytes()]).then_pause(3000)]);
        let t = Instant::now();
        let (r, _) = run(&OpenAi::new(&server.base, "m", ""), &[], &Cancel::new());
        assert_eq!(r.unwrap().turn.text, "ok");
        assert!(t.elapsed() < Duration::from_millis(2000));
    }

    #[test]
    fn an_error_event_in_the_stream() {
        let stream = "data: {\"choices\":[{\"delta\":{\"content\":\"par\"}}]}\n\ndata: {\"error\":{\"message\":\"model overloaded\"}}\n\n";
        let server = FakeServer::start(vec![Reply::stream(200, vec![stream.as_bytes().to_vec()])]);
        let (r, shown) = run(&OpenAi::new(&server.base, "m", ""), &[], &Cancel::new());
        let e = r.unwrap_err();
        assert_eq!(e.message, "model overloaded");
        assert_eq!(e.status, None);
        assert_eq!(shown, "par");
        let server = FakeServer::start(vec![Reply::stream(200, vec![b"data: {\"error\":\"just text\"}\n\n".to_vec()])]);
        assert_eq!(run(&OpenAi::new(&server.base, "m", ""), &[], &Cancel::new()).0.unwrap_err().message, "just text");
        let server = FakeServer::start(vec![Reply::stream(200, vec![b"data: {\"error\":{\"code\":1}}\n\n".to_vec()])]);
        assert_eq!(run(&OpenAi::new(&server.base, "m", ""), &[], &Cancel::new()).0.unwrap_err().message, "[object Object]");
    }

    #[test]
    fn a_400_about_tools_is_retried_once_without_them_and_remembered() {
        let ok = || Reply::stream(200, vec![sse(&[json!({"choices": [{"delta": {"content": "fine"}}]})]).into_bytes()]);
        let server = FakeServer::start(vec![
            Reply::json(400, r#"{"error":{"message":"registry.ollama.ai/library/gemma3:4b does not support tools"}}"#),
            ok(),
            ok(),
        ]);
        let p = OpenAi::new(&server.base, "gemma3:4b", "");
        let (r, _) = run(&p, &[tool()], &Cancel::new());
        assert_eq!(r.unwrap().turn.text, "fine");
        // the next request for this base and model goes without tools from the start
        let (r, _) = run(&p, &[tool()], &Cancel::new());
        assert_eq!(r.unwrap().turn.text, "fine");
        let reqs = server.requests();
        assert_eq!(reqs.len(), 3);
        let has_tools = |i: usize| serde_json::from_str::<Value>(&reqs[i].body).unwrap().get("tools").is_some();
        assert!(has_tools(0));
        assert!(!has_tools(1));
        assert!(!has_tools(2));
    }

    #[test]
    fn other_errors_are_not_retried() {
        // a 400 not about tools
        let server = FakeServer::start(vec![Reply::json(400, r#"{"error":{"message":"context length exceeded"}}"#)]);
        let e = run(&OpenAi::new(&server.base, "m1", ""), &[tool()], &Cancel::new()).0.unwrap_err();
        assert_eq!((e.status, e.message.as_str()), (Some(400), "400: context length exceeded"));
        assert_eq!(server.requests().len(), 1);
        // a 400 about tools when no tools were sent
        let server = FakeServer::start(vec![Reply::json(400, r#"{"error":{"message":"tools broke"}}"#)]);
        let e = run(&OpenAi::new(&server.base, "m2", ""), &[], &Cancel::new()).0.unwrap_err();
        assert_eq!(e.message, "400: tools broke");
        assert_eq!(server.requests().len(), 1);
        // 429 and 5xx: no retry, no backoff
        let server = FakeServer::start(vec![Reply::json(429, r#"{"error":{"message":"Rate limit reached"}}"#)]);
        let e = run(&OpenAi::new(&server.base, "m3", ""), &[tool()], &Cancel::new()).0.unwrap_err();
        assert_eq!((e.status, e.message.as_str()), (Some(429), "429: Rate limit reached"));
        assert_eq!(server.requests().len(), 1);
    }

    #[test]
    fn http_errors_and_their_wording() {
        let server = FakeServer::start(vec![Reply::json(401, r#"{"error":{"message":"Incorrect API key provided","type":"invalid_request_error"}}"#)]);
        let p = OpenAi::new(&server.base, "gpt-x", "sk-wrong");
        let e = run(&p, &[], &Cancel::new()).0.unwrap_err();
        assert_eq!(e.status, Some(401));
        assert_eq!(e.message, "401: Incorrect API key provided");
        assert_eq!(
            crate::assistant::friendly_error(&e, "OpenAI", &p.model, &p.base_url),
            "The OpenAI service refused the key (401: Incorrect API key provided). Check the API key in the settings."
        );
        let server = FakeServer::start(vec![Reply::json(404, "")]);
        let e = run(&OpenAi::new(&server.base, "gpt-x", ""), &[], &Cancel::new()).0.unwrap_err();
        assert_eq!(e.message, "404: Not Found", "an empty body falls back to the status text");
        let server = FakeServer::start(vec![Reply::json(500, "upstream exploded")]);
        let e = run(&OpenAi::new(&server.base, "gpt-x", ""), &[], &Cancel::new()).0.unwrap_err();
        assert_eq!(e.message, "500: upstream exploded");
    }

    #[test]
    fn an_unreachable_address_is_a_network_error() {
        let port = {
            let l = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
            l.local_addr().unwrap().port()
        };
        let base = format!("http://127.0.0.1:{port}/v1");
        let e = run(&OpenAi::new(&base, "m", ""), &[], &Cancel::new()).0.unwrap_err();
        assert_eq!(e.kind, ErrorKind::Network);
        assert_eq!(crate::assistant::friendly_error(&e, "Ollama", "m", &base), format!("Can't reach {base}. Check the address and your connection."));
    }

    #[test]
    fn idle_timeout_fires_when_the_stream_stalls() {
        let first = b"data: {\"choices\":[{\"delta\":{\"content\":\"part\"}}]}\n\n".to_vec();
        let server = FakeServer::start(vec![Reply::stream(200, vec![first]).then_pause(2000)]);
        let mut p = OpenAi::new(&server.base, "m", "");
        p.idle = Duration::from_millis(300);
        let t = Instant::now();
        let (r, shown) = run(&p, &[], &Cancel::new());
        let e = r.unwrap_err();
        assert_eq!(e.message, "The AI service stopped sending anything for 0.005 minutes. Try again.");
        assert_eq!(e.kind, ErrorKind::Service);
        assert_eq!(shown, "part");
        assert!(t.elapsed() < Duration::from_millis(1500), "{:?}", t.elapsed());
    }

    #[test]
    fn a_slow_but_steady_stream_has_no_total_limit() {
        // ten pieces 100 ms apart: 1 s in all, more than twice the idle limit of 400 ms
        let mut parts = Vec::new();
        for i in 0..10 {
            parts.push(format!("data: {{\"choices\":[{{\"delta\":{{\"content\":\"{i}\"}}}}]}}\n\n").into_bytes());
        }
        let server = FakeServer::start(vec![Reply::stream(200, parts).with_gap(100)]);
        let mut p = OpenAi::new(&server.base, "m", "");
        p.idle = Duration::from_millis(400);
        assert_eq!(run(&p, &[], &Cancel::new()).0.unwrap().turn.text, "0123456789");
    }

    #[test]
    fn stop_mid_stream() {
        let first = b"data: {\"choices\":[{\"delta\":{\"content\":\"first\"}}]}\n\n".to_vec();
        let server = FakeServer::start(vec![Reply::stream(200, vec![first]).then_pause(3000)]);
        let p = OpenAi::new(&server.base, "m", "");
        let cancel = Cancel::new();
        let t = Instant::now();
        let mut shown = String::new();
        let r = p.chat("sys", &user("hi"), &[], &cancel, &mut |s| {
            shown.push_str(s);
            cancel.cancel();
        });
        let e = r.unwrap_err();
        assert!(e.is_aborted());
        assert_eq!(crate::assistant::friendly_error(&e, "x", "m", &p.base_url), "Stopped.");
        assert_eq!(shown, "first");
        assert!(t.elapsed() < Duration::from_millis(1500), "{:?}", t.elapsed());
    }

    #[test]
    fn stop_while_waiting_for_the_answer_and_before_sending() {
        let server = FakeServer::start(vec![Reply::stream(200, vec![sse(&[]).into_bytes()]).after(3000)]);
        let p = OpenAi::new(&server.base, "m", "");
        let cancel = Cancel::new();
        let c2 = cancel.clone();
        std::thread::spawn(move || {
            std::thread::sleep(Duration::from_millis(200));
            c2.cancel();
        });
        let t = Instant::now();
        assert!(run(&p, &[], &cancel).0.unwrap_err().is_aborted());
        assert!(t.elapsed() < Duration::from_millis(1500), "{:?}", t.elapsed());
        // already stopped: nothing is sent
        let server = FakeServer::start(vec![]);
        let cancel = Cancel::new();
        cancel.cancel();
        assert!(run(&OpenAi::new(&server.base, "m", ""), &[], &cancel).0.unwrap_err().is_aborted());
        assert!(server.requests().is_empty());
    }

    fn ok_stream() -> Reply {
        Reply::stream(200, vec![sse(&[json!({"choices": [{"delta": {"content": "fine"}}]})]).into_bytes()])
    }

    #[test]
    fn redirects_to_another_origin_are_refused() {
        // `other` would answer, so a followed redirect would succeed
        let other = FakeServer::start(vec![ok_stream()]);
        let server = FakeServer::start(vec![Reply::redirect(307, &format!("{}/v1/chat/completions", other.base))]);
        let e = run(&OpenAi::new(&server.base, "m", "sk-test"), &[], &Cancel::new()).0.unwrap_err();
        assert!(e.message.contains("a different address"), "{}", e.message);
        assert_eq!(e.kind, ErrorKind::Service);
        assert!(other.requests().is_empty());
    }

    #[test]
    fn a_same_origin_307_is_followed_with_the_key_and_body() {
        let server = FakeServer::start(vec![Reply::redirect(307, "/v2/chat/completions"), ok_stream()]);
        let p = OpenAi::new(&format!("{}/v1", server.base), "m", "sk-test");
        assert_eq!(run(&p, &[tool()], &Cancel::new()).0.unwrap().turn.text, "fine");
        let reqs = server.requests();
        assert_eq!(reqs.len(), 2);
        assert_eq!(reqs[1].method, "POST");
        assert_eq!(reqs[1].path, "/v2/chat/completions");
        assert_eq!(reqs[1].header("authorization").as_deref(), Some("Bearer sk-test"));
        assert_eq!(reqs[1].body, reqs[0].body);
        assert!(reqs[1].body.contains("\"tools\""));
    }

    #[test]
    fn a_302_for_the_post_is_refused() {
        let server = FakeServer::start(vec![Reply::redirect(302, "/v2/chat/completions"), ok_stream()]);
        let e = run(&OpenAi::new(&server.base, "m", "sk-test"), &[], &Cancel::new()).0.unwrap_err();
        assert_eq!(e.message, crate::redirect::Refused::MethodChange(302).message());
        assert_eq!(server.requests().len(), 1);
    }

    #[test]
    fn stop_from_another_thread_while_a_read_waits() {
        let first = b"data: {\"choices\":[{\"delta\":{\"content\":\"first\"}}]}\n\n".to_vec();
        let server = FakeServer::start(vec![Reply::stream(200, vec![first]).then_pause(3000)]);
        let cancel = Cancel::new();
        let c2 = cancel.clone();
        std::thread::spawn(move || {
            std::thread::sleep(Duration::from_millis(200));
            c2.cancel();
        });
        let t = Instant::now();
        let (r, shown) = run(&OpenAi::new(&server.base, "m", ""), &[], &cancel);
        assert!(r.unwrap_err().is_aborted());
        assert_eq!(shown, "first");
        assert!(t.elapsed() >= Duration::from_millis(200));
        assert!(t.elapsed() < Duration::from_millis(1500), "{:?}", t.elapsed());
    }

    #[test]
    fn stop_while_an_error_body_stalls() {
        let server = FakeServer::start(vec![Reply::json(500, "{\"error\":").then_pause(3000)]);
        let cancel = Cancel::new();
        let c2 = cancel.clone();
        std::thread::spawn(move || {
            std::thread::sleep(Duration::from_millis(200));
            c2.cancel();
        });
        let t = Instant::now();
        assert!(run(&OpenAi::new(&server.base, "m", ""), &[], &cancel).0.unwrap_err().is_aborted());
        assert!(t.elapsed() < Duration::from_millis(1500), "{:?}", t.elapsed());
    }

    #[test]
    fn the_servers_own_reason_phrase_is_the_status_text() {
        let server = FakeServer::start(vec![Reply::json(503, "").reason("Model Is Loading")]);
        let e = run(&OpenAi::new(&server.base, "m", ""), &[], &Cancel::new()).0.unwrap_err();
        assert_eq!(e.message, "503: Model Is Loading");
    }

    #[test]
    fn only_the_first_mib_of_an_error_body_is_read() {
        // a large validation error still parses
        let body = format!("{{\"error\":{{\"message\":\"short\"}},\"pad\":\"{}\"}}", "x".repeat(200 * 1024));
        let server = FakeServer::start(vec![Reply::json(422, &body)]);
        assert_eq!(run(&OpenAi::new(&server.base, "m", ""), &[], &Cancel::new()).0.unwrap_err().message, "422: short");
        // whole, this JSON would give "short"; cut at 1 MiB it is no JSON and the text is used
        let body = format!("{{\"error\":{{\"message\":\"short\"}},\"pad\":\"{}\"}}", "x".repeat(1536 * 1024));
        let server = FakeServer::start(vec![Reply::json(500, &body)]);
        let e = run(&OpenAi::new(&server.base, "m", ""), &[], &Cancel::new()).0.unwrap_err();
        assert!(e.message.starts_with("500: {\"error\":{\"message\":\"short\"},\"pad\":\"xxx"), "{}", &e.message[..60]);
        assert_eq!(e.message.chars().count(), 505);
    }

    #[test]
    fn answers_and_arguments_past_the_limit_are_refused() {
        let mut asm = Assembler::new(10);
        let mut sink = |_: &str| {};
        assert!(asm.event(r#"{"choices":[{"delta":{"content":"0123456"}}]}"#, &mut sink).unwrap());
        assert_eq!(asm.event(r#"{"choices":[{"delta":{"content":"789ab"}}]}"#, &mut sink).unwrap_err().message, "The AI service sent an event that is too large.");
        let mut asm = Assembler::new(10);
        let call = r#"{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"q\":\"x\"}"}}]}}]}"#;
        assert!(asm.event(call, &mut sink).unwrap());
        assert!(asm.event(call, &mut sink).is_err());
    }

    #[test]
    fn a_key_that_cannot_be_sent_and_a_bad_address() {
        let server = FakeServer::start(vec![ok_stream()]);
        for key in ["sk-a\nb", "sk-\u{e4}", "sk a", "sk-\u{7f}"] {
            let e = run(&OpenAi::new(&server.base, "m", key), &[], &Cancel::new()).0.unwrap_err();
            assert_eq!(e.message, crate::assistant::http::KEY_CHARS, "{key:?}");
        }
        assert!(server.requests().is_empty());
        let e = run(&OpenAi::new("not a url", "m", ""), &[], &Cancel::new()).0.unwrap_err();
        assert_eq!(e.message, "not a url/chat/completions is not a valid URL.");
        assert_ne!(e.kind, ErrorKind::Network);
    }

    #[test]
    fn debug_hides_the_key() {
        let shown = format!("{:?}", OpenAi::new("http://h/v1", "m", "sk-secret-123"));
        assert!(!shown.contains("sk-secret"), "{shown}");
        assert!(shown.contains("<redacted>"));
    }
}
