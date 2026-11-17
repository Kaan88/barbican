# Admin (Stalwart dashboard and list pages)

Source: `ui/app.slint` 5398-5700 (nav, list pages, action dialog), `ui/dashboard.slint` (charts),
`src/dashboard.rs`. Units: README.md. Admin is read-mostly in Rust; the C# plan builds the same
pages first and the write flows after (PORT-PLAN M7).

## Shell
| Item | Bento | Nordic | Source |
|---|---|---|---|
| container padding | top/bottom/right `pad` 14 | 14 | app.slint:5404-5406 |
| navigation width | 210 (BNDashboard nav tile) | side-w 228 (the mail sidebar width) with 14 px insets | 5411 |
| nav padding | top 18, left/right 10, bottom 14, spacing 2 | top 28, left/right 14 | 5413-5417 |
| nav group label | left 10 (Nordic 16), right 10, bottom 14 | | 5420-5422 |
| nav row | height 36 x fs (Nordic 38; large 44), radius 13 / pill | | 5429-5430 |
| content surface | tile | border 1 px, radius `r-pane` | 5450-5452 |

## List pages (Accounts, Domains, Queue, Reports, Logs, Tasks, Cluster, Actions)
| Item | Value | Source |
|---|---|---|
| min width | 500 | 5475 |
| header | height 58 (Nordic 66); padding top 18 (Nordic 24), left 20 (Nordic 28), right 14 (Nordic 20), bottom 6; spacing 10; title letter-spacing -0.3; count pill height 22 radius 11 width = text + 16, `tile2`, 1 px border | 5481-5501 |
| filter row | padding left 16 (Nordic 24), right 16, top 6, bottom 10, spacing 8; search height 36 radius 14 (Nordic 18); status Segmented padding 3, spacing 2, segment radius 11, width = text + 22 | 5509-5526 |
| column heads | height 34; hairline under, inset 14 (Nordic 0); cells placed with `admin-cx` / `admin-cw` on the same grid | 5538-5556 |
| data row | height 42 x fs (large 48), radius 12 (Nordic 0 with a hairline inset 28) | 5546-5554 |
| status chip | height 22, radius 11, width `min(cell, text + 18)`; mint active, rose inactive, amber stale | 5560-5567 |
| queue delete | IconBtn right-aligned at `parent.width - 22` (Nordic 28) | 5571 |
| row action button | 74 wide, height 30 (large 40), radius 10 / pill, 1 px border unless hovered | 5573-5578 |

## Action dialog (x:Action/set: confirm, form, running, result)
Width `min(560, parent - 40)`, height `min(content, parent - 40)`, radius 24 (Nordic 28), 1 px border,
padding 22 (Nordic 28) (app.slint:5595-5605).

## Dashboard (ui/dashboard.slint)
Rows of weighted columns (`DashRow` / `DashCol` from `src/dashboard.rs`); each panel is a card
with `p.height` px height (fixed, or growing when `grows`).
| Item | Value | Source |
|---|---|---|
| panel kinds | 0 stat card, 1 area/line chart, 2 bar chart, 3 horizontal bar list, 4 highlight | dashboard.slint:20-30 |
| card radius | Bento: `r-summary` 22 for stat and highlight cards, `r-tile` 24 for charts; Nordic: 0 with 1 px hairlines top and left (one surface) | 97-103 |
| border | 1 px in Bento unless tinted; none in Nordic | 99 |
| inner width | `width - 32` (16 padding) | 109 |
| legend row | height 28, spacing 8; legend dot 8 x 8 radius 4; chevron 14 | 109-139 |
| tooltip | height 24, radius 6, width = text + 16 | 128-130 |
| spark room | the sparkline (104 px) only when the card is >= 190 wide | 147-148 |
| stat value | letter-spacing -0.56 | 158 |
| chart geometry | Rust sends SVG paths in a 1000 x 100 view box; axis labels 20 high at `height - 32` | 162-184 |
| sparkline / chip | 96 x 30 at `(w - 112, h - 44)`; stroke 2 | 168-184 |
