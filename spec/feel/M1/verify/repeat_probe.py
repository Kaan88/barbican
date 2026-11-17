"""Arrow-repeat probe for the M1 verify stage: 20 x `key j` (and `key k`) at 33 ms under the `rendering` probe.
Reports UI-thread frame cost (end-start), the longest frame-to-frame gap and input -> end of next frame."""
import json, socket, statistics, sys, time

port = int(sys.argv[1])
s = socket.create_connection(("127.0.0.1", port), timeout=60)
f = s.makefile("rw", encoding="utf8")


def cmd(c):
    f.write(c + "\n"); f.flush()
    return f.readline().strip()


out = {}
for layout in ("bento", "nordic"):
    cmd(f"ui {layout} dark")
    cmd("key Escape")
    time.sleep(0.5)
    cmd("uia-select e31")
    time.sleep(0.5)
    for key in ("j", "k"):
        cmd("probe-start rendering")
        time.sleep(0.2)
        for _ in range(20):
            cmd(f"key {key}")
            time.sleep(0.033)
        time.sleep(0.4)
        p = json.loads(cmd("probe-stop"))
        frames = p["frames"]
        events = [t for t, c in p["events"] if c.startswith("key")]
        cost = [e - st for st, e in frames]
        gaps = [frames[i + 1][0] - frames[i][0] for i in range(len(frames) - 1)]
        lat = []
        for t in events:
            nxt = [e for st, e in frames if e >= t]
            if nxt:
                lat.append(min(nxt) - t)
        out[f"{layout}/{key}x20"] = {
            "frames": len(frames),
            "cost_median_ms": round(statistics.median(cost), 2),
            "cost_max_ms": round(max(cost), 2),
            "gap_max_ms": round(max(gaps), 2),
            "input_to_frame_end_median_ms": round(statistics.median(lat), 2) if lat else None,
            "input_to_frame_end_max_ms": round(max(lat), 2) if lat else None,
            "inputs": len(events),
        }
        print(layout, key, out[f"{layout}/{key}x20"])
json.dump(out, open(sys.argv[2], "w"), indent=1)
