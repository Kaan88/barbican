//! Spell checking on Linux through hunspell's ispell-compatible pipe (`hunspell -a`), with
//! the same interface as the Windows ISpellChecker in rte/spell.rs. Without hunspell or
//! without a dictionary for the writing language, `create` returns None and nothing is
//! underlined (the same as spell checking switched off).
#![cfg_attr(windows, allow(dead_code))]

/// One line of hunspell's answer.
#[derive(Debug, PartialEq)]
pub enum Answer {
    Ok,
    /// "& word count offset: s1, s2" or "# word offset"
    Miss { word: String, suggestions: Vec<String> },
}

pub fn parse_line(line: &str) -> Option<Answer> {
    let line = line.trim_end();
    match line.chars().next()? {
        '*' | '+' | '-' => Some(Answer::Ok),
        '&' => {
            let (head, sugg) = line.split_once(':')?;
            let word = head.split_whitespace().nth(1)?.to_string();
            let suggestions = sugg.split(',').map(|s| s.trim().to_string()).filter(|s| !s.is_empty()).collect();
            Some(Answer::Miss { word, suggestions })
        }
        '#' => Some(Answer::Miss { word: line.split_whitespace().nth(1)?.to_string(), suggestions: vec![] }),
        _ => None,
    }
}

/// UTF-16 (start, length) of each missed word in `text`, found in order. hunspell's offsets
/// count differently across versions and encodings, so the words are located here.
pub fn locate(text: &str, words: &[String]) -> Vec<(u32, u32)> {
    let mut out = vec![];
    let mut from = 0;
    for w in words {
        let mut search = from;
        let hit = loop {
            let Some(i) = text[search..].find(w.as_str()).map(|i| i + search) else { break None };
            let before = text[..i].chars().next_back();
            let after = text[i + w.len()..].chars().next();
            let edge = |c: Option<char>| c.is_none_or(|c| !c.is_alphanumeric());
            if edge(before) && edge(after) { break Some(i); }
            search = i + w.len();
        };
        if let Some(i) = hit {
            let start = text[..i].encode_utf16().count() as u32;
            out.push((start, w.encode_utf16().count() as u32));
            from = i + w.len();
        }
    }
    out
}

/// hunspell dictionary name for a BCP 47 tag: "de-DE" -> "de_DE".
pub fn dictionary(tag: &str) -> String {
    tag.replace('-', "_")
}

/// The user's language from the environment ("de_DE.UTF-8" -> "de-DE").
pub fn user_locale() -> String {
    let raw = ["LC_ALL", "LC_MESSAGES", "LANG"].iter().filter_map(|v| std::env::var(v).ok()).find(|v| !v.is_empty() && v != "C" && v != "POSIX").unwrap_or_default();
    raw.split(['.', '@']).next().unwrap_or("").replace('_', "-")
}

#[cfg(not(windows))]
pub use pipe::{create, Checker};

#[cfg(not(windows))]
mod pipe {
    use super::*;
    use std::cell::RefCell;
    use std::io::{BufRead, BufReader, Write};
    use std::process::{Child, ChildStdin, ChildStdout, Command, Stdio};

    pub struct Checker {
        io: RefCell<(Child, ChildStdin, BufReader<ChildStdout>)>,
    }

    impl Drop for Checker {
        fn drop(&mut self) { let _ = self.io.borrow_mut().0.kill(); }
    }

    pub fn create(tag: &str) -> Option<Checker> {
        let mut child = Command::new("hunspell").args(["-a", "-i", "utf-8", "-d", &dictionary(tag)])
            .stdin(Stdio::piped()).stdout(Stdio::piped()).stderr(Stdio::null()).spawn().ok()?;
        let stdin = child.stdin.take()?;
        let mut out = BufReader::new(child.stdout.take()?);
        // the banner ("@(#) International Ispell Version ...") says the dictionary loaded
        let mut banner = String::new();
        if out.read_line(&mut banner).ok()? == 0 || !banner.starts_with('@') { let _ = child.kill(); return None; }
        Some(Checker { io: RefCell::new((child, stdin, out)) })
    }

    impl Checker {
        fn ask(&self, text: &str) -> Vec<Answer> {
            let mut io = self.io.borrow_mut();
            let (_, stdin, out) = &mut *io;
            // "^" keeps a line from being read as a command; one line per request
            let line = format!("^{}\n", text.replace(['\n', '\r'], " "));
            if stdin.write_all(line.as_bytes()).and_then(|_| stdin.flush()).is_err() { return vec![]; }
            let mut res = vec![];
            loop {
                let mut l = String::new();
                match out.read_line(&mut l) {
                    Ok(0) | Err(_) => break,
                    Ok(_) if l.trim().is_empty() => break,
                    Ok(_) => res.extend(parse_line(&l)),
                }
            }
            res
        }

        fn send(&self, cmd: &str) {
            let mut io = self.io.borrow_mut();
            let _ = io.1.write_all(cmd.as_bytes()).and_then(|_| io.1.flush());
        }

        pub fn check(&self, text: &str) -> Vec<(u32, u32)> {
            let words: Vec<String> = self.ask(text).into_iter().filter_map(|a| match a { Answer::Miss { word, .. } => Some(word), _ => None }).collect();
            locate(text, &words)
        }

        pub fn suggest(&self, word: &str) -> Vec<String> {
            self.ask(word).into_iter().find_map(|a| match a { Answer::Miss { suggestions, .. } => Some(suggestions), _ => None }).unwrap_or_default()
        }

        /// Into the personal dictionary ("*word", then "#" saves it).
        pub fn add(&self, word: &str) { self.send(&format!("*{word}\n#\n")); }

        /// Accepted for this session ("@word").
        pub fn ignore(&self, word: &str) { self.send(&format!("@{word}\n")); }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn answers() {
        assert_eq!(parse_line("*"), Some(Answer::Ok));
        assert_eq!(parse_line("+ walk"), Some(Answer::Ok));
        assert_eq!(parse_line("& sentense 3 5: sentence, sentences, sentenced\n"),
            Some(Answer::Miss { word: "sentense".into(), suggestions: vec!["sentence".into(), "sentences".into(), "sentenced".into()] }));
        assert_eq!(parse_line("# qzxv 12"), Some(Answer::Miss { word: "qzxv".into(), suggestions: vec![] }));
        assert_eq!(parse_line("@(#) International Ispell Version 3.2.06 (but really Hunspell 1.7.2)"), None);
    }

    #[test]
    fn locates_whole_words_in_utf16() {
        let t = "é wrng and wrngs then wrng";
        // whole words only: "wrngs" is not a hit for "wrng"
        assert_eq!(locate(t, &["wrng".into(), "wrng".into()]), vec![(2, 4), (22, 4)]);
        assert_eq!(locate("😀 helo", &["helo".into()]), vec![(3, 4)]);
        assert!(locate("fine", &["gone".into()]).is_empty());
    }

    #[test]
    fn names() {
        assert_eq!(dictionary("de-DE"), "de_DE");
    }
}
