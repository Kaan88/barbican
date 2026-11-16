// Dark mode for HTML mail in the reader's WebView: a port of Bulwark lib/email-dark-mode.ts
// (applyEmailDarkMode). The rendered document is read back through getComputedStyle and its
// colours are rewritten in place: light backgrounds are darkened with their hue kept (vivid and
// already dark ones stay as sent), text and borders only change when they lack contrast against
// the new background, images keep their pixels (a transparent one that would vanish gets its
// original light background as a plate), and whatever is drawn on a background image keeps the
// sender's colours. The page starts with <html class="barbican-dark-pending"> (body hidden, with
// a 1.5 s failsafe) so the light original never flashes. Runs once on DOMContentLoaded.
(function () {
  var CANVAS = { r: 10, g: 10, b: 10, a: 1 };
  var PENDING = 'barbican-dark-pending';
  var WHITE = { r: 255, g: 255, b: 255, a: 1 };
  var SIDES = ['top', 'right', 'bottom', 'left'];
  var SKIP = { IMG: 1, VIDEO: 1, CANVAS: 1, PICTURE: 1, SOURCE: 1, svg: 1, SVG: 1, STYLE: 1, SCRIPT: 1, BR: 1, WBR: 1, HEAD: 1, TITLE: 1, META: 1, LINK: 1 };
  var BG_WHITE_L = 0.2, BG_DARK_L = 0.45, VIVID_CHROMA = 0.1;

  function parse(v) {
    var m = /^rgba?\(\s*([\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)(?:\s*[,/]\s*([\d.]+%?))?\s*\)$/i.exec(v || '');
    if (!m) return null;
    var a = 1;
    if (m[4] !== undefined) a = m[4].slice(-1) === '%' ? parseFloat(m[4]) / 100 : parseFloat(m[4]);
    return { r: +m[1], g: +m[2], b: +m[3], a: a };
  }
  function fmt(c) {
    var R = Math.round(c.r), G = Math.round(c.g), B = Math.round(c.b);
    return c.a >= 1 ? 'rgb(' + R + ', ' + G + ', ' + B + ')' : 'rgba(' + R + ', ' + G + ', ' + B + ', ' + (+c.a.toFixed(3)) + ')';
  }
  function toLin(v) { var c = v / 255; return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); }
  function fromLin(v) { var c = v <= 0.0031308 ? v * 12.92 : 1.055 * Math.pow(v, 1 / 2.4) - 0.055; return c * 255; }
  function toLch(c) {
    var lr = toLin(c.r), lg = toLin(c.g), lb = toLin(c.b);
    var l = Math.cbrt(0.4122214708 * lr + 0.5363325363 * lg + 0.0514459929 * lb);
    var m = Math.cbrt(0.2119034982 * lr + 0.6806995451 * lg + 0.1073969566 * lb);
    var s = Math.cbrt(0.0883024619 * lr + 0.2817188376 * lg + 0.6299787005 * lb);
    var L = 0.2104542553 * l + 0.793617785 * m - 0.0040720468 * s;
    var A = 1.9779984951 * l - 2.428592205 * m + 0.4505937099 * s;
    var B = 0.0259040371 * l + 0.7827717662 * m - 0.808675766 * s;
    return { l: L, c: Math.hypot(A, B), h: Math.atan2(B, A) };
  }
  function lchToLin(x) {
    var A = x.c * Math.cos(x.h), B = x.c * Math.sin(x.h);
    var l = Math.pow(x.l + 0.3963377774 * A + 0.2158037573 * B, 3);
    var m = Math.pow(x.l - 0.1055613458 * A - 0.0638541728 * B, 3);
    var s = Math.pow(x.l - 0.0894841775 * A - 1.291485548 * B, 3);
    return [4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
      -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
      -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s];
  }
  function out(v) { return v < -1e-4 || v > 1 + 1e-4; }
  function fromLch(x, a) {
    var c = x.c, lin = lchToLin({ l: x.l, c: c, h: x.h });
    for (var i = 0; i < 24 && (out(lin[0]) || out(lin[1]) || out(lin[2])); i++) { c *= 0.9; lin = lchToLin({ l: x.l, c: c, h: x.h }); }
    var v = lin.map(function (q) { return Math.min(255, Math.max(0, fromLin(Math.min(1, Math.max(0, q))))); });
    return { r: v[0], g: v[1], b: v[2], a: a };
  }
  function lum(c) { return 0.2126 * toLin(c.r) + 0.7152 * toLin(c.g) + 0.0722 * toLin(c.b); }
  function contrast(x, y) { var a = lum(x), b = lum(y); return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05); }
  function same(x, y) { return Math.round(x.r) === Math.round(y.r) && Math.round(x.g) === Math.round(y.g) && Math.round(x.b) === Math.round(y.b) && x.a === y.a; }
  function composite(t, u) { var a = t.a; return { r: t.r * a + u.r * (1 - a), g: t.g * a + u.g * (1 - a), b: t.b * a + u.b * (1 - a), a: 1 }; }

  function darken(c) {
    if (c.a === 0) return c;
    var x = toLch(c);
    if (x.l <= BG_DARK_L || x.c >= VIVID_CHROMA) return c;
    var l = x.l >= 0.7 ? BG_WHITE_L + (1 - x.l) * 0.6 : BG_DARK_L - ((x.l - BG_DARK_L) / (0.7 - BG_DARK_L)) * (BG_DARK_L - 0.38);
    return fromLch({ l: l, c: x.c, h: x.h }, c.a);
  }
  function ensure(c, bg, min) {
    if (c.a === 0) return c;
    var op = c.a < 1 ? composite(c, bg) : c;
    if (contrast(op, bg) >= min) return c;
    var x = toLch(op), lighten = toLch(bg).l < 0.6;
    var l = lighten ? Math.max(x.l, 0.96 - x.l * 0.5) : Math.min(x.l, 0.3);
    var o = fromLch({ l: l, c: x.c, h: x.h }, 1);
    for (var i = 0; i < 40 && contrast(o, bg) < min; i++) {
      l = lighten ? Math.min(1, l + 0.02) : Math.max(0, l - 0.02);
      o = fromLch({ l: l, c: x.c, h: x.h }, 1);
      if (l === 0 || l === 1) break;
    }
    return o;
  }
  function mapGradient(v, f) { return v.replace(/rgba?\([^)]*\)/gi, function (m) { var c = parse(m); return c ? fmt(f(c)) : m; }); }
  function hasImage(v) { return !!v && v !== 'none' && /url\(/i.test(v.replace(/url\(\s*(?:""|''|)\s*\)/gi, '')); }
  function keepSeparation(next, orig, parent) {
    if (next !== orig || orig.a < 0.9) return next;
    if (contrast(orig, parent.original) < 3) return next;
    if (contrast(next, parent.bg) >= 1.8) return next;
    var x = toLch(next), l = Math.max(x.l, toLch(parent.bg).l), o = next;
    for (var i = 0; i < 25 && contrast(o, parent.bg) < 1.8; i++) { l += 0.02; o = fromLch({ l: l, c: x.c, h: x.h }, next.a); }
    return o;
  }
  function judge(img, doc, bg) {
    var w = img.naturalWidth, h = img.naturalHeight;
    if (!w || !h) return 'fine';
    var sc = Math.min(1, 48 / Math.max(w, h)), cw = Math.max(1, Math.round(w * sc)), ch = Math.max(1, Math.round(h * sc));
    var cv = doc.createElement('canvas'); cv.width = cw; cv.height = ch;
    var ctx = cv.getContext('2d', { willReadFrequently: true });
    if (!ctx) return 'unknown';
    var d;
    try { ctx.drawImage(img, 0, 0, cw, ch); d = ctx.getImageData(0, 0, cw, ch).data; } catch (e) { return 'unknown'; }
    var clear = 0, drawn = 0, hidden = 0;
    for (var i = 0; i < d.length; i += 4) {
      var a = d[i + 3] / 255;
      if (a < 0.5) { clear++; continue; }
      drawn++;
      if (contrast(composite({ r: d[i], g: d[i + 1], b: d[i + 2], a: a }, bg), bg) < 2) hidden++;
    }
    var total = d.length / 4;
    if (clear / total < 0.02 || drawn === 0) return 'fine';
    return hidden / drawn > 0.35 ? 'plate' : 'fine';
  }
  function plate(img, surface) {
    var go = function () {
      if (!(img.naturalWidth > 0) || (img.naturalWidth <= 2 && img.naturalHeight <= 2)) return;
      // pixels that cannot be read (a remote image without CORS) get the plate as well
      if (judge(img, img.ownerDocument, surface.bg) === 'fine' || !img.isConnected) return;
      img.style.setProperty('background-color', fmt(surface.original), 'important');
    };
    if (img.complete) go(); else img.addEventListener('load', go, { once: true });
  }

  function apply(doc) {
    var win = doc.defaultView, body = doc.body, html = doc.documentElement;
    try {
      if (!win || !body) return;
      html.classList.remove(PENDING);
      var canvas = parse(win.getComputedStyle(html).backgroundColor);
      var mailCanvas = canvas && canvas.a === 1 && !same(canvas, CANVAS) ? canvas : null;
      var base = mailCanvas ? darken(mailCanvas) : CANVAS;
      var els = [body];
      body.querySelectorAll('*').forEach(function (el) { if (!SKIP[el.tagName] && !el.closest('svg')) els.push(el); });
      var snaps = els.map(function (el) {
        var cs = win.getComputedStyle(el);
        return {
          el: el, color: parse(cs.color), bg: parse(cs.backgroundColor), bgImage: cs.backgroundImage,
          borders: SIDES.map(function (s) {
            return cs.getPropertyValue('border-' + s + '-style') !== 'none' && parseFloat(cs.getPropertyValue('border-' + s + '-width')) > 0
              ? parse(cs.getPropertyValue('border-' + s + '-color')) : null;
          })
        };
      });
      var root = { bg: base, original: mailCanvas || WHITE, onImage: false };
      var surfaces = new Map();
      var surfaceOf = function (el) { for (var n = el; n; n = n.parentElement) { var s = surfaces.get(n); if (s) return s; } return root; };
      var writes = [];
      if (mailCanvas && base !== mailCanvas) writes.push(function () { html.style.setProperty('background-color', fmt(base), 'important'); });
      snaps.forEach(function (snap) {
        var el = snap.el, parent = el === body ? root : surfaceOf(el.parentElement), surface = parent;
        if (hasImage(snap.bgImage)) {
          var fill = snap.bg && snap.bg.a > 0 ? composite(snap.bg, parent.bg) : parent.bg;
          surface = { bg: fill, original: fill, onImage: true };
        } else {
          if (snap.bgImage && snap.bgImage !== 'none' && !parent.onImage) {
            var g = mapGradient(snap.bgImage, darken);
            if (g !== snap.bgImage) writes.push(function () { el.style.setProperty('background-image', g, 'important'); });
          }
          if (el === body && snap.bg && same(snap.bg, CANVAS)) {
            // our own canvas, the white page of the light view
          } else if (snap.bg && snap.bg.a > 0 && !(parent.onImage && snap.bg.a < 0.9)) {
            var orig = snap.bg;
            var next = parent.onImage || el === body ? darken(orig) : keepSeparation(darken(orig), orig, parent);
            surface = { bg: composite(next, parent.bg), original: composite(orig, parent.original), onImage: false };
            if (next !== orig) writes.push(function () { el.style.setProperty('background-color', fmt(next), 'important'); });
          }
        }
        surfaces.set(el, surface);
        if (surface.onImage) return;
        if (snap.color) {
          var nc = ensure(snap.color, surface.bg, 4.5);
          if (nc !== snap.color) writes.push(function () { el.style.setProperty('color', fmt(nc), 'important'); });
        }
        snap.borders.forEach(function (b, i) {
          if (!b || b.a === 0) return;
          var d = darken(b), nb = d !== b ? d : ensure(b, parent.bg, 1.6);
          if (nb !== b) writes.push(function () { el.style.setProperty('border-' + SIDES[i] + '-color', fmt(nb), 'important'); });
        });
      });
      var plates = [];
      body.querySelectorAll('img').forEach(function (img) {
        var s = surfaceOf(img.parentElement);
        if (s.onImage || s.bg === s.original) return;
        if (toLch(s.original).l < 0.7 || contrast(s.original, s.bg) < 3) return;
        plates.push([img, s]);
      });
      writes.forEach(function (w) { w(); });
      plates.forEach(function (p) { plate(p[0], p[1]); });
      html.setAttribute('data-dark-done', String(writes.length));
    } finally {
      html.classList.remove(PENDING);
    }
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { apply(document); });
  else apply(document);
})();
