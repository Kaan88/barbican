# Barbican motion

Smooth and modern, but **never in the way**: nobody waits for an animation. Motion only
decorates a change that has already happened.

## Rules (mandatory everywhere)

1. **Input is never gated by animation.** Selection, open/close, navigation and data changes
   apply instantly in the model. Never delay an action, a callback, focus or content until an
   animation ends. A second click or key during an animation acts at once: animations retarget
   from where they are, they never queue.
2. **Durations.**
   - feedback (hover, press, focus, selection highlight): 80–120 ms → `Motion.fast` (100 ms)
   - enter (menus, popups, flyouts, tooltips, dialogs, panels): 120–180 ms, ease-out → `Motion.enter` (150 ms)
   - exit: ≤ 100 ms or instant → `Motion.exit` (90 ms); most things simply vanish
   - view / app switch: the new content is visible on the first frame; at most a ≤ 120 ms
     fade/slide of the incoming view (`Motion.view`), never a crossfade that hides content
   - nothing over 200 ms. Ease-out for enter, ease-in for exit. No bounce, no spring overshoot
     on anything frequent.
3. **Lists.** Never animate every row on a refresh. Animate only inserted/removed rows (short
   height/opacity) when that is cheap, and the selection highlight. Keyboard repeat through a
   list does not animate per step: list feedback snaps while the selection moves faster than
   the animation (`Motion.rapid`).
4. **Reduce motion.** Settings › Accessibility › Reduce motion (Bulwark `reduceMotion`), or
   Windows "Animation effects" off while that setting is on "System" ⇒ every duration is 0.
5. **Nothing continuous** except a progress bar or spinner that is on screen. Skeletons only
   when loading takes longer than 150 ms (no flash of skeleton for fast loads).
6. **Performance.** No dropped frames at 60 Hz. No per-frame work in Rust. Model updates are
   incremental (`listsync`) instead of replacing models. No blocking IO / JMAP / settings
   loads on the UI thread.

## Tokens (`ui/theme.slint`, `global Motion` and `global Ease`)

The curves live in their own global `Ease` because Slint cannot expose easing properties to
Rust, and `Motion` is set from Rust.

| token | value | use |
|---|---|---|
| `Motion.reduce` | in, from Rust | reduce motion on (setting, or Windows animations off on "System") |
| `Motion.rapid` | in, from Rust | the list selection is moving faster than an animation (key repeat) |
| `Motion.k` | 0 or 1 | multiplier on every duration (0 with `reduce`) |
| `Motion.fast` | 100 ms × k | hover, press, focus ring, selection highlight |
| `Motion.list` | `fast`, 0 while `rapid` | message-list row feedback |
| `Motion.enter` | 150 ms × k | menus, popups, flyouts, tooltips, dialogs, toasts coming in |
| `Motion.exit` | 90 ms × k | the rare thing that animates out (toasts) |
| `Motion.view` | 110 ms × k | incoming view after a switch (optional, content visible at once) |
| `Motion.tip-delay` | 350 ms | hover time before a tooltip shows (a wait, not motion: kept with reduce) |
| `Ease.enter` | cubic-bezier(0.2, 0, 0, 1) | enter, feedback (half way after ~20 % of the time) |
| `Ease.exit` | cubic-bezier(0.4, 0, 1, 1) | exit |
| `Motion.pop-scale` | 0.96 (1 with reduce) | the scale an entering popup starts from |
| `Motion.rise` | 8 px (0 with reduce) | how far a toast slides in from |

`Prefs.reduce-motion` stays for older code; new code reads `Motion`. Already-applied users:
the Switch knob, buttons (RailButton, ToolButton, BarButton, PrimaryButton, ActionButton,
IconBtn, QuickChip, HoverAction, FmtButton, CmpHeadButton, MenuItem, PeopleCta, PeopleBtn),
nav rows (FolderRow, TagRow, PeopleNav, PeopleLine, FileNavRow, settings navigation,
Segmented), MailRow, ContextMenuRow, the focus outline of PeopleField and the settings search.

### Building blocks (`ui/theme.slint`)

- `PopIn` (inherits Rectangle): fades in from 0 and scales from `Motion.pop-scale` when it is
  created, over `Motion.enter`; removed instantly. Set `transform-origin` to the anchor corner
  (the context menu grows from its top-left, where the pointer was). Used by `MenuPanel`
  (every PopupWindow menu), `ContextMenu` (menus and flyouts), the account switcher, the
  admin / event / prompt dialog cards and the calendar hover preview.
- `Scrim` (inherits Rectangle): the dimmed backdrop of a dialog; its colour fades in over
  `Motion.enter`, the dialog card inside is a `PopIn`. Closing removes both at once.
- `Tip` (inherits Rectangle): a tooltip; waits `Motion.tip-delay`, then fades in over
  `Motion.enter`. Removed instantly when the pointer leaves.

## How to animate

Feedback on a control: animate the property the state drives. The state change itself is
immediate, the colour follows.

```slint
background: ta.pressed ? Theme.track : ta.has-hover ? Theme.hover : transparent;
animate background { duration: Motion.fast; easing: Ease.enter; }
```

Something that appears (`if cond: …` or a `PopupWindow`): make it a `PopIn` (or give it the
same `init` pattern). Removal of an `if` element is instant, which is the exit we want.

```slint
if root.menu-open: PopIn { transform-origin: { x: 0, y: 0 }; … }

// the pattern inside PopIn: start values, then assign the end values in init (assignments
// to an animated property animate; with reduce motion the duration is 0 and it just snaps)
opacity: 0;
transform-scale: Motion.pop-scale;
animate opacity, transform-scale { duration: Motion.enter; easing: Ease.enter; }
init => { self.opacity = 1; self.transform-scale = 1; }
```

Something that must also animate out (toasts): keep the element, drive it from a bool, pick
the duration by direction, and stop drawing it once it has faded.

```slint
property <bool> on: root.toast != "";
opacity: on ? 1 : 0;
visible: on || self.opacity > 0;
animate opacity { duration: on ? Motion.enter : Motion.exit; easing: on ? Ease.enter : Ease.exit; }
```

Different in and out timing for a state (e.g. a highlight that appears at once but fades out):

```slint
states [
    lit when ta.has-hover: {
        background: Theme.hover;
        in { animate background { duration: 0ms; } }
        out { animate background { duration: Motion.fast; easing: Ease.exit; } }
    }
]
```

Retargeting is automatic: a property that changes while it is animating starts the new
animation from its current value, so a fast second click never waits and never queues.

Do not:
- animate `x`/`y`/`width`/`height` of anything inside a layout that many rows share (a list
  refresh would move every row), or any property of every row on a refresh;
- put `animate` on properties that change continuously (scroll position, drag position,
  sizes that follow the window): they would lag the pointer;
- delay a callback, a focus change or a model update with a `Timer` "until the animation ends";
- use `delay` for anything but tooltips.

## Lists: incremental model updates (`src/listsync.rs`)

Replacing a list property with a new `ModelRc::new(VecModel::from(rows))` rebuilds every row:
the list flashes, hover and press are lost, the whole list repaints. Instead sync the model
that is already shown:

```rust
use crate::listsync;

// rows: Vec<T> built as before; key: the row's stable id; same: content equality
listsync::sync_into(&u.get_folders(), rows, |f| f.id.clone(), |a, b| a == b, |m| u.set_folders(m));
```

- `sync_into(current, new, key, same, install) -> SyncStats`: syncs the `VecModel` behind a
  property in place; the first time (the property still holds the default empty model) it
  installs a new `VecModel` through `install`, once.
- `sync_by(&VecModel<T>, new, key, same) -> SyncStats` / `sync_model(&VecModel<T>, new, key)`
  (rows with `==`): the diff itself.
  - unchanged rows: not touched (no notification, no repaint)
  - changed rows: `set_row_data`
  - new rows: `insert`; gone rows: `remove`; a few moved rows: `remove` + `insert`
  - mostly new content (fewer than half of the new rows were shown: another folder, a new
    search, the first fill, a clear), mostly gone (more than 8 rows and more rows removed
    than kept: a filter), more than 8 moves, or duplicate keys: one `set_vec` reset, which is
    cheaper than a notification per row.
- `rows_eq(&ModelRc<T>, &ModelRc<T>)`: content equality for nested models (`[color]`,
  `[string]` fields), since `==` on `ModelRc` only compares identity. `mail::same_row` uses it.
- `SyncStats { set, inserted, removed, moved, reset }`, `untouched()`.

Converted: the message list (`mail::rebuild_list`), the folder list (`mail::rebuild_folders`)
and the tag list (`mail::set_tags`). Every other `ModelRc::new(VecModel…)` on a list that
refreshes while visible should move to `sync_into`.

## Reduce motion and the system setting (`src/motion.rs`)

- `motion::apply(ui, pref, reduce)` (from `apply_ui_prefs`, with the stored `reduceMotion`
  value and `A11y::reduce_motion`): sets `Motion.reduce`. On "System" that is Windows
  `SPI_GETCLIENTAREAANIMATION` ("Animation effects" off ⇒ reduce).
- `motion::watch_system(ui)` (startup): installs the window's winit event filter
  (`motion::install_filter`) which re-reads the Windows setting whenever the window is
  activated, so turning animations off in Windows takes effect when you come back (no
  polling). A window has a single filter, so this one also feeds the automation frame probe.
- `motion::reduce_for(pref, system_off)`: the rule itself (on / off / follow the system).
- `motion::step(ui)`: called on every keyboard step through the list; a step within 150 ms of
  the previous one sets `Motion.rapid` (row feedback snaps) until 180 ms pass without a step.

## Measuring (`tools/motion.py`)

The automation channel has a frame probe: `probe-start` records every rendered frame and
the time each command is dispatched, `probe-stop` returns them as JSON. A frame starts at the
window's winit `RedrawRequested` (Slint renders and presents right after) and ends when a
zero-delay timer queued then fires (the Skia renderer's rendering notifier is never called for
its Windows surface, so it cannot be used). `every MS N CMD` replays a command on a UI-thread timer
(`A+Bi` arguments step by B each time) for precise key repeat and pointer sweeps.
`motion reduce|normal|rapid|calm` flips the tokens in memory.

```
BARBICAN_AUTOMATION=1 BARBICAN_AUTOMATION_PORT=8970 target/release/barbican.exe
python tools/motion.py --port 8970 --json tools/motion-results/after.json
```

It reports per scenario: input → first frame latency, frames per input (1–2 = a snap),
the longest frame gap inside a run of motion (> 25 ms = a dropped frame), render time per
frame, and how long the screen kept moving after the last input. Each scenario runs once
unrecorded (warm-up) and then 3 times recorded. The hidden automation window is not
vsync-throttled: while something animates it renders every ~7 ms (≈150 Hz), so frame counts
are ~2.5× what a 60 Hz screen shows. Runs of async-load frames (body, thread) can chain into a
"run" too, which is where the ~49 ms gaps in click-mail / arrow-repeat come from (both before
and after; they are not animation drops).

Scenarios (`tools/motion-layout.json`, Bento dark 1400×900, the Sent folder: all read, so
opening mail writes nothing): pointer sweep over 7 rows (60 ms per row), click two mails,
20 arrow-key repeats at 33 ms, open + Escape a message context menu, open the "New subfolder"
prompt from the folder menu + close it on its backdrop, rail Mail → Calendar → Mail.

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
