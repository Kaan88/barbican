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
use std::sync::atomic::{AtomicBool, Ordering};
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
    /// Debounce for refreshing the body capture that sits under the shown view.
    static SNAP_TIMER: slint::Timer = slint::Timer::default();
    /// The capture under the view matches what the view shows: an overlay can hide the view in
    /// the very event that opens it, with the capture already in place (no frame with the
    /// overlay cut off by the child window).
    static FRESH: Cell<bool> = const { Cell::new(false) };
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
// the view owns the OS keyboard focus once it was clicked: pass the app's shortcuts back to the native side
function kd(e){try{if(e.defaultPrevented||e.isComposing)return;var t=e.target,n=t&&t.tagName;
if(t&&(t.isContentEditable||n==='INPUT'||n==='TEXTAREA'||n==='SELECT'))return;
var k=e.key,c=e.ctrlKey||e.metaKey,a=e.altKey,fw=false;
if(c&&!a){fw=['k','e','f','m'].indexOf(k.toLowerCase())>=0}
else if(!a){fw=k.length===1&&k!==' '||k==='Escape'||k==='Delete'||k==='F5'||k==='F9'}
if(!fw)return;e.preventDefault();post('key:'+(c?'c':'')+(e.shiftKey?'s':'')+(a?'a':'')+'|'+(k.length===1&&c?k.toLowerCase():k))}catch(x){}}
document.addEventListener('keydown',kd,true);
// A message can name its own elements (<img name="URL">, <form><input name="setAttribute">):
// named access beats the built-in members of `document` and of forms, so nothing here reads a
// member of a message's document or element directly. Everything goes through the prototypes
// (saved before any message exists), and the "handlers attached" mark lives in a WeakSet.
// (mailview::sanitize_mail_html also drops the names that could shadow, as a second layer.)
var DP=Document.prototype,EP=Element.prototype,ET=EventTarget.prototype;
function dget(n){return Object.getOwnPropertyDescriptor(DP,n).get}
var gURL=dget('URL'),gBody=dget('body'),gDE=dget('documentElement'),gView=dget('defaultView'),gReady=dget('readyState'),
qsa=DP.querySelectorAll,addL=ET.addEventListener,setAttr=EP.setAttribute,
gCls=Object.getOwnPropertyDescriptor(EP,'classList').get,cHas=DOMTokenList.prototype.contains,seen=new WeakSet();
function fix(d){try{var a=qsa.call(d,'a,area,form');for(var i=0;i<a.length;i++){try{setAttr.call(a[i],'target','_blank')}catch(e){}}}catch(e){}
try{if(!seen.has(d)){seen.add(d);var t=0,p=function(){if(t)return;t=1;requestAnimationFrame(function(){t=0;post('dirty')})};
addL.call(d,'keydown',kd,true);addL.call(d,'scroll',p,true);addL.call(d,'load',p,true);addL.call(d,'selectionchange',p);addL.call(gView.call(d),'resize',p)}}catch(e){}}
function laidOut(d){try{return gURL.call(d)==='about:srcdoc'&&gReady.call(d)!=='loading'&&!!gBody.call(d)&&!cHas.call(gCls.call(gDE.call(d)),'barbican-dark-pending')}catch(e){return false}}
window.__bb={
show:function(s,html,bg,sb){seq=s;if(nxt){nxt.remove();nxt=null}
var f=document.createElement('iframe');if(sb!==null)f.setAttribute('sandbox',sb);f.setAttribute('title','Message');
f.style.cssText='position:absolute;left:0;top:0;width:100%;height:100%;border:0;margin:0;visibility:hidden;background:'+bg;
f._bg=bg;nxt=f;var done=false;
function ready(){if(done||seq!==s)return;done=true;post('ready:'+s)}
addL.call(f,'load',function(){try{fix(f.contentDocument)}catch(e){}ready()});f.srcdoc=html;document.body.appendChild(f);
(function poll(){if(done||seq!==s)return;var d=null;try{d=f.contentDocument}catch(e){}
if(d&&laidOut(d)){fix(d);requestAnimationFrame(ready);return}
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
<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline' *; font-src * data:; img-src * data:; script-src 'unsafe-inline'; base-uri 'none'; frame-src 'self' about:; object-src 'none'; form-action 'none'\">\
<style>html,body{{margin:0;padding:0;height:100%;overflow:hidden}}</style></head><body><script>{SHELL_JS}</script></body></html>"
    )
}

/// The frame's sandbox: same origin (so the shell can tell when it is laid out), popups for
/// links (they open in the browser), modals for printing; scripts only for our own script.
///
/// Decision (P0, C# D-071 asks for no `allow-scripts` ever): kept as is. A frame gets
/// `allow-scripts` only when our dark-mode or accessibility script runs in it, and then the
/// frame's CSP admits exactly that script by its sha256 (`script-src 'sha256-..'`), so a
/// message's own scripts, inline handlers and `javascript:` links still do not run. The C#
/// alternative (the shell runs the effect against the frame's document) was not taken because
/// it cannot be done safely without a real window to check it in:
///   * the shell's CSP forbids eval, so the script would have to be sent as a function literal
///     in `evaluate_script` and called with the frame's document, and the accessibility script
///     (src/a11y.rs `mail_script`, which this change may not touch) calls the bare global
///     `addEventListener('DOMContentLoaded', ..)`, so it needs a shim;
///   * the timing changes (a frame's own script runs at DOMContentLoaded after its blocking
///     stylesheets; the shell would poll), which can flash light mail or compute colours before
///     remote CSS applied, and the dark pass is the one thing that must not misfire;
///   * it moves message-DOM walking into the shell's privileged realm (it holds `window.ipc`).
/// What makes the current setup safe: the CSP hash (mail cannot add a script, and a mail
/// `<meta http-equiv=CSP>` can only narrow it), `sanitize_mail_html` (no `<base>`, no meta
/// refresh, no shadowing names, so even our script's `document.X` reads are the real ones),
/// and `base-uri 'none'`. Revisit when the shell-side variant can be tried in a live window.
fn sandbox(script: bool) -> &'static str {
    if script {
        "allow-same-origin allow-popups allow-popups-to-escape-sandbox allow-modals allow-scripts"
    } else {
        "allow-same-origin allow-popups allow-popups-to-escape-sandbox allow-modals"
    }
}

/// Set once the shell reported in: from then on the view may not navigate anywhere else.
/// (Until then the shell's own initial load is on its way, in whatever URL form the platform
/// reports it, so that one load is not judged.)
static NAV_LOCKED: AtomicBool = AtomicBool::new(false);

#[derive(Debug, PartialEq)]
enum Nav { Allow, Browser, Cancel }

/// What to do with a top-level navigation of the mail view.
fn nav_decision(url: &str, locked: bool) -> Nav {
    let u = url.trim();
    let l = u.to_ascii_lowercase();
    // web links go to the browser (and the view stays), mailto: and files never load here
    if l.starts_with("http://") || l.starts_with("https://") { return Nav::Browser; }
    if crate::mailto::parse(u).is_some() || l.starts_with("file:") { return Nav::Cancel; }
    if !locked { return if l.starts_with("http") { Nav::Browser } else { Nav::Allow }; }
    // the shell (NavigateToString reports a data: URL, WebKitGTK about:blank) and the frames
    if l == "about:srcdoc" || l == "about:blank" || l.starts_with("data:text/html") { Nav::Allow } else { Nav::Cancel }
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
    mark_dirty();
}

/// The theme changed with no message open: the empty view takes the reader's new colour.
pub fn theme_changed() {
    mark_dirty();
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

/// The email view is a child window: once it was clicked it keeps the OS keyboard focus even when
/// the user then clicks the native UI, and every key (Ctrl+K, ?, the single-key shortcuts) would
/// go to the page instead of the app. A press on the main window (the view's own clicks never
/// reach it) hands the focus back.
pub fn install_focus_hook(ui: &AppWindow) {
    #[cfg(windows)]
    {
        use slint::winit_030::{winit::event::{ElementState, WindowEvent}, EventResult, WinitWindowAccessor};
        ui.window().on_winit_window_event(|_, ev| {
            if let WindowEvent::MouseInput { state: ElementState::Pressed, .. } = ev {
                WV.with(|w| if let Ok(w) = w.try_borrow() { if let Some(v) = &*w { let _ = v.focus_parent(); } });
            }
            EventResult::Propagate
        });
    }
    #[cfg(not(windows))]
    let _ = ui;
}

/// A key the email view caught (`flags|key`): replay it in the native window so the app's
/// shortcuts work with the view focused.
/// Parse a forwarded key (`flags|key`): the modifiers (c, s, a) and the key text for Slint.
fn parse_key(spec: &str) -> Option<(Vec<slint::platform::Key>, String)> {
    use slint::platform::Key;
    let (flags, key) = spec.split_once('|')?;
    let text = match key {
        "Escape" => char::from(Key::Escape).to_string(),
        "Delete" => char::from(Key::Delete).to_string(),
        "F5" => char::from(Key::F5).to_string(),
        "F9" => char::from(Key::F9).to_string(),
        k if k.chars().count() == 1 => k.to_string(),
        _ => return None,
    };
    let mut mods = vec![];
    if flags.contains('c') { mods.push(Key::Control); }
    if flags.contains('s') { mods.push(Key::Shift); }
    if flags.contains('a') { mods.push(Key::Alt); }
    Some((mods, text))
}

fn forward_key(ui: &AppWindow, spec: &str) {
    use slint::platform::WindowEvent;
    let Some((mods, text)) = parse_key(spec) else { return };
    let win = ui.window();
    for m in &mods { win.dispatch_event(WindowEvent::KeyPressed { text: (*m).into() }); }
    win.dispatch_event(WindowEvent::KeyPressed { text: text.as_str().into() });
    win.dispatch_event(WindowEvent::KeyReleased { text: text.as_str().into() });
    for m in mods.iter().rev() { win.dispatch_event(WindowEvent::KeyReleased { text: (*m).into() }); }
}

// ---- message HTML sanitising (the parts the frame's CSP and sandbox cannot do) ----

/// Tags `document` exposes by name (HTML "named access on Document"): a message with
/// `<img name="URL">` makes `document.URL` that element, `name="body"` makes `document.body`
/// it, `name="querySelectorAll"` replaces the method, and so on, for every member of
/// Document.prototype / Node / EventTarget (the shell and the dark script use URL, body,
/// documentElement, defaultView, readyState, head, createElement, querySelectorAll,
/// addEventListener, getComputedStyle). Rather than chase a list of names (a value can be
/// written with character references), `name` goes from all of these, and `id` from the ones
/// that are exposed by id as well (`object`). An `img` is exposed by id only together with a
/// name, which is gone, so ordinary `id`s on images (CSS hooks in newsletters) survive.
const NAMED_ACCESS_TAGS: &[&str] = &["img", "form", "embed", "object", "iframe", "applet", "frame"];
const NAMED_ACCESS_ID_TAGS: &[&str] = &["object", "applet"];

fn is_html_ws(b: u8) -> bool { matches!(b, b' ' | b'\t' | b'\n' | b'\r' | 0x0c) }

/// Remove from a message's HTML what would let it escape its frame's rules:
///  * `<base>` (it would move every relative URL; the frame's CSP also says `base-uri 'none'`),
///  * `<meta http-equiv=..>` of any kind (`refresh` redirects the frame; `set-cookie` and the
///    rest have no business in a message),
///  * `name` (and `id`) attributes that shadow `document` members (see `NAMED_ACCESS_TAGS`).
/// Every `<letter` in the text is looked at as a possible tag start, wherever it sits (inside a
/// comment, a quoted value, a textarea): over-removing from inert text is harmless, but a
/// scanner that disagreed with the browser about where a tag starts could be fooled. The
/// removals repeat until nothing changes, so deleting one tag cannot join the pieces around
/// it into a new one (`<me<base>ta http-equiv=refresh>`). Input made to cost too much
/// to scan is not rendered as HTML at all.
pub fn sanitize_mail_html(html: &str) -> String {
    let mut cur = std::borrow::Cow::Borrowed(html);
    // each change makes the text shorter, so this ends; the cap only bounds the work
    for _ in 0..64 {
        match sanitize_pass(&cur) {
            Ok(None) => return cur.into_owned(),
            Ok(Some(next)) => cur = std::borrow::Cow::Owned(next),
            Err(()) => return format!("<pre>{}</pre>", crate::esc(html)),
        }
    }
    format!("<pre>{}</pre>", crate::esc(html))
}

/// One pass: `Ok(None)` when nothing needs removing, `Err` when the work budget ran out.
fn sanitize_pass(s: &str) -> Result<Option<String>, ()> {
    let b = s.as_bytes();
    let mut budget: usize = b.len().saturating_mul(24).saturating_add(1 << 20);
    let mut dels: Vec<(usize, usize)> = Vec::new();
    let mut i = 0;
    while i < b.len() {
        let Some(off) = b[i..].iter().position(|&c| c == b'<') else { break };
        i += off;
        if !b.get(i + 1).is_some_and(|c| c.is_ascii_alphabetic()) { i += 1; continue; }
        // tag name
        let mut j = i + 1;
        while j < b.len() && !is_html_ws(b[j]) && b[j] != b'/' && b[j] != b'>' { j += 1; }
        let name = s[i + 1..j].to_ascii_lowercase();
        // attributes: (lowercase name, start, end including its value)
        let mut attrs: Vec<(String, usize, usize)> = Vec::new();
        let mut p = j;
        let end = loop {
            while p < b.len() && (is_html_ws(b[p]) || b[p] == b'/') { p += 1; }
            if p >= b.len() { break b.len(); }
            if b[p] == b'>' { break p + 1; }
            let start = p;
            p += 1; // a leading '=' belongs to the name
            while p < b.len() && !is_html_ws(b[p]) && b[p] != b'/' && b[p] != b'>' && b[p] != b'=' { p += 1; }
            let aname = s.get(start..p).unwrap_or("").to_ascii_lowercase();
            let mut q = p;
            while q < b.len() && is_html_ws(b[q]) { q += 1; }
            if q < b.len() && b[q] == b'=' {
                q += 1;
                while q < b.len() && is_html_ws(b[q]) { q += 1; }
                if q < b.len() && (b[q] == b'"' || b[q] == b'\'') {
                    let quote = b[q];
                    q += 1;
                    while q < b.len() && b[q] != quote { q += 1; }
                    if q < b.len() { q += 1; }
                } else {
                    while q < b.len() && !is_html_ws(b[q]) && b[q] != b'>' { q += 1; }
                }
                p = q;
            }
            attrs.push((aname, start, p));
        };
        budget = budget.checked_sub(end - i).ok_or(())?;
        let has = |n: &str| attrs.iter().any(|a| a.0 == n);
        if name == "base" || (name == "meta" && has("http-equiv")) {
            dels.push((i, end));
        } else if NAMED_ACCESS_TAGS.contains(&name.as_str()) {
            let id_too = NAMED_ACCESS_ID_TAGS.contains(&name.as_str());
            for (n, a, e) in &attrs {
                if n == "name" || (id_too && n == "id") { dels.push((*a, *e)); }
            }
        }
        // the next candidate may sit inside this tag's quoted values
        i += 1;
    }
    if dels.is_empty() { return Ok(None); }
    dels.sort_unstable();
    let mut out = String::with_capacity(s.len());
    let mut at = 0;
    for (a, e) in dels {
        if e <= at { continue; }
        let a = a.max(at);
        if !s.is_char_boundary(a) || !s.is_char_boundary(e) { continue; }
        out.push_str(&s[at..a]);
        at = e;
    }
    out.push_str(&s[at..]);
    Ok(if out.len() == s.len() { None } else { Some(out) })
}

#[cfg(test)]
mod tests {
    use super::{nav_decision, parse_key, sanitize_mail_html as san, Nav};
    use slint::platform::Key;

    #[test]
    fn plain_mail_is_left_alone() {
        let h = "<p id=\"a\">Hi <img src=\"x.png\" id=\"logo\" alt=\"n\"> <a href=\"https://e.x/?a=1&b=2\">go</a><meta name=\"color-scheme\" content=\"dark\"></p>";
        assert_eq!(san(h), h);
    }

    #[test]
    fn base_is_removed() {
        for h in ["<base href=\"http://evil/\">", "<BASE HREF=http://evil/>", "<base/href='//evil/'>", "<base\n target=_top href=x>"] {
            let o = san(&format!("a{h}b<img src=\"x.png\">"));
            assert!(!o.to_ascii_lowercase().contains("<base"), "{o}");
            assert!(o.starts_with('a') && o.contains("<img src=\"x.png\">"), "{o}");
        }
    }

    #[test]
    fn meta_refresh_is_removed() {
        for h in [
            "<meta http-equiv=\"refresh\" content=\"0;url=http://evil/\">",
            "<META HTTP-EQUIV=ReFresh CONTENT='0; URL=//evil'>",
            "<meta content=\"0;url=x\" http-equiv = refresh>",
            "<meta/http-equiv=refresh content=0>",
            "<meta http-equiv=\"re&#102;resh\" content=0>",
            "<meta http-equiv=set-cookie content=\"a=b\">",
            "<noscript><meta http-equiv=refresh content=0></noscript>",
        ] {
            let o = san(&format!("x{h}y"));
            assert!(!o.to_ascii_lowercase().contains("http-equiv") && !o.to_ascii_lowercase().contains("<meta"), "{h} -> {o}");
        }
        // metas that cannot redirect stay
        assert!(san("<meta name=\"color-scheme\" content=\"light dark\">").contains("color-scheme"));
    }

    #[test]
    fn removal_cannot_build_a_new_tag() {
        let o = san("<me<base>ta http-equiv=refresh content=0>");
        assert!(!o.contains("http-equiv"), "{o}");
        let o = san("<b<meta http-equiv=x>ase href=//evil/>");
        assert!(!o.to_ascii_lowercase().contains("<base"), "{o}");
    }

    #[test]
    fn tag_start_is_found_where_a_naive_scanner_would_skip_it() {
        // a comment, a textarea or a quoted value that the browser ends earlier than a scanner thinks
        for h in [
            "<!-- <a title=\"--> <meta http-equiv=refresh content=0> \"> -->",
            "<textarea><a title=\"</textarea><meta http-equiv=refresh content=0>\">",
            "<svg><style><meta http-equiv=refresh content=0></style></svg>",
            "<style></style><meta http-equiv=refresh content=0>",
        ] {
            assert!(!san(h).contains("http-equiv"), "{h}");
        }
        let o = san("<textarea><a title=\"</textarea><img name=URL>\">");
        assert!(!o.contains("name=URL"), "{o}");
    }

    #[test]
    fn shadowing_names_are_removed() {
        for n in ["URL", "body", "querySelectorAll", "addEventListener", "__bbd", "documentElement", "defaultView", "readyState", "head", "createElement", "UR&#76;"] {
            for q in ["\"", "'", ""] {
                let o = san(&format!("<img src=\"x.png\" name={q}{n}{q} alt=\"a\">"));
                assert!(!o.contains("name"), "{n}: {o}");
                assert!(o.contains("src=\"x.png\"") && o.contains("alt=\"a\""), "{o}");
            }
        }
        let o = san("<img id=\"body\" name=\"body\" src=x><form name=\"URL\"><input name=\"setAttribute\"></form><object id=\"URL\" name=x></object><embed name=y><iframe name=z></iframe>");
        assert!(!o.contains("name=\"body\"") && !o.contains("name=\"URL\"") && !o.contains("id=\"URL\"") && !o.contains("name=y") && !o.contains("name=z"), "{o}");
        // a plain image id (no name) is not exposed on document and stays; so do names on other tags
        assert!(san("<img id=logo src=x>").contains("id=logo"));
        assert!(san("<a name=top></a>").contains("name=top"));
    }

    #[test]
    fn hostile_input_is_bounded_and_falls_back_to_text() {
        let big = "<b ".repeat(400_000);
        let t = std::time::Instant::now();
        let o = san(&big);
        assert!(t.elapsed() < std::time::Duration::from_secs(20));
        assert!(o.starts_with("<pre>") || o == big, "unexpected rewrite");
        let quoted = "<a x=\"".repeat(200_000);
        let o = san(&quoted);
        assert!(o.starts_with("<pre>") || o == quoted);
    }

    #[test]
    fn navigation_is_filtered() {
        // before the shell reported in: its own load passes whatever form the platform reports
        assert_eq!(nav_decision("data:text/html;charset=utf-8,<p>", false), Nav::Allow);
        assert_eq!(nav_decision("https://example.org/", false), Nav::Browser);
        // after: only the shell, blank and srcdoc
        for u in ["about:srcdoc", "about:blank", "data:text/html;charset=utf-8;base64,AAAA"] {
            assert_eq!(nav_decision(u, true), Nav::Allow, "{u}");
        }
        for u in ["javascript:alert(1)", "data:text/plain,x", "blob:https://x/y", "ftp://x/", "about:config", "ms-appx://x", "view-source:x", "mailto:a@b.c", "FILE:///c:/x", "chrome://x", "wry://localhost/x"] {
            assert_eq!(nav_decision(u, true), Nav::Cancel, "{u}");
        }
        // links still reach the browser, as before
        assert_eq!(nav_decision("https://example.org/a", true), Nav::Browser);
        assert_eq!(nav_decision("HTTP://example.org/a", true), Nav::Browser);
    }

    #[test]
    fn shell_and_policy_are_hardened() {
        assert!(super::shell_html().contains("base-uri 'none'"));
        // the shell reads message documents through saved prototype members only
        let js = super::SHELL_JS;
        for bad in ["d.URL", "d.body", "d.querySelectorAll", "d.__bbd", "d.defaultView", "d.documentElement", "d.readyState", "a[i].setAttribute"] {
            assert!(!js.contains(bad), "{bad}");
        }
        assert!(js.contains("Document.prototype") && js.contains("new WeakSet()"));
    }

    #[test]
    fn forwarded_keys_parse() {
        assert_eq!(parse_key("c|k"), Some((vec![Key::Control], "k".into())));
        assert_eq!(parse_key("s|?"), Some((vec![Key::Shift], "?".into())));
        assert_eq!(parse_key("cs|m"), Some((vec![Key::Control, Key::Shift], "m".into())));
        assert_eq!(parse_key("|j"), Some((vec![], "j".into())));
        assert_eq!(parse_key("|Escape").map(|k| k.1), Some(char::from(Key::Escape).to_string()));
        assert_eq!(parse_key("|Delete").map(|k| k.1), Some(char::from(Key::Delete).to_string()));
        // anything else the page sends is dropped
        assert_eq!(parse_key("|Enter"), None);
        assert_eq!(parse_key("nokey"), None);
        assert_eq!(parse_key("|"), None);
    }
}

fn on_ipc(msg: &str) {
    if msg == "shell" {
        SHELL_READY.with(|r| r.set(true));
        NAV_LOCKED.store(true, Ordering::Relaxed);
        if let Some(js) = PENDING_JS.with(|p| p.borrow_mut().take()) { run_js(js); }
    } else if let Some(n) = msg.strip_prefix("ready:").and_then(|n| n.parse::<u64>().ok()) {
        ready(n, true);
    } else if let Some(k) = msg.strip_prefix("key:") {
        if let Some(u) = ui() { forward_key(&u, k); }
    } else if msg == "dirty" {
        mark_dirty();
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
            // A top-level navigation is never a click on a mail link (those are retargeted to a
            // new window); it comes from the page itself. Once the shell is up it is the only
            // page: everything but about:srcdoc/about:blank/the shell's own data: page is
            // cancelled, and a web URL is handed to the browser instead.
            match nav_decision(&url, NAV_LOCKED.load(Ordering::Relaxed)) {
                Nav::Allow => true,
                Nav::Browser => { crate::oauth::open_browser(&url); false }
                Nav::Cancel => false,
            }
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

/// The view's content changed (scroll, resize, a new page, theme): the capture under it is out
/// of date until it is taken again, shortly after the change stops.
pub fn mark_dirty() {
    FRESH.with(|f| f.set(false));
    SNAP_TIMER.with(|t| t.start(slint::TimerMode::SingleShot, Duration::from_millis(140), || {
        let Some(ui) = ui() else { return };
        if SHOWN.with(|x| x.get()) && ui.get_body_visible() && has_message(&ui) && SHELL_READY.with(|r| r.get()) && freeze(&ui) {
            FRESH.with(|f| f.set(true));
        }
    }));
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
        let moved_to = AT.with(|a| a.get());
        let moved = shown && !want && moved_to != now;
        // a shown view only moves with a body that stays (hiding parks it)
        if want || !shown {
            let _ = v.set_bounds(place(ui));
            if want { AT.with(|a| a.set(now)); }
        }
        if want {
            HIDE_PENDING.with(|h| h.set(false));
            set_shown(ui, v, true);
            // keep the capture under the view current (it is what shows when an overlay hides the view)
            if !shown || moved_to != now || (!FRESH.with(|f| f.get()) && !SNAP_TIMER.with(|t| t.running())) { mark_dirty(); }
        } else if shown && !HIDE_PENDING.with(|h| h.get()) {
            SNAP_TIMER.with(|t| t.stop());
            if !moved && FRESH.with(|f| f.get()) && ui.get_body_snap_on() && has_message(ui) {
                // the capture is already drawn under the view: hide it in this very event, before
                // the frame that brings the overlay
                set_shown(ui, v, false);
            } else {
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
