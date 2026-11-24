//! Copy messages to another account (Bulwark stores/email-store.ts crossAccountMoveEmails with
//! keepOriginal, copyEmailsToAccount; lib/jmap/client.ts copyEmailAcrossAccounts, importRawEmail;
//! hooks/use-message-file-actions.ts useCopyToAccount).
//!
//! Within one login the server copies the message itself (Email/copy, RFC 8621 §4.7: the
//! source's keywords are sent along, since a copy keeps none unless the create sets them).
//! Between two logins no server can see both accounts, so the message's raw blob is downloaded
//! from the first and filed into the second with Email/import, keeping keywords and arrival
//! time. Each message goes on its own, so one that fails does not hold back the others; a copy
//! never removes the original. With `keep_original` off (a move between accounts) the source is
//! destroyed only after the copy exists, and never with `onSuccessDestroyOriginal`: some servers
//! destroy the source even when the copy failed (Bulwark client.ts:12258-12261).
use crate::jmap::Client;
use serde_json::{json, Value};

/// Where a message lives: the remembered login that reaches it, and its JMAP account (the
/// login's own, or a shared / group account of it).
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct Place { pub login: String, pub account: String }

/// The folder a copy is filed into.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Dest { pub place: Place, pub mailbox: String }

/// How a message gets from `src` to `dst`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Route {
    /// Already in that account: nothing to copy.
    Same,
    /// One login reaches both accounts: Email/copy.
    Server,
    /// Two logins: download the blob and Email/import it.
    Blob,
}

pub fn route(src: &Place, dst: &Place) -> Route {
    if src.login == dst.login {
        if src.account == dst.account { Route::Same } else { Route::Server }
    } else {
        Route::Blob
    }
}

/// Messages of one source place that take the same route.
#[derive(Debug, PartialEq)]
pub struct Group { pub from: Place, pub route: Route, pub ids: Vec<String> }

/// What to do for a selection: the groups to run, in order of first appearance.
#[derive(Debug, PartialEq, Default)]
pub struct Plan { pub groups: Vec<Group>, pub skipped: Vec<(String, String)> }

/// Group `items` (server id, place) by source place; those already in the destination account
/// are skipped with a reason (Bulwark: "The messages are already in that account").
pub fn plan(items: &[(String, Place)], dest: &Place) -> Plan {
    let mut p = Plan::default();
    for (id, place) in items {
        let r = route(place, dest);
        if r == Route::Same {
            p.skipped.push((id.clone(), "The message is already in that account".to_string()));
            continue;
        }
        match p.groups.iter_mut().find(|g| g.from == *place) {
            Some(g) => g.ids.push(id.clone()),
            None => p.groups.push(Group { from: place.clone(), route: r, ids: vec![id.clone()] }),
        }
    }
    p
}

/// The Email/get that reads what a copy carries over.
pub fn read_request(account: &str, id: &str) -> Value {
    json!([["Email/get", {"accountId": account, "ids": [id], "properties": ["keywords", "blobId", "receivedAt"]}, "0"]])
}

/// Email/copy of one message into `mailbox` of another account of the same login. `keywords` is
/// every keyword of the source, the server's attachment ones included.
pub fn copy_request(from: &str, to: &str, mailbox: &str, id: &str, keywords: &Value) -> Value {
    json!([["Email/copy", {
        "fromAccountId": from, "accountId": to,
        "create": {"c": {"id": id, "mailboxIds": { mailbox: true }, "keywords": keywords}}
    }, "0"]])
}

/// Email/import of an uploaded message, with the original's keywords and arrival time (a copy
/// without it sorts as today's mail).
pub fn import_request(account: &str, blob: &str, mailbox: &str, keywords: &Value, received_at: Option<&str>) -> Value {
    let mut e = json!({"blobId": blob, "mailboxIds": { mailbox: true }, "keywords": keywords});
    if let Some(r) = received_at { e["receivedAt"] = json!(r); }
    json!([["Email/import", {"accountId": account, "emails": {"c": e}}, "0"]])
}

pub fn destroy_request(account: &str, id: &str) -> Value {
    json!([["Email/set", {"accountId": account, "destroy": [id]}, "0"]])
}

/// The id a copy / import created, or why it did not (the server's own description first).
pub fn created_id(resp: &[Value]) -> Result<String, String> {
    let r = resp.first().ok_or("The server sent no answer.")?;
    if r[0] == "error" { return Err(err_text(&r[1], "The server refused the copy.")); }
    let body = &r[1];
    if let Some(e) = body["notCreated"]["c"].as_object().map(|_| &body["notCreated"]["c"]) {
        return Err(err_text(e, "The server could not create the copy."));
    }
    body["created"]["c"]["id"].as_str().map(String::from).ok_or_else(|| "The server did not return the new message.".to_string())
}

fn err_text(e: &Value, fallback: &str) -> String {
    ["description", "type"].iter().find_map(|k| e[*k].as_str().filter(|s| !s.is_empty())).unwrap_or(fallback).to_string()
}

/// What the copy needs from a server (a trait so the flow can be tested without one).
pub trait Side {
    fn call(&self, calls: Value) -> Result<Vec<Value>, String>;
    fn download(&self, account: &str, blob: &str) -> Result<Vec<u8>, String>;
    fn upload(&self, account: &str, data: Vec<u8>) -> Result<String, String>;
}

impl Side for Client {
    fn call(&self, calls: Value) -> Result<Vec<Value>, String> { Client::call(self, calls) }
    fn download(&self, account: &str, blob: &str) -> Result<Vec<u8>, String> {
        Client::download_from(self, account, blob, "message.eml", "message/rfc822")
    }
    fn upload(&self, account: &str, data: Vec<u8>) -> Result<String, String> {
        Client::upload_to(self, account, data, "message/rfc822").map(|r| r.0)
    }
}

/// Copy one message; returns the new message's id. Nothing is removed from the source unless
/// `keep_original` is off, and then only once the copy exists.
pub fn copy_one(src: &dyn Side, dst: &dyn Side, route: Route, from: &Place, to: &Dest, id: &str, keep_original: bool) -> Result<String, String> {
    if route == Route::Same { return Err("The message is already in that account".into()); }
    let r = src.call(read_request(&from.account, id))?;
    let m = r.first().map(|x| &x[1]["list"][0]).filter(|m| m.is_object()).ok_or("The message is gone.")?;
    let keywords = if m["keywords"].is_object() { m["keywords"].clone() } else { json!({}) };
    let new_id = match route {
        Route::Server => created_id(&src.call(copy_request(&from.account, &to.place.account, &to.mailbox, id, &keywords))?)?,
        _ => {
            let blob = m["blobId"].as_str().filter(|b| !b.is_empty()).ok_or("The message has no raw copy to transfer.")?;
            let data = src.download(&from.account, blob)?;
            let up = dst.upload(&to.place.account, data)?;
            created_id(&dst.call(import_request(&to.place.account, &up, &to.mailbox, &keywords, m["receivedAt"].as_str()))?)?
        }
    };
    if !keep_original {
        let d = src.call(destroy_request(&from.account, id))?;
        let ok = d.first().is_some_and(|x| x[1]["destroyed"].as_array().is_some_and(|a| a.iter().any(|v| v == id)));
        if !ok { return Err("Copied, but the original could not be removed.".into()); }
    }
    Ok(new_id)
}

/// What a run did, per message.
#[derive(Debug, Default, PartialEq)]
pub struct Report { pub copied: Vec<String>, pub failed: Vec<(String, String)> }

/// Run a plan. `side` gives the server of a login (None: that login is not connected).
pub fn run<'a>(plan: &Plan, dest: &Dest, dst: &dyn Side, side: &dyn Fn(&str) -> Option<&'a dyn Side>, keep_original: bool) -> Report {
    let mut rep = Report { failed: plan.skipped.clone(), ..Report::default() };
    for g in &plan.groups {
        let Some(src) = side(&g.from.login) else {
            rep.failed.extend(g.ids.iter().map(|i| (i.clone(), "The source account is not connected".to_string())));
            continue;
        };
        for id in &g.ids {
            match copy_one(src, dst, g.route, &g.from, dest, id, keep_original) {
                Ok(_) => rep.copied.push(id.clone()),
                Err(e) => rep.failed.push((id.clone(), e)),
            }
        }
    }
    rep
}

/// The toast for a finished run (Bulwark: "{count} emails copied" / "Failed to copy N email(s);
/// first error: ..."). Several failures are reported together with the first reason.
pub fn summary(rep: &Report, keep_original: bool) -> String {
    let n = rep.copied.len();
    let verb = if keep_original { "copied" } else { "moved" };
    let noun = |k: usize| if k == 1 { "message" } else { "messages" };
    match (n, rep.failed.len()) {
        (0, 0) => "Nothing to copy.".to_string(),
        (n, 0) => format!("{n} {} {verb}", noun(n)),
        (0, 1) => format!("Could not {}: {}", if keep_original { "copy" } else { "move" }, rep.failed[0].1),
        (0, f) => format!("Could not {} {f} messages; first error: {}", if keep_original { "copy" } else { "move" }, rep.failed[0].1),
        (n, f) => format!("{n} of {} messages {verb}; {f} failed, first error: {}", n + f, rep.failed[0].1),
    }
}

/// Folders of an account in tree order for a picker: top level by role (inbox first) then
/// name, each folder's children right under it, with the depth to indent by. Input rows are
/// Mailbox objects; folders the user may not add to are left out.
pub fn folder_choices(list: &[Value]) -> Vec<(String, String, usize)> {
    const ORDER: [&str; 6] = ["inbox", "drafts", "sent", "archive", "junk", "trash"];
    let rank = |m: &Value| ORDER.iter().position(|r| m["role"] == *r).unwrap_or(ORDER.len());
    let name = |m: &Value| m["name"].as_str().unwrap_or("").to_lowercase();
    let ids: std::collections::HashSet<&str> = list.iter().filter_map(|m| m["id"].as_str()).collect();
    let mut out = vec![];
    fn walk(list: &[Value], parent: Option<&str>, ids: &std::collections::HashSet<&str>, depth: usize,
            rank: &dyn Fn(&Value) -> usize, name: &dyn Fn(&Value) -> String, out: &mut Vec<(String, String, usize)>) {
        let mut kids: Vec<&Value> = list.iter().filter(|m| match (m["parentId"].as_str(), parent) {
            (None, None) => true,
            // a folder whose parent is not listed (not shared with us) sits at the top
            (Some(p), None) => !ids.contains(p),
            (Some(p), Some(q)) => p == q,
            _ => false,
        }).collect();
        kids.sort_by_key(|m| (if depth == 0 { rank(m) } else { 0 }, name(m)));
        for m in kids {
            let Some(id) = m["id"].as_str() else { continue };
            if depth < 16 {
                if m["myRights"]["mayAddItems"] != false { out.push((id.to_string(), m["name"].as_str().unwrap_or("").to_string(), depth)); }
                walk(list, Some(id), ids, depth + 1, rank, name, out);
            }
        }
    }
    walk(list, None, &ids, 0, &rank, &name, &mut out);
    out
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::cell::RefCell;

    fn place(l: &str, a: &str) -> Place { Place { login: l.into(), account: a.into() } }

    #[test]
    fn routes_follow_login_and_account() {
        assert_eq!(route(&place("a@x", "A"), &place("a@x", "A")), Route::Same);
        assert_eq!(route(&place("a@x", "A"), &place("a@x", "G")), Route::Server);
        assert_eq!(route(&place("a@x", "A"), &place("b@y", "A")), Route::Blob);
    }

    #[test]
    fn plan_groups_by_source_and_skips_messages_already_there() {
        let items = vec![
            ("1".to_string(), place("a@x", "A")), ("2".to_string(), place("b@y", "B")),
            ("3".to_string(), place("a@x", "A")), ("4".to_string(), place("b@y", "B2")),
        ];
        let p = plan(&items, &place("b@y", "B"));
        assert_eq!(p.skipped.len(), 1);
        assert_eq!(p.skipped[0].0, "2");
        assert_eq!(p.groups.len(), 2);
        assert_eq!((p.groups[0].route, p.groups[0].ids.clone()), (Route::Blob, vec!["1".to_string(), "3".to_string()]));
        assert_eq!((p.groups[1].route, p.groups[1].ids.clone()), (Route::Server, vec!["4".to_string()]));
    }

    #[test]
    fn request_shapes() {
        let kw = json!({"$seen": true, "$hasattachment": true});
        assert_eq!(copy_request("A", "G", "m1", "e1", &kw), json!([["Email/copy", {
            "fromAccountId": "A", "accountId": "G",
            "create": {"c": {"id": "e1", "mailboxIds": {"m1": true}, "keywords": {"$seen": true, "$hasattachment": true}}}
        }, "0"]]));
        // no onSuccessDestroyOriginal anywhere: the source goes only after the copy exists
        assert!(!copy_request("A", "G", "m1", "e1", &kw).to_string().contains("onSuccessDestroyOriginal"));
        assert_eq!(import_request("B", "blob9", "m2", &kw, Some("2024-05-01T10:00:00Z")), json!([["Email/import", {
            "accountId": "B",
            "emails": {"c": {"blobId": "blob9", "mailboxIds": {"m2": true}, "keywords": {"$seen": true, "$hasattachment": true}, "receivedAt": "2024-05-01T10:00:00Z"}}
        }, "0"]]));
        assert!(import_request("B", "b", "m", &kw, None)[0][1]["emails"]["c"].get("receivedAt").is_none());
        assert_eq!(destroy_request("A", "e1"), json!([["Email/set", {"accountId": "A", "destroy": ["e1"]}, "0"]]));
        assert_eq!(read_request("A", "e1")[0][1]["properties"], json!(["keywords", "blobId", "receivedAt"]));
    }

    #[test]
    fn created_id_reads_success_and_every_failure() {
        assert_eq!(created_id(&[json!(["Email/copy", {"created": {"c": {"id": "n1"}}}, "0"])]), Ok("n1".to_string()));
        assert_eq!(created_id(&[json!(["Email/copy", {"notCreated": {"c": {"type": "overQuota", "description": "Over quota"}}}, "0"])]), Err("Over quota".to_string()));
        assert_eq!(created_id(&[json!(["Email/copy", {"notCreated": {"c": {"type": "notFound"}}}, "0"])]), Err("notFound".to_string()));
        assert_eq!(created_id(&[json!(["error", {"type": "forbidden", "description": "No access"}, "0"])]), Err("No access".to_string()));
        assert!(created_id(&[json!(["Email/copy", {}, "0"])]).is_err());
        assert!(created_id(&[]).is_err());
    }

    /// A fake server: logs every call and answers by method.
    struct Fake { log: RefCell<Vec<String>>, fail_copy: bool, fail_destroy: bool, gone: Vec<String> }
    impl Fake { fn new() -> Fake { Fake { log: RefCell::new(vec![]), fail_copy: false, fail_destroy: false, gone: vec![] } } }
    impl Side for Fake {
        fn call(&self, calls: Value) -> Result<Vec<Value>, String> {
            let (m, a) = (calls[0][0].as_str().unwrap().to_string(), &calls[0][1]);
            self.log.borrow_mut().push(format!("{m} {}", a["accountId"].as_str().unwrap_or("")));
            Ok(vec![match m.as_str() {
                "Email/get" => {
                    let id = a["ids"][0].as_str().unwrap();
                    if self.gone.iter().any(|g| g == id) { json!(["Email/get", {"list": [], "notFound": [id]}, "0"]) }
                    else { json!(["Email/get", {"list": [{"id": id, "keywords": {"$seen": true}, "blobId": "b1", "receivedAt": "2024-05-01T10:00:00Z"}]}, "0"]) }
                }
                "Email/copy" | "Email/import" if self.fail_copy => json!([m, {"notCreated": {"c": {"type": "overQuota"}}}, "0"]),
                "Email/copy" | "Email/import" => json!([m, {"created": {"c": {"id": "new"}}}, "0"]),
                "Email/set" if self.fail_destroy => json!([m, {"notDestroyed": {"x": {}}}, "0"]),
                _ => json!([m, {"destroyed": a["destroy"].clone()}, "0"]),
            }])
        }
        fn download(&self, account: &str, blob: &str) -> Result<Vec<u8>, String> { self.log.borrow_mut().push(format!("download {account} {blob}")); Ok(b"raw".to_vec()) }
        fn upload(&self, account: &str, data: Vec<u8>) -> Result<String, String> { self.log.borrow_mut().push(format!("upload {account} {}", data.len())); Ok("up1".into()) }
    }

    fn dest() -> Dest { Dest { place: place("b@y", "B"), mailbox: "m2".into() } }

    #[test]
    fn same_login_copies_on_the_server_and_keeps_the_original() {
        let (s, d) = (Fake::new(), Fake::new());
        let to = Dest { place: place("a@x", "G"), mailbox: "m2".into() };
        let id = copy_one(&s, &d, Route::Server, &place("a@x", "A"), &to, "e1", true).unwrap();
        assert_eq!(id, "new");
        assert_eq!(*s.log.borrow(), vec!["Email/get A", "Email/copy G"]);
        assert!(d.log.borrow().is_empty());
    }

    #[test]
    fn two_logins_transfer_the_blob_and_a_move_destroys_only_afterwards() {
        let (s, d) = (Fake::new(), Fake::new());
        copy_one(&s, &d, Route::Blob, &place("a@x", "A"), &dest(), "e1", false).unwrap();
        assert_eq!(*s.log.borrow(), vec!["Email/get A", "download A b1", "Email/set A"]);
        assert_eq!(*d.log.borrow(), vec!["upload B 3", "Email/import B"]);
    }

    #[test]
    fn a_failed_copy_deletes_nothing() {
        let (s, mut d) = (Fake::new(), Fake::new());
        d.fail_copy = true;
        let e = copy_one(&s, &d, Route::Blob, &place("a@x", "A"), &dest(), "e1", false).unwrap_err();
        assert_eq!(e, "overQuota");
        assert!(!s.log.borrow().iter().any(|l| l.starts_with("Email/set")));
        // the same-login route: Email/copy refused, no destroy either
        let mut s2 = Fake::new();
        s2.fail_copy = true;
        assert!(copy_one(&s2, &d, Route::Server, &place("a@x", "A"), &Dest { place: place("a@x", "G"), mailbox: "m".into() }, "e1", false).is_err());
        assert!(!s2.log.borrow().iter().any(|l| l.starts_with("Email/set")));
    }

    #[test]
    fn a_move_whose_original_stays_says_so() {
        let (mut s, d) = (Fake::new(), Fake::new());
        s.fail_destroy = true;
        let e = copy_one(&s, &d, Route::Blob, &place("a@x", "A"), &dest(), "e1", false).unwrap_err();
        assert!(e.contains("original"));
    }

    #[test]
    fn run_reports_each_message_and_carries_on_after_a_failure() {
        let mut s = Fake::new();
        s.gone = vec!["e2".into()];
        let d = Fake::new();
        let items = vec![("e1".to_string(), place("a@x", "A")), ("e2".to_string(), place("a@x", "A")), ("e3".to_string(), place("c@z", "C")), ("e4".to_string(), place("b@y", "B"))];
        let p = plan(&items, &dest().place);
        let side = |l: &str| -> Option<&dyn Side> { if l == "a@x" { Some(&s) } else { None } };
        let rep = run(&p, &dest(), &d, &side, true);
        assert_eq!(rep.copied, vec!["e1".to_string()]);
        assert_eq!(rep.failed.len(), 3);
        assert!(rep.failed.iter().any(|f| f.0 == "e2" && f.1 == "The message is gone."));
        assert!(rep.failed.iter().any(|f| f.0 == "e3" && f.1.contains("not connected")));
        assert!(rep.failed.iter().any(|f| f.0 == "e4" && f.1.contains("already")));
        assert_eq!(summary(&rep, true), "1 of 4 messages copied; 3 failed, first error: The message is already in that account");
    }

    #[test]
    fn summaries() {
        let ok = |n: usize| Report { copied: (0..n).map(|i| i.to_string()).collect(), failed: vec![] };
        assert_eq!(summary(&ok(1), true), "1 message copied");
        assert_eq!(summary(&ok(3), true), "3 messages copied");
        assert_eq!(summary(&ok(2), false), "2 messages moved");
        let bad = Report { copied: vec![], failed: vec![("a".into(), "Over quota".into())] };
        assert_eq!(summary(&bad, true), "Could not copy: Over quota");
        assert_eq!(summary(&Report::default(), true), "Nothing to copy.");
    }

    #[test]
    fn folder_choices_are_in_tree_order_and_skip_read_only_folders() {
        let l = vec![
            json!({"id": "t", "name": "Trash", "role": "trash"}),
            json!({"id": "c", "name": "Child", "parentId": "z"}),
            json!({"id": "z", "name": "Zeta"}),
            json!({"id": "i", "name": "Inbox", "role": "inbox"}),
            json!({"id": "ro", "name": "Shared", "myRights": {"mayAddItems": false}}),
            json!({"id": "o", "name": "Orphan", "parentId": "not-listed"}),
        ];
        let got: Vec<(String, usize)> = folder_choices(&l).into_iter().map(|(id, _, d)| (id, d)).collect();
        assert_eq!(got, vec![("i".to_string(), 0), ("t".to_string(), 0), ("o".to_string(), 0), ("z".to_string(), 0), ("c".to_string(), 1)]);
    }
}
