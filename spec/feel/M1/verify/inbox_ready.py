"""Inbox-interactive time of the hidden sample-data app: process start (pid.txt) to the first moment a message row
can be selected through UIA (`uia-select e31` replies ok). Polling and the auto-run start-up wait add to the number (upper bound)."""
import datetime, socket, sys, time

port, pidfile = int(sys.argv[1]), sys.argv[2]
started = datetime.datetime.fromisoformat(open(pidfile).read().strip().split("|")[1]).timestamp()
s = socket.create_connection(("127.0.0.1", port), timeout=60)
f = s.makefile("rw", encoding="utf8")


def cmd(c):
    f.write(c + "\n"); f.flush()
    return f.readline().strip()


first = None
for _ in range(400):
    r = cmd("uia-select e31")
    if r.startswith("ok"):
        first = time.time()
        break
    time.sleep(0.02)
print("reply", r)
print("process_start_to_row_ms", None if first is None else round((first - started) * 1000))
