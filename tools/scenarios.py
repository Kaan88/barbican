"""Run the flicker scenarios against a Barbican started with BARBICAN_AUTOMATION=1 and print
(and save) the tools/flicker.py summary of each.

Usage: python tools/scenarios.py --port N --out RESULTS.json --dir FRAMES_DIR [--only a,b] [--modes light,dark] [--search TEXT]

Each scenario: setup commands, then a frame burst (whole window incl. the WebView) while the
action commands run. Coordinates are logical px for the default 1781x900 window with the
assistant panel open (as the app starts); `resize` puts it back first.
The account must be the writable test account (actions star/unstar the open message only).
"""
import json, os, socket, sys, time

sys.path.insert(0, os.path.dirname(__file__))
import flicker  # noqa: E402

ROW = lambda i: f"click 450 {118 + 82 * i}"  # noqa: E731
BASE = ["theme {mode}", "resize 1781 900", "render-mode 0", "discard-composer", "click 22 87", ROW(0), "sleep 0.3", "key Escape", "sleep 0.5"]
READ = BASE + [ROW(0), "sleep 1.6"]

# name: (setup, action, burst ms). Open/close transitions are separate bursts so that each
# burst's start and end pictures are the two states the transition is between.
SCENARIOS = {
    "open_first":     (BASE + ["cache-clear", "sleep 0.3"], [ROW(0)], 1800),
    "open_again":     (BASE + ["sleep 0.3"], [ROW(0)], 1500),
    "switch_key":     (READ, ["key j"], 1800),
    "switch_click":   (READ, [ROW(2)], 1800),
    "switch_back":    (BASE + [ROW(1), "sleep 1.4", ROW(0), "sleep 1.6"], [ROW(1)], 1500),
    "close_reader":   (READ, ["key Escape"], 1200),
    "thread_switch":  (BASE + [ROW(0), "sleep 2.2"], ["click 700 180"], 1800),
    "ctx_menu":       (READ, ["rclick 720 111", "sleep 0.6", "key Escape"], 1500),
    "composer_open":  (READ, ["click 645 14"], 1500),
    "composer_close": (READ + ["click 645 14", "sleep 1.2"], ["discard-composer"], 1500),
    "settings_open":  (READ, ["click 22 837"], 1500),
    "settings_close": (READ + ["click 22 837", "sleep 1.2"], ["key Escape"], 1500),
    "app_to_cal":     (READ, ["click 22 129"], 1500),
    "app_back":       (READ + ["click 22 129", "sleep 1.2"], ["click 22 87"], 1500),
    "theme_flip":     (READ, ["theme {other}"], 1500),
    "resize":         (READ, ["resize 1500 820"], 1500),
    "star_toggle":    (READ, ["type s", "sleep 0.9", "type s"], 2200),
    "list_refresh":   (READ, ["type G"], 2000),
    "images_toggle":  (READ, ["load-images"], 1500),
    "light_switch":   (BASE + ["render-mode 1", ROW(0), "sleep 1.6"], ["key j"], 1800),
}


class Driver:
    def __init__(self, port):
        self.s = socket.create_connection(("127.0.0.1", port), timeout=30)
        self.f = self.s.makefile("rw", encoding="utf8")

    def cmd(self, c):
        if c.startswith("sleep "):
            time.sleep(float(c.split()[1]))
            return ""
        self.f.write(c + "\n")
        self.f.flush()
        return self.f.readline().strip()


def run(d, name, mode, frames_dir):
    setup, action, ms = SCENARIOS[name]
    other = "light" if mode == "dark" else "dark"
    fill = lambda c: c.format(mode=mode, other=other)  # noqa: E731
    for c in setup:
        d.cmd(fill(c))
    out = os.path.join(frames_dir, f"{mode}_{name}")
    d.cmd(f"burst {ms} 10 {out} 2")
    time.sleep(0.25)
    for c in action:
        d.cmd(fill(c))
    while d.cmd("burst-status").startswith("running"):
        time.sleep(0.2)
    while not d.cmd("burst-status").startswith(("done", "none")):
        time.sleep(0.2)
    return flicker.analyse(out)


def main():
    a = sys.argv[1:]
    opt = lambda k, dflt=None: a[a.index(k) + 1] if k in a else dflt  # noqa: E731
    port = int(opt("--port", "8951"))
    out = opt("--out", "flicker.json")
    frames_dir = opt("--dir", "frames")
    only = opt("--only")
    modes = opt("--modes", "light,dark").split(",")
    names = only.split(",") if only else list(SCENARIOS)
    d = Driver(port)
    search = opt("--search")
    if search:
        # pin the list to messages other test runs do not keep adding to
        for c in ["click 22 87", "click 420 24", f"type {search}", "key Enter", "sleep 2", ROW(0), "sleep 1"]:
            d.cmd(c)
    res = json.load(open(out)) if os.path.exists(out) else {}
    for mode in modes:
        d.cmd(f"theme {mode}")
        time.sleep(0.5)
        for n in names:
            r = run(d, n, mode, frames_dir)
            res[f"{mode}/{n}"] = r
            print(f"{mode:5} {n:15} " + " ".join(f"{k}={v}" for k, v in r.items()), flush=True)
            json.dump(res, open(out, "w"), indent=1)
    d.cmd("render-mode 0")


if __name__ == "__main__":
    main()
