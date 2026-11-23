//! Redirects for requests that carry credentials, followed by hand.
//!
//! The HTTP client is built with `redirect::Policy::none()`; `send` then follows a redirect only
//! when [`next_url`] allows it, and builds each hop's request itself (so the credentials are
//! attached by the caller's closure, never copied by the HTTP library):
//!   * only to the same origin (scheme, host and port), so the Authorization header (or an API
//!     key header, which reqwest would not strip) never reaches another host;
//!   * never from https to http;
//!   * at most [`MAX_HOPS`] hops;
//!   * a request with a body (POST) only on 307 and 308, which keep the method and the body;
//!   * never to an address with a user name or password in it.
//!
//! A redirect to another origin is refused with a message naming it. (The old behaviour sent the
//! request on without credentials, which ends in a 401 that reads as "wrong password".)
use reqwest::blocking::{RequestBuilder, Response};
use reqwest::Url;

pub const MAX_HOPS: usize = 5;

#[derive(Debug, PartialEq)]
pub enum Refused {
    TooManyHops,
    MissingLocation,
    BadLocation,
    NotHttp,
    Downgrade,
    OtherOrigin(String),
    MethodChange(u16),
}

impl Refused {
    pub fn message(&self) -> String {
        match self {
            Refused::TooManyHops => format!("The server redirected more than {MAX_HOPS} times; check the server address."),
            Refused::MissingLocation => "The server redirected without saying where to; check the server address.".into(),
            Refused::BadLocation => "The server redirected to an address Barbican cannot use.".into(),
            Refused::NotHttp => "The server redirected to an address that is not http or https.".into(),
            Refused::Downgrade => "The server redirected from https to http; Barbican will not send your sign-in over an unencrypted connection.".into(),
            Refused::OtherOrigin(o) => format!("The server redirected to {o}, a different address. Barbican does not send your sign-in there; enter that address as the server instead."),
            Refused::MethodChange(s) => format!("The server answered a request that changes data with a redirect ({s}); Barbican does not repeat it elsewhere."),
        }
    }
}

pub fn is_redirect(status: u16) -> bool {
    matches!(status, 301 | 302 | 303 | 307 | 308)
}

/// Where to go after a redirect from `from`, or why not. `hops` is how many were followed
/// already; `idempotent` is true for a request without a body (GET).
pub fn next_url(from: &Url, status: u16, location: Option<&str>, hops: usize, idempotent: bool) -> Result<Url, Refused> {
    if hops >= MAX_HOPS { return Err(Refused::TooManyHops); }
    let location = location.map(str::trim).filter(|l| !l.is_empty()).ok_or(Refused::MissingLocation)?;
    if !idempotent && !matches!(status, 307 | 308) { return Err(Refused::MethodChange(status)); }
    let to = from.join(location).map_err(|_| Refused::BadLocation)?;
    if !matches!(to.scheme(), "http" | "https") { return Err(Refused::NotHttp); }
    if !to.username().is_empty() || to.password().is_some() { return Err(Refused::BadLocation); }
    if from.scheme() == "https" && to.scheme() == "http" { return Err(Refused::Downgrade); }
    if from.origin() != to.origin() { return Err(Refused::OtherOrigin(to.origin().ascii_serialization())); }
    Ok(to)
}

/// Send the request `build(url)` makes, following redirects as `next_url` allows. The client
/// that `build` uses must not follow redirects itself. `idempotent`: no request body.
pub fn send(first: &str, idempotent: bool, mut build: impl FnMut(&str) -> RequestBuilder) -> Result<Response, String> {
    let mut url = Url::parse(first).map_err(|e| e.to_string())?;
    let mut hops = 0;
    loop {
        let resp = build(url.as_str()).send().map_err(|e| if e.is_timeout() { "The request timed out.".to_string() } else { e.to_string() })?;
        let status = resp.status().as_u16();
        if !is_redirect(status) { return Ok(resp); }
        let location = resp.headers().get(reqwest::header::LOCATION).and_then(|v| v.to_str().ok()).map(String::from);
        url = next_url(&url, status, location.as_deref(), hops, idempotent).map_err(|r| r.message())?;
        hops += 1;
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn u(s: &str) -> Url { Url::parse(s).unwrap() }
    fn next(from: &str, loc: &str) -> Result<String, Refused> {
        next_url(&u(from), 302, Some(loc), 0, true).map(|x| x.to_string())
    }

    #[test]
    fn same_origin_is_followed() {
        assert_eq!(next("https://mail.example.com/.well-known/jmap", "/jmap/session").unwrap(), "https://mail.example.com/jmap/session");
        assert_eq!(next("https://mail.example.com/a/b", "c").unwrap(), "https://mail.example.com/a/c");
        assert_eq!(next("https://mail.example.com/a", "https://mail.example.com/b").unwrap(), "https://mail.example.com/b");
        // an explicit default port is the same origin
        assert_eq!(next("https://mail.example.com/a", "https://mail.example.com:443/b").unwrap(), "https://mail.example.com/b");
        assert_eq!(next("http://localhost:8080/a", "/b").unwrap(), "http://localhost:8080/b");
    }

    #[test]
    fn other_hosts_ports_and_schemes_are_refused() {
        assert_eq!(next("https://mail.example.com/a", "https://evil.example.net/a"), Err(Refused::OtherOrigin("https://evil.example.net".into())));
        assert_eq!(next("https://mail.example.com/a", "https://example.com/a"), Err(Refused::OtherOrigin("https://example.com".into())));
        assert_eq!(next("https://mail.example.com/a", "https://mail.example.com:8443/a"), Err(Refused::OtherOrigin("https://mail.example.com:8443".into())));
        // look-alike hosts
        assert!(matches!(next("https://mail.example.com/a", "https://mail.example.com.evil.test/a"), Err(Refused::OtherOrigin(_))));
        assert!(matches!(next("https://mail.example.com/a", "//other.example.com/a"), Err(Refused::OtherOrigin(_))));
        // http to https on the same host is another origin too: nothing is sent on
        assert!(matches!(next("http://mail.example.com/a", "https://mail.example.com/a"), Err(Refused::OtherOrigin(_))));
    }

    #[test]
    fn https_never_drops_to_http() {
        assert_eq!(next("https://mail.example.com/a", "http://mail.example.com/a"), Err(Refused::Downgrade));
        assert_eq!(next("https://mail.example.com/a", "http://mail.example.com:443/a"), Err(Refused::Downgrade));
        assert_eq!(next("https://mail.example.com/a", "http://evil.example.net/a"), Err(Refused::Downgrade));
    }

    #[test]
    fn odd_locations_are_refused() {
        assert_eq!(next("https://mail.example.com/a", "ftp://mail.example.com/a"), Err(Refused::NotHttp));
        assert_eq!(next("https://mail.example.com/a", "javascript:alert(1)"), Err(Refused::NotHttp));
        assert_eq!(next("https://mail.example.com/a", "https://user:pw@mail.example.com/a"), Err(Refused::BadLocation));
        assert_eq!(next("https://mail.example.com/a", "https://u@mail.example.com/a"), Err(Refused::BadLocation));
        assert_eq!(next("https://mail.example.com/a", "https://[::1"), Err(Refused::BadLocation));
        assert_eq!(next_url(&u("https://mail.example.com/a"), 302, None, 0, true), Err(Refused::MissingLocation));
        assert_eq!(next_url(&u("https://mail.example.com/a"), 302, Some("  "), 0, true), Err(Refused::MissingLocation));
    }

    #[test]
    fn hop_count_is_capped() {
        let from = u("https://mail.example.com/a");
        assert!(next_url(&from, 302, Some("/b"), MAX_HOPS - 1, true).is_ok());
        assert_eq!(next_url(&from, 302, Some("/b"), MAX_HOPS, true), Err(Refused::TooManyHops));
    }

    #[test]
    fn a_request_with_a_body_only_follows_307_and_308() {
        let from = u("https://mail.example.com/jmap");
        assert!(next_url(&from, 307, Some("/api"), 0, false).is_ok());
        assert!(next_url(&from, 308, Some("/api"), 0, false).is_ok());
        for s in [301, 302, 303] {
            assert_eq!(next_url(&from, s, Some("/api"), 0, false), Err(Refused::MethodChange(s)));
        }
        // a plain GET follows them all
        for s in [301, 302, 303, 307, 308] {
            assert!(next_url(&from, s, Some("/api"), 0, true).is_ok());
        }
    }

    #[test]
    fn redirect_statuses() {
        for s in [301, 302, 303, 307, 308] { assert!(is_redirect(s)); }
        for s in [200, 204, 300, 304, 305, 400, 401, 404, 503] { assert!(!is_redirect(s)); }
    }

    #[test]
    fn messages_name_the_problem() {
        assert!(Refused::OtherOrigin("https://x.test".into()).message().contains("https://x.test"));
        assert!(Refused::Downgrade.message().contains("https to http"));
        assert!(Refused::TooManyHops.message().contains('5'));
    }

    /// Against a real socket: the credentials go to the same-origin hop and the chain stops at
    /// a foreign one.
    #[test]
    fn send_follows_same_origin_and_stops_at_a_foreign_host() {
        use std::io::{Read, Write};
        use std::net::TcpListener;
        let l = TcpListener::bind("127.0.0.1:0").unwrap();
        let port = l.local_addr().unwrap().port();
        let seen = std::sync::Arc::new(std::sync::Mutex::new(Vec::<String>::new()));
        let s2 = seen.clone();
        std::thread::spawn(move || {
            for mut sock in l.incoming().flatten() {
                let mut buf = [0u8; 4096];
                let n = sock.read(&mut buf).unwrap_or(0);
                let req = String::from_utf8_lossy(&buf[..n]).to_string();
                let path = req.split_whitespace().nth(1).unwrap_or("/").to_string();
                let has_auth = req.to_ascii_lowercase().contains("authorization: secret");
                s2.lock().unwrap().push(format!("{path} auth={has_auth}"));
                let reply = match path.as_str() {
                    "/start" => "HTTP/1.1 302 Found\r\nLocation: /next\r\nContent-Length: 0\r\nConnection: close\r\n\r\n".to_string(),
                    "/next" => "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok".to_string(),
                    "/away" => "HTTP/1.1 302 Found\r\nLocation: http://127.0.0.2:1/steal\r\nContent-Length: 0\r\nConnection: close\r\n\r\n".to_string(),
                    _ => "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n".to_string(),
                };
                let _ = sock.write_all(reply.as_bytes());
            }
        });
        let http = reqwest::blocking::Client::builder().redirect(reqwest::redirect::Policy::none()).build().unwrap();
        let go = |path: &str| send(&format!("http://127.0.0.1:{port}{path}"), true, |url| http.get(url).header("Authorization", "secret"));
        let r = go("/start").unwrap();
        assert_eq!(r.status().as_u16(), 200);
        assert_eq!(*seen.lock().unwrap(), vec!["/start auth=true".to_string(), "/next auth=true".to_string()]);
        let e = go("/away").unwrap_err();
        assert!(e.contains("127.0.0.2"), "{e}");
        // nothing was sent to the other host (it would have been a connection to port 1)
        assert_eq!(seen.lock().unwrap().len(), 3);
    }
}
