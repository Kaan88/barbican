# Settings

Source: `ui/app.slint` 5770-5997 (navigation and page), `ui/settings-schema.json` (the rows),
`src/settings.rs`. Units: README.md.

## Structure
Settings replaces the app content (rail stays). A navigation column on the left (tabs from the
schema, 21 tabs, grouped with separators) and a scrolling page on the right. Page content is a
single column centred on the left edge of the page.

| Item | Bento | Nordic | Source |
|---|---|---|---|
| container padding | top/bottom/right `pad` 14 | top/bottom/right 14 | app.slint:5770-5772 |
| navigation container | Tile (radius 24, 1 px border), width 230, clipped | flat (on the ground), width 230 | 5776-5778 |
| nav top padding | 14 | 22 | 5780 |
| "Back to mail" / title row | height 34, inner width `parent - 20`, radius 12 / pill, left padding 20, gap 8; title letter-spacing -0.3; "Settings" heading 19 x fs / 800 (Nordic 24); hover background `tile2` (Nordic `tile`) | | 5783-5806 |
| search field | height 38 (large 44), radius 14, 1 px border (focus `max(1, ring)`), padding-left 12, right 10, gap 8; outer padding 10 | height 40, pill, padding-left 16 | 5808-5820 |
| tab group header | height 32; a separator entry ("-") is 13 high with a 1 px line at y 6, inset 20 | | 5844-5845 |
| tab row | height 34 x fs (Nordic 36 x fs; large 44), width `parent - 24`, radius 13 / pill; left padding `10 + indent x 16`, gap 10 | | 5849-5858 |
| bottom fade | 18 px gradient so a cut-off last item reads as "more below" | | 5869 |
| nav list padding | 12 (left 20, right 16) | | 5873-5875 |

## Page
| Item | Value | Source |
|---|---|---|
| page header | height 64 (Nordic 72); title letter-spacing -0.4; hairline underneath, inset 14 in Bento, full in Nordic | 5891-5902 |
| scroll area bottom padding | 40 | 5907 |
| content width | `min(560, window - 300)` | 5912 |
| section row | top padding 26, bottom 4, spacing 4; title 19 x fs / 700, letter-spacing -0.2, radius 8 hit area; description 13 x fs dim | 5913-5919 |
| normal row | top padding 12, spacing 10; label 13.5 x fs / 600; description 12 x fs dim; 16 gap between text and control; 3 between label and description | 5913-5937 |
| label width | `content - 52` for a toggle, `- 256` for a value, `- 236` for other controls | 5939 |
| control widths | button 220, select / text field 220, read-only value 240 (right aligned, elided) | 5946-5959 |
| Switch | 36 x 20, radius 10, knob 16 x 16 radius 8 at x 2 / `w - 18` | app.slint:291-311 |
| textarea | height 90, radius 8 | 5922-5927 |
| row separator | 1 px hairline under every non-section row | 5966 |
| text on a settings row | fonts follow `Prefs.fs` | |

## Confirm dialog (shared by settings and others)
Popup surface, radius `r-tile` 24 (Nordic 28), 1 px border, width `min(400, window - 40)`, padding 22
(Nordic 28), spacing 14, buttons right-aligned with spacing 8 (app.slint:5979-5991). Same shell as
the admin action dialog.

## Settings pages with their own layouts
Filters (`ui/pim-filters.slint`): rule row radius 14, padding 10, spacing 8; on/off switch 36 x 20
(knob 16); condition combos 140 / 130 / 160 wide, action combo 190; inline "Delete?" confirm button
height 26. Identities, templates, vacation and the other form pages use the schema rows above.
