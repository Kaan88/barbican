//! HTML in (tolerant subset parser for replies, drafts, templates, signatures, pasted
//! HTML) and email-safe HTML out (inline styles), plus the text/plain rendering.
use super::model::*;
use std::sync::Arc;

// ---------------------------------------------------------------- tokenizer

#[derive(Debug)]
enum Tok {
    Open { name: String, attrs: Vec<(String, String)>, self_close: bool },
    Close(String),
    Text(String),
}

pub fn decode_entities(s: &str) -> String {
    if !s.contains('&') { return s.to_string(); }
    let mut out = String::with_capacity(s.len());
    let mut rest = s;
    while let Some(p) = rest.find('&') {
        out.push_str(&rest[..p]);
        let after = &rest[p + 1..];
        let end = after.find(';').filter(|&e| e <= 10);
        let decoded = end.and_then(|e| {
            let name = &after[..e];
            let c = if let Some(num) = name.strip_prefix("#x").or_else(|| name.strip_prefix("#X")) {
                u32::from_str_radix(num, 16).ok().and_then(char::from_u32)
            } else if let Some(num) = name.strip_prefix('#') {
                num.parse::<u32>().ok().and_then(char::from_u32)
            } else {
                Some(match name {
                    "amp" => '&', "lt" => '<', "gt" => '>', "quot" => '"', "apos" => '\'', "nbsp" => '\u{a0}',
                    "mdash" => '—', "ndash" => '–', "hellip" => '…', "copy" => '©', "reg" => '®', "trade" => '™',
                    "rsquo" => '’', "lsquo" => '‘', "ldquo" => '“', "rdquo" => '”', "euro" => '€', "bull" => '•',
                    "middot" => '·', "laquo" => '«', "raquo" => '»', "times" => '×', "deg" => '°', "zwnj" => '\u{200c}',
                    _ => return None,
                })
            };
            c.map(|c| (c, e))
        });
        match decoded {
            Some((c, e)) => { out.push(c); rest = &after[e + 1..]; }
            None => { out.push('&'); rest = after; }
        }
    }
    out.push_str(rest);
    out
}

fn tokenize(h: &str) -> Vec<Tok> {
    let mut out = vec![];
    let b = h.as_bytes();
    let mut i = 0;
    while i < b.len() {
        if b[i] == b'<' {
            if h[i..].starts_with("<!--") {
                i = h[i..].find("-->").map(|e| i + e + 3).unwrap_or(b.len());
                continue;
            }
            if h[i..].starts_with("<!") || h[i..].starts_with("<?") {
                i = h[i..].find('>').map(|e| i + e + 1).unwrap_or(b.len());
                continue;
            }
            // find the end of the tag, respecting quotes
            let mut j = i + 1;
            let mut q: Option<u8> = None;
            while j < b.len() {
                match (q, b[j]) {
                    (None, b'"') | (None, b'\'') => q = Some(b[j]),
                    (Some(c), x) if c == x => q = None,
                    (None, b'>') => break,
                    _ => {}
                }
                j += 1;
            }
            let inner = &h[i + 1..j.min(b.len())];
            i = j + 1;
            if inner.is_empty() || !(inner.as_bytes()[0].is_ascii_alphabetic() || inner.starts_with('/')) {
                out.push(Tok::Text(format!("<{inner}>")));
                continue;
            }
            if let Some(name) = inner.strip_prefix('/') {
                out.push(Tok::Close(name.trim().split_whitespace().next().unwrap_or("").to_ascii_lowercase()));
                continue;
            }
            let self_close = inner.trim_end().ends_with('/');
            let inner = inner.trim_end().trim_end_matches('/');
            let name_end = inner.find(|c: char| c.is_whitespace()).unwrap_or(inner.len());
            let name = inner[..name_end].to_ascii_lowercase();
            let attrs = parse_attrs(&inner[name_end..]);
            // raw text elements
            if matches!(name.as_str(), "style" | "script" | "head" | "title" | "xml") && !self_close {
                let close = format!("</{name}");
                let lower = h[i.min(b.len())..].to_ascii_lowercase();
                match lower.find(&close) {
                    Some(e) => { i += e; i = h[i..].find('>').map(|x| i + x + 1).unwrap_or(b.len()); }
                    None => i = b.len(),
                }
                continue;
            }
            out.push(Tok::Open { name, attrs, self_close });
        } else {
            let next = h[i..].find('<').map(|e| i + e).unwrap_or(b.len());
            out.push(Tok::Text(decode_entities(&h[i..next])));
            i = next;
        }
    }
    out
}

fn parse_attrs(s: &str) -> Vec<(String, String)> {
    let mut out = vec![];
    let c: Vec<char> = s.chars().collect();
    let mut i = 0;
    while i < c.len() {
        while i < c.len() && (c[i].is_whitespace() || c[i] == '/') { i += 1; }
        let ks = i;
        while i < c.len() && !c[i].is_whitespace() && c[i] != '=' { i += 1; }
        let key: String = c[ks..i].iter().collect::<String>().to_ascii_lowercase();
        while i < c.len() && c[i].is_whitespace() { i += 1; }
        let mut val = String::new();
        if i < c.len() && c[i] == '=' {
            i += 1;
            while i < c.len() && c[i].is_whitespace() { i += 1; }
            if i < c.len() && (c[i] == '"' || c[i] == '\'') {
                let qc = c[i];
                i += 1;
                let vs = i;
                while i < c.len() && c[i] != qc { i += 1; }
                val = c[vs..i].iter().collect();
                i += 1;
            } else {
                let vs = i;
                while i < c.len() && !c[i].is_whitespace() { i += 1; }
                val = c[vs..i].iter().collect();
            }
        }
        if !key.is_empty() { out.push((key, decode_entities(&val))); }
    }
    out
}

fn attr<'a>(attrs: &'a [(String, String)], k: &str) -> Option<&'a str> {
    attrs.iter().find(|(a, _)| a == k).map(|(_, v)| v.as_str())
}

fn style_prop(attrs: &[(String, String)], prop: &str) -> Option<String> {
    let st = attr(attrs, "style")?;
    st.split(';').filter_map(|d| d.split_once(':')).find(|(k, _)| k.trim().eq_ignore_ascii_case(prop))
        .map(|(_, v)| v.trim().trim_end_matches("!important").trim().to_string())
}

/// Normalize a CSS colour to `#rrggbb` when possible.
pub fn norm_color(v: &str) -> Option<String> {
    let v = v.trim().to_ascii_lowercase();
    if let Some(h) = v.strip_prefix('#') {
        return match h.len() {
            3 => Some(format!("#{}", h.chars().flat_map(|c| [c, c]).collect::<String>())),
            6 => Some(format!("#{h}")),
            8 => Some(format!("#{}", &h[..6])),
            _ => None,
        };
    }
    if let Some(inner) = v.strip_prefix("rgba(").or_else(|| v.strip_prefix("rgb(")) {
        let n: Vec<f32> = inner.trim_end_matches(')').split(',').filter_map(|x| x.trim().parse().ok()).collect();
        if n.len() >= 3 {
            if n.len() == 4 && n[3] == 0.0 { return None; }
            return Some(format!("#{:02x}{:02x}{:02x}", n[0] as u8, n[1] as u8, n[2] as u8));
        }
        return None;
    }
    let named = match v.as_str() {
        "red" => "#ff0000", "green" => "#008000", "blue" => "#0000ff", "black" => "#000000", "white" => "#ffffff",
        "yellow" => "#ffff00", "orange" => "#ffa500", "purple" => "#800080", "gray" | "grey" => "#808080",
        "navy" => "#000080", "maroon" => "#800000", "teal" => "#008080", "silver" => "#c0c0c0",
        _ => return None,
    };
    Some(named.into())
}

fn css_size_px(v: &str) -> Option<u8> {
    let v = v.trim().to_ascii_lowercase();
    let (num, mul) = if let Some(n) = v.strip_suffix("px") { (n, 1.0) } else if let Some(n) = v.strip_suffix("pt") { (n, 4.0 / 3.0) }
        else if let Some(n) = v.strip_suffix("em") { (n, 14.0) } else if let Some(n) = v.strip_suffix("rem") { (n, 14.0) }
        else { match v.as_str() { "small" | "x-small" => return Some(12), "large" => return Some(18), "x-large" => return Some(24), "xx-large" => return Some(30), _ => return None } };
    num.trim().parse::<f32>().ok().map(|n| (n * mul).round().clamp(6.0, 72.0) as u8)
}

fn font_size_attr(v: &str) -> Option<u8> {
    Some(match v.trim() { "1" => 10, "2" => 12, "3" => 14, "4" => 18, "5" => 24, "6" => 32, "7" => 48, _ => return None })
}

// ---------------------------------------------------------------- import

struct Builder {
    blocks: Vec<Block>,
    cur: Option<Block>,
    marks: Vec<(String, Marks)>,
    lists: Vec<bool>,
    quote: usize,
    pre: usize,
    aligns: Vec<(String, Align)>,
    table: Option<(u32, i32, i32)>,
    table_depth: usize,
    next_table: u32,
    heading: Option<u8>,
    pending_space: bool,
}

impl Builder {
    fn marks(&self) -> Marks { self.marks.last().map(|m| m.1.clone()).unwrap_or_default() }
    fn align(&self) -> Align { self.aligns.last().map(|a| a.1).unwrap_or_default() }

    fn kind(&self) -> Kind {
        if let Some((t, r, c)) = self.table { if self.table_depth == 1 { return Kind::Cell { table: t, row: r.max(0) as u32, col: (c - 1).max(0) as u32, header: false }; } }
        if self.pre > 0 { return Kind::Code; }
        if let Some(h) = self.heading { return Kind::Heading(h); }
        if self.quote > 0 { return Kind::Quote; }
        Kind::Para
    }

    fn in_cell(&self) -> bool { self.table.is_some() && self.table_depth >= 1 && self.cur.as_ref().is_some_and(|b| b.kind.is_cell()) }

    fn ensure(&mut self) -> &mut Block {
        if self.cur.is_none() {
            let mut b = Block::new(self.kind());
            b.align = self.align();
            self.cur = Some(b);
        }
        self.cur.as_mut().unwrap()
    }

    fn flush(&mut self) {
        if let Some(mut b) = self.cur.take() {
            // trailing <br> placeholder and trailing whitespace
            if let Some(r) = b.runs.last_mut() {
                if b.kind != Kind::Code { let t = r.text.trim_end_matches([' ', '\t']).to_string(); r.text = t; }
                if r.text.ends_with('\n') { r.text.pop(); }
            }
            b.normalize();
            self.blocks.push(b);
        }
        self.pending_space = false;
    }

    /// Start a block-level element: inside table cells this is just a line break.
    fn block_break(&mut self) {
        if self.in_cell() {
            let b = self.cur.as_mut().unwrap();
            if !b.is_empty() && !b.text().ends_with('\n') { let m = b.marks_at(b.len()); let l = b.len(); b.insert(l, "\n", &m); }
            self.pending_space = false;
            return;
        }
        if let Some(c) = &self.cur {
            if c.is_empty() { if !c.kind.is_list() { self.cur = None; } return; }
            // a <p> inside a list item continues the item on a new line
            if c.kind.is_list() && !self.lists.is_empty() {
                let b = self.cur.as_mut().unwrap();
                if !b.text().ends_with('\n') { let m = b.marks_at(b.len()); let l = b.len(); b.insert(l, "\n", &m); }
                return;
            }
        }
        self.flush();
    }

    fn text(&mut self, t: &str) {
        if self.pre > 0 {
            let m = self.marks();
            let b = self.ensure();
            let l = b.len();
            b.insert(l, t, &m);
            return;
        }
        // collapse whitespace (a space stays with the text it followed)
        let mut prev_space = self.cur.as_ref().is_none_or(|b| { let t = b.text(); t.is_empty() || t.ends_with(' ') || t.ends_with('\n') });
        let mut s = String::new();
        for c in t.chars() {
            if c.is_whitespace() && c != '\u{a0}' {
                if !prev_space { s.push(' '); prev_space = true; }
                continue;
            }
            s.push(if c == '\u{a0}' { ' ' } else { c });
            prev_space = false;
        }
        if s.is_empty() { return; }
        let m = self.marks();
        let b = self.ensure();
        let l = b.len();
        b.insert(l, &s, &m);
    }

    fn push_marks(&mut self, name: &str, f: impl FnOnce(&mut Marks)) {
        let mut m = self.marks();
        f(&mut m);
        self.marks.push((name.to_string(), m));
    }

    fn pop_to(&mut self, name: &str) {
        if let Some(i) = self.marks.iter().rposition(|(n, _)| n == name) { self.marks.truncate(i); }
    }
}

fn inline_style(m: &mut Marks, attrs: &[(String, String)]) {
    if let Some(w) = style_prop(attrs, "font-weight") { m.bold = w == "bold" || w == "bolder" || w.parse::<u32>().is_ok_and(|n| n >= 600); }
    if let Some(s) = style_prop(attrs, "font-style") { m.italic = s == "italic" || s == "oblique"; }
    if let Some(d) = style_prop(attrs, "text-decoration").or_else(|| style_prop(attrs, "text-decoration-line")) {
        if d.contains("underline") { m.underline = true; }
        if d.contains("line-through") { m.strike = true; }
    }
    if let Some(c) = style_prop(attrs, "color").and_then(|c| norm_color(&c)) { m.color = Some(c); }
    if let Some(c) = style_prop(attrs, "background-color").or_else(|| style_prop(attrs, "background")).and_then(|c| norm_color(&c)) {
        if c != "#ffffff" { m.highlight = Some(c); }
    }
    if let Some(s) = style_prop(attrs, "font-size").and_then(|s| css_size_px(&s)) { m.size = (s != 14).then_some(s); }
}

fn image_from(attrs: &[(String, String)]) -> Option<ImageData> {
    let src = attr(attrs, "src")?.trim().to_string();
    if src.is_empty() { return None; }
    let mut img = ImageData {
        src: src.clone(), alt: attr(attrs, "alt").unwrap_or("").into(),
        width: attr(attrs, "width").and_then(|w| w.trim().trim_end_matches("px").parse().ok())
            .or_else(|| style_prop(attrs, "width").and_then(|w| w.trim().strip_suffix("px")?.trim().parse::<f32>().ok()).map(|w| w as u32))
            .unwrap_or(0),
        height: attr(attrs, "height").and_then(|w| w.trim_end_matches("px").parse().ok()).unwrap_or(0),
        ..Default::default()
    };
    if let Some(c) = src.strip_prefix("cid:") { img.cid = Some(c.to_string()); }
    if let Some(data) = src.strip_prefix("data:") {
        if let Some((meta, b64)) = data.split_once(',') {
            if meta.ends_with(";base64") {
                use base64::Engine;
                if let Ok(bytes) = base64::engine::general_purpose::STANDARD.decode(b64.trim()) {
                    img.mime = meta.trim_end_matches(";base64").into();
                    img.bytes = Some(Arc::new(bytes));
                }
            }
        }
    }
    Some(img)
}

/// Parse an HTML fragment into blocks.
pub fn import(html: &str) -> Vec<Block> {
    let mut b = Builder {
        blocks: vec![], cur: None, marks: vec![], lists: vec![], quote: 0, pre: 0, aligns: vec![], table: None,
        table_depth: 0, next_table: 0, heading: None, pending_space: false,
    };
    for t in tokenize(html) {
        match t {
            Tok::Text(s) => b.text(&s),
            Tok::Open { name, attrs, self_close } => {
                let n = name.as_str();
                // block alignment
                let align = style_prop(&attrs, "text-align").or_else(|| attr(&attrs, "align").map(String::from)).map(|a| match a.to_ascii_lowercase().as_str() {
                    "center" => Align::Center, "right" | "end" => Align::Right, "justify" => Align::Justify, _ => Align::Left,
                });
                match n {
                    "br" => {
                        let m = b.marks();
                        let blk = b.ensure();
                        let l = blk.len();
                        blk.insert(l, "\n", &m);
                        b.pending_space = false;
                    }
                    "p" | "div" | "section" | "article" | "header" | "footer" | "center" | "address" | "main" | "aside" | "nav" | "figure" | "dd" | "dt" | "dl" => {
                        b.block_break();
                        let a = if n == "center" { Some(Align::Center) } else { align };
                        if !self_close { b.aligns.push((n.to_string(), a.unwrap_or(b.align()))); }
                        if let Some(c) = &mut b.cur { if let Some(a) = a { c.align = a; } }
                        // block-level inline styles (e.g. <div style="font-weight:bold">)
                        if !self_close && attr(&attrs, "style").is_some() { b.push_marks(n, |m| inline_style(m, &attrs)); }
                    }
                    "h1" | "h2" | "h3" | "h4" | "h5" | "h6" => {
                        b.block_break();
                        b.flush();
                        b.heading = Some((n.as_bytes()[1] - b'0').min(3));
                        b.aligns.push((n.to_string(), align.unwrap_or(b.align())));
                    }
                    "blockquote" => { b.block_break(); b.flush(); b.quote += 1; }
                    "pre" => { b.block_break(); b.flush(); b.pre += 1; }
                    "ul" | "ol" => { b.block_break(); if !b.in_cell() { b.flush(); } b.lists.push(n == "ol"); }
                    "li" => {
                        if b.in_cell() { b.block_break(); continue; }
                        b.flush();
                        let mut blk = Block::new(if b.lists.last().copied().unwrap_or(false) { Kind::Ordered } else { Kind::Bullet });
                        blk.indent = b.lists.len().saturating_sub(1) as u8;
                        blk.align = b.align();
                        b.cur = Some(blk);
                    }
                    "hr" => { b.block_break(); b.flush(); b.blocks.push(Block::new(Kind::Rule)); }
                    "table" => {
                        b.table_depth += 1;
                        if b.table_depth == 1 {
                            b.block_break(); b.flush();
                            b.table = Some((b.next_table, -1, 0));
                            b.next_table += 1;
                        } else { b.block_break(); }
                    }
                    "tr" => {
                        if b.table_depth == 1 { if let Some(t) = &mut b.table { t.1 += 1; t.2 = 0; } b.flush(); } else { b.block_break(); }
                    }
                    "td" | "th" => {
                        if b.table_depth == 1 {
                            b.flush();
                            if let Some(t) = &mut b.table {
                                if t.1 < 0 { t.1 = 0; }
                                t.2 += 1;
                                let mut blk = Block::new(Kind::Cell { table: t.0, row: t.1 as u32, col: (t.2 - 1) as u32, header: n == "th" });
                                blk.align = align.unwrap_or_default();
                                b.cur = Some(blk);
                            }
                            if n == "th" { b.push_marks("th", |m| m.bold = true); }
                        } else {
                            let m = b.marks();
                            if let Some(c) = &mut b.cur { if !c.is_empty() { let l = c.len(); c.insert(l, " ", &m); } }
                        }
                    }
                    "img" => {
                        if let Some(img) = image_from(&attrs) {
                            if b.in_cell() { b.text(&format!("[{}]", if img.alt.is_empty() { "image" } else { &img.alt })); continue; }
                            b.block_break();
                            b.flush();
                            let mut blk = Block::new(Kind::Image(img));
                            blk.align = b.align();
                            b.blocks.push(blk);
                        }
                    }
                    "b" | "strong" => b.push_marks(n, |m| { m.bold = true; inline_style(m, &attrs) }),
                    "i" | "em" | "cite" | "var" => b.push_marks(n, |m| { m.italic = true; inline_style(m, &attrs) }),
                    "u" | "ins" => b.push_marks(n, |m| { m.underline = true; inline_style(m, &attrs) }),
                    "s" | "strike" | "del" => b.push_marks(n, |m| { m.strike = true; inline_style(m, &attrs) }),
                    "code" | "tt" | "kbd" | "samp" => { let pre = b.pre > 0; b.push_marks(n, |m| m.code = !pre) }
                    "mark" => b.push_marks(n, |m| { m.highlight = Some("#fef08a".into()); inline_style(m, &attrs) }),
                    "a" => {
                        let href = attr(&attrs, "href").map(String::from);
                        b.push_marks(n, |m| { m.link = href.filter(|h| !h.is_empty() && !h.starts_with('#')); inline_style(m, &attrs) })
                    }
                    "font" => b.push_marks(n, |m| {
                        if let Some(c) = attr(&attrs, "color").and_then(norm_color) { m.color = Some(c); }
                        if let Some(s) = attr(&attrs, "size").and_then(font_size_attr) { m.size = (s != 14).then_some(s); }
                        inline_style(m, &attrs);
                    }),
                    "span" | "sub" | "sup" | "small" | "big" | "abbr" | "label" | "o:p" => {
                        if !self_close { b.push_marks(n, |m| { if n == "small" { m.size = Some(12); } inline_style(m, &attrs) }); }
                    }
                    _ => {}
                }
            }
            Tok::Close(name) => {
                let n = name.as_str();
                match n {
                    "p" | "div" | "section" | "article" | "header" | "footer" | "center" | "address" | "main" | "aside" | "nav" | "figure" | "dd" | "dt" | "dl" => {
                        if b.in_cell() {
                            b.block_break();
                        } else if b.cur.as_ref().is_some_and(|c| c.kind.is_list()) {
                            // </p> inside <li>: keep the item open
                        } else if b.cur.is_none() && matches!(n, "p") {
                            // empty <p></p> still makes a line
                            let mut e = Block::new(b.kind());
                            e.align = b.align();
                            b.blocks.push(e);
                        } else {
                            b.flush();
                        }
                        if let Some(i) = b.aligns.iter().rposition(|(t, _)| t == n) { b.aligns.truncate(i); }
                        b.pop_to(n);
                    }
                    "h1" | "h2" | "h3" | "h4" | "h5" | "h6" => {
                        b.flush();
                        b.heading = None;
                        if let Some(i) = b.aligns.iter().rposition(|(t, _)| t == n) { b.aligns.truncate(i); }
                    }
                    "blockquote" => { b.flush(); b.quote = b.quote.saturating_sub(1); }
                    "pre" => { b.flush(); b.pre = b.pre.saturating_sub(1); }
                    "ul" | "ol" => { if !b.in_cell() { b.flush(); } b.lists.pop(); }
                    "li" => { if !b.in_cell() { b.flush(); } }
                    "td" | "th" => {
                        if b.table_depth == 1 { b.flush(); b.pop_to("th"); }
                    }
                    "tr" => { if b.table_depth == 1 { b.flush(); } }
                    "table" => {
                        if b.table_depth == 1 { b.flush(); b.table = None; }
                        b.table_depth = b.table_depth.saturating_sub(1);
                    }
                    _ => b.pop_to(n),
                }
            }
        }
    }
    b.flush();
    let mut blocks = b.blocks;
    // drop leading/trailing empty paragraphs produced by wrappers
    while blocks.len() > 1 && blocks.last().is_some_and(|x| x.kind == Kind::Para && x.is_empty()) { blocks.pop(); }
    pad_tables(&mut blocks);
    blocks
}

/// Make every table rectangular.
fn pad_tables(blocks: &mut Vec<Block>) {
    let mut i = 0;
    while i < blocks.len() {
        let Some(t) = blocks[i].kind.table() else { i += 1; continue };
        let s = i;
        while i < blocks.len() && blocks[i].kind.table() == Some(t) { i += 1; }
        let d = Doc { blocks: blocks[s..i].to_vec() };
        let r = 0..d.blocks.len();
        let (rows, cols) = d.table_dims(&r);
        if rows as usize * cols as usize != i - s {
            let grid = d.table_grid(&r);
            let mut cells = vec![];
            for (ri, row) in grid.into_iter().enumerate() {
                for (ci, mut c) in row.into_iter().enumerate() {
                    let header = matches!(c.kind, Kind::Cell { header: true, .. });
                    c.kind = Kind::Cell { table: t, row: ri as u32, col: ci as u32, header };
                    cells.push(c);
                }
            }
            let n = cells.len();
            blocks.splice(s..i, cells);
            i = s + n;
        }
    }
}

/// Plain text → blocks (one paragraph per line).
pub fn import_text(text: &str) -> Vec<Block> {
    text.replace("\r\n", "\n").split('\n').map(Block::para).collect()
}

// ---------------------------------------------------------------- export

pub fn esc(s: &str) -> String {
    let mut prev_space = true;
    esc_run(s, &mut prev_space)
}

/// Escape text for HTML, keeping runs of spaces visible (`prev_space` carries across runs).
fn esc_run(s: &str, prev_space: &mut bool) -> String {
    let mut out = String::with_capacity(s.len());
    for c in s.chars() {
        match c {
            '&' => out.push_str("&amp;"),
            '<' => out.push_str("&lt;"),
            '>' => out.push_str("&gt;"),
            '"' => out.push_str("&quot;"),
            '\n' => { out.push_str("<br>"); *prev_space = true; continue; }
            ' ' if *prev_space => { out.push_str("&nbsp;"); *prev_space = false; continue; }
            '\t' => { out.push_str("&nbsp;&nbsp;&nbsp;&nbsp;"); *prev_space = false; continue; }
            c => out.push(c),
        }
        *prev_space = c == ' ';
    }
    out
}

fn esc_attr(s: &str) -> String { s.replace('&', "&amp;").replace('"', "&quot;").replace('<', "&lt;") }

const LINK_COLOR: &str = "#2563eb";
const CODE_STYLE: &str = "font-family:Consolas,'Courier New',monospace;background-color:#f1f5f9;border-radius:3px;padding:1px 4px;font-size:90%";

pub fn runs_html(runs: &[Run], in_code: bool) -> String {
    let mut h = String::new();
    let mut prev_space = true;
    for r in runs {
        let m = &r.marks;
        let mut t = if in_code {
            r.text.replace('&', "&amp;").replace('<', "&lt;").replace('>', "&gt;")
        } else { esc_run(&r.text, &mut prev_space) };
        if m.code && !in_code { t = format!("<code style=\"{CODE_STYLE}\">{t}</code>"); }
        if m.strike { t = format!("<s>{t}</s>"); }
        if m.underline { t = format!("<u>{t}</u>"); }
        if m.italic { t = format!("<em>{t}</em>"); }
        if m.bold { t = format!("<strong>{t}</strong>"); }
        let mut style = String::new();
        if let Some(c) = &m.color { style.push_str(&format!("color:{c};")); }
        if let Some(c) = &m.highlight { style.push_str(&format!("background-color:{c};")); }
        if let Some(s) = m.size { style.push_str(&format!("font-size:{s}px;")); }
        if !style.is_empty() { t = format!("<span style=\"{style}\">{t}</span>"); }
        if let Some(l) = &m.link {
            let c = m.color.as_deref().unwrap_or(LINK_COLOR);
            t = format!("<a href=\"{}\" style=\"color:{c};text-decoration:underline\">{t}</a>", esc_attr(l));
        }
        h.push_str(&t);
    }
    h
}

fn align_style(a: Align) -> String {
    if a == Align::Left { String::new() } else { format!("text-align:{};", a.css()) }
}

pub fn image_src(img: &ImageData) -> String {
    if let Some(c) = &img.cid { return format!("cid:{c}"); }
    img.src.clone()
}

/// Export the document as an email-safe HTML body (all styles inline).
pub fn export(doc: &Doc) -> String {
    let bl = &doc.blocks;
    let mut h = String::from("<div style=\"font-family:'Segoe UI',-apple-system,Roboto,Helvetica,Arial,sans-serif;font-size:14px;line-height:1.5\">");
    let mut i = 0;
    while i < bl.len() {
        let b = &bl[i];
        let al = align_style(b.align);
        match &b.kind {
            Kind::Para => {
                let inner = runs_html(&b.runs, false);
                let inner = if inner.is_empty() || b.text().ends_with('\n') { format!("{inner}<br>") } else { inner };
                h.push_str(&format!("<p style=\"margin:4px 0;{al}\">{inner}</p>"));
                i += 1;
            }
            Kind::Heading(l) => {
                let (size, weight) = match l { 1 => (24, 700), 2 => (20, 600), _ => (17, 600) };
                h.push_str(&format!("<h{l} style=\"font-size:{size}px;font-weight:{weight};line-height:1.3;margin:8px 0;{al}\">{}</h{l}>", runs_html(&b.runs, false)));
                i += 1;
            }
            Kind::Quote => {
                h.push_str("<blockquote style=\"margin:8px 0;padding:0 0 0 16px;border-left:3px solid #d4d4d8;color:#52525b\">");
                while i < bl.len() && bl[i].kind == Kind::Quote {
                    let inner = runs_html(&bl[i].runs, false);
                    let al = align_style(bl[i].align);
                    h.push_str(&format!("<p style=\"margin:4px 0;{al}\">{}</p>", if inner.is_empty() { "<br>".into() } else { inner }));
                    i += 1;
                }
                h.push_str("</blockquote>");
            }
            Kind::Code => {
                h.push_str("<pre style=\"font-family:Consolas,'Courier New',monospace;font-size:13px;background-color:#f4f4f5;color:#18181b;border:1px solid #e4e4e7;border-radius:6px;padding:12px;margin:8px 0;white-space:pre-wrap\">");
                let mut first = true;
                while i < bl.len() && bl[i].kind == Kind::Code {
                    if !first { h.push('\n'); }
                    first = false;
                    h.push_str(&runs_html(&bl[i].runs, true));
                    i += 1;
                }
                h.push_str("</pre>");
            }
            Kind::Bullet | Kind::Ordered => {
                // nested lists from indent levels
                let mut stack: Vec<Kind> = vec![];
                while i < bl.len() && bl[i].kind.is_list() {
                    let it = &bl[i];
                    let lvl = it.indent as usize;
                    while stack.len() > lvl + 1 { let k = stack.pop().unwrap(); h.push_str(if k == Kind::Ordered { "</li></ol>" } else { "</li></ul>" }); }
                    if stack.len() == lvl + 1 {
                        if stack[lvl] != it.kind {
                            let k = stack.pop().unwrap();
                            h.push_str(if k == Kind::Ordered { "</li></ol>" } else { "</li></ul>" });
                        } else {
                            h.push_str("</li>");
                        }
                    }
                    while stack.len() < lvl + 1 {
                        let k = if stack.len() == lvl { it.kind.clone() } else { Kind::Bullet };
                        let style = "margin:4px 0;padding-left:24px";
                        h.push_str(&if k == Kind::Ordered { format!("<ol style=\"{style}\">") } else { format!("<ul style=\"{style}\">") });
                        if stack.len() < lvl { h.push_str("<li style=\"list-style:none\">"); }
                        stack.push(k);
                    }
                    let al = align_style(it.align);
                    h.push_str(&format!("<li style=\"margin:2px 0;{al}\">{}", runs_html(&it.runs, false)));
                    i += 1;
                }
                while let Some(k) = stack.pop() { h.push_str(if k == Kind::Ordered { "</li></ol>" } else { "</li></ul>" }); }
            }
            Kind::Cell { .. } => {
                let r = doc.table_range(i).unwrap_or(i..i + 1);
                let grid = doc.table_grid(&r);
                h.push_str("<table style=\"border-collapse:collapse;margin:6px 0\" cellpadding=\"0\" cellspacing=\"0\">");
                for row in &grid {
                    h.push_str("<tr>");
                    for c in row {
                        let header = matches!(c.kind, Kind::Cell { header: true, .. });
                        let tag = if header { "th" } else { "td" };
                        let al = align_style(c.align);
                        let bg = if header { "background-color:#f4f4f5;font-weight:700;" } else { "" };
                        let inner = runs_html(&c.runs, false);
                        h.push_str(&format!("<{tag} style=\"border:1px solid #d4d4d8;padding:6px 10px;vertical-align:top;text-align:left;{bg}{al}\">{}</{tag}>", if inner.is_empty() { "<br>".into() } else { inner }));
                    }
                    h.push_str("</tr>");
                }
                h.push_str("</table>");
                i = r.end;
            }
            Kind::Image(img) => {
                let mut attrs = format!("src=\"{}\" alt=\"{}\"", esc_attr(&image_src(img)), esc_attr(&img.alt));
                let mut style = String::new();
                if img.width > 0 {
                    attrs.push_str(&format!(" width=\"{}\"", img.width));
                    style = format!("width:{}px;", img.width);
                }
                h.push_str(&format!("<div style=\"{al}\"><img {attrs} style=\"{style}max-width:100%;height:auto\"></div>"));
                i += 1;
            }
            Kind::Quoted { html, cite, .. } => {
                if *cite {
                    h.push_str(&format!("<blockquote type=\"cite\" style=\"margin:0 0 0 .8ex;border-left:2px solid #d4d4d8;padding-left:1ex\">{html}</blockquote>"));
                } else {
                    h.push_str(&format!("<div>{html}</div>"));
                }
                i += 1;
            }
            Kind::Rule => { h.push_str("<hr style=\"border:none;border-top:1px solid #d4d4d8;margin:16px 0\">"); i += 1; }
        }
    }
    h.push_str("</div>");
    h
}

/// The text/plain alternative.
pub fn to_text(doc: &Doc) -> String {
    let bl = &doc.blocks;
    let mut out: Vec<String> = vec![];
    let runs_text = |runs: &[Run]| -> String {
        runs.iter().map(|r| match &r.marks.link {
            Some(l) if l.trim_start_matches("mailto:") != r.text.trim() && !r.text.contains(l.as_str()) => format!("{} <{l}>", r.text),
            _ => r.text.clone(),
        }).collect()
    };
    let mut i = 0;
    while i < bl.len() {
        let b = &bl[i];
        match &b.kind {
            Kind::Para | Kind::Code => out.push(runs_text(&b.runs)),
            Kind::Heading(_) => out.push(runs_text(&b.runs)),
            Kind::Quote => out.extend(runs_text(&b.runs).split('\n').map(|l| format!("> {l}"))),
            Kind::Bullet | Kind::Ordered => {
                let pad = "   ".repeat(b.indent as usize);
                let marker = if b.kind == Kind::Ordered { format!("{}. ", doc.ordinal(i)) } else { "- ".into() };
                let t = runs_text(&b.runs).replace('\n', &format!("\n{pad}  "));
                out.push(format!("{pad}{marker}{t}"));
            }
            Kind::Cell { .. } => {
                let r = doc.table_range(i).unwrap_or(i..i + 1);
                for row in doc.table_grid(&r) {
                    out.push(row.iter().map(|c| runs_text(&c.runs).replace('\n', " ")).collect::<Vec<_>>().join(" | "));
                }
                i = r.end;
                continue;
            }
            Kind::Image(img) => out.push(format!("[{}]", if img.alt.is_empty() { if img.name.is_empty() { "image" } else { &img.name } } else { &img.alt })),
            Kind::Quoted { text, cite, .. } => {
                if *cite { out.extend(text.lines().map(|l| if l.is_empty() { ">".to_string() } else { format!("> {l}") })); }
                else { out.push(text.clone()); }
            }
            Kind::Rule => out.push("----------".into()),
        }
        i += 1;
    }
    let mut s = out.join("\n");
    while s.ends_with('\n') { s.pop(); }
    s
}

#[cfg(test)]
mod tests {
    use super::*;

    fn texts(b: &[Block]) -> Vec<String> { b.iter().map(|x| x.text()).collect() }

    #[test]
    fn import_inline_marks() {
        let b = import("<p>Hello <b>bold</b> <i>it</i> <a href=\"https://x.y\">link</a> <span style=\"color: rgb(255,0,0); font-size:18px\">red</span></p>");
        assert_eq!(b.len(), 1);
        let r = &b[0].runs;
        assert!(r.iter().any(|r| r.text == "bold" && r.marks.bold));
        assert!(r.iter().any(|r| r.text == "it" && r.marks.italic));
        assert!(r.iter().any(|r| r.text == "link" && r.marks.link.as_deref() == Some("https://x.y")));
        assert!(r.iter().any(|r| r.text == "red" && r.marks.color.as_deref() == Some("#ff0000") && r.marks.size == Some(18)));
    }

    #[test]
    fn import_blocks_and_lists() {
        let b = import("<h1>T</h1><div>a<br>b</div><div><br></div><ul><li><p>one</p></li><li>two<ul><li>sub</li></ul></li></ul><ol><li>n</li></ol><blockquote><p>q</p></blockquote><pre>x\n  y</pre>");
        let kinds: Vec<Kind> = b.iter().map(|x| x.kind.clone()).collect();
        assert_eq!(kinds, vec![Kind::Heading(1), Kind::Para, Kind::Para, Kind::Bullet, Kind::Bullet, Kind::Bullet, Kind::Ordered, Kind::Quote, Kind::Code]);
        assert_eq!(texts(&b), vec!["T", "a\nb", "", "one", "two", "sub", "n", "q", "x\n  y"]);
        assert_eq!(b[5].indent, 1);
    }

    #[test]
    fn import_table() {
        let b = import("<table><tr><th>A</th><th>B</th></tr><tr><td>1</td></tr></table><p>after</p>");
        assert_eq!(b.len(), 5);
        assert!(matches!(b[0].kind, Kind::Cell { row: 0, col: 0, header: true, .. }));
        assert!(matches!(b[3].kind, Kind::Cell { row: 1, col: 1, .. }));
        assert_eq!(b[4].text(), "after");
    }

    #[test]
    fn import_whitespace_and_entities() {
        let b = import("<div>\n   a  &amp;   b&nbsp;&nbsp;c\n</div>");
        assert_eq!(texts(&b), vec!["a & b  c"]);
        let b = import("<style>p{}</style><!-- x --><p>ok</p>");
        assert_eq!(texts(&b), vec!["ok"]);
    }

    #[test]
    fn round_trip() {
        let src = "<h2>Title</h2><div>Plain <strong>bold</strong> and <em>it</em></div><ul><li>a</li><li>b<ol><li>c</li></ol></li></ul><table><tr><td>x</td><td>y</td></tr></table><blockquote><div>quote</div></blockquote>";
        let d1 = Doc { blocks: import(src) };
        let html = export(&d1);
        let d2 = Doc { blocks: import(&html) };
        assert_eq!(d1.blocks.iter().map(|b| (&b.kind, b.indent, b.text())).collect::<Vec<_>>(),
                   d2.blocks.iter().map(|b| (&b.kind, b.indent, b.text())).collect::<Vec<_>>());
        assert!(html.contains("<strong>bold</strong> and <em>"));
        assert!(html.contains("<ol"));
    }

    #[test]
    fn plain_text_output() {
        let d = Doc { blocks: import("<p>Hi <a href=\"https://a.b\">site</a></p><ul><li>x</li></ul><ol><li>y</li><li>z</li></ol><blockquote>q</blockquote>") };
        assert_eq!(to_text(&d), "Hi site <https://a.b>\n- x\n1. y\n2. z\n> q");
    }

    #[test]
    fn export_escapes() {
        let d = Doc { blocks: vec![Block::para("a<b>&  c")] };
        let h = export(&d);
        assert!(h.contains("a&lt;b&gt;&amp; &nbsp;c"));
    }
}

// ---------------------------------------------------------------- quoted originals

/// Clean an original message's HTML for quoting: drop document wrappers, head,
/// styles and scripts so the quote cannot restyle the reply.
pub fn clean_quoted_html(h: &str) -> String {
    let mut s = h.to_string();
    for tag in ["script", "style", "head", "title"] {
        loop {
            let lower = s.to_ascii_lowercase();
            let Some(a) = lower.find(&format!("<{tag}")) else { break };
            let close = format!("</{tag}>");
            let b = lower[a..].find(&close).map(|e| a + e + close.len()).unwrap_or(s.len());
            s.replace_range(a..b, "");
        }
    }
    let lower = s.to_ascii_lowercase();
    // keep only the <body> content
    if let Some(a) = lower.find("<body") {
        if let Some(gt) = lower[a..].find('>') {
            let start = a + gt + 1;
            let end = lower.rfind("</body>").filter(|&e| e >= start).unwrap_or(s.len());
            s = s[start..end].to_string();
        }
    }
    for t in ["<!doctype", "<html", "</html", "<meta", "<?xml"] {
        loop {
            let lower = s.to_ascii_lowercase();
            let Some(a) = lower.find(t) else { break };
            let b = s[a..].find('>').map(|e| a + e + 1).unwrap_or(s.len());
            s.replace_range(a..b, "");
        }
    }
    s.trim().to_string()
}

/// Plain text of an HTML fragment (for the collapsed quote and text/plain).
pub fn html_text(h: &str) -> String {
    let d = Doc { blocks: import(h) };
    to_text(&d)
}

/// Import a whole message body (drafts): a top-level `<blockquote type="cite">`
/// becomes the collapsible quoted block again.
pub fn import_with_quotes(h: &str) -> Vec<Block> {
    let lower = h.to_ascii_lowercase();
    let Some(a) = lower.find("<blockquote type=\"cite\"") else { return import(h) };
    let Some(gt) = lower[a..].find('>') else { return import(h) };
    let inner_start = a + gt + 1;
    // matching close tag
    let mut depth = 1;
    let mut i = inner_start;
    let mut end = None;
    while i < lower.len() {
        let rest = &lower[i..];
        let o = rest.find("<blockquote");
        let c = rest.find("</blockquote");
        match (o, c) {
            (Some(o), Some(c)) if o < c => { depth += 1; i += o + 11; }
            (_, Some(c)) => { depth -= 1; if depth == 0 { end = Some(i + c); break; } i += c + 12; }
            _ => break,
        }
    }
    let Some(end) = end else { return import(h) };
    let after = lower[end..].find('>').map(|e| end + e + 1).unwrap_or(lower.len());
    let mut blocks = import(&h[..a]);
    while blocks.last().is_some_and(|b| b.kind == Kind::Para && b.is_empty()) && blocks.len() > 1 { blocks.pop(); }
    let inner = h[inner_start..end].to_string();
    blocks.push(Block::new(Kind::Quoted { text: html_text(&inner), html: inner, open: false, cite: true }));
    let tail = import(&h[after..]);
    if !(tail.len() == 1 && tail[0].is_empty()) { blocks.extend(tail); }
    blocks
}

#[cfg(test)]
mod quote_tests {
    use super::*;
    #[test]
    fn quoted_round_trip() {
        let mut d = Doc { blocks: vec![Block::para("Thanks!"), Block::para("On Mon, A wrote:"),
            Block::new(Kind::Quoted { html: "<p>orig <b>x</b></p><blockquote>older</blockquote>".into(), text: "orig x".into(), open: false, cite: true })] };
        d.fix();
        let h = export(&d);
        let b = import_with_quotes(&h);
        assert_eq!(b[0].text(), "Thanks!");
        assert_eq!(b[1].text(), "On Mon, A wrote:");
        assert!(matches!(&b[2].kind, Kind::Quoted { html, .. } if html.contains("<blockquote>older</blockquote>")));
        assert!(to_text(&d).contains("> orig x"));
    }
    #[test]
    fn clean_quote() {
        let h = clean_quoted_html("<!DOCTYPE html><html><head><style>p{color:red}</style></head><body><p>hi</p><script>x()</script></body></html>");
        assert_eq!(h, "<p>hi</p>");
    }
}
// ---------------------------------------------------------------- HTML source view

const WRAP_OPEN: &str = "<div style=\"font-family:'Segoe UI',-apple-system,Roboto,Helvetica,Arial,sans-serif;font-size:14px;line-height:1.5\">";

/// Index of the first quoted block (reply/forward original): the source view edits what comes before it.
pub fn editable_end(doc: &Doc) -> usize {
    doc.blocks.iter().position(|b| matches!(b.kind, Kind::Quoted { .. })).unwrap_or(doc.blocks.len())
}

/// The editable part of the document as readable HTML: one block per line, without the wrapper div.
pub fn source(doc: &Doc) -> String {
    let d = Doc { blocks: doc.blocks[..editable_end(doc)].to_vec() };
    if d.blocks.is_empty() { return String::new(); }
    let h = export(&d);
    let inner = h.strip_prefix(WRAP_OPEN).and_then(|x| x.strip_suffix("</div>")).unwrap_or(&h);
    pretty(inner)
}

/// Line breaks after block-level tags (the importer ignores whitespace between blocks).
pub fn pretty(h: &str) -> String {
    let mut out = String::with_capacity(h.len() + h.len() / 16);
    let lower = h.to_ascii_lowercase();
    let ends = ["</p>", "</h1>", "</h2>", "</h3>", "</li>", "</ul>", "</ol>", "</tr>", "</table>", "</blockquote>", "</pre>", "</div>"];
    let opens = ["<ul", "<ol", "<table", "<tr"];
    let mut i = 0;
    while i < h.len() {
        let rest = &lower[i..];
        if let Some(e) = ends.iter().find(|e| rest.starts_with(**e)) {
            out.push_str(&h[i..i + e.len()]);
            out.push('\n');
            i += e.len();
            continue;
        }
        if rest.starts_with("<hr") || opens.iter().any(|o| rest.starts_with(o) && rest[o.len()..].starts_with([' ', '>'])) {
            if let Some(gt) = rest.find('>') {
                out.push_str(&h[i..i + gt + 1]);
                out.push('\n');
                i += gt + 1;
                continue;
            }
        }
        let ch = h[i..].chars().next().unwrap();
        out.push(ch);
        i += ch.len_utf8();
    }
    while out.ends_with('\n') { out.pop(); }
    out
}

/// The document after editing the source: the new editable part plus the untouched quoted tail.
pub fn apply_source(doc: &Doc, src: &str) -> Doc {
    let mut blocks = import(src);
    if blocks.is_empty() { blocks.push(Block::new(Kind::Para)); }
    blocks.extend(doc.blocks[editable_end(doc)..].iter().cloned());
    let mut d = Doc { blocks };
    d.fix();
    d
}

#[cfg(test)]
mod source_tests {
    use super::*;

    #[test]
    fn source_round_trip() {
        let mut bold = Block::para("");
        bold.runs.push(Run { text: "Hello ".into(), marks: Marks::default() });
        bold.runs.push(Run { text: "world".into(), marks: Marks { bold: true, ..Default::default() } });
        let mut d = Doc { blocks: vec![
            bold, Block::with_text(Kind::Heading(2), "Title", Marks::default()),
            Block::with_text(Kind::Bullet, "one", Marks::default()), Block::with_text(Kind::Bullet, "two", Marks::default()),
            Block::new(Kind::Quoted { html: "<p>orig</p>".into(), text: "orig".into(), open: false, cite: true }),
        ] };
        d.fix();
        let s = source(&d);
        assert!(!s.contains("orig"), "the quote stays out of the source view");
        assert!(s.lines().count() >= 4, "one block per line: {s}");
        let back = apply_source(&d, &s);
        let texts: Vec<String> = back.blocks.iter().map(|b| b.text()).collect();
        assert_eq!(&texts[..4], &["Hello world", "Title", "one", "two"]);
        assert!(back.blocks[0].runs.iter().any(|r| r.marks.bold && r.text == "world"));
        assert_eq!(back.blocks[1].kind, Kind::Heading(2));
        assert_eq!(back.blocks.iter().filter(|b| matches!(b.kind, Kind::Quoted { .. })).count(), 1);
        // edited source
        let e = apply_source(&d, "<p>New <i>text</i></p>\n<p>Second</p>");
        assert_eq!(e.blocks[0].text(), "New text");
        assert_eq!(e.blocks[1].text(), "Second");
        assert_eq!(e.blocks.iter().filter(|b| matches!(b.kind, Kind::Quoted { .. })).count(), 1);
    }

    #[test]
    fn image_width_round_trip() {
        let img = ImageData { src: "https://x.org/a.png".into(), width: 240, height: 120, ..Default::default() };
        let d = Doc { blocks: vec![Block::new(Kind::Image(img))] };
        let h = export(&d);
        assert!(h.contains("width=\"240\""), "{h}");
        assert!(h.contains("width:240px"), "{h}");
        let b = import(&h);
        let Kind::Image(i) = &b[0].kind else { panic!("{b:?}") };
        assert_eq!(i.width, 240);
        // a CSS width alone is understood too
        let b = import("<img src=\"https://x.org/a.png\" style=\"width: 300px; height:auto\">");
        let Kind::Image(i) = &b[0].kind else { panic!() };
        assert_eq!(i.width, 300);
    }
}
