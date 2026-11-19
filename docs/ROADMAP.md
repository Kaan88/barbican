# Roadmap (C# app)

The plan and the full scope of each milestone: [PORT-PLAN.md](PORT-PLAN.md) §5. Decisions that
change the plan: [adr/](adr/). The Rust-era roadmap is in [legacy/ROADMAP.md](legacy/ROADMAP.md).

Current execution order: **M6 -> M6b -> M7 -> M8 -> M10 -> Astra review/fix loop -> UI walkthrough and usability fixes**. The user requests everything in the roadmap except M9 (Linux), with implementation followed by fixes until Astra is satisfied, then a look/feel/usability walkthrough. Historical hand-test gates below do not pause implementation. See [PROGRESS.md](PROGRESS.md) for evidence and carryovers.

| Milestone | Scope (short) | Status |
|---|---|---|
| M0 | Foundation, catalogue, tokens and spikes | Done |
| M1 | Read-only mail, sync, accounts, shell and reader | Implemented; historical manual checks/carryovers in report |
| M2 | Mail actions, menus, search, import and native plugin foundation | Implemented with follow-up fixes |
| M3 | Compose and send with native editor | Done; manual/live limitations recorded |
| M4 | Calendar and tasks | Done; Linux deferred |
| M5 | Contacts and Files | Done; explicit feature carryovers remain |
| M6 | Settings, server features and assistant | Integration/close pass active; final tests, UI/live verification, report and fresh Windows portable zip pending |
| M6b | Full plugin platform, sandbox, permissions, consent and Add-ons | Next; source inventory/planning preparation underway |
| M7 | Admin dashboard and management | Planned |
| M8 | Windows desktop integration and distribution | Planned |
| M10 | Parity sweep, UI polish and release readiness | Planned; followed by Astra fixes and a UI walkthrough |
| M9 | Linux | Deferred until after this run |

UI work follows [Bento Balanced, Nordic Focus and Studio](../design/UI-GUIDELINES.md), including modern WinUI 3 materials, depth and responsive motion with platform/accessibility fallbacks. Atlas remains exploratory. No merge to `main`, tags, OS changes or release publication is implied by the roadmap.

## Historical milestone evidence

## M0 progress
- [x] Rust repo frozen: all 44 branches pushed, `rust-final` tagged, renamed `Kaan88/barbican-rust`
  (private). Branch list: [legacy/branches.md](legacy/branches.md).
- [x] Public repo `Kaan88/barbican` with the prototype imported (history kept, scrubbed).
- [x] Foundation: strict props, central packages, analyzers, banned APIs, `Directory.Build.rsp`,
  slot-limited `tools/build.ps1` / `tools/build.sh`, Linux VM runner `tools/linux.ps1`, privacy
  check and pre-push hook, CLAUDE.md, ADRs 0001–0003.
- [x] Legacy docs, design, tools, feel baselines and packaging files copied (scrubbed).
- [x] `tools/Barbican.GenTokens` and the token equality tests.
- [x] Linux VM: .NET SDK set up under the VM user, first green run of `tools/linux.ps1 test`.
- [x] Spec catalogue `spec/` (PORT-PLAN.md §2.3): 353 T-, 729 B-, 232 D-, 471 I-, 77 J-, 110 G-,
  58 L- rows, 200 settings rows, 24 menus, layout metrics; `tools/spec-check.py`. Opus sample review
  done (`docs/m0-checklist.md` §3).
- [x] Spikes, all decided ([adr/README.md](adr/README.md)): S1 Uno single project (ADR 0004), S2 body
  shell (ADR 0005), S3 editor engine (ADR 0006), S4 hidden automation (ADR 0007), S5 secrets (ADR 0008).
- [x] All M0 branches merged into `main`; green on Windows and on the Linux VM (62/62).
- Carried to M1: `design/README.md` and `design/MOTION.md` in XAML terms; archiving the Rust repo
  (end of M1); the Rust repo's local pre-commit guard (needs the user: blocked by the permission
  classifier).

## M1 progress
- [x] Fixed stages F1 (automation foundation, hidden window and smoke, popup guard, shell link routing) and F2 (74 red
  tests, contract skeleton, plugin catalogue); streams jmap, mailcore, accounts, shell-ui and body, each reviewed and merged
  into `m1` ([M1-plan.md](milestones/M1-plan.md)).
- [x] Windows `tools/build.ps1 test` passes on merged `m1` (801 tests, 0 failed, 4 skipped); `spec-check --milestone M1` 0 open (75 ported, T-159
  dropped pending the user's OK); 41/41 M1 hooks have call sites; trace-diff open/switch/push match; cold start 284-353 ms
  to the first frame (Release smoke); portable zip built locally.
- [x] Linux VM: green on merged `m2`, which contains `m1` (1426 tests, 0 failed, 6 skipped; M2 verify).
- [ ] Frame-level feel gates (BLANK/FLASH, input to frame <= 21 ms): not measurable at M1 verify; measured with valid WGC frames at M2 verify
  (M2.md §3). Real-input scenarios still need the user.
- [ ] **HAND TEST 1** and the gate decisions ([milestones/M1.md](milestones/M1.md) §4, §6). After the user's OK: merge `m1` to
  `main`, tag `m1`, archive the Rust repo (needs the user).

## M2 progress
- [x] Stage F (spec tests, contracts) and streams actions, menus, reader, search and unified, each reviewed and merged into `m2`
  ([M2-plan.md](milestones/M2-plan.md)).
- [x] Windows `tools/build.ps1 test` on merged `m2`: 1430 tests, 0 failed, 4 skipped. Linux VM: 1426, 0 failed, 6 skipped; `xrun -Script auto-m2`
  drove menus, a dialog over the body, the palette and a simulated drop.
- [x] `spec-check --milestone M2` 0 open (89 ported, `--strict` 0); 36/36 M2 hooks fire; trace-diff open/switch/push and the new
  delete_undo and move match; cold start 310-322 ms to the first frame; publish carries the bundled spam-score plugin; portable zip built locally.
- [x] Feel: ctx_menu, dialog, palette, delete and undo show 0 BLANK / 0 FLASH in Bento and Nordic, light and dark (valid WGC captures).
- [ ] Interaction rows: 70 M2 rows and 19 carried M1 rows still `todo` (views not built). Decision at the gate ([M2.md](milestones/M2.md) §6).
- [ ] Live writes and the cleanup of the Rust leftovers on the test account: wait for M2-Q2 or HAND TEST 2.
- [ ] **HAND TEST 2** and the gate decisions ([milestones/M2.md](milestones/M2.md) §6, §7), after HAND TEST 1. Then merge to `main` and tag
  (needs the user).

## M1: what it starts with
1. **Uno.Sdk 7.0.x** (ADR 0004 "M0 review"): one SkiaSharp major for the App head and the editor;
   re-run the S1 checks and the S2 harness on both OSes; drop the `UsingUnoSdk` condition in
   `Directory.Packages.props`. Fallback: stay on 6.7.49, vendor RichTextKit for M3.
2. **Spec tests first:** port the 76 M1 T- rows as red xUnit tests (`python tools/spec-check.py
   --milestone M1` lists them), including the CSP builder and the OAuth refresh decision.
3. **Automation foundation:** the AUTOMATION configuration (FlaUI, Win2D capture and the server outside
   Release builds), the fail-closed popup guard, a hidden smoke mode so the published exe can be
   started off-screen.
4. **Linux shell security before the reader ships:** shell-routed links, no `location` writes, CSP tests.
5. Then the plan's M1 streams: JMAP core/session/sync/push (Opus), storage, body view, shell + list +
   reader views, tokens in XAML.

## Changed from the plan
- **No GitHub CI** (ADR 0002): "CI" in the plan means `tools/build.ps1 test` locally plus
  `tools/linux.ps1 test` on the Linux VM. Real-input UI tests (layer C) wait for a throwaway VM.
- **Public repository** with privacy rules (ADR 0002); license AGPL-3.0-only.
- **Packaging** (ADR 0003): only a Windows portable zip until M8; M1's per-push zip artifact is a
  local script output.

## End of project: distribution and enterprise deployment (M8–M10, ADR 0003)
- Windows: per-user setup exe (self-updating), per-machine MSI (notify-only updates, MSI switches),
  portable zip; x64, then arm64.
- Linux: AppImage (self-updating), .deb and .rpm (notify-only).
- Policy templates generated from one definition: ADMX/ADML (Group Policy, Intune), a macOS
  .mobileconfig sample, `/etc/barbican/policies.json` (root-owned, else the app refuses to start).
  Policies: server and allowed JMAP servers, plugins allowlist, offline mail, updates, app lock,
  Files options (Bulwark's set).
- Signed update manifests (Ed25519, the Rust format and key), code signing, a manual-only release
  workflow.
