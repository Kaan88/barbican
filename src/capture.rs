//! Window capture that includes the WebView2 child window (the email body).
//! `PrintWindow(PW_CLIENTONLY | PW_RENDERFULLCONTENT)` asks DWM for the composed client area,
//! so it works for a window placed off-screen and picks up the cross-process WebView content
//! that Slint's own `take_snapshot` cannot see.
//! Used by the automation frame-burst probe and to freeze the reader under native overlays.

/// A captured image: RGBA8, top-down, `w * h * 4` bytes.
pub struct Frame {
    pub w: u32,
    pub h: u32,
    pub rgba: Vec<u8>,
}

/// The top-level window's HWND as an integer (0 when unavailable).
pub fn hwnd_of(win: &slint::Window) -> isize {
    use wry::raw_window_handle::{HasWindowHandle, RawWindowHandle};
    match win.window_handle().window_handle().map(|h| h.as_raw()) {
        Ok(RawWindowHandle::Win32(h)) => h.hwnd.get(),
        _ => 0,
    }
}

/// Capture the client area of `hwnd`, cropped to `crop` (physical px: x, y, w, h) when given,
/// and downscaled by `step` (1 = full size, 2 = every other pixel...).
#[cfg(windows)]
pub fn grab(hwnd: isize, crop: Option<(i32, i32, i32, i32)>, step: u32) -> Option<Frame> {
    use windows::Win32::Foundation::{HWND, RECT};
    use windows::Win32::Graphics::Gdi::*;
    use windows::Win32::Storage::Xps::{PrintWindow, PRINT_WINDOW_FLAGS};
    use windows::Win32::UI::WindowsAndMessaging::GetClientRect;
    if hwnd == 0 { return None; }
    let step = step.max(1) as i32;
    unsafe {
        let hwnd = HWND(hwnd as *mut core::ffi::c_void);
        let mut rc = RECT::default();
        GetClientRect(hwnd, &mut rc).ok()?;
        let (cw, ch) = (rc.right - rc.left, rc.bottom - rc.top);
        if cw <= 0 || ch <= 0 { return None; }
        let screen = GetDC(None);
        let mem = CreateCompatibleDC(Some(screen));
        let mut bmi = BITMAPINFO::default();
        bmi.bmiHeader.biSize = std::mem::size_of::<BITMAPINFOHEADER>() as u32;
        bmi.bmiHeader.biWidth = cw;
        bmi.bmiHeader.biHeight = -ch; // top-down
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = BI_RGB.0;
        let mut bits: *mut core::ffi::c_void = std::ptr::null_mut();
        let out = match CreateDIBSection(Some(mem), &bmi, DIB_RGB_COLORS, &mut bits, None, 0) {
            Ok(bmp) => {
                let old = SelectObject(mem, bmp.into());
                // PW_CLIENTONLY | PW_RENDERFULLCONTENT
                let ok = PrintWindow(hwnd, mem, PRINT_WINDOW_FLAGS(1 | 2)).as_bool();
                let res = if ok && !bits.is_null() {
                    let src = std::slice::from_raw_parts(bits as *const u8, (cw * ch * 4) as usize);
                    let (x0, y0, w0, h0) = crop.unwrap_or((0, 0, cw, ch));
                    let x0 = x0.clamp(0, cw);
                    let y0 = y0.clamp(0, ch);
                    let w0 = w0.min(cw - x0).max(0);
                    let h0 = h0.min(ch - y0).max(0);
                    let (ow, oh) = ((w0 + step - 1) / step, (h0 + step - 1) / step);
                    let mut rgba = Vec::with_capacity((ow * oh * 4) as usize);
                    for oy in 0..oh {
                        let row = ((y0 + oy * step) * cw) as usize * 4;
                        for ox in 0..ow {
                            let i = row + (x0 + ox * step) as usize * 4;
                            rgba.extend_from_slice(&[src[i + 2], src[i + 1], src[i], 255]);
                        }
                    }
                    (ow > 0 && oh > 0).then_some(Frame { w: ow as u32, h: oh as u32, rgba })
                } else { None };
                SelectObject(mem, old);
                let _ = DeleteObject(bmp.into());
                res
            }
            Err(_) => None,
        };
        let _ = DeleteDC(mem);
        ReleaseDC(None, screen);
        out
    }
}

#[cfg(not(windows))]
pub fn grab(_hwnd: isize, _crop: Option<(i32, i32, i32, i32)>, _step: u32) -> Option<Frame> { None }

pub fn write_png(path: &str, w: u32, h: u32, rgba: &[u8]) -> Result<(), String> {
    let f = std::fs::File::create(path).map_err(|e| e.to_string())?;
    let mut enc = png::Encoder::new(std::io::BufWriter::new(f), w, h);
    enc.set_color(png::ColorType::Rgba);
    enc.set_depth(png::BitDepth::Eight);
    let mut wr = enc.write_header().map_err(|e| e.to_string())?;
    wr.write_image_data(rgba).map_err(|e| e.to_string())
}
