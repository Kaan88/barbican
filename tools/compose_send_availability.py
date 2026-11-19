"""Check actual composer controls in an already-running hidden BARBICAN_SAMPLE=1 session.

Start the Windows Automation app with tools/auto-run.ps1 on the requested private
desktop and port first. This uses only synthetic composer data; it never sends.
"""

import argparse
import time

import autoconn


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=8935)
    args = parser.parse_args()
    sock, stream = autoconn.connect(args.port)

    def command(text, expected):
        # Let the hidden XAML tree finish laying out after the preceding command.
        time.sleep(0.2)
        stream.write(text + "\n")
        stream.flush()
        reply = stream.readline().strip()
        print(f"{text}: {reply}")
        if expected not in reply:
            raise AssertionError(f"{text}: expected {expected!r}, got {reply!r}")

    try:
        command("compose-open new", "ok")
        command("uia-invoke composer.send", "ElementNotEnabledException")
        command("uia-invoke composer.send.arrow", "ElementNotEnabledException")
        command("compose-type to invalid", "ok")
        command("uia-invoke composer.send", "ok composer.send")
        command("compose-state", "status=\"Check the address: invalid\"")
        command("compose-do chip-remove to 0", "ok")
        command("uia-invoke composer.send.arrow", "ElementNotEnabledException")
        command("compose-type to valid@example.org", "ok")
        command("compose-key to Enter", "handled")
        command("uia-invoke composer.send.arrow", "ok composer.send.arrow")
        command("compose-do chip-remove to 0", "ok")
        command("uia-invoke composer.sendmenu.now", "ElementNotEnabledException")
        command("uia-invoke composer.sendmenu.schedule", "ElementNotEnabledException")
        command("compose-type to pending@example.org", "ok")
        command("uia-invoke composer.sendmenu.schedule", "ok composer.sendmenu.schedule")
        command("compose-state", "schedule=yes")
        command("uia-invoke composer.schedule.cancel", "ok composer.schedule.cancel")
        command("compose-state", "schedule=no")
        command("focus-guard", "own-is-foreground=no")
    finally:
        stream.close()
        sock.close()


if __name__ == "__main__":
    main()
