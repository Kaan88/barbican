"""Replays the hidden ui.alert / ui.confirm / ui.prompt walkthrough (docs/milestones/M6b-solo-parity.md, "Native add-on dialogs").

Needs a run started with the Automation build and the synthetic sample account (see the module docstring of the milestone section):

    $env:BARBICAN_SAMPLE='1'; $env:BARBICAN_SAMPLE_ADDON_ACCOUNT='1'; $env:BARBICAN_SAMPLE_ORGANIZATION_POLICY='1'
    tools/auto-run.ps1 start -Port 9311
    python tools/dialog-probe-replay.py 9311
    tools/auto-run.ps1 stop -Port 9311

No real input is produced: every step is an in-process automation command (peer invoke, the dialog form's own press / field / key methods,
the shell's key router). Synthetic data only (FakeJmap sample account, a synthetic add-on). Screenshots and results.json go to artifacts/auto/<port>/dialog-probe/.
"""
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import autoconn  # noqa: E402

RUN = "reader.addon.sample-dialog-probe.run"
SECOND = "reader.addon.sample-dialog-probe.second"
RESET = "reader.addon.sample-dialog-probe.reset"
SOURCE = "From the add-on Sample dialog probe"
ADDON_NAME = "Sample dialog probe"


class Probe:
    def __init__(self, port):
        self.port = port
        self.out = os.path.join(autoconn.ROOT, "artifacts", "auto", str(port), "dialog-probe")
        os.makedirs(self.out, exist_ok=True)
        self.results = []
        self.theme = "dark"
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
        return self.send("addons-fixture dialog-probe " + rest)

    def state(self):
        reply = self.probe("state")
        return json.loads(reply[5:]) if reply.startswith("open ") else None

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
        self.results.append({"theme": self.theme, "step": name, "pass": bool(ok), "value": value, "note": note})
        print(("PASS " if ok else "FAIL ") + f"[{self.theme}] {name}: {value} {note}".rstrip())

    def toast(self, name):
        """The value the frame reported for scenario NAME (toast text dp|name|value)."""
        def read():
            reply = self.send("toast-last source")
            marker = "|dp|" + name + "|"
            return reply.split(marker, 1)[1] if marker in reply else None
        value = self.wait("the report of " + name, read)
        self.last_toast = time.time()
        return value

    def pace(self):
        """The host lets an add-on show 5 toasts in 10 s and drops the rest silently; the reports are toasts, so keep a gap."""
        time.sleep(max(0.0, 2.6 - (time.time() - self.last_toast)))

    def shot(self, name):
        time.sleep(1.0)  # the hidden window composes the dialog a moment after its state is set
        path = os.path.join(self.out, f"{self.theme}-{name}.png")
        reply = self.send("wshot " + path)
        self.record("screenshot " + name, reply.startswith("saved"), reply, path)

    def quiet(self):
        """Waits until the last toast has left the screen, so a capture shows the dialog alone."""
        self.wait("the toast to leave", lambda: self.send("toast-last").endswith("|gone") or self.send("toast-last") == "none", 20)

    def open_next(self, expect_title, clean=False):
        """Focuses the run button, invokes it and waits for the dialog; returns its description."""
        self.pace()
        if clean:
            self.quiet()
        self.send("focus " + RUN)
        self.send("invoke " + RUN)
        d = self.wait("the dialog", self.state)
        # the form moves the focus on a low-priority tick after it loads
        d = self.wait("the dialog to take the focus", lambda: (lambda x: x if x and x["focus"] != RUN else None)(self.state()), 5)
        self.record(f"opened {expect_title}", d["title"] == expect_title, d["title"])
        return d

    def closed_with_focus(self, name):
        self.wait("the dialog to close", lambda: self.state() is None)
        focus = self.wait("focus back", lambda: self.send("focus-id") == RUN and RUN, 5)
        self.record(name + ": dialog closed, focus returned to the opener", focus == RUN, focus)

    def expect(self, name, got, want):
        self.record(name, got == want, got, "" if got == want else f"(expected {want})")

    # ---- the scenarios, in the order of the add-on's list --------------------------------------------------------------------
    def alert(self, kind, how):
        d = self.open_next("Probe alert", clean=kind == "alert-ok")
        if kind == "alert-ok":
            self.expect("alert: attribution line", d["source"], SOURCE)
            self.expect("alert: message", d["message"], "Synthetic notice for the probe.\nSecond line of the message.")
            self.expect("alert: only an OK button", (d["ok"]["label"], d["cancel"]), ("OK", None))
            self.expect("alert: first focus is OK", d["focus"], "plugin.dialog.ok")
            self.shot("alert")
        self.step(how)
        self.expect(kind + " resolved", self.toast(kind), "undefined")
        self.closed_with_focus(kind)

    def step(self, how):
        if how == "ok":
            self.expect("press OK", self.probe("press ok"), "ok")
        elif how == "cancel":
            self.expect("press Cancel", self.probe("press cancel"), "ok")
        elif how == "form-escape":
            self.expect("Escape through the focused control", self.probe("key Escape"), "handled")
        elif how == "router-escape":
            self.expect("Escape through the shell key router", self.send("key Escape"), "ok")
        elif how == "enter":
            self.expect("Enter on the focused button", self.probe("key Enter"), "button")
        else:
            raise ValueError(how)

    def confirm(self, kind, title, how, labels=("Proceed", "Not now"), want="true"):
        d = self.open_next(title, clean=kind == "confirm-cancel")
        self.expect(kind + ": labels", (d["ok"]["label"], (d["cancel"] or {}).get("label")), labels)
        self.expect(kind + ": first focus", d["focus"], "plugin.dialog.ok")
        if kind == "confirm-ok":
            self.expect("confirm: title falls back to the add-on name when none is given", d["title"], ADDON_NAME)
        if kind == "confirm-cancel":
            self.shot("confirm")
        self.step(how)
        self.expect(kind + " resolved", self.toast(kind), want)
        self.closed_with_focus(kind)

    def danger(self, kind, how, want="false"):
        d = self.open_next("Delete synthetic item", clean=kind == "danger-enter")
        self.expect(kind + ": danger confirm", (d["danger"], d["ok"]["label"], d["cancel"]["label"]), (True, "Delete", "Cancel"))
        self.expect(kind + ": first focus is the safe button (Cancel)", d["focus"], "plugin.dialog.cancel")
        if kind == "danger-enter":
            self.shot("danger")
        if kind == "danger-ok":
            self.expect("danger: Enter that is not on a button does not accept", self.probe("key Enter nonbutton"), "ignored")
            self.expect("danger: dialog still open after that Enter", self.state() is not None, True)
        self.step(how)
        self.expect(kind + " resolved", self.toast(kind), want)
        self.closed_with_focus(kind)

    def prompt(self, kind):
        d = self.open_next("Probe prompt", clean=kind == "prompt-ok")
        self.expect(kind + ": fields", [(f["Label"], f["Password"], f["Required"]) for f in d["fields"]],
                    [("User name", False, True), ("Secret", True, True), ("Note (optional)", False, False)])
        self.expect(kind + ": first focus is the first field", d["focus"], "plugin.dialog.field.0")
        self.expect(kind + ": password note shown", d["passwordNote"], True)
        self.expect(kind + ": Submit disabled while required fields are empty", (d["ok"]["label"], d["ok"]["enabled"]), ("Send", False))
        if kind == "prompt-ok":
            self.shot("prompt-empty")
            self.expect("required: pressing disabled Send is refused", self.probe("press ok"), "refused")
            self.expect("required: Enter in the empty field does not submit", self.probe("key Enter"), "handled")
            self.expect("required: dialog still open", self.state() is not None, True)
            self.probe("field 0 alice@example.org")
            self.expect("required: still disabled with only the user name", self.state()["ok"]["enabled"], False)
            self.probe("field 1 synthetic-secret")
            enabled = self.wait("Send to enable", lambda: self.state()["ok"]["enabled"], 3)
            self.expect("required: enabled once both are filled", enabled, True)
            self.shot("prompt-filled")
            self.expect("press Send", self.probe("press ok"), "ok")
            self.expect(kind + " resolved", self.toast(kind), '{"user":"alice@example.org","secret":"synthetic-secret","note":""}')
        elif kind == "prompt-cancel":
            self.probe("field 0 discarded")
            self.expect("press Cancel", self.probe("press cancel"), "ok")
            self.expect(kind + " resolved", self.toast(kind), "null")
        elif kind == "prompt-enter":
            self.probe("field 0 bob@example.org")
            self.probe("field 1 synthetic-secret-2")
            self.probe("field 2 a note")
            self.expect("Enter in a field submits", self.probe("key Enter"), "handled")
            self.expect(kind + " resolved", self.toast(kind), '{"user":"bob@example.org","secret":"synthetic-secret-2","note":"a note"}')
        else:
            self.expect("Escape cancels from a field", self.probe("key Escape"), "handled")
            self.expect(kind + " resolved", self.toast(kind), "null")
        self.closed_with_focus(kind)

    def concurrent(self):
        self.open_next("Probe confirm")
        self.send("invoke " + SECOND)
        error = self.toast("second-dialog")
        self.record("second concurrent dialog is refused", error.startswith("ERR"), error)
        d = self.state()
        self.expect("still exactly the first dialog", d["title"] if d else None, "Probe confirm")
        self.expect("press Proceed on the first", self.probe("press ok"), "ok")
        self.expect("first resolved normally", self.toast("concurrent-first"), "true")
        self.closed_with_focus("concurrent")

    def disable_while_open(self):
        d = self.open_next("Delete synthetic item")
        self.record("disable: dialog is open first", d is not None, d["title"])
        self.expect("disable the add-on", self.probe("disable"), "ok")
        self.wait("the dialog to close", lambda: self.state() is None)
        self.record("disable: dialog closed by the add-on going away", True, "none")
        status = self.probe("status")
        self.record("disable: add-on not running", "running=False" in status, status)
        time.sleep(3)
        reply = self.send("toast-last source")
        self.record("disable: the dead frame reported nothing", "|dp|disable-open|" not in reply, reply)
        self.expect("re-enable the add-on", self.probe(""), "ok")
        self.wait("the reader button", lambda: self.send("bounds " + RUN).startswith(tuple("0123456789")))

    def pass_(self, theme):
        self.theme = theme
        self.send("ui bulwark " + theme)
        self.send("resize 1296 736")
        self.send("select #1")
        time.sleep(1)
        self.send("select #0")
        self.wait("the reader button", lambda: self.send("bounds " + RUN).startswith(tuple("0123456789")))
        self.send("invoke " + RESET)
        self.toast("reset")
        self.alert("alert-ok", "ok")
        self.alert("alert-escape", "router-escape")
        self.alert("alert-enter", "enter")
        self.confirm("confirm-ok", ADDON_NAME, "ok", labels=("Confirm", "Cancel"), want="true")
        self.confirm("confirm-cancel", "Probe confirm", "cancel", want="false")
        self.confirm("confirm-escape", "Probe confirm", "form-escape", want="false")
        self.confirm("confirm-enter", "Probe confirm", "enter", want="true")
        self.danger("danger-enter", "enter", want="false")
        self.danger("danger-ok", "ok", want="true")
        self.danger("danger-cancel", "cancel", want="false")
        self.danger("danger-escape", "router-escape", want="false")
        self.prompt("prompt-ok")
        self.prompt("prompt-cancel")
        self.prompt("prompt-enter")
        self.prompt("prompt-escape")
        self.concurrent()
        self.disable_while_open()


def main():
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 9311
    p = Probe(port)
    p.send("caption")  # attaches the window chrome: the dialog host needs the active window
    print("install:", p.probe(""))
    p.send("pane-set 180 240")
    try:
        for theme in ("dark", "light"):
            p.pass_(theme)
    except AssertionError as error:
        p.record("walkthrough", False, str(error))
    p.results.append({"focus-guard": p.send("focus-guard"), "popup-guard": p.send("popup-guard")})
    with open(os.path.join(p.out, "results.json"), "w", encoding="utf8") as f:
        json.dump(p.results, f, indent=1)
    failed = [r for r in p.results if r.get("pass") is False]
    print(f"{len([r for r in p.results if 'pass' in r]) - len(failed)} passed, {len(failed)} failed; {p.results[-1]}")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
