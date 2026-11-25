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
    /// The highlighted row of the open menu, kept for a confirmation step.
    static HL: RefCell<String> = const { RefCell::new(String::new()) };
    /// The user's addresses, refreshed on every request (for RSVP).
    static MINE: RefCell<Vec<String>> = const { RefCell::new(Vec::new()) };
    static CLIENT: RefCell<Option<Arc<jmap::Client>>> = const { RefCell::new(None) };
    /// Address book chosen for the next new contact ("New contact in this address book").
    pub static BOOK_FOR_NEW: RefCell<Option<String>> = const { RefCell::new(None) };
    /// Names of the principals the file under the menu is shared with (filled in the background).
    static SHARE_NAMES: RefCell<Vec<String>> = const { RefCell::new(Vec::new()) };
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
const G_PHISH: &str = "\u{E7BA}";
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
const G_CLOCK: &str = "\u{E823}";
const G_COLOR: &str = "\u{E790}";
const G_UP: &str = "\u{E74A}";
const G_DOWN: &str = "\u{E74B}";
const G_ERASE: &str = "\u{E75C}";
const G_GROUP: &str = "\u{E716}";
const G_SHARE: &str = "\u{E72D}";
const G_CUT: &str = "\u{E8C6}";
const G_PASTE: &str = "\u{E77F}";
const G_HOME: &str = "\u{E80F}";
const G_FOLDER: &str = "\u{E8B7}";
const G_DEFAULT: &str = "\u{E735}";

// must match ContextMenuRow heights in ui/app.slint
fn row_h(i: &CtxItem) -> f32 { match i.kind { 1 => 9.0, 2 => 24.0, _ => 30.0 } }

/// Update a menu's rows in place by position: unchanged rows are not touched, the tail is
/// inserted or removed (menus are short and have no stable ids, separators repeat).
fn fill(cur: &ModelRc<CtxItem>, items: Vec<CtxItem>, install: impl FnOnce(ModelRc<CtxItem>)) {
    let Some(vm) = cur.as_any().downcast_ref::<VecModel<CtxItem>>() else { return install(ModelRc::new(VecModel::from(items))) };
    let n = vm.row_count();
    for (i, it) in items.iter().enumerate() {
        if i < n { if vm.row_data(i).as_ref() != Some(it) { vm.set_row_data(i, it.clone()); } } else { vm.push(it.clone()); }
    }
    for i in (items.len()..n).rev() { vm.remove(i); }
}

fn show(u: &AppWindow, items: Vec<CtxItem>) {
    let h: f32 = items.iter().map(row_h).sum::<f32>() + 10.0;
    u.set_ctx_h(h);
    fill(&u.get_ctx_items(), items, |m| u.set_ctx_items(m));
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
    fill(&u.get_ctx_sub_items(), items, |m| u.set_ctx_sub_items(m));
    u.set_ctx_sub_action(action.into());
}

pub fn set_mine(v: Vec<String>) { MINE.with(|m| *m.borrow_mut() = v); }

pub fn request(u: &AppWindow, kind: &str, id: &str) {
    u.global::<crate::CtxHl>().set_id(format!("{kind}:{id}").into());
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
        // the selection bar's "Report phishing": the same question as the list menu, under the button
        "phish-bar" => { t.ids = mail::checked_ids(); phish_confirm(u, &t.ids) }
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
        "att" => {
            let mut v = vec![
                it(G_OPEN, "Open", "", "open"),
                it(G_DOWNLOAD, "Save as…", "", "save"),
                it(G_EDIT, "Rename…", "", "rename"),
            ];
            // reader extras: open an attached message, Save to Files, Download all (.zip)
            v.extend(crate::reader::att_items(id));
            v.push(sep());
            v.push(it(G_COPY, "Copy file name", "", "copy-name"));
            v
        }
        // a composer chip (Bulwark: Rename… in the chip's menu)
        "cmp-att" => vec![
            it(G_EDIT, "Rename…", "", "rename"),
            danger(G_DELETE, "Remove", "", "remove"),
        ],
        "reader" => crate::reader::menu(u),
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
        "setting" if coll_id(id).is_some() => {
            let (book, cid) = coll_id(id).unwrap();
            t.kind = if book { "bookset".into() } else { "calset".into() };
            t.id = cid.clone();
            u.global::<crate::CtxHl>().set_id(format!("setting:{}set:{cid}", if book { "book" } else { "cal" }).into());
            match coll_row(u, book, &cid) {
                None => vec![],
                Some((name, default, hidden)) if !book => {
                    let order = coll_order(u, false);
                    let pos = order.iter().position(|x| *x == cid).unwrap_or(0);
                    vec![
                        header(&name),
                        it(G_RENAME, "Rename", "", "rename"),
                        sub(G_COLOR, "Colour", "sub:color"),
                        it(G_EYE, if hidden { "Show calendar" } else { "Hide calendar" }, "", "toggle"),
                        off(it(G_DEFAULT, "Set as default", "", "default"), !default),
                        sep(),
                        off(it(G_UP, "Move up", "", "up"), pos > 0),
                        off(it(G_DOWN, "Move down", "", "down"), pos + 1 < order.len()),
                        sep(),
                        danger(G_ERASE, "Clear all events", "", "ask:clear"),
                        off(danger(G_DELETE, "Delete calendar", "", "ask:delete"), !default),
                    ]
                }
                Some((name, default, _)) => vec![
                    header(&name),
                    it(G_RENAME, "Rename", "", "rename"),
                    off(it(G_DEFAULT, "Set as default", "", "default"), !default),
                    sep(),
                    off(danger(G_DELETE, "Delete address book", "", "ask:delete"), !default),
                ],
            }
        }
        "folder" | "setting" => {
            let fid = if kind == "setting" { id.strip_prefix("server.folder.rename:").unwrap_or("").to_string() } else { id.to_string() };
            if fid.is_empty() || fid.starts_with("__") { vec![] } else {
                t.kind = "folder".into();
                t.id = fid.clone();
                folder_items(u, &fid)
            }
        }
        "tag" => {
            let mut v = vec![
                it(G_OPEN, "Show messages", "", "open"),
                it(G_READ, "Mark all as read", "", "tag-read"),
                sep(),
                it(G_ADD, "New tag…", "", "new-tag"),
            ];
            // nestedTags: a tag inside this one
            if crate::tags::nested() && crate::tags::valid_id(&format!("{id}/x")) { v.push(it(G_ADD, "New tag inside…", "", "new-subtag")); }
            v.extend([
                it(G_RENAME, "Rename…", "", "rename-tag"),
                sub(G_COLOR, "Colour", "color"),
                danger(G_DELETE, "Delete tag", "", "ask:delete-tag"),
                sep(),
                it(G_SETTINGS, "Manage tags…", "", "manage"),
            ]);
            v
        }
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
                sub(G_COLOR, "Colour", "sub:color"),
                off(it(G_DEFAULT, "Set as default", "", "default"), !default),
                sep(),
                // Bulwark never offers to delete the default calendar
                off(danger(G_DELETE, "Delete calendar", "", "ask:delete"), !default),
            ],
        },
        "contact" => vec![
            it(G_OPEN, "Open", "", "open"),
            it(G_UNREAD, "Send email", "", "email"),
            it(G_EDIT, "Edit", "", "edit"),
            sep(),
            sub(G_GROUP, "Add to group", "sub:groups"),
            sub(G_TAG, "Categories", "sub:cats"),
            sep(),
            it(G_COPY, "Copy email address", "", "copy-email"),
            sep(),
            danger(G_DELETE, "Delete", "Del", "ask:delete"),
        ],
        "group" => group_items(),
        "ctnav" => {
            let (k, nid) = id.split_once(':').unwrap_or(("", ""));
            t.kind = format!("ct-{k}");
            t.id = nid.to_string();
            match k {
                "group" => { t.kind = "group".into(); let mut v = vec![header(&contacts::group_name(nid))]; v.extend(group_items()); v }
                "keyword" => vec![
                    header(nid),
                    it(G_OPEN, "Show contacts", "", "open"),
                    it(G_RENAME, "Rename category", "", "rename"),
                    sep(),
                    danger(G_DELETE, "Delete category", "", "delete"),
                ],
                "book" => match contacts::book_info(nid) {
                    None => vec![],
                    Some((name, default)) => vec![
                        header(&name),
                        it(G_ADD_CONTACT, "New contact in this address book", "", "new-contact"),
                        it(G_RENAME, "Rename", "", "rename"),
                        off(it(G_DEFAULT, "Set as default", "", "default"), !default),
                        sep(),
                        off(danger(G_DELETE, "Delete address book", "", "ask:delete"), !default),
                    ],
                },
                _ => vec![],
            }
        }
        // an item in the Files trash: put back, or delete for good
        "file" if files::is_trashed(id) => match files::node_info(id) {
            None => vec![],
            Some((name, ..)) => vec![
                header(&name),
                it(G_REFRESH, "Restore", "", "restore"),
                sep(),
                danger(G_DELETE, "Delete forever", "Del", "ask:delete"),
            ],
        },
        // the files sidebar's Favorites and Recent entries (Bulwark files-sidebar menus)
        "file-fav" | "file-recent" => match files::node_info(id) {
            None => vec![],
            Some((name, is_folder, _, _)) => {
                let fav = kind == "file-fav";
                t.kind = "file".into();
                u.global::<crate::CtxHl>().set_id(format!("file:{id}").into());
                let mut v = vec![header(&name), it(G_OPEN, "Open", "", "open")];
                if fav {
                    v.extend([sep(), it(G_STARRED, "Unstar", "", "star")]);
                } else {
                    v.push(it(G_SEARCH, "Show in folder", "", "reveal"));
                    if !is_folder { v.push(it(G_DOWNLOAD, "Download", "", "download")); }
                    v.extend([sep(), it("\u{E711}", "Remove from recent", "", "unrecent")]);
                }
                v
            }
        },
        "file" => match files::node_info(id) {
            None => vec![],
            Some((name, is_folder, shared_in, may_share)) => {
                let mut v = vec![
                    header(&name),
                    it(G_OPEN, "Open", "", "open"),
                ];
                if !is_folder { v.push(it(G_DOWNLOAD, "Download", "", "download")); }
                if may_share || (is_folder && !shared_in) { v.push(sep()); }
                if may_share { v.push(sub(G_SHARE, "Share", "sub:share")); }
                if is_folder && !shared_in {
                    v.push(if files::is_starred(id) { it(G_STARRED, "Unstar", "", "star") } else { it(G_STAR, "Star", "", "star") });
                }
                v.extend([
                    sep(),
                    it(G_RENAME, "Rename", "F2", "rename"),
                    it(G_DUP, "Duplicate", "", "duplicate"),
                    sub(G_MOVE, "Move to", "sub:file-move"),
                    it(G_CUT, "Cut", "Ctrl+X", "cut"),
                    it(G_COPY, "Copy", "Ctrl+C", "copy"),
                    sep(),
                    danger(G_DELETE, "Delete", "Del", "ask:delete"),
                ]);
                v
            }
        },
        "rte" => rte_items(u),
        // the Empty trash button: ask first, as Bulwark does
        "files-empty" => { t.kind = "files-bg".into(); confirm("Delete everything in the trash", "do:empty-trash") }
        "files-bg" if u.get_fl_in_trash() => vec![
            off(danger(G_DELETE, "Empty trash", "", "ask:empty-trash"), u.get_files().row_count() > 0),
            it(G_REFRESH, "Refresh", "", "refresh"),
        ],
        "files-bg" => vec![
            it(G_NEW_FOLDER, "New folder", "", "new-folder"),
            it(G_UPLOAD, "Upload files", "", "upload"),
            it(G_UPLOAD, "Upload folder", "", "upload-folder"),
            sep(),
            off(it(G_PASTE, "Paste", "Ctrl+V", "paste"), files::has_clipboard()),
            it(G_REFRESH, "Refresh", "", "refresh"),
        ],
        _ => vec![],
    };
    if t.kind == "file" { fetch_share_names(u, &t.id); }
    TARGET.with(|x| *x.borrow_mut() = t);
    HL.with(|h| *h.borrow_mut() = u.global::<crate::CtxHl>().get_id().to_string());
    show(u, items);
}

/// A settings row key that names a calendar / address book on the management pages: (is book, id).
fn coll_id(key: &str) -> Option<(bool, String)> {
    for (p, book) in [("calset:", false), ("bookset:", true)] { if let Some(id) = key.strip_prefix(p) { return Some((book, id.into())); } }
    let (book, rest) = if let Some(r) = key.strip_prefix("server.cal.") { (false, r) } else { (true, key.strip_prefix("server.book.")?) };
    let (_, id) = rest.split_once(':')?;
    Some((book, id.into()))
}

/// (name, is default, is hidden) from the page's section row.
fn coll_row(u: &AppWindow, book: bool, id: &str) -> Option<(String, bool, bool)> {
    let key = format!("{}set:{id}", if book { "book" } else { "cal" });
    let m = u.get_settings_rows();
    (0..m.row_count()).filter_map(|i| m.row_data(i)).find(|r| r.key == key).map(|r| (r.label.to_string(), r.desc.contains("Default"), r.desc.contains("Hidden")))
}

fn coll_order(u: &AppWindow, book: bool) -> Vec<String> {
    let p = if book { "bookset:" } else { "calset:" };
    let m = u.get_settings_rows();
    (0..m.row_count()).filter_map(|i| m.row_data(i)).filter_map(|r| r.key.strip_prefix(p).map(String::from)).collect()
}

/// Run a calendar / address book change in the background, then reload the settings page and the app.
fn coll_op(u: &AppWindow, st: &Shared, done: &'static str, f: impl FnOnce(&jmap::Client) -> Result<(), String> + Send + 'static) {
    let Some(c) = client(st) else { return };
    let w = u.as_weak();
    std::thread::spawn(move || {
        let res = f(&c);
        let w2 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| {
            mail::show_toast(&u, &match res { Ok(()) => done.to_string(), Err(e) => e }, false);
            if u.get_show_settings() { u.invoke_settings_open_tab(u.get_settings_tab()); }
            calendar::load(w2.clone(), c.clone());
            contacts::load(w2, c);
        });
    });
}

fn group_items() -> Vec<CtxItem> {
    vec![
        it(G_OPEN, "Open", "", "open"),
        it(G_RENAME, "Rename group", "", "rename"),
        sub(G_UNREAD, "Send email to group", "sub:group-mail"),
        sep(),
        danger(G_DELETE, "Delete group", "", "ask:delete"),
    ]
}

/// Ask for a name, then create a contact group (with `member` in it when given).
pub fn new_group_prompt(u: &AppWindow, member: Option<String>) {
    prompt(u, "New group", "", "new-group", &member.unwrap_or_default());
}

/// Resolve the names behind a file's shareWith principals for the Share flyout.
fn fetch_share_names(u: &AppWindow, id: &str) {
    SHARE_NAMES.with(|n| n.borrow_mut().clear());
    let ids: Vec<String> = files::shares(id).into_iter().map(|s| s.0).collect();
    if ids.is_empty() { return; }
    let Some(c) = CLIENT.with(|c| c.borrow().clone()) else { return };
    let w = u.as_weak();
    std::thread::spawn(move || {
        let names = files::principal_names(&c, &ids);
        let _ = w.upgrade_in_event_loop(move |_| SHARE_NAMES.with(|n| *n.borrow_mut() = names));
    });
}

pub fn set_client(c: Option<Arc<jmap::Client>>) { CLIENT.with(|x| *x.borrow_mut() = c); }

fn file_row(u: &AppWindow, id: &str) -> Option<crate::FileRow> {
    let m = u.get_files();
    (0..m.row_count()).filter_map(|i| m.row_data(i)).find(|f| f.id == id)
}

/// "Report to {address}?" with the report and Cancel (Bulwark phishing-report-host.tsx); nothing
/// (and a toast saying why) when there is nothing to report.
fn phish_confirm(u: &AppWindow, ids: &[String]) -> Vec<CtxItem> {
    match crate::phishflow::prepare(ids) {
        crate::phishflow::Ask::Say(text) => { mail::show_toast(u, &text, false); vec![] }
        crate::phishflow::Ask::Confirm { address, count, left_out } => {
            let mut v = vec![header(&format!("Report to {address}?"))];
            if !left_out.is_empty() { v.push(header(&left_out)); }
            v.push(danger(G_PHISH, &if count == 1 { "Report and move to Junk".to_string() } else { format!("Report {count} messages and move to Junk") }, "", "do:phish-go"));
            v.push(it("\u{E711}", "Cancel", "Esc", ""));
            v
        }
    }
}

fn mail_items(u: &AppWindow, t: &Target) -> Vec<CtxItem> {
    let n = t.ids.len();
    let first = mail::email(&t.ids[0]);
    let junk = mail::mailbox_role(&u.get_current_folder()) == "junk";
    let mut v = vec![];
    if n > 1 { v.push(header(&format!("{n} selected"))); }
    // Scheduled view (Bulwark message-actions.ts): reschedule, cancel and edit, cancel send
    if u.get_current_folder() == "__scheduled" && first.as_ref().is_some_and(|m| m["submissionId"].is_string()) {
        v.extend([
            off(it(G_CLOCK, "Reschedule", "", "sched:reschedule"), n == 1),
            off(it(G_EDIT, "Cancel and edit", "", "sched:edit"), n == 1),
            off(danger(G_CANCEL, "Cancel send", "", "sched:cancel"), n == 1),
            sep(),
        ]);
    }
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
        // Bulwark message-actions.ts: "Report phishing…" next to the spam entry, for the whole selection
        if crate::phish::setup_cached().is_some() { it(G_PHISH, "Report phishing\u{2026}", "", "phish") }
        else { off(it(G_PHISH, "Report phishing\u{2026}", "Set an address in Settings", ""), false) },
        sep(),
        sub(G_MORE, "More actions", "sub:more"),
    ]);
    // Bulwark copy_to_account: next to Move, only with another account signed in
    if crate::unified::several_accounts() {
        if let Some(at) = v.iter().position(|i| i.action == "sub:move") { v.insert(at + 1, sub(G_COPY, "Copy to account", "sub:copy-acct")); }
        prefetch_copy_targets(u, &t.ids);
    }
    v
}

// ---- copy to another account (Bulwark useCopyToAccount) ----

/// Folders of the other signed-in accounts, loaded in the background when a message menu opens
/// (or a message drag starts): one load per login at a time, a failed load is shown as failed.
static ACCT_FOLDERS: std::sync::Mutex<Option<crate::copymsg::FolderCache>> = std::sync::Mutex::new(None);

fn with_cache<R>(f: impl FnOnce(&mut crate::copymsg::FolderCache) -> R) -> R {
    f(ACCT_FOLDERS.lock().unwrap().get_or_insert_with(Default::default))
}

/// A login's folders changed (its folder list was re-read, a push said so, a copy or move filed
/// into it): forget what was loaded, the next menu loads it again.
pub fn folders_changed(login: &str) { with_cache(|c| c.invalidate(login)); }

/// The login a list row belongs to.
fn row_login(id: &str) -> String { mail::owner(id).unwrap_or_else(crate::unified::active_user) }

/// The connected logins a message of `from` can be copied to (Bulwark lists the accounts it has
/// a client for; one that is not connected has nothing to copy into).
fn other_logins(from: &str) -> Vec<String> {
    crate::oauth::accounts().iter().filter_map(|a| a["user"].as_str().map(String::from))
        .filter(|l| l != from && crate::unified::cached(l).is_some()).collect()
}

/// Same, for the reader's menu (the open message's row id).
pub fn prefetch_for(u: &AppWindow, id: &str) { prefetch_copy_targets(u, &[id.to_string()]); }

pub fn prefetch_copy_targets(u: &AppWindow, ids: &[String]) {
    let from = ids.first().map(|i| row_login(i)).unwrap_or_default();
    for login in other_logins(&from) { start_load(u.as_weak(), login); }
}

/// Load `login`'s folders unless that is running or done. When the account's folders changed
/// while it ran, its answer is dropped and a new load starts at once.
fn start_load(w: slint::Weak<AppWindow>, login: String) {
    let Some(gen) = with_cache(|c| c.begin(&login)) else { return };
    std::thread::spawn(move || {
        let res = crate::unified::cached(&login).ok_or_else(|| "The account is not connected.".to_string())
            .and_then(|c| crate::unified::mailboxes_of(&c, &c.account))
            .map(|list| crate::copymsg::folder_choices(&list));
        let taken = with_cache(|c| c.finish(&login, gen, res));
        if !taken { start_load(w, login); return; }
        // a flyout opened meanwhile shows "Loading folders…": fill it in (and a drag's targets)
        let _ = w.upgrade_in_event_loop(|u| {
            if u.get_ctx_open() && u.get_ctx_sub_action().as_str() == "sub:copy-acct" {
                u.set_ctx_sub_action("".into());
                hover(&u, "sub:copy-acct", -1);
            }
            if u.global::<crate::Pd>().get_dnd_active() { show_drag_targets(&u); }
        });
    });
}

/// The messages a menu acts on (the reader's menu acts on the open one).
fn target_ids(u: &AppWindow, t: &Target) -> Vec<String> {
    if t.kind == "reader" { vec![u.get_current_mail().to_string()] } else { t.ids.clone() }
}

/// The flyout: every other connected account with its folders.
fn copy_items(from: &str) -> Vec<CtxItem> {
    use crate::copymsg::Slot;
    let mut v = vec![];
    for login in other_logins(from) {
        if !v.is_empty() { v.push(sep()); }
        v.push(header(&login));
        match with_cache(|c| c.slot(&login).cloned()) {
            Some(Slot::Ready(list)) if !list.is_empty() => for (id, name, depth) in list {
                v.push(it(G_FOLDER, &format!("{}{name}", "   ".repeat(depth)), "", &format!("copyacct:{login}\u{1f}{id}")));
            },
            Some(Slot::Ready(_)) => v.push(off(it("", "No folders", "", ""), false)),
            Some(Slot::Failed(_)) => v.push(off(it("", "Could not load folders", "Reopen to retry", ""), false)),
            Some(Slot::Loading) | None => v.push(off(it("", "Loading folders…", "", ""), false)),
        }
    }
    if v.is_empty() { v.push(off(it("", "No other accounts", "", ""), false)); }
    v
}

/// Targets of a message drag: the other connected accounts' folders (Bulwark's sidebar shows every
/// account's folders; dropping on one of another account moves the message there).
/// Rows: kind 0 account name, 1 folder (id = login, unit separator, folder id), 2 a note.
pub fn drag_rows(from: &str) -> Vec<crate::DndAcct> {
    use crate::copymsg::Slot;
    let row = |kind: i32, id: String, label: String, depth: i32| crate::DndAcct { kind, id: id.into(), label: label.into(), depth };
    let mut v = vec![];
    for login in other_logins(from) {
        v.push(row(0, String::new(), login.clone(), 0));
        match with_cache(|c| c.slot(&login).cloned()) {
            Some(Slot::Ready(list)) if !list.is_empty() => for (id, name, depth) in list {
                v.push(row(1, format!("{login}\u{1f}{id}"), name, depth as i32));
            },
            Some(Slot::Ready(_)) => v.push(row(2, String::new(), "No folders".into(), 0)),
            Some(Slot::Failed(_)) => v.push(row(2, String::new(), "Could not load folders".into(), 0)),
            Some(Slot::Loading) | None => v.push(row(2, String::new(), "Loading folders…".into(), 0)),
        }
    }
    v
}

/// Fill the drag targets of the folder pane for the drag in progress.
pub fn show_drag_targets(u: &AppWindow) {
    let id = u.global::<crate::Pd>().get_dnd_id().to_string();
    let ids = crate::dnd::ids_for_mail(&id);
    let from = ids.first().map(|i| row_login(i)).unwrap_or_default();
    let rows = if crate::unified::several_accounts() { drag_rows(&from) } else { vec![] };
    u.global::<crate::Pd>().set_dnd_accts(ModelRc::new(VecModel::from(rows)));
}

/// The path of the folder a drop on `spec` ("login", unit separator, folder id) lands in, for the toast.
pub fn drop_path(spec: &str) -> String {
    use crate::copymsg::Slot;
    let (login, mailbox) = spec.split_once('\u{1f}').unwrap_or((spec, ""));
    let path = match with_cache(|c| c.slot(login).cloned()) {
        Some(Slot::Ready(list)) => crate::copymsg::path_of(&list, mailbox),
        _ => None,
    };
    // the folders were forgotten meanwhile (a push): name the account rather than nothing
    path.filter(|p| !p.is_empty()).unwrap_or_else(|| login.to_string())
}

/// Copy `ids` into folder `spec` ("login", unit separator, mailbox id) of another account. Runs
/// in the background; one toast reports the whole run, failures included. Nothing is removed.
fn copy_to_account(u: &AppWindow, st: &Shared, ids: Vec<String>, spec: &str) {
    transfer(u, st, ids, spec, true);
}

/// Copy (`keep_original`) or move `ids` into a folder of another account (Bulwark
/// crossAccountMoveEmails). A move removes each source message only after its copy is confirmed;
/// the rows that moved leave the list, and both accounts' folders and counts are re-read.
pub fn transfer(u: &AppWindow, st: &Shared, ids: Vec<String>, spec: &str, keep_original: bool) {
    let Some((dest_login, mailbox)) = spec.split_once('\u{1f}') else { return };
    let (dest_login, mailbox) = (dest_login.to_string(), mailbox.to_string());
    let path = drop_path(spec);
    let active = crate::unified::active_user();
    let mut clients: std::collections::HashMap<String, Arc<jmap::Client>> = Default::default();
    for id in &ids {
        let l = row_login(id);
        let c = if l == active { client(st) } else { crate::unified::cached(&l) };
        if let Some(c) = c { clients.entry(l).or_insert(c); }
    }
    let rows: Vec<(String, String, String, Option<String>)> = ids.iter().map(|i| (i.clone(), row_login(i), crate::unified::real_id(i).to_string(), mail::shared_account(i))).collect();
    let n = ids.len();
    let verb = if keep_original { "Copying" } else { "Moving" };
    mail::show_toast(u, &format!("{verb} {n} message{}…", if n == 1 { "" } else { "s" }), false);
    let w = u.as_weak();
    std::thread::spawn(move || {
        let mut touched: Vec<String> = vec![dest_login.clone()];
        let mut moved_rows: Vec<String> = vec![];
        let msg = (|| {
            let dst = crate::unified::cached(&dest_login).ok_or("The other account is not connected.")?;
            let items: Vec<(String, crate::copymsg::Place)> = rows.iter().map(|(_, login, id, shared)| {
                let account = shared.clone().or_else(|| clients.get(login).map(|c| c.account.clone())).unwrap_or_default();
                (id.clone(), crate::copymsg::Place { login: login.clone(), account })
            }).collect();
            let dest = crate::copymsg::Dest { place: crate::copymsg::Place { login: dest_login.clone(), account: dst.account.clone() }, mailbox };
            let side = |l: &str| clients.get(l).map(|c| c.as_ref() as &dyn crate::copymsg::Side);
            // one run per source place: an id is only unique within its account, so what moved is
            // matched to its rows within that place
            let mut places: Vec<crate::copymsg::Place> = vec![];
            for (_, p) in &items { if !places.contains(p) { places.push(p.clone()); } }
            let mut rep = crate::copymsg::Report::default();
            for place in places {
                let group: Vec<(String, crate::copymsg::Place)> = items.iter().filter(|(_, p)| *p == place).cloned().collect();
                let plan = crate::copymsg::plan(&group, &dest.place);
                let r = crate::copymsg::run(&plan, &dest, dst.as_ref(), &side, keep_original);
                if !keep_original {
                    let moved: Vec<String> = rows.iter().zip(&items)
                        .filter(|(row, (id, p))| *p == place && r.copied.contains(id) && !r.failed.iter().any(|f| f.0 == *id) && row.1 == place.login)
                        .map(|(row, _)| row.0.clone()).collect();
                    touched.extend(moved.iter().map(|_| place.login.clone()));
                    moved_rows.extend(moved);
                }
                rep.copied.extend(r.copied);
                rep.failed.extend(r.failed);
            }
            Ok::<String, String>(if keep_original { crate::copymsg::summary(&rep, true) } else { crate::copymsg::move_toast(&rep, &path) })
        })().unwrap_or_else(|e: String| format!("Could not {}: {e}", if keep_original { "copy" } else { "move" }));
        touched.sort();
        touched.dedup();
        let _ = w.upgrade_in_event_loop(move |u| {
            mail::show_toast(&u, &msg, false);
            if !moved_rows.is_empty() { mail::moved_away(&u, &moved_rows); }
            refresh_accounts(&u, &touched);
        });
    });
}

/// Re-read the folders and counts of the accounts a copy or move touched (Bulwark refreshes the
/// folder lists of every account involved): the open account's tree, the others' cached pickers
/// and inbox counts.
pub fn refresh_accounts(u: &AppWindow, logins: &[String]) {
    let active = crate::unified::active_user();
    let mut others = false;
    for l in logins {
        folders_changed(l);
        if *l == active {
            if let Some(c) = crate::unified::cached(l) { mail::load_mailboxes(u.as_weak(), c, false); }
        } else { others = true; }
    }
    if others { crate::unified::refresh_other_logins(u.as_weak(), true); }
}

fn folder_items(u: &AppWindow, id: &str) -> Vec<CtxItem> {
    let role = mail::mailbox_role(id);
    let name = folder(u, id).map(|f| f.name.to_string()).unwrap_or_default();
    let mut v = vec![
        header(&name),
        it(G_READ, "Mark folder as read", "", "folder-read"),
    ];
    let boxes = mail::mailbox_list();
    if crate::folders::has_children(&boxes, id) {
        // nothing to do below a folder without unread mail (Bulwark mailbox-context-menu.tsx:58-70)
        v.push(off(it(G_READ, "Mark folder and subfolders as read", "", "folder-read-tree"), crate::folders::subtree_unread(&boxes, id) > 0));
    }
    // Bulwark folder settings: subscribe, pin, notification mute, counter, icon, standard role
    let subscribed = mail::mailbox_list().iter().find(|m| m["id"] == id).is_none_or(|m| m["isSubscribed"].as_bool() != Some(false));
    v.extend([
        it(G_REFRESH, "Refresh", "", "refresh"),
        sep(),
        it("\u{E718}", if mail::is_pinned(id) { "Unpin from top" } else { "Pin to top" }, "", "pin"),
    ]);
    // nothing arrives in Trash, Spam, Sent or Drafts, so there is nothing to mute (mailbox-context-menu.tsx:316,393)
    if !crate::folders::mute_hidden(&role) {
        v.push(it("\u{E74F}", if mail::is_muted(id) { "Unmute notifications" } else { "Mute notifications" }, "", "mute"));
    }
    v.extend([
        it(if subscribed { "\u{E711}" } else { "\u{E710}" }, if subscribed { "Unsubscribe" } else { "Subscribe" }, "", "subscribe"),
        sub("\u{E8FD}", "Counter", "sub:fcount"),
        sub("\u{E790}", "Icon", "sub:ficon"),
        sub(G_TAG, "Use as standard folder", "sub:frole"),
        sep(),
        it(G_NEW_FOLDER, "New subfolder", "", "new-sub"),
        off(it(G_RENAME, "Rename", "", "rename"), role.is_empty()),
        // Bulwark mail import: .eml and .mbox files into this folder (Email/import)
        it(G_UPLOAD, "Import messages (.eml, .mbox)…", "", "import"),
        // Bulwark "keep folders": each .mbox into its own subfolder named from the file
        it(G_UPLOAD, "Import .mbox files as subfolders…", "", "import-folders"),
        // Bulwark mail export: the folder as .mbox or as .eml files
        it(G_DOWNLOAD, "Export folder as .mbox…", "", "export-mbox"),
        it(G_DOWNLOAD, "Export folder as .eml files…", "", "export-eml"),
        // Bulwark whole-account export (no mailbox): every folder, nested as the folders are
        it(G_DOWNLOAD, "Export all mail as .mbox files…", "", "export-all-mbox"),
        it(G_DOWNLOAD, "Export all mail as .eml files…", "", "export-all-eml"),
    ]);
    if crate::mailexport::running() { v.push(it(G_DELETE, "Cancel export", "", "export-cancel")); }
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
        // folder settings (Bulwark folder-settings.tsx): own counter, icon, standard role
        "fcount" => {
            let prefs = crate::settings::Settings::load();
            let have = crate::folders::decode_count_modes(prefs.prefs.get(crate::folders::KEY_COUNT_MODES)).get(&t.id).copied();
            let mut d = it("", "Default", "", "fcount:default");
            d.checked = have.is_none();
            v.push(d);
            for m in crate::folders::CountMode::ALL {
                let mut i = it("", m.label(), "", &format!("fcount:{}", m.name()));
                i.checked = have == Some(m);
                v.push(i);
            }
        }
        "ficon" => {
            let prefs = crate::settings::Settings::load();
            let have = crate::folders::decode_icons(prefs.prefs.get(crate::folders::KEY_ICONS)).get(&t.id).cloned();
            let mut d = it("", "Default", "", "ficon:default");
            d.checked = have.is_none();
            v.push(d);
            for (name, glyph) in crate::folders::ICON_CHOICES {
                let mut i = it(glyph, name, "", &format!("ficon:{name}"));
                i.checked = have.as_deref() == Some(name);
                v.push(i);
            }
        }
        "frole" => {
            let own = mail::mailbox_role(&t.id);
            for (role, label) in crate::folders::STANDARD_ROLES {
                let mut i = it("", label, "", &format!("frole:{role}"));
                i.checked = own == role;
                v.push(i);
            }
            v.push(sep());
            v.push(off(it("", "No role", "", "frole:none"), !own.is_empty()));
        }
        "copy-acct" => v = copy_items(&row_login(target_ids(u, t).first().map(String::as_str).unwrap_or(""))),
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
                off(it("\u{E943}", "View source", "", "rx:source"), single),
                off(it(G_FORWARD, "Forward as attachment", "", "rx:fwd-att"), single),
                sep(),
                off(it(G_COPY, "Copy sender address", "", "copy-sender"), single),
                off(it(G_SEARCH, "Find emails from sender", "", "find-from"), single),
            ]);
        }
        r if r.starts_with("rx-") => v = crate::reader::submenu(u, r, &t.id),
        "file-move" => {
            for (k, label) in files::move_targets(&t.id) {
                v.push(it(if k.is_empty() { G_HOME } else { G_FOLDER }, &label, "", &format!("moveto:{k}")));
            }
            if v.is_empty() { v.push(off(it("", "No other folders", "", ""), false)); }
        }
        "share" => {
            v.push(it(G_ADD_CONTACT, "Share read-only…", "", "share-add:r"));
            v.push(it(G_ADD_CONTACT, "Share with edit rights…", "", "share-add:w"));
            let cur = files::shares(&t.id);
            if !cur.is_empty() {
                v.push(sep());
                v.push(header("Shared with"));
                let names = SHARE_NAMES.with(|n| n.borrow().clone());
                for (i, (pid, write)) in cur.iter().enumerate() {
                    let who = names.get(i).cloned().unwrap_or_else(|| pid.clone());
                    v.push(it(G_DELETE, &format!("Stop sharing with {who}{}", if *write { " (edit)" } else { "" }), "", &format!("unshare:{pid}")));
                }
            }
        }
        "groups" => {
            for (gid, name, member) in contacts::groups_for(&t.id) {
                let mut i = it(G_GROUP, &name, "", &format!("grp:{gid}"));
                i.checked = member;
                v.push(i);
            }
            if !v.is_empty() { v.push(sep()); }
            v.push(it(G_ADD, "New group…", "", "grp-new"));
        }
        "cats" => {
            let mine = contacts::keywords_of(&t.id);
            for (k, _) in contacts::all_keywords() {
                let mut i = it(G_TAG, &k, "", &format!("kw:{k}"));
                i.checked = mine.contains(&k);
                v.push(i);
            }
            if !v.is_empty() { v.push(sep()); }
            v.push(it(G_ADD, "New category…", "", "kw-new"));
        }
        "group-mail" => {
            v.push(it(G_UNREAD, "To", "", "email:to"));
            v.push(it(G_UNREAD, "Cc", "", "email:cc"));
            v.push(it(G_UNREAD, "Bcc", "", "email:bcc"));
        }
        "color" if t.kind == "tag" => {
            let cur = crate::tags::defs().into_iter().find(|d| d.id == t.id).map(|d| d.color);
            for name in crate::tags::PALETTE {
                let mut i = it("", &format!("{}{}", name[..1].to_uppercase(), &name[1..]), "", &format!("color:{name}"));
                i.color = slint::Color::from_argb_encoded(0xff000000 | crate::tags::color_rgb(name));
                i.checked = cur.as_deref() == Some(name);
                v.push(i);
            }
        }
        "color" => {
            let cur = if t.kind == "cal" { calendar::calendar_color(&t.id) } else { coll_color(u, &t.id) };
            for (hex, name) in crate::server_settings::CAL_COLORS {
                let mut i = it("", name, "", &format!("color:{hex}"));
                i.color = hex_color(hex);
                i.checked = cur.as_deref() == Some(hex);
                v.push(i);
            }
        }
        "rte-format" | "rte-para" | "rte-table" | "rte-ai" => v = rte_sub(which, u.get_ai_busy()),
        "due" => {
            v.push(it(G_CALENDAR, "Today", "", "due:0"));
            v.push(it(G_CALENDAR, "Tomorrow", "", "due:1"));
            v.push(it(G_CALENDAR, "Next week", "", "due:7"));
            if calendar::task_has_due(&t.id) { v.push(it(G_CALENDAR, "No due date", "", "due:none")); }
        }
        _ => {}
    }
    v
}

pub fn hex_color(h: &str) -> slint::Color {
    let n = u32::from_str_radix(h.trim_start_matches('#'), 16).unwrap_or(0x3b82f6);
    slint::Color::from_rgb_u8((n >> 16) as u8, (n >> 8) as u8, n as u8)
}

/// The calendar's colour as the page's colour select shows it.
fn coll_color(u: &AppWindow, id: &str) -> Option<String> {
    let key = format!("server.cal.color:{id}");
    let m = u.get_settings_rows();
    (0..m.row_count()).filter_map(|i| m.row_data(i)).find(|r| r.key == key).map(|r| r.text.to_string())
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
            // only what changed (Email/queryChanges + Email/changes), not the whole list
            mail::sync(w2, c);
        });
    });
}

/// Mark the unread mail of a folder and all its subfolders as read (batched by maxObjectsInSet),
/// then refresh the folders and the list.
fn mark_tree_read(u: &AppWindow, c: Arc<jmap::Client>, folders: Vec<String>) {
    let w = u.as_weak();
    std::thread::spawn(move || {
        let res = c.mark_folders_read(&folders);
        let w2 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| {
            let msg = match res {
                Ok((n, bad)) if bad.is_empty() => format!("Marked {n} as read"),
                Ok((n, bad)) => format!("Marked {n} as read, {} could not be changed", bad.len()),
                Err(e) => format!("Could not mark as read: {e}"),
            };
            mail::show_toast(&u, &msg, false);
            mail::load_mailboxes(w2.clone(), c.clone(), false);
            mail::sync(w2, c);
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
            // the delta sync falls back to a full reload when the list's states are gone
            // (a deleted folder that was on screen)
            mail::sync(w2, c);
            if u.get_show_settings() && u.get_settings_tab() == "organize" { u.invoke_settings_open_tab("organize".into()); }
        });
    });
}

/// "New tag" (or a tag inside `parent`, with nestedTags).
pub fn new_tag_prompt(u: &AppWindow, parent: Option<&str>) {
    let title = match parent.and_then(|p| crate::tags::defs().into_iter().find(|d| d.id == p)) {
        Some(p) => format!("New tag inside {}", p.label),
        None => "New tag".to_string(),
    };
    prompt(u, &title, "", "new-tag", parent.unwrap_or(""));
}

/// Change the user's tags, save them and redraw the tag list and the rows' colours.
fn edit_tags(u: &AppWindow, f: impl FnOnce(&mut Vec<crate::tags::TagDef>) -> Result<String, String>) {
    let mut defs = crate::tags::defs();
    match f(&mut defs) {
        Ok(msg) => {
            crate::tags::save(&defs);
            mail::rebuild_tags(u);
            mail::rebuild_list(u);
            if !msg.is_empty() { mail::show_toast(u, &msg, false); }
        }
        Err(e) => mail::show_error(u, &e),
    }
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
    match action.as_str() {
        "rename-att" | "rename-cmp-att" => {
            let i: usize = id.parse().unwrap_or(usize::MAX);
            let old = if action == "rename-att" { crate::attachments::reader_name(i) } else { crate::compose::attachment_name(i) };
            let Some(old) = old else { return };
            match crate::attachments::renamed(&old, &name) {
                Err(why) => mail::show_error(u, why),
                Ok(None) => {}
                Ok(Some(new)) => if action == "rename-att" { crate::attachments::rename_in_reader(u, i, &new) } else { crate::compose::rename_attachment(u, i, &new) },
            }
            return;
        }
        "new-tag" => {
            let parent = (!id.is_empty()).then_some(id.as_str());
            return edit_tags(u, |d| crate::tags::add(d, parent, &name).map(|_| "Tag created".to_string()));
        }
        "rename-tag" => return edit_tags(u, |d| crate::tags::rename(d, &id, &name).map(|_| String::new())),
        _ => {}
    }
    let Some(c) = client(st) else { return };
    match action.as_str() {
        "new-sub" => mailbox_op(u, c, "Folder created", move |c| c.create_mailbox(&name, Some(&id)).map(|_| String::new())),
        "rename-folder" => mailbox_op(u, c, "Folder renamed", move |c| c.rename_mailbox(&id, &name).map(|_| String::new())),
        "rename-cal" => calendar::set_call(u, c, "Calendar/set", json!({"update": {id: {"name": name}}}), "Calendar not renamed"),
        "new-task" => calendar::add_task(u, c, &name, &id),
        "new-group" => contacts::create_group(u, c, &name, if id.is_empty() { None } else { Some(id) }),
        "rename-group" => contacts::card_set(u, c, json!({"update": { id: {"name": {"full": name}} }}), "Group renamed"),
        "new-kw" => contacts::set_keyword(u, c, &id, &name, true),
        "rename-kw" => contacts::rename_keyword(u, c, &id, &name),
        "rename-calset" | "rename-book" => {
            let book = action == "rename-book";
            coll_op(u, st, "Renamed", move |c| crate::server_settings::collection_set(c, book, json!({"update": { id: {"name": name} }})));
        }
        "new-file-folder" => files::new_folder(u, c, &name),
        "rename-file" => files::rename(u, c, &id, &name),
        "share-r" | "share-w" => files::share(u, c, &id, &name, Some(action == "share-w")),
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
            // a file outside the trash only moves there; trashed and shared items go for good
            ("file", _) if !files::is_trashed(&t.id) && !files::is_shared(&t.id) => "Move to trash",
            ("file", _) => "Delete for good",
            ("files-bg", "empty-trash") => "Delete everything in the trash",
            ("calset", "clear") => "Delete every event in this calendar",
            ("calset", _) => "Delete calendar and its events",
            ("bookset", _) | ("ct-book", _) => "Delete address book and its contacts",
            ("group", _) => "Delete group",
            ("tag", _) => "Delete this tag (messages keep it)",
            ("mail", _) => "Delete selected messages",
            _ => "Delete",
        };
        u.global::<crate::CtxHl>().set_id(HL.with(|h| h.borrow().clone()).into());
        reopen(u, confirm(label, &format!("do:{a}")));
        return;
    }
    if action == "phish" && matches!(t.kind.as_str(), "mail" | "thread") {
        // "Report phishing?" with the address, for the whole selection (Bulwark phishing-report-host.tsx)
        let v = phish_confirm(u, &target_ids(u, &t));
        if !v.is_empty() {
            u.global::<crate::CtxHl>().set_id(HL.with(|h| h.borrow().clone()).into());
            reopen(u, v);
        }
        return;
    }
    if action == "do:phish-go" { crate::phishflow::run(u, st, target_ids(u, &t)); return; }
    if let Some(spec) = action.strip_prefix("copyacct:") { copy_to_account(u, st, target_ids(u, &t), spec); return; }
    let action = action.strip_prefix("do:").unwrap_or(action);
    let id = t.id.as_str();
    match t.kind.as_str() {
        "mail" | "thread" => match action {
            s if s.starts_with("sched:") => {
                let Some(c) = crate::client_for(st, id) else { return };
                crate::schedule::action(u, c, id, &s[6..]);
            }
            "reply" | "reply-all" | "forward" | "print" => on_open_mail(u, id, action),
            a if a.starts_with("rx:") => on_open_mail(u, id, a),
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
        "reader" => crate::reader::pick(u, st, action, id),
        "cmp-att" => {
            let i: i32 = id.parse().unwrap_or(-1);
            match action {
                "remove" => u.invoke_remove_attachment(i),
                "rename" => {
                    let Some(name) = crate::compose::attachment_name(i.max(0) as usize).filter(|_| i >= 0) else { return };
                    let (stem, ext) = crate::attachments::split_stem_ext(&name);
                    prompt(u, &if ext.is_empty() { "Rename attachment".to_string() } else { format!("Rename attachment (keeps {ext})") }, &stem, "rename-cmp-att", id);
                }
                _ => {}
            }
        }
        "att" if action.starts_with("rx") => crate::reader::pick(u, st, action, id),
        "att" => {
            let i: i32 = id.parse().unwrap_or(0);
            match action {
                "open" => u.invoke_open_attachment(i),
                "save" => u.invoke_save_attachment(i),
                "rename" => {
                    let Some(name) = crate::attachments::reader_name(i as usize) else { return };
                    // only the part before the extension is edited, so the file keeps its type
                    let (stem, ext) = crate::attachments::split_stem_ext(&name);
                    prompt(u, &if ext.is_empty() { "Rename attachment".to_string() } else { format!("Rename attachment (keeps {ext})") }, &stem, "rename-att", id);
                }
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
        "rte" => crate::rte::ctx_pick(u, action),
        "folder" => {
            let Some(c) = client(st) else { return };
            let fid = id.to_string();
            match action {
                "folder-read" => mark_read_query(u, c, json!({"inMailbox": fid, "notKeyword": "$seen"})),
                "folder-read-tree" => mark_tree_read(u, c, crate::folders::subtree(&mail::mailbox_list(), &fid)),
                "pin" => {
                    let pin = !mail::is_pinned(&fid);
                    let key = mail::pin_key_of(&fid);
                    crate::settings::update_pref(crate::folders::KEY_PINNED, |v| crate::folders::encode_pin(v, &key, pin));
                    mail::rebuild_folders(u);
                }
                "mute" => {
                    let account = mail::account_id();
                    let mut muted = false;
                    crate::settings::update_pref(crate::folders::KEY_MUTES, |v| {
                        let mut m = crate::folders::Mutes::decode(v);
                        muted = !m.muted(&account, &fid);
                        m.set_muted(&account, &fid, muted);
                        m.encode()
                    });
                    mail::rebuild_folders(u);
                    mail::show_toast(u, if muted { "New mail in this folder stays quiet" } else { "Notifications back on for this folder" }, false);
                }
                "subscribe" => {
                    let on = mail::mailbox_list().iter().find(|m| m["id"] == id).is_some_and(|m| m["isSubscribed"].as_bool() == Some(false));
                    if on { mailbox_op(u, c, "Subscribed", move |c| c.set_mailbox_subscribed(&fid, true).map(|_| String::new())) }
                    else { mailbox_op(u, c, "Unsubscribed", move |c| c.set_mailbox_subscribed(&fid, false).map(|_| String::new())) }
                }
                a if a.starts_with("fcount:") => {
                    let mode = crate::folders::CountMode::parse(&a[7..]);
                    crate::settings::update_pref(crate::folders::KEY_COUNT_MODES, |v| crate::folders::encode_count_mode(v, &fid, mode));
                    mail::rebuild_folders(u);
                }
                a if a.starts_with("ficon:") => {
                    let icon = Some(&a[6..]).filter(|n| *n != "default");
                    crate::settings::update_pref(crate::folders::KEY_ICONS, |v| crate::folders::encode_icon(v, &fid, icon));
                    mail::rebuild_folders(u);
                }
                a if a.starts_with("frole:") || a.starts_with("frole-go:") => {
                    let (go, r) = match a.strip_prefix("frole-go:") { Some(r) => (true, r), None => (false, &a[6..]) };
                    let role = Some(r).filter(|r| *r != "none").map(String::from);
                    let list = mail::mailbox_list();
                    // the Inbox, Sent and Drafts roles decide where mail goes: ask before taking one from its folder
                    if !go && crate::folders::role_needs_confirm(&list, &fid, role.as_deref()) {
                        let own = mail::mailbox_role(&fid);
                        let label = if matches!(own.as_str(), "inbox" | "sent" | "drafts") && own != r {
                            format!("Take the {own} role away from this folder")
                        } else { format!("Move the {r} role here from the folder that has it") };
                        u.global::<crate::CtxHl>().set_id(HL.with(|h| h.borrow().clone()).into());
                        reopen(u, confirm(&label, &format!("do:frole-go:{r}")));
                        return;
                    }
                    mailbox_op(u, c, "Folder role changed", move |c| c.set_mailbox_role(&list, &fid, role.as_deref()).map(|_| String::new()));
                }
                "refresh" => u.invoke_refresh(),
                "new-sub" => prompt(u, "New subfolder", "", "new-sub", id),
                "import" => crate::mailimport::start(u, c, &fid, false),
                "import-folders" => crate::mailimport::start(u, c, &fid, true),
                "export-all-mbox" | "export-all-eml" => crate::mailexport::start_all(u, c, action == "export-all-mbox"),
                "export-mbox" | "export-eml" => {
                    let name = folder(u, id).map(|f| f.name.to_string()).unwrap_or_default();
                    crate::mailexport::start(u, c, &fid, &name, action == "export-mbox");
                }
                "export-cancel" => crate::mailexport::cancel(),
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
            "new-tag" => new_tag_prompt(u, None),
            "new-subtag" => new_tag_prompt(u, Some(id)),
            "rename-tag" => {
                let label = crate::tags::defs().into_iter().find(|d| d.id == id).map(|d| d.label).unwrap_or_default();
                prompt(u, "Rename tag", &label, "rename-tag", id);
            }
            "delete-tag" => edit_tags(u, |d| crate::tags::remove(d, id).map(|_| "Tag deleted".to_string())),
            a if a.starts_with("color:") => { let c = a[6..].to_string(); edit_tags(u, |d| { crate::tags::set_color(d, id, &c); Ok(String::new()) }); }
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
                "due:none" => calendar::clear_task_due(u, c, id),
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
                "default" => { let cid = id.to_string(); coll_op(u, st, "Default changed", move |c| crate::server_settings::default_collection(c, false, &cid)); }
                "delete" => calendar::set_call(u, c, "Calendar/set", json!({"destroy": [id], "onDestroyRemoveEvents": true}), "Calendar not deleted"),
                a if a.starts_with("color:") => calendar::set_color(u, c, id, &a[6..]),
                _ => {}
            }
        }
        "calset" | "bookset" => {
            let book = t.kind == "bookset";
            let cid = id.to_string();
            match action {
                "rename" => prompt(u, if book { "Rename address book" } else { "Rename calendar" }, &coll_row(u, book, id).map(|r| r.0).unwrap_or_default(), if book { "rename-book" } else { "rename-calset" }, id),
                "toggle" => {
                    let show = coll_row(u, false, id).is_some_and(|r| r.2);
                    coll_op(u, st, if show { "Calendar shown" } else { "Calendar hidden" }, move |c| crate::server_settings::collection_set(c, false, json!({"update": { cid: {"isVisible": show} }})));
                }
                "default" => coll_op(u, st, "Default changed", move |c| crate::server_settings::default_collection(c, book, &cid)),
                "up" | "down" => {
                    let mut order = coll_order(u, false);
                    let Some(i) = order.iter().position(|x| *x == cid) else { return };
                    let j = if action == "up" { i.wrapping_sub(1) } else { i + 1 };
                    if j >= order.len() { return; }
                    order.swap(i, j);
                    let upd: serde_json::Map<String, Value> = order.iter().enumerate().map(|(n, x)| (x.clone(), json!({"sortOrder": n}))).collect();
                    coll_op(u, st, "Order changed", move |c| crate::server_settings::collection_set(c, false, json!({"update": upd})));
                }
                "clear" => coll_op(u, st, "Events cleared", move |c| {
                    let acc = c.account_for("urn:ietf:params:jmap:calendars");
                    loop {
                        let q = c.call(json!([["CalendarEvent/query", {"accountId": acc, "filter": {"inCalendar": cid}, "limit": c.max_set()}, "q"]]))?;
                        let ids = q[0][1]["ids"].as_array().cloned().unwrap_or_default();
                        if ids.is_empty() { return Ok(()); }
                        let r = c.call(json!([["CalendarEvent/set", {"accountId": acc, "destroy": ids}, "d"]]))?;
                        if r[0][1]["destroyed"].as_array().is_none_or(|a| a.is_empty()) { return Err("The server deleted nothing".into()); }
                    }
                }),
                "delete" => {
                    let args = if book { json!({"destroy": [cid], "onDestroyRemoveContents": true}) } else { json!({"destroy": [cid], "onDestroyRemoveEvents": true}) };
                    coll_op(u, st, if book { "Address book deleted" } else { "Calendar deleted" }, move |c| crate::server_settings::collection_set(c, book, args));
                }
                a if a.starts_with("color:") => {
                    let hex = a[6..].to_string();
                    coll_op(u, st, "Colour changed", move |c| crate::server_settings::collection_set(c, false, json!({"update": { cid: {"color": hex} }})));
                }
                _ => {}
            }
        }
        "contact" if action.starts_with("grp") || action.starts_with("kw") => {
            let Some(c) = client(st) else { return };
            let uid = contacts::uid_of(id).unwrap_or_default();
            if action == "grp-new" { new_group_prompt(u, Some(uid)); return; }
            if action == "kw-new" { prompt(u, "New category", "", "new-kw", id); return; }
            if let Some(g) = action.strip_prefix("grp:") {
                let on = !contacts::groups_for(id).iter().any(|x| x.0 == g && x.2);
                contacts::set_member(u, c, g, &uid, on);
            } else if let Some(k) = action.strip_prefix("kw:") {
                let on = !contacts::keywords_of(id).iter().any(|x| x == k);
                contacts::set_keyword(u, c, id, k, on);
            }
        }
        "ct-keyword" => {
            let Some(c) = client(st) else { return };
            match action {
                "open" => { contacts::set_filter(u, &format!("kw:{id}")); u.set_ct_group("".into()); }
                "rename" => prompt(u, "Rename category", id, "rename-kw", id),
                "delete" => contacts::delete_keyword(u, c, id),
                _ => {}
            }
        }
        "ct-book" => {
            let book = id.to_string();
            match action {
                "new-contact" => { u.invoke_ct_new(); BOOK_FOR_NEW.with(|b| *b.borrow_mut() = Some(book)); }
                "rename" => prompt(u, "Rename address book", &contacts::book_info(id).map(|b| b.0).unwrap_or_default(), "rename-book", id),
                "default" => coll_op(u, st, "Default changed", move |c| crate::server_settings::default_collection(c, true, &book)),
                "delete" => coll_op(u, st, "Address book deleted", move |c| crate::server_settings::collection_set(c, true, json!({"destroy": [book], "onDestroyRemoveContents": true}))),
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
                "email" | "email:to" => u.invoke_ct_email(),
                "email:cc" | "email:bcc" => {
                    let to = u.get_ct_emails();
                    u.invoke_compose_new();
                    if action == "email:cc" { u.set_c_cc(to); } else { u.set_c_bcc(to); }
                }
                "rename" => prompt(u, "Rename group", &contacts::group_name(id), "rename-group", id),
                "open" => contacts::set_filter(u, &format!("group:{id}")),
                "delete" => {
                    let Some(c) = client(st) else { return };
                    let (w, id) = (u.as_weak(), id.to_string());
                    std::thread::spawn(move || {
                        let acc = c.account_for("urn:ietf:params:jmap:contacts");
                        let res = c.call(json!([["ContactCard/set", {"accountId": acc, "destroy": [id]}, "d"]]));
                        let w2 = w.clone();
                        let _ = w.upgrade_in_event_loop(move |u| match res {
                            Ok(_) => { u.set_ct_group("".into()); contacts::load(w2, c); }
                            Err(e) => mail::show_error(&u, &e),
                        });
                    });
                }
                _ => {}
            }
        }
        "file" => {
            let Some(c) = client(st) else { return };
            match action {
                "open" => u.invoke_fl_activate(id.into()),
                "download" => u.invoke_fl_download(id.into()),
                "rename" => {
                    // a folder from the sidebar may not be listed in the folder on screen: rename by prompt
                    if file_row(u, id).is_some() { u.invoke_fl_start_rename(id.into()); }
                    else { prompt(u, "Rename", &files::node_info(id).map(|n| n.0).unwrap_or_default(), "rename-file", id); }
                }
                "delete" => u.invoke_fl_delete(id.into()),
                "restore" => files::restore(u, c, id),
                "star" => files::toggle_star(u, id),
                "unrecent" => files::remove_recent(u, id),
                "reveal" => { if let Some(p) = files::parent_of(id) { files::open(u, c, &p); } }
                "duplicate" => files::duplicate(u, c, id),
                "cut" => files::set_clipboard(u, id, true),
                "copy" => files::set_clipboard(u, id, false),
                a if a.starts_with("moveto:") => files::move_to(u, c, id, &a[7..]),
                a if a.starts_with("share-add:") => prompt(u, if a.ends_with('w') { "Share with edit rights: user's email" } else { "Share read-only: user's email" }, "", if a.ends_with('w') { "share-w" } else { "share-r" }, id),
                a if a.starts_with("unshare:") => files::share(u, c, id, &a[8..], None),
                _ => {}
            }
        }
        "files-bg" => match action {
            "new-folder" => prompt(u, "New folder", "", "new-file-folder", ""),
            "upload" => u.invoke_fl_upload(),
            "upload-folder" => u.invoke_fl_upload_folder(),
            "empty-trash" => u.invoke_fl_empty_trash(),
            "paste" => { if let Some(c) = client(st) { files::paste(u, c); } }
            "refresh" => { let ids = u.get_crumb_ids(); let last = ids.row_count().checked_sub(1).and_then(|i| ids.row_data(i)).unwrap_or_default(); u.invoke_fl_open(last); }
            _ => {}
        },
        _ => {}
    }
}

// ---- the composer's editor (Bulwark: composer-selection-menu plus the browser's own editing menu)

/// Right-click menu of the rich-text editor: spelling, clipboard, formatting, link, table, image, AI.
fn rte_items(u: &AppWindow) -> Vec<CtxItem> {
    let c = crate::rte::ctx_info();
    let mut v = vec![];
    if let Some((word, sugg)) = &c.spell {
        if sugg.is_empty() { v.push(off(it("", "No suggestions", "", ""), false)); }
        for s in sugg.iter().take(5) { v.push(it("", s, "", &format!("spell:{s}"))); }
        v.push(it(G_ADD, &format!("Add \u{201C}{word}\u{201D} to dictionary"), "", "spell-add"));
        v.push(it("", "Ignore", "", "spell-ignore"));
        v.push(sep());
    }
    v.extend([
        off(it(G_CUT, "Cut", "Ctrl+X", "cut"), c.has_sel),
        off(it(G_COPY, "Copy", "Ctrl+C", "copy"), c.has_sel),
        it(G_PASTE, "Paste", "Ctrl+V", "paste"),
        it(G_PASTE, "Paste as plain text", "Ctrl+Shift+V", "paste-plain"),
        sep(),
        it(G_SELECT_ALL, "Select all", "Ctrl+A", "select-all"),
        sep(),
        sub("\u{E8D2}", "Format", "sub:rte-format"),
        sub("\u{E8E4}", "Paragraph", "sub:rte-para"),
    ]);
    if c.in_table { v.push(sub("\u{E80A}", "Table", "sub:rte-table")); }
    v.push(sep());
    match &c.link {
        Some(_) => v.extend([
            it(G_EDIT, "Edit link\u{2026}", "Ctrl+K", "link"),
            it(G_OPEN, "Open link", "", "link-open"),
            it(G_COPY, "Copy link address", "", "copy-link"),
            it("\u{E711}", "Remove link", "", "link-remove"),
        ]),
        None => v.push(off(it("\u{E71B}", "Insert link\u{2026}", "Ctrl+K", "link"), !c.image)),
    }
    if c.image {
        v.push(sep());
        v.push(it("\u{E9A6}", "Original image size", "", "image-size:0"));
        v.push(it("\u{E9A6}", "Small (240 px)", "", "image-size:240"));
        v.push(it("\u{E9A6}", "Medium (480 px)", "", "image-size:480"));
    }
    if !u.get_ai_hidden() {
        v.push(sep());
        v.push(sub("\u{E945}", if c.has_sel { "Rewrite selection with AI" } else { "Rewrite with AI" }, "sub:rte-ai"));
    }
    v.push(sep());
    v.push(off(it("\u{E7A7}", "Undo", "Ctrl+Z", "undo"), c.can_undo));
    v.push(off(it("\u{E7A6}", "Redo", "Ctrl+Y", "redo"), c.can_redo));
    v
}

fn rte_sub(which: &str, ai_busy: bool) -> Vec<CtxItem> {
    let c = crate::rte::ctx_info();
    let chk = |mut i: CtxItem, on: bool| { i.checked = on; i };
    match which {
        "rte-format" => vec![
            chk(it("", "Bold", "Ctrl+B", "bold"), c.bold),
            chk(it("", "Italic", "Ctrl+I", "italic"), c.italic),
            chk(it("", "Underline", "Ctrl+U", "underline"), c.underline),
            chk(it("", "Strikethrough", "Ctrl+Shift+S", "strike"), c.strike),
            chk(it("\u{E943}", "Inline code", "Ctrl+E", "code-inline"), c.code),
            chk(it("\u{E7E6}", "Highlight", "Ctrl+Shift+H", "highlight"), c.highlight),
            sep(),
            it(G_ERASE, "Clear formatting", "Ctrl+\\", "clear"),
        ],
        "rte-para" => vec![
            chk(it("", "Normal text", "Ctrl+Alt+0", "para"), c.block == "p"),
            chk(it("", "Heading 1", "Ctrl+Alt+1", "h1"), c.block == "h1"),
            chk(it("", "Heading 2", "Ctrl+Alt+2", "h2"), c.block == "h2"),
            chk(it("", "Heading 3", "Ctrl+Alt+3", "h3"), c.block == "h3"),
            sep(),
            chk(it("\u{E8FD}", "Bulleted list", "Ctrl+Shift+8", "bullet"), c.block == "bullet"),
            chk(it("", "Numbered list", "Ctrl+Shift+7", "numbered"), c.block == "ordered"),
            chk(it("", "Quote", "Ctrl+Shift+B", "quote"), c.block == "quote"),
            chk(it("\u{E943}", "Code block", "Ctrl+Alt+C", "code"), c.block == "code"),
            sep(),
            chk(it("\u{E8E4}", "Align left", "Ctrl+Shift+L", "align:left"), c.align == "left"),
            chk(it("\u{E8E3}", "Center", "Ctrl+Shift+E", "align:center"), c.align == "center"),
            chk(it("\u{E8E2}", "Align right", "Ctrl+Shift+R", "align:right"), c.align == "right"),
            chk(it("", "Justify", "Ctrl+Shift+J", "align:justify"), c.align == "justify"),
        ],
        "rte-table" => vec![
            it("", "Add row above", "", "table-op:row-above"),
            it("", "Add row below", "", "table-op:row-below"),
            it("", "Add column left", "", "table-op:col-left"),
            it("", "Add column right", "", "table-op:col-right"),
            sep(),
            it("", "Delete row", "", "table-op:del-row"),
            it("", "Delete column", "", "table-op:del-col"),
            it("", "Toggle header row", "", "table-op:header"),
            sep(),
            danger(G_DELETE, "Delete table", "", "table-op:del-table"),
        ],
        "rte-ai" => ["Improve", "Shorten", "Formal", "Casual", "Fix spelling", "Translate"].iter()
            .map(|a| off(it("", a, "", &format!("ai:{a}")), !ai_busy)).collect(),
        _ => vec![],
    }
}
