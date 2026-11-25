//! Platform layer: everything that differs between Windows and Linux sits behind these
//! functions, so the rest of Barbican stays the same on both.
//!
//! | what                 | Windows                                 | Linux                                              |
//! |----------------------|-----------------------------------------|----------------------------------------------------|
//! | email view           | WebView2 (wry)                          | WebKitGTK (wry, X11 child window; else Blitz)     |
//! | secrets              | Credential Manager (keyring)            | Secret Service (keyring, zbus)                     |
//! | system light/dark    | registry `AppsUseLightTheme`            | XDG portal `color-scheme`, then gsettings          |
//! | spell checking       | ISpellChecker (rte/spell.rs)            | hunspell `-a` pipe (platform/hunspell.rs) or none  |
//! | notifications        | WinRT toasts with app id and group      | freedesktop notifications (D-Bus) with actions     |
//! | start at login       | HKCU `...\CurrentVersion\Run` value     | `~/.config/autostart/barbican.desktop`             |
//! | folders              | %APPDATA% / %LOCALAPPDATA%              | $XDG_CONFIG_HOME / $XDG_DATA_HOME                  |
use std::path::{Path, PathBuf};

pub mod hunspell;
#[cfg(windows)]
pub mod win;

/// Settings, sessions and caches that belong to the user (roams on Windows), per profile.
pub fn config_dir() -> PathBuf {
    let root = app_config_dir();
    match crate::profile::subdir(crate::profile::current()) {
        Some(sub) => root.join(sub),
        None => root,
    }
}

/// The config folder of the default profile (other profiles live below it, see `profile.rs`).
fn app_config_dir() -> PathBuf {
    // a test process never writes to the user's real folders: its own throwaway root
    #[cfg(test)]
    { return test_root().join("config"); }
    #[cfg(all(not(test), windows))]
    {
        let base = std::env::var("APPDATA").unwrap_or_else(|_| ".".into());
        PathBuf::from(base).join("Barbican")
    }
    #[cfg(all(not(test), not(windows)))]
    {
        xdg_dir("XDG_CONFIG_HOME", ".config").join("barbican")
    }
}

/// The folder a unit-test process keeps its config and data in (`<temp>/barbican-test-<pid>`),
/// so tests never touch the real %APPDATA% / %LOCALAPPDATA%. Not removed when the run ends.
#[cfg(test)]
fn test_root() -> PathBuf {
    std::env::temp_dir().join(format!("barbican-test-{}", std::process::id()))
}

/// Large machine-local data: downloaded updates (shared by every profile, `data_dir()/updates`)
/// and the mail view's WebView profile (see [`webview_dir`]).
pub fn data_dir() -> PathBuf {
    #[cfg(test)]
    { return test_root().join("data"); }
    #[cfg(all(not(test), windows))]
    {
        let base = std::env::var("LOCALAPPDATA").or_else(|_| std::env::var("APPDATA")).unwrap_or_else(|_| ".".into());
        PathBuf::from(base).join("Barbican")
    }
    #[cfg(all(not(test), not(windows)))]
    {
        xdg_dir("XDG_DATA_HOME", ".local/share").join("barbican")
    }
}

// ---- what belongs to one profile, outside the config folder ----
//
// The config folder is per profile already. The mail view's WebView folder, the folder mail
// attachments are opened from and the print file are not: they each get a name per profile,
// so one profile's sign-out (which deletes them) can never reach another's. The default
// profile keeps the names every earlier version used.

/// The WebView user-data folder of a profile: `<data>/webview` (default, where 0.1.0 and 0.1.1
/// put it) or `<data>/profiles/<profile>/webview`.
pub fn webview_dir_for(profile: &str, data: &Path) -> PathBuf {
    match crate::profile::subdir(profile) {
        Some(sub) => data.join(sub).join("webview"),
        None => data.join("webview"),
    }
}

/// Where attachments and files opened in another program are written: `<temp>/Barbican`
/// (default) or `<temp>/Barbican-<profile>`.
pub fn temp_folder_for(profile: &str, temp: &Path) -> PathBuf {
    if crate::profile::is_default(profile) { temp.join("Barbican") } else { temp.join(format!("Barbican-{profile}")) }
}

/// The file versions before 0.7 wrote the last printed message to: `<temp>/barbican-print.html`
/// (default) or `<temp>/barbican-print-<profile>.html`. Printing is gone; sign-out still removes
/// one left behind (signout.rs).
pub fn print_file_for(profile: &str, temp: &Path) -> PathBuf {
    if crate::profile::is_default(profile) { temp.join("barbican-print.html") } else { temp.join(format!("barbican-print-{profile}.html")) }
}

/// This process's WebView folder.
pub fn webview_dir() -> PathBuf { webview_dir_for(crate::profile::current(), &data_dir()) }

/// This process's folder for files opened from mail.
pub fn mail_temp_dir() -> PathBuf { temp_folder_for(crate::profile::current(), &std::env::temp_dir()) }

#[cfg(not(windows))]
fn xdg_dir(var: &str, fallback: &str) -> PathBuf {
    match std::env::var(var) {
        Ok(v) if v.starts_with('/') => PathBuf::from(v),
        _ => PathBuf::from(std::env::var("HOME").unwrap_or_else(|_| ".".into())).join(fallback),
    }
}

/// Where secrets are kept, for setting descriptions.
/// Whether this is the only Barbican running in this session (Windows: a named mutex held
/// until exit). Elsewhere always true for now.
pub fn first_instance() -> bool {
    #[cfg(windows)]
    { win::first_instance() }
    #[cfg(not(windows))]
    { true }
}

/// Windows: listen for the installer's quit signal (`Local\Barbican.Quit.<pid>`); `on_signal`
/// runs on a background thread. Elsewhere nothing.
pub fn start_quit_listener(on_signal: fn()) {
    #[cfg(windows)]
    win::start_quit_listener(on_signal);
    #[cfg(not(windows))]
    let _ = on_signal;
}

/// Whether this is the first Barbican of this profile to start (Windows: decided by the
/// profile's own mutex, so a second instance of another profile does not count). Elsewhere
/// always true for now.
pub fn first_of_profile() -> bool {
    #[cfg(windows)]
    { win::first_of_profile() }
    #[cfg(not(windows))]
    { true }
}

/// Live, at the moment of asking: is another Barbican process of this profile running, one that
/// may have this profile's WebView folder open? Windows: the profile's named mutex, see
/// `win.rs`. Elsewhere there is no such check yet, so the answer is the cautious one, true:
/// sign-out then leaves the WebView folder for the next start's sweep instead of deleting a
/// folder another process might still use.
pub fn other_of_profile_running() -> bool {
    #[cfg(windows)]
    { win::other_of_profile_running() }
    #[cfg(not(windows))]
    { true }
}

pub fn keystore_name() -> &'static str {
    if cfg!(windows) { "Windows Credential Manager" } else { "your system keyring (Secret Service)" }
}

/// The spell checker's name, for setting descriptions.
pub fn spell_engine_name() -> &'static str {
    if cfg!(windows) { "the Windows spell checker" } else { "hunspell (when installed)" }
}

/// Open a web address or a folder with the system's default handler.
pub fn open_url(url: &str) {
    #[cfg(windows)]
    {
        let _ = std::process::Command::new("rundll32").args(["url.dll,FileProtocolHandler", url]).spawn();
    }
    #[cfg(not(windows))]
    {
        let _ = std::process::Command::new("xdg-open").arg(url).spawn();
    }
}

/// Plain text to the clipboard.
pub fn copy_text(text: &str) -> bool {
    #[cfg(windows)]
    {
        use std::io::Write;
        use std::os::windows::process::CommandExt;
        let mut cmd = std::process::Command::new("clip.exe");
        cmd.creation_flags(0x0800_0000);
        let Ok(mut child) = cmd.stdin(std::process::Stdio::piped()).spawn() else { return false };
        let ok = child.stdin.take().is_some_and(|mut s| s.write_all(text.as_bytes()).is_ok());
        ok && child.wait().is_ok_and(|s| s.success())
    }
    #[cfg(not(windows))]
    {
        arboard::Clipboard::new().and_then(|mut c| c.set_text(text.to_string())).is_ok()
    }
}

/// UTF-16 with the closing NUL that CF_UNICODETEXT wants.
pub fn utf16z(text: &str) -> Vec<u16> { text.encode_utf16().chain(std::iter::once(0)).collect() }

/// A secret (an app password, an API key, a TOTP link) to the clipboard, kept out of Windows'
/// clipboard history, cloud clipboard and clipboard monitors: the text is set together with the
/// `ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory` (0) and
/// `CanUploadToCloudClipboard` (0) formats. Elsewhere it is the plain clipboard (a history there
/// is the desktop's own).
pub fn copy_secret(text: &str) -> bool {
    #[cfg(windows)]
    {
        use std::ffi::c_void;
        #[link(name = "user32")]
        extern "system" {
            fn OpenClipboard(owner: *mut c_void) -> i32;
            fn CloseClipboard() -> i32;
            fn EmptyClipboard() -> i32;
            fn SetClipboardData(format: u32, mem: *mut c_void) -> *mut c_void;
            fn RegisterClipboardFormatW(name: *const u16) -> u32;
            fn CreateWindowExW(ex: u32, class: *const u16, title: *const u16, style: u32, x: i32, y: i32, w: i32, h: i32,
                parent: *mut c_void, menu: *mut c_void, inst: *mut c_void, param: *mut c_void) -> *mut c_void;
            fn DestroyWindow(hwnd: *mut c_void) -> i32;
        }
        #[link(name = "kernel32")]
        extern "system" {
            fn GlobalAlloc(flags: u32, bytes: usize) -> *mut c_void;
            fn GlobalLock(mem: *mut c_void) -> *mut c_void;
            fn GlobalUnlock(mem: *mut c_void) -> i32;
            fn GlobalFree(mem: *mut c_void) -> *mut c_void;
        }
        const GMEM_MOVEABLE: u32 = 2;
        const CF_UNICODETEXT: u32 = 13;
        // a movable block holding `bytes`; None when memory is short
        unsafe fn block(bytes: &[u8]) -> Option<*mut c_void> {
            let h = GlobalAlloc(GMEM_MOVEABLE, bytes.len());
            if h.is_null() { return None; }
            let p = GlobalLock(h) as *mut u8;
            if p.is_null() { GlobalFree(h); return None; }
            std::ptr::copy_nonoverlapping(bytes.as_ptr(), p, bytes.len());
            GlobalUnlock(h);
            Some(h)
        }
        unsafe {
            // EmptyClipboard with no owner window makes SetClipboardData fail: the clipboard is
            // opened for a message-only window (parent HWND_MESSAGE, -3) of this thread
            let class = utf16z("STATIC");
            let owner = CreateWindowExW(0, class.as_ptr(), class.as_ptr(), 0, 0, 0, 0, 0, -3isize as *mut c_void,
                std::ptr::null_mut(), std::ptr::null_mut(), std::ptr::null_mut());
            let mut opened = false;
            for _ in 0..10 {
                if OpenClipboard(owner) != 0 { opened = true; break; }
                std::thread::sleep(std::time::Duration::from_millis(30));
            }
            if !opened { if !owner.is_null() { DestroyWindow(owner); } return false; }
            EmptyClipboard();
            let wide: Vec<u8> = utf16z(text).iter().flat_map(|u| u.to_le_bytes()).collect();
            let mut ok = false;
            if let Some(h) = block(&wide) {
                // the clipboard owns the block once it is set; on failure it is ours to free
                if SetClipboardData(CF_UNICODETEXT, h).is_null() { GlobalFree(h); } else { ok = true; }
            }
            if ok {
                for (name, value) in [("ExcludeClipboardContentFromMonitorProcessing", 1u32), ("CanIncludeInClipboardHistory", 0), ("CanUploadToCloudClipboard", 0)] {
                    let n: Vec<u16> = utf16z(name);
                    let f = RegisterClipboardFormatW(n.as_ptr());
                    if f == 0 { continue; }
                    if let Some(h) = block(&value.to_le_bytes()) {
                        if SetClipboardData(f, h).is_null() { GlobalFree(h); }
                    }
                }
            }
            CloseClipboard();
            if !owner.is_null() { DestroyWindow(owner); }
            ok
        }
    }
    #[cfg(not(windows))]
    {
        copy_text(text)
    }
}

/// The system's app theme: Some(true) for dark, None when it can't be told.
pub fn system_dark() -> Option<bool> {
    #[cfg(windows)]
    {
        win::apps_use_light_theme().map(|light| !light)
    }
    #[cfg(not(windows))]
    {
        let run = |cmd: &str, args: &[&str]| -> Option<String> {
            let o = std::process::Command::new(cmd).args(args).output().ok()?;
            o.status.success().then(|| String::from_utf8_lossy(&o.stdout).to_string())
        };
        // org.freedesktop.appearance color-scheme: 0 no preference, 1 dark, 2 light
        let portal = |method: &str| run("dbus-send", &["--session", "--print-reply=literal", "--reply-timeout=1000", "--dest=org.freedesktop.portal.Desktop",
            "/org/freedesktop/portal/desktop", &format!("org.freedesktop.portal.Settings.{method}"), "string:org.freedesktop.appearance", "string:color-scheme"]);
        if let Some(v) = portal("ReadOne").or_else(|| portal("Read")).and_then(|o| parse_portal_scheme(&o)) {
            return Some(v);
        }
        if let Some(v) = run("gsettings", &["get", "org.gnome.desktop.interface", "color-scheme"]).and_then(|o| parse_gsettings_scheme(&o)) {
            return Some(v);
        }
        run("gsettings", &["get", "org.gnome.desktop.interface", "gtk-theme"]).map(|o| o.to_lowercase().contains("dark"))
    }
}

/// dbus-send's literal reply to Settings.ReadOne/Read: "variant uint32 1" (or a variant in a
/// variant from older portals). 1 dark, 2 light, 0 no preference (None).
#[cfg_attr(windows, allow(dead_code))]
pub fn parse_portal_scheme(out: &str) -> Option<bool> {
    let n: u32 = out.split_whitespace().skip_while(|w| *w != "uint32").nth(1)?.parse().ok()?;
    match n { 1 => Some(true), 2 => Some(false), _ => None }
}

/// `gsettings get org.gnome.desktop.interface color-scheme`: 'prefer-dark' | 'prefer-light' | 'default'.
#[cfg_attr(windows, allow(dead_code))]
pub fn parse_gsettings_scheme(out: &str) -> Option<bool> {
    let v = out.trim().trim_matches('\'');
    match v { "prefer-dark" => Some(true), "prefer-light" => Some(false), _ => None }
}

// ---------------- email view (wry) ----------------

/// Called once before the first web view: GTK has to be up on Linux (WebKitGTK).
pub fn webview_init() {
    #[cfg(target_os = "linux")]
    {
        let _ = gtk::init();
    }
}

#[cfg_attr(not(target_os = "linux"), allow(dead_code))]
/// Run pending GTK work (the WebKitGTK view and the file dialogs); a no-op on Windows,
/// where WebView2 runs on the window's own message loop.
pub fn pump() {
    #[cfg(target_os = "linux")]
    {
        if gtk::is_initialized_main_thread() {
            while gtk::events_pending() { gtk::main_iteration_do(false); }
        }
    }
}

// ---------------- start at login ----------------

/// What turning "Start when you sign in" on or off writes. Built separately from applying it
/// so the exact change can be tested without touching the computer.
#[derive(Debug, Clone, PartialEq)]
pub enum Autostart {
    /// HKCU\Software\Microsoft\Windows\CurrentVersion\Run, value `name`: the command, or removed (None).
    WindowsRun { name: String, command: Option<String> },
    /// An XDG autostart entry: written, or removed (None).
    DesktopFile { path: PathBuf, contents: Option<String> },
}

/// Argument the login entry starts Barbican with ("Start in the tray" applies only then).
pub const AUTOSTART_ARG: &str = "--autostart";

/// The program to start: the AppImage itself when running from one (its mount point changes
/// every run), else this executable.
pub fn launcher_path() -> PathBuf {
    if let Some(p) = std::env::var_os("APPIMAGE").filter(|p| !p.is_empty()) { return PathBuf::from(p); }
    std::env::current_exe().unwrap_or_else(|_| PathBuf::from("barbican"))
}

pub fn autostart_plan(enable: bool, exe: &std::path::Path) -> Autostart {
    if cfg!(windows) {
        Autostart::WindowsRun { name: "Barbican".into(), command: enable.then(|| format!("\"{}\" {AUTOSTART_ARG}", exe.display())) }
    } else {
        let path = std::env::var("XDG_CONFIG_HOME").ok().filter(|v| v.starts_with('/')).map(PathBuf::from)
            .unwrap_or_else(|| PathBuf::from(std::env::var("HOME").unwrap_or_else(|_| ".".into())).join(".config"))
            .join("autostart").join("barbican.desktop");
        let exec = exe.display().to_string().replace('\\', "\\\\").replace('"', "\\\"");
        Autostart::DesktopFile {
            path,
            contents: enable.then(|| format!(
                "[Desktop Entry]\nType=Application\nName=Barbican\nComment=Mail, calendar and contacts\nExec=\"{exec}\" {AUTOSTART_ARG}\nIcon=barbican\nTerminal=false\nX-GNOME-Autostart-enabled=true\n")),
        }
    }
}

/// Write the plan. Only ever called from the user's own toggle in Settings.
pub fn apply_autostart(plan: &Autostart) -> Result<(), String> {
    match plan {
        Autostart::WindowsRun { name, command } => {
            #[cfg(windows)]
            { win::set_run_value(name, command.as_deref()) }
            #[cfg(not(windows))]
            { let _ = (name, command); Err("not on Windows".into()) }
        }
        Autostart::DesktopFile { path, contents } => match contents {
            Some(c) => {
                if let Some(d) = path.parent() { std::fs::create_dir_all(d).map_err(|e| e.to_string())?; }
                std::fs::write(path, c).map_err(|e| e.to_string())
            }
            None => match std::fs::remove_file(path) {
                Err(e) if e.kind() != std::io::ErrorKind::NotFound => Err(e.to_string()),
                _ => Ok(()),
            },
        },
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn clipboard_text_is_utf16_with_a_nul() {
        assert_eq!(utf16z(""), vec![0]);
        assert_eq!(utf16z("aé"), vec![0x61, 0xe9, 0]);
    }

    #[test]
    fn portal_reply() {
        assert_eq!(parse_portal_scheme("   variant       uint32 1\n"), Some(true));
        assert_eq!(parse_portal_scheme("   variant       variant       uint32 2\n"), Some(false));
        assert_eq!(parse_portal_scheme("   variant       uint32 0\n"), None);
        assert_eq!(parse_portal_scheme("Error org.freedesktop.portal.Error.NotFound"), None);
    }

    #[test]
    fn gsettings_reply() {
        assert_eq!(parse_gsettings_scheme("'prefer-dark'\n"), Some(true));
        assert_eq!(parse_gsettings_scheme("'prefer-light'"), Some(false));
        assert_eq!(parse_gsettings_scheme("'default'"), None);
    }

    #[test]
    fn autostart_plans() {
        let exe = std::path::Path::new(if cfg!(windows) { r"C:\Apps\Barbican\barbican.exe" } else { "/opt/Barbican.AppImage" });
        match autostart_plan(true, exe) {
            Autostart::WindowsRun { name, command } => {
                assert_eq!(name, "Barbican");
                assert_eq!(command.as_deref(), Some(r#""C:\Apps\Barbican\barbican.exe" --autostart"#));
            }
            Autostart::DesktopFile { path, contents } => {
                assert!(path.ends_with("autostart/barbican.desktop"));
                let c = contents.unwrap();
                assert!(c.contains("Exec=\"/opt/Barbican.AppImage\" --autostart\n"), "{c}");
                assert!(c.starts_with("[Desktop Entry]\nType=Application\n"));
            }
        }
        match autostart_plan(false, exe) {
            Autostart::WindowsRun { command, .. } => assert!(command.is_none()),
            Autostart::DesktopFile { contents, .. } => assert!(contents.is_none()),
        }
    }

    #[test]
    fn folders() {
        // a test process runs under its own throwaway profile, never the user's folder
        assert!(config_dir().starts_with(app_config_dir()));
        assert!(config_dir().ends_with(crate::profile::test_profile()));
        // and in a throwaway root of its own, not under %APPDATA% / %LOCALAPPDATA%
        assert!(config_dir().starts_with(std::env::temp_dir()) && data_dir().starts_with(std::env::temp_dir()));
        assert!(data_dir().ends_with("data") && data_dir().starts_with(test_root()));
    }

    #[test]
    fn webview_temp_and_print_names_are_per_profile() {
        let (data, temp) = (Path::new("/d"), Path::new("/t"));
        // the default profile keeps the names of 0.1.0 and 0.1.1
        assert_eq!(webview_dir_for("default", data), data.join("webview"));
        assert_eq!(temp_folder_for("default", temp), temp.join("Barbican"));
        assert_eq!(print_file_for("default", temp), temp.join("barbican-print.html"));
        // any other profile has its own, none of them inside or equal to the default's
        let w = webview_dir_for("dev", data);
        assert_eq!(w, data.join("profiles").join("dev").join("webview"));
        assert_ne!(w, webview_dir_for("default", data));
        assert_ne!(w, webview_dir_for("qa", data));
        assert!(!w.starts_with(data.join("webview")) && !data.join("webview").starts_with(&w));
        assert_eq!(temp_folder_for("dev", temp), temp.join("Barbican-dev"));
        assert!(!temp_folder_for("dev", temp).starts_with(temp.join("Barbican")));
        assert_eq!(print_file_for("dev", temp), temp.join("barbican-print-dev.html"));
        assert_ne!(print_file_for("dev", temp), print_file_for("qa", temp));
        // this process is a test profile: its own names
        assert!(webview_dir().ends_with(Path::new("profiles").join(crate::profile::test_profile()).join("webview")));
        assert!(mail_temp_dir().ends_with(format!("Barbican-{}", crate::profile::test_profile())));
    }
}
