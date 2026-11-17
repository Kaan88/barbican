"""Feel scenarios of the M1 body stream against the AUTOMATION build (hidden window, BARBICAN_SAMPLE=1, own port and profile).

Usage: python spec/feel/M1/body_feel.py --port 8857 [--out artifacts/auto/8857/body] [--write spec/feel/M1/body-results.json]

Scenarios (names of the Rust baseline, spec/feel/rust-baseline/flicker): open_first, open_again, switch_key, switch_click, switch_back,
close_reader, thread_switch, light_switch; each in Bento and Nordic, dark and light.

Two kinds of numbers per scenario:
- in-process timings from `body timing`: open-to-shown (the open call to the dispatcher turn that applies header and body and sends
  reveal; the frame that shows it is the next compositor frame) and send-to-ready (the page handed to the shell to its ready signal);
  whether the open was served from the body cache.
- tools/flicker.py on a WGC burst of the scenario (BLANK / FLASH). A hidden off-screen window is composed only after a layout change
  (M1 shell-ui Fable log); the burst wakes it with a one-pixel resize after the action, so these frames show the settled state and
  little of the way there: weak evidence, kept for completeness.
"""
import json, os, shutil, socket, sys, time

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "..", "tools"))
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "..", "tools", "layout"))
import flicker  # noqa: E402
import m1_scenarios  # noqa: E402


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


def kv(line):
    parts = line.split()
    return {parts[i]: parts[i + 1] for i in range(0, len(parts) - 1, 2)}


def timing(d):
    return kv(d.cmd("body timing"))


def settle(d, want=None, timeout=5.0):
    end = time.time() + timeout
    while time.time() < end:
        r = d.cmd("body reader").split()
        if r[2] in ("Ready", "Truncated", "Failed", "Empty") and (want is None or r[1] == want):
            return r
        time.sleep(0.02)
    return d.cmd("body reader").split()


def wake(d, w, h):
    d.cmd(f"resize {w + 1} {h}")
    d.cmd(f"resize {w} {h}")


def measure(d, out, name, actions, w, h, want=None):
    before = timing(d)
    shutil.rmtree(os.path.join(out, name), ignore_errors=True)
    d.cmd(f"burst 1200 0 {os.path.join(out, name)} 2")
    time.sleep(0.2)
    t0 = time.time()
    for a in actions:
        d.cmd(a)
    reader = settle(d, want)
    wall = (time.time() - t0) * 1000
    wake(d, w, h)
    while not d.cmd("burst-status").startswith(("done", "none")):
        time.sleep(0.1)
    after = timing(d)
    fl = flicker.analyse(os.path.join(out, name))
    opened = int(after["opens"]) - int(before["opens"])
    cached = int(after["cached"]) - int(before["cached"])
    return {
        "reader": reader[1] + " " + reader[2],
        "open_to_shown_ms": None if opened == 0 or after["open-to-shown"] == "-" else float(after["open-to-shown"]),
        "send_to_ready_ms": None if after["send-to-ready"] == "-" or after["send-to-ready"] == before["send-to-ready"] else float(after["send-to-ready"]),
        "opens": opened,
        "cached": cached,
        "wall_ms": round(wall, 1),
        "frames": fl.get("frames"),
        "blank": fl.get("blank"),
        "flash": fl.get("flash"),
    }


def run_layout(d, out, layout_name, theme):
    layout = json.load(open(os.path.join(os.path.dirname(__file__), "..", "..", "..", "tools", "layout", f"m1-{layout_name}.json")))
    m1_scenarios.install_regions(layout)
    w, h = layout["window"]
    d.cmd(f"resize {w} {h}")
    d.cmd(f"ui {layout_name} {theme}")
    wake(d, w, h)
    d.cmd("key Escape")
    time.sleep(0.5)
    o = os.path.join(out, f"{layout_name}-{theme}")
    res = {}
    res["open_first"] = measure(d, o, "open_first", ["uia-select e31"], w, h, "e31")
    res["close_reader"] = measure(d, o, "close_reader", ["key Escape"], w, h)
    res["open_again"] = measure(d, o, "open_again", ["uia-select e31"], w, h, "e31")
    res["switch_key"] = measure(d, o, "switch_key", ["key j"], w, h, "e24")
    res["switch_click"] = measure(d, o, "switch_click", ["uia-select e40"], w, h, "e40")
    res["switch_back"] = measure(d, o, "switch_back", ["uia-select e24"], w, h, "e24")
    d.cmd("uia-select e21")
    settle(d, "e21")
    time.sleep(0.3)
    res["thread_switch"] = measure(d, o, "thread_switch", ["thread 0"], w, h, "e8")
    other = "light" if theme == "dark" else "dark"
    res["light_switch"] = measure(d, o, "light_switch", [f"ui {layout_name} {other}", "sleep 0.3"], w, h)
    d.cmd(f"ui {layout_name} {theme}")
    return res


def main():
    a = sys.argv[1:]
    opt = lambda k, dflt=None: a[a.index(k) + 1] if k in a else dflt  # noqa: E731
    port = int(opt("--port", "8857"))
    out = os.path.abspath(opt("--out", os.path.join("artifacts", "auto", str(port), "body")))
    d = Driver(port)
    results = {}
    for layout_name in ("bento", "nordic"):
        for theme in ("dark", "light"):
            results[f"{layout_name}/{theme}"] = run_layout(d, out, layout_name, theme)
    d.cmd("key Escape")
    time.sleep(0.5)
    d.cmd("probe-start wgc")
    time.sleep(2)
    probe = json.loads(d.cmd("probe-stop"))
    results["idle_frames_2s"] = len(probe["frames"])
    results["body_state"] = d.cmd("body state")
    if opt("--write"):
        json.dump(results, open(opt("--write"), "w"), indent=1)
    for k, v in results.items():
        if isinstance(v, dict):
            for s, r in v.items():
                print(k, s, r)
        else:
            print(k, v)


if __name__ == "__main__":
    main()
