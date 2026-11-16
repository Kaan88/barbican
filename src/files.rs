//! Files app: JMAP File Storage (FileNode). Handles both Stalwart 0.16 (`nodeType`, `type`)
//! and 1.0 / draft 18 (`@type`, `mediaType`) shapes, like Bulwark. Browse, breadcrumb,
//! upload, download/open, new folder, rename, delete; sidebar (My files tree, Favorites,
//! Recent, Shared with me); star, share (shareWith to other users), move, cut/copy/paste,
//! duplicate (Bulwark files-sidebar / file-browser).
//!
//! Nodes of every account with the filenode capability are listed; a node is addressed by
//! its key: the bare id in the primary account, `account|id` in another (shared) one.
use crate::jmap::Client;
use crate::{AppWindow, FileRow, NavRow};
use serde_json::{json, Value};
use slint::{ComponentHandle, ModelRc, SharedString, VecModel};
use std::cell::RefCell;
use std::collections::HashSet;
use std::sync::Arc;

const CAP: &str = "urn:ietf:params:jmap:filenode";

#[derive(Default)]
struct State {
    nodes: Vec<Value>,
    cwd: String,
    draft18: bool,
    rename: Option<String>,
    primary: String,
    /// Folders open in the sidebar tree (keys).
    expanded: HashSet<String>,
    /// Cut or copied keys, and whether it was a cut.
    clipboard: Option<(Vec<String>, bool)>,
    /// Accounts whose server names file rights mayRead / mayWrite / mayShare (Stalwart before 0.16.6).
    legacy: HashSet<String>,
}

thread_local! { static ST: RefCell<State> = RefCell::new(State::default()); }

const PROPS: [&str; 13] = ["id", "parentId", "name", "type", "blobId", "size", "created", "modified", "nodeType", "@type", "mediaType", "shareWith", "myRights"];

fn is_dir(n: &Value) -> bool {
    match n["nodeType"].as_str().or(n["@type"].as_str().map(|t| match t { "Directory" => "directory", "File" => "file", _ => "other" })) {
        Some("directory") => true,
        Some(_) => false,
        None => n["blobId"].is_null(),
    }
}

fn mime(n: &Value) -> String {
    n["type"].as_str().or(n["mediaType"].as_str()).unwrap_or("application/octet-stream").to_string()
}

fn key(n: &Value) -> String { n["_key"].as_str().unwrap_or("").to_string() }
fn acc_of(n: &Value) -> String { n["_acc"].as_str().unwrap_or("").to_string() }
fn raw_id(n: &Value) -> String { n["id"].as_str().unwrap_or("").to_string() }
fn shared(n: &Value) -> bool { n["_shared"] == true }

/// The key of a node's parent (empty for a root).
fn parent_key(n: &Value) -> String {
    match n["parentId"].as_str() {
        None | Some("") => String::new(),
        Some(p) if shared(n) => format!("{}|{p}", acc_of(n)),
        Some(p) => p.to_string(),
    }
}

fn node(k: &str) -> Option<Value> { ST.with(|s| s.borrow().nodes.iter().find(|n| key(n) == k).cloned()) }

// ---------------- trash (Bulwark lib/files-trash.ts) ----------------
//
// Stalwart keeps no deleted FileNodes, so the trash is a folder the app manages: `.Trash` at
// the root of the user's own drive, hidden from every listing. Each delete puts its items in a
// folder of its own inside it, named `<ms since epoch>~<parent id>`, so Restore knows where
// they came from and two deleted files of the same name never clash.

pub const TRASH_NAME: &str = ".Trash";
/// The trash as a place in the Files view (no JMAP id contains "*").
pub const TRASH_LOC: &str = "*trash";

/// The name of the folder one delete's items go into.
pub fn trash_batch_name(parent: Option<&str>, deleted_ms: i64) -> String { format!("{deleted_ms}~{}", parent.unwrap_or("")) }

/// When a batch was deleted (ms) and from which folder (None: the root); None for other names.
pub fn parse_trash_batch(name: &str) -> Option<(i64, Option<String>)> {
    let (ms, parent) = name.split_once('~')?;
    if ms.len() < 10 || !ms.bytes().all(|b| b.is_ascii_digit()) { return None; }
    if !parent.bytes().all(|b| b.is_ascii_alphanumeric() || b == b'_' || b == b'-') { return None; }
    Some((ms.parse().ok()?, (!parent.is_empty()).then(|| parent.to_string())))
}

fn trash_folder(s: &State) -> Option<&Value> {
    s.nodes.iter().find(|n| !shared(n) && parent_key(n).is_empty() && is_dir(n) && n["name"] == TRASH_NAME)
}

/// The trash folder and everything in it (keys), kept out of normal listings.
fn trash_subtree(s: &State) -> HashSet<String> {
    let mut ids = HashSet::new();
    let Some(t) = trash_folder(s) else { return ids };
    ids.insert(key(t));
    loop {
        let before = ids.len();
        for n in &s.nodes { if ids.contains(&parent_key(n)) { ids.insert(key(n)); } }
        if ids.len() == before { return ids; }
    }
}

/// What the trash holds: (item, its batch key if any, deleted at ms, the folder it came from).
fn trashed_items(s: &State) -> Vec<(Value, Option<String>, Option<i64>, Option<String>)> {
    let Some(t) = trash_folder(s) else { return vec![] };
    let tk = key(t);
    let mut out = vec![];
    for child in s.nodes.iter().filter(|n| parent_key(n) == tk) {
        match is_dir(child).then(|| parse_trash_batch(child["name"].as_str().unwrap_or(""))).flatten() {
            Some((ms, from)) => {
                let bk = key(child);
                for n in s.nodes.iter().filter(|n| parent_key(n) == bk) { out.push((n.clone(), Some(bk.clone()), Some(ms), from.clone())); }
            }
            None => out.push((child.clone(), None, None, None)),
        }
    }
    out
}

/// Whether a node sits in the trash (an item of it, not the trash folder itself).
pub fn is_shared(k: &str) -> bool { node(k).is_some_and(|n| shared(&n)) }

pub fn is_trashed(k: &str) -> bool { ST.with(|s| { let s = s.borrow(); trash_subtree(&s).contains(k) && trash_folder(&s).is_none_or(|t| key(t) != k) }) }

/// A name not taken among `used` (Bulwark getUniqueName: "name (2).ext").
pub fn unique_name(name: &str, used: &HashSet<String>) -> String {
    if !used.contains(name) { return name.to_string(); }
    let (stem, ext) = match name.rfind('.') { Some(i) if i > 0 => (&name[..i], &name[i..]), _ => (name, "") };
    (2..).map(|i| format!("{stem} ({i}){ext}")).find(|n| !used.contains(n)).unwrap()
}

/// Favourites and recent files live on this device (Bulwark: files-favorites / files-recent-files).
fn local() -> Value {
    std::fs::read_to_string(crate::config_dir().join("files.json")).ok().and_then(|t| serde_json::from_str(&t).ok()).unwrap_or(json!({}))
}

fn prefs_list(name: &str) -> Vec<String> {
    local()[name].as_array().map(|a| a.iter().filter_map(|x| x.as_str().map(String::from)).collect()).unwrap_or_default()
}

fn set_prefs_list(name: &str, v: &[String]) {
    let mut l = local();
    l[name] = json!(v);
    let _ = std::fs::create_dir_all(crate::config_dir());
    let _ = std::fs::write(crate::config_dir().join("files.json"), l.to_string());
}

/// The time part of a date, following the timeFormat setting (12h or 24h).
fn clock() -> &'static str {
    if crate::settings::Settings::load().str("timeFormat", "24h") == "12h" { "%-I:%M %p" } else { "%H:%M" }
}

pub fn is_starred(k: &str) -> bool { prefs_list("filesFavorites").iter().any(|x| x == k) }

/// Star / unstar a folder (Bulwark keeps favourites on the device, `files-favorites`).
pub fn toggle_star(u: &AppWindow, k: &str) {
    let mut v = prefs_list("filesFavorites");
    if let Some(i) = v.iter().position(|x| x == k) { v.remove(i); } else { v.push(k.to_string()); }
    set_prefs_list("filesFavorites", &v);
    render(u);
}

/// Drop an entry from the sidebar's Recent list.
pub fn remove_recent(u: &AppWindow, k: &str) {
    let mut v = prefs_list("filesRecent");
    v.retain(|x| x != k);
    set_prefs_list("filesRecent", &v);
    render(u);
}

/// The folder a node sits in (for "Show in folder").
pub fn parent_of(k: &str) -> Option<String> {
    ST.with(|s| s.borrow().nodes.iter().find(|n| key(n) == k).map(parent_key))
}

fn note_recent(k: &str) {
    let mut v = prefs_list("filesRecent");
    v.retain(|x| x != k);
    v.insert(0, k.to_string());
    v.truncate(10);
    set_prefs_list("filesRecent", &v);
}

pub fn open(u: &AppWindow, c: Arc<Client>, folder: &str) {
    ST.with(|s| s.borrow_mut().cwd = folder.to_string());
    if !c.has(CAP) { crate::mail::show_toast(u, "This server has no file storage.", false); return; }
    let w = u.as_weak();
    std::thread::spawn(move || {
        let primary = c.account_for(CAP);
        let mut accounts = vec![primary.clone()];
        if let Some(o) = c.session["accounts"].as_object() {
            for (id, a) in o { if *id != primary && a["accountCapabilities"].get(CAP).is_some() { accounts.push(id.clone()); } }
        }
        // Stalwart 1.0 (File Storage draft 18) lists supportedNodeTypes and wants @type on create (Bulwark usesDraftFileNodeCreate)
        let draft_cap = c.session["accounts"][&primary]["accountCapabilities"][CAP].get("supportedNodeTypes").is_some()
            || c.session["capabilities"][CAP].get("supportedNodeTypes").is_some();
        let mut legacy = HashSet::new();
        for a in &accounts {
            let cap = &c.session["accounts"][a]["accountCapabilities"][CAP];
            if cap.is_object() && cap.get("forbiddenNameChars").is_none() { legacy.insert(a.clone()); }
        }
        // Stalwart can't filter FileNode/query by parent: list the account, filter here (as Bulwark does).
        let res: Result<Vec<Value>, String> = (|| {
            let mut nodes = vec![];
            for (i, acc) in accounts.iter().enumerate() {
                let ids = match c.query_all("FileNode/query", acc, json!({})) { Ok(q) => q, Err(e) if i == 0 => return Err(e), Err(_) => continue };
                for chunk in ids.chunks(c.max_get()) {
                    let g = c.call(json!([["FileNode/get", {"accountId": acc, "ids": chunk, "properties": PROPS}, "g"]]))?;
                    for mut n in g[0][1]["list"].as_array().cloned().unwrap_or_default() {
                        let id = raw_id(&n);
                        n["_acc"] = json!(acc);
                        n["_shared"] = json!(i > 0);
                        n["_key"] = json!(if i == 0 { id } else { format!("{acc}|{id}") });
                        nodes.push(n);
                    }
                }
            }
            Ok(nodes)
        })();
        let _ = w.upgrade_in_event_loop(move |u| match res {
            Ok(nodes) => {
                let draft = draft_cap || nodes.iter().any(|n| n["@type"].is_string());
                ST.with(|s| { let mut s = s.borrow_mut(); s.nodes = nodes; s.draft18 = draft; s.primary = primary; s.legacy = legacy; });
                render(&u);
            }
            Err(e) => crate::mail::show_toast(&u, &format!("Files: {e}"), false),
        });
    });
}

fn nav(kind: &str, id: &str, label: &str, icon: &str, depth: i32, header: &str) -> NavRow {
    NavRow { kind: kind.into(), id: id.into(), label: label.into(), icon: icon.into(), count: "".into(), color: Default::default(), depth, header: header.into() }
}

pub fn render(u: &AppWindow) {
    let favs = prefs_list("filesFavorites");
    let recent = prefs_list("filesRecent");
    ST.with(|s| {
        let s = s.borrow();
        let hidden = trash_subtree(&s);
        let in_trash = s.cwd == TRASH_LOC;
        let trashed = if in_trash { trashed_items(&s) } else { vec![] };
        let mut rows: Vec<&Value> = if in_trash { trashed.iter().map(|t| &t.0).collect() } else {
            s.nodes.iter().filter(|n| parent_key(n) == s.cwd && (!s.cwd.is_empty() || !shared(n)) && !hidden.contains(&key(n))).collect()
        };
        rows.sort_by_key(|n| (!is_dir(n), n["name"].as_str().unwrap_or("").to_lowercase()));
        let ck = clock();
        let deleted_at = |n: &Value| trashed.iter().find(|t| key(&t.0) == key(n)).and_then(|t| t.2)
            .and_then(chrono::DateTime::from_timestamp_millis).map(|t| t.with_timezone(&chrono::Local).format(&format!("Deleted %d %b %Y {ck}")).to_string());
        let cut: Vec<String> = s.clipboard.as_ref().filter(|c| c.1).map(|c| c.0.clone()).unwrap_or_default();
        let out: Vec<FileRow> = rows.iter().map(|n| FileRow {
            trashed: in_trash,
            id: key(n).into(),
            name: n["name"].as_str().unwrap_or("").into(),
            folder: is_dir(n),
            size: if is_dir(n) { "".into() } else { crate::human_size(n["size"].as_u64().unwrap_or(0)).into() },
            modified: deleted_at(n).or_else(|| n["modified"].as_str().and_then(|d| crate::mail::parse_time(&json!(d))).map(|t| t.format(&format!("%d %b %Y {ck}")).to_string())).unwrap_or_default().into(),
            renaming: s.rename.as_deref() == Some(key(n).as_str()),
            starred: favs.contains(&key(n)),
            shared: n["shareWith"].as_object().is_some_and(|m| !m.is_empty()) || shared(n),
            cut: cut.contains(&key(n)),
        }).collect();
        u.set_files(ModelRc::new(VecModel::from(out)));
        // breadcrumb
        let mut crumbs: Vec<(String, String)> = vec![];
        let mut cur = s.cwd.clone();
        let mut guard = 0;
        while !cur.is_empty() && guard < 64 {
            guard += 1;
            let Some(n) = s.nodes.iter().find(|n| key(n) == cur) else { break };
            crumbs.push((cur.clone(), n["name"].as_str().unwrap_or("").to_string()));
            cur = parent_key(n);
        }
        let in_shared = s.nodes.iter().find(|n| key(n) == s.cwd).is_some_and(shared);
        if in_trash { crumbs.push((TRASH_LOC.into(), "Trash".into())); }
        crumbs.push(("".into(), if in_shared { "Shared with me".into() } else { "My files".into() }));
        crumbs.reverse();
        let ids: Vec<SharedString> = crumbs.iter().map(|c| c.0.as_str().into()).collect();
        let names: Vec<SharedString> = crumbs.iter().map(|c| c.1.as_str().into()).collect();
        u.set_crumb_ids(ModelRc::new(VecModel::from(ids)));
        u.set_crumb_names(ModelRc::new(VecModel::from(names)));

        // ---- sidebar (Bulwark files-sidebar): My files tree, Favorites, Recent, Shared with me ----
        let mut v = vec![nav("root", "", "My files", "\u{E8B7}", 0, "")];
        fn tree(s: &State, parent: &str, depth: i32, v: &mut Vec<NavRow>) {
            let mut kids: Vec<&Value> = s.nodes.iter().filter(|n| !shared(n) && is_dir(n) && parent_key(n) == parent && !(parent.is_empty() && n["name"] == TRASH_NAME)).collect();
            kids.sort_by_key(|n| n["name"].as_str().unwrap_or("").to_lowercase());
            for n in kids {
                let k = key(n);
                let has_kids = s.nodes.iter().any(|c| is_dir(c) && parent_key(c) == k);
                let open = s.expanded.contains(&k);
                let mut r = nav("folder", &k, n["name"].as_str().unwrap_or(""), "\u{E8B7}", depth, "");
                r.count = if has_kids { if open { "\u{E70D}".into() } else { "\u{E76C}".into() } } else { "".into() };
                v.push(r);
                if open { tree(s, &k, depth + 1, v); }
            }
        }
        tree(&s, "", 1, &mut v);
        let mut first = true;
        for f in &favs {
            let Some(n) = s.nodes.iter().find(|n| key(n) == *f && !hidden.contains(f)) else { continue };
            v.push(nav("fav", f, n["name"].as_str().unwrap_or(""), "\u{E735}", 0, if first { "Favorites" } else { "" }));
            first = false;
        }
        let mut first = true;
        for f in &recent {
            let Some(n) = s.nodes.iter().find(|n| key(n) == *f && !hidden.contains(f)) else { continue };
            v.push(nav("recent", f, n["name"].as_str().unwrap_or(""), "\u{E81C}", 0, if first { "Recent" } else { "" }));
            first = false;
        }
        let mut first = true;
        let shared_keys: HashSet<String> = s.nodes.iter().filter(|n| shared(n)).map(key).collect();
        let mut roots: Vec<&Value> = s.nodes.iter().filter(|n| shared(n) && !shared_keys.contains(&parent_key(n))).collect();
        roots.sort_by_key(|n| n["name"].as_str().unwrap_or("").to_lowercase());
        for n in roots {
            v.push(nav("shared", &key(n), n["name"].as_str().unwrap_or(""), if is_dir(n) { "\u{E8B7}" } else { "\u{E7C3}" }, 0, if first { "Shared with me" } else { "" }));
            first = false;
        }
        // Bulwark's Trash section, with how many items it holds
        let mut t = nav("trash", TRASH_LOC, "Trash", "\u{E74D}", 0, "");
        let n = trashed_items(&s).len();
        if n > 0 { t.label = format!("Trash ({n})").into(); }
        v.push(t);
        u.set_fl_nav(ModelRc::new(VecModel::from(v)));
        u.set_fl_cwd(s.cwd.clone().into());
        u.set_fl_in_trash(in_trash);
    });
}

/// Sidebar folder chevron.
pub fn toggle_expand(u: &AppWindow, k: &str) {
    ST.with(|s| { let mut s = s.borrow_mut(); if !s.expanded.remove(k) { s.expanded.insert(k.to_string()); } });
    render(u);
}

pub fn activate(u: &AppWindow, c: Arc<Client>, id: &str) {
    if id == TRASH_LOC { open(u, c, id); return; }
    let Some(n) = node(id) else { return };
    // a folder in the trash is not browsed into: restore it first (Bulwark)
    if is_trashed(id) && is_dir(&n) { return; }
    note_recent(id);
    if is_dir(&n) { open(u, c, id); return; }
    let name = crate::sanitize_name(n["name"].as_str().unwrap_or("file"));
    std::thread::spawn(move || {
        if let Ok(bytes) = c.download_from(&acc_of(&n), n["blobId"].as_str().unwrap_or(""), &name, &mime(&n)) {
            let dir = std::env::temp_dir().join("Barbican");
            let _ = std::fs::create_dir_all(&dir);
            let p = dir.join(&name);
            if std::fs::write(&p, bytes).is_ok() { crate::oauth::open_browser(&p.to_string_lossy()); }
        }
    });
}

pub fn download(u: &AppWindow, c: Arc<Client>, id: &str) {
    let Some(n) = node(id) else { return };
    if is_dir(&n) { return; }
    let name = crate::sanitize_name(n["name"].as_str().unwrap_or("file"));
    let Some(path) = rfd::FileDialog::new().set_file_name(&name).save_file() else { return };
    let w = u.as_weak();
    std::thread::spawn(move || {
        let res = c.download_from(&acc_of(&n), n["blobId"].as_str().unwrap_or(""), &name, &mime(&n)).and_then(|b| std::fs::write(&path, b).map_err(|e| e.to_string()));
        let msg = match res { Ok(()) => format!("Saved {name}"), Err(e) => format!("Could not save: {e}") };
        let _ = w.upgrade_in_event_loop(move |u| crate::mail::show_toast(&u, &msg, false));
    });
}

/// The (account, raw id) a new node goes into for a folder key ("" = the primary root).
fn target(k: &str) -> (String, Option<String>) {
    if k.is_empty() { return (ST.with(|s| s.borrow().primary.clone()), None); }
    match node(k) { Some(n) => (acc_of(&n), Some(raw_id(&n))), None => (ST.with(|s| s.borrow().primary.clone()), None) }
}

/// Create a node; returns the new raw id.
fn create_in(c: &Client, draft: bool, acc: &str, parent: Option<&str>, name: &str, blob: Option<(&str, &str)>) -> Result<String, String> {
    let p = parent.map(|p| json!(p)).unwrap_or(Value::Null);
    let node = match (blob, draft) {
        (None, false) => json!({"parentId": p, "name": name}),
        (None, true) => json!({"@type": "Directory", "parentId": p, "name": name}),
        (Some((b, t)), false) => json!({"parentId": p, "name": name, "blobId": b, "type": t}),
        (Some((b, t)), true) => json!({"@type": "File", "parentId": p, "name": name, "blobId": b, "mediaType": t}),
    };
    let r = c.call(json!([["FileNode/set", {"accountId": acc, "onExists": "rename", "create": {"n": node}}, "s"]]))?;
    r[0][1]["created"]["n"]["id"].as_str().map(String::from).ok_or_else(|| crate::jmap::set_error(&r[0][1]["notCreated"]["n"]))
}

pub fn upload(u: &AppWindow, c: Arc<Client>) {
    if ST.with(|s| s.borrow().cwd == TRASH_LOC) { crate::mail::show_toast(u, "Nothing can be added to the trash.", false); return; }
    let Some(files) = crate::automation::pick_files() else { return };
    let (cwd, draft) = ST.with(|s| (s.borrow().cwd.clone(), s.borrow().draft18));
    let (acc, parent) = target(&cwd);
    let w = u.as_weak();
    crate::mail::show_toast(u, "Uploading…", false);
    std::thread::spawn(move || {
        let mut err = None;
        for f in files {
            let name = f.file_name().map(|n| n.to_string_lossy().to_string()).unwrap_or_else(|| "file".into());
            let t = crate::compose::mime_for(&name);
            let r = std::fs::read(&f).map_err(|e| e.to_string()).and_then(|b| c.upload_to(&acc, b, t)).and_then(|(blob, _)| create_in(&c, draft, &acc, parent.as_deref(), &name, Some((&blob, t))));
            if let Err(e) = r { err = Some(e); break; }
        }
        let _ = w.upgrade_in_event_loop(move |u| {
            crate::mail::show_toast(&u, &err.map(|e| format!("Upload failed: {e}")).unwrap_or_else(|| "Uploaded".into()), false);
            open(&u, c, &cwd);
        });
    });
}

pub fn new_folder(u: &AppWindow, c: Arc<Client>, name: &str) {
    if ST.with(|s| s.borrow().cwd == TRASH_LOC) { crate::mail::show_toast(u, "Nothing can be added to the trash.", false); return; }
    let name = name.trim().to_string();
    if name.is_empty() { return; }
    let (cwd, draft) = ST.with(|s| (s.borrow().cwd.clone(), s.borrow().draft18));
    let (acc, parent) = target(&cwd);
    let w = u.as_weak();
    std::thread::spawn(move || {
        let res = create_in(&c, draft, &acc, parent.as_deref(), &name, None);
        let _ = w.upgrade_in_event_loop(move |u| {
            if let Err(e) = res { crate::mail::show_toast(&u, &e, false); }
            open(&u, c, &cwd);
        });
    });
}

pub fn start_rename(u: &AppWindow, id: &str) {
    ST.with(|s| s.borrow_mut().rename = Some(id.to_string()));
    render(u);
}

/// One FileNode/set on a node's account, reporting the first SetError; then reload.
fn node_set(u: &AppWindow, c: Arc<Client>, acc: String, args: Value, done: &'static str) {
    let cwd = ST.with(|s| s.borrow().cwd.clone());
    let w = u.as_weak();
    std::thread::spawn(move || {
        let mut a = args;
        a["accountId"] = json!(acc);
        let res = c.call(json!([["FileNode/set", a, "s"]])).and_then(|r| {
            if r[0][0] == "error" { return Err(crate::jmap::method_error(&r[0][1])); }
            for k in ["notCreated", "notUpdated", "notDestroyed"] {
                if let Some(e) = r[0][1][k].as_object().and_then(|m| m.values().next()) { return Err(crate::jmap::set_error(e)); }
            }
            Ok(())
        });
        let _ = w.upgrade_in_event_loop(move |u| {
            match res { Ok(()) => if !done.is_empty() { crate::mail::show_toast(&u, done, false) }, Err(e) => crate::mail::show_toast(&u, &e, false) }
            open(&u, c, &cwd);
        });
    });
}

pub fn rename(u: &AppWindow, c: Arc<Client>, id: &str, name: &str) {
    ST.with(|s| s.borrow_mut().rename = None);
    let name = name.trim().to_string();
    let Some(n) = node(id).filter(|_| !name.is_empty()) else { render(u); return };
    node_set(u, c, acc_of(&n), json!({"update": { raw_id(&n): {"name": name} }}), "");
}

/// Whether deleting `id` removes it for good (a shared drive item, or one already in the trash).
pub fn deletes_permanently(id: &str) -> bool {
    node(id).is_some_and(|n| shared(&n)) || is_trashed(id)
}

/// Delete: in the user's own drive the item goes to the trash; in a shared drive, or when it
/// is already in the trash, it is removed for good (Bulwark deletesToTrash).
pub fn delete(u: &AppWindow, c: Arc<Client>, id: &str) {
    let Some(n) = node(id) else { return };
    ST.with(|s| { let mut s = s.borrow_mut(); if let Some(cb) = s.clipboard.as_mut() { cb.0.retain(|x| x != id); } });
    if shared(&n) { node_set(u, c, acc_of(&n), json!({"destroy": [raw_id(&n)], "onDestroyRemoveChildren": true}), ""); return; }
    if is_trashed(id) { purge(u, c, id); return; }
    let (trash, draft) = ST.with(|s| { let s = s.borrow(); (trash_folder(&s).map(raw_id), s.draft18) });
    let acc = acc_of(&n);
    let parent = n["parentId"].as_str().filter(|p| !p.is_empty()).map(String::from);
    let cwd = ST.with(|s| s.borrow().cwd.clone());
    let w = u.as_weak();
    let item = raw_id(&n);
    std::thread::spawn(move || {
        let res: Result<(), String> = (|| {
            let trash = match trash { Some(t) => t, None => create_in(&c, draft, &acc, None, TRASH_NAME, None)? };
            let batch = create_in(&c, draft, &acc, Some(&trash), &trash_batch_name(parent.as_deref(), chrono::Utc::now().timestamp_millis()), None)?;
            let r = c.call(json!([["FileNode/set", {"accountId": acc, "update": {item.clone(): {"parentId": batch}}}, "s"]]))?;
            if let Some(e) = r[0][1]["notUpdated"].as_object().and_then(|m| m.values().next()) {
                let _ = c.call(json!([["FileNode/set", {"accountId": acc, "destroy": [batch]}, "d"]]));
                return Err(crate::jmap::set_error(e));
            }
            Ok(())
        })();
        let _ = w.upgrade_in_event_loop(move |u| {
            match res { Ok(()) => crate::mail::show_toast(&u, "Moved to the trash", false), Err(e) => crate::mail::show_toast(&u, &e, false) }
            open(&u, c, &cwd);
        });
    });
}

/// Batches (keys) left empty once `gone` leave the trash.
fn emptied_batches(s: &State, gone: &[String]) -> Vec<String> {
    let mut out = vec![];
    for (_, b, _, _) in trashed_items(s).iter().filter(|t| gone.contains(&key(&t.0))) {
        let Some(b) = b else { continue };
        if out.contains(b) { continue; }
        if s.nodes.iter().filter(|n| parent_key(n) == *b).all(|n| gone.contains(&key(n))) { out.push(b.clone()); }
    }
    out
}

/// Delete an item of the trash for good, and its batch folder when that empties.
pub fn purge(u: &AppWindow, c: Arc<Client>, id: &str) {
    let Some(n) = node(id) else { return };
    let mut destroy = vec![raw_id(&n)];
    ST.with(|s| { let s = s.borrow(); destroy.extend(emptied_batches(&s, &[id.to_string()]).iter().filter_map(|b| s.nodes.iter().find(|x| key(x) == *b).map(raw_id))); });
    node_set(u, c, acc_of(&n), json!({"destroy": destroy, "onDestroyRemoveChildren": true}), "Deleted for good");
}

/// Put a trashed item back where it came from (the root when that folder is gone or itself
/// in the trash), under a free name.
pub fn restore(u: &AppWindow, c: Arc<Client>, id: &str) {
    let plan = ST.with(|s| {
        let s = s.borrow();
        let (n, _, _, from) = trashed_items(&s).into_iter().find(|t| key(&t.0) == id)?;
        let hidden = trash_subtree(&s);
        let target = from.filter(|f| s.nodes.iter().any(|x| !shared(x) && key(x) == *f && is_dir(x) && !hidden.contains(f)));
        let tk = target.clone().unwrap_or_default();
        let used: HashSet<String> = s.nodes.iter().filter(|x| !shared(x) && parent_key(x) == tk && !hidden.contains(&key(x)))
            .filter_map(|x| x["name"].as_str().map(String::from)).collect();
        let name = n["name"].as_str().unwrap_or("").to_string();
        let free = unique_name(&name, &used);
        let mut patch = json!({"parentId": target});
        if free != name { patch["name"] = json!(free); }
        let batches: Vec<String> = emptied_batches(&s, &[id.to_string()]).iter().filter_map(|b| s.nodes.iter().find(|x| key(x) == *b).map(raw_id)).collect();
        let mut args = json!({"update": {raw_id(&n): patch}});
        if !batches.is_empty() { args["destroy"] = json!(batches); }
        Some((acc_of(&n), args))
    });
    let Some((acc, args)) = plan else { return };
    node_set(u, c, acc, args, "Restored");
}

/// Delete everything in the trash for good: the folder goes with all of it (the next delete
/// makes a new one).
pub fn empty_trash(u: &AppWindow, c: Arc<Client>) {
    let Some((acc, id)) = ST.with(|s| { let s = s.borrow(); trash_folder(&s).map(|t| (acc_of(t), raw_id(t))) }) else { return };
    node_set(u, c, acc, json!({"destroy": [id], "onDestroyRemoveChildren": true}), "Trash emptied");
}

/// Upload a folder from disk with everything in it into the folder on screen, recreating its
/// subfolders (Bulwark uploadFolder).
pub fn upload_folder(u: &AppWindow, c: Arc<Client>) {
    let Some(root) = rfd::FileDialog::new().pick_folder() else { return };
    let (cwd, draft) = ST.with(|s| (s.borrow().cwd.clone(), s.borrow().draft18));
    if cwd == TRASH_LOC { crate::mail::show_toast(u, "Nothing can be added to the trash.", false); return; }
    let (acc, parent) = target(&cwd);
    let w = u.as_weak();
    crate::mail::show_toast(u, "Uploading folder…", false);
    std::thread::spawn(move || {
        let (mut done, mut failed) = (0usize, vec![]);
        let res: Result<(), String> = (|| {
            let top = root.file_name().map(|n| n.to_string_lossy().to_string()).unwrap_or_else(|| "Folder".into());
            let top_id = create_in(&c, draft, &acc, parent.as_deref(), &top, None)?;
            // breadth-first: each directory is created before what is inside it
            let mut queue = vec![(root.clone(), top_id)];
            while let Some((dir, id)) = queue.pop() {
                let mut entries: Vec<_> = std::fs::read_dir(&dir).map_err(|e| e.to_string())?.filter_map(|e| e.ok()).collect();
                entries.sort_by_key(|e| e.file_name());
                for e in entries {
                    let name = e.file_name().to_string_lossy().to_string();
                    let Ok(ft) = e.file_type() else { continue };
                    if ft.is_symlink() { continue; }
                    if ft.is_dir() {
                        match create_in(&c, draft, &acc, Some(&id), &name, None) {
                            Ok(sub) => queue.push((e.path(), sub)),
                            Err(err) => failed.push(format!("{name}: {err}")),
                        }
                    } else {
                        let t = crate::compose::mime_for(&name);
                        let r = std::fs::read(e.path()).map_err(|x| x.to_string())
                            .and_then(|b| c.upload_to(&acc, b, t))
                            .and_then(|(blob, _)| create_in(&c, draft, &acc, Some(&id), &name, Some((&blob, t))));
                        match r { Ok(_) => done += 1, Err(err) => failed.push(format!("{name}: {err}")) }
                    }
                }
            }
            Ok(())
        })();
        let msg = match (res, failed.first()) {
            (Err(e), _) => format!("Upload failed: {e}"),
            (Ok(()), Some(f)) => format!("Uploaded {done} files; {} failed ({f})", failed.len()),
            (Ok(()), None) => format!("Uploaded {done} files"),
        };
        let _ = w.upgrade_in_event_loop(move |u| { crate::mail::show_toast(&u, &msg, false); open(&u, c, &cwd); });
    });
}

// ---------------- move, cut / copy / paste, duplicate ----------------

/// Folders a node can move into: (key, label with indent), My files first; not itself or below it.
pub fn move_targets(k: &str) -> Vec<(String, String)> {
    ST.with(|s| {
        let s = s.borrow();
        let Some(me) = s.nodes.iter().find(|n| key(n) == k) else { return vec![] };
        let acc = acc_of(me);
        let mut out = vec![];
        if !shared(me) { out.push((String::new(), "My files".to_string())); }
        fn walk(s: &State, acc: &str, parent: &str, depth: usize, skip: &str, cur_parent: &str, out: &mut Vec<(String, String)>) {
            let mut kids: Vec<&Value> = s.nodes.iter().filter(|n| is_dir(n) && acc_of(n) == acc && parent_key(n) == parent).collect();
            kids.sort_by_key(|n| n["name"].as_str().unwrap_or("").to_lowercase());
            for n in kids {
                let k = key(n);
                if k == skip || (parent.is_empty() && !shared(n) && n["name"] == TRASH_NAME) { continue; }
                if k != cur_parent { out.push((k.clone(), format!("{}{}", "   ".repeat(depth), n["name"].as_str().unwrap_or("")))); }
                walk(s, acc, &k, depth + 1, skip, cur_parent, out);
            }
        }
        let root = if shared(me) { s.nodes.iter().filter(|n| acc_of(n) == acc).map(parent_key).find(|p| !s.nodes.iter().any(|n| key(n) == *p)).unwrap_or_default() } else { String::new() };
        walk(&s, &acc, &root, 1, k, &parent_key(me), &mut out);
        if parent_key(me).is_empty() { out.retain(|o| !o.0.is_empty()); }
        out
    })
}

pub fn move_to(u: &AppWindow, c: Arc<Client>, k: &str, dest: &str) {
    let Some(n) = node(k) else { return };
    let parent = if dest.is_empty() { Value::Null } else { node(dest).map(|d| json!(raw_id(&d))).unwrap_or(Value::Null) };
    node_set(u, c, acc_of(&n), json!({"update": { raw_id(&n): {"parentId": parent} }, "onExists": "rename"}), "Moved");
}

pub fn set_clipboard(u: &AppWindow, k: &str, cut: bool) {
    ST.with(|s| s.borrow_mut().clipboard = Some((vec![k.to_string()], cut)));
    crate::mail::show_toast(u, if cut { "Cut — paste it in another folder" } else { "Copied — paste it in another folder" }, false);
    render(u);
}

pub fn has_clipboard() -> bool { ST.with(|s| s.borrow().clipboard.as_ref().is_some_and(|c| !c.0.is_empty())) }

/// Copy a node (a folder with everything below it) into `parent` under `name`: files reuse their blob.
fn copy_tree(c: &Client, draft: bool, nodes: &[Value], src: &Value, acc: &str, parent: Option<&str>, name: &str) -> Result<(), String> {
    if !is_dir(src) {
        let blob = src["blobId"].as_str().unwrap_or("").to_string();
        let blob = if acc_of(src) == acc { blob } else {
            let bytes = c.download_from(&acc_of(src), &blob, name, &mime(src))?;
            c.upload_to(acc, bytes, &mime(src))?.0
        };
        create_in(c, draft, acc, parent, name, Some((&blob, &mime(src))))?;
        return Ok(());
    }
    let id = create_in(c, draft, acc, parent, name, None)?;
    let k = key(src);
    for child in nodes.iter().filter(|n| parent_key(n) == k) {
        copy_tree(c, draft, nodes, child, acc, Some(&id), child["name"].as_str().unwrap_or(""))?;
    }
    Ok(())
}

pub fn paste(u: &AppWindow, c: Arc<Client>) {
    let Some((keys, cut)) = ST.with(|s| s.borrow().clipboard.clone()) else { return };
    let (cwd, draft, nodes) = ST.with(|s| { let s = s.borrow(); (s.cwd.clone(), s.draft18, s.nodes.clone()) });
    let (acc, parent) = target(&cwd);
    if cut { ST.with(|s| s.borrow_mut().clipboard = None); }
    let w = u.as_weak();
    std::thread::spawn(move || {
        let mut err = None;
        for k in &keys {
            let Some(src) = nodes.iter().find(|n| key(n) == *k) else { continue };
            let name = src["name"].as_str().unwrap_or("file").to_string();
            let r = if cut && acc_of(src) == acc {
                if parent_key(src) == cwd { Ok(()) } else {
                    c.call(json!([["FileNode/set", {"accountId": acc, "onExists": "rename", "update": { raw_id(src): {"parentId": parent.clone().map(Value::from).unwrap_or(Value::Null)} }}, "m"]]))
                        .and_then(|r| r[0][1]["notUpdated"].as_object().and_then(|m| m.values().next()).map(|e| Err(crate::jmap::set_error(e))).unwrap_or(Ok(())))
                }
            } else {
                copy_tree(&c, draft, &nodes, src, &acc, parent.as_deref(), &name).and_then(|_| {
                    if !cut { return Ok(()); }
                    c.call(json!([["FileNode/set", {"accountId": acc_of(src), "destroy": [raw_id(src)], "onDestroyRemoveChildren": true}, "d"]])).map(|_| ())
                })
            };
            if let Err(e) = r { err = Some(e); break; }
        }
        let _ = w.upgrade_in_event_loop(move |u| {
            crate::mail::show_toast(&u, &err.map(|e| format!("Paste failed: {e}")).unwrap_or_else(|| if cut { "Moved".into() } else { "Pasted".into() }), false);
            open(&u, c, &cwd);
        });
    });
}

/// "name (copy).ext" next to the original (Bulwark duplicateResource).
pub fn duplicate(u: &AppWindow, c: Arc<Client>, k: &str) {
    let Some(src) = node(k) else { return };
    let name = src["name"].as_str().unwrap_or("file");
    let copy = match name.rfind('.') { Some(i) if i > 0 && !is_dir(&src) => format!("{} (copy){}", &name[..i], &name[i..]), _ => format!("{name} (copy)") };
    let (draft, nodes) = ST.with(|s| { let s = s.borrow(); (s.draft18, s.nodes.clone()) });
    let parent = parent_key(&src);
    let (acc, pid) = target(&parent);
    let (acc, pid) = if parent.is_empty() { (acc_of(&src), None) } else { (acc, pid) };
    let cwd = ST.with(|s| s.borrow().cwd.clone());
    let w = u.as_weak();
    std::thread::spawn(move || {
        let r = copy_tree(&c, draft, &nodes, &src, &acc, pid.as_deref(), &copy);
        let _ = w.upgrade_in_event_loop(move |u| {
            crate::mail::show_toast(&u, &r.err().map(|e| format!("Not duplicated: {e}")).unwrap_or_else(|| "Duplicated".into()), false);
            open(&u, c, &cwd);
        });
    });
}

// ---------------- sharing (RFC 9670 shareWith) ----------------

pub fn node_info(k: &str) -> Option<(String, bool, bool, bool)> {
    // (name, is folder, shared with me, may share)
    node(k).map(|n| {
        let may_share = !shared(&n) || n["myRights"]["mayShare"] == true;
        (n["name"].as_str().unwrap_or("").to_string(), is_dir(&n), shared(&n), may_share)
    })
}

/// Who the node is shared with: (principal id, read-write).
pub fn shares(k: &str) -> Vec<(String, bool)> {
    node(k).and_then(|n| n["shareWith"].as_object().map(|m| m.iter().map(|(p, r)| (p.clone(), r["mayWrite"] == true || r["mayModifyContent"] == true || r["mayAddChildren"] == true)).collect())).unwrap_or_default()
}

/// Bulwark's FILE_PRESETS read / readWrite, in the account's wire shape.
fn rights(legacy: bool, write: bool) -> Value {
    if legacy { json!({"mayRead": true, "mayWrite": write, "mayShare": false}) } else {
        json!({"mayRead": true, "mayAddChildren": write, "mayRename": write, "mayDelete": write, "mayModifyContent": write, "mayShare": false})
    }
}

/// Find a user by address or name (Principal/query, like Bulwark's share dialog search).
fn find_principal(c: &Client, who: &str) -> Result<(String, String), String> {
    let acc = c.account_for("urn:ietf:params:jmap:principals");
    for filter in [json!({"email": who}), json!({"name": who}), json!({"text": who})] {
        let r = c.call(json!([
            ["Principal/query", {"accountId": acc, "filter": filter, "limit": 5}, "q"],
            ["Principal/get", {"accountId": acc, "#ids": {"resultOf": "q", "name": "Principal/query", "path": "/ids"}, "properties": ["id", "name", "email", "type"]}, "g"],
        ]));
        let Ok(r) = r else { continue };
        if let Some(p) = r[1][1]["list"].as_array().and_then(|l| l.iter().find(|p| p["type"] != "group").or(l.first())) {
            let name = p["email"].as_str().or(p["name"].as_str()).unwrap_or("").to_string();
            return Ok((p["id"].as_str().unwrap_or("").to_string(), name));
        }
    }
    Err(format!("No user {who} on this server"))
}

/// Share with a user (`shareWith/<principal>`), or stop sharing when `write` is None.
pub fn share(u: &AppWindow, c: Arc<Client>, k: &str, who: &str, write: Option<bool>) {
    let Some(n) = node(k) else { return };
    let legacy = ST.with(|s| s.borrow().legacy.contains(&acc_of(&n)));
    let (acc, id, who, is_principal) = (acc_of(&n), raw_id(&n), who.trim().to_string(), write.is_none());
    let cwd = ST.with(|s| s.borrow().cwd.clone());
    let w = u.as_weak();
    std::thread::spawn(move || {
        let res: Result<String, String> = (|| {
            let (pid, name) = if is_principal { (who.clone(), String::new()) } else { find_principal(&c, &who)? };
            let mut patch = serde_json::Map::new();
            patch.insert(format!("shareWith/{}", pid.replace('~', "~0").replace('/', "~1")), write.map(|w| rights(legacy, w)).unwrap_or(Value::Null));
            let r = c.call(json!([["FileNode/set", {"accountId": acc, "update": { id.clone(): Value::Object(patch) }}, "s"]]))?;
            if let Some(e) = r[0][1]["notUpdated"][&id].as_object() { return Err(crate::jmap::set_error(&Value::Object(e.clone()))); }
            if !r[0][1]["updated"].as_object().is_some_and(|m| m.contains_key(&id)) { return Err("The server did not confirm the share".into()); }
            Ok(if is_principal { "Stopped sharing".into() } else { format!("Shared with {name}") })
        })();
        let _ = w.upgrade_in_event_loop(move |u| {
            crate::mail::show_toast(&u, &res.unwrap_or_else(|e| format!("Not shared: {e}")), false);
            open(&u, c, &cwd);
        });
    });
}

/// Display names of principals (for the share list), fetched once per menu.
pub fn principal_names(c: &Client, ids: &[String]) -> Vec<String> {
    if ids.is_empty() { return vec![]; }
    let acc = c.account_for("urn:ietf:params:jmap:principals");
    let r = c.call(json!([["Principal/get", {"accountId": acc, "ids": ids, "properties": ["id", "name", "email"]}, "g"]])).ok();
    ids.iter().map(|id| r.as_ref().and_then(|r| r[0][1]["list"].as_array().and_then(|l| l.iter().find(|p| p["id"] == id.as_str())).and_then(|p| p["email"].as_str().or(p["name"].as_str()).map(String::from))).unwrap_or_else(|| id.clone())).collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn trash_batch_names() {
        assert_eq!(trash_batch_name(Some("b12"), 1730112000000), "1730112000000~b12");
        assert_eq!(trash_batch_name(None, 1730112000000), "1730112000000~");
        assert_eq!(parse_trash_batch("1730112000000~b12"), Some((1730112000000, Some("b12".into()))));
        assert_eq!(parse_trash_batch("1730112000000~"), Some((1730112000000, None)));
        assert_eq!(parse_trash_batch("report.pdf"), None);
        assert_eq!(parse_trash_batch("123~x"), None);
        assert_eq!(parse_trash_batch("1730112000000~a/b"), None);
    }

    #[test]
    fn trash_listing_and_names() {
        let n = |id: &str, parent: Option<&str>, name: &str, dir: bool| {
            let mut v = json!({"id": id, "parentId": parent, "name": name, "_key": id, "_acc": "a", "_shared": false});
            if !dir { v["blobId"] = json!("b"); }
            v
        };
        let s = State { nodes: vec![
            n("t", None, ".Trash", true), n("bt", Some("t"), "1730112000000~d", true), n("f", Some("bt"), "a.txt", false),
            n("loose", Some("t"), "stray.txt", false), n("d", None, "Docs", true), n("g", Some("d"), "keep.txt", false),
        ], ..Default::default() };
        let sub = trash_subtree(&s);
        assert!(sub.contains("t") && sub.contains("bt") && sub.contains("f") && sub.contains("loose"));
        assert!(!sub.contains("d") && !sub.contains("g"));
        let items = trashed_items(&s);
        assert_eq!(items.len(), 2);
        let f = items.iter().find(|t| t.0["id"] == "f").unwrap();
        assert_eq!((f.1.as_deref(), f.2, f.3.as_deref()), (Some("bt"), Some(1730112000000), Some("d")));
        assert_eq!(emptied_batches(&s, &["f".into()]), vec!["bt".to_string()]);
        let used: HashSet<String> = ["a.txt".to_string(), "a (2).txt".to_string()].into();
        assert_eq!(unique_name("a.txt", &used), "a (3).txt");
        assert_eq!(unique_name("b", &used), "b");
    }
}
