//! Rules made from a message (Bulwark lib/filters/quick-rules.ts and rules-menu.tsx): "Always
//! move messages from Anna to…", from her domain or from the list, "Always mark as read",
//! "Always tag with", "Block sender". The rule goes into the Sieve script's Bulwark metadata
//! (src/sieve.rs) at the top, or is merged into an existing one-condition rule that already
//! does the same, so one-click rules don't pile up as near duplicates.
use serde_json::{json, Value};

/// A preset from the menu.
#[derive(Clone, Debug, PartialEq)]
pub enum Preset {
    /// Move mail from the sender / their domain / the list into a folder (id, Sieve path, name).
    MoveSender(Folder),
    MoveDomain(Folder),
    MoveList(Folder),
    MarkRead,
    Tag { id: String, name: String },
    Block(Folder),
}

#[derive(Clone, Debug, PartialEq, Default)]
pub struct Folder {
    pub id: String,
    pub path: String,
    pub name: String,
}

/// What the presets are made from.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Subject {
    /// Lower-cased sender addresses, the user's own left out.
    pub senders: Vec<String>,
    pub domain: Option<String>,
    pub list_id: Option<String>,
}

pub fn normalize(a: &str) -> String { a.trim().to_lowercase() }

pub fn address_domain(a: &str) -> Option<String> {
    let at = a.rfind('@')?;
    (at > 0 && at < a.len() - 1).then(|| a[at + 1..].to_lowercase())
}

/// The list identifier of a List-Id header (RFC 2919): inside the angle brackets, else whole.
pub fn extract_list_id(raw: &str) -> Option<String> {
    let v = raw.trim();
    let id = match (v.rfind('<'), v.ends_with('>')) {
        (Some(i), true) => &v[i + 1..v.len() - 1],
        _ => v,
    }.trim();
    (!id.is_empty() && !id.chars().any(char::is_whitespace)).then(|| id.to_string())
}

/// The subject of a single message: its senders (own addresses dropped), their shared domain
/// and its List-Id.
pub fn subject_of(m: &Value, own: &[String]) -> Subject {
    let mut senders: Vec<String> = vec![];
    for f in m["from"].as_array().cloned().unwrap_or_default() {
        let a = normalize(f["email"].as_str().unwrap_or(""));
        if a.is_empty() || !a.contains('@') || own.iter().any(|o| normalize(o) == a) || senders.contains(&a) { continue; }
        senders.push(a);
    }
    let mut domain = None;
    for s in &senders {
        let d = address_domain(s);
        if d.is_none() || (domain.is_some() && domain != d) { domain = None; break; }
        domain = d;
    }
    let headers = m["headers"].as_array().cloned().unwrap_or_default();
    let list_id = crate::reader::header(&headers, "List-Id").and_then(|h| extract_list_id(&h.replace(['\r', '\n'], " ")));
    Subject { senders, domain, list_id }
}

/// The Sieve path of a folder: parent names joined with "/", the inbox as "INBOX".
/// `folders` are (id, name, parentId, role).
pub fn sieve_path(folders: &[(String, String, Option<String>, String)], id: &str) -> String {
    let mut segs = vec![];
    let mut seen = std::collections::HashSet::new();
    let mut cur = folders.iter().find(|f| f.0 == id);
    while let Some(f) = cur {
        if !seen.insert(f.0.clone()) { break; }
        segs.insert(0, if f.3 == "inbox" { "INBOX".to_string() } else { f.1.clone() });
        cur = f.2.as_ref().and_then(|p| folders.iter().find(|x| &x.0 == p));
    }
    segs.join("/")
}

fn sender_value(s: &Subject) -> Value {
    if s.senders.len() == 1 { json!(s.senders[0]) } else { json!(s.senders) }
}

/// How rule names refer to the senders.
pub fn sender_name(s: &Subject) -> String {
    if s.senders.len() == 1 { s.senders[0].clone() } else { format!("{} senders", s.senders.len()) }
}

fn move_action(f: &Folder) -> Value { json!({"type": "move", "value": f.path, "mailboxId": f.id}) }

pub fn new_id() -> String {
    let mut b = [0u8; 6];
    let _ = getrandom::getrandom(&mut b);
    format!("bw-{}", b.iter().map(|x| format!("{x:02x}")).collect::<String>())
}

/// The rule a one-click preset saves (Bulwark buildPresetRule).
pub fn preset_rule(p: &Preset, s: &Subject, id: &str) -> Value {
    let sender = sender_name(s);
    let from = json!({"field": "from", "comparator": "address_is", "value": sender_value(s)});
    let (name, cond, action, include_spam) = match p {
        Preset::MoveSender(f) => (format!("Move from {sender} to {}", f.name), from, move_action(f), false),
        Preset::MoveDomain(f) => {
            let d = s.domain.clone().unwrap_or_default();
            (format!("Move from @{d} to {}", f.name), json!({"field": "from", "comparator": "domain_is", "value": d}), move_action(f), false)
        }
        Preset::MoveList(f) => {
            let l = s.list_id.clone().unwrap_or_default();
            (format!("Move list {l} to {}", f.name), json!({"field": "header", "headerName": "List-Id", "comparator": "contains", "value": l}), move_action(f), false)
        }
        Preset::MarkRead => (format!("Mark as read from {sender}"), from, json!({"type": "mark_read"}), false),
        Preset::Tag { id: t, name: n } => (format!("Tag as {n} from {sender}"), from, json!({"type": "add_label", "value": t}), false),
        // the spam guard would skip exactly the mail this rule is for
        Preset::Block(f) => (format!("Block {sender}"), from, move_action(f), true),
    };
    let mut r = json!({"id": id, "name": name, "enabled": true, "matchType": "all", "conditions": [cond], "actions": [action], "stopProcessing": true});
    if include_spam { r["includeSpam"] = json!(true); }
    r
}

fn values(v: &Value) -> Vec<String> {
    match v {
        Value::Array(a) => a.iter().filter_map(|x| x.as_str()).filter(|x| !x.is_empty()).map(String::from).collect(),
        Value::String(s) if !s.is_empty() => vec![s.clone()],
        _ => vec![],
    }
}

fn same_condition_kind(a: &Value, b: &Value) -> bool {
    a["field"] == b["field"] && a["comparator"] == b["comparator"]
        && a["headerName"].as_str().unwrap_or("").to_lowercase() == b["headerName"].as_str().unwrap_or("").to_lowercase()
}

fn same_action(a: &Value, b: &Value) -> bool {
    if a["type"] != b["type"] { return false; }
    match a["type"].as_str().unwrap_or("") {
        "move" | "copy" => match (a["mailboxId"].as_str(), b["mailboxId"].as_str()) {
            (Some(x), Some(y)) if !x.is_empty() && !y.is_empty() => x == y,
            _ => a["value"].as_str().unwrap_or("") == b["value"].as_str().unwrap_or(""),
        },
        "add_label" | "forward" | "reject" => a["value"].as_str().unwrap_or("") == b["value"].as_str().unwrap_or(""),
        _ => true,
    }
}

fn same_actions(a: &[Value], b: &[Value]) -> bool {
    if a.len() != b.len() { return false; }
    let mut left: Vec<&Value> = b.iter().collect();
    for x in a {
        let Some(i) = left.iter().position(|y| same_action(x, y)) else { return false };
        left.remove(i);
    }
    true
}

fn has_period(r: &Value) -> bool {
    r["activeFrom"].as_str().is_some_and(|s| !s.is_empty()) || r["activeUntil"].as_str().is_some_and(|s| !s.is_empty())
}

/// What saving a one-click rule did.
#[derive(Debug, PartialEq)]
pub enum Outcome {
    /// A new rule, at the top.
    Created(Vec<Value>),
    /// An existing rule (its name) took the new values.
    Merged(Vec<Value>, String),
    /// An existing rule (its name) already covers it: nothing to write.
    Covered(String),
}

/// Bulwark applyQuickRule: an enabled Bulwark rule without a period, with a single condition of
/// the same kind and the same actions, takes the candidate's values and keeps its name and place.
pub fn apply(rules: &[Value], candidate: &Value) -> Outcome {
    let conds = candidate["conditions"].as_array().cloned().unwrap_or_default();
    let acts = candidate["actions"].as_array().cloned().unwrap_or_default();
    let target = if conds.len() == 1 {
        rules.iter().position(|r| {
            let origin_ok = r["origin"].as_str().is_none_or(|o| o == "bulwark");
            let rc = r["conditions"].as_array().cloned().unwrap_or_default();
            origin_ok && r["enabled"] != false && !has_period(r) && rc.len() == 1 && same_condition_kind(&rc[0], &conds[0])
                && same_actions(r["actions"].as_array().map(|a| a.as_slice()).unwrap_or(&[]), &acts)
        })
    } else { None };
    let Some(ti) = target else {
        let mut out = vec![candidate.clone()];
        out.extend(rules.iter().cloned());
        return Outcome::Created(out);
    };
    let t = &rules[ti];
    let name = t["name"].as_str().unwrap_or("").to_string();
    let mut existing = values(&t["conditions"][0]["value"]);
    let mut known: Vec<String> = existing.iter().map(|v| v.to_lowercase()).collect();
    let mut added = 0;
    for v in values(&conds[0]["value"]) {
        if known.contains(&v.to_lowercase()) { continue; }
        known.push(v.to_lowercase());
        existing.push(v);
        added += 1;
    }
    if added == 0 { return Outcome::Covered(name); }
    let mut merged = t.clone();
    merged["conditions"][0]["value"] = json!(existing);
    let mut out = rules.to_vec();
    out[ti] = merged;
    Outcome::Merged(out, name)
}

/// What saving `candidate` into the parsed script does: Err for a hand-edited script (it is
/// never rewritten), Ok(Err(toast)) when nothing needs saving, else the new rules and the toast.
#[allow(clippy::type_complexity)]
pub fn plan(p: &crate::sieve::Parsed, candidate: &Value) -> Result<Result<(Vec<Value>, String), String>, String> {
    if p.locked { return Err("Your filters were edited by hand. Change them in Settings › Filters.".into()); }
    let rules = p.metadata["rules"].as_array().cloned().unwrap_or_default();
    Ok(match apply(&rules, candidate) {
        Outcome::Covered(n) => Err(format!("Already covered by \"{n}\"")),
        Outcome::Created(r) => Ok((r, format!("Rule \"{}\" created", candidate["name"].as_str().unwrap_or("")))),
        Outcome::Merged(r, n) => Ok((r, format!("Added to rule \"{n}\""))),
    })
}

/// Read the active script, apply the rule and save it back (as the rule editor does).
pub fn save(c: &crate::jmap::Client, candidate: &Value) -> Result<String, String> {
    let sacc = c.account_for("urn:ietf:params:jmap:sieve");
    let r = c.call(json!([["SieveScript/get", {"accountId": sacc, "ids": null, "properties": ["id", "name", "blobId", "isActive"]}, "s"]]))?;
    let scripts = r[0][1]["list"].as_array().cloned().unwrap_or_default();
    let text = match scripts.iter().find(|x| x["isActive"] == true).or(scripts.first()).and_then(|x| x["blobId"].as_str()) {
        Some(b) => String::from_utf8_lossy(&c.download(b, "script.sieve", "application/sieve")?).to_string(),
        None => String::new(),
    };
    let p = crate::sieve::parse(&text);
    let (rules, msg) = match plan(&p, candidate)? {
        Err(covered) => return Ok(covered),
        Ok(x) => x,
    };
    let ext: Vec<String> = c.session["capabilities"]["urn:ietf:params:jmap:sieve"]["sieveExtensions"].as_array()
        .or(c.session["accounts"][&sacc]["accountCapabilities"]["urn:ietf:params:jmap:sieve"]["sieveExtensions"].as_array())
        .map(|x| x.iter().filter_map(|v| v.as_str().map(String::from)).collect()).unwrap_or_default();
    crate::server_settings::save_script(c, &crate::sieve::generate_meta(&crate::sieve::with_rules(&p.metadata, &rules), &p.external, &ext), &scripts)?;
    Ok(msg)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn hand_edited_script_is_refused() {
        let cand = json!({"name": "x", "conditions": [], "actions": []});
        let hand = crate::sieve::parse("/* @metadata:begin
{\"version\": 9, \"rules\": []}
@metadata:end */
if true { stop; }");
        assert!(plan(&hand, &cand).unwrap_err().contains("edited by hand"));
        let broken = crate::sieve::parse("/* @metadata:begin
not json
@metadata:end */");
        assert!(plan(&broken, &cand).is_err());
        let fresh = crate::sieve::parse("");
        assert!(plan(&fresh, &cand).unwrap().is_ok(), "an empty script takes the rule");
    }

    fn subj(s: &[&str]) -> Subject {
        Subject { senders: s.iter().map(|x| x.to_string()).collect(), domain: Some("acme.com".into()), list_id: Some("news.acme.com".into()) }
    }
    fn news() -> Folder { Folder { id: "m1".into(), path: "INBOX/News".into(), name: "News".into() } }

    #[test]
    fn subject_from_a_message() {
        let m = json!({"from": [{"email": "Anna@Acme.com", "name": "Anna"}],
            "headers": [{"name": "List-Id", "value": " Weekly news <news.acme.com>"}]});
        let s = subject_of(&m, &["me@x.org".into()]);
        assert_eq!(s.senders, vec!["anna@acme.com"]);
        assert_eq!(s.domain.as_deref(), Some("acme.com"));
        assert_eq!(s.list_id.as_deref(), Some("news.acme.com"));
        let own = subject_of(&json!({"from": [{"email": "me@x.org"}]}), &["ME@x.org".into()]);
        assert!(own.senders.is_empty() && own.domain.is_none());
        assert_eq!(extract_list_id("plain.list.id"), Some("plain.list.id".into()));
        assert_eq!(extract_list_id("has space"), None);
    }

    #[test]
    fn sieve_paths() {
        let f = vec![("i".to_string(), "Inbox".to_string(), None, "inbox".to_string()),
            ("n".to_string(), "News".to_string(), Some("i".to_string()), String::new()),
            ("a".to_string(), "Archive".to_string(), None, "archive".to_string())];
        assert_eq!(sieve_path(&f, "n"), "INBOX/News");
        assert_eq!(sieve_path(&f, "a"), "Archive");
    }

    #[test]
    fn presets_build_bulwark_rules() {
        let s = subj(&["anna@acme.com"]);
        let r = preset_rule(&Preset::MoveSender(news()), &s, "r1");
        assert_eq!(r, json!({"id": "r1", "name": "Move from anna@acme.com to News", "enabled": true, "matchType": "all",
            "conditions": [{"field": "from", "comparator": "address_is", "value": "anna@acme.com"}],
            "actions": [{"type": "move", "value": "INBOX/News", "mailboxId": "m1"}], "stopProcessing": true}));
        let d = preset_rule(&Preset::MoveDomain(news()), &s, "r2");
        assert_eq!(d["conditions"][0], json!({"field": "from", "comparator": "domain_is", "value": "acme.com"}));
        assert_eq!(d["name"], "Move from @acme.com to News");
        let l = preset_rule(&Preset::MoveList(news()), &s, "r3");
        assert_eq!(l["conditions"][0], json!({"field": "header", "headerName": "List-Id", "comparator": "contains", "value": "news.acme.com"}));
        let b = preset_rule(&Preset::Block(Folder { id: "j".into(), path: "Junk Mail".into(), name: "Junk Mail".into() }), &s, "r4");
        assert_eq!(b["includeSpam"], true);
        assert_eq!(b["name"], "Block anna@acme.com");
        let t = preset_rule(&Preset::Tag { id: "work".into(), name: "Work".into() }, &subj(&["a@acme.com", "b@acme.com"]), "r5");
        assert_eq!(t["name"], "Tag as Work from 2 senders");
        assert_eq!(t["conditions"][0]["value"], json!(["a@acme.com", "b@acme.com"]));
        assert_eq!(t["actions"][0], json!({"type": "add_label", "value": "work"}));
        // the generator turns it into Sieve
        let script = crate::sieve::generate(&[r], "");
        assert!(script.contains(r#"address :is "From" "anna@acme.com""#), "{script}");
        assert!(script.contains(r#"fileinto "INBOX/News";"#), "{script}");
    }

    #[test]
    fn apply_creates_merges_or_covers() {
        let s = subj(&["anna@acme.com"]);
        let first = preset_rule(&Preset::MoveSender(news()), &s, "r1");
        let Outcome::Created(rules) = apply(&[], &first) else { panic!() };
        assert_eq!(rules.len(), 1);
        // the same sender again: covered
        assert_eq!(apply(&rules, &preset_rule(&Preset::MoveSender(news()), &s, "r2")), Outcome::Covered("Move from anna@acme.com to News".into()));
        // another sender to the same folder: merged into the first rule, keeping its name
        let bob = preset_rule(&Preset::MoveSender(news()), &subj(&["Bob@acme.com"]), "r3");
        let Outcome::Merged(merged, name) = apply(&rules, &bob) else { panic!() };
        assert_eq!(name, "Move from anna@acme.com to News");
        assert_eq!(merged.len(), 1);
        assert_eq!(merged[0]["conditions"][0]["value"], json!(["anna@acme.com", "Bob@acme.com"]));
        // another folder: a new rule at the top
        let other = Folder { id: "m2".into(), path: "Other".into(), name: "Other".into() };
        let Outcome::Created(two) = apply(&merged, &preset_rule(&Preset::MoveSender(other), &s, "r4")) else { panic!() };
        assert_eq!(two[0]["id"], "r4");
        // a disabled rule, or one with a period, is never the one
        let mut off = rules.clone();
        off[0]["enabled"] = json!(false);
        assert!(matches!(apply(&off, &bob), Outcome::Created(_)));
        let mut timed = rules.clone();
        timed[0]["activeUntil"] = json!("2026-12-01T00:00:00.000Z");
        assert!(matches!(apply(&timed, &bob), Outcome::Created(_)));
    }
}
