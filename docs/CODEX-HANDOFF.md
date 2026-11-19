# Codex takeover and resume guide

Updated 2026-11-19. Read this with [CLAUDE.md](../CLAUDE.md),
[PROGRESS.md](PROGRESS.md), and [the M6 plan](milestones/M6-plan.md).
This is the Codex-specific continuation of the user's Claude handoff.

## Selected UI guidelines (2026-11-19)

The user selected **Bento Balanced**, **Nordic Focus** (integrated sender/context panel,
with an accessible show/hide toggle and expanding reader), and **Studio** (horizontal app navigation
and a warm editorial workspace). Modern WinUI 3 materials, depth and fluid feedback are also requested with
accessibility and platform fallbacks. The user directed all future UI work,
including Claude's, to use them. Read [design/UI-GUIDELINES.md](../design/UI-GUIDELINES.md)
and the updated CLAUDE.md. Additional alternatives by the same Terra agent remain proposals.
The selected images are stored in the repository; do not depend on a machine-local gallery.

## Project and starting state

The active project is the C# Barbican checkout at `<downloads>/barbican`:
WinUI 3 on Windows, Uno/Skia on Linux, JMAP against Stalwart.
`<rust-repo>` is the older Rust app and is a read-only reference. Start the
new Codex chat in the C# project so the sandbox includes that checkout and
obtain access to the existing stream worktrees when needed.

The source session was Claude's **Confirmation to proceed**. Its final
handoff is recorded in PROGRESS.md at `9719835` on `m6`.

- M4 calendar/tasks and M5 contacts/files are complete on their milestone branches.
- M6 settings, server features, and assistant is paused across four streams.
- No M6 implementation stream is merged into `m6`; it contains the plan and handoff notes.
- Nothing is merged to `main` or tagged. Do not do either without user authorization.
- Treat recorded test counts and cleanup reports as historical evidence. Verify
  current branch heads, uncommitted changes, and relevant processes before acting.

## Agent policy approved by the user

Keep the plan -> independent streams -> coordinator merge/test -> close-pass
workflow. The user explicitly authorizes delegation for continuing the project.
For Codex, replace old Sonnet assignments with **GPT-6.1 Terra** and old Haiku
assignments with **GPT-6 Luna**. This is a work assignment policy, not a claim
that Terra and Sonnet have equal capability.

| Role | Model and effort | Work |
|---|---|---|
| Coordinator | Current parent model | Own the plan, resolve contracts and merge conflicts, verify integration and milestone exit criteria. |
| Stream implementation and UI verification | `gpt-6.1-terra`, high | Resume one owned stream; split large tasks into verifiable steps. Use hidden app automation for UI work. |
| Narrow helper | `gpt-6-luna`, high | Mechanical edits, targeted searches, bounded fixes, test/log triage, and documentation with clear acceptance criteria. |
| Difficult cross-cutting issue | Parent first | Investigate and resolve or assign a stronger model only when necessary; do not silently switch routine workers to expensive models. |

Terra's visual abilities never authorize real mouse, keyboard, focus, or screen
control in this project. Hidden in-app automation remains mandatory.

Pass model and reasoning effort explicitly when supported. This session previously
omitted Luna from its spawn-tool list, but the user successfully spawned Luna High
in another chat on 2026-11-19. Recheck the new chat's actual tool capabilities;
do not conclude Luna is unavailable to the account from an old tool list. If an
explicit model override needs a fresh subagent context, give it a complete brief
with the relevant paths, ownership, rules, acceptance criteria, and pause state.

Run only as many workers as the current session permits. The previous Codex chat
allowed four agents total: coordinator plus three workers. With that limit, start
three streams and queue the fourth for the next free slot; do not lose a stream
or try to start all four simultaneously. Never exceed three concurrent builds.

## Read before implementation

1. Read AGENTS.md, CLAUDE.md, this guide, and the gitignored `.local/accounts.md`.
   Never read the admin key file named there.
2. Check the Git noreply identity and `.githooks` configuration. Check status,
   upstreams, and branch heads in the main checkout and each stream worktree.
   Preserve any user changes; do not reset, rebase, or force-push them.
3. Read PROGRESS.md, docs/PORT-PLAN.md for the milestone, the M6 plan's ownership,
   contracts, naming/decision prefixes, composition counts and threading rules,
   and the stream's WIP commit. Read its FABLE-REVIEW notes before resuming it.
4. Give each worker its branch/worktree, exact next step, owned files, commands,
   port/profile, cleanup duties, and required final report. Workers never merge
   their own branches into the milestone branch.

## Resume M6 from these checkpoints

| Stream | Branch / worktree | Last recorded commit | Next work |
|---|---|---|---|
| set-data | `m6-set-data` / `wt-m6-set-data` | `c2d9652` | Core does not compile. Add `AssistantJson.Push(JsonArray, JsonNode)`, fix RS0030/MA0051/MA0002 issues, build and run the 18 unproven tests, spec-check, decisions D-M6-A. |
| set-ui | `m6-set-ui` / `wt-m6-set-ui` | `a1e88a6` | Build Automation, recheck the fixed theme-write crash, hidden sample run on port 8921, inspect screenshots, Fable notes, D-M6-B04..B06, full tests. |
| srv-data | `m6-srv-data` / `wt-m6-srv-data` | `b87f023` | Add service tests using FilterTestRig, run the unproven rule/hook tests, complete hooks CSV, D-M6-C06/C07, Fable notes, spec-check, full tests. |
| srv-ui | `m6-srv-ui` / `wt-m6-srv-ui` | `36efd7d` | Finish Automation build, hidden sample run on port 8922, inspect screenshots, decisions/Fable notes, full tests and Windows head build. |

Removing a Red attribute is not proof of a passing test. Run the tests that the
paused streams unmarked without executing. Never claim them green from metadata.

When streams finish, merge into `m6` one at a time and resolve conflicts by
meaning: preserve distinct decision/Fable entries, combine independent
composition-count terms, and retain spec completion only where evidence supports
it. Verify unique decision/type names and generated hook consistency.

The close pass wires real services in place of sample fakes, checks background
thread notifications and UI dispatch, runs the full Windows suite and hidden
sample scenarios, performs the plan's permitted live checks with cleanup,
writes docs/milestones/M6.md and PROGRESS.md, and produces/verifies a portable zip.
Use port 8929 and an isolated automation profile for integration checks.

## Non-negotiable execution rules

- Before every build invocation set `$env:BARBICAN_BUILD_SLOTS = "3"` in that same
  PowerShell command. Build/test only with `tools/build.ps1`; never direct dotnet.
  Run builds in the foreground and shut down your build server when finished.
- Do not touch the user's real input or windows. Use tools/auto-run.ps1 and an
  isolated `auto-<port>` profile. `dev` is the user's profile according to the
  handoff; do not use it for agent checks. Use `agent-ui` only for the explicitly
  permitted live test-account checks. Do not touch the Rust profile.
- Read-only account means no writes, including mark-as-read. Only the test
  account permits writes, with cleanup. Leave no test filter, Sieve script or
  vacation response enabled. Security-setting writes remain FakeJmap-only
  where the M6 plan requires that.
- Never kill an app/build process you did not start. The old M2 process in the
  handoff is historical information, not permission to kill it.
- This repo is public. No real account data, home paths, secrets or real-mail
  screenshots in tracked files. Inspect sample screenshots before committing.
- Run `sh tools/privacy-check.sh --worktree` before every commit. Push every
  commit immediately. Codex commits must not falsely attribute authorship to
  Claude; the Claude-specific coauthor trailer applies to Claude-authored work.
- No OS changes or destructive cleanup without the required user authorization.
  No CI workflows. Preserve project analyzers, test requirements and contracts.

## Process and remaining work

Choose reasonable defaults following Bulwark and record them in spec/decisions.md.
Do not stop at each milestone for hand-test gates or routine questions. Record
human-only checks as unproven. Maintain FABLE-REVIEW notes as work proceeds, but
run no separate Fable reviews until the final whole-app review.

The current user goal is **complete the roadmap except M9 (Linux)**: finish
M6 -> M6b -> M7 -> M8 -> M10, then work with Astra to fix findings until Astra is
satisfied, then perform a UI walkthrough and fix look, feel and usability issues.
Produce and verify a fresh Windows portable zip after M6 closes; the delivered M6
preview does not replace that final milestone package. Astra review is part of this
run after implementation, not a separate review gate on every stream. Record its
findings, fixes and explicit final disposition against current commits. The later UI
walkthrough uses hidden native automation and synthetic data under the standing rules.
Windows-only results must be identified as such; Linux is deferred, not passed. On pause, save and push work, stop only your own processes,
clean test-account writes, and update PROGRESS.md with exact commits and next steps.

Known carryovers: crowded calendar event titles clip; scheduled-send banner can
remain after cancellation (S-02); reader can retain the old message after a
folder change (S-03); M3 human/live checks and M5 deferred features remain open.
Use PROGRESS.md and the milestone reports for the full list and evidence.

## Prompt for the new Codex chat

> Take over the C# Barbican project. Read AGENTS.md, CLAUDE.md,
> docs/CODEX-HANDOFF.md, docs/PROGRESS.md, the local account rules, and the M6 plan.
> Resume all four M6 streams from their existing WIP branches/worktrees. Use Terra
> High for stream implementation and Luna High for narrow helper work, within
> the actual agent limit and a maximum of three concurrent builds. Coordinate
> merges into m6 and complete the integration/close pass, then continue through
> the recorded milestone order. Preserve all safety/privacy rules, push every
> commit, and record defaults rather than waiting for routine hand-test gates.
