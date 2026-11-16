//! Native rich-text composer: document model, editing commands with undo/redo,
//! HTML import/export, and layout/painting through parley (the text engine Slint uses).
//! This module also holds the glue to the Slint composer (key/pointer handling,
//! clipboard, toolbar commands, pushing the painted document to the UI).
pub mod editor;
pub mod html;
pub mod layout;
pub mod model;

use crate::{AppWindow, RteBox, RteImage, RteState, RteText};
use editor::Editor;
use layout::{Painted, Role, View};
use model::*;
use slint::{Color, ComponentHandle, ModelRc, SharedString, VecModel};
use std::cell::{Cell, RefCell};
use std::collections::HashMap;
use std::sync::Arc;
use std::time::Instant;

thread_local! {
    static ED: RefCell<Editor> = RefCell::new(Editor::default());
    static VIEW: RefCell<View> = RefCell::new(View::default());
    static UP: Cell<bool> = const { Cell::new(false) };
    static IMAGES: RefCell<HashMap<String, (slint::Image, u32, u32)>> = RefCell::new(HashMap::new());
    static CLICKS: Cell<(Option<Instant>, f32, f32, u32)> = const { Cell::new((None, 0.0, 0.0, 0)) };
    static DRAG_ANCHOR: Cell<Option<Pos>> = const { Cell::new(None) };
    static DRAG_WORD: Cell<bool> = const { Cell::new(false) };
}

// ---------------------------------------------------------------- document access

pub fn with<R>(f: impl FnOnce(&mut Editor) -> R) -> R { ED.with(|e| f(&mut e.borrow_mut())) }

/// Replace the composer document (opening a message); clears undo history.
pub fn set_doc(u: &AppWindow, doc: Doc) {
    for b in &doc.blocks { if let Kind::Image(img) = &b.kind { register_image(img); } }
    with(|e| { e.load(doc); e.set_caret(Pos::new(0, 0)); });
    UP.with(|x| x.set(false));
    refresh(u);
}

/// Put the keyboard focus into the editor (now, and when the composer appears).
pub fn focus(u: &AppWindow) {
    u.set_rte_focus_on_open(true);
    u.set_rte_focus_req(u.get_rte_focus_req() + 1);
}

pub fn doc() -> Doc { with(|e| e.doc.clone()) }
pub fn is_empty() -> bool { with(|e| e.doc.is_empty()) }
pub fn to_html() -> String { with(|e| html::export(&e.doc)) }
pub fn to_text() -> String { with(|e| html::to_text(&e.doc)) }

/// Insert blocks at the caret (templates, signatures), as one undo step.
pub fn insert_blocks(u: &AppWindow, blocks: Vec<Block>) {
    for b in &blocks { if let Kind::Image(img) = &b.kind { register_image(img); } }
    with(|e| e.insert_blocks(blocks));
    refresh(u);
}

/// Text for the AI: the selection if any, else the editable body (without the quote).
pub fn ai_source() -> (String, bool) {
    with(|e| {
        if !e.sel.is_collapsed() { return (e.selected_text(), false); }
        let end = e.doc.blocks.iter().position(|b| matches!(b.kind, Kind::Quoted { .. })).unwrap_or(e.doc.blocks.len());
        let d = Doc { blocks: e.doc.blocks[..end].to_vec() };
        let t = if d.blocks.is_empty() { String::new() } else { html::to_text(&d) };
        (t, true)
    })
}

pub fn ai_replace(u: &AppWindow, text: &str, whole: bool) {
    with(|e| e.replace_text(text, whole));
    refresh(u);
}

pub fn undo(u: &AppWindow) { with(|e| e.undo()); refresh(u); }

// ---------------------------------------------------------------- images

pub fn image_key(img: &ImageData) -> String {
    if let Some(c) = &img.cid { return format!("cid:{c}"); }
    if !img.src.is_empty() && !img.src.starts_with("data:") { return img.src.clone(); }
    format!("data:{}", img.bytes.as_ref().map(|b| b.len()).unwrap_or(0))
}

fn decode_image(bytes: &[u8]) -> Option<(slint::Image, u32, u32)> {
    let im = image::load_from_memory(bytes).ok()?.to_rgba8();
    let (w, h) = im.dimensions();
    let buf = slint::SharedPixelBuffer::<slint::Rgba8Pixel>::clone_from_slice(im.as_raw(), w, h);
    Some((slint::Image::from_rgba8(buf), w, h))
}

pub fn register_image(img: &ImageData) {
    let key = image_key(img);
    if IMAGES.with(|m| m.borrow().contains_key(&key)) { return; }
    if let Some(b) = &img.bytes {
        if let Some(x) = decode_image(b) { IMAGES.with(|m| m.borrow_mut().insert(key, x)); }
    }
}

pub fn image_dims(img: &ImageData) -> Option<(u32, u32)> {
    IMAGES.with(|m| m.borrow().get(&image_key(img)).map(|x| (x.1, x.2)))
}

/// Inline images of the document as (cid, blob id, mime, name, size); images still uploading have no blob id.
pub fn inline_images() -> Vec<(String, Option<String>, String, String, usize)> {
    with(|e| e.doc.blocks.iter().filter_map(|b| match &b.kind {
        Kind::Image(i) if i.cid.is_some() => Some((i.cid.clone().unwrap(), i.blob_id.clone(), i.mime.clone(), i.name.clone(), i.bytes.as_ref().map(|b| b.len()).unwrap_or(0))),
        _ => None,
    }).collect())
}

/// Record the uploaded blob of an inline image (undo history keeps the old value harmlessly).
pub fn set_image_blob(cid: &str, blob: &str) {
    with(|e| {
        for b in &mut e.doc.blocks {
            if let Kind::Image(i) = &mut b.kind { if i.cid.as_deref() == Some(cid) { i.blob_id = Some(blob.to_string()); } }
        }
    });
}

fn new_cid() -> String {
    let mut r = [0u8; 8];
    let _ = getrandom::getrandom(&mut r);
    format!("img{}@barbican", r.iter().map(|b| format!("{b:02x}")).collect::<String>())
}

/// Insert an image (pasted, dropped or picked) and start uploading it as a cid attachment.
pub fn insert_image_bytes(u: &AppWindow, bytes: Vec<u8>, mime: &str, name: &str) {
    let Some((im, w, h)) = decode_image(&bytes) else { u.set_c_status("This image format is not supported.".into()); return };
    let cid = new_cid();
    // large images are shown at most 600 px wide (the sent file keeps full resolution)
    let (dw, dh) = if w > 600 { (600, (h as f32 * 600.0 / w as f32) as u32) } else { (0, 0) };
    let img = ImageData { src: String::new(), cid: Some(cid.clone()), blob_id: None, mime: mime.into(), name: name.into(), width: dw, height: dh, bytes: Some(Arc::new(bytes.clone())), alt: name.into() };
    IMAGES.with(|m| m.borrow_mut().insert(image_key(&img), (im, w, h)));
    with(|e| e.insert_image(img));
    refresh(u);
    crate::compose::upload_inline(u, cid, bytes, mime.to_string(), name.to_string());
}

pub fn pick_image(u: &AppWindow) {
    let Some(f) = rfd::FileDialog::new().add_filter("Images", &["png", "jpg", "jpeg", "gif", "webp", "bmp"]).pick_file() else { return };
    let name = f.file_name().map(|n| n.to_string_lossy().to_string()).unwrap_or_else(|| "image".into());
    match std::fs::read(&f) {
        Ok(b) => insert_image_bytes(u, b, crate::compose::mime_for(&name), &name),
        Err(e) => u.set_c_status(e.to_string().into()),
    }
}

// ---------------------------------------------------------------- painting

fn role_color(r: Role) -> (i32, Color) {
    let (code, rgb) = r.code();
    (code, Color::from_argb_u8(255, (rgb >> 16) as u8, (rgb >> 8) as u8, rgb as u8))
}

fn state(e: &Editor) -> RteState {
    let m = e.current_marks();
    let fb = e.focus_block();
    let block = match &fb.kind {
        Kind::Para => "p".to_string(), Kind::Heading(l) => format!("h{l}"), Kind::Quote => "quote".into(), Kind::Code => "code".into(),
        Kind::Bullet => "bullet".into(), Kind::Ordered => "ordered".into(), Kind::Cell { .. } => "cell".into(), Kind::Image(_) => "image".into(),
        Kind::Quoted { .. } => "quoted".into(), Kind::Rule => "rule".into(),
    };
    let link = e.current_link().unwrap_or_default();
    RteState {
        bold: m.bold, italic: m.italic, underline: m.underline, strike: m.strike, code: m.code,
        color: m.color.clone().unwrap_or_default().into(), highlight: m.highlight.is_some(),
        size: m.size.map(|s| s as i32).unwrap_or(0),
        block: block.into(), align: fb.align.css().into(), in_table: fb.kind.is_cell(),
        link: link.into(), has_sel: !e.sel.is_collapsed(),
        can_undo: e.can_undo(), can_redo: e.can_redo(),
    }
}

/// Lay out, paint and push everything to the UI.
pub fn refresh(u: &AppWindow) {
    let width = u.get_rte_width().max(80.0);
    let base = u.get_rte_base_size().max(9.0);
    let up = UP.with(|x| x.get());
    let (p, st, empty): (Painted, RteState, bool) = ED.with(|e| {
        let e = e.borrow();
        VIEW.with(|v| {
            let mut v = v.borrow_mut();
            v.build(&e.doc, width, base, &image_dims);
            (v.paint(&e.doc, &e.sel, up, base), state(&e), e.doc.is_empty())
        })
    });
    let texts: Vec<RteText> = p.texts.into_iter().map(|t| {
        let (role, color) = role_color(t.role);
        RteText { x: t.x, y: t.y, w: t.w, h: t.h, text: t.text.into(), role, color, size: t.size, weight: t.weight, italic: t.italic, font: t.font, right: t.right }
    }).collect();
    let boxes = |v: Vec<layout::PBox>| -> Vec<RteBox> {
        v.into_iter().map(|b| {
            let (role, color) = role_color(b.role);
            let (brole, bcolor) = role_color(b.border_role);
            RteBox { x: b.x, y: b.y, w: b.w, h: b.h, role, color, border: b.border, border_role: brole, border_color: bcolor, radius: b.radius }
        }).collect()
    };
    let imgs: Vec<RteImage> = p.imgs.into_iter().map(|i| {
        let img = IMAGES.with(|m| m.borrow().get(&i.key).map(|x| x.0.clone())).unwrap_or_default();
        RteImage { x: i.x, y: i.y, w: i.w, h: i.h, img, selected: i.selected, label: i.label.into() }
    }).collect();
    u.set_rte_texts(ModelRc::new(VecModel::from(texts)));
    u.set_rte_back(ModelRc::new(VecModel::from(boxes(p.back))));
    u.set_rte_front(ModelRc::new(VecModel::from(boxes(p.front))));
    u.set_rte_imgs(ModelRc::new(VecModel::from(imgs)));
    u.set_rte_height(p.height);
    u.set_rte_caret_x(p.caret.0);
    u.set_rte_caret_y(p.caret.1);
    u.set_rte_caret_h(p.caret.2);
    u.set_rte_empty(empty);
    match p.sel_top {
        Some((x, y)) => { u.set_rte_sel_x(x); u.set_rte_sel_y(y); }
        None => { u.set_rte_sel_x(-1.0); }
    }
    u.set_rte_state(st);
    u.set_rte_blink(true);
}

// ---------------------------------------------------------------- clipboard

fn copy(cut: bool) -> bool {
    let (frag, text) = with(|e| (e.selected_blocks(), e.selected_text()));
    if frag.is_empty() { return false; }
    let d = Doc { blocks: frag };
    let html = html::export(&d);
    let plain = if text.is_empty() { html::to_text(&d) } else { text };
    if let Ok(mut cb) = arboard::Clipboard::new() { let _ = cb.set_html(html, Some(plain)); }
    if cut { with(|e| e.backspace(false)); }
    true
}

/// Strip the CF_HTML header and keep the copied fragment.
fn clipboard_fragment(h: &str) -> String {
    if let (Some(a), Some(b)) = (h.find("<!--StartFragment-->"), h.find("<!--EndFragment-->")) {
        if a < b { return h[a + 20..b].to_string(); }
    }
    if h.starts_with("Version:") { if let Some(p) = h.find('<') { return h[p..].to_string(); } }
    h.to_string()
}

fn paste(u: &AppWindow, plain_only: bool) {
    let Ok(mut cb) = arboard::Clipboard::new() else { return };
    let in_code = with(|e| e.focus_block().kind == Kind::Code);
    if !plain_only && !in_code {
        if let Ok(files) = cb.get().file_list() {
            let imgs: Vec<_> = files.into_iter().filter(|f| crate::compose::mime_for(&f.to_string_lossy()).starts_with("image/")).collect();
            if !imgs.is_empty() {
                for f in imgs {
                    let name = f.file_name().map(|n| n.to_string_lossy().to_string()).unwrap_or_else(|| "image".into());
                    if let Ok(b) = std::fs::read(&f) { insert_image_bytes(u, b, crate::compose::mime_for(&name), &name); }
                }
                return;
            }
        }
        if let Ok(h) = cb.get().html() {
            let blocks = html::import(&clipboard_fragment(&h));
            if !blocks.is_empty() {
                for b in &blocks { if let Kind::Image(img) = &b.kind { register_image(img); } }
                with(|e| e.insert_blocks(blocks));
                return;
            }
        }
        if let Ok(img) = cb.get_image() {
            let mut png_bytes = vec![];
            {
                let mut enc = png::Encoder::new(&mut png_bytes, img.width as u32, img.height as u32);
                enc.set_color(png::ColorType::Rgba);
                enc.set_depth(png::BitDepth::Eight);
                if let Ok(mut w) = enc.write_header() { let _ = w.write_image_data(&img.bytes); }
            }
            if !png_bytes.is_empty() { insert_image_bytes(u, png_bytes, "image/png", "pasted-image.png"); }
            return;
        }
    }
    if let Ok(t) = cb.get_text() {
        let t = t.replace("\r\n", "\n");
        with(|e| e.insert_text(&t));
    }
}

// ---------------------------------------------------------------- input

fn key(t: &str, k: slint::platform::Key) -> bool { let s: SharedString = k.into(); t == s.as_str() }

/// Keyboard input from the editor's FocusScope. Returns true when handled.
pub fn on_key(u: &AppWindow, text: &str, ctrl: bool, shift: bool, alt: bool) -> bool {
    let handled = handle_key(u, text, ctrl, shift, alt);
    if handled { refresh(u); u.invoke_rte_scroll_to_caret(); }
    handled
}

fn handle_key(u: &AppWindow, text: &str, ctrl: bool, shift: bool, alt: bool) -> bool {
    use slint::platform::Key as K;
    let t = text;
    let altgr = ctrl && alt;
    // ---- movement
    let moves = [K::LeftArrow, K::RightArrow, K::UpArrow, K::DownArrow, K::Home, K::End, K::PageUp, K::PageDown];
    if moves.iter().any(|k| key(t, *k)) {
        let up = UP.with(|x| x.get());
        let (np, nup, keep_goal) = VIEW.with(|v| {
            let v = v.borrow();
            with(|e| {
                let f = e.sel.focus;
                let doc = &e.doc;
                // collapsing a selection with Left/Right goes to its edge
                if !shift && !e.sel.is_collapsed() && (key(t, K::LeftArrow) || key(t, K::RightArrow)) && !ctrl {
                    return (if key(t, K::LeftArrow) { e.sel.start() } else { e.sel.end() }, false, false);
                }
                if key(t, K::LeftArrow) {
                    if f.o == 0 { return (if f.b > 0 { Pos::new(f.b - 1, doc.blocks[f.b - 1].len()) } else { f }, false, false); }
                    if ctrl { return v.move_in_line(doc, f, up, "word-left").map(|(p, u)| (p, u, false)).unwrap_or((f, false, false)); }
                    let s = doc.blocks[f.b].text();
                    return (Pos::new(f.b, prev_char(&s, f.o)), false, false);
                }
                if key(t, K::RightArrow) {
                    let len = doc.blocks[f.b].len();
                    if f.o >= len { return (if f.b + 1 < doc.blocks.len() { Pos::new(f.b + 1, 0) } else { f }, false, false); }
                    if ctrl { return v.move_in_line(doc, f, up, "word-right").map(|(p, u)| (p, u, false)).unwrap_or((f, false, false)); }
                    let s = doc.blocks[f.b].text();
                    return (Pos::new(f.b, next_char(&s, f.o)), false, false);
                }
                if key(t, K::Home) {
                    if ctrl { return (Pos::new(0, 0), false, false); }
                    return v.move_in_line(doc, f, up, "home").map(|(p, u)| (p, u, false)).unwrap_or((Pos::new(f.b, 0), false, false));
                }
                if key(t, K::End) {
                    if ctrl { return (doc.end(), false, false); }
                    return v.move_in_line(doc, f, up, "end").map(|(p, u)| (p, u, false)).unwrap_or((Pos::new(f.b, doc.blocks[f.b].len()), true, false));
                }
                let goal = e.goal_x.unwrap_or_else(|| v.caret_rect(doc, f, up).0);
                let (dir, n) = if key(t, K::UpArrow) { (-1, 1) } else if key(t, K::DownArrow) { (1, 1) } else if key(t, K::PageUp) { (-1, 15) } else { (1, 15) };
                let (mut p, mut pu) = (f, up);
                for _ in 0..n { let (q, qu) = v.move_vertical(doc, p, pu, dir, goal); if q == p { break; } p = q; pu = qu; }
                (p, pu, true)
            })
        });
        let goal = if keep_goal {
            Some(with(|e| e.goal_x).unwrap_or_else(|| VIEW.with(|v| with(|e| v.borrow().caret_rect(&e.doc, e.sel.focus, up).0))))
        } else { None };
        with(|e| {
            if shift { let a = e.sel.anchor; e.select(a, np); } else { e.set_caret(np); }
            e.goal_x = goal;
        });
        UP.with(|x| x.set(nup));
        return true;
    }
    UP.with(|x| x.set(false));
    if key(t, K::Backspace) { with(|e| { e.goal_x = None; e.backspace(ctrl) }); return true; }
    if key(t, K::Delete) {
        if shift && !ctrl { copy(true); return true; }
        with(|e| e.delete_forward(ctrl)); return true;
    }
    if key(t, K::Return) || t == "\n" || t == "\r" {
        if ctrl { return false; } // Ctrl+Enter sends
        with(|e| e.enter(shift));
        return true;
    }
    if key(t, K::Tab) || t == "\t" {
        if ctrl || alt { return false; }
        let done = with(|e| e.cell_tab(shift) || e.indent(if shift { -1 } else { 1 }));
        if done { return true; }
        if shift { return false; }
        with(|e| e.insert_text("\t"));
        return true;
    }
    if key(t, K::Escape) || t == "\u{1b}" {
        if u.get_rte_link_open() { u.set_rte_link_open(false); return true; }
        return false;
    }
    if key(t, K::Insert) {
        if shift { paste(u, false); return true; }
        if ctrl { copy(false); return true; }
        return false;
    }
    if ctrl && !altgr {
        let lower = t.to_lowercase();
        let c = lower.as_str();
        match (c, shift, alt) {
            ("a", false, false) => with(|e| e.select_all()),
            ("b", false, false) => with(|e| e.toggle_mark(Mark::Bold)),
            ("i", false, false) => with(|e| e.toggle_mark(Mark::Italic)),
            ("u", false, false) => with(|e| e.toggle_mark(Mark::Underline)),
            ("e", false, false) => with(|e| e.toggle_mark(Mark::Code)),
            ("x", true, false) | ("s", true, false) => with(|e| e.toggle_mark(Mark::Strike)),
            ("h", true, false) => with(|e| { let on = e.current_marks().highlight.is_some(); e.set_highlight(if on { None } else { Some("#fef08a".into()) }) }),
            ("k", false, false) => open_link_editor(u),
            ("z", false, false) => { with(|e| e.undo()); }
            ("y", false, false) | ("z", true, false) => { with(|e| e.redo()); }
            ("c", false, false) => { copy(false); }
            ("x", false, false) => { copy(true); }
            ("v", false, false) => paste(u, false),
            ("v", true, false) => paste(u, true),
            ("7", true, false) | ("&", true, false) => with(|e| e.set_block(Kind::Ordered)),
            ("8", true, false) | ("*", true, false) => with(|e| e.set_block(Kind::Bullet)),
            ("b", true, false) => with(|e| e.set_block(Kind::Quote)),
            ("c", false, true) => with(|e| e.set_block(Kind::Code)),
            ("0", false, true) => with(|e| e.set_block(Kind::Para)),
            ("1", false, true) | ("2", false, true) | ("3", false, true) => { let l = c.parse::<u8>().unwrap(); with(|e| e.set_block(Kind::Heading(l))) }
            ("l", true, false) => with(|e| e.set_align(Align::Left)),
            ("e", true, false) => with(|e| e.set_align(Align::Center)),
            ("r", true, false) => with(|e| e.set_align(Align::Right)),
            ("j", true, false) => with(|e| e.set_align(Align::Justify)),
            ("\\", false, false) | (" ", false, false) => with(|e| e.clear_format()),
            ("]", false, false) => { with(|e| e.indent(1)); }
            ("[", false, false) => { with(|e| e.indent(-1)); }
            _ => return false,
        }
        return true;
    }
    // ---- text input (also IME commits, which arrive as key text)
    let printable: String = t.chars().filter(|c| !c.is_control() && !('\u{f700}'..='\u{f8ff}').contains(c)).collect();
    if printable.is_empty() || (alt && !ctrl) { return false; }
    with(|e| { e.goal_x = None; e.insert_text(&printable) });
    true
}

/// Mouse input on the editor surface (content coordinates).
pub fn on_pointer(u: &AppWindow, kind: &str, x: f32, y: f32, shift: bool) {
    match kind {
        "down" => {
            let (pos, up, header_hit) = VIEW.with(|v| {
                let v = v.borrow();
                with(|e| {
                    let (p, up) = v.hit(&e.doc, x, y);
                    let hh = v.geo.get(p.b).and_then(|g| g.header).is_some_and(|(hy, hh)| y >= hy && y <= hy + hh && x <= 190.0);
                    (p, up, hh)
                })
            });
            if header_hit {
                with(|e| { e.toggle_quoted(pos.b); e.set_caret(pos); });
                refresh(u);
                return;
            }
            // click counting (double = word, triple = paragraph)
            let now = Instant::now();
            let n = CLICKS.with(|c| {
                let (t, cx, cy, n) = c.get();
                let n = if t.is_some_and(|t| now.duration_since(t).as_millis() < 450) && (cx - x).abs() < 5.0 && (cy - y).abs() < 5.0 { n % 3 + 1 } else { 1 };
                c.set((Some(now), x, y, n));
                n
            });
            UP.with(|c| c.set(up));
            with(|e| {
                e.goal_x = None;
                match n {
                    2 => { e.select_word(pos); }
                    3 => { e.select_block(pos.b); }
                    _ if shift => { let a = e.sel.anchor; e.select(a, pos); }
                    _ => e.set_caret(pos),
                }
            });
            DRAG_ANCHOR.with(|d| d.set(Some(with(|e| if n >= 2 { e.sel.start() } else { e.sel.anchor }))));
            DRAG_WORD.with(|d| d.set(n == 2));
            if u.get_rte_link_open() { u.set_rte_link_open(false); }
            refresh(u);
        }
        "drag" => {
            let Some(anchor) = DRAG_ANCHOR.with(|d| d.get()) else { return };
            let (pos, up) = VIEW.with(|v| with(|e| v.borrow().hit(&e.doc, x, y)));
            UP.with(|c| c.set(up));
            with(|e| {
                if DRAG_WORD.with(|d| d.get()) {
                    // extend by whole words
                    let saved = e.sel;
                    e.select_word(pos);
                    let w = e.sel;
                    e.sel = saved;
                    if pos < anchor { let end = saved.end().max(anchor); e.select(end, w.start()); } else { e.select(anchor, w.end()); }
                } else {
                    e.select(anchor, pos);
                }
            });
            refresh(u);
            u.invoke_rte_scroll_to_caret();
        }
        "up" => { DRAG_ANCHOR.with(|d| d.set(None)); }
        _ => {}
    }
}

fn open_link_editor(u: &AppWindow) {
    let (url, text) = with(|e| (e.current_link().unwrap_or_default(), if e.sel.is_collapsed() {
        e.link_range().map(|(s, en, _)| e.focus_block().text()[s..en].to_string()).unwrap_or_default()
    } else { e.selected_text() }));
    u.set_rte_link_url(url.into());
    u.set_rte_link_text(text.into());
    u.set_rte_link_open(true);
}

/// Toolbar and menu commands.
pub fn on_cmd(u: &AppWindow, cmd: &str) {
    let (name, arg) = cmd.split_once(':').unwrap_or((cmd, ""));
    match name {
        "relayout" => {}
        "bold" => with(|e| e.toggle_mark(Mark::Bold)),
        "italic" => with(|e| e.toggle_mark(Mark::Italic)),
        "underline" => with(|e| e.toggle_mark(Mark::Underline)),
        "strike" => with(|e| e.toggle_mark(Mark::Strike)),
        "code-inline" => with(|e| e.toggle_mark(Mark::Code)),
        "color" => with(|e| e.set_color((!arg.is_empty()).then(|| arg.to_string()))),
        "highlight" => with(|e| {
            let on = e.current_marks().highlight.is_some();
            let c = if arg.is_empty() { if on { None } else { Some("#fef08a".to_string()) } } else if arg == "none" { None } else { Some(arg.to_string()) };
            e.set_highlight(c)
        }),
        "size" => with(|e| e.set_size(arg.parse::<u8>().ok().filter(|s| *s != 0 && *s != 14))),
        "h1" => with(|e| e.set_block(Kind::Heading(1))),
        "h2" => with(|e| e.set_block(Kind::Heading(2))),
        "h3" => with(|e| e.set_block(Kind::Heading(3))),
        "para" => with(|e| e.set_block(Kind::Para)),
        "bullet" => with(|e| e.set_block(Kind::Bullet)),
        "numbered" => with(|e| e.set_block(Kind::Ordered)),
        "quote" => with(|e| e.set_block(Kind::Quote)),
        "code" => with(|e| e.set_block(Kind::Code)),
        "indent" => { with(|e| e.indent(1)); }
        "outdent" => { with(|e| e.indent(-1)); }
        "align" => with(|e| e.set_align(match arg { "center" => Align::Center, "right" => Align::Right, "justify" => Align::Justify, _ => Align::Left })),
        "clear" => with(|e| e.clear_format()),
        "undo" => { with(|e| e.undo()); }
        "redo" => { with(|e| e.redo()); }
        "rule" => with(|e| e.insert_rule()),
        "link" => { open_link_editor(u); return; }
        "link-apply" => {
            let (url, text) = (u.get_rte_link_url().to_string(), u.get_rte_link_text().to_string());
            with(|e| e.set_link(&url, &text));
            u.set_rte_link_open(false);
        }
        "link-remove" => { with(|e| e.set_link("", "")); u.set_rte_link_open(false); }
        "link-open" => {
            if let Some(l) = with(|e| e.current_link()) { crate::oauth::open_browser(&l); }
            return;
        }
        "table" => {
            let (r, c) = arg.split_once('x').and_then(|(a, b)| Some((a.parse().ok()?, b.parse().ok()?))).unwrap_or((3, 3));
            with(|e| e.insert_table(r, c));
        }
        "table-op" => { with(|e| e.table_op(arg)); }
        "image" => { pick_image(u); return; }
        "copy" => { copy(false); return; }
        "copy-link" => {
            if let Ok(mut cb) = arboard::Clipboard::new() { let _ = cb.set_text(u.get_rte_link_url().to_string()); }
            return;
        }
        "cut" => { copy(true); }
        "paste" => paste(u, false),
        "paste-plain" => paste(u, true),
        "select-all" => with(|e| e.select_all()),
        _ => return,
    }
    refresh(u);
}

pub fn wire(ui: &AppWindow) {
    let w = ui.as_weak();
    ui.on_rte_key(move |text, ctrl, shift, alt| on_key(&w.unwrap(), &text, ctrl, shift, alt));
    let w = ui.as_weak();
    ui.on_rte_pointer(move |kind, x, y, shift| on_pointer(&w.unwrap(), &kind, x, y, shift));
    let w = ui.as_weak();
    ui.on_rte_cmd(move |cmd| on_cmd(&w.unwrap(), &cmd));
}
