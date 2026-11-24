//! Filter rules compatible with Bulwark (lib/sieve/generator.ts, parser.ts, period.ts,
//! vacation-forward.ts, vacation-audience.ts): rules are kept as JSON in a
//! `/* @metadata:begin ... @metadata:end */` block at the top of the Sieve script, followed by the
//! generated Sieve (vacation include / auto-reply / forwarding, then the rules) and, unchanged,
//! any rules other clients wrote.
use serde_json::{json, Value};

const BEGIN: &str = "/* @metadata:begin";
const END: &str = "@metadata:end */";
const EXTERNAL: &str = "# --- External rules (managed outside Bulwark) ---";
const VACATION_FORWARD_MARKER: &str = "# Vacation forwarding";
/// Metadata keys this editor regenerates.
const KNOWN_KEYS: [&str; 6] = ["version", "rules", "vacation", "includeVacation", "vacationForward", "vacationAudience"];

pub struct Parsed {
    /// The whole metadata object; save with `with_rules` so the extras are kept.
    pub metadata: Value,
    /// Everything that is not Bulwark's (other clients' rules), kept verbatim.
    pub external: String,
    /// The metadata is something Bulwark itself would take for a hand-edited script (unknown
    /// version or keys, a period or forward it cannot read): do not rewrite it.
    pub locked: bool,
}

pub fn parse(script: &str) -> Parsed {
    let Some(b) = script.find(BEGIN) else {
        // Stalwart writes a bare vacation script (no metadata): Bulwark reads it as the auto-reply
        if let Some(v) = vacation_only(script) {
            return Parsed { metadata: json!({"version": 1, "rules": [], "vacation": v}), external: String::new(), locked: false };
        }
        let ext = script.trim();
        return Parsed { metadata: json!({"version": 1, "rules": []}), external: strip_requires(ext), locked: false };
    };
    let e = script[b..].find(END).map(|i| b + i).unwrap_or(script.len());
    let json_txt = script[b + BEGIN.len()..e].replace("*\\/", "*/");
    let parsed: Option<Value> = serde_json::from_str(json_txt.trim()).ok();
    let rest = &script[(e + END.len()).min(script.len())..];
    let locked = parsed.as_ref().is_none_or(|m| !metadata_ok(m));
    let metadata = parsed.unwrap_or(json!({"version": 1, "rules": []}));
    let external = external_blocks(rest, &metadata);
    Parsed { metadata, external, locked }
}

/// Everything after the metadata that is not Bulwark's own output, as Bulwark's parser finds it:
/// every top-level block is scanned (not only what follows the External header), so rules
/// written by hand between the generated blocks survive a save. Blocks that are Bulwark's
/// ("# Rule: <name>" of a metadata rule, "# Vacation auto-reply", the vacation forwarding
/// block while it is on) are dropped; the require tokens are kept ("include" only when an
/// external block uses it).
fn external_blocks(rest: &str, meta: &Value) -> String {
    let rest: String = rest.lines().filter(|l| l.trim() != EXTERNAL).collect::<Vec<_>>().join("\n");
    let names: Vec<String> = meta["rules"].as_array().map(|a| a.iter().map(|r| collapse_ws(r["name"].as_str().unwrap_or("")).trim().to_string()).collect()).unwrap_or_default();
    let forward_on = meta["vacationForward"]["enabled"] == true;
    let mut requires: Vec<String> = vec![];
    let mut kept: Vec<String> = vec![];
    for b in scan_top_level(&rest) {
        if b.kind == "require" {
            for t in require_tokens(&b.statement) { if !requires.contains(&t) { requires.push(t); } }
            continue;
        }
        let raw = b.raw.as_str();
        let rule_name = raw.lines().find_map(|l| {
            let t = l.trim_start();
            let t = t.strip_prefix('#')?.trim_start();
            t.strip_prefix("Rule:").map(|n| collapse_ws(n).trim().to_string())
        });
        if rule_name.is_some_and(|n| names.contains(&n)) { continue; }
        if raw.lines().any(|l| l.trim_start().strip_prefix('#').is_some_and(|c| c.trim_start().to_lowercase().starts_with("vacation auto-reply"))) { continue; }
        if forward_on && raw.lines().any(|l| l.trim() == VACATION_FORWARD_MARKER) { continue; }
        kept.push(raw.trim().to_string());
    }
    if !kept.iter().any(|k| has_word(k, "include")) { requires.retain(|r| r != "include"); }
    let mut out = String::new();
    if !requires.is_empty() && !kept.is_empty() {
        out.push_str(&format!("require [{}];\n", requires.iter().map(|r| format!("\"{r}\"")).collect::<Vec<_>>().join(", ")));
    }
    out.push_str(&kept.join("\n\n"));
    out.trim().to_string()
}

/// A top-level statement of a Sieve script, with the comments before it (Bulwark scanTopLevel).
struct Block { kind: &'static str, raw: String, statement: String }

fn skip_string(s: &[u8], mut i: usize) -> usize {
    i += 1;
    while i < s.len() {
        if s[i] == b'\\' { i += 2; continue; }
        if s[i] == b'"' { return i + 1; }
        i += 1;
    }
    s.len()
}
fn skip_hash(s: &[u8], mut i: usize) -> usize { while i < s.len() && s[i] != b'\n' { i += 1; } i }
/// RFC 5228 multi-line string at `i` ("text:" ... a line holding only "."): its end, if one starts here.
fn skip_text(s: &[u8], i: usize) -> Option<usize> {
    if !s[i..].starts_with(b"text:") || (i > 0 && (s[i - 1].is_ascii_alphanumeric() || s[i - 1] == b'_')) { return None; }
    let mut j = i + 5;
    // rest of the "text:" line (spaces or a comment), then lines up to a lone "."
    while j < s.len() && s[j] != b'\n' { j += 1; }
    while j < s.len() {
        let line_start = j + 1;
        let mut k = line_start;
        while k < s.len() && s[k] != b'\n' { k += 1; }
        let line = &s[line_start.min(s.len())..k];
        let line = line.strip_suffix(b"\r").unwrap_or(line);
        if line == b"." { return Some(k); }
        j = k;
    }
    Some(s.len())
}
fn skip_block_comment(s: &[u8], i: usize) -> usize {
    (i + 2..s.len().saturating_sub(1)).find(|&j| s[j] == b'*' && s[j + 1] == b'/').map(|j| j + 2).unwrap_or(s.len())
}
fn skip_statement(s: &[u8], mut i: usize) -> usize {
    while i < s.len() {
        match s[i] {
            b'"' => { i = skip_string(s, i); continue; }
            b'#' => { i = skip_hash(s, i); continue; }
            b'/' if s.get(i + 1) == Some(&b'*') => { i = skip_block_comment(s, i); continue; }
            b't' if skip_text(s, i).is_some() => { i = skip_text(s, i).unwrap_or(s.len()); continue; }
            b';' => return i + 1,
            _ => i += 1,
        }
    }
    i
}
fn skip_balanced(s: &[u8], mut i: usize, open: u8, close: u8) -> usize {
    let mut depth = 0i32;
    while i < s.len() {
        let c = s[i];
        if c == b'"' { i = skip_string(s, i); continue; }
        if c == b'#' { i = skip_hash(s, i); continue; }
        if c == b'/' && s.get(i + 1) == Some(&b'*') { i = skip_block_comment(s, i); continue; }
        if let Some(j) = (c == b't').then(|| skip_text(s, i)).flatten() { i = j; continue; }
        i += 1;
        if c == open { depth += 1; } else if c == close { depth -= 1; if depth == 0 { return i; } }
    }
    i
}
fn skip_if(s: &[u8], mut i: usize) -> usize {
    while i < s.len() && s[i] != b'{' {
        match s[i] {
            b'"' => { i = skip_string(s, i); continue; }
            b'(' => { i = skip_balanced(s, i, b'(', b')'); continue; }
            b'#' => { i = skip_hash(s, i); continue; }
            b'/' if s.get(i + 1) == Some(&b'*') => { i = skip_block_comment(s, i); continue; }
            _ => i += 1,
        }
    }
    if i >= s.len() { return i; }
    skip_balanced(s, i, b'{', b'}')
}

/// An `if` with its `elsif` / `else` branches: one block.
fn skip_if_chain(s: &[u8], content: &str, i: usize) -> usize {
    let mut end = skip_if(s, i);
    loop {
        let mut j = end;
        while j < s.len() && s[j].is_ascii_whitespace() { j += 1; }
        let w = j;
        while j < s.len() && (s[j].is_ascii_alphanumeric() || s[j] == b'_') { j += 1; }
        match &content[w..j] {
            "elsif" => end = skip_if(s, j),
            "else" => {
                let mut k = j;
                while k < s.len() && s[k] != b'{' { k += 1; }
                if k >= s.len() { return end; }
                end = skip_balanced(s, k, b'{', b'}');
            }
            _ => return end,
        }
    }
}

fn scan_top_level(content: &str) -> Vec<Block> {
    let s = content.as_bytes();
    let mut blocks = vec![];
    let (mut i, mut seg) = (0usize, 0usize);
    while i < s.len() {
        while i < s.len() && s[i].is_ascii_whitespace() { i += 1; }
        if i >= s.len() { break; }
        if s[i] == b'#' { i = skip_hash(s, i); continue; }
        if s[i] == b'/' && s.get(i + 1) == Some(&b'*') { i = skip_block_comment(s, i); continue; }
        if !(s[i].is_ascii_alphabetic() || s[i] == b'_') { i += 1; continue; }
        let start = i;
        while i < s.len() && (s[i].is_ascii_alphanumeric() || s[i] == b'_') { i += 1; }
        let kind = match &content[start..i] { "require" => "require", "if" => "if", "vacation" => "vacation", _ => "other" };
        i = if kind == "if" { skip_if_chain(s, content, i) } else { skip_statement(s, i) };
        let end = i.min(s.len());
        blocks.push(Block { kind, raw: content[seg..end].to_string(), statement: content[start..end].to_string() });
        seg = end;
    }
    blocks
}

fn require_tokens(stmt: &str) -> Vec<String> {
    let body = stmt.trim_start().strip_prefix("require").unwrap_or("").trim().trim_end_matches(';').trim();
    let mut out = vec![];
    let mut rest = body;
    while let Some(a) = rest.find('"') {
        let Some(b) = rest[a + 1..].find('"') else { break };
        out.push(rest[a + 1..a + 1 + b].to_string());
        rest = &rest[a + 2 + b..];
    }
    out
}

/// `word` as a whole word (JS \bword\b).
fn has_word(s: &str, word: &str) -> bool {
    let w = |c: char| c.is_ascii_alphanumeric() || c == '_';
    s.match_indices(word).any(|(i, _)| !s[..i].chars().next_back().is_some_and(w) && !s[i + word.len()..].chars().next().is_some_and(w))
}

fn unescape_sieve(s: &str) -> String {
    let mut out = String::new();
    let mut it = s.chars();
    while let Some(c) = it.next() { if c == '\\' { if let Some(n) = it.next() { out.push(n); } } else { out.push(c); } }
    out
}

/// Quoted strings of a script: (start of the opening quote, raw inner text).
fn quoted(s: &str) -> Vec<(usize, String)> {
    let b = s.as_bytes();
    let mut out = vec![];
    let mut i = 0;
    while i < b.len() {
        if b[i] == b'"' {
            let end = skip_string(b, i);
            let close = if end <= b.len() && end > i + 1 && b[end - 1] == b'"' { end - 1 } else { end };
            out.push((i, s[i + 1..close.max(i + 1)].to_string()));
            i = end;
        } else { i += 1; }
    }
    out
}

/// Bulwark detectVacationOnlyScript: a script without metadata that is only a vacation action
/// (as Stalwart's own auto-reply writes it) reads as the auto-reply {isEnabled, subject, textBody}.
pub fn vacation_only(content: &str) -> Option<Value> {
    if !has_word(content, "vacation") { return None; }
    // drop require lines, then # and /* */ comments (as Bulwark's regexes do)
    let no_req: String = content.lines().filter(|l| {
        let t = l.trim();
        !(t.starts_with("require") && t[7..].trim_start().starts_with('[') && t.ends_with(';'))
    }).collect::<Vec<_>>().join("\n");
    let no_hash: String = no_req.lines().map(|l| l.split_once('#').map(|(a, _)| a).unwrap_or(l)).collect::<Vec<_>>().join("\n");
    let mut stripped = String::new();
    let mut rest = no_hash.as_str();
    while let Some(a) = rest.find("/*") {
        stripped.push_str(&rest[..a]);
        rest = match rest[a + 2..].find("*/") { Some(b) => &rest[a + 2 + b + 2..], None => { stripped.push_str(&rest[a..]); "" } };
    }
    stripped.push_str(rest);
    let stripped = stripped.trim().to_string();
    let strings = quoted(&stripped);
    let mut structural = String::new();
    let mut last = 0;
    for (start, inner) in &strings {
        structural.push_str(&stripped[last..*start]);
        structural.push_str("\"\"");
        last = (start + inner.len() + 2).min(stripped.len());
    }
    structural.push_str(&stripped[last.min(stripped.len())..]);
    if ["if", "elsif", "else"].iter().any(|w| has_word(&structural, w)) || !has_word(&structural, "vacation") { return None; }
    let subject = stripped.find(":subject").and_then(|i| {
        let after = stripped[i + 8..].trim_start();
        after.starts_with('"').then(|| quoted(after).first().map(|q| unescape_sieve(&q.1))).flatten()
    }).unwrap_or_default();
    // a MIME body: the text after the Content-Transfer-Encoding header's blank line
    let mime = stripped.find("Content-Transfer-Encoding:").and_then(|i| {
        let after = &stripped[i..];
        let nl = after.find('\n')?;
        let body = after[nl + 1..].strip_prefix("\r\n").or_else(|| after[nl + 1..].strip_prefix('\n'))?;
        let q = body.find('"')?;
        body[q..].contains(';').then(|| body[..q].trim().to_string())
    });
    let text = mime.unwrap_or_else(|| strings.last().map(|q| unescape_sieve(&q.1)).unwrap_or_default());
    Some(json!({"isEnabled": true, "subject": subject, "textBody": text}))
}

/// What Bulwark's parser accepts as its own metadata, limited to the keys this editor writes back.
fn metadata_ok(m: &Value) -> bool {
    let Some(o) = m.as_object() else { return false };
    if !(m["version"] == 1 || m["version"] == 2) { return false; }
    if o.keys().any(|k| !KNOWN_KEYS.contains(&k.as_str())) { return false; }
    let Some(rules) = m["rules"].as_array() else { return false };
    let bound_ok = |x: &Value, k: &str| x.get(k).is_none_or(|v| v.as_str().and_then(period_boundary).is_some());
    if !rules.iter().all(|r| bound_ok(r, "activeFrom") && bound_ok(r, "activeUntil")) { return false; }
    if m.get("vacationForward").is_some_and(|f| !valid_forward(f)) { return false; }
    if m.get("vacationAudience").is_some_and(|a| !valid_audience(a)) { return false; }
    m.get("vacation").is_none_or(|v| v.is_object())
}

fn strip_requires(s: &str) -> String {
    s.lines().filter(|l| !l.trim_start().starts_with("require ")).collect::<Vec<_>>().join("\n").trim().to_string()
}

fn esc(v: &str) -> String {
    v.replace('\\', "\\\\").replace('"', "\\\"")
}

fn values(v: &Value) -> Vec<String> {
    match v {
        Value::Array(a) => a.iter().filter_map(|x| x.as_str()).filter(|x| !x.is_empty()).map(String::from).collect(),
        Value::String(s) if !s.is_empty() => vec![s.clone()],
        _ => vec![],
    }
}

fn arg(vals: &[String], f: impl Fn(&str) -> String) -> String {
    if vals.len() == 1 { format!("\"{}\"", esc(&f(&vals[0]))) }
    else { format!("[{}]", vals.iter().map(|v| format!("\"{}\"", esc(&f(v)))).collect::<Vec<_>>().join(", ")) }
}

fn header_name(c: &Value) -> String {
    match c["field"].as_str().unwrap_or("from") {
        "from" => "From".into(), "to" => "To".into(), "cc" => "Cc".into(), "subject" => "Subject".into(),
        _ => c["headerName"].as_str().map(str::trim).filter(|x| !x.is_empty()).unwrap_or("X-Unknown").into(),
    }
}

fn condition(c: &Value, req: &mut Vec<&'static str>) -> String {
    let field = c["field"].as_str().unwrap_or("from");
    let cmp = c["comparator"].as_str().unwrap_or("contains");
    if field == "all" { return "true".into(); }
    if field == "size" {
        let raw = match &c["value"] { Value::Array(a) => a.first().cloned().unwrap_or(Value::Null), v => v.clone() };
        let raw = match raw { Value::String(s) => s, Value::Number(n) => n.to_string(), _ => String::new() };
        let raw = raw.trim();
        let digits = raw.trim_end_matches(['K', 'M', 'G', 'k', 'm', 'g']);
        let ok = !digits.is_empty() && digits.chars().all(|ch| ch.is_ascii_digit()) && raw.len() - digits.len() <= 1;
        return format!("size {} {}", if cmp == "greater_than" { ":over" } else { ":under" }, if ok { raw } else { "0" });
    }
    if cmp == "regex" { req.push("regex"); }
    let vals = values(&c["value"]);
    if field == "body" {
        req.push("body");
        let m = match cmp { "is" => ":is", "regex" => ":regex", _ => ":contains" };
        return format!("body {m} {}", arg(&vals, |s| s.into()));
    }
    if field == "attachment" {
        req.push("mime");
        if cmp == "has_any" { return r#"header :mime :anychild :contains "Content-Disposition" "attachment""#.into(); }
        let exts: Vec<String> = vals.iter().map(|v| v.trim_start_matches(['.', '*']).trim().to_string()).filter(|v| !v.is_empty()).collect();
        return format!(r#"header :mime :anychild :matches ["Content-Disposition", "Content-Type"] {}"#, arg(&exts, |e| format!("*.{e}*")));
    }
    let h = esc(&header_name(c));
    if matches!(cmp, "address_is" | "domain_is") && matches!(field, "from" | "to" | "cc") {
        return format!("address {}:is \"{h}\" {}", if cmp == "domain_is" { ":domain " } else { "" }, arg(&vals, |s| s.into()));
    }
    match cmp {
        "not_contains" => format!("not header :contains \"{h}\" {}", arg(&vals, |s| s.into())),
        "is" => format!("header :is \"{h}\" {}", arg(&vals, |s| s.into())),
        "not_is" => format!("not header :is \"{h}\" {}", arg(&vals, |s| s.into())),
        "starts_with" => format!("header :matches \"{h}\" {}", arg(&vals, |s| format!("{s}*"))),
        "ends_with" => format!("header :matches \"{h}\" {}", arg(&vals, |s| format!("*{s}"))),
        "matches" => format!("header :matches \"{h}\" {}", arg(&vals, |s| s.into())),
        "regex" => format!("header :regex \"{h}\" {}", arg(&vals, |s| s.into())),
        _ => format!("header :contains \"{h}\" {}", arg(&vals, |s| s.into())),
    }
}

fn actions(acts: &[Value], use_mbid: bool, req: &mut Vec<&'static str>) -> Vec<String> {
    let is_flag = |a: &Value| matches!(a["type"].as_str(), Some("mark_read" | "star" | "add_label"));
    let mut ordered: Vec<&Value> = acts.iter().filter(|a| is_flag(a)).collect();
    ordered.extend(acts.iter().filter(|a| !is_flag(a)));
    let target = |a: &Value, req: &mut Vec<&'static str>| {
        let path = format!("\"{}\"", esc(a["value"].as_str().unwrap_or("")));
        match a["mailboxId"].as_str().filter(|m| use_mbid && !m.is_empty()) {
            Some(id) => { req.push("mailbox"); req.push("mailboxid"); format!(":mailboxid \"{}\" {path}", esc(id)) }
            None => path,
        }
    };
    ordered.iter().filter_map(|a| {
        let v = esc(a["value"].as_str().unwrap_or(""));
        Some(match a["type"].as_str()? {
            "move" => { req.push("fileinto"); format!("fileinto {};", target(a, req)) }
            "copy" => { req.push("fileinto"); req.push("copy"); format!("fileinto :copy {};", target(a, req)) }
            "forward" => { if a["keepCopy"] == true { req.push("copy"); } format!("redirect {}\"{v}\";", if a["keepCopy"] == true { ":copy " } else { "" }) }
            "mark_read" => { req.push("imap4flags"); r#"addflag "\\Seen";"#.to_string() }
            "star" => { req.push("imap4flags"); r#"addflag "\\Flagged";"#.to_string() }
            "add_label" => { req.push("imap4flags"); format!("addflag \"$label:{v}\";") }
            "discard" => "discard;".to_string(),
            "reject" => { req.push("reject"); format!("reject \"{v}\";") }
            "keep" => { req.push("fileinto"); "fileinto \"INBOX\";".to_string() }
            "stop" => "stop;".to_string(),
            _ => return None,
        })
    }).collect()
}

const SPAM_GUARD: &str = r#"not spamtest :percent :value "ge" :comparator "i;ascii-numeric" "50""#;
const SPAM_GUARD_REQUIRES: [&str; 3] = ["spamtestplus", "relational", "comparator-i;ascii-numeric"];
/// Whether the server lists the Sieve extension `name` (JMAP `sieveExtensions`, lowercase).
pub fn server_has(ext: &[String], name: &str) -> bool { ext.iter().any(|x| x == name) }

/// Whether the rule editor offers action type `kind` on this server. `reject` is offered only
/// when the server lists the reject extension (G-020; Bulwark's filter-rule-modal.tsx offers it
/// unconditionally). Existing rules keep their actions either way: see `unsupported_actions`.
pub fn action_offered(kind: &str, ext: &[String]) -> bool { kind != "reject" || server_has(ext, "reject") }

/// Action types a rule uses that this server does not list (the rule still shows and saves as is).
pub fn unsupported_actions(rule: &Value, ext: &[String]) -> Vec<String> {
    let mut out: Vec<String> = vec![];
    for a in rule["actions"].as_array().into_iter().flatten() {
        let kind = a["type"].as_str().unwrap_or("");
        if !action_offered(kind, ext) && !out.iter().any(|k| k == kind) { out.push(kind.to_string()); }
    }
    out
}

/// Sieve extensions a period needs (Bulwark period.ts PERIOD_REQUIRES).
pub const PERIOD_REQUIRES: [&str; 2] = ["date", "relational"];

/// A period boundary as Bulwark accepts it (period.ts isPeriodBoundary: ISO 8601 with its zone),
/// returned as the RFC 5260 "date" and "time" parts in UTC.
pub fn period_boundary(v: &str) -> Option<(String, String)> {
    let b = v.as_bytes();
    let dig = |r: std::ops::Range<usize>| r.into_iter().all(|i| b.get(i).is_some_and(u8::is_ascii_digit));
    if !(dig(0..4) && b.get(4) == Some(&b'-') && dig(5..7) && b.get(7) == Some(&b'-') && dig(8..10) && b.get(10) == Some(&b'T')
        && dig(11..13) && b.get(13) == Some(&b':') && dig(14..16)) { return None; }
    let mut i = 16;
    let has_secs = b.get(16) == Some(&b':');
    if has_secs {
        if !dig(17..19) { return None; }
        i = 19;
        if b.get(i) == Some(&b'.') {
            let n = b[i + 1..].iter().take_while(|c| c.is_ascii_digit()).count();
            if !(1..=3).contains(&n) { return None; }
            i += 1 + n;
        }
    }
    let zone = &v[i..];
    let z = zone.as_bytes();
    let zone_ok = zone == "Z" || (z.len() == 6 && (z[0] == b'+' || z[0] == b'-') && z[1..3].iter().all(u8::is_ascii_digit)
        && z[3] == b':' && z[4..6].iter().all(u8::is_ascii_digit));
    if !zone_ok { return None; }
    let full = if has_secs { v.to_string() } else { format!("{}:00{zone}", &v[..16]) };
    let t = chrono::DateTime::parse_from_rfc3339(&full).ok()?.with_timezone(&chrono::Utc);
    let iso = t.format("%Y-%m-%dT%H:%M:%S").to_string();
    // The four-digit year of Sieve's "date" part.
    if iso.len() != 19 || !iso.as_bytes()[..4].iter().all(u8::is_ascii_digit) { return None; }
    Some((iso[..10].to_string(), iso[11..19].to_string()))
}

const NOW: &str = r#"currentdate :zone "+0000""#;

/// The tests confining a rule (or the vacation forward) to its period: none without one, None
/// when a boundary is unusable (Bulwark periodTests).
pub fn period_tests(x: &Value) -> Option<Vec<String>> {
    let mut t = vec![];
    if let Some(f) = x.get("activeFrom") {
        let (d, tm) = period_boundary(f.as_str()?)?;
        t.push(format!(r#"anyof({NOW} :value "gt" "date" "{d}", allof({NOW} :is "date" "{d}", {NOW} :value "ge" "time" "{tm}"))"#));
    }
    if let Some(u) = x.get("activeUntil") {
        let (d, tm) = period_boundary(u.as_str()?)?;
        t.push(format!(r#"anyof({NOW} :value "lt" "date" "{d}", allof({NOW} :is "date" "{d}", {NOW} :value "le" "time" "{tm}"))"#));
    }
    Some(t)
}

fn has_period(x: &Value) -> bool { x.get("activeFrom").is_some() || x.get("activeUntil").is_some() }

/// Bulwark lib/validation.ts isValidEmail.
fn valid_email(e: &str) -> bool {
    if e.is_empty() || e.len() > 254 || e.contains(['\r', '\n', '\0', '<', '>']) { return false; }
    let Some((l, d)) = e.split_once('@') else { return false };
    let label = |s: &str| !s.is_empty() && s.len() <= 63 && s.chars().all(|c| c.is_ascii_alphanumeric() || c == '-') && !s.starts_with('-') && !s.ends_with('-');
    !l.is_empty() && l.chars().all(|c| c.is_ascii_alphanumeric() || ".!#$%&'*+/=?^_`{|}~-".contains(c)) && d.split('.').all(label)
}

fn valid_domain(d: &str) -> bool {
    let labels: Vec<&str> = d.split('.').collect();
    labels.len() >= 2 && labels.iter().all(|l| !l.is_empty() && l.chars().all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || c == '-')
        && !l.starts_with('-') && !l.ends_with('-'))
}

/// Bulwark isValidVacationForward.
pub fn valid_forward(f: &Value) -> bool {
    let b = |k: &str| f.get(k).is_none_or(|v| v.as_str().and_then(period_boundary).is_some());
    f.is_object() && f["enabled"].is_boolean() && f["keepCopy"].is_boolean() && f["to"].as_str().is_some_and(valid_email) && b("activeFrom") && b("activeUntil")
}

/// Bulwark isValidVacationAudience.
pub fn valid_audience(a: &Value) -> bool {
    matches!(a["only"].as_str(), Some("internal" | "external"))
        && a["domains"].as_array().is_some_and(|d| !d.is_empty() && d.iter().all(|x| x.as_str().is_some_and(valid_domain)))
}

fn pick(v: &Value, keys: &[&str]) -> Value {
    Value::Object(keys.iter().filter_map(|k| v.get(*k).map(|x| (k.to_string(), x.clone()))).collect())
}

/// Build the whole script from rules only.
pub fn generate(rules: &[Value], external: &str) -> String {
    generate_meta(&json!({"version": 1, "rules": rules}), external, &[])
}

/// `meta` with its rules replaced and everything else (vacation, forward, audience) kept.
pub fn with_rules(meta: &Value, rules: &[Value]) -> Value {
    let mut m = if meta.is_object() { meta.clone() } else { json!({"version": 1}) };
    m["rules"] = json!(rules);
    m
}

/// Bulwark generateScript: metadata, require line, vacation include, auto-reply, vacation
/// forwarding, rules (spam guard, active periods), then the external rules verbatim.
/// `server_ext` is the server's sieveExtensions (spam guard, `:mailboxid`).
pub fn generate_meta(meta: &Value, external: &str, server_ext: &[String]) -> String {
    let has_ext = |e: &str| server_ext.iter().any(|x| x == e);
    let guard_ok = has_ext("spamtestplus") && has_ext("relational");
    let use_mbid = has_ext("mailboxid");
    let rules: Vec<Value> = meta["rules"].as_array().cloned().unwrap_or_default();
    let vacation = meta.get("vacation").filter(|v| v["isEnabled"] == true);
    let include_vac = meta["includeVacation"] == true;
    let forward = meta.get("vacationForward").filter(|f| valid_forward(f)).map(|f| pick(f, &["enabled", "to", "keepCopy", "activeFrom", "activeUntil"]));
    let audience = meta.get("vacationAudience").filter(|a| valid_audience(a)).map(|a| pick(a, &["only", "domains"]));

    let uses_regex = |r: &Value| r["conditions"].as_array().is_some_and(|c| c.iter().any(|c| c["comparator"] == "regex" && matches!(c["field"].as_str(), Some("from" | "to" | "cc" | "subject" | "header" | "body"))));
    let v2 = rules.iter().any(has_period) || rules.iter().any(uses_regex) || forward.as_ref().is_some_and(|f| f["enabled"] == true) || audience.is_some();
    let mut m = serde_json::Map::new();
    m.insert("version".into(), json!(if v2 { 2 } else { 1 }));
    m.insert("rules".into(), json!(rules));
    if let Some(v) = vacation { m.insert("vacation".into(), v.clone()); }
    if include_vac { m.insert("includeVacation".into(), json!(true)); }
    if let Some(f) = &forward { m.insert("vacationForward".into(), f.clone()); }
    if let Some(a) = &audience { m.insert("vacationAudience".into(), a.clone()); }
    let meta_txt = Value::Object(m).to_string().replace("*/", "*\\/");

    let mut req: Vec<&'static str> = vec![];
    if vacation.is_some() { req.push("vacation"); }
    let mut rule_lines: Vec<String> = vec![];
    for r in rules.iter().filter(|r| r["enabled"] != false) {
        let conds_v = r["conditions"].as_array().cloned().unwrap_or_default();
        let acts_v = r["actions"].as_array().cloned().unwrap_or_default();
        let all = r["matchType"] != "any";
        let guarded = guard_ok && r["includeSpam"] != true && acts_v.iter().any(|x| matches!(x["type"].as_str(), Some("move" | "copy")));
        if guarded { req.extend(SPAM_GUARD_REQUIRES); }
        let period = period_tests(r);
        if period.as_ref().is_some_and(|p| !p.is_empty()) { req.extend(PERIOD_REQUIRES); }
        let conds: Vec<String> = conds_v.iter().map(|c| condition(c, &mut req)).collect();
        let mut acts = actions(&acts_v, use_mbid, &mut req);
        if conds.is_empty() || acts_v.is_empty() { continue; }
        let bad_header = conds_v.iter().filter(|c| c["field"] == "header").map(header_name)
            .any(|h| h.is_empty() || !h.chars().all(|ch| ch.is_ascii_alphanumeric() || ch == '_' || ch == '-'));
        if bad_header { continue; }
        let Some(period) = period else { continue };
        let mut test = if conds.len() == 1 { conds[0].clone() } else { format!("{}({})", if all { "allof" } else { "anyof" }, conds.join(", ")) };
        if guarded {
            test = if conds.len() > 1 && all { format!("allof({}, {SPAM_GUARD})", conds.join(", ")) } else { format!("allof({test}, {SPAM_GUARD})") };
        }
        if !period.is_empty() { test = format!("allof({}, {test})", period.join(", ")); }
        if r["stopProcessing"] == true && !acts.iter().any(|a| a == "stop;") { acts.push("stop;".into()); }
        rule_lines.push(String::new());
        rule_lines.push(format!("# Rule: {}", collapse_ws(r["name"].as_str().unwrap_or(""))));
        rule_lines.push(format!("if {test} {{"));
        rule_lines.extend(acts.iter().map(|a| format!("    {a}")));
        rule_lines.push("}".into());
    }
    if include_vac { req.push("include"); }
    if include_vac && audience.is_some() { req.push("envelope"); }
    let forward = forward.filter(|f| f["enabled"] == true);
    let forward_period = forward.as_ref().and_then(period_tests);
    if let (Some(f), Some(p)) = (&forward, &forward_period) {
        if f["keepCopy"] == true { req.push("copy"); }
        if !p.is_empty() { req.extend(PERIOD_REQUIRES); }
        if guard_ok { req.extend(SPAM_GUARD_REQUIRES); }
    }
    let ext_req: Vec<String> = external.lines().filter_map(|l| l.trim().strip_prefix("require "))
        .flat_map(|x| x.trim_end_matches(';').trim().trim_matches(['[', ']']).split(',').map(|s| s.trim().trim_matches('"').to_string()).collect::<Vec<_>>()).collect();
    let mut all_req: Vec<String> = req.iter().map(|s| s.to_string()).chain(ext_req).filter(|s| !s.is_empty()).collect();
    all_req.sort();
    all_req.dedup();

    let mut lines: Vec<String> = vec![BEGIN.into(), meta_txt, END.into(), String::new()];
    if !all_req.is_empty() { lines.push(format!("require [{}];", all_req.iter().map(|s| format!("\"{s}\"")).collect::<Vec<_>>().join(", "))); }
    if include_vac {
        lines.push(String::new());
        lines.push("# Vacation auto-reply".into());
        let inc = r#"include :personal :optional "vacation";"#;
        match &audience {
            Some(a) => {
                let doms = a["domains"].as_array().map(|d| d.iter().map(|x| format!("\"{}\"", esc(x.as_str().unwrap_or("")))).collect::<Vec<_>>().join(", ")).unwrap_or_default();
                lines.push(format!("if {}envelope :domain :is \"from\" [{doms}] {{", if a["only"] == "external" { "not " } else { "" }));
                lines.push(format!("    {inc}"));
                lines.push("}".into());
            }
            None => lines.push(inc.into()),
        }
    }
    if let Some(v) = vacation {
        lines.push(String::new());
        lines.push("# Vacation auto-reply".into());
        let subject = v["subject"].as_str().filter(|s| !s.is_empty()).map(|s| format!(":subject \"{}\" ", esc(s))).unwrap_or_default();
        lines.push(format!("vacation {subject}\"{}\";", esc(v["textBody"].as_str().unwrap_or(""))));
    }
    if let (Some(f), Some(mut tests)) = (&forward, forward_period) {
        if guard_ok { tests.push(SPAM_GUARD.into()); }
        let c = match tests.len() { 0 => "true".to_string(), 1 => tests.remove(0), _ => format!("allof({})", tests.join(", ")) };
        let kc = f["keepCopy"] == true;
        lines.push(String::new());
        lines.push(VACATION_FORWARD_MARKER.into());
        lines.push(format!("if {c} {{"));
        lines.push(format!("    redirect {}\"{}\";", if kc { ":copy " } else { "" }, esc(f["to"].as_str().unwrap_or(""))));
        if !kc { lines.push("    stop;".into()); }
        lines.push("}".into());
    }
    lines.extend(rule_lines);
    let ext = strip_requires(external);
    if !ext.is_empty() {
        lines.push(String::new());
        lines.push(EXTERNAL.into());
        lines.push(ext);
    }
    lines.push(String::new());
    lines.join("\n")
}

/// JS `name.replace(/\s+/g, ' ')`.
fn collapse_ws(s: &str) -> String {
    let mut out = String::new();
    let mut ws = false;
    for c in s.chars() {
        if c.is_whitespace() { if !ws { out.push(' '); } ws = true; } else { out.push(c); ws = false; }
    }
    out
}

/// A Bulwark-shaped rule from the simple form.
pub fn rule(name: &str, field: &str, cmp: &str, value: &str, action: &str, action_value: &str, stop: bool) -> Value {
    let mut b = [0u8; 6];
    let _ = getrandom::getrandom(&mut b);
    let id: String = b.iter().map(|x| format!("{x:02x}")).collect();
    let (cmp, value) = if field == "attachment" { ("has_any", String::new()) } else if field == "all" { ("any", String::new()) } else { (cmp, value.to_string()) };
    let act = if action_value.trim().is_empty() { json!({"type": action}) } else { json!({"type": action, "value": action_value.trim()}) };
    json!({"id": format!("bw-{id}"), "name": if name.trim().is_empty() { "Rule" } else { name.trim() }, "enabled": true, "matchType": "all",
        "conditions": [{"field": field, "comparator": cmp, "value": value}], "actions": [act], "stopProcessing": stop})
}

/// A form date (YYYY-MM-DD, optionally with " HH:MM" or "THH:MM") as Bulwark's period fields
/// store it: the wall-clock moment in the local zone as ISO UTC (`toISOString`). A bare date
/// means 00:00 for the start and 23:59 for the end. Empty gives None; bad input Err.
pub fn day_boundary(day: &str, end: bool) -> Result<Option<String>, String> {
    use chrono::TimeZone;
    let day = day.trim();
    if day.is_empty() { return Ok(None); }
    let bad = || format!("\"{day}\" is not a date (YYYY-MM-DD).");
    let t = match chrono::NaiveDateTime::parse_from_str(&day.replacen(' ', "T", 1), "%Y-%m-%dT%H:%M") {
        Ok(t) => t,
        Err(_) => {
            let d = chrono::NaiveDate::parse_from_str(day, "%Y-%m-%d").map_err(|_| bad())?;
            if end { d.and_hms_opt(23, 59, 0) } else { d.and_hms_opt(0, 0, 0) }.ok_or_else(bad)?
        }
    };
    let local = chrono::Local.from_local_datetime(&t).earliest().ok_or("bad date")?;
    Ok(Some(local.with_timezone(&chrono::Utc).format("%Y-%m-%dT%H:%M:%S%.3fZ").to_string()))
}

#[cfg(test)]
mod tests {
    use super::{action_offered, server_has, unsupported_actions};
    use serde_json::json;

    const FROM_T: &str = r#"anyof(currentdate :zone "+0000" :value "gt" "date" "2026-10-05", allof(currentdate :zone "+0000" :is "date" "2026-10-05", currentdate :zone "+0000" :value "ge" "time" "06:00:00"))"#;
    const UNTIL_T: &str = r#"anyof(currentdate :zone "+0000" :value "lt" "date" "2026-10-16", allof(currentdate :zone "+0000" :is "date" "2026-10-16", currentdate :zone "+0000" :value "le" "time" "16:00:00"))"#;

    #[test]
    fn reject_needs_the_reject_extension_and_existing_rules_keep_it() {
        let listed = vec!["fileinto".to_string(), "reject".to_string()];
        let none = vec!["fileinto".to_string(), "ereject".to_string()];
        assert!(server_has(&listed, "reject") && !server_has(&none, "reject"));
        assert!(action_offered("move", &none) && action_offered("reject", &listed));
        // ereject alone does not make reject offered: the generator only writes reject
        assert!(!action_offered("reject", &none));
        let rule = json!({"name": "R", "conditions": [], "actions": [{"type": "move", "value": "A"}, {"type": "reject", "value": "No"}, {"type": "reject", "value": "No again"}]});
        assert_eq!(unsupported_actions(&rule, &none), vec!["reject".to_string()]);
        assert!(unsupported_actions(&rule, &listed).is_empty());
        assert!(unsupported_actions(&json!({"name": "M"}), &none).is_empty());
    }

    #[test]
    fn external_keeps_elsif_else_and_text_strings() {
        let ext = "if header :contains \"subject\" \"a\" { fileinto \"A\"; } elsif header :contains \"subject\" \"b\" { fileinto \"B\"; } else { keep; }\nreject text:\nNo; thanks.\n# not a comment\n.\n;\nkeep;";
        let script = format!("/* @metadata:begin\n{{\"version\":1,\"rules\":[]}}\n@metadata:end */\n{}\n{}", super::EXTERNAL, ext);
        let p = super::parse(&script);
        assert!(p.external.contains("else { keep; }"), "{}", p.external);
        assert!(p.external.contains("reject text:\nNo; thanks.\n# not a comment\n.\n;"), "{}", p.external);
        let blocks = super::scan_top_level(ext);
        assert_eq!(blocks.len(), 3);
        assert!(blocks[0].raw.trim_end().ends_with("else { keep; }"));
    }

    #[test]
    fn vacation_only_scripts() {
        let s = "require [\"vacation\"];\n# auto\nvacation :days 1 :subject \"Away \\\"now\\\"\" \"I am out.\";\n";
        let p = super::parse(s);
        assert_eq!(p.metadata["vacation"], json!({"isEnabled": true, "subject": "Away \"now\"", "textBody": "I am out."}));
        assert!(p.external.is_empty() && !p.locked);
        // with a test it is a rule, not the auto-reply
        assert!(super::vacation_only("if true { vacation \"x\"; }").is_none());
        // a word "if" inside a string does not count
        assert_eq!(super::vacation_only("vacation \"if you need me\";").unwrap()["textBody"], "if you need me");
        assert!(super::vacation_only("keep;").is_none());
    }

    #[test]
    fn hand_written_blocks_between_generated_ones_survive() {
        let meta = json!({"version": 1, "rules": [super::rule("News", "from", "contains", "n@", "move", "N", false)], "vacation": {"isEnabled": true, "subject": "", "textBody": "away"}});
        let g = super::generate_meta(&meta, "", &[]);
        // someone adds a block between the vacation and the rules, and one at the end
        let edited = g.replacen("\n# Rule: News", "\n# mine\nif header :contains \"subject\" \"x\" { discard; }\n\n# Rule: News", 1)
            + "\nif true { keep; }\n";
        let p = super::parse(&edited);
        assert!(p.external.contains("# mine\nif header :contains \"subject\" \"x\" { discard; }"), "{}", p.external);
        assert!(p.external.contains("if true { keep; }"));
        assert!(!p.external.contains("# Rule: News") && !p.external.contains("Vacation auto-reply"));
        // saving again keeps them exactly once
        let again = super::generate_meta(&p.metadata, &p.external, &[]);
        assert_eq!(again.matches("# mine").count(), 1);
        assert_eq!(super::parse(&again).external, p.external);
    }

    #[test]
    fn roundtrip_matches_bulwark_shape() {
        let r = super::rule("News", "from", "contains", "newsletter@", "move", "Newsletters", false);
        let s = super::generate(&[r], "# rule:[ext]\nif true { keep; }");
        assert!(s.starts_with("/* @metadata:begin\n{") && s.contains("\"version\":1") && s.contains("@metadata:end */"));
        assert!(s.contains("require [\"fileinto\"];"));
        assert!(s.contains("if header :contains \"From\" \"newsletter@\" {\n    fileinto \"Newsletters\";\n}"));
        assert!(s.contains("# --- External rules (managed outside Bulwark) ---"));
        let p = super::parse(&s);
        assert_eq!(p.metadata["rules"][0]["name"], "News");
        assert!(p.external.contains("# rule:[ext]"));
        assert!(!p.locked);
        let ext = vec!["spamtestplus".to_string(), "relational".to_string()];
        let g = super::generate_meta(&json!({"version": 1, "rules": [super::rule("N", "from", "contains", "x", "move", "F", false)]}), "", &ext);
        assert!(g.contains(r#"if allof(header :contains "From" "x", not spamtest :percent :value "ge" :comparator "i;ascii-numeric" "50")"#), "{g}");
        assert!(g.contains("\"comparator-i;ascii-numeric\""));
    }

    // period.test.ts 'writes the start and end as UTC date and time tests'
    #[test]
    fn period_tests_match_bulwark() {
        let t = super::period_tests(&json!({"activeFrom": "2026-10-05T08:00:00+02:00", "activeUntil": "2026-10-16T16:00:00.000Z"})).unwrap();
        assert_eq!(t, vec![FROM_T.to_string(), UNTIL_T.to_string()]);
        assert_eq!(super::period_tests(&json!({})).unwrap().len(), 0);
        assert!(super::period_tests(&json!({"activeFrom": "2026-10-05T08:00"})).is_none());
        assert!(super::period_tests(&json!({"activeFrom": "2026-10-05T06:00:00Z", "activeUntil": "soon"})).is_none());
        assert!(super::period_boundary("2026-10-05T06:00Z").is_some());
    }

    // Expected text written from generator.ts / period.test.ts ('confines the rule to its period').
    #[test]
    fn rule_with_period_matches_bulwark() {
        let rule = json!({"id": "r1", "name": "Fwd", "enabled": true, "matchType": "all",
            "conditions": [{"field": "from", "comparator": "contains", "value": "@"}],
            "actions": [{"type": "forward", "value": "kollege@example.com"}, {"type": "discard"}],
            "stopProcessing": false, "activeFrom": "2026-10-05T06:00:00.000Z", "activeUntil": "2026-10-16T16:00:00.000Z"});
        let got = super::generate_meta(&json!({"version": 1, "rules": [rule]}), "", &[]);
        let want = format!("/* @metadata:begin\n{}\n@metadata:end */\n\nrequire [\"date\", \"relational\"];\n\n# Rule: Fwd\nif allof({FROM_T}, {UNTIL_T}, header :contains \"From\" \"@\") {{\n    redirect \"kollege@example.com\";\n    discard;\n}}\n",
            r#"{"version":2,"rules":[{"id":"r1","name":"Fwd","enabled":true,"matchType":"all","conditions":[{"field":"from","comparator":"contains","value":"@"}],"actions":[{"type":"forward","value":"kollege@example.com"},{"type":"discard"}],"stopProcessing":false,"activeFrom":"2026-10-05T06:00:00.000Z","activeUntil":"2026-10-16T16:00:00.000Z"}]}"#);
        assert_eq!(got, want);
        let p = super::parse(&got);
        assert!(!p.locked);
        assert_eq!(super::generate_meta(&p.metadata, &p.external, &[]), got);
        // spam guard inside the period (period.test.ts 'keeps the conditions and the spam guard intact')
        let ext: Vec<String> = ["fileinto", "mailbox", "mailboxid", "spamtestplus", "relational", "comparator-i;ascii-numeric", "date", "copy"].iter().map(|s| s.to_string()).collect();
        let any = json!({"id": "r1", "name": "Fwd", "enabled": true, "matchType": "any",
            "conditions": [{"field": "from", "comparator": "contains", "value": "a"}, {"field": "subject", "comparator": "contains", "value": ["b", "c"]}],
            "actions": [{"type": "move", "value": "F", "mailboxId": "mb"}], "stopProcessing": false,
            "activeFrom": "2026-10-05T06:00:00.000Z", "activeUntil": "2026-10-16T16:00:00.000Z"});
        let g = super::generate_meta(&json!({"version": 1, "rules": [any]}), "", &ext);
        assert!(g.contains(&format!(r#"if allof({FROM_T}, {UNTIL_T}, allof(anyof(header :contains "From" "a", header :contains "Subject" ["b", "c"]), not spamtest :percent :value "ge" :comparator "i;ascii-numeric" "50")) {{"#)), "{g}");
        assert!(g.contains(r#"require ["comparator-i;ascii-numeric", "date", "fileinto", "mailbox", "mailboxid", "relational", "spamtestplus"];"#), "{g}");
        assert!(g.contains(r#"    fileinto :mailboxid "mb" "F";"#));
    }

    // generator-compat.test.ts 'reads version 2 back as its own' plus an enabled auto-reply.
    #[test]
    fn vacation_audience_forward_match_bulwark() {
        let meta = json!({"version": 1, "rules": [],
            "vacation": {"isEnabled": true, "subject": "Away", "textBody": "Back \"soon\""},
            "includeVacation": true,
            "vacationForward": {"enabled": true, "to": "kollege@example.com", "keepCopy": true,
                "activeFrom": "2026-10-05T06:00:00.000Z", "activeUntil": "2026-10-16T16:00:00.000Z"},
            "vacationAudience": {"only": "external", "domains": ["example.com"]}});
        let got = super::generate_meta(&meta, "", &[]);
        let want = format!(concat!(
            "/* @metadata:begin\n",
            r#"{{"version":2,"rules":[],"vacation":{{"isEnabled":true,"subject":"Away","textBody":"Back \"soon\""}},"includeVacation":true,"vacationForward":{{"enabled":true,"to":"kollege@example.com","keepCopy":true,"activeFrom":"2026-10-05T06:00:00.000Z","activeUntil":"2026-10-16T16:00:00.000Z"}},"vacationAudience":{{"only":"external","domains":["example.com"]}}}}"#,
            "\n@metadata:end */\n\n",
            "require [\"copy\", \"date\", \"envelope\", \"include\", \"relational\", \"vacation\"];\n\n",
            "# Vacation auto-reply\n",
            "if not envelope :domain :is \"from\" [\"example.com\"] {{\n    include :personal :optional \"vacation\";\n}}\n\n",
            "# Vacation auto-reply\n",
            "vacation :subject \"Away\" \"Back \\\"soon\\\"\";\n\n",
            "# Vacation forwarding\n",
            "if allof({}, {}) {{\n    redirect :copy \"kollege@example.com\";\n}}\n"), FROM_T, UNTIL_T);
        assert_eq!(got, want);
        let p = super::parse(&got);
        assert!(!p.locked);
        let mut rules = p.metadata["rules"].as_array().cloned().unwrap();
        assert_eq!(super::generate_meta(&super::with_rules(&p.metadata, &rules), &p.external, &[]), got);
        // adding a rule keeps the vacation extras
        rules.push(super::rule("N", "from", "contains", "x", "move", "F", false));
        let g = super::generate_meta(&super::with_rules(&p.metadata, &rules), &p.external, &[]);
        assert!(g.contains("\"vacationAudience\"") && g.contains("# Vacation forwarding") && g.contains("# Rule: N"));
        // forward without copy, no period, spam guard: stop after redirect
        let ext = vec!["spamtestplus".to_string(), "relational".to_string()];
        let g = super::generate_meta(&json!({"version": 1, "rules": [], "vacationForward": {"enabled": true, "to": "a@b.cd", "keepCopy": false}}), "", &ext);
        assert!(g.contains(r#"require ["comparator-i;ascii-numeric", "relational", "spamtestplus"];"#), "{g}");
        assert!(g.contains("# Vacation forwarding\nif not spamtest :percent :value \"ge\" :comparator \"i;ascii-numeric\" \"50\" {\n    redirect \"a@b.cd\";\n    stop;\n}\n"), "{g}");
        // a forward that is off keeps version 1 and writes no block
        let g = super::generate_meta(&json!({"version": 1, "rules": [], "vacationForward": {"enabled": false, "to": "a@b.cd", "keepCopy": false}}), "", &[]);
        assert!(g.contains("{\"version\":1,") && !g.contains("# Vacation forwarding"));
    }

    #[test]
    fn unknown_or_bad_extras_stay_locked() {
        let s = |m: serde_json::Value| format!("/* @metadata:begin\n{m}\n@metadata:end */\n");
        assert!(super::parse(&s(json!({"version": 3, "rules": []}))).locked);
        assert!(super::parse(&s(json!({"version": 1, "rules": [], "other": 1}))).locked);
        assert!(super::parse(&s(json!({"version": 2, "rules": [{"activeFrom": "soon"}]}))).locked);
        assert!(super::parse(&s(json!({"version": 2, "rules": [], "vacationAudience": {"only": "x", "domains": []}}))).locked);
        assert!(!super::parse(&s(json!({"version": 2, "rules": [], "vacationAudience": {"only": "internal", "domains": ["a.de"]}}))).locked);
    }

    #[test]
    fn form_days_become_utc_boundaries() {
        let f = super::day_boundary("2026-10-05", false).unwrap().unwrap();
        assert!(super::period_boundary(&f).is_some() && f.ends_with(".000Z"));
        assert!(super::day_boundary("2026-10-05", true).unwrap().unwrap().ends_with(":00.000Z"));
        assert_eq!(super::day_boundary("", false).unwrap(), None);
        assert!(super::day_boundary("5.10.2026", false).is_err());
    }
}
