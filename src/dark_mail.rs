//! Dark mode for HTML mail bodies (Bulwark lib/email-dark-mode.ts). The colour rewrite itself
//! runs in the WebView (email_dark.js, allowed by its CSP hash); this module holds the page
//! parts around it: the fallback colours, the pending class that hides the light original
//! until the pass ran, and the pinning of the mail's own `prefers-color-scheme` queries to
//! the theme the app shows it in (inside the WebView they would follow the OS).

/// The recolouring script.
pub const SCRIPT: &str = include_str!("email_dark.js");
/// Class on <html> that keeps the body hidden until the script ran (1.5 s failsafe).
pub const PENDING_CLASS: &str = "barbican-dark-pending";
/// The canvas the converted mail sits on (the reader's dark background; white maps near it).
pub const BACKGROUND: &str = "#141414";
const TEXT: &str = "#e6e6e6";
const LINK: &str = "#8ab4f8";

const ALWAYS: &str = "(min-width: 0px)";
const NEVER: &str = "(max-width: 0.001px)";

/// Whether the mail ships its own dark styles.
pub fn has_native_dark(html: &str) -> bool {
    let l = html.to_ascii_lowercase();
    let mut i = 0;
    while let Some(p) = l[i..].find("prefers-color-scheme") {
        let rest = l[i + p + "prefers-color-scheme".len()..].trim_start();
        if let Some(r) = rest.strip_prefix(':') { if r.trim_start().starts_with("dark") { return true; } }
        i += p + 1;
    }
    false
}

/// `color-scheme` of the page: dark only for a mail with its own dark styles shown dark.
pub fn color_scheme(dark: bool, native: bool) -> &'static str { if dark && native { "dark" } else { "light" } }

/// Thin overlay-style scrollbar matching the app (8 px, rounded thumb, no track, no arrows) instead of
/// Chromium's 16 px classic one. `dark` picks the thumb colour for a dark or a light page.
pub fn scrollbar_css(dark: bool) -> String {
    let (thumb, hover) = if dark { ("#3a3a3a", "#525252") } else { ("#c4c4c8", "#a1a1a6") };
    format!("::-webkit-scrollbar{{width:12px;height:12px;background:transparent}}::-webkit-scrollbar-track,::-webkit-scrollbar-corner{{background:transparent}}::-webkit-scrollbar-button{{display:none;width:0;height:0}}::-webkit-scrollbar-thumb{{background:{thumb};border-radius:6px;border:3px solid transparent;background-clip:content-box;min-height:36px}}::-webkit-scrollbar-thumb:hover{{background:{hover};background-clip:content-box}}")
}

/// The fallback colours and the pending rules (Bulwark emailDarkModeCss).
pub fn css() -> String {
    format!("html{{background:{BACKGROUND}}}body{{color:{TEXT};background:{BACKGROUND}}}a{{color:{LINK}}}\
@keyframes barbican-dark-reveal{{to{{visibility:visible}}}}\
html.{PENDING_CLASS}{{background-color:{BACKGROUND}!important}}\
html.{PENDING_CLASS} body{{visibility:hidden;animation:barbican-dark-reveal 0s 1.5s forwards}}")
}

/// Rewrite `(prefers-color-scheme: dark|light)` inside <style> blocks so the matching one always
/// applies and the other never does (Bulwark resolveColorSchemeMedia). Only <style> blocks are
/// touched; ASCII case-insensitive.
pub fn resolve_color_scheme_media(html: &str, dark: bool) -> String {
    if !html.to_ascii_lowercase().contains("prefers-color-scheme") { return html.to_string(); }
    let lower = html.to_ascii_lowercase();
    let mut out = String::with_capacity(html.len());
    let mut i = 0;
    while let Some(p) = lower[i..].find("<style") {
        let start = i + p;
        // "<style" must be the whole tag name
        let after = lower.as_bytes().get(start + 6).copied().unwrap_or(b'>');
        if !(after == b'>' || after.is_ascii_whitespace() || after == b'/') {
            out.push_str(&html[i..start + 6]);
            i = start + 6;
            continue;
        }
        let Some(open_end) = lower[start..].find('>').map(|e| start + e + 1) else { break };
        let Some(close) = lower[open_end..].find("</style").map(|e| open_end + e) else { break };
        out.push_str(&html[i..open_end]);
        out.push_str(&pin_queries(&html[open_end..close], dark));
        i = close;
    }
    out.push_str(&html[i..]);
    out
}

/// `(prefers-color-scheme: x)` with any spacing, replaced in one CSS text.
fn pin_queries(css: &str, dark: bool) -> String {
    let lower = css.to_ascii_lowercase();
    let b = lower.as_bytes();
    let mut out = String::with_capacity(css.len());
    let mut i = 0;
    let mut last = 0;
    while i < b.len() {
        if b[i] == b'(' {
            let mut j = i + 1;
            let skip = |j: &mut usize| while *j < b.len() && b[*j].is_ascii_whitespace() { *j += 1; };
            skip(&mut j);
            if lower[j..].starts_with("prefers-color-scheme") {
                j += "prefers-color-scheme".len();
                skip(&mut j);
                if j < b.len() && b[j] == b':' {
                    j += 1;
                    skip(&mut j);
                    let scheme = if lower[j..].starts_with("dark") { Some(true) } else if lower[j..].starts_with("light") { Some(false) } else { None };
                    if let Some(is_dark) = scheme {
                        j += if is_dark { 4 } else { 5 };
                        skip(&mut j);
                        if j < b.len() && b[j] == b')' {
                            out.push_str(&css[last..i]);
                            out.push_str(if is_dark == dark { ALWAYS } else { NEVER });
                            i = j + 1;
                            last = i;
                            continue;
                        }
                    }
                }
            }
        }
        i += 1;
    }
    out.push_str(&css[last..]);
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn native_dark_detection() {
        assert!(has_native_dark("<style>@media (prefers-color-scheme: dark){body{background:#000}}</style>"));
        assert!(has_native_dark("<STYLE>@media (PREFERS-COLOR-SCHEME:DARK){}</STYLE>"));
        assert!(!has_native_dark("<style>@media (prefers-color-scheme: light){}</style>"));
        assert!(!has_native_dark("<p>no styles</p>"));
        assert_eq!(color_scheme(true, true), "dark");
        assert_eq!(color_scheme(true, false), "light");
        assert_eq!(color_scheme(false, true), "light");
    }

    #[test]
    fn pins_media_queries_to_the_app_theme() {
        let h = "<style>@media (prefers-color-scheme: dark){a{}} @media ( prefers-color-scheme : light ){b{}}</style><p>(prefers-color-scheme: dark)</p>";
        let d = resolve_color_scheme_media(h, true);
        assert_eq!(d, "<style>@media (min-width: 0px){a{}} @media (max-width: 0.001px){b{}}</style><p>(prefers-color-scheme: dark)</p>");
        let l = resolve_color_scheme_media(h, false);
        assert!(l.starts_with("<style>@media (max-width: 0.001px){a{}} @media (min-width: 0px){b{}}"));
        // <style media=…> and several blocks; a <styles> tag is not a style block
        let m = resolve_color_scheme_media("<styles>(prefers-color-scheme: dark)</styles><style type=\"text/css\">(prefers-color-scheme:dark)</style><style>(prefers-color-scheme: light)</style>", true);
        assert_eq!(m, "<styles>(prefers-color-scheme: dark)</styles><style type=\"text/css\">(min-width: 0px)</style><style>(max-width: 0.001px)</style>");
        assert_eq!(resolve_color_scheme_media("<p>plain</p>", true), "<p>plain</p>");
    }

    #[test]
    fn css_hides_until_the_pass_ran() {
        let c = css();
        assert!(c.contains("html.barbican-dark-pending body{visibility:hidden"));
        assert!(c.contains("body{color:#e6e6e6;background:#141414}"));
        // the script looks for the same canvas colour and pending class
        assert!(SCRIPT.contains("{ r: 10, g: 10, b: 10, a: 1 }"));
        assert!(SCRIPT.contains(&format!("'{PENDING_CLASS}'")));
    }
}
