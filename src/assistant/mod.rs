//! The AI assistant core: a 1:1 port of the Thunderbird "AI Assistant" extension's provider layer
//! (docs/AI-ASSISTANT-SPEC.md, section 3). Toolkit-free on purpose: nothing here knows about Slint
//! or any other UI. A caller runs a request on a worker thread, gets text chunks through a
//! callback and stops it with a [`Cancel`] flag.
//!
//! * [`history`]: the neutral conversation entries every provider reads and writes (3.0).
//! * [`sse`]: the server-sent events reader, the 180 s idle timeout, cancellation (3.1).
//! * [`error`]: [`ProviderError`], the error text built from an HTTP answer and the friendly
//!   wording the panel shows (3.1, 3.4).
//! * [`openai`]: the OpenAI-compatible Chat Completions client (3.3).
//!
//! Not wired into the app yet (milestone M12 swaps it in for `ai.rs` / `agent.rs`).
#![allow(dead_code)]

pub mod error;
pub mod history;
pub mod openai;
pub mod sse;

#[cfg(test)]
pub(crate) mod fake;

// the API M12 and the new UI use; nothing outside the module calls it yet
#[allow(unused_imports)]
pub use error::{friendly_error, ErrorKind, ProviderError};
#[allow(unused_imports)]
pub use history::{AssistantTurn, Entry, Raw, ToolCall, ToolDef, ToolResult};
#[allow(unused_imports)]
pub use sse::{Cancel, IDLE};

/// Runs a future to completion on a fresh single-threaded runtime on the calling thread. The
/// public calls of this module block; they must not be made from inside an async runtime.
pub(crate) fn block_on<F: std::future::Future>(f: F) -> Result<F::Output, ProviderError> {
    let rt = tokio::runtime::Builder::new_current_thread()
        .enable_all()
        .build()
        .map_err(|e| ProviderError::new(format!("Could not start the request: {e}")))?;
    Ok(rt.block_on(f))
}

/// A tool-call id for a call the service sent without one:
/// `"call_" + crypto.randomUUID().replace(/-/g, "").slice(0, 24)`.
pub fn new_id() -> String {
    let mut b = [0u8; 16];
    let _ = getrandom::getrandom(&mut b);
    // a version 4 UUID, as randomUUID makes (the version nibble falls inside the 24 characters)
    b[6] = (b[6] & 0x0f) | 0x40;
    b[8] = (b[8] & 0x3f) | 0x80;
    let hex: String = b.iter().map(|x| format!("{x:02x}")).collect();
    format!("call_{}", &hex[..24])
}

#[cfg(test)]
mod tests {
    #[test]
    fn new_id_has_the_extension_shape() {
        let a = super::new_id();
        assert_eq!(a.len(), 29);
        assert!(a.starts_with("call_"));
        assert!(a[5..].chars().all(|c| c.is_ascii_hexdigit() && !c.is_ascii_uppercase()));
        assert_eq!(&a[17..18], "4", "version nibble of the UUID");
        assert_ne!(a, super::new_id());
    }
}
