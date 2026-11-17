# M1 feel: body stream

Hidden AUTOMATION build on Windows (`tools/auto-run.ps1 start -Port 8857 -ProfileName body-8857`, `BARBICAN_SAMPLE=1`, FakeJmap seed).
The foreground window and the cursor were unchanged before and after every run. Linux was not run because the VM was unreachable.

- `body_feel.py` runs the scenarios: open_first, close_reader, open_again, switch_key, switch_click, switch_back, thread_switch and
  light_switch, in Bento and Nordic, dark and light.
- `body-results.json` holds the last run. Its cache was warm, so every open was a cache hit.
- `shell-checks.json` holds the shell checks: the CSP against a loopback beacon, links, and navigation.

## Numbers (2026-11-17, after the shell.js poll change)

| measure | value | gate |
|---|---|---|
| open-to-shown, cached switch (open call to the turn that applies the header and sends reveal) | 4.3 to 6.9 ms over 28 switches | cached switch ≤ 50 ms |
| send-to-ready (page handed to the shell until `ready:N`) | 3.9 to 5.7 ms; theme re-render 12.9 to 15.5 ms | none |
| open-to-shown, first open with no cache (fetch from the in-process fake, cid inlining, page) | 50.4 ms (first run, bento/dark) | dim only after 150 ms, so no dim was shown |
| BLANK / FLASH (tools/flicker.py on WGC bursts) | 0 / 0 in all 32 scenarios | 0 / 0 (weak evidence, see below) |
| idle frames in 2 s with the reader closed | 0 | 0 |

Before the change, shell.js polled the frame on `requestAnimationFrame` and then waited one more frame before `ready`.
That measured 55 ms send-to-ready and 60 ms open-to-shown for a cached switch, which failed the 50 ms gate.
shell.js now polls on the task queue and forces layout with `offsetHeight` before posting `ready`.
For comparison, the Rust baseline (`spec/feel/rust-baseline/flicker/after-review-bento.json`) measured content_ms of 18 ms for switch_key.

## What these numbers do and do not show

- **The window does not deliver frames on its own.** WGC of the hidden off-screen window delivers a frame only after a layout change.
  This is the M1 shell-ui note. The bursts wake it with a one-pixel resize after the action, so they show settled states (2 to 6 frames each).
- **Frames can mix old and new content.** In one burst, the WebView2 surface had moved on to the new message while the XAML part,
  including the list selection, was still the old frame. The WebView composes on its own and the XAML does not, so the mix comes from
  the capture, not from the app.
- **"Header and body in the same frame" is shown by the code, not by frames.** The header is applied in the same dispatcher turn that
  sends `reveal(N)` (`BodyShell.Ready`). The exit gate "input to first changed frame ≤ 21 ms" is therefore given only as open-to-shown
  plus at most one compositor frame (20.8 ms off-screen): ≤ 28 ms worst, ≤ 26 ms typical. It needs an on-screen or private-display
  measurement.
- **Reduce motion has nothing to settle.** The body view has no animation: the swap is one visibility change, and the dim is an
  opacity change with no transition.
