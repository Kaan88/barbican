# Reader (message pane, sender panel, assistant panel)

Source: `ui/app.slint` reader 2813-3205, assistant panel 3862-4056, sender panel 4058+,
`ui/reader.slint` (banners), `ui/mailextras.slint` (attachment cards, reply dock). Units: README.md.

## Pane
| Item | Bento | Nordic | Source |
|---|---|---|---|
| min size | 240 x 200 | same | app.slint:2817-2818 |
| surface | tile, radius `r-pane` 26, 1 px border | part of the shared surface (no radius, no border) | 2820-2822 |
| empty state | centred 68 x 68 mark (radius `round(68)`, `tile2`, 1 px line), spacing 10 | same | 2826-2831 |
| content padding | top 22, bottom 18, left/right 26 | top 30, bottom 24, left/right 44 | 2840-2843 |
| loading hint | 2 px sliding bar along the top edge | same | 3215 |
| fade-in | content opacity from 60 % (never gates input) | same | 3225 |

## Action bar (top of the reader)
| Item | Value | Source |
|---|---|---|
| buttons | ActionButton (see shell.md): height 34 / 38 (large 44), icon-only in Bento unless labels are on | 2847-2870 |
| inline set | Reply and Forward always; Reply all, Delete, Move, Tag when `reader-w > 600` (Bento) / `720` (Nordic); Star unless `reader-w < 250`; More always | 2849-2866 |
| More popup | width 220, padding 6, rows `MenuItem` 30 high (radius 9 / 15) | 2909 |
| Move popup | width 240, rows 30 high, indent `12 x depth`, folder icon 15 | 2871-2889 |
| Tag popup | width 200, rows 30 high, 10 x 10 colour dot radius 4 | 2890-2905 |
| gap below bar | Bento 20, Nordic 30 (before the subject) | 2924 |

## Header
| Item | Bento | Nordic | Source |
|---|---|---|---|
| subject | 27 x fs / 700, letter-spacing -0.54, up to 3 lines | 34 x fs / 700, letter-spacing -0.85 | 2925-2934 |
| gap under subject | 14 | 18 | 2937 |
| sender line | "Name to you and ... · Today 09:42", 12.5 x fs | 13.5 x fs | 2938-2958 |
| conversation strip | rows 30 high on a 32 pitch, max 5 rows visible, radius 10 (Nordic 15), padding 10, gap 10, unread dot 6, sender column 150 wide; header "Conversation · N messages" 11.5/700; top padding 12 | | 2960-2984 |
| spam / plugin pills | height 22, radius 11, width = text + 18 | | 2988-2995 |
| authentication badges | height 22, radius 11, width = text + 20 | | 3007-3012 |
| remote-content bar ("Load external content") | height 44, radius 14, 1 px border, padding 14 left, 6 right, gap 10 | | 3025-3033 |
| retained gap after header | 14 / 18 | | 3039 |

## Body
| Item | Value | Source |
|---|---|---|
| host | WebView2 body area; rounded-clip off (border-radius 0), gutter `Prefs.reader-gutter` (default 20) | app.slint:3042, theme.slint Prefs |
| inner padding | 16 (plain/lite modes) | 3069 |
| body shell window colour | the page colour (commit 9dc15d8) so there is no flash | 
| truncation / banners | RxBanner: radius 18, padding 12 (right 14), gap 12, icon tile 34 x 34 radius 12, action buttons height 30 radius 12 (min width = label + 24, or + 8 for link style) | reader.slint:81-126 |

## Attachment cards and rows
| Item | Bento | Nordic | Source |
|---|---|---|---|
| strip height with previews | 66 | | 3108 |
| file row | height 54, radius 16, 1 px border, left padding 10 | height 50, radius 18, no border, left padding 18; right padding 8, gap 10 | 3115-3126 |
| type badge | 32 x 32, radius 10, `rose-bg` | | 3130 |
| Download all (.zip) | same row metrics, padding 12/12, gap 8 | | 3147-3153 |
| preview card (images) | 216 x 56, radius 14, 1 px border, padding 8/6, gap 10; thumbnail 40 x 40 radius 10 (cover) | | mailextras.slint:126-149 |
| "Show N more" / small button | 28 x 28 radius 14 | | mailextras.slint:173-175 |
| From override chip | height 24, radius 12, width = text + 20; name field 150 wide | | mailextras.slint:101-110 |

## Quick reply and reply dock
| Item | Bento | Nordic | Source |
|---|---|---|---|
| gap above | 14 | | 3166 |
| bar | height 54, radius 20, 1 px border (focus `max(1, Prefs.focus-ring)`), padding 6, left 16 | height 58, radius 26, padding 8, left 20 | 3169-3178 |
| Send button | width = label + 40, radius 15 | width = label + 44, radius 20 | 3195-3196 |
| dock (replyUnderMessage) | preferred height `dock-h`; folded 34; handle in mailextras.slint:242 | | 3160-3163 |
| toast offset | the "Undo send" toast sits above the bar: `parent.height - 68 - bar height` | | 5730 |

## Sender (context) panel and assistant panel
| Item | Bento | Nordic | Source |
|---|---|---|---|
| sender panel width | 236 | 250 | 1726 |
| surface | tile, radius 26, 1 px border, clipped | transparent, hairline to its left | 4058-4064, 3862 |
| assistant panel width | 300 | 300 | 1727, 3864 |
| assistant header | rose tile, radius 24, padding 14 top/bottom, 18 left, 10 right, spacing 6 | plain heading, padding 28 top, 12 bottom, 22 left, 14 right | 3873-3882 |
| assistant action buttons | two per row: `(chat-w - (30 or 44) - 8) / 2` wide, 58 high (large 64), radius 16 (Nordic 18), 1 px border, padding 12/8 | | 3908-3920 |
| chat bubbles | padding 5 vertical, user left 40 right 10, assistant left 10 right 22 (tool rows 1), assistant bubble has a 1 px border; input pill height `text + 18`, radius 14 (Nordic pill), padding 14 | | 3945-3972 |

## C# metrics (M1 shell-ui)
`StyleMetrics.ReaderPadTop/Bottom/Side`, `ReaderSubjectSize` (27 / 34), `ActionButtonHeight` (34 / 38), `ReaderMinWidth` (240). The chrome is
`Views/Reader/ReaderView.xaml` (toolbar shown disabled, header, details panel with the authentication chips, conversation strip, attachment cards,
the `MailBodyView` slot, the quick-reply bar shown disabled).
