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

## M0 spikes S1/S2 (stream: m0-s12)

### Incident
- **A MenuFlyout appeared on the user's real screen.** What: in S2 runs 1 and 2 on Windows the
  harness opened a `MenuFlyout` over the body of its off-screen automation window. On WinAppSDK
  2.6.5 the flyout is a windowed popup (`IsConstrainedToRootBounds=false` despite
  `ShouldConstrainToRootBounds=true`) and Windows clamped it onto the visible monitor: about 0.5–0.7 s
  at the right screen edge, twice per run, four times in all. No input was sent; nothing was
  clicked. Fix: the Windows harness now uses a constrained `Popup`, checks
  `IsConstrainedToRootBounds` in the same turn and closes it at once if not; the `ContentDialog`
  phase is guarded the same way; a real `MenuFlyout` runs on Windows only with
  `BARBICAN_SPIKE_WINDOWED_MENUS=1` (throwaway VM). Why: violates the "never touch the real screen"
  rule; any future UI automation (§3.7 menus, S4) must assume windowed popups escape off-screen
  windows. Where: `spikes/BodyShell/Scenario.Phases.cs`, docs/adr/0005 decision 2.

### Judgement calls and deviations
- **Uno.Sdk 6.7.49, not 7.0.22.** 7.0 was four days old and moves to SkiaSharp 4.x. Revisit at M2
  (S3 RichTextKit). Where: `global.json`, docs/adr/0004.
- **WebView2Aot removed from compile references (desktop TFM).** Its global `WebView2` namespace
  shadows the control. A build target removes one `ReferencePath`; the assembly still ships. Check it
  does not hide anything the Win32 Skia host needs at compile time. Where:
  `eng/Uno.WebView2Name.targets`.
- **Analyzer relaxations.** App head: CA1010, CA2007, MA0004 off; MA0048 off for `App.xaml.cs`.
  Spikes: many rules off and `Uno0001` not an error (`spikes/Directory.Build.props`,
  `.editorconfig`). Check the head relaxations are the minimum.
- **Prototype patched at build time** for Uno (`SizeInt32` object initialiser) instead of editing
  `prototype/winui`. Where: `prototype/uno/Barbican.Prototype.Uno.csproj`.
- **VM packages and library shim.** `setup-ui` installed `libwebkit2gtk-4.1-0`, GTK 3, Xvfb, fonts
  (plus dependencies) with `sudo -n apt-get`; `xrun` symlinks unversioned GTK/WebKit names into
  `~/.barbican-lib` instead of installing `-dev` packages. Where: `tools/linux-remote.sh`.
- **CSP port duplicated in the spike.** `spikes/BodyShell/MailDocument.cs` copies `csp_with`, the
  sandbox flags and the dark wrapper without the Rust tests. M1 must move it into Core with them.

### Security
- **Linux has no host-side request filter.** `WebResourceRequested` is not on Uno/Linux; remote
  blocking there is CSP-only (verified: 0 beacon hits with images off).
- **Linux link routing and navigation.** `target=_blank` link clicks raise nothing (lost, not
  routed); a script-initiated top-level navigation of the shell reached the server although
  `NavigationStarting` cancelled the load. The shell must route links itself on Linux, and no code
  path may navigate the shell. Where: docs/adr/0005 decision 4, `spikes/BodyShell/Scenario.Probes.cs`.
- **`shell.js` posts through `webkit.messageHandlers.unoWebView` on Linux.** Any page script with
  same-origin access could post too; the frames have no scripts except the hashed dark script. Check
  the host validates every message (`ready:N` sequence, `ctx:` bounds).

### Partially verified
- **The published Windows exe was not launched** (the App head shows a visible window; needs a hidden
  smoke mode). Publish sizes: win-x64 292.9 MB (R2R), linux-x64 160 MB (21 MB `DirectN.dll`).
- **WGC delivers frames only on change**, so the Windows phase counts are small (3–29 frames); a
  bad frame that equals its predecessor would not be recorded. Linux samples every 8 ms by XGetImage.
- **Linux overlay results come from Xvfb** (no compositor, no GPU). Header/body skew there was up to 2
  samples; real-desktop behaviour is unmeasured.
- **Inner-loop times** include `tools/build.ps1` start-up and the build-slot mutex; one machine, one
  sample each.
