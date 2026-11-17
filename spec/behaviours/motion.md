# Behaviours: motion and feel (B-MOT)

Area: motion settings, reduce motion, rapid key repeat, transitions, the frame probe and the
flicker rules. The rules are copied from `design/MOTION.md` (Rust, `rust-final`); this file
states them as behaviours. Measured numbers live in `spec/feel/rust-baseline/` (the bar to beat,
PORT-PLAN §4.5). Those numbers are not restated here except where a commit cites them.
Rust sources: `desktop/src/motion.rs`, `design/MOTION.md`, `tools/motion.py`, `tools/flicker.py`,
`tools/scenarios.py`, `tools/motion-layout.json`, `ui/theme.slint` (`Motion`, `Ease` globals).

| ID | Rule | Source (Rust) | Bulwark ref | C# status |
|---|---|---|---|---|
| B-MOT-001 | `Motion.reduce` comes from Settings > Accessibility > Reduce motion. On "System" it comes from Windows' animation-effects setting, re-read on window activation. | motion.rs header (lines 1-4); test `setting_wins_over_system` (:81) | accessibility | ported (m1 mailcore: MotionSettings, MotionState; the views bind in shell-ui) |
| B-MOT-002 | `Motion.rapid` is on while the list selection moves faster than an animation (key repeat). It is set on a user action, not per frame. | motion.rs:18 `RAPID` (150 ms); header | n/a | ported (m1 mailcore: MotionSettings, MotionState; the views bind in shell-ui) |
| B-MOT-003 | A single-shot timer clears `rapid` after 180 ms of calm. | motion.rs:20 `CALM_AFTER` | n/a | ported (m1 mailcore: MotionSettings, MotionState; the views bind in shell-ui) |
| B-MOT-004 | No per-frame work in the motion module. | motion.rs header (lines 3-4) | n/a | ported (m1 mailcore: MotionSettings, MotionState; the views bind in shell-ui) |
| B-MOT-005 | Durations (design/MOTION.md tokens): fast 100 ms, list, enter 150 ms, exit 90 ms, view 110 ms, tip delay, pop scale, rise. | commit ea190d4 (ui/theme.slint `Motion`) | n/a | ported (m1 shell-ui: `MotionScale` from the generated `Tokens.Motion`; MotionScaleTests) |
| B-MOT-006 | Easing: enter cubic-bezier(0.2, 0, 0, 1); exit cubic-bezier(0.4, 0, 1, 1). | commit ea190d4 (`Ease`) | n/a | ported (m1 shell-ui: `MotionScale.EnterCurve`/`ExitCurve` from `Tokens.Ease`) |
| B-MOT-007 | With reduce motion on, every duration is multiplied by 0. | commit ea190d4 | accessibility | ported (m1 mailcore: MotionSettings, MotionState; the views bind in shell-ui) |
| B-MOT-008 | Nothing gates input. State applies at once; animations retarget. | commit ea190d4 | n/a | ported (m1: keys go to `KeyRouter` at once; the list and reader apply state in one UI turn and never wait for an animation; judged in HAND TEST 1) |
| B-MOT-009 | Late-content fades are instant while keys repeat (`Motion.late`). | commit 358bb57 | n/a | ported (m1 mailcore: MotionSettings, MotionState; the views bind in shell-ui) |
| B-MOT-010 | Popups (context menu, flyouts, menu panels, account switcher, dialogs, calendar preview) pop in; they are removed instantly. | commit ea190d4 | n/a | pending (M2) |
| B-MOT-011 | Tooltips wait and then fade in; the toast slides in and fades out. | commit ea190d4 | n/a | pending (M2) |
| B-MOT-012 | Shared buttons and nav rows give hover, press and selection feedback; the focus outline is shown. | commit ea190d4 | n/a | partial (m1 shell-ui: hover, pressed and selected states in the XAML styles; hover cannot be injected (ADR 0007): HAND TEST 1) |
| B-MOT-013 | A start-up warm-up runs so the first popup of a session is not late. | commit ea190d4 | n/a | partial (m1 body: the body shell is warm from window start); popup warm-up deferred(M2) with the menus |
| B-MOT-014 | Mail reader: a message on screen stays whole until the new one is laid out; the header changes with the body; a 2 px progress bar appears only after 150 ms. | commits b72743d, 0530d4a; design/MOTION.md | n/a | ported (m1 body: `MessageLoader`, `BodyShell.Ready`, B-SYNC-022..025; spec/feel/M1) |
| B-MOT-015 | Opening a message in an empty reader fills the header from the list row at once. | commit b72743d | n/a | ported (m1 body: header prefilled from the row, B-SYNC-027) |
| B-MOT-016 | The WebView is parked outside the window, not hidden, so it keeps painting. Overlays first show a captured image of the body. | commit b72743d | n/a | n/a on Windows (ADR 0005: XAML composes over WebView2); Linux `X11BodyOverlay` written, unwired and never run: deferred(M2) with the dialogs |
| B-MOT-017 | The body host window is painted in the page colour, so no white strip shows when the view moves. | commit 9dc15d8 | n/a | ported (m1 body: `BodyShell` sets `DefaultBackgroundColor` to the reader colour) |
| B-MOT-018 | The email view background follows the theme, so opening a message does not flash white. | commit 7f2d706 | emailAlwaysLightMode | ported (m1 body: the page background follows the reader theme; theme re-render 12 to 15 ms, spec/feel/M1) |
| B-MOT-019 | The composer fades in; popovers pop in; attachment chips and the assistant fade in. | commit 38bf81f | n/a | pending (M3) |
| B-MOT-020 | Admin and settings: in-place models; the settings save is async; views fade in; nav, dialog and login have feedback. | commit b3c8cdf | n/a | pending (M6/M7) |
| B-MOT-021 | Calendar, contacts and files use incremental models; the view fades in. | commit 464e518 | n/a | pending (M4/M5) |
| B-MOT-022 | Mail thread strip, context menu and glance lists update in place. | commit 1a1eaa5 | n/a | pending (M2) |
| B-MOT-023 | Hover actions are an overlay on the row; they do not shift text; the hover survives moving onto them. | commit 7a60ae8 | hover actions | pending (M2) |
| B-MOT-024 | The frame probe reads RedrawRequested timings through the automation channel (probe-start, probe-stop). | commit ea190d4; commit a675246 | n/a | ported (m1 F1: `probe-start wgc|rendering` / `probe-stop` in `src/Barbican.Automation`, ADR 0007) |
| B-MOT-025 | Flicker scenarios: open_first, switch_key, switch_click, switch_back, thread_switch, close_reader. A frame with a blank or flashed header or body is a failure. | tools/scenarios.py; tools/flicker.py (header note); PORT-PLAN §5 M1 | n/a | ported (m1 body: spec/feel/M1/body_feel.py; frame evidence weak, see docs/milestones/M1.md "Feel") |
| B-MOT-026 | Reference results (the bar): `spec/feel/rust-baseline/flicker` and `spec/feel/rust-baseline/motion`. The C# port must be no worse. | PORT-PLAN §4.5; commit 301b129, 9dc15d8 | n/a | partial (m1 verify: in-process timings are within the bar; the frame gates cannot be measured on the hidden window, docs/milestones/M1.md "Feel") |
| B-MOT-027 | Cited Rust results: j or click switches in 13 to 26 ms; open again in 16 ms; open uncached in 123 ms; 0 bad frames except the known Settings identities fill-in (dark, 3 frames). | commit 0530d4a (probe results) | n/a | ported (reference numbers, compared in docs/milestones/M1.md "Feel"); the dark-settings flicker is open: L-053 |
| B-MOT-028 | The motion layout table (motion-layout.json) is keyed by AutomationId where possible. | commit e7a74c9; PORT-PLAN §2.2 | n/a | partial (m1 shell-ui: tools/layout/m1-*.json regions, scenarios select by AutomationId with `uia-select`); the Rust `motion.py` layout: deferred(M2) |
| B-MOT-029 | Overlays and async panels fade in; the thread strip, glance lists and list rows do not rebuild. | commits 1a1eaa5, ea190d4 | n/a | pending (M2) |
| B-MOT-030 | A late background refresh does not insert a Loading row above the list. | commit b72743d | n/a | ported (m1 mailcore: old rows stay until the new page is ready, no Loading row; MessageListViewModelTests) |
