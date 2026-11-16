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
//!   wv-eval JS / wv-result (run JS in the email WebView, then read its JSON result)
//!   pick PATH[|PATH...]  (answer the next file-open picker with these files instead of a dialog)
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

fn run(u: &AppWindow, line: &str) -> String {
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
        "size" => { let s = win.size(); format!("{} {} scale {}", s.width, s.height, win.scale_factor()) }
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
