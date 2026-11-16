//! "Light" email renderer: Blitz (pure Rust HTML/CSS engine using Firefox's Stylo + Taffy),
//! painted on the CPU with vello_cpu into an image shown natively by Slint.
//! No JavaScript, no network: remote images never load in this mode.
use anyrender_vello_cpu::VelloCpuImageRenderer;
use blitz_dom::DocumentConfig;
use blitz_html::HtmlDocument;
use blitz_traits::shell::{ColorScheme, Viewport};
use std::cell::RefCell;

thread_local! {
    static DOC: RefCell<Option<HtmlDocument>> = const { RefCell::new(None) };
}

const MAX_HEIGHT: u32 = 32000;

/// Render `html` at `width` logical px. Returns RGBA pixels, physical width and height.
pub fn render(html: &str, width: f32, scale: f32) -> (Vec<u8>, u32, u32) {
    let pw = (width * scale).max(100.0) as u32;
    let mut doc = HtmlDocument::from_html(html, DocumentConfig {
        viewport: Some(Viewport::new(pw, 600, scale, ColorScheme::Light)),
        ..Default::default()
    });
    doc.resolve(0.0);
    let content_h = doc.root_element().final_layout().size.height.max(1.0);
    let ph = ((content_h * scale).ceil() as u32).clamp(1, MAX_HEIGHT);
    doc.set_viewport(Viewport::new(pw, ph, scale, ColorScheme::Light));
    doc.resolve(0.0);
    let buf = anyrender::render_to_buffer::<VelloCpuImageRenderer, _>(
        |scene| blitz_paint::paint_scene(scene, &mut doc, scale as f64, pw, ph, 0, 0),
        pw,
        ph,
    );
    DOC.with(|d| *d.borrow_mut() = Some(doc));
    (buf, pw, ph)
}

/// The link under a point (logical px, relative to the rendered image), if any.
pub fn link_at(x: f32, y: f32) -> Option<String> {
    DOC.with(|d| {
        let d = d.borrow();
        let doc = d.as_ref()?;
        let mut id = doc.element_from_point(x, y)?;
        loop {
            let n = doc.get_node(id)?;
            if let Some(e) = n.element_data() {
                if &*e.name.local == "a" {
                    if let Some(a) = e.attrs().iter().find(|a| &*a.name.local == "href") {
                        return Some(a.value.clone());
                    }
                }
            }
            id = n.parent?;
        }
    })
}

pub fn clear() {
    DOC.with(|d| *d.borrow_mut() = None);
}

#[cfg(test)]
mod tests {
    #[test]
    fn renders_email_html() {
        let html = r#"<html><body style="margin:16px"><table width="100%"><tr><td style="background:#db2d54;color:#fff;padding:20px">Receipt</td></tr></table>
            <p>Hello <a href="https://example.com/x">link</a></p><p style="height:900px">tall</p></body></html>"#;
        let (buf, w, h) = super::render(html, 600.0, 1.0);
        assert_eq!(w, 600);
        assert!(h > 900, "height {h}");
        assert_eq!(buf.len(), (w * h * 4) as usize);
        // the red header cell is painted
        let px = &buf[((40 * w + 100) * 4) as usize..][..4];
        assert!(px[0] > 180 && px[1] < 90, "pixel {px:?}");
        assert!(super::link_at(5.0, 5.0).is_none());
    }
}
