//! vCard (RFC 6350, plus 3.0 and 2.1 files) to and from JSContact cards (RFC 9553), as
//! Bulwark's lib/vcard.ts does it: import keeps types, labels, preference, dates as
//! PartialDate, photos as data: URIs, Apple grouped labels and the common X- properties;
//! export writes vCard 4.0 with folded CRLF lines.
use serde_json::{json, Map, Value};

/// Limits for hostile input (D-M5-03, D-M5-A03). A .vcf from a stranger is untrusted: none of
/// these can make the reader allocate without bound. Bulwark's lib/vcard.ts has none.
/// Longest file the reader takes, in characters.
pub const MAX_FILE_CHARS: usize = 16_000_000;
/// Cards read from one file; the rest are left out.
pub const MAX_CARDS: usize = 10_000;
/// Longest unfolded property line, in characters (a photo line has its own limit).
pub const MAX_LINE_CHARS: usize = 1_000_000;
/// Property lines kept per card.
pub const MAX_PROPS_PER_CARD: usize = 5_000;
/// Entries kept per kind (emails, phones, ...) on one card.
pub const MAX_ENTRIES: usize = 500;
/// A photo or a picked image above this many bytes (decoded) is refused before decoding.
pub const MAX_IMAGE_BYTES: usize = 8 * 1024 * 1024;
/// A picture above this many pixels is refused from its header, before any pixel is allocated.
pub const MAX_IMAGE_PIXELS: u64 = 40_000_000;
/// A PHOTO / LOGO line may be as long as its base64 text for `MAX_IMAGE_BYTES` plus its header.
const MAX_PHOTO_LINE_CHARS: usize = MAX_IMAGE_BYTES / 3 * 4 + 8192;

/// What the reader left out of a file, for the import summary.
#[derive(Default, Debug, Clone, PartialEq, Eq)]
pub struct ParseReport {
    /// The file is above `MAX_FILE_CHARS`: nothing was read.
    pub file_too_large: bool,
    /// More than `MAX_CARDS` cards: the first ones were kept.
    pub cards_truncated: bool,
    /// Property lines above the line limit, dropped.
    pub lines_dropped: usize,
    /// Photos or logos dropped (too big, or not base64); their cards are kept.
    pub photos_dropped: usize,
}

impl ParseReport {
    /// Text for the import summary; empty when nothing was left out.
    pub fn note(&self) -> String {
        let mut v = vec![];
        if self.file_too_large { v.push(format!("The file is larger than {} million characters and was not read.", MAX_FILE_CHARS / 1_000_000)); }
        if self.cards_truncated { v.push(format!("Only the first {MAX_CARDS} contacts were read.")); }
        if self.photos_dropped > 0 { v.push(format!("{} photo{} too large or not valid and left out.", self.photos_dropped, if self.photos_dropped == 1 { " was" } else { "s were" })); }
        if self.lines_dropped > 0 { v.push(format!("{} line{} too long and skipped.", self.lines_dropped, if self.lines_dropped == 1 { " was" } else { "s were" })); }
        v.join(" ")
    }
}

/// Physical lines split at CRLF, LF or a lone CR, without collecting them.
struct RawLines<'a> { text: &'a str, pos: usize }

impl<'a> Iterator for RawLines<'a> {
    type Item = &'a str;
    fn next(&mut self) -> Option<&'a str> {
        let b = self.text.as_bytes();
        if self.pos >= b.len() { return None; }
        let start = self.pos;
        let mut i = start;
        while i < b.len() && b[i] != b'\n' && b[i] != b'\r' { i += 1; }
        let line = &self.text[start..i];
        self.pos = if i < b.len() && b[i] == b'\r' && b.get(i + 1) == Some(&b'\n') { i + 2 } else { (i + 1).min(b.len()) };
        Some(line)
    }
}

/// Unfolded lines (RFC 6350 section 3.2: a line break followed by one space or tab goes), with
/// vCard 2.1 quoted-printable soft breaks joined, one at a time so a big file is never copied.
/// Blank lines are skipped.
struct Logical<'a> { raw: std::iter::Peekable<RawLines<'a>> }

impl<'a> Logical<'a> {
    fn new(text: &'a str) -> Self { Logical { raw: RawLines { text, pos: 0 }.peekable() } }

    fn unfolded(&mut self) -> Option<String> {
        let mut l = self.raw.next()?.to_string();
        while let Some(n) = self.raw.peek() {
            if n.starts_with(' ') || n.starts_with('\t') { l.push_str(&n[1..]); self.raw.next(); } else { break; }
        }
        Some(l)
    }
}

impl Iterator for Logical<'_> {
    type Item = String;
    fn next(&mut self) -> Option<String> {
        loop {
            let mut l = self.unfolded()?;
            if l.ends_with('=') && l.to_ascii_uppercase().contains(";ENCODING=QUOTED-PRINTABLE") {
                while l.ends_with('=') {
                    let Some(n) = self.unfolded() else { break };
                    l.pop();
                    l.push_str(&n);
                }
            }
            if !l.trim().is_empty() { return Some(l); }
        }
    }
}

/// Whether a property line is within the line limit (a PHOTO or LOGO line has its own).
fn line_allowed(line: &str) -> bool {
    if line.len() <= MAX_LINE_CHARS { return true; }
    let chars = line.chars().count();
    if chars <= MAX_LINE_CHARS { return true; }
    let head = line.split([';', ':']).next().unwrap_or("");
    let name = head.rsplit('.').next().unwrap_or(head);
    (name.eq_ignore_ascii_case("PHOTO") || name.eq_ignore_ascii_case("LOGO")) && chars <= MAX_PHOTO_LINE_CHARS
}

/// Whether base64 text is plausible and decodes to at most `MAX_IMAGE_BYTES`, judged from its
/// length alone (nothing is decoded here).
pub fn base64_ok(data: &str) -> bool {
    let body = data.trim_end_matches('=');
    !body.is_empty() && data.len() - body.len() <= 2
        && body.len() * 3 / 4 <= MAX_IMAGE_BYTES
        && body.bytes().all(|c| c.is_ascii_alphanumeric() || c == b'+' || c == b'/')
}

/// A media type that is safe to write into a data: URI or a parameter, or None.
fn safe_mime(m: &str) -> Option<&str> {
    let ok = !m.is_empty() && m.len() <= 100 && m.matches('/').count() == 1
        && m.bytes().all(|c| c.is_ascii_alphanumeric() || b"/+.-_".contains(&c));
    ok.then_some(m)
}

/// RFC 6868 caret decoding of a parameter value.
fn decode_param(s: &str) -> String {
    let mut out = String::new();
    let mut it = s.chars().peekable();
    while let Some(c) = it.next() {
        if c == '^' {
            match it.peek() {
                Some('n') => { out.push('\n'); it.next(); continue; }
                Some('^') => { out.push('^'); it.next(); continue; }
                Some('\'') => { out.push('"'); it.next(); continue; }
                _ => {}
            }
        }
        out.push(c);
    }
    out
}

fn split_quoted(s: &str, delim: char) -> Vec<String> {
    let mut out = vec![];
    let mut buf = String::new();
    let mut q = false;
    for c in s.chars() {
        if c == '"' { q = !q; buf.push(c); continue; }
        if c == delim && !q { out.push(std::mem::take(&mut buf)); continue; }
        buf.push(c);
    }
    out.push(buf);
    out
}

fn value_colon(line: &str) -> Option<usize> {
    let mut q = false;
    for (i, c) in line.char_indices() {
        if c == '"' { q = !q; continue; }
        if c == ':' && !q { return Some(i); }
    }
    None
}

fn split_group(key: &str) -> (String, String) {
    if let Some(dot) = key.find('.') {
        let before = &key[..dot];
        if !before.contains(';') && !before.is_empty() && before.chars().all(|c| c.is_ascii_alphanumeric() || c == '-') {
            return (before.to_lowercase(), key[dot + 1..].to_string());
        }
    }
    (String::new(), key.to_string())
}

fn apple_label(v: &str) -> String {
    v.strip_prefix("_$!<").and_then(|x| x.strip_suffix(">!$_")).unwrap_or(v).to_string()
}

fn encode_param(s: &str) -> String { s.replace('^', "^^").replace('"', "^'").replace('\n', "^n") }

fn strip_scheme(v: &str, scheme: &str) -> String {
    let p = format!("{scheme}:");
    if v.len() >= p.len() && v[..p.len()].eq_ignore_ascii_case(&p) { v[p.len()..].to_string() } else { v.to_string() }
}

/// Text value escapes (one pass; `\:` is not standard but Google writes it).
pub fn decode_value(raw: &str) -> String {
    let mut out = String::new();
    let mut it = raw.chars().peekable();
    while let Some(c) = it.next() {
        if c == '\\' {
            match it.peek().copied() {
                Some('n') | Some('N') => { out.push('\n'); it.next(); continue; }
                Some(x @ ('\\' | ',' | ';' | ':')) => { out.push(x); it.next(); continue; }
                _ => {}
            }
        }
        out.push(c);
    }
    out
}

pub fn encode_value(v: &str) -> String {
    v.replace('\\', "\\\\").replace(';', "\\;").replace(',', "\\,").replace('\n', "\\n")
}

fn decode_qp(input: &str) -> String {
    let b = input.as_bytes();
    let mut out = vec![];
    let mut i = 0;
    while i < b.len() {
        if b[i] == b'=' {
            if let (Some(h), Some(l)) = (b.get(i + 1).and_then(|c| (*c as char).to_digit(16)), b.get(i + 2).and_then(|c| (*c as char).to_digit(16))) {
                out.push((h * 16 + l) as u8);
                i += 3;
                continue;
            }
        }
        out.push(b[i]);
        i += 1;
    }
    String::from_utf8_lossy(&out).to_string()
}

fn parse_params(s: &str) -> Map<String, Value> {
    let mut p = Map::new();
    // Every TYPE value (repeated TYPE=, bare WORK/HOME/...) is collected here and joined once, so a line of
    // 200 000 repeats costs linear time; at most MAX_ENTRIES are kept.
    let mut types: Vec<String> = vec![];
    let add_type = |t: String, types: &mut Vec<String>| { if types.len() < MAX_ENTRIES { types.push(t); } };
    for part in split_quoted(s, ';') {
        if part.is_empty() { continue; }
        if let Some(eq) = part.find('=') {
            let name = part[..eq].to_uppercase();
            let mut raw = part[eq + 1..].to_string();
            if raw.len() >= 2 && raw.starts_with('"') && raw.ends_with('"') { raw = raw[1..raw.len() - 1].to_string(); }
            let val = decode_param(&raw);
            // repeated TYPE params add up (TYPE=WORK;TYPE=VOICE)
            if name == "TYPE" { add_type(val, &mut types); continue; }
            p.insert(name, json!(val));
        } else {
            let up = part.to_uppercase();
            if up == "QUOTED-PRINTABLE" || up == "BASE64" { p.insert("ENCODING".into(), json!(up)); }
            else if ["WORK", "HOME", "CELL", "FAX", "VOICE", "PREF", "PAGER", "VIDEO", "TEXT", "TEXTPHONE", "INTERNET"].contains(&up.as_str()) {
                add_type(up, &mut types);
            }
        }
    }
    if !types.is_empty() { p.insert("TYPE".into(), json!(types.join(","))); }
    p
}

fn pstr<'a>(p: &'a Map<String, Value>, k: &str) -> Option<&'a str> { p.get(k).and_then(|v| v.as_str()).filter(|s| !s.is_empty()) }

fn pref(p: &Map<String, Value>) -> Option<i64> {
    if let Some(n) = pstr(p, "PREF").and_then(|v| v.trim().parse().ok()) { return Some(n); }
    if pstr(p, "TYPE").is_some_and(|t| t.to_uppercase().split(',').any(|x| x.trim() == "PREF")) { return Some(1); }
    None
}

fn contexts(t: Option<&str>) -> Option<Value> {
    let t = t?.to_uppercase();
    let types: Vec<&str> = t.split(',').map(str::trim).collect();
    let mut m = Map::new();
    if types.contains(&"WORK") { m.insert("work".into(), json!(true)); }
    if types.contains(&"HOME") { m.insert("private".into(), json!(true)); }
    if m.is_empty() { None } else { Some(Value::Object(m)) }
}

const TEL_FEATURES: [(&str, &str); 9] = [("CELL", "mobile"), ("MOBILE", "mobile"), ("VOICE", "voice"), ("FAX", "fax"), ("PAGER", "pager"),
    ("VIDEO", "video"), ("TEXT", "text"), ("TEXTPHONE", "textphone"), ("MAIN-NUMBER", "main-number")];

fn phone_features(t: Option<&str>) -> Option<Value> {
    let t = t?.to_uppercase();
    let mut m = Map::new();
    for x in t.split(',') {
        if let Some((_, f)) = TEL_FEATURES.iter().find(|(k, _)| *k == x.trim()) { m.insert(f.to_string(), json!(true)); }
    }
    if m.is_empty() { None } else { Some(Value::Object(m)) }
}

fn feature_types(f: &Value) -> Vec<String> {
    let mut out: Vec<String> = vec![];
    if let Some(o) = f.as_object() {
        for (k, on) in o {
            if *on != true { continue; }
            let kl = k.to_lowercase();
            let t = match kl.as_str() { "mobile" | "cell" => Some("CELL".to_string()), _ => TEL_FEATURES.iter().find(|(_, v)| *v == kl).map(|(t, _)| t.to_string()) };
            let t = t.or_else(|| (kl.starts_with("x-") && kl[2..].chars().all(|c| c.is_ascii_alphanumeric() || c == '-')).then(|| k.to_uppercase()));
            if let Some(t) = t { if !out.contains(&t) { out.push(t); } }
        }
    }
    out
}

fn context_type(c: &Value) -> &'static str {
    if c["work"] == true { "WORK" } else if c["private"] == true { "HOME" } else { "" }
}

/// A vCard DATE / DATE-AND-OR-TIME as a JSContact PartialDate (time dropped).
pub fn parse_date(value: &str, params: &Map<String, Value>) -> Option<Value> {
    if pstr(params, "VALUE").is_some_and(|v| v.eq_ignore_ascii_case("text")) { return None; }
    let d = value.trim().split(['T', ' ']).next().unwrap_or("");
    let digits = |s: &str| !s.is_empty() && s.chars().all(|c| c.is_ascii_digit());
    let n = |s: &str| s.parse::<i64>().ok();
    let mk = |y: Option<i64>, m: Option<i64>, dd: Option<i64>| -> Option<Value> {
        if m.is_some_and(|m| !(1..=12).contains(&m)) || dd.is_some_and(|d| !(1..=31).contains(&d)) { return None; }
        let mut o = Map::new();
        o.insert("@type".into(), json!("PartialDate"));
        if let Some(y) = y { o.insert("year".into(), json!(y)); }
        if let Some(m) = m { o.insert("month".into(), json!(m)); }
        if let Some(dd) = dd { o.insert("day".into(), json!(dd)); }
        if o.len() > 1 { Some(Value::Object(o)) } else { None }
    };
    if let Some(r) = d.strip_prefix("---") { return if r.len() == 2 && digits(r) { mk(None, None, n(r)) } else { None }; }
    if let Some(r) = d.strip_prefix("--") {
        let r2 = r.replace('-', "");
        return match r2.len() { 4 if digits(&r2) => mk(None, n(&r2[..2]), n(&r2[2..])), 2 if digits(&r2) && !r.contains('-') => mk(None, n(&r2), None), _ => None };
    }
    let compact = d.replace('-', "");
    if compact.len() == 8 && digits(&compact) && (d.len() == 8 || d.len() == 10) { return mk(n(&compact[..4]), n(&compact[4..6]), n(&compact[6..])); }
    if d.len() == 7 && d.as_bytes()[4] == b'-' && digits(&d[..4]) && digits(&d[5..]) { return mk(n(&d[..4]), n(&d[5..]), None); }
    if d.len() == 4 && digits(d) { return mk(n(d), None, None); }
    None
}

/// A JSContact anniversary date (PartialDate, Timestamp or string) as a vCard 4.0 date.
pub fn date_to_vcard(d: &Value) -> String {
    if let Some(s) = d.as_str() { return s.to_string(); }
    if let Some(u) = d["utc"].as_str() { return u.split('T').next().unwrap_or("").replace('-', ""); }
    let (y, m, dd) = (d["year"].as_i64(), d["month"].as_i64(), d["day"].as_i64());
    match (y, m, dd) {
        (Some(y), Some(m), Some(dd)) => format!("{y:04}{m:02}{dd:02}"),
        (None, Some(m), Some(dd)) => format!("--{m:02}{dd:02}"),
        (Some(y), Some(m), None) => format!("{y:04}-{m:02}"),
        (Some(y), None, None) => format!("{y}"),
        _ => String::new(),
    }
}

const X_ONLINE: [(&str, &str); 23] = [
    ("X-AIM", "AIM"), ("X-ICQ", "ICQ"), ("X-JABBER", "Jabber"), ("X-MSN", "MSN"), ("X-MS-IMADDRESS", "Skype"), ("X-YAHOO", "Yahoo"),
    ("X-SKYPE", "Skype"), ("X-SKYPE-USERNAME", "Skype"), ("X-GADUGADU", "Gadu-Gadu"), ("X-GROUPWISE", "GroupWise"), ("X-GOOGLE-TALK", "Google Talk"),
    ("X-GOOGLETALK", "Google Talk"), ("X-TWITTER", "Twitter"), ("X-MASTODON", "Mastodon"), ("X-MATRIX", "Matrix"), ("X-TELEGRAM", "Telegram"),
    ("X-SIGNAL", "Signal"), ("X-DISCORD", "Discord"), ("X-QQ", "QQ"), ("X-WECHAT", "WeChat"), ("X-FACEBOOK", "Facebook"), ("X-INSTAGRAM", "Instagram"),
    ("X-LINKEDIN", "LinkedIn"),
];

const RELATION_LABELS: [(&str, &str); 22] = [
    ("spouse", "spouse"), ("wife", "spouse"), ("husband", "spouse"), ("partner", "sweetheart"), ("domestic partner", "sweetheart"), ("friend", "friend"),
    ("child", "child"), ("son", "child"), ("daughter", "child"), ("parent", "parent"), ("father", "parent"), ("mother", "parent"), ("brother", "sibling"),
    ("sister", "sibling"), ("sibling", "sibling"), ("relative", "kin"), ("assistant", "colleague"), ("manager", "colleague"), ("colleague", "colleague"),
    ("referred by", "contact"), ("kin", "kin"), ("sweetheart", "sweetheart"),
];

fn relation_from_label(l: &str) -> Option<&'static str> {
    let l = l.trim().to_lowercase();
    RELATION_LABELS.iter().find(|(k, _)| *k == l).map(|(_, v)| *v)
}

fn next_key(m: &Map<String, Value>, prefix: &str) -> String {
    let mut i = m.len();
    loop { let k = format!("{prefix}{i}"); if !m.contains_key(&k) { return k; } i += 1; }
}

fn push(card: &mut Map<String, Value>, map: &str, prefix: &str, v: Value) {
    let m = card.entry(map.to_string()).or_insert_with(|| json!({}));
    if let Some(o) = m.as_object_mut() {
        if o.len() >= MAX_ENTRIES { return; }
        let k = next_key(o, prefix);
        o.insert(k, v);
    }
}

/// Set `map[key]`, unless the map already has `MAX_ENTRIES` entries and the key is new.
fn put_keyed(card: &mut Map<String, Value>, map: &str, key: &str, v: Value) {
    let m = card.entry(map.to_string()).or_insert_with(|| json!({}));
    if let Some(o) = m.as_object_mut() {
        if o.len() >= MAX_ENTRIES && !o.contains_key(key) { return; }
        o.insert(key.to_string(), v);
    }
}

fn clean(v: Value) -> Value {
    match v {
        Value::Object(o) => Value::Object(o.into_iter().filter(|(_, x)| !x.is_null()).collect()),
        x => x,
    }
}

/// Every card in a .vcf file. Cards with no name, email or organisation are skipped (not
/// groups). Ids are left out; `uid` is kept when the file has one. Hostile input is bounded
/// (see the `MAX_*` limits); use `parse_report` to learn what was left out.
#[allow(dead_code)]
pub fn parse(vcf: &str) -> Vec<Value> { parse_report(vcf).0 }

/// `parse`, with what the limits left out. A BEGIN inside a card starts the card over (no
/// nesting); a card keeps at most `MAX_PROPS_PER_CARD` property lines.
pub fn parse_report(vcf: &str) -> (Vec<Value>, ParseReport) {
    let mut rep = ParseReport::default();
    let mut out = vec![];
    if vcf.len() > MAX_FILE_CHARS && vcf.chars().count() > MAX_FILE_CHARS { rep.file_too_large = true; return (out, rep); }
    let mut cur: Option<Vec<(String, String, String)>> = None;
    for line in Logical::new(vcf) {
        if !line_allowed(&line) { rep.lines_dropped += 1; continue; }
        let t = line.trim();
        if t.eq_ignore_ascii_case("BEGIN:VCARD") { cur = Some(vec![]); continue; }
        if t.eq_ignore_ascii_case("END:VCARD") {
            if let Some(props) = cur.take() {
                if let Some(c) = build(props, &mut rep) {
                    if out.len() >= MAX_CARDS { rep.cards_truncated = true; break; }
                    out.push(c);
                }
            }
            continue;
        }
        if let Some(props) = cur.as_mut() {
            if props.len() >= MAX_PROPS_PER_CARD { continue; }
            let Some(ci) = value_colon(t).filter(|c| *c >= 1) else { continue };
            let (group, key) = split_group(&t[..ci]);
            props.push((group, key, t[ci + 1..].to_string()));
        }
    }
    (out, rep)
}

fn build(props: Vec<(String, String, String)>, rep: &mut ParseReport) -> Option<Value> {
    // Apple grouped labels: item1.TEL + item1.X-ABLABEL
    let mut labels: std::collections::HashMap<String, String> = Default::default();
    for (g, k, v) in &props {
        if !g.is_empty() && split_quoted(k, ';')[0].eq_ignore_ascii_case("X-ABLABEL") { labels.insert(g.clone(), apple_label(&decode_value(v))); }
    }
    let mut card = Map::new();
    card.insert("@type".into(), json!("Card"));
    card.insert("version".into(), json!("1.0"));
    let (mut birth_place, mut death_place, mut maiden): (Option<String>, Option<String>, Option<String>) = (None, None, None);
    let mut name: Option<Map<String, Value>> = None;
    for (g, key, raw_val) in props {
        let mut segs = split_quoted(&key, ';');
        let prop = segs.remove(0).to_uppercase();
        let mut params = parse_params(&segs.join(";"));
        if let Some(l) = labels.get(&g) { if !params.contains_key("X-ABLABEL") { params.insert("X-ABLABEL".into(), json!(l)); } }
        let pf = pref(&params);
        let qp = pstr(&params, "ENCODING").is_some_and(|e| e.eq_ignore_ascii_case("QUOTED-PRINTABLE"));
        let decoded = if qp { decode_qp(&raw_val) } else { raw_val.clone() };
        let val = decode_value(&decoded);
        let label = pstr(&params, "X-ABLABEL").map(String::from);
        let ty = pstr(&params, "TYPE").map(String::from);
        let ctx = contexts(ty.as_deref());
        match prop.as_str() {
            "FN" => {
                let n = name.get_or_insert_with(Map::new);
                if !n.contains_key("components") {
                    let parts: Vec<&str> = val.split(' ').collect();
                    let mut comps = vec![];
                    if parts.len() >= 2 { comps.push(json!({"kind": "given", "value": parts[0]})); comps.push(json!({"kind": "surname", "value": parts[1..].join(" ")})); }
                    else if parts.len() == 1 && !parts[0].is_empty() { comps.push(json!({"kind": "given", "value": parts[0]})); }
                    n.insert("components".into(), json!(comps));
                    n.insert("isOrdered".into(), json!(true));
                    n.insert("_fromFn".into(), json!(true));
                }
                if !n.contains_key("full") { n.insert("full".into(), json!(val)); }
            }
            "N" => {
                let p: Vec<&str> = val.split(';').collect();
                let g = |i: usize| p.get(i).copied().unwrap_or("");
                let mut comps = vec![];
                for (i, kind) in [(3, "title"), (1, "given"), (2, "given2"), (0, "surname"), (4, "generation")] {
                    if !g(i).is_empty() { comps.push(json!({"kind": kind, "value": g(i)})); }
                }
                if !comps.is_empty() {
                    let n = name.get_or_insert_with(Map::new);
                    n.insert("components".into(), json!(comps));
                    n.insert("isOrdered".into(), json!(true));
                    n.remove("_fromFn");
                }
            }
            "EMAIL" => push(&mut card, "emails", "e", clean(json!({"@type": "EmailAddress", "address": strip_scheme(&val, "mailto"), "contexts": ctx, "label": label, "pref": pf}))),
            "TEL" => push(&mut card, "phones", "p", clean(json!({"@type": "Phone", "number": strip_scheme(&val, "tel"), "contexts": ctx, "features": phone_features(ty.as_deref()), "label": label, "pref": pf}))),
            "ORG" => {
                // the organisation name and at most MAX_ENTRIES units
                let parts: Vec<&str> = val.split(';').filter(|x| !x.is_empty()).take(MAX_ENTRIES + 1).collect();
                if let Some(first) = parts.first() {
                    let units: Vec<Value> = parts[1..].iter().map(|u| json!({"@type": "OrgUnit", "name": u})).collect();
                    let mut o = json!({"@type": "Organization", "name": first});
                    if !units.is_empty() { o["units"] = json!(units); }
                    push(&mut card, "organizations", "o", o);
                }
            }
            "ADR" => {
                let p: Vec<&str> = val.split(';').collect();
                let g = |i: usize| p.get(i).copied().filter(|x| !x.is_empty());
                let mut comps = vec![];
                for (i, kind) in [(2, "name"), (3, "locality"), (4, "region"), (5, "postcode"), (6, "country")] {
                    if let Some(v) = g(i) { comps.push(json!({"kind": kind, "value": v})); }
                }
                let a = clean(json!({"@type": "Address", "components": comps, "countryCode": pstr(&params, "CC"), "full": pstr(&params, "LABEL"),
                    "coordinates": pstr(&params, "GEO").map(|g| strip_scheme(g, "geo")), "timeZone": pstr(&params, "TZ"), "contexts": ctx, "label": label, "pref": pf}));
                push(&mut card, "addresses", "a", a);
            }
            "NOTE" => push(&mut card, "notes", "n", json!({"@type": "Note", "note": val})),
            "NICKNAME" => for nick in val.split(',').map(str::trim).filter(|x| !x.is_empty()) { push(&mut card, "nicknames", "k", json!({"@type": "Nickname", "name": nick})) },
            "UID" => { card.insert("uid".into(), json!(val)); }
            "KIND" | "X-ADDRESSBOOKSERVER-KIND" => {
                let k = val.to_lowercase();
                if ["group", "individual", "org", "location", "device", "application"].contains(&k.as_str()) { card.insert("kind".into(), json!(k)); }
            }
            "MEMBER" | "X-ADDRESSBOOKSERVER-MEMBER" => {
                put_keyed(&mut card, "members", val.strip_prefix("urn:uuid:").unwrap_or(&val), json!(true));
            }
            "PHOTO" | "LOGO" => {
                let kind = if prop == "LOGO" { "logo" } else { "photo" };
                let enc = pstr(&params, "ENCODING").map(str::to_uppercase);
                let t = pstr(&params, "TYPE").unwrap_or("");
                let mt = pstr(&params, "MEDIATYPE").map(String::from).unwrap_or_else(|| {
                    if t.contains('/') { t.to_string() } else if ["JPEG", "JPG", "PNG", "GIF", "WEBP", "HEIC", "BMP", "SVG"].contains(&t.to_uppercase().as_str()) { t.to_string() } else { String::new() }
                });
                if enc.as_deref() == Some("B") || enc.as_deref() == Some("BASE64") {
                    // the text must be base64 and within the size limit; a bad photo is dropped, its card kept
                    let data: String = raw_val.chars().filter(|c| !c.is_whitespace()).collect();
                    if !base64_ok(&data) { rep.photos_dropped += 1; continue; }
                    let mime = if mt.contains('/') { safe_mime(&mt).unwrap_or("image/jpeg").to_string() }
                        else if !mt.is_empty() { format!("image/{}", mt.to_lowercase().replace("jpg", "jpeg")) } else { "image/jpeg".into() };
                    let mime = safe_mime(&mime).unwrap_or("image/jpeg").to_string();
                    push(&mut card, "media", "m", json!({"@type": "Media", "kind": kind, "uri": format!("data:{mime};base64,{data}"), "mediaType": mime}));
                } else if val.starts_with("data:") {
                    // only a base64 data: URI within the size limit
                    match parse_data_uri(&val) {
                        Some((_, data)) if base64_ok(&data) => push(&mut card, "media", "m", clean(json!({"@type": "Media", "kind": kind, "uri": val, "mediaType": if mt.contains('/') { safe_mime(&mt).map(String::from) } else { None }}))),
                        _ => rep.photos_dropped += 1,
                    }
                } else if val.starts_with("http://") || val.starts_with("https://") {
                    // a web link is kept as it is and never fetched (D-M5-A08)
                    push(&mut card, "media", "m", clean(json!({"@type": "Media", "kind": kind, "uri": val, "mediaType": if mt.contains('/') { safe_mime(&mt).map(String::from) } else { None }})));
                }
            }
            "TITLE" => push(&mut card, "titles", "t", json!({"@type": "Title", "name": val, "kind": "title"})),
            "ROLE" => push(&mut card, "titles", "t", json!({"@type": "Title", "name": val, "kind": "role"})),
            "URL" => {
                let tl = ty.as_deref().map(str::to_lowercase);
                let lab = label.clone().or_else(|| ty.clone().filter(|_| !matches!(tl.as_deref(), Some("home" | "work"))));
                push(&mut card, "links", "l", clean(json!({"@type": "Link", "uri": val, "contexts": ctx, "label": lab, "pref": pf})));
            }
            "IMPP" | "X-SOCIALPROFILE" | "SOCIALPROFILE" => {
                let mut s = json!({"@type": "OnlineService", "uri": val, "contexts": ctx, "pref": pf});
                let service = pstr(&params, "X-SERVICE-TYPE").or(pstr(&params, "SERVICE")).map(String::from).or_else(|| {
                    (prop != "IMPP").then(|| ty.clone()).flatten().filter(|t| !matches!(t.to_lowercase().as_str(), "home" | "work"))
                });
                if let Some(sv) = service { s["service"] = json!(sv); }
                if let Some(u) = pstr(&params, "X-USER") { s["user"] = json!(u); }
                if let Some(l) = &label { s["label"] = json!(l); }
                push(&mut card, "onlineServices", "s", clean(s));
            }
            "BDAY" => add_anniversary(&mut card, "birth", &val, &params),
            "ANNIVERSARY" | "X-ANNIVERSARY" => add_anniversary(&mut card, "wedding", &val, &params),
            "DEATHDATE" | "X-DEATHDATE" => add_anniversary(&mut card, "death", &val, &params),
            "X-ABDATE" => {
                let l = label.clone().unwrap_or_default().to_lowercase();
                let kind = if l.contains("birth") { "birth" } else if l.contains("anniversary") || l.contains("wedding") { "wedding" } else { "other" };
                add_anniversary(&mut card, kind, &val, &params);
            }
            "BIRTHPLACE" => birth_place = Some(val),
            "DEATHPLACE" => death_place = Some(val),
            "CATEGORIES" => for c in val.split(',').map(str::trim).filter(|x| !x.is_empty()) {
                put_keyed(&mut card, "keywords", c, json!(true));
            },
            "RELATED" => {
                let mut rel = Map::new();
                if let Some(t) = &ty { for x in t.split(',') { let n = x.trim().to_lowercase(); if !n.is_empty() && rel.len() < MAX_ENTRIES { rel.insert(n, json!(true)); } } }
                put_keyed(&mut card, "relatedTo", &val, if rel.is_empty() { json!({"@type": "Relation"}) } else { json!({"@type": "Relation", "relation": rel}) });
            }
            "X-ABRELATEDNAMES" | "X-SPOUSE" | "X-MANAGER" | "X-ASSISTANT" => {
                let lab = match prop.as_str() { "X-SPOUSE" => "spouse".to_string(), "X-MANAGER" => "manager".into(), "X-ASSISTANT" => "assistant".into(), _ => label.clone().unwrap_or_default() };
                if !val.is_empty() {
                    put_keyed(&mut card, "relatedTo", &val, match relation_from_label(&lab) { Some(x) => json!({"@type": "Relation", "relation": { x: true }}), None => json!({"@type": "Relation"}) });
                }
            }
            "X-MAIDENNAME" | "X-BIRTHNAME" => maiden = Some(val),
            "LANG" => push(&mut card, "preferredLanguages", "l", clean(json!({"@type": "LanguagePref", "language": val, "contexts": ctx, "pref": pf}))),
            p => {
                if let Some((_, svc)) = X_ONLINE.iter().find(|(k, _)| *k == p) {
                    let mut s = json!({"@type": "OnlineService", "uri": val, "service": svc, "contexts": ctx, "pref": pf});
                    let has_scheme = val.find(':').is_some_and(|i| i > 0 && val[..i].chars().all(|c| c.is_ascii_alphanumeric() || "+.-".contains(c)) && val.chars().next().is_some_and(|c| c.is_ascii_alphabetic()));
                    if !has_scheme { s["user"] = json!(val); }
                    if let Some(l) = &label { s["label"] = json!(l); }
                    push(&mut card, "onlineServices", "s", clean(s));
                }
            }
        }
    }
    if let Some(mut n) = name {
        n.remove("_fromFn");
        n.insert("@type".into(), json!("Name"));
        card.insert("name".into(), Value::Object(n));
    }
    if let Some(m) = maiden {
        let n = card.entry("name").or_insert_with(|| json!({"@type": "Name", "components": [], "isOrdered": true}));
        if !n["components"].is_array() { n["components"] = json!([]); }
        let comps = n["components"].as_array_mut().unwrap();
        if !comps.iter().any(|c| c["kind"] == "surname2") { comps.push(json!({"kind": "surname2", "value": m})); }
    }
    for (kind, place) in [("birth", birth_place), ("death", death_place)] {
        let Some(place) = place else { continue };
        let a = card.entry("anniversaries").or_insert_with(|| json!({}));
        let o = a.as_object_mut().unwrap();
        let key = o.iter().find(|(_, v)| v["kind"] == kind).map(|(k, _)| k.clone());
        match key {
            Some(k) => { o[&k]["place"] = json!({"@type": "Address", "full": place}); }
            None => { let k = next_key(o, "a"); o.insert(k, json!({"@type": "Anniversary", "kind": kind, "place": {"@type": "Address", "full": place}})); }
        }
    }
    let card = Value::Object(card);
    let has_name = card["name"]["components"].as_array().is_some_and(|c| !c.is_empty()) || card["name"]["full"].as_str().is_some_and(|f| !f.is_empty());
    let has_email = card["emails"].as_object().is_some_and(|e| !e.is_empty());
    let has_org = card["organizations"].as_object().and_then(|o| o.values().next()).and_then(|o| o["name"].as_str()).is_some_and(|n| !n.is_empty());
    if !has_name && !has_email && !has_org && card["kind"] != "group" { return None; }
    Some(card)
}

fn add_anniversary(card: &mut Map<String, Value>, kind: &str, raw: &str, params: &Map<String, Value>) {
    let Some(date) = parse_date(raw, params) else { return };
    push(card, "anniversaries", "a", json!({"@type": "Anniversary", "kind": kind, "date": date}));
}

fn has_scheme(v: &str) -> bool {
    let mut it = v.chars();
    it.next().is_some_and(|c| c.is_ascii_alphabetic()) && v.find(':').is_some_and(|i| v[..i].chars().all(|c| c.is_ascii_alphanumeric() || "+.-".contains(c)))
}

fn vcard_ts(v: &str) -> String {
    // 2025-07-28T12:17:00Z -> 20250728T121700Z
    match chrono::DateTime::parse_from_rfc3339(v) {
        Ok(t) => { let z = if v.ends_with('Z') || v.ends_with('z') { "Z".to_string() } else { t.format("%z").to_string() }; format!("{}{z}", t.format("%Y%m%dT%H%M%S")) }
        Err(_) => v.to_string(),
    }
}

/// RFC 6350 §3.2: fold at 75 octets without splitting a UTF-8 character.
fn fold(line: &str) -> String {
    let mut out: Vec<String> = vec![];
    let mut cur = String::new();
    let mut oct = 0;
    for c in line.chars() {
        let n = c.len_utf8();
        let limit = if out.is_empty() { 75 } else { 74 };
        if oct + n > limit { out.push(std::mem::take(&mut cur)); oct = 0; }
        cur.push(c);
        oct += n;
    }
    out.push(cur);
    out.join("\r\n ")
}

fn params_str(p: &[String]) -> String { if p.is_empty() { String::new() } else { format!(";{}", p.join(";")) } }

fn vals(c: &Value, k: &str) -> Vec<Value> { c[k].as_object().map(|o| o.values().cloned().collect()).unwrap_or_default() }

/// Text of one address entry: its components or flat RFC 9553 / legacy fields.
pub fn address_parts(a: &Value) -> (String, String, String, String, String) {
    let comp = |kinds: &[&str]| -> String {
        a["components"].as_array().map(|c| c.iter().filter(|x| x["kind"].as_str().is_some_and(|k| kinds.contains(&k))).filter_map(|x| x["value"].as_str()).collect::<Vec<_>>().join(" ")).unwrap_or_default()
    };
    let flat = |k: &str| a[k].as_str().unwrap_or("").to_string();
    let or = |a: String, b: String| if a.is_empty() { b } else { a };
    (or(flat("street"), comp(&["number", "name", "block", "building", "floor", "apartment", "room", "extension", "direction", "landmark", "postOfficeBox", "subdistrict", "district"])),
     or(flat("locality"), comp(&["locality"])), or(flat("region"), comp(&["region"])), or(flat("postcode"), comp(&["postcode"])), or(flat("country"), comp(&["country"])))
}

fn one(c: &Value) -> String {
    let mut l: Vec<String> = vec!["BEGIN:VCARD".into(), "VERSION:4.0".into(), "PRODID:-//Barbican//Barbican Mail//EN".into()];
    if let Some(u) = c["uid"].as_str() { l.push(if has_scheme(u) { format!("UID:{u}") } else { format!("UID;VALUE=text:{u}") }); }
    if let Some(k) = c["kind"].as_str() { l.push(format!("KIND:{k}")); }
    if let Some(u) = c["updated"].as_str() { l.push(format!("REV:{}", vcard_ts(u))); }
    let comps = c["name"]["components"].as_array().cloned().unwrap_or_default();
    let find = |kinds: &[&str]| comps.iter().find(|x| x["kind"].as_str().is_some_and(|k| kinds.contains(&k))).and_then(|x| x["value"].as_str()).unwrap_or("").to_string();
    let (given, surname, prefix, suffix, add) = (find(&["given"]), find(&["surname"]), find(&["title", "prefix"]), find(&["generation", "suffix"]), find(&["given2", "additional", "middle"]));
    let joined = [prefix.as_str(), given.as_str(), add.as_str(), surname.as_str(), suffix.as_str()].iter().filter(|x| !x.is_empty()).copied().collect::<Vec<_>>().join(" ");
    let fname = if !joined.is_empty() { joined } else { c["name"]["full"].as_str().map(String::from)
        .or_else(|| vals(c, "organizations").first().and_then(|o| o["name"].as_str().map(String::from))).unwrap_or_default() };
    if !fname.is_empty() {
        l.push(format!("N:{};{};{};{};{}", encode_value(&surname), encode_value(&given), encode_value(&add), encode_value(&prefix), encode_value(&suffix)));
        l.push(format!("FN:{}", encode_value(&fname)));
    }
    let s2 = find(&["surname2"]);
    if !s2.is_empty() { l.push(format!("X-MAIDENNAME:{}", encode_value(&s2))); }
    for n in vals(c, "nicknames") { if let Some(x) = n["name"].as_str() { l.push(format!("NICKNAME:{}", encode_value(x))); } }
    for e in vals(c, "emails") {
        let mut p = vec![];
        let t = context_type(&e["contexts"]); if !t.is_empty() { p.push(format!("TYPE={t}")); }
        if let Some(n) = e["pref"].as_i64() { p.push(format!("PREF={n}")); }
        if let Some(lab) = e["label"].as_str().filter(|x| !x.is_empty()) { p.push(format!("X-ABLABEL=\"{}\"", encode_param(lab))); }
        l.push(format!("EMAIL{}:{}", params_str(&p), e["address"].as_str().unwrap_or("")));
    }
    for ph in vals(c, "phones") {
        let mut types: Vec<String> = vec![];
        let t = context_type(&ph["contexts"]); if !t.is_empty() { types.push(t.into()); }
        types.extend(feature_types(&ph["features"]));
        let mut p = vec![];
        if !types.is_empty() { p.push(format!("TYPE={}", types.join(","))); }
        if let Some(n) = ph["pref"].as_i64() { p.push(format!("PREF={n}")); }
        if let Some(lab) = ph["label"].as_str().filter(|x| !x.is_empty()) { p.push(format!("X-ABLABEL=\"{}\"", encode_param(lab))); }
        l.push(format!("TEL{}:{}", params_str(&p), ph["number"].as_str().unwrap_or("")));
    }
    for o in vals(c, "organizations") {
        let mut parts = vec![o["name"].as_str().unwrap_or("").to_string()];
        parts.extend(o["units"].as_array().map(|u| u.iter().filter_map(|x| x["name"].as_str().map(String::from)).collect::<Vec<_>>()).unwrap_or_default());
        l.push(format!("ORG:{}", parts.iter().map(|x| encode_value(x)).collect::<Vec<_>>().join(";")));
    }
    for t in vals(c, "titles") {
        let n = encode_value(t["name"].as_str().unwrap_or(""));
        l.push(if t["kind"] == "role" { format!("ROLE:{n}") } else { format!("TITLE:{n}") });
    }
    for a in vals(c, "addresses") {
        let mut p = vec![];
        let t = context_type(&a["contexts"]); if !t.is_empty() { p.push(format!("TYPE={t}")); }
        if let Some(cc) = a["countryCode"].as_str() { p.push(format!("CC={cc}")); }
        if let Some(n) = a["pref"].as_i64() { p.push(format!("PREF={n}")); }
        if let Some(lab) = a["label"].as_str().filter(|x| !x.is_empty()) { p.push(format!("X-ABLABEL=\"{}\"", encode_param(lab))); }
        let (st, loc, reg, pc, co) = address_parts(&a);
        let (st, loc, reg, pc, co) = if [&st, &loc, &reg, &pc, &co].iter().all(|x| x.is_empty()) {
            (a["full"].as_str().or(a["fullAddress"].as_str()).unwrap_or("").to_string(), loc, reg, pc, co)
        } else { (st, loc, reg, pc, co) };
        l.push(format!("ADR{}:;;{};{};{};{};{}", params_str(&p), encode_value(&st), encode_value(&loc), encode_value(&reg), encode_value(&pc), encode_value(&co)));
    }
    for an in vals(c, "anniversaries") {
        let d = date_to_vcard(&an["date"]);
        let place = an["place"]["full"].as_str().or(an["place"]["fullAddress"].as_str());
        match an["kind"].as_str().unwrap_or("") {
            "birth" => { if !d.is_empty() { l.push(format!("BDAY:{d}")); } if let Some(p) = place { l.push(format!("BIRTHPLACE:{}", encode_value(p))); } }
            "wedding" => if !d.is_empty() { l.push(format!("ANNIVERSARY:{d}")) },
            "death" => { if !d.is_empty() { l.push(format!("DEATHDATE:{d}")); } if let Some(p) = place { l.push(format!("DEATHPLACE:{}", encode_value(p))); } }
            _ => if !d.is_empty() { l.push(format!("X-ABDATE:{d}")) },
        }
    }
    for s in vals(c, "onlineServices") {
        let mut p = vec![];
        if let Some(sv) = s["service"].as_str() { p.push(format!("X-SERVICE-TYPE={sv}")); }
        let t = context_type(&s["contexts"]); if !t.is_empty() { p.push(format!("TYPE={t}")); }
        if let Some(n) = s["pref"].as_i64() { p.push(format!("PREF={n}")); }
        let uri = s["uri"].as_str().or(s["user"].as_str()).unwrap_or("");
        if s["service"].is_string() || s["user"].is_string() { l.push(format!("IMPP{}:{uri}", params_str(&p))); } else { l.push(format!("URL{}:{uri}", params_str(&p))); }
    }
    for k in vals(c, "links") {
        let mut p = vec![];
        let t = context_type(&k["contexts"]); if !t.is_empty() { p.push(format!("TYPE={t}")); }
        if let Some(n) = k["pref"].as_i64() { p.push(format!("PREF={n}")); }
        if let Some(lab) = k["label"].as_str().filter(|x| !x.is_empty()) { p.push(format!("X-ABLABEL=\"{}\"", encode_param(lab))); }
        l.push(format!("{}{}:{}", if k["kind"] == "contact" { "CONTACT-URI" } else { "URL" }, params_str(&p), k["uri"].as_str().unwrap_or("")));
    }
    if let Some(kw) = c["keywords"].as_object() {
        let cats: Vec<String> = kw.iter().filter(|(_, v)| **v == true).map(|(k, _)| encode_value(k)).collect();
        if !cats.is_empty() { l.push(format!("CATEGORIES:{}", cats.join(","))); }
    }
    for lang in vals(c, "preferredLanguages") {
        let mut p = vec![];
        let t = context_type(&lang["contexts"]); if !t.is_empty() { p.push(format!("TYPE={t}")); }
        if let Some(n) = lang["pref"].as_i64() { p.push(format!("PREF={n}")); }
        l.push(format!("LANG{}:{}", params_str(&p), lang["language"].as_str().unwrap_or("")));
    }
    if let Some(r) = c["relatedTo"].as_object() {
        for (uri, rel) in r {
            let mut p = vec![];
            if !has_scheme(uri) { p.push("VALUE=text".to_string()); }
            if let Some(t) = rel["relation"].as_object().and_then(|o| o.iter().find(|(_, v)| **v == true).map(|(k, _)| k.clone())) { p.push(format!("TYPE={t}")); }
            l.push(format!("RELATED{}:{}", params_str(&p), if has_scheme(uri) { uri.clone() } else { encode_value(uri) }));
        }
    }
    for n in vals(c, "notes") { if let Some(x) = n["note"].as_str() { l.push(format!("NOTE:{}", encode_value(x))); } }
    if let Some(m) = c["members"].as_object() {
        for (k, on) in m { if *on == true { l.push(format!("MEMBER:{}", if has_scheme(k) { k.clone() } else { format!("urn:uuid:{k}") })); } }
    }
    for m in vals(c, "media") {
        let Some(uri) = m["uri"].as_str() else { continue };
        let prop = match m["kind"].as_str() { Some("logo") => "LOGO", Some("sound") => "SOUND", _ => "PHOTO" };
        if let Some((ty, data)) = parse_data_uri(uri) {
            // a picture that would not be read back (too big, not base64) is not written either
            if !base64_ok(&data) { continue; }
            let ty = if ty.is_empty() { m["mediaType"].as_str().unwrap_or("").to_string() } else { ty };
            let ty = safe_mime(&ty).unwrap_or("");
            l.push(format!("{prop}:data:{ty};base64,{data}"));
        } else if !uri.starts_with("data:") {
            let mt = m["mediaType"].as_str().and_then(safe_mime).map(|x| format!(";MEDIATYPE={x}")).unwrap_or_default();
            l.push(format!("{prop};VALUE=URI{mt}:{uri}"));
        }
    }
    for a in vals(c, "addresses") {
        if let Some(g) = a["coordinates"].as_str() { l.push(format!("GEO:{}", if g.to_lowercase().starts_with("geo:") { g.to_string() } else { format!("geo:{}", g.replace(';', ",")) })); }
        if let Some(tz) = a["timeZone"].as_str() { l.push(format!("TZ:{tz}")); }
    }
    if let Some(cr) = c["created"].as_str() { l.push(format!("CREATED:{}", vcard_ts(cr))); }
    l.push("END:VCARD".into());
    // D-M5-A04: no CR or LF may survive in a property line, so a value with a line break cannot
    // end its property and forge another (addresses, numbers and URIs are written unescaped)
    l.iter().map(|x| format!("{}\r\n", fold(&x.replace(['\r', '\n'], "")))).collect()
}

/// (media type, base64 data) of a base64 data: URI. Stalwart writes `data:;base64,` and
/// `data:base64,` for photos without a type.
pub fn parse_data_uri(uri: &str) -> Option<(String, String)> {
    let rest = uri.strip_prefix("data:")?;
    let (head, data) = rest.split_once(',')?;
    if head != "base64" && !head.ends_with(";base64") { return None; }
    let ty = head.trim_end_matches("base64").trim_end_matches(';').split(';').next().unwrap_or("").to_string();
    Some((ty, data.to_string()))
}

/// Cards as one vCard 4.0 file.
pub fn generate(cards: &[Value]) -> String { cards.iter().map(one).collect() }

/// Incoming cards (by index) whose email address an existing card, or an earlier card in
/// the same file, already has (Bulwark detectDuplicates).
pub fn detect_duplicates(existing: &[Value], incoming: &[Value]) -> Vec<usize> {
    let addrs = |c: &Value| -> Vec<String> { vals(c, "emails").iter().filter_map(|e| e["address"].as_str()).map(|a| a.trim().to_lowercase()).filter(|a| !a.is_empty()).collect() };
    let mut known: std::collections::HashSet<String> = existing.iter().flat_map(addrs).collect();
    let mut out = vec![];
    for (i, c) in incoming.iter().enumerate() {
        let a = addrs(c);
        if a.iter().any(|x| known.contains(x)) { out.push(i); continue; }
        known.extend(a);
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_vcard3_with_types_and_apple_labels() {
        let v = "BEGIN:VCARD\r\nVERSION:3.0\r\nN:Doe;Jane;Q;Dr.;Jr.\r\nFN:Dr. Jane Q Doe Jr.\r\nEMAIL;TYPE=INTERNET,WORK,PREF:jane@work.example\r\n\
EMAIL;TYPE=HOME:jane@home.example\r\nTEL;TYPE=CELL:+1 555 0100\r\nitem1.TEL:+1 555 0199\r\nitem1.X-ABLabel:_$!<Ski cabin>!$_\r\n\
ADR;TYPE=WORK:;;1 Main St;Springfield;IL;62701;USA\r\nBDAY:1985-04-12\r\nX-ABRELATEDNAMES;TYPE=pref:John Doe\r\nitem2.X-ABRELATEDNAMES:Ann\r\nitem2.X-ABLabel:_$!<Mother>!$_\r\n\
URL;TYPE=WORK:https://example.com\r\nNOTE:Line one\\nline two\\, with comma\r\nCATEGORIES:Friends,VIP\r\nORG:ACME;R&D\r\nTITLE:Engineer\r\n\
PHOTO;ENCODING=b;TYPE=JPEG:/9j/4AAQ\r\n SkZJRg==\r\nEND:VCARD\r\n";
        let cards = parse(v);
        assert_eq!(cards.len(), 1);
        let c = &cards[0];
        assert_eq!(c["name"]["full"], "Dr. Jane Q Doe Jr.");
        let comps = c["name"]["components"].as_array().unwrap();
        assert_eq!(comps[0]["kind"], "title");
        assert_eq!(comps[1]["value"], "Jane");
        assert_eq!(comps[3]["value"], "Doe");
        let emails: Vec<&Value> = c["emails"].as_object().unwrap().values().collect();
        assert_eq!(emails[0]["contexts"]["work"], true);
        assert_eq!(emails[0]["pref"], 1);
        assert_eq!(emails[1]["contexts"]["private"], true);
        let phones: Vec<&Value> = c["phones"].as_object().unwrap().values().collect();
        assert_eq!(phones[0]["features"]["mobile"], true);
        assert_eq!(phones[1]["label"], "Ski cabin");
        let a = c["addresses"].as_object().unwrap().values().next().unwrap();
        assert_eq!(address_parts(a).1, "Springfield");
        let bday = c["anniversaries"].as_object().unwrap().values().next().unwrap();
        assert_eq!(bday["date"]["year"], 1985);
        assert_eq!(bday["date"]["day"], 12);
        assert_eq!(c["relatedTo"]["Ann"]["relation"]["parent"], true);
        assert!(c["relatedTo"]["John Doe"].is_object());
        assert_eq!(c["notes"].as_object().unwrap().values().next().unwrap()["note"], "Line one\nline two, with comma");
        assert_eq!(c["keywords"]["VIP"], true);
        assert_eq!(c["organizations"].as_object().unwrap().values().next().unwrap()["units"][0]["name"], "R&D");
        assert_eq!(c["media"].as_object().unwrap().values().next().unwrap()["uri"], "data:image/jpeg;base64,/9j/4AAQSkZJRg==");
        assert_eq!(c["links"].as_object().unwrap().values().next().unwrap()["uri"], "https://example.com");
    }

    #[test]
    fn dates_reduced_forms() {
        let p = Map::new();
        assert_eq!(parse_date("--0412", &p).unwrap()["month"], 4);
        assert!(parse_date("--0412", &p).unwrap().get("year").is_none());
        assert_eq!(parse_date("19850412T232050Z", &p).unwrap()["day"], 12);
        assert_eq!(parse_date("1985-04", &p).unwrap()["month"], 4);
        assert_eq!(parse_date("---12", &p).unwrap()["day"], 12);
        assert!(parse_date("1985-13-01", &p).is_none());
        assert!(parse_date("circa", &p).is_none());
        assert_eq!(date_to_vcard(&json!({"month": 4, "day": 12})), "--0412");
        assert_eq!(date_to_vcard(&json!({"@type": "Timestamp", "utc": "1990-01-02T00:00:00Z"})), "19900102");
    }

    #[test]
    fn round_trip_and_folding() {
        let card = json!({"uid": "urn:uuid:1234", "name": {"components": [{"kind": "given", "value": "Ünal"}, {"kind": "surname", "value": "Öztürk"}]},
            "emails": {"e0": {"address": "u@example.com", "contexts": {"work": true}, "pref": 1}},
            "phones": {"p0": {"number": "+90 555", "features": {"mobile": true}, "contexts": {"private": true}}},
            "addresses": {"a0": {"components": [{"kind": "name", "value": "Long Street"}, {"kind": "locality", "value": "Ankara"}], "contexts": {"private": true}}},
            "anniversaries": {"a": {"kind": "wedding", "date": {"year": 2001, "month": 6, "day": 9}}},
            "notes": {"n": {"note": "x".repeat(200)}}, "keywords": {"Family": true},
            "relatedTo": {"Ayşe": {"relation": {"spouse": true}}}, "links": {"l": {"uri": "https://ex.org"}}});
        let out = generate(&[card]);
        assert!(out.contains("UID:urn:uuid:1234\r\n"));
        assert!(out.contains("EMAIL;TYPE=WORK;PREF=1:u@example.com"));
        assert!(out.contains("TEL;TYPE=HOME,CELL:+90 555"));
        assert!(out.contains("ANNIVERSARY:20010609"));
        assert!(out.contains("RELATED;VALUE=text;TYPE=spouse:Ayşe"));
        assert!(out.lines().all(|l| l.trim_end_matches('\r').len() <= 75));
        let back = parse(&out);
        assert_eq!(back.len(), 1);
        let b = &back[0];
        assert_eq!(b["uid"], "urn:uuid:1234");
        assert_eq!(b["notes"].as_object().unwrap().values().next().unwrap()["note"].as_str().unwrap().len(), 200);
        assert_eq!(b["phones"].as_object().unwrap().values().next().unwrap()["features"]["mobile"], true);
        assert_eq!(b["anniversaries"].as_object().unwrap().values().next().unwrap()["date"]["month"], 6);
        assert_eq!(b["relatedTo"]["Ayşe"]["relation"]["spouse"], true);
        assert_eq!(b["keywords"]["Family"], true);
        assert_eq!(address_parts(b["addresses"].as_object().unwrap().values().next().unwrap()).1, "Ankara");
    }

    #[test]
    fn quoted_printable_and_skips_empty() {
        let v = "BEGIN:VCARD\nVERSION:2.1\nN;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:M=C3=BCller;J=\n=C3=BCrgen\nEND:VCARD\nBEGIN:VCARD\nVERSION:3.0\nNOTE:nothing\nEND:VCARD\n";
        let c = parse(v);
        assert_eq!(c.len(), 1);
        assert_eq!(c[0]["name"]["components"][0]["value"], "Jürgen");
        assert_eq!(c[0]["name"]["components"][1]["value"], "Müller");
    }

    fn one_card(body: &str) -> String { format!("BEGIN:VCARD\r\nVERSION:4.0\r\n{body}\r\nEND:VCARD\r\n") }

    #[test]
    fn a_file_above_16_million_characters_is_not_read() {
        let big = format!("{}{}", one_card("FN:Ann Example"), " ".repeat(MAX_FILE_CHARS));
        let (cards, rep) = parse_report(&big);
        assert!(cards.is_empty() && rep.file_too_large);
        assert!(rep.note().contains("16 million"));
        // exactly at the limit is read
        let at = format!("{}{}", one_card("FN:Ann Example"), "\n".repeat(MAX_FILE_CHARS - one_card("FN:Ann Example").chars().count()));
        assert_eq!(at.chars().count(), MAX_FILE_CHARS);
        let (cards, rep) = parse_report(&at);
        assert_eq!((cards.len(), rep.file_too_large), (1, false));
        // the limit counts characters, not bytes
        let wide = format!("{}{}", one_card("FN:Ann"), "\u{e9}".repeat(MAX_FILE_CHARS - 100));
        assert!(wide.len() > MAX_FILE_CHARS && !parse_report(&wide).1.file_too_large);
    }

    #[test]
    fn at_most_10000_cards_are_read() {
        let one = "BEGIN:VCARD\nFN:A B\nEND:VCARD\n";
        let (cards, rep) = parse_report(&one.repeat(MAX_CARDS));
        assert_eq!((cards.len(), rep.cards_truncated), (MAX_CARDS, false));
        let (cards, rep) = parse_report(&one.repeat(MAX_CARDS + 5));
        assert_eq!((cards.len(), rep.cards_truncated), (MAX_CARDS, true));
        assert!(rep.note().contains("10000"));
    }

    #[test]
    fn a_line_above_1_million_characters_is_skipped_and_its_card_kept() {
        let at = format!("NOTE:{}", "x".repeat(MAX_LINE_CHARS - 5));
        let over = format!("NOTE:{}", "x".repeat(MAX_LINE_CHARS - 4));
        assert_eq!(at.chars().count(), MAX_LINE_CHARS);
        let (cards, rep) = parse_report(&one_card(&format!("FN:Ann Example\r\n{at}\r\nEMAIL:ann@example.test")));
        assert_eq!((cards.len(), rep.lines_dropped), (1, 0));
        assert_eq!(cards[0]["notes"].as_object().unwrap().len(), 1);
        let (cards, rep) = parse_report(&one_card(&format!("FN:Ann Example\r\n{over}\r\nEMAIL:ann@example.test")));
        assert_eq!((cards.len(), rep.lines_dropped), (1, 1));
        assert!(cards[0]["notes"].is_null());
        assert_eq!(cards[0]["emails"]["e0"]["address"], "ann@example.test");
        // folding does not get around it: 20 000 folds of 75 characters make one long line
        let folded = format!("NOTE:{}", "y".repeat(75 * 20_000)).as_bytes().chunks(74).map(|c| String::from_utf8_lossy(c).to_string()).collect::<Vec<_>>().join("\r\n ");
        let (cards, rep) = parse_report(&one_card(&format!("FN:Ann Example\r\n{folded}")));
        assert_eq!((cards.len(), rep.lines_dropped), (1, 1));
        assert!(cards[0]["notes"].is_null());
    }

    #[test]
    fn a_card_keeps_5000_property_lines_and_500_entries_a_kind() {
        let junk = |n: usize| "X-JUNK:1\r\n".repeat(n);
        // VERSION is line 1, FN line 2, the email line 5000: kept; one line later: dropped
        let (cards, _) = parse_report(&one_card(&format!("FN:Ann Example\r\n{}EMAIL:a@example.test", junk(MAX_PROPS_PER_CARD - 3))));
        assert_eq!(cards[0]["emails"]["e0"]["address"], "a@example.test");
        let (cards, _) = parse_report(&one_card(&format!("FN:Ann Example\r\n{}EMAIL:a@example.test", junk(MAX_PROPS_PER_CARD - 2))));
        assert!(cards[0]["emails"].is_null());
        // 600 of each kind: 500 kept
        let many = |f: &dyn Fn(usize) -> String| (0..600).map(f).collect::<Vec<_>>().join("\r\n");
        let body = format!("FN:Ann Example\r\n{}\r\n{}\r\n{}\r\n{}\r\n{}", many(&|i| format!("EMAIL:e{i}@example.test")), many(&|i| format!("TEL:{i}")),
            many(&|i| format!("CATEGORIES:k{i}")), many(&|i| format!("RELATED;VALUE=text:p{i}")), many(&|i| format!("NICKNAME:n{i}")));
        let (cards, _) = parse_report(&one_card(&body));
        for k in ["emails", "phones", "keywords", "relatedTo", "nicknames"] {
            assert_eq!(cards[0][k].as_object().unwrap().len(), MAX_ENTRIES, "{k}");
        }
        assert_eq!(cards[0]["emails"]["e0"]["address"], "e0@example.test");
        // 500 exactly are all kept
        let (cards, _) = parse_report(&one_card(&format!("FN:A B\r\n{}", (0..MAX_ENTRIES).map(|i| format!("EMAIL:e{i}@example.test")).collect::<Vec<_>>().join("\r\n"))));
        assert_eq!(cards[0]["emails"].as_object().unwrap().len(), MAX_ENTRIES);
    }

    #[test]
    fn organisation_units_and_relation_types_are_capped_at_500() {
        // 600 units: the name and 500 units are kept
        let org = (0..601).map(|i| format!("u{i}")).collect::<Vec<_>>().join(";");
        let (cards, _) = parse_report(&one_card(&format!("FN:Ann Example\r\nORG:{org}")));
        let o = &cards[0]["organizations"]["o0"];
        assert_eq!(o["name"], "u0");
        assert_eq!(o["units"].as_array().unwrap().len(), MAX_ENTRIES);
        assert_eq!(o["units"][0]["name"], "u1");
        assert_eq!(o["units"][MAX_ENTRIES - 1]["name"], format!("u{MAX_ENTRIES}"));
        // exactly 500 units are all kept
        let org = (0..=MAX_ENTRIES).map(|i| format!("u{i}")).collect::<Vec<_>>().join(";");
        let (cards, _) = parse_report(&one_card(&format!("FN:Ann Example\r\nORG:{org}")));
        assert_eq!(cards[0]["organizations"]["o0"]["units"].as_array().unwrap().len(), MAX_ENTRIES);
        // a long line of units is still bounded (200 000 units in one line)
        let (cards, _) = parse_report(&one_card(&format!("FN:Ann Example\r\nORG:{}", "a;".repeat(200_000))));
        assert_eq!(cards[0]["organizations"]["o0"]["units"].as_array().unwrap().len(), MAX_ENTRIES);
        // 600 distinct relation types: 500 keys
        let types = (0..600).map(|i| format!("t{i}")).collect::<Vec<_>>().join(",");
        let (cards, _) = parse_report(&one_card(&format!("FN:Ann Example\r\nRELATED;TYPE={types}:bob")));
        assert_eq!(cards[0]["relatedTo"]["bob"]["relation"].as_object().unwrap().len(), MAX_ENTRIES);
    }

    #[test]
    fn repeated_type_parameters_cost_linear_time_and_keep_500_types() {
        // 190 000 repeats on one line: with a quadratic join this took minutes
        let line = format!("TEL{}:1", ";WORK".repeat(190_000));
        assert!(line.len() < MAX_LINE_CHARS);
        let started = std::time::Instant::now();
        let (cards, rep) = parse_report(&one_card(&format!("FN:Ann Example\r\n{line}\r\nTEL{}:2", ";TYPE=HOME".repeat(90_000))));
        assert!(started.elapsed().as_secs() < 20, "parse took {:?}", started.elapsed());
        assert_eq!(rep.lines_dropped, 0);
        assert_eq!(cards[0]["phones"]["p0"]["number"], "1");
        assert_eq!(cards[0]["phones"]["p0"]["contexts"]["work"], true);
        assert_eq!(cards[0]["phones"]["p1"]["contexts"]["private"], true);
        // order and joining are as before for the ordinary case
        let p = parse_params("TYPE=WORK;VOICE;TYPE=pref");
        assert_eq!(p["TYPE"], "WORK,VOICE,pref");
        let p = parse_params("WORK;TYPE=VOICE");
        assert_eq!(p["TYPE"], "WORK,VOICE");
        assert_eq!(parse_params(&"WORK;".repeat(900))["TYPE"].as_str().unwrap().split(',').count(), MAX_ENTRIES);
    }

    #[test]
    fn a_begin_inside_a_card_starts_the_card_over_and_nesting_is_bounded() {
        let (cards, _) = parse_report("BEGIN:VCARD\nFN:Outer One\nBEGIN:VCARD\nFN:Inner One\nEND:VCARD\nEND:VCARD\n");
        assert_eq!(cards.len(), 1);
        assert_eq!(cards[0]["name"]["full"], "Inner One");
        // a hundred thousand BEGINs and one END: one card at most, no recursion, no panic
        let deep = format!("{}FN:Deep Card\nEND:VCARD\n", "BEGIN:VCARD\n".repeat(100_000));
        let (cards, _) = parse_report(&deep);
        assert_eq!(cards.len(), 1);
        // END without BEGIN, BEGIN without END
        assert!(parse("END:VCARD\nFN:A B\n").is_empty());
        assert!(parse("BEGIN:VCARD\nFN:A B\n").is_empty());
    }

    #[test]
    fn photos_above_8_mib_or_not_base64_are_dropped_with_the_card_kept() {
        let photo = |head: &str, body: &str| format!("FN:Ann Example\r\nEMAIL:ann@example.test\r\n{head}:{body}");
        // just under 8 MiB decoded: kept (the line is far above the 1 M line limit; photo lines have their own)
        let ok_len = MAX_IMAGE_BYTES / 3 * 4 - 8;
        let (cards, rep) = parse_report(&one_card(&photo("PHOTO;ENCODING=b;TYPE=JPEG", &"A".repeat(ok_len))));
        assert_eq!((cards.len(), rep.photos_dropped, rep.lines_dropped), (1, 0, 0));
        assert_eq!(cards[0]["media"].as_object().unwrap().len(), 1);
        // a little over 8 MiB: the photo goes, the card stays
        let (cards, rep) = parse_report(&one_card(&photo("PHOTO;ENCODING=b;TYPE=JPEG", &"A".repeat(MAX_IMAGE_BYTES / 3 * 4 + 16))));
        assert_eq!((cards.len(), rep.photos_dropped), (1, 1));
        assert!(cards[0]["media"].is_null());
        assert_eq!(cards[0]["emails"]["e0"]["address"], "ann@example.test");
        assert!(rep.note().contains("1 photo was"));
        // the same through a data: URI
        let (cards, rep) = parse_report(&one_card(&photo("PHOTO", &format!("data:image/jpeg;base64,{}", "A".repeat(MAX_IMAGE_BYTES / 3 * 4 + 16)))));
        assert_eq!((cards.len(), rep.photos_dropped), (1, 1));
        assert!(cards[0]["media"].is_null());
        // a photo line past even the photo limit is a dropped line
        let (cards, rep) = parse_report(&one_card(&photo("PHOTO;ENCODING=b", &"A".repeat(MAX_IMAGE_BYTES / 3 * 4 + 20_000))));
        assert_eq!((cards.len(), rep.lines_dropped, rep.photos_dropped), (1, 1, 0));
        // not base64: embedded text, a data: URI that is not base64, junk in the base64
        for (head, body) in [("PHOTO;ENCODING=b", "not base64 at all!"), ("PHOTO", "data:image/png,rawtext"), ("PHOTO", "data:image/png;base64,@@@@"),
            ("PHOTO;ENCODING=b", "AAAA=AAA"), ("PHOTO;ENCODING=b", "")] {
            let (cards, rep) = parse_report(&one_card(&photo(head, body)));
            assert_eq!((cards.len(), rep.photos_dropped), (1, 1), "{head}:{body}");
            assert!(cards[0]["media"].is_null());
        }
        // good ones: embedded with folded text, a data: URI, a web link (kept, never fetched)
        let (cards, rep) = parse_report(&one_card("FN:Ann Example\r\nPHOTO;ENCODING=b;TYPE=PNG:iVBORw0K\r\n Ggo=\r\nLOGO:data:image/png;base64,iVBORw0KGgo=\r\nPHOTO;VALUE=URI:https://example.test/a.jpg"));
        assert_eq!(rep.photos_dropped, 0);
        let m: Vec<&Value> = cards[0]["media"].as_object().unwrap().values().collect();
        assert_eq!(m.len(), 3);
        assert_eq!(m[0]["uri"], "data:image/png;base64,iVBORw0KGgo=");
        assert_eq!(m[1]["kind"], "logo");
        assert_eq!(m[2]["uri"], "https://example.test/a.jpg");
        // a hostile media type is not carried into the data: URI
        let (cards, _) = parse_report(&one_card("FN:A B\r\nPHOTO;ENCODING=b;MEDIATYPE=\"x/y,z;w\":AAAA"));
        assert_eq!(cards[0]["media"]["m0"]["uri"], "data:image/jpeg;base64,AAAA");
    }

    #[test]
    fn base64_limit_is_judged_from_the_length() {
        assert!(base64_ok("iVBORw0KGgo="));
        assert!(base64_ok(&"A".repeat(MAX_IMAGE_BYTES / 3 * 4 - 16)));
        assert!(!base64_ok(&"A".repeat(MAX_IMAGE_BYTES / 3 * 4 + 16)));
        assert!(!base64_ok("") && !base64_ok("====") && !base64_ok("AA==="));
        assert!(!base64_ok("AA AA") && !base64_ok("AA-_") && !base64_ok("AA\nAA"));
    }

    #[test]
    fn line_endings_folding_and_soft_breaks_still_unfold() {
        // CRLF, LF and a lone CR; a tab continues a line too; blank lines are skipped
        let v = "BEGIN:VCARD\rFN:Ann\r\n\t Example\n\nNOTE:one\r\n  two\nEND:VCARD";
        let c = parse(v);
        assert_eq!(c.len(), 1);
        assert_eq!(c[0]["name"]["full"], "Ann Example");
        assert_eq!(c[0]["notes"]["n0"]["note"], "one two");
    }

    #[test]
    fn written_values_cannot_carry_a_line_break() {
        let evil = "1 Main St\r\nEND:VCARD\r\nBEGIN:VCARD\r\nFN:Forged\nEMAIL:forged@example.test";
        let card = json!({"name": {"full": "Ann Example"},
            "emails": {"e0": {"address": "ann@example.test\r\nTEL:555"}},
            "phones": {"p0": {"number": "555\nURL:https://evil.example.test"}},
            "addresses": {"a0": {"components": [{"kind": "name", "value": evil}, {"kind": "locality", "value": "Town\rEND:VCARD"}]}},
            "links": {"l0": {"uri": "https://example.test/\r\nNOTE:forged"}},
            "onlineServices": {"s0": {"service": "X\nY", "uri": "xmpp:ann@example.test\nNOTE:forged"}},
            "notes": {"n0": {"note": "a\r\nb\rc"}}, "relatedTo": {"Bob\r\nNOTE:forged": {"relation": {"friend": true}}},
            "media": {"m0": {"uri": "https://example.test/p.jpg\r\nNOTE:forged", "mediaType": "image/jpeg\r\nNOTE:forged"}}});
        let out = generate(&[card]);
        // exactly one card, and every physical line is a property of it or a fold of one
        assert_eq!(out.matches("BEGIN:VCARD\r\n").count(), 1);
        assert_eq!(out.matches("END:VCARD\r\n").count(), 1);
        assert!(out.ends_with("END:VCARD\r\n"));
        for l in out.split("\r\n").filter(|l| !l.is_empty() && !l.starts_with(' ')) {
            let name = l.split([';', ':']).next().unwrap();
            assert!(["BEGIN", "VERSION", "PRODID", "N", "FN", "EMAIL", "TEL", "ADR", "URL", "IMPP", "NOTE", "RELATED", "PHOTO", "END"].contains(&name), "{l}");
        }
        assert_eq!(out.matches('\r').count(), out.matches("\r\n").count());
        assert_eq!(out.matches('\n').count(), out.matches("\r\n").count());
        assert!(!out.contains("\nNOTE:forged") && !out.contains("\nFN:Forged") && !out.contains("\nTEL:555\r"));
        // read back, it is still one contact without a forged email or a second card
        let back = parse(&out);
        assert_eq!(back.len(), 1);
        assert_eq!(back[0]["name"]["full"], "Ann Example");
        assert!(!back[0]["emails"].as_object().unwrap().values().any(|e| e["address"] == "forged@example.test"));
    }

    #[test]
    fn export_skips_pictures_the_reader_would_refuse_and_checks_media_types() {
        let big = "A".repeat(MAX_IMAGE_BYTES / 3 * 4 + 16);
        let card = json!({"name": {"full": "Ann Example"}, "media": {"m0": {"kind": "photo", "uri": format!("data:image/jpeg;base64,{big}")},
            "m1": {"kind": "logo", "uri": "data:image/x y;z,base64,AAAA".replace(",base64", ";base64"), "mediaType": "image/png"},
            "m2": {"kind": "photo", "uri": "data:image/png;base64,iVBORw0KGgo="}}});
        let out = generate(&[card]);
        assert!(!out.contains(&big[..100]));
        assert!(out.contains("PHOTO:data:image/png;base64,iVBORw0KGgo="));
        assert!(out.contains("LOGO:data:;base64,AAAA"));
    }

    #[test]
    fn duplicates_in_file_and_existing() {
        let existing = vec![json!({"emails": {"a": {"address": "A@x.org"}}})];
        let incoming = vec![json!({"emails": {"a": {"address": "a@x.org"}}}), json!({"emails": {"a": {"address": "b@x.org"}}}), json!({"emails": {"a": {"address": "B@X.org "}}})];
        assert_eq!(detect_duplicates(&existing, &incoming), vec![0, 2]);
        assert_eq!(parse_data_uri("data:;base64,AAA"), Some(("".into(), "AAA".into())));
        assert_eq!(parse_data_uri("data:image/png;base64,AAA"), Some(("image/png".into(), "AAA".into())));
    }
}
