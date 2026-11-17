"""Feel scenarios of the M1 shell against the AUTOMATION build (hidden, BARBICAN_SAMPLE=1, own port): theme_flip, resize, folder_click, key_j20, idle.

Usage: python tools/layout/m1_scenarios.py --port 8841 --layout bento|nordic [--out artifacts/auto/<port>/m1]
Regions come from tools/layout/m1-<layout>.json and replace flicker.py's body-relative ones (the body view is not in the window yet), so the BLANK / FLASH
checks of tools/flicker.py run on the rail, sidebar, list and reader of the C# app.

A hidden off-screen window delivers WGC frames only after a layout change (a resize wakes it); a palette change alone is composed but not captured (M1 shell-ui
Fable log). The theme_flip scenario therefore follows the flip with a one-pixel resize so a frame of the settled state is captured; the in-between state is excluded
structurally (the flip is one UI-thread turn).
"""
import json, os, shutil, socket, sys, time

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import flicker  # noqa: E402


class Driver:
    def __init__(self, port):
        self.s = socket.create_connection(("127.0.0.1", port), timeout=60)
        self.f = self.s.makefile("rw", encoding="utf8")

    def cmd(self, c):
        if c.startswith("sleep "):
            time.sleep(float(c.split()[1]))
            return ""
        self.f.write(c + "\n")
        self.f.flush()
        return self.f.readline().strip()


def install_regions(layout):
    def regions(meta, shape):
        k = meta["scale"] / meta["step"]
        H, W = shape
        out = {}
        for name, (x, y, w, h) in layout["regions"].items():
            x0, y0, x1, y1 = int(max(0, x * k)), int(max(0, y * k)), int(min(W, (x + w) * k)), int(min(H, (y + h) * k))
            if x1 > x0 + 1 and y1 > y0 + 1:
                out[name] = (y0, y1, x0, x1)
        return out, {}

    flicker.regions = regions


def burst(d, out, ms, actions, step=2):
    shutil.rmtree(out, ignore_errors=True)
    d.cmd(f"burst {ms} 10 {out} {step}")
    time.sleep(0.3)
    for a in actions:
        d.cmd(a)
    while not d.cmd("burst-status").startswith(("done", "none")):
        time.sleep(0.2)
    return flicker.analyse(out)


def main():
    a = sys.argv[1:]
    opt = lambda k, dflt=None: a[a.index(k) + 1] if k in a else dflt  # noqa: E731
    port = int(opt("--port", "8841"))
    name = opt("--layout", "bento")
    layout = json.load(open(os.path.join(os.path.dirname(__file__), f"m1-{name}.json")))
    out = os.path.abspath(opt("--out", os.path.join("artifacts", "auto", str(port), "m1")))
    install_regions(layout)
    d = Driver(port)
    w, h = layout["window"]
    d.cmd(f"resize {w} {h}")
    d.cmd(layout["ui"])
    d.cmd(f"resize {w + 1} {h}")
    d.cmd(f"resize {w} {h}")
    time.sleep(1.0)
    ids = layout["ids"]
    results = {}
    results["theme_flip"] = burst(d, os.path.join(out, "theme_flip"), 2500, [layout["ui"].replace("dark", "light"), "sleep 0.4", f"resize {w + 1} {h}", f"resize {w} {h}"])
    d.cmd(layout["ui"])
    d.cmd(f"resize {w + 1} {h}")
    d.cmd(f"resize {w} {h}")
    time.sleep(1.0)
    results["resize"] = burst(d, os.path.join(out, "resize"), 2500, [f"resize {w - 150} {h - 80}", "sleep 0.5", f"resize {w} {h}"])
    results["folder_click"] = burst(d, os.path.join(out, "folder_click"), 2500, [f"uia-select {ids['folder']}", "sleep 0.6", "uia-select m1"])
    results["key_j20"] = burst(d, os.path.join(out, "key_j20"), 4000, ["key j", "sleep 0.1"] * 20)
    json.dump(results, open(os.path.join(out, "results.json"), "w"), indent=1)
    for k, v in results.items():
        print(k, {x: v.get(x) for x in ("frames", "bad", "blank", "flash", "max_bad_run_ms") if x in v} or v)
    d.cmd("probe-start wgc")
    time.sleep(2)
    probe = json.loads(d.cmd("probe-stop"))
    print("idle frames in 2 s:", len(probe["frames"]))


if __name__ == "__main__":
    main()
