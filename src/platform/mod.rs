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
use std::path::PathBuf;

pub mod hunspell;
#[cfg(windows)]
pub mod win;

/// Settings, sessions and caches that belong to the user (roams on Windows).
pub fn config_dir() -> PathBuf {
    #[cfg(windows)]
    {
        let base = std::env::var("APPDATA").unwrap_or_else(|_| ".".into());
        PathBuf::from(base).join("Barbican")
    }
    #[cfg(not(windows))]
    {
        xdg_dir("XDG_CONFIG_HOME", ".config").join("barbican")
    }
}

/// Large machine-local data: downloaded updates.
pub fn data_dir() -> PathBuf {
    #[cfg(windows)]
    {
        let base = std::env::var("LOCALAPPDATA").or_else(|_| std::env::var("APPDATA")).unwrap_or_else(|_| ".".into());
        PathBuf::from(base).join("Barbican")
    }
    #[cfg(not(windows))]
    {
        xdg_dir("XDG_DATA_HOME", ".local/share").join("barbican")
    }
}

#[cfg(not(windows))]
fn xdg_dir(var: &str, fallback: &str) -> PathBuf {
    match std::env::var(var) {
        Ok(v) if v.starts_with('/') => PathBuf::from(v),
        _ => PathBuf::from(std::env::var("HOME").unwrap_or_else(|_| ".".into())).join(fallback),
    }
}

/// Where secrets are kept, for setting descriptions.
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
        assert!(config_dir().ends_with(if cfg!(windows) { "Barbican" } else { "barbican" }));
        assert!(data_dir().ends_with(if cfg!(windows) { "Barbican" } else { "barbican" }));
    }
}
