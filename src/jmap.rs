use base64::Engine;
use serde_json::{json, Value};
use serde_json::Map;
use std::collections::HashMap;

pub struct Client {
    http: reqwest::blocking::Client,
    auth: std::sync::Mutex<String>,
    oauth: Option<std::sync::Mutex<crate::oauth::Tokens>>,
    api: String,
    pub base: String,
    pub account: String,
    pub username: String,
    pub caps: Vec<String>,
    pub session: Value,
}

type R<T> = Result<T, String>;

fn e<E: std::fmt::Display>(x: E) -> String {
    x.to_string()
}

pub fn normalize_base(server: &str) -> String {
    let mut base = server.trim().trim_end_matches('/').to_string();
    if let Some(i) = base.find("/.well-known/jmap") { base.truncate(i); }
    if !base.starts_with("http") {
        base = format!("https://{base}");
    }
    base
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
        let resp = http.get(format!("{base}/.well-known/jmap")).header("Authorization", &auth).send().map_err(e)?;
        if resp.status() == 401 {
            return Err(if oauth.is_some() { "The server did not accept the sign-in token.".into() } else { "Wrong username, password or 2FA code".into() });
        }
        if !resp.status().is_success() {
            return Err(format!("Server returned {}", resp.status()));
        }
        let s: Value = resp.json().map_err(e)?;
        let mut api = s["apiUrl"].as_str().ok_or("No apiUrl in session")?.to_string();
        if api.starts_with('/') {
            let origin: String = base.splitn(4, '/').take(3).collect::<Vec<_>>().join("/");
            api = format!("{origin}{api}");
        }
        let account = s["primaryAccounts"]["urn:ietf:params:jmap:mail"]
            .as_str()
            .ok_or("Account has no mail capability")?
            .to_string();
        let username = s["username"].as_str().unwrap_or(user).to_string();
        let caps = s["capabilities"].as_object().map(|o| o.keys().cloned().collect()).unwrap_or_default();
        Ok(Client {
            http, auth: std::sync::Mutex::new(auth), oauth: oauth.map(std::sync::Mutex::new),
            api, base: base.to_string(), account, username, caps, session: s,
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

    pub fn has(&self, cap: &str) -> bool {
        self.caps.iter().any(|c| c == cap)
    }

    // ------------------------------------------------------------------
    // Core request (RFC 8620 §3)
    // ------------------------------------------------------------------

    /// Send method calls; returns the raw `methodResponses`. Method-level
    /// `error` responses are returned to the caller, not raised (see [`ok`]).
    pub fn request(&self, calls: Value) -> R<Vec<Value>> {
        let body = json!({ "using": self.caps, "methodCalls": calls });
        if let Some(m) = &self.oauth {
            let due = m.lock().unwrap().expires.is_some_and(|x| x <= std::time::Instant::now());
            if due { let _ = self.renew(); }
        }
        let send = || {
            let auth = self.auth.lock().unwrap().clone();
            self.http.post(&self.api).header("Authorization", auth).json(&body).send().map_err(e)
        };
        let mut resp = send()?;
        if resp.status() == 401 && self.oauth.is_some() {
            self.renew()?;
            resp = send()?;
        }
        match resp.status().as_u16() {
            401 => return Err("Session expired; sign in again.".into()),
            429 => return Err("The server is rate limiting requests; try again shortly.".into()),
            s if s >= 400 => {
                // RFC 8620 §3.6.1 request-level problem details
                let v: Value = resp.json().unwrap_or(json!({}));
                return Err(format!("Request failed ({s}): {}", v["detail"].as_str().or(v["type"].as_str()).unwrap_or("")));
            }
            _ => {}
        }
        let v: Value = resp.json().map_err(e)?;
        v["methodResponses"].as_array().cloned().ok_or_else(|| "Bad JMAP response".into())
    }

    /// Request with an explicit `using` list (e.g. Stalwart's own `urn:stalwart:jmap` objects).
    pub fn request_using(&self, using: &[&str], calls: Value) -> R<Vec<Value>> {
        let body = json!({ "using": using, "methodCalls": calls });
        let auth = self.auth.lock().unwrap().clone();
        let resp = self.http.post(&self.api).header("Authorization", auth).json(&body).send().map_err(e)?;
        if !resp.status().is_success() { return Err(format!("Request failed ({})", resp.status())); }
        let v: Value = resp.json().map_err(e)?;
        v["methodResponses"].as_array().cloned().ok_or_else(|| "Bad JMAP response".into())
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

    pub fn account_for(&self, cap: &str) -> String {
        self.session["primaryAccounts"][cap].as_str().map(String::from).unwrap_or_else(|| self.account.clone())
    }

    fn limit(&self, name: &str, default: u64) -> usize {
        self.session["capabilities"]["urn:ietf:params:jmap:core"][name].as_u64().unwrap_or(default) as usize
    }

    pub fn max_get(&self) -> usize { self.limit("maxObjectsInGet", 500).max(1) }
    pub fn max_set(&self) -> usize { self.limit("maxObjectsInSet", 500).max(1) }
    pub fn max_upload(&self) -> usize { self.limit("maxSizeUpload", 50_000_000) }

    fn url_template(&self, key: &str) -> Option<String> {
        let mut u = self.session[key].as_str()?.to_string();
        if u.starts_with('/') {
            let origin: String = self.base.splitn(4, '/').take(3).collect::<Vec<_>>().join("/");
            u = format!("{origin}{u}");
        }
        Some(u)
    }

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

    // ------------------------------------------------------------------
    // Mailboxes (RFC 8621 §2)
    // ------------------------------------------------------------------

    pub fn mailboxes(&self) -> R<Vec<Value>> {
        let r = self.call(json!([["Mailbox/get", {"accountId": self.account, "ids": null, "properties": MAILBOX_PROPERTIES}, "0"]]))?;
        let mut list = r[0][1]["list"].as_array().cloned().unwrap_or_default();
        // Stalwart may cut ids:null at maxObjectsInGet: page through Mailbox/query then.
        if list.len() >= self.max_get() {
            let q = self.call(json!([["Mailbox/query", {"accountId": self.account}, "q"]]))?;
            let ids: Vec<Value> = q[0][1]["ids"].as_array().cloned().unwrap_or_default();
            list.clear();
            for chunk in ids.chunks(self.max_get()) {
                let r = self.call(json!([["Mailbox/get", {"accountId": self.account, "ids": chunk, "properties": MAILBOX_PROPERTIES}, "0"]]))?;
                list.extend(r[0][1]["list"].as_array().cloned().unwrap_or_default());
            }
        }
        Ok(list)
    }

    pub fn role_ids(&self) -> R<HashMap<String, String>> {
        Ok(self.mailboxes()?.iter().filter_map(|m| Some((m["role"].as_str()?.to_string(), m["id"].as_str()?.to_string()))).collect())
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

    /// One page of a list. Returns (emails, total, thread sizes by thread id, query state).
    pub fn list(&self, filter: Value, position: usize, limit: usize) -> R<(Vec<Value>, usize, HashMap<String, usize>)> {
        let r = self.call(json!([
            ["Email/query", {
                "accountId": self.account, "filter": filter,
                "sort": [{"property": "receivedAt", "isAscending": false}],
                "position": position, "limit": limit, "calculateTotal": true
            }, "0"],
            ["Email/get", {
                "accountId": self.account,
                "#ids": {"resultOf": "0", "name": "Email/query", "path": "/ids"},
                "properties": EMAIL_LIST_PROPERTIES
            }, "1"],
            ["Thread/get", {
                "accountId": self.account,
                "#ids": {"resultOf": "1", "name": "Email/get", "path": "/list/*/threadId"}
            }, "2"]
        ]))?;
        let total = r[0][1]["total"].as_u64().unwrap_or(0) as usize;
        let emails = r[1][1]["list"].as_array().cloned().unwrap_or_default();
        let threads = r[2][1]["list"].as_array().map(|l| l.iter().filter_map(|t| {
            Some((t["id"].as_str()?.to_string(), t["emailIds"].as_array()?.len()))
        }).collect()).unwrap_or_default();
        Ok((emails, total, threads))
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
            "fetchAllBodyValues": true, "maxBodyValueBytes": 2_000_000
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
        let key = format!("keywords/{}", keyword.replace('~', "~0").replace('/', "~1"));
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
                created_id(&r[0][1], "a")?
            }
            None => return Err(format!("This account has no {role} folder.")),
        };
        self.move_to(ids, &t)
    }

    pub fn supports_delayed_send(&self) -> bool {
        self.session["capabilities"]["urn:ietf:params:jmap:submission"]["maxDelayedSend"].as_u64().unwrap_or(0) > 0
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
        let r = self.call(json!([["Email/get", {"accountId": self.account, "ids": ids, "properties": EMAIL_LIST_PROPERTIES}, "e"]]))?;
        let mut emails = r[0][1]["list"].as_array().cloned().unwrap_or_default();
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
        let enc = |s: &str| s.bytes().map(|b| match b {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'~' => (b as char).to_string(),
            _ => format!("%{b:02X}"),
        }).collect::<String>();
        let url = t.replace("{accountId}", &enc(account)).replace("{blobId}", &enc(blob_id))
            .replace("{name}", &enc(if name.is_empty() { "file" } else { name }))
            .replace("{type}", &enc(if mime.is_empty() { "application/octet-stream" } else { mime }));
        let auth = self.auth.lock().unwrap().clone();
        let mut resp = self.http.get(&url).header("Authorization", auth).send().map_err(e)?;
        if resp.status() == 401 && self.oauth.is_some() {
            self.renew()?;
            let auth = self.auth.lock().unwrap().clone();
            resp = self.http.get(&url).header("Authorization", auth).send().map_err(e)?;
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
        let url = t.replace("{accountId}", account);
        let len = data.len();
        let auth = self.auth.lock().unwrap().clone();
        let resp = self.http.post(&url).header("Authorization", auth).header("Content-Type", mime).body(data).send().map_err(e)?;
        if !resp.status().is_success() { return Err(format!("Upload failed ({})", resp.status())); }
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
        for k in &o.keywords { kw[k.as_str()] = json!(true); }
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
            json!({"type": "multipart/alternative", "subParts": [text_part, {"partId": "h", "type": "text/html"}]})
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
            let max = self.session["capabilities"]["urn:ietf:params:jmap:submission"]["maxDelayedSend"].as_u64().unwrap_or(0);
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

    /// Undo a held submission (RFC 8621 §7.5 undoStatus).
    pub fn cancel_submission(&self, id: &str) -> R<()> {
        let acc = self.account_for("urn:ietf:params:jmap:submission");
        let (_, bad) = self.update("EmailSubmission/set", &acc, vec![(id.into(), json!({"undoStatus": "canceled"}))])?;
        if bad.is_empty() { Ok(()) } else { Err(bad.join("; ")) }
    }

    // ------------------------------------------------------------------
    // Push (RFC 8620 §7.3 EventSource)
    // ------------------------------------------------------------------

    /// Blocks reading the event stream; calls `on_change` with each StateChange.
    pub fn watch(&self, on_change: &dyn Fn(&Value)) -> R<()> {
        let t = self.url_template("eventSourceUrl").ok_or("No push support.")?;
        let url = t.replace("{types}", "*").replace("{closeafter}", "no").replace("{ping}", "60");
        let auth = self.auth.lock().unwrap().clone();
        let client = reqwest::blocking::Client::builder().timeout(None).build().map_err(e)?;
        let resp = client.get(&url).header("Authorization", auth).header("Accept", "text/event-stream").send().map_err(e)?;
        if !resp.status().is_success() { return Err(format!("Push failed ({})", resp.status())); }
        let reader = std::io::BufReader::new(resp);
        let mut event = String::new();
        let mut data = String::new();
        for line in std::io::BufRead::lines(reader) {
            let line = line.map_err(e)?;
            if line.is_empty() {
                if event == "state" || (event.is_empty() && data.contains("StateChange")) {
                    if let Ok(v) = serde_json::from_str::<Value>(&data) { on_change(&v); }
                }
                event.clear();
                data.clear();
            } else if let Some(x) = line.strip_prefix("event:") {
                event = x.trim().to_string();
            } else if let Some(x) = line.strip_prefix("data:") {
                if !data.is_empty() { data.push('\n'); }
                data.push_str(x.trim_start());
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
        let q = r[0][1]["list"].as_array().and_then(|l| l.iter().find(|q| q["resourceType"] == "octets").cloned());
        Ok(q.map(|q| (q["used"].as_u64().unwrap_or(0), q["hardLimit"].as_u64().unwrap_or(0))))
    }
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
        _ => if d.is_empty() { format!("Server error: {t}") } else { format!("{t}: {d}") },
    }
}

pub fn set_error(err: &Value) -> String {
    let t = err["type"].as_str().unwrap_or("error");
    let d = err["description"].as_str().unwrap_or("");
    let props = err["properties"].as_array().map(|a| a.iter().filter_map(|p| p.as_str()).collect::<Vec<_>>().join(", ")).unwrap_or_default();
    match t {
        "overQuota" => "Your mailbox is full.".into(),
        "tooLarge" => "The message is too large.".into(),
        "forbiddenFrom" => "You may not send from that address.".into(),
        "invalidEmail" => format!("The message is invalid{}.", if d.is_empty() { String::new() } else { format!(": {d}") }),
        "tooManyRecipients" => "Too many recipients.".into(),
        "noRecipients" => "Add at least one recipient.".into(),
        "invalidRecipients" => format!("Some recipients are invalid{}.", if d.is_empty() { String::new() } else { format!(": {d}") }),
        "mailboxHasChild" => "Delete or move its subfolders first.".into(),
        "mailboxHasEmail" => "The folder is not empty.".into(),
        "alreadyExists" => "That already exists.".into(),
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

pub const MAILBOX_PROPERTIES: [&str; 11] = ["id", "name", "parentId", "role", "sortOrder", "totalEmails", "unreadEmails",
    "totalThreads", "unreadThreads", "myRights", "isSubscribed"];

pub const EMAIL_LIST_PROPERTIES: [&str; 16] = ["id", "threadId", "mailboxIds", "keywords", "size", "receivedAt", "sentAt", "attachments",
    "from", "to", "cc", "subject", "preview", "hasAttachment", "blobId", "header:Authentication-Results:all"];

pub const EMAIL_FULL_PROPERTIES: [&str; 25] = ["id", "threadId", "mailboxIds", "keywords", "size", "receivedAt", "sentAt",
    "from", "to", "cc", "bcc", "replyTo", "sender", "subject", "messageId", "inReplyTo", "references",
    "textBody", "htmlBody", "attachments", "bodyValues", "hasAttachment", "preview", "headers", "blobId"];

pub const BODY_PROPERTIES: [&str; 9] = ["partId", "blobId", "size", "name", "type", "charset", "cid", "disposition", "language"];
