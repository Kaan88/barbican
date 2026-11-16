//! Spell checking with the Windows spell checker (ISpellChecker, the engine Edge and
//! WebView2 use), in the writing language from settings. Results are cached per text,
//! so only changed paragraphs are checked again while typing.
use std::cell::RefCell;
use std::collections::HashMap;
use std::time::{Duration, Instant};

/// Language tag for the `writingLanguage` setting ("auto" = the Windows user locale).
pub fn language_tag(pref: &str, user_locale: &str) -> String {
    match pref {
        "en" => "en-US".into(),
        "de" => "de-DE".into(),
        "fr" => "fr-FR".into(),
        "es" => "es-ES".into(),
        "tr" => "tr-TR".into(),
        "" | "auto" => if user_locale.is_empty() { "en-US".into() } else { user_locale.into() },
        other => other.into(),
    }
}

/// Byte ranges of UTF-16 ranges in `text`.
pub fn utf16_to_bytes(text: &str, ranges: &[(u32, u32)]) -> Vec<(usize, usize)> {
    let mut map = Vec::with_capacity(text.len() + 1); // utf16 index -> byte offset
    for (b, c) in text.char_indices() { for _ in 0..c.len_utf16() { map.push(b); } }
    map.push(text.len());
    ranges.iter().filter_map(|&(s, l)| {
        let (s, e) = (s as usize, (s + l) as usize);
        (e < map.len() && s < e).then(|| (map[s], map[e]))
    }).collect()
}

/// Words the checker should not flag: URLs, e-mail addresses, words with digits.
pub fn skip_word(w: &str) -> bool {
    w.contains("://") || w.contains('@') || w.starts_with("www.") || w.chars().any(|c| c.is_ascii_digit()) || w.chars().count() < 2
}

/// The whitespace-delimited token around byte range (s, e) of `text`.
fn token(text: &str, s: usize, e: usize) -> &str {
    let a = text[..s].rfind(char::is_whitespace).map(|i| i + text[i..].chars().next().unwrap().len_utf8()).unwrap_or(0);
    let b = text[e..].find(char::is_whitespace).map(|i| e + i).unwrap_or(text.len());
    &text[a..b]
}

#[cfg(windows)]
mod sys {
    use windows::core::{HSTRING, PWSTR};
    use windows::Win32::Globalization::*;
    use windows::Win32::System::Com::*;

    pub struct Checker(ISpellChecker);

    pub fn user_locale() -> String {
        let mut buf = [0u16; 85];
        let n = unsafe { GetUserDefaultLocaleName(&mut buf) };
        if n <= 1 { return String::new(); }
        String::from_utf16_lossy(&buf[..n as usize - 1])
    }

    pub fn create(tag: &str) -> Option<Checker> {
        unsafe {
            let _ = CoInitializeEx(None, COINIT_APARTMENTTHREADED);
            let f: ISpellCheckerFactory = CoCreateInstance(&SpellCheckerFactory, None, CLSCTX_INPROC_SERVER).ok()?;
            let mut tag = tag.to_string();
            if !f.IsSupported(&HSTRING::from(&tag)).ok()?.as_bool() {
                // "de-AT" -> any supported "de-*", else English
                let base = tag.split('-').next().unwrap_or("en").to_string();
                let cand = [format!("{base}-{}", base.to_uppercase()), "en-US".to_string()];
                tag = cand.into_iter().find(|t| f.IsSupported(&HSTRING::from(t)).is_ok_and(|b| b.as_bool()))?;
            }
            f.CreateSpellChecker(&HSTRING::from(&tag)).ok().map(Checker)
        }
    }

    unsafe fn take(p: PWSTR) -> String {
        if p.is_null() { return String::new(); }
        let s = unsafe { p.to_string().unwrap_or_default() };
        unsafe { CoTaskMemFree(Some(p.0 as *const _)) };
        s
    }

    impl Checker {
        /// UTF-16 (start, length) of the misspelled words.
        pub fn check(&self, text: &str) -> Vec<(u32, u32)> {
            let mut out = vec![];
            unsafe {
                let Ok(errs) = self.0.Check(&HSTRING::from(text)) else { return out };
                loop {
                    let mut e: Option<ISpellingError> = None;
                    if errs.Next(&mut e) != windows::Win32::Foundation::S_OK { break; }
                    let Some(e) = e else { break };
                    // CORRECTIVE_ACTION_DELETE marks repeated words: not a spelling mistake
                    if e.CorrectiveAction().is_ok_and(|a| a == CORRECTIVE_ACTION_DELETE) { continue; }
                    if let (Ok(s), Ok(l)) = (e.StartIndex(), e.Length()) { out.push((s, l)); }
                }
            }
            out
        }

        pub fn suggest(&self, word: &str) -> Vec<String> {
            let mut out = vec![];
            unsafe {
                let Ok(en) = self.0.Suggest(&HSTRING::from(word)) else { return out };
                loop {
                    let mut item = [PWSTR::null()];
                    let mut got = 0u32;
                    let hr = en.Next(&mut item, Some(&mut got));
                    if hr != windows::Win32::Foundation::S_OK || got == 0 { break; }
                    let s = take(item[0]);
                    if !s.is_empty() && s != word { out.push(s); }
                    if out.len() >= 6 { break; }
                }
            }
            out
        }

        pub fn add(&self, word: &str) { unsafe { let _ = self.0.Add(&HSTRING::from(word)); } }
        pub fn ignore(&self, word: &str) { unsafe { let _ = self.0.Ignore(&HSTRING::from(word)); } }
    }
}

#[cfg(not(windows))]
mod sys {
    pub struct Checker;
    pub fn user_locale() -> String { String::new() }
    pub fn create(_: &str) -> Option<Checker> { None }
    impl Checker {
        pub fn check(&self, _: &str) -> Vec<(u32, u32)> { vec![] }
        pub fn suggest(&self, _: &str) -> Vec<String> { vec![] }
        pub fn add(&self, _: &str) {}
        pub fn ignore(&self, _: &str) {}
    }
}

struct State {
    /// (enabled, language tag) from settings, re-read every few seconds.
    config: Option<(Instant, bool, String)>,
    checker: Option<(String, Option<sys::Checker>)>,
    cache: HashMap<String, Vec<(usize, usize)>>,
}

thread_local! {
    static ST: RefCell<State> = RefCell::new(State { config: None, checker: None, cache: HashMap::new() });
}

fn config(st: &mut State) -> (bool, String) {
    let fresh = st.config.as_ref().is_some_and(|(t, _, _)| t.elapsed() < Duration::from_secs(3));
    if !fresh {
        let p = crate::settings::Settings::load();
        let on = p.bool("spellCheck", true);
        let tag = language_tag(&p.str("writingLanguage", "auto"), &sys::user_locale());
        st.config = Some((Instant::now(), on, tag));
    }
    let (_, on, tag) = st.config.clone().unwrap();
    (on, tag)
}

fn with_checker<R>(f: impl FnOnce(&sys::Checker, &mut HashMap<String, Vec<(usize, usize)>>) -> R) -> Option<R> {
    ST.with(|s| {
        let mut st = s.borrow_mut();
        let (on, tag) = config(&mut st);
        if !on { return None; }
        if st.checker.as_ref().is_none_or(|(t, _)| *t != tag) {
            st.cache.clear();
            st.checker = Some((tag.clone(), sys::create(&tag)));
        }
        let State { checker, cache, .. } = &mut *st;
        let c = checker.as_ref()?.1.as_ref()?;
        Some(f(c, cache))
    })
}

/// Settings changed: re-read them on the next check.
pub fn reload() { ST.with(|s| s.borrow_mut().config = None); }

/// Misspelled words of a paragraph as byte ranges (empty when spell checking is off).
pub fn misspelled(text: &str) -> Vec<(usize, usize)> {
    if text.trim().is_empty() { return vec![]; }
    with_checker(|c, cache| {
        if let Some(r) = cache.get(text) { return r.clone(); }
        let r: Vec<(usize, usize)> = utf16_to_bytes(text, &c.check(text)).into_iter()
            .filter(|&(s, e)| !skip_word(token(text, s, e))).collect();
        if cache.len() > 2000 { cache.clear(); }
        cache.insert(text.to_string(), r.clone());
        r
    }).unwrap_or_default()
}

pub fn suggestions(word: &str) -> Vec<String> { with_checker(|c, _| c.suggest(word)).unwrap_or_default() }
pub fn add(word: &str) { with_checker(|c, cache| { c.add(word); cache.clear(); }); }
pub fn ignore(word: &str) { with_checker(|c, cache| { c.ignore(word); cache.clear(); }); }

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn tags() {
        assert_eq!(language_tag("de", "en-GB"), "de-DE");
        assert_eq!(language_tag("auto", "en-GB"), "en-GB");
        assert_eq!(language_tag("auto", ""), "en-US");
    }

    #[test]
    fn utf16_ranges() {
        // "é" is 2 bytes / 1 unit, "😀" 4 bytes / 2 units
        let t = "é 😀 wrng";
        assert_eq!(utf16_to_bytes(t, &[(5, 4)]), vec![(t.len() - 4, t.len())]);
        assert_eq!(utf16_to_bytes(t, &[(0, 1)]), vec![(0, 2)]);
        assert!(utf16_to_bytes(t, &[(8, 9)]).is_empty());
    }

    #[test]
    fn skips() {
        assert!(skip_word("https://x.org/abc"));
        assert!(skip_word("me@x.org"));
        assert!(skip_word("abc123"));
        assert!(!skip_word("helo"));
        assert_eq!(token("see https://x.org/qq now", 14, 16), "https://x.org/qq");
    }

    #[cfg(windows)]
    #[test]
    fn windows_checker() {
        // the Windows spell checker is present on every Windows 8+ install with English
        let Some(c) = sys::create("en-US") else { return };
        let t = "This sentense has a mispelled word.";
        let r = utf16_to_bytes(t, &c.check(t));
        let words: Vec<&str> = r.iter().map(|&(s, e)| &t[s..e]).collect();
        assert!(words.contains(&"sentense"), "{words:?}");
        assert!(words.contains(&"mispelled"), "{words:?}");
        assert!(c.suggest("sentense").iter().any(|s| s == "sentence"));
    }
}
