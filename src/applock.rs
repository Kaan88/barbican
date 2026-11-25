//! App lock (Bulwark components/app-lock/*, lib/app-lock/*, components/settings/app-lock-settings.tsx):
//! a PIN asked at every start and after a time without input in the app, over the whole window.
//!
//! * The PIN itself is never stored. A salted PBKDF2-HMAC-SHA256 hash (600 000 rounds, Bulwark
//!   lib/app-lock/crypto.ts:15) is kept in the profile's secret store (`Barbican/<profile>/applock`,
//!   `secrets.rs`), with the count of wrong tries: the count is written before a PIN is checked,
//!   so quitting during the check or after a wrong one gains nothing (lib/app-lock/vault.ts:1-10, 236-262).
//!   Nothing of it is in prefs.json, so a settings export or sync never carries it to another computer.
//! * Bulwark's PIN unwraps the key of the mail it keeps in the browser. Barbican keeps no mail on
//!   disk, so here the lock is a gate over the window: the lock screen covers everything, the mail
//!   view (a WebView) is hidden, keys and clicks never reach the app, and notifications say only
//!   "New mail" (lib/desktop/mail-notifications.ts:100, reminder-notifications.ts:78). The session
//!   stays signed in and mail keeps syncing behind it.
//! * Windows Hello (UserConsentVerifier) is a yes/no check by Windows: it opens the lock, the PIN keeps
//!   working and is needed to turn Hello on (Bulwark's "os" unlock, locales/en appLock.os_desc).
//! * A forgotten PIN: the way out is signing out every account on this computer, which also
//!   removes the PIN (lib/app-lock/sign-out.ts `forgetAppLockAtSignOut`). The last account
//!   signing out the ordinary way removes it as well: nobody is left to lock for.
use crate::secrets::Backend;
use base64::Engine;
use serde_json::{json, Value};
use std::num::NonZeroU32;
use std::sync::{Mutex, MutexGuard};

/// The app's own floor for a new PIN (Bulwark lib/app-lock/crypto.ts:17).
pub const MIN_PIN_LENGTH: usize = 5;
/// OWASP's floor for PBKDF2-HMAC-SHA256 (crypto.ts:15).
pub const PBKDF2_ITERATIONS: u32 = 600_000;
/// Wrong PINs that cost nothing; then 30 s, doubling, up to an hour, and never a wipe (vault.ts:209-219).
pub const FREE_FAILURES: u32 = 4;
const FIRST_WAIT_MS: i64 = 30_000;
pub const LONGEST_WAIT_MS: i64 = 60 * 60_000;
/// Bulwark lib/app-lock/state.ts:24-25.
pub const DEFAULT_TIMEOUT_MINUTES: i64 = 10;
pub const TIMEOUT_CHOICES: [i64; 7] = [1, 5, 10, 15, 30, 60, 240];
const SECRET: &str = "applock";

pub const HINT: &str = "At least 5 characters. Letters work too, and a longer PIN is harder to guess.";

// ---------------- the PIN ----------------

/// The same PIN typed on another keyboard gives the same bytes (crypto.ts:41 `pinBytes`).
pub fn normalize(pin: &str) -> String {
    icu_normalizer::ComposingNormalizerBorrowed::new_nfkc().normalize(pin).into_owned()
}

pub fn pin_long_enough(pin: &str) -> bool {
    normalize(pin).chars().count() >= MIN_PIN_LENGTH
}

/// A few of what people pick most (crypto.ts:51-56). Runs and repeats are found by rule.
const COMMON_PINS: &[&str] = &[
    "qwert", "asdfg", "yxcvb", "zxcvb", "1q2w3", "abc12", "12321",
    "password", "password1", "password123", "passwort", "qwerty", "qwertz", "qwerty123", "qwertyuiop", "asdfgh", "yxcvbn", "zxcvbn",
    "1q2w3e", "1q2w3e4r", "q1w2e3", "abc123", "a1b2c3", "123abc", "letmein", "iloveyou", "admin123", "welcome", "welcome1", "monkey", "dragon",
    "147258", "159753", "258456", "789456", "102030", "101010", "696969", "111222", "121314", "131313", "123321", "112233",
];

/// Every step the same; digits go round as on a keypad: 7890123 (crypto.ts:59-66).
fn all_same_step(chars: &[char], step: i64) -> bool {
    chars.windows(2).all(|w| {
        let distance = w[1] as i64 - w[0] as i64;
        let digits = w[0].is_ascii_digit() && w[1].is_ascii_digit();
        distance == step || (digits && distance.rem_euclid(10) == step.rem_euclid(10))
    })
}

/// Refused when a PIN is set: someone guessing tries exactly these first (crypto.ts:69-78).
pub fn pin_is_obvious(pin: &str) -> bool {
    let chars: Vec<char> = normalize(pin).to_lowercase().chars().collect();
    let text: String = chars.iter().collect();
    if COMMON_PINS.contains(&text.as_str()) { return true; }
    if all_same_step(&chars, 0) || all_same_step(&chars, 1) || all_same_step(&chars, -1) { return true; }
    // 121212, 123123, abcabc: a short piece over and over
    (1..=3).any(|size| chars.len() >= size * 2 && chars.iter().enumerate().all(|(i, c)| *c == chars[i % size]))
}

/// What the new-PIN fields say, and whether Save may be pressed (Bulwark pin-fields.tsx:20-23, 58-60):
/// nothing is said before there is something to say.
pub fn new_pin_check(pin: &str, repeat: &str) -> (bool, &'static str) {
    let long = pin_long_enough(pin);
    if !pin.is_empty() && !long { return (false, "That PIN is too short."); }
    if long && pin_is_obvious(pin) { return (false, "That PIN is too easy to guess. Avoid repeats, runs and common choices."); }
    if !repeat.is_empty() && repeat != pin { return (false, "The two PINs are not the same."); }
    (long && repeat == pin, "")
}

/// How long after this many wrong tries the next one is allowed (vault.ts:214-217).
pub fn wait_after(failures: u32) -> i64 {
    if failures <= FREE_FAILURES { return 0; }
    let doublings = (failures - FREE_FAILURES - 1).min(20);
    (FIRST_WAIT_MS << doublings).min(LONGEST_WAIT_MS)
}

/// When the app locks by itself.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Timeout {
    /// After this many minutes without input in the app.
    Minutes(i64),
    /// Only when Barbican starts (Bulwark's desktop "When I sign out or restart").
    Session,
}

/// What the secret store keeps.
#[derive(Clone, Debug, PartialEq)]
pub struct Record {
    salt: Vec<u8>,
    iterations: u32,
    hash: Vec<u8>,
    pub failures: u32,
    /// Unix ms before which no PIN is checked.
    pub blocked_until: i64,
    pub timeout: Timeout,
    pub hello: bool,
}

/// The salt's length: a stored record with another one was not written by Barbican.
const SALT_LEN: usize = 16;

/// The fewest rounds a stored record may have: one with fewer was not written by Barbican (or
/// was weakened) and is unreadable. The tests' records use fewer to stay fast.
fn min_iterations() -> u32 { if cfg!(test) { 1_000 } else { PBKDF2_ITERATIONS } }

fn derive(pin: &str, salt: &[u8], iterations: u32) -> Vec<u8> {
    let mut out = vec![0u8; 32];
    let n = NonZeroU32::new(iterations.max(1)).unwrap_or(NonZeroU32::MIN);
    let pin = zeroize::Zeroizing::new(normalize(pin));
    ring::pbkdf2::derive(ring::pbkdf2::PBKDF2_HMAC_SHA256, n, salt, pin.as_bytes(), &mut out);
    out
}

fn b64() -> base64::engine::GeneralPurpose { base64::engine::general_purpose::STANDARD }

impl Record {
    /// A new PIN with a new random salt. None when no random bytes could be had.
    pub fn new(pin: &str, iterations: u32) -> Option<Record> {
        let mut salt = vec![0u8; SALT_LEN];
        getrandom::getrandom(&mut salt).ok()?;
        let hash = derive(pin, &salt, iterations);
        Some(Record { salt, iterations, hash, failures: 0, blocked_until: 0, timeout: Timeout::Minutes(DEFAULT_TIMEOUT_MINUTES), hello: false })
    }

    /// The same derivation, compared in constant time (ring's `pbkdf2::verify`).
    pub fn matches(&self, pin: &str) -> bool {
        let Some(n) = NonZeroU32::new(self.iterations) else { return false };
        let pin = zeroize::Zeroizing::new(normalize(pin));
        ring::pbkdf2::verify(ring::pbkdf2::PBKDF2_HMAC_SHA256, n, &self.salt, pin.as_bytes(), &self.hash).is_ok()
    }

    /// What the remembered sign-ins carry (oauth::LOCK_MARKER): a hash of the salt and the PIN's
    /// hash, which change with every new PIN and never with the count of tries.
    pub fn fingerprint(&self) -> String {
        use sha2::Digest;
        let mut h = sha2::Sha256::new();
        h.update(b"barbican-app-lock-v1");
        h.update(&self.salt);
        h.update(&self.hash);
        h.update(self.iterations.to_le_bytes());
        h.finalize().iter().take(16).map(|b| format!("{b:02x}")).collect()
    }

    pub fn to_json(&self) -> String {
        let (minutes, until) = match self.timeout { Timeout::Minutes(m) => (json!(m), Value::Null), Timeout::Session => (Value::Null, json!("session")) };
        json!({
            "v": 1, "kdf": "pbkdf2-sha256", "salt": b64().encode(&self.salt), "iterations": self.iterations, "hash": b64().encode(&self.hash),
            "failures": self.failures, "blockedUntil": self.blocked_until, "timeoutMinutes": minutes, "until": until, "hello": self.hello,
        }).to_string()
    }

    pub fn from_json(text: &str) -> Option<Record> { Record::parse(text, min_iterations()) }

    /// A stored record, refused when it has fewer rounds than `min_rounds` or a salt of another length.
    pub fn parse(text: &str, min_rounds: u32) -> Option<Record> {
        let v: Value = serde_json::from_str(text).ok()?;
        if v["kdf"] != "pbkdf2-sha256" { return None; }
        let salt = b64().decode(v["salt"].as_str()?).ok()?;
        let hash = b64().decode(v["hash"].as_str()?).ok()?;
        let iterations = u32::try_from(v["iterations"].as_u64()?).ok()?;
        if salt.len() != SALT_LEN || hash.len() != 32 || iterations < min_rounds.max(1) { return None; }
        let timeout = if v["until"] == "session" { Timeout::Session } else {
            Timeout::Minutes(v["timeoutMinutes"].as_i64().filter(|m| *m > 0).unwrap_or(DEFAULT_TIMEOUT_MINUTES))
        };
        Some(Record {
            salt, iterations, hash,
            failures: v["failures"].as_u64().unwrap_or(0).min(u32::MAX as u64) as u32,
            blocked_until: v["blockedUntil"].as_i64().unwrap_or(0),
            timeout,
            hello: v["hello"].as_bool().unwrap_or(false),
        })
    }
}

/// What can stop a PIN.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum PinError {
    /// Wrong; the next try waits until then (0: no wait).
    Wrong(i64),
    /// Too many tries: nothing is checked until then.
    Blocked(i64),
    /// The count could not be written, so nothing was checked.
    NotSaved,
    /// No PIN is set.
    NotSet,
    /// The PIN was turned off (in another window): nothing to unlock.
    Off,
}

/// What a stored entry that can't be read means: there is a lock, and nobody knows its PIN. The
/// way out is the same as for a forgotten one.
#[derive(Clone, Debug, PartialEq)]
pub enum Stored {
    /// No entry at all: never set, or removed from outside Barbican.
    Missing,
    Unreadable,
    Some(Record),
    /// Turned off by Barbican: the record's place holds a tombstone ([`turn_off_in`]), so a
    /// window still holding the PIN tells "turned off in another window" from "removed".
    Off,
}

/// A store that cannot be read, a value that is not text, or not a record: Unreadable (locked).
pub fn load_in(b: &dyn Backend, profile: &str) -> Stored {
    match crate::secrets::load_checked(b, profile, SECRET) {
        Err(()) => Stored::Unreadable,
        Ok(None) => Stored::Missing,
        Ok(Some(t)) => match Record::from_json(&t) {
            Some(r) => Stored::Some(r),
            None if is_tombstone(&t) => Stored::Off,
            None => Stored::Unreadable,
        },
    }
}

/// What "Turn off" (and forgetting the PIN) leaves in the record's place. Barbican writes it as
/// UTF-8; a value typed into the Credential Manager window is UTF-16, which reads as Unreadable,
/// so it can't be made by hand there.
fn tombstone(now: i64) -> String { json!({"off": true, "at": now}).to_string() }

fn is_tombstone(text: &str) -> bool {
    serde_json::from_str::<Value>(text).is_ok_and(|v| v["off"] == true)
}

/// The PIN turned off: the tombstone replaces the record.
pub fn turn_off_in(b: &dyn Backend, profile: &str, now: i64) -> bool {
    crate::secrets::save(b, profile, SECRET, &tombstone(now))
}

/// The lock at start, checked against the markers the remembered sign-ins carry: a marker with no
/// lock entry, or one whose fingerprint differs, means the entry was removed or replaced, and the
/// lock stays on with nobody's PIN (Unreadable: only "Forgot your PIN?" gets out).
pub fn checked(stored: Stored, markers: &[String]) -> Stored { checked_with(stored, markers, !markers.is_empty()) }

/// [`checked`] with what decides that a lock is on (`exists`: any sign-in carries a marker) apart
/// from which fingerprints vouch for the entry (`accepted`, see `oauth::lock_markers_in`).
pub fn checked_with(stored: Stored, accepted: &[String], exists: bool) -> Stored {
    match stored {
        Stored::Some(r) if exists && !accepted.contains(&r.fingerprint()) => Stored::Unreadable,
        Stored::Missing | Stored::Off if exists => Stored::Unreadable,
        s => s,
    }
}

/// The marker while a PIN changes: both fingerprints (oauth::lock_markers_in reads each).
pub fn both_markers(old: &str, new: &str) -> String { format!("{old} {new}") }

/// The stored record's fingerprint, when there is one: a sign-in written while the lock is on,
/// before any marker carries it, gets it.
pub fn fingerprint_in(b: &dyn Backend, profile: &str) -> Option<String> {
    match load_in(b, profile) { Stored::Some(r) => Some(r.fingerprint()), _ => None }
}

/// A wait never longer than the longest one, whatever the store says (a clock set back, an
/// edited entry).
fn clamp_wait(blocked_until: i64, now: i64) -> i64 { blocked_until.min(now.saturating_add(LONGEST_WAIT_MS)) }

pub fn save_in(b: &dyn Backend, profile: &str, r: &Record) -> bool {
    crate::secrets::save(b, profile, SECRET, &r.to_json())
}

pub fn forget_in(b: &dyn Backend, profile: &str) {
    crate::secrets::forget(b, profile, SECRET);
}

/// One PIN try: counted first, checked after (vault.ts:243-262). The right PIN clears the count.
pub fn try_pin_in(b: &dyn Backend, profile: &str, pin: &str, now: i64) -> Result<Record, PinError> {
    let Stored::Some(mut r) = load_in(b, profile) else { return Err(PinError::NotSet) };
    let wait = clamp_wait(r.blocked_until, now);
    if wait != r.blocked_until {
        // written back, so the cut wait runs out instead of moving along with the clock
        r.blocked_until = wait;
        let _ = save_in(b, profile, &r);
    }
    if r.blocked_until > now { return Err(PinError::Blocked(r.blocked_until)); }
    r.failures = r.failures.saturating_add(1);
    r.blocked_until = now + wait_after(r.failures);
    if !save_in(b, profile, &r) { return Err(PinError::NotSaved); }
    if !r.matches(pin) {
        return Err(PinError::Wrong(if r.blocked_until > now { r.blocked_until } else { 0 }));
    }
    r.failures = 0;
    r.blocked_until = 0;
    // a right PIN whose clean count could not be written still opens: the next wrong one counts on
    let _ = save_in(b, profile, &r);
    Ok(r)
}

/// When the next try is allowed (0: now).
pub fn blocked_until_in(b: &dyn Backend, profile: &str, now: i64) -> i64 {
    match load_in(b, profile) { Stored::Some(r) if r.blocked_until > now => clamp_wait(r.blocked_until, now), _ => 0 }
}

/// A wait longer than the longest one is written back cut, so it runs out instead of moving along
/// with the clock. Read again here: the caller holds the [`RecordLock`], so nothing changes it in
/// between.
pub fn keep_cut_wait_in(b: &dyn Backend, profile: &str, now: i64) {
    let Stored::Some(mut r) = load_in(b, profile) else { return };
    let wait = clamp_wait(r.blocked_until, now);
    if wait != r.blocked_until {
        r.blocked_until = wait;
        let _ = save_in(b, profile, &r);
    }
}

/// "Too many tries. Try again in 2 minutes." (locales/en appLock.blocked_seconds / blocked_minutes)
pub fn blocked_text(until: i64, now: i64) -> String {
    let left = ((until - now).max(0) + 999) / 1000;
    if left >= 60 {
        let m = (left + 59) / 60;
        format!("Too many tries. Try again in {m} minute{}.", if m == 1 { "" } else { "s" })
    } else {
        format!("Too many tries. Try again in {left} second{}.", if left == 1 { "" } else { "s" })
    }
}

// ---------------- time without input ----------------

/// The timeout has passed since the last input (Bulwark lib/app-lock/idle.ts:46-47). Wall clock:
/// a laptop opened after the timeout locks at once.
pub fn expired(last_input_ms: i64, now_ms: i64, timeout: Timeout) -> bool {
    match timeout {
        Timeout::Session => false,
        Timeout::Minutes(m) => now_ms - last_input_ms >= m.max(1) * 60_000,
    }
}

/// What this process knows: no secret, only whether there is a lock and whether it is locked now.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Live {
    pub on: bool,
    pub locked: bool,
    pub hello: bool,
    pub timeout: Timeout,
    pub last_input_ms: i64,
}

impl Live {
    pub const OFF: Live = Live { on: false, locked: false, hello: false, timeout: Timeout::Minutes(DEFAULT_TIMEOUT_MINUTES), last_input_ms: 0 };

    /// Input at `now`. Input after the timeout has passed locks rather than counts: coming back
    /// to the computer must not undo the timeout (idle.ts:236-239). True when it locked.
    pub fn input(&mut self, now: i64) -> bool {
        if !self.on || self.locked { return false; }
        if expired(self.last_input_ms, now, self.timeout) { self.locked = true; return true; }
        self.last_input_ms = self.last_input_ms.max(now);
        false
    }

    /// The periodic check. True when it locked.
    pub fn tick(&mut self, now: i64) -> bool {
        if !self.on || self.locked || !expired(self.last_input_ms, now, self.timeout) { return false; }
        self.locked = true;
        true
    }

    pub fn unlock(&mut self, now: i64) {
        self.locked = false;
        self.last_input_ms = now;
    }
}

static LIVE: Mutex<Live> = Mutex::new(Live::OFF);

fn live() -> MutexGuard<'static, Live> { LIVE.lock().unwrap_or_else(|e| e.into_inner()) }

/// The last lock record this process read from the store (vouched for by the markers) or wrote:
/// salt, hash and counts, never the PIN. A locked window keeps its PIN with it, so removing the
/// stored entries (with the sign-ins) while it is locked does not open it.
static HELD: Mutex<Option<Record>> = Mutex::new(None);

fn held() -> MutexGuard<'static, Option<Record>> { HELD.lock().unwrap_or_else(|e| e.into_inner()) }

/// Nothing with mail in it may show now (Bulwark lib/app-lock/state.ts `isAppLockedNow`).
pub fn locked() -> bool { live().locked }

/// A PIN is set (locked or not).
pub fn is_on() -> bool { live().on }

/// Whether a notification goes out without its content: always while locked, and whenever a PIN
/// is set but Barbican's delivered notifications can't be cleared when it locks (no app id of its
/// own), since they would stay readable in the notification centre.
pub fn hide_notifications(l: &Live, clearable: bool) -> bool { l.locked || (l.on && !clearable) }

/// [`hide_notifications`] for this process.
pub fn hide_notification_now() -> bool { hide_notifications(&live(), crate::notify::delivered_clearable()) }

pub fn now_ms() -> i64 { chrono::Utc::now().timestamp_millis() }

// ---------------- notifications while locked ----------------

/// A notification without its content: no sender, subject, preview, title or buttons; a click
/// only brings the window back (mail-notifications.ts:131-143, reminder-notifications.ts:78,
/// os-commands.ts:103-107).
pub fn hide_content(n: crate::notify::Note) -> crate::notify::Note {
    let mail = n.group.starts_with("mail:");
    let title = if !mail {
        if n.group == "reminders" { "Upcoming event".to_string() } else { "Barbican".to_string() }
    } else if n.title.ends_with(" new messages") && n.title.split(' ').next().is_some_and(|c| c.parse::<u32>().is_ok()) {
        n.title.clone()
    } else {
        "New mail".to_string()
    };
    // a reminder's id named its event: two at once are one "you have a reminder"
    let id = if n.group == "reminders" { "reminder-locked".to_string() } else { n.id };
    crate::notify::Note { id, title, body: String::new(), context: String::new(), actions: vec![], ..n }
}

// ---------------- the automation channel (src/automation.rs) ----------------

/// What the test channel may still do while locked: say what state it is in, and act through the
/// window as a person at the keyboard would (clicks, keys and scrolling land on the lock screen,
/// which takes them all). Nothing that reads the app's content or drives it directly: no
/// wv-eval, cal-dump, pim, notes, chips, rx-*, ui, from, cache or shot (a picture of the window).
pub fn automation_allowed(cmd: &str) -> bool {
    matches!(cmd, "applock" | "size" | "click" | "dclick" | "move" | "scroll" | "type" | "key")
}

// ---------------- settings (Password & sign-in > App lock) ----------------

pub const KEY_ON: &str = "appLock.on";
pub const KEY_HELLO: &str = "appLock.hello";
pub const KEY_TIMEOUT: &str = "appLock.timeout";
pub const KEY_LOCK_NOW: &str = "appLock.lockNow";
pub const KEY_CHANGE: &str = "appLock.changePin";

fn timeout_label(t: Timeout) -> String {
    match t {
        Timeout::Session => "When I sign out or restart".into(),
        Timeout::Minutes(m) if m >= 60 && m % 60 == 0 => { let h = m / 60; format!("{h} hour{}", if h == 1 { "" } else { "s" }) }
        Timeout::Minutes(m) => format!("{m} minute{}", if m == 1 { "" } else { "s" }),
    }
}

/// "Lock after": Bulwark's choices, the current value when it is none of them, and "When I sign
/// out or restart" (app-lock-settings.tsx:195-204, controller.ts:1067-1073). "When the screen
/// locks" is not offered: Barbican is not told when Windows locks.
pub fn timeout_choices(current: Timeout) -> Vec<Timeout> {
    let mut m: Vec<i64> = TIMEOUT_CHOICES.to_vec();
    if let Timeout::Minutes(c) = current { if !m.contains(&c) { m.push(c); m.sort(); } }
    let mut out: Vec<Timeout> = m.into_iter().map(Timeout::Minutes).collect();
    out.push(Timeout::Session);
    out
}

fn srow(kind: &str, key: &str, label: &str, desc: &str) -> crate::SRow {
    crate::SRow { kind: kind.into(), key: key.into(), label: label.into(), desc: desc.into(), options: Default::default(), index: 0, on: false, text: "".into() }
}

/// The App lock rows (app-lock-settings.tsx:158-236). `hello_available`: Windows Hello can check
/// the user on this computer; the switch is hidden otherwise, as Bulwark's `hello.shown`. No
/// passkey row: a passkey needs a browser's WebAuthn, which the desktop app doesn't have.
pub fn rows_for(l: &Live, hello_available: bool) -> Vec<crate::SRow> {
    let mut out = vec![srow("section", "", "App lock",
        "Asks for a PIN after a time without use, and every time Barbican starts. While it's locked, nothing of your mail, calendar or contacts shows in the window or in notifications.")];
    let mut on = srow("toggle", KEY_ON, "Lock with a PIN", "The PIN stays on this computer.");
    on.on = l.on;
    out.push(on);
    if l.on && hello_available {
        let mut h = srow("toggle", KEY_HELLO, "Unlock with Windows Hello", "Use your face, fingerprint or Windows PIN. The app's PIN keeps working.");
        h.on = l.hello;
        out.push(h);
    }
    let mut what = srow("value", "", "What this protects",
        "The PIN keeps people out of Barbican's window. It doesn't encrypt anything: someone signed in to your Windows account who uses administrator or developer tools, or a program running as you, can still get around it. Mail keeps arriving while it's locked, and your account on the server isn't affected.");
    what.text = "".into();
    out.push(what);
    if !l.on { return out; }
    let choices = timeout_choices(l.timeout);
    let labels: Vec<slint::SharedString> = choices.iter().map(|t| timeout_label(*t).into()).collect();
    let mut sel = srow("select", KEY_TIMEOUT, "Lock after", "Time without use in the window.");
    sel.options = slint::ModelRc::new(slint::VecModel::from(labels));
    sel.index = choices.iter().position(|t| *t == l.timeout).unwrap_or(0) as i32;
    out.push(sel);
    let mut now = srow("button", KEY_LOCK_NOW, "Lock now", "Locks the window at once.");
    now.text = "Lock now".into();
    out.push(now);
    let mut pin = srow("button", KEY_CHANGE, "PIN", "If you forget it, the only way back is to sign out and sign in again. Your mail on the server stays.");
    pin.text = "Change PIN…".into();
    out.push(pin);
    out
}

/// The timeout a "Lock after" index picks.
pub fn timeout_at(current: Timeout, index: i32) -> Option<Timeout> {
    timeout_choices(current).get(usize::try_from(index).ok()?).copied()
}

// ---------------- the real store ----------------

fn store() -> &'static dyn Backend { crate::secrets::store() }

/// One reader-and-writer of the lock record at a time, across Barbican processes of the profile:
/// two windows trying PINs at once can't each count from the same old number.
struct RecordLock(#[allow(dead_code)] Option<std::fs::File>);

impl RecordLock {
    fn acquire() -> RecordLock {
        let dir = crate::config_dir();
        let _ = std::fs::create_dir_all(&dir);
        let Ok(f) = std::fs::OpenOptions::new().write(true).create(true).truncate(false).open(dir.join("applock.lock")) else { return RecordLock(None) };
        let start = std::time::Instant::now();
        loop {
            if f.try_lock().is_ok() { return RecordLock(Some(f)); }
            // a holder that hangs must not keep the lock screen from answering
            if start.elapsed() > std::time::Duration::from_secs(10) { return RecordLock(None); }
            std::thread::sleep(std::time::Duration::from_millis(50));
        }
    }
}

/// [`try_pin_in`] on the real store, under the [`RecordLock`].
/// A record the sign-ins' markers don't vouch for opens with no PIN, only with signing out.
fn try_pin(pin: &str, now: i64) -> Result<Record, PinError> {
    let _l = RecordLock::acquire();
    let locked = live().locked;
    let stored = read_checked();
    let res = try_pin_with(store(), crate::profile::current(), stored, locked, &mut held(), pin, now);
    if res == Err(PinError::Off) { *live() = Live::OFF; }
    res
}

/// One PIN try against what there is (`stored`: the store as the markers vouch for it):
/// - a stored record: tried there (counted first); counts the store could not keep are kept with
///   the held record, and a store that cannot be written counts in memory instead;
/// - locked, with no record in the store (removed, or unreadable): tried against the held one,
///   counted in memory with the same waits; nothing held is NotSet;
/// - not locked, no entry and no marker: the PIN was turned off in another window (Off).
pub fn try_pin_with(b: &dyn Backend, profile: &str, stored: Stored, locked: bool, held: &mut Option<Record>, pin: &str, now: i64) -> Result<Record, PinError> {
    let missing = stored == Stored::Missing;
    match stored {
        Stored::Some(mut r) => {
            if let Some(h) = held.as_ref().filter(|h| h.fingerprint() == r.fingerprint()) {
                if h.failures > r.failures || h.blocked_until > r.blocked_until {
                    r.failures = r.failures.max(h.failures);
                    r.blocked_until = r.blocked_until.max(h.blocked_until);
                    let _ = save_in(b, profile, &r);
                }
            }
            match try_pin_in(b, profile, pin, now) {
                Err(PinError::NotSaved) => {
                    let mut h = r;
                    let res = try_held(&mut h, pin, now);
                    *held = Some(h);
                    res
                }
                res => {
                    match &res {
                        Ok(r) => *held = Some(r.clone()),
                        Err(_) => if let Stored::Some(r) = load_in(b, profile) { *held = Some(r); },
                    }
                    res
                }
            }
        }
        Stored::Off if !locked => { *held = None; Err(PinError::Off) }
        // locked with no record, or open with the entry removed (put back by the next resync)
        Stored::Missing | Stored::Off | Stored::Unreadable if locked || missing => match held.as_mut() {
            Some(h) => try_held(h, pin, now),
            None => Err(PinError::NotSet),
        },
        _ => Err(PinError::NotSet),
    }
}

/// [`try_pin_in`] on a record in memory: counted first, the same waits.
fn try_held(h: &mut Record, pin: &str, now: i64) -> Result<Record, PinError> {
    h.blocked_until = clamp_wait(h.blocked_until, now);
    if h.blocked_until > now { return Err(PinError::Blocked(h.blocked_until)); }
    h.failures = h.failures.saturating_add(1);
    h.blocked_until = now + wait_after(h.failures);
    if !h.matches(pin) {
        return Err(PinError::Wrong(if h.blocked_until > now { h.blocked_until } else { 0 }));
    }
    h.failures = 0;
    h.blocked_until = 0;
    Ok(h.clone())
}

/// [`blocked_until_in`] on the real store; a wait that needs cutting is written back under the
/// [`RecordLock`].
fn blocked_until(now: i64) -> i64 {
    let (b, p) = (store(), crate::profile::current());
    let until = blocked_until_in(b, p, now);
    if until > 0 && until == now.saturating_add(LONGEST_WAIT_MS) {
        let _l = RecordLock::acquire();
        keep_cut_wait_in(b, p, now);
    }
    // the tries counted in memory (a store that could not be written, or no entry any more)
    let mem = held().as_ref().map_or(0, |h| clamp_wait(h.blocked_until, now));
    if mem > now { until.max(mem) } else { until }
}

/// The lock as the store and the sign-ins' markers say (see [`checked`]).
fn read_checked() -> Stored {
    let m = crate::oauth::lock_markers();
    checked_with(load_in(store(), crate::profile::current()), &m.accepted, m.exists)
}

/// What the process knows after reading `s`: a PIN set (or an entry nobody can open) locks.
pub fn live_for(s: &Stored, now: i64) -> Live {
    match s {
        Stored::Some(r) => Live { on: true, locked: true, hello: r.hello, timeout: r.timeout, last_input_ms: now },
        Stored::Unreadable => Live { on: true, locked: true, ..Live::OFF },
        Stored::Missing | Stored::Off => Live::OFF,
    }
}

/// Read the lock at start: a PIN set means locked. An entry that can't be read, or that the
/// sign-ins' markers don't vouch for, locks too, and only signing out opens it. A record the
/// markers miss on an entry (a sign-in from before) gets them now.
pub fn init() -> Stored {
    let s = read_checked();
    if let Stored::Some(r) = &s {
        crate::oauth::set_lock_marker(Some(&r.fingerprint()));
        *held() = Some(r.clone());
    }
    *live() = live_for(&s, now_ms());
    s
}

/// The window came to the front: another Barbican of this profile may have set a PIN (or a new
/// one) meanwhile. A PIN that is now there locks this window too; the lock never opens this way.
/// Locking every window at once ("Lock now" in another process) is not followed.
pub fn resync() -> Resync {
    let s = read_checked();
    let mut l = live();
    let (next, change) = resynced(*l, &s, now_ms());
    *l = next;
    drop(l);
    match s {
        Stored::Some(r) => *held() = Some(r),
        _ if !next.on => *held() = None,
        _ => {}
    }
    if change == Resync::Repair {
        let _l = RecordLock::acquire();
        let held = held().clone();
        if repair_in(store(), crate::profile::current(), held.as_ref()) {
            // markers on the sign-ins as well (oauth::LOCK_MARKER), from the same copy
            if let Some(r) = &held { crate::oauth::set_lock_marker(Some(&r.fingerprint())); }
            eprintln!("app lock: the PIN's entry was removed from outside Barbican; written back");
        }
    }
    change
}

/// The lock's entry is gone (no tombstone: not turned off by Barbican) while this window is open
/// and holds the record: it is written back from the held copy. Read again first; false when
/// nothing was written.
pub fn repair_in(b: &dyn Backend, profile: &str, held: Option<&Record>) -> bool {
    let Some(r) = held else { return false };
    load_in(b, profile) == Stored::Missing && save_in(b, profile, r)
}

/// What [`resync`] changed in the window.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Resync {
    Same,
    Locked,
    /// The entry was removed while the window is open: [`resync`] writes it back (the lock stays).
    Repair,
}

/// [`resync`]'s rule: a PIN (or an entry nobody can open) that appeared locks. Turned off in
/// another window (the tombstone) is off, only while this window is open. An entry removed from
/// outside Barbican while the window is open is written back (Repair). A locked window never
/// opens this way, whatever the store holds.
pub fn resynced(mut l: Live, s: &Stored, now: i64) -> (Live, Resync) {
    match s {
        Stored::Some(r) => {
            l.hello = r.hello;
            l.timeout = r.timeout;
            if !l.on { return (live_for(s, now), Resync::Locked); }
        }
        Stored::Unreadable if !l.locked => return (live_for(s, now), Resync::Locked),
        Stored::Off if l.on && !l.locked => return (Live { last_input_ms: now, ..Live::OFF }, Resync::Same),
        Stored::Missing if l.on && !l.locked => return (l, Resync::Repair),
        _ => {}
    }
    (l, Resync::Same)
}

/// The PIN and the lock are gone (sign-out of the last account, a forgotten PIN, turned off),
/// with the sign-ins' markers.
/// False when the tombstone could not be written: the lock then stays as it is (deleting the
/// entry instead would read as removed from outside, which an open window writes back), and the
/// caller says so; the next try turns it off.
#[must_use]
pub fn forget() -> bool {
    // the markers first: an entry left without them still opens with its PIN, markers left
    // without an entry would lock with nobody's
    crate::oauth::set_lock_marker(None);
    let ok = { let _l = RecordLock::acquire(); turn_off_in(store(), crate::profile::current(), now_ms()) };
    if ok {
        *held() = None;
        *live() = Live::OFF;
    } else {
        eprintln!("app lock: the PIN could not be removed; it stays on");
    }
    gui::refresh();
    ok
}

/// What to say when [`forget`] could not take the PIN away.
pub const NOT_FORGOTTEN: &str = "Signed out, but the app lock's PIN could not be removed. It stays on: turn it off in Settings, Security.";

// ---------------- the window ----------------

pub use gui::{settings_button, settings_select, settings_toggle, wire};

mod gui {
    use super::*;
    use crate::{AppLock, AppWindow};
    use slint::ComponentHandle;
    use std::cell::{Cell, RefCell};
    use std::sync::atomic::{AtomicU8, Ordering};

    thread_local! {
        static UI: RefCell<Option<slint::Weak<AppWindow>>> = const { RefCell::new(None) };
        static TIMERS: RefCell<Vec<slint::Timer>> = const { RefCell::new(Vec::new()) };
        /// Which Windows Hello prompt is the current one: a late answer to one the user left for
        /// the PIN changes nothing (lock-screen.tsx:151).
        static HELLO_TRY: Cell<u64> = const { Cell::new(0) };
        /// The Windows Hello prompt of this lock was asked for already.
        static HELLO_ASKED: Cell<bool> = const { Cell::new(false) };
        /// What was asked for while locked (the tray's Compose), done once it opens.
        static AFTER_UNLOCK: RefCell<Vec<Box<dyn FnOnce()>>> = const { RefCell::new(Vec::new()) };
    }

    /// Run `f` once the app is unlocked (at once when it is).
    pub fn after_unlock(f: Box<dyn FnOnce()>) {
        if !locked() { f(); return; }
        AFTER_UNLOCK.with(|q| { let mut q = q.borrow_mut(); q.push(f); let n = q.len(); if n > 5 { q.drain(..n - 5); } });
    }
    /// 0 not known yet, 1 Windows Hello can check the user here, 2 it can't.
    static HELLO_AVAILABLE: AtomicU8 = AtomicU8::new(0);

    fn ui() -> Option<AppWindow> { UI.with(|u| u.borrow().as_ref().and_then(|w| w.upgrade())) }

    pub fn hello_available() -> bool { HELLO_AVAILABLE.load(Ordering::Relaxed) == 1 }

    fn profile() -> &'static str { crate::profile::current() }

    /// Wire the lock screen, the PIN dialog and the timer. Before the window is shown: a PIN set
    /// means the first frame is the lock screen.
    pub fn wire(window: &AppWindow) {
        UI.with(|u| *u.borrow_mut() = Some(window.as_weak()));
        super::init();
        apply(window);
        std::thread::spawn(|| {
            HELLO_AVAILABLE.store(if hello::available() { 1 } else { 2 }, Ordering::Relaxed);
            let _ = slint::invoke_from_event_loop(|| if let Some(u) = self::ui() { apply(&u); });
        });

        let g = window.global::<AppLock>();
        g.on_unlock(|pin| unlock_with_pin(zeroize::Zeroizing::new(pin.to_string())));
        g.on_hello(ask_hello);
        g.on_use_pin(|| {
            HELLO_TRY.with(|t| t.set(t.get() + 1));
            if let Some(u) = ui() { let g = u.global::<AppLock>(); g.set_busy(false); g.set_message("".into()); g.set_mode("pin".into()); bump_focus(&u); }
        });
        g.on_forgot(|| if let Some(u) = ui() { u.global::<AppLock>().set_mode("forgot".into()); bump_focus(&u); });
        g.on_forgot_cancel(|| if let Some(u) = ui() { u.global::<AppLock>().set_mode("pin".into()); refresh_blocked(&u); bump_focus(&u); });
        g.on_forgot_confirm(|| if let Some(u) = ui() {
            // a sign-in still on its way would end signed in, after the sign-out, with no PIN
            if u.get_busy() { u.global::<AppLock>().set_message("Barbican is still signing in. Try again in a moment.".into()); return; }
            sign_out_everyone(&u);
        });
        g.on_dlg_edit(|| if let Some(u) = ui() { dialog_check(&u); });
        g.on_dlg_cancel(|| if let Some(u) = ui() { close_dialog(&u); });
        g.on_dlg_save(|| if let Some(u) = ui() { dialog_save(&u); });

        // the periodic check (idle.ts:10 checks every 10 s; the lock screen's countdown wants 1 s)
        let t = slint::Timer::default();
        t.start(slint::TimerMode::Repeated, std::time::Duration::from_secs(1), || {
            let Some(u) = self::ui() else { return };
            let now = now_ms();
            if let Some(at) = input::last_in_window(crate::capture::hwnd_of(u.window()), now) {
                if live().input(at) { lock_ui(&u); return; }
            }
            if live().tick(now) { lock_ui(&u); return; }
            if locked() && u.global::<AppLock>().get_blocked() { refresh_blocked(&u); }
        });
        TIMERS.with(|v| v.borrow_mut().push(t));
    }

    /// Any input event in the window (the winit filter, motion.rs): counts as use, or locks when
    /// the timeout has passed already. True: this event locked, and must not reach the app.
    /// On Windows the pointer only passing over the window is not use (GetLastInputInfo with the
    /// window in front counts what is); elsewhere it is, as Bulwark's pointermove (idle.ts:11).
    pub fn input_event(ev: &slint::winit_030::winit::event::WindowEvent) -> bool {
        use slint::winit_030::winit::event::WindowEvent as E;
        if matches!(ev, E::Focused(true)) {
            match super::resync() {
                Resync::Same | Resync::Repair => {}
                Resync::Locked => slint::Timer::single_shot(std::time::Duration::ZERO, || if let Some(u) = ui() { lock_ui(&u); }),
            }
            return false;
        }
        let moved = matches!(ev, E::CursorMoved { .. }) && !cfg!(windows);
        if !moved && !matches!(ev, E::KeyboardInput { .. } | E::MouseInput { .. } | E::MouseWheel { .. } | E::Touch(_)) { return false; }
        if !live().input(now_ms()) { return false; }
        // after this event: the filter runs inside winit's dispatch
        slint::Timer::single_shot(std::time::Duration::ZERO, || if let Some(u) = ui() { lock_ui(&u); });
        true
    }

    fn bump_focus(u: &AppWindow) {
        let g = u.global::<AppLock>();
        g.set_focus_tick(g.get_focus_tick().wrapping_add(1));
    }

    /// The window as the lock state says.
    pub(super) fn apply(u: &AppWindow) {
        let l = *live();
        let g = u.global::<AppLock>();
        let was = g.get_locked();
        g.set_hello_ready(l.locked && l.hello && hello_available());
        if l.locked && !was {
            // what was open over the app closes: a menu (a popup window draws over everything,
            // the lock screen too) and the PIN dialog. The open composer and message stay as they
            // are under the lock: locking signs nobody out and drops no draft.
            slint::private_unstable_api::re_exports::WindowInner::from_pub(u.window()).close_all_popups();
            close_dialog(u);
            g.set_pin("".into());
            g.set_message("".into());
            g.set_busy(false);
            HELLO_ASKED.with(|a| a.set(false));
            g.set_mode(if g.get_hello_ready() { "hello" } else { "pin" }.into());
            // the mail view goes at once (no capture first), and gives back the OS keyboard focus
            crate::mailview::hide_now(u);
            // delivered toasts named senders and subjects
            crate::notify::clear_delivered();
        }
        if !l.locked && g.get_mode() == "hello" { g.set_mode("pin".into()); }
        g.set_locked(l.locked);
        if l.locked {
            refresh_blocked(u);
            bump_focus(u);
            if g.get_mode() == "hello" && !HELLO_ASKED.with(|a| a.get()) && window_in_front(u) {
                HELLO_ASKED.with(|a| a.set(true));
                ask_hello();
            }
        }
        if u.get_show_settings() && u.get_settings_tab() == "security" { u.invoke_settings_search(u.get_settings_query()); }
    }

    /// From a thread that only knows the lock changed.
    pub fn refresh() {
        let _ = slint::invoke_from_event_loop(|| if let Some(u) = ui() { apply(&u); });
    }

    fn lock_ui(u: &AppWindow) {
        apply(u);
    }

    /// Lock now (the settings button).
    pub fn lock_now(u: &AppWindow) {
        { let mut l = live(); if !l.on { return; } l.locked = true; }
        apply(u);
    }

    fn window_in_front(u: &AppWindow) -> bool {
        input::is_foreground(crate::capture::hwnd_of(u.window()))
    }

    fn refresh_blocked(u: &AppWindow) {
        let g = u.global::<AppLock>();
        let until = blocked_until(now_ms());
        let blocked = until > 0;
        if blocked { g.set_message(blocked_text(until, now_ms()).into()); }
        else if g.get_blocked() { g.set_message("".into()); }
        if g.get_blocked() != blocked {
            g.set_blocked(blocked);
            bump_focus(u);
        }
    }

    fn opened(u: &AppWindow) {
        live().unlock(now_ms());
        let g = u.global::<AppLock>();
        g.set_pin("".into());
        g.set_message("".into());
        g.set_busy(false);
        apply(u);
        for f in AFTER_UNLOCK.with(|q| std::mem::take(&mut *q.borrow_mut())) { f(); }
    }

    /// The slow hash runs off the UI thread; the PIN's copies are wiped when dropped.
    fn unlock_with_pin(pin: zeroize::Zeroizing<String>) {
        let Some(u) = ui() else { return };
        let g = u.global::<AppLock>();
        if pin.is_empty() || g.get_busy() || g.get_blocked() { return; }
        g.set_busy(true);
        g.set_message("".into());
        let w = u.as_weak();
        std::thread::spawn(move || {
            let res = try_pin(&pin, now_ms());
            drop(pin);
            let _ = w.upgrade_in_event_loop(move |u| {
                let g = u.global::<AppLock>();
                match res {
                    Ok(_) => { opened(&u); return; }
                    Err(PinError::Wrong(_)) => g.set_message("That PIN is not right.".into()),
                    Err(PinError::Blocked(_)) => {}
                    Err(PinError::NotSet | PinError::Off) => g.set_message("This PIN can't be read. Choose \"Forgot your PIN?\" to sign out and continue.".into()),
                    Err(PinError::NotSaved) => g.set_message("Unlocking did not work. Try again.".into()),
                }
                g.set_busy(false);
                g.set_pin("".into());
                refresh_blocked(&u);
                bump_focus(&u);
            });
        });
    }

    fn ask_hello() {
        let Some(u) = ui() else { return };
        let g = u.global::<AppLock>();
        if !g.get_hello_ready() { return; }
        let attempt = HELLO_TRY.with(|t| { t.set(t.get() + 1); t.get() });
        g.set_mode("hello".into());
        g.set_busy(true);
        g.set_message("".into());
        let hwnd = crate::capture::hwnd_of(u.window());
        let w = u.as_weak();
        std::thread::spawn(move || {
            let res = hello::verify(hwnd, "Unlock Barbican");
            let _ = w.upgrade_in_event_loop(move |u| {
                if attempt != HELLO_TRY.with(|t| t.get()) || !locked() { return; }
                let g = u.global::<AppLock>();
                if res == hello::Outcome::Verified { opened(&u); return; }
                g.set_busy(false);
                g.set_mode("pin".into());
                g.set_message(res.unlock_message().into());
                if matches!(res, hello::Outcome::Unavailable) { g.set_hello_ready(false); }
                refresh_blocked(&u);
                bump_focus(&u);
            });
        });
    }

    /// Forgot the PIN: every account on this computer signs out, and the PIN goes with the last
    /// one (main.rs `on_sign_out`). The lock screen stays until then, so nothing shows meanwhile.
    fn sign_out_everyone(u: &AppWindow) {
        let g = u.global::<AppLock>();
        if g.get_busy() { return; }
        g.set_busy(true);
        // the other remembered accounts first: the current one's sign-out then is the last, and
        // takes what the device keeps for every account (chats, searches, sender pictures)
        let current = u.get_account().to_string();
        for a in crate::oauth::accounts() {
            let user = a["user"].as_str().unwrap_or("").to_string();
            let server = a["server"].as_str().unwrap_or("").to_string();
            if user.is_empty() || user.eq_ignore_ascii_case(&current) { continue; }
            crate::signout::forget_keyring(&[user.as_str()], &server);
            crate::signout::wipe(crate::signout::Who {
                user: user.clone(), owner: Some(crate::reminders::owner_key(&server, &user)), last: false,
                webview_live: crate::mailview::is_built() || crate::platform::other_of_profile_running(),
            });
        }
        u.set_show_settings(false);
        u.invoke_sign_out();
        // whatever the sign-out could not take, the lock goes now: signing in again asks the password
        if crate::oauth::remembered().is_some() { crate::secrets::delete("session"); }
        let forgotten = super::forget();
        let g = u.global::<AppLock>();
        g.set_busy(false);
        g.set_mode("pin".into());
        apply(u);
        // still locked: "Forgot your PIN?" again takes it
        if !forgotten { g.set_message("Everyone is signed out, but the PIN could not be removed. Choose \"Forgot your PIN?\" to try again.".into()); }
    }

    // ---------------- settings ----------------

    pub fn settings_rows(tab: &str) -> Vec<crate::SRow> {
        if tab != "security" { return vec![]; }
        rows_for(&live(), hello_available())
    }

    /// A switch of the App lock section. True when it was one.
    pub fn settings_toggle(u: &AppWindow, key: &str, on: bool) -> bool {
        match key {
            KEY_ON => { open_dialog(u, if on { "enable" } else { "disable" }); true }
            KEY_HELLO if on => { open_dialog(u, "hello"); true }
            KEY_HELLO => {
                let _l = RecordLock::acquire();
                if let Stored::Some(mut r) = load_in(store(), profile()) {
                    r.hello = false;
                    if save_in(store(), profile(), &r) { live().hello = false; } else { crate::mail::show_toast(u, "That did not work. Try again.", false); }
                }
                true
            }
            _ => false,
        }
    }

    pub fn settings_select(u: &AppWindow, key: &str, index: i32) -> bool {
        if key != KEY_TIMEOUT { return false; }
        let current = live().timeout;
        let Some(t) = timeout_at(current, index) else { return true };
        let _l = RecordLock::acquire();
        if let Stored::Some(mut r) = load_in(store(), profile()) {
            r.timeout = t;
            if save_in(store(), profile(), &r) {
                let mut l = live();
                l.timeout = t;
                l.last_input_ms = now_ms();
            } else {
                crate::mail::show_toast(u, "That did not work. Try again.", false);
            }
        }
        true
    }

    pub fn settings_button(u: &AppWindow, key: &str) -> bool {
        match key {
            KEY_LOCK_NOW => { lock_now(u); true }
            KEY_CHANGE => { open_dialog(u, "change"); true }
            _ => false,
        }
    }

    fn open_dialog(u: &AppWindow, mode: &str) {
        let g = u.global::<AppLock>();
        let (title, body, current, new, save) = match mode {
            "enable" => ("Choose a PIN", "You need it each time the app locks and after a restart.", false, true, "Save"),
            "change" => ("Change the PIN", "", true, true, "Save"),
            "disable" => ("Turn off the app lock?", "Enter your PIN to turn it off.", true, false, "Turn off"),
            _ => ("Turn on Windows Hello", "Enter the app's PIN. Windows then asks you to confirm with Windows Hello.", true, false, "Turn on"),
        };
        g.set_dlg_title(title.into());
        g.set_dlg_body(body.into());
        g.set_ask_current(current);
        g.set_ask_new(new);
        g.set_current("".into());
        g.set_new_pin("".into());
        g.set_repeat("".into());
        g.set_dlg_hint(HINT.into());
        g.set_dlg_error("".into());
        g.set_dlg_busy(false);
        g.set_dlg_save_label(save.into());
        g.set_dlg(mode.into());
        dialog_check(u);
    }

    fn close_dialog(u: &AppWindow) {
        let g = u.global::<AppLock>();
        if g.get_dlg_busy() && !locked() { return; }
        g.set_dlg("".into());
        g.set_current("".into());
        g.set_new_pin("".into());
        g.set_repeat("".into());
        g.set_dlg_busy(false);
        // the switches show what is stored again
        if u.get_show_settings() { u.invoke_settings_search(u.get_settings_query()); }
    }

    fn dialog_check(u: &AppWindow) {
        let g = u.global::<AppLock>();
        let mut ready = !g.get_ask_current() || !g.get_current().is_empty();
        let mut note = "";
        if g.get_ask_new() {
            let (ok, msg) = new_pin_check(&g.get_new_pin(), &g.get_repeat());
            ready &= ok;
            note = msg;
        }
        // a PIN error from Save stays until something is typed again
        g.set_dlg_error(note.into());
        g.set_dlg_ready(ready);
    }

    fn dialog_save(u: &AppWindow) {
        let g = u.global::<AppLock>();
        dialog_check(u);
        if !g.get_dlg_ready() || g.get_dlg_busy() { return; }
        let mode = g.get_dlg().to_string();
        let (current, new) = (zeroize::Zeroizing::new(g.get_current().to_string()), zeroize::Zeroizing::new(g.get_new_pin().to_string()));
        let hwnd = crate::capture::hwnd_of(u.window());
        g.set_dlg_busy(true);
        g.set_dlg_error("".into());
        let w = u.as_weak();
        std::thread::spawn(move || {
            let res = run_dialog(&mode, &current, &new, hwnd);
            drop((current, new));
            let _ = w.upgrade_in_event_loop(move |u| {
                let g = u.global::<AppLock>();
                g.set_dlg_busy(false);
                match res {
                    Ok(l) => {
                        // the app locked meanwhile (time without use): it stays locked with what is
                        // stored now, so the new PIN opens it; a lock turned off opens it
                        let was_locked = {
                            let mut live = live();
                            let was = live.locked;
                            *live = Live { locked: was && l.on, last_input_ms: now_ms(), ..l };
                            was
                        };
                        if was_locked { apply(&u); } else { close_dialog(&u); }
                    }
                    Err(e) => { g.set_current("".into()); g.set_dlg_error(e.into()); g.set_dlg_ready(false); }
                }
            });
        });
    }

    fn pin_error_text(e: PinError) -> &'static str {
        match e {
            PinError::Wrong(_) => "That PIN is not right.",
            PinError::Blocked(_) => "Too many tries. Try again later.",
            PinError::Off => "The PIN was turned off in another Barbican window.",
            _ => "That did not work. Try again.",
        }
    }

    /// The dialog's work (the slow hash and Windows Hello), off the UI thread. What the lock is
    /// afterwards, or what to say.
    fn run_dialog(mode: &str, current: &str, new: &str, hwnd: isize) -> Result<Live, &'static str> {
        let (b, p) = (store(), profile());
        let now = now_ms();
        // the record and the sign-ins' markers with its fingerprint (oauth::LOCK_MARKER)
        const FAILED: &str = "That did not work. Try again.";
        let live_of = |r: &Record| Live { on: true, locked: false, hello: r.hello, timeout: r.timeout, last_input_ms: now };
        // a first PIN: the entry, then the markers (an entry without them still opens with its
        // PIN); Windows Hello turned on keeps the PIN's fingerprint, so only the entry is written
        let saved = |r: &Record, mark: bool| -> Result<Live, &'static str> {
            let ok = { let _l = RecordLock::acquire(); save_in(b, p, r) };
            if !ok { return Err(FAILED); }
            if mark { crate::oauth::set_lock_marker(Some(&r.fingerprint())); }
            *held() = Some(r.clone());
            Ok(live_of(r))
        };
        match mode {
            "enable" => {
                if !new_pin_check(new, new).0 { return Err("That PIN is too easy to guess. Avoid repeats, runs and common choices."); }
                let r = Record::new(new, PBKDF2_ITERATIONS).ok_or(FAILED)?;
                saved(&r, true)
            }
            "change" => {
                let old = try_pin(current, now).map_err(pin_error_text)?;
                if !new_pin_check(new, new).0 { return Err("That PIN is too easy to guess. Avoid repeats, runs and common choices."); }
                // a new PIN takes Windows Hello off: it is turned on with the PIN (Bulwark's helloOff)
                let r = Record { timeout: old.timeout, hello: false, ..Record::new(new, PBKDF2_ITERATIONS).ok_or(FAILED)? };
                // the markers vouch for both PINs while the entry changes, so a write that fails at
                // any step leaves a stored PIN that opens: both, then the entry, then the new alone
                let (was, next) = (old.fingerprint(), r.fingerprint());
                if !crate::oauth::set_lock_marker(Some(&both_markers(&was, &next))) {
                    crate::oauth::set_lock_marker(Some(&was));
                    return Err(FAILED);
                }
                if !{ let _l = RecordLock::acquire(); save_in(b, p, &r) } {
                    crate::oauth::set_lock_marker(Some(&was));
                    return Err(FAILED);
                }
                // left with both when this fails: the next start writes the new one alone (init)
                crate::oauth::set_lock_marker(Some(&next));
                *held() = Some(r.clone());
                Ok(live_of(&r))
            }
            "disable" => {
                let r = try_pin(current, now).map_err(pin_error_text)?;
                // the markers first, then the entry: neither failure leaves a PIN that cannot open
                if !crate::oauth::set_lock_marker(None) {
                    crate::oauth::set_lock_marker(Some(&r.fingerprint()));
                    return Err(FAILED);
                }
                // the tombstone tells other windows holding the PIN that it was turned off
                if !{ let _l = RecordLock::acquire(); turn_off_in(b, p, now) } {
                    crate::oauth::set_lock_marker(Some(&r.fingerprint()));
                    return Err(FAILED);
                }
                *held() = None;
                Ok(Live::OFF)
            }
            _ => {
                let mut r = try_pin(current, now).map_err(pin_error_text)?;
                let res = hello::verify(hwnd, "Turn on Windows Hello for Barbican");
                if res != hello::Outcome::Verified { return Err(res.enable_message()); }
                r.hello = true;
                saved(&r, false)
            }
        }
    }

    #[cfg(windows)]
    mod input {
        use windows::Win32::System::SystemInformation::GetTickCount;
        use windows::Win32::UI::Input::KeyboardAndMouse::{GetLastInputInfo, LASTINPUTINFO};
        use windows::Win32::UI::WindowsAndMessaging::GetForegroundWindow;

        pub fn is_foreground(hwnd: isize) -> bool {
            hwnd != 0 && unsafe { GetForegroundWindow() }.0 as isize == hwnd
        }

        /// When the last input was, while this window is in front: the mail view is a child
        /// window whose clicks, keys and scrolling never pass through the app's own events.
        pub fn last_in_window(hwnd: isize, now: i64) -> Option<i64> {
            if !is_foreground(hwnd) { return None; }
            let mut info = LASTINPUTINFO { cbSize: std::mem::size_of::<LASTINPUTINFO>() as u32, dwTime: 0 };
            if !unsafe { GetLastInputInfo(&mut info) }.as_bool() { return None; }
            let ago = unsafe { GetTickCount() }.wrapping_sub(info.dwTime) as i64;
            Some(now - ago)
        }
    }

    /// Elsewhere the app's own input events are all there is, and the window counts as in front.
    #[cfg(not(windows))]
    mod input {
        pub fn is_foreground(_hwnd: isize) -> bool { true }
        pub fn last_in_window(_hwnd: isize, _now: i64) -> Option<i64> { None }
    }

    pub(super) mod hello {
        #[derive(Clone, Copy, Debug, PartialEq, Eq)]
        pub enum Outcome { Verified, Cancelled, Unavailable, Busy, Failed }

        impl Outcome {
            /// On the lock screen (locales/en appLockHello.failed / gone).
            pub fn unlock_message(self) -> &'static str {
                match self {
                    Outcome::Cancelled => "",
                    Outcome::Unavailable => "Windows Hello no longer unlocks the app on this computer. Enter your PIN.",
                    Outcome::Busy => "Windows Hello is busy or locked. Try again later.",
                    _ => "Windows Hello didn't unlock the app. Enter your PIN.",
                }
            }
            /// In the dialog that turns it on.
            pub fn enable_message(self) -> &'static str {
                match self {
                    Outcome::Cancelled => "Windows Hello was cancelled.",
                    Outcome::Unavailable => "Windows Hello isn't available on this computer.",
                    Outcome::Busy => "Windows Hello is busy or locked. Try again later.",
                    _ => "Windows Hello could not be turned on. Try again.",
                }
            }
        }

        #[cfg(windows)]
        pub fn available() -> bool {
            use windows::Security::Credentials::UI::{UserConsentVerifier, UserConsentVerifierAvailability};
            UserConsentVerifier::CheckAvailabilityAsync().and_then(|op| op.join()).is_ok_and(|a| a == UserConsentVerifierAvailability::Available)
        }

        /// Windows asks the user (face, fingerprint or Windows PIN) over `hwnd`. Blocks: a worker thread only.
        #[cfg(windows)]
        pub fn verify(hwnd: isize, message: &str) -> Outcome {
            use windows::core::HSTRING;
            use windows::Security::Credentials::UI::{UserConsentVerificationResult as R, UserConsentVerifier};
            use windows::Win32::Foundation::HWND;
            use windows::Win32::System::WinRT::IUserConsentVerifierInterop;
            let res = (|| -> windows::core::Result<R> {
                let interop = windows::core::factory::<UserConsentVerifier, IUserConsentVerifierInterop>()?;
                // the desktop way: the prompt belongs to the window (no CoreWindow here)
                let op: windows_future::IAsyncOperation<R> = unsafe { interop.RequestVerificationForWindowAsync(HWND(hwnd as *mut core::ffi::c_void), &HSTRING::from(message))? };
                op.join()
            })();
            match res {
                Ok(R::Verified) => Outcome::Verified,
                Ok(R::Canceled) => Outcome::Cancelled,
                Ok(R::DeviceNotPresent) | Ok(R::NotConfiguredForUser) | Ok(R::DisabledByPolicy) => Outcome::Unavailable,
                Ok(R::DeviceBusy) | Ok(R::RetriesExhausted) => Outcome::Busy,
                _ => Outcome::Failed,
            }
        }

        #[cfg(not(windows))]
        pub fn available() -> bool { false }
        #[cfg(not(windows))]
        pub fn verify(_hwnd: isize, _message: &str) -> Outcome { Outcome::Unavailable }
    }
}

pub use gui::{after_unlock, input_event, settings_rows};

#[cfg(test)]
mod tests {
    use super::*;
    use crate::secrets::testkit::Fake;

    const FAST: u32 = 1_000; // the tests' rounds; the app's are PBKDF2_ITERATIONS

    fn set(b: &Fake, pin: &str) -> Record {
        let r = Record::new(pin, FAST).unwrap();
        assert!(save_in(b, "default", &r));
        r
    }

    #[test]
    fn the_pin_is_never_stored_and_verifies() {
        let b = Fake::default();
        set(&b, "k7#mpz");
        let stored = b.new.borrow().get("Barbican/default/applock").cloned().unwrap();
        assert!(!stored.contains("k7#mpz"));
        let Stored::Some(r) = load_in(&b, "default") else { panic!() };
        assert!(r.matches("k7#mpz"));
        assert!(!r.matches("k7#mpZ"));
        assert!(!r.matches(""));
        // salted: the same PIN twice gives two hashes
        let (a, c) = (Record::new("k7#mpz", FAST).unwrap(), Record::new("k7#mpz", FAST).unwrap());
        assert_ne!(a.hash, c.hash);
        assert_eq!(Record::from_json(&r.to_json()), Some(r));
    }

    #[test]
    fn the_same_pin_typed_another_way_matches() {
        // full-width digits and letters are the same PIN after NFKC (crypto.ts:41)
        let r = Record::new("4821x", FAST).unwrap();
        assert!(r.matches("\u{FF14}\u{FF18}\u{FF12}\u{FF11}x"));
    }

    #[test]
    fn a_damaged_entry_is_unreadable_not_missing() {
        let b = Fake::default();
        assert_eq!(load_in(&b, "default"), Stored::Missing);
        crate::secrets::save(&b, "default", SECRET, "{\"kdf\":\"pbkdf2-sha256\"}");
        assert_eq!(load_in(&b, "default"), Stored::Unreadable);
        assert_eq!(try_pin_in(&b, "default", "anything", 0), Err(PinError::NotSet));
    }

    #[test]
    fn pin_rules_follow_bulwark() {
        assert!(!pin_long_enough("1234"));
        assert!(pin_long_enough("12345"));
        for obvious in ["12345", "54321", "11111", "aaaaa", "abcde", "78901", "121212", "123123", "abcabc", "qwerty", "Password", "000000"] {
            assert!(pin_is_obvious(obvious), "{obvious}");
        }
        for fine in ["k7#mpz", "40917", "horse battery", "93846"] {
            assert!(!pin_is_obvious(fine), "{fine}");
        }
        assert_eq!(new_pin_check("", ""), (false, ""));
        assert_eq!(new_pin_check("123", "").1, "That PIN is too short.");
        assert!(new_pin_check("11111", "11111").1.contains("too easy"));
        assert_eq!(new_pin_check("40917", "4091").1, "The two PINs are not the same.");
        assert_eq!(new_pin_check("40917", ""), (false, ""));
        assert_eq!(new_pin_check("40917", "40917"), (true, ""));
    }

    #[test]
    fn wrong_pins_wait_longer_and_never_wipe() {
        assert_eq!((1..=4).map(wait_after).collect::<Vec<_>>(), vec![0, 0, 0, 0]);
        assert_eq!(wait_after(5), 30_000);
        assert_eq!(wait_after(6), 60_000);
        assert_eq!(wait_after(7), 120_000);
        assert_eq!(wait_after(12), LONGEST_WAIT_MS);
        assert_eq!(wait_after(u32::MAX), LONGEST_WAIT_MS);
    }

    #[test]
    fn tries_are_counted_before_the_check_and_survive_a_restart() {
        let b = Fake::default();
        set(&b, "40917");
        let mut now = 1_000_000;
        for _ in 0..4 { assert_eq!(try_pin_in(&b, "default", "00000", now), Err(PinError::Wrong(0))); }
        // the fifth wrong one waits 30 s
        assert_eq!(try_pin_in(&b, "default", "00000", now), Err(PinError::Wrong(now + 30_000)));
        // the store says so, as a new process reads it
        let Stored::Some(r) = load_in(&b, "default") else { panic!() };
        assert_eq!((r.failures, r.blocked_until), (5, now + 30_000));
        assert_eq!(blocked_until_in(&b, "default", now), now + 30_000);
        // while blocked even the right PIN is not checked, and nothing is counted
        assert_eq!(try_pin_in(&b, "default", "40917", now + 10), Err(PinError::Blocked(now + 30_000)));
        now += 30_000;
        assert_eq!(try_pin_in(&b, "default", "00000", now), Err(PinError::Wrong(now + 60_000)));
        now += 60_000;
        assert!(try_pin_in(&b, "default", "40917", now).is_ok());
        let Stored::Some(r) = load_in(&b, "default") else { panic!() };
        assert_eq!((r.failures, r.blocked_until), (0, 0));
    }

    #[test]
    fn a_try_that_cannot_be_counted_is_not_checked() {
        let b = Fake::default();
        set(&b, "40917");
        b.fail_writes.set(true);
        assert_eq!(try_pin_in(&b, "default", "40917", 5), Err(PinError::NotSaved));
    }

    #[test]
    fn the_pin_is_per_profile() {
        let b = Fake::default();
        assert!(save_in(&b, "dev", &Record::new("40917", FAST).unwrap()));
        assert_eq!(load_in(&b, "default"), Stored::Missing);
        assert!(b.new.borrow().contains_key("Barbican/dev/applock"));
        forget_in(&b, "dev");
        assert_eq!(load_in(&b, "dev"), Stored::Missing);
    }

    #[test]
    fn idle_locks_after_the_timeout() {
        let min = 60_000;
        assert!(!expired(0, 9 * min, Timeout::Minutes(10)));
        assert!(expired(0, 10 * min, Timeout::Minutes(10)));
        assert!(!expired(0, 1_000 * min, Timeout::Session));
        let mut l = Live { on: true, locked: false, hello: false, timeout: Timeout::Minutes(5), last_input_ms: 0 };
        assert!(!l.tick(4 * min));
        assert!(!l.input(4 * min));
        assert!(!l.tick(8 * min));
        assert!(l.tick(9 * min));
        assert!(l.locked);
        // locked: input does nothing until the PIN
        assert!(!l.input(10 * min));
        l.unlock(10 * min);
        assert!(!l.locked);
        // input after the time has passed locks rather than counts
        assert!(l.input(16 * min));
        assert!(l.locked);
        // an input stamp from before the last one does not move the clock back
        l.unlock(20 * min);
        assert!(!l.input(19 * min));
        assert_eq!(l.last_input_ms, 20 * min);
        // no lock, no locking
        let mut off = Live::OFF;
        assert!(!off.tick(i64::MAX / 2) && !off.input(i64::MAX / 2));
    }

    #[test]
    fn blocked_text_counts_down() {
        assert_eq!(blocked_text(30_000, 0), "Too many tries. Try again in 30 seconds.");
        assert_eq!(blocked_text(1_000, 0), "Too many tries. Try again in 1 second.");
        assert_eq!(blocked_text(61_000, 0), "Too many tries. Try again in 2 minutes.");
        assert_eq!(blocked_text(60_000, 0), "Too many tries. Try again in 1 minute.");
    }

    #[test]
    fn locked_notifications_say_nothing() {
        use crate::notify::{mail_note, reminder_note, NewMail};
        let m = |id: &str| NewMail { id: id.into(), mailbox: "inbox".into(), sender: "Ada Example".into(), subject: "Payroll".into(), preview: "Your code is 1234".into() };
        let one = hide_content(mail_note(&[m("e1")], "user@example.com", true, true).unwrap());
        assert_eq!((one.title.as_str(), one.body.as_str(), one.context.as_str()), ("New mail", "", ""));
        assert!(one.actions.is_empty());
        assert!(one.sound);
        let two = hide_content(mail_note(&[m("e1"), m("e2")], "user@example.com", false, true).unwrap());
        assert_eq!((two.title.as_str(), two.body.as_str()), ("2 new messages", ""));
        let r = hide_content(reminder_note("ev1", 5, "Doctor", "In 10 min", true));
        assert_eq!((r.id.as_str(), r.title.as_str(), r.body.as_str(), r.context.as_str()), ("reminder-locked", "Upcoming event", "", ""));
        // nothing of the original anywhere
        for n in [one, two, r] {
            let all = format!("{n:?}");
            assert!(!all.contains("Ada") && !all.contains("Payroll") && !all.contains("1234") && !all.contains("Doctor") && !all.contains("ev1"), "{all}");
        }
    }

    #[test]
    fn settings_rows_follow_the_state() {
        let keys = |rows: &[crate::SRow]| rows.iter().map(|r| r.key.to_string()).filter(|k| !k.is_empty()).collect::<Vec<_>>();
        let off = rows_for(&Live::OFF, true);
        assert_eq!(off[0].label, "App lock");
        assert_eq!(keys(&off), vec![KEY_ON]);
        assert!(!off[1].on);
        let on = Live { on: true, timeout: Timeout::Minutes(15), ..Live::OFF };
        let rows = rows_for(&on, true);
        assert_eq!(keys(&rows), vec![KEY_ON, KEY_HELLO, KEY_TIMEOUT, KEY_LOCK_NOW, KEY_CHANGE]);
        assert!(!rows.iter().any(|r| r.label.contains("asskey")));
        // no Windows Hello here: no switch
        assert_eq!(keys(&rows_for(&on, false)), vec![KEY_ON, KEY_TIMEOUT, KEY_LOCK_NOW, KEY_CHANGE]);
        let sel = rows.iter().find(|r| r.key == KEY_TIMEOUT).unwrap();
        use slint::Model;
        let opts: Vec<String> = sel.options.iter().map(|s| s.to_string()).collect();
        assert_eq!(opts, ["1 minute", "5 minutes", "10 minutes", "15 minutes", "30 minutes", "1 hour", "4 hours", "When I sign out or restart"]);
        assert_eq!(opts[sel.index as usize], "15 minutes");
        assert_eq!(timeout_at(Timeout::Minutes(15), 7), Some(Timeout::Session));
        assert_eq!(timeout_at(Timeout::Minutes(15), 0), Some(Timeout::Minutes(1)));
        assert_eq!(timeout_at(Timeout::Minutes(15), 99), None);
        // a value that is none of the choices is shown, in order
        assert_eq!(timeout_choices(Timeout::Minutes(20))[4], Timeout::Minutes(20));
    }

    #[test]
    fn the_lock_screen_covers_everything() {
        // the window: the mail view (a native WebView child) is hidden while locked, the lock
        // screen is drawn last (over every dialog), and keys never reach the app under it
        let app = include_str!("../ui/app.slint");
        let body = app.split("out property <bool> body-visible:").nth(1).unwrap().split(';').next().unwrap();
        assert!(body.contains("!AppLock.locked"), "{body}");
        let tail = app.trim_end().trim_end_matches('}').trim_end();
        assert!(tail.ends_with("if AppLock.locked: LockScreen { x: 0; y: 0; width: root.width; height: root.height; app-busy: root.busy; }"));
        assert!(app.contains("if AppLock.locked { AppLock.focus-tick += 1; return accept; }"));
        let lock = include_str!("../ui/applock.slint");
        let screen = lock.split("export component LockScreen").nth(1).unwrap();
        // opaque, takes scrolling and every key, and keeps Tab inside
        assert!(screen.contains("background: Theme.ground;"));
        assert!(screen.contains("scroll-event(e) => { accept }"));
        assert!(screen.contains("key-pressed(e) => { accept }"));
        assert!(screen.contains("e.text == Key.Tab || e.text == Key.Backtab"));
        // "Forgot your PIN?" waits for a sign-in that is still running
        assert!(screen.contains("TouchArea { enabled: !root.app-busy;"));
    }

    /// The window's own children (elements, not properties or handlers), each with its first line.
    fn top_level_elements(app: &str) -> Vec<(String, String)> {
        let start = app.find("export component AppWindow").unwrap();
        let (mut depth, mut out) = (0i32, Vec::new());
        let lines: Vec<&str> = app[start..].lines().collect();
        for (i, line) in lines.iter().enumerate() {
            // strings and comments don't count
            let mut code = String::new();
            let (mut in_str, mut slash) = (false, false);
            for c in line.chars() {
                if c == '"' { in_str = !in_str; slash = false; continue; }
                if in_str { continue; }
                if c == '/' && slash { code.pop(); break; }
                slash = c == '/';
                code.push(c);
            }
            if depth == 1 {
                if let Some(k) = code.find('{') {
                    let ty = code[..k].split_whitespace().last().unwrap_or("");
                    if ty.starts_with(|c: char| c.is_ascii_uppercase()) {
                        out.push((ty.to_string(), lines[i..(i + 4).min(lines.len())].join("\n")));
                    }
                }
            }
            depth += code.matches('{').count() as i32 - code.matches('}').count() as i32;
            if i > 0 && depth == 0 { break; }
        }
        out
    }

    #[test]
    fn nothing_under_the_lock_screen_is_drawn_or_read_out() {
        // a screen reader walks hidden-behind elements too: everything but the lock's own parts
        // is made invisible while locked
        let app = include_str!("../ui/app.slint");
        let kids = top_level_elements(app);
        assert!(kids.iter().any(|(t, _)| t == "FocusScope"), "{kids:?}");
        assert!(kids.len() > 15, "{}", kids.len());
        for (ty, head) in &kids {
            // a Timer draws nothing; the context menu closes on lock (gui::apply); the warm-up
            // tile shows only the name; the PIN dialog and the lock screen are the lock
            if ty == "Timer" || ty == "PopupWindow" || ty == "PinDialog" || ty == "LockScreen" || head.contains("root.motion-warm:") { continue; }
            assert!(head.contains("visible: !AppLock.locked;"), "{ty}: {head}");
        }
    }

    #[test]
    fn a_removed_or_replaced_entry_stays_locked() {
        let r = Record::new("k7#mpz", FAST).unwrap();
        let other = Record::new("k7#mpz", FAST).unwrap();
        let mine = vec![r.fingerprint()];
        assert_ne!(r.fingerprint(), other.fingerprint());
        assert_eq!(checked(Stored::Some(r.clone()), &mine), Stored::Some(r.clone()));
        // no markers (a sign-in from before, or none at all): the entry alone decides
        assert_eq!(checked(Stored::Some(r.clone()), &[]), Stored::Some(r.clone()));
        assert_eq!(checked(Stored::Missing, &[]), Stored::Missing);
        // deleted, or overwritten with a PIN someone knows
        assert_eq!(checked(Stored::Missing, &mine), Stored::Unreadable);
        assert_eq!(checked(Stored::Some(other), &mine), Stored::Unreadable);
        assert_eq!(checked(Stored::Unreadable, &[]), Stored::Unreadable);
        let l = live_for(&Stored::Unreadable, 5);
        assert!(l.on && l.locked);
        let l = live_for(&Stored::Some(r), 5);
        assert!(l.on && l.locked && l.last_input_ms == 5);
        assert_eq!(live_for(&Stored::Missing, 5), Live::OFF);
    }

    #[test]
    fn a_store_that_cannot_be_read_locks() {
        let b = Fake::default();
        set(&b, "k7#mpz");
        b.fail_reads.set(true);
        assert_eq!(load_in(&b, "default"), Stored::Unreadable);
        assert_eq!(try_pin_in(&b, "default", "k7#mpz", 0), Err(PinError::NotSet));
        b.fail_reads.set(false);
        assert!(matches!(load_in(&b, "default"), Stored::Some(_)));
    }

    #[test]
    fn a_weak_or_foreign_record_is_refused() {
        let r = Record::new("k7#mpz", FAST).unwrap();
        let json = r.to_json();
        assert!(Record::parse(&json, FAST).is_some());
        // fewer rounds than the app writes
        assert!(Record::parse(&json, PBKDF2_ITERATIONS).is_none());
        let mut v: Value = serde_json::from_str(&json).unwrap();
        v["iterations"] = json!(PBKDF2_ITERATIONS);
        assert!(Record::parse(&v.to_string(), PBKDF2_ITERATIONS).is_some());
        v["iterations"] = json!(1);
        assert!(Record::parse(&v.to_string(), PBKDF2_ITERATIONS).is_none());
        // a salt of another length
        let mut v: Value = serde_json::from_str(&json).unwrap();
        v["salt"] = json!(b64().encode([7u8; 8]));
        assert!(Record::parse(&v.to_string(), FAST).is_none());
    }

    #[test]
    fn a_wait_far_in_the_future_is_cut_to_the_longest() {
        let b = Fake::default();
        let mut r = set(&b, "k7#mpz");
        let now = 1_000_000;
        r.blocked_until = i64::MAX / 2;
        assert!(save_in(&b, "default", &r));
        assert_eq!(blocked_until_in(&b, "default", now), now + LONGEST_WAIT_MS);
        // the cut wait is kept: read later, it does not move along
        keep_cut_wait_in(&b, "default", now);
        assert_eq!(blocked_until_in(&b, "default", now + 10), now + LONGEST_WAIT_MS);
        assert_eq!(try_pin_in(&b, "default", "k7#mpz", now), Err(PinError::Blocked(now + LONGEST_WAIT_MS)));
        // and it runs out like any other
        assert!(try_pin_in(&b, "default", "k7#mpz", now + LONGEST_WAIT_MS + 1).is_ok());
    }

    #[test]
    fn a_pin_being_changed_opens_with_either_fingerprint() {
        // the markers carry both while the entry changes (gui::run_dialog), so whichever entry
        // is stored when a write fails, its PIN opens
        let (old, new) = (Record::new("k7#mpz", FAST).unwrap(), Record::new("q9!wre", FAST).unwrap());
        let both: Vec<String> = both_markers(&old.fingerprint(), &new.fingerprint()).split_whitespace().map(String::from).collect();
        assert_eq!(both.len(), 2);
        assert_eq!(checked(Stored::Some(old.clone()), &both), Stored::Some(old.clone()));
        assert_eq!(checked(Stored::Some(new.clone()), &both), Stored::Some(new));
        assert_eq!(checked(Stored::Some(old), &both[1..]), Stored::Unreadable);
    }

    #[test]
    fn focus_follows_another_windows_pin() {
        let r = Record::new("k7#mpz", FAST).unwrap();
        let on = live_for(&Stored::Some(r.clone()), 0);
        // a PIN set elsewhere locks
        assert_eq!(resynced(Live::OFF, &Stored::Some(r.clone()), 9).1, Resync::Locked);
        assert_eq!(resynced(Live::OFF, &Stored::Unreadable, 9).1, Resync::Locked);
        // the same PIN changes nothing; an open window stays open
        let open = Live { locked: false, ..on };
        assert_eq!(resynced(open, &Stored::Some(r), 9), (open, Resync::Same));
        // turned off in another window (the tombstone) while open: off
        assert_eq!(resynced(open, &Stored::Off, 9), (Live { last_input_ms: 9, ..Live::OFF }, Resync::Same));
        assert_eq!(resynced(Live::OFF, &Stored::Off, 9), (Live::OFF, Resync::Same));
        assert_eq!(resynced(Live::OFF, &Stored::Missing, 9), (Live::OFF, Resync::Same));
        // removed from outside while open: written back, the lock stays on
        assert_eq!(resynced(open, &Stored::Missing, 9), (open, Resync::Repair));
        // a locked window never opens, whatever the store holds
        assert_eq!(resynced(on, &Stored::Missing, 9), (on, Resync::Same));
        assert_eq!(resynced(on, &Stored::Off, 9), (on, Resync::Same));
        assert_eq!(resynced(on, &Stored::Unreadable, 9), (on, Resync::Same));
    }

    #[test]
    fn a_removed_entry_is_written_back_and_a_turned_off_one_is_not() {
        let b = Fake::default();
        let r = set(&b, "k7#mpz");
        // removed from outside: put back from the held copy, once
        forget_in(&b, "default");
        assert!(repair_in(&b, "default", Some(&r)));
        assert_eq!(load_in(&b, "default"), Stored::Some(r.clone()));
        assert!(!repair_in(&b, "default", Some(&r)));
        assert!(!repair_in(&b, "default", None));
        // turned off by Barbican: the tombstone stays, and reads as off
        assert!(turn_off_in(&b, "default", 5));
        assert_eq!(load_in(&b, "default"), Stored::Off);
        assert!(!repair_in(&b, "default", Some(&r)));
        assert_eq!(live_for(&Stored::Off, 5), Live::OFF);
        // a tombstone where the markers say a lock is: locked, nobody's PIN
        assert_eq!(checked(Stored::Off, &[r.fingerprint()]), Stored::Unreadable);
        assert_eq!(checked(Stored::Off, &[]), Stored::Off);
        // turning the PIN on again replaces it
        let n = set(&b, "q9!wre");
        assert_eq!(load_in(&b, "default"), Stored::Some(n));
    }

    #[test]
    fn a_tombstone_while_locked_keeps_the_lock_and_the_pin() {
        let b = Fake::default();
        let r = set(&b, "k7#mpz");
        let mut held = Some(r);
        assert!(turn_off_in(&b, "default", 5));
        let stored = || checked(load_in(&b, "default"), &[]);
        assert_eq!(stored(), Stored::Off);
        assert_eq!(try_pin_with(&b, "default", stored(), true, &mut held, "wrong1", 10), Err(PinError::Wrong(0)));
        assert!(try_pin_with(&b, "default", stored(), true, &mut held, "k7#mpz", 10).is_ok());
        // open (the settings dialog): it was turned off
        assert_eq!(try_pin_with(&b, "default", stored(), false, &mut held, "k7#mpz", 10), Err(PinError::Off));
        assert!(held.is_none());
    }

    #[test]
    fn removing_the_entries_while_locked_keeps_the_lock_and_the_pin() {
        let b = Fake::default();
        let r = set(&b, "k7#mpz");
        let mut held = Some(r.clone());
        // the lock entry, the session and the accounts are removed: nothing in the store is left
        forget_in(&b, "default");
        let stored = || checked(load_in(&b, "default"), &[]);
        assert_eq!(stored(), Stored::Missing);
        // wrong PINs still count, in memory, with the same waits
        for _ in 0..FREE_FAILURES {
            assert_eq!(try_pin_with(&b, "default", stored(), true, &mut held, "wrong1", 1_000), Err(PinError::Wrong(0)));
        }
        let wait = 1_000 + wait_after(FREE_FAILURES + 1);
        assert_eq!(try_pin_with(&b, "default", stored(), true, &mut held, "wrong1", 1_000), Err(PinError::Wrong(wait)));
        assert_eq!(try_pin_with(&b, "default", stored(), true, &mut held, "k7#mpz", 1_001), Err(PinError::Blocked(wait)));
        // the right PIN opens after the wait
        assert!(try_pin_with(&b, "default", stored(), true, &mut held, "k7#mpz", wait).is_ok());
        assert_eq!(held.as_ref().unwrap().failures, 0);
        // an unreadable store while locked: the same
        b.fail_reads.set(true);
        assert_eq!(load_in(&b, "default"), Stored::Unreadable);
        assert!(try_pin_with(&b, "default", Stored::Unreadable, true, &mut held, "k7#mpz", wait).is_ok());
        // nothing held: no PIN opens
        assert_eq!(try_pin_with(&b, "default", Stored::Missing, true, &mut None, "k7#mpz", wait), Err(PinError::NotSet));
        // open (the settings dialog), entry removed: the held copy still answers
        assert!(try_pin_with(&b, "default", Stored::Missing, false, &mut held, "k7#mpz", wait).is_ok());
        assert_eq!(try_pin_with(&b, "default", Stored::Unreadable, false, &mut held, "k7#mpz", wait), Err(PinError::NotSet));
    }

    #[test]
    fn a_store_that_cannot_count_counts_in_memory() {
        let b = Fake::default();
        let r = set(&b, "k7#mpz");
        let mut held = Some(r);
        b.fail_writes.set(true);
        let stored = || load_in(&b, "default");
        for _ in 0..FREE_FAILURES {
            assert_eq!(try_pin_with(&b, "default", stored(), true, &mut held, "wrong1", 0), Err(PinError::Wrong(0)));
        }
        assert_eq!(held.as_ref().unwrap().failures, FREE_FAILURES);
        // the count goes on from memory, though the store still says none
        assert!(matches!(try_pin_with(&b, "default", stored(), true, &mut held, "wrong1", 0), Err(PinError::Wrong(w)) if w > 0));
        // and once the store can be written again, it gets the count before the next try
        b.fail_writes.set(false);
        let wait = held.as_ref().unwrap().blocked_until;
        assert_eq!(try_pin_with(&b, "default", stored(), true, &mut held, "k7#mpz", 1), Err(PinError::Blocked(wait)));
        let Stored::Some(s) = stored() else { panic!() };
        assert_eq!((s.failures, s.blocked_until), (FREE_FAILURES + 1, wait));
        assert!(try_pin_with(&b, "default", stored(), true, &mut held, "k7#mpz", wait).is_ok());
    }

    #[test]
    fn automation_is_limited_while_locked() {
        for c in ["applock", "key", "type", "click"] { assert!(automation_allowed(c), "{c}"); }
        for c in ["wv-eval", "cal-dump", "pim", "notes", "state", "shot", "open", ""] { assert!(!automation_allowed(c), "{c}"); }
    }

    #[test]
    fn notifications_hide_content_when_they_cannot_be_cleared() {
        let on = Live { on: true, ..Live::OFF };
        assert!(!hide_notifications(&Live::OFF, false));
        assert!(!hide_notifications(&on, true));
        assert!(hide_notifications(&on, false));
        assert!(hide_notifications(&Live { locked: true, ..on }, true));
    }
}
