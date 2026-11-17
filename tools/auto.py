"""Drive a Barbican started with BARBICAN_AUTOMATION=1 (hidden, no real input).
Usage: python tools/auto.py [--port N] "size" "shot C:/tmp/a.png" "click 30 160" "sleep 1" ...
"""
import os, sys, time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import autoconn  # noqa: E402

args = sys.argv[1:]
port = 8792
if len(args) >= 2 and args[0] == "--port":
    port = int(args[1]); args = args[2:]
s, f = autoconn.connect(port, timeout=20)
for c in args:
    if c.startswith("sleep "):
        time.sleep(float(c.split()[1])); continue
    f.write(c + "\n"); f.flush()
    print(c, "->", f.readline().strip())
