# Moving Barbican's UI from Slint to markdown-shared-rs

Written 2026-11-25. A read-only study. The owner has decided to drop Slint and build the UI on
`Kaan88/markdown-shared-rs` (here "the toolkit"). Barbican stays a functional clone of Bulwark
1.21.2 for the features the owner kept, but its look comes from the toolkit. This file plans how to
make that work.

> **Revised 2026-11-25 after the feature cut** (`docs/FEATURE-CUT.md`). What changed:
> - Everything was re-costed against the cut. Plugins (and their slots), app lock, sharing of
>   folders, calendars and address books, printing, the Blitz light renderer, pop-out windows,
>   Find a time, i18n, list columns, RTL editing and the other cut rows are no longer ported or built.
> - **K1 shrinks** from "several windows on one loop" (L, high) to host additions for one window:
>   named timers, a hidden live window, damage regions (M, medium). The single-window app needs
>   only owned popup surfaces, and that piece moves into **K2**, which no longer waits on K1.
> - **K3 rises** to rank 3: with Blitz cut, WebView2 shows the mail body and the Files previews of
>   .eml and HTML, with no CPU fallback. **K10 drops** (English only). **K6 is optional**, with
>   themes (Later). K12 and K13 are unchanged. W9, X6, L4 and most of W1b are no longer wanted.
> - Fit table (section 3): a new last column. Printing, several windows, plugin slots and the light
>   renderer are marked not needed.
> - Port size recounted at `b00812d` without the cut UI (5.1). About 5 % of the Slint goes. The
>   larger saving is unbuilt work.
> - 5.3 (the Blitz alternative is gone; Files previews need the WebView), 5.4, 5.5 and 5.6 updated.
> - Milestones (5.7): R1-R3 (removal of cut code, under way) come first. U0 shrinks (the settings
>   cache is merged). T3 shrinks. T4 is split into T4 (K3) and T5 (K2). U13 (pop-outs) is dropped.
>   New length: about 4 to 5 months, against 4 to 6.
> - Section 6 kept. K6 is marked optional, and 6.4 says what the themes decision would change.
> - The toolkit was re-read at `02a52be`. Only X3's turning disclosure for the tree landed since
>   (PR #13). It changes no K item.

> **Revised again 2026-11-25 after the second cut session** (`docs/FEATURE-CUT.md`, "Second
> session"). The owner weighed the features behind the costly K items. Nothing was cut for good
> apart from loading skeletons; several items moved to **after the switch**:
> - **K12** (screen readers) and **K2** (owned popup surfaces) are future items, out of the switch
>   path. The capture workaround stays for menus over the mail body. T5 and U7 move after U12; the
>   screens build no accessibility tree; U11 shrinks to S.
> - **K8 and K13 are unchanged**: all four in-app drags stay, and so do drops from Explorer.
> - **K9 is still open** (toolkit K9, or Barbican's RTE in a plain mode). The raw Sieve editor, the
>   composer's HTML source view, signature HTML source and the admin raw message fields are not
>   ported before the switch, but K9 in some form is still needed by U6.
> - **L9** (pasting images) moves after the switch; **L8** stays optional.
> - App drawing deferred: the pop-up date picker, the typed time field, hover actions on rows and
>   colour swatches. Skeletons are cut. Chip fields outside the composer reuse the composer's.
> - Rough total of toolkit work before the switch: about 1 L, 5 M and 4 S, 6,500-10,500 lines
>   (8,000-13,000 before). Length: about **3.5 to 4.5 months** (4 to 5 before).

**What was read.** The toolkit at `main` `48eb3ce`, re-read at `02a52be` (2026-11-25), cloned
read-only. Its docs, its `crates/mdshared-ui` sources, its open PRs and branches. Barbican at `main`
`3774fd5`, recounted at `b00812d`: `ui/*.slint`, `src/`, `docs/JANK-AUDIT.md`, `ROADMAP.md`,
`docs/FEATURE-CUT.md`. Bulwark's folder list, for the feature set only. The HTML mockups in
`docs/mockups/` are undecided and nothing here rests on them.

**What was not done.** Nothing was built or run, in either repository. Every claim about speed,
feel or behaviour on screen is marked as such. Where a question can only be answered by building,
the text says so.

Paths: toolkit paths start at its repository root (`crates/...`, `docs/...`). Barbican paths start
at this repository's root (`src/...`, `ui/...`). Line numbers will drift, and the R removals will
move many of them.

Sizes: toolkit items use the toolkit's own scale (`docs/ROADMAP.md:25-26`): **S** up to about 300
lines, **M** 300-1,000, **L** 1,000-3,000, **XL** more. Barbican work uses Barbican's scale
(`ROADMAP.md:14`): **S** up to 2 days, **M** up to a week, **L** several weeks.

---

## Summary

- The toolkit is a **software-rendered, immediate-mode** toolkit. The application owns its state
  and draws every frame through a CPU painter into a bitmap, which softbuffer presents. There is no
  widget tree, no callbacks, no layout engine and no GPU.
- It is young as a toolkit. It has served mdview and mdedit since 2026-11-12. Most widgets for new
  applications are two days old, marked "provisional", and "not seen by a person". It has good
  unit tests (about 1,700) and CI on Windows, Linux and macOS.
- The **three hard gaps** for Barbican: popups that draw above the WebView2 child (the toolkit draws
  menus inside the one window); a layout helper (Barbican's Slint leans on Slint layouts); and the
  everyday controls (buttons, framed fields, multi-line text, check boxes with labels, radio
  groups, chips). After the cut, the first gap **no longer needs several windows**: the app is
  single-window, so owned popup surfaces are enough (K2). After the second cut session K2 is a
  future item: the capture trick covers popups over the mail body until after the switch.
- The WebView2 child now has **two homes and no fallback**: the mail body and, after R3, the Files
  previews of .eml and HTML. Blitz is cut (5.3).
- A **complete skin** is **partly possible today**: colours and some widget looks are runtime data.
  Fonts and most sizes are compile-time constants inside the toolkit. Themes are "Later", so the
  item that closes this (K6) is optional (section 6).
- **The port cannot be done step by step inside one binary**: Slint's winit backend owns the event
  loop and the window, and there can be only one winit loop per process. The plan builds the new UI
  as a second front end over the same Rust logic, behind a cargo feature, made usable for mail
  first, and switches over at parity. The Slint app keeps shipping until then.
- After the removals (R1-R3), about **29,300 lines (53 of 107 files) of `src/` never touch Slint**
  and survive unchanged. About 45,000 lines touch it through about **2,440 binding call sites**
  (2,650 before the cut). A generated stand-in for Slint's `AppWindow` keeps most of those as they
  are.
- The cut takes about **640 of 12,654 Slint lines** (5 %) out of the port. The larger saving is
  work never started: the plugin slot renderer, pop-outs and the multi-window host, printing,
  translations, list columns.

---

## 1. What the toolkit has

### 1.1 Shape and size

| Item | Fact | Evidence |
|---|---|---|
| Crates | `mdshared-text` (bitmap, text engine), `mdshared-highlight`, `mdshared-ui` (painter, widgets, models, platform glue, host), `mdshared-app` (plumbing), `mdshared-fetch` (downloads), `mdshared-term` (terminal model) | `README.md`, `AGENTS.md` "The crates" |
| Lines | `mdshared-ui` 84,372 (with tests and examples); `mdshared-app` 38,841; the rest about 14,600 | `wc -l` over `crates/*` |
| Tests | 1,033 in `mdshared-ui`, 549 in `mdshared-app`, 130 in the others: about 1,712 `#[test]` | `grep -c '#\[test\]'` |
| CI | fmt, clippy with warnings as errors, and tests on Linux, Windows and two Macs, each feature built alone | `.github/workflows/ci.yml`, `AGENTS.md` "Build, test, lint" |
| Users | mdview and mdedit since 2026-11-12, each pinning a commit; Purge, mdmix, mdplay planned | `README.md`, `docs/ROADMAP.md:3-7` |
| Rules | Model and drawing kept apart; no callbacks and no retained tree; nothing remembers the layout; no clock inside a model; `unsafe` only at the system's edge; add, never change a public API | `AGENTS.md` "Shape of the code", "Compatibility" |

### 1.2 Architecture, area by area

| Area | What there is | Evidence |
|---|---|---|
| Rendering | A CPU painter (`Painter`) draws into a `Bitmap` of opaque BGRA bytes: boxes, rounded boxes, frames, polygons, paths, rings, arcs, gradients, images, exact soft shadows (CSS `box-shadow`), clip, offset, group fade, icons turned about their centre (`icon_rotated`, new in `02a52be`). No GPU. softbuffer presents the bitmap | `AGENTS.md` "What this is"; `docs/CATALOGUE.md:17-21`; `crates/mdshared-ui/src/gfx.rs:1-4` |
| Windowing | winit 0.30.13. Each application owns its loop. An opt-in host (`host`, feature `host`) runs the loop, the present and the timers of **one** undecorated window: damage-only presents, no drawing while covered, a hidden first frame, Mica and frost on Windows, resize edges and snap layouts | `crates/mdshared-ui/src/host.rs:1-100`; `docs/CATALOGUE.md:137` |
| Layout | None as an engine. Models are handed rectangles each call. `Rect` has `inset` and `contains` only. Layout models exist for specific shapes: `rows`, `sections`, `grid`, `table`, `splitter`, `viewport`, `infobar::stack_layout` | `AGENTS.md` "Nothing remembers the layout"; `gfx.rs:37-72`; `docs/CATALOGUE.md:87-99` |
| Text shaping | parley `=0.12.0` lays out, swash rasterizes with subpixel masks (ClearType order on Windows), a byte-capped glyph cache, fontique fallback, colour emoji runs, bidi through parley. Three faces only (`Ui`, `Semibold`, `Mono`) from fixed family lists | `crates/mdshared-text/src/text.rs:182-340`; `gfx.rs:76-127`; `docs/CATALOGUE.md:31-32` |
| Text input | `textfield`: one line, caret, selection, IME preedit, secret mode, caret blink on the animator. `seltext`: read-only selectable paragraph, no scrolling. **No multi-line editor** | `crates/mdshared-ui/src/textfield.rs:1-60`; `docs/CATALOGUE.md:77-78`; `docs/ROADMAP.md:163` |
| Input and IME | The host reduces winit events to its own `Event` (pointer, press with click count, wheel with precise flag, key with text, IME commit and preedit, focus, scale, size, theme, contrast). IME is switched on only on macOS; on Windows the app must call `set_ime_allowed` itself, and it "has not been tried". Nothing calls `set_ime_cursor_area`. Key releases, touch, pen and file drops are not reduced (raw winit in `App::raw`) | `host.rs:140-244`; `host/run.rs:591-596`; `crates/mdshared-ui/src/window.rs:82-90` |
| Focus and keys | `focus`: Tab order declared per frame, rings on keyboard use only, scopes for dialogs and menus. `keys`: shortcut parsing. `commands`: a command table with user keys, conflicts, settings. No chord sequences (`Ctrl+K Ctrl+S`) | `docs/CATALOGUE.md:47, 98-99`; `docs/ROADMAP.md:214` (X6) |
| Accessibility | AccessKit tree built from the models (feature `a11y`), served to UI Automation on Windows only. 50 UIA checks pass on the Purge example. Not tried with Narrator. No UIA Text pattern, no live regions, AT-SPI and macOS open | `crates/mdshared-ui/src/a11y.rs:64-71`; `docs/ROADMAP.md:74` |
| Theming | `theme::Palette` (about 30 colour fields, `LIGHT`, `DARK`) passed to every `draw_*`. `tokens`: accent made legible, high contrast at 7:1, the Windows contrast theme's colours, sizes **named but not read by the widgets**. `appearance`: schemes (dark, light, warm, cool), accent source, corners, shadow strength, menu rows, motion speed, saved as one setting. Eight widgets take a `Look` struct | `crates/mdshared-ui/src/theme.rs:8-40`; `crates/mdshared-ui/src/tokens.rs:1-30`; `crates/mdshared-ui/src/appearance.rs:1-35` |
| Widgets | Title row and tabs, caption buttons, panes, menus with access keys and submenus, flyout, drop-down, toggle, tick box, expander (its chevron now turns), link button, segmented switch and tab bar, slider and progress, spinner, scrollbars, splitter, table columns, tooltip timing, toast and tooltip bubbles, dialog card and dialog buttons, InfoBar, notice bars, command palette, settings form, folder tree (with a turning disclosure, `glyph::draw_disclosure_animated`), thumbnails, plus media widgets. No general push button, icon button, framed text field, multi-line text, radio group, chips, tri-state check, card or avatar | `docs/CATALOGUE.md:37-85`; `docs/PURGE-MOCKUP.md:44-69` |
| Event and state model | Immediate mode. The application keeps all state. It passes a press, key or wheel into a model, which returns an outcome. Hits are rectangles the app records while drawing. Workers reply through `Proxy::send` to `App::message`. One wake-up folds the animator's deadline and one app timer (`Cx::wake_at`) | `AGENTS.md` "Shape of the code"; `host.rs:21-23, 43-50`; `crates/mdshared-ui/src/host/cx.rs:110` |
| Animation | `anim::Animator` (tweens, springs, Windows 11 timings), presence for popups, transients, a toast stack, page transitions, damage groups, reduced motion that follows Windows' setting live | `docs/ANIMATION.md`; `docs/CATALOGUE.md:111-125` |
| Multi-window and popups | **One window.** The host says "Deliberately not here: tabs and several windows". Menus are "Popup menus drawn inside the window". Flyouts, drop-downs, tooltips, toasts and dialogs are drawn into the same bitmap | `host.rs:90-91`; `crates/mdshared-ui/src/menu.rs:1`; `docs/ROADMAP.md:67` (W1b open) |
| Native child window | Nothing in the toolkit. But `Cx::window()` hands out the winit `Window`, which implements `HasWindowHandle`, so wry's `build_as_child` can attach a WebView2 to it. The toolkit then knows nothing about the child's rectangle or z-order | `host/cx.rs:165`; no match for `WS_CHILD`, `SetParent` or `WebView` in the toolkit |
| DPI | Logical px in every API; the painter scales and snaps edges to device px; the host redraws on a scale change; `window_state` restores a window at its monitor's scale; `platform::system_scale` | `gfx.rs:1-4`; `host.rs:199-201` (`Event::Scale`); `docs/CATALOGUE.md:46` |
| Platform glue | Clipboard text and HTML both ways, open URL and file, reveal in Explorer, file dialogs through rfd (async), system dark mode, accent, high contrast, reduced motion, caret blink time, double-click time, DWM styling and backdrop, tray icon, toasts with actions, media session, Recycle Bin | `crates/mdshared-ui/src/platform.rs:56-1700`; `docs/CATALOGUE.md:52-53, 133` |
| Platforms | Windows first. Linux compiled and tested in CI, run on KDE Plasma and sway only. macOS compiled and tested in CI; tray, notifications and a11y adapters missing there. For Barbican, Linux is "Later" (`docs/FEATURE-CUT.md:37`) | `README.md` "Linux"; `docs/LINUX.md`; `docs/CATALOGUE.md:52-53` |

### 1.3 Maturity

- **Strong:** the painter, text engine, menus, text field, tree and platform glue have shipped in
  two applications for two weeks. Drawing is tested by pixels. Time is tested on a hand-moved clock.
  Public functions are fed NaN and empty input (`AGENTS.md` "Tests").
- **Young:** most widgets past the Markdown apps' needs landed on 2026-11-24 and 2026-11-25. The
  catalogue marks them "provisional" until a second application uses them (`docs/ROADMAP.md:23`).
  Many say "not seen by a person" (`docs/PURGE-MOCKUP.md:12`, `docs/ANIMATION.md:6-10`).
- **The host is new.** It was run headless for a few seconds on Windows, "not seen by a person"
  (`host.rs:96-100`). mdview and mdedit still use their own loops (`README.md`).
- **Measured once:** a Purge-shaped window's first frame took a median 63.9 ms against 41.7 ms for a
  bare window, and its exe was 3.46 MB (`docs/ROADMAP.md:171-173`). Nothing was measured for a
  window as busy as Barbican's.
- **Compatibility rule.** "Add; do not change" (`AGENTS.md` "Compatibility"). Good for Barbican,
  which will pin a commit. It also means each look change must arrive as a new opt-in function,
  which slows a skin system (section 6).

---

## 2. The toolkit's own roadmap

Source: `docs/ROADMAP.md` (the toolkit's one plan), its open PRs, its branches. `gh issue list`
returns no issues: the repository uses no issues. Since `48eb3ce`, PR #13 (the tree's disclosure
turns) has merged. `feat/l10-settings-motion` (settings form with motion) is still open.

**Order of work** (`docs/ROADMAP.md:50-60`): Purge fast track first, then the music player, mdmix,
a file manager and a terminal. Barbican is not in it yet.

**Open items, and whether Barbican still needs them after the cut:**

| Id | What | Size | Status | After the cut | Evidence |
|---|---|---|---|---|---|
| W1b | The rest of the host: several windows, tabs, regions with their own damage, key releases, Linux shadow and backdrop | L, high risk | open | **Only the damage regions** (in K1). Several windows: pop-outs cut. Linux: Later | `docs/ROADMAP.md:67` |
| W8 rest | AT-SPI and macOS adapters; Narrator never tried | XL | open beyond Purge's scope | **After the switch** (K12, a future item). AT-SPI: Linux Later | `docs/ROADMAP.md:74` |
| W9 | Translations of the toolkit's own texts, plural forms, locale number formats | L | open | **Not needed**: English only | `docs/ROADMAP.md:75` |
| L4 | Table columns hidden and reordered | M | open | **Not needed**: list columns cut; Files keeps its sort headers | `docs/ROADMAP.md:84` |
| L5 | Rename in place (a field over a row) | S | open | Needed (folders, files) | `docs/ROADMAP.md:85` |
| L7 | Drag and drop inside a window, files dropped from the system | M | open | Needed (= K8) | `docs/ROADMAP.md:87` |
| L8 | Dragging files out to other applications | L, high | open | Optional, after the switch (second cut session) | `docs/ROADMAP.md:88` |
| L9 | Files and images on the clipboard | M, high | open | **After the switch** (second cut session): until then a screenshot is saved and attached, or dropped in from Explorer | `docs/ROADMAP.md:89` |
| X6 | Chord sequences and keymaps per context | M | open | **Not needed**: Barbican's `g` sequences are its own state machine (`src/shortcuts.rs:154-163, 551`) | `docs/ROADMAP.md:214` |
| P8b | The single-instance hand-over reaching a winit window | S | open | Needed (mailto and webcal links into the running app) | `docs/ROADMAP.md:165` |

**Done items Barbican can use as they are:** focus (W2), window state (W3), cursors (W4), tokens
(W5, W5b), tray (W6), notifications (W7), submenus (L1), sectioned lists (L2), two-axis scrolling
(L3), menu look (L11), appearance (L12), JPEG and thumbnails (F6), drop-down, toggle, link,
flyout, InfoBar, expander, selectable text (P2-P7), single instance by mutex (P8), animation
(D1-D7, X3 with the tree's disclosure, X4).

**Other plans noticed:**
- A Win32 "hosted mode" (`feat/hosted`, uncommitted) draws a toolkit component into a window whose
  loop belongs to another program (`docs/ROADMAP.md:45-48`). In theory it could put a toolkit pane
  inside the Slint window. It would bring back the child-window z-order problem, so this plan does
  not use it (section 5.2).
- PR #8, "classic desktop widgets (menu bar, column header, field, button)", was closed unmerged
  on 2026-11-24. Its branch `feat/classic-widgets` (`d5edb8a`) still exists. Its buttons and fields
  are exactly what Barbican lacks (K4).

---

## 3. Fit against Barbican's needs

"Toolkit effort" is new work in markdown-shared-rs (toolkit sizes). "Barbican effort" is the work
in this repository to use it (Barbican sizes). Item ids K1-K14 are the proposals in section 4.
"After the cut" says whether the row still holds.

| Capability | Barbican today | Toolkit | Toolkit effort | Barbican effort | After the cut |
|---|---|---|---|---|---|
| Virtualised long lists | Slint `ListView` (14 uses), rebuilt in full on many events (JANK #8) | **Present.** `rows::Rows` (one height), `sections::Sections` (heights that differ, sticky date headers, collapsible groups), `select::Selection` (Ctrl, Shift, rubber band, type-ahead), `scroll::SmoothScroll`, an a11y list that keys rows by item (`docs/CATALOGUE.md:71, 93-95, 109`) | none | M per list (mail, contacts, files, search) | Needed. Mail list stays two-line rows, newest first: no column mode |
| Rich text editor with spell squiggles | Own engine: `src/rte/{model,editor,html,layout,spell}.rs`, 3,349 lines with no Slint, laid out with parley 0.11.1; Slint draws boxes and texts from models (`src/rte/mod.rs:244-328`) | **Partial.** The painter draws parley layouts, but `Painter::draw_layout` is private (`gfx.rs:670`). No wavy underline. parley versions differ (`=0.12.0` there, `=0.11.1` here) | K7, S | M: `rte/mod.rs` glue (832 lines) redone as painter calls; parley bump | Needed. RTL editing cut, so no bidi editing work |
| HTML mail body and Files previews | WebView2 child through wry (`src/mailview.rs`), one view for both: the reader's body and, since R3, the Files preview of .eml and HTML (`mailview::show_file`, placed over the preview's document area); plain text. Blitz is removed (R3) | **Missing** as a feature. Not blocked: `Cx::window()` gives wry the parent handle (`host/cx.rs:165`). Nothing keeps a child's bounds in step with layout | K3, M | M: mailview glue; sanitizer, shell JS and IPC unchanged. S: the Files preview placed the same way | **More needed.** Blitz cut: WebView2 in two places, no CPU fallback (5.3) |
| Menus and popups above the WebView | Slint draws popups inside the main surface (JANK-AUDIT.md:6); a capture of the body stands in while the WebView hides (`src/mailview.rs:7-9`, `src/capture.rs`) | **Missing.** Menus are "drawn inside the window" (`menu.rs:1`); one window only (`host.rs:90`) | K2, L, high risk (no longer K1 + K2) | S to keep the capture trick; S per popup kind to move to K2 later | **After the switch** (second cut session): the capture trick is the plan; K2 is a future item |
| Drag and drop in the app | Slint pointer tracking and named drop targets (`src/dnd.rs:1-12`): mail to folders, folder reorder, events in the grid, files to folders | **Missing.** L7 open (`docs/ROADMAP.md:87`) | K8 (= L7), M | M | Needed: all four drags kept (second cut session) |
| Drag and drop from the OS | winit `DroppedFile` in a Slint window filter (`src/rte/mod.rs:814-832`) | **Partial.** The host passes raw winit events through `App::raw` (`host.rs:291-296`); no reduced event, no hover feedback | part of K8, S | S | Needed: kept (second cut session) |
| Drag out to Explorer (attachments) | Not done | **Missing.** L8 open, high risk (`docs/ROADMAP.md:88`) | L8, L | optional | Optional, after the switch |
| Resizable split panes | Own `ResizeHandle`, widths in `Pd.list-w` (`src/dnd.rs`) | **Present.** `splitter`: min sizes, snaps, collapse, reset, saved position (`docs/CATALOGUE.md:84`) | none | S | Needed, and now also for the kept reading pane position (right or off; bottom is cut) |
| Calendar grid | `ui/calendar_ext.slint` (1,338 lines) and parts of `ui/app.slint`; day, week, month, agenda; drag to move and resize | **Missing.** `grid` is uniform cells only; the painter is enough to draw it | optional K13, M | L | Needed. Find a time is cut, which never used the grid |
| Keyboard focus and shortcuts | `src/shortcuts.rs` (848 lines) via a Slint key hook; Tab does not reach controls, no focus ring (JANK #23) | **Present.** `focus` (Tab order, rings, scopes), `keys`, `commands` (user keys, conflicts). Missing: chords (X6) | none (X6 not needed) | M: route keys through `commands`; declare focus each frame | Needed. Print shortcut goes in R2 |
| IME | Slint's `TextInput` does it | **Partial.** Events and preedit exist; IME off on Windows, untried; no candidate window placement (`window.rs:82-90`) | K10, S | S per text surface (fields, recipients, RTE) | Lower: English only. Still S |
| Accessibility | Slint's AccessKit plus 115 `accessible-*` properties in `ui/*.slint` | **Partial.** UIA on Windows at Purge's scope; no text ranges, no live regions (`a11y.rs:64-71`) | K12, L | ~~L: the app builds the tree each frame~~ none before the switch | **After the switch** (second cut session): keyboard and focus kept, screen readers a future item. Goes backwards from Slint's free tree until then |
| System tray | `tray-icon` crate (`src/desktop.rs`) | **Present.** `tray`, Windows done, Linux unverified (`docs/CATALOGUE.md:52`) | none | S, or keep `tray-icon` | Needed (close to tray kept) |
| Notifications | Own WinRT toasts (`src/notify.rs`, no Slint) | **Present.** `notify` with actions (`docs/CATALOGUE.md:53`) | none | none: keep `src/notify.rs` | Needed |
| Theme tokens | `Theme` global (`ui/theme.slint`, 353 lines), two styles (Bento, Nordic), switchable in memory | **Partial.** Runtime palette, accent, high contrast, schemes; sizes and fonts not themeable (`tokens.rs:24-25`) | K6, M-L: **optional, Later** | S: one look; M if a second style or themes come back | One look now; themes Later (section 6) |
| Animations | 69 `animate` blocks; `src/motion.rs` | **Present.** `anim`, reduced motion that follows Windows live (`docs/ANIMATION.md`) | none | M, spread over the screens | Needed |
| Images and avatars | `image` crate (PNG, JPEG, GIF, WebP, BMP, ICO) and resvg, shown as `slint::Image` (19 uses) | **Partial.** `Painter::image` with a corner radius (so round avatars), PNG and JPEG decode, a thumbnail cache. No GIF, WebP or SVG decode; the image is scaled on every draw and nothing is kept (`docs/ROADMAP.md:103`) | K14, S | S: Barbican's own decoders feed RGBA | Needed (avatars, BIMI, Files thumbnails) |
| Text selection and copy | Mail body selection lives in the WebView; native text is mostly not selectable | **Partial.** `seltext` (wrapped paragraph, Ctrl+C, context menu, no scrolling); clipboard text and HTML both ways (`platform.rs:227, 309`) | none for now | S | Needed |
| File picker | `rfd` directly | **Present.** `pick_files_async`, `pick_folder_async`, `pick_save_async` (`platform.rs:105-140`) | none | S, or keep `rfd` | Needed |
| Printing | WebView2 print, else an HTML file opened in the browser (`src/main.rs:2521` "print" arm, `src/mailview.rs:316`, `src/platform/mod.rs:89-102`) | **Missing** | none | none | **Not needed**: all printing cut, removed in R2 |
| Window chrome | Native frame | **Present.** Hand-drawn title row, caption buttons, snap layouts, Mica, shadow, corners (`docs/CATALOGUE.md:44-45, 136-137`) | none | S | Needed |
| Several windows (pop-out reader and composer, G-055) | Not done; a "Pop out" button shows a toast (`ui/app.slint:3465`) | **Missing** (W1b) | ~~K1, L~~ | ~~M once K1 lands~~ | **Not needed**: pop-outs cut, single window |
| Plugin slots (declarative plugin UI drawn by the host) | Plugin rows in the reader and a send check in the composer (`ui/app.slint:2081`, `:2246-2248`, `:3127-3143`); slots planned as B5 (`ROADMAP.md:52`) | **Missing** | would have been new | would have been M-L (B5) | **Not needed**: plugin platform cut, removed in R1 |
| Light renderer (Blitz) | `src/lite.rs`, shown as a Slint image (`ui/app.slint:3191-3205`) | Would have needed only `Painter::image` | none | ~~S~~ | **Not needed**: cut, removed in R3 |
| Layout of forms and panes | Slint `HorizontalLayout`, `VerticalLayout`, `GridLayout` across about 12,000 lines | **Missing** | K5, M | built into every screen | Needed |
| Buttons, framed fields, check boxes with labels, radios, chips, cards | Slint std-widgets (`LineEdit` 52 uses, `TextEdit` 10, `Button`, `CheckBox`, `ComboBox`) and own components | **Partial.** Dialog buttons, tick box, toggle, drop-down, segments, link (`docs/PURGE-MOCKUP.md:44-69`) | K4, L | built into every screen | Needed. No PIN fields (app lock cut) |
| Multi-line plain text | Slint `TextEdit` (sieve script, signature source, notes, descriptions, the AI chat) | **Missing** | K9, M-L | S per use | Needed, form open (toolkit K9 or the RTE in plain mode). The raw Sieve editor, composer HTML source, signature HTML source and admin raw message are after the switch |
| Scrolling a page of controls | Slint `ScrollView` (28 uses), `Flickable` (15) | **Partial.** `viewport`, scrollbars and `SmoothScroll` exist; no "scroll area" that ties them to a clip and to focus | K11, S | S per page | Needed |
| Settings page | Rows built from a schema in `src/settings.rs` | **Present.** `settings::Form` from rows (`docs/CATALOGUE.md:80`) | none (L10 in review) | M | Needed, smaller: no plugin, app lock or share pages |
| Command palette, tooltips, toasts, dialogs | Own Slint components | **Present.** `palette`, `tooltip`, `overlay::Bubble`, `anim::Stack`, dialog card (body does not scroll, `docs/PURGE-MOCKUP.md:91`) | dialog scroll in K11 | M across them | Needed. No lock screen, PIN, share or plugin consent dialogs |
| Timers | 95 `slint::Timer` uses | **Partial.** One app deadline (`Cx::wake_at`) | part of K1, S | S | Needed (5 fewer with app lock gone) |

---

## 4. Proposed new toolkit items, ranked

Only what Barbican needs. Ranked by how much of the port waits on it. Each would become a row in
the toolkit's `docs/ROADMAP.md`, claimed the usual way. The prefix K is chosen to clash with
neither the toolkit's ids nor the old plugin steps B1-B7. Ids are kept from the first version, so
the rank and the number no longer match.

| Rank | Id | What | Size | Risk | Builds on | Change after the cut |
|---|---|---|---|---|---|---|
| 1 | **K5** | **A layout helper.** Pure arithmetic: rows and columns with fixed, flexible, minimum and maximum sizes, padding, gaps, alignment and wrapping, returning rectangles. No tree is kept, as the toolkit's rule asks | M | low | `gfx::Rect` | was 5; every screen waits on it |
| 2 | **K4** | **Everyday controls.** Push button (primary, secondary, subtle, destructive), icon button with tooltip, button that opens a menu, split button, framed text field (leading icon, clear button, error state), check box with label and a mixed state, radio group, chip with remove, badge and count, card, divider, avatar (initials or picture, round). `feat/classic-widgets` and `docs/PURGE-MOCKUP.md:44-69` list the same gaps | L | low | `overlay::draw_dialog_button`, `check`, `textfield`, `menu::Menu::around`, `link` | was 4; unchanged. "Card" now means dashboard and admin tiles, not structured-mail cards |
| 3 | **K3** | **Native children in the layout.** Rectangles the app lays out each frame are given to child windows (WebView2 through wry): bounds set in the same frame, before the present; shown and hidden by rule; the keyboard focus handed back on a press elsewhere; scale changes; a capture of a child (PrintWindow) for the frames when something must cover it (a modal scrim, a page transition). **More than one child**, or one moved between places: the mail body and the Files preview | M | medium-high | `host`, Barbican's `src/mailview.rs` and `src/capture.rs` as the source | was 3; more needed: Blitz is cut, so the Files previews of .eml and HTML need it too |
| 4 | **K11** | **A scroll area**: one helper that ties `viewport`, both scrollbars, the clip, wheel and keys, and "reveal the focused control"; a dialog whose body scrolls | S | low | `viewport`, `scroll`, `focus`, `overlay` | was 11; unchanged |
| 5 | **K1** | **Host additions for one window** (shrunk). Several named timers folded into one wake-up; the main window hidden but alive (close to tray is kept); regions with their own damage, so a caret blink and a list scroll do not repaint one union rectangle. **Dropped:** opening and closing top-level windows at run time, tabs, an animator and a11y adapter per window, events tagged by window | M (was L) | medium (was high) | `host`, the damage part of W1b | was 1; pop-outs are cut, so only the single-window parts remain. The popup surface moves to K2 |
| 6 | **K7** | **Rich text through the painter.** A public way to draw a caller's parley layout (with per-run brushes), a wavy underline as one path (spell squiggles, JANK #18), and mixed families per run. Barbican moves its RTE to parley 0.12 at the same time | S | low | `gfx.rs:670` `draw_layout` | was 7; unchanged |
| 7 | **K8** | **Drag and drop** (= L7): a threshold, a drag image, drop targets that light up, insert position between rows, auto-scroll; files hovered and dropped from the system reduced to host events | M | medium | `rows`, `sections`, `select`, `anim`, `host` | was 8; unchanged. Second cut session: all four drags and Explorer drops kept |
| 8 | **K9** | **Multi-line plain text area**: wrapping, scrolling, selection, undo, IME, find. mdedit has an editor of its own (`crates/mdedit-core/src/lines.rs`, `edit.rs` in markdown-editor-rs) that could be a source | M-L | medium | `textfield`, `viewport`, `seltext` | was 9. Second cut session: the form is open (this item, a smaller M without find, or Barbican's RTE in plain mode with no K9). Four source editors are after the switch, but notes, descriptions, the AI chat and the vacation, template and signature bodies still need multi-line text by U6 |
| 9 | **K14** | **Pictures kept at their drawn size**: an image prepared once at device size and blitted after (`Painter::image` scales on every draw), plus a decoder slot an app can fill (Barbican's GIF, WebP, ICO, SVG) | S | low | `mdshared_text::image`, `thumbs` | was 14; ranked up, avatars and BIMI badges are on most screens |
| 10 | **K2** | **Popups in owned surfaces.** The host gains one kind of extra window only: borderless, owned by the main window, never activated, no taskbar entry, its own bitmap and present, its events routed to the popup's model. Menus, cascades, flyouts, drop-down lists, tooltips and toasts drawn into it, placed in screen coordinates, kept on the monitor's work area, with the DWM corners and shadow on Windows 11 (or per-pixel alpha), closed when the owner moves, loses activation or is minimised. Same models and `draw_*` functions, only the target changes. This is what puts a menu above the WebView2 child | L | high | `host` (not K1), `menu`, `submenu`, `flyout`, `dropdown`, `overlay`, `anim::presence` | was 2; now carries its own small window piece instead of waiting on all of K1. Ranked down: the capture trick covers the switch, so K2 can land after it. **Second cut session: a future item, after the switch.** The capture is the plan |
| 11 | **K12** | **Accessibility past Purge's scope**: text ranges for the editor and fields, tree items (folders), a grid (calendar), menus in their own surfaces (with K2), live regions for toasts and the notification centre; tried with Narrator | L | high | `a11y`, K2 | was 12. **Second cut session: a future item, after the switch.** Keyboard navigation and focus stay in the port; the screens build no tree until K12 |
| 12 | **K10** | **IME on Windows**: allowed while a text surface has focus, the candidate window placed at the caret (`set_ime_cursor_area`), preedit drawn in fields and in a caller's rich text | S | medium | `textfield::Preedit`, `host` | was 10; ranked down. The UI is English only and nothing on the plan asks for CJK input. The emoji panel (Win+.) and dead keys must still type; whether that needs IME on is not known without building |
| 13 | **K13** | **A time-grid model** (optional; better kept in Barbican, since no other toolkit app needs it): day and week columns, 15-minute snapping, packing overlapping events into lanes, move and resize by drag, an all-day strip with "show more", month cells | M | low | `grid`, K8 | was 13; unchanged. Find a time is cut but never used the grid. Second cut session: event move and resize kept, so the drag part stays |
| 14 | **K6** | **A whole skin as data** (**optional, Later**, with themes). Faces and families as run-time values (today `const UI_FAMILY`, `gfx.rs:108-127`); every widget reading its sizes from `tokens::Sizes` (today they keep constants, `tokens.rs:24-25`); a `Look` for every widget that lacks one; the icon set as a replaceable table; one `Skin` value passed to `_with(skin)` variants of the `draw_*` functions. Opt-in, as the compatibility rule asks. Section 6 | M-L | medium | `tokens`, `appearance`, every `draw_*` | was 6; themes are Later, and one look needs none of it (6.4) |

**What the cut dropped or shrank, item by item:**
- **K1, from L to M.** It was there for two things: pop-outs (G-055, cut, `docs/FEATURE-CUT.md:39`)
  and as the base for K2. A popup never takes focus, has no title row, no snap and no taskbar
  entry, so it does not need a general multi-window host. K2 now asks the host for one kind of
  owned, non-activating surface and nothing more. What is left of K1 serves the one main window:
  named timers (Barbican has 95 `slint::Timer` uses), a window hidden while alive (close to tray,
  `src/desktop.rs:35-44`), and damage regions (the CPU speed risk, 5.6).
- **K2, same size, fewer dependencies.** It absorbs the owned-surface piece. It is still L and high
  risk, because of DWM shadows, DPI across monitors and WebView2's focus habits.
- **K3, more needed.** WebView2 now shows the Files previews too (5.3).
- **K6, optional.** One look needs only the palette and `appearance` the toolkit has (6.4).
- **K10, lower.** English only.
- **K12 and K13, unchanged.** Everything in them serves kept features.
- **K4, K5, K7, K8, K9, K11, K14, unchanged.** The cut removes a few uses (PIN fields, the share
  dialog), none of the widgets.

**What the second cut session changed, item by item** (`docs/FEATURE-CUT.md`, "Second session"):
- **K2 and K12, after the switch.** Neither is cut: both are future items, outside the switch path.
  Together they were about 2,000-6,000 lines and most of the risk. The port keeps keyboard
  navigation and focus (W2) and the capture trick (5.3).
- **K9, open.** Its form is decided before U6. The raw Sieve editor, composer HTML source,
  signature HTML source and admin raw message fields wait for after the switch.
- **K8 and K13, unchanged.** Every drag was kept.
- **App drawing, not K items:** the pop-up date picker, the typed time field, hover actions and
  colour swatches wait for after the switch (typed date field, 15-minute drop-down, the Actions
  button, a drop-down of named colours until then). Loading skeletons are cut (the spinner
  instead). Chip fields outside the composer reuse the composer's.

Also wanted, already on the toolkit's roadmap: L5 (rename in place), P8b (link hand-over to the
running app). L9 (images on the clipboard, for pasting a screenshot into the composer) is after
the switch since the second cut session.

**No longer wanted:** W9 (translations; English only), X6 (Barbican keeps its own `g` sequences),
L4 (list columns cut), W1b beyond its damage regions (pop-outs cut; Linux is Later).

Not proposed: printing (cut); notifications and the tray (the toolkit has them, and Barbican's own
work); spell checking (Barbican's `src/rte/spell.rs` over ISpellChecker stays).

**Rough total before the switch** (second cut session): K1, K3, K4, K5, K7, K8, K9, K10, K11 and
K14 come to about 1 L, 5 M and 4 S on the toolkit's scale, roughly **6,500 to 10,500 lines** with
tests (8,000-13,000 after the first cut, for K1-K11 without K6; 10,000-16,000 before any cut).
K13 is optional and may stay in Barbican. K2 and K12, which carried most of the risk, come after
the switch; K9's size depends on the decision still open.

---

## 5. Migration plan

### 5.1 Size of the port

Counted at `b00812d`, before the R removals land. "Cut" is what R1-R3 delete, or what is cut and
so is not ported. Counts are `grep` (see the appendix), so treat them as "about".

**The Slint side** (`ui/`):

| Measure | Before the cut | Cut | To port |
|---|---|---|---|
| Files, lines | 19 files, 12,654 lines; `ui/app.slint` alone 6,382 | `ui/applock.slint` (254) and `ui/share.slint` (271) whole; about 110 lines in `ui/app.slint`, `ui/calendar_ext.slint` and `ui/theme.slint`: about 640 lines | 17 files, about 12,010 lines |
| Components | 146 | 8 (`LockButton`, `LockCard`, `LockScreen`, `PinField`, `PinDialog`, `ShBtn`, `Initial`, `ShareDialog`) | 138 |
| Structs, globals | 64 structs, 27 globals | 3 structs (`ShareRow`, `SharePerson`, `CxSlot`), 2 globals (`AppLock`, `ShareX`) | 61 structs, 25 globals |
| Properties | 1,399 declared, 1,199 of them `in`/`out`/`in-out` | about 60: app lock 32, share 19, plugin rows and send check 2, light renderer 2, Find a time 4, the print icon 1 | about 1,340 |
| Callbacks | 368 | 23: app lock 12, share 6, plugins 2 (`plugin-watch`, `cancel-send-check`), `lite-click`, Find a time 2 (`ed-find`, `ed-pick-slot`) | 345 |
| Popups, lists | 17 `PopupWindow`; 14 `ListView`, 28 `ScrollView`, 15 `Flickable`, 120 `for` | none of the popups; a few `for` loops (plugin rows, suggested slots) and one `ScrollView` (light mode) | about the same |
| Input | 220 `TouchArea`, 15 `FocusScope`, 52 `LineEdit`, 25 `TextInput`, 10 `TextEdit` | 11 `TouchArea` and 3 `FocusScope` in the two cut files, 3 PIN `LineEdit`s, a few `TouchArea`s in `ui/app.slint` and `ui/calendar_ext.slint` | about 205, 12, 49, 24, 10 |
| Motion, a11y | 69 `animate`; 115 `accessible-*` | none of either in the cut files | about the same |
| Screens | mail, calendar, contacts, files, dashboard, settings, composer, app lock, dialogs | the lock screen and PIN dialog, the share dialog, the plugin rows, the light body, Suggested times | mail, calendar, contacts, files, dashboard and admin (read-only), settings, composer, dialogs |
| Two styles | 607 `Theme.nordic`/`Theme.bento` tests on 581 lines | not cut, but the port draws **one look** (section 6.4) | one branch of each |

**The Rust side** (`src/`):

| Measure | Before the cut | Cut | After the cut |
|---|---|---|---|
| Files, lines | 116 files, 85,072 lines | 9 files deleted, 10,297 lines: the six `plugin*.rs` (7,705), `applock.rs` (1,762), `share.rs` (754), `lite.rs` (76); plus the wiring in kept files (at least 490 lines name cut code: `main.rs` 164, `compose.rs` 114, `settings.rs` 84, `settings_sync.rs` 23, `signout.rs` 20, `cal_editor.rs` 15, `oauth.rs` 13, `ctxmenu.rs` 12, others fewer) | 107 files, about 74,300 lines |
| Files with no Slint | 58 files, 33,664 lines | 5 files, 4,391 lines (`plugins.rs`, `plugin_package.rs`, `plugin_catalog.rs`, `plugin_catalog_check.rs`, `lite.rs`) | **53 files, 29,273 lines** |
| Files that touch Slint | 58 files, 51,408 lines | 4 files, 5,906 lines (`plugin_host.rs`, `plugin_hooks.rs`, `applock.rs`, `share.rs`) and the wiring | **54 files, about 45,000 lines** |
| Binding calls (`.set_*`, `.get_*`, `.on_*`) | 2,651 (1,558 + 823 + 270; a few are not Slint's) | 155 in the deleted files (`applock.rs` 103, `share.rs` 31, `plugin_host.rs` 12, `plugin_hooks.rs` 4, `plugins.rs` 3, `lite.rs` 2); 40 to 80 in kept files | **about 2,440** |
| Callback handlers (`.on_*`) | 270 | 19: `applock.rs` 9, `share.rs` 5, and `on_lite_click`, `on_cancel_send_check`, `on_plugin_watch` (`src/main.rs:2001, 2124, 2126`), `on_ed_find`, `on_ed_pick_slot` (`src/cal_extra.rs:981-982`) | 251 |
| Globals, models, timers | 285 `global::<`, 339 `ModelRc`/`VecModel`, 95 `slint::Timer` | `applock.rs` 21, 2, 5; `share.rs` 8, 13, 0 | about 256, 324, 90 |
| Window wiring | `main()` from `src/main.rs:1172` to `:2645`, about 1,470 lines; three winit event filters (`src/motion.rs:48`, `src/mailview.rs:328`, `src/rte/mod.rs:817`) | the plugin, lock, print and light-mode arms in it | about 1,300 lines |
| Heaviest UI files | `main.rs`, `cal_editor.rs`, `cal_extra.rs`, `compose.rs`, `contacts_x.rs`, `applock.rs`, `ctxmenu.rs` | `applock.rs` goes; `compose.rs` and `main.rs` lose the plugin wiring | `main.rs`, `cal_editor.rs`, `cal_extra.rs`, `compose.rs`, `contacts_x.rs`, `ctxmenu.rs` |
| Tests | 1,201 `#[test]`, nearly all on logic | the cut files' own tests | |

**What the counts say.** The cut takes about 5 % of the Slint and about 8 % of the binding calls
out of the port. That is small, because most cut features were small on screen. The plugin
platform was 7,700 lines but only about 30 lines of Slint: its settings page and consent are
built from Rust (`src/settings.rs`, `src/main.rs`) on the generic settings rows and dialogs, and
no `.slint` file names them. The real saving is **work that was never started** and now
never will be on this path:

| Unbuilt work no longer needed | Size it would have had |
|---|---|
| Plugin slots drawn by the new UI (B5), host calls (B6), proof plugins (B7), the rest of B4 | L + L + M + L in Barbican |
| Pop-out reader and composer (G-055, U13) and the multi-window half of K1 | M in Barbican; about L to M in the toolkit |
| Translations (W9) and the time zone picker | L in the toolkit; L + S in Barbican |
| List columns (L4, G-062), category tabs, structured-mail cards | M in the toolkit; M in Barbican |
| Printing of calendars and contacts, RTL editing, the tour, offline and outbox, delegation and Pro, admin writes, Files sync and WOPI | S to L each, in Barbican |

Rough size of the new view code: Slint is dense. The same screens drawn in Rust with a layout
helper will likely take 1.5 to 2.5 times the lines: about **18,000-28,000 lines** of view code
(20,000-30,000 before). Drawing one look instead of Bento and Nordic keeps it nearer the low end.

### 5.2 Step by step, or big bang?

**Not step by step inside one binary.** Slint's winit backend creates the event loop and the
window (`src/main.rs:1182`, `:1195`, `:2645`). winit allows one event loop per process, and the
toolkit's host wants to own it too. So a Slint window and a toolkit window cannot live side by side
in one process. The toolkit's Win32 "hosted mode" (`docs/ROADMAP.md:45-48`) could draw a toolkit
pane inside the Slint window as a child HWND, but that child would hide Slint's popups exactly as
the WebView does now, and the app would wear two looks at once. Not worth it.

**Instead: remove the cut code, then a second front end over the same logic, then one
switch-over.** Every step still leaves a usable app, because the Slint app keeps shipping until the
switch:

0. **Remove the cut code first (R1-R3).** Each removal shrinks `main.rs`, `compose.rs`,
   `settings.rs` and `ui/app.slint`, which the next steps edit everywhere. The generator in step 1
   reads the `.slint` declarations, so it should run after the removals, or it writes stand-ins for
   API that is about to go.
1. **A stand-in for `AppWindow`.** A script (`tools/gen-viewmodel`) reads the `ui/*.slint`
   declarations (properties, callbacks, structs, globals) and writes a plain Rust `AppWindow` with
   the same method names: `set_x`, `get_x`, `on_x`, `invoke_x`, `global::<Pd>()`, `as_weak`,
   `upgrade_in_event_loop`. Fields hold the values; a setter marks the view dirty. A cargo feature
   picks which `AppWindow` the logic sees: Slint's generated one or the stand-in. Most of the
   2,440 binding call sites then compile unchanged in both builds. The Slint-only APIs used from
   Rust (`slint::Image`, `SharedPixelBuffer`, `Timer`, `Color`, `with_winit_window`,
   `dispatch_event`, `take_snapshot`) get small wrappers of Barbican's own.
2. **Move logic out of the `.slint` files.** Hundreds of properties carry binding expressions, for
   example `body-visible` and `any-overlay` (`ui/app.slint:2263-2271`) and the pane width maths
   (`ui/app.slint:1750-1768`, JANK #11). The new UI has no bindings, so each becomes a Rust function
   that both front ends call. This is done on the Slint app first, so it ships and is tested there.
   R2 makes `body-visible` simpler: its `!AppLock.locked` term goes (`ui/app.slint:2264-2265`).
3. **Build the new front end screen by screen** behind the feature, mail first. The owner can use
   it for mail while calendar and contacts are still on the Slint build.
4. **Switch over** once every screen is there: the feature becomes the default, then Slint, its
   build step and the stand-in's Slint path are deleted.

Cost of this route: for some weeks a UI change must be made twice. So UI feature work on the Slint
app stops at the start of the port, except fixes. Kept features that are not built yet (reading
pane position, toolbar position, the account switcher, @mentions, priority, proofreading, pinned
messages, the hover tag picker) are built on the new UI, not on Slint (5.5, 5.7).

### 5.3 The WebView and popups

**Two homes, one engine.** With Blitz cut (`docs/FEATURE-CUT.md:29`), every HTML view is WebView2:

- the mail body, as now (`src/mailview.rs:792-836`);
- after R3, the Files previews of .eml and HTML files, which Blitz paints today
  (`src/files_view.rs:967-968`). The preview is a full-window overlay (`ui/app.slint:6378`).

The preview shows untrusted content, so it must go through the same sanitizer, CSP and no-script
shell as the mail body. R3 decides whether it is a second WebView2 controller (sharing the one
environment and profile folder, `src/mailview.rs:776-780`) or the reader's one moved. Either way
K3 must handle more than one place. The plain-text view (render mode 2) stays native.

**Day one (no K2 or K3 yet).** Keep the WebView2 child exactly as now. wry attaches it to the host's
window through `Cx::window()` (`host/cx.rs:165`). Popups draw in the bitmap, and the capture trick
(`src/mailview.rs:7-9`, `:861-870`; `src/capture.rs`) stands in for the body while one is open.
Two things get better at once:
- Every overlay is now plain state the app draws last, so "something covers the body" is one Rust
  function, not a Slint flag that misses half the popups (JANK #2, #13).
- The host hands every winit event to one `App::raw` (`host.rs:291-296`), so the three competing
  event filters become one (JANK #1).
Still to fix in Barbican: capture only when an overlay opens, and only the body's rectangle (JANK #3).

**With K3.** Each child's rectangle comes from the frame's layout, and its bounds are set before
the present, in the same frame (JANK #9; no 50 ms timer, `src/main.rs:2634`). The Files preview
gets the same placement for free.

**With K2 (after the switch, second cut session).** Until then the capture trick above is the
plan, not a stopgap. Menus, submenus, drop-downs, flyouts, tooltips and toasts open in owned surfaces above
the WebView child, so the WebView stays shown and live under a menu. This no longer waits on a
multi-window host (section 4). Modal dialogs keep the capture: a modal covers the whole window with
a scrim and blocks the body anyway, so a still picture under it costs nothing in use. The Files
preview is itself modal, so menus inside it (its toolbar, its context menu) are the only new case.

**No fallback any more.** The first version kept Blitz as a "light" alternative that the painter
could show with no popup problem. That is gone. If the WebView2 runtime is missing or broken, the
only HTML view left is plain text. The "Show messages with" setting loses its light choice (R3).

**Linux note.** wry's `build_as_child` works on X11 but not on Wayland, and WebKitGTK needs GTK
pumped from the loop (Barbican does this from Slint's loop today). The host would need a hook per
loop turn. Linux is "Later", so this waits for that decision.

### 5.4 What survives

**Unchanged (no Slint today, and kept):** `jmap.rs`, `oauth.rs`, `auth.rs`, `settings_sync.rs`,
`invite.rs`, `vacation.rs`, `vcard.rs`, `sieve.rs`, `bimi.rs`, `identities.rs`, `signout.rs`,
`templates.rs`, `phish.rs`, `spam.rs` (its verdict now always shows, R1), `ai.rs`, `agent.rs`,
`copymsg.rs`, `recurrence.rs`, `recipients.rs`, `feed_guard.rs`, `updater.rs`,
`release_manifest.rs`, `notify.rs`, `secrets.rs`, `bodycache.rs`, `zip.rs`, `trust.rs`,
`masked.rs`, `quota.rs`, `otp.rs`, `a11y.rs` (it reads system settings), `platform/*` (minus the
print file, R2), and the RTE core `rte/{model,editor,html,layout,spell}.rs`. After the removals that
is 53 files and 29,273 lines, with their tests. `oauth.rs`, `settings_sync.rs` and `signout.rs`
lose their app lock and plugin parts in R1 and R2 (`src/oauth.rs:627-688` holds the lock's marker).

**Deleted, not ported:** `plugins.rs`, `plugin_host.rs`, `plugin_package.rs`, `plugin_hooks.rs`,
`plugin_catalog.rs`, `plugin_catalog_check.rs` (R1); `applock.rs`, `share.rs` (R2); `lite.rs`
(R3). In the first version `plugins.rs`, `plugin_package.rs`, `plugin_catalog.rs` and `lite.rs`
were on the unchanged list, and `applock.rs` and `plugin_host.rs` on the next one.

**Mostly unchanged behind the stand-in:** the logic halves of `mail.rs`, `reader.rs`,
`compose.rs`, `calendar.rs`, `cal_editor.rs` (without Suggest times, R2), `contacts*.rs`,
`files*.rs` (with the Files share flyout, which stays), `ctxmenu.rs` (without the Share and Print
entries), `shortcuts.rs`, `desktop.rs`, `livesync.rs`, `notices.rs` (share notices from others
still show), `unified.rs`.

**Rewritten:** all of `ui/*.slint` (as Rust drawing), the glue in `rte/mod.rs`, `mailview.rs`'s
placement code, `files_view.rs`'s preview placement (a WebView rectangle after R3, not a bitmap),
`motion.rs`, `capture.rs`'s caller, `winstate.rs` (the toolkit's `window_state` can replace it),
`dnd.rs`'s pointer side, `automation.rs`'s input injection and screenshots.

**Easier after the port:** the automation channel. Input goes straight into `App::event` and a
screenshot is the bitmap itself (`Bitmap::write_png`, `docs/CATALOGUE.md:17`), so `shot` and most
of `wshot` need neither Slint's `take_snapshot` nor PrintWindow. `render-mode 1`
(`src/automation.rs:41, 463`) goes with Blitz.

**Must be kept:** `build.rs` compiles Slint and also embeds the icon and the VERSIONINFO resource
that Hub reads (`build.rs:1-7`). At the switch, delete only the `slint_build` lines. Read Hub's
COMPLIANCE.md before touching it (CLAUDE.md, "Hub").

### 5.5 What happens to the current roadmap

- The settings cache (JANK batch B) is merged (`a151af0`). The backend parts of the jank list (#5-#8,
  #15-#17, #19) are independent of the toolkit. Do them on the Slint app; they carry over.
- JANK batch A (WebView, overlays, focus) is solved by the port with K2 and K3. Do only the S fixes
  that hurt today.
- JANK batch G and `ROADMAP.md:39-42` ("Bulwark's look becomes the default theme") are superseded:
  the look now comes from the toolkit, and the font pass (G-092) is cut. The Bulwark palette work
  (G-070 part 1) stops; themes are Later.
- The mail layout step (`ROADMAP.md:32-37`) loses list columns (cut). Reading pane position and
  toolbar position are kept and built in the new shell (U4), not on Slint.
- The shell step (`ROADMAP.md:43-47`) loses the phone-width layout, pop-outs and sidebar apps (all
  cut). The sidebar that folds to icons goes into U4.
- The plugin track (`ROADMAP.md:52`), Phase 7 (`ROADMAP.md:53`) and printing (`ROADMAP.md:55`) are
  gone, apart from what R1-R3 delete.
- Tab focus and rings (JANK #23) come with the toolkit's `focus`.

### 5.6 Risks

| Risk | Why | What to do |
|---|---|---|
| CPU rendering speed | Every pixel is drawn on the CPU. A full repaint of a 4K window at 150 % touches about 33 MB per frame, and the host joins all damage into one rectangle (`host.rs:24-30`). Barbican has busy screens and many animations | Unknown without building. Spike first (S1): a 10,000-row list scrolled, and a menu fade, on a 4K monitor; set a frame budget as Purge did (C1, `docs/ROADMAP.md:171`). The damage regions stay in the shrunk K1 for this reason |
| Popup surfaces on Windows | Owned, non-activating popups with DWM shadows, across monitors with different DPI, and WebView2's own focus habits | Out of the switch path (second cut session): K2 is a future item and the capture trick is the plan. Spike K2 when it is taken up |
| WebView2 is the only HTML view | Blitz is cut. The mail body and the Files previews both need WebView2, and untrusted HTML files now reach it from Files too | R3 puts the previews through the mail sanitizer and CSP, with tests. Plain text when the runtime is missing. K3 handles two places |
| A young toolkit with other priorities | Its order of work is Purge, mdplay, mdmix, a file manager, a terminal (`docs/ROADMAP.md:50-60`); Barbican is not on it | The owner adds the K items to the toolkit's roadmap and orders them. The list is shorter now (K1 smaller, K6 optional, W9, X6, L4 gone). Barbican pins a commit |
| Logic hidden in Slint bindings | Hundreds of bound properties; some hold real rules (overlay flags, widths) | Step 2 of 5.2 moves them to Rust while still on Slint, with tests. Fewer after R2 (no lock terms) |
| Removal and port prep in the same files | R1-R3 run in parallel and rewrite `main.rs`, `compose.rs`, `settings.rs` and `ui/app.slint`; U1-U3 touch the same files everywhere | Start U1 once R1 has merged, and U2 once R1-R3 have merged. The generator then never sees cut declarations |
| Accessibility regresses | Slint builds an AccessKit tree for its own widgets; with the toolkit the app builds every node itself, and text ranges are missing (`a11y.rs:69`) | **Accepted** (second cut session): screen readers are a future item, so the new UI ships with keyboard navigation and focus but little for a screen reader. When K12 is taken up, every screen gets its tree in one pass. Keep the screens' state in models so that pass stays mechanical |
| IME untried on Windows | `window.rs:82-90` | Lower than before: English only. K10 in the composer milestone; check the emoji panel and dead keys there. A CJK test only if the owner asks |
| Two parley versions | Toolkit `=0.12.0`, RTE `=0.11.1` | Move the RTE to 0.12 in the composer milestone (K7) |
| Doing UI twice | Months of two front ends | Stop Slint UI features at the start; the stand-in keeps logic shared; kept-but-unbuilt features go on the new UI only |
| Toolkit rules vs Barbican | "No bundled fonts" (`docs/PLAN.md:101`); "add, do not change" | Matters only if themes with their own fonts come back (Later). K6 must then be opt-in |
| Linux WebView | wry children need X11; GTK must be pumped | Later, with the Linux decision |

### 5.7 Milestones

Each milestone is sized for one review. "R" milestones remove cut code in Barbican, and are
already being worked on in parallel. "U" milestones are in Barbican, "T" milestones in
markdown-shared-rs, "S" is a spike in both. T and U run side by side where the dependencies allow.
Sizes are Barbican's for R, U and S, and the toolkit's for T.

| Id | Where | Work | Needs | Size | Done when |
|---|---|---|---|---|---|
| R1 | Barbican | Remove the plugin platform: the six `plugin*.rs` files, `plugins/spam-score/`, `spec/plugins/`, `PLUGINS.md`, the wiring in `main.rs`, `compose.rs`, `settings.rs` and the hooks elsewhere, the plugin settings page and consent dialog, plugin rows and the send check in `ui/app.slint`. `spam::analyze`'s verdict always shows (`docs/FEATURE-CUT.md:109-114`) | none | M | `cargo test` and the privacy check pass; no `plugin` left outside the changelog |
| R2 | Barbican | Remove app lock (`src/applock.rs`, `ui/applock.slint`, the Security rows, idle and session-timeout wiring, the marker in `src/oauth.rs`), sharing of folders, calendars and address books (`src/share.rs`, `ui/share.slint`, the Share entries; the Files share flyout stays), all printing (the print paths in `src/mailview.rs` and `src/platform/mod.rs`, the Print menu entry and shortcut), and Find a time (Suggest times and `Principal/getAvailability`, `src/cal_editor.rs:1227-1332`, and its panel in `ui/calendar_ext.slint`) | none | M | `cargo test` and the privacy check pass; sign-out and start-up never ask about a lock |
| R3 | Barbican | Remove Blitz: `src/lite.rs`, the four `blitz-*` crates (`Cargo.toml:45-48`), the light mode in `src/main.rs` and `src/mailview.rs`, the light choice in "Show messages with". The Files preview of .eml and HTML moves to a WebView2 preview through the mail sanitizer and CSP, replacing `render_doc` (`src/files_view.rs:967`) | none | M | no `blitz` in `Cargo.lock`; .eml and HTML files preview in Files; a hostile HTML file runs no script |
| U0 | Barbican | The backend jank fixes left (JANK #5-#8 data side, #15-#17, #19) on the Slint app. The settings cache is done (`a151af0`) | none | M | as in JANK-AUDIT.md |
| U1 | Barbican | Barbican-owned wrappers for `slint::Image`, `Color`, `Timer`, `SharedString`, `Weak` and the winit accessors; Slint app unchanged on screen | R1 | M | `cargo test` passes; the app looks and behaves the same |
| U2 | Barbican | `tools/gen-viewmodel` and the stand-in `AppWindow` behind feature `ui-mdshared`; the logic compiles against both | U1, R1-R3 | M | both feature sets build; a test compares the generated API with Slint's |
| U3 | Barbican | Bound expressions moved from `.slint` to Rust functions, both front ends call them | U2 | M | every moved rule has a test; the Slint app unchanged |
| T1 | toolkit | K5 layout helper, K11 scroll area, K7 rich text drawing, K14 pictures | none | M + S + S + S | the toolkit's CI; a specimen sheet |
| T2 | toolkit | K4 everyday controls | T1 | L | pixel tests and a sheet; seen by the owner |
| T3 | toolkit | K1 (shrunk): named timers in one wake-up, the window hidden but alive, regions with their own damage | none | M (was L) | damage tests; the host example blinks a caret while a list scrolls, with two damage regions |
| S1 | both | Spike: the new shell with a 10,000-row list and a WebView2 child, measured on a 4K monitor | T1, T3, U2 | S | frame times written down; the owner looks at it |
| T4 | toolkit | K3 native children in the layout (more than one) | T3 | M | two child windows placed by layout in the example; a capture under a scrim; DPI across two monitors |
| U4 | Barbican | New shell: host window, title row, rail, sidebar that folds to icons, folder tree, account switcher, mail list, search field, **reading pane position (right or off; bottom is cut) and toolbar position**; usable read-only | S1, T2 | L | browse folders and messages in the new UI, in each pane position |
| U5 | Barbican | Reader: header, actions, banners, attachments, the WebView child placed by K3, capture under in-window popups, context menus | U4, T4 | L | read, move, tag, reply buttons work; the automation channel's reader probes pass |
| U6 | Barbican | Composer: RTE drawn by the painter on parley 0.12, squiggles, recipients and chips, composer popups, files dropped from the system, IME on (K10), the AI assistant panel. Not here (after the switch): the HTML source view, pasting images (L9), the pop-up date picker in schedule send | U5, T1, K10, K9 (or the RTE in plain mode: decide first) | L | write and send with an inline image from a file and a misspelling; the emoji panel types into the body |
| U8 | Barbican | Settings (from `src/settings.rs`'s schema on `settings::Form`, without the plugin, app lock and share pages, and without the raw Sieve editor, signature HTML source and admin raw message fields until after the switch), notices and the notification centre, update banner, command palette, shortcuts sheet, dialogs | U4 | L | every settings page opens and saves |
| U9 | Barbican | Calendar: day, week, month, agenda grid, tasks, drag to move and resize, event editor without Suggest times (with K13 or app code); typed date fields and a 15-minute time drop-down (the pickers come after the switch) | U8, K8 | L | the calendar rows of `spec/parity.csv` still pass |
| U10 | Barbican | Contacts, Files (with the WebView2 preview from R3, placed by K3, and the share flyout), dashboard and admin read-only views, filters | U8, T4 | L | their parity rows still pass |
| U11 | Barbican | Automation channel on the new UI; jank re-audit. The accessibility tree and K12 are after the switch (second cut session) | U10 | S (was M) | the automation probes pass on the new UI; Tab reaches every control; the audit has no high item |
| U12 | Barbican | Switch-over: new UI by default, Slint and `slint_build` removed, `build.rs` keeps the version resource, exe size and first frame measured, release | U11 | M | installer run on a VM by the owner; Hub offers the update |
| T5 | toolkit | K2 popups in owned surfaces. **After the switch** (second cut session) | T3 | L | a menu over a child window in the example; DPI across two monitors; no focus taken from the owner |
| U7 | Barbican | Popups moved to K2 surfaces; the WebView stays live under menus. **After the switch**; before it, U5 fixes JANK #3 (capture on open, only the body), and K3 (T4) removes the 50 ms sync timer (JANK #9) | U5, T5 | M | JANK #2 gone, checked by a `burst` capture |
| ~~U13~~ | | ~~Pop-out reader and composer windows (G-055) on K1~~ | | | **Dropped**: pop-outs cut |

Kept features not yet built (@mentions, priority and classification, proofreading, pinned messages
and the hover tag picker, the missing account security rows) go on the new UI after the screen
they belong to is ported. They are not in the length below.

**Rough length** (second cut session). R1-R3 are about 2 to 3 weeks, already under way, beside U0
and T1. The U path after them is about **3 to 4 months** of agent work with one worker and one
reviewer: 3 M of prep, 6 L screens (U4-U6, U8-U10, each a little lighter: no accessibility tree,
no source editors, pickers or hover actions), U11 at S and U12 at M. The T path is about **4 to 6
weeks** without T5 and runs beside it. In all, about **3.5 to 4.5 months** from now (4 to 5 after
the first cut, 4 to 6 before it). The saving comes from U13 and half of K1 gone, smaller settings
and dialog screens, one look instead of two, the removal work done before the port, and now K2,
T5, U7, K12 and most of U11 moved after the switch. These are guesses from the counts, not
measurements.

**After the switch** (future items from the second cut session, in no order and not in the length
above): T5 and U7 (K2 popups, L + M); K12 and the accessibility tree for every screen (L + M);
L9, image paste (M); the raw Sieve editor, composer and signature HTML source and admin raw
message fields (with K9); the pop-up date picker, the typed time field, hover actions and colour
swatches (S each); L8, dragging out (optional).

---

## 6. A complete skinned look

The question: can the whole look (colours, metrics, shapes, fonts, icons, maybe per-widget
overrides) be one theme or skin value, switched while the app runs? Themes are "Later"
(`docs/FEATURE-CUT.md:40`), so this section is kept for that decision. The port does not wait on it.

### 6.1 With the toolkit as it is

| Part of the look | Themeable today? | Evidence |
|---|---|---|
| Colours | **Yes, at run time.** Every `draw_*` takes a `&Palette`; pass another one and the next frame uses it | `theme.rs:8-40`; `docs/CATALOGUE.md:23` |
| Accent, high contrast, schemes | **Yes.** `tokens` and `appearance` (dark, light, warm, cool; accent from theme, system, cover or a colour) | `tokens.rs:1-30`; `appearance.rs:1-35` |
| Corners, shadows, menu rows, motion speed | **Partly.** `Appearance` holds them; menus take `menu::Look`; seven other widgets have a `Look`; the settings form has a `Style` | `appearance.rs:1-35`; `menu.rs:179` |
| Metrics (row heights, paddings, text sizes) | **No.** `tokens::Sizes` names them, and `Sizes::scaled` exists, but the widgets "keep their constants"; about 96 public `f32` constants sit across the modules | `tokens.rs:24-25, 95`; `grep 'pub const .*: f32'` |
| Fonts | **No.** Three faces from compile-time family lists (Segoe UI Variable, Cascadia Mono, ...) | `gfx.rs:76-127` |
| Icons | **Partly.** `Painter::icon` takes any SVG path string, so an app can bring its own set; the built-in set is constants; icons are strokes only | `gfx.rs:802-803`; `glyph.rs:1-10` |
| Shapes of a widget (a button's form, a field's frame) | **Mostly no**, beyond the `Look` structs | widget sources |

So today: **partly**. Colours, accents and a few widget looks switch at run time. Sizes, fonts and
most shapes do not.

One thing is in the toolkit's favour. It keeps no widget tree. A frame is drawn from the values the
app passes in, so a run-time switch is only "the next frame uses other values". No restyling pass,
no invalidation, no recompile.

### 6.2 With the proposed items

With **K6** (a `Skin` value of palette, sizes, fonts, icons and per-widget looks, read by
`_with(skin)` variants of every `draw_*`) plus **K4** (the everyday controls drawn from the same
skin), the answer is **yes**: one value describes the whole look, and switching it costs one
redraw. A skin can also be loaded from a file, because it is plain data.

Two conditions:
- Fonts outside the system's own need the owner to lift the toolkit's "no bundled fonts" rule
  (`docs/PLAN.md:101`).
- Barbican's own screens must take every size from the skin, not from literals. The jank audit
  found about 25 hard-coded font sizes in today's Slint (JANK #21, #22); the port is the moment to
  stop that.

K6 is **optional and Later**. The toolkit does not cover it today, and nothing kept needs it (6.4).

### 6.3 Against Slint, briefly

- Slint's std-widgets style (fluent, material, cupertino, cosmic) is chosen when the app is
  compiled (`build.rs:12`, `with_style("fluent")`). Its colour scheme can change at run time, its
  widgets' shapes cannot.
- Barbican's own Slint components read a `Theme` global, and those tokens do switch at run time:
  the automation channel's `ui bento|nordic dark|light` command does it in memory
  (`src/automation.rs:15`). Fonts and sizes there are tokens too, where the code uses them.
- So for Barbican's own components the two are close. The toolkit goes further once K6 lands:
  every pixel is Rust code that reads the skin, std widgets included, and no style is fixed at
  compile time. Slint goes further today on fonts and sizes, because its tokens already cover them.

### 6.4 Themes are Later: what the decision would change

What the port needs **without** themes, and what covers it today:

| Kept setting | Covered by | Toolkit work |
|---|---|---|
| Light, dark, follow the system | `Palette` `LIGHT`/`DARK`, `platform` dark mode, `tokens` high contrast | none |
| Text size (`fontSize`, read by `src/a11y.rs:180`) | the painter's scale: logical px times the text size factor, as a browser zoom does | none. Text-only scaling (bigger text, same paddings) would need the sizes part of K6 |
| Density | Barbican's own row heights and paddings; `Appearance::menu_row` for menus | none |
| Colour-blind palette | Barbican's own tag and calendar colours, plain data | none |
| Style (Bento or Nordic, `uiStyle`) | not covered: two layouts, not two palettes (`ui/theme.slint:10-11, 71-72`) | none. The port draws one look, and this setting is part of the themes decision |

What each answer to "themes" would change:

| Decision | Effect on the port | Toolkit |
|---|---|---|
| No themes, ever | One look, from the toolkit's palette and `appearance`. The 581 lines of Bento/Nordic tests port as one branch. The Style setting goes | K6 not needed |
| Colours and accents only (Bulwark's built-in presets, an accent picker, import of a colour file) | A table of palettes and a picker in Barbican, S. Import reads `appearance`'s text format (`appearance::read`/`write`) or Barbican's own | none: `Palette`, `AccentFrom`, `Scheme` cover it |
| A second built-in style (Bento and Nordic both kept) | The two differ in layout (tiles with gaps against one surface), not only colour, so screens must lay out both ways: about M across the ported screens, and twice the screen tests | none; K6 does not help with layout |
| Full skins (fonts, sizes, shapes, icons, per-widget looks) | Every Barbican size read from one `Metrics` value from U4 on | K6 (M-L), and the "no bundled fonts" rule lifted if a skin brings a font |

Cheap insurance whatever the answer: from U4 on, Barbican's own screens read their sizes and
colours from one Barbican value (a `Metrics` beside the toolkit's `Palette`), never from literals.
Then a later "full skins" decision costs K6 and a loader, not a pass over every screen.

---

## Appendix: how the numbers were counted

- Slint counts are `grep` over `ui/*.slint`: `component`, `struct`, `global`, `callback`,
  `property <` (all declared) and `(in|out|in-out) property` (public), element names as whole
  words.
- The cut Slint is: `ui/applock.slint` and `ui/share.slint` whole; in `ui/app.slint`, the imports of
  `AppLock` and `ShareX`, `plugin-rows` and its block (`:2081`, `:3127-3143`), `plugin-watch`
  (`:2241-2245`), the send check (`:2246-2248`, `:3961-3967`), the light mode (`:2190-2195`,
  `:3191-3205`), the Print item (`:3029`), the Pop out button (`:3465`), the lock and share lines
  (`:2264-2265`, `:2314-2322`, `:6371-6372`, `:6379-6381`); in `ui/calendar_ext.slint`, `CxSlot`,
  the four `ed-find-*` properties, `ed-find`, `ed-pick-slot`, the Suggest times button and the
  Suggested times panel (`:43`, `:120-138`, `:589-593`, `:653-675`); the `print` icon in
  `ui/theme.slint:217`. The 20 or so `visible: !AppLock.locked` lines in `ui/app.slint` are edited,
  not removed, and are not in the line count.
- Rust counts are `grep -o` over `src/` (all `.rs` files, subfolders included). Binding calls are
  `\.(set|get|on)_[a-z0-9_]+\(`; they include a few calls that are not Slint's (for example
  `set_bounds` is wry's), so treat them as "about".
- Binding calls in kept files that belong to cut features: calls on a line that names cut code
  (`plugin`, `applock`/`AppLock`, `share::`/`ShareX`, `lite`, `print`, `find_slots`, `find_busy`,
  `pick_slot`, `ed_find`, `getAvailability`, `send_check`) give 38; the same within three lines
  give 82. The truth is between, so "40 to 80".
- "Files with no Slint" are the files where neither `slint` nor `AppWindow` appears.
- Line counts are `wc -l` at `b00812d`, before the R removals. Kept files lose more than the lines
  that name cut code (whole closures in `main.rs` and `compose.rs` go), so "about 45,000" is an
  upper bound.
- Toolkit tests are `#[test]` lines; ignored sheet tests are included.
