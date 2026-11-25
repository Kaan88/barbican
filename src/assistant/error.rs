//! Provider errors (spec 3.1 "HTTP errors") and the wording the panel shows for them (3.4).
use serde_json::Value;

/// What went wrong, as far as the friendly wording needs to know.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ErrorKind {
    /// The service answered with an error (an HTTP status, an error event, a stall).
    Service,
    /// The user pressed Stop (the extension's `AbortError`).
    Aborted,
    /// The service could not be reached, or the connection broke (the browser's `NetworkError`).
    Network,
}

/// The extension's `ProviderError`: a message, and the HTTP status when there was one.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ProviderError {
    pub message: String,
    pub status: Option<u16>,
    pub kind: ErrorKind,
}

/// The browser's text for a failed fetch, which `friendly_error` recognises.
pub const NETWORK_MESSAGE: &str = "NetworkError when attempting to fetch resource.";

impl ProviderError {
    pub fn new(message: impl Into<String>) -> ProviderError {
        ProviderError { message: message.into(), status: None, kind: ErrorKind::Service }
    }

    pub fn http(status: u16, message: impl Into<String>) -> ProviderError {
        ProviderError { message: message.into(), status: Some(status), kind: ErrorKind::Service }
    }

    /// Stop was pressed (`new DOMException("Stopped", "AbortError")`).
    pub fn aborted() -> ProviderError {
        ProviderError { message: "Stopped".into(), status: None, kind: ErrorKind::Aborted }
    }

    pub fn network() -> ProviderError {
        ProviderError { message: NETWORK_MESSAGE.into(), status: None, kind: ErrorKind::Network }
    }

    /// From an error of the HTTP library.
    pub(crate) fn from_reqwest(e: &reqwest::Error) -> ProviderError {
        if e.is_redirect() {
            // a redirect `redirect_policy` refused: its own sentence is the source
            if let Some(s) = std::error::Error::source(e) {
                return ProviderError::new(s.to_string());
            }
        }
        if e.is_builder() {
            // the request could not be put together (checked addresses and keys never get here)
            return ProviderError::new(format!("Could not build the request: {e}"));
        }
        ProviderError::network()
    }

    pub fn is_aborted(&self) -> bool {
        self.kind == ErrorKind::Aborted
    }
}

impl std::fmt::Display for ProviderError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(&self.message)
    }
}

impl std::error::Error for ProviderError {}

/// JavaScript truthiness of a JSON value.
pub(crate) fn truthy(v: &Value) -> bool {
    match v {
        Value::Null => false,
        Value::Bool(b) => *b,
        Value::Number(n) => n.as_f64().is_some_and(|f| f != 0.0),
        Value::String(s) => !s.is_empty(),
        Value::Array(_) | Value::Object(_) => true,
    }
}

/// `errorFrom(response)`: the error for a non-OK answer. If the body is JSON the message is
/// `error.message`, else `message`, else `error`, else the body text; a non-string is
/// JSON-stringified; cut to 500 characters; falling back to the status text or
/// `Request failed`. The text is `"<status>: <message>"`.
pub fn error_from(status: u16, status_text: &str, body: &str) -> ProviderError {
    let mut message = Value::String(body.to_string());
    if let Ok(Value::Object(json)) = serde_json::from_str::<Value>(body) {
        let pick = [json.get("error").and_then(|e| e.get("message")), json.get("message"), json.get("error")]
            .into_iter()
            .flatten()
            .find(|v| truthy(v));
        if let Some(v) = pick {
            message = v.clone();
        }
    }
    let message = match message {
        Value::String(s) => s,
        other => other.to_string(),
    };
    let message = [message.as_str(), status_text].into_iter().find(|s| !s.is_empty()).unwrap_or("Request failed");
    let message: String = message.chars().take(500).collect();
    ProviderError::http(status, format!("{status}: {message}"))
}

/// `friendlyError(e, provider)`: what the panel shows. `label` is the provider's name in the
/// settings, `model` its model and `base_url` its address (empty for none).
pub fn friendly_error(e: &ProviderError, label: &str, model: &str, base_url: &str) -> String {
    let msg = &e.message;
    if e.kind == ErrorKind::Aborted {
        return "Stopped.".into();
    }
    match e.status {
        Some(401 | 403) => return format!("The {label} service refused the key ({msg}). Check the API key in the settings."),
        Some(404) => return format!("Not found ({msg}). Check the address and the model name ({model}) in the settings."),
        Some(429) => return format!("The service is busy or your quota is used up ({msg}). Try again in a moment."),
        _ => {}
    }
    let lower = msg.to_lowercase();
    let unreachable = e.kind == ErrorKind::Network || lower.contains("networkerror") || lower.contains("failed to fetch");
    if unreachable && !base_url.is_empty() {
        return format!("Can't reach {base_url}. Check the address and your connection.");
    }
    msg.clone()
}

#[cfg(test)]
mod tests {
    use super::*;

    fn msg(status: u16, text: &str, body: &str) -> String {
        error_from(status, text, body).message
    }

    #[test]
    fn error_body_shapes() {
        assert_eq!(msg(400, "Bad Request", r#"{"error":{"message":"bad tool"}}"#), "400: bad tool");
        assert_eq!(msg(400, "Bad Request", r#"{"message":"top level"}"#), "400: top level");
        assert_eq!(msg(400, "Bad Request", r#"{"error":"plain"}"#), "400: plain");
        // error.message empty: the next candidate wins, as with `||`
        assert_eq!(msg(400, "", r#"{"error":{"message":""},"message":"second"}"#), "400: second");
        // an object without a message is stringified
        assert_eq!(msg(500, "", r#"{"error":{"code":5,"type":"x"}}"#), r#"500: {"code":5,"type":"x"}"#);
        assert_eq!(msg(500, "", r#"{"error":true}"#), "500: true");
        // nothing usable in the JSON: the body text
        assert_eq!(msg(500, "", r#"{"detail":"x"}"#), r#"500: {"detail":"x"}"#);
        assert_eq!(msg(502, "Bad Gateway", "null"), "502: null");
        assert_eq!(msg(502, "Bad Gateway", "<html>oops</html>"), "502: <html>oops</html>");
        // empty body: the status text, then `Request failed`
        assert_eq!(msg(503, "Service Unavailable", ""), "503: Service Unavailable");
        assert_eq!(msg(503, "", ""), "503: Request failed");
        let e = error_from(418, "", "");
        assert_eq!(e.status, Some(418));
        assert_eq!(e.kind, ErrorKind::Service);
    }

    #[test]
    fn error_message_is_cut_to_500_characters() {
        let long = "é".repeat(600);
        let m = msg(400, "", &long);
        assert_eq!(m.chars().count(), "400: ".len() + 500);
    }

    #[test]
    fn friendly_wording() {
        let f = |e: &ProviderError| friendly_error(e, "OpenAI", "gpt-x", "http://h/v1");
        assert_eq!(f(&ProviderError::aborted()), "Stopped.");
        assert_eq!(f(&ProviderError::http(401, "401: no")), "The OpenAI service refused the key (401: no). Check the API key in the settings.");
        assert_eq!(f(&ProviderError::http(403, "403: no")), "The OpenAI service refused the key (403: no). Check the API key in the settings.");
        assert_eq!(f(&ProviderError::http(404, "404: gone")), "Not found (404: gone). Check the address and the model name (gpt-x) in the settings.");
        assert_eq!(f(&ProviderError::http(429, "429: slow")), "The service is busy or your quota is used up (429: slow). Try again in a moment.");
        assert_eq!(f(&ProviderError::network()), "Can't reach http://h/v1. Check the address and your connection.");
        assert_eq!(friendly_error(&ProviderError::network(), "OpenAI", "m", ""), NETWORK_MESSAGE);
        assert_eq!(f(&ProviderError::http(500, "500: boom")), "500: boom");
    }
}
