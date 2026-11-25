//! winmail.dat (TNEF, MS-OXTNEF) decoding (Bulwark lib/tnef.ts parseTnef and isTnefAttachment,
//! components/email/email-viewer.tsx "TNEF (winmail.dat) detection and processing"): the files
//! an Outlook sender attached inside application/ms-tnef, shown in the reader like normal
//! attachments. The input is untrusted: every read is bounds checked, counts that the data cannot
//! back are clamped, sizes are capped, and nothing here can panic on malformed bytes.
use crate::jmap::Client;
use serde_json::{json, Value};

/// The reader decodes a winmail.dat on its own only up to this size (Bulwark MAX_INLINE_PARSE_BYTES).
pub const MAX_PARSE_BYTES: usize = 5 * 1024 * 1024;
/// More attachments than this in one file is not a mail, it is an attack.
const MAX_ATTACHMENTS: usize = 200;
/// MAPI properties read from one attribute.
const MAX_PROPS: usize = 20_000;

const SIGNATURE: u32 = 0x223E_9F78;
const LVL_MESSAGE: u8 = 1;
const LVL_ATTACHMENT: u8 = 2;
const ATT_BODY: u32 = 0x0002_800C;
const ATT_MAPI_PROPS: u32 = 0x0006_9003;
const ATT_ATTACH_RENDDATA: u32 = 0x0006_9002;
const ATT_ATTACH_DATA: u32 = 0x0006_800F;
const ATT_ATTACH_TITLE: u32 = 0x0001_8010;
const ATT_ATTACHMENT: u32 = 0x0006_9005;

const PT_SHORT: u16 = 0x02;
const PT_LONG: u16 = 0x03;
const PT_FLOAT: u16 = 0x04;
const PT_DOUBLE: u16 = 0x05;
const PT_CURRENCY: u16 = 0x06;
const PT_APPTIME: u16 = 0x07;
const PT_ERROR: u16 = 0x0A;
const PT_BOOLEAN: u16 = 0x0B;
const PT_OBJECT: u16 = 0x0D;
const PT_I8: u16 = 0x14;
const PT_STRING8: u16 = 0x1E;
const PT_UNICODE: u16 = 0x1F;
const PT_SYSTIME: u16 = 0x40;
const PT_CLSID: u16 = 0x48;
const PT_BINARY: u16 = 0x102;
const MV_FLAG: u16 = 0x1000;

const PR_BODY: u16 = 0x1000;
const PR_BODY_HTML: u16 = 0x1013;
const PR_ATTACH_LONG_FILENAME: u16 = 0x3707;
const PR_ATTACH_MIME_TAG: u16 = 0x370E;
const PR_ATTACH_DATA_BIN: u16 = 0x3701;

/// One file found inside the winmail.dat.
#[derive(Debug, Clone, PartialEq)]
pub struct Attachment { pub name: String, pub mime: String, pub data: Vec<u8> }

#[derive(Debug, Default, PartialEq)]
pub struct Tnef { pub body: Option<String>, pub html: Option<String>, pub attachments: Vec<Attachment> }

/// Bulwark isTnefAttachment: a part named winmail.dat or typed application/(vnd.)ms-tnef.
pub fn is_tnef(name: &str, mime: &str) -> bool {
    let (n, t) = (name.to_ascii_lowercase(), mime.to_ascii_lowercase());
    n == "winmail.dat" || t == "application/ms-tnef" || t == "application/vnd.ms-tnef"
}

/// A forward-only reader whose every method checks what is left.
struct Rd<'a> { b: &'a [u8], at: usize }

impl<'a> Rd<'a> {
    fn left(&self) -> usize { self.b.len() - self.at }
    fn take(&mut self, n: usize) -> Option<&'a [u8]> {
        if n > self.left() { return None; }
        let s = &self.b[self.at..self.at + n];
        self.at += n;
        Some(s)
    }
    fn skip(&mut self, n: usize) -> Option<()> { self.take(n).map(|_| ()) }
    fn u8(&mut self) -> Option<u8> { self.take(1).map(|s| s[0]) }
    fn u16(&mut self) -> Option<u16> { self.take(2).map(|s| u16::from_le_bytes([s[0], s[1]])) }
    fn u32(&mut self) -> Option<u32> { self.take(4).map(|s| u32::from_le_bytes([s[0], s[1], s[2], s[3]])) }
}

fn pad4(n: usize) -> usize { (4 - n % 4) % 4 }

/// A MAPI property: the property id, its type and (for strings and binaries) the last value.
struct Prop<'a> { id: u16, ty: u16, value: &'a [u8] }

/// The size of a fixed-length value, or None for a type that is not one.
fn fixed_size(base: u16) -> Option<usize> {
    match base {
        PT_SHORT | PT_LONG | PT_FLOAT | PT_ERROR | PT_BOOLEAN => Some(4),
        PT_DOUBLE | PT_CURRENCY | PT_APPTIME | PT_I8 | PT_SYSTIME => Some(8),
        PT_CLSID => Some(16),
        _ => None,
    }
}

/// The MAPI properties of an attribute block. Reading stops (keeping what was read) at the first
/// thing that does not fit or is not understood; a count larger than the data can hold is clamped.
fn mapi_props(data: &[u8]) -> Vec<Prop<'_>> {
    let mut out = vec![];
    let mut r = Rd { b: data, at: 0 };
    let Some(count) = r.u32() else { return out };
    for _ in 0..(count as usize).min(MAX_PROPS) {
        if r.left() < 4 { break; }
        let (Some(ty), Some(id)) = (r.u16(), r.u16()) else { break };
        if id >= 0x8000 {
            // a named property: GUID, then an id or a name
            let named = (|| {
                r.skip(16)?;
                if r.u32()? == 0 { r.skip(4) } else { let n = r.u32()? as usize; r.skip(n.checked_add(pad4(n))?) }
            })();
            if named.is_none() { break; }
        }
        let base = ty & 0x0FFF;
        let multi = ty & MV_FLAG != 0;
        if matches!(base, PT_STRING8 | PT_UNICODE | PT_BINARY | PT_OBJECT) {
            let Some(n) = r.u32() else { break };
            // every value takes at least four bytes
            let n = (n as usize).min(r.left() / 4);
            let mut last: Option<&[u8]> = None;
            let mut ok = true;
            for _ in 0..n {
                let Some(len) = r.u32() else { ok = false; break };
                let len = len as usize;
                let Some(v) = r.take(len) else { ok = false; break };
                if r.skip(pad4(len)).is_none() { ok = false; }
                last = Some(v);
            }
            if let (false, Some(v), true) = (multi, last, base != PT_OBJECT) { out.push(Prop { id, ty, value: v }); }
            if !ok { break; }
        } else if let Some(size) = fixed_size(base) {
            let count = if multi { match r.u32() { Some(n) => (n as usize).min(r.left() / 4), None => break } } else { 1 };
            // a short value is padded to four bytes
            let step = size.max(4);
            let Some(total) = count.checked_mul(step) else { break };
            let Some(v) = r.take(total) else { break };
            if !multi { out.push(Prop { id, ty, value: &v[..size] }); }
        } else {
            break; // unknown type: its length is unknown, so is where the next property starts
        }
    }
    out
}

/// A MAPI string: UTF-16 for PT_UNICODE, else UTF-8 when it is valid and Windows-1252-like when not.
fn mapi_string(v: &[u8], ty: u16) -> String {
    if ty & 0x0FFF == PT_UNICODE {
        let mut n = v.len() & !1;
        while n >= 2 && v[n - 1] == 0 && v[n - 2] == 0 { n -= 2; }
        let units: Vec<u16> = v[..n].chunks_exact(2).map(|c| u16::from_le_bytes([c[0], c[1]])).collect();
        return String::from_utf16_lossy(&units);
    }
    ansi(v)
}

fn ansi(v: &[u8]) -> String {
    let v = v.strip_suffix(&[0]).unwrap_or(v);
    match std::str::from_utf8(v) {
        Ok(s) => s.to_string(),
        Err(_) => v.iter().map(|&b| b as char).collect(),
    }
}

/// Decode a winmail.dat. Anything that is not TNEF gives an empty result; a file cut off or
/// damaged gives what was read before the damage. Never panics.
pub fn parse(data: &[u8]) -> Tnef {
    let mut out = Tnef::default();
    let mut r = Rd { b: data, at: 0 };
    if r.u32() != Some(SIGNATURE) { return out; }
    if r.skip(2).is_none() { return out; } // legacy key
    // the attachment being assembled: name, mime, data
    type Cur = Option<(String, String, Option<Vec<u8>>)>;
    let mut cur: Cur = None;
    let flush = |cur: &mut Cur, out: &mut Tnef| {
        if let Some((name, mime, Some(data))) = cur.take() {
            if out.attachments.len() < MAX_ATTACHMENTS { out.attachments.push(Attachment { name, mime, data }); }
        }
    };
    while r.left() >= 11 {
        let (Some(level), Some(id), Some(len)) = (r.u8(), r.u32(), r.u32()) else { break };
        // the data and its two-byte checksum must both be there
        let Some(attr) = r.take(len as usize) else { break };
        if r.skip(2).is_none() { break; }
        match (level, id) {
            (LVL_MESSAGE, ATT_BODY) => out.body = Some(ansi(attr)),
            (LVL_MESSAGE, ATT_MAPI_PROPS) => {
                let props = mapi_props(attr);
                if let Some(p) = props.iter().find(|p| p.id == PR_BODY_HTML) { out.html = Some(mapi_string(p.value, p.ty)); }
                if out.body.is_none() { if let Some(p) = props.iter().find(|p| p.id == PR_BODY) { out.body = Some(mapi_string(p.value, p.ty)); } }
            }
            (LVL_ATTACHMENT, ATT_ATTACH_RENDDATA) => {
                flush(&mut cur, &mut out);
                cur = Some(("attachment".into(), "application/octet-stream".into(), None));
            }
            (LVL_ATTACHMENT, ATT_ATTACH_TITLE) => if let Some(c) = cur.as_mut() { c.0 = ansi(attr); },
            (LVL_ATTACHMENT, ATT_ATTACH_DATA) => if let Some(c) = cur.as_mut() { c.2 = Some(attr.to_vec()); },
            (LVL_ATTACHMENT, ATT_ATTACHMENT) => if let Some(c) = cur.as_mut() {
                for p in mapi_props(attr) {
                    match p.id {
                        PR_ATTACH_LONG_FILENAME => c.0 = mapi_string(p.value, p.ty),
                        PR_ATTACH_MIME_TAG => c.1 = mapi_string(p.value, p.ty),
                        PR_ATTACH_DATA_BIN => c.2 = Some(p.value.to_vec()),
                        _ => {}
                    }
                }
            },
            _ => {}
        }
    }
    flush(&mut cur, &mut out);
    out
}

// ------------------------------------------------------------------ in the reader

/// The identity of the message the reader shows: its id and blob id (an attached message read
/// through Email/parse has a blob id of its own and no id).
pub fn shown_key(m: &Value) -> String { format!("{}|{}", m["id"].as_str().unwrap_or(""), m["blobId"].as_str().unwrap_or("")) }

/// Whether a result computed for the message `then` may still be put on screen.
fn still_shown(then: &str, now: Option<&str>) -> bool { now == Some(then) }

/// Bulwark isHtmlBodyEffectivelyEmpty (email-viewer.tsx:474-484): Outlook writes an HTML body of
/// Word CSS and &nbsp; with no text.
pub fn html_effectively_empty(html: &str) -> bool {
    use std::sync::OnceLock;
    static RE: OnceLock<(regex::Regex, regex::Regex, regex::Regex)> = OnceLock::new();
    let (style, tag, nbsp) = RE.get_or_init(|| (regex::Regex::new(r"(?is)<style[^>]*>.*?</style>").unwrap(), regex::Regex::new(r"<[^>]+>").unwrap(), regex::Regex::new(r"(?i)&nbsp;").unwrap()));
    let t = style.replace_all(html, "");
    let t = tag.replace_all(&t, "");
    let t = t.replace("&#160;", " ");
    let t = nbsp.replace_all(&t, " ").into_owned();
    t.chars().all(char::is_whitespace)
}

/// Bulwark hasRealHtmlBody (email-viewer.tsx:2776-2779): the first htmlBody part of the message has
/// a value that is not effectively empty. (JMAP lets a text-only message list its text part there.)
pub fn has_real_html_body(m: &Value) -> bool {
    let Some(pid) = m["htmlBody"][0]["partId"].as_str() else { return false };
    let v = m["bodyValues"][pid]["value"].as_str().unwrap_or("").trim();
    !v.is_empty() && !html_effectively_empty(v)
}

/// What a winmail.dat shows as the message body: its HTML, else its text (Bulwark
/// email-viewer.tsx:2825-2830, 3270-3275); nothing when the outer message has a real HTML body.
#[derive(Debug, Clone, PartialEq)]
pub enum Shown { Html(String), Text(String) }

pub fn body_to_show(outer_has_real_html: bool, t: &Tnef) -> Option<Shown> {
    if outer_has_real_html { return None; }
    if let Some(h) = t.html.as_ref().filter(|h| !h.is_empty()) { return Some(Shown::Html(h.clone())); }
    t.body.as_ref().filter(|b| !b.is_empty()).map(|b| Shown::Text(b.clone()))
}

/// A listed attachment standing for file `index` inside the winmail.dat `parent` (a blob id).
fn synthetic(parent: &str, index: usize, a: &Attachment) -> Value {
    let name = if a.name.trim().is_empty() { "attachment" } else { a.name.as_str() };
    let mime = if a.mime.trim().is_empty() { "application/octet-stream" } else { a.mime.as_str() };
    json!({"name": name, "type": mime, "size": a.data.len(), "tnef": {"parent": parent, "index": index}})
}

/// The bytes of a listed attachment: a file inside a winmail.dat is decoded from it again (the
/// parent is capped, so this is cheap), anything else is downloaded.
pub fn fetch(c: &Client, a: &Value) -> Result<Vec<u8>, String> {
    let name = a["name"].as_str().unwrap_or("attachment");
    let mime = a["type"].as_str().unwrap_or("");
    let Some(t) = a.get("tnef") else { return c.download(a["blobId"].as_str().unwrap_or(""), name, mime) };
    let parent = c.download(t["parent"].as_str().unwrap_or(""), "winmail.dat", "application/ms-tnef")?;
    if parent.len() > MAX_PARSE_BYTES { return Err("The attachment is too large.".into()); }
    let idx = t["index"].as_u64().unwrap_or(u64::MAX) as usize;
    parse(&parent).attachments.into_iter().nth(idx).map(|f| f.data).ok_or_else(|| "The attachment is no longer in the winmail.dat.".into())
}

/// After a message is shown: decode its winmail.dat (when small enough) and list the files in it
/// after the others. The winmail.dat itself is dropped from the list once it yielded files
/// (Bulwark hides the chip then). Runs in the background; a message opened meanwhile is left alone.
pub fn expand(u: &crate::AppWindow, c: std::sync::Arc<Client>, id: &str, atts: &[Value]) {
    use slint::ComponentHandle;
    // which message these attachments belong to: the outer message and an attached .eml opened
    // from it share the open mail id, so the identity is the shown message's own id and blob id
    let Some(shown) = crate::CUR_MSG.with(|x| x.borrow().as_ref().map(|(m, _)| shown_key(m))) else { return };
    let real_html = crate::CUR_MSG.with(|x| x.borrow().as_ref().is_some_and(|(m, _)| has_real_html_body(m)));
    let found: Vec<(usize, String)> = atts.iter().enumerate()
        .filter(|(_, a)| a.get("tnef").is_none() && is_tnef(a["name"].as_str().unwrap_or(""), a["type"].as_str().unwrap_or("")))
        .filter(|(_, a)| a["size"].as_u64().unwrap_or(0) as usize <= MAX_PARSE_BYTES)
        .filter_map(|(i, a)| Some((i, a["blobId"].as_str().filter(|b| !b.is_empty())?.to_string())))
        .collect();
    if found.is_empty() { return; }
    let (w, id, atts) = (u.as_weak(), id.to_string(), atts.to_vec());
    std::thread::spawn(move || {
        let mut all: Vec<Value> = vec![];
        let mut hide = std::collections::HashSet::new();
        let mut shown_body: Option<Shown> = None;
        for (i, blob) in &found {
            let Ok(bytes) = c.download(blob, "winmail.dat", "application/ms-tnef") else { continue };
            if bytes.len() > MAX_PARSE_BYTES { continue; }
            let t = parse(&bytes);
            if shown_body.is_none() { shown_body = body_to_show(real_html, &t); }
            if t.attachments.is_empty() { continue; }
            hide.insert(*i);
            all.extend(t.attachments.iter().enumerate().map(|(n, a)| synthetic(blob, n, a)));
        }
        if all.is_empty() && shown_body.is_none() { return; }
        let all_empty = all.is_empty();
        let list: Vec<Value> = atts.iter().enumerate().filter(|(i, _)| !hide.contains(i)).map(|(_, a)| a.clone()).chain(all).collect();
        let _ = w.upgrade_in_event_loop(move |u| {
            // another message (or the attached one, or the outer one after Back) is shown now
            let now = crate::CUR_MSG.with(|x| x.borrow().as_ref().map(|(m, _)| shown_key(m)));
            if !still_shown(&shown, now.as_deref()) { return; }
            if let Some(b) = shown_body { show_body(&u, b); }
            if all_empty { return; }
            let rows: Vec<crate::Att> = list.iter().enumerate().map(|(i, a)| crate::Att {
                name: a["name"].as_str().unwrap_or("attachment").into(), size: crate::human_size(a["size"].as_u64().unwrap_or(0)).into(), index: i as i32,
            }).collect();
            u.set_attachments(slint::ModelRc::new(slint::VecModel::from(rows)));
            crate::attachments::show(&u, c, &id, &list);
            crate::CUR_MSG.with(|x| if let Some((_, a)) = x.borrow_mut().as_mut() { *a = list; });
        });
    });
}

/// Whether the body on screen is no real body, so the winmail.dat's may replace it. Under RFC 8621
/// a message with no text/plain lists its HTML part in textBody too, so the text of an Outlook
/// boilerplate message is that boilerplate: both are tested the same way. A "View source" page has
/// content and is left alone.
fn body_replaceable(html: Option<&str>, text: &str) -> bool {
    html.map_or(true, html_effectively_empty) && html_effectively_empty(text)
}

/// Put the winmail.dat's body on screen through the normal render path (sanitiser, remote-content
/// policy, links-off for phishing). Only replaces a body that is empty: the message's own, not a
/// "View source" page.
fn show_body(u: &crate::AppWindow, b: Shown) {
    let replaced = crate::CUR.with(|c| {
        let mut c = c.borrow_mut();
        let Some(cur) = c.as_mut() else { return false };
        if !body_replaceable(cur.html.as_deref(), &cur.text) { return false; }
        match b {
            Shown::Html(h) => { cur.remote = crate::has_remote_images(&h); cur.html = Some(h); cur.sanitized = None; }
            Shown::Text(t) => { cur.remote = false; cur.html = None; cur.text = t; cur.sanitized = None; }
        }
        true
    });
    if replaced { crate::render_current(u); }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn attr(level: u8, id: u32, data: &[u8]) -> Vec<u8> {
        let mut v = vec![level];
        v.extend(id.to_le_bytes());
        v.extend((data.len() as u32).to_le_bytes());
        v.extend(data);
        v.extend([0, 0]);
        v
    }
    fn header() -> Vec<u8> { let mut v = SIGNATURE.to_le_bytes().to_vec(); v.extend([1, 0]); v }
    fn prop_bin(ty: u16, id: u16, val: &[u8]) -> Vec<u8> {
        let mut v = vec![];
        v.extend(ty.to_le_bytes());
        v.extend(id.to_le_bytes());
        v.extend(1u32.to_le_bytes());
        v.extend((val.len() as u32).to_le_bytes());
        v.extend(val);
        v.extend(vec![0; pad4(val.len())]);
        v
    }
    fn props(list: Vec<Vec<u8>>) -> Vec<u8> {
        let mut v = (list.len() as u32).to_le_bytes().to_vec();
        for p in list { v.extend(p); }
        v
    }
    fn utf16(s: &str) -> Vec<u8> { s.encode_utf16().chain([0]).flat_map(|u| u.to_le_bytes()).collect() }

    fn sample() -> Vec<u8> {
        let mut f = header();
        f.extend(attr(LVL_MESSAGE, ATT_BODY, b"Hello\0"));
        f.extend(attr(LVL_MESSAGE, ATT_MAPI_PROPS, &props(vec![prop_bin(PT_UNICODE, PR_BODY_HTML, &utf16("<p>Hi</p>"))])));
        f.extend(attr(LVL_ATTACHMENT, ATT_ATTACH_RENDDATA, &[0; 14]));
        f.extend(attr(LVL_ATTACHMENT, ATT_ATTACH_TITLE, b"REPORT~1.TXT\0"));
        f.extend(attr(LVL_ATTACHMENT, ATT_ATTACH_DATA, b"report bytes"));
        f.extend(attr(LVL_ATTACHMENT, ATT_ATTACHMENT, &props(vec![
            prop_bin(PT_UNICODE, PR_ATTACH_LONG_FILENAME, &utf16("Quarterly report.txt")),
            prop_bin(PT_STRING8, PR_ATTACH_MIME_TAG, b"text/plain\0"),
        ])));
        f.extend(attr(LVL_ATTACHMENT, ATT_ATTACH_RENDDATA, &[0; 14]));
        f.extend(attr(LVL_ATTACHMENT, ATT_ATTACHMENT, &props(vec![
            prop_bin(PT_UNICODE, PR_ATTACH_LONG_FILENAME, &utf16("b.bin")),
            prop_bin(PT_BINARY, PR_ATTACH_DATA_BIN, &[1, 2, 3]),
        ])));
        f
    }

    #[test]
    fn decodes_body_and_attachments() {
        let t = parse(&sample());
        assert_eq!(t.body.as_deref(), Some("Hello"));
        assert_eq!(t.html.as_deref(), Some("<p>Hi</p>"));
        assert_eq!(t.attachments.len(), 2);
        assert_eq!(t.attachments[0], Attachment { name: "Quarterly report.txt".into(), mime: "text/plain".into(), data: b"report bytes".to_vec() });
        assert_eq!(t.attachments[1], Attachment { name: "b.bin".into(), mime: "application/octet-stream".into(), data: vec![1, 2, 3] });
    }

    #[test]
    fn a_result_is_dropped_when_another_message_is_shown() {
        let outer = json!({"id": "m1", "blobId": "bo"});
        let inner = json!({"blobId": "bi"});
        let other = json!({"id": "m2", "blobId": "bx"});
        let (k_outer, k_inner) = (shown_key(&outer), shown_key(&inner));
        assert!(still_shown(&k_outer, Some(&shown_key(&outer))));
        // the attached .eml is open: the outer message's winmail result must not land on it
        assert!(!still_shown(&k_outer, Some(&k_inner)));
        // and the inner message's result must not land on the outer one after Back
        assert!(!still_shown(&k_inner, Some(&k_outer)));
        assert!(!still_shown(&k_outer, Some(&shown_key(&other))));
        assert!(!still_shown(&k_outer, None));
    }

    #[test]
    fn detects_tnef_parts() {
        assert!(is_tnef("WINMAIL.DAT", ""));
        assert!(is_tnef("x", "application/ms-tnef"));
        assert!(is_tnef("x", "Application/Vnd.Ms-Tnef"));
        assert!(!is_tnef("a.dat", "application/octet-stream"));
    }

    #[test]
    fn not_tnef_is_empty() {
        assert_eq!(parse(b""), Tnef::default());
        assert_eq!(parse(b"hello world, not tnef"), Tnef::default());
    }

    #[test]
    fn every_truncation_is_safe() {
        let f = sample();
        for n in 0..f.len() { let _ = parse(&f[..n]); }
        // a cut inside the second attachment keeps the first
        assert_eq!(parse(&f[..f.len() - 10]).attachments.len(), 1);
    }

    #[test]
    fn every_corrupted_byte_is_safe() {
        let f = sample();
        for i in 0..f.len() {
            for v in [0u8, 0xFF, 0x80, 0x7F] {
                let mut g = f.clone();
                g[i] = v;
                let _ = parse(&g);
            }
        }
    }

    #[test]
    fn pseudo_random_garbage_is_safe() {
        let mut x: u32 = 12345;
        for round in 0..300 {
            let mut g = header();
            for _ in 0..20 + round % 200 { x = x.wrapping_mul(1664525).wrapping_add(1013904223); g.push((x >> 24) as u8); }
            let _ = parse(&g);
        }
    }

    #[test]
    fn huge_lengths_and_counts_do_not_allocate_or_loop() {
        // an attribute that claims 4 GB
        let mut f = header();
        f.push(LVL_ATTACHMENT);
        f.extend(ATT_ATTACH_DATA.to_le_bytes());
        f.extend(u32::MAX.to_le_bytes());
        f.extend([0; 8]);
        assert!(parse(&f).attachments.is_empty());
        // a MAPI block that claims 4 billion properties, one a multi-value of 4 billion values
        let mut p = u32::MAX.to_le_bytes().to_vec();
        p.extend((PT_LONG | MV_FLAG).to_le_bytes());
        p.extend(PR_BODY.to_le_bytes());
        p.extend(u32::MAX.to_le_bytes());
        p.extend([0; 16]);
        let mut f = header();
        f.extend(attr(LVL_MESSAGE, ATT_MAPI_PROPS, &p));
        assert_eq!(parse(&f).html, None);
        // a variable value whose length runs past the block
        let mut p = 1u32.to_le_bytes().to_vec();
        p.extend(PT_BINARY.to_le_bytes());
        p.extend(PR_ATTACH_DATA_BIN.to_le_bytes());
        p.extend(1u32.to_le_bytes());
        p.extend(u32::MAX.to_le_bytes());
        assert!(mapi_props(&p).is_empty());
        // a property name longer than the data (its padding would overflow on 32-bit)
        let mut p = 1u32.to_le_bytes().to_vec();
        p.extend(PT_LONG.to_le_bytes());
        p.extend(0x8000u16.to_le_bytes());
        p.extend([0; 16]);
        p.extend(1u32.to_le_bytes());
        p.extend(u32::MAX.to_le_bytes());
        assert!(mapi_props(&p).is_empty());
    }

    #[test]
    fn caps_the_attachment_count() {
        let mut f = header();
        for _ in 0..MAX_ATTACHMENTS + 50 {
            f.extend(attr(LVL_ATTACHMENT, ATT_ATTACH_RENDDATA, &[0; 14]));
            f.extend(attr(LVL_ATTACHMENT, ATT_ATTACH_DATA, b"x"));
        }
        assert_eq!(parse(&f).attachments.len(), MAX_ATTACHMENTS);
    }

    #[test]
    fn ansi_names_fall_back_to_single_byte() {
        assert_eq!(ansi(b"caf\xe9\0"), "caf\u{e9}");
        assert_eq!(ansi("café".as_bytes()), "café");
    }

    #[test]
    fn odd_length_utf16_is_safe() {
        assert_eq!(mapi_string(&[0x41, 0x00, 0x42], PT_UNICODE), "A");
    }

    #[test]
    fn tnef_body_is_shown_only_without_a_real_html_body() {
        let t = Tnef { body: Some("plain".into()), html: Some("<p>hi</p>".into()), attachments: vec![] };
        assert_eq!(body_to_show(false, &t), Some(Shown::Html("<p>hi</p>".into())));
        assert_eq!(body_to_show(true, &t), None);
        let t = Tnef { body: Some("plain".into()), html: None, attachments: vec![] };
        assert_eq!(body_to_show(false, &t), Some(Shown::Text("plain".into())));
        assert_eq!(body_to_show(false, &Tnef::default()), None);
        let t = Tnef { body: Some("plain".into()), html: Some(String::new()), attachments: vec![] };
        assert_eq!(body_to_show(false, &t), Some(Shown::Text("plain".into())));
    }

    #[test]
    fn word_boilerplate_is_an_empty_html_body() {
        assert!(html_effectively_empty("<html><style>p{x:y}</style><body><p>&nbsp;</p>&#160; 
</body></html>"));
        assert!(!html_effectively_empty("<p>Hello</p>"));
        let m = |v: &str| json!({"htmlBody": [{"partId": "1"}], "bodyValues": {"1": {"value": v}}});
        assert!(!has_real_html_body(&m("<style>a{}</style>&nbsp;")));
        assert!(!has_real_html_body(&m("  ")));
        assert!(has_real_html_body(&m("<p>x</p>")));
        assert!(!has_real_html_body(&json!({})));
    }

    #[test]
    fn only_an_empty_body_is_replaced() {
        let word = "<html><style>p{x:y}</style><body><p>&nbsp;</p></body></html>";
        // the forwarded HTML-only Outlook message: its text is the same boilerplate
        assert!(body_replaceable(Some(word), word));
        assert!(body_replaceable(None, ""));
        assert!(body_replaceable(None, "  
"));
        // a real body, or the View source page, stays
        assert!(!body_replaceable(Some("<p>Hello</p>"), "Hello"));
        assert!(!body_replaceable(None, "Hello"));
        assert!(!body_replaceable(Some("<pre>Subject: x</pre>"), "Subject: x"));
    }
}
