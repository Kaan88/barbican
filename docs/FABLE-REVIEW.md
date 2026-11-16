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

## Spec catalogue part B (stream: m0-cat-b)

### Method and coverage
- **Behaviour rows are extracted, not read line by line.** What: `spec/behaviours/*.md` (412
  B- rows over 14 areas) come from module headers, doc comments, constants, `#[test]` names and
  commit subjects and bodies, not from reading all of the roughly 23,000 lines in scope. Why: this is
  the first pass of PORT-PLAN §2.3 step 1; the Opus sample review (step 5) has not run. Expect misses
  in the hard areas and rows that cite a test name without its logic. Where: `spec/behaviours/`.
- **Line numbers are from `rust-final`** (`bba9613`). Check any `file:line` before relying on it.
  Some rows cite a test and a commit without a line.
- **Shortcuts and palette live under `spec/behaviours/`.** The plan (§2.3) puts `shortcuts.md` at
  the `spec/` root. I used `behaviours/shortcuts.md` and `behaviours/palette.md` as the brief asked.
  The cat-a stream's `menus.md` and `shortcuts.md` may overlap; reconcile at merge. Where:
  `spec/behaviours/shortcuts.md`, `spec/behaviours/palette.md`.
- **Motion numbers are quoted from commit bodies.** Feel numbers in B-MOT-027 (13 to 26 ms, 16 ms,
  123 ms) come from commit `0530d4a`, not from the `tools/*-results` JSON files. Check them against
  `spec/feel/rust-baseline/` once that stream has merged. Where: `spec/behaviours/motion.md`.

### Settings wiring (`spec/settings.csv`)
- **Wired is a literal match, not a read.** What: a key counts as "wired" when its quoted string
  appears anywhere in `desktop/src/**/*.rs`. That gives 110 wired and 26 unwired of 136 keys. Why:
  a literal can be a write, a default table or an unused list entry, so 110 is an upper bound. The
  26 unwired keys match the GAPS P2-1 list by name, but GAPS says "about 40". Where:
  `spec/settings.csv` (`rust_hits` gives the first file:line). The generator is in the local
  scratchpad, not committed; re-run it if the schema changes.
- **Key count.** The schema holds 136 distinct keys over 137 keyed rows (`plainTextFont` appears
  twice) and 64 keyless rows. The plan's "201 keys" (§2.2, §5 M6) matches neither. Decide which
  number the port targets. Where: `ui/settings-schema.json` (Rust), `spec/settings.csv`.
- **Bulwark defaults are the schema defaults.** The `default` column is the schema's `default`
  field, which `extract_settings.py` copied from Bulwark. It is not checked against Bulwark's
  settings store. Where: `spec/settings.csv` column `default`.

### Decisions harvest (`spec/decisions.md`)
- **Every commit has a row.** What: 232 D- rows cover all 158 non-merge commits of `rust-final`
  plus the parity-round merges. Why: the brief asked for every decision and fix; several commit
  bodies are empty, and the reason is then the subject, marked as such in the row text where I
  saw it. Where: `spec/decisions.md`.
- **Three WIP branches are recorded as intent only.** `mat-overlay`, `mat-polish` and
  `mat-switching` intent comes from the commit subjects and the plan §2.6 text. Read their diffs
  before reviving them. Where: `spec/decisions.md` (WIP section).
- **Renumbering.** I renumbered D- IDs sequentially after removing duplicate rows. Nothing outside
  this file cites a D- ID yet; once other streams do, freeze the numbers. Where: `spec/decisions.md`.

### Gaps and live checks
- **G-094 to G-110 are finished items kept for traceability.** GAPS.md lists them as done in parity
  round 2. Their live status is partial in most cases. Where: `spec/gaps.csv`, `spec/live-checks.md`.
- **Priorities and milestones are mine.** Each G- row's milestone comes from PORT-PLAN §5 and §M10;
  the GAPS.md file does not assign them. Check the mapping, especially G-054 (external-recipient
  confirm, M3) and G-075 (mentions). Where: `spec/gaps.csv`.
- **Observed issues without a reproduction.** L-009 (stray "(no subject)" draft) was seen once and
  not reproduced; L-047 (focus after the rail) and L-049 (mini month shading) are observations
  without a fix. They are live checks, not known bugs. Where: `spec/live-checks.md`.
- **Hand checks mapped.** The round-2 hand list (13 items) is mapped to L- rows in
  `spec/live-checks.md`. Items 2, 5 and 6 map to several rows each; re-check the mapping.

### Privacy (public repository)
- **Account names.** PARITY-ROUND2 and GAPS name the real accounts and a server. I replaced them
  with `<readonly-account>` and `<test-account>` (the test account is the one the Rust report wrote
  to), and dropped the rest. The mapping in `.local/accounts.md` should confirm which placeholder
  is which. Where: `spec/live-checks.md`, `spec/gaps.csv` (none expected), `spec/decisions.md`.
- **Commit bodies.** Commit bodies quoted in `spec/decisions.md` contain no personal data (checked
  by eye and by `tools/privacy-check.sh --worktree`). The Rust log has not been searched with the
  full hard-pattern set on every commit body; run `git log --all` through the check once more.
