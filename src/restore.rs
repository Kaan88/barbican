//! What happens when the saved sign-in cannot be used at start-up: told apart (the server turned
//! it down, or it could not be asked), retried when it is only the network, always said out loud,
//! and never dropped from the store. Pure functions; `restore_session` in main.rs uses them.
use crate::jmap::{auth_rejected, normalize_base};
use crate::oauth::Remembered;
use serde_json::Value;
use std::time::Duration;

/// Seconds to wait before retry 1, 2, 3 and 4 of a restore at start-up that failed for a
/// reason other than the server turning the sign-in down.
const RETRY_SECONDS: [u64; 4] = [2, 5, 15, 30];

#[derive(Debug, PartialEq)]
pub enum RestoreOutcome {
    /// Not the server's verdict (no network yet, a timeout, a 5xx): wait, then try again.
    Retry(Duration),
    /// Show the login form with this text; the stored sign-in stays where it is.
    ShowForm(String),
}

/// The host of a server address as typed or normalized (no scheme, port or path).
pub fn server_host(server: &str) -> String {
    server.trim().trim_start_matches("https://").trim_start_matches("http://").split(['/', ':']).next().unwrap_or("").to_string()
}

/// Who a saved sign-in is for in a message: the user, or the server's host when the user is
/// blank (a 0.1.0 SSO session has no user); never an empty name.
pub fn who(user: &str, server: &str) -> String {
    let user = user.trim();
    if !user.is_empty() { return user.to_string(); }
    let host = server_host(server);
    if host.is_empty() { "This account".to_string() } else { host }
}

/// The text for a saved sign-in the server turned down. A cleared saved password was never sent,
/// so its own text says what happened instead.
pub fn turned_down(who: &str, err: &str) -> String {
    if err == crate::jmap::CLEARED_PASSWORD { return format!("{who}: {err}"); }
    format!("{who}: the saved sign-in was turned down by the server (expired or revoked). Sign in again. The server said: {err} Your saved sign-in stays on this computer until you sign in.")
}

/// What to do about a failed restore at start-up: `attempt` counts the tries made so far
/// (0 after the first failure).
pub fn restore_outcome(err: &str, attempt: usize, user: &str, server: &str) -> RestoreOutcome {
    if auth_rejected(err) { return RestoreOutcome::ShowForm(turned_down(&who(user, server), err)); }
    match RETRY_SECONDS.get(attempt) {
        Some(s) => RestoreOutcome::Retry(Duration::from_secs(*s)),
        None => {
            let host = server_host(server);
            let host = if host.is_empty() { "the server".to_string() } else { host };
            RestoreOutcome::ShowForm(format!("Could not reach {host} to restore your sign-in: {err} Your saved sign-in is kept; try again, or sign in."))
        }
    }
}

/// The stored session to try on a retry of a start-up restore. A failed attempt may already have
/// refreshed: the server rotated the token, the new one went into the store, and only the call
/// after the refresh (the session discovery) failed. The token this restore was started with is
/// then dead, so each retry starts from the store as it is now, when it is still the same session
/// (oauth, same token endpoint, same account, same server). Anything else (nothing stored, an
/// unreadable entry, another account) keeps `v` as it is.
pub fn retry_value(v: &Value, stored: &Remembered) -> Value {
    let Remembered::Some(s) = stored else { return v.clone() };
    let ep = v["endpoint"].as_str().unwrap_or("");
    let same = v["kind"] == "oauth" && s["kind"] == "oauth" && !ep.is_empty() && s["endpoint"].as_str() == Some(ep)
        && s["user"].as_str().unwrap_or("").eq_ignore_ascii_case(v["user"].as_str().unwrap_or(""))
        && normalize_base(s["server"].as_str().unwrap_or("")) == normalize_base(v["server"].as_str().unwrap_or(""));
    match s["refresh"].as_str().filter(|r| !r.is_empty()) {
        Some(r) if same => { let mut n = v.clone(); n["refresh"] = Value::String(r.to_string()); n }
        _ => v.clone(),
    }
}

/// The one diagnostic line of a start, without a single value (no token, password, user or
/// server name): where the entry came from, its kind, whether its user was blank, how it went.
pub fn line(source: &str, kind: &str, user_empty: bool, outcome: &str) -> String {
    format!("restore: source={source} kind={kind} user_empty={user_empty} outcome={outcome}")
}

/// Write the line to stderr and to `restore.log` in the config folder (the last 20 starts).
pub fn log(line: &str) {
    eprintln!("{line}");
    let path = crate::config_dir().join("restore.log");
    let mut lines: Vec<String> = std::fs::read_to_string(&path).map(|s| s.lines().map(String::from).collect()).unwrap_or_default();
    lines.push(line.to_string());
    let from = lines.len().saturating_sub(20);
    let _ = std::fs::create_dir_all(crate::config_dir());
    let _ = std::fs::write(&path, lines[from..].join("\n") + "\n");
}

#[cfg(test)]
mod tests {
    use super::*;

    const GRANT: &str = "Session expired; sign in again. (Token request failed (400 Bad Request): invalid_grant )";

    #[test]
    fn a_rejection_is_final_and_says_so_with_a_name() {
        let RestoreOutcome::ShowForm(m) = restore_outcome(GRANT, 0, "ann@example.test", "https://mail.example.test") else { panic!("rejection must end the attempt") };
        assert!(m.starts_with("ann@example.test: the saved sign-in was turned down by the server"), "{m}");
        assert!(m.contains("invalid_grant") && m.contains("until you sign in"));
        // a blank user (a 0.1.0 SSO session) is named by the server, never by an empty string
        let RestoreOutcome::ShowForm(m) = restore_outcome(GRANT, 3, "", "https://mail.example.test:8443/x") else { panic!() };
        assert!(m.starts_with("mail.example.test: the saved"), "{m}");
        let RestoreOutcome::ShowForm(m) = restore_outcome(GRANT, 0, "  ", "") else { panic!() };
        assert!(m.starts_with("This account: the saved"), "{m}");
        assert!(!m.starts_with(':') && !m.starts_with(' '));
        // the same for a token the JMAP server turned down and for a wrong password
        for e in ["The server did not accept the sign-in token.", "Wrong username, password or 2FA code"] {
            assert!(matches!(restore_outcome(e, 0, "ann@example.test", "x"), RestoreOutcome::ShowForm(_)));
        }
    }

    #[test]
    fn a_cleared_saved_password_is_not_called_turned_down_by_the_server() {
        let RestoreOutcome::ShowForm(m) = restore_outcome(crate::jmap::CLEARED_PASSWORD, 0, "ann@example.test", "x") else { panic!("final") };
        assert_eq!(m, format!("ann@example.test: {}", crate::jmap::CLEARED_PASSWORD));
        assert!(!m.contains("turned down by the server"), "{m}");
    }

    #[test]
    fn trouble_with_the_network_retries_with_backoff_and_then_says_why() {
        for e in ["error sending request: timed out", "Token request failed (503 Service Unavailable):  ", "Server returned 502", "error sending request for url"] {
            for (attempt, secs) in [2u64, 5, 15, 30].into_iter().enumerate() {
                assert_eq!(restore_outcome(e, attempt, "ann@example.test", "https://mail.example.test"), RestoreOutcome::Retry(Duration::from_secs(secs)), "{e} attempt {attempt}");
            }
            let RestoreOutcome::ShowForm(m) = restore_outcome(e, 4, "ann@example.test", "https://mail.example.test") else { panic!("{e}") };
            assert!(m.starts_with("Could not reach mail.example.test to restore your sign-in:") && m.contains(e), "{m}");
            assert!(!m.contains("turned down"));
        }
    }

    fn session(user: &str, server: &str, refresh: &str) -> Value {
        serde_json::json!({"kind": "oauth", "server": server, "user": user, "refresh": refresh, "endpoint": "https://mail.example.test/auth/token", "client": "c"})
    }

    #[test]
    fn a_retry_starts_from_the_token_the_store_holds_now() {
        let v = session("", "https://mail.example.test", "r_cur");
        let stored = Remembered::Some(session("", "mail.example.test/", "r_new1"));
        assert_eq!(retry_value(&v, &stored)["refresh"], "r_new1");
        let named = session("Ann@Example.test", "https://mail.example.test", "a");
        assert_eq!(retry_value(&named, &Remembered::Some(session("ann@example.test", "https://mail.example.test", "b")))["refresh"], "b");
        // other account, other server, other endpoint, nothing or something unreadable: as it was
        for other in [
            Remembered::Some(session("bob@example.test", "https://mail.example.test", "b")),
            Remembered::Some(session("", "https://other.example.test", "b")),
            Remembered::Some({ let mut o = session("", "https://mail.example.test", "b"); o["endpoint"] = "https://other.example.test/t".into(); o }),
            Remembered::Some(session("", "https://mail.example.test", "")),
            Remembered::Some(serde_json::json!({"kind": "password", "server": "https://mail.example.test", "user": "", "pass": "p"})),
            Remembered::Missing, Remembered::Unreadable,
        ] {
            assert_eq!(retry_value(&v, &other), v);
        }
    }

    #[test]
    fn the_host_and_the_name_in_messages() {
        assert_eq!(server_host("https://mail.example.test/"), "mail.example.test");
        assert_eq!(server_host("mail.example.test:8080"), "mail.example.test");
        assert_eq!(who("ann@example.test", "https://mail.example.test"), "ann@example.test");
        assert_eq!(who("", "mail.example.test"), "mail.example.test");
    }

    #[test]
    fn the_diagnostic_line_carries_no_value() {
        let l = line("legacy", "oauth", true, "rejected");
        assert_eq!(l, "restore: source=legacy kind=oauth user_empty=true outcome=rejected");
    }
}
