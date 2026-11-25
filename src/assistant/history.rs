//! The neutral history (spec 3.0). Every provider reads and writes these entries, so a
//! conversation can switch provider mid-way:
//!
//! ```text
//! { role: "user", text }
//! { role: "assistant", text, toolCalls: [{ id, name, args }], raw }
//! { role: "tool", results: [{ id, name, content, isError }] }
//! ```
//!
//! `raw` keeps what a provider needs to replay its own turn (Anthropic's content blocks, thinking
//! blocks and signatures included, which the API wants back unchanged).
use serde_json::Value;

#[derive(Clone, Debug, PartialEq)]
pub enum Entry {
    User { text: String },
    Assistant(AssistantTurn),
    Tool { results: Vec<ToolResult> },
}

/// One model turn: the visible text, the tool calls it asked for, and the provider's raw form.
#[derive(Clone, Debug, PartialEq)]
pub struct AssistantTurn {
    pub text: String,
    pub tool_calls: Vec<ToolCall>,
    pub raw: Raw,
}

/// `raw.provider` and what comes with it.
#[derive(Clone, Debug, PartialEq)]
pub enum Raw {
    /// `{provider: "anthropic", content}`: the content blocks as the API sent them.
    Anthropic { content: Vec<Value> },
    /// `{provider: "openai"}`
    OpenAi,
    /// `{provider: "claude", claudeSession}`: Claude on this computer.
    Claude { session: Option<String> },
    /// `{provider: "local"}`: written by the agent loop itself (the step-limit message).
    Local,
}

#[derive(Clone, Debug, PartialEq)]
pub struct ToolCall {
    pub id: String,
    pub name: String,
    /// The parsed arguments (`{}` when the service sent none, or sent JSON that did not parse).
    pub args: Value,
    /// The arguments were not valid JSON; the agent loop answers the call with an error.
    pub invalid: bool,
}

#[derive(Clone, Debug, PartialEq)]
pub struct ToolResult {
    pub id: String,
    pub name: String,
    pub content: String,
    pub is_error: bool,
}

/// A tool offered to the model: `{ name, description, parameters }` with a JSON Schema.
#[derive(Clone, Debug, PartialEq)]
pub struct ToolDef {
    pub name: String,
    pub description: String,
    pub parameters: Value,
}
