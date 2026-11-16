//! Search query syntax, relevance and the "Search everything" fan-out across mail, contacts,
//! calendar and files.
//!
//! Bulwark: lib/global-search/query-parser.ts (operators), rank.ts (scoreHit, rankHits),
//! run-global-search.ts + providers/{mail,contacts,calendar,files}.ts (one query, every kind,
//! results published per kind as they land), lib/jmap/search-utils.ts (splitSearchText,
//! textConditions, buildJMAPFilter), lib/search-snippet.ts (SearchSnippet/get, RFC 8621 §5).

use serde_json::{json, Value};

// ---------------- query syntax (query-parser.ts) ----------------

#[derive(Clone, Copy, Debug, PartialEq, Default)]
pub enum Scope { #[default] All, Mail, Contacts, Calendar, Files }

impl Scope {
    pub fn name(self) -> &'static str {
        match self { Scope::All => "all", Scope::Mail => "mail", Scope::Contacts => "contacts", Scope::Calendar => "calendar", Scope::Files => "files" }
    }
    pub fn from_name(s: &str) -> Scope {
        match s { "mail" => Scope::Mail, "contacts" => Scope::Contacts, "calendar" => Scope::Calendar, "files" => Scope::Files, _ => Scope::All }
    }
}

/// Bulwark SearchFilters, the part the query syntax fills.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct MailFilters {
    pub from: Option<String>,
    pub to: Option<String>,
    pub subject: Option<String>,
    pub has_attachment: Option<bool>,
    pub unread: Option<bool>,
    pub starred: Option<bool>,
    pub after: Option<String>,
    pub before: Option<String>,
}

#[derive(Clone, Debug, Default, PartialEq)]
pub struct Parsed {
    pub raw: String,
    /// Free text for the server: words and quoted phrases, operators removed.
    pub text: String,
    /// Lower-cased words and phrases for local matching.
    pub terms: Vec<String>,
    pub scope: Scope,
    pub account: Option<String>,
    pub mail: MailFilters,
    /// `in:trash` / `in:junk`.
    pub mailbox_role: Option<&'static str>,
    /// `is:anything`: do not leave Trash and Spam out.
    pub include_trash_and_junk: bool,
    pub has_operators: bool,
}

#[derive(Clone, Debug, PartialEq)]
pub struct Token { pub key: Option<String>, pub value: String, pub quoted: bool, pub original: String }

fn scope_alias(s: &str) -> Option<Scope> {
    Some(match s {
        "all" | "everything" => Scope::All,
        "mail" | "email" | "emails" | "messages" => Scope::Mail,
        "contacts" | "contact" | "people" => Scope::Contacts,
        "calendar" | "calendars" | "events" | "event" => Scope::Calendar,
        "files" | "file" | "drive" => Scope::Files,
        _ => return None,
    })
}

const OPERATORS: [&str; 9] = ["in", "account", "from", "to", "subject", "has", "is", "after", "before"];

/// `YYYY-MM-DD` that names a real day.
pub fn valid_date(v: &str) -> bool {
    v.len() == 10 && chrono::NaiveDate::parse_from_str(v, "%Y-%m-%d").is_ok()
}

/// `key:"quoted value"`, `key:value`, `"quoted phrase"` or a bare word (TOKEN_RE). A missing
/// closing quote is fine (still typing).
pub fn tokenize(raw: &str) -> Vec<Token> {
    let mut out = vec![];
    let ch: Vec<char> = raw.chars().collect();
    let mut i = 0;
    let at = |i: usize| ch.get(i).copied();
    while i < ch.len() {
        if ch[i].is_whitespace() { i += 1; continue; }
        let start = i;
        // key: letters followed by ':'
        let mut j = i;
        while j < ch.len() && ch[j].is_ascii_alphabetic() { j += 1; }
        if j > i && at(j) == Some(':') {
            let key: String = ch[i..j].iter().collect();
            let mut k = j + 1;
            if at(k) == Some('"') {
                k += 1;
                let vs = k;
                while k < ch.len() && ch[k] != '"' { k += 1; }
                let value: String = ch[vs..k].iter().collect();
                if k < ch.len() { k += 1; }
                out.push(Token { key: Some(key.to_lowercase()), value, quoted: true, original: ch[start..k].iter().collect() });
                i = k;
                continue;
            }
            let vs = k;
            while k < ch.len() && !ch[k].is_whitespace() { k += 1; }
            if k > vs {
                let value: String = ch[vs..k].iter().collect();
                out.push(Token { key: Some(key.to_lowercase()), value, quoted: false, original: ch[start..k].iter().collect() });
                i = k;
                continue;
            }
            // `key:` with nothing after it: an (unfinished) operator with an empty value
            out.push(Token { key: Some(key.to_lowercase()), value: String::new(), quoted: false, original: ch[start..k].iter().collect() });
            i = k;
            continue;
        }
        if ch[i] == '"' {
            let mut k = i + 1;
            while k < ch.len() && ch[k] != '"' { k += 1; }
            let value: String = ch[i + 1..k].iter().collect();
            if k < ch.len() { k += 1; }
            out.push(Token { key: None, value, quoted: true, original: ch[start..k].iter().collect() });
            i = k;
            continue;
        }
        let mut k = i;
        while k < ch.len() && !ch[k].is_whitespace() { k += 1; }
        let w: String = ch[i..k].iter().collect();
        out.push(Token { key: None, value: w.clone(), quoted: false, original: w });
        i = k;
    }
    out
}

pub fn parse(raw: &str) -> Parsed {
    let mut p = Parsed { raw: raw.to_string(), ..Default::default() };
    let mut text: Vec<String> = vec![];
    let mut push = |p: &mut Parsed, v: &str, quoted: bool| {
        let t = v.trim();
        if t.is_empty() { return; }
        text.push(if quoted { format!("\"{t}\"") } else { t.to_string() });
        p.terms.push(t.to_lowercase());
    };
    for tok in tokenize(raw) {
        let Some(key) = tok.key.as_deref().filter(|k| OPERATORS.contains(k)) else {
            if tok.key.is_some() { push(&mut p, &tok.original, false); } else { push(&mut p, &tok.value, tok.quoted); }
            continue;
        };
        let value = tok.value.trim().to_string();
        let lower = value.to_lowercase();
        let mut used = true;
        match key {
            "in" => match lower.as_str() {
                l if scope_alias(l).is_some() => p.scope = scope_alias(l).unwrap(),
                "trash" | "bin" => { p.scope = Scope::Mail; p.mailbox_role = Some("trash"); }
                "junk" | "spam" => { p.scope = Scope::Mail; p.mailbox_role = Some("junk"); }
                _ => used = false,
            },
            "account" => if lower.is_empty() { used = false } else { p.account = Some(lower) },
            "from" => if value.is_empty() { used = false } else { p.mail.from = Some(value) },
            "to" => if value.is_empty() { used = false } else { p.mail.to = Some(value) },
            "subject" => if value.is_empty() { used = false } else { p.mail.subject = Some(value) },
            "has" => if matches!(lower.as_str(), "attachment" | "attachments" | "file") { p.mail.has_attachment = Some(true) } else { used = false },
            "is" => match lower.as_str() {
                "unread" => p.mail.unread = Some(true),
                "read" => p.mail.unread = Some(false),
                "starred" | "flagged" => p.mail.starred = Some(true),
                "unstarred" => p.mail.starred = Some(false),
                "anything" | "any" => p.include_trash_and_junk = true,
                _ => used = false,
            },
            "after" => if valid_date(&value) { p.mail.after = Some(value) } else { used = false },
            "before" => if valid_date(&value) { p.mail.before = Some(value) } else { used = false },
            _ => used = false,
        }
        if used { p.has_operators = true; } else { push(&mut p, &tok.original, false); }
    }
    p.text = text.join(" ");
    p
}

/// Rewrite the `in:` and `account:` operators of a query (the scope chips are sugar over them).
pub fn with_scope(raw: &str, scope: Scope) -> String {
    let mut kept: Vec<String> = tokenize(raw).into_iter()
        .filter(|t| !(t.key.as_deref() == Some("in") && scope_alias(&t.value.to_lowercase()).is_some()))
        .map(|t| t.original).collect();
    if scope != Scope::All { kept.push(format!("in:{}", scope.name())); }
    kept.join(" ")
}

/// The query with one operator (by key) taken out: removing a chip.
pub fn without_operator(raw: &str, key: &str) -> String {
    tokenize(raw).into_iter().filter(|t| t.key.as_deref() != Some(key)).map(|t| t.original).collect::<Vec<_>>().join(" ")
}

/// Every term is a substring of at least one field (matchesTerms).
pub fn matches_terms(terms: &[String], fields: &[&str]) -> bool {
    if terms.is_empty() { return false; }
    let hay: Vec<String> = fields.iter().filter(|f| !f.is_empty()).map(|f| f.to_lowercase()).collect();
    if hay.is_empty() { return false; }
    terms.iter().all(|t| hay.iter().any(|f| f.contains(t.as_str())))
}

// ---------------- JMAP filters (search-utils.ts) ----------------

/// Typed search text as the values to AND together (splitSearchText): every "phrase" and every
/// `word*` prefix on its own, the remaining words together as typed.
pub fn split_search_text(raw: &str) -> Vec<String> {
    let mut parts: Vec<String> = vec![];
    let mut words: Vec<String> = vec![];
    let mut words_at: Option<usize> = None;
    let ch: Vec<char> = raw.chars().collect();
    let mut i = 0;
    while i < ch.len() {
        if ch[i].is_whitespace() { i += 1; continue; }
        if ch[i] == '"' {
            let mut k = i + 1;
            while k < ch.len() && ch[k] != '"' { k += 1; }
            let phrase: String = ch[i + 1..k].iter().collect();
            let phrase = phrase.trim();
            if !phrase.is_empty() { parts.push(format!("\"{phrase}\"")); }
            i = (k + 1).min(ch.len());
            continue;
        }
        let mut k = i;
        while k < ch.len() && !ch[k].is_whitespace() && ch[k] != '"' { k += 1; }
        let w: String = ch[i..k].iter().collect();
        if w.ends_with('*') {
            if !w.chars().all(|c| c == '*') { parts.push(w); }
        } else {
            if words_at.is_none() { words_at = Some(parts.len()); }
            words.push(w);
        }
        i = k;
    }
    let mut bare = words.join(" ");
    while bare.len() > 1 && bare.starts_with('\'') && bare.ends_with('\'') { bare = bare[1..bare.len() - 1].trim().to_string(); }
    if !bare.is_empty() { parts.insert(words_at.unwrap_or(parts.len()).min(parts.len()), bare); }
    parts
}

/// One `{property: part}` condition per part (textConditions); the input as it is when no part
/// is left, so a lone `*` never widens the search to everything.
pub fn text_conditions(raw: &str, property: &str) -> Vec<Value> {
    let parts = split_search_text(raw);
    if parts.is_empty() { return vec![json!({ property: raw.trim() })]; }
    parts.into_iter().map(|p| json!({ property: p })).collect()
}

fn and_all(conds: Vec<Value>) -> Value {
    match conds.len() {
        0 => json!({}),
        1 => conds.into_iter().next().unwrap(),
        _ => json!({"operator": "AND", "conditions": conds}),
    }
}

/// End of a local day as UTC, for `before:` (inclusive, like Bulwark's setHours(23,59,59,999)).
fn day_bound(d: &str, end: bool) -> Option<String> {
    use chrono::TimeZone;
    let day = chrono::NaiveDate::parse_from_str(d, "%Y-%m-%d").ok()?;
    let t = if end { day.and_hms_opt(23, 59, 59)? } else { day.and_hms_opt(0, 0, 0)? };
    let local = chrono::Local.from_local_datetime(&t).earliest()?;
    Some(local.with_timezone(&chrono::Utc).format("%Y-%m-%dT%H:%M:%SZ").to_string())
}

/// The mail conditions of a parsed query (buildJMAPFilter), without the folder scope.
pub fn mail_conditions(p: &Parsed) -> Vec<Value> {
    let mut c: Vec<Value> = vec![];
    if !p.text.trim().is_empty() { c.extend(text_conditions(&p.text, "text")); }
    let m = &p.mail;
    if let Some(v) = &m.from { c.extend(text_conditions(v, "from")); }
    if let Some(v) = &m.to { c.extend(text_conditions(v, "to")); }
    if let Some(v) = &m.subject { c.extend(text_conditions(v, "subject")); }
    if let Some(a) = m.has_attachment { c.push(json!({"hasAttachment": a})); }
    if let Some(d) = m.after.as_deref().and_then(|d| day_bound(d, false)) { c.push(json!({"after": d})); }
    if let Some(d) = m.before.as_deref().and_then(|d| day_bound(d, true)) { c.push(json!({"before": d})); }
    match m.unread { Some(true) => c.push(json!({"notKeyword": "$seen"})), Some(false) => c.push(json!({"hasKeyword": "$seen"})), None => {} }
    match m.starred { Some(true) => c.push(json!({"hasKeyword": "$flagged"})), Some(false) => c.push(json!({"notKeyword": "$flagged"})), None => {} }
    c
}

/// The filter for the mail search box: the typed query (operators and all) inside `folder`.
/// `in:trash` / `in:junk` search that folder instead; `role_id` finds it.
pub fn mailbox_search_filter(text: &str, folder: Option<&str>, role_id: impl Fn(&str) -> Option<String>) -> Value {
    let p = parse(text);
    let mut c = mail_conditions(&p);
    if c.is_empty() { c.push(json!({"text": text.trim()})); }
    match p.mailbox_role.and_then(&role_id) {
        Some(id) => c.insert(0, json!({"inMailbox": id})),
        None => if let Some(f) = folder.filter(|f| !f.is_empty()) { c.insert(0, json!({"inMailbox": f})); },
    }
    and_all(c)
}

/// The filter for "Search everything": everything but Trash and Spam (Bulwark's default
/// scope), `in:trash` / `in:junk` that folder alone, `is:anything` every folder.
pub fn global_mail_filter(p: &Parsed, role_id: impl Fn(&str) -> Option<String>) -> Value {
    let mut c = mail_conditions(p);
    if let Some(role) = p.mailbox_role {
        if let Some(id) = role_id(role) { c.push(json!({"inMailbox": id})); }
    } else if !p.include_trash_and_junk {
        let skip: Vec<String> = ["trash", "junk"].iter().filter_map(|r| role_id(r)).collect();
        if !skip.is_empty() { c.push(json!({"inMailboxOtherThan": skip})); }
    }
    and_all(c)
}

/// Whether the query has anything the server can be asked for this kind (hasRemoteQuery).
pub fn has_remote_query(p: &Parsed, scope: Scope) -> bool {
    if !p.text.trim().is_empty() { return true; }
    scope == Scope::Mail && (p.mail != MailFilters::default() || p.mailbox_role.is_some())
}

/// The removable chips of a mail search: (operator key, label, value) for each operator in force.
pub fn chips(p: &Parsed) -> Vec<(&'static str, String, String)> {
    let m = &p.mail;
    let mut v = vec![];
    if let Some(x) = &m.from { v.push(("from", "From".into(), x.clone())); }
    if let Some(x) = &m.to { v.push(("to", "To".into(), x.clone())); }
    if let Some(x) = &m.subject { v.push(("subject", "Subject".into(), x.clone())); }
    if m.has_attachment == Some(true) { v.push(("has", "Attachments".into(), "Yes".into())); }
    if let Some(x) = &m.after { v.push(("after", "After".into(), x.clone())); }
    if let Some(x) = &m.before { v.push(("before", "Before".into(), x.clone())); }
    match m.unread { Some(true) => v.push(("is", "Unread".into(), String::new())), Some(false) => v.push(("is", "Read".into(), String::new())), None => {} }
    match m.starred { Some(s) => v.push(("is", "Starred".into(), if s { "Yes".into() } else { "No".into() })), None => {} }
    if let Some(r) = p.mailbox_role { v.push(("in", "In".into(), if r == "trash" { "Trash".into() } else { "Spam".into() })); }
    v
}

// ---------------- SearchSnippet/get (search-snippet.ts) ----------------

/// Whether a filter has a term the server can highlight (text, subject or body).
pub fn has_snippet_terms(f: &Value) -> bool {
    if let Some(c) = f["conditions"].as_array() { return c.iter().any(has_snippet_terms); }
    ["text", "subject", "body"].iter().any(|k| f[*k].as_str().is_some_and(|s| !s.trim().is_empty()))
}

/// The filter for SearchSnippet/get: phrases unquoted and `word*` prefixes without the star,
/// so Stalwart marks the words wherever they occur (snippetFilterFor).
pub fn snippet_filter(f: &Value) -> Value {
    match f {
        Value::Object(o) => {
            let mut out = serde_json::Map::new();
            for (k, v) in o {
                let nv = if k == "conditions" {
                    Value::Array(v.as_array().map(|a| a.iter().map(snippet_filter).collect()).unwrap_or_default())
                } else if matches!(k.as_str(), "text" | "subject" | "body") && v.is_string() {
                    let s = v.as_str().unwrap();
                    let t = s.trim();
                    if t.len() >= 2 && ((t.starts_with('"') && t.ends_with('"')) || (t.starts_with('\'') && t.ends_with('\''))) {
                        json!(t[1..t.len() - 1])
                    } else {
                        json!(s.split_whitespace().map(|w| if w.len() > 1 && w.ends_with('*') { &w[..w.len() - 1] } else { w }).collect::<Vec<_>>().join(" "))
                    }
                } else { v.clone() };
                out.insert(k.clone(), nv);
            }
            Value::Object(out)
        }
        v => v.clone(),
    }
}

/// A snippet split into (text, marked) runs; only `<mark>` / `</mark>` are tags, the rest is
/// escaped text (parseSearchSnippet).
pub fn snippet_segments(s: &str) -> Vec<(String, bool)> {
    let mut out = vec![];
    let mut marked = false;
    let mut rest = s;
    loop {
        let open = rest.find("<mark>");
        let close = rest.find("</mark>");
        let next = match (open, close) { (Some(a), Some(b)) => Some(a.min(b)), (a, b) => a.or(b) };
        let Some(at) = next else { break };
        if at > 0 { out.push((decode(&rest[..at]), marked)); }
        let is_open = rest[at..].starts_with("<mark>");
        marked = is_open;
        rest = &rest[at + if is_open { 6 } else { 7 }..];
    }
    if !rest.is_empty() { out.push((decode(rest), marked)); }
    out
}

fn decode(s: &str) -> String {
    s.replace("&lt;", "<").replace("&gt;", ">").replace("&quot;", "\"").replace("&apos;", "'").replace("&#39;", "'").replace("&#x27;", "'").replace("&amp;", "&")
}

pub fn has_marked(segs: &[(String, bool)]) -> bool { segs.iter().any(|(t, m)| *m && !t.is_empty()) }

/// Markdown for Slint's StyledText: plain runs escaped, marked runs bold in `color`.
pub fn snippet_markdown(segs: &[(String, bool)], color: &str) -> String {
    let mut out = String::new();
    for (t, m) in segs {
        let t = t.replace(['\n', '\r'], " ");
        let e = md_escape(&t);
        if *m && !t.trim().is_empty() {
            // keep the spaces outside the strong run so CommonMark sees it as emphasis
            let lead = &t[..t.len() - t.trim_start().len()];
            let trail = &t[t.trim_end().len()..];
            out.push_str(lead);
            out.push_str(&format!("<font color=\"{color}\">**{}**</font>", md_escape(t.trim())));
            out.push_str(trail);
        } else {
            out.push_str(&e);
        }
    }
    out
}

fn md_escape(s: &str) -> String {
    let mut o = String::with_capacity(s.len());
    for c in s.chars() {
        if "\\`*_{}[]()#+-.!<>|~&".contains(c) { o.push('\\'); }
        o.push(c);
    }
    o
}

// ---------------- relevance (rank.ts) ----------------

#[derive(Clone, Debug, PartialEq)]
pub struct Hit {
    pub kind: Scope,
    pub id: String,
    pub title: String,
    pub subtitle: String,
    /// RFC 3339 or YYYY-MM-DD..., for recency.
    pub date: String,
    /// Where to go: mail folder id, file parent key, event start date.
    pub place: String,
}

fn at_word_boundary(field: &str, term: &str) -> bool {
    let mut from = 0;
    while let Some(i) = field[from..].find(term) {
        let at = from + i;
        let prev = field[..at].chars().last();
        if prev.is_none_or(|c| !c.is_alphanumeric()) { return true; }
        from = at + term.len().max(1);
        if from >= field.len() { break; }
    }
    false
}

/// exact > prefix > whole words > substrings of the title > a subtitle match > anything else.
pub fn score(h: &Hit, p: &Parsed) -> i32 {
    let q = p.terms.join(" ");
    if q.is_empty() { return 10; }
    let title = h.title.to_lowercase();
    if title == q { return 100; }
    if title.starts_with(&q) { return 80; }
    if p.terms.iter().all(|t| at_word_boundary(&title, t)) { return 60; }
    if p.terms.iter().all(|t| title.contains(t.as_str())) { return 50; }
    if p.terms.iter().any(|t| title.contains(t.as_str())) { return 30; }
    let sub = h.subtitle.to_lowercase();
    if p.terms.iter().any(|t| sub.contains(t.as_str())) { return 20; }
    10
}

/// Score, then newest first, then title.
pub fn rank(mut hits: Vec<Hit>, p: &Parsed) -> Vec<Hit> {
    hits.sort_by(|a, b| score(b, p).cmp(&score(a, p)).then_with(|| b.date.cmp(&a.date)).then_with(|| a.title.cmp(&b.title)));
    hits
}

// ---------------- providers ----------------

pub const REMOTE_LIMIT: usize = 25;
pub const ROWS_PER_KIND: usize = 5;

/// Mail hits from the server: Email/query + Email/get (providers/mail.ts remote).
/// The folder a mail hit opens in: the first of `prefer` (open folder, Inbox) it is in, else
/// its first folder in id order (stable, unlike the map's order).
pub fn hit_folder(m: &Value, prefer: &[String]) -> String {
    let Some(o) = m["mailboxIds"].as_object() else { return String::new() };
    if let Some(p) = prefer.iter().find(|p| !p.is_empty() && o.get(p.as_str()) == Some(&Value::Bool(true))) { return p.clone(); }
    let mut keys: Vec<&String> = o.iter().filter(|(_, v)| **v == Value::Bool(true)).map(|(k, _)| k).collect();
    keys.sort();
    keys.first().map(|k| k.to_string()).unwrap_or_default()
}

pub fn search_mail(c: &crate::jmap::Client, p: &Parsed, roles: &std::collections::HashMap<String, String>, prefer: &[String], folder_name: impl Fn(&str) -> String) -> Result<Vec<Hit>, String> {
    let filter = global_mail_filter(p, |r| roles.get(r).cloned());
    let r = c.call(json!([
        ["Email/query", {"accountId": c.account, "filter": filter, "sort": crate::jmap::list_sort(), "limit": REMOTE_LIMIT}, "q"],
        ["Email/get", {"accountId": c.account, "#ids": {"resultOf": "q", "name": "Email/query", "path": "/ids"},
            "properties": ["id", "subject", "from", "receivedAt", "mailboxIds", "preview"]}, "g"]
    ]))?;
    Ok(r[1][1]["list"].as_array().cloned().unwrap_or_default().iter().map(|m| {
        let folder = hit_folder(m, prefer);
        let from = crate::mail::sender(m);
        let fname = folder_name(&folder);
        Hit {
            kind: Scope::Mail,
            id: m["id"].as_str().unwrap_or("").into(),
            title: m["subject"].as_str().filter(|s| !s.is_empty()).unwrap_or("(no subject)").into(),
            subtitle: [from.as_str(), fname.as_str()].iter().filter(|s| !s.is_empty()).copied().collect::<Vec<_>>().join(" · "),
            date: m["receivedAt"].as_str().unwrap_or("").into(),
            place: folder,
        }
    }).collect())
}

fn card_name(c: &Value) -> String {
    if let Some(f) = c["name"]["full"].as_str().filter(|s| !s.is_empty()) { return f.to_string(); }
    let comps: Vec<&str> = c["name"]["components"].as_array().map(|a| a.iter().filter_map(|x| x["value"].as_str()).collect()).unwrap_or_default();
    comps.join(" ")
}

fn card_values(c: &Value, map: &str, field: &str) -> Vec<String> {
    c[map].as_object().map(|o| o.values().filter_map(|v| v[field].as_str().map(String::from)).collect()).unwrap_or_default()
}

/// Contacts the server finds for the text (ContactCard/query `text`, RFC 9610), narrowed to
/// cards whose name, address, company, phone or nickname has every term.
pub fn search_contacts(c: &crate::jmap::Client, p: &Parsed) -> Result<Vec<Hit>, String> {
    let acc = c.account_for("urn:ietf:params:jmap:contacts");
    let filter = and_all(text_conditions(&p.text, "text"));
    let r = c.call(json!([
        ["ContactCard/query", {"accountId": acc, "filter": filter, "limit": REMOTE_LIMIT}, "q"],
        ["ContactCard/get", {"accountId": acc, "#ids": {"resultOf": "q", "name": "ContactCard/query", "path": "/ids"},
            "properties": ["id", "name", "emails", "organizations", "phones", "nicknames", "kind", "updated"]}, "g"]
    ]))?;
    Ok(r[1][1]["list"].as_array().cloned().unwrap_or_default().iter().filter(|card| card["kind"] != "group").map(|card| {
        let name = card_name(card);
        let email = card_values(card, "emails", "address").into_iter().next().unwrap_or_default();
        let title = if name.is_empty() { email.clone() } else { name.clone() };
        Hit {
            kind: Scope::Contacts,
            id: card["id"].as_str().unwrap_or("").into(),
            subtitle: if email != title { email } else { String::new() },
            title,
            date: card["updated"].as_str().unwrap_or("").into(),
            place: String::new(),
        }
    }).collect())
}

/// Events the server finds (CalendarEvent/query `text`), newest first.
pub fn search_events(c: &crate::jmap::Client, p: &Parsed) -> Result<Vec<Hit>, String> {
    let acc = c.account_for("urn:ietf:params:jmap:calendars");
    let mut conds: Vec<Value> = text_conditions(&p.text, "text");
    if let Some(a) = &p.mail.after { conds.push(json!({"after": format!("{a}T00:00:00Z")})); }
    if let Some(b) = &p.mail.before { conds.push(json!({"before": format!("{b}T23:59:59Z")})); }
    let r = c.call(json!([
        ["CalendarEvent/query", {"accountId": acc, "filter": and_all(conds), "sort": [{"property": "start", "isAscending": false}], "limit": REMOTE_LIMIT}, "q"],
        ["CalendarEvent/get", {"accountId": acc, "#ids": {"resultOf": "q", "name": "CalendarEvent/query", "path": "/ids"},
            "properties": ["id", "@type", "title", "start", "locations", "recurrenceRules", "uid"]}, "g"]
    ]))?;
    let mut seen = std::collections::HashSet::new();
    Ok(r[1][1]["list"].as_array().cloned().unwrap_or_default().iter()
        .filter(|e| !e["@type"].as_str().is_some_and(|t| t.eq_ignore_ascii_case("task")))
        // a series once, not per occurrence
        .filter(|e| e["uid"].as_str().is_none_or(|u| seen.insert(u.to_string())))
        .map(|e| {
            let start = e["start"].as_str().unwrap_or("").to_string();
            let loc = e["locations"].as_object().and_then(|o| o.values().next()).and_then(|l| l["name"].as_str()).unwrap_or("");
            let when = chrono::NaiveDateTime::parse_from_str(&start[..start.len().min(19)], "%Y-%m-%dT%H:%M:%S").map(|d| d.format("%a %d %b %Y %H:%M").to_string()).unwrap_or_default();
            let recurring = e["recurrenceRules"].as_array().is_some_and(|a| !a.is_empty());
            Hit {
                kind: Scope::Calendar,
                id: e["id"].as_str().unwrap_or("").into(),
                title: e["title"].as_str().filter(|s| !s.is_empty()).unwrap_or("(no title)").into(),
                subtitle: [when.as_str(), loc, if recurring { "Recurring" } else { "" }].iter().filter(|s| !s.is_empty()).copied().collect::<Vec<_>>().join(" · "),
                date: start.clone(),
                place: start.get(..10).unwrap_or("").into(),
            }
        }).collect())
}

/// Files have no usable server search, so the listing is filtered here (providers/files.ts):
/// name or kind matches every term; the `.Trash` folder and what is in it are left out.
pub fn filter_files(nodes: &[Value], p: &Parsed) -> Vec<Hit> {
    let by_id: std::collections::HashMap<&str, &Value> = nodes.iter().filter_map(|n| Some((n["id"].as_str()?, n))).collect();
    let trash = nodes.iter().find(|n| n["name"] == crate::files::TRASH_NAME && n["parentId"].as_str().is_none_or(|s| s.is_empty())).and_then(|n| n["id"].as_str());
    let in_trash = |n: &Value| {
        let mut cur = n["parentId"].as_str();
        let mut guard = 0;
        while let Some(pid) = cur.filter(|s| !s.is_empty()) {
            if Some(pid) == trash { return true; }
            cur = by_id.get(pid).and_then(|x| x["parentId"].as_str());
            guard += 1;
            if guard > 64 { break; }
        }
        false
    };
    let path = |n: &Value| {
        let mut segs = vec![];
        let mut cur = n["parentId"].as_str();
        let mut guard = 0;
        while let Some(pid) = cur.filter(|s| !s.is_empty()) {
            let Some(parent) = by_id.get(pid) else { break };
            segs.insert(0, parent["name"].as_str().unwrap_or("").to_string());
            cur = parent["parentId"].as_str();
            guard += 1;
            if guard > 64 { break; }
        }
        format!("/{}", segs.join("/"))
    };
    let mut hits = vec![];
    for n in nodes {
        let id = n["id"].as_str().unwrap_or("");
        if Some(id) == trash || in_trash(n) { continue; }
        let dir = n["blobId"].is_null() || n["nodeType"] == "directory" || n["@type"] == "Directory";
        let kind = if dir { "folder" } else { n["type"].as_str().or(n["mediaType"].as_str()).unwrap_or("") };
        let name = n["name"].as_str().unwrap_or("");
        if !matches_terms(&p.terms, &[name, kind]) { continue; }
        hits.push(Hit {
            kind: Scope::Files,
            id: id.into(),
            title: name.into(),
            subtitle: path(n),
            date: n["modified"].as_str().or(n["created"].as_str()).unwrap_or("").into(),
            place: if dir { id.into() } else { n["parentId"].as_str().unwrap_or("").into() },
        });
    }
    hits
}

/// The primary account's file listing (FileNode/query + get), for filter_files.
pub fn file_nodes(c: &crate::jmap::Client) -> Result<Vec<Value>, String> {
    let acc = c.account_for("urn:ietf:params:jmap:filenode");
    let ids = c.query_all("FileNode/query", &acc, json!({}))?;
    c.get_all("FileNode/get", &acc, &ids, Some(&json!(["id", "parentId", "name", "type", "mediaType", "blobId", "nodeType", "@type", "modified", "created"])))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn mail_hit_prefers_open_folder_then_inbox() {
        let m = json!({"mailboxIds": {"zz": true, "inbox1": true, "cur": true}});
        assert_eq!(hit_folder(&m, &["cur".into(), "inbox1".into()]), "cur");
        assert_eq!(hit_folder(&m, &["other".into(), "inbox1".into()]), "inbox1");
        assert_eq!(hit_folder(&m, &["".into(), "".into()]), "cur");
        assert_eq!(hit_folder(&json!({"mailboxIds": {"b": true, "a": true}}), &[]), "a");
        assert_eq!(hit_folder(&json!({}), &["x".into()]), "");
    }

    #[test]
    fn parses_operators() {
        let p = parse("from:alice subject:\"q3 report\" has:attachment is:unread budget after:2026-01-02 before:2026-13-01");
        assert_eq!(p.mail.from.as_deref(), Some("alice"));
        assert_eq!(p.mail.subject.as_deref(), Some("q3 report"));
        assert_eq!(p.mail.has_attachment, Some(true));
        assert_eq!(p.mail.unread, Some(true));
        assert_eq!(p.mail.after.as_deref(), Some("2026-01-02"));
        // an impossible date stays text
        assert_eq!(p.mail.before, None);
        assert_eq!(p.text, "budget before:2026-13-01");
        assert!(p.has_operators);
        assert_eq!(p.terms, vec!["budget", "before:2026-13-01"]);
    }

    #[test]
    fn unknown_keys_and_phrases_stay_text() {
        let p = parse("http://x.y \"exact words\" foo:bar");
        assert_eq!(p.text, "http://x.y \"exact words\" foo:bar");
        assert!(!p.has_operators);
        let p = parse("in:files invoice");
        assert_eq!(p.scope, Scope::Files);
        assert_eq!(p.text, "invoice");
        let p = parse("in:trash old");
        assert_eq!(p.scope, Scope::Mail);
        assert_eq!(p.mailbox_role, Some("trash"));
        let p = parse("is:anything x");
        assert!(p.include_trash_and_junk);
        let p = parse("subject:\"unfinished");
        assert_eq!(p.mail.subject.as_deref(), Some("unfinished"));
        let p = parse("from: alone");
        assert_eq!(p.mail.from, None);
        assert_eq!(p.text, "from: alone");
    }

    #[test]
    fn scope_rewrite_and_chip_removal() {
        assert_eq!(with_scope("hello in:mail", Scope::Files), "hello in:files");
        assert_eq!(with_scope("hello in:trash", Scope::All), "hello in:trash");
        assert_eq!(with_scope("hello in:mail", Scope::All), "hello");
        assert_eq!(without_operator("from:a budget to:b", "from"), "budget to:b");
    }

    #[test]
    fn split_text_like_bulwark() {
        assert_eq!(split_search_text("annual report"), vec!["annual report"]);
        assert_eq!(split_search_text("\"annual report\" 2024 \"draft\""), vec!["\"annual report\"", "2024", "\"draft\""]);
        assert_eq!(split_search_text("project budg*"), vec!["project", "budg*"]);
        assert_eq!(split_search_text("*"), Vec::<String>::new());
        assert_eq!(split_search_text("'quoted'"), vec!["quoted"]);
        assert_eq!(text_conditions("*", "text"), vec![json!({"text": "*"})]);
    }

    #[test]
    fn mailbox_filter() {
        let roles = |r: &str| match r { "trash" => Some("T".to_string()), "junk" => Some("J".to_string()), _ => None };
        // plain text: one text condition in the folder
        assert_eq!(mailbox_search_filter("hello world", Some("INBOX"), roles),
            json!({"operator": "AND", "conditions": [{"inMailbox": "INBOX"}, {"text": "hello world"}]}));
        let f = mailbox_search_filter("from:bob has:attachment is:starred", None, roles);
        assert_eq!(f, json!({"operator": "AND", "conditions": [{"from": "bob"}, {"hasAttachment": true}, {"hasKeyword": "$flagged"}]}));
        let f = mailbox_search_filter("in:trash x", Some("INBOX"), roles);
        assert_eq!(f["conditions"][0], json!({"inMailbox": "T"}));
        let g = global_mail_filter(&parse("x"), roles);
        assert_eq!(g, json!({"operator": "AND", "conditions": [{"text": "x"}, {"inMailboxOtherThan": ["T", "J"]}]}));
        let g = global_mail_filter(&parse("x is:anything"), roles);
        assert_eq!(g, json!({"text": "x"}));
        let f = mailbox_search_filter("before:2026-03-01", None, roles);
        assert!(f["before"].as_str().is_some_and(|s| s.ends_with('Z')));
    }

    #[test]
    fn remote_query_needed() {
        assert!(has_remote_query(&parse("x"), Scope::Contacts));
        assert!(!has_remote_query(&parse("is:unread"), Scope::Contacts));
        assert!(has_remote_query(&parse("is:unread"), Scope::Mail));
        assert!(!has_remote_query(&parse(""), Scope::Mail));
    }

    #[test]
    fn chips_follow_operators() {
        let c = chips(&parse("from:ann is:unread has:attachment in:junk"));
        let keys: Vec<&str> = c.iter().map(|x| x.0).collect();
        assert_eq!(keys, vec!["from", "has", "is", "in"]);
        assert_eq!(c[0].2, "ann");
    }

    #[test]
    fn snippets() {
        let s = snippet_segments("Q3 <mark>budget</mark> &amp; plan &lt;b&gt;");
        assert_eq!(s, vec![("Q3 ".into(), false), ("budget".into(), true), (" & plan <b>".into(), false)]);
        assert!(has_marked(&s));
        assert!(!has_marked(&snippet_segments("no marks here")));
        let md = snippet_markdown(&s, "#ff0000");
        assert_eq!(md, "Q3 <font color=\"#ff0000\">**budget**</font> \\& plan \\<b\\>");
        assert!(slint::StyledText::from_markdown(&md).is_ok());
        // unexpected tags are text
        assert_eq!(snippet_segments("<b>x</b>"), vec![("<b>x</b>".into(), false)]);
    }

    #[test]
    fn snippet_filter_loosens_terms() {
        let f = json!({"operator": "AND", "conditions": [{"inMailbox": "a"}, {"text": "\"annual report\""}, {"subject": "budg* plan"}]});
        assert!(has_snippet_terms(&f));
        assert!(!has_snippet_terms(&json!({"inMailbox": "a"})));
        let s = snippet_filter(&f);
        assert_eq!(s["conditions"][1]["text"], "annual report");
        assert_eq!(s["conditions"][2]["subject"], "budg plan");
        assert_eq!(s["conditions"][0]["inMailbox"], "a");
    }

    #[test]
    fn ranking() {
        let p = parse("report");
        let h = |t: &str, d: &str| Hit { kind: Scope::Mail, id: t.into(), title: t.into(), subtitle: String::new(), date: d.into(), place: String::new() };
        let ranked = rank(vec![h("the annual reports", "2026-01-01"), h("misc", "2026-05-01"), h("report", "2020-01-01"), h("reporting q3", "2021-01-01")], &p);
        let titles: Vec<&str> = ranked.iter().map(|x| x.title.as_str()).collect();
        assert_eq!(titles, vec!["report", "reporting q3", "the annual reports", "misc"]);
        // ties go to the newest
        let ranked = rank(vec![h("misc a", "2026-01-01"), h("misc b", "2026-05-01")], &parse("zzz"));
        assert_eq!(ranked[0].title, "misc b");
    }

    #[test]
    fn files_filter_skips_trash() {
        let nodes = vec![
            json!({"id": "t", "name": ".Trash", "parentId": null}),
            json!({"id": "b", "name": "1~", "parentId": "t"}),
            json!({"id": "x", "name": "report.pdf", "parentId": "b", "blobId": "B", "type": "application/pdf"}),
            json!({"id": "d", "name": "Work", "parentId": null}),
            json!({"id": "y", "name": "Report 2026.docx", "parentId": "d", "blobId": "C"}),
            json!({"id": "z", "name": "Reports", "parentId": null}),
        ];
        let hits = filter_files(&nodes, &parse("report"));
        let ids: Vec<&str> = hits.iter().map(|h| h.id.as_str()).collect();
        assert_eq!(ids, vec!["y", "z"]);
        assert_eq!(hits[0].subtitle, "/Work");
        assert_eq!(hits[0].place, "d");
        // a folder opens itself
        assert_eq!(hits[1].place, "z");
        // "folder" matches folders by kind
        assert_eq!(filter_files(&nodes, &parse("folder work")).len(), 1);
    }

    #[test]
    fn terms_match_any_field() {
        let t = vec!["ann".to_string(), "acme".to_string()];
        assert!(matches_terms(&t, &["Ann Lee", "ann@acme.com"]));
        assert!(!matches_terms(&t, &["Ann Lee", ""]));
        assert!(!matches_terms(&[], &["x"]));
    }
}
