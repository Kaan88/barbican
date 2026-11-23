//! Startup crash guard. A setting that crashes the app while it starts (once a switched-on
//! Gravatar crashed every list build) must not make every later start crash too. `begin`
//! leaves a marker in the config dir before the window is shown; it is removed once the app
//! has been up a few seconds, and on a clean quit. A marker still there at the next start
//! means the last start did not get that far: this run keeps the network-picture features
//! (Settings > Sender pictures, Gravatar) off. The saved settings are left as they are.
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};

/// How long the app must stay up before a start counts as good.
pub const UP_FOR: std::time::Duration = std::time::Duration::from_secs(8);

pub const NOTICE: &str = "Barbican did not start properly last time, so sender pictures from the web are off for now. You can switch them on again in Settings.";

static SAFE: AtomicBool = AtomicBool::new(false);

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

/// Network pictures stay off for this run.
pub fn enter_safe_mode() { SAFE.store(true, Ordering::SeqCst); }

pub fn safe_mode() -> bool { SAFE.load(Ordering::SeqCst) }

#[cfg(test)]
mod tests {
    use super::*;

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
