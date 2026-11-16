# ADR 0003: Packaging: a Windows portable zip now, Bulwark's desktop set at the end

- Status: accepted (2026-11-17)
- Deciders: the user (owner), recorded by the M0 bootstrap agent
- Supersedes: the packaging defaults in PORT-PLAN.md §3.10 and Q3 where they differ

## Context
The plan's default was a Velopack per-user `Setup.exe` plus a portable zip on Windows, AppImage and
.deb on Linux, and no MSI unless managed deployment was needed. The user wants, for now, only a
Windows portable zip, and at the end of the project the same kinds of packages and enterprise
deployment material that Bulwark's desktop app ships.

What Bulwark's desktop app ships (Bulwark 1.21.2: CHANGELOG, `desktop-policies/`,
`scripts/generate-desktop-policies.mjs`):
- Windows: an installer (setup exe) and an **MSI** for per-machine installs, x64 and arm64, signed.
  The MSI install is the one to use where standard users must not write to the app folder.
- Linux: **AppImage**, **.deb** and **.rpm**, x64 and arm64.
- macOS: DMG, signed and notarized (out of scope for Barbican for now, see PORT-PLAN.md non-goals).
- Updates: the installer, macOS and AppImage builds update themselves (saving drafts first); MSI,
  deb and rpm installs only show that an update is out (the package manager owns the files).
- Enterprise policy: **ADMX/ADML** templates (en-US, de-DE) for Group Policy and Intune, MSI
  switches, a sample **macOS .mobileconfig**, and **`/etc/<app>/policies.json`** on Linux (root-owned,
  mode 644, otherwise the app refuses to start). Policies can set or force the server, allowed JMAP
  servers, plugins (allowlist), offline mail, updates, the app lock and Files options. All
  generated from one source by a script.

## Decision
1. **Now (M0 to M7):** the only package is a **Windows portable zip** (self-contained, unpackaged
   WinUI 3 app). It is built locally by a script (added with the App head, M1), never by CI.
2. **At the end (M8 to M10)**, Barbican ships the Bulwark-like set:
   - Windows: a per-user **setup exe** (self-updating) and a per-machine **MSI** (notify-only
     updates, admin-deployable with MSI switches), plus the portable zip; x64 first, arm64 if the
     dependencies allow.
   - Linux: **AppImage** (self-updating), **.deb** and **.rpm** (notify-only; the `install-kind`
     marker files in `packaging/` already encode this rule from the Rust app).
   - Enterprise policy: **ADMX/ADML** templates, a **macOS .mobileconfig sample** (for parity of the
     policy set even though there is no macOS build yet), and **`/etc/barbican/policies.json`**,
     all generated from one policy definition, with the same root-owned-file rule on Linux.
   - Signed update manifests keep the Rust app's Ed25519 format and key (PORT-PLAN.md §3.10).
   - Code signing per PORT-PLAN.md Q5.
3. **No installers are built before M8.** The WiX source of the Rust app (`packaging/wix/main.wxs`)
   and its Linux packaging files are kept as reference.
4. A GitHub release workflow may be drafted at the very end as **manual-only** (ADR 0002).

## Consequences
- M1's "portable zip build artifact on each main push" (PORT-PLAN.md §5) becomes a local script
  output, not a CI artifact.
- Velopack remains the likely tool for the self-updating setup exe and AppImage; WiX for the MSI;
  the choice is made in M8.
- The policy feature itself (reading and enforcing policies in the app) is planned work for M8/M10
  and is listed in docs/ROADMAP.md.
