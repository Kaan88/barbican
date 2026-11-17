# Roadmap (C# app)

The plan and the full scope of each milestone: [PORT-PLAN.md](PORT-PLAN.md) §5. Decisions that
change the plan: [adr/](adr/). The Rust-era roadmap is in [legacy/ROADMAP.md](legacy/ROADMAP.md).

| Milestone | Scope (short) | Status |
|---|---|---|
| M0 | Freeze the Rust repo, new repo and foundation, spec catalogue, design tokens, spikes S1–S5 | **done** (2026-11-17, [m0-checklist.md](m0-checklist.md)) |
| M1 | Read-only mail client on the test account (JMAP core, sync, push, shell, list, reader) | **next** |
| M2 | Mail actions, menus, reader depth, search, palette, import, plugins | planned |
| M3 | Compose and send with the native editor | planned |
| M4 | Calendar and tasks | planned |
| M5 | Contacts and Files | planned |
| M6 | Settings pages, server settings, assistant | planned |
| M7 | Admin | planned |
| M8 | Windows desktop integration and distribution | planned |
| M9 | Linux | planned |
| M10 | Parity sweep, polish, 1.0 | planned |

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
