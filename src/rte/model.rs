//! Document model of the rich-text composer: a flat list of blocks, each holding
//! styled text runs. Positions are (block index, byte offset into the block text).
//! Tables are stored as consecutive `Cell` blocks (row-major) sharing a table id,
//! which keeps every editable position addressable the same way.
use std::sync::Arc;

#[derive(Clone, Default, PartialEq, Eq, Hash, Debug)]
pub struct Marks {
    pub bold: bool,
    pub italic: bool,
    pub underline: bool,
    pub strike: bool,
    pub code: bool,
    pub link: Option<String>,
    pub color: Option<String>,
    pub highlight: Option<String>,
    /// Font size in px (None = body size).
    pub size: Option<u8>,
}

impl Marks {
    pub fn is_plain(&self) -> bool { *self == Marks::default() }
}

#[derive(Clone, Copy, PartialEq, Eq, Hash, Debug)]
pub enum Mark { Bold, Italic, Underline, Strike, Code }

impl Mark {
    pub fn get(self, m: &Marks) -> bool {
        match self { Mark::Bold => m.bold, Mark::Italic => m.italic, Mark::Underline => m.underline, Mark::Strike => m.strike, Mark::Code => m.code }
    }
    pub fn set(self, m: &mut Marks, v: bool) {
        match self { Mark::Bold => m.bold = v, Mark::Italic => m.italic = v, Mark::Underline => m.underline = v, Mark::Strike => m.strike = v, Mark::Code => m.code = v }
    }
}

#[derive(Clone, PartialEq, Eq, Hash, Debug)]
pub struct Run {
    pub text: String,
    pub marks: Marks,
}

/// An inline image (`cid:` attachment once uploaded, or a remote/data URL kept as is).
#[derive(Clone, PartialEq, Eq, Hash, Debug, Default)]
pub struct ImageData {
    pub src: String,
    pub cid: Option<String>,
    pub blob_id: Option<String>,
    pub mime: String,
    pub name: String,
    /// Display size in px (0 = natural, capped by the editor width).
    pub width: u32,
    pub height: u32,
    /// Encoded bytes for the preview (pasted/inserted images).
    pub bytes: Option<Arc<Vec<u8>>>,
    pub alt: String,
}

#[derive(Clone, PartialEq, Eq, Hash, Debug)]
pub enum Kind {
    Para,
    Heading(u8),
    Quote,
    Code,
    Bullet,
    Ordered,
    Cell { table: u32, row: u32, col: u32, header: bool },
    Image(ImageData),
    /// The quoted original of a reply/forward: kept as HTML, shown collapsed.
    /// `cite` = reply quote (blockquote, "> " in text/plain); false = forwarded body.
    Quoted { html: String, text: String, open: bool, cite: bool },
    Rule,
}

impl Kind {
    pub fn is_atomic(&self) -> bool { matches!(self, Kind::Image(_) | Kind::Quoted { .. } | Kind::Rule) }
    pub fn is_list(&self) -> bool { matches!(self, Kind::Bullet | Kind::Ordered) }
    pub fn is_cell(&self) -> bool { matches!(self, Kind::Cell { .. }) }
    pub fn table(&self) -> Option<u32> { if let Kind::Cell { table, .. } = self { Some(*table) } else { None } }
}

#[derive(Clone, Copy, PartialEq, Eq, Hash, Debug, Default)]
pub enum Align { #[default] Left, Center, Right, Justify }

impl Align {
    pub fn css(self) -> &'static str {
        match self { Align::Left => "left", Align::Center => "center", Align::Right => "right", Align::Justify => "justify" }
    }
}

#[derive(Clone, PartialEq, Eq, Hash, Debug)]
pub struct Block {
    pub kind: Kind,
    pub align: Align,
    /// Nesting level for list items (0 = top level).
    pub indent: u8,
    pub runs: Vec<Run>,
}

impl Block {
    pub fn new(kind: Kind) -> Self { Block { kind, align: Align::Left, indent: 0, runs: vec![] } }
    pub fn para(text: &str) -> Self { Self::with_text(Kind::Para, text, Marks::default()) }
    pub fn with_text(kind: Kind, text: &str, marks: Marks) -> Self {
        let mut b = Block::new(kind);
        if !text.is_empty() { b.runs.push(Run { text: text.into(), marks }); }
        b
    }
    pub fn text(&self) -> String { self.runs.iter().map(|r| r.text.as_str()).collect() }
    pub fn len(&self) -> usize { self.runs.iter().map(|r| r.text.len()).sum() }
    pub fn is_empty(&self) -> bool { self.len() == 0 }

    /// Merge neighbouring runs with equal marks and drop empty ones.
    pub fn normalize(&mut self) {
        let mut out: Vec<Run> = Vec::with_capacity(self.runs.len());
        for r in self.runs.drain(..) {
            if r.text.is_empty() { continue; }
            match out.last_mut() {
                Some(l) if l.marks == r.marks => l.text.push_str(&r.text),
                _ => out.push(r),
            }
        }
        self.runs = out;
    }

    /// Split the runs at byte offset `o` so a run boundary falls there; returns the run index starting at `o`.
    fn split_at(&mut self, o: usize) -> usize {
        let mut acc = 0;
        for i in 0..self.runs.len() {
            let l = self.runs[i].text.len();
            if o == acc { return i; }
            if o < acc + l {
                let tail = self.runs[i].text.split_off(o - acc);
                let marks = self.runs[i].marks.clone();
                self.runs.insert(i + 1, Run { text: tail, marks });
                return i + 1;
            }
            acc += l;
        }
        self.runs.len()
    }

    pub fn insert(&mut self, o: usize, text: &str, marks: &Marks) {
        if text.is_empty() { return; }
        let i = self.split_at(o.min(self.len()));
        self.runs.insert(i, Run { text: text.into(), marks: marks.clone() });
        self.normalize();
    }

    pub fn insert_runs(&mut self, o: usize, runs: Vec<Run>) {
        let i = self.split_at(o.min(self.len()));
        for (k, r) in runs.into_iter().enumerate() { self.runs.insert(i + k, r); }
        self.normalize();
    }

    /// Remove and return the runs of byte range a..b.
    pub fn cut(&mut self, a: usize, b: usize) -> Vec<Run> {
        let (a, b) = (a.min(self.len()), b.min(self.len()));
        if a >= b { return vec![]; }
        let i = self.split_at(a);
        let j = self.split_at(b);
        let out: Vec<Run> = self.runs.drain(i..j).collect();
        self.normalize();
        out
    }

    pub fn slice(&self, a: usize, b: usize) -> Vec<Run> {
        let mut c = self.clone();
        c.cut(a, b)
    }

    /// Apply `f` to the marks of byte range a..b.
    pub fn map_marks(&mut self, a: usize, b: usize, f: &dyn Fn(&mut Marks)) {
        if a >= b { return; }
        let i = self.split_at(a);
        let j = self.split_at(b);
        for r in &mut self.runs[i..j] { f(&mut r.marks); }
        self.normalize();
    }

    /// Marks in effect at a caret position (those of the character before it).
    pub fn marks_at(&self, o: usize) -> Marks {
        let mut acc = 0;
        let mut last = None;
        for r in &self.runs {
            if o > acc && o <= acc + r.text.len() { return r.marks.clone(); }
            if o == 0 && acc == 0 { last = Some(r.marks.clone()); break; }
            acc += r.text.len();
        }
        let mut m = last.unwrap_or_default();
        // a link does not extend past its start
        if o == 0 { m.link = None; }
        m
    }

    /// Runs overlapping byte range a..b (for "is the mark on everywhere" checks).
    pub fn runs_in(&self, a: usize, b: usize) -> Vec<&Run> {
        let mut acc = 0;
        let mut out = vec![];
        for r in &self.runs {
            let (s, e) = (acc, acc + r.text.len());
            if e > a && s < b { out.push(r); }
            acc = e;
        }
        out
    }
}

#[derive(Clone, Copy, PartialEq, Eq, Hash, Debug, Default)]
pub struct Pos {
    pub b: usize,
    pub o: usize,
}

impl Pos {
    pub fn new(b: usize, o: usize) -> Self { Pos { b, o } }
}

impl PartialOrd for Pos {
    fn partial_cmp(&self, other: &Self) -> Option<std::cmp::Ordering> { Some(self.cmp(other)) }
}
impl Ord for Pos {
    fn cmp(&self, other: &Self) -> std::cmp::Ordering { (self.b, self.o).cmp(&(other.b, other.o)) }
}

#[derive(Clone, Copy, PartialEq, Eq, Debug, Default)]
pub struct Sel {
    pub anchor: Pos,
    pub focus: Pos,
}

impl Sel {
    pub fn caret(p: Pos) -> Self { Sel { anchor: p, focus: p } }
    pub fn is_collapsed(&self) -> bool { self.anchor == self.focus }
    pub fn start(&self) -> Pos { self.anchor.min(self.focus) }
    pub fn end(&self) -> Pos { self.anchor.max(self.focus) }
}

#[derive(Clone, PartialEq, Eq, Hash, Debug)]
pub struct Doc {
    pub blocks: Vec<Block>,
}

impl Default for Doc {
    fn default() -> Self { Doc { blocks: vec![Block::new(Kind::Para)] } }
}

pub fn next_char(s: &str, o: usize) -> usize {
    s[o..].chars().next().map(|c| o + c.len_utf8()).unwrap_or(o)
}
pub fn prev_char(s: &str, o: usize) -> usize {
    s[..o].chars().next_back().map(|c| o - c.len_utf8()).unwrap_or(o)
}

impl Doc {
    pub fn from_text(text: &str) -> Self {
        let blocks: Vec<Block> = text.split('\n').map(Block::para).collect();
        let mut d = Doc { blocks };
        d.fix();
        d
    }

    /// Keep the invariants: at least one block, an editable block at the end, valid table numbering.
    pub fn fix(&mut self) {
        if self.blocks.is_empty() { self.blocks.push(Block::new(Kind::Para)); }
        if self.blocks.last().is_some_and(|b| b.kind.is_atomic() || b.kind.is_cell()) {
            self.blocks.push(Block::new(Kind::Para));
        }
        for b in &mut self.blocks {
            if b.kind.is_atomic() { b.runs.clear(); }
            b.normalize();
        }
    }

    pub fn clamp(&self, p: Pos) -> Pos {
        let b = p.b.min(self.blocks.len() - 1);
        let t = self.blocks[b].text();
        let mut o = p.o.min(t.len());
        while !t.is_char_boundary(o) { o -= 1; }
        Pos { b, o }
    }

    pub fn end(&self) -> Pos {
        let b = self.blocks.len() - 1;
        Pos { b, o: self.blocks[b].len() }
    }

    pub fn is_empty(&self) -> bool {
        self.blocks.iter().all(|b| b.is_empty() && matches!(b.kind, Kind::Para))
    }

    /// Plain text of a range (blocks joined with newlines).
    pub fn text_range(&self, a: Pos, b: Pos) -> String {
        let mut out = String::new();
        for i in a.b..=b.b.min(self.blocks.len() - 1) {
            let blk = &self.blocks[i];
            let t = blk.text();
            let s = if i == a.b { a.o.min(t.len()) } else { 0 };
            let e = if i == b.b { b.o.min(t.len()) } else { t.len() };
            if i > a.b { out.push('\n'); }
            match &blk.kind {
                Kind::Quoted { text, .. } => out.push_str(&text.lines().map(|l| format!("> {l}")).collect::<Vec<_>>().join("\n")),
                _ => out.push_str(&t[s..e.max(s)]),
            }
        }
        out
    }

    /// The blocks of a range, as a fragment (partial first/last blocks trimmed).
    pub fn fragment(&self, a: Pos, b: Pos) -> Vec<Block> {
        let mut out = vec![];
        for i in a.b..=b.b.min(self.blocks.len() - 1) {
            let blk = &self.blocks[i];
            let mut nb = blk.clone();
            if !blk.kind.is_atomic() {
                let s = if i == a.b { a.o } else { 0 };
                let e = if i == b.b { b.o } else { blk.len() };
                nb.runs = blk.slice(s, e);
            }
            out.push(nb);
        }
        out
    }

    /// Delete the range a..b. Returns the caret position afterwards.
    pub fn delete(&mut self, a: Pos, b: Pos) -> Pos {
        let (a, b) = (a.min(b), a.max(b));
        if a == b { return a; }
        if a.b == b.b {
            if self.blocks[a.b].kind.is_atomic() { return a; }
            self.blocks[a.b].cut(a.o, b.o);
            return a;
        }
        let first_cell = self.blocks[a.b].kind.is_cell();
        let last_cell = self.blocks[b.b].kind.is_cell();
        // middle blocks: cells are emptied (tables keep their shape), others removed
        let mut remove = vec![];
        for i in a.b + 1..b.b {
            if self.blocks[i].kind.is_cell() { self.blocks[i].runs.clear(); } else { remove.push(i); }
        }
        let first_atomic = self.blocks[a.b].kind.is_atomic();
        if !first_atomic { let l = self.blocks[a.b].len(); self.blocks[a.b].cut(a.o, l); }
        let last_atomic = self.blocks[b.b].kind.is_atomic();
        if last_atomic { remove.push(b.b); }
        let mut caret = a;
        if !last_atomic {
            if first_cell || last_cell || first_atomic {
                self.blocks[b.b].cut(0, b.o);
            } else {
                // merge the tail of the last block into the first
                let tail = { let l = self.blocks[b.b].len(); self.blocks[b.b].cut(b.o, l) };
                self.blocks[a.b].insert_runs(a.o, tail);
                remove.push(b.b);
            }
        }
        if first_atomic && a.o == 0 {
            remove.push(a.b);
            caret = Pos { b: a.b, o: 0 };
        }
        remove.sort_unstable();
        remove.dedup();
        for &i in remove.iter().rev() { self.blocks.remove(i); }
        self.fix();
        self.clamp(caret)
    }

    pub fn insert_text(&mut self, p: Pos, text: &str, marks: &Marks) -> Pos {
        let p = self.clamp(p);
        if self.blocks[p.b].kind.is_atomic() {
            self.blocks.insert(p.b + 1, Block::new(Kind::Para));
            return self.insert_text(Pos::new(p.b + 1, 0), text, marks);
        }
        let blk = &mut self.blocks[p.b];
        blk.insert(p.o, text, marks);
        Pos { b: p.b, o: p.o + text.len() }
    }

    /// Split a block at `p` (Enter). Returns the caret position at the start of the new block.
    pub fn split(&mut self, p: Pos) -> Pos {
        let p = self.clamp(p);
        let blk = &mut self.blocks[p.b];
        if blk.kind.is_atomic() {
            self.blocks.insert(p.b + 1, Block::new(Kind::Para));
            return Pos::new(p.b + 1, 0);
        }
        let tail = { let l = blk.len(); blk.cut(p.o, l) };
        let kind = match &blk.kind {
            Kind::Heading(_) if tail.is_empty() => Kind::Para,
            k => k.clone(),
        };
        let mut nb = Block { kind, align: blk.align, indent: blk.indent, runs: tail };
        nb.normalize();
        self.blocks.insert(p.b + 1, nb);
        Pos::new(p.b + 1, 0)
    }

    /// Insert whole blocks at `p`, splitting the block there (paste, templates, signatures).
    pub fn insert_blocks(&mut self, p: Pos, mut frag: Vec<Block>) -> Pos {
        let p = self.clamp(p);
        if frag.is_empty() { return p; }
        // a single text block pastes inline
        if frag.len() == 1 && !frag[0].kind.is_atomic() && !frag[0].kind.is_cell() && !self.blocks[p.b].kind.is_atomic() {
            let runs = std::mem::take(&mut frag[0].runs);
            let n: usize = runs.iter().map(|r| r.text.len()).sum();
            self.blocks[p.b].insert_runs(p.o, runs);
            return Pos::new(p.b, p.o + n);
        }
        let in_cell = self.blocks[p.b].kind.is_cell();
        if in_cell {
            // paste text into the cell, as lines
            let text: Vec<String> = frag.iter().map(|b| b.text()).collect();
            let s = text.join("\n");
            let m = self.blocks[p.b].marks_at(p.o);
            return self.insert_text(p, &s, &m);
        }
        let tables_base = self.blocks.iter().filter_map(|b| b.kind.table()).max().map(|t| t + 1).unwrap_or(0);
        for b in &mut frag {
            if let Kind::Cell { table, .. } = &mut b.kind { *table += tables_base; }
        }
        let first_text = !frag[0].kind.is_atomic() && !frag[0].kind.is_cell();
        let at_start_empty = self.blocks[p.b].is_empty() && !self.blocks[p.b].kind.is_atomic();
        let after = if self.blocks[p.b].kind.is_atomic() { Pos::new(p.b + 1, 0) } else { self.split(p) };
        if !self.blocks[p.b].kind.is_atomic() && first_text {
            // first fragment block merges into the block before the split
            let first = frag.remove(0);
            let o = self.blocks[p.b].len();
            if at_start_empty { self.blocks[p.b].kind = first.kind.clone(); self.blocks[p.b].align = first.align; self.blocks[p.b].indent = first.indent; }
            self.blocks[p.b].insert_runs(o, first.runs);
        }
        let n = frag.len();
        let last_is_text = frag.last().is_some_and(|b| !b.kind.is_atomic() && !b.kind.is_cell());
        for (k, b) in frag.into_iter().enumerate() { self.blocks.insert(after.b + k, b); }
        let mut caret = Pos::new(after.b + n, 0);
        if last_is_text && n > 0 {
            // merge the split-off tail into the last inserted block
            let tail_idx = after.b + n;
            if tail_idx < self.blocks.len() && !self.blocks[tail_idx].kind.is_atomic() && !self.blocks[tail_idx].kind.is_cell() {
                let li = tail_idx - 1;
                let o = self.blocks[li].len();
                let tail = std::mem::take(&mut self.blocks[tail_idx].runs);
                self.blocks[li].insert_runs(o, tail);
                self.blocks.remove(tail_idx);
                caret = Pos::new(li, o);
            }
        }
        self.fix();
        self.clamp(caret)
    }

    /// Ordinal number of an ordered list item (counting siblings at the same level).
    pub fn ordinal(&self, i: usize) -> usize {
        let lvl = self.blocks[i].indent;
        let mut n = 1;
        let mut j = i;
        while j > 0 {
            j -= 1;
            let b = &self.blocks[j];
            if !b.kind.is_list() { break; }
            if b.indent < lvl { break; }
            if b.indent == lvl {
                if b.kind == Kind::Ordered { n += 1; } else { break; }
            }
        }
        n
    }

    /// Range of block indices forming the table that block `i` belongs to.
    pub fn table_range(&self, i: usize) -> Option<std::ops::Range<usize>> {
        let t = self.blocks.get(i)?.kind.table()?;
        let mut s = i;
        while s > 0 && self.blocks[s - 1].kind.table() == Some(t) { s -= 1; }
        let mut e = i + 1;
        while e < self.blocks.len() && self.blocks[e].kind.table() == Some(t) { e += 1; }
        Some(s..e)
    }

    pub fn table_dims(&self, r: &std::ops::Range<usize>) -> (u32, u32) {
        let mut rows = 0;
        let mut cols = 0;
        for b in &self.blocks[r.clone()] {
            if let Kind::Cell { row, col, .. } = b.kind { rows = rows.max(row + 1); cols = cols.max(col + 1); }
        }
        (rows, cols)
    }

    pub fn new_table_id(&self) -> u32 {
        self.blocks.iter().filter_map(|b| b.kind.table()).max().map(|t| t + 1).unwrap_or(0)
    }

    /// Rebuild a table from a grid of cell blocks.
    pub fn set_table(&mut self, r: std::ops::Range<usize>, grid: Vec<Vec<Block>>) -> usize {
        let t = self.blocks[r.start].kind.table().unwrap_or(0);
        let mut cells = vec![];
        for (ri, row) in grid.into_iter().enumerate() {
            for (ci, mut c) in row.into_iter().enumerate() {
                let header = matches!(c.kind, Kind::Cell { header: true, .. });
                c.kind = Kind::Cell { table: t, row: ri as u32, col: ci as u32, header };
                cells.push(c);
            }
        }
        let n = cells.len();
        let start = r.start;
        self.blocks.splice(r, cells);
        if n == 0 { self.fix(); }
        start
    }

    pub fn table_grid(&self, r: &std::ops::Range<usize>) -> Vec<Vec<Block>> {
        let (rows, cols) = self.table_dims(r);
        let mut grid = vec![vec![Block::new(Kind::Cell { table: 0, row: 0, col: 0, header: false }); cols as usize]; rows as usize];
        for b in &self.blocks[r.clone()] {
            if let Kind::Cell { row, col, .. } = b.kind { grid[row as usize][col as usize] = b.clone(); }
        }
        grid
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn bold() -> Marks { Marks { bold: true, ..Default::default() } }

    #[test]
    fn insert_and_merge_runs() {
        let mut b = Block::para("hello world");
        b.map_marks(0, 5, &|m| m.bold = true);
        assert_eq!(b.runs.len(), 2);
        b.insert(5, "!", &bold());
        assert_eq!(b.runs[0].text, "hello!");
        b.map_marks(0, 6, &|m| m.bold = false);
        assert_eq!(b.runs.len(), 1);
        assert_eq!(b.text(), "hello! world");
    }

    #[test]
    fn split_and_merge_blocks() {
        let mut d = Doc::from_text("abcdef");
        let p = d.split(Pos::new(0, 3));
        assert_eq!(p, Pos::new(1, 0));
        assert_eq!(d.blocks[0].text(), "abc");
        assert_eq!(d.blocks[1].text(), "def");
        let p = d.delete(Pos::new(0, 2), Pos::new(1, 1));
        assert_eq!(p, Pos::new(0, 2));
        assert_eq!(d.blocks.len(), 1);
        assert_eq!(d.blocks[0].text(), "abef");
    }

    #[test]
    fn heading_enter_at_end_gives_paragraph() {
        let mut d = Doc { blocks: vec![Block::with_text(Kind::Heading(1), "Title", Marks::default())] };
        let p = d.split(Pos::new(0, 5));
        assert_eq!(d.blocks[p.b].kind, Kind::Para);
    }

    #[test]
    fn ordinal_counts_levels() {
        let mut d = Doc { blocks: vec![] };
        for (lvl, k) in [(0, Kind::Ordered), (0, Kind::Ordered), (1, Kind::Ordered), (0, Kind::Ordered)] {
            let mut b = Block::para("x"); b.kind = k; b.indent = lvl; d.blocks.push(b);
        }
        assert_eq!(d.ordinal(1), 2);
        assert_eq!(d.ordinal(2), 1);
        assert_eq!(d.ordinal(3), 3);
    }

    #[test]
    fn paste_multi_block() {
        let mut d = Doc::from_text("helloworld");
        let frag = vec![Block::para("A"), Block::with_text(Kind::Heading(2), "B", Marks::default()), Block::para("C")];
        let p = d.insert_blocks(Pos::new(0, 5), frag);
        let texts: Vec<String> = d.blocks.iter().map(|b| b.text()).collect();
        assert_eq!(texts, vec!["helloA", "B", "Cworld"]);
        assert_eq!(p, Pos::new(2, 1));
    }

    #[test]
    fn delete_keeps_table_shape() {
        let mut d = Doc { blocks: vec![Block::para("before")] };
        for (r, c) in [(0, 0), (0, 1)] {
            d.blocks.push(Block::with_text(Kind::Cell { table: 0, row: r, col: c, header: false }, "cell", Marks::default()));
        }
        d.blocks.push(Block::para("after"));
        d.delete(Pos::new(1, 2), Pos::new(2, 2));
        assert_eq!(d.blocks.len(), 4);
        assert_eq!(d.blocks[1].text(), "ce");
        assert_eq!(d.blocks[2].text(), "ll");
    }

    #[test]
    fn marks_at_caret() {
        let mut b = Block::para("ab");
        b.map_marks(0, 1, &|m| m.bold = true);
        assert!(b.marks_at(1).bold);
        assert!(!b.marks_at(2).bold);
        assert!(b.marks_at(0).bold);
    }
}
