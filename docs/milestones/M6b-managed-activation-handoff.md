# M6b managed add-on activation handoff

**Historical pause record, superseded.** The activation draft described below was integrated and verified at `3791df4`; it is no longer pending. Resume from the [current work list](M6b-plan.md#current-work) and [Claude review packet](../FABLE-REVIEW.md#claude-continuation-review-packet-2026-11-20). The remaining text preserves the earlier pause, not current instructions.

**Original pause on 2026-11-20.** The goal is to finish the milestones except Linux. The remaining sequence is M6b, M7 admin and organization management, M8 Windows integration and distribution, M10 parity and release polish, then the final UI walkthrough. The user asked the agent to coordinate, delegate suitable work to worker agents, and iterate with the reviewer until it explicitly approves. The user authorized pushes to the milestone branch.

## Resume location

- Windows worktree `<downloads>/wt-m6b-bulwark-ui`, branch `codex/m6b-bulwark-ui`. Local and remote were at `0bc5f0c28c2eb5a94e8a02ea4e13b43d91e87c15` when paused. Start with `git status --short --branch` and preserve the local activation changes. `.cortexkit/` was already untracked. `<downloads>` means the Windows user’s Downloads folder.
- The desktop session opens in `webmail-main/desktop`, a different checkout. Run C# work in the milestone worktree. The pinned Bulwark reference is 1.21.2 at `933bc18f517135e38245821c5cc1bc7117ce2d64`.
- Retain the verified runtime assets. Their machine-local staging is `<downloads>/webmail-main/desktop/artifacts/managed-runtime-official-check/source/public/plugin-sandbox-assets`. `tools/plugin-runtime-build/prepare.ps1` reproduces them from the pinned source and full dependency lock. C: had about 15.5 GB free at pause. Check space before builds and remove only verified, generated output if needed.

## Verified and pushed

| Revision | Verified behavior |
|---|---|
| `88a3826` | Windows Settings verifies a managed provider, signer and package and asks for separate consent to share account access and install. Managed packages persist disabled. |
| `4c46940` | The Windows WebView isolates managed JavaScript on `https://managed-plugin.invalid` with the native bridge on `https://plugin.invalid`. A synthetic managed frame completed two hook calls on a private desktop. It could not read the parent document, and a direct message reached zero native dispatcher calls. The ordinary opaque frame also started. A clean runtime preparation verified all eight patched asset hashes. |
| `90c01cf` | Core re-verifies the signed provider response against the complete installed archive and its authenticated generation. The startup proof releases code through a synchronous callback only while that generation, provider and account remain current. Windows Automation passed **3,615 tests** with four skips. Release built without warnings or errors. |
| `0bc5f0c` | Recorded the Windows portable preview and checksum. |

The reviewer approved the three code checkpoints within their documented scopes. [Verification log](../FABLE-REVIEW.md)

The preview is `<downloads>/wt-m6b-portable-4c46940/artifacts/portable/Barbican-4c46940318d3-win-x64.zip`. It is 151,701,278 bytes, with SHA-256 `A910A72B110CFFBB74948FCAB7019B5815031827D5F9DCB680C66B4F312C42F3`. The extracted Release app reported `4c46940318d3f77c38a48c2d1ac106b447cd37f1` and passed the isolated launch check. It contains the verified runtime and its notices. The preview installs managed add-ons disabled. [Portable verification](M6b-solo-parity.md#windows-portable-preview-at-4c46940)

## Uncommitted at pause

These edits have **no recorded build, test, or reviewer approval**. They stay local. The last passing suite applies to the earlier Core proof.

- `src/Barbican.Core/Plugins/Packages/PluginPackageManager.ManagedRuntime.cs` and `Managed/ManagedPluginStartupProof.cs` draft the session grant. The authenticated installation remains disabled on disk. The in-memory grant uses a separately reserved generation and retains its startup proof. The draft includes stop, rollback, and periodic expiry methods. The base manager still needs to call those methods and apply the overlay.
- `src/Barbican.App/Plugins/Sandbox/` has edits to `IPluginSandboxStartup`, `PluginSandboxFactory`, `PluginSandboxSession`, and `PluginSandboxWindowsStartup`. The factory obtains the proof for the authenticated binding. The Windows path waits for the intended frame to be ready, then reads the entrypoint, builds the init message, and posts it inside the synchronous delivery callback. Ordinary startup still uses its existing path.
- `src/Barbican.App/Views/Settings/AddonsPageFactory.cs` and `.Managed.cs` add Enable, Retry and Disable states. The rows depend on the manager projecting a live managed grant through the existing installation API.

## Managed activation implementation details

Use the shared [M6b work list](M6b-plan.md#current-work) for prioritization, UI work and review cadence. The steps below cover the local activation draft.

1. Connect `ProjectManagedRuntime` to `FindAsync` and `ListAsync`. Keep durable installation checks and install-review generation comparisons on the authenticated disk record. Apply `TryUseManagedRuntime` to managed `TryPostAsync`, `TryContinuePost` and multi-binding `TryRunWithGrantsAsync` at the synchronous side effect. Wire the managed stop into disable, replacement and uninstall. Stop the expiry timer on profile shutdown and verify every managed grant is revoked. Treat sign-out, expired provider or JMAP sessions, concurrent retry, uninstallation, and failed cleanup as race cases.
2. Connect `PluginPackageHost` and `PluginBackgroundRuntimes` to fresh verification, session enable and rollback. Avoid holding broad app or manager gates during the provider wait. Ensure Disable cancels a pending start and rollback touches only the generation that failed. Keep the proof’s installed generation distinct from the host-issued runtime generation. Same-provider reapproval currently replaces the route object and invalidates proofs for that route. Make that behavior explicit or reuse an identical, still-authorized route.
3. Verify that the same current-session grant guards actual WebView init and privileged writes. Cover startup failure, package replacement, revoked provider or account, repeated Enable and competing Disable with focused tests. Run the affected tests through `tools/build.ps1`. Use a new private desktop and a synthetic OAuth account/provider to exercise Settings install, Enable, running state and Disable. Repeat ordinary startup as a regression check. Ask for targeted review of changed account or runtime authority. Record the findings and their fixes with the flow.
4. Finish a managed slot walkthrough and a signed, scoped composer-send takeover end to end. Reconcile the remaining M6b API and slot inventory with executable evidence, including dialog and scoped mail-read operations. Run the full Windows Automation suite and Release build, the M6b catalogue and generated hook checks, and the combined portable walkthrough before asking the reviewer to approve the full milestone. Continue with the remaining authorized non-Linux milestones after that gate.

Use the existing hidden test tools and synthetic providers. Keep real accounts and live mail out of the managed activation walkthrough. [Roadmap](../ROADMAP.md) · [M6b plan](M6b-plan.md)
