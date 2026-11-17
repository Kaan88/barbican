"""M1 verify: hidden-run checks of display I- rows on sample data (I-039 chevron, I-046 section collapse, I-156 keys)."""
import json, os, socket, sys, time

port, out = int(sys.argv[1]), os.path.abspath(sys.argv[2])
os.makedirs(out, exist_ok=True)
s = socket.create_connection(("127.0.0.1", port), timeout=60)
f = s.makefile("rw", encoding="utf8")


def cmd(c):
    f.write(c + "\n"); f.flush()
    return f.readline().strip()


def ids():
    return {e["id"]: e for e in json.loads(cmd("tree"))}


def reader():
    time.sleep(0.4)
    return cmd("body reader")


res = {}
cmd("resize 1400 900"); cmd("ui bento dark normal"); time.sleep(0.5)
t0 = ids()
res["I-039 collapse"] = cmd("invoke m1.chevron"); time.sleep(0.4)
t1 = ids()
res["I-039 child hidden after collapse"] = "m7" not in t1 or t1["m7"]["name"] is None
cmd(f"shot {os.path.join(out, 'i039-collapsed.png')}")
cmd("invoke m1.chevron"); time.sleep(0.4)
res["I-039 child back after expand"] = ids().get("m7", {}).get("name") == "Projects"
res["I-046 toggle"] = cmd("invoke sidebar.folders.toggle"); time.sleep(0.4)
t2 = ids()
res["I-046 folders hidden"] = "m1" not in t2 or t2["m1"]["name"] is None
cmd(f"shot {os.path.join(out, 'i046-folders-collapsed.png')}")
cmd("invoke sidebar.folders.toggle"); time.sleep(0.4)
res["I-046 folders back"] = ids().get("m1", {}).get("name") == "Inbox"
cmd("uia-select e40"); res["open e40"] = reader()
cmd("key j"); res["j"] = reader()
cmd("key j"); res["j2"] = reader()
cmd("key k"); res["k"] = reader()
cmd("key Down"); res["Down"] = reader()
cmd("key Up"); res["Up"] = reader()
cmd("key Escape"); res["Escape"] = reader()
res["/"] = cmd("key /")
cmd(f"shot {os.path.join(out, 'i156-slash.png')}")
cmd("key Escape")
res["popup-guard"] = cmd("popup-guard")
res["focus-guard"] = cmd("focus-guard")
for k, v in res.items():
    print(k, "=>", v)
json.dump(res, open(os.path.join(out, "irows.json"), "w"), indent=1)
