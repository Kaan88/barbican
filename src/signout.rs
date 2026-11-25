//! What signing out removes from this computer.
//!
//! Bulwark (lib/sign-out-cleanup.ts `SIGNED_OUT_STORAGE_KEYS`, stores/auth-store.ts
//! `performFullLogout`): once nobody is signed in, nothing the accounts produced stays behind
//! for the next person at this computer: recent searches, favourites and recent files, the
//! reminder state, sender pictures, offline data. Device preferences stay
//! (`settings-storage`, `theme-storage`, `locale-storage`, templates), and so does the login
//! form's list of user names (`webmail_usernames`). A sign-out that leaves other accounts
//! signed in only takes what is that account's (stores/auth-store.ts `removeAccount`).
//!
//! Barbican keeps no mail, calendar, contact or search index on disk and drafts live on the
//! server, so the list in [`targets`] is short. The one thing inside a kept file that goes is the trusted-senders list
//! in `prefs.json` (`trustedSenders`, [`crate::settings::forget_trusted_senders`], called by the sign-out handler before the wipe starts), when the last account signs out. The one list lives there; [`wipe`] runs it.
//!
//! Everything that list names belongs to the signing-out profile ([`Roots`]): the config folder
//! (per profile), the mail view's WebView folder, the folder mail attachments are opened from
//! and the print file each have a name per profile (`platform::webview_dir_for` and friends), so a
//! profile's sign-out never lists, deletes or sweeps another profile's data, and a non-default
//! profile never touches the default profile's folders or the `<exe>.WebView2` folder 0.1.0 left.
//!
//! Deleting never fails the sign-out. A file that is locked (the mail view's WebView2 is
//! running) is retried, and what still stays is written to `pending-delete.txt` in the config
//! folder; [`sweep_pending`] removes it at the next start, before any WebView exists.
use std::io;
use std::path::{Component, Path, PathBuf};
use std::time::Duration;

/// The folders one profile writes account data to.
#[derive(Clone, Debug)]
pub struct Roots {
    /// The profile's config folder.
    pub config: PathBuf,
    /// The mail view's WebView profile (cookies, cache, local storage).
    pub webview: PathBuf,
    /// The folder attachments and files opened in another program are written to.
    pub temp: PathBuf,
    /// The file the last printed message is written to.
    pub print: PathBuf,
    /// The WebView profile 0.1.0 left next to the program (`<exe>.WebView2`), from before the
    /// profile moved to the data folder. Only the default profile has one.
    pub legacy_webview: Option<PathBuf>,
    /// Folders inside `config` that are other profiles' (the default profile's config folder
    /// holds `profiles/<name>`): never listed, never swept.
    pub other_profiles: Vec<PathBuf>,
}

impl Roots {
    /// The folders of `profile`: `config` is its config folder, `data` and `sys_temp` the
    /// machine-wide data and temp folders, `exe` this program's path.
    pub fn for_profile(profile: &str, config: &Path, data: &Path, sys_temp: &Path, exe: Option<&Path>) -> Roots {
        let default = crate::profile::is_default(profile);
        let legacy = exe.filter(|_| default).and_then(|exe| {
            let name = exe.file_name()?.to_string_lossy().to_string();
            Some(exe.parent()?.join(format!("{name}.WebView2")))
        });
        Roots {
            config: config.to_path_buf(),
            webview: crate::platform::webview_dir_for(profile, data),
            temp: crate::platform::temp_folder_for(profile, sys_temp),
            print: crate::platform::print_file_for(profile, sys_temp),
            legacy_webview: legacy,
            other_profiles: if default { vec![config.join("profiles")] } else { vec![] },
        }
    }
    /// This process's profile.
    pub fn current() -> Roots {
        Roots::for_profile(crate::profile::current(), &crate::config_dir(), &crate::platform::data_dir(),
            &std::env::temp_dir(), std::env::current_exe().ok().as_deref())
    }
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
/// form (`login.txt`), downloaded updates.
pub fn targets(r: &Roots, who: &Who) -> Vec<PathBuf> {
    let c = &r.config;
    // always: the account's assistant chat, and what was opened from its mail
    let mut t = vec![c.join(crate::chat_file(&who.user))];
    t.push(r.temp.clone()); // attachments and files opened in another program
    t.push(r.print.clone()); // the last printed message
    t.push(r.webview.clone()); // cookies, cache and local storage of the mail view
    t.extend(r.legacy_webview.clone()); // the same, where 0.1.0 kept it
    if who.last {
        // the chats of every account, recent searches, Files favourites and recent files,
        // reminder state, sender pictures and the assistant's memory of the user
        if let Ok(rd) = std::fs::read_dir(c) {
            for e in rd.flatten() {
                let n = e.file_name().to_string_lossy().to_string();
                if n.starts_with("chat") && n.ends_with(".json") { t.push(e.path()); }
            }
        }
        for f in ["search-history.json", "files.json", crate::reminders::STATE_FILE, "memory.txt", "avatars", crate::notices::FILE] {
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
    let list = targets(r, who);
    // written first: when the program quits while a locked file is still being retried, the
    // next start finishes the list
    schedule(r, &list);
    for p in list {
        // a live WebView profile is not touched from outside: it would corrupt what the
        // running view still has open
        if who.webview_live && p == r.webview {
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
    unschedule(r, &report.removed);
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

/// What [`forget_keyring`] found.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Forgot {
    /// No remembered account is left.
    pub last: bool,
    /// Nothing of the account is left in the keychain. False: the credential could not be
    /// removed, and the sign-out must not claim it was.
    pub cleared: bool,
}

/// Everything the OS keychain holds for the account: its entry in the account list and, when
/// it is that account's, the remembered session (both under one acquisition of the refresh
/// lock, `oauth::forget_account`); with the last account also the Stalwart admin key. All
/// keychain entries that sign-out deletes go through here.
/// `names`: how the account is known (the login form's name and the server's); `server`: the
/// one it signed in to (empty when unknown: then the name alone decides).
pub fn forget_keyring(names: &[&str], server: &str) -> Forgot {
    // Whether other accounts stay is decided from the list as it was, not from the store after the
    // sign-out: when the shortened list cannot be written the whole entry is deleted, and the
    // other accounts' local data (chats, avatars, the admin key) must not be wiped for that.
    let others = !crate::oauth::without_account(crate::oauth::accounts(), names, server).is_empty();
    let cleared = crate::oauth::forget_account(names, server);
    let last = !others;
    if last { crate::admin::set_key(""); }
    Forgot { last, cleared }
}

// ---------------- what stays locked until the next start ----------------

/// Wipes can overlap (a sign-out, a quick sign-in, a second sign-out while the first still
/// retries): the pending list is read, changed and rewritten under this lock so one wipe
/// cannot drop the other's entries.
static PENDING: std::sync::Mutex<()> = std::sync::Mutex::new(());

fn pending_lock() -> std::sync::MutexGuard<'static, ()> {
    PENDING.lock().unwrap_or_else(|e| e.into_inner())
}

fn schedule(r: &Roots, paths: &[PathBuf]) {
    if paths.is_empty() { return; }
    let _lock = pending_lock();
    let file = r.pending_file();
    let mut all = read_pending(&file);
    for p in paths { if !all.contains(p) { all.push(p.clone()); } }
    write_pending(&file, &all);
}

fn unschedule(r: &Roots, done: &[PathBuf]) {
    let _lock = pending_lock();
    let file = r.pending_file();
    let mut all = read_pending(&file);
    all.retain(|p| !done.contains(p));
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

/// A path the list may name: inside this profile's own folders (its config folder, never the
/// folder itself and never another profile's below it; its WebView, temp and print paths; the
/// folder 0.1.0 left, default profile only), never through `..`, so an edited file cannot
/// make the start delete something else, or another profile's data.
fn allowed(r: &Roots, p: &Path) -> bool {
    if !p.is_absolute() || p.components().any(|c| matches!(c, Component::ParentDir)) { return false; }
    if r.other_profiles.iter().any(|o| p.starts_with(o)) { return false; }
    let inside = |root: &Path| p.starts_with(root) && p != root;
    inside(&r.config) || p.starts_with(&r.webview) || p.starts_with(&r.temp) || p == r.print
        || r.legacy_webview.as_deref() == Some(p)
}

/// At start, before any WebView exists: remove what the last sign-out could not.
pub fn sweep_pending_with(r: &Roots, remove: &dyn Fn(&Path) -> io::Result<()>) -> Report {
    let _lock = pending_lock();
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
        fn roots(&self) -> Roots { self.roots_for("default") }
        /// The folders of one profile, as the program lays them out: every profile's config
        /// folder below the default one, the system's data and temp folders shared.
        fn roots_for(&self, profile: &str) -> Roots {
            let config = match crate::profile::subdir(profile) {
                Some(sub) => self.0.join("config").join(sub),
                None => self.0.join("config"),
            };
            let exe = self.0.join("app/barbican.exe");
            let r = Roots::for_profile(profile, &config, &self.0.join("data"), &self.0.join("temp"), Some(&exe));
            fs::create_dir_all(&r.config).unwrap();
            fs::create_dir_all(self.0.join("data")).unwrap();
            fs::create_dir_all(self.0.join("temp")).unwrap();
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
            ("instance.json", "{}"),
        ] { put(&c.join(f), t); }
        put(&c.join("calendar.json"), r#"{"acked":{"a|ann@example.test":{"k":1},"a|bob@example.test":{"k":2}},"lastCheck":{"a|ann@example.test":5,"a|bob@example.test":6}}"#);
        put(&r.webview.join("EBWebView/Cookies"), "c");
        put(&r.webview.join("EBWebView/Local Storage/x"), "l");
        put(&r.webview.parent().unwrap().join("updates/pending.json"), "{}");
        if let Some(l) = &r.legacy_webview { put(&l.join("EBWebView/Cookies"), "c"); }
        put(&r.temp.join("invoice.pdf"), "pdf");
        put(&r.print, "<p>");
        put(&r.temp.parent().unwrap().join("unrelated.txt"), "not ours");
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
        let c = &r.config;
        for gone in ["chat-ann@example.test.json", "chat-bob@example.test.json", "chat.json", "search-history.json", "files.json",
            "memory.txt", "avatars", "calendar.json"] {
            assert!(!c.join(gone).exists(), "{gone} should be gone");
        }
        assert!(!r.webview.exists());
        assert!(!r.legacy_webview.clone().unwrap().exists(), "the profile 0.1.0 left next to the program");
        assert!(!r.temp.exists());
        assert!(!r.print.exists());
        for kept in ["prefs.json", "settings.json", "panes.json", "oauth-clients.json", "login.txt", "instance.json"] {
            assert!(c.join(kept).exists(), "{kept} should stay");
        }
        assert!(r.webview.parent().unwrap().join("updates/pending.json").exists(), "downloaded updates are shared and stay");
        assert!(r.temp.parent().unwrap().join("unrelated.txt").exists());
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
        assert!(!r.webview.exists());
        assert!(!r.temp.exists());
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
        assert_eq!(rep.pending, vec![r.webview.clone()]);
        assert!(r.webview.join("EBWebView/Cookies").exists(), "a running view's profile is left alone");
        assert!(!r.config.join("chat.json").exists());
        // next start
        let swept = sweep_pending_with(&r, &remove_any);
        assert_eq!(swept.removed, vec![r.webview.clone()]);
        assert!(!r.webview.exists());
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
    fn the_list_is_on_disk_before_anything_is_deleted() {
        let t = Tmp::new("early");
        let r = t.roots();
        fill(&r);
        let w = who("ann@example.test", true, false);
        let all = targets(&r, &w);
        // the program quits during the first deletion: the next start must have the whole list
        let seen = std::cell::RefCell::new(Vec::new());
        let remove = |p: &Path| -> io::Result<()> {
            if seen.borrow().is_empty() { *seen.borrow_mut() = read_pending(&r.pending_file()); }
            remove_any(p)
        };
        wipe_with(&r, &w, &remove, 1, Duration::ZERO);
        assert_eq!(*seen.borrow(), all);
        // and a start after such a quit finishes the job
        write_pending(&r.pending_file(), &all);
        sweep_pending_with(&r, &remove_any);
        assert!(!r.config.join("chat.json").exists() && !r.webview.exists());
        assert!(!r.config.join("pending-delete.txt").exists());
    }

    #[test]
    fn the_legacy_profile_is_swept_at_start_too() {
        let t = Tmp::new("legacy");
        let r = t.roots();
        let old = r.legacy_webview.clone().unwrap();
        put(&old.join("EBWebView/Cookies"), "c");
        put(&r.config.join("pending-delete.txt"), &format!("{}
", old.to_string_lossy()));
        sweep_pending_with(&r, &remove_any);
        assert!(!old.exists());
        // the folder next to the exe is not a general license: its parent is not deletable
        let beside = old.parent().unwrap().join("barbican.exe");
        put(&beside, "exe");
        put(&r.config.join("pending-delete.txt"), &format!("{}
", beside.to_string_lossy()));
        sweep_pending_with(&r, &remove_any);
        assert!(beside.exists());
    }

    #[test]
    fn the_account_is_found_by_either_name_without_regard_to_case() {
        let list = vec![
            serde_json::json!({"user": "Ann@Example.test", "server": "https://mail.example.test"}),
            serde_json::json!({"user": "bob@example.test", "server": "https://mail.example.test"}),
        ];
        // the server reports another form of the name than the one typed
        let left = crate::oauth::without_account(list.clone(), &["Ann@Example.test", "ann"], "");
        assert_eq!(left.len(), 1);
        assert_eq!(left[0]["user"], "bob@example.test");
        let left = crate::oauth::without_account(list.clone(), &["", "ann@example.test"], "");
        assert_eq!(left.len(), 1);
        // an empty name matches nothing
        assert_eq!(crate::oauth::without_account(list, &["", ""], "").len(), 2);
    }

    #[test]
    fn the_same_name_on_another_server_stays() {
        let list = vec![
            serde_json::json!({"user": "admin@example.test", "server": "https://a.example.test"}),
            serde_json::json!({"user": "admin@example.test", "server": "https://b.example.test/"}),
        ];
        let left = crate::oauth::without_account(list.clone(), &["admin@example.test"], "https://a.example.test");
        assert_eq!(left.len(), 1);
        assert_eq!(left[0]["server"], "https://b.example.test/");
        // the server as typed on the login form (no scheme, trailing slash, other case)
        let left = crate::oauth::without_account(list.clone(), &["admin@example.test"], "B.example.test/");
        assert_eq!(left.len(), 1);
        assert_eq!(left[0]["server"], "https://a.example.test");
        // server unknown: the name alone decides
        assert!(crate::oauth::without_account(list, &["admin@example.test"], "").is_empty());
    }

    #[test]
    fn overlapping_wipes_keep_each_others_pending_entries() {
        let t = Tmp::new("overlap");
        let r = t.roots();
        let a = r.config.join("a.json");
        let b = r.config.join("b.json");
        schedule(&r, &[a.clone()]);
        schedule(&r, &[b.clone()]);
        unschedule(&r, &[a.clone()]);
        assert_eq!(read_pending(&r.pending_file()), vec![b.clone()]);
        // many threads scheduling and unscheduling at once lose nothing
        let extra: Vec<PathBuf> = (0..16).map(|i| r.config.join(format!("c{i}.json"))).collect();
        std::thread::scope(|s| {
            for p in &extra {
                s.spawn(|| { schedule(&r, &[p.clone()]); });
            }
            s.spawn(|| { for _ in 0..16 { unschedule(&r, &[a.clone()]); } });
        });
        let left = read_pending(&r.pending_file());
        for p in extra.iter().chain([&b]) { assert!(left.contains(p), "{p:?} was lost"); }
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
        let inside = r.webview.clone();
        put(&inside.join("x"), "x");
        let print = r.print.clone();
        put(&print, "x");
        let sys_temp = r.temp.parent().unwrap().to_path_buf();
        let other_temp = sys_temp.join("other.txt");
        put(&other_temp, "keep");
        let data = r.webview.parent().unwrap().to_path_buf();
        put(&data.join("updates/pending.json"), "{}");
        let list = [
            outside.clone(), r.config.clone(), data.clone(), sys_temp.clone(), other_temp.clone(), data.join("updates"),
            r.config.join("..").join("precious.txt"), PathBuf::from("relative.txt"), inside.clone(), print.clone(),
        ];
        let text: String = list.iter().map(|p| format!("{}\n", p.to_string_lossy())).collect();
        put(&r.config.join("pending-delete.txt"), &text);
        sweep_pending_with(&r, &remove_any);
        assert!(outside.exists() && r.config.exists() && data.exists() && sys_temp.exists() && other_temp.exists());
        assert!(data.join("updates/pending.json").exists(), "downloaded updates are shared, never swept");
        assert!(!inside.exists() && !print.exists());
    }

    /// Two profiles side by side, each with its own data in every place sign-out deletes from.
    fn two_profiles(t: &Tmp) -> (Roots, Roots) {
        let (d, o) = (t.roots_for("default"), t.roots_for("dev"));
        for r in [&d, &o] {
            put(&r.config.join("chat-ann@example.test.json"), "[]");
            put(&r.config.join("chat.json"), "[]");
            put(&r.config.join("search-history.json"), "[]");
            put(&r.config.join("avatars/0a1b"), "png");
            put(&r.webview.join("EBWebView/Cookies"), "c");
            put(&r.temp.join("invoice.pdf"), "pdf");
            put(&r.print, "<p>");
        }
        put(&d.legacy_webview.clone().unwrap().join("EBWebView/Cookies"), "c");
        (d, o)
    }

    fn untouched(r: &Roots) -> bool {
        r.config.join("chat-ann@example.test.json").exists() && r.config.join("chat.json").exists()
            && r.config.join("search-history.json").exists() && r.config.join("avatars/0a1b").exists()
            && r.webview.join("EBWebView/Cookies").exists() && r.temp.join("invoice.pdf").exists() && r.print.exists()
            && r.legacy_webview.as_ref().is_none_or(|l| l.join("EBWebView/Cookies").exists())
    }

    #[test]
    fn a_non_default_profile_never_lists_or_sweeps_the_default_ones_paths() {
        let t = Tmp::new("prof-other");
        let (d, o) = two_profiles(&t);
        assert!(o.legacy_webview.is_none(), "the folder 0.1.0 left belongs to the default profile");
        // nothing the other profile's list names is inside, or equal to, one of the default's
        let all = targets(&o, &who("ann@example.test", true, false));
        for p in &all {
            for root in [&d.webview, &d.temp, &d.print] { assert!(!p.starts_with(root), "{p:?} is the default profile's"); }
            assert!(!d.legacy_webview.iter().any(|l| p.starts_with(l)), "{p:?} is the 0.1.0 folder");
            assert!(p != &d.config.join("chat.json"), "{p:?}");
        }
        assert!(all.contains(&o.webview) && all.contains(&o.temp) && all.contains(&o.print));
        quick(&o, &who("ann@example.test", true, false));
        assert!(untouched(&d), "the default profile's data is all still there");
        for gone in [&o.webview, &o.temp, &o.print] { assert!(!gone.exists()); }
        assert!(!o.config.join("chat.json").exists() && !o.config.join("avatars").exists());
    }

    #[test]
    fn the_default_profile_never_touches_another_profiles_data() {
        let t = Tmp::new("prof-default");
        let (d, o) = two_profiles(&t);
        // the default config folder holds profiles/dev: its top-level chat files are the only ones listed
        let all = targets(&d, &who("ann@example.test", true, false));
        assert!(all.iter().all(|p| !p.starts_with(&o.config) && !p.starts_with(&o.webview) && !p.starts_with(&o.temp) && p != &o.print), "{all:?}");
        quick(&d, &who("ann@example.test", true, false));
        assert!(untouched(&o), "the other profile's data is all still there");
        assert!(!d.webview.exists() && !d.temp.exists() && !d.print.exists() && !d.legacy_webview.unwrap().exists());
    }

    #[test]
    fn the_start_sweep_only_accepts_the_current_profiles_own_roots() {
        let t = Tmp::new("prof-sweep");
        let (d, o) = two_profiles(&t);
        // a pending list (even an edited one) naming the other side's paths deletes none of them
        let theirs_of_o = [o.webview.clone(), o.temp.clone(), o.print.clone(), o.config.join("chat.json"), o.config.join("avatars")];
        let text: String = theirs_of_o.iter().map(|p| format!("{}\n", p.to_string_lossy())).collect();
        put(&d.pending_file(), &text);
        sweep_pending_with(&d, &remove_any);
        assert!(untouched(&o), "the default profile's start swept another profile's data");
        // and the other way: the default's paths, including the 0.1.0 folder, are not a dev profile's to sweep
        let theirs_of_d = [d.webview.clone(), d.temp.clone(), d.print.clone(), d.config.join("chat.json"), d.legacy_webview.clone().unwrap()];
        let text: String = theirs_of_d.iter().map(|p| format!("{}\n", p.to_string_lossy())).collect();
        put(&o.pending_file(), &text);
        sweep_pending_with(&o, &remove_any);
        assert!(untouched(&d), "a non-default profile's start swept the default profile's data");
        // its own are swept
        put(&o.pending_file(), &format!("{}\n{}\n{}\n", o.webview.to_string_lossy(), o.temp.to_string_lossy(), o.print.to_string_lossy()));
        sweep_pending_with(&o, &remove_any);
        assert!(!o.webview.exists() && !o.temp.exists() && !o.print.exists());
    }

    #[test]
    fn the_default_sweep_leaves_the_profiles_folder_alone() {
        let t = Tmp::new("prof-sub");
        let (d, o) = two_profiles(&t);
        // the whole profiles folder and anything in it is no default sweep's to delete
        put(&d.pending_file(), &format!("{}\n{}\n", d.config.join("profiles").to_string_lossy(), o.config.to_string_lossy()));
        sweep_pending_with(&d, &remove_any);
        assert!(untouched(&o) && o.config.exists());
    }

    #[test]
    fn a_temp_folder_is_swept_itself_when_it_stayed_locked() {
        let t = Tmp::new("prof-temp");
        let r = t.roots_for("dev");
        put(&r.temp.join("a.pdf"), "x");
        put(&r.pending_file(), &format!("{}\n", r.temp.to_string_lossy()));
        sweep_pending_with(&r, &remove_any);
        assert!(!r.temp.exists());
    }
}
