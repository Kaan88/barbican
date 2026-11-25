//! Masked addresses: Stalwart's `x:MaskedEmail` registry objects, as Bulwark's Account security
//! page manages them (components/settings/masked-addresses-section.tsx,
//! lib/stalwart/masked-email.ts).
//!
//! Every call names the signed-in account and checks the answer is for it; a record that names
//! another owner, a list that changes while it is read, or an answer that does not say a change
//! was made is an error, not a success. The section stays hidden until the server answers with
//! anything but "not supported" (most servers, Stalwart Community among them, have none).
use crate::jmap::Client;
use serde_json::{json, Value};

/// The management capabilities, in Bulwark's order (lib/stalwart/management-capability.ts).
pub const CAPABILITIES: [&str; 2] = ["urn:stalwart:jmap", "urn:inbuxa:jmap:registry"];
pub const PAGE_SIZE: usize = 10;

#[derive(Clone, Debug, PartialEq)]
pub struct Masked {
    pub id: String,
    pub email: String,
    pub for_domain: Option<String>,
    pub description: Option<String>,
    pub enabled: bool,
}

/// A failure: the JMAP error or SetError type (or one of Bulwark's own: invalidResponse,
/// ownershipMismatch, listChanged, deleteUnconfirmed, transport) and its description.
#[derive(Clone, Debug, PartialEq)]
pub struct MErr {
    pub kind: String,
    pub desc: String,
    /// A SetError for the object itself (notCreated / notUpdated / notDestroyed): the server
    /// answered and did not make the change.
    pub set: bool,
}

impl MErr {
    pub fn new(kind: &str, desc: &str) -> MErr { MErr { kind: kind.into(), desc: desc.into(), set: false } }
    pub fn set_error(e: &Value) -> MErr { MErr { set: true, ..MErr::new(e["type"].as_str().unwrap_or("error"), e["description"].as_str().unwrap_or("")) } }
    /// A request the server turned down at the HTTP level (`status`), or one that failed on the way.
    pub fn http(status: Option<u16>, desc: &str) -> MErr {
        match status { Some(403) => MErr::new("forbidden", desc), Some(401) => MErr::new("unauthorized", desc), _ => MErr::new("transport", desc) }
    }
    /// maskedEmailUnavailable: not supported, or Stalwart Community's enterprise-only answer.
    pub fn unavailable(&self) -> bool {
        matches!(self.kind.as_str(), "unknownMethod" | "unknownCapability" | "notImplemented" | "unsupported")
            || (self.kind == "forbidden" && self.desc.to_lowercase().contains("only available in the enterprise edition"))
    }
    pub fn forbidden(&self) -> bool { self.kind == "forbidden" && !self.unavailable() }
}

/// The capability of the signed-in account, never a server-wide one or another account's.
pub fn capability(session: &Value, account: &str) -> Option<&'static str> {
    CAPABILITIES.into_iter().find(|c| session["accounts"][account]["accountCapabilities"].get(*c).is_some())
}

/// The released Stalwart schema calls the switch `enable`, the Inbuxa registry `enabled`.
pub fn enabled_prop(cap: &str) -> &'static str { if cap == "urn:stalwart:jmap" { "enable" } else { "enabled" } }

/// The answer of `method` in a response (requireResult), checked to be for `account`.
pub fn result(resp: &[Value], method: &str, account: &str) -> Result<Value, MErr> {
    if let Some(e) = resp.iter().find(|r| r[0] == "error") {
        return Err(MErr::new(e[1]["type"].as_str().unwrap_or("error"), e[1]["description"].as_str().unwrap_or("")));
    }
    let r = resp.iter().find(|r| r[0] == method).ok_or_else(|| MErr::new("invalidResponse", ""))?;
    if let Some(a) = r[1].get("accountId") { if a.as_str() != Some(account) { return Err(MErr::new("ownershipMismatch", "")); } }
    Ok(r[1].clone())
}

/// verifyRecord: a record of the account, with the fields of the right types.
pub fn verify_record(raw: &Value, account: &str, prop: &str) -> Result<Masked, MErr> {
    if let Some(a) = raw.get("accountId") { if a.as_str() != Some(account) { return Err(MErr::new("ownershipMismatch", "")); } }
    let opt = |k: &str| -> Result<Option<String>, MErr> {
        match raw.get(k) { None | Some(Value::Null) => Ok(None), Some(Value::String(s)) => Ok(Some(s.clone())), _ => Err(MErr::new("invalidResponse", "")) }
    };
    let (Some(id), Some(email), Some(enabled)) = (raw["id"].as_str(), raw["email"].as_str(), raw[prop].as_bool()) else { return Err(MErr::new("invalidResponse", "")) };
    Ok(Masked { id: id.into(), email: email.into(), for_domain: opt("forDomain")?, description: opt("description")?, enabled })
}

/// Reads the account's masked addresses through `send` (one request in, its method responses
/// out): x:MaskedEmail/query pages filtered by the account, then /get in batches. Stops only at
/// a verified complete result; an empty first page is confirmed with an empty /get, so a query
/// that works where the objects do not (Stalwart Community) does not show an empty list.
pub fn list_with(account: &str, prop: &str, max_get: usize, mut send: impl FnMut(Value) -> Result<Vec<Value>, MErr>) -> Result<Vec<Masked>, MErr> {
    let props = json!(["id", "accountId", "email", "forDomain", "description", prop]);
    let batch = max_get.clamp(1, 50);
    let mut out: Vec<Masked> = vec![];
    let mut seen = std::collections::HashSet::new();
    let (mut position, mut query_state, mut total): (usize, Option<String>, Option<u64>) = (0, None, None);
    loop {
        let r = send(json!([["x:MaskedEmail/query", {"accountId": account, "filter": {"accountId": account}, "position": position, "limit": 50, "calculateTotal": true}, "masked"]]))?;
        let page = result(&r, "x:MaskedEmail/query", account)?;
        let ids: Vec<String> = match page["ids"].as_array() {
            Some(a) if a.iter().all(|x| x.as_str().is_some_and(|s| !seen.contains(s))) => a.iter().map(|x| x.as_str().unwrap().to_string()).collect(),
            _ => return Err(MErr::new("invalidResponse", "")),
        };
        if let Some(t) = page.get("total").filter(|t| !t.is_null()) {
            let t = t.as_u64().ok_or_else(|| MErr::new("invalidResponse", ""))?;
            if (t as usize) < position + ids.len() { return Err(MErr::new("invalidResponse", "")); }
            if total.is_some_and(|x| x != t) { return Err(MErr::new("listChanged", "")); }
            total = Some(t);
        }
        let qs = page["queryState"].as_str().map(String::from);
        if let (Some(a), Some(b)) = (&query_state, &qs) { if a != b { return Err(MErr::new("listChanged", "")); } }
        query_state = qs;
        if ids.is_empty() {
            if total.is_some_and(|t| (position as u64) < t) { return Err(MErr::new("invalidResponse", "")); }
            if position == 0 {
                let r = send(json!([["x:MaskedEmail/get", {"accountId": account, "ids": [], "properties": props}, "masked"]]))?;
                let g = result(&r, "x:MaskedEmail/get", account)?;
                if g["accountId"].as_str() != Some(account) { return Err(MErr::new("ownershipMismatch", "")); }
                if g["list"].as_array().is_none_or(|l| !l.is_empty()) { return Err(MErr::new("invalidResponse", "")); }
            }
            break;
        }
        for id in &ids { seen.insert(id.clone()); }
        for chunk in ids.chunks(batch) {
            let r = send(json!([["x:MaskedEmail/get", {"accountId": account, "ids": chunk, "properties": props}, "masked"]]))?;
            let g = result(&r, "x:MaskedEmail/get", account)?;
            if g["accountId"].as_str() != Some(account) { return Err(MErr::new("ownershipMismatch", "")); }
            for raw in g["list"].as_array().ok_or_else(|| MErr::new("invalidResponse", ""))? {
                let id = raw["id"].as_str().unwrap_or("");
                if !chunk.iter().any(|c| c == id) || out.iter().any(|m| m.id == id) { return Err(MErr::new("invalidResponse", "")); }
                out.push(verify_record(raw, account, prop)?);
            }
        }
        position += ids.len();
        if total == Some(position as u64) { break; }
    }
    if total.is_some_and(|t| out.len() as u64 != t) { return Err(MErr::new("listChanged", "")); }
    Ok(out)
}

/// The create object: no description or website is null (Stalwart refuses an empty string).
pub fn create_args(account: &str, prop: &str, description: &str, website: &str) -> Value {
    let or_null = |s: &str| if s.trim().is_empty() { Value::Null } else { json!(s.trim()) };
    let mut o = json!({"description": or_null(description), "forDomain": or_null(website)});
    o[prop] = json!(true);
    json!({"accountId": account, "create": {"new": o}})
}

/// The answer to a create: the new id.
pub fn created(resp: &[Value], account: &str) -> Result<String, MErr> {
    let r = result(resp, "x:MaskedEmail/set", account)?;
    if let Some(e) = r["notCreated"].get("new") { return Err(MErr::set_error(e)); }
    r["created"]["new"]["id"].as_str().map(String::from).ok_or_else(|| MErr::new("invalidResponse", ""))
}

/// The patch for a description (cleared is null) and / or the switch; nothing is an error.
pub fn update_args(account: &str, prop: &str, id: &str, description: Option<&str>, enabled: Option<bool>) -> Result<Value, MErr> {
    let mut p = json!({});
    if let Some(d) = description { p["description"] = if d.is_empty() { Value::Null } else { json!(d) }; }
    if let Some(e) = enabled { p[prop] = json!(e); }
    if p.as_object().is_some_and(|o| o.is_empty()) { return Err(MErr::new("invalidProperties", "")); }
    Ok(json!({"accountId": account, "update": { id: p }}))
}

pub fn updated(resp: &[Value], account: &str, id: &str) -> Result<(), MErr> {
    let r = result(resp, "x:MaskedEmail/set", account)?;
    if let Some(e) = r["notUpdated"].get(id) { return Err(MErr::set_error(e)); }
    if r["updated"].get(id).is_none() { return Err(MErr::new("invalidResponse", "")); }
    Ok(())
}

/// The answer to a destroy, read strictly: the one id destroyed, or its SetError.
pub fn destroyed(resp: &[Value], account: &str, id: &str) -> Result<(), MErr> {
    let r = result(resp, "x:MaskedEmail/set", account)?;
    if r["accountId"].as_str() != Some(account) { return Err(MErr::new("ownershipMismatch", "")); }
    let d = r.get("destroyed").filter(|v| !v.is_null());
    if let Some(d) = d { if d.as_array().is_none_or(|a| a.len() > 1 || a.iter().any(|x| x.as_str() != Some(id))) { return Err(MErr::new("invalidResponse", "")); } }
    if let Some(nd) = r.get("notDestroyed").filter(|v| !v.is_null()) {
        let Some(o) = nd.as_object() else { return Err(MErr::new("invalidResponse", "")) };
        if o.keys().any(|k| k != id) { return Err(MErr::new("invalidResponse", "")); }
        if let Some(e) = o.get(id) {
            if !e["type"].is_string() { return Err(MErr::new("invalidResponse", "")); }
            if d.and_then(Value::as_array).is_some_and(|a| !a.is_empty()) { return Err(MErr::new("invalidResponse", "")); }
            return Err(MErr::set_error(e));
        }
    }
    if d.and_then(Value::as_array).is_none_or(|a| a.len() != 1) { return Err(MErr::new("invalidResponse", "")); }
    Ok(())
}

/// The account and capability a client manages masked addresses with, if any.
pub fn context(c: &Client) -> Option<(String, &'static str)> {
    let acc = c.account.clone();
    capability(&c.session, &acc).map(|cap| (acc, cap))
}

fn send(c: &Client, cap: &str, calls: Value) -> Result<Vec<Value>, MErr> {
    c.request_using_status(&["urn:ietf:params:jmap:core", cap], calls).map_err(|(status, e)| MErr::http(status, &e))
}

pub fn list(c: &Client) -> Result<Vec<Masked>, MErr> {
    let (acc, cap) = context(c).ok_or_else(|| MErr::new("unsupported", ""))?;
    list_with(&acc, enabled_prop(cap), c.max_get(), |calls| send(c, cap, calls))
}

pub fn create(c: &Client, description: &str, website: &str) -> Result<String, MErr> {
    let (acc, cap) = context(c).ok_or_else(|| MErr::new("unsupported", ""))?;
    let r = send(c, cap, json!([["x:MaskedEmail/set", create_args(&acc, enabled_prop(cap), description, website), "masked"]]))?;
    created(&r, &acc)
}

pub fn update(c: &Client, id: &str, description: Option<&str>, enabled: Option<bool>) -> Result<(), MErr> {
    let (acc, cap) = context(c).ok_or_else(|| MErr::new("unsupported", ""))?;
    let args = update_args(&acc, enabled_prop(cap), id, description, enabled)?;
    let r = send(c, cap, json!([["x:MaskedEmail/set", args, "masked"]]))?;
    updated(&r, &acc, id)
}

/// Destroy one address. Anything but a clear answer (a transport failure, an answer that does
/// not say) is `deleteUnconfirmed`: it is not retried, the list must be read again first.
pub fn destroy(c: &Client, id: &str) -> Result<(), MErr> {
    let (acc, cap) = context(c).ok_or_else(|| MErr::new("unsupported", ""))?;
    let r = send(c, cap, json!([["x:MaskedEmail/set", {"accountId": acc, "destroy": [id]}, "masked"]]));
    destroy_outcome(r.and_then(|r| destroyed(&r, &acc, id)))
}

/// What a delete's failure means (masked-email.ts:224-232): the server's SetError, a 401 or 403,
/// `forbidden` or "not supported" are definite (nothing was deleted); anything else (a method
/// error such as serverFail, a lost connection, an answer that does not say) may have deleted
/// it, so it is `deleteUnconfirmed` and is not retried.
pub fn destroy_outcome(r: Result<(), MErr>) -> Result<(), MErr> {
    match r {
        Ok(()) => Ok(()),
        Err(e) if e.set || e.kind == "forbidden" || e.kind == "unauthorized" || e.unavailable() => Err(e),
        Err(_) => Err(MErr::new("deleteUnconfirmed", "")),
    }
}

/// Bulwark's notice for a failed change (masked-addresses-section.tsx mutate).
pub fn notice(kind: &str, e: &MErr) -> &'static str {
    if e.unavailable() { return "Masked addresses aren't available for this account or server."; }
    if kind == "destroy" {
        return if e.forbidden() { "Your current session is not allowed to delete masked addresses. Other permitted actions remain available." }
            else if e.kind == "deleteUnconfirmed" { "Deletion could not be confirmed. It will not be retried. Refresh the list to check the address before any further deletion." }
            else { "The address was not deleted. Refresh the list before trying again." };
    }
    match (e.kind.as_str(), kind) {
        ("forbidden", "create") => "The server did not allow creating a masked address with this session.",
        ("forbidden", _) => "The server did not allow editing masked addresses with this session. You can still copy addresses.",
        ("overQuota", _) => "The server's masked address quota has been reached. Disabled addresses may still count towards the limit.",
        ("rateLimit", _) => "The server's creation rate limit has been reached. Try again later.",
        _ => "The change could not be confirmed. Refresh the list before trying again.",
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const ACC: &str = "a1";

    fn rec(id: &str, enabled: bool) -> Value { json!({"id": id, "email": format!("{id}@mask.example.test"), "forDomain": "shop.example", "description": null, "enable": enabled}) }

    /// A fake server: the query pages and the records it holds.
    fn server(ids: Vec<&'static str>, total: Option<u64>) -> impl FnMut(Value) -> Result<Vec<Value>, MErr> {
        move |calls: Value| {
            let (m, a) = (calls[0][0].as_str().unwrap().to_string(), calls[0][1].clone());
            assert_eq!(a["accountId"], ACC, "every call names the account");
            if m == "x:MaskedEmail/query" {
                assert_eq!(a["filter"]["accountId"], ACC);
                let pos = a["position"].as_u64().unwrap() as usize;
                let page: Vec<&str> = ids.iter().skip(pos).take(2).copied().collect();
                let mut r = json!({"accountId": ACC, "ids": page, "queryState": "q1"});
                if let Some(t) = total { r["total"] = json!(t); }
                Ok(vec![json!([m, r, "masked"])])
            } else {
                let list: Vec<Value> = a["ids"].as_array().unwrap().iter().map(|i| rec(i.as_str().unwrap(), true)).collect();
                Ok(vec![json!([m, {"accountId": ACC, "list": list}, "masked"])])
            }
        }
    }

    #[test]
    fn the_capability_is_the_accounts_own() {
        let s = json!({"capabilities": {"urn:stalwart:jmap": {}}, "accounts": {"a1": {"accountCapabilities": {"urn:inbuxa:jmap:registry": {}}}, "a2": {"accountCapabilities": {}}}});
        assert_eq!(capability(&s, "a1"), Some("urn:inbuxa:jmap:registry"));
        // a server-wide capability is not the account's
        assert_eq!(capability(&s, "a2"), None);
        assert_eq!(enabled_prop("urn:stalwart:jmap"), "enable");
        assert_eq!(enabled_prop("urn:inbuxa:jmap:registry"), "enabled");
    }

    #[test]
    fn lists_every_page_and_stops_at_the_total() {
        let l = list_with(ACC, "enable", 50, server(vec!["m1", "m2", "m3"], Some(3))).unwrap();
        assert_eq!(l.iter().map(|m| m.id.as_str()).collect::<Vec<_>>(), ["m1", "m2", "m3"]);
        assert_eq!(l[0].for_domain.as_deref(), Some("shop.example"));
        assert!(l[0].enabled);
        // without a total it reads until an empty page
        assert_eq!(list_with(ACC, "enable", 1, server(vec!["m1", "m2", "m3"], None)).unwrap().len(), 3);
        // an empty list is confirmed with an empty get
        assert_eq!(list_with(ACC, "enable", 50, server(vec![], Some(0))).unwrap(), vec![]);
    }

    #[test]
    fn answers_for_another_owner_or_a_changing_list_are_refused() {
        // a record of another account
        let r = list_with(ACC, "enable", 50, |c: Value| {
            if c[0][0] == "x:MaskedEmail/query" { Ok(vec![json!(["x:MaskedEmail/query", {"accountId": ACC, "ids": ["m1"], "total": 1}, "masked"])]) }
            else { let mut x = rec("m1", true); x["accountId"] = json!("other"); Ok(vec![json!(["x:MaskedEmail/get", {"accountId": ACC, "list": [x]}, "masked"])]) }
        });
        assert_eq!(r.unwrap_err().kind, "ownershipMismatch");
        // the answer envelope for another account
        let r = result(&[json!(["x:MaskedEmail/get", {"accountId": "other", "list": []}, "m"])], "x:MaskedEmail/get", ACC);
        assert_eq!(r.unwrap_err().kind, "ownershipMismatch");
        // the total changes between pages
        let mut n = 0;
        let r = list_with(ACC, "enable", 50, |c: Value| {
            if c[0][0] == "x:MaskedEmail/query" { n += 1; let ids = if n == 1 { json!(["m1", "m2"]) } else { json!(["m3"]) }; Ok(vec![json!(["x:MaskedEmail/query", {"accountId": ACC, "ids": ids, "total": if n == 1 { 3 } else { 4 }}, "masked"])]) }
            else { let list: Vec<Value> = c[0][1]["ids"].as_array().unwrap().iter().map(|i| rec(i.as_str().unwrap(), true)).collect(); Ok(vec![json!(["x:MaskedEmail/get", {"accountId": ACC, "list": list}, "masked"])]) }
        });
        assert_eq!(r.unwrap_err().kind, "listChanged");
        // a bad record type
        assert_eq!(verify_record(&json!({"id": "x", "email": "e@x", "enable": "yes"}), ACC, "enable").unwrap_err().kind, "invalidResponse");
    }

    #[test]
    fn not_supported_and_enterprise_only_hide_the_section() {
        let unknown = list_with(ACC, "enable", 50, |_| Ok(vec![json!(["error", {"type": "unknownMethod"}, "masked"])])).unwrap_err();
        assert!(unknown.unavailable());
        let ent = MErr::new("forbidden", "This feature is only available in the Enterprise edition");
        assert!(ent.unavailable() && !ent.forbidden());
        let perm = MErr::new("forbidden", "no");
        assert!(perm.forbidden() && !perm.unavailable());
        // Community: the query works, the get does not
        let r = list_with(ACC, "enable", 50, |c: Value| {
            if c[0][0] == "x:MaskedEmail/query" { Ok(vec![json!(["x:MaskedEmail/query", {"accountId": ACC, "ids": [], "total": 0}, "masked"])]) }
            else { Ok(vec![json!(["error", {"type": "forbidden", "description": "only available in the enterprise edition"}, "masked"])]) }
        });
        assert!(r.unwrap_err().unavailable());
    }

    #[test]
    fn request_shapes() {
        assert_eq!(create_args(ACC, "enable", "", " shop.example "), json!({"accountId": ACC, "create": {"new": {"description": null, "forDomain": "shop.example", "enable": true}}}));
        assert_eq!(update_args(ACC, "enabled", "m1", Some(""), Some(false)).unwrap(), json!({"accountId": ACC, "update": {"m1": {"description": null, "enabled": false}}}));
        assert_eq!(update_args(ACC, "enable", "m1", None, None).unwrap_err().kind, "invalidProperties");
        assert_eq!(created(&[json!(["x:MaskedEmail/set", {"accountId": ACC, "created": {"new": {"id": "m9"}}}, "m"])], ACC).unwrap(), "m9");
        assert_eq!(created(&[json!(["x:MaskedEmail/set", {"accountId": ACC, "notCreated": {"new": {"type": "overQuota"}}}, "m"])], ACC).unwrap_err().kind, "overQuota");
        assert!(updated(&[json!(["x:MaskedEmail/set", {"accountId": ACC, "updated": {"m1": null}}, "m"])], ACC, "m1").is_ok());
        assert_eq!(updated(&[json!(["x:MaskedEmail/set", {"accountId": ACC}, "m"])], ACC, "m1").unwrap_err().kind, "invalidResponse");
    }

    #[test]
    fn a_destroy_counts_only_when_clearly_done() {
        let ok = [json!(["x:MaskedEmail/set", {"accountId": ACC, "destroyed": ["m1"]}, "m"])];
        assert!(destroyed(&ok, ACC, "m1").is_ok());
        // silent, or naming another id: not done
        assert_eq!(destroyed(&[json!(["x:MaskedEmail/set", {"accountId": ACC}, "m"])], ACC, "m1").unwrap_err().kind, "invalidResponse");
        assert_eq!(destroyed(&[json!(["x:MaskedEmail/set", {"accountId": ACC, "destroyed": ["m2"]}, "m"])], ACC, "m1").unwrap_err().kind, "invalidResponse");
        // the answer must name the account
        assert_eq!(destroyed(&[json!(["x:MaskedEmail/set", {"destroyed": ["m1"]}, "m"])], ACC, "m1").unwrap_err().kind, "ownershipMismatch");
        let refused = [json!(["x:MaskedEmail/set", {"accountId": ACC, "notDestroyed": {"m1": {"type": "forbidden"}}}, "m"])];
        let e = destroyed(&refused, ACC, "m1").unwrap_err();
        assert!(e.forbidden());
        assert!(notice("destroy", &e).contains("not allowed to delete"));
        assert!(notice("destroy", &MErr::new("deleteUnconfirmed", "")).contains("will not be retried"));
        // a SetError other than forbidden is definite too
        let nf = destroyed(&[json!(["x:MaskedEmail/set", {"accountId": ACC, "notDestroyed": {"m1": {"type": "notFound"}}}, "m"])], ACC, "m1");
        assert_eq!(destroy_outcome(nf).unwrap_err().kind, "notFound");
    }

    #[test]
    fn a_failed_delete_is_unconfirmed_unless_the_answer_is_definite() {
        let kind = |r: Result<(), MErr>| destroy_outcome(r).unwrap_err().kind;
        // method errors: the server may have done it
        for t in ["serverFail", "serverPartialFail", "invalidArguments"] {
            let e = result(&[json!(["error", {"type": t}, "m"])], "x:MaskedEmail/set", ACC).unwrap_err();
            assert_eq!(kind(Err(e)), "deleteUnconfirmed", "{t}");
        }
        assert_eq!(kind(Err(MErr::http(None, "connection reset"))), "deleteUnconfirmed");
        assert_eq!(kind(Err(MErr::http(Some(500), "server error"))), "deleteUnconfirmed");
        assert_eq!(kind(Err(MErr::new("invalidResponse", ""))), "deleteUnconfirmed");
        // turned down before anything was done
        assert_eq!(kind(Err(MErr::http(Some(401), "signed out"))), "unauthorized");
        assert_eq!(kind(Err(MErr::http(Some(403), "no"))), "forbidden");
        assert_eq!(kind(Err(MErr::new("forbidden", "no"))), "forbidden");
        assert_eq!(kind(Err(MErr::new("unknownMethod", ""))), "unknownMethod");
        assert!(destroy_outcome(Ok(())).is_ok());
        assert!(notice("create", &MErr::new("rateLimit", "")).contains("rate limit"));
    }
}
