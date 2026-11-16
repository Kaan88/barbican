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

/// ISO timestamps in local time, e.g. "15 Nov 2026 13:38" (or "1:38 PM" with timeFormat 12h).
fn when(v: &Value) -> String {
    let f = if crate::settings::h12() { "%d %b %Y %-I:%M %p" } else { "%d %b %Y %H:%M" };
    v.as_str().and_then(|t| chrono::DateTime::parse_from_rfc3339(t).ok())
        .map(|d| d.with_timezone(&chrono::Local).format(f).to_string())
        .unwrap_or_else(|| s(v))
}

fn s(v: &Value) -> String {
    match v { Value::String(x) => x.clone(), Value::Null => String::new(), x => x.to_string() }
}

//// Sub-pages of a section (the WebUI's nested navigation), first = default.
pub fn subs(section: &str) -> &'static [(&'static str, &'static str)] {
    match section {
        "tasks" => &[("scheduled", "Scheduled"), ("failed", "Failed"), ("index", "Index queue")],
        "cluster" => &[("nodes", "Nodes"), ("roles", "Roles")],
        _ => &[],
    }
}

/// Load an admin section: overview, accounts, domains, queue, logs, reports, tasks, cluster, actions.
pub fn load(u: &AppWindow, c: std::sync::Arc<crate::jmap::Client>, section: &str) {
    if section == "overview" {
        crate::dashboard::load(u, c, u.get_dash_tab().max(0) as usize, u.get_dash_range().clamp(0, 3) as usize);
        return;
    }
    let w = u.as_weak();
    let section = section.to_string();
    // sub-page: keep the current one when it belongs to this section, else the first
    let sl = subs(&section);
    let cur = u.get_admin_sub().to_string();
    let sub = if sl.iter().any(|(k, _)| *k == cur) { cur } else { sl.first().map(|(k, _)| k.to_string()).unwrap_or_default() };
    u.set_admin_sub(sub.clone().into());
    u.set_admin_subs(ModelRc::new(VecModel::from(sl.iter().map(|(k, l)| row(k, [l.to_string(), String::new(), String::new(), String::new()])).collect::<Vec<_>>())));
    u.set_admin_note("".into());
    u.set_admin_status("Loading…".into());
    std::thread::spawn(move || {
        let Some(a) = Admin::new(&c) else {
            let _ = w.upgrade_in_event_loop(|u| u.set_admin_status("Add a Stalwart admin API key in Settings → Admin.".into()));
            return;
        };
        let mut note = String::new();
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
                // Tasks: x:Task ("Scheduled"), its "Failed" view (filter status = Failed) and
                // x:IndexQueueStatus, as in Stalwart's WebUI navigation (Management → Tasks).
                "tasks" if sub == "index" => {
                    note = "Search index processing per account and index.".into();
                    let (_, l) = a.list("IndexQueueStatus", 500, None)?;
                    (vec!["Index".into(), "Partition".into(), "Queued (updates / deletions)".into(), "Status".into()],
                     l.iter().map(|x| row(x["id"].as_str().unwrap_or(""), index_row(x))).collect(), vec![])
                }
                "tasks" => {
                    let failed = sub == "failed";
                    note = if failed { "Background tasks that failed permanently and need operator action." } else { "Background tasks scheduled for execution, soonest first." }.into();
                    let mut q = json!({"limit": 500, "calculateTotal": true});
                    if failed { q["filter"] = json!({"status": "Failed"}); } else { q["sort"] = json!([{"property": "due", "isAscending": true}]); }
                    let r = a.call("x:Task/query", q)?;
                    let ids = r["ids"].clone();
                    let l = if ids.as_array().is_none_or(|v| v.is_empty()) { vec![] } else {
                        a.call("x:Task/get", json!({"ids": ids}))?["list"].as_array().cloned().unwrap_or_default()
                    };
                    LAST_SEND.lock().unwrap().replace(l.clone());
                    (vec!["Task type".into(), "Due".into(), "Status".into(), "Details".into()],
                     l.iter().map(|x| row(x["id"].as_str().unwrap_or(""), task_row(x))).collect(), vec![])
                }
                // Cluster: x:ClusterNode (Management → Cluster) and x:ClusterRole (Settings → Cluster → Roles);
                // the x:Coordinator singleton tells whether clustering is on.
                "cluster" if sub == "roles" => {
                    note = "Cluster node roles with their enabled tasks and listeners.".into();
                    let (_, l) = a.list("ClusterRole", 500, None)?;
                    (vec!["Name".into(), "Description".into(), "Tasks".into(), "Listeners".into()],
                     l.iter().map(|x| row(x["id"].as_str().unwrap_or(""), [s(&x["name"]), s(&x["description"]), group_summary(&x["tasks"]), group_summary(&x["listeners"])])).collect(), vec![])
                }
                "cluster" => {
                    let coord = a.call("x:Coordinator/get", json!({"ids": ["singleton"]})).ok()
                        .and_then(|r| r["list"][0]["@type"].as_str().map(String::from));
                    note = match coord.as_deref() {
                        Some("Disabled") => "Coordinator: disabled (this server runs as a single node).".into(),
                        Some(t) => format!("Coordinator: {}. Nodes with their last lease renewal and status.", coordinator_label(t)),
                        None => "Nodes in the cluster with their last lease renewal and status.".into(),
                    };
                    let r = a.call("x:ClusterNode/query", json!({"limit": 500, "sort": [{"property": "nodeId", "isAscending": true}]}))?;
                    let ids = r["ids"].clone();
                    let l = if ids.as_array().is_none_or(|v| v.is_empty()) { vec![] } else {
                        a.call("x:ClusterNode/get", json!({"ids": ids}))?["list"].as_array().cloned().unwrap_or_default()
                    };
                    (vec!["Node ID".into(), "Hostname".into(), "Last renewal".into(), "Status".into()],
                     l.iter().map(|x| row(x["id"].as_str().unwrap_or(""), node_row(x))).collect(), vec![])
                }
                "actions" => {
                    note = "Server management actions (x:Action). Each one asks before it runs.".into();
                    (vec!["Action".into(), "Group".into(), "Input".into(), "".into()],
                     ACTIONS.iter().map(|(k, label, _)| {
                         let (g, n) = split_label(label);
                         let how = if form_fields(k).is_empty() { "" } else { "Asks for input" };
                         row(k, [n.to_string(), g.to_string(), how.to_string(), String::new()])
                     }).collect(), vec![])
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
        let _ = w.upgrade_in_event_loop(move |u| {
            // a late answer for a page the user already left is dropped
            if u.get_admin_section().as_str() != section || u.get_admin_sub().as_str() != sub { return; }
            match res {
                Ok((cols, rows, tiles)) => {
                    LAST.with(|l| *l.borrow_mut() = last);
                    let (cols, rows) = if !rows.is_empty() && rows.iter().all(|r| r.c1.is_empty() && r.c2.is_empty()) {
                        LAST.with(|l| auto_columns(&l.borrow()))
                    } else { (cols, rows) };
                    u.set_admin_cols(ModelRc::new(VecModel::from(cols.into_iter().map(Into::into).collect::<Vec<slint::SharedString>>())));
                    u.set_admin_rows(ModelRc::new(VecModel::from(rows)));
                    u.set_admin_tiles(ModelRc::new(VecModel::from(tiles.into_iter().map(|(a, b)| AdminRow { id: "".into(), c1: a.into(), c2: b.into(), c3: "".into(), c4: "".into() }).collect::<Vec<_>>())));
                    u.set_admin_note(note.into());
                    u.set_admin_status("".into());
                }
                Err(e) => {
                    u.set_admin_rows(ModelRc::default());
                    u.set_admin_status(e.into());
                }
            }
        });
    });
}

// ---------------------------------------------------------------- tasks, cluster

const TASK_TYPES: &[(&str, &str)] = &[
    ("CalendarAlarmEmail", "Calendar alarm e-mail"),
    ("CalendarAlarmNotification", "Calendar alarm notification"),
    ("CalendarItipMessage", "Calendar iTIP message"),
    ("MergeThreads", "Merge email threads"),
    ("DmarcReport", "Send DMARC report"),
    ("TlsReport", "Send TLS report"),
    ("RestoreArchivedItem", "Restore archived item"),
    ("DestroyAccount", "Destroy account"),
    ("AccountMaintenance", "Account maintenance"),
    ("TenantMaintenance", "Tenant maintenance"),
    ("StoreMaintenance", "Store maintenance"),
    ("SpamFilterMaintenance", "Spam filter maintenance"),
    ("AcmeRenewal", "ACME certificate renewal"),
    ("DkimManagement", "DKIM key rotation"),
    ("DnsManagement", "DNS management"),
    ("ShareRevokeCleanup", "Revoked share cleanup"),
    ("DmarcReportWindow", "Build DMARC aggregate reports"),
    ("TlsReportWindow", "Build TLS aggregate reports"),
    ("ExternalPublicKeyMaintenance", "External public key maintenance"),
    ("BlobRecode", "Recode stored blobs"),
];

fn label_of(table: &[(&str, &'static str)], k: &str) -> String {
    table.iter().find(|(n, _)| *n == k).map(|(_, l)| l.to_string()).unwrap_or_else(|| k.to_string())
}

/// "Group: Name" -> (group, name).
pub fn split_label(label: &str) -> (&str, &str) {
    label.split_once(':').map(|(g, n)| (g.trim(), n.trim())).filter(|(g, _)| !g.is_empty()).unwrap_or(("Other", label))
}

/// A task row: type, due, status, details (x:Task and its x:TaskStatus variant).
pub fn task_row(x: &Value) -> [String; 4] {
    let st = &x["status"];
    let kind = st["@type"].as_str().unwrap_or("");
    let due = if x["due"].is_string() { when(&x["due"]) } else if st["due"].is_string() { when(&st["due"]) } else { String::new() };
    let (status, details) = match kind {
        "Pending" => ("Pending".to_string(), String::new()),
        "Retry" => (format!("Retry #{}", st["attemptNumber"].as_u64().unwrap_or(1)), s(&st["failureReason"])),
        "Stalled" => ("Stalled".to_string(), s(&st["failureReason"])),
        "Failed" => ("Failed".into(), format!("After {} attempts: {}", st["failedAttemptNumber"].as_u64().unwrap_or(0), s(&st["failureReason"]))),
        k => (k.to_string(), String::new()),
    };
    let mut ty = label_of(TASK_TYPES, x["@type"].as_str().unwrap_or(""));
    // name what the task works on when the server says so
    for k in ["domain", "accountId", "tenantId"] {
        if let Some(v) = x[k].as_str() { ty = format!("{ty} · {v}"); break; }
    }
    [ty, due, status, details]
}

fn index_row(x: &Value) -> [String; 4] {
    let idx = match x["index"].as_str().unwrap_or("") { "telemetry" => "Telemetry", "email" => "Email", "calendar" => "Calendar", "contacts" => "Contacts", "file" => "File", o => o }.to_string();
    let st = match x["status"]["@type"].as_str() {
        Some("Failed") => format!("Failed {}", s(&x["status"]["reason"])).trim().to_string(),
        Some(o) => o.to_string(),
        None => "Running".to_string(),
    };
    [idx, s(&x["partition"]), format!("{} / {}", x["queuedUpdates"].as_u64().unwrap_or(0), x["queuedDeletions"].as_u64().unwrap_or(0)), st]
}

fn node_row(x: &Value) -> [String; 4] {
    let st = match x["status"].as_str().unwrap_or("") { "active" => "Active", "stale" => "Stale", "inactive" => "Inactive", o => o }.to_string();
    [s(&x["nodeId"]), s(&x["hostname"]), when(&x["lastRenewal"]), st]
}

fn coordinator_label(t: &str) -> &str {
    match t { "Default" => "in-memory store", "Kafka" => "Kafka", "Nats" => "NATS", "Zenoh" => "Eclipse Zenoh", "Redis" => "Redis", "RedisCluster" => "Redis Cluster", "RedisSentinel" => "Redis Sentinel", o => o }
}

/// "a, b, c +3" for a group object of booleans (role tasks/listeners).
fn group_summary(v: &Value) -> String {
    let Some(o) = v.as_object() else { return s(v) };
    let on: Vec<&str> = o.iter().filter(|(k, x)| k.as_str() != "@type" && x.as_bool() == Some(true)).map(|(k, _)| k.as_str()).collect();
    if on.is_empty() { return o.get("@type").map(s).unwrap_or_default(); }
    let mut t = on.iter().take(3).copied().collect::<Vec<_>>().join(", ");
    if on.len() > 3 { t.push_str(&format!(" +{}", on.len() - 3)); }
    t
}

// ---------------------------------------------------------------- actions (x:Action)

/// x:Action variants (Stalwart schema): name, label "Group: Name", description shown when confirming.
pub const ACTIONS: &[(&str, &str, &str)] = &[
    ("ReloadSettings", "Reload: Server settings", "Rebuilds the configuration from the stored settings and applies it to the running server."),
    ("ReloadTlsCertificates", "Reload: TLS certificates", "Reloads all TLS certificates."),
    ("ReloadLookupStores", "Reload: Lookup stores", "Reloads the lookup stores."),
    ("ReloadBlockedIps", "Reload: Blocked IPs list", "Reloads the list of blocked IP addresses."),
    ("UpdateApps", "Application Management: Update applications", "Downloads and installs the latest bundled web applications."),
    ("TroubleshootDmarc", "DMARC: Troubleshooting", "Runs the SPF, DKIM, ARC and DMARC checks for a message as if it arrived from the given client."),
    ("ClassifySpam", "Spam Filter: Classify a message", "Runs the spam filter on a message and shows its score and tags."),
    ("InvalidateCaches", "Cache: Invalidate all caches", "Clears every server cache."),
    ("InvalidateNegativeCaches", "Cache: Invalidate negative caches", "Clears the caches of failed lookups."),
    ("PauseMtaQueue", "MTA: Pause queue processing", "Stops delivering queued messages until the queue is resumed."),
    ("ResumeMtaQueue", "MTA: Resume queue processing", "Resumes delivering queued messages."),
];

/// Input fields of an action with a form: (property, label, kind: text | multiline | set | bool).
pub fn form_fields(action: &str) -> &'static [(&'static str, &'static str, &'static str)] {
    match action {
        "TroubleshootDmarc" => &[
            ("remoteIp", "Remote IP address", "text"), ("ehloDomain", "EHLO domain", "text"),
            ("mailFrom", "MAIL FROM address", "text"), ("rcptTo", "RCPT TO addresses (comma separated)", "set"),
            ("spfEhloDomain", "SPF EHLO domain", "text"), ("spfMailFromDomain", "SPF MAIL FROM domain", "text"),
            ("message", "Message (raw, optional)", "multiline"),
        ],
        "ClassifySpam" => &[
            ("remoteIp", "Remote IP address", "text"), ("ehloDomain", "EHLO domain", "text"),
            ("authenticatedAs", "Authenticated as (optional)", "text"), ("isTls", "Connection uses TLS", "bool"),
            ("mailFrom", "MAIL FROM address", "text"), ("rcptTo", "RCPT TO addresses (comma separated)", "set"),
            ("message", "Raw message", "multiline"),
        ],
        _ => &[],
    }
}

/// Nullable string properties: an empty field is sent as null.
const NULLABLE: &[&str] = &["mailFrom", "message", "authenticatedAs"];

/// The x:Action/set create payload for an action and its form values.
pub fn action_payload(action: &str, values: &std::collections::HashMap<String, String>) -> Value {
    let mut p = serde_json::Map::new();
    p.insert("@type".into(), json!(action));
    for (k, _, kind) in form_fields(action) {
        let v = values.get(*k).map(|v| v.trim()).unwrap_or("");
        let val = match *kind {
            "bool" => json!(v == "true"),
            "set" => Value::Object(v.split([',', ';', ' ']).map(str::trim).filter(|x| !x.is_empty()).map(|x| (x.to_string(), json!(true))).collect()),
            _ if v.is_empty() => { if NULLABLE.contains(k) && !(action == "ClassifySpam" && *k == "message") { Value::Null } else { continue } }
            "multiline" => json!(v.replace("\r\n", "\n").replace('\n', "\r\n")),
            _ => json!(v),
        };
        p.insert(k.to_string(), val);
    }
    Value::Object(p)
}

/// Flatten an action result into (key, value) lines.
pub fn flatten(prefix: &str, v: &Value, out: &mut Vec<(String, String)>) {
    match v {
        Value::Object(o) => {
            if o.is_empty() && !prefix.is_empty() { out.push((prefix.to_string(), "—".into())); }
            for (k, x) in o {
                if prefix.is_empty() && matches!(k.as_str(), "id" | "@type" | "message") { continue; }
                let key = if prefix.is_empty() { k.clone() } else { format!("{prefix}.{k}") };
                // sets come as {"value": true}
                if let Value::Object(m) = x {
                    if !m.is_empty() && m.values().all(|b| b == &Value::Bool(true)) {
                        out.push((key, m.keys().cloned().collect::<Vec<_>>().join(", ")));
                        continue;
                    }
                }
                flatten(&key, x, out);
            }
        }
        Value::Array(a) => {
            if a.is_empty() { out.push((prefix.to_string(), "—".into())); }
            for (i, x) in a.iter().enumerate() { flatten(&format!("{prefix}[{}]", i + 1), x, out); }
        }
        x => out.push((prefix.to_string(), s(x))),
    }
}

thread_local! {
    static FORM: std::cell::RefCell<(String, std::collections::HashMap<String, String>)> = std::cell::RefCell::new(Default::default());
}

/// A click on an action: open its form, or the confirmation for actions without input.
pub fn action_open(u: &AppWindow, id: &str) {
    let Some((_, label, desc)) = ACTIONS.iter().find(|(k, _, _)| *k == id) else { return };
    FORM.with(|f| *f.borrow_mut() = (id.to_string(), Default::default()));
    let fields = form_fields(id);
    u.set_admin_dlg_title(split_label(label).1.into());
    u.set_admin_dlg_text((*desc).into());
    u.set_admin_dlg_fields(ModelRc::new(VecModel::from(fields.iter().map(|(k, l, kind)| row(k, [l.to_string(), String::new(), kind.to_string(), String::new()])).collect::<Vec<_>>())));
    u.set_admin_dlg_result(ModelRc::default());
    u.set_admin_dlg(if fields.is_empty() { "confirm".into() } else { "form".into() });
}

pub fn action_edit(key: &str, value: &str) {
    FORM.with(|f| { f.borrow_mut().1.insert(key.to_string(), value.to_string()); });
}

/// Run the confirmed action (form submitted or "Execute" pressed): x:Action/set create.
pub fn action_execute(u: &AppWindow, c: std::sync::Arc<crate::jmap::Client>) {
    let (id, values) = FORM.with(|f| f.borrow().clone());
    if id.is_empty() { return; }
    let payload = action_payload(&id, &values);
    u.set_admin_dlg("running".into());
    let w = u.as_weak();
    std::thread::spawn(move || {
        let res = Admin::new(&c).ok_or("No admin key".to_string())
            .and_then(|a| a.call("x:Action/set", json!({"create": {"action-0": payload}})))
            .and_then(|r| action_result(&r));
        let _ = w.upgrade_in_event_loop(move |u| {
            match res {
                Ok(l) => {
                    u.set_admin_dlg_text(if l.is_empty() { "The action completed.".into() } else { "The action completed:".into() });
                    u.set_admin_dlg_result(ModelRc::new(VecModel::from(l.into_iter().map(|(k, v)| row("", [k, v, String::new(), String::new()])).collect::<Vec<_>>())));
                }
                Err(e) => {
                    u.set_admin_dlg_text(format!("The action failed: {e}").into());
                    u.set_admin_dlg_result(ModelRc::default());
                }
            }
            u.set_admin_dlg("result".into());
        });
    });
}

/// Lines of an x:Action/set answer, or its error.
pub fn action_result(r: &Value) -> Result<Vec<(String, String)>, String> {
    if let Some(e) = r["notCreated"]["action-0"].as_object() {
        return Err(e.get("description").and_then(|d| d.as_str()).map(String::from)
            .unwrap_or_else(|| e.get("type").map(s).unwrap_or_else(|| "unknown error".into())));
    }
    let mut out = vec![];
    flatten("", &r["created"]["action-0"], &mut out);
    out.retain(|(k, _)| !k.is_empty());
    Ok(out)
}

// Delete a queued message (cancels delivery).
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

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn task_rows() {
        let t = json!({"id": "a", "@type": "StoreMaintenance", "due": "2026-11-16T10:00:00Z", "status": {"@type": "Pending", "due": "2026-11-16T10:00:00Z"}});
        let r = task_row(&t);
        assert_eq!(r[0], "Store maintenance");
        assert!(!r[1].is_empty());
        assert_eq!(r[2], "Pending");
        let f = json!({"@type": "DkimManagement", "domain": "x.org", "status": {"@type": "Failed", "failedAttemptNumber": 3, "failureReason": "boom"}});
        let r = task_row(&f);
        assert_eq!(r[0], "DKIM key rotation · x.org");
        assert_eq!(r[2], "Failed");
        assert_eq!(r[3], "After 3 attempts: boom");
        let rt = task_row(&json!({"@type": "X", "status": {"@type": "Retry", "attemptNumber": 2, "failureReason": "dns"}}));
        assert_eq!((rt[0].as_str(), rt[2].as_str(), rt[3].as_str()), ("X", "Retry #2", "dns"));
    }

    #[test]
    fn nodes_and_roles() {
        let r = node_row(&json!({"nodeId": 1, "hostname": "mx1", "status": "stale"}));
        assert_eq!((r[0].as_str(), r[1].as_str(), r[3].as_str()), ("1", "mx1", "Stale"));
        assert_eq!(group_summary(&json!({"a": true, "b": false, "c": true})), "a, c");
        assert_eq!(group_summary(&json!({"@type": "All"})), "All");
        assert_eq!(group_summary(&json!({"a": true, "b": true, "c": true, "d": true, "e": true})), "a, b, c +2");
    }

    #[test]
    fn actions() {
        assert_eq!(ACTIONS.len(), 11);
        assert_eq!(split_label("Reload: TLS certificates"), ("Reload", "TLS certificates"));
        assert_eq!(split_label("Plain"), ("Other", "Plain"));
        let p = action_payload("InvalidateCaches", &Default::default());
        assert_eq!(p, json!({"@type": "InvalidateCaches"}));
        let mut v = std::collections::HashMap::new();
        v.insert("remoteIp".to_string(), " 1.2.3.4 ".to_string());
        v.insert("rcptTo".to_string(), "a@x.org, b@x.org".to_string());
        v.insert("message".to_string(), "A\nB".to_string());
        let p = action_payload("TroubleshootDmarc", &v);
        assert_eq!(p["remoteIp"], "1.2.3.4");
        assert_eq!(p["rcptTo"], json!({"a@x.org": true, "b@x.org": true}));
        assert_eq!(p["message"], "A\r\nB");
        assert_eq!(p["mailFrom"], Value::Null);
        assert!(p.get("ehloDomain").is_none());
        let s = action_payload("ClassifySpam", &[("isTls".to_string(), "true".to_string())].into_iter().collect());
        assert_eq!(s["isTls"], true);
        assert!(s.get("message").is_none());
    }

    #[test]
    fn action_results() {
        let ok = json!({"created": {"action-0": {"id": "1", "warnings": {"licence expired": true}, "score": 5.5, "tags": {"BAYES": {"score": 1}}}}});
        let l = action_result(&ok).unwrap();
        assert!(l.contains(&("warnings".to_string(), "licence expired".to_string())));
        assert!(l.contains(&("score".to_string(), "5.5".to_string())));
        assert!(l.contains(&("tags.BAYES.score".to_string(), "1".to_string())));
        assert!(!l.iter().any(|(k, _)| k == "id"));
        let bad = json!({"notCreated": {"action-0": {"type": "forbidden", "description": "No"}}});
        assert_eq!(action_result(&bad), Err("No".to_string()));
        assert_eq!(action_result(&json!({"created": {"action-0": {"id": "1"}}})), Ok(vec![]));
    }
}
