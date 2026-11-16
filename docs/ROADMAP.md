# Roadmap (C# app)

The plan and the full scope of each milestone: [PORT-PLAN.md](PORT-PLAN.md) §5. Decisions that
change the plan: [adr/](adr/). The Rust-era roadmap is in [legacy/ROADMAP.md](legacy/ROADMAP.md).

| Milestone | Scope (short) | Status |
|---|---|---|
| M0 | Freeze the Rust repo, new repo and foundation, spec catalogue, design tokens, spikes S1–S5 | in progress |
| M1 | Read-only mail client on the test account (JMAP core, sync, push, shell, list, reader) | planned |
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
- [ ] Linux VM: SDK setup and first green run (needs the VM key authorized; see FABLE-REVIEW.md).
- [ ] Spec catalogue `spec/` (PORT-PLAN.md §2.3), `tools/spec-check`.
- [ ] Spikes S1 (Uno single project), S2 (body shell), S3 (editor engine), S4 (hidden
  automation), S5 (secrets format).
- [ ] `design/README.md` and `design/MOTION.md` rewritten in XAML terms (rules kept word for word).

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
