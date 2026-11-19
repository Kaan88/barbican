# Barbican design language

**Current direction (approved 2026-11-19):** [Bento Balanced, Nordic Focus and Studio](UI-GUIDELINES.md)
are the UI guidelines from now on, including for Claude. The selected directions refine the
existing two styles; Studio is also approved as a distinct horizontal-navigation editorial direction.
These guidelines take precedence where the older boards below differ. Modern WinUI 3 materials,
subtle elevation and fluid control feedback are requested with accessible platform fallbacks.
Further Terra alternatives, including Atlas, remain explorations until selected. Keep all motion, accessibility,
shared-token and hidden-automation requirements.

Source of truth for the UI. Canvas: `<design-canvas-url>` (link in .local/accounts.md) (page "Bento × Nordic").
The `.dc.html` files here are the board sources: plain HTML with inline styles; values come from
`renderVals()` (palette `t`, icon paths `I`). `{{ x }}` interpolates, `sc-for` repeats, `dc-import` embeds another board.

- One palette (dark first) + four meaning tints: rose = today, mint = waiting/done, sky = travel/work, amber = attention.
- Two styles over the same tokens, switched in Settings › Appearance › Style:
  - Bento: every panel its own tile (radius 24–28, 1px line, 12px gaps, 14px window padding), tinted summary tiles, squircle avatars/buttons.
  - Nordic: rail + folders on the ground, list/reader/context in one rounded surface split by hairlines, pills and circles, no summary tiles.
- Both have a context panel beside content (sender in mail, guests in calendar, assistant in compose).
- Icons: 1.6–1.8px stroke line icons (paths in `I`), Segoe UI text.

## In the code (C#: WinUI 3 on Windows, Uno on Linux)

- **Palette and brushes.** `src/Barbican.App/Themes/Tokens.xaml` (generated from the token table by
  `tools/build.ps1 gen-tokens`, never edited by hand) declares every palette colour once as a `SolidColorBrush` resource
  (`Ground`, `Tile`, `Line`, `Text`, `Dim`, `Accent`, the tints `RoseBg`/`RoseFg`, ...). Views use `{StaticResource Key}`
  and never copy a colour. A theme, style or more-contrast change calls `Palette.Apply`
  (`Theme/PaletteApplier.cs` over `PaletteWriter`): it **rewrites the colour of the existing brushes in place**, so the brush
  instances never change, nothing is re-templated and nothing flashes. `ThemeController` does it in one UI-thread turn together
  with the shape dictionary, `StyleState` and the root's `RequestedTheme`, and fires `onThemeChange` / `onThemeBeforeApply`.
- **Shape per style.** `Themes/Shape.Bento.xaml` and `Shape.Nordic.xaml` hold the numeric tokens (`Shape.Gap`, `Shape.RTile`, ...).
  They are `x:Double`: WinUI cannot bind a Double resource to `Thickness`, `CornerRadius` or `GridLength` (it crashes on load),
  so views bind the typed values of `Theme/StyleState.cs` (`Look.PaneRadius`, `Look.RailWidth`, ...) with `x:Bind`; a style flip is
  one "all properties changed" notification. `Theme/StyleMetrics.cs` holds the numbers (one instance per style, UI-free and tested),
  `Theme/ShellLayout.cs` the breakpoints (list width, summary tiles). The metrics columns of `spec/layout/*.md` say which
  number is which.
- **Style and theme come from the settings** (`uiStyle`, `theme`, contrast; `IPrefsStore` through `ShellViewModel`). The
  automation command `ui bento|nordic dark|light [more|normal]` previews a look in memory only (`ThemeController.Preview`).
- **Icons.** `Controls/SIcon.cs`: the `I` paths of the mockups as `PathIcon` geometry, 1.6-1.8 px stroke, size and stroke width as
  dependency properties, colour from the foreground brush.
- **Views, one per file** under `Views/{Rail,Sidebar,List,Glance,Reader,SignIn,Accounts}`; `Shell/ShellPage.xaml` only places the
  regions. Bento and Nordic are the **same views**: only the `StyleState` values and the item-container styles
  (`Styles/Controls.xaml`) differ. Rules: `x:Bind` only, an `AutomationProperties.AutomationId` and `Name` on every interactive
  control (item containers get theirs in `ContainerContentChanging`), every flyout `ShouldConstrainToRootBounds="True"` and no
  popup control from the deny list (`tests/Barbican.Automation.Tests/PopupLintTests`, `tests/Barbican.Presentation.Tests/ShellUi/ViewXamlLintTests`).
- **Uno.** Members Uno does not implement (`ShouldConstrainToRootBounds`, `ItemContainerTransitions`, `ShowsScrollingPlaceholders`)
  are written with the `win:` prefix so the Linux head ignores them (`Uno0001` is an error in this repo).
- Hidden test channel: `ui ...`, `resize W H`, `key CHORD`, `uia-select ID` (a folder or message row), `shot` / `burst` / `probe-start`
  (`spec/automation-protocol.md`); screenshots with sample data: `tools/layout/m1_shots.py` (`docs/milestones/M1-screens/`).
- Motion: `design/MOTION.md` (rules, `Motion` resources and `MotionScale`, `PopIn` / `Scrim` / `Tip`, incremental lists through `ListSync`, the `wgc` frame probe).

The legacy Slint tokens (`ui/theme.slint`) are kept in `design/legacy-slint/theme.slint` as the reference for the numbers.
