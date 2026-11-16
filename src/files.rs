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

pub fn is_starred(k: &str) -> bool { prefs_list("filesFavorites").iter().any(|x| x == k) }

/// Star / unstar a folder (Bulwark keeps favourites on the device, `files-favorites`).
pub fn toggle_star(u: &AppWindow, k: &str) {
    let mut v = prefs_list("filesFavorites");
    if let Some(i) = v.iter().position(|x| x == k) { v.remove(i); } else { v.push(k.to_string()); }
    set_prefs_list("filesFavorites", &v);
    render(u);
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
        let mut legacy = HashSet::new();
        for a in &accounts {
            let cap = &c.session["accounts"][a]["accountCapabilities"][CAP];
            if cap.is_object() && cap.get("forbiddenNameChars").is_none() { legacy.insert(a.clone()); }
        }
        // Stalwart can't filter FileNode/query by parent: list the account, filter here (as Bulwark does).
        let res: Result<Vec<Value>, String> = (|| {
            let mut nodes = vec![];
            for (i, acc) in accounts.iter().enumerate() {
                let q = match c.call(json!([["FileNode/query", {"accountId": acc, "limit": 20000}, "q"]])) { Ok(q) => q, Err(e) if i == 0 => return Err(e), Err(_) => continue };
                let ids: Vec<Value> = q[0][1]["ids"].as_array().cloned().unwrap_or_default();
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
                let draft = nodes.iter().any(|n| n["@type"].is_string());
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
        let mut rows: Vec<&Value> = s.nodes.iter().filter(|n| parent_key(n) == s.cwd && (!s.cwd.is_empty() || !shared(n))).collect();
        rows.sort_by_key(|n| (!is_dir(n), n["name"].as_str().unwrap_or("").to_lowercase()));
        let cut: Vec<String> = s.clipboard.as_ref().filter(|c| c.1).map(|c| c.0.clone()).unwrap_or_default();
        let out: Vec<FileRow> = rows.iter().map(|n| FileRow {
            id: key(n).into(),
            name: n["name"].as_str().unwrap_or("").into(),
            folder: is_dir(n),
            size: if is_dir(n) { "".into() } else { crate::human_size(n["size"].as_u64().unwrap_or(0)).into() },
            modified: n["modified"].as_str().and_then(|d| crate::mail::parse_time(&json!(d))).map(|t| t.format("%d %b %Y %H:%M").to_string()).unwrap_or_default().into(),
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
        crumbs.push(("".into(), if in_shared { "Shared with me".into() } else { "My files".into() }));
        crumbs.reverse();
        let ids: Vec<SharedString> = crumbs.iter().map(|c| c.0.as_str().into()).collect();
        let names: Vec<SharedString> = crumbs.iter().map(|c| c.1.as_str().into()).collect();
        u.set_crumb_ids(ModelRc::new(VecModel::from(ids)));
        u.set_crumb_names(ModelRc::new(VecModel::from(names)));

        // ---- sidebar (Bulwark files-sidebar): My files tree, Favorites, Recent, Shared with me ----
        let mut v = vec![nav("root", "", "My files", "\u{E8B7}", 0, "")];
        fn tree(s: &State, parent: &str, depth: i32, v: &mut Vec<NavRow>) {
            let mut kids: Vec<&Value> = s.nodes.iter().filter(|n| !shared(n) && is_dir(n) && parent_key(n) == parent).collect();
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
            let Some(n) = s.nodes.iter().find(|n| key(n) == *f) else { continue };
            v.push(nav("fav", f, n["name"].as_str().unwrap_or(""), "\u{E735}", 0, if first { "Favorites" } else { "" }));
            first = false;
        }
        let mut first = true;
        for f in &recent {
            let Some(n) = s.nodes.iter().find(|n| key(n) == *f) else { continue };
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
        u.set_fl_nav(ModelRc::new(VecModel::from(v)));
        u.set_fl_cwd(s.cwd.clone().into());
    });
}

/// Sidebar folder chevron.
pub fn toggle_expand(u: &AppWindow, k: &str) {
    ST.with(|s| { let mut s = s.borrow_mut(); if !s.expanded.remove(k) { s.expanded.insert(k.to_string()); } });
    render(u);
}

pub fn activate(u: &AppWindow, c: Arc<Client>, id: &str) {
    let Some(n) = node(id) else { return };
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
    let Some(files) = rfd::FileDialog::new().pick_files() else { return };
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

pub fn delete(u: &AppWindow, c: Arc<Client>, id: &str) {
    let Some(n) = node(id) else { return };
    ST.with(|s| { let mut s = s.borrow_mut(); if let Some(cb) = s.clipboard.as_mut() { cb.0.retain(|x| x != id); } });
    node_set(u, c, acc_of(&n), json!({"destroy": [raw_id(&n)], "onDestroyRemoveChildren": true}), "");
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
                if k == skip { continue; }
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
