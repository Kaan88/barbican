//! Export a folder as an .mbox file (mboxrd) or as a folder of .eml files (Bulwark
//! lib/mail-export/engine.ts, mbox-writer.ts, components/mail-transfer/mail-export-dialog.tsx).
//! Each message's raw blob is downloaded (RFC 8620 §6.2) and written at once, so only one message
//! is in memory at a time however large the folder is. The mbox keeps read / answered / flagged /
//! draft and other keywords in Status, X-Status and X-Keywords, the headers the import reads
//! back (mailimport::mbox_state), so an export imports as the same messages.
use crate::jmap::Client;
use serde_json::{json, Value};
use std::io::Write;
use std::sync::atomic::{AtomicBool, Ordering};

/// What an export needs to know about one message.
#[derive(Debug, Clone, PartialEq)]
pub struct MsgRef { pub blob_id: String, pub keywords: Vec<String>, pub received_at: Option<String>, pub subject: String }

/// A message larger than this is not downloaded into memory for an export (no server takes one).
pub const MAX_MESSAGE: usize = 200_000_000;

/// The sender on the envelope lines Barbican writes (Bulwark EXPORT_ENVELOPE_SENDER is
/// "bulwark-export").
pub const ENVELOPE_SENDER: &str = "barbican-export";

const DAYS: [&str; 7] = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
const MONTHS: [&str; 12] = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
/// Headers the export writes itself (and Thunderbird's), dropped from the message first so an old
/// value cannot contradict the new one.
const STATE_HEADERS: [&str; 6] = ["status", "x-status", "x-keywords", "x-mozilla-status", "x-mozilla-status2", "x-mozilla-keys"];
/// Keywords the server works out itself, or that have their own header.
const NOT_IN_X_KEYWORDS: [&str; 7] = ["$seen", "$flagged", "$answered", "$draft", "$recent", "$hasattachment", "$hasnoattachment"];

/// `From barbican-export Tue May  5 10:11:12 2020` for a JMAP UTCDate (UTC asctime).
pub fn envelope_line(received_at: Option<&str>, eol: &str) -> String {
    use chrono::{Datelike, Timelike};
    let d = received_at.and_then(|r| chrono::DateTime::parse_from_rfc3339(r).ok()).map(|t| t.with_timezone(&chrono::Utc))
        .unwrap_or_else(|| chrono::DateTime::UNIX_EPOCH);
    format!("From {ENVELOPE_SENDER} {} {} {:>2} {:02}:{:02}:{:02} {}{eol}",
        DAYS[d.weekday().num_days_from_sunday() as usize], MONTHS[d.month0() as usize], d.day(), d.hour(), d.minute(), d.second(), d.year())
}

/// The Status / X-Status / X-Keywords lines for a message's keywords.
pub fn state_headers(keywords: &[String], eol: &str) -> String {
    let has = |k: &str| keywords.iter().any(|x| x.eq_ignore_ascii_case(k));
    let xs = format!("{}{}{}", if has("$answered") { "A" } else { "" }, if has("$flagged") { "F" } else { "" }, if has("$draft") { "T" } else { "" });
    let others: Vec<&str> = keywords.iter().map(String::as_str)
        .filter(|k| !NOT_IN_X_KEYWORDS.contains(&k.to_ascii_lowercase().as_str()) && !k.contains(char::is_whitespace)).collect();
    let mut out = format!("Status: {}{eol}", if has("$seen") { "RO" } else { "O" });
    if !xs.is_empty() { out.push_str(&format!("X-Status: {xs}{eol}")); }
    if !others.is_empty() { out.push_str(&format!("X-Keywords: {}{eol}", others.join(" "))); }
    out
}

/// `^>*From ` at the start of a line.
fn quoted_from(line: &[u8]) -> bool {
    let n = line.iter().take_while(|b| **b == b'>').count();
    line[n..].starts_with(b"From ")
}

/// One message as mboxrd: the envelope line, the state headers, the message with every `>*From `
/// line quoted with one more `>`, and the empty line that ends the entry. The message's own line
/// endings are kept and the lines added use the same ones (the first line ending decides). A
/// message without a final line ending gets one first so its last line is not joined to the next
/// envelope line; the import then sees one line ending more than the server stored.
pub fn mbox_entry(raw: &[u8], keywords: &[String], received_at: Option<&str>) -> Vec<u8> {
    let eol = match raw.iter().position(|b| *b == b'\n') { Some(i) if i > 0 && raw[i - 1] == b'\r' => "\r\n", _ => "\n" };
    let mut out = Vec::with_capacity(raw.len() + 200);
    out.extend_from_slice(envelope_line(received_at, eol).as_bytes());
    out.extend_from_slice(state_headers(keywords, eol).as_bytes());
    let (mut in_headers, mut skipping) = (true, false);
    for line in raw.split_inclusive(|b| *b == b'\n') {
        if in_headers {
            if line == b"\n" || line == b"\r\n" {
                in_headers = false;
                skipping = false;
            } else if line[0] == b' ' || line[0] == b'\t' {
                // a folded line continues the field before it
                if skipping { continue; }
            } else {
                let name = line.iter().position(|b| *b == b':').map(|c| String::from_utf8_lossy(&line[..c]).trim().to_ascii_lowercase()).unwrap_or_default();
                skipping = STATE_HEADERS.contains(&name.as_str());
                if skipping { continue; }
            }
        }
        if quoted_from(line) { out.push(b'>'); }
        out.extend_from_slice(line);
    }
    let ends_lf = raw.last() == Some(&b'\n');
    out.extend_from_slice(eol.as_bytes());
    if !ends_lf { out.extend_from_slice(eol.as_bytes()); }
    out
}

// ------------------------------------------------------------------ file names

/// Characters that are not allowed in a file name on some system, plus control and bidirectional
/// formatting characters (a right-to-left override can make "gpj.exe" read as "exe.jpg").
fn bad_char(c: char) -> bool {
    c.is_control() || "<>:\"/\\|?*".contains(c)
        || matches!(c, '\u{200B}'..='\u{200F}' | '\u{202A}'..='\u{202E}' | '\u{2066}'..='\u{2069}' | '\u{FEFF}')
}

/// Windows device names, which cannot be file names there with or without an extension.
fn reserved(stem: &str) -> bool {
    let s = stem.trim_end_matches([' ', '.']).to_ascii_uppercase();
    matches!(s.as_str(), "CON" | "PRN" | "AUX" | "NUL" | "CONIN$" | "CONOUT$")
        || (s.len() == 4 && (s.starts_with("COM") || s.starts_with("LPT")) && s.as_bytes()[3].is_ascii_digit() && s.as_bytes()[3] != b'0')
}

/// A name that is safe as a file or folder name on every system (Bulwark safeName, plus Windows
/// device names and a byte limit): separators and special characters become spaces, runs of space
/// collapse, leading and trailing dots and spaces go (so no "..", no hidden file, no name Windows
/// shortens), at most `max` characters and 150 bytes, never empty and never a device name.
pub fn safe_name(name: &str, fallback: &str, max: usize) -> String {
    let spaced: String = name.chars().map(|c| if bad_char(c) { ' ' } else { c }).collect();
    let collapsed = spaced.split_whitespace().collect::<Vec<_>>().join(" ");
    let mut out = String::new();
    for c in collapsed.trim_matches(['.', ' ']).chars() {
        if out.chars().count() >= max || out.len() + c.len_utf8() > 150 { break; }
        out.push(c);
    }
    let out = out.trim_matches(['.', ' ']).to_string();
    let out = if out.is_empty() { fallback.to_string() } else { out };
    // the part before the first dot is what Windows compares with a device name
    if reserved(out.split('.').next().unwrap_or("")) { format!("_{out}") } else { out }
}

/// "00001 Subject.eml": the number keeps the folder's order and makes every name unique.
pub fn eml_name(seq: usize, subject: &str) -> String { format!("{seq:05} {}.eml", safe_name(subject, "message", 60)) }

/// A folder name for the export that does not exist yet next to it: "Inbox", "Inbox (2)", ...
pub fn free_dir(parent: &std::path::Path, name: &str) -> std::path::PathBuf {
    let base = safe_name(name, "Folder", 80);
    let mut p = parent.join(&base);
    let mut n = 1;
    while p.exists() { n += 1; p = parent.join(format!("{base} ({n})")); }
    p
}

// ------------------------------------------------------------------ the export loop

/// What an export did.
#[derive(Debug, Default, PartialEq)]
pub struct Outcome { pub exported: usize, pub failed: usize, pub first_error: Option<String>, pub cancelled: bool }

impl Outcome {
    fn fail(&mut self, e: String) { self.failed += 1; if self.first_error.is_none() { self.first_error = Some(e); } }
}

/// Fetch each message in turn and hand it to `write`; `progress(done, total)` after each one. A
/// message that cannot be fetched is counted and skipped; a write that fails (disk full) ends the
/// export with the error. The cancel flag is checked before every message.
fn run(refs: &[MsgRef], fetch: &mut dyn FnMut(&MsgRef) -> Result<Vec<u8>, String>, write: &mut dyn FnMut(usize, &MsgRef, &[u8]) -> std::io::Result<()>,
       cancel: &AtomicBool, progress: &mut dyn FnMut(usize, usize)) -> Result<Outcome, String> {
    let mut out = Outcome::default();
    for (i, r) in refs.iter().enumerate() {
        if cancel.load(Ordering::Relaxed) { out.cancelled = true; break; }
        match fetch(r) {
            Ok(raw) => {
                write(i + 1, r, &raw).map_err(|e| format!("Could not write the file: {e}"))?;
                out.exported += 1;
            }
            Err(e) => out.fail(e),
        }
        progress(i + 1, refs.len());
    }
    Ok(out)
}

/// Write the messages as one mbox.
pub fn export_mbox(refs: &[MsgRef], fetch: &mut dyn FnMut(&MsgRef) -> Result<Vec<u8>, String>, out: &mut dyn Write,
                   cancel: &AtomicBool, progress: &mut dyn FnMut(usize, usize)) -> Result<Outcome, String> {
    run(refs, fetch, &mut |_, r, raw| out.write_all(&mbox_entry(raw, &r.keywords, r.received_at.as_deref())), cancel, progress)
}

/// Write the messages as .eml files in `dir` (which must exist); a file is never overwritten.
pub fn export_eml(refs: &[MsgRef], fetch: &mut dyn FnMut(&MsgRef) -> Result<Vec<u8>, String>, dir: &std::path::Path,
                  cancel: &AtomicBool, progress: &mut dyn FnMut(usize, usize)) -> Result<Outcome, String> {
    run(refs, fetch, &mut |seq, r, raw| {
        let mut f = std::fs::OpenOptions::new().write(true).create_new(true).open(dir.join(eml_name(seq, &r.subject)))?;
        f.write_all(raw)
    }, cancel, progress)
}

/// The folder's messages, oldest first.
pub fn list_refs(c: &Client, mailbox: &str) -> Result<Vec<MsgRef>, String> {
    let acc = c.account.clone();
    let ids = c.query_all("Email/query", &acc, json!({"filter": {"inMailbox": mailbox}, "sort": [{"property": "receivedAt", "isAscending": true}]}))?;
    let list = c.get_all("Email/get", &acc, &ids, Some(&json!(["id", "blobId", "keywords", "receivedAt", "subject"])))?;
    // Email/get returns in server order; keep the query's order
    let mut by_id: std::collections::HashMap<String, Value> = list.into_iter().filter_map(|e| Some((e["id"].as_str()?.to_string(), e))).collect();
    Ok(ids.iter().filter_map(|i| by_id.remove(i.as_str()?)).filter_map(|e| Some(MsgRef {
        blob_id: e["blobId"].as_str()?.to_string(),
        keywords: e["keywords"].as_object().map(|k| k.iter().filter(|(_, v)| v.as_bool() == Some(true)).map(|(k, _)| k.clone()).collect()).unwrap_or_default(),
        received_at: e["receivedAt"].as_str().map(String::from),
        subject: e["subject"].as_str().unwrap_or("").to_string(),
    })).collect())
}

/// Makes a new folder `name` (or "name (2)", ...) in `parent`. `create_dir` fails when the name
/// exists, so a folder that was there before is never reused (and never removed on a cancel).
pub fn make_dir(parent: &std::path::Path, name: &str) -> std::io::Result<std::path::PathBuf> {
    let mut last = None;
    for _ in 0..100 {
        let p = free_dir(parent, name);
        match std::fs::create_dir(&p) {
            Ok(()) => return Ok(p),
            Err(e) if e.kind() == std::io::ErrorKind::AlreadyExists => last = Some(e),
            Err(e) => return Err(e),
        }
    }
    Err(last.unwrap_or_else(|| std::io::Error::other("no free folder name")))
}

/// Clears `RUNNING` when the export thread ends, however it ends.
struct RunningGuard;
impl Drop for RunningGuard { fn drop(&mut self) { RUNNING.store(false, Ordering::Relaxed); } }

static RUNNING: AtomicBool = AtomicBool::new(false);
static CANCEL: AtomicBool = AtomicBool::new(false);

/// Whether an export is running (the folder menu then offers "Cancel export").
pub fn running() -> bool { RUNNING.load(Ordering::Relaxed) }

/// Ask the running export to stop after the message it is on.
pub fn cancel() { CANCEL.store(true, Ordering::Relaxed); }

/// "Exported 12 messages", with what went wrong when something did.
pub fn summary(o: &Outcome, what: &str) -> String {
    let n = |k: usize| if k == 1 { "1 message".to_string() } else { format!("{k} messages") };
    if o.cancelled { return "Export cancelled".into(); }
    let mut s = format!("Exported {} to {what}", n(o.exported));
    if o.failed > 0 { s.push_str(&format!(", {} not exported: {}", o.failed, o.first_error.as_deref().unwrap_or("download failed"))); }
    s
}

// ------------------------------------------------------------------ the whole account

/// One folder of a whole-account export: its mailbox id and its path of safe names from the top.
#[derive(Debug, Clone, PartialEq)]
pub struct PlanFolder { pub id: String, pub path: Vec<String> }

/// One path component: Bulwark safeName, then the attachment file-name rules on top.
fn folder_component(name: &str) -> String { crate::attachments::safe_file_name(&safe_name(name, "Folder", 80)) }

type ByParent<'a> = std::collections::HashMap<&'a str, Vec<&'a Value>>;

fn plan_visit(parent: &str, path: &[String], by_parent: &ByParent, out: &mut Vec<PlanFolder>, depth: usize) {
    const ROLES: [&str; 6] = ["inbox", "drafts", "sent", "archive", "junk", "trash"];
    let rank = |m: &Value| m["role"].as_str().and_then(|r| ROLES.iter().position(|x| *x == r)).unwrap_or(ROLES.len());
    let Some(kids) = by_parent.get(parent) else { return };
    if depth > 64 { return; }
    let mut kids = kids.clone();
    kids.sort_by(|a, b| rank(a).cmp(&rank(b))
        .then(a["sortOrder"].as_i64().unwrap_or(0).cmp(&b["sortOrder"].as_i64().unwrap_or(0)))
        .then(a["name"].as_str().unwrap_or("").to_lowercase().cmp(&b["name"].as_str().unwrap_or("").to_lowercase())));
    let mut used = std::collections::HashSet::new();
    for m in kids {
        let base = folder_component(m["name"].as_str().unwrap_or(""));
        let mut name = base.clone();
        let mut n = 2;
        // a folder takes `name` and `name.mbox` together, so "Inbox" and "Inbox.mbox" cannot meet
        let taken = |s: &str, used: &std::collections::HashSet<String>| used.contains(&s.to_lowercase()) || used.contains(&format!("{s}.mbox").to_lowercase());
        while taken(&name, &used) { name = format!("{base} ({n})"); n += 1; }
        used.insert(name.to_lowercase());
        used.insert(format!("{name}.mbox").to_lowercase());
        let mut p = path.to_vec();
        p.push(name);
        let id = m["id"].as_str().unwrap_or("");
        out.push(PlanFolder { id: id.to_string(), path: p.clone() });
        plan_visit(id, &p, by_parent, out, depth + 1);
    }
}

/// The account's folders as export paths (Bulwark lib/mail-export/engine.ts folderTree, 148-174):
/// parents first, siblings in the special-folder order then sortOrder then name, and two siblings
/// whose names agree (case-insensitively) get " (2)", " (3)", ... A folder whose parent is not in
/// the list is at the top.
pub fn folder_plan(boxes: &[Value]) -> Vec<PlanFolder> {
    let ids: std::collections::HashSet<&str> = boxes.iter().filter_map(|m| m["id"].as_str()).collect();
    let mut by_parent: ByParent = std::collections::HashMap::new();
    for m in boxes {
        if m["id"].as_str().is_none() { continue; }
        let parent = m["parentId"].as_str().filter(|p| ids.contains(p)).unwrap_or("");
        by_parent.entry(parent).or_default().push(m);
    }
    let mut out = vec![];
    plan_visit("", &[], &by_parent, &mut out, 0);
    out
}

/// Write every planned folder under `root` (which exists): as `<path>.mbox` (only folders with
/// messages; each written to `.part` and moved into place when whole) or as a directory per folder
/// of .eml files (an empty folder is kept as an empty directory). `progress(done, total)` counts
/// messages over the whole account. Stops at the first cancel or write error; a `.part` is removed.
pub fn export_tree(plan: &[(PlanFolder, Vec<MsgRef>)], root: &std::path::Path, mbox: bool,
                   fetch: &mut dyn FnMut(&MsgRef) -> Result<Vec<u8>, String>, cancel: &AtomicBool, progress: &mut dyn FnMut(usize, usize)) -> Result<Outcome, String> {
    let total: usize = plan.iter().map(|(_, r)| r.len()).sum();
    let mut all = Outcome::default();
    let mut base = 0;
    for (f, refs) in plan {
        if cancel.load(Ordering::Relaxed) { all.cancelled = true; break; }
        let Some((last, parents)) = f.path.split_last() else { continue };
        let parent_dir = parents.iter().fold(root.to_path_buf(), |d, p| d.join(p));
        let mut prog = |d: usize, _: usize| progress(base + d, total);
        let o = if mbox {
            if refs.is_empty() { continue; }
            std::fs::create_dir_all(&parent_dir).map_err(|e| format!("Could not create the folder: {e}"))?;
            let target = parent_dir.join(format!("{last}.mbox"));
            let part = parent_dir.join(format!("{last}.mbox.part"));
            let file = std::fs::File::create(&part).map_err(|e| format!("Could not create the file: {e}"))?;
            let mut out = std::io::BufWriter::with_capacity(1 << 20, file);
            let r = export_mbox(refs, fetch, &mut out, cancel, &mut prog).and_then(|o| out.flush().map(|_| o).map_err(|e| e.to_string()));
            drop(out);
            match &r {
                Ok(o) if !o.cancelled => std::fs::rename(&part, &target).map_err(|e| { let _ = std::fs::remove_file(&part); format!("Could not write the file: {e}") })?,
                _ => { let _ = std::fs::remove_file(&part); }
            }
            r?
        } else {
            let dir = parent_dir.join(last);
            std::fs::create_dir_all(&dir).map_err(|e| format!("Could not create the folder: {e}"))?;
            export_eml(refs, fetch, &dir, cancel, &mut prog)?
        };
        base += refs.len();
        all.exported += o.exported;
        all.failed += o.failed;
        if all.first_error.is_none() { all.first_error = o.first_error; }
        if o.cancelled { all.cancelled = true; break; }
    }
    Ok(all)
}

/// "Export all mail…": ask for a folder, make a new "Mail export <date>" folder in it, and write
/// every folder of the account there (see `export_tree`), in the background with a running count.
pub fn start_all(u: &crate::AppWindow, c: std::sync::Arc<Client>, mbox: bool) {
    use slint::ComponentHandle;
    if RUNNING.load(Ordering::Relaxed) { crate::mail::show_toast(u, "An export is already running.", false); return; }
    let Some(target) = rfd::FileDialog::new().set_title("Choose where to put the exported mail").pick_folder() else { return };
    RUNNING.store(true, Ordering::Relaxed);
    CANCEL.store(false, Ordering::Relaxed);
    crate::mail::show_toast(u, "Reading the folders…", false);
    let w = u.as_weak();
    std::thread::spawn(move || {
        let _running = RunningGuard;
        let mut shown = String::new();
        let mut skipped = 0usize;
        let res = (|| -> Result<Outcome, String> {
            let boxes = c.mailboxes()?;
            let plan = folder_plan(&boxes);
            skipped = boxes.len().saturating_sub(plan.len());
            let mut listed = Vec::with_capacity(plan.len());
            for f in plan {
                if CANCEL.load(Ordering::Relaxed) { return Ok(Outcome { cancelled: true, ..Default::default() }); }
                let refs = list_refs(&c, &f.id)?;
                listed.push((f, refs));
            }
            if listed.iter().all(|(_, r)| r.is_empty()) { return Err("The account has no messages.".into()); }
            let root = make_dir(&target, &format!("Mail export {}", chrono::Local::now().format("%Y-%m-%d"))).map_err(|e| format!("Could not create the folder: {e}"))?;
            shown = root.file_name().map(|n| n.to_string_lossy().to_string()).unwrap_or_default();
            let mut fetch = |r: &MsgRef| c.download(&r.blob_id, "message.eml", "message/rfc822").and_then(|b| if b.len() > MAX_MESSAGE { Err("A message is too large.".into()) } else { Ok(b) });
            let mut last = std::time::Instant::now() - std::time::Duration::from_secs(1);
            let w2 = w.clone();
            let mut progress = move |done: usize, total: usize| {
                if done != total && last.elapsed() < std::time::Duration::from_millis(250) { return; }
                last = std::time::Instant::now();
                let msg = format!("Exporting… {done} of {total} messages");
                let _ = w2.upgrade_in_event_loop(move |u| u.set_toast(msg.into()));
            };
            let r = export_tree(&listed, &root, mbox, &mut fetch, &CANCEL, &mut progress);
            // only the folder this export made is ever removed again
            if !matches!(&r, Ok(o) if !o.cancelled) { let _ = std::fs::remove_dir_all(&root); }
            r
        })();
        let msg = match res {
            Ok(o) if skipped > 0 && !o.cancelled => format!("{}; {skipped} folders left out (nested too deep or in a loop)", summary(&o, &shown)),
            Ok(o) => summary(&o, &shown),
            Err(e) => format!("Could not export: {e}"),
        };
        let _ = w.upgrade_in_event_loop(move |u| crate::mail::show_toast(&u, &msg, false));
    });
}

/// "Export folder…" on a folder: ask where, then export in the background with a running count.
/// `mbox` writes one .mbox file, otherwise a new folder of .eml files is made in the chosen folder.
pub fn start(u: &crate::AppWindow, c: std::sync::Arc<Client>, mailbox: &str, folder_name: &str, mbox: bool) {
    use slint::ComponentHandle;
    if RUNNING.load(Ordering::Relaxed) { crate::mail::show_toast(u, "An export is already running.", false); return; }
    let target = if mbox {
        crate::automation::save_file(&format!("{}.mbox", safe_name(folder_name, "Folder", 80)), "mbox", &["mbox"])
    } else {
        // the folder to make the export folder in; the export folder itself is made in the thread
        rfd::FileDialog::new().set_title("Choose where to put the .eml files").pick_folder()
    };
    let Some(target) = target else { return };
    let folder_name = folder_name.to_string();
    RUNNING.store(true, Ordering::Relaxed);
    CANCEL.store(false, Ordering::Relaxed);
    crate::mail::show_toast(u, "Reading the folder…", false);
    let (w, mailbox) = (u.as_weak(), mailbox.to_string());
    std::thread::spawn(move || {
        // cleared however the thread ends, a panic included
        let _running = RunningGuard;
        let mut shown = target.clone();
        let res = (|| -> Result<Outcome, String> {
            let refs = list_refs(&c, &mailbox)?;
            if refs.is_empty() { return Err("The folder has no messages.".into()); }
            let mut fetch = |r: &MsgRef| c.download(&r.blob_id, "message.eml", "message/rfc822").and_then(|b| if b.len() > MAX_MESSAGE { Err("A message is too large.".into()) } else { Ok(b) });
            let mut last = std::time::Instant::now() - std::time::Duration::from_secs(1);
            let w2 = w.clone();
            let mut progress = move |done: usize, total: usize| {
                // a toast per message would flood the event loop on a big folder
                if done != total && last.elapsed() < std::time::Duration::from_millis(250) { return; }
                last = std::time::Instant::now();
                let msg = format!("Exporting… {done} of {total} messages");
                let _ = w2.upgrade_in_event_loop(move |u| u.set_toast(msg.into()));
            };
            if mbox {
                // written beside the chosen file and moved over it at the end, so a cancel or a
                // failure never destroys a file the user chose to replace
                let mut part = target.clone().into_os_string();
                part.push(".part");
                let part = std::path::PathBuf::from(part);
                let file = std::fs::File::create(&part).map_err(|e| format!("Could not create the file: {e}"))?;
                let mut out = std::io::BufWriter::with_capacity(1 << 20, file);
                let r = export_mbox(&refs, &mut fetch, &mut out, &CANCEL, &mut progress).and_then(|o| out.flush().map(|_| o).map_err(|e| e.to_string()));
                drop(out);
                match &r {
                    Ok(o) if !o.cancelled => std::fs::rename(&part, &target).map_err(|e| { let _ = std::fs::remove_file(&part); format!("Could not write the file: {e}") })?,
                    _ => { let _ = std::fs::remove_file(&part); }
                }
                r
            } else {
                // only a folder this export made is ever removed again
                let dir = make_dir(&target, &folder_name).map_err(|e| format!("Could not create the folder: {e}"))?;
                shown = dir.clone();
                let r = export_eml(&refs, &mut fetch, &dir, &CANCEL, &mut progress);
                if !matches!(&r, Ok(o) if !o.cancelled) { let _ = std::fs::remove_dir_all(&dir); }
                r
            }
        })();
        let msg = match res {
            Ok(o) => summary(&o, &shown.file_name().map(|n| n.to_string_lossy().to_string()).unwrap_or_default()),
            Err(e) => format!("Could not export: {e}"),
        };
        let _ = w.upgrade_in_event_loop(move |u| crate::mail::show_toast(&u, &msg, false));
    });
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::mailimport::{items_of, Item};

    fn kw(k: &[&str]) -> Vec<String> { k.iter().map(|s| s.to_string()).collect() }
    const AT: &str = "2020-05-05T10:11:12Z";

    #[test]
    fn envelope_line_is_utc_asctime() {
        assert_eq!(envelope_line(Some(AT), "\n"), "From barbican-export Tue May  5 10:11:12 2020\n");
        assert_eq!(envelope_line(Some("2021-12-25T23:59:59+02:00"), "\r\n"), "From barbican-export Sat Dec 25 21:59:59 2021\r\n");
        assert_eq!(envelope_line(None, "\n"), "From barbican-export Thu Jan  1 00:00:00 1970\n");
        assert_eq!(envelope_line(Some("nonsense"), "\n"), "From barbican-export Thu Jan  1 00:00:00 1970\n");
        assert!(crate::mailimport::is_envelope_line(envelope_line(Some(AT), "\n").as_bytes()));
    }

    #[test]
    fn state_headers_by_keyword() {
        assert_eq!(state_headers(&kw(&["$seen"]), "\n"), "Status: RO\n");
        assert_eq!(state_headers(&[], "\n"), "Status: O\n");
        assert_eq!(state_headers(&kw(&["$seen", "$answered", "$flagged", "$draft", "$label:work", "$hasattachment"]), "\r\n"),
            "Status: RO\r\nX-Status: AFT\r\nX-Keywords: $label:work\r\n");
    }

    #[test]
    fn from_lines_are_quoted_once_more() {
        let raw = b"Subject: a\n\nFrom me\n>From you\n>>From them\nFrom\nx From y\n";
        let e = mbox_entry(raw, &[], Some(AT));
        let t = String::from_utf8(e).unwrap();
        assert!(t.contains("\n>From me\n>>From you\n>>>From them\nFrom\nx From y\n"), "{t}");
        assert!(t.ends_with("\n\n"));
    }

    #[test]
    fn a_from_line_among_the_headers_is_quoted_too() {
        let e = mbox_entry(b"From: a@example.test\nFrom x\nSubject: s\n\nb\n", &[], Some(AT));
        let t = String::from_utf8(e).unwrap();
        assert!(t.contains("From: a@example.test\n>From x\n"), "{t}");
    }

    #[test]
    fn old_state_headers_are_replaced_including_folded_ones() {
        let raw = b"X-Keywords: old\n folded\nStatus: R\nSubject: s\nX-Mozilla-Status: 0001\n\nStatus: in body\n";
        let t = String::from_utf8(mbox_entry(raw, &kw(&["$flagged"]), Some(AT))).unwrap();
        assert!(!t.contains("old") && !t.contains("folded") && !t.contains("0001"), "{t}");
        assert!(t.contains("Status: O\nX-Status: F\nSubject: s\n\nStatus: in body\n"), "{t}");
    }

    #[test]
    fn crlf_messages_get_crlf_lines() {
        let raw = b"Subject: s\r\n\r\nFrom x\r\n";
        let t = mbox_entry(raw, &kw(&["$seen"]), Some(AT));
        assert!(t.starts_with(b"From barbican-export Tue May  5 10:11:12 2020\r\nStatus: RO\r\nSubject: s\r\n\r\n>From x\r\n"));
        assert!(t.ends_with(b"\r\n\r\n"));
    }

    #[test]
    fn a_message_without_final_newline_ends_cleanly() {
        let t = mbox_entry(b"Subject: s\n\nno newline", &[], Some(AT));
        assert!(t.ends_with(b"no newline\n\n"));
    }

    #[test]
    fn names_are_safe() {
        assert_eq!(safe_name("a/b\\c:d", "x", 80), "a b c d");
        assert_eq!(safe_name("../../etc/passwd", "x", 80), "etc passwd");
        assert_eq!(safe_name("..", "x", 80), "x");
        assert_eq!(safe_name("  .hidden. ", "x", 80), "hidden");
        assert_eq!(safe_name("", "message", 60), "message");
        assert_eq!(safe_name("CON", "x", 80), "_CON");
        assert_eq!(safe_name("nul.txt", "x", 80), "_nul.txt");
        assert_eq!(safe_name("com1", "x", 80), "_com1");
        assert_eq!(safe_name("COM0", "x", 80), "COM0");
        assert_eq!(safe_name("console", "x", 80), "console");
        assert_eq!(safe_name("a\u{202E}gpj.exe\u{0}b", "x", 80), "a gpj.exe b");
        assert_eq!(safe_name("a\tb\nc", "x", 80), "a b c");
        assert_eq!(safe_name(&"x".repeat(500), "x", 60).len(), 60);
        // multi-byte characters are cut by characters and by bytes, never in the middle of one
        let long = safe_name(&"é".repeat(500), "x", 80);
        assert!(long.len() <= 150 && long.chars().all(|c| c == 'é'));
        let t = safe_name(&format!("{}...", "a".repeat(60)), "x", 60);
        assert_eq!(t, "a".repeat(60));
    }

    #[test]
    fn eml_names_are_numbered_and_never_collide() {
        assert_eq!(eml_name(1, "Hello: world"), "00001 Hello world.eml");
        assert_eq!(eml_name(12, ""), "00012 message.eml");
        assert_ne!(eml_name(1, "same"), eml_name(2, "same"));
        // a device name as subject is fine behind the number, a path is never produced
        assert!(!eml_name(3, "../../x").contains(['/', '\\']));
    }

    #[test]
    fn free_dir_avoids_existing_folders() {
        let base = std::env::temp_dir().join(format!("barbican-export-test-{}", std::process::id()));
        std::fs::create_dir_all(base.join("Inbox")).unwrap();
        std::fs::create_dir_all(base.join("Inbox (2)")).unwrap();
        assert_eq!(free_dir(&base, "Inbox"), base.join("Inbox (3)"));
        assert_eq!(free_dir(&base, "../x"), base.join("x"));
        let _ = std::fs::remove_dir_all(&base);
    }

    #[test]
    fn make_dir_never_reuses_an_existing_folder() {
        let base = std::env::temp_dir().join(format!("barbican-export-mk-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&base);
        std::fs::create_dir_all(base.join("Inbox")).unwrap();
        std::fs::write(base.join("Inbox").join("keep.txt"), b"x").unwrap();
        let made = make_dir(&base, "Inbox").unwrap();
        assert_eq!(made, base.join("Inbox (2)"));
        assert!(base.join("Inbox").join("keep.txt").exists());
        let _ = std::fs::remove_dir_all(&base);
    }

    #[test]
    fn the_running_flag_is_cleared_by_a_panicking_thread() {
        RUNNING.store(true, Ordering::Relaxed);
        let h = std::thread::spawn(|| { let _g = RunningGuard; panic!("boom"); });
        assert!(h.join().is_err());
        assert!(!running());
    }

    fn msgs() -> Vec<(MsgRef, Vec<u8>)> {
        let mk = |n: usize, kws: &[&str], raw: &[u8]| (MsgRef { blob_id: format!("b{n}"), keywords: kw(kws), received_at: Some(AT.into()), subject: format!("Subject {n}") }, raw.to_vec());
        vec![
            mk(1, &["$seen"], b"Message-ID: <1@example.test>\nDate: Tue, 05 May 2020 10:11:12 +0000\nSubject: one\n\nHello\nFrom me to you\n>From quoted\n"),
            mk(2, &["$answered", "$flagged"], b"Message-ID: <2@example.test>\r\nSubject: two\r\n\r\nBody\r\n\r\nafter blank\r\n"),
            mk(3, &[], b"Message-ID: <3@example.test>\nSubject: three\n\nFrom\n"),
        ]
    }

    #[test]
    fn mbox_round_trips_through_the_import() {
        let list = msgs();
        let refs: Vec<MsgRef> = list.iter().map(|(r, _)| r.clone()).collect();
        let mut fetch = |r: &MsgRef| list.iter().find(|(x, _)| x.blob_id == r.blob_id).map(|(_, b)| b.clone()).ok_or("gone".to_string());
        let mut out = vec![];
        let mut seen = vec![];
        let o = export_mbox(&refs, &mut fetch, &mut out, &AtomicBool::new(false), &mut |d, t| seen.push((d, t))).unwrap();
        assert_eq!(o, Outcome { exported: 3, ..Default::default() });
        assert_eq!(seen, vec![(1, 3), (2, 3), (3, 3)]);
        let items: Vec<Item> = items_of("export.mbox", &out);
        assert_eq!(items.len(), 3);
        for (it, (r, raw)) in items.iter().zip(&list) {
            // the same bytes: the import drops the state headers the export added
            assert_eq!(&it.bytes, raw, "bytes of {}", r.subject);
            let mut got = it.keywords.clone();
            got.sort();
            let mut want = r.keywords.clone();
            want.sort();
            assert_eq!(got, want, "keywords of {}", r.subject);
        }
        assert_eq!(items[0].received_at.as_deref(), Some("2020-05-05T10:11:12Z"));
        // messages 2 and 3 have no Date header: the export's own envelope date is kept
        assert_eq!(items[1].received_at.as_deref(), Some("2020-05-05T10:11:12Z"));
        assert_eq!(items[2].received_at.as_deref(), Some("2020-05-05T10:11:12Z"));
    }

    #[test]
    fn a_receivedat_that_differs_from_the_date_header_survives() {
        let r = MsgRef { blob_id: "b".into(), keywords: vec![], received_at: Some("2021-03-04T05:06:07Z".into()), subject: "s".into() };
        let raw = b"Message-ID: <d@example.test>\nDate: Tue, 05 May 2020 10:11:12 +0000\nSubject: s\n\nb\n".to_vec();
        let mut fetch = |_: &MsgRef| Ok(raw.clone());
        let mut out = vec![];
        export_mbox(&[r], &mut fetch, &mut out, &AtomicBool::new(false), &mut |_, _| {}).unwrap();
        assert_eq!(items_of("x.mbox", &out)[0].received_at.as_deref(), Some("2021-03-04T05:06:07Z"));
    }

    #[test]
    fn eml_files_round_trip_byte_for_byte() {
        let list = msgs();
        let refs: Vec<MsgRef> = list.iter().map(|(r, _)| r.clone()).collect();
        let dir = std::env::temp_dir().join(format!("barbican-export-eml-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let mut fetch = |r: &MsgRef| list.iter().find(|(x, _)| x.blob_id == r.blob_id).map(|(_, b)| b.clone()).ok_or("gone".to_string());
        let o = export_eml(&refs, &mut fetch, &dir, &AtomicBool::new(false), &mut |_, _| {}).unwrap();
        assert_eq!(o.exported, 3);
        let mut names: Vec<String> = std::fs::read_dir(&dir).unwrap().map(|e| e.unwrap().file_name().to_string_lossy().to_string()).collect();
        names.sort();
        assert_eq!(names, vec!["00001 Subject 1.eml", "00002 Subject 2.eml", "00003 Subject 3.eml"]);
        for (n, (_, raw)) in names.iter().zip(&list) {
            let bytes = std::fs::read(dir.join(n)).unwrap();
            assert_eq!(&bytes, raw);
            let items = items_of(n, &bytes);
            assert_eq!(items.len(), 1);
            assert_eq!(&items[0].bytes, raw);
        }
        // a second run into the same folder never overwrites
        assert!(export_eml(&refs, &mut fetch, &dir, &AtomicBool::new(false), &mut |_, _| {}).is_err());
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn failures_are_counted_and_the_rest_exported() {
        let list = msgs();
        let refs: Vec<MsgRef> = list.iter().map(|(r, _)| r.clone()).collect();
        let mut fetch = |r: &MsgRef| if r.blob_id == "b2" { Err("Download failed (404)".to_string()) } else { Ok(b"Subject: x\n\ny\n".to_vec()) };
        let mut out = vec![];
        let o = export_mbox(&refs, &mut fetch, &mut out, &AtomicBool::new(false), &mut |_, _| {}).unwrap();
        assert_eq!((o.exported, o.failed, o.first_error.as_deref()), (2, 1, Some("Download failed (404)")));
        assert_eq!(items_of("a.mbox", &out).len(), 2);
        assert_eq!(summary(&o, "x.mbox"), "Exported 2 messages to x.mbox, 1 not exported: Download failed (404)");
    }

    #[test]
    fn cancel_stops_before_the_next_message() {
        let list = msgs();
        let refs: Vec<MsgRef> = list.iter().map(|(r, _)| r.clone()).collect();
        let cancel = AtomicBool::new(false);
        let mut fetched = 0;
        let mut fetch = |_: &MsgRef| { fetched += 1; Ok(b"Subject: x\n\ny\n".to_vec()) };
        let mut out = vec![];
        let o = export_mbox(&refs, &mut fetch, &mut out, &cancel, &mut |done, _| if done == 1 { cancel.store(true, Ordering::Relaxed) }).unwrap();
        assert!(o.cancelled);
        assert_eq!((o.exported, fetched), (1, 1));
        assert_eq!(summary(&o, "x"), "Export cancelled");
    }

    #[test]
    fn a_failing_writer_ends_the_export() {
        struct Full;
        impl Write for Full { fn write(&mut self, _: &[u8]) -> std::io::Result<usize> { Err(std::io::Error::other("disk full")) } fn flush(&mut self) -> std::io::Result<()> { Ok(()) } }
        let refs = vec![msgs().remove(0).0];
        let mut fetch = |_: &MsgRef| Ok(b"Subject: x\n\ny\n".to_vec());
        let e = export_mbox(&refs, &mut fetch, &mut Full, &AtomicBool::new(false), &mut |_, _| {}).unwrap_err();
        assert!(e.contains("disk full"), "{e}");
    }

    fn mb(id: &str, name: &str, parent: Option<&str>, role: Option<&str>, order: i64) -> Value {
        json!({"id": id, "name": name, "parentId": parent, "role": role, "sortOrder": order})
    }

    #[test]
    fn plan_lists_parents_first_in_role_order_and_numbers_twins() {
        let boxes = vec![
            mb("t", "Trash", None, Some("trash"), 9),
            mb("z", "Zeta", None, None, 5),
            mb("i", "Inbox", None, Some("inbox"), 1),
            mb("c", "Kids", Some("z"), None, 1),
            mb("a", "a/b", None, None, 2),
            mb("a2", "A/B", None, None, 2),
            mb("o", "Orphan", Some("gone"), None, 7),
        ];
        let plan: Vec<(String, Vec<String>)> = folder_plan(&boxes).into_iter().map(|f| (f.id, f.path)).collect();
        let paths: Vec<String> = plan.iter().map(|(_, p)| p.join("/")).collect();
        assert_eq!(paths, ["Inbox", "Trash", "a b", "A B (2)", "Zeta", "Zeta/Kids", "Orphan"]);
        assert!(folder_plan(&[]).is_empty());
    }

    #[test]
    fn plan_names_are_safe_components() {
        let p = folder_plan(&[mb("1", "CON", None, None, 0), mb("2", "..", None, None, 1), mb("3", "", None, None, 2)]);
        for f in &p { assert!(f.path.iter().all(|c| !c.contains(['/', '\\', ':']) && !c.starts_with('.') && !c.is_empty()), "{f:?}"); }
        assert_eq!(p[0].path, ["_CON"]);
    }

    #[test]
    fn plan_survives_a_parent_cycle() {
        let p = folder_plan(&[mb("a", "A", Some("b"), None, 0), mb("b", "B", Some("a"), None, 0)]);
        assert!(p.is_empty());
    }

    fn r(subject: &str) -> MsgRef { MsgRef { blob_id: subject.into(), keywords: vec![], received_at: None, subject: subject.into() } }

    #[test]
    fn tree_writes_one_mbox_per_folder_with_messages() {
        let root = std::env::temp_dir().join(format!("barbican-export-tree-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir_all(&root).unwrap();
        let f = |id: &str, p: &[&str]| PlanFolder { id: id.into(), path: p.iter().map(|s| s.to_string()).collect() };
        let plan = vec![(f("1", &["Inbox"]), vec![r("a"), r("b")]), (f("2", &["Inbox", "Kids"]), vec![r("c")]), (f("3", &["Empty"]), vec![])];
        let mut fetch = |m: &MsgRef| Ok(format!("Subject: {}

body
", m.subject).into_bytes());
        let mut seen = vec![];
        let o = export_tree(&plan, &root, true, &mut fetch, &AtomicBool::new(false), &mut |d, t| seen.push((d, t))).unwrap();
        assert_eq!((o.exported, o.failed, o.cancelled), (3, 0, false));
        assert_eq!(seen.last(), Some(&(3, 3)));
        assert_eq!(items_of("Inbox.mbox", &std::fs::read(root.join("Inbox.mbox")).unwrap()).len(), 2);
        assert_eq!(items_of("Kids.mbox", &std::fs::read(root.join("Inbox").join("Kids.mbox")).unwrap()).len(), 1);
        assert!(!root.join("Empty.mbox").exists());
        assert!(!root.join("Inbox.mbox.part").exists());
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn tree_as_eml_keeps_empty_folders_and_cancel_leaves_no_part() {
        let root = std::env::temp_dir().join(format!("barbican-export-tree-eml-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir_all(&root).unwrap();
        let f = |id: &str, p: &[&str]| PlanFolder { id: id.into(), path: p.iter().map(|s| s.to_string()).collect() };
        let plan = vec![(f("1", &["Inbox"]), vec![r("a")]), (f("3", &["Empty"]), vec![])];
        let mut fetch = |_: &MsgRef| Ok(b"Subject: x

y
".to_vec());
        let o = export_tree(&plan, &root, false, &mut fetch, &AtomicBool::new(false), &mut |_, _| {}).unwrap();
        assert_eq!(o.exported, 1);
        assert!(root.join("Inbox").join("00001 a.eml").exists());
        assert!(root.join("Empty").is_dir());
        // a cancel during the first mbox removes its .part and stops before the next folder
        let cancel = AtomicBool::new(false);
        let plan = vec![(f("1", &["One"]), vec![r("a"), r("b")]), (f("2", &["Two"]), vec![r("c")])];
        let o = export_tree(&plan, &root, true, &mut fetch, &cancel, &mut |d, _| if d == 1 { cancel.store(true, Ordering::Relaxed) }).unwrap();
        assert!(o.cancelled);
        assert!(!root.join("One.mbox").exists() && !root.join("One.mbox.part").exists() && !root.join("Two.mbox").exists());
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn a_folder_named_like_another_ones_mbox_file_is_renamed() {
        let p = folder_plan(&[mb("1", "Inbox", None, None, 0), mb("2", "Inbox.mbox", None, None, 1)]);
        assert_eq!(p[0].path, ["Inbox"]);
        assert_eq!(p[1].path, ["Inbox.mbox (2)"]);
    }
}
