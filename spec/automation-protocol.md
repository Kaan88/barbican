# Automation protocol (Layer A)

The hidden in-app automation server of AUTOMATION builds (`BARBICAN_AUTOMATION=1`, loopback only,
`BARBICAN_AUTOMATION_PORT`, default 8792). One command per line, one reply line. The command set is the one of the
Rust app (`src/automation.rs` header at tag `rust-final`), so `tools/auto.py`, `tools/motion.py`, `tools/flicker.py` and
`tools/scenarios.py` run unchanged. Decisions and measurements: [ADR 0007](../docs/adr/0007-hidden-automation.md).
Reference implementation of the transport, click, capture and probe commands: `spikes/s4-automation/`.

Status column: **spike** = implemented and measured in `spikes/s4-automation`; **M1** = to port with the real app;
**n/a** = cannot be done in-process (Layer C); **drop** = Rust-only.

Coordinates are logical px of the window's client area. Replies: `ok`, a value, or `error ...` / `unknown command ...`.

## Input (no real input is ever produced)
| Command | Meaning in C# | Status |
|---|---|---|
| `click X Y`, `dclick X Y` | smallest UIA element under the point with Invoke, Toggle, SelectionItem or ExpandCollapse, pattern call (a text box is focused through XAML; a point in the web area clicks the DOM element) | spike |
| `rclick X Y` | `ContextFlyout.ShowAt` of the element under the point | spike |
| `peer-click X Y` | in-process AutomationPeer path (needed for XAML drawn over a WebView2, invisible to UIA clients) | spike |
| `scroll X Y DY` | `ScrollViewer.ChangeView` under the point (DY in px, positive = down) | spike |
| `type TEXT` | appends to the Value pattern of the XAML-focused element | spike |
| `key CHORD` | `KeyRouter.Handle` (Enter, Escape, Tab, Up, Down, Ctrl+N in the spike; the full table with the app) | spike, M1 |
| `move X Y` | accepted as a no-op (no hover injection in-process) | spike (no-op) |
| `drag X1 Y1 X2 Y2` | not possible in-process | n/a (Layer C) |
| `every MS N CMD` | UI-thread timer; a token `A+Bi` becomes A + B*i for the i-th run | spike |

## New (by AutomationId, robust against layout changes)
| Command | Meaning | Status |
|---|---|---|
| `uia-click X Y`, `uia-invoke ID`, `uia-select ID`, `uia-toggle ID` | UIA client, patterns only (`uia-click` replies `ok <id or name>`) | spike |
| `uia-text ID VALUE`, `uia-append ID TEXT` | Value pattern `SetValue` | spike |
| `invoke ID`, `select ID`, `text ID VALUE` | in-process peer variants (work over a WebView2) | spike |
| `uia-find TEXT`, `uia-tree`, `tree` | debugging: UIA elements by name with rectangle and patterns; elements that carry an AutomationId | spike |
| `patterns X Y` | peer classes and patterns along the ancestors of the point (debugging) | spike |
| `status`, `focus-guard` | spike-only read-outs (status text; foreground window and cursor position for the no-focus proof) | spike |

## Capture and measurement
| Command | Meaning | Status |
|---|---|---|
| `shot PATH.png` | XAML only (`RenderTargetBitmap`); the WebView area is empty | spike |
| `wshot PATH.png` | whole window incl. WebView2 and in-window flyouts, Windows.Graphics.Capture, client area | spike |
| `wshot-pw PATH.png` | `PrintWindow`; **black for WinUI 3**, kept only to document that it is not a fallback | spike (drop in M1) |
| `burst MS EVERY DIR [STEP]`, `burst-status` | WGC frames of the window into DIR (`f####_<ms>.png`, `events.txt`, `meta.json`); `tools/flicker.py` reads it | spike |
| `probe-start [wgc\|rendering]`, `probe-stop` | `{"frames":[[start,end],...],"events":[[t,cmd],...],"source":...}`, ms since start; `tools/motion.py` reads it | spike |
| `size`, `resize W H`, `title` | physical size and scale (`W H scale S`), logical resize, window title (with unread badge in the app) | spike |
| `motion reduce\|normal\|rapid\|calm` | in-memory motion settings | spike (flag only) |
| `theme dark\|light`, `ui bento\|nordic dark\|light [more\|normal]` | in-memory style, theme, contrast | spike (theme), M1 (ui) |
| `anim comp\|sb` | spike-only: a 150 ms composition-thread or storyboard animation | spike |
| `wv-eval JS` | runs JS in the web view and returns its result directly (the Rust `wv-result` poll is not needed) | spike |
| `close` | closes the window | spike |

## To port with the real app (M1+), same names and meaning as the Rust header
`pick PATH[|PATH...]`, `save PATH`/`savepick PATH` (queued answers for the file pickers), `rx-banners`, `cal-dump TEXT`,
`pim WHAT`, `notes`, `note-mail N`, `note-activate ACTION CONTEXT`, `desktop`, `update-check`, `tray ...`, `cache-clear`,
`cache`, `load-images`, `discard-composer`, `render-mode N`, `chips`, `from`, `toast [TEXT]`, `rte-image`, `rte-html`.
Write-capable test commands (`rx-import`, `rx-find`, `rx-destroy`) exist only in `AUTOMATION_WRITES` builds and only act on
`<test-account>` (PORT-PLAN.md §4.4).

## Rules for scripts
- Every popup of the app is `ShouldConstrainToRootBounds` (otherwise it is outside the capture and can appear on the real screen).
- Frame times are WGC present times: on the dev machine one frame every 20.8 ms for an off-screen window; gates use that quantum.
- Own port, own profile (`BARBICAN_PROFILE=auto-<port>`), never the user's windows or entries.
