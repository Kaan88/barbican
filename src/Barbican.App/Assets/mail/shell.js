// The body view's shell (PORT-PLAN.md §3.6; port of the Rust app's mailview.rs SHELL_JS at
// rust-final). One persistent document that never navigates. Each message is a sandboxed srcdoc
// iframe with its own CSP, loaded hidden next to the one on screen; the shell posts "ready:N" once
// the frame is laid out (and, for a page under the dark conversion, once that pass ran), the host
// updates its native header and calls reveal(N) in the same dispatcher turn, and the old frame is
// dropped. Nothing blank, white or black is ever shown in between.
//
// Commands arrive either as functions on window.__bb (ExecuteScriptAsync) or as web messages
// ({op:"show"|"reveal"|"cancel"|"dim"|"clear"|"print", ...}; PostWebMessageAsJson), which carries
// large pages without NavigateToString's 2 MB limit. Messages to the host are plain strings:
//   shell            the shell is up (commands sent before this are queued by the host)
//   ready:N          document N is laid out and may be revealed
//   key:FLAGS|KEY    a shortcut pressed while the page has focus (c=ctrl/meta, s=shift, a=alt)
//   dirty            the visible page changed (scroll, resize, load, selection)
//   ctx:X,Y          a right-click at page coordinates X,Y (the host opens its native menu)
// Links: every link and form of a message is retargeted to a new window before it is shown; the
// host blocks new windows and routes http(s) to the browser and mailto: to the composer.
(function () {
  var cur = null, nxt = null, seq = 0;
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
        d.addEventListener('scroll', p, true);
        d.addEventListener('load', p, true);
        d.addEventListener('selectionchange', p);
        d.defaultView.addEventListener('resize', p);
      }
    } catch (e) { }
  }
  var bb = window.__bb = {
    show: function (s, html, bg, sb) {
      seq = s;
      if (nxt) { nxt.remove(); nxt = null; }
      var f = document.createElement('iframe');
      if (sb !== null) f.setAttribute('sandbox', sb);
      f.setAttribute('title', 'Message');
      f.style.cssText = 'position:absolute;left:0;top:0;width:100%;height:100%;border:0;margin:0;visibility:hidden;background:' + bg;
      f._bg = bg;
      nxt = f;
      var done = false;
      function ready() { if (done || seq !== s) return; done = true; post('ready:' + s); }
      f.addEventListener('load', function () { try { fix(f.contentDocument); } catch (e) { } ready(); });
      f.srcdoc = html;
      document.body.appendChild(f);
      (function poll() {
        if (done || seq !== s) return;
        var d = null;
        try { d = f.contentDocument; } catch (e) { }
        if (d && d.URL === 'about:srcdoc' && d.readyState !== 'loading' && d.body && !d.documentElement.classList.contains('barbican-dark-pending')) {
          fix(d);
          requestAnimationFrame(ready);
          return;
        }
        requestAnimationFrame(poll);
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
        case 'show': bb.show(m.seq, m.html, m.bg, m.sandbox === undefined ? null : m.sandbox); break;
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
