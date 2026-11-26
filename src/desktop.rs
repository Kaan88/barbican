//! The desktop app's OS integration (Bulwark components/desktop/desktop-os-host.tsx,
//! lib/desktop/os-summary.ts, settings desktopOs / desktop_updates): tray icon with the
//! unread badge and Open / Compose / Check mail / Quit, close to tray, start in the tray at
//! sign-in, start at login, the taskbar's unread overlay, what notification clicks and
//! buttons do, and the updater's prompt.
use crate::{notify, platform, settings, updater, AppWindow, Shared};
use slint::{ComponentHandle, Model};
use std::cell::{Cell, RefCell};

thread_local! {
    static UI: RefCell<Option<crate::ui_types::Weak<AppWindow>>> = const { RefCell::new(None) };
    static STATE: RefCell<Option<Shared>> = const { RefCell::new(None) };
    static TRAY: RefCell<Option<tray::Tray>> = const { RefCell::new(None) };
    static UNREAD: Cell<i64> = const { Cell::new(-1) };
    static UPDATE_REV: Cell<u64> = const { Cell::new(u64::MAX) };
    /// A toast of another login, waiting for that login's folders after the switch.
    static PENDING: RefCell<Option<(notify::MailRef, std::time::Instant)>> = const { RefCell::new(None) };
    /// Versions whose prompt the user closed this session.
    static DISMISSED: RefCell<Vec<String>> = const { RefCell::new(Vec::new()) };
    /// After a draft could not be saved, quitting again within a minute closes without it.
    static QUIT_ANYWAY_UNTIL: Cell<Option<std::time::Instant>> = const { Cell::new(None) };
    static TIMERS: RefCell<Vec<crate::ui_types::Timer>> = const { RefCell::new(Vec::new()) };
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
    crate::ui_types::with_winit_window(u.window(), |w| { w.set_minimized(false); w.focus_window(); });
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
        let t = crate::ui_types::Timer::default();
        t.start(crate::ui_types::TimerMode::Repeated, std::time::Duration::from_millis(16), platform::pump);
        TIMERS.with(|v| v.borrow_mut().push(t));
    }

    if tray_supported() {
        TRAY.with(|t| *t.borrow_mut() = tray::Tray::new());
    }

    // the installer's quit signal: the same graceful quit as the tray's Quit item (saves an open
    // draft first), whether or not the window is hidden in the tray
    platform::start_quit_listener(|| {
        let _ = slint::invoke_from_event_loop(|| if let Some(u) = app() { quit_saving_draft(&u, || Ok(())); });
    });

    // closing the window: to the tray (setting) or quit
    ui.window().on_close_requested(|| {
        // the window's place is saved whichever way it closes
        if let Some(u) = app() { crate::winstate::save(&u); }
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
    let t = crate::ui_types::Timer::default();
    t.start(crate::ui_types::TimerMode::Repeated, std::time::Duration::from_secs(1), || {
        if let Some(u) = app() { refresh_badge(&u); refresh_update(&u); }
    });
    TIMERS.with(|v| v.borrow_mut().push(t));

    // updates: a first check soon after start, then every few hours
    let t = crate::ui_types::Timer::default();
    // (the default profile only: a test, automation or dev profile never checks or downloads)
    t.start(crate::ui_types::TimerMode::SingleShot, std::time::Duration::from_secs(20), || {
        if !updater::enabled() { return; }
        if updater::auto_check() { updater::check(false); }
        let every = crate::ui_types::Timer::default();
        every.start(crate::ui_types::TimerMode::Repeated, updater::CHECK_EVERY, || if updater::enabled() && updater::auto_check() { updater::check(false); });
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
        Err(e) => if let Some(u) = app() { crate::mail::show_error(&u, &e); },
    };
    let anyway = QUIT_ANYWAY_UNTIL.with(|q| q.get()).is_some_and(|t| std::time::Instant::now() < t);
    // nothing typed: no draft is made on the server
    if !u.get_composing() || anyway || crate::compose::composer_blank(u) { finish(); return; }
    let fails = crate::compose::save_failures();
    u.invoke_save_draft(true);
    let tries = std::rc::Rc::new(Cell::new(0));
    let timer = std::rc::Rc::new(crate::ui_types::Timer::default());
    let t2 = timer.clone();
    timer.start(crate::ui_types::TimerMode::Repeated, std::time::Duration::from_millis(100), move || {
        tries.set(tries.get() + 1);
        let Some(u) = app() else { t2.stop(); return };
        // the composer may close at once while its save waits behind an autosave (src/autosave.rs):
        // quit only when no save is running any more
        if !u.get_composing() && !crate::autosave::in_flight() { t2.stop(); finish(); }
        // a failed save is reported at once; a save that hangs gives up after 15 s
        else if crate::compose::save_failures() != fails || tries.get() > 150 {
            t2.stop();
            QUIT_ANYWAY_UNTIL.with(|q| q.set(Some(std::time::Instant::now() + std::time::Duration::from_secs(60))));
            crate::mail::show_toast(&u, "Your draft could not be saved. Quit again to close without saving it.", false);
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
        use crate::ui_types::winit::raw_window_handle::{HasWindowHandle, RawWindowHandle};
        let hwnd = crate::ui_types::with_winit_window(u.window(), |w| match w.window_handle().map(|h| h.as_raw()) {
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
    // several new messages: the Inbox of the login that got them
    if let Some(r) = notify::parse_inbox_context(&a.context) {
        match st.and_then(|s| s.lock().unwrap().clone()) {
            Some(c) => open_from_toast(&u, c, r),
            None => { show_window(&u); u.set_show_settings(false); u.set_app(0); u.invoke_app_changed(0); }
        }
        return;
    }
    let Some(r) = notify::parse_mail_context(&a.context) else { return };
    // no session (signed out, or the add-account form is up): a click still brings the window up
    let Some(c) = st.and_then(|s| s.lock().unwrap().clone()) else {
        if !matches!(a.action.as_str(), "archive" | "mark-read" | "delete") { show_window(&u); }
        return;
    };
    match a.action.as_str() {
        "archive" | "mark-read" | "delete" => {
            // another login's message id means nothing to this session: say so, do nothing
            if r.account.as_deref().is_some_and(|x| x != crate::unified::login_of(&c)) {
                crate::mail::show_toast(&u, "That message belongs to another account. Click the notification to switch to it.", false);
                return;
            }
            let action = match a.action.as_str() { "mark-read" => "read", x => x };
            crate::mail::apply_confirmed(&u, c, vec![r.id.clone()], action, &[r.id]);
        }
        _ => open_from_toast(&u, c, r),
    }
}

/// Folder and window state the activation decides on.
fn view_of(u: &AppWindow, c: &crate::jmap::Client, id: &str) -> notify::View {
    let f = u.get_folders();
    let m = u.get_mails();
    let ids: Vec<String> = (0..f.row_count()).filter_map(|i| f.row_data(i)).map(|f| f.id.to_string()).filter(|i| !i.starts_with("__")).collect();
    notify::View {
        // the remembered login, as the toast carries it (the session's own user name can differ)
        account: crate::unified::login_of(c),
        folder: u.get_current_folder().to_string(),
        narrowed: !u.get_search().trim().is_empty() || !u.get_current_tag().is_empty(),
        listed: !id.is_empty() && (0..m.row_count()).any(|i| m.row_data(i).is_some_and(|r| r.id == id)),
        current_mail: u.get_current_mail().to_string(),
        inbox: crate::mail::role_id("inbox").filter(|i| ids.contains(i)),
        folders: ids,
    }
}

/// Show a folder the way a click on it does: no search or tag, its row selected (and unfolded
/// in the tree), its list loaded.
fn select_folder(u: &AppWindow, id: &str) {
    u.set_current_tag("".into());
    u.set_search("".into());
    u.set_current_folder(id.into());
    crate::mail::reveal_folder(u, id);
    u.invoke_select_folder(id.into());
}

/// A toast's click: the window comes up, the message's login is made the active one (Bulwark
/// applyMailDeepLink: waits for it and switches), then its own folder, then the message. A
/// reference without a message (several new messages) ends at the login's Inbox.
fn open_from_toast(u: &AppWindow, c: std::sync::Arc<crate::jmap::Client>, r: notify::MailRef) {
    show_window(u);
    u.set_show_settings(false);
    u.set_app(0);
    u.invoke_app_changed(0);
    let signed_in: Vec<String> = crate::oauth::accounts().iter().filter_map(|a| a["user"].as_str().map(String::from)).collect();
    match notify::plan_account(&r, &crate::unified::login_of(&c), &signed_in) {
        notify::AccountPlan::Here => open_here(u, c, r, false),
        notify::AccountPlan::Missing => crate::mail::show_toast(u, "That message belongs to an account that is not signed in.", false),
        notify::AccountPlan::Switch(user) => {
            // the folders of that login are not there yet: open the message once they are
            // (resume_pending, from the folder load that follows the switch)
            PENDING.with(|p| *p.borrow_mut() = Some((r, std::time::Instant::now())));
            u.invoke_switch_account(user.into());
        }
    }
}

/// The folders of `c` just loaded after a login switch: run the toast that asked for it.
/// Returns whether one did (the Inbox is then not selected on top of it).
pub fn resume_pending(u: &AppWindow, c: &std::sync::Arc<crate::jmap::Client>) -> bool {
    let Some((r, at)) = PENDING.with(|p| p.borrow_mut().take()) else { return false };
    if !notify::pending_applies(&r, at, std::time::Instant::now(), &crate::unified::login_of(c)) { return false; }
    open_here(u, c.clone(), r, true);
    true
}

/// A switch that failed: its queued toast must not open the message at a later switch.
pub fn drop_pending() { PENDING.with(|p| *p.borrow_mut() = None); }

/// Select the message's folder in the active login and open it. Where the message is now comes
/// from the list on screen or from the server (it may have moved since the toast, or be gone).
/// `fresh`: the login was switched to just now, so the list and folder on screen are the
/// previous login's and nothing there counts.
fn open_here(u: &AppWindow, c: std::sync::Arc<crate::jmap::Client>, r: notify::MailRef, fresh: bool) {
    let then = view_of(u, &c, &r.id);
    let v = if fresh { notify::View { folder: String::new(), narrowed: false, listed: false, ..then.clone() } } else { then.clone() };
    // only the Inbox (a toast for several messages): nothing to look up
    if r.id.is_empty() {
        finish_open(u, &r, &v, &notify::Location::Unknown, fresh);
        return;
    }
    if !fresh {
        if let Some(m) = crate::mail::email(&r.id) {
            let boxes = m["mailboxIds"].as_object().map(|o| o.iter().filter(|(_, v)| **v == true).map(|(k, _)| k.clone()).collect());
            if let Some(b) = boxes {
                finish_open(u, &r, &v, &notify::Location::In(b), false);
                return;
            }
        }
    }
    let w = u.as_weak();
    std::thread::spawn(move || {
        let at = match c.email_mailboxes(&r.id) {
            Ok(Some(b)) => notify::Location::In(b),
            Ok(None) => notify::Location::Gone,
            Err(_) => notify::Location::Unknown,
        };
        let _ = w.upgrade_in_event_loop(move |u| {
            // the user may have moved on while the server answered: another login, folder or
            // message is theirs now; the message list may have changed too
            let now = match STATE.with(|s| s.borrow().clone()).and_then(|s| s.lock().unwrap().clone()) {
                Some(c) => view_of(&u, &c, &r.id),
                None => return,
            };
            if notify::moved_on(&then, &now) { return; }
            let v = if fresh { notify::View { folder: String::new(), narrowed: false, listed: false, ..now } } else { now };
            finish_open(&u, &r, &v, &at, fresh);
        });
    });
}

fn finish_open(u: &AppWindow, r: &notify::MailRef, v: &notify::View, at: &notify::Location, fresh: bool) {
    let plan = notify::plan_mail_open(r, v, at);
    // after a switch the list is empty: a message that cannot be shown still leaves the Inbox
    let folder = plan.folder.clone().or_else(|| if fresh { v.inbox.clone() } else { None });
    if let Some(f) = &folder { select_folder(u, f); }
    if let Some(n) = plan.notice { crate::mail::show_toast(u, n, false); }
    if plan.open {
        u.set_current_mail(r.id.clone().into());
        u.invoke_open_mail(r.id.clone().into());
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
        // no update prompt outside the default profile
        _ if !updater::enabled() => 0,
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

fn s(v: &str) -> crate::ui_types::SharedString { v.into() }

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
        "about" => out.extend(about_rows(updater::enabled(), p)),
        _ => {}
    }
    out
}

/// The Updates section of About; outside the default profile only a note (see `updater::enabled`).
fn about_rows(enabled: bool, p: &settings::Settings) -> Vec<crate::SRow> {
    let mut out = vec![];
    let toggle = |key: &str, label: &str, desc: &str, on: bool| { let mut r = row("toggle", key, label, desc); r.on = on; r };
    if !enabled {
        out.push(row("section", "", "Updates", "This profile does not update Barbican. The default profile checks for updates and installs them."));
        return out;
    }
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
        "desktop.updateChannel" | "desktop.autoUpdate" if on && updater::enabled() => updater::check(true),
        _ => {}
    }
}

/// Settings buttons of the Updates section. Returns whether `key` was one of them.
pub fn settings_button(u: &AppWindow, key: &str) -> bool {
    match key {
        k if k.starts_with("desktop.update.") && !updater::enabled() => true,
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
        let about: Vec<String> = about_rows(true, &p).iter().map(|r| r.key.to_string()).collect();
        assert_eq!(about, ["", "desktop.update.check", "desktop.updateChannel", "desktop.autoUpdate"]);
        // another profile does not update: a note, no buttons or toggles
        let about: Vec<String> = about_rows(false, &p).iter().map(|r| r.key.to_string()).collect();
        assert_eq!(about, [""]);
        // (a test process runs under a throwaway profile)
        assert_eq!(settings_rows("about", &p).len(), 1);
    }
}
