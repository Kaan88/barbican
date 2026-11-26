//! Tests: a fake JMAP server and the [`Mail`] / [`Host`] the tools are run against.
//!
//! The fake is a state (accounts, folders, messages, identities, blobs) behind
//! [`crate::jmap::Client::fake`], so the tools make the same JMAP calls they make against a real
//! server and every call is recorded for the test to check. Nothing leaves the process.
use crate::assistant::tools::mail::{new_tag, Login, Mail};
use crate::assistant::tools::{Approve, Host};
use crate::assistant::Cancel;
use crate::tags::TagDef;
use serde_json::{json, Map, Value};
use std::collections::{HashMap, HashSet};
use std::sync::{Arc, Mutex};

#[derive(Clone, Default)]
pub struct Account {
    pub id: String,
    pub name: String,
    pub mailboxes: Vec<Value>,
    pub emails: Vec<Value>,
    pub identities: Vec<Value>,
}

#[derive(Default)]
pub struct State {
    pub accounts: Vec<Account>,
    pub blobs: HashMap<String, String>,
    pub requests: Vec<Value>,
    denied: HashSet<(String, String)>,
}

impl State {
    fn account_mut(&mut self, id: &str) -> &mut Account {
        self.accounts.iter_mut().find(|a| a.id == id).unwrap_or_else(|| panic!("fake: no account {id}"))
    }
}

/// A fake JMAP server for one or more logins.
#[derive(Clone, Default)]
pub struct Store {
    inner: Arc<Mutex<State>>,
}

fn lower(v: &Value) -> String {
    v.as_str().unwrap_or("").to_lowercase()
}

fn addresses(v: &Value) -> String {
    v.as_array()
        .map(|a| {
            a.iter()
                .map(|x| format!("{} {}", lower(&x["name"]), lower(&x["email"])))
                .collect::<Vec<_>>()
                .join(", ")
        })
        .unwrap_or_default()
}

fn body_of(e: &Value) -> String {
    let mut out = String::new();
    for part in e["textBody"].as_array().into_iter().flatten() {
        if let Some(v) = part["partId"].as_str().and_then(|p| e["bodyValues"][p]["value"].as_str()) {
            out.push_str(v);
        }
    }
    out.to_lowercase()
}

fn date_of(e: &Value) -> String {
    e["receivedAt"].as_str().or_else(|| e["sentAt"].as_str()).unwrap_or("").to_string()
}

/// `Email/query`'s filter: one condition, or an operator over conditions.
fn matches(e: &Value, f: &Value) -> bool {
    if let Some(conds) = f["conditions"].as_array() {
        let any = f["operator"].as_str() == Some("OR");
        return if any { conds.iter().any(|c| matches(e, c)) } else { conds.iter().all(|c| matches(e, c)) };
    }
    for (k, v) in f.as_object().into_iter().flatten() {
        let ok = match k.as_str() {
            "text" => {
                let s = lower(v);
                lower(&e["subject"]).contains(&s) || body_of(e).contains(&s) || addresses(&e["from"]).contains(&s)
            }
            "body" => body_of(e).contains(&lower(v)),
            "from" => addresses(&e["from"]).contains(&lower(v)),
            "to" => addresses(&e["to"]).contains(&lower(v)),
            "subject" => lower(&e["subject"]).contains(&lower(v)),
            "hasKeyword" => e["keywords"][v.as_str().unwrap_or("")].as_bool() == Some(true),
            "notKeyword" => e["keywords"][v.as_str().unwrap_or("")].as_bool() != Some(true),
            "inMailbox" => e["mailboxIds"][v.as_str().unwrap_or("")].as_bool() == Some(true),
            "after" => date_of(e) >= lower(v),
            "before" => date_of(e) < lower(v),
            "hasAttachment" => e["attachments"].as_array().map(|a| !a.is_empty()).unwrap_or(false) == v.as_bool().unwrap_or(true),
            other => panic!("fake: the filter {other} is not implemented"),
        };
        if !ok {
            return false;
        }
    }
    true
}

/// A JSON Pointer patch path (`keywords/$label:red`, `mailboxIds/x~1y`) as its two parts.
fn pointer(path: &str) -> Vec<String> {
    path.split('/').map(|p| p.replace("~1", "/").replace("~0", "~")).collect()
}

fn patch(e: &mut Value, path: &str, value: &Value) {
    let parts = pointer(path);
    match parts.as_slice() {
        [a] => {
            if value.is_null() {
                e.as_object_mut().unwrap().remove(a);
            } else {
                e[a] = value.clone();
            }
        }
        [a, b] => {
            if e[a].is_null() {
                e[a] = Value::Object(Map::new());
            }
            if value.is_null() {
                e[a].as_object_mut().unwrap().remove(b);
            } else {
                e[a][b] = value.clone();
            }
        }
        other => panic!("fake: a patch path of {} parts", other.len()),
    }
}

impl Store {
    pub fn new() -> Store {
        Store::default()
    }

    pub fn account(self, id: &str, name: &str) -> Store {
        self.inner.lock().unwrap().accounts.push(Account { id: id.into(), name: name.into(), ..Account::default() });
        self
    }

    pub fn mailbox(self, account: &str, m: Value) -> Store {
        self.inner.lock().unwrap().account_mut(account).mailboxes.push(m);
        self
    }

    pub fn email(self, account: &str, e: Value) -> Store {
        self.inner.lock().unwrap().account_mut(account).emails.push(e);
        self
    }

    pub fn identity(self, account: &str, i: Value) -> Store {
        self.inner.lock().unwrap().account_mut(account).identities.push(i);
        self
    }

    pub fn blob(self, id: &str, text: &str) -> Store {
        self.inner.lock().unwrap().blobs.insert(id.into(), text.into());
        self
    }

    pub fn deny_write(self, account: &str, id: &str) -> Store {
        self.inner.lock().unwrap().denied.insert((account.into(), id.into()));
        self
    }

    /// A client for one account, answering from this store.
    pub fn client(&self, account: &str) -> crate::jmap::Client {
        let state = self.inner.clone();
        let account = account.to_string();
        let accounts: Map<String, Value> = self.inner.lock().unwrap().accounts.iter().map(|a| {
            let mut caps = json!({"urn:ietf:params:jmap:mail": {}});
            if !a.identities.is_empty() {
                caps["urn:ietf:params:jmap:submission"] = json!({});
            }
            (a.id.clone(), json!({"name": a.name, "accountCapabilities": caps}))
        }).collect();
        let mut client = crate::jmap::Client::fake(
            "https://mail.example.test",
            account.as_str(),
            &["urn:ietf:params:jmap:core", "urn:ietf:params:jmap:mail", "urn:ietf:params:jmap:submission"],
            move |calls| answer(&state, calls),
        );
        client.session["accounts"] = Value::Object(accounts);
        client.set_live_session(client.session.clone());
        client
    }

    pub fn emails(&self, account: &str) -> Vec<Value> {
        self.inner.lock().unwrap().accounts.iter().find(|a| a.id == account).map(|a| a.emails.clone()).unwrap_or_default()
    }

    pub fn email_of(&self, account: &str, id: &str) -> Value {
        self.emails(account).into_iter().find(|e| e["id"] == id).unwrap_or(Value::Null)
    }

    pub fn mailboxes(&self, account: &str) -> Vec<Value> {
        self.inner.lock().unwrap().accounts.iter().find(|a| a.id == account).map(|a| a.mailboxes.clone()).unwrap_or_default()
    }

    /// Every method call the tools made, in order.
    pub fn requests(&self) -> Vec<Value> {
        self.inner.lock().unwrap().requests.clone()
    }

    /// The calls of one method, in order.
    pub fn calls(&self, method: &str) -> Vec<Value> {
        self.requests().into_iter().filter(|c| c[0] == method).map(|c| c[1].clone()).collect()
    }
}

fn answer(store: &Arc<Mutex<State>>, calls: &Value) -> Result<Vec<Value>, String> {
    let mut state = store.lock().unwrap();
    let list = calls.as_array().cloned().unwrap_or_default();
    state.requests.extend(list.iter().cloned());
    let mut out = Vec::new();
    for call in &list {
        let name = call[0].as_str().unwrap_or("");
        let args = &call[1];
        let id = &call[2];
        let account = args["accountId"].as_str().unwrap_or("").to_string();
        let response = match name {
            "Email/query" => {
                let acc = state.accounts.iter().find(|a| a.id == account).cloned().unwrap_or_default();
                let mut ids: Vec<Value> = acc
                    .emails
                    .iter()
                    .filter(|e| match args.get("filter") {
                        Some(f) if !f.is_null() => matches(e, f),
                        _ => true,
                    })
                    .map(|e| e["id"].clone())
                    .collect();
                // newest first, as the tools ask for
                ids.sort_by_key(|i| {
                    let e = acc.emails.iter().find(|e| &e["id"] == i).cloned().unwrap_or(Value::Null);
                    std::cmp::Reverse(date_of(&e))
                });
                let position = args["position"].as_u64().unwrap_or(0) as usize;
                let limit = args["limit"].as_u64().map(|l| l as usize).unwrap_or(ids.len());
                let page: Vec<Value> = ids.iter().skip(position).take(limit).cloned().collect();
                json!([name, {"accountId": account, "queryState": "q1", "canCalculateChanges": false, "position": position, "total": ids.len(), "ids": page}, id])
            }
            "Mailbox/query" => {
                let acc = state.accounts.iter().find(|a| a.id == account).cloned().unwrap_or_default();
                let position = args["position"].as_u64().unwrap_or(0) as usize;
                let limit = args["limit"].as_u64().unwrap_or(5000) as usize;
                let ids: Vec<_> = acc.mailboxes.iter().skip(position).take(limit).map(|m| m["id"].clone()).collect();
                json!([name, {"accountId": account, "position": position, "total": acc.mailboxes.len(), "ids": ids}, id])
            }
            "Email/get" | "Mailbox/get" | "Identity/get" => {
                let kind = name.split('/').next().unwrap_or("");
                let acc = state.accounts.iter().find(|a| a.id == account).cloned().unwrap_or_default();
                let all: Vec<Value> = match kind {
                    "Email" => acc.emails,
                    "Mailbox" => acc.mailboxes,
                    _ => acc.identities,
                };
                let wanted: Option<Vec<Value>> = args["ids"].as_array().cloned();
                let mut list: Vec<Value> = match &wanted {
                    Some(ids) => ids
                        .iter()
                        .filter_map(|i| all.iter().find(|x| &x["id"] == i).cloned())
                        .collect(),
                    None => all,
                };
                if kind == "Mailbox" && wanted.is_none() {
                    list.sort_by(|a, b| lower(&a["name"]).cmp(&lower(&b["name"])));
                }
                let missing: Vec<_> = wanted.iter().flatten().filter(|i| !list.iter().any(|m| m["id"] == **i)).collect();
                if let Some(properties) = args["properties"].as_array() {
                    for entry in &mut list {
                        entry.as_object_mut().unwrap().retain(|k, _| k == "id" || properties.iter().any(|p| p == k.as_str()));
                    }
                }
                json!([name, {"accountId": account, "state": "s1", "list": list, "notFound": missing}, id])
            }
            "Email/set" => {
                let denied = state.denied.clone();
                let acc = state.account_mut(&account);
                let mut updated = Map::new();
                let mut destroyed = Vec::new();
                let mut not_updated = Map::new();
                let mut not_destroyed = Map::new();
                if let Some(update) = args["update"].as_object() {
                    for (eid, p) in update {
                        if denied.contains(&(account.clone(), eid.clone())) {
                            not_updated.insert(eid.clone(), json!({"type": "forbidden"}));
                            continue;
                        }
                        let Some(e) = acc.emails.iter_mut().find(|e| e["id"] == eid.as_str()) else {
                            not_updated.insert(eid.clone(), json!({"type": "notFound"}));
                            continue;
                        };
                        for (path, value) in p.as_object().into_iter().flatten() {
                            assert!(!path.starts_with("keywords/") || *value == true || value.is_null(), "JMAP keyword value must be true or removed");
                            patch(e, path, value);
                        }
                        updated.insert(eid.clone(), Value::Null);
                    }
                }
                if let Some(list) = args["destroy"].as_array() {
                    for eid in list {
                        let key = eid.as_str().unwrap().to_owned();
                        if denied.contains(&(account.clone(), key.clone())) {
                            not_destroyed.insert(key, json!({"type": "forbidden"}));
                            continue;
                        }
                        let before = acc.emails.len();
                        acc.emails.retain(|e| e["id"] != *eid);
                        if acc.emails.len() < before {
                            destroyed.push(eid.clone());
                        } else {
                            not_destroyed.insert(key, json!({"type":"notFound"}));
                        }
                    }
                }
                json!([name, {"accountId": account, "updated": updated, "destroyed": destroyed, "notUpdated": not_updated, "notDestroyed": not_destroyed}, id])
            }
            "Mailbox/set" => {
                let acc = state.account_mut(&account);
                let mut created = Map::new();
                if let Some(create) = args["create"].as_object() {
                    for (key, m) in create {
                        let n = acc.mailboxes.len() + 1;
                        let new_id = format!("mb{n}");
                        let mut m = m.clone();
                        m["id"] = Value::String(new_id.clone());
                        acc.mailboxes.push(m);
                        created.insert(key.clone(), json!({"id": new_id}));
                    }
                }
                json!([name, {"accountId": account, "created": created, "updated": {}, "destroyed": [], "notCreated": {}, "notUpdated": {}}, id])
            }
            "blob/download" => {
                let text = state.blobs.get(args["blobId"].as_str().unwrap_or("")).cloned();
                json!([name, {"text": text}, id])
            }
            other => panic!("fake: the method {other} is not implemented"),
        };
        out.push(response);
    }
    Ok(out)
}

/// A [`Mail`] over the fake clients, with its own tag list.
pub struct TestMail {
    pub logins: Vec<Login>,
    tags: Mutex<Vec<TagDef>>,
}

impl TestMail {
    pub fn new(logins: Vec<Login>, tags: Vec<TagDef>) -> TestMail {
        TestMail { logins, tags: Mutex::new(tags) }
    }
}

impl Mail for TestMail {
    fn logins(&self) -> &[Login] {
        &self.logins
    }

    fn tags(&self) -> Vec<TagDef> {
        self.tags.lock().unwrap().clone()
    }

    fn create_tag(&self, name: &str, color: &str) -> Result<String, String> {
        new_tag(&mut self.tags.lock().unwrap(), name, color)
    }
}

/// One approval a [`TestHost`] was asked for.
#[derive(Clone, Debug, PartialEq)]
pub struct Asked {
    pub description: String,
    pub always: bool,
    pub danger: bool,
    pub confirm_label: Option<String>,
    pub details: Option<String>,
}

/// A [`Host`] that answers every card the same way and remembers them.
pub struct TestHost {
    pub answer: bool,
    pub asked: Mutex<Vec<Asked>>,
    /// Stop pressed while the card is up (the Stop-cancels-every-card path).
    pub cancel_on_approve: Option<Cancel>,
}

impl TestHost {
    pub fn new(answer: bool) -> TestHost {
        TestHost { answer, asked: Mutex::new(Vec::new()), cancel_on_approve: None }
    }

    pub fn asked(&self) -> Vec<Asked> {
        self.asked.lock().unwrap().clone()
    }
}

impl Host for TestHost {
    fn approve(&self, description: &str, opts: Approve<'_>, _: &Cancel) -> bool {
        self.asked.lock().unwrap().push(Asked {
            description: description.to_string(),
            always: opts.always,
            danger: opts.danger,
            confirm_label: opts.confirm_label.clone(),
            details: opts.details.clone(),
        });
        if let Some(c) = &self.cancel_on_approve {
            c.cancel();
        }
        self.answer
    }
}

/// The folders and messages the M6 tests share: one login (`l1`) with one account (`a`) holding
/// Inbox, Clients, Archive, Trash and Sent, and three messages.
pub fn store() -> Store {
    Store::new()
        .account("a", "Ann Example")
        .mailbox("a", json!({"id": "inbox", "name": "Inbox", "role": "inbox", "totalEmails": 3, "unreadEmails": 1}))
        .mailbox("a", json!({"id": "archive", "name": "Archive", "role": "archive", "totalEmails": 0, "unreadEmails": 0}))
        .mailbox("a", json!({"id": "trash", "name": "Trash", "role": "trash", "totalEmails": 0, "unreadEmails": 0}))
        .mailbox("a", json!({"id": "sent", "name": "Sent", "role": "sent", "totalEmails": 1, "unreadEmails": 0}))
        .mailbox("a", json!({"id": "work", "name": "Clients", "parentId": "inbox", "totalEmails": 0, "unreadEmails": 0}))
        .identity("a", json!({"id": "i1", "name": "Ann Example", "email": "ann@example.test"}))
        .email("a", email("M1", "inbox", "Ann Example <ann@example.test>", "me@example.test", "Lunch on Thursday", "2026-11-25T09:05:00Z",
            "Hi,\nlunch on Thursday?\n\nAnn", &[]))
        .email("a", email("M2", "inbox", "Bob <bob@example.test>", "me@example.test", "Invoice 42", "2026-11-20T08:00:00Z",
            "Please find the invoice attached.", &[json!({"partId": "3", "blobId": "b2", "name": "notes.txt", "type": "text/plain", "size": 12}),
                json!({"partId": "4", "blobId": "b3", "name": "scan.pdf", "type": "application/pdf", "size": 2048})]))
        .email("a", email("M3", "sent", "me@example.test", "Bob <bob@example.test>", "Re: Invoice 42", "2026-11-19T10:00:00Z",
            "Thanks, got it.", &[]))
        .blob("b2", "the notes")
        .blob("b3", "%PDF-1.4")
        .blob("blobM1", "Return-Path: <ann@example.test>\r\nSubject: Lunch on Thursday\r\n\r\nHi,\r\nlunch on Thursday?\r\n")
}

/// One message of [`store`].
pub fn email(id: &str, mailbox: &str, from: &str, to: &str, subject: &str, at: &str, text: &str, attachments: &[Value]) -> Value {
    let (name, address) = match from.split_once('<') {
        Some((n, a)) => (n.trim().to_string(), a.trim_end_matches('>').to_string()),
        None => (String::new(), from.to_string()),
    };
    json!({
        "id": id,
        "threadId": format!("t{id}"),
        "mailboxIds": { (mailbox): true },
        "keywords": {},
        "size": 1000,
        "sentAt": at,
        "receivedAt": at,
        "from": [{"name": name, "email": address}],
        "to": [{"name": "", "email": to}],
        "cc": [],
        "subject": subject,
        "preview": text.chars().take(80).collect::<String>(),
        "textBody": [{"partId": "1", "type": "text/plain"}],
        "htmlBody": [],
        "bodyValues": {"1": {"value": text}},
        "attachments": attachments,
        "blobId": format!("blob{id}"),
        "headers": [
            {"name": "From", "value": from},
            {"name": "To", "value": to},
            {"name": "Subject", "value": subject},
            {"name": "Received", "value": "from mail.example.test"},
            {"name": "Received", "value": "by mail.example.test"},
        ],
        "hasAttachment": !attachments.is_empty()
    })
}

/// The login id every test message carries.
pub const LOGIN: &str = "l1";

/// The `l1` login over a store's `a` account, plus the [`Mail`] that holds it.
pub fn mail(store: &Store, tags: Vec<TagDef>) -> TestMail {
    TestMail::new(vec![Login { id: LOGIN.into(), client: store.client("a") }], tags)
}

/// A [`MessageRef`](crate::assistant::context::MessageRef) as the model must send it.
pub fn message(id: &str) -> Value {
    json!({"id": id, "account": "a", "login": LOGIN})
}

