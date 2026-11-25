//! The names of downloaded mail and attachments (Bulwark lib/download-filename.ts): a template
//! filled from the message, made safe, then the four "Downloads" settings applied
//! (filenameSpaceReplacement, filenameLowercase, filenameStripDiacritics,
//! filenameCollapseSeparators; stores/settings-store.ts:995-998). Bulwark applies them where a
//! message is saved as .eml (hooks/use-message-file-actions.ts:29-38), to an attachment's download
//! name (components/email/email-viewer.tsx:1642-1652, 3323-3330) and to the name of a message
//! forwarded as an attachment (lib/forward-as-attachment.ts:56). Files downloads keep the node's
//! own name (stores/file-store.ts:1480).
use serde_json::Value;

/// "Spaces" setting (filenameSpaceReplacement).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Space { Keep, Underscore, Dash }

/// Bulwark FilenameTransformOptions.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Opts {
    pub space: Space,
    pub lowercase: bool,
    pub strip_diacritics: bool,
    pub collapse: bool,
}

impl Default for Opts {
    /// Bulwark's defaults (stores/settings-store.ts:1322-1325).
    fn default() -> Opts { Opts { space: Space::Keep, lowercase: false, strip_diacritics: false, collapse: true } }
}

impl Opts {
    pub fn from_settings(s: &crate::settings::Settings) -> Opts {
        Opts {
            space: match s.str("filenameSpaceReplacement", "keep").as_str() { "underscore" => Space::Underscore, "dash" => Space::Dash, _ => Space::Keep },
            lowercase: s.bool("filenameLowercase", false),
            strip_diacritics: s.bool("filenameStripDiacritics", false),
            collapse: s.bool("filenameCollapseSeparators", true),
        }
    }

    /// The user's settings (tests: the defaults, so no test reads the real profile).
    pub fn load() -> Opts {
        #[cfg(test)]
        return Opts::default();
        #[cfg(not(test))]
        Opts::from_settings(&crate::settings::Settings::load())
    }
}

/// Bulwark DEFAULT_EMAIL_TEMPLATE (download-filename.ts:24).
pub const DEFAULT_EMAIL_TEMPLATE: &str = "{date} ({from}-{to}) {subject}";
/// The template of a message that leaves the account (forward as attachment): no names in it.
pub const FORWARD_TEMPLATE: &str = "{date}-{subject}";
/// Bulwark FILENAME_MAX_LEN.
const MAX_LEN: usize = 200;

/// Bulwark SAFE_CHARS (download-filename.ts:9): letters, digits and some punctuation stay.
fn unsafe_chars() -> &'static regex::Regex {
    static R: std::sync::OnceLock<regex::Regex> = std::sync::OnceLock::new();
    // \p{L} is letters only: a combining mark is not kept, as in Bulwark's /u regex
    R.get_or_init(|| regex::Regex::new(r"[^\p{L}\p{N} _\-().,!@#&+=\[\]{}']").expect("safe chars"))
}

fn replace_unsafe(s: &str) -> String { unsafe_chars().replace_all(s, "_").into_owned() }

/// Runs of `c` made one.
fn squeeze(s: &str, c: char) -> String {
    let mut out = String::with_capacity(s.len());
    for ch in s.chars() { if ch == c && out.ends_with(c) { continue; } out.push(ch); }
    out
}

fn take_chars(s: &str, n: usize) -> String { s.chars().take(n).collect() }

/// Bulwark sanitizePart (download-filename.ts:66-74).
pub fn sanitize_part(input: &str, max: usize) -> String {
    let s = squeeze(&replace_unsafe(input), '_');
    // \s+ to one space
    let mut ws = String::with_capacity(s.len());
    for c in s.chars() { if c.is_whitespace() { if !ws.ends_with(' ') { ws.push(' '); } } else { ws.push(c); } }
    let t = ws.trim().trim_matches(['.', '_', '-']);
    take_chars(t, max)
}

/// Bulwark applyTransforms (download-filename.ts:76-92).
pub fn apply_transforms(input: &str, o: &Opts) -> String {
    let mut s = input.to_string();
    if o.strip_diacritics {
        // NFD splits "ä" into "a" + U+0308, the marks go; "ß" has no decomposition and stays
        let nfd = icu_normalizer::DecomposingNormalizerBorrowed::new_nfd().normalize(&s).into_owned();
        s = marks().replace_all(&nfd, "").into_owned();
    }
    match o.space {
        Space::Underscore => s = squeeze(&s, ' ').replace(' ', "_"),
        Space::Dash => s = squeeze(&s, ' ').replace(' ', "-"),
        Space::Keep => {}
    }
    if o.collapse { s = squeeze(&squeeze(&squeeze(&s, '_'), '-'), ' '); }
    if o.lowercase { s = s.to_lowercase(); }
    s.trim_matches(['.', '_', '-', ' ']).to_string()
}

fn marks() -> &'static regex::Regex {
    static R: std::sync::OnceLock<regex::Regex> = std::sync::OnceLock::new();
    R.get_or_init(|| regex::Regex::new(r"\p{M}+").expect("marks"))
}

/// Bulwark dateParts: (date, date_short, time, year, month, day), "0000…" without a date.
fn date_parts(t: Option<chrono::NaiveDateTime>) -> [(&'static str, String); 6] {
    let (date, short, time, y, m, d) = match t {
        Some(t) => (
            t.format("%Y-%m-%d %H.%M.%S").to_string(), t.format("%Y-%m-%d").to_string(), t.format("%H.%M.%S").to_string(),
            t.format("%Y").to_string(), t.format("%m").to_string(), t.format("%d").to_string(),
        ),
        None => ("0000-00-00 00.00.00".into(), "0000-00-00".into(), "00.00.00".into(), "0000".into(), "00".into(), "00".into()),
    };
    [("date", date), ("date_short", short), ("time", time), ("year", y), ("month", m), ("day", d)]
}

/// Bulwark addrLabel: (name, email, label).
fn addr_label(a: &Value) -> (String, String, String) {
    if !a.is_object() { return (String::new(), String::new(), "unknown".into()); }
    let name = a["name"].as_str().map(str::trim).unwrap_or("").to_string();
    let email = a["email"].as_str().unwrap_or("").to_string();
    let user = email.split('@').next().unwrap_or("");
    let label = if !name.is_empty() { name.clone() } else if !user.is_empty() { user.to_string() } else if !email.is_empty() { email.clone() } else { "unknown".into() };
    (name, email, label)
}

/// Bulwark emailVars, with the message's time as local wall-clock time.
pub fn email_vars_at(m: &Value, when: Option<chrono::NaiveDateTime>) -> Vec<(&'static str, String)> {
    let mut v: Vec<(&'static str, String)> = date_parts(when).into_iter().collect();
    let (fname, femail, flabel) = addr_label(&m["from"][0]);
    let (tname, temail, tlabel) = addr_label(&m["to"][0]);
    v.extend([("from", flabel), ("from_email", femail), ("from_name", fname), ("to", tlabel), ("to_email", temail), ("to_name", tname)]);
    v.push(("subject", m["subject"].as_str().filter(|s| !s.is_empty()).unwrap_or("no subject").to_string()));
    v
}

/// Bulwark `dateParts(email.receivedAt || email.sentAt)`: no date is now, a date that does
/// not parse is None (the zeros).
fn when_of(m: &Value) -> Option<chrono::NaiveDateTime> {
    let iso = m["receivedAt"].as_str().filter(|s| !s.is_empty()).or_else(|| m["sentAt"].as_str().filter(|s| !s.is_empty()));
    match iso {
        None => Some(chrono::Local::now().naive_local()),
        Some(s) => chrono::DateTime::parse_from_rfc3339(s).ok().map(|d| d.with_timezone(&chrono::Local).naive_local()),
    }
}

/// Bulwark renderRaw: each {token} is its sanitised value, an unknown one is empty.
fn render(template: &str, vars: &[(&str, String)], each: impl Fn(&str, &str) -> String) -> String {
    let mut out = String::new();
    let mut rest = template;
    while let Some(i) = rest.find('{') {
        out.push_str(&rest[..i]);
        let after = &rest[i + 1..];
        match after.find('}') {
            Some(j) if j > 0 && after[..j].chars().all(|c| c.is_ascii_alphanumeric() || c == '_') => {
                let k = &after[..j];
                if let Some((_, v)) = vars.iter().find(|(n, _)| *n == k) { out.push_str(&each(k, v)); }
                rest = &after[j + 1..];
            }
            _ => { out.push('{'); rest = after; }
        }
    }
    out.push_str(rest);
    out
}

/// Bulwark emailExportFilename with the message's time given (tests).
pub fn email_export_filename_at(m: &Value, when: Option<chrono::NaiveDateTime>, template: &str, o: &Opts) -> String {
    let vars = email_vars_at(m, when);
    let rendered = render(template, &vars, |_, v| sanitize_part(v, MAX_LEN));
    let cleaned = sanitize_part(&rendered, MAX_LEN);
    let stem = take_chars(&apply_transforms(&cleaned, o), MAX_LEN);
    format!("{}.eml", if stem.is_empty() { "email" } else { &stem })
}

/// The .eml name of a message: Bulwark emailExportFilename.
pub fn email_export_filename(m: &Value, template: &str, o: &Opts) -> String { email_export_filename_at(m, when_of(m), template, o) }

/// A message saved as .eml by the user (list row or reader), by the file name settings.
pub fn eml_save_name(m: &Value) -> String { email_export_filename(m, DEFAULT_EMAIL_TEMPLATE, &Opts::load()) }

/// A message forwarded as an attachment: the fixed template, with the user's transforms.
pub fn forward_name(m: &Value) -> String { email_export_filename(m, FORWARD_TEMPLATE, &Opts::load()) }

/// Bulwark attachmentDownloadFilename with the default template "{filename}"
/// (download-filename.ts:192-222): the name keeps its dots, then the transforms.
pub fn attachment_download_filename(name: &str, o: &Opts) -> String {
    let filename = { let t = name.trim(); if t.is_empty() { "attachment" } else { t } };
    let rendered = replace_unsafe(filename);
    let cleaned = { let c = sanitize_part(&rendered, MAX_LEN); if c.is_empty() { "attachment".to_string() } else { c } };
    let t = apply_transforms(&cleaned, o);
    if t.is_empty() { cleaned } else { t }
}

/// What to do with a message after it was saved as .eml ("After export", postExportAction;
/// hooks/use-message-file-actions.ts:83-86): None keeps it.
pub fn post_export_action(setting: &str) -> Option<&'static str> {
    match setting { "archive" => Some("archive"), "trash" => Some("delete"), _ => None }
}

/// The action after a single message was exported, by the user's setting.
pub fn post_export() -> Option<&'static str> {
    #[cfg(test)]
    return None;
    #[cfg(not(test))]
    post_export_action(&crate::settings::str_pref("postExportAction", "keep"))
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    fn at() -> Option<chrono::NaiveDateTime> { chrono::NaiveDate::from_ymd_opt(2026, 5, 22).unwrap().and_hms_opt(14, 5, 33) }
    fn sample() -> Value {
        json!({"subject": "Benachrichtigung von Ihrem Gerät", "from": [{"name": "Alice Sender", "email": "alice@example.com"}],
            "to": [{"name": "Bob Recipient", "email": "bob@example.com"}]})
    }

    #[test]
    fn sanitize_part_matches_bulwark() {
        assert_eq!(sanitize_part("a/b:c??  d.", 80), "a_b_c_ d");
        assert_eq!(sanitize_part("__x__", 80), "x");
        assert_eq!(sanitize_part("Grüße 😀 日本", 80), "Grüße _ 日本");
        assert_eq!(sanitize_part("a\tb", 80), "a_b");
        assert_eq!(sanitize_part(&"x".repeat(300), 200).len(), 200);
        assert_eq!(sanitize_part("50% off; now", 80), "50_ off_ now");
        // a combining mark is not a letter (\p{L}): "e" + U+0301 keeps the e only
        assert_eq!(sanitize_part("Cafe\u{301}", 80), "Cafe");
        assert_eq!(sanitize_part("Cafe\u{301}s", 80), "Cafe_s");
    }

    #[test]
    fn default_eml_name_is_bulwarks_template() {
        let o = Opts::default();
        assert_eq!(email_export_filename_at(&sample(), at(), DEFAULT_EMAIL_TEMPLATE, &o), "2026-05-22 14.05.33 (Alice Sender-Bob Recipient) Benachrichtigung von Ihrem Gerät.eml");
        // a date that does not parse: the zeros
        assert_eq!(email_export_filename_at(&json!({}), None, DEFAULT_EMAIL_TEMPLATE, &o), "0000-00-00 00.00.00 (unknown-unknown) no subject.eml");
        assert_eq!(when_of(&json!({"receivedAt": "junk"})), None);
        // no date at all: now, as Bulwark's new Date()
        let now = when_of(&json!({})).unwrap();
        assert!((chrono::Local::now().naive_local() - now).num_seconds().abs() < 5);
        // an empty receivedAt falls back to sentAt
        assert!(when_of(&json!({"receivedAt": "", "sentAt": "2026-05-22T14:05:33Z"})).is_some_and(|t| t.format("%Y-%m").to_string() == "2026-05"));
        // a sender without a name: the user part of the address
        let m = json!({"subject": "Hi", "from": [{"email": "carol@example.com"}], "to": [{"name": " ", "email": "dan@example.com"}]});
        assert_eq!(email_export_filename_at(&m, at(), "{from} {to_email} {nope}{subject}", &o), "carol dan@example.com Hi.eml");
        assert_eq!(email_export_filename_at(&sample(), at(), FORWARD_TEMPLATE, &o), "2026-05-22 14.05.33-Benachrichtigung von Ihrem Gerät.eml");
        // an unclosed brace is text (and then unsafe)
        assert_eq!(email_export_filename_at(&sample(), at(), "{subject", &o), "{subject.eml");
        assert_eq!(email_export_filename_at(&sample(), at(), "...", &o), "email.eml");
    }

    #[test]
    fn transforms_follow_the_settings() {
        let all = Opts { space: Space::Underscore, lowercase: true, strip_diacritics: true, collapse: true };
        assert_eq!(email_export_filename_at(&sample(), at(), DEFAULT_EMAIL_TEMPLATE, &all), "2026-05-22_14.05.33_(alice_sender-bob_recipient)_benachrichtigung_von_ihrem_gerat.eml");
        let dash = Opts { space: Space::Dash, ..Opts::default() };
        assert_eq!(apply_transforms("a  b - c", &dash), "a-b-c");
        assert_eq!(apply_transforms("a  b", &Opts { collapse: false, ..Opts::default() }), "a  b");
        assert_eq!(apply_transforms("a__b--c  d", &Opts::default()), "a_b-c d");
        assert_eq!(apply_transforms("a__b", &Opts { collapse: false, ..Opts::default() }), "a__b");
        let strip = Opts { strip_diacritics: true, ..Opts::default() };
        assert_eq!(apply_transforms("Ärger über Café straße", &strip), "Arger uber Cafe straße");
        assert_eq!(apply_transforms("Ünïcödé", &Opts { lowercase: true, ..strip }), "unicode");
        assert_eq!(apply_transforms(" -_x_- ", &Opts::default()), "x");
    }

    #[test]
    fn attachment_names() {
        let o = Opts::default();
        assert_eq!(attachment_download_filename("Report Q3.final.PDF", &o), "Report Q3.final.PDF");
        assert_eq!(attachment_download_filename("a/b.txt", &o), "a_b.txt");
        assert_eq!(attachment_download_filename("  ", &o), "attachment");
        assert_eq!(attachment_download_filename("😀", &o), "attachment");
        let all = Opts { space: Space::Dash, lowercase: true, strip_diacritics: true, collapse: true };
        assert_eq!(attachment_download_filename("Résumé  Final.PDF", &all), "resume-final.pdf");
    }

    #[test]
    fn options_come_from_the_settings_rows() {
        let mut m = serde_json::Map::new();
        assert_eq!(Opts::from_settings(&crate::settings::Settings::with_prefs(m.clone())), Opts::default());
        m.insert("filenameSpaceReplacement".into(), json!("dash"));
        m.insert("filenameLowercase".into(), json!(true));
        m.insert("filenameStripDiacritics".into(), json!(true));
        m.insert("filenameCollapseSeparators".into(), json!(false));
        assert_eq!(Opts::from_settings(&crate::settings::Settings::with_prefs(m)), Opts { space: Space::Dash, lowercase: true, strip_diacritics: true, collapse: false });
    }

    #[test]
    fn after_export() {
        assert_eq!(post_export_action("keep"), None);
        assert_eq!(post_export_action("archive"), Some("archive"));
        assert_eq!(post_export_action("trash"), Some("delete"));
        assert_eq!(post_export_action("bogus"), None);
    }
}
