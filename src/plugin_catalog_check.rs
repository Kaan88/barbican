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
    assert_eq!((HOOKS.len(), SLOTS.len(), HOST_API.len()), (218, 58, 186));
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
    }
}

#[test]
fn slots_match_csv() {
    let t = table(SLOTS_CSV);
    assert_eq!(t.len(), SLOTS.len(), "run python tools/gen-plugin-catalog.py");
    for (r, s) in t.iter().zip(SLOTS.iter()) {
        let kind = match s.kind { SlotKind::Frame => "frame", SlotKind::Action => "action", SlotKind::Data => "data" };
        assert_eq!((r["name"].as_str(), r["area"].as_str(), r["kind"].as_str(), r["permissions"].as_str()), (s.name, s.area, kind, s.permissions), "stale: run python tools/gen-plugin-catalog.py");
    }
}

#[test]
fn host_api_matches_csv() {
    let t = table(API_CSV);
    let names: Vec<&str> = t.iter().map(|r| r["method"].as_str()).collect();
    assert_eq!(names, HOST_API.to_vec(), "run python tools/gen-plugin-catalog.py");
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
