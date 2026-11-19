"""Check an already running hidden, synthetic Bulwark sample app on the supplied port."""
import argparse
import time
from pathlib import Path

import autoconn


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("port", type=int)
    parser.add_argument("--capture", type=Path, help="Save the detached composer at its minimum size")
    args = parser.parse_args()
    sock, stream = autoconn.connect(args.port)
    with sock, stream:
        def send(command):
            stream.write(command + "\n")
            stream.flush()
            return stream.readline().strip()

        def expect(command, expected):
            actual = send(command)
            if expected not in actual:
                raise AssertionError(f"{command}: expected {expected!r}, got {actual!r}")
            return actual

        def wait(command, expected):
            deadline = time.monotonic() + 4
            while time.monotonic() < deadline:
                if expected in send(command):
                    return
                time.sleep(0.05)
            expect(command, expected)

        expect("compose-window sample", "sample")
        expect("ui bulwark light", "ok")
        expect("compose-open new", "ok")
        expect("compose-type to recipient@example.org", "ok")
        expect("compose-key to Enter", "handled")
        expect("compose-set subject Window test", "ok")
        expect("compose-edit insert Original", "ok")
        expect("compose-do attach note.txt Sample", "ok")
        expect("compose-do save", "saved")
        baseline = expect("compose-state", "note.txt:Done")
        assert 'body="Original"' in baseline and "draft=none" not in baseline

        expect("compose-window pop", "child")
        wait("compose-window state", "child loaded")
        assert send("compose-state") == baseline
        expect("compose-edit undo", "ok")
        expect("compose-state", 'body=""')
        expect("compose-edit insert In child", "ok")
        expect("compose-window return", "main")
        expect("compose-edit undo", "ok")
        expect("compose-state", 'body=""')

        expect("compose-window pop", "child")
        expect("compose-set subject Child close", "ok")
        send("compose-window close")
        wait("compose-window state", "child confirmation")
        expect("compose-window return", "child confirmation")
        expect("compose-dialog cancel", "ok")
        expect("ui bulwark dark", "ok")
        expect("compose-set subject Main close", "ok")
        expect("close", "ok")
        wait("compose-window state", "child confirmation")
        expect("compose-dialog cancel", "ok")
        wait("compose-window state", "child loaded")
        expect("compose-set subject Save and close", "ok")
        send("compose-window close")
        wait("compose-window state", "child confirmation")
        expect("compose-dialog save", "ok")
        wait("compose-window state", "main")
        expect("compose-state", "closed")
        expect("select #0", "ok")
        deadline = time.monotonic() + 4
        while not send("bounds reader.reply").split()[0].lstrip("-").isdigit():
            if time.monotonic() >= deadline:
                raise AssertionError("Reply control did not appear")
        expect("invoke reader.reply", "ok")
        wait("compose-state", "docked=True")
        expect("compose-edit insert Docked note", "ok")
        expect("compose-window pop", "child")
        wait("compose-window state", "child loaded")
        send("compose-window minimum")
        deadline = time.monotonic() + 4
        while True:
            bounds = send("compose-window bounds")
            width, height = map(int, bounds.split("x"))
            if 400 <= width <= 500 and 460 <= height <= 580:
                break
            if time.monotonic() >= deadline:
                raise AssertionError(f"Unexpected minimum client bounds: {bounds}")
            time.sleep(0.05)
        if args.capture:
            expect("compose-window shot " + str(args.capture.resolve()), "saved ")
        expect("compose-window return", "main")
        expect("compose-state", "docked=True")
        expect("compose-state", 'Docked note')
        expect("compose-window pop", "child")
        expect("compose-set subject Discard from child", "ok")
        send("compose-window close")
        wait("compose-window state", "child confirmation")
        expect("compose-dialog discard", "ok")
        wait("compose-state", "closed")
        wait("compose-window state", "main")
        expect("focus-guard", "own-is-foreground=no")
        print("Passed composer transfer, undo, minimum size, docked reply, close, save and discard checks.")


if __name__ == "__main__":
    main()
