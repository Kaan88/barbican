//! Remembers the main window between runs: size, position, maximised state and the monitor
//! it was on, in `window.json` in the config folder. The file is local to this computer and
//! is not a preference (no sync, no import, no export): a rectangle only means something on
//! the monitors it was saved on.
//!
//! Units. The size is stored in logical pixels (the same window looks the same after a DPI
//! change); the position and the monitor rectangle are physical pixels in the virtual desktop
//! (that is what the OS places windows with); `scale` is the scale factor the window had.
//!
//! Which rectangle. Only the normal (restored) rectangle is saved. While the window is
//! maximised or minimised the last normal one is kept, so un-maximising after a restart goes
//! back to where the window was, and a minimised window is never restored minimised.
//!
//! Monitors. Slint cannot list monitors, and the winit window only exists once the event loop
//! runs. On Windows the work areas come from the Windows API (`platform::win`), so the
//! placement is applied before the window is shown. Elsewhere they come from the winit
//! window (`available_monitors`), so the placement is applied right after the loop starts.
//!
//! The pure part (parse, serialise, visibility, clamping) is `Saved`, `parse`, `to_json` and
//! `plan`; the rest is thin platform calls.
use crate::AppWindow;
use serde_json::{json, Value};
use slint::ComponentHandle;
use std::cell::{Cell, RefCell};
use std::path::{Path, PathBuf};
use std::time::Duration;

const FILE_VERSION: i64 = 1;
/// Smallest window a restore produces, logical pixels.
const MIN_W: f64 = 400.0;
const MIN_H: f64 = 300.0;
/// How much of the window must be on a monitor (physical pixels) to count as visible: enough
/// of the title bar to grab.
const VISIBLE_W: i64 = 120;
const VISIBLE_H: i64 = 60;
/// Quiet time after the last resize or move before the file is written.
const DEBOUNCE: Duration = Duration::from_millis(700);

/// A rectangle in physical pixels.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Rect { pub x: i32, pub y: i32, pub w: i32, pub h: i32 }

impl Rect {
    fn right(&self) -> i64 { self.x as i64 + self.w as i64 }
    fn bottom(&self) -> i64 { self.y as i64 + self.h as i64 }
    /// Width and height of the overlap with `o` (0 when they do not touch).
    fn overlap(&self, o: &Rect) -> (i64, i64) {
        let w = self.right().min(o.right()) - (self.x as i64).max(o.x as i64);
        let h = self.bottom().min(o.bottom()) - (self.y as i64).max(o.y as i64);
        (w.max(0), h.max(0))
    }
}

/// What `window.json` holds.
#[derive(Clone, Debug, PartialEq)]
pub struct Saved {
    /// Outer position of the normal rectangle, physical pixels.
    pub x: i32,
    pub y: i32,
    /// Inner size of the normal rectangle, logical pixels.
    pub width: f64,
    pub height: f64,
    /// Scale factor when it was saved.
    pub scale: f64,
    pub maximized: bool,
    /// Work area of the monitor the window was mostly on, physical pixels.
    pub monitor: Option<Rect>,
}

impl Saved {
    /// The window's rectangle in physical pixels at the saved scale.
    fn physical(&self) -> Rect {
        Rect { x: self.x, y: self.y, w: (self.width * self.scale).round() as i32, h: (self.height * self.scale).round() as i32 }
    }

    fn is_sane(&self) -> bool {
        let finite = self.width.is_finite() && self.height.is_finite() && self.scale.is_finite();
        finite
            && (0.25..=16.0).contains(&self.scale)
            && (100.0..=30000.0).contains(&self.width)
            && (100.0..=30000.0).contains(&self.height)
            && self.x > -30000 && self.y > -30000 && self.x < 100_000 && self.y < 100_000
    }
}

/// Reads `window.json` text. None for anything that is not a usable rectangle (corrupt file,
/// another version, zero or absurd size, the -32000 position Windows gives minimised windows).
pub fn parse(text: &str) -> Option<Saved> {
    let v: Value = serde_json::from_str(text).ok()?;
    if v["version"].as_i64()? != FILE_VERSION { return None; }
    let int = |k: &str| v[k].as_i64().and_then(|n| i32::try_from(n).ok());
    let (x, y) = (int("x")?, int("y")?);
    let (width, height, scale) = (v["width"].as_f64()?, v["height"].as_f64()?, v["scale"].as_f64()?);
    let monitor = v["monitor"].as_object().and_then(|m| {
        let g = |k: &str| m.get(k).and_then(|n| n.as_i64()).and_then(|n| i32::try_from(n).ok());
        Some(Rect { x: g("x")?, y: g("y")?, w: g("w")?, h: g("h")? }).filter(|r| r.w > 0 && r.h > 0)
    });
    let s = Saved { x, y, width, height, scale, maximized: v["maximized"].as_bool().unwrap_or(false), monitor };
    s.is_sane().then_some(s)
}

pub fn to_json(s: &Saved) -> String {
    let mut v = json!({
        "version": FILE_VERSION, "x": s.x, "y": s.y, "width": s.width, "height": s.height,
        "scale": s.scale, "maximized": s.maximized,
    });
    if let Some(m) = s.monitor { v["monitor"] = json!({"x": m.x, "y": m.y, "w": m.w, "h": m.h}); }
    serde_json::to_string_pretty(&v).unwrap_or_default()
}

/// What to do with the window at start.
#[derive(Clone, Debug, PartialEq)]
pub struct Placement {
    /// Logical size and physical position; None keeps the default.
    pub size: Option<(f64, f64)>,
    pub pos: Option<(i32, i32)>,
    pub maximized: bool,
}

/// Where the saved window goes on the monitors that exist now (their work areas, physical
/// pixels). None when nothing may be restored (no monitors known), which keeps the default.
/// A rectangle that is not at least partly on a monitor gives the default size and position
/// (still maximised when it was); one that sticks out is moved onto its monitor and, when it
/// is bigger than the monitor, shrunk.
pub fn plan(s: &Saved, monitors: &[Rect]) -> Option<Placement> {
    if monitors.is_empty() { return None; }
    let win = s.physical();
    let need = (VISIBLE_W.min(win.w as i64), VISIBLE_H.min(win.h as i64));
    // the monitor with the most overlap; a tie goes to the monitor it was saved on
    let best = monitors.iter()
        .filter_map(|m| {
            let (w, h) = win.overlap(m);
            (w >= need.0 && h >= need.1).then_some((*m, w * h, Some(*m) == s.monitor))
        })
        .max_by_key(|(_, area, saved)| (*area, *saved))
        .map(|(m, _, _)| m);
    let Some(m) = best else {
        return Some(Placement { size: None, pos: None, maximized: s.maximized });
    };
    // logical size, no bigger than the monitor and no smaller than the minimum
    let max_w = (m.w as f64 / s.scale).max(MIN_W);
    let max_h = (m.h as f64 / s.scale).max(MIN_H);
    let (width, height) = (s.width.clamp(MIN_W, max_w).round(), s.height.clamp(MIN_H, max_h).round());
    let (pw, ph) = ((width * s.scale).round() as i64, (height * s.scale).round() as i64);
    let x = (s.x as i64).clamp(m.x as i64, (m.right() - pw).max(m.x as i64));
    let y = (s.y as i64).clamp(m.y as i64, (m.bottom() - ph).max(m.y as i64));
    Some(Placement { size: Some((width, height)), pos: Some((x as i32, y as i32)), maximized: s.maximized })
}

fn path() -> PathBuf { crate::config_dir().join("window.json") }

fn read_file(p: &Path) -> Option<Saved> { parse(&std::fs::read_to_string(p).ok()?) }

/// Written next to the file and renamed over it, so a crash never leaves half a file.
fn write_file(p: &Path, s: &Saved) {
    if let Some(d) = p.parent() { let _ = std::fs::create_dir_all(d); }
    let tmp = p.with_extension("json.tmp");
    if std::fs::write(&tmp, to_json(s)).is_ok() && std::fs::rename(&tmp, p).is_err() {
        let _ = std::fs::remove_file(&tmp);
    }
}

// ---------------- the thin platform part ----------------

thread_local! {
    /// The last normal rectangle seen (never a maximised or minimised one).
    static NORMAL: RefCell<Option<Saved>> = const { RefCell::new(None) };
    static MAXIMIZED: Cell<bool> = const { Cell::new(false) };
    static DEBOUNCER: crate::ui_types::Timer = crate::ui_types::Timer::default();
}

/// Automation runs off-screen and must leave the user's window file alone.
fn off() -> bool { crate::automation::enabled() }

/// Work areas of the monitors, physical pixels (empty when they cannot be known yet).
fn monitors(ui: &AppWindow) -> Vec<Rect> {
    #[cfg(windows)]
    {
        let _ = ui;
        crate::platform::win::monitor_work_areas().into_iter().map(|(x, y, w, h)| Rect { x, y, w, h }).collect()
    }
    #[cfg(not(windows))]
    {
        crate::ui_types::with_winit_window(ui.window(), |w| {
            w.available_monitors().map(|m| {
                let (p, s) = (m.position(), m.size());
                Rect { x: p.x, y: p.y, w: s.width as i32, h: s.height as i32 }
            }).collect()
        }).unwrap_or_default()
    }
}

fn apply(ui: &AppWindow, p: &Placement) {
    let win = ui.window();
    if let Some((w, h)) = p.size { win.set_size(slint::LogicalSize::new(w as f32, h as f32)); }
    if let Some((x, y)) = p.pos { win.set_position(slint::PhysicalPosition::new(x, y)); }
    if p.maximized { win.set_maximized(true); }
}

/// Put the window where it was. Call before the window is shown. Nothing saved, a corrupt
/// file or a rectangle that is off every monitor leaves the default size and place.
pub fn restore(ui: &AppWindow) {
    if off() { return; }
    let Some(saved) = read_file(&path()) else { return };
    // the saved rectangle is the one to keep until the window is moved
    NORMAL.with(|n| *n.borrow_mut() = Some(Saved { maximized: false, ..saved.clone() }));
    MAXIMIZED.with(|m| m.set(saved.maximized));
    let mons = monitors(ui);
    if !mons.is_empty() {
        if let Some(p) = plan(&saved, &mons) { apply(ui, &p); }
        return;
    }
    // no monitor list before the window exists (not Windows): once the loop runs
    let w = ui.as_weak();
    crate::ui_types::Timer::single_shot(Duration::ZERO, move || {
        let Some(u) = w.upgrade() else { return };
        if let Some(p) = plan(&saved, &monitors(&u)) { apply(&u, &p); }
    });
}

/// Read the window's current state into the cache (a no-op while minimised).
fn snapshot(ui: &AppWindow) {
    let win = ui.window();
    if win.is_minimized() { return; }
    if win.is_maximized() { MAXIMIZED.with(|m| m.set(true)); return; }
    MAXIMIZED.with(|m| m.set(false));
    let (pos, size, scale) = (win.position(), win.size(), win.scale_factor() as f64);
    if size.width == 0 || size.height == 0 || !(0.25..=16.0).contains(&scale) { return; }
    let phys = Rect { x: pos.x, y: pos.y, w: size.width as i32, h: size.height as i32 };
    let monitor = monitors(ui).into_iter()
        .map(|m| { let (w, h) = phys.overlap(&m); (m, w * h) })
        .filter(|(_, a)| *a > 0).max_by_key(|(_, a)| *a).map(|(m, _)| m);
    let s = Saved { x: pos.x, y: pos.y, width: size.width as f64 / scale, height: size.height as f64 / scale, scale, maximized: false, monitor };
    // Windows reports a minimised window at about -32000: not a place to come back to
    if s.is_sane() { NORMAL.with(|n| *n.borrow_mut() = Some(s)); }
}

/// Write the window's state now (closing, hiding to the tray, quitting).
pub fn save(ui: &AppWindow) {
    if off() { return; }
    snapshot(ui);
    if let Some(s) = NORMAL.with(|n| n.borrow().clone()) {
        write_file(&path(), &Saved { maximized: MAXIMIZED.with(|m| m.get()), ..s });
    }
}

/// From the window's winit event filter (motion::install_filter): a resize or move writes
/// the file once it has been quiet for a moment.
pub fn event(ui: &AppWindow, ev: &crate::ui_types::winit::event::WindowEvent) {
    use crate::ui_types::winit::event::WindowEvent::{Moved, Resized};
    if off() || !matches!(ev, Resized(_) | Moved(_)) { return; }
    let w = ui.as_weak();
    DEBOUNCER.with(|t| t.start(crate::ui_types::TimerMode::SingleShot, DEBOUNCE, move || { if let Some(u) = w.upgrade() { save(&u); } }));
}

#[cfg(test)]
mod tests {
    use super::*;

    const M1: Rect = Rect { x: 0, y: 0, w: 1920, h: 1040 };
    const M2: Rect = Rect { x: 1920, y: 0, w: 2560, h: 1400 };
    const LEFT: Rect = Rect { x: -1280, y: 100, w: 1280, h: 924 };

    fn saved(x: i32, y: i32, w: f64, h: f64) -> Saved {
        Saved { x, y, width: w, height: h, scale: 1.0, maximized: false, monitor: Some(M1) }
    }

    #[test]
    fn json_round_trips() {
        let s = Saved { x: -1200, y: 150, width: 1100.5, height: 700.0, scale: 1.25, maximized: true, monitor: Some(LEFT) };
        assert_eq!(parse(&to_json(&s)), Some(s));
        let none = Saved { monitor: None, ..saved(10, 20, 800.0, 600.0) };
        assert_eq!(parse(&to_json(&none)), Some(none));
    }

    #[test]
    fn bad_files_are_no_rectangle() {
        for t in ["", "not json", "{}", "[]", "null",
            r#"{"version":2,"x":0,"y":0,"width":800,"height":600,"scale":1}"#,
            r#"{"version":1,"x":0,"y":0,"width":0,"height":600,"scale":1}"#,
            r#"{"version":1,"x":0,"y":0,"width":800,"height":-5,"scale":1}"#,
            r#"{"version":1,"x":0,"y":0,"width":800,"height":600,"scale":0}"#,
            r#"{"version":1,"x":0,"y":0,"width":800,"height":600}"#,
            r#"{"version":1,"x":"0","y":0,"width":800,"height":600,"scale":1}"#,
            r#"{"version":1,"x":99999999999,"y":0,"width":800,"height":600,"scale":1}"#,
            // where Windows puts a minimised window
            r#"{"version":1,"x":-32000,"y":-32000,"width":800,"height":600,"scale":1}"#,
            r#"{"version":1,"x":0,"y":0,"width":1e999,"height":600,"scale":1}"#] {
            assert_eq!(parse(t), None, "{t}");
        }
        // a broken monitor only loses the monitor
        let s = parse(r#"{"version":1,"x":5,"y":6,"width":800,"height":600,"scale":1,"monitor":{"x":0}}"#).unwrap();
        assert_eq!((s.x, s.monitor), (5, None));
        let s = parse(r#"{"version":1,"x":5,"y":6,"width":800,"height":600,"scale":1,"monitor":{"x":0,"y":0,"w":0,"h":9}}"#).unwrap();
        assert_eq!(s.monitor, None);
    }

    #[test]
    fn a_visible_window_stays_where_it_was() {
        let p = plan(&saved(200, 100, 1400.0, 900.0), &[M1]).unwrap();
        assert_eq!(p, Placement { size: Some((1400.0, 900.0)), pos: Some((200, 100)), maximized: false });
    }

    #[test]
    fn no_monitors_known_keeps_the_default() {
        assert_eq!(plan(&saved(0, 0, 800.0, 600.0), &[]), None);
    }

    #[test]
    fn an_unplugged_monitor_gives_the_default() {
        // saved on the second monitor, which is gone
        let s = Saved { monitor: Some(M2), ..saved(2500, 200, 1400.0, 900.0) };
        let p = plan(&s, &[M1]).unwrap();
        assert_eq!(p, Placement { size: None, pos: None, maximized: false });
        // maximised stays maximised, on the default size
        let p = plan(&Saved { maximized: true, ..s }, &[M1]).unwrap();
        assert_eq!(p, Placement { size: None, pos: None, maximized: true });
    }

    #[test]
    fn a_monitor_to_the_left_has_negative_coordinates() {
        let s = Saved { monitor: Some(LEFT), ..saved(-1200, 150, 1000.0, 700.0) };
        let p = plan(&s, &[M1, LEFT]).unwrap();
        assert_eq!(p.pos, Some((-1200, 150)));
        // the same rectangle without that monitor is off screen
        assert_eq!(plan(&s, &[M1]).unwrap().pos, None);
    }

    #[test]
    fn a_sliver_on_screen_is_not_visible() {
        // 20 px of the window left on the monitor
        assert_eq!(plan(&saved(-980, 100, 1000.0, 700.0), &[M1]).unwrap().pos, None);
        // below the work area
        assert_eq!(plan(&saved(100, 1020, 1000.0, 700.0), &[M1]).unwrap().pos, None);
    }

    #[test]
    fn a_window_sticking_out_is_moved_onto_its_monitor() {
        // half off the right edge
        let p = plan(&saved(1400, 100, 1000.0, 700.0), &[M1]).unwrap();
        assert_eq!(p.pos, Some((920, 100)));
        // off the bottom, and above the top
        assert_eq!(plan(&saved(100, 700, 1000.0, 700.0), &[M1]).unwrap().pos, Some((100, 340)));
        assert_eq!(plan(&saved(100, -400, 1000.0, 700.0), &[M1]).unwrap().pos, Some((100, 0)));
    }

    #[test]
    fn a_window_bigger_than_the_monitor_shrinks_to_it() {
        // a big window from a 2560x1400 monitor, mostly on the smaller one now
        let p = plan(&saved(0, 0, 2500.0, 1380.0), &[M1]).unwrap();
        assert_eq!((p.size, p.pos), (Some((1920.0, 1040.0)), Some((0, 0))));
    }

    #[test]
    fn dpi_keeps_the_logical_size() {
        // 200%: 1000x700 logical is 2000x1400 physical, on a 2560x1400 monitor
        let s = Saved { scale: 2.0, monitor: Some(M2), ..saved(1920, 0, 1000.0, 700.0) };
        let p = plan(&s, &[M1, M2]).unwrap();
        assert_eq!((p.size, p.pos), (Some((1000.0, 700.0)), Some((1920, 0))));
        // the same window on a 1920x1040 monitor is 960x520 logical at most
        let s = Saved { scale: 2.0, ..saved(0, 0, 1000.0, 700.0) };
        let p = plan(&s, &[M1]).unwrap();
        assert_eq!((p.size, p.pos), (Some((960.0, 520.0)), Some((0, 0))));
    }

    #[test]
    fn the_overlap_decides_the_monitor_and_a_tie_goes_to_the_saved_one() {
        let (a, b) = (Rect { x: 0, y: 0, w: 1000, h: 800 }, Rect { x: 1000, y: 0, w: 1000, h: 800 });
        // 300 px on a, 700 px on b: b wins and the window moves to fit b
        let s = Saved { monitor: Some(a), ..saved(700, 50, 1000.0, 500.0) };
        assert_eq!(plan(&s, &[a, b]).unwrap().pos, Some((1000, 50)));
        // 500/500: the monitor it was saved on wins
        let s = Saved { monitor: Some(b), ..saved(500, 50, 1000.0, 500.0) };
        assert_eq!(plan(&s, &[a, b]).unwrap().pos, Some((1000, 50)));
        let s = Saved { monitor: Some(a), ..saved(500, 50, 1000.0, 500.0) };
        assert_eq!(plan(&s, &[a, b]).unwrap().pos, Some((0, 50)));
    }

    #[test]
    fn a_tiny_saved_size_is_raised_to_the_minimum() {
        let p = plan(&saved(10, 10, 150.0, 120.0), &[M1]).unwrap();
        assert_eq!(p.size, Some((MIN_W, MIN_H)));
    }

    #[test]
    fn the_file_is_written_and_read_back() {
        let dir = std::env::temp_dir().join(format!("barbican-winstate-{}", std::process::id()));
        let p = dir.join("window.json");
        assert_eq!(read_file(&p), None); // missing
        let s = saved(30, 40, 1200.0, 800.0);
        write_file(&p, &s);
        assert_eq!(read_file(&p), Some(s.clone()));
        assert!(!dir.join("window.json.tmp").exists());
        write_file(&p, &Saved { maximized: true, ..s });
        assert!(read_file(&p).unwrap().maximized);
        std::fs::write(&p, "{ truncated").unwrap();
        assert_eq!(read_file(&p), None); // corrupt
        let _ = std::fs::remove_dir_all(&dir);
    }
}
