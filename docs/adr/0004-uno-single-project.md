# ADR 0004: The App head is one Uno single project (spike S1)

- Status: accepted (2026-11-17), go for option A of PORT-PLAN.md §3.1
- Deciders: recorded by the M0 spikes agent (stream m0-s12); the user may overrule
- Context documents: [PORT-PLAN.md](../PORT-PLAN.md) §3.1, [ADR 0001](0001-stack-winui3-uno-csharp.md)

## Context
ADR 0001 chose one Uno.Sdk project with two target frameworks, to be confirmed by spike S1 against
the gate in PORT-PLAN.md §3.1: the prototype's XAML builds and runs on both TFMs, the Windows App
SDK version can be overridden to 2.6.5, the app publishes self-contained and unpackaged, and the
inner loop stays fast with one TFM.

## Decision
Option A. `src/Barbican.App/Barbican.App.csproj` is an Uno.Sdk single project with
`net10.0-windows10.0.26100.0` (real WinUI 3 on WinAppSDK) and `net10.0-desktop` (Uno Skia, X11 on
Linux). The fallback (B, separate heads) is not needed.

- **Uno.Sdk 6.7.49** (Uno 6.7.424, SkiaSharp 3.119.2), pinned in `global.json` `msbuild-sdks`.
  Uno.Sdk 7.0.22 existed but was four days old and moves SkiaSharp to 4.x (risk for spike S3's
  RichTextKit); revisit at M2.
- **WinAppSDK 2.6.5** through `<WinAppSdkVersion>` (Uno 6.7's default is 1.7.250909003).
- **Unpackaged, self-contained:** `WindowsPackageType None`, `WindowsAppSDKSelfContained true`,
  publish profiles `Properties/PublishProfiles/win-x64.pubxml` / `win-arm64.pubxml` (NETSDK1198
  without them). `tools/build.ps1 publish -Head win|desktop -Rid <rid>`.
- **One TFM in the inner loop:** `BarbicanHead=win|desktop` (set by `tools/build.ps1 -Head`) narrows
  `TargetFrameworks`. On Linux, Uno.Sdk drops the Windows TFM by itself, so the App head is in
  `Barbican.slnx` and `tools/linux.ps1 test` builds it.
- **Central package management** works with Uno.Sdk's implicit packages (no NU1008).

## Results

| Gate item | Result |
|---|---|
| Prototype XAML, Windows TFM | builds unchanged (`prototype/uno`, linking `prototype/winui` files) |
| Prototype XAML, desktop TFM | builds with one build-time patch: `new SizeInt32(w, h)` has no 2-argument constructor on Uno (object initialiser instead); runs 45 s under Xvfb on the VM with no exception |
| WinAppSDK 2.6.5 override | works |
| Self-contained unpackaged publish | win-x64, Release, ReadyToRun: 549 files, 292.9 MB, ~25 s (245.7 MB without R2R). linux-x64: 308 files, 160 MB, ~8 s |
| Inner loop (Windows host, through `tools/build.ps1`, incl. its own start-up) | win head: no-op 7.2 s, one-line XAML edit 10.5 s; desktop head: no-op 1.5 s, XAML edit 5.0 s. First restore + build ~36 s |
| Linux | `tools/linux.ps1 test` 37/37 with the App head in the solution (Debian 13 VM, build ~37 s) |

`Uno0001` ("not implemented in Uno") on the desktop head for the prototype: title-bar button
colours, `ItemsControl.ItemContainerTransitions`, `WebView2.DefaultBackgroundColor`,
`ContentThemeTransition` offsets, `VisualTransition.GeneratedDuration`, four
`CoreWebView2Settings` flags. All cosmetic; they stay warnings (not errors) and views that need them
must not depend on them on Linux.

## Workarounds that are part of the decision
- **`WebView2` name clash on the desktop head.** The `WebView2Aot` package (pulled in by
  `Uno.WinUI.Runtime.Skia.Win32`) declares a global namespace `WebView2` that shadows the control
  (CS0118; a using alias gives CS0576). `eng/Uno.WebView2Name.targets` removes that one assembly
  from the compile references on the desktop TFM only (it still ships and loads at run time).
- `UnoDisableGenerateAssemblyInfoWorkaround` (Uno's DevServer otherwise turns off assembly-version
  attributes in Debug, CA1016), `DefaultLanguage en-US` (PRI257), the app class is `BarbicanApp`
  (an `App` type in namespace `Barbican.App` trips MA0049).
- Analyzer suppressions for the head only (`.editorconfig`): CA1010 (Uno's `UIElement` is
  `IEnumerable`), CA2007/MA0004 (UI code continues on the dispatcher), MA0048 for `App.xaml.cs`.

## Consequences and open points
- The Windows publish is large: the WinAppSDK 2.x metapackage brings onnxruntime, DirectML and the
  AI/Search components (~60 MB) the app never uses, plus the full Windows SDK projection. Trimming
  to the needed WinAppSDK component packages is M9 work.
- The linux-x64 publish carries `DirectN.dll` (21 MB, the Win32 host for the desktop TFM). A
  Linux-only host builder (no `UseWin32()`) under a Linux RID would drop it.
- Not verified: that the published Windows exe starts (the App head shows a visible window; a hidden
  smoke mode is needed first, M1).

## M0 review (stream m0-review, at the merge of all M0 branches)
- **Finding: two SkiaSharp majors.** Uno.Sdk 6.7.49 uses SkiaSharp 3.119.2. Spike S3 pinned the editor
  stack to SkiaSharp 4.156.0, which is what Uno.Sdk 7.0.22 uses (Uno 7.0.65; its default WinAppSDK is
  2.5.1, which we still override to 2.6.5). Both facts were read from the two Uno.Sdk packages'
  `packages.json`. With transitive pinning on, the S3 pins would have lifted the App head to SkiaSharp 4
  under Uno 6.7. At the merge, the editor pins in `Directory.Packages.props` are withheld from Uno.Sdk
  projects (`Condition="'$(UsingUnoSdk)' != 'true'"`). Checked in `project.assets.json`: the App head
  resolves 3.119.2 and `Barbican.Editor.Layout` 4.156.0. Both OSes are green with it.
- **Decision (explicit):** option A stands. **The first task of M1 is the move to Uno.Sdk 7.0.x**
  (whose SkiaSharp matches the editor), before M1 adds UI code that would have to be ported. Its gate
  is a re-run of this ADR's checks (both heads build, the prototype runs under Xvfb, publish win-x64
  and linux-x64, inner-loop times) and of the S2 harness on both OSes. After it, the condition above
  is removed.
- **Fallback (explicit):** if Uno 7 fails a gate, stay on 6.7.49 through M2 and, for M3, vendor
  RichTextKit built against SkiaSharp 3.119 (ADR 0006, fallback 1). One process cannot load two
  SkiaSharp majors, so M3 cannot start with the split in place. Option B (separate heads) is only for
  a Uno 7 break of the WinUI head that an override cannot fix.
