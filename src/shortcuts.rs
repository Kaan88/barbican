//! Keyboard shortcuts in every app and the `?` help sheet.
//!
//! Bulwark: lib/keyboard-shortcuts/{mail,calendar,contacts,files}.ts (what the help lists),
//! hooks/use-keyboard-shortcuts.ts (mail keys, the `g` sequences), lib/calendar-shortcuts.ts,
//! components/contacts/contact-list.tsx and components/files/file-browser.tsx (their list keys),
//! components/command-palette/command-palette.tsx (`?` and Ctrl+K in every app),
//! components/keyboard-shortcuts-modal.tsx (the help sheet).
//!
//! `resolve` turns a key press into an [`Action`] without touching the UI (unit tested);
//! `handle` runs it. Keys this does not claim fall through to the older mail handler in main.rs.

use crate::{AppWindow, KeyRow, Pd};
use slint::{ComponentHandle, Model, ModelRc, VecModel};
use crate::ui_types::SharedString;
use std::cell::{Cell, RefCell};
use std::time::{Duration, Instant};

// Slint key texts (i-slint-common key_codes.rs)
pub const UP: &str = "\u{F700}";
pub const DOWN: &str = "\u{F701}";
pub const LEFT: &str = "\u{F702}";
pub const RIGHT: &str = "\u{F703}";
pub const HOME: &str = "\u{F729}";
pub const END: &str = "\u{F72B}";
pub const ESC: &str = "\u{1b}";
pub const DEL: &str = "\u{7f}";
pub const BACKSPACE: &str = "\u{8}";
pub const F2: &str = "\u{F705}";
pub const F5: &str = "\u{F708}";
pub const F9: &str = "\u{F70C}";
pub const F10: &str = "\u{F70D}";

fn is_enter(t: &str) -> bool { t == "\n" || t == "\r" }

/// How long a pending `g` waits for its second key (Bulwark GO_TO_SEQUENCE_MS).
pub const GO_TO_SEQUENCE: Duration = Duration::from_millis(1500);

/// Where the key was pressed.
#[derive(Clone, Copy, Debug, PartialEq)]
pub enum Place { Mail, Calendar, Contacts, Files, Settings, Admin, Composer }

/// What the UI looks like right now, as far as the keys care.
#[derive(Clone, Copy, Debug, Default)]
pub struct Ctx {
    /// A message is open (Barbican's selection).
    pub has_mail: bool,
    /// The calendar's event editor is open.
    pub editor_open: bool,
    /// A contact is selected.
    pub has_contact: bool,
    /// A file row is highlighted.
    pub has_file: bool,
    /// Settings > Accessibility > Single-key shortcuts.
    pub single_keys: bool,
    /// A `g` was pressed less than GO_TO_SEQUENCE ago.
    pub g_pending: bool,
}

#[derive(Clone, Debug, PartialEq)]
pub enum Action {
    /// Not ours: let the older handler have it.
    Pass,
    /// Ours, and nothing to do (e.g. the first half of a `g` sequence).
    Swallow,
    Palette,
    Help,
    A11y,
    Refresh,
    Compose,
    FocusSearch,
    // mail
    GoTo(&'static str),
    ArmGo,
    Open,
    ToggleSelect,
    Extend(i32),
    Mail(&'static str),
    Undo,
    // composer
    ShowCc,
    ShowBcc,
    // calendar
    Cal(&'static str),
    CalView(i32),
    NewEvent,
    EditEvent,
    SaveEvent,
    CloseEditor,
    // contacts and files lists
    Move(i32),
    First,
    Last,
    OpenItem,
    Delete,
    Clear,
    Menu,
    Rename,
    Copy,
    Cut,
    Paste,
    Up,
}

/// A printable key that only counts as a "single-key shortcut" (WCAG 2.1.4), which Settings >
/// Accessibility can turn off. Enter, Escape, Delete, the arrows and F-keys always work.
pub fn is_single_key(t: &str) -> bool {
    let mut it = t.chars();
    matches!((it.next(), it.next()), (Some(c), None) if c.is_ascii_graphic())
}

/// Turn a key press into an action. `t` is Slint's key text, already with shift applied.
pub fn resolve(place: Place, ctx: Ctx, t: &str, shift: bool, ctrl: bool, alt: bool) -> Action {
    use Action::*;
    let lower = t.to_lowercase();
    let l = lower.as_str();
    // ---- composer (Bulwark email-composer.tsx capture handler) ----
    if place == Place::Composer {
        if ctrl && shift && !alt && l == "c" { return ShowCc; }
        if ctrl && shift && !alt && l == "b" { return ShowBcc; }
        return Pass;
    }
    // ---- every app ----
    if ctrl && !shift && !alt && l == "k" { return Palette; }
    if alt && shift && !ctrl && l == "a" { return A11y; }
    if !ctrl && !alt && (t == F5 || t == F9) { return Refresh; }
    if ctrl && shift && !alt && l == "m" { return Compose; }
    let single = is_single_key(t) && !ctrl && !alt;
    if single && !ctx.single_keys { return Pass; }
    if single && t == "?" && !(place == Place::Calendar && ctx.editor_open) { return Help; }
    match place {
        Place::Mail => mail(ctx, t, l, shift, ctrl, alt),
        Place::Calendar => calendar(ctx, t, l, shift, ctrl, alt),
        Place::Contacts => contacts(ctx, t, l, shift, ctrl, alt),
        Place::Files => files(ctx, t, l, shift, ctrl, alt),
        _ => Pass,
    }
}

fn mail(ctx: Ctx, t: &str, l: &str, shift: bool, ctrl: bool, alt: bool) -> Action {
    use Action::*;
    if alt { return Pass; }
    if ctrl {
        return match (l, shift) {
            // Bulwark desktopPcKey: Ctrl+F forwards with a message selected (Outlook) and
            // searches elsewhere; Ctrl+Shift+F always forwards
            ("f", false) => if ctx.has_mail { Mail("forward") } else { FocusSearch },
            ("f", true) => if ctx.has_mail { Mail("forward") } else { Swallow },
            ("e", false) => FocusSearch,
            // Windows desktop key for Mark as read
            ("q", false) => Mail("read!"),
            ("z", false) => Undo,
            _ => Pass,
        };
    }
    // second half of a `g` sequence, before the single keys so s / t / a / d jump
    if ctx.g_pending {
        let target = match l { "i" => Some("inbox"), "s" => Some("starred"), "t" => Some("sent"), "d" => Some("drafts"), "a" => Some("all"), _ => None };
        if let Some(target) = target.filter(|_| !shift) { return GoTo(target); }
    }
    match t {
        _ if shift && (t == DOWN || t == "J") => Extend(1),
        _ if shift && (t == UP || t == "K") => Extend(-1),
        _ if is_enter(t) || t == "o" => Open,
        "g" => ArmGo,
        "x" => if ctx.has_mail { ToggleSelect } else { Pass },
        // Bulwark's unadvertised alias of Shift+R
        "a" => if ctx.has_mail { Mail("reply-all") } else { Pass },
        _ => Pass,
    }
}

/// Single-key mail actions on an open message (the palette shows these as hints).
pub fn mail_key_action(t: &str) -> Option<&'static str> {
    Some(match t {
        "r" => "reply", "R" | "a" => "reply-all", "f" => "forward", "s" => "star", "e" => "archive",
        "#" | "\u{7f}" => "delete", "u" => "unread!", "I" => "read!", "!" => "spam",
        _ => return None,
    })
}

fn calendar(ctx: Ctx, t: &str, l: &str, shift: bool, ctrl: bool, alt: bool) -> Action {
    use Action::*;
    if ctx.editor_open {
        // the event editor (event-modal.tsx): Mod+Enter saves, Esc closes
        if ctrl && is_enter(t) { return SaveEvent; }
        if t == ESC { return CloseEditor; }
        return Pass;
    }
    if ctrl && !shift && !alt && l == "r" { return Refresh; }
    if ctrl || alt { return Pass; }
    match t {
        _ if t == LEFT => Cal("prev"),
        _ if t == RIGHT => Cal("next"),
        "t" => Cal("today"),
        "d" => CalView(0),
        "w" => CalView(1),
        "m" => CalView(2),
        "n" | "c" => NewEvent,
        "e" => EditEvent,
        "/" => FocusSearch,
        _ => { let _ = shift; Pass }
    }
}

fn contacts(ctx: Ctx, t: &str, l: &str, shift: bool, ctrl: bool, alt: bool) -> Action {
    use Action::*;
    let _ = l;
    if ctrl || alt { return Pass; }
    match t {
        _ if t == DOWN => Move(1),
        _ if t == UP => Move(-1),
        _ if t == HOME => First,
        _ if t == END => Last,
        _ if is_enter(t) => if ctx.has_contact { OpenItem } else { Pass },
        _ if t == DEL || t == BACKSPACE => if ctx.has_contact { Delete } else { Pass },
        _ if t == ESC => Clear,
        _ if t == F10 && shift => if ctx.has_contact { Menu } else { Pass },
        "/" => FocusSearch,
        _ => Pass,
    }
}

fn files(ctx: Ctx, t: &str, l: &str, shift: bool, ctrl: bool, alt: bool) -> Action {
    use Action::*;
    if alt && !ctrl && t == UP { return Up; }
    if alt { return Pass; }
    if ctrl && !shift {
        return match l {
            "c" => if ctx.has_file { Copy } else { Pass },
            "x" => if ctx.has_file { Cut } else { Pass },
            "v" => Paste,
            _ => Pass,
        };
    }
    if ctrl { return Pass; }
    match t {
        _ if t == DOWN => Move(1),
        _ if t == UP => Move(-1),
        _ if t == HOME => First,
        _ if t == END => Last,
        _ if is_enter(t) => if ctx.has_file { OpenItem } else { Pass },
        _ if t == F2 => if ctx.has_file { Rename } else { Pass },
        _ if t == DEL || t == BACKSPACE => if ctx.has_file { Delete } else { Pass },
        _ if t == ESC => Clear,
        _ if t == F10 && shift => if ctx.has_file { Menu } else { Pass },
        _ => Pass,
    }
}

// ---------------- the help sheet (keyboard-shortcuts-modal.tsx) ----------------

/// One help row: keys in Bulwark's notation ("Ctrl + R / r", alternatives split by " / ").
pub struct Entry(pub &'static str, pub &'static str);
pub struct Section(pub &'static str, pub &'static [Entry]);

/// The keys Barbican handles, per app, in Bulwark's sections and words. The Windows desktop
/// key comes first, then the page key (lib/keyboard-shortcuts shortcutKeyFor, host "windows").
/// Rows Barbican has no feature for (schedule send, agenda, task list, proofreading) are left out.
pub const MAIL_HELP: &[Section] = &[
    Section("Navigation", &[
        Entry("j / ↓", "Next email"),
        Entry("k / ↑", "Previous email"),
        Entry("Enter / o", "Open email"),
        Entry("Esc", "Close / Deselect"),
        Entry("n", "Next unread email"),
        Entry("g → i / s / t / d / a", "Go to folder"),
    ]),
    Section("Email Actions", &[
        Entry("Ctrl + R / r", "Reply"),
        Entry("Ctrl + Shift + R / Shift + R", "Reply all"),
        Entry("Ctrl + F / Ctrl + Shift + F / f", "Forward"),
        Entry("s", "Toggle star"),
        Entry("e", "Archive"),
        Entry("Del", "Delete"),
        Entry("Ctrl + U / u", "Mark as unread"),
        Entry("Ctrl + Q / Shift + I", "Mark as read"),
        Entry("!", "Report spam / Not spam"),
        Entry("Ctrl + Z / z", "Undo the last action"),
    ]),
    Section("Global", &[
        Entry("Ctrl + N / Ctrl + Shift + M / c", "Compose new email"),
        Entry("Ctrl + E / /", "Focus search"),
        Entry("F9 / F5 / Shift + G", "Refresh emails"),
        Entry("Ctrl + 1 / Ctrl + 2 / Ctrl + 3 / Ctrl + 4", "Go to Mail, Calendar, Contacts or Files"),
        Entry("Ctrl + A", "Select all"),
        Entry("Shift + ↓ / Shift + ↑", "Extend selection down / up"),
    ]),
    Section("Threads", &[
        Entry("x", "Select the conversation"),
    ]),
    Section("Composer", &[
        Entry("Ctrl + Enter", "Send email"),
        Entry("Ctrl + S", "Save draft"),
        Entry("Ctrl + K", "Insert link"),
        Entry("Ctrl + Shift + C", "Show Cc"),
        Entry("Ctrl + Shift + B", "Show Bcc"),
    ]),
];

pub const CALENDAR_HELP: &[Section] = &[
    Section("Navigation", &[
        Entry("←", "Previous day, week or month"),
        Entry("→", "Next day, week or month"),
        Entry("t", "Go to today"),
    ]),
    Section("Views", &[
        Entry("d", "Day view"),
        Entry("w", "Week view"),
        Entry("m", "Month view"),
    ]),
    Section("Actions", &[
        Entry("/", "Search events"),
        Entry("n / c", "New event"),
        Entry("e", "Edit event"),
        Entry("Ctrl + Enter", "Save"),
        Entry("Esc", "Close"),
    ]),
    Section("Global", &[
        Entry("F5 / Ctrl + R", "Refresh"),
    ]),
];

pub const CONTACTS_HELP: &[Section] = &[
    Section("List", &[
        Entry("↓ / ↑", "Next or previous contact"),
        Entry("Home / End", "First or last"),
        Entry("Enter", "Open the contact"),
        Entry("Esc", "Clear the selection or search"),
    ]),
    Section("Actions", &[
        Entry("Del / Backspace", "Delete"),
        Entry("Shift + F10", "Open the contact's menu"),
    ]),
    Section("Global", &[
        Entry("/", "Search"),
    ]),
];

pub const FILES_HELP: &[Section] = &[
    Section("List", &[
        Entry("↓ / ↑", "Next or previous item"),
        Entry("Home / End", "First or last"),
        Entry("Enter", "Open"),
        Entry("Esc", "Clear the selection or search"),
        Entry("Alt + ↑", "Up a folder"),
    ]),
    Section("Actions", &[
        Entry("F2", "Rename"),
        Entry("Del / Backspace", "Delete"),
        Entry("Ctrl + C", "Copy"),
        Entry("Ctrl + X", "Cut"),
        Entry("Ctrl + V", "Paste"),
        Entry("Shift + F10", "Open the item's menu"),
    ]),
];

/// Keys every app shares (Bulwark GENERAL_SHORTCUTS), added to each app's "Global" section.
pub const GENERAL_HELP: &[Entry] = &[
    Entry("Ctrl + K", "Open the command palette"),
    Entry("?", "Show shortcuts"),
    Entry("Alt + Shift + A", "Open the accessibility options"),
];

pub const TABS: [&str; 4] = ["Mail", "Calendar", "Contacts", "Files"];

fn sections(tab: usize) -> &'static [Section] {
    match tab { 1 => CALENDAR_HELP, 2 => CONTACTS_HELP, 3 => FILES_HELP, _ => MAIL_HELP }
}

/// The keys of a row: alternatives ("j / ↓") split apart. A lone "/" is a key, not a separator.
pub fn alternatives(keys: &str) -> Vec<String> {
    if keys.trim() == "/" { return vec!["/".into()]; }
    let mut out: Vec<String> = vec![];
    for part in keys.split(" / ") {
        let p = part.trim();
        if p.is_empty() { continue; }
        out.push(p.to_string());
    }
    out
}

/// Flattened rows of one tab: a header row per section, then its entries; the general keys
/// go into the "Global" section (or one of their own), and single keys are marked so the sheet
/// can grey them out while single-key shortcuts are off.
pub fn help_rows(tab: usize) -> Vec<(String, String, Vec<String>)> {
    let mut out = vec![];
    let secs = sections(tab);
    let mut had_global = false;
    for s in secs {
        out.push((s.0.to_string(), String::new(), vec![]));
        for e in s.1 { out.push((String::new(), e.1.to_string(), alternatives(e.0))); }
        if s.0 == "Global" {
            had_global = true;
            for e in GENERAL_HELP { out.push((String::new(), e.1.to_string(), alternatives(e.0))); }
        }
    }
    if !had_global {
        out.push(("Global".to_string(), String::new(), vec![]));
        for e in GENERAL_HELP { out.push((String::new(), e.1.to_string(), alternatives(e.0))); }
    }
    out
}

pub fn show_help(u: &AppWindow, tab: usize) {
    let single = crate::a11y::A11y::load().single_keys;
    let rows: Vec<KeyRow> = help_rows(tab).into_iter().map(|(header, desc, keys)| {
        let off = !single && !keys.is_empty() && keys.iter().all(|k| is_single_key(k));
        KeyRow {
            header: header.into(),
            desc: desc.into(),
            keys: ModelRc::new(VecModel::from(keys.into_iter().map(SharedString::from).collect::<Vec<_>>())),
            off,
        }
    }).collect();
    let pd = u.global::<Pd>();
    pd.set_keys_rows(ModelRc::new(VecModel::from(rows)));
    pd.set_keys_tab(tab as i32);
    pd.set_keys_tabs(ModelRc::new(VecModel::from(TABS.iter().map(|t| SharedString::from(*t)).collect::<Vec<_>>())));
    pd.set_keys_single_off(!single);
    pd.set_keys_open(true);
}

// ---------------- running the actions ----------------

thread_local! {
    static G_AT: Cell<Option<Instant>> = const { Cell::new(None) };
    /// The message last opened, for Enter / o after Esc closed it (the list keeps its place).
    static LAST_MAIL: RefCell<String> = const { RefCell::new(String::new()) };
    static DELETE_ARMED: RefCell<String> = const { RefCell::new(String::new()) };
}

pub fn note_opened(id: &str) {
    if !id.is_empty() { LAST_MAIL.with(|l| *l.borrow_mut() = id.to_string()); }
}

fn place(u: &AppWindow) -> Place {
    if u.get_composing() { return Place::Composer; }
    if u.get_show_settings() { return Place::Settings; }
    match u.get_app() { 1 => Place::Calendar, 2 => Place::Contacts, 3 => Place::Files, 4 => Place::Admin, _ => Place::Mail }
}

/// The key hook (kbd FocusScope, before the older handler). True when the key was used.
pub fn handle(u: &AppWindow, st: &crate::Shared, text: &str, shift: bool, ctrl: bool, alt: bool) -> bool {
    let pd = u.global::<Pd>();
    // an open overlay of ours owns the keyboard
    if pd.get_keys_open() {
        if text == ESC || (text == "?" && !ctrl) { pd.set_keys_open(false); u.invoke_focus_keys(); return true; }
        if text == LEFT || text == RIGHT {
            let n = TABS.len() as i32;
            let t = (pd.get_keys_tab() + if text == LEFT { n - 1 } else { 1 }) % n;
            show_help(u, t as usize);
            return true;
        }
        return true;
    }
    if pd.get_palette_open() { return false; }
    // the mail search box: its arrows walk the suggestions, Esc closes them
    if pd.get_search_focus() && !ctrl && !alt && u.get_app() == 0 {
        if text == DOWN || text == UP {
            if pd.get_sugg_hidden() && text == DOWN && pd.get_sugg_rows().row_count() > 0 {
                pd.set_sugg_hidden(false);
                pd.set_sugg_active(0);
                return true;
            }
            if crate::search_ui::move_active(u, if text == DOWN { 1 } else { -1 }) { return true; }
        }
        if text == ESC && !pd.get_sugg_hidden() && pd.get_sugg_rows().row_count() > 0 { pd.set_sugg_hidden(true); return true; }
    }
    let place = place(u);
    let g_pending = G_AT.with(|g| g.get().is_some_and(|at| at.elapsed() < GO_TO_SEQUENCE));
    let ctx = Ctx {
        has_mail: u.get_current_mail() != "",
        editor_open: u.get_ev_open(),
        has_contact: u.get_ct_id() != "",
        has_file: pd.get_file_sel() != "",
        single_keys: crate::a11y::A11y::load().single_keys,
        g_pending,
    };
    let action = resolve(place, ctx, text, shift, ctrl, alt);
    // any key but the first `g` ends a pending sequence
    G_AT.with(|g| g.set(if action == Action::ArmGo { Some(Instant::now()) } else { None }));
    run(u, st, place, action)
}

fn client(st: &crate::Shared) -> Option<std::sync::Arc<crate::jmap::Client>> { st.lock().unwrap().clone() }

fn run(u: &AppWindow, st: &crate::Shared, place: Place, action: Action) -> bool {
    use Action::*;
    match action {
        Pass => return false,
        Swallow | ArmGo => {}
        Palette => crate::palette::open(u, "commands"),
        Help => {
            let tab = match place { Place::Calendar => 1, Place::Contacts => 2, Place::Files => 3, _ => 0 };
            show_help(u, tab);
        }
        A11y => { u.set_settings_tab("accessibility".into()); u.invoke_open_settings(); }
        Refresh => match place {
            Place::Calendar => { if let Some(c) = client(st) { crate::calendar::load(u.as_weak(), c); } }
            Place::Contacts => { if let Some(c) = client(st) { crate::contacts::load(u.as_weak(), c); } }
            Place::Files => { if let Some(c) = client(st) { crate::files::reload(u, c); } }
            Place::Mail => u.invoke_refresh(),
            _ => return false,
        },
        Compose => u.invoke_compose_new(),
        FocusSearch => match place {
            Place::Calendar => u.global::<Pd>().set_focus_req(u.global::<Pd>().get_focus_req() + 1),
            Place::Contacts => u.global::<Pd>().set_focus_req(u.global::<Pd>().get_focus_req() + 1),
            _ => u.invoke_focus_search(),
        },
        GoTo(target) => go_to(u, st, target),
        Open => {
            let cur = u.get_current_mail().to_string();
            let id = if !cur.is_empty() { cur } else {
                let last = LAST_MAIL.with(|l| l.borrow().clone());
                let m = u.get_mails();
                let listed = |id: &str| (0..m.row_count()).any(|i| m.row_data(i).is_some_and(|r| r.id == id));
                if !last.is_empty() && listed(&last) { last } else { m.row_data(0).map(|r| r.id.to_string()).unwrap_or_default() }
            };
            if id.is_empty() { return true; }
            u.set_current_mail(id.clone().into());
            u.invoke_open_mail(id.into());
        }
        ToggleSelect => u.invoke_toggle_check(u.get_current_mail()),
        Extend(d) => extend(u, d),
        Mail(a) => u.invoke_act(a.into()),
        Undo => u.invoke_undo_toast(),
        ShowCc => { u.set_c_show_cc(true); }
        ShowBcc => { u.set_c_show_bcc(true); }
        Cal(how) => u.invoke_cal_nav(how.into()),
        CalView(v) => u.invoke_cal_set_view(v),
        NewEvent => u.invoke_cal_edit("".into(), "".into(), -1),
        EditEvent => {
            let id = u.global::<Pd>().get_hover_event().to_string();
            if id.is_empty() { return false; }
            u.invoke_cal_edit(id.into(), "".into(), -1);
        }
        SaveEvent => u.invoke_ev_save(),
        CloseEditor => { u.set_ev_open(false); u.invoke_focus_keys(); }
        Move(_) | First | Last | OpenItem | Delete | Clear | Menu | Rename | Copy | Cut | Paste | Up => {
            return match place {
                Place::Contacts => contacts_key(u, st, action),
                Place::Files => files_key(u, st, action),
                _ => false,
            };
        }
    }
    true
}

/// `g` then i / s / t / d / a: Inbox, Starred, Sent, Drafts, All mail (Bulwark onGoToMailbox).
fn go_to(u: &AppWindow, st: &crate::Shared, target: &str) {
    let Some(c) = client(st) else { return };
    u.set_show_settings(false);
    if u.get_app() != 0 { u.set_app(0); u.invoke_app_changed(0); }
    u.set_current_mail("".into());
    u.set_current_tag("".into());
    u.set_search("".into());
    match target {
        "starred" => {
            u.set_current_folder("".into());
            crate::mail::load_list(u.as_weak(), c, serde_json::json!({"hasKeyword": "$flagged"}));
            crate::mail::show_toast(u, "Starred", false);
        }
        "all" => {
            u.set_current_folder("".into());
            // every folder but Trash and Spam, like Bulwark's All mail
            let skip: Vec<String> = ["trash", "junk"].iter().filter_map(|r| crate::mail::role_id(r)).collect();
            let f = if skip.is_empty() { serde_json::json!({}) } else { serde_json::json!({"inMailboxOtherThan": skip}) };
            crate::mail::load_list(u.as_weak(), c, f);
            crate::mail::show_toast(u, "All mail", false);
        }
        role => {
            if let Some(id) = crate::mail::role_id(role) {
                u.set_current_folder(id.clone().into());
                u.invoke_select_folder(id.into());
            }
        }
    }
}

/// Shift+↓ / Shift+↑: take the open row and the next one into the selection and move there.
fn extend(u: &AppWindow, d: i32) {
    let m = u.get_mails();
    let n = m.row_count() as i32;
    if n == 0 { return; }
    let cur = u.get_current_mail();
    let Some(i) = (0..n).find(|&i| m.row_data(i as usize).is_some_and(|r| r.id == cur)) else {
        if let Some(r) = m.row_data(0) { u.set_current_mail(r.id.clone()); u.invoke_open_mail(r.id); }
        return;
    };
    let j = i + d;
    if j < 0 || j >= n { return; }
    let (a, b) = (m.row_data(i as usize).unwrap(), m.row_data(j as usize).unwrap());
    if !a.checked { u.invoke_toggle_check(a.id.clone()); }
    if !b.checked { u.invoke_toggle_check(b.id.clone()); }
    u.set_current_mail(b.id.clone());
    u.invoke_open_mail(b.id);
}

/// Index of `id` in a list of ids, moved by an action (Move / First / Last), clamped.
pub fn step(ids: &[String], cur: &str, action: &Action) -> Option<usize> {
    if ids.is_empty() { return None; }
    let last = ids.len() - 1;
    let at = ids.iter().position(|x| x == cur);
    Some(match action {
        Action::First => 0,
        Action::Last => last,
        Action::Move(d) => match at {
            None => if *d > 0 { 0 } else { last },
            Some(i) => (i as i64 + *d as i64).clamp(0, last as i64) as usize,
        },
        _ => return at,
    })
}

fn contacts_key(u: &AppWindow, st: &crate::Shared, action: Action) -> bool {
    let rows = u.get_contacts();
    let ids: Vec<String> = (0..rows.row_count()).filter_map(|i| rows.row_data(i)).map(|r| r.id.to_string()).collect();
    let cur = u.get_ct_id().to_string();
    match action {
        Action::Move(_) | Action::First | Action::Last => {
            if let Some(i) = step(&ids, &cur, &action) { u.invoke_ct_select(ids[i].clone().into()); }
        }
        Action::OpenItem => { u.set_ct_editing(false); u.invoke_ct_select(cur.into()); }
        Action::Delete => {
            // a second press confirms (Bulwark asks before deleting a contact)
            let armed = DELETE_ARMED.with(|d| std::mem::take(&mut *d.borrow_mut()));
            if armed == format!("contact:{cur}") {
                if let Some(c) = client(st) { crate::contacts::delete(u, c); }
            } else {
                DELETE_ARMED.with(|d| *d.borrow_mut() = format!("contact:{cur}"));
                crate::mail::show_toast(u, &format!("Press Delete again to delete {}", u.get_ct_name()), false);
            }
        }
        Action::Clear => {
            u.set_ct_editing(false);
            u.set_ct_id("".into());
            u.set_ct_group("".into());
            u.global::<Pd>().set_clear_req(u.global::<Pd>().get_clear_req() + 1);
            u.invoke_ct_search("".into());
        }
        Action::Menu => {
            let (w, h) = (u.window().size().width as f32 / u.window().scale_factor(), u.window().size().height as f32 / u.window().scale_factor());
            u.invoke_ctx("contact".into(), cur.into(), w / 2.0 - 120.0, h / 3.0);
        }
        _ => return false,
    }
    true
}

fn files_key(u: &AppWindow, st: &crate::Shared, action: Action) -> bool {
    let pd = u.global::<Pd>();
    let rows = u.get_files();
    let ids: Vec<String> = (0..rows.row_count()).filter_map(|i| rows.row_data(i)).map(|r| r.id.to_string()).collect();
    let cur = pd.get_file_sel().to_string();
    let Some(c) = client(st) else { return false };
    match action {
        Action::Move(_) | Action::First | Action::Last => {
            if let Some(i) = step(&ids, &cur, &action) { pd.set_file_sel(ids[i].clone().into()); }
        }
        Action::OpenItem => { pd.set_file_sel("".into()); crate::files::activate(u, c, &cur); }
        Action::Rename => crate::files::start_rename(u, &cur),
        Action::Delete => {
            // the permanent cases (shared drive, already in the trash) ask with a second press
            if crate::files::deletes_permanently(&cur) {
                let armed = DELETE_ARMED.with(|d| std::mem::take(&mut *d.borrow_mut()));
                if armed != format!("file:{cur}") {
                    DELETE_ARMED.with(|d| *d.borrow_mut() = format!("file:{cur}"));
                    crate::mail::show_toast(u, "This is deleted for good. Press Delete again to confirm.", false);
                    return true;
                }
            }
            pd.set_file_sel("".into());
            crate::files::delete(u, c, &cur);
        }
        Action::Copy => crate::files::set_clipboard(u, &cur, false),
        Action::Cut => crate::files::set_clipboard(u, &cur, true),
        Action::Paste => crate::files::paste(u, c),
        Action::Up => {
            let cwd = u.get_fl_cwd().to_string();
            if cwd.is_empty() { return true; }
            let parent = crate::files::parent_of(&cwd).unwrap_or_default();
            pd.set_file_sel(cwd.into());
            crate::files::open(u, c, &parent);
        }
        Action::Clear => pd.set_file_sel("".into()),
        Action::Menu => {
            let (w, h) = (u.window().size().width as f32 / u.window().scale_factor(), u.window().size().height as f32 / u.window().scale_factor());
            u.invoke_ctx("file".into(), cur.into(), w / 2.0 - 120.0, h / 3.0);
        }
        _ => return false,
    }
    true
}

#[cfg(test)]
mod tests {
    use super::*;

    fn ctx() -> Ctx { Ctx { single_keys: true, has_mail: true, ..Default::default() } }

    #[test]
    fn mail_keys_never_flip_and_backspace_does_not_delete() {
        assert_eq!(resolve(Place::Mail, ctx(), BACKSPACE, false, false, false), Action::Pass);
        assert_eq!(mail_key_action("I"), Some("read!"));
        assert_eq!(mail_key_action("u"), Some("unread!"));
        // every single-key hint the palette shows for mail resolves to that command
        for (k, a) in [("r", "reply"), ("a", "reply-all"), ("f", "forward"), ("e", "archive"), ("#", "delete"), ("s", "star"), ("!", "spam")] {
            let via_table = match resolve(Place::Mail, ctx(), k, false, false, false) { Action::Mail(x) => Some(x), _ => mail_key_action(k) };
            assert_eq!(via_table, Some(a), "{k}");
        }
    }

    #[test]
    fn palette_and_help_everywhere() {
        for p in [Place::Mail, Place::Calendar, Place::Contacts, Place::Files, Place::Settings] {
            assert_eq!(resolve(p, ctx(), "k", false, true, false), Action::Palette);
            assert_eq!(resolve(p, ctx(), "?", true, false, false), Action::Help);
            assert_eq!(resolve(p, ctx(), F5, false, false, false), Action::Refresh);
            assert_eq!(resolve(p, ctx(), "A", true, false, true), Action::A11y);
        }
        // the composer keeps Ctrl+K for links and types "?"
        assert_eq!(resolve(Place::Composer, ctx(), "k", false, true, false), Action::Pass);
        assert_eq!(resolve(Place::Composer, ctx(), "?", true, false, false), Action::Pass);
    }

    #[test]
    fn single_keys_off_leaves_named_keys() {
        let c = Ctx { single_keys: false, ..ctx() };
        assert_eq!(resolve(Place::Mail, c, "?", true, false, false), Action::Pass);
        assert_eq!(resolve(Place::Mail, c, "x", false, false, false), Action::Pass);
        assert_eq!(resolve(Place::Mail, c, "\n", false, false, false), Action::Open);
        assert_eq!(resolve(Place::Mail, c, "k", false, true, false), Action::Palette);
        assert_eq!(resolve(Place::Calendar, c, LEFT, false, false, false), Action::Cal("prev"));
    }

    #[test]
    fn g_sequences() {
        assert_eq!(resolve(Place::Mail, ctx(), "g", false, false, false), Action::ArmGo);
        let armed = Ctx { g_pending: true, ..ctx() };
        assert_eq!(resolve(Place::Mail, armed, "i", false, false, false), Action::GoTo("inbox"));
        assert_eq!(resolve(Place::Mail, armed, "s", false, false, false), Action::GoTo("starred"));
        assert_eq!(resolve(Place::Mail, armed, "t", false, false, false), Action::GoTo("sent"));
        assert_eq!(resolve(Place::Mail, armed, "d", false, false, false), Action::GoTo("drafts"));
        assert_eq!(resolve(Place::Mail, armed, "a", false, false, false), Action::GoTo("all"));
        // without the `g`, s and a are star and reply-all again
        assert_eq!(resolve(Place::Mail, ctx(), "s", false, false, false), Action::Pass);
        assert_eq!(resolve(Place::Mail, ctx(), "a", false, false, false), Action::Mail("reply-all"));
        // Shift+G stays refresh (older handler)
        assert_eq!(resolve(Place::Mail, armed, "G", true, false, false), Action::Pass);
    }

    #[test]
    fn mail_keys() {
        assert_eq!(resolve(Place::Mail, ctx(), DOWN, true, false, false), Action::Extend(1));
        assert_eq!(resolve(Place::Mail, ctx(), "K", true, false, false), Action::Extend(-1));
        assert_eq!(resolve(Place::Mail, ctx(), DOWN, false, false, false), Action::Pass);
        assert_eq!(resolve(Place::Mail, ctx(), "o", false, false, false), Action::Open);
        assert_eq!(resolve(Place::Mail, ctx(), "x", false, false, false), Action::ToggleSelect);
        assert_eq!(resolve(Place::Mail, ctx(), "f", false, true, false), Action::Mail("forward"));
        let none = Ctx { has_mail: false, ..ctx() };
        assert_eq!(resolve(Place::Mail, none, "f", false, true, false), Action::FocusSearch);
        assert_eq!(resolve(Place::Mail, none, "x", false, false, false), Action::Pass);
        assert_eq!(resolve(Place::Mail, ctx(), "q", false, true, false), Action::Mail("read!"));
        assert_eq!(resolve(Place::Mail, ctx(), "z", false, true, false), Action::Undo);
        assert_eq!(resolve(Place::Mail, ctx(), F9, false, false, false), Action::Refresh);
        assert_eq!(resolve(Place::Mail, ctx(), "M", true, true, false), Action::Compose);
    }

    #[test]
    fn composer_keys() {
        assert_eq!(resolve(Place::Composer, ctx(), "C", true, true, false), Action::ShowCc);
        assert_eq!(resolve(Place::Composer, ctx(), "B", true, true, false), Action::ShowBcc);
        assert_eq!(resolve(Place::Composer, ctx(), "b", false, true, false), Action::Pass);
    }

    #[test]
    fn calendar_keys() {
        let c = ctx();
        assert_eq!(resolve(Place::Calendar, c, RIGHT, false, false, false), Action::Cal("next"));
        assert_eq!(resolve(Place::Calendar, c, "t", false, false, false), Action::Cal("today"));
        assert_eq!(resolve(Place::Calendar, c, "w", false, false, false), Action::CalView(1));
        assert_eq!(resolve(Place::Calendar, c, "n", false, false, false), Action::NewEvent);
        assert_eq!(resolve(Place::Calendar, c, "c", false, false, false), Action::NewEvent);
        assert_eq!(resolve(Place::Calendar, c, "/", false, false, false), Action::FocusSearch);
        assert_eq!(resolve(Place::Calendar, c, "r", false, true, false), Action::Refresh);
        let open = Ctx { editor_open: true, ..c };
        assert_eq!(resolve(Place::Calendar, open, "\n", false, true, false), Action::SaveEvent);
        assert_eq!(resolve(Place::Calendar, open, ESC, false, false, false), Action::CloseEditor);
        assert_eq!(resolve(Place::Calendar, open, "t", false, false, false), Action::Pass);
        assert_eq!(resolve(Place::Calendar, open, "?", true, false, false), Action::Pass);
    }

    #[test]
    fn list_keys() {
        let c = Ctx { has_contact: true, has_file: true, ..ctx() };
        assert_eq!(resolve(Place::Contacts, c, DOWN, false, false, false), Action::Move(1));
        assert_eq!(resolve(Place::Contacts, c, HOME, false, false, false), Action::First);
        assert_eq!(resolve(Place::Contacts, c, DEL, false, false, false), Action::Delete);
        assert_eq!(resolve(Place::Contacts, c, F10, true, false, false), Action::Menu);
        assert_eq!(resolve(Place::Files, c, UP, false, false, true), Action::Up);
        assert_eq!(resolve(Place::Files, c, F2, false, false, false), Action::Rename);
        assert_eq!(resolve(Place::Files, c, "x", false, true, false), Action::Cut);
        assert_eq!(resolve(Place::Files, c, "v", false, true, false), Action::Paste);
        let none = Ctx { has_file: false, has_contact: false, ..ctx() };
        assert_eq!(resolve(Place::Files, none, DEL, false, false, false), Action::Pass);
        assert_eq!(resolve(Place::Contacts, none, "\n", false, false, false), Action::Pass);
    }

    #[test]
    fn stepping_through_a_list() {
        let ids: Vec<String> = ["a", "b", "c"].iter().map(|s| s.to_string()).collect();
        assert_eq!(step(&ids, "", &Action::Move(1)), Some(0));
        assert_eq!(step(&ids, "", &Action::Move(-1)), Some(2));
        assert_eq!(step(&ids, "b", &Action::Move(1)), Some(2));
        assert_eq!(step(&ids, "c", &Action::Move(1)), Some(2));
        assert_eq!(step(&ids, "a", &Action::Move(-1)), Some(0));
        assert_eq!(step(&ids, "b", &Action::Last), Some(2));
        assert_eq!(step(&[], "b", &Action::Last), None);
    }

    #[test]
    fn help_rows_split_keys() {
        assert_eq!(alternatives("j / ↓"), vec!["j", "↓"]);
        assert_eq!(alternatives("/"), vec!["/"]);
        assert_eq!(alternatives("Ctrl + E / /"), vec!["Ctrl + E", "/"]);
        assert_eq!(alternatives("Ctrl + 1 / Ctrl + 2"), vec!["Ctrl + 1", "Ctrl + 2"]);
        let rows = help_rows(0);
        assert_eq!(rows[0].0, "Navigation");
        assert!(rows.iter().any(|r| r.1 == "Show shortcuts" && r.2 == vec!["?"]));
        // apps without a Global section get one with the general keys
        let files = help_rows(3);
        assert!(files.iter().any(|r| r.0 == "Global"));
        assert!(files.iter().any(|r| r.1 == "Open the command palette"));
        // general keys are listed once per tab
        assert_eq!(help_rows(1).iter().filter(|r| r.1 == "Show shortcuts").count(), 1);
    }

    #[test]
    fn single_key_detection() {
        assert!(is_single_key("x"));
        assert!(is_single_key("?"));
        assert!(!is_single_key("\n"));
        assert!(!is_single_key(DOWN));
        assert!(!is_single_key("Ctrl + K"));
    }
}
