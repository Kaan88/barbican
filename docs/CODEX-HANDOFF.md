# Codex continuation

**Start in the C# milestone checkout.** The active branch is `codex/m6b-bulwark-ui`, in `<downloads>/wt-m6b-bulwark-ui`. A chat opened in `webmail-main/desktop` starts in the retired Rust reference. The older coordinator checkout at `<downloads>/barbican` holds unrelated local test deletions. Preserve them.

Read [CLAUDE.md](../CLAUDE.md), then the [current M6b work list](milestones/M6b-plan.md#current-work). Use `git status --short --branch` in the milestone worktree before resuming. The implementation was paused during managed activation on November 20. The local technical handoff, when present, is `docs/milestones/M6b-managed-activation-handoff.md`. It identifies the uncommitted files and their dependencies. The branch work list can also be used from a fresh clone. The Windows preview and UI evidence are in the [verification history](milestones/M6b-solo-parity.md#windows-portable-preview-at-4c46940).

## Working in this checkout

- Before a Windows build set `$env:BARBICAN_BUILD_SLOTS = '3'` in the same shell and use `tools/build.ps1`. Use an isolated automation profile and a free port. Reserve the user's `dev` profile. Use a private desktop for popups. Stop only processes you started. For permitted live checks follow the account rules and use `agent-ui` only where previously authorized. Keep M6 security-setting write tests on FakeJmap and clean up permitted test-account writes.
- Preserve worker edits and stage only the files for the verified change. Run the worktree privacy check, use the configured GitHub noreply identity and push authorized milestone commits to their branch. Attribute Codex work accurately. Main merges, tags, releases and OS changes require their own authorization.
- The coordinator integrates independent work and owns the [review record](FABLE-REVIEW.md). Use the [roadmap](ROADMAP.md) for milestone order and [PROGRESS.md](PROGRESS.md) for dated evidence. On a pause, record which work is local, the last pushed code, the last tested build and the next reproducible action in the active work list. Detailed technical notes can live beside it when needed.

Earlier stream ownership, implementation history and carryovers are recorded in [the progress history](PROGRESS.md) and the milestone reports. Git revision `0bc5f0c` preserves the earlier continuation guide.
