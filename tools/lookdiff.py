"""Look comparison of the M1 app against the WinUI prototype, and the drift check that keeps it from drifting silently.

Both apps run hidden (off-screen, never activated, sample data only) and are rendered with RenderTargetBitmap (XAML only: the
WebView2 body is not in the picture on either side). The states are the same on both sides, in Bento and Nordic, dark and light:
  inbox     the folder as it opens, first row selected, reader open
  selected  the third row open
  hover     the second row in its PointerOver state (`vstate`: no pointer is moved)
  switch    the fourth row selected, rendered as fast as RenderTargetBitmap allows (4 pictures)
  narrow    the window at 1100x760

Usage (Windows):
  python tools/lookdiff.py proto  [--out artifacts/look/proto]          runs prototype/capture (build it first:
                                                                         tools/build.ps1 build -Project prototype/capture/ProtoCapture.csproj)
  python tools/lookdiff.py m1     --port 8871 [--out artifacts/look/m1]  drives a running AUTOMATION build started with BARBICAN_SAMPLE=1:
                                                                         $env:BARBICAN_SAMPLE='1'; tools/auto-run.ps1 start -Port 8871
  python tools/lookdiff.py pair   --proto DIR --m1 DIR --out DIR         side-by-side pictures (prototype left, M1 right) per state
  python tools/lookdiff.py check  --m1 DIR [--ref docs/milestones/M1-look/ref] [--tolerance 0.02]
                                                                         the drift check: every reference picture against the new one;
                                                                         exit 1 when more than TOLERANCE of the pixels differ by more than 24/255
  python tools/lookdiff.py accept --m1 DIR [--ref ...]                   copies the inbox/selected/hover pictures of DIR into the reference set

The layout boxes (layout-*.json: rail, sidebar, list, reader, rows, ...) are written by both capture modes, so spacing is compared
in numbers, not by eye (docs/milestones/M1-look-gap.md).
"""
import json, os, shutil, subprocess, sys, time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

LOOKS = [(s, t) for s in ("bento", "nordic") for t in ("dark", "light")]
STATES = ("inbox", "selected", "hover", "narrow")
REF = os.path.join(ROOT, "docs", "milestones", "M1-look", "ref")
BOX_IDS = {
    "rail": "rail", "sidebar": "sidebar", "compose": "sidebar.compose", "list": "list", "list.title": "list.title",
    "reader": "reader", "reader.subject": "reader.subject", "reader.body": "reader.body", "context": "context",
    "railbtn0": "rail.mail", "railbtn1": "rail.calendar", "railbtn2": "rail.contacts",
}


def opt(a, k, dflt=None):
    return a[a.index(k) + 1] if k in a else dflt


def proto(a):
    out = os.path.abspath(opt(a, "--out", os.path.join(ROOT, "artifacts", "look", "proto")))
    os.makedirs(out, exist_ok=True)
    exe = os.path.join(ROOT, "prototype", "capture", "bin", "x64", "Debug", "net10.0-windows10.0.19041.0", "win-x64", "ProtoCapture.exe")
    if not os.path.exists(exe):
        raise SystemExit("build it first: tools/build.ps1 build -Project prototype/capture/ProtoCapture.csproj")
    si = subprocess.STARTUPINFO()
    si.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    si.wShowWindow = 0  # SW_HIDE: even a first ShowWindow cannot activate it
    r = subprocess.run([exe, out], startupinfo=si, timeout=300)
    print(open(os.path.join(out, "proto-capture.log"), encoding="utf8").read())
    return r.returncode


class Driver:
    def __init__(self, port):
        import autoconn
        self.s, self.f = autoconn.connect(port, timeout=60)

    def cmd(self, c):
        self.f.write(c + "\n")
        self.f.flush()
        return self.f.readline().strip()


def m1(a):
    port = int(opt(a, "--port", "8871"))
    out = os.path.abspath(opt(a, "--out", os.path.join(ROOT, "artifacts", "look", "m1")))
    os.makedirs(out, exist_ok=True)
    d = Driver(port)
    tree = json.loads(d.cmd("tree"))
    ids = [n["id"] for n in tree]
    start = ids.index("list.rows") + 1
    rows = [i for i in ids[start:] if i.startswith("e")][:6]
    if len(rows) < 4:
        raise SystemExit(f"need 4 message rows, got {rows}")
    log = []
    for style, theme in LOOKS:
        tag = f"{style}-{theme}"
        d.cmd(f"ui {style} {theme} normal")
        d.cmd("resize 1560 960")
        time.sleep(0.6)
        d.cmd(f"uia-select {rows[0]}")
        time.sleep(0.8)
        log.append(f"m1-{tag}-inbox.png " + d.cmd(f"shot {os.path.join(out, f'm1-{tag}-inbox.png')}"))
        boxes = {}
        for name, aid in BOX_IDS.items():
            r = d.cmd(f"bounds {aid}")
            if not r.startswith("error"):
                boxes[name] = [float(x) for x in r.split()]
        for i, rid in enumerate(rows[:4]):
            r = d.cmd(f"bounds {rid}")
            if not r.startswith("error"):
                boxes[f"row{i}"] = [float(x) for x in r.split()]
        with open(os.path.join(out, f"layout-m1-{tag}.json"), "w", encoding="utf8") as f:
            json.dump(boxes, f, indent=1)
        d.cmd(f"uia-select {rows[2]}")
        time.sleep(0.6)
        log.append(f"m1-{tag}-selected.png " + d.cmd(f"shot {os.path.join(out, f'm1-{tag}-selected.png')}"))
        log.append("vstate " + d.cmd(f"vstate {rows[1]} PointerOver"))
        time.sleep(0.2)
        log.append(f"m1-{tag}-hover.png " + d.cmd(f"shot {os.path.join(out, f'm1-{tag}-hover.png')}"))
        d.cmd(f"vstate {rows[1]} Normal")
        t0 = time.perf_counter()
        d.cmd(f"uia-select {rows[3]}")
        for i in range(4):
            ms = int((time.perf_counter() - t0) * 1000)
            log.append(f"m1-{tag}-switch-{i}-{ms}ms.png " + d.cmd(f"shot {os.path.join(out, f'm1-{tag}-switch-{i}-{ms}ms.png')}"))
        d.cmd("resize 1100 760")
        time.sleep(0.6)
        log.append(f"m1-{tag}-narrow.png " + d.cmd(f"shot {os.path.join(out, f'm1-{tag}-narrow.png')}"))
        d.cmd("resize 1560 960")
        time.sleep(0.4)
    with open(os.path.join(out, "m1-capture.log"), "w", encoding="utf8") as f:
        f.write("\n".join(log) + "\n")
    print("\n".join(log))
    return 0


def pair(a):
    from PIL import Image, ImageDraw
    pdir, mdir = opt(a, "--proto"), opt(a, "--m1")
    out = os.path.abspath(opt(a, "--out", os.path.join(ROOT, "artifacts", "look", "pair")))
    os.makedirs(out, exist_ok=True)
    for style, theme in LOOKS:
        for state in STATES:
            p = os.path.join(pdir, f"proto-{style}-{theme}-{state}.png")
            m = os.path.join(mdir, f"m1-{style}-{theme}-{state}.png")
            if not (os.path.exists(p) and os.path.exists(m)):
                continue
            a_, b_ = Image.open(p).convert("RGB"), Image.open(m).convert("RGB")
            h = max(a_.height, b_.height)
            img = Image.new("RGB", (a_.width + b_.width + 12, h + 24), (128, 128, 128))
            img.paste(a_, (0, 24))
            img.paste(b_, (a_.width + 12, 24))
            dr = ImageDraw.Draw(img)
            dr.text((8, 6), f"prototype  {style} {theme} {state}", fill=(0, 0, 0))
            dr.text((a_.width + 20, 6), f"M1  {style} {theme} {state}", fill=(0, 0, 0))
            img = img.resize((img.width // 2, img.height // 2), Image.LANCZOS)
            img.save(os.path.join(out, f"pair-{style}-{theme}-{state}.png"), optimize=True)
    print("pairs in", out)
    return 0


def _diff(ref, new):
    import numpy as np
    from PIL import Image
    a_ = np.asarray(Image.open(ref).convert("RGB"), dtype=np.int16)
    b_ = np.asarray(Image.open(new).convert("RGB"), dtype=np.int16)
    if a_.shape != b_.shape:
        return 1.0, f"size {a_.shape[1]}x{a_.shape[0]} -> {b_.shape[1]}x{b_.shape[0]}"
    if b_.reshape(-1, 3).std(axis=0).max() < 1:
        return 1.0, "uniform picture (capture invalid)"
    changed = (np.abs(a_ - b_).max(axis=2) > 24).mean()
    return float(changed), f"{changed * 100:.2f}% of pixels changed"


def check(a):
    mdir = opt(a, "--m1")
    ref = opt(a, "--ref", REF)
    tol = float(opt(a, "--tolerance", "0.02"))
    bad = 0
    names = sorted(n for n in os.listdir(ref) if n.endswith(".png"))
    if not names:
        raise SystemExit(f"no reference pictures in {ref}")
    for n in names:
        new = os.path.join(mdir, n)
        if not os.path.exists(new):
            print(f"MISSING {n}")
            bad += 1
            continue
        frac, msg = _diff(os.path.join(ref, n), new)
        status = "ok" if frac <= tol else "DRIFT"
        bad += status != "ok"
        print(f"{status:5} {n}: {msg}")
    print(f"{len(names) - bad}/{len(names)} within {tol * 100:.1f}%")
    return 1 if bad else 0


def accept(a):
    mdir = opt(a, "--m1")
    ref = opt(a, "--ref", REF)
    os.makedirs(ref, exist_ok=True)
    for style, theme in LOOKS:
        for state in ("inbox", "selected", "hover"):
            n = f"m1-{style}-{theme}-{state}.png"
            shutil.copy(os.path.join(mdir, n), os.path.join(ref, n))
    print("reference updated:", ref)
    return 0


def main():
    a = sys.argv[1:]
    if not a:
        print(__doc__)
        return 2
    return {"proto": proto, "m1": m1, "pair": pair, "check": check, "accept": accept}[a[0]](a[1:])


if __name__ == "__main__":
    sys.exit(main())
