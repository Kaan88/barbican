"""Replays the hidden add-on frames walkthrough: the `app-page` frame of an add-on page, `ui.setDayLabels` in the calendar views, and `ui.openDialog` with its onResult callback.

Needs a run started with the Automation build, the synthetic sample account and the pinned sandbox bootstrap (not part of the repository; an AUTOMATION build of an auto-* profile takes it
from BARBICAN_BOOTSTRAP_DIR, every file still checked against its pinned size and SHA-256):

    $env:BARBICAN_SAMPLE='1'; $env:BARBICAN_SAMPLE_ADDON_ACCOUNT='1'; $env:BARBICAN_SAMPLE_ORGANIZATION_POLICY='1'
    $env:BARBICAN_BOOTSTRAP_DIR='<folder with the pinned runtime-*.js, kit-*.css, chunk-*.js, manifest.json>'
    tools/auto-run.ps1 start -Port 47741 -ProfileName auto-addonframes
    python tools/frames-probe-replay.py 47741
    tools/auto-run.ps1 stop -Port 47741

No real input is produced: every step is an in-process automation command (the shell's own commands, peer invoke, the dialog form's own press method). Synthetic data only (FakeJmap sample account,
a synthetic add-on). Screenshots and results.json go to artifacts/auto/<port>/frames/.
"""
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import autoconn  # noqa: E402

PLUGIN = "sample-frames-probe"
BUTTON = "reader.addon." + PLUGIN + "."


class Probe:
    def __init__(self, port):
        self.port = port
        self.out = os.path.join(autoconn.ROOT, "artifacts", "auto", str(port), "frames")
        os.makedirs(self.out, exist_ok=True)
        self.results = []
        self.last_toast = 0.0

    def send(self, line):
        s, f = autoconn.connect(self.port, timeout=60)
        try:
            f.write(line + "\n")
            f.flush()
            return f.readline().rstrip("\n")
        finally:
            s.close()

    def probe(self, rest):
        return self.send("addons-fixture frames-probe " + rest)

    def wait(self, what, check, seconds=15):
        end = time.time() + seconds
        value = None
        while time.time() < end:
            value = check()
            if value:
                return value
            time.sleep(0.25)
        raise AssertionError("timed out waiting for " + what)

    def record(self, name, ok, value, note=""):
        self.results.append({"step": name, "pass": bool(ok), "value": value, "note": note})
        print(("PASS " if ok else "FAIL ") + f"{name}: {value} {note}".rstrip())

    def expect(self, name, got, want):
        self.record(name, got == want, got, "" if got == want else f"(expected {want})")

    def shot(self, name):
        time.sleep(1.0)  # the hidden window composes a moment after its state is set
        path = os.path.join(self.out, name + ".png")
        reply = self.send("wshot " + path.replace("\\", "/"))
        self.record("screenshot " + name, reply.startswith("saved"), reply, path)

    def bounds(self, element):
        reply = self.send("bounds " + element)
        parts = reply.split()
        ok = len(parts) == 4 and all(p.replace(".", "", 1).isdigit() for p in parts)
        return [float(p) for p in parts] if ok else None

    def toast(self, name, seconds=20):
        """The value the add-on reported for scenario NAME (toast text fp|name|value)."""
        def read():
            reply = self.send("toast-last source")
            marker = "|fp|" + name + "|"
            return reply.split(marker, 1)[1] if marker in reply else None
        value = self.wait("the report of " + name, read, seconds)
        self.last_toast = time.time()
        return value

    def pace(self):
        """The host lets an add-on show 5 toasts in 10 s and drops the rest silently; the reports are toasts, so keep a gap."""
        time.sleep(max(0.0, 2.8 - (time.time() - self.last_toast)))

    def dialog(self):
        reply = self.probe("dialog-state")
        return json.loads(reply[5:]) if reply.startswith("open ") else None

    def open_dialog(self, button, shown=True):
        self.pace()
        self.send("focus " + BUTTON + button)
        self.expect("invoke " + button, self.send("invoke " + BUTTON + button), "ok")
        return self.wait("the dialog", self.dialog) if shown else None

    def run(self):
        self.expect("install", self.probe("install"), "ok")
        status = self.wait("the add-on to run", lambda: (lambda x: x if "running=True" in x else None)(self.probe("status")))
        self.record("add-on running and offering its slots", "app-page(fill)" in status and "plugin-dialog" in status, status)
        self.send("caption")  # the dialog host needs the window chrome attached
        self.send("ui bulwark dark")
        self.send("resize 1296 736")

        # ---- OUTCOME 1: the app-page frame, sized and mounted --------------------------------------------------------------
        self.expect("open the page", self.probe("open-page reports/2026"), "ok " + PLUGIN + "/overview")
        time.sleep(3.0)
        frame = self.bounds("plugin-page.frame")
        rail = self.bounds("rail")
        self.record("frame mounted beside the rail and filling the area", frame is not None and rail is not None and frame[0] >= rail[0] + rail[2] - 1 and frame[2] > 900 and frame[3] > 600,
                    frame)
        self.shot("page")

        # ---- day labels --------------------------------------------------------------------------------------------------
        labels = self.probe("labels")
        self.record("two labels registered", labels.startswith("labels=2"), labels)
        self.expect("open the calendar", self.probe("open-calendar"), "ok")
        self.send("cal-view day")
        time.sleep(1.5)
        day = self.bounds("cal.head.label." + time.strftime("%Y-%m-%d"))
        self.record("day view: label under the date", day is not None, day)
        self.shot("calendar-day")
        self.send("cal-view week")
        time.sleep(1.5)
        week = self.bounds("cal.head.label." + time.strftime("%Y-%m-%d"))
        self.record("week view: label under the date", week is not None, week)
        self.shot("calendar-week")
        self.send("cal-view month")
        time.sleep(1.5)
        month = self.bounds("cal.cell.label." + time.strftime("%Y-%m-%d"))
        self.record("month view: label beside the date", month is not None, month)
        self.shot("calendar-month")

        # ---- OUTCOME 2: ui.openDialog -------------------------------------------------------------------------------------
        self.expect("open mail", self.probe("open-mail"), "ok")
        self.send("pane-set 180 240")
        self.send("select #1")
        time.sleep(1)
        self.send("select #0")
        self.wait("the reader button", lambda: self.bounds(BUTTON + "dialog"))

        d = self.open_dialog("dialog")
        time.sleep(1.5)  # the frame draws after the dialog opens
        d = self.dialog()
        self.expect("dialog title", d["title"], "Probe dialog")
        self.expect("dialog attribution line", d["source"], "From the add-on Sample frames probe")
        self.record("dialog holds the add-on's frame (a WebView mounted, no note)", "WebView2" in "".join(d["mounted"]) and d["note"] is None, d["mounted"])
        content = [float(v) for v in d["content"].split(",")]
        frame_box = [float(v) for v in d["frame"].split(",")]
        window = [float(v) for v in d["window"].split("x")]
        self.record("dialog frame sized (non-empty, inside the window)", frame_box[2] > 200 and frame_box[3] > 20 and frame_box[0] + frame_box[2] <= window[0], d["frame"], "window " + d["window"])
        self.record("dialog content fits the 480 px dialog (card padding and border kept)", content[2] <= 480 - 48 - 2, d["content"])
        self.shot("dialog-open")
        self.expect("Close ends the dialog", self.probe("dialog-close"), "ok")
        self.wait("the dialog to close", lambda: self.dialog() is None)
        self.expect("closed without a result resolves null", self.toast("dialog"), "null")

        self.open_dialog("dialog-auto")
        self.record("auto dialog open", True, "waiting for the frame's own button")
        self.shot("dialog-auto-open")
        self.wait("the dialog to close by the frame's onResult", lambda: self.dialog() is None, 25)
        answered = self.toast("dialog-auto")
        self.expect("onResult value reaches the add-on", answered, '{"picked":"/docs/a.txt","n":2}')

        self.open_dialog("dialog-bad", shown=False)
        self.record("an unsupported slot opens no dialog", self.dialog() is None, "none")
        bad = self.toast("dialog-bad")
        self.record("unsupported slot is refused with an error", bad.startswith("ERR"), bad)

        # a disabled add-on: its dialog goes with it
        d = self.open_dialog("dialog")
        self.expect("disable while open", self.probe("disable"), "ok")
        self.wait("the dialog to close with the add-on", lambda: self.dialog() is None)
        status = self.probe("status")
        self.record("the add-on stopped and its dialog is gone", "running=False" in status, status)

        # an add-on that was never granted ui:observe (installed once the first add-on is off: with more than three actions the reader moves them into its overflow menu)
        self.expect("install the add-on without ui:observe", self.probe("install-denied"), "ok")
        self.send("select #1")
        time.sleep(1)
        self.send("select #0")
        denied_button = "reader.addon.sample-frames-denied.dialog"
        self.wait("the denied add-on's reader button", lambda: self.bounds(denied_button))
        self.pace()
        self.send("focus " + denied_button)
        self.expect("invoke the denied add-on's dialog", self.send("invoke " + denied_button), "ok")
        denied = self.toast("denied")
        self.record("no ui:observe: refused with a permission error, nothing drawn", denied.startswith("ERR") and "permission" in denied.lower() and self.dialog() is None, denied)

        failed = [r for r in self.results if not r["pass"]]
        with open(os.path.join(self.out, "results.json"), "w", encoding="utf-8") as handle:
            json.dump(self.results, handle, indent=1)
        print(f"{len(self.results) - len(failed)} passed, {len(failed)} failed")
        return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(Probe(int(sys.argv[1]) if len(sys.argv) > 1 else 47741).run())
