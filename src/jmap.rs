use base64::Engine;
use serde_json::{json, Value};
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
            api, base: base.to_string(), account, username, caps,
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

    pub fn call(&self, calls: Value) -> R<Vec<Value>> {
        let body = json!({
            "using": self.caps,
            "methodCalls": calls
        });
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
        if resp.status() == 401 {
            return Err("Session expired; sign in again.".into());
        }
        let v: Value = resp.json().map_err(e)?;
        let out = v["methodResponses"].as_array().cloned().ok_or("Bad JMAP response")?;
        for r in &out {
            if r[0] == "error" {
                return Err(format!("JMAP error: {}", r[1]));
            }
        }
        Ok(out)
    }

    pub fn mailboxes(&self) -> R<Vec<Value>> {
        let r = self.call(json!([["Mailbox/get", {"accountId": self.account, "ids": null}, "0"]]))?;
        Ok(r[0][1]["list"].as_array().cloned().unwrap_or_default())
    }

    pub fn emails(&self, mailbox: Option<&str>, text: Option<&str>) -> R<Vec<Value>> {
        let mut filter = serde_json::Map::new();
        if let Some(m) = mailbox {
            filter.insert("inMailbox".into(), json!(m));
        }
        if let Some(t) = text {
            filter.insert("text".into(), json!(t));
        }
        let r = self.call(json!([
            ["Email/query", {
                "accountId": self.account,
                "filter": filter,
                "sort": [{"property": "receivedAt", "isAscending": false}],
                "limit": 100
            }, "q"],
            ["Email/get", {
                "accountId": self.account,
                "#ids": {"resultOf": "q", "name": "Email/query", "path": "/ids"},
                "properties": ["from", "subject", "receivedAt", "preview", "hasAttachment", "keywords"]
            }, "g"]
        ]))?;
        Ok(r[1][1]["list"].as_array().cloned().unwrap_or_default())
    }

    pub fn email_body(&self, id: &str) -> R<Value> {
        let r = self.call(json!([
            ["Email/get", {
                "accountId": self.account,
                "ids": [id],
                "properties": ["from", "to", "subject", "receivedAt", "textBody", "htmlBody", "bodyValues", "messageId", "headers"],
                "fetchAllBodyValues": true
            }, "g"],
            ["Email/set", {
                "accountId": self.account,
                "update": { id: {"keywords/$seen": true} }
            }, "s"]
        ]))?;
        Ok(r[0][1]["list"][0].clone())
    }

    fn role_ids(&self) -> R<HashMap<String, String>> {
        Ok(self.mailboxes()?.iter().filter_map(|m| Some((m["role"].as_str()?.to_string(), m["id"].as_str()?.to_string()))).collect())
    }

    pub fn trash(&self, id: &str) -> R<()> {
        let roles = self.role_ids()?;
        let t = roles.get("trash").ok_or("No Trash folder")?;
        self.call(json!([["Email/set", {"accountId": self.account, "update": { id: {"mailboxIds": { t: true }} }}, "t"]]))?;
        Ok(())
    }

    pub fn send(&self, to: &str, subject: &str, body: &str, in_reply_to: Option<&str>) -> R<()> {
        let r = self.call(json!([["Identity/get", {"accountId": self.account, "ids": null}, "i"]]))?;
        let ident = r[0][1]["list"][0].clone();
        let ident_id = ident["id"].as_str().ok_or("No sending identity on this account")?;
        let roles = self.role_ids()?;
        let drafts = roles.get("drafts").ok_or("No Drafts folder")?;
        let sent = roles.get("sent").ok_or("No Sent folder")?;
        let rcpts: Vec<Value> = to.split([',', ';']).map(str::trim).filter(|s| !s.is_empty()).map(|e| json!({"email": e})).collect();
        if rcpts.is_empty() { return Err("Add at least one recipient".into()); }
        let mut email = json!({
            "mailboxIds": { drafts: true },
            "keywords": {"$draft": true, "$seen": true},
            "from": [{"name": ident["name"], "email": ident["email"]}],
            "to": rcpts,
            "subject": subject,
            "bodyValues": {"b": {"value": body}},
            "textBody": [{"partId": "b", "type": "text/plain"}]
        });
        if let Some(m) = in_reply_to {
            email["inReplyTo"] = json!([m]);
            email["references"] = json!([m]);
        }
        self.call(json!([
            ["Email/set", {"accountId": self.account, "create": {"d": email}}, "e"],
            ["EmailSubmission/set", {
                "accountId": self.account,
                "create": {"s": {"emailId": "#d", "identityId": ident_id}},
                "onSuccessUpdateEmail": {"#s": {
                    format!("mailboxIds/{drafts}"): null,
                    format!("mailboxIds/{sent}"): true,
                    "keywords/$draft": null
                }}
            }, "s"]
        ]))?;
        Ok(())
    }
}
