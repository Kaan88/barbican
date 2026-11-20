# M6b plan: full plugin platform

Prepared by Codex / Sol on 2026-11-19. M6 delivered its verified Windows portable. This milestone implements PORT-PLAN section 5 M6b; it does not reduce compatibility to a plugin demo. Follow CLAUDE.md and CODEX-HANDOFF.md. Windows verification is required here; Linux/WebKit verification stays deferred to M9.

## Current work

The Windows milestone is open. Use `codex/m6b-bulwark-ui` in `<downloads>/wt-m6b-bulwark-ui`. Implementation paused on November 20 with local managed-activation edits. This is the shared work list for the next implementation session. Product scope and the adverse cases required at exit appear below.

| Work | Evidence at the pause | Next observable result |
|---|---|---|
| Composer layout · UI owner | Bulwark 1.21.2 puts Discard beside Send on desktop. In the 1280 × 720 native sample at `06ad154`, Discard sits beside Options across the footer from Send. The same placement is present in `ComposerView.xaml` and `ComposerView.xaml.cs` at `90c01cf`. | Fix the Bulwark footer. Show Discard adjacent to Send in a matching wide draft, verify compact sizing and keyboard order, then exercise Cancel in the discard confirmation with the draft intact. Compare light and dark. |
| Mail and other screens · UI owner | Windows evidence covers grouped inbox, expanded cards, inline send, compact navigation and composer dock/maximize/pop-out within their recorded scopes. Calendar editor, Contacts forms and nested Settings controls still have gaps in walkthrough coverage. [UI history](M6b-solo-parity.md#native-interaction-check) · [Walkthrough](M6b-ui-parity.md) | Next, exercise quick reply by pressing Ctrl+Enter in a focused synthetic draft and count submissions (I-220). Then check Calendar’s event editor and Contacts’ New form against the recorded reference entries. Link each to its interaction row, screen or menu and the owning milestone. Reopen a verified flow when a changed dependency, user report or failing comparison gives a reason. |
| Managed add-on in Settings · runtime owner | `88a3826` verified install-disabled. `4c46940` verified isolated Windows transport. `90c01cf` verified the installed-package startup proof (3,615 Windows tests passed, four skipped). The local activation draft has no recorded run. | On a synthetic account, install → Enable → observe the running add-on → Disable. Show that failure or loss of the account grant leaves it inactive. Exercise ordinary startup as a regression check. The local managed-activation handoff identifies the existing edits. |
| Slot, composer and API compatibility · integration owner | Platform reviews record individual operations. The catalogues enumerate 186 API methods, 218 hooks and 58 slots. Their totals describe the target. [API inventory](M6b-api-inventory.md) · [Review log](../FABLE-REVIEW.md) | Use the running managed add-on to show a slot contribution and a signed, scoped composer-send takeover end to end. Run the two unchanged fixtures below. For each remaining family, link the covered method, hook or slot names to executable evidence. Record uncovered names and the owning surface. Pay particular attention to dialogs and scoped mail reads. |
| Combined Windows candidate · coordinator and Astra | The latest verified portable is `4c46940`. The later startup proof and local activation draft are outside that archive. [Portable record](M6b-solo-parity.md#windows-portable-preview-at-4c46940) | Integrate both workstreams, reconcile every due item below, run the complete Windows checks and walkthrough, verify the extracted portable and obtain explicit Astra approval of the final revision. Later-surface obligations retain their named milestone. |

### Working on a flow

Start the UI and managed-activation work independently. Use a separate clean worktree based on the same milestone branch for UI changes when the activation draft occupies the shared checkout. The coordinator owns integration and assigns shared files to one worker. Pair a reviewer early with work that changes access or sending authority. Keep working on independent screens while feedback is pending.

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
