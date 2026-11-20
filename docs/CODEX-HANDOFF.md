# Codex continuation

**Start in the C# milestone checkout.** The active branch is `codex/m6b-bulwark-ui`, in `<downloads>/wt-m6b-bulwark-ui`. A chat opened in `webmail-main/desktop` starts in the retired Rust reference. The older coordinator checkout at `<downloads>/barbican` holds unrelated local test deletions. Preserve them.

Read [CLAUDE.md](../CLAUDE.md), then the [current M6b work list](milestones/M6b-plan.md#current-work). Use `git status --short --branch` in the milestone worktree before resuming. The Windows checkpoint at `3791df4` includes managed activation and the composer footer. Its [verification history](milestones/M6b-solo-parity.md#windows-checkpoint-at-3791df4) records the reviewed portable. The branch subsequently verified [nested Settings](milestones/M6b-solo-parity.md#nested-settings-and-restart) and [typing proofreading](milestones/M6b-solo-parity.md#typing-checker-on-windows). The local managed activation handoff, when present, describes the earlier pause.

At the current pause, `artifacts/m6b-paused/dialog-source/` holds the later buildable dialog files relative to the checkout root. The isolated Windows build and 11 focused tests passed. Resume with the private `addons-fixture dialog-probe` walkthrough. `artifacts/m6b-paused/scoped-mail.patch` is unverified transport and delivery fencing work for scoped mail reads. The M6b work list owns the remaining compatibility gates and the final integrated candidate review.

## Working in this checkout

- Before a Windows build set `$env:BARBICAN_BUILD_SLOTS = '3'` in the same shell and use `tools/build.ps1`. Use an isolated automation profile and a free port. Reserve the user's `dev` profile. Use a private desktop for popups. Stop only processes you started. For permitted live checks follow the account rules and use `agent-ui` only where previously authorized. Keep M6 security-setting write tests on FakeJmap and clean up permitted test-account writes.
- Preserve worker edits and stage only the files for the verified change. Run the worktree privacy check, use the configured GitHub noreply identity and push authorized milestone commits to their branch. Attribute Codex work accurately. Main merges, tags, releases and OS changes require their own authorization.
- The coordinator integrates independent work and owns the [review record](FABLE-REVIEW.md). Use the [roadmap](ROADMAP.md) for milestone order and [PROGRESS.md](PROGRESS.md) for dated evidence. On a pause, record which work is local, the last pushed code, the last tested build and the next reproducible action in the active work list. Detailed technical notes can live beside it when needed.

Earlier stream ownership, implementation history and carryovers are recorded in [the progress history](PROGRESS.md) and the milestone reports. Git revision `0bc5f0c` preserves the earlier continuation guide.
