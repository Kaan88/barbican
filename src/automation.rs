//! Test automation without touching the real mouse, keyboard or screen.
//! Enabled only when BARBICAN_AUTOMATION=1. Listens on 127.0.0.1:<BARBICAN_AUTOMATION_PORT|8792>
//! for one command per line; input is injected into Slint's own event queue
//! (`Window::dispatch_event`) and screenshots come from Slint's renderer
//! (`Window::take_snapshot`), so the window can stay off-screen and unfocused.
//!
//! Commands (logical pixels):
//!   click X Y | rclick X Y | dclick X Y | move X Y | scroll X Y DY
//!   type TEXT            (sent as key presses)
//!   key Enter|Escape|Tab|Backspace|Delete|Up|Down|Left|Right
//!   shot PATH.png        (window contents; the WebView2 email body is a separate window and not included)
//!   size                 (window size)
use crate::AppWindow;
use slint::platform::{Key, PointerEventButton, WindowEvent};
use slint::{ComponentHandle, LogicalPosition, SharedString};
use std::io::{BufRead, BufReader, Write};
use std::net::TcpListener;
use std::sync::mpsc;

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
            let k = match args.first().copied().unwrap_or("") {
                "Enter" => Key::Return, "Escape" => Key::Escape, "Tab" => Key::Tab, "Backspace" => Key::Backspace,
                "Delete" => Key::Delete, "Up" => Key::UpArrow, "Down" => Key::DownArrow, "Left" => Key::LeftArrow, "Right" => Key::RightArrow,
                other => return format!("unknown key {other}"),
            };
            let text: SharedString = k.into();
            win.dispatch_event(WindowEvent::KeyPressed { text: text.clone() });
            win.dispatch_event(WindowEvent::KeyReleased { text });
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
