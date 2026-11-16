//! The email body view: one persistent WebView2 document (the "shell") that never navigates.
//! Each message is a sandboxed srcdoc iframe with its own CSP (the per-message image policy),
//! loaded hidden next to the one on screen and swapped in once it is laid out, so switching
//! messages never shows a blank, white or black page in between. The shell reports back
//! (`ready:N`) before the swap, so the native header can change in the same moment.
//!
//! While a native overlay (context menu, dialog, settings) must cover the body, the WebView
//! (a child window that would paint over it) is hidden and a capture of it is shown in its
//! place, so the reader looks unchanged underneath.
use crate::{AppWindow, WV};
use slint::ComponentHandle;
use std::cell::{Cell, RefCell};
use std::time::Duration;

thread_local! {
    static SHELL_READY: Cell<bool> = const { Cell::new(false) };
    /// The last script that could not run yet (the shell was still loading).
    static PENDING_JS: RefCell<Option<String>> = const { RefCell::new(None) };
    static DOC_SEQ: Cell<u64> = const { Cell::new(0) };
    /// Native updates to run when document N is ready, right before it is revealed; false means
    /// the document is no longer wanted.
    static ON_READY: RefCell<Option<(u64, Box<dyn FnOnce() -> bool>)>> = const { RefCell::new(None) };
    /// The pending document is slow (large mail): the loading bar is on until it is ready.
    static SLOW: Cell<bool> = const { Cell::new(false) };
    static READY_TIMER: slint::Timer = slint::Timer::default();
    /// Whether the WebView child window is shown.
    static SHOWN: Cell<bool> = const { Cell::new(false) };
    static SNAP_TIMER: slint::Timer = slint::Timer::default();
    /// A hide waiting for the frame that shows the capture in its place.
    static HIDE_PENDING: Cell<bool> = const { Cell::new(false) };
    static UI: RefCell<Option<slint::Weak<AppWindow>>> = const { RefCell::new(None) };
    /// The host window's current background, and the page colour of each pending document.
    static HOST_BG: RefCell<String> = const { RefCell::new(String::new()) };
    static NEXT_BG: RefCell<String> = const { RefCell::new(String::new()) };
}

/// The shell's script: show(seq, html, bg, sandbox) loads a hidden frame and reports
/// `ready:seq` once it is laid out; reveal(seq) swaps it in and drops the old one;
/// cancel(seq) drops a pending frame; dim(on) fades the frame on screen while the next loads.
///
/// A page under the dark conversion (dark_mail.rs) is ready once that pass ran.
///
/// Links: every link and form of a message is retargeted to a new window before it is shown
/// (wry only sees main-frame navigations, so a `target=_self/_top/_parent` link would otherwise
/// navigate the frame itself, or do nothing); new windows open in the browser or the composer.
const SHELL_JS: &str = r#"(function(){
var cur=null,nxt=null,seq=0;
function post(m){try{window.ipc.postMessage(m)}catch(e){}}
function fix(d){try{var a=d.querySelectorAll('a,area,form');for(var i=0;i<a.length;i++)a[i].setAttribute('target','_blank')}catch(e){}}
window.__bb={
show:function(s,html,bg,sb){seq=s;if(nxt){nxt.remove();nxt=null}
var f=document.createElement('iframe');if(sb!==null)f.setAttribute('sandbox',sb);f.setAttribute('title','Message');
f.style.cssText='position:absolute;left:0;top:0;width:100%;height:100%;border:0;margin:0;visibility:hidden;background:'+bg;
f._bg=bg;nxt=f;var done=false;
function ready(){if(done||seq!==s)return;done=true;post('ready:'+s)}
f.addEventListener('load',function(){try{fix(f.contentDocument)}catch(e){}ready()});f.srcdoc=html;document.body.appendChild(f);
(function poll(){if(done||seq!==s)return;var d=null;try{d=f.contentDocument}catch(e){}
if(d&&d.URL==='about:srcdoc'&&d.readyState!=='loading'&&d.body&&!d.documentElement.classList.contains('barbican-dark-pending')){fix(d);requestAnimationFrame(ready);return}
requestAnimationFrame(poll)})();},
reveal:function(s){if(s!==seq||!nxt)return;var f=nxt;nxt=null;document.documentElement.style.background=f._bg;
f.style.visibility='visible';if(cur)cur.remove();cur=f},
cancel:function(s){seq=s;if(nxt){nxt.remove();nxt=null}},
dim:function(on){if(cur)cur.style.opacity=on?'0.4':''},
clear:function(bg){seq++;if(nxt){nxt.remove();nxt=null}if(cur){cur.remove();cur=null}document.documentElement.style.background=bg},
print:function(){if(cur)cur.contentWindow.print()}
};post('shell')})();"#;

/// The reader's background in the current theme (Theme.reader, the tile colour).
pub fn reader_bg() -> &'static str {
    if crate::DARK.with(|d| d.get()) { "#141414" } else { "#ffffff" }
}

fn parse_hex(c: &str) -> (u8, u8, u8) {
    let h = c.trim_start_matches('#');
    let p = |i: usize| u8::from_str_radix(h.get(i..i + 2).unwrap_or("ff"), 16).unwrap_or(255);
    (p(0), p(2), p(4))
}

pub fn color(c: &str) -> slint::Color {
    let (r, g, b) = parse_hex(c);
    slint::Color::from_rgb_u8(r, g, b)
}

/// Paint the WebView's host window (wry's container HWND) in `c`. A host window has no
/// background of its own, so while the WebView resizes or moves the newly uncovered strip
/// shows whatever was there (often white) for a frame; in the page's colour it is invisible.
#[cfg(windows)]
fn paint_host(v: &wry::WebView, c: &str) {
    use windows::Win32::Foundation::{COLORREF, HWND};
    use windows::Win32::Graphics::Gdi::{CreateSolidBrush, DeleteObject, HGDIOBJ};
    use windows::Win32::UI::WindowsAndMessaging::{SetClassLongPtrW, GCLP_HBRBACKGROUND};
    use wry::WebViewExtWindows;
    if HOST_BG.with(|h| h.borrow().as_str() == c) { return; }
    HOST_BG.with(|h| *h.borrow_mut() = c.to_string());
    let (r, g, b) = parse_hex(c);
    let hwnd = HWND(v.hwnd().0 as _);
    unsafe {
        let brush = CreateSolidBrush(COLORREF(r as u32 | (g as u32) << 8 | (b as u32) << 16));
        let old = SetClassLongPtrW(hwnd, GCLP_HBRBACKGROUND, brush.0 as isize);
        if old != 0 { let _ = DeleteObject(HGDIOBJ(old as _)); }
    }
}

#[cfg(not(windows))]
fn paint_host(_: &wry::WebView, _: &str) {}

/// The page colour changed (a message was swapped in, the view cleared, the theme changed).
fn page_bg(c: &str) {
    WV.with(|w| if let Some(v) = &*w.borrow() { paint_host(v, c); });
}

pub fn shell_html() -> String {
    // the shell's own policy is the most a message may get; each message frame narrows it with
    // its own (images blocked unless allowed, only our accessibility script by its hash)
    let bg = reader_bg();
    format!(
        "<!doctype html><html style=\"background:{bg}\"><head><meta charset=utf-8>\
<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline' *; font-src * data:; img-src * data:; script-src 'unsafe-inline'; frame-src 'self' about:; object-src 'none'; form-action 'none'\">\
<style>html,body{{margin:0;padding:0;height:100%;overflow:hidden}}</style></head><body><script>{SHELL_JS}</script></body></html>"
    )
}

/// The frame's sandbox: same origin (so the shell can tell when it is laid out), popups for
/// links (they open in the browser), modals for printing; scripts only for our own script.
fn sandbox(script: bool) -> &'static str {
    if script {
        "allow-same-origin allow-popups allow-popups-to-escape-sandbox allow-modals allow-scripts"
    } else {
        "allow-same-origin allow-popups allow-popups-to-escape-sandbox allow-modals"
    }
}

fn run_js(js: String) {
    let ready = SHELL_READY.with(|r| r.get());
    let ran = ready && WV.with(|w| w.borrow().as_ref().map(|v| v.evaluate_script(&js).is_ok()).unwrap_or(false));
    if !ran { PENDING_JS.with(|p| *p.borrow_mut() = Some(js)); }
}

/// Show a document (a full HTML page) in the body view, swapping it in once laid out.
/// `script`: the page carries our own (hashed) script, the only kind its frame may run.
/// `on_ready` runs on the UI thread right before the swap. A page that takes long (a large
/// mail) turns the loading bar on and is still only shown once laid out, never half parsed.
pub fn show(doc: &str, bg: &str, script: bool, on_ready: Option<Box<dyn FnOnce() -> bool>>) -> u64 {
    let seq = DOC_SEQ.with(|s| { s.set(s.get() + 1); s.get() });
    let js = format!(
        "window.__bb&&__bb.show({seq},{},{},{})",
        serde_json::to_string(doc).unwrap_or_default(),
        serde_json::to_string(bg).unwrap_or_default(),
        serde_json::to_string(sandbox(script)).unwrap_or_default()
    );
    ON_READY.with(|r| *r.borrow_mut() = on_ready.map(|f| (seq, f)));
    NEXT_BG.with(|b| *b.borrow_mut() = bg.to_string());
    crate::automation::mark(&format!("doc {seq}"));
    run_js(js);
    READY_TIMER.with(|t| t.start(slint::TimerMode::SingleShot, Duration::from_millis(600), move || slow(seq)));
    seq
}

/// Document `seq` is not ready after 600 ms. With the shell up it is still parsing (a large
/// mail): show the loading bar and wait for it (the shell reports it at its load event at the
/// latest), with a last resort for a view that never answers. Without a shell there is nothing
/// to wait for.
fn slow(seq: u64) {
    if DOC_SEQ.with(|s| s.get()) != seq { return; }
    if !SHELL_READY.with(|r| r.get()) { ready(seq, false); return; }
    SLOW.with(|s| s.set(true));
    if let Some(u) = ui() { u.set_r_loading(true); }
    READY_TIMER.with(|t| t.start(slint::TimerMode::SingleShot, Duration::from_millis(8000), move || ready(seq, false)));
}

pub(crate) fn ui() -> Option<AppWindow> { UI.with(|u| u.borrow().as_ref().and_then(|w| w.upgrade())) }

/// Drop the document that is loading, if any (another message is being opened): it must not
/// be revealed under that message's header.
pub fn cancel_pending() {
    let seq = DOC_SEQ.with(|s| { s.set(s.get() + 1); s.get() });
    ON_READY.with(|r| *r.borrow_mut() = None);
    READY_TIMER.with(|t| t.stop());
    SLOW.with(|s| s.set(false));
    run_js(format!("window.__bb&&__bb.cancel({seq})"));
}

/// Fade the message on screen (it stays until the next one is ready) while the next loads.
pub fn dim(on: bool) {
    run_js(format!("window.__bb&&__bb.dim({on})"));
}

/// Document `seq` is laid out (or the wait timed out): update the native side, then reveal it.
fn ready(seq: u64, from_view: bool) {
    if DOC_SEQ.with(|s| s.get()) != seq { return; }
    if from_view { READY_TIMER.with(|t| t.stop()); }
    READY_TIMER.with(|t| t.stop());
    let cb = ON_READY.with(|r| {
        let mut r = r.borrow_mut();
        if r.as_ref().is_some_and(|(s, _)| *s == seq) { r.take().map(|(_, f)| f) } else { None }
    });
    if SLOW.with(|s| s.replace(false)) {
        if let Some(u) = ui() { u.set_r_loading(false); }
    }
    // the native side refused it (the message it belongs to is no longer the open one)
    if let Some(f) = cb { if !f() { cancel_pending(); return; } }
    if let Some(u) = ui() {
        // a capture taken before this page is out of date
        if !SHOWN.with(|x| x.get()) { u.set_body_snap_on(false); }
        // the header just changed (thread strip, attachments): put the view at the body's new
        // place before the page shows, not one frame later
        crate::sync_webview(&u);
    }
    page_bg(&NEXT_BG.with(|b| b.borrow().clone()));
    crate::automation::mark(&format!("reveal {seq}"));
    run_js(format!("window.__bb&&__bb.reveal({seq})"));
}

/// The theme changed with no message open: the empty view takes the reader's new colour.
pub fn theme_changed() {
    SLOW.with(|s| s.set(false));
    run_js(format!("window.__bb&&__bb.clear('{}')", reader_bg()));
    page_bg(reader_bg());
}

/// Empty the view (the reader closed), in the reader's colour.
pub fn clear() {
    DOC_SEQ.with(|s| s.set(s.get() + 1));
    crate::CUR.with(|c| *c.borrow_mut() = None);
    crate::lite::clear();
    ON_READY.with(|r| *r.borrow_mut() = None);
    READY_TIMER.with(|t| t.stop());
    SLOW.with(|s| s.set(false));
    // an empty reader is not loading anything
    if let Some(u) = ui() { u.set_r_loading(false); }
    run_js(format!("window.__bb&&__bb.clear('{}')", reader_bg()));
    page_bg(reader_bg());
}

pub fn print() -> bool {
    SHELL_READY.with(|r| r.get()) && WV.with(|w| w.borrow().as_ref().map(|v| v.evaluate_script("window.__bb&&__bb.print()").is_ok()).unwrap_or(false))
}

fn on_ipc(msg: &str) {
    if msg == "shell" {
        SHELL_READY.with(|r| r.set(true));
        if let Some(js) = PENDING_JS.with(|p| p.borrow_mut().take()) { run_js(js); }
    } else if let Some(n) = msg.strip_prefix("ready:").and_then(|n| n.parse::<u64>().ok()) {
        ready(n, true);
    }
}

fn rect(x: f32, y: f32, w: f32, h: f32) -> wry::Rect {
    wry::Rect {
        position: wry::dpi::LogicalPosition::new(x as f64, y as f64).into(),
        size: wry::dpi::LogicalSize::new(w.max(1.0) as f64, h.max(1.0) as f64).into(),
    }
}

/// Where the view goes while it is "hidden": the same size, outside the window. A hidden
/// WebView2 stops painting, so a page swapped in meanwhile would first show its stale last
/// frame when it came back (a white or black flash); parked outside it stays current, and
/// keeps its layout (no reflow on return).
fn parked(ui: &AppWindow) -> wry::Rect {
    rect(-30000.0, -30000.0, ui.get_bw(), ui.get_bh())
}

fn place(ui: &AppWindow) -> wry::Rect {
    if SHOWN.with(|x| x.get()) { rect(ui.get_bx(), ui.get_by(), ui.get_bw(), ui.get_bh()) } else { parked(ui) }
}

fn build(ui: &AppWindow) -> Option<wry::WebView> {
    let r = parked(ui);
    let (br, bg, bb) = parse_hex(reader_bg());
    wry::WebViewBuilder::new()
        .with_html(shell_html())
        .with_background_color((br, bg, bb, 255))
        .with_bounds(r)
        .with_visible(true)
        .with_devtools(false)
        .with_ipc_handler(|req| {
            let m = req.body().clone();
            slint::Timer::single_shot(Duration::ZERO, move || on_ipc(&m));
        })
        .with_navigation_handler(|url| {
            // the shell itself is the only page; web links open in the browser, mailto: in the composer
            // A top-level navigation is never a click on a mail link (those are retargeted to a
            // new window); it comes from the page itself, so a mailto: here is dropped, not opened.
            if crate::mailto::parse(&url).is_some() || url.get(..5).is_some_and(|p| p.eq_ignore_ascii_case("file:")) { false }
            else if url.starts_with("http") { crate::oauth::open_browser(&url); false }
            else { true }
        })
        .with_new_window_req_handler(|url, _| {
            // links in a message (all retargeted to a new window): web links open in the
            // browser, mailto: in the composer
            if url.starts_with("http://") || url.starts_with("https://") { crate::oauth::open_browser(&url); }
            else if crate::mailto::parse(&url).is_some() { crate::mailto::request(url); }
            wry::NewWindowResponse::Deny
        })
        .build_as_child(&ui.window().window_handle())
        .ok()
}

/// Whether the reader itself is on screen (so a capture of the body belongs there).
fn reader_shown(ui: &AppWindow) -> bool {
    // settings and the composer replace the reader: the view goes at once, not after a frame
    has_message(ui) && ui.get_app() == 0 && !ui.get_show_settings() && (!ui.get_composing() || ui.get_dock_shows_body())
}

/// A message is open in the WebView renderer.
fn has_message(ui: &AppWindow) -> bool {
    ui.get_logged_in() && ui.get_current_mail() != "" && ui.get_render_mode() == 0
}

/// Capture the body as it is on screen and show it in the WebView's place.
fn freeze(ui: &AppWindow) -> bool {
    let s = ui.window().scale_factor();
    let crop = ((ui.get_bx() * s).round() as i32, (ui.get_by() * s).round() as i32, (ui.get_bw() * s).round() as i32, (ui.get_bh() * s).round() as i32);
    let Some(f) = crate::capture::grab(crate::capture::hwnd_of(ui.window()), Some(crop), 1) else { return false };
    let px = slint::SharedPixelBuffer::<slint::Rgba8Pixel>::clone_from_slice(&f.rgba, f.w, f.h);
    ui.set_body_snap(slint::Image::from_rgba8(px));
    ui.set_body_snap_on(true);
    true
}

fn set_shown(ui: &AppWindow, v: &wry::WebView, show: bool) {
    if SHOWN.with(|x| x.replace(show)) == show { return; }
    // keep keyboard focus in the native UI while the email view is hidden
    if !show { let _ = v.focus_parent(); }
    let _ = v.set_bounds(place(ui));
}

/// Called after Slint rendered a frame: a pending hide can go now that the capture is drawn.
fn after_render() {
    if !HIDE_PENDING.with(|h| h.get()) { return; }
    // after this frame is presented
    slint::Timer::single_shot(Duration::ZERO, || {
        if !HIDE_PENDING.with(|h| h.replace(false)) { return; }
        let ui = UI.with(|u| u.borrow().as_ref().and_then(|w| w.upgrade()));
        let Some(ui) = ui.filter(|u| !u.get_body_visible()) else { return };
        WV.with(|w| if let Some(v) = &*w.borrow() { set_shown(&ui, v, false); });
    });
}

/// Keep the WebView's place, size and visibility in step with the layout.
pub fn sync(ui: &AppWindow) {
    // Linux on Wayland: no child web view, so the native renderer shows mail
    if crate::WV_UNAVAILABLE.with(|x| x.get()) {
        if ui.get_render_mode() == 0 { ui.set_render_mode(1); if ui.get_current_mail() != "" { crate::render_current(ui); } }
        return;
    }
    let want = ui.get_body_visible();
    if UI.with(|u| u.borrow().is_none()) { UI.with(|u| *u.borrow_mut() = Some(ui.as_weak())); }
    // registering fails until the window has a renderer: retry until it takes
    if !NOTIFIER.with(|n| n.get()) {
        let ok = ui.window().set_rendering_notifier(|state, _| {
            if matches!(state, slint::RenderingState::AfterRendering) { after_render(); }
        }).is_ok();
        NOTIFIER.with(|n| n.set(ok));
    }
    WV.with(|w| {
        let mut w = w.borrow_mut();
        if w.is_none() {
            *w = build(ui);
            if let Some(v) = &*w { let _ = v.focus_parent(); HOST_BG.with(|h| h.borrow_mut().clear()); paint_host(v, reader_bg()); }
            if w.is_none() {
                if cfg!(target_os = "linux") { crate::WV_UNAVAILABLE.with(|x| x.set(true)); }
                return;
            }
        }
        let Some(v) = &*w else { return };
        let shown = SHOWN.with(|x| x.get());
        let now = (ui.get_bx(), ui.get_by(), ui.get_bw(), ui.get_bh());
        // the layout moved the body as it went away (composer, another screen): the view must
        // not follow it there for a frame, and a capture of the old place is of no use
        let moved = shown && !want && AT.with(|a| a.get()) != now;
        // a shown view only moves with a body that stays (hiding parks it)
        if want || !shown {
            let _ = v.set_bounds(place(ui));
            if want { AT.with(|a| a.set(now)); }
        }
        if want {
            HIDE_PENDING.with(|h| h.set(false));
            set_shown(ui, v, true);
            // let the WebView paint over the capture before the capture goes
            if ui.get_body_snap_on() {
                let wk = ui.as_weak();
                SNAP_TIMER.with(|t| t.start(slint::TimerMode::SingleShot, Duration::from_millis(80), move || {
                    if let Some(u) = wk.upgrade() { if SHOWN.with(|x| x.get()) { u.set_body_snap_on(false); } }
                }));
            }
        } else if shown && !HIDE_PENDING.with(|h| h.get()) {
            SNAP_TIMER.with(|t| t.stop());
            // the capture also covers the first frame back (app switch, composer, settings)
            let frozen = !moved && has_message(ui) && SHELL_READY.with(|r| r.get()) && freeze(ui);
            if frozen && reader_shown(ui) {
                // hide once the frame with the capture in its place is on screen
                HIDE_PENDING.with(|h| h.set(true));
                ui.window().request_redraw();
                // should no frame come (nothing to draw), hide anyway: a body over the overlay is worse
                slint::Timer::single_shot(Duration::from_millis(120), || { after_render(); });
            } else {
                if !frozen { ui.set_body_snap_on(false); }
                set_shown(ui, v, false);
            }
        }
        if ui.get_current_mail() == "" && !want && CLEARED.with(|c| !c.replace(true)) {
            // the reader closed: start the next message from an empty page, not this one
            drop(w);
            clear();
        } else if ui.get_current_mail() != "" {
            CLEARED.with(|c| c.set(false));
        }
    });
}

thread_local! {
    static CLEARED: Cell<bool> = const { Cell::new(true) };
    static NOTIFIER: Cell<bool> = const { Cell::new(false) };
    /// Where the shown view was last placed (logical x, y, w, h).
    static AT: Cell<(f32, f32, f32, f32)> = const { Cell::new((0.0, 0.0, 0.0, 0.0)) };
}

/// Automation: the probe asks whether the shell is up.
pub fn shell_ready() -> bool { SHELL_READY.with(|r| r.get()) }

/// Automation: whether the after-render hook is in place, and whether the view is on screen.
pub fn state() -> String {
    format!("notifier {} shown {} hide-pending {}", NOTIFIER.with(|n| n.get()), SHOWN.with(|x| x.get()), HIDE_PENDING.with(|h| h.get()))
}
