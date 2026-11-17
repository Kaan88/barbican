# ADR 0007: Hidden automation for the C# app (spike S4)

- Status: accepted (2026-11-17), measured on Windows 11 build 26300, one 144 Hz monitor, WinAppSDK 2.6.5, .NET 10
- Deciders: M0 spike S4 (agent `port-s45`); review items in `docs/FABLE-REVIEW.md`
- Plan sections: PORT-PLAN.md §4.4 (layers A to C), §4.5 (feel), §3.8 table row "Window capture"
- Code: `spikes/s4-automation/` (outside `Barbican.slnx`, no strictness). Build with
  `tools/build.ps1 build -Project spikes/s4-automation/S4.csproj`, run with `spikes/s4-automation/run.ps1 start`.

## Question
Can the C# app be driven and measured the way the Rust app was (off-screen window, line protocol on
loopback, `tools/auto.py`, `motion.py`, `flicker.py` unchanged) without ever touching the user's mouse,
keyboard, focus or screen? Which capture and probe mechanisms give the numbers the feel gates need?

## Decision (what the app's AUTOMATION build does)
1. **Window.** Create the WinUI `Window` as usual, never call `Activate()`. Before the first show:
   `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` (no taskbar button, not in Alt+Tab),
   `OverlappedPresenter.SetBorderAndTitleBar(false, false)`, `AppWindow.MoveAndResize` to (-12000, 200),
   then `ShowWindow(SW_SHOWNOACTIVATE)` (twice if still invisible: a launcher's SW_HIDE overrides the first
   call). Measured: the foreground window was never ours in 2 runs of about 60 samples at 40 ms while the harness
   clicked, typed, opened flyouts and captured (`spikes/s4-automation/check-focus.ps1`). DWM keeps
   composing a window parked 12000 px off every monitor; WebView2 renders and is captured.
2. **Server.** `TcpListener` on `127.0.0.1:$BARBICAN_AUTOMATION_PORT`, one line in, one line out, the
   `automation.rs` protocol (`spec/automation-protocol.md`). Commands run on the UI dispatcher, except the
   UIA-client commands, which must run on the server thread (see 3).
3. **Clicks go through UI Automation patterns, via a UIA client in the same process.**
   `click X Y` (logical px, client origin) finds the smallest UIA element under the point that supports
   Invoke, Toggle, SelectionItem or ExpandCollapse and calls the pattern. 30 to 100 ms per click (the element
   search dominates). Reached through `FlaUI.UIA3` here; no `FlaUI.Core.Input` type is referenced. A UIA client call
   from the UI thread into the app's own providers would deadlock, so these commands run on the server thread.
   - Why not in-process `AutomationPeer`s (the plan's first idea): `FrameworkElementAutomationPeer.CreatePeerForElement`
     yields peers without patterns for `ListViewItem` containers and `TextBox` (measured: `ListViewItemAutomationPeer`
     exposes none, `TextBoxAutomationPeer` no Value pattern), because the real peers are created by the parent
     peer's `GetChildren` for UIA. Peers are fine for `Button`, `CheckBox`, `ToggleButton`.
   - Limits found: (a) XAML elements drawn **over a WebView2** are missing from the UIA client tree, so they cannot
     be clicked by coordinates through UIA; the harness keeps the in-process path for them
     (`peer-click X Y`, `invoke ID`, which work: verified on a button over the web area); (b) a click inside the web
     area is done through the DOM (`elementFromPoint(..).click()`) on the in-process path, and web elements are also
     exposed by UIA (Invoke worked on a `<button>`); (c) text boxes have no click pattern: a click on one focuses it
     through XAML (`Focus(Programmatic)`), and `type TEXT` appends to the XAML-focused element's Value pattern (worked:
     caret and text visible in the capture); (d) hover, drag, IME and real pointer routing cannot be injected: `move`
     is accepted as a no-op and `drag` is not implemented (they belong to Layer C, real input in a throwaway VM).
   - Right click: the `ContextFlyout` of the element under the point is shown with `ShowAt` (there is no UIA pattern
     for context menus).
   - Keys: one `KeyRouter.Handle(chord)` that real `KeyDown` handlers and the automation channel both call
     (spike: Up, Down, Tab, Enter, Escape, Ctrl+N). `UIElement.TryInvokeKeyboardAccelerator` cannot be used: its
     argument type has no public constructor.
4. **Every popup is `ShouldConstrainToRootBounds="True"`.** A `MenuFlyout`, `Flyout`, `ComboBox` drop-down or
   `ToolTip` with the default settings opens in its own top-level `Microsoft.UI.Content.PopupWindowSiteBridge`
   HWND. Measured consequences: (a) it is **not part of the app window's capture**; (b) the OS keeps it on a
   monitor: it appeared at about x = -10 on the user's real screen edge, a visible strip, although the app window
   was at -12000. With `ShouldConstrainToRootBounds="True"` the flyout is drawn inside the window (also over the
   WebView2 area), the capture contains it, no extra HWND exists (checked with EnumWindows), and its items are in
   the UIA tree. This is a hard rule for every flyout in `Barbican.App` (analyzer or XAML lint in M1). Unconstrained
   popups are an automation hazard (they leak onto the user's screen), not only a capture one.
5. **Capture = Windows.Graphics.Capture only.** `GraphicsCaptureItem.CreateForWindow` via the
   `IGraphicsCaptureItemInterop` COM interface, a free-threaded `Direct3D11CaptureFramePool`, device from Win2D's
   `CanvasDevice.GetSharedDevice()` (Win2D is only used to copy the surface to bytes and write the PNG, so it is an
   AUTOMATION-build dependency, not an app one). The frame is the window's DWM frame; it is cropped to the client
   area with `DwmGetWindowAttribute(EXTENDED_FRAME_BOUNDS)` vs `ClientToScreen`, so coordinates match logical px.
   - Includes the WebView2 body and in-window flyouts (`docs/adr/img/s4-wgc-offscreen-webview.png`,
     `s4-wgc-flyout-over-webview.png`; both show synthetic spike data only).
   - `wshot` takes about 27 ms. `shot` (RenderTargetBitmap, XAML only) takes about 27 ms and shows the WebView area
     empty, as expected.
   - **`PrintWindow(PW_RENDERFULLCONTENT)`, the Rust fallback, returns an all-black image for a WinUI 3 window**
     (composition island; mean pixel value 0.0 in three captures). There is no GDI fallback. The chain is WGC, then
     `shot` for the XAML part only.
   - The spike targets `net10.0-windows10.0.26100.0` for `GraphicsCaptureSession.MinUpdateInterval` and
     `IsBorderRequired` (set best-effort; see "Open issues" for the cadence). The App's TFM is S1's decision.
6. **Burst.** `burst MS EVERY DIR [STEP]` records every composed frame of the window (`EVERY` is a minimum spacing,
   0 = all) into `f####_<ms>.png`, `events.txt`, `meta.json` (`scale`, `step`, `body` = the WebView bounds,
   `size`). `tools/flicker.py` ran on it unchanged (`frames=22 bad=0 blank=0 flash=0`); the Bento/Nordic `layout`
   key and the real region rectangles belong to the real layout (an M1 task). Frames only exist when something
   changes (an idle window yields 0 frames), which fits the "0 frames while idle" gate better than the Rust
   fixed-interval polling.
7. **Probe.** `probe-start [wgc|rendering]` / `probe-stop` return `{"frames":[[start,end]],"events":[[t,cmd]],"source":...}`
   (ms since `probe-start`), the format `tools/motion.py` reads. `motion.py --layout spikes/s4-automation/motion-layout.json`
   ran five scenarios against the spike unchanged (it only needed a layout file).
   - `wgc` (default): one entry per composed frame; `start` = `SystemRelativeTime` (the present time DWM stamps),
     `end` = max(start, arrival). Sees composition-thread animations (`Visual.StartAnimation`), CSS animations in
     the WebView and XAML storyboards. Idle: 0 frames. Does not perturb the app.
   - `rendering`: `CompositionTarget.Rendering` plus a low-priority dispatcher callback. **Attaching the handler makes
     the UI thread tick at the display rate even when nothing changes** (measured 288 frames in 2 s idle at 144 Hz)
     and it does not see composition-thread animations as separate frames. Use only to measure UI-thread render
     cost, never for the idle gate.
   - Event timestamps for UIA clicks are taken at the pattern call, not before the element search.
   - `every MS N CMD` keeps the `A+Bi` token expansion (a pointer sweep) and runs `click` through UIA.
   - Measured on the spike (click to first frame, includes the capture quantisation below): median 19 to 25 ms,
     max 33 ms; arrow-key repeat at 33 ms: median 3.6 ms, max 24 ms; a 150 ms animation is 7 to 9 frames.

## Consequences
- AUTOMATION builds (`BARBICAN_AUTOMATION=1`, separate configuration, `#if AUTOMATION`) carry the server, the UIA
  client (FlaUI.UIA3), Win2D capture and the probe. Release builds contain none of it, so `FlaUI.Core.Input` is not
  shipped either (and the banned-API list covers it for the test projects).
- Layer B (UIA pattern tests) and Layer A's click share one UIA client implementation.
- Every interactive control needs an `AutomationId` (text boxes need one to be focusable by automation) and every
  flyout `ShouldConstrainToRootBounds`.
- The feel gates read the `wgc` probe and treat about 21 ms as the frame quantum on this machine (below).

## Open issues (measured, not solved)
- **WGC cadence is 20.8 ms (48 fps), not 6.9 ms**, for the off-screen window on a 144 Hz display, with or without
  `MinUpdateInterval = 1 ms`. The `rendering` probe ticks at 6.9 ms. A 25 ms frame-gap gate still detects a dropped
  frame (the next gap is 41.6 ms) but input-to-frame latency has a 21 ms quantum, against the plan's "median
  <= 16 ms". Unknown whether this is DWM throttling of windows on no monitor, WGC pacing, or the panel. To test in
  M1: a 60 Hz or virtual display, and WGC on an on-screen window (the user's machine only, hidden or not).
- WebView2 content is composed by another process: WGC delivered it (the CSS animation produced frames), but that every
  WebView frame is captured under load is not proven (spike S2 measures it).
- The first smoke run (before every popup was constrained) briefly drew a flyout strip at the left edge of the user's
  screen; no input and no focus change was involved. Fixed by item 4.
- Not implemented in the spike: `drag`, `wv-result`, `pick`/`save`, `ui`, `rx-*`, `cal-dump`, `pim`, `notes`
  (they depend on the real app); the spike has a throwaway layout.

## M0 review (stream m0-review, at the merge of all M0 branches)
- Renumbered from 0004, because spike S1 had taken that number; the file and its references are updated.
- **Contradiction with ADR 0005 on `MenuFlyout`:** see ADR 0005, "M0 review". The decision there (a
  `Popup` host, XAML-declared constrained flyouts only, a fail-closed check in the AUTOMATION build)
  replaces "every flyout `ShouldConstrainToRootBounds`" as the rule; that rule still holds as a minimum.
- **"WGC on an on-screen window (the user's machine only)"** in the open issues would draw on the user's
  real screen, which an agent may not do. It needs the user's OK as a [HAND TEST], or a virtual display
  (an OS change, which is also the user's call). Until then the feel gates use the measured ~21 ms quantum.
- **Release builds must not carry FlaUI, the Win2D capture or the server.** First M1 automation task:
  keep them in a separate project referenced only under the AUTOMATION configuration, and make
  `tools/build.ps1 publish` check that the Release output has no `FlaUI*.dll`.
- **Go/fallback summary:** go for the off-screen non-activating window, the loopback line protocol,
  UIA-pattern clicks, WGC capture and the `wgc` frame probe. Fallbacks: in-process peers and
  `invoke ID` for elements over the WebView2 (already in use). Drag, hover and IME stay [HAND TEST]
  until a throwaway-VM layer exists.
