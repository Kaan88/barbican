//! Stalwart administration (feature: entering an admin API key enables a separate Admin tab).
//! Stalwart 1.0 exposes management objects over JMAP (capability `urn:stalwart:jmap`,
//! types prefixed `x:`), authorised with `Authorization: Bearer <api key or token>`.
use crate::{AdminRow, AppWindow};
use serde_json::{json, Value};
use slint::{ComponentHandle, ModelRc, VecModel};

const CAP: &str = "urn:stalwart:jmap";

fn entry() -> Option<keyring::Entry> {
    keyring::Entry::new("Barbican", "stalwart-admin-key").ok()
}

pub fn key() -> String {
    entry().and_then(|e| e.get_password().ok()).unwrap_or_default()
}

pub fn set_key(k: &str) {
    if let Some(e) = entry() {
        let _ = if k.trim().is_empty() { e.delete_credential() } else { e.set_password(k.trim()) };
    }
}

pub struct Admin {
    http: reqwest::blocking::Client,
    api: String,
    key: String,
    account: Option<String>,
}

impl Admin {
    pub fn new(c: &crate::jmap::Client) -> Option<Admin> {
        let key = key();
        if key.is_empty() { return None; }
        let api = c.session["apiUrl"].as_str().map(|a| if a.starts_with('/') {
            format!("{}{}", c.base.splitn(4, '/').take(3).collect::<Vec<_>>().join("/"), a)
        } else { a.to_string() })?;
        Some(Admin {
            http: reqwest::blocking::Client::builder().timeout(std::time::Duration::from_secs(30)).build().ok()?,
            api, key, account: c.session["primaryAccounts"][CAP].as_str().map(String::from),
        })
    }

    pub fn call(&self, method: &str, mut args: Value) -> Result<Value, String> {
        if let (Some(a), Some(o)) = (&self.account, args.as_object_mut()) { o.entry("accountId").or_insert(json!(a)); }
        let body = json!({"using": ["urn:ietf:params:jmap:core", CAP], "methodCalls": [[method, args, "a"]]});
        let r = self.http.post(&self.api).bearer_auth(&self.key).json(&body).send().map_err(|e| e.to_string())?;
        match r.status().as_u16() {
            401 => return Err("The admin API key was rejected.".into()),
            403 => return Err("This key lacks the permission for that.".into()),
            s if s >= 400 => return Err(format!("Admin request failed ({s}).")),
            _ => {}
        }
        let v: Value = r.json().map_err(|e| e.to_string())?;
        let res = v["methodResponses"][0].clone();
        if res[0] == "error" { return Err(crate::jmap::method_error(&res[1])); }
        Ok(res[1].clone())
    }

    /// query + get of an x: type.
    pub fn list(&self, ty: &str, limit: usize, sort: Option<Value>) -> Result<(usize, Vec<Value>), String> {
        let r = self.list_inner(ty, limit, sort)?;
        let copy = r.1.clone();
        LAST_SEND.lock().unwrap().replace(copy);
        Ok(r)
    }

    fn list_inner(&self, ty: &str, limit: usize, sort: Option<Value>) -> Result<(usize, Vec<Value>), String> {
        let mut q = json!({"limit": limit, "calculateTotal": true});
        if let Some(s) = sort { q["sort"] = s; }
        let r = self.call(&format!("x:{ty}/query"), q)?;
        let total = r["total"].as_u64().unwrap_or(0) as usize;
        let ids = r["ids"].clone();
        if ids.as_array().is_none_or(|a| a.is_empty()) { return Ok((total, vec![])); }
        let g = self.call(&format!("x:{ty}/get"), json!({"ids": ids}))?;
        Ok((total, g["list"].as_array().cloned().unwrap_or_default()))
    }
}

fn row(id: &str, c: [String; 4]) -> AdminRow {
    let [c1, c2, c3, c4] = c;
    AdminRow { id: id.into(), c1: c1.into(), c2: c2.into(), c3: c3.into(), c4: c4.into() }
}

/// ISO timestamps in local time, e.g. "15 Nov 2026 13:38".
fn when(v: &Value) -> String {
    v.as_str().and_then(|t| chrono::DateTime::parse_from_rfc3339(t).ok())
        .map(|d| d.with_timezone(&chrono::Local).format("%d %b %Y %H:%M").to_string())
        .unwrap_or_else(|| s(v))
}

fn s(v: &Value) -> String {
    match v { Value::String(x) => x.clone(), Value::Null => String::new(), x => x.to_string() }
}

/// Load an admin section: overview, accounts, domains, queue, logs, reports.
pub fn load(u: &AppWindow, c: std::sync::Arc<crate::jmap::Client>, section: &str) {
    let w = u.as_weak();
    let section = section.to_string();
    u.set_admin_status("Loading…".into());
    std::thread::spawn(move || {
        let Some(a) = Admin::new(&c) else {
            let _ = w.upgrade_in_event_loop(|u| u.set_admin_status("Add a Stalwart admin API key in Settings → Admin.".into()));
            return;
        };
        let res: Result<(Vec<String>, Vec<AdminRow>, Vec<(String, String)>), String> = (|| {
            Ok(match section.as_str() {
                "accounts" => {
                    let doms: std::collections::HashMap<String, String> = a.list("Domain", 500, None).map(|(_, d)| d.iter()
                        .filter_map(|x| Some((x["id"].as_str()?.to_string(), x["name"].as_str()?.to_string()))).collect()).unwrap_or_default();
                    let (_, l) = a.list("Account", 500, None)?;
                    let l: Vec<Value> = l.into_iter().map(|mut x| {
                        let d = x["domainId"].as_str().and_then(|i| doms.get(i)).cloned().unwrap_or_default();
                        if !d.is_empty() { x["domainId"] = json!(d.clone()); x["name"] = json!(format!("{}@{d}", x["name"].as_str().unwrap_or(""))); }
                        x
                    }).collect();
                    (vec!["Name".into(), "Domain".into(), "Type".into(), "Quota".into()],
                     l.iter().map(|x| row(x["id"].as_str().unwrap_or(""), [s(&x["name"]), s(&x["domainId"]), s(&x["@type"]), s(&x["quota"])])).collect(), vec![])
                }
                "domains" => {
                    let (_, l) = a.list("Domain", 500, None)?;
                    (vec!["Domain".into(), "Description".into(), "".into(), "".into()],
                     l.iter().map(|x| row(x["id"].as_str().unwrap_or(""), [s(&x["name"]), s(&x["description"]), String::new(), String::new()])).collect(), vec![])
                }
                "queue" => {
                    let (_, l) = a.list("QueuedMessage", 500, None)?;
                    (vec!["Recipients".into(), "From".into(), "Next retry".into(), "Size".into()],
                     l.iter().map(|x| {
                         let rc = x["recipients"].as_object().map(|o| o.keys().cloned().collect::<Vec<_>>().join(", ")).unwrap_or_default();
                         row(x["id"].as_str().unwrap_or(""), [rc, s(&x["returnPath"]), when(&x["nextRetry"]), crate::human_size(x["size"].as_u64().unwrap_or(0))])
                     }).collect(), vec![])
                }
                "logs" => {
                    let (_, l) = a.list("Log", 200, Some(json!([{"property": "timestamp", "isAscending": false}])))
                        .or_else(|_| a.list("Log", 200, None))
                        .map_err(|e| if e.contains("serverUnavailable") || e.contains("unavailable") {
                            "Logs are not available: the server has no log store configured (Settings → Telemetry → Logging in Stalwart).".to_string()
                        } else { e })?;
                    (vec!["Time".into(), "Level".into(), "Event".into(), "Details".into()],
                     l.iter().map(|x| row(x["id"].as_str().unwrap_or(""), [when(&x["timestamp"]), s(&x["level"]), s(&x["event"]), s(&x["details"])])).collect(), vec![])
                }
                "reports" => {
                    let (_, l) = a.list("DmarcExternalReport", 200, None)?;
                    (vec!["Reporter".into(), "Received".into(), "Period".into(), "Expires".into()],
                     l.iter().map(|x| row(x["id"].as_str().unwrap_or(""), [
                         s(&x["from"]), when(&x["receivedAt"]),
                         format!("{} – {}", when(&x["report"]["dateRangeStart"]), when(&x["report"]["dateRangeEnd"])),
                         when(&x["expiresAt"]),
                     ])).collect(), vec![])
                }
                _ => {
                    let mut tiles = vec![];
                    for (label, ty) in [("Domains", "Domain"), ("Accounts", "Account"), ("Queued messages", "QueuedMessage"), ("DMARC reports", "DmarcExternalReport")] {
                        let n = a.call(&format!("x:{ty}/query"), json!({"limit": 0, "calculateTotal": true}))
                            .map(|r| r["total"].as_u64().map(|n| n.to_string()).unwrap_or_else(|| "?".into()))
                            .unwrap_or_else(|e| format!("— ({e})"));
                        tiles.push((label.to_string(), n));
                    }
                    (vec![], vec![], tiles)
                }
            })
        })();
        let last = LAST_SEND.lock().unwrap().take().unwrap_or_default();
        let _ = w.upgrade_in_event_loop(move |u| match res {
            Ok((cols, rows, tiles)) => {
                LAST.with(|l| *l.borrow_mut() = last);
                let (cols, rows) = if !rows.is_empty() && rows.iter().all(|r| r.c1.is_empty() && r.c2.is_empty()) {
                    LAST.with(|l| auto_columns(&l.borrow()))
                } else { (cols, rows) };
                u.set_admin_cols(ModelRc::new(VecModel::from(cols.into_iter().map(Into::into).collect::<Vec<slint::SharedString>>())));
                u.set_admin_rows(ModelRc::new(VecModel::from(rows)));
                u.set_admin_tiles(ModelRc::new(VecModel::from(tiles.into_iter().map(|(a, b)| AdminRow { id: "".into(), c1: a.into(), c2: b.into(), c3: "".into(), c4: "".into() }).collect::<Vec<_>>())));
                u.set_admin_status("".into());
            }
            Err(e) => u.set_admin_status(e.into()),
        });
    });
}

/// Delete a queued message (cancels delivery).
pub fn delete_queued(u: &AppWindow, c: std::sync::Arc<crate::jmap::Client>, id: &str) {
    let w = u.as_weak();
    let id = id.to_string();
    std::thread::spawn(move || {
        let res = Admin::new(&c).ok_or("No admin key".to_string()).and_then(|a| a.call("x:QueuedMessage/set", json!({"destroy": [id]})));
        let _ = w.upgrade_in_event_loop(move |u| {
            if let Err(e) = res { crate::mail::show_toast(&u, &e, false); }
            load(&u, c, "queue");
        });
    });
}

static LAST_SEND: std::sync::Mutex<Option<Vec<Value>>> = std::sync::Mutex::new(None);
thread_local! { static LAST: std::cell::RefCell<Vec<Value>> = const { std::cell::RefCell::new(Vec::new()) }; }

fn short(v: &Value) -> String {
    let t = match v {
        Value::String(x) => x.clone(),
        Value::Null => String::new(),
        Value::Object(o) => o.iter().filter(|(_, x)| !x.is_object() && !x.is_array()).map(|(k, x)| format!("{k}: {}", s(x))).take(3).collect::<Vec<_>>().join(", "),
        Value::Array(a) => a.iter().map(s).take(3).collect::<Vec<_>>().join(", "),
        x => x.to_string(),
    };
    t.chars().take(120).collect()
}

/// Columns picked from what the server actually returns (first four informative fields).
fn auto_columns(list: &[Value]) -> (Vec<String>, Vec<AdminRow>) {
    let Some(first) = list.first().and_then(|v| v.as_object()) else { return (vec![], vec![]) };
    let keys: Vec<String> = first.iter()
        .filter(|(k, v)| k.as_str() != "id" && !short(v).is_empty())
        .map(|(k, _)| k.clone()).take(4).collect();
    let mut cols = keys.clone();
    while cols.len() < 4 { cols.push(String::new()); }
    let rows = list.iter().map(|x| {
        let c: Vec<String> = (0..4).map(|i| keys.get(i).map(|k| short(&x[k])).unwrap_or_default()).collect();
        row(x["id"].as_str().unwrap_or(""), [c[0].clone(), c[1].clone(), c[2].clone(), c[3].clone()])
    }).collect();
    (cols, rows)
}
