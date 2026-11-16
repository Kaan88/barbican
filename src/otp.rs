//! One-time code detection for list rows (showVerificationCodes), a dependency-free port of
//! Bulwark's lib/verification-code.ts: a token counts only when it looks like a code and sits
//! near a word that names one (code, Bestätigungscode, PIN, OTP, ...). Doubtful tokens are
//! left out — a wrong chip is worse than none.

const MAX_AFTER: usize = 120;
const MAX_BEFORE: usize = 60;

const NOT_A_CODE_PREFIXES: &[&str] = &[
    "en", "de", "uni", "x", "vs", "op", "byte", "bar", "post", "zip", "qr", "geo", "short", "pseudo",
    "hash", "leet", "trans", "re", "source", "quell", "programm", "program", "status", "error",
    "fehler", "dress", "color", "colour", "farb", "html", "css", "embed", "tracking", "sendungs",
    "referral", "empfehlungs", "sort", "swift", "tax", "ean", "produkt", "product", "artikel",
    "promo", "coupon", "discount", "voucher", "gift", "gutschein", "rabatt", "aktions", "geschenk",
    "bonus", "vorteils", "area", "country", "länder", "laender", "landes", "claude", "studio",
];

const CODE_WORD_ENDINGS: &[&str] = &[
    "code", "codes", "codice", "codici", "código", "códigos", "codigo", "codigos", "cod", "kod",
    "kodu", "kodem", "kodunuz", "kód", "kódot", "kódu", "kode", "koden", "koodi", "koodia", "kood",
    "kods", "kodas", "код", "кода", "коду", "кодом", "κωδικός", "κωδικό", "קוד", "رمز", "کد",
];

const CODE_WORDS_EXACT: &[&str] = &["passcode", "einmalkennwort", "einmalpasswort"];
const CJK_CODE_WORDS: &[&str] = &["コード", "認証番号", "確認番号", "验证码", "驗證碼", "校验码", "动态码", "인증번호", "인증 번호", "인증코드", "인증 코드", "코드"];
const OTP_PHRASES: &[&str] = &["one-time password", "one time password", "one-time passcode", "one time passcode",
    "single-use password", "single use password", "single-use passcode", "temporary password", "temporary passcode"];

const PROMO_WORDS: &[&str] = &["off", "rabatt", "save", "discount", "coupon", "voucher", "sale"];
const PROMO_PREFIXES: &[&str] = &["spar", "gutschein", "angebot"];
const OTHER_LABEL_WORDS: &[&str] = &["nr", "no", "id", "line", "zeile", "version", "tel", "telefon", "phone", "fax", "mobil", "plz",
    "order", "bestellung", "account", "konto", "invoice", "rechnung", "ticket", "case", "page", "seite", "room", "zimmer",
    "commit", "issue", "pr", "build", "run", "job", "step", "port"];
const LINK_WORDS: &[&str] = &["is", "lautet", "ist", "est", "es", "è", "é", "är", "er", "jest", "je"];

fn is_uppercase_code_word(p: &str) -> bool {
    if p == "PIN" || p == "OTP" { return true; }
    let rest = p.strip_suffix("TAN");
    matches!(rest, Some("" | "m" | "sms" | "push" | "chip" | "photo" | "i" | "e"))
}

fn is_code_word_part(part: &str, previous: Option<&str>) -> bool {
    if is_uppercase_code_word(part) { return true; }
    let lower = part.to_lowercase();
    if CODE_WORDS_EXACT.contains(&lower.as_str()) { return true; }
    for ending in CODE_WORD_ENDINGS {
        if !lower.ends_with(ending) { continue; }
        let prefix = &lower[..lower.len() - ending.len()];
        let prev_lower = previous.map(|p| p.to_lowercase());
        let qualifier = if prefix.is_empty() { prev_lower.as_deref() } else { Some(prefix) };
        return qualifier.is_none_or(|q| !NOT_A_CODE_PREFIXES.contains(&q));
    }
    false
}

#[derive(Clone, Copy)]
struct Span { start: usize, end: usize }

fn is_letter(c: char) -> bool { c.is_alphabetic() }
fn is_alnum(c: char) -> bool { c.is_alphanumeric() }

fn normalize(raw: &str) -> Vec<char> {
    let invisible = |c: char| matches!(c as u32, 0xAD | 0x200B..=0x200F | 0x2060 | 0xFEFF | 0x34F);
    let cleaned: String = raw.chars().filter(|c| !invisible(*c)).collect();
    // links and addresses carry ids and tokens, never the code to type
    let mut out = String::new();
    for (i, line) in cleaned.split('\n').enumerate() {
        if i > 0 { out.push('\n'); }
        let words: Vec<&str> = line.split_whitespace()
            .map(|w| { let l = w.to_lowercase(); if l.starts_with("http://") || l.starts_with("https://") || l.starts_with("www.") || w.contains('@') { "" } else { w } })
            .filter(|w| !w.is_empty()).collect();
        out.push_str(&words.join(" "));
    }
    let mut t = out.trim().to_string();
    // a preview cut off with "..." may end in half a code
    for ell in ["...", "…"] {
        if t.ends_with(ell) {
            let body = t[..t.len() - ell.len()].to_string();
            let keep = body.trim_end_matches(|c: char| is_alnum(c) || c == '-').to_string();
            t = keep.trim_end().to_string();
        }
    }
    t.chars().collect()
}

fn find_code_words(text: &[char]) -> Vec<Span> {
    let mut spans = vec![];
    let mut previous: Option<String> = None;
    let mut i = 0;
    while i < text.len() {
        if !is_letter(text[i]) { i += 1; continue; }
        let start = i;
        while i < text.len() && (is_letter(text[i]) || (text[i] == '-' && i + 1 < text.len() && is_letter(text[i + 1]))) { i += 1; }
        let word: String = text[start..i].iter().collect();
        let parts: Vec<&str> = word.split('-').collect();
        let hit = parts.iter().enumerate().any(|(k, p)| is_code_word_part(p, if k > 0 { Some(parts[k - 1]) } else { previous.as_deref() }));
        if hit {
            let next: String = text[i..(i + 12).min(text.len())].iter().collect();
            let of = next.to_lowercase().trim_start().starts_with("of ") && next.starts_with(char::is_whitespace);
            if !of && !labels_short_number(&next) { spans.push(Span { start, end: i }); }
        }
        previous = parts.last().map(|s| s.to_string());
    }
    let lower: Vec<char> = text.iter().flat_map(|c| c.to_lowercase()).collect();
    if lower.len() == text.len() {
        for phrase in CJK_CODE_WORDS.iter().chain(OTP_PHRASES) {
            let p: Vec<char> = phrase.chars().collect();
            let mut k = 0;
            while k + p.len() <= lower.len() {
                if lower[k..k + p.len()] == p[..] { spans.push(Span { start: k, end: k + p.len() }); k += p.len(); } else { k += 1; }
            }
        }
    }
    spans
}

/// "with code 250 (2.1.5)": a code word followed by a 1–3 digit number names a status code.
fn labels_short_number(next: &str) -> bool {
    let t = next.trim_start();
    let t = t.strip_prefix([':', '=']).unwrap_or(t).trim_start();
    let digits: String = t.chars().take_while(|c| c.is_ascii_digit()).collect();
    if digits.is_empty() || digits.len() > 3 { return false; }
    let rest: Vec<char> = t.chars().skip(digits.len()).take(2).collect();
    match rest.as_slice() {
        [] => true,
        [c, ..] if is_alnum(*c) => false,
        ['.' | ',', d, ..] if d.is_ascii_digit() => false,
        [' ' | '-', d, ..] if d.is_ascii_digit() => false,
        _ => true,
    }
}

/// Candidate tokens: hyphen groups (6ZA-BRE), digit groups (557 260), runs of 4–10 alnum.
fn candidates(text: &[char]) -> Vec<(usize, usize)> {
    let mut out = vec![];
    let mut i = 0;
    while i < text.len() {
        if !text[i].is_ascii_alphanumeric() || (i > 0 && is_alnum(text[i - 1])) { i += 1; continue; }
        // a run of ascii alnum and inner hyphens
        let start = i;
        let mut j = i;
        while j < text.len() && (text[j].is_ascii_alphanumeric() || (text[j] == '-' && j + 1 < text.len() && text[j + 1].is_ascii_alphanumeric())) { j += 1; }
        if j < text.len() && is_alnum(text[j]) { i = j + 1; continue; } // runs into a non-ascii letter
        let tok: String = text[start..j].iter().collect();
        let parts: Vec<&str> = tok.split('-').collect();
        if parts.len() >= 2 && parts.len() <= 4 && parts.iter().all(|p| (2..=8).contains(&p.len())) {
            out.push((start, j));
        } else if parts.len() == 1 {
            // "557 260"
            let mut merged = false;
            if (3..=4).contains(&tok.len()) && tok.chars().all(|c| c.is_ascii_digit()) && j + 1 < text.len() && text[j] == ' ' {
                let k0 = j + 1;
                let mut k = k0;
                while k < text.len() && text[k].is_ascii_digit() { k += 1; }
                if (3..=4).contains(&(k - k0)) && !(k < text.len() && text[k].is_ascii_digit()) {
                    out.push((start, k));
                    merged = true;
                }
            }
            if !merged && (4..=10).contains(&tok.len()) { out.push((start, j)); }
        }
        i = j;
    }
    out
}

fn window(text: &[char], from: isize, to: usize) -> String {
    let mut s = from.max(0) as usize;
    let mut e = to.min(text.len());
    while s > 0 && s < e && is_letter(text[s - 1]) && is_letter(text[s]) { s += 1; }
    while e < text.len() && e > s && is_letter(text[e]) && is_letter(text[e - 1]) { e -= 1; }
    text[s..e].iter().collect()
}

fn last_word(s: &str) -> String {
    let t = s.trim_end_matches(|c: char| c == ' ' || c == '#' || c == ':' || c == '.');
    t.rsplit(|c: char| !is_letter(c)).next().unwrap_or("").to_lowercase()
}

fn labelled_as_other(before: &str) -> bool {
    let lower = before.to_lowercase();
    let t = lower.trim_end_matches([' ', '#']);
    if t.ends_with("ending in") || t.ends_with("endet auf") || t.ends_with("endend auf") || t.ends_with("ends with") { return true; }
    let w = last_word(t);
    if w.is_empty() { return false; }
    if w.ends_with("nummer") || w.ends_with("number") { return true; }
    OTHER_LABEL_WORDS.contains(&w.as_str())
}

fn has_promo_words(ctx: &str) -> bool {
    if ctx.contains('%') { return true; }
    ctx.split(|c: char| !is_letter(c)).filter(|w| !w.is_empty()).any(|w| {
        let l = w.to_lowercase();
        PROMO_WORDS.contains(&l.as_str()) || PROMO_PREFIXES.iter().any(|p| l.starts_with(p))
    })
}

fn score(text: &[char], start: usize, end: usize, keywords: &[Span]) -> Option<(String, f32)> {
    let raw: String = text[start..end].iter().collect();
    let letters: String = raw.chars().filter(|c| c.is_ascii_alphabetic()).collect();
    let digits: String = raw.chars().filter(|c| c.is_ascii_digit()).collect();
    let before = window(text, start as isize - 40, start);
    let after: String = text[end..(end + 8).min(text.len())].iter().collect();
    if digits.is_empty() { return None; }
    if !letters.is_empty() && letters != letters.to_uppercase() && letters != letters.to_lowercase() { return None; }
    let bc: Vec<char> = before.chars().collect();
    let ac: Vec<char> = after.chars().collect();
    let mut code = raw.clone();
    if letters.is_empty() {
        code = digits.clone();
        if raw.contains('-') && !(raw.len() <= 9 && raw.split('-').count() == 2 && raw.split('-').all(|p| (3..=4).contains(&p.len()))) { return None; }
        if digits.len() < 4 || digits.len() > 10 { return None; }
        let sep = |c: char| matches!(c, '.' | ',' | ':' | '/' | '-');
        if bc.len() >= 2 && sep(bc[bc.len() - 1]) && bc[bc.len() - 2].is_ascii_digit() { return None; }
        if ac.len() >= 2 && sep(ac[0]) && ac[1].is_ascii_digit() { return None; }
        if matches!(bc.last(), Some('#' | '+')) { return None; }
        // phone numbers, IBAN groups: a digit or ")" right before (one separator allowed), a digit right after
        let gap = |c: char| matches!(c, ' ' | '/' | '-');
        let mut bi = bc.len();
        if bi > 0 && gap(bc[bi - 1]) { bi -= 1; }
        if bi > 0 && (bc[bi - 1].is_ascii_digit() || bc[bi - 1] == ')') { return None; }
        let mut ai = 0;
        if ai < ac.len() && gap(ac[ai]) { ai += 1; }
        if ai < ac.len() && ac[ai].is_ascii_digit() { return None; }
        let b = before.trim_end();
        if b.ends_with(['€', '$', '£']) { return None; }
        let a = after.trim_start().to_lowercase();
        if a.starts_with(['€', '$', '£', '%']) || ["eur", "usd", "chf"].iter().any(|c| a.starts_with(c) && !a[c.len()..].starts_with(is_letter)) { return None; }
    } else {
        // 10min, 600px; THANKS10
        let lead_digits = raw.chars().take_while(|c| c.is_ascii_digit()).count();
        let tail = &raw[lead_digits..];
        if lead_digits > 0 && (1..=3).contains(&tail.len()) && tail.chars().all(|c| c.is_ascii_alphabetic()) { return None; }
        let lead_upper = raw.chars().take_while(|c| c.is_ascii_uppercase()).count();
        let rest = &raw[lead_upper..];
        if lead_upper >= 4 && (1..=4).contains(&rest.len()) && rest.chars().all(|c| c.is_ascii_digit()) { return None; }
        if raw.len() > 10 && raw.as_bytes().get(4) == Some(&b'-') && raw.contains('T') && raw[..4].chars().all(|c| c.is_ascii_digit()) { return None; }
    }
    if labelled_as_other(&before) { return None; }
    if has_promo_words(&window(text, start as isize - 60, end + 40)) { return None; }

    let (mut best, mut best_after) = (f32::INFINITY, f32::INFINITY);
    for kw in keywords {
        if kw.start < end && kw.end > start { return None; }
        if kw.end <= start && start - kw.end <= MAX_AFTER {
            let d = (start - kw.end) as f32;
            best_after = best_after.min(d);
            best = best.min(d);
        }
        if kw.start >= end && kw.start - end <= MAX_BEFORE { best = best.min((kw.start - end) as f32 * 1.5); }
    }
    if best.is_infinite() { return None; }
    if !letters.is_empty() && letters == letters.to_lowercase() {
        if best_after > 40.0 { return None; }
        if raw.chars().all(|c| c.is_ascii_hexdigit() && !c.is_ascii_uppercase() || c == '-') && best_after > 2.0 { return None; }
    }
    let bt = before.trim_end();
    let linked = bt.ends_with([':', '=']) || (bt.ends_with(is_letter) && LINK_WORDS.contains(&last_word(bt).as_str()));
    let b2 = before.trim_end_matches([' ', '\t']);
    let a2 = after.trim_start_matches([' ', '\t']);
    let own_line = (b2.is_empty() && start <= 40 || b2.ends_with('\n')) && (a2.is_empty() && end == text.len() || a2.starts_with('\n'));
    let is_year = digits.len() == 4 && (digits.starts_with("19") || digits.starts_with("20"));
    if letters.is_empty() && is_year && !linked && !own_line { return None; }
    let mut s = best;
    if linked { s -= 15.0; }
    if own_line { s -= 15.0; }
    Some((code, s))
}

fn best_candidate(raw: &str) -> Option<String> {
    let text = normalize(raw);
    if text.is_empty() { return None; }
    let keywords = find_code_words(&text);
    if keywords.is_empty() { return None; }
    candidates(&text).into_iter()
        .filter_map(|(s, e)| score(&text, s, e, &keywords))
        .min_by(|a, b| a.1.total_cmp(&b.1))
        .map(|c| c.0)
}

/// The one-time code in a mail; a code in the subject wins.
pub fn find(subject: &str, text: &str) -> Option<String> {
    let subject: String = subject.chars().take(1000).collect();
    best_candidate(&subject).or_else(|| best_candidate(text))
}

#[cfg(test)]
mod tests {
    use super::find;
    #[test]
    fn finds_codes() {
        assert_eq!(find("Your verification code is 177945", "").as_deref(), Some("177945"));
        assert_eq!(find("HUK6QR ist Ihr Bestätigungscode", "").as_deref(), Some("HUK6QR"));
        assert_eq!(find("Sign in", "Your code: 557 260. It expires in 10 minutes.").as_deref(), Some("557260"));
        assert_eq!(find("Sign-in", "Your OTP: 6ZA-BRE").as_deref(), Some("6ZA-BRE"));
        assert_eq!(find("Welcome", "Der Bestätigungscode lautet 548980.").as_deref(), Some("548980"));
    }
    #[test]
    fn skips_non_codes() {
        assert_eq!(find("Promo code SAVE20 for 20% off", ""), None);
        assert_eq!(find("Order 123456 shipped", "Tracking code follows"), None);
        assert_eq!(find("Meeting tomorrow", "Room 4021 at 10:30"), None);
        assert_eq!(find("Claude Code 2026 release", ""), None);
    }
}
