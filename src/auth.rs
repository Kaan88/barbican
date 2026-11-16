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

fn strip_comments(s: &str) -> String {
    let mut out = String::new();
    let mut depth = 0usize;
    for c in s.chars() {
        match c {
            '(' => depth += 1,
            ')' if depth > 0 => depth -= 1,
            _ if depth == 0 => out.push(c),
            _ => {}
        }
    }
    out
}

/// Parse one Authentication-Results value (the authserv-id is skipped).
pub fn parse(ar: &str) -> Vec<AuthResult> {
    let clean = strip_comments(ar);
    let mut out = vec![];
    for seg in clean.split(';') {
        let mut toks = seg.split_whitespace();
        let Some(first) = toks.next() else { continue };
        let Some((m, r)) = first.split_once('=') else { continue };
        let props = toks.filter_map(|t| t.split_once('=')).map(|(k, v)| (k.to_ascii_lowercase(), v.trim_matches('"').to_string())).collect();
        out.push(AuthResult { method: m.to_ascii_lowercase(), result: r.to_ascii_lowercase(), props });
    }
    out
}

/// The topmost Authentication-Results of a JMAP `headers` list (the one our server wrote).
pub fn topmost(headers: &[Value]) -> Vec<AuthResult> {
    crate::reader::headers_all(headers, "Authentication-Results").first().map(|h| parse(h)).unwrap_or_default()
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

#[cfg(test)]
mod tests {
    use super::*;

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
}
