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

## Spikes S4 and S5 (stream: port-s45)
- **S4 conclusions rest on one machine and one off-screen position.** What: WGC cadence of 20.8 ms (48 fps) for the
  window parked at x = -12000 on a 144 Hz display, the UIA click path, the in-window flyout rule and the black
  `PrintWindow` result. Why: the 20.8 ms quantum is unexplained (DWM, WGC pacing or panel) and it is coarser than the
  plan's 16 ms input-to-frame bar; the plan's feel gates may need re-stating or a 60 Hz/virtual display. Where:
  `docs/adr/0004-hidden-automation.md` (Open issues), `spikes/s4-automation/Capture.cs`, `Probe.cs`.
- **Deviation from the plan: clicks use a UIA client, not in-process AutomationPeers.** What: `Uia.cs` (FlaUI.UIA3) runs on
  the server thread; peers made by `CreatePeerForElement` lack patterns on `ListViewItem` and `TextBox`. Why: judgement call;
  it adds FlaUI to the AUTOMATION configuration and has a blind spot (XAML over a WebView2 is invisible to UIA clients, so
  `peer-click`/`invoke ID` remain). Check the choice and that FlaUI never reaches the release app. Where: `Uia.cs`,
  `Automation.cs` (`Dispatch`), ADR 0004 item 3.
- **Hard rule discovered: every popup must be `ShouldConstrainToRootBounds="True"`.** What: default flyouts open in a separate
  `PopupWindowSiteBridge` HWND that is outside the app capture and was seen at the edge of the user's real screen. Why:
  security/privacy of hidden automation and a UX constraint on every menu. Verified only for `MenuFlyout`; `ComboBox`,
  `ToolTip`, `AutoSuggestBox`, `TeachingTip`, `CommandBarFlyout` and `ContentDialog` are untested. Where: ADR 0004 item 4,
  `spikes/s4-automation/MainWindow.xaml`.
- **Disclosure: the first smoke run showed an unconstrained flyout strip (about 118 px wide) at the left edge of the user's
  screen for a few seconds.** No input was sent and the foreground window did not change; it happened before the constraint was
  set. Fixed in the spike. Why listed: the standing rule is "hidden only". Where: ADR 0004 Open issues.
- **Window position and process start were verified, not exhaustively.** The foreground-window check
  (`spikes/s4-automation/check-focus.ps1`) samples every 40 ms for about 2.4 s per run (2 runs); a shorter focus flash between
  samples would be missed. `STARTUPINFO` hidden launch worked; launch through other shells/`cmd start` is untested.
- **Probe semantics are an interpretation.** What: `start` = WGC `SystemRelativeTime` (present time, observed 3 to 6 ms after
  arrival), `end` = max(start, arrival); `render` in `motion.py` is therefore 0, not the Rust render time. Why: the gates'
  definitions (`input -> first changed frame`, gap > 25 ms) were mapped, not re-derived. Where: `Probe.cs`, ADR 0004 item 7.
- **S5 stores secrets under new names and migrates by explicit import** (`Barbican.Next/<profile>/<name>`, `CRED_PERSIST_LOCAL_MACHINE`
  instead of the Rust `ENTERPRISE`, UTF-8 JSON instead of UTF-16LE). Why: judgement call to protect the Rust sign-in and avoid
  roaming refresh tokens; the user may prefer roaming or reusing the Rust names. Where: `docs/adr/0005-credential-migration.md`.
- **S5 read real Credential Manager blobs in memory (structure only).** What: `spikes/s5-credentials/read-format.ps1` loads the
  four `*.Barbican` generic credentials via `CredEnumerateW`, zeroes the buffers and prints names, type, persistence, size,
  encoding and JSON key names, never values; it enumerates only those four exact names. Why listed: it is the one step that
  touched secrets; check the script prints nothing sensitive (blob lengths of the two plain-string entries were looked at
  once and left out of the ADR). Where: `spikes/s5-credentials/read-format.ps1`.
- **Linux format of the Rust credentials is source-only.** Derived from `keyring 3.6.3` `secret_service.rs`, not observed.
  Where: ADR 0005.
- **Spike code is not production code.** `spikes/` is outside `Barbican.slnx` and the strictness rules (`Nullable` off, no
  analyzers, `DllImport`, FlaUI). Reuse the ideas, not the files. Where: `spikes/s4-automation/`, `spikes/s5-credentials/`.
