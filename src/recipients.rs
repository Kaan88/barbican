//! Recipient addresses for the composer, after Bulwark:
//! - lib/email-composer-utils.ts: splitRecipients, parseRecipient (with RFC 5322 group
//!   syntax for contact groups), formatRecipientEntry, expandRecipients,
//!   invalidRecipientAddresses, splitPastedRecipients, defaultSuggestionIndex
//! - lib/rfc5322-mailbox.ts: splitMailbox, sanitizeDisplayName
//! - lib/idn.ts: domains go out in ASCII (punycode) form, shown in Unicode only when they
//!   decode back to the same ASCII form
//! - lib/validation.ts: isValidEmail
//! - stores/contact-store.ts getAutocomplete: matchRank, normalizeSuggestions, 10 results
//!
//! The pure parts live here; `chips.rs` wires them to the composer's recipient fields.

/// One recipient: an address, or a contact group carrying a snapshot of its members.
#[derive(Clone, Debug, PartialEq, Default)]
pub struct Recipient {
    pub name: String,
    pub email: String,
    pub group: Option<Vec<Recipient>>,
}

impl Recipient {
    pub fn addr(name: &str, email: &str) -> Recipient {
        Recipient { name: name.to_string(), email: email.to_string(), group: None }
    }
}

/// An autocomplete entry: a person, or a contact group (empty email, `group` = (id, members)).
#[derive(Clone, Debug, PartialEq, Default)]
pub struct Suggestion {
    pub name: String,
    pub email: String,
    pub group: Option<(String, usize)>,
}

pub const MAX_SUGGESTIONS: usize = 10;

// ---------------------------------------------------------------- IDN (lib/idn.ts)

/// The ASCII form of a domain (`bücher.de` -> `xn--bcher-kva.de`), UTS #46 mapped and
/// lowercased; None when it is not a valid host name.
pub fn to_ascii_domain(domain: &str) -> Option<String> {
    let d = domain.trim();
    if d.is_empty() || d.chars().any(|c| c.is_whitespace() || "/\\?#@:%[]".contains(c)) { return None; }
    idna::domain_to_ascii(d).ok().filter(|s| !s.is_empty())
}

/// The address with a non-ASCII domain in ASCII form. ASCII addresses and the local part are
/// never touched (the local part is case-sensitive to the server).
pub fn to_ascii_email(address: &str) -> String {
    let Some(at) = address.rfind('@').filter(|&i| i > 0) else { return address.to_string() };
    let domain = &address[at + 1..];
    if domain.is_ascii() { return address.to_string(); }
    match to_ascii_domain(domain) {
        Some(a) => format!("{}@{a}", &address[..at]),
        None => address.to_string(),
    }
}

/// The Unicode form of a domain for display; unchanged unless it has an `xn--` label that
/// decodes to a domain whose ASCII form is exactly the input.
pub fn to_unicode_domain(domain: &str) -> String {
    let lower = domain.to_lowercase();
    if !lower.split('.').any(|l| l.starts_with("xn--")) { return domain.to_string(); }
    let (uni, res) = idna::domain_to_unicode(&lower);
    if res.is_err() { return domain.to_string(); }
    if to_ascii_domain(&uni).as_deref() == Some(lower.as_str()) { uni } else { domain.to_string() }
}

pub fn to_unicode_email(address: &str) -> String {
    let Some(at) = address.rfind('@').filter(|&i| i > 0) else { return address.to_string() };
    let domain = &address[at + 1..];
    let uni = to_unicode_domain(domain);
    if uni == domain { address.to_string() } else { format!("{}@{uni}", &address[..at]) }
}

// ---------------------------------------------------------------- validation (lib/validation.ts)

fn label_ok(l: &str) -> bool {
    let b = l.as_bytes();
    !b.is_empty() && b.len() <= 63
        && b[0].is_ascii_alphanumeric() && b[b.len() - 1].is_ascii_alphanumeric()
        && b.iter().all(|c| c.is_ascii_alphanumeric() || *c == b'-')
}

/// Bulwark isValidEmail (RFC 5322 dot-atom local part, LDH domain labels, length limits,
/// no control characters or brackets), applied to the ASCII form so an IDN domain counts.
pub fn is_valid_email(email: &str) -> bool {
    let e = to_ascii_email(email);
    if e.is_empty() || e.len() > 254 { return false; }
    if e.chars().any(|c| matches!(c, '\r' | '\n' | '\0' | '<' | '>')) { return false; }
    let Some((local, domain)) = e.split_once('@') else { return false };
    if domain.contains('@') { return false; }
    const ATEXT: &str = ".!#$%&'*+/=?^_`{|}~-";
    if local.is_empty() || local.len() > 64 || !local.chars().all(|c| c.is_ascii_alphanumeric() || ATEXT.contains(c)) { return false; }
    if domain.len() > 255 || domain.starts_with('.') || domain.ends_with('.') || domain.contains("..") { return false; }
    domain.split('.').all(label_ok)
}

// ---------------------------------------------------------------- RFC 5322 mailbox (lib/rfc5322-mailbox.ts)

/// Strips a surrounding quoted-string (and its escapes) from a display name.
pub fn unquote_name(name: &str) -> String {
    let t = name.trim();
    if t.len() >= 2 && t.starts_with('"') && t.ends_with('"') {
        let inner = &t[1..t.len() - 1];
        let mut out = String::new();
        let mut it = inner.chars().peekable();
        while let Some(c) = it.next() {
            if c == '\\' && matches!(it.peek(), Some('"') | Some('\\')) { out.push(it.next().unwrap()); } else { out.push(c); }
        }
        return out.trim().to_string();
    }
    t.to_string()
}

/// The name only: unquoted, without an address it carries (`Jane <j@x.com>`, `Jane<j@x.com`).
pub fn sanitize_display_name(name: &str) -> String {
    let n = unquote_name(name);
    // drop a trailing `<...>` or `<...` run
    let cut = match n.rfind('<') {
        Some(i) if !n[i + 1..].contains('<') && n[i + 1..].trim_end().trim_end_matches('>').chars().all(|c| c != '>') => n[..i].trim_end().to_string(),
        _ => n.clone(),
    };
    unquote_name(&cut)
}

/// The addr-spec a mailbox string carries ('' when none): the last angle run (closing
/// bracket optional), else the whole string when it has an @.
fn extract_addr_spec(v: &str) -> String {
    let t = v.trim_end();
    if let Some(i) = t.rfind('<') {
        let rest = &t[i + 1..];
        let inner = rest.strip_suffix('>').unwrap_or(rest);
        if !inner.contains(['<', '>']) { return inner.trim().to_string(); }
    }
    if v.contains('@') { v.to_string() } else { String::new() }
}

/// Splits a mailbox into (display name, addr-spec), tolerating the malformed shapes above.
pub fn split_mailbox(value: &str) -> (String, String) {
    let t = value.trim();
    let email = extract_addr_spec(t);
    if email.is_empty() { return (String::new(), t.to_string()); }
    let at = t.rfind(email.as_str()).unwrap_or(0);
    let src = t[..at].trim_end();
    let src = src.strip_suffix('<').unwrap_or(src);
    let name = sanitize_display_name(src);
    if !name.is_empty() && name != email { (name, email) } else { (String::new(), email) }
}

// ---------------------------------------------------------------- lists (lib/email-composer-utils.ts)

fn angle_run_closes(v: &[char], from: usize) -> bool {
    let mut quoted = false;
    let mut i = from + 1;
    while i < v.len() {
        let c = v[i];
        if quoted && c == '\\' { i += 1; }
        else if c == '"' { quoted = !quoted; }
        else if quoted {}
        else if c == '>' { return true; }
        else if c == '<' { return false; }
        i += 1;
    }
    false
}

/// Top-level split on any of `seps`: literal inside a quoted name, angle brackets or an
/// RFC 5322 group (`Team: a@x, b@y;`). An unclosed `<` does not swallow the rest.
pub fn split_recipients(value: &str, seps: &str) -> Vec<String> {
    let v: Vec<char> = value.chars().collect();
    let mut out = vec![];
    let mut cur = String::new();
    let (mut quoted, mut angle, mut group) = (false, false, false);
    let push = |cur: &mut String, out: &mut Vec<String>| { let t = cur.trim(); if !t.is_empty() { out.push(t.to_string()); } cur.clear(); };
    let mut i = 0;
    while i < v.len() {
        let ch = v[i];
        if quoted && ch == '\\' && i + 1 < v.len() { cur.push(ch); cur.push(v[i + 1]); i += 2; continue; }
        if ch == '"' { quoted = !quoted; cur.push(ch); }
        else if ch == '<' && !quoted { angle = true; cur.push(ch); }
        else if ch == '>' && !quoted { angle = false; cur.push(ch); }
        else if seps.contains(ch) && angle && !quoted && !group && !angle_run_closes(&v, i) { angle = false; push(&mut cur, &mut out); }
        else if ch == ':' && !quoted && !angle { group = true; cur.push(ch); }
        else if ch == ';' && group && !quoted && !angle { group = false; cur.push(ch); }
        else if seps.contains(ch) && !quoted && !angle && !group { push(&mut cur, &mut out); }
        else { cur.push(ch); }
        i += 1;
    }
    push(&mut cur, &mut out);
    out
}

fn top_level_colon(v: &str) -> Option<usize> {
    let (mut quoted, mut angle) = (false, false);
    let mut it = v.char_indices();
    while let Some((i, c)) = it.next() {
        if quoted && c == '\\' { it.next(); }
        else if c == '"' { quoted = !quoted; }
        else if c == '<' && !quoted { angle = true; }
        else if c == '>' && !quoted { angle = false; }
        else if c == ':' && !quoted && !angle { return Some(i); }
    }
    None
}

/// One recipient: `Name <email>`, `"Quoted, Name" <email>`, a bare address, or the group
/// form `Team: a@x, b@y;` (taken only when a member is a real address).
pub fn parse_recipient(s: &str) -> Recipient {
    let t = s.trim();
    if t.ends_with(';') {
        if let Some(colon) = top_level_colon(t) {
            let members: Vec<Recipient> = split_recipients(&t[colon + 1..t.len() - 1], ",").iter()
                .map(|m| parse_recipient(m)).filter(|m| !m.email.is_empty() && m.group.is_none()).collect();
            if members.iter().any(|m| is_valid_email(&m.email)) {
                return Recipient { name: unquote_name(&t[..colon]), email: String::new(), group: Some(members) };
            }
        }
    }
    // ^(.+?)\s*<([^>]+)>$
    if t.ends_with('>') {
        if let Some(open) = t.find('<') {
            let inner = &t[open + 1..t.len() - 1];
            let name = t[..open].trim_end();
            if !name.is_empty() && !inner.is_empty() && !inner.contains('>') {
                return Recipient::addr(&unquote_name(name), inner.trim());
            }
        }
    }
    Recipient::addr("", t)
}

pub fn parse_list(value: &str) -> Vec<Recipient> {
    split_recipients(value, ",").iter().map(|e| parse_recipient(e)).collect()
}

fn needs_quoting(n: &str) -> bool { n.contains([',', '<', '>', '"', '@', ';', ':']) }

fn quote_name(n: &str) -> String {
    if needs_quoting(n) { format!("\"{}\"", n.replace('\\', "\\\\").replace('"', "\\\"")) } else { n.to_string() }
}

/// `Name <email>` (name quoted when needed), or the bare address.
pub fn format_recipient(name: &str, email: &str) -> String {
    let n = name.trim();
    if n.is_empty() || n == email { return email.to_string(); }
    format!("{} <{email}>", quote_name(n))
}

/// A recipient as text; a group in RFC 5322 group syntax so it survives the string field.
pub fn format_entry(r: &Recipient) -> String {
    match &r.group {
        Some(m) => {
            let name = if r.name.trim().is_empty() { "Group".to_string() } else { r.name.trim().to_string() };
            format!("{}: {};", quote_name(&name), m.iter().map(|x| format_recipient(&x.name, &x.email)).collect::<Vec<_>>().join(", "))
        }
        None => format_recipient(&r.name, &r.email),
    }
}

pub fn format_list(list: &[Recipient]) -> String {
    list.iter().map(format_entry).collect::<Vec<_>>().join(", ")
}

/// Groups expanded into their members, deduplicated by address (first one wins).
pub fn expand(list: &[Recipient]) -> Vec<Recipient> {
    let mut seen = std::collections::HashSet::new();
    let mut out = vec![];
    for r in list {
        let entries: Vec<&Recipient> = match &r.group { Some(m) => m.iter().collect(), None => vec![r] };
        for e in entries {
            let k = e.email.trim().to_lowercase();
            if k.is_empty() || !seen.insert(k) { continue; }
            out.push(Recipient::addr(&e.name, e.email.trim()));
        }
    }
    out
}

/// A recipient field's text with groups expanded and domains in ASCII, ready for the server.
pub fn expand_for_send(value: &str) -> String {
    expand(&parse_list(value)).iter().map(|r| format_recipient(&r.name, &to_ascii_email(&r.email))).collect::<Vec<_>>().join(", ")
}

/// Entries that are not addresses, group members included.
pub fn invalid_addresses(list: &[Recipient]) -> Vec<String> {
    list.iter().flat_map(|r| match &r.group { Some(m) => m.clone(), None => vec![r.clone()] })
        .filter(|r| !is_valid_email(&r.email)).map(|r| r.email).collect()
}

/// Splits pasted text into valid recipients (deduplicated against `existing` and each other)
/// and leftover tokens that are not addresses.
pub fn split_pasted(text: &str, existing: &[String]) -> (Vec<Recipient>, Vec<String>) {
    let mut seen: std::collections::HashSet<String> = existing.iter().map(|e| e.to_lowercase()).collect();
    let mut valid = vec![];
    let mut invalid = vec![];
    let mut try_add = |r: Recipient, valid: &mut Vec<Recipient>| -> bool {
        if !is_valid_email(&r.email) { return false; }
        if seen.insert(r.email.to_lowercase()) { valid.push(Recipient::addr(&r.name, &r.email)); }
        true
    };
    let carries_address = |v: &str| v.split(|c: char| c.is_whitespace() || c == ';').any(|t| is_valid_email(t.trim().trim_start_matches('<').trim_end_matches('>')));
    for entry in split_recipients(text, ",;\n\r") {
        let p = parse_recipient(&entry);
        if p.group.is_none() && try_add(p, &mut valid) { continue; }
        let un = unquote_name(&entry);
        if un != entry && try_add(parse_recipient(&un), &mut valid) { continue; }
        if let Some(i) = entry.rfind('<') {
            if !carries_address(&entry[..i]) {
                let (n, e) = split_mailbox(&entry);
                if try_add(Recipient::addr(&n, &e), &mut valid) { continue; }
            }
        }
        for tok in entry.split(|c: char| c.is_whitespace() || c == ';').map(str::trim).filter(|t| !t.is_empty()) {
            let bare = tok.trim_start_matches('<').trim_end_matches('>');
            if !try_add(Recipient::addr("", bare), &mut valid) { invalid.push(tok.to_string()); }
        }
    }
    (valid, invalid)
}

/// The suggestion Enter/Tab takes before the arrows moved: a complete typed address takes
/// its own suggestion (or none: add as typed), anything else the top one.
pub fn default_suggestion_index(input: &str, s: &[Suggestion]) -> i32 {
    let typed = input.trim();
    if typed.is_empty() || s.is_empty() { return -1; }
    let email = parse_recipient(typed).email;
    if is_valid_email(&email) {
        let l = email.to_lowercase();
        return s.iter().position(|x| x.email.to_lowercase() == l).map(|i| i as i32).unwrap_or(-1);
    }
    0
}

// ---------------------------------------------------------------- autocomplete (contact-store getAutocomplete)

/// 0 when a word of one of the texts starts with `query`, 1 when one only contains it, -1 when none.
pub fn match_rank(query: &str, texts: &[&str]) -> i32 {
    let mut rank = -1;
    for t in texts {
        let lower = t.to_lowercase();
        let mut from = 0;
        while let Some(off) = lower[from..].find(query) {
            let at = from + off;
            let prev = lower[..at].chars().next_back();
            if prev.is_none_or(|c| !c.is_alphanumeric()) { return 0; }
            rank = 1;
            from = at + lower[at..].chars().next().map(|c| c.len_utf8()).unwrap_or(1);
        }
    }
    rank
}

/// Where suggestions come from, in precedence order.
#[derive(Default, Clone)]
pub struct Sources {
    /// (name, email) per contact address.
    pub contacts: Vec<(String, String)>,
    /// (id, name, members) per contact group (members with an address).
    pub groups: Vec<(String, String, Vec<Recipient>)>,
    /// (name, email) people written to, most recent first.
    pub recent: Vec<(String, String)>,
}

/// Bulwark getAutocomplete: groups and contacts, then recent recipients not already
/// suggested; stable-sorted by rank (word start before substring); cleaned names; 10 at most.
pub fn suggest(query: &str, src: &Sources) -> Vec<Suggestion> {
    let q = query.trim().to_lowercase();
    if q.is_empty() { return vec![]; }
    let mut hits: Vec<(i32, Suggestion)> = vec![];
    for (id, name, members) in &src.groups {
        let r = match_rank(&q, &[name]);
        if r >= 0 && !members.is_empty() { hits.push((r, Suggestion { name: name.clone(), email: String::new(), group: Some((id.clone(), members.len())) })); }
    }
    for (name, email) in &src.contacts {
        let r = match_rank(&q, &[name, email]);
        if r >= 0 { hits.push((r, Suggestion { name: name.clone(), email: email.clone(), group: None })); }
    }
    let mut seen: std::collections::HashSet<String> = hits.iter().map(|h| h.1.email.to_lowercase()).collect();
    for (name, email) in &src.recent {
        let k = email.to_lowercase();
        if seen.contains(&k) { continue; }
        let r = match_rank(&q, &[&k, name]);
        if r >= 0 { seen.insert(k); hits.push((r, Suggestion { name: name.clone(), email: email.clone(), group: None })); }
    }
    hits.sort_by_key(|h| h.0);
    normalize(hits.into_iter().map(|h| h.1).collect()).into_iter().take(MAX_SUGGESTIONS).collect()
}

/// Clean name + bare address per suggestion; ones with the same address collapse into the
/// first (which takes a name it was missing).
fn normalize(list: Vec<Suggestion>) -> Vec<Suggestion> {
    let mut out: Vec<Suggestion> = vec![];
    for s in list {
        if s.group.is_some() { out.push(s); continue; }
        let (mname, email) = split_mailbox(&s.email);
        if email.is_empty() { continue; }
        let mut name = sanitize_display_name(&s.name);
        if name.is_empty() { name = mname; }
        if name == email { name.clear(); }
        match out.iter_mut().find(|o| o.group.is_none() && o.email.eq_ignore_ascii_case(&email)) {
            Some(o) => { if o.name.is_empty() && !name.is_empty() { o.name = name; } }
            None => out.push(Suggestion { name, email, group: None }),
        }
    }
    out
}

/// Two-letter initials for a chip avatar.
pub fn initials(name: &str, email: &str) -> String {
    let src = if name.trim().is_empty() { email.split('@').next().unwrap_or("") } else { name };
    let words: Vec<&str> = src.split(|c: char| c.is_whitespace() || c == '.' || c == '_' || c == '-').filter(|w| !w.is_empty()).collect();
    let mut s: String = words.iter().take(2).filter_map(|w| w.chars().next()).collect();
    if s.chars().count() < 2 { s = src.chars().filter(|c| c.is_alphanumeric()).take(2).collect(); }
    s.to_uppercase()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn idn_round_trip() {
        assert_eq!(to_ascii_email("jo@bücher.de"), "jo@xn--bcher-kva.de");
        assert_eq!(to_ascii_email("Jo@Example.COM"), "Jo@Example.COM");
        assert_eq!(to_unicode_email("jo@xn--bcher-kva.de"), "jo@bücher.de");
        assert_eq!(to_unicode_email("jo@XN--BCHER-KVA.DE"), "jo@bücher.de");
        // not canonical / broken punycode stays as it is
        assert_eq!(to_unicode_domain("xn--zz-zz.de"), "xn--zz-zz.de");
        assert_eq!(to_ascii_domain("a b.de"), None);
    }

    #[test]
    fn validation() {
        assert!(is_valid_email("a.b+tag@example.org"));
        assert!(is_valid_email("jo@bücher.de"));
        for bad in ["", "a", "a@", "@b.c", "a@b..c", "a@-b.c", "a@b.c.", "a b@c.d", "a@b@c.d", "a<b@c.d", "user\n@x.y"] {
            assert!(!is_valid_email(bad), "{bad}");
        }
        assert!(!is_valid_email(&format!("{}@x.y", "a".repeat(65))));
    }

    #[test]
    fn mailbox_split() {
        assert_eq!(split_mailbox("Jane <j@x.com>"), ("Jane".into(), "j@x.com".into()));
        assert_eq!(split_mailbox("Jane<j@x.com"), ("Jane".into(), "j@x.com".into()));
        assert_eq!(split_mailbox("\"Jane <j@x.com>\" <j@x.com>"), ("Jane".into(), "j@x.com".into()));
        assert_eq!(split_mailbox("j@x.com"), ("".into(), "j@x.com".into()));
        assert_eq!(sanitize_display_name("\"Doe, J\""), "Doe, J");
    }

    #[test]
    fn split_and_parse() {
        assert_eq!(split_recipients("\"Doe, John\" <j@d.o>, x@y.z", ","), vec!["\"Doe, John\" <j@d.o>", "x@y.z"]);
        assert_eq!(split_recipients("Team: a@x.io, b@y.io;, c@z.io", ","), vec!["Team: a@x.io, b@y.io;", "c@z.io"]);
        // an unclosed < does not swallow the rest
        assert_eq!(split_recipients("Jo <jo@x.io, b@y.io", ","), vec!["Jo <jo@x.io", "b@y.io"]);
        assert_eq!(split_recipients("\"Sup\\\", ceo@c.ex, x\" <s@a.b>", ","), vec!["\"Sup\\\", ceo@c.ex, x\" <s@a.b>"]);
        let g = parse_recipient("\"Team, A\": Ann <a@x.io>, b@y.io;");
        assert_eq!(g.name, "Team, A");
        assert_eq!(g.group.as_ref().unwrap().len(), 2);
        assert_eq!(parse_recipient("Subject: hello;").group, None);
        assert_eq!(parse_recipient("\"Doe, J\" <j@d.o>"), Recipient::addr("Doe, J", "j@d.o"));
        // format and parse round-trip, groups included
        let list = vec![Recipient::addr("Doe, \"J\"", "j@d.o"), g.clone(), Recipient::addr("", "x@y.z")];
        assert_eq!(parse_list(&format_list(&list)), list);
    }

    #[test]
    fn expansion_and_invalid() {
        let v = parse_list("Ann <a@x.io>, Team: a@x.io, B <b@y.io>;, nope");
        let e = expand(&v);
        assert_eq!(e.iter().map(|r| r.email.as_str()).collect::<Vec<_>>(), vec!["a@x.io", "b@y.io", "nope"]);
        assert_eq!(invalid_addresses(&v), vec!["nope".to_string()]);
        assert_eq!(expand_for_send("Team: Jo <jo@bücher.de>, b@y.io;"), "Jo <jo@xn--bcher-kva.de>, b@y.io");
    }

    #[test]
    fn paste() {
        let (v, inv) = split_pasted("a@x.io b@y.io; \"Doe, J\" <j@d.o>\nC <c@z.io, junk, A@X.IO", &["old@q.io".into()]);
        assert_eq!(v.iter().map(|r| r.email.as_str()).collect::<Vec<_>>(), vec!["a@x.io", "b@y.io", "j@d.o", "c@z.io"]);
        assert_eq!(v[2].name, "Doe, J");
        assert_eq!(v[3].name, "C");
        assert_eq!(inv, vec!["junk".to_string()]);
    }

    #[test]
    fn ranking() {
        let src = Sources {
            contacts: vec![("Daniel".into(), "dan@x.io".into()), ("Anna".into(), "anna@x.io".into()), ("Jane <jane@x.io>".into(), "jane@x.io".into())],
            groups: vec![("g1".into(), "Annual team".into(), vec![Recipient::addr("", "a@x.io")]), ("g2".into(), "Empty an".into(), vec![])],
            recent: vec![("".into(), "anna@x.io".into()), ("Anders".into(), "and@y.io".into())],
        };
        let s = suggest("an", &src);
        let keys: Vec<String> = s.iter().map(|x| if x.group.is_some() { format!("g:{}", x.name) } else { x.email.clone() }).collect();
        // word starts first (group, Anna, Anders), then Daniel and Jane which only contain it
        assert_eq!(keys, vec!["g:Annual team", "anna@x.io", "and@y.io", "dan@x.io", "jane@x.io"]);
        assert_eq!(suggest("jane", &src)[0].name, "Jane");
        assert!(suggest(" ", &src).is_empty());
        assert_eq!(match_rank("ül", &["Müller", "x"]), 1);
        assert_eq!(match_rank("mü", &["Hans Müller"]), 0);
    }

    #[test]
    fn default_pick() {
        let s = vec![Suggestion { name: "A".into(), email: "a@x.io".into(), group: None }, Suggestion { name: "B".into(), email: "b@x.io".into(), group: None }];
        assert_eq!(default_suggestion_index("b@x.io", &s), 1);
        assert_eq!(default_suggestion_index("c@x.io", &s), -1);
        assert_eq!(default_suggestion_index("an", &s), 0);
        assert_eq!(default_suggestion_index("", &s), -1);
        assert_eq!(initials("Mira Okafor", ""), "MO");
        assert_eq!(initials("", "jonas.weber@x.io"), "JW");
    }
}
