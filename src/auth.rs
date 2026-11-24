//! The receiving server's Authentication-Results (RFC 8601), read the way Bulwark's
//! lib/email-headers.ts does: only a pass that aligns with the From domain (DMARC relaxed
//! alignment, no public-suffix list) says anything about the visible sender.
use serde_json::Value;

/// One result of an Authentication-Results header: method, result and its properties.
#[derive(Debug, Clone, PartialEq)]
pub struct AuthResult {
    pub method: String,
    pub result: String,
    pub props: Vec<(String, String)>,
}

impl AuthResult {
    pub fn prop(&self, k: &str) -> Option<&str> {
        self.props.iter().find(|(n, _)| n.eq_ignore_ascii_case(k)).map(|(_, v)| v.as_str())
    }
}

/// Split one Authentication-Results value into its `;`-separated parts (RFC 8601 §2.2),
/// dropping comments. A `;` or a parenthesis inside a quoted string (the envelope local part
/// in `smtp.mailfrom="a;dmarc=pass"@x.net` is sender-chosen text, also in a header the
/// server wrote) does not split or open a comment, and a quote inside a comment does not
/// open a string. The text is bounded: an absurd header is not read.
fn split_parts(header: &str) -> Vec<String> {
    let mut parts = vec![];
    let mut cur = String::new();
    let (mut depth, mut quoted) = (0usize, false);
    let mut chars = header.chars().take(MAX_HEADER);
    while let Some(c) = chars.next() {
        if c == '\\' && (quoted || depth > 0) {
            let next = chars.next();
            if depth == 0 { cur.push(c); if let Some(n) = next { cur.push(n); } }
            continue;
        }
        if quoted { cur.push(c); if c == '"' { quoted = false; } continue; }
        if depth > 0 {
            match c { '(' => depth += 1, ')' => depth -= 1, _ => {} }
            if depth == 0 { cur.push(' '); }
            continue;
        }
        match c {
            '(' => depth = 1,
            '"' => { quoted = true; cur.push(c); }
            ';' => parts.push(std::mem::take(&mut cur)),
            _ => cur.push(c),
        }
    }
    parts.push(cur);
    parts.into_iter().map(|p| p.trim().to_string()).collect()
}

/// Longest Authentication-Results (or Received) value looked at.
const MAX_HEADER: usize = 16 * 1024;

/// Whitespace-separated tokens, a quoted string (with its spaces) staying one token.
fn tokens(part: &str) -> Vec<String> {
    let mut out = vec![];
    let mut cur = String::new();
    let (mut quoted, mut escaped) = (false, false);
    for c in part.chars() {
        if escaped { cur.push(c); escaped = false; continue; }
        if quoted && c == '\\' { escaped = true; continue; }
        if c == '"' { quoted = !quoted; cur.push(c); continue; }
        if c.is_whitespace() && !quoted { if !cur.is_empty() { out.push(std::mem::take(&mut cur)); } continue; }
        cur.push(c);
    }
    if !cur.is_empty() { out.push(cur); }
    out
}

fn unquote(v: &str) -> String {
    v.strip_prefix('"').map(|r| r.strip_suffix('"').unwrap_or(r)).unwrap_or(v).to_string()
}

/// One resinfo: the method must open the part (`dmarc=pass`), so a `dmarc=pass` that sits
/// inside a property value is never taken for a result. The first value of a property wins.
fn parse_resinfo(part: &str) -> Option<AuthResult> {
    let toks = tokens(part);
    let (m, r) = toks.first()?.split_once('=')?;
    let method = m.split('/').next().unwrap_or("").to_ascii_lowercase();
    let ok = !method.is_empty() && method.chars().all(|c| c.is_ascii_alphanumeric() || c == '-' || c == '_')
        && !r.is_empty() && r.chars().all(|c| c.is_ascii_alphabetic());
    if !ok { return None; }
    let mut props: Vec<(String, String)> = vec![];
    for t in &toks[1..] {
        if let Some((k, v)) = t.split_once('=') {
            let k = k.to_ascii_lowercase();
            if !k.is_empty() && !props.iter().any(|(n, _)| *n == k) { props.push((k, unquote(v))); }
        }
    }
    Some(AuthResult { method, result: r.to_ascii_lowercase(), props })
}

/// Parse one Authentication-Results value (its authserv-id, the first part, is skipped).
pub fn parse(ar: &str) -> Vec<AuthResult> {
    split_parts(ar).iter().skip(1).filter_map(|p| parse_resinfo(p)).collect()
}

/// The authserv-id of an Authentication-Results value (lower case, no trailing dot, no version).
pub fn authserv_id(ar: &str) -> String {
    let first = split_parts(ar).into_iter().next().unwrap_or_default();
    unquote(tokens(&first).first().map(String::as_str).unwrap_or("")).trim_end_matches('.').to_ascii_lowercase()
}

/// The domain part of an address, lower-case, without a trailing dot; None if it has no dot.
pub fn domain_of(addr: &str) -> Option<String> {
    let d = addr.rsplit('@').next()?.trim().trim_end_matches('>').trim_end_matches('.').to_ascii_lowercase();
    (d.contains('.') && !d.starts_with('.') && !d.contains(char::is_whitespace) && !d.contains(['<', '>', '@'])).then_some(d)
}

/// DMARC relaxed alignment: the same domain or a parent/child of it.
pub fn aligned(from_domain: &str, candidate: Option<&str>) -> bool {
    let Some(o) = candidate.and_then(|c| domain_of(&format!("@{c}"))) else { return false };
    o == from_domain || o.ends_with(&format!(".{from_domain}")) || from_domain.ends_with(&format!(".{o}"))
}

/// A DKIM pass whose signing domain (header.d) aligns with `from_domain`.
pub fn aligned_dkim_pass(res: &[AuthResult], from_domain: &str) -> bool {
    res.iter().any(|r| r.method == "dkim" && r.result == "pass" && aligned(from_domain, r.prop("header.d")))
}

/// Whether the checks tie the message to its From address: dmarc=pass, or an aligned SPF
/// (MAIL FROM, never HELO) or DKIM pass.
pub fn aligned_pass(res: &[AuthResult], from: &str) -> bool {
    let Some(d) = domain_of(from) else { return false };
    res.iter().any(|r| r.method == "dmarc" && r.result == "pass")
        || res.iter().any(|r| r.method == "spf" && r.result == "pass" && r.prop("smtp.mailfrom").is_some_and(|m| aligned(&d, Some(&domain_of(m).unwrap_or_default()))))
        || aligned_dkim_pass(res, &d)
}

/// A failure that leaves the From domain unauthenticated (a DMARC fail always counts; a single
/// mechanism failing does not when an aligned pass vouches for the sender).
pub fn failure(res: &[AuthResult], verified: bool) -> bool {
    if res.iter().any(|r| r.method == "dmarc" && r.result == "fail") { return true; }
    if verified { return false; }
    res.iter().any(|r| (r.method == "dkim" && matches!(r.result.as_str(), "fail" | "policy" | "permerror"))
        || (r.method == "spf" && matches!(r.result.as_str(), "fail" | "softfail" | "permerror")))
}


// ---------------------------------------------------------------- trust (R3-06)

/// The one place an Authentication-Results header is believed. Everything that reads mail
/// authentication (the verified-brand badge, the sender logo, the invitation assessment, the
/// one-click unsubscribe gate) goes through [`assess`] and takes only [`Trust::results`].
///
/// The rule (RFC 8601 §5, §7.1; Bulwark `authenticationResultsOnReceipt`, C# R3-06): the
/// results are the server's own only when they stand in the block our server wrote when the
/// message arrived, which is
///   1. the first results header below the topmost `Received` header (a receiving server writes
///      its Received line first and its results right under it; trace headers may sit between,
///      a second Received may not; later results headers add SPF failures only),
///   2. that Received names our server (`by <host>`) and records no authenticated submission
///      (`with ESMTPSA`, `ESMTPA`, `LMTPA`: the client handed the message in, so the text
///      below is the client's own), and
///   3. the header carries our server's authserv-id.
/// Any Authentication-Results above the topmost Received, a message with no Received, a
/// message imported from a file or one the user wrote (Sent, Drafts) earns nothing.
///
/// Limit: Stalwart keeps a signed-in user's own headers when a mail is submitted over JMAP and
/// adds nothing above them, so a forged Received line followed by a forged results line can
/// still look like the server's. No client-side check closes that (every marker in a header
/// is text the sender can write); it needs the server. Only a user of the same server can do
/// it, and what it can buy is a badge on mail from that server.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct Trust {
    /// The message carries an Authentication-Results header (trusted or not).
    pub present: bool,
    /// The results are the server's own.
    pub trusted: bool,
    /// The server's results; empty unless `trusted`.
    pub results: Vec<AuthResult>,
}

/// What is known about where a message came from.
#[derive(Debug, Clone, Copy)]
pub struct Origin<'a> {
    /// The host of the account's JMAP URL (the server's name).
    pub server_host: &'a str,
    /// Came in from a file (`$imported`): the server never checked it.
    pub imported: bool,
    /// The user's own mail (a folder Sent or Drafts, or `$draft`): no verdict.
    pub own_mail: bool,
}

impl<'a> Origin<'a> {
    /// The origin of a message `m` (Email/get) on the server `server_host`, in folders of `roles`.
    pub fn of(m: &Value, server_host: &'a str, roles: &[String]) -> Origin<'a> {
        Origin {
            server_host,
            imported: m["keywords"]["$imported"].as_bool() == Some(true),
            own_mail: is_own_mail(roles, m["keywords"]["$draft"].as_bool() == Some(true)),
        }
    }
}

/// Whether the keyword and folder facts say the user wrote the message.
pub fn is_own_mail(roles: &[String], draft: bool) -> bool {
    draft || roles.iter().any(|r| matches!(r.as_str(), "sent" | "drafts"))
}

/// A JMAP `headers` list as (lower-case name, unfolded value) in message order.
pub fn header_list(headers: &[Value]) -> Vec<(String, String)> {
    headers.iter().filter_map(|h| {
        let name = h["name"].as_str()?.to_ascii_lowercase();
        let value = crate::reader::unfold(h["value"].as_str()?);
        Some((name, value))
    }).collect()
}

/// [`assess_list`] for the `headers` of an Email/get.
pub fn assess(headers: &[Value], origin: &Origin) -> Trust {
    assess_list(&header_list(headers), origin)
}

/// The judgement, on headers in message order.
pub fn assess_list(headers: &[(String, String)], origin: &Origin) -> Trust {
    let is_ar = |h: &(String, String)| h.0 == "authentication-results";
    let present = headers.iter().any(is_ar);
    let untrusted = Trust { present, trusted: false, results: vec![] };
    if !present || origin.imported || origin.own_mail || origin.server_host.trim().is_empty() { return untrusted; }
    let Some(r) = headers.iter().position(|h| h.0 == "received") else { return untrusted };
    // anything above our server's own Received was not written by it
    if headers[..r].iter().any(is_ar) { return untrusted; }
    if is_submission(&headers[r].1) || !received_by(&headers[r].1, origin.server_host) { return untrusted; }
    // The server's results are the FIRST results header below that Received (Bulwark
    // `own = perHeader[0]`): a header the sender planted after it is never believed for a pass.
    // Other trace headers (Received-SPF, ARC-*, X-...) may sit in between, a second Received may not.
    let Some(first) = headers.iter().skip(r + 1).position(is_ar).map(|i| r + 1 + i) else { return untrusted };
    if headers[r + 1..first].iter().any(|h| h.0 == "received") { return untrusted; }
    if !same_server(&authserv_id(&headers[first].1), origin.server_host) { return untrusted; }
    let mut results = parse(&headers[first].1);
    // later results headers (an earlier hop's, or planted) add SPF failures only (Bulwark
    // email-headers.ts): DKIM and DMARC come from our own header, so a forwarder's or a list's
    // dkim=fail / dmarc=none does not sink genuine mail, and nothing later can add a pass
    for h in headers[first + 1..].iter().filter(|h| is_ar(h)) {
        results.extend(parse(&h.1).into_iter().filter(|x| x.method == "spf" && matches!(x.result.as_str(), "fail" | "softfail" | "permerror")));
    }
    if results.is_empty() { return untrusted; }
    Trust { present, trusted: true, results }
}

/// `with` protocols (RFC 3848) that record an authenticated submission or local hand-over.
pub fn is_submission(received: &str) -> bool {
    let clean = split_parts(received).join(" ").to_ascii_lowercase();
    let toks: Vec<&str> = clean.split_whitespace().collect();
    toks.windows(2).any(|w| {
        w[0] == "with" && {
            let p = w[1].trim_end_matches([';', ',']);
            let p = p.strip_prefix("utf8").unwrap_or(p);
            matches!(p, "esmtpa" | "esmtpsa" | "smtpa" | "smtpsa" | "lmtpa" | "lmtpsa")
        }
    })
}

/// Whether the first `by <host>` of a Received header is our server.
fn received_by(received: &str, server_host: &str) -> bool {
    // comments hold sender-chosen text (the "from" part's rDNS note); the by-clause is ours
    let mut text = String::new();
    let mut depth = 0usize;
    for c in received.chars().take(MAX_HEADER) {
        match c { '(' => depth += 1, ')' if depth > 0 => depth -= 1, _ if depth == 0 => text.push(c), _ => {} }
    }
    let toks: Vec<&str> = text.split_whitespace().collect();
    toks.windows(2).find(|w| w[0].eq_ignore_ascii_case("by"))
        .is_some_and(|w| same_server(w[1].trim_end_matches(';'), server_host))
}

/// Whether a host name (an authserv-id or a Received `by`) is the account's server: the JMAP
/// host itself, or a name under the same registered domain (a mail host `mx.example.org`
/// behind `jmap.example.org`).
pub fn same_server(id: &str, server_host: &str) -> bool {
    let id = id.trim().trim_end_matches('.').to_ascii_lowercase();
    let host = server_host.trim().trim_end_matches('.').to_ascii_lowercase();
    if id.is_empty() || host.is_empty() { return false; }
    id == host || (id.contains('.') && host.contains('.') && crate::avatars::valid_domain(&id)
        && crate::avatars::root_domain(&id) == crate::avatars::root_domain(&host))
}

/// The worst DMARC result there is (fail, then permerror and temperror, then none, then pass).
pub fn dmarc(res: &[AuthResult]) -> Option<&AuthResult> {
    let rank = |r: &str| match r { "fail" => 3, "pass" => 0, "none" => 1, _ => 2 };
    res.iter().filter(|r| r.method == "dmarc").max_by_key(|r| rank(&r.result))
}

/// Whether the results say the message passed DMARC for exactly `from_domain` (the domain of
/// the From address): the worst DMARC result is a pass for that header.from, and nothing else
/// in the results says the message is spoofed (Bulwark brandIndicatorDomain). Stricter than
/// Bulwark in one point: the result must name its header.from.
pub fn dmarc_pass_for(res: &[AuthResult], from_domain: &str) -> bool {
    let Some(d) = dmarc(res) else { return false };
    let checked = d.prop("header.from").and_then(|h| domain_of(&format!("@{h}")));
    let hard_spf_fail = res.iter().any(|r| r.method == "spf" && r.result == "fail")
        && !res.iter().any(|r| r.method == "dkim" && r.result == "pass");
    d.result == "pass" && checked.as_deref() == Some(from_domain) && !hard_spf_fail
}

/// The From domain of a message whose results are trusted and passed DMARC for it, for the
/// sender logo and the verified-brand badge.
pub fn brand_domain(m: &Value, origin: &Origin) -> Option<String> {
    let from = domain_of(m["from"][0]["email"].as_str()?)?;
    let t = assess(m["headers"].as_array()?, origin);
    (t.trusted && dmarc_pass_for(&t.results, &from)).then_some(from)
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn parses_and_aligns() {
        let r = parse("mx.example.org; dkim=pass (good sig) header.d=mail.shop.com header.s=s1; spf=pass smtp.mailfrom=bounce@other.net; dmarc=none header.from=shop.com");
        assert_eq!(r.len(), 3);
        assert_eq!(r[0].prop("header.d"), Some("mail.shop.com"));
        assert!(aligned_pass(&r, "news@shop.com"), "dkim d= is a child of the From domain");
        assert!(!aligned_pass(&r, "ceo@bank.com"), "a pass for someone else's domain says nothing");
        assert!(aligned_dkim_pass(&r, "shop.com"));
        assert!(!aligned_dkim_pass(&r, "evil.com"));
    }

    #[test]
    fn spf_needs_aligned_mailfrom() {
        let r = parse("mx; spf=pass smtp.mailfrom=a@attacker.net");
        assert!(!aligned_pass(&r, "boss@corp.com"));
        assert!(aligned_pass(&r, "x@attacker.net"));
    }

    #[test]
    fn failures() {
        assert!(failure(&parse("mx; dmarc=fail"), true));
        assert!(failure(&parse("mx; spf=softfail"), false));
        assert!(!failure(&parse("mx; spf=softfail; dkim=pass header.d=a.com"), true));
        assert!(!failure(&parse("mx; dkim=none"), false));
    }

    #[test]
    fn domains() {
        assert_eq!(domain_of("A@Example.COM."), Some("example.com".into()));
        assert_eq!(domain_of("nobody"), None);
        assert!(aligned("a.com", Some("b.a.com")));
        assert!(!aligned("a.com", Some("ba.com")));
    }

    // ---- the parse

    #[test]
    fn a_semicolon_or_a_comment_in_a_quoted_value_is_not_a_result() {
        // the envelope local part is the sender's own text, also in a header the server wrote
        let r = parse(r#"mx.mine.org; spf=pass smtp.mailfrom="a;dmarc=pass header.from=bank.com"@evil.net; dmarc=fail header.from=bank.com"#);
        assert_eq!(r.len(), 2, "{r:?}");
        assert_eq!(r[0].method, "spf");
        assert!(dmarc(&r).is_some_and(|d| d.result == "fail"));
        assert!(!dmarc_pass_for(&r, "bank.com"));
        // a parenthesis inside quotes opens no comment; a quote inside a comment opens no string
        let r = parse(r#"mx; dkim=pass header.d=a.com (a " quote; dmarc=pass); spf=none"#);
        assert_eq!(r.iter().map(|x| x.method.as_str()).collect::<Vec<_>>(), ["dkim", "spf"]);
        let r = parse(r#"mx; spf=pass smtp.mailfrom="x(y@z.net"; dmarc=pass header.from=z.net"#);
        assert_eq!(r.len(), 2);
        assert_eq!(r[0].prop("smtp.mailfrom"), Some("x(y@z.net"));
        // the method must open the part
        assert!(parse("mx; reason=dmarc=pass").iter().all(|x| x.method != "dmarc"));
        assert!(parse("mx; foo dmarc=pass").is_empty());
        // a method/version form, the first value of a property wins
        let r = parse("mx; dkim/2=pass header.d=a.com header.d=b.com");
        assert_eq!((r[0].method.as_str(), r[0].prop("header.d")), ("dkim", Some("a.com")));
        assert_eq!(authserv_id("MX.Example.org. 1; dkim=pass"), "mx.example.org");
        assert_eq!(authserv_id("; dkim=pass"), "");
        assert!(parse("mx.example.org; none").is_empty());
    }

    #[test]
    fn the_worst_dmarc_result_decides() {
        let r = parse("mx; dmarc=pass header.from=a.com; dmarc=fail header.from=a.com");
        assert!(!dmarc_pass_for(&r, "a.com"));
        let r = parse("mx; dmarc=pass header.from=a.com");
        assert!(dmarc_pass_for(&r, "a.com"));
        assert!(!dmarc_pass_for(&r, "b.com"), "a pass for another header.from");
        assert!(!dmarc_pass_for(&parse("mx; dmarc=pass"), "a.com"), "the pass must name its header.from");
        assert!(!dmarc_pass_for(&parse("mx; dmarc=none header.from=a.com"), "a.com"));
        assert!(!dmarc_pass_for(&parse("mx; spf=fail smtp.mailfrom=x@a.com; dmarc=pass header.from=a.com"), "a.com"), "a hard SPF fail with no DKIM pass");
        assert!(dmarc_pass_for(&parse("mx; spf=fail smtp.mailfrom=x@a.com; dkim=pass header.d=a.com; dmarc=pass header.from=A.com"), "a.com"));
    }

    // ---- trust (R3-06): only the results our server wrote on receipt

    fn hs(v: &[(&str, &str)]) -> Vec<(String, String)> { v.iter().map(|(n, x)| (n.to_ascii_lowercase(), x.to_string())).collect() }
    const HOST: &str = "mail.mine.test";
    const IN: &str = "from mx.sender.test (mx.sender.test [192.0.2.7]) by mx.mine.test (Stalwart) with ESMTPS id 1; Tue, 24 Nov 2026 10:00:00 +0000";
    const AR: &str = "mx.mine.test; dkim=pass header.d=brand.example; spf=pass smtp.mailfrom=bounce@brand.example; dmarc=pass header.from=brand.example";
    fn org(host: &str) -> Origin<'_> { Origin { server_host: host, imported: false, own_mail: false } }

    #[test]
    fn the_servers_block_is_trusted() {
        let t = assess_list(&hs(&[("Return-Path", "<a@brand.example>"), ("Received", IN), ("Authentication-Results", AR), ("Received", "from x by y"), ("Subject", "hi")]), &org(HOST));
        assert!(t.present && t.trusted && dmarc_pass_for(&t.results, "brand.example"));
        // the same through JMAP headers
        let j = vec![json!({"name": "Received", "value": format!(" {IN}")}), json!({"name": "Authentication-Results", "value": format!(" {AR}")})];
        assert!(assess(&j, &org(HOST)).trusted);
        let m = json!({"from": [{"email": "n@brand.example"}], "headers": j, "keywords": {}});
        assert_eq!(brand_domain(&m, &org(HOST)).as_deref(), Some("brand.example"));
    }

    #[test]
    fn a_forged_header_is_not_trusted() {
        // sent through submission by a user of the same server: their own header is all there is
        let forged_ar = "mail.mine.test; dkim=pass header.d=bank.example; dmarc=pass header.from=bank.example";
        let submitted = "from [10.0.0.9] (laptop) by mx.mine.test (Stalwart) with ESMTPSA id 2; Tue, 24 Nov 2026 10:00:00 +0000";
        // 1. the server noted the submission: nothing below it counts
        assert!(!assess_list(&hs(&[("Received", submitted), ("Authentication-Results", forged_ar)]), &org(HOST)).trusted);
        assert!(!assess_list(&hs(&[("Received", &submitted.replace("ESMTPSA", "ESMTPA")), ("Authentication-Results", forged_ar)]), &org(HOST)).trusted);
        assert!(!assess_list(&hs(&[("Received", &submitted.replace("ESMTPSA", "LMTPA")), ("Authentication-Results", forged_ar)]), &org(HOST)).trusted);
        // 2. the forged header sits above the topmost Received
        assert!(!assess_list(&hs(&[("Authentication-Results", forged_ar), ("Received", IN)]), &org(HOST)).trusted);
        // 3. no Received at all
        assert!(!assess_list(&hs(&[("Authentication-Results", forged_ar)]), &org(HOST)).trusted);
        // 4. the server's own block holds the real result; a forged header further down is never read
        let t = assess_list(&hs(&[("Received", IN), ("Authentication-Results", "mx.mine.test; dmarc=fail header.from=bank.example"),
            ("Received", "from a by b"), ("Authentication-Results", forged_ar)]), &org(HOST));
        assert!(t.trusted && !dmarc_pass_for(&t.results, "bank.example"));
        // a forged header after something that is not results is not in the server's block
        let t = assess_list(&hs(&[("Received", IN), ("Authentication-Results", "mx.mine.test; dmarc=none header.from=bank.example"), ("X-Other", "1"), ("Authentication-Results", forged_ar)]), &org(HOST));
        assert!(!dmarc_pass_for(&t.results, "bank.example"));
        // 5. other trace headers may sit between the Received and the results, a second Received may not
        assert!(assess_list(&hs(&[("Received", IN), ("Received-SPF", "pass"), ("ARC-Seal", "i=1"), ("Authentication-Results", AR)]), &org(HOST)).trusted);
        assert!(!assess_list(&hs(&[("Received", IN), ("Received", "from a by b"), ("Authentication-Results", AR)]), &org(HOST)).trusted);
        // 5b. a results header the sender planted right after the real one is not believed
        let real_fail = "mx.mine.test; dmarc=fail header.from=bank.example";
        let t = assess_list(&hs(&[("Received", IN), ("Authentication-Results", real_fail), ("Authentication-Results", forged_ar)]), &org(HOST));
        assert!(t.trusted && !dmarc_pass_for(&t.results, "bank.example"), "{t:?}");
        let t = assess_list(&hs(&[("Received", IN), ("Authentication-Results", "mx.mine.test; dmarc=none header.from=bank.example"), ("Authentication-Results", forged_ar)]), &org(HOST));
        assert!(!dmarc_pass_for(&t.results, "bank.example"), "a later pass is dropped");
        // an earlier hop's DKIM or DMARC verdict (a forwarder, a list) does not sink genuine mail
        let t = assess_list(&hs(&[("Received", IN), ("Authentication-Results", "mx.mine.test; dmarc=pass header.from=bank.example"),
            ("Authentication-Results", "x.elsewhere.test; dkim=fail header.d=bank.example; dmarc=none header.from=bank.example; dkim=none")]), &org(HOST));
        assert!(dmarc_pass_for(&t.results, "bank.example") && !t.results.iter().any(|r| r.method == "dkim" || r.result == "none"), "{t:?}");
        // but a later SPF failure still counts
        let t = assess_list(&hs(&[("Received", IN), ("Authentication-Results", "mx.mine.test; dmarc=pass header.from=bank.example"), ("Authentication-Results", "x.elsewhere.test; spf=softfail smtp.mailfrom=a@bank.example")]), &org(HOST));
        assert!(t.results.iter().any(|r| r.method == "spf" && r.result == "softfail"));
        // a forged first header with another authserv-id is not skipped to reach a later one
        assert!(!assess_list(&hs(&[("Received", IN), ("Authentication-Results", "evil.example; dmarc=pass header.from=bank.example"), ("Authentication-Results", AR)]), &org(HOST)).trusted);
        // 6. an authserv-id that is not ours (an upstream hop's, or the sender's)
        assert!(!assess_list(&hs(&[("Received", IN), ("Authentication-Results", "mx.bank.example; dmarc=pass header.from=bank.example")]), &org(HOST)).trusted);
        assert!(!assess_list(&hs(&[("Received", IN), ("Authentication-Results", "; dmarc=pass header.from=bank.example")]), &org(HOST)).trusted);
        // 7. the Received is not ours
        assert!(!assess_list(&hs(&[("Received", "from a by mx.elsewhere.example with ESMTP"), ("Authentication-Results", AR)]), &org(HOST)).trusted);
        // a "by mine" written in the from comment does not make it ours
        assert!(!assess_list(&hs(&[("Received", "from evil (by mx.mine.test) by mx.elsewhere.example with ESMTP"), ("Authentication-Results", AR)]), &org(HOST)).trusted);
        // 8. imported files and the user's own mail earn nothing; neither does an unknown server
        assert!(!assess_list(&hs(&[("Received", IN), ("Authentication-Results", AR)]), &Origin { imported: true, ..org(HOST) }).trusted);
        assert!(!assess_list(&hs(&[("Received", IN), ("Authentication-Results", AR)]), &Origin { own_mail: true, ..org(HOST) }).trusted);
        assert!(!assess_list(&hs(&[("Received", IN), ("Authentication-Results", AR)]), &org("")).trusted);
        // the header is still "present" when it is not trusted
        let t = assess_list(&hs(&[("Authentication-Results", forged_ar)]), &org(HOST));
        assert!(t.present && !t.trusted && t.results.is_empty());
        // a message without any results
        assert_eq!(assess_list(&hs(&[("Received", IN)]), &org(HOST)), Trust::default());
    }

    #[test]
    fn same_server_means_the_host_or_its_registered_domain() {
        assert!(same_server("mail.mine.test", "mail.mine.test"));
        assert!(same_server("MX1.Mine.Test.", "jmap.mine.test"));
        assert!(!same_server("mine.test.evil.net", "mail.mine.test"));
        assert!(!same_server("evilmine.test", "mine.test"));
        assert!(!same_server("", "mail.mine.test"));
        assert!(same_server("localhost", "localhost"), "the same single-label host is the same server");
        assert!(!same_server("mx", "mail.mine.test"));
    }

    #[test]
    fn submission_protocols() {
        assert!(is_submission("from x by y with ESMTPSA id 1"));
        assert!(is_submission("from x by y with UTF8SMTPA id 1"));
        assert!(is_submission("from x by y\r\n with lmtpa; date"));
        assert!(!is_submission("from x by y with ESMTPS id 1"));
        assert!(!is_submission("from x by y with ESMTP id ESMTPSA"));
        assert!(!is_submission("from x (with ESMTPSA) by y with SMTP id 1"), "inside a comment");
    }

    #[test]
    fn own_mail_is_judged_by_folder_and_draft() {
        assert!(is_own_mail(&["sent".into()], false));
        assert!(is_own_mail(&["inbox".into(), "drafts".into()], false));
        assert!(is_own_mail(&[], true));
        assert!(!is_own_mail(&["inbox".into()], false));
        let m = json!({"keywords": {"$imported": true}});
        assert!(Origin::of(&m, "h", &[]).imported);
    }
}
