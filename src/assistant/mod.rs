//! The AI assistant core: a 1:1 port of the Thunderbird "AI Assistant" extension's provider layer
//! (docs/AI-ASSISTANT-SPEC.md, section 3). Toolkit-free on purpose: nothing here knows about Slint
//! or any other UI. A caller runs a request on a worker thread, gets text chunks through a
//! callback and stops it with a [`Cancel`] flag.
//!
//! * [`history`]: the neutral conversation entries every provider reads and writes (3.0).
//! * [`sse`]: the server-sent events reader, the 180 s idle timeout, cancellation (3.1).
//! * [`error`]: [`ProviderError`], the error text built from an HTTP answer and the friendly
//!   wording the panel shows (3.1, 3.4).
//! * [`anthropic`]: the Anthropic Messages API client, model names and the Models API (3.2).
//! * [`config`]: the settings, the migration from the earlier ones, the provider list and pick (4).
//! * [`context`]: the open message as text for the model and the user turn (2.0).
//! * `http`: the client, redirect rules and error answers shared by the providers.
//! * [`markdown`]: answers as a block tree, `email` fences as their own block (7.6).
//! * [`openai`]: the OpenAI-compatible Chat Completions client (3.3).
//! * [`prompts`]: the system prompt, the "Now:" line, the quick actions and the writing help's
//!   prompts (2.0, 2.3 to 2.6, 2.13 to 2.19).
//!
//! Not wired into the app yet (milestone M12 swaps it in for `ai.rs` / `agent.rs`).
#![allow(dead_code)]

pub mod anthropic;
pub mod config;
pub mod context;
pub mod error;
pub mod history;
pub(crate) mod http;
pub mod markdown;
pub mod openai;
pub mod prompts;
pub mod sse;

#[cfg(test)]
pub(crate) mod fake;

// the API M12 and the new UI use; nothing outside the module calls it yet
#[allow(unused_imports)]
pub use error::{friendly_error, ErrorKind, ProviderError};
#[allow(unused_imports)]
pub use history::{AssistantTurn, ChatOutcome, Entry, Raw, ToolCall, ToolDef, ToolResult};
#[allow(unused_imports)]
pub use sse::{Cancel, IDLE};

/// The error for a call made on a thread that belongs to a tokio runtime.
pub const IN_RUNTIME: &str = "The assistant's requests can't be made from a thread of an async runtime; run them on a plain worker thread.";

/// Runs a future to completion on a fresh single-threaded runtime on the calling thread. The
/// public calls of this module block: make them from a plain `std::thread`. On any thread of a
/// tokio runtime they fail with [`IN_RUNTIME`]: inside an async task tokio would panic, and a
/// `spawn_blocking` thread is refused too, because tokio offers no public way to tell the two
/// apart (both have a current handle). Running the request on a helper thread instead would need
/// the future, and so the caller's `on_text`, to be `Send`; the UI's workers are std threads, so
/// the simple rule is kept. Their callbacks (`on_text`) run on
/// that runtime, so they must not block on async code or use `reqwest::blocking`; hand the chunk
/// to the UI (a channel, a queued closure) and return.
///
/// The runtime is shut down without waiting for its blocking pool: an address being resolved
/// (getaddrinfo runs there and cannot be interrupted) must not hold up Stop.
pub(crate) fn block_on<F: std::future::Future>(f: F) -> Result<F::Output, ProviderError> {
    if tokio::runtime::Handle::try_current().is_ok() {
        return Err(ProviderError::new(IN_RUNTIME));
    }
    let rt = tokio::runtime::Builder::new_current_thread()
        .enable_all()
        .build()
        .map_err(|e| ProviderError::new(format!("Could not start the request: {e}")))?;
    let out = rt.block_on(f);
    rt.shutdown_background();
    Ok(out)
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

    #[test]
    fn block_on_does_not_wait_for_the_blocking_pool() {
        // stands in for a DNS lookup still running when Stop ends the request
        let t = std::time::Instant::now();
        let r = super::block_on(async {
            drop(tokio::task::spawn_blocking(|| std::thread::sleep(std::time::Duration::from_secs(3))));
            7
        });
        assert_eq!(r.unwrap(), 7);
        assert!(t.elapsed() < std::time::Duration::from_secs(1), "{:?}", t.elapsed());
    }

    #[test]
    fn block_on_inside_a_runtime_is_an_error_not_a_panic() {
        let rt = tokio::runtime::Builder::new_current_thread().enable_all().build().unwrap();
        let r = rt.block_on(async { super::block_on(async { 1 }) });
        assert_eq!(r.unwrap_err().message, super::IN_RUNTIME);
        // a spawn_blocking thread counts as one of the runtime's threads
        let r = rt.block_on(async { tokio::task::spawn_blocking(|| super::block_on(async { 1 })).await.unwrap() });
        assert_eq!(r.unwrap_err().message, super::IN_RUNTIME);
    }
}
