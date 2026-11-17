# M1 look gap: the M1 build against the WinUI prototype

HAND TEST 1 found that M1 "does not look or feel like the concept winui3 build". The concept build is `prototype/winui/`. This file measures the
gap and records what the M1 views took from the prototype. It also lists what still differs, and why. Every value is taken from the
prototype's XAML and code (`prototype/winui/App.xaml`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `Palette.cs`, `MailHtml.cs`), not read off a
picture.

Branch `m1-look`, from `m2`. Both apps were captured hidden: off-screen, never activated, sample data only, rendered with
RenderTargetBitmap, so the WebView2 body is not in the pictures on either side.

## How it is measured

| Tool | What it does |
|---|---|
| `prototype/capture/` | Builds the prototype's own XAML and code unchanged, plus a hidden window and a capture driver. It writes the pictures and `layout-proto-*.json` (element boxes). |
| `tools/lookdiff.py proto` / `m1 --port N` | The same states on both sides, in Bento and Nordic, dark and light, at 1560x960 (the prototype's default size): inbox, selected, hover (VisualState `PointerOver` set through the new `vstate` automation command), four switch frames, narrow (1100x760). The M1 boxes come from the new `bounds` command. |
| `tools/lookdiff.py pair` | Side-by-side pictures: the prototype on the left, M1 on the right. |
| `tools/lookdiff.py check` | **The drift check.** It compares each picture in `docs/milestones/M1-look/ref/` (12 pictures: inbox, selected and hover × 4 looks) with a fresh capture. It fails when more than 2 % of the pixels move by more than 24/255, and it rejects uniform (blank) captures. After an intended look change, run `lookdiff.py accept --m1 DIR`. |
| `tools/layout/look_feel.py` | Feel numbers from the `rendering` probe: input to first frame, the longest frame gap, dropped frames, and the body stream's open-to-shown time. |
| `tools/layout/m1_scenarios.py` | Flicker scenarios: theme flip, resize, folder click, 20 × `j`. The regions in `tools/layout/m1-*.json` are re-measured for the new layout. |

Comparison pictures (sample data): `docs/milestones/M1-look/compare/`.
- `before-*.png`: the prototype against M1 before the port.
- `after-*.png`: the prototype against M1 after it.

## The gap, and what was done

P = prototype; M1b = M1 before; M1a = M1 after.

### Window and materials

| Area | P | M1b | M1a |
|---|---|---|---|
| Title bar | Content extended into it. A 38 px band with "Barbican" (12, faint, x = 22). Caption buttons in the palette. | System title bar | As P (WinUI head). The Linux head keeps the window manager's bar, so the band is 0 high there. |
| Ground | Solid `ground`, no Mica or Acrylic | Solid | Unchanged (it already matched) |
| Font | `Segoe UI` (ContentControlThemeFontFamily) | Segoe UI Variable (WinUI default) | Segoe UI |
| Shell padding / gap | Bento 14,0,14,14, gap 12. Nordic 0,0,14,14, gap 0. | Bento 14 all round, gap 12. Nordic 0 padding, so no margin on the right or bottom. | As P (StyleMetrics) |
| Nordic surface | List, reader and context in one rounded tile (radius 28, hairline) | No surface; the regions sat on the ground | As P (ShellPage `Surface`) |
| Pressed colour | `Pressed` #282828 / #e9e9eb | No token | New `pressed` in `design/legacy-slint/theme.slint`, then gen-tokens |

### Rail

| Area | P | M1b | M1a |
|---|---|---|---|
| Width / button | Bento 66 / 44 (r14). Nordic 76 / 46 (round). | Bento 64 / 42 | As P |
| Bento mark | "B" squircle: 40, r13, accent, Black 18 | Three bars | "B" (Nordic keeps the bars, as P) |
| Inactive button | Clear background, faint icon | Hover fill on every button | Clear |
| Icon colour | Faint, sel-text when active | **Bug:** SIcon stroked in its own, un-inherited Foreground. Icons were whitish in dark and almost invisible in light. | Fixed: SIcon follows the content presenter it sits in, and the button templates pass their Foreground to it |
| Account avatar | `sel` background, `sel-text` initials | Rose | sel / sel-text |

### Sidebar

| Area | P | M1b | M1a |
|---|---|---|---|
| Frame | Bento: one tile, 236 wide, padding 12,14, r26. Nordic: on the ground, 228 wide. | Three tiles (compose, folders, tags), 204 wide | One tile / bare, as P |
| Compose | "New message". Bento 46 high, r16, pen icon. Nordic 50 high, r25, text only. | "Compose"; Bento 52 high, r20 | As P |
| Folder row | Bento 38 high, r14, icon 18, name 13.5. Nordic 44 high, r22, no icon, name 14.5. Selected: `sel` + `sel-text`. | 36 high in both styles, name 13.5, Nordic with icons; the selected text stayed body-coloured | As P (the folder template recolours its content) |
| Counts | 12, SemiBold faint (Bento) / 12.5 Normal (Nordic) | 12 Bold dim | As P |
| Labels | Rows: a 10 px squircle dot and the name | Bento: a two-column chip grid | Rows in both styles |
| Section header | Bento "LABELS": 11 Bold, tracking 80, faint. Nordic "Labels": 12 Bold, dim. | Boxed headers | As P. "FOLDERS" / "Folders" kept, because M1 folds the sections. |
| Storage | Nordic text: "2.4 of 10 GB used" | Separate tile with a meter | At the bottom of the column; the meter shows in Bento only |

### Message list

| Area | P | M1b | M1a |
|---|---|---|---|
| Width | Bento 370, Nordic 390 | 368 / 390 | As P |
| Header | Bento: "Inbox" 24 Bold, "N unread" 12.5 dim. Nordic: 28 ExtraBold, tracking −20, greeting 13 dim. | 25 Bold, rose "5 new" pill, greeting in both styles | As P. The search box is kept (M1 feature). |
| Bento row | Margin 8,1, padding 12,10, r18. No frame at rest. Selected: `tile2` + 1 px `line`. | Every row framed; selected with the accent bar; group header boxed | As P (SelLine, no bar) |
| Nordic row | Padding 28,14. Selected: `tile2` + a 3 px accent bar down the full height. | Short bar | As P |
| Text | Sender 14 (Bold unread / SemiBold read; Nordic Medium read). Subject 13.5 body (Bento) / 14 (Nordic). Preview 12.5 faint / 13 dim. Time 12. | Sender 13.5, subject 13 (text colour, bold when unread), preview 12 dim, time 11.5 | As P |
| Bento avatar | 38, r13, initials 13 Bold | 38, initials 12.5 | As P |
| Unread dot | Shown only when unread. Bento: the time turns accent. | Always laid out | As P |

### Reader

| Area | P | M1b | M1a |
|---|---|---|---|
| Order | Bento: subject, sender, actions. Nordic: actions, subject, "X to you · time". | Toolbar first in both | As P (rows bound to StyleState) |
| Subject | Bento 22 SemiBold. Nordic 34 Bold, LineHeight 41, tracking −25. | 27 Bold | As P |
| Sender | Bento: avatar 40 r13, From 14 SemiBold over the address 12.5. Nordic: one 13.5 dim line. | Avatar 32, one line | As P |
| Actions | Pills: Bento 36 high (r18), icon 16 + label 13 SemiBold. Nordic 38 high (r19), text only. Transparent with a hairline. | Filled tile2 buttons | As P. M1 keeps six actions; in Bento, Reply all, Delete and More are icon-only, so they fit. |
| Quick reply | r26, hairline, no fill, padding 20,8,8,8. Placeholder 14.5. Send 40 high, r20, padding 22,0. | Filled tile2 | As P, in both styles (P has it in Nordic only; M1 shows it in both) |
| Body CSS | Bento 15 px / 1.6; Nordic 16 px / 1.8, max width 600 | 14 px (the Rust-ported MailPageBuilder) | **Not changed.** See "Still differs". |

### Sender panel (context)

| Area | P | M1b | M1a |
|---|---|---|---|
| Panel | Bento: a 272 tile; avatar 68 r22, name 18 Bold, "RECENT" cards (tile2 + line, r14). Nordic: 250 wide, a left hairline, avatar 64 round, name 17 ExtraBold, hairlines between the parts. | None | New `Views/Context/ContextView`. It shows the open message's sender, the authentication verdict, and up to four other messages from that sender in the loaded list (no server request). **It hides when the reader would drop below 420 px** (ShellLayout.ContextShown). P always shows it. |

### Motion

| Motion | P | M1b | M1a |
|---|---|---|---|
| Hover and press | VisualTransition 80 ms with setter-only states, so in effect instant | Instant | Instant (MOTION.md rule 3) |
| List entrance | EntranceThemeTransition, staggered per item, FromVerticalOffset 18 | None | **One block rise** of the rows on a folder change: opacity 0.5 → 1 and 10 px → 0 over `enter` (150 ms), enter curve, not while keys repeat. No per-row stagger, because it would hold rows back (rule 3; ViewXamlLintTests forbids item transitions). |
| Reader header | ContentThemeTransition, VerticalOffset 6. It plays only on first load, because P sets Text and never re-adds the children. | None | A 6 px rise of the subject and sender line on every switch, over `view` (110 ms), skipped while keys repeat. |
| Body switch | Double buffer: the incoming body fades 0 → 1 over 110 ms over the old one | Instant swap | Cross-fade over `view` (110 ms) in `shell.js`. The old page stays under the new one until the fade ends, so nothing blank shows. 0 with reduce motion and during key repeat. |
| Style switch | Shell opacity 0.55 → 1 over 220 ms, Y 10 → 0 over 260 ms, CubicEase out | Instant | The mail screen rises over `enter` (150 ms; P's 260 ms is over the 200 ms ceiling). |
| Theme switch | Instant | Instant | Instant |

Every motion is `Motion/Rise`. The new content is complete on the first frame, so it never starts invisible and never waits. A new run
stops the running one, and duration 0 does nothing.

## Choices where the prototype and the mockups disagree (prototype preferred)

1. **Bento selection.** P draws a tile2 card with a hairline; the mockup has an accent bar. Taken from P.
2. **Bento mark.** P's "B" squircle; the mockup and M1 had the bars. Taken from P.
3. **Labels.** P shows dot rows; the Bento mockup shows a chip grid. Taken from P.
4. **Compose text.** P's "New message"; M1 and the mockup said "Compose". Taken from P. The accessible name stays "Compose".
5. **Reader order in Bento.** P puts the subject first; the mockup puts the toolbar first. Taken from P.
6. **Sidebar frame.** P uses one tile; the mockup and M1 used three. Taken from P.
7. **Font.** P sets Segoe UI explicitly; the WinUI default is Segoe UI Variable. Taken from P.

## Feel, before and after

Values are from `look_feel.py`, hidden, sample data, dark, all in ms; dropped = frame gaps over 25 ms.
- Before: `artifacts/look/feel-before.json`.
- After: `artifacts/look/feel-after.json`.

| Probe | Before: in→frame med / max | After: in→frame med / max | Before: gap max / dropped | After: gap max / dropped | Open→shown med / max (before → after) |
|---|---|---|---|---|---|
| Bento switch (`j`) | 7.2 / 37.7 | 5.6 / 30.2 | 39.1 / 1 | 33.3 / 1 | 8.6 / 171 → 9.5 / 146 |
| Bento key repeat | 4.0 / 27.3 | 4.9 / 23.9 | 29.6 / 1 | 25.0 / 0 | |
| Bento folder | 5.6 / 10.6 | 5.9 / 10.3 | 15.9 / 0 | 14.0 / 0 | |
| Bento style flip | | | 47.3 / 2 | 51.3 / 2 | |
| Nordic switch (`j`) | 5.6 / 27.2 | 6.2 / 20.9 | 28.6 / 1 | 26.1 / 1 | 9.6 / 151 → 8.3 / 142 |
| Nordic key repeat | 3.5 / 41.3 | 4.4 / 26.5 | 35.6 / 3 | 26.4 / 1 | |
| Nordic folder | 4.4 / 8.2 | 10.5 / 11.3 | 14.7 / 0 | 11.7 / 0 | |
| Nordic style flip | | | 48.0 / 2 | 38.8 / 2 | |

Idle: 0 frames in 2 s, before and after; nothing animates at rest.

**Flicker** (`m1_scenarios.py`):
- Theme flip, resize and 20 × `j`: 0 bad / blank / flash / empty frames in both styles.
- Folder click reports BLANK on the list region (11 frames in Bento, 12 in Nordic). The target folder (Sent, in the sample data) holds one message, so the rows region has a twentieth of the inbox's ink. That ink is steady from the first frame after the click to the click back. The old rows stay until the new ones are in, and no frame falls between them (`artifacts/look/scen-after-*/folder_click`).
- The before run reported 0 there only because its `select m3` never changed the list (3 frames, the list unchanged).
- The flicker rule compares each frame with the first and the last, so a sparse target folder will always trip it. It needs a target-aware baseline (Fable item below).

## Still differs, and why

- **Glance tiles, search box, folder headers that fold, six reader actions, the storage meter, the conversation strip, attachment cards.**
  These are M1 features that P does not have; they are kept, in P's shapes.
- **Body typography.** It is still the Rust-ported MailPageBuilder (14 px). P's 15 / 16 px, line height 1.6 / 1.8, and Nordic's 600 px
  measure belong to the body pipeline (Core, sizing, and the dark pass). That pipeline is outside a look-only diff, and m2 streams touch it.
- **The sender panel at narrow widths.** P keeps it at every width and squeezes the reader; M1 drops it below a 420 px reader.
- **Per-row staggered entrance and P's 220 / 260 ms style switch.** Both are against design/MOTION.md (rule 3, and the 200 ms ceiling).
  M1 uses one block rise and 150 ms instead.
- **Disabled actions.** Compose, Send and the reader actions are inert until M2 / M3, so they draw at 45 % opacity. P draws them as if
  live.
- **P's chips (Focused / Other / Newsletters), "Snooze", contact buttons and notes in the sender panel.** These are features M1 does
  not have.
- **Hover token.** #1f1f1f against P's #1d1d1d, and #f2f2f3 against #f3f3f4. Kept, because the 1-level difference is below what the drift
  check can see.

## Mail body typography (m2-close-b)
The prototype's `MailHtml.Wrap` sets the body at 15 px / line height 1.6 in Bento and 16 px / 1.8 in Nordic, with a 600 px measure in Nordic. `MailPageBuilder` now does the same for the themed and light pages (the measure applies to prose only: plain text and HTML without its own colours, so a newsletter layout keeps its width). The dark-adjusted page takes the size and line height but not the measure. Pictures: `docs/milestones/M2-screens/reader-body-{nordic,bento}-dark.png`.
