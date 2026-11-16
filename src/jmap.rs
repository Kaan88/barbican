use base64::Engine;
use serde_json::{json, Value};
use serde_json::Map;
use std::collections::HashMap;
use std::sync::Arc;

pub struct Client {
    http: reqwest::blocking::Client,
    auth: Arc<std::sync::Mutex<String>>,
    oauth: Option<Arc<std::sync::Mutex<crate::oauth::Tokens>>>,
    pub base: String,
    pub account: String,
    pub username: String,
    pub caps: Vec<String>,
    /// The session as first fetched (RFC 8620 §2).
    pub session: Value,
    /// The current session: re-fetched when a response's `sessionState` differs (RFC 8620 §3.4).
    live: std::sync::RwLock<Value>,
    /// maxConcurrentRequests / maxConcurrentUpload (RFC 8620 §2).
    /// Shared with `scoped` clients of the same login.
    requests: Arc<Gate>,
    uploads: Arc<Gate>,
    /// Mailbox role -> id, refreshed by every Mailbox/get of the whole tree.
    roles: std::sync::Mutex<Option<(std::time::Instant, HashMap<String, String>)>>,
}

type R<T> = Result<T, String>;

fn e<E: std::fmt::Display>(x: E) -> String {
    x.to_string()
}

/// A counting semaphore (the server's concurrency limits).
struct Gate { n: std::sync::Mutex<usize>, cv: std::sync::Condvar, max: usize }

struct Permit<'a>(&'a Gate);

impl Gate {
    fn new(max: usize) -> Gate { Gate { n: std::sync::Mutex::new(0), cv: std::sync::Condvar::new(), max: max.max(1) } }
    fn enter(&self) -> Permit<'_> {
        let mut n = self.n.lock().unwrap();
        while *n >= self.max { n = self.cv.wait(n).unwrap(); }
        *n += 1;
        Permit(self)
    }
}

impl Drop for Permit<'_> {
    fn drop(&mut self) {
        *self.0.n.lock().unwrap() -= 1;
        self.0.cv.notify_one();
    }
}

pub fn normalize_base(server: &str) -> String {
    let mut base = server.trim().trim_end_matches('/').to_string();
    if let Some(i) = base.find("/.well-known/jmap") { base.truncate(i); }
    if !base.starts_with("http") {
        base = format!("https://{base}");
    }
    base
}

/// A session URL may be relative to the server (Stalwart answers absolute ones).
fn absolute(base: &str, u: &str) -> String {
    if u.starts_with('/') {
        let origin: String = base.splitn(4, '/').take(3).collect::<Vec<_>>().join("/");
        format!("{origin}{u}")
    } else { u.to_string() }
}

/// Session discovery (RFC 8620 §2.2): GET /.well-known/jmap, following redirects.
fn fetch_session(http: &reqwest::blocking::Client, base: &str, auth: &str) -> Result<(u16, Value), String> {
    let resp = http.get(format!("{base}/.well-known/jmap")).header("Authorization", auth).header("Accept", "application/json").send().map_err(e)?;
    let status = resp.status().as_u16();
    if !resp.status().is_success() { return Ok((status, Value::Null)); }
    Ok((status, resp.json().map_err(e)?))
}

/// With BARBICAN_JMAP_TRACE=<file>, append each request's method names and the response
/// method names (no arguments, no data) for diagnosing sync behaviour.
fn trace(calls: &Value, resp: &Value) {
    let Ok(path) = std::env::var("BARBICAN_JMAP_TRACE") else { return };
    let names = |a: &Value| a.as_array().map(|a| a.iter().filter_map(|c| {
        let n = c[0].as_str()?;
        Some(match c[1]["limit"].as_u64() { Some(l) => format!("{n}(limit={l})"), None => n.to_string() })
    }).collect::<Vec<_>>().join(",")).unwrap_or_default();
    let errs: Vec<String> = resp["methodResponses"].as_array().into_iter().flatten().filter(|r| r[0] == "error").map(|r| r[1]["type"].to_string()).collect();
    let t = chrono::Local::now().format("%H:%M:%S%.3f");
    let line = format!("{t} {} -> {}{}
", names(calls), names(&resp["methodResponses"]), if errs.is_empty() { String::new() } else { format!(" errors={}", errs.join(",")) });
    if let Ok(mut f) = std::fs::OpenOptions::new().create(true).append(true).open(path) { use std::io::Write; let _ = f.write_all(line.as_bytes()); }
}

/// Methods that change nothing and can be sent again safely after a 429/503.
fn read_only(calls: &Value) -> bool {
    calls.as_array().is_some_and(|a| a.iter().all(|c| c[0].as_str().is_some_and(|m|
        ["/get", "/query", "/changes", "/queryChanges", "/parse", "/validate"].iter().any(|s| m.ends_with(s)) || m == "Core/echo")))
}

/// Split method calls into requests of at most `max` calls (maxCallsInRequest). Calls that use a
/// back-reference (`#arg`, RFC 8620 §3.7) or a creation id (`#cid`, §5.3) stay together: both
/// resolve only inside their own request.
pub fn split_calls(calls: &[Value], max: usize) -> Vec<Vec<Value>> {
    let linked = calls.iter().any(|c| c[1].as_object().is_some_and(|o| o.keys().any(|k| k.starts_with('#')))
        || c[1].to_string().contains("\"#"));
    if calls.len() <= max.max(1) || linked { return vec![calls.to_vec()]; }
    calls.chunks(max.max(1)).map(|c| c.to_vec()).collect()
}

/// A request-level error (RFC 8620 §3.6.1, RFC 7807 problem details) in words.
pub fn problem_message(status: u16, v: &Value) -> String {
    let t = v["type"].as_str().unwrap_or("");
    let detail = v["detail"].as_str().unwrap_or("");
    match t.rsplit(':').next().unwrap_or("") {
        "limit" => format!("The request is over the server's {} limit.", v["limit"].as_str().unwrap_or("size")),
        "unknownCapability" => format!("The server does not support a capability this needs{}.", if detail.is_empty() { String::new() } else { format!(" ({detail})") }),
        "notJSON" | "notRequest" => format!("The server could not read the request ({status})."),
        _ => format!("Request failed ({status}){}", if detail.is_empty() { String::new() } else { format!(": {detail}") }),
    }
}

impl Client {
    /// Password (or app password) sign-in. `totp` is appended Stalwart-style as `password$code`.
    pub fn connect(server: &str, user: &str, pass: &str, totp: &str) -> R<Client> {
        let pass = if totp.trim().is_empty() { pass.to_string() } else { format!("{pass}${}", totp.trim()) };
        let auth = format!("Basic {}", base64::engine::general_purpose::STANDARD.encode(format!("{user}:{pass}")));
        Self::open(&normalize_base(server), auth, None, user)
    }

    pub fn connect_oauth(server: &str, tokens: crate::oauth::Tokens) -> R<Client> {
        let mut t = tokens;
        if t.expires.is_some_and(|x| x <= std::time::Instant::now()) || t.access.is_empty() {
            t = crate::oauth::refresh(&t)?;
        }
        Self::open(&normalize_base(server), format!("Bearer {}", t.access), Some(t), "")
    }

    fn open(base: &str, auth: String, oauth: Option<crate::oauth::Tokens>, user: &str) -> R<Client> {
        let http = reqwest::blocking::Client::builder()
            .timeout(std::time::Duration::from_secs(30))
            .build()
            .map_err(e)?;
        let (status, s) = fetch_session(&http, base, &auth)?;
        if status == 401 {
            return Err(if oauth.is_some() { "The server did not accept the sign-in token.".into() } else { "Wrong username, password or 2FA code".into() });
        }
        if s.is_null() {
            return Err(format!("Server returned {status}"));
        }
        s["apiUrl"].as_str().ok_or("No apiUrl in session")?;
        let account = s["primaryAccounts"]["urn:ietf:params:jmap:mail"]
            .as_str()
            .ok_or("Account has no mail capability")?
            .to_string();
        let username = s["username"].as_str().unwrap_or(user).to_string();
        let caps = s["capabilities"].as_object().map(|o| o.keys().cloned().collect()).unwrap_or_default();
        let core = &s["capabilities"]["urn:ietf:params:jmap:core"];
        let gate = |k: &str, d: u64| Arc::new(Gate::new(core[k].as_u64().unwrap_or(d) as usize));
        Ok(Client {
            http, auth: Arc::new(std::sync::Mutex::new(auth)), oauth: oauth.map(|t| Arc::new(std::sync::Mutex::new(t))),
            base: base.to_string(), account, username, caps,
            requests: gate("maxConcurrentRequests", 4), uploads: gate("maxConcurrentUpload", 4),
            live: std::sync::RwLock::new(s.clone()), session: s,
            roles: std::sync::Mutex::new(None),
        })
    }

    /// The same login addressing another JMAP account it can reach (a shared/group mail
    /// account): every method then reads and writes that account. Auth (and OAuth renewal)
    /// is shared with `self`.
    pub fn scoped(&self, account: &str) -> Client {
        Client {
            http: self.http.clone(), auth: self.auth.clone(), oauth: self.oauth.clone(),
            base: self.base.clone(), account: account.to_string(), username: self.username.clone(),
            caps: self.caps.clone(), session: self.session.clone(),
            live: std::sync::RwLock::new(self.live.read().unwrap().clone()),
            requests: self.requests.clone(), uploads: self.uploads.clone(),
            // the role map is per account
            roles: std::sync::Mutex::new(None),
        }
    }

    /// Current OAuth tokens (for remembering the session).
    pub fn tokens(&self) -> Option<crate::oauth::Tokens> {
        self.oauth.as_ref().map(|m| m.lock().unwrap().clone())
    }

    fn renew(&self) -> R<()> {
        let Some(m) = &self.oauth else { return Err("Not an OAuth session".into()) };
        let mut t = m.lock().unwrap();
        *t = crate::oauth::refresh(&t)?;
        *self.auth.lock().unwrap() = format!("Bearer {}", t.access);
        Ok(())
    }

    fn renew_if_due(&self) {
        if let Some(m) = &self.oauth {
            let due = m.lock().unwrap().expires.is_some_and(|x| x <= std::time::Instant::now());
            if due { let _ = self.renew(); }
        }
    }

    fn auth_header(&self) -> String { self.auth.lock().unwrap().clone() }

    pub fn has(&self, cap: &str) -> bool {
        self.caps.iter().any(|c| c == cap)
    }

    /// Re-fetch the session resource (RFC 8620 §3.4: when `sessionState` changes).
    pub fn refresh_session(&self) -> R<()> {
        let (_, s) = fetch_session(&self.http, &self.base, &self.auth_header())?;
        if s["apiUrl"].is_string() { *self.live.write().unwrap() = s; }
        Ok(())
    }

    // ------------------------------------------------------------------
    // Core request (RFC 8620 §3)
    // ------------------------------------------------------------------

    /// One POST to the API endpoint: maxSizeRequest, maxConcurrentRequests, OAuth renewal, one
    /// retry of read-only calls after 429/503, request-level errors, and the sessionState check.
    fn post(&self, using: &Value, calls: &Value) -> R<Vec<Value>> {
        let body = serde_json::to_vec(&json!({ "using": using, "methodCalls": calls })).map_err(e)?;
        let max = self.limit("maxSizeRequest", 10_000_000);
        if body.len() > max {
            return Err(format!("The request is larger than the server accepts ({} KB).", max / 1000));
        }
        self.renew_if_due();
        let api = absolute(&self.base, self.live.read().unwrap()["apiUrl"].as_str().unwrap_or(""));
        let send = || {
            let _p = self.requests.enter();
            self.http.post(&api).header("Authorization", self.auth_header()).header("Content-Type", "application/json")
                .header("Accept", "application/json").body(body.clone()).send().map_err(e)
        };
        let mut resp = send()?;
        if resp.status() == 401 && self.oauth.is_some() {
            self.renew()?;
            resp = send()?;
        }
        if matches!(resp.status().as_u16(), 429 | 503) && read_only(calls) {
            let wait = resp.headers().get("Retry-After").and_then(|v| v.to_str().ok()).and_then(|v| v.trim().parse::<u64>().ok()).unwrap_or(2).min(10);
            std::thread::sleep(std::time::Duration::from_secs(wait));
            resp = send()?;
        }
        match resp.status().as_u16() {
            401 => return Err("Session expired; sign in again.".into()),
            429 => return Err("The server is rate limiting requests; try again shortly.".into()),
            s if s >= 400 => {
                let v: Value = resp.json().unwrap_or(json!({}));
                return Err(problem_message(s, &v));
            }
            _ => {}
        }
        let v: Value = resp.json().map_err(e)?;
        trace(calls, &v);
        let known = self.live.read().unwrap()["state"].as_str().map(String::from);
        if let (Some(now), Some(known)) = (v["sessionState"].as_str(), known) {
            if now != known { let _ = self.refresh_session(); }
        }
        v["methodResponses"].as_array().cloned().ok_or_else(|| "Bad JMAP response".into())
    }

    /// Send method calls; returns the raw `methodResponses`. Method-level
    /// `error` responses are returned to the caller, not raised (see [`call`]).
    /// More calls than maxCallsInRequest go out as several requests when they are independent.
    pub fn request(&self, calls: Value) -> R<Vec<Value>> {
        let using = json!(self.caps);
        let list = calls.as_array().cloned().unwrap_or_default();
        let mut out = vec![];
        for chunk in split_calls(&list, self.max_calls()) {
            out.extend(self.post(&using, &Value::Array(chunk))?);
        }
        Ok(out)
    }

    /// Request with an explicit `using` list (e.g. Stalwart's own `urn:stalwart:jmap` objects).
    pub fn request_using(&self, using: &[&str], calls: Value) -> R<Vec<Value>> {
        self.post(&json!(using), &calls)
    }

    /// Like [`request`] but fails on the first method error.
    pub fn call(&self, calls: Value) -> R<Vec<Value>> {
        let out = self.request(calls)?;
        for r in &out {
            if r[0] == "error" {
                return Err(method_error(&r[1]));
            }
        }
        Ok(out)
    }

    /// The primary account for a capability (RFC 8620 §2 primaryAccounts), else the mail account.
    pub fn account_for(&self, cap: &str) -> String {
        self.session["primaryAccounts"][cap].as_str().map(String::from).unwrap_or_else(|| self.account.clone())
    }

    /// A capability's account-level object (RFC 8620 §2 accountCapabilities), falling back to the
    /// session-level one. RFC 8621 puts the mail and submission limits (maxDelayedSend,
    /// submissionExtensions, maxMailboxesPerEmail, ...) at the account level.
    pub fn account_cap(&self, cap: &str) -> Value {
        let s = self.live.read().unwrap();
        let acc = self.account_for(cap);
        let v = &s["accounts"][&acc]["accountCapabilities"][cap];
        if v.is_object() { v.clone() } else { s["capabilities"][cap].clone() }
    }

    fn limit(&self, name: &str, default: u64) -> usize {
        self.live.read().unwrap()["capabilities"]["urn:ietf:params:jmap:core"][name].as_u64().unwrap_or(default) as usize
    }

    pub fn max_get(&self) -> usize { self.limit("maxObjectsInGet", 500).max(1) }
    pub fn max_set(&self) -> usize { self.limit("maxObjectsInSet", 500).max(1) }
    pub fn max_calls(&self) -> usize { self.limit("maxCallsInRequest", 16).max(1) }
    pub fn max_upload(&self) -> usize { self.limit("maxSizeUpload", 50_000_000) }

    fn url_template(&self, key: &str) -> Option<String> {
        let u = self.live.read().unwrap()[key].as_str()?.to_string();
        Some(absolute(&self.base, &u))
    }

    /// Whether the server is Stalwart (its `urn:stalwart:jmap` capability).
    pub fn is_stalwart(&self) -> bool { self.has("urn:stalwart:jmap") }

    /// Who owns cached sync states: server, user and account. Account ids alone repeat across
    /// servers (Stalwart's are short sequential ids), and a state must never cross accounts.
    pub fn state_key(&self) -> String { format!("{}
{}
{}", self.base, self.username, self.account) }

    /// `X/set` with `update`, split by maxObjectsInSet; returns (updated, failed descriptions).
    pub fn update(&self, method: &str, account: &str, patches: Vec<(String, Value)>) -> R<(usize, Vec<String>)> {
        let mut ok = 0;
        let mut bad = vec![];
        for chunk in patches.chunks(self.max_set()) {
            let upd: Map<String, Value> = chunk.iter().cloned().collect();
            let r = self.call(json!([[method, {"accountId": account, "update": upd}, "u"]]))?;
            ok += r[0][1]["updated"].as_object().map(|o| o.len()).unwrap_or(0);
            if let Some(nu) = r[0][1]["notUpdated"].as_object() {
                bad.extend(nu.iter().map(|(id, err)| format!("{id}: {}", set_error(err))));
            }
        }
        Ok((ok, bad))
    }

    /// Every id a `X/query` matches, paged by position (the server may cap `limit`, RFC 8620 §5.5).
    pub fn query_all(&self, method: &str, account: &str, args: Value) -> R<Vec<Value>> {
        let mut ids: Vec<Value> = vec![];
        loop {
            let mut a = args.clone();
            a["accountId"] = json!(account);
            a["position"] = json!(ids.len());
            a["calculateTotal"] = json!(true);
            if a.get("limit").is_none() { a["limit"] = json!(5000); }
            let r = self.call(json!([[method, a, "q"]]))?;
            let page = r[0][1]["ids"].as_array().cloned().unwrap_or_default();
            let total = r[0][1]["total"].as_u64().map(|t| t as usize);
            if page.is_empty() { break; }
            ids.extend(page);
            if total.is_none_or(|t| ids.len() >= t) || ids.len() > 1_000_000 { break; }
        }
        Ok(ids)
    }

    /// `X/get` for many ids: chunks of maxObjectsInGet, several chunks per request (maxCallsInRequest).
    pub fn get_all(&self, method: &str, account: &str, ids: &[Value], properties: Option<&Value>) -> R<Vec<Value>> {
        let chunks: Vec<&[Value]> = ids.chunks(self.max_get()).collect();
        let mut out = vec![];
        for group in chunks.chunks(self.max_calls()) {
            let calls: Vec<Value> = group.iter().enumerate().map(|(i, ch)| {
                let mut a = json!({"accountId": account, "ids": ch});
                if let Some(p) = properties { a["properties"] = p.clone(); }
                json!([method, a, format!("g{i}")])
            }).collect();
            for r in self.call(Value::Array(calls))? {
                out.extend(r[1]["list"].as_array().cloned().unwrap_or_default());
            }
        }
        Ok(out)
    }

    /// `X/changes` (RFC 8620 §5.2). `Ok(None)` when the server can't compute a delta from `since`
    /// (cannotCalculateChanges, any method error, or a Stalwart state that went backwards):
    /// the caller then re-fetches in full.
    pub fn changes(&self, ty: &str, account: &str, since: &str, max: Option<usize>) -> R<Option<Changes>> {
        let mut a = json!({"accountId": account, "sinceState": since});
        if let Some(m) = max { a["maxChanges"] = json!(m); }
        let r = self.request(json!([[format!("{ty}/changes"), a, "c"]]))?;
        Ok(r.first().and_then(|x| parse_changes(x, since, self.is_stalwart())))
    }

    // ------------------------------------------------------------------
    // Mailboxes (RFC 8621 §2)
    // ------------------------------------------------------------------

    pub fn mailboxes(&self) -> R<Vec<Value>> {
        Ok(self.mailboxes_with_state()?.0)
    }

    /// The whole folder tree and its Mailbox `state` (RFC 8620 §5.1), for a later Mailbox/changes.
    pub fn mailboxes_with_state(&self) -> R<(Vec<Value>, Option<String>)> {
        let r = self.call(json!([["Mailbox/get", {"accountId": self.account, "ids": null, "properties": MAILBOX_PROPERTIES}, "0"]]))?;
        let mut list = r[0][1]["list"].as_array().cloned().unwrap_or_default();
        let mut state = r[0][1]["state"].as_str().map(String::from);
        // Stalwart may cut ids:null at maxObjectsInGet: page through Mailbox/query then.
        if list.len() >= self.max_get() {
            // the state first: a delta from it can only repeat, never miss, what the pages read
            let r = self.call(json!([["Mailbox/get", {"accountId": self.account, "ids": [], "properties": ["id"]}, "s"]]))?;
            state = r[0][1]["state"].as_str().map(String::from);
            let ids = self.query_all("Mailbox/query", &self.account, json!({}))?;
            list = self.get_all("Mailbox/get", &self.account, &ids, Some(&json!(MAILBOX_PROPERTIES)))?;
        }
        self.remember_roles(&list);
        Ok((list, state))
    }

    fn remember_roles(&self, list: &[Value]) {
        let roles = list.iter().filter_map(|m| Some((m["role"].as_str()?.to_string(), m["id"].as_str()?.to_string()))).collect();
        *self.roles.lock().unwrap() = Some((std::time::Instant::now(), roles));
    }

    /// Role -> mailbox id (inbox, drafts, sent, ...). Cached for a few minutes: every send, move
    /// and push used to fetch the whole folder tree just for this.
    pub fn role_ids(&self) -> R<HashMap<String, String>> {
        if let Some((at, r)) = self.roles.lock().unwrap().as_ref() {
            if at.elapsed() < std::time::Duration::from_secs(300) { return Ok(r.clone()); }
        }
        let list = self.mailboxes()?;
        Ok(list.iter().filter_map(|m| Some((m["role"].as_str()?.to_string(), m["id"].as_str()?.to_string()))).collect())
    }

    /// Bring a folder list up to date with Mailbox/changes (RFC 8621 §2.2) instead of re-reading
    /// the tree. `Ok(None)`: the server can't give a delta from `since`; re-fetch in full.
    /// When only counts changed (`updatedProperties`), only those properties are fetched.
    pub fn mailbox_delta(&self, since: &str, have: &[Value]) -> R<Option<(Vec<Value>, String)>> {
        let (mut created, mut updated, mut destroyed) = (vec![], vec![], vec![]);
        let mut props: Option<Vec<String>> = Some(vec![]);
        let mut state = since.to_string();
        for _ in 0..20 {
            let Some(ch) = self.changes("Mailbox", &self.account, &state, None)? else { return Ok(None) };
            created.extend(ch.created);
            updated.extend(ch.updated);
            destroyed.extend(ch.destroyed);
            props = match (props, ch.updated_properties) {
                (Some(mut a), Some(b)) => { a.extend(b); Some(a) }
                _ => None,
            };
            state = ch.new_state;
            if !ch.has_more {
                let gone: std::collections::HashSet<&String> = destroyed.iter().collect();
                let fetch_full: Vec<Value> = created.iter().filter(|i| !gone.contains(i)).map(|i| json!(i)).collect();
                let partial = props.as_ref().filter(|p| !p.is_empty() && p.iter().all(|x| MAILBOX_PROPERTIES.contains(&x.as_str())));
                let upd: Vec<&String> = updated.iter().filter(|i| !gone.contains(i) && !created.contains(i)).collect();
                // a counts-only fetch is merged into a cached folder; one the cache lacks is
                // fetched whole, or it would join the tree with no name, parent or role
                let (fetch_upd, missing) = split_known(&upd, have, partial.is_some());
                let fetch_full: Vec<Value> = fetch_full.into_iter().chain(missing).collect();
                let mut fetched = self.get_all("Mailbox/get", &self.account, &fetch_full, Some(&json!(MAILBOX_PROPERTIES)))?;
                let upd_props = match partial {
                    Some(p) => { let mut p = p.clone(); p.push("id".into()); json!(p) }
                    None => json!(MAILBOX_PROPERTIES),
                };
                fetched.extend(self.get_all("Mailbox/get", &self.account, &fetch_upd, Some(&upd_props))?);
                let list = apply_object_delta(have, &destroyed, &fetched);
                self.remember_roles(&list);
                return Ok(Some((list, state)));
            }
        }
        Ok(None)
    }

    pub fn create_mailbox(&self, name: &str, parent: Option<&str>) -> R<String> {
        let r = self.call(json!([["Mailbox/set", {"accountId": self.account, "create": {"n": {"name": name, "parentId": parent, "isSubscribed": true}}}, "m"]]))?;
        created_id(&r[0][1], "n")
    }

    pub fn rename_mailbox(&self, id: &str, name: &str) -> R<()> {
        let (_, bad) = self.update("Mailbox/set", &self.account, vec![(id.into(), json!({"name": name}))])?;
        if bad.is_empty() { Ok(()) } else { Err(bad.join("; ")) }
    }

    pub fn delete_mailbox(&self, id: &str) -> R<()> {
        let r = self.call(json!([["Mailbox/set", {"accountId": self.account, "destroy": [id], "onDestroyRemoveEmails": false}, "d"]]))?;
        self.roles.lock().unwrap().take();
        match r[0][1]["notDestroyed"][id].as_object() {
            Some(err) => Err(set_error(&Value::Object(err.clone()))),
            None => Ok(()),
        }
    }

    /// Empty a folder (Trash / Junk): destroys every message in it.
    pub fn empty_mailbox(&self, id: &str) -> R<usize> {
        let mut n = 0;
        loop {
            let q = self.call(json!([["Email/query", {"accountId": self.account, "filter": {"inMailbox": id}, "limit": self.max_set()}, "q"]]))?;
            let ids: Vec<Value> = q[0][1]["ids"].as_array().cloned().unwrap_or_default();
            if ids.is_empty() { return Ok(n); }
            let r = self.call(json!([["Email/set", {"accountId": self.account, "destroy": ids}, "d"]]))?;
            let d = r[0][1]["destroyed"].as_array().map(|a| a.len()).unwrap_or(0);
            if d == 0 { return Err("The server did not delete the messages.".into()); }
            n += d;
        }
    }

    // ------------------------------------------------------------------
    // Email list / read (RFC 8621 §4)
    // ------------------------------------------------------------------

    /// One page of a list. Returns (emails, total, thread sizes by thread id).
    pub fn list(&self, filter: Value, position: usize, limit: usize) -> R<(Vec<Value>, usize, HashMap<String, usize>)> {
        let p = self.list_page(filter, position, None, limit)?;
        Ok((p.emails, p.total, p.threads))
    }

    /// One page of a list with the states needed to keep it current (Email/queryChanges,
    /// Email/changes, Thread/changes). With `anchor` the page starts right after that message
    /// (RFC 8620 §5.5 anchor/anchorOffset), so mail arriving meanwhile can't shift the page;
    /// an anchor the server no longer finds falls back to `position`.
    pub fn list_page(&self, filter: Value, position: usize, anchor: Option<&str>, limit: usize) -> R<ListPage> {
        let mut q = json!({
            "accountId": self.account, "filter": filter.clone(), "sort": list_sort(),
            "position": position, "limit": limit, "calculateTotal": true
        });
        if let Some(a) = anchor {
            q["anchor"] = json!(a);
            q["anchorOffset"] = json!(1);
            q.as_object_mut().unwrap().remove("position");
        }
        let props = json!(EMAIL_LIST_PROPERTIES);
        if limit > self.max_get() {
            // the ids would not fit one Email/get (maxObjectsInGet): fetch them in chunks
            // the states come from the same request as the ids, so a later delta misses nothing
            let r = self.request(json!([["Email/query", q, "0"],
                ["Email/get", {"accountId": self.account, "ids": [], "properties": ["id"]}, "e"],
                ["Thread/get", {"accountId": self.account, "ids": []}, "t"]]))?;
            if r[0][0] == "error" {
                if anchor.is_some() && r[0][1]["type"] == "anchorNotFound" { return self.list_page(filter.clone(), position, None, limit); }
                return Err(method_error(&r[0][1]));
            }
            let states = &r[1..];
            let ids = r[0][1]["ids"].as_array().cloned().unwrap_or_default();
            let mut emails = self.get_all("Email/get", &self.account, &ids, Some(&props))?;
            let order: HashMap<&str, usize> = ids.iter().enumerate().filter_map(|(i, v)| Some((v.as_str()?, i))).collect();
            emails.sort_by_key(|m| order.get(m["id"].as_str().unwrap_or("")).copied().unwrap_or(usize::MAX));
            let tids: Vec<Value> = emails.iter().filter_map(|m| m["threadId"].as_str().map(String::from)).collect::<std::collections::BTreeSet<_>>().into_iter().map(Value::from).collect();
            let threads = thread_sizes(&self.get_all("Thread/get", &self.account, &tids, None)?);
            return Ok(ListPage {
                emails, total: r[0][1]["total"].as_u64().unwrap_or(0) as usize, threads,
                query_state: r[0][1]["queryState"].as_str().map(String::from),
                email_state: states[0][1]["state"].as_str().map(String::from),
                thread_state: states[1][1]["state"].as_str().map(String::from),
                can_calculate_changes: r[0][1]["canCalculateChanges"].as_bool().unwrap_or(false),
            });
        }
        let r = self.request(json!([
            ["Email/query", q, "0"],
            ["Email/get", {
                "accountId": self.account,
                "#ids": {"resultOf": "0", "name": "Email/query", "path": "/ids"},
                "properties": props
            }, "1"],
            ["Thread/get", {
                "accountId": self.account,
                "#ids": {"resultOf": "1", "name": "Email/get", "path": "/list/*/threadId"}
            }, "2"]
        ]))?;
        if r[0][0] == "error" && anchor.is_some() && r[0][1]["type"] == "anchorNotFound" {
            return self.list_page(filter.clone(), position, None, limit);
        }
        if let Some(err) = r.iter().find(|x| x[0] == "error") { return Err(method_error(&err[1])); }
        Ok(ListPage {
            total: r[0][1]["total"].as_u64().unwrap_or(0) as usize,
            emails: r[1][1]["list"].as_array().cloned().unwrap_or_default(),
            threads: thread_sizes(r[2][1]["list"].as_array().map(|v| v.as_slice()).unwrap_or(&[])),
            query_state: r[0][1]["queryState"].as_str().map(String::from),
            email_state: r[1][1]["state"].as_str().map(String::from),
            thread_state: r[2][1]["state"].as_str().map(String::from),
            can_calculate_changes: r[0][1]["canCalculateChanges"].as_bool().unwrap_or(false),
        })
    }

    /// What changed in a list since its states, in one request (RFC 8620 §5.2/§5.6):
    /// Email/changes (messages updated in place), Email/queryChanges (membership and position),
    /// Thread/changes (conversation sizes), and the Email/get / Thread/get of what moved.
    /// `Ok(None)`: no delta possible (cannotCalculateChanges, tooManyChanges, a state that went
    /// backwards, too many changes): re-query the list instead.
    pub fn list_delta(&self, filter: &Value, query_state: &str, email_state: &str, thread_state: &str) -> R<Option<ListDelta>> {
        const MAX: usize = 256;
        if self.max_calls() < 7 { return Ok(None); }
        let max = MAX.min(self.max_get());
        let acc = &self.account;
        let props = json!(EMAIL_LIST_PROPERTIES);
        let r = self.request(json!([
            ["Email/changes", {"accountId": acc, "sinceState": email_state, "maxChanges": max}, "c"],
            ["Email/queryChanges", {"accountId": acc, "filter": filter, "sort": list_sort(),
                "sinceQueryState": query_state, "calculateTotal": true}, "q"],
            ["Email/get", {"accountId": acc, "#ids": {"resultOf": "q", "name": "Email/queryChanges", "path": "/added/*/id"}, "properties": props}, "a"],
            ["Email/get", {"accountId": acc, "#ids": {"resultOf": "c", "name": "Email/changes", "path": "/updated"}, "properties": props}, "u"],
            ["Thread/changes", {"accountId": acc, "sinceState": thread_state, "maxChanges": max}, "tc"],
            ["Thread/get", {"accountId": acc, "#ids": {"resultOf": "tc", "name": "Thread/changes", "path": "/updated"}}, "tu"],
            ["Thread/get", {"accountId": acc, "#ids": {"resultOf": "tc", "name": "Thread/changes", "path": "/created"}}, "tn"],
        ]))?;
        if r.len() < 7 || r.iter().any(|x| x[0] == "error") { return Ok(None); }
        let stalwart = self.is_stalwart();
        let Some(ec) = parse_changes(&r[0], email_state, stalwart) else { return Ok(None) };
        let Some(tc) = parse_changes(&r[4], thread_state, stalwart) else { return Ok(None) };
        if ec.has_more || tc.has_more { return Ok(None); }
        let qc = &r[1][1];
        let Some(new_query_state) = qc["newQueryState"].as_str() else { return Ok(None) };
        if stalwart && went_back(query_state, new_query_state) { return Ok(None); }
        let mut emails: HashMap<String, Value> = HashMap::new();
        for m in r[2][1]["list"].as_array().into_iter().flatten().chain(r[3][1]["list"].as_array().into_iter().flatten()) {
            if let Some(id) = m["id"].as_str() { emails.insert(id.to_string(), m.clone()); }
        }
        let mut threads = thread_sizes(r[5][1]["list"].as_array().map(|v| v.as_slice()).unwrap_or(&[]));
        threads.extend(thread_sizes(r[6][1]["list"].as_array().map(|v| v.as_slice()).unwrap_or(&[])));
        Ok(Some(ListDelta {
            removed: qc["removed"].as_array().map(|a| a.iter().filter_map(|x| x.as_str().map(String::from)).collect()).unwrap_or_default(),
            added: qc["added"].as_array().map(|a| a.iter().filter_map(|x| Some((x["id"].as_str()?.to_string(), x["index"].as_u64()? as usize))).collect()).unwrap_or_default(),
            emails,
            destroyed_threads: tc.destroyed,
            threads,
            total: qc["total"].as_u64().map(|t| t as usize),
            query_state: new_query_state.to_string(),
            email_state: ec.new_state,
            thread_state: tc.new_state,
        }))
    }

    /// Legacy helper: first page of a folder or a text search.
    pub fn emails(&self, mailbox: Option<&str>, text: Option<&str>) -> R<Vec<Value>> {
        let mut f = Map::new();
        if let Some(m) = mailbox { f.insert("inMailbox".into(), json!(m)); }
        if let Some(t) = text { f.insert("text".into(), json!(t)); }
        Ok(self.list(Value::Object(f), 0, 100)?.0)
    }

    /// A full message for the reader, and mark it read.
    pub fn email_body(&self, id: &str) -> R<Value> {
        self.email_full(id, false)
    }

    pub fn email_full(&self, id: &str, mark_seen: bool) -> R<Value> {
        let mut calls = vec![json!(["Email/get", {
            "accountId": self.account, "ids": [id],
            "properties": EMAIL_FULL_PROPERTIES,
            "bodyProperties": BODY_PROPERTIES,
            // only the text and HTML body parts are shown (RFC 8621 §4.2 fetchTextBodyValues /
            // fetchHTMLBodyValues); attachments are downloaded as blobs
            "fetchTextBodyValues": true, "fetchHTMLBodyValues": true, "maxBodyValueBytes": 2_000_000
        }, "g"])];
        if mark_seen {
            calls.push(json!(["Email/set", {"accountId": self.account, "update": { id: {"keywords/$seen": true} }}, "s"]));
        }
        let r = self.request(Value::Array(calls))?;
        if r[0][0] == "error" { return Err(method_error(&r[0][1])); }
        let m = r[0][1]["list"][0].clone();
        if m.is_null() { Err("Message not found.".into()) } else { Ok(m) }
    }

    pub fn thread_emails(&self, email_id: &str) -> R<Vec<Value>> {
        let r = self.call(json!([
            ["Email/get", {"accountId": self.account, "ids": [email_id], "properties": ["threadId"]}, "a"],
            ["Thread/get", {"accountId": self.account, "#ids": {"resultOf": "a", "name": "Email/get", "path": "/list/*/threadId"}}, "b"],
            ["Email/get", {"accountId": self.account, "#ids": {"resultOf": "b", "name": "Thread/get", "path": "/list/*/emailIds"},
                "properties": EMAIL_LIST_PROPERTIES}, "c"]
        ]))?;
        Ok(r[2][1]["list"].as_array().cloned().unwrap_or_default())
    }

    // ------------------------------------------------------------------
    // Flags, moving, deleting
    // ------------------------------------------------------------------

    pub fn set_keyword(&self, ids: &[String], keyword: &str, on: bool) -> R<(usize, Vec<String>)> {
        let v = if on { json!(true) } else { Value::Null };
        if !valid_keyword(keyword) { return Err(format!("\"{keyword}\" is not a valid keyword.")); }
        let key = keyword_path(keyword);
        self.update("Email/set", &self.account, ids.iter().map(|i| (i.clone(), json!({ key.clone(): v.clone() }))).collect())
    }

    /// Move: replace all mailboxes with `target`.
    pub fn move_to(&self, ids: &[String], target: &str) -> R<(usize, Vec<String>)> {
        self.update("Email/set", &self.account, ids.iter().map(|i| (i.clone(), json!({"mailboxIds": { target: true }}))).collect())
    }

    pub fn move_to_role(&self, ids: &[String], role: &str) -> R<(usize, Vec<String>)> {
        let roles = self.role_ids()?;
        let t = match roles.get(role) {
            Some(t) => t.clone(),
            // Bulwark creates the Archive folder the first time it is needed
            None if role == "archive" => {
                let r = self.call(json!([["Mailbox/set", {"accountId": self.account,
                    "create": {"a": {"name": "Archive", "role": "archive", "parentId": null, "isSubscribed": true}}}, "m"]]))?;
                self.roles.lock().unwrap().take();
                created_id(&r[0][1], "a")?
            }
            None => return Err(format!("This account has no {role} folder.")),
        };
        self.move_to(ids, &t)
    }

    /// Longest server-side hold in seconds: the submission account capability `maxDelayedSend`
    /// (RFC 8621 §1.3.2; account level, not session level) with the FUTURERELEASE (RFC 4865)
    /// submission extension. Stalwart advertises 30 days while its MTA refuses holds past its
    /// futureRelease limit (7 days by default), as Bulwark notes.
    pub fn max_delayed_send(&self) -> u64 {
        delayed_send_limit(&self.account_cap("urn:ietf:params:jmap:submission"), self.is_stalwart())
    }

    pub fn supports_delayed_send(&self) -> bool {
        self.max_delayed_send() > 0
    }

    /// Messages whose submission is still pending (scheduled / held sends).
    pub fn scheduled(&self) -> R<Vec<Value>> {
        let acc = self.account_for("urn:ietf:params:jmap:submission");
        let r = self.call(json!([
            ["EmailSubmission/query", {"accountId": acc, "filter": {"undoStatus": "pending"}}, "q"],
            ["EmailSubmission/get", {"accountId": acc, "#ids": {"resultOf": "q", "name": "EmailSubmission/query", "path": "/ids"}}, "g"]
        ]))?;
        let subs = r[1][1]["list"].as_array().cloned().unwrap_or_default();
        let ids: Vec<Value> = subs.iter().filter_map(|s| s["emailId"].as_str().map(|x| json!(x))).collect();
        if ids.is_empty() { return Ok(vec![]); }
        let mut emails = self.get_all("Email/get", &self.account, &ids, Some(&json!(EMAIL_LIST_PROPERTIES)))?;
        for e in emails.iter_mut() {
            if let Some(s) = subs.iter().find(|s| s["emailId"] == e["id"]) {
                e["receivedAt"] = s["sendAt"].clone();
                e["submissionId"] = s["id"].clone();
            }
        }
        Ok(emails)
    }

    pub fn trash(&self, id: &str) -> R<()> {
        self.move_to_role(&[id.to_string()], "trash").map(|_| ())
    }

    pub fn mark_spam(&self, ids: &[String], spam: bool) -> R<()> {
        let roles = self.role_ids()?;
        let target = if spam { roles.get("junk") } else { roles.get("inbox") }.ok_or("Missing Junk or Inbox folder.")?;
        let patch = if spam { json!({"mailboxIds": { target: true }, "keywords/$junk": true, "keywords/$notjunk": null}) }
                    else { json!({"mailboxIds": { target: true }, "keywords/$notjunk": true, "keywords/$junk": null}) };
        let (_, bad) = self.update("Email/set", &self.account, ids.iter().map(|i| (i.clone(), patch.clone())).collect())?;
        if bad.is_empty() { Ok(()) } else { Err(bad.join("; ")) }
    }

    pub fn destroy(&self, ids: &[String]) -> R<usize> {
        let mut n = 0;
        for chunk in ids.chunks(self.max_set()) {
            let r = self.call(json!([["Email/set", {"accountId": self.account, "destroy": chunk}, "d"]]))?;
            n += r[0][1]["destroyed"].as_array().map(|a| a.len()).unwrap_or(0);
        }
        Ok(n)
    }

    // ------------------------------------------------------------------
    // Blobs (RFC 8620 §6)
    // ------------------------------------------------------------------

    pub fn download(&self, blob_id: &str, name: &str, mime: &str) -> R<Vec<u8>> {
        let acc = self.account.clone();
        self.download_from(&acc, blob_id, name, mime)
    }

    /// Download a blob of a given account (a shared account's files).
    pub fn download_from(&self, account: &str, blob_id: &str, name: &str, mime: &str) -> R<Vec<u8>> {
        let t = self.url_template("downloadUrl").ok_or("The server has no download URL.")?;
        let enc = pct;
        let url = t.replace("{accountId}", &enc(account)).replace("{blobId}", &enc(blob_id))
            .replace("{name}", &enc(if name.is_empty() { "file" } else { name }))
            .replace("{type}", &enc(if mime.is_empty() { "application/octet-stream" } else { mime }));
        self.renew_if_due();
        // no per-request timeout: in reqwest it is a total deadline that also covers reading the
        // body, which a big attachment on a slow link overruns. The client's blocking timeout
        // bounds each wait (headers, every body read) with no overall cap.
        let mut resp = self.http.get(&url).header("Authorization", self.auth_header()).send().map_err(e)?;
        if resp.status() == 401 && self.oauth.is_some() {
            self.renew()?;
            resp = self.http.get(&url).header("Authorization", self.auth_header()).send().map_err(e)?;
        }
        if !resp.status().is_success() { return Err(format!("Download failed ({})", resp.status())); }
        Ok(resp.bytes().map_err(e)?.to_vec())
    }

    pub fn upload(&self, data: Vec<u8>, mime: &str) -> R<(String, usize)> {
        let acc = self.account.clone();
        self.upload_to(&acc, data, mime)
    }

    /// Upload a blob into a given account.
    pub fn upload_to(&self, account: &str, data: Vec<u8>, mime: &str) -> R<(String, usize)> {
        if data.len() > self.max_upload() {
            return Err(format!("The file is larger than the server allows ({} MB).", self.max_upload() / 1_000_000));
        }
        let t = self.url_template("uploadUrl").ok_or("The server has no upload URL.")?;
        let url = t.replace("{accountId}", &pct(account));
        let len = data.len();
        let mime = if mime.is_empty() { "application/octet-stream" } else { mime };
        self.renew_if_due();
        // RFC 8620 §6.1 maxConcurrentUpload
        let _p = self.uploads.enter();
        // a request timeout is a total deadline covering the body; scale it with the size
        // (at least 5 min, allowing ~32 KB/s) so slow links can still upload big files
        let limit = std::time::Duration::from_secs(300u64.max(len as u64 / 32_000));
        let send = |data: Vec<u8>| self.http.post(&url).header("Authorization", self.auth_header()).header("Content-Type", mime)
            .timeout(limit).body(data).send().map_err(e);
        let mut resp = send(data.clone())?;
        if resp.status() == 401 && self.oauth.is_some() {
            self.renew()?;
            resp = send(data)?;
        }
        if !resp.status().is_success() {
            let s = resp.status().as_u16();
            let v: Value = resp.json().unwrap_or(json!({}));
            return Err(if s == 413 { format!("The file is larger than the server allows ({} MB).", self.max_upload() / 1_000_000) } else { format!("Upload failed: {}", problem_message(s, &v)) });
        }
        let v: Value = resp.json().map_err(e)?;
        Ok((v["blobId"].as_str().ok_or("No blobId in upload response")?.to_string(), v["size"].as_u64().map(|s| s as usize).unwrap_or(len)))
    }

    // ------------------------------------------------------------------
    // Identities and sending (RFC 8621 §6, §7)
    // ------------------------------------------------------------------

    pub fn identities(&self) -> R<Vec<Value>> {
        let acc = self.account_for("urn:ietf:params:jmap:submission");
        let r = self.call(json!([["Identity/get", {"accountId": acc, "ids": null}, "i"]]))?;
        Ok(r[0][1]["list"].as_array().cloned().unwrap_or_default())
    }

    /// Simple text send (assistant / quick paths).
    pub fn send(&self, to: &str, subject: &str, body: &str, in_reply_to: Option<&str>) -> R<()> {
        let mut out = Outgoing { to: to.into(), subject: subject.into(), text: body.into(), ..Default::default() };
        if let Some(m) = in_reply_to { out.in_reply_to = vec![m.into()]; out.references = vec![m.into()]; }
        self.send_message(&out).map(|_| ())
    }

    /// Build the Email/set create object for a message.
    fn build_email(&self, o: &Outgoing, ident: &Value, mailbox: &str, draft: bool) -> R<Value> {
        let addrs = |s: &str| -> Vec<Value> { parse_addresses(s) };
        let mut kw = json!({"$seen": true});
        if draft { kw["$draft"] = json!(true); }
        for k in o.keywords.iter().filter(|k| valid_keyword(k)) { kw[k.to_lowercase().as_str()] = json!(true); }
        let from = header_from(o, ident, &self.username);
        let mut email = json!({
            "mailboxIds": { mailbox: true },
            "keywords": kw,
            "from": [from.clone()],
            "to": addrs(&o.to), "subject": o.subject,
        });
        if !o.cc.trim().is_empty() { email["cc"] = json!(addrs(&o.cc)); }
        if !o.bcc.trim().is_empty() { email["bcc"] = json!(addrs(&o.bcc)); }
        if let Some(rt) = ident["replyTo"].as_array().filter(|a| !a.is_empty()) { email["replyTo"] = json!(rt); }
        if !o.in_reply_to.is_empty() { email["inReplyTo"] = json!(o.in_reply_to); }
        if !o.references.is_empty() { email["references"] = json!(o.references); }
        if o.read_receipt { email["header:Disposition-Notification-To:asAddresses"] = json!([{"email": from["email"]}]); }
        let mut values = json!({"t": {"value": o.text}});
        let text_part = json!({"partId": "t", "type": "text/plain"});
        let body = if let Some(html) = &o.html {
            values["h"] = json!({"value": html});
            let html_part = json!({"partId": "h", "type": "text/html"});
            let used: Vec<&(String, Attachment)> = o.inline.iter().filter(|(cid, _)| html.contains(&format!("cid:{cid}"))).collect();
            let html_part = if used.is_empty() { html_part } else {
                let mut parts = vec![html_part];
                for (cid, a) in used {
                    parts.push(json!({"blobId": a.blob_id, "type": a.mime, "name": a.name, "disposition": "inline", "cid": cid}));
                }
                json!({"type": "multipart/related", "subParts": parts})
            };
            json!({"type": "multipart/alternative", "subParts": [text_part, html_part]})
        } else { text_part };
        email["bodyValues"] = values;
        if o.attachments.is_empty() {
            email["bodyStructure"] = body;
        } else {
            let mut parts = vec![body];
            for a in &o.attachments {
                parts.push(json!({"blobId": a.blob_id, "type": a.mime, "name": a.name, "disposition": "attachment"}));
            }
            email["bodyStructure"] = json!({"type": "multipart/mixed", "subParts": parts});
        }
        Ok(email)
    }

    /// The identity a message goes out through. With a From override (Bulwark
    /// sendEmail): an identity whose address is the override itself, else the composer's
    /// identity `from`, else one covering the override as a wildcard (`*@domain`, RFC 8621 §6).
    fn pick_identity_for(&self, from: &str, from_override: Option<&str>) -> R<Value> {
        let ids = self.identities()?;
        choose_identity(&ids, from, from_override).cloned().ok_or_else(|| match from_override.filter(|o| o.contains('@')) {
            Some(o) => format!("No identity of this account may send as {}.", o.trim()),
            None => "No sending identity on this account.".into(),
        })
    }

    /// Save or replace a draft. Returns the new draft id.
    pub fn save_draft(&self, o: &Outgoing, replace: Option<&str>) -> R<String> {
        let ident = self.pick_identity_for(&o.from, o.from_override.as_ref().map(|f| f.1.as_str()))?;
        let roles = self.role_ids()?;
        let drafts = roles.get("drafts").ok_or("No Drafts folder.")?;
        let email = self.build_email(o, &ident, drafts, true)?;
        let mut args = json!({"accountId": self.account, "create": {"d": email}});
        if let Some(old) = replace { args["destroy"] = json!([old]); }
        let r = self.call(json!([["Email/set", args, "e"]]))?;
        created_id(&r[0][1], "d")
    }

    /// Send: create in Drafts, submit (with HOLDFOR for delayed/undo send), then move to Sent.
    /// Returns the submission id (for undo) when the server holds it.
    pub fn send_message(&self, o: &Outgoing) -> R<Option<String>> {
        if parse_addresses(&o.to).is_empty() && parse_addresses(&o.cc).is_empty() && parse_addresses(&o.bcc).is_empty() {
            return Err("Add at least one recipient.".into());
        }
        let ident = self.pick_identity_for(&o.from, o.from_override.as_ref().map(|f| f.1.as_str()))?;
        let ident_id = ident["id"].as_str().unwrap_or("").to_string();
        let roles = self.role_ids()?;
        let drafts = roles.get("drafts").ok_or("No Drafts folder.")?.clone();
        let sent = roles.get("sent").ok_or("No Sent folder.")?.clone();
        let email = self.build_email(o, &ident, &drafts, true)?;
        let sub_acc = self.account_for("urn:ietf:params:jmap:submission");
        let rcpts: Vec<String> = parse_addresses(&format!("{},{},{}", o.to, o.cc, o.bcc)).iter().filter_map(|a| a["email"].as_str().map(String::from)).collect();
        // HOLDFOR: a scheduled time (Schedule send), else the undo-send delay (RFC 4865 FUTURERELEASE)
        let max = self.max_delayed_send();
        let hold: Option<u64> = match o.send_at {
            Some(at) => Some(scheduled_hold(at, chrono::Utc::now().timestamp_millis(), max)?),
            None => (o.hold_seconds > 0 && max > 0).then(|| o.hold_seconds.min(max)),
        };
        // the MAIL FROM the server derives from the identity, and the one asked for: the From
        // override, so the Return-Path does not give the identity's address away (Bulwark #1009)
        let identity_mail_from = header_from(&Outgoing { from_override: None, ..o.clone() }, &ident, &self.username)["email"].as_str().unwrap_or("").to_string();
        let mail_from = o.from_override.as_ref().map(|f| f.1.trim().to_string()).filter(|e| e.contains('@')).unwrap_or_else(|| identity_mail_from.clone());
        let submission = |email_id: &str, mail_from: &str| -> Value {
            let mut sub = json!({"emailId": email_id, "identityId": ident_id});
            if let Some(env) = submission_envelope(mail_from, &identity_mail_from, hold, &rcpts) { sub["envelope"] = env; }
            sub
        };
        let on_success = json!({"#s": {
            format!("mailboxIds/{drafts}"): null,
            format!("mailboxIds/{sent}"): true,
            "keywords/$draft": null
        }});
        let mut calls = vec![
            json!(["Email/set", {"accountId": self.account, "create": {"d": email}, "destroy": o.replaces_draft.iter().collect::<Vec<_>>()}, "e"]),
            json!(["EmailSubmission/set", {
                "accountId": sub_acc,
                "create": {"s": submission("#d", &mail_from)},
                "onSuccessUpdateEmail": on_success
            }, "s"]),
        ];
        if let Some(orig) = &o.answering {
            let kw = if o.forwarding { "$forwarded" } else { "$answered" };
            calls.push(json!(["Email/set", {"accountId": self.account, "update": { orig: { format!("keywords/{kw}"): true } }}, "a"]));
        }
        let r = self.call(Value::Array(calls))?;
        let draft_copy = created_id(&r[0][1], "d")?;
        let sid = match created_id(&r[1][1], "s") {
            Ok(sid) => sid,
            // a server that only takes the identity's own address as MAIL FROM (RFC 8621
            // §7.5 forbiddenMailFrom; Stalwart answers forbiddenFrom): the copy in Drafts is
            // submitted once more with the identity's address (Bulwark sendEmail)
            Err(_) if mail_from_refused(&r[1][1]["notCreated"]["s"]) && !mail_from.eq_ignore_ascii_case(&identity_mail_from) => {
                let r2 = self.call(json!([["EmailSubmission/set", {
                    "accountId": sub_acc,
                    "create": {"s": submission(&draft_copy, &identity_mail_from)},
                    "onSuccessUpdateEmail": on_success
                }, "s"]]))?;
                created_id(&r2[0][1], "s")?
            }
            Err(e) => return Err(e),
        };
        Ok(hold.is_some_and(|h| h > 0).then_some(sid))
    }

    /// Move a scheduled submission to another time (Bulwark rescheduleEmailSubmission): cancel
    /// the original first (servers may allow one active submission per message), then submit
    /// the same message again with HOLDFOR for the new time and the original envelope. When
    /// that fails the original is put back at its old time; when even that fails the message
    /// goes back to Drafts so it does not sit in Sent looking sent.
    pub fn reschedule_submission(&self, id: &str, at_ms: i64) -> R<String> {
        let acc = self.account_for("urn:ietf:params:jmap:submission");
        let max = self.max_delayed_send();
        scheduled_hold(at_ms, chrono::Utc::now().timestamp_millis(), max)?;
        let r = self.call(json!([["EmailSubmission/get", {"accountId": acc, "ids": [id], "properties": ["emailId", "identityId", "envelope", "sendAt", "undoStatus"]}, "g"]]))?;
        let orig = r[0][1]["list"][0].clone();
        if orig.is_null() { return Err("This message is no longer scheduled.".into()); }
        if orig["undoStatus"].as_str() != Some("pending") {
            return Err(if orig["undoStatus"] == "final" { "This message was already sent." } else { "This message is no longer scheduled." }.into());
        }
        let email_id = orig["emailId"].as_str().ok_or("This message is no longer scheduled.")?.to_string();
        let ident_id = orig["identityId"].as_str().unwrap_or("").to_string();
        let old_at = orig["sendAt"].as_str().and_then(|s| chrono::DateTime::parse_from_rfc3339(s).ok()).map(|d| d.timestamp_millis());
        // the envelope it had, else one from the recipients of the message
        let (from, rcpt) = match orig["envelope"]["rcptTo"].as_array().filter(|a| !a.is_empty()) {
            Some(rc) => (orig["envelope"]["mailFrom"]["email"].as_str().unwrap_or("").to_string(), rc.iter().map(|x| json!({"email": x["email"]})).collect::<Vec<_>>()),
            None => {
                let e = self.call(json!([["Email/get", {"accountId": acc, "ids": [email_id], "properties": ["from", "to", "cc", "bcc"]}, "e"]]))?;
                let m = &e[0][1]["list"][0];
                let rc: Vec<Value> = ["to", "cc", "bcc"].iter().flat_map(|k| m[*k].as_array().cloned().unwrap_or_default()).map(|x| json!({"email": x["email"]})).collect();
                (m["from"][0]["email"].as_str().unwrap_or("").to_string(), rc)
            }
        };
        let from = if from.is_empty() { self.username.clone() } else { from };
        let roles = self.role_ids()?;
        let sent = roles.get("sent").cloned();
        let create = |until: Option<i64>| -> R<String> {
            let mut mail_from = json!({"email": from});
            if let Some(t) = until {
                let secs = ((t - chrono::Utc::now().timestamp_millis()) as f64 / 1000.0).ceil().max(1.0) as u64;
                mail_from["parameters"] = json!({"HOLDFOR": secs.to_string()});
            }
            let mut args = json!({"accountId": acc, "create": {"n": {"emailId": email_id, "identityId": ident_id, "envelope": {"mailFrom": mail_from, "rcptTo": rcpt}}}});
            if let Some(sent) = &sent {
                args["onSuccessUpdateEmail"] = json!({"#n": {"mailboxIds": { sent.as_str(): true }, "keywords/$draft": null}});
            }
            let r = self.call(json!([["EmailSubmission/set", args, "s"]]))?;
            created_id(&r[0][1], "n")
        };
        let c = self.call(json!([["EmailSubmission/set", {"accountId": acc, "update": { id: {"undoStatus": "canceled"} }}, "c"]]))?;
        if let Some(err) = c[0][1]["notUpdated"].get(id) { return Err(set_error(err)); }
        let res = match create(Some(at_ms)) {
            Ok(n) => Ok(n),
            Err(e) => {
                let mut out = Err(format!("Reschedule failed and the message is no longer scheduled: {e}"));
                for step in reschedule_fallbacks(old_at, chrono::Utc::now().timestamp_millis()) {
                    match step {
                        RescheduleFallback::Recreate(t) => if create(Some(t)).is_ok() {
                            out = Err(format!("Nothing was changed: {e}. The message is still scheduled."));
                            break;
                        },
                        RescheduleFallback::RestoreDraft => {
                            // the message lives in the submission's account (RFC 8621 §7)
                            if self.restore_draft_in(&acc, &email_id) {
                                out = Err(format!("Reschedule failed: {e}. The message is back in Drafts."));
                            }
                        }
                    }
                }
                out
            }
        };
        // the canceled record is housekeeping only (counts against the submission limit)
        let _ = self.call(json!([["EmailSubmission/set", {"accountId": acc, "destroy": [id]}, "d"]]));
        res
    }

    /// `restore_draft` for a message in account `acc`, which need not be the mail account.
    fn restore_draft_in(&self, acc: &str, eid: &str) -> bool {
        if acc == self.account { return self.restore_draft(eid); }
        let Ok(r) = self.call(json!([["Mailbox/query", {"accountId": acc, "filter": {"role": "drafts"}}, "q"]])) else { return false };
        let Some(drafts) = r[0][1]["ids"][0].as_str().map(str::to_string) else { return false };
        self.update("Email/set", acc, vec![(eid.to_string(), json!({"mailboxIds": { drafts: true }, "keywords/$draft": true}))])
            .is_ok_and(|(_, bad)| bad.is_empty())
    }

    /// Undo a held submission (RFC 8621 §7.5 undoStatus). The canceled record is destroyed (it
    /// only counts against the server's submission limit) and the message, which
    /// onSuccessUpdateEmail already filed in Sent, goes back to Drafts. Returns its id.
    pub fn cancel_submission(&self, id: &str) -> R<Option<String>> {
        let acc = self.account_for("urn:ietf:params:jmap:submission");
        let r = self.call(json!([
            ["EmailSubmission/set", {"accountId": acc, "update": { id: {"undoStatus": "canceled"} }}, "c"],
            ["EmailSubmission/get", {"accountId": acc, "ids": [id], "properties": ["emailId", "undoStatus"]}, "g"]
        ]))?;
        if let Some(err) = r[0][1]["notUpdated"].get(id) { return Err(set_error(err)); }
        if r[1][1]["list"][0]["undoStatus"].as_str().is_some_and(|s| s != "canceled") {
            return Err("The message has already been sent.".into());
        }
        let Some(eid) = r[1][1]["list"][0]["emailId"].as_str().map(String::from) else { return Ok(None) };
        // the canceled submission record is housekeeping only: a failed destroy leaves a
        // harmless canceled entry, so its result does not decide the undo
        let _ = self.call(json!([["EmailSubmission/set", {"accountId": acc, "destroy": [id]}, "d"]]));
        if acc == self.account && self.restore_draft(&eid) { return Ok(Some(eid)); }
        // could not turn it back into a draft (or it lives in another account): remove the
        // never-sent copy so it cannot pass for sent mail; the composer still holds the content
        // and saves it as a new draft
        let _ = self.call(json!([["Email/set", {"accountId": acc, "destroy": [eid]}, "x"]]));
        Ok(None)
    }

    /// Move a message back to Drafts with `$draft`; retried once with fresh folder roles.
    pub(crate) fn restore_draft(&self, eid: &str) -> bool {
        for fresh in [false, true] {
            if fresh { self.roles.lock().unwrap().take(); }
            let Ok(roles) = self.role_ids() else { continue };
            let Some(drafts) = roles.get("drafts") else { continue };
            if let Ok((_, bad)) = self.update("Email/set", &self.account, vec![(eid.to_string(), json!({"mailboxIds": { drafts: true }, "keywords/$draft": true}))]) {
                if bad.is_empty() { return true; }
            }
        }
        false
    }

    // ------------------------------------------------------------------
    // Push (RFC 8620 §7.3 EventSource)
    // ------------------------------------------------------------------

    /// Blocks reading the event stream (RFC 8620 §7.3) for the given data types, calling
    /// `on_event` with each event (pings included, so a stopped caller notices within 30 s);
    /// it returns `false` to stop. `ping` asks the server for a
    /// keep-alive every 30 s (Stalwart's minimum) and a read that stays silent for 75 s counts as a
    /// dead connection, so the caller can reconnect. OAuth tokens are renewed on 401.
    pub fn watch(&self, types: &[&str], on_event: &mut dyn FnMut(PushEvent) -> bool) -> R<()> {
        let t = self.url_template("eventSourceUrl").ok_or("No push support.")?;
        let types = if types.is_empty() { "*".to_string() } else { types.join(",") };
        let url = t.replace("{types}", &pct(&types).replace("%2C", ",")).replace("{closeafter}", "no").replace("{ping}", "30");
        // reqwest's blocking timeout bounds each read, not the whole stream
        let client = reqwest::blocking::Client::builder().timeout(std::time::Duration::from_secs(75))
            .tcp_keepalive(std::time::Duration::from_secs(30)).build().map_err(e)?;
        self.renew_if_due();
        let open = || client.get(&url).header("Authorization", self.auth_header()).header("Accept", "text/event-stream")
            .header("Cache-Control", "no-cache").send().map_err(e);
        let mut resp = open()?;
        if resp.status() == 401 && self.oauth.is_some() {
            self.renew()?;
            resp = open()?;
        }
        if !resp.status().is_success() { return Err(format!("Push failed ({})", resp.status())); }
        if !on_event(PushEvent::Open) { return Ok(()); }
        let reader = std::io::BufReader::new(resp);
        let mut parser = SseParser::default();
        for line in std::io::BufRead::lines(reader) {
            let line = line.map_err(e)?;
            if let Some(ev) = parser.line(&line) {
                if let Ok(path) = std::env::var("BARBICAN_JMAP_TRACE") {
                    let what = match &ev { PushEvent::State(v) => format!("state {}", v["changed"]), PushEvent::Ping => "ping".into(), e => format!("{e:?}") };
                    if let Ok(mut f) = std::fs::OpenOptions::new().create(true).append(true).open(path) {
                        use std::io::Write; let _ = writeln!(f, "{} PUSH {what}", chrono::Local::now().format("%H:%M:%S%.3f"));
                    }
                }
                if !on_event(ev) { return Ok(()); }
            }
        }
        Err("Push stream closed.".into())
    }

    // ------------------------------------------------------------------
    // Vacation, quota
    // ------------------------------------------------------------------

    pub fn quota(&self) -> R<Option<(u64, u64)>> {
        if !self.has("urn:ietf:params:jmap:quota") { return Ok(None); }
        let acc = self.account_for("urn:ietf:params:jmap:quota");
        let r = self.call(json!([["Quota/get", {"accountId": acc, "ids": null}, "q"]]))?;
        let list = r[0][1]["list"].as_array().cloned().unwrap_or_default();
        Ok(pick_quota(&list).map(|q| (q["used"].as_u64().unwrap_or(0), q["hardLimit"].as_u64().unwrap_or(0))))
    }
}

/// The storage quota to show (RFC 9425 §4): an `octets` quota, preferring one that covers Mail
/// (`types`) at account scope, then the tightest of the rest.
pub fn pick_quota(list: &[Value]) -> Option<&Value> {
    let octets = list.iter().filter(|q| q["resourceType"] == "octets");
    let mail = |q: &&Value| q["types"].as_array().is_none_or(|t| t.is_empty() || t.iter().any(|x| x == "Mail"));
    let room = |q: &&Value| q["hardLimit"].as_u64().unwrap_or(0).saturating_sub(q["used"].as_u64().unwrap_or(0));
    octets.clone().filter(mail).min_by_key(|q| (q["scope"] != "account", room(q)))
        .or_else(|| octets.min_by_key(room))
}

// ----------------------------------------------------------------------
// Helpers and constants
// ----------------------------------------------------------------------

#[derive(Default, Clone)]
pub struct Attachment {
    pub blob_id: String,
    pub name: String,
    pub mime: String,
    pub size: usize,
}

#[derive(Default, Clone)]
pub struct Outgoing {
    pub from: String,
    pub to: String,
    pub cc: String,
    pub bcc: String,
    pub subject: String,
    pub text: String,
    pub html: Option<String>,
    pub attachments: Vec<Attachment>,
    /// Inline images referenced from the HTML part as `cid:` (content id, blob).
    pub inline: Vec<(String, Attachment)>,
    pub in_reply_to: Vec<String>,
    pub references: Vec<String>,
    pub answering: Option<String>,
    pub forwarding: bool,
    pub replaces_draft: Option<String>,
    pub hold_seconds: u64,
    /// Scheduled send: release time (Unix ms). The hold is worked out right before the request.
    pub send_at: Option<i64>,
    pub read_receipt: bool,
    /// Extra keywords (tags) to set on the message.
    pub keywords: Vec<String>,
    /// Bulwark's From override ("Edit sender…", or the catch-all reply rewrite): the header
    /// From as (name, email), sent through the identity `from`. Also asked for as MAIL FROM.
    pub from_override: Option<(String, String)>,
}

/// The header From of a message: the override when there is one, else the identity
/// (a wildcard identity names no address: the login stands in, as in Bulwark).
pub fn header_from(o: &Outgoing, ident: &Value, username: &str) -> Value {
    if let Some((name, email)) = o.from_override.as_ref().filter(|f| f.1.trim().contains('@')) {
        let name = name.trim();
        return if name.is_empty() { json!({"email": email.trim()}) } else { json!({"name": name, "email": email.trim()}) };
    }
    let e = ident["email"].as_str().unwrap_or("");
    let email = if e.contains('*') || !e.contains('@') { username } else { e };
    match ident["name"].as_str().filter(|n| !n.trim().is_empty()) {
        Some(n) => json!({"name": n, "email": email}),
        None => json!({"email": email}),
    }
}

/// Whether identity address `pattern` is a wildcard covering `addr` (`*@example.com`).
pub fn wildcard_covers(pattern: &str, addr: &str) -> bool {
    let (p, a) = (pattern.trim().to_lowercase(), addr.trim().to_lowercase());
    match (p.split_once('@'), a.split_once('@')) {
        (Some(("*", pd)), Some((al, ad))) => !al.is_empty() && pd == ad,
        _ => false,
    }
}

/// Bulwark's identity choice for a send: the override's own identity, the composer's
/// (by address or id), one covering the override as a wildcard, else the first; with an
/// override nothing matches, none (the override must not go out under an unrelated identity).
pub fn choose_identity<'a>(ids: &'a [Value], from: &str, from_override: Option<&str>) -> Option<&'a Value> {
    let eq = |i: &&Value, a: &str| { let a = a.trim(); !a.is_empty() && i["email"].as_str().is_some_and(|e| e.trim().eq_ignore_ascii_case(a)) };
    from_override.and_then(|o| ids.iter().find(|i| eq(i, o)))
        .or_else(|| ids.iter().find(|i| eq(i, from)))
        .or_else(|| ids.iter().find(|i| i["id"].as_str() == Some(from)))
        .or_else(|| from_override.and_then(|o| ids.iter().find(|i| i["email"].as_str().is_some_and(|e| wildcard_covers(e, o)))))
        .or_else(|| if from_override.is_some_and(|o| o.contains('@')) { None } else { ids.first() })
}

/// The submission envelope (RFC 8621 §7): only when MAIL FROM differs from the identity's
/// address or carries HOLDFOR; otherwise the server derives it.
pub fn submission_envelope(mail_from: &str, identity_mail_from: &str, hold: Option<u64>, rcpts: &[String]) -> Option<Value> {
    if hold.is_none() && mail_from.eq_ignore_ascii_case(identity_mail_from) { return None; }
    let mut from = json!({"email": mail_from});
    if let Some(h) = hold { from["parameters"] = json!({"HOLDFOR": h.to_string()}); }
    Some(json!({"mailFrom": from, "rcptTo": rcpts.iter().map(|r| json!({"email": r})).collect::<Vec<_>>()}))
}

/// A submission refused for its MAIL FROM (forbiddenMailFrom, or Stalwart's forbiddenFrom).
pub fn mail_from_refused(err: &Value) -> bool {
    matches!(err["type"].as_str(), Some("forbiddenMailFrom" | "forbiddenFrom"))
}

/// "Name <a@b>, c@d" -> [{name, email}]
pub fn parse_addresses(s: &str) -> Vec<Value> {
    let mut out = vec![];
    let mut cur = String::new();
    let mut quoted = false;
    let mut angle = false;
    let mut chars = s.chars();
    while let Some(ch) = chars.next() {
        match ch {
            // a quoted-pair inside a display name (`"Doo, \"J\"" <j@d.o>`)
            '\\' if quoted => { cur.push(ch); if let Some(n) = chars.next() { cur.push(n); } }
            '"' => { quoted = !quoted; cur.push(ch); }
            '<' => { angle = true; cur.push(ch); }
            '>' => { angle = false; cur.push(ch); }
            ',' | ';' if !quoted && !angle => { out.push(std::mem::take(&mut cur)); }
            _ => cur.push(ch),
        }
    }
    out.push(cur);
    out.into_iter().filter_map(|p| {
        let p = p.trim();
        if p.is_empty() { return None; }
        if let (Some(a), Some(b)) = (p.rfind('<'), p.rfind('>')) {
            let email = p[a + 1..b].trim();
            let raw = p[..a].trim();
            let name = match raw.strip_prefix('"').and_then(|r| r.strip_suffix('"')) {
                Some(q) => { let mut o = String::new(); let mut it = q.chars(); while let Some(c) = it.next() { o.push(if c == '\\' { it.next().unwrap_or(c) } else { c }); } o }
                None => raw.to_string(),
            };
            let name = name.trim();
            if email.contains('@') {
                return Some(if name.is_empty() { json!({"email": email}) } else { json!({"name": name, "email": email}) });
            }
        }
        p.contains('@').then(|| json!({"email": p.trim_matches(['<', '>'])}))
    }).collect()
}

pub fn method_error(err: &Value) -> String {
    let t = err["type"].as_str().unwrap_or("error");
    let d = err["description"].as_str().unwrap_or("");
    match t {
        "unknownMethod" | "unknownCapability" => format!("The server does not support this ({t})."),
        "forbidden" | "accountReadOnly" => "You do not have permission to do that.".into(),
        "accountNotFound" | "accountNotSupportedByMethod" => "This account does not support that.".into(),
        "requestTooLarge" => "Too many items in one request.".into(),
        "cannotCalculateChanges" => "The list changed too much; reload.".into(),
        "tooManyChanges" => "Too many changes at once; reload.".into(),
        "stateMismatch" => "Someone else changed this at the same time; try again.".into(),
        "anchorNotFound" => "The list changed; reload.".into(),
        "serverUnavailable" | "serverFail" | "serverPartialFail" => format!("The server had a problem ({t}){}; try again.", if d.is_empty() { String::new() } else { format!(": {d}") }),
        "unsupportedSort" | "unsupportedFilter" => format!("The server can't sort or search like that{}.", if d.is_empty() { String::new() } else { format!(": {d}") }),
        _ => if d.is_empty() { format!("Server error: {t}") } else { format!("{t}: {d}") },
    }
}

pub fn set_error(err: &Value) -> String {
    let t = err["type"].as_str().unwrap_or("error");
    let d = err["description"].as_str().unwrap_or("");
    let props = err["properties"].as_array().map(|a| a.iter().filter_map(|p| p.as_str()).collect::<Vec<_>>().join(", ")).unwrap_or_default();
    let with = |s: &str| format!("{s}{}.", if d.is_empty() { String::new() } else { format!(": {d}") });
    match t {
        "overQuota" => "Your mailbox is full.".into(),
        "tooLarge" => "The message is too large.".into(),
        "rateLimit" => "Too many messages sent recently; try again later.".into(),
        "forbiddenFrom" => "You may not send from that address.".into(),
        "forbiddenMailFrom" => "The server refused the sender address.".into(),
        "forbiddenToSend" => "This account may not send mail.".into(),
        "invalidEmail" => with("The message is invalid"),
        "tooManyRecipients" => "Too many recipients.".into(),
        "noRecipients" => "Add at least one recipient.".into(),
        "invalidRecipients" => with("Some recipients are invalid"),
        "cannotUnsend" => "The message has already been sent.".into(),
        "mailboxHasChild" => "Delete or move its subfolders first.".into(),
        "mailboxHasEmail" => "The folder is not empty.".into(),
        "tooManyKeywords" => "The message has too many tags.".into(),
        "tooManyMailboxes" => "The message is in too many folders.".into(),
        "blobNotFound" => "An attachment is no longer on the server; attach it again.".into(),
        "notFound" => "It no longer exists.".into(),
        "forbidden" => "You do not have permission to do that.".into(),
        "singleton" => "This object can't be created or deleted.".into(),
        "alreadyExists" => "That already exists.".into(),
        "willDestroy" => "It is being deleted.".into(),
        "invalidPatch" => with("The change is invalid"),
        "invalidProperties" => format!("Invalid {props}{}", if d.is_empty() { String::new() } else { format!(": {d}") }),
        _ => if d.is_empty() { t.to_string() } else { format!("{t}: {d}") },
    }
}

fn created_id(resp: &Value, key: &str) -> R<String> {
    if let Some(id) = resp["created"][key]["id"].as_str() { return Ok(id.to_string()); }
    match resp["notCreated"].get(key) {
        Some(err) => Err(set_error(err)),
        None => Err("The server did not create it.".into()),
    }
}

// ----------------------------------------------------------------------
// Sync: /changes, /queryChanges and push (RFC 8620 §5.2, §5.6, §7)
// ----------------------------------------------------------------------

/// The message list order (Email/query and Email/queryChanges must use the same sort).
pub fn list_sort() -> Value { json!([{"property": "receivedAt", "isAscending": false}]) }

/// A `Foo/changes` response (RFC 8620 §5.2).
#[derive(Debug, Default, Clone, PartialEq)]
pub struct Changes {
    pub old_state: String,
    pub new_state: String,
    pub has_more: bool,
    pub created: Vec<String>,
    pub updated: Vec<String>,
    pub destroyed: Vec<String>,
    /// Mailbox/changes: only these properties changed (RFC 8621 §2.2), e.g. the counts.
    pub updated_properties: Option<Vec<String>>,
}

/// Read a `[name, args, id]` /changes response; `None` for a method error
/// (cannotCalculateChanges and the like) or a Stalwart state that went backwards.
pub fn parse_changes(resp: &Value, since: &str, stalwart: bool) -> Option<Changes> {
    if !resp[0].as_str().is_some_and(|m| m.ends_with("/changes")) { return None; }
    let r = &resp[1];
    let new_state = r["newState"].as_str()?.to_string();
    if stalwart && went_back(since, &new_state) { return None; }
    let ids = |k: &str| r[k].as_array().map(|a| a.iter().filter_map(|x| x.as_str().map(String::from)).collect()).unwrap_or_default();
    Some(Changes {
        old_state: r["oldState"].as_str().unwrap_or(since).to_string(),
        new_state,
        has_more: r["hasMoreChanges"] == true,
        created: ids("created"),
        updated: ids("updated"),
        destroyed: ids("destroyed"),
        updated_properties: r["updatedProperties"].as_array().map(|a| a.iter().filter_map(|x| x.as_str().map(String::from)).collect()),
    })
}

const STALWART_BASE32: &[u8; 32] = b"abcdefghijklmnopqrstuvwxyz792013";

/// The change id inside a Stalwart state string (`s` + base32(LEB128(id))), or `None` for an
/// initial / intermediate state or another server's format (Bulwark lib/jmap/stalwart-state.ts,
/// Stalwart jmap-proto types/state.rs).
pub fn stalwart_change_id(state: &str) -> Option<u64> {
    let body = state.strip_prefix('s')?;
    if body.is_empty() { return None; }
    let (mut buf, mut bits, mut bytes) = (0u32, 0u32, vec![]);
    for ch in body.bytes() {
        let v = STALWART_BASE32.iter().position(|c| *c == ch)? as u32;
        buf = (buf << 5) | v;
        bits += 5;
        if bits >= 8 {
            bits -= 8;
            bytes.push(((buf >> bits) & 0xff) as u8);
            buf &= (1 << bits) - 1;
        }
    }
    let mut id: u64 = 0;
    for (i, b) in bytes.iter().enumerate().take(10) {
        id |= ((b & 0x7f) as u64).checked_shl(7 * i as u32).unwrap_or(0);
        if b & 0x80 == 0 { return (stalwart_state(id) == state).then_some(id); }
    }
    None
}

/// The Stalwart state string of a change id (the inverse of [`stalwart_change_id`]).
pub fn stalwart_state(mut id: u64) -> String {
    let mut bytes = vec![];
    loop {
        let b = (id & 0x7f) as u8;
        id >>= 7;
        if id == 0 { bytes.push(b); break; }
        bytes.push(b | 0x80);
    }
    let (mut out, mut buf, mut bits) = (String::from("s"), 0u32, 0u32);
    for b in bytes {
        buf = (buf << 8) | b as u32;
        bits += 8;
        while bits >= 5 {
            bits -= 5;
            out.push(STALWART_BASE32[((buf >> bits) & 31) as usize] as char);
        }
        buf &= (1 << bits) - 1;
    }
    if bits > 0 { out.push(STALWART_BASE32[((buf << (5 - bits)) & 31) as usize] as char); }
    out
}

/// Stalwart change ids only grow: a new state below the old one means its counters went back
/// (a restore), and it would then answer with an empty delta. Re-fetch in full instead.
pub fn went_back(since: &str, new: &str) -> bool {
    matches!((stalwart_change_id(since), stalwart_change_id(new)), (Some(a), Some(b)) if b < a)
}

/// What `reschedule_submission` tries when the new submission cannot be created, in order.
#[derive(Debug, PartialEq)]
pub enum RescheduleFallback { Recreate(i64), RestoreDraft }

/// Fallback order (pure, for tests): put it back at its old time while that is still ahead
/// (a time already past would send it at once, which nobody asked for), else back to Drafts
/// so the message never stays in Sent looking sent.
pub fn reschedule_fallbacks(old_at: Option<i64>, now: i64) -> Vec<RescheduleFallback> {
    let mut v = vec![];
    if let Some(t) = old_at.filter(|t| *t > now + 1000) { v.push(RescheduleFallback::Recreate(t)); }
    v.push(RescheduleFallback::RestoreDraft);
    v
}

/// Apply a /changes result to a cached object list: drop `destroyed`, then merge each fetched
/// object into the one with its id (a partial fetch updates only its properties) or append it.
/// HOLDFOR seconds for a release at `at_ms` (Unix ms), checked against the server's
/// maxDelayedSend `max` (0: the server cannot hold messages).
pub fn scheduled_hold(at_ms: i64, now_ms: i64, max: u64) -> R<u64> {
    if max == 0 { return Err("Scheduled send is not supported for this account.".into()); }
    let secs = ((at_ms - now_ms) as f64 / 1000.0).ceil() as i64;
    if secs <= 0 { return Err("Choose a future date and time.".into()); }
    if secs as u64 > max { return Err("This time is later than the server allows.".into()); }
    Ok(secs as u64)
}

/// `maxDelayedSend` of a submission capability, or 0 unless FUTURERELEASE is listed in
/// `submissionExtensions` (as Bulwark's hasDelayedSend: a missing list means no extension).
fn delayed_send_limit(cap: &Value, stalwart: bool) -> u64 {
    let max = cap["maxDelayedSend"].as_u64().unwrap_or(0);
    let has = |k: &str| k.eq_ignore_ascii_case("FUTURERELEASE");
    let future = match &cap["submissionExtensions"] {
        Value::Object(o) => o.keys().any(|k| has(k)),
        Value::Array(a) => a.iter().any(|k| k.as_str().is_some_and(has)),
        _ => false,
    };
    if !future { return 0; }
    if stalwart && max == 30 * 86400 { 7 * 86400 } else { max }
}

/// Updated ids to fetch with the changed properties only (`partial`) and those to fetch whole:
/// an id missing from `have` always needs every property.
fn split_known(ids: &[&String], have: &[Value], partial: bool) -> (Vec<Value>, Vec<Value>) {
    let (known, missing): (Vec<&&String>, Vec<&&String>) = ids.iter().partition(|i| !partial || have.iter().any(|m| m["id"] == i.as_str()));
    (known.into_iter().map(|i| json!(i)).collect(), missing.into_iter().map(|i| json!(i)).collect())
}

pub fn apply_object_delta(have: &[Value], destroyed: &[String], fetched: &[Value]) -> Vec<Value> {
    let mut out: Vec<Value> = have.iter().filter(|m| !destroyed.iter().any(|d| m["id"] == d.as_str())).cloned().collect();
    for f in fetched {
        match out.iter_mut().find(|m| m["id"] == f["id"]) {
            Some(m) => if let (Some(m), Some(f)) = (m.as_object_mut(), f.as_object()) {
                for (k, v) in f { m.insert(k.clone(), v.clone()); }
            },
            None => out.push(f.clone()),
        }
    }
    out
}

/// A page of a message list with what is needed to keep it current.
#[derive(Debug, Default, Clone)]
pub struct ListPage {
    pub emails: Vec<Value>,
    pub total: usize,
    pub threads: HashMap<String, usize>,
    pub query_state: Option<String>,
    pub email_state: Option<String>,
    pub thread_state: Option<String>,
    /// Email/query `canCalculateChanges` (RFC 8620 §5.5).
    pub can_calculate_changes: bool,
}

/// What changed in a list (see [`Client::list_delta`]).
#[derive(Debug, Default, Clone)]
pub struct ListDelta {
    /// Email/queryChanges `removed` (may include ids that are re-added, RFC 8620 §5.6).
    pub removed: Vec<String>,
    /// Email/queryChanges `added`: (id, index in the new list), ascending.
    pub added: Vec<(String, usize)>,
    /// Fresh copies of the added and the updated messages.
    pub emails: HashMap<String, Value>,
    pub threads: HashMap<String, usize>,
    pub destroyed_threads: Vec<String>,
    pub total: Option<usize>,
    pub query_state: String,
    pub email_state: String,
    pub thread_state: String,
}

/// Apply a delta to the loaded part of a list (RFC 8620 §5.6: remove every `removed` id, then
/// insert the `added` ones by ascending index). Additions past the loaded rows are left to paging.
/// Messages updated in place (keywords) are replaced by their fresh copy.
pub fn apply_list_delta(emails: &mut Vec<Value>, total: &mut usize, threads: &mut HashMap<String, usize>, d: &ListDelta) {
    let had = emails.len();
    emails.retain(|m| !d.removed.iter().any(|r| m["id"] == r.as_str()));
    let mut added = d.added.clone();
    added.sort_by_key(|a| a.1);
    for (id, index) in added {
        let Some(m) = d.emails.get(&id) else { continue };
        // already present (a server may report an unmoved message as added)
        if let Some(pos) = emails.iter().position(|x| x["id"] == id.as_str()) { emails.remove(pos); }
        // what is loaded is a prefix of the new list: an index past it is left to paging
        if index <= emails.len() { emails.insert(index, m.clone()); }
    }
    for m in emails.iter_mut() {
        if let Some(f) = m["id"].as_str().and_then(|id| d.emails.get(id)) {
            let account = m["__account"].clone();
            *m = f.clone();
            if !account.is_null() { m["__account"] = account; }
        }
    }
    for t in &d.destroyed_threads { threads.remove(t); }
    threads.extend(d.threads.iter().map(|(k, v)| (k.clone(), *v)));
    *total = d.total.unwrap_or_else(|| (*total + emails.len()).saturating_sub(had));
}

pub fn thread_sizes(list: &[Value]) -> HashMap<String, usize> {
    list.iter().filter_map(|t| Some((t["id"].as_str()?.to_string(), t["emailIds"].as_array()?.len()))).collect()
}

/// An event of the push stream.
#[derive(Debug, Clone, PartialEq)]
pub enum PushEvent {
    /// The stream is connected.
    Open,
    /// A keep-alive (`event: ping`).
    Ping,
    /// A StateChange object (RFC 8620 §7.1).
    State(Value),
    /// Any other event (Stalwart's `calendarAlert`, ...).
    Other(String, Value),
}

/// Line-by-line text/event-stream parser (WHATWG server-sent events; RFC 8620 §7.3).
#[derive(Default)]
pub struct SseParser { event: String, data: String }

impl SseParser {
    pub fn line(&mut self, line: &str) -> Option<PushEvent> {
        let line = line.strip_suffix('\r').unwrap_or(line);
        if line.is_empty() {
            let (event, data) = (std::mem::take(&mut self.event), std::mem::take(&mut self.data));
            if event.is_empty() && data.is_empty() { return None; }
            let v: Value = serde_json::from_str(&data).unwrap_or(Value::Null);
            return Some(match event.as_str() {
                "ping" => PushEvent::Ping,
                "state" => PushEvent::State(v),
                "" if v["@type"] == "StateChange" => PushEvent::State(v),
                _ => PushEvent::Other(if event.is_empty() { "message".into() } else { event }, v),
            });
        }
        if line.starts_with(':') { return None; }
        let (field, value) = match line.split_once(':') {
            Some((f, v)) => (f, v.strip_prefix(' ').unwrap_or(v)),
            None => (line, ""),
        };
        match field {
            "event" => self.event = value.to_string(),
            "data" => { if !self.data.is_empty() { self.data.push('\n'); } self.data.push_str(value); }
            _ => {}
        }
        None
    }
}

/// The types (with their new state) a StateChange reports for one account.
pub fn changed_types(change: &Value, account: &str) -> HashMap<String, String> {
    change["changed"][account].as_object().map(|o| o.iter().filter_map(|(k, v)| Some((k.clone(), v.as_str()?.to_string()))).collect()).unwrap_or_default()
}

/// Percent-encode a URI template value (RFC 6570 level 1, as RFC 8620 §6.2 asks).
pub fn pct(s: &str) -> String {
    s.bytes().map(|b| match b {
        b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'~' => (b as char).to_string(),
        _ => format!("%{b:02X}"),
    }).collect()
}

/// A keyword as RFC 8621 §4.1.1 allows it: 1-255 of %x21-7E without `( ) { ] % * " \`.
pub fn valid_keyword(k: &str) -> bool {
    !k.is_empty() && k.len() <= 255 && k.bytes().all(|b| (0x21..=0x7e).contains(&b) && !b"(){]%*\"\\".contains(&b))
}

/// The patch path of a keyword (RFC 8620 §5.3: a JSON Pointer, `~` and `/` escaped); keywords
/// are case-insensitive and sent lower-case.
pub fn keyword_path(k: &str) -> String {
    format!("keywords/{}", k.to_lowercase().replace('~', "~0").replace('/', "~1"))
}

pub const MAILBOX_PROPERTIES: [&str; 11] = ["id", "name", "parentId", "role", "sortOrder", "totalEmails", "unreadEmails",
    "totalThreads", "unreadThreads", "myRights", "isSubscribed"];

pub const EMAIL_LIST_PROPERTIES: [&str; 16] = ["id", "threadId", "mailboxIds", "keywords", "size", "receivedAt", "sentAt", "attachments",
    "from", "to", "cc", "subject", "preview", "hasAttachment", "blobId", "header:Authentication-Results:all"];

pub const EMAIL_FULL_PROPERTIES: [&str; 25] = ["id", "threadId", "mailboxIds", "keywords", "size", "receivedAt", "sentAt",
    "from", "to", "cc", "bcc", "replyTo", "sender", "subject", "messageId", "inReplyTo", "references",
    "textBody", "htmlBody", "attachments", "bodyValues", "hasAttachment", "preview", "headers", "blobId"];

pub const BODY_PROPERTIES: [&str; 9] = ["partId", "blobId", "size", "name", "type", "charset", "cid", "disposition", "language"];

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reschedule_fallback_order() {
        use RescheduleFallback::*;
        assert_eq!(reschedule_fallbacks(Some(10_000), 0), vec![Recreate(10_000), RestoreDraft]);
        // the old time has passed (or is about to): straight back to Drafts
        assert_eq!(reschedule_fallbacks(Some(10_000), 10_000), vec![RestoreDraft]);
        assert_eq!(reschedule_fallbacks(None, 0), vec![RestoreDraft]);
    }

    #[test]
    fn from_override_header_identity_and_envelope() {
        let ident = json!({"id": "i1", "name": "Me", "email": "me@a.com"});
        let o = Outgoing { from: "me@a.com".into(), ..Default::default() };
        assert_eq!(header_from(&o, &ident, "me@a.com"), json!({"name": "Me", "email": "me@a.com"}));
        let o2 = Outgoing { from_override: Some(("Sales".into(), " sales@a.com ".into())), ..o.clone() };
        assert_eq!(header_from(&o2, &ident, "me@a.com"), json!({"name": "Sales", "email": "sales@a.com"}));
        let o3 = Outgoing { from_override: Some(("".into(), "sales@a.com".into())), ..o.clone() };
        assert_eq!(header_from(&o3, &ident, "me@a.com"), json!({"email": "sales@a.com"}));
        // an override without an address is ignored; a wildcard identity falls back to the login
        let o4 = Outgoing { from_override: Some(("X".into(), "nope".into())), ..o.clone() };
        assert_eq!(header_from(&o4, &ident, "me@a.com")["email"], "me@a.com");
        assert_eq!(header_from(&o, &json!({"email": "*@a.com"}), "login@a.com"), json!({"email": "login@a.com"}));

        assert!(wildcard_covers("*@A.com", "x@a.com"));
        assert!(!wildcard_covers("*@a.com", "x@b.com"));
        assert!(!wildcard_covers("me@a.com", "x@a.com"));
        let ids = vec![json!({"id": "1", "email": "me@a.com"}), json!({"id": "2", "email": "*@b.org"}), json!({"id": "3", "email": "info@a.com"})];
        assert_eq!(choose_identity(&ids, "me@a.com", Some("info@a.com")).unwrap()["id"], "3");
        assert_eq!(choose_identity(&ids, "me@a.com", Some("sales@a.com")).unwrap()["id"], "1");
        assert_eq!(choose_identity(&ids, "", Some("x@b.org")).unwrap()["id"], "2");
        assert_eq!(choose_identity(&ids, "3", None).unwrap()["id"], "3");
        assert_eq!(choose_identity(&ids, "", None).unwrap()["id"], "1");
        // an override no identity covers, with no composer identity: refused, not the first
        assert!(choose_identity(&ids, "", Some("x@other.net")).is_none());
        assert!(choose_identity(&ids, "gone@a.com", Some("x@other.net")).is_none());
        // an override without an address is ignored
        assert_eq!(choose_identity(&ids, "", Some("nope")).unwrap()["id"], "1");

        let r = vec!["t@x.y".to_string()];
        assert_eq!(submission_envelope("me@a.com", "ME@a.com", None, &r), None);
        let e = submission_envelope("sales@a.com", "me@a.com", None, &r).unwrap();
        assert_eq!(e["mailFrom"], json!({"email": "sales@a.com"}));
        assert_eq!(e["rcptTo"], json!([{"email": "t@x.y"}]));
        let h = submission_envelope("me@a.com", "me@a.com", Some(5), &r).unwrap();
        assert_eq!(h["mailFrom"]["parameters"]["HOLDFOR"], "5");
        assert!(mail_from_refused(&json!({"type": "forbiddenFrom"})));
        assert!(mail_from_refused(&json!({"type": "forbiddenMailFrom"})));
        assert!(!mail_from_refused(&json!({"type": "overQuota"})));
    }

    fn ids(v: &[Value]) -> Vec<&str> { v.iter().map(|m| m["id"].as_str().unwrap()).collect() }
    fn em(id: &str) -> Value { json!({"id": id, "threadId": format!("t{id}"), "keywords": {}}) }

    #[test]
    fn stalwart_state_round_trip() {
        for id in [0u64, 1, 31, 127, 128, 300, 16_383, 16_384, 1 << 40, u64::MAX] {
            let s = stalwart_state(id);
            assert!(s.starts_with('s'));
            assert_eq!(stalwart_change_id(&s), Some(id), "{s}");
        }
        // vectors captured from Stalwart (Bulwark lib/__tests__/stalwart-state.test.ts)
        for (s, id) in [("sae", 1u64), ("sai", 2), ("saa", 0), ("sd2", 31), ("sp2", 127), ("sqaaq", 128), ("svqba", 300),
                        ("s303q", 16383), ("sqcaac", 16384), ("syccd7", 1_000_000), ("sz9bpcbi", 12_345_678), ("s33333333333333yb", u64::MAX)] {
            assert_eq!(stalwart_change_id(s), Some(id), "{s}");
            assert_eq!(stalwart_state(id), s);
        }
        assert_eq!(stalwart_change_id("n"), None);
        assert_eq!(stalwart_change_id("rabc"), None);
        assert_eq!(stalwart_change_id("s"), None);
        assert_eq!(stalwart_change_id("sXYZ"), None);
        // a non-canonical encoding is not a state Stalwart wrote
        assert_eq!(stalwart_change_id(&format!("{}a", stalwart_state(5))), None);
    }

    #[test]
    fn went_back_only_for_lower_stalwart_ids() {
        assert!(went_back(&stalwart_state(500), &stalwart_state(20)));
        assert!(!went_back(&stalwart_state(20), &stalwart_state(500)));
        assert!(!went_back(&stalwart_state(20), &stalwart_state(20)));
        assert!(!went_back("opaque-1", "opaque-0"));
    }

    #[test]
    fn parse_changes_reads_response() {
        let r = json!(["Mailbox/changes", {"accountId": "a", "oldState": "1", "newState": "2", "hasMoreChanges": false,
            "created": ["x"], "updated": ["y"], "destroyed": ["z"], "updatedProperties": ["totalEmails", "unreadEmails"]}, "c"]);
        let c = parse_changes(&r, "1", false).unwrap();
        assert_eq!((c.created, c.updated, c.destroyed), (vec!["x".to_string()], vec!["y".to_string()], vec!["z".to_string()]));
        assert_eq!(c.new_state, "2");
        assert_eq!(c.updated_properties.unwrap().len(), 2);
        // cannotCalculateChanges and friends: no delta
        assert!(parse_changes(&json!(["error", {"type": "cannotCalculateChanges"}, "c"]), "1", false).is_none());
        // a Stalwart state that went backwards: no delta
        let back = json!(["Email/changes", {"oldState": stalwart_state(9), "newState": stalwart_state(3)}, "c"]);
        assert!(parse_changes(&back, &stalwart_state(9), true).is_none());
        assert!(parse_changes(&back, &stalwart_state(9), false).is_some());
    }

    #[test]
    fn object_delta_merges_partial_updates() {
        let have = vec![json!({"id": "a", "name": "Inbox", "unreadEmails": 1}), json!({"id": "b", "name": "Old"})];
        let out = apply_object_delta(&have, &["b".into()], &[json!({"id": "a", "unreadEmails": 5}), json!({"id": "c", "name": "New"})]);
        assert_eq!(ids(&out), ["a", "c"]);
        assert_eq!(out[0]["name"], "Inbox");
        assert_eq!(out[0]["unreadEmails"], 5);
    }

    #[test]
    fn list_delta_inserts_removes_and_updates() {
        let mut emails = vec![em("1"), em("2"), em("3"), em("4")];
        let (mut total, mut threads) = (10usize, HashMap::from([("t2".to_string(), 2usize)]));
        let mut fresh3 = em("3");
        fresh3["keywords"] = json!({"$seen": true});
        let d = ListDelta {
            // 2 left the list; 3 was updated (re-added at the same place); n is new at the top
            removed: vec!["2".into(), "3".into()],
            added: vec![("3".into(), 2), ("n".into(), 0)],
            emails: HashMap::from([("n".to_string(), em("n")), ("3".to_string(), fresh3)]),
            threads: HashMap::from([("tn".to_string(), 1usize)]),
            destroyed_threads: vec!["t2".into()],
            total: Some(10),
            ..Default::default()
        };
        apply_list_delta(&mut emails, &mut total, &mut threads, &d);
        assert_eq!(ids(&emails), ["n", "1", "3", "4"]);
        assert_eq!(emails[2]["keywords"]["$seen"], true);
        assert_eq!(total, 10);
        assert!(!threads.contains_key("t2") && threads["tn"] == 1);
    }

    #[test]
    fn list_delta_leaves_far_additions_to_paging() {
        let mut emails = vec![em("1"), em("2")];
        let (mut total, mut threads) = (50usize, HashMap::new());
        let d = ListDelta {
            added: vec![("x".into(), 2), ("y".into(), 40)],
            emails: HashMap::from([("x".to_string(), em("x")), ("y".to_string(), em("y"))]),
            total: Some(52),
            ..Default::default()
        };
        apply_list_delta(&mut emails, &mut total, &mut threads, &d);
        assert_eq!(ids(&emails), ["1", "2", "x"]);
        assert_eq!(total, 52);
    }

    #[test]
    fn list_delta_refreshes_rows_updated_in_place() {
        // immutable filters: queryChanges reports nothing, Email/changes the keyword change
        let mut emails = vec![em("1")];
        let mut fresh = em("1");
        fresh["keywords"] = json!({"$flagged": true});
        let d = ListDelta { emails: HashMap::from([("1".to_string(), fresh)]), ..Default::default() };
        let (mut total, mut threads) = (1usize, HashMap::new());
        apply_list_delta(&mut emails, &mut total, &mut threads, &d);
        assert_eq!(emails[0]["keywords"]["$flagged"], true);
        assert_eq!(total, 1);
    }

    #[test]
    fn calls_split_by_limit_unless_linked() {
        let c = |m: &str| json!([m, {"accountId": "a"}, "x"]);
        let calls: Vec<Value> = (0..5).map(|_| c("Email/query")).collect();
        assert_eq!(split_calls(&calls, 2).iter().map(|x| x.len()).collect::<Vec<_>>(), [2, 2, 1]);
        assert_eq!(split_calls(&calls, 16).len(), 1);
        let mut linked = calls.clone();
        linked.push(json!(["Email/get", {"#ids": {"resultOf": "x", "name": "Email/query", "path": "/ids"}}, "y"]));
        assert_eq!(split_calls(&linked, 2).len(), 1);
        let mut created = calls.clone();
        created.push(json!(["EmailSubmission/set", {"create": {"s": {"emailId": "#d"}}}, "s"]));
        assert_eq!(split_calls(&created, 2).len(), 1);
    }

    #[test]
    fn read_only_calls() {
        assert!(read_only(&json!([["Email/get", {}, "a"], ["Email/queryChanges", {}, "b"]])));
        assert!(!read_only(&json!([["Email/get", {}, "a"], ["Email/set", {}, "b"]])));
    }

    #[test]
    fn problem_details() {
        let m = problem_message(400, &json!({"type": "urn:ietf:params:jmap:error:limit", "limit": "maxCallsInRequest", "status": 400}));
        assert!(m.contains("maxCallsInRequest"));
        assert!(problem_message(400, &json!({"type": "urn:ietf:params:jmap:error:unknownCapability"})).contains("capability"));
        assert!(problem_message(500, &json!({})).contains("500"));
    }

    #[test]
    fn sse_parser_events() {
        let mut p = SseParser::default();
        let mut out = vec![];
        for l in ["event: state", "data: {\"@type\":\"StateChange\",", "data: \"changed\":{\"a\":{\"Email\":\"s1\"}}}", "",
                  ": comment", "event: ping", "data: {\"interval\": 30000}", "", "", "event: calendarAlert", "data: {}", "",
                  "data: {\"@type\":\"StateChange\",\"changed\":{}}", "\r"] {
            if let Some(e) = p.line(l) { out.push(e); }
        }
        assert_eq!(out.len(), 4);
        match &out[0] { PushEvent::State(v) => assert_eq!(changed_types(v, "a")["Email"], "s1"), e => panic!("{e:?}") }
        assert_eq!(out[1], PushEvent::Ping);
        assert!(matches!(&out[2], PushEvent::Other(n, _) if n == "calendarAlert"));
        assert!(matches!(&out[3], PushEvent::State(_)));
        assert!(changed_types(&json!({"changed": {"b": {"Email": "x"}}}), "a").is_empty());
    }

    #[test]
    fn keywords() {
        assert!(valid_keyword("$label:red") && valid_keyword("$seen"));
        assert!(!valid_keyword("") && !valid_keyword("a b") && !valid_keyword("a(b") && !valid_keyword("ü") && !valid_keyword("a]"));
        assert_eq!(keyword_path("A/b~c"), "keywords/a~1b~0c");
        assert_eq!(pct("a b/ü"), "a%20b%2F%C3%BC");
    }

    #[test]
    fn quota_prefers_mail_account_octets() {
        let list = vec![
            json!({"id": "1", "resourceType": "count", "scope": "account", "used": 1, "hardLimit": 9}),
            json!({"id": "2", "resourceType": "octets", "scope": "domain", "types": ["Mail"], "used": 1, "hardLimit": 10}),
            json!({"id": "3", "resourceType": "octets", "scope": "account", "types": ["Mail", "Calendar"], "used": 5, "hardLimit": 1000}),
            json!({"id": "4", "resourceType": "octets", "scope": "account", "types": ["Contact"], "used": 0, "hardLimit": 2}),
        ];
        assert_eq!(pick_quota(&list).unwrap()["id"], "3");
        assert_eq!(pick_quota(&list[3..]).unwrap()["id"], "4");
        assert!(pick_quota(&list[..1]).is_none());
    }

    #[test]
    fn addresses() {
        let a = parse_addresses("\"Doe, Jane\" <jane@x.org>; bob@y.org, <c@z.org>, nobody");
        assert_eq!(a.len(), 3);
        assert_eq!(a[0]["name"], "Doe, Jane");
        assert_eq!(a[2]["email"], "c@z.org");
    }

    #[test]
    fn delayed_send_needs_futurerelease() {
        assert_eq!(delayed_send_limit(&json!({"maxDelayedSend": 600}), false), 0);
        assert_eq!(delayed_send_limit(&json!({"maxDelayedSend": 600, "submissionExtensions": {}}), false), 0);
        assert_eq!(delayed_send_limit(&json!({"maxDelayedSend": 600, "submissionExtensions": {"FUTURERELEASE": []}}), false), 600);
        // scheduled send: seconds left, rounded up, within the limit
        assert_eq!(scheduled_hold(10_500, 0, 600), Ok(11));
        assert!(scheduled_hold(0, 10, 600).is_err());
        assert!(scheduled_hold(700_000, 0, 600).is_err());
        assert!(scheduled_hold(10_000, 0, 0).is_err());
        assert_eq!(delayed_send_limit(&json!({"maxDelayedSend": 600, "submissionExtensions": ["futurerelease"]}), false), 600);
        assert_eq!(delayed_send_limit(&json!({"maxDelayedSend": 2592000, "submissionExtensions": {"FUTURERELEASE": []}}), true), 7 * 86400);
    }

    #[test]
    fn counts_only_update_of_unknown_folder_fetches_it_whole() {
        let have = vec![json!({"id": "a", "name": "Inbox"})];
        let (a, b) = (String::from("a"), String::from("z"));
        let (part, full) = split_known(&[&a, &b], &have, true);
        assert_eq!(part, vec![json!("a")]);
        assert_eq!(full, vec![json!("z")]);
        let (part, full) = split_known(&[&a, &b], &have, false);
        assert_eq!(part.len(), 2);
        assert!(full.is_empty());
    }
}
