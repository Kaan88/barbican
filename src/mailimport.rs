//! Import .eml and .mbox files into a folder (Bulwark mail-transfer/mail-import-dialog and
//! lib/mail-import: mbox.ts, mbox-status.ts, engine.ts): each message is uploaded as a blob
//! and filed with Email/import (RFC 8621 §4.8), keeping its read / answered / flagged state
//! from the mbox status headers and its arrival time from its Date.
use crate::jmap::Client;
use serde_json::{json, Map, Value};

/// One message to import: its bytes, keywords and arrival time (RFC 3339).
#[derive(Debug, Clone, PartialEq)]
pub struct Item { pub bytes: Vec<u8>, pub keywords: Vec<String>, pub received_at: Option<String> }

/// An .eml file larger than this is not read at all (no server takes a single message this big,
/// and it would otherwise be held in memory whole).
const MAX_EML: u64 = 200_000_000;

/// Streaming mbox splitter: feed it lines, it hands back each message as soon as the next
/// separator shows up, so a multi-GB mbox never sits in memory whole.
#[derive(Default)]
pub struct MboxSplitter { cur: Option<Vec<u8>>, pending_empty: Option<Vec<u8>>, cur_from: Vec<u8>, done_from: Vec<u8> }

impl MboxSplitter {
    /// One line (with its line ending); returns a finished message when `l` starts the next.
    pub fn push(&mut self, l: &[u8]) -> Option<Vec<u8>> {
        let separator = l.starts_with(b"From ") && (self.cur.is_none() || self.pending_empty.is_some()) && is_envelope_line(l);
        if separator {
            self.pending_empty = None;
            self.done_from = std::mem::replace(&mut self.cur_from, strip_eol(l).to_vec());
            let done = self.cur.replace(vec![]);
            return done.filter(|m| !blank(m));
        }
        let m = self.cur.as_mut()?;
        if let Some(e) = self.pending_empty.take() { m.extend_from_slice(&e); }
        if is_empty_line(l) { self.pending_empty = Some(l.to_vec()); return None; }
        let quotes = l.iter().take_while(|b| **b == b'>').count();
        if quotes > 0 && l[quotes..].starts_with(b"From ") { m.extend_from_slice(&l[1..]); } else { m.extend_from_slice(l); }
        None
    }

    /// The last message, at the end of the file.
    pub fn finish(&mut self) -> Option<Vec<u8>> {
        self.pending_empty = None;
        self.done_from = std::mem::take(&mut self.cur_from);
        self.cur.take().filter(|m| !blank(m))
    }

    /// The envelope line (without its line ending) of the message `push` or `finish` returned last.
    pub fn last_from(&self) -> &[u8] { &self.done_from }
}

fn blank(m: &[u8]) -> bool { m.iter().all(|b| b.is_ascii_whitespace()) }

#[cfg(test)]
fn lines(bytes: &[u8]) -> Vec<&[u8]> {
    let mut out = vec![];
    let mut start = 0;
    for (i, b) in bytes.iter().enumerate() {
        if *b == b'\n' { out.push(&bytes[start..=i]); start = i + 1; }
    }
    if start < bytes.len() { out.push(&bytes[start..]); }
    out
}

fn is_empty_line(l: &[u8]) -> bool { l == b"\n" || l == b"\r\n" }

fn strip_eol(l: &[u8]) -> &[u8] {
    let mut e = l.len();
    if e > 0 && l[e - 1] == b'\n' { e -= 1; }
    if e > 0 && l[e - 1] == b'\r' { e -= 1; }
    &l[..e]
}

/// "From sender Tue May  5 10:11:12 2020" (optionally a zone before the year, or "remote from
/// host" after it): an envelope line, not a body line "From now on…" (Bulwark isEnvelopeLine).
pub fn is_envelope_line(l: &[u8]) -> bool {
    let t = String::from_utf8_lossy(strip_eol(l)).to_string();
    let Some(rest) = t.strip_prefix("From ") else { return false };
    let mut words: Vec<&str> = rest.split_whitespace().collect();
    if words.len() < 3 { return false; }
    if words.len() >= 3 && words[words.len() - 3] == "remote" && words[words.len() - 2] == "from" { words.truncate(words.len() - 3); }
    let Some(year) = words.last() else { return false };
    if year.len() != 4 || !year.chars().all(|c| c.is_ascii_digit()) { return false; }
    // a time hh:mm(:ss) among the words after the sender
    words[1..].iter().any(|w| {
        let p: Vec<&str> = w.split(':').collect();
        (p.len() == 2 || p.len() == 3) && p[0].len() <= 2 && p.iter().all(|x| !x.is_empty() && x.chars().all(|c| c.is_ascii_digit()))
    })
}

/// Split an mbox file into messages (mboxo / mboxrd): a separator is a `From ` envelope line at
/// the start or after an empty line; that empty line belongs to the format and is dropped, and
/// `>From ` lines lose one `>`. Bytes are otherwise kept as they are.
#[cfg(test)]
pub fn split_mbox(bytes: &[u8]) -> Vec<Vec<u8>> {
    let mut sp = MboxSplitter::default();
    let mut out: Vec<Vec<u8>> = lines(bytes).into_iter().filter_map(|l| sp.push(l)).collect();
    out.extend(sp.finish());
    out
}

/// The header block of a message (up to the first empty line), unfolded, by lowercased name.
pub fn header_fields(bytes: &[u8]) -> Vec<(String, String)> {
    let text = String::from_utf8_lossy(bytes);
    let head = text.split("\r\n\r\n").next().unwrap_or("").split("\n\n").next().unwrap_or("").to_string();
    let mut out: Vec<(String, String)> = vec![];
    for line in head.replace("\r\n", "\n").split('\n') {
        if line.starts_with(' ') || line.starts_with('\t') {
            if let Some(last) = out.last_mut() { last.1.push(' '); last.1.push_str(line.trim()); }
            continue;
        }
        if let Some((n, v)) = line.split_once(':') {
            if !n.trim().is_empty() && !n.contains(' ') { out.push((n.trim().to_lowercase(), v.trim().to_string())); }
        }
    }
    out
}

fn field<'a>(f: &'a [(String, String)], name: &str) -> Option<&'a str> { f.iter().find(|(n, _)| n == name).map(|(_, v)| v.as_str()) }

/// Keywords from the status headers mail programs write into mbox files, and whether the
/// message was deleted (Thunderbird's expunged flag) (Bulwark mboxState). With none of them it
/// is imported as read.
pub fn mbox_state(f: &[(String, String)]) -> (Vec<String>, bool) {
    let (status, xstatus, moz, gmail) = (field(f, "status"), field(f, "x-status"), field(f, "x-mozilla-status"), field(f, "x-gmail-labels"));
    let mut kw: Vec<String> = vec![];
    let mut add = |k: &str| { let k = k.to_lowercase(); if !kw.contains(&k) { kw.push(k); } };
    let mut expunged = false;
    if status.is_none() && xstatus.is_none() && moz.is_none() && gmail.is_none() { add("$seen"); }
    if status.is_some_and(|s| s.to_uppercase().contains('R')) { add("$seen"); }
    if let Some(x) = xstatus.map(str::to_uppercase) {
        if x.contains('A') { add("$answered"); }
        if x.contains('F') { add("$flagged"); }
        if x.contains('T') { add("$draft"); }
    }
    if let Some(flags) = moz.filter(|m| m.len() <= 8).and_then(|m| u32::from_str_radix(m, 16).ok()) {
        if flags & 0x1 != 0 { add("$seen"); }
        if flags & 0x2 != 0 { add("$answered"); }
        if flags & 0x4 != 0 { add("$flagged"); }
        if flags & 0x8 != 0 { expunged = true; }
        if flags & 0x1000 != 0 { add("$forwarded"); }
    }
    if let Some(g) = gmail {
        let labels: Vec<String> = g.split(',').map(|l| l.trim().to_lowercase()).collect();
        if !labels.iter().any(|l| l == "unread") { add("$seen"); }
        if labels.iter().any(|l| l == "starred") { add("$flagged"); }
    }
    for v in [field(f, "x-keywords"), field(f, "x-mozilla-keys")].into_iter().flatten() {
        for k in v.split(|c: char| c.is_whitespace() || c == ',').filter(|k| !k.is_empty()) {
            let k = match k.to_lowercase().as_str() { "\\seen" => "$seen".to_string(), "\\flagged" => "$flagged".into(), "\\answered" => "$answered".into(), "\\draft" => "$draft".into(), o => o.to_string() };
            if crate::jmap::valid_keyword(&k) && !["$recent", "\\recent", "$hasattachment", "$hasnoattachment"].contains(&k.as_str()) { add(&k); }
        }
    }
    (kw, expunged)
}

/// The arrival time from the Date header (RFC 5322), as RFC 3339 UTC.
pub fn received_at(f: &[(String, String)]) -> Option<String> {
    let d = field(f, "date")?;
    // drop a trailing comment such as "(UTC)"
    let d = d.split(" (").next().unwrap_or(d).trim();
    chrono::DateTime::parse_from_rfc2822(d).ok().map(|t| t.with_timezone(&chrono::Utc).format("%Y-%m-%dT%H:%M:%SZ").to_string())
}

/// A date between 1980 and a day from now, else None (Bulwark `plausible`).
fn plausible(t: Option<chrono::DateTime<chrono::Utc>>) -> Option<chrono::DateTime<chrono::Utc>> {
    let t = t?;
    let min = chrono::NaiveDate::from_ymd_opt(1980, 1, 1)?.and_hms_opt(0, 0, 0)?.and_utc();
    (t >= min && t <= chrono::Utc::now() + chrono::Duration::days(1)).then_some(t)
}

/// The date of an envelope line, "From sender Tue May  5 10:11:12 2020" (read as UTC, with an
/// optional zone before the year) (Bulwark `envelopeDate`).
pub fn envelope_date(from_line: &str) -> Option<chrono::DateTime<chrono::Utc>> {
    use std::sync::OnceLock;
    static RE: OnceLock<regex::Regex> = OnceLock::new();
    let re = RE.get_or_init(|| regex::Regex::new(r"(?i)(?:^|\s)(?:mon|tue|wed|thu|fri|sat|sun)\w*\s+([a-z]{3})\w*\s+(\d{1,2})\s+(\d{1,2}):(\d{2})(?::(\d{2}))?\s+(?:([+-]\d{4})\s+)?(\d{4})").unwrap());
    let c = re.captures(from_line)?;
    const MONTHS: [&str; 12] = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];
    let month = MONTHS.iter().position(|m| *m == c[1].to_lowercase())? as u32 + 1;
    let num = |i: usize| c.get(i).map(|m| m.as_str().parse::<u32>().unwrap_or(0)).unwrap_or(0);
    let t = chrono::NaiveDate::from_ymd_opt(c[7].parse().ok()?, month, num(2))?.and_hms_opt(num(3), num(4), num(5))?.and_utc();
    let off = match c.get(6) {
        Some(z) => {
            let z = z.as_str();
            let mins = z[1..3].parse::<i64>().ok()? * 60 + z[3..].parse::<i64>().ok()?;
            if z.starts_with('-') { -mins } else { mins }
        }
        None => 0,
    };
    Some(t - chrono::Duration::minutes(off))
}

/// When an mbox message arrived (Bulwark `arrivalTime`): the envelope line is trusted where it is
/// the arrival time for sure, Barbican's and Bulwark's own export and lines with a time zone
/// (Google Takeout); elsewhere it may be when the message was copied into the folder
/// (Thunderbird's `From - <date>`, local time), so the Date header comes first and a plausible
/// envelope date is the fallback.
pub fn arrival_time(from_line: &str, f: &[(String, String)]) -> Option<String> {
    use std::sync::OnceLock;
    static ZONE: OnceLock<regex::Regex> = OnceLock::new();
    let zone = ZONE.get_or_init(|| regex::Regex::new(r"\s[+-]\d{4}\s+\d{4}\s*$").unwrap());
    let fmt = |t: chrono::DateTime<chrono::Utc>| t.format("%Y-%m-%dT%H:%M:%SZ").to_string();
    let envelope = plausible(envelope_date(from_line)).map(fmt);
    let trusted = from_line.starts_with("From barbican-export ") || from_line.starts_with("From bulwark-export ") || zone.is_match(from_line);
    if trusted { envelope.or_else(|| received_at(f)) } else { received_at(f).or(envelope) }
}

/// The headers the mbox state is read from (and the export writes); dropped on import.
const STATE_HEADERS: [&str; 6] = ["status", "x-status", "x-keywords", "x-mozilla-status", "x-mozilla-status2", "x-mozilla-keys"];

/// The message without the mbox state headers (their state is now in keywords, and a stale copy
/// would contradict it) (Bulwark `withoutStateHeaders`). Everything else stays byte for byte.
pub fn without_state_headers(m: Vec<u8>) -> Vec<u8> {
    let mut out = Vec::with_capacity(m.len());
    let (mut in_headers, mut skipping, mut found) = (true, false, false);
    for line in m.split_inclusive(|b| *b == b'\n') {
        if in_headers {
            if strip_eol(line).is_empty() { in_headers = false; }
            else if line[0] == b' ' || line[0] == b'\t' { if skipping { continue; } }
            else {
                let name = line.iter().position(|b| *b == b':').map(|c| String::from_utf8_lossy(&line[..c]).trim().to_ascii_lowercase()).unwrap_or_default();
                skipping = STATE_HEADERS.contains(&name.as_str());
                if skipping { found = true; continue; }
            }
        }
        out.extend_from_slice(line);
    }
    if found { out } else { m }
}

/// `from_line` is the mbox envelope line, empty for an .eml file (whose state headers are kept).
fn to_item(m: Vec<u8>, from_line: Option<&[u8]>) -> Option<Item> {
    let f = header_fields(&m);
    let (keywords, expunged) = mbox_state(&f);
    if expunged { return None; }
    match from_line {
        Some(l) => Some(Item { received_at: arrival_time(&String::from_utf8_lossy(l), &f), keywords, bytes: without_state_headers(m) }),
        None => Some(Item { received_at: received_at(&f), keywords, bytes: m }),
    }
}

fn looks_mbox(name: &str, first_line: &[u8]) -> bool {
    let lower = name.to_lowercase();
    lower.ends_with(".mbox") || lower.ends_with(".mbx") || lower.ends_with(".mbs") || first_line.starts_with(b"From ") && is_envelope_line(first_line)
}

/// The messages of one file: an .mbox (or anything starting with an envelope line) is split,
/// anything else is one message. Deleted (expunged) messages are left out.
#[cfg(test)]
pub fn items_of(name: &str, bytes: &[u8]) -> Vec<Item> {
    let mut r = Reader::new(name, std::io::Cursor::new(bytes.to_vec()), u64::MAX);
    std::iter::from_fn(|| r.next_item()).collect()
}

/// Reads the messages of one file one at a time (Bulwark streams mbox files in chunks too).
pub struct Reader<R: std::io::BufRead> {
    src: R,
    mbox: Option<MboxSplitter>,
    first: Option<Vec<u8>>,
    done: bool,
    eml_cap: u64,
    /// bytes read so far, for progress
    pub read: u64,
    /// an .eml over the cap was skipped
    pub too_big: bool,
    /// the envelope line of the mbox message `next_raw` returned last
    from_line: Vec<u8>,
}

impl<R: std::io::BufRead> Reader<R> {
    pub fn new(name: &str, mut src: R, eml_cap: u64) -> Self {
        let mut first = vec![];
        let ok = src.read_until(b'\n', &mut first).is_ok();
        let mbox = (ok && looks_mbox(name, &first)).then(MboxSplitter::default);
        Reader { read: first.len() as u64, src, mbox, first: Some(first), done: !ok, eml_cap, too_big: false, from_line: vec![] }
    }

    fn next_raw(&mut self) -> Option<Vec<u8>> {
        if self.done { return None; }
        match self.mbox.as_mut() {
            Some(sp) => {
                if let Some(f) = self.first.take() { if let Some(m) = sp.push(&f) { self.from_line = sp.last_from().to_vec(); return Some(m); } }
                let mut l = vec![];
                loop {
                    l.clear();
                    match self.src.read_until(b'\n', &mut l) {
                        Ok(0) | Err(_) => { self.done = true; let m = sp.finish(); self.from_line = sp.last_from().to_vec(); return m; }
                        Ok(n) => { self.read += n as u64; if let Some(m) = sp.push(&l) { self.from_line = sp.last_from().to_vec(); return Some(m); } }
                    }
                }
            }
            None => {
                self.done = true;
                let mut m = self.first.take().unwrap_or_default();
                let cap = self.eml_cap.saturating_sub(m.len() as u64);
                let mut lim = std::io::Read::take(&mut self.src, cap.saturating_add(1));
                let n = std::io::Read::read_to_end(&mut lim, &mut m).ok()? as u64;
                self.read += n;
                if n > cap { self.too_big = true; return None; }
                (!blank(&m)).then_some(m)
            }
        }
    }

    /// The next message to import, skipping deleted ones.
    pub fn next_item(&mut self) -> Option<Item> {
        loop {
            let m = self.next_raw()?;
            let from = self.mbox.is_some().then(|| self.from_line.clone());
            if let Some(it) = to_item(m, from.as_deref()) { return Some(it); }
        }
    }
}

/// The Email/import entry of an uploaded message.
pub fn import_entry(blob: &str, mailbox: &str, it: &Item) -> Value {
    let mut kw: Map<String, Value> = it.keywords.iter().map(|k| (k.clone(), json!(true))).collect();
    // from a file: the server never checked it (Bulwark lib/mail-import/sources.ts:118-122), so
    // its Authentication-Results are not believed (auth::Origin)
    kw.insert("$imported".into(), json!(true));
    let mut e = json!({"blobId": blob, "mailboxIds": { mailbox: true }, "keywords": kw});
    if let Some(r) = &it.received_at { e["receivedAt"] = json!(r); }
    e
}

/// What `import` needs from the server (a trait so the request loop can be tested).
pub trait Server {
    fn account(&self) -> String;
    fn max_set(&self) -> usize;
    fn max_upload(&self) -> usize;
    fn upload(&self, data: Vec<u8>) -> Result<String, String>;
    fn call(&self, calls: Value) -> Result<Vec<Value>, String>;
    /// The Message-IDs of the messages already in a folder.
    fn message_ids_in(&self, mailbox: &str) -> std::collections::HashSet<String>;
}

impl Server for Client {
    fn account(&self) -> String { self.account.clone() }
    fn max_set(&self) -> usize { Client::max_set(self) }
    fn max_upload(&self) -> usize { Client::max_upload(self) }
    fn upload(&self, data: Vec<u8>) -> Result<String, String> { Client::upload(self, data, "message/rfc822").map(|r| r.0) }
    fn call(&self, calls: Value) -> Result<Vec<Value>, String> { Client::call(self, calls) }
    /// inMailbox comes from the server's message cache, not its search index (which a
    /// Message-ID header filter needs and which lags behind a fresh import), so this is
    /// exact: every id in the folder, then their messageId (get_all chunks by the limits).
    fn message_ids_in(&self, mailbox: &str) -> std::collections::HashSet<String> {
        let acc = self.account.clone();
        let Ok(ids) = self.query_all("Email/query", &acc, json!({"filter": {"inMailbox": mailbox}})) else { return Default::default() };
        let Ok(list) = self.get_all("Email/get", &acc, &ids, Some(&json!(["messageId"]))) else { return Default::default() };
        list.iter().flat_map(|e| e["messageId"].as_array().cloned().unwrap_or_default()).filter_map(|v| v.as_str().map(str::to_string)).collect()
    }
}

/// The Message-ID of a message, without its angle brackets.
pub fn message_id(bytes: &[u8]) -> Option<String> {
    let f = header_fields(bytes);
    let v = field(&f, "message-id")?.trim().trim_start_matches('<').trim_end_matches('>').trim().to_string();
    (!v.is_empty() && !v.contains(['"', '\\'])).then_some(v)
}

/// What an import did.
#[derive(Debug, Default, PartialEq)]
pub struct Outcome { pub imported: usize, pub already: usize, pub errors: Vec<String> }

/// Import messages from `items` into `mailbox` a batch at a time: those already in the folder
/// (same Message-ID) are skipped, the rest uploaded (bytes moved, not copied) and filed with
/// Email/import. `progress(processed)` reports after each batch with the count actually handled.
pub fn import(c: &dyn Server, mailbox: &str, items: &mut dyn Iterator<Item = Item>, progress: &mut dyn FnMut(usize)) -> Outcome {
    let mut out = Outcome::default();
    let batch = c.max_set().clamp(1, 50);
    let mut processed = 0;
    // what the folder holds already, so a retried import does not file messages twice
    let mut have = c.message_ids_in(mailbox);
    loop {
        let chunk: Vec<Item> = (&mut *items).take(batch).collect();
        if chunk.is_empty() { break; }
        processed += chunk.len();
        let ids: Vec<Option<String>> = chunk.iter().map(|it| message_id(&it.bytes)).collect();
        let mut entries = Map::new();
        for (i, (mut it, id)) in chunk.into_iter().zip(ids).enumerate() {
            if let Some(id) = id { if !have.insert(id) { out.already += 1; continue; } }
            if it.bytes.len() > c.max_upload() { out.errors.push("A message is larger than the server takes.".into()); continue; }
            let bytes = std::mem::take(&mut it.bytes);
            match c.upload(bytes) {
                Ok(blob) => { entries.insert(format!("m{i}"), import_entry(&blob, mailbox, &it)); }
                Err(e) => out.errors.push(e),
            }
        }
        if !entries.is_empty() {
            let count = entries.len();
            match c.call(json!([["Email/import", {"accountId": c.account(), "emails": entries}, "i"]])) {
                Ok(r) => {
                    out.imported += r[0][1]["created"].as_object().map(|o| o.len()).unwrap_or(0);
                    if let Some(bad) = r[0][1]["notCreated"].as_object() {
                        out.errors.extend(bad.values().map(crate::jmap::set_error));
                    }
                }
                Err(e) => out.errors.extend(std::iter::repeat_n(e, count)),
            }
        }
        progress(processed);
    }
    out
}

/// Whether the folder takes new messages (RFC 8621 §2 myRights.mayAddItems).
fn may_add(c: &Client, mailbox: &str) -> Result<bool, String> {
    let r = c.call(json!([["Mailbox/get", {"accountId": c.account, "ids": [mailbox], "properties": ["myRights"]}, "m"]]))?;
    Ok(r[0][1]["list"][0]["myRights"]["mayAddItems"].as_bool().unwrap_or(false))
}

/// "Import messages…" on a folder: pick .eml / .mbox files, then import them in the background
/// one message at a time (never a whole mbox in memory) with a running count, and bring the
/// folder list and the message list up to date.
pub fn start(u: &crate::AppWindow, c: std::sync::Arc<Client>, mailbox: &str) {
    use slint::ComponentHandle;
    let (w, mailbox) = (u.as_weak(), mailbox.to_string());
    // refuse up front when the folder does not take new messages (a read-only shared folder)
    match may_add(&c, &mailbox) {
        Ok(true) => {}
        Ok(false) => { crate::mail::show_toast(u, "You can't add messages to this folder.", false); return; }
        Err(e) => { crate::mail::show_toast(u, &e, false); return; }
    }
    let Some(files) = crate::automation::pick_files() else { return };
    if files.is_empty() { return; }
    crate::mail::show_toast(u, "Reading the files…", false);
    std::thread::spawn(move || {
        let total_bytes: u64 = files.iter().filter_map(|f| std::fs::metadata(f).ok()).map(|m| m.len()).sum::<u64>().max(1);
        let (mut unreadable, mut too_big, mut done_bytes) = (0usize, 0usize, 0u64);
        let found = std::cell::Cell::new(0usize);
        let mut acc = Outcome::default();
        for f in &files {
            let name = f.file_name().map(|n| n.to_string_lossy().to_string()).unwrap_or_default();
            let Ok(file) = std::fs::File::open(f) else { unreadable += 1; continue };
            let r = std::cell::RefCell::new(Reader::new(&name, std::io::BufReader::with_capacity(1 << 20, file), MAX_EML));
            let mut it = std::iter::from_fn(|| { let i = r.borrow_mut().next_item(); if i.is_some() { found.set(found.get() + 1); } i });
            let w2 = w.clone();
            let base = acc.imported + acc.already + acc.errors.len();
            let o = import(&*c, &mailbox, &mut it, &mut |n| {
                let pct = ((done_bytes + r.borrow().read) * 100 / total_bytes).min(100);
                let msg = format!("Importing… {} messages ({pct}%)", base + n);
                let _ = w2.upgrade_in_event_loop(move |u| u.set_toast(msg.into()));
            });
            let r = r.into_inner();
            done_bytes += r.read;
            if r.too_big { too_big += 1; }
            acc.imported += o.imported;
            acc.already += o.already;
            acc.errors.extend(o.errors);
        }
        if found.get() == 0 && unreadable + too_big == 0 {
            let _ = w.upgrade_in_event_loop(|u| crate::mail::show_toast(&u, "No messages found in those files.", false));
            return;
        }
        if too_big > 0 { acc.errors.push("A file is too large to be one message.".into()); }
        let failed = acc.errors.len() + unreadable;
        let msg = summary(acc.imported, acc.already, failed, acc.errors.first().map(String::as_str));
        let w3 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| {
            crate::mail::show_toast(&u, &msg, false);
            crate::mail::load_mailboxes(w3.clone(), c.clone(), false);
            crate::mail::sync(w3, c);
        });
    });
}

/// "Imported 12 messages", with what was skipped or went wrong when something was.
pub fn summary(ok: usize, already: usize, failed: usize, first_error: Option<&str>) -> String {
    let n = |k: usize| if k == 1 { "1 message".to_string() } else { format!("{k} messages") };
    let mut s = format!("Imported {}", n(ok));
    if already > 0 { s.push_str(&format!(", {already} already in the folder")); }
    match (failed, first_error) {
        (0, _) => {}
        (f, Some(e)) => s.push_str(&format!(", {f} not imported: {e}")),
        (f, None) => s.push_str(&format!(", {f} not imported")),
    }
    s
}

#[cfg(test)]
mod tests {
    #[test]
    fn summaries() {
        assert_eq!(super::summary(1, 0, 0, None), "Imported 1 message");
        assert_eq!(super::summary(2, 3, 0, None), "Imported 2 messages, 3 already in the folder");
        assert_eq!(super::summary(3, 0, 1, Some("The message is invalid.")), "Imported 3 messages, 1 not imported: The message is invalid.");
    }

    use super::*;

    const MBOX: &[u8] = b"From a@b.c Tue May  5 10:11:12 2020\nSubject: one\nStatus: RO\nX-Status: F\nDate: Tue, 05 May 2020 10:11:12 +0200\n\nHello\n>From the start\nFrom now on, unquoted\n\nFrom c@d.e Wed May  6 08:00:00 2020\r\nSubject: two\r\nX-Mozilla-Status: 0008\r\n\r\nGone\r\n\r\nFrom x@y.z Thu May  7 09:00 +0000 2020\nSubject: three\n\nBody\n";

    #[test]
    fn splits_mbox() {
        let m = split_mbox(MBOX);
        assert_eq!(m.len(), 3);
        let one = String::from_utf8(m[0].clone()).unwrap();
        assert!(one.starts_with("Subject: one\n"));
        // one ">" comes off, a body "From " after a line of text is not a separator, the empty
        // line before the next separator is dropped
        assert!(one.ends_with("Hello\nFrom the start\nFrom now on, unquoted\n"), "{one:?}");
        assert!(String::from_utf8_lossy(&m[1]).ends_with("Gone\r\n"));
        assert!(String::from_utf8_lossy(&m[2]).contains("Subject: three"));
    }

    #[test]
    fn envelope_lines() {
        assert!(is_envelope_line(b"From a@b.c Tue May  5 10:11:12 2020\n"));
        assert!(is_envelope_line(b"From MAILER-DAEMON Thu May  7 09:00 +0000 2020"));
        assert!(is_envelope_line(b"From a Tue May  5 10:11:12 2020 remote from host"));
        assert!(!is_envelope_line(b"From now on, we meet at 10:00 every day\n"));
        assert!(!is_envelope_line(b"Subject: x"));
    }

    #[test]
    fn states_and_dates() {
        let items = items_of("box.mbox", MBOX);
        // the expunged one is left out
        assert_eq!(items.len(), 2);
        assert_eq!(items[0].keywords, vec!["$seen".to_string(), "$flagged".to_string()]);
        assert_eq!(items[0].received_at.as_deref(), Some("2020-05-05T08:11:12Z"));
        // no status headers: read
        assert_eq!(items[1].keywords, vec!["$seen".to_string()]);
        let f = header_fields(b"X-Mozilla-Status: 0001\nX-Keywords: Work, \\Flagged $Junk\n\nbody");
        let (kw, gone) = mbox_state(&f);
        assert!(!gone);
        assert_eq!(kw, vec!["$seen", "work", "$flagged", "$junk"]);
        let unread = header_fields(b"Status: O\nDate: Mon, 16 Nov 2026 17:00:00 +0000 (UTC)\n\n");
        assert!(mbox_state(&unread).0.is_empty());
        assert_eq!(received_at(&unread).as_deref(), Some("2026-11-16T17:00:00Z"));
    }

    #[test]
    fn eml_is_one_message_and_entries() {
        let eml = b"Subject: hi\r\nFrom: a@b.c\r\n\r\nFrom here on it is body\r\n";
        let items = items_of("note.eml", eml);
        assert_eq!(items.len(), 1);
        assert_eq!(items[0].bytes, eml.to_vec());
        let e = import_entry("B1", "M1", &items[0]);
        assert_eq!(e["blobId"], "B1");
        assert_eq!(e["mailboxIds"]["M1"], true);
        assert_eq!(e["keywords"]["$seen"], true);
        assert_eq!(e["keywords"]["$imported"], true, "every message from a file is marked");
        assert!(e.get("receivedAt").is_none());
        let none = import_entry("B2", "M1", &Item { bytes: vec![], keywords: vec![], received_at: Some("2026-01-01T00:00:00Z".into()) });
        assert_eq!(none["keywords"], json!({"$imported": true}));
        assert_eq!(none["receivedAt"], "2026-01-01T00:00:00Z");
        // folded headers are unfolded
        let f = header_fields(b"Subject: a\n  b\n\n");
        assert_eq!(f[0], ("subject".to_string(), "a b".to_string()));
    }

    struct Mock { calls: std::cell::RefCell<Vec<Value>>, uploads: std::cell::RefCell<usize>, have: Vec<&'static str>, fail_one: bool }
    impl Server for Mock {
        fn account(&self) -> String { "A".into() }
        fn max_set(&self) -> usize { 2 }
        fn max_upload(&self) -> usize { 1000 }
        fn upload(&self, _d: Vec<u8>) -> Result<String, String> { let mut n = self.uploads.borrow_mut(); *n += 1; Ok(format!("B{n}")) }
        fn call(&self, calls: Value) -> Result<Vec<Value>, String> {
            self.calls.borrow_mut().push(calls.clone());
            let emails = calls[0][1]["emails"].as_object().unwrap();
            let mut created = Map::new();
            let mut not = Map::new();
            for (k, _) in emails {
                if self.fail_one && !created.is_empty() { not.insert(k.clone(), json!({"type": "invalidEmail", "description": "bad"})); } else { created.insert(k.clone(), json!({"id": "E"})); }
            }
            Ok(vec![json!(["Email/import", {"created": created, "notCreated": not}, "i"])])
        }
        fn message_ids_in(&self, mailbox: &str) -> std::collections::HashSet<String> {
            assert_eq!(mailbox, "M1");
            self.have.iter().map(|s| s.to_string()).collect()
        }
    }

    fn msg(id: &str) -> Item { Item { bytes: format!("Message-ID: <{id}>\r\nSubject: s\r\n\r\nb\r\n").into_bytes(), keywords: vec!["$seen".into()], received_at: None } }

    #[test]
    fn import_loop_created_not_created_and_duplicates() {
        let m = Mock { calls: Default::default(), uploads: Default::default(), have: vec!["b@x"], fail_one: true };
        let big = Item { bytes: vec![b'a'; 2000], keywords: vec![], received_at: None };
        // b@x is in the folder already; the second a@x repeats one in the same file
        let mut items = vec![msg("a@x"), msg("b@x"), msg("c@x"), big, msg("a@x")].into_iter();
        let mut seen = vec![];
        let o = import(&m, "M1", &mut items, &mut |n| seen.push(n));
        // b@x is already in the folder, the big one is refused before uploading
        assert_eq!(o.already, 2);
        assert_eq!(*m.uploads.borrow(), 2);
        // batch 1: a imported; batch 2: c imported (first entry), nothing refused
        assert_eq!(o.imported, 2);
        assert_eq!(o.errors, vec!["A message is larger than the server takes.".to_string()]);
        assert_eq!(seen, vec![2, 4, 5]);
        let calls = m.calls.borrow();
        assert_eq!(calls.len(), 2);
        let imp = &calls[0][0][1]["emails"];
        assert_eq!(imp.as_object().unwrap().len(), 1);
        assert_eq!(imp["m0"]["blobId"], "B1");
        // a notCreated answer is reported
        let m2 = Mock { calls: Default::default(), uploads: Default::default(), have: vec![], fail_one: true };
        let o2 = import(&m2, "M1", &mut vec![msg("a@x"), msg("b@x")].into_iter(), &mut |_| {});
        assert_eq!(o2.imported, 1);
        assert_eq!(o2.errors.len(), 1);
    }

    #[test]
    fn streaming_reader_and_eml_cap() {
        let mut r = Reader::new("box.mbox", std::io::BufReader::with_capacity(16, MBOX), u64::MAX);
        let a = r.next_item().unwrap();
        assert!(String::from_utf8_lossy(&a.bytes).starts_with("Subject: one"));
        let b = r.next_item().unwrap();
        assert!(String::from_utf8_lossy(&b.bytes).contains("Subject: three"));
        assert!(r.next_item().is_none());
        assert_eq!(r.read, MBOX.len() as u64);
        let mut big = Reader::new("x.eml", &b"Subject: x\r\n\r\n0123456789"[..], 10);
        assert!(big.next_item().is_none());
        assert!(big.too_big);
        assert_eq!(message_id(b"Message-Id: <a.b@c>\r\n\r\n").as_deref(), Some("a.b@c"));
        assert_eq!(message_id(b"Subject: x\r\n\r\n"), None);
    }
}

#[cfg(test)]
mod arrival_tests {
    use super::*;

    fn hdr(date: Option<&str>) -> Vec<(String, String)> {
        date.map(|d| vec![("date".to_string(), d.to_string())]).unwrap_or_default()
    }

    #[test]
    fn envelope_dates_are_read_as_utc_with_an_optional_zone() {
        let f = |l: &str| envelope_date(l).map(|t| t.format("%Y-%m-%dT%H:%M:%SZ").to_string());
        assert_eq!(f("From a Tue May  5 10:11:12 2020").as_deref(), Some("2020-05-05T10:11:12Z"));
        assert_eq!(f("From a Thu May  7 09:00 +0200 2020").as_deref(), Some("2020-05-07T07:00:00Z"));
        assert_eq!(f("From a Thu May  7 09:00:30 -0130 2020").as_deref(), Some("2020-05-07T10:30:30Z"));
        assert_eq!(f("From a Thu Foo  7 09:00:30 2020"), None);
        assert_eq!(f("From a Tue Feb 31 10:11:12 2020"), None);
        assert_eq!(f("From someone"), None);
    }

    #[test]
    fn own_exports_and_zoned_lines_trust_the_envelope() {
        let date = hdr(Some("Mon, 01 Jan 2018 00:00:00 +0000"));
        assert_eq!(arrival_time("From barbican-export Tue May  5 10:11:12 2020", &date).as_deref(), Some("2020-05-05T10:11:12Z"));
        assert_eq!(arrival_time("From bulwark-export Tue May  5 10:11:12 2020", &date).as_deref(), Some("2020-05-05T10:11:12Z"));
        assert_eq!(arrival_time("From x Tue May  5 10:11:12 +0200 2020", &date).as_deref(), Some("2020-05-05T08:11:12Z"));
        // trusted but unusable (before 1980): the Date header
        assert_eq!(arrival_time("From barbican-export Thu Jan  1 00:00:00 1970", &date).as_deref(), Some("2018-01-01T00:00:00Z"));
    }

    #[test]
    fn other_programs_lines_come_second() {
        let date = hdr(Some("Mon, 01 Jan 2018 00:00:00 +0000"));
        assert_eq!(arrival_time("From - Tue May  5 10:11:12 2020", &date).as_deref(), Some("2018-01-01T00:00:00Z"));
        // no Date header: a plausible envelope date, never an implausible one
        assert_eq!(arrival_time("From - Tue May  5 10:11:12 2020", &hdr(None)).as_deref(), Some("2020-05-05T10:11:12Z"));
        assert_eq!(arrival_time("From - Thu Jan  1 00:00:00 1970", &hdr(None)), None);
        assert_eq!(arrival_time("From - Tue May  5 10:11:12 2999", &hdr(None)), None);
        assert_eq!(arrival_time("", &hdr(None)), None);
    }

    #[test]
    fn the_splitter_hands_back_each_messages_envelope_line() {
        let mut sp = MboxSplitter::default();
        let mut got = vec![];
        for l in [&b"From a Tue May  5 10:11:12 2020\n"[..], b"Subject: 1\n", b"\n", b"From b Wed May  6 10:11:12 2020\r\n", b"Subject: 2\r\n"] {
            if sp.push(l).is_some() { got.push(sp.last_from().to_vec()); }
        }
        assert!(sp.finish().is_some());
        got.push(sp.last_from().to_vec());
        assert_eq!(got, vec![b"From a Tue May  5 10:11:12 2020".to_vec(), b"From b Wed May  6 10:11:12 2020".to_vec()]);
    }

    #[test]
    fn mbox_items_lose_their_state_headers_and_eml_files_keep_them() {
        let mbox = b"From barbican-export Tue May  5 10:11:12 2020\nStatus: RO\nX-Status: F\nX-Keywords: a\n b\nSubject: s\nX-Mozilla-Status: 0001\n\nStatus: in the body\n";
        let it = items_of("x.mbox", mbox);
        assert_eq!(it.len(), 1);
        assert_eq!(it[0].bytes, b"Subject: s\n\nStatus: in the body\n".to_vec());
        assert!(it[0].keywords.contains(&"$seen".to_string()) && it[0].keywords.contains(&"$flagged".to_string()));
        assert_eq!(it[0].received_at.as_deref(), Some("2020-05-05T10:11:12Z"));
        let eml = b"Status: RO\nSubject: s\n\nb\n";
        assert_eq!(items_of("a.eml", eml)[0].bytes, eml.to_vec());
        // a message with none of them is returned untouched
        assert_eq!(without_state_headers(b"Subject: s\n\nb\n".to_vec()), b"Subject: s\n\nb\n".to_vec());
    }
}
