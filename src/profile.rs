//! Profiles: `BARBICAN_PROFILE` selects a separate set of settings, caches and secrets, so a
//! development, test or automation run never reads or overwrites the user's own sign-in.
//!
//! * Unset (or `default`): the user's own profile. Its folder and secrets are the ones every
//!   earlier version used.
//! * Anything else: its own folder (`<config>/profiles/<name>`) and its own secret names
//!   (`Barbican/<name>/<secret>`, see `secrets.rs`).
//! * An automation run (`BARBICAN_AUTOMATION=1`) is never allowed on the default profile:
//!   without a profile it gets `auto-<port>` (the automation port), and naming `default`
//!   counts as naming none.
use std::sync::OnceLock;

pub const DEFAULT: &str = "default";
const MAX_LEN: usize = 40;

/// A profile name that is safe as a folder name and in a credential name: ASCII letters,
/// digits, `-` and `_` only (so no dots, no separators, nothing that climbs out of the
/// profiles folder), at most 40 characters, lower case. `None` for an empty name.
pub fn sanitize(raw: &str) -> Option<String> {
    let s: String = raw.trim().chars().take(MAX_LEN)
        .map(|c| if c.is_ascii_alphanumeric() || c == '-' || c == '_' { c.to_ascii_lowercase() } else { '_' })
        .collect();
    (!s.is_empty()).then_some(s)
}

/// The profile for a given environment: `env_profile` is `BARBICAN_PROFILE`, `automation_port`
/// is `Some(port)` for an automation run.
pub fn resolve(env_profile: Option<&str>, automation_port: Option<&str>) -> String {
    let named = env_profile.and_then(sanitize).filter(|p| p != DEFAULT);
    match (named, automation_port) {
        (Some(p), _) => p,
        (None, Some(port)) => format!("auto-{}", sanitize(port).unwrap_or_else(|| "8792".into())),
        (None, None) => DEFAULT.to_string(),
    }
}

/// The profile of this process (read once from the environment).
pub fn current() -> &'static str {
    static P: OnceLock<String> = OnceLock::new();
    P.get_or_init(|| {
        let port = crate::automation::enabled()
            .then(|| std::env::var("BARBICAN_AUTOMATION_PORT").unwrap_or_else(|_| "8792".into()));
        let profile = resolve(std::env::var("BARBICAN_PROFILE").ok().as_deref(), port.as_deref());
        // a test process never touches the user's own sign-in or settings
        #[cfg(test)]
        let profile = if is_default(&profile) { test_profile() } else { profile };
        profile
    })
}

/// The throwaway profile of a test process (unit tests only).
#[cfg(test)]
pub fn test_profile() -> String { format!("test-{}", std::process::id()) }

pub fn is_default(profile: &str) -> bool { profile == DEFAULT }

/// The folder below the config folder that belongs to `profile` (none for the default one).
pub fn subdir(profile: &str) -> Option<std::path::PathBuf> {
    (!is_default(profile)).then(|| std::path::Path::new("profiles").join(profile))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn unset_is_the_default_profile() {
        assert_eq!(resolve(None, None), "default");
        assert_eq!(resolve(Some(""), None), "default");
        assert_eq!(resolve(Some("  "), None), "default");
        assert_eq!(resolve(Some("Default"), None), "default");
    }

    #[test]
    fn named_profile_is_cleaned() {
        assert_eq!(resolve(Some("Dev"), None), "dev");
        assert_eq!(resolve(Some("my test!"), None), "my_test_");
        assert_eq!(sanitize("a".repeat(100).as_str()).unwrap().len(), 40);
    }

    #[test]
    fn a_name_cannot_climb_out_of_the_profiles_folder() {
        for bad in ["..", "../x", "a/b", "a\\b", ".", "..\\..\\x"] {
            let p = resolve(Some(bad), None);
            assert!(!p.contains(['.', '/', '\\']), "{bad} -> {p}");
            let dir = subdir(&p);
            assert!(dir.is_some_and(|d| d.components().count() == 2));
        }
    }

    #[test]
    fn automation_never_gets_the_default_profile() {
        assert_eq!(resolve(None, Some("8792")), "auto-8792");
        assert_eq!(resolve(Some("default"), Some("9001")), "auto-9001");
        assert_eq!(resolve(Some(""), Some("9001")), "auto-9001");
        // an explicit other profile is kept
        assert_eq!(resolve(Some("qa"), Some("9001")), "qa");
        assert!(!is_default(&resolve(None, Some("8792"))));
    }

    #[test]
    fn only_the_default_profile_keeps_the_old_folder() {
        assert_eq!(subdir("default"), None);
        assert_eq!(subdir("dev"), Some(std::path::Path::new("profiles").join("dev")));
    }
}
