# Files

Source: `ui/app.slint` 5110-5397. Derived from BNContacts plus BNMail (no mockup board of its
own). Units: README.md.

## Layout and breakpoints
| Item | Bento | Nordic | Source |
|---|---|---|---|
| structure | an Upload button and a places tile on the left, then the file list tile (title with breadcrumbs, a toolbar, rows with squircle type tiles) | places on the ground; the list in one rounded surface; pills and circles | app.slint:5111-5113 |
| list width (`list-w`) | `width - 220 - gap - pad` | `width - 228 - gap - pad - 14` | 5118 |
| `narrow` | list-w < 820: the Size column drops | | 5119, 5270 |
| `tight` | list-w < 640: the Modified/Deleted column drops, Upload folder button drops | | 5120, 5256, 5271 |

## Sidebar (places)
| Item | Bento | Nordic | Source |
|---|---|---|---|
| width | 220 | 228 | 5129 |
| padding | none | top 30, bottom 22, left/right 14 | 5130-5133 |
| spacing | 12 | 22 | 5134 |
| places tile | padding 10, row spacing 2 | padding 0 | 5151-5154 |
| nav row | height 34 x fs, indent per depth (FileNavRow, app.slint:639) | | 5156 |
| trash separator | 9 high, 1 px line inset 1 (Nordic 6) | | 5159 |

## List
| Item | Bento | Nordic | Source |
|---|---|---|---|
| list surface | tile, 1 px border, padding 0 | surface padding 14 (left 0), 1 px border | 5179-5184 |
| top padding | 20 | 28 | 5189 |
| title row | left 22 (Nordic 32), right 16 (Nordic 24), spacing 6; breadcrumb last crumb letter-spacing -0.4; more button at the right | | 5193-5213 |
| toolbar | top 14, bottom 12, left 18 (Nordic 28), right 16 (Nordic 24), spacing 8 | | 5217-5221 |
| search field (toolbar) | min 120, preferred/max 220, height 34 (large 44) / Nordic 38, radius 12 / pill, 1 px border (focus `max(1.5, ring)`), padding-left 11 (Nordic 14), right 10, gap 8 | | 5223-5235 |
| Upload button | PrimaryButton, height 32 (large 44) / Nordic pill | | 5260 |
| column heads | height 30 x fs; left padding = outer (8 / Nordic 16) + 12 + 36 + 12; right padding = outer + 12 + 3 x action (24, large 32) + 2 x 6 + 12; spacing 12; Size column 90 wide (11.5/700), Modified column 140 wide | | 5264-5271 |
| header hairline | 1 px | | 5273 |
| file row | height 52 x fs (large 56), radius 16 (Nordic pill), padding left 8 (Nordic 16) right 8 (Nordic 12), top 2; inner padding 12, gap 12; drop target has a 1.5 px accent border | | 5279-5313 |
| type tile | 36 x 36, folders amber, files sky (squircle / circle) | | 5314-5319 |
| row actions | three 24 x 24 (large 32) icon buttons, spacing 6; in trash: restore and delete forever | | 5328, 5365 |
| inline rename field | height 34, radius 12 / pill, 1.5 px border, width = row - 24 | | 5337-5345 |
| empty state | 64 x 64 mark, 1 px border, spacing 10 | | 5375-5383 |
| sizes | Size cells 12 x fs width 90; Modified 12 x fs width 140 | | 5358-5359 |
