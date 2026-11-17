# Overlays: menus, palette, shortcuts sheet, dialogs, toasts, banners

Source: `ui/app.slint` (ContextMenu 1226, MenuItem/MenuPanel 1565-1597, toast 5723-5760, overlays
5998-6059), `ui/productivity.slint`, `ui/update-banner.slint`, `ui/theme.slint` (Motion, PopIn).
Units: README.md. Menus and dialogs are the same in both styles except where a Nordic value is given.

## Context menu (right-click and "..." menus)
| Item | Value | Source |
|---|---|---|
| width | 248 (flyout also 248) | app.slint:6040, 6050 |
| max height | `min(content, window - 8)`, scrolls (Flickable) | 6041 |
| surface | `Theme.popup`, radius `r-popup` (14 / 16), 1 px line border, shadow blur 16 `#00000055` | 1226-1240 |
| padding | 4 | 1241 |
| row | height 30, radius 9 (Nordic 15), left padding 8, right 10, gap 10 | 1200-1215 |
| separator | 9 high, a 1 px line inset 4 | 1205-1209 |
| header row | 24 high, text 11/600 dim, inset 10 | 1205, 1210 |
| icon | glyph 13 in a 16 wide slot (a tag colour dot replaces it for tag rows); checked rows show a 13 check | 1216-1220 |
| submenu arrow | glyph 9 in a 10 wide slot; the flyout opens beside the row | 1221 |
| keyboard hint | right-aligned in `hint` | 218 (CtxItem) |
| dismissal | a press outside both menus closes; flyout presses can fall through and must not | 6018-6022 |
| flyout open | hovering a `sub:` row opens it; hovering any other row closes it | ctxmenu.rs:133 |

## Popup menus (MenuPanel, used by toolbar menus)
Surface `Theme.popup`, radius `r-popup`, 1 px border, shadow blur 12 `#00000060` (app.slint:1589-1597).
MenuItem: height 30, radius 9 (Nordic 15), left padding 8, right 10, gap 8; check slot 14; glyph 12 in
a 14 slot; colour swatch 12 x 12 radius 6 in a 14 slot (app.slint:1565-1584). Standard widths:
reader More 220, Move 240, Tag 200; composer popups 150 to 240 (see reader.md, composer.md).

## Command palette (productivity.slint:165-316)
| Item | Value |
|---|---|
| width | `min(580, parent - 32)`; 680 in search-everything mode |
| surface | radius 18, 1 px border; PopIn |
| input row | height 54, left 18, right 14, gap 10 |
| results | padding 6 (bottom 8); header rows 28 high; result rows 40 (48 with a sub-line), radius 12, padding 12, gap 12; row icon tile 28 x 28 radius 9 with a 15 px stroke icon |
| list height | `content + 40`, scrolls (about 40 px per row, 28 per header) |
| scope chips | height 28, radius 14, width = label + 24 |
| key cap | height 22, radius 7, width = text + 14, 1 px border |

## Shortcuts sheet (productivity.slint:317-414)
Width `min(760, parent - 32)`, height `min(parent - 32, 680)`, radius 24, 1 px border, padding 24
(bottom 16), gap 14. Header icon tile 36 x 36 radius 13; close button 32 x 32 radius 12. Section
cards radius 14, padding 10; rows 34 high (headers 40), radius 10, padding 10.

## Dialogs and banners
| Item | Value | Source |
|---|---|---|
| confirm / simple dialog | width `min(400, parent - 40)`, radius 24 (Nordic 28), padding 22 (Nordic 28), spacing 14, buttons right-aligned gap 8 | app.slint:5979-5991 |
| admin dialog | width `min(560, parent - 40)` | app.slint:5595 |
| calendar Modal | radius 26, width `min(max-w, window - 32)` (see calendar.md) | calendar_ext.slint:341 |
| scrim | `Scrim` component: backdrop colour fades in over `Motion.enter`, removed instantly | theme.slint |
| toast | height 44, radius 16 (Nordic 22), padding 18 / 8, gap 12; slides up `Motion.rise` 8 px and fades in; undo pill 28 high (large 36) | app.slint:5723-5751 |
| update banner | 300 wide, radius 24, padding 16, gap 12, icon tile 36 x 36; bottom-left at x 56, 16 px above the bottom | update-banner.slint:47-67, app.slint:5762 |
| reminder stack | 340 wide, radius 22, top-right 16 px inset | calendar_ext.slint:831, 1104 |
| tooltip (`Tip`) | waits `Motion.tip-delay` 350 ms (kept with reduce motion), then fades in over `Motion.enter` | theme.slint |
| icon button tooltip | height 24, radius 8 (Nordic 12), width = text + 12, 4 px below the control | app.slint:1641-1645 |

## Motion tokens used by every overlay (theme.slint, see design/MOTION.md)
| Token | Value |
|---|---|
| fast (hover, press, focus) | 100 ms x k |
| enter (menus, popups, dialogs, toasts) | 150 ms x k, ease-out cubic-bezier(0.2, 0, 0, 1) |
| exit | 90 ms x k, ease-in cubic-bezier(0.4, 0, 1, 1) (few things animate out at all) |
| view (view switches) | 110 ms x k |
| list (row feedback) | 0 while `rapid` (key repeat), else fast |
| late (async panels) | 0 while `rapid`, else enter |
| pop-scale / rise | 0.96 / 8 px (both 1 / 0 with reduce motion) |
| PopIn | opacity 0 to 1 and scale `pop-scale` to 1 over `Motion.enter`; removal is instant; input live from the first frame |
| ViewIn | opacity 0.55 to 1 over `Motion.view` (calendar views); PageIn 0.45 to 1 (settings, admin pages) |
`k` is 0 when motion is reduced (Settings > Accessibility > Reduce motion, or Windows "Animation
effects" off while that setting follows the system).

## C# metrics (M1 shell-ui)
Every flyout is in-window: `ShouldConstrainToRootBounds="True"` (written `win:ShouldConstrainToRootBounds` for Uno). `StyleMetrics.PopupRadius` (14 / 16).
Enter: `Motion/PopIn.cs` from the flyout's `Opened`. Tooltips are `Motion/Tip.cs` (in-window), never `ToolTip`. Popup controls on the deny list
(`ComboBox`, `CommandBarFlyout`, `AutoSuggestBox`, ...) are not used (design/MOTION.md, ADR 0007).
