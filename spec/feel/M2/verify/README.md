# Feel results: M2 verify (merged `m2`)

Run on 2026-11-17 by the M2 verify stage on the hidden AUTOMATION build (Windows, off-screen at (-12000, 200), non-activating),
port 8889, profile `auto-8889`, `BARBICAN_SAMPLE=1` (FakeJmap seed), started and stopped only through `tools/auto-run.ps1`.
Every `popup-guard` answered `open 0 closed 0` and every `focus-guard` `own-is-foreground=no`; the foreground window was the same
before and after each start.

**This time WGC delivered real frames** (the display was on): every burst below is a valid capture (no uniform frame,
first and last frame spread 13 to 35 grey levels). M1 and the M2 streams could not measure these gates.

## Cross-stream scenarios (`tools/feel-m2.py`, results in `feel-m2.json`)
Open and close are separate bursts (as `tools/scenarios.py` does), because `tools/flicker.py` compares every frame with the first and
last frame of its burst. A first run that opened and closed the overlay inside one burst flagged the open overlay itself (the palette
scrim and the dialog scrim) as BLANK/FLASH; that is a property of the measure, not a flash.

| Scenario | Looks | WGC frames | BLANK / FLASH | `rendering` probe, worst frame gap |
|---|---|---|---|---|
| ctx_menu (`menu-open MENU-MAIL 0`) | Bento, Nordic × light, dark | 13 to 15 | 0 / 0 | 8.8 to 11.2 ms |
| ctx_menu_close (`key Escape`) | all four | 2 | 0 / 0 | 7.3 to 8.2 ms |
| dialog (`dialog-open confirm`, scrim over the body) | all four | 13 to 15 | 0 / 0 | 7.5 to 21.3 ms; **27.4 and 28.6 ms** in the first Bento light and dark pass (one long frame), not seen in 3 repeats |
| dialog_close (`dialog-answer cancel`) | all four | 2 | 0 / 0 | 7.1 to 7.5 ms |
| palette_open (`palette`) | all four | 13 to 14 | 0 / 0 | 9.4 to 10.4 ms |
| palette_close (`key Escape`) | all four | 2 | 0 / 0 | 7.2 to 7.3 ms |
| delete (`act mail.delete`, the open message) | all four | 3 to 5 | 0 / 0 | 7.2 to 9.0 ms |
| undo (`act mail.undo`) | all four | 4 to 5 | 0 / 0 | 7.1 to 7.5 ms |

A close is one swap: WGC sends a frame only on a change, so the burst holds the open state and the closed state and nothing between
them. There is no in-between frame that could flash; two frames are too few to call the motion smooth, so the close rows count as
"no flash seen", not as a motion pass.

## The streams' scenarios, re-run on the merged branch
- **search** (`tools/feel-search.py`): `search_type` 58 frames, worst gap 9.7 ms (ok). `search_results` and `chip_remove`: the script
  stops the probe straight after the command (the search takes 1.7 ms and the chip re-query 3.2 ms in process), so 0 to 1 frames:
  still "unproven (capture invalid)". Fix the script (wait 400 ms before `probe-stop`, as `feel-m2.py` does) before the gate run.
- **unified** (`spec/feel/M2/unified/feel.py`, light and dark): `unified_open` 22 to 30 frames, `account_switch` 19 to 22 frames,
  0 BLANK / 0 FLASH / 0 bad, valid captures. `import_progress` light: 3 frames, 0 / 0. In dark the harness timed out: `import-sim`
  holds the automation connection until the import ends (12.7 s for 500 messages). A separate probe during a 500-message import
  saw 3086 rendered frames, worst gap 34.8 ms: the UI keeps drawing, but one gap is over 25 ms.
  Note: the script needs an absolute `--dir`; a relative one lands under the exe folder (the App resolves it), and the run then reports 0 frames.

## Not measured here
- The five actions scenarios with real input (select_range, drop_move, pane_resize, hover_strip) and every gesture: they need the
  pointer (ADR 0007 layer C), HAND TEST 2.
- The reader's banner_open: the sample seed has no banner mail (SeedM2 is off in sample runs).

## Linux (VM, Xvfb, `tools/linux.ps1 xrun -Script auto-m2`)
Menu, dialog, palette, `dnd-sim #1 folder:archive` + undo, search with chips, `unified on` and `plugins` all answered as on Windows;
`popup-guard open 0 closed 0`. Captures: `docs/milestones/M2-screens/linux-*.png`. An X11 capture taken at once missed the
just-opened menu and dialog, and showed the mail body as a white block in the dark theme (`linux-body-white-first-second.png`);
one second later both overlays were drawn and the body area was dark. The idle probe saw 4 X11 frames in the first 0.5 s after
start. Both are M9 (Linux) items: the white block is a possible white flash of WebKitGTK before the first paint.
