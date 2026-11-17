"""Analyse a frame burst recorded by the automation `burst` command (see src/automation.rs).

Usage:
  python tools/flicker.py DIR [--frames] [--json] [--bento]   (--bento: the Bento/Nordic layout)

DIR holds f####_<ms>.png frames (whole window incl. the WebView2 email body), events.txt
(commands and app marks with their time) and meta.json (scale, downscale step, body rect).

Regions (logical px, from meta.json's body rect bx,by,bw,bh):
  head  the reader's subject line, from  its sender line
  body  the email body rect, split into a 4x3 grid of tiles
  list  the message list column left of the reader
  pane  the whole reader column (for time-to-content)

Per frame it reports each region's mean luminance (0-255) and flags:
  FLASH  a tile/region jumps to a brightness that is neither the start nor the end state
         (outside both by > 40), e.g. a white page between two dark ones, or near-black
         between two light ones
  BLANK  a tile/region that has content (ink: pixels standing out from its background) at
         the start and the end loses most of it (< 25 %) in between: the body went away
  HEADER the same for the header: subject/sender text missing
Summary: frames, bad frames (any flag), blank/flash frame counts, the longest bad run (ms),
the number of visible state changes in the reader pane, time from the first command to the
first visible change (response) and to the settled final picture (content).
"""
import json, os, re, sys
import numpy as np
from PIL import Image


def load(d):
    meta = json.load(open(os.path.join(d, "meta.json")))
    frames = []
    for n in sorted(os.listdir(d)):
        m = re.match(r"f(\d+)_([\d.]+)\.png$", n)
        if m:
            frames.append((float(m.group(2)), os.path.join(d, n)))
    events = []
    p = os.path.join(d, "events.txt")
    if os.path.exists(p):
        for line in open(p, encoding="utf8"):
            if "\t" in line:
                t, e = line.rstrip("\n").split("\t", 1)
                events.append((float(t), e))
    return meta, frames, events


def lum(path):
    a = np.asarray(Image.open(path).convert("RGB"), dtype=np.float32)
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def regions(meta, shape):
    k = meta["scale"] / meta["step"]
    bx, by, bw, bh = meta["body"]
    H, W = shape

    def r(x, y, w, h):
        x0, y0 = int(max(0, x * k)), int(max(0, y * k))
        x1, y1 = int(min(W, (x + w) * k)), int(min(H, (y + h) * k))
        return (y0, y1, x0, x1) if x1 > x0 + 1 and y1 > y0 + 1 else None

    if meta.get("layout") == "bento":
        # Bento/Nordic design: subject and sender line above the conversation strip, the list
        # in its own tile left of the reader tile
        regs = {"head": r(bx, 185, bw, 42), "from": r(bx, 236, bw * 0.8, 22), "list": r(bx - 397, 230, 350, H / k - 290), "pane": r(bx - 25, 113, bw + 50, H / k - 128)}
    else:
        regs = {"head": r(bx + 8, 50, bw - 70, 40), "from": r(bx + 52, 100, bw * 0.55, 26), "list": r(bx - 330, 45, 320, H / k - 90), "pane": r(bx, 0, bw, H / k)}
    tiles = {}
    for i in range(4):
        for j in range(3):
            t = r(bx + bw * i / 4, by + bh * j / 3, bw / 4, bh / 3)
            if t:
                tiles[f"t{i}{j}"] = t
    return {k: v for k, v in regs.items() if v}, tiles


def crop(a, r):
    y0, y1, x0, x1 = r
    return a[y0:y1, x0:x1]


def changed(a, b):
    """A visible change: more than 0.02 % of the pixels moved by over 20 levels."""
    return (np.abs(a - b) > 20).mean() > 0.0002


def ink(c):
    """Share of pixels that stand out from the region's background (text, images, lines)."""
    return float((np.abs(c - np.median(c)) > 25).mean())


def analyse(d, show_frames=False, layout=None):
    meta, frames, events = load(d)
    if layout:
        meta["layout"] = layout
    if not frames:
        return {"error": "no frames"}
    L = [lum(p) for _, p in frames]
    # a resize changes the frame size: compare the part every frame has
    H, W = min(a.shape[0] for a in L), min(a.shape[1] for a in L)
    L = [a[:H, :W] for a in L]
    times = [t for t, _ in frames]
    regs, tiles = regions(meta, L[0].shape)
    allr = dict(regs, **tiles)
    first, last = L[0], L[-1]
    s0 = {k: (crop(first, r).mean(), ink(crop(first, r))) for k, r in allr.items()}
    s1 = {k: (crop(last, r).mean(), ink(crop(last, r))) for k, r in allr.items()}
    t_cmd = next((t for t, e in events if e.startswith("cmd ")), times[0])

    # A capture with no ink anywhere (an all-white or all-black window) is not a picture of the app: WGC of a hidden
    # window returns such frames while the display is off (M1 verify). BLANK/FLASH compare against the first and last
    # frame, so a burst of uniform frames would pass as 0/0; count them so callers can reject the burst.
    empty_n = sum(1 for a in L if ink(a[::4, ::4]) < 0.001)
    rows, bad, blank_n, flash_n, head_n = [], [], 0, 0, 0
    pane = regs.get("pane")
    changes, prev, response, settled = 0, None, None, None
    lst, lprev, list_changes = regs.get("list"), None, 0
    for idx, (t, a) in enumerate(zip(times, L)):
        flags = []
        for k, r in allr.items():
            c = crop(a, r)
            m, s = c.mean(), ink(c)
            (m0, sd0), (m1, sd1) = s0[k], s1[k]
            if k in ("pane",):
                continue
            lo, hi = min(m0, m1) - 40, max(m0, m1) + 40
            if (m < lo or m > hi) and (m > 225 or m < 30 or abs(m - m0) > 60 and abs(m - m1) > 60):
                flags.append(f"FLASH:{k}")
            hdr = k in ("head", "from")
            if min(sd0, sd1) > 0.01 and s < (0.4 if hdr else 0.25) * min(sd0, sd1):
                flags.append("HEADER" if hdr else f"BLANK:{k}")
        if pane:
            pc = crop(a, pane)
            if prev is not None and changed(pc, prev):
                changes += 1
                if response is None and t >= t_cmd:
                    response = t - t_cmd
            prev = pc
        if lst:
            lc = crop(a, lst)
            if lprev is not None and changed(lc, lprev):
                list_changes += 1
            lprev = lc
        if any(f.startswith("FLASH") for f in flags):
            flash_n += 1
        if any(f.startswith("BLANK") for f in flags):
            blank_n += 1
        if "HEADER" in flags:
            head_n += 1
        if flags:
            bad.append(t)
        rows.append((t, {k: round(float(crop(a, r).mean()), 1) for k, r in regs.items()}, flags))
    # settled: first frame after the command from which the pane shows no visible change from the last frame
    if pane:
        lastp = crop(last, pane)
        for t, a in zip(reversed(times), reversed(L)):
            if changed(crop(a, pane), lastp):
                break
            settled = t
        if settled is not None:
            settled = max(0.0, settled - t_cmd)
    # longest run of consecutive bad frames, as time
    run, best, start = 0.0, 0.0, None
    bset = set(bad)
    for i, t in enumerate(times):
        if t in bset:
            start = t if start is None else start
            nxt = times[i + 1] if i + 1 < len(times) else t
            best = max(best, nxt - start)
        else:
            start = None
    if show_frames:
        for t, m, f in rows:
            print(f"{t:8.1f}  " + "  ".join(f"{k}={v:5.1f}" for k, v in m.items()) + ("  " + " ".join(sorted(set(f))) if f else ""))
        for t, e in events:
            print(f"{t:8.1f}  event {e}")
    return {
        "frames": len(frames),
        "bad": len(bad),
        "blank": blank_n,
        "flash": flash_n,
        "header_missing": head_n,
        "empty": empty_n,
        "capture_valid": empty_n == 0,
        "worst_ms": round(best, 1),
        "changes": changes,
        "list_changes": list_changes,
        "response_ms": None if response is None else round(response, 1),
        "content_ms": None if settled is None else round(settled, 1),
    }


if __name__ == "__main__":
    a = [x for x in sys.argv[1:] if not x.startswith("--")]
    res = analyse(a[0], "--frames" in sys.argv, "bento" if "--bento" in sys.argv else None)
    print(json.dumps(res) if "--json" in sys.argv else " ".join(f"{k}={v}" for k, v in res.items()))
