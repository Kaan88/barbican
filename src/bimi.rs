//! BIMI on the receiving side: the verified-brand badge (Bulwark components/email/
//! brand-verified-badge.tsx, lib/bimi/{records,certificate,lookup,roots}.ts, app/api/bimi).
//!
//! A check mark stands beside the sender's name only when a Verified Mark Certificate (VMC) or
//! Common Mark Certificate (CMC) backs the domain's BIMI logo; a logo the sender only published in
//! DNS earns none (anyone can do that for a lookalike domain). The message itself must also have
//! passed DMARC for exactly the From domain in the results our own server wrote
//! (`auth::brand_domain`, the one trusted parse); this module only answers per domain.
//!
//! Bulwark does all of this in its own server route (it is a web app); Barbican is the client, so
//! it does it here: the DMARC policy (quarantine or reject at pct=100), the assertion record
//! (`default._bimi.<domain>`, else the organisational domain's), the evidence document (the PEM
//! chain at `a=`), the chain checked against the pinned roots (`bimi_roots.pem`, the same four
//! Mark Verifying Authority roots Bulwark pins, each with its SHA-256 fingerprint) by rustls-webpki
//! with the BIMI key purpose required, then the rules webpki does not know: the BIMI key purpose on
//! the issuing CA, the SAN rule of the draft, SCTs present, a logotype with the SVG, and
//! revocation by CRL (an unreachable CRL does not fail the check, as in Bulwark).
//!
//! Not done (as in Bulwark): the signatures on the embedded SCTs are not checked against the CT
//! log list. Not done here: the logo is not drawn, so Bulwark's SVG profile check has no
//! counterpart (the avatar code draws the logo it fetches itself).
use std::collections::HashMap;
use std::io::Read;
use std::sync::Mutex;
use std::time::{Duration, Instant};

use base64::Engine;
use rustls_pki_types::{CertificateDer, SignatureVerificationAlgorithm, UnixTime};
use sha2::{Digest, Sha256};
use webpki::{
    anchor_from_trusted_cert, BorrowedCertRevocationList, CertRevocationList, EndEntityCert, Error as WebpkiError, ExpirationPolicy, KeyUsage,
    RevocationCheckDepth, RevocationOptions, RevocationOptionsBuilder, UnknownStatusPolicy,
};

// ---------------------------------------------------------------- DNS records (records.ts)

/// The assertion record of a domain.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct Record {
    /// The indicator (SVG) location, https only.
    pub logo_url: Option<String>,
    /// The evidence document (mark certificate PEM) location, https only.
    pub evidence_url: Option<String>,
    /// `l=` and `a=` both empty: the domain asks for no indicator.
    pub declined: bool,
}

fn tags(record: &str) -> Vec<(String, String)> {
    let mut out: Vec<(String, String)> = vec![];
    for part in record.split(';') {
        if let Some((n, v)) = part.split_once('=') {
            let n = n.trim().to_ascii_lowercase();
            if !n.is_empty() && !out.iter().any(|(k, _)| *k == n) { out.push((n, v.trim().to_string())); }
        }
    }
    out
}

fn tag<'a>(t: &'a [(String, String)], name: &str) -> &'a str {
    t.iter().find(|(k, _)| k == name).map(|(_, v)| v.as_str()).unwrap_or("")
}

fn https_url(value: &str) -> Option<String> {
    // early drafts allowed a comma-separated list; the first entry counts
    let first = value.split(',').next()?.trim();
    if first.is_empty() { return None; }
    let url = reqwest::Url::parse(first).ok()?;
    (url.scheme() == "https").then(|| url.to_string())
}

fn is_record(record: &str, version: &str) -> bool {
    let Some((v, rest)) = record.trim().split_once('=') else { return false };
    v.trim().eq_ignore_ascii_case("v") && rest.split(';').next().unwrap_or("").trim().eq_ignore_ascii_case(version)
}

/// The one record that starts with `v=BIMI1` among the TXT answers. Zero or several make it
/// None (a wildcard TXT answers with the SPF record; two assertion records are an error).
pub fn pick_record(answers: &[String]) -> Option<Record> {
    let records: Vec<&String> = answers.iter().filter(|a| is_record(a, "BIMI1")).collect();
    if records.len() != 1 { return None; }
    let t = tags(records[0]);
    let (logo, evidence) = (tag(&t, "l"), tag(&t, "a"));
    let (logo_url, evidence_url) = (https_url(logo), https_url(evidence));
    // a tag that names a non-https location is invalid, not a declination
    if (!logo.is_empty() && logo_url.is_none()) || (!evidence.is_empty() && evidence_url.is_none()) { return None; }
    Some(Record { declined: logo_url.is_none() && evidence_url.is_none(), logo_url, evidence_url })
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub enum Policy { None, Quarantine, Reject }

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Dmarc { pub p: Policy, pub sp: Option<Policy>, pub pct: i64 }

fn policy(v: &str) -> Option<Policy> {
    match v.to_ascii_lowercase().as_str() { "none" => Some(Policy::None), "quarantine" => Some(Policy::Quarantine), "reject" => Some(Policy::Reject), _ => None }
}

/// The one DMARC record among the TXT answers, or None.
pub fn pick_dmarc(answers: &[String]) -> Option<Dmarc> {
    let records: Vec<&String> = answers.iter().filter(|a| is_record(a, "DMARC1")).collect();
    if records.len() != 1 { return None; }
    let t = tags(records[0]);
    let p = policy(tag(&t, "p"))?;
    let pct = match tag(&t, "pct") { "" => 100, v => v.parse::<i64>().unwrap_or(0) };
    Some(Dmarc { p, sp: policy(tag(&t, "sp")), pct })
}

/// Whether the published DMARC policy is strict enough for BIMI: quarantine or reject for every
/// message (pct=100), and for a subdomain an organisational domain that is not p=none either.
/// `author` is the record at `_dmarc.<author domain>`, `organizational` the one at the
/// organisational domain (the same record twice when they are the same domain).
pub fn dmarc_allows_bimi(author: Option<Dmarc>, organizational: Option<Dmarc>, is_subdomain: bool) -> bool {
    let applied = author.or(if is_subdomain { organizational } else { None });
    let Some(applied) = applied else { return false };
    let effective = if let Some(a) = author { a.p } else { applied.sp.unwrap_or(applied.p) };
    if effective == Policy::None || applied.pct != 100 { return false; }
    !(is_subdomain && organizational.is_some_and(|o| o.p == Policy::None))
}

/// A host name we may look up: public-looking, lower case, no trailing dot.
pub fn normalize_domain(domain: &str) -> Option<String> {
    let d = domain.trim().trim_end_matches('.').to_ascii_lowercase();
    if d.len() > 253 || !crate::avatars::valid_domain(&d) { return None; }
    let local = |s: &str| matches!(s, "localhost" | "local" | "internal" | "lan" | "home" | "arpa");
    (!d.split('.').any(local)).then_some(d)
}

// ---------------------------------------------------------------- DER (der.ts)

/// One TLV: (tag, content, what follows). Single-byte tags and definite lengths only.
fn tlv(b: &[u8]) -> Option<(u8, &[u8], &[u8])> {
    let tag = *b.first()?;
    if tag & 0x1f == 0x1f { return None; }
    let l0 = *b.get(1)? as usize;
    let (len, hdr) = if l0 < 0x80 { (l0, 2) } else {
        let n = l0 & 0x7f;
        if n == 0 || n > 4 { return None; }
        let mut v = 0usize;
        for i in 0..n { v = (v << 8) | *b.get(2 + i)? as usize; }
        (v, 2 + n)
    };
    let end = hdr.checked_add(len)?;
    (end <= b.len()).then(|| (tag, &b[hdr..end], &b[end..]))
}

fn children(mut c: &[u8]) -> Option<Vec<(u8, &[u8])>> {
    let mut out = vec![];
    while !c.is_empty() {
        let (t, content, rest) = tlv(c)?;
        out.push((t, content));
        c = rest;
        if out.len() > 4096 { return None; }
    }
    Some(out)
}

/// The content of every node with tag `want`, anywhere in `buf` (constructed nodes are entered).
fn strings_in(buf: &[u8], want: u8, depth: usize, out: &mut Vec<String>) {
    if depth > 16 { return; }
    let mut rest = buf;
    while !rest.is_empty() {
        let Some((t, content, next)) = tlv(rest) else { return };
        if t == want { if let Ok(s) = std::str::from_utf8(content) { out.push(s.to_string()); } }
        if t & 0x20 != 0 { strings_in(content, want, depth + 1, out); }
        rest = next;
    }
}

/// The DER content bytes of a dotted OID (the form webpki and the extensions use).
pub fn oid_der(dotted: &str) -> Vec<u8> {
    let n: Vec<u64> = dotted.split('.').filter_map(|p| p.parse().ok()).collect();
    let mut out = vec![(n[0] * 40 + n[1]) as u8];
    for &v in &n[2..] {
        let mut chunks = vec![(v & 0x7f) as u8];
        let mut v = v >> 7;
        while v > 0 { chunks.push((v & 0x7f) as u8 | 0x80); v >>= 7; }
        chunks.reverse();
        out.extend(chunks);
    }
    out
}

/// id-kp-BrandIndicatorforMessageIdentification, 1.3.6.1.5.5.7.3.31.
pub const BIMI_EKU: &[u8] = &[0x2b, 0x06, 0x01, 0x05, 0x05, 0x07, 0x03, 0x1f];
const LOGOTYPE_OID: &str = "1.3.6.1.5.5.7.1.12";
const SCT_LIST_OID: &str = "1.3.6.1.4.1.11129.2.4.2";
const CRL_DP_OID: &str = "2.5.29.31";
const MARK_TYPE_OID: &str = "1.3.6.1.4.1.53087.1.13";
const MAX_SVG_BYTES: usize = 64 * 1024;
const MAX_PEM_BYTES: usize = 128 * 1024;
const MAX_CRL_BYTES: usize = 2 * 1024 * 1024;
const MAX_CERTS: usize = 8;

/// The parts of a certificate the BIMI rules read.
#[derive(Debug, Default)]
struct CertInfo {
    subject: Vec<u8>,
    issuer: Vec<u8>,
    not_after: i64,
    exts: Vec<(Vec<u8>, Vec<u8>)>,
}

fn time(tag: u8, c: &[u8]) -> Option<i64> {
    let s = std::str::from_utf8(c).ok()?;
    let (fmt, s) = if tag == 0x17 {
        // two-digit year: 50 and over is 19xx (RFC 5280 §4.1.2.5.1)
        let yy: u32 = s.get(..2)?.parse().ok()?;
        ("%Y%m%d%H%M%SZ", format!("{}{}", if yy < 50 { "20" } else { "19" }, s))
    } else { ("%Y%m%d%H%M%SZ", s.to_string()) };
    chrono::NaiveDateTime::parse_from_str(&s, fmt).ok().map(|d| d.and_utc().timestamp())
}

fn parse_cert(der: &[u8]) -> Option<CertInfo> {
    let (t, cert, _) = tlv(der)?;
    if t != 0x30 { return None; }
    let (t, tbs, _) = tlv(cert)?;
    if t != 0x30 { return None; }
    let f = children(tbs)?;
    let mut i = 0;
    if f.get(i)?.0 == 0xa0 { i += 1; }
    i += 3; // serial, signature algorithm, issuer is next
    let issuer = f.get(i - 1)?.1.to_vec();
    let validity = children(f.get(i)?.1)?;
    let not_after = time(validity.get(1)?.0, validity.get(1)?.1)?;
    let subject = f.get(i + 1)?.1.to_vec();
    let mut info = CertInfo { subject, issuer, not_after, exts: vec![] };
    if let Some((_, ext)) = f.iter().skip(i + 2).find(|(t, _)| *t == 0xa3) {
        let (_, list, _) = tlv(ext)?;
        for (t, e) in children(list)? {
            if t != 0x30 { return None; }
            let parts = children(e)?;
            let oid = parts.first().filter(|p| p.0 == 0x06)?.1.to_vec();
            let value = parts.last().filter(|p| p.0 == 0x04)?.1.to_vec();
            info.exts.push((oid, value));
        }
    }
    Some(info)
}

impl CertInfo {
    fn ext(&self, oid: &str) -> Option<&[u8]> {
        let o = oid_der(oid);
        self.exts.iter().find(|(k, _)| *k == o).map(|(_, v)| v.as_slice())
    }
    fn is_ca(&self) -> bool {
        self.ext("2.5.29.19").and_then(|v| tlv(v)).and_then(|(_, c, _)| children(c)).and_then(|ch| ch.first().copied()).is_some_and(|(t, c)| t == 0x01 && c.first().is_some_and(|b| *b != 0))
    }
    fn ekus(&self) -> Vec<Vec<u8>> {
        self.ext("2.5.29.37").and_then(|v| tlv(v)).and_then(|(_, c, _)| children(c)).map(|ch| ch.into_iter().filter(|(t, _)| *t == 0x06).map(|(_, c)| c.to_vec()).collect()).unwrap_or_default()
    }
    fn has_bimi_eku(&self) -> bool { self.ekus().iter().any(|e| e == BIMI_EKU) }
    fn san_dns(&self) -> Vec<String> {
        let Some(v) = self.ext("2.5.29.17").and_then(|v| tlv(v)).and_then(|(_, c, _)| children(c)) else { return vec![] };
        v.into_iter().filter(|(t, _)| *t == 0x82).filter_map(|(_, c)| std::str::from_utf8(c).ok()).map(|s| s.trim().trim_end_matches('.').to_ascii_lowercase()).collect()
    }
    fn crl_urls(&self) -> Vec<String> {
        let Some(v) = self.ext(CRL_DP_OID) else { return vec![] };
        let mut out = vec![];
        // GeneralName uniformResourceIdentifier is [6] IMPLICIT IA5String
        strings_in(v, 0x86, 0, &mut out);
        out.into_iter().filter(|u| u.starts_with("http://") || u.starts_with("https://")).collect()
    }
}

/// The first value of attribute `oid` in a Name (the content of its outer SEQUENCE).
fn attribute(name: &[u8], oid: &str) -> Option<String> {
    let want = oid_der(oid);
    for (t, rdn) in children(name)? {
        if t != 0x31 { continue; }
        for (t, atv) in children(rdn)? {
            if t != 0x30 { continue; }
            let p = children(atv)?;
            if p.first().is_some_and(|(t, c)| *t == 0x06 && *c == want.as_slice()) {
                let (vt, vc) = p.get(1)?;
                if matches!(vt, 0x0c | 0x13 | 0x16 | 0x14) { return std::str::from_utf8(vc).ok().map(String::from); }
            }
        }
    }
    None
}

// ---------------------------------------------------------------- the evidence document (certificate.ts)

#[derive(Debug, Clone, Copy, PartialEq)]
pub enum MarkType { Verified, Common }

#[derive(Debug, Clone, PartialEq)]
pub struct Mark {
    pub svg: String,
    pub mark_type: MarkType,
    pub organization: Option<String>,
    pub issuer: Option<String>,
    /// When the certificate expires (Unix seconds).
    pub not_after: i64,
}

const PEM_BEGIN: &str = "-----BEGIN CERTIFICATE-----";
const PEM_END: &str = "-----END CERTIFICATE-----";

/// The certificates (DER) of a PEM bundle, each from a BEGIN line to the next END line.
pub fn split_pem(pem: &str) -> Result<Vec<Vec<u8>>, String> {
    let mut out = vec![];
    let mut pos = 0;
    while let Some(b) = pem[pos..].find(PEM_BEGIN) {
        let start = pos + b + PEM_BEGIN.len();
        let Some(e) = pem[start..].find(PEM_END) else { break };
        let body: String = pem[start..start + e].chars().filter(|c| !c.is_whitespace()).collect();
        if out.len() >= MAX_CERTS { return Err("too many certificates".into()); }
        out.push(base64::engine::general_purpose::STANDARD.decode(body).map_err(|_| "unparsable PEM".to_string())?);
        pos = start + e + PEM_END.len();
    }
    Ok(out)
}

/// The SAN rule of the draft (§5.2): names with a `_bimi` label pin a selector, the others cover
/// the default one.
pub fn san_matches(names: &[String], domains: &[String], selector: &str) -> bool {
    let has_bimi = |n: &&String| n.split('.').any(|l| l == "_bimi");
    domains.iter().any(|d| {
        let d = d.to_ascii_lowercase();
        names.iter().filter(has_bimi).any(|n| *n == format!("{}._bimi.{d}", selector.to_ascii_lowercase()))
            || names.iter().filter(|n| !has_bimi(n)).any(|n| *n == d)
    })
}

/// The SVG in the logotype extension: a `data:` URI holding gzip-compressed, base64-encoded SVG.
pub fn logotype_svg(ext: &[u8]) -> Option<String> {
    let mut uris = vec![];
    strings_in(ext, 0x16, 0, &mut uris);
    for uri in uris {
        let uri = uri.trim();
        let lower = uri.to_ascii_lowercase();
        let Some(rest) = lower.strip_prefix("data:image/svg+xml") else { continue };
        let Some(comma) = rest.find(',') else { continue };
        if !rest[..comma].ends_with(";base64") { continue; }
        // the data after the comma, in its original case
        let data: String = uri[uri.len() - (rest.len() - comma - 1)..].chars().filter(|c| !c.is_whitespace()).collect();
        let mut bytes = base64::engine::general_purpose::STANDARD.decode(data).ok()?;
        if bytes.starts_with(&[0x1f, 0x8b]) {
            let mut out = vec![];
            flate2::read::GzDecoder::new(bytes.as_slice()).take(MAX_SVG_BYTES as u64 + 1).read_to_end(&mut out).ok()?;
            bytes = out;
        }
        if bytes.is_empty() || bytes.len() > MAX_SVG_BYTES { return None; }
        return String::from_utf8(bytes).ok();
    }
    None
}

static ALGS: &[&dyn SignatureVerificationAlgorithm] = &[
    webpki::ring::ECDSA_P256_SHA256, webpki::ring::ECDSA_P256_SHA384, webpki::ring::ECDSA_P384_SHA256, webpki::ring::ECDSA_P384_SHA384,
    webpki::ring::RSA_PKCS1_2048_8192_SHA256, webpki::ring::RSA_PKCS1_2048_8192_SHA384, webpki::ring::RSA_PKCS1_2048_8192_SHA512,
    webpki::ring::RSA_PKCS1_3072_8192_SHA384, webpki::ring::RSA_PKCS1_2048_8192_SHA256_ABSENT_PARAMS,
    webpki::ring::RSA_PKCS1_2048_8192_SHA384_ABSENT_PARAMS, webpki::ring::RSA_PKCS1_2048_8192_SHA512_ABSENT_PARAMS,
];

/// What the validation needs from the outside.
pub struct Options<'a> {
    /// Where the assertion record was found and its organisational domain.
    pub domains: &'a [String],
    pub selector: &'a str,
    /// The pinned roots (DER). A root in the sender's PEM is only ever a step on the way.
    pub roots: &'a [Vec<u8>],
    /// Now, Unix seconds.
    pub now: u64,
}

/// Validate the PEM chain of a VMC or CMC (draft-fetch-validation-vmc-wchuang). `fetch_crls`
/// gets the CRL URLs of the validated chain and returns the CRLs (DER) it could get; none is
/// "not revoked", like browsers.
pub fn validate(pem: &str, o: &Options, fetch_crls: &mut dyn FnMut(&[String]) -> Vec<Vec<u8>>) -> Result<Mark, String> {
    let ders = split_pem(pem)?;
    let infos: Vec<CertInfo> = ders.iter().map(|d| parse_cert(d).ok_or("unparsable certificate")).collect::<Result<_, _>>()?;
    let leaves: Vec<usize> = (0..infos.len()).filter(|&i| !infos[i].is_ca() && infos[i].has_bimi_eku()).collect();
    let leaf = match leaves.as_slice() {
        [one] => *one,
        [] => return Err("no mark certificate".into()),
        _ => return Err("more than one mark certificate".into()),
    };
    let leaf_der = CertificateDer::from(ders[leaf].as_slice());
    let ee = EndEntityCert::try_from(&leaf_der).map_err(|_| "unparsable certificate".to_string())?;
    let others: Vec<CertificateDer> = (0..ders.len()).filter(|&i| i != leaf).map(|i| CertificateDer::from(ders[i].as_slice())).collect();
    let root_ders: Vec<CertificateDer> = o.roots.iter().map(|d| CertificateDer::from(d.as_slice())).collect();
    let anchors: Vec<_> = root_ders.iter().filter_map(|d| anchor_from_trusted_cert(d).ok()).collect();
    let now = UnixTime::since_unix_epoch(Duration::from_secs(o.now));
    let verify = |rev: Option<RevocationOptions<'_>>| ee.verify_for_usage(ALGS, &anchors, &others, now, KeyUsage::required(BIMI_EKU), rev, None);
    let path = verify(None).map_err(|e| match e {
        WebpkiError::UnknownIssuer => "chain does not reach a trusted root".to_string(),
        e => format!("certificate rejected: {e}"),
    })?;
    // the CA that issued the leaf carries the BIMI key purpose too (unless it is the root itself)
    let mut chain_infos = vec![];
    for cert in path.intermediate_certificates() {
        chain_infos.push(parse_cert(cert.der().as_ref()).ok_or("unparsable certificate")?);
    }
    if chain_infos.first().is_some_and(|c| !c.has_bimi_eku()) { return Err("missing BIMI key purpose".into()); }
    let mut urls: Vec<String> = infos[leaf].crl_urls();
    for c in &chain_infos { urls.extend(c.crl_urls()); }
    drop(path);
    {
        let crls = fetch_crls(&urls);
        let parsed: Vec<CertRevocationList> = crls.iter().filter_map(|d| BorrowedCertRevocationList::from_der(d).ok()).map(CertRevocationList::Borrowed).collect();
        let refs: Vec<&CertRevocationList> = parsed.iter().collect();
        if let Ok(b) = RevocationOptionsBuilder::new(&refs) {
            let rev = b.with_depth(RevocationCheckDepth::Chain).with_status_policy(UnknownStatusPolicy::Allow).with_expiration_policy(ExpirationPolicy::Ignore).build();
            // a CRL that does not belong or does not verify is ignored; only a listed serial fails
            if matches!(verify(Some(rev)), Err(WebpkiError::CertRevoked)) { return Err("certificate revoked".into()); }
        }
    }
    let leaf_info = &infos[leaf];
    if !san_matches(&leaf_info.san_dns(), o.domains, o.selector) { return Err("domain not in certificate".into()); }
    if leaf_info.ext(SCT_LIST_OID).is_none() { return Err("not logged to certificate transparency".into()); }
    let svg = leaf_info.ext(LOGOTYPE_OID).and_then(logotype_svg).ok_or("no logo in certificate")?;
    let mark_type = if attribute(&leaf_info.subject, MARK_TYPE_OID).as_deref() == Some("Prior Use Mark") { MarkType::Common } else { MarkType::Verified };
    Ok(Mark {
        svg, mark_type,
        organization: attribute(&leaf_info.subject, "2.5.4.10"),
        issuer: attribute(&leaf_info.issuer, "2.5.4.10"),
        not_after: leaf_info.not_after,
    })
}

/// The pinned roots (DER): `bimi_roots.pem`, each block with its fingerprint above it; one that
/// does not match its pin is dropped.
pub fn roots() -> Vec<Vec<u8>> {
    parse_roots(include_str!("bimi_roots.pem"))
}

fn parse_roots(text: &str) -> Vec<Vec<u8>> {
    let mut out = vec![];
    let mut pin = String::new();
    let mut block = String::new();
    let mut inside = false;
    for line in text.lines() {
        if let Some(fp) = line.strip_prefix("# sha256:") { pin = fp.chars().filter(|c| c.is_ascii_hexdigit()).collect::<String>().to_ascii_lowercase(); }
        if line.starts_with(PEM_BEGIN) { inside = true; block.clear(); }
        if inside { block.push_str(line); block.push('\n'); }
        if line.starts_with(PEM_END) {
            inside = false;
            if let Ok(mut d) = split_pem(&block) {
                if let Some(der) = d.pop() {
                    let fp: String = Sha256::digest(&der).iter().map(|b| format!("{b:02x}")).collect();
                    if fp == pin { out.push(der); }
                }
            }
        }
    }
    out
}

// ---------------------------------------------------------------- the lookup (lookup.ts)

/// What a lookup answers for a From domain.
#[derive(Debug, Clone, PartialEq)]
pub enum Info {
    None,
    /// Only the DNS record backs the logo: no badge.
    SelfAsserted,
    /// A VMC or CMC backs the logo.
    Verified { mark_type: MarkType, organization: Option<String>, issuer: Option<String>, not_after: i64 },
}

/// A passing failure (DNS, network): asked again soon.
#[derive(Debug)]
pub struct Temporary(#[allow(dead_code)] pub String);

pub trait Net {
    /// The TXT records of a name (each record's strings joined); empty when there are none.
    fn txt(&self, name: &str) -> Result<Vec<String>, Temporary>;
    /// GET with a byte ceiling; None for a refused or unusable answer.
    fn get(&self, url: &str, max: usize, allow_http: bool) -> Result<Option<Vec<u8>>, Temporary>;
}

const SELECTOR: &str = "default";

/// The receiver side of BIMI for one From domain.
pub fn lookup(net: &dyn Net, domain: &str, roots: &[Vec<u8>], now: u64) -> Result<Info, Temporary> {
    let Some(domain) = normalize_domain(domain) else { return Ok(Info::None) };
    let org = crate::avatars::root_domain(&domain);
    let is_sub = org != domain;
    let author = pick_dmarc(&net.txt(&format!("_dmarc.{domain}"))?);
    let organizational = if is_sub { pick_dmarc(&net.txt(&format!("_dmarc.{org}"))?) } else { author };
    if !dmarc_allows_bimi(author, organizational, is_sub) { return Ok(Info::None); }
    // the author domain's record, else the organisational domain's; a declining record stops the search
    let mut record = pick_record(&net.txt(&format!("{SELECTOR}._bimi.{domain}"))?);
    if record.is_none() && is_sub { record = pick_record(&net.txt(&format!("{SELECTOR}._bimi.{org}"))?); }
    let Some(record) = record.filter(|r| !r.declined) else { return Ok(Info::None) };
    let Some(evidence) = &record.evidence_url else {
        return Ok(if record.logo_url.is_some() { Info::SelfAsserted } else { Info::None });
    };
    let Some(pem) = net.get(evidence, MAX_PEM_BYTES, false)? else { return Ok(Info::None) };
    let Ok(pem) = String::from_utf8(pem) else { return Ok(Info::None) };
    let mut domains = vec![domain.clone()];
    if org != domain { domains.push(org); }
    let opts = Options { domains: &domains, selector: SELECTOR, roots, now };
    let mut fetch = |urls: &[String]| -> Vec<Vec<u8>> {
        urls.iter().take(6).filter_map(|u| net.get(u, MAX_CRL_BYTES, true).ok().flatten()).collect()
    };
    // an evidence document that does not validate fails BIMI outright; the self-published logo
    // next to it does not count instead
    Ok(match validate(&pem, &opts, &mut fetch) {
        Ok(m) => Info::Verified { mark_type: m.mark_type, organization: m.organization, issuer: m.issuer, not_after: m.not_after },
        Err(_) => Info::None,
    })
}

/// The real network: the system resolver and the avatar code's capped, public-hosts-only GET.
struct Live;

impl Net for Live {
    fn txt(&self, name: &str) -> Result<Vec<String>, Temporary> { Ok(crate::avatars::txt_records(name)) }
    fn get(&self, url: &str, max: usize, allow_http: bool) -> Result<Option<Vec<u8>>, Temporary> {
        // the URLs come from DNS records and certificates any sender may publish: public names only
        if !crate::avatars::safe_public_host(url, allow_http) { return Ok(None); }
        let Some(http) = crate::avatars::http() else { return Err(Temporary("no client".into())) };
        let mut r = http.get(url).send().map_err(|e| Temporary(e.to_string()))?;
        let st = r.status();
        if st.is_server_error() || st.as_u16() == 429 { return Err(Temporary(st.to_string())); }
        if !st.is_success() || r.content_length().is_some_and(|n| n > max as u64) { return Ok(None); }
        let mut bytes = vec![];
        (&mut r).take(max as u64 + 1).read_to_end(&mut bytes).map_err(|e| Temporary(e.to_string()))?;
        Ok((bytes.len() <= max).then_some(bytes))
    }
}

const POSITIVE_TTL: Duration = Duration::from_secs(24 * 3600);
const NEGATIVE_TTL: Duration = Duration::from_secs(6 * 3600);
const ERROR_TTL: Duration = Duration::from_secs(10 * 60);
const CACHE_MAX: usize = 500;

static CACHE: Mutex<Option<HashMap<String, (Instant, Duration, Info)>>> = Mutex::new(None);

/// The BIMI answer for a From domain, from the cache or the network. Blocks: run it off the UI
/// thread. Only worth asking for a domain `auth::brand_domain` named, and only with the
/// "Show favicons and brand logos" setting on (it reaches the sender's DNS and hosts).
pub fn info(domain: &str) -> Info {
    let Some(domain) = normalize_domain(domain) else { return Info::None };
    let key = domain.clone();
    if let Some((at, ttl, info)) = CACHE.lock().unwrap_or_else(|e| e.into_inner()).as_ref().and_then(|c| c.get(&key)) {
        if at.elapsed() < *ttl { return info.clone(); }
    }
    let now = chrono::Utc::now().timestamp().max(0) as u64;
    let (info, ttl) = match lookup(&Live, &domain, &roots(), now) {
        Ok(i @ Info::Verified { not_after, .. }) => {
            let left = (not_after - now as i64).max(0) as u64;
            (i, POSITIVE_TTL.min(Duration::from_secs(left)))
        }
        Ok(i) => (i, NEGATIVE_TTL),
        Err(_) => (Info::None, ERROR_TTL),
    };
    let mut g = CACHE.lock().unwrap_or_else(|e| e.into_inner());
    let c = g.get_or_insert_with(HashMap::new);
    if c.len() >= CACHE_MAX { c.clear(); }
    c.insert(key, (Instant::now(), ttl, info.clone()));
    info
}

/// The line the badge says (Bulwark `email_viewer.brand_logo.verified` / `verified_by`).
pub fn description(domain: &str, info: &Info) -> Option<String> {
    let Info::Verified { organization, issuer, .. } = info else { return None };
    let org = organization.clone().unwrap_or_else(|| domain.to_string());
    Some(match issuer {
        Some(i) => format!("{i} verified that this logo belongs to {org}."),
        None => format!("This logo is verified as belonging to {org}."),
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    // Test PKI made with openssl (src/bimi_fixtures, public certificates only): a root, an issuing CA
    // with the BIMI key purpose, and a leaf for brand.example.test with SCTs and a logotype.
    const ROOT: &str = include_str!("bimi_fixtures/root.pem");
    const INTER: &str = include_str!("bimi_fixtures/inter.pem");
    const INTER_PLAIN: &str = include_str!("bimi_fixtures/interplain.pem");
    const LEAF: &str = include_str!("bimi_fixtures/leaf.pem");
    const LEAF_REVOKED: &str = include_str!("bimi_fixtures/leafrevoked.pem");
    const LEAF_NO_LOGO: &str = include_str!("bimi_fixtures/leafnologo.pem");
    const LEAF_NO_SCT: &str = include_str!("bimi_fixtures/leafnosct.pem");
    const LEAF_NO_EKU: &str = include_str!("bimi_fixtures/leafnoeku.pem");
    const LEAF_PLAIN: &str = include_str!("bimi_fixtures/leafplain.pem");
    const LEAF_EVIL: &str = include_str!("bimi_fixtures/leafevil.pem");
    const EVIL_ROOT: &str = include_str!("bimi_fixtures/evilroot.pem");
    const CRL: &[u8] = include_bytes!("bimi_fixtures/inter.crl.der");

    fn now() -> u64 { chrono::NaiveDate::from_ymd_opt(2026, 12, 1).unwrap().and_hms_opt(0, 0, 0).unwrap().and_utc().timestamp() as u64 }
    fn roots() -> Vec<Vec<u8>> { split_pem(ROOT).unwrap() }
    fn doms(d: &[&str]) -> Vec<String> { d.iter().map(|s| s.to_string()).collect() }
    fn check(pem: &str, domains: &[&str], crls: Vec<Vec<u8>>) -> Result<Mark, String> {
        let r = roots();
        let d = doms(domains);
        validate(pem, &Options { domains: &d, selector: "default", roots: &r, now: now() }, &mut |_| crls.clone())
    }
    fn chain(parts: &[&str]) -> String { parts.join("\n") }

    #[test]
    fn records() {
        let one = |s: &str| pick_record(&[s.to_string()]);
        let r = one("v=BIMI1; l=https://x.example/l.svg; a=https://x.example/c.pem").unwrap();
        assert_eq!((r.logo_url.as_deref(), r.evidence_url.as_deref(), r.declined), (Some("https://x.example/l.svg"), Some("https://x.example/c.pem"), false));
        assert!(one("v=BIMI1; l=; a=").unwrap().declined, "both empty: the domain asks for no indicator");
        assert!(one("v=BIMI1;").unwrap().declined);
        assert_eq!(one("v=BIMI1; l=http://x.example/l.svg"), None, "a non-https location is invalid, not a declination");
        assert_eq!(one("v=BIMI1; l=https://x.example/l.svg; a=http://x.example/c.pem"), None);
        assert_eq!(one("v=spf1 include:x"), None);
        assert_eq!(one("v=BIMI2; l=https://x.example/l.svg"), None);
        assert_eq!(pick_record(&["v=BIMI1; l=https://a.example/l.svg".into(), "v=BIMI1; l=https://b.example/l.svg".into()]), None, "two assertion records are an error");
        assert_eq!(pick_record(&["v=spf1 -all".into(), " V = bimi1 ; L=https://a.example/l.svg ".into()]).unwrap().logo_url.as_deref(), Some("https://a.example/l.svg"));
        assert_eq!(one("v=BIMI1; l=https://a.example/l.svg,https://b.example/l.svg").unwrap().logo_url.as_deref(), Some("https://a.example/l.svg"));
    }

    #[test]
    fn dmarc_policy() {
        let d = |s: &str| pick_dmarc(&[s.to_string()]);
        assert_eq!(d("v=DMARC1; p=reject"), Some(Dmarc { p: Policy::Reject, sp: None, pct: 100 }));
        assert_eq!(d("v=DMARC1; p=quarantine; sp=none; pct=50").unwrap().pct, 50);
        assert_eq!(d("v=DMARC1; pct=100"), None, "p is required");
        assert_eq!(d("v=DMARC1; p=bogus"), None);
        assert_eq!(pick_dmarc(&["v=DMARC1; p=none".into(), "v=DMARC1; p=reject".into()]), None);
        let rej = d("v=DMARC1; p=reject");
        let none = d("v=DMARC1; p=none");
        let part = d("v=DMARC1; p=reject; pct=50");
        assert!(dmarc_allows_bimi(rej, rej, false));
        assert!(!dmarc_allows_bimi(none, none, false));
        assert!(!dmarc_allows_bimi(part, part, false), "pct below 100");
        assert!(!dmarc_allows_bimi(None, None, false));
        // a subdomain with no record of its own takes the organisational one (sp, else p)
        assert!(dmarc_allows_bimi(None, rej, true));
        assert!(!dmarc_allows_bimi(None, d("v=DMARC1; p=reject; sp=none"), true));
        // a subdomain that enforces under an organisational domain that does not
        assert!(!dmarc_allows_bimi(rej, none, true));
        assert!(dmarc_allows_bimi(rej, rej, true));
    }

    #[test]
    fn domains_we_may_look_up() {
        assert_eq!(normalize_domain(" Brand.Example.COM. ").as_deref(), Some("brand.example.com"));
        for bad in ["localhost", "nas.local", "host.lan", "x.internal", "a", "1.2.3.4.in-addr.arpa", "", "a b.example", "x.home.example"] {
            assert_eq!(normalize_domain(bad), None, "{bad}");
        }
    }

    #[test]
    fn oids_and_pinned_roots() {
        assert_eq!(oid_der("1.3.6.1.5.5.7.3.31"), BIMI_EKU);
        assert_eq!(oid_der("2.5.4.10"), [0x55, 0x04, 0x0a]);
        assert_eq!(oid_der("1.2.840.113549.1.1.11"), [0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x0b]);
        // all four Mark Verifying Authority roots Bulwark pins load, each against its fingerprint
        assert_eq!(super::roots().len(), 4);
        // a block whose bytes do not match its pin is dropped
        let tampered = include_str!("bimi_roots.pem").replacen("# sha256: ", "# sha256: 00:", 1);
        assert_eq!(parse_roots(&tampered).len(), 3);
    }

    #[test]
    fn a_valid_mark_certificate() {
        let m = check(&chain(&[LEAF, INTER]), &["brand.example.test"], vec![]).unwrap();
        assert_eq!(m.organization.as_deref(), Some("Example Brand Inc"));
        assert_eq!(m.issuer.as_deref(), Some("Test Mark CA"));
        assert_eq!(m.mark_type, MarkType::Verified);
        assert!(m.svg.contains("<svg") && m.svg.contains("Example Brand"), "the logo is the SVG inside the certificate");
        assert!(m.not_after > now() as i64);
        // the order of the sender's certificates does not matter, a root in the PEM is only a step
        assert!(check(&chain(&[INTER, ROOT, LEAF]), &["brand.example.test"], vec![]).is_ok());
        // the organisational domain's name in the SAN is not what a sub-domain's certificate says
        assert!(check(&chain(&[LEAF, INTER]), &["example.test", "brand.example.test"], vec![]).is_ok());
    }

    #[test]
    fn a_mark_certificate_that_does_not_validate() {
        let bad = |pem: &str, domains: &[&str]| check(pem, domains, vec![]).unwrap_err();
        // the chain must reach a pinned root: the sender's own root proves nothing
        let untrusted = |e: String| e == "chain does not reach a trusted root" || e.starts_with("certificate rejected");
        assert!(untrusted(bad(&chain(&[LEAF_EVIL, EVIL_ROOT]), &["brand.example.test"])));
        assert!(untrusted(bad(LEAF, &["brand.example.test"])), "the intermediate is missing");
        // the CA that issued the leaf must carry the BIMI key purpose
        let e = bad(&chain(&[LEAF_PLAIN, INTER_PLAIN]), &["brand.example.test"]);
        assert!(e == "missing BIMI key purpose" || e.starts_with("certificate rejected"), "{e}");
        // a certificate with no BIMI key purpose is no mark certificate
        assert_eq!(bad(&chain(&[LEAF_NO_EKU, INTER]), &["brand.example.test"]), "no mark certificate");
        // no logo, no SCTs
        assert_eq!(bad(&chain(&[LEAF_NO_LOGO, INTER]), &["brand.example.test"]), "no logo in certificate");
        assert_eq!(bad(&chain(&[LEAF_NO_SCT, INTER]), &["brand.example.test"]), "not logged to certificate transparency");
        // the domain must be in the certificate
        assert_eq!(bad(&chain(&[LEAF, INTER]), &["other.example.test"]), "domain not in certificate");
        assert_eq!(bad(&chain(&[LEAF, INTER]), &["test"]), "domain not in certificate");
        // two mark certificates, none, or garbage
        assert_eq!(bad(&chain(&[LEAF, LEAF_REVOKED, INTER]), &["brand.example.test"]), "more than one mark certificate");
        assert_eq!(bad(&chain(&[INTER]), &["brand.example.test"]), "no mark certificate");
        assert_eq!(bad("not a pem", &["brand.example.test"]), "no mark certificate");
        assert!(check("-----BEGIN CERTIFICATE-----\n!!!\n-----END CERTIFICATE-----", &["brand.example.test"], vec![]).is_err());
        assert!(check("-----BEGIN CERTIFICATE-----\nAAAA\n-----END CERTIFICATE-----", &["brand.example.test"], vec![]).is_err());
    }

    #[test]
    fn validity_period_and_revocation() {
        // outside the validity period (before the certificates exist)
        let r = roots();
        let d = doms(&["brand.example.test"]);
        let early = validate(&chain(&[LEAF, INTER]), &Options { domains: &d, selector: "default", roots: &r, now: 946_684_800 }, &mut |_| vec![]);
        assert!(early.is_err(), "{early:?}");
        // a revoked leaf listed on the issuing CA's CRL
        assert_eq!(check(&chain(&[LEAF_REVOKED, INTER]), &["brand.example.test"], vec![CRL.to_vec()]).unwrap_err(), "certificate revoked");
        // the same CRL does not touch another certificate
        assert!(check(&chain(&[LEAF, INTER]), &["brand.example.test"], vec![CRL.to_vec()]).is_ok());
        // no CRL to be had, or one that is not a CRL, counts as not revoked
        assert!(check(&chain(&[LEAF_REVOKED, INTER]), &["brand.example.test"], vec![]).is_ok());
        assert!(check(&chain(&[LEAF_REVOKED, INTER]), &["brand.example.test"], vec![vec![1, 2, 3]]).is_ok());
        let mut cut = CRL.to_vec();
        let n = cut.len();
        cut[n - 3] ^= 0xff;
        assert!(check(&chain(&[LEAF_REVOKED, INTER]), &["brand.example.test"], vec![cut]).is_ok(), "a CRL that does not verify is ignored");
    }

    #[test]
    fn the_san_rule_of_the_draft() {
        let n = |v: &[&str]| v.iter().map(|s| s.to_string()).collect::<Vec<_>>();
        assert!(san_matches(&n(&["brand.example"]), &n(&["brand.example"]), "default"));
        assert!(san_matches(&n(&["default._bimi.brand.example"]), &n(&["brand.example"]), "default"));
        assert!(!san_matches(&n(&["other._bimi.brand.example"]), &n(&["brand.example"]), "default"), "pins another selector");
        assert!(!san_matches(&n(&["sub.brand.example"]), &n(&["brand.example"]), "default"), "no wildcard or child match");
        assert!(san_matches(&n(&["brand.example"]), &n(&["mail.brand.example", "brand.example"]), "default"));
        assert!(!san_matches(&n(&[]), &n(&["brand.example"]), "default"));
    }

    #[test]
    fn logotype_extraction_is_bounded() {
        // an IA5String holding a data: URI of plain (not gzipped) base64 SVG
        let svg = b"<svg xmlns='http://www.w3.org/2000/svg'/>";
        let uri = format!("data:image/svg+xml;base64,{}", base64::engine::general_purpose::STANDARD.encode(svg));
        let mut der = vec![0x16, uri.len() as u8];
        der.extend(uri.as_bytes());
        let mut seq = vec![0x30, der.len() as u8];
        seq.extend(der);
        assert_eq!(logotype_svg(&seq).unwrap(), String::from_utf8(svg.to_vec()).unwrap());
        // a gzip bomb is cut off
        let mut gz = flate2::write::GzEncoder::new(vec![], flate2::Compression::best());
        std::io::Write::write_all(&mut gz, &vec![b'a'; 4 * MAX_SVG_BYTES]).unwrap();
        let bomb = format!("data:image/svg+xml;base64,{}", base64::engine::general_purpose::STANDARD.encode(gz.finish().unwrap()));
        let mut der = vec![0x16, 0x82, (bomb.len() >> 8) as u8, bomb.len() as u8];
        der.extend(bomb.as_bytes());
        let mut seq = vec![0x30, 0x82, (der.len() >> 8) as u8, der.len() as u8];
        seq.extend(der);
        assert_eq!(logotype_svg(&seq), None);
        assert_eq!(logotype_svg(&[0x30, 0x00]), None);
        assert_eq!(logotype_svg(b"garbage"), None);
    }

    // ---- the lookup, with a fake network

    #[derive(Default)]
    struct Fake { txt: HashMap<String, Vec<String>>, get: HashMap<String, Vec<u8>> }
    impl Net for Fake {
        fn txt(&self, name: &str) -> Result<Vec<String>, Temporary> { Ok(self.txt.get(name).cloned().unwrap_or_default()) }
        fn get(&self, url: &str, _max: usize, _http: bool) -> Result<Option<Vec<u8>>, Temporary> { Ok(self.get.get(url).cloned()) }
    }
    fn fake(dmarc: &str, bimi: &str, pem: Option<String>) -> Fake {
        let mut f = Fake::default();
        f.txt.insert("_dmarc.brand.example.test".into(), vec![dmarc.into()]);
        f.txt.insert("default._bimi.brand.example.test".into(), vec![bimi.into()]);
        if let Some(p) = pem { f.get.insert("https://brand.example.test/vmc.pem".into(), p.into_bytes()); }
        f
    }
    const BIMI_VMC: &str = "v=BIMI1; l=https://brand.example.test/logo.svg; a=https://brand.example.test/vmc.pem";

    #[test]
    fn lookup_gives_a_badge_only_for_a_verified_mark_under_an_enforced_policy() {
        let pem = chain(&[LEAF, INTER]);
        let go = |f: &Fake| lookup(f, "brand.example.test", &roots(), now()).unwrap();
        let verified = go(&fake("v=DMARC1; p=reject", BIMI_VMC, Some(pem.clone())));
        let Info::Verified { organization, issuer, mark_type, .. } = verified.clone() else { panic!("{verified:?}") };
        assert_eq!((organization.as_deref(), issuer.as_deref(), mark_type), (Some("Example Brand Inc"), Some("Test Mark CA"), MarkType::Verified));
        assert_eq!(description("brand.example.test", &verified).unwrap(), "Test Mark CA verified that this logo belongs to Example Brand Inc.");
        // the policy must be enforced
        assert_eq!(go(&fake("v=DMARC1; p=none", BIMI_VMC, Some(pem.clone()))), Info::None);
        assert_eq!(go(&fake("v=DMARC1; p=reject; pct=10", BIMI_VMC, Some(pem.clone()))), Info::None);
        // a logo only published in DNS earns no badge, whatever it is
        assert_eq!(go(&fake("v=DMARC1; p=reject", "v=BIMI1; l=https://brand.example.test/logo.svg;", None)), Info::SelfAsserted);
        assert!(description("brand.example.test", &Info::SelfAsserted).is_none());
        // an evidence document that does not validate fails BIMI outright (the logo next to it does not count)
        assert_eq!(go(&fake("v=DMARC1; p=reject", BIMI_VMC, Some(chain(&[LEAF_EVIL, EVIL_ROOT])))), Info::None);
        assert_eq!(go(&fake("v=DMARC1; p=reject", BIMI_VMC, None)), Info::None, "unreachable evidence");
        // a record that declines, a wildcard TXT, no record
        assert_eq!(go(&fake("v=DMARC1; p=reject", "v=BIMI1; l=; a=", Some(pem.clone()))), Info::None);
        assert_eq!(go(&fake("v=DMARC1; p=reject", "v=spf1 -all", Some(pem.clone()))), Info::None);
        assert_eq!(go(&Fake::default()), Info::None);
        // a name that is not a public host is never looked up
        assert_eq!(lookup(&Fake::default(), "localhost", &roots(), now()).unwrap(), Info::None);
    }

    #[test]
    fn lookup_falls_back_to_the_organisational_domain() {
        // mail.example.test has no records of its own: example.test's DMARC and BIMI records apply
        let mut f = Fake::default();
        f.txt.insert("_dmarc.example.test".into(), vec!["v=DMARC1; p=quarantine".into()]);
        f.txt.insert("default._bimi.example.test".into(), vec!["v=BIMI1; l=https://example.test/logo.svg".into()]);
        assert_eq!(lookup(&f, "mail.example.test", &roots(), now()).unwrap(), Info::SelfAsserted);
        // and an organisational domain at p=none stops a sub-domain that enforces
        f.txt.insert("_dmarc.mail.example.test".into(), vec!["v=DMARC1; p=reject".into()]);
        f.txt.insert("_dmarc.example.test".into(), vec!["v=DMARC1; p=none".into()]);
        assert_eq!(lookup(&f, "mail.example.test", &roots(), now()).unwrap(), Info::None);
    }

    #[test]
    fn the_answers_cache_is_bounded_and_keyed_by_domain() {
        // info() for a name that is not a public host answers without touching the network
        assert_eq!(info("localhost"), Info::None);
        assert_eq!(info("not a domain"), Info::None);
    }

    #[test]
    fn urls_from_dns_and_certificates_must_name_public_hosts() {
        use crate::avatars::safe_public_host;
        assert!(safe_public_host("https://brand.example/vmc.pem", false));
        assert!(!safe_public_host("http://brand.example/vmc.pem", false), "evidence is https only");
        assert!(safe_public_host("http://crl.ca.example/ca.crl", true), "a CRL address is http");
        for bad in ["https://10.0.0.5/x", "http://127.0.0.1/x.crl", "http://[::1]/x", "https://localhost/x", "http://nas.local/x", "https://a.example:8443/x", "http://u:p@a.example/x", "ftp://a.example/x", "http://169.254.169.254/x"] {
            assert!(!safe_public_host(bad, true), "{bad}");
        }
    }
}
