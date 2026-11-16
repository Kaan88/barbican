# Mail list (message list tile)

Source: `ui/app.slint` MailRow (1250-1475), list tile (2583-2780), `ui/productivity.slint`
(search suggestions, chips). Units: see README.md.

## List tile
| Item | Bento | Nordic | Source |
|---|---|---|---|
| width | 368 (320 below 1150 px window) | 390 (330 below 1180) | app.slint:1724 |
| surface | own tile: radius `r-pane` 26, 1 px border | part of the shared surface, no radius, no border | 2585-2587 |
| header padding | top 20, left 20, right 16, bottom 10 | top 28, left 28, right 24, bottom 16 | 2593-2596 |
| header spacing | 12 | 4 (plus a 10 px spacer before the search) | 2597, 2636 |
| title / count pill | title row spacing 6; "N new" pill height 24, radius 12, width = text + 20 | | 2599-2615 |
| title letter-spacing | -0.5 | -0.56 | 2605 |
| search field | height 38 (large 44), radius 14, 1 px border (focus ring `max(1, Prefs.focus-ring)`), padding-left 12, right 6, gap 8 | height 40, pill, padding-left 16 | 2639-2648 |
| "/" hint | 18 x 17 box, radius 5, 1 px line | | 2673 |
| filter toggle button | width 28 | | 2681 |
| quick filter chips | spacing 6, left/right = list header paddings | | 2689-2706 |
| filter panel | padding bottom 10, spacing 6 (date, size, unread/starred/has-attachment) | | 2707-2719 |
| selection bar (when rows ticked) | height 40, padding-left 20 (Nordic 28), right 12 (Nordic 20), spacing 2 | | 2726-2731 |
| list column gap | none inside the list; Nordic uses a 1 px hairline between list and reader, Bento a 12 px gap | | 2779 |
| load-more trigger | when fewer than 300 px remain below the viewport | | 2747 |
| loading / empty text rows | 24 and 44 / 80 high | | 2743, 2764, 2773 |
| suggestion dropdown | y = search bottom + 12, width `max(search width + 60, 280)` | | 6002-6003 |

## MailRow
| Item | Bento | Nordic | Source |
|---|---|---|---|
| row height | 82 (+24 when a verification code or attachment chips show) | 82 + 6 (+24) | 1252, 1286 |
| date group header | height 30, left padding 20 (Nordic 28), top 6, label 11.5 x fs/700 faint | | 1274-1280 |
| row background | x inset 8, radius `r-row` 18; background `tile2` when selected or hovered, `sel` when ticked, else the row tint; Bento selected has a 1 px line border; drop-target highlight is a 1 px accent border | x 0, radius 0 (full-width row) | 1288-1293 |
| selection marker | 3 px accent bar at x 3, y 14, height `row - 28`, radius 1.5 (a tag's colour otherwise) | 3 px accent bar full height at x 0 | 1312-1319 |
| padding | top/bottom 10, right 12 | top/bottom 14, right 24 | 1323-1326 |
| column gap | 11 | 8 | 1327 |
| leading element | avatar 38 x 38 (initials, photo or logo; squircle via `Theme.round`); unread state is the bold sender | no avatar: unread dot 7 x 7 accent (radius 3.5), top padding `7 x fs` | 1328-1336 |
| account colour dot | 8 x 8 (unified inbox) | | 1344 |
| text block spacing | 1 | 3 | 1338 |
| sender | 13.5 x fs / 600-700 | 14 x fs | 1349 |
| thread count chip | height 17, radius 8.5, text 10.5/700, width = text + 12 | | 1356-1360 |
| time | 11.5 x fs | 12 x fs | 1367 |
| subject | 13 x fs | 14 x fs | 1381 |
| preview | 12 x fs, one line, elided | 13 x fs | 1397 |
| search hit (marked) | subject height 18 x fs, preview height 17 x fs, StyledText with the same sizes | | 1389-1402 |
| tag dot | 8 x 8, radius 4 | | 1406 |
| verification-code chip | height 20, radius 10, width = text + 30, text 11/700 rose-fg, key glyph 9 | | 1414-1423 |
| attachment chip | height 20, radius 10, width `min(text + 30, 160)`, text 10.5, top padding 4 | | 1427-1436 |

## Hover actions (an overlay, painted, hit-tested against the row pointer)
| Item | Value | Source |
|---|---|---|
| button | 24 x 24 (large 32 x 32), 2 px inset, 2 px y | app.slint:1163, 1456-1457 |
| inline mode position | right-aligned at `row.width - act-w - 6` (Nordic 18); y centred on the sender line (Nordic 14 + 9 x fs, Bento 10 + 9 x fs) | 1268-1269 |
| floating mode | a pill in the chosen corner: 8 px from the side, 4 px from top or bottom; 1 px border | 1268, 1451 |
| default order | delete, star, read, archive (setting `hoverActions`; "tag" is not drawn) | main.rs:2133 |

## Drag and drop
A row drags after a 6 px move. Drop targets (folders, tag tiles) show a 1.5 px accent outline
(inside) or a before/after insertion line (folder reorder). The ghost follows the pointer
(`DragGhost`, productivity.slint:502). Details in `spec/behaviours` (dnd).
