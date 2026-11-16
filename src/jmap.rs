use base64::Engine;
use serde_json::{json, Value};
use serde_json::Map;
use std::collections::HashMap;

pub struct Client {
    http: reqwest::blocking::Client,
    auth: std::sync::Mutex<String>,
    oauth: Option<std::sync::Mutex<crate::oauth::Tokens>>,
    pub base: String,
    pub account: String,
    pub username: String,
    pub caps: Vec<String>,
    /// The session as first fetched (RFC 8620 §2).
    pub session: Value,
    /// The current session: re-fetched when a response's `sessionState` differs (RFC 8620 §3.4).
    live: std::sync::RwLock<Value>,
    /// maxConcurrentRequests / maxConcurrentUpload (RFC 8620 §2).
    requests: Gate,
    uploads: Gate,
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
        let gate = |k: &str, d: u64| Gate::new(core[k].as_u64().unwrap_or(d) as usize);
        Ok(Client {
            http, auth: std::sync::Mutex::new(auth), oauth: oauth.map(std::sync::Mutex::new),
            base: base.to_string(), account, username, caps,
            requests: gate("maxConcurrentRequests", 4), uploads: gate("maxConcurrentUpload", 4),
            live: std::sync::RwLock::new(s.clone()), session: s,
            roles: std::sync::Mutex::new(None),
        })
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
            let ids = self.query_all("Mailbox/query", &self.account, json!({}))?;
            let r = self.call(json!([["Mailbox/get", {"accountId": self.account, "ids": [], "properties": ["id"]}, "s"]]))?;
            state = r[0][1]["state"].as_str().map(String::from);
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
                let fetch_upd: Vec<Value> = updated.iter().filter(|i| !gone.contains(i) && !created.contains(i)).map(|i| json!(i)).collect();
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
        let cap = self.account_cap("urn:ietf:params:jmap:submission");
        let max = cap["maxDelayedSend"].as_u64().unwrap_or(0);
        let has = |k: &str| k.eq_ignore_ascii_case("FUTURERELEASE");
        let future = match &cap["submissionExtensions"] {
            Value::Object(o) => o.keys().any(|k| has(k)),
            Value::Array(a) => a.iter().any(|k| k.as_str().is_some_and(has)),
            _ => true,
        };
        if !future { return 0; }
        if self.is_stalwart() && max == 30 * 86400 { 7 * 86400 } else { max }
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
        // big attachments: no overall deadline, only one per read (reqwest blocking semantics)
        let mut resp = self.http.get(&url).header("Authorization", self.auth_header()).timeout(std::time::Duration::from_secs(120)).send().map_err(e)?;
        if resp.status() == 401 && self.oauth.is_some() {
            self.renew()?;
            resp = self.http.get(&url).header("Authorization", self.auth_header()).timeout(std::time::Duration::from_secs(120)).send().map_err(e)?;
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
        let send = |data: Vec<u8>| self.http.post(&url).header("Authorization", self.auth_header()).header("Content-Type", mime)
            .timeout(std::time::Duration::from_secs(300)).body(data).send().map_err(e);
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
        let mut email = json!({
            "mailboxIds": { mailbox: true },
            "keywords": kw,
            "from": [{"name": ident["name"], "email": ident["email"]}],
            "to": addrs(&o.to), "subject": o.subject,
        });
        if !o.cc.trim().is_empty() { email["cc"] = json!(addrs(&o.cc)); }
        if !o.bcc.trim().is_empty() { email["bcc"] = json!(addrs(&o.bcc)); }
        if let Some(rt) = ident["replyTo"].as_array().filter(|a| !a.is_empty()) { email["replyTo"] = json!(rt); }
        if !o.in_reply_to.is_empty() { email["inReplyTo"] = json!(o.in_reply_to); }
        if !o.references.is_empty() { email["references"] = json!(o.references); }
        if o.read_receipt { email["header:Disposition-Notification-To:asAddresses"] = json!([{"email": ident["email"]}]); }
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

    fn pick_identity(&self, from: &str) -> R<Value> {
        let ids = self.identities()?;
        let f = from.trim().to_lowercase();
        ids.iter().find(|i| !f.is_empty() && i["email"].as_str().is_some_and(|e| e.to_lowercase() == f))
            .or_else(|| ids.iter().find(|i| i["id"].as_str() == Some(from)))
            .or(ids.first()).cloned().ok_or_else(|| "No sending identity on this account.".into())
    }

    /// Save or replace a draft. Returns the new draft id.
    pub fn save_draft(&self, o: &Outgoing, replace: Option<&str>) -> R<String> {
        let ident = self.pick_identity(&o.from)?;
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
        let ident = self.pick_identity(&o.from)?;
        let ident_id = ident["id"].as_str().unwrap_or("").to_string();
        let roles = self.role_ids()?;
        let drafts = roles.get("drafts").ok_or("No Drafts folder.")?.clone();
        let sent = roles.get("sent").ok_or("No Sent folder.")?.clone();
        let email = self.build_email(o, &ident, &drafts, true)?;
        let sub_acc = self.account_for("urn:ietf:params:jmap:submission");
        let mut sub = json!({"emailId": "#d", "identityId": ident_id});
        if o.hold_seconds > 0 {
            let max = self.max_delayed_send();
            if max > 0 {
                sub["envelope"] = json!({
                    "mailFrom": {"email": ident["email"], "parameters": {"HOLDFOR": o.hold_seconds.min(max).to_string()}},
                    "rcptTo": parse_addresses(&format!("{},{},{}", o.to, o.cc, o.bcc)).iter().map(|a| json!({"email": a["email"]})).collect::<Vec<_>>()
                });
            }
        }
        let mut calls = vec![
            json!(["Email/set", {"accountId": self.account, "create": {"d": email}, "destroy": o.replaces_draft.iter().collect::<Vec<_>>()}, "e"]),
            json!(["EmailSubmission/set", {
                "accountId": sub_acc,
                "create": {"s": sub},
                "onSuccessUpdateEmail": {"#s": {
                    format!("mailboxIds/{drafts}"): null,
                    format!("mailboxIds/{sent}"): true,
                    "keywords/$draft": null
                }}
            }, "s"]),
        ];
        if let Some(orig) = &o.answering {
            let kw = if o.forwarding { "$forwarded" } else { "$answered" };
            calls.push(json!(["Email/set", {"accountId": self.account, "update": { orig: { format!("keywords/{kw}"): true } }}, "a"]));
        }
        let r = self.call(Value::Array(calls))?;
        created_id(&r[0][1], "d")?;
        let sid = created_id(&r[1][1], "s")?;
        Ok((o.hold_seconds > 0).then_some(sid))
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
        let email = r[1][1]["list"][0]["emailId"].as_str().map(String::from);
        let _ = self.call(json!([["EmailSubmission/set", {"accountId": acc, "destroy": [id]}, "d"]]));
        if let (Some(eid), true) = (&email, acc == self.account) {
            if let Some(drafts) = self.role_ids()?.get("drafts") {
                let _ = self.update("Email/set", &self.account, vec![(eid.clone(), json!({"mailboxIds": { drafts: true }, "keywords/$draft": true}))]);
            }
        }
        Ok(email)
    }

    // ------------------------------------------------------------------
    // Push (RFC 8620 §7.3 EventSource)
    // ------------------------------------------------------------------

    /// Blocks reading the event stream (RFC 8620 §7.3) for the given data types, calling
    /// `on_event` with each event; it returns `false` to stop. `ping` asks the server for a
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
    pub read_receipt: bool,
    /// Extra keywords (tags) to set on the message.
    pub keywords: Vec<String>,
}

/// "Name <a@b>, c@d" -> [{name, email}]
pub fn parse_addresses(s: &str) -> Vec<Value> {
    let mut out = vec![];
    let mut cur = String::new();
    let mut quoted = false;
    let mut angle = false;
    for ch in s.chars() {
        match ch {
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
            let name = p[..a].trim().trim_matches('"').trim();
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

/// Apply a /changes result to a cached object list: drop `destroyed`, then merge each fetched
/// object into the one with its id (a partial fetch updates only its properties) or append it.
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
}
