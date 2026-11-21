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
//   ctx:X,Y          a right-click at page coordinates X,Y (the host opens its native menu); a sel: with the current selection is posted just before it
//   sel:TEXT         the selected text changed (debounced; at most 4000 characters; empty when it was cleared)
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
//
// The frame document belongs to the mail: an element named "URL", "body" or "querySelectorAll"
// shadows that member of it (WHATWG HTML, named access on the Document object; Fable review 2
// R2-04). So the shell reads a frame document only through the prototypes of its own realm (G, Q,
// on below), keeps its marks in WeakSets instead of on the document, and reveals only a frame that
// passed finish(). Core's MailSanitizer also drops such names; this is the other guard.
(function () {
  var cur = null, nxt = null, seq = 0, old = null, oldTimer = 0;
  // the frame a cross-fade is leaving: it stays under the new one until the fade ends, and goes at once when anything else happens
  function dropOld() { if (oldTimer) { clearTimeout(oldTimer); oldTimer = 0; } if (old) { old.remove(); old = null; } }
  var DP = Document.prototype, EP = Element.prototype;
  var getter = function (n) { var x = Object.getOwnPropertyDescriptor(DP, n); return x && x.get; };
  var GET = { url: getter('URL'), body: getter('body'), root: getter('documentElement'), view: getter('defaultView'), state: getter('readyState') };
  // a member of document d read through Document.prototype (null when it cannot be read)
  function G(d, which) { try { return GET[which].call(d); } catch (e) { return null; } }
  function Q(d, sel) { return DP.querySelectorAll.call(d, sel); }
  function on(t, type, fn, capture) { EventTarget.prototype.addEventListener.call(t, type, fn, capture); }
  var handled = new WeakSet(), finished = new WeakSet();
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
      // a held z or Delete acts once (Bulwark use-undo-shortcut.ts:17 e.repeat; KeyGate.RepeatRoutes)
      if (e.repeat && (e.key === 'z' || e.key === 'Z' || e.key === 'Delete')) return;
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
  // RD-08 (Bulwark reader-selection-menu.tsx:95-99): a right-click on selected text. The selection goes first (the debounce below is skipped) so the host
  // has it when ctx arrives (sent even when unchanged: a dropped frame leaves no event, so the host's copy may be of an earlier message); the channel is ordered.
  function ctxOf(d) {
    return function (e) {
      try {
        e.preventDefault();
        if (selT) { clearTimeout(selT); selT = 0; }
        pubSel(d, true);
        post('ctx:' + Math.round(e.clientX) + ',' + Math.round(e.clientY));
      } catch (x) { }
    };
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
  // onTextSelectionChange (Bulwark plugin-hook-catalog.ts:328): the selected text, debounced, capped like the host (ShellMessage.MaxSelection)
  var selT = 0, selLast = '';
  function pubSel(d, force) {
    try {
      var v = G(d, 'view'), t = v && v.getSelection ? String(v.getSelection()) : '';
      if (t.length > 4000) t = t.slice(0, 4000);
      if (force || t !== selLast) { selLast = t; post('sel:' + t); }
    } catch (x) { }
  }
  function sel(d) {
    return function () {
      if (selT) clearTimeout(selT);
      selT = setTimeout(function () { selT = 0; pubSel(d); }, 300);
    };
  }
  document.addEventListener('keydown', kd, true);
  document.addEventListener('contextmenu', ctxOf(document), true);
  function fix(d) {
    try {
      var a = Q(d, 'a,area,form');
      for (var i = 0; i < a.length; i++) EP.setAttribute.call(a[i], 'target', '_blank');
    } catch (e) { }
    try {
      if (!handled.has(d)) {
        handled.add(d);
        var t = 0, p = function () { if (t) return; t = 1; requestAnimationFrame(function () { t = 0; post('dirty'); }); };
        on(d, 'keydown', kd, true);
        on(d, 'contextmenu', ctxOf(d), true);
        on(d, 'click', link, true);
        on(d, 'auxclick', link, true);
        on(d, 'scroll', p, true);
        on(d, 'load', p, true);
        on(d, 'selectionchange', p, false);
        on(d, 'selectionchange', sel(d), false);
        on(G(d, 'view'), 'resize', p, false);
      }
    } catch (e) { }
  }
  // Settings > Accessibility text size, minimum size and fit to the pane (Core A11y.Sizing; Bulwark applyMailTextSize).
  function size(d, z) {
    try {
      var b = G(d, 'body'), v = G(d, 'view');
      if (!z || !b || !v) return;
      var f = +z[0] || 1, m = +z[1] || 0, fit = !!z[2];
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
      if (fit) { refit(d); fitLater(d); }
    } catch (e) { }
  }
  // Fit to the pane: the zoom that makes the page as wide as the pane (never wider than 1).
  function refit(d) {
    try {
      var b = G(d, 'body'), de = G(d, 'root');
      if (!b || !de) return;
      b.style.zoom = '';
      var w = de.scrollWidth, c = de.clientWidth;
      if (w > c + 2) b.style.zoom = (c / w).toFixed(4);
    } catch (e) { }
  }
  // R2-13: a picture that loads later widens the page; fit again once each pending one has loaded (or failed).
  function fitLater(d) {
    try {
      var imgs = Q(d, 'img');
      for (var i = 0; i < imgs.length; i++) {
        if (!imgs[i].complete) {
          on(imgs[i], 'load', function () { refit(d); }, false);
          on(imgs[i], 'error', function () { refit(d); }, false);
        }
      }
    } catch (e) { }
  }
  // The dark conversion of a dark-pending page; the pending class goes whatever happens (its CSS hides the body).
  function dark(d) {
    try { if (window.__bbDarkPass) window.__bbDarkPass(d); } catch (e) { }
    try { G(d, 'root').classList.remove('barbican-dark-pending'); } catch (e) { }
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
      // only the frame's own srcdoc is ever finished, and only a finished frame is revealed (a frame that navigated elsewhere never is)
      function finish(d) {
        if (done || seq !== s || !d || G(d, 'url') !== 'about:srcdoc' || !G(d, 'body')) return false;
        done = true;
        fix(d);
        size(d, sz);
        if (dk) dark(d);
        void G(d, 'root').offsetHeight;
        finished.add(f);
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
        if (d && G(d, 'state') !== 'loading' && finish(d)) return;
        setTimeout(poll, 1);
      })();
    },
    reveal: function (s, ms) {
      // a frame the shell never finished is not shown (the host sends its error page instead, BodyShell.GiveUp)
      if (s !== seq || !nxt || !finished.has(nxt)) return;
      var f = nxt;
      nxt = null;
      dropOld();
      document.documentElement.style.background = f._bg;
      // The prototype's body cross-fade (docs/milestones/M1-look-gap.md): the finished page fades in over the one on screen, which stays
      // until the fade ends, so nothing blank ever shows. ms is 0 with reduce motion and while keys repeat: then it swaps at once.
      if (cur && ms > 0) {
        f.style.opacity = '0';
        f.style.transition = 'opacity ' + ms + 'ms cubic-bezier(0.2,0,0,1)';
        f.style.visibility = 'visible';
        void f.offsetHeight;
        f.style.opacity = '1';
        old = cur;
        oldTimer = setTimeout(dropOld, ms + 20);
      } else {
        f.style.visibility = 'visible';
        if (cur) cur.remove();
      }
      cur = f;
    },
    cancel: function (s) { seq = s; if (nxt) { nxt.remove(); nxt = null; } },
    dim: function (on) { if (cur) cur.style.opacity = on ? '0.4' : ''; },
    clear: function (bg) {
      seq++;
      if (nxt) { nxt.remove(); nxt = null; }
      dropOld();
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
        case 'reveal': bb.reveal(m.seq, +m.ms || 0); break;
        case 'cancel': bb.cancel(m.seq); break;
        case 'dim': bb.dim(!!m.on); break;
        case 'clear': bb.clear(m.bg); break;
        case 'print': bb.print(); break;
      }
    });
  } catch (e) { }
  post('shell');
})();
