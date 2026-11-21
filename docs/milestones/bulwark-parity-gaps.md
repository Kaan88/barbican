# Bulwark parity gap ledger

Goal (user decision, 2026-11-21): Barbican's default theme is a native clone of Bulwark webmail 1.21.2 on every
screen: design, feel, placement and behaviour. Bento and Nordic are built on top later and share the behaviour.
This ledger is the work source for that. It was produced by reading source only (no build, no run, no live
Bulwark session), so every row is **source** evidence; "verify" marks a claim that needs a native capture before a
fix is accepted. Paths: `<bulwark>` is the pinned Bulwark 1.21.2 tree; Barbican paths are relative to the repo root.
Bulwark line numbers are from that tree; Barbican line numbers are at `eba6efa`.

Link: [M6b UI parity](M6b-ui-parity.md) (coverage ledger), [UI guidelines](../../design/UI-GUIDELINES.md),
[motion rules](../../design/MOTION.md), open item catalogue [spec/gaps.csv](../../spec/gaps.csv) (G-ids are
cross-referenced where a gap already has one).

## 1. How to read it

- **Order**: areas run from what a user touches every day (shell, list, reader, composer) to rare surfaces.
  Inside an area, rows are ordered by visible impact. Section 3 is the cross-cutting feedback/motion area the
  coordinator asked for; section 5 groups everything into work packages with disjoint file ownership.
- **Size**: S under 2 h, M about half a day, L about a day or more (one worker).
- **Milestone note**: Calendar, Contacts, Files and Settings rows are M10 as assigned, but they inherit the shape-language gap (TH-04, FM-19), which blocks the default theme and is owned by WP-1/WP-2 (M6b).
- **MS** (owning milestone): M6b = mail, composer, add-on surfaces and anything blocking the default theme;
  M10 = general parity sweep; M7 = admin; M8 = Windows integration.
- **Structural finding that drives most rows**: the Bulwark look exists only as `IsBulwark` branches in the
  mail views and in `StyleState` (`src/Barbican.App/Theme/StyleState.cs`, 30 `IsBulwark` uses, all in
  `Views/List`, `Views/Reader`, `Views/Sidebar`, `Shell/ShellPage.xaml.cs`, `Theme`). Everything built in code
  (Settings, Calendar, Contacts, Files, dialogs, toasts, menus, search panel, composer cards) still uses the
  Bento shape language (pill buttons radius 15-18, panels radius 12-16, `Popup` brush) from
  `Styles/Controls.xaml` and literal `new CornerRadius(...)` values (120 `new CornerRadius(...)` literals in `Views/**`, `Shell/**`, `Controls/**`; 10/8/12/14
  dominate). Bulwark's shape language is `rounded-md` (6 px) controls, `rounded-lg` (8 px) dialogs/cards,
  `rounded-full` only for avatars, chips and the rail account.

## 2. Already matched (do not redo)

Evidence links point at the existing notes; nothing here is re-verified by this audit.

| Item | Evidence |
|---|---|
| Pane widths 56 / 256 / 384, flush panes, hairline seams, rail/sidebar/list surfaces | [M6b-ui-parity.md "First native pass"](M6b-ui-parity.md#first-native-pass), `StyleMetrics.cs:46,64,67` |
| Dark/light palette values (background, card, muted, border, foreground, muted-foreground, popover, ring, selection, selection-foreground, primary) equal `globals.css:46-93,125-187` | this audit, `Tokens.g.cs` bulwark-0 / bulwark-2 vs `<bulwark>/app/globals.css` |
| Reader fullscreen (icon-only 32 px, rail retained, exit restores x696) | [Native baseline](M6b-ui-parity.md#native-baseline-after-the-preview), [Review and follow-up](M6b-ui-parity.md#review-and-follow-up) |
| Quick-reply footer geometry, recipient hint, Ctrl+Enter single submit, inline send with checks and undo | [Native baseline](M6b-ui-parity.md#native-baseline-after-the-preview), [Inline reply delivery](M6b-solo-parity.md#inline-reply-delivery), [Composer footer walkthrough](M6b-solo-parity.md#bulwark-composer-footer-and-form-walkthrough) |
| Grouped inbox (one representative per thread, paging, folder-wide actions), reader older-message disclosure | [Grouped inbox preview](M6b-solo-parity.md#grouped-inbox-preview), [First native pass](M6b-ui-parity.md#first-native-pass) |
| Composer maximize/restore, dock divider (keys, Home/double-tap reset, remembered split), Discard beside Send, footer Tab order | [Composer maximize](M6b-solo-parity.md#composer-maximize-and-restore), [Reply dock divider](M6b-solo-parity.md#reply-dock-divider), [Composer footer walkthrough](M6b-solo-parity.md#bulwark-composer-footer-and-form-walkthrough) |
| List toolbar placement (select-all, search with filter toggle, folder-actions menu) | [Review and follow-up "Inbox chrome"](M6b-ui-parity.md#review-and-follow-up) (menu is a subset: ML-09) |
| Message list / Reading / Composing settings behave and persist across restart | [Nested Settings and restart](M6b-solo-parity.md#nested-settings-and-restart) |
| Managed add-on enable/disable/retry in Settings > Add-ons | [Managed add-on activation](M6b-solo-parity.md#managed-add-on-activation-and-takeover) |

## 3. Feedback and motion consistency (cross-cutting)

Requirement: button presses, hovers, focus states and transitions are consistent and not over the top.

### 3.1 Where Barbican defines feedback today (complete list from grep of `Theme/`, `Styles/`, `Motion/`, `Views/**`, `Shell/**`)

| # | Definition | Where | What it does |
|---|---|---|---|
| 1 | Button template | `Styles/Controls.xaml:36-60` | hover = `Hover` brush, press = `Pressed` brush plus `Track` border, disabled = opacity 0.45; all instant setters; used by `Button.Icon/Action/Chip` |
| 2 | Accent button template | `Controls.xaml:62-84` | no hover change, press = opacity 0.88, disabled 0.45 (`Button.Primary`) |
| 3 | Bento row | `Controls.xaml:162-194` | hover `Hover`, selected `Tile2` + 1 px line, press `Pressed`; instant |
| 4 | Nordic row | `Controls.xaml:204-236` | as 3 with a 3 px accent bar; instant |
| 5 | Folder row | `Controls.xaml:245-282` | selected = `Sel` pill + `Accent` border + `SelText` foreground; instant |
| 6 | Bulwark message row | `Controls.xaml:306-325` | hover `Hover`, selected `Sel`, press `Pressed`, bottom rule; instant; system focus visual on |
| 7 | Bulwark folder row | `Controls.xaml:335-352` | Bento template with radius 0, 2 px left border; system focus visual on |
| 8 | Focus visual flags | `Controls.xaml:94,109,121,135` (True), `:293,302,336,346` (False), `:306,352` (True) | Fluent two-colour system focus ring on buttons and Bulwark rows, none on Bento/Nordic rows |
| 9 | Focus thickness override | `FocusVisualPrimaryThickness` bound to `Look.FocusThickness` only on rail buttons (`RailView.xaml`), sidebar and floating compose (`SidebarView.xaml`, `ShellPage.xaml:61`) | the accessibility "focus ring" setting reaches 3 surfaces; every other control ignores it |
| 10 | Hand-set hover in code | `Views/Menus/MenuHostView.xaml.cs:253`, `Views/Palette/PaletteView.xaml.cs:190`, `Views/Search/SearchBoxView.xaml.cs:164`, `Views/Fmt.cs:193-195`, `Views/Files/FilesListView.cs:146` (selection), `Views/Calendar/*` pointer handlers | each sets `Background = Hover/Sel` itself, no shared state |
| 11 | Caption buttons | `Shell/ShellPage.xaml.cs:375-380` | hover/pressed colours copied from the palette |
| 12 | Motion tokens | `Tokens.g.cs` `Tokens.Motion/Ease`, `Themes/Tokens.xaml:44-52`, `Motion/MotionScale.cs` | Fast 100, List 100, Enter 150, Late 150, Exit 90, View 110, TipDelay 350, PopScale 0.96, Rise 8 px, enter curve (0.2,0,0,1), exit curve (0.4,0,1,1), ceiling 200 |
| 13 | PopIn | `Motion/PopIn.cs`; callers `DialogHostView.xaml.cs:100`, `MenuHostView.xaml.cs:86,112`, `PaletteView.xaml.cs:81`, `ShortcutSheetView.xaml.cs:55`, `RailView.xaml.cs:65` | fade 0 to 1 plus scale 0.96 to 1, 150 ms |
| 14 | FadeIn / Scrim | `Motion/MotionRuntime.cs`, `Motion/Scrim.cs` (unused), callers `DialogHostView.xaml.cs:99`, `PaletteView.xaml.cs:80`, `ShortcutSheetView.xaml.cs:54`, `Tip.cs` | fade 0 to 1, 150 ms |
| 15 | Rise | `Motion/Rise.cs`; callers `ShellPage.xaml.cs:191,241,244`, `MessageListView.xaml.cs:141`, `ReaderView.xaml.cs:193-194` | fade from 0.5/0.55/0.94 and slide 3-10 px, 110-150 ms |
| 16 | Body cross-fade | `MailBody/BodyShell.cs:450-452` + `Assets/mail/shell.js` `reveal` | 110 ms fade of the incoming message body |
| 17 | Tip (tooltip) | `Motion/Tip.cs` | 350 ms delay then 150 ms fade; radius 8, `Popup` brush |
| 18 | WinUI defaults left untouched | `App.xaml` merges `XamlControlsResources` and no override of any control resource (no `ToggleSwitch*`, `CheckBox*`, `TextControl*`, `ScrollBar*`, `MenuFlyout*`, `Expander*` key anywhere in `src/Barbican.App`) | `ToggleSwitch` x4, `CheckBox` x15, `RadioButton` x1, `TextBox` x45 (all but search/subject/quick-reply), `PasswordBox` x6, `MenuFlyoutItem` x6, `Expander` x2, `ProgressBar/Ring` x4, `ScrollViewer` x33 run Fluent templates: system accent colour, own hover/press/focus visuals and 83-167 ms internal animations |
| 19 | Default list item template | `Views/Calendar/AgendaView.xaml:17-24`, `Calendar/Tasks/TasksListView.xaml:27-34`, `Contacts/ContactsListView.xaml:45-52` set only padding on the stock `ListViewItem` | Fluent hover/press/selection indicator and press animation, not the Bulwark row states |
| 20 | Unused tokens | `Motion.Fast`, `Motion.List`, `Motion.Exit`, `Motion.Late`, `RisePx` have no reader in `src/Barbican.App` (grep) | the documented "feedback 100 ms ease" never runs: row/button feedback is an instant setter; exits never animate (toasts included, `ToastHostView.xaml:10-11`) |

### 3.2 Bulwark's actual feedback and motion (for comparison)

| Source | Value |
|---|---|
| `<bulwark>/components/ui/button.tsx:42` | every button `transition-all duration-200`; default hover `bg-primary/90 shadow-sm hover:shadow` (:48); ghost/outline hover `bg-accent text-accent-foreground` (:49,51) |
| `<bulwark>/components/ui/icon-button.tsx` | icon buttons are ghost 32 px (`icon-sm`), so hover is the **accent** tint, not muted |
| `<bulwark>/components/ui/toolbar.tsx:16,29,44` | active toggle `bg-primary/10 text-primary hover:bg-primary/15`; inactive `text-muted-foreground hover:text-foreground`; `transition-colors` |
| tailwind usage count (`components/**/*.tsx`) | `transition-colors` 371, `duration-150` 87, `duration-200` 38, `duration-300` 20, `duration-100` 4, `transition-opacity` 30, `transition-transform` 25, `transition-all` 28; `hover:bg-muted` 275, `hover:bg-accent` 53, `hover:scale-` 11, `active:scale-` 5 (touch long-press only, `thread-list-item.tsx:590,737`) |
| `<bulwark>/app/globals.css:106` | `--transition-duration: 0.2s` |
| `globals.css:570-606` | toast enter 0.32 s `cubic-bezier(0.21,1.02,0.73,1)` (overshoot), exit 0.28 s; slides from the end edge |
| `globals.css:901-905,1001-1057` | `.view-transition` 0.3 s ease-in-out; slide-in-from-right/left 0.3 s (phone views, sidebar drawer); calendar slide 0.25 s |
| `globals.css:967-984,987-998,1338-1366` | shake 0.4 s (rejected input), fade-in 0.2 s (popovers), settings-search-pulse 1.6 s |
| `globals.css:1062-1087` | reduced motion: every animation/transition 0.01 ms; position/visibility properties 0 |
| `components/ui/dialog.tsx:181,231` | scrim `bg-black/50 backdrop-blur-[1px]` `fade-in 150ms`; card `zoom-in-95 duration-200` |
| `components/ui/context-menu.tsx:663-667,710` | menu items `transition-colors duration-150`, `hover:bg-muted`, focus `bg-muted`; **no entrance animation** on the menu panel |
| `components/ui/settings-section.tsx` (`components/settings/settings-section.tsx:396-410`) | switch `transition-colors duration-150`, knob `transition-transform duration-150`, 24x44 px |
| `app/accessibility.css:115-136` | one focus style for every control: 2 px solid `--color-ring`, offset 2 px (inset -2 px for rows, options, menu items, tabs); 3 px / 5 px for the thick/extra setting; `data-focus-always` option |
| Sidebar/lists width | `mail-app.tsx:6427,6536`, `sidebar.tsx:2653`: `transition-[width] duration-300` on pane/sidebar width (not while resizing) |
| Row hover | `thread-list-item.tsx:720-745`: `transition-shadow duration-200`, hover `bg-muted shadow-sm`; avatar checkbox `hover:bg-muted/50 hover:scale-110` (:796) |
| Hover actions | `email-hover-actions.tsx:271-274`: `transition-colors duration-100`, `hover:bg-black/5 dark:hover:bg-white/10`; container `shadow-md border rounded-lg` (:339) |

Bulwark is itself not minimal (300 ms width transitions, an overshooting toast, 0.98 scale on touch). The user's
rule is "consistent and not over the top", so the target is: **Bulwark's hover/press look, with Barbican's shorter
durations, no overshoot, no scale**, applied from one token set. Where Bulwark animates something Barbican
deliberately keeps instant (width transitions, view slides), keep Barbican's behaviour and record it as an accepted
deviation rather than a gap.

### 3.3 Findings

**(a) Controls of the same kind that behave differently** (all `FM-*` rows below)

- FM-01 Icon buttons: `Button.Icon` hover = grey `Hover`; Bulwark `IconButton` hover = accent tint (`button.tsx:49`) and toolbar toggles get a primary tint when on (`toolbar.tsx:16`). Barbican has no "active/toggled" state for toolbar buttons at all.
- FM-02 Message/folder rows are custom templates, but Agenda, Tasks and Contacts rows (rows of the same kind) use the stock `ListViewItem` template (`AgendaView.xaml:17`, `TasksListView.xaml:27`, `ContactsListView.xaml:45`), Files rows hand-set backgrounds (`FilesListView.cs:146`), calendar sidebar and Contacts sidebar build their own row buttons.
- FM-03 Menus: context menus use the in-window `MenuHostView` (radius 12, row radius 8, PopIn), but `MenuFlyout` is used for composer add-ons, classification, proofreading and reader add-ons (`ComposerView.xaml:31,94,121`, `ComposerView.AddOns.cs:56`, `ReaderView.AddOns.cs:80`), which render the Fluent menu with its own animation and metrics. Calendar "more" menu (`Calendar/MoreMenuView.cs:18`), calendar hover card, search results, recipient suggestions (`RecipientFieldView.cs:48`) are four more hand-built popups.
- FM-04 Dialog-like surfaces: `DialogHostView` fades its scrim and PopIns its card; the calendar card host and its question card (`CalendarCardHost.cs:25-41`) have a scrim and card with neither; the reminder popup (`ReminderPopupView.cs:24,104`) has its own card; the account popover is a WinUI `Flyout` that runs both Fluent's flyout animation and `PopIn` (`RailView.xaml.cs:65`); the sender popover and sidebar account popover another `Flyout`.
- FM-05 Focus: Fluent two-colour focus visual on buttons and Bulwark rows, none on Bento/Nordic rows, thickness setting honoured on 3 surfaces only, text boxes use the Fluent bottom accent line, `ToggleSwitch`/`CheckBox` use their own. Bulwark has one 2 px `--color-ring` outline everywhere.
- FM-06 Pressed: `Button` press fills `Pressed` and recolours the border; accent press dims to 0.88; list-item press fills `Pressed`; Fluent controls press with their own scale/colour; Bulwark has no press state (only `hover`), except a 0.98 scale on touch long-press.
- FM-07 Selected-but-hovered: Bento/Nordic rows show `Tile2`, Bulwark rows `Sel`, folders `Sel` with accent border; Bulwark selected rows hover to `bg-accent` (`thread-list-item.tsx:731`), Barbican keeps `Sel`.

**(b) Motion longer, bouncier, scaling or more decorative than Bulwark's equivalent**

- FM-08 PopIn scale 0.96 + fade 150 ms on dialogs, palette, shortcuts, account popover. Bulwark dialogs use `zoom-in-95` 200 ms (`dialog.tsx:181`), so dialogs are comparable (Barbican shorter); keep but drive from one token. Not a gap for dialogs.
- FM-09 Reader subject/sender rise and body cross-fade 110 ms on every message switch (`ReaderView.xaml.cs:193`, `BodyShell.cs:450`): Bulwark has none (message switch is instant). This is decoration Bulwark does not have (see (c)).
- FM-10 Compact pane transition (`ShellPage.xaml.cs:244`, 3 px rise from 0.94) vs Bulwark phone `slide-in-from-right 0.3s` (`globals.css:1001`): Barbican is smaller; accepted.
- FM-11 No Barbican motion exceeds 200 ms and none bounces or overshoots (`MotionScale.CeilingMs = 200`); no scale on hover or press. The only scaling motion is PopIn 0.96. There is no "over the top" motion to remove in Barbican's own code; the over-the-top risk is the **un-themed Fluent defaults** (row 18/19): toggle knob stretch, checkbox check draw, scroll bar expand, expander slide (250 ms) and flyout entrance run with their own durations, easings and system accent colour.

**(c) Motion where Bulwark has none** (remove, or at least gate behind one token)

- FM-12 Message-list folder-change rise (0.5 opacity, 10 px, 150 ms, `MessageListView.xaml.cs:141`): Bulwark swaps rows with no motion (the only list motion is the `isLoading && opacity-50` dim, `email-list.tsx:1671`). Remove or reduce to the standard fade.
- FM-13 Reader subject/sender rise (FM-09) and message-body cross-fade: remove for the Bulwark theme.
- FM-14 Context-menu PopIn (`MenuHostView.xaml.cs:86,112`): Bulwark's menu panel has no animation (`context-menu.tsx:667` has no `animate-*`). Remove for menus and flyouts; keep scrim fade + card zoom for dialogs only (Bulwark has it there).
- FM-15 Tooltip fade 150 ms after 350 ms delay (`Tip.cs`): Bulwark tooltips are native `title` (delay, no fade). Keep the delay, drop the fade.
- FM-16 Mail screen rise on a style switch (`ShellPage.xaml.cs:191`): a Bento/Nordic decoration; must not run for the default theme.
- FM-17 Mail "screen" and pane Rise on compact navigation: keep (Bulwark animates it, longer).

**Missing where Bulwark animates and the rule allows it** (hover is not instantaneous in Bulwark): colour changes on hover/active ease over 150 ms (`transition-colors`, 371 uses). Barbican snaps. Decide once: either all hover/press colours ease over `FastMs` through one shared state brush transition, or all snap. Recommendation: snap on lists (rows, as MOTION.md rule 3), 100 ms colour ease on buttons, toggles and menu rows, from one token. This is FM-18.

### 3.4 Feedback and motion ledger

| ID | Gap | Bulwark | Barbican now | Impact | Size | MS |
|---|---|---|---|---|---|---|
| FM-01 | Icon button hover/active state differs; no toggled state | `<bulwark>/components/ui/button.tsx:49`, `toolbar.tsx:16,29` | `Styles/Controls.xaml:36-60` (grey `Hover`, no active) | every toolbar/rail/sidebar button | M | M6b |
| FM-02 | Same-kind list rows behave differently (agenda, tasks, contacts stock template; files hand-set) | `thread-list-item.tsx:720-745` family: one row look | `AgendaView.xaml:17`, `TasksListView.xaml:27`, `ContactsListView.xaml:45`, `FilesListView.cs:146` | hover/selection looks different per app | M | M10 |
| FM-03 | Two menu systems (custom host vs WinUI `MenuFlyout`) and four hand-built popups | `context-menu.tsx:663-667` single item class | `ComposerView.xaml:31,94,121`, `ReaderView.AddOns.cs:80`, `Calendar/MoreMenuView.cs:18`, `RecipientFieldView.cs:48` | composer menus look like Windows, not like Bulwark | M | M6b |
| FM-04 | Dialog-like surfaces have different scrim/entrance; account flyout animates twice | `dialog.tsx:181,231` one scrim and card | `CalendarCardHost.cs:25-41`, `ReminderPopupView.cs:24,104`, `RailView.xaml.cs:65` | inconsistent open feel | M | M6b |
| FM-05 | No single focus ring; setting reaches 3 surfaces | `app/accessibility.css:115-136` | `Controls.xaml:94-352`, `StyleState.cs:FocusThickness`, `RailView.xaml` | keyboard users see different rings; accessibility setting ignored | M | M6b |
| FM-06 | Press state set differs per template; Bulwark has none | `button.tsx:42-51` (hover only) | `Controls.xaml:46-50,71-75,170-173` | extra grey flash on press | S | M6b |
| FM-07 | Selected+hover state differs from Bulwark (`bg-accent`) | `thread-list-item.tsx:731` | `Controls.xaml:181-186,318-319` | list feels flatter | S | M6b |
| FM-08 | Dialog open uses PopIn token; keep, drive from the shared token set | `dialog.tsx:181,231` | `DialogHostView.xaml.cs:99-100` | none (accepted) | S | M6b |
| FM-09 | Reader switch rise + body fade has no Bulwark equivalent | none | `ReaderView.xaml.cs:188-194`, `BodyShell.cs:450-452` | message switch feels animated, Bulwark is instant | S | M6b |
| FM-12 | Folder-change row rise (0.5, 10 px) | none | `MessageListView.xaml.cs:136-142` | list flickers/lifts on folder click | S | M6b |
| FM-14 | Menu PopIn where Bulwark has no animation | `context-menu.tsx:667` | `MenuHostView.xaml.cs:86,112` | menus scale in | S | M6b |
| FM-15 | Tooltip fade | native `title` | `Motion/Tip.cs:105-106` | negligible | S | M6b |
| FM-16 | Shell rise on style switch | none | `ShellPage.xaml.cs:191` | only on style change | S | M6b |
| FM-18 | Hover colour easing policy undefined; 5 motion tokens unused | `transition-colors` 150 ms (371 uses) | `Themes/Tokens.xaml:44-52`, `MotionScale.cs:14-22` | feel mismatch on every hover | M | M6b |
| FM-19 | Fluent defaults un-themed: toggle, checkbox, radio, text box, password box, scroll bar, expander, progress | `settings-section.tsx:396-410` (switch), `input.tsx:13-18` (field), `app/globals.css` scrollbar rules | no resource overrides in `src/Barbican.App`; 45 `TextBox`, 15 `CheckBox`, 4 `ToggleSwitch`, 6 `PasswordBox` | every settings, composer card, dialog field | L | M6b |
| FM-20 | Accent colour is the system accent, not Bulwark `primary` (`#185aea` light / `#fafafa` dark) for Fluent controls | `globals.css:37,63` | no `SystemAccentColor*` override (grep empty) | toggles, checkboxes, focus fills blue/purple per Windows | S | M6b |
| FM-21 | Reduce-motion has no equivalent for Fluent internal animations | `globals.css:1062-1087` blanket | `MotionScale` applies only to Barbican's own storyboards | reduce motion partly ineffective | M | M6b |
| FM-22 | Toast enter/exit: Barbican none; Bulwark slides 0.32 s with overshoot (do **not** copy the overshoot) | `globals.css:570-606` | `ToastHostView.xaml:10-11` | toast pops in; accepted unless a slide is wanted (see MN-06) | S | M6b |

**Proposed single work package: WP-1 "Shared state tokens and feedback"** (section 5). One new shared dictionary
(`Styles/Feedback.xaml`) defines the only hover/press/active/selected/focus/disabled brushes and a `Motion.Fast`
Storyboard helper; `Controls.xaml` templates, hand-set hover sites and Fluent overrides all read it; durations
come from one place (Fast 100, Enter 150, Exit 90, nothing over 200, no scale/overshoot), animation removed per
FM-09/12/14/15/16, reduce motion applied to the overrides.

### 3.5 WP-1 status (422181d, branch m6b/wp1-feedback)

| ID | Status | Note |
|---|---|---|
| FM-01 | closed | `Button.Icon/Action/Chip` hover with the accent tint (Bulwark) and `ToggleButton.Icon` has the primary-tint active state (`FeedbackActive*`). Screens must switch their toolbar toggles to `ToggleButton.Icon` |
| FM-05 | closed for every control that takes the shared styles | one ring colour (`SystemControlFocusVisualPrimaryBrush` = live `Ring`, secondary transparent), 2 px, widened by the Accessibility setting through `Theme/FocusRing.cs`; rows inset it. Text boxes, toggles, check boxes still draw the Fluent focus line: WP-2 |
| FM-06 | closed | no pressed border change; pressed is one brush step stronger than hover; accent button dims through `FeedbackPrimaryPressed` |
| FM-07 | closed | selected row = `FeedbackSelected` (Bulwark accent), the same on hover |
| FM-18 | closed | policy: buttons 200 ms, toggles/rows-that-recolour 150 ms, menu rows 150 ms, hover actions 100 ms (system brush transition); list rows snap. `FastMs` is now used (hover actions). `ListMs`, `LateMs`, `ExitMs`, `RisePx` remain unread (WP-5: selection highlight, WP-8: toast exit/slide) |
| FM-20 | closed for Fluent brushes | `AccentFillColor{Default,Secondary,Tertiary}Brush`, `TextOnAccentFillColor*`, `AccentTextFillColorPrimaryBrush` are the primary palette brushes (live). The `SystemAccentColor*` Colour keys are not overridden |
| FM-21 | partly | `ControlFaster/Fast/NormalAnimationDuration` follow the tokens and reduce motion (`Motion/FluentMotion.cs`); a template already on screen keeps its length until rebuilt. WP-2 finishes it when it re-templates the controls |
| TH-01 | closed | `Destructive`, `Success`, `Warning`, `Info`, `Secondary`, `Input`, `PopoverFg`, `AccentFill`, `AccentFillFg`, `Unread`, `Chart1..5` (+ `Fg`) in every variant; `selection-foreground` is `SelText` |
| TH-02 | closed | Bulwark dark `Hover` = `#262626` |
| TH-03 | closed | Bulwark light `Pressed` = `#e2e8f0`, below `Hover` `#f1f5f9`; test: pressed is stronger than hover in dark and light for every style |
| TH-04 | closed for tokens, partly for screens | `Shape.Bulwark.xaml` (6 / 8), `StyleMetrics` reads it for Bulwark (`ButtonRadius`, `FieldRadius`, `NavRadius`, `RowRadius`, `PopupRadius`, `SummaryRadius`, new `DialogRadius`, `MenuRadius`, `MenuItemRadius`, `ToastRadius`; `Round()` is a circle). Literal `new CornerRadius(...)` in views and `Style` setters (`Button.Chip` 15) are for the screen packages |
| TH-09 | closed | the Bulwark palette lives in `theme.slint` (`export global Bulwark`); gen-tokens requires it to define exactly the colours of `Theme` |
| AX-01 | partly | one ring and the width setting; `focus-always` (ring on pointer focus) is not implemented |

**Adopt in the screen packages (after WP-1):**

- Buttons: use `Button.Icon`, `Button.Action`, `Button.Primary`, `Button.Chip`; toolbar toggles `ToggleButton.Icon` bound to their state; hover actions over a row `Button.HoverAction`. Set the corner radius from `StyleState` (`ButtonRadius`), not a literal.
- Menus and popups (WP-8): menu rows `Button.MenuItem` / `Button.MenuItem.Destructive` (`StyleState.MenuItemStyle`, `MenuItemRadius`, panel `MenuRadius`, dialog card `DialogRadius`, toast `ToastRadius`); remove the hand-set hover in `MenuHostView.xaml.cs:253`, `PaletteView.xaml.cs:190`, `SearchBoxView.xaml.cs:164`; pass `PopInKind.Menu` to `PopIn.Run` for menus and flyouts (FM-14, instant in Bulwark); drop the `Tip` fade in Bulwark with `MotionRuntime.DecorationMs` (FM-15).
- Lists: agenda, tasks, contacts, files rows take `ListItem.Row` (FM-02: WP-12, 13, 14; replaces the stock template and `FilesListView.cs:146` hand-set selection). Use the `FeedbackHover/Selected/Pressed` brushes for any state a template draws itself (`Fmt.cs:193-195`, calendar pointer handlers, caption buttons `ShellPage.xaml.cs:375-380`).
- Motion Bulwark does not have, to switch off with `MotionRuntime.DecorationMs(ms)` (0 in the Bulwark style, unchanged in Bento and Nordic): list folder-change rise (`MessageListView.xaml.cs:141`, FM-12, WP-5), reader subject/sender rise and body cross-fade (`ReaderView.xaml.cs:193`, `BodyShell.cs:450`, FM-09, WP-6), shell rise on a style switch (`ShellPage.xaml.cs:191`, FM-16, WP-10). The compact-navigation rise (`ShellPage.xaml.cs:244`) stays (FM-10, FM-17).
- Focus: set `UseSystemFocusVisuals="True"` on custom focusable elements and remove per-control `FocusVisualPrimaryThickness` bindings; the global ring supplies colour and width.
- Do not edit `Styles/Controls.xaml` or `Styles/Feedback.xaml`; ask WP-1 or define a local style in your own `Styles/<Area>.xaml` that is `BasedOn` a shared one.
### 3.6 WP-2 status (branch m6b/wp2-fluent)

`Styles/FluentOverrides.xaml` (merged after `Controls.xaml` by `AppResources.EnsureStyles`) restyles the stock controls from the shared palette and `Feedback*` brushes; `Theme/FluentShape.cs` writes the two radii it needs (`Fluent.FieldRadius`, `Fluent.MenuRadius`) per style.

| ID | Status | Note |
|---|---|---|
| FM-19 | closed for the named controls | ToggleSwitch (own 44x24 template), TextBox (own template, no clear button), PasswordBox, CheckBox, RadioButton, ScrollBar, ProgressBar, MenuFlyout presenter and rows, Expander: Bulwark colours, hover and focus (`input.tsx:13-18`, `settings-section.tsx:374-410`). ComboBox, Slider, DatePicker and other stock controls are not restyled |
| FM-20 | closed | the accent resources are the primary brushes (WP-1); CheckBox, RadioButton, ProgressBar and the switch read `Accent` / `FeedbackPrimary*` directly |
| FM-21 | closed for the restyled controls | the switch uses `ControlFastAnimationDuration` (follows `ColorMs`, 0 with reduce motion). The stock TextBox and PasswordBox border change is instant here; the ScrollBar expand/contract durations (`ScrollBarExpandDuration`, 100 ms) and the stock CheckBox/RadioButton storyboards stay the library's (all <= 167 ms, no overshoot) and do not follow reduce motion |
| ST-03 | closed | 44x24 track, 16 px knob, 20 px travel on the 150 ms Tailwind curve, no hover/pressed (Bulwark has none), disabled opacity 0.5 |
| TH-07 | closed | 8 px scroll bars, border-colour thumb, muted-foreground on hover, transparent track. The Fluent thin resting indicator (4 px) and the arrow buttons remain; the "native scrollbars" accessibility switch is not built |

Limits: a radius is read when a control's style is applied, so a text box created before a style switch keeps its radius until rebuilt. TextBox minimum height is 40 (`h-10`); a view that needs a denser field sets its own `MinHeight`. The text box border colour changes at once (the brush transition API covers only the background).

### 3.7 WP-3 status (branch m6b/wp3-rail)

| ID | Status | Note |
|---|---|---|
| SH-01 | partly | order is now Bulwark's: Search first, then Mail, Calendar, Contacts, Files; footer Accessibility, Settings, divider, Shortcuts, storage ring, account (`navigation-rail.tsx:584-760,700-949`). Bell, plugin pages, add-on apps and Admin do not exist yet (SH-08..10); sign-out is in the account popover and, with `showRailAccountList`, a rail menu |
| SH-02 | closed | the five navigation buttons are `ToggleButton`s on the rail template: active = `FeedbackActive`/`FeedbackActiveFg` (Bulwark primary/10, primary), hover = `FeedbackHover` (muted) + `Text`, pressed one step stronger, 150 ms colour transition (`navigation-rail.tsx:640-652`). Settings is a nav toggle too |
| SH-03 | closed | 18 px icon in the 40 px box, 4 px gap (`StyleState.RailIcon`; Bento/Nordic keep 20) |
| SH-04 | closed | red-600 pill (16 px, 10 px bold) at -top-0.5 -end-0.5, or the 10 px red-500 dot for the "dot" unread style (`navigation-rail.tsx:659-668`) |
| SH-06 | closed | the storage ring is a 32 px button (r 12, stroke 3, level colour success/warning/destructive at 70/90 %, percentage inside); its popover lists used, free and total and the bar (`navigation-rail.tsx:164-221`). Free is the total less the used share of `StorageText` (Presentation exposes the text, not the byte counts) |
| SH-07 | partly | account circles, active ring, dimmed others, Add account and the Sign out menu behind `showRailAccountList` and not simple mode (`navigation-rail.tsx:790-890`). WP-15 flipped the row to wired in `SettingsCatalog.UnwiredKeys` / `spec/settings.csv` |
| SH-09 | open | no Admin entry: admin is M7 (no `AppId.Admin`, no update-dot source) |
| SH-10 | partly closed | add-on pages are in the rail: `ui.setPages` / `ui.openPage` fill `PluginPageRegistry`, `RailViewModel.PluginPages` lists them after the built-in apps (same button, 18 px icon, active tint and in-window tooltip "title (add-on)", puzzle piece without an icon), a click or `ui.openPage` shows `PluginPageScreen`, a disabled or removed add-on loses its entries and an open page. The add-on's own `app-page` frame now mounts inside the page (`PluginPageFrameHost`). Still open: the user-defined sidebar apps (URL apps with open mode and icon, the divider and "Add app" button, `sidebar-apps-modal.tsx`) and the managed apps list: they need a `sidebarApps` setting with persistence and an inline web view that nothing has yet |
| (popover) | closed | the account popover is Bulwark's card: 288 px, square rows px-3 py-2.5 with a 36 px avatar, check badge, amber star, primary unread text, border-t sections, Manage accounts row (`account-switcher.tsx:353-682`); rail account button is the 40 px button with a 32 px tinted avatar and the other-accounts dot (`:310-372`); flyouts use `PopInKind.Menu` (instant in Bulwark) and the shared radii |

### 3.8 WP-8 status (branch m6b/wp8-dialogs)

Default (Bulwark) theme matched; Bento and Nordic keep their look (radii come from `StyleState`, where those styles equal the old literals). Every popup stays in-window; the one new flyout (the add-on dialog's options menu) is a `MenuFlyout` with `ShouldConstrainToRootBounds` (`popup-guard`: constrained).

| ID | Status | Note (Bulwark citation) |
|---|---|---|
| MN-01 | closed | `DialogHostView`: card `DialogRadius` 8, padding 24, title 18 semibold, message 14 muted 8 px under it, footer 24 px above, buttons h-10 with `ButtonRadius`, `gap-3` 12 px, scrim black/50 (`ui/dialog.tsx:131-134,181,231`; `ui/button.tsx:42-56`). Bulwark's confirm and prompt have no close X (`showClose` false, `confirm-dialog.tsx:140`, `prompt-dialog.tsx:103`). Not built: the 1 px backdrop blur |
| MN-02 | closed | destructive confirm: 40 px circle (`FeedbackDestructiveHover` fill) with a 20 px triangle beside the text column, solid `Destructive` button (hover 90 %, pressed 80 %; `Views/Dialogs/DialogStyles.xaml`) (`confirm-dialog.tsx:143-170`, `button.tsx:52-53`) | Reach: every destructive confirm is a `ConfirmDialogRequest` with `Danger` (contacts, files, settings, add-ons) or, in the calendar, a `ComposerDialog` with a `Danger` choice: `CalendarCardHost.QuestionCard` draws the same 40 px circle (`DialogStyles.IconBadge`, `DestructiveIcon`) and the solid destructive button; discarding unsaved changes keeps no icon (as Bulwark's close dialog).
| MN-03 | closed | the content dialogs of Contacts (directory, group, import, merge, print), Files (upload conflict, move), Settings (rule editor, rule apply), Calendar (create calendar, subscription, import, the question card), the mailto account picker, the reader print preview and the add-on file preview use `DialogStyles.FooterButton` / `Footer` / `Description` (card, title, text and `gap-3` footer of `ui/dialog.tsx:131-134,181`; Cancel first, the action last; the safe or primary button starts focused through `IDialogContent.InitialFocus`). The module header of each cites its Bulwark dialog. Left: the contact form dialog is the inline `ContactsFormPanel` (its own Cancel/Save row, `contact-form.tsx:1439-1447`); the calendar event and task editors are a side sheet; the upload conflict keeps Keep both as the primary button (G-016) where Bulwark makes Replace primary (`upload-conflict-dialog.tsx:40,47`); Barbican adds Previous/Next to the merge dialog. |
| MN-04 | partly | toast: bottom-right (`bottom-5 end-5` as 15 px stack margin + 5 px card margin), 360 px, `ToastRadius` 6, status icon 18 px (check and info neutral, triangle amber for a warning, red for an error), message 13.5 medium, add-on source line with the puzzle glyph at 11 px, actions and a close X at the end (`ui/toast.tsx:92-107,243-261,281-330,545-561`). Not built: the countdown progress bar, pause on hover / focus, swipe to dismiss, the shortcut that focuses the newest toast, action wrap below the text (the service owns the timers; `Presentation/Actions/ToastService` unchanged) |
| MN-05 | partly | stack width 360 and the 220 px scroll cap for progress toasts; `max-w-[calc(100vw-2.5rem)]` and the phone layout are not built |
| MN-06 | closed | Bulwark style only: 150 ms slide in from the end edge on the enter curve, no overshoot; exit is instant (`globals.css:570-606` not copied) |
| MN-07 | not in this package | `Views/Accounts/AccountPopoverView.xaml`, `Views/Rail/RailView.xaml:107-117`, recipient popover: owned by the Accounts, Rail and Compose packages |
| MN-08 | partly | `Tip`: no fade in Bulwark (`DecorationMs`), 4 px radius (native `title` tooltip, `ui/icon-button.tsx`). Adding `Tip.Text` to buttons that have none is per screen (M10); new automation command `tip-show ID` shows a tip in a hidden run |
| MN-09 | closed | palette: scrim black/50, 576 px card at 12 vh, `DialogRadius`, search icon + borderless 48 px input + Esc keycap + rule, group label 11 px semibold, rows 36 px `ButtonRadius` with `FeedbackHover`, keycaps with border (`command-palette.tsx:336-418`). Shortcuts sheet: scrim, radius, title 18, underlined tab list, 14 px section heads, 14 px muted labels, bordered keycaps (`keyboard-shortcuts-modal.tsx:92-209`); its two-column grid and footer link are not built |
| FM-03 | not in this package | the four hand-built popups and the composer `MenuFlyout`s (`ComposerView.xaml:31,94,121`, `ReaderView.AddOns.cs:80`, `Calendar/MoreMenuView.cs:18`, `RecipientFieldView.cs:48`) belong to the Compose, Reader and Calendar packages; the shared host now gives them a target look to follow |
| FM-04 | partly | the dialog host, palette and sheet share one scrim (0x80) and one entrance; `CalendarCardHost.cs`, `ReminderPopupView.cs`, `RailView.xaml.cs:65` are outside this package | The calendar question card now fades its scrim and pops in with the shared shadow (`CalendarCardHost.Refresh`); the card of the calendar host and `ReminderPopupView.cs` are not moved.
| FM-08 | closed (kept) | dialog scrim fades over `Motion.Enter` 150 ms (`dialog.tsx:231`), the card zooms from `PopScale` (`dialog.tsx:181`) |
| FM-14 | closed | menu and flyout cards enter with `PopInKind.Menu`: instant in Bulwark (`context-menu.tsx:667`) |
| FM-15 | closed | see MN-08 |
| FM-22 | closed | see MN-06 |
| MX-01 | closed | menu panel `MenuRadius` 6, vertical padding 4, rows 32 px (`MenuLevel.RowHeight`, set by the host), `px-3 text-sm gap-2`, icon column only when there is an icon, check in the icon column for icon-less check items, 16 px icons, bordered 11 px shortcut keycap, submenu chevron 16 px muted, square rows (`MenuItemRadius`), hover `FeedbackHover` eased 150 ms (`FeedbackKind.Menu`), destructive hover `FeedbackDestructiveHover` + `Destructive` text, destructive icon red (`context-menu.tsx:657-749,962`). Not built: `shadow-lg`, row `hint` second line, the 44 px coarse-pointer rows |
| MX-02 | partly | folder menu order follows `mailbox-context-menu.tsx:309-466` (SB-07 below); the other menus were not re-diffed |
| MX-03 | open (M10) | narrow-screen sheet |
| SB-07 | audited, partly | `MENU-FOLDER` follows `mailbox-context-menu.tsx:309-466`: the folder path as the header, Mark folder as read (off with nothing unread), Mark folder & subfolders as read (folders with children, off when nothing unread below), —, New subfolder and Rename (Rename only for custom folders), Rules (not in Trash, Junk, Sent or Drafts), —, Import (not in Trash or Junk), Export x2 (off for an empty folder), Refresh, then the danger rows (Empty trash / Empty spam off for an empty folder, Delete folder). Still missing for lack of commands or data: Share, Mute notifications, the shared-account "New folder". `MENU-LIST-ACTIONS`, the list toolbar's folder-actions button, is Bulwark's `MailFolderMenu` (`mail-list-menus.tsx:141-254`): Refresh, Sort ›, Group by date, Mark folder as read, Mark all folders as read, then Empty trash / Empty spam in Trash and Spam. `FOLDERS-HEADER` (`mailbox-context-menu.tsx:251-282`): Mark all folders as read, —, New folder, Refresh. Bulwark has no header row (Barbican keeps one); `MENU-TAG` has no Bulwark counterpart (sidebar tag rows only select, `sidebar.tsx:1160-1180`): kept as a Barbican extra with the same look |
| Add-on dialog | closed | puzzle glyph beside "From the add-on <name>" (12 px muted), title 18 (the host's), buttons 12 px apart and h-10, solid destructive confirm, header options button with "Turn off <name>" which disables the add-on and closes the dialog (`plugin-dialog-host.tsx:317-328,459-466,583-621`). The "prompt as its own window" of Bulwark desktop is not copied (in-window host, ADR 0007) |

Verification (Windows, Automation build, hidden run on port 9328, FakeJmap sample account): `tools/dialog-probe-replay.py` 244 passed, 0 failed (dark and light). Geometry read with UIA and the new `popup-bounds` command: confirm card (title, 8 px to message, 40 px buttons), menu rows 32 px with the text 36 px from the row edge, palette card 576 px, toast host 360 px at the right edge with Undo and a 24 px close button, add-on dialog (options button in the header row, "Turn off" item in a constrained `MenuFlyout`, invoking it disables the add-on and closes the dialog). Whole-window captures (`wshot`) were blank or stale on this machine (display idle), so no current screenshots exist; `artifacts/auto/9328/wp8/dark-confirm-stale-frame.png` is one valid older frame of the destructive confirm (before the 12 px gap fix) and is not committed.

### 3.9 WP-11 status (branch m6b/wp11-settings, partial)

Default (Bulwark) style matched for the frame, rows, choices, Add-ons and the settings dialog footers; Bento and Nordic keep the earlier look (`Theme/StyleState.Settings.cs` holds every value, Bulwark and older). Open items are listed last.

| ID | Status | Note (Bulwark citation) |
|---|---|---|
| ST-01 | closed for the frame | nav column 256 px on `Side` with a border-e, rail beside it, h-14 head with the border-b and "Back to mail" (the history Back and Forward stay as two icon buttons: Barbican has no browser chrome), search h-8 with its icon, groups separated by a rule and headed "Mail" and "Apps" (11 px semibold muted), rows py-2 ps-3 text-sm with a 16 px line icon and `FeedbackSelected` / hover `FeedbackHover` (the open page is medium weight), search results listed under their page as indented text-xs rows ("No matching settings"), foot with border-t and the Advanced switch; page column max-w-3xl (768) centred with px-6 py-6 and no visible title (`settings-app.tsx:1188-1360`, `settings-section.tsx:157-171`). Help links (operator-set) and the resizable nav are not built. The `assistant` entry follows the bundled add-on: it leaves the list when `disabledPlugins` holds it (and returns when it is switched on); the open page stays listed |
| ST-02 | closed | section title text-lg medium with space-y-8 above, description text-sm muted; row py-3 with the border-b (none under the last row of a section), label text-sm medium, description text-xs muted mt-1; an advanced row shows an "Advanced" badge; with the switch off, "N advanced settings are hidden on this page" with "Show advanced settings" (`settings-section.tsx:136-153,172-180,222-342`). Reveal-on-search of one advanced row (settings-p3-7) is not built |
| ST-03 | closed by WP-2 | |
| ST-04 | closed | choices are Bulwark's RadioGroup: a wrapping row (max 480) of px-3 text-xs rounded-md options, the chosen one primary and medium, the others `Input` (bg-muted) with the shared hover (`settings-section.tsx:278-305`); the drop-down is not used (ADR 0007). Colour swatches have no page yet (ST-06) |
| ST-10 | partly | the server pages (identities, templates, folders and tags, filters, out of office, recover) take the Bulwark column, section titles, bordered rounded-lg cards, h-8 buttons and select-style cycle buttons; their layouts were not re-diffed row by row. A `folders` or `tags` anchor (the sidebar gears) scrolls Folders and tags to that section once its data has drawn (`SrvUiPage.Reveal`) |
| AO-01 | closed for the installed list | "Plugins" section with its description, install button and hint, one bordered rounded-lg card per add-on: name, badges ("Yours", "Managed", "Built-in", "Problem"), description, version and state, the switch at the end (`set.addons.enable.<id>`, `.managed.toggle.<id>`, `.bundled.toggle.<id>`), and a click on the card opens the details (what it can do, Start, Review websites, Remove) (`plugins-settings.tsx:101-300`). The sidebar apps section has no counterpart (SH-10). A managed add-on's Retry label is now just the switch plus "Needs attention" |
| AO-02 | closed by WP-8 | add-on dialog and toast attribution |
| MN-03 | closed | see the gaps table: Settings rule editor / rule apply (Cancel then the action) and the shared footer helpers; the accessibility quick panel keeps its own bound sizes |
| AO-03 | page frame done, other mounts open | the rail pages are listed (SH-10) and the page's `app-page` frame is mounted through the Slot-kind runtime (`PluginSlotFrames`); day labels draw in the calendar; the other mount points (dialog, side panels, list filter chips, sidebar sections, print themes) belong to the plugin host work |
| ST-05..09 | open | not part of this pass (unwired rows, themes, i18n, security pages) |

Limits: a view built before a style switch re-reads its metrics only where it listens (the page column, `SetUiRowsView`, the nav, `SrvUiPage`); hand-built pages (Account, Security, Backup, Add-ons) keep their old section sizes until reopened after a restart. A page column is given an explicit width (`SetUiUi.FitColumn`) because a column capped only by `MaxWidth` is placed by its widest child.

Verification (Windows, Automation build, hidden run on port 9336, sample account, `set-set theme`): captures of General, Account, Appearance, Add-ons, Identities and Folders and tags (anchor `tags`) in dark and light, and the rule editor dialog in both, in `artifacts/wp11/` (not committed). Presentation 1,196 passed, Core `*Setting*` 52 passed, Automation 245 of 246 (the one failure is `PopupLintTests`, whose three files belong to other packages), rules, spec-check, win and desktop builds clean.

### 3.10 WP-10 status (branch m6b/wp10-shell)

Default (Bulwark) theme only unless stated; Bento and Nordic keep their frame (title band with the app name, rail, tiles). Every popup stays in-window: the drawer, scrim and tab bar are ordinary elements of the shell grid.

| ID | Status | Note (Bulwark citation) |
|---|---|---|
| SH-11 | closed for the mail screen | phone layout below 600 px (`ShellLayout.PhoneBelow`, `use-media-query.ts:12-33`): no rail, a bottom tab bar (`Shell/PhoneTabBar.cs`: Mail, Calendar, Contacts, Files, Settings as available, 44 px items, 20 px icon over a 10 px medium label, accent colour and a 16 x 2 bar for the current app, unread pill or dot, `navigation-rail.tsx:390-445`), hidden while a message, the composer or the folder drawer is open (`mail-app.tsx:6432-6447`). The folders are a drawer from the start edge over the list with the shared black/50 `Scrim` (a click on it, Escape, a folder choice or leaving Mail closes it; focus goes in and returns to the opener; `edge-drawer.tsx:36-66`). Deviations: the drawer slides in over the 110 ms view token and closes at once (Bulwark 300 ms; design/MOTION.md caps motion at 200 ms and has no exit), and keeps the sidebar's own width (about 256 px) not 288 px (`SidebarView` sizes itself). The Calendar, Contacts, Files and Settings screens still show their own rail on a phone (their packages) |
| SH-12 | done (pending merge) | `mailLayout` split / focus / horizontal rearrange the mail screen (Bulwark style; `MailLayoutRules`, `ShellPage.ApplyMailPanes`); Mountain View and Simple mode force focus without rewriting the saved value and hide the setting; the stacked list height has its own resize handle (`panes.json` `listHeight`). Not done: one-line list rows and the layout preview (`use-list-row-shape.ts`, `layout-preview.tsx`) |
| SH-13 | closed except easing | `PaneHandle`: 12 px hit area with a 4 px line, tint of the primary colour 30 % on hover, 40 % on keyboard focus, 50 % while dragging, resize cursor (Windows), separator with a range value (width, limits 180-400 / 240-600; `PaneHandlePeer`), `resize-handle.tsx:122-175`. Width easing stays an accepted deviation; the keyboard step `PanesViewModel.KeyStep` is 10 px since WP-15 (`resize-handle.tsx:33`); the 48 px fold is SB-02 |
| SH-14 | partly | the title text is gone in the Bulwark style and the band is 32 px (the caption buttons' height) instead of 38; it stays the drag area (`globals.css:615-640`, `window-controls-host.tsx`). Bulwark has no row at all (the content runs to the top and the controls sit in the first row of the column); that needs every screen's header to leave room for the caption buttons, so the rail still starts below a blank 32 px |
| SH-15 | verified, no change | no window minimum is set anywhere (`grep` of `Shell/`, `Composition/`, `App.xaml.cs`); `ShellLayout.MinWidth` 960 x `MinHeight` 640 is only the planning floor the Bento and Nordic layout tests use. Phone mode now begins at 600 like Bulwark (was 700); with no enforced minimum a window can reach it (the automation `resize` clamps at 400). A Bulwark minimum would have to be below 600 |
| RS-01 | closed for mail | see SH-11 |
| RS-02 | open | the reader toolbar still wraps (`Controls/WrapRow.cs`) and its labels follow the "show toolbar labels" setting, not the width. A collapse-to-icons mode needs `ReaderView` (WP-6) to mark the label text blocks; a `WrapRow` that hides them while measuring risks a layout cycle and was not attempted |
| AX-02 | partly | the frame's own buttons (folders opener, sender-panel toggle, reply-dock disclosure, tab bar, sign-in buttons) keep 44 px with large targets (`button.tsx:30-37`); other screens' buttons are theirs |
| AX-03 | partly | focus rescue for the list (`focus-rescue.tsx:1-30`): when a row is deleted or moved while the list had the focus and the focus fell to the page, it returns to the list — to the open row when there is one, else to the list itself (`Presentation/Shell/FocusRescue.cs`, `MessageListView.ReturnFocus`); it never moves a focus that is somewhere else. The drawer returns focus to its opener. Pane cycle F6 unchanged. Tab order of the frame is rail, folders, folder handle, list, reader, context (explicit `TabIndex`; the folders are declared after the surface so the phone drawer draws above it) |
| AX-04 | closed for the list | a folder that finished loading ("Inbox: 5 messages") and a search's result count are announced once through a UIA notification on the list after 600 ms (`use-mail-status-announcements.ts:30-62`) |
| FM-16 | closed | the shell rise on a style switch only runs for Bento and Nordic (`ShellPage.xaml.cs`, `MotionRuntime.DecorationMs`; zero when the target style is Bulwark) |
| Sign-in | closed (look) | backdrop = sidebar colour with an 8 % primary wash over the first 65 % (`login.css:19-76`), card 400 px max (16 px page padding), 1 px border, 8 px radius, centred 64 px logo, 24 px semibold "Sign in", muted subtitle, 32 px padding and 16 px between rows, full-width 40 px sign-in button with an icon, "or" rule above the browser button (`login/page.tsx:1672-1680,1795-1818`, `login.css:97-110`); the title band takes the wash colour; scrolls in a short window. Bulwark's soft card shadow, language/theme menu and footer links are not built. The remembered-accounts suggestions list is a separate gap |
| Glance strip, context panel | n/a | Bulwark has neither; they stay Bento (strip, panel) and Nordic (panel). Nordic Focus's sender panel now has the accessible show/hide toggle the guidelines require (`ShellPage.ContextToggle`, a `ToggleButton` with a pressed state, tooltip and name that say what it does; the panel's state is kept for the session; the toggle shows only while the window has room for the panel; a hidden panel reserves no width). Persisting the choice across restarts is not done |

Observation outside this package: between 600 and 1024 px Bulwark folds the sidebar to a 48 px icon column (`use-media-query.ts:17-24`, SB-02, M10); without it the reader is only about 130 px wide at 844 px.

Verification (Windows, Automation build, hidden runs on port 9337, FakeJmap sample account and the empty profile for sign-in; screenshots under `artifacts/wp10/`, not committed): Bulwark dark and light at 1280, 844 and 480 wide, the phone folder drawer open, the tab bar switching to Calendar and back, sign-in dark and light at 1280 and 480, Nordic with the toggle at 1560, Bento at 1560. `tools/build.ps1 test` Presentation 1211 passed; Automation 245 of 246 (the known `PopupLint` failure for the three other packages' files; no file of this package is listed); `build -Head win` and `-Head desktop` succeed; `rules` clean. Not verified: a click on the sender-panel toggle (no toggle command in the automation protocol), the tab bar and drawer with the keyboard or a screen reader, focus rescue and the list announcement (no automation hook), the Linux head at runtime (M9), the published-exe smoke run.

### 3.11 WP-15 status (branch m6b/wp15-polish)

| ID | Status | Note (Bulwark citation) |
|---|---|---|
| TH-05 | closed for the shared helpers, per-screen sizes already set by the packages | the settings pages' shared `SetUiUi.Body` and `StyleState.BodyTextSize` (accessibility quick panel) are 14 px (`text-sm`) in the Bulwark style and stay 13 in Bento and Nordic; `Text.Meta` 12 is `text-xs`. Screens that name their own size (names 14, counts 12, section heads 18, subject 20, dialog title 18) were set by their packages and are unchanged. `Text.Label` (11.5 bold, uppercase headings) is only used by the Bento popover header and the legacy settings group heads (`globals.css:13-95`) |
| TH-06 | closed on Windows, not drawn on Skia | `Theme/CardShadow.cs`: the dialog card and the command palette take `shadow-xl` (Z lift 32), menu panels and toasts `shadow-lg` (Z lift 20), as a `ThemeShadow` cast on the popup layer (`dialog.tsx:181`, `context-menu.tsx:667`, `toast.tsx:92-107`); Bento and Nordic clear it. Not done: `shadow-sm` on outline buttons and rows (a one-pixel hairline in Bulwark; the border already carries it), the Uno Skia heads (`ThemeShadow` is not drawn there: M9), the flyout menus of `MenuFlyout` (stock presenter) |
| TH-08 | closed | Bulwark `more-contrast` now carries Bulwark's pairs for `Unread` (#93c5fd dark, #1d4ed8 light), `Destructive` (#fca5a5, #b91c1c) and the destructive hover (`app/accessibility.css:242-335`); before, light `Unread` read 3.7:1 and `Destructive` 4.8:1. `BulwarkContrastTests` checks every text-like token of both high-contrast variants (at least 6:1 on the page ground, line and ring at least 7:1) and that more contrast is never weaker than normal; `Every_variant_defines_every_feedback_and_semantic_token` already covers all 12 variants |
| RD-09 | closed | header band h-14 area, 20 px subject, 14 px sender text and the 56 px toolbar are the values of WP-6; captured dark and light again here |
| RD-10 | closed | WP-6's empty state (56 px circle, 28 px icon, 16 medium title, 14 muted description); captured here |
| SB-08 | closed | Bulwark's sidebar has no storage block (`sidebar.tsx`; the quota is the rail ring and its popover, `navigation-rail.tsx:164-221`, SH-06): the block is hidden in the Bulwark style (`Fmt.VisStorageBlock`); Bento and Nordic keep the text and meter. A phone-width window has no rail, so there the quota is not shown (Bulwark's phone layout shows it in the drawer: open, M10) |
| Leftovers closed | | `Fmt.RailBackground` / `RailForeground` deleted (WP-3); `PanesViewModel.KeyStep` 10 px (WP-10); `showRailAccountList` wired in the catalogue and `spec/settings.csv` (WP-3); `FeedbackTransition` prunes dead references whenever its list reaches 256 (WP-1); the sign-in "Remember me" check box no longer draws a glyph while unchecked (the unchecked glyph brushes in `Styles/FluentOverrides.xaml` were the accent foreground, WP-2); modal scrims of the calendar cards, the missed-reminders card and the composer cards are Bulwark's black/50 through `StyleState.ScrimBrush` (was a literal 40 %); the contacts search buttons take `ButtonRadius` |
| Left open | | literal radii that equal the Bulwark value (6 or 8) and circle badges in the views (a mechanical audit would need a visual-tree dump per screen); 382 literal `FontSize` values in views that match their cited Bulwark sizes; `shadow-sm` |

## 4. Gap ledger by area

Legend for each row: **B** = Bulwark, **N** = Barbican now.

### 4.1 App shell and rail/navigation

| ID | Gap | B | N | Impact | Size | MS |
|---|---|---|---|---|---|---|
| SH-01 | Rail order and set: Bulwark = logo, Search, Mail, Calendar, Contacts, Files, plugin pages, add-on apps + "Add app", Admin, notification bell, Accessibility, Settings, divider, Shortcuts, account avatars + add account + sign out. Barbican = Mail, Calendar, Contacts, Files at top; Search, Shortcuts, Accessibility, Settings, single account button at bottom; no plugin pages, no add-on apps, no Admin, no bell, no sign-out | `<bulwark>/components/layout/navigation-rail.tsx:584-760,700-949` | `Views/Rail/RailView.xaml:33-111` | main navigation differs on every screen | M | M6b |
| SH-02 | Active item style: Bulwark = `bg-primary/10 text-primary font-medium`, icon `text-primary`, `aria-current="page"`; Barbican = `Sel` fill + `SelText` foreground (accent blue / selection) | `navigation-rail.tsx:640-652` | `Views/Fmt.cs:173-175` | active app looks different (blue vs primary tint) | S | M6b |
| SH-03 | Rail icon size 18 px in a 40x40 `rounded-md` box with 4 px gap; Barbican uses 20 px icons | `navigation-rail.tsx:606-608,641,656` | `RailView.xaml:38-70` (`Size="20"`), `StyleMetrics.cs:36-48` (40 box, radius 6) | icons 2 px larger | S | M6b |
| SH-04 | Unread badge: red-600 pill, 16 px, `text-[0.625rem]`, or a 10 px red dot per `unreadCountStyle` | `navigation-rail.tsx:659-668` | `RailView.xaml:53-58` (`Accent` pill) | badge colour and dot mode | S | M6b |
| SH-05 | Rail collapse/expand with labels (`collapsed` prop, labels beside icons, `w-10 h-10` vs `px-2.5 text-sm`) | `navigation-rail.tsx:65,229,588,608,620,643` | missing (fixed 56 px) | rail labels setting, discoverability | M | M10 |
| SH-06 | Storage ring popover (used / free / total, bar) opened from the ring | `navigation-rail.tsx:164-221` | ring only, no popover (`Controls/StorageRing.cs`) | storage detail | S | M10 |
| SH-07 | Account stack on the rail (avatars, active check, add, sign-out menu) behind `showRailAccountList` | `navigation-rail.tsx:170-240` | missing; `S-showRailAccountList` unwired (`spec/settings.csv`) | multi-account switching | M | M6b |
| SH-08 | Notification bell and centre | `<bulwark>/components/notifications/notification-center.tsx`; rail :100 | missing (G-045) | no notification history | M | M8 |
| SH-09 | Admin entry (Shield) with update dot | `navigation-rail.tsx:520-545` | missing from rail (admin is M7) | admins | S | M7 |
| SH-10 | Plugin pages and add-on sidebar apps in the rail, plus the add-ons modal | `navigation-rail.tsx:459-512,700-770`, `layout/sidebar-apps-modal.tsx` | partly closed (pages); sidebar apps and the modal open | add-ons that contribute pages | M | M6b |
| SH-11 | Phone layout: bottom tab bar (<600 px) with edge drawer for folders and 0.3 s slide | `navigation-rail.tsx:390-445`, `mail-app.tsx:6432-6447`, `layout/edge-drawer.tsx:66` | rail hidden in reading/composing (`ShellPage.xaml.cs:210-219`), `CompactFolders` button (`ShellPage.xaml:48`); breakpoint 700 not 600 | narrow windows | M | M10 |
| SH-12 | Pane layouts: `mailLayout` split / focus / horizontal (list above reader) and Mountain View interface | `<bulwark>/stores/settings-store.ts:448-466`, `hooks/use-effective-mail-layout.ts:6-13` | done (pending merge); `S-mailLayout` wired | layout choice | L | M6b |
| SH-13 | Resizable sidebar/list with 300 ms width easing (not while dragging), sidebar fold to 48 px | `mail-app.tsx:6427,6536`, `sidebar.tsx:2653` | `Views/Actions/PaneHandle.cs` resize, no width easing | accepted deviation for easing; fold: see SB-02 | S | M10 |
| SH-14 | Title band: Barbican keeps a 38 px band with the app name above the rail; Bulwark's desktop window has no title text row (window controls overlay) | `app/desktop.css`, `layout/window-controls-host.tsx` | `Shell/ShellPage.xaml:30-33` | wasted 38 px, differs in every screenshot | S | M8 |
| SH-15 | Minimum window width: `ShellLayout.MinWidth = 960` but phone mode begins at 700 | `use-media-query.ts` PHONE_BELOW 600 | `Theme/ShellLayout.cs:14`, `ShellPage.xaml.cs:210`; no window minimum is set in `Shell/`, `Composition/`, `App.xaml.cs` (grep) | confirm the window minimum (verify) | S | M10 |

### 4.2 Mail sidebar, folders, tags

| ID | Gap | B | N | Impact | Size | MS |
|---|---|---|---|---|---|---|
| SB-01 | Sidebar head (h-14): collapse chevron, account switcher, compose pill (`rounded-full h-9`) | `sidebar.tsx:2688-2745` | `SidebarView.xaml:80-118` (account header 56 px, compose is a `Sel`-filled radius 18 button, optional) | header differs | S | M6b |
| SB-02 | Fold to 48 px icon column with hover peek of names | `sidebar.tsx:1457-1499,2651-2660,2704-2709` | missing | space on small windows | M | M10 |
| SB-03 | Folder row metrics: `text-sm` row, density-driven `py`, `tap:min-h-[44px]`, `transition-colors duration-150`; Barbican 30 px fixed, 13.5 px text | `sidebar.tsx:520` | `StyleMetrics.cs:95` (30), `StyleState.cs:FolderNameSize` | density setting has no effect on folders (verify) | S | M6b |
| SB-04 | Folder icons: coloured per role with `folderIcons` setting, count modes | `sidebar.tsx` (folder icon colours), `settings/folder-settings.tsx` | `SidebarView.xaml` 16 px icon; role chart tint or the colour-blind-safe colour, text colour for a plain folder (`FolderRows.RoleTint` / `RoleCvdRgb`, `SidebarFmt.IconBrush`); Nordic still hides the icons (`FolderIconVisibility`, G-038) | folder icon fidelity | S | M6b |
| SB-05 | Tag rows are Buttons inside an `ItemsRepeater`, folder rows are `ListViewItem`s: different hover/selection/focus; tag rows radius 14/22 | `sidebar.tsx:1181` one row style | `SidebarView.xaml:53-70,178-185`, `StyleState.cs:TagRowRadius` | rows differ within one sidebar | S | M6b |
| SB-06 | "Folder settings" button disabled placeholder | `sidebar.tsx` section header actions | `SidebarView.xaml:145-148` (`IsEnabled="False"`) | dead control | S | M6b |
| SB-07 | Folder context menu parity (new subfolder, rename, empty, mark read, share, settings) | `components/layout/mailbox-context-menu.tsx` | `spec/menus.md` `MENU-FOLDER`, `MENU-LIST-ACTIONS`, `FOLDERS-HEADER` (re-audited) | Share and mute still missing | S | M6b |
| SB-08 | closed (WP-15) Storage block: text only in Bulwark mode, no bar | `sidebar.tsx` bottom quota | `SidebarView.xaml:189-197` (`VisStorageMeter` Bento only) | quota bar | S | M10 |
| SB-09 | Scheduled mailbox, all-inboxes unread, shared accounts section | `sidebar.tsx:2735-2790` (`showScheduledMailbox`, `showCrossUnread`) | `UnifiedSectionView.xaml`, `SharedSectionView.xaml` exist; `S-showSharedMailAccounts` unwired | cross-account | M | M10 |

### 4.3 Message list

| ID | Gap | B | N | Impact | Size | MS |
|---|---|---|---|---|---|---|
| ML-01 | Row status icons: pinned (primary), starred (amber filled), replied, forwarded, paperclip, scheduled-send chip | `thread-list-item.tsx:912-923,978-997` | `MessageListView.xaml` status stack (pin first, then star, reply, forward, clip); `MessageRowViewModel.Pinned` / `Answered` / `Forwarded` from `$pinned` / `$answered` / `$forwarded`; `SameAs` compares `Pinned`, so a pin change replaces the row | matched except the scheduled-send chip (ML-10) | S | M6b |
| ML-02 | Row selection model: avatar-as-checkbox with hover swap, `hover:scale-110`, checked ring; Barbican shows avatar and ticks on tap | `thread-list-item.tsx:790-806,1520-1530` | `MessageListView.xaml:35-45` | selection discoverability | S | M6b |
| ML-03 | Row hover/selected colours: hover `bg-muted shadow-sm`, selected `bg-accent shadow-sm`, unread `bg-accent/30`, checked `ring-2 ring-primary/20` | `thread-list-item.tsx:720-745` | `Controls.xaml:306-325`, `MessageListView.xaml:27-31` (unread `Sel` at 0.3 opacity ok); hover uses `Hover` (dark `#1A1A1A` vs muted `#262626`) | dark-mode hover contrast | S | M6b |
| ML-04 | Row density (`--list-item-height` 48, `--density-item-py`) and one-line row shape (`useListRowShape`) | `app/globals.css:106-115`, `hooks/use-list-row-shape.ts` | `StyleState.cs:MessageRowPadding` fixed 12 | density setting | M | M10 |
| ML-05 | Date group headers, `S-groupListByDate` | `components/email/list-date-group-header.tsx` | `MessageListView.xaml` header template: Bulwark's h-9 header is one fold button (chevron, px-2, "Collapse/Expand <title>"; unread-first sections stay captions); `ListLayout.Build` leaves a folded group's rows out; `MessageListViewModel.ToggleGroup` | header style | S | M6b |
| ML-06 | Hover actions: container rounded-lg `shadow-md`, buttons `p-1.5 rounded-md hover:bg-black/5 dark:hover:bg-white/10`, destructive hover text, plugin actions at container width | `email-hover-actions.tsx:271-274,333-371` | `MessageListView.xaml` hover strip: the row colour to clear over 32 px at its start (`:343-351`), 28 px buttons radius 6, delete turns destructive on hover (`Button.HoverAction.Destructive`); plugin actions do not widen the container | visible on hover | S | M6b |
| ML-07 | Empty and loading states: centred empty-state component, loading dims list to 50%, skeleton via boot-skeleton | `components/ui/empty-state.tsx`, `email-list.tsx:1602,1671`, `boot-skeleton/mail-skeleton.tsx` | `MessageListView.xaml:178-180` plain text "No messages" | first-run feel | S | M6b |
| ML-08 | Batch action bar replaces the toolbar with animated swap (`fade-in slide-in-from-left-3 300ms`); Barbican shows a separate `SelectionBarView` below with the count and the buttons fading in from the left and right | `email-list.tsx:1412-1425` | `Views/Actions/SelectionBarView.xaml(.cs)` (count slides 12 px from the left, buttons 12 px from the right, over `Motion.Enter`) | placement | M | M6b |
| ML-09 | Folder-actions menu matches Bulwark's `MailFolderMenu` (refresh, sort, group by date, mark read, mark all folders read, empty folder in Trash and Spam) | `email-list.tsx` `MailFolderMenu` call at `mail-app.tsx:6667-6676`, `mail-list-menus.tsx:141-254` | `FolderMenus.ListActions` (`MENU-LIST-ACTIONS`), `FolderMenus.ListSort`, `spec/menus.md` | menu items matched | S | M6b |
| ML-10 | Category/list tabs (`message-list-tabs`), list columns, structured-mail cards, pinned (`$pinned`) | `components/email/message-list-tabs.tsx`, `list-columns.tsx`, `structured-mail/` | missing (G-073); `S-listColumns` unwired | feature gap | L | M10 |
| ML-11 | Row swipe actions and long-press (touch) | `thread-list-item.tsx:760-780,590` | missing; `S-swipeLeftAction/Right` unwired | touch devices | M | M10 |
| ML-12 | Row action button (`showRowActionButton`, MoreHorizontal for non-hover pointers) | `thread-list-item.tsx:746-770` | `S-showRowActionButton` (verify consumer) | accessibility | S | M10 |
| ML-13 | Right-click menu highlight outline while open (`[data-menu-opener]`) | `globals.css:1700` | verify in `ContextPointerRouter.cs` | small | S | M10 |
| ML-14 | Account tint/dot, tag tint, verification-code copy in rows | `thread-list-item.tsx` (`resolvedRowTint`), `verification-code-chip.tsx` | row tints present (verify look); the verification code is Bulwark's chip (`verification-code-chip.tsx:55-72`): bordered rounded-md, amber key icon 14 px, mono code, click copies with a "Code copied" toast | code chip matched; row tints unverified | S | M6b |

**WP-5 status (branch m6b/wp5-list, Bulwark style; Bento and Nordic unchanged):**

| ID | Status | Note |
|---|---|---|
| ML-01 | closed except the scheduled-send chip | pinned in the primary colour first of the status icons (`thread-list-item.tsx:503,912-923`; `$pinned`), starred (filled amber, 14 px), replied, forwarded (`$answered`, `$forwarded`, both when both), paperclip, gap 6 (`:978-997`). A pin or unpin repaints the row: `MessageRowViewModel.SameAs` compares `Pinned`, so the list swaps it. The scheduled-send chip is not drawn (no `submissionId` on a list row: ML-10) |
| ML-02 | closed | Bulwark rows show the 40 px avatar with the unread dot 10 px to its left; the check shows on avatar hover and while ticked (instant, no `scale-110`). The gutter click area beside the avatar is not widened |
| ML-03 | closed | hover and selected come from the row template (`FeedbackHover`/`FeedbackSelected`); the open row drops the unread wash (`MessageRowViewModel.IsSelected`); a ticked row has the accent wash at 40 % and the 2 px ring at 20 % |
| ML-04 | open | density (M10) |
| ML-05 | closed (Bulwark) | h-9 header with bottom rule, text-sm medium muted; a date group is one fold button (chevron down open / right folded, 8 px gap, px-2, hover fill, "Collapse/Expand <title>" name) (`list-date-group-header.tsx:14-37`, `email-list.tsx:421-466`). Folded groups leave their rows out except the open message and ticked rows; the folds belong to one folder/search/tag for the session (nothing persists them). Unread-first sections stay captions and do not fold |
| ML-06 | closed except plugin actions | strip flush with the row end in the row colour (selected fill or hover), a 32 px gradient from that colour to clear at its start (`email-hover-actions.tsx:343-351`), 28 px `Button.HoverAction` buttons radius 6, 100 ms system hover; delete turns destructive on hover (`:51-56`). Plugin actions still do not widen the container |
| ML-07 | closed | empty state component (56 px muted circle, title, description; search variant), first-load skeleton rows (static), loading over rows: half-strength dim with a pill, no fade |
| ML-08 | partly | the bar is edge to edge under the toolbar (accent wash 30 %, bottom rule, `N conversations`/`N selected` at the left, 32 px icon buttons at the right). It swaps in with the count fading from 12 px left and the buttons from 12 px right over `Motion.Enter` (Bulwark's 300 ms; MOTION.md caps at 200 ms), Bulwark style only, nothing under reduce motion; no height tween, no exit. It stays a separate row below the header, it does not replace the toolbar. The select-all toolbar button is the shared `ToggleButton.Icon`, active while a selection exists. Move and Tag buttons are not in it |
| ML-09 | closed | the toolbar's folder-actions button opens `MENU-LIST-ACTIONS`, Bulwark's `MailFolderMenu` (`email/mail-list-menus.tsx:141-254`): Refresh, Sort › (seven orders, the custom order from Settings shown checked, `ListSortChoices`), Group by date, —, Mark folder as read (off with nothing unread), Mark all folders as read (asks), and in Trash and Spam —, Empty trash / Empty spam (asks, off for an empty folder). Sort and Group by date write `messageListOrder` (and widen its scope) and `groupListByDate` |
| ML-10..13 | open | category tabs, swipe actions, row action button, menu outline: M10 |
| ML-14 | partly | the verification code is Bulwark's chip: 1 px border, rounded-md, 14 px amber key icon, the code in mono with tracking-wide, hover fill, click copies and shows a "Code copied" toast (`verification-code-chip.tsx:35-43,55-72`); the prototypes keep the plain button. The account dot and tag tint of the row keep their earlier look (not re-verified against `resolvedRowTint`) |
| FM-12 | closed | the folder-change rise runs through `MotionRuntime.DecorationMs` (0 in Bulwark) |

### 4.4 Reader and conversation view

| ID | Gap | B | N | Impact | Size | MS |
|---|---|---|---|---|---|---|
| RD-01 | Toolbar contents: Reply, Reply all, Forward, Archive, Delete, Move, tag, star/flag, mark read, print, add-ons, fullscreen, more menu with `moreMenuSlideEnabled` | `email-viewer.tsx:4917,5100-5260,5402,5594-5616` | `ReaderView.xaml:46-72` (Reply, Reply all, Forward, Archive, Delete, add-ons, fullscreen, more): no Move/Tag/print buttons at top | frequently used actions one click deeper | M | M6b |
| RD-02 | Per-message cards: independent expand/collapse, per-card actions, Print on card menu; Barbican has headers/bodies but one body surface in the base reader and cards are code-built | `thread-conversation-view.tsx:472-560`, `conversation-message-menu.tsx` | `Views/Reader/ConversationCardView.cs`, `ReaderView.xaml:213` (partly matched, see [First native pass](M6b-ui-parity.md#first-native-pass)) | thread reading | M | M6b |
| RD-03 | Conversation header actions and conversation menu (forward as attachment, rules, pin, links, read aloud, reader view, plain text, EML export, source) | `email/conversation-header-actions.tsx`, `rules-menu.tsx`, `reader-view-dialog.tsx`, `read-aloud-bar.tsx` | partial (`spec/menus.md` `MENU-THREAD`); read aloud, reader view dialog missing (grep) | M | M10 |
| RD-04 | Sender details inline expand with recipient popover; Barbican has `reader.details` toggle | `recipient-popover.tsx`, `email-viewer.tsx:6000-6010` | `ReaderView.xaml:173` | matched in principle (verify look) | S | M10 |
| RD-05 | Safety banners: link mismatch, phishing report, external-sender chip, BIMI badge, per-sender trust, retention notice, unsubscribe/read-receipt banners | `email/phishing-report-host.tsx`, `external-sender-chip.tsx`, `brand-verified-badge.tsx`, `unsubscribe-banner.tsx`, `read-receipt-banner.tsx`, `retention-notice.tsx` | G-049..053 open; no code for phishing/link-mismatch/retention (grep) | security surface | L | M10 |
| RD-06 | Attachments: chips with preview/rename/save-to-files, drag out | `reader-attachments.tsx`, `rename-attachment-dialog.tsx`, `save-to-files-dialog.tsx` | `ReaderView.xaml:246`, `AttachmentPreviewDialogContent.cs` | verify chip look and actions | M | M10 |
| RD-07 | Quick reply: avatar, sender identity, inline Send, Assistant button, plugin actions | `email-viewer.tsx:6652-6684`, `quick-reply-plugin-actions.tsx` | `ReaderView.xaml:294`, `ReaderView.QuickReply.cs` (avatar/identity missing per [Native baseline](M6b-ui-parity.md#native-baseline-after-the-preview)) | quick reply look | S | M6b |
| RD-08 | closed (RD-08 branch) Reader selection menu (`reader-selection-menu`): right-click on selected body text opens Copy, a separator, then the add-ons' rows, anchored at the click | `reader-selection-menu.tsx:177-198`, `:95-99`, `:144-175` | `MailBody/BodyShell.cs` (`ContextRequested`), `MailBodyView.cs` (`OpenSelectionMenu`), `Assets/mail/shell.js` (`ctxOf`, `pubSel`), `Presentation/Reader/ReaderSelectionMenu.cs`, `App/Plugins/ReaderSelectionAddOns.cs`; the other reader surfaces (`reader-plugin-surfaces.tsx` frames) stay with `ReaderView.AddOns.cs` | add-ons | M | M6b |
| RD-09 | closed (WP-15) Message header typography: 20 px subject, header band h-14, `text-sm` meta | `email-viewer.tsx` header region | `StyleMetrics.cs:ReaderSubjectSize=20`, `StyleState.cs:ReaderBand*` | matched in principle | S | M10 |
| RD-10 | closed (WP-15) Reader empty state | `ui/empty-state.tsx` | `ReaderView.xaml:30-40` (`reader.empty`) | verify look | S | M10 |
| RD-11 | Reading mask, read-aloud bar | `email/reading-mask.tsx`, `read-aloud-bar.tsx` | A11y setting only (`Core/Settings/Accessibility/A11y.cs`) | accessibility | M | M10 |
| RD-12 | Message pop-out window | `components/popout/popout-message-app.tsx` | missing (G-055) | multi-window | L | M10 |

### 4.5 Composer

| ID | Gap | B | N | Impact | Size | MS |
|---|---|---|---|---|---|---|
| CP-01 | Footer buttons are `rounded-md` (6 px) icon+label; Send is `rounded-md rounded-e-none` with a divider and chevron; Barbican uses pills (radius 18, 36 px) and a 18/0/0/18 split | `email-composer.tsx:6500-6720`, `ui/button.tsx:42,55` | `ComposerView.xaml:135-152` | most visible composer difference | S | M6b |
| CP-02 | Footer set: Formatting toggle (Type), Templates, Options (priority + classification), **Tags**, Assistant, Proofreading, Discard, Send, Schedule; Barbican has Attach, Templates, Options and no Tags or formatting toggle | `email-composer.tsx:6500-6640` | `ComposerView.xaml:130-155` ids `composer.*` (no `composer.tags`, no formatting toggle) | missing controls | M | M6b |
| CP-03 | Header: flat title row h-14 with assistant, Full screen, Open in new window, Close; Barbican has them on Windows only (`WindowButton` collapsed elsewhere); pop-out round trip open | `email-composer.tsx:5674-5676,5783-5786` | `ComposerView.xaml:26-52`, `Shell/ShellPage.ComposerWindow.cs` (`#if WINDOWS` `ShellPage.xaml.cs:283`) (G-055) | pop-out | L | M6b |
| CP-04 | Recipient row: From dropdown, To with contact picker, Cc/Bcc toggles, chips with radius/colour, suggestion popover | `email-composer.tsx:6031-6056`, `email/recipient-popover.tsx` | `Views/Compose/RecipientFieldView.cs:48` (`Popup` brush suggestion, radius per Bento) | field look | M | M6b |
| CP-05 | Subject field and fields are bordered `Input` (h-10 `rounded-md border-input`) or flat ruled rows; Barbican bare text boxes in ruled rows | `ui/input.tsx:13-18` | `ComposerView.xaml:85` (`TextBox.Bare`) | matched for ruled rows (verify) | S | M10 |
| CP-06 | Editor toolbar: Bulwark rich-text toolbar (headings, lists, align, colour, tables, image resize, mentions, link/image dialogs); Barbican `EditorToolbarView` canvas-drawn | `email/rich-text-editor.tsx`, `link-dialog.tsx`, `image-url-dialog.tsx`, `resizable-image.tsx` | `Views/Editor/EditorToolbarView.cs` (own renderer) | verify per control | L | M6b |
| CP-07 | Schedule send dialog, send warnings, external-recipient confirm, close/save/discard dialog | `schedule-send-dialog.tsx`, `send-warnings-dialog.tsx`, `external-recipient-confirm.tsx` | `ComposerView.Cards.cs` cards (code-built, own radius) | dialogs not in dialog host (see MN-01) | M | M6b |
| CP-08 | Attachments strip chips, drop overlay, attach-from-Files (G-076), link-to-Files (G-079) | `composer-*`, `file-picker-dialog.tsx` | `ComposerView.Files.cs` (attach-from-Files missing) | feature | M | M10 |
| CP-09 | Mentions of recipients, `S-recipientMentionsEnabled`, RTL editing | `recipient-mention.tsx`, `mention-chip.tsx` | G-075, G-063 open | feature | M | M10 |
| CP-10 | Dock vs window vs maximized states and offline hint (`composer-offline`) | `email-composer.tsx:6640-6650` | partly matched (see section 2) | small | S | M10 |

**WP-7 status (branch m6b/wp7-composer).** Bulwark theme only; Bento and Nordic keep their pill look (same code paths, switched on `Applied.Bulwark`).

| ID | Status | Note |
|---|---|---|
| CP-01 | closed | footer buttons h-36 with `StyleState.ButtonRadius` (6), Send `rounded-e-none` + 1 px `primary-foreground/20` divider + chevron half, font-medium, ghost bin turns destructive on hover (`email-composer.tsx:6640-6700`, `button.tsx:42`) |
| CP-02 | partly | Options shows the active-count badge and Templates/Options show the chevron (`composer-toolbar-menu.tsx:44-61`). Open: Tags menu (needs a compose-time tag model and `Presentation/Tags`, owned by another package), Formatting toggle (Bulwark shows it only in the phone/dock-compact layouts; Barbican draws its editor toolbar inside the editor), Templates/Options/Attach still open cards, not anchored menus |
| CP-04 | partly | chips `rounded-md` (ButtonRadius), 14 px text, suggestion list `rounded-md` (`email-composer.tsx:7505,6933`); From dropdown and contact picker unchanged |
| CP-05 | verified | `TextBox.Bare` is an explicit style, so the WP-2 `MinHeight 40` does not reach the ruled recipient/subject rows |
| CP-02 (menus) | partly, unbuilt | Bulwark Templates (insert, save) and Options (plain text, read receipt, priority) open an in-window `MenuFlyout` above the button (`ComposerView.Menus.cs`); Bento/Nordic keep cards. The Send arrow keeps its card (Bulwark opens the schedule dialog directly; `tools/compose_send_availability.py` probes the card ids). Tags menu: open, needs a compose-time tag model and `Presentation/Tags` |
| CP-03 | partly | pop-out and return work on Windows (`tools/check-composer-window.py`); the Linux head still hides the button (Uno multi-window, M9) |
| CP-06 | partly | `EditorToolbarView` is XAML buttons, not canvas-drawn (the ledger text above was wrong). Restyled to `rich-text-editor.tsx:1030-1480`: Bulwark's order and groups (font size | bold, italic, underline, strikethrough, colour, highlight | H1, H2 | bullet, ordered, quote, code block | align left, centre, right | link, picture, table | clear formatting | undo, redo), 16 px line icons in 28 px buttons, radius 4, 2 px gaps, a 1 x 20 separator between groups, `px-3 py-1.5`; hover, pressed and the colour transition come from `Button.Icon` (the shared Feedback tokens), an active toggle is `FeedbackIconHover` with `FeedbackIconHoverFg` (`bg-accent text-accent-foreground`); the overflow popover wraps its buttons in a row (`:1480`). 19 icons added to `theme.slint` (bold ... building). Link and picture-from-address are dialogs of the shared host (`link-dialog.tsx`, `image-url-dialog.tsx`: labelled fields, Remove link at the start of the footer, Save/Insert disabled until there is an address, Enter submits, Escape cancels). Not done: the toolbar strip has no `bg-muted/30` fill and a full-strength border (Bulwark `border-border/50`); the font-size and colour popovers and the table picker keep their own cards (`rich-text-editor.tsx:1076-1115, 1170-1240, 406-460`); the heading dropdown is gone, so Heading 3 and "Normal text" are reachable only by toggling H1/H2 off or by the keys, and Justify only by its key; the font-size button does not show the size in its tooltip; table sub-menu while inside a table, image alt button, mentions and `resizable-image.tsx` are untouched (CP-09). Barbican keeps inline code, horizontal rule and HTML source after Redo (Bulwark has no button for them). The alt-text dialog (`image-alt-dialog`) is still a card. Hover and press were not captured against Bulwark. |
| CP-07 | closed for the questions | the leave, discard, send-warnings, external-recipient and schedule questions are dialogs of the shared host (`ComposerView.Questions.cs`, `Views/Dialogs/ComposerQuestionContent.cs`, `ScheduleDialogContent.cs`; MN-01 card, scrim, PopIn, focus restore). Texts, buttons and order are Bulwark's: close `email-composer.tsx:6854-6879` ("Save or discard draft?", Cancel, Discard destructive, Save primary; discard: "Discard draft?", Cancel, Discard); warnings `send-warnings-dialog.tsx:63-150` (one dialog for several, titled "Send this message anyway?" with bullets and placeholder chips; a single warning keeps its own title, text and "Back to editing"/"Review"; "Schedule anyway" when scheduling); external `external-recipient-confirm.tsx:203-250` (its own question after the warnings, warning icon, count text, To/Cc/Bcc list, Back then Send anyway, Back holds the focus); schedule `schedule-send-dialog.tsx:97-146` ("Pick when to send it.", preset rows with the time at the end, field, error, Cancel then confirm). Escape answers "no" in all of them, a click on the scrim does nothing (Bulwark's send warnings and external confirm close on it). The detached composer window carries its own host (`DialogHostRouter`). Not done: the "Don't ask again" box under a single subject or placeholder warning (`:138-148`, it writes a setting); "Send without text?" (Barbican has no empty-body warning); the 1 px scrim blur (MN-01); the Templates, From, Options and Send menu cards of the composer are still cards in `ComposerView.Cards.cs`; the date and time are two text boxes, not one `datetime-local` input. |
| CP-08, CP-09 | open | attach-from-Files, recipient mentions: features |
| CP-10 | open | offline hint (`composer-offline`) needs a connectivity service |
| (radii) | closed, unbuilt | literal chip avatar, suggestion row, remove button, card button radii replaced by `ComposerShape` (StyleState) |
| (strip) | closed, unbuilt | attachment strip sits in the 16 px gutter in Bulwark (the capture showed it flush to the pane edge) |
| (extra) | closed | attachment cards `rounded-md` no border (`:6290`), drop overlay and card use `DialogRadius`, `bg-background/80` (`:5714`) |

### 4.6 Search, filters, command palette

| ID | Gap | B | N | Impact | Size | MS |
|---|---|---|---|---|---|---|
| SR-01 | Search field: `Input h-8 rounded-md`, search icon `start-2.5`, clear X, `kbd` shortcut hint, trailing Filter button with count badge (primary circle, 16 px); Barbican field radius 14, filter icon is "Opt", count in text | `ui/toolbar.tsx:74-120`, `mail-app.tsx:6649-6665` | `SearchBoxView.xaml:11-27` (`FieldRadius` 14, `Raised` panels) | visible every day | S | M6b |
| SR-02 | Filter panel fields and layout (`SearchFilterPanel`) | `components/search/filter-panel-host.tsx`, `lib/plugin-types.ts:953` | `SearchBoxView.xaml:41-44` panel (radius 14, `Raised`); I-277..280 | look and field set (verify) | M | M6b |
| SR-03 | done (pending merge): global search palette scope chips, facets (counts), preview; the search tab is not built | `components/global-search/*`, `command-palette/command-palette.tsx` | `Views/Palette/PaletteView.xaml(.cs)` (G-103/104 marked done: verify scope chips and preview) | power users | L | M10 |
| SR-04 | Search results count line and snippets highlighting | `email/search-result-count.tsx`, `search-snippet-text.tsx` | `MessageListView` results line above the list (Bulwark style only, while a search shows and nothing loads) with the words of `SearchResultCount` (conversation and message counts); `Views/List/Snippet.cs` highlights matches | count line matched; snippet look unverified | S | M6b |

**WP-9 status (branch m6b/wp9-search, Bulwark style; Bento and Nordic keep their hairline field):**

| ID | Status | What matches |
|---|---|---|
| SR-01 | closed (Bulwark) | field `Input h-8 rounded-md border-input` (hover muted-foreground, focus ring), magnifier 16 px, `/` key cap only while empty and unfocused, clear X 24 px, filter toggle is a `ToggleButton.Icon` that tints while the panel is open or filters are active, 16 px primary count badge at the top end corner, text-sm 14 (`ui/toolbar.tsx:36-120`, `mail-app.tsx:6649-6672`, `ui/input.tsx:13-18`). The list header's select-all and folder buttons are top-aligned so the panel does not centre them (`MessageListView.xaml`, shared file, 3 attributes) |
| SR-02 | closed | in-flow panel (no card): pills Unread / Read / Starred / Attachment with check when on, From / To / Subject / "Has the words" fields (live, 350 ms debounce, Enter closes), date choices, Size (Any size / Larger than 1, 5, 10, 25 MB / Custom size with the two KB fields "Larger than (KB)" and "Smaller than (KB)"), place choices, Clear all, Search (`search-filter-panel.tsx:97-274`). "Has the words" is `body:` and reaches the server as `{"body": …}` text conditions; the size bounds are `larger:` / `smaller:` in KB and become `{"minSize": kb*1024}` / `{"maxSize": kb*1024}` (rounded, like Bulwark's `sizeFilterBytes`, `search-utils.ts:98-102, 237-245`), an empty, non-numeric or non-positive bound sets no condition. Chips follow `search-chips.tsx:35-70` ("Body", "Larger than (KB): 100 KB", "Smaller than (KB): …"), and the filter count counts them. The two `<select>`s are in-window choice rows because a ComboBox popup is out of window (ADR 0007) |
| SR-03 | done (pending merge), branch m6b/global-search | the palette's Search scope: scope chips with counts (Ctrl+Left/Right), mail, contacts, events and files groups over the stores, an in: operator beats the chip, a preview pane with the highlighted hit's real data (mail: from, folder, date and the server's SearchSnippet marks) and Open; Enter opens the hit in its app. Not built: the full search tab, the account chip (one login per window), the remembered chip across restarts |
| SR-04 | closed for the count line | the line above the results (`mail-app.tsx:6733-6737`, `search-result-count.tsx:14-31`): px-4 text-xs muted on a 20 % muted wash with a bottom rule, shown while a search or filter is active and nothing loads; "No results found", "1 conversation found", "N conversations found", "N+ …" while another page follows, and "N conversations (M messages)" when threading hides some. Snippet highlighting is unchanged (`Views/List/Snippet.cs`) |

Also matched: chips (`search-chips.tsx:78-110`, wrapping row, primary tint, X hit area), suggestion dropdown as an in-window Popup under the field (`search-suggestions.tsx:70-195`: rows px-3 py-2, hover/active fill, hover-only remove and "To" buttons, People rows with avatar, name, address and a "From" badge, match in semibold, closes on blur). New: `SearchBoxModel.PickPersonToAsync`, `GetOperator`; `WrapRow` panel. Feedback: one-frame colour swaps and the shared button/toggle styles, no fades or scaling.

### 4.7 Context menus and keyboard shortcuts

| ID | Gap | B | N | Impact | Size | MS |
|---|---|---|---|---|---|---|
| MX-01 | Menu panel: `bg-background rounded-md shadow-lg border`, item `px-3 py-1.5 text-sm gap-2`, 16 px icons, shortcut text right, check items, destructive icon colour; Barbican `Popup` fill, radius 12, 30 px rows radius 8, 13 px text, no icons (labels only in list) | `ui/context-menu.tsx:663-667,707-740,816-841` | `Views/Menus/MenuHostView.xaml.cs:163-208` | every right click | M | M6b |
| MX-02 | Menu item set per surface (`spec/menus.md` ported from Rust; Bulwark 1.21.2 has since added items such as rules, links, read aloud, pin) | `email-context-menu.tsx`, `mailbox-context-menu.tsx`, `conversation-message-menu.tsx`, `address-context-menu.tsx` | `spec/menus.md` (163 lines) baselined on Rust | missing items (verify against menus.md) | M | M6b |
| MX-03 | Menu placement on narrow screens becomes a sheet | `context-menu.tsx:1007-1100` | `MenuHostView` flips only | narrow | S | M10 |
| MX-04 | Shortcut sheet content/grouping (`keyboard-shortcuts-modal`), customisable shortcuts | `components/keyboard-shortcuts-modal.tsx`, `message-action-shortcuts.ts` | `Views/Shortcuts/ShortcutSheetView.xaml`, `spec/behaviours/shortcuts.md` | verify against 1.21.2 (not diffed here) | M | M10 |

### 4.8 Calendar and tasks

| ID | Gap | B | N | Impact | Size | MS |
|---|---|---|---|---|---|---|
| CA-01 | Toolbar: [sidebar toggle][Create][Today][prev][next] title (`text-base font-semibold`) ... search, more menu, **view dropdown** (outline button + radio menu with shortcuts); Barbican title left, segmented view buttons, Today as chip | `calendar/calendar-toolbar.tsx:410-490` | `Views/Calendar/CalendarToolbarView.cs:25-60,137-146` | placement and control type | M | M10 |
| CA-02 | Shape language: Bento radii (search box 12, buttons 10-17) | `ui/button.tsx:42`, `ui/toolbar.tsx` | `CalendarToolbarView.cs:48-56`, `CalendarSidebarView.cs`, `CalendarVisuals.cs`, 11 files in `Views/Calendar` | looks like another app | M | M10 |
| CA-03 | Event editor: Bulwark date/time inputs and calendar select (kind picker); Barbican plain text date/time pairs and chip calendar choice | `calendar/event-modal.tsx:1401-1474,1587-1596` (I-333..335) | `Views/Calendar/Editors/CalendarCardKit.cs:36,67` | editing events | L | M10 |
| CA-04 | Quick-create popover, event detail popover, hover preview, context menus | `quick-create-popover.tsx`, `event-detail-popover.tsx`, `event-context-menu.tsx` | `HoverCardView.cs`, `EventDetailPanel.cs`, `Calendar/MoreMenuView.cs` (own cards) | popovers | M | M10 |
| CA-05 | Calendar sidebar header/panel, mini calendar, my calendars list with visibility toggles | `calendar-sidebar-panel.tsx`, `mini-calendar.tsx` | `CalendarSidebarView.cs`, `MiniMonthView.cs` | verify look | M | M10 |
| CA-06 | Week/day/month/agenda grids: now line, all-day strip (G-003), slide animation 0.25 s when switching | `calendar-week-view.tsx`, `calendar-month-view.tsx`, `globals.css:1029-1057` | `TimeGridView.cs`, `MonthGridView.cs`, `AgendaView.xaml` (G-003 open) | grids | M | M10 |
| CA-07 | Tasks: list, task modal, task chip, toolbar | `calendar/task-list-view.tsx`, `task-modal.tsx`, `task-toolbar.tsx` | `Views/Calendar/Tasks/TasksListView.xaml`, `TasksSidebarView.cs`; rows use stock `ListViewItem` (FM-02) | tasks | M | M10 |
| CA-08 | Invitations, RSVP, recurrence editor, alerts, find a time (G-009), print (G-008) | `recurrence-editor.tsx`, `find-a-time.tsx` | `CalendarPrintView.cs`; G-008/009/004/005 open | features | L | M10 |

### 4.9 Contacts

| ID | Gap | B | N | Impact | Size | MS |
|---|---|---|---|---|---|---|
| CT-01 | One New menu (contact, group, address book, import); Barbican has separate actions | `contacts/contacts-sidebar.tsx:294` | `Views/Contacts/ContactsSidebarView.cs` (I-364..366, see [composer walkthrough](M6b-solo-parity.md#bulwark-composer-footer-and-form-walkthrough)) | entry flow | S | M10 |
| CT-02 | Contact form as scrollable panel with address book/type on top, not a centred card | `contacts/contact-form.tsx:873-971` | `ContactsFormDialogContent.cs:28` | forms | M | M10 |
| CT-03 | List items: avatar-checkbox with `hover:scale-110`, `transition-all 200`, selection bar; stock `ListViewItem` | `contacts/contact-list-item.tsx:84-108` | `ContactsListView.xaml:45-52`, `ContactsListItem.cs` | list look | M | M10 |
| CT-04 | Detail view, activity tiles (G-011), duplicates view, merge, directory browser | `contact-detail.tsx`, `contact-activity.tsx`, `contact-duplicates-view.tsx` | `ContactsDetailView.cs` (activity tiles deferred) | feature | M | M10 |
| CT-05 | Context menu, print, export | `contact-context-menu.tsx`, `contact-print.ts` | `ContactsPrintDialogContent.cs` (G-010) | feature | S | M10 |

**WP-13 status (branch m6b/wp13-contacts, Bulwark style; Bento and Nordic keep their rounded rows, title row and tiles):**

| ID | Status | Note |
|---|---|---|
| CT-01 | closed | the sidebar head is Bulwark's: h-14 with the title and a `+` that opens `MENU-CONTACTS-NEW` (new contact, new group, new address book, import; `contacts-sidebar.tsx:283-317`). Rows are flat, full width, 32 px, 16 px icon, muted count, selected = `FeedbackSelected` with `FeedbackSelectedFg` and medium weight (`:322-370`), headings text-xs medium muted with a `+` |
| CT-02 | partly | in Bulwark, while the Contacts app is on screen, the form is a panel of the detail pane (header with title and close, scrolling sections divided by a hairline with a 16 px icon, address book first, footer with Cancel and Save; `contact-form.tsx:237-262,868-975`) through `ContactsViewModel.FormHost`; the reader's "Add to contacts" and Bento/Nordic still open the same panel in the dialog card. Not done: the Person / Organization switch, collapsible sections, the more-fields group (anniversaries, personal info, gender, calendar links) and the category combo box |
| CT-03 | closed | rows use `ListItem.Row` (FM-02: hover, pressed, selected from `Feedback*`, no scale, no fade): full width, px 16 py 12, border-b, 32 px circular avatar in the 12 address hues with white/black initials, name text-sm medium, email text-xs muted; the check boxes show while rows are ticked (select mode or Ctrl/Shift on several) with no `scale-110`; letter headers px-4 text-[0.6875rem] medium muted on the background (`contact-list-item.tsx:75-133`, `contact-list.tsx:337-339`, `avatar.tsx:339-345,416-420`). The organization line (comfortable density) is not drawn: density is M10 |
| CT-04 | partly | the contact pane is Bulwark's: border-b header with a 48 px avatar, text-xl semibold name, nickname and organization, outline Send email / Call / Edit, print and more (MENU-CONTACT or MENU-GROUP); sections divided by a hairline with icon rows, mail and phone in the primary colour, chips as primary tint (`contact-detail.tsx:112-235,237-550`). Activity tiles (G-011), the duplicates view and the merge dialog keep their existing look |
| CT-05 | partly | the list toolbar has Bulwark's print button (prints the list as on screen); export is in the bulk bar; the context menu is unchanged (`MENU-CONTACT`) |
| (toolbar) | closed | h-14 row: select toggle (`ToggleButton.Icon`, tinted while ticking), a 32 px search field with the clear button and the filter toggle with its count (`contact-list.tsx:408-440`, `ui/toolbar.tsx`), the filter panel (company, job title, location, email domain, birthday month, has email / phone / photo tri-state chips; `ContactsFilters`, `contact-list.tsx:34-90,164-243`). The sort-by chips are not drawn (the setting is read-only here: Settings > Contacts). The month field steps through the months (a drop-down opens a popup: ADR 0007) |
| (bulk bar) | closed | accent wash 30 %, border-b, select-all box, `N selected`, ghost Add to group (MENU-CONTACT>groups at the button) / Export / Delete and a clear button (`contact-list.tsx:522-600`). Move to address book and Merge selected have no command here |
| (empty) | closed | `ui/empty-state.tsx`: a 56 px muted circle with a 28 px icon, 16 px medium title, 14 px muted line, New contact and Import (all empty), Clear search / Clear (nothing matches) |
| FM-02 (contacts) | closed | contact rows take `ListItem.Row`; sidebar rows draw `FeedbackHover` / `FeedbackSelected` in their own pointer handlers |

Shared files touched: `spec/menus.md` (the `MENU-CONTACTS-NEW` entry). Layout: the list is 384 wide with a hairline after it, the surface is the page background (`contacts-app.tsx:1461-1510`).

### 4.10 Files

| ID | Gap | B | N | Impact | Size | MS |
|---|---|---|---|---|---|---|
| FL-01 | Browser header: breadcrumb, sort, view toggle, New menu (uploads, folder, text file); Barbican has toolbar with own buttons | `files/file-browser.tsx`, `files-app.tsx` | `Views/Files/FilesToolbarView.cs` | placement | M | M10 |
| FL-02 | Grid/list and thumbnails, details panel (G-014, G-015), in-app preview (partly: `FilesPreviewView.cs`), zoomable image with 200 ms ease | `files/zoomable-image.tsx:286`, `file-browser.tsx:1376` | `FilesListView.cs`, `FilesDetailsView.cs`, `FilesPreviewView.cs:173-174` | feature | M | M10 |
| FL-03 | Upload conflict dialog, move dialog, zip download, new text file, filename rules (G-016..019) | `files/file-picker-dialog.tsx` | `FileConflictDialogContent.cs`, `FileMoveDialogContent.cs` | feature | M | M10 |
| FL-04 | Sidebar and row hover use hand-set backgrounds | `files/*` hover classes | `FilesListView.cs:146,273`, `FilesSidebarView.cs` | FM-02 | S | M10 |

**WP-14 status (branch m6b/wp14-files, Bulwark style; Bento and Nordic keep their previous look):**

| ID | Status | Note |
|---|---|---|
| FL-01 | closed for the header | h-14 header with a bottom border: breadcrumb (current folder semibold, the way up muted until hovered, chevron separators), Empty trash / Paste outline buttons, 224 px search, sort icon button opening the in-window sort menu `MENU-FILES-SORT` (Name, Modified, Size, Type, Ascending, Descending; command `files.sort`, `FilesSortRules`), list/grid radio group, Refresh, Details toggle (`ToggleButton.Icon`), selection bar (clear, "N selected", Open, Download, Rename, Move to, Cut, Copy, Delete in the destructive tint; Restore and Delete permanently in the trash) (`files-header.tsx:111-258,313-390,431-452`, `ui/toolbar.tsx:16-29`). Sidebar header: title and a plus icon button opening the New menu (`files-sidebar.tsx:287-311`). Not built: the crumb overflow menu, the tree button, the phone layout |
| FL-02 | partly | list columns Name, Modified, Size (sortable header, arrow on the active column; the Type column is gone as in Bulwark), 40 px rows with a hairline, hover "more" button, starred folders; grid with Folders / Files headings, folder items and tiles with a muted thumbnail area; details panel with header, close button and muted preview area; empty states (empty folder with Upload files / New folder, no search results, empty trash, starred, recent, shared); preview overlay is full screen bg-black/80 with a background header (`file-views.tsx:170-367,414-492`, `file-details-panel.tsx:88-125`, `ui/empty-state.tsx`, `preview-shell.tsx:121-144`). No thumbnails of images, no row checkboxes, no folder-first section split in the list, no zoom easing (the zoom snaps) |
| FL-03 | not in this package | dialogs are WP-8 (`Views/Dialogs`) |
| FL-04 | closed | list rows, grid tiles and sidebar rows are `ListViewItem`s with `ListItem.Row` (shared hover, pressed and selected brushes); the hand-set `Paint()` backgrounds are gone. Sidebar: square 32 px rows, selected = `FeedbackSelected` / `FeedbackSelectedFg`, `StyleState.Files.cs` carries the radii and heights |
| FM-02 | closed for files | as FL-04 |

### 4.11 Settings

| ID | Gap | B | N | Impact | Size | MS |
|---|---|---|---|---|---|---|
| ST-01 | Settings frame: 56 px rail, nav column with search box, page groups with icons, sub-sections (`ps-9 text-xs`), Help links, Sign out at the foot, h-14 header with Back; page content `max-w-3xl`; Barbican: search + list of pages in a tile, content `MaxWidth 760` with Bento title 22 | `settings/settings-app.tsx:1213-1360` | `Views/Settings/SetUiScreen.cs:26-60`, `SetUiUi.cs:Page` | every settings visit | L | M10 |
| ST-02 | Row layout: label + description left, control right with `border-b`; section cards; "Advanced" gating with hidden-count notice and reveal-on-search | `settings/settings-section.tsx:1-120` | `SetUiUi.cs:RowWithDescription`; `SetUiScreen.cs:1` lists "Show advanced settings" (no `settings.csv` row; reveal-on-search verify) | settings readability | M | M10 |
| ST-03 | Switch is 24x44 `rounded-full` primary/`bg-muted ring` with 150 ms; Barbican `ToggleSwitch` Fluent (FM-19) | `settings-section.tsx:384-410` | `SetUiRowsView.cs:259` | every toggle | M | M10 |
| ST-04 | Selects, segmented choices, sliders, colour swatches (`hover:scale-110`) | `settings/appearance-settings.tsx`, `keyword-settings.tsx:84` | `SetUiChoice.cs`, `SetUiRowsView.cs` (no ComboBox; custom) | look | M | M10 |
| ST-05 | Unwired settings with no native consumer (25 rows): `S-toolbarPosition`, `S-listColumns`, `S-showRailAccountList`, `S-sidebarAccountSwitcher`, `S-swipe*`, `S-dateLocale`, `S-notificationSoundChoice`, `S-pushRelayUrl`, `S-debugMode`, filename rules, `S-rtlEditingSupport` ... | `stores/settings-store.ts` | `spec/settings.csv` (`unwired` rows; labelled inert in UI) | controls that do nothing | L | M10 |
| ST-06 | Themes: built-in themes, accent colours, custom themes | `settings/themes-settings.tsx`, `lib/builtin-themes.ts` | none (G-070); light/dark/system only | the Bulwark appearance page | L | M10 |
| ST-07 | Page coverage: all 24 routes are entered in Bulwark ([M6b-ui-parity.md](M6b-ui-parity.md#other-reference-surfaces-entered)); Barbican keyless rows (64) and pages without factories draw from the catalogue | `settings/*.tsx` | `SetUiScreen.cs:4` (D-M6-B02), `spec/settings.csv` | per-page parity (verify each) | L | M10 |
| ST-08 | Language (i18n) and time zone | `settings/language-settings.tsx`, `locale-store.ts` | English only (G-071) | localisation | L | M10 |
| ST-09 | Account security (password, TOTP, app passwords, encryption), app lock (G-027..031) | `settings/account-security-settings.tsx`, `components/app-lock/*` | partial, live writes excluded (D-M6-09); app lock none | security pages | L | M10 |
| ST-10 | Identities, signatures (rich), templates, folders admin, vacation (G-021..042) | `identity-settings.tsx`, `folder-settings.tsx`, `signature-details-section.tsx` | `Views/SettingsServer/*` partial | account setup | L | M6b |

### 4.12 Add-ons (Settings > Add-ons)

| ID | Gap | B | N | Impact | Size | MS |
|---|---|---|---|---|---|---|
| AO-01 | Sidebar apps vs plugins sections, install action, expandable installed entries with toggles | `settings/addons-settings.tsx`, `plugins-settings.tsx`, `sidebar-apps-settings.tsx` | `Views/Settings/AddonsPageFactory.cs:53-170`, `.Managed.cs` (lifecycle matched; layout verify) | add-on management look | M | M6b |
| AO-02 | Add-on dialogs (`plugin-dialog-host`), toasts attributed to plugin (`from_plugin` line, puzzle icon), progress toasts | `plugins/plugin-dialog-host.tsx:425`, `ui/toast.tsx:187-190` | `Views/Plugins/PluginToastPresenter.cs`, `PluginProgressToasts.cs`; toast has `DisplaySource` text only | plugin feedback | S | M6b |
| AO-03 | Plugin slots in composer, reader, list rows, top bar, rail pages, settings sections | `components/plugins/plugin-slot.tsx`, `composer-plugin-sidebars.tsx`, `plugin-row-badges.tsx` | mount points per `docs/milestones/M6b-plan.md`; rail page entries and the page surface exist (SH-10) but the add-on frame inside the page and the sidebar apps are missing | add-on reach | L | M6b |

### 4.13 Dialogs, toasts, popovers, tooltips

| ID | Gap | B | N | Impact | Size | MS |
|---|---|---|---|---|---|---|
| MN-01 | Dialog shell: card `bg-background rounded-lg shadow-xl p-6`, title `text-lg font-semibold`, description `text-sm muted`, footer `gap-3 px-6 pb-6`, close X (`absolute top-3 end-3`), scrim `black/50` + 1 px blur; Barbican card `Popup` fill, radius 16, padding 20, title 16, scrim 0x66, Cancel/OK pills radius 14, no close X | `ui/dialog.tsx:120-232`, `ui/confirm-dialog.tsx:143-175` | `Views/Dialogs/DialogHostView.xaml:11-29` | every confirm/prompt | M | M6b |
| MN-02 | Confirm dialog: destructive variant with 40 px circled warning icon and destructive button; Barbican uses same primary OK | `ui/confirm-dialog.tsx:143-170` | `DialogHostView.xaml:24-27` | delete confirmations | S | M6b |
| MN-03 | Prompt/dialog content built in code uses Bento controls (Settings, Contacts, Files dialog contents) | `ui/prompt-dialog.tsx` | `Views/**/*DialogContent.cs` (14 files) | dialogs look mixed | M | M10 |
| MN-04 | Toast: bottom-right (`bottom-5 end-5`), 360 px, `rounded-md`, status icon (neutral for success/info, red/amber for error/warning), title + message, action buttons, progress bar, pause on hover/focus, swipe, 5 s default | `ui/toast.tsx:92-130,243-261,500-561` | `Views/Actions/ToastHostView.xaml:11-35` bottom centre, radius 12, Close chip, no icon, no progress bar | every action feedback | M | M6b |
| MN-05 | Toast stack position and size cap (`max-w-[calc(100vw-2.5rem)]`, scroll) | `toast.tsx:561` | `ToastHostView.xaml:28-34` (`MaxHeight 220`) | small | S | M6b |
| MN-06 | Toast slide-in (end edge, 0.32 s overshoot) and slide-out: not copying the overshoot; 150 ms slide with no overshoot is the aligned choice | `globals.css:570-606` | none | polish | S | M6b |
| MN-07 | Popovers: account switcher (sidebar head and rail), recipient popover, storage popover, date/time popovers; Barbican flyouts use `Flyout` with radius 14 | `layout/account-switcher.tsx`, `rail :164-221`, `recipient-popover.tsx` | `Views/Accounts/AccountPopoverView.xaml`, `RailView.xaml:107-117` | look | M | M6b |
| MN-08 | Tooltips: native `title` on every icon button (label + shortcut) | `ui/icon-button.tsx` (`title` defaults to label) | `Motion/Tip.cs` only where `Tip.Text` set; many buttons have none (e.g. `ReaderView.xaml` actions use names only) | discoverability | S | M10 |
| MN-09 | Shortcut sheet and palette scrim/card use PopIn; shape differs | `keyboard-shortcuts-modal.tsx` | `ShortcutSheetView.xaml`, `PaletteView.xaml` | see MN-01 | S | M10 |

### 4.14 Theming tokens

| ID | Gap | B | N | Impact | Size | MS |
|---|---|---|---|---|---|---|
| TH-01 | Missing semantic tokens: `destructive` (#dc2626/#ef4444), `success`, `warning`, `info`, `secondary`, `input`, `popover-foreground`, `accent` (separate from `primary`: #dbeafe / #1e3a8a), `selection-foreground`, `unread`, chart 1-5 | `app/globals.css:31-125,141-187` | `Tokens.g.cs` palette keys (Ground, Tile, Tile2, Line, Track, Text, Body, Dim, Faint, Accent, OnAccent, Sel, SelText, Popup, Rose/Mint/Sky/Amber, Bg, Rail, Side, Hover, Pressed, Chip, Raised, Zebra, Bubble, Reader, MutedDay, Ring) | destructive buttons, warning/error colours come from tint pairs | M | M6b |
| TH-02 | Dark `Hover` is `#1A1A1A` (sidebar-accent); Bulwark `hover:bg-muted` is `#262626` (275 uses) and ghost `bg-accent` is `#1E3A8A` | `globals.css:134,152` | `Tokens.g.cs` bulwark-0 `Hover` | dark hover too faint / wrong hue | S | M6b |
| TH-03 | Light `Pressed` (`#F8FAFC`) is lighter than `Hover` (`#F1F5F9`); Bulwark has no pressed state | `globals.css:37-60` | `Tokens.g.cs` bulwark-2 | pressed looks like un-hover | S | M6b |
| TH-04 | Radii: `rounded-md` 6, `rounded-lg` 8, `rounded-full`; Barbican `Shape.R*` are Bento (RButton 12, RField 14, RPopup 14, RChip 99, RTile 24) used by Bulwark mode | tailwind default radii | `Themes/Shape.Bento.xaml`, `StyleMetrics.cs:ButtonRadius/FieldRadius/PopupRadius`, `StyleState.cs:401-416` | everything not hand-patched | M | M6b |
| TH-05 | closed (WP-15, 3.11) Typography: Bulwark root 100%, `text-sm` (14 px) body, `text-xs` 12, headings `text-lg`/`text-base font-semibold`; Barbican body 13 / meta 12 / label 11.5 bold | `globals.css:13-95` | `Styles/Controls.xaml:15-31` | density vs Bulwark (verify per screen) | M | M10 |
| TH-06 | closed on Windows (WP-15, 3.11) Shadows: `shadow-sm` on rows/buttons/dialog `shadow-xl`, menu `shadow-lg`; Barbican has none in Bulwark mode (no `ThemeShadow` use) | `button.tsx:48`, `dialog.tsx:181`, `context-menu.tsx:667` | grep `ThemeShadow`/`Shadow` in `src/Barbican.App` (none) | depth cues | M | M10 |
| TH-07 | Scroll bars: 8 px minimalist, optional native (`data-native-scrollbars`) | `globals.css:296-330` | Fluent default | every scroll area | S | M10 |
| TH-08 | closed (WP-15, 3.11) High contrast / increased contrast variants (`bulwark-1/3`) exist; verify new tokens in all four variants | `app/accessibility.css` | `Tokens.g.cs:397-510` | accessibility | S | M10 |
| TH-09 | Palette source is `design/legacy-slint/theme.slint` through `tools/Barbican.GenTokens`; Bulwark tokens should be authored once as the source of truth, with Bento/Nordic derived | n/a | `Themes/Tokens.xaml` header | maintainability | M | M6b |

### 4.15 Responsive and narrow layouts, accessibility

| ID | Gap | B | N | Impact | Size | MS |
|---|---|---|---|---|---|---|
| RS-01 | Phone <600: single pane navigation, bottom tab bar, drawers with scrim | `mail-app.tsx:6395-6460`, `navigation-rail.tsx:390` | `ShellPage.xaml.cs:197-228` at <700, no bottom bar (SH-11) | narrow windows | M | M10 |
| RS-02 | Reader toolbar collapses to icons, then to "secondary" label set | `globals.css:609-660` (reader toolbar icon-only rules) | `Controls/WrapRow.cs`, `ReaderView.xaml:46` (wrap, not collapse) | narrow reader | S | M10 |
| AX-01 | One focus ring (FM-05), `focus-always`, rows with inset ring | `accessibility.css:115-136` | see FM-05 | keyboard | M | M6b |
| AX-02 | Large targets (44 px hit area) on every button | `button.tsx:30-37` | `StyleState.MinimumTarget` applies to rail/list/folder/compose only | motor | M | M10 |
| AX-03 | Roving focus in list with j/k, focus rescue after delete/move, pane cycle | `layout/focus-rescue.tsx`, `pane-cycle.tsx`, `email/list-a11y.ts` | pane cycle (F6) and j/k present; focus rescue present (`FocusRescue.ShouldReturnToList`, `MessageListView.ReturnFocus` lands on the open row or the list, `ShellPage.Phone.cs` calls it) | keyboard users | M | M10 |
| AX-04 | Live regions and status announcements | `ui/live-regions.tsx`, `use-mail-status-announcements.ts` | toast announces via UIA notification (`ToastHostView.xaml.cs:39-56`); list status none | screen readers | S | M10 |

### 4.2a WP-4 status (branch m6b/wp4-sidebar)

| ID | Status | Note |
|---|---|---|
| SB-01 | partly | head: one rule under the switcher and Compose (`sidebar.tsx:2684`), Compose is a primary pill (h-9, rounded-full, px-3, left aligned, medium weight, `Button.Primary` feedback) under the switcher or alone in the 56 px head row (`:2728-2745`). The collapse chevron is the fold feature: SB-02 (M10) |
| SB-02 | open (M10) | no fold exists to put a chevron on |
| SB-03 | closed | folder and tag rows are 28 px (a 20 px line + the default `--density-sidebar-py` 4 px; the only density Bulwark ships), 14 px names, 12 px counts (unread semibold foreground, `/ total` muted, an 8 px primary dot for the dot style), an unread folder is not bold, the selected one is semibold, the sub-unread dot follows the count (`sidebar.tsx:520,286-345,660-669`) |
| SB-04 | closed | icons 16 px; a plain folder icon takes the text colour (`text-foreground/80`). A role folder takes its chart token (Inbox chart-1, Sent chart-2, Archive chart-3, Junk chart-4, Drafts chart-5, Trash muted), or the colour-blind-safe colour while that palette is on (`sidebar.tsx:263-284`, `lib/cvd-palette.ts`; `FolderRoleTintTests`). Count modes unchanged |
| SB-05 | closed | tag rows are folder rows: same height, square band, neutral hover and pressed, accent-fill selection with the accent text, semibold and a 2 px primary bar (`sidebar.tsx:518-535,1181`); folders use `ListItem.Sidebar.Bulwark` (own template, `FeedbackHover/Pressed/Selected(Fg)`, 150 ms colour ease), tags and the all-inboxes row layer a hit surface, the selected fill and the content. Bento and Nordic keep the shared pill rows and `Sel`/`SelText` |
| SB-06 | closed | the gear opens Settings > Folders & tags (`sidebar.tsx:2430-2431`); a tags gear next to the plus |
| SB-07 | closed | folder menu re-audited against `mailbox-context-menu.tsx:309-466` (WP-8): the list toolbar's folder-actions button now opens `MENU-LIST-ACTIONS` (Refresh, Sort ›, Group by date, Mark folder as read, Mark all folders as read, Empty in Trash and Spam), and the sidebar's folder section opens `FOLDERS-HEADER` (mark all folders as read, new folder, refresh). Share and Mute notifications have no command |
| SB-08 | closed (WP-15) | see 3.11 |
| SB-09 | open (M10) | unchanged |

Section headers (`sidebar.tsx:806-835`): the whole row is the toggle with the neutral hover band, a muted leading chevron, 14 px semibold name, 24 px plus and gear with their own hover (`Button.Sidebar.Tool`). The shared-accounts header and rows take the folders' metrics. Local styles live in `Views/Sidebar/SidebarRows.xaml`; per-style values in `Theme/StyleState.Sidebar.cs`. Needs from other packages: none. Note for every package: a `Button` does not stretch by default (a content-less hit surface is 0 wide), set `HorizontalAlignment="Stretch"`.

## 5. Work packages (about one day each, disjoint ownership)

Rule for shared files: `Theme/StyleState.cs`, `Views/Fmt.cs` and `Styles/Controls.xaml` are touched by many areas.
**WP-0 (S, 1 h, first and alone)** makes `StyleState` and `Fmt` `partial` with no behaviour change; after that each
package adds its own `StyleState.<Area>.cs` / `Fmt.<Area>.cs` and never edits the base files. `Styles/Controls.xaml`
belongs to WP-1 only; other packages add new `Styles/<Area>.xaml` dictionaries merged by their own registration.
Packages marked "after WP-1" need the brush/token names WP-1 publishes first (agree the list in the WP-1 brief:
`FeedbackHover`, `FeedbackPressed`, `FeedbackActive`, `FeedbackActiveHover`, `FeedbackSelected`, `FeedbackFocus`,
`FeedbackDisabledOpacity`, `Motion.Fast`).

| # | Package | Gaps | Owned files | Size | Order |
|---|---|---|---|---|---|
| WP-0 | Make shared singletons partial | prerequisite | `Theme/StyleState.cs`, `Views/Fmt.cs` (partial keyword only) | S | 0 |
| WP-1 | Shared state tokens and feedback (done, see 3.5) | FM-01, 05, 06, 07, 18, 20, 21; TH-01, 02, 03, 04, 09; AX-01 | `design/legacy-slint/theme.slint`, `tools/Barbican.GenTokens/**`, `src/Barbican.Presentation/Theme/**`, `Themes/Tokens.xaml`, `Themes/Shape.*.xaml`, `Styles/Controls.xaml`, new `Styles/Feedback.xaml`, `Motion/MotionRuntime.cs`, `MotionScale.cs`, `PopIn.cs`, `Rise.cs`, `Scrim.cs`, `design/MOTION.md`, `Theme/AppResources.cs`, `Theme/PaletteWriter.cs`, `tests/**/MotionScaleTests*`, token tests | L | 1 |
| WP-2 | Fluent control overrides | FM-19, 20, 21; ST-03; TH-07 | new `Styles/FluentOverrides.xaml` (ToggleSwitch, CheckBox, RadioButton, TextBox, PasswordBox, ScrollBar, Expander, MenuFlyout presenter, ProgressBar), `App.xaml`, `Theme/AppResources.cs` merge line (after WP-1) | M | 2 |
| WP-3 | Rail and navigation | SH-01..04, 06, 07, 09, 10 | `Views/Rail/**`, `Controls/StorageRing.cs`, new `Theme/StyleState.Rail.cs`, `Views/Accounts/**` | M | 2 |
| WP-4 | Sidebar, folders, tags | SB-01..09 | `Views/Sidebar/**`, `Views/Unified/**`, `Presentation/Tags/**`, new `Theme/StyleState.Sidebar.cs` | M | 2 |
| WP-5 | Message list, rows, toolbar | ML-01..14; FM-12 | `Views/List/**`, `Views/Actions/SelectionBarView*`, `Presentation/Mail/MessageRowViewModel*.cs`, `Presentation/Mail/MessageListViewModel*.cs`, `Presentation/Mail/RowTag.cs`, `Presentation/Mail/HoverStrip.cs`, new `Theme/StyleState.List.cs` | L | 2 |
| WP-6 | Reader and conversation | RD-01..11; FM-09 | `Views/Reader/**`, `MailBody/**` (except automation), `Presentation/Mail/Reader*.cs`, `Presentation/Reader/**`, new `Theme/StyleState.Reader.cs` | L | 2 |
| WP-7 | Composer and editor chrome | CP-01..10 | `Views/Compose/**`, `Views/Editor/**` (toolbar/cards only), `Presentation/Compose/**`, `Shell/ShellPage.ComposerWindow.cs`, `Shell/ShellPage.ReplyDock.cs`, `Shell/ReplyDockLayout.cs` | L | 2 |
| WP-8 | Dialogs, toasts, menus, popovers, tooltips | MN-01..09; FM-03, 04, 08, 14, 15, 22; MX-01..03 | `Views/Dialogs/**`, `Views/Actions/Toast*`, `Presentation/Actions/Toast*.cs`, `Views/Menus/**`, `Views/Shortcuts/**`, `Views/Palette/**`, `Motion/Tip.cs`, `Views/Plugins/Plugin*Toast*`, `Views/Plugins/PluginProgressToasts.cs` | L | 2 (status: section 3.8) |
| WP-9 | Search and filters | SR-01..04 | `Views/Search/**`, `Presentation/Search/**` | M | 2 |
| WP-10 | Shell frame and layouts | SH-11..15; RS-01, 02; AX-02..04; FM-16 | `Shell/**` except `ShellPage.ComposerWindow.cs`, `ShellPage.ReplyDock.cs`, `ReplyDockLayout.cs`; `Theme/ShellLayout.cs`, `Theme/ThemeController.cs`, `Views/Actions/PaneHandle.cs`, `Views/Glance/**`, `Views/Context/**`, `Views/SignIn/**`, `Controls/WrapRow.cs` | L | 3 |
| WP-11 | Settings frame, pages, add-ons | ST-01..10; AO-01..03 | `Views/Settings/**`, `Views/SettingsServer/**`, `Settings/**`, `Composition/Features/Settings*.cs`, `Composition/Features/SrvUi*.cs` | L | 3 (split ST-01/02/04 and AO-* from ST-05..10 if the worker is slow; status: section 3.9) |
| WP-12 | Calendar and tasks | CA-01..08; FM-02 (agenda, tasks) | `Views/Calendar/**`, `Presentation/Calendar/**`, `Composition/Features/Calendar*.cs` | L | 3 |
| WP-13 | Contacts | CT-01..05; FM-02 (contacts) | `Views/Contacts/**`, `Presentation/Contacts/**`, `Composition/Features/ContactsUiRegistration.cs` | M | 3 |
| WP-14 | Files | FL-01..04; FM-02 (files) | `Views/Files/**`, `Presentation/Files/**`, `Composition/Features/FilesUiRegistration.cs` | M | 3 |
| WP-15 | Theme polish sweep (done, see 3.11) | TH-05, 06, 08; RD-09, 10; SB-08 re-check | no owned files; runs last on a clean tree and edits only through the owner packages' files after they merge | M | 4 |

Parallelism: after WP-0 and WP-1 land, WP-2 to WP-9 are disjoint and can run together (at most 3 builds at once per
CLAUDE.md). WP-10 to WP-14 follow, because they read the new tokens and the shell frame. WP-1 is the only package
allowed to edit `Styles/Controls.xaml` and the generated token inputs; if WP-5 or WP-3 need a template change they
request it from WP-1 or define a local template in their own new `Styles/<Area>.xaml`.

## 6. Counts

Total gap rows: 140 (FM-08 is recorded as accepted, not a defect).

| Area | M6b | M10 | M7 | M8 | Total |
|---|---|---|---|---|---|
| Feedback and motion | 17 | 1 | 0 | 0 | 18 |
| Shell and rail | 6 | 6 | 1 | 2 | 15 |
| Sidebar, folders, tags | 6 | 3 | 0 | 0 | 9 |
| Message list | 8 | 6 | 0 | 0 | 14 |
| Reader and conversation | 4 | 8 | 0 | 0 | 12 |
| Composer | 6 | 4 | 0 | 0 | 10 |
| Search and filters | 2 | 2 | 0 | 0 | 4 |
| Menus and shortcuts | 2 | 2 | 0 | 0 | 4 |
| Calendar and tasks | 0 | 8 | 0 | 0 | 8 |
| Contacts | 0 | 5 | 0 | 0 | 5 |
| Files | 0 | 4 | 0 | 0 | 4 |
| Settings | 1 | 9 | 0 | 0 | 10 |
| Add-ons | 3 | 0 | 0 | 0 | 3 |
| Dialogs, toasts, popovers, tooltips | 6 | 3 | 0 | 0 | 9 |
| Theming tokens | 5 | 4 | 0 | 0 | 9 |
| Responsive | 0 | 2 | 0 | 0 | 2 |
| Accessibility | 1 | 3 | 0 | 0 | 4 |
| **All** | 67 | 70 | 1 | 2 | 140 |

By size: L 18, M 64, S 58.

## 7. Not audited (state, do not assume)

- Sign-in/login screens (`Views/SignIn/**` vs `<bulwark>/app/login.css`, `components/auth/**`), app lock, boot skeleton, onboarding tour, PWA prompts (not applicable natively).
- Admin screens (M7) beyond the rail entry; print views beyond the calendar/contacts notes; the 24 settings pages one by one (ST-07).
- Exact pixel metrics per screen: need paired native and live captures (see UI-GUIDELINES "visual change" rule).
- Behaviour already catalogued in `spec/interactions.csv`, `spec/menus.md`, `spec/behaviours/shortcuts.md`, which were baselined on the Rust app: a Bulwark 1.21.2 diff of those three files is itself a gap (MX-02, MX-04).

### 5.1 WP-6 status (branch m6b/wp6-reader, partial)

| ID | Status | Note |
|---|---|---|
| FM-09 | closed | reader subject/sender rise (`ReaderView.xaml.cs`) and body cross-fade (`BodyShell.cs`) go through `MotionRuntime.DecorationMs`: instant in Bulwark, unchanged in Bento and Nordic |
| RD-06 | partly | attachment chip: 8 px radius, 1 px border, `Tile` card fill, 14/12 px text, hover tint (`reader-attachments.tsx:165-181`); `StyleState.Reader.cs`. Preview/rename/save-to-files buttons on the chip not added |
| RD-10 | closed | empty state: 56 px muted circle, 28 px icon, 16 medium title, 14 muted description, max 384 (`ui/empty-state.tsx:27-36`) |
| RD-01 | closed (verified by design) | toolbar: Reply, Reply all, Forward, Archive, Delete, then Move/tag/read/print through `ExtraActions` from the width where Bulwark's `data-overflow-priority` keeps them, the rest under More (`email-viewer.tsx:4917-5260`); not re-captured at a wide window |
| RD-02 | closed | collapsed card: 8 px radius, chevron, 14 px sender, muted time; "Show N older messages" and per-card Reply / Reply all / Forward / Print use `Button.Action` with the `ButtonRadius` (`thread-conversation-view.tsx:468-590`). Unread start bar, preview line and card-level expand animation (Bulwark `duration-200`) not added |
| RD-04, RD-09 | closed | details panel uses the chip radius; header band, 20 px subject and 56 px toolbar already matched; captured dark and light |
| RD-06 | closed for the visible tile | heading with paperclip and count, always shown (the snapshot size summary stays empty while cards are off), chip download button (`attachment.save`); the hover-only save-to-files and rename buttons are not added: rename has no command in Core (`CommandIds` has no rename), save-to-files stays in MENU-ATT |
| RD-07 | closed | quick reply Cancel and Open full editor are ghost (`Button.Icon`) in Bulwark, Send is the primary button, 32 px high, `ButtonRadius` (`email-viewer.tsx:6640-6680`); the Maximize2 / Reply glyphs and plugin actions row are not added |
| Banners, tags | closed | `StyleState.BannerRadius` (6 Bulwark, 14 others), `TagRadius` (full pill, `tag-badge.tsx:23`) |
| RD-05 | open, outside the owned set | link mismatch, phishing report, external-sender chip, BIMI, per-sender trust, retention, unsubscribe and read-receipt banners need Core services and settings (G-049..053) and a native surface in `MailBody/` JS (`Assets/mail/shell.js`, not owned) |
| RD-08 | closed | shell.js posts the forced `sel:` then `ctx:X,Y` on a right-click (`ctxOf`, `pubSel(d, true)`); the host validates both (`ShellMessage`, 4000 chars, finite coordinates), `BodyShell.ContextRequested` carries point and text, `MailBodyView` converts the point to window coordinates and opens `MENU-SELECTION` through the in-window menu host (`ShouldConstrainToRootBounds`, shared row look and pop-in; no new popup). Items and order as `reader-selection-menu.tsx:187-195`: Copy, separator, add-on rows (`api.ui.setActions('reader-selection-menu', …)`, needs `ui:context-menu` or `ui:toolbar`, runs `onAction` with `{ emailId, selection: { text } }` behind `email:read`). Differences: Bulwark shows the menu only when an add-on contributes (otherwise the browser's own menu, with Copy); Barbican's WebView has no browser menu, so Copy alone opens it. Right-click with no selection still does nothing (MENU-BODY, I-215, plain-text bodies only in Bulwark). Not added: Bulwark's keyboard-opened menu at the selection (Shift+F10 fires the event at 0,0 there; WebView2's Menu key is not forwarded by shell.js), text-fit scaling of the point (the frame fills the view). Hidden run (sample data, Automation build): `menu-open MENU-SELECTION TEXT`, a synthetic `contextmenu` DOM event in the body frame via `wv-eval` opened the menu at the selection, `menu-pick Copy` and Escape closed it; clipboard content and the add-on rows with a real add-on were not exercised end to end (unit-tested) |
| RD-11 | open, outside the owned set | the reading mask is a line overlay inside the body WebView (needs `Assets/mail/shell.js`) driven by the A11y setting in Core |

### 5.2 WP-12 status (branch m6b/wp12-calendar)

Bulwark theme only; Bento and Nordic keep their views (every change is behind `StyleState.IsBulwark` / `CalendarVisuals.Bulwark`). New values live in `Theme/StyleState.Calendar.cs`; the in-window menu cards in `Views/Calendar/CalendarMenuKit.cs`.

| ID | Status | Note |
|---|---|---|
| CA-01 | closed | toolbar is the h-14 row with a border below: sidebar toggle, Create (while the sidebar is hidden), outline Today, prev, next, title (text-base semibold), then search (h-8, 160/224/256, hidden in Tasks), More (vertical dots) and the view dropdown with a radio menu and key caps D W M A K (`calendar-toolbar.tsx:410-490`, `ui/button.tsx:16`, `ui/toolbar.tsx:84-130`); `CalendarToolbarView.cs`, `ViewMenuView.cs`, `CalendarViewMenu` (Presentation, tested). The Bento/Nordic toolbar is untouched |
| CA-02 | closed for the calendar views | radii come from `ButtonRadius`/`FieldRadius`/`MenuRadius`/`DialogRadius`; event chips and blocks are rounded-xs (2 px), 12 px medium text, 20 % calendar colour over the page ground (`color-mix`, `CalendarVisuals.Mix`) or the solid colour with readable text, selected = 2 px primary ring, past events are not dimmed (`event-card.tsx:93-111,343-486`) |
| CA-03 | partly | the event and task editors are the 400 px end sheet under the toolbar (border-s, page ground, no scrim, `calendar-app.tsx:2631`) and the other cards are 8 px dialogs; buttons h-9 `ButtonRadius`, fields h-10, heading text-lg (`CalendarCardKit.cs`, `CalendarCardHost.cs`). The date/time inputs are still typed text and the calendar choice is still a chip row (Bulwark: date and time inputs, `calendar-select`), because a picker flyout would open a windowed popup (ADR 0007) and an in-window picker is new work |
| CA-04 | partly | detail panel is Bulwark's popover content (actions row with Edit and Close, 16 px colour square and text-lg title, icon + text-sm facts, guests with their answer) at 320 px beside the grid; hover card and More menu use the Bulwark card. The quick-create card and the right-click menus are the shared card/menu (WP-8) |
| CA-05 | closed | sidebar: `bg-secondary` (`Side`) column with a border at its end, "Calendar" header row with "+", mini calendar (title with chevron, 28 px circles, today filled, picked day primary/15, dots), the Tasks row with the pending and overdue counts, foldable "My calendars" / "Other calendars" with 14 px checkbox squares in the calendar colour, hover band, options button on hover (`calendar-sidebar-panel.tsx`, `mini-calendar.tsx`, `calendar-sidebar-header.tsx`). The capped task list of Barbican's sidebar is hidden in Bulwark (Bulwark has none) |
| CA-06 | closed except the slide | month: weekday header over a hairline, hairlines per week, other months on muted/30, date circle centred, picked day tinted, month name on the 1st (`calendar-month-view.tsx:594-700`); week/day: stacked weekday over a 40 px date circle, right-aligned 10 px hour labels, half-strength hour rules, destructive now line with a 12 px dot (`calendar-week-view.tsx:520-566,700,835-852`). The 0.25 s slide when switching is not added (Bulwark animates it on touch devices only; Barbican keeps its no-blank-frame switch) |
| CA-07 | closed | tasks: toolbar row with the bordered filter segment and Create task, quick-add row, rows on `ListItem.Row` with a 20 px round check (success colour when done), title, due date (destructive when overdue) and list dot (`task-toolbar.tsx:36-66`, `task-list-view.tsx:296-360`); task editor is the end sheet |
| CA-08 | open | invitations/RSVP row, recurrence editor, find a time, print: features, not in this pass |
| FM-02 | closed for agenda and tasks | both lists use `ListItem.Row` (neutral hover, accent selection) with Bulwark rows: agenda date column (36 px circle, weekday and month, Today/Tomorrow) and a hairline per day (`calendar-agenda-view.tsx:233-330`) |
| B-CAL-059 | Bulwark exception | Bulwark does not dim past events (`event-card.tsx`); Bento and Nordic keep the dim |
