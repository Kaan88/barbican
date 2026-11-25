//! Out of office, as Bulwark stores it, so a reply set up in one app shows in the other.
//!
//! * The reply itself is the server's `VacationResponse` (subject, text and HTML body, dates).
//! * Who gets it (`vacationAudience`), forwarding while away (`vacationForward`, with "keep a copy
//!   in the Inbox" as `keepCopy`) and the include of the server's vacation script live in the
//!   metadata and the generated Sieve of the filters script (Bulwark stores/vacation-store.ts,
//!   stores/filter-store.ts readVacationFilters / syncVacationWithFilters,
//!   lib/sieve/vacation-audience.ts, lib/sieve/vacation-forward.ts; the Sieve itself is
//!   `sieve::generate_meta`).
//! * "Block my calendar": one busy event with the UID `bulwark-ooo-<accountId>` (Bulwark
//!   lib/vacation-calendar.ts). Everything that decides or formats is pure and tested here;
//!   the JMAP calls are thin.
use crate::jmap::Client;
use crate::sieve;
use chrono::{DateTime, Duration, NaiveDate, NaiveDateTime, NaiveTime, TimeZone, Utc};
use chrono_tz::Tz;
use serde_json::{json, Value};

/// The name of the script Stalwart manages for VacationResponse (Bulwark VACATION_SCRIPT_NAME).
pub const VACATION_SCRIPT_NAME: &str = "vacation";
/// Stalwart refuses a subject of 512 bytes or more and a body of 2048 or more (vacation-settings.tsx).
pub const SUBJECT_MAX: usize = 511;
pub const BODY_MAX: usize = 2047;

// ---------------------------------------------------------------------------------------------
// audience and forwarding values
// ---------------------------------------------------------------------------------------------

/// The domains of the given addresses, lower-cased, each once, in order (vacation-audience.ts ownDomains).
pub fn own_domains(addresses: &[String]) -> Vec<String> {
    let mut out: Vec<String> = vec![];
    for a in addresses {
        let Some((_, d)) = a.rsplit_once('@') else { continue };
        let d = d.trim().to_lowercase();
        if sieve::valid_domain(&d) && !out.contains(&d) { out.push(d); }
    }
    out
}

/// The audience a choice stores: `None` for everyone; `Some` for the account's own domains only
/// (`1`, "internal") or everyone else (`2`, "external"). Bulwark stores `{only, domains}`.
pub fn audience_for(choice: i32, domains: &[String]) -> Option<Value> {
    let only = match choice { 1 => "internal", 2 => "external", _ => return None };
    if domains.is_empty() { return None; }
    Some(json!({"only": only, "domains": domains}))
}

/// The select index of a stored audience (0 everyone, 1 internal, 2 external).
pub fn audience_choice(a: Option<&Value>) -> i32 {
    match a.and_then(|a| a["only"].as_str()) { Some("internal") => 1, Some("external") => 2, _ => 0 }
}

/// A forwarding with the vacation's period: it runs while the reply does (an empty date leaves
/// that end open) (vacation-forward.ts withVacationPeriod).
pub fn with_period(forward: &Value, from: Option<&str>, until: Option<&str>) -> Value {
    let mut m = serde_json::Map::new();
    m.insert("enabled".into(), forward["enabled"].clone());
    m.insert("to".into(), forward["to"].clone());
    m.insert("keepCopy".into(), forward["keepCopy"].clone());
    if let Some(f) = from.filter(|s| !s.is_empty()) { m.insert("activeFrom".into(), json!(f)); }
    if let Some(u) = until.filter(|s| !s.is_empty()) { m.insert("activeUntil".into(), json!(u)); }
    Value::Object(m)
}

fn same_moment(a: Option<&str>, b: Option<&str>) -> bool {
    match (a, b) {
        (None, None) => true,
        (Some(a), Some(b)) => a == b || matches!((DateTime::parse_from_rfc3339(a), DateTime::parse_from_rfc3339(b)), (Ok(x), Ok(y)) if x == y),
        _ => false,
    }
}

fn same_forward(a: Option<&Value>, b: Option<&Value>) -> bool {
    match (a, b) {
        (Some(a), Some(b)) => a["enabled"] == b["enabled"] && a["to"] == b["to"] && a["keepCopy"] == b["keepCopy"]
            && same_moment(a["activeFrom"].as_str(), b["activeFrom"].as_str()) && same_moment(a["activeUntil"].as_str(), b["activeUntil"].as_str()),
        (None, None) => true,
        _ => false,
    }
}

fn same_audience(a: Option<&Value>, b: Option<&Value>) -> bool {
    let n = |v: &Value| json!({"only": v["only"], "domains": v["domains"]});
    match (a, b) { (Some(a), Some(b)) => n(a) == n(b), (None, None) => true, _ => false }
}

// ---------------------------------------------------------------------------------------------
// what the filters script says (readVacationFilters)
// ---------------------------------------------------------------------------------------------

/// What the vacation page needs from the account's filters script.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Filters {
    /// The filters script runs the server's vacation script (VacationResponse then reads "off").
    pub includes_vacation: bool,
    pub forward: Option<Value>,
    pub forward_available: bool,
    pub audience: Option<Value>,
    pub audience_available: bool,
    /// Forwarding that is on, or a reply for some senders only, is stored but does not run.
    pub not_running: bool,
}

/// The script Bulwark writes: the active one that is not the vacation script, else the first.
pub fn target_script(scripts: &[Value]) -> Option<&Value> {
    let filters = || scripts.iter().filter(|s| s["name"] != VACATION_SCRIPT_NAME);
    filters().find(|s| s["isActive"] == true).or_else(|| filters().next())
}

fn has_ext(ext: &[String], e: &str) -> bool { ext.iter().any(|x| x == e) }

/// The server runs the forwarding block: a redirect, its period, its spam check and the copy.
fn can_forward(ext: &[String], max_redirects: Option<i64>) -> bool {
    max_redirects != Some(0) && has_ext(ext, "date") && has_ext(ext, "relational") && has_ext(ext, "spamtestplus") && has_ext(ext, "copy")
}

/// `target_text` is the target script's content (None: no target script).
pub fn read_filters(scripts: &[Value], target_text: Option<&str>, ext: &[String], max_redirects: Option<i64>) -> Filters {
    let target = target_script(scripts);
    let parsed = target_text.map(sieve::parse);
    let opaque = target.is_some() && parsed.as_ref().is_none_or(|p| p.locked);
    let usable = has_ext(ext, "include") && !opaque;
    let meta = parsed.as_ref().filter(|p| !p.locked).map(|p| &p.metadata);
    let forward = meta.and_then(|m| m.get("vacationForward")).filter(|f| sieve::valid_forward(f)).cloned();
    let audience = meta.and_then(|m| m.get("vacationAudience")).filter(|a| sieve::valid_audience(a)).cloned();
    let vacation_script = scripts.iter().find(|s| s["name"] == VACATION_SCRIPT_NAME);
    let filters_active = target.is_some_and(|t| t["isActive"] == true);
    let vacation_active = vacation_script.is_some_and(|s| s["isActive"] == true);
    Filters {
        includes_vacation: vacation_script.is_some() && filters_active && meta.is_some_and(|m| m["includeVacation"] == true),
        forward_available: usable && (can_forward(ext, max_redirects) || forward.as_ref().is_some_and(|f| f["enabled"] == true)),
        audience_available: usable && has_ext(ext, "envelope"),
        not_running: !filters_active && (forward.as_ref().is_some_and(|f| f["enabled"] == true) || (audience.is_some() && vacation_active)),
        forward,
        audience,
    }
}

// ---------------------------------------------------------------------------------------------
// writing the filters script (syncVacationWithFilters)
// ---------------------------------------------------------------------------------------------

/// What a save asks of the filters script. `forward` / `audience`: `None` keeps what is stored,
/// `Some(None)` removes it, `Some(Some(v))` replaces it.
pub struct SyncRequest<'a> {
    pub enabled: bool,
    pub forward: Option<Option<Value>>,
    pub audience: Option<Option<Value>>,
    pub from: Option<&'a str>,
    pub until: Option<&'a str>,
}

#[derive(Debug, PartialEq)]
pub enum Plan {
    Nothing,
    /// Write `content` over `target` (None: create the script) and activate it when `activate`.
    Write { target: Option<String>, content: String, activate: bool },
}

/// The pure decision of syncVacationWithFilters. `target_text` is the content of
/// `target_script(scripts)`.
pub fn plan_sync(req: &SyncRequest, scripts: &[Value], target_text: Option<&str>, ext: &[String]) -> Result<Plan, String> {
    let can_include = has_ext(ext, "include");
    if req.enabled && !can_include && req.forward.is_none() && req.audience.is_none() { return Ok(Plan::Nothing); }
    let target = target_script(scripts);
    let parsed = target_text.map(sieve::parse);
    let opaque = target.is_some() && parsed.as_ref().is_none_or(|p| p.locked);
    let readable = parsed.as_ref().filter(|p| !p.locked);
    let meta = readable.map(|p| p.metadata.clone()).unwrap_or_else(|| json!({"version": 1, "rules": []}));
    let stored_forward = meta.get("vacationForward").filter(|f| sieve::valid_forward(f)).cloned();
    let stored_audience = meta.get("vacationAudience").filter(|a| sieve::valid_audience(a)).cloned();
    let requested = match &req.forward { None => stored_forward.clone(), Some(f) => f.clone() };
    let next_forward = requested.map(|f| with_period(&f, req.from, req.until));
    let next_audience = match &req.audience { None => stored_audience.clone(), Some(a) => a.clone() };
    let changed = !same_forward(stored_forward.as_ref(), next_forward.as_ref()) || !same_audience(stored_audience.as_ref(), next_audience.as_ref());
    if changed {
        if opaque { return Err("The filters script was edited by hand, so forwarding and reply recipients cannot be changed here.".into()); }
        if !can_include { return Err("Forwarding and reply recipients need the Sieve \"include\" extension".into()); }
        if next_forward.as_ref().is_some_and(|f| !sieve::valid_forward(f)) { return Err("Unusable vacation forwarding".into()); }
        if next_audience.as_ref().is_some_and(|a| !sieve::valid_audience(a)) { return Err("Unusable vacation reply recipients".into()); }
    }
    if (req.enabled && !can_include) || opaque { return Ok(Plan::Nothing); }

    let has_rules = meta["rules"].as_array().is_some_and(|r| !r.is_empty()) || readable.is_some_and(|p| !p.external.trim().is_empty());
    let forwarding = next_forward.as_ref().is_some_and(|f| f["enabled"] == true);
    let target_active = target.is_some_and(|t| t["isActive"] == true);
    if req.enabled {
        let vacation_active = scripts.iter().any(|s| s["name"] == VACATION_SCRIPT_NAME && s["isActive"] == true);
        let took_over = vacation_active && !target_active;
        let needs_filters = has_rules || forwarding || next_audience.is_some();
        if !changed && !(took_over && needs_filters) { return Ok(Plan::Nothing); }
    } else {
        let forwarding_idle = forwarding && !target_active;
        if !changed && meta["includeVacation"] != true && !forwarding_idle { return Ok(Plan::Nothing); }
    }

    let mut m = meta;
    m["includeVacation"] = json!(req.enabled);
    match &next_forward { Some(f) => m["vacationForward"] = f.clone(), None => { m.as_object_mut().map(|o| o.remove("vacationForward")); } }
    match &next_audience { Some(a) => m["vacationAudience"] = a.clone(), None => { m.as_object_mut().map(|o| o.remove("vacationAudience")); } }
    let external = readable.map(|p| p.external.clone()).unwrap_or_default();
    let content = sieve::generate_meta(&m, &external, ext);
    Ok(Plan::Write { target: target.and_then(|t| t["id"].as_str().map(String::from)), content, activate: req.enabled || forwarding || target_active })
}

/// The account's Sieve scripts, extensions and redirect limit; `None` when the account has no Sieve.
pub fn read_sieve(c: &Client) -> Result<Option<(Vec<Value>, Vec<String>, Option<i64>)>, String> {
    if !c.has("urn:ietf:params:jmap:sieve") { return Ok(None); }
    let acc = c.account_for("urn:ietf:params:jmap:sieve");
    let r = c.call(json!([["SieveScript/get", {"accountId": acc, "ids": null, "properties": ["id", "name", "blobId", "isActive"]}, "s"]]))?;
    let scripts = r[0][1]["list"].as_array().cloned().unwrap_or_default();
    let cap = if c.session["accounts"][&acc]["accountCapabilities"]["urn:ietf:params:jmap:sieve"].is_object() {
        &c.session["accounts"][&acc]["accountCapabilities"]["urn:ietf:params:jmap:sieve"]
    } else { &c.session["capabilities"]["urn:ietf:params:jmap:sieve"] };
    let ext = cap["sieveExtensions"].as_array().map(|a| a.iter().filter_map(|x| x.as_str().map(String::from)).collect()).unwrap_or_default();
    Ok(Some((scripts, ext, cap["maxNumberRedirects"].as_i64())))
}

fn target_content(c: &Client, scripts: &[Value]) -> Result<Option<String>, String> {
    match target_script(scripts).and_then(|t| t["blobId"].as_str()) {
        Some(b) => Ok(Some(String::from_utf8_lossy(&c.download(b, "script.sieve", "application/sieve")?).to_string())),
        None => Ok(None),
    }
}

/// What the filters script holds now (None: no Sieve, or it could not be read).
pub fn load_filters(c: &Client) -> Option<Filters> {
    let (scripts, ext, max) = read_sieve(c).ok()??;
    let text = target_content(c, &scripts).ok()?;
    Some(read_filters(&scripts, text.as_deref(), &ext, max))
}

/// Keep the filters, the auto-reply and its forwarding running after `VacationResponse/set`.
pub fn sync_filters(c: &Client, req: &SyncRequest) -> Result<(), String> {
    let Some((scripts, ext, _)) = read_sieve(c)? else { return Ok(()) };
    let text = target_content(c, &scripts)?;
    let Plan::Write { target, content, activate } = plan_sync(req, &scripts, text.as_deref(), &ext)? else { return Ok(()) };
    let acc = c.account_for("urn:ietf:params:jmap:sieve");
    let (blob, _) = c.upload(content.into_bytes(), "application/sieve")?;
    let v = c.call(json!([["SieveScript/validate", {"accountId": acc, "blobId": blob}, "v"]]))?;
    if let Some(err) = v[0][1]["error"].as_object() {
        return Err(format!("Not saved: {}", err.get("description").and_then(|d| d.as_str()).unwrap_or("invalid script")));
    }
    let r = match target {
        Some(id) => {
            let mut args = json!({"accountId": acc, "update": { id.clone(): {"blobId": blob} }});
            if activate { args["onSuccessActivateScript"] = json!(id); }
            c.call(json!([["SieveScript/set", args, "s"]]))?
        }
        None => {
            let mut args = json!({"accountId": acc, "create": {"n": {"name": "filters", "blobId": blob}}});
            if activate { args["onSuccessActivateScript"] = json!("#n"); }
            c.call(json!([["SieveScript/set", args, "s"]]))?
        }
    };
    if r[0][1]["updated"].is_object() || r[0][1]["created"].is_object() { return Ok(()); }
    Err(format!("Not saved: {}", crate::jmap::set_error(r[0][1]["notUpdated"].as_object().and_then(|o| o.values().next())
        .or(r[0][1]["notCreated"].as_object().and_then(|o| o.values().next())).unwrap_or(&Value::Null))))
}

// ---------------------------------------------------------------------------------------------
// the message
// ---------------------------------------------------------------------------------------------

/// Plain text as the HTML body Bulwark's editor holds: a paragraph per block of lines, a line break
/// per single newline, everything escaped (lib/text-to-paragraph-html.ts).
pub fn text_to_paragraph_html(text: &str) -> String {
    let t = text.replace("\r\n", "\n").replace('\r', "\n");
    let t = t.trim();
    if t.is_empty() { return String::new(); }
    let mut blocks: Vec<String> = vec![];
    let mut cur: Vec<&str> = vec![];
    let mut blank = 0;
    for line in t.split('\n') {
        if line.is_empty() { blank += 1; continue; }
        if blank >= 1 && !cur.is_empty() { blocks.push(cur.join("\n")); cur.clear(); }
        blank = 0;
        cur.push(line);
    }
    if !cur.is_empty() { blocks.push(cur.join("\n")); }
    blocks.iter().map(|b| format!("<p>{}</p>", crate::esc(b).replace('\n', "<br>"))).collect()
}

/// Whether HTML holds any text a recipient would read.
pub fn has_readable_text(html: &str) -> bool { !html_to_plain(&sanitize_html(html)).trim().is_empty() }

/// The parts a save of the plain-text message sends: `(textBody, htmlBody)`.
pub fn parts_from_text(text: &str) -> (String, Option<String>) {
    let t = text.replace("\r\n", "\n").replace('\r', "\n").trim().to_string();
    let html = text_to_paragraph_html(&t);
    (t, if html.is_empty() { None } else { Some(html) })
}

/// The parts a save of the HTML message sends: the sanitised HTML and the plain text derived from it.
pub fn parts_from_html(html: &str) -> (String, Option<String>) {
    let clean = sanitize_html(html);
    let text = html_to_plain(&clean);
    if text.trim().is_empty() { return (String::new(), None); }
    (text.trim().to_string(), Some(clean))
}

// ---------------------------------------------------------------------------------------------
// the HTML body: an allowlist sanitiser. The stored body is shown by other people's mail clients
// and by Bulwark's preview (innerHTML), so the output is rebuilt from scratch: only these tags,
// only href (http, https, mailto) and title on a link, text left as text.
// ---------------------------------------------------------------------------------------------

/// What Bulwark's short editor toolbar produces, and the usual block tags.
const ALLOWED_TAGS: &[&str] = &[
    "a", "b", "strong", "i", "em", "u", "s", "strike", "del", "p", "br", "div", "span", "ul", "ol", "li",
    "blockquote", "h1", "h2", "h3", "h4", "h5", "h6", "hr", "sub", "sup", "code", "pre",
];
/// Elements dropped together with what is inside them.
const DROP_WITH_CONTENT: &[&str] = &[
    "script", "style", "iframe", "object", "embed", "applet", "noscript", "template", "textarea", "title", "svg", "math",
    "head", "select", "xmp", "noembed", "noframes", "frameset", "frame", "audio", "video", "canvas", "button", "form",
];

struct Tag { name: String, closing: bool, attrs: Vec<(String, String)>, end: usize }

/// The tag starting at `at` (a `<`): `Ok(None)` when it is not one, `Err` when it never ends.
fn parse_tag(s: &str, at: usize) -> Result<Option<Tag>, ()> {
    let b = s.as_bytes();
    let mut i = at + 1;
    let closing = b.get(i) == Some(&b'/');
    if closing { i += 1; }
    let ns = i;
    if !b.get(i).is_some_and(u8::is_ascii_alphabetic) { return Ok(None); }
    while b.get(i).is_some_and(|c| c.is_ascii_alphanumeric()) { i += 1; }
    let name = s[ns..i].to_ascii_lowercase();
    let mut attrs = vec![];
    loop {
        while b.get(i).is_some_and(|c| c.is_ascii_whitespace() || *c == b'/') { i += 1; }
        match b.get(i) {
            None => return Err(()),
            Some(b'>') => { i += 1; break; }
            _ => {}
        }
        let ks = i;
        while b.get(i).is_some_and(|c| !c.is_ascii_whitespace() && !matches!(c, b'=' | b'>' | b'/')) { i += 1; }
        if i == ks { i += 1; continue; }
        let key = s[ks..i].to_ascii_lowercase();
        while b.get(i).is_some_and(u8::is_ascii_whitespace) { i += 1; }
        let mut val = String::new();
        if b.get(i) == Some(&b'=') {
            i += 1;
            while b.get(i).is_some_and(u8::is_ascii_whitespace) { i += 1; }
            match b.get(i) {
                Some(&q) if q == b'"' || q == b'\'' => {
                    let vs = i + 1;
                    let end = s[vs..].find(q as char).ok_or(())? + vs;
                    val = s[vs..end].to_string();
                    i = end + 1;
                }
                _ => {
                    let vs = i;
                    while b.get(i).is_some_and(|c| !c.is_ascii_whitespace() && *c != b'>') { i += 1; }
                    val = s[vs..i].to_string();
                }
            }
        }
        attrs.push((key, val));
    }
    Ok(Some(Tag { name, closing, attrs, end: i }))
}

/// The index after the end tag of `name` that closes the element opened before `from`; the end of
/// the text when it never closes.
fn skip_element(s: &str, from: usize, name: &str) -> usize {
    let b = s.as_bytes();
    let mut i = from;
    while let Some(e) = s[i..].find('<') {
        let at = i + e;
        let close = format!("</{name}");
        if b.get(at..at + close.len()).is_some_and(|x| x.eq_ignore_ascii_case(close.as_bytes()))
            && !b.get(at + close.len()).is_some_and(u8::is_ascii_alphanumeric) {
            return s[at..].find('>').map_or(s.len(), |g| at + g + 1);
        }
        i = at + 1;
    }
    s.len()
}

/// Entities decoded once: the usual named ones and numeric ones (`&#106;`, `&#x6A;`, with or
/// without the semicolon, as browsers read them). Anything else stays as written.
fn decode_entities(s: &str) -> String {
    let mut out = String::with_capacity(s.len());
    let mut rest = s;
    while let Some(p) = rest.find('&') {
        out.push_str(&rest[..p]);
        rest = &rest[p..];
        let body = &rest[1..];
        let named = [("amp;", '&'), ("lt;", '<'), ("gt;", '>'), ("quot;", '"'), ("apos;", '\''), ("nbsp;", '\u{a0}'),
            ("colon;", ':'), ("tab;", '\t'), ("newline;", '\n'), ("lpar;", '('), ("rpar;", ')'), ("sol;", '/')];
        // on the bytes: the text after `&` may start with a character of several bytes ("&Ü")
        if let Some((n, ch)) = named.iter().find(|(n, _)| body.as_bytes().get(..n.len()).is_some_and(|x| x.eq_ignore_ascii_case(n.as_bytes()))) {
            out.push(*ch);
            rest = &rest[1 + n.len()..];
            continue;
        }
        if let Some(num) = body.strip_prefix('#') {
            let (hex, digits) = match num.strip_prefix(['x', 'X']) { Some(h) => (true, h), None => (false, num) };
            // the whole run of digits, as browsers read it: leading zeros are any number, and a
            // number too big for a character (more than 8 significant digits) is U+FFFD
            let len = digits.bytes().take_while(|c| if hex { c.is_ascii_hexdigit() } else { c.is_ascii_digit() }).count();
            if len > 0 {
                let sig = digits[..len].trim_start_matches('0');
                let code = if sig.len() > 8 { None } else if sig.is_empty() { Some(0) } else { u32::from_str_radix(sig, if hex { 16 } else { 10 }).ok() };
                out.push(code.map_or('\u{fffd}', numeric_char));
                let used = 1 + 1 + usize::from(hex) + len;
                rest = &rest[used..];
                if rest.starts_with(';') { rest = &rest[1..]; }
                continue;
            }
        }
        out.push('&');
        rest = &rest[1..];
    }
    out.push_str(rest);
    out
}

/// The character a numeric reference stands for, as browsers read it (HTML "numeric character
/// reference end state"): NUL, surrogates and numbers past Unicode are U+FFFD, 0x80-0x9F are the
/// Windows-1252 characters. Other control characters but tab, line feed, form feed and carriage
/// return are U+FFFD too (browsers keep them, but they have no place in a reply's text).
fn numeric_char(code: u32) -> char {
    const CP1252: [u32; 32] = [
        0x20AC, 0, 0x201A, 0x0192, 0x201E, 0x2026, 0x2020, 0x2021, 0x02C6, 0x2030, 0x0160, 0x2039, 0x0152, 0, 0x017D, 0,
        0, 0x2018, 0x2019, 0x201C, 0x201D, 0x2022, 0x2013, 0x2014, 0x02DC, 0x2122, 0x0161, 0x203A, 0x0153, 0, 0x017E, 0x0178,
    ];
    let code = if (0x80..=0x9F).contains(&code) { CP1252[(code - 0x80) as usize] } else { code };
    match char::from_u32(code) {
        Some(c) if c.is_control() && !matches!(c, '\t' | '\n' | '\u{c}' | '\r') => '\u{fffd}',
        Some(c) => c,
        None => '\u{fffd}',
    }
}

fn esc_attr(s: &str) -> String {
    s.replace('&', "&amp;").replace('"', "&quot;").replace('<', "&lt;").replace('>', "&gt;")
}

/// A link target the reply may carry: http, https or mailto, no control characters anywhere.
fn safe_href(raw: &str) -> Option<String> {
    let v = decode_entities(raw);
    let v = v.trim_matches(|c: char| c.is_ascii_control() || c == ' ');
    if v.is_empty() || v.chars().any(|c| c.is_control()) { return None; }
    let l = v.to_ascii_lowercase();
    ["http://", "https://", "mailto:"].iter().any(|p| l.starts_with(p)).then(|| v.to_string())
}

/// The HTML body rebuilt from the allowlist: scripts, frames, forms, styles, event handlers, ids,
/// classes and every URL scheme but http, https and mailto are gone; unknown tags lose their tag
/// and keep their text.
pub fn sanitize_html(html: &str) -> String {
    let mut out = String::new();
    let mut i = 0;
    while i < html.len() {
        let next = html[i..].find('<').map_or(html.len(), |e| i + e);
        out.push_str(&html[i..next]);
        i = next;
        if i >= html.len() { break; }
        if html[i..].starts_with("<!--") {
            match html[i + 4..].find("-->") { Some(e) => { i += 4 + e + 3; continue; } None => break }
        }
        if matches!(html.as_bytes().get(i + 1), Some(b'!' | b'?')) {
            match html[i..].find('>') { Some(e) => { i += e + 1; continue; } None => break }
        }
        match parse_tag(html, i) {
            Err(()) => break,
            Ok(None) => { out.push_str("&lt;"); i += 1; }
            Ok(Some(t)) => {
                i = t.end;
                if t.name == "plaintext" { break; }
                if !t.closing && DROP_WITH_CONTENT.contains(&t.name.as_str()) { i = skip_element(html, i, &t.name); continue; }
                if !ALLOWED_TAGS.contains(&t.name.as_str()) { continue; }
                if t.closing {
                    if !matches!(t.name.as_str(), "br" | "hr") { out.push_str(&format!("</{}>", t.name)); }
                    continue;
                }
                out.push('<');
                out.push_str(&t.name);
                if t.name == "a" {
                    let href = t.attrs.iter().find(|(k, _)| k == "href").and_then(|(_, v)| safe_href(v));
                    if let Some(h) = &href { out.push_str(&format!(" href=\"{}\" rel=\"noopener noreferrer\"", esc_attr(h))); }
                    if let Some((_, v)) = t.attrs.iter().find(|(k, _)| k == "title") { out.push_str(&format!(" title=\"{}\"", esc_attr(&decode_entities(v)))); }
                }
                out.push('>');
            }
        }
    }
    out
}

/// The plain-text part of (sanitised) HTML: a blank line between paragraphs, a line break per
/// `<br>`, list items on their own lines, entities decoded once.
pub fn html_to_plain(html: &str) -> String {
    let mut out = String::new();
    let mut i = 0;
    while i < html.len() {
        let next = html[i..].find('<').map_or(html.len(), |e| i + e);
        for ch in decode_entities(&html[i..next]).chars() {
            if ch == '\u{a0}' { out.push(' '); }
            else if ch.is_whitespace() { if !out.is_empty() && !out.ends_with([' ', '\n']) { out.push(' '); } }
            else { out.push(ch); }
        }
        i = next;
        if i >= html.len() { break; }
        match parse_tag(html, i) {
            Ok(Some(t)) => {
                i = t.end;
                let n = t.name.as_str();
                if n == "br" { out.push('\n'); }
                else if n == "li" && !t.closing { if !out.is_empty() && !out.ends_with('\n') { out.push('\n'); } out.push_str("- "); }
                else if n == "li" { if !out.ends_with('\n') { out.push('\n'); } }
                else if n == "hr" || (t.closing && matches!(n, "ul" | "ol" | "p" | "div" | "blockquote" | "pre" | "h1" | "h2" | "h3" | "h4" | "h5" | "h6")) {
                    while out.ends_with(' ') { out.pop(); }
                    if !out.is_empty() { while !out.ends_with("\n\n") { out.push('\n'); } }
                }
            }
            Ok(None) => { out.push('<'); i += 1; }
            Err(()) => break,
        }
    }
    let lines: Vec<&str> = out.lines().map(str::trim_end).collect();
    let mut res = String::new();
    let mut blank = 0;
    for l in lines {
        if l.trim().is_empty() { blank += 1; if blank > 1 { continue; } } else { blank = 0; }
        res.push_str(l.trim_start_matches(' ').trim_end());
        res.push('\n');
    }
    res.trim().to_string()
}

/// The refusals before a save: an empty message on a reply that is on, a subject or body over
/// Stalwart's limits (they would only answer "Field could not be set.").
pub fn check_message(enabled: bool, subject: &str, text: &str, html: Option<&str>) -> Result<(), String> {
    if subject.len() > SUBJECT_MAX { return Err(format!("The subject is too long (at most {SUBJECT_MAX} bytes).")); }
    if text.len() > BODY_MAX || html.is_some_and(|h| h.len() > BODY_MAX) { return Err(format!("The message is too long (at most {BODY_MAX} bytes).")); }
    if enabled && text.trim().is_empty() && !html.is_some_and(has_readable_text) { return Err("Write the message first.".into()); }
    Ok(())
}

// ---------------------------------------------------------------------------------------------
// dates as the page shows them
// ---------------------------------------------------------------------------------------------

/// The user's time zone (the system's), UTC when unknown.
pub fn user_tz() -> Tz { crate::agent::local_tz().parse().unwrap_or(chrono_tz::UTC) }

/// The first moment of `day` in `tz`: midnight, or, where the clocks go forward at midnight
/// (America/Santiago, America/Havana...), the first time of the day that exists.
pub fn day_start(day: NaiveDate, tz: Tz) -> Option<DateTime<Utc>> {
    let midnight = day.and_time(NaiveTime::MIN);
    (0..=4 * 24).find_map(|q| tz.from_local_datetime(&midnight.checked_add_signed(Duration::minutes(15 * q))?).earliest()).map(|d| d.with_timezone(&Utc))
}

/// Whether `t` is the first moment of its day in `tz` (a day the page and the block show alone).
pub fn is_day_start(t: DateTime<Utc>, tz: Tz) -> bool { day_start(t.with_timezone(&tz).date_naive(), tz) == Some(t) }

/// What the user typed as an instant: `YYYY-MM-DD` (the start of that day) or `YYYY-MM-DD HH:MM`
/// on the wall clock of `tz`; `Ok(None)` for empty, `Err` for text that is not a date yet. A time
/// that does not exist (the hour skipped when the clocks go forward) is read an hour later, the
/// first valid moment after the gap; one that exists twice is read as its first.
pub fn parse_moment_dt(input: &str, tz: Tz) -> Result<Option<DateTime<Utc>>, ()> {
    let t = input.trim().replace('T', " ");
    if t.is_empty() { return Ok(None); }
    let utc = if t.len() == 10 {
        // a day alone stays a day where its midnight is skipped, so the block is all-day
        day_start(NaiveDate::parse_from_str(&t, "%Y-%m-%d").map_err(|_| ())?, tz).ok_or(())?
    } else {
        let naive = NaiveDateTime::parse_from_str(&t, "%Y-%m-%d %H:%M").map_err(|_| ())?;
        tz.from_local_datetime(&naive).earliest()
            .or_else(|| tz.from_local_datetime(&naive.checked_add_signed(Duration::hours(1))?).earliest()).ok_or(())?.with_timezone(&Utc)
    };
    if utc.format("%Y").to_string().len() != 4 { return Err(()); }
    Ok(Some(utc))
}

/// [`parse_moment_dt`] as the stored text.
#[cfg(test)]
pub fn parse_moment(input: &str, tz: Tz) -> Result<Option<String>, ()> {
    Ok(parse_moment_dt(input, tz)?.map(|u| u.format("%Y-%m-%dT%H:%M:%S.000Z").to_string()))
}

/// A stored moment as an instant (`...Z` and `....000Z` are the same one).
pub fn moment_instant(s: &str) -> Option<DateTime<Utc>> { DateTime::parse_from_rfc3339(s).ok().map(|d| d.with_timezone(&Utc)) }

/// Whether two optional stored moments are the same instant.
pub fn same_opt_moment(a: Option<&str>, b: Option<&str>) -> bool {
    match (a, b) { (None, None) => true, (Some(a), Some(b)) => a == b || matches!((moment_instant(a), moment_instant(b)), (Some(x), Some(y)) if x == y), _ => false }
}

/// A stored moment as the field shows it: the day alone at its start, else day and time.
pub fn show_moment(stored: &str, tz: Tz) -> String {
    let Some(t) = moment_instant(stored) else { return stored.chars().take(10).collect() };
    let l = t.with_timezone(&tz);
    if is_day_start(t, tz) { l.format("%Y-%m-%d").to_string() } else { l.format("%Y-%m-%d %H:%M").to_string() }
}

// ---------------------------------------------------------------------------------------------
// the calendar block (lib/vacation-calendar.ts)
// ---------------------------------------------------------------------------------------------

pub const OOO_UID_PREFIX: &str = "bulwark-ooo-";
/// The keyword (iCalendar CATEGORIES) on the busy event.
pub const OOO_KEYWORD: &str = "bulwark-out-of-office";
/// The event title when the reply has no subject (English on purpose: colleagues see it).
pub const OUT_OF_OFFICE_TITLE: &str = "Out of office";

pub fn ooo_uid(account: &str) -> String { format!("{OOO_UID_PREFIX}{account}") }

/// What the vacation response says, as far as the block cares.
#[derive(Clone, Debug, PartialEq)]
pub struct Reply { pub is_enabled: bool, pub from: Option<String>, pub to: Option<String>, pub subject: String }

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Trigger { Save, Load }

#[derive(Debug, PartialEq)]
pub enum Decision {
    None,
    Create(DateTime<Utc>, DateTime<Utc>),
    Update(DateTime<Utc>, DateTime<Utc>),
    Destroy,
    EndNow(DateTime<Utc>),
}

pub struct DecisionInput<'a> {
    pub block: bool,
    pub reply: &'a Reply,
    pub event: Option<(DateTime<Utc>, DateTime<Utc>)>,
    /// The times last written, as ISO instants.
    pub last: Option<(String, String)>,
    /// The reply's dates before the change at hand.
    pub previous: Option<(Option<String>, Option<String>)>,
    pub marked: bool,
    pub now: DateTime<Utc>,
    pub trigger: Trigger,
}

fn instant(s: &Option<String>) -> Option<DateTime<Utc>> {
    s.as_deref().and_then(|s| DateTime::parse_from_rfc3339(s).ok()).map(|d| d.with_timezone(&Utc))
}

fn same_instant(a: DateTime<Utc>, b: &str) -> bool { DateTime::parse_from_rfc3339(b).is_ok_and(|b| b.with_timezone(&Utc) == a) }

/// What to do with the busy event, by the rules of Bulwark's decideBlock.
pub fn decide_block(i: &DecisionInput) -> Decision {
    let (from, to) = (instant(&i.reply.from), instant(&i.reply.to));
    let dates_usable = matches!((from, to), (Some(f), Some(t)) if t > f);
    let wanted = i.block && i.reply.is_enabled && dates_usable;
    let ran_out = i.reply.is_enabled && to.is_some_and(|t| t <= i.now);
    if let Some((es, ee)) = i.event {
        let matches_last = i.last.as_ref().is_some_and(|(s, e)| same_instant(es, s) && same_instant(ee, e));
        let matches_prev = i.previous.as_ref().is_some_and(|(f, t)| matches!((f, t), (Some(f), Some(t)) if same_instant(es, f) && same_instant(ee, t)));
        let marked_on_load = i.last.is_none() && i.marked && i.trigger == Trigger::Load;
        if !matches_last && !matches_prev && !marked_on_load { return Decision::None; }
        if wanted {
            let (f, t) = (from.unwrap(), to.unwrap());
            if ran_out || (es == f && ee == t) { return Decision::None; }
            return Decision::Update(f, t);
        }
        if ran_out || ee <= i.now { return Decision::None; }
        if es > i.now { return Decision::Destroy; }
        return Decision::EndNow(i.now);
    }
    if !wanted || ran_out { return Decision::None; }
    let (f, t) = (from.unwrap(), to.unwrap());
    if i.trigger == Trigger::Load && i.last.as_ref().is_some_and(|(s, e)| same_instant(f, s) && same_instant(t, e)) {
        // it was there for these dates and someone deleted it: that sticks
        return Decision::None;
    }
    Decision::Create(f, t)
}

/// ISO 8601 duration of a span, in days, hours and minutes.
pub fn iso_duration(ms: i64) -> String {
    let total = (ms.max(0) as f64 / 60000.0).round() as i64;
    let (days, hours, minutes) = (total / 1440, (total % 1440) / 60, total % 60);
    let time = format!("{}{}", if hours > 0 { format!("{hours}H") } else { String::new() }, if minutes > 0 { format!("{minutes}M") } else { String::new() });
    if days == 0 && time.is_empty() { return "PT0S".into(); }
    format!("P{}{}", if days > 0 { format!("{days}D") } else { String::new() }, if time.is_empty() { String::new() } else { format!("T{time}") })
}

/// The event's time fields for a span: all-day when both ends start their day in `tz`, timed in the
/// zone otherwise (blockTimes).
pub fn block_times(start: DateTime<Utc>, end: DateTime<Utc>, tz: Tz) -> Value {
    let (a, b) = (start.with_timezone(&tz).naive_local(), end.with_timezone(&tz).naive_local());
    if is_day_start(start, tz) && is_day_start(end, tz) {
        let days = (b.date() - a.date()).num_days();
        return json!({"start": format!("{}T00:00:00", a.date().format("%Y-%m-%d")), "duration": format!("P{days}D"), "timeZone": null, "showWithoutTime": true});
    }
    json!({"start": a.format("%Y-%m-%dT%H:%M:%S").to_string(), "duration": iso_duration((end - start).num_milliseconds()), "timeZone": tz.name(), "showWithoutTime": false})
}

/// Seconds of an ISO 8601 duration (`P1DT2H30M`, `P2W`, `PT90S`).
pub fn duration_secs(d: &str) -> Option<i64> {
    let rest = d.strip_prefix('P')?;
    let (date, time) = match rest.split_once('T') { Some((a, b)) => (a, b), None => (rest, "") };
    let mut total = 0i64;
    for (part, units) in [(date, [('W', 604800), ('D', 86400)].as_slice()), (time, [('H', 3600), ('M', 60), ('S', 1)].as_slice())] {
        let mut num = String::new();
        for ch in part.chars() {
            if ch.is_ascii_digit() { num.push(ch); continue; }
            let (_, mult) = units.iter().find(|(u, _)| *u == ch)?;
            // a server's value: too big to be a span is no span (never an overflow)
            total = total.checked_add(num.parse::<i64>().ok()?.checked_mul(*mult)?)?;
            num.clear();
        }
        if !num.is_empty() { return None; }
    }
    Some(total)
}

/// The instants an event covers, read the way `block_times` writes them (eventSpan).
pub fn event_span(ev: &Value, tz: Tz) -> Option<(DateTime<Utc>, DateTime<Utc>)> {
    let start = ev["start"].as_str()?;
    let naive = NaiveDateTime::parse_from_str(start, "%Y-%m-%dT%H:%M:%S").ok()?;
    let all_day = ev["showWithoutTime"] == true;
    let zone: Tz = if all_day { tz } else { ev["timeZone"].as_str().and_then(|z| z.parse().ok()).unwrap_or(tz) };
    let secs = duration_secs(ev["duration"].as_str().unwrap_or("PT0S")).unwrap_or(0);
    if all_day {
        let day = naive.date();
        let days = (secs as f64 / 86400.0).round() as i64;
        let s = day_start(day, zone)?;
        let e = day_start(day.checked_add_signed(Duration::try_days(days)?)?, zone)?;
        return Some((s, e));
    }
    let s = zone.from_local_datetime(&naive).earliest()?.with_timezone(&Utc);
    Some((s, s.checked_add_signed(Duration::try_seconds(secs)?)?))
}

/// The calendar a block goes into: the default one the user may add events to (blockCalendar).
pub fn block_calendar(calendars: &[Value]) -> Option<&Value> {
    let own: Vec<&Value> = calendars.iter().filter(|c| c["isTasksOnly"] != true).collect();
    let cal = own.iter().find(|c| c["isDefault"] == true).or(own.first()).copied()?;
    let r = &cal["myRights"];
    if r.is_null() || r["mayWriteAll"] == true || r["mayWriteOwn"] == true { Some(cal) } else { None }
}

/// The event to create for a span (the fields of syncVacationBlock's `create`).
pub fn new_block_event(account: &str, calendar_id: &str, subject: &str, start: DateTime<Utc>, end: DateTime<Utc>, tz: Tz) -> Value {
    let mut ev = json!({
        "@type": "Event", "uid": ooo_uid(account), "title": title_for(subject), "calendarIds": {calendar_id: true},
        "freeBusyStatus": "busy", "privacy": "public", "status": "confirmed", "keywords": {OOO_KEYWORD: true},
    });
    for (k, v) in block_times(start, end, tz).as_object().cloned().unwrap_or_default() { ev[k] = v; }
    ev
}

fn title_for(subject: &str) -> String { if subject.trim().is_empty() { OUT_OF_OFFICE_TITLE.into() } else { subject.trim().into() } }

/// The record of the span last written for `account`, from the `vacationCalendarBlocks` pref.
pub fn block_record(blocks: Option<&Value>, account: &str) -> Option<(String, String)> {
    let o = blocks?.get(account)?.as_object()?;
    Some((o.get("start")?.as_str()?.to_string(), o.get("end")?.as_str()?.to_string()))
}

/// The `vacationCalendarBlocks` pref with `account`'s record set (or removed).
pub fn with_block(blocks: Option<&Value>, account: &str, block: Option<(DateTime<Utc>, DateTime<Utc>)>) -> Value {
    let mut m = blocks.cloned().filter(Value::is_object).unwrap_or_else(|| json!({}));
    match block {
        Some((s, e)) => m[account] = json!({"start": s.to_rfc3339_opts(chrono::SecondsFormat::Millis, true), "end": e.to_rfc3339_opts(chrono::SecondsFormat::Millis, true)}),
        None => { m.as_object_mut().map(|o| o.remove(account)); }
    }
    m
}

/// The record of `key` (server, login and account: `Client::key_for`), else one an older
/// version wrote under the bare account id.
pub fn block_record_for(blocks: Option<&Value>, key: &str, legacy_id: &str) -> Option<(String, String)> {
    block_record(blocks, key).or_else(|| block_record(blocks, legacy_id))
}

/// The pref with `key`'s record set (or removed), and the older bare-id record it replaces gone.
pub fn with_block_for(blocks: Option<&Value>, key: &str, legacy_id: &str, block: Option<(DateTime<Utc>, DateTime<Utc>)>) -> Value {
    with_block(Some(&with_block(blocks, legacy_id, None)), key, block)
}

/// Through the live settings (under their lock), so the settings page's next save keeps it.
fn remember(key: &str, legacy_id: &str, block: Option<(DateTime<Utc>, DateTime<Utc>)>) {
    crate::settings::update_pref("vacationCalendarBlocks", |cur| with_block_for(cur, key, legacy_id, block));
}

fn last_written(key: &str, legacy_id: &str) -> Option<(String, String)> {
    let mut r = None;
    crate::settings::with_live(|s| r = block_record_for(s.prefs.get("vacationCalendarBlocks"), key, legacy_id));
    r
}

/// Whether the block is on: the "Block my calendar" setting, on by default. Takes the live
/// settings' lock: never call it while holding them (use [`block_setting_in`]).
pub fn block_setting() -> bool {
    let mut on = true;
    crate::settings::with_live(|s| on = block_setting_in(s));
    on
}

/// [`block_setting`] read from settings the caller already holds.
pub fn block_setting_in(s: &crate::settings::Settings) -> bool { s.bool("vacationBlockCalendar", true) }

/// Runs `f` with the account's out-of-office writes held, so two saves (or a save and the load
/// sync) never interleave their script and calendar writes. Called again on the same thread it
/// just runs.
pub fn serialized<T>(c: &Client, f: impl FnOnce() -> T) -> T {
    use std::sync::{Arc, Mutex, OnceLock};
    use std::collections::HashMap;
    thread_local! { static HELD: std::cell::Cell<bool> = const { std::cell::Cell::new(false) }; }
    static LOCKS: OnceLock<Mutex<HashMap<String, Arc<Mutex<()>>>>> = OnceLock::new();
    if HELD.with(|h| h.get()) { return f(); }
    // per server and login too: two servers can hand out the same short account id
    let lock = LOCKS.get_or_init(Default::default).lock().unwrap().entry(c.state_key()).or_default().clone();
    let _g = lock.lock().unwrap_or_else(|e| e.into_inner());
    HELD.with(|h| h.set(true));
    let r = f();
    HELD.with(|h| h.set(false));
    r
}

/// Bring the busy event in line with the reply. Never throws into the save: the caller logs.
pub fn sync_block(c: &Client, reply: &Reply, trigger: Trigger, previous: Option<(Option<String>, Option<String>)>) -> Result<&'static str, String> {
    if !c.has("urn:ietf:params:jmap:calendars") { return Ok("none"); }
    let acc = c.account_for("urn:ietf:params:jmap:calendars");
    let tz = user_tz();
    let r = c.call(json!([
        ["CalendarEvent/query", {"accountId": acc, "filter": {"uid": ooo_uid(&acc)}, "limit": 1}, "q"],
        ["CalendarEvent/get", {"accountId": acc, "#ids": {"resultOf": "q", "name": "CalendarEvent/query", "path": "/ids"},
            "properties": ["id", "start", "duration", "timeZone", "showWithoutTime", "keywords"]}, "g"],
    ]))?;
    let found = r[1][1]["list"].as_array().and_then(|l| l.first()).cloned();
    let span = found.as_ref().and_then(|f| event_span(f, tz));
    let rec = c.key_for(&acc);
    let last = last_written(&rec, &acc);
    let marked = found.as_ref().is_some_and(|f| f["keywords"][OOO_KEYWORD] == true);
    let block = block_setting();
    let now = Utc::now();
    let decision = decide_block(&DecisionInput { block, reply, event: span, last: last.clone(), previous, marked, now, trigger });
    let id = found.as_ref().and_then(|f| f["id"].as_str()).unwrap_or("").to_string();
    let set = |args: Value| -> Result<Value, String> {
        let mut a = args;
        a["accountId"] = json!(acc);
        a["sendSchedulingMessages"] = json!(false);
        let r = c.call(json!([["CalendarEvent/set", a, "s"]]))?;
        for k in ["notCreated", "notUpdated", "notDestroyed"] {
            if let Some(e) = r[0][1][k].as_object().and_then(|m| m.values().next()) { return Err(crate::jmap::set_error(e)); }
        }
        Ok(r[0][1].clone())
    };
    match decision {
        Decision::None => {
            if found.is_none() && last.is_some() && !(reply.is_enabled && block) { remember(&rec, &acc, None); }
            Ok("none")
        }
        Decision::Create(s, e) => {
            let cals = c.call(json!([["Calendar/get", {"accountId": acc, "ids": null, "properties": ["id", "isDefault", "myRights", "isTasksOnly"]}, "c"]]))?;
            let list = cals[0][1]["list"].as_array().cloned().unwrap_or_default();
            let Some(cal) = block_calendar(&list) else { return Ok("no-calendar") };
            set(json!({"create": {"e": new_block_event(&acc, cal["id"].as_str().unwrap_or(""), &reply.subject, s, e, tz)}}))?;
            remember(&rec, &acc, Some((s, e)));
            Ok("create")
        }
        Decision::Update(s, e) => {
            let mut patch = block_times(s, e, tz);
            patch["title"] = json!(title_for(&reply.subject));
            patch[format!("keywords/{OOO_KEYWORD}")] = json!(true);
            set(json!({"update": {id: patch}}))?;
            remember(&rec, &acc, Some((s, e)));
            Ok("update")
        }
        Decision::EndNow(now_end) => {
            let (start, _) = span.ok_or("event has no times")?;
            // whole minutes: Stalwart stores the duration in them
            let end = std::cmp::max(start + Duration::seconds(60), Utc.timestamp_opt((now_end.timestamp() + 59) / 60 * 60, 0).single().unwrap_or(now_end));
            set(json!({"update": {id: block_times(start, end, tz)}}))?;
            let written = event_span(&block_times(start, end, tz), tz).map(|x| x.1).unwrap_or(end);
            remember(&rec, &acc, Some((start, written)));
            Ok("end-now")
        }
        Decision::Destroy => {
            set(json!({"destroy": [id]}))?;
            remember(&rec, &acc, None);
            Ok("destroy")
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn t(s: &str) -> DateTime<Utc> { DateTime::parse_from_rfc3339(s).unwrap().with_timezone(&Utc) }
    fn reply(on: bool, from: Option<&str>, to: Option<&str>) -> Reply { Reply { is_enabled: on, from: from.map(Into::into), to: to.map(Into::into), subject: "Away".into() } }
    fn doms(v: &[&str]) -> Vec<String> { v.iter().map(|s| s.to_string()).collect() }

    #[test]
    fn own_domains_are_lowercase_unique_and_valid() {
        let a = doms(&["Ann@Example.Test", "bob@example.test", "x@sub.example.test", "no-at", "y@localhost", "z@bad_domain.test"]);
        assert_eq!(own_domains(&a), doms(&["example.test", "sub.example.test"]));
    }

    #[test]
    fn audience_is_stored_as_bulwark_does() {
        let d = doms(&["example.test"]);
        assert_eq!(audience_for(0, &d), None);
        assert_eq!(audience_for(1, &[]), None);
        let a = audience_for(2, &d).unwrap();
        assert_eq!(a.to_string(), r#"{"only":"external","domains":["example.test"]}"#);
        assert!(sieve::valid_audience(&a));
        assert_eq!(audience_choice(Some(&a)), 2);
        assert_eq!(audience_choice(audience_for(1, &d).as_ref()), 1);
        assert_eq!(audience_choice(None), 0);
    }

    #[test]
    fn forward_takes_the_vacation_period() {
        let f = json!({"enabled": true, "to": "ann@example.test", "keepCopy": true, "activeFrom": "old"});
        let w = with_period(&f, Some("2026-10-05T06:00:00.000Z"), None);
        assert_eq!(w, json!({"enabled": true, "to": "ann@example.test", "keepCopy": true, "activeFrom": "2026-10-05T06:00:00.000Z"}));
        assert!(sieve::valid_forward(&w));
        assert_eq!(with_period(&f, None, Some("")), json!({"enabled": true, "to": "ann@example.test", "keepCopy": true}));
    }

    const EXT: [&str; 8] = ["include", "envelope", "date", "relational", "spamtestplus", "copy", "vacation", "fileinto"];
    fn ext() -> Vec<String> { EXT.iter().map(|s| s.to_string()).collect() }
    fn script(id: &str, name: &str, active: bool) -> Value { json!({"id": id, "name": name, "isActive": active, "blobId": format!("b{id}")}) }

    #[test]
    fn forwarding_writes_the_include_and_the_block() {
        let scripts = vec![script("1", "vacation", true)];
        let fwd = json!({"enabled": true, "to": "ann@example.test", "keepCopy": true});
        let req = SyncRequest { enabled: true, forward: Some(Some(fwd)), audience: Some(audience_for(2, &doms(&["example.test"]))),
            from: Some("2026-10-05T06:00:00.000Z"), until: Some("2026-10-16T16:00:00.000Z") };
        let Plan::Write { target, content, activate } = plan_sync(&req, &scripts, None, &ext()).unwrap() else { panic!("expected a write") };
        assert_eq!(target, None);
        assert!(activate);
        assert!(content.contains(r#""vacationForward":{"enabled":true,"to":"ann@example.test","keepCopy":true,"activeFrom":"2026-10-05T06:00:00.000Z","activeUntil":"2026-10-16T16:00:00.000Z"}"#), "{content}");
        assert!(content.contains(r#""vacationAudience":{"only":"external","domains":["example.test"]}"#));
        assert!(content.contains(r#""includeVacation":true"#));
        assert!(content.contains("# Vacation forwarding") && content.contains(r#"redirect :copy "ann@example.test";"#));
        assert!(content.contains(r#"if not envelope :domain :is "from" ["example.test"] {"#));
        // what was written reads back as the page shows it
        let written = vec![script("1", "vacation", false), script("2", "filters", true)];
        let f = read_filters(&written, Some(&content), &ext(), None);
        assert!(f.includes_vacation && f.forward_available && f.audience_available && !f.not_running);
        assert_eq!(f.forward.unwrap()["keepCopy"], true);
        assert_eq!(f.audience.unwrap()["only"], "external");
    }

    #[test]
    fn nothing_to_do_changes_nothing() {
        // a reply with no filters script and nothing to forward needs no script
        let scripts = vec![script("1", "vacation", true)];
        let req = SyncRequest { enabled: true, forward: None, audience: None, from: None, until: None };
        assert_eq!(plan_sync(&req, &scripts, None, &ext()).unwrap(), Plan::Nothing);
        // switching off with nothing stored
        let off = SyncRequest { enabled: false, ..req };
        assert_eq!(plan_sync(&off, &scripts, None, &ext()).unwrap(), Plan::Nothing);
        // no `include` on the server: the reply simply runs on its own
        let no_include = vec!["vacation".to_string()];
        let req = SyncRequest { enabled: true, forward: None, audience: None, from: None, until: None };
        assert_eq!(plan_sync(&req, &scripts, None, &no_include).unwrap(), Plan::Nothing);
    }

    #[test]
    fn rules_keep_running_when_the_reply_switches_on_and_off() {
        let rule = sieve::rule("News", "from", "contains", "n@", "move", "N", false);
        let base = sieve::generate_meta(&json!({"version": 1, "rules": [rule]}), "", &ext());
        let scripts = vec![script("1", "vacation", true), script("2", "filters", false)];
        let on = SyncRequest { enabled: true, forward: None, audience: None, from: None, until: None };
        // the vacation script took over from the filters: the include puts them back
        let Plan::Write { target, content, activate } = plan_sync(&on, &scripts, Some(&base), &ext()).unwrap() else { panic!() };
        assert_eq!(target.as_deref(), Some("2"));
        assert!(activate && content.contains(r#"include :personal :optional "vacation";"#) && content.contains("# Rule: News"));
        // switched off again: the include goes, the filters stay active
        let scripts = vec![script("1", "vacation", false), script("2", "filters", true)];
        let off = SyncRequest { enabled: false, ..on };
        let Plan::Write { content, activate, .. } = plan_sync(&off, &scripts, Some(&content), &ext()).unwrap() else { panic!() };
        assert!(activate && !content.contains(r#"include :personal"#) && content.contains("# Rule: News"));
    }

    #[test]
    fn a_hand_edited_script_is_not_rewritten() {
        let scripts = vec![script("2", "filters", true)];
        let hand = "/* @metadata:begin\n{\"version\":99,\"rules\":[]}\n@metadata:end */\nkeep;\n";
        let fwd = json!({"enabled": true, "to": "ann@example.test", "keepCopy": false});
        let req = SyncRequest { enabled: true, forward: Some(Some(fwd)), audience: None, from: None, until: None };
        assert!(plan_sync(&req, &scripts, Some(hand), &ext()).is_err());
        let plain = SyncRequest { enabled: true, forward: None, audience: None, from: None, until: None };
        assert_eq!(plan_sync(&plain, &scripts, Some(hand), &ext()).unwrap(), Plan::Nothing);
        let f = read_filters(&scripts, Some(hand), &ext(), None);
        assert!(!f.forward_available && !f.audience_available);
        // an unusable address is refused
        let bad = SyncRequest { enabled: true, forward: Some(Some(json!({"enabled": true, "to": "nope", "keepCopy": false}))), audience: None, from: None, until: None };
        assert!(plan_sync(&bad, &vec![], None, &ext()).is_err());
    }

    #[test]
    fn forwarding_availability_follows_the_server() {
        let scripts = vec![script("2", "filters", true)];
        let text = sieve::generate_meta(&json!({"version": 1, "rules": []}), "", &ext());
        assert!(read_filters(&scripts, Some(&text), &ext(), Some(5)).forward_available);
        assert!(!read_filters(&scripts, Some(&text), &ext(), Some(0)).forward_available);
        let few: Vec<String> = doms(&["include", "envelope"]);
        let f = read_filters(&scripts, Some(&text), &few, None);
        assert!(!f.forward_available && f.audience_available);
        // stored forwarding that is idle: the filters script is not the active one
        let on = sieve::generate_meta(&json!({"version": 2, "rules": [], "vacationForward": {"enabled": true, "to": "a@b.cd", "keepCopy": false}}), "", &ext());
        let idle = vec![script("1", "vacation", true), script("2", "filters", false)];
        assert!(read_filters(&idle, Some(&on), &ext(), None).not_running);
    }

    #[test]
    fn messages_are_stored_as_text_plus_html() {
        assert_eq!(text_to_paragraph_html("Hi <b>\nthere\n\n\nBye & thanks"), "<p>Hi &lt;b&gt;<br>there</p><p>Bye &amp; thanks</p>");
        assert_eq!(text_to_paragraph_html("  \n "), "");
        assert_eq!(parts_from_text("Away\r\nback soon"), ("Away\nback soon".to_string(), Some("<p>Away<br>back soon</p>".to_string())));
        assert_eq!(parts_from_text(""), (String::new(), None));
        let (text, html) = parts_from_html("<p>Hello</p><script>alert(1)</script><p>there</p>");
        assert_eq!(html.as_deref(), Some("<p>Hello</p><p>there</p>"));
        assert_eq!(text, "Hello\n\nthere");
        assert_eq!(parts_from_html("<p> </p>"), (String::new(), None));
        let (_, h) = parts_from_html(r#"<p onclick="x()">Hi <a href="javascript:evil()">a</a><scr<script></script>ipt>bad</scr<script></script>ipt></p>"#);
        let h = h.unwrap();
        assert!(!h.contains("onclick") && !h.contains("javascript:") && !h.to_lowercase().contains("<script"), "{h}");
    }

    #[test]
    fn message_checks_match_stalwart_limits() {
        assert!(check_message(true, "S", "text", None).is_ok());
        assert!(check_message(true, "S", "", None).is_err());
        assert!(check_message(false, "S", "", None).is_ok());
        assert!(check_message(true, "S", "", Some("<p>x</p>")).is_ok());
        assert!(check_message(false, &"s".repeat(512), "x", None).is_err());
        assert!(check_message(false, &"s".repeat(511), &"x".repeat(2047), None).is_ok());
        assert!(check_message(false, "s", &"x".repeat(2048), None).is_err());
        assert!(check_message(false, "s", "x", Some(&"y".repeat(2048))).is_err());
    }

    #[test]
    fn dates_round_trip_on_the_wall_clock() {
        let berlin: Tz = "Europe/Berlin".parse().unwrap();
        assert_eq!(parse_moment("2026-10-05", berlin), Ok(Some("2026-10-04T22:00:00.000Z".into())));
        assert_eq!(parse_moment("2026-10-05 08:30", berlin), Ok(Some("2026-10-05T06:30:00.000Z".into())));
        assert_eq!(parse_moment("", berlin), Ok(None));
        assert_eq!(parse_moment("2026-1", berlin), Err(()));
        assert_eq!(parse_moment("2026-13-40", berlin), Err(()));
        assert_eq!(show_moment("2026-10-04T22:00:00.000Z", berlin), "2026-10-05");
        assert_eq!(show_moment("2026-10-05T06:30:00Z", berlin), "2026-10-05 08:30");
    }

    #[test]
    fn calendar_times_are_all_day_or_timed() {
        let berlin: Tz = "Europe/Berlin".parse().unwrap();
        let all_day = block_times(t("2026-10-04T22:00:00Z"), t("2026-10-11T22:00:00Z"), berlin);
        assert_eq!(all_day, json!({"start": "2026-10-05T00:00:00", "duration": "P7D", "timeZone": null, "showWithoutTime": true}));
        assert_eq!(event_span(&all_day, berlin), Some((t("2026-10-04T22:00:00Z"), t("2026-10-11T22:00:00Z"))));
        let timed = block_times(t("2026-10-05T06:30:00Z"), t("2026-10-05T16:00:00Z"), berlin);
        assert_eq!(timed, json!({"start": "2026-10-05T08:30:00", "duration": "PT9H30M", "timeZone": "Europe/Berlin", "showWithoutTime": false}));
        assert_eq!(event_span(&timed, berlin), Some((t("2026-10-05T06:30:00Z"), t("2026-10-05T16:00:00Z"))));
        assert_eq!(iso_duration(0), "PT0S");
        assert_eq!(iso_duration(26 * 3600 * 1000), "P1DT2H");
        assert_eq!(duration_secs("P1DT2H30M"), Some(95400));
        assert_eq!(duration_secs("P2W"), Some(1209600));
        assert_eq!(duration_secs("PT5"), None);
    }

    #[test]
    fn the_event_carries_bulwarks_uid_and_marks() {
        let ev = new_block_event("a1", "cal9", "  ", t("2026-10-05T06:30:00Z"), t("2026-10-05T16:00:00Z"), chrono_tz::UTC);
        assert_eq!(ev["uid"], "bulwark-ooo-a1");
        assert_eq!(ev["title"], "Out of office");
        assert_eq!(ev["calendarIds"], json!({"cal9": true}));
        assert_eq!(ev["freeBusyStatus"], "busy");
        assert_eq!(ev["privacy"], "public");
        assert_eq!(ev["status"], "confirmed");
        assert_eq!(ev["keywords"], json!({"bulwark-out-of-office": true}));
        assert_eq!(ev["start"], "2026-10-05T06:30:00");
        let cals = vec![json!({"id": "t", "isTasksOnly": true, "isDefault": true}), json!({"id": "c1"}), json!({"id": "c2", "isDefault": true, "myRights": {"mayWriteAll": true}})];
        assert_eq!(block_calendar(&cals).unwrap()["id"], "c2");
        let ro = vec![json!({"id": "c", "isDefault": true, "myRights": {"mayWriteAll": false, "mayWriteOwn": false}})];
        assert!(block_calendar(&ro).is_none());
    }

    fn input<'a>(r: &'a Reply, event: Option<(&str, &str)>, last: Option<(&str, &str)>, trigger: Trigger, now: &str) -> DecisionInput<'a> {
        DecisionInput { block: true, reply: r, event: event.map(|(a, b)| (t(a), t(b))), last: last.map(|(a, b)| (a.into(), b.into())),
            previous: None, marked: false, now: t(now), trigger }
    }
    const FROM: &str = "2026-10-05T00:00:00Z";
    const TO: &str = "2026-10-12T00:00:00Z";

    #[test]
    fn block_decisions_follow_bulwark() {
        let on = reply(true, Some(FROM), Some(TO));
        // no event yet: create it for the dates
        assert_eq!(decide_block(&input(&on, None, None, Trigger::Save, "2026-09-30T00:00:00Z")), Decision::Create(t(FROM), t(TO)));
        // reply off or no dates: nothing
        assert_eq!(decide_block(&input(&reply(false, Some(FROM), Some(TO)), None, None, Trigger::Save, "2026-09-30T00:00:00Z")), Decision::None);
        assert_eq!(decide_block(&input(&reply(true, Some(FROM), None), None, None, Trigger::Save, "2026-09-30T00:00:00Z")), Decision::None);
        let mut off = input(&on, None, None, Trigger::Save, "2026-09-30T00:00:00Z");
        off.block = false;
        assert_eq!(decide_block(&off), Decision::None);
        // an event we wrote with other dates is moved
        let moved = reply(true, Some("2026-10-06T00:00:00Z"), Some(TO));
        assert_eq!(decide_block(&input(&moved, Some((FROM, TO)), Some((FROM, TO)), Trigger::Save, "2026-09-30T00:00:00Z")), Decision::Update(t("2026-10-06T00:00:00Z"), t(TO)));
        // the same dates: nothing
        assert_eq!(decide_block(&input(&on, Some((FROM, TO)), Some((FROM, TO)), Trigger::Save, "2026-09-30T00:00:00Z")), Decision::None);
        // someone edited the event: left alone
        assert_eq!(decide_block(&input(&moved, Some(("2026-10-07T00:00:00Z", TO)), Some((FROM, TO)), Trigger::Save, "2026-09-30T00:00:00Z")), Decision::None);
        // the reply switched off: a future event goes, a running one ends now, a past one stays
        let off = reply(false, Some(FROM), Some(TO));
        assert_eq!(decide_block(&input(&off, Some((FROM, TO)), Some((FROM, TO)), Trigger::Save, "2026-09-30T00:00:00Z")), Decision::Destroy);
        assert_eq!(decide_block(&input(&off, Some((FROM, TO)), Some((FROM, TO)), Trigger::Save, "2026-10-07T00:00:00Z")), Decision::EndNow(t("2026-10-07T00:00:00Z")));
        assert_eq!(decide_block(&input(&off, Some((FROM, TO)), Some((FROM, TO)), Trigger::Save, "2026-10-20T00:00:00Z")), Decision::None);
        // a reply that ran out leaves its event; and creates none
        assert_eq!(decide_block(&input(&on, Some((FROM, TO)), Some((FROM, TO)), Trigger::Load, "2026-10-20T00:00:00Z")), Decision::None);
        assert_eq!(decide_block(&input(&on, None, None, Trigger::Load, "2026-10-20T00:00:00Z")), Decision::None);
    }

    #[test]
    fn a_deleted_event_stays_deleted_until_the_dates_change() {
        let on = reply(true, Some(FROM), Some(TO));
        // on load, with a record for the same dates: it was deleted elsewhere
        assert_eq!(decide_block(&input(&on, None, Some((FROM, TO)), Trigger::Load, "2026-09-30T00:00:00Z")), Decision::None);
        // on a save it is made again
        assert_eq!(decide_block(&input(&on, None, Some((FROM, TO)), Trigger::Save, "2026-09-30T00:00:00Z")), Decision::Create(t(FROM), t(TO)));
        // a marked event with no record on this device counts as ours when loaded; the previous dates count on a save
        let moved = reply(true, Some("2026-10-06T00:00:00Z"), Some(TO));
        let mut i = input(&moved, Some((FROM, TO)), None, Trigger::Load, "2026-09-30T00:00:00Z");
        assert_eq!(decide_block(&i), Decision::None);
        i.marked = true;
        assert_eq!(decide_block(&i), Decision::Update(t("2026-10-06T00:00:00Z"), t(TO)));
        let mut i = input(&moved, Some((FROM, TO)), None, Trigger::Save, "2026-09-30T00:00:00Z");
        i.previous = Some((Some(FROM.into()), Some(TO.into())));
        assert_eq!(decide_block(&i), Decision::Update(t("2026-10-06T00:00:00Z"), t(TO)));
    }

    #[test]
    fn the_html_body_is_rebuilt_from_an_allowlist() {
        let clean = |h: &str| sanitize_html(h);
        // slash instead of a space before an attribute, unquoted javascript: links, entity-encoded schemes
        assert_eq!(clean(r#"<img src="x"/onerror=alert(1)>Hi"#), "Hi");
        assert_eq!(clean("<a href=javascript:alert(1)>x</a>"), "<a>x</a>");
        assert_eq!(clean(r#"<a href="&#106;avascript:alert(1)">x</a>"#), "<a>x</a>");
        assert_eq!(clean(r#"<a href="&#x6A;avascript:alert(1)">x</a>"#), "<a>x</a>");
        assert_eq!(clean(r#"<a href="&#106avascript:alert(1)">x</a>"#), "<a>x</a>");
        assert_eq!(clean("<a href=\"java\tscript:alert(1)\">x</a>"), "<a>x</a>");
        assert_eq!(clean(r#"<a href="  JaVaScRiPt:alert(1)">x</a>"#), "<a>x</a>");
        assert_eq!(clean(r#"<a href="data:text/html,<script>1</script>">x</a>"#), "<a>x</a>");
        assert_eq!(clean(r#"<a href="/relative">x</a>"#), "<a>x</a>");
        // forms, buttons, svg, styles and handlers
        assert_eq!(clean(r#"<form action="https://evil.test"><button formaction="javascript:1">Go</button></form>"#), "");
        assert_eq!(clean(r#"<svg onload=alert(1)><circle/></svg>ok"#), "ok");
        assert_eq!(clean(r#"<p style="x" class="y" id="z" onclick="evil()">Hi</p>"#), "<p>Hi</p>");
        assert_eq!(clean("<style>p{color:red}</style><iframe src=x></iframe><object data=x></object>text"), "text");
        // pieces cannot be joined into a tag, and an unfinished tag ends the text
        assert!(!clean("<scr<script></script>ipt>alert(1)</scr<script></script>ipt>").to_lowercase().contains("<script"));
        assert_eq!(clean("Hi <b onclick=\"x"), "Hi ");
        assert_eq!(clean("1 < 2"), "1 &lt; 2");
        assert_eq!(clean("<!-- <script>x</script> -->Hi"), "Hi");
        // what the editor produces stays; links keep only safe addresses and get rel
        assert_eq!(clean("<p><strong>Back</strong> on <em>Monday</em><br>Ann</p><ul><li>a</li></ul>"), "<p><strong>Back</strong> on <em>Monday</em><br>Ann</p><ul><li>a</li></ul>");
        assert_eq!(clean(r#"<a href="https://example.test/?a=1&amp;b=2" onclick="x" title="T">x</a>"#), r#"<a href="https://example.test/?a=1&amp;b=2" rel="noopener noreferrer" title="T">x</a>"#);
        assert_eq!(clean(r#"<a href='mailto:ann@example.test'>m</a>"#), r#"<a href="mailto:ann@example.test" rel="noopener noreferrer">m</a>"#);
        // the output of a sanitise is stable
        let once = clean(r#"<p>a &amp; b <a href="https://x.test/?q=1&amp;r=2">l</a></p>"#);
        assert_eq!(clean(&once), once);
    }

    #[test]
    fn the_plain_text_part_has_paragraph_spacing_and_decodes_once() {
        assert_eq!(html_to_plain("<p>Hello</p><p>there</p>"), "Hello\n\nthere");
        assert_eq!(html_to_plain("<p>a<br>b</p>"), "a\nb");
        assert_eq!(html_to_plain("<p>Tom &amp; Jerry &amp;lt;b&amp;gt; &#169; &nbsp;x</p>"), "Tom & Jerry &lt;b&gt; \u{a9}  x");
        assert_eq!(html_to_plain("<ul><li>one</li><li>two</li></ul><p>end</p>"), "- one\n- two\n\nend");
        assert_eq!(parts_from_html("<p>İzin</p>"), ("İzin".to_string(), Some("<p>İzin</p>".to_string())));
        assert!(!has_readable_text("<p> &nbsp; </p><script>x</script>"));
        assert!(has_readable_text("<p>x</p>"));
    }

    #[test]
    fn html_to_text_survives_characters_that_change_length_when_lowercased() {
        // `İ` lower-cases to a longer string; the old byte positions then pointed into a character
        assert_eq!(crate::html_to_text("<p>İzin</p><STYLE>x</STYLE><P>İ</P>"), "İzin\nİ");
        assert_eq!(crate::html_to_text("<p>&amp;lt;</p>"), "&lt;");
    }

    #[test]
    fn a_script_with_only_external_rules_keeps_running_with_the_reply() {
        let hand = "require [\"fileinto\"];\nif header :contains \"subject\" \"news\" { fileinto \"News\"; }\n";
        let scripts = vec![script("1", "vacation", true), script("2", "filters", false)];
        let on = SyncRequest { enabled: true, forward: None, audience: None, from: None, until: None };
        let Plan::Write { target, content, activate } = plan_sync(&on, &scripts, Some(hand), &ext()).unwrap() else { panic!("the hand-written rules must be put back in front of the vacation script") };
        assert_eq!(target.as_deref(), Some("2"));
        assert!(activate);
        assert!(content.contains(r#"include :personal :optional "vacation";"#), "{content}");
        assert!(content.contains(r#"fileinto "News";"#), "{content}");
        // its require is kept, so the script validates
        let require = content.lines().find(|l| l.starts_with("require")).unwrap();
        assert!(require.contains("\"fileinto\"") && require.contains("\"include\""), "{content}");
        let again = read_filters(&[script("1", "vacation", false), script("2", "filters", true)], Some(&content), &ext(), None);
        assert!(again.includes_vacation);
    }

    #[test]
    fn dates_compare_as_instants_and_a_gap_reads_later() {
        assert!(same_opt_moment(Some("2026-10-05T06:00:00Z"), Some("2026-10-05T06:00:00.000Z")));
        assert!(same_opt_moment(Some("2026-10-05T08:00:00+02:00"), Some("2026-10-05T06:00:00.000Z")));
        assert!(!same_opt_moment(Some("2026-10-05T06:00:00Z"), None));
        assert!(same_opt_moment(None, None));
        let berlin: Tz = "Europe/Berlin".parse().unwrap();
        // 02:30 does not exist on 2026-03-29 in Berlin: it reads as the first valid moment after the gap
        assert_eq!(parse_moment("2026-03-29 02:30", berlin), Ok(Some("2026-03-29T01:30:00.000Z".into())));
        assert_eq!(parse_moment("2026-03-29 03:30", berlin), Ok(Some("2026-03-29T01:30:00.000Z".into())));
        assert_eq!(parse_moment_dt("2026-03-29", berlin), Ok(Some(t("2026-03-28T23:00:00Z"))));
    }

    #[test]
    fn the_calendar_records_are_merged_not_replaced() {
        let one = with_block(None, "a1", Some((t("2026-10-05T00:00:00Z"), t("2026-10-12T00:00:00Z"))));
        assert_eq!(block_record(Some(&one), "a1"), Some(("2026-10-05T00:00:00.000Z".into(), "2026-10-12T00:00:00.000Z".into())));
        let two = with_block(Some(&one), "b2", Some((t("2026-11-01T00:00:00Z"), t("2026-11-02T00:00:00Z"))));
        assert!(block_record(Some(&two), "a1").is_some() && block_record(Some(&two), "b2").is_some());
        let none = with_block(Some(&two), "a1", None);
        assert!(block_record(Some(&none), "a1").is_none() && block_record(Some(&none), "b2").is_some());
        assert_eq!(block_record(None, "a1"), None);
        // a record that is not an object is replaced by one
        assert!(block_record(Some(&with_block(Some(&json!("x")), "a1", Some((t("2026-10-05T00:00:00Z"), t("2026-10-06T00:00:00Z"))))), "a1").is_some());
    }

    #[test]
    fn calendar_prefs_go_through_the_live_settings() {
        // update_pref changes the program's one settings object (so the settings page's next save keeps
        // it); this is the value it hands over and the reader that sees it
        let live = std::sync::Arc::new(std::sync::Mutex::new(crate::settings::Settings::with_prefs(Default::default())));
        {
            let mut s = live.lock().unwrap();
            let v = with_block(s.prefs.get("vacationCalendarBlocks"), "a1", Some((t("2026-10-05T00:00:00Z"), t("2026-10-06T00:00:00Z"))));
            s.prefs.insert("vacationCalendarBlocks".into(), v);
            s.prefs.insert("theme".into(), json!("dark"));
        }
        let s = live.lock().unwrap();
        assert!(block_record(s.prefs.get("vacationCalendarBlocks"), "a1").is_some());
        assert_eq!(s.prefs["theme"], "dark");
    }

    #[test]
    fn entities_next_to_letters_of_several_bytes_do_not_break_the_page() {
        // "&" before a Turkish letter: the named-entity check once cut the text inside the letter
        for html in ["<p>Ar-Ge & Ürün</p>", "<p>& Öğle</p>", "<p>&üü</p>", "<p>&ğ;x</p>", "<p>a&</p>", "<p>&#</p>", "<p>&#x</p>"] {
            let (text, h) = parts_from_html(html);
            assert!(h.is_some(), "{html}");
            assert!(has_readable_text(html), "{html}");
            assert!(!text.is_empty(), "{html}");
        }
        assert_eq!(parts_from_html("<p>Ar-Ge & Ürün</p>").0, "Ar-Ge & Ürün");
        assert_eq!(parts_from_html("<p>&üü</p>").0, "&üü");
        assert_eq!(parts_from_html("<p>&amp;Öğle &AMP; x</p>").0, "&Öğle & x");
        // in a link's title and href too
        let s = sanitize_html(r#"<a href="https://example.test/?a=1&Ü=2" title="R&D Ürün">x</a>"#);
        assert_eq!(s, r#"<a href="https://example.test/?a=1&amp;Ü=2" rel="noopener noreferrer" title="R&amp;D Ürün">x</a>"#);
        assert!(safe_href("&Ü").is_none() && safe_href("mailto:a@example.test?subject=Ar-Ge &Ürün").is_some());
    }

    #[test]
    fn numeric_references_never_give_control_characters() {
        // as browsers: NUL, surrogates and numbers past Unicode are U+FFFD, 0x80-0x9F Windows-1252
        assert_eq!(decode_entities("a&#0;b"), "a\u{fffd}b");
        assert_eq!(decode_entities("&#x0;&#1;&#x1F;&#127;"), "\u{fffd}\u{fffd}\u{fffd}\u{fffd}");
        assert_eq!(decode_entities("&#xD800;&#57343;&#x110000;&#99999999;"), "\u{fffd}\u{fffd}\u{fffd}\u{fffd}");
        assert_eq!(decode_entities("&#128;&#x99;&#x81;&#159;"), "€™\u{fffd}Ÿ");
        assert_eq!(decode_entities("&#9;&#10;&#13;&#65;&#x130;"), "\t\n\rAİ");
        assert!(!parts_from_html("<p>x&#0;y&#x7;z</p>").0.chars().any(|c| c.is_control()));
    }

    #[test]
    fn a_day_whose_midnight_is_skipped_stays_a_day() {
        // Chile puts the clocks forward at midnight: 2026-09-06 starts at 01:00 (-03)
        let scl: Tz = "America/Santiago".parse().unwrap();
        let day = NaiveDate::from_ymd_opt(2026, 9, 6).unwrap();
        assert!(scl.from_local_datetime(&day.and_time(NaiveTime::MIN)).single().is_none(), "the test needs a skipped midnight");
        let start = parse_moment_dt("2026-09-06", scl).unwrap().unwrap();
        assert_eq!(start, t("2026-09-06T04:00:00Z"));
        assert!(is_day_start(start, scl));
        assert_eq!(show_moment("2026-09-06T04:00:00.000Z", scl), "2026-09-06");
        let end = parse_moment_dt("2026-09-08", scl).unwrap().unwrap();
        // the block is an all-day event, and reads back as the same span
        let times = block_times(start, end, scl);
        assert_eq!(times, json!({"start": "2026-09-06T00:00:00", "duration": "P2D", "timeZone": null, "showWithoutTime": true}));
        assert_eq!(event_span(&times, scl), Some((start, end)));
        // an ordinary day and a time of day are unchanged
        let berlin: Tz = "Europe/Berlin".parse().unwrap();
        assert_eq!(parse_moment_dt("2026-10-05", berlin).unwrap(), Some(t("2026-10-04T22:00:00Z")));
        assert_eq!(show_moment("2026-10-05T06:30:00Z", berlin), "2026-10-05 08:30");
        assert!(!is_day_start(t("2026-09-06T05:00:00Z"), scl));
    }

    #[test]
    fn server_durations_too_big_are_no_span() {
        assert_eq!(duration_secs("P999999999999999D"), None);
        assert_eq!(duration_secs("PT9223372036854775807H"), None);
        assert_eq!(duration_secs("P1DT1S"), Some(86401));
        let tz: Tz = "Europe/Berlin".parse().unwrap();
        for d in ["P99999999D", "P999999999999D", "PT99999999999999S", "P99999999W"] {
            for all_day in [true, false] {
                let ev = json!({"start": "2026-10-05T00:00:00", "duration": d, "showWithoutTime": all_day, "timeZone": "Europe/Berlin"});
                assert_eq!(event_span(&ev, tz), None, "{d} {all_day}");
            }
        }
        // the last moment chrono knows, plus the gap hour: an error, not a panic
        assert!(parse_moment_dt("+262142-12-31 23:59", tz).is_err());
    }

    #[test]
    fn numeric_references_take_any_number_of_leading_zeros() {
        assert_eq!(decode_entities("&#0000000000065;&#x00000000041;&#0;"), "AA\u{fffd}");
        assert_eq!(decode_entities("&#123456789;x"), "\u{fffd}x");
        assert_eq!(decode_entities("&#x000000000;"), "\u{fffd}");
    }

    #[test]
    fn block_records_are_per_server_and_login_and_old_ones_still_read() {
        let s = |d: &str| t(d);
        let old = with_block(None, "b", Some((s("2026-10-05T00:00:00Z"), s("2026-10-06T00:00:00Z"))));
        // an older version's record, under the bare id, is still found
        assert_eq!(block_record_for(Some(&old), "https://one.example.test\nann\nb", "b").map(|r| r.0), Some("2026-10-05T00:00:00.000Z".into()));
        // writing moves it under the full key; another server's account "b" is not this one
        let new = with_block_for(Some(&old), "https://one.example.test\nann\nb", "b", Some((s("2026-11-01T00:00:00Z"), s("2026-11-02T00:00:00Z"))));
        assert!(block_record(Some(&new), "b").is_none());
        assert_eq!(block_record_for(Some(&new), "https://one.example.test\nann\nb", "b").map(|r| r.0), Some("2026-11-01T00:00:00.000Z".into()));
        assert_eq!(block_record_for(Some(&new), "https://two.example.test\nann\nb", "b"), None);
        let gone = with_block_for(Some(&new), "https://one.example.test\nann\nb", "b", None);
        assert_eq!(gone, json!({}));
    }
}
