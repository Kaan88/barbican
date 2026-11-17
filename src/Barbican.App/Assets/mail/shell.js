// The body view's shell (PORT-PLAN.md §3.6; port of the Rust app's mailview.rs SHELL_JS at
// rust-final). One persistent document that never navigates. Each message is a sandboxed srcdoc
// iframe with its own CSP, loaded hidden next to the one on screen; the shell posts "ready:N" once
// the frame is laid out (and after the shell ran the text sizing and the dark pass on it), the host
// updates its native header and calls reveal(N) in the same dispatcher turn, and the old frame is
// dropped. Nothing blank, white or black is ever shown in between.
//
// No script ever runs inside a message frame (Fable review 1 F-03): the sandbox never carries
// allow-scripts and the frame's CSP is script-src 'none'. The dark pass (email_dark.js, prepended
// to this script by Core's ShellDocument as window.__bbDarkPass) and the text sizing (size() below,
// the port of A11y's sizing script) run from here against the frame document, which the shell may
// read because the frame keeps allow-same-origin. This script runs only by its CSP hash.
//
// Commands arrive either as functions on window.__bb (ExecuteScriptAsync) or as web messages
// ({op:"show"|"reveal"|"cancel"|"dim"|"clear"|"print", ...}; PostWebMessageAsJson), which carries
// large pages without NavigateToString's 2 MB limit. Messages to the host are plain strings:
//   shell            the shell is up (commands sent before this are queued by the host)
//   ready:N          document N is laid out and may be revealed
//   key:FLAGS|KEY    a shortcut pressed while the page has focus (c=ctrl/meta, s=shift, a=alt)
//   dirty            the visible page changed (scroll, resize, load, selection)
//   ctx:X,Y          a right-click at page coordinates X,Y (the host opens its native menu)
//   link:URL         a link in a message was activated (the host checks the scheme and routes it)
// The host validates every message (src/Barbican.Core/Mail/Body/ShellMessage.cs) and drops the rest.
//
// Links (docs/adr/0005 decision 4): a capturing click handler in every frame document takes each
// activated link itself and posts link:URL, on both OSes (on Linux neither a new window nor a
// navigation event fires for a target=_blank click, so the link would be lost). As a fallback every
// link and form is also retargeted to _blank, which the sandbox (no allow-popups) refuses: a link the
// handler misses goes nowhere instead of navigating the frame.
// This script never navigates: it never assigns location, never calls location.assign/replace,
// window.open or document.write (tests/Barbican.Core.Tests/Mail/Body/ShellScriptTests.cs).
(function () {
  var cur = null, nxt = null, seq = 0;
  // The only sandbox keywords a frame may get (the same list as Core's MailSandbox.Allowed): never
  // allow-scripts, allow-top-navigation*, allow-popups*, allow-forms or allow-downloads, whatever the host sends.
  var SANDBOX_OK = ['allow-same-origin', 'allow-modals'];
  function sandbox(sb) {
    var out = [];
    var t = typeof sb === 'string' ? sb.split(/\s+/) : [];
    for (var i = 0; i < t.length; i++) if (SANDBOX_OK.indexOf(t[i]) >= 0 && out.indexOf(t[i]) < 0) out.push(t[i]);
    if (out.indexOf('allow-same-origin') < 0) out.unshift('allow-same-origin');
    return out.join(' ');
  }
  // WebView2 (Windows) exposes chrome.webview; Uno's WebKitGTK view (Linux) exposes no shim, only
  // the script message handler it registers as "unoWebView" (spike S2, docs/adr/0005).
  function post(m) {
    try {
      if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage(m); return; }
      window.webkit.messageHandlers.unoWebView.postMessage(m);
    } catch (e) { }
  }
  // The view owns the OS keyboard focus once it was clicked: pass the app's shortcuts back.
  function kd(e) {
    try {
      if (e.defaultPrevented || e.isComposing) return;
      var t = e.target, n = t && t.tagName;
      if (t && (t.isContentEditable || n === 'INPUT' || n === 'TEXTAREA' || n === 'SELECT')) return;
      var k = e.key, c = e.ctrlKey || e.metaKey, a = e.altKey, fw = false;
      if (c && !a) { fw = ['k', 'e', 'f', 'm'].indexOf(k.toLowerCase()) >= 0; }
      else if (!a) { fw = k.length === 1 && k !== ' ' || k === 'Escape' || k === 'Delete' || k === 'F5' || k === 'F9'; }
      if (!fw) return;
      e.preventDefault();
      post('key:' + (c ? 'c' : '') + (e.shiftKey ? 's' : '') + (a ? 'a' : '') + '|' + (k.length === 1 && c ? k.toLowerCase() : k));
    } catch (x) { }
  }
  function ctx(e) {
    try { e.preventDefault(); post('ctx:' + Math.round(e.clientX) + ',' + Math.round(e.clientY)); } catch (x) { }
  }
  // A primary or middle click (or Enter) on a link: the host gets the absolute URL, the frame stays.
  function link(e) {
    try {
      if (e.button !== 0 && e.button !== 1) return;
      var a = e.target && e.target.closest ? e.target.closest('a[href],area[href]') : null;
      if (!a) return;
      e.preventDefault();
      e.stopPropagation();
      var href = a.href;
      if (typeof href === 'string' && href.length > 0 && href.length <= 8000) post('link:' + href);
    } catch (x) { }
  }
  document.addEventListener('keydown', kd, true);
  document.addEventListener('contextmenu', ctx, true);
  function fix(d) {
    try {
      var a = d.querySelectorAll('a,area,form');
      for (var i = 0; i < a.length; i++) a[i].setAttribute('target', '_blank');
    } catch (e) { }
    try {
      if (!d.__bbd) {
        d.__bbd = 1;
        var t = 0, p = function () { if (t) return; t = 1; requestAnimationFrame(function () { t = 0; post('dirty'); }); };
        d.addEventListener('keydown', kd, true);
        d.addEventListener('contextmenu', ctx, true);
        d.addEventListener('click', link, true);
        d.addEventListener('auxclick', link, true);
        d.addEventListener('scroll', p, true);
        d.addEventListener('load', p, true);
        d.addEventListener('selectionchange', p);
        d.defaultView.addEventListener('resize', p);
      }
    } catch (e) { }
  }
  // Settings > Accessibility text size, minimum size and fit to the pane (Core A11y.Sizing; Bulwark applyMailTextSize).
  function size(d, z) {
    try {
      if (!z || !d.body) return;
      var f = +z[0] || 1, m = +z[1] || 0, fit = !!z[2], v = d.defaultView, b = d.body;
      if (f != 1 || m > 0) {
        var skip = { IMG: 1, BR: 1, HR: 1, WBR: 1, PICTURE: 1, SOURCE: 1, VIDEO: 1, AUDIO: 1, CANVAS: 1, IFRAME: 1, OBJECT: 1, EMBED: 1, STYLE: 1, SCRIPT: 1, TEMPLATE: 1 };
        var els = [b], any = false, sz = [];
        b.querySelectorAll('*').forEach(function (e) { if (skip[e.tagName] || e.closest('svg')) return; els.push(e); });
        els.forEach(function (e) {
          var s = v.getComputedStyle(e), p = parseFloat(s.fontSize);
          if (!(p > 0)) return;
          var lh = s.lineHeight.slice(-2) == 'px' ? parseFloat(s.lineHeight) : null;
          var n = Math.max(p * f, m || 0);
          if (Math.abs(n - p) > 0.01) any = true;
          sz.push([e, n, lh === null ? null : lh * n / p]);
        });
        if (any) sz.forEach(function (x) {
          x[0].style.setProperty('font-size', Math.round(x[1] * 100) / 100 + 'px', 'important');
          if (x[2] !== null) x[0].style.setProperty('line-height', Math.round(x[2] * 100) / 100 + 'px', 'important');
        });
      }
      if (fit) { var de = d.documentElement, w = de.scrollWidth, c = de.clientWidth; if (w > c + 2) b.style.zoom = (c / w).toFixed(4); }
    } catch (e) { }
  }
  // The dark conversion of a dark-pending page; the pending class goes whatever happens (its CSS hides the body).
  function dark(d) {
    try { if (window.__bbDarkPass) window.__bbDarkPass(d); } catch (e) { }
    try { d.documentElement.classList.remove('barbican-dark-pending'); } catch (e) { }
  }
  var bb = window.__bb = {
    show: function (s, html, bg, sb, dk, sz) {
      seq = s;
      if (nxt) { nxt.remove(); nxt = null; }
      var f = document.createElement('iframe');
      f.setAttribute('sandbox', sandbox(sb));
      f.setAttribute('title', 'Message');
      f.style.cssText = 'position:absolute;left:0;top:0;width:100%;height:100%;border:0;margin:0;visibility:hidden;background:' + bg;
      f._bg = bg;
      nxt = f;
      var done = false;
      // only the frame's own srcdoc is ever finished and revealed (a frame that navigated elsewhere never is)
      function finish(d) {
        if (done || seq !== s || !d || d.URL !== 'about:srcdoc' || !d.body) return false;
        done = true;
        fix(d);
        size(d, sz);
        if (dk) dark(d);
        void d.documentElement.offsetHeight;
        // One frame before the host hears of the page, so data: images have decoded when it is revealed (Fable M1 verify). A hidden or
        // throttled window may never run a frame: a timer is the fallback, so the page is never held back for good.
        var sent = false;
        var ready = function () { if (sent) return; sent = true; if (seq === s) post('ready:' + s); };
        requestAnimationFrame(ready);
        setTimeout(ready, 34);
        return true;
      }
      f.addEventListener('load', function () { var d = null; try { d = f.contentDocument; } catch (e) { } finish(d); });
      f.srcdoc = html;
      document.body.appendChild(f);
      // Polled on the task queue, not per animation frame: a frame-paced poll plus a frame for layout cost
      // two to three frames (about 55 ms in a hidden run) before the host even heard of the page. Reading
      // offsetHeight lays the page out now, so the reveal that follows paints a finished page.
      (function poll() {
        if (done || seq !== s) return;
        var d = null;
        try { d = f.contentDocument; } catch (e) { }
        if (d && d.readyState !== 'loading' && finish(d)) return;
        setTimeout(poll, 1);
      })();
    },
    reveal: function (s) {
      if (s !== seq || !nxt) return;
      var f = nxt;
      nxt = null;
      document.documentElement.style.background = f._bg;
      f.style.visibility = 'visible';
      if (cur) cur.remove();
      cur = f;
    },
    cancel: function (s) { seq = s; if (nxt) { nxt.remove(); nxt = null; } },
    dim: function (on) { if (cur) cur.style.opacity = on ? '0.4' : ''; },
    clear: function (bg) {
      seq++;
      if (nxt) { nxt.remove(); nxt = null; }
      if (cur) { cur.remove(); cur = null; }
      document.documentElement.style.background = bg;
    },
    print: function () { if (cur) cur.contentWindow.print(); }
  };
  try {
    // Web messages from the host: WebView2 only (Uno 6.7 implements no PostWebMessageAs*).
    window.chrome.webview.addEventListener('message', function (e) {
      var m = e.data;
      if (typeof m === 'string') { try { m = JSON.parse(m); } catch (x) { return; } }
      if (!m || !m.op) return;
      switch (m.op) {
        case 'show': bb.show(m.seq, m.html, m.bg, m.sandbox === undefined ? null : m.sandbox, !!m.dark, m.size || null); break;
        case 'reveal': bb.reveal(m.seq); break;
        case 'cancel': bb.cancel(m.seq); break;
        case 'dim': bb.dim(!!m.on); break;
        case 'clear': bb.clear(m.bg); break;
        case 'print': bb.print(); break;
      }
    });
  } catch (e) { }
  post('shell');
})();
