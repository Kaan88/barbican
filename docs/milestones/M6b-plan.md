# M6b plan: full plugin platform

Prepared by Codex / Sol on 2026-11-19. Implementation starts after M6 closes and its fresh Windows portable zip is verified. This milestone implements PORT-PLAN section 5 M6b; it does not reduce compatibility to a plugin demo. Follow CLAUDE.md and CODEX-HANDOFF.md. Windows verification is required here; Linux/WebKit verification stays deferred to M9. The active run stops after M10, before M9 and the final Astra review.

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

Untrusted plugin frames have an opaque origin and allow-scripts sandbox only. Bind each frame instance, parent and RPC channel explicitly; authenticate the exact sending frame, reject sibling-frame impersonation, navigation and stale instances, and use bounded structured-clone messages. Do not expose the privileged desktop bridge or mail-view credentials to plugin content. Apply a restrictive CSP and the reference frame/network isolation.

The privileged reference tier has host-origin authority. It requires the reference signature, managed/admin approval and crypto:full conditions plus applicable user consent. Signing does not make code sandboxed. Network allowlists, redirects, local/private targets, byte/time limits, account scope and secret handling require independent enforcement. Keystore access always requires user consent, even for managed packages.

Enforce permissions at registration and on each invocation. Project hook payloads and slot props/fields by executable allowlists; unknown fields are denied. Revalidate grants on updates and revocation. Consent is bound to immutable package identity/hash and requested authority; changed code or expanded permissions cannot inherit stale approval. Native user packages must be discovered from inert metadata and validated before any DLL load, constructor or Manifest getter runs. Keep UserRoot disabled until the full consent path exists.

Bulwark registry/approval/bundle endpoints are web services, not assumed Stalwart JMAP methods. Configure real compatible acquisition/policy sources and report unavailable integrations accurately. Local package import remains a real supported installation path, with its trust tier and consent.

## Coordinator foundation and frozen contracts

Before independent implementation, the coordinator records the protocol schema and adds shared models/interfaces plus acceptance tests. The source inventory must enumerate every host API method, hook kind/payload policy, slot and package/consent transition. Do not use descriptive CSV text as an authorization engine.

The shared protocol must include:

- Immutable package identity, content hash, version, provenance/trust tier, requested and granted permissions, enabled/error state and update diff.
- A named per-instance runtime identity bound to the authenticated frame, account context and lifecycle cancellation. JSON RPC request/result/error envelopes with correlation, message size and timeout limits, callback/stream ownership and teardown.
- Observer, intercept, transform and takeover registration with stable ordering, per-call permission projection, validated result conversion and reference failure policy. Native abstractions need additive support for transform/takeover; current Observe/Intercept alone is insufficient.
- Slot definitions with executable permission-aware prop/field projection, action/data/frame contribution models, callbacks and safe disposal. Unknown slot/props fail closed.
- Package manager, consent store, runtime, API dispatcher, slot registry and presentation seams; production composition and test fakes. Platform resources/profile/origin adapters must not depend on Presentation from Core.

The coordinator owns shared contracts, generated catalogues, DI composition, integration branch, host surface insertion points, conflict resolution and direct code checks. Independent reviewer agents are deferred to the later Astra whole-app review.

## Implementation streams

| Stream | Model | Ownership |
|---|---|---|
| A runtime | Terra High | App/Plugins/Sandbox, Assets/plugins, dedicated WebView resource/origin/profile adapters and runtime tests. Port actual activation, background/slot iframe lifecycle, CSP and RPC transport. |
| B packages | Terra High | Core/Plugins/Packages, Permissions, Storage and Native; Platform/Plugins; corresponding fixtures/tests. Package safety, signature/tier/grants, scoped storage, native consent before loading, disable/unload. |
| C mediation | Terra High | Core/Plugins/Bridge and Api, exhaustive API adapters and tests; permission projections, typed result conversion, network/OAuth mediation and all four hook kinds. Shared service adapters require explicit coordinator ownership assignment. |
| D UI/slots | Terra High | Presentation/Plugins, App/Views/Plugins, Add-ons/consent/error surfaces, slot mounts and hidden automation. Install/update permission diffs, keyboard/focus, accessible dialogs, themes and all slot kinds. |

Use existing stream worktrees where appropriate, with explicit owned files and ports. With four total agent slots, run at most three workers and queue D until a slot opens; sequence dependent contracts before implementation. Luna High may assist only with bounded catalogue/mechanical tasks when actually available. No more than three concurrent builds; every build uses tools/build.ps1 with BARBICAN_BUILD_SLOTS=3 in the same command. Push privacy-checked commits immediately. Do not use the real mouse, keyboard or screen.

## Acceptance and exit evidence

Port spec/behavior tests before implementation. Run unchanged Bulwark integration fixtures plugin-api-probe and proofreading-checker, plus a real slot contribution sample against FakeJmap and the native spam-score regression. Preserve their manifest/index.js; fixture modifications invalidate the unchanged-plugin claim.

Required adverse cases include denied/expanded/revoked consent; undeclared API/hook/slot permissions; tampered package/hash/signature; ZIP traversal, symlinks, duplicate IDs and resource limits; spoofed sibling/stale frames and top-level route escape; direct network and redirect/private-target denial; account/plugin storage isolation; secret non-disclosure; stream/callback caps; all hook kinds, stable order and timeout/failure policy; disable/uninstall teardown of frames, hooks, streams and dialogs; privileged-tier restrictions; keyboard/focus and in-window popup rules.

Execute the full Windows suite on the merged milestone head, catalogue generation/spec gates, and hidden sample automation over actual production seams. Exercise every current-milestone hook and slot, rather than counting catalogue names or registrations. M8/M10 hooks cannot be claimed fired before their surfaces exist; retain explicit later-milestone evidence requirements. Report exact tested commits and unproven human/platform checks in docs/milestones/M6b.md and the Codex/Astra review log. Produce a verified Windows portable build. Do not merge to main, tag or publish a release without the applicable authorization.
