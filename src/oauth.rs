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

/// The JSON of a token response, or the error text for a refused one.
fn token_json(r: reqwest::blocking::Response) -> Result<Value, String> {
    let status = r.status();
    let v: Value = r.json().unwrap_or(json!({}));
    if !status.is_success() || v["access_token"].as_str().is_none() {
        return Err(format!("Token request failed ({status}): {} {}", v["error"].as_str().unwrap_or(""), v["error_description"].as_str().unwrap_or("")));
    }
    Ok(v)
}

fn parse_tokens(r: reqwest::blocking::Response, endpoint: &str, client: &str, old_refresh: Option<&str>) -> Result<Tokens, String> {
    let v = token_json(r)?;
    Ok(Tokens {
        access: v["access_token"].as_str().unwrap().to_string(),
        refresh: v["refresh_token"].as_str().map(String::from).or(old_refresh.map(String::from)),
        expires: v["expires_in"].as_u64().map(|s| Instant::now() + Duration::from_secs(s.saturating_sub(30))),
        token_endpoint: endpoint.to_string(),
        client_id: client.to_string(),
        user: String::new(),
    })
}

/// What to do after a refresh attempt.
#[derive(Debug, PartialEq)]
pub enum Next {
    /// Another token is worth a try: one another instance stored, or ours when the stored one
    /// turned out dead (a write that failed after our last rotation leaves the stored one stale).
    Retry(String),
    /// The server rejected every token we have: the user must sign in again.
    NeedsSignIn,
    /// Network or server trouble: keep everything, try again later.
    Transient,
}

/// After a refresh attempt with `tried` (every token used so far, the last one just now): the
/// stored token first, then this session's own, each at most once, so the loop is over after at
/// most three requests and a dead sign-in is never retried.
pub fn next_token(tried: &[String], mine: &str, stored: Option<&str>, rejected: bool) -> Next {
    if !rejected { return Next::Transient; }
    [stored, Some(mine)].into_iter().flatten()
        .find(|c| !c.is_empty() && !tried.iter().any(|t| t == c))
        .map(|c| Next::Retry(c.to_string()))
        .unwrap_or(Next::NeedsSignIn)
}

/// Marker prefix of the error for an account that needs signing in again (never deleted).
pub const NEEDS_SIGN_IN: &str = "Session expired";

fn is_rejection(e: &str) -> bool {
    e.starts_with("Token request failed (4") && ["invalid_grant", "invalid_client", "unauthorized_client"].iter().any(|k| e.contains(k))
}

/// Cross-process lock for refreshing: an operating-system lock on a file in the config folder
/// (per profile). The system releases it when the holder dies, so it never goes stale. After
/// 15 s without it `acquire` gives up and the refresh goes ahead without it (adopting the
/// stored token below still covers a rotation by someone else).
struct RefreshLock(std::fs::File);
impl RefreshLock {
    fn acquire() -> Option<RefreshLock> {
        let dir = crate::config_dir();
        let _ = std::fs::create_dir_all(&dir);
        let f = std::fs::OpenOptions::new().write(true).create(true).truncate(false).open(dir.join("oauth-refresh.lock")).ok()?;
        let start = Instant::now();
        loop {
            if f.try_lock().is_ok() { return Some(RefreshLock(f)); }
            if start.elapsed() > Duration::from_secs(15) { return None; }
            std::thread::sleep(Duration::from_millis(100));
        }
    }
}
impl Drop for RefreshLock {
    fn drop(&mut self) { let _ = self.0.unlock(); }
}

/// Whether a stored entry is the OAuth account this session belongs to: the same user, and the
/// same token endpoint when both sides know theirs (two servers may share a user name).
fn is_session_account(a: &Value, user: &str, endpoint: &str) -> bool {
    a["kind"] == "oauth" && a["user"].as_str() == Some(user) && match a["endpoint"].as_str() {
        Some(e) if !e.is_empty() && !endpoint.is_empty() => e == endpoint,
        _ => true,
    }
}

/// The refresh token stored for an account in a list of remembered accounts.
fn find_refresh(all: &[Value], user: &str, endpoint: &str) -> Option<String> {
    if user.is_empty() { return None; }
    all.iter().find(|a| is_session_account(a, user, endpoint)).and_then(|a| a["refresh"].as_str().map(String::from))
}

/// Put a rotated refresh token into every entry of that account; whether anything changed. An
/// entry is the account's when it has the same user (not for an empty `user`) and endpoint, or,
/// whatever its user, when its endpoint is ours and its refresh token is the one just `used`: a
/// 0.1.0 session of an SSO sign-in has no user, and a stale duplicate of the account is healed
/// the same way.
fn rotate_in(all: &mut [Value], user: &str, endpoint: &str, used: &str, refresh: &str) -> bool {
    let mut changed = false;
    for a in all.iter_mut() {
        let by_user = !user.is_empty() && is_session_account(a, user, endpoint);
        let by_token = a["kind"] == "oauth" && !used.is_empty() && !endpoint.is_empty()
            && a["endpoint"].as_str() == Some(endpoint) && a["refresh"].as_str() == Some(used);
        if (by_user || by_token) && a["refresh"].as_str() != Some(refresh) {
            a["refresh"] = json!(refresh);
            changed = true;
        }
    }
    changed
}

/// The refresh token currently stored for this session's account.
fn stored_refresh_in(b: &dyn crate::secrets::Backend, profile: &str, t: &Tokens) -> Option<String> {
    find_refresh(&accounts_in(b, profile), &t.user, &t.token_endpoint)
}

/// Store a secret; a failed write (the keyring was busy) is tried once more, because the old
/// refresh token is already dead and the rotated one must not get lost.
fn put_in(b: &dyn crate::secrets::Backend, profile: &str, name: &str, value: &str) -> bool {
    crate::secrets::save(b, profile, name, value) || crate::secrets::save(b, profile, name, value)
}

/// Write a rotated refresh token (`new`, issued for `used`) back so other instances see it. The
/// caller holds the [`RefreshLock`].
fn store_rotated_in(b: &dyn crate::secrets::Backend, profile: &str, user: &str, endpoint: &str, used: &str, new: &str) {
    let mut all = accounts_in(b, profile);
    if rotate_in(&mut all, user, endpoint, used, new) { put_in(b, profile, "accounts", &Value::Array(all).to_string()); }
    if let Some(mut v) = remembered_in(b, profile) {
        if rotate_in(std::slice::from_mut(&mut v), user, endpoint, used, new) { put_in(b, profile, "session", &v.to_string()); }
    }
}

/// The token endpoint, as far as the refresh grant is concerned (a fake in the tests).
pub trait TokenServer {
    /// Exchange a refresh token: (access token, the new refresh token when the server rotates
    /// it, seconds the access token lives, 0 when not said).
    fn refresh(&self, endpoint: &str, client: &str, refresh_token: &str) -> Result<(String, Option<String>, u64), String>;
}

/// The real token endpoint over HTTP.
struct HttpServer;
impl TokenServer for HttpServer {
    fn refresh(&self, endpoint: &str, client: &str, rt: &str) -> Result<(String, Option<String>, u64), String> {
        let r = http().post(endpoint).form(&[
            ("grant_type", "refresh_token"),
            ("refresh_token", rt),
            ("client_id", client),
        ]).send().map_err(|e| e.to_string())?;
        let v = token_json(r)?;
        Ok((v["access_token"].as_str().unwrap_or("").to_string(), v["refresh_token"].as_str().map(String::from), v["expires_in"].as_u64().unwrap_or(0)))
    }
}

fn refresh_with(srv: &dyn TokenServer, t: &Tokens, rt: &str) -> Result<Tokens, String> {
    let (access, new, expires_in) = srv.refresh(&t.token_endpoint, &t.client_id, rt)?;
    Ok(Tokens {
        access,
        refresh: new.or(Some(rt.to_string())),
        expires: (expires_in > 0).then(|| Instant::now() + Duration::from_secs(expires_in.saturating_sub(30))),
        token_endpoint: t.token_endpoint.clone(),
        client_id: t.client_id.clone(),
        user: t.user.clone(),
    })
}

/// The refresh loop of [`refresh`] against one secret store and token endpoint; the caller holds
/// the [`RefreshLock`].
fn refresh_in(b: &dyn crate::secrets::Backend, profile: &str, srv: &dyn TokenServer, t: &Tokens) -> Result<Tokens, String> {
    let mine = t.refresh.as_deref().ok_or(format!("{NEEDS_SIGN_IN}; sign in again."))?;
    let mut used = stored_refresh_in(b, profile, t).filter(|s| !s.is_empty()).unwrap_or_else(|| mine.to_string());
    let mut tried: Vec<String> = vec![];
    loop {
        tried.push(used.clone());
        match refresh_with(srv, t, &used) {
            Ok(n) => {
                if let Some(r) = n.refresh.as_deref() { store_rotated_in(b, profile, &t.user, &t.token_endpoint, &used, r); }
                return Ok(n);
            }
            Err(e) => match next_token(&tried, mine, stored_refresh_in(b, profile, t).as_deref(), is_rejection(&e)) {
                Next::Retry(s) => used = s,
                Next::NeedsSignIn => return Err(format!("{NEEDS_SIGN_IN}; sign in again. ({e})")),
                Next::Transient => return Err(e),
            },
        }
    }
}

/// Refresh under a cross-process lock. Stalwart rotates refresh tokens, so another instance may
/// have used ours already: adopt the stored token first, and on a rejection try what the keyring
/// holds now and our own (see `next_token`). A rejection of all of them is final: the error says
/// "Session expired" and the caller must not try again until the user signs in. Never deletes
/// anything.
pub fn refresh(t: &Tokens) -> Result<Tokens, String> {
    if t.refresh.is_none() { return Err(format!("{NEEDS_SIGN_IN}; sign in again.")); }
    let _lock = RefreshLock::acquire();
    refresh_in(crate::secrets::store(), crate::profile::current(), &HttpServer, t)
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

// ---- remembered sessions (secrets.rs: Credential Manager / Secret Service, one set per profile) ----

/// Where a session handed to [`remember`] comes from.
#[derive(Clone, Debug, PartialEq)]
pub enum Origin {
    /// A new sign-in: its tokens replace the stored ones.
    SignIn,
    /// A session restored from the store. `read` is the refresh token that was read from the
    /// session entry at the start of the restore. The restored session always refreshed, so its
    /// token is newer than anything stored, unless another instance stored one meanwhile.
    Restore { read: Option<String> },
}

/// The refresh token another instance stored in the session entry since this restore read it:
/// the stored session is oauth, of this endpoint and this account (or of none yet), and holds a
/// token that is neither the one read nor ours.
fn newer_session_token(session: &Value, ours: &Value, read: Option<&str>, names: &[&str], server: &str) -> Option<String> {
    let ep = ours["endpoint"].as_str().unwrap_or("");
    if session["kind"] != "oauth" || ep.is_empty() || session["endpoint"].as_str() != Some(ep) { return None; }
    if !session["user"].as_str().unwrap_or("").is_empty() && !is_account(session, names, server) { return None; }
    let r = session["refresh"].as_str().filter(|r| !r.is_empty())?;
    (Some(r) != read && Some(r) != ours["refresh"].as_str()).then(|| r.to_string())
}

/// The remembered-account list after remembering `v`, and `v` as it is to be stored. Every
/// entry of the same account (the user as `v` names it or as any of `names`, case-insensitive,
/// on the same server as the address it resolves to) is replaced by `v` and moved first, so a
/// 0.1.0 entry (typed address) and a newer one (the server's own) become one. Every other entry
/// stays exactly as `all` (read under the lock just now) has it, so a refresh token another
/// instance rotated meanwhile survives.
///
/// The session entry is the source of truth: the token of a restored session (`Restore`) is
/// this instance's own (never older than the one it just used), unless `session` (the stored
/// entry, read under the same lock) holds one another instance wrote since. The list never
/// gives a token to a restored session, because an entry of it can be a rotation behind.
fn merge_account(mut all: Vec<Value>, v: &Value, origin: &Origin, session: Option<&Value>, names: &[&str]) -> (Vec<Value>, Value) {
    let mut v = v.clone();
    let user = v["user"].as_str().unwrap_or("").to_string();
    let server = v["server"].as_str().unwrap_or("").to_string();
    let mut names: Vec<&str> = names.to_vec();
    names.push(&user);
    if let (Origin::Restore { read }, true) = (origin, v["kind"] == "oauth") {
        if let Some(r) = session.and_then(|s| newer_session_token(s, &v, read.as_deref(), &names, &server)) { v["refresh"] = json!(r); }
    }
    if !user.is_empty() {
        all.retain(|a| !is_account(a, &names, &server));
        all.insert(0, v.clone());
    }
    (all, v)
}

/// [`remember`] against one secret store; the caller holds the [`RefreshLock`].
fn remember_in(b: &dyn crate::secrets::Backend, profile: &str, v: &Value, origin: &Origin) {
    let session = remembered_in(b, profile);
    let (all, v) = merge_account(accounts_in(b, profile), v, origin, session.as_ref(), &[]);
    put_in(b, profile, "session", &v.to_string());
    // keep every signed-in account for the account switcher
    if v["user"].as_str().is_some_and(|u| !u.is_empty()) { put_in(b, profile, "accounts", &Value::Array(all).to_string()); }
}

/// kind: "oauth" (secret = refresh token, plus endpoint/client) or "password". The
/// read-modify-write runs under the refresh lock, so it cannot overwrite a token another
/// instance rotated and stored in between.
pub fn remember(v: &Value, origin: Origin) {
    let _lock = RefreshLock::acquire();
    remember_in(crate::secrets::store(), crate::profile::current(), v, &origin);
}

/// Replace one account's entry where it stands, without making it the startup session: the list
/// keeps its order, and the session entry changes only when it already is this account. (A
/// `remember` moves the account first and makes it the session.) Nothing is written when the
/// account has no entry.
fn update_account_in(b: &dyn crate::secrets::Backend, profile: &str, v: &Value) {
    let user = v["user"].as_str().unwrap_or("");
    let server = v["server"].as_str().unwrap_or("");
    let mut all = accounts_in(b, profile);
    let Some(slot) = all.iter_mut().find(|a| is_account(a, &[user], server)) else { return };
    *slot = v.clone();
    let v = &slot.clone();
    put_in(b, profile, "accounts", &Value::Array(all).to_string());
    if remembered_in(b, profile).is_some_and(|s| is_account(&s, &[user], server)) { put_in(b, profile, "session", &v.to_string()); }
}

/// Put the password `v` back into the account's saved entry, only while that entry still is a
/// password sign-in whose password was cleared (an account signed in again meanwhile, with SSO
/// or a password, is never overwritten). Like [`update_account_in`] it keeps the list's order and
/// changes the session entry only when it is this account, and the same cleared entry. True when
/// the list entry was written.
fn repair_account_in(b: &dyn crate::secrets::Backend, profile: &str, v: &Value) -> bool {
    let user = v["user"].as_str().unwrap_or("");
    let server = v["server"].as_str().unwrap_or("");
    let cleared = |a: &Value| a["kind"] == "password" && a["pass"].as_str().unwrap_or("").is_empty() && is_account(a, &[user], server);
    let mut all = accounts_in(b, profile);
    let Some(slot) = all.iter_mut().find(|a| is_account(a, &[user], server)) else { return false };
    if !cleared(slot) { return false; }
    *slot = v.clone();
    let v = &slot.clone();
    put_in(b, profile, "accounts", &Value::Array(all).to_string());
    if remembered_in(b, profile).is_some_and(|s| cleared(&s)) { put_in(b, profile, "session", &v.to_string()); }
    true
}

/// [`repair_account_in`] on the real store, under the refresh lock (read and write in one go).
pub fn repair_account(v: &Value) -> bool {
    let _lock = RefreshLock::acquire();
    repair_account_in(crate::secrets::store(), crate::profile::current(), v)
}

/// [`update_account_in`] on the real store, under the refresh lock.
pub fn update_account(v: &Value) {
    let _lock = RefreshLock::acquire();
    update_account_in(crate::secrets::store(), crate::profile::current(), v);
}

/// All remembered accounts (most recently used first).
pub fn accounts() -> Vec<Value> {
    crate::secrets::get("accounts").and_then(|s| serde_json::from_str::<Value>(&s).ok())
        .and_then(|v| v.as_array().cloned()).unwrap_or_default()
}

/// The list without the account that signed in as any of `names` on `server` (names compared
/// without regard to case: the name typed on the login form and the name the server reports
/// can differ; servers compared as the addresses they resolve to). The same name on another
/// server is another account and stays. An empty `server` matches every server.
pub fn without_account(all: Vec<Value>, names: &[&str], server: &str) -> Vec<Value> {
    all.into_iter().filter(|a| !is_account(a, names, server)).collect()
}

/// Whether a stored entry is the account that signed in as any of `names` on `server`
/// (see [`without_account`]).
fn is_account(a: &Value, names: &[&str], server: &str) -> bool {
    let s = a["server"].as_str().unwrap_or("");
    let same_server = server.is_empty() || s.is_empty()
        || crate::jmap::normalize_base(s).eq_ignore_ascii_case(&crate::jmap::normalize_base(server));
    let u = a["user"].as_str().unwrap_or("");
    same_server && names.iter().any(|n| !n.is_empty() && n.eq_ignore_ascii_case(u))
}

fn accounts_in(b: &dyn crate::secrets::Backend, profile: &str) -> Vec<Value> {
    crate::secrets::load(b, profile, "accounts").and_then(|s| serde_json::from_str::<Value>(&s).ok())
        .and_then(|v| v.as_array().cloned()).unwrap_or_default()
}

fn remembered_in(b: &dyn crate::secrets::Backend, profile: &str) -> Option<Value> {
    crate::secrets::load(b, profile, "session").and_then(|s| serde_json::from_str(&s).ok())
}

/// Sign-out of an account, against one secret store: its entry leaves the switcher's list
/// (`accounts`), and the remembered session (`session`) goes with it when it is that account's;
/// the session of another remembered account stays. The caller holds the [`RefreshLock`], so
/// a refresh cannot write the session back in between (see `store_rotated`).
/// When the shortened list cannot be written, the whole list entry is deleted instead: the
/// other accounts then sign in again, but the account that signed out does not stay.
/// Returns whether nothing of the account is left in the store.
fn forget_account_in(b: &dyn crate::secrets::Backend, profile: &str, names: &[&str], server: &str) -> bool {
    let all = accounts_in(b, profile);
    let rest = without_account(all.clone(), names, server);
    if rest.len() != all.len() {
        if rest.is_empty() {
            crate::secrets::forget(b, profile, "accounts");
        } else {
            let text = Value::Array(rest).to_string();
            // a busy keyring is tried once more
            let saved = crate::secrets::save(b, profile, "accounts", &text) || crate::secrets::save(b, profile, "accounts", &text);
            if !saved { crate::secrets::forget(b, profile, "accounts"); }
        }
    }
    if remembered_in(b, profile).is_some_and(|s| is_account(&s, names, server)) {
        crate::secrets::forget(b, profile, "session");
    }
    !accounts_in(b, profile).iter().any(|a| is_account(a, names, server))
        && !remembered_in(b, profile).is_some_and(|s| is_account(&s, names, server))
}

/// Sign-out of an account: see [`forget_account_in`]; one lock acquisition covers the list and
/// the session (the lock is not reentrant). False when something of the account is still in
/// the keychain.
pub fn forget_account(names: &[&str], server: &str) -> bool {
    let _lock = RefreshLock::acquire();
    forget_account_in(crate::secrets::store(), crate::profile::current(), names, server)
}

// ---- what the removed app lock left in the store ----

/// The field the app lock (removed in 0.7) put on remembered sign-ins and kept keys.
const RETIRED_LOCK_MARKER: &str = "appLock";
/// The secrets that carried it besides the session and the account list.
const RETIRED_MARKED_KEYS: [&str; 2] = ["stalwart-admin-key", "ai-api-key"];
/// The app lock's own entry: the PIN's salted hash, the count of wrong tries and its options.
pub const RETIRED_LOCK_ENTRY: &str = "applock";

/// Take away what the removed app lock kept in the profile's secret store: its own entry, and
/// the marker it put on the session, the account list and the kept keys. Everything else in
/// those entries stays as it is; an entry without a marker is not written. A user who had the
/// lock on simply gets no lock. False when an entry that needed it could not be written (the
/// next start tries again; a marker left behind is ignored meanwhile).
pub fn retire_app_lock_in(b: &dyn crate::secrets::Backend, profile: &str) -> bool {
    if b.read(&crate::secrets::target(profile, RETIRED_LOCK_ENTRY)).is_some() { crate::secrets::forget(b, profile, RETIRED_LOCK_ENTRY); }
    let strip = |v: &mut Value| v.as_object_mut().is_some_and(|o| o.remove(RETIRED_LOCK_MARKER).is_some());
    let mut ok = true;
    let mut all = accounts_in(b, profile);
    if all.iter_mut().map(strip).fold(false, |x, y| x | y) { ok &= put_in(b, profile, "accounts", &Value::Array(all).to_string()); }
    if let Some(mut s) = remembered_in(b, profile) {
        if strip(&mut s) { ok &= put_in(b, profile, "session", &s.to_string()); }
    }
    for name in RETIRED_MARKED_KEYS {
        let entry = crate::secrets::load(b, profile, name).and_then(|s| serde_json::from_str::<Value>(&s).ok()).filter(|v| v.is_object());
        if let Some(mut v) = entry {
            if strip(&mut v) { ok &= put_in(b, profile, name, &v.to_string()); }
        }
    }
    ok
}

/// [`retire_app_lock_in`] on the real store, under the refresh lock.
pub fn retire_app_lock() -> bool {
    let _lock = RefreshLock::acquire();
    retire_app_lock_in(crate::secrets::store(), crate::profile::current())
}

/// The stored session, told apart: nothing stored, something stored that cannot be read, or the
/// session.
#[derive(Clone, Debug, PartialEq)]
pub enum Remembered {
    Missing,
    Unreadable,
    Some(Value),
}

fn remembered_status_in(b: &dyn crate::secrets::Backend, profile: &str) -> (Remembered, crate::secrets::Source) {
    let (text, source) = crate::secrets::load_with_source(b, profile, "session");
    let status = match text {
        None => Remembered::Missing,
        Some(t) => match serde_json::from_str::<Value>(&t) {
            Ok(v) if v.is_object() => Remembered::Some(v),
            _ => Remembered::Unreadable,
        },
    };
    (status, source)
}

/// The stored session of this profile, "missing" told apart from "unreadable".
pub fn remembered_status() -> Remembered { remembered_status_with_source().0 }

/// [`remembered_status`], and where the entry came from (for the start-up diagnostic line).
pub fn remembered_status_with_source() -> (Remembered, crate::secrets::Source) {
    remembered_status_in(crate::secrets::store(), crate::profile::current())
}

pub fn remembered() -> Option<Value> {
    match remembered_status() { Remembered::Some(v) => Some(v), _ => None }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn tried(v: &[&str]) -> Vec<String> { v.iter().map(|s| s.to_string()).collect() }

    #[test]
    fn rotated_elsewhere_retries_with_stored() {
        assert_eq!(next_token(&tried(&["old"]), "old", Some("new"), true), Next::Retry("new".into()));
    }

    #[test]
    fn unchanged_rejection_needs_sign_in() {
        assert_eq!(next_token(&tried(&["old"]), "old", Some("old"), true), Next::NeedsSignIn);
        assert_eq!(next_token(&tried(&["old"]), "old", None, true), Next::NeedsSignIn);
        assert_eq!(next_token(&tried(&["old"]), "old", Some(""), true), Next::NeedsSignIn);
    }

    #[test]
    fn stale_stored_token_falls_back_to_our_own() {
        // our last rotation could not be written: the keyring still has the dead token
        assert_eq!(next_token(&tried(&["dead"]), "mine", Some("dead"), true), Next::Retry("mine".into()));
        assert_eq!(next_token(&tried(&["dead"]), "mine", None, true), Next::Retry("mine".into()));
    }

    #[test]
    fn every_token_is_tried_once_then_the_user_signs_in() {
        // stored, then ours, then nothing: a rejected sign-in is never retried in a loop
        let mut t = tried(&["stored"]);
        assert_eq!(next_token(&t, "mine", Some("stored"), true), Next::Retry("mine".into()));
        t.push("mine".into());
        assert_eq!(next_token(&t, "mine", Some("stored"), true), Next::NeedsSignIn);
        // a third token appearing meanwhile (a third instance) is tried too, and then it ends
        assert_eq!(next_token(&t, "mine", Some("third"), true), Next::Retry("third".into()));
        t.push("third".into());
        assert_eq!(next_token(&t, "mine", Some("third"), true), Next::NeedsSignIn);
    }

    #[test]
    fn network_error_keeps_and_retries_later() {
        assert_eq!(next_token(&tried(&["old"]), "old", Some("new"), false), Next::Transient);
        assert_eq!(next_token(&tried(&["old"]), "old", Some("old"), false), Next::Transient);
        assert!(!is_rejection("error sending request"));
        assert!(is_rejection("Token request failed (400 Bad Request): invalid_grant "));
        assert!(!is_rejection("Token request failed (503 Service Unavailable):  "));
        assert!(!is_rejection("error sending request: timed out"));
    }

    fn acct(user: &str, endpoint: &str, refresh: &str) -> Value {
        json!({"kind": "oauth", "server": "https://mail.example.test", "user": user, "refresh": refresh, "endpoint": endpoint, "client": "barbican"})
    }

    #[test]
    fn stored_refresh_is_found_by_user_and_endpoint() {
        let all = vec![acct("a@example.test", "https://one.example.test/token", "r1"), acct("a@example.test", "https://two.example.test/token", "r2"),
            json!({"kind": "password", "user": "p@example.test", "pass": "x"})];
        assert_eq!(find_refresh(&all, "a@example.test", "https://two.example.test/token").as_deref(), Some("r2"));
        assert_eq!(find_refresh(&all, "a@example.test", "https://one.example.test/token").as_deref(), Some("r1"));
        // an unknown endpoint (an entry written before it was kept) matches by user
        assert_eq!(find_refresh(&all, "a@example.test", "").as_deref(), Some("r1"));
        assert_eq!(find_refresh(&all, "nobody@example.test", ""), None);
        assert_eq!(find_refresh(&all, "", "https://one.example.test/token"), None);
        // a password account has no refresh token to adopt
        assert_eq!(find_refresh(&all, "p@example.test", ""), None);
    }

    #[test]
    fn a_rotation_only_touches_its_own_account() {
        let mut all = vec![acct("a@example.test", "https://one.example.test/token", "r1"), acct("a@example.test", "https://two.example.test/token", "r2"),
            acct("b@example.test", "https://one.example.test/token", "r3")];
        assert!(rotate_in(&mut all, "a@example.test", "https://two.example.test/token", "r2", "r2b"));
        assert_eq!(all[0]["refresh"], "r1");
        assert_eq!(all[1]["refresh"], "r2b");
        assert_eq!(all[2]["refresh"], "r3");
        // nothing changes when it is stored already
        assert!(!rotate_in(&mut all, "a@example.test", "https://two.example.test/token", "r2", "r2b"));
        assert!(!rotate_in(&mut all, "c@example.test", "https://two.example.test/token", "r1", "x"));
    }

    #[test]
    fn two_instances_share_one_rotation_chain() {
        // A refreshes r1 -> r2 and stores it; B (still holding r1) then adopts r2 and gets r3
        let mut stored = vec![acct("a@example.test", "https://one.example.test/token", "r1")];
        let ep = "https://one.example.test/token";
        assert!(rotate_in(&mut stored, "a@example.test", ep, "r1", "r2"));
        assert_eq!(find_refresh(&stored, "a@example.test", ep).as_deref(), Some("r2"));
        assert!(rotate_in(&mut stored, "a@example.test", ep, "r2", "r3"));
        assert_eq!(find_refresh(&stored, "a@example.test", ep).as_deref(), Some("r3"));
    }

    #[test]
    fn remembering_keeps_the_other_entries_as_stored() {
        // B reads the list after A stored X:r2, so X keeps r2 while Y is added in front
        let ep = "https://one.example.test/token";
        let all = vec![acct("x@example.test", ep, "r2")];
        let y = acct("y@example.test", ep, "y1");
        let (out, _) = merge_account(all, &y, &Origin::SignIn, None, &[]);
        assert_eq!(out.len(), 2);
        assert_eq!(out[0]["user"], "y@example.test");
        assert_eq!(out[1]["refresh"], "r2");
    }

    const EP: &str = "https://one.example.test/token";

    #[test]
    fn a_restored_session_keeps_its_own_token_and_never_takes_the_lists() {
        // the 0.1.0 shape: the list holds the token of the first sign-in, long dead; the restored
        // session just refreshed r_cur into r_new
        let stale = vec![acct("x@example.test", EP, "stale")];
        let mine = acct("x@example.test", EP, "r_new");
        let session = acct("", EP, "r_cur");
        let (out, v) = merge_account(stale, &mine, &Origin::Restore { read: Some("r_cur".into()) }, Some(&session), &[]);
        assert_eq!(v["refresh"], "r_new");
        assert_eq!(out.len(), 1);
        assert_eq!(out[0]["refresh"], "r_new");
    }

    #[test]
    fn a_restored_session_adopts_only_what_another_instance_stored_since_it_read() {
        let stale = vec![acct("x@example.test", EP, "stale")];
        let mine = acct("x@example.test", EP, "r1");
        let restore = Origin::Restore { read: Some("r1".into()) };
        // the session entry still holds what was read: ours stays
        let same = acct("x@example.test", EP, "r1");
        assert_eq!(merge_account(stale.clone(), &mine, &restore, Some(&same), &[]).1["refresh"], "r1");
        // another instance stored r2 meanwhile: adopt it, in the session and in the list
        let newer = acct("x@example.test", EP, "r2");
        let (out, v) = merge_account(stale.clone(), &mine, &restore, Some(&newer), &[]);
        assert_eq!((v["refresh"].as_str(), out[0]["refresh"].as_str()), (Some("r2"), Some("r2")));
        // a session of another endpoint, another account, or a password session is none of ours
        assert_eq!(merge_account(stale.clone(), &mine, &restore, Some(&acct("x@example.test", "https://two.example.test/token", "r9")), &[]).1["refresh"], "r1");
        assert_eq!(merge_account(stale.clone(), &mine, &restore, Some(&acct("y@example.test", EP, "r9")), &[]).1["refresh"], "r1");
        assert_eq!(merge_account(stale.clone(), &mine, &restore, Some(&json!({"kind": "password", "user": "x@example.test", "pass": "p"})), &[]).1["refresh"], "r1");
        // a session without a user yet (0.1.0) is the account's
        assert_eq!(merge_account(stale.clone(), &mine, &restore, Some(&acct("", EP, "r2")), &[]).1["refresh"], "r2");
        // a new sign-in always replaces what is stored
        let (out, v) = merge_account(stale, &mine, &Origin::SignIn, Some(&newer), &[]);
        assert_eq!((v["refresh"].as_str(), out[0]["refresh"].as_str()), (Some("r1"), Some("r1")));
    }

    #[test]
    fn an_empty_user_is_not_added_to_the_account_list() {
        let (out, v) = merge_account(vec![], &acct("", "https://one.example.test/token", "r1"), &Origin::SignIn, None, &[]);
        assert!(out.is_empty());
        assert_eq!(v["refresh"], "r1");
    }

    use crate::secrets::testkit::Fake;

    fn signed_in(b: &Fake, session_user: &str) {
        let (ann, bob) = (acct("ann@example.test", "https://e.example.test/t", "r1"), acct("bob@example.test", "https://e.example.test/t", "r2"));
        let mut ann = ann; ann["server"] = json!("https://mail.example.test");
        let mut bob = bob; bob["server"] = json!("https://mail.example.test");
        let session = if session_user == "ann@example.test" { ann.clone() } else { bob.clone() };
        assert!(crate::secrets::save(b, "p", "accounts", &json!([ann, bob]).to_string()));
        assert!(crate::secrets::save(b, "p", "session", &session.to_string()));
    }

    const ANN: &[&str] = &["ann@example.test"];
    const SERVER: &str = "https://mail.example.test";

    #[test]
    fn signing_out_takes_the_session_of_that_account_and_keeps_the_others() {
        let b = Fake::default();
        signed_in(&b, "ann@example.test");
        assert!(forget_account_in(&b, "p", ANN, SERVER));
        assert!(remembered_in(&b, "p").is_none(), "ann's session is gone");
        let left = accounts_in(&b, "p");
        assert_eq!(left.len(), 1);
        assert_eq!(left[0]["user"], "bob@example.test");
        // the session of another remembered account stays when it is not the one signing out
        let b = Fake::default();
        signed_in(&b, "bob@example.test");
        assert!(forget_account_in(&b, "p", ANN, SERVER));
        assert_eq!(remembered_in(&b, "p").unwrap()["user"], "bob@example.test");
        assert_eq!(accounts_in(&b, "p").len(), 1);
        // the last account takes both entries
        assert!(forget_account_in(&b, "p", &["bob@example.test"], SERVER));
        assert!(remembered_in(&b, "p").is_none() && accounts_in(&b, "p").is_empty());
        assert!(b.new.borrow().is_empty());
    }

    #[test]
    fn a_rotation_after_sign_out_cannot_bring_the_account_back() {
        // forget_account_in runs under the refresh lock, as store_rotated does: whichever comes
        // second finds nothing to restore
        let b = Fake::default();
        signed_in(&b, "ann@example.test");
        assert!(forget_account_in(&b, "p", ANN, SERVER));
        let mut all = accounts_in(&b, "p");
        assert!(!rotate_in(&mut all, "ann@example.test", "https://e.example.test/t", "r1", "r9"));
        assert!(remembered_in(&b, "p").is_none());
        assert!(accounts_in(&b, "p").iter().all(|a| a["user"] != "ann@example.test"));
    }

    #[test]
    fn a_list_that_cannot_be_shortened_is_deleted_rather_than_kept() {
        let b = Fake::default();
        signed_in(&b, "ann@example.test");
        b.fail_writes.set(true);
        // the shortened list cannot be written: the whole entry goes, ann does not stay
        assert!(forget_account_in(&b, "p", ANN, SERVER));
        assert!(accounts_in(&b, "p").is_empty());
        assert!(remembered_in(&b, "p").is_none());
    }

    #[test]
    fn the_same_name_on_another_server_keeps_its_session() {
        let b = Fake::default();
        signed_in(&b, "ann@example.test");
        // signing out ann of a different server leaves this ann's entries alone
        assert!(forget_account_in(&b, "p", ANN, "https://other.example.test"));
        assert_eq!(accounts_in(&b, "p").len(), 2);
        assert_eq!(remembered_in(&b, "p").unwrap()["user"], "ann@example.test");
    }

    #[test]
    fn the_refresh_lock_excludes_a_second_holder_until_released() {
        let dir = std::env::temp_dir().join(format!("barbican-lock-test-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let open = || std::fs::OpenOptions::new().write(true).create(true).truncate(false).open(dir.join("l.lock")).unwrap();
        let (a, b) = (open(), open());
        assert!(a.try_lock().is_ok());
        assert!(b.try_lock().is_err());
        a.unlock().unwrap();
        assert!(b.try_lock().is_ok());
        let _ = std::fs::remove_dir_all(&dir);
    }

    // ---- the 0.1.0 -> 0.1.1 upgrade, against a fake secret store and a fake token endpoint ----

    use std::cell::{Cell, RefCell};
    use std::collections::HashSet;

    /// A token endpoint: a refresh token in `dead` is refused with invalid_grant; a rotating one
    /// kills the token it was given and issues a new one, a stateless one issues none.
    struct FakeServer { dead: RefCell<HashSet<String>>, rotate: bool, issued: Cell<u32> }
    impl FakeServer {
        fn new(rotate: bool, dead: &[&str]) -> FakeServer {
            FakeServer { dead: RefCell::new(dead.iter().map(|s| s.to_string()).collect()), rotate, issued: Cell::new(0) }
        }
    }
    impl TokenServer for FakeServer {
        fn refresh(&self, _: &str, _: &str, rt: &str) -> Result<(String, Option<String>, u64), String> {
            if self.dead.borrow().contains(rt) { return Err("Token request failed (400 Bad Request): invalid_grant ".into()); }
            if !self.rotate { return Ok(("access".into(), None, 3600)); }
            self.issued.set(self.issued.get() + 1);
            self.dead.borrow_mut().insert(rt.to_string());
            Ok(("access".into(), Some(format!("r_new{}", self.issued.get())), 3600))
        }
    }

    const P: &str = "default";
    const HOST: &str = "https://mail.example.test";
    const U: &str = "x@example.test";

    fn legacy_entry(user: &str, server: &str, refresh: &str) -> Value {
        json!({"kind": "oauth", "server": server, "user": user, "refresh": refresh, "endpoint": EP, "client": "c"})
    }

    /// What 0.1.0 left after an SSO sign-in and a quit: the session has a blank user and the
    /// newest token, the list the typed login and raw address with the first token. Everything
    /// is under the names of 0.1.0.
    fn legacy_store(typed: Option<&str>) -> Fake {
        let b = Fake::default();
        {
            let mut old = b.old.borrow_mut();
            old.insert("session".into(), legacy_entry("", HOST, "r_cur").to_string());
            if let Some(t) = typed { old.insert("accounts".into(), json!([legacy_entry(t, "mail.example.test", "r_orig")]).to_string()); }
            old.insert("stalwart-admin-key".into(), "admin-secret".into());
            old.insert("ai-api-key".into(), "ai-secret".into());
        }
        b
    }

    /// One start of the app on a stored session, up to the moment the window shows (restore_session
    /// and finish_login): refresh, then remember. The process ends there, before any renew.
    /// Returns the refresh token the process holds.
    fn start(b: &Fake, srv: &FakeServer, username: &str) -> Result<String, String> {
        let session = match remembered_status_in(b, P).0 { Remembered::Some(v) => v, other => panic!("no session: {other:?}") };
        let read = session["refresh"].as_str().map(String::from);
        let t = Tokens {
            access: String::new(), refresh: read.clone(), expires: None,
            token_endpoint: session["endpoint"].as_str().unwrap().into(), client_id: session["client"].as_str().unwrap().into(),
            user: session["user"].as_str().unwrap().into(),
        };
        let mut n = refresh_in(b, P, srv, &t)?;
        // finish_login: a blank user becomes the server's own name for the login
        let user = if t.user.is_empty() { username.to_string() } else { t.user.clone() };
        n.user = user.clone();
        let mut v = json!({"kind": "oauth", "server": session["server"], "user": user});
        v["refresh"] = json!(n.refresh);
        v["endpoint"] = json!(n.token_endpoint);
        v["client"] = json!(n.client_id);
        remember_in(b, P, &v, &Origin::Restore { read });
        Ok(n.refresh.unwrap())
    }

    fn assert_upgraded(b: &Fake, srv: &FakeServer, held: &str) {
        let session = remembered_in(b, P).unwrap();
        assert_eq!(session["user"], U);
        assert_eq!(session["refresh"], held, "the session holds the token the process holds");
        assert!(!srv.dead.borrow().contains(held), "and the server still accepts it");
        let all = accounts_in(b, P);
        let mine: Vec<_> = all.iter().filter(|a| is_account(a, &[U], HOST)).collect();
        assert_eq!(all.len(), 1, "one entry for one login: {all:?}");
        assert_eq!(mine.len(), 1);
        assert_eq!(mine[0]["refresh"], held, "carrying the live token");
        assert!(b.old.borrow().get("session").is_none() && b.old.borrow().get("accounts").is_none(), "0.1.0 entries are gone");
    }

    fn upgrade_survives_restarts(rotate: bool, typed: Option<&str>) {
        let b = legacy_store(typed);
        // r_orig is long dead; r_cur is the live token
        let srv = FakeServer::new(rotate, &["r_orig"]);
        let mut held = String::new();
        for run in 1..=4 {
            held = start(&b, &srv, U).unwrap_or_else(|e| panic!("start {run} (rotate {rotate}, typed {typed:?}): {e}"));
            assert_upgraded(&b, &srv, &held);
        }
        // the other secrets came across untouched
        assert_eq!(crate::secrets::load(&b, P, "stalwart-admin-key").as_deref(), Some("admin-secret"));
        assert_eq!(crate::secrets::load(&b, P, "ai-api-key").as_deref(), Some("ai-secret"));
        assert!(b.old.borrow().is_empty(), "nothing is left under the 0.1.0 names: {:?}", b.old.borrow().keys().collect::<Vec<_>>());
        if rotate { assert_eq!(held, "r_new4"); } else { assert_eq!(held, "r_cur"); }
    }

    #[test]
    fn a_0_1_0_sso_session_survives_any_number_of_restarts() {
        for rotate in [true, false] {
            upgrade_survives_restarts(rotate, Some(U));
        }
    }

    #[test]
    fn the_same_when_the_typed_login_differs_in_case() {
        for rotate in [true, false] {
            upgrade_survives_restarts(rotate, Some("X@Example.test"));
        }
    }

    #[test]
    fn the_same_when_no_login_was_typed() {
        for rotate in [true, false] {
            upgrade_survives_restarts(rotate, None);
        }
    }

    #[test]
    fn a_restore_never_takes_the_stale_token_of_the_list() {
        // what 0.1.1 did: the list's token replaced the one that had just worked
        let stale = vec![legacy_entry(U, "mail.example.test", "r_orig")];
        let mine = json!({"kind": "oauth", "server": HOST, "user": U, "refresh": "r_new1", "endpoint": EP, "client": "c"});
        let (out, v) = merge_account(stale, &mine, &Origin::Restore { read: Some("r_cur".into()) }, None, &[]);
        assert_eq!(v["refresh"], "r_new1");
        assert_eq!(out.len(), 1);
        assert_eq!(out[0]["refresh"], "r_new1");
    }

    #[test]
    fn a_rotation_while_restoring_a_blank_user_session_is_stored_at_once() {
        let b = Fake::default();
        assert!(crate::secrets::save(&b, P, "session", &legacy_entry("", HOST, "r_cur").to_string()));
        // a stale duplicate carrying the same token under another user is healed too; another
        // account of the same endpoint is not touched
        assert!(crate::secrets::save(&b, P, "accounts", &json!([legacy_entry("typed", HOST, "r_cur"), legacy_entry("other@example.test", HOST, "r_other")]).to_string()));
        store_rotated_in(&b, P, "", EP, "r_cur", "r_new");
        assert_eq!(remembered_in(&b, P).unwrap()["refresh"], "r_new");
        let all = accounts_in(&b, P);
        assert_eq!(all[0]["refresh"], "r_new");
        assert_eq!(all[1]["refresh"], "r_other");
        // a token that is not the one just used is left alone
        store_rotated_in(&b, P, "", EP, "something_else", "r_x");
        assert_eq!(remembered_in(&b, P).unwrap()["refresh"], "r_new");
    }

    #[test]
    fn a_refresh_of_a_blank_user_session_keeps_the_store_current() {
        let b = Fake::default();
        assert!(crate::secrets::save(&b, P, "session", &legacy_entry("", HOST, "r_cur").to_string()));
        let srv = FakeServer::new(true, &[]);
        let t = Tokens { access: String::new(), refresh: Some("r_cur".into()), expires: None, token_endpoint: EP.into(), client_id: "c".into(), user: String::new() };
        let n = refresh_in(&b, P, &srv, &t).unwrap();
        assert_eq!(n.refresh.as_deref(), Some("r_new1"));
        assert_eq!(remembered_in(&b, P).unwrap()["refresh"], "r_new1");
    }

    #[test]
    fn two_instances_one_rotation_chain_on_restore() {
        let b = Fake::default();
        let s = legacy_entry(U, HOST, "r1");
        assert!(crate::secrets::save(&b, P, "session", &s.to_string()));
        assert!(crate::secrets::save(&b, P, "accounts", &json!([s]).to_string()));
        // A read r1 at start; B rotated r1 into r2 and stored it
        store_rotated_in(&b, P, U, EP, "r1", "r2");
        // A (which still holds r1) remembers its restore
        let mine = legacy_entry(U, HOST, "r1");
        remember_in(&b, P, &mine, &Origin::Restore { read: Some("r1".into()) });
        assert_eq!(remembered_in(&b, P).unwrap()["refresh"], "r2", "A adopts what B stored, not its own copy");
        assert_eq!(accounts_in(&b, P)[0]["refresh"], "r2");
        // nothing stored meanwhile: A's own token is written, and the list follows the session
        let mine = legacy_entry(U, HOST, "r3");
        remember_in(&b, P, &mine, &Origin::Restore { read: Some("r2".into()) });
        assert_eq!(remembered_in(&b, P).unwrap()["refresh"], "r3");
        assert_eq!(accounts_in(&b, P)[0]["refresh"], "r3");
    }

    #[test]
    fn updating_an_entry_keeps_the_order_and_the_startup_session() {
        let b = Fake::default();
        let bob = json!({"kind": "password", "server": HOST, "user": "bob@example.test", "pass": "b"});
        let ann = json!({"kind": "password", "server": HOST, "user": "ann@example.test", "pass": "old"});
        b.old.borrow_mut().insert("session".into(), bob.to_string());
        b.old.borrow_mut().insert("accounts".into(), json!([bob, ann]).to_string());
        let cleared = json!({"kind": "password", "server": HOST, "user": "ann@example.test", "pass": ""});
        update_account_in(&b, P, &cleared);
        assert_eq!(accounts_in(&b, P), vec![bob.clone(), cleared.clone()]);
        assert_eq!(remembered_in(&b, P).unwrap(), bob, "another account's startup session is not replaced");
        // when the session is this account it follows
        put_in(&b, P, "session", &ann.to_string());
        update_account_in(&b, P, &cleared);
        assert_eq!(remembered_in(&b, P).unwrap(), cleared);
        // an account with no entry writes nothing
        update_account_in(&b, P, &json!({"kind": "password", "server": HOST, "user": "eve@example.test", "pass": "x"}));
        assert_eq!(accounts_in(&b, P).len(), 2);
    }

    #[test]
    fn a_repair_writes_only_over_a_cleared_password_entry() {
        let b = Fake::default();
        let bob = json!({"kind": "password", "server": HOST, "user": "bob@example.test", "pass": "b"});
        let cleared = json!({"kind": "password", "server": HOST, "user": "ann@example.test", "pass": ""});
        let fixed = json!({"kind": "password", "server": HOST, "user": "ann@example.test", "pass": "app-pw"});
        b.old.borrow_mut().insert("session".into(), cleared.to_string());
        b.old.borrow_mut().insert("accounts".into(), json!([bob, cleared]).to_string());
        assert!(repair_account_in(&b, P, &fixed));
        assert_eq!(accounts_in(&b, P), vec![bob.clone(), fixed.clone()]);
        assert_eq!(remembered_in(&b, P).unwrap(), fixed);
        // not cleared any more: a second repair writes nothing
        assert!(!repair_account_in(&b, P, &json!({"kind": "password", "server": HOST, "user": "ann@example.test", "pass": "other"})));
        assert_eq!(accounts_in(&b, P)[1], fixed);
        // signed in again with SSO meanwhile: the oauth entry and session stay
        let sso = json!({"kind": "oauth", "server": HOST, "user": "ann@example.test", "refresh": "r", "endpoint": "e", "client": "c"});
        put_in(&b, P, "accounts", &json!([bob, sso]).to_string());
        put_in(&b, P, "session", &sso.to_string());
        assert!(!repair_account_in(&b, P, &fixed));
        assert_eq!(accounts_in(&b, P), vec![bob.clone(), sso.clone()]);
        assert_eq!(remembered_in(&b, P).unwrap(), sso);
        // another account's startup session is not replaced
        put_in(&b, P, "accounts", &json!([bob, cleared]).to_string());
        put_in(&b, P, "session", &bob.to_string());
        assert!(repair_account_in(&b, P, &fixed));
        assert_eq!(remembered_in(&b, P).unwrap(), bob);
        // no entry: nothing
        assert!(!repair_account_in(&b, P, &json!({"kind": "password", "server": HOST, "user": "eve@example.test", "pass": "x"})));
    }

    #[test]
    fn a_0_1_0_password_session_restores_unchanged() {
        let b = Fake::default();
        let pw = json!({"kind": "password", "server": HOST, "user": "ann@example.test", "pass": "pw"});
        b.old.borrow_mut().insert("session".into(), pw.to_string());
        b.old.borrow_mut().insert("accounts".into(), json!([pw]).to_string());
        let Remembered::Some(v) = remembered_status_in(&b, P).0 else { panic!() };
        assert_eq!(v, pw);
        // merge never touches it: no token appears, no matter what the session entry holds
        let (all, out) = merge_account(accounts_in(&b, P), &v, &Origin::Restore { read: None }, Some(&legacy_entry("ann@example.test", HOST, "r1")), &[]);
        assert_eq!(out, pw);
        assert_eq!(all, vec![pw.clone()]);
        remember_in(&b, P, &v, &Origin::Restore { read: None });
        assert_eq!(remembered_in(&b, P).unwrap(), pw);
        assert_eq!(accounts_in(&b, P), vec![pw]);
    }

    #[test]
    fn the_typed_and_the_servers_name_for_one_login_leave_one_entry() {
        // the 0.1.0 list has the typed address; the restore brings the normalized one
        let all = vec![legacy_entry(U, "mail.example.test", "r_orig"), legacy_entry("other@example.test", "mail.example.test", "r_o")];
        let v = legacy_entry(U, "https://mail.example.test", "r_new");
        let (out, _) = merge_account(all.clone(), &v, &Origin::Restore { read: Some("r_cur".into()) }, None, &[]);
        assert_eq!(out.len(), 2);
        assert_eq!((out[0]["user"].as_str(), out[0]["refresh"].as_str()), (Some(U), Some("r_new")));
        assert_eq!(out[1]["user"], "other@example.test");
        // the same user on another server stays
        let other = legacy_entry(U, "https://other.example.test", "r_x");
        let (out, _) = merge_account(vec![other], &v, &Origin::SignIn, None, &[]);
        assert_eq!(out.len(), 2);
        // a name the form had besides the server's own collapses too
        let (out, _) = merge_account(vec![legacy_entry("typed", "mail.example.test", "old")], &v, &Origin::SignIn, None, &["typed"]);
        assert_eq!(out.len(), 1);
    }

    #[test]
    fn a_saved_session_that_cannot_be_read_is_told_from_none() {
        let b = Fake::default();
        assert_eq!(remembered_status_in(&b, P).0, Remembered::Missing);
        assert!(crate::secrets::save(&b, P, "session", "{not json"));
        assert_eq!(remembered_status_in(&b, P).0, Remembered::Unreadable);
        assert!(crate::secrets::save(&b, P, "session", "[1]"));
        assert_eq!(remembered_status_in(&b, P).0, Remembered::Unreadable);
        assert!(crate::secrets::save(&b, P, "session", "{\"kind\":\"password\"}"));
        assert_eq!(remembered_status_in(&b, P), (Remembered::Some(json!({"kind": "password"})), crate::secrets::Source::New));
        // an entry of 0.1.0 is adopted, and says so
        let b = legacy_store(None);
        assert_eq!(remembered_status_in(&b, P).1, crate::secrets::Source::Legacy);
        assert_eq!(remembered_status_in(&b, P).1, crate::secrets::Source::New);
    }

    #[test]
    fn a_rejection_keeps_everything_stored() {
        let b = legacy_store(Some(U));
        let srv = FakeServer::new(true, &["r_orig", "r_cur"]);
        let err = start(&b, &srv, U).unwrap_err();
        assert!(crate::jmap::auth_rejected(&err), "{err}");
        // nothing was deleted or rewritten: the form shows, the entries wait for a sign-in
        assert_eq!(remembered_in(&b, P).unwrap()["refresh"], "r_cur");
        assert_eq!(accounts_in(&b, P).len(), 1);
    }

    /// The start-up retry loop of restore_session, one attempt after the other against a store
    /// and a server: the first attempt refreshes (the server rotates, the store keeps the new
    /// token) and then fails after the refresh (session discovery timed out); `retry_from_store`
    /// says whether the next attempt starts from the store (the fix) or from the value read first.
    fn attempts(b: &Fake, srv: &FakeServer, retry_from_store: bool) -> Result<(), String> {
        let first = match remembered_status_in(b, P).0 { Remembered::Some(v) => v, other => panic!("{other:?}") };
        let tokens = |v: &Value| Tokens {
            access: String::new(), refresh: v["refresh"].as_str().map(String::from), expires: None,
            token_endpoint: v["endpoint"].as_str().unwrap().into(), client_id: "c".into(), user: v["user"].as_str().unwrap().into(),
        };
        // attempt 0: refreshed fine, then the connection failed
        refresh_in(b, P, srv, &tokens(&first)).expect("the first refresh works");
        // attempt 1
        let v = if retry_from_store { crate::restore::retry_value(&first, &remembered_status_in(b, P).0) } else { first.clone() };
        refresh_in(b, P, srv, &tokens(&v)).map(|_| ())
    }

    #[test]
    fn a_retry_after_a_refresh_that_rotated_uses_the_rotated_token() {
        // a 0.1.0 SSO store (blank user) on a server that rotates its refresh tokens
        let b = legacy_store(Some(U));
        let srv = FakeServer::new(true, &["r_orig"]);
        attempts(&b, &srv, true).expect("the retry presents the token the first refresh stored");
        // the session and the list hold the token of the second refresh
        assert_eq!(remembered_in(&b, P).unwrap()["refresh"], "r_new2");
        // the next start still works
        start(&b, &srv, U).unwrap();
        // the value read first is dead after the first refresh: the old loop ended in the form
        let b = legacy_store(Some(U));
        let srv = FakeServer::new(true, &["r_orig"]);
        let err = attempts(&b, &srv, false).unwrap_err();
        assert!(crate::jmap::auth_rejected(&err), "{err}");
    }

    #[test]
    fn what_the_removed_app_lock_kept_is_taken_away() {
        let b = Fake::default();
        // a store as the app lock left it: its entry, and its marker on every sign-in and kept key
        let ann = json!({"kind": "password", "server": HOST, "user": "ann@example.test", "pass": "pw", "appLock": "f1"});
        assert!(crate::secrets::save(&b, P, "session", &ann.to_string()));
        assert!(crate::secrets::save(&b, P, "accounts", &json!([ann, {"kind": "password", "server": HOST, "user": "bob@example.test", "pass": "pw"}]).to_string()));
        assert!(crate::secrets::save(&b, P, "ai-api-key", &json!({"key": "sk-test", "appLock": "f1"}).to_string()));
        assert!(crate::secrets::save(&b, P, "stalwart-admin-key", &json!({"key": "adm", "base": HOST, "account": "a", "appLock": "f1"}).to_string()));
        assert!(crate::secrets::save(&b, P, RETIRED_LOCK_ENTRY, r#"{"salt":"x","hash":"y","failures":0}"#));
        assert!(retire_app_lock_in(&b, P));
        assert_eq!(crate::secrets::load(&b, P, RETIRED_LOCK_ENTRY), None);
        // the rest of every entry stays as it was
        let s = remembered_in(&b, P).unwrap();
        assert_eq!((s.get("appLock"), s["user"].as_str(), s["pass"].as_str()), (None, Some("ann@example.test"), Some("pw")));
        let all = accounts_in(&b, P);
        assert_eq!(all.len(), 2);
        assert!(all.iter().all(|a| a.get("appLock").is_none()));
        let ai: Value = serde_json::from_str(&crate::secrets::load(&b, P, "ai-api-key").unwrap()).unwrap();
        assert_eq!(ai, json!({"key": "sk-test"}));
        let admin: Value = serde_json::from_str(&crate::secrets::load(&b, P, "stalwart-admin-key").unwrap()).unwrap();
        assert_eq!(admin, json!({"key": "adm", "base": HOST, "account": "a"}));
        // nothing left: nothing is written (a store that refuses writes is never asked)
        b.fail_writes.set(true);
        assert!(retire_app_lock_in(&b, P));
        // an older bare AI key is no JSON object and stays as it is
        b.fail_writes.set(false);
        assert!(crate::secrets::save(&b, P, "ai-api-key", "sk-bare"));
        assert!(retire_app_lock_in(&b, P));
        assert_eq!(crate::secrets::load(&b, P, "ai-api-key").as_deref(), Some("sk-bare"));
        // a marker that cannot be taken away is reported, and taken the next time
        assert!(crate::secrets::save(&b, P, "session", &json!({"kind": "password", "server": HOST, "user": "ann@example.test", "pass": "pw", "appLock": "f1"}).to_string()));
        b.fail_writes.set(true);
        assert!(!retire_app_lock_in(&b, P));
        b.fail_writes.set(false);
        assert!(retire_app_lock_in(&b, P));
        assert!(remembered_in(&b, P).unwrap().get("appLock").is_none());
    }
}
