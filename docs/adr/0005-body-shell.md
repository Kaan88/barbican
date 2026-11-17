# ADR 0005: The mail body shell on WinUI and Uno (spike S2)

- Status: accepted (2026-11-17). Windows: go. Linux: go with the capture strategy for full-window
  overlays and two shell changes (below).
- Deciders: recorded by the M0 spikes agent (stream m0-s12); the user may overrule
- Context documents: [PORT-PLAN.md](../PORT-PLAN.md) §3.6, §3.7; Rust `mailview.rs` / `main.rs`
  `csp_with` / `dark_mail.rs` at `rust-final`

## Context
The Rust app showed mail in one persistent WebView2 "shell" document: every message is a sandboxed
`srcdoc` iframe with its own CSP, loaded hidden; the shell posts `ready:N` once it is laid out (and
after the dark pass), the host updates its native header and calls `reveal(N)` in the same turn.
Overlays over the body needed a captured image of the body (airspace). S2 asks whether the shell
works unchanged on WinUI 3, whether XAML overlays compose over WebView2 with no airspace, and what
of this works on Linux (Uno, WebKitGTK).

## What was built
- `src/Barbican.App/Assets/mail/shell.js`: the port of `SHELL_JS` (frames, ready/reveal, cancel,
  dim, clear, print, key forwarding, `ctx:X,Y` for the native context menu, `dirty`). Commands by
  `window.__bb.*` through `ExecuteScriptAsync`, or as web messages `{op,…}` (Windows only).
- `spikes/BodyShell/`: an Uno single-project harness (both TFMs) that drives the shell, captures
  the window (Windows.Graphics.Capture of an off-screen window on Windows; XGetImage of the Xvfb root
  on Linux), locates the window by a marker square and classifies every frame by colour. It writes
  `report.json` and PNGs of bad/representative frames. Pages are synthetic (solid colours, sample
  text); no mail.
- `MailDocument.cs` in the spike ports `csp_with`, the sandbox flags and the dark-mode wrapper. It is
  a spike copy: M1 moves it into Core with the Rust tests.

## Results

Windows 11 (WinAppSDK 2.6.5, WebView2 runtime of the machine), run 4, 11/11 checks, 5/5 phases:

| Probe | Result |
|---|---|
| shell up (EnsureCoreWebView2Async → `shell`) | 319 ms (286 ms in run 3) |
| `ExecuteScriptAsync`, srcdoc `ready:N` | works; 14 ms to ready |
| 3 MB page | web message (`PostWebMessageAsJson`) 83 ms; `ExecuteScriptAsync` 239 ms |
| CSP with images off (img, css, font, background, poster) | 0 requests reach a loopback beacon |
| `WebResourceRequested` filter alone (CSP allowing) | 0 reach the beacon, 5 refused |
| probe sanity (both allowing) | 5 requests seen |
| mail scripts / inline handlers / `javascript:` links | none run |
| hashed dark script in a script-enabled frame | runs; an injected unhashed script is refused |
| link click / top-level navigation | `new-window` and `navigate` blocked, 0 beacon hits, shell intact |
| switch A↔B ×24 | 0 frames neither A nor B; header and body agree in every frame (run 3: ≤ 1 frame skew, max 1 in a row) |
| dark pass on a light mail | 0 frames with white in the body |
| constrained `Popup` menu over the body | 0 frames with web content over the menu |
| `ContentDialog` (smoke) over the body | 0 punch-through; smoke covers the body |
| in-tree scrim + dialog panel | 0 punch-through |

So on Windows the capture-under-overlay workaround and its `FRESH`/`HIDE_PENDING` state machine
are **not ported** (PORT-PLAN.md §3.6 confirmed). WGC delivers frames only on change, so the frame
counts are low; "0 bad frames" means no changed frame was bad.

Debian 13 VM (Uno 6.7.424 X11, WebKitGTK 2.54 / 4.1, Xvfb), 8/9 checks, 4/7 phases:

| Probe | Result |
|---|---|
| shell up | 112–127 ms |
| `ExecuteScriptAsync`, srcdoc `ready:N` | works; 10–16 ms; 3 MB page 36–38 ms |
| page → host messages | work, but through `window.webkit.messageHandlers.unoWebView`, not `chrome.webview` |
| host → page web messages | **not available**: `PostWebMessageAsJson`/`AsString` are not implemented in Uno 6.7 |
| CSP with images off | 0 requests reach the beacon (no `WebResourceRequested` on Linux: CSP is the only guard) |
| script blocking, hashed dark script | as on Windows |
| link click (target `_blank`) | **nothing happens**: no `NewWindowRequested`, no request (the link is lost, not routed) |
| top-level navigation from the shell | **the request reaches the server** (1 beacon hit) although the shell stays loaded |
| switch A↔B ×24 | 1 sample neither A nor B (the first one, still the previous probe page); header and body disagree in 19–21 of ~540 samples, max 2 in a row (X11 polling every 8 ms, so ≲ 2 frames at 60 Hz) |
| dark pass | 0 white frames |
| constrained `Popup` / `MenuFlyout` over the body | **0 punch-through**: Uno clips the native WebKit window under in-window popups |
| `ContentDialog` / full-window scrim | no punch-through, but Uno **hides the WebKit window** under a full-window overlay: the body area shows the empty tile, not the dimmed message, in every overlay frame |
| capture stand-in (XGetImage → `Image`, WebView collapsed) + `MenuFlyout` | pass, the message stays visible |

## Decision
1. **Windows: go.** Keep the shell design 1:1. Use `ExecuteScriptAsync` for commands and web
   messages for large pages (no 2 MB limit). Keep `AddWebResourceRequestedFilter` as defence in depth.
2. **Menus over the body (Windows):** `MenuFlyout` on WinAppSDK 2.6.5 is a **windowed** popup
   (`IsConstrainedToRootBounds=false` even with `ShouldConstrainToRootBounds=true`). It composes over
   WebView2 like any window, but it is clamped onto the real monitor when the app window is off-screen,
   which matters for hidden automation (see the FABLE-REVIEW incident). The context menu host in §3.7
   should be a constrained `Popup`-based menu (verified in-window, 0 punch-through), or automation
   must keep the window on a virtual display. To be settled in S4/M2.
3. **Linux: go, with the capture strategy for full-window overlays.** `IBodyOverlayStrategy.Linux`
   applies to dialogs and scrims (the native view is hidden there, so a stand-in image keeps the
   message visible). Menus and small popups need no stand-in on Uno 6.7.
4. **Linux shell changes:**
   - `post()` uses `chrome.webview` when present, otherwise `webkit.messageHandlers.unoWebView`
     (done in `shell.js`).
   - All host → page traffic goes through `ExecuteScriptAsync` on Linux (no web messages); the 3 MB
     probe shows it is fast enough.
   - Links must be routed by the shell itself on Linux (a click handler posting `link:URL`), because
     neither `NewWindowRequested` nor a navigation event fires for `target=_blank`.
   - Top-level navigation of the shell must never be triggered (the frames have no
     `allow-top-navigation`, so only our own code could); M1 adds a test that no shell code assigns
     `location`.
5. **Fallback not taken:** plain-text mode on Linux (§3.6) is not needed: `ExecuteScriptAsync` and
   page → host messages both work.

## VM packages
`tools/linux.ps1 setup-ui` installs, only when missing: `xvfb`, `xauth`, `libwebkit2gtk-4.1-0`,
`libgtk-3-0`, `libfontconfig1`, `fonts-dejavu-core` (with their dependencies, GStreamer among them).
Uno 6.7 loads unversioned library names (`libwebkit2gtk-4.1.so`, `libgdk-3.so`, …) that only the
`-dev` packages provide, and falls back to WebKitGTK 4.0 (gone from Debian 13). `xrun` puts private
symlinks in `~/.barbican-lib` on `LD_LIBRARY_PATH` instead. The packaged app (M9) needs the same:
a `.deb` dependency on the `-dev` names or a launcher that sets the path.

## Open points
- Linux header/body skew up to 2 polled samples: the header (Uno, Skia) and the body (separate X
  window) do not present atomically. Measure on a real compositor before M3 (feel).
- `NavigationStarting` on Uno did not stop the request of a script-initiated top-level navigation.
- The Windows `MenuFlyout` behaviour over an on-screen window was not captured (it ran only off-screen,
  where it escapes the window); `BARBICAN_SPIKE_WINDOWED_MENUS=1` runs that phase on a throwaway VM.
