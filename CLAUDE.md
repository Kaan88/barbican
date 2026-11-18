# Barbican: rules for every agent

Barbican is a native desktop mail client for Stalwart (JMAP), ported from a Rust/Slint app to C#:
**WinUI 3 on Windows, Uno Platform (Skia, X11) on Linux.** The master plan is
[docs/PORT-PLAN.md](docs/PORT-PLAN.md); read the sections your brief names before you start.
AGENTS.md points here; this file is the single source of the rules.

**This repository is PUBLIC.** Read "Privacy" before you write anything.

## Codex continuation (2026-11-19)

Codex agents must also read [docs/CODEX-HANDOFF.md](docs/CODEX-HANDOFF.md).
It carries the user's takeover instructions: Terra for former Sonnet assignments,
Luna for former Haiku assignments, explicit reasoning settings, current agent
limits, the four M6 pause checkpoints, and the resume prompt. Apply its
Codex-specific model and attribution instructions when older plans name Claude
models; all safety, privacy, build and account rules below remain in force.

## Start of every session
1. Read `.local/accounts.md` (gitignored, machine-local). It maps the placeholders used in this repo
   (`<readonly-account>`, `<test-account>`, `<server>`, `<admin-key-file>`, `<rust-repo>`,
   `<bulwark>`, …) to the real accounts and paths, and repeats their rules. If it is missing, ask
   the user; never guess.
2. Check your git identity in this clone: `git config user.email` must be the GitHub noreply
   address (`<id>+<login>@users.noreply.github.com`), never a personal address. The pre-push hook
   refuses anything else.
3. Enable the hooks once per clone: `git config core.hooksPath .githooks`.

## Standing rules
- **Push every commit immediately, on every branch** (including agent branches and worktrees).
- **Never touch the real mouse, keyboard or screen.** No `SendInput`, `InputInjector`, FlaUI
  `Mouse`/`Keyboard`, `SetCursorPos`, xdotool, focus stealing or real-window driving on the user's
  machine. Hidden in-app automation only, with your own port and profile (PORT-PLAN.md §4.4). The
  banned-API analyzer and `tools/build.ps1 rules` enforce this.
- **`<readonly-account>` is read-only.** Writes (send, move, delete, flag, mark read, settings)
  happen only on `<test-account>`, and every write test cleans up after itself.
- **Never read `<admin-key-file>`** (path in .local/accounts.md), never copy anything from it, never
  use the admin API key yourself. Admin is tested read-only; the user enters the key in Settings.
- **Never sign in, sign out or clear credentials of the Rust app**, and never touch its profile.
  C# dev builds use their own profile (`BARBICAN_PROFILE=dev`, PORT-PLAN.md §3.5).
- **Don't kill processes you didn't start.** The user may be running the Rust `barbican.exe`, other
  agents may be building.
- **No OS changes without the user's OK:** registry associations, autostart, certificates, WSL,
  Windows Sandbox, system-wide installs.
- **The Rust repo (`<rust-repo>`, GitHub `Kaan88/barbican-rust`, tag `rust-final`) is read-only.**
  Read it; never commit to it.
- **Bulwark (`<bulwark>`, version 1.21.2) and Stalwart's source are the references.** Cite the
  Bulwark file:line and the Stalwart path for every JMAP behaviour, in the code's module header.
- **Port the spec tests first** (PORT-PLAN.md §2.4), and update `spec/` status in the same commit
  as the code.
- **Commit messages** say the behaviour decided and why, and end with
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Build limits (the machine froze under parallel builds)
- Build and test **only** through `tools/build.ps1` (Windows) and `tools/linux.ps1` (Linux VM).
  Direct `dotnet build` / `dotnet test` is forbidden.
- `tools/build.ps1` takes one of `BARBICAN_BUILD_SLOTS` (default 4) machine-wide slots, runs at
  BelowNormal priority with `-maxCpuCount:4` (passed explicitly: the dotnet CLI overrides the
  rsp's value), `-nodeReuse:false` (Directory.Build.rsp) and the compiler in-process. Rust builds
  (rare reference runs) go through the global rustc cap.
- MSBuild verbosity through build.ps1: `-verbosity:diag` (`-v` is PowerShell's `-Verbose`).
- At most 3 agents compile at once, at most 5 agents alive. When you finish:
  `tools/build.ps1 shutdown`.
- Worktrees: `<downloads>/wt-m<n>-<stream>` on branch `m<n>/<stream>`; prune after merge.

## Commands
| What | Command |
|---|---|
| Build everything (Windows) | `tools/build.ps1` |
| Build + all tests (Windows) | `tools/build.ps1 test` |
| One test project | `tools/build.ps1 test -Project tests/Barbican.Core.Tests/Barbican.Core.Tests.csproj` |
| Filter tests (xUnit v3) | `tools/build.ps1 test -Class "*TokenTests"` or `-Method "*Palette*"` |
| Regenerate design tokens | `tools/build.ps1 gen-tokens` (check only: `-Check`) |
| Real-input rule check | `tools/build.ps1 rules` |
| Linux build + tests (VM) | push first, then `tools/linux.ps1 test` (or `build`; `-Branch`, `-Class`, `-Method`). Works from a worktree: the VM address is read from the main checkout's `.local/` |
| Linux VM first-time setup | `tools/linux.ps1 setup`; reachability: `tools/linux.ps1 ping` |
| One head only (inner loop) | `tools/build.ps1 build -Head win` or `-Head desktop` (App head; `-Project` for others) |
| Publish the app | `tools/build.ps1 publish -Head win -Rid win-x64` (or `-Head desktop -Rid linux-x64`) → `artifacts/publish/<head>/<rid>` |
| Run a UI project on the VM | `tools/linux.ps1 setup-ui` once, then `tools/linux.ps1 xrun -Project <csproj> -Out <name>` (private Xvfb; results in `artifacts/linux/<name>`) |
| Hidden smoke run | Windows: `tools/auto-run.ps1 smoke` (published exe, `--smoke`); Linux: `tools/linux.ps1 xrun -Project src/Barbican.App/Barbican.App.csproj -Script smoke -Out smoke` |
| Automation build (hidden, ADR 0007) | `tools/build.ps1 build -Configuration Automation -Head win`, then `tools/auto-run.ps1 start -Port <p>` / `send -Command '...'` / `scenario` / `stop`; popup probes only with `start -PrivateDesktop`. Linux: `tools/linux.ps1 xrun ... -Configuration Automation -Script auto` |
| Portable zip | `tools/build.ps1 publish -Head win`, then `tools/package-portable.ps1` → `artifacts/portable/` |
| Red tests (expected to fail until their stream lands) | `python tools/red-check.py` (excluded from `tools/build.ps1 test` by `[Red("stream")]`; `--list` counts them) |
| Plugin catalogue | `python tools/gen-hooks.py` (HookNames.g.cs from `spec/plugins/hooks.csv`; `--check`), `python tools/extract-plugin-catalogue.py <bulwark>` |
| Privacy check before commit | `sh tools/privacy-check.sh --worktree` |
| Spec status / milestone gate | `python tools/spec-check.py` (summary), `--milestone M<n>` (gate: exit 1 while T- rows of M<n> are unmapped), `--write` (fill `csharp_test`/`status` in `spec/rust-tests.csv` after porting tests) |

Run PowerShell scripts with `powershell -NoProfile -ExecutionPolicy Bypass -File tools/<script>.ps1 …`
if the execution policy blocks them.

**Green** means: `tools/build.ps1 test` passes locally **and** `tools/linux.ps1 test` passes on
the VM. There is **no GitHub CI** (the user's decision, docs/adr/0002): never add a workflow that
runs on push or pull request. Wherever the plan says "CI", use the two commands above.

## Layout
```
Barbican.slnx               the solution (Core, Presentation, Platform, App head, tests, tools)
Directory.Build.props       strictness for every project (nullable, warnings as errors, analyzers)
Directory.Packages.props    every package version (central package management)
Directory.Build.rsp         -maxCpuCount:4 -nodeReuse:false
BannedSymbols.txt, eng/     banned APIs: all projects / Core / Presentation / tests
src/Barbican.Core/          net10.0, no UI types: models, JMAP, sync, services, settings schema
src/Barbican.Presentation/  net10.0: ViewModels, menus/shortcuts as data, design tokens (Theme/)
src/Barbican.Platform/      net10.0: platform service interfaces
src/Barbican.App/           the Uno single-project head (Uno.Sdk, WinUI 3 + Uno Skia; docs/adr/0004); Assets/mail/shell.js
tests/<Project>.Tests/      xUnit v3 on Microsoft.Testing.Platform; shared settings in tests/Directory.Build.props
tests/fixtures/             test fixtures (Bulwark vectors, recorded responses, the spam-score plugin source)
tools/                      build.ps1, build.sh, linux.ps1, privacy-check.sh, Barbican.GenTokens, auto.py & probes
spec/                       the behaviour catalogue (PORT-PLAN.md §2.3): IDs, CSVs, behaviours/, feel/
docs/                       PORT-PLAN.md, ROADMAP.md, FABLE-REVIEW.md, adr/, legacy/ (Rust-era docs), milestones/
design/                     README.md, MOTION.md, mockups/ (source of truth for the UI), legacy-slint/theme.slint
packaging/                  icons, Linux .desktop and Debian scripts, install-kind markers, WiX source (reference)
prototype/winui/            the WinUI 3 prototype (reference only, not in the solution, no strictness)
prototype/uno/              the same prototype XAML built as an Uno single project (spike S1)
spikes/                     throwaway M0 spikes (not in the solution; relaxed rules in spikes/Directory.Build.props)
```
Rules for new code (PORT-PLAN.md §3.2, §4.1): one type per file (MA0048), one view per file, one
ResourceDictionary and one DI registration file per feature, records for models, `Result<T>` for
expected failures in the JMAP layer, `TimeProvider` for time, every Core file starts with the
Bulwark file / RFC section / Stalwart path it mirrors. Don't weaken a rule in
Directory.Build.props; suppress one diagnostic locally with a reason if you must.

Where things go:
- Spec files: `spec/` (`spec/README.md` explains IDs); feel results: `spec/feel/M<n>/`.
- Ported Rust tests: the owning test project, tagged `[RustTest("module::test_name")]` with the value
  exactly as `spec/rust-tests.csv` `rust_test_tag` has it (no `tests::` segment); the attribute is
  `tests/Shared/RustTestAttribute.cs`, linked into every test project. Run `python tools/spec-check.py --write`.
- Context menus: `spec/menus.md` item by item. Every popup and flyout is in-window
  (`ShouldConstrainToRootBounds="True"`, ADR 0007); a windowed popup can appear on the user's real screen.
- Milestone reports: `docs/milestones/M<n>.md`. Decisions: `docs/adr/NNNN-title.md`.
- Generated files (`*.g.cs`, `src/Barbican.App/Themes/*.xaml`) are never edited by hand.

## Privacy (the repository is public)
- Never commit personal data: no real e-mail addresses or names of the user's accounts, no server
  hostnames, no local user names or home paths, no tokens or keys, no screenshots of real mail.
  Use the placeholders; the real mapping lives only in `.local/accounts.md`.
- Commit author and committer must be the GitHub noreply identity.
- The private patterns live in the gitignored `.local/privacy-patterns.txt` of the main checkout (never in a tracked file); the script fails closed without it.
- Before every commit run `sh tools/privacy-check.sh --worktree`; before every push the
  `.githooks/pre-push` hook runs it on what you push. It checks text only: scan binaries you add
  (DLLs, images) by hand, for example for home paths compiled into them.
- Screenshots in the repo show sample data only (FakeJmap or fixtures), never a real mailbox.

## Fable review log
`docs/FABLE-REVIEW.md` lists what a later reviewer model (Fable) must look at. Every agent appends,
in its own branch, under a heading with its stream name, one bullet per item: what to review, why
(risk, judgement call, uncertainty, deviation from Bulwark/RFC/plan, security, privacy, anything
verified only partially), and exact paths and commits. Add items as you go, not only at the end.
