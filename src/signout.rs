//! What signing out removes from this computer.
//!
//! Bulwark (lib/sign-out-cleanup.ts `SIGNED_OUT_STORAGE_KEYS`, stores/auth-store.ts
//! `performFullLogout`): once nobody is signed in, nothing the accounts produced stays behind
//! for the next person at this computer: recent searches, favourites and recent files, the
//! reminder state, sender pictures, plugin storage, offline data. Device preferences stay
//! (`settings-storage`, `theme-storage`, `locale-storage`, templates), and so does the login
//! form's list of user names (`webmail_usernames`). A sign-out that leaves other accounts
//! signed in only takes what is that account's (stores/auth-store.ts `removeAccount`).
//!
//! Barbican keeps no mail, calendar, contact or search index on disk, drafts live on the
//! server, there is no trusted-senders or remote-content allow list (the server's address
//! book holds trusted senders) and plugins keep no data of their own, so the list in
//! [`targets`] is short. The one list lives there; [`wipe`] runs it.
//!
//! Deleting never fails the sign-out. A file that is locked (the mail view's WebView2 is
//! running) is retried, and what still stays is written to `pending-delete.txt` in the config
//! folder; [`sweep_pending`] removes it at the next start, before any WebView exists.
use std::io;
use std::path::{Component, Path, PathBuf};
use std::time::Duration;

/// Where the mail view's WebView keeps its profile (cookies, cache, local storage).
pub fn webview_dir() -> PathBuf {
    crate::platform::data_dir().join("webview")
}

/// The three folders Barbican writes account data to.
#[derive(Clone, Debug)]
pub struct Roots {
    pub config: PathBuf,
    pub data: PathBuf,
    pub temp: PathBuf,
}

impl Roots {
    pub fn current() -> Roots {
        Roots { config: crate::config_dir(), data: crate::platform::data_dir(), temp: std::env::temp_dir() }
    }
    fn webview(&self) -> PathBuf { self.data.join("webview") }
    fn pending_file(&self) -> PathBuf { self.config.join("pending-delete.txt") }
}

/// Who signed out.
#[derive(Clone, Debug)]
pub struct Who {
    /// The account's name as the assistant's chat file knows it.
    pub user: String,
    /// The reminder runner's key for the account ([`crate::reminders::owner_key`]).
    pub owner: Option<String>,
    /// No other account stays signed in here: the device-wide account data goes too.
    pub last: bool,
    /// The mail view's WebView is running: its profile folder is locked, so it is emptied
    /// through the WebView (`mailview::clear_browsing_data`) and deleted at the next start.
    pub webview_live: bool,
}

/// What a sign-out removes: files and folders under [`Roots`]. Everything else stays: the
/// preferences (`prefs.json`, `settings.json`, `panes.json`), the AI provider's key, the OAuth
/// client ids per server (`oauth-clients.json`), the last server and user name for the login
/// form (`login.txt`), plugin files the user installed, downloaded updates.
pub fn targets(r: &Roots, who: &Who) -> Vec<PathBuf> {
    let c = &r.config;
    // always: the account's assistant chat, and what was opened from its mail
    let mut t = vec![c.join(crate::chat_file(&who.user))];
    t.push(r.temp.join("Barbican")); // attachments and files opened in another program
    t.push(r.temp.join("barbican-print.html")); // the last printed message
    t.push(r.webview()); // cookies, cache and local storage of the mail view
    if who.last {
        // the chats of every account, recent searches, Files favourites and recent files,
        // reminder state, sender pictures and the assistant's memory of the user
        if let Ok(rd) = std::fs::read_dir(c) {
            for e in rd.flatten() {
                let n = e.file_name().to_string_lossy().to_string();
                if n.starts_with("chat") && n.ends_with(".json") { t.push(e.path()); }
            }
        }
        for f in ["search-history.json", "files.json", crate::reminders::STATE_FILE, "memory.txt", "avatars"] {
            t.push(c.join(f));
        }
    }
    t.sort();
    t.dedup();
    t
}

/// What a wipe did.
#[derive(Debug, Default)]
pub struct Report {
    pub removed: Vec<PathBuf>,
    /// Still there (locked): removed at the next start.
    pub pending: Vec<PathBuf>,
}

/// Remove a file or a folder; already gone counts as removed.
pub fn remove_any(p: &Path) -> io::Result<()> {
    match std::fs::symlink_metadata(p) {
        Err(e) if e.kind() == io::ErrorKind::NotFound => Ok(()),
        Err(e) => Err(e),
        Ok(m) if m.is_dir() => std::fs::remove_dir_all(p),
        Ok(_) => std::fs::remove_file(p),
    }
}

fn gone(p: &Path) -> bool {
    matches!(std::fs::symlink_metadata(p), Err(e) if e.kind() == io::ErrorKind::NotFound)
}

/// Run the list for `who`. `remove` deletes one path (tests pass a failing one), tried
/// `attempts` times with `delay` between; what stays is scheduled for the next start.
pub fn wipe_with(r: &Roots, who: &Who, remove: &dyn Fn(&Path) -> io::Result<()>, attempts: u32, delay: Duration) -> Report {
    let mut report = Report::default();
    for p in targets(r, who) {
        // a live WebView profile is not touched from outside: it would corrupt what the
        // running view still has open
        if who.webview_live && p == r.webview() {
            if gone(&p) { report.removed.push(p); } else { report.pending.push(p); }
            continue;
        }
        let mut done = false;
        for i in 0..attempts.max(1) {
            if remove(&p).is_ok() || gone(&p) { done = true; break; }
            if i + 1 < attempts && !delay.is_zero() { std::thread::sleep(delay); }
        }
        if done { report.removed.push(p); } else { report.pending.push(p); }
    }
    if !who.last {
        if let Some(owner) = &who.owner { forget_owner(&r.config.join(crate::reminders::STATE_FILE), owner); }
    }
    schedule(r, &report.pending);
    report
}

/// Sign-out: wipe in the background (retries sleep), never blocking or failing the UI.
pub fn wipe(who: Who) {
    std::thread::spawn(move || { wipe_with(&Roots::current(), &who, &remove_any, 5, Duration::from_millis(300)); });
}

/// Drop one account's entries from the reminder state (other accounts keep theirs).
fn forget_owner(file: &Path, owner: &str) {
    let Some(mut v) = std::fs::read_to_string(file).ok().and_then(|t| serde_json::from_str::<serde_json::Value>(&t).ok()) else { return };
    let mut changed = false;
    for k in ["acked", "lastCheck"] {
        if let Some(o) = v.get_mut(k).and_then(|x| x.as_object_mut()) { changed |= o.remove(owner).is_some(); }
    }
    if changed { let _ = std::fs::write(file, v.to_string()); }
}

/// Everything the OS keychain holds for the account: the remembered session and its entry in
/// the account list; with the last account also the Stalwart admin key. All keychain entries
/// that sign-out deletes go through here. Returns whether no remembered account is left.
pub fn forget_keyring(user: &str) -> bool {
    crate::oauth::forget();
    crate::oauth::forget_account(user);
    let last = crate::oauth::accounts().is_empty();
    if last { crate::admin::set_key(""); }
    last
}

// ---------------- what stays locked until the next start ----------------

fn schedule(r: &Roots, paths: &[PathBuf]) {
    if paths.is_empty() { return; }
    let file = r.pending_file();
    let mut all = read_pending(&file);
    for p in paths { if !all.contains(p) { all.push(p.clone()); } }
    write_pending(&file, &all);
}

fn read_pending(file: &Path) -> Vec<PathBuf> {
    std::fs::read_to_string(file).map(|t| t.lines().filter(|l| !l.trim().is_empty()).map(PathBuf::from).collect()).unwrap_or_default()
}

fn write_pending(file: &Path, paths: &[PathBuf]) {
    if paths.is_empty() { let _ = std::fs::remove_file(file); return; }
    if let Some(d) = file.parent() { let _ = std::fs::create_dir_all(d); }
    let text: String = paths.iter().map(|p| format!("{}\n", p.to_string_lossy())).collect();
    let _ = std::fs::write(file, text);
}

/// A path the list may name: inside one of Barbican's folders, never the folder itself and
/// never through `..`, so an edited file cannot make the start delete something else.
fn allowed(r: &Roots, p: &Path) -> bool {
    if !p.is_absolute() || p.components().any(|c| matches!(c, Component::ParentDir)) { return false; }
    let inside = |root: &Path| p.starts_with(root) && p != root;
    inside(&r.config) || inside(&r.data) || inside(&r.temp.join("Barbican")) || p == r.temp.join("barbican-print.html")
}

/// At start, before any WebView exists: remove what the last sign-out could not.
pub fn sweep_pending_with(r: &Roots, remove: &dyn Fn(&Path) -> io::Result<()>) -> Report {
    let file = r.pending_file();
    let mut report = Report::default();
    for p in read_pending(&file) {
        if !allowed(r, &p) { continue; }
        if remove(&p).is_ok() || gone(&p) { report.removed.push(p); } else { report.pending.push(p); }
    }
    write_pending(&file, &report.pending);
    report
}

pub fn sweep_pending() { sweep_pending_with(&Roots::current(), &remove_any); }

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;

    struct Tmp(PathBuf);
    impl Tmp {
        fn new(tag: &str) -> Tmp {
            let d = std::env::temp_dir().join(format!("barbican-signout-{tag}-{}", std::process::id()));
            let _ = fs::remove_dir_all(&d);
            fs::create_dir_all(&d).unwrap();
            Tmp(d)
        }
        fn roots(&self) -> Roots {
            let r = Roots { config: self.0.join("config"), data: self.0.join("data"), temp: self.0.join("temp") };
            for d in [&r.config, &r.data, &r.temp] { fs::create_dir_all(d).unwrap(); }
            r
        }
    }
    impl Drop for Tmp {
        fn drop(&mut self) { let _ = fs::remove_dir_all(&self.0); }
    }

    fn put(p: &Path, text: &str) {
        fs::create_dir_all(p.parent().unwrap()).unwrap();
        fs::write(p, text).unwrap();
    }

    /// A config folder with everything Barbican writes there, for two accounts.
    fn fill(r: &Roots) {
        let c = &r.config;
        for (f, t) in [
            ("chat-ann@example.test.json", "[]"), ("chat-bob@example.test.json", "[]"), ("chat.json", "[]"),
            ("search-history.json", "[\"invoice\"]"), ("files.json", "{}"), ("memory.txt", "likes tea"),
            ("avatars/0a1b", "png"), ("avatars/2c3d", "png"),
            ("prefs.json", "{\"theme\":\"dark\"}"), ("settings.json", "{}"), ("panes.json", "{}"),
            ("oauth-clients.json", "{}"), ("login.txt", "https://mail.example.test\nann@example.test\n"),
            ("instance.json", "{}"), ("plugins/one.dll", "x"),
        ] { put(&c.join(f), t); }
        put(&c.join("calendar.json"), r#"{"acked":{"a|ann@example.test":{"k":1},"a|bob@example.test":{"k":2}},"lastCheck":{"a|ann@example.test":5,"a|bob@example.test":6}}"#);
        put(&r.data.join("webview/EBWebView/Cookies"), "c");
        put(&r.data.join("webview/EBWebView/Local Storage/x"), "l");
        put(&r.data.join("updates/pending.json"), "{}");
        put(&r.temp.join("Barbican/invoice.pdf"), "pdf");
        put(&r.temp.join("barbican-print.html"), "<p>");
        put(&r.temp.join("unrelated.txt"), "not ours");
    }

    fn who(user: &str, last: bool, live: bool) -> Who {
        Who { user: user.into(), owner: Some(format!("a|{user}")), last, webview_live: live }
    }

    fn quick(r: &Roots, w: &Who) -> Report { wipe_with(r, w, &remove_any, 1, Duration::ZERO) }

    #[test]
    fn last_account_takes_account_data_and_keeps_preferences() {
        let t = Tmp::new("last");
        let r = t.roots();
        fill(&r);
        let rep = quick(&r, &who("ann@example.test", true, false));
        assert!(rep.pending.is_empty());
        let (c, d, tmp) = (&r.config, &r.data, &r.temp);
        for gone in ["chat-ann@example.test.json", "chat-bob@example.test.json", "chat.json", "search-history.json", "files.json",
            "memory.txt", "avatars", "calendar.json"] {
            assert!(!c.join(gone).exists(), "{gone} should be gone");
        }
        assert!(!d.join("webview").exists());
        assert!(!tmp.join("Barbican").exists());
        assert!(!tmp.join("barbican-print.html").exists());
        for kept in ["prefs.json", "settings.json", "panes.json", "oauth-clients.json", "login.txt", "instance.json", "plugins/one.dll"] {
            assert!(c.join(kept).exists(), "{kept} should stay");
        }
        assert!(d.join("updates/pending.json").exists());
        assert!(tmp.join("unrelated.txt").exists());
        assert!(!r.config.join("pending-delete.txt").exists());
    }

    #[test]
    fn other_accounts_keep_their_data() {
        let t = Tmp::new("other");
        let r = t.roots();
        fill(&r);
        quick(&r, &who("ann@example.test", false, false));
        let c = &r.config;
        assert!(!c.join("chat-ann@example.test.json").exists());
        assert!(c.join("chat-bob@example.test.json").exists());
        for kept in ["chat.json", "search-history.json", "files.json", "memory.txt", "avatars/0a1b", "prefs.json", "login.txt"] {
            assert!(c.join(kept).exists(), "{kept} should stay");
        }
        // the mail view's profile and what was opened from mail are not any account's
        assert!(!r.data.join("webview").exists());
        assert!(!r.temp.join("Barbican").exists());
        // calendar.json keeps only the other account's reminder state
        let v: serde_json::Value = serde_json::from_str(&fs::read_to_string(c.join("calendar.json")).unwrap()).unwrap();
        assert!(v["acked"].get("a|ann@example.test").is_none() && v["lastCheck"].get("a|ann@example.test").is_none());
        assert_eq!(v["acked"]["a|bob@example.test"]["k"], 2);
        assert_eq!(v["lastCheck"]["a|bob@example.test"], 6);
    }

    #[test]
    fn chat_name_is_the_one_the_assistant_uses() {
        let t = Tmp::new("chatname");
        let r = t.roots();
        let w = who("ann smith/x@example.test", false, false);
        let p = r.config.join(crate::chat_file(&w.user));
        put(&p, "[]");
        assert!(targets(&r, &w).contains(&p));
        quick(&r, &w);
        assert!(!p.exists());
    }

    #[test]
    fn live_webview_profile_waits_for_the_next_start() {
        let t = Tmp::new("live");
        let r = t.roots();
        fill(&r);
        let rep = quick(&r, &who("ann@example.test", true, true));
        assert_eq!(rep.pending, vec![r.data.join("webview")]);
        assert!(r.data.join("webview/EBWebView/Cookies").exists(), "a running view's profile is left alone");
        assert!(!r.config.join("chat.json").exists());
        // next start
        let swept = sweep_pending_with(&r, &remove_any);
        assert_eq!(swept.removed, vec![r.data.join("webview")]);
        assert!(!r.data.join("webview").exists());
        assert!(!r.config.join("pending-delete.txt").exists());
    }

    #[test]
    fn a_locked_file_is_retried_then_scheduled_and_never_fails() {
        let t = Tmp::new("locked");
        let r = t.roots();
        fill(&r);
        let stuck = r.config.join("search-history.json");
        let tries = std::cell::Cell::new(0);
        let remove = |p: &Path| -> io::Result<()> {
            if p == stuck { tries.set(tries.get() + 1); return Err(io::Error::new(io::ErrorKind::PermissionDenied, "in use")); }
            remove_any(p)
        };
        let rep = wipe_with(&r, &who("ann@example.test", true, false), &remove, 3, Duration::ZERO);
        assert_eq!(tries.get(), 3);
        assert_eq!(rep.pending, vec![stuck.clone()]);
        assert!(stuck.exists() && !r.config.join("files.json").exists(), "the rest still went");
        assert_eq!(fs::read_to_string(r.config.join("pending-delete.txt")).unwrap().trim(), stuck.to_string_lossy());
        // the next start, still locked: stays scheduled; then free: gone
        let swept = sweep_pending_with(&r, &|_| Err(io::Error::new(io::ErrorKind::PermissionDenied, "in use")));
        assert_eq!(swept.pending, vec![stuck.clone()]);
        let swept = sweep_pending_with(&r, &remove_any);
        assert_eq!(swept.removed, vec![stuck.clone()]);
        assert!(!stuck.exists() && !r.config.join("pending-delete.txt").exists());
    }

    #[cfg(windows)]
    #[test]
    fn a_really_locked_file_survives_until_released() {
        use std::os::windows::fs::OpenOptionsExt;
        let t = Tmp::new("winlock");
        let r = t.roots();
        let f = r.config.join("search-history.json");
        put(&f, "[]");
        let held = fs::OpenOptions::new().read(true).share_mode(0).open(&f).unwrap();
        let rep = wipe_with(&r, &who("ann@example.test", true, false), &remove_any, 2, Duration::from_millis(10));
        assert_eq!(rep.pending, vec![f.clone()]);
        assert!(f.exists());
        drop(held);
        sweep_pending_with(&r, &remove_any);
        assert!(!f.exists());
    }

    #[test]
    fn missing_things_are_not_a_problem() {
        let t = Tmp::new("empty");
        let r = t.roots();
        let rep = quick(&r, &who("ann@example.test", true, false));
        assert!(rep.pending.is_empty());
        assert!(!r.config.join("pending-delete.txt").exists());
    }

    #[test]
    fn the_start_only_deletes_inside_barbicans_folders() {
        let t = Tmp::new("sweep");
        let r = t.roots();
        let outside = t.0.join("precious.txt");
        put(&outside, "keep");
        let inside = r.data.join("webview");
        put(&inside.join("x"), "x");
        let print = r.temp.join("barbican-print.html");
        put(&print, "x");
        let other_temp = r.temp.join("other.txt");
        put(&other_temp, "keep");
        let list = [
            outside.clone(), r.config.clone(), r.data.clone(), r.temp.clone(), other_temp.clone(),
            r.config.join("..").join("precious.txt"), PathBuf::from("relative.txt"), inside.clone(), print.clone(),
        ];
        let text: String = list.iter().map(|p| format!("{}\n", p.to_string_lossy())).collect();
        put(&r.config.join("pending-delete.txt"), &text);
        sweep_pending_with(&r, &remove_any);
        assert!(outside.exists() && r.config.exists() && r.data.exists() && r.temp.exists() && other_temp.exists());
        assert!(!inside.exists() && !print.exists());
    }
}
