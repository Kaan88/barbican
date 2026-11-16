# spec/layout: tuned metrics from the Slint UI

Per-screen paddings, radii, row heights, widths and breakpoints, extracted from the Rust app's
`ui/*.slint` at `rust-final` (`bba9613`). They are the tuned values behind the approved Bento and
Nordic mockups (`design/mockups/`), so the XAML matches the running Rust app and not only the
pictures. Where the two disagree, the mockup is the design and these files record what Slint
shipped (note the disagreement in the C# milestone report).

Files: `shell.md` (window, rail, sidebar, panes, responsive rules), `mail-list.md`, `reader.md`,
`composer.md`, `calendar.md`, `contacts.md`, `files.md`, `settings.md`, `admin.md`,
`overlays.md` (menus, palette, shortcuts sheet, dialogs, toasts, banners, motion).

## How to read the tables
- Units are Slint logical pixels. WinUI/Uno effective pixels (epx) are the same unit at 100 %
  scale, so values carry over unchanged.
- **Bento / Nordic** columns are the two styles (`Theme.style` 0 and 1). A single value spans
  both. Style-independent shared values live in `design/legacy-slint/theme.slint` and become
  `Themes/Tokens.xaml` (generated); this folder lists only what the screens add on top.
- `Prefs.fs` is the user's text scale (font size / 13 px); text sizes marked "x fs" scale with
  it. `Prefs.large-targets` is Settings > Accessibility > Large targets (bigger hit areas,
  listed as "large" where the source has a value). Sizes that appear without "x fs" are fixed.
- `Source` gives `file:line` in the Rust repo at `rust-final`. `app.slint` is the 6059-line main
  file; line numbers drift if the Rust repo is ever read at another commit.
- A radius written `h/2` means "pill/circle": half the control's height (Nordic makes every
  control a pill via `min(height / 2, ...)`).
- Hairline = 1 px `Theme.line`. Squircle (Bento) vs circle (Nordic): `Theme.round(size)` is
  `size * 0.37` (Bento) or `size / 2` (Nordic).

## Shared shape tokens (from theme.slint, for reference)
| Token | Bento | Nordic |
|---|---|---|
| pad (window padding) / gap (between tiles) | 14 / 12 | 0 / 0 |
| rail-w | 64 | 76 |
| shell-x (content start) | 14 + 64 + 12 = 90 | 76 |
| r-tile (side tiles) | 24 | 24 |
| r-pane (list, reader, context) | 26 | 28 |
| r-summary | 22 | 22 |
| r-button / r-field | 12 / 14 | pill |
| r-nav / r-popup | 14 / 14 | 22 / 16 |
| r-row | 18 | 0 |
| type scale | title 28/800, section 19/700, body 14.5/400, meta 12/600 | same |

## Not extracted (known gaps)
- Slint has no minimum window size; the minimum falls out of the layout. `shell.md` records the
  arithmetic. The C# app must pick explicit minimums (open point in the shell file).
- Pixel positions that Rust computes (calendar event rectangles, dashboard chart geometry, RTE
  painted document boxes) are in the behaviours catalogue, not here.
- The mockups' 1400 x 900 canvas is the reference size for all "default" widths.
