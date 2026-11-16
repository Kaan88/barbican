//! OAuth 2.0 / OpenID Connect sign-in for Stalwart (and external SSO providers Stalwart trusts).
//! Authorization code + PKCE with a loopback redirect (RFC 8252), discovery via
//! `/api/discover/<account>` (Stalwart 0.16+) or the RFC 8414 / OIDC well-known documents,
//! dynamic client registration (RFC 7591) when offered, refresh tokens.
use base64::Engine;
use reqwest::Url;
use serde_json::{json, Value};
use sha2::Digest;
use std::io::{Read, Write};
use std::net::TcpListener;
use std::time::{Duration, Instant};

const DATA_SCOPE_PREFIX: &str = "urn:ietf:params:oauth:scope:";
const WANTED: [&str; 4] = ["openid", "email", "profile", "offline_access"];

#[derive(Clone, Debug)]
pub struct Meta {
    pub authorization: String,
    pub token: String,
    pub registration: Option<String>,
    pub revocation: Option<String>,
    pub scope: String,
    #[allow(dead_code)]
    pub external: bool,
}

#[derive(Clone, Debug)]
pub struct Tokens {
    pub access: String,
    pub refresh: Option<String>,
    pub expires: Option<Instant>,
    pub token_endpoint: String,
    pub client_id: String,
    /// Remembered account this session belongs to (empty before it is stored).
    pub user: String,
}

fn http() -> reqwest::blocking::Client {
    reqwest::blocking::Client::builder()
        .timeout(Duration::from_secs(15))
        .redirect(reqwest::redirect::Policy::none())
        .build()
        .unwrap()
}

fn origin(base: &str) -> String {
    Url::parse(base).map(|u| u.origin().ascii_serialization()).unwrap_or_else(|_| base.to_string())
}

fn rebase(endpoint: &str, base: &str) -> String {
    if endpoint.starts_with('/') && !endpoint.starts_with("//") {
        return format!("{}{endpoint}", origin(base));
    }
    match (Url::parse(endpoint), Url::parse(base)) {
        // Stalwart may advertise its configured hostname; keep the address the user reaches it under.
        (Ok(e), Ok(_)) => format!("{}{}{}", origin(base), e.path(), e.query().map(|q| format!("?{q}")).unwrap_or_default()),
        _ => endpoint.to_string(),
    }
}

fn pick_scope(supported: &Value) -> String {
    let Some(list) = supported.as_array() else { return "openid email profile offline_access".into() };
    let has = |s: &str| list.iter().any(|x| x == s);
    let data: Vec<&str> = list.iter().filter_map(|x| x.as_str()).filter(|s| s.starts_with(DATA_SCOPE_PREFIX)).collect();
    let mut out: Vec<String> = WANTED.iter().filter(|s| has(s)).map(|s| s.to_string()).collect();
    out.extend(data.iter().map(|s| s.to_string()));
    out.dedup();
    out.join(" ")
}

fn meta_from(doc: &Value, base: &str, own: bool) -> Option<Meta> {
    let a = doc["authorization_endpoint"].as_str().filter(|s| !s.is_empty())?;
    let t = doc["token_endpoint"].as_str().filter(|s| !s.is_empty())?;
    let fix = |e: &str| if own { rebase(e, base) } else { e.to_string() };
    let opt = |k: &str| doc[k].as_str().filter(|s| !s.is_empty()).map(fix);
    Some(Meta {
        authorization: fix(a),
        token: fix(t),
        registration: opt("registration_endpoint"),
        revocation: opt("revocation_endpoint"),
        scope: pick_scope(&doc["scopes_supported"]),
        external: !own,
    })
}

/// Find where this account signs in.
pub fn discover(base: &str, account: &str) -> Result<Meta, String> {
    let c = http();
    let mut own_doc: Option<Value> = None;
    for wk in ["/.well-known/oauth-authorization-server", "/.well-known/openid-configuration"] {
        if let Ok(r) = c.get(format!("{base}{wk}")).send() {
            if r.status().is_success() {
                if let Ok(v) = r.json::<Value>() { own_doc = Some(v); break; }
            }
        }
    }
    if !account.trim().is_empty() {
        if let Ok(r) = c.get(format!("{base}/api/discover/{}", urlencode(account.trim()))).send() {
            if r.status().is_success() {
                if let Ok(doc) = r.json::<Value>() {
                    if let Some(a) = doc["authorization_endpoint"].as_str() {
                        let own = a.starts_with('/')
                            || Url::parse(a).ok().map(|u| u.origin().ascii_serialization()) == Some(origin(base))
                            || own_doc.as_ref().and_then(|d| d["authorization_endpoint"].as_str()) == Some(a);
                        if let Some(m) = meta_from(&doc, base, own) { return Ok(m); }
                    }
                }
            }
        }
    }
    own_doc.as_ref().and_then(|d| meta_from(d, base, true)).ok_or_else(|| "This server does not offer OAuth sign-in.".into())
}

fn urlencode(s: &str) -> String {
    s.bytes().map(|b| match b {
        b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'~' => (b as char).to_string(),
        _ => format!("%{b:02X}"),
    }).collect()
}

fn random(n: usize) -> String {
    let mut buf = vec![0u8; n];
    getrandom::getrandom(&mut buf).expect("rng");
    base64::engine::general_purpose::URL_SAFE_NO_PAD.encode(buf)
}

fn clients_path() -> std::path::PathBuf {
    crate::config_dir().join("oauth-clients.json")
}

/// A client id for this authorization server: registered dynamically when possible, else the configured one.
fn client_id(m: &Meta, redirect: &str, fallback: &str) -> String {
    let Some(reg) = &m.registration else { return fallback.to_string() };
    let mut cache: Value = std::fs::read_to_string(clients_path()).ok().and_then(|s| serde_json::from_str(&s).ok()).unwrap_or(json!({}));
    if let Some(id) = cache[reg.as_str()].as_str() { return id.to_string(); }
    let body = json!({
        "client_name": "Barbican",
        "redirect_uris": [redirect, "http://127.0.0.1:8791/callback", "http://127.0.0.1/callback"],
        "grant_types": ["authorization_code", "refresh_token"],
        "response_types": ["code"],
        "token_endpoint_auth_method": "none",
        "application_type": "native",
        "scope": m.scope,
    });
    match http().post(reg).json(&body).send().and_then(|r| r.json::<Value>()) {
        Ok(v) => match v["client_id"].as_str() {
            Some(id) => {
                cache[reg.as_str()] = json!(id);
                let _ = std::fs::create_dir_all(crate::config_dir());
                let _ = std::fs::write(clients_path(), cache.to_string());
                id.to_string()
            }
            None => fallback.to_string(),
        },
        Err(_) => fallback.to_string(),
    }
}

pub fn open_browser(url: &str) {
    crate::platform::open_url(url);
}

/// Run the interactive browser sign-in. Blocks until the browser comes back (5 minutes max).
pub fn sign_in(base: &str, account: &str, fallback_client: &str) -> Result<Tokens, String> {
    let meta = discover(base, account)?;
    // fixed port so a pre-registered client can list an exact redirect URI
    let listener = TcpListener::bind("127.0.0.1:8791").or_else(|_| TcpListener::bind("127.0.0.1:0")).map_err(|e| e.to_string())?;
    let port = listener.local_addr().map_err(|e| e.to_string())?.port();
    let redirect = format!("http://127.0.0.1:{port}/callback");
    let client = client_id(&meta, &redirect, fallback_client);
    let verifier = random(48);
    let challenge = base64::engine::general_purpose::URL_SAFE_NO_PAD.encode(sha2::Sha256::digest(verifier.as_bytes()));
    let state = random(16);
    let mut params = vec![
        ("response_type", "code".to_string()),
        ("client_id", client.clone()),
        ("redirect_uri", redirect.clone()),
        ("code_challenge", challenge),
        ("code_challenge_method", "S256".into()),
        ("state", state.clone()),
    ];
    if !meta.scope.is_empty() { params.push(("scope", meta.scope.clone())); }
    if !account.trim().is_empty() { params.push(("login_hint", account.trim().to_string())); }
    let url = Url::parse_with_params(&meta.authorization, &params).map_err(|e| e.to_string())?;
    open_browser(url.as_str());

    listener.set_nonblocking(true).map_err(|e| e.to_string())?;
    let deadline = Instant::now() + Duration::from_secs(300);
    let (code, got_state) = loop {
        if Instant::now() > deadline { return Err("Sign-in timed out.".into()); }
        match listener.accept() {
            Ok((mut sock, _)) => {
                let _ = sock.set_nonblocking(false);
                let _ = sock.set_read_timeout(Some(Duration::from_secs(5)));
                let mut buf = [0u8; 8192];
                let n = sock.read(&mut buf).unwrap_or(0);
                let req = String::from_utf8_lossy(&buf[..n]).to_string();
                let path = req.split_whitespace().nth(1).unwrap_or("/").to_string();
                let Ok(u) = Url::parse(&format!("http://127.0.0.1{path}")) else { continue };
                if u.path() != "/callback" {
                    let _ = sock.write_all(b"HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\n\r\n");
                    continue;
                }
                let q: std::collections::HashMap<String, String> = u.query_pairs().into_owned().collect();
                let (title, msg) = if q.contains_key("code") {
                    ("Signed in", "You are signed in to Barbican. You can close this tab.")
                } else {
                    ("Sign-in failed", "Barbican could not sign you in. Return to the app for details.")
                };
                let page = format!("<!doctype html><meta charset=utf-8><title>{title}</title><body style=\"font-family:Segoe UI,sans-serif;background:#111;color:#eee;display:grid;place-items:center;height:90vh\"><div><h2 style=\"color:#db2d54\">{title}</h2><p>{msg}</p></div>");
                let _ = sock.write_all(format!("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{page}", page.len()).as_bytes());
                if let Some(e) = q.get("error") {
                    return Err(format!("The provider refused the sign-in: {e} {}", q.get("error_description").cloned().unwrap_or_default()));
                }
                match q.get("code") {
                    Some(c) => break (c.clone(), q.get("state").cloned().unwrap_or_default()),
                    None => continue,
                }
            }
            Err(ref e) if e.kind() == std::io::ErrorKind::WouldBlock => std::thread::sleep(Duration::from_millis(100)),
            Err(e) => return Err(e.to_string()),
        }
    };
    if got_state != state { return Err("Sign-in state mismatch; try again.".into()); }
    let r = http().post(&meta.token).form(&[
        ("grant_type", "authorization_code"),
        ("code", &code),
        ("redirect_uri", &redirect),
        ("client_id", &client),
        ("code_verifier", &verifier),
    ]).send().map_err(|e| e.to_string())?;
    parse_tokens(r, &meta.token, &client, None)
}

fn parse_tokens(r: reqwest::blocking::Response, endpoint: &str, client: &str, old_refresh: Option<&str>) -> Result<Tokens, String> {
    let status = r.status();
    let v: Value = r.json().unwrap_or(json!({}));
    if !status.is_success() || v["access_token"].as_str().is_none() {
        return Err(format!("Token request failed ({status}): {} {}", v["error"].as_str().unwrap_or(""), v["error_description"].as_str().unwrap_or("")));
    }
    Ok(Tokens {
        access: v["access_token"].as_str().unwrap().to_string(),
        refresh: v["refresh_token"].as_str().map(String::from).or(old_refresh.map(String::from)),
        expires: v["expires_in"].as_u64().map(|s| Instant::now() + Duration::from_secs(s.saturating_sub(30))),
        token_endpoint: endpoint.to_string(),
        client_id: client.to_string(),
        user: String::new(),
    })
}

/// What to do after a refresh attempt, given the token used and what the keyring holds now.
#[derive(Debug, PartialEq)]
pub enum Next {
    /// Another instance rotated the token: retry once with this one.
    Retry(String),
    /// The server rejected the token and nothing newer is stored: the user must sign in again.
    NeedsSignIn,
    /// Network or server trouble: keep everything, try again later.
    Transient,
}

pub fn decide(used: &str, stored: Option<&str>, rejected: bool) -> Next {
    if !rejected { return Next::Transient; }
    match stored {
        Some(s) if !s.is_empty() && s != used => Next::Retry(s.to_string()),
        _ => Next::NeedsSignIn,
    }
}

/// Marker prefix of the error for an account that needs signing in again (never deleted).
pub const NEEDS_SIGN_IN: &str = "Session expired";

fn is_rejection(e: &str) -> bool {
    e.starts_with("Token request failed (4") && ["invalid_grant", "invalid_client", "unauthorized_client"].iter().any(|k| e.contains(k))
}

/// Cross-process lock for refreshing (a lock file in the config dir; stale after 60 s).
struct RefreshLock(std::path::PathBuf);
impl RefreshLock {
    fn acquire() -> Option<RefreshLock> {
        let dir = crate::config_dir();
        let _ = std::fs::create_dir_all(&dir);
        let p = dir.join("oauth-refresh.lock");
        let start = Instant::now();
        loop {
            if std::fs::OpenOptions::new().write(true).create_new(true).open(&p).is_ok() { return Some(RefreshLock(p)); }
            let stale = std::fs::metadata(&p).and_then(|m| m.modified()).ok()
                .and_then(|m| m.elapsed().ok()).is_some_and(|a| a > Duration::from_secs(60));
            if stale { let _ = std::fs::remove_file(&p); continue; }
            if start.elapsed() > Duration::from_secs(15) { return None; }
            std::thread::sleep(Duration::from_millis(100));
        }
    }
}
impl Drop for RefreshLock {
    fn drop(&mut self) { let _ = std::fs::remove_file(&self.0); }
}

/// The refresh token currently stored for this session's account.
fn stored_refresh(t: &Tokens) -> Option<String> {
    if t.user.is_empty() { return None; }
    accounts().into_iter().find(|a| a["user"].as_str() == Some(t.user.as_str()) && a["kind"] == "oauth")
        .and_then(|a| a["refresh"].as_str().map(String::from))
}

/// Write a rotated refresh token back so other instances see it.
fn store_rotated(user: &str, refresh: &str) {
    if user.is_empty() { return; }
    let mut all = accounts();
    let mut changed = false;
    for a in all.iter_mut() {
        if a["user"].as_str() == Some(user) && a["kind"] == "oauth" && a["refresh"].as_str() != Some(refresh) {
            a["refresh"] = json!(refresh);
            changed = true;
        }
    }
    if changed { if let Some(e) = accounts_entry() { let _ = e.set_password(&Value::Array(all).to_string()); } }
    if let Some(mut v) = remembered() {
        if v["user"].as_str() == Some(user) && v["kind"] == "oauth" && v["refresh"].as_str() != Some(refresh) {
            v["refresh"] = json!(refresh);
            if let Some(e) = entry() { let _ = e.set_password(&v.to_string()); }
        }
    }
}

fn refresh_with(t: &Tokens, rt: &str) -> Result<Tokens, String> {
    let r = http().post(&t.token_endpoint).form(&[
        ("grant_type", "refresh_token"),
        ("refresh_token", rt),
        ("client_id", &t.client_id),
    ]).send().map_err(|e| e.to_string())?;
    let mut n = parse_tokens(r, &t.token_endpoint, &t.client_id, Some(rt))?;
    n.user = t.user.clone();
    Ok(n)
}

/// Refresh under a cross-process lock. Stalwart rotates refresh tokens, so another instance may
/// have used ours already: adopt the stored token first, and on a rejection re-read the keyring
/// and retry once. Never deletes anything.
pub fn refresh(t: &Tokens) -> Result<Tokens, String> {
    let mine = t.refresh.as_deref().ok_or(format!("{NEEDS_SIGN_IN}; sign in again."))?;
    let _lock = RefreshLock::acquire();
    let mut used = mine.to_string();
    if let Some(s) = stored_refresh(t) { if !s.is_empty() { used = s; } }
    let mut tried_retry = false;
    loop {
        match refresh_with(t, &used) {
            Ok(n) => {
                if let Some(r) = n.refresh.as_deref() { store_rotated(&t.user, r); }
                return Ok(n);
            }
            Err(e) => match decide(&used, stored_refresh(t).as_deref(), is_rejection(&e)) {
                Next::Retry(s) if !tried_retry => { tried_retry = true; used = s; }
                Next::Retry(_) | Next::NeedsSignIn => return Err(format!("{NEEDS_SIGN_IN}; sign in again. ({e})")),
                Next::Transient => return Err(e),
            },
        }
    }
}

/// Best-effort token revocation on sign-out (RFC 7009).
pub fn revoke(base: &str, t: &Tokens) {
    let Ok(m) = discover(base, "") else { return };
    let Some(ep) = m.revocation else { return };
    for (tok, hint) in [(t.refresh.as_deref(), "refresh_token"), (Some(t.access.as_str()), "access_token")] {
        if let Some(tok) = tok {
            let _ = http().post(&ep).form(&[("token", tok), ("token_type_hint", hint), ("client_id", &t.client_id)]).send();
        }
    }
}

// ---- remembered sessions (Windows Credential Manager) ----
fn entry() -> Option<keyring::Entry> {
    keyring::Entry::new("Barbican", "session").ok()
}

/// kind: "oauth" (secret = refresh token, plus endpoint/client) or "password".
pub fn remember(v: &Value) {
    if let Some(e) = entry() { let _ = e.set_password(&v.to_string()); }
    // keep every signed-in account for the account switcher
    let user = v["user"].as_str().filter(|u| !u.is_empty()).map(String::from).unwrap_or_default();
    if user.is_empty() { return; }
    let mut all = accounts();
    all.retain(|a| a["user"].as_str() != Some(user.as_str()) || a["server"] != v["server"]);
    all.insert(0, v.clone());
    if let Some(e) = accounts_entry() { let _ = e.set_password(&Value::Array(all).to_string()); }
}

fn accounts_entry() -> Option<keyring::Entry> {
    keyring::Entry::new("Barbican", "accounts").ok()
}

/// All remembered accounts (most recently used first).
pub fn accounts() -> Vec<Value> {
    accounts_entry().and_then(|e| e.get_password().ok()).and_then(|s| serde_json::from_str::<Value>(&s).ok())
        .and_then(|v| v.as_array().cloned()).unwrap_or_default()
}

pub fn forget_account(user: &str) {
    let mut all = accounts();
    all.retain(|a| a["user"].as_str() != Some(user));
    if let Some(e) = accounts_entry() { let _ = e.set_password(&Value::Array(all).to_string()); }
}

pub fn remembered() -> Option<Value> {
    entry()?.get_password().ok().and_then(|s| serde_json::from_str(&s).ok())
}

pub fn forget() {
    if let Some(e) = entry() { let _ = e.delete_credential(); }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rotated_elsewhere_retries_with_stored() {
        assert_eq!(decide("old", Some("new"), true), Next::Retry("new".into()));
    }
    #[test]
    fn unchanged_rejection_needs_sign_in() {
        assert_eq!(decide("old", Some("old"), true), Next::NeedsSignIn);
        assert_eq!(decide("old", None, true), Next::NeedsSignIn);
        assert_eq!(decide("old", Some(""), true), Next::NeedsSignIn);
    }
    #[test]
    fn network_error_keeps_and_retries_later() {
        assert_eq!(decide("old", Some("new"), false), Next::Transient);
        assert_eq!(decide("old", Some("old"), false), Next::Transient);
        assert!(!is_rejection("error sending request"));
        assert!(is_rejection("Token request failed (400 Bad Request): invalid_grant "));
        assert!(!is_rejection("Token request failed (503 Service Unavailable):  "));
    }
}

#[cfg(test)]
mod rotation_tests {
    use super::*;

    #[test]
    fn token_changed_retries_with_stored() {
        assert_eq!(decide("old", Some("new"), true), Next::Retry("new".into()));
    }

    #[test]
    fn unchanged_invalid_grant_needs_sign_in() {
        assert_eq!(decide("old", Some("old"), true), Next::NeedsSignIn);
        assert_eq!(decide("old", None, true), Next::NeedsSignIn);
        assert_eq!(decide("old", Some(""), true), Next::NeedsSignIn);
    }

    #[test]
    fn network_error_is_transient() {
        assert_eq!(decide("old", Some("new"), false), Next::Transient);
        assert_eq!(decide("old", Some("old"), false), Next::Transient);
    }

    #[test]
    fn rejection_detection() {
        assert!(is_rejection("Token request failed (400 Bad Request): invalid_grant "));
        assert!(!is_rejection("error sending request: timed out"));
        assert!(!is_rejection("Token request failed (503): "));
    }
}
