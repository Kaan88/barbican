//! Startup crash guard. A setting that crashes the app while it starts (once a switched-on
//! Gravatar crashed every list build) must not make every later start crash too. `start`
//! leaves a marker in the config dir before the window is shown. `done` removes it as soon
//! as the start has got past what could crash: the first message list has been rendered
//! after sign-in (the crash was in that list build, which follows an asynchronous sign-in
//! that can take longer than any fixed wait), or the login form is shown because nothing is
//! remembered, or the app quits cleanly. [`UP_FOR`] is only the fallback (an empty mailbox
//! never renders a row). A marker still there at the next start means the last start did
//! not get that far: this run keeps the network-picture features (Settings > Sender
//! pictures, Gravatar) off. The saved settings are left as they are.
//!
//! The marker belongs to the first Barbican of a profile: a second instance neither writes
//! nor removes it (it would otherwise find the first's marker and wrongly start in safe mode,
//! and its quit would clear the marker of a first instance that has not got past the start).
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};

/// The fallback: a start that has neither rendered a list nor shown the login form after
/// this long still counts as good (a slow server, an empty mailbox).
pub const UP_FOR: std::time::Duration = std::time::Duration::from_secs(60);

pub const NOTICE: &str = "Barbican did not start properly last time, so sender pictures from the web are off for now. You can switch them on again in Settings.";

static SAFE: AtomicBool = AtomicBool::new(false);
/// This process wrote the marker and has not removed it yet.
static ACTIVE: AtomicBool = AtomicBool::new(false);

fn marker(dir: &Path) -> PathBuf { dir.join("starting.marker") }

/// Marks a start as under way. True when the previous start never got cleared.
pub fn begin(dir: &Path) -> bool {
    let crashed = marker(dir).exists();
    let _ = std::fs::create_dir_all(dir);
    let _ = std::fs::write(marker(dir), b"");
    crashed
}

/// The start went well (or the app quit normally).
pub fn clear(dir: &Path) { let _ = std::fs::remove_file(marker(dir)); }

/// Mark this process's start as under way, when it is the first Barbican of its profile
/// (`first`); True when the previous start never got cleared. A later instance does not
/// guard, and so never reports a crash it did not see.
pub fn start(dir: &Path, first: bool) -> bool {
    if !first { return false; }
    ACTIVE.store(true, Ordering::SeqCst);
    begin(dir)
}

/// The start got past what could crash (or the app quit): remove this process's marker. Once
/// only, and only the marker this process wrote.
pub fn done(dir: &Path) {
    if ACTIVE.swap(false, Ordering::SeqCst) { clear(dir); }
}

/// Network pictures stay off for this run.
pub fn enter_safe_mode() { SAFE.store(true, Ordering::SeqCst); }

pub fn safe_mode() -> bool { SAFE.load(Ordering::SeqCst) }

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn only_the_first_instance_guards_and_it_clears_once() {
        let dir = std::env::temp_dir().join(format!("barbican-guard-first-{}", std::process::id())).join("cfg");
        let _ = std::fs::remove_dir_all(&dir);
        // a second instance neither writes a marker nor reports a crash
        assert!(!start(&dir, false));
        assert!(!marker(&dir).exists());
        done(&dir); // and its quit clears nothing
        // the first instance guards
        assert!(!start(&dir, true));
        assert!(marker(&dir).exists());
        // the first instance's done clears its marker
        done(&dir);
        assert!(!marker(&dir).exists());
        // done again is harmless, and cannot remove a marker this process did not write
        assert!(!begin(&dir));
        done(&dir);
        assert!(marker(&dir).exists(), "a marker written by someone else stays");
        clear(&dir);
        let _ = std::fs::remove_dir_all(dir.parent().unwrap());
    }

    #[test]
    fn a_marker_left_behind_means_the_last_start_failed() {
        let dir = std::env::temp_dir().join(format!("barbican-guard-test-{}", std::process::id())).join("cfg");
        let _ = std::fs::remove_dir_all(&dir);
        // first start: no marker yet, and one is left
        assert!(!begin(&dir));
        assert!(marker(&dir).exists());
        // it crashed before clear(): the next start sees it (and leaves a fresh marker)
        assert!(begin(&dir));
        // a good start clears it; the start after that is normal again
        clear(&dir);
        assert!(!marker(&dir).exists());
        assert!(!begin(&dir));
        clear(&dir);
        // clearing nothing is fine
        clear(&dir);
        let _ = std::fs::remove_dir_all(dir.parent().unwrap());
    }
}
