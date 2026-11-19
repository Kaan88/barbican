"""Check an already running hidden, synthetic Bulwark sample app on the supplied port."""
import argparse
import time

import autoconn


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("port", type=int)
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
        expect("focus-guard", "own-is-foreground=no")
        print("Passed composer transfer, undo, close confirmation and save checks.")


if __name__ == "__main__":
    main()
