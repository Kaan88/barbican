//! Email tags (Bulwark settings-store emailKeywords, lib/keyword-nesting.ts): the user's own
//! tags, stored with the settings as Bulwark's `emailKeywords` ({id, label, color}) and on the
//! messages as the JMAP keyword `$label:<id>` (RFC 8621 §4.1.1). With `nestedTags` a "/" in an
//! id makes a tree (`work/clients` sits under `work`), drawn as one in the sidebar.
use serde_json::{json, Value};
use std::collections::HashSet;

#[derive(Clone, Debug, PartialEq)]
pub struct TagDef { pub id: String, pub label: String, pub color: String }

/// Separates parent from child inside a tag id.
pub const SEPARATOR: char = '/';
/// The longest keyword Stalwart keeps (bytes), so the id gets what is left after `$label:`.
pub const MAX_KEYWORD: usize = 128;
const PREFIX: &str = "$label:";

/// The seven colour tags a new account starts with (Bulwark DEFAULT_KEYWORDS).
pub fn defaults() -> Vec<TagDef> {
    [("red", "Red"), ("orange", "Orange"), ("yellow", "Yellow"), ("green", "Green"), ("blue", "Blue"), ("purple", "Purple"), ("pink", "Pink")]
        .iter().map(|(id, l)| TagDef { id: (*id).into(), label: (*l).into(), color: (*id).into() }).collect()
}

/// Tag definitions from stored settings (the defaults until the user changes them).
pub fn parse(v: Option<&Value>) -> Vec<TagDef> {
    let Some(a) = v.and_then(|v| v.as_array()) else { return defaults() };
    a.iter().filter_map(|t| {
        let id = t["id"].as_str()?.trim().to_string();
        valid_id(&id).then(|| TagDef {
            label: t["label"].as_str().filter(|l| !l.trim().is_empty()).unwrap_or(&id).to_string(),
            color: t["color"].as_str().unwrap_or("gray").to_string(),
            id,
        })
    }).collect()
}

pub fn defs() -> Vec<TagDef> { parse(crate::settings::Settings::load().prefs.get("emailKeywords")) }

pub fn to_value(defs: &[TagDef]) -> Value {
    Value::Array(defs.iter().map(|d| json!({"id": d.id, "label": d.label, "color": d.color})).collect())
}

pub fn save(defs: &[TagDef]) { crate::settings::Settings::put("emailKeywords", Some(to_value(defs))); }

pub fn nested() -> bool { crate::settings::Settings::load().bool("nestedTags", false) }

/// RFC 8621 keyword syntax for the whole `$label:<id>`, within Stalwart's length.
pub fn valid_id(id: &str) -> bool {
    let kw = format!("{PREFIX}{id}");
    !id.is_empty() && kw.len() <= MAX_KEYWORD && kw.bytes().all(|b| (0x21..=0x7e).contains(&b) && !b"(){]%*\"\\".contains(&b))
}

/// One level of an id from a name: lowercase, anything outside [a-z0-9_-] folded to one dash
/// (a "/" typed in a name is part of the name, not a level) (Bulwark normalizeKeywordLevel).
pub fn normalize_level(name: &str) -> String {
    let mut out = String::new();
    for c in name.trim().to_lowercase().chars() {
        let c = if c.is_ascii_lowercase() || c.is_ascii_digit() || c == '_' || c == '-' { c } else { '-' };
        if c == '-' && out.ends_with('-') { continue; }
        out.push(c);
    }
    out.trim_matches('-').to_string()
}

/// `tag-` and a 32-bit FNV-1a hash of the name in base 36 (Bulwark generatedKeywordLevel), for a
/// name with no usable slug (Работа) or one already taken.
pub fn generated_level(name: &str) -> String {
    let mut h: u32 = 0x811c9dc5;
    // JavaScript hashes UTF-16 code units
    for u in name.trim().to_lowercase().encode_utf16() { h = (h ^ u as u32).wrapping_mul(0x01000193); }
    let mut n = h as u64;
    let digits = b"0123456789abcdefghijklmnopqrstuvwxyz";
    let mut s = vec![];
    loop { s.push(digits[(n % 36) as usize]); n /= 36; if n == 0 { break; } }
    s.reverse();
    format!("tag-{}", String::from_utf8(s).unwrap_or_default())
}

pub fn parent_of(id: &str) -> Option<&str> { id.rfind(SEPARATOR).map(|i| &id[..i]) }

pub fn is_descendant(id: &str, ancestor: &str) -> bool { id.starts_with(&format!("{ancestor}{SEPARATOR}")) }

/// The id a new tag called `name` gets under `parent` (Bulwark chooseKeywordId for a new tag):
/// its slug when free, else a generated level, numbered when even that is taken.
pub fn choose_id(parent: Option<&str>, name: &str, taken: &[String]) -> String {
    if name.trim().is_empty() { return String::new(); }
    let taken: HashSet<String> = taken.iter().map(|t| t.to_lowercase()).collect();
    let join = |part: &str| match parent { Some(p) if !p.is_empty() => format!("{p}{SEPARATOR}{part}"), _ => part.to_string() };
    let slug = normalize_level(name);
    if !slug.is_empty() && !taken.contains(&join(&slug)) { return join(&slug); }
    let level = generated_level(name);
    let mut id = join(&level);
    let mut n = 2;
    while taken.contains(&id) { id = join(&format!("{level}-{n}")); n += 1; }
    id
}

/// Whether a tag directly under `parent` already has this name (Bulwark isKeywordNameTaken).
pub fn name_taken(defs: &[TagDef], parent: Option<&str>, name: &str, except: Option<&str>) -> bool {
    let want = name.trim().to_lowercase();
    !want.is_empty() && defs.iter().any(|d| Some(d.id.as_str()) != except && parent_of(&d.id) == parent && d.label.trim().to_lowercase() == want)
}

/// A row of the sidebar's tag list: the tag, its depth and its chevron (0 none, 1 open, 2 closed).
#[derive(Clone, Debug, PartialEq)]
pub struct Row { pub def: TagDef, pub depth: i32, pub chevron: i32 }

/// The tags in sidebar order. Without nesting each is its own root; with it the tree the ids
/// describe, keeping the user's order within a level. A tag whose parent is not defined stays
/// a root and is named in full ("work/clients"), so the missing level shows (Bulwark
/// buildKeywordTree). Children of a collapsed tag are left out.
pub fn rows(defs: &[TagDef], nested: bool, collapsed: &HashSet<String>) -> Vec<Row> {
    if !nested { return defs.iter().map(|d| Row { def: d.clone(), depth: 0, chevron: 0 }).collect(); }
    let ids: HashSet<&str> = defs.iter().map(|d| d.id.as_str()).collect();
    let is_root = |d: &TagDef| parent_of(&d.id).is_none_or(|p| !ids.contains(p));
    fn walk(d: &TagDef, depth: i32, defs: &[TagDef], collapsed: &HashSet<String>, out: &mut Vec<Row>, orphan: bool) {
        let kids: Vec<&TagDef> = defs.iter().filter(|k| parent_of(&k.id) == Some(d.id.as_str())).collect();
        let closed = collapsed.contains(&d.id);
        let mut def = d.clone();
        if orphan { def.label = format!("{}{SEPARATOR}{}", parent_of(&d.id).unwrap_or(""), d.label); }
        out.push(Row { def, depth, chevron: if kids.is_empty() { 0 } else if closed { 2 } else { 1 } });
        if !closed { for k in kids { walk(k, depth + 1, defs, collapsed, out, false); } }
    }
    let mut out = vec![];
    for d in defs.iter().filter(|d| is_root(d)) { walk(d, 0, defs, collapsed, &mut out, parent_of(&d.id).is_some()); }
    out
}

/// Tag palette (Bulwark KEYWORD_PALETTE, Tailwind shades: -light 300, base 500, -dark 700) or a
/// custom "#rrggbb".
pub fn color_rgb(name: &str) -> u32 {
    if let Some(hex) = name.strip_prefix('#') { if hex.len() == 6 { if let Ok(v) = u32::from_str_radix(hex, 16) { return v; } } }
    let (hue, shade) = match name.rsplit_once('-') { Some((h, "light")) => (h, 0), Some((h, "dark")) => (h, 2), _ => (name, 1) };
    let s: [u32; 3] = match hue {
        "red" => [0xfca5a5, 0xef4444, 0xb91c1c], "orange" => [0xfdba74, 0xf97316, 0xc2410c], "amber" => [0xfcd34d, 0xf59e0b, 0xb45309],
        "yellow" => [0xfde047, 0xeab308, 0xa16207], "lime" => [0xbef264, 0x84cc16, 0x4d7c0f], "green" => [0x86efac, 0x22c55e, 0x15803d],
        "teal" => [0x5eead4, 0x14b8a6, 0x0f766e], "cyan" => [0x67e8f9, 0x06b6d4, 0x0e7490], "blue" => [0x93c5fd, 0x3b82f6, 0x1d4ed8],
        "indigo" => [0xa5b4fc, 0x6366f1, 0x4338ca], "purple" => [0xd8b4fe, 0xa855f7, 0x7e22ce], "pink" => [0xf9a8d4, 0xec4899, 0xbe185d],
        _ => [0xd1d5db, 0x6b7280, 0x374151],
    };
    s[shade]
}

/// The base palette offered in the colour menu.
pub const PALETTE: [&str; 13] = ["red", "orange", "amber", "yellow", "lime", "green", "teal", "cyan", "blue", "indigo", "purple", "pink", "gray"];

/// Add a tag (under `parent` when nesting); returns the new id or why not.
pub fn add(defs: &mut Vec<TagDef>, parent: Option<&str>, name: &str) -> Result<String, String> {
    let name = name.trim();
    if name.is_empty() { return Err("Give the tag a name.".into()); }
    if name_taken(defs, parent, name, None) { return Err("A tag with that name is already there.".into()); }
    let taken: Vec<String> = defs.iter().map(|d| d.id.clone()).collect();
    let id = choose_id(parent, name, &taken);
    if !valid_id(&id) { return Err("That name nests too deep for a tag.".into()); }
    // a child goes after its parent's last descendant, a root at the end
    let color = parent.and_then(|p| defs.iter().find(|d| d.id == p)).map(|d| d.color.clone()).unwrap_or_else(|| "blue".into());
    let at = parent.and_then(|p| defs.iter().rposition(|d| d.id == p || is_descendant(&d.id, p))).map(|i| i + 1).unwrap_or(defs.len());
    defs.insert(at, TagDef { id: id.clone(), label: name.to_string(), color });
    Ok(id)
}

/// Rename a tag's label (its id, and so the messages' keyword, stays).
pub fn rename(defs: &mut [TagDef], id: &str, name: &str) -> Result<(), String> {
    let name = name.trim();
    if name.is_empty() { return Err("Give the tag a name.".into()); }
    if name_taken(defs, parent_of(id), name, Some(id)) { return Err("A tag with that name is already there.".into()); }
    let d = defs.iter_mut().find(|d| d.id == id).ok_or("That tag is gone.")?;
    d.label = name.to_string();
    Ok(())
}

/// Remove a tag definition (Bulwark removeKeyword: messages keep their keyword). A tag with
/// tags under it stays until those are gone.
pub fn remove(defs: &mut Vec<TagDef>, id: &str) -> Result<(), String> {
    if defs.iter().any(|d| is_descendant(&d.id, id)) { return Err("Delete the tags inside it first.".into()); }
    let before = defs.len();
    defs.retain(|d| d.id != id);
    if defs.len() == before { Err("That tag is gone.".into()) } else { Ok(()) }
}

pub fn set_color(defs: &mut [TagDef], id: &str, color: &str) {
    if let Some(d) = defs.iter_mut().find(|d| d.id == id) { d.color = color.to_string(); }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn d(id: &str) -> TagDef { TagDef { id: id.into(), label: id.rsplit('/').next().unwrap().into(), color: "blue".into() } }

    #[test]
    fn ids_follow_bulwark() {
        assert_eq!(normalize_level("  Work Stuff!! "), "work-stuff");
        assert_eq!(normalize_level("a/b"), "a-b");
        assert_eq!(normalize_level("Работа"), "");
        assert_eq!(choose_id(None, "Work", &[]), "work");
        assert_eq!(choose_id(Some("work"), "Clients", &[]), "work/clients");
        // taken slug or none: the generated level, then numbered
        let g = choose_id(None, "Работа", &[]);
        assert!(g.starts_with("tag-") && g.len() > 4, "{g}");
        assert_eq!(choose_id(None, "Work", &["WORK".into()]), generated_level("Work"));
        assert_eq!(choose_id(None, "Work", &["work".into(), generated_level("Work")]), format!("{}-2", generated_level("Work")));
        assert!(valid_id("work/clients") && !valid_id("a b") && !valid_id("a*b") && !valid_id(&"x".repeat(130)));
    }

    #[test]
    fn fnv_matches_javascript() {
        // FNV-1a 32 of "a" is 0xe40c292c = 3826002220, base 36 "1r9wi7g"
        assert_eq!(generated_level("A"), "tag-1r9wi7g");
    }

    #[test]
    fn tree_rows() {
        let defs = vec![d("work"), d("home"), d("work/clients"), d("work/clients/acme"), d("lost/child")];
        let flat = rows(&defs, false, &HashSet::new());
        assert_eq!(flat.iter().map(|r| r.depth).collect::<Vec<_>>(), vec![0, 0, 0, 0, 0]);
        let t = rows(&defs, true, &HashSet::new());
        let order: Vec<(&str, i32, i32)> = t.iter().map(|r| (r.def.id.as_str(), r.depth, r.chevron)).collect();
        assert_eq!(order, vec![("work", 0, 1), ("work/clients", 1, 1), ("work/clients/acme", 2, 0), ("home", 0, 0), ("lost/child", 0, 0)]);
        // an orphan is named in full
        assert_eq!(t[4].def.label, "lost/child");
        let closed: HashSet<String> = ["work".to_string()].into();
        let c = rows(&defs, true, &closed);
        assert_eq!(c.iter().map(|r| r.def.id.as_str()).collect::<Vec<_>>(), vec!["work", "home", "lost/child"]);
        assert_eq!(c[0].chevron, 2);
    }

    #[test]
    fn manage_tags() {
        let mut defs = vec![d("work"), d("home")];
        assert_eq!(add(&mut defs, Some("work"), "Clients").unwrap(), "work/clients");
        assert_eq!(defs.iter().map(|x| x.id.as_str()).collect::<Vec<_>>(), vec!["work", "work/clients", "home"]);
        assert!(add(&mut defs, Some("work"), "clients").is_err());
        assert!(add(&mut defs, None, " ").is_err());
        assert!(remove(&mut defs, "work").is_err());
        rename(&mut defs, "work/clients", "Customers").unwrap();
        assert_eq!(defs[1].label, "Customers");
        assert_eq!(defs[1].id, "work/clients");
        remove(&mut defs, "work/clients").unwrap();
        remove(&mut defs, "work").unwrap();
        assert_eq!(defs.len(), 1);
        set_color(&mut defs, "home", "teal");
        assert_eq!(defs[0].color, "teal");
    }

    #[test]
    fn stored_tags_and_colours() {
        assert_eq!(parse(None), defaults());
        let v = json!([{"id": "work", "label": "Work", "color": "green-dark"}, {"id": "bad id"}, {"id": "x"}]);
        let p = parse(Some(&v));
        assert_eq!(p.len(), 2);
        assert_eq!(p[1].label, "x");
        assert_eq!(parse(Some(&to_value(&p))), p);
        assert_eq!(color_rgb("red"), 0xef4444);
        assert_eq!(color_rgb("green-dark"), 0x15803d);
        assert_eq!(color_rgb("#123abc"), 0x123abc);
        assert_eq!(color_rgb("nope"), 0x6b7280);
    }
}
