//! mailto: links (GAPS P0-8), after Bulwark lib/protocol-handlers/mailto.ts:
//! - RFC 6068 parsing with lenient percent-decoding ('+' stays '+'), quote-aware recipient
//!   splitting, control characters stripped, recipient/subject/body limits;
//! - links clicked in a message open Barbican's composer instead of the OS handler;
//! - links handed over by the OS (Barbican as the default mail app) arrive through
//!   `instance.rs` and open the composer the same way;
//! - registration as the default mail app: Windows (HKCU, user-triggered from Settings;
//!   Windows then asks the user to confirm in Default apps) and Linux (.desktop with
//!   MimeType=x-scheme-handler/mailto). Never run by tests: they check the plan only.
use crate::AppWindow;
use slint::ComponentHandle;
use std::cell::RefCell;

#[derive(Debug, Default, PartialEq, Clone)]
pub struct Mailto {
    pub to: Vec<String>,
    pub cc: Vec<String>,
    pub bcc: Vec<String>,
    pub subject: String,
    pub body: String,
}

const MAX_RECIPIENTS: usize = 200;
const MAX_SUBJECT: usize = 998;
const MAX_BODY: usize = 64 * 1024;

fn hex(b: u8) -> Option<u8> {
    match b { b'0'..=b'9' => Some(b - b'0'), b'a'..=b'f' => Some(b - b'a' + 10), b'A'..=b'F' => Some(b - b'A' + 10), _ => None }
}

/// Percent-decoding that never gives up: valid escapes are decoded, a stray '%' stays, bytes
/// that are not UTF-8 become U+FFFD. '+' is not a space (RFC 6068 is not form encoding).
pub fn decode_lenient(v: &str) -> String {
    let b = v.as_bytes();
    let mut out: Vec<u8> = Vec::with_capacity(b.len());
    let mut i = 0;
    while i < b.len() {
        if b[i] == b'%' {
            if let (Some(h), Some(l)) = (b.get(i + 1).and_then(|x| hex(*x)), b.get(i + 2).and_then(|x| hex(*x))) {
                out.push(h * 16 + l);
                i += 3;
                continue;
            }
        }
        out.push(b[i]);
        i += 1;
    }
    String::from_utf8_lossy(&out).into_owned()
}

fn strip_control(v: &str) -> String { v.chars().filter(|c| !c.is_control()).collect() }

fn clean_body(v: &str) -> String {
    let v = v.replace("\r\n", "\n").replace('\r', "\n");
    let s: String = v.chars().filter(|c| *c == '\n' || *c == '\t' || !c.is_control()).collect();
    s.chars().take(MAX_BODY).collect()
}

fn split(v: &str) -> Vec<String> { crate::recipients::split_recipients(&strip_control(v), ",") }

/// Parse a mailto: URL; None when it is not one.
pub fn parse(raw: &str) -> Option<Mailto> {
    let raw = raw.trim();
    if raw.len() < 7 || !raw[..7].eq_ignore_ascii_case("mailto:") { return None; }
    let rest = &raw[7..];
    let (path, query) = rest.split_once('?').unwrap_or((rest, ""));
    let params: Vec<(String, String)> = query.split('&').filter(|p| !p.is_empty()).map(|p| {
        let (k, v) = p.split_once('=').unwrap_or((p, ""));
        (decode_lenient(k), decode_lenient(v))
    }).collect();
    let get = |key: &str| params.iter().filter(|(k, _)| k.eq_ignore_ascii_case(key)).map(|(_, v)| v.as_str()).collect::<Vec<_>>().join(",");
    let mut to = split(&decode_lenient(path));
    to.extend(split(&get("to")));
    to.truncate(MAX_RECIPIENTS);
    let mut cc = split(&get("cc"));
    cc.truncate(MAX_RECIPIENTS - to.len());
    let mut bcc = split(&get("bcc"));
    bcc.truncate(MAX_RECIPIENTS - to.len() - cc.len());
    Some(Mailto {
        to, cc, bcc,
        subject: strip_control(&get("subject")).chars().take(MAX_SUBJECT).collect(),
        body: clean_body(&get("body")),
    })
}

// ---------------------------------------------------------------- opening the composer

type Handler = Box<dyn Fn(&AppWindow, &Mailto) -> bool>;

thread_local! {
    static WINDOW: RefCell<Option<slint::Weak<AppWindow>>> = const { RefCell::new(None) };
    /// opens the composer for a link; false while nobody is signed in
    static HANDLER: RefCell<Option<Handler>> = const { RefCell::new(None) };
    static PENDING: RefCell<Vec<Mailto>> = const { RefCell::new(vec![]) };
}

/// Install the composer opener (main.rs, which owns the session state).
pub fn install(ui: &AppWindow, open: Handler) {
    WINDOW.with(|w| *w.borrow_mut() = Some(ui.as_weak()));
    HANDLER.with(|h| *h.borrow_mut() = Some(open));
}

/// A mailto: URL to open, from any thread (a click in the email view, another instance).
pub fn request(url: String) {
    let _ = slint::invoke_from_event_loop(move || dispatch(&url));
}

fn dispatch(url: &str) {
    let Some(m) = parse(url) else { return };
    let Some(u) = WINDOW.with(|w| w.borrow().as_ref().and_then(|w| w.upgrade())) else { return };
    if !try_open(&u, &m) {
        // not signed in yet (the session is still being restored): try again for a minute
        PENDING.with(|p| p.borrow_mut().push(m));
        retry(0);
    }
}

fn try_open(u: &AppWindow, m: &Mailto) -> bool {
    HANDLER.with(|h| h.borrow().as_ref().is_some_and(|f| f(u, m)))
}

fn retry(n: u32) {
    if n > 60 { PENDING.with(|p| p.borrow_mut().clear()); return; }
    slint::Timer::single_shot(std::time::Duration::from_secs(1), move || {
        let Some(u) = WINDOW.with(|w| w.borrow().as_ref().and_then(|w| w.upgrade())) else { return };
        let Some(m) = PENDING.with(|p| p.borrow().first().cloned()) else { return };
        if try_open(&u, &m) { PENDING.with(|p| { p.borrow_mut().remove(0); }); if PENDING.with(|p| p.borrow().is_empty()) { return; } }
        retry(n + 1);
    });
}

/// Fill a freshly opened composer from a link (after compose::open New).
pub fn fill(u: &AppWindow, m: &Mailto) {
    if !m.to.is_empty() { u.set_c_to(m.to.join(", ").into()); }
    if !m.cc.is_empty() { u.set_c_cc(m.cc.join(", ").into()); u.set_c_show_cc(true); u.set_c_show_ccbcc(true); }
    if !m.bcc.is_empty() { u.set_c_bcc(m.bcc.join(", ").into()); u.set_c_show_bcc(true); u.set_c_show_ccbcc(true); }
    if !m.subject.is_empty() { u.set_c_subject(m.subject.as_str().into()); }
    if !m.body.is_empty() {
        crate::rte::set_doc(u, crate::rte::model::Doc { blocks: crate::rte::html::import_text(&m.body) });
    }
    // handed-in text is not the user's edit: no draft until they change something
    crate::autosave::rebase(u);
    // bring the window forward for a link that came from another program
    if !crate::automation::enabled() {
        u.window().show().ok();
        use slint::winit_030::WinitWindowAccessor;
        u.window().with_winit_window(|w| { w.set_minimized(false); w.focus_window(); });
    }
}

// ---------------------------------------------------------------- default mail app

pub const PROG_ID: &str = "Barbican.Url.mailto";

/// HKCU registry values that register Barbican as a mail client for mailto: links
/// (key, value name or "" for the default value, data). Windows 10/11 then lets the user
/// pick it in Settings > Default apps; the user choice itself cannot be set by a program.
pub fn windows_plan(exe: &str) -> Vec<(String, String, String)> {
    let cmd = format!("\"{exe}\" \"%1\"");
    let base = r"HKCU\Software\Clients\Mail\Barbican".to_string();
    let class = format!(r"HKCU\Software\Classes\{PROG_ID}");
    vec![
        (class.clone(), "".into(), "Barbican mailto link".into()),
        (class.clone(), "URL Protocol".into(), "".into()),
        (format!(r"{class}\DefaultIcon"), "".into(), format!("\"{exe}\",0")),
        (format!(r"{class}\shell\open\command"), "".into(), cmd.clone()),
        (base.clone(), "".into(), "Barbican".into()),
        (format!(r"{base}\Capabilities"), "ApplicationName".into(), "Barbican".into()),
        (format!(r"{base}\Capabilities"), "ApplicationDescription".into(), "Mail, calendar and contacts for Stalwart".into()),
        (format!(r"{base}\Capabilities\URLAssociations"), "mailto".into(), PROG_ID.into()),
        (format!(r"{base}\shell\open\command"), "".into(), format!("\"{exe}\"")),
        (r"HKCU\Software\RegisteredApplications".into(), "Barbican".into(), r"Software\Clients\Mail\Barbican\Capabilities".into()),
    ]
}

/// Quote a path for a .desktop Exec line (Desktop Entry spec: reserved characters escaped
/// inside double quotes).
fn desktop_quote(p: &str) -> String {
    let mut s = String::from("\"");
    for c in p.chars() {
        if matches!(c, '"' | '`' | '$' | '\\') { s.push('\\'); }
        s.push(c);
    }
    s.push('"');
    s
}

/// The barbican.desktop file that makes Barbican a handler for mailto: links on Linux.
pub fn desktop_entry(exe: &str) -> String {
    format!("[Desktop Entry]\nType=Application\nName=Barbican\nComment=Mail, calendar and contacts for Stalwart\nExec={} %u\nIcon=barbican\nTerminal=false\nCategories=Network;Email;\nMimeType=x-scheme-handler/mailto;\nStartupNotify=true\n", desktop_quote(exe))
}

/// Register Barbican as the default mail app (Settings button). Returns what to tell the user.
pub fn register_default() -> Result<String, String> {
    let exe = std::env::current_exe().map_err(|e| e.to_string())?.to_string_lossy().to_string();
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        for (key, name, data) in windows_plan(&exe) {
            let mut c = std::process::Command::new("reg");
            c.arg("add").arg(&key);
            if name.is_empty() { c.arg("/ve"); } else { c.args(["/v", &name]); }
            let out = c.args(["/t", "REG_SZ", "/d", &data, "/f"]).creation_flags(0x0800_0000).output().map_err(|e| e.to_string())?;
            if !out.status.success() { return Err(format!("Could not write {key}: {}", String::from_utf8_lossy(&out.stderr).trim())); }
        }
        // Windows asks the user to confirm the default themselves
        crate::oauth::open_browser("ms-settings:defaultapps?registeredAppUser=Barbican");
        Ok("Barbican is registered for mailto: links. Choose it as the email app in the Windows settings that just opened.".into())
    }
    #[cfg(not(windows))]
    {
        let base = std::env::var("XDG_DATA_HOME").map(std::path::PathBuf::from)
            .or_else(|_| std::env::var("HOME").map(|h| std::path::Path::new(&h).join(".local/share")))
            .map_err(|_| "No home folder.".to_string())?;
        let dir = base.join("applications");
        std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
        std::fs::write(dir.join("barbican.desktop"), desktop_entry(&exe)).map_err(|e| e.to_string())?;
        let _ = std::process::Command::new("update-desktop-database").arg(&dir).status();
        let ok = std::process::Command::new("xdg-mime").args(["default", "barbican.desktop", "x-scheme-handler/mailto"]).status().is_ok_and(|s| s.success());
        if ok { Ok("Barbican now opens mailto: links.".into()) } else { Err("barbican.desktop was written, but xdg-mime could not set it as the default.".into()) }
    }
}

/// Whether mailto: links open Barbican now (read-only check of the user's choice).
pub fn is_default() -> bool {
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        std::process::Command::new("reg")
            .args(["query", r"HKCU\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\mailto\UserChoice", "/v", "ProgId"])
            .creation_flags(0x0800_0000).output().ok()
            .is_some_and(|o| String::from_utf8_lossy(&o.stdout).contains(PROG_ID))
    }
    #[cfg(not(windows))]
    {
        std::process::Command::new("xdg-mime").args(["query", "default", "x-scheme-handler/mailto"]).output().ok()
            .is_some_and(|o| String::from_utf8_lossy(&o.stdout).trim() == "barbican.desktop")
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parse_rfc6068() {
        let m = parse("mailto:a@x.io,%22Doe,%20J%22%20%3Cj@d.o%3E?cc=c@x.io&CC=d@x.io&bcc=e@x.io&subject=Hi%20there+you&body=Line%201%0D%0ALine%202%07").unwrap();
        assert_eq!(m.to, vec!["a@x.io", "\"Doe, J\" <j@d.o>"]);
        assert_eq!(m.cc, vec!["c@x.io", "d@x.io"]);
        assert_eq!(m.bcc, vec!["e@x.io"]);
        assert_eq!(m.subject, "Hi there+you");
        assert_eq!(m.body, "Line 1\nLine 2");
        assert_eq!(parse("MAILTO:?to=z@x.io").unwrap().to, vec!["z@x.io"]);
        assert_eq!(parse("http://x"), None);
        // a stray % and broken UTF-8 do not lose the link
        assert_eq!(parse("mailto:a@x.io?subject=50%25%20off%2").unwrap().subject, "50% off%2");
        assert_eq!(parse("mailto:a@x.io?subject=%FF").unwrap().subject, "\u{FFFD}");
        // control characters in the subject are dropped
        assert_eq!(parse("mailto:a@x.io?subject=a%0Ab").unwrap().subject, "ab");
    }

    #[test]
    fn limits() {
        let many = (0..250).map(|i| format!("u{i}@x.io")).collect::<Vec<_>>().join(",");
        let m = parse(&format!("mailto:{many}?cc=c@x.io")).unwrap();
        assert_eq!(m.to.len(), 200);
        assert!(m.cc.is_empty());
    }

    #[test]
    fn registration_plans() {
        let plan = windows_plan(r"C:\Apps\Barbican\barbican.exe");
        let get = |k: &str, n: &str| plan.iter().find(|(a, b, _)| a == k && b == n).map(|x| x.2.clone());
        assert_eq!(get(r"HKCU\Software\Classes\Barbican.Url.mailto\shell\open\command", ""), Some(r#""C:\Apps\Barbican\barbican.exe" "%1""#.into()));
        assert_eq!(get(r"HKCU\Software\Classes\Barbican.Url.mailto", "URL Protocol"), Some("".into()));
        assert_eq!(get(r"HKCU\Software\Clients\Mail\Barbican\Capabilities\URLAssociations", "mailto"), Some(PROG_ID.into()));
        assert_eq!(get(r"HKCU\Software\RegisteredApplications", "Barbican"), Some(r"Software\Clients\Mail\Barbican\Capabilities".into()));
        // everything stays in the user's own hive
        assert!(plan.iter().all(|(k, _, _)| k.starts_with(r"HKCU\Software\")));
        let d = desktop_entry("/opt/bar bican/$bin");
        assert!(d.contains("MimeType=x-scheme-handler/mailto;\n"));
        assert!(d.contains("Exec=\"/opt/bar bican/\\$bin\" %u\n"));
    }
}
