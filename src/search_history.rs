//! Recent searches and the suggestions under the mail search box.
//!
//! Bulwark: stores/search-history-store.ts (ten raw queries, newest first, no duplicates,
//! kept per device) and lib/search-suggestions.ts (recent searches when the box is empty;
//! while typing, recent searches that contain the text and then people: contacts and the
//! senders of the loaded mail).

use std::path::PathBuf;

pub const MAX_RECENT: usize = 10;
pub const MAX_RECENT_IDLE: usize = 5;
pub const MAX_RECENT_TYPING: usize = 3;
pub const MAX_CONTACTS: usize = 5;

fn path() -> PathBuf { crate::config_dir().join("search-history.json") }

pub fn load() -> Vec<String> {
    std::fs::read_to_string(path()).ok()
        .and_then(|s| serde_json::from_str::<Vec<String>>(&s).ok())
        .unwrap_or_default()
}

fn save(v: &[String]) {
    let p = path();
    let _ = std::fs::create_dir_all(p.parent().unwrap());
    let _ = std::fs::write(p, serde_json::to_string(v).unwrap_or_default());
}

/// The list with `q` put first (case-insensitive duplicates dropped), capped.
pub fn with_added(list: &[String], q: &str) -> Vec<String> {
    let t = q.trim();
    if t.is_empty() { return list.to_vec(); }
    let mut v = vec![t.to_string()];
    v.extend(list.iter().filter(|x| !x.eq_ignore_ascii_case(t)).cloned());
    v.truncate(MAX_RECENT);
    v
}

pub fn add(q: &str) {
    let cur = load();
    let next = with_added(&cur, q);
    if next != cur { save(&next); }
}

pub fn remove(q: &str) {
    let mut v = load();
    v.retain(|x| x != q);
    save(&v);
}

#[derive(Clone, Debug, PartialEq)]
pub enum Suggestion {
    Recent(String),
    Person { name: String, email: String },
}

/// Bulwark buildSearchSuggestions.
pub fn build(query: &str, recent: &[String], people: &[(String, String)]) -> Vec<Suggestion> {
    let q = query.trim().to_lowercase();
    if q.is_empty() {
        return recent.iter().take(MAX_RECENT_IDLE).map(|r| Suggestion::Recent(r.clone())).collect();
    }
    let mut out: Vec<Suggestion> = recent.iter()
        .filter(|r| { let l = r.to_lowercase(); l != q && l.contains(&q) })
        .take(MAX_RECENT_TYPING)
        .map(|r| Suggestion::Recent(r.clone())).collect();
    let mut seen = std::collections::HashSet::new();
    for (name, email) in people {
        if seen.len() >= MAX_CONTACTS { break; }
        let e = email.trim();
        if e.is_empty() || !seen.insert(e.to_lowercase()) { continue; }
        out.push(Suggestion::Person { name: name.trim().to_string(), email: e.to_string() });
    }
    out
}

/// People whose name or address contains the query (contacts first, then senders).
pub fn people_matching(query: &str, candidates: &[(String, String)]) -> Vec<(String, String)> {
    let q = query.trim().to_lowercase();
    if q.is_empty() { return vec![]; }
    candidates.iter().filter(|(n, e)| !e.trim().is_empty() && (n.to_lowercase().contains(&q) || e.to_lowercase().contains(&q))).cloned().collect()
}

/// The search for a picked person: their mail (`from:` the address).
pub fn person_query(email: &str) -> String {
    if email.contains(char::is_whitespace) { format!("from:\"{email}\"") } else { format!("from:{email}") }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn adds_newest_first_without_duplicates() {
        let v = with_added(&[], "  budget ");
        assert_eq!(v, vec!["budget"]);
        let v = with_added(&v, "report");
        let v = with_added(&v, "BUDGET");
        assert_eq!(v, vec!["BUDGET", "report"]);
        let mut v = v;
        for i in 0..20 { v = with_added(&v, &format!("q{i}")); }
        assert_eq!(v.len(), MAX_RECENT);
        assert_eq!(v[0], "q19");
        assert_eq!(with_added(&v, "   "), v);
    }

    #[test]
    fn suggestions_like_bulwark() {
        let recent: Vec<String> = ["invoice march", "invoice", "travel", "x1", "x2", "x3"].iter().map(|s| s.to_string()).collect();
        // empty box: the five newest
        let s = build("", &recent, &[]);
        assert_eq!(s.len(), 5);
        assert_eq!(s[0], Suggestion::Recent("invoice march".into()));
        // typing: matching recent searches, not the exact one, then people (deduplicated)
        let people = vec![("Ann".to_string(), "ann@x.org".to_string()), ("Ann L".to_string(), "ANN@x.org".to_string()), ("".into(), "".into()), ("Inv Co".into(), "billing@inv.co".into())];
        let s = build("invoice", &recent, &people);
        assert_eq!(s[0], Suggestion::Recent("invoice march".into()));
        assert_eq!(s.len(), 3);
        assert_eq!(s[1], Suggestion::Person { name: "Ann".into(), email: "ann@x.org".into() });
    }

    #[test]
    fn people_and_query() {
        let c = vec![("Ann Lee".to_string(), "ann@x.org".to_string()), ("Bob".into(), "bob@y.org".into()), ("No mail".into(), "".into())];
        assert_eq!(people_matching("lee", &c).len(), 1);
        assert_eq!(people_matching("Y.ORG", &c)[0].0, "Bob");
        assert!(people_matching("no", &c).is_empty());
        assert_eq!(person_query("ann@x.org"), "from:ann@x.org");
    }
}
