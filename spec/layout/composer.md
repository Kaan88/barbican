# Composer

Source: `ui/app.slint` 3205-3860 (inline composer replacing the reader pane), `ui/recipients.slint`,
`ui/compose-extras.slint`, `ui/mailextras.slint` (From override). Units: README.md.

## Placement and responsive rules
| Item | Value | Source |
|---|---|---|
| placement | replaces the reader; folders column hidden while composing; inset one px so the tile's rounded edge shows (radius `r-pane - 1`, Nordic 0) | app.slint:3205-3211 |
| `narrow` | composer width < 800 (large targets: < 1000): less-used formatting buttons move to a "More formatting" menu (width 210) | 3229 |
| `tight` | width < 600: bottom-bar buttons are icon-only (BarButton `compact`) | 3230 |
| `roomy` | width >= 780: the "Proofreading off" note shows | 3232 |
| docked reply | y = `parent.height - dock-h`; takeover mode uses `dock-top`; folded height 34 | 3234-3235 |
| assistant column | opens beside the composer when window >= 1280 (shell.md) | 1728 |

## Header
| Item | Bento | Nordic | Source |
|---|---|---|---|
| padding | top 16, bottom 10, left 24, right 18 | left 28 | 3316-3319 |
| title | 19 px (BNCompose) with draft state text (SaveState) | | 3314, compose-extras.slint:34 |
| spacing | 8 | | 3320 |
| pop-out / close buttons | CmpHeadButton 32 x 32 (large 40), 1 px border, radius squircle / circle; tooltip height 24, radius 8 (Nordic 12) | | 1626-1650 |

## Recipient rows
| Row | Height | Notes | Source |
|---|---|---|---|
| horizontal padding | left/right 24 (Nordic 28) | label column 56 wide; 1 px hairline under each row | 3328-3331 |
| From | 44 | identity chip: height 28, radius 14, 1 px border, padding 10 / 8, gap 6; chooser width 380, padding 4 | 3334-3373 |
| To | min 48, gap 8 | Cc and Bcc toggles (FmtButton) top padding 10 | 3383-3389 |
| Cc, Bcc | min 44, gap 12 | | 3394-3405 |
| Subject | 44, gap 12 | | 3412-3414 |
| recipient chip | height 24, radius 12, selected 2 px border; avatar 18 x 18 (radius 6 for groups, 9 otherwise); padding 3 / 4, gap 6; max width 240; remove glyph 16 x 16 | | recipients.slint:56-109 |
| autocomplete popup | radius 8, 1 px border, padding 7; rows min height 28; y = field bottom + 2 | | recipients.slint:68-125 |

## Formatting toolbar and editor
| Item | Bento | Nordic | Source |
|---|---|---|---|
| toolbar host height | 50 (large 58); 0 when plain text | | 3425 |
| host padding | top 12, left/right 24 | left/right 28 | 3426-3428 |
| strip | inset tile-2, 1 px border, radius 16, padding 5, gap 2 | radius = h/2, padding left/right 10 | 3432-3439 |
| FmtButton | see app.slint:1521 (glyph 12) | | |
| format popups | y 32; size and colour 150 wide, table 200 wide, padding 4 | | 3441-3480 |
| body scroll area | padding 12, spacing 6; content width = viewport - 56; min height `max(doc + 20, viewport - 40)` | | 3533-3558 |
| image selection | 2 px accent border, radius 4; resize handles at the corners (RteHandle) | | 3600-3607 |
| image size badge | height 18, radius 4, `#000000a0`, 6 px from top-right | | 3609-3610 |
| caret | 1.5 wide; height from the editor | | 3624 |
| floating selection toolbar | height 36 (large 44), radius 13 (Nordic h/2), 1 px border, padding 4, gap 2; buttons radius 9, padding 9 | | 3631-3654 |
| link bubble | width 330, height 36 (large 44), radius 13; x clamped to `[0, width - 330]`, caret x - 20 | | 3663-3668 |
| IME input | 1 x 1 hidden TextInput; the pre-edit text is drawn at the caret | | 3258-3290 |

## Attachment strip and bottom bar
| Item | Value | Source |
|---|---|---|
| chips area | left/right 24 (Nordic 28), bottom 12, gap 8 | 3715-3720 |
| attachment chip | height 34, radius 13 (Nordic h/2), 1 px border, padding 12 / 4, gap 8; type badge tinted | 3726-3733 |
| separator | 1 px hairline | 3740 |
| bottom bar | padding 12 top/bottom, 16 left/right, gap 6; buttons BarButton height 36 (large 44), radius 12 (Nordic pill), padding 12 | 3743-3747, 1598 |
| bar popups | attach 200 wide, templates 240, options 230, tags 180, assistant 200; open upward (negative y), rows 30 | 3749-3792 |
| split Send | arrow is cut off at 1400 px (known Rust issue, PARITY-ROUND2 hand list 11) | |

## Dialogs from the composer
| Dialog | Metrics | Source |
|---|---|---|
| Schedule send | width `min(400, parent - 32)`, radius 24, 1 px border, padding 20, gap 12; icon tile 36 x 36 radius 12; preset rows 40 high radius 12, padding 12 | compose-extras.slint:102-144 |
| Scheduled bar | height 52; inner 40 high radius 14, inset 12 | compose-extras.slint:189-198 |
