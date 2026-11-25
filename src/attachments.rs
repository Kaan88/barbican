//! Attachment preview cards in the reader (Bulwark reader-attachments.tsx with
//! attachmentImagePreviewsEnabled): each file is a tile with its name and size; an image up to
//! 10 MB shows its own picture, fetched and scaled down in the background (and kept for the
//! session), anything else its kind.
use crate::jmap::Client;
use crate::{AppWindow, AttCard};
use serde_json::Value;
use slint::{ComponentHandle, Model, ModelRc, SharedPixelBuffer, Rgba8Pixel, VecModel};
use std::cell::RefCell;
use std::collections::HashMap;
use std::sync::Arc;

/// Bulwark fetches thumbnails for image attachments up to this size.
const MAX_PREVIEW_BYTES: u64 = 10_000_000;
/// Thumbnail edge in pixels (the tile draws 40 logical px; twice that stays sharp at 200 %).
const THUMB: u32 = 80;
const CACHE_MAX: usize = 200;

thread_local! {
    static THUMBS: RefCell<HashMap<String, SharedPixelBuffer<Rgba8Pixel>>> = RefCell::new(HashMap::new());
}

/// "report.final.pdf" -> ("report.final", "PDF"); a leading dot or none gives no extension.
pub fn split_name(name: &str) -> (String, String) {
    match name.rsplit_once('.') {
        Some((stem, ext)) if !stem.is_empty() && !ext.is_empty() && ext.len() <= 8 && !ext.contains(' ') => (stem.to_string(), ext.to_uppercase()),
        _ => (name.to_string(), String::new()),
    }
}

/// Renaming an attachment (Bulwark components/email/rename-attachment-dialog.tsx, lib/attachment-name.ts
/// splitAttachmentName, lib/file-name-rules.ts plainFileNameProblem, lib/download-filename.ts
/// renamedAttachmentFilename): only the part before the extension is edited, so the file keeps
/// its type; the new name goes out with the message (composer) or names the saved copies (reader).
///
/// ("report.final.pdf") -> ("report.final", ".pdf"); a leading or trailing dot is no extension.
pub fn split_stem_ext(name: &str) -> (String, String) {
    match name.rfind('.') {
        Some(dot) if dot > 0 && dot < name.len() - 1 => (name[..dot].to_string(), name[dot..].to_string()),
        _ => (name.to_string(), String::new()),
    }
}

/// Longest name Bulwark takes for a copy (characters; every server refuses more than 255 bytes).
const MAX_NAME: usize = 255;
/// Longest a saved name may be in UTF-8 bytes (what NTFS, ext4 and APFS take per name component).
const MAX_NAME_BYTES: usize = 255;
/// Longest extension (dot included, in bytes) a cut name keeps; anything longer is cut too.
const MAX_EXT_BYTES: usize = 16;

/// Why a typed name can't be used (Bulwark plainFileNameProblem), in the user's words.
pub fn name_problem(name: &str) -> Option<&'static str> {
    if name.chars().count() > MAX_NAME { return Some("This name is too long"); }
    if name.chars().any(|c| c == '/' || c == '\\' || c.is_control()) { return Some("File names can't contain / or \\ or control characters"); }
    if name.chars().any(is_bidi_control) { return Some("File names can't contain text direction characters"); }
    None
}

/// The outcome of the rename prompt: `Err` with the reason, `Ok(None)` when nothing changed,
/// else the new full name (the typed stem plus the old extension).
pub fn renamed(old: &str, typed_stem: &str) -> Result<Option<String>, &'static str> {
    let stem = typed_stem.trim();
    if stem.is_empty() { return Ok(None); }
    let (_, ext) = split_stem_ext(old);
    let new = format!("{stem}{ext}");
    if let Some(p) = name_problem(&new) { return Err(p); }
    Ok((new != old).then_some(new))
}

/// Characters that change the direction text is drawn in (LRM, RLM, ALM, the embeddings and
/// overrides, the isolates).
pub fn is_bidi_control(c: char) -> bool {
    matches!(c, '\u{061c}' | '\u{200e}' | '\u{200f}' | '\u{202a}'..='\u{202e}' | '\u{2066}'..='\u{2069}')
}

/// Whether Windows treats `stem` (the part before the first dot) as a device, not a file:
/// CON, PRN, AUX, NUL, CONIN$, CONOUT$ and COM / LPT with 1-9 or a superscript 1, 2, 3.
fn is_device_name(stem: &str) -> bool {
    let up = stem.trim_end().to_uppercase();
    if matches!(up.as_str(), "CON" | "PRN" | "AUX" | "NUL" | "CONIN$" | "CONOUT$") { return true; }
    let mut ch = up.chars();
    let head: String = ch.by_ref().take(3).collect();
    (head == "COM" || head == "LPT")
        && matches!((ch.next(), ch.next()), (Some('1'..='9' | '\u{b9}' | '\u{b2}' | '\u{b3}'), None))
}

/// `s` cut to at most `max` bytes on a character boundary.
fn cut_bytes(s: &str, max: usize) -> &str {
    if s.len() <= max { return s; }
    let mut n = max;
    while !s.is_char_boundary(n) { n -= 1; }
    &s[..n]
}

/// A name a file system takes: directional controls (they reorder what the user sees, so
/// `report<RLO>fdp.exe` would look like `reportexe.pdf`), control characters and `<>:"/\|?*`
/// become `_` or go; a name with nothing left, or ending in dots or spaces, is repaired; a
/// Windows device name (CON, NUL, COM1...) gets a leading `_`; the length is capped at 255
/// characters keeping the extension.
pub fn safe_file_name(name: &str) -> String {
    let bad = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
    let s: String = name.chars().filter(|c| !is_bidi_control(*c)).map(|c| if bad.contains(&c) || c.is_control() { '_' } else { c }).collect();
    let s = s.trim().trim_matches('.').trim().to_string();
    if s.is_empty() { return "attachment".into(); }
    let (stem, ext) = split_stem_ext(&s);
    let reserved = is_device_name(s.split('.').next().unwrap_or(""));
    let (stem, s) = if reserved { (format!("_{stem}"), format!("_{s}")) } else { (stem, s) };
    if s.len() <= MAX_NAME_BYTES { return s; }
    // too long: keep a sane extension (an absurd one is cut too), shorten the stem to fit
    let ext = cut_bytes(&ext, MAX_EXT_BYTES).trim_end_matches(['.', ' ']);
    let ext = if ext == "." { "" } else { ext };
    let stem = cut_bytes(&stem, MAX_NAME_BYTES - ext.len()).trim_end_matches(['.', ' ']);
    if stem.is_empty() { return format!("attachment{ext}"); }
    format!("{stem}{ext}")
}

/// The name a saved copy of an attachment gets: a renamed one as the user typed it, cleaned up.
pub fn save_name(a: &Value) -> String { save_name_with(a, &crate::dlnames::Opts::load()) }

/// The name a copy of an attachment gets in a zip or a folder (Bulwark attachmentCopyName,
/// email-viewer.tsx:499-502): no template and no transforms, only made safe for this disk.
pub fn copy_name(a: &Value) -> String {
    safe_file_name(a["name"].as_str().filter(|n| !n.is_empty()).unwrap_or("attachment"))
}

/// The saved name by the "Downloads" settings: a renamed attachment as typed (Bulwark
/// renamedAttachmentFilename), any other through attachmentDownloadFilename
/// (email-viewer.tsx:3323-3330); then made safe for this disk.
pub fn save_name_with(a: &Value, o: &crate::dlnames::Opts) -> String {
    let raw = a["name"].as_str().filter(|n| !n.is_empty()).unwrap_or("attachment");
    if a["renamed"] == true { return safe_file_name(raw); }
    safe_file_name(&crate::dlnames::attachment_download_filename(raw, o))
}

/// The name of attachment `i` of the open message (UI thread).
pub fn reader_name(i: usize) -> Option<String> {
    crate::CUR_MSG.with(|x| x.borrow().as_ref().and_then(|(_, atts)| atts.get(i).and_then(|a| a["name"].as_str().map(String::from))))
}

/// Give attachment `i` of the open message a new name: the reader's chips and cards show it
/// and saved copies (Save as, Download all) use it; the message on the server is untouched.
pub fn rename_in_reader(u: &AppWindow, i: usize, new: &str) {
    crate::CUR_MSG.with(|x| {
        if let Some((_, atts)) = x.borrow_mut().as_mut() {
            if let Some(a) = atts.get_mut(i) { a["name"] = Value::String(new.to_string()); a["renamed"] = Value::Bool(true); }
        }
    });
    let rows = u.get_attachments();
    for k in 0..rows.row_count() {
        if let Some(mut r) = rows.row_data(k) {
            if r.index == i as i32 { r.name = new.into(); rows.set_row_data(k, r); }
        }
    }
    let cards = u.get_att_cards();
    for k in 0..cards.row_count() {
        if let Some(mut c) = cards.row_data(k) {
            if c.index == i as i32 { let (stem, ext) = split_name(new); c.stem = stem.into(); c.ext = ext.into(); cards.set_row_data(k, c); }
        }
    }
}

/// An image the app can draw a thumbnail of (the image crate's decoders), within the size cap.
pub fn previewable(a: &Value) -> bool {
    let t = a["type"].as_str().unwrap_or("").to_lowercase();
    let decodable = matches!(t.as_str(), "image/png" | "image/jpeg" | "image/jpg" | "image/pjpeg" | "image/gif" | "image/webp" | "image/bmp");
    decodable && a["size"].as_u64().unwrap_or(0) <= MAX_PREVIEW_BYTES && a["blobId"].is_string()
}

/// Whether the reader shows the cards: the setting is on and at least one file is an image
/// (otherwise the compact chips stay, as before).
pub fn wanted(atts: &[Value], enabled: bool) -> bool {
    enabled && atts.iter().any(previewable)
}

/// "3 attachments · 1.2 MB"
pub fn summary(atts: &[Value]) -> String {
    let total: u64 = atts.iter().map(|a| a["size"].as_u64().unwrap_or(0)).sum();
    format!("{} attachment{} · {}", atts.len(), if atts.len() == 1 { "" } else { "s" }, crate::human_size(total))
}

/// Decode an image and scale it to fill a square thumbnail (cropping the overflow).
pub fn thumbnail(bytes: &[u8]) -> Option<SharedPixelBuffer<Rgba8Pixel>> {
    let img = image::load_from_memory(bytes).ok()?;
    let t = img.resize_to_fill(THUMB, THUMB, image::imageops::FilterType::Triangle).to_rgba8();
    Some(SharedPixelBuffer::clone_from_slice(t.as_raw(), t.width(), t.height()))
}

/// Fill the reader's attachment cards for message `id` (the open one), then fetch the image
/// thumbnails it lacks. `atts` are the listed attachments in Att index order.
pub fn show(u: &AppWindow, c: Arc<Client>, id: &str, atts: &[Value]) {
    let enabled = crate::settings::Settings::load().bool("attachmentImagePreviewsEnabled", true);
    if !wanted(atts, enabled) {
        u.set_att_cards(ModelRc::default());
        return;
    }
    let cards: Vec<AttCard> = atts.iter().enumerate().map(|(i, a)| {
        let (stem, ext) = split_name(a["name"].as_str().unwrap_or("attachment"));
        let cached = a["blobId"].as_str().and_then(|b| THUMBS.with(|t| t.borrow().get(b).cloned()));
        AttCard {
            index: i as i32,
            stem: stem.into(),
            ext: ext.into(),
            size: crate::human_size(a["size"].as_u64().unwrap_or(0)).into(),
            has_thumb: cached.is_some(),
            thumb: cached.map(slint::Image::from_rgba8).unwrap_or_default(),
        }
    }).collect();
    u.set_att_summary(summary(atts).into());
    u.set_att_cards(ModelRc::new(VecModel::from(cards)));
    let todo: Vec<(usize, String, String, String)> = atts.iter().enumerate()
        .filter(|(_, a)| previewable(a))
        .filter_map(|(i, a)| Some((i, a["blobId"].as_str()?.to_string(), a["name"].as_str().unwrap_or("image").to_string(), a["type"].as_str().unwrap_or("").to_string())))
        .filter(|(_, b, _, _)| THUMBS.with(|t| !t.borrow().contains_key(b)))
        .collect();
    if todo.is_empty() { return; }
    let (w, id) = (u.as_weak(), id.to_string());
    std::thread::spawn(move || {
        for (i, blob, name, mime) in todo {
            let Some(buf) = c.download(&blob, &name, &mime).ok().and_then(|b| thumbnail(&b)) else { continue };
            let (id, blob) = (id.clone(), blob.clone());
            let _ = w.upgrade_in_event_loop(move |u| {
                THUMBS.with(|t| {
                    let mut t = t.borrow_mut();
                    if t.len() >= CACHE_MAX { t.clear(); }
                    t.insert(blob, buf.clone());
                });
                if u.get_current_mail().as_str() != id { return; }
                let m = u.get_att_cards();
                if let Some(mut card) = m.row_data(i) {
                    card.thumb = slint::Image::from_rgba8(buf);
                    card.has_thumb = true;
                    m.set_row_data(i, card);
                }
            });
        }
    });
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn names_and_kinds() {
        assert_eq!(split_name("report.final.pdf"), ("report.final".into(), "PDF".into()));
        assert_eq!(split_name("README"), ("README".into(), String::new()));
        assert_eq!(split_name(".bashrc"), (".bashrc".into(), String::new()));
        assert_eq!(split_name("a.tar gz"), ("a.tar gz".into(), String::new()));
    }

    #[test]
    fn rename_keeps_the_extension() {
        assert_eq!(split_stem_ext("report.final.pdf"), ("report.final".into(), ".pdf".into()));
        assert_eq!(split_stem_ext("README"), ("README".into(), String::new()));
        assert_eq!(split_stem_ext(".bashrc"), (".bashrc".into(), String::new()));
        assert_eq!(split_stem_ext("a."), ("a.".into(), String::new()));
        assert_eq!(renamed("report.pdf", " Q3 numbers "), Ok(Some("Q3 numbers.pdf".to_string())));
        assert_eq!(renamed("report.pdf", "report"), Ok(None));
        assert_eq!(renamed("report.pdf", "  "), Ok(None));
        assert_eq!(renamed("notes", "todo"), Ok(Some("todo".to_string())));
        assert!(renamed("report.pdf", "a/b").is_err());
        assert!(renamed("report.pdf", "a\\b").is_err());
        assert!(renamed("report.pdf", "a\u{7}b").is_err());
        for c in ['\u{202e}', '\u{202a}', '\u{2066}', '\u{200f}', '\u{61c}'] {
            assert!(renamed("report.pdf", &format!("a{c}b")).is_err(), "U+{:04X}", c as u32);
        }
        assert!(is_bidi_control('\u{61c}') && !is_bidi_control('a'));
        assert!(renamed("report.pdf", &"x".repeat(256)).is_err());
        assert!(renamed("report.pdf", &"x".repeat(251)).is_ok());
    }

    #[test]
    fn saved_names_are_safe() {
        assert_eq!(safe_file_name("a<b>c:d\"e/f\\g|h?i*j.txt"), "a_b_c_d_e_f_g_h_i_j.txt");
        assert_eq!(safe_file_name("  ..hidden.. "), "hidden");
        assert_eq!(safe_file_name(""), "attachment");
        assert_eq!(safe_file_name("..."), "attachment");
        // direction overrides are dropped, not turned into a look-alike
        assert_eq!(safe_file_name("report\u{202e}fdp.exe"), "reportfdp.exe");
        assert_eq!(safe_file_name("tab\there.txt"), "tab_here.txt");
        assert_eq!(safe_file_name("con.txt"), "_con.txt");
        assert_eq!(safe_file_name("NUL"), "_NUL");
        assert_eq!(safe_file_name("com1.tar.gz"), "_com1.tar.gz");
        assert_eq!(safe_file_name("com10.txt"), "com10.txt");
        assert_eq!(safe_file_name("console.txt"), "console.txt");
        assert_eq!(safe_file_name("CONIN$"), "_CONIN$");
        assert_eq!(safe_file_name("conout$.log"), "_conout$.log");
        assert_eq!(safe_file_name("COM\u{b9}.txt"), "_COM\u{b9}.txt");
        assert_eq!(safe_file_name("lpt\u{b3}"), "_lpt\u{b3}");
        assert_eq!(safe_file_name("COM0.txt"), "COM0.txt");
        assert_eq!(safe_file_name("COM\u{2074}.txt"), "COM\u{2074}.txt");
        // the Arabic letter mark is a direction control too
        assert_eq!(safe_file_name("a\u{61c}b.txt"), "ab.txt");
        let long = format!("{}.pdf", "x".repeat(300));
        let cut = safe_file_name(&long);
        assert_eq!(cut.len(), 255);
        assert!(cut.ends_with(".pdf"));
        assert_eq!(save_name(&json!({"name": "a/b.txt"})), "a_b.txt");
        // the cap counts UTF-8 bytes, never splits a character, keeps a sane extension
        let cut = safe_file_name(&format!("{}.pdf", "\u{e9}".repeat(200)));
        assert!(cut.len() <= 255 && cut.ends_with(".pdf") && cut.starts_with('\u{e9}'));
        let cut = safe_file_name(&format!("{}.docx", "\u{6f22}".repeat(100)));
        assert!(cut.len() <= 255 && cut.ends_with(".docx"));
        // an absurd extension is cut as well
        let cut = safe_file_name(&format!("{}.{}", "n".repeat(300), "e".repeat(400)));
        assert!(cut.len() <= 255 && cut.starts_with("nnn") && cut.rsplit_once('.').unwrap().1.len() < 16);
        let cut = safe_file_name(&format!("a.{}", "e".repeat(400)));
        assert!(cut.len() <= 255 && cut.starts_with("a.e"));
        // a cut extension does not end in a dot or space either
        let cut = safe_file_name(&format!("n.{}. .{}", "e".repeat(13), "x".repeat(300)));
        assert!(cut.len() <= 255 && !cut.ends_with('.') && !cut.ends_with(' '));
        let cut = safe_file_name(&format!("{}.{}.", "n".repeat(300), "e".repeat(300)));
        assert!(!cut.ends_with('.'));
        // a cut never leaves a trailing dot or space
        let cut = safe_file_name(&format!("{} .pdf", "d".repeat(253)));
        assert!(cut.len() <= 255 && !cut.contains(" ."));
        // a device name that also needs the cap keeps its prefix
        assert!(safe_file_name(&format!("con.{}", "x".repeat(300))).starts_with("_con."));
        assert_eq!(save_name(&json!({})), "attachment");
    }

    #[test]
    fn saved_names_follow_the_download_settings_unless_renamed() {
        use crate::dlnames::{Opts, Space};
        let o = Opts { space: Space::Underscore, lowercase: true, strip_diacritics: true, collapse: true };
        assert_eq!(save_name_with(&json!({"name": "Résumé  Final.PDF"}), &o), "resume_final.pdf");
        // a name the user typed is kept as typed (Bulwark renamedAttachmentFilename)
        assert_eq!(save_name_with(&json!({"name": "Résumé Final.PDF", "renamed": true}), &o), "Résumé Final.PDF");
        assert_eq!(save_name_with(&json!({"name": "con.txt"}), &Opts::default()), "_con.txt");
    }

    #[test]
    fn which_attachments_preview() {
        let img = json!({"type": "image/PNG", "size": 2000, "blobId": "b"});
        let big = json!({"type": "image/jpeg", "size": 20_000_000, "blobId": "b"});
        let svg = json!({"type": "image/svg+xml", "size": 10, "blobId": "b"});
        let pdf = json!({"type": "application/pdf", "size": 10, "blobId": "b"});
        assert!(previewable(&img));
        assert!(!previewable(&big) && !previewable(&svg) && !previewable(&pdf));
        assert!(wanted(&[pdf.clone(), img.clone()], true));
        assert!(!wanted(&[pdf.clone(), img], false));
        assert!(!wanted(&[pdf], true));
        assert_eq!(summary(&[json!({"size": 1500}), json!({"size": 500})]), "2 attachments · 2 KB");
    }

    #[test]
    fn thumbnails_are_square() {
        let mut png = vec![];
        let src = image::RgbaImage::from_pixel(120, 60, image::Rgba([10, 20, 30, 255]));
        image::DynamicImage::ImageRgba8(src).write_to(&mut std::io::Cursor::new(&mut png), image::ImageFormat::Png).unwrap();
        let t = thumbnail(&png).unwrap();
        assert_eq!((t.width(), t.height()), (THUMB, THUMB));
        assert!(thumbnail(b"not an image").is_none());
    }
}
