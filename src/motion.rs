//! Motion settings for the UI (design/MOTION.md): `Motion.reduce` from Settings › Accessibility ›
//! Reduce motion or, on "System", Windows' animation effects; `Motion.rapid` while the list
//! selection moves faster than an animation. No per-frame work: a flag flips on a user action
//! or when the window is activated, and one single-shot timer clears `rapid`.
use crate::{AppWindow, Motion};
use slint::ComponentHandle;
use std::cell::{Cell, RefCell};
use std::time::{Duration, Instant};

thread_local! {
    /// the reduceMotion setting as stored: "on" | "off" | "system"
    static PREF: RefCell<String> = RefCell::new("system".into());
    static LAST_STEP: Cell<Option<Instant>> = const { Cell::new(None) };
    static CALM: slint::Timer = slint::Timer::default();
}

/// Steps closer together than this are a key repeat (or a very fast hand): list feedback snaps.
const RAPID: Duration = Duration::from_millis(150);
/// How long after the last fast step the list goes back to animating.
const CALM_AFTER: Duration = Duration::from_millis(180);

/// What `Motion.reduce` should be for a reduceMotion setting and Windows' state.
pub fn reduce_for(pref: &str, system_animations_off: bool) -> bool {
    match pref { "on" => true, "off" => false, _ => system_animations_off }
}

/// Apply the reduceMotion setting (`pref` as stored) with the A11y result already worked out.
pub fn apply(ui: &AppWindow, pref: &str, reduce: bool) {
    PREF.with(|p| *p.borrow_mut() = pref.to_string());
    ui.global::<Motion>().set_reduce(reduce);
}

/// Follow Windows "Animation effects" while the setting is on "System": re-read it whenever the
/// window is activated (no polling; the user changes it in Windows Settings and comes back).
pub fn watch_system(ui: &AppWindow) {
    install_filter(ui);
    // the winit window only exists once the event loop runs: install again then
    let w = ui.as_weak();
    slint::Timer::single_shot(Duration::ZERO, move || { if let Some(u) = w.upgrade() { install_filter(&u); } });
}

/// The window's winit event filter (a window has exactly one): activation re-reads Windows'
/// animation setting, and every event is offered to the automation frame probe (a no-op
/// unless it is recording). Installing it again replaces it with the same filter.
pub fn install_filter(ui: &AppWindow) {
    use slint::winit_030::{winit::event::WindowEvent, EventResult, WinitWindowAccessor};
    let w = ui.as_weak();
    ui.window().on_winit_window_event(move |_, ev| {
        crate::automation::winit_event(ev);
        if let WindowEvent::Focused(true) = ev {
            if PREF.with(|p| p.borrow().as_str() == "system") {
                if let Some(u) = w.upgrade() {
                    let reduce = reduce_for("system", crate::a11y::System::detect().reduce_motion);
                    if u.global::<Motion>().get_reduce() != reduce { u.global::<Motion>().set_reduce(reduce); }
                }
            }
        }
        EventResult::Propagate
    });
}

/// The list selection moved one step (keyboard). Steps faster than an animation make list
/// feedback snap (`Motion.rapid`) until the keys calm down, so a held arrow key never trails.
pub fn step(ui: &AppWindow) {
    let now = Instant::now();
    let fast = LAST_STEP.with(|l| l.replace(Some(now))).is_some_and(|t| now.duration_since(t) < RAPID);
    if !fast { return; }
    let m = ui.global::<Motion>();
    if !m.get_rapid() { m.set_rapid(true); }
    let w = ui.as_weak();
    CALM.with(|t| t.start(slint::TimerMode::SingleShot, CALM_AFTER, move || {
        if let Some(u) = w.upgrade() { u.global::<Motion>().set_rapid(false); }
    }));
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn setting_wins_over_system() {
        assert!(reduce_for("on", false));
        assert!(!reduce_for("off", true));
        assert!(reduce_for("system", true));
        assert!(!reduce_for("system", false));
        assert!(!reduce_for("", false));
    }
}
