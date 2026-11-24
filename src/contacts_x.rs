//! Contacts depth on top of contacts.rs (Bulwark contact-detail / contact-form /
//! contact-duplicates-view / contact-merge-dialog / contact-import-dialog / contact-export /
//! directory-browser): typed and labelled entries with their unknown properties kept on save,
//! photos (data: URIs and Stalwart 1.0 blob media), anniversaries, related people, vCard import
//! and export, duplicates and merge, and the server directory (RFC 9670 principals).
use crate::jmap::Client;
use crate::{AppWindow, CtDupe, CtField, CtLine, CtX};
use serde_json::{json, Map, Value};
use slint::{ComponentHandle, Image, ModelRc, Rgba8Pixel, SharedPixelBuffer, SharedString, VecModel};
use std::cell::RefCell;
use std::collections::HashMap;
use std::sync::{Arc, Mutex};

pub type ClientSlot = Arc<Mutex<Option<Arc<Client>>>>;
const CAP: &str = "urn:ietf:params:jmap:contacts";

#[derive(Clone, Default, Debug, PartialEq)]
pub struct Field {
    pub section: String,
    /// The entry's key in the card's map (None: new). For `related` the map key is the name.
    pub key: Option<String>,
    pub kind: i32,
    pub kind2: i32,
    pub a: String,
    pub b: String,
    pub c: String,
    pub d: String,
    pub e: String,
    /// The entry's own label (X-ABLabel): shown as the last type option and kept while picked.
    pub label: Option<String>,
}

#[derive(Clone, Default)]
enum PhotoEdit { #[default] Unchanged, New(String, Vec<u8>), Removed }

#[derive(Default)]
struct Ed {
    orig: Option<Value>,
    fields: Vec<Field>,
    photo: PhotoEdit,
}

#[derive(Default)]
struct St {
    ed: Ed,
    photos: HashMap<String, Option<SharedPixelBuffer<Rgba8Pixel>>>,
    import: Vec<Value>,
    import_dupes: Vec<usize>,
    directory: Vec<Value>,
    dir_loaded: bool,
    has_principals: bool,
    groups: Vec<Vec<String>>,
}

thread_local! { static S: RefCell<St> = RefCell::new(St::default()); }

// ---------------- type options ----------------

pub const CONTEXTS: [&str; 3] = ["Personal", "Work", "Other"];
pub const PHONE_FEATURES: [(&str, &str); 7] = [("voice", "Phone"), ("mobile", "Mobile"), ("fax", "Fax"), ("pager", "Pager"), ("text", "Text"), ("video", "Video"), ("main-number", "Main")];
pub const PHONE_CTX: [&str; 3] = ["Any", "Personal", "Work"];
pub const DATE_KINDS: [(&str, &str); 4] = [("birth", "Birthday"), ("wedding", "Anniversary"), ("death", "Date of death"), ("other", "Other date")];
pub const RELATIONS: [(&str, &str); 13] = [("contact", "Contact"), ("spouse", "Spouse"), ("sweetheart", "Partner"), ("child", "Child"), ("parent", "Parent"),
    ("sibling", "Sibling"), ("friend", "Friend"), ("colleague", "Colleague"), ("co-worker", "Co-worker"), ("kin", "Relative"), ("emergency", "Emergency contact"),
    ("neighbor", "Neighbour"), ("acquaintance", "Acquaintance")];

fn ctx_index(c: &Value) -> i32 { if c["private"] == true { 0 } else if c["work"] == true { 1 } else { 2 } }
fn ctx_value(i: i32) -> Value { match i { 0 => json!({"private": true}), 1 => json!({"work": true}), _ => Value::Null } }

fn kinds_for(f: &Field) -> (Vec<String>, Vec<String>) {
    let with_label = |mut v: Vec<String>| { if let Some(l) = &f.label { v.push(l.clone()); } v };
    match f.section.as_str() {
        "email" | "address" | "url" | "im" => (with_label(CONTEXTS.iter().map(|s| s.to_string()).collect()), vec![]),
        "phone" => (with_label(PHONE_FEATURES.iter().map(|s| s.1.to_string()).collect()), PHONE_CTX.iter().map(|s| s.to_string()).collect()),
        "date" => (DATE_KINDS.iter().map(|s| s.1.to_string()).collect(), vec![]),
        "related" => (RELATIONS.iter().map(|s| s.1.to_string()).collect(), vec![]),
        _ => (vec![], vec![]),
    }
}

/// The label option sits after the fixed ones; picking it keeps the entry's label.
fn label_picked(f: &Field) -> bool {
    let n = match f.section.as_str() { "phone" => PHONE_FEATURES.len(), "email" | "address" | "url" | "im" => CONTEXTS.len(), _ => usize::MAX };
    f.label.is_some() && f.kind as usize == n
}

// ---------------- card -> fields ----------------

fn entries(c: &Value, k: &str) -> Vec<(String, Value)> {
    c[k].as_object().map(|o| o.iter().map(|(k, v)| (k.clone(), v.clone())).collect()).unwrap_or_default()
}

/// A date as the form shows it: YYYY-MM-DD, --MM-DD (no year) or YYYY.
pub fn date_text(d: &Value) -> String {
    if let Some(s) = d.as_str() { return s.to_string(); }
    if let Some(u) = d["utc"].as_str() { return u.split('T').next().unwrap_or("").to_string(); }
    match (d["year"].as_i64(), d["month"].as_i64(), d["day"].as_i64()) {
        (Some(y), Some(m), Some(dd)) => format!("{y:04}-{m:02}-{dd:02}"),
        (None, Some(m), Some(dd)) => format!("--{m:02}-{dd:02}"),
        (Some(y), Some(m), None) => format!("{y:04}-{m:02}"),
        (Some(y), None, None) => format!("{y}"),
        _ => String::new(),
    }
}

/// The form's date text as a JSContact PartialDate; Err for text it cannot read.
pub fn parse_date_text(t: &str) -> Result<Value, String> {
    let t = t.trim();
    let mut p = Map::new();
    p.insert("VALUE".into(), json!("date"));
    let bad = || format!("\"{t}\" is not a date (use YYYY-MM-DD or --MM-DD)");
    let d = crate::vcard::parse_date(t, &p).ok_or_else(bad)?;
    // a day the month has: 31 April and 29 February of a common year are refused; 29 February
    // with no year is fine (it is a birthday every year, D-M5-A07)
    if let (Some(m), Some(day)) = (d["month"].as_u64(), d["day"].as_u64()) {
        let max = days_in_month(d["year"].as_i64().map(|y| y as i32), m as u32);
        if day as u32 > max { return Err(bad()); }
    }
    Ok(d)
}

/// Days in a month; a month with no year counts February as 29 (a leap year is possible).
fn days_in_month(year: Option<i32>, month: u32) -> u32 {
    match month {
        2 => if year.is_none_or(|y| (y % 4 == 0 && y % 100 != 0) || y % 400 == 0) { 29 } else { 28 },
        4 | 6 | 9 | 11 => 30,
        _ => 31,
    }
}

fn phone_kind(p: &Value) -> i32 {
    if p["label"].as_str().is_some_and(|l| !l.trim().is_empty()) { return PHONE_FEATURES.len() as i32; }
    let f = &p["features"];
    if f["mobile"] == true || f["cell"] == true { return 1; }
    PHONE_FEATURES.iter().position(|(k, _)| f[*k] == true).unwrap_or(0) as i32
}

pub fn fields_of(c: &Value) -> Vec<Field> {
    let mut v = vec![];
    let label = |e: &Value| e["label"].as_str().filter(|l| !l.trim().is_empty()).map(String::from);
    for (k, e) in entries(c, "emails") {
        let l = label(&e);
        v.push(Field { section: "email".into(), key: Some(k), kind: if l.is_some() { CONTEXTS.len() as i32 } else { ctx_index(&e["contexts"]) }, a: e["address"].as_str().unwrap_or("").into(), label: l, ..Default::default() });
    }
    for (k, p) in entries(c, "phones") {
        let ctx = &p["contexts"];
        v.push(Field { section: "phone".into(), key: Some(k), kind: phone_kind(&p), kind2: if ctx["private"] == true { 1 } else if ctx["work"] == true { 2 } else { 0 },
            a: p["number"].as_str().unwrap_or("").into(), label: label(&p), ..Default::default() });
    }
    for (k, a) in entries(c, "addresses") {
        let (st, loc, reg, pc, co) = crate::vcard::address_parts(&a);
        let st = if [&st, &loc, &reg, &pc, &co].iter().all(|x| x.is_empty()) { a["full"].as_str().or(a["fullAddress"].as_str()).unwrap_or("").to_string() } else { st };
        let l = label(&a);
        v.push(Field { section: "address".into(), key: Some(k), kind: if l.is_some() { CONTEXTS.len() as i32 } else { ctx_index(&a["contexts"]) }, a: st, b: loc, c: reg, d: pc, e: co, label: l, ..Default::default() });
    }
    for (k, u) in entries(c, "links") {
        let l = label(&u);
        v.push(Field { section: "url".into(), key: Some(k), kind: if l.is_some() { CONTEXTS.len() as i32 } else { ctx_index(&u["contexts"]) }, a: u["uri"].as_str().unwrap_or("").into(), label: l, ..Default::default() });
    }
    for (k, s) in entries(c, "onlineServices") {
        let l = label(&s);
        // a plain web link without a service is a website (Bulwark keeps URLs here too)
        let value = s["user"].as_str().or(s["uri"].as_str()).unwrap_or("").to_string();
        v.push(Field { section: "im".into(), key: Some(k), kind: if l.is_some() { CONTEXTS.len() as i32 } else { ctx_index(&s["contexts"]) }, a: value,
            b: s["service"].as_str().unwrap_or("").into(), label: l, ..Default::default() });
    }
    for (k, a) in entries(c, "anniversaries") {
        let kind = DATE_KINDS.iter().position(|(x, _)| a["kind"] == *x).unwrap_or(3) as i32;
        v.push(Field { section: "date".into(), key: Some(k), kind, a: date_text(&a["date"]), ..Default::default() });
    }
    for (name, r) in entries(c, "relatedTo") {
        let rel = r["relation"].as_object().and_then(|o| o.iter().find(|(_, v)| **v == true).map(|(k, _)| k.clone())).unwrap_or_default();
        let kind = RELATIONS.iter().position(|(x, _)| *x == rel).unwrap_or(0) as i32;
        v.push(Field { section: "related".into(), key: Some(name.clone()), kind, a: name, ..Default::default() });
    }
    v
}

// ---------------- fields -> card ----------------

fn rand_hex(n: usize) -> String {
    let mut b = vec![0u8; n];
    let _ = getrandom::getrandom(&mut b);
    b.iter().map(|x| format!("{x:02x}")).collect()
}

pub fn new_uid() -> String {
    let h = rand_hex(16);
    // RFC 4122 v4 layout
    format!("urn:uuid:{}-{}-4{}-{}{}-{}", &h[0..8], &h[8..12], &h[13..16], ["8", "9", "a", "b"][(h.as_bytes()[16] % 4) as usize], &h[17..20], &h[20..32])
}

fn free_key(m: &Map<String, Value>, prefix: &str) -> String {
    (0..).map(|i| format!("{prefix}{i}")).find(|k| !m.contains_key(k)).unwrap()
}

fn put(o: &mut Map<String, Value>, k: &str, v: Value) { if v.is_null() { o.remove(k); } else { o.insert(k.into(), v); } }

/// The name, single fields and every section built from the form, starting from the card's
/// own entries so properties the form does not show survive (Bulwark contact-form).
#[derive(Default, Clone)]
pub struct Form {
    pub prefix: String, pub given: String, pub middle: String, pub surname: String, pub suffix: String, pub nickname: String,
    pub org: String, pub dept: String, pub job_title: String, pub role: String, pub notes: String,
}

pub fn build_card(orig: Option<&Value>, f: &Form, fields: &[Field]) -> Result<Map<String, Value>, String> {
    let empty = json!({});
    let o = orig.unwrap_or(&empty);
    let mut out = Map::new();
    // name: the form's parts plus components it does not edit (surname2, …)
    let mut comps = vec![];
    for (kind, v) in [("title", &f.prefix), ("given", &f.given), ("given2", &f.middle), ("surname", &f.surname), ("generation", &f.suffix)] {
        if !v.trim().is_empty() { comps.push(json!({"@type": "NameComponent", "kind": kind, "value": v.trim()})); }
    }
    if let Some(old) = o["name"]["components"].as_array() {
        for c in old {
            if !matches!(c["kind"].as_str(), Some("title" | "given" | "given2" | "surname" | "generation" | "prefix" | "suffix" | "middle" | "additional" | "separator")) { comps.push(c.clone()); }
        }
    }
    let full = [&f.prefix, &f.given, &f.middle, &f.surname, &f.suffix].iter().map(|s| s.trim()).filter(|s| !s.is_empty()).collect::<Vec<_>>().join(" ");
    out.insert("name".into(), if comps.is_empty() && full.is_empty() { Value::Null } else {
        let mut n = o["name"].as_object().cloned().unwrap_or_default();
        n.insert("@type".into(), json!("Name"));
        n.insert("components".into(), json!(comps));
        n.insert("isOrdered".into(), json!(true));
        n.insert("full".into(), json!(full));
        n.remove("sortAs");
        Value::Object(n)
    });
    // first entry of single-value maps edited in place, the rest kept
    let first_edit = |map: &str, prefix: &str, value: Option<Value>| -> Value {
        let mut m = o[map].as_object().cloned().unwrap_or_default();
        let k = m.keys().next().cloned();
        match (k, value) {
            (Some(k), Some(v)) => { let mut e = m[&k].as_object().cloned().unwrap_or_default(); for (pk, pv) in v.as_object().unwrap() { e.insert(pk.clone(), pv.clone()); } m.insert(k, Value::Object(e)); }
            (Some(k), None) => { m.remove(&k); }
            (None, Some(v)) => { let k = free_key(&m, prefix); m.insert(k, v); }
            (None, None) => {}
        }
        if m.is_empty() { Value::Null } else { Value::Object(m) }
    };
    let nn = f.nickname.trim();
    out.insert("nicknames".into(), first_edit("nicknames", "k", (!nn.is_empty()).then(|| json!({"@type": "Nickname", "name": nn}))));
    let (org, dept) = (f.org.trim(), f.dept.trim());
    out.insert("organizations".into(), edit_organizations(o["organizations"].as_object().cloned().unwrap_or_default(), org, dept));
    // titles: one title and one role (Bulwark t0 / t1), others kept
    let mut titles = o["titles"].as_object().cloned().unwrap_or_default();
    for (kind, v) in [("title", f.job_title.trim()), ("role", f.role.trim())] {
        let k = titles.iter().find(|(_, t)| t["kind"].as_str().unwrap_or("title") == kind).map(|(k, _)| k.clone());
        match (k, v.is_empty()) {
            (Some(k), true) => { titles.remove(&k); }
            (Some(k), false) => { titles[&k]["name"] = json!(v); }
            (None, false) => { let k = free_key(&titles, "t"); titles.insert(k, json!({"@type": "Title", "name": v, "kind": kind})); }
            (None, true) => {}
        }
    }
    out.insert("titles".into(), if titles.is_empty() { Value::Null } else { Value::Object(titles) });
    let note = f.notes.trim_end();
    out.insert("notes".into(), first_edit("notes", "n", (!note.trim().is_empty()).then(|| json!({"@type": "Note", "note": note}))));

    let section = |name: &str, map: &str, prefix: &str, make: &dyn Fn(&Field, Map<String, Value>) -> Result<Option<Map<String, Value>>, String>| -> Result<Value, String> {
        let old = o[map].as_object().cloned().unwrap_or_default();
        let mut m = Map::new();
        for fl in fields.iter().filter(|x| x.section == name) {
            let base = fl.key.as_ref().and_then(|k| old.get(k)).and_then(|e| e.as_object().cloned()).unwrap_or_default();
            if let Some(e) = make(fl, base)? {
                let k = fl.key.clone().filter(|k| !m.contains_key(k)).unwrap_or_else(|| { let mut all = old.clone(); all.extend(m.clone()); free_key(&all, prefix) });
                m.insert(k, Value::Object(e));
            }
        }
        Ok(if m.is_empty() { Value::Null } else { Value::Object(m) })
    };
    let ctx_or_label = |fl: &Field, e: &mut Map<String, Value>| {
        if label_picked(fl) { return; }
        e.remove("label");
        put(e, "contexts", ctx_value(fl.kind));
    };
    out.insert("emails".into(), section("email", "emails", "e", &|fl, mut e| {
        let a = fl.a.trim();
        if a.is_empty() { return Ok(None); }
        if !a.contains('@') || a.contains(char::is_whitespace) { return Err(format!("\"{a}\" is not an email address")); }
        e.insert("@type".into(), json!("EmailAddress"));
        e.insert("address".into(), json!(a));
        ctx_or_label(fl, &mut e);
        Ok(Some(e))
    })?);
    out.insert("phones".into(), section("phone", "phones", "p", &|fl, mut e| {
        let a = fl.a.trim();
        if a.is_empty() { return Ok(None); }
        e.insert("@type".into(), json!("Phone"));
        e.insert("number".into(), json!(a));
        if !label_picked(fl) {
            e.remove("label");
            let feat = PHONE_FEATURES.get(fl.kind as usize).map(|x| x.0).unwrap_or("voice");
            e.insert("features".into(), json!({ feat: true }));
        }
        put(&mut e, "contexts", match fl.kind2 { 1 => json!({"private": true}), 2 => json!({"work": true}), _ => Value::Null });
        Ok(Some(e))
    })?);
    out.insert("addresses".into(), section("address", "addresses", "a", &|fl, mut e| {
        let parts = [("name", &fl.a), ("locality", &fl.b), ("region", &fl.c), ("postcode", &fl.d), ("country", &fl.e)];
        if parts.iter().all(|(_, v)| v.trim().is_empty()) { return Ok(None); }
        let comps: Vec<Value> = parts.iter().filter(|(_, v)| !v.trim().is_empty()).map(|(k, v)| json!({"@type": "AddressComponent", "kind": k, "value": v.trim()})).collect();
        e.insert("@type".into(), json!("Address"));
        e.insert("components".into(), json!(comps));
        e.insert("isOrdered".into(), json!(false));
        for k in ["street", "locality", "region", "postcode", "country", "full", "fullAddress"] { e.remove(k); }
        ctx_or_label(fl, &mut e);
        Ok(Some(e))
    })?);
    out.insert("links".into(), section("url", "links", "l", &|fl, mut e| {
        let a = fl.a.trim();
        if a.is_empty() { return Ok(None); }
        let uri = if a.contains("://") || a.starts_with("mailto:") { a.to_string() } else { format!("https://{a}") };
        e.insert("@type".into(), json!("Link"));
        e.insert("uri".into(), json!(uri));
        ctx_or_label(fl, &mut e);
        Ok(Some(e))
    })?);
    out.insert("onlineServices".into(), section("im", "onlineServices", "s", &|fl, mut e| {
        let a = fl.a.trim();
        if a.is_empty() { return Ok(None); }
        e.insert("@type".into(), json!("OnlineService"));
        let is_uri = a.contains(':') && a.split(':').next().is_some_and(|s| !s.is_empty() && s.chars().all(|c| c.is_ascii_alphanumeric() || "+.-".contains(c)));
        if is_uri { e.insert("uri".into(), json!(a)); e.remove("user"); } else { e.insert("user".into(), json!(a)); e.remove("uri"); }
        put(&mut e, "service", if fl.b.trim().is_empty() { Value::Null } else { json!(fl.b.trim()) });
        ctx_or_label(fl, &mut e);
        Ok(Some(e))
    })?);
    out.insert("anniversaries".into(), section("date", "anniversaries", "a", &|fl, mut e| {
        let a = fl.a.trim();
        if a.is_empty() { return Ok(None); }
        e.insert("@type".into(), json!("Anniversary"));
        e.insert("kind".into(), json!(DATE_KINDS.get(fl.kind as usize).map(|x| x.0).unwrap_or("other")));
        // a date the user did not touch is kept as the server has it, even when it is not a calendar day
        let unchanged = e.get("date").is_some_and(|d| date_text(d) == a);
        if !unchanged { e.insert("date".into(), parse_date_text(a)?); }
        Ok(Some(e))
    })?);
    // related: keyed by the person's name or URI
    let old_rel = o["relatedTo"].as_object().cloned().unwrap_or_default();
    let mut rel = Map::new();
    for fl in fields.iter().filter(|x| x.section == "related") {
        let a = fl.a.trim();
        if a.is_empty() { continue; }
        let mut e = fl.key.as_ref().and_then(|k| old_rel.get(k)).and_then(|x| x.as_object().cloned()).unwrap_or_default();
        e.insert("@type".into(), json!("Relation"));
        let r = RELATIONS.get(fl.kind as usize).map(|x| x.0).unwrap_or("contact");
        e.insert("relation".into(), json!({ r: true }));
        rel.insert(a.to_string(), Value::Object(e));
    }
    out.insert("relatedTo".into(), if rel.is_empty() { Value::Null } else { Value::Object(rel) });
    Ok(out)
}

/// The organisations after the form's organisation and department fields: the first
/// organisation's name and its first unit are edited in place; its other units, the other
/// organisations and properties the form does not show are kept, and emptying the department
/// removes only the first unit (D-M5-A06; Bulwark's contact-form.tsx:729 keeps the units only
/// while the department is untouched and otherwise replaces them all).
pub fn edit_organizations(mut orgs: Map<String, Value>, name: &str, dept: &str) -> Value {
    let first = orgs.keys().next().cloned();
    let Some(k) = first else {
        if name.is_empty() && dept.is_empty() { return Value::Null; }
        let mut e = Map::new();
        e.insert("@type".into(), json!("Organization"));
        if !name.is_empty() { e.insert("name".into(), json!(name)); }
        if !dept.is_empty() { e.insert("units".into(), json!([{"@type": "OrgUnit", "name": dept}])); }
        orgs.insert("o0".into(), Value::Object(e));
        return Value::Object(orgs);
    };
    let mut e = orgs[&k].as_object().cloned().unwrap_or_default();
    let mut units = e.get("units").and_then(|u| u.as_array()).cloned().unwrap_or_default();
    let shown = units.first().and_then(|u| u["name"].as_str()).unwrap_or("");
    if dept.is_empty() {
        // only a unit the form showed (it had a name) is removed by emptying the field
        if !shown.is_empty() { units.remove(0); }
    } else if let Some(u) = units.first_mut().and_then(|u| u.as_object_mut()) {
        u.insert("name".into(), json!(dept));
        u.entry("@type").or_insert_with(|| json!("OrgUnit"));
    } else {
        units.insert(0, json!({"@type": "OrgUnit", "name": dept}));
    }
    if units.is_empty() { e.remove("units"); } else { e.insert("units".into(), Value::Array(units)); }
    if name.is_empty() { e.remove("name"); } else { e.insert("name".into(), json!(name)); }
    e.entry("@type").or_insert_with(|| json!("Organization"));
    // nothing left of it: the entry goes
    if !e.contains_key("name") && !e.contains_key("units") { orgs.remove(&k); } else { orgs.insert(k, Value::Object(e)); }
    if orgs.is_empty() { Value::Null } else { Value::Object(orgs) }
}

// ---------------- detail ----------------

const MONTHS: [&str; 12] = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

/// "12 April 1985", "12 April", "1985"; with the days until the next one when it is a
/// birthday or anniversary.
pub fn pretty_date(d: &Value, today: chrono::NaiveDate) -> (String, Option<i64>) {
    let t = date_text(d);
    let parts: Vec<&str> = t.trim_start_matches("--").split('-').collect();
    let (y, m, dd) = if t.starts_with("--") { (None, parts.first().and_then(|x| x.parse::<u32>().ok()), parts.get(1).and_then(|x| x.parse::<u32>().ok())) }
        else { (parts.first().and_then(|x| x.parse::<i32>().ok()), parts.get(1).and_then(|x| x.parse::<u32>().ok()), parts.get(2).and_then(|x| x.parse::<u32>().ok())) };
    let text = match (y, m, dd) {
        (Some(y), Some(m), Some(d)) if (1..=12).contains(&m) => format!("{d} {} {y}", MONTHS[m as usize - 1]),
        (None, Some(m), Some(d)) if (1..=12).contains(&m) => format!("{d} {}", MONTHS[m as usize - 1]),
        (Some(y), Some(m), None) if (1..=12).contains(&m) => format!("{} {y}", MONTHS[m as usize - 1]),
        _ => t.clone(),
    };
    let next = match (m, dd) { (Some(m), Some(d)) => next_occurrence(m, d, today).map(|x| (x - today).num_days()), _ => None };
    (text, next)
}

/// The next time a month and day comes round, today included. 29 February in a common year is
/// 1 March (D-M5-A07; the days until it are never negative). None for a day no year has.
pub fn next_occurrence(month: u32, day: u32, today: chrono::NaiveDate) -> Option<chrono::NaiveDate> {
    use chrono::Datelike;
    // a leap day is at most 8 years away (1896 to 1904 has none in between)
    (today.year()..=today.year() + 8).find_map(|y| {
        let d = chrono::NaiveDate::from_ymd_opt(y, month, day).or_else(|| (month == 2 && day == 29).then(|| chrono::NaiveDate::from_ymd_opt(y, 3, 1)).flatten())?;
        (d >= today).then_some(d)
    })
}

fn line(section: &str, label: &str, value: &str, act: &str, tint: &str) -> CtLine {
    CtLine { section: section.into(), label: label.into(), value: value.into(), act: act.into(), tint: tint.into() }
}

fn ctx_label(c: &Value) -> &'static str { if c["work"] == true { "Work" } else if c["private"] == true { "Personal" } else { "" } }

pub fn detail_lines(c: &Value, today: chrono::NaiveDate) -> Vec<CtLine> {
    let mut v = vec![];
    let lab = |e: &Value, dflt: &str| -> String {
        e["label"].as_str().filter(|l| !l.trim().is_empty()).map(String::from).unwrap_or_else(|| { let l = ctx_label(&e["contexts"]); if l.is_empty() { dflt.into() } else { l.into() } })
    };
    for (_, e) in entries(c, "emails") { v.push(line("EMAIL", &lab(&e, "Email"), e["address"].as_str().unwrap_or(""), "mail", "")); }
    for (_, p) in entries(c, "phones") {
        let f = PHONE_FEATURES.get(phone_kind(&p) as usize).map(|x| x.1).unwrap_or("");
        let l = p["label"].as_str().filter(|l| !l.trim().is_empty()).map(String::from)
            .unwrap_or_else(|| [f, ctx_label(&p["contexts"])].iter().filter(|x| !x.is_empty()).copied().collect::<Vec<_>>().join(" · "));
        v.push(line("PHONE", &l, p["number"].as_str().unwrap_or(""), "tel", ""));
    }
    for (_, a) in entries(c, "addresses") {
        let (st, loc, reg, pc, co) = crate::vcard::address_parts(&a);
        let city = [pc.as_str(), loc.as_str()].iter().filter(|x| !x.is_empty()).copied().collect::<Vec<_>>().join(" ");
        let mut text = [st.as_str(), city.as_str(), reg.as_str(), co.as_str()].iter().filter(|x| !x.is_empty()).copied().collect::<Vec<_>>().join("\n");
        if text.is_empty() { text = a["full"].as_str().or(a["fullAddress"].as_str()).unwrap_or("").to_string(); }
        v.push(line("ADDRESS", &lab(&a, "Address"), &text, "", ""));
    }
    for (_, u) in entries(c, "links") { v.push(line("WEB", &lab(&u, "Website"), u["uri"].as_str().unwrap_or(""), "url", "")); }
    for (_, s) in entries(c, "onlineServices") {
        let val = s["user"].as_str().or(s["uri"].as_str()).unwrap_or("");
        let is_web = s["service"].is_null() && (val.starts_with("http://") || val.starts_with("https://"));
        let l = s["service"].as_str().map(String::from).or_else(|| s["label"].as_str().map(String::from)).unwrap_or_else(|| if is_web { "Website".into() } else { "Account".into() });
        v.push(line(if is_web { "WEB" } else { "MESSAGING & SOCIAL" }, &l, val, if is_web { "url" } else { "" }, ""));
    }
    for (_, a) in entries(c, "anniversaries") {
        let kind = DATE_KINDS.iter().find(|(k, _)| a["kind"] == *k).map(|x| x.1).unwrap_or("Date");
        let (mut text, next) = pretty_date(&a["date"], today);
        if let Some(p) = a["place"]["full"].as_str().or(a["place"]["fullAddress"].as_str()) { text = if text.is_empty() { p.to_string() } else { format!("{text}, {p}") }; }
        let soon = a["kind"] != "death" && next.is_some_and(|n| n <= 14);
        v.push(line("DATES", kind, &text, "", if soon { "rose" } else { "" }));
    }
    for (name, r) in entries(c, "relatedTo") {
        let rel = r["relation"].as_object().and_then(|o| o.iter().find(|(_, v)| **v == true).map(|(k, _)| k.clone())).unwrap_or_default();
        let l = RELATIONS.iter().find(|(k, _)| *k == rel).map(|x| x.1.to_string()).unwrap_or_else(|| if rel.is_empty() { "Related".into() } else { rel.clone() });
        v.push(line("RELATED PEOPLE", &l, name.strip_prefix("urn:uuid:").map(|_| "(contact)").unwrap_or(&name), "", ""));
    }
    let nicks: Vec<String> = entries(c, "nicknames").iter().filter_map(|(_, n)| n["name"].as_str().map(String::from)).collect();
    if !nicks.is_empty() { v.push(line("ABOUT", "Nickname", &nicks.join(", "), "", "")); }
    for (_, l) in entries(c, "preferredLanguages") { v.push(line("ABOUT", "Language", l["language"].as_str().unwrap_or(""), "", "")); }
    for (_, n) in entries(c, "notes") { if let Some(t) = n["note"].as_str().filter(|t| !t.trim().is_empty()) { v.push(line("NOTES", "Note", t, "", "")); } }
    v
}

fn subtitle(c: &Value) -> String {
    let title = entries(c, "titles").iter().find(|(_, t)| t["kind"].as_str().unwrap_or("title") == "title").and_then(|(_, t)| t["name"].as_str().map(String::from)).unwrap_or_default();
    let role = entries(c, "titles").iter().find(|(_, t)| t["kind"] == "role").and_then(|(_, t)| t["name"].as_str().map(String::from)).unwrap_or_default();
    let org = entries(c, "organizations").first().map(|(_, o)| {
        let units: Vec<String> = o["units"].as_array().map(|u| u.iter().filter_map(|x| x["name"].as_str().map(String::from)).collect()).unwrap_or_default();
        [o["name"].as_str().unwrap_or("").to_string()].into_iter().chain(units).filter(|x| !x.is_empty()).collect::<Vec<_>>().join(", ")
    }).unwrap_or_default();
    [title, role, org].into_iter().filter(|x| !x.is_empty()).collect::<Vec<_>>().join(" · ")
}

fn to_model<T: Clone + 'static>(v: Vec<T>) -> ModelRc<T> { ModelRc::new(VecModel::from(v)) }

/// Whether bytes are a picture this app will decode: at most `MAX_IMAGE_BYTES`, and at most
/// `MAX_IMAGE_PIXELS` by the size in its header, which is read before any pixel is allocated
/// (D-M5-03, D-M5-A03: a 100 000 x 100 000 PNG is a few hundred bytes).
pub fn check_image(bytes: &[u8]) -> Result<(), String> {
    if bytes.len() > crate::vcard::MAX_IMAGE_BYTES { return Err("That picture is larger than 8 MiB.".into()); }
    let reader = image::ImageReader::new(std::io::Cursor::new(bytes)).with_guessed_format().map_err(|e| format!("Not an image: {e}"))?;
    let (w, h) = reader.into_dimensions().map_err(|e| format!("Not an image: {e}"))?;
    if u64::from(w) * u64::from(h) > crate::vcard::MAX_IMAGE_PIXELS { return Err("That picture has too many pixels (more than 40 million).".into()); }
    Ok(())
}

/// Decode a picture after `check_image`, with the decoder's own allocation limit as well.
fn load_checked(bytes: &[u8]) -> Result<image::DynamicImage, String> {
    check_image(bytes)?;
    let mut reader = image::ImageReader::new(std::io::Cursor::new(bytes)).with_guessed_format().map_err(|e| format!("Not an image: {e}"))?;
    let mut limits = image::Limits::default();
    limits.max_alloc = Some(512 * 1024 * 1024);
    reader.limits(limits);
    reader.decode().map_err(|e| format!("Not an image: {e}"))
}

fn decode_image(bytes: &[u8]) -> Option<SharedPixelBuffer<Rgba8Pixel>> {
    let img = load_checked(bytes).ok()?;
    let img = if img.width() > 256 || img.height() > 256 { img.thumbnail(256, 256) } else { img };
    let rgba = img.to_rgba8();
    Some(SharedPixelBuffer::clone_from_slice(rgba.as_raw(), rgba.width(), rgba.height()))
}

fn photo_entry(c: &Value) -> Option<Value> {
    c["media"].as_object().and_then(|m| m.values().find(|x| x["kind"] == "photo").cloned())
}

/// The photo's bytes: from its data: URI here, from its blob on the server.
fn photo_bytes(c: Option<&Client>, card: &Value) -> Option<Vec<u8>> {
    use base64::Engine;
    let p = photo_entry(card)?;
    if let Some((_, data)) = p["uri"].as_str().and_then(crate::vcard::parse_data_uri) {
        // refused on the text's length, before decoding it (a base64 bomb in a card from a server or a file)
        if !crate::vcard::base64_ok(data.trim()) { return None; }
        return base64::engine::general_purpose::STANDARD.decode(data.trim()).ok();
    }
    let blob = p["blobId"].as_str()?;
    let c = c?;
    c.download_from(&c.account_for(CAP), blob, "photo", p["mediaType"].as_str().unwrap_or("image/jpeg")).ok().filter(|b| b.len() <= crate::vcard::MAX_IMAGE_BYTES)
}

/// Show a card (or a directory entry) in the detail card; photos load in the background.
pub fn show(u: &AppWindow, card: Option<&Value>, client: Option<Arc<Client>>) {
    let g = u.global::<CtX>();
    let today = chrono::Local::now().date_naive();
    g.set_directory_entry(false);
    let Some(c) = card else {
        g.set_lines(to_model(vec![])); g.set_subtitle("".into()); g.set_chips(to_model(vec![])); g.set_has_photo(false);
        load_editor(u, None);
        return;
    };
    g.set_lines(to_model(detail_lines(c, today)));
    g.set_subtitle(subtitle(c).into());
    let mut chips: Vec<SharedString> = c["keywords"].as_object().map(|o| o.iter().filter(|(_, v)| **v == true).map(|(k, _)| SharedString::from(k.as_str())).collect()).unwrap_or_default();
    if c["kind"].as_str().is_some_and(|k| k != "individual") { chips.insert(0, SharedString::from(match c["kind"].as_str() { Some("org") => "Organisation", Some("group") => "Group", Some(k) => k, None => "" })); }
    g.set_chips(to_model(chips));
    let id = c["id"].as_str().unwrap_or("").to_string();
    let cached = S.with(|s| s.borrow().photos.get(&id).cloned());
    match cached {
        Some(Some(buf)) => { g.set_photo(Image::from_rgba8(buf)); g.set_has_photo(true); }
        Some(None) => g.set_has_photo(false),
        None => {
            g.set_has_photo(false);
            if photo_entry(c).is_some() {
                let (w, card, id2) = (u.as_weak(), c.clone(), id.clone());
                std::thread::spawn(move || {
                    let buf = photo_bytes(client.as_deref(), &card).and_then(|b| decode_image(&b));
                    let _ = w.upgrade_in_event_loop(move |u| {
                        S.with(|s| s.borrow_mut().photos.insert(id2.clone(), buf.clone()));
                        if u.get_ct_id() == id2.as_str() {
                            if let Some(b) = buf { u.global::<CtX>().set_photo(Image::from_rgba8(b.clone())); u.global::<CtX>().set_has_photo(true);
                                u.global::<CtX>().set_edit_photo(Image::from_rgba8(b)); u.global::<CtX>().set_edit_has_photo(true); }
                        }
                    });
                });
            } else {
                S.with(|s| s.borrow_mut().photos.insert(id.clone(), None));
            }
        }
    }
    load_editor(u, Some(c));
}

fn render_fields(u: &AppWindow) {
    let rows: Vec<CtField> = S.with(|s| s.borrow().ed.fields.iter().map(|f| {
        let (k1, k2) = kinds_for(f);
        CtField { section: f.section.as_str().into(), key: f.key.clone().unwrap_or_default().into(), kind: f.kind, kinds: to_model(k1.into_iter().map(SharedString::from).collect()),
            kind2: f.kind2, kinds2: to_model(k2.into_iter().map(SharedString::from).collect()), a: f.a.as_str().into(), b: f.b.as_str().into(), c: f.c.as_str().into(), d: f.d.as_str().into(), e: f.e.as_str().into() }
    }).collect());
    u.global::<CtX>().set_fields(to_model(rows));
}

fn books_model(u: &AppWindow) {
    let books = crate::contacts::books();
    let names: Vec<SharedString> = books.iter().map(|b| SharedString::from(b["name"].as_str().unwrap_or("Address book"))).collect();
    let def = books.iter().position(|b| b["isDefault"] == true).unwrap_or(0) as i32;
    let g = u.global::<CtX>();
    g.set_books(to_model(names));
    g.set_book(def);
    g.set_import_book(def);
}

/// Fill the form from a card (None: empty form for a new contact).
pub fn load_editor(u: &AppWindow, card: Option<&Value>) {
    let g = u.global::<CtX>();
    let empty = json!({});
    let c = card.unwrap_or(&empty);
    let comp = |kinds: &[&str]| c["name"]["components"].as_array().and_then(|a| a.iter().find(|x| x["kind"].as_str().is_some_and(|k| kinds.contains(&k))))
        .and_then(|x| x["value"].as_str()).unwrap_or("").to_string();
    let mut given = comp(&["given"]);
    let surname = comp(&["surname"]);
    // a card with only a full name: it goes into the first-name field
    if given.is_empty() && surname.is_empty() && c["name"]["components"].as_array().is_none_or(|a| a.is_empty()) { given = c["name"]["full"].as_str().unwrap_or("").to_string(); }
    g.set_prefix(comp(&["title", "prefix"]).into());
    g.set_given(given.into());
    g.set_middle(comp(&["given2", "middle", "additional"]).into());
    g.set_surname(surname.into());
    g.set_suffix(comp(&["generation", "suffix"]).into());
    g.set_nickname(entries(c, "nicknames").first().and_then(|(_, n)| n["name"].as_str().map(String::from)).unwrap_or_default().into());
    let org = entries(c, "organizations").first().map(|(_, o)| o.clone()).unwrap_or(Value::Null);
    g.set_org(org["name"].as_str().unwrap_or("").into());
    g.set_dept(org["units"][0]["name"].as_str().unwrap_or("").into());
    g.set_job_title(entries(c, "titles").iter().find(|(_, t)| t["kind"].as_str().unwrap_or("title") == "title").and_then(|(_, t)| t["name"].as_str().map(String::from)).unwrap_or_default().into());
    g.set_role(entries(c, "titles").iter().find(|(_, t)| t["kind"] == "role").and_then(|(_, t)| t["name"].as_str().map(String::from)).unwrap_or_default().into());
    g.set_notes(entries(c, "notes").first().and_then(|(_, n)| n["note"].as_str().map(String::from)).unwrap_or_default().into());
    g.set_is_new(card.is_none());
    g.set_status("".into());
    let mut fields = fields_of(c);
    if card.is_none() { fields.push(Field { section: "email".into(), ..Default::default() }); fields.push(Field { section: "phone".into(), kind: 1, ..Default::default() }); }
    S.with(|s| { let mut s = s.borrow_mut(); s.ed = Ed { orig: card.cloned(), fields, photo: PhotoEdit::Unchanged }; });
    let id = c["id"].as_str().unwrap_or("").to_string();
    match S.with(|s| s.borrow().photos.get(&id).cloned().flatten()) {
        Some(b) if card.is_some() => { g.set_edit_photo(Image::from_rgba8(b)); g.set_edit_has_photo(true); }
        _ => g.set_edit_has_photo(false),
    }
    books_model(u);
    render_fields(u);
}

fn form(u: &AppWindow) -> Form {
    let g = u.global::<CtX>();
    Form { prefix: g.get_prefix().into(), given: g.get_given().into(), middle: g.get_middle().into(), surname: g.get_surname().into(), suffix: g.get_suffix().into(),
        nickname: g.get_nickname().into(), org: g.get_org().into(), dept: g.get_dept().into(), job_title: g.get_job_title().into(), role: g.get_role().into(), notes: g.get_notes().into() }
}

/// Shrink a picked image to at most 512 px and encode it as JPEG (Bulwark image-shrink).
pub fn shrink_photo(bytes: &[u8]) -> Result<(String, Vec<u8>), String> {
    let img = load_checked(bytes)?;
    let img = if img.width() > 512 || img.height() > 512 { img.thumbnail(512, 512) } else { img };
    let mut out = std::io::Cursor::new(Vec::new());
    image::DynamicImage::ImageRgb8(img.to_rgb8()).write_to(&mut out, image::ImageFormat::Jpeg).map_err(|e| e.to_string())?;
    Ok(("image/jpeg".into(), out.into_inner()))
}

fn save(u: &AppWindow, c: Arc<Client>) {
    let f = form(u);
    let (orig, fields, photo) = S.with(|s| { let s = s.borrow(); (s.ed.orig.clone(), s.ed.fields.clone(), s.ed.photo.clone()) });
    let mut card = match build_card(orig.as_ref(), &f, &fields) { Ok(c) => c, Err(e) => { u.global::<CtX>().set_status(e.into()); return } };
    let has_name = !card["name"].is_null() || !card["organizations"].is_null();
    let has_email = !card["emails"].is_null();
    if !has_name && !has_email { u.global::<CtX>().set_status("Add a name, an organisation or an email address.".into()); return; }
    // media: photo edits; other media kept
    let mut media: Map<String, Value> = orig.as_ref().and_then(|o| o["media"].as_object().cloned()).unwrap_or_default();
    let photo_key = media.iter().find(|(_, m)| m["kind"] == "photo").map(|(k, _)| k.clone());
    let new_photo = match &photo {
        PhotoEdit::Unchanged => None,
        PhotoEdit::Removed => { if let Some(k) = &photo_key { media.remove(k); } None }
        PhotoEdit::New(mime, bytes) => Some((photo_key.clone().unwrap_or_else(|| free_key(&media, "m")), mime.clone(), bytes.clone())),
    };
    let id = orig.as_ref().and_then(|o| o["id"].as_str().map(String::from));
    let book_idx = u.global::<CtX>().get_book();
    let book = crate::contacts::books().get(book_idx.max(0) as usize).and_then(|b| b["id"].as_str().map(String::from));
    let w = u.as_weak();
    u.global::<CtX>().set_status("Saving…".into());
    std::thread::spawn(move || {
        let acc = c.account_for(CAP);
        let write = |card: &Map<String, Value>| -> Result<String, String> {
            let r = match &id {
                Some(id) => c.call(json!([["ContactCard/set", {"accountId": acc, "update": { id.clone(): card }}, "s"]]))?,
                None => {
                    let mut cc = card.clone();
                    cc.retain(|_, v| !v.is_null());
                    cc.insert("@type".into(), json!("Card"));
                    cc.insert("version".into(), json!("1.0"));
                    cc.insert("uid".into(), json!(new_uid()));
                    let b = book.clone().or_else(|| crate::contacts::default_book_for(&c));
                    let Some(b) = b else { return Err("No address book".into()) };
                    cc.insert("addressBookIds".into(), json!({ b: true }));
                    c.call(json!([["ContactCard/set", {"accountId": acc, "create": {"c": cc}}, "s"]]))?
                }
            };
            if r[0][0] == "error" { return Err(crate::jmap::method_error(&r[0][1])); }
            for k in ["notCreated", "notUpdated"] {
                if let Some(e) = r[0][1][k].as_object().and_then(|m| m.values().next()) {
                    let props = e["properties"].as_array().map(|p| p.iter().filter_map(|x| x.as_str()).collect::<Vec<_>>().join(",")).unwrap_or_default();
                    return Err(format!("{}{}", crate::jmap::set_error(e), if props.starts_with("media") { "\u{1}media" } else { "" }));
                }
            }
            Ok(r[0][1]["created"]["c"]["id"].as_str().map(String::from).or(id.clone()).unwrap_or_default())
        };
        let res = match new_photo {
            None => { card.insert("media".into(), if media.is_empty() { Value::Null } else { Value::Object(media) }); write(&card) }
            Some((key, mime, bytes)) => {
                use base64::Engine;
                let uri = format!("data:{mime};base64,{}", base64::engine::general_purpose::STANDARD.encode(&bytes));
                // Stalwart 1.0 keeps photos as blobs; 0.16 refuses blobIds in media: retry inline
                let blob = c.upload(bytes.clone(), &mime).ok().map(|b| b.0);
                let mut with = media.clone();
                let res = match blob {
                    Some(b) => {
                        with.insert(key.clone(), json!({"@type": "Media", "kind": "photo", "blobId": b, "mediaType": mime}));
                        card.insert("media".into(), Value::Object(with));
                        write(&card)
                    }
                    None => Err("\u{1}media".into()),
                };
                match res {
                    Err(e) if e.ends_with("\u{1}media") => {
                        media.insert(key, json!({"@type": "Media", "kind": "photo", "uri": uri, "mediaType": mime}));
                        card.insert("media".into(), Value::Object(media));
                        write(&card)
                    }
                    r => r,
                }
            }
        };
        let new_photo_buf = match &photo { PhotoEdit::New(_, b) => decode_image(b), _ => None };
        let removed = matches!(photo, PhotoEdit::Removed);
        let _ = w.upgrade_in_event_loop(move |u| match res {
            Ok(id) => {
                S.with(|s| { let mut s = s.borrow_mut(); if removed { s.photos.insert(id.clone(), None); } else if let Some(b) = new_photo_buf { s.photos.insert(id.clone(), Some(b)); } else { s.photos.remove(&id); } });
                u.global::<CtX>().set_status("".into());
                u.set_ct_editing(false);
                crate::mail::show_toast(&u, "Contact saved", false);
                crate::contacts::reload_then_select(&u, c, &id);
            }
            Err(e) => u.global::<CtX>().set_status(e.trim_end_matches("\u{1}media").to_string().into()),
        });
    });
}

// ---------------- duplicates ----------------

pub fn dupe_groups() -> Vec<Vec<Value>> {
    let cards = crate::contacts::cards();
    crate::contact_merge::find_groups(&cards).into_iter().map(|g| g.into_iter().map(|i| cards[i].clone()).collect()).collect()
}

fn card_summary(c: &Value) -> String {
    let mut bits: Vec<String> = entries(c, "emails").iter().filter_map(|(_, e)| e["address"].as_str().map(String::from)).collect();
    bits.extend(entries(c, "phones").iter().filter_map(|(_, p)| p["number"].as_str().map(String::from)));
    if let Some(u) = c["updated"].as_str() { bits.push(format!("changed {}", &u[..10.min(u.len())])); }
    bits.join(" · ")
}

pub fn render_dupes(u: &AppWindow) {
    let groups = dupe_groups();
    let mut rows = vec![];
    let mut ids = vec![];
    for (gi, g) in groups.iter().enumerate() {
        let keep = crate::contact_merge::plan(g).map(|p| p.keep["id"].clone()).unwrap_or(Value::Null);
        ids.push(g.iter().filter_map(|c| c["id"].as_str().map(String::from)).collect::<Vec<_>>());
        for (i, c) in g.iter().enumerate() {
            rows.push(CtDupe { group: gi as i32, id: c["id"].as_str().unwrap_or("").into(), name: crate::contacts::display_name(c).into(), detail: card_summary(c).into(), keep: c["id"] == keep, first: i == 0 });
        }
    }
    S.with(|s| s.borrow_mut().groups = ids);
    let g = u.global::<CtX>();
    g.set_dupe_groups(groups.len() as i32);
    g.set_dupes(to_model(rows));
}

pub fn dupe_ids() -> std::collections::HashSet<String> {
    dupe_groups().into_iter().flatten().filter_map(|c| c["id"].as_str().map(String::from)).collect()
}

fn merge(u: &AppWindow, c: Arc<Client>, group: i32) {
    let ids = S.with(|s| s.borrow().groups.get(group as usize).cloned()).unwrap_or_default();
    let cards: Vec<Value> = crate::contacts::cards().into_iter().filter(|x| x["id"].as_str().is_some_and(|i| ids.iter().any(|y| y == i))).collect();
    let Some(plan) = crate::contact_merge::plan(&cards).filter(|_| cards.len() > 1) else { return };
    let moves = crate::contact_merge::group_moves(&crate::contacts::groups(), &plan.keep, &plan.others);
    let keep_id = plan.keep["id"].as_str().unwrap_or("").to_string();
    let mut update = Map::new();
    if !plan.patch.is_empty() { update.insert(keep_id.clone(), Value::Object(plan.patch.clone())); }
    for (gid, members) in moves { update.insert(gid, json!({"members": members})); }
    let destroy: Vec<Value> = plan.others.iter().map(|o| o["id"].clone()).collect();
    let w = u.as_weak();
    u.global::<CtX>().set_dupe_status("Merging…".into());
    std::thread::spawn(move || {
        let acc = c.account_for(CAP);
        // update first: a failed update leaves every card in place
        let res = c.call(json!([["ContactCard/set", {"accountId": acc, "update": update}, "u"]])).and_then(|r| {
            if r[0][0] == "error" { return Err(crate::jmap::method_error(&r[0][1])); }
            if let Some(e) = r[0][1]["notUpdated"].as_object().and_then(|m| m.values().next()) { return Err(crate::jmap::set_error(e)); }
            let d = c.call(json!([["ContactCard/set", {"accountId": acc, "destroy": destroy}, "d"]]))?;
            if let Some(e) = d[0][1]["notDestroyed"].as_object().and_then(|m| m.values().next()) { return Err(crate::jmap::set_error(e)); }
            Ok(())
        });
        let _ = w.upgrade_in_event_loop(move |u| {
            u.global::<CtX>().set_dupe_status(match &res { Ok(()) => "Merged.".into(), Err(e) => format!("Not merged: {e}").into() });
            S.with(|s| s.borrow_mut().photos.remove(&keep_id));
            crate::contacts::reload_then(&u, c, |u| render_dupes(u));
        });
    });
}

// ---------------- import / export ----------------

/// A file's bytes, read only when it is at most `cap` bytes (by its size on disk, and again on
/// what was read): Ok(None) for a file that cannot be opened, Err for one above the cap.
fn read_capped(path: &std::path::Path, cap: usize) -> Result<Option<Vec<u8>>, String> {
    use std::io::Read;
    let too_big = || format!("\"{}\" is larger than the limit ({} MiB).", path.file_name().map(|n| n.to_string_lossy().to_string()).unwrap_or_default(), cap / (1024 * 1024));
    let Ok(f) = std::fs::File::open(path) else { return Ok(None) };
    if f.metadata().is_ok_and(|m| m.len() > cap as u64) { return Err(too_big()); }
    let mut buf = vec![];
    // never read past the cap + 1, whatever the file grows to
    if f.take(cap as u64 + 1).read_to_end(&mut buf).is_err() { return Ok(None); }
    if buf.len() > cap { return Err(too_big()); }
    Ok(Some(buf))
}

/// The file name offered by the save picker for one exported contact (D-M5-18): letters, digits,
/// spaces and hyphens only, no leading or trailing dot, space or underscore, no Windows device
/// name, at most 80 characters; the picker still decides the folder.
pub fn export_file_name(display_name: &str) -> String {
    let cleaned: String = display_name.chars().map(|c| if c.is_alphanumeric() || c == ' ' || c == '-' { c } else { '_' }).take(80).collect();
    let mut stem = cleaned.trim_matches(|c: char| c == ' ' || c == '.' || c == '_').to_string();
    if stem.is_empty() { stem = "contact".into(); }
    // "CON.vcf", "nul.vcf", "COM1.vcf" name devices on Windows
    let up = stem.to_ascii_uppercase();
    let reserved = ["CON", "PRN", "AUX", "NUL"].contains(&up.as_str())
        || ((up.starts_with("COM") || up.starts_with("LPT")) && up.len() == 4 && up.as_bytes()[3].is_ascii_digit());
    if reserved { stem.insert(0, '_'); }
    format!("{stem}.vcf")
}

fn start_import(u: &AppWindow) {
    let Some(files) = crate::automation::pick_files() else { return };
    let mut cards = vec![];
    let mut names = vec![];
    let mut notes: Vec<String> = vec![];
    for f in &files {
        // at most 4 bytes a character: a bigger file is above the limit whatever it holds
        let bytes = match read_capped(f, crate::vcard::MAX_FILE_CHARS * 4) {
            Ok(Some(b)) => b,
            Ok(None) => continue,
            Err(_) => { notes.push(crate::vcard::ParseReport { file_too_large: true, ..Default::default() }.note()); continue }
        };
        let text = String::from_utf8_lossy(&bytes);
        let (got, rep) = crate::vcard::parse_report(&text);
        cards.extend(got);
        let note = rep.note();
        if !note.is_empty() { notes.push(note); }
        names.push(f.file_name().map(|n| n.to_string_lossy().to_string()).unwrap_or_default());
    }
    let g = u.global::<CtX>();
    notes.dedup();
    if cards.is_empty() {
        let msg = if notes.is_empty() { "No contacts found in that file. Pick a .vcf (vCard) file.".to_string() } else { notes.join(" ") };
        crate::mail::show_toast(u, &msg, false);
        return;
    }
    // all the files together stay within the card limit too
    if cards.len() > crate::vcard::MAX_CARDS { cards.truncate(crate::vcard::MAX_CARDS); notes.push(format!("Only the first {} contacts were read.", crate::vcard::MAX_CARDS)); }
    let dupes = crate::vcard::detect_duplicates(&crate::contacts::cards(), &cards);
    let n = cards.len();
    books_model(u);
    g.set_import_title("Import contacts".into());
    let note = if notes.is_empty() { String::new() } else { format!(" {}", notes.join(" ")) };
    g.set_import_summary(format!("{n} contact{} in {}.{note}", if n == 1 { "" } else { "s" }, names.join(", ")).into());
    g.set_import_dupes(dupes.len() as i32);
    g.set_import_skip_dupes(true);
    S.with(|s| { let mut s = s.borrow_mut(); s.import = cards; s.import_dupes = dupes; });
    g.set_import_open(true);
}

/// The cards to create: duplicates left out when asked, every card in `book`, a uid for
/// each, and ids gone.
pub fn import_batch(cards: &[Value], dupes: &[usize], skip: bool, book: &str) -> Vec<Value> {
    cards.iter().enumerate().filter(|(i, _)| !(skip && dupes.contains(i))).map(|(_, c)| {
        let mut c = c.clone();
        let o = c.as_object_mut().unwrap();
        o.remove("id");
        o.insert("addressBookIds".into(), json!({ book: true }));
        if !o.contains_key("uid") { o.insert("uid".into(), json!(new_uid())); }
        c
    }).collect()
}

fn run_import(u: &AppWindow, c: Arc<Client>) {
    let g = u.global::<CtX>();
    let skip = g.get_import_skip_dupes();
    let book = crate::contacts::books().get(g.get_import_book().max(0) as usize).and_then(|b| b["id"].as_str().map(String::from));
    let (cards, dupes) = S.with(|s| { let s = s.borrow(); (s.import.clone(), s.import_dupes.clone()) });
    g.set_import_busy(true);
    let w = u.as_weak();
    std::thread::spawn(move || {
        let acc = c.account_for(CAP);
        let res: Result<(usize, Vec<String>), String> = (|| {
            let book = book.or_else(|| crate::contacts::default_book_for(&c)).ok_or("No address book to import into")?;
            let batch = import_batch(&cards, &dupes, skip, &book);
            let (mut ok, mut bad) = (0, vec![]);
            for chunk in batch.chunks(c.max_set().min(100)) {
                let create: Map<String, Value> = chunk.iter().enumerate().map(|(i, x)| (format!("i{i}"), x.clone())).collect();
                let r = c.call(json!([["ContactCard/set", {"accountId": acc, "create": create}, "s"]]))?;
                if r[0][0] == "error" { return Err(crate::jmap::method_error(&r[0][1])); }
                ok += r[0][1]["created"].as_object().map(|m| m.len()).unwrap_or(0);
                if let Some(m) = r[0][1]["notCreated"].as_object() { bad.extend(m.values().map(crate::jmap::set_error)); }
            }
            Ok((ok, bad))
        })();
        let _ = w.upgrade_in_event_loop(move |u| {
            let g = u.global::<CtX>();
            g.set_import_busy(false);
            g.set_import_open(false);
            let msg = match res {
                Ok((n, bad)) if bad.is_empty() => format!("Imported {n} contact{}", if n == 1 { "" } else { "s" }),
                Ok((n, bad)) => format!("Imported {n}; {} not imported: {}", bad.len(), bad.first().cloned().unwrap_or_default()),
                Err(e) => format!("Import failed: {e}"),
            };
            crate::mail::show_toast(&u, &msg, false);
            S.with(|s| s.borrow_mut().import.clear());
            crate::contacts::reload_then(&u, c, |_| {});
        });
    });
}

/// Blob photos turned into data: URIs so the vCard carries the bytes (Bulwark
/// inlineContactMediaForExport); a photo that cannot be downloaded is left out.
fn inline_media(c: &Client, cards: &mut [Value]) {
    use base64::Engine;
    let acc = c.account_for(CAP);
    for card in cards.iter_mut() {
        let Some(m) = card["media"].as_object_mut() else { continue };
        let keys: Vec<String> = m.keys().cloned().collect();
        for k in keys {
            let e = m[&k].clone();
            if e["uri"].is_string() { continue; }
            let Some(b) = e["blobId"].as_str() else { continue };
            let mt = e["mediaType"].as_str().unwrap_or("image/jpeg").to_string();
            match c.download_from(&acc, b, "photo", &mt) {
                Ok(bytes) if bytes.len() > crate::vcard::MAX_IMAGE_BYTES => { m.remove(&k); }
                Ok(bytes) => { m[&k] = json!({"kind": e["kind"], "mediaType": mt, "uri": format!("data:{mt};base64,{}", base64::engine::general_purpose::STANDARD.encode(bytes))}); }
                Err(_) => { m.remove(&k); }
            }
        }
    }
}

fn export(u: &AppWindow, c: Option<Arc<Client>>, cards: Vec<Value>, name: &str) {
    if cards.is_empty() { crate::mail::show_toast(u, "No contacts to export", false); return; }
    let Some(path) = crate::automation::save_file(name, "vCard", &["vcf"]) else { return };
    let w = u.as_weak();
    std::thread::spawn(move || {
        let mut cards = cards;
        if let Some(c) = &c { inline_media(c, &mut cards); }
        let n = cards.len();
        let res = std::fs::write(&path, crate::vcard::generate(&cards));
        let _ = w.upgrade_in_event_loop(move |u| crate::mail::show_toast(&u, &match res {
            Ok(()) => format!("Exported {n} contact{} to {}", if n == 1 { "" } else { "s" }, path.display()),
            Err(e) => format!("Export failed: {e}"),
        }, false));
    });
}

/// Export the contacts the list shows (all, or the open address book, group or category).
pub fn export_all(u: &AppWindow, c: Option<Arc<Client>>) {
    let mut cards = crate::contacts::shown_cards();
    if crate::contacts::current_filter().is_empty() { cards.extend(crate::contacts::groups()); }
    export(u, c, cards, "contacts.vcf");
}

// ---------------- directory (RFC 9670 principals) ----------------

const PRINCIPALS: &str = "urn:ietf:params:jmap:principals";
const PRINCIPAL_PROPS: [&str; 7] = ["id", "type", "name", "description", "email", "timeZone", "accounts"];

pub fn directory_enabled() -> bool {
    S.with(|s| s.borrow().has_principals) && crate::settings::Settings::load().bool("showContactsDirectory", true)
}

pub fn set_has_principals(on: bool) { S.with(|s| s.borrow_mut().has_principals = on); }

/// Principal/query with its Principal/get; when the server refuses the query (Stalwart 1.0
/// with directory queries off), the principals Principal/get without ids still returns.
pub fn fetch_directory(c: &Client) -> Result<(Vec<Value>, bool), String> {
    let using = ["urn:ietf:params:jmap:core", PRINCIPALS];
    let acc = c.account.clone();
    let mut all = vec![];
    let page = c.max_get().min(500);
    let mut pos = 0;
    loop {
        let r = c.request_using(&using, json!([["Principal/query", {"accountId": acc, "position": pos, "limit": page}, "0"],
            ["Principal/get", {"accountId": acc, "#ids": {"resultOf": "0", "name": "Principal/query", "path": "/ids"}, "properties": PRINCIPAL_PROPS}, "1"]]))?;
        if r[0][0] == "error" {
            if r[0][1]["type"] == "forbidden" {
                let g = c.request_using(&using, json!([["Principal/get", {"accountId": acc, "ids": null, "properties": PRINCIPAL_PROPS}, "0"]]))?;
                return Ok((g[0][1]["list"].as_array().cloned().unwrap_or_default(), true));
            }
            return Err(crate::jmap::method_error(&r[0][1]));
        }
        let ids = r[0][1]["ids"].as_array().map(|a| a.len()).unwrap_or(0);
        all.extend(r.get(1).and_then(|x| x[1]["list"].as_array().cloned()).unwrap_or_default());
        if ids < page || pos > 20000 { break; }
        pos += ids;
    }
    Ok((all, false))
}

pub fn load_directory(u: &AppWindow, c: Arc<Client>) {
    let g = u.global::<CtX>();
    if S.with(|s| s.borrow().dir_loaded) { return; }
    g.set_dir_status("Loading the directory…".into());
    let w = u.as_weak();
    std::thread::spawn(move || {
        let res = fetch_directory(&c);
        let _ = w.upgrade_in_event_loop(move |u| {
            let g = u.global::<CtX>();
            match res {
                Ok((mut list, restricted)) => {
                    list.retain(|p| p["type"] != "resource" || p["email"].is_string());
                    list.sort_by_key(|p| p["name"].as_str().unwrap_or("").to_lowercase());
                    g.set_dir_status(if list.is_empty() { "The directory is empty, or your server does not list it.".into() }
                        else if restricted { "Your server only shows the accounts you already know (directory queries are off).".into() } else { "".into() });
                    S.with(|s| { let mut s = s.borrow_mut(); s.directory = list; s.dir_loaded = true; });
                }
                Err(e) => g.set_dir_status(format!("The directory could not be loaded: {e}").into()),
            }
            crate::contacts::render(&u);
        });
    });
}

pub fn directory() -> Vec<Value> { S.with(|s| s.borrow().directory.clone()) }

/// A principal as a card for the detail view and "Add to contacts".
pub fn principal_card(p: &Value) -> Value {
    let name = p["name"].as_str().unwrap_or("").to_string();
    let mut c = json!({"name": {"full": name}, "kind": match p["type"].as_str() { Some("group") => "group", Some("location") => "location", Some("resource") => "device", _ => "individual" }});
    if let Some(e) = p["email"].as_str().filter(|e| !e.is_empty()) { c["emails"] = json!({"e0": {"@type": "EmailAddress", "address": e, "contexts": {"work": true}}}); }
    if let Some(d) = p["description"].as_str().filter(|d| !d.is_empty()) { c["notes"] = json!({"n0": {"@type": "Note", "note": d}}); }
    if let Some(tz) = p["timeZone"].as_str() { c["addresses"] = json!({"a0": {"@type": "Address", "timeZone": tz, "full": format!("Time zone: {tz}")}}); }
    c
}

pub fn show_principal(u: &AppWindow, id: &str) {
    let Some(p) = directory().into_iter().find(|p| p["id"] == id) else { return };
    let card = principal_card(&p);
    let g = u.global::<CtX>();
    g.set_lines(to_model(detail_lines(&card, chrono::Local::now().date_naive())));
    g.set_subtitle(match p["type"].as_str() { Some("group") => "Group on this server", Some("location") => "Location", Some("resource") => "Resource", _ => "Account on this server" }.into());
    g.set_chips(to_model(vec![SharedString::from("Directory")]));
    g.set_has_photo(false);
    g.set_directory_entry(true);
}

fn add_from_directory(u: &AppWindow, c: Arc<Client>) {
    let id = u.get_ct_id().to_string();
    let Some(p) = directory().into_iter().find(|p| format!("dir:{}", p["id"].as_str().unwrap_or("")) == id) else { return };
    let mut card = principal_card(&p);
    if let Some(o) = card.as_object_mut() { o.remove("addresses"); if o.get("kind") == Some(&json!("individual")) { o.remove("kind"); } }
    let book = crate::contacts::default_book_for(&c);
    let w = u.as_weak();
    std::thread::spawn(move || {
        let acc = c.account_for(CAP);
        let res = (|| {
            let b = book.ok_or("No address book")?;
            card["@type"] = json!("Card"); card["version"] = json!("1.0"); card["uid"] = json!(new_uid()); card["addressBookIds"] = json!({ b: true });
            let r = c.call(json!([["ContactCard/set", {"accountId": acc, "create": {"c": card}}, "s"]]))?;
            if let Some(e) = r[0][1]["notCreated"]["c"].as_object() { return Err(crate::jmap::set_error(&Value::Object(e.clone()))); }
            Ok(())
        })();
        let _ = w.upgrade_in_event_loop(move |u| {
            crate::mail::show_toast(&u, &match res { Ok(()) => "Added to your contacts".to_string(), Err(e) => format!("Not added: {e}") }, false);
            crate::contacts::reload_then(&u, c, |_| {});
        });
    });
}

// ---------------- wiring ----------------

fn act(u: &AppWindow, c: Option<Arc<Client>>, kind: &str, value: &str) {
    match kind {
        "mail" => if let Some(c) = c { crate::compose::open(u, c, crate::compose::ComposeKind::New, None); u.set_c_to(value.into()); },
        "url" => { if value.starts_with("http://") || value.starts_with("https://") { crate::oauth::open_browser(value); } }
        "tel" => { u.global::<CtX>().set_status("".into()); crate::oauth::open_browser(&format!("tel:{}", value.replace(' ', ""))); }
        _ => {}
    }
}

pub fn wire(ui: &AppWindow, st: ClientSlot) {
    let g = ui.global::<CtX>();
    let client = move |st: &ClientSlot| st.lock().unwrap().clone();
    let (w, s) = (ui.as_weak(), st.clone());
    g.on_act(move |k, v| act(&w.unwrap(), client(&s), &k, &v));
    let w = ui.as_weak();
    g.on_add_field(move |section| {
        let u = w.unwrap();
        S.with(|s| {
            let mut s = s.borrow_mut();
            let kind = match section.as_str() { "phone" => 1, "email" | "address" | "url" | "im" => 0, _ => 0 };
            // insert after the section's last entry so the order stays as shown
            let at = s.ed.fields.iter().rposition(|f| f.section == section.as_str()).map(|i| i + 1).unwrap_or(s.ed.fields.len());
            s.ed.fields.insert(at, Field { section: section.to_string(), kind, ..Default::default() });
        });
        render_fields(&u);
    });
    let w = ui.as_weak();
    g.on_remove_field(move |i| { S.with(|s| { let mut s = s.borrow_mut(); if (i as usize) < s.ed.fields.len() { s.ed.fields.remove(i as usize); } }); render_fields(&w.unwrap()); });
    g.on_set_field(move |i, part, v| S.with(|s| {
        if let Some(f) = s.borrow_mut().ed.fields.get_mut(i as usize) {
            let v = v.to_string();
            match part.as_str() { "a" => f.a = v, "b" => f.b = v, "c" => f.c = v, "d" => f.d = v, "e" => f.e = v, _ => {} }
        }
    }));
    let w = ui.as_weak();
    g.on_set_kind(move |i, k, k2| { S.with(|s| if let Some(f) = s.borrow_mut().ed.fields.get_mut(i as usize) { f.kind = k; f.kind2 = k2; }); render_fields(&w.unwrap()); });
    let w = ui.as_weak();
    g.on_pick_photo(move || {
        let u = w.unwrap();
        let Some(files) = crate::automation::pick_files() else { return };
        // a file above the limit is refused on its size, before it is read
        let Some(bytes) = files.first().and_then(|f| read_capped(f, crate::vcard::MAX_IMAGE_BYTES).transpose()) else { return };
        let bytes = match bytes { Ok(b) => b, Err(e) => { u.global::<CtX>().set_status(e.into()); return } };
        match shrink_photo(&bytes) {
            Ok((mime, jpeg)) => {
                if let Some(buf) = decode_image(&jpeg) { u.global::<CtX>().set_edit_photo(Image::from_rgba8(buf)); u.global::<CtX>().set_edit_has_photo(true); }
                S.with(|s| s.borrow_mut().ed.photo = PhotoEdit::New(mime, jpeg));
            }
            Err(e) => u.global::<CtX>().set_status(e.into()),
        }
    });
    let w = ui.as_weak();
    g.on_clear_photo(move || { S.with(|s| s.borrow_mut().ed.photo = PhotoEdit::Removed); w.unwrap().global::<CtX>().set_edit_has_photo(false); });
    let (w, s) = (ui.as_weak(), st.clone());
    g.on_save(move || { if let Some(c) = client(&s) { save(&w.unwrap(), c); } });
    let w = ui.as_weak();
    g.on_cancel(move || { let u = w.unwrap(); u.set_ct_editing(false); let id = u.get_ct_id(); u.invoke_ct_select(id); });
    let (w, s) = (ui.as_weak(), st.clone());
    g.on_merge(move |grp| { if let Some(c) = client(&s) { merge(&w.unwrap(), c, grp); } });
    let w = ui.as_weak();
    g.on_open(move |id| w.unwrap().invoke_ct_select(id));
    let w = ui.as_weak();
    g.on_import_file(move || start_import(&w.unwrap()));
    let (w, s) = (ui.as_weak(), st.clone());
    g.on_import_run(move || { if let Some(c) = client(&s) { run_import(&w.unwrap(), c); } });
    let (w, s) = (ui.as_weak(), st.clone());
    g.on_export_all(move || export_all(&w.unwrap(), client(&s)));
    let (w, s) = (ui.as_weak(), st.clone());
    g.on_export_one(move || {
        let u = w.unwrap();
        let id = u.get_ct_id().to_string();
        let Some(card) = crate::contacts::cards().into_iter().chain(crate::contacts::groups()).find(|c| c["id"] == id.as_str()) else { return };
        let name = export_file_name(&crate::contacts::display_name(&card));
        export(&u, client(&s), vec![card], &name);
    });
    let (w, s) = (ui.as_weak(), st.clone());
    g.on_add_from_directory(move || { if let Some(c) = client(&s) { add_from_directory(&w.unwrap(), c); } });
}

/// Settings > Contacts buttons.
pub fn settings_button(u: &AppWindow, c: Option<Arc<Client>>, key: &str) -> bool {
    match key {
        "contacts.import" => { start_import(u); true }
        "contacts.export" => { export_all(u, c); true }
        _ => false,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn card() -> Value {
        json!({"id": "c1", "name": {"components": [{"kind": "given", "value": "Ada"}, {"kind": "surname", "value": "Lovelace"}, {"kind": "surname2", "value": "Byron"}], "full": "Ada Lovelace"},
            "emails": {"e0": {"address": "ada@x.org", "contexts": {"work": true}, "pref": 1}, "e1": {"address": "ada@home.org", "label": "Old", "x-custom": 7}},
            "phones": {"p0": {"number": "+44 1", "features": {"mobile": true}, "contexts": {"private": true}}},
            "addresses": {"a0": {"components": [{"kind": "name", "value": "1 St James"}, {"kind": "locality", "value": "London"}], "countryCode": "GB"}},
            "anniversaries": {"a0": {"kind": "birth", "date": {"year": 1815, "month": 12, "day": 10}}},
            "relatedTo": {"Charles Babbage": {"relation": {"colleague": true}}},
            "titles": {"t0": {"name": "Countess", "kind": "title"}}, "organizations": {"o0": {"name": "Analytical Engines", "units": [{"name": "R&D"}]}},
            "notes": {"n0": {"note": "First programmer"}}, "nicknames": {"k0": {"name": "Enchantress"}}})
    }

    #[test]
    fn fields_round_trip_keeps_unknown_properties() {
        let c = card();
        let fields = fields_of(&c);
        assert_eq!(fields.iter().filter(|f| f.section == "email").count(), 2);
        let e1 = fields.iter().find(|f| f.key.as_deref() == Some("e1")).unwrap();
        assert_eq!(e1.label.as_deref(), Some("Old"));
        assert_eq!(e1.kind, 3);
        assert_eq!(fields.iter().find(|f| f.section == "phone").unwrap().kind, 1);
        assert_eq!(fields.iter().find(|f| f.section == "phone").unwrap().kind2, 1);
        assert_eq!(fields.iter().find(|f| f.section == "date").unwrap().a, "1815-12-10");
        assert_eq!(fields.iter().find(|f| f.section == "related").unwrap().kind, 7);
        let form = Form { given: "Ada".into(), surname: "Lovelace".into(), org: "Analytical Engines".into(), dept: "R&D".into(), job_title: "Countess".into(), notes: "First programmer".into(), nickname: "Enchantress".into(), ..Default::default() };
        let out = build_card(Some(&c), &form, &fields).unwrap();
        assert_eq!(out["emails"]["e0"]["pref"], 1);
        assert_eq!(out["emails"]["e1"]["x-custom"], 7);
        assert_eq!(out["emails"]["e1"]["label"], "Old");
        assert_eq!(out["emails"]["e0"]["contexts"]["work"], true);
        assert_eq!(out["phones"]["p0"]["features"]["mobile"], true);
        assert_eq!(out["addresses"]["a0"]["countryCode"], "GB");
        assert_eq!(out["anniversaries"]["a0"]["date"]["year"], 1815);
        assert_eq!(out["relatedTo"]["Charles Babbage"]["relation"]["colleague"], true);
        // surname2 survives the name rebuild
        assert!(out["name"]["components"].as_array().unwrap().iter().any(|x| x["kind"] == "surname2"));
        assert_eq!(out["name"]["full"], "Ada Lovelace");
        assert_eq!(out["organizations"]["o0"]["units"][0]["name"], "R&D");
    }

    #[test]
    fn edits_add_remove_and_validate() {
        let c = card();
        let mut fields = fields_of(&c);
        fields.retain(|f| f.key.as_deref() != Some("e1"));
        // work email becomes personal, phone becomes fax at work
        for f in fields.iter_mut() {
            if f.key.as_deref() == Some("e0") { f.kind = 0; }
            if f.section == "phone" { f.kind = 2; f.kind2 = 2; }
        }
        fields.push(Field { section: "email".into(), a: "new@x.org".into(), kind: 1, ..Default::default() });
        fields.push(Field { section: "date".into(), a: "--06-01".into(), kind: 1, ..Default::default() });
        fields.push(Field { section: "url".into(), a: "example.org".into(), ..Default::default() });
        let form = Form { given: "Ada".into(), ..Default::default() };
        let out = build_card(Some(&c), &form, &fields).unwrap();
        let emails = out["emails"].as_object().unwrap();
        assert_eq!(emails.len(), 2);
        assert_eq!(out["emails"]["e0"]["contexts"]["private"], true);
        assert_eq!(out["emails"]["e2"]["address"], "new@x.org");
        assert_eq!(out["phones"]["p0"]["features"]["fax"], true);
        assert_eq!(out["phones"]["p0"]["contexts"]["work"], true);
        let wed = out["anniversaries"].as_object().unwrap().values().find(|a| a["kind"] == "wedding").unwrap();
        assert_eq!(wed["date"]["month"], 6);
        assert!(wed["date"].get("year").is_none());
        assert_eq!(out["links"]["l0"]["uri"], "https://example.org");
        // emptied single fields go away; titles removed
        assert!(out["titles"].is_null());
        assert!(out["notes"].is_null());
        // bad input is refused
        fields.push(Field { section: "email".into(), a: "not an address".into(), ..Default::default() });
        assert!(build_card(Some(&c), &form, &fields).is_err());
        assert!(parse_date_text("31/12").is_err());
    }

    #[test]
    fn pretty_dates_and_upcoming() {
        let today = chrono::NaiveDate::from_ymd_opt(2026, 11, 16).unwrap();
        let (t, n) = pretty_date(&json!({"year": 1815, "month": 12, "day": 10}), today);
        assert_eq!(t, "10 December 1815");
        assert_eq!(n, Some(24));
        let (t, n) = pretty_date(&json!({"month": 11, "day": 20}), today);
        assert_eq!(t, "20 November");
        assert_eq!(n, Some(4));
        let (_, n) = pretty_date(&json!({"month": 1, "day": 2}), today);
        assert_eq!(n, Some(47));
        let lines = detail_lines(&card(), today);
        assert!(lines.iter().any(|l| l.section == "EMAIL" && l.label == "Work"));
        assert!(lines.iter().any(|l| l.section == "PHONE" && l.label == "Mobile · Personal"));
        assert!(lines.iter().any(|l| l.section == "ADDRESS" && l.value.contains("London")));
        assert!(lines.iter().any(|l| l.section == "RELATED PEOPLE" && l.label == "Colleague"));
    }

    #[test]
    fn import_batch_and_principals() {
        let cards = vec![json!({"id": "import-1", "name": {"full": "A"}}), json!({"name": {"full": "B"}, "uid": "u-b"})];
        let b = import_batch(&cards, &[0], true, "book1");
        assert_eq!(b.len(), 1);
        assert_eq!(b[0]["uid"], "u-b");
        assert_eq!(b[0]["addressBookIds"]["book1"], true);
        let b = import_batch(&cards, &[0], false, "book1");
        assert_eq!(b.len(), 2);
        assert!(b[0].get("id").is_none());
        assert!(b[0]["uid"].as_str().unwrap().starts_with("urn:uuid:"));
        assert_eq!(new_uid().len(), 45);
        let p = principal_card(&json!({"id": "x", "type": "group", "name": "Sales", "email": "sales@x.org", "description": "Team"}));
        assert_eq!(p["kind"], "group");
        assert_eq!(p["emails"]["e0"]["address"], "sales@x.org");
    }

    fn units(v: &Value) -> Vec<String> { v.as_array().map(|a| a.iter().filter_map(|u| u["name"].as_str().map(String::from)).collect()).unwrap_or_default() }

    fn two_unit_card() -> Value {
        json!({"id": "c2", "name": {"full": "Bea Lee"}, "organizations": {
            "o0": {"@type": "Organization", "name": "Acme", "sortAs": "acme", "units": [{"@type": "OrgUnit", "name": "R&D", "sortAs": "rd"}, {"@type": "OrgUnit", "name": "Legal"}, {"@type": "OrgUnit", "name": "Tax"}]},
            "o1": {"@type": "Organization", "name": "Other Ltd", "units": [{"@type": "OrgUnit", "name": "Sales"}]}}})
    }

    fn form_of(c: &Value) -> Form {
        let first = c["organizations"].as_object().and_then(|o| o.values().next()).cloned().unwrap_or(Value::Null);
        Form { given: "Bea".into(), surname: "Lee".into(), org: first["name"].as_str().unwrap_or("").into(), dept: first["units"][0]["name"].as_str().unwrap_or("").into(), ..Default::default() }
    }

    #[test]
    fn saving_keeps_the_other_organisation_units() {
        let c = two_unit_card();
        let fields = fields_of(&c);
        // untouched form: everything as it was
        let out = build_card(Some(&c), &form_of(&c), &fields).unwrap();
        assert_eq!(out["organizations"], c["organizations"]);
        // department edited: only the first unit changes (its own unknown properties stay)
        let mut f = form_of(&c);
        f.dept = "Research".into();
        let out = build_card(Some(&c), &f, &fields).unwrap();
        assert_eq!(units(&out["organizations"]["o0"]["units"]), ["Research", "Legal", "Tax"]);
        assert_eq!(out["organizations"]["o0"]["units"][0]["sortAs"], "rd");
        assert_eq!(out["organizations"]["o0"]["sortAs"], "acme");
        assert_eq!(out["organizations"]["o1"], c["organizations"]["o1"]);
        // department emptied: only the first unit goes
        f.dept = "".into();
        let out = build_card(Some(&c), &f, &fields).unwrap();
        assert_eq!(units(&out["organizations"]["o0"]["units"]), ["Legal", "Tax"]);
        assert_eq!(out["organizations"]["o0"]["name"], "Acme");
        assert_eq!(units(&out["organizations"]["o1"]["units"]), ["Sales"]);
        // organisation renamed, department untouched
        let mut f = form_of(&c);
        f.org = "Acme Corp".into();
        let out = build_card(Some(&c), &f, &fields).unwrap();
        assert_eq!(out["organizations"]["o0"]["name"], "Acme Corp");
        assert_eq!(units(&out["organizations"]["o0"]["units"]), ["R&D", "Legal", "Tax"]);
    }

    #[test]
    fn organisation_edits_on_small_cards() {
        let one = |v: Value| v.as_object().cloned().unwrap();
        // one unit, emptied: no units left, name kept
        let o = edit_organizations(one(json!({"o0": {"@type": "Organization", "name": "Acme", "units": [{"name": "R&D"}]}})), "Acme", "");
        assert_eq!(o["o0"]["name"], "Acme");
        assert!(o["o0"].get("units").is_none());
        // a new department on an organisation with none
        let o = edit_organizations(one(json!({"o0": {"name": "Acme"}})), "Acme", "Ops");
        assert_eq!(units(&o["o0"]["units"]), ["Ops"]);
        // no organisation yet
        let o = edit_organizations(Map::new(), "New Co", "HR");
        assert_eq!((o["o0"]["name"].as_str(), units(&o["o0"]["units"])), (Some("New Co"), vec!["HR".to_string()]));
        assert!(edit_organizations(Map::new(), "", "").is_null());
        // everything emptied: the first organisation goes, the second stays
        let o = edit_organizations(one(json!({"o0": {"name": "A", "units": [{"name": "B"}]}, "o1": {"name": "C"}})), "", "");
        assert!(o.get("o0").is_none() && o["o1"]["name"] == "C");
        // a first unit with no name is not the one the form shows: an empty field leaves it alone
        let o = edit_organizations(one(json!({"o0": {"name": "A", "units": [{"@type": "OrgUnit", "sortAs": "x"}, {"name": "Z"}]}})), "A", "");
        assert_eq!(o["o0"]["units"].as_array().unwrap().len(), 2);
        // a department on a card whose only organisation has a nameless first unit fills that unit
        let o = edit_organizations(one(json!({"o0": {"name": "A", "units": [{"sortAs": "x"}]}})), "A", "Ops");
        assert_eq!(o["o0"]["units"][0]["name"], "Ops");
        assert_eq!(o["o0"]["units"][0]["sortAs"], "x");
        // only the department filled in, no organisation name
        let o = edit_organizations(Map::new(), "", "HR");
        assert!(o["o0"].get("name").is_none() || o["o0"]["name"] == "");
    }

    #[test]
    fn a_29_february_birthday_counts_down_to_1_march_in_a_common_year() {
        use chrono::NaiveDate;
        let d = |y, m, dd| NaiveDate::from_ymd_opt(y, m, dd).unwrap();
        assert_eq!(next_occurrence(2, 29, d(2027, 2, 10)), Some(d(2027, 3, 1)));
        assert_eq!(next_occurrence(2, 29, d(2027, 3, 1)), Some(d(2027, 3, 1)));
        assert_eq!(next_occurrence(2, 29, d(2027, 3, 2)), Some(d(2028, 2, 29)));
        assert_eq!(next_occurrence(2, 29, d(2028, 2, 29)), Some(d(2028, 2, 29)));
        assert_eq!(next_occurrence(2, 29, d(2028, 3, 1)), Some(d(2029, 3, 1)));
        assert_eq!(next_occurrence(2, 29, d(2096, 3, 1)), Some(d(2097, 3, 1)));
        assert_eq!(next_occurrence(12, 31, d(2026, 12, 31)), Some(d(2026, 12, 31)));
        assert_eq!(next_occurrence(2, 30, d(2026, 1, 1)), None);
        assert_eq!(next_occurrence(4, 31, d(2026, 1, 1)), None);
        // through the detail text: never negative, whatever the day
        let (_, n) = pretty_date(&json!({"month": 2, "day": 29}), d(2026, 11, 16));
        assert_eq!(n, Some(105));
        let (t, n) = pretty_date(&json!({"year": 2000, "month": 2, "day": 29}), d(2027, 2, 27));
        assert_eq!((t.as_str(), n), ("29 February 2000", Some(2)));
        let mut day = d(2026, 1, 1);
        while day < d(2034, 1, 1) {
            let (_, n) = pretty_date(&json!({"month": 2, "day": 29}), day);
            assert!(n.is_some_and(|n| (0..=4 * 366).contains(&n)), "{day}: {n:?}");
            day = day.succ_opt().unwrap();
        }
    }

    #[test]
    fn typed_dates_must_be_real_days() {
        assert_eq!(parse_date_text("--02-29").unwrap()["day"], 29);
        assert_eq!(parse_date_text("2024-02-29").unwrap()["year"], 2024);
        assert_eq!(parse_date_text("2000-02-29").unwrap()["day"], 29);
        for bad in ["2023-02-29", "1900-02-29", "--02-30", "--04-31", "2024-06-31", "2023-11-31"] { assert!(parse_date_text(bad).is_err(), "{bad}"); }
        assert!(parse_date_text("2023-02").is_ok() && parse_date_text("1985").is_ok());
        // a stored date the user did not touch is not revalidated (the card can still be saved)
        let c = json!({"id": "c3", "name": {"full": "Cy Doe"}, "anniversaries": {"a0": {"kind": "birth", "date": "2023-02-29"}}});
        let out = build_card(Some(&c), &Form { given: "Cy".into(), surname: "Doe".into(), ..Default::default() }, &fields_of(&c)).unwrap();
        assert_eq!(out["anniversaries"]["a0"]["date"], "2023-02-29");
        // changed to a bad one: refused
        let mut fields = fields_of(&c);
        fields[0].a = "2023-02-30".into();
        assert!(build_card(Some(&c), &Form { given: "Cy".into(), ..Default::default() }, &fields).is_err());
    }

    fn png_claiming(w: u32, h: u32) -> Vec<u8> {
        // a real 1x1 PNG with the size in its IHDR changed (and the chunk's CRC redone): what a bomb is
        let mut png = std::io::Cursor::new(vec![]);
        image::DynamicImage::ImageRgba8(image::RgbaImage::new(1, 1)).write_to(&mut png, image::ImageFormat::Png).unwrap();
        let mut b = png.into_inner();
        b[16..20].copy_from_slice(&w.to_be_bytes());
        b[20..24].copy_from_slice(&h.to_be_bytes());
        let mut crc = 0xffff_ffffu32;
        for &x in &b[12..29] { crc ^= u32::from(x); for _ in 0..8 { crc = if crc & 1 != 0 { (crc >> 1) ^ 0xedb8_8320 } else { crc >> 1 }; } }
        b[29..33].copy_from_slice(&(!crc).to_be_bytes());
        b
    }

    #[test]
    fn pictures_above_the_limits_are_refused_before_decoding() {
        // 100 000 x 100 000: a few dozen bytes that would be 40 GB of pixels
        let bomb = png_claiming(100_000, 100_000);
        assert!(bomb.len() < 200);
        assert!(check_image(&bomb).unwrap_err().contains("too many pixels"));
        assert!(shrink_photo(&bomb).is_err());
        assert!(decode_image(&bomb).is_none());
        // 40 million pixels exactly passes the header check, one more does not
        assert!(check_image(&png_claiming(8000, 5000)).is_ok());
        assert!(check_image(&png_claiming(8000, 5001)).is_err());
        assert!(check_image(&png_claiming(u32::MAX, u32::MAX)).is_err());
        // bytes above 8 MiB are refused whatever they are
        assert!(check_image(&vec![0u8; crate::vcard::MAX_IMAGE_BYTES + 1]).unwrap_err().contains("8 MiB"));
        assert!(check_image(b"nope").is_err());
        // a normal picture passes and shrinks
        let img = image::RgbaImage::from_pixel(64, 48, image::Rgba([1, 2, 3, 255]));
        let mut png = std::io::Cursor::new(vec![]);
        image::DynamicImage::ImageRgba8(img).write_to(&mut png, image::ImageFormat::Png).unwrap();
        assert!(check_image(png.get_ref()).is_ok());
        assert!(decode_image(png.get_ref()).is_some());
    }

    #[test]
    fn a_data_uri_photo_above_8_mib_is_not_decoded() {
        let big = format!("data:image/jpeg;base64,{}", "A".repeat(crate::vcard::MAX_IMAGE_BYTES / 3 * 4 + 16));
        let c = json!({"media": {"m0": {"kind": "photo", "uri": big}}});
        assert!(photo_bytes(None, &c).is_none());
        let ok = json!({"media": {"m0": {"kind": "photo", "uri": "data:image/png;base64,iVBORw0KGgo="}}});
        assert_eq!(photo_bytes(None, &ok).unwrap().len(), 8);
        // not base64: refused
        assert!(photo_bytes(None, &json!({"media": {"m0": {"kind": "photo", "uri": "data:image/png;base64,@@@@"}}})).is_none());
    }

    #[test]
    fn files_above_a_cap_are_not_read() {
        let dir = std::env::temp_dir().join(format!("barbican-contacts-test-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let f = dir.join("a.vcf");
        std::fs::write(&f, vec![b'x'; 100]).unwrap();
        assert_eq!(read_capped(&f, 100).unwrap().unwrap().len(), 100);
        assert!(read_capped(&f, 99).unwrap_err().contains("a.vcf"));
        assert_eq!(read_capped(&dir.join("missing.vcf"), 100), Ok(None));
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn export_file_names_are_safe() {
        assert_eq!(export_file_name("Ada Lovelace"), "Ada Lovelace.vcf");
        assert_eq!(export_file_name("Jean-Luc Picard"), "Jean-Luc Picard.vcf");
        assert_eq!(export_file_name("Ünal Öztürk"), "Ünal Öztürk.vcf");
        // path parts, wildcards, streams and control characters become underscores
        assert_eq!(export_file_name("..\\..\\evil/name?*:\"<>|"), "evil_name.vcf");
        assert_eq!(export_file_name("a\r\nb\u{0}c"), "a__b_c.vcf");
        assert_eq!(export_file_name("name.vcf.exe"), "name_vcf_exe.vcf");
        // nothing left, dots and spaces at the ends, device names
        for n in ["", "   ", "...", "///", "_"] { assert_eq!(export_file_name(n), "contact.vcf", "{n:?}"); }
        assert_eq!(export_file_name("Ann. "), "Ann.vcf");
        for n in ["CON", "nul", "Aux", "PRN", "com1", "LPT9"] { assert_eq!(export_file_name(n), format!("_{n}.vcf")); }
        assert_eq!(export_file_name("Con Smith"), "Con Smith.vcf");
        assert_eq!(export_file_name("COM10"), "COM10.vcf");
        // length
        assert_eq!(export_file_name(&"x".repeat(500)).len(), 80 + 4);
        for n in ["a/b", "x:y", "q\\r", "tab\tname"] { let r = export_file_name(n); assert!(!r.contains(['/', '\\', ':', '\t', '*', '?']), "{r}"); }
    }

    #[test]
    fn shrinks_photo_to_jpeg() {
        let img = image::RgbaImage::from_pixel(900, 600, image::Rgba([200, 30, 60, 255]));
        let mut png = std::io::Cursor::new(vec![]);
        image::DynamicImage::ImageRgba8(img).write_to(&mut png, image::ImageFormat::Png).unwrap();
        let (mime, jpeg) = shrink_photo(&png.into_inner()).unwrap();
        assert_eq!(mime, "image/jpeg");
        let back = image::load_from_memory(&jpeg).unwrap();
        assert_eq!((back.width(), back.height()), (512, 341));
        assert!(shrink_photo(b"nope").is_err());
    }
}
