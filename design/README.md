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

## Merge note: composer extras (branch p2-composer)

The recipient chips, the schedule-send dialog and the Scheduled banner (`ui/recipients.slint`,
`ui/compose-extras.slint`) read colours from a separate `CTheme` global (`ui/ctheme.slint`) so
they never import app.slint. `src/ctheme.rs::sync` copies the neutral `Theme` tokens into it;
the rose/mint/sky/amber tints, `shadow` and `scrim` are defined in `ctheme.slint` itself.
When the design branches add Theme tokens or the Bento/Nordic style switch, either extend
`ctheme::sync` to copy them, or (preferred, after the merge) replace `CTheme` with `Theme` in
those two files and delete `ctheme.slint` / `ctheme.rs`.
