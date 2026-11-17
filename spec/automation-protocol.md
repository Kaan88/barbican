# Automation protocol (Layer A)

The hidden in-app automation server of AUTOMATION builds (the `Automation` build configuration:
`tools/build.ps1 build -Configuration Automation -Head win`; `src/Barbican.Automation`), loopback only,
`BARBICAN_AUTOMATION_PORT` (default 8792), profile `BARBICAN_PROFILE` (default `auto-<port>`). Launch on Windows with
`tools/auto-run.ps1 start` (SW_HIDE, own port and profile, stops only its own process; `-PrivateDesktop` for
popup probes); on Linux with `tools/linux.ps1 xrun -Configuration Automation -Script auto` (private Xvfb only). One command per line, one reply line. The command set is the one of the
Rust app (`src/automation.rs` header at tag `rust-final`), so `tools/auto.py`, `tools/motion.py`, `tools/flicker.py` and
`tools/scenarios.py` run with one change (they authenticate first, below). Decisions and measurements: [ADR 0007](../docs/adr/0007-hidden-automation.md).
Reference implementation of the transport, click, capture and probe commands: `spikes/s4-automation/`.

Status column: **spike** = implemented and measured in `spikes/s4-automation`; **F1** = ported to
`src/Barbican.Automation` by M1 stage F1 (every spike command below is, except where noted); **M1** = to port with the
real app; **n/a** = cannot be done in-process (Layer C); **drop** = Rust-only. On Linux (Uno desktop head under the
VM's Xvfb) the UIA commands fall back to peers and `wshot`/`probe`/`burst` use the Xvfb root picture.

Coordinates are logical px of the window's client area. Replies: `ok`, a value, or `error ...` / `unknown command ...`.

**Authentication (Fable review 1 F-08).** The server opens its port only when the run has a token,
`BARBICAN_AUTOMATION_TOKEN` (at least 16 characters, no spaces; `tools/auto-run.ps1 start` makes one per run and writes it
to `artifacts/auto/<port>/token`; `tools/linux-remote.sh auto` makes its own). The first line of every connection must be
`auth <token>`, answered `ok auth`; any other first line gets `error auth required` and the connection is closed, and a
later line that looks like HTTP (a request line or a `Name: value` header) closes it without a reply. Python clients
connect through `tools/autoconn.py` (token from the variable, else the token file). The popup guard runs whether or not
the port opens.

## Input (no real input is ever produced)
| Command | Meaning in C# | Status |
|---|---|---|
| `click X Y`, `dclick X Y` | smallest UIA element under the point with Invoke, Toggle, SelectionItem or ExpandCollapse, pattern call (a text box is focused through XAML; a point in the web area clicks the DOM element) | spike |
| `rclick X Y` | `ContextFlyout.ShowAt` of the element under the point | spike |
| `peer-click X Y` | in-process AutomationPeer path (needed for XAML drawn over a WebView2, invisible to UIA clients) | spike |
| `scroll X Y DY` | `ScrollViewer.ChangeView` under the point (DY in px, positive = down) | spike |
| `type TEXT` | appends to the Value pattern of the XAML-focused element | spike |
| `key CHORD` | `KeyRouter.Handle` (Enter, Escape, Tab, Up, Down, Ctrl+N in the spike; the full table with the app). F1 replies `error no key router yet`; the stream that owns the KeyRouter registers `key` as an `IAutomationCommand` | spike, M1 |
| `move X Y` | accepted as a no-op (no hover injection in-process) | spike (no-op) |
| `vstate ID STATE` | `VisualStateManager.GoToState` on the control with that AutomationId (`PointerOver`, `Normal`, `Pressed`, ...): the hover stand-in for screenshots; no pointer moves | M1 look |
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
| `status` | spike-only status text | spike (drop) |
| `focus-guard` | `fg=<hwnd> own=<hwnd> own-is-foreground=yes\|no cursor=X,Y`: the no-focus proof (Windows) | F1 |
| `popup-guard` | `open N closed M[: types]`: the fail-closed popup guard's view (see Rules) | F1 |
| `popup-probe KIND [free]` | opens one popup control in code (menuflyout, flyout, commandbarflyout, popup, teachingtip, combobox, tooltip, autosuggest, contentdialog), replies `probe KIND constrained=yes\|no\|none guard-closed=N` or the guard's error. **Only on a private desktop** (`tools/auto-run.ps1 start -PrivateDesktop`) | F1 |

## Capture and measurement
| Command | Meaning | Status |
|---|---|---|
| `shot PATH.png` | XAML only (`RenderTargetBitmap`); the WebView area is empty | spike |
| `wshot PATH.png` | whole window incl. WebView2 and in-window flyouts, Windows.Graphics.Capture, client area | spike |
| `wshot-pw PATH.png` | `PrintWindow`; **black for WinUI 3**, kept only to document that it is not a fallback | spike (drop in M1) |
| `burst MS EVERY DIR [STEP]`, `burst-status` | WGC frames of the window into DIR (`f####_<ms>.png`, `events.txt`, `meta.json`); `tools/flicker.py` reads it | spike |
| `probe-start [wgc\|rendering\|x11]`, `probe-stop` | `{"frames":[[start,end],...],"events":[[t,cmd],...],"source":...}`, ms since start; `tools/motion.py` reads it. `x11` (Linux default) polls the window's Xvfb rectangle every 8 ms and records changed pictures | spike, F1 (x11) |
| `size`, `resize W H`, `title` | physical size and scale (`W H scale S`), logical resize, window title (with unread badge in the app) | spike |
| `bounds ID` | `X Y W H` of the element with that AutomationId, logical px relative to the window content (`tools/lookdiff.py`) | M1 look |
| `motion reduce\|normal\|rapid\|calm` | in-memory motion settings | spike (flag only) |
| `theme dark\|light`, `ui bento\|nordic dark\|light [more\|normal]` | in-memory style, theme, contrast | spike (theme), M1 (ui) |
| `anim comp\|sb` | spike-only: a 150 ms composition-thread or storyboard animation | spike |
| `wv-eval JS` | runs JS in the web view and returns its result directly (the Rust `wv-result` poll is not needed) | spike |
| `close` | replies `ok`, then closes the window 200 ms later | spike |

## To port with the real app (M1+), same names and meaning as the Rust header
`pick PATH[|PATH...]`, `save PATH`/`savepick PATH` (queued answers for the file pickers), `rx-banners`, `cal-dump TEXT`,
`pim WHAT`, `notes`, `note-mail N`, `note-activate ACTION CONTEXT`, `desktop`, `update-check`, `tray ...`, `cache-clear`,
`cache`, `load-images`, `discard-composer`, `render-mode N`, `chips`, `from`, `toast [TEXT]`, `rte-image`, `rte-html`.
Write-capable test commands (`rx-import`, `rx-find`, `rx-destroy`) exist only in `AUTOMATION_WRITES` builds and only act on
`<test-account>` (PORT-PLAN.md §4.4).

## M2 commands (planned; each owner fills the meaning and appends the row in the commit that adds the command, M2 plan 4.7)
| Command | Owner | Meaning (to be written) |
|---|---|---|
| `dnd-sim SRC DST`, `hover-sim ROW\|off`, `pane-set SIDE LIST`, `select ROWID [shift\|ctrl]`, `toast-last` | actions | planned |
| `menu-open MENU TARGET`, `menu-pick LABEL`, `menu-items`, `palette TEXT`, `palette-pick N`, `dialog-state`, `key CHORD` | menus | planned |
| `rx-banners`, `banner-act ID ACTION`, `source-text`, `avatar-state EMAIL` | reader | planned |
| `rx-import TOKEN`, `rx-find TOKEN`, `rx-destroy TOKEN ID...` | reader | `AUTOMATION_WRITES` builds only (M2-Q2, a proposal pending the user's OK). `rx-import` imports one synthetic message (subject `Barbican write test [rx:TOKEN]`, header `X-Barbican-Write-Test: TOKEN`, example.org addresses) into the Inbox: `ok EMAILID`. `rx-find`: `ok ID...` or `ok -`. `rx-destroy`: destroys only when every id carries TOKEN on the server, else nothing: `ok destroyed N`. Each first checks, re-reading both `.local` lists: an automation profile, the login on the writable list and not on the read-only list; else `error refused: ...`. TOKEN is 8-40 letters and digits |
| `search TEXT`, `suggestions`, `chips` | search | planned |
| `unified on\|off`, `import-sim PATH MAILBOX` (FakeJmap only), `plugins` | unified | planned |

## Rules for scripts
- Every popup of the app is `ShouldConstrainToRootBounds` (otherwise it is outside the capture and can appear on the real screen).
  `tests/Barbican.Automation.Tests/PopupLintTests.cs` enforces it on the App's XAML with a deny list
  (`src/Barbican.Automation/Protocol/PopupControls.cs`: CommandBarFlyout, ComboBox, ToolTip, AutoSuggestBox, pickers, ...).
- The popup guard (AUTOMATION builds) sweeps the open popups every 10 ms and around every command, closes each one with
  `IsConstrainedToRootBounds == false` and makes the running (or next) command reply `error unconstrained-popup <type>`.
  It reacts; it does not prevent the first frame of such a popup (docs/adr/0007 "M1 F1").
- `Barbican --smoke REPORT.json` (every configuration, Release included) runs hidden, waits for the body shell's `shell`
  message and writes `{"schema":1,"ok",...,"processToFirstFrameMs","processToShellReadyMs","exitCode"}`; exit 0 = ok,
  2 = cannot run hidden here, 3 = no shell within 60 s, 4 = shell failed.
- Frame times are WGC present times: on the dev machine one frame every 20.8 ms for an off-screen window; gates use that quantum.
- Own port, own profile (`BARBICAN_PROFILE=auto-<port>`), never the user's windows or entries.
