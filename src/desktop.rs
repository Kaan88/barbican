//! The desktop app's OS integration (Bulwark components/desktop/desktop-os-host.tsx,
//! lib/desktop/os-summary.ts, settings desktopOs / desktop_updates): tray icon with the
//! unread badge and Open / Compose / Check mail / Quit, close to tray, start in the tray at
//! sign-in, start at login, the taskbar's unread overlay, what notification clicks and
//! buttons do, and the updater's prompt.
use crate::{notify, platform, settings, updater, AppWindow, Shared};
use slint::{ComponentHandle, Model};
use std::cell::{Cell, RefCell};

thread_local! {
    static UI: RefCell<Option<slint::Weak<AppWindow>>> = const { RefCell::new(None) };
    static STATE: RefCell<Option<Shared>> = const { RefCell::new(None) };
    static TRAY: RefCell<Option<tray::Tray>> = const { RefCell::new(None) };
    static UNREAD: Cell<i64> = const { Cell::new(-1) };
    static UPDATE_REV: Cell<u64> = const { Cell::new(u64::MAX) };
    /// Versions whose prompt the user closed this session.
    static DISMISSED: RefCell<Vec<String>> = const { RefCell::new(Vec::new()) };
    static TIMERS: RefCell<Vec<slint::Timer>> = const { RefCell::new(Vec::new()) };
}

/// Calendar hook: what a reminder notification's click or button does (the calendar sets it).
pub type EventHandler = fn(&AppWindow, &str, &str);
thread_local! { static EVENT_HANDLER: Cell<Option<EventHandler>> = const { Cell::new(None) }; }

/// For the calendar: `f(ui, action, event_id)` runs for "open" and "snooze-N" on a reminder.
#[allow(dead_code)]
pub fn set_event_handler(f: EventHandler) { EVENT_HANDLER.with(|h| h.set(Some(f))); }

/// Settings (prefs.json keys, all per computer like Bulwark's DesktopOsSettings).
pub struct Prefs {
    pub close_to_tray: bool,
    pub start_at_login: bool,
    pub start_minimized: bool,
}

impl Prefs {
    pub fn load() -> Prefs {
        let p = settings::Settings::load();
        Prefs {
            close_to_tray: p.bool("desktop.closeToTray", false),
            start_at_login: p.bool("desktop.startAtLogin", false),
            start_minimized: p.bool("desktop.startMinimized", false),
        }
    }
}

/// Bulwark "Start in the tray": only when started at sign-in, and only with a tray to come back from.
pub fn start_hidden_for(args: &[String], p: &Prefs, tray: bool) -> bool {
    tray && p.start_at_login && p.start_minimized && args.iter().any(|a| a == platform::AUTOSTART_ARG)
}

/// Whether main() should leave the window closed at start.
pub fn start_hidden() -> bool {
    let args: Vec<String> = std::env::args().collect();
    start_hidden_for(&args, &Prefs::load(), tray_supported())
}

fn tray_supported() -> bool { !crate::automation::enabled() }

fn app() -> Option<AppWindow> { UI.with(|u| u.borrow().as_ref().and_then(|w| w.upgrade())) }

/// Bring the window back (from the tray, a notification or a second start).
pub fn show_window(u: &AppWindow) {
    let _ = u.show();
    use slint::winit_030::WinitWindowAccessor;
    u.window().with_winit_window(|w| { w.set_minimized(false); w.focus_window(); });
}

pub fn init(ui: &AppWindow, state: Shared) {
    #[cfg(windows)]
    platform::win::set_process_app_id();
    platform::webview_init();
    UI.with(|u| *u.borrow_mut() = Some(ui.as_weak()));
    STATE.with(|s| *s.borrow_mut() = Some(state));

    // Linux: WebKitGTK and the GTK file dialogs need their loop turned
    #[cfg(target_os = "linux")]
    {
        let t = slint::Timer::default();
        t.start(slint::TimerMode::Repeated, std::time::Duration::from_millis(16), platform::pump);
        TIMERS.with(|v| v.borrow_mut().push(t));
    }

    if tray_supported() {
        TRAY.with(|t| *t.borrow_mut() = tray::Tray::new());
    }

    // closing the window: to the tray (setting) or quit
    ui.window().on_close_requested(|| {
        let to_tray = Prefs::load().close_to_tray && TRAY.with(|t| t.borrow().is_some());
        if !to_tray {
            // an open draft is saved before Barbican quits (the window stays until then)
            if let Some(u) = app().filter(|u| u.get_composing()) {
                quit_saving_draft(&u, || Ok(()));
                return slint::CloseRequestResponse::KeepWindowShown;
            }
            slint::quit_event_loop().ok();
        }
        slint::CloseRequestResponse::HideWindow
    });

    notify::set_handler(|a| {
        let _ = slint::invoke_from_event_loop(move || activate(a));
    });

    // unread badge (tray, taskbar overlay) and the updater's state, checked every second
    let t = slint::Timer::default();
    t.start(slint::TimerMode::Repeated, std::time::Duration::from_secs(1), || {
        if let Some(u) = app() { refresh_badge(&u); refresh_update(&u); }
    });
    TIMERS.with(|v| v.borrow_mut().push(t));

    // updates: a first check soon after start, then every few hours
    let t = slint::Timer::default();
    t.start(slint::TimerMode::SingleShot, std::time::Duration::from_secs(20), || {
        if updater::auto_check() { updater::check(false); }
        let every = slint::Timer::default();
        every.start(slint::TimerMode::Repeated, updater::CHECK_EVERY, || if updater::auto_check() { updater::check(false); });
        TIMERS.with(|v| v.borrow_mut().push(every));
    });
    TIMERS.with(|v| v.borrow_mut().push(t));

    let u = ui.global::<crate::UpdateState>();
    u.on_restart(|| if let Some(u) = app() { restart_to_update(&u); });
    u.on_later(|| {
        let msg = match updater::approve_next_start() { Ok(()) => "The update installs the next time Barbican starts.".to_string(), Err(e) => e };
        if let Some(u) = app() {
            u.global::<crate::UpdateState>().set_mode(0);
            dismiss_current(&u);
            crate::mail::show_toast(&u, &msg, false);
        }
    });
    u.on_download(|| {
        let url = updater::download_page();
        if !url.is_empty() { platform::open_url(&url); }
        if let Some(u) = app() { dismiss_current(&u); }
    });
    u.on_dismiss(|| if let Some(u) = app() { dismiss_current(&u); });
}

/// Quit, saving an open draft first (Bulwark: "Your drafts are saved first"). `then` runs
/// right before the loop ends; nothing happens when the draft could not be saved.
pub fn quit_saving_draft(u: &AppWindow, then: fn() -> Result<(), String>) {
    let finish = move || match then() {
        Ok(()) => { slint::quit_event_loop().ok(); }
        Err(e) => if let Some(u) = app() { crate::mail::show_toast(&u, &e, false); },
    };
    if !u.get_composing() { finish(); return; }
    u.invoke_save_draft(true);
    let tries = std::rc::Rc::new(Cell::new(0));
    let timer = std::rc::Rc::new(slint::Timer::default());
    let t2 = timer.clone();
    timer.start(slint::TimerMode::Repeated, std::time::Duration::from_millis(200), move || {
        tries.set(tries.get() + 1);
        let Some(u) = app() else { t2.stop(); return };
        if !u.get_composing() { t2.stop(); finish(); }
        else if tries.get() > 50 {
            t2.stop();
            crate::mail::show_toast(&u, "Your draft could not be saved, so Barbican stays open.", false);
        }
    });
}

fn restart_to_update(u: &AppWindow) {
    show_window(u);
    quit_saving_draft(u, updater::install_now);
}

fn dismiss_current(u: &AppWindow) {
    let v = u.global::<crate::UpdateState>().get_version().to_string();
    DISMISSED.with(|d| d.borrow_mut().push(v));
    u.global::<crate::UpdateState>().set_mode(0);
}

// ---------------- tray ----------------

/// Inbox unread of the signed-in account (the folder list's own count).
pub fn inbox_unread(u: &AppWindow) -> i64 {
    if !u.get_logged_in() { return 0; }
    let folders = u.get_folders();
    (0..folders.row_count()).filter_map(|i| folders.row_data(i))
        .find(|f| crate::mail::mailbox_role(&f.id) == "inbox").map(|f| f.unread.max(0) as i64).unwrap_or(0)
}

/// The tray's tooltip (and the overlay's description).
pub fn tooltip(unread: i64) -> String {
    match unread { 0 => "Barbican".into(), 1 => "Barbican: 1 unread message".into(), n => format!("Barbican: {n} unread messages") }
}

fn refresh_badge(u: &AppWindow) {
    let n = inbox_unread(u);
    if UNREAD.with(|x| x.replace(n)) == n { return; }
    TRAY.with(|t| if let Some(t) = &*t.borrow() { t.set_unread(n as u32); });
    #[cfg(windows)]
    {
        use slint::winit_030::{winit::raw_window_handle::{HasWindowHandle, RawWindowHandle}, WinitWindowAccessor};
        let hwnd = u.window().with_winit_window(|w| match w.window_handle().map(|h| h.as_raw()) {
            Ok(RawWindowHandle::Win32(h)) => Some(h.hwnd.get()),
            _ => None,
        }).flatten();
        if let Some(hwnd) = hwnd {
            let px = (n > 0).then(|| crate::icon::render(&crate::icon::overlay_svg(n as u32), 32)).flatten();
            platform::win::set_overlay(hwnd, px.as_deref().map(|p| (p, 32)), &tooltip(n));
        }
    }
}

/// What a tray menu item does.
pub fn tray_command(id: &str) {
    let Some(u) = app() else { return };
    match id {
        "open" => show_window(&u),
        "compose" => {
            show_window(&u);
            u.set_show_settings(false);
            u.set_app(0);
            u.invoke_compose_new();
        }
        "check" => {
            if u.get_logged_in() { u.invoke_refresh(); }
        }
        "quit" => quit_saving_draft(&u, || Ok(())),
        _ => {}
    }
}

mod tray {
    use tray_icon::menu::{Menu, MenuEvent, MenuItem, PredefinedMenuItem};
    use tray_icon::{Icon, MouseButton, MouseButtonState, TrayIcon, TrayIconBuilder, TrayIconEvent};

    pub struct Tray {
        icon: TrayIcon,
    }

    fn icon(unread: u32) -> Option<Icon> {
        let px = crate::icon::render(&crate::icon::app_svg(unread), 32)?;
        Icon::from_rgba(px, 32, 32).ok()
    }

    impl Tray {
        pub fn new() -> Option<Tray> {
            let menu = Menu::new();
            let _ = menu.append_items(&[
                &MenuItem::with_id("open", "Open Barbican", true, None),
                &MenuItem::with_id("compose", "Compose", true, None),
                &MenuItem::with_id("check", "Check mail", true, None),
                &PredefinedMenuItem::separator(),
                &MenuItem::with_id("quit", "Quit", true, None),
            ]);
            MenuEvent::set_event_handler(Some(|e: MenuEvent| {
                let id = e.id.0.clone();
                let _ = slint::invoke_from_event_loop(move || super::tray_command(&id));
            }));
            TrayIconEvent::set_event_handler(Some(|e: TrayIconEvent| {
                // a left click (Windows) or activation (Linux) opens the window
                let open = match e {
                    TrayIconEvent::Click { button: MouseButton::Left, button_state: MouseButtonState::Up, .. } => true,
                    TrayIconEvent::DoubleClick { button: MouseButton::Left, .. } => true,
                    _ => false,
                };
                if open { let _ = slint::invoke_from_event_loop(|| super::tray_command("open")); }
            }));
            let icon = TrayIconBuilder::new()
                .with_id("barbican")
                .with_menu(Box::new(menu))
                .with_menu_on_left_click(false)
                .with_tooltip(super::tooltip(0))
                .with_icon(icon(0)?)
                .build()
                .ok()?;
            Some(Tray { icon })
        }

        pub fn set_unread(&self, n: u32) {
            let _ = self.icon.set_icon(icon(n));
            let _ = self.icon.set_tooltip(Some(super::tooltip(n as i64)));
        }
    }
}

// ---------------- notifications ----------------

/// A notification's click or button (Bulwark: the toast's context and action).
fn activate(a: notify::Activation) {
    let Some(u) = app() else { return };
    let st = STATE.with(|s| s.borrow().clone());
    if let Some(ev) = a.context.strip_prefix("event:") {
        show_window(&u);
        u.set_show_settings(false);
        match EVENT_HANDLER.with(|h| h.get()) {
            Some(f) => f(&u, &a.action, ev),
            None => { u.set_app(1); u.invoke_app_changed(1); }
        }
        return;
    }
    let inbox = || {
        let f = u.get_folders();
        (0..f.row_count()).filter_map(|i| f.row_data(i)).find(|f| crate::mail::mailbox_role(&f.id) == "inbox").map(|f| f.id.to_string())
    };
    if a.context == "inbox" {
        show_window(&u);
        u.set_show_settings(false);
        u.set_app(0);
        u.invoke_app_changed(0);
        if let Some(id) = inbox() { u.invoke_select_folder(id.into()); }
        return;
    }
    let Some(id) = a.context.strip_prefix("mail:").map(String::from) else { return };
    match a.action.as_str() {
        "archive" | "mark-read" | "delete" => {
            let Some(c) = st.and_then(|s| s.lock().unwrap().clone()) else { return };
            let action = match a.action.as_str() { "mark-read" => "read", x => x };
            crate::mail::apply_confirmed(&u, c, vec![id.clone()], action, &[id]);
        }
        _ => {
            show_window(&u);
            u.set_show_settings(false);
            u.set_app(0);
            u.invoke_app_changed(0);
            // from another folder: go to the Inbox the message arrived in, then open it
            let in_list = { let m = u.get_mails(); (0..m.row_count()).any(|i| m.row_data(i).is_some_and(|r| r.id == id)) };
            if !in_list {
                if let Some(ib) = inbox() { if ib != u.get_current_folder().as_str() { u.invoke_select_folder(ib.into()); } }
            }
            u.set_current_mail(id.clone().into());
            u.invoke_open_mail(id.into());
        }
    }
}

// ---------------- updates ----------------

fn refresh_update(u: &AppWindow) {
    let s = updater::state();
    if UPDATE_REV.with(|r| r.replace(s.rev)) == s.rev { return; }
    let kind = updater::current_kind();
    let g = u.global::<crate::UpdateState>();
    let dismissed = DISMISSED.with(|d| d.borrow().iter().any(|v| *v == s.target));
    let mode = match s.status {
        updater::Status::Ready if !dismissed => 1,
        // only installs that can't update themselves get the "available" prompt (and not dev builds)
        updater::Status::Available if !dismissed && matches!(kind, updater::Kind::Zip) => 2,
        _ => 0,
    };
    g.set_version(s.target.clone().into());
    g.set_detail(match mode {
        1 => "It installs when you restart Barbican. An open draft is saved first.".into(),
        2 => "Download the new version from the release page.".into(),
        _ => "".into(),
    });
    g.set_mode(mode);
    // the About page shows the live status
    if u.get_show_settings() && u.get_settings_tab() == "about" && u.get_settings_query().is_empty() {
        u.invoke_settings_search(u.get_settings_query());
    }
}

// ---------------- settings ----------------

fn s(v: &str) -> slint::SharedString { v.into() }

fn row(kind: &str, key: &str, label: &str, desc: &str) -> crate::SRow {
    crate::SRow { kind: s(kind), key: s(key), label: s(label), desc: s(desc), options: Default::default(), index: 0, on: false, text: s("") }
}

/// Rows of the Desktop app page and the Updates section of About (Bulwark desktop-os-settings.tsx,
/// desktop-update-settings.tsx).
pub fn settings_rows(tab: &str, p: &settings::Settings) -> Vec<crate::SRow> {
    let mut out = vec![];
    let toggle = |key: &str, label: &str, desc: &str, on: bool| { let mut r = row("toggle", key, label, desc); r.on = on; r };
    match tab {
        "desktop" => {
            out.push(row("section", "", "Starting and closing", "How the app works with your computer."));
            let login = p.bool("desktop.startAtLogin", false);
            out.push(toggle("desktop.startAtLogin", "Start when you sign in to your computer", "The app starts by itself when you sign in.", login));
            if login {
                out.push(toggle("desktop.startMinimized", "Start in the tray", "No window opens at sign-in. Use the tray icon to open it.", p.bool("desktop.startMinimized", false)));
            }
            out.push(toggle("desktop.closeToTray", "Keep running in the tray when closed", "New mail and reminders keep coming after you close the window.", p.bool("desktop.closeToTray", false)));
        }
        "about" => {
            let st = updater::state();
            let kind = updater::current_kind();
            out.push(row("section", "", "Updates", ""));
            let (label, button) = match st.status {
                updater::Status::Ready => ("Restart to update", "desktop.update.restart"),
                updater::Status::Available if !matches!(kind, updater::Kind::Dev) => ("Download", "desktop.update.download"),
                _ => ("Check now", "desktop.update.check"),
            };
            let mut r = row("button", button, "Updates", &format!("Barbican {} · {}", updater::VERSION, updater::status_text(&st, kind)));
            r.text = s(label);
            out.push(r);
            let mut ch = row("select", "desktop.updateChannel", "Update channel", "Beta versions come earlier and can have more problems.");
            ch.options = slint::ModelRc::new(slint::VecModel::from(vec![s("Stable"), s("Beta")]));
            ch.index = p.int("desktop.updateChannel", 0).clamp(0, 1) as i32;
            out.push(ch);
            out.push(toggle("desktop.autoUpdate", "Check for updates automatically",
                "Checks every few hours and downloads new versions in the background. Nothing installs until you say so.", p.bool("desktop.autoUpdate", true)));
        }
        _ => {}
    }
    out
}

/// After a desktop setting changed (Settings page).
pub fn setting_changed(key: &str, on: bool) {
    match key {
        "desktop.startAtLogin" => {
            let plan = platform::autostart_plan(on, &platform::launcher_path());
            if let Err(e) = platform::apply_autostart(&plan) {
                if let Some(u) = app() { crate::mail::show_toast(&u, &format!("Could not change start at sign-in: {e}"), false); }
            }
        }
        "desktop.updateChannel" | "desktop.autoUpdate" if on => updater::check(true),
        _ => {}
    }
}

/// Settings buttons of the Updates section. Returns whether `key` was one of them.
pub fn settings_button(u: &AppWindow, key: &str) -> bool {
    match key {
        "desktop.update.check" => { updater::check(true); true }
        "desktop.update.restart" => { restart_to_update(u); true }
        "desktop.update.download" => {
            let url = updater::download_page();
            if !url.is_empty() { platform::open_url(&url); }
            true
        }
        _ => false,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn hidden_start_only_at_login_with_tray() {
        let p = Prefs { close_to_tray: false, start_at_login: true, start_minimized: true };
        let at_login = vec!["barbican".to_string(), "--autostart".to_string()];
        assert!(start_hidden_for(&at_login, &p, true));
        // started by hand: the window opens
        assert!(!start_hidden_for(&["barbican".to_string()], &p, true));
        // no tray to come back from (or automation): the window opens
        assert!(!start_hidden_for(&at_login, &p, false));
        assert!(!start_hidden_for(&at_login, &Prefs { start_minimized: false, ..p }, true));
        assert!(!start_hidden_for(&at_login, &Prefs { start_at_login: false, close_to_tray: false, start_minimized: true }, true));
    }

    #[test]
    fn tooltips() {
        assert_eq!(tooltip(0), "Barbican");
        assert_eq!(tooltip(1), "Barbican: 1 unread message");
        assert_eq!(tooltip(5), "Barbican: 5 unread messages");
    }

    #[test]
    fn desktop_rows() {
        let mut m = serde_json::Map::new();
        let p = settings::Settings::with_prefs(m.clone());
        let keys: Vec<String> = settings_rows("desktop", &p).iter().map(|r| r.key.to_string()).collect();
        // "Start in the tray" only under "Start when you sign in"
        assert_eq!(keys, ["", "desktop.startAtLogin", "desktop.closeToTray"]);
        m.insert("desktop.startAtLogin".into(), serde_json::json!(true));
        let p = settings::Settings::with_prefs(m);
        let keys: Vec<String> = settings_rows("desktop", &p).iter().map(|r| r.key.to_string()).collect();
        assert_eq!(keys, ["", "desktop.startAtLogin", "desktop.startMinimized", "desktop.closeToTray"]);
        let about: Vec<String> = settings_rows("about", &p).iter().map(|r| r.key.to_string()).collect();
        assert_eq!(about, ["", "desktop.update.check", "desktop.updateChannel", "desktop.autoUpdate"]);
    }
}
