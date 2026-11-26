//! Markdown in answers (docs/AI-ASSISTANT-SPEC.md 7.6): a port of the extension's
//! `lib/markdown.js` that builds a toolkit-free block tree instead of DOM nodes. It runs again on
//! the growing text while an answer streams, so it tolerates unfinished input and never fails: on
//! a panic it falls back to the text as it is ([`Block::Plain`]).
//!
//! * Blocks: paragraphs with hard line breaks, ATX and setext headings (`#` is level 3, so levels
//!   shift down by 2, at most 6), fenced code (``` and ~~~, the language kept, lower-cased),
//!   nested quotes, rules, GitHub tables with alignment, nested ordered and unordered lists (with
//!   their start number), task items.
//! * A fence in the language `email` is its own block, [`Block::Email`] (the panel's draft card,
//!   the extension's `onFence`).
//! * Inline: `**bold**`, `*italic*`, `_italic_` (not inside words), `***both***`, `~~strike~~`,
//!   `` `code` ``, `[links](url "title")`, `<autolinks>` and `<email@host>`, bare `http(s)` URLs,
//!   images as links (never loaded), backslash escapes. Nesting depth at most 12.
//! * Only `http(s)` and `mailto:` links become [`Inline::Link`]s; any other target leaves the
//!   label as text.
//!
//! Text and link targets are kept as they are, not HTML-escaped: the panel (M13) must escape them
//! when it renders the tree. It must also pass an answer's text through
//! [`super::context::restore_tags`] before showing it, so a `<\/text` the model copied back from a
//! prompt reads as `</text`.
//!
//! The markdown-shared-rs toolkit has no Markdown block parser to reuse (its crates highlight
//! code and convert HTML; the editors parse elsewhere), so this one stays here; the tree is the
//! interface the panel (M13) renders.
//!
//! Positions count characters (`char`s), as the extension's count UTF-16 units; the two differ
//! only outside the Basic Multilingual Plane, where no Markdown syntax lives.
use regex::Regex;
use std::sync::OnceLock;

/// The nesting limit of quotes, lists and inline spans.
pub const MAX_DEPTH: usize = 12;

/// A block of an answer.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Block {
    /// A paragraph; its lines are separated by [`Inline::Break`].
    Paragraph(Vec<Inline>),
    /// A heading, level 3 to 6 (`#` is 3).
    Heading { level: u8, content: Vec<Inline> },
    /// A fenced code block; `lang` is lower-case, empty for none.
    Code { lang: String, text: String },
    /// A fenced block in the language `email`: an email draft (the panel shows it as a card).
    Email(String),
    /// A quote.
    Quote(Vec<Block>),
    /// A horizontal rule.
    Rule,
    /// A table: the head row, the body rows (each as many cells as the head) and the alignment
    /// of each column.
    Table { aligns: Vec<Align>, head: Vec<Vec<Inline>>, rows: Vec<Vec<Vec<Inline>>> },
    /// A list; `start` is the first number of an ordered list (1 for an unordered one), `tasks`
    /// whether it holds task items.
    List { ordered: bool, start: u64, tasks: bool, items: Vec<Item> },
    /// Text directly in a list item, without a paragraph (a tight item).
    Text(Vec<Inline>),
    /// Text shown as it is, line breaks kept: past the nesting limit, or when parsing failed.
    Plain(String),
}

/// A list item: the checkbox of a task item (checked or not), then its content.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Item {
    pub task: Option<bool>,
    pub content: Vec<Block>,
}

/// A table column's alignment.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Align {
    None,
    Left,
    Center,
    Right,
}

/// Inline content.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Inline {
    Text(String),
    Code(String),
    Strong(Vec<Inline>),
    Emph(Vec<Inline>),
    Strike(Vec<Inline>),
    /// A clickable link (`http(s)` or `mailto:` only); its title is the target.
    Link { href: String, content: Vec<Inline> },
    /// An image, shown as a link to it with its alt text (never loaded).
    Image { alt: String, href: String },
    /// A hard line break.
    Break,
}

/// Parses an answer. Never fails: a panic falls back to the whole text as [`Block::Plain`].
pub fn parse(md: &str) -> Vec<Block> {
    let text = md.replace("\r\n", "\n").replace('\r', "\n");
    let lines: Vec<String> = text.split('\n').map(String::from).collect();
    let run = std::panic::catch_unwind(|| {
        let mut dom = Dom::default();
        let root = dom.add(N::Container(Vec::new()));
        render_blocks(&mut dom, &lines, root, 0);
        dom.blocks(root)
    });
    run.unwrap_or_else(|_| vec![Block::Plain(md.to_string())])
}

/// The plain text of inline content (labels, alt texts, a copy of the answer).
pub fn inline_text(content: &[Inline]) -> String {
    let mut out = String::new();
    for i in content {
        match i {
            Inline::Text(t) | Inline::Code(t) => out.push_str(t),
            Inline::Strong(c) | Inline::Emph(c) | Inline::Strike(c) | Inline::Link { content: c, .. } => out.push_str(&inline_text(c)),
            Inline::Image { alt, .. } => out.push_str(alt),
            Inline::Break => out.push('\n'),
        }
    }
    out
}

// ---------------------------------------------------------------- the block arena
//
// The block code is the extension's, step for step; it appends to containers it holds on to
// (a list level keeps its list and its current item open), so blocks go into an arena first and
// become the tree at the end.

enum N {
    /// the root, a quote's or a list item's content
    Container(Vec<usize>),
    Quote(Vec<usize>),
    List { ordered: bool, start: u64, tasks: bool, items: Vec<usize> },
    Item { task: Option<bool>, kids: Vec<usize> },
    /// inline content directly in an item
    Run(Vec<Inline>),
    Leaf(Block),
}

#[derive(Default)]
struct Dom {
    nodes: Vec<N>,
}

impl Dom {
    fn add(&mut self, n: N) -> usize {
        self.nodes.push(n);
        self.nodes.len() - 1
    }

    fn append(&mut self, parent: usize, n: N) -> usize {
        let id = self.add(n);
        match &mut self.nodes[parent] {
            N::Container(k) | N::Quote(k) | N::Item { kids: k, .. } => k.push(id),
            N::List { items, .. } => items.push(id),
            N::Run(_) | N::Leaf(_) => {}
        }
        id
    }

    fn leaf(&mut self, parent: usize, b: Block) {
        self.append(parent, N::Leaf(b));
    }

    /// Appends inline content to an item (after what it has), into its last run if that is one.
    fn inline_into(&mut self, item: usize, content: Vec<Inline>) {
        if content.is_empty() {
            return;
        }
        let last = match &self.nodes[item] {
            N::Item { kids, .. } => kids.last().copied(),
            _ => None,
        };
        if let Some(l) = last {
            if let N::Run(run) = &mut self.nodes[l] {
                push_all(run, content);
                return;
            }
        }
        self.append(item, N::Run(content));
    }

    /// Whether an item has content (the extension: child nodes besides the checkbox).
    fn has_content(&self, item: usize) -> bool {
        matches!(&self.nodes[item], N::Item { kids, .. } if !kids.is_empty())
    }

    fn blocks(&self, id: usize) -> Vec<Block> {
        match &self.nodes[id] {
            N::Container(kids) => kids.iter().map(|&k| self.block(k)).collect(),
            _ => vec![self.block(id)],
        }
    }

    fn block(&self, id: usize) -> Block {
        match &self.nodes[id] {
            N::Container(kids) | N::Quote(kids) => Block::Quote(kids.iter().map(|&k| self.block(k)).collect()),
            N::List { ordered, start, tasks, items } => Block::List {
                ordered: *ordered,
                start: *start,
                tasks: *tasks,
                items: items
                    .iter()
                    .map(|&i| match &self.nodes[i] {
                        N::Item { task, kids } => Item { task: *task, content: kids.iter().map(|&k| self.block(k)).collect() },
                        _ => Item { task: None, content: vec![self.block(i)] },
                    })
                    .collect(),
            },
            N::Item { kids, .. } => Block::Quote(kids.iter().map(|&k| self.block(k)).collect()),
            N::Run(c) => Block::Text(c.clone()),
            N::Leaf(b) => b.clone(),
        }
    }
}

// ---------------------------------------------------------------- blocks

fn re(cell: &'static OnceLock<Regex>, pattern: &str) -> &'static Regex {
    cell.get_or_init(|| Regex::new(pattern).unwrap())
}

fn re_fence() -> &'static Regex {
    static R: OnceLock<Regex> = OnceLock::new();
    re(&R, r"^ {0,3}(`{3,}|~{3,})\s*([A-Za-z0-9_+#.-]*)[^`]*$")
}

/// `^ {0,3}([-*_])(?:[ \t]*\1){2,}[ \t]*$`
fn re_hr() -> &'static Regex {
    static R: OnceLock<Regex> = OnceLock::new();
    re(&R, r"^ {0,3}(?:-(?:[ \t]*-){2,}|\*(?:[ \t]*\*){2,}|_(?:[ \t]*_){2,})[ \t]*$")
}

fn re_atx() -> &'static Regex {
    static R: OnceLock<Regex> = OnceLock::new();
    re(&R, r"^ {0,3}(#{1,6})(?:[ \t]+(.*?))?(?:[ \t]+#+)?[ \t]*$")
}

fn re_item() -> &'static Regex {
    static R: OnceLock<Regex> = OnceLock::new();
    re(&R, r"^([ \t]*)([-*+•]|[0-9]{1,9}[.)])(?:[ \t]+(.*))?$")
}

fn re_quote() -> &'static Regex {
    static R: OnceLock<Regex> = OnceLock::new();
    re(&R, r"^ {0,3}>")
}

fn re_align() -> &'static Regex {
    static R: OnceLock<Regex> = OnceLock::new();
    re(&R, r"^ *\|? *:?-+:? *(?:\| *:?-+:? *)*\|? *$")
}

fn re_setext() -> &'static Regex {
    static R: OnceLock<Regex> = OnceLock::new();
    re(&R, r"^ {0,3}(=+|-{2,})[ \t]*$")
}

fn re_task() -> &'static Regex {
    static R: OnceLock<Regex> = OnceLock::new();
    re(&R, r"^\[([ xX])\](?:[ \t]+|$)")
}

fn indent_of(s: &str) -> usize {
    let mut w = 0;
    for ch in s.chars() {
        match ch {
            ' ' => w += 1,
            '\t' => w += 4 - (w % 4),
            _ => break,
        }
    }
    w
}

/// Removes `n` columns of leading whitespace.
fn dedent(line: &str, n: usize) -> String {
    let mut w = 0;
    let mut k = 0;
    for (i, ch) in line.char_indices() {
        if w >= n {
            break;
        }
        match ch {
            ' ' => w += 1,
            '\t' => w += 4 - (w % 4),
            _ => break,
        }
        k = i + 1;
    }
    line[k..].to_string()
}

fn split_row(line: &str) -> Vec<String> {
    let mut s = line.trim();
    if let Some(r) = s.strip_prefix('|') {
        s = r;
    }
    if s.ends_with('|') && !s.ends_with("\\|") {
        s = &s[..s.len() - 1];
    }
    let chars: Vec<char> = s.chars().collect();
    let mut cells = Vec::new();
    let mut cur = String::new();
    let mut in_code = false;
    let mut k = 0;
    while k < chars.len() {
        let ch = chars[k];
        if ch == '\\' && chars.get(k + 1) == Some(&'|') {
            cur.push('|');
            k += 1;
        } else if ch == '`' {
            in_code = !in_code;
            cur.push(ch);
        } else if ch == '|' && !in_code {
            cells.push(cur.trim().to_string());
            cur.clear();
        } else {
            cur.push(ch);
        }
        k += 1;
    }
    cells.push(cur.trim().to_string());
    cells
}

fn is_table_start(lines: &[String], i: usize) -> bool {
    lines[i].contains('|')
        && i + 1 < lines.len()
        && lines[i + 1].contains('-')
        && (lines[i + 1].contains('|') || lines[i].trim().starts_with('|'))
        && re_align().is_match(&lines[i + 1])
}

/// An item line with text after its marker (`RE_ITEM.exec(l)[3] !== undefined`).
fn is_item_with_text(l: &str) -> bool {
    re_item().captures(l).is_some_and(|c| c.get(3).is_some())
}

fn starts_block(lines: &[String], i: usize) -> bool {
    let l = &lines[i];
    re_fence().is_match(l) || re_hr().is_match(l) || re_atx().is_match(l) || re_quote().is_match(l) || is_item_with_text(l) || is_table_start(lines, i)
}

fn inl(t: &str) -> Vec<Inline> {
    let chars: Vec<char> = t.chars().collect();
    let mut out = Vec::new();
    inline(&chars, &mut out, 0);
    out
}

fn render_blocks(dom: &mut Dom, lines: &[String], container: usize, depth: usize) {
    let mut i = 0;
    let mut para: Vec<String> = Vec::new();

    let flush_para = |dom: &mut Dom, para: &mut Vec<String>| {
        if para.is_empty() {
            return;
        }
        let mut p = Vec::new();
        for (k, l) in para.iter().enumerate() {
            if k > 0 {
                p.push(Inline::Break);
            }
            push_all(&mut p, inl(l.trim()));
        }
        dom.leaf(container, Block::Paragraph(p));
        para.clear();
    };
    let heading = |dom: &mut Dom, level: usize, text: &str| {
        dom.leaf(container, Block::Heading { level: (level + 2).min(6) as u8, content: inl(text) });
    };

    while i < lines.len() {
        let line = &lines[i];

        // setext headings: a paragraph underlined with === or ---
        if !para.is_empty() && re_setext().is_match(line) {
            let level = if line.trim().starts_with('=') { 1 } else { 2 };
            let text = para.iter().map(|l| l.trim()).collect::<Vec<_>>().join(" ");
            para.clear();
            heading(dom, level, &text);
            i += 1;
            continue;
        }

        if let Some(fence) = re_fence().captures(line) {
            flush_para(dom, &mut para);
            let marker = fence.get(1).unwrap().as_str();
            let ind = indent_of(line);
            let lang = fence.get(2).map_or("", |m| m.as_str()).to_lowercase();
            let close = Regex::new(&format!(r"^ {{0,3}}{}{{{},}}[ \t]*$", if marker.starts_with('`') { "`" } else { "~" }, marker.len())).unwrap();
            let mut code = Vec::new();
            i += 1;
            while i < lines.len() && !close.is_match(&lines[i]) {
                code.push(dedent(&lines[i], ind));
                i += 1;
            }
            i += 1;
            let text = code.join("\n");
            dom.leaf(container, if lang == "email" { Block::Email(text) } else { Block::Code { lang, text } });
            continue;
        }

        if line.trim().is_empty() {
            flush_para(dom, &mut para);
            i += 1;
            continue;
        }

        if let Some(atx) = re_atx().captures(line) {
            flush_para(dom, &mut para);
            heading(dom, atx[1].len(), atx.get(2).map_or("", |m| m.as_str()));
            i += 1;
            continue;
        }

        if re_hr().is_match(line) {
            flush_para(dom, &mut para);
            dom.leaf(container, Block::Rule);
            i += 1;
            continue;
        }

        if re_quote().is_match(line) {
            flush_para(dom, &mut para);
            let mut inner = Vec::new();
            static STRIP: OnceLock<Regex> = OnceLock::new();
            let strip = re(&STRIP, r"^ {0,3}> ?");
            while i < lines.len() && !lines[i].trim().is_empty() {
                if re_quote().is_match(&lines[i]) {
                    inner.push(strip.replace(&lines[i], "").into_owned());
                } else if !inner.is_empty() && !starts_block(lines, i) {
                    inner.push(lines[i].clone()); // lazy continuation
                } else {
                    break;
                }
                i += 1;
            }
            let bq = dom.append(container, N::Quote(Vec::new()));
            if depth < MAX_DEPTH {
                render_blocks(dom, &inner, bq, depth + 1);
            } else {
                dom.leaf(bq, Block::Plain(inner.join("\n")));
            }
            continue;
        }

        if is_table_start(lines, i) {
            flush_para(dom, &mut para);
            i = render_table(dom, lines, i, container);
            continue;
        }

        if let Some(item) = re_item().captures(line) {
            if item.get(3).is_some() || para.is_empty() {
                flush_para(dom, &mut para);
                i = render_list(dom, lines, i, container, depth);
                continue;
            }
        }

        para.push(line.clone());
        i += 1;
    }
    flush_para(dom, &mut para);
}

fn render_table(dom: &mut Dom, lines: &[String], mut i: usize, container: usize) -> usize {
    let head = split_row(&lines[i]);
    let aligns: Vec<Align> = split_row(&lines[i + 1])
        .iter()
        .map(|c| match (c.starts_with(':'), c.ends_with(':')) {
            (true, true) => Align::Center,
            (false, true) => Align::Right,
            (true, false) => Align::Left,
            _ => Align::None,
        })
        .collect();
    let cols = head.len();
    let row = |cells: &[String]| (0..cols).map(|c| inl(cells.get(c).map_or("", |s| s.as_str()))).collect::<Vec<_>>();
    let head_row = row(&head);
    i += 2;
    let mut rows = Vec::new();
    while i < lines.len() && !lines[i].trim().is_empty() && lines[i].contains('|') {
        rows.push(row(&split_row(&lines[i])));
        i += 1;
    }
    let aligns = (0..cols).map(|c| aligns.get(c).copied().unwrap_or(Align::None)).collect();
    dom.leaf(container, Block::Table { aligns, head: head_row, rows });
    i
}

/// An open list level: its list, its current item and that item's lines.
struct Level {
    indent: usize,
    content: usize,
    el: usize,
    parent: usize,
    marker: String,
    li: Option<usize>,
    task: bool,
    buf: Vec<String>,
}

// Indentation-based nested lists. Each open level keeps its own list and the lines of its current
// item; an item's lines are rendered (inline when they are a single paragraph, as blocks
// otherwise) when the item ends or a nested list starts inside it.
//
// `level` below is a level's index in `stack`, so its items' content sits `depth + level + 1`
// deep. Nesting stops at `MAX_DEPTH` (the deepest container may sit one level past it, as a
// quote's does): a line indented under a level past it stays text in that item instead of opening
// another list (unbounded nesting overflows the stack while the tree is built or dropped, which
// `catch_unwind` cannot stop).
fn render_list(dom: &mut Dom, lines: &[String], mut i: usize, container: usize, depth: usize) -> usize {
    let mut stack: Vec<Level> = Vec::new();

    let flush_item = |dom: &mut Dom, lvl: &mut Level, level: usize| {
        let Some(li) = lvl.li else { return };
        if lvl.buf.is_empty() {
            return;
        }
        let mut buf = std::mem::take(&mut lvl.buf);
        while buf.last().is_some_and(|l| l.trim().is_empty()) {
            buf.pop();
        }
        if buf.is_empty() {
            return;
        }
        let simple = buf.iter().all(|l| !l.trim().is_empty()) && !(1..buf.len()).any(|k| starts_block(&buf, k));
        if simple {
            for (k, l) in buf.iter().enumerate() {
                let mut run = Vec::new();
                if k > 0 || dom.has_content(li) {
                    run.push(Inline::Break);
                }
                push_all(&mut run, inl(l.trim()));
                dom.inline_into(li, run);
            }
        } else if depth + level < MAX_DEPTH {
            render_blocks(dom, &buf, li, depth + level + 1);
        } else {
            dom.inline_into(li, vec![Inline::Text(buf.join("\n"))]);
        }
    };
    let new_list = |dom: &mut Dom, parent: usize, ordered: bool, start: u64, indent: usize, content: usize| Level {
        indent,
        content,
        el: dom.append(parent, N::List { ordered, start: if ordered { start } else { 1 }, tasks: false, items: Vec::new() }),
        parent,
        marker: String::new(),
        li: None,
        task: false,
        buf: Vec::new(),
    };

    while i < lines.len() {
        let line = &lines[i];

        if line.trim().is_empty() {
            // a blank line continues the list only if more list content follows
            let mut k = i + 1;
            while k < lines.len() && lines[k].trim().is_empty() {
                k += 1;
            }
            if k >= lines.len() {
                break;
            }
            let next = &lines[k];
            if !re_item().is_match(next) && indent_of(next) < stack.first().map_or(2, |l| l.content) {
                break;
            }
            if let Some(top) = stack.last_mut() {
                top.buf.push(String::new());
            }
            i += 1;
            continue;
        }

        let item = re_item().captures(line);
        let ind = indent_of(line);
        // inside an open fence in the current item, everything is code
        let in_fence = stack.last().is_some_and(|top| top.buf.iter().filter(|l| re_fence().is_match(l)).count() % 2 == 1);

        if let Some(item) = item.filter(|_| !in_fence && !stack.last().is_some_and(|top| ind >= top.content + 4)) {
            let mark = item.get(2).unwrap().as_str();
            let ordered = mark.chars().any(|c| c.is_ascii_digit());
            let marker = if ordered { mark[mark.len() - 1..].to_string() } else { mark.to_string() };
            let number: u64 = if ordered { mark[..mark.len() - 1].parse().unwrap_or(1) } else { 1 };
            let after = if item.get(3).is_none() { "" } else { &line[item.get(2).unwrap().end()..] };
            let ws = after.chars().take_while(|c| *c == ' ' || *c == '\t').count();
            let content = ind + mark.chars().count() + ws.clamp(1, 4);

            // close deeper levels
            while stack.len() > 1 && ind + 1 < stack.last().unwrap().indent {
                let level = stack.len() - 1;
                let mut lvl = stack.pop().unwrap();
                flush_item(dom, &mut lvl, level);
            }
            if stack.is_empty() {
                let mut lvl = new_list(dom, container, ordered, number, ind, content);
                lvl.marker = marker.clone();
                stack.push(lvl);
            } else {
                let level = stack.len() - 1;
                let can_nest = depth + level + 1 < MAX_DEPTH;
                let top = stack.last_mut().unwrap();
                if ind >= top.indent + 2 && top.li.is_some() {
                    flush_item(dom, top, level);
                    if can_nest {
                        let li = top.li.unwrap();
                        let child = new_list(dom, li, ordered, number, ind, content);
                        stack.push(child);
                    } else {
                        // past the nesting limit: the line stays text in the item
                        let text = dedent(line, ind.min(top.content));
                        top.buf.push(text);
                        i += 1;
                        continue;
                    }
                } else {
                    flush_item(dom, top, level);
                    if top.marker != marker {
                        let parent = top.parent;
                        *top = new_list(dom, parent, ordered, number, ind, content);
                    }
                }
            }
            let lvl = stack.last_mut().unwrap();
            lvl.content = content;
            lvl.marker = marker;
            let mut text = item.get(3).map_or("", |m| m.as_str()).to_string();
            let mut task = None;
            if let Some(t) = re_task().captures(&text) {
                task = Some(&t[1] != " ");
                let len = t[0].len();
                text = text[len..].to_string();
                if let N::List { tasks, .. } = &mut dom.nodes[lvl.el] {
                    *tasks = true;
                }
            }
            lvl.task = task.is_some();
            lvl.li = Some(dom.append(lvl.el, N::Item { task, kids: Vec::new() }));
            lvl.buf = if text.is_empty() { Vec::new() } else { vec![text] };
            i += 1;
            continue;
        }

        // a continuation line belongs to the deepest level it is indented under
        if !stack.is_empty() && (ind >= 2 || in_fence) {
            while stack.len() > 1 && ind < stack.last().unwrap().indent + 2 && !in_fence {
                let level = stack.len() - 1;
                let mut lvl = stack.pop().unwrap();
                flush_item(dom, &mut lvl, level);
            }
            let lvl = stack.last_mut().unwrap();
            lvl.buf.push(dedent(line, ind.min(lvl.content)));
            i += 1;
            continue;
        }

        // lazy paragraph continuation (no blank line before, not a new block)
        if stack.last().is_some_and(|top| top.buf.last().is_some_and(|l| !l.trim().is_empty())) && !starts_block(lines, i) {
            stack.last_mut().unwrap().buf.push(line.clone());
            i += 1;
            continue;
        }
        break;
    }
    while !stack.is_empty() {
        let level = stack.len() - 1;
        let mut lvl = stack.pop().unwrap();
        flush_item(dom, &mut lvl, level);
    }
    i
}

// ---------------------------------------------------------------- inline

const PUNCT: &str = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";

/// Appends inline content, joining neighbouring text.
fn push(out: &mut Vec<Inline>, i: Inline) {
    if let Inline::Text(t) = &i {
        if t.is_empty() {
            return;
        }
        if let Some(Inline::Text(last)) = out.last_mut() {
            last.push_str(t);
            return;
        }
    }
    out.push(i);
}

fn push_all(out: &mut Vec<Inline>, items: Vec<Inline>) {
    for i in items {
        push(out, i);
    }
}

/// `safeHref`: only `http(s)` with a host and `mailto:` targets, without spaces.
fn safe_href(url: &str) -> Option<String> {
    static HTTP: OnceLock<Regex> = OnceLock::new();
    static MAILTO: OnceLock<Regex> = OnceLock::new();
    let u = url.trim();
    if u.chars().any(char::is_whitespace) {
        return None;
    }
    if re(&HTTP, r"(?i)^https?://[^/\s]").is_match(u) || re(&MAILTO, r"(?i)^mailto:[^\s]+$").is_match(u) {
        Some(u.to_string())
    } else {
        None
    }
}

fn is_alnum(ch: Option<char>) -> bool {
    ch.is_some_and(char::is_alphanumeric)
}

fn is_space(ch: Option<char>) -> bool {
    ch.is_none_or(char::is_whitespace)
}

fn at(t: &[char], k: isize) -> Option<char> {
    if k < 0 {
        None
    } else {
        t.get(k as usize).copied()
    }
}

fn index_of(t: &[char], c: char, from: usize) -> Option<usize> {
    (from..t.len()).find(|&k| t[k] == c)
}

fn index_of_str(t: &[char], s: &[char], from: usize) -> Option<usize> {
    if s.len() > t.len() {
        return None;
    }
    (from..=t.len() - s.len()).find(|&k| t[k..k + s.len()] == *s)
}

fn text_of(t: &[char]) -> String {
    t.iter().collect()
}

/// The `]` that matches the `[` at `open`, minding nesting and escapes.
fn match_bracket(t: &[char], open: usize) -> Option<usize> {
    let mut depth = 0i32;
    let mut k = open;
    while k < t.len() {
        match t[k] {
            '\\' => k += 1,
            '[' => depth += 1,
            ']' => {
                depth -= 1;
                if depth == 0 {
                    return Some(k);
                }
            }
            _ => {}
        }
        k += 1;
    }
    None
}

/// `(url "title")` at `open` (the `(`): the url and the position after the `)`.
fn parse_dest(t: &[char], open: usize) -> Option<(String, usize)> {
    if t.get(open) != Some(&'(') {
        return None;
    }
    let mut k = open + 1;
    while t.get(k) == Some(&' ') {
        k += 1;
    }
    let url;
    if t.get(k) == Some(&'<') {
        let close = index_of(t, '>', k)?;
        url = text_of(&t[k + 1..close]);
        k = close + 1;
    } else {
        let mut depth = 0;
        let start = k;
        while k < t.len() && !t[k].is_whitespace() {
            if t[k] == '(' {
                depth += 1;
            } else if t[k] == ')' {
                if depth == 0 {
                    break;
                }
                depth -= 1;
            }
            k += 1;
        }
        url = text_of(&t[start..k]);
    }
    while t.get(k) == Some(&' ') {
        k += 1;
    }
    if let Some(q @ ('"' | '\'')) = t.get(k).copied() {
        let close = index_of(t, q, k + 1)?;
        k = close + 1;
        while t.get(k) == Some(&' ') {
            k += 1;
        }
    }
    if t.get(k) != Some(&')') {
        return None;
    }
    Some((url, k + 1))
}

/// A closing emphasis run of at least `n` chars `c` after `from`: where its last `n` start.
fn find_closer(t: &[char], from: usize, c: char, n: usize) -> Option<usize> {
    let mut k = from;
    while let Some(found) = index_of(t, c, k) {
        k = found;
        if at(t, k as isize - 1) == Some('\\') {
            k += 1;
            continue;
        }
        let mut e = k;
        while t.get(e) == Some(&c) {
            e += 1;
        }
        let len = e - k;
        if len >= n && !is_space(at(t, k as isize - 1)) && k > from && (c != '_' || !is_alnum(t.get(k + n).copied())) {
            // prefer a run of exactly n; of a longer run, its tail
            return Some(if len == n { k } else { e - n });
        }
        k = e;
    }
    None
}

/// The text from `i`, at most 2048 characters, for the patterns that look ahead.
fn ahead(t: &[char], i: usize) -> String {
    text_of(&t[i..(i + 2048).min(t.len())])
}

fn inline(t: &[char], out: &mut Vec<Inline>, depth: usize) {
    if depth > MAX_DEPTH {
        push(out, Inline::Text(text_of(t)));
        return;
    }
    let mut buf = String::new();
    let flush = |out: &mut Vec<Inline>, buf: &mut String| push(out, Inline::Text(std::mem::take(buf)));
    let sub = |s: &[char]| {
        let mut v = Vec::new();
        inline(s, &mut v, depth + 1);
        v
    };
    let n = t.len();
    let mut i = 0;
    while i < n {
        let ch = t[i];

        if ch == '\\' && i + 1 < n && PUNCT.contains(t[i + 1]) {
            buf.push(t[i + 1]);
            i += 2;
            continue;
        }

        if ch == '`' {
            let mut e = i;
            while t.get(e) == Some(&'`') {
                e += 1;
            }
            let run = e - i;
            let mut k = e;
            let mut close = None;
            while let Some(found) = index_of(t, '`', k) {
                let mut ke = found;
                while t.get(ke) == Some(&'`') {
                    ke += 1;
                }
                if ke - found == run {
                    close = Some(found);
                    break;
                }
                k = ke;
            }
            let Some(close) = close else {
                buf.push_str(&text_of(&t[i..e]));
                i = e;
                continue;
            };
            let mut code = &t[e..close];
            // one space each side goes when the code has other text
            if code.len() >= 2 && code[0] == ' ' && code[code.len() - 1] == ' ' && code.iter().any(|c| *c != ' ') {
                code = &code[1..code.len() - 1];
            }
            flush(out, &mut buf);
            push(out, Inline::Code(text_of(code)));
            i = close + run;
            continue;
        }

        if ch == '<' {
            static AUTO: OnceLock<Regex> = OnceLock::new();
            let auto = re(&AUTO, r"(?i)^<((?:https?://|mailto:)[^\s<>]+|[^\s<>@]+@[^\s<>@]+\.[^\s<>@]+)>");
            let rest = ahead(t, i);
            if let Some(m) = auto.captures(&rest) {
                let target = &m[1];
                let href = safe_href(&if target.contains(':') { target.to_string() } else { format!("mailto:{target}") });
                if let Some(href) = href {
                    flush(out, &mut buf);
                    // a label is never cut mid-character: `Şükrü@…` has no boundary at 7
                    let label = match target.get(..7) {
                        Some(head) if head.eq_ignore_ascii_case("mailto:") => &target[7..],
                        _ => target,
                    };
                    push(out, Inline::Link { href, content: vec![Inline::Text(label.to_string())] });
                    i += m[0].chars().count();
                    continue;
                }
            }
        }

        if ch == '[' || (ch == '!' && t.get(i + 1) == Some(&'[')) {
            let img = ch == '!';
            let open = if img { i + 1 } else { i };
            let close = match_bracket(t, open);
            if let Some((url, end)) = close.and_then(|c| parse_dest(t, c + 1)) {
                let close = close.unwrap();
                let label = &t[open + 1..close];
                let href = safe_href(&url);
                flush(out, &mut buf);
                if img {
                    let alt = text_of(label).trim().to_string();
                    let alt = if alt.is_empty() { "image".to_string() } else { alt };
                    match href {
                        Some(href) => push(out, Inline::Image { alt, href }),
                        None => push(out, Inline::Text(alt)),
                    }
                } else if let Some(href) = href {
                    push(out, Inline::Link { href, content: sub(label) });
                } else {
                    push_all(out, sub(label));
                }
                i = end;
                continue;
            }
        }

        if ch == '~' && t.get(i + 1) == Some(&'~') && !is_space(t.get(i + 2).copied()) {
            if let Some(close) = index_of_str(t, &['~', '~'], i + 3) {
                if close > 0 && !is_space(at(t, close as isize - 1)) {
                    flush(out, &mut buf);
                    push(out, Inline::Strike(sub(&t[i + 2..close])));
                    i = close + 2;
                    continue;
                }
            }
        }

        if ch == '*' || ch == '_' {
            let mut e = i;
            while t.get(e) == Some(&ch) {
                e += 1;
            }
            let run = e - i;
            let opens = run <= 3 && !is_space(t.get(e).copied()) && (ch == '*' || !is_alnum(at(t, i as isize - 1)));
            if opens {
                let mut done = false;
                for len in (1..=run).rev() {
                    let Some(close) = find_closer(t, e, ch, len) else { continue };
                    // leading delimiters this pair does not use stay as they are
                    for _ in 0..run - len {
                        buf.push(ch);
                    }
                    flush(out, &mut buf);
                    let inner = sub(&t[e..close]);
                    push(
                        out,
                        match len {
                            3 => Inline::Strong(vec![Inline::Emph(inner)]),
                            2 => Inline::Strong(inner),
                            _ => Inline::Emph(inner),
                        },
                    );
                    i = close + len;
                    done = true;
                    break;
                }
                if done {
                    continue;
                }
            }
            buf.push_str(&text_of(&t[i..e]));
            i = e;
            continue;
        }

        if (ch == 'h' || ch == 'H') && !is_alnum(at(t, i as isize - 1)) {
            static BARE: OnceLock<Regex> = OnceLock::new();
            static PARENS: OnceLock<Regex> = OnceLock::new();
            let bare = re(&BARE, r#"(?i)^https?://[^\s<>()]*[^\s<>().,;:!?'"*_~]"#);
            let rest = ahead(t, i);
            if let Some(m) = bare.find(&rest) {
                let mut url = m.as_str().to_string();
                // keep balanced parentheses (Wikipedia links)
                if let Some(p) = re(&PARENS, r"^\([^\s()<>]*\)").find(&rest[m.end()..]) {
                    url.push_str(p.as_str());
                }
                flush(out, &mut buf);
                i += url.chars().count();
                push(out, Inline::Link { href: url.clone(), content: vec![Inline::Text(url)] });
                continue;
            }
        }

        buf.push(ch);
        i += 1;
    }
    flush(out, &mut buf);
}

#[cfg(test)]
mod tests {
    use super::*;

    fn t(s: &str) -> Inline {
        Inline::Text(s.into())
    }

    fn p(c: Vec<Inline>) -> Block {
        Block::Paragraph(c)
    }

    fn text(items: Vec<Inline>) -> Block {
        Block::Text(items)
    }

    fn link(href: &str, label: &str) -> Inline {
        Inline::Link { href: href.into(), content: vec![t(label)] }
    }

    fn item(content: Vec<Block>) -> Item {
        Item { task: None, content }
    }

    fn ul(items: Vec<Item>) -> Block {
        Block::List { ordered: false, start: 1, tasks: false, items }
    }

    #[test]
    fn paragraphs_and_hard_breaks() {
        assert_eq!(parse("one\n  two  \n\nthree"), [p(vec![t("one"), Inline::Break, t("two")]), p(vec![t("three")])]);
        assert_eq!(parse(""), []);
        assert_eq!(parse("a\r\nb\rc"), [p(vec![t("a"), Inline::Break, t("b"), Inline::Break, t("c")])]);
    }

    #[test]
    fn headings_shift_down_by_two() {
        let h = |level, s: &str| Block::Heading { level, content: vec![t(s)] };
        assert_eq!(parse("# One\n## Two ##\n#### Four\n###### Six"), [h(3, "One"), h(4, "Two"), h(6, "Four"), h(6, "Six")]);
        assert_eq!(parse("#"), [Block::Heading { level: 3, content: vec![] }]);
        assert_eq!(parse("#nospace"), [p(vec![t("#nospace")])]);
        assert_eq!(parse("Title\n===\nSub\n---"), [h(3, "Title"), h(4, "Sub")]);
        assert_eq!(parse("two\nlines\n=="), [h(3, "two lines")]);
    }

    #[test]
    fn fences_and_the_email_block() {
        assert_eq!(parse("```Rust\nfn x() {}\n```"), [Block::Code { lang: "rust".into(), text: "fn x() {}".into() }]);
        assert_eq!(parse("~~~~\na\n~~~\n~~~~~\nafter"), [Block::Code { lang: "".into(), text: "a\n~~~".into() }, p(vec![t("after")])]);
        assert_eq!(
            parse("Here is a draft:\n\n```email\nHi Bob,\n\nyes.\n```\nAnything else?"),
            [p(vec![t("Here is a draft:")]), Block::Email("Hi Bob,\n\nyes.".into()), p(vec![t("Anything else?")])]
        );
        // an indented fence takes its indent off the code
        assert_eq!(parse("  ```\n    x\n  ```"), [Block::Code { lang: "".into(), text: "  x".into() }]);
        // unfinished: the code runs to the end, while it streams
        assert_eq!(parse("```email\nHi Bob,\nI can"), [Block::Email("Hi Bob,\nI can".into())]);
        assert_eq!(parse("```email"), [Block::Email("".into())]);
        // markup inside code stays text
        assert_eq!(parse("```\n**not bold**\n```"), [Block::Code { lang: "".into(), text: "**not bold**".into() }]);
        // backticks after the language: not a fence
        assert_eq!(parse("```a`b"), [p(vec![t("```a`b")])]);
    }

    #[test]
    fn quotes_rules_and_tables() {
        assert_eq!(parse("> one\nlazy\n> > deep"), [Block::Quote(vec![p(vec![t("one"), Inline::Break, t("lazy")]), Block::Quote(vec![p(vec![t("deep")])])])]);
        assert_eq!(parse("---\n* * *\n___"), [Block::Rule, Block::Rule, Block::Rule]);
        assert_eq!(parse("-*-"), [p(vec![t("-*-")])]);
        let table = parse("| Name | Count | Note |\n|:-----|------:|:----:|\n| a | 1 |\n| `x|y` | 2 | \\| |\n\nafter");
        assert_eq!(
            table,
            [
                Block::Table {
                    aligns: vec![Align::Left, Align::Right, Align::Center],
                    head: vec![vec![t("Name")], vec![t("Count")], vec![t("Note")]],
                    rows: vec![vec![vec![t("a")], vec![t("1")], vec![]], vec![vec![Inline::Code("x|y".into())], vec![t("2")], vec![t("|")]]],
                },
                p(vec![t("after")])
            ]
        );
        assert_eq!(parse("a | b\n--|--"), [Block::Table { aligns: vec![Align::None, Align::None], head: vec![vec![t("a")], vec![t("b")]], rows: vec![] }]);
        // the nesting limit
        let deep = ">".repeat(20) + " x";
        let mut b = &parse(&deep)[0];
        let mut levels = 0;
        while let Block::Quote(inner) = b {
            levels += 1;
            b = &inner[0];
        }
        assert_eq!((levels, b), (13, &Block::Plain(">>>>>>> x".into())));
    }

    #[test]
    fn lists() {
        assert_eq!(parse("- a\n- b\n  more"), [ul(vec![item(vec![text(vec![t("a")])]), item(vec![text(vec![t("b"), Inline::Break, t("more")])])])]);
        // ordered with a start, nested, and back
        assert_eq!(
            parse("3. three\n4. four\n   - sub\n   - sub2\n5. five"),
            [Block::List {
                ordered: true,
                start: 3,
                tasks: false,
                items: vec![
                    item(vec![text(vec![t("three")])]),
                    item(vec![text(vec![t("four")]), ul(vec![item(vec![text(vec![t("sub")])]), item(vec![text(vec![t("sub2")])])])]),
                    item(vec![text(vec![t("five")])]),
                ],
            }]
        );
        // a new marker starts a new list
        assert_eq!(parse("- a\n+ b"), [ul(vec![item(vec![text(vec![t("a")])])]), ul(vec![item(vec![text(vec![t("b")])])])]);
        // task items
        assert_eq!(
            parse("- [x] done\n- [ ] todo\n- [y] no"),
            [Block::List {
                ordered: false,
                start: 1,
                tasks: true,
                items: vec![
                    Item { task: Some(true), content: vec![text(vec![t("done")])] },
                    Item { task: Some(false), content: vec![text(vec![t("todo")])] },
                    item(vec![text(vec![t("[y] no")])]),
                ],
            }]
        );
        // a loose item with two paragraphs renders as blocks; a blank line before a paragraph ends the list
        assert_eq!(
            parse("- one\n\n  two\n\nafter"),
            [ul(vec![item(vec![p(vec![t("one")]), p(vec![t("two")])])]), p(vec![t("after")])]
        );
        // a fence inside an item
        assert_eq!(parse("- code:\n  ```\n  x\n  ```"), [ul(vec![item(vec![p(vec![t("code:")]), Block::Code { lang: "".into(), text: "x".into() }])])]);
        // a bullet and a number without text
        assert_eq!(parse("-"), [ul(vec![item(vec![])])]);
        assert_eq!(parse("text\n-"), [p(vec![t("text"), Inline::Break, t("-")])]);
        assert_eq!(parse("• dot"), [ul(vec![item(vec![text(vec![t("dot")])])])]);
        assert_eq!(parse("1) one"), [Block::List { ordered: true, start: 1, tasks: false, items: vec![item(vec![text(vec![t("one")])])] }]);
    }

    #[test]
    fn deep_lists_stop_at_the_nesting_limit() {
        // a tab-nested list one level deeper than MAX_DEPTH every line; unbounded nesting would
        // overflow the stack while the tree is built or dropped
        let mut md = String::new();
        for k in 0..48 {
            md.push_str(&"\t".repeat(k));
            md.push_str(&format!("- line{k}\n"));
        }
        fn depth_of(b: &Block) -> usize {
            let kids = |bs: &[Block]| bs.iter().map(depth_of).max().unwrap_or(0);
            match b {
                Block::List { items, .. } => 1 + items.iter().map(|i| kids(&i.content)).max().unwrap_or(0),
                Block::Quote(inner) => 1 + kids(inner),
                _ => 0,
            }
        }
        let blocks = parse(&md);
        let plain = format!("{blocks:?}");
        assert!(!blocks.iter().any(|b| matches!(b, Block::Plain(_))), "the whole answer fell back to plain text");
        // the lines past the limit are kept as text in the deepest item, not dropped
        assert!(plain.contains("line0"), "{plain}");
        assert!(plain.contains("line47"), "the deepest line was dropped: {plain}");
        let depth = depth_of(&blocks[0]);
        // the recursion stops at MAX_DEPTH, so the deepest container is one level past it, as a
        // quote's is (quotes: 13 levels, then plain text)
        assert_eq!(depth, MAX_DEPTH + 1, "nesting depth {depth}");
    }

    #[test]
    fn inline_spans() {
        let one = |s: &str| match parse(s).into_iter().next() {
            Some(Block::Paragraph(c)) => c,
            other => panic!("{other:?}"),
        };
        assert_eq!(one("**b** *i* _i_ ***bi*** ~~s~~"), [
            Inline::Strong(vec![t("b")]), t(" "), Inline::Emph(vec![t("i")]), t(" "), Inline::Emph(vec![t("i")]), t(" "),
            Inline::Strong(vec![Inline::Emph(vec![t("bi")])]), t(" "), Inline::Strike(vec![t("s")])
        ]);
        assert_eq!(one("snake_case_name and 2*3*4"), [t("snake_case_name and 2"), Inline::Emph(vec![t("3")]), t("4")]);
        assert_eq!(one("** not** *x"), [t("** not** *x")]);
        assert_eq!(one("`a` `` b`c `` ` x `"), [Inline::Code("a".into()), t(" "), Inline::Code("b`c".into()), t(" "), Inline::Code("x".into())]);
        assert_eq!(one("`open"), [t("`open")]);
        assert_eq!(one(r"\*not\* \q"), [t(r"*not* \q")]);
        assert_eq!(one("[site](https://example.test/a_(b) \"T\") [bad](javascript:alert(1)) [mail](mailto:a@example.test)"), [
            Inline::Link { href: "https://example.test/a_(b)".into(), content: vec![t("site")] },
            t(" bad "),
            link("mailto:a@example.test", "mail"),
        ]);
        assert_eq!(one("[**bold** link](http://h.test)"), [Inline::Link { href: "http://h.test".into(), content: vec![Inline::Strong(vec![t("bold")]), t(" link")] }]);
        assert_eq!(one("![chart](https://img.test/c.png) ![](ftp://x)"), [Inline::Image { alt: "chart".into(), href: "https://img.test/c.png".into() }, t(" image")]);
        assert_eq!(one("<https://a.test/x> <bob@example.test> <MAILTO:c@d.test> <not a link>"), [
            link("https://a.test/x", "https://a.test/x"), t(" "), link("mailto:bob@example.test", "bob@example.test"), t(" "),
            link("MAILTO:c@d.test", "c@d.test"), t(" <not a link>")
        ]);
        assert_eq!(one("See https://example.test/a(b), or http://x.test."), [
            t("See "), link("https://example.test/a(b)", "https://example.test/a(b)"), t(", or "), link("http://x.test", "http://x.test"), t(".")
        ]);
        // as the extension: the URL may not end in `_`, so the parentheses after one are text
        assert_eq!(one("https://w.test/Rust_(x)"), [link("https://w.test/Rust", "https://w.test/Rust"), t("_(x)")]);
        assert_eq!(one("xhttp://no.test"), [t("xhttp://no.test")]);
        // unfinished spans stay text while they stream
        assert_eq!(one("**bold and [link](http"), [t("**bold and [link](http")]);
        // an autolink's label is never cut mid-character (a non-ASCII local part has no byte
        // boundary at 7)
        assert_eq!(one("<Şükrü@example.test> <山田太郎@example.jp>"), [
            link("mailto:Şükrü@example.test", "Şükrü@example.test"),
            t(" "),
            link("mailto:山田太郎@example.jp", "山田太郎@example.jp"),
        ]);
        // the nesting limit leaves the rest as text: 13 links, then the brackets as they are
        let deep = "[".repeat(20) + "x" + &"](http://h.test)".repeat(20);
        let content = one(&deep);
        let mut levels = 0;
        let mut cur: &[Inline] = &content;
        while let [Inline::Link { content, .. }] = cur {
            levels += 1;
            cur = content.as_slice();
        }
        assert_eq!(levels, MAX_DEPTH + 1);
        assert_eq!(inline_text(&content), "[".repeat(7) + "x" + &"](http://h.test)".repeat(7));
    }

    #[test]
    fn inline_text_and_odd_input_never_fails() {
        assert_eq!(inline_text(&[Inline::Strong(vec![t("a")]), Inline::Break, Inline::Image { alt: "i".into(), href: "h".into() }]), "a\ni");
        // odd input is parsed rather than passed through as one plain block, which is what an
        // internal panic would give; a panic here would pass unnoticed before
        for s in ["*", "**", "***", "~~", "[", "![", "](", "<", "`", "|", "|\n|-", "- [", "1.", "> ", "\t- x\n\t\t- y", "* * *x", "_ _ _"] {
            let blocks = parse(s);
            assert!(!blocks.iter().any(|b| matches!(b, Block::Plain(_))), "{s:?} fell back to plain text");
        }
        let mixed = "# T\n> - a\n>   ```\n>   x\n| a |\n|---|\n- [ ] t\n  1. n\n     > q";
        assert_eq!(parse(mixed).first(), Some(&Block::Heading { level: 3, content: vec![t("T")] }));
        assert!(!parse(mixed).iter().any(|b| matches!(b, Block::Plain(_))), "{:?}", parse(mixed));
        assert_eq!(parse(mixed).len(), 4, "{:?}", parse(mixed));
    }
}
