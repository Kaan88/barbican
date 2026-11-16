//! Duplicate contacts: finding them and merging several cards into one (Bulwark
//! lib/contacts/merge.ts). The most recently changed card stays and gets every entry the
//! others have and it lacks; single fields only when it has none. The others are deleted
//! and their group memberships move to the kept card.
use serde_json::{json, Map, Value};

fn fold(t: &str) -> String {
    // strip accents for the common Latin letters (NFKD without a Unicode table)
    let base: String = t.chars().map(|c| match c {
        'à'..='å' | 'ā' | 'ă' | 'ą' => 'a', 'À'..='Å' => 'a', 'ç' | 'ć' | 'č' | 'Ç' => 'c', 'è'..='ë' | 'ē' | 'ę' | 'ě' | 'È'..='Ë' => 'e',
        'ì'..='ï' | 'ı' | 'İ' | 'Ì'..='Ï' => 'i', 'ñ' | 'ń' | 'Ñ' => 'n', 'ò'..='ö' | 'ø' | 'ő' | 'Ò'..='Ö' | 'Ø' => 'o', 'ù'..='ü' | 'ű' | 'Ù'..='Ü' => 'u',
        'ý' | 'ÿ' | 'Ý' => 'y', 'ş' | 'ś' | 'š' | 'Ş' | 'Š' => 's', 'ğ' | 'Ğ' => 'g', 'ž' | 'ź' | 'ż' | 'Ž' => 'z', 'ł' | 'Ł' => 'l', 'ß' => 's',
        c => c,
    }).collect();
    base.to_lowercase().split_whitespace().collect::<Vec<_>>().join(" ")
}

/// The name as a key: case, accents, order and commas do not count.
pub fn name_key(c: &Value) -> String {
    let parts: Vec<String> = c["name"]["components"].as_array().map(|a| a.iter()
        .filter(|x| matches!(x["kind"].as_str(), Some("given" | "surname" | "given2")))
        .filter_map(|x| x["value"].as_str().map(String::from)).collect()).unwrap_or_default();
    let text = if parts.is_empty() { c["name"]["full"].as_str().unwrap_or("").to_string() } else { parts.join(" ") };
    let mut words: Vec<String> = fold(&text).split([' ', ',']).filter(|w| !w.is_empty()).map(String::from).collect();
    words.sort();
    let k = words.join(" ");
    if k.chars().count() >= 3 { k } else { String::new() }
}

/// The phone's last eight digits, so "+49 30 550000" and "030 550000" match.
pub fn phone_key(n: &str) -> String {
    let d: String = n.chars().filter(|c| c.is_ascii_digit()).collect();
    if d.len() >= 7 { d[d.len().saturating_sub(8)..].to_string() } else { String::new() }
}

fn entries(c: &Value, k: &str) -> Vec<Value> { c[k].as_object().map(|o| o.values().cloned().collect()).unwrap_or_default() }

/// Groups (indexes into `cards`) of likely duplicates: the same name, email or phone.
pub fn find_groups(cards: &[Value]) -> Vec<Vec<usize>> {
    let idx: Vec<usize> = (0..cards.len()).filter(|i| cards[*i]["kind"] != "group").collect();
    let mut parent: Vec<usize> = (0..cards.len()).collect();
    fn find(p: &mut Vec<usize>, i: usize) -> usize { if p[i] == i { i } else { let r = find(p, p[i]); p[i] = r; r } }
    let mut seen: std::collections::HashMap<String, usize> = Default::default();
    for &i in &idx {
        let c = &cards[i];
        let mut keys = vec![];
        let n = name_key(c);
        if !n.is_empty() { keys.push(format!("n|{n}")); }
        for e in entries(c, "emails") { if let Some(a) = e["address"].as_str().map(|a| a.trim().to_lowercase()).filter(|a| !a.is_empty()) { keys.push(format!("e|{a}")); } }
        for p in entries(c, "phones") { let k = phone_key(p["number"].as_str().unwrap_or("")); if !k.is_empty() { keys.push(format!("p|{k}")); } }
        for k in keys {
            match seen.get(&k) {
                None => { seen.insert(k, i); }
                Some(&f) => { let (a, b) = (find(&mut parent, i), find(&mut parent, f)); parent[a] = b; }
            }
        }
    }
    let mut groups: Vec<(usize, Vec<usize>)> = vec![];
    for &i in &idx {
        let r = find(&mut parent, i);
        match groups.iter_mut().find(|g| g.0 == r) { Some(g) => g.1.push(i), None => groups.push((r, vec![i])) }
    }
    groups.into_iter().map(|g| g.1).filter(|g| g.len() > 1).collect()
}

const MULTI: [&str; 17] = ["emails", "phones", "addresses", "onlineServices", "notes", "nicknames", "organizations", "titles", "anniversaries",
    "personalInfo", "relatedTo", "cryptoKeys", "links", "directories", "preferredLanguages", "calendars", "schedulingAddresses"];
const SINGLE: [&str; 3] = ["name", "language", "speakToAs"];

/// JSON with sorted keys and without `pref`, to compare entries.
fn stable(v: &Value) -> String {
    match v {
        Value::Array(a) => format!("[{}]", a.iter().map(stable).collect::<Vec<_>>().join(",")),
        Value::Object(o) => {
            let mut k: Vec<&String> = o.keys().filter(|k| *k != "pref" && !o[*k].is_null()).collect();
            k.sort();
            format!("{{{}}}", k.iter().map(|k| format!("{}:{}", json!(k), stable(&o[*k]))).collect::<Vec<_>>().join(","))
        }
        x => x.to_string(),
    }
}

fn address_text(e: &Value) -> String {
    let (a, b, c, d, f) = crate::vcard::address_parts(e);
    let t = [a, b, c, d, f].into_iter().filter(|x| !x.is_empty()).collect::<Vec<_>>().join(" ");
    fold(if t.is_empty() { e["full"].as_str().or(e["fullAddress"].as_str()).unwrap_or("") } else { &t })
}

fn signature(key: &str, e: &Value) -> String {
    let s = |k: &str| e[k].as_str().unwrap_or("").to_string();
    match key {
        "emails" => s("address").trim().to_lowercase(),
        "phones" => { let k = phone_key(&s("number")); if k.is_empty() { s("number").replace(char::is_whitespace, "") } else { k } }
        "addresses" => address_text(e),
        "onlineServices" => e["uri"].as_str().or(e["user"].as_str()).unwrap_or("").trim().to_lowercase(),
        "notes" => s("note").trim().to_string(),
        "nicknames" | "titles" => fold(&s("name")),
        "organizations" => fold(&format!("{} {}", s("name"), stable(e.get("units").unwrap_or(&json!([]))))),
        _ => stable(e),
    }
}

fn has_value(v: &Value) -> bool {
    match v {
        Value::Null => false,
        Value::String(s) => !s.trim().is_empty(),
        Value::Array(a) => !a.is_empty(),
        Value::Object(o) => {
            if o.contains_key("components") || o.contains_key("full") {
                o.get("components").and_then(|c| c.as_array()).is_some_and(|c| !c.is_empty()) || o.get("full").and_then(|f| f.as_str()).is_some_and(|f| !f.trim().is_empty())
            } else { !o.is_empty() }
        }
        _ => true,
    }
}

fn updated_at(c: &Value) -> i64 {
    c["updated"].as_str().and_then(|u| chrono::DateTime::parse_from_rfc3339(u).ok()).map(|t| t.timestamp_millis()).unwrap_or(i64::MIN)
}

fn free_key(m: &Map<String, Value>, k: &str) -> String {
    if !m.contains_key(k) { return k.to_string(); }
    (1..).map(|n| format!("{k}m{n}")).find(|x| !m.contains_key(x)).unwrap()
}

pub struct MergePlan {
    pub keep: Value,
    pub others: Vec<Value>,
    /// Only the properties that change, each in full.
    pub patch: Map<String, Value>,
    #[allow(dead_code)]
    pub result: Value,
}

/// How the cards become one; the first stays when none says when it changed.
pub fn plan(cards: &[Value]) -> Option<MergePlan> {
    let mut order: Vec<(usize, &Value)> = cards.iter().enumerate().collect();
    order.sort_by(|a, b| updated_at(b.1).cmp(&updated_at(a.1)).then(a.0.cmp(&b.0)));
    let keep = order.first()?.1.clone();
    let others: Vec<Value> = order[1..].iter().map(|x| x.1.clone()).collect();
    let mut result = keep.clone();
    for other in &others {
        let mut org_ids: std::collections::HashMap<String, String> = Default::default();
        for key in MULTI {
            let Some(from) = other[key].as_object().filter(|o| !o.is_empty()) else { continue };
            let mut into: Map<String, Value> = result[key].as_object().cloned().unwrap_or_default();
            let mut sigs: std::collections::HashMap<String, String> = into.iter().map(|(k, e)| (signature(key, e), k.clone())).collect();
            let has_pref = into.values().any(|e| e.get("pref").is_some_and(|p| !p.is_null()));
            let mut changed = false;
            for (ek, e) in from {
                if e.is_null() { continue; }
                let sig = signature(key, e);
                if let Some(ex) = sigs.get(&sig).filter(|_| !sig.is_empty()) {
                    if key == "organizations" { org_ids.insert(ek.clone(), ex.clone()); }
                    continue;
                }
                let mut copy = e.clone();
                if has_pref { if let Some(o) = copy.as_object_mut() { o.remove("pref"); } }
                if key == "titles" {
                    if let Some(m) = copy["organizationId"].as_str().and_then(|o| org_ids.get(o)).cloned() { copy["organizationId"] = json!(m); }
                }
                let nk = free_key(&into, ek);
                if key == "organizations" { org_ids.insert(ek.clone(), nk.clone()); }
                into.insert(nk.clone(), copy);
                sigs.insert(sig, nk);
                changed = true;
            }
            if changed { result[key] = Value::Object(into); }
        }
        if let Some(kw) = other["keywords"].as_object() {
            for (k, on) in kw {
                if *on == true && result["keywords"][k] != true {
                    if !result["keywords"].is_object() { result["keywords"] = json!({}); }
                    result["keywords"][k] = json!(true);
                }
            }
        }
        for key in SINGLE {
            if !has_value(&result[key]) && has_value(&other[key]) { result[key] = other[key].clone(); }
        }
        let has_photo = entries(&result, "media").iter().any(|m| m["kind"] == "photo");
        if !has_photo {
            if let Some((k, m)) = other["media"].as_object().and_then(|o| o.iter().find(|(_, m)| m["kind"] == "photo")) {
                if !result["media"].is_object() { result["media"] = json!({}); }
                let nk = free_key(result["media"].as_object().unwrap(), k);
                result["media"][nk] = m.clone();
            }
        }
    }
    let mut patch = Map::new();
    for key in MULTI.iter().chain(SINGLE.iter()).chain(["keywords", "media"].iter()) {
        let (a, b) = (result.get(*key).cloned().unwrap_or(Value::Null), keep.get(*key).cloned().unwrap_or(Value::Null));
        if stable(&a) != stable(&b) { patch.insert(key.to_string(), a); }
    }
    Some(MergePlan { keep, others, patch, result })
}

/// The keys a group's `members` may list a card under.
pub fn member_keys(c: &Value) -> Vec<String> {
    let mut k = vec![];
    if let Some(u) = c["uid"].as_str().filter(|u| !u.is_empty()) {
        k.push(u.to_string());
        k.push(match u.strip_prefix("urn:uuid:") { Some(b) => b.to_string(), None => format!("urn:uuid:{u}") });
    }
    if let Some(id) = c["id"].as_str() { k.push(id.to_string()); }
    k
}

/// The groups listing a merged-away card, with their new `members`: those out, the kept card in.
pub fn group_moves(groups: &[Value], keep: &Value, others: &[Value]) -> Vec<(String, Value)> {
    let other_keys: std::collections::HashSet<String> = others.iter().flat_map(member_keys).collect();
    let keep_keys = member_keys(keep);
    let mut out = vec![];
    for g in groups {
        let Some(m) = g["members"].as_object() else { continue };
        let listed: Vec<&String> = m.iter().filter(|(k, v)| **v == true && other_keys.contains(*k)).map(|(k, _)| k).collect();
        if listed.is_empty() { continue; }
        let mut members = m.clone();
        for k in listed { members.remove(k); }
        if !keep_keys.iter().any(|k| members.get(k) == Some(&json!(true))) {
            let k = keep["uid"].as_str().filter(|u| !u.is_empty()).or(keep["id"].as_str()).unwrap_or("").to_string();
            members.insert(k, json!(true));
        }
        out.push((g["id"].as_str().unwrap_or("").to_string(), Value::Object(members)));
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn finds_groups_by_name_email_phone() {
        let cards = vec![
            json!({"id": "1", "name": {"full": "Anna Müller"}}),
            json!({"id": "2", "name": {"components": [{"kind": "surname", "value": "Muller"}, {"kind": "given", "value": "anna"}]}}),
            json!({"id": "3", "name": {"full": "Bob"}, "emails": {"e": {"address": "bob@x.org"}}}),
            json!({"id": "4", "name": {"full": "Robert"}, "emails": {"e": {"address": "BOB@x.org"}}, "phones": {"p": {"number": "+49 30 5550000"}}}),
            json!({"id": "5", "name": {"full": "R."}, "phones": {"p": {"number": "030 5550000"}}}),
            json!({"id": "6", "name": {"full": "Alone"}}),
            json!({"id": "7", "kind": "group", "name": {"full": "Alone"}}),
        ];
        let g = find_groups(&cards);
        assert_eq!(g, vec![vec![0, 1], vec![2, 3, 4]]);
        assert_eq!(phone_key("12"), "");
    }

    #[test]
    fn merge_keeps_newest_and_adds_missing() {
        let a = json!({"id": "a", "uid": "ua", "updated": "2024-01-01T00:00:00Z", "name": {"full": "Ann"},
            "emails": {"e1": {"address": "ann@x.org", "pref": 1}}, "keywords": {"VIP": true}});
        let b = json!({"id": "b", "uid": "urn:uuid:ub", "updated": "2025-01-01T00:00:00Z", "name": {"full": "Ann B"},
            "emails": {"e1": {"address": "ANN@x.org"}}, "phones": {"p1": {"number": "555 1234"}},
            "media": {"m": {"kind": "photo", "uri": "data:image/png;base64,AA"}}});
        let p = plan(&[a.clone(), b.clone()]).unwrap();
        assert_eq!(p.keep["id"], "b");
        assert_eq!(p.others[0]["id"], "a");
        // email already there (case-insensitive): not added; keywords added
        assert!(!p.patch.contains_key("emails"));
        assert_eq!(p.patch["keywords"]["VIP"], true);
        assert!(!p.patch.contains_key("name"));
        assert!(!p.patch.contains_key("phones"));
        // the other way round the phone and photo move over, key clash renamed
        let c = json!({"id": "c", "updated": "2026-01-01T00:00:00Z", "name": {"full": "Ann C"}, "phones": {"p1": {"number": "999 0000"}}});
        let p2 = plan(&[b.clone(), c.clone()]).unwrap();
        assert_eq!(p2.keep["id"], "c");
        assert_eq!(p2.patch["phones"]["p1m1"]["number"], "555 1234");
        assert!(p2.patch["media"].is_object());
        assert_eq!(p2.patch["emails"]["e1"]["address"], "ANN@x.org");

        let groups = vec![json!({"id": "g1", "kind": "group", "members": {"ub": true, "other": true}}), json!({"id": "g2", "kind": "group", "members": {"x": true}})];
        let moves = group_moves(&groups, &c, &[b]);
        assert_eq!(moves.len(), 1);
        assert_eq!(moves[0].0, "g1");
        assert!(moves[0].1.get("ub").is_none());
        assert_eq!(moves[0].1["c"], true);
        assert_eq!(moves[0].1["other"], true);
    }
}
