//! Settings > About > "Refresh cached data" (Bulwark lib/clear-cached-data.ts and
//! components/settings/about-data-settings.tsx:273-293, S-keyless-063): drop what Barbican keeps
//! only to be faster, then load the view again from the server. Nothing the user made goes:
//! settings, credentials, drafts and the outbox (both on the server), plugin storage, other
//! profiles and the app lock are never touched.
//!
//! What a refresh clears, each a cache Barbican can rebuild by itself:
//! - `<config>/avatars/`: sender pictures (avatars.rs: "Answers are kept on disk (config dir
//!   `avatars/`) for two weeks"); each one is fetched again when a message needs it.
//! - sender pictures held in memory (avatars::forget_memory), verified-brand (BIMI) answers
//!   (bimi::forget_all) and the opened message bodies (bodycache::clear), which the next view
//!   fills from the server.
//!
//! Deliberately untouched, with the reason:
//! - `prefs.json`, `settings.json`, `panes.json`, `window.json`, `search-history.json`,
//!   `files.json`, `chat*.json`, `reminders*`, `settings-sync.json`, `oauth-clients.json`,
//!   `login.txt`, `restore.log`, `pending-delete.txt`: settings and the user's own state, not
//!   caches.
//! - `plugins/` and `plugin-data/`: installed plugins and what they stored.
//! - `profiles/`: other profiles (the default profile's config folder holds them).
//! - the app lock's files (applock.rs) and the sign-in session (src/secretstore): losing them
//!   would lock the user out or make them sign in again.
//! - the WebView profile (`platform::webview_dir()`): cookies and cache of the mail view, held
//!   open by the running view; signing out empties it (signout.rs). Deleting it under a live
//!   view is what the sign-out path avoids for the same reason.
//! - `<data>/updates/`: installers the updater downloaded, not a view cache.
use std::path::{Path, PathBuf};

/// The config-folder entries a refresh deletes. Fixed here in code: no file the user can edit
/// decides what goes.
pub const CACHE_DIRS: [&str; 1] = ["avatars"];

/// The paths a refresh removes under `config`: only a direct child with one of [`CACHE_DIRS`],
/// so a config path that is empty or holds `..` cannot reach anything else.
pub fn targets(config: &Path) -> Vec<PathBuf> {
    if config.as_os_str().is_empty() { return vec![]; }
    CACHE_DIRS.iter().filter_map(|name| {
        let p = config.join(name);
        (p.parent() == Some(config) && p.file_name() == Some(std::ffi::OsStr::new(name))).then_some(p)
    }).collect()
}

/// What a refresh deleted, and what it could not (a folder a program still has open).
#[derive(Debug, Default, PartialEq)]
pub struct Report {
    pub removed: Vec<PathBuf>,
    pub kept: Vec<PathBuf>,
}

impl Report {
    /// The line the page shows: what went, and anything left alone.
    pub fn summary(&self) -> String {
        match self.kept.len() {
            0 => "Cached data refreshed".to_string(),
            1 => "Cached data refreshed; one folder is still in use and was left alone".to_string(),
            n => format!("Cached data refreshed; {n} folders are still in use and were left alone"),
        }
    }
}

/// Delete the cache folders under `config`. Anything already gone counts as removed.
pub fn clear_in(config: &Path) -> Report {
    clear_with(config, &|p| std::fs::remove_dir_all(p))
}

/// [`clear_in`] with its deletion injected (`remove` deletes one path), so a test can make one
/// fail and see it reported as kept instead of thrown away.
pub fn clear_with(config: &Path, remove: &dyn Fn(&Path) -> std::io::Result<()>) -> Report {
    let mut r = Report::default();
    for p in targets(config) {
        if !p.exists() { continue; }
        match remove(&p) {
            Ok(()) => r.removed.push(p),
            Err(_) if !p.exists() => r.removed.push(p),
            Err(_) => r.kept.push(p),
        }
    }
    r
}

/// The button: the disk caches, then the session's caches in memory. The caller loads the view
/// again from the server (folders, list, calendar, contacts).
pub fn refresh() -> Report {
    let report = clear_in(&crate::config_dir());
    crate::avatars::forget_memory();
    crate::bimi::forget_all();
    crate::bodycache::with(|c| c.clear());
    report
}

#[cfg(test)]
mod tests {
    use super::*;

    fn temp_root(tag: &str) -> PathBuf {
        let d = std::env::temp_dir().join(format!("barbican-cachedata-{tag}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&d);
        std::fs::create_dir_all(&d).unwrap();
        d
    }

    /// A config folder with the cache, and the files that must survive a refresh.
    fn config_with_caches(root: &Path) -> PathBuf {
        let config = root.join("config");
        std::fs::create_dir_all(config.join("avatars")).unwrap();
        std::fs::write(config.join("avatars").join("fav:acme.test"), b"png").unwrap();
        let keep = ["prefs.json", "settings.json", "login.txt", "pending-delete.txt", "chat-ann@x.test.json", "lock.json"];
        for f in keep {
            std::fs::write(config.join(f), b"x").unwrap();
        }
        std::fs::create_dir_all(config.join("plugin-data").join("dev.example.demo")).unwrap();
        std::fs::write(config.join("plugin-data").join("dev.example.demo").join("stored.json"), b"{}").unwrap();
        // another profile keeps its own pictures
        std::fs::create_dir_all(config.join("profiles").join("work").join("avatars")).unwrap();
        std::fs::write(config.join("profiles").join("work").join("avatars").join("bimi:acme.test"), b"png").unwrap();
        config
    }

    #[test]
    fn only_the_pictures_go() {
        let root = temp_root("paths");
        let config = config_with_caches(&root);
        // the one and only target
        assert_eq!(targets(&config), vec![config.join("avatars")]);
        let r = clear_in(&config);
        assert_eq!(r, Report { removed: vec![config.join("avatars")], kept: vec![] });
        assert_eq!(r.summary(), "Cached data refreshed");
        assert!(!config.join("avatars").exists());
        for f in ["prefs.json", "settings.json", "login.txt", "pending-delete.txt", "chat-ann@x.test.json", "lock.json"] {
            assert!(config.join(f).is_file(), "{f} was deleted");
        }
        assert!(config.join("plugin-data").join("dev.example.demo").join("stored.json").is_file());
        assert!(config.join("profiles").join("work").join("avatars").join("bimi:acme.test").is_file());
        // nothing left to clear: nothing reported either way
        assert_eq!(clear_in(&config), Report::default());
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn a_folder_in_use_is_kept_and_said_so() {
        let root = temp_root("locked");
        let config = config_with_caches(&root);
        // a deletion that fails (a file another program has open) must not be reported as gone
        let r = clear_with(&config, &|_| Err(std::io::Error::new(std::io::ErrorKind::PermissionDenied, "in use")));
        assert_eq!(r, Report { removed: vec![], kept: vec![config.join("avatars")] });
        assert!(config.join("avatars").join("fav:acme.test").is_file());
        assert!(r.summary().contains("left alone"), "{}", r.summary());
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn an_empty_or_odd_config_path_reaches_nothing() {
        assert!(targets(Path::new("")).is_empty());
        assert_eq!(clear_in(Path::new("")).removed, Vec::<PathBuf>::new());
        // the paths are always exactly <config>/<name>: never the config folder itself or above it
        let root = temp_root("odd");
        for config in [root.clone(), root.join("a").join("b")] {
            for p in targets(&config) {
                assert_eq!(p.parent(), Some(config.as_path()));
                assert!(!p.components().any(|c| c.as_os_str() == ".."));
            }
        }
        let _ = std::fs::remove_dir_all(&root);
    }
}
