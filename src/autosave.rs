//! Draft autosave on a debounce, as Bulwark's email-composer.tsx: two seconds after the last
//! change the draft is saved, but only once the user changed something (a fresh composer,
//! the quote and the signature a reply starts with are no reason to keep a draft), only when
//! the content differs from what was saved last, and never while another save or a send is
//! on its way (those queue behind it, so two saves cannot both replace the same draft).
use crate::AppWindow;
use slint::ComponentHandle;
use std::cell::RefCell;
use std::time::{Duration, Instant};

/// Bulwark's autosave delay.
pub const QUIET: Duration = Duration::from_millis(2000);
const TICK: Duration = Duration::from_millis(500);

/// What waits for the save in flight.
#[derive(Clone, Copy, Debug, PartialEq)]
pub enum Queued { Send, Schedule(i64), Save(bool), Discard }

#[derive(Default)]
struct S {
    /// composer generation: a save that finishes after the composer was reopened is ignored
    gen: u64,
    timer: Option<slint::Timer>,
    /// content when the composer opened (or the system last changed it, e.g. the signature)
    baseline: Option<u64>,
    /// content of the draft on the server
    saved: Option<u64>,
    /// cheap fingerprint and the content key computed for it
    cheap: u64,
    key: u64,
    changed: Option<Instant>,
    in_flight: bool,
    /// content key of the save in flight (None for a send)
    flight_key: Option<u64>,
    queued: Option<Queued>,
}

/// What the save still in flight from a composer that was just replaced must do once it
/// finishes, so nothing the user wrote or asked for is lost.
#[derive(Clone, Copy, Debug, PartialEq)]
pub enum Leftover {
    /// save the old composer's last content (snapshotted at the reset) over the new draft
    Save,
    /// remove the draft that save creates (a discard was queued behind it)
    Discard,
}

/// Pure decision for `reset_leftover` (for tests): `unsaved` = the old composer holds content
/// the save in flight does not carry. A queued send is not carried over into a send nobody
/// sees: it is kept as a draft instead.
pub fn leftover_for(queued: Option<Queued>, unsaved: bool) -> Option<Leftover> {
    match queued {
        Some(Queued::Discard) => Some(Leftover::Discard),
        Some(Queued::Save(_) | Queued::Send | Queued::Schedule(_)) => Some(Leftover::Save),
        None if unsaved => Some(Leftover::Save),
        None => None,
    }
}

thread_local! { static ST: RefCell<S> = RefCell::new(S::default()); }

/// Whether an autosave is due (pure, for tests).
pub fn due(key: u64, baseline: Option<u64>, saved: Option<u64>, quiet: Duration, in_flight: bool) -> bool {
    !in_flight && Some(key) != baseline && Some(key) != saved && quiet >= QUIET
}

/// A new composer replaces one whose save is still in flight: returns that save's generation
/// and what it must still do (see `Leftover`), then resets. `key` is the old composer's
/// content now. None when nothing is in flight (plain reset).
pub fn reset_leftover(key: u64) -> Option<(u64, Leftover)> {
    let r = ST.with(|s| {
        let s = s.borrow();
        if !s.in_flight { return None; }
        let unsaved = Some(key) != s.saved && Some(key) != s.flight_key && Some(key) != s.baseline;
        // a send in flight (no key) carries the content itself
        let unsaved = unsaved && s.flight_key.is_some();
        leftover_for(s.queued, unsaved).map(|l| (s.gen, l))
    });
    reset();
    r
}

/// A new composer: forget everything about the previous one.
pub fn reset() {
    ST.with(|s| {
        let mut s = s.borrow_mut();
        let gen = s.gen + 1;
        *s = S { gen, ..Default::default() };
    });
}

fn current_key(u: &AppWindow) -> u64 {
    let cheap = crate::compose::cheap_key(u);
    let (known, key) = ST.with(|s| { let s = s.borrow(); (s.cheap == cheap && s.changed.is_some(), s.key) });
    if known { return key; }
    let key = crate::compose::content_key(u);
    ST.with(|s| { let mut s = s.borrow_mut(); s.cheap = cheap; if s.key != key || s.changed.is_none() { s.key = key; s.changed = Some(Instant::now()); } });
    key
}

/// The composer finished opening: what it holds now counts as unedited (`saved`: it is
/// also what the server has, a reopened draft). Starts the debounce timer.
pub fn start(u: &AppWindow, saved: bool) {
    let key = crate::compose::content_key(u);
    let cheap = crate::compose::cheap_key(u);
    let timer = slint::Timer::default();
    let w = u.as_weak();
    timer.start(slint::TimerMode::Repeated, TICK, move || { if let Some(u) = w.upgrade() { tick(&u); } });
    ST.with(|s| {
        let mut s = s.borrow_mut();
        s.baseline = Some(key);
        s.saved = saved.then_some(key);
        s.cheap = cheap;
        s.key = key;
        s.changed = Some(Instant::now());
        s.timer = Some(timer);
    });
}

/// Whether the user has not changed anything yet.
pub fn unedited(u: &AppWindow) -> bool {
    let k = current_key(u);
    ST.with(|s| s.borrow().baseline == Some(k))
}

/// The system changed the content (signature added): still unedited, at the new content.
pub fn rebase(u: &AppWindow) {
    let k = crate::compose::content_key(u);
    ST.with(|s| {
        let mut s = s.borrow_mut();
        if s.saved.is_some() && s.saved == s.baseline { s.saved = Some(k); }
        s.baseline = Some(k);
    });
}

/// Whether the content is what the server already has.
pub fn is_saved(u: &AppWindow) -> bool {
    let k = current_key(u);
    ST.with(|s| s.borrow().saved == Some(k))
}

fn tick(u: &AppWindow) {
    if !u.get_composing() {
        ST.with(|s| { if let Some(t) = s.borrow_mut().timer.take() { t.stop(); } });
        return;
    }
    if u.get_sending() { return; }
    let key = current_key(u);
    crate::plugin_hooks::draft_tick(key, &|c| crate::compose::draft_payload(u, c));
    let go = ST.with(|s| {
        let s = s.borrow();
        due(key, s.baseline, s.saved, s.changed.map(|t| t.elapsed()).unwrap_or_default(), s.in_flight)
    });
    if go { crate::compose::autosave_now(u); }
}

pub fn in_flight() -> bool { ST.with(|s| s.borrow().in_flight) }

/// Something to do once the save in flight is done (the last request wins).
pub fn queue(q: Queued) { ST.with(|s| s.borrow_mut().queued = Some(q)); }

/// A save or send starts: returns the generation to hand to `finish`.
pub fn begin() -> u64 { ST.with(|s| { let mut s = s.borrow_mut(); s.in_flight = true; s.flight_key = None; s.gen }) }

/// A draft save of content `key` starts.
pub fn begin_save(key: u64) -> u64 { let g = begin(); ST.with(|s| s.borrow_mut().flight_key = Some(key)); g }

pub fn gen() -> u64 { ST.with(|s| s.borrow().gen) }

/// A save or send of generation `gen` finished; `saved_key` is the content now on the server.
/// Returns what was queued behind it.
pub fn finish(gen: u64, saved_key: Option<u64>) -> Option<Queued> {
    ST.with(|s| {
        let mut s = s.borrow_mut();
        if s.gen != gen { return None; }
        s.in_flight = false;
        s.flight_key = None;
        if saved_key.is_some() { s.saved = saved_key; }
        s.queued.take()
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn debounce_rules() {
        let q = QUIET;
        // unedited: nothing to save
        assert!(!due(1, Some(1), None, q, false));
        // edited and quiet for 2 s
        assert!(due(2, Some(1), None, q, false));
        // still typing
        assert!(!due(2, Some(1), None, Duration::from_millis(1500), false));
        // already on the server
        assert!(!due(2, Some(1), Some(2), q, false));
        // a save is on its way
        assert!(!due(2, Some(1), None, q, true));
        // back to the opened content of a reopened draft that was saved meanwhile
        assert!(due(1, Some(1), Some(3), q, false) == false);
    }

    #[test]
    fn generations() {
        reset();
        let g = begin();
        queue(Queued::Send);
        assert!(in_flight());
        assert_eq!(finish(g, Some(7)), Some(Queued::Send));
        assert!(!in_flight());
        let g2 = begin();
        reset();
        // a save from the previous composer changes nothing
        assert_eq!(finish(g2, Some(9)), None);
        assert!(!in_flight());
    }

    #[test]
    fn reopen_during_a_save_keeps_the_queued_work() {
        // queue, then reset, then finish: the queued save survives as a leftover
        reset();
        let g = begin_save(5);
        queue(Queued::Save(false));
        let left = reset_leftover(5);
        assert_eq!(left, Some((g, Leftover::Save)));
        assert_eq!(finish(g, Some(5)), None);
        // typed after the save started, nothing queued: still saved
        reset();
        let g = begin_save(5);
        assert_eq!(reset_leftover(6), Some((g, Leftover::Save)));
        // the save in flight already carries everything: nothing more to do
        reset();
        begin_save(5);
        assert_eq!(reset_leftover(5), None);
        // a discard queued behind it removes the draft the save creates
        reset();
        let g = begin_save(5);
        queue(Queued::Discard);
        assert_eq!(reset_leftover(7), Some((g, Leftover::Discard)));
        // nothing in flight: a plain reset
        reset();
        assert_eq!(reset_leftover(1), None);
        assert_eq!(leftover_for(Some(Queued::Send), false), Some(Leftover::Save));
    }
}

/// Test hook (automation `chips` probe): the autosave state.
pub fn describe(u: &AppWindow) -> String {
    let k = crate::compose::content_key(u);
    ST.with(|s| { let s = s.borrow(); format!("autosave key={k:x} base={:x?} saved={:x?} flight={}", s.baseline, s.saved, s.in_flight) })
}
