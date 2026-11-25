//! The HTTP pieces every provider shares: the client, the redirect rules, sending with Stop, and
//! the error built from a non-OK answer (spec 3.1 "HTTP errors").
use super::error::{error_from, ProviderError};
use super::sse::{until_cancelled, Cancel};

/// How much of an error answer's body is read; the message is cut to 500 characters anyway.
pub(crate) const MAX_ERROR_BODY: usize = 64 * 1024;

/// Redirects are followed only by Barbican's rules for requests that carry credentials
/// (`crate::redirect`): same origin, no https to http, a POST only on 307 and 308, at most 5 hops.
fn redirect_policy() -> reqwest::redirect::Policy {
    reqwest::redirect::Policy::custom(|attempt| {
        let hops = attempt.previous().len().saturating_sub(1);
        let Some(from) = attempt.previous().last().cloned() else { return attempt.stop() };
        let status = attempt.status().as_u16();
        let to = attempt.url().to_string();
        match crate::redirect::next_url(&from, status, Some(&to), hops, false) {
            Ok(_) => attempt.follow(),
            Err(r) => attempt.error(r.message()),
        }
    })
}

/// A client for one request (no total timeout: the stream has its own idle limit).
pub(crate) fn client() -> Result<reqwest::Client, ProviderError> {
    reqwest::Client::builder()
        .redirect(redirect_policy())
        .build()
        .map_err(|e| ProviderError::new(format!("Could not start the request: {e}")))
}

/// The address must parse before anything is sent (`fetch` throws `… is not a valid URL.`).
pub(crate) fn check_url(url: &str) -> Result<(), ProviderError> {
    match reqwest::Url::parse(url) {
        Ok(u) if matches!(u.scheme(), "http" | "https") => Ok(()),
        _ => Err(ProviderError::new(format!("{url} is not a valid URL."))),
    }
}

/// A key that cannot go in a header (a line break, a control character, a non-ASCII letter).
pub(crate) const KEY_CHARS: &str = "The API key has characters that can't be sent. Check the API key in the settings.";

pub(crate) fn check_header_value(value: &str) -> Result<(), ProviderError> {
    reqwest::header::HeaderValue::from_str(value).map(|_| ()).map_err(|_| ProviderError::new(KEY_CHARS))
}

/// Sends the request, giving up as soon as Stop is pressed.
pub(crate) async fn send(req: reqwest::RequestBuilder, cancel: &Cancel) -> Result<reqwest::Response, ProviderError> {
    until_cancelled(req.send(), cancel).await?.map_err(|e| ProviderError::from_reqwest(&e))
}

/// `response.statusText`: the server's own reason phrase when it sent one (hyper keeps only a
/// non-standard one), else the standard phrase for the status.
pub(crate) fn status_text(resp: &reqwest::Response) -> String {
    if let Some(r) = resp.extensions().get::<hyper::ext::ReasonPhrase>() {
        return String::from_utf8_lossy(r.as_bytes()).to_string();
    }
    resp.status().canonical_reason().unwrap_or("").to_string()
}

/// `errorFrom(response)`: reads up to [`MAX_ERROR_BODY`] of the body (Stop still works; a body
/// that breaks off counts as empty, as in the extension) and builds the error.
pub(crate) async fn error_of(mut resp: reqwest::Response, cancel: &Cancel) -> Result<ProviderError, ProviderError> {
    let status = resp.status().as_u16();
    let text = status_text(&resp);
    let mut body = Vec::new();
    while body.len() < MAX_ERROR_BODY {
        match until_cancelled(resp.chunk(), cancel).await? {
            Ok(Some(c)) => body.extend_from_slice(&c),
            Ok(None) => break,
            // `response.text()` failed: the extension goes on with an empty text
            Err(_) => {
                body.clear();
                break;
            }
        }
    }
    body.truncate(MAX_ERROR_BODY);
    Ok(error_from(status, &text, &String::from_utf8_lossy(&body)))
}
