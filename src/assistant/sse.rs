//! The server-sent events reader shared by the providers (spec 3.1), with the idle timeout and
//! the Stop flag.
//!
//! * The body is decoded as streaming UTF-8 and `\r\n` and `\r` become `\n`.
//! * Events end at a blank line. An event's `data:` lines (one leading space after `data:`
//!   dropped) are joined with `\n`; other lines (event names, comments, ids) are ignored; an event
//!   without data yields nothing. What is left when the stream ends is yielded too.
//! * Stop is checked before every read, and while waiting for one.
//! * Each read has [`IDLE`] (180 s) to bring something; a stream that stalls longer fails with
//!   `The AI service stopped sending anything for 3 minutes. Try again.` There is no limit on the
//!   whole request: thinking models keep sending pings or thinking deltas, which reset the timer.
use super::error::ProviderError;
use std::future::Future;
use std::pin::pin;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::time::{Duration, Instant};

/// `IDLE_MS`: how long a stream may send nothing before it counts as stalled.
pub const IDLE: Duration = Duration::from_millis(180_000);

/// How often a wait looks at the Stop flag.
const POLL: Duration = Duration::from_millis(20);

/// The Stop button: shared between the UI and the worker running the request. Setting it ends
/// the request at the next check (within a few tens of milliseconds), with an `Aborted` error.
#[derive(Clone, Debug, Default)]
pub struct Cancel {
    flag: Arc<AtomicBool>,
    parent: Option<Arc<Cancel>>,
}

impl Cancel {
    pub fn new() -> Cancel {
        Cancel::default()
    }

    pub fn cancel(&self) {
        self.flag.store(true, Ordering::SeqCst);
    }

    pub fn is_cancelled(&self) -> bool {
        self.flag.load(Ordering::SeqCst) || self.parent.as_ref().is_some_and(|c| c.is_cancelled())
    }

    /// Stop one tool without stopping the entire conversation; conversation Stop still reaches it.
    pub(crate) fn child(&self) -> Self {
        Self { parent: Some(Arc::new(self.clone())), ..Self::default() }
    }
}

/// The stall error: `The AI service stopped sending anything for ${ms / 60000} minutes. Try again.`
pub fn idle_error(idle: Duration) -> ProviderError {
    let minutes = idle.as_millis() as f64 / 60000.0;
    ProviderError::new(format!("The AI service stopped sending anything for {minutes} minutes. Try again."))
}

/// The most a single event, a tool call's arguments or the answer text may hold.
pub const MAX_EVENT: usize = 16 * 1024 * 1024;

/// The error for something larger than [`MAX_EVENT`].
pub fn too_large() -> ProviderError {
    ProviderError::new("The AI service sent an event that is too large.")
}

/// Splits a byte stream into the data of its events. Pure, so the splitting rules are tested
/// without a server.
pub struct SseParser {
    /// the start of a UTF-8 sequence cut off at the end of the last chunk
    partial: Vec<u8>,
    /// a `\r` at the end of the last chunk, which may be the first half of a `\r\n`
    cr: bool,
    /// some text was decoded already (TextDecoder drops a byte order mark only at the start)
    started: bool,
    /// the event being received
    buffer: String,
    /// `buffer[..scanned]` was searched for a blank line already
    scanned: usize,
    limit: usize,
}

impl Default for SseParser {
    fn default() -> SseParser {
        SseParser::with_limit(MAX_EVENT)
    }
}

fn data_of(event: &str) -> String {
    event
        .split('\n')
        .filter_map(|l| l.strip_prefix("data:"))
        .map(|l| l.strip_prefix(' ').unwrap_or(l))
        .collect::<Vec<_>>()
        .join("\n")
}

impl SseParser {
    /// A parser that fails once an unfinished event grows past `limit` bytes.
    pub fn with_limit(limit: usize) -> SseParser {
        SseParser { partial: Vec::new(), cr: false, started: false, buffer: String::new(), scanned: 0, limit }
    }

    /// Adds a chunk of the body and returns the data of every event it completed.
    pub fn feed(&mut self, chunk: &[u8]) -> Result<Vec<String>, ProviderError> {
        let mut text = String::new();
        if std::mem::take(&mut self.cr) {
            text.push('\r');
        }
        self.partial.extend_from_slice(chunk);
        let bytes = std::mem::take(&mut self.partial);
        let mut rest = &bytes[..];
        loop {
            match std::str::from_utf8(rest) {
                Ok(s) => {
                    text.push_str(s);
                    break;
                }
                Err(e) => {
                    let (good, bad) = rest.split_at(e.valid_up_to());
                    text.push_str(std::str::from_utf8(good).unwrap_or_default());
                    match e.error_len() {
                        // an incomplete sequence at the end: wait for the next chunk
                        None => {
                            self.partial = bad.to_vec();
                            break;
                        }
                        Some(n) => {
                            text.push('\u{FFFD}');
                            rest = &bad[n..];
                        }
                    }
                }
            }
        }
        if !self.started && !text.is_empty() {
            self.started = true;
            if let Some(t) = text.strip_prefix('\u{FEFF}') {
                text = t.to_string();
            }
        }
        if text.ends_with('\r') {
            text.pop();
            self.cr = true;
        }
        self.buffer.push_str(&text.replace("\r\n", "\n").replace('\r', "\n"));
        let mut out = Vec::new();
        // a blank line may straddle the old end of the buffer
        let mut from = match self.scanned.checked_sub(1) {
            Some(last) if self.buffer.as_bytes()[last] == b'\n' => last,
            _ => self.scanned,
        };
        let mut start = 0;
        while let Some(rel) = self.buffer[from..].find("\n\n") {
            let i = from + rel;
            let data = data_of(&self.buffer[start..i]);
            if !data.is_empty() {
                out.push(data);
            }
            start = i + 2;
            from = start;
        }
        self.buffer.drain(..start);
        self.scanned = self.buffer.len();
        if self.buffer.len() > self.limit {
            return Err(too_large());
        }
        Ok(out)
    }

    /// The stream ended: the data of what is left, if any.
    pub fn finish(&mut self) -> Option<String> {
        if std::mem::take(&mut self.cr) {
            self.buffer.push('\n');
        }
        self.scanned = 0;
        let data = data_of(&std::mem::take(&mut self.buffer));
        (!data.is_empty()).then_some(data)
    }
}

/// Waits for `fut`, giving up with `Aborted` as soon as Stop is pressed (also before it starts).
pub(crate) async fn until_cancelled<F: Future>(fut: F, cancel: &Cancel) -> Result<F::Output, ProviderError> {
    let mut fut = pin!(fut);
    loop {
        if cancel.is_cancelled() {
            return Err(ProviderError::aborted());
        }
        if let Ok(v) = tokio::time::timeout(POLL, fut.as_mut()).await {
            return Ok(v);
        }
    }
}

/// `readWithin(reader, IDLE_MS)`: the next chunk of the body (`None` at its end), failing when
/// nothing comes within `idle` or when Stop is pressed.
async fn read_within(resp: &mut reqwest::Response, cancel: &Cancel, idle: Duration) -> Result<Option<Vec<u8>>, ProviderError> {
    let deadline = Instant::now() + idle;
    let mut fut = pin!(resp.chunk());
    loop {
        if cancel.is_cancelled() {
            return Err(ProviderError::aborted());
        }
        let now = Instant::now();
        if now >= deadline {
            return Err(idle_error(idle));
        }
        match tokio::time::timeout(POLL.min(deadline - now), fut.as_mut()).await {
            Ok(Ok(chunk)) => return Ok(chunk.map(|b| b.to_vec())),
            Ok(Err(e)) => return Err(ProviderError::from_reqwest(&e)),
            Err(_) => {}
        }
    }
}

/// `readSSE(response, signal)`: feeds each event's data to `on_data` until the stream ends or
/// `on_data` returns `Ok(false)` (the OpenAI `[DONE]`). An error from `on_data` ends the read.
pub(crate) async fn read_sse(
    resp: &mut reqwest::Response,
    cancel: &Cancel,
    idle: Duration,
    mut on_data: impl FnMut(String) -> Result<bool, ProviderError>,
) -> Result<(), ProviderError> {
    let mut parser = SseParser::default();
    while let Some(chunk) = read_within(resp, cancel, idle).await? {
        for data in parser.feed(&chunk)? {
            if !on_data(data)? {
                return Ok(());
            }
        }
    }
    if let Some(rest) = parser.finish() {
        on_data(rest)?;
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn all(chunks: &[&[u8]]) -> Vec<String> {
        let mut p = SseParser::default();
        let mut out: Vec<String> = chunks.iter().flat_map(|c| p.feed(c).unwrap()).collect();
        out.extend(p.finish());
        out
    }

    const STREAM: &str = "event: delta\r\ndata: {\"a\":1}\r\n\r\n: comment\r\nid: 7\r\ndata:two\r\ndata:  lines\r\n\r\n\r\n\r\ndata: ünï cödé ✓\r\n\r\ndata: tail";

    #[test]
    fn events_data_lines_and_leftover() {
        assert_eq!(all(&[STREAM.as_bytes()]), vec!["{\"a\":1}", "two\n lines", "ünï cödé ✓", "tail"]);
    }

    #[test]
    fn same_events_at_every_split() {
        let want = all(&[STREAM.as_bytes()]);
        let b = STREAM.as_bytes();
        for i in 0..=b.len() {
            for j in i..=b.len() {
                assert_eq!(all(&[&b[..i], &b[i..j], &b[j..]]), want, "split at {i} and {j}");
            }
        }
        // one byte at a time
        let bytes: Vec<&[u8]> = b.chunks(1).collect();
        assert_eq!(all(&bytes), want);
    }

    #[test]
    fn line_endings() {
        assert_eq!(all(&[b"data: a\r\rdata: b\n\ndata: c\r\n\r\n"]), vec!["a", "b", "c"]);
        // a lone \r at the end of the stream still ends the line
        assert_eq!(all(&[b"data: a\r"]), vec!["a"]);
    }

    #[test]
    fn events_without_data_are_skipped() {
        assert_eq!(all(&[b": ping\n\nevent: x\n\ndata:\n\ndata: y\n\n"]), vec!["y"]);
        assert!(all(&[b""]).is_empty());
    }

    #[test]
    fn only_one_leading_space_is_dropped() {
        assert_eq!(all(&[b"data:   x\n\n"]), vec!["  x"]);
    }

    #[test]
    fn bad_utf8_becomes_a_replacement_character() {
        assert_eq!(all(&[b"data: a\xffb\n\n"]), vec!["a\u{FFFD}b"]);
    }

    #[test]
    fn a_byte_order_mark_at_the_start_is_dropped() {
        assert_eq!(all(&[b"\xef\xbb\xbfdata: a\n\n"]), vec!["a"]);
        // cut inside the mark
        assert_eq!(all(&[b"\xef", b"\xbb", b"\xbfdata: a\n\n"]), vec!["a"]);
        // only one, and only at the very start
        assert!(all(&[b"\xef\xbb\xbf\xef\xbb\xbfdata: a\n\n"]).is_empty());
        assert_eq!(all(&[b"data: a\n\n", b"\xef\xbb\xbfdata: b\n\n"]), vec!["a"]);
        assert_eq!(all(&[b"data: \xef\xbb\xbfb\n\n"]), vec!["\u{FEFF}b"]);
    }

    #[test]
    fn a_blank_line_split_over_chunks_after_a_long_line() {
        let long = "é".repeat(5_000);
        let mut p = SseParser::default();
        assert!(p.feed(format!("data: {long}").as_bytes()).unwrap().is_empty());
        assert!(p.feed(b"\n").unwrap().is_empty());
        assert_eq!(p.feed(b"\ndata: b").unwrap(), vec![long]);
        assert_eq!(p.finish().as_deref(), Some("b"));
    }

    #[test]
    fn an_endless_event_is_refused() {
        let mut p = SseParser::with_limit(1000);
        assert!(p.feed(&[b'a'; 600]).unwrap().is_empty());
        assert_eq!(p.feed(&[b'a'; 600]).unwrap_err().message, "The AI service sent an event that is too large.");
        // finished events do not count
        let mut p = SseParser::with_limit(1000);
        for _ in 0..10 {
            assert_eq!(p.feed(format!("data: {}\n\n", "b".repeat(500)).as_bytes()).unwrap().len(), 1);
        }
        assert_eq!(SseParser::default().limit, 16 * 1024 * 1024);
    }

    #[test]
    fn idle_text() {
        assert_eq!(idle_error(IDLE).message, "The AI service stopped sending anything for 3 minutes. Try again.");
        assert_eq!(idle_error(Duration::from_millis(300)).message, "The AI service stopped sending anything for 0.005 minutes. Try again.");
    }
}
