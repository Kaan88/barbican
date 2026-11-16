//! Layout and painting of the document with parley (the same text engine and system
//! font collection Slint uses, so measured runs match what Slint draws), plus
//! hit-testing and caret movement that need line geometry.
use super::model::*;
use parley::{Affinity, Alignment, AlignmentOptions, Cursor, FontContext, FontFamily, FontStyle, FontWeight, Layout, LayoutContext, LineHeight, Selection, StyleProperty};
use std::cell::RefCell;
use std::collections::HashMap;
use std::hash::{Hash, Hasher};
use std::rc::Rc;

thread_local! {
    static CTX: RefCell<(FontContext, LayoutContext<u32>)> = RefCell::new((FontContext::new(), LayoutContext::new()));
}

pub const BODY_FONT: &str = "Segoe UI";
pub const MONO_FONT: &str = "Consolas";

/// Colour roles resolved against the Slint theme (so light/dark switch live).
#[derive(Clone, Copy, PartialEq, Debug)]
pub enum Role { Rgb(u32), Text, Dim, Line, Selection, CodeBg, Link, HeaderBg, Accent, None }

impl Role {
    pub fn code(self) -> (i32, u32) {
        match self {
            Role::Rgb(c) => (0, c), Role::Text => (1, 0), Role::Dim => (2, 0), Role::Line => (3, 0), Role::Selection => (4, 0),
            Role::CodeBg => (5, 0), Role::Link => (6, 0), Role::HeaderBg => (7, 0), Role::Accent => (8, 0), Role::None => (9, 0),
        }
    }
}

pub fn parse_rgb(s: &str) -> Option<u32> {
    let h = s.strip_prefix('#')?;
    if h.len() == 6 { u32::from_str_radix(h, 16).ok() } else { None }
}

#[derive(Clone, Debug)]
pub struct PText { pub x: f32, pub y: f32, pub w: f32, pub h: f32, pub text: String, pub role: Role, pub size: f32, pub weight: i32, pub italic: bool, pub font: i32, pub right: bool }
#[derive(Clone, Debug)]
pub struct PBox { pub x: f32, pub y: f32, pub w: f32, pub h: f32, pub role: Role, pub border: f32, pub border_role: Role, pub radius: f32 }
#[derive(Clone, Debug)]
pub struct PImg { pub x: f32, pub y: f32, pub w: f32, pub h: f32, pub key: String, pub selected: bool, pub label: String }

#[derive(Default)]
pub struct Painted {
    pub texts: Vec<PText>,
    pub back: Vec<PBox>,
    pub front: Vec<PBox>,
    pub imgs: Vec<PImg>,
    pub caret: (f32, f32, f32),
    pub height: f32,
    /// Bounding box of the selection's first line (for the floating toolbar), if any.
    pub sel_top: Option<(f32, f32)>,
}

#[derive(Clone)]
pub struct Geo {
    pub x: f32,
    pub y: f32,
    pub w: f32,
    pub h: f32,
    /// Origin of the text layout.
    pub tx: f32,
    pub ty: f32,
    pub layout: Option<Rc<Layout<u32>>>,
    /// Layout made for an empty block (one placeholder space).
    pub empty: bool,
    /// Header strip of a quoted block (y, h) — clicking it toggles the quote.
    pub header: Option<(f32, f32)>,
    pub img: Option<(f32, f32, f32, f32)>,
}

pub struct View {
    pub geo: Vec<Geo>,
    pub width: f32,
    pub base: f32,
    pub height: f32,
    cache: HashMap<u64, Rc<Layout<u32>>>,
}

impl Default for View {
    fn default() -> Self { View { geo: vec![], width: 600.0, base: 14.0, height: 0.0, cache: HashMap::new() } }
}

fn heading_scale(l: u8) -> f32 { match l { 1 => 1.75, 2 => 1.4, _ => 1.18 } }

/// Natural size of an image, if known (provided by the UI layer's image cache).
pub type ImgDims<'a> = &'a dyn Fn(&ImageData) -> Option<(u32, u32)>;

fn build_text_layout(text: &str, runs: &[Run], kind: &Kind, align: Align, width: f32, base: f32, dim: bool) -> Layout<u32> {
    CTX.with(|c| {
        let (fcx, lcx) = &mut *c.borrow_mut();
        let empty = text.is_empty();
        let t = if empty { " " } else { text };
        let mut b = lcx.ranged_builder(fcx, t, 1.0, true);
        let (size, weight, mono, lh) = match kind {
            Kind::Heading(l) => (base * heading_scale(*l), 700.0, false, 1.3),
            Kind::Cell { header: true, .. } => (base, 700.0, false, 1.5),
            Kind::Code => (base * 0.93, 400.0, true, 1.45),
            _ => (base, 400.0, false, 1.5),
        };
        let _ = dim;
        b.push_default(StyleProperty::FontFamily(FontFamily::named(if mono { MONO_FONT } else { BODY_FONT })));
        b.push_default(StyleProperty::FontSize(size));
        b.push_default(StyleProperty::FontWeight(FontWeight::new(weight)));
        b.push_default(StyleProperty::LineHeight(LineHeight::FontSizeRelative(lh)));
        b.push_default(StyleProperty::Brush(0u32));
        if !empty {
            let mut acc = 0;
            for (i, r) in runs.iter().enumerate() {
                let rg = acc..acc + r.text.len();
                acc = rg.end;
                b.push(StyleProperty::Brush(i as u32), rg.clone());
                let m = &r.marks;
                if m.bold { b.push(StyleProperty::FontWeight(FontWeight::BOLD), rg.clone()); }
                if m.italic { b.push(StyleProperty::FontStyle(FontStyle::Italic), rg.clone()); }
                if m.code && !mono {
                    b.push(StyleProperty::FontFamily(FontFamily::named(MONO_FONT)), rg.clone());
                    b.push(StyleProperty::FontSize(m.size.map(|s| s as f32).unwrap_or(size) * 0.92), rg.clone());
                } else if let Some(s) = m.size {
                    let s = if matches!(kind, Kind::Heading(_)) { s as f32 * heading_scale(match kind { Kind::Heading(l) => *l, _ => 3 }) } else { s as f32 };
                    b.push(StyleProperty::FontSize(s), rg.clone());
                }
            }
        }
        let mut layout = b.build(t);
        layout.break_all_lines(Some(width.max(10.0)));
        let a = match align { Align::Left => Alignment::Start, Align::Center => Alignment::Center, Align::Right => Alignment::End, Align::Justify => Alignment::Justify };
        layout.align(a, AlignmentOptions::default());
        // justify must not stretch a paragraph's last line
        layout
    })
}

impl View {
    fn text_layout(&mut self, blk: &Block, width: f32, dim: bool) -> Rc<Layout<u32>> {
        let mut h = std::collections::hash_map::DefaultHasher::new();
        blk.runs.hash(&mut h);
        std::mem::discriminant(&blk.kind).hash(&mut h);
        if let Kind::Heading(l) = blk.kind { l.hash(&mut h); }
        blk.align.hash(&mut h);
        width.to_bits().hash(&mut h);
        self.base.to_bits().hash(&mut h);
        dim.hash(&mut h);
        let key = h.finish();
        if let Some(l) = self.cache.get(&key) { return l.clone(); }
        let l = Rc::new(build_text_layout(&blk.text(), &blk.runs, &blk.kind, blk.align, width, self.base, dim));
        self.cache.insert(key, l.clone());
        l
    }

    /// Compute block geometry for the document at the given width.
    pub fn build(&mut self, doc: &Doc, width: f32, base: f32, dims: ImgDims) {
        if (self.width - width).abs() > 0.5 || (self.base - base).abs() > 0.01 || self.cache.len() > 4000 { self.cache.clear(); }
        self.width = width;
        self.base = base;
        let w = width.max(60.0);
        let mut geo: Vec<Geo> = Vec::with_capacity(doc.blocks.len());
        let mut y = 0.0f32;
        let bl = &doc.blocks;
        let mut i = 0;
        while i < bl.len() {
            let b = &bl[i];
            let prev = if i > 0 { Some(&bl[i - 1].kind) } else { None };
            let next = bl.get(i + 1).map(|x| &x.kind);
            match &b.kind {
                Kind::Cell { .. } => {
                    let r = doc.table_range(i).unwrap_or(i..i + 1);
                    let (rows, cols) = doc.table_dims(&r);
                    y += if i > 0 { 8.0 } else { 2.0 };
                    let cw = (w / cols.max(1) as f32).floor();
                    let mut cells: Vec<Geo> = Vec::with_capacity(r.len());
                    let mut row_h = vec![0.0f32; rows as usize];
                    for k in r.clone() {
                        if let Kind::Cell { row, col, .. } = bl[k].kind {
                            let l = self.text_layout(&bl[k], cw - 20.0, false);
                            row_h[row as usize] = row_h[row as usize].max(l.height() + 12.0);
                            cells.push(Geo { x: col as f32 * cw, y: 0.0, w: cw, h: 0.0, tx: col as f32 * cw + 10.0, ty: 6.0, empty: bl[k].is_empty(), layout: Some(l), header: None, img: None });
                        }
                    }
                    let mut row_y = vec![y; rows as usize];
                    for rr in 1..rows as usize { row_y[rr] = row_y[rr - 1] + row_h[rr - 1]; }
                    for (k, mut g) in cells.into_iter().enumerate() {
                        if let Kind::Cell { row, .. } = bl[r.start + k].kind {
                            g.y = row_y[row as usize];
                            g.h = row_h[row as usize];
                            g.ty += g.y;
                        }
                        geo.push(g);
                    }
                    y = row_y.last().copied().unwrap_or(y) + row_h.last().copied().unwrap_or(0.0) + 8.0;
                    i = r.end;
                    continue;
                }
                Kind::Image(img) => {
                    y += 4.0;
                    let (nw, nh) = dims(img).unwrap_or((0, 0));
                    let (mut iw, mut ih) = if img.width > 0 {
                        let h = if img.height > 0 { img.height as f32 } else if nw > 0 { img.width as f32 * nh as f32 / nw as f32 } else { 120.0 };
                        (img.width as f32, h)
                    } else if nw > 0 { (nw as f32, nh as f32) } else { (220.0, 56.0) };
                    if iw > w { ih *= w / iw; iw = w; }
                    let ix = match b.align { Align::Center => (w - iw) / 2.0, Align::Right => w - iw, _ => 0.0 };
                    geo.push(Geo { x: 0.0, y, w, h: ih, tx: ix, ty: y, layout: None, empty: false, header: None, img: Some((ix, y, iw, ih)) });
                    y += ih + 4.0;
                }
                Kind::Rule => {
                    geo.push(Geo { x: 0.0, y, w, h: 17.0, tx: 0.0, ty: y, layout: None, empty: false, header: None, img: None });
                    y += 17.0;
                }
                Kind::Quoted { text, open, cite, .. } => {
                    y += 6.0;
                    let hdr = 26.0;
                    let mut g = Geo { x: 0.0, y, w, h: hdr, tx: 0.0, ty: y, layout: None, empty: false, header: Some((y, hdr)), img: None };
                    if *open {
                        let inset = if *cite { 14.0 } else { 0.0 };
                        let blk = Block::para(text);
                        let l = self.text_layout(&blk, w - inset, true);
                        g.tx = inset;
                        g.ty = y + hdr + 6.0;
                        g.h = hdr + 6.0 + l.height();
                        g.layout = Some(l);
                    }
                    y += g.h + 4.0;
                    geo.push(g);
                }
                k => {
                    let (inset, pad_top, pad_bot, gap) = match k {
                        Kind::Heading(l) => (0.0, 0.0, 0.0, if i == 0 { 0.0 } else { match l { 1 => 14.0, 2 => 12.0, _ => 8.0 } }),
                        Kind::Quote => (16.0, 0.0, 0.0, if prev == Some(&Kind::Quote) { 0.0 } else { 4.0 }),
                        Kind::Code => (12.0, if prev == Some(&Kind::Code) { 0.0 } else { 8.0 }, if next == Some(&Kind::Code) { 0.0 } else { 8.0 }, if prev == Some(&Kind::Code) { 0.0 } else { 6.0 }),
                        Kind::Bullet | Kind::Ordered => (24.0 + 22.0 * b.indent as f32, 0.0, 0.0, if prev.is_some_and(|p| p.is_list()) { 1.0 } else { 2.0 }),
                        _ => (0.0, 0.0, 0.0, if prev.is_some_and(|p| matches!(p, Kind::Heading(_))) { 2.0 } else if prev == Some(&Kind::Quote) || prev == Some(&Kind::Code) { 4.0 } else { 0.0 }),
                    };
                    y += gap;
                    let tw = w - inset - if *k == Kind::Code { 12.0 } else { 0.0 };
                    let l = self.text_layout(b, tw, false);
                    let h = pad_top + l.height() + pad_bot;
                    geo.push(Geo { x: 0.0, y, w, h, tx: inset, ty: y + pad_top, layout: Some(l), empty: b.is_empty(), header: None, img: None });
                    y += h;
                }
            }
            i += 1;
        }
        self.geo = geo;
        self.height = y;
    }

    // ------------------------------------------------------------ hit testing

    /// Block index at a point (nearest block vertically; cells also by column).
    pub fn block_at(&self, x: f32, y: f32) -> usize {
        let mut best = 0;
        let mut best_d = f32::MAX;
        for (i, g) in self.geo.iter().enumerate() {
            let dy = if y < g.y { g.y - y } else if y > g.y + g.h { y - g.y - g.h } else { 0.0 };
            let dx = if x < g.x { g.x - x } else if x > g.x + g.w { x - g.x - g.w } else { 0.0 };
            let d = dy * 1000.0 + dx;
            if d < best_d { best_d = d; best = i; }
        }
        best
    }

    pub fn hit(&self, doc: &Doc, x: f32, y: f32) -> (Pos, bool) {
        if self.geo.is_empty() { return (Pos::default(), false); }
        let b = self.block_at(x, y);
        let g = &self.geo[b];
        if doc.blocks[b].kind.is_atomic() { return (Pos::new(b, 0), false); }
        let Some(l) = &g.layout else { return (Pos::new(b, 0), false) };
        if g.empty { return (Pos::new(b, 0), false); }
        let c = Cursor::from_point(l, x - g.tx, (y - g.ty).clamp(0.0, (l.height() - 1.0).max(0.0)));
        (Pos::new(b, c.index().min(doc.blocks[b].len())), c.affinity() == Affinity::Upstream)
    }

    fn cursor(&self, doc: &Doc, p: Pos, up: bool) -> Option<(Rc<Layout<u32>>, Cursor)> {
        let g = self.geo.get(p.b)?;
        let l = g.layout.clone()?;
        if doc.blocks[p.b].kind.is_atomic() { return None; }
        let o = if g.empty { 0 } else { p.o };
        let c = Cursor::from_byte_index(&l, o, if up { Affinity::Upstream } else { Affinity::Downstream });
        Some((l, c))
    }

    /// Caret rectangle (x, y, h) in content coordinates.
    pub fn caret_rect(&self, doc: &Doc, p: Pos, up: bool) -> (f32, f32, f32) {
        let Some(g) = self.geo.get(p.b) else { return (0.0, 0.0, 18.0) };
        match self.cursor(doc, p, up) {
            Some((l, c)) => {
                let bb = c.geometry(&l, 1.5);
                (g.tx + bb.x0 as f32, g.ty + bb.y0 as f32, (bb.y1 - bb.y0) as f32)
            }
            None => match g.img { Some((ix, iy, _, ih)) => (ix - 3.0, iy, ih), None => (g.x, g.y, g.h.min(26.0)) },
        }
    }

    /// Caret movement inside the layout: "home", "end", "word-left", "word-right".
    pub fn move_in_line(&self, doc: &Doc, p: Pos, up: bool, how: &str) -> Option<(Pos, bool)> {
        let (l, c) = self.cursor(doc, p, up)?;
        let len = doc.blocks[p.b].len();
        let s = Selection::from(c);
        let (idx, aff) = match how {
            "home" => { let f = s.line_start(&l, false).focus(); (f.index(), f.affinity()) }
            "end" => { let f = s.line_end(&l, false).focus(); (f.index(), f.affinity()) }
            "word-left" => { let f = c.previous_logical_word(&l); (f.index(), f.affinity()) }
            "word-right" => { let f = c.next_logical_word(&l); (f.index(), f.affinity()) }
            _ => return None,
        };
        let idx = if self.geo[p.b].empty { 0 } else { idx.min(len) };
        // the hard line break at the end of a line is not a caret stop for End
        let t = doc.blocks[p.b].text();
        let idx = if how == "end" && idx > 0 && t[..idx].ends_with('\n') { idx - 1 } else { idx };
        Some((Pos::new(p.b, idx), aff == Affinity::Upstream))
    }

    /// Vertical caret movement by `dir` lines (+1 down, -1 up) keeping the goal x.
    pub fn move_vertical(&self, doc: &Doc, p: Pos, up: bool, dir: i32, goal_x: f32) -> (Pos, bool) {
        let (cx, cy, ch) = self.caret_rect(doc, p, up);
        let _ = cx;
        let g = &self.geo[p.b];
        // inside the same block?
        if let Some(l) = &g.layout {
            if !doc.blocks[p.b].kind.is_atomic() {
                let ly = cy - g.ty;
                let target_y = if dir > 0 { ly + ch + 1.0 } else { ly - 1.0 };
                if target_y >= 0.0 && target_y < l.height() - 0.5 && !g.empty {
                    let c = Cursor::from_point(l, goal_x - g.tx, target_y);
                    return (Pos::new(p.b, c.index().min(doc.blocks[p.b].len())), c.affinity() == Affinity::Upstream);
                }
            }
        }
        // next/previous block (cells move by row in the same column)
        let target = if let Kind::Cell { row, col, .. } = doc.blocks[p.b].kind {
            let r = doc.table_range(p.b).unwrap();
            let (rows, cols) = doc.table_dims(&r);
            let nr = row as i32 + dir;
            if nr < 0 { r.start.checked_sub(1) } else if nr >= rows as i32 { (r.end < doc.blocks.len()).then_some(r.end) } else { Some(r.start + (nr as u32 * cols + col) as usize) }
        } else if dir > 0 { (p.b + 1 < doc.blocks.len()).then_some(p.b + 1) } else { p.b.checked_sub(1) };
        let Some(t) = target else {
            return if dir > 0 { (Pos::new(p.b, doc.blocks[p.b].len()), false) } else { (Pos::new(p.b, 0), false) };
        };
        // entering a table from below/above: pick the cell in the goal column
        let t = if doc.blocks[t].kind.is_cell() && !doc.blocks[p.b].kind.is_cell() {
            let r = doc.table_range(t).unwrap();
            let (rows, cols) = doc.table_dims(&r);
            let cw = self.width / cols.max(1) as f32;
            let col = ((goal_x / cw) as u32).min(cols - 1);
            let row = if dir > 0 { 0 } else { rows - 1 };
            r.start + (row * cols + col) as usize
        } else { t };
        let tg = &self.geo[t];
        if doc.blocks[t].kind.is_atomic() { return (Pos::new(t, 0), false); }
        let Some(l) = &tg.layout else { return (Pos::new(t, 0), false) };
        if tg.empty { return (Pos::new(t, 0), false); }
        let ly = if dir > 0 { 1.0 } else { l.height() - 1.0 };
        let c = Cursor::from_point(l, goal_x - tg.tx, ly);
        (Pos::new(t, c.index().min(doc.blocks[t].len())), c.affinity() == Affinity::Upstream)
    }

    // ------------------------------------------------------------ painting

    pub fn paint(&self, doc: &Doc, sel: &Sel, up: bool, base: f32) -> Painted {
        let mut p = Painted { height: self.height, ..Default::default() };
        let (ss, se) = (sel.start(), sel.end());
        let has_sel = !sel.is_collapsed();
        let mut i = 0;
        while i < doc.blocks.len() && i < self.geo.len() {
            let b = &doc.blocks[i];
            let g = &self.geo[i];
            let in_sel = has_sel && i >= ss.b && i <= se.b;
            match &b.kind {
                Kind::Quote => p.back.push(PBox { x: 2.0, y: g.y, w: 3.0, h: g.h, role: Role::Line, border: 0.0, border_role: Role::Line, radius: 1.5 }),
                Kind::Code => p.back.push(PBox { x: 0.0, y: g.y, w: g.w, h: g.h, role: Role::CodeBg, border: 0.0, border_role: Role::Line,
                    radius: if i > 0 && doc.blocks[i - 1].kind == Kind::Code || doc.blocks.get(i + 1).is_some_and(|n| n.kind == Kind::Code) { 0.0 } else { 6.0 } }),
                Kind::Cell { header, .. } => {
                    p.back.push(PBox { x: g.x, y: g.y, w: g.w + 1.0, h: g.h + 1.0, role: if *header { Role::HeaderBg } else { Role::None }, border: 1.0, border_role: Role::Line, radius: 0.0 });
                }
                Kind::Bullet | Kind::Ordered => {
                    let marker = if b.kind == Kind::Ordered {
                        let n = doc.ordinal(i);
                        match b.indent % 3 { 1 => format!("{}.", alpha(n)), 2 => format!("{}.", roman(n)), _ => format!("{n}.") }
                    } else { ["•", "◦", "▪"][b.indent as usize % 3].to_string() };
                    if let Some(l) = &g.layout {
                        if let Some(line) = l.lines().next() {
                            let m = line.metrics();
                            let size = base;
                            p.texts.push(PText { x: g.tx - 40.0, y: g.ty + m.baseline - size * 1.07, w: 34.0, h: size * 1.4, text: marker, role: Role::Text, size, weight: 400, italic: false, font: 0, right: true });
                        }
                    }
                }
                Kind::Rule => p.back.push(PBox { x: 0.0, y: g.y + 8.0, w: g.w, h: 1.0, role: Role::Line, border: 0.0, border_role: Role::Line, radius: 0.0 }),
                Kind::Image(img) => {
                    let (ix, iy, iw, ih) = g.img.unwrap();
                    let selected = (has_sel && in_sel) || (!has_sel && sel.focus.b == i);
                    p.imgs.push(PImg { x: ix, y: iy, w: iw, h: ih, key: super::image_key(img), selected, label: if img.alt.is_empty() { img.name.clone() } else { img.alt.clone() } });
                }
                Kind::Quoted { open, cite, .. } => {
                    let (hy, hh) = g.header.unwrap();
                    let label = if *open { if *cite { "Hide quoted text" } else { "Hide forwarded message" } } else if *cite { "Show quoted text" } else { "Show forwarded message" };
                    let selected = (has_sel && in_sel) || (!has_sel && sel.focus.b == i);
                    p.back.push(PBox { x: 0.0, y: hy, w: 180.0, h: hh, role: Role::CodeBg, border: 1.0, border_role: if selected { Role::Accent } else { Role::Line }, radius: 6.0 });
                    p.texts.push(PText { x: 10.0, y: hy + 7.0, w: 14.0, h: 14.0, text: if *open { "\u{E70D}" } else { "\u{E76C}" }.into(), role: Role::Dim, size: 10.0, weight: 400, italic: false, font: 2, right: false });
                    p.texts.push(PText { x: 28.0, y: hy + 4.0, w: 150.0, h: 18.0, text: label.into(), role: Role::Dim, size: base * 0.9, weight: 500, italic: false, font: 0, right: false });
                    if *open && *cite { p.back.push(PBox { x: 2.0, y: g.ty, w: 2.0, h: g.y + g.h - g.ty, role: Role::Line, border: 0.0, border_role: Role::Line, radius: 1.0 }); }
                }
                _ => {}
            }
            if let Some(l) = &g.layout {
                if !g.empty || matches!(b.kind, Kind::Quoted { .. }) {
                    let runs: Vec<Run> = if let Kind::Quoted { text, .. } = &b.kind { vec![Run { text: text.clone(), marks: Marks::default() }] } else { b.runs.clone() };
                    let full: String = runs.iter().map(|r| r.text.as_str()).collect();
                    let dim = matches!(b.kind, Kind::Quoted { .. } | Kind::Quote);
                    paint_layout(&mut p, l, &full, &runs, g.tx, g.ty, dim, &b.kind);
                }
                // selection highlight
                if in_sel && !b.kind.is_atomic() {
                    let s = if i == ss.b { ss.o } else { 0 };
                    let e = if i == se.b { se.o } else { b.len() };
                    if g.empty || s == e {
                        if i != se.b || e > s {
                            let (cx, cy, ch) = self.caret_rect(doc, Pos::new(i, s), false);
                            p.back.push(PBox { x: cx, y: cy, w: 6.0, h: ch, role: Role::Selection, border: 0.0, border_role: Role::Line, radius: 0.0 });
                        }
                    } else {
                        let sl = Selection::new(Cursor::from_byte_index(l, s, Affinity::Downstream), Cursor::from_byte_index(l, e, Affinity::Upstream));
                        for (bb, _) in sl.geometry(l) {
                            p.back.push(PBox { x: g.tx + bb.x0 as f32, y: g.ty + bb.y0 as f32, w: (bb.x1 - bb.x0) as f32, h: (bb.y1 - bb.y0) as f32, role: Role::Selection, border: 0.0, border_role: Role::Line, radius: 0.0 });
                        }
                        if i < se.b && e == b.len() {
                            let (cx, cy, ch) = self.caret_rect(doc, Pos::new(i, e), true);
                            p.back.push(PBox { x: cx, y: cy, w: 6.0, h: ch, role: Role::Selection, border: 0.0, border_role: Role::Line, radius: 0.0 });
                        }
                    }
                }
            }
            if in_sel && matches!(b.kind, Kind::Rule) {
                p.front.push(PBox { x: g.x, y: g.y, w: g.w, h: g.h, role: Role::Selection, border: 0.0, border_role: Role::Line, radius: 0.0 });
            }
            i += 1;
        }
        // caret and selection anchor
        p.caret = self.caret_rect(doc, sel.focus, up);
        if has_sel {
            let (x0, y0, _) = self.caret_rect(doc, ss, false);
            let (x1, y1, _) = self.caret_rect(doc, se, true);
            let x = if (y0 - y1).abs() < 2.0 { (x0 + x1) / 2.0 } else { x0.max(60.0) };
            p.sel_top = Some((x, y0));
        }
        p
    }
}

fn alpha(n: usize) -> String {
    let mut n = n;
    let mut s = String::new();
    while n > 0 { n -= 1; s.insert(0, (b'a' + (n % 26) as u8) as char); n /= 26; }
    s
}

fn roman(mut n: usize) -> String {
    let t = [(1000, "m"), (900, "cm"), (500, "d"), (400, "cd"), (100, "c"), (90, "xc"), (50, "l"), (40, "xl"), (10, "x"), (9, "ix"), (5, "v"), (4, "iv"), (1, "i")];
    let mut s = String::new();
    for (v, r) in t { while n >= v { s.push_str(r); n -= v; } }
    s
}

fn paint_layout(p: &mut Painted, l: &Layout<u32>, full: &str, runs: &[Run], ox: f32, oy: f32, dim: bool, kind: &Kind) {
    let mut starts = Vec::with_capacity(runs.len());
    let mut acc = 0;
    for r in runs { starts.push(acc); acc += r.text.len(); }
    let block_mono = *kind == Kind::Code;
    for line in l.lines() {
        let lr = line.text_range();
        let lm = *line.metrics();
        for item in line.items() {
            let parley::PositionedLayoutItem::GlyphRun(gr) = item else { continue };
            let bi = gr.style().brush as usize;
            let Some(run) = runs.get(bi) else { continue };
            let rr = starts[bi]..starts[bi] + run.text.len();
            let sr = gr.run().text_range();
            let s = rr.start.max(lr.start).max(sr.start);
            let e = rr.end.min(lr.end).min(sr.end);
            if s >= e || e > full.len() { continue; }
            let text: String = full[s..e].trim_end_matches(['\n', '\r']).to_string();
            if text.is_empty() { continue; }
            let m = gr.run().metrics();
            let size = gr.run().font_size();
            let base_line = oy + gr.baseline();
            let top = base_line - m.ascent - m.leading / 2.0;
            let x = ox + gr.offset();
            let w = gr.advance();
            let mk = &run.marks;
            let role = if let Some(c) = mk.color.as_deref().and_then(parse_rgb) { Role::Rgb(c) }
                else if mk.link.is_some() { Role::Link } else if dim { Role::Dim } else { Role::Text };
            if let Some(c) = mk.highlight.as_deref().and_then(parse_rgb) {
                p.back.push(PBox { x, y: oy + lm.block_min_coord, w, h: lm.block_max_coord - lm.block_min_coord, role: Role::Rgb(c), border: 0.0, border_role: Role::Line, radius: 0.0 });
            }
            if mk.code && !block_mono {
                p.back.push(PBox { x: x - 2.0, y: top - 1.0, w: w + 4.0, h: m.ascent + m.descent + m.leading + 2.0, role: Role::CodeBg, border: 0.0, border_role: Role::Line, radius: 3.0 });
            }
            let weight = if mk.bold || matches!(kind, Kind::Heading(_) | Kind::Cell { header: true, .. }) { 700 } else { 400 };
            p.texts.push(PText { x, y: top, w: w + 4.0, h: m.ascent + m.descent + m.leading, text, role, size, weight, italic: mk.italic, font: if block_mono || mk.code { 1 } else { 0 }, right: false });
            let th = (m.underline_size).max(1.0);
            if mk.underline || mk.link.is_some() {
                p.back.push(PBox { x, y: base_line - m.underline_offset, w, h: th, role, border: 0.0, border_role: Role::Line, radius: 0.0 });
            }
            if mk.strike {
                p.back.push(PBox { x, y: base_line - m.strikethrough_offset, w, h: m.strikethrough_size.max(1.0), role, border: 0.0, border_role: Role::Line, radius: 0.0 });
            }
        }
    }
}
