//! Right-click context menus (Bulwark's email/mailbox/calendar/contact/file menus).
//! The UI calls `ctx-request(kind, id)` on a right click; `request` fills `ctx-items`
//! and the popup shows them. A pick comes back to `pick`. Submenus and confirmations
//! replace the items and re-open the popup at the same place.
use crate::{calendar, contacts, files, jmap, mail, AppWindow, CtxItem, Shared};
use serde_json::{json, Value};
use slint::{ComponentHandle, Model, ModelRc, VecModel};
use std::cell::RefCell;
use std::sync::Arc;

#[derive(Default, Clone)]
struct Target {
    kind: String,
    id: String,
    ids: Vec<String>,
}

thread_local! {
    static TARGET: RefCell<Target> = RefCell::new(Target::default());
    /// The user's addresses, refreshed on every request (for RSVP).
    static MINE: RefCell<Vec<String>> = const { RefCell::new(Vec::new()) };
    /// Pending text prompt: (action, id).
    static PROMPT: RefCell<Option<(String, String)>> = const { RefCell::new(None) };
}

// ---- item builders ----
fn it(icon: &str, label: &str, hint: &str, action: &str) -> CtxItem {
    CtxItem { icon: icon.into(), label: label.into(), hint: hint.into(), action: action.into(), kind: 0, sub: false, danger: false, enabled: true, checked: false, color: Default::default() }
}
fn sub(icon: &str, label: &str, action: &str) -> CtxItem { CtxItem { sub: true, ..it(icon, label, "", action) } }
fn danger(icon: &str, label: &str, hint: &str, action: &str) -> CtxItem { CtxItem { danger: true, ..it(icon, label, hint, action) } }
fn off(mut i: CtxItem, enabled: bool) -> CtxItem { i.enabled = enabled; i }
fn sep() -> CtxItem { CtxItem { kind: 1, ..it("", "", "", "") } }
fn header(label: &str) -> CtxItem { CtxItem { kind: 2, ..it("", label, "", "") } }

const G_REPLY: &str = "\u{E97A}";
const G_REPLY_ALL: &str = "\u{EE35}";
const G_FORWARD: &str = "\u{E72D}";
const G_ARCHIVE: &str = "\u{E7B8}";
const G_DELETE: &str = "\u{E74D}";
const G_MOVE: &str = "\u{E8DE}";
const G_READ: &str = "\u{E8C3}";
const G_UNREAD: &str = "\u{E715}";
const G_STAR: &str = "\u{E734}";
const G_STARRED: &str = "\u{E735}";
const G_TAG: &str = "\u{E8EC}";
const G_SPAM: &str = "\u{E8F8}";
const G_MORE: &str = "\u{E712}";
const G_OPEN: &str = "\u{E8A7}";
const G_PRINT: &str = "\u{E749}";
const G_DOWNLOAD: &str = "\u{E896}";
const G_COPY: &str = "\u{E8C8}";
const G_SEARCH: &str = "\u{E721}";
#[allow(dead_code)]
const G_ADD: &str = "\u{E710}";
const G_RENAME: &str = "\u{E8AC}";
const G_NEW_FOLDER: &str = "\u{E8F4}";
const G_REFRESH: &str = "\u{E72C}";
const G_CALENDAR: &str = "\u{E787}";
const G_CONTACT: &str = "\u{E77B}";
const G_ADD_CONTACT: &str = "\u{E8FA}";
const G_CHECK: &str = "\u{E73E}";
const G_TASK: &str = "\u{E9D5}";
const G_EDIT: &str = "\u{E70F}";
const G_DUP: &str = "\u{E8C8}";
const G_EYE: &str = "\u{E7B3}";
const G_EMPTY: &str = "\u{E894}";
const G_SELECT_ALL: &str = "\u{E8B3}";
const G_UPLOAD: &str = "\u{E898}";
const G_SETTINGS: &str = "\u{E713}";
const G_MAYBE: &str = "\u{E9CE}";
const G_CANCEL: &str = "\u{E711}";
const G_DEFAULT: &str = "\u{E735}";

fn row_h(i: &CtxItem) -> f32 { match i.kind { 1 => 9.0, 2 => 24.0, _ => 28.0 } }

fn show(u: &AppWindow, items: Vec<CtxItem>) {
    let h: f32 = items.iter().map(row_h).sum::<f32>() + 10.0;
    u.set_ctx_h(h);
    u.set_ctx_items(ModelRc::new(VecModel::from(items)));
}

fn reopen(u: &AppWindow, items: Vec<CtxItem>) {
    show(u, items);
    let w = u.as_weak();
    slint::Timer::single_shot(std::time::Duration::from_millis(1), move || { if let Some(u) = w.upgrade() { u.invoke_ctx_show(); } });
}

fn client(st: &Shared) -> Option<Arc<jmap::Client>> { st.lock().unwrap().clone() }

fn folder(u: &AppWindow, id: &str) -> Option<crate::Folder> {
    let m = u.get_folders();
    (0..m.row_count()).filter_map(|i| m.row_data(i)).find(|f| f.id == id)
}

fn kw(m: &Option<Value>, k: &str) -> bool { m.as_ref().is_some_and(|m| m["keywords"][k].as_bool() == Some(true)) }

/// First address of "Name <a@b>, ..." or a bare address.
fn first_addr(s: &str) -> String {
    jmap::parse_addresses(s).first().and_then(|a| a["email"].as_str().map(String::from)).unwrap_or_else(|| s.trim().to_string())
}

/// Hover (or click) on a menu row: a "sub:" row opens its flyout beside it, any other row closes it.
pub fn hover(u: &AppWindow, action: &str, index: i32) {
    let Some(w) = action.strip_prefix("sub:") else { u.set_ctx_sub_action("".into()); return };
    if u.get_ctx_sub_action().as_str() == action { return; }
    let t = TARGET.with(|x| x.borrow().clone());
    let items = submenu(u, &t, w);
    let m = u.get_ctx_items();
    let idx = if index >= 0 { index as usize } else { (0..m.row_count()).find(|&i| m.row_data(i).is_some_and(|r| r.action == action)).unwrap_or(0) };
    let y: f32 = 4.0 + (0..idx).filter_map(|i| m.row_data(i)).map(|r| row_h(&r)).sum::<f32>();
    u.set_ctx_sub_h(items.iter().map(row_h).sum::<f32>() + 10.0);
    u.set_ctx_sub_y(y);
    u.set_ctx_sub_items(ModelRc::new(VecModel::from(items)));
    u.set_ctx_sub_action(action.into());
}

pub fn set_mine(v: Vec<String>) { MINE.with(|m| *m.borrow_mut() = v); }

pub fn request(u: &AppWindow, kind: &str, id: &str) {
    u.global::<crate::CtxHl>().set_id(id.into());
    let mut t = Target { kind: kind.into(), id: id.into(), ids: vec![id.into()] };
    let items: Vec<CtxItem> = match kind {
        "mail" => {
            let checked = mail::checked_ids();
            if checked.iter().any(|c| c == id) && checked.len() > 1 { t.ids = checked; }
            mail_items(u, &t)
        }
        "thread" => {
            let tm = u.get_thread_msgs();
            let unread = (0..tm.row_count()).filter_map(|i| tm.row_data(i)).find(|m| m.id == id).is_some_and(|m| m.unread);
            let starred = kw(&mail::email(id), "$flagged");
            vec![
                it(G_OPEN, "Open", "", "open"),
                it(G_REPLY, "Reply", "R", "reply"),
                it(G_REPLY_ALL, "Reply all", "Shift+R", "reply-all"),
                it(G_FORWARD, "Forward", "F", "forward"),
                sep(),
                if unread { it(G_READ, "Mark as read", "U", "read") } else { it(G_UNREAD, "Mark as unread", "U", "unread") },
                if starred { it(G_STARRED, "Unstar", "S", "unstar") } else { it(G_STAR, "Star", "S", "star") },
                sep(),
                it(G_PRINT, "Print", "", "print"),
                danger(G_DELETE, "Delete", "Del", "delete"),
            ]
        }
        "addr" | "addr-to" => {
            let a = first_addr(id);
            if a.is_empty() { vec![] } else {
                t.id = a.clone();
                vec![
                    header(&a),
                    it(G_COPY, "Copy email address", "", "copy-addr"),
                    it(G_UNREAD, "New message", "", "compose-to"),
                    it(G_ADD_CONTACT, "Add to contacts", "", "add-contact"),
                    sep(),
                    it(G_SEARCH, "Find emails from this sender", "", "find-from"),
                ]
            }
        }
        "att" => vec![
            it(G_OPEN, "Open", "", "open"),
            it(G_DOWNLOAD, "Save as…", "", "save"),
            sep(),
            it(G_COPY, "Copy file name", "", "copy-name"),
        ],
        "body" => {
            let mut v = vec![];
            if id == "plain" {
                v.push(it(G_COPY, "Copy", "Ctrl+C", "copy-sel"));
                v.push(it(G_SELECT_ALL, "Select all", "Ctrl+A", "select-all"));
                v.push(sep());
            }
            v.push(it(G_COPY, "Copy message text", "", "copy-all"));
            v.push(it(G_REPLY, "Reply", "R", "reply"));
            v.push(it(G_PRINT, "Print", "", "print"));
            v
        }
        "folder" | "setting" => {
            let fid = if kind == "setting" { id.strip_prefix("server.folder.rename:").unwrap_or("").to_string() } else { id.to_string() };
            if fid.is_empty() || fid.starts_with("__") { vec![] } else {
                t.kind = "folder".into();
                t.id = fid.clone();
                folder_items(u, &fid)
            }
        }
        "tag" => vec![
            it(G_OPEN, "Show messages", "", "open"),
            it(G_READ, "Mark all as read", "", "tag-read"),
            sep(),
            it(G_SETTINGS, "Manage tags…", "", "manage"),
        ],
        "list" => vec![
            it(G_REFRESH, "Refresh", "", "refresh"),
            it(G_READ, "Mark folder as read", "", "folder-read"),
            it(G_SELECT_ALL, "Select all", "Ctrl+A", "select-all"),
        ],
        "event" => {
            let mut v = vec![];
            if let Some((_, status)) = calendar::my_participation(id, &MINE.with(|m| m.borrow().clone())) {
                if status != "accepted" { v.push(it(G_CHECK, "Accept", "", "rsvp:accepted")); }
                if status != "tentative" { v.push(it(G_MAYBE, "Maybe", "", "rsvp:tentative")); }
                if status != "declined" { v.push(it(G_CANCEL, "Decline", "", "rsvp:declined")); }
                v.push(sep());
            }
            v.extend([
                it(G_EDIT, "Edit", "", "edit"),
                it(G_DUP, "Duplicate", "", "duplicate"),
                sep(),
                it(G_DOWNLOAD, "Export as .ics", "", "export-ics"),
                it(G_COPY, "Copy title", "", "copy-title"),
                sep(),
                danger(G_DELETE, "Delete", "Del", "ask:delete"),
            ]);
            v
        }
        "slot" => vec![
            it(G_CALENDAR, "New event", "", "new-event"),
            it(G_CALENDAR, "New all-day event", "", "new-allday"),
            it(G_TASK, "New task", "", "new-task"),
            sep(),
            it(G_CALENDAR, "Go to today", "", "today"),
        ],
        "task" => {
            let done = calendar::task_done(id);
            vec![
                if done { it(G_UNREAD, "Mark incomplete", "", "toggle") } else { it(G_CHECK, "Mark complete", "", "toggle") },
                sub(G_CALENDAR, "Due date", "sub:due"),
                sep(),
                danger(G_DELETE, "Delete", "Del", "ask:delete"),
            ]
        }
        "cal" => match calendar::calendar_info(id) {
            None => vec![],
            Some((_, name, visible, default)) => vec![
                header(&name),
                it(G_CALENDAR, "New event in this calendar", "", "new-event"),
                it(G_EYE, if visible { "Hide calendar" } else { "Show calendar" }, "", "toggle"),
                it(G_RENAME, "Rename", "", "rename"),
                off(it(G_DEFAULT, "Set as default", "", "default"), !default),
                sep(),
                danger(G_DELETE, "Delete calendar", "", "ask:delete"),
            ],
        },
        "contact" => vec![
            it(G_OPEN, "Open", "", "open"),
            it(G_UNREAD, "Send email", "", "email"),
            it(G_EDIT, "Edit", "", "edit"),
            sep(),
            it(G_COPY, "Copy email address", "", "copy-email"),
            sep(),
            danger(G_DELETE, "Delete", "Del", "ask:delete"),
        ],
        "group" => vec![
            it(G_OPEN, "Open", "", "open"),
            it(G_UNREAD, "Send email to group", "", "email"),
            sep(),
            danger(G_DELETE, "Delete group", "", "ask:delete"),
        ],
        "file" => {
            let f = file_row(u, id);
            let is_folder = f.as_ref().is_some_and(|f| f.folder);
            vec![
                it(G_OPEN, "Open", "", "open"),
                off(it(G_DOWNLOAD, "Download", "", "download"), !is_folder),
                it(G_RENAME, "Rename", "F2", "rename"),
                sep(),
                danger(G_DELETE, "Delete", "Del", "ask:delete"),
            ]
        }
        "files-bg" => vec![
            it(G_NEW_FOLDER, "New folder", "", "new-folder"),
            it(G_UPLOAD, "Upload files", "", "upload"),
            sep(),
            it(G_REFRESH, "Refresh", "", "refresh"),
        ],
        _ => vec![],
    };
    TARGET.with(|x| *x.borrow_mut() = t);
    show(u, items);
}

fn file_row(u: &AppWindow, id: &str) -> Option<crate::FileRow> {
    let m = u.get_files();
    (0..m.row_count()).filter_map(|i| m.row_data(i)).find(|f| f.id == id)
}

fn mail_items(u: &AppWindow, t: &Target) -> Vec<CtxItem> {
    let n = t.ids.len();
    let first = mail::email(&t.ids[0]);
    let junk = mail::mailbox_role(&u.get_current_folder()) == "junk";
    let mut v = vec![];
    if n > 1 { v.push(header(&format!("{n} selected"))); }
    v.extend([
        off(it(G_REPLY, "Reply", "R", "reply"), n == 1),
        off(it(G_REPLY_ALL, "Reply all", "Shift+R", "reply-all"), n == 1),
        off(it(G_FORWARD, "Forward", "F", "forward"), n == 1),
        sep(),
        it(G_ARCHIVE, "Archive", "E", "archive"),
        if n > 1 && crate::settings::Settings::load().bool("confirmBulkDelete", true) { danger(G_DELETE, "Delete", "Del", "ask:delete") } else { danger(G_DELETE, "Delete", "Del", "delete") },
        sub(G_MOVE, "Move to", "sub:move"),
        sep(),
        if kw(&first, "$seen") { it(G_UNREAD, "Mark as unread", "U", "unread") } else { it(G_READ, "Mark as read", "U", "read") },
        if kw(&first, "$flagged") { it(G_STARRED, "Unstar", "S", "unstar") } else { it(G_STAR, "Star", "S", "star") },
        sub(G_TAG, "Tag", "sub:tag"),
        sep(),
        if junk { it(G_SPAM, "Not spam", "", "notspam") } else { it(G_SPAM, "Report spam", "!", "spam") },
        sep(),
        sub(G_MORE, "More actions", "sub:more"),
    ]);
    v
}

fn folder_items(u: &AppWindow, id: &str) -> Vec<CtxItem> {
    let role = mail::mailbox_role(id);
    let name = folder(u, id).map(|f| f.name.to_string()).unwrap_or_default();
    let mut v = vec![
        header(&name),
        it(G_READ, "Mark folder as read", "", "folder-read"),
        it(G_REFRESH, "Refresh", "", "refresh"),
        sep(),
        it(G_NEW_FOLDER, "New subfolder", "", "new-sub"),
        off(it(G_RENAME, "Rename", "", "rename"), role.is_empty()),
    ];
    if role == "trash" || role == "junk" {
        v.push(sep());
        v.push(danger(G_EMPTY, if role == "trash" { "Empty trash" } else { "Empty spam" }, "", "ask:empty"));
    }
    if role.is_empty() {
        v.push(sep());
        v.push(danger(G_DELETE, "Delete folder", "", "ask:delete"));
    }
    v
}

fn submenu(u: &AppWindow, t: &Target, which: &str) -> Vec<CtxItem> {
    let mut v = vec![];
    match which {
        "move" => {
            let m = u.get_folders();
            for f in (0..m.row_count()).filter_map(|i| m.row_data(i)) {
                if f.id.starts_with("__") || f.id == u.get_current_folder() { continue; }
                let indent = "   ".repeat(f.depth.max(0) as usize);
                v.push(it(&f.icon, &format!("{indent}{}", f.name), "", &format!("move:{}", f.id)));
            }
        }
        "tag" => {
            let first = mail::email(&t.ids[0]);
            let m = u.get_tags();
            for tg in (0..m.row_count()).filter_map(|i| m.row_data(i)) {
                let mut i = it(G_TAG, &tg.name, "", &format!("tagt:{}", tg.id));
                i.color = tg.color;
                i.checked = kw(&first, &format!("$label:{}", tg.id));
                v.push(i);
            }
            if m.row_count() == 0 { v.push(off(it("", "No tags", "", ""), false)); }
        }
        "more" => {
            let single = t.ids.len() == 1;
            v.extend([
                off(it(G_OPEN, "Open", "Enter", "open"), single),
                off(it(G_PRINT, "Print", "", "print"), single),
                off(it(G_DOWNLOAD, "Download (.eml)", "", "eml"), single),
                sep(),
                off(it(G_COPY, "Copy sender address", "", "copy-sender"), single),
                off(it(G_SEARCH, "Find emails from sender", "", "find-from"), single),
            ]);
        }
        "due" => {
            v.push(it(G_CALENDAR, "Today", "", "due:0"));
            v.push(it(G_CALENDAR, "Tomorrow", "", "due:1"));
            v.push(it(G_CALENDAR, "Next week", "", "due:7"));
        }
        _ => {}
    }
    v
}

fn confirm(label: &str, action: &str) -> Vec<CtxItem> {
    vec![header("Are you sure?"), danger(G_DELETE, label, "", action), it("\u{E711}", "Cancel", "Esc", "")]
}

/// Run `act` on a message once it is the open one (opening it first if needed).
fn on_open_mail(u: &AppWindow, id: &str, act: &str) {
    let loaded = |id: &str| crate::CUR_MSG.with(|x| x.borrow().as_ref().is_some_and(|(m, _)| m["id"] == id));
    if u.get_current_mail().as_str() == id && loaded(id) {
        if !act.is_empty() { u.invoke_act(act.into()); }
        return;
    }
    u.set_current_mail(id.into());
    u.invoke_open_mail(id.into());
    if act.is_empty() { return; }
    let (w, id, act) = (u.as_weak(), id.to_string(), act.to_string());
    let tries = std::rc::Rc::new(std::cell::Cell::new(0));
    let timer = std::rc::Rc::new(slint::Timer::default());
    let t2 = timer.clone();
    timer.start(slint::TimerMode::Repeated, std::time::Duration::from_millis(100), move || {
        tries.set(tries.get() + 1);
        let Some(u) = w.upgrade() else { t2.stop(); return };
        if loaded(&id) { t2.stop(); if u.get_current_mail().as_str() == id { u.invoke_act(act.clone().into()); } }
        else if tries.get() > 100 { t2.stop(); }
    });
    std::mem::forget(timer);
}

fn mark_read_query(u: &AppWindow, c: Arc<jmap::Client>, filter: Value) {
    let w = u.as_weak();
    std::thread::spawn(move || {
        let res: Result<usize, String> = (|| {
            let mut n = 0;
            loop {
                let q = c.call(json!([["Email/query", {"accountId": c.account, "filter": filter.clone(), "limit": c.max_set()}, "q"]]))?;
                let ids: Vec<String> = q[0][1]["ids"].as_array().map(|a| a.iter().filter_map(|x| x.as_str().map(String::from)).collect()).unwrap_or_default();
                if ids.is_empty() { return Ok(n); }
                let (ok, bad) = c.set_keyword(&ids, "$seen", true)?;
                if !bad.is_empty() { return Err(bad.join("; ")); }
                if ok == 0 { return Ok(n); }
                n += ok;
            }
        })();
        let w2 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| {
            let msg = match res { Ok(n) => format!("Marked {n} as read"), Err(e) => format!("Could not mark as read: {e}") };
            mail::show_toast(&u, &msg, false);
            mail::load_mailboxes(w2.clone(), c.clone(), false);
            mail::reload(w2, c);
        });
    });
}

/// Background mailbox change, then reload folders (and the organize settings page if open).
fn mailbox_op(u: &AppWindow, c: Arc<jmap::Client>, done: &'static str, f: impl FnOnce(&jmap::Client) -> Result<String, String> + Send + 'static) {
    let w = u.as_weak();
    std::thread::spawn(move || {
        let res = f(&c);
        let w2 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| {
            mail::show_toast(&u, &match res { Ok(s) if s.is_empty() => done.to_string(), Ok(s) => s, Err(e) => e }, false);
            mail::load_mailboxes(w2.clone(), c.clone(), false);
            mail::reload(w2, c);
            if u.get_show_settings() && u.get_settings_tab() == "organize" { u.invoke_settings_open_tab("organize".into()); }
        });
    });
}

fn prompt(u: &AppWindow, title: &str, text: &str, action: &str, id: &str) {
    PROMPT.with(|p| *p.borrow_mut() = Some((action.into(), id.into())));
    u.set_prompt_title(title.into());
    u.set_prompt_text(text.into());
    u.set_prompt_open(true);
}

pub fn prompt_ok(u: &AppWindow, st: &Shared, text: &str) {
    let Some((action, id)) = PROMPT.with(|p| p.borrow_mut().take()) else { return };
    let name = text.trim().to_string();
    if name.is_empty() { return; }
    let Some(c) = client(st) else { return };
    match action.as_str() {
        "new-sub" => mailbox_op(u, c, "Folder created", move |c| c.create_mailbox(&name, Some(&id)).map(|_| String::new())),
        "rename-folder" => mailbox_op(u, c, "Folder renamed", move |c| c.rename_mailbox(&id, &name).map(|_| String::new())),
        "rename-cal" => calendar::set_call(u, c, "Calendar/set", json!({"update": {id: {"name": name}}}), "Calendar not renamed"),
        "new-task" => calendar::add_task(u, c, &name, &id),
        "new-file-folder" => files::new_folder(u, c, &name),
        _ => {}
    }
}

pub fn pick(u: &AppWindow, st: &Shared, action: &str) {
    if action.is_empty() { return; }
    let t = TARGET.with(|x| x.borrow().clone());
    if let Some(a) = action.strip_prefix("ask:") {
        let label = match (t.kind.as_str(), a) {
            ("folder", "empty") => "Delete all messages permanently",
            ("folder", _) => "Delete this folder",
            ("cal", _) => "Delete calendar and its events",
            ("mail", _) => "Delete selected messages",
            _ => "Delete",
        };
        u.global::<crate::CtxHl>().set_id(t.id.clone().into());
        reopen(u, confirm(label, &format!("do:{a}")));
        return;
    }
    let action = action.strip_prefix("do:").unwrap_or(action);
    let id = t.id.as_str();
    match t.kind.as_str() {
        "mail" | "thread" => match action {
            "reply" | "reply-all" | "forward" | "print" => on_open_mail(u, id, action),
            "open" => on_open_mail(u, id, ""),
            "eml" => {
                let Some(c) = crate::client_for(st, id) else { return };
                let Some(m) = mail::email(id) else { return };
                let subj = m["subject"].as_str().filter(|s| !s.is_empty()).unwrap_or("message");
                let name = format!("{}.eml", crate::sanitize_name(subj));
                let Some(path) = rfd::FileDialog::new().set_file_name(&name).save_file() else { return };
                let blob = m["blobId"].as_str().unwrap_or("").to_string();
                let w = u.as_weak();
                std::thread::spawn(move || {
                    let res = c.download(&blob, &name, "message/rfc822").and_then(|b| std::fs::write(&path, b).map_err(|e| e.to_string()));
                    let msg = match res { Ok(()) => format!("Saved {name}"), Err(e) => format!("Could not save: {e}") };
                    let _ = w.upgrade_in_event_loop(move |u| mail::show_toast(&u, &msg, false));
                });
            }
            "copy-sender" | "find-from" => {
                let Some(a) = mail::email(id).and_then(|m| m["from"][0]["email"].as_str().map(String::from)) else { return };
                if action == "copy-sender" { u.invoke_copy_text(a.into()); mail::show_toast(u, "Copied", false); } else { u.invoke_do_search(format!("from:{a}").into()); }
            }
            a => {
                let a = if let Some(tid) = a.strip_prefix("tagt:") {
                    let has = kw(&mail::email(&t.ids[0]), &format!("$label:{tid}"));
                    format!("{}:{tid}", if has { "untag" } else { "tag" })
                } else { a.to_string() };
                // the menu asked once already (or bulk confirm is off): don't ask again in mail::apply
                if a == "delete" && t.ids.len() > 1 { mail::CONFIRM_BULK.with(|x| x.set(true)); }
                // explicit read/unread/star/unstar: mail::apply toggles "read"/"star" from state, so pass resolved forms
                crate::apply_routed(u, st, t.ids.clone(), &a);
            }
        },
        "addr" | "addr-to" => match action {
            "copy-addr" => { u.invoke_copy_text(id.into()); mail::show_toast(u, "Copied", false); }
            "compose-to" => { u.invoke_compose_new(); u.set_c_to(id.into()); }
            "find-from" => u.invoke_do_search(format!("from:{id}").into()),
            "add-contact" => {
                let name = if u.get_r_email().as_str() == id { u.get_r_from().to_string() } else { String::new() };
                u.set_app(2);
                u.invoke_app_changed(2);
                u.invoke_ct_new();
                u.set_ct_name(name.into());
                u.set_ct_emails(id.into());
            }
            _ => {}
        },
        "att" => {
            let i: i32 = id.parse().unwrap_or(0);
            match action {
                "open" => u.invoke_open_attachment(i),
                "save" => u.invoke_save_attachment(i),
                "copy-name" => {
                    let m = u.get_attachments();
                    if let Some(a) = (0..m.row_count()).filter_map(|k| m.row_data(k)).find(|a| a.index == i) { u.invoke_copy_text(a.name); }
                }
                _ => {}
            }
        }
        "body" => match action {
            "copy-sel" => { let v = u.get_ctx_body_cmd(); u.set_ctx_body_cmd(if v % 2 == 0 { v + 1 } else { v + 2 }); }
            "select-all" if id == "plain" => { let v = u.get_ctx_body_cmd(); u.set_ctx_body_cmd(if v % 2 == 0 { v + 2 } else { v + 1 }); }
            "copy-all" => {
                let text = crate::CUR.with(|c| c.borrow().as_ref().map(|c| c.text.clone())).unwrap_or_else(|| u.get_r_body().to_string());
                u.invoke_copy_text(text.into());
                mail::show_toast(u, "Copied message text", false);
            }
            a => u.invoke_act(a.into()),
        },
        "folder" => {
            let Some(c) = client(st) else { return };
            let fid = id.to_string();
            match action {
                "folder-read" => mark_read_query(u, c, json!({"inMailbox": fid, "notKeyword": "$seen"})),
                "refresh" => u.invoke_refresh(),
                "new-sub" => prompt(u, "New subfolder", "", "new-sub", id),
                "rename" => prompt(u, "Rename folder", &folder(u, id).map(|f| f.name.to_string()).unwrap_or_default(), "rename-folder", id),
                "empty" => mailbox_op(u, c, "Folder emptied", move |c| c.empty_mailbox(&fid).map(|n| format!("Deleted {n} messages"))),
                "delete" => mailbox_op(u, c, "Folder deleted", move |c| c.delete_mailbox(&fid).map(|_| String::new())),
                _ => {}
            }
        }
        "tag" => match action {
            "open" => { u.set_current_tag(id.into()); u.invoke_select_tag(id.into()); }
            "tag-read" => { if let Some(c) = client(st) { mark_read_query(u, c, json!({"hasKeyword": format!("$label:{id}"), "notKeyword": "$seen"})); } }
            "manage" => { u.set_settings_tab("organize".into()); u.invoke_open_settings(); }
            _ => {}
        },
        "list" => match action {
            "refresh" => u.invoke_refresh(),
            "select-all" => u.invoke_select_all(),
            "folder-read" => { if let Some(c) = client(st) { mark_read_query(u, c, json!({"inMailbox": u.get_current_folder().to_string(), "notKeyword": "$seen"})); } }
            _ => {}
        },
        "event" => {
            let Some(c) = client(st) else { return };
            match action {
                "edit" => u.invoke_cal_edit(id.into(), "".into(), -1),
                "duplicate" => { u.invoke_cal_edit(id.into(), "".into(), -1); u.set_ev_id("".into()); u.set_ev_title(format!("{} (copy)", u.get_ev_title()).into()); }
                "copy-title" => u.invoke_copy_text(calendar::event_title(id).into()),
                "export-ics" => {
                    let Some((name, text)) = calendar::event_ics(id) else { return };
                    let Some(path) = rfd::FileDialog::new().set_file_name(&name).add_filter("iCalendar", &["ics"]).save_file() else { return };
                    let msg = match std::fs::write(&path, text) { Ok(()) => "Event exported".to_string(), Err(e) => format!("Could not export: {e}") };
                    mail::show_toast(u, &msg, false);
                }
                r if r.starts_with("rsvp:") => {
                    let Some((pid, _)) = calendar::my_participation(id, &crate::my_addrs(st)) else { return };
                    calendar::rsvp(u, c, id, &pid, &r[5..]);
                    mail::show_toast(u, match &r[5..] { "accepted" => "Invitation accepted", "tentative" => "Answered maybe", _ => "Invitation declined" }, false);
                }
                "delete" => { u.set_ev_id(id.into()); calendar::delete(u, c); }
                _ => {}
            }
        }
        "slot" => {
            let (date, hour) = match id.strip_prefix('@') { Some(h) => (String::new(), h.parse().unwrap_or(-1)), None => (id.to_string(), -1) };
            match action {
                "new-event" => u.invoke_cal_edit("".into(), date.into(), hour),
                "new-allday" => { u.invoke_cal_edit("".into(), date.into(), hour); u.set_ev_all_day(true); }
                "new-task" => prompt(u, "New task", "", "new-task", &date),
                "today" => u.invoke_cal_nav("today".into()),
                _ => {}
            }
        }
        "task" => {
            let Some(c) = client(st) else { return };
            match action {
                "toggle" => u.invoke_task_toggle(id.into()),
                "delete" => calendar::delete_task(u, c, id),
                d if d.starts_with("due:") => {
                    let n: i64 = d[4..].parse().unwrap_or(0);
                    calendar::set_task_due(u, c, id, chrono::Local::now().date_naive() + chrono::Duration::days(n));
                }
                _ => {}
            }
        }
        "cal" => {
            let Some(c) = client(st) else { return };
            let Some((idx, name, _, _)) = calendar::calendar_info(id) else { return };
            match action {
                "new-event" => { u.invoke_cal_edit("".into(), "".into(), -1); u.set_ev_calendar(idx as i32); }
                "toggle" => u.invoke_cal_toggle(id.into()),
                "rename" => prompt(u, "Rename calendar", &name, "rename-cal", id),
                "default" => calendar::set_call(u, c, "Calendar/set", json!({"update": {id: {"isDefault": true}}}), "Could not set default"),
                "delete" => calendar::set_call(u, c, "Calendar/set", json!({"destroy": [id], "onDestroyRemoveEvents": true}), "Calendar not deleted"),
                _ => {}
            }
        }
        "contact" => {
            u.set_ct_group("".into());
            u.set_ct_editing(false);
            u.invoke_ct_select(id.into());
            match action {
                "email" => u.invoke_ct_email(),
                "edit" => u.set_ct_editing(true),
                "copy-email" => {
                    let m = u.get_contacts();
                    if let Some(r) = (0..m.row_count()).filter_map(|i| m.row_data(i)).find(|r| r.id == id) { u.invoke_copy_text(r.email); mail::show_toast(u, "Copied", false); }
                }
                "delete" => u.invoke_ct_delete(),
                _ => {}
            }
        }
        "group" => {
            u.set_ct_editing(false);
            u.invoke_ct_select_group(id.into());
            match action {
                "email" => u.invoke_ct_email(),
                "delete" => {
                    let Some(c) = client(st) else { return };
                    let (w, id) = (u.as_weak(), id.to_string());
                    std::thread::spawn(move || {
                        let acc = c.account_for("urn:ietf:params:jmap:contacts");
                        let res = c.call(json!([["ContactCard/set", {"accountId": acc, "destroy": [id]}, "d"]]));
                        let w2 = w.clone();
                        let _ = w.upgrade_in_event_loop(move |u| match res {
                            Ok(_) => { u.set_ct_group("".into()); contacts::load(w2, c); }
                            Err(e) => mail::show_toast(&u, &e, false),
                        });
                    });
                }
                _ => {}
            }
        }
        "file" => match action {
            "open" => u.invoke_fl_activate(id.into()),
            "download" => u.invoke_fl_download(id.into()),
            "rename" => u.invoke_fl_start_rename(id.into()),
            "delete" => u.invoke_fl_delete(id.into()),
            _ => {}
        },
        "files-bg" => match action {
            "new-folder" => prompt(u, "New folder", "", "new-file-folder", ""),
            "upload" => u.invoke_fl_upload(),
            "refresh" => { let ids = u.get_crumb_ids(); let last = ids.row_count().checked_sub(1).and_then(|i| ids.row_data(i)).unwrap_or_default(); u.invoke_fl_open(last); }
            _ => {}
        },
        _ => {}
    }
}
