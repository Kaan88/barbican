# Fable review log

What Fable (the later reviewer model) should review. Every agent appends in its own branch, under
a heading with its stream name: one bullet per item with **what** to review, **why** (risk,
judgement call, uncertainty, deviation from Bulwark/RFC/plan, security, privacy, partial
verification) and **where** (paths, commits). Add items as you go. Accounts and paths are
placeholders (see CLAUDE.md); never write real values here.

## M0 bootstrap (stream: m0-bootstrap)

### Privacy (public repository)
- **Prototype history rewrite.** What: the two imported prototype commits were rewritten
  (`git filter-branch`): author/committer set to the GitHub noreply identity, personal sample data
  in `Data.cs` replaced (a family-name contact and a webmail address, a user handle in a sample
  notification), `screenshots/` dropped from history. Why: privacy; it also changes the hashes the
  plan cites (`da8dcd1` → `70965c3`, `dd9a05c` → `4bc0fda`). Check that no personal data survives
  in any blob of the public history (`git rev-list --all --objects`), and that the unscrubbed
  original stays only in `<prototype>` locally. Where: `prototype/winui/Data.cs`, commits `70965c3`,
  `4bc0fda`, `58bb19b`.
- **Scrub of the copied Rust-era files.** What: legacy docs, PORT-PLAN.md, design mockups and
  README, tools were copied with string substitutions (accounts, server, key file, local paths,
  design canvas link, the sample user's name in two mockups). Why: a missed hit is a public leak;
  regex substitution of the short account nicknames in prose may also have changed meaning in
  places. Check the substitution list (documented in `docs/legacy/README.md`) and read the diffs of
  `docs/legacy/PARITY-ROUND2.md`, `docs/legacy/GAPS.md`, `docs/PORT-PLAN.md`,
  `design/mockups/BN*.dc.html`, `design/mockups/NordMail.dc.html`, `design/README.md` against the
  Rust originals at `rust-final`.
- **`tools/privacy-check.sh` coverage.** What: hard patterns + an e-mail allowlist + commit
  identity check, run by `.githooks/pre-push`. Why: it is the only automated guard. Known gaps: it
  scans text only (binaries skipped by `git grep -I`); the user's first name alone is not a pattern
  (the GitHub owner name contains it); LAN addresses, private artifact links and phone numbers are
  not patterns; the hook is only active after `git config core.hooksPath .githooks` in each clone.
  Decide whether to widen the patterns and whether to make the hook impossible to forget.
- **Binary fixtures.** What: the spam-score plugin DLL was not committed because the Rust build
  embeds the builder's home path. Why: same leak class will recur for any binary fixture (DLLs,
  PDBs, images with metadata). Where: `tests/fixtures/plugins/spam-score/README.md`.
- **Linux VM address kept out of the repo.** What: `tools/linux.ps1` reads `user@host` from
  `$env:BARBICAN_VM` or the gitignored `.local/linux-vm.txt` instead of hard-coding it as the brief
  wrote it. Why: judgement call (a LAN address is low-risk but still the user's infrastructure).

### Judgement calls and deviations from the plan
- **License AGPL-3.0-only.** Why: judgement call; chosen because Barbican includes material
  derived from Bulwark (AGPL-3.0-only: settings schema, dark-mail script, ported rules). The Rust
  repo had no license file. Bulwark's provisional app-store permission was not adopted. Check
  `LICENSE` (canonical FSF text, sha256 `0d96a4ff…`), `NOTICE.md`, `docs/adr/0002-public-repo-privacy-license.md`.
- **Build slots are N named mutexes, not one named semaphore.** What: `Global\BarbicanDotnetBuild-<i>`
  for i < `BARBICAN_BUILD_SLOTS`; the plan said a semaphore `Global\BarbicanDotnetBuild`. Why: a
  semaphore count is never returned when a holder is killed; an abandoned mutex is. Verified
  locally: a second build waits while all slots are held, and takes the slot at once when the
  holder process is killed. Not verified: behaviour across elevated/non-elevated sessions or users.
  Where: `tools/build.ps1` (`Enter-BuildSlot`, `Test-TakeMutex`).
- **Compiler server off by default.** What: `tools/build.ps1` and `tools/build.sh` pass
  `-p:UseSharedCompilation=false`. Why: a VBCSCompiler server started by another tool at Normal
  priority would compile our code there and linger; measured: the dotnet/MSBuild processes started
  by build.ps1 run BelowNormal, but a pre-existing compiler server ran at Normal. Cost: slower
  builds, to be measured once the App head exists. Opt-out: `BARBICAN_SHARED_COMPILER=1`.
- **Tests run on Microsoft.Testing.Platform.** What: `global.json` sets `test.runner` to MTP
  (xUnit v3 4.x refuses VSTest on the .NET 10 SDK); `Microsoft.NET.Test.Sdk` and
  `xunit.runner.visualstudio` are not referenced; `build.ps1 test` builds first, then runs
  `dotnet test --no-build`; filters are `-Class`/`-Method` (xUnit v3 MTP options), not VSTest
  `--filter`. Check that `tests/xunit.runner.json` (`maxParallelThreads: 4`) is honoured under MTP
  and that IDE test discovery still works. Where: `global.json`, `tests/Directory.Build.props`.
- **Analyzer strictness levels.** What: Meziantou is on at its defaults, which include MA0048
  (one type per file) and MA0051 (methods at most 60 lines); tests switch off CA1707, CA2007 and
  MA0004 (`.editorconfig`). Why: MA0048 conflicts with the plan's sketch of the `JmapError` record
  hierarchy in one file (§3.4); decide before M1 whether to keep it. Where: `Directory.Build.props`,
  `Directory.Packages.props`, `.editorconfig`.
- **Banned API lists.** What: `HttpClient` constructors are banned in all of Core (the JMAP
  transport must suppress RS0030 locally), `File`/`Directory` types and wall-clock time are banned
  in Presentation, real-input types in tests; P/Invoke real input (`SendInput`, `keybd_event`,
  `mouse_event`, `SetCursorPos`) and xdotool are caught by a grep (`tools/build.ps1 rules`), not by
  the analyzer. Verified: RS0030 fires for `DateTime.Now` in Core and `File`/`Thread.Sleep` in
  Presentation. Not verified: the test-project list (no FlaUI/CsWin32 referenced yet). Where:
  `BannedSymbols.txt`, `eng/BannedSymbols.*.txt`.
- **Files not copied from the Rust repo.** `tools/drive.ps1`, `tools/scroll.ps1` (real input, a
  planned drop) and `tools/admin_probe.sh` (reads the admin key file by default) were left out;
  `examples/release_manifest.rs` stays in the Rust repo for the ReleaseTool port (M8). The Rust
  repo's `.github/workflows` are kept as reference only under `docs/legacy/ci/` (no active CI).
- **Rust repo freeze was already partly done.** The rename to `barbican-rust`, the local remote URL
  and the annotated `rust-final` tag on `bba9613` were in place when this stream started; this
  stream verified them (tag pushed, all 44 branches equal to origin) rather than redoing them.
  The prototype had 2 commits, not 3 as the brief said; the Nordic fix is the second one.

### Partially verified
- **Linux VM bring-up.** What: the VM key was authorized during this stream; `tools/linux.ps1
  setup` installed the .NET SDK 10.0.404 under the VM user (`~/.dotnet`, dotnet-install.sh) and,
  because the VM user has passwordless sudo, apt-installed `libicu-dev` (pulling `icu-devtools`):
  a system change. ICU's runtime was probably already present; the setup now checks the ICU
  library with `ldconfig` instead of the `-dev` package name. Then `tools/linux.ps1 test` was green
  (37/37, Debian 13, 10 cores). Check whether `libicu-dev`/`icu-devtools` should be removed again
  and whether `sudo -n apt-get` in the setup is acceptable at all. Where: `tools/linux-remote.sh`.
- **`Directory.Build.rsp` alone does not cap MSBuild nodes.** What: the dotnet CLI passes a bare
  `-maxcpucount` on the command line, which beats the auto-response file: measured
  `MSBuildNodeCount = 28` on Windows with only the rsp. `tools/build.ps1` now passes
  `-maxCpuCount:4` (`BARBICAN_MSBUILD_CPU`) explicitly, measured `MSBuildNodeCount = 4`; on the VM
  `-m:2` gives 2. Why: deviation from the plan's assumption (§6.3); any direct `dotnet build`
  outside the scripts would use every core. The rsp still supplies `-nodeReuse:false`. Where:
  `tools/build.ps1`, `tools/build.sh`, `Directory.Build.rsp`.
- **`tools/build.ps1` parameter quirk.** `[CmdletBinding()]` makes `-v` mean PowerShell's
  `-Verbose`, so MSBuild verbosity must be passed as `-verbosity:<level>`. Minor; consider dropping
  CmdletBinding or documenting it in CLAUDE.md.
- **Rust repo pre-commit guard not installed.** PORT-PLAN.md §2.1 wants a local `pre-commit` hook in
  `<rust-repo>` that refuses commits; writing it was blocked by the session's permission
  classifier. The repo is still frozen by convention (CLAUDE.md) and by the `rust-final` tag.
- **Generated XAML not compiled yet.** What: `src/Barbican.App/Themes/Tokens.xaml` and
  `Shape.*.xaml` are generated and parsed as XML by the tests, but no project compiles them until
  spike S1 creates the App head. Why: `x:Double` resources and the key names (`Motion.Fast`,
  `Shape.RTile`) are unverified on WinUI and Uno.
- **Token generator correctness.** What: `tools/Barbican.GenTokens` evaluates a subset of Slint
  expressions (ternaries, references, + − × ÷, ==, !=, cubic-bezier). The equality test compares
  generated code against the same evaluator (circular), so an independent hand-copied oracle covers
  13 colour keys × 8 variants, 11 shape keys × 2 styles, motion and easing. Check the evaluator for
  precedence and alpha handling (`#rrggbbaa` → `#AARRGGBB`), and decide what to do about the
  prototype-only brushes (`Hover`/`Pressed`/`SelRow` differ from theme.slint's `hover`). Where:
  `tools/Barbican.GenTokens/*.cs`, `tests/Barbican.Presentation.Tests/Theme/TokenTests.cs`.
- **Settings schema count.** The plan says 201 settings keys; the embedded schema has 21 tabs,
  275 rows and 136 distinct non-empty `key` values. The catalogue stream (`spec/settings.csv`)
  should reconcile the number. Where: `src/Barbican.Core/Settings/settings-schema.json`,
  `tests/Barbican.Core.Tests/Settings/SettingsSchemaTests.cs`.

## M0 spike S3, editor engine (stream: m0-s3)
Branch `m0-s3`; decision record `docs/adr/0006-editor-engine.md`.

### Judgement calls and deviations
- **UTF-16 offsets instead of Rust's UTF-8 bytes, grapheme caret steps instead of code points.**
  Why: every later port of `editor.rs`/`html.rs` and their tests inherits this; Rust tests with
  non-ASCII text will need their expected offsets recomputed, and grapheme stepping changes
  Backspace behaviour on combining sequences (Rust deleted one code point). Check that this is
  wanted (Bulwark/ProseMirror deletes by code point in some cases). Where:
  `src/Barbican.Core/Editor/TextOffsets.cs`, `Pos.cs`, `Doc.Clamp`; commit `adca3e1`.
- **Rust model semantics ported, with small C# shape changes.** `Kind` became a closed
  `BlockKind` record hierarchy, `Marks` an immutable record (`MapMarks` takes `Func<Marks,Marks>`),
  `Range<usize>` became `BlockSpan`, `Delete` was split (`ClearMiddle`) for the 60-line rule, and
  `TextRange` emulates Rust `str::lines()` for quoted text. Review `Doc.Delete`, `Doc.InsertBlocks`
  /`InsertSplit` line by line against `rte/model.rs` (the 7 ported tests cover only part of the
  branches: atomic blocks at range ends and paste into cells are untested). Where:
  `src/Barbican.Core/Editor/Doc.cs`, `Block.cs`.
- **New project `Barbican.Editor.Layout`** (not in the plan's §3.2 layout): Skia + RichTextKit,
  no UI framework, so layout tests run headless on both OSes. Check that this layering is wanted
  (alternative: inside the App head, untestable on the VM). Where: `src/Barbican.Editor.Layout/`,
  `Barbican.slnx`.
- **`RustTestAttribute` created here** (xUnit v3 `ITraitAttribute`, trait `RustTest`). Other
  streams may create their own; merge into one. The 7 `rte::model` tests are M3 rows in the plan:
  the catalogue streams must mark them ported. Where:
  `tests/Barbican.Core.Tests/RustTestAttribute.cs`, `tests/Barbican.Core.Tests/Editor/ModelTests.cs`.
- **`TextContrast` alpha curve (k = 1.6) to match DirectWrite darkness.** An empirical fit at one
  size, one scale (100 %), dark text on white. Risk: wrong for light-on-dark (dark theme),
  coloured runs, other sizes and 125/150 % scaling; the curve also changes selection-free layer
  cost (one `SaveLayer` per paint). Where: `src/Barbican.Editor.Layout/DocLayout.cs` (`Paint`,
  `ContrastFilter`), `LayoutStyle.cs`; evidence `docs/adr/0006/harness-results.txt`.
- **CSS line-height conversion.** RichTextKit's `LineHeight` multiplies font spacing, not size;
  `StyleMapper.CssLineHeight` converts using `FontMapper.Default` metrics cached per
  family/weight/italic. Check font-fallback runs (emoji, CJK) on a line: their own spacing may
  still make a line taller than 22.5 px. Where: `src/Barbican.Editor.Layout/StyleMapper.cs`.
- **ADR number 0006 chosen without coordination** (0004/0005 assumed for S1/S2, S4/S5 are on
  `m0-s45`). Renumber at merge if it collides; the assets folder is `docs/adr/0006/`.
- **SkiaSharp pinned to 4.156.0 (Uno 7.0.65's version) and HarfBuzzSharp 14.4.0.100**, including
  `SkiaSharp.NativeAssets.Linux`, overriding RichTextKit's 4.153.1 natives. S1 picks the Uno
  version: if it is not 7.0.65, the pins must move with it. Where: `Directory.Packages.props`.
- **Spike packages file.** `spikes/Directory.Packages.props` imports the root one and adds
  `Microsoft.WindowsAppSDK` 2.6.5 and SDK BuildTools for the harness, to avoid colliding with S1.
  The harness also relaxes VSTHRD100/101/103, CA1515, CA1861, MA0048 in its csproj. Where:
  `spikes/`.

### Facts that contradict the plan or earlier ADRs
- **Uno 7.0.65 ships a Skia `RichEditBox`** (36 files under
  `src/Uno.UI/UI/Xaml/Controls/RichEditBox/` at tag 7.0.65, 1 at 6.7.424), contradicting ADR 0001
  "Consequences" and PORT-PLAN.md §3.8. ADR 0006 §7 keeps the custom editor for the HTML
  round-trip reason; the user may want to know. Also: the plan says Uno 6.6 is current; 7.0.65 is.
- **RichTextKit is Apache-2.0, not MIT** (nuspec of 0.6.173). Compatible with AGPL-3.0; a vendored
  copy must keep its LICENSE/NOTICE, and the release's third-party notices should list it.

### Partially verified
- **Text-quality gate measured only on Windows at 100 % scale, light theme, one paragraph.** Uno
  (`SKCanvasElement` vs Uno `TextBlock`) not run: no App head before S1. Where: ADR 0006 "Pending".
- **IME:** only the committed-text forwarding was exercised (programmatically, no real input);
  composition events never fired. Uno's X11 IBus/Fcitx support was read in Uno's source at tag
  7.0.65, not run. Needs the M3 [HAND TEST].
- **Hidden harness window.** Off-screen at (-32000, -32000), `IsShownInSwitchers = false`,
  `AppWindow.Show(false)`. I did not verify that no taskbar flash or focus change happens on every
  shell configuration (multi-monitor layouts with negative coordinates far out are assumed
  absent). Compare with S4's hidden-window design and reuse one. Where:
  `spikes/s3-editor/S3Harness/HarnessWindow.cs` (`Start`).
- **BiDi caret ambiguity pinned, not solved.** The hit-test round trip fails at the two offsets
  around an RTL run; the test accepts only those. Where:
  `tests/Barbican.Editor.Layout.Tests/BlockLayoutTests.cs` (`Hit_test_at_each_caret_returns_that_caret`).
- **TextPointer offset shift of 2** in the harness's caret comparison (TextBlock content start
  plus Run start) was found empirically (shifts 0/1/2 all logged; 2 is the one with 0 line
  mismatches). Where: `HarnessWindow.CompareCarets`.
- **Perf numbers are single-machine, Debug build, median of 15.** Where:
  `docs/adr/0006/harness-results.txt`.
