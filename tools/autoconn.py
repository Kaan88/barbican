"""Connects to the automation port of a hidden Barbican run and proves the run's token (Fable review 1 F-08).

The token comes from BARBICAN_AUTOMATION_TOKEN, else from artifacts/auto/<port>/token, which tools/auto-run.ps1 writes when it
starts the run. The server answers "ok auth"; anything else means the token is wrong or the run has none (its port stays closed).
"""
import os
import socket

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def token_for(port):
    t = os.environ.get("BARBICAN_AUTOMATION_TOKEN")
    if t:
        return t.strip()
    path = os.path.join(ROOT, "artifacts", "auto", str(port), "token")
    try:
        with open(path, encoding="utf8") as f:
            return f.read().strip()
    except OSError:
        raise SystemExit(f"no automation token: set BARBICAN_AUTOMATION_TOKEN or start the run with tools/auto-run.ps1 ({path})")


def connect(port, timeout=30):
    """Returns (socket, text file) of an authenticated connection."""
    s = socket.create_connection(("127.0.0.1", port), timeout=timeout)
    f = s.makefile("rw", encoding="utf8")
    f.write("auth " + token_for(port) + "\n")
    f.flush()
    reply = f.readline().strip()
    if reply != "ok auth":
        s.close()
        raise SystemExit(f"automation port {port} refused the token: {reply or '(closed)'}")
    return s, f
