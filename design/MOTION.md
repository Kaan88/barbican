# Barbican motion

Smooth and modern, but **never in the way**: nobody waits for an animation. Motion only
decorates a change that has already happened.

## Rules (mandatory everywhere)

1. **Input is never gated by animation.** Selection, open/close, navigation and data changes
   apply instantly in the model. Never delay an action, a callback, focus or content until an
   animation ends. A second click or key during an animation acts at once: animations retarget
   from where they are, they never queue.
2. **Durations.**
   - feedback (hover, press, focus, selection highlight): 80–120 ms → `FastMs` (100 ms)
   - enter (menus, popups, flyouts, tooltips, dialogs, panels): 120–180 ms, ease-out → `EnterMs` (150 ms)
   - exit: ≤ 100 ms or instant → `ExitMs` (90 ms); most things simply vanish
   - view / app switch: the new content is visible on the first frame; at most a ≤ 120 ms
     fade/slide of the incoming view (`ViewMs`), never a crossfade that hides content
   - nothing over 200 ms. Ease-out for enter, ease-in for exit. No bounce, no spring overshoot
     on anything frequent.
3. **Lists.** Never animate every row on a refresh. Animate only inserted/removed rows (short
   height/opacity) when that is cheap, and the selection highlight. Keyboard repeat through a
   list does not animate per step: list feedback snaps while the selection moves faster than
   the animation (`MotionScale.Rapid`).
4. **Reduce motion.** Settings › Accessibility › Reduce motion (Bulwark `reduceMotion`), or
   Windows "Animation effects" off while that setting is on "System" ⇒ every duration is 0.
5. **Nothing continuous** except a progress bar or spinner that is on screen. Skeletons only
   when loading takes longer than 150 ms (no flash of skeleton for fast loads).
6. **Performance.** No dropped frames at 60 Hz. No per-frame work in code-behind. Collection updates are
   incremental (`ListSync`) instead of replacing models. No blocking IO / JMAP / settings
   loads on the UI thread.

## Tokens (`src/Barbican.Presentation/Theme/Tokens.g.cs` `Tokens.Motion` / `Tokens.Ease`, `src/Barbican.App/Motion/MotionScale.cs`)

The values are generated with the palette (`tools/build.ps1 gen-tokens`). `MotionScale` (a record struct: `Reduce`, `Rapid`) applies
reduce motion and rapid selection to them; `MotionRuntime.Scale` is the one the views read, kept current by `ThemeController`
from `ShellViewModel` (`ReduceMotion`, `IsRapid`). It is pure, so the rules are unit-tested (`MotionScaleTests`).

| token | value | use |
|---|---|---|
| `MotionScale.Reduce` | from the shell | reduce motion on (setting, or Windows animations off on "System") |
| `MotionScale.Rapid` | from the shell | the list selection is moving faster than an animation (key repeat) |
| `FastMs` | 100 ms (0 with reduce) | hover, press, focus ring, selection highlight |
| `ListMs` | `FastMs`, 0 while `Rapid` | message-list row feedback |
| `EnterMs` | 150 ms (0 with reduce) | menus, popups, flyouts, tooltips, dialogs, toasts coming in |
| `ExitMs` | 90 ms (0 with reduce) | the rare thing that animates out (toasts) |
| `ViewMs` | 110 ms (0 with reduce) | incoming view after a switch (optional, content visible at once) |
| `TipDelayMs` | 350 ms | hover time before a tooltip shows (a wait, not motion: kept with reduce) |
| `EnterCurve` | cubic-bezier(0.2, 0, 0, 1) as a `KeySpline` | enter, feedback (half way after ~20 % of the time) |
| `ExitCurve` | cubic-bezier(0.4, 0, 1, 1) | exit |
| `PopScale` | 0.96 (1 with reduce) | the scale an entering popup starts from |
| `RisePx` | 8 px (0 with reduce) | how far a toast slides in from |
| `CeilingMs` | 200 | nothing animates over this (rule 2; tested) |

### Building blocks (`src/Barbican.App/Motion/`)

- `PopIn.Run(card, origin)`: fades the card in from 0 and scales it from `PopScale`, over `EnterMs`, with the enter `KeySpline`;
  the origin is the anchor corner (the account popover grows from its bottom-right, where the avatar is). Call it from the
  flyout's `Opened` event. With reduce motion it sets the end values at once. The exit is instant (the flyout simply closes).
- `Scrim` (a `Grid`): the dimmed backdrop of a dialog; `Show()` fades it in over `EnterMs` through `MotionRuntime.FadeIn`,
  `Hide()` removes it at once. The dialog card inside uses `PopIn`. M1 has no dialog yet; M2's use it.
- `Tip` (attached property `Tip.Text`): an in-window tooltip drawn in the shell's overlay canvas (`Tip.SetHost`); waits
  `TipDelayMs`, then fades in over `EnterMs`; removed instantly when the pointer leaves. **Not** `ToolTip` / `ToolTipService`:
  those open as their own top-level window and are on the popup deny list (ADR 0007).
- `MotionRuntime.FadeIn(element)`: the one enter fade the others use.
- `Rise.Run(element, shift, ms, fromOpacity, fromY)`: the incoming-view decoration taken from the WinUI prototype
  (docs/milestones/M1-look-gap.md). It acts on an element that **already shows its new content**: a few pixels of rise and a fade
  from a partly transparent start (never from 0), with the enter curve. A new run stops the running one; 0 ms does nothing.
  Its uses:
  - the message rows on a folder change: `EnterMs`, 0.5 and 10 px;
  - the reader's subject and sender line on a switch: `ViewMs`, 0.55 and 6 px;
  - the mail screen on a Bento/Nordic switch: `EnterMs`.

  The two list and reader uses pass 0 while keys repeat (`MotionScale.Rapid`).
- Body cross-fade (`Assets/mail/shell.js` `reveal(seq, ms)`): the finished page fades in over the one on screen over `ViewMs`.
  The old page is dropped when the fade ends, or at once when anything else happens. `BodyShell` sends 0 while keys repeat.

## Feedback (one shared system: `src/Barbican.App/Styles/Feedback.xaml`)

Button presses, hovers, focus and disabled look are one system (user decision, 2026-11-21): every control of one kind gives the same
feedback, with the same timing, from state brushes and durations that the token generator writes for every style. A view never names a
state colour or a duration; it takes a style from `Feedback.xaml` and, where it draws its own state, a `Feedback*` brush.

State brushes (palette keys of `Tokens.xaml`, rewritten in place on a theme flip; Bulwark values from `design/legacy-slint/theme.slint` `Bulwark`,
legacy values from `Theme`). Dark / light for the Bulwark style:

| brush | Bulwark | legacy (Bento, Nordic) | use |
|---|---|---|---|
| `FeedbackHover` | `#262626` / `#f1f5f9` (muted) | `Hover` | neutral hover: menu rows, rows, buttons without a fill |
| `FeedbackPressed` | `#333333` / `#e2e8f0` | `Pressed` | pressed, one step stronger than hover in dark and light |
| `FeedbackIconHover` / `FeedbackIconHoverFg` | `#1e3a8a` / `#dbeafe` (accent), text | `Hover`, `Text` | icon, ghost, outline buttons (`button.tsx:49`) |
| `FeedbackIconPressed` | `#1e40af` / `#bfdbfe` | `Pressed` | the same, pressed |
| `FeedbackActive` / `Hover` / `Pressed` / `Fg` | primary at 10 / 15 / 20 %, primary text | `Sel`, `Sel`, `Pressed`, `SelText` | a toggle that is on (`toolbar.tsx:16`) |
| `FeedbackSelected` / `Fg` | `#1e3a8a` / `#dbeafe` (accent), accent-foreground | `Tile2`, `Text` | the selected row, still this on hover (`thread-list-item.tsx:731`) |
| `FeedbackPrimaryHover` / `Pressed` | primary at 90 % / 80 % | accent, accent at 88 % | the filled button (`button.tsx:48`) |
| `FeedbackDestructiveHover` | destructive at 10 % | the same | a destructive menu row (`context-menu.tsx:726`) |
| `FeedbackFocus` | `#d4d4d4` / `#2563eb` (ring) | ring | the focus ring colour |

The semantic colours of `globals.css` are tokens too (`Destructive`, `Success`, `Warning`, `Info`, `Secondary`, `Input`, `PopoverFg`, `AccentFill`,
`AccentFillFg`, `Unread`, `Chart1`..`Chart5`, each with a `Fg` where Bulwark has one).

Shared styles: `Button.Icon`, `Button.Action`, `Button.Chip`, `Button.Primary`, `ToggleButton.Icon`, `Button.MenuItem`, `Button.MenuItem.Destructive`,
`Button.HoverAction`, `ListItem.Row` (plain lists), plus the message and folder row styles in `Controls.xaml` (same brushes).

- **Hover and press ease the colour; lists snap.** A button, toggle or menu row changes its Background through the system brush transition
  (`Motion/FeedbackTransition.cs`, attached `Kind` on the template's Border): `Button` 200 ms, `Menu` 150 ms, `Color` 150 ms, `Fast` 100 ms (hover actions over a row).
  The state itself is applied at once by the VisualStateManager; the transition only softens the paint and never gates input. Rows
  (message, folder, agenda ...) have no transition (rule 3; Bulwark animates only a row's shadow). The system transition has a fixed curve, so there is
  no easing token for it; the foreground colour of a control snaps (WinUI has no foreground transition). On the Uno Skia heads the transition is not applied.
- **Reduce motion** zeroes `ColorMs/ButtonMs/MenuMs` (`MotionScale`), removes the transitions (`FeedbackTransition.Refresh`) and sets the stock controls' own
  durations (`ControlFasterAnimationDuration` etc., `Motion/FluentMotion.cs`) to 0; those pick the value up when their template is created.
- **One focus ring.** `SystemControlFocusVisualPrimaryBrush` is the live `Ring` brush and the secondary brush is transparent, so every control with
  `UseSystemFocusVisuals` draws one 2 px ring (rows inset it, `FocusVisualMargin` 2: `accessibility.css:130`). `Theme/FocusRing.cs` widens it to the Accessibility
  setting (3 / 5) on any control that gets focus; a control whose thickness is bound keeps its binding.
- **Fluent accent.** The stock controls' accent fill (`AccentFillColorDefaultBrush` and friends) is the primary brush, not the Windows accent colour.
- **Motion that Bulwark does not have** goes through `MotionRuntime.DecorationMs(ms)`, which is 0 in the Bulwark style: a list refresh rise, the reader switch rise
  and body fade, a tooltip fade, the shell rise on a style switch (their owners call it, see the ledger). A menu entering passes `PopInKind.Menu` to `PopIn.Run`
  (instant in Bulwark); a dialog, palette or sheet keeps its 150 ms zoom and fade (Bulwark animates it, `dialog.tsx:181,231`). The dialog scrim is black at 50 %.

Changed with this system (2026-11-21): disabled opacity 0.45 to 0.5; the pressed border change on buttons is gone (Bulwark has none); the Bulwark dark hover is
`#262626` (was `#1A1A1A`) and its light pressed is `#e2e8f0` (was lighter than hover); Bulwark radii are 6 (controls, menus, toasts) and 8 (dialogs, popovers);
`Round()` of the Bulwark style is a circle (avatars and the rail account only).
## How to animate

Feedback on a control: put the state change in the `VisualStateManager` (setters, no transition), so the new colour is there on
the first frame; a colour change that should ease uses a `Storyboard` of `FastMs` with the enter curve, started from the state
change and never awaited. The state itself is immediate. Item templates (`Styles/Controls.xaml`) use setters only.

Something that appears (a flyout, a popup card): `PopIn.Run` from `Opened`. Removal is instant, which is the exit we want.

Something that must also animate out (toasts): keep the element, drive its opacity from a bool, pick the duration by direction
(`EnterMs` in, `ExitMs` out), and collapse it once it has faded.

Retargeting: start the new `Storyboard` from the current value (do not reset to the start value first); a fast second click never
waits and never queues. A `Storyboard` that is already running is replaced, not appended.

Where the framework animates by itself, switch it off: every `ListView` / `ItemsRepeater` clears `ItemContainerTransitions`
(`<win:ListView.ItemContainerTransitions><TransitionCollection /></win:ListView.ItemContainerTransitions>`: the default
add/remove/reposition transitions would animate every row of a refresh), and nothing uses `Frame` navigation transitions or
`EntranceThemeTransition`. `ViewXamlLintTests` checks both.

Do not:
- animate the size or position of anything inside a layout that many rows share (a list refresh would move every row), or any
  property of every row on a refresh;
- animate properties that change continuously (scroll position, drag position, sizes that follow the window): they would lag the pointer;
- delay a command, a focus change or a collection update with a timer "until the animation ends";
- use a delay for anything but tooltips.

## Lists: incremental updates (`src/Barbican.Presentation/Common/ListSync.cs`)

Replacing an `ObservableCollection` (or its `ItemsSource`) rebuilds every row: the list flashes, hover and press are lost, the
whole list repaints. Instead sync the collection that is already shown: `ListSync` diffs the new rows against the shown ones by a
stable key and touches only what changed.

- unchanged rows: not touched (no notification, no repaint)
- changed rows: replaced in place (`SetItem`); new rows: `Insert`; gone rows: `RemoveAt`; a few moved rows: `Move`
- mostly new content (another folder, a new search, the first fill, a clear), mostly gone (a filter), many moves, or duplicate
  keys: one reset, which is cheaper than a notification per row.

The message list, the folder list and the tag list are all fed through it by their ViewModels; the views only bind. A view never
calls `Clear()` + `Add()` on a collection that is visible. The ported Rust tests of `listsync.rs` are in
`tests/Barbican.Core.Tests` (`[RustTest("listsync::...")]`).

## Reduce motion and the system setting

- `ShellViewModel.ReduceMotion` is the stored `reduceMotion` setting; on "System" it follows Windows "Animation effects"
  (read again when the window is activated: `ShellViewModel.NotifyWindowActive`, wired by `ShellLifecycle.AttachWindow`).
  `ThemeController` copies it into `MotionRuntime.Scale`.
- Rapid selection: `ShellViewModel.IsRapid` (`MotionState`) is set by the message list's key steps: a step within 150 ms of the
  previous one sets it (row feedback snaps) until 180 ms pass without a step.

## Measuring (`tools/motion.py`, `tools/flicker.py`, `tools/layout/m1_scenarios.py`)

The AUTOMATION build (hidden, off-screen, own port and profile: ADR 0007, `tools/auto-run.ps1`) has the frame probe:
`probe-start wgc` records every presented frame of the window (Windows.Graphics.Capture on Windows, `x11` on Linux) and the time
each command is dispatched; `probe-stop` returns them as JSON. `burst MS EVERY DIR [STEP]` writes the frames as pictures for
`tools/flicker.py` (BLANK / FLASH detection). `every MS N CMD` replays a command on a UI-thread timer for precise key repeat.
`tools/layout/m1-bento.json` / `m1-nordic.json` hold the region rectangles and AutomationIds for the C# app;
`python tools/layout/m1_scenarios.py --port N --layout bento|nordic` runs theme_flip, resize, folder_click, key_j20 and the idle
probe (0 frames expected).

Known limit of the hidden window: WGC delivers a frame only after a layout change (a resize wakes it); a palette-only change is
composed in the XAML tree (`shot` shows it) but not captured. The theme flip is therefore measured by its structure (one UI-thread
turn, same brush instances: `PaletteWriterTests`) plus a settled-frame check.

## Rust baseline (Slint, 2026-11-16): the bar to beat

The M0 measurements of the Rust app, kept as the target the C# app is compared with (`spec/feel/rust-baseline/`).

### Results (2026-11-16, `tools/motion-results/{baseline,after}.json`)

Warm (median of all inputs over 3 runs; ms):

| scenario | latency med (before → after) | latency max | frames / input | max gap in motion | settle after last input |
|---|---|---|---|---|---|
| hover across list | 8.6 → 7.3 | 69.1 → 8.8 | 7 → 37 | – → 13.0 | 15 → 125 |
| click mail | 8.2 → 9.0 | 9.4 → 9.8 | 8.5 → 8.0 | (async) | 182 → 174 |
| arrow repeat ×20 | 8.4 → 7.7 | 9.7 → 10.1 | 12 → 16.5 | (async) | 170 → 166 |
| context menu open / close | 8.5 → 8.8 | 9.5 → 10.7 | 2 → 21 | – → 9.7 | 14 → 118 |
| dialog open / close | 8.4 → 8.1 | 8.7 → 9.4 | 2 → 13.5 | – → 8.2 | 15 → 14 |
| app switch | 10.8 → 8.8 | 12.9 → 9.9 | 3 → 18.5 | – → 7.4 | 15 → 120 |

- Input → first frame is unchanged (8–10 ms): nothing waits for an animation; the first
  frame already shows the change (ease-out: half way after ~20 % of the duration).
- No dropped frames inside any animation (max gap 13 ms).
- "settle" is the decoration running out after the last input (≤ 125 ms); closing a menu
  or dialog is instant (dialog settle 14 ms = the closing frame pair).
- Arrow repeat: the first step animates, then `Motion.rapid` makes every later step snap
  (3 frames per step vs 2 before; the first steps after a pause 5).

Cold (first open after launch, 2 launches each): context menu 9.3 / 10.2 → 13.0 / 12.8 ms,
dialog 26.3 / 26.8 → 22.5 / 23.5 ms. Without the start-up warm-up (`motion-warm` in
app.slint) the first animated popup of a session cost 23 / 87 ms: the renderer builds what a
faded, scaled surface with text and a shadow needs the first time it draws one, so the app
draws one at 1 % opacity while it starts.

List refresh (Sent, pointer resting on a row, `G`): before, the refresh replaced the model,
the hovered row lost its hover and hover actions until the pointer moved; now the rows are
synced in place and the hover (and the actions) stay.
