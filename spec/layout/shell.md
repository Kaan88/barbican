# Shell: window, rail, sidebar, panes, responsive rules

Source: `ui/app.slint` (AppWindow starts at line 1671), `ui/theme.slint`, `ui/productivity.slint`,
`src/dnd.rs`. See README.md for units.

## Window
| Item | Value | Source |
|---|---|---|
| preferred size | 1400 x 900 (no min size set; derived) | app.slint:1677-1678 |
| ground (behind everything) | `Theme.ground` | app.slint:2340 |
| window padding / tile gap | Bento 14 / 12; Nordic 0 / 0 | theme.slint pad, gap |
| `win-w` | the window width copied in a `changed width` handler (a binding on width loops the layout) | app.slint:1719-1720 |
| layout order, left to right | rail, sidebar (folders), list, reader, context/sender panel or assistant | app.slint:2341-2780 |

Derived minimum: `fixed-w = shell-x + (side-shown ? side-w : -gap) + list-w + (Nordic ? 16 : 2*gap + pad)`
(app.slint:1734). The reader needs at least 240 x 200 (app.slint:2817-2818), and the sender panel only
appears when 440 px remain for the reader. Open point: choose explicit min size for the C# window
(suggest 960 x 640) and test the 1280 / 1150 / 1000 breakpoints below.

## Breakpoints and responsive rules (mail screen)
| Rule | Condition | Effect | Source |
|---|---|---|---|
| list narrow | `win-w < 1150` (Bento) / `< 1180` (Nordic), or (assistant open or composing) and `win-w < 1500` | list width 320 (Bento) / 330 (Nordic) instead of 368 / 390 | app.slint:1722-1724 |
| sender panel shown | no assistant, a message open, not composing, and `win-w - fixed-w - ctx-w - gap >= 440` | context column ctx-w beside the reader | app.slint:1735 |
| summary tiles shown | Bento only, not composing, `reader-w >= 330` | Today / Flagged / To do tiles above the reader | app.slint:1737 |
| assistant panel | opened, or composing and `win-w >= 1280` and not closed for this draft | 300 px column replaces the sender panel | app.slint:1728 |
| reader action bar | `roomy`: reader-w > 720 (Nordic) / 600 (Bento); `tight`: reader-w < 250 | roomy shows Reply all, Delete, Move, Tag inline; below it they move into More; tight also moves Delete and Star | app.slint:2849-2852 |
| composing | folders hidden (`side-shown = false`), composer replaces the reader | app.slint:1730 |
| reader-w | `win-w - fixed-w - (assistant ? chat-w + gap : sender ? ctx-w + gap : 0) - (Nordic ? 88 : 54)` | app.slint:1736 |

## Pane widths
| Pane | Bento | Nordic | Resize limits | Source |
|---|---|---|---|---|
| rail | 64 | 76 | fixed | theme.slint rail-w |
| folders (sidebar) | 204 | 228 | 180 to 400, drag the grip, double-click resets (stored 0 = default in panes.json) | app.slint:1725, 4187-4201 |
| message list | 368 (320 narrow) | 390 (330 narrow) | 240 to 600 | app.slint:1724, 4198-4201 |
| context / sender panel | 236 | 250 | fixed | app.slint:1726 |
| assistant (chat) | 300 | 300 | fixed | app.slint:1727 |
| ResizeHandle | 8 wide, a 2 px accent line only on hover or press | | | productivity.slint:526 |
| grips sit at | folders|list: `shell-x + side-w + gap/2`; list|reader: after the list, Bento `+ gap/2`, Nordic `+ 1.5` | | | app.slint:4190, 4198 |

Nordic collapses list, reader and context into **one rounded surface** (radius 28, 1 px line
border) split by 1 px hairlines; Bento gives each its own tile with `gap` 12 between.

## Rail (app.slint:2343-2415)
| Item | Bento | Nordic |
|---|---|---|
| container | tile, radius 24, hairline border, width 64 | flat (on the ground), width 76 |
| padding top / bottom | 16 / 16 | 26 / 26 |
| gap between buttons | 8 | 12 |
| mark (logo) | 32 | 34 |
| RailButton | 42 wide (large 48), radius 14 | 46 wide, circle |
| rail buttons order | Mail, Calendar, Contacts, Files, Tasks (optional), Assistant, Admin, spacer, Settings, account avatar | same |
| account popover | width 260, popup surface, radius `r-popup`, padding 6, rows 32 high radius 9 (Nordic 16), header 24 high | |

## Folders sidebar (app.slint:2418-2580)
| Item | Bento | Nordic |
|---|---|---|
| container | no own tile for the column; each section is a tile | on the ground, padding 30 top, 22 bottom, 14 left/right |
| section spacing | 12 | 0 |
| Compose button | height 52 (large 54), radius 20 | height 50 (large 54), pill; label "New message" (Bento: "Compose") |
| folders tile | padding 10, max-height = content + 22, min-height 120, radius 24 | padding 0 |
| section header ("Folders", "Tags") | height 26; label 12 x fs/700; gear and plus IconBtn 22 wide | left padding 16, right 12 |
| FolderRow | height 36 x fs (large 44), radius 18 (Nordic: pill 41 x fs); indent 14 per depth; left padding 11, icon 18 wide, gap 10 | left padding 16, right 12 |
| row spacing in lists | 2 | 4 |
| tags | two-column grid of tiles, cell width `(w - 8) / 2`, gap 8 | rows |
| tag tile / row | see TagTile app.slint:744, TagRow:709 | |
| Storage tile | radius 20, padding 12 top/bottom, 14 left/right, bar height 6 radius 3 | |

## Common controls (used on every screen)
| Control | Bento | Nordic | Source |
|---|---|---|---|
| ActionButton | height 34 (large 44), radius 12, 1 px border, padding 12 (label) | height 38, pill, padding 16 | app.slint:790 |
| PrimaryButton | height 32 (large 44), radius min(h/2, 12), min width = label + 28 | pill | app.slint:775 |
| IconBtn | width 30 (large 40) | same | app.slint:826 |
| QuickChip | height 28 (large 40), width = label + 24 | same | app.slint:842 |
| Segmented | radius 12, padding 3, segment height 26 (large 36), radius 9, width = label + 20 | height 28, pill, width = label + 24 | app.slint:903 |
| search field | height 38 (large 44), radius 14, padding-left 12, right 6, gap 8 | height 40, pill, padding-left 16 | app.slint:2639 |
| SummaryTile | radius 22, padding 14 top/bottom, 16 left/right; label 11.5/600, value 19/800, sub 12 | same | app.slint:873 |
| Avatar | default 34; list 38; large detail 88 (radius 30 / circle) | | app.slint:321 |
| Tile (side panels) | radius 24, 1 px line border | flat | app.slint:356 |
| toast | height 44, radius 16 (Nordic 22), padding left 18 right 8, gap 12; undo pill height 28 (large 36) radius 10 / pill | | app.slint:5733 |

## Toast and banner positions
| Item | Value | Source |
|---|---|---|
| toast y | `parent.height - 68` (rises by `Motion.rise` = 8 px as it enters), minus the quick-reply bar height when a message is open | app.slint:5730 |
| update banner | x 56, y `parent.height - self.height - 16`, width 300, radius 24, padding 16, gap 12 | app.slint:5762, update-banner.slint:47 |

## C# metrics (M1 shell-ui)
Where each number above lives in the C# app (`src/Barbican.App/Theme/`; UI-free, tested by `tests/Barbican.Presentation.Tests/ShellUi`):
| Item | C# |
|---|---|
| window padding / gap | `StyleMetrics.Pad`, `StyleMetrics.Gap` (Bento 14 / 12, Nordic 0 / 0); typed as `StyleState.WindowPadding`, `StyleState.Gap` |
| rail width, button, radius | `StyleMetrics.RailWidth` (64 / 76), `RailButton` (42 / 46), `RailButtonRadius`, `MarkSize` |
| sidebar width | `StyleMetrics.SidebarWidth` (204 / 228) |
| list width and the 1150 / 1180 breakpoint | `StyleMetrics.ListWidth(windowWidth)` (320 / 330 narrow, 368 / 390 wide), used by `ShellLayout.Compute` |
| summary tiles (Bento, reader-w >= 330) | `ShellLayout.SummaryTilesShown`, `ShellLayout.TilesMinReader` |
| reader width | `ShellLayout.ReaderWidth`; minimum window `ShellLayout.MinWidth` x `MinHeight` (960 x 640, open point of this file settled) |
| pane / tile radii | `StyleMetrics.PaneRadius`, `TileRadius`, `SummaryRadius`, `ButtonRadius`, `FieldRadius`, `RowRadius`, `NavRadius`, `PopupRadius` |
| compose button | `StyleMetrics.ComposeHeight`, `ComposeRadius` |
Not in M1: the sender/context panel, the assistant panel and the composing layout (M3, M5, M9).
