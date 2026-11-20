# M6b plan: full plugin platform

Prepared by Codex / Sol on 2026-11-19. M6 delivered its verified Windows portable. This milestone implements PORT-PLAN section 5 M6b; it does not reduce compatibility to a plugin demo. Follow CLAUDE.md and CODEX-HANDOFF.md. Windows verification is required here; Linux/WebKit verification stays deferred to M9.

## Current work

The Windows milestone is open. Use `codex/m6b-bulwark-ui` in `<downloads>/wt-m6b-bulwark-ui`. The November 20 continuation integrated the local managed-activation draft and exercised the Windows composer, forms, and synthetic add-on lifecycle. Product scope and the adverse cases required at exit appear below.

| Work | Evidence at the pause | Next observable result |
|---|---|---|
| Composer layout · UI owner | Matching 1280 × 720 light and dark drafts show Discard beside Send. The footer traversal works at 1280 × 720 and 844 × 720. Keep editing preserves the draft. Switching themes with an open draft keeps the body legible. The integrated replay passed at `3791df4`. [Comparisons and interactions](M6b-solo-parity.md#bulwark-composer-footer-and-form-walkthrough) | Retain the Bento regression evidence and reopen when a dependency changes. |
| Mail and other screens · UI owner | Focused Ctrl+Enter submitted once. Empty and unfocused presses produced zero additional submissions. Calendar's event editor and Contacts' New form were exercised. The Message list, Reading and Composing Settings walkthrough verified nine persisted preferences. A fresh inbox starts with date grouping off. Reading spacing, contrast and font update the open message. [Composer and forms](M6b-solo-parity.md#bulwark-composer-footer-and-form-walkthrough) · [Nested Settings](M6b-solo-parity.md#nested-settings-and-restart) | Reverify affected flows on the combined revision. The interaction rows track the Calendar date/time picker (M4), combined Contacts New menu (M5) and configurable message hover actions (M6/I-419). |
| Managed add-on in Settings · runtime owner | The private Windows sample installed an add-on disabled and exercised Enable, Running and Disable. A failed startup stayed disabled and offered Retry. Withdrawing the synthetic account stopped a running instance and blocked another Enable. An ordinary add-on reached Running. Revocation after WebView readiness produced zero initialization writes. The integrated replay passed at `3791df4`. [Runtime walkthrough](M6b-solo-parity.md#managed-add-on-activation-and-takeover) | Carry the lifecycle and refusal evidence forward to the next integrated candidate. |
| Slot, composer and API compatibility · integration owner | The signed managed fixture contributed a composer action and produced one classified raw submission. The unchanged API probe exercised composer and reader actions, PDF preview and simulated print. The unchanged proofreading fixture now reaches Running. The composer underlines issues, applies the selected replacement and warns before Send. An opted-out draft produced zero posts, and revoking policy after queueing stopped the next post. [Typing checker](M6b-solo-parity.md#typing-checker-on-windows) · [API evidence and open methods](M6b-api-inventory.md#windows-execution-evidence-for-the-november-20-continuation) | Continue the on-demand checker APIs and remaining proofreading interactions. Resume the native alert, confirm and prompt replay, scoped mail reads and remaining slot mounts. |
| Combined Windows candidate · coordinator and Astra | Astra approved the code and portable checkpoint at `3791df4`. Windows Automation passed 3,623 tests with four skips. Release, catalogue and generated hooks checks passed. The extracted portable reported the same revision and passed smoke. [Portable and candidate record](M6b-solo-parity.md#windows-checkpoint-at-3791df4) | Resolve the remaining M6b compatibility gates, rerun the affected walkthroughs and full Windows checks on the next combined revision, and obtain explicit Astra approval for milestone completion. |

### Continuation state (November 20)

Committed implementation ends at `bff8a91`; the latest recorded Windows Automation suite passed 3,638 tests with four skips. [Claude review packet](../FABLE-REVIEW.md#claude-continuation-review-packet-2026-11-20) covers the full delta from `b459504`. The approved portable remains `3791df4`.

Next: compare the local `artifacts/m6b-paused/dialog-source/` snapshot with HEAD, restore only the intended dialog changes and run the private synthetic `addons-fixture dialog-probe` walkthrough. Its reported isolated build and 11 focused tests do not establish integrated UI acceptance. Preserve the earlier dialog snapshots and unverified `scoped-mail.patch`; they are not committed implementation. Then complete the open compatibility row above and the combined acceptance gate.

### Working on a flow

Managed activation is integrated at `3791df4`; do not restart its superseded draft. Use a separate clean worktree when independent UI work would conflict with an active compatibility change. The coordinator owns integration and assigns shared files to one worker. Pair a reviewer early with work that changes access or sending authority. Keep working on independent screens while feedback is pending.

For the first UI task, use `<bulwark>/screenshots/light-composer-mention.webp`, `components/email/email-composer.tsx` in the pinned upstream, and the synthetic baseline `artifacts/auto/9073/composer-wide.png` in the milestone checkout. The upstream draft is populated and the saved native draft is empty. Match theme, pane widths, labels, account settings and draft state before judging enabled actions. Inspect a settled whole-window capture at 1280 × 720 and the existing narrow case at 844 × 720. Keep background color, spacing, labels, focus and usable hit targets visible. Preserve the saved Bento layout. On machines without the local baseline, use the [hidden sample procedure](M6b-solo-parity.md) to regenerate it.

At integration, update the relevant row with the revision, what the user can do, the comparison or test that proves it and the next remaining discrepancy. Each finding has one owner. Report what became usable, which previously reported defects still reproduce and what is waiting on a dependency. For a visual change, link the before/after pair. If consecutive checkpoints deliver only supporting code, finish the smallest demonstrable flow before splitting off more supporting tasks. Use focused tests and a hidden render or interaction during edits. Run the full Windows Automation suite, Release build, catalogue/generator checks and portable smoke on the combined candidate, or earlier when a shared regression or requested preview warrants it. For Astra re-review, list the previous findings, the fixes and the affected evidence. Recheck the integrated candidate after code changes. Historical approvals remain scoped to their tested code and behavior.

## Current evidence and scope

The current catalogue contains 218 hooks in 27 groups and 58 slots (39 frame, 16 action, three data). Bulwark's sandbox source currently includes 100 non-test TypeScript/TSX files plus seven tests in its sandbox test directory; the older 73-file and 219-hook plan counts are historical. Inventory exact source and API dispatch methods before porting. Eight stale M4 hook rows were reconciled to existing concrete call sites during M6; metadata is not evidence of a plugin receiving all payloads.

Required product behavior:

- Run unchanged Bulwark plugins through the compatible JavaScript activation, hook, API, background and slot protocols. Include theme, localization, UI kit, callbacks, streams and disposal.
- Supply the complete permission-mediated host API over real app services, with explicit capability errors where a connected server lacks a feature. A generic method registry that answers unsupported for otherwise required app APIs is not completion.
- Implement package acquisition/import, inert validation, integrity/signature verification, permissions, update consent, enable/disable/uninstall and Settings Add-ons management.
- Preserve the native spam-score plugin and extend native hook/slot support under explicit full-trust consent. AssemblyLoadContext is unloading/version isolation, not a security boundary.
- Mount all 58 catalogue slot surfaces and validate real action/data contributions. Existing UI and behavior must remain accessible and usable without plugins.

## Security invariants

The reference is Bulwark lib/plugin-sandbox, plugin-types.ts, plugin-loader.ts, plugin-runtime.ts, plugin-hook-catalog.ts and plugin-slot-catalog.ts, together with its bundle fetching/signing and OAuth code. Verify the actual source paths/lines in new module headers.

Untrusted plugin frames have an opaque origin and the reference allow-scripts allow-forms sandbox (D-M6b-01), with form-action none and native navigation/resource enforcement; no allow-same-origin. Bind each frame instance, parent and RPC channel explicitly; authenticate the exact sending frame, reject sibling-frame impersonation, navigation and stale instances, and use bounded structured-clone messages. Do not expose the privileged desktop bridge or mail-view credentials to plugin content. Apply a restrictive CSP and the reference frame/network isolation.

The privileged reference tier has host-origin authority. It requires the reference signature, managed/admin approval and crypto:full conditions plus applicable user consent. Signing does not make code sandboxed. Network allowlists, redirects, local/private targets, byte/time limits, account scope and secret handling require independent enforcement. Keystore access always requires user consent, even for managed packages.

Enforce permissions at registration and on each invocation. Project hook payloads and slot props/fields by executable allowlists; unknown fields are denied. Revalidate grants on updates and revocation. Consent is bound to immutable package identity/hash and requested authority; changed code or expanded permissions cannot inherit stale approval. Native user packages must be discovered from inert metadata and validated before any DLL load, constructor or Manifest getter runs. Keep UserRoot disabled until the full consent path exists.

Bulwark registry/approval/bundle endpoints are web services, not assumed Stalwart JMAP methods. Configure real compatible acquisition/policy sources and report unavailable integrations accurately. Local package import remains a real supported installation path, with its trust tier and consent.

## Foundation design and shared contracts

The foundation design calls for a shared protocol schema, models, interfaces and acceptance tests. Reuse the contracts and tests already on the active branch. The source inventory must enumerate every host API method, hook kind/payload policy, slot and package/consent transition. Do not use descriptive CSV text as an authorization engine.

The shared protocol must include:

- Immutable package identity, content hash, version, provenance/trust tier, requested and granted permissions, enabled/error state and update diff.
- A named per-instance runtime identity bound to the authenticated frame, package/grant and lifecycle cancellation, plus a distinct call-time captured session/account generation. Background frames must remain able to follow active-account changes; local login keys and shared JMAP account IDs are separate namespaces (see M6b-account-routing.md). JSON RPC request/result/error envelopes with correlation, message size and timeout limits, callback/stream ownership and teardown.
- Observer, intercept, transform and takeover registration with stable ordering, per-call permission projection, validated result conversion and reference failure policy. Native abstractions need additive support for transform/takeover; current Observe/Intercept alone is insufficient.
- Slot definitions with executable permission-aware prop/field projection, action/data/frame contribution models, callbacks and safe disposal. Unknown slot/props fail closed.
- Package manager, consent store, runtime, API dispatcher, slot registry and presentation seams; production composition and test fakes. Platform resources/profile/origin adapters must not depend on Presentation from Core.

The coordinator owns shared contracts, generated catalogues, DI composition, integration branch, host surface insertion points, conflict resolution and direct code checks. M6b requires Astra review, fixes and re-review until Astra explicitly approves the final revision.

## Component ownership

| Stream | Model | Ownership |
|---|---|---|
| A runtime | Terra High | App/Plugins/Sandbox, Assets/plugins, dedicated WebView resource/origin/profile adapters and runtime tests. Port actual activation, background/slot iframe lifecycle, CSP and RPC transport. |
| B packages | Terra High | Core/Plugins/Packages, Permissions, Storage and Native; Platform/Plugins; corresponding fixtures/tests. Package safety, signature/tier/grants, scoped storage, native consent before loading, disable/unload. |
| C mediation | Terra High | Core/Plugins/Bridge and Api, exhaustive API adapters and tests; permission projections, typed result conversion, network/OAuth mediation and all four hook kinds. Shared service adapters require explicit coordinator ownership assignment. |
| D UI/slots | Terra High | Presentation/Plugins, App/Views/Plugins, Add-ons/consent/error surfaces, slot mounts and hidden automation. Install/update permission diffs, keyboard/focus, accessible dialogs, themes and all slot kinds. |

Use existing stream worktrees where appropriate, with explicit owned files and ports. Apply the standing delegation policy in CLAUDE.md, queue work beyond available slots, and sequence dependent contracts before implementation. No more than three concurrent builds; every build uses tools/build.ps1 with BARBICAN_BUILD_SLOTS=3 in the same command. Push privacy-checked commits immediately. Do not use the real mouse, keyboard or screen.

## Acceptance and exit evidence

Port spec/behavior tests before implementation. Run unchanged Bulwark integration fixtures plugin-api-probe and proofreading-checker, plus a real slot contribution sample against FakeJmap and the native spam-score regression. Preserve their manifest/index.js; fixture modifications invalidate the unchanged-plugin claim.

Required adverse cases include denied/expanded/revoked consent; undeclared API/hook/slot permissions; tampered package/hash/signature; ZIP traversal, symlinks, duplicate IDs and resource limits; spoofed sibling/stale frames and top-level route escape; direct network and redirect/private-target denial; account/plugin storage isolation; secret non-disclosure; stream/callback caps; all hook kinds, stable order and timeout/failure policy; disable/uninstall teardown of frames, hooks, streams and dialogs; privileged-tier restrictions; keyboard/focus and in-window popup rules.

Execute the full Windows suite on the merged milestone head, catalogue generation/spec gates, and hidden sample automation over actual production seams. Exercise every current-milestone hook and slot, rather than counting catalogue names or registrations. M8/M10 hooks cannot be claimed fired before their surfaces exist; retain explicit later-milestone evidence requirements. Report exact tested commits and unproven human/platform checks in docs/milestones/M6b.md and the Codex/Astra review log. Produce a verified Windows portable build. Do not merge to main, tag or publish a release without the applicable authorization.

Coordinator preparation: [API inventory](M6b-api-inventory.md) and [contract preparation](M6b-contracts.md). These documents do not substitute for shared compiled contracts, red tests or working production adapters.
