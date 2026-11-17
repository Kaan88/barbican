# Contacts

Source: `ui/app.slint` 4749-5109 (screen), `ui/pim-contacts.slint` (card, editor, duplicates,
import), `ui/pim-theme.slint`. Units: README.md.

## Screen structure
| Item | Bento | Nordic | Source |
|---|---|---|---|
| structure | sidebar tiles, people list tile, a header tile and a grid of detail tiles | sidebar on the ground; list and detail share one rounded surface (radius 28, 1 px border, padding 1); detail has a context column behind a hairline | app.slint:4749-4753, 4899-4911 |
| sidebar width | 220 | 228 | 4765 |
| sidebar padding | none (tiles) | top 30, bottom 22, left/right 14 | 4766-4769 |
| sidebar spacing | 12 | 22 (sections 18) | 4770, 4777 |
| people list width | 330 | 340 | 4915 |
| list surface | tile, 1 px border, radius `r-pane` 26 | no border, no radius | 4917-4919 |
| list padding | top 18, left/right 10 | top 28, left/right 14 | 4922-4924 |
| list header | left 12 (Nordic 14), right 12, bottom 10 (Nordic 14); export and import IconBtns 26 wide (large 40), icon 14 | | 4927-4933 |
| search field | height 38 (large 44), radius 14, 1 px border (focus `max(1.5, Prefs.focus-ring)`), padding-left 12, right 6, gap 8; clear X 24 wide | height 38, pill, padding-left 16 | 4935-4971 |
| section label row | height 30 x fs | | 4979 |
| contact row | height 52 x fs (large 56), radius 16 (Nordic pill), padding 12, gap 11; Avatar 36 x 36 | | 4987-5002 |
| list/detail divider | none (gap) | 1 px hairline | 5023 |

## Sidebar sections
| Section | Bento | Nordic | Source |
|---|---|---|---|
| address books tile | padding 10, row gap 0; book rows top padding 2 | padding 0; top 4 | 4782-4785 |
| groups | tint tiles inside a tile (padding 12); + IconBtn 22 (large 40); child groups top padding 8 | dot rows, left 16, right 8, bottom 2; child top 2 | 4798-4828 |
| categories | two-column chip grid: cell width `(w - 6) / 2`, gap 6, tile padding 12, chip padding 10 / 8, gap 6, dot 8 x 8 radius 3; a lone last chip takes the full width | dot rows, spacing 2, left 16 | 4834-4870 |
| nested keyword rows | top padding 2 for depth > 0 | | 4879 |

## Detail
| Item | Bento | Nordic | Source |
|---|---|---|---|
| empty state | 64 x 64 mark, 1 px border, spacing 10 | | 5034-5042 |
| detail surface | radius 26 | 0 | 5031, 5065 |
| padding | top 24, bottom 24, left/right 26 | top 30, left/right 44 | 5070-5073 |
| spacing | 18 (header row 20) | | 5074-5076 |
| photo | 88 x 88, radius 30 | circle | 5078-5080 |
| name block spacing | 2 | | 5086 |
| ContactCardView | padding 24, spacing 14; header row padding 18, gap 16; photo 72 x 72 radius 22 (cover); sub-rows spacing 4/6/8 | | pim-contacts.slint:116-154 |
| editor | padding 24; section caption rows 22 (first) / 30; field label column 120 wide; type select 104 wide; IM service field 150 wide; Add button height 26 | | pim-contacts.slint:164-229 |
| duplicates / import dialogs | see ContactDupes (323) and ContactImportDialog (369); read at M5 | | |

Open point: the editor and dialogs use `PimTheme` (`ui/pim-theme.slint`, 118 lines), a per-stream
alias of `Theme`; the C# port should use `Tokens.xaml` directly and drop the alias.
