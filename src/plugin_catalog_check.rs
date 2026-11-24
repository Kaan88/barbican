//! Tests that src/plugin_catalog.rs (generated) matches the committed CSVs in spec/plugins/.
use crate::plugin_catalog::*;

/// Minimal RFC 4180 reader: quoted fields, doubled quotes, newlines inside quotes.
fn parse(text: &str) -> Vec<Vec<String>> {
    let (mut rows, mut row, mut field, mut quoted) = (vec![], vec![], String::new(), false);
    let mut it = text.chars().peekable();
    while let Some(c) = it.next() {
        match (quoted, c) {
            (true, '"') => if it.peek() == Some(&'"') { field.push('"'); it.next(); } else { quoted = false },
            (true, _) => field.push(c),
            (false, '"') => quoted = true,
            (false, ',') => row.push(std::mem::take(&mut field)),
            (false, '\r') => {}
            (false, '\n') => { row.push(std::mem::take(&mut field)); rows.push(std::mem::take(&mut row)); }
            (false, _) => field.push(c),
        }
    }
    if !field.is_empty() || !row.is_empty() { row.push(field); rows.push(row); }
    rows
}

/// Data rows as header-keyed lookups.
fn table(text: &str) -> Vec<std::collections::HashMap<String, String>> {
    let mut rows = parse(text).into_iter();
    let head = rows.next().unwrap();
    rows.map(|r| { assert_eq!(r.len(), head.len(), "ragged row {r:?}"); head.iter().cloned().zip(r).collect() }).collect()
}

const HOOKS_CSV: &str = include_str!("../spec/plugins/hooks.csv");
const SLOTS_CSV: &str = include_str!("../spec/plugins/slots.csv");
const API_CSV: &str = include_str!("../spec/plugins/host-api.csv");

#[test]
fn counts() {
    assert_eq!((HOOKS.len(), SLOTS.len(), HOST_API.len()), (218, 58, 192));
}

fn support(s: Support) -> &'static str {
    match s { Support::Adopt => "adopt", Support::Adapt => "adapt", Support::Skip => "skip" }
}

/// adopt / adapt / skip per table, pinned like the row counts (spec/plugins/README.md has the same numbers).
#[test]
fn support_counts() {
    fn count(it: impl Iterator<Item = Support>) -> (usize, usize, usize) {
        it.fold((0, 0, 0), |(a, d, s), x| match x { Support::Adopt => (a + 1, d, s), Support::Adapt => (a, d + 1, s), Support::Skip => (a, d, s + 1) })
    }
    assert_eq!(count(HOOKS.iter().map(|h| h.support)), (194, 20, 4), "hooks adopt/adapt/skip");
    assert_eq!(count(SLOTS.iter().map(|s| s.support)), (19, 37, 2), "slots adopt/adapt/skip");
    assert_eq!(count(HOST_API.iter().map(|m| m.support)), (150, 28, 14), "host API adopt/adapt/skip");
}

#[test]
fn hooks_match_csv() {
    let t = table(HOOKS_CSV);
    assert_eq!(t.len(), HOOKS.len(), "run python tools/gen-plugin-catalog.py");
    for (r, h) in t.iter().zip(HOOKS.iter()) {
        let kind = match h.kind { HookKind::Observer => "observer", HookKind::Intercept => "intercept", HookKind::Transform => "transform", HookKind::Takeover => "takeover" };
        assert_eq!((r["name"].as_str(), r["group"].as_str(), r["kind"].as_str(), r["permission"].as_str()), (h.name, h.group, kind, h.permission), "stale: run python tools/gen-plugin-catalog.py");
        assert_eq!(r["budget_ms"].parse::<u32>().unwrap(), h.budget_ms, "{}", h.name);
        assert_eq!(r["fail_closed"] == "true", h.fail_closed, "{}", h.name);
        assert_eq!(r["barbican_status"], support(h.support), "{}", h.name);
        assert!(!r["barbican_note"].is_empty(), "{}: barbican_note", h.name);
    }
}

#[test]
fn slots_match_csv() {
    let t = table(SLOTS_CSV);
    assert_eq!(t.len(), SLOTS.len(), "run python tools/gen-plugin-catalog.py");
    for (r, s) in t.iter().zip(SLOTS.iter()) {
        let kind = match s.kind { SlotKind::Frame => "frame", SlotKind::Action => "action", SlotKind::Data => "data" };
        assert_eq!((r["name"].as_str(), r["area"].as_str(), r["kind"].as_str(), r["permissions"].as_str()), (s.name, s.area, kind, s.permissions), "stale: run python tools/gen-plugin-catalog.py");
        assert_eq!(r["barbican_status"], support(s.support), "{}", s.name);
        assert!(!r["barbican_note"].is_empty(), "{}: barbican_note", s.name);
    }
}

#[test]
fn host_api_matches_csv() {
    let t = table(API_CSV);
    assert_eq!(t.len(), HOST_API.len(), "run python tools/gen-plugin-catalog.py");
    for (r, m) in t.iter().zip(HOST_API.iter()) {
        assert_eq!((r["method"].as_str(), r["base_permission"].as_str()), (m.name, m.permission), "stale: run python tools/gen-plugin-catalog.py");
        assert_eq!(r["barbican_status"], support(m.support), "{}", m.name);
        assert!(!r["barbican_note"].is_empty(), "{}: barbican_note", m.name);
    }
    assert_eq!(lookup_host_method("http.fetch").map(|m| m.support), Some(Support::Adopt));
    assert_eq!(lookup_host_method("http.post").map(|m| m.support), Some(Support::Skip));
}

#[test]
fn names_are_unique() {
    let mut seen = std::collections::HashSet::new();
    assert!(HOOKS.iter().all(|h| seen.insert(h.name)));
}

#[test]
fn aliases_resolve() {
    assert_eq!(resolve_alias("compose.check"), "onBeforeEmailSend");
    assert_eq!(resolve_alias("email.details"), "onEmailOpen");
    assert_eq!(resolve_alias("onEmailOpen"), "onEmailOpen");
    let send = lookup_hook("compose.check").unwrap();
    assert!(send.fail_closed && send.kind == HookKind::Intercept);
    assert_eq!(lookup_hook("email.details").unwrap().budget_ms, 5000);
    assert!(lookup_hook("nope").is_none());
    for (old, new) in HOOK_ALIASES { assert!(lookup_hook(new).is_some() && lookup_hook(old).is_some()); }
}

#[test]
fn also_permission_table_matches_the_csv() {
    let mut csv: Vec<(String, String)> = table(HOOKS_CSV).into_iter()
        .filter(|r| !r["also_permission"].is_empty()).map(|r| (r["name"].clone(), r["also_permission"].clone())).collect();
    let mut ours: Vec<(String, String)> = crate::plugins::ALSO_PERMISSION.iter().map(|(a, b)| (a.to_string(), b.to_string())).collect();
    csv.sort();
    ours.sort();
    assert_eq!(ours, csv, "plugins::ALSO_PERMISSION must list hooks.csv also_permission");
}

#[test]
fn every_catalogued_permission_is_offered_or_dropped() {
    use crate::plugins::{DROPPED_PERMISSIONS, PERMISSIONS};
    let offered = |p: &str| PERMISSIONS.iter().any(|x| x.name == p) || DROPPED_PERMISSIONS.contains(&p);
    for h in HOOKS.iter() { assert!(offered(h.permission), "hook {} needs {}", h.name, h.permission); }
    for s in SLOTS.iter() { for p in s.permissions.split(';') { assert!(offered(p), "slot {} needs {p}", s.name); } }
}
