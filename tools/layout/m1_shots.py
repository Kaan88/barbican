"""Sample-data screenshots of the M1 views: Bento and Nordic x dark and light x normal and more contrast, 1400x900.

Usage: python tools/layout/m1_shots.py --port 8841 [--out docs/milestones/M1-screens]
Needs the AUTOMATION build started hidden with BARBICAN_SAMPLE=1 (tools/auto-run.ps1 start; the data is FakeJmap's seed, never a real mailbox).
The picture is the XAML render (`shot`): a hidden off-screen window is not re-captured by WGC after a palette change (see tools/layout/m1_scenarios.py),
so the XAML tree is the source. The WebView2 body area is empty in it (the body view is the body stream's).
"""
import os, socket, sys, time


class Driver:
    def __init__(self, port):
        self.s = socket.create_connection(("127.0.0.1", port), timeout=60)
        self.f = self.s.makefile("rw", encoding="utf8")

    def cmd(self, c):
        self.f.write(c + "\n")
        self.f.flush()
        return self.f.readline().strip()


def main():
    a = sys.argv[1:]
    opt = lambda k, dflt=None: a[a.index(k) + 1] if k in a else dflt  # noqa: E731
    port = int(opt("--port", "8841"))
    out = os.path.abspath(opt("--out", "docs/milestones/M1-screens"))
    os.makedirs(out, exist_ok=True)
    d = Driver(port)
    d.cmd("resize 1400 900")
    for style in ("bento", "nordic"):
        for theme in ("dark", "light"):
            for contrast in ("normal", "more"):
                d.cmd(f"ui {style} {theme} {contrast}")
                time.sleep(0.6)
                name = f"{style}-{theme}-{contrast}.png"
                print(name, d.cmd(f"shot {os.path.join(out, name)}"))


if __name__ == "__main__":
    main()
