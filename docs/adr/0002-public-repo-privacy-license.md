# ADR 0002: Public repository, no GitHub CI, privacy rules, AGPL-3.0-only

- Status: accepted (2026-11-17)
- Deciders: the user (owner), recorded by the M0 bootstrap agent

## Context
The plan (PORT-PLAN.md §2.1, Q1) proposed a new **private** `Kaan88/barbican` with GitHub Actions CI
on every push. The user has no paid Actions minutes for private repositories, and then decided that
CI on push would slow the work down: "CI will slow us down too much. use my linux vm instead."

## Decision
1. **Repositories.** The Rust repository is renamed `Kaan88/barbican-rust` and stays **private**.
   The C# app is the new **public** repository `Kaan88/barbican`.
2. **No GitHub CI.** No workflow runs on push or pull request. Verification is local:
   - Windows: `tools/build.ps1 test` on the user's machine (slot-limited, low priority).
   - Linux: `tools/linux.ps1 test` runs `tools/build.sh` on the user's Linux VM over SSH (key auth
     only, one job at a time, `nice -n 10`, `-m:2`), against a clone of this public repository at
     `~/barbican`, fetching the exact pushed commit.
   - "Green" = both pass. Wherever the plan says "CI", these two commands are meant.
   - A release workflow may be drafted at the very end (M8/M10), **manual-only**
     (`workflow_dispatch`), never on push. Real-input UI tests (PORT-PLAN.md §4.4 layer C) need a
     throwaway VM; until one exists they are not run (see docs/ROADMAP.md).
3. **Privacy.** Nothing personal is committed: no real e-mail addresses or names of the user's
   accounts, no server hostnames, no local user names or home paths, no tokens or keys, no
   screenshots of real mail, no LAN details of the user's machines. Docs use placeholders
   (`<readonly-account>`, `<test-account>`, `<server>`, `<admin-key-file>`, `<rust-repo>`, …); the
   real mapping is in the gitignored `.local/accounts.md`, which CLAUDE.md tells every agent to
   read. Commit identities are the GitHub noreply address.
   - Enforced by `tools/privacy-check.sh` (run before commits, and by `.githooks/pre-push` on every
     push): forbidden patterns, an e-mail allowlist of fictional/public domains, commit identities.
   - Imported history was rewritten: the prototype's two commits were re-authored to the noreply
     identity, personal sample data in `Data.cs` was replaced, and its screenshots were dropped.
     Legacy documents were copied with the same substitutions (`docs/legacy/README.md`).
4. **License: AGPL-3.0-only.** Barbican ports Bulwark's behaviour and includes material derived
   from it (the settings schema, the dark-mail script, rules cited by file:line). Bulwark is
   AGPL-3.0-only, so the compatible choice for a public repository is the same license. The Rust
   repository had no license file (private, all rights reserved by the owner), so nothing there
   conflicts. Bulwark's provisional app-store permission is not adopted. Attribution: NOTICE.md.

## Consequences
- Public Actions minutes would have been free, but the user chose the VM for speed; revisit only if
  the user asks.
- Update hosting (PORT-PLAN.md Q6) gets simpler: releases can be published from this repository.
- Every agent must run the privacy check; a missed hit is a public leak. History rewrites after a
  push are expensive, so the check runs before the push, not after.
- The license choice is a judgement call recorded for review (docs/FABLE-REVIEW.md); the user may
  change it before the first release.
