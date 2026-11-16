//! Barbican's icon drawn from ui/barbican.svg at any size, with an optional unread badge,
//! for the tray, the taskbar overlay and the installers' icon files. The badge digits are
//! stroked segments, so no font has to be found on the system.
const APP_SVG: &str = include_str!("../ui/barbican.svg");

/// Amber = attention in the design language (design/README.md).
const BADGE_FILL: &str = "#fbbf24";
const BADGE_TEXT: &str = "#3b2503";

/// What the badge says: nothing for 0, the count up to 99, then "99+".
pub fn badge_text(n: u32) -> String {
    match n { 0 => String::new(), 1..=99 => n.to_string(), _ => "99+".into() }
}

/// Seven-segment strokes on a 4 x 7 cell: a top, b top right, c bottom right, d bottom,
/// e bottom left, f top left, g middle.
fn segments(ch: char) -> &'static [u8] {
    match ch {
        '0' => b"abcdef", '1' => b"bc", '2' => b"abged", '3' => b"abgcd", '4' => b"fgbc",
        '5' => b"afgcd", '6' => b"afgedc", '7' => b"abc", '8' => b"abcdefg", '9' => b"abcdfg",
        _ => b"",
    }
}

fn glyph_path(ch: char, x: f32, y: f32, w: f32, h: f32) -> String {
    let m = h / 2.0;
    if ch == '+' {
        return format!("M{} {}H{}M{} {}V{}", x, y + m, x + w, x + w / 2.0, y + m - w / 2.0, y + m + w / 2.0);
    }
    segments(ch).iter().map(|s| {
        let ((x0, y0), (x1, y1)) = match s {
            b'a' => ((0.0, 0.0), (w, 0.0)), b'b' => ((w, 0.0), (w, m)), b'c' => ((w, m), (w, h)),
            b'd' => ((0.0, h), (w, h)), b'e' => ((0.0, m), (0.0, h)), b'f' => ((0.0, 0.0), (0.0, m)),
            _ => ((0.0, m), (w, m)),
        };
        format!("M{} {}L{} {}", x + x0, y + y0, x + x1, y + y1)
    }).collect()
}

/// SVG of a badge circle with `text`, centred at (cx, cy) with radius r (64-unit space).
fn badge_svg(text: &str, cx: f32, cy: f32, r: f32) -> String {
    let n = text.chars().count().max(1) as f32;
    let h = r * if n > 2.0 { 0.8 } else { 1.05 };
    let w = h * 0.5;
    let gap = w * 0.45;
    let total = n * w + (n - 1.0) * gap;
    let (mut x, y) = (cx - total / 2.0, cy - h / 2.0);
    let mut d = String::new();
    for ch in text.chars() {
        d.push_str(&glyph_path(ch, x, y, w, h));
        x += w + gap;
    }
    format!("<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{r}\" fill=\"{BADGE_FILL}\" stroke=\"#ffffff\" stroke-width=\"{}\"/>\
        <path d=\"{d}\" fill=\"none\" stroke=\"{BADGE_TEXT}\" stroke-width=\"{}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>",
        r * 0.14, h * 0.17)
}

/// The app icon with an unread badge in its top-right corner (none for 0).
pub fn app_svg(unread: u32) -> String {
    let text = badge_text(unread);
    if text.is_empty() { return APP_SVG.to_string(); }
    let badge = badge_svg(&text, 46.0, 18.0, 17.0);
    APP_SVG.replacen("</svg>", &format!("{badge}</svg>"), 1)
}

/// Only the badge, filling the square: Windows' taskbar overlay.
pub fn overlay_svg(unread: u32) -> String {
    format!("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 64 64\">{}</svg>", badge_svg(&badge_text(unread), 32.0, 32.0, 29.0))
}

/// Straight (not premultiplied) RGBA of an SVG at `size` x `size`.
pub fn render(svg: &str, size: u32) -> Option<Vec<u8>> {
    use resvg::{tiny_skia, usvg};
    let tree = usvg::Tree::from_str(svg, &usvg::Options::default()).ok()?;
    let mut pixmap = tiny_skia::Pixmap::new(size, size)?;
    let s = tree.size();
    resvg::render(&tree, tiny_skia::Transform::from_scale(size as f32 / s.width(), size as f32 / s.height()), &mut pixmap.as_mut());
    Some(pixmap.pixels().iter().flat_map(|p| { let c = p.demultiply(); [c.red(), c.green(), c.blue(), c.alpha()] }).collect())
}

/// PNG bytes of square RGBA (icon files for the installers).
#[cfg_attr(not(test), allow(dead_code))]
pub fn png(rgba: &[u8], size: u32) -> Vec<u8> {
    let mut out = vec![];
    {
        let mut enc = png::Encoder::new(&mut out, size, size);
        enc.set_color(png::ColorType::Rgba);
        enc.set_depth(png::BitDepth::Eight);
        if let Ok(mut w) = enc.write_header() { let _ = w.write_image_data(rgba); }
    }
    out
}

/// A Windows .ico holding PNG images (Vista and later read these).
#[cfg_attr(not(test), allow(dead_code))]
pub fn ico(images: &[(u32, Vec<u8>)]) -> Vec<u8> {
    let mut out = vec![0, 0, 1, 0];
    out.extend((images.len() as u16).to_le_bytes());
    let mut offset = 6 + 16 * images.len() as u32;
    for (size, data) in images {
        let b = if *size >= 256 { 0 } else { *size as u8 };
        out.extend([b, b, 0, 0]);
        out.extend(1u16.to_le_bytes());
        out.extend(32u16.to_le_bytes());
        out.extend((data.len() as u32).to_le_bytes());
        out.extend(offset.to_le_bytes());
        offset += data.len() as u32;
    }
    for (_, data) in images { out.extend(data); }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn badge_texts() {
        assert_eq!(badge_text(0), "");
        assert_eq!(badge_text(7), "7");
        assert_eq!(badge_text(42), "42");
        assert_eq!(badge_text(100), "99+");
    }

    #[test]
    fn renders_icon_and_badge() {
        let plain = render(&app_svg(0), 32).unwrap();
        assert_eq!(plain.len(), 32 * 32 * 4);
        // the rose tile in the middle, transparent corner
        let px = |b: &[u8], x: usize, y: usize| b[(y * 32 + x) * 4..(y * 32 + x) * 4 + 4].to_vec();
        assert_eq!(px(&plain, 0, 0)[3], 0);
        assert_eq!(&px(&plain, 4, 16)[..3], &[0xdb, 0x2d, 0x54]);
        let badged = render(&app_svg(3), 32).unwrap();
        assert_ne!(plain, badged);
        // amber appears in the top-right corner
        let corner = px(&badged, 26, 6);
        assert!(corner[0] > 200 && corner[1] > 150 && corner[2] < 120, "{corner:?}");
        let overlay = render(&overlay_svg(12), 16).unwrap();
        assert!(overlay.chunks(4).any(|p| p[3] > 0));
    }

    #[test]
    fn ico_layout() {
        let data = ico(&[(16, vec![1, 2, 3]), (256, vec![4, 5])]);
        assert_eq!(&data[..6], &[0, 0, 1, 0, 2, 0]);
        assert_eq!(data[6], 16);
        assert_eq!(data[22], 0); // 256 is written as 0
        assert_eq!(u32::from_le_bytes(data[18..22].try_into().unwrap()), 38);
        assert_eq!(&data[38..], &[1, 2, 3, 4, 5]);
    }

    /// Writes packaging/icons from the SVG: `cargo test --release write_icon_assets -- --ignored`.
    #[test]
    #[ignore]
    fn write_icon_assets() {
        let dir = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("packaging/icons");
        std::fs::create_dir_all(&dir).unwrap();
        let mut ico_imgs = vec![];
        for size in [16, 24, 32, 48, 64, 128, 256, 512] {
            let p = png(&render(APP_SVG, size).unwrap(), size);
            std::fs::write(dir.join(format!("barbican-{size}.png")), &p).unwrap();
            if size <= 256 { ico_imgs.push((size, p)); }
        }
        std::fs::write(dir.join("barbican.ico"), ico(&ico_imgs)).unwrap();
        std::fs::copy(std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("ui/barbican.svg"), dir.join("barbican.svg")).unwrap();
    }
}
