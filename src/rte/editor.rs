//! Editing commands, input rules and undo/redo on top of the document model.
//! Layout-independent; cursor movement that needs line geometry lives in `layout`.
use super::model::*;
use std::time::{Duration, Instant};

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum EditKind { Typing, Delete, Other }

pub struct Editor {
    pub doc: Doc,
    pub sel: Sel,
    /// Marks toggled at a collapsed caret, applied to the next typed text.
    pub stored: Option<Marks>,
    undo: Vec<(Doc, Sel)>,
    redo: Vec<(Doc, Sel)>,
    last: Option<(EditKind, Instant)>,
    /// Preferred x for vertical caret movement.
    pub goal_x: Option<f32>,
    /// Bumped on every change (drives relayout and "dirty" checks).
    pub version: u64,
}

impl Default for Editor {
    fn default() -> Self { Editor::new(Doc::default()) }
}

const HISTORY: usize = 300;

impl Editor {
    pub fn new(doc: Doc) -> Self {
        let mut d = doc;
        d.fix();
        Editor { doc: d, sel: Sel::default(), stored: None, undo: vec![], redo: vec![], last: None, goal_x: None, version: 1 }
    }

    /// Replace the whole document (opening the composer); clears history.
    pub fn load(&mut self, doc: Doc) {
        *self = Editor::new(doc);
    }

    // ------------------------------------------------------------ history

    pub fn checkpoint(&mut self, k: EditKind) {
        let now = Instant::now();
        let merge = matches!(k, EditKind::Typing | EditKind::Delete)
            && self.last.is_some_and(|(lk, t)| lk == k && now.duration_since(t) < Duration::from_millis(1200));
        if !merge {
            self.undo.push((self.doc.clone(), self.sel));
            if self.undo.len() > HISTORY { self.undo.remove(0); }
        }
        self.redo.clear();
        self.last = Some((k, now));
        self.version += 1;
    }

    /// End the current typing group (caret moved by the user, etc.).
    pub fn break_group(&mut self) { self.last = None; }

    pub fn can_undo(&self) -> bool { !self.undo.is_empty() }
    pub fn can_redo(&self) -> bool { !self.redo.is_empty() }

    pub fn undo(&mut self) -> bool {
        let Some((d, s)) = self.undo.pop() else { return false };
        self.redo.push((std::mem::replace(&mut self.doc, d), self.sel));
        self.sel = s;
        self.after_change();
        true
    }

    pub fn redo(&mut self) -> bool {
        let Some((d, s)) = self.redo.pop() else { return false };
        self.undo.push((std::mem::replace(&mut self.doc, d), self.sel));
        self.sel = s;
        self.after_change();
        true
    }

    fn after_change(&mut self) {
        self.last = None;
        self.stored = None;
        self.version += 1;
        self.sel.anchor = self.doc.clamp(self.sel.anchor);
        self.sel.focus = self.doc.clamp(self.sel.focus);
    }

    // ------------------------------------------------------------ selection

    pub fn set_caret(&mut self, p: Pos) {
        self.sel = Sel::caret(self.doc.clamp(p));
        self.stored = None;
        self.break_group();
    }

    pub fn select(&mut self, anchor: Pos, focus: Pos) {
        self.sel = Sel { anchor: self.doc.clamp(anchor), focus: self.doc.clamp(focus) };
        self.stored = None;
        self.break_group();
    }

    pub fn select_all(&mut self) {
        self.select(Pos::new(0, 0), self.doc.end());
    }

    pub fn selected_text(&self) -> String {
        if self.sel.is_collapsed() { return String::new(); }
        self.doc.text_range(self.sel.start(), self.sel.end())
    }

    pub fn selected_blocks(&self) -> Vec<Block> {
        if self.sel.is_collapsed() { return vec![]; }
        self.doc.fragment(self.sel.start(), self.sel.end())
    }

    /// Select the word around `p`.
    pub fn select_word(&mut self, p: Pos) {
        let p = self.doc.clamp(p);
        let t = self.doc.blocks[p.b].text();
        let is_word = |c: char| c.is_alphanumeric() || c == '_' || c == '\'' || c == '-';
        let mut a = p.o;
        while a > 0 { let q = prev_char(&t, a); if t[q..a].chars().all(is_word) { a = q; } else { break; } }
        let mut b = p.o;
        while b < t.len() { let q = next_char(&t, b); if t[b..q].chars().all(is_word) { b = q; } else { break; } }
        if a == b && b < t.len() { b = next_char(&t, b); }
        self.select(Pos::new(p.b, a), Pos::new(p.b, b));
    }

    pub fn select_block(&mut self, b: usize) {
        let b = b.min(self.doc.blocks.len() - 1);
        self.select(Pos::new(b, 0), Pos::new(b, self.doc.blocks[b].len()));
    }

    // ------------------------------------------------------------ queries

    /// Marks that typed text would get now.
    pub fn current_marks(&self) -> Marks {
        if let Some(m) = &self.stored { return m.clone(); }
        let p = self.sel.focus;
        if self.sel.is_collapsed() {
            return self.doc.blocks[p.b].marks_at(p.o);
        }
        // selection: a mark is "on" if every selected run has it
        let (a, b) = (self.sel.start(), self.sel.end());
        let mut first: Option<Marks> = None;
        let mut all = [true; 5];
        for i in a.b..=b.b {
            let blk = &self.doc.blocks[i];
            if blk.kind.is_atomic() { continue; }
            let s = if i == a.b { a.o } else { 0 };
            let e = if i == b.b { b.o } else { blk.len() };
            for r in blk.runs_in(s, e) {
                if first.is_none() { first = Some(r.marks.clone()); }
                for (k, m) in [Mark::Bold, Mark::Italic, Mark::Underline, Mark::Strike, Mark::Code].into_iter().enumerate() {
                    all[k] &= m.get(&r.marks);
                }
            }
        }
        let mut m = first.unwrap_or_default();
        for (k, mk) in [Mark::Bold, Mark::Italic, Mark::Underline, Mark::Strike, Mark::Code].into_iter().enumerate() { mk.set(&mut m, all[k]); }
        m
    }

    pub fn focus_block(&self) -> &Block { &self.doc.blocks[self.sel.focus.b] }

    // ------------------------------------------------------------ text edits

    /// Delete the selection if any; returns true if something was deleted.
    fn delete_selection(&mut self) -> bool {
        if self.sel.is_collapsed() { return false; }
        let p = self.doc.delete(self.sel.start(), self.sel.end());
        self.sel = Sel::caret(p);
        true
    }

    pub fn insert_text(&mut self, text: &str) {
        if text.is_empty() { return; }
        let typing = text.chars().count() == 1;
        self.checkpoint(if typing { EditKind::Typing } else { EditKind::Other });
        let marks = self.current_marks();
        let stored = self.stored.take();
        self.delete_selection();
        let marks = stored.unwrap_or(if self.sel.is_collapsed() { self.doc.blocks[self.sel.focus.b].marks_at(self.sel.focus.o) } else { marks });
        // a code block or table cell keeps newlines; elsewhere a multi-line text becomes paragraphs
        let p = if text.contains('\n') && !matches!(self.focus_block().kind, Kind::Code | Kind::Cell { .. }) {
            let blocks: Vec<Block> = text.split('\n').map(|l| Block::with_text(Kind::Para, l, marks.clone())).collect();
            self.doc.insert_blocks(self.sel.focus, blocks)
        } else {
            self.doc.insert_text(self.sel.focus, text, &marks)
        };
        self.sel = Sel::caret(p);
        self.goal_x = None;
        if typing { self.input_rules(text.chars().next().unwrap()); }
    }

    /// Markdown-style shortcuts applied as you type.
    fn input_rules(&mut self, c: char) {
        let p = self.sel.focus;
        let blk = &self.doc.blocks[p.b];
        let t = blk.text();
        let before = &t[..p.o];
        // block rules at the start of a paragraph
        if c == ' ' && blk.kind == Kind::Para {
            let prefix = &before[..before.len() - 1];
            let kind = match prefix {
                "#" => Some(Kind::Heading(1)),
                "##" => Some(Kind::Heading(2)),
                "###" => Some(Kind::Heading(3)),
                "-" | "*" | "+" => Some(Kind::Bullet),
                ">" => Some(Kind::Quote),
                "```" => Some(Kind::Code),
                s if s.len() >= 2 && s.ends_with('.') && s[..s.len() - 1].chars().all(|c| c.is_ascii_digit()) => Some(Kind::Ordered),
                _ => None,
            };
            if let Some(k) = kind {
                self.undo.push((self.doc.clone(), self.sel));
                let b = &mut self.doc.blocks[p.b];
                b.cut(0, p.o);
                b.kind = k;
                self.sel = Sel::caret(Pos::new(p.b, 0));
                self.last = None; // the conversion is its own undo step
                return;
            }
        }
        if blk.kind == Kind::Code { return; }
        // inline rules: **bold**, *italic*, `code`, ~~strike~~
        let rules: [(&str, Mark); 5] = [("**", Mark::Bold), ("__", Mark::Bold), ("~~", Mark::Strike), ("`", Mark::Code), ("*", Mark::Italic)];
        for (d, mark) in rules {
            if !before.ends_with(d) || !d.ends_with(c) { continue; }
            let close = before.len() - d.len();
            let Some(open) = before[..close].rfind(d) else { continue };
            let inner = &before[open + d.len()..close];
            if inner.is_empty() || inner.starts_with(' ') || inner.ends_with(' ') { continue; }
            if d == "*" && (inner.contains('*') || before[..open].ends_with('*')) { continue; }
            if d == "`" && inner.contains('`') { continue; }
            self.undo.push((self.doc.clone(), self.sel));
            let b = &mut self.doc.blocks[p.b];
            b.cut(close, close + d.len());
            b.cut(open, open + d.len());
            let (s, e) = (open, close - d.len());
            b.map_marks(s, e, &|m| mark.set(m, true));
            self.sel = Sel::caret(Pos::new(p.b, e));
            let mut m = b.marks_at(e);
            mark.set(&mut m, false);
            self.stored = Some(m);
            self.last = None;
            return;
        }
    }

    pub fn enter(&mut self, shift: bool) {
        self.checkpoint(EditKind::Other);
        self.delete_selection();
        let p = self.sel.focus;
        let kind = self.doc.blocks[p.b].kind.clone();
        let empty = self.doc.blocks[p.b].is_empty();
        if shift || matches!(kind, Kind::Cell { .. }) {
            let m = self.doc.blocks[p.b].marks_at(p.o);
            let np = self.doc.insert_text(p, "\n", &m);
            self.sel = Sel::caret(np);
            return;
        }
        if kind == Kind::Code {
            let t = self.doc.blocks[p.b].text();
            // Enter on an empty last line leaves the code block
            if p.o == t.len() && (t.ends_with('\n') || t.is_empty()) {
                if !t.is_empty() { self.doc.blocks[p.b].cut(t.len() - 1, t.len()); }
                if t.is_empty() { self.doc.blocks[p.b].kind = Kind::Para; return; }
                self.doc.blocks.insert(p.b + 1, Block::new(Kind::Para));
                self.sel = Sel::caret(Pos::new(p.b + 1, 0));
                return;
            }
            let np = self.doc.insert_text(p, "\n", &Marks::default());
            self.sel = Sel::caret(np);
            return;
        }
        if empty && (kind.is_list() || kind == Kind::Quote) {
            let b = &mut self.doc.blocks[p.b];
            if kind.is_list() && b.indent > 0 { b.indent -= 1; } else { b.kind = Kind::Para; b.indent = 0; }
            return;
        }
        let np = self.doc.split(p);
        // carry the caret marks into the new block (minus links)
        let mut m = self.doc.blocks[p.b].marks_at(p.o);
        m.link = None;
        if !m.is_plain() && self.doc.blocks[np.b].is_empty() { self.stored = Some(m); }
        self.sel = Sel::caret(np);
    }

    pub fn backspace(&mut self, word: bool) {
        if !self.sel.is_collapsed() { self.checkpoint(EditKind::Delete); self.delete_selection(); return; }
        let p = self.sel.focus;
        let blk = &self.doc.blocks[p.b];
        if p.o > 0 {
            let t = blk.text();
            self.checkpoint(EditKind::Delete);
            let mut a = prev_char(&t, p.o);
            if word {
                while a > 0 && t[..a].ends_with(' ') { a = prev_char(&t, a); }
                while a > 0 { let q = prev_char(&t, a); if t[q..a].chars().all(|c| c.is_alphanumeric() || c == '_') { a = q; } else { break; } }
            }
            let np = self.doc.delete(Pos::new(p.b, a), p);
            self.sel = Sel::caret(np);
            return;
        }
        // at the start of a block
        let kind = blk.kind.clone();
        if kind.is_atomic() {
            self.checkpoint(EditKind::Other);
            self.doc.blocks.remove(p.b);
            self.doc.fix();
            self.sel = Sel::caret(self.doc.clamp(Pos::new(p.b.saturating_sub(if p.b > 0 { 1 } else { 0 }), usize::MAX)));
            if p.b < self.doc.blocks.len() && p.b > 0 { self.sel = Sel::caret(Pos::new(p.b - 1, self.doc.blocks[p.b - 1].len())); }
            return;
        }
        if kind.is_list() || matches!(kind, Kind::Quote | Kind::Heading(_) | Kind::Code) {
            self.checkpoint(EditKind::Other);
            let b = &mut self.doc.blocks[p.b];
            if kind.is_list() && b.indent > 0 { b.indent -= 1; } else { b.kind = Kind::Para; b.indent = 0; }
            return;
        }
        if kind.is_cell() || p.b == 0 { return; }
        let prev = &self.doc.blocks[p.b - 1];
        if prev.kind.is_cell() {
            // leave the cell alone; just remove an empty paragraph after a table
            if self.doc.blocks[p.b].is_empty() && p.b + 1 < self.doc.blocks.len() {
                self.checkpoint(EditKind::Other);
                self.doc.blocks.remove(p.b);
            }
            self.sel = Sel::caret(Pos::new(p.b - 1, self.doc.blocks[p.b - 1].len()));
            return;
        }
        let (prev_atomic, l) = (prev.kind.is_atomic(), prev.len());
        self.checkpoint(EditKind::Other);
        if prev_atomic {
            self.doc.blocks.remove(p.b - 1);
            self.sel = Sel::caret(Pos::new(p.b - 1, 0));
            return;
        }
        // an empty paragraph merges "up"; otherwise the block joins the previous one
        let np = self.doc.delete(Pos::new(p.b - 1, l), Pos::new(p.b, 0));
        self.sel = Sel::caret(np);
    }

    pub fn delete_forward(&mut self, word: bool) {
        if !self.sel.is_collapsed() { self.checkpoint(EditKind::Delete); self.delete_selection(); return; }
        let p = self.sel.focus;
        let blk = &self.doc.blocks[p.b];
        let t = blk.text();
        if blk.kind.is_atomic() {
            self.checkpoint(EditKind::Other);
            self.doc.blocks.remove(p.b);
            self.doc.fix();
            self.sel = Sel::caret(self.doc.clamp(Pos::new(p.b, 0)));
            return;
        }
        if p.o < t.len() {
            self.checkpoint(EditKind::Delete);
            let mut e = next_char(&t, p.o);
            if word {
                while e < t.len() { let q = next_char(&t, e); if t[e..q].chars().all(|c| c.is_alphanumeric() || c == '_') { e = q; } else { break; } }
                while e < t.len() && t[e..].starts_with(' ') { e += 1; }
            }
            self.doc.delete(p, Pos::new(p.b, e));
            return;
        }
        if p.b + 1 >= self.doc.blocks.len() || blk.kind.is_cell() { return; }
        let next = &self.doc.blocks[p.b + 1];
        if next.kind.is_cell() { return; }
        let next_atomic = next.kind.is_atomic();
        self.checkpoint(EditKind::Other);
        if next_atomic { self.doc.blocks.remove(p.b + 1); self.doc.fix(); return; }
        self.doc.delete(p, Pos::new(p.b + 1, 0));
    }

    // ------------------------------------------------------------ formatting

    /// Apply `f` to the marks of the selection, or to the stored marks at a caret.
    fn map_selection_marks(&mut self, f: &dyn Fn(&mut Marks)) {
        if self.sel.is_collapsed() {
            let mut m = self.current_marks();
            f(&mut m);
            self.stored = Some(m);
            self.version += 1;
            return;
        }
        self.checkpoint(EditKind::Other);
        let (a, b) = (self.sel.start(), self.sel.end());
        for i in a.b..=b.b {
            let blk = &mut self.doc.blocks[i];
            if blk.kind.is_atomic() { continue; }
            let s = if i == a.b { a.o } else { 0 };
            let e = if i == b.b { b.o } else { blk.len() };
            blk.map_marks(s, e, f);
        }
    }

    pub fn toggle_mark(&mut self, mark: Mark) {
        let on = !mark.get(&self.current_marks());
        self.map_selection_marks(&move |m| mark.set(m, on));
    }

    pub fn set_color(&mut self, c: Option<String>) { self.map_selection_marks(&move |m| m.color = c.clone()); }
    pub fn set_highlight(&mut self, c: Option<String>) { self.map_selection_marks(&move |m| m.highlight = c.clone()); }
    pub fn set_size(&mut self, s: Option<u8>) { self.map_selection_marks(&move |m| m.size = s); }

    fn block_range(&self) -> std::ops::RangeInclusive<usize> { self.sel.start().b..=self.sel.end().b }

    /// Toggle a block type for every block in the selection.
    pub fn set_block(&mut self, kind: Kind) {
        self.checkpoint(EditKind::Other);
        let r = self.block_range();
        let all = self.doc.blocks[r.clone()].iter().filter(|b| !b.kind.is_atomic() && !b.kind.is_cell()).all(|b| b.kind == kind);
        for i in r {
            let b = &mut self.doc.blocks[i];
            if b.kind.is_atomic() || b.kind.is_cell() { continue; }
            if all { b.kind = Kind::Para; b.indent = 0; } else {
                if !kind.is_list() { b.indent = 0; }
                b.kind = kind.clone();
            }
        }
    }

    pub fn set_align(&mut self, a: Align) {
        self.checkpoint(EditKind::Other);
        for i in self.block_range() { self.doc.blocks[i].align = a; }
    }

    /// Indent (+1) or outdent (-1) list items; returns false if nothing to do.
    pub fn indent(&mut self, delta: i32) -> bool {
        let r = self.block_range();
        if !self.doc.blocks[r.clone()].iter().any(|b| b.kind.is_list()) { return false; }
        self.checkpoint(EditKind::Other);
        for i in r {
            let prev_level = if i > 0 && self.doc.blocks[i - 1].kind.is_list() { Some(self.doc.blocks[i - 1].indent) } else { None };
            let b = &mut self.doc.blocks[i];
            if !b.kind.is_list() { continue; }
            if delta > 0 {
                // only one level deeper than the previous item
                if prev_level.is_some_and(|l| b.indent <= l) && b.indent < 6 { b.indent += 1; }
            } else if b.indent > 0 { b.indent -= 1; } else { b.kind = Kind::Para; }
        }
        true
    }

    pub fn clear_format(&mut self) {
        self.checkpoint(EditKind::Other);
        if self.sel.is_collapsed() {
            self.stored = Some(Marks::default());
        } else {
            let (a, b) = (self.sel.start(), self.sel.end());
            for i in a.b..=b.b {
                let blk = &mut self.doc.blocks[i];
                if blk.kind.is_atomic() { continue; }
                let s = if i == a.b { a.o } else { 0 };
                let e = if i == b.b { b.o } else { blk.len() };
                blk.map_marks(s, e, &|m| { let link = m.link.take(); *m = Marks { link, ..Default::default() }; });
            }
        }
        for i in self.block_range() {
            let b = &mut self.doc.blocks[i];
            if matches!(b.kind, Kind::Heading(_) | Kind::Quote | Kind::Code) { b.kind = Kind::Para; }
            b.align = Align::Left;
        }
    }

    /// Byte range of the link around a collapsed caret.
    pub fn link_range(&self) -> Option<(usize, usize, String)> {
        let p = self.sel.focus;
        let blk = &self.doc.blocks[p.b];
        let mut spans: Vec<(usize, usize, String)> = vec![];
        let mut acc = 0;
        for r in &blk.runs {
            let (s, e) = (acc, acc + r.text.len());
            acc = e;
            let Some(l) = &r.marks.link else { continue };
            match spans.last_mut() {
                Some(last) if last.1 == s && &last.2 == l => last.1 = e,
                _ => spans.push((s, e, l.clone())),
            }
        }
        spans.into_iter().find(|(s, e, _)| *s < p.o && p.o <= *e || (*s == 0 && p.o == 0 && *e > 0))
    }

    pub fn current_link(&self) -> Option<String> {
        if self.sel.is_collapsed() { return self.link_range().map(|x| x.2); }
        self.current_marks().link
    }

    /// Set (or with an empty url, remove) a link on the selection / the link at the caret;
    /// with a collapsed caret outside a link, insert `text` (or the url) as a link.
    pub fn set_link(&mut self, url: &str, text: &str) {
        let url = url.trim();
        let link = if url.is_empty() { None } else {
            Some(if url.contains("://") || url.starts_with("mailto:") || url.starts_with("tel:") { url.to_string() }
                 else if url.contains('@') && !url.contains('/') { format!("mailto:{url}") } else { format!("https://{url}") })
        };
        if self.sel.is_collapsed() {
            if let Some((s, e, _)) = self.link_range() {
                let b = self.sel.focus.b;
                self.checkpoint(EditKind::Other);
                let blk = &mut self.doc.blocks[b];
                if !text.is_empty() && *text != blk.text()[s..e] {
                    let m = blk.marks_at(s + 1);
                    blk.cut(s, e);
                    blk.insert(s, text, &m);
                    let l = link.clone();
                    blk.map_marks(s, s + text.len(), &move |m| m.link = l.clone());
                    self.sel = Sel::caret(Pos::new(b, s + text.len()));
                } else {
                    let l = link.clone();
                    blk.map_marks(s, e, &move |m| m.link = l.clone());
                }
                return;
            }
            let Some(l) = link else { return };
            let t = if text.is_empty() { l.trim_start_matches("mailto:").to_string() } else { text.to_string() };
            self.checkpoint(EditKind::Other);
            let mut m = self.current_marks();
            m.link = Some(l);
            let p = self.doc.insert_text(self.sel.focus, &t, &m);
            self.sel = Sel::caret(p);
            let mut after = m.clone();
            after.link = None;
            self.stored = Some(after);
            return;
        }
        self.map_selection_marks(&move |m| m.link = link.clone());
    }

    // ------------------------------------------------------------ blocks

    pub fn insert_blocks(&mut self, blocks: Vec<Block>) {
        if blocks.is_empty() { return; }
        self.checkpoint(EditKind::Other);
        self.delete_selection();
        let p = self.doc.insert_blocks(self.sel.focus, blocks);
        self.sel = Sel::caret(p);
    }

    pub fn insert_image(&mut self, img: ImageData) {
        self.checkpoint(EditKind::Other);
        self.delete_selection();
        let p = self.sel.focus;
        let at = if self.doc.blocks[p.b].kind.is_cell() {
            // images go after the table
            self.doc.table_range(p.b).map(|r| r.end).unwrap_or(p.b + 1)
        } else if self.doc.blocks[p.b].is_empty() && !self.doc.blocks[p.b].kind.is_atomic() {
            self.doc.blocks.remove(p.b);
            p.b
        } else if p.o == 0 && !self.doc.blocks[p.b].kind.is_atomic() { p.b } else {
            let np = self.doc.split(p);
            np.b
        };
        self.doc.blocks.insert(at, Block::new(Kind::Image(img)));
        self.doc.fix();
        self.sel = Sel::caret(self.doc.clamp(Pos::new(at + 1, 0)));
    }

    pub fn insert_rule(&mut self) {
        self.checkpoint(EditKind::Other);
        let p = self.sel.focus;
        let np = if self.doc.blocks[p.b].kind.is_cell() { Pos::new(self.doc.table_range(p.b).map(|r| r.end).unwrap_or(p.b + 1), 0) } else { self.doc.split(p) };
        self.doc.blocks.insert(np.b, Block::new(Kind::Rule));
        self.doc.fix();
        self.sel = Sel::caret(Pos::new(np.b + 1, 0));
    }

    pub fn insert_table(&mut self, rows: u32, cols: u32) {
        self.checkpoint(EditKind::Other);
        self.delete_selection();
        let p = self.sel.focus;
        if self.doc.blocks[p.b].kind.is_cell() { return; }
        let at = if self.doc.blocks[p.b].is_empty() && !self.doc.blocks[p.b].kind.is_atomic() { self.doc.blocks.remove(p.b); p.b } else { self.doc.split(p).b };
        let t = self.doc.new_table_id();
        let mut cells = vec![];
        for r in 0..rows { for c in 0..cols { cells.push(Block::new(Kind::Cell { table: t, row: r, col: c, header: r == 0 })); } }
        let n = cells.len();
        for (k, c) in cells.into_iter().enumerate() { self.doc.blocks.insert(at + k, c); }
        if at + n >= self.doc.blocks.len() || self.doc.blocks[at + n].kind.is_atomic() { self.doc.blocks.insert(at + n, Block::new(Kind::Para)); }
        self.doc.fix();
        self.sel = Sel::caret(Pos::new(at, 0));
    }

    fn cell_pos(&self) -> Option<(std::ops::Range<usize>, u32, u32)> {
        let b = self.sel.focus.b;
        let r = self.doc.table_range(b)?;
        if let Kind::Cell { row, col, .. } = self.doc.blocks[b].kind { Some((r, row, col)) } else { None }
    }

    fn focus_cell(&mut self, start: usize, row: u32, col: u32) {
        let r = self.doc.table_range(start).unwrap_or(start..start + 1);
        let (_, cols) = self.doc.table_dims(&r);
        let i = (start + (row * cols + col) as usize).min(r.end - 1);
        self.sel = Sel::caret(Pos::new(i, self.doc.blocks[i].len()));
    }

    /// Table edits: "row-above", "row-below", "col-left", "col-right", "del-row", "del-col", "del-table", "header".
    pub fn table_op(&mut self, op: &str) -> bool {
        let Some((r, row, col)) = self.cell_pos() else { return false };
        self.checkpoint(EditKind::Other);
        let mut grid = self.doc.table_grid(&r);
        let cols = grid.first().map(|x| x.len()).unwrap_or(0);
        let blank = || Block::new(Kind::Cell { table: 0, row: 0, col: 0, header: false });
        let (mut nr, mut nc) = (row, col);
        match op {
            "row-above" => { grid.insert(row as usize, vec![blank(); cols]); }
            "row-below" => { grid.insert(row as usize + 1, vec![blank(); cols]); nr += 1; }
            "col-left" => { for (ri, rw) in grid.iter_mut().enumerate() { let mut c = blank(); if ri == 0 && matches!(rw[0].kind, Kind::Cell { header: true, .. }) { c.kind = Kind::Cell { table: 0, row: 0, col: 0, header: true }; } rw.insert(col as usize, c); } }
            "col-right" => { for (ri, rw) in grid.iter_mut().enumerate() { let mut c = blank(); if ri == 0 && matches!(rw[0].kind, Kind::Cell { header: true, .. }) { c.kind = Kind::Cell { table: 0, row: 0, col: 0, header: true }; } rw.insert(col as usize + 1, c); } nc += 1; }
            "del-row" => { grid.remove(row as usize); nr = nr.min(grid.len().saturating_sub(1) as u32); }
            "del-col" => { for rw in grid.iter_mut() { rw.remove(col as usize); } nc = nc.min(cols.saturating_sub(2) as u32); }
            "header" => {
                let on = !matches!(grid[0][0].kind, Kind::Cell { header: true, .. });
                for c in grid[0].iter_mut() { c.kind = Kind::Cell { table: 0, row: 0, col: 0, header: on }; }
            }
            "del-table" => { grid.clear(); }
            _ => return false,
        }
        if grid.is_empty() || grid[0].is_empty() {
            let start = r.start;
            self.doc.blocks.drain(r);
            if start >= self.doc.blocks.len() || self.doc.blocks[start].kind.is_cell() { self.doc.blocks.insert(start.min(self.doc.blocks.len()), Block::new(Kind::Para)); }
            self.doc.fix();
            self.sel = Sel::caret(self.doc.clamp(Pos::new(start, 0)));
            return true;
        }
        let start = self.doc.set_table(r, grid);
        self.focus_cell(start, nr, nc);
        true
    }

    /// Tab / Shift+Tab inside a table: move between cells (adding a row at the end).
    pub fn cell_tab(&mut self, back: bool) -> bool {
        let Some((r, row, col)) = self.cell_pos() else { return false };
        let (rows, cols) = self.doc.table_dims(&r);
        let idx = row * cols + col;
        if back {
            if idx == 0 { return true; }
            let i = r.start + idx as usize - 1;
            self.select(Pos::new(i, 0), Pos::new(i, self.doc.blocks[i].len()));
            return true;
        }
        if idx + 1 >= rows * cols {
            self.table_op("row-below");
            let i = self.sel.focus.b - (self.sel.focus.b - self.doc.table_range(self.sel.focus.b).unwrap().start) % cols as usize;
            self.sel = Sel::caret(Pos::new(i, 0));
            return true;
        }
        let i = r.start + idx as usize + 1;
        self.select(Pos::new(i, 0), Pos::new(i, self.doc.blocks[i].len()));
        true
    }

    /// Replace the selection (or the whole editable body if nothing is selected) with plain text.
    /// The quoted original and anything after it are kept.
    pub fn replace_text(&mut self, text: &str, whole: bool) {
        self.checkpoint(EditKind::Other);
        let blocks: Vec<Block> = text.trim_end().split('\n').map(Block::para).collect();
        if whole {
            let end = self.doc.blocks.iter().position(|b| matches!(b.kind, Kind::Quoted { .. })).unwrap_or(self.doc.blocks.len());
            // keep a reply header line ("On … wrote:") right before the quote
            let end = if end < self.doc.blocks.len() && end > 0 && self.doc.blocks[end - 1].text().trim_end().ends_with(':') { end - 1 } else { end };
            let tail: Vec<Block> = self.doc.blocks.drain(end..).collect();
            let n = blocks.len();
            self.doc.blocks = blocks;
            if !tail.is_empty() { self.doc.blocks.push(Block::new(Kind::Para)); }
            self.doc.blocks.extend(tail);
            self.doc.fix();
            let last = n - 1;
            self.sel = Sel::caret(Pos::new(last, self.doc.blocks[last].len()));
            return;
        }
        self.delete_selection();
        let p = self.doc.insert_blocks(self.sel.focus, blocks);
        self.sel = Sel::caret(p);
    }

    pub fn toggle_quoted(&mut self, b: usize) {
        if let Some(Kind::Quoted { open, .. }) = self.doc.blocks.get_mut(b).map(|x| &mut x.kind) { *open = !*open; self.version += 1; }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn ed(text: &str) -> Editor {
        let mut e = Editor::new(Doc::from_text(text));
        e.set_caret(e.doc.end());
        e
    }
    fn texts(e: &Editor) -> Vec<String> { e.doc.blocks.iter().map(|b| b.text()).collect() }
    fn typ(e: &mut Editor, s: &str) { for c in s.chars() { e.insert_text(&c.to_string()); } }

    #[test]
    fn typing_and_undo_groups() {
        let mut e = ed("");
        typ(&mut e, "hello");
        e.enter(false);
        typ(&mut e, "world");
        assert_eq!(texts(&e), vec!["hello", "world"]);
        assert!(e.undo());
        assert_eq!(texts(&e), vec!["hello", ""]);
        assert!(e.undo());
        assert_eq!(texts(&e), vec!["hello"]);
        assert!(e.undo());
        assert_eq!(texts(&e), vec![""]);
        assert!(e.redo());
        assert!(e.redo());
        assert_eq!(texts(&e), vec!["hello", ""]);
    }

    #[test]
    fn markdown_block_rules() {
        let mut e = ed("");
        typ(&mut e, "# Title");
        assert_eq!(e.doc.blocks[0].kind, Kind::Heading(1));
        assert_eq!(texts(&e), vec!["Title"]);
        e.enter(false);
        typ(&mut e, "- a");
        assert_eq!(e.doc.blocks[1].kind, Kind::Bullet);
        e.enter(false);
        assert_eq!(e.doc.blocks[2].kind, Kind::Bullet);
        e.enter(false); // empty item leaves the list
        assert_eq!(e.doc.blocks[2].kind, Kind::Para);
        typ(&mut e, "1. x");
        assert_eq!(e.doc.blocks[2].kind, Kind::Ordered);
        e.undo();
        e.undo();
        assert_eq!(e.doc.blocks[2].kind, Kind::Para);
        assert_eq!(e.doc.blocks[2].text(), "1. ");
    }

    #[test]
    fn markdown_inline_rules() {
        let mut e = ed("");
        typ(&mut e, "a **bold** and *it* `c`");
        let b = &e.doc.blocks[0];
        assert_eq!(b.text(), "a bold and it c");
        assert!(b.runs.iter().any(|r| r.text == "bold" && r.marks.bold));
        assert!(b.runs.iter().any(|r| r.text == "it" && r.marks.italic && !r.marks.bold));
        assert!(b.runs.iter().any(|r| r.text == "c" && r.marks.code));
        assert!(b.runs.iter().any(|r| r.text == " and " && r.marks.is_plain()));
    }

    #[test]
    fn toggle_marks_selection_and_caret() {
        let mut e = ed("hello world");
        e.select(Pos::new(0, 0), Pos::new(0, 5));
        e.toggle_mark(Mark::Bold);
        assert!(e.doc.blocks[0].runs[0].marks.bold);
        assert!(e.current_marks().bold);
        e.toggle_mark(Mark::Bold);
        assert_eq!(e.doc.blocks[0].runs.len(), 1);
        e.set_caret(Pos::new(0, 11));
        e.toggle_mark(Mark::Italic);
        typ(&mut e, "!");
        assert!(e.doc.blocks[0].runs.last().unwrap().marks.italic);
    }

    #[test]
    fn lists_indent_and_backspace() {
        let mut e = ed("");
        typ(&mut e, "- a");
        e.enter(false);
        typ(&mut e, "b");
        assert!(e.indent(1));
        assert_eq!(e.doc.blocks[1].indent, 1);
        e.set_caret(Pos::new(1, 0));
        e.backspace(false);
        assert_eq!(e.doc.blocks[1].indent, 0);
        e.backspace(false);
        assert_eq!(e.doc.blocks[1].kind, Kind::Para);
        e.backspace(false);
        assert_eq!(texts(&e), vec!["ab"]);
    }

    #[test]
    fn links() {
        let mut e = ed("see site");
        e.select(Pos::new(0, 4), Pos::new(0, 8));
        e.set_link("example.com", "");
        assert_eq!(e.doc.blocks[0].runs[1].marks.link.as_deref(), Some("https://example.com"));
        e.set_caret(Pos::new(0, 6));
        assert_eq!(e.current_link().as_deref(), Some("https://example.com"));
        e.set_link("", "");
        assert!(e.doc.blocks[0].runs.iter().all(|r| r.marks.link.is_none()));
    }

    #[test]
    fn tables() {
        let mut e = ed("");
        e.insert_table(2, 2);
        assert_eq!(e.doc.blocks.iter().filter(|b| b.kind.is_cell()).count(), 4);
        typ(&mut e, "a");
        assert!(e.cell_tab(false));
        typ(&mut e, "b");
        e.table_op("col-right");
        assert_eq!(e.doc.blocks.iter().filter(|b| b.kind.is_cell()).count(), 6);
        e.table_op("del-row");
        assert_eq!(e.doc.blocks.iter().filter(|b| b.kind.is_cell()).count(), 3);
        e.table_op("del-table");
        assert!(e.doc.blocks.iter().all(|b| !b.kind.is_cell()));
    }

    #[test]
    fn replace_keeps_quote() {
        let mut e = ed("draft");
        e.doc.blocks.push(Block::para("On Mon, X wrote:"));
        e.doc.blocks.push(Block::new(Kind::Quoted { html: "<p>q</p>".into(), text: "q".into(), open: false, cite: true }));
        e.doc.fix();
        e.replace_text("better draft", true);
        assert_eq!(e.doc.blocks[0].text(), "better draft");
        assert!(e.doc.blocks.iter().any(|b| matches!(b.kind, Kind::Quoted { .. })));
        assert!(e.doc.blocks.iter().any(|b| b.text() == "On Mon, X wrote:"));
    }
}
