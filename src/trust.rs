//! Reader safety: remote content per sender, the "External" sender label and the confirm
//! before sending outside the organisation. The decisions are pure functions; the callers
//! (reader, settings, composer) only gather their inputs.
//!
//! Bulwark references (1.21.2):
//! * `stores/settings-store.ts:444` `ExternalContentPolicy = 'ask' | 'block' | 'allow'`, default
//!   `'ask'` (:1118); `trustedSenders: string[]` of lower-cased addresses (:791, default :1159);
//!   `addTrustedSender` / `removeTrustedSender` / `isSenderTrusted` (:2172-2200) accept
//!   `Name <a@b>` and keep the bare lower-cased address. Trust is per address, never per domain.
//! * `components/email/email-viewer.tsx:3129-3146` `shouldBlockExternal`: trusted senders always
//!   load; otherwise `block` blocks, `ask` blocks until this message is allowed, `allow` never
//!   blocks. The banner (:6332-6375) offers "Load external content" only under `ask`, and
//!   "Trust sender" only when the sender is not flagged as forged.
//! * `lib/external-mail.ts` "who is outside the organisation" (Bulwark's own switches for the
//!   chip and the confirm are admin policy, `organisation.externalMail`; Barbican has no admin
//!   policy, so it keeps them as the user's settings `externalSenderLabel` and
//!   `externalRecipientConfirm`, see docs/from-csharp/spec/decisions.md D-262).
use serde_json::{json, Map, Value};

// ---------------------------------------------------------------- remote content policy

/// Settings key and values are Bulwark's, so a settings export moves between the apps.
pub const POLICY_KEY: &str = "externalContentPolicy";
pub const TRUSTED_KEY: &str = "trustedSenders";
pub const SENDER_LABEL_KEY: &str = "externalSenderLabel";
pub const RECIPIENT_CONFIRM_KEY: &str = "externalRecipientConfirm";

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ContentPolicy { Ask, Block, Allow }

impl ContentPolicy {
    pub const ALL: [ContentPolicy; 3] = [ContentPolicy::Ask, ContentPolicy::Block, ContentPolicy::Allow];

    pub fn parse(s: &str) -> Option<ContentPolicy> {
        match s { "ask" => Some(ContentPolicy::Ask), "block" => Some(ContentPolicy::Block), "allow" => Some(ContentPolicy::Allow), _ => None }
    }

    pub fn as_str(self) -> &'static str {
        match self { ContentPolicy::Ask => "ask", ContentPolicy::Block => "block", ContentPolicy::Allow => "allow" }
    }

    pub fn label(self) -> &'static str {
        match self {
            ContentPolicy::Ask => "Ask first (blocked until you choose)",
            ContentPolicy::Block => "Always block (only trusted senders load)",
            ContentPolicy::Allow => "Always load",
        }
    }
}

/// The policy in the preferences. A preference never set falls back to the 0.1.x "Load remote
/// images" switch (`legacy_allow`), so an upgrade keeps what the user chose; an unknown value
/// is `ask`, the safe default.
pub fn policy(prefs: &Map<String, Value>, legacy_allow: bool) -> ContentPolicy {
    match prefs.get(POLICY_KEY) {
        None => if legacy_allow { ContentPolicy::Allow } else { ContentPolicy::Ask },
        Some(v) => v.as_str().and_then(ContentPolicy::parse).unwrap_or(ContentPolicy::Ask),
    }
}

/// The bare, lower-cased address of `Name <a@b>` or `a@b` (Bulwark's `addTrustedSender` parse).
pub fn sender_key(s: &str) -> String {
    let t = s.trim();
    let inner = match (t.rfind('<'), t.ends_with('>')) {
        (Some(i), true) if i + 1 < t.len() - 1 => &t[i + 1..t.len() - 1],
        _ => t,
    };
    inner.trim().to_lowercase()
}

/// Something that can be a sender or recipient address: one `@`, text on both sides, no spaces.
pub fn is_address(s: &str) -> bool {
    match s.split_once('@') {
        Some((l, d)) => !l.is_empty() && d.contains('.') && !d.starts_with('.') && !d.ends_with('.') && !d.contains('@') && !s.chars().any(|c| c.is_whitespace() || c == '<' || c == '>'),
        None => false,
    }
}

/// The trusted addresses stored in the preferences (lower-cased, no duplicates, order kept).
pub fn trusted_list(prefs: &Map<String, Value>) -> Vec<String> {
    let mut out: Vec<String> = vec![];
    for v in prefs.get(TRUSTED_KEY).and_then(|v| v.as_array()).into_iter().flatten() {
        let k = sender_key(v.as_str().unwrap_or(""));
        if !k.is_empty() && !out.contains(&k) { out.push(k); }
    }
    out
}

pub fn is_trusted(list: &[String], address: &str) -> bool {
    let k = sender_key(address);
    !k.is_empty() && list.iter().any(|t| *t == k)
}

/// Add an address; false when it was already there or is not an address.
pub fn add_trusted(list: &mut Vec<String>, address: &str) -> bool {
    let k = sender_key(address);
    if !is_address(&k) || list.contains(&k) { return false; }
    list.push(k);
    true
}

/// Remove an address; false when it was not there (the settings page edits the list as text).
#[allow(dead_code)]
pub fn remove_trusted(list: &mut Vec<String>, address: &str) -> bool {
    let k = sender_key(address);
    let n = list.len();
    list.retain(|t| *t != k);
    list.len() != n
}

/// The settings page edits the list as text, one address per line (commas and semicolons
/// also separate). Lines that are not addresses are dropped, so half-typed text never trusts
/// anything.
pub fn parse_list(text: &str) -> Vec<String> {
    let mut out = vec![];
    for part in text.split(['\n', '\r', ',', ';']) {
        let k = sender_key(part);
        if is_address(&k) && !out.contains(&k) { out.push(k); }
    }
    out
}

pub fn list_text(list: &[String]) -> String { list.join("\n") }

pub fn trusted_json(list: &[String]) -> Value { json!(list) }

/// Whether a message's remote content loads when it opens: `allow`, or a trusted sender
/// (email-viewer.tsx:3135-3144). Under `ask` the user can still allow one message.
pub fn loads_on_open(policy: ContentPolicy, trusted: bool) -> bool {
    policy == ContentPolicy::Allow || trusted
}

/// What the reader banner for blocked remote content offers.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Offers {
    /// "Load images" for this message: only under `ask` (email-viewer.tsx:6359).
    pub load_once: bool,
    /// "Trust sender": there is a sender, and it is not already trusted. `forged` is true when the
    /// sender looks forged (email-viewer.tsx:6369 `!senderVerification`): trusting a forged
    /// address would load remote content for the next forgery too.
    pub trust: bool,
}

pub fn offers(policy: ContentPolicy, sender: &str, trusted: bool, forged: bool) -> Offers {
    Offers { load_once: policy == ContentPolicy::Ask, trust: is_address(&sender_key(sender)) && !trusted && !forged }
}

// ---------------------------------------------------------------- who is outside

/// The lower-cased domain of `a@b` or `Name <a@b>`; "" when there is none. IDN domains are
/// compared in their ASCII (punycode) form, as Bulwark does (lib/external-mail.ts:17).
pub fn domain_of(address: &str) -> String {
    let k = sender_key(address);
    let Some((_, d)) = k.rsplit_once('@') else { return String::new() };
    let d = d.trim().trim_end_matches('.');
    if d.is_empty() { return String::new(); }
    idna::domain_to_ascii(d).unwrap_or_else(|_| d.to_string())
}

/// The organisation's own domains: those of the user's identities (and login), the only
/// source Barbican has (Bulwark adds the admin's policy list, `internalDomains`,
/// lib/external-mail.ts:65). Deduplicated, ASCII form.
pub fn own_domains(addresses: &[String]) -> Vec<String> {
    let mut out: Vec<String> = vec![];
    for a in addresses {
        let d = domain_of(a);
        if !d.is_empty() && !out.contains(&d) { out.push(d); }
    }
    out
}

/// `domain` is one of `internal` or a subdomain of one (example.com covers eu.example.com).
pub fn domain_matches(domain: &str, internal: &[String]) -> bool {
    if domain.is_empty() { return false; }
    let d = domain.to_lowercase();
    internal.iter().any(|e| d == *e || d.ends_with(&format!(".{e}")))
}

/// Whether mail to `address` leaves the organisation. With no known domain nothing is external
/// (nobody can be told apart), and an address without a domain is not counted
/// (external-mail.ts:70).
pub fn is_external_address(address: &str, internal: &[String]) -> bool {
    if internal.is_empty() { return false; }
    let d = domain_of(address);
    !d.is_empty() && !domain_matches(&d, internal)
}

/// The "External" chip for a message: its From is outside the organisation, or has no domain
/// at all ("CEO <ceo>": no organisation sends like that, external-mail.ts:118). Off when the
/// setting is off or no own domain is known.
pub fn external_sender(from: &str, internal: &[String], label_on: bool) -> bool {
    if !label_on || internal.is_empty() { return false; }
    let d = domain_of(from);
    d.is_empty() || !domain_matches(&d, internal)
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ExternalRecipient {
    /// "To", "Cc" or "Bcc": the first field the address appears in.
    pub field: &'static str,
    pub address: String,
}

/// The recipients outside the organisation, each address once, in To, Cc, Bcc order
/// (external-mail.ts:83 `externalRecipients`). The lists are the raw field texts.
pub fn external_recipients(to: &str, cc: &str, bcc: &str, internal: &[String]) -> Vec<ExternalRecipient> {
    let mut out: Vec<ExternalRecipient> = vec![];
    let mut seen: Vec<String> = vec![];
    for (field, text) in [("To", to), ("Cc", cc), ("Bcc", bcc)] {
        for r in crate::recipients::parse_list(text) {
            let key = sender_key(&r.email);
            if key.is_empty() || seen.contains(&key) { continue; }
            seen.push(key);
            if is_external_address(&r.email, internal) { out.push(ExternalRecipient { field, address: r.email.trim().to_string() }); }
        }
    }
    out
}

/// The pre-send warning text for external recipients; None when the setting is off or nobody is
/// external. `advice` is the composer's "press Send again" line for the action being taken.
pub fn recipient_warning(on: bool, to: &str, cc: &str, bcc: &str, internal: &[String], advice: &str) -> Option<String> {
    if !on { return None; }
    let ext = external_recipients(to, cc, bcc, internal);
    if ext.is_empty() { return None; }
    let list: Vec<String> = ext.iter().map(|r| format!("{} ({})", r.address, r.field)).collect();
    Some(format!("This message goes outside your organisation to {}. {advice}", list.join(", ")))
}

/// Bulwark hides "Trust sender" whenever `getSenderVerification` is non-null (email-viewer.tsx:6369,
/// lib/email-headers.ts:132-161): the server's Authentication-Results failed for the From, or
/// they exist but none ties the message to the From domain. With no results at all there is
/// nothing to doubt, and trusting stays offered.
///
/// Only results our own server wrote count (`auth::Trust`, R3-06): results the sender wrote or
/// that were not written on arrival give no verdict, so they never hide "Trust sender" (a sender
/// could otherwise hide it from the user by sending a failing header) and never allow it either.
pub fn forged(t: &crate::auth::Trust, from: &str) -> bool {
    if !t.trusted { return false; }
    let res = &t.results;
    let aligned = crate::auth::aligned_pass(res, from);
    // only spf, dkim and dmarc count as results (email-headers.ts:136): an iprev-only header says nothing
    let present = res.iter().any(|r| matches!(r.method.as_str(), "spf" | "dkim" | "dmarc"));
    crate::auth::failure(res, aligned) || (present && !aligned)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn map(v: Value) -> Map<String, Value> { v.as_object().unwrap().clone() }
    fn own() -> Vec<String> { own_domains(&["me@example.test".into(), "Me <me@alt.example.test>".into()]) }

    #[test]
    fn policy_reads_bulwark_values_and_falls_back_safely() {
        assert_eq!(policy(&map(json!({})), false), ContentPolicy::Ask);
        assert_eq!(policy(&map(json!({})), true), ContentPolicy::Allow);
        assert_eq!(policy(&map(json!({"externalContentPolicy": "block"})), true), ContentPolicy::Block);
        assert_eq!(policy(&map(json!({"externalContentPolicy": "allow"})), false), ContentPolicy::Allow);
        assert_eq!(policy(&map(json!({"externalContentPolicy": "nonsense"})), true), ContentPolicy::Ask);
        assert_eq!(policy(&map(json!({"externalContentPolicy": 3})), true), ContentPolicy::Ask);
        for p in ContentPolicy::ALL { assert_eq!(ContentPolicy::parse(p.as_str()), Some(p)); }
    }

    #[test]
    fn loads_on_open_follows_bulwarks_should_block_external() {
        assert!(!loads_on_open(ContentPolicy::Ask, false));
        assert!(loads_on_open(ContentPolicy::Ask, true));
        assert!(!loads_on_open(ContentPolicy::Block, false));
        assert!(loads_on_open(ContentPolicy::Block, true), "block = always block unless trusted");
        assert!(loads_on_open(ContentPolicy::Allow, false));
    }

    #[test]
    fn banner_offers() {
        let o = offers(ContentPolicy::Ask, "ann@example.test", false, false);
        assert!(o.load_once && o.trust);
        let o = offers(ContentPolicy::Block, "ann@example.test", false, false);
        assert!(!o.load_once && o.trust, "block: no load-once, trust still offered");
        assert!(!offers(ContentPolicy::Ask, "ann@example.test", false, true).trust, "forged sender can't be trusted");
        assert!(!offers(ContentPolicy::Ask, "", false, false).trust, "no sender");
        assert!(!offers(ContentPolicy::Ask, "ann@example.test", true, false).trust);
    }

    #[test]
    fn trusted_list_normalises_like_bulwark() {
        assert_eq!(sender_key("Ann Lee <Ann@Example.TEST>"), "ann@example.test");
        assert_eq!(sender_key("  ann@example.test "), "ann@example.test");
        let mut l = vec![];
        assert!(add_trusted(&mut l, "Ann <Ann@Example.test>"));
        assert!(!add_trusted(&mut l, "ann@example.test"), "duplicate");
        assert!(!add_trusted(&mut l, "not an address"));
        assert!(!add_trusted(&mut l, ""));
        assert!(is_trusted(&l, "ANN@example.test"));
        assert!(!is_trusted(&l, "bob@example.test"));
        assert!(!is_trusted(&l, "other@example.test"), "trust is per address, not per domain");
        assert!(remove_trusted(&mut l, "Ann@example.test"));
        assert!(!remove_trusted(&mut l, "ann@example.test"));
        assert!(l.is_empty());
        let p = map(json!({"trustedSenders": ["A@x.test", "a@x.test", "<b@y.test>", 5, ""]}));
        assert_eq!(trusted_list(&p), vec!["a@x.test", "b@y.test"]);
    }

    #[test]
    fn list_text_round_trip_drops_junk() {
        assert_eq!(parse_list("A@x.test\n b@y.test, c@z.test;junk\n\nA@x.test\nhalf@"), vec!["a@x.test", "b@y.test", "c@z.test"]);
        assert_eq!(parse_list(&list_text(&["a@x.test".into(), "b@y.test".into()])), vec!["a@x.test", "b@y.test"]);
        assert!(parse_list("").is_empty());
    }

    #[test]
    fn external_addresses() {
        let own = own();
        assert!(!is_external_address("x@example.test", &own));
        assert!(!is_external_address("X@EU.Example.TEST", &own), "subdomain of an own domain");
        assert!(!is_external_address("x@alt.example.test", &own));
        assert!(is_external_address("x@other.test", &own));
        assert!(is_external_address("x@notexample.test", &own), "suffix without a dot is another domain");
        assert!(!is_external_address("nodomain", &own), "no domain: not counted");
        assert!(!is_external_address("x@other.test", &[]), "no own domain known: nobody is external");
        assert_eq!(domain_of("Name <a@Bücher.test>"), "xn--bcher-kva.test");
        assert_eq!(own_domains(&["a@x.test".into(), "b@X.test".into(), "junk".into()]), vec!["x.test"]);
    }

    #[test]
    fn sender_chip() {
        let own = own();
        assert!(external_sender("Ann <ann@other.test>", &own, true));
        assert!(!external_sender("Ann <ann@other.test>", &own, false), "setting off");
        assert!(!external_sender("ann@example.test", &own, true));
        assert!(external_sender("CEO <ceo>", &own, true), "a From without a domain counts as external");
        assert!(!external_sender("ann@other.test", &[], true), "no own domain known");
    }

    #[test]
    fn external_recipients_listed_once_in_field_order() {
        let own = own();
        let r = external_recipients("a@example.test, Bob <bob@other.test>", "BOB@other.test, c@third.test", "d@eu.example.test, e@fourth.test", &own);
        let got: Vec<(&str, &str)> = r.iter().map(|x| (x.field, x.address.as_str())).collect();
        assert_eq!(got, vec![("To", "bob@other.test"), ("Cc", "c@third.test"), ("Bcc", "e@fourth.test")]);
        assert!(external_recipients("a@example.test", "", "", &own).is_empty());
    }

    #[test]
    fn recipient_warning_text_and_switch() {
        let own = own();
        let advice = "Press Send again to send anyway.";
        assert!(recipient_warning(false, "x@other.test", "", "", &own, advice).is_none(), "off by default");
        assert!(recipient_warning(true, "a@example.test", "", "", &own, advice).is_none());
        let w = recipient_warning(true, "x@other.test", "y@third.test", "", &own, advice).unwrap();
        assert!(w.contains("x@other.test (To)") && w.contains("y@third.test (Cc)") && w.ends_with(advice), "{w}");
    }

    #[test]
    fn forged_sender_follows_bulwarks_sender_verification() {
        use crate::auth::{assess_list, Origin};
        const IN: &str = "from mx.sender.test by mx.mine.test (Stalwart) with ESMTPS id 1; Tue, 24 Nov 2026 10:00:00 +0000";
        let org = Origin { server_host: "mail.mine.test", imported: false, own_mail: false };
        let t = |v: &str| assess_list(&[("received".to_string(), IN.to_string()), ("authentication-results".to_string(), v.to_string())], &org);
        let h = |v: &str| t(&format!(" mx.mine.test{}", v.strip_prefix("mx.test").unwrap_or(v)));
        let from = "ann@example.test";
        assert!(!forged(&crate::auth::Trust::default(), from), "no results: nothing to doubt, trust stays offered");
        // results that are not our server's give no verdict either way
        let foreign = t("mx.sender.test; dmarc=fail header.from=example.test");
        assert!(foreign.present && !foreign.trusted);
        assert!(!forged(&foreign, from), "a failing header the sender wrote does not hide Trust sender");
        let submitted = assess_list(&[("received".to_string(), IN.replace("ESMTPS", "ESMTPSA")), ("authentication-results".to_string(), " mx.mine.test; dmarc=fail header.from=example.test".to_string())], &org);
        assert!(!forged(&submitted, from));
        // a header planted after the server's own does not change the verdict
        let planted = assess_list(&[("received".to_string(), IN.to_string()), ("authentication-results".to_string(), " mx.mine.test; dmarc=fail header.from=example.test".to_string()),
            ("authentication-results".to_string(), " mx.mine.test; dmarc=pass header.from=example.test".to_string())], &org);
        assert!(forged(&planted, from));
        assert!(!forged(&h("mx.test; dmarc=pass header.from=example.test"), from));
        assert!(!forged(&h("mx.test; dkim=pass header.d=mail.example.test"), from), "aligned pass");
        assert!(forged(&h("mx.test; dmarc=fail header.from=example.test"), from), "failed");
        assert!(!forged(&h("mx.test; iprev=pass policy.iprev=192.0.2.1"), from), "iprev alone is no verdict");
        assert!(forged(&h("mx.test; spf=pass smtp.mailfrom=bounce@other.test; dmarc=none header.from=example.test"), from),
            "results exist but only another domain passed: not tied to the From");
        assert!(forged(&h("mx.test; spf=pass smtp.mailfrom=bounce@other.test"), "CEO <ceo>"), "no usable From domain");
    }
}
