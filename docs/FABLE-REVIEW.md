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

## M0 catalogue A (stream: m0-cat-a)

### Privacy
- **Hard-coded test account in the Rust source.** What: `reader.rs:1168` holds a real test-account
  address as a constant for the live checks. It was not copied into `spec/`; the port must take the
  test account from `.local/accounts.md` through configuration. Why: the repository is public.
  Where: Rust repo `src/reader.rs:1168` (rust-final `bba9613`), `spec/behaviours/reader.md` (open questions).
- **Fixture names in the test catalogue.** What: `spec/rust-tests.csv` paraphrases test assertions and
  quotes a few fictional fixture values (names such as "Ann Lee", "Doe, J"). Why: checked by hand and by
  `tools/privacy-check.sh`; confirm no real contact names slipped in. Where: `spec/rust-tests.csv`
  (`asserts` column).

### Judgement calls and deviations
- **Milestone mapping follows Appendix B exactly.** What: M1 76, M2 89, M3 66, M4 45, M5 14, M6 18,
  M7 7, M8 35, M9 3 (= 353). Why: counts were checked by script, not by eye. The lite test
  (`lite::renders_email_html`) is marked `dropped? (decision needed)`. Where: `spec/rust-tests.csv`,
  `spec/behaviours/reader.md` B-READ-045/046.
- **Lite mode decision.** What: PORT-PLAN Appendix A says lite is dropped with plain-text mode kept.
  The user must confirm. Why: a user-facing feature removal. Where: `spec/behaviours/reader.md` B-READ-046.
- **Sender pictures default.** What: Bulwark has favicons and BIMI on; Barbican has them off and asks
  first (`senderFavicons`, `gravatarAvatars`). Why: deliberate privacy deviation, needs a D- entry in
  `spec/decisions.md`. Where: `spec/behaviours/reader.md` B-READ-032, `avatars.rs:1-7`.
- **Sieve replace is last-writer-wins.** What: no `ifInState` on Sieve script replace (J-030). Why: same
  as Bulwark, but Stalwart supports `ifInState`; decide whether the port adds it. Where:
  `spec/jmap.csv` J-030, `spec/behaviours/jmap.md` B-JMAP-054.
- **Stable test ids.** What: `T-###` ids are assigned in file-path and line order, so they shift when a
  Rust test is added. Why: spec rows will be referenced from C# attributes. Suggest keying on module
  plus name for the long term. Where: `spec/rust-tests.csv` (generated by a script, not kept in the repo).

### Contradictions and partial verification
- **Truncated body notice.** What: B-READ-010 says `displayed_truncated` exists and is tested, while
  J-053 says a truncated body is shown with no notice. Why: one of the two is out of date. Resolve in M1.
  Where: `spec/behaviours/reader.md`, `spec/jmap.csv` J-053.
- **`rust_verified` column in jmap.csv.** What: derived from the compliance doc's live and not-live lists
  and the test names it names. Not re-checked against the code. Why: a reviewer should spot-check a few
  rows (for example J-023 and J-063). Where: `spec/jmap.csv`.
- **Line numbers in the behaviours.** What: taken from a script dump at rust-final. Some are approximate
  ranges (for example `instance.rs:146-187`, `mdn.rs:276-298`). Why: the Rust repo is frozen, so they stay
  valid, but the port's rules must cite the new code. Where: `spec/behaviours/*.md`.
- **Test descriptions are partial.** What: each description in `spec/rust-tests.csv` is written from the
  first four assertions a script found. Not read in full: `icon::write_icon_assets` (no assertion found),
  `mailto::registration_plans`, the tail of `sieve.rs` and `updater.rs`, and the `rte` tests. Why: a
  reviewer should read these bodies before the milestone that owns them. Where: `spec/rust-tests.csv`.
- **Unread WIP intent.** What: B-SYNC-022 cites the `mat-switching` WIP intent from the plan. The
  branch's diff was not read. Why: the behaviour may differ from the plan's summary. Where:
  `spec/behaviours/sync.md`, Rust repo branch `mat-switching`.
- **Font names.** What: `rte/layout.rs` uses Segoe UI and Consolas. Why: Windows-only fonts; Linux (Uno)
  needs a substitute and a visual check. Where: `spec/behaviours/compose.md` B-COMP-069.
- **Coverage gap.** What: the five behaviour files hold 257 B- rows against the plan's estimate of
  1,000 to 1,500 for the whole catalogue. Why: this stream covers mail, JMAP, sync, reader and compose
  only. Admin, dashboard, files, filters, sieve, contacts, calendar, notifications, updater and platform
  are not covered. Where: `spec/behaviours/`.
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
## M0 catalogue: interactions and layout (stream: m0-cat-c)

- **Re-baseline is mostly carried, not re-read.** What: of 471 rows in `spec/interactions.csv`, 84
  changed status, 9 were re-read and confirmed, 378 are `carried` (inventory status kept). Why: the
  re-check used Rust module headers, `shortcuts.rs`/`palette.rs`/`dnd.rs`, the ctxmenu and reader
  code and the parity-round-2 report, not a click-through of every row; areas untouched by the later
  commits (settings pages, admin, most menus) were not re-verified. Review a sample of `changed` rows
  (evidence column names module and function) and of `carried` rows in the busiest areas (reader
  toolbar, composer, settings). Where: `spec/interactions.csv`, `spec/interactions.md`.
- **"D" means a code path exists.** Many new D rows (schedule send, scope dialog, quick create,
  one-click unsubscribe, invitation RSVP, drag and drop) were never driven live in Rust
  (PARITY-ROUND2); the evidence column says so. Treat them as "implemented, unverified" when judging
  the C# port against Rust. Where: rows I-042, I-208, I-211, I-212, I-241, I-300, I-314, I-337.
- **Judgement: milestone assignment.** Each row got a milestone from its area with per-row
  overrides (for example reader display M1, menus and actions M2, safety features M10). Check that
  the M1 and M2 loads are realistic; M2 holds 142 rows. Where: `milestone` column.
- **Judgement: rows moved to D on inference.** I-322 and I-323 (hour-slot click lands on the clicked
  day) are inferred from `cal_editor::quick` taking a day column, and I-454 (palette covers sign out,
  theme, switch account) from the `palette.rs` command list, not from running the app. Where: I-322,
  I-323, I-454.
- **Layout extraction is by reading, not measuring.** What: `spec/layout/*.md` copies paddings,
  radii and sizes out of `ui/*.slint` with `file:line`; nothing was rendered, and computed values
  (event rectangles, dashboard geometry, RTE document boxes) are excluded. Why: risk of a
  transcription slip or of a value that Slint overrides at runtime (for example `Prefs.fs`). Check:
  render the Rust app hidden at 1400 x 900 in both styles and compare a few rows (mail row 82, rail
  64/76, calendar hour 52/54, composer toolbar 50) before trusting the rest. Where: `spec/layout/`.
- **Open layout points.** No minimum window size in Slint (derived); the C# window needs explicit
  minimums. Calendar Tasks view, contact duplicates/import dialogs and several PIM dialogs are
  listed by component and line but not itemised. Where: `spec/layout/shell.md`,
  `spec/layout/calendar.md`, `spec/layout/contacts.md`.
- **Slint line numbers are cited as at `rust-final`.** `app.slint` grew to 6059 lines; the
  inventory's `app.slint:NNNN` locations in `carried` rows are stale and are marked as such.
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
