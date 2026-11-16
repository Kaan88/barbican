//! Lightweight composer markup. Slint has no rich-text editor, so the formatting
//! toolbar inserts a small Markdown-like syntax into the plain body and this module
//! turns it into HTML for the `text/html` part (and readable text for `text/plain`).
//!
//! Inline: `**bold**`, `*italic*`, `~~strike~~`, `` `code` ``, `[text](url)` and the
//! tags `<u>`, `<mark>`, `<font color|size="…">`. Blocks (line prefixes): `# `, `## `,
//! `- `, `1. `, `> `, `| table | rows |` and `<div align="…">…</div>`.

fn esc(s: &str) -> String {
    s.replace('&', "&amp;").replace('<', "&lt;").replace('>', "&gt;")
}

/// Re-enable the whitelisted tags in already-escaped text.
fn allow_tags(s: &str) -> String {
    let mut s = s.to_string();
    for t in ["u", "mark"] {
        s = s.replace(&format!("&lt;{t}&gt;"), &format!("<{t}>")).replace(&format!("&lt;/{t}&gt;"), &format!("</{t}>"));
    }
    s = s.replace("&lt;/font&gt;", "</font>").replace("&lt;/div&gt;", "</div>");
    for (open, attrs) in [("&lt;font ", &["color", "size"][..]), ("&lt;div ", &["align"][..])] {
        let tag = if open.contains("font") { "font" } else { "div" };
        let mut out = String::new();
        let mut rest = s.as_str();
        while let Some(p) = rest.find(open) {
            out.push_str(&rest[..p]);
            let after = &rest[p + open.len()..];
            let ok = after.find("&gt;").and_then(|e| {
                let inner = &after[..e];
                let (k, v) = inner.split_once('=')?;
                let v = v.trim_matches('"');
                (attrs.contains(&k) && !v.is_empty() && v.chars().all(|c| c.is_ascii_alphanumeric() || c == '#'))
                    .then(|| (format!("<{tag} {k}=\"{v}\">"), e + 4))
            });
            match ok {
                Some((t, n)) => { out.push_str(&t); rest = &after[n..]; }
                None => { out.push_str(open); rest = after; }
            }
        }
        out.push_str(rest);
        s = out;
    }
    s
}

/// Replace `d…d` pairs (non-empty, not starting/ending with a space) with `<t>…</t>`.
fn pairs(s: &str, d: &str, t: &str) -> String {
    let mut out = String::new();
    let mut rest = s;
    loop {
        let Some(a) = rest.find(d) else { break };
        let after = &rest[a + d.len()..];
        let close = after.find(d).filter(|&b| b > 0 && !after.starts_with(' ') && !after[..b].ends_with(' '));
        match close {
            Some(b) => {
                out.push_str(&rest[..a]);
                out.push_str(&format!("<{t}>{}</{t}>", &after[..b]));
                rest = &after[b + d.len()..];
            }
            None => { out.push_str(&rest[..a + d.len()]); rest = after; }
        }
    }
    out.push_str(rest);
    out
}

fn links(s: &str) -> String {
    let mut out = String::new();
    let mut rest = s;
    while let Some(a) = rest.find('[') {
        let after = &rest[a + 1..];
        let hit = after.find("](").and_then(|m| {
            let url_part = &after[m + 2..];
            let e = url_part.find(')')?;
            let url = &url_part[..e];
            let safe = ["http://", "https://", "mailto:"].iter().any(|p| url.starts_with(p)) && !url.contains('"') && !url.contains(' ');
            safe.then(|| (format!("<a href=\"{url}\">{}</a>", &after[..m]), m + 2 + e + 1))
        });
        match hit {
            Some((h, n)) => { out.push_str(&rest[..a]); out.push_str(&h); rest = &after[n..]; }
            None => { out.push_str(&rest[..a + 1]); rest = after; }
        }
    }
    out.push_str(rest);
    out
}

fn inline(line: &str) -> String {
    // code spans first, so their content is left alone
    let n = line.matches('`').count();
    let mut out = String::new();
    for (i, seg) in line.split('`').enumerate() {
        if i % 2 == 1 && i < n {
            out.push_str(&format!("<code style=\"background:#f1f1f1;padding:1px 4px;border-radius:3px;font-family:monospace\">{}</code>", esc(seg)));
        } else {
            if i % 2 == 1 { out.push('`'); }
            let s = allow_tags(&esc(seg));
            let s = pairs(&s, "**", "strong");
            let s = pairs(&s, "~~", "s");
            let s = pairs(&s, "*", "em");
            out.push_str(&links(&s));
        }
    }
    out
}

fn table_row(line: &str, head: bool) -> String {
    let t = if head { "th" } else { "td" };
    let cells: Vec<&str> = line.trim().trim_matches('|').split('|').collect();
    let mut r = String::from("<tr>");
    for c in cells {
        r.push_str(&format!("<{t} style=\"border:1px solid #ccc;padding:4px 8px\">{}</{t}>", inline(c.trim())));
    }
    r.push_str("</tr>");
    r
}

fn is_sep(line: &str) -> bool {
    let t = line.trim();
    t.starts_with('|') && t.chars().all(|c| matches!(c, '|' | '-' | ':' | ' '))
}

fn numbered(line: &str) -> Option<&str> {
    let d = line.find(". ")?;
    (d > 0 && line[..d].chars().all(|c| c.is_ascii_digit())).then(|| &line[d + 2..])
}

/// Convert composer markup to an HTML document body.
pub fn to_html(src: &str) -> String {
    let lines: Vec<&str> = src.lines().collect();
    let mut h = String::from("<div style=\"font-family:sans-serif;font-size:14px\">");
    let mut i = 0;
    while i < lines.len() {
        let l = lines[i];
        if l.trim_start().starts_with('|') {
            h.push_str("<table style=\"border-collapse:collapse\">");
            let head = i + 1 < lines.len() && is_sep(lines[i + 1]);
            let mut first = true;
            while i < lines.len() && lines[i].trim_start().starts_with('|') {
                if !is_sep(lines[i]) { h.push_str(&table_row(lines[i], head && first)); first = false; }
                i += 1;
            }
            h.push_str("</table>");
            continue;
        }
        if l.starts_with("- ") || l.starts_with("* ") {
            h.push_str("<ul>");
            while i < lines.len() && (lines[i].starts_with("- ") || lines[i].starts_with("* ")) {
                h.push_str(&format!("<li>{}</li>", inline(&lines[i][2..])));
                i += 1;
            }
            h.push_str("</ul>");
            continue;
        }
        if numbered(l).is_some() {
            h.push_str("<ol>");
            while i < lines.len() && numbered(lines[i]).is_some() {
                h.push_str(&format!("<li>{}</li>", inline(numbered(lines[i]).unwrap())));
                i += 1;
            }
            h.push_str("</ol>");
            continue;
        }
        if l.starts_with('>') {
            h.push_str("<blockquote style=\"margin:0 0 0 4px;padding-left:10px;border-left:3px solid #ccc;color:#555\">");
            let mut inner = Vec::new();
            while i < lines.len() && lines[i].starts_with('>') {
                let q = &lines[i][1..];
                inner.push(q.strip_prefix(' ').unwrap_or(q));
                i += 1;
            }
            let body = to_html(&inner.join("\n"));
            h.push_str(&body);
            h.push_str("</blockquote>");
            continue;
        }
        if let Some(t) = l.strip_prefix("## ") {
            h.push_str(&format!("<h2>{}</h2>", inline(t)));
        } else if let Some(t) = l.strip_prefix("# ") {
            h.push_str(&format!("<h1>{}</h1>", inline(t)));
        } else {
            let s = inline(l);
            let block = (s.starts_with("<div ") && !s.contains("</div>")) || s == "</div>";
            h.push_str(&s);
            if !block { h.push_str("<br>"); }
        }
        i += 1;
    }
    h.push_str("</div>");
    h
}

/// Plain-text part: keep the Markdown-ish symbols (readable as is), drop the HTML-ish tags.
pub fn to_text(src: &str) -> String {
    let mut s = src.to_string();
    for t in ["<u>", "</u>", "<mark>", "</mark>", "</font>", "</div>"] { s = s.replace(t, ""); }
    for open in ["<font ", "<div "] {
        while let Some(p) = s.find(open) {
            match s[p..].find('>') { Some(e) => s.replace_range(p..p + e + 1, ""), None => break }
        }
    }
    s
}

/// Remove all markup from a span of text (the "clear formatting" button).
pub fn strip(src: &str) -> String {
    let s = to_text(src).replace("**", "").replace("~~", "").replace('`', "");
    s.lines().map(|l| {
        let l = l.strip_prefix("## ").or_else(|| l.strip_prefix("# ")).or_else(|| l.strip_prefix("- "))
            .or_else(|| l.strip_prefix("> ")).or_else(|| numbered(l)).unwrap_or(l);
        l.to_string()
    }).collect::<Vec<_>>().join("\n")
}

/// Apply a toolbar action to `body` with the selection `[a, b]` (UTF-8 byte offsets,
/// either order). Returns the new text and the new selection (anchor, cursor).
pub fn apply(body: &str, kind: &str, a: i32, b: i32) -> (String, i32, i32) {
    let clamp = |x: i32| -> usize {
        let mut x = (x.max(0) as usize).min(body.len());
        while !body.is_char_boundary(x) { x -= 1; }
        x
    };
    let (s, e) = { let (x, y) = (clamp(a), clamp(b)); (x.min(y), x.max(y)) };
    let sel = &body[s..e];
    let wrap = |l: &str, r: &str, placeholder: &str| -> (String, i32, i32) {
        let inner = if sel.is_empty() { placeholder } else { sel };
        let t = format!("{}{l}{inner}{r}{}", &body[..s], &body[e..]);
        let st = (s + l.len()) as i32;
        (t, st, st + inner.len() as i32)
    };
    let line_start = body[..s].rfind('\n').map(|p| p + 1).unwrap_or(0);
    let line_end = body[e..].find('\n').map(|p| e + p).unwrap_or(body.len());
    let prefix = |p: &dyn Fn(usize) -> String| -> (String, i32, i32) {
        let block = &body[line_start..line_end];
        let lines: Vec<&str> = block.split('\n').collect();
        let all_have = lines.iter().enumerate().all(|(i, l)| l.starts_with(&p(i)) || (p(0) == "1. " && numbered(l).is_some()));
        let new: Vec<String> = lines.iter().enumerate().map(|(i, l)| {
            if all_have {
                if p(0) == "1. " { numbered(l).unwrap_or(l).to_string() } else { l[p(i).len()..].to_string() }
            } else {
                let base = l.strip_prefix("## ").or_else(|| l.strip_prefix("# ")).or_else(|| l.strip_prefix("- "))
                    .or_else(|| l.strip_prefix("> ")).or_else(|| numbered(l)).unwrap_or(l);
                format!("{}{base}", p(i))
            }
        }).collect();
        let nb = new.join("\n");
        let t = format!("{}{nb}{}", &body[..line_start], &body[line_end..]);
        let end = (line_start + nb.len()) as i32;
        (t, end, end)
    };
    match kind {
        "bold" => wrap("**", "**", "bold text"),
        "italic" => wrap("*", "*", "italic text"),
        "underline" => wrap("<u>", "</u>", "underlined text"),
        "strike" => wrap("~~", "~~", "struck text"),
        "highlight" => wrap("<mark>", "</mark>", "highlighted text"),
        "code" => wrap("`", "`", "code"),
        "link" => {
            let label = if sel.is_empty() { "link text" } else { sel };
            let t = format!("{}[{label}](https://){}", &body[..s], &body[e..]);
            let u = (s + 1 + label.len() + 2) as i32;
            (t, u, u + 8)
        }
        "h1" => prefix(&|_| "# ".into()),
        "h2" => prefix(&|_| "## ".into()),
        "bullet" => prefix(&|_| "- ".into()),
        "quote" => prefix(&|_| "> ".into()),
        "numbered" => prefix(&|i| format!("{}. ", i + 1)),
        "table" => {
            let lead = if s == 0 || body[..s].ends_with('\n') { "" } else { "\n" };
            let tbl = "| Column 1 | Column 2 |\n| --- | --- |\n|  |  |\n";
            let t = format!("{}{lead}{tbl}{}", &body[..s], &body[s..]);
            let c = (s + lead.len() + 2) as i32;
            (t, c, c + 8)
        }
        "clear" => {
            if sel.is_empty() {
                let nb = strip(&body[line_start..line_end]);
                let t = format!("{}{nb}{}", &body[..line_start], &body[line_end..]);
                let c = (line_start + nb.len()) as i32;
                (t, c, c)
            } else {
                let nb = strip(sel);
                let t = format!("{}{nb}{}", &body[..s], &body[e..]);
                (t, s as i32, (s + nb.len()) as i32)
            }
        }
        k if k.starts_with("color:") => wrap(&format!("<font color=\"{}\">", &k[6..]), "</font>", "coloured text"),
        k if k.starts_with("size:") => wrap(&format!("<font size=\"{}\">", &k[5..]), "</font>", "text"),
        k if k.starts_with("align:") => {
            let l = format!("<div align=\"{}\">\n", &k[6..]);
            let (s2, e2) = (line_start, line_end);
            let inner = &body[s2..e2];
            let t = format!("{}{l}{inner}\n</div>{}", &body[..s2], &body[e2..]);
            let c = (s2 + l.len() + inner.len()) as i32;
            (t, c, c)
        }
        _ => (body.to_string(), a, b),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn html() {
        let h = to_html("Hi **there** *you* <u>u</u> ~~x~~ `a<b`\n# Title\n- one\n- two\n1. a\n2. b\n> quoted\n[site](https://x.org)\n<font color=\"#dc2626\">red</font>\n<div align=\"center\">\nmid\n</div>\n| A | B |\n| --- | --- |\n| 1 | 2 |\n<script>");
        assert!(h.contains("<strong>there</strong>") && h.contains("<em>you</em>") && h.contains("<u>u</u>") && h.contains("<s>x</s>"));
        assert!(h.contains("a&lt;b") && h.contains("<h1>Title</h1>") && h.contains("<ul><li>one</li><li>two</li></ul>"));
        assert!(h.contains("<ol><li>a</li><li>b</li></ol>") && h.contains("<blockquote") && h.contains("<a href=\"https://x.org\">site</a>"));
        assert!(h.contains("<font color=\"#dc2626\">red</font>") && h.contains("<div align=\"center\">") && h.contains("<th"));
        assert!(h.contains("&lt;script&gt;") && !h.contains("<script>"));
        assert_eq!(to_text("<u>a</u> <font size=\"5\">b</font>"), "a b");
    }
    #[test]
    fn apply_ops() {
        assert_eq!(apply("hello world", "bold", 0, 5).0, "**hello** world");
        assert_eq!(apply("a\nb", "bullet", 0, 3).0, "- a\n- b");
        assert_eq!(apply("- a\n- b", "bullet", 0, 7).0, "a\nb");
        assert_eq!(apply("a\nb", "numbered", 0, 3).0, "1. a\n2. b");
        assert_eq!(apply("**x** y", "clear", 0, 7).0, "x y");
    }
}
