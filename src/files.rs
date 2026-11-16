//! Files app: JMAP File Storage (FileNode). Handles both Stalwart 0.16 (`nodeType`, `type`)
//! and 1.0 / draft 18 (`@type`, `mediaType`) shapes, like Bulwark. Browse, breadcrumb,
//! upload, download/open, new folder, rename, delete.
use crate::jmap::Client;
use crate::{AppWindow, FileRow};
use serde_json::{json, Value};
use slint::{ComponentHandle, ModelRc, SharedString, VecModel};
use std::cell::RefCell;
use std::sync::Arc;

#[derive(Default)]
struct State {
    nodes: Vec<Value>,
    cwd: String,
    draft18: bool,
    rename: Option<String>,
}

thread_local! { static ST: RefCell<State> = RefCell::new(State::default()); }

const PROPS: [&str; 11] = ["id", "parentId", "name", "type", "blobId", "size", "created", "modified", "nodeType", "@type", "mediaType"];

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

pub fn open(u: &AppWindow, c: Arc<Client>, folder: &str) {
    ST.with(|s| s.borrow_mut().cwd = folder.to_string());
    if !c.has("urn:ietf:params:jmap:filenode") { crate::mail::show_toast(u, "This server has no file storage.", false); return; }
    let w = u.as_weak();
    std::thread::spawn(move || {
        let acc = c.account_for("urn:ietf:params:jmap:filenode");
        // Stalwart can't filter FileNode/query by parent: list the account, filter here (as Bulwark does).
        let res: Result<Vec<Value>, String> = (|| {
            let q = c.call(json!([["FileNode/query", {"accountId": acc, "limit": 20000}, "q"]]))?;
            let ids: Vec<Value> = q[0][1]["ids"].as_array().cloned().unwrap_or_default();
            let mut nodes = vec![];
            for chunk in ids.chunks(c.max_get()) {
                let g = c.call(json!([["FileNode/get", {"accountId": acc, "ids": chunk, "properties": PROPS}, "g"]]))?;
                nodes.extend(g[0][1]["list"].as_array().cloned().unwrap_or_default());
            }
            Ok(nodes)
        })();
        let _ = w.upgrade_in_event_loop(move |u| match res {
            Ok(nodes) => {
                let draft = nodes.iter().any(|n| n["@type"].is_string());
                ST.with(|s| { let mut s = s.borrow_mut(); s.nodes = nodes; s.draft18 = draft; });
                render(&u);
            }
            Err(e) => crate::mail::show_toast(&u, &format!("Files: {e}"), false),
        });
    });
}

pub fn render(u: &AppWindow) {
    ST.with(|s| {
        let s = s.borrow();
        let parent = |n: &Value| n["parentId"].as_str().unwrap_or("").to_string();
        let mut rows: Vec<&Value> = s.nodes.iter().filter(|n| parent(n) == s.cwd).collect();
        rows.sort_by_key(|n| (!is_dir(n), n["name"].as_str().unwrap_or("").to_lowercase()));
        let out: Vec<FileRow> = rows.iter().map(|n| FileRow {
            id: n["id"].as_str().unwrap_or("").into(),
            name: n["name"].as_str().unwrap_or("").into(),
            folder: is_dir(n),
            size: if is_dir(n) { "".into() } else { crate::human_size(n["size"].as_u64().unwrap_or(0)).into() },
            modified: n["modified"].as_str().and_then(|d| crate::mail::parse_time(&json!(d))).map(|t| t.format("%d %b %Y %H:%M").to_string()).unwrap_or_default().into(),
            renaming: s.rename.as_deref() == n["id"].as_str(),
        }).collect();
        u.set_files(ModelRc::new(VecModel::from(out)));
        // breadcrumb
        let mut crumbs: Vec<(String, String)> = vec![];
        let mut cur = s.cwd.clone();
        let mut guard = 0;
        while !cur.is_empty() && guard < 64 {
            guard += 1;
            let Some(n) = s.nodes.iter().find(|n| n["id"] == cur.as_str()) else { break };
            crumbs.push((cur.clone(), n["name"].as_str().unwrap_or("").to_string()));
            cur = parent(n);
        }
        crumbs.push(("".into(), "Files".into()));
        crumbs.reverse();
        let ids: Vec<SharedString> = crumbs.iter().map(|c| c.0.as_str().into()).collect();
        let names: Vec<SharedString> = crumbs.iter().map(|c| c.1.as_str().into()).collect();
        u.set_crumb_ids(ModelRc::new(VecModel::from(ids)));
        u.set_crumb_names(ModelRc::new(VecModel::from(names)));
    });
}

pub fn activate(u: &AppWindow, c: Arc<Client>, id: &str) {
    let node = ST.with(|s| s.borrow().nodes.iter().find(|n| n["id"] == id).cloned());
    let Some(n) = node else { return };
    if is_dir(&n) { open(u, c, id); return; }
    let name = crate::sanitize_name(n["name"].as_str().unwrap_or("file"));
    std::thread::spawn(move || {
        if let Ok(bytes) = c.download(n["blobId"].as_str().unwrap_or(""), &name, &mime(&n)) {
            let dir = std::env::temp_dir().join("Barbican");
            let _ = std::fs::create_dir_all(&dir);
            let p = dir.join(&name);
            if std::fs::write(&p, bytes).is_ok() { crate::oauth::open_browser(&p.to_string_lossy()); }
        }
    });
}

pub fn download(u: &AppWindow, c: Arc<Client>, id: &str) {
    let Some(n) = ST.with(|s| s.borrow().nodes.iter().find(|n| n["id"] == id).cloned()) else { return };
    if is_dir(&n) { return; }
    let name = crate::sanitize_name(n["name"].as_str().unwrap_or("file"));
    let Some(path) = rfd::FileDialog::new().set_file_name(&name).save_file() else { return };
    let w = u.as_weak();
    std::thread::spawn(move || {
        let res = c.download(n["blobId"].as_str().unwrap_or(""), &name, &mime(&n)).and_then(|b| std::fs::write(&path, b).map_err(|e| e.to_string()));
        let msg = match res { Ok(()) => format!("Saved {name}"), Err(e) => format!("Could not save: {e}") };
        let _ = w.upgrade_in_event_loop(move |u| crate::mail::show_toast(&u, &msg, false));
    });
}

fn create(c: &Client, draft: bool, parent: &str, name: &str, blob: Option<(&str, &str)>) -> Result<(), String> {
    let acc = c.account_for("urn:ietf:params:jmap:filenode");
    let p = if parent.is_empty() { Value::Null } else { json!(parent) };
    let node = match (blob, draft) {
        (None, false) => json!({"parentId": p, "name": name}),
        (None, true) => json!({"@type": "Directory", "parentId": p, "name": name}),
        (Some((b, t)), false) => json!({"parentId": p, "name": name, "blobId": b, "type": t}),
        (Some((b, t)), true) => json!({"@type": "File", "parentId": p, "name": name, "blobId": b, "mediaType": t}),
    };
    let r = c.call(json!([["FileNode/set", {"accountId": acc, "onExists": "rename", "create": {"n": node}}, "s"]]))?;
    if r[0][1]["created"]["n"].is_object() { Ok(()) } else { Err(crate::jmap::set_error(&r[0][1]["notCreated"]["n"])) }
}

pub fn upload(u: &AppWindow, c: Arc<Client>) {
    let Some(files) = rfd::FileDialog::new().pick_files() else { return };
    let (cwd, draft) = ST.with(|s| (s.borrow().cwd.clone(), s.borrow().draft18));
    let w = u.as_weak();
    crate::mail::show_toast(u, "Uploading…", false);
    std::thread::spawn(move || {
        let mut err = None;
        for f in files {
            let name = f.file_name().map(|n| n.to_string_lossy().to_string()).unwrap_or_else(|| "file".into());
            let t = crate::compose::mime_for(&name);
            let r = std::fs::read(&f).map_err(|e| e.to_string()).and_then(|b| c.upload(b, t)).and_then(|(blob, _)| create(&c, draft, &cwd, &name, Some((&blob, t))));
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
    let w = u.as_weak();
    std::thread::spawn(move || {
        let res = create(&c, draft, &cwd, &name, None);
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

pub fn rename(u: &AppWindow, c: Arc<Client>, id: &str, name: &str) {
    ST.with(|s| s.borrow_mut().rename = None);
    let (id, name) = (id.to_string(), name.trim().to_string());
    if name.is_empty() { render(u); return; }
    let cwd = ST.with(|s| s.borrow().cwd.clone());
    let w = u.as_weak();
    std::thread::spawn(move || {
        let acc = c.account_for("urn:ietf:params:jmap:filenode");
        let res = c.update("FileNode/set", &acc, vec![(id, json!({"name": name}))]);
        let _ = w.upgrade_in_event_loop(move |u| {
            if let Ok((_, bad)) = &res { if !bad.is_empty() { crate::mail::show_toast(&u, &bad.join("; "), false); } }
            if let Err(e) = &res { crate::mail::show_toast(&u, e, false); }
            open(&u, c, &cwd);
        });
    });
}

pub fn delete(u: &AppWindow, c: Arc<Client>, id: &str) {
    let id = id.to_string();
    let cwd = ST.with(|s| s.borrow().cwd.clone());
    let w = u.as_weak();
    std::thread::spawn(move || {
        let acc = c.account_for("urn:ietf:params:jmap:filenode");
        let res = c.call(json!([["FileNode/set", {"accountId": acc, "destroy": [id.clone()], "onDestroyRemoveChildren": true}, "d"]]));
        let _ = w.upgrade_in_event_loop(move |u| {
            match res {
                Ok(r) => if let Some(e) = r[0][1]["notDestroyed"][&id].as_object() { crate::mail::show_toast(&u, &crate::jmap::set_error(&Value::Object(e.clone())), false); },
                Err(e) => crate::mail::show_toast(&u, &e, false),
            }
            open(&u, c, &cwd);
        });
    });
}
