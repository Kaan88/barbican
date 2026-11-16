//! Test automation without touching the real mouse, keyboard or screen.
//! Enabled only when BARBICAN_AUTOMATION=1. Listens on 127.0.0.1:<BARBICAN_AUTOMATION_PORT|8792>
//! for one command per line; input is injected into Slint's own event queue
//! (`Window::dispatch_event`) and screenshots come from Slint's renderer
//! (`Window::take_snapshot`), so the window can stay off-screen and unfocused.
//!
//! Commands (logical pixels):
//!   click X Y | rclick X Y | dclick X Y | move X Y | scroll X Y DY
//!   type TEXT            (sent as key presses)
//!   key Enter|Escape|Tab|Backspace|Delete|Up|Down|Left|Right|Home|End (with Ctrl+/Shift+/Alt+ prefixes)
//!   drag X1 Y1 X2 Y2
//!   shot PATH.png        (window contents; the WebView2 email body is a separate window and not included)
//!   size                 (window size)
//!   resize W H           (resize the off-screen window)
//!   ui bento|nordic dark|light [more|normal]   (style, theme and contrast in memory only, never saved)
//!   wv-eval JS / wv-result (run JS in the email WebView, then read its JSON result)
//!   pick PATH[|PATH...]  (answer the next file-open picker with these files instead of a dialog)
//!   probe-start          (frame probe: from now on record every rendered frame's start/end time
//!                        and the time each command is dispatched; nothing is captured or copied)
//!   probe-stop           (stop and return one JSON line: {"frames":[[start,end],…],"events":[[t,cmd],…]},
//!                        times in ms since probe-start; analyse with tools/motion.py)
//!   every MS N CMD       (run CMD N times, one every MS ms, from a UI-thread timer; returns at once)
//!   motion reduce|normal|rapid|calm   (Motion.reduce / Motion.rapid in memory only, never saved)
//!   toast [TEXT]         (show a toast, or clear it without TEXT; UI only)
use crate::AppWindow;
use slint::platform::{Key, PointerEventButton, WindowEvent};
use slint::{ComponentHandle, LogicalPosition, SharedString};
use std::io::{BufRead, BufReader, Write};
use std::net::TcpListener;
use std::sync::mpsc;

thread_local! {
    static PICK: std::cell::RefCell<Option<Vec<std::path::PathBuf>>> = const { std::cell::RefCell::new(None) };
}

/// Files for a file-open picker: the paths queued by the `pick` command when automation is on,
/// otherwise the native dialog.
pub fn pick_files() -> Option<Vec<std::path::PathBuf>> {
    if enabled() {
        if let Some(v) = PICK.with(|p| p.borrow_mut().take()) { return Some(v); }
    }
    rfd::FileDialog::new().pick_files()
}

pub fn enabled() -> bool {
    std::env::var("BARBICAN_AUTOMATION").is_ok_and(|v| v == "1")
}

pub fn start(ui: &AppWindow) {
    if !enabled() { return; }
    let port: u16 = std::env::var("BARBICAN_AUTOMATION_PORT").ok().and_then(|p| p.parse().ok()).unwrap_or(8792);
    let w = ui.as_weak();
    std::thread::spawn(move || {
        let Ok(listener) = TcpListener::bind(("127.0.0.1", port)) else { return };
        for stream in listener.incoming().flatten() {
            let mut out = stream.try_clone().unwrap();
            for line in BufReader::new(stream).lines().map_while(Result::ok) {
                let (tx, rx) = mpsc::channel();
                let w2 = w.clone();
                let cmd = line.clone();
                let _ = w2.upgrade_in_event_loop(move |u| { let _ = tx.send(run(&u, &cmd)); });
                let reply = rx.recv_timeout(std::time::Duration::from_secs(10)).unwrap_or_else(|_| "timeout".into());
                let _ = writeln!(out, "{reply}");
            }
        }
    });
}

fn pos(a: &[&str], i: usize) -> f32 {
    a.get(i).and_then(|v| v.parse().ok()).unwrap_or(0.0)
}

/// The frame probe: rendered frames (start, end) and dispatched commands, in ms since `t0`.
/// Lives on the UI thread, where the window events, the rendering and the commands all run.
struct Probe {
    t0: std::time::Instant,
    on: bool,
    frames: Vec<(f64, f64)>,
    events: Vec<(f64, String)>,
}

thread_local! {
    static PROBE: std::cell::RefCell<Probe> = std::cell::RefCell::new(Probe {
        t0: std::time::Instant::now(), on: false, frames: vec![], events: vec![],
    });
}

/// The probe is recording (checked first, so a frame costs one atomic load when it is not).
static PROBING: std::sync::atomic::AtomicBool = std::sync::atomic::AtomicBool::new(false);

fn probe_ms(p: &Probe) -> f64 { p.t0.elapsed().as_secs_f64() * 1000.0 }

/// From the window's winit event filter (motion::install_filter). A frame starts with RedrawRequested (Slint renders
/// and presents right after the filter returns); it has ended when a zero-delay timer queued
/// at its start fires, on the next turn of the event loop. Only does anything while probing.
pub fn winit_event(ev: &slint::winit_030::winit::event::WindowEvent) {
    if !PROBING.load(std::sync::atomic::Ordering::Relaxed) { return; }
    if !matches!(ev, slint::winit_030::winit::event::WindowEvent::RedrawRequested) { return; }
    let idx = PROBE.with(|p| {
        let mut p = p.borrow_mut();
        if !p.on { return None; }
        let t = probe_ms(&p);
        p.frames.push((t, t));
        Some(p.frames.len() - 1)
    });
    if let Some(i) = idx {
        slint::Timer::single_shot(std::time::Duration::ZERO, move || PROBE.with(|p| {
            let mut p = p.borrow_mut();
            let t = probe_ms(&p);
            if let Some(f) = p.frames.get_mut(i) { f.1 = t; }
        }));
    }
}

fn probe_start(u: &AppWindow) -> String {
    use std::sync::atomic::Ordering;
    // make sure the window's event filter (which feeds winit_event) is the current one
    crate::motion::install_filter(u);
    PROBE.with(|p| {
        let mut p = p.borrow_mut();
        p.on = true;
        p.t0 = std::time::Instant::now();
        p.frames.clear();
        p.events.clear();
    });
    PROBING.store(true, Ordering::Relaxed);
    "ok".into()
}

fn probe_stop() -> String {
    PROBING.store(false, std::sync::atomic::Ordering::Relaxed);
    PROBE.with(|p| {
        let mut p = p.borrow_mut();
        p.on = false;
        let frames: Vec<serde_json::Value> = p.frames.iter().map(|(a, b)| serde_json::json!([(a * 100.0).round() / 100.0, (b * 100.0).round() / 100.0])).collect();
        let events: Vec<serde_json::Value> = p.events.iter().map(|(t, e)| serde_json::json!([(t * 100.0).round() / 100.0, e])).collect();
        serde_json::json!({"frames": frames, "events": events}).to_string()
    })
}

/// `every MS N CMD`: run CMD N times on a UI-thread timer (precise spacing, no socket round trips).
fn every(u: &AppWindow, args: &[&str]) -> String {
    let ms: u64 = args.first().and_then(|v| v.parse().ok()).unwrap_or(33).max(1);
    let n: usize = args.get(1).and_then(|v| v.parse().ok()).unwrap_or(1);
    let cmd = args.get(2..).map(|a| a.join(" ")).unwrap_or_default();
    if cmd.is_empty() || cmd.starts_with("every") { return "usage: every MS N CMD".into(); }
    let w = u.as_weak();
    let left = std::rc::Rc::new(std::cell::Cell::new(n));
    let timer = std::rc::Rc::new(slint::Timer::default());
    let t2 = timer.clone();
    let i = std::rc::Rc::new(std::cell::Cell::new(0usize));
    timer.start(slint::TimerMode::Repeated, std::time::Duration::from_millis(ms), move || {
        if left.get() == 0 { t2.stop(); return; }
        left.set(left.get() - 1);
        let line = step_args(&cmd, i.get());
        i.set(i.get() + 1);
        if let Some(u) = w.upgrade() { let _ = run(&u, &line); }
    });
    // the closure holds the timer, so it lives until it has run N times and stops
    "ok".into()
}

/// `A+Bi` arguments of an `every` command become A + B * i on the i-th run (a pointer sweep).
fn step_args(cmd: &str, i: usize) -> String {
    cmd.split(' ').map(|w| {
        let Some(body) = w.strip_suffix('i') else { return w.to_string() };
        let Some((a, b)) = body.split_once('+') else { return w.to_string() };
        match (a.parse::<f32>(), b.parse::<f32>()) {
            (Ok(a), Ok(b)) => format!("{}", a + b * i as f32),
            _ => w.to_string(),
        }
    }).collect::<Vec<_>>().join(" ")
}

fn run(u: &AppWindow, line: &str) -> String {
    PROBE.with(|p| {
        let mut p = p.borrow_mut();
        if p.on && !line.starts_with("probe") && !line.starts_with("every") {
            let t = probe_ms(&p);
            p.events.push((t, line.trim().to_string()));
        }
    });
    run_cmd(u, line)
}

fn run_cmd(u: &AppWindow, line: &str) -> String {
    let a: Vec<&str> = line.trim().splitn(2, ' ').collect();
    let args: Vec<&str> = a.get(1).map(|s| s.split(' ').collect()).unwrap_or_default();
    let win = u.window();
    let click = |x: f32, y: f32, n: usize| {
        let position = LogicalPosition::new(x, y);
        win.dispatch_event(WindowEvent::PointerMoved { position });
        for _ in 0..n {
            win.dispatch_event(WindowEvent::PointerPressed { position, button: PointerEventButton::Left });
            win.dispatch_event(WindowEvent::PointerReleased { position, button: PointerEventButton::Left });
        }
    };
    match a[0] {
        "click" => { click(pos(&args, 0), pos(&args, 1), 1); "ok".into() }
        "rclick" => {
            let position = LogicalPosition::new(pos(&args, 0), pos(&args, 1));
            win.dispatch_event(WindowEvent::PointerMoved { position });
            win.dispatch_event(WindowEvent::PointerPressed { position, button: PointerEventButton::Right });
            win.dispatch_event(WindowEvent::PointerReleased { position, button: PointerEventButton::Right });
            "ok".into()
        }
        "dclick" => { click(pos(&args, 0), pos(&args, 1), 2); "ok".into() }
        "move" => { win.dispatch_event(WindowEvent::PointerMoved { position: LogicalPosition::new(pos(&args, 0), pos(&args, 1)) }); "ok".into() }
        "scroll" => {
            let position = LogicalPosition::new(pos(&args, 0), pos(&args, 1));
            win.dispatch_event(WindowEvent::PointerMoved { position });
            win.dispatch_event(WindowEvent::PointerScrolled { position, delta_x: 0.0, delta_y: -pos(&args, 2) });
            "ok".into()
        }
        "type" => {
            for ch in a.get(1).copied().unwrap_or("").chars() {
                let text = SharedString::from(ch.to_string());
                win.dispatch_event(WindowEvent::KeyPressed { text: text.clone() });
                win.dispatch_event(WindowEvent::KeyReleased { text });
            }
            "ok".into()
        }
        "key" => {
            // "key Enter", "key Ctrl+B", "key Ctrl+Shift+Left", "key Shift+End"
            let spec = args.first().copied().unwrap_or("");
            let mut parts: Vec<&str> = spec.split('+').collect();
            let last = parts.pop().unwrap_or("");
            let named = |n: &str| -> Option<SharedString> {
                Some(match n {
                    "Enter" => Key::Return.into(), "Escape" => Key::Escape.into(), "Tab" => Key::Tab.into(), "Backspace" => Key::Backspace.into(),
                    "Delete" => Key::Delete.into(), "Up" => Key::UpArrow.into(), "Down" => Key::DownArrow.into(), "Left" => Key::LeftArrow.into(),
                    "Right" => Key::RightArrow.into(), "Home" => Key::Home.into(), "End" => Key::End.into(), "PageUp" => Key::PageUp.into(),
                    "PageDown" => Key::PageDown.into(), "Space" => " ".into(),
                    s if s.chars().count() == 1 => s.to_lowercase().into(),
                    _ => return None,
                })
            };
            let Some(text) = named(last) else { return format!("unknown key {last}") };
            let mods: Vec<SharedString> = parts.iter().filter_map(|m| match *m {
                "Ctrl" => Some(Key::Control.into()), "Shift" => Some(Key::Shift.into()), "Alt" => Some(Key::Alt.into()), _ => None,
            }).collect();
            for m in &mods { win.dispatch_event(WindowEvent::KeyPressed { text: m.clone() }); }
            win.dispatch_event(WindowEvent::KeyPressed { text: text.clone() });
            win.dispatch_event(WindowEvent::KeyReleased { text });
            for m in mods.iter().rev() { win.dispatch_event(WindowEvent::KeyReleased { text: m.clone() }); }
            "ok".into()
        }
        "drag" => {
            let (x0, y0, x1, y1) = (pos(&args, 0), pos(&args, 1), pos(&args, 2), pos(&args, 3));
            let p0 = LogicalPosition::new(x0, y0);
            win.dispatch_event(WindowEvent::PointerMoved { position: p0 });
            win.dispatch_event(WindowEvent::PointerPressed { position: p0, button: PointerEventButton::Left });
            for i in 1..=8 {
                let f = i as f32 / 8.0;
                win.dispatch_event(WindowEvent::PointerMoved { position: LogicalPosition::new(x0 + (x1 - x0) * f, y0 + (y1 - y0) * f) });
            }
            win.dispatch_event(WindowEvent::PointerReleased { position: LogicalPosition::new(x1, y1), button: PointerEventButton::Left });
            "ok".into()
        }
        "rte-image" => {
            // insert an image file into the composer (as paste/drop would)
            let path = a.get(1).copied().unwrap_or("");
            match std::fs::read(path) {
                Ok(b) => { let name = path.rsplit(['/', '\\']).next().unwrap_or("image").to_string(); crate::rte::insert_image_bytes(u, b, crate::compose::mime_for(&name), &name); "ok".into() }
                Err(e) => format!("error {e}"),
            }
        }
        "rte-html" => {
            // dump the composer's HTML export (for checking the round trip)
            crate::rte::to_html().replace('\n', " ")
        }
        // run JS in the email WebView (not captured by `shot`); fetch the JSON result with wv-result
        "wv-eval" => if crate::wv_eval(a.get(1).copied().unwrap_or("")) { "ok".into() } else { "no webview".into() },
        "wv-result" => crate::WV_EVAL.lock().unwrap().clone().unwrap_or_else(|| "pending".into()).replace(['\n', '\r'], " "),
        "pick" => {
            let v: Vec<std::path::PathBuf> = a.get(1).copied().unwrap_or("").split('|').filter(|p| !p.is_empty()).map(Into::into).collect();
            PICK.with(|p| *p.borrow_mut() = Some(v));
            "ok".into()
        }
        // flip the motion tokens in memory only: reduce|normal (Motion.reduce), rapid|calm (Motion.rapid)
        "motion" => {
            let m = u.global::<crate::Motion>();
            for w in &args {
                match *w {
                    "reduce" => m.set_reduce(true), "normal" => m.set_reduce(false),
                    "rapid" => m.set_rapid(true), "calm" => m.set_rapid(false),
                    "" => {}
                    other => return format!("unknown motion option {other}"),
                }
            }
            format!("reduce {} rapid {}", m.get_reduce(), m.get_rapid())
        }
        // show (or with no text clear) a toast, UI only: for checking its enter / exit motion
        "toast" => { u.set_toast(a.get(1).copied().unwrap_or("").into()); "ok".into() }
        "probe-start" => probe_start(u),
        "probe-stop" => probe_stop(),
        "every" => every(u, &args),
        "size" => { let s = win.size(); format!("{} {} scale {}", s.width, s.height, win.scale_factor()) }
        // resize the (off-screen) window, in logical pixels, to check narrow layouts
        "resize" => { win.set_size(slint::LogicalSize::new(pos(&args, 0).max(400.0), pos(&args, 1).max(300.0))); "ok".into() }
        // switch the look in memory only (nothing is saved): ui bento|nordic dark|light [more|normal]
        "ui" => {
            let t = u.global::<crate::Theme>();
            for w in &args {
                match *w {
                    "bento" => t.set_style(0), "nordic" => t.set_style(1),
                    "dark" => t.set_dark(true), "light" => t.set_dark(false),
                    "more" => t.set_more_contrast(true), "normal" => t.set_more_contrast(false),
                    "" => {}
                    other => return format!("unknown ui option {other}"),
                }
            }
            format!("style {} dark {} more-contrast {}", if t.get_style() == 1 { "nordic" } else { "bento" }, t.get_dark(), t.get_more_contrast())
        }
        "shot" => {
            let path = a.get(1).copied().unwrap_or("shot.png");
            match win.take_snapshot() {
                Ok(buf) => match write_png(path, buf.width(), buf.height(), buf.as_bytes()) {
                    Ok(()) => format!("saved {}x{}", buf.width(), buf.height()),
                    Err(e) => format!("error {e}"),
                },
                Err(e) => format!("error {e}"),
            }
        }
        other => format!("unknown command {other}"),
    }
}

fn write_png(path: &str, w: u32, h: u32, rgba: &[u8]) -> Result<(), String> {
    let f = std::fs::File::create(path).map_err(|e| e.to_string())?;
    let mut enc = png::Encoder::new(std::io::BufWriter::new(f), w, h);
    enc.set_color(png::ColorType::Rgba);
    enc.set_depth(png::BitDepth::Eight);
    let mut wr = enc.write_header().map_err(|e| e.to_string())?;
    wr.write_image_data(rgba).map_err(|e| e.to_string())
}
