//! The agent loop (docs/AI-ASSISTANT-SPEC.md 2.0): up to 30 model turns per user message, the
//! tool calls of each turn run one after another, their results go back as one `tool` entry, and a
//! turn with no tool calls ends the message. `run` blocks: call it on the assistant's worker thread
//! (see [`super::block_on`](super::block_on) about runtimes).
use crate::assistant::config::{self, Provider};
use crate::assistant::error::ProviderError;
use crate::assistant::history::{AssistantTurn, Entry, Raw, ToolCall, ToolResult};
use crate::assistant::tools::{self, Ctx, Outcome, STOPPED};

/// `maxSteps`: the most model turns one user message may take.
pub const MAX_STEPS: usize = 30;

/// After the last step the answer ends with this.
pub const STEP_LIMIT: &str = "I stopped here because this took too many steps. Ask again more specifically?";

/// `Failed: ${message}`: a tool that threw.
pub const FAILED: &str = "Failed: ";

/// What the panel is told while a message runs (2.8's step log and 9's `AnswerEvent`).
pub trait Sink {
    /// Start the next streamed segment after a turn's tools.
    fn turn_start(&mut self) {}
    /// A piece of the model's text, as it streams.
    fn text(&mut self, chunk: &str);
    /// One tool call's result, after it ran (or was skipped).
    fn step(&mut self, call: &ToolCall, result: &Outcome);
}

/// A sink that throws everything away.
pub struct NoSink;

impl Sink for NoSink {
    fn text(&mut self, _chunk: &str) {}
    fn step(&mut self, _call: &ToolCall, _result: &Outcome) {}
}

/// Runs one user message to its end: the turns, the tool calls, and the step limit. Returns
/// `Err(aborted)` after Stop, with the open calls answered [`STOPPED`] first; `Err` with any other
/// error is the provider's.
pub fn run(
    provider: &Provider,
    system: &str,
    history: &mut Vec<Entry>,
    ctx: &Ctx,
    sink: &mut dyn Sink,
) -> Result<(), ProviderError> {
    let settings = &ctx.settings;
    let cancel = &ctx.signal;
    let defs = tools::tools_for(settings);
    for step in 0..MAX_STEPS {
        if cancel.is_cancelled() {
            return Err(ProviderError::aborted());
        }
        if step != 0 {
            sink.turn_start();
        }
        // one model turn; the text streams to the sink as it arrives
        let out = config::chat(settings, provider, system, history, &defs, cancel, &mut |t| sink.text(t))?;
        let turn = out.turn;
        let calls = turn.tool_calls.clone();
        history.push(Entry::Assistant(turn));
        if calls.is_empty() {
            return if cancel.is_cancelled() { Err(ProviderError::aborted()) } else { Ok(()) };
        }
        let mut results = Vec::new();
        for call in &calls {
            let outcome = if cancel.is_cancelled() {
                Outcome::error(STOPPED)
            } else {
                match tools::run_tool(call, ctx) {
                    Ok(o) => o,
                    Err(message) => Outcome::error(tools::cap(&format!("{FAILED}{message}"), tools::MAX_RESULT)),
                }
            };
            sink.step(call, &outcome);
            results.push(ToolResult {
                id: call.id.clone(),
                name: call.name.clone(),
                content: outcome.content,
                is_error: outcome.is_error,
            });
        }
        history.push(Entry::Tool { results });
        // Stop cancels the rest of the calls and ends the message
        if cancel.is_cancelled() {
            return Err(ProviderError::aborted());
        }
    }
    let text = STEP_LIMIT.to_string();
    history.push(Entry::Assistant(AssistantTurn { text: text.clone(), tool_calls: Vec::new(), raw: Raw::Local }));
    sink.turn_start();
    sink.text(&text);
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::assistant::config::{Kind, Settings};
    use crate::assistant::fake::{FakeServer, Reply};
    use crate::assistant::tools::fake as tfake;
    use crate::assistant::tools::INVALID_JSON;
    use crate::assistant::Cancel;
    use serde_json::{json, Value};
    use std::sync::Arc;

    fn provider(base: &str) -> Provider {
        Provider {
            id: "openai".into(),
            kind: Kind::OpenAi,
            label: "OpenAI-compatible",
            title: None,
            model: "gpt-4o-mini".into(),
            base_url: format!("{base}/v1"),
        }
    }

    fn settings(base: &str) -> Settings {
        let mut s = Settings::default();
        s.openai_base_url = format!("{base}/v1");
        s.openai_model = "gpt-4o-mini".into();
        s
    }

    /// One streamed reply: the events, then `[DONE]`.
    fn sse(events: &[Value]) -> Reply {
        let mut text = String::new();
        for e in events {
            text.push_str(&format!("data: {e}\r\n\r\n"));
        }
        text.push_str("data: [DONE]\r\n\r\n");
        Reply::stream(200, vec![text.into_bytes()])
    }

    fn says(text: &str) -> Reply {
        sse(&[json!({"choices": [{"index": 0, "delta": {"content": text}}]}), json!({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]})])
    }

    /// A turn that asks for tool calls and says nothing.
    fn calls(list: &[(&str, &str)]) -> Reply {
        let calls: Vec<Value> = list
            .iter()
            .enumerate()
            .map(|(i, (name, args))| json!({"index": i, "id": format!("c{i}"), "type": "function", "function": {"name": name, "arguments": args}}))
            .collect();
        sse(&[
            json!({"choices": [{"index": 0, "delta": {"tool_calls": calls}}]}),
            json!({"choices": [{"index": 0, "delta": {}, "finish_reason": "tool_calls"}]}),
        ])
    }

    #[derive(Default)]
    struct Record {
        text: String,
        steps: Vec<(String, Outcome)>,
        starts: usize,
    }

    impl Sink for Record {
        fn turn_start(&mut self) {
            self.starts += 1;
        }

        fn text(&mut self, chunk: &str) {
            self.text.push_str(chunk);
        }
        fn step(&mut self, call: &ToolCall, result: &Outcome) {
            self.steps.push((call.name.clone(), result.clone()));
        }
    }

    /// A sink that presses Stop as soon as the first step comes back.
    struct StopAfterFirst<'a> {
        cancel: &'a Cancel,
        steps: Vec<(String, Outcome)>,
    }

    impl Sink for StopAfterFirst<'_> {
        fn text(&mut self, _chunk: &str) {}
        fn step(&mut self, call: &ToolCall, result: &Outcome) {
            self.steps.push((call.name.clone(), result.clone()));
            self.cancel.cancel();
        }
    }

    fn tags() -> Vec<crate::tags::TagDef> {
        vec![crate::tags::TagDef { id: "work".into(), label: "Work".into(), color: "blue".into() }]
    }

    fn ctx(s: &Settings, host: Arc<tfake::TestHost>, cancel: Cancel) -> Ctx {
        Ctx::new(s, host, Arc::new(tfake::mail(&tfake::store(), tags())), cancel)
    }

    #[test]
    fn one_turn_without_tools_ends_the_message() {
        let server = FakeServer::start(vec![says("Hello there.")]);
        let s = settings(&server.base);
        let host = Arc::new(tfake::TestHost::new(true));
        let ctx = ctx(&s, host.clone(), Cancel::new());
        let mut history = vec![Entry::User { text: "Hi".into() }];
        let mut sink = Record::default();
        run(&provider(&server.base), "sys", &mut history, &ctx, &mut sink).unwrap();
        assert_eq!(sink.text, "Hello there.");
        assert!(sink.steps.is_empty());
        assert_eq!(history.len(), 2);
        match &history[1] {
            Entry::Assistant(t) => assert_eq!(t.text, "Hello there."),
            other => panic!("{other:?}"),
        }
        // the tools offered are the registry's, and the settings' frozen copy decided it
        let body: Value = serde_json::from_str(&server.requests()[0].body).unwrap();
        assert_eq!(body["tools"].as_array().unwrap().len(), 13);
        assert_eq!(body["tools"][0]["function"]["name"], "search_mail");
        assert_eq!(host.asked().len(), 0);
    }

    #[test]
    fn a_tool_call_runs_and_its_result_goes_back() {
        let server = FakeServer::start(vec![calls(&[("list_tags", "{}")]), says("You have one tag.")]);
        let s = settings(&server.base);
        let ctx = ctx(&s, Arc::new(tfake::TestHost::new(true)), Cancel::new());
        let mut history = vec![Entry::User { text: "Which tags?".to_string() }];
        let mut sink = Record::default();
        run(&provider(&server.base), "sys", &mut history, &ctx, &mut sink).unwrap();

        assert_eq!(sink.text, "You have one tag.");
        assert_eq!(sink.starts, 1);
        assert_eq!(sink.steps.len(), 1);
        assert_eq!(sink.steps[0].0, "list_tags");
        assert_eq!(sink.steps[0].1, Outcome::ok("[\n {\n  \"key\": \"work\",\n  \"name\": \"Work\",\n  \"color\": \"blue\"\n }\n]"));
        assert_eq!(history.len(), 4);
        match &history[2] {
            Entry::Tool { results } => {
                assert_eq!(results.len(), 1);
                assert_eq!(results[0].id, "c0");
                assert_eq!(results[0].name, "list_tags");
                assert!(!results[0].is_error);
                assert!(results[0].content.contains("\"key\": \"work\""));
            }
            other => panic!("{other:?}"),
        }
        match &history[3] {
            Entry::Assistant(t) => assert_eq!(t.text, "You have one tag."),
            other => panic!("{other:?}"),
        }
        // the second request carries the call and its result
        let body: Value = serde_json::from_str(&server.requests()[1].body).unwrap();
        let msgs = body["messages"].as_array().unwrap();
        assert_eq!(msgs[msgs.len() - 2]["role"], "assistant");
        assert_eq!(msgs[msgs.len() - 2]["tool_calls"][0]["function"]["name"], "list_tags");
        assert_eq!(msgs[msgs.len() - 1]["role"], "tool");
        assert_eq!(msgs[msgs.len() - 1]["tool_call_id"], "c0");
        assert!(msgs[msgs.len() - 1]["content"].as_str().unwrap().contains("work"));
    }

    #[test]
    fn bad_json_and_unknown_tools_are_answered_not_thrown() {
        let server = FakeServer::start(vec![calls(&[("list_tags", "{\"a\": "), ("nope", "{}")]), says("Sorry.")]);
        let s = settings(&server.base);
        let ctx = ctx(&s, Arc::new(tfake::TestHost::new(true)), Cancel::new());
        let mut history = vec![Entry::User { text: "?".to_string() }];
        let mut sink = Record::default();
        run(&provider(&server.base), "sys", &mut history, &ctx, &mut sink).unwrap();
        match &history[2] {
            Entry::Tool { results } => {
                assert_eq!(results[0].content, INVALID_JSON);
                assert_eq!(results[0].content, "The tool input was not valid JSON. Try again.");
                assert!(results[0].is_error);
                assert_eq!(results[1].content, "Unknown tool nope.");
                assert!(results[1].is_error);
            }
            other => panic!("{other:?}"),
        }
    }

    #[test]
    fn a_thrown_tool_error_reaches_the_model_as_failed() {
        let args = r#"{"id": {"id": "M9", "account": "a", "login": "l1"}}"#;
        let server = FakeServer::start(vec![calls(&[("read_message", args)]), says("It is gone.")]);
        let s = settings(&server.base);
        let ctx = ctx(&s, Arc::new(tfake::TestHost::new(true)), Cancel::new());
        let mut history = vec![Entry::User { text: "Read M9".to_string() }];
        let mut sink = Record::default();
        run(&provider(&server.base), "sys", &mut history, &ctx, &mut sink).unwrap();
        match &history[2] {
            Entry::Tool { results } => {
                assert_eq!(results[0].content, "Failed: No message M9 in account a. Use search_mail or list_messages to find it again.");
                assert!(results[0].is_error);
            }
            other => panic!("{other:?}"),
        }
    }

    #[test]
    fn thirty_steps_end_with_the_stop_text() {
        // 30 turns, each asking for the same call
        let server = FakeServer::start((0..MAX_STEPS).map(|_| calls(&[("list_tags", "{}")])).collect());
        let s = settings(&server.base);
        let ctx = ctx(&s, Arc::new(tfake::TestHost::new(true)), Cancel::new());
        let mut history = vec![Entry::User { text: "Loop".to_string() }];
        let mut sink = Record::default();
        run(&provider(&server.base), "sys", &mut history, &ctx, &mut sink).unwrap();
        assert_eq!(server.requests().len(), MAX_STEPS);
        assert!(sink.text.ends_with(STEP_LIMIT));
        assert_eq!(STEP_LIMIT, "I stopped here because this took too many steps. Ask again more specifically?");
        match history.last().unwrap() {
            Entry::Assistant(t) => {
                assert_eq!(t.text, STEP_LIMIT);
                assert!(t.tool_calls.is_empty());
                assert_eq!(t.raw, Raw::Local);
            }
            other => panic!("{other:?}"),
        }
        // 30 assistant turns, each followed by its results
        assert_eq!(history.len(), 2 + MAX_STEPS * 2);
    }

    #[test]
    fn stop_before_the_first_turn_makes_no_request() {
        let server = FakeServer::start(vec![says("never")]);
        let s = settings(&server.base);
        let cancel = Cancel::new();
        cancel.cancel();
        let ctx = ctx(&s, Arc::new(tfake::TestHost::new(true)), cancel);
        let mut history = vec![Entry::User { text: "Hi".into() }];
        let mut sink = Record::default();
        let e = run(&provider(&server.base), "sys", &mut history, &ctx, &mut sink).unwrap_err();
        assert!(e.is_aborted());
        assert_eq!(e.message, "Stopped");
        assert!(server.requests().is_empty());
        assert_eq!(history.len(), 1);
    }

    #[test]
    fn the_calls_left_after_stop_get_the_stop_text() {
        let server = FakeServer::start(vec![calls(&[("list_tags", "{}"), ("list_tags", "{}")])]);
        let s = settings(&server.base);
        let cancel = Cancel::new();
        let ctx = ctx(&s, Arc::new(tfake::TestHost::new(true)), cancel.clone());
        let mut history = vec![Entry::User { text: "Two tags".to_string() }];
        let mut sink = StopAfterFirst { cancel: &cancel, steps: Vec::new() };
        let e = run(&provider(&server.base), "sys", &mut history, &ctx, &mut sink).unwrap_err();
        assert!(e.is_aborted());
        // the first call ran, the one after Stop did not
        assert!(!sink.steps[0].1.is_error);
        assert!(sink.steps[0].1.content.contains("work"));
        assert_eq!(sink.steps[1].1, Outcome::error(STOPPED));
        assert_eq!(sink.steps[1].1.content, "Stopped by the user.");
        match &history[2] {
            Entry::Tool { results } => {
                assert!(!results[0].is_error);
                assert_eq!(results[1].content, "Stopped by the user.");
                assert!(results[1].is_error);
            }
            other => panic!("{other:?}"),
        }
        // and Stop ends the message: no second request went out
        assert_eq!(server.requests().len(), 1);
    }

    #[test]
    fn actions_off_offers_only_the_lookups() {
        let server = FakeServer::start(vec![says("ok")]);
        let mut s = settings(&server.base);
        s.actions = crate::assistant::config::Actions::Off;
        let ctx = ctx(&s, Arc::new(tfake::TestHost::new(true)), Cancel::new());
        let mut history = vec![Entry::User { text: "Hi".into() }];
        let mut sink = Record::default();
        run(&provider(&server.base), "sys", &mut history, &ctx, &mut sink).unwrap();
        let body: Value = serde_json::from_str(&server.requests()[0].body).unwrap();
        let names: Vec<&str> = body["tools"].as_array().unwrap().iter().map(|t| t["function"]["name"].as_str().unwrap()).collect();
        assert_eq!(names.len(), 7);
        assert!(names.contains(&"search_mail") && !names.contains(&"delete_messages"));
    }

    #[test]
    fn a_streaming_error_is_the_providers_error() {
        let server = FakeServer::start(vec![Reply::json(401, r#"{"error":{"message":"no key"}}"#)]);
        let s = settings(&server.base);
        let ctx = ctx(&s, Arc::new(tfake::TestHost::new(true)), Cancel::new());
        let mut history = vec![Entry::User { text: "Hi".into() }];
        let mut sink = Record::default();
        let e = run(&provider(&server.base), "sys", &mut history, &ctx, &mut sink).unwrap_err();
        assert_eq!(e.status, Some(401));
        assert!(!e.is_aborted());
        assert_eq!(history.len(), 1, "a failed turn is not kept");
    }
}
