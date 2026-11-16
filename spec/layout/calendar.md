# Calendar and tasks

Source: `ui/app.slint` 4204-4748 (screen), `ui/app.slint` CalChip 939-1038 and CalContext 1039-1157,
`ui/calendar_ext.slint` (editor, agenda, tasks, overlays). Units: README.md.

## Layout and breakpoints
| Item | Bento | Nordic | Source |
|---|---|---|---|
| sidebar width | 250 | 248 | app.slint:1878 (`cal-side-w`) |
| hour gutter | 48 | 52 | 1880 (`cal-gutter`) |
| hour height | 52 | 54 | 1881 (`cal-hour-h`); CalChip default `hour-px` 56 |
| context panel | own tile, 280 wide | inside the surface after a 1 px hairline, 272 wide | 4724-4737 |
| context panel shows when | window width >= 1240 (`cal-ctx-on`) | same | 1879 |
| grid width | `width - shell-x - pad - (Nordic 14 : pad) - side-w - gap - (ctx on ? 292 : 0)` | | 1884 |
| `cal-tight` | grid width < 900: smaller title, short search | | 1885, 4485-4500 |
| `cal-narrow` | width available to the grid < 600: week shows three days unless `calendarNarrowWeekAllDays`; search 130 wide | | 1875, 1886 |
| range title size | 18 when grid < 800; else Nordic 22 (tight) / 28, Bento 21 | | 4485 |
| search field | 180 wide (130 narrow/tight); collapses to a 34 (large 40) icon until used when tight | | 4499-4500 |
| right padding | Nordic 14, Bento `pad` 14 | | 4215 |

## Sidebar
| Item | Bento | Nordic | Source |
|---|---|---|---|
| container | tiles with spacing 12 | on the ground: padding 30 top, 22 bottom, 16 left/right, spacing 18 | 4217-4224 |
| New event | height 52 (large 54), radius 20, accent | height 50, pill | 4227-4228 |
| mini month tile | padding 14, radius 24 | padding 0 | 4246 |
| mini month header | nav IconBtns 26 wide, icon 14, padding-bottom 4 (Nordic left 6, bottom 6), spacing 2 | | 4249-4255 |
| week numbers column | 22 wide | | 4258 |
| day cell | height 24 (Nordic 26), selection radius 9 (Nordic 14), inset 1 px | | 4266-4288 |
| calendars tile | padding 14 (Nordic 0, left/right 6), row spacing 2 (Nordic 4) | | 4308-4311 |
| calendar row | height 26 (Nordic 28; large 36), radius 8; colour dot 14 (Nordic 12), radius 5 (Nordic 6), hidden = 1.5 px ring; gap 10 (Nordic 12) | | 4314-4330 |
| tasks tile | min height 110, padding 14, row gap 6, row radius 8, checkbox 16 (large 22) radius 6 | | 4343-4374 |
| tasks cap | 6 visible then "Show all (N more)" (behaviour, not metric) | | PARITY-ROUND2 |

## Header and summary tiles (Bento)
| Item | Value | Source |
|---|---|---|
| grid tile padding | top 15 (Nordic 25), bottom 15 (Nordic 19), left/right 19 (Nordic 27) | 4473-4476 |
| header spacing | 8 (Nordic 10) | 4480 |
| summary tiles | three SummaryTiles, height `max(78, content)`, gap 12, Nordic has none above the grid (padding 14 top/bottom instead) | 4437-4455 |
| surface | Bento: tile, 1 px border, padding 1; Nordic: shared surface, hairline between grid and context | 4464-4469 |
| view switch | Segmented (see shell.md) | |

## Month view
| Item | Value | Source |
|---|---|---|
| week number column | 28 | 4540, 4548 |
| cell hairlines | 1 px between days and weeks | 4557-4559 |
| cell padding | 4 (top 5), spacing 2 | 4571-4573 |
| day number | 24 x 22, radius 8 (Nordic 11) | 4578 |
| event chip | height 19, radius 6; padding-left 5, gap 5; dot style 7 x 7 radius 3.5; `calendarMonthTimedEvents`: 0 tint bar, 1 coloured text, 2 dot and text | 4584-4606 |
| more link | "+N more" when more than 4 events (not clickable in Rust) | calendar.rs:541 |

## Day and week views
| Item | Value | Source |
|---|---|---|
| day head | "Mon 16" (Bento) or weekday over a big date with today in an accent circle 34 x 34 radius 17 (Nordic); padding-bottom 6 (Nordic 10), spacing 2 | 4625-4637 |
| all-day row | height 22, padding 3 / 3; no height cap (known gap) | 4643-4645 |
| hairlines | under the head in Bento; from the gutter in Nordic | 4655, 4670 |
| content height | `(hour_to - hour_from) * hour-h + 10` | 4659 |
| hour label | y -8, height 16, width = text + 6 | 4672-4673 |
| event block | height `max(e.height * hour-h / 56 - 4, 18)`; CalChip radius 12 (Nordic 14; compact 6); invite / selected = 1.5 px border; resize grip 6 px at the bottom; while resizing min height 14; padding 5 / 2 / 8 / 6 (Nordic 6 / 2 / 9 / 6) | 4701, 939-1027 |
| drag snap | 15 minutes (`hour-px / 4` per step), whole day columns across | 949-961 |
| now line | 2 px accent, 10 px high holder; Bento adds a 10 x 10 dot on today's column | 4709-4717 |

## Event context panel (CalContext, 1039-1157)
| Item | Bento | Nordic |
|---|---|---|
| padding | 22 outer; top 20, bottom 18, left/right 18 | top 28, bottom 22, left/right 22 |
| spacing | 14 | 16 |
| header tile | tint tile, radius 18, padding 14, spacing 2 | coloured label over a big title |
| RSVP buttons | height 36 (large 44), radius 12, border 1 px unless primary | height 38, pill |
| detail rows | height 30, gap 10 | height 32, a 1 px hairline before the description |

## Event editor and calendar dialogs (ui/calendar_ext.slint)
All dialogs use the shared `Modal`: width `min(max-w, window - 32)`, height `min(content, window - 32)`,
radius 26, 1 px border (calendar_ext.slint:341-353). They use a `pal` palette bound to `Theme`, so
they follow style, light/dark and more-contrast. Nordic does not change their metrics.
| Dialog | Metrics | Source |
|---|---|---|
| EventEditor | width `min(780, window - 32)`, height `min(body + 140, window - 32)`, radius 26; padding 20 (bottom 8), spacing 10; title field height 34 radius 12, side padding 20; date field 120 wide, time field 74 wide; row labels 84 wide; description TextEdit height 96; footer padding 16 (left/right 20), spacing 8, separated by a 1 px hairline | 365-725 |
| custom repeat box | radius 18, padding 12, spacing 8; interval field 60 wide; weekday toggles height 30 radius 10 border 1 px; combo widths 110 to 130 | 499-552 |
| guest rows | height 34 radius 10, left padding 8, gap 10; avatar 24 x 24 radius 9; suggestion list radius 18, padding 12, rows 32 high radius 10, padding 10 | 624-665 |
| alert rows | height 38, gap 10, icon tile 30 x 30 radius 11 | 593-599 |
| ScopeDialog (this / following / all) | max width 420, padding 22, spacing 10; icon tile 40 x 40 radius 14; option rows 40 high radius 14, 1 px border, left padding 12, radio 16 x 16 (selected ring 5 px) | 737-779 |
| QuickCreate | 340 wide, radius 22, padding 16, spacing 10; position clamped 12 px inside the window | 789-820 |
| ReminderStack | 340 wide, radius 22, padding 14, spacing 6 (rows 10); icon tile 30 x 30 radius 11; shown at top-right `window - self - 16` | 831-867, 1104 |
| MissedDialog | max width 460, padding 22; list up to 320 high, rows 48 high radius 16, padding-left 14 | 878-905 |
| TaskEditor | max width 520, padding 22; notes TextEdit 70 high; row labels 80 wide; date 130 wide, time 80 wide | 915-962 |
| ImportDialog | max width 480, padding 22; file chooser height 32 radius 12; Import button 34 high radius 12; label "Into" 50 wide | 973-1007 |
| SubscribeDialog | max width 480, padding 22; colour swatches 26 x 26 radius 9 (selected 2 px ring); label "Refresh" 70 wide | 1017-1048 |
| CxButton | height 32, radius 12, padding 14 / 14, gap 6 | 265-285 |
| Pill | height 28, radius 14, width = text + 24 | 319-327 |

## Agenda and tasks views (calendar_ext.slint:1111-1285)
| Item | Value |
|---|---|
| agenda list padding | 16 (top 8), spacing 4 |
| day header | height 40 (top padding 12, gap 8); date pill height 24 radius 12, width = text + 20 |
| agenda row | height 44, radius 14, padding 10, gap 12; marker 10 x 10, radius 5 for events (1.5 px ring for tasks) and 3 otherwise |
| empty state | 120 high |
| tasks view | filter row, rows and overdue rose pills as in the sidebar tasks; per-row metrics are in TasksView (1179); extract in M4 |
