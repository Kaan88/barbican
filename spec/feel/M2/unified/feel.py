"""M2 unified feel scenarios (M2 plan 6.5): unified_open, account_switch, import_progress, on the hidden Automation build.

Usage (the app started with tools/auto-run.ps1 start -Port 8885 and BARBICAN_SAMPLE=1: two in-process FakeJmap logins):
  python spec/feel/M2/unified/feel.py --port 8885 --dir artifacts/auto/8885/feel --mbox artifacts/auto/8885/big.mbox [--modes light,dark]

Each scenario records a frame burst of the whole window while the action runs and prints tools/flicker.py's summary
(--bento layout regions), plus each burst's first and last frame spread: a capture whose frames are one flat colour is
invalid and reported as such, never as a pass. Nothing here sends real input: every command goes over the loopback
automation channel to the off-screen, non-activating window.

  unified_open     Inbox on screen -> `unified on` (All inboxes, two logins merged)
  account_switch   All inboxes -> the Inbox of the account on screen (`unified inbox`); a switch between two signed-in
                   accounts needs real sessions (no sample path), so this is the list-source switch the unified row makes
  import_progress  Inbox on screen, `import-sim MBOX inbox` (500 messages) while the burst runs
"""
import json, os, sys, time

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "tools"))
import flicker  # noqa: E402
import scenarios  # noqa: E402  (its Driver: one loopback connection, `sleep N` handled locally)
from PIL import Image, ImageStat  # noqa: E402


def spread(path):
    """Standard deviation of the luminance of one frame: near 0 means a flat (invalid) capture."""
    with Image.open(path) as im:
        return round(ImageStat.Stat(im.convert("L")).stddev[0], 1)


def burst(d, out, ms, action):
    d.cmd(f"burst {ms} 10 {out} 2")
    time.sleep(0.25)
    replies = [d.cmd(c) for c in action]
    while d.cmd("burst-status").startswith("running"):
        time.sleep(0.2)
    while not d.cmd("burst-status").startswith(("done", "none")):
        time.sleep(0.2)
    frames = sorted(f for f in os.listdir(out) if f.startswith("f") and f.endswith(".png"))
    r = flicker.analyse(out, layout="bento") if frames else {"frames": 0}
    r["replies"] = replies
    r["first_spread"] = spread(os.path.join(out, frames[0])) if frames else None
    r["last_spread"] = spread(os.path.join(out, frames[-1])) if frames else None
    r["valid"] = bool(frames) and min(r["first_spread"], r["last_spread"]) > 2.0
    return r


def main():
    a = sys.argv[1:]
    opt = lambda k, dflt=None: a[a.index(k) + 1] if k in a else dflt  # noqa: E731
    port = int(opt("--port", "8885"))
    out_dir = opt("--dir", os.path.join(ROOT, "artifacts", "auto", str(port), "feel"))
    mbox = os.path.abspath(opt("--mbox", os.path.join(ROOT, "artifacts", "auto", str(port), "big.mbox")))
    modes = opt("--modes", "light").split(",")
    d = scenarios.Driver(port)
    results = {}
    for mode in modes:
        d.cmd(f"ui bento {mode}")
        time.sleep(0.5)
        plan = {
            "unified_open": (["unified off", "unified inbox", "sleep 1.2"], ["unified on"], 1800),
            "account_switch": (["unified on", "sleep 1.5"], ["unified inbox"], 1800),
            "import_progress": (["unified off", "unified inbox", "sleep 1.2"], [f"import-sim {mbox} inbox"], 4000),
        }
        for name, (setup, action, ms) in plan.items():
            # a starved capture thread (machine under load) gives a handful of frames: retry, as tools/scenarios.py does
            for attempt in range(3):
                for c in setup:
                    d.cmd(c)
                out = os.path.join(out_dir, f"{mode}_{name}_{attempt}")
                os.makedirs(out, exist_ok=True)
                r = burst(d, out, ms, action)
                if r["frames"] >= ms / 40:
                    break
            r["attempts"] = attempt + 1
            results[f"{mode}/{name}"] = r
            print(name, mode, json.dumps(results[f"{mode}/{name}"]))
    with open(os.path.join(out_dir, "results.json"), "w", encoding="utf-8") as f:
        json.dump(results, f, indent=1)


if __name__ == "__main__":
    main()
