//! Files depth (ROADMAP Phase 4): download as zip (G-017, Bulwark lib/files-zip.ts), the
//! upload conflict question (G-016, lib/files-upload-conflicts.ts and
//! components/files/upload-conflict-dialog.tsx) and a new text file (G-018,
//! components/files/new-file-dialog.tsx), with the server's naming rules
//! (lib/file-name-rules.ts). The pure parts come first and are tested; the flows below them
//! drive ui/files-dialogs.slint (`FilesX`). Node helpers and the view are in src/files.rs.
use crate::files::{acc_of, is_dir, key, mime, parent_key, raw_id};
use crate::jmap::Client;
use crate::{AppWindow, FilesX};
use serde_json::{json, Value};
use slint::ComponentHandle;
use std::cell::RefCell;
use std::collections::{HashMap, HashSet};
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;

const CAP: &str = "urn:ietf:params:jmap:filenode";

// ---------------------------------------------------------------- names

/// A name not taken among `used`: "name (1).ext", then (2)… (Bulwark getUniqueName,
/// stores/file-store.ts:526-534; uploads, Keep both).
pub fn numbered_name(name: &str, used: &HashSet<String>) -> String {
    if !used.contains(name) { return name.to_string(); }
    let (base, ext) = match name.rfind('.') { Some(i) if i > 0 => (&name[..i], &name[i..]), _ => (name, "") };
    (1..).map(|i| format!("{base} ({i}){ext}")).find(|n| !used.contains(n)).unwrap_or_default()
}

/// Bulwark DEFAULT_BASE_NAME and the dialog's first value (new-file-dialog.tsx:13, 53).
pub const NEW_FILE_BASE: &str = "new-file";
pub fn default_new_file_name() -> String { format!("{NEW_FILE_BASE}.txt") }

/// The naming rules a server publishes in its filenode capability (Stalwart 0.16.6+;
/// lib/file-name-rules.ts:17-45).
#[derive(Clone, Debug, PartialEq)]
pub struct NameRules {
    pub forbidden_chars: String,
    pub forbidden_names: Vec<String>,
    /// Longest name in bytes of UTF-8.
    pub max_bytes: usize,
}

pub fn rules_from(cap: &Value) -> Option<NameRules> {
    if !cap.is_object() { return None; }
    let forbidden_chars = cap["forbiddenNameChars"].as_str().unwrap_or("").to_string();
    let forbidden_names: Vec<String> = cap["forbiddenNodeNames"].as_array().map(|a| a.iter().filter_map(|n| n.as_str().map(String::from)).collect()).unwrap_or_default();
    if forbidden_chars.is_empty() && forbidden_names.is_empty() { return None; }
    let max_bytes = cap["maxSizeFileNodeName"].as_u64().filter(|n| *n > 0).map(|n| n as usize).unwrap_or(255);
    Some(NameRules { forbidden_chars, forbidden_names, max_bytes })
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub enum NameProblem { Chars(String), Control, TooLong, Reserved }

fn is_control(c: char) -> bool { (c as u32) <= 0x1f || (0x7f..=0x9f).contains(&(c as u32)) }

fn stem(name: &str) -> &str { match name.find('.') { Some(i) if i > 0 => &name[..i], _ => name } }

fn reserved(name: &str, names: &[String]) -> bool {
    let (whole, st) = (name.to_ascii_lowercase(), stem(name).to_ascii_lowercase());
    names.iter().any(|n| { let n = n.to_ascii_lowercase(); n == whole || n == st })
}

/// What the server would refuse in `name` (Bulwark fileNameProblem); None without rules.
pub fn name_problem(name: &str, rules: Option<&NameRules>) -> Option<NameProblem> {
    let r = rules?;
    if name.len() > r.max_bytes { return Some(NameProblem::TooLong); }
    let mut bad: Vec<char> = vec![];
    for c in name.chars() { if r.forbidden_chars.contains(c) && !bad.contains(&c) { bad.push(c); } }
    if !bad.is_empty() { return Some(NameProblem::Chars(bad.iter().map(|c| c.to_string()).collect::<Vec<_>>().join(" "))); }
    if name.chars().any(is_control) { return Some(NameProblem::Control); }
    if reserved(name, &r.forbidden_names) { return Some(NameProblem::Reserved); }
    None
}

/// The message under the name field (Bulwark files.name_* strings).
pub fn problem_text(p: &NameProblem) -> String {
    match p {
        NameProblem::Chars(c) => format!("The server does not allow these characters in names: {c}"),
        NameProblem::Control => "Names cannot contain control characters.".into(),
        NameProblem::TooLong => "This name is too long for the server.".into(),
        NameProblem::Reserved => "This name is reserved on the server.".into(),
    }
}

/// The new-file dialog's error for what is typed: empty says nothing, Create just waits.
pub fn new_file_error(typed: &str, rules: Option<&NameRules>) -> String {
    let t = typed.trim();
    if t.is_empty() { return String::new(); }
    name_problem(t, rules).map(|p| problem_text(&p)).unwrap_or_default()
}

// ---------------------------------------------------------------- zip plan

/// One file of a zip: where it goes inside, and what to fetch.
#[derive(Clone, Debug, PartialEq)]
pub struct ZipFile {
    pub path: String,
    pub acc: String,
    pub blob: String,
    pub name: String,
    pub mime: String,
    pub size: u64,
    pub modified: Option<String>,
}

#[derive(Clone, Debug, Default, PartialEq)]
pub struct ZipPlan {
    pub files: Vec<ZipFile>,
    /// Folders with nothing in them, kept so the zip has the same shape.
    pub empty_folders: Vec<String>,
    /// Bytes to fetch, for the progress.
    pub total_bytes: u64,
}

/// A node neither folder nor file (a Stalwart 1.0 symlink): nothing to pack.
fn is_other(n: &Value) -> bool {
    match (n["nodeType"].as_str(), n["@type"].as_str()) {
        (Some(t), _) => t != "directory" && t != "file",
        (None, Some(t)) => t != "Directory" && t != "File",
        _ => false,
    }
}

/// A name as one segment of a path inside the zip: no separator (Bulwark replaces / and \ with
/// "_"), and no "." or ".." segment or control character an unzip tool could trip on.
pub fn zip_segment(name: &str) -> String {
    let s: String = name.chars().map(|c| if c == '/' || c == '\\' || is_control(c) { '_' } else { c }).collect();
    // "", ".", "..", "  ." and the like: what Windows would trim to nothing
    if s.trim_end_matches(['.', ' ']).is_empty() { "_".into() } else { s }
}

/// What goes into the zip of `keys`: files as they are, folders with everything below them,
/// in the order given and then the order of the listing (Bulwark planZip, files-zip.ts:26-76).
/// A taken path is numbered "name (1).ext".
pub fn plan_zip(nodes: &[Value], keys: &[String]) -> ZipPlan {
    let mut by_parent: HashMap<String, Vec<&Value>> = HashMap::new();
    for n in nodes { by_parent.entry(parent_key(n)).or_default().push(n); }
    let mut plan = ZipPlan::default();
    let mut used: HashSet<String> = HashSet::new();
    let mut seen: HashSet<String> = HashSet::new();
    fn unique(used: &mut HashSet<String>, path: String) -> String {
        if used.insert(path.clone()) { return path; }
        let (dir, name) = match path.rfind('/') { Some(i) => (&path[..=i], &path[i + 1..]), None => ("", path.as_str()) };
        let (base, ext) = match name.rfind('.') { Some(i) if i > 0 => (&name[..i], &name[i..]), _ => (name, "") };
        let next = (1..).map(|i| format!("{dir}{base} ({i}){ext}")).find(|p| !used.contains(p)).unwrap_or_default();
        used.insert(next.clone());
        next
    }
    fn add(n: &Value, prefix: &str, by_parent: &HashMap<String, Vec<&Value>>, plan: &mut ZipPlan, used: &mut HashSet<String>, seen: &mut HashSet<String>) {
        if !seen.insert(key(n)) || is_other(n) { return; }
        let path = unique(used, format!("{prefix}{}", zip_segment(n["name"].as_str().unwrap_or(""))));
        if is_dir(n) {
            let kids = by_parent.get(&key(n)).map(|v| v.as_slice()).unwrap_or(&[]);
            if kids.is_empty() { plan.empty_folders.push(path.clone()); }
            for k in kids { add(k, &format!("{path}/"), by_parent, plan, used, seen); }
            return;
        }
        let Some(blob) = n["blobId"].as_str().filter(|b| !b.is_empty()) else { return };
        let size = n["size"].as_u64().unwrap_or(0);
        plan.files.push(ZipFile { path, acc: acc_of(n), blob: blob.into(), name: n["name"].as_str().unwrap_or("file").into(), mime: mime(n), size, modified: n["modified"].as_str().map(String::from) });
        plan.total_bytes += size;
    }
    let by_key: HashMap<&str, &Value> = nodes.iter().filter_map(|n| n["_key"].as_str().map(|k| (k, n))).collect();
    for k in keys {
        if let Some(n) = by_key.get(k.as_str()) { add(n, "", &by_parent, &mut plan, &mut used, &mut seen); }
    }
    plan
}

/// The zip's name: the item's own for one, the folder's for several (file-browser.tsx:635-636).
pub fn zip_name(item_names: &[String], folder_name: &str) -> String {
    let stem = if item_names.len() == 1 { item_names[0].as_str() } else { folder_name };
    format!("{}.zip", if stem.trim().is_empty() { "Files" } else { stem })
}

/// What this writer can't pack (no ZIP64): more than 65535 entries or 4 GB.
pub fn plan_problem(plan: &ZipPlan) -> Option<&'static str> {
    if plan.files.is_empty() && plan.empty_folders.is_empty() { return Some("There is nothing to download."); }
    if plan.files.len() + plan.empty_folders.len() >= 0xFFFF { return Some("Too many files for one zip."); }
    // deflate may grow data that is packed already a little; keep room for that and the headers
    if plan.total_bytes > u32::MAX as u64 - u32::MAX as u64 / 64 { return Some(crate::zip::TOO_BIG); }
    None
}

fn local_time(iso: Option<&str>) -> chrono::NaiveDateTime {
    iso.and_then(|s| chrono::DateTime::parse_from_rfc3339(s).ok()).map(|d| d.with_timezone(&chrono::Local).naive_local())
        .unwrap_or_else(|| chrono::Local::now().naive_local())
}

/// Write the plan's zip into `out`: empty folders first, then each file as `fetch` streams it
/// (Bulwark buildZip, files-zip.ts:86-115). `progress(fraction, current path)`.
pub fn write_zip<W: std::io::Write>(plan: &ZipPlan, out: W, cancel: &AtomicBool,
    fetch: &mut dyn FnMut(&ZipFile, &mut dyn std::io::Write) -> Result<u64, String>,
    progress: &mut dyn FnMut(f32, &str)) -> Result<W, String> {
    let mut z = crate::zip::StreamZip::new(out);
    for f in &plan.empty_folders { z.add_dir(f, chrono::Local::now().naive_local())?; }
    let mut done = 0u64;
    for f in &plan.files {
        if cancel.load(Ordering::Relaxed) { return Err(crate::zip::CANCELLED.into()); }
        progress(if plan.total_bytes > 0 { done as f32 / plan.total_bytes as f32 } else { 0.0 }, &f.path);
        z.add_file(&f.path, local_time(f.modified.as_deref()), Some(cancel), &mut |w| fetch(f, w).map(|_| ()))?;
        done += f.size;
    }
    if cancel.load(Ordering::Relaxed) { return Err(crate::zip::CANCELLED.into()); }
    progress(1.0, "");
    z.finish()
}

// ---------------------------------------------------------------- upload conflicts

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum Kind { File, Folder }

/// A name of an upload that lands right in the target folder: a file, or a folder upload's top.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Conflict { pub name: String, pub kind: Kind }

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Choice { Replace, Keep, Skip }

impl Choice {
    pub fn parse(s: &str) -> Option<Choice> { match s { "replace" => Some(Choice::Replace), "keep" => Some(Choice::Keep), "skip" => Some(Choice::Skip), _ => None } }
}

/// What in the folder an upload meets: (name, is a folder, raw id). Symlinks are left out:
/// their name is taken, but they can't be replaced (file-store.ts:508-509).
pub fn existing_entries(children: &[Value]) -> Vec<(String, bool, String)> {
    children.iter().filter(|n| !is_other(n)).map(|n| (n["name"].as_str().unwrap_or("").to_string(), is_dir(n), raw_id(n))).collect()
}

/// What of `incoming` clashes with `existing`, in order; only a file over a file and a folder
/// over a folder, each name once (Bulwark findUploadConflicts).
pub fn find_conflicts(incoming: &[Conflict], existing: &[(String, bool, String)]) -> Vec<Conflict> {
    let mut seen = HashSet::new();
    let mut out = vec![];
    for c in incoming {
        if !seen.insert((c.kind, c.name.clone())) { continue; }
        if existing.iter().any(|(n, dir, _)| *n == c.name && *dir == (c.kind == Kind::Folder)) { out.push(c.clone()); }
    }
    out
}

/// The questions, one at a time; "Do this for all" answers the rest (Bulwark
/// resolveConflictsInTurn).
#[derive(Clone, Debug, Default)]
pub struct Asking {
    pub conflicts: Vec<Conflict>,
    pub at: usize,
    pub choices: HashMap<String, Choice>,
}

impl Asking {
    pub fn new(conflicts: Vec<Conflict>) -> Asking { Asking { conflicts, ..Default::default() } }
    /// The open question and how many are left, this one included.
    pub fn current(&self) -> Option<(&Conflict, usize)> { self.conflicts.get(self.at).map(|c| (c, self.conflicts.len() - self.at)) }
    pub fn answer(&mut self, choice: Choice, all: bool) {
        if all {
            for c in &self.conflicts[self.at.min(self.conflicts.len())..] { self.choices.insert(c.name.clone(), choice); }
            self.at = self.conflicts.len();
        } else if let Some(c) = self.conflicts.get(self.at) {
            self.choices.insert(c.name.clone(), choice);
            self.at += 1;
        }
    }
    pub fn done(&self) -> bool { self.at >= self.conflicts.len() }
}

/// What an upload does with one file.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Step {
    /// A new node of this name (numbered when the name is taken: Keep both, or no one asked).
    Create(String),
    /// New content for the node there (raw id): same id, same shares.
    Replace(String),
    Skip,
}

/// Every name taken in a folder, a symlink's included (a new node never takes it).
pub fn taken_names(children: &[Value]) -> HashSet<String> {
    children.iter().filter_map(|n| n["name"].as_str().map(String::from)).collect()
}

/// The steps of a file upload into a folder holding `existing`, whose names are `taken`
/// (file-store.ts:1158-1182).
pub fn plan_file_uploads(names: &[String], existing: &[(String, bool, String)], taken: &HashSet<String>, choices: &HashMap<String, Choice>) -> Vec<Step> {
    let replace_ids: HashMap<&str, &str> = existing.iter().filter(|e| !e.1).map(|e| (e.0.as_str(), e.2.as_str())).collect();
    let mut used: HashSet<String> = taken.clone();
    used.extend(existing.iter().map(|e| e.0.clone()));
    names.iter().map(|n| match choices.get(n) {
        Some(Choice::Skip) => Step::Skip,
        Some(Choice::Replace) if replace_ids.contains_key(n.as_str()) => Step::Replace(replace_ids[n.as_str()].to_string()),
        _ => { let u = numbered_name(n, &used); used.insert(u.clone()); Step::Create(u) }
    }).collect()
}

/// The `FileNode/set` arguments giving node `id` new content (Bulwark replaceFileNodeBlob,
/// lib/jmap/client.ts:11178-11199): `modified: null` has the server stamp the change.
pub fn replace_args(acc: &str, id: &str, blob: &str) -> Value {
    json!({"accountId": acc, "update": { id: {"blobId": blob, "modified": null} }})
}

pub fn replace_blob(c: &Client, acc: &str, id: &str, blob: &str) -> Result<(), String> {
    let r = c.call(json!([["FileNode/set", replace_args(acc, id, blob), "r"]]))?;
    if r[0][0] == "error" { return Err(crate::jmap::method_error(&r[0][1])); }
    if let Some(e) = r[0][1]["notUpdated"].get(id) { return Err(crate::jmap::set_error(e)); }
    Ok(())
}

/// The children of every folder of `acc` by (parent raw id, name, is a folder), for a merge.
pub type ChildIndex = HashMap<(String, String, bool), String>;

pub fn child_index(nodes: &[Value], acc: &str) -> ChildIndex {
    nodes.iter().filter(|n| acc_of(n) == acc && !is_other(n))
        .filter_map(|n| Some(((n["parentId"].as_str().unwrap_or("").to_string(), n["name"].as_str()?.to_string(), is_dir(n)), raw_id(n))))
        .collect()
}

/// The id of the child of `parent` (raw id, "" the root) named `name` of a kind.
pub fn child_named(ix: &ChildIndex, parent: &str, name: &str, dir: bool) -> Option<String> {
    ix.get(&(parent.to_string(), name.to_string(), dir)).cloned()
}

/// Files below a folder on disk (links not followed): what a skipped folder upload leaves out.
pub fn count_files(root: &std::path::Path) -> usize {
    let mut n = 0;
    let mut stack = vec![root.to_path_buf()];
    while let Some(d) = stack.pop() {
        let Ok(rd) = std::fs::read_dir(&d) else { continue };
        for e in rd.flatten() {
            let Ok(ft) = e.file_type() else { continue };
            if ft.is_symlink() { continue; }
            if ft.is_dir() { stack.push(e.path()); } else { n += 1; }
        }
    }
    n
}

// ---------------------------------------------------------------- flows

/// An upload waiting for its conflicts to be answered.
enum Job {
    Files(Vec<PathBuf>),
    Folder(PathBuf),
}

struct Pending { c: Arc<Client>, job: Job, cwd: String, asking: Asking }

thread_local! {
    static PENDING: RefCell<Option<Pending>> = const { RefCell::new(None) };
    /// The new-file dialog's folder and the server's naming rules there.
    static NEWFILE: RefCell<Option<(Arc<Client>, String, Option<NameRules>)>> = const { RefCell::new(None) };
}

static ZIP_RUNNING: AtomicBool = AtomicBool::new(false);
static ZIP_CANCEL: AtomicBool = AtomicBool::new(false);

fn no_trash(u: &AppWindow, cwd: &str) -> bool {
    if cwd == crate::files::TRASH_LOC { crate::mail::show_toast(u, "Nothing can be added to the trash.", false); return false; }
    true
}

fn file_name(p: &std::path::Path, fallback: &str) -> String { p.file_name().map(|n| n.to_string_lossy().to_string()).unwrap_or_else(|| fallback.into()) }

/// Upload files into the folder on screen, asking first about each name that is taken.
pub fn upload_files(u: &AppWindow, c: Arc<Client>) {
    let cwd = crate::files::cwd();
    if !no_trash(u, &cwd) { return; }
    let Some(files) = crate::automation::pick_files() else { return };
    let incoming: Vec<Conflict> = files.iter().map(|f| Conflict { name: file_name(f, "file"), kind: Kind::File }).collect();
    start(u, c, Job::Files(files), cwd, &incoming);
}

/// Upload a folder from disk with everything in it (Bulwark uploadFolder); a folder of the
/// same name asks first: Replace merges into it.
pub fn upload_folder(u: &AppWindow, c: Arc<Client>) {
    let cwd = crate::files::cwd();
    if !no_trash(u, &cwd) { return; }
    let Some(root) = rfd::FileDialog::new().pick_folder() else { return };
    let incoming = [Conflict { name: file_name(&root, "Folder"), kind: Kind::Folder }];
    start(u, c, Job::Folder(root), cwd, &incoming);
}

fn start(u: &AppWindow, c: Arc<Client>, job: Job, cwd: String, incoming: &[Conflict]) {
    let conflicts = find_conflicts(incoming, &existing_entries(&crate::files::children(&cwd)));
    if conflicts.is_empty() { run(u, c, job, cwd, HashMap::new()); return; }
    PENDING.with(|p| *p.borrow_mut() = Some(Pending { c, job, cwd, asking: Asking::new(conflicts) }));
    show_question(u);
}

fn show_question(u: &AppWindow) {
    let x = u.global::<FilesX>();
    let q = PENDING.with(|p| p.borrow().as_ref().and_then(|p| p.asking.current().map(|(c, left)| (c.clone(), left))));
    match q {
        Some((c, left)) => {
            x.set_conflict_name(c.name.into());
            x.set_conflict_folder(c.kind == Kind::Folder);
            x.set_conflict_remaining(left as i32);
            // each question starts unticked
            x.set_conflict_all(false);
            x.set_conflict_open(true);
        }
        None => x.set_conflict_open(false),
    }
}

/// The dialog's answer: a choice (and whether it covers the rest), or "" to call it all off.
pub fn answer(u: &AppWindow, choice: &str, all: bool) {
    let Some(choice) = Choice::parse(choice) else {
        PENDING.with(|p| p.borrow_mut().take());
        u.global::<FilesX>().set_conflict_open(false);
        crate::mail::show_toast(u, "Upload cancelled", false);
        return;
    };
    let finished = PENDING.with(|p| {
        let mut p = p.borrow_mut();
        let Some(pd) = p.as_mut() else { return None };
        pd.asking.answer(choice, all);
        if pd.asking.done() { p.take() } else { None }
    });
    match finished {
        Some(pd) => { u.global::<FilesX>().set_conflict_open(false); run(u, pd.c, pd.job, pd.cwd, pd.asking.choices); }
        None => show_question(u),
    }
}

fn report(done: usize, skipped: usize, failed: &[String]) -> String {
    let mut msg = match done { 1 => "Uploaded 1 file".to_string(), n => format!("Uploaded {n} files") };
    if skipped > 0 { msg.push_str(&format!("; {skipped} skipped")); }
    if let Some(f) = failed.first() { msg.push_str(&format!("; {} failed ({f})", failed.len())); }
    msg
}

fn run(u: &AppWindow, c: Arc<Client>, job: Job, cwd: String, choices: HashMap<String, Choice>) {
    let draft = crate::files::draft18();
    let kids = crate::files::children(&cwd);
    let (existing, taken) = (existing_entries(&kids), taken_names(&kids));
    let (acc, parent) = crate::files::target(&cwd);
    // a folder merged into another looks up what is there already
    let ix = match &job {
        Job::Folder(root) if choices.get(&file_name(root, "Folder")) == Some(&Choice::Replace) => crate::files::with_nodes(|n| child_index(n, &acc)),
        _ => ChildIndex::new(),
    };
    let w = u.as_weak();
    crate::mail::show_toast(u, if matches!(job, Job::Folder(_)) { "Uploading folder…" } else { "Uploading…" }, false);
    std::thread::spawn(move || {
        let (mut done, mut skipped, mut failed) = (0usize, 0usize, vec![]);
        let put = |path: &std::path::Path, name: &str| -> Result<String, String> {
            let t = crate::compose::mime_for(name);
            std::fs::read(path).map_err(|e| e.to_string()).and_then(|b| c.upload_to(&acc, b, t)).map(|(blob, _)| blob)
        };
        let res: Result<(), String> = (|| {
            match job {
                Job::Files(files) => {
                    let names: Vec<String> = files.iter().map(|f| file_name(f, "file")).collect();
                    for ((f, name), step) in files.iter().zip(&names).zip(plan_file_uploads(&names, &existing, &taken, &choices)) {
                        // one file that fails is noted and the rest go on (D-M5-C06)
                        let r = match step {
                            Step::Skip => { skipped += 1; continue; }
                            Step::Replace(id) => put(f, name).and_then(|blob| replace_blob(&c, &acc, &id, &blob)),
                            Step::Create(new) => put(f, name).and_then(|blob| crate::files::create_in(&c, draft, &acc, parent.as_deref(), &new, Some((&blob, crate::compose::mime_for(name)))).map(|_| ())),
                        };
                        match r { Ok(()) => done += 1, Err(e) => failed.push(format!("{name}: {e}")) }
                    }
                }
                Job::Folder(root) => {
                    let top = file_name(&root, "Folder");
                    let existing_dir = existing.iter().find(|e| e.1 && e.0 == top).map(|e| e.2.clone());
                    let (top_id, merged) = match (choices.get(&top), existing_dir) {
                        (Some(Choice::Skip), _) => { skipped += count_files(&root); return Ok(()); }
                        (Some(Choice::Replace), Some(id)) => (id, true),
                        _ => (crate::files::create_in(&c, draft, &acc, parent.as_deref(), &numbered_name(&top, &taken), None)?, false),
                    };
                    // breadth-first: each directory is there before what is inside it; a merged
                    // folder reuses its subfolders and replaces files of the same name
                    let mut queue = vec![(root.clone(), top_id, merged)];
                    while let Some((dir, id, merged)) = queue.pop() {
                        let mut entries: Vec<_> = std::fs::read_dir(&dir).map_err(|e| e.to_string())?.filter_map(|e| e.ok()).collect();
                        entries.sort_by_key(|e| e.file_name());
                        for e in entries {
                            let name = e.file_name().to_string_lossy().to_string();
                            let Ok(ft) = e.file_type() else { continue };
                            if ft.is_symlink() { continue; }
                            let there = |d: bool| if merged { child_named(&ix, &id, &name, d) } else { None };
                            if ft.is_dir() {
                                match there(true).map(|x| Ok((x, true))).unwrap_or_else(|| crate::files::create_in(&c, draft, &acc, Some(&id), &name, None).map(|x| (x, false))) {
                                    Ok((sub, m)) => queue.push((e.path(), sub, m)),
                                    Err(err) => failed.push(format!("{name}: {err}")),
                                }
                            } else {
                                let t = crate::compose::mime_for(&name);
                                let r = put(&e.path(), &name).and_then(|blob| match there(false) {
                                    Some(old) => replace_blob(&c, &acc, &old, &blob),
                                    None => crate::files::create_in(&c, draft, &acc, Some(&id), &name, Some((&blob, t))).map(|_| ()),
                                });
                                match r { Ok(()) => done += 1, Err(err) => failed.push(format!("{name}: {err}")) }
                            }
                        }
                    }
                }
            }
            Ok(())
        })();
        let msg = match res { Err(e) => format!("Upload failed: {e}"), Ok(()) => report(done, skipped, &failed) };
        let _ = w.upgrade_in_event_loop(move |u| { crate::mail::show_toast(&u, &msg, false); refresh_after(&u, c); });
    });
}

/// A part file beside `target` that is not there yet: "<target>.part", then "<target>.1.part"…
/// An existing file of that name is never truncated or removed.
pub fn create_part(target: &std::path::Path) -> std::io::Result<(PathBuf, std::fs::File)> {
    for i in 0..1000 {
        let mut p = target.as_os_str().to_owned();
        if i == 0 { p.push(".part"); } else { p.push(format!(".{i}.part")); }
        let p = PathBuf::from(p);
        match std::fs::OpenOptions::new().write(true).create_new(true).open(&p) {
            Ok(f) => return Ok((p, f)),
            Err(e) if e.kind() == std::io::ErrorKind::AlreadyExists => continue,
            Err(e) => return Err(e),
        }
    }
    Err(std::io::Error::new(std::io::ErrorKind::AlreadyExists, "no free name for the part file"))
}

/// Removes the part file unless the zip was moved into place (a failure, a cancel, a panic).
struct PartGuard { part: PathBuf, keep: bool }
impl Drop for PartGuard { fn drop(&mut self) { if !self.keep { let _ = std::fs::remove_file(&self.part); } } }

/// Write the plan's zip to `target`: into a part file beside it first, moved over it only once
/// complete, so a cancel or a failure never leaves half a zip nor destroys a file the user
/// chose to replace.
pub fn save_zip(plan: &ZipPlan, target: &std::path::Path, cancel: &AtomicBool,
    fetch: &mut dyn FnMut(&ZipFile, &mut dyn std::io::Write) -> Result<u64, String>,
    progress: &mut dyn FnMut(f32, &str)) -> Result<(), String> {
    let (part, file) = create_part(target).map_err(|e| format!("Could not create the file: {e}"))?;
    let mut guard = PartGuard { part, keep: false };
    let out = write_zip(plan, std::io::BufWriter::with_capacity(1 << 20, file), cancel, fetch, progress)?;
    let file = out.into_inner().map_err(|e| e.to_string())?;
    file.sync_all().map_err(|e| e.to_string())?;
    drop(file);
    std::fs::rename(&guard.part, target).map_err(|e| format!("Could not write the file: {e}"))?;
    guard.keep = true;
    Ok(())
}

/// However the zip thread ends (a panic included), the card closes and the next zip may start,
/// both on the UI thread after anything the thread queued before.
struct ZipDone { w: slint::Weak<AppWindow>, msg: Option<(String, bool)> }
impl Drop for ZipDone {
    fn drop(&mut self) {
        let msg = self.msg.take().unwrap_or_else(|| ("Couldn't download the zip.".into(), true));
        let r = self.w.upgrade_in_event_loop(move |u| {
            u.global::<FilesX>().set_zip_open(false);
            ZIP_RUNNING.store(false, Ordering::SeqCst);
            if msg.1 { crate::mail::show_error(&u, &msg.0) } else { crate::mail::show_toast(&u, &msg.0, false) }
        });
        // no event loop any more: nothing to show, but never stay "running"
        if r.is_err() { ZIP_RUNNING.store(false, Ordering::SeqCst); }
    }
}

/// Download nodes (folders with everything below them) as one zip, written to disk as it is
/// fetched, with a progress card that can cancel it (Bulwark downloadZip / handleDownloadZip).
pub fn download_zip(u: &AppWindow, c: Arc<Client>, keys: Vec<String>, zip_name: String) {
    if ZIP_RUNNING.load(Ordering::SeqCst) { crate::mail::show_toast(u, "A zip download is already running.", false); return; }
    let plan = crate::files::with_nodes(|n| plan_zip(n, &keys));
    if let Some(p) = plan_problem(&plan) { crate::mail::show_error(u, &format!("Couldn't download {zip_name}: {p}")); return; }
    let Some(path) = rfd::FileDialog::new().set_file_name(crate::sanitize_name(&zip_name)).add_filter("Zip", &["zip"]).save_file() else { return };
    ZIP_RUNNING.store(true, Ordering::SeqCst);
    ZIP_CANCEL.store(false, Ordering::SeqCst);
    let x = u.global::<FilesX>();
    x.set_zip_title(format!("Preparing {zip_name}").into());
    x.set_zip_current("".into());
    x.set_zip_progress(0.0);
    x.set_zip_open(true);
    let w = u.as_weak();
    std::thread::spawn(move || {
        let mut done = ZipDone { w: w.clone(), msg: None };
        let mut last = std::time::Instant::now() - std::time::Duration::from_secs(1);
        let mut progress = move |p: f32, cur: &str| {
            if p < 1.0 && last.elapsed() < std::time::Duration::from_millis(150) { return; }
            last = std::time::Instant::now();
            let cur = cur.to_string();
            let _ = w.upgrade_in_event_loop(move |u| { let x = u.global::<FilesX>(); x.set_zip_progress(p); x.set_zip_current(cur.into()); });
        };
        let mut fetch = |f: &ZipFile, out: &mut dyn std::io::Write| c.download_to(&f.acc, &f.blob, &f.name, &f.mime, out);
        let res = save_zip(&plan, &path, &ZIP_CANCEL, &mut fetch, &mut progress);
        done.msg = Some(match res {
            Ok(()) => (format!("Saved {zip_name}"), false),
            Err(_) if ZIP_CANCEL.load(Ordering::SeqCst) => ("Download cancelled".into(), false),
            Err(e) => (format!("Couldn't download {zip_name}: {e}"), true),
        });
    });
}

/// "Download as zip" for a folder, or for everything in the folder on screen (`keys` empty).
pub fn download_zip_of(u: &AppWindow, c: Arc<Client>, keys: &[String]) {
    let (keys, names, folder) = if keys.is_empty() {
        let kids = crate::files::children(&crate::files::cwd());
        let names = u.get_crumb_names();
        let folder = slint::Model::row_count(&names).checked_sub(1).and_then(|i| slint::Model::row_data(&names, i)).map(|s| s.to_string()).unwrap_or_default();
        (kids.iter().map(key).collect::<Vec<_>>(), kids.iter().map(|n| n["name"].as_str().unwrap_or("").to_string()).collect::<Vec<_>>(), folder)
    } else {
        let names = crate::files::with_nodes(|nodes| {
            let want: HashSet<&str> = keys.iter().map(String::as_str).collect();
            nodes.iter().filter(|n| n["_key"].as_str().is_some_and(|k| want.contains(k))).map(|n| n["name"].as_str().unwrap_or("").to_string()).collect::<Vec<_>>()
        });
        (keys.to_vec(), names, String::new())
    };
    if names.is_empty() { crate::mail::show_toast(u, "There is nothing to download.", false); return; }
    download_zip(u, c, keys, zip_name(&names, &folder));
}

pub fn cancel_zip() { ZIP_CANCEL.store(true, Ordering::SeqCst); }

/// Open "New text file" for the folder on screen.
pub fn open_new_file(u: &AppWindow, c: Arc<Client>) {
    let cwd = crate::files::cwd();
    if !no_trash(u, &cwd) { return; }
    let (acc, _) = crate::files::target(&cwd);
    // a shared account may publish no rules of its own: the user's own files account's then
    let rules = rules_from(&c.session["accounts"][&acc]["accountCapabilities"][CAP])
        .or_else(|| rules_from(&c.session["accounts"][&c.account_for(CAP)]["accountCapabilities"][CAP]));
    NEWFILE.with(|n| *n.borrow_mut() = Some((c, cwd, rules)));
    let x = u.global::<FilesX>();
    x.set_newfile_name(default_new_file_name().into());
    x.set_newfile_error("".into());
    x.set_newfile_ok(true);
    x.set_newfile_open(true);
}

pub fn new_file_edited(u: &AppWindow, typed: &str) {
    let err = NEWFILE.with(|n| n.borrow().as_ref().map(|(_, _, r)| new_file_error(typed, r.as_ref()))).unwrap_or_default();
    let x = u.global::<FilesX>();
    x.set_newfile_ok(err.is_empty() && !typed.trim().is_empty());
    x.set_newfile_error(err.into());
}

/// Create the empty text file (Bulwark createFile: an empty text/plain blob, then the node);
/// a name taken in the folder gets a number, like an upload kept beside another.
pub fn new_file_create(u: &AppWindow, typed: &str) {
    let name = typed.trim().to_string();
    let Some((c, cwd, rules)) = NEWFILE.with(|n| n.borrow().clone()) else { return };
    if name.is_empty() { return; }
    if let Some(p) = name_problem(&name, rules.as_ref()) { u.global::<FilesX>().set_newfile_error(problem_text(&p).into()); return; }
    NEWFILE.with(|n| n.borrow_mut().take());
    u.global::<FilesX>().set_newfile_open(false);
    let name = numbered_name(&name, &taken_names(&crate::files::children(&cwd)));
    let draft = crate::files::draft18();
    let (acc, parent) = crate::files::target(&cwd);
    let w = u.as_weak();
    std::thread::spawn(move || {
        let r = c.upload_to(&acc, vec![], "text/plain").and_then(|(blob, _)| create_text_node(&c, draft, &acc, parent.as_deref(), &name, &blob));
        let _ = w.upgrade_in_event_loop(move |u| {
            match r { Ok(_) => crate::mail::show_toast(&u, "File created", false), Err(e) => crate::mail::show_error(&u, &format!("Couldn't create the file: {e}")) }
            refresh_after(&u, c);
        });
    });
}

/// Read the files again once an upload or a new file is done, in whatever folder is on screen
/// now (the one it went into, or the one the user went to meanwhile): never a jump back.
fn refresh_after(u: &AppWindow, c: Arc<Client>) { crate::files::reload(u, c); }

/// The node of a new text file, its blob uploaded already.
pub fn create_text_node(c: &Client, draft: bool, acc: &str, parent: Option<&str>, name: &str, blob: &str) -> Result<String, String> {
    crate::files::create_in(c, draft, acc, parent, name, Some((blob, "text/plain")))
}

/// The dialogs' callbacks.
pub fn wire(ui: &AppWindow) {
    let x = ui.global::<FilesX>();
    let w = ui.as_weak();
    x.on_conflict_answer(move |c, all| { if let Some(u) = w.upgrade() { answer(&u, &c, all); } });
    x.on_zip_cancel(cancel_zip);
    let w = ui.as_weak();
    x.on_newfile_edited(move |t| { if let Some(u) = w.upgrade() { new_file_edited(&u, &t); } });
    let w = ui.as_weak();
    x.on_newfile_create(move |t| { if let Some(u) = w.upgrade() { new_file_create(&u, &t); } });
}

#[cfg(test)]
mod tests {
    use super::*;

    fn n(id: &str, parent: Option<&str>, name: &str, dir: bool, size: u64) -> Value {
        let mut v = json!({"id": id, "parentId": parent, "name": name, "_key": id, "_acc": "A", "_shared": false, "size": size});
        if !dir { v["blobId"] = json!(format!("b-{id}")); v["type"] = json!("text/plain"); }
        v
    }

    #[test]
    fn numbered_names_follow_bulwark() {
        let used: HashSet<String> = ["a.txt", "a (1).txt", "Folder", ".env"].iter().map(|s| s.to_string()).collect();
        assert_eq!(numbered_name("a.txt", &used), "a (2).txt");
        assert_eq!(numbered_name("Folder", &used), "Folder (1)");
        assert_eq!(numbered_name(".env", &used), ".env (1)");
        assert_eq!(numbered_name("b.txt", &used), "b.txt");
        assert_eq!(default_new_file_name(), "new-file.txt");
    }

    #[test]
    fn server_name_rules() {
        assert_eq!(rules_from(&json!({})), None);
        assert_eq!(rules_from(&json!({"forbiddenNameChars": ""})), None);
        let r = rules_from(&json!({"forbiddenNameChars": "/\\:", "forbiddenNodeNames": ["con", ".."], "maxSizeFileNodeName": 10})).unwrap();
        assert_eq!(r.max_bytes, 10);
        assert_eq!(rules_from(&json!({"forbiddenNodeNames": ["x"]})).unwrap().max_bytes, 255);
        let r = Some(&r);
        assert_eq!(name_problem("a:b/c:", r), Some(NameProblem::Chars(": /".into())));
        assert_eq!(name_problem("a\u{7}b", r), Some(NameProblem::Control));
        assert_eq!(name_problem("CON.txt", r), Some(NameProblem::Reserved));
        assert_eq!(name_problem("Con", r), Some(NameProblem::Reserved));
        assert_eq!(name_problem("..", r), Some(NameProblem::Reserved));
        assert_eq!(name_problem("console", r), None);
        assert_eq!(name_problem("ü".repeat(6).as_str(), r), Some(NameProblem::TooLong));
        assert_eq!(name_problem("anything/", None), None);
        assert_eq!(new_file_error("  ", r), "");
        assert_eq!(new_file_error("con.txt", r), "This name is reserved on the server.");
        assert_eq!(new_file_error("a:b", r), "The server does not allow these characters in names: :");
    }

    #[test]
    fn zip_plan_keeps_shape_order_and_unique_paths() {
        let nodes = vec![
            n("d", None, "Taxes", true, 0), n("y", Some("d"), "2025", true, 0), n("r", Some("y"), "return.pdf", false, 100),
            n("e", Some("d"), "Empty", true, 0), n("s", Some("d"), "a/b.txt", false, 5), n("s2", Some("d"), "a_b.txt", false, 7),
            n("t", None, "top.txt", false, 3), n("t2", None, "Taxes", false, 1),
            json!({"id": "l", "parentId": "d", "name": "link", "@type": "Symlink", "_key": "l", "_acc": "A"}),
            json!({"id": "x", "parentId": "d", "name": "..", "_key": "x", "_acc": "A", "blobId": "bx", "size": 2}),
        ];
        let p = plan_zip(&nodes, &["d".into(), "t".into(), "t2".into(), "d".into()]);
        let paths: Vec<&str> = p.files.iter().map(|f| f.path.as_str()).collect();
        assert_eq!(paths, ["Taxes/2025/return.pdf", "Taxes/a_b.txt", "Taxes/a_b (1).txt", "Taxes/_", "top.txt", "Taxes (1)"]);
        assert_eq!(p.empty_folders, ["Taxes/Empty"]);
        assert_eq!(p.total_bytes, 100 + 5 + 7 + 2 + 3 + 1);
        assert_eq!(p.files[0].blob, "b-r");
        assert_eq!(p.files[0].acc, "A");
        assert_eq!(p.files[0].mime, "text/plain");
        assert_eq!(plan_problem(&p), None);
        assert_eq!(plan_problem(&ZipPlan::default()), Some("There is nothing to download."));
        assert_eq!(plan_problem(&ZipPlan { total_bytes: 5_000_000_000, files: p.files.clone(), ..Default::default() }), Some(crate::zip::TOO_BIG));
        assert_eq!(zip_name(&["Taxes".into()], "My files"), "Taxes.zip");
        assert_eq!(zip_name(&["a".into(), "b".into()], "Projects"), "Projects.zip");
        assert_eq!(zip_name(&["a".into(), "b".into()], ""), "Files.zip");
    }

    #[test]
    fn zip_is_streamed_and_cancel_stops_it() {
        let nodes = vec![n("d", None, "Docs", true, 0), n("a", Some("d"), "a.txt", false, 3), n("e", Some("d"), "Empty", true, 0)];
        let plan = plan_zip(&nodes, &["d".into()]);
        let cancel = AtomicBool::new(false);
        let mut seen = vec![];
        let mut fetch = |_: &ZipFile, w: &mut dyn std::io::Write| { w.write_all(b"abc").map_err(|e| e.to_string())?; Ok(3) };
        let out = write_zip(&plan, Vec::new(), &cancel, &mut fetch, &mut |p, cur| seen.push((p, cur.to_string()))).unwrap();
        assert_eq!(&out[out.len() - 22..out.len() - 18], &0x0605_4b50u32.to_le_bytes());
        // two entries: the empty folder and the file
        assert_eq!(u16::from_le_bytes([out[out.len() - 12], out[out.len() - 11]]), 2);
        assert_eq!(seen.first().unwrap().1, "Docs/a.txt");
        assert_eq!(seen.last().unwrap().0, 1.0);
        cancel.store(true, Ordering::Relaxed);
        let r = write_zip(&plan, Vec::new(), &cancel, &mut fetch, &mut |_, _| {});
        assert_eq!(r.err().as_deref(), Some(crate::zip::CANCELLED));
    }

    #[test]
    fn conflicts_are_same_kind_once_each() {
        let existing = vec![("a.txt".to_string(), false, "1".to_string()), ("Docs".to_string(), true, "2".to_string()), ("b".to_string(), true, "3".to_string())];
        let inc = |n: &str, k| Conflict { name: n.into(), kind: k };
        let got = find_conflicts(&[inc("a.txt", Kind::File), inc("a.txt", Kind::File), inc("b", Kind::File), inc("Docs", Kind::Folder), inc("new", Kind::File)], &existing);
        assert_eq!(got, [inc("a.txt", Kind::File), inc("Docs", Kind::Folder)]);
        let children = vec![n("1", None, "a.txt", false, 1), json!({"id": "l", "name": "link", "@type": "Symlink", "_key": "l", "_acc": "A"})];
        assert_eq!(existing_entries(&children), [("a.txt".to_string(), false, "1".to_string())]);
    }

    #[test]
    fn questions_in_turn_and_apply_to_all() {
        let c = |n: &str| Conflict { name: n.into(), kind: Kind::File };
        let mut a = Asking::new(vec![c("x"), c("y"), c("z")]);
        assert_eq!(a.current().map(|(c, left)| (c.name.clone(), left)), Some(("x".into(), 3)));
        a.answer(Choice::Skip, false);
        assert_eq!(a.current().map(|q| q.1), Some(2));
        a.answer(Choice::Replace, true);
        assert!(a.done());
        assert_eq!(a.choices.get("x"), Some(&Choice::Skip));
        assert_eq!(a.choices.get("y"), Some(&Choice::Replace));
        assert_eq!(a.choices.get("z"), Some(&Choice::Replace));
        assert_eq!(Choice::parse("keep"), Some(Choice::Keep));
        assert_eq!(Choice::parse(""), None);
    }

    #[test]
    fn upload_steps_follow_the_choices() {
        let existing = vec![("a.txt".to_string(), false, "id-a".to_string()), ("b.txt".to_string(), false, "id-b".to_string()),
            ("c.txt".to_string(), false, "id-c".to_string()), ("Docs".to_string(), true, "id-d".to_string())];
        let choices: HashMap<String, Choice> = [("a.txt".to_string(), Choice::Replace), ("b.txt".to_string(), Choice::Keep), ("c.txt".to_string(), Choice::Skip)].into();
        let names: Vec<String> = ["a.txt", "b.txt", "c.txt", "new.txt", "new.txt", "Docs"].iter().map(|s| s.to_string()).collect();
        assert_eq!(plan_file_uploads(&names, &existing, &HashSet::new(), &choices), [
            Step::Replace("id-a".into()), Step::Create("b (1).txt".into()), Step::Skip,
            Step::Create("new.txt".into()), Step::Create("new (1).txt".into()), Step::Create("Docs (1)".into()),
        ]);
        // a symlink's name is taken too, though it is never a conflict
        let taken = taken_names(&[json!({"id": "l", "name": "link.txt", "@type": "Symlink", "_key": "l", "_acc": "A"})]);
        assert_eq!(plan_file_uploads(&["link.txt".to_string()], &[], &taken, &HashMap::new()), [Step::Create("link (1).txt".into())]);
    }

    #[test]
    fn replace_and_create_request_shapes() {
        let seen = Arc::new(std::sync::Mutex::new(vec![]));
        let s2 = seen.clone();
        let c = Client::fake("https://mail.example.test", "A", &[CAP], move |calls| {
            s2.lock().unwrap_or_else(|e| e.into_inner()).push(calls.clone());
            let m = &calls[0];
            Ok(vec![match m[0].as_str() {
                Some("FileNode/set") if m[1].get("update").is_some() => json!(["FileNode/set", {"updated": {"n1": null}}, "r"]),
                _ => json!(["FileNode/set", {"created": {"n": {"id": "new1"}}}, "s"]),
            }])
        });
        replace_blob(&c, "A", "n1", "blob9").unwrap();
        assert_eq!(create_text_node(&c, false, "A", Some("p1"), "new-file.txt", "blob0").unwrap(), "new1");
        create_text_node(&c, true, "A", None, "x.txt", "blob1").unwrap();
        let seen = seen.lock().unwrap_or_else(|e| e.into_inner());
        assert_eq!(seen[0], json!([["FileNode/set", {"accountId": "A", "update": {"n1": {"blobId": "blob9", "modified": null}}}, "r"]]));
        assert_eq!(seen[1][0][1], json!({"accountId": "A", "onExists": "rename", "create": {"n": {"parentId": "p1", "name": "new-file.txt", "blobId": "blob0", "type": "text/plain"}}}));
        assert_eq!(seen[2][0][1]["create"]["n"], json!({"@type": "File", "parentId": null, "name": "x.txt", "blobId": "blob1", "mediaType": "text/plain"}));
    }

    #[test]
    fn a_refused_replace_is_an_error() {
        let c = Client::fake("https://mail.example.test", "A", &[CAP], |_| Ok(vec![json!(["FileNode/set", {"notUpdated": {"n1": {"type": "forbidden", "description": "No"}}}, "r"])]));
        assert!(replace_blob(&c, "A", "n1", "b").is_err());
    }

    #[test]
    fn merge_finds_children_by_kind() {
        let nodes = vec![n("d", None, "Docs", true, 0), n("s", Some("d"), "Sub", true, 0), n("f", Some("d"), "Sub", false, 1), n("g", None, "Sub", true, 0)];
        let ix = child_index(&nodes, "A");
        assert_eq!(child_named(&ix, "d", "Sub", true).as_deref(), Some("s"));
        assert_eq!(child_named(&ix, "d", "Sub", false).as_deref(), Some("f"));
        assert_eq!(child_named(&ix, "", "Sub", true).as_deref(), Some("g"));
        assert!(child_named(&child_index(&nodes, "B"), "d", "Sub", true).is_none());
    }

    #[test]
    fn zip_segments_never_vanish() {
        for bad in ["", ".", "..", "...", " .", ". .", "  "] { assert_eq!(zip_segment(bad), "_", "{bad:?}"); }
        assert_eq!(zip_segment("a/b\\c"), "a_b_c");
        assert_eq!(zip_segment("a\u{7}b"), "a_b");
        assert_eq!(zip_segment("notes."), "notes.");
        assert_eq!(zip_segment(".env"), ".env");
    }

    fn scratch(tag: &str) -> PathBuf {
        let d = std::env::temp_dir().join(format!("barbican-files-x-{tag}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&d);
        std::fs::create_dir_all(&d).unwrap();
        d
    }

    /// A fetch that writes each file's bytes in small pieces, as a download stream does.
    fn chunked(data: &HashMap<String, Vec<u8>>) -> impl FnMut(&ZipFile, &mut dyn std::io::Write) -> Result<u64, String> + '_ {
        move |f, w| {
            let b = &data[&f.blob];
            for c in b.chunks(997) { w.write_all(c).map_err(|e| e.to_string())?; }
            Ok(b.len() as u64)
        }
    }

    /// The files of a zip (its folder entries left out), read back the way an unzip tool does.
    fn unzipped(z: &[u8]) -> Vec<(String, Vec<u8>)> {
        crate::zip::tests::read_back(z).into_iter().filter(|(name, _)| !name.ends_with('/')).collect()
    }

    #[test]
    fn streamed_zip_reads_back_as_files() {
        let nodes = vec![n("d", None, "Docs", true, 0), n("a", Some("d"), "a.txt", false, 40_000), n("e", Some("d"), "Empty", true, 0), n("t", None, "top.bin", false, 5)];
        let plan = plan_zip(&nodes, &["d".into(), "t".into()]);
        let data: HashMap<String, Vec<u8>> = [("b-a".to_string(), (0..40_000u32).map(|i| (i % 253) as u8).collect()), ("b-t".to_string(), b"12345".to_vec())].into();
        let out = write_zip(&plan, Vec::new(), &AtomicBool::new(false), &mut chunked(&data), &mut |_, _| {}).unwrap();
        let files = unzipped(&out);
        let got: Vec<(&str, &[u8])> = files.iter().map(|f| (f.0.as_str(), f.1.as_slice())).collect();
        assert_eq!(got, [("Docs/a.txt", data["b-a"].as_slice()), ("top.bin", data["b-t"].as_slice())]);
    }

    #[test]
    fn a_cancel_in_the_middle_of_a_file_stops_the_zip() {
        let nodes = vec![n("a", None, "a.bin", false, 50_000), n("b", None, "b.bin", false, 1)];
        let plan = plan_zip(&nodes, &["a".into(), "b".into()]);
        let cancel = AtomicBool::new(false);
        let mut pieces = 0;
        let mut fetched = vec![];
        let mut fetch = |f: &ZipFile, w: &mut dyn std::io::Write| {
            fetched.push(f.path.clone());
            for c in vec![7u8; 50_000].chunks(1000) {
                pieces += 1;
                if pieces == 3 { cancel.store(true, Ordering::Relaxed); }
                w.write_all(c).map_err(|e| e.to_string())?;
            }
            Ok(50_000)
        };
        let r = write_zip(&plan, Vec::new(), &cancel, &mut fetch, &mut |_, _| {});
        assert!(r.err().is_some_and(|e| e.contains(crate::zip::CANCELLED)));
        assert_eq!(pieces, 3, "the stream stops at the first write after the cancel");
        assert_eq!(fetched, ["a.bin"], "no later file is fetched");
    }

    #[test]
    fn a_zip_lands_only_when_complete_and_never_clobbers_a_part_file() {
        let dir = scratch("save");
        let target = dir.join("Docs.zip");
        // someone else's file where the part file would go: kept as it is
        std::fs::write(dir.join("Docs.zip.part"), b"not ours").unwrap();
        let nodes = vec![n("a", None, "a.txt", false, 3)];
        let plan = plan_zip(&nodes, &["a".into()]);
        let data: HashMap<String, Vec<u8>> = [("b-a".to_string(), b"abc".to_vec())].into();
        save_zip(&plan, &target, &AtomicBool::new(false), &mut chunked(&data), &mut |_, _| {}).unwrap();
        let files = unzipped(&std::fs::read(&target).unwrap());
        assert_eq!(files[0].1, b"abc");
        assert_eq!(std::fs::read(dir.join("Docs.zip.part")).unwrap(), b"not ours");
        assert!(!dir.join("Docs.zip.1.part").exists(), "our part file became the zip");
        // a failure half way: the old zip stays, no part file of ours is left
        let mut failing = |_: &ZipFile, w: &mut dyn std::io::Write| { w.write_all(b"partial").map_err(|e| e.to_string())?; Err("Download failed (500)".to_string()) };
        let r = save_zip(&plan, &target, &AtomicBool::new(false), &mut failing, &mut |_, _| {});
        assert_eq!(r.err().as_deref(), Some("Download failed (500)"));
        assert_eq!(unzipped(&std::fs::read(&target).unwrap())[0].1, b"abc");
        assert!(!dir.join("Docs.zip.1.part").exists());
        // a cancel before it starts leaves nothing either
        let r = save_zip(&plan, &dir.join("New.zip"), &AtomicBool::new(true), &mut chunked(&data), &mut |_, _| {});
        assert!(r.is_err());
        assert!(!dir.join("New.zip").exists() && !dir.join("New.zip.part").exists());
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_skipped_folder_counts_its_files() {
        let dir = scratch("count");
        std::fs::create_dir_all(dir.join("a/b")).unwrap();
        for f in ["x.txt", "a/y.txt", "a/b/z.txt"] { std::fs::write(dir.join(f), b"1").unwrap(); }
        assert_eq!(count_files(&dir), 3);
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn zip_download_streams_blobs_through_the_client() {
        let c = Client::fake("https://mail.example.test", "A", &[CAP], |calls| {
            assert_eq!(calls[0][0], "blob/download");
            Ok(vec![json!(["blob/download", {"text": format!("data of {}", calls[0][1]["blobId"].as_str().unwrap())}, "d"])])
        });
        let mut out = vec![];
        let n = c.download_to("A", "b-1", "a.txt", "text/plain", &mut out).unwrap();
        assert_eq!((n, out.as_slice()), (11, &b"data of b-1"[..]));
    }
}
