# Barbican design language (approved 2026-11-16)

Source of truth for the UI. Canvas: https://claude.ai/artifact/SvDTEckkbUTNRjVv4MqqU6 (page "Bento × Nordic").
The `.dc.html` files here are the board sources: plain HTML with inline styles; values come from
`renderVals()` (palette `t`, icon paths `I`). `{{ x }}` interpolates, `sc-for` repeats, `dc-import` embeds another board.

- One palette (dark first) + four meaning tints: rose = today, mint = waiting/done, sky = travel/work, amber = attention.
- Two styles over the same tokens, switched in Settings › Appearance › Style:
  - Bento: every panel its own tile (radius 24–28, 1px line, 12px gaps, 14px window padding), tinted summary tiles, squircle avatars/buttons.
  - Nordic: rail + folders on the ground, list/reader/context in one rounded surface split by hairlines, pills and circles, no summary tiles.
- Both have a context panel beside content (sender in mail, guests in calendar, assistant in compose).
- Icons: 1.6–1.8px stroke line icons (paths in `I`), Segoe UI text.

## In the code

- Tokens live in `ui/theme.slint` (re-exported by `ui/app.slint`): `Theme` (palette, tints, shape per style),
  `Icons` (the `I` paths, plus a few in the same family), `SIcon` (stroke icon), `Prefs.fs` (text scale).
- `Theme.style` is 0 Bento / 1 Nordic (`Theme.bento`, `Theme.nordic`); set from the `uiStyle` setting
  (Settings › Appearance › Style). Older token names (`bg`, `side`, `hover`, `reader`, …) map onto the palette.
- Hidden test channel: `ui bento|nordic dark|light [more|normal]` switches the look in memory only; `resize W H`.
- Motion: `design/MOTION.md` (rules, `Motion` / `Ease` tokens, `PopIn` / `Scrim` / `Tip`, incremental list models via `src/listsync.rs`, the frame probe `tools/motion.py`).
