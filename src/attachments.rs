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
