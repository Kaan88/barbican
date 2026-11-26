//! Barbican's own names for the few view types the logic handles.
//!
//! The UI is moving to the owner's toolkit (`docs/UI-TOOLKIT-EVAL.md`, milestones U0-U12). Most of
//! `src/` survives that switch. This module re-exports the current view types without conversion
//! and adapts host window and input calls for the logic.
//!
//! Not here, and still named through `slint::`: `ComponentHandle`, `Model`, `ModelRc`, `VecModel`
//! and the generated `AppWindow` (replaced wholesale by U2's stand-in), `StyledText` (the list
//! rows' markup) and `include_modules!` (the generated bindings).

/// A picture for a view property: an avatar, a BIMI badge, a file thumbnail, a snapshot.
pub use slint::Image;
/// A colour for a view property: a calendar's tint, a tag's.
pub use slint::Color;
/// A string for a view property (the generated setters take these).
pub use slint::SharedString;
/// A one-shot or repeated timer on the UI thread.
pub use slint::Timer;
/// `Timer::start`'s mode: fire once, or every interval.
pub use slint::TimerMode;
/// A weak handle to the one window, so a task off the UI thread can ask whether it is still open.
pub use slint::Weak;
/// Pixels in rows: what `Image::from_rgba8` takes.
pub use slint::SharedPixelBuffer;
/// The pixel type of `SharedPixelBuffer`.
pub use slint::Rgba8Pixel;
/// The winit API Slint is built against: the host window's own calls (JANK #1, #9 in the plan).
pub use slint::winit_030::{winit, EventResult};
use slint::winit_030::WinitWindowAccessor;

/// Calls `f` with the winit window behind `window`, or returns `None` when there is none (no winit
/// backend yet, or the window is gone). The one way the logic reaches winit.
pub fn with_winit_window<T>(window: &slint::Window, f: impl FnOnce(&winit::window::Window) -> T) -> Option<T> {
    window.with_winit_window(f)
}

/// Install a host event filter using the current backend's registration semantics.
pub fn on_winit_window_event(window: &slint::Window, mut f: impl FnMut(&winit::event::WindowEvent) -> EventResult + 'static) {
    window.on_winit_window_event(move |_, event| f(event));
}

/// Input put into the one window, and its pixels read back: what the automation channel
/// (`src/automation.rs`) needs, without its naming a Slint event.
pub mod input {
    pub use slint::platform::Key;
    use slint::platform::{PointerEventButton, WindowEvent};

    /// Which mouse button a press or a release is for.
    #[derive(Clone, Copy, PartialEq, Eq, Debug)]
    pub enum Button {
        Left,
        Right,
    }

    impl From<Button> for PointerEventButton {
        fn from(b: Button) -> Self {
            match b {
                Button::Left => Self::Left,
                Button::Right => Self::Right,
            }
        }
    }

    fn send(window: &slint::Window, event: WindowEvent) {
        window.dispatch_event(event);
    }

    fn at(x: f32, y: f32) -> slint::LogicalPosition {
        slint::LogicalPosition::new(x, y)
    }

    /// The pointer over the window, logical pixels, no button state.
    pub fn pointer_moved(window: &slint::Window, x: f32, y: f32) {
        send(window, WindowEvent::PointerMoved { position: at(x, y) });
    }

    /// A button pressed at a point (two in a row make a double click).
    pub fn pointer_pressed(window: &slint::Window, x: f32, y: f32, button: Button) {
        send(window, WindowEvent::PointerPressed { position: at(x, y), button: button.into() });
    }

    /// The same button released at a point.
    pub fn pointer_released(window: &slint::Window, x: f32, y: f32, button: Button) {
        send(window, WindowEvent::PointerReleased { position: at(x, y), button: button.into() });
    }

    /// A wheel turn at a point, logical pixels of scroll (down is negative).
    pub fn pointer_scrolled(window: &slint::Window, x: f32, y: f32, dx: f32, dy: f32) {
        send(window, WindowEvent::PointerScrolled { position: at(x, y), delta_x: dx, delta_y: dy });
    }

    /// A key pressed, carrying the text it types (see `key_text`).
    pub fn key_pressed(window: &slint::Window, text: impl Into<super::SharedString>) {
        send(window, WindowEvent::KeyPressed { text: text.into() });
    }

    /// The same key released.
    pub fn key_released(window: &slint::Window, text: impl Into<super::SharedString>) {
        send(window, WindowEvent::KeyReleased { text: text.into() });
    }

    /// The window's close button: quits, or hides to the tray.
    pub fn close_requested(window: &slint::Window) {
        send(window, WindowEvent::CloseRequested);
    }

    /// The renderer's RGBA8 buffer, without copying a frame's worth of pixels.
    pub fn snapshot(window: &slint::Window) -> Result<super::SharedPixelBuffer<super::Rgba8Pixel>, String> {
        window.take_snapshot().map_err(|e| e.to_string())
    }

    /// Resize the window, in logical pixels.
    pub fn set_size(window: &slint::Window, width: f32, height: f32) {
        window.set_size(slint::LogicalSize::new(width, height));
    }

    /// Translate an automation key name to the current backend's key text.
    pub fn key_text(name: &str) -> Option<crate::ui_types::SharedString> {
        Some(match name {
            "Enter" => Key::Return.into(),
            "Escape" => Key::Escape.into(),
            "Tab" => Key::Tab.into(),
            "Backspace" => Key::Backspace.into(),
            "Delete" => Key::Delete.into(),
            "Up" => Key::UpArrow.into(),
            "Down" => Key::DownArrow.into(),
            "Left" => Key::LeftArrow.into(),
            "Right" => Key::RightArrow.into(),
            "Home" => Key::Home.into(),
            "End" => Key::End.into(),
            "PageUp" => Key::PageUp.into(),
            "PageDown" => Key::PageDown.into(),
            "Space" => " ".into(),
            "F2" => Key::F2.into(),
            "F5" => Key::F5.into(),
            "F9" => Key::F9.into(),
            "F10" => Key::F10.into(),
            s if s.chars().count() == 1 => s.to_lowercase().into(),
            _ => return None,
        })
    }

    /// The key code of a modifier held while another key is pressed: `Ctrl`, `Shift`, `Alt`.
    pub fn modifier_text(name: &str) -> Option<crate::ui_types::SharedString> {
        Some(match name {
            "Ctrl" => Key::Control.into(),
            "Shift" => Key::Shift.into(),
            "Alt" => Key::Alt.into(),
            _ => return None,
        })
    }
}

#[cfg(test)]
mod tests {
    use std::path::{Path, PathBuf};

    /// The names this module owns; nothing else under `src/` may reach them through `slint::`.
    const OWNED: [&str; 9] = ["Image", "Color", "SharedString", "Timer", "TimerMode", "Weak", "SharedPixelBuffer", "Rgba8Pixel", "winit_030"];

    /// The wrappers are the very Slint types, not copies: no conversion, no allocation.
    #[test]
    fn wrappers_are_slints_own_types() {
        use std::any::TypeId;
        let same = |a: TypeId, b: TypeId| assert_eq!(a, b);
        same(TypeId::of::<super::Image>(), TypeId::of::<slint::Image>());
        same(TypeId::of::<super::Color>(), TypeId::of::<slint::Color>());
        same(TypeId::of::<super::SharedString>(), TypeId::of::<slint::SharedString>());
        same(TypeId::of::<super::Timer>(), TypeId::of::<slint::Timer>());
        same(TypeId::of::<super::TimerMode>(), TypeId::of::<slint::TimerMode>());
        same(TypeId::of::<super::Weak<crate::AppWindow>>(), TypeId::of::<slint::Weak<crate::AppWindow>>());
        same(TypeId::of::<super::SharedPixelBuffer<super::Rgba8Pixel>>(), TypeId::of::<slint::SharedPixelBuffer<slint::Rgba8Pixel>>());
    }

    /// A synthetic key types what Slint's own key code would: the mapping must not drift.
    #[test]
    fn key_texts_are_slints_own_key_codes() {
        use slint::platform::Key;
        let cases: [(&str, Key); 18] = [
            ("Enter", Key::Return), ("Escape", Key::Escape), ("Tab", Key::Tab), ("Backspace", Key::Backspace),
            ("Delete", Key::Delete), ("Up", Key::UpArrow), ("Down", Key::DownArrow), ("Left", Key::LeftArrow),
            ("Right", Key::RightArrow), ("Home", Key::Home), ("End", Key::End), ("PageUp", Key::PageUp),
            ("PageDown", Key::PageDown), ("Space", Key::Space), ("F2", Key::F2), ("F5", Key::F5),
            ("F9", Key::F9), ("F10", Key::F10),
        ];
        for (name, key) in cases {
            assert_eq!(super::input::key_text(name), Some(slint::SharedString::from(key)), "{name}");
        }
        assert_eq!(super::input::key_text("B"), Some("b".into()));
        assert_eq!(super::input::key_text("Escapee"), None);
        assert_eq!(super::input::key_text(""), None);
        for (name, key) in [("Ctrl", Key::Control), ("Shift", Key::Shift), ("Alt", Key::Alt)] {
            assert_eq!(super::input::modifier_text(name), Some(slint::SharedString::from(key)), "{name}");
        }
        assert_eq!(super::input::modifier_text("Meta"), None);
    }

    fn sources(dir: &Path, out: &mut Vec<PathBuf>) {
        for entry in std::fs::read_dir(dir).expect("read src/") {
            let path = entry.expect("dir entry").path();
            if path.is_dir() {
                sources(&path, out);
            } else if path.extension().is_some_and(|e| e == "rs") {
                out.push(path);
            }
        }
    }

    /// True when `line` names `name` right after `slint::`: the path itself, or one `use slint::`
    /// imports, but not a longer name that merely starts with it (`slint::Timer` in
    /// `slint::TimerMode`).
    fn names_slint(line: &str, name: &str) -> bool {
        let pat = format!("slint::{name}");
        let mut from = 0;
        while let Some(i) = line[from..].find(&pat) {
            let end = from + i + pat.len();
            if !line[end..].chars().next().is_some_and(|c| c.is_alphanumeric() || c == '_') {
                return true;
            }
            from = i + 1;
        }
        let Some(rest) = line.trim().strip_prefix("use slint::") else { return false };
        rest.split(['{', '}', ',', ';']).filter_map(|item| item.split_whitespace().next()).any(|item| item.split("::").next() == Some(name))
    }

    /// `src/` stopped naming Slint's view types when it moved onto this module (U1). The wrapper
    /// module is the only place allowed to, so the logic stays ready for the toolkit's front end.
    #[test]
    fn deny_slint_types() {
        let root = Path::new(env!("CARGO_MANIFEST_DIR")).join("src");
        let mut files = Vec::new();
        sources(&root, &mut files);
        assert!(files.len() > 50, "found only {} files under src/", files.len());
        let mut found = Vec::new();
        for file in files {
            if file == root.join("ui_types.rs") {
                continue;
            }
            let text = std::fs::read_to_string(&file).expect("read source");
            for (n, line) in text.lines().enumerate() {
                if let Some(name) = OWNED.iter().find(|name| names_slint(line, name)) {
                    found.push(format!("{}:{}: slint::{name} in `{}`", file.display(), n + 1, line.trim()));
                }
                if [".dispatch_event(", ".take_snapshot(", ".on_winit_window_event(", ".with_winit_window("]
                    .iter().any(|method| line.contains(method))
                {
                    found.push(format!("{}:{}: direct window call in `{}`", file.display(), n + 1, line.trim()));
                }
            }
        }
        assert!(found.is_empty(), "name these through crate::ui_types instead:\n{}", found.join("\n"));
    }
}
