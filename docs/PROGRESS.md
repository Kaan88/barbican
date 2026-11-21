# Progress

For the active branch, next actions and evidence, use [M6b current work](milestones/M6b-plan.md#current-work). The [roadmap](ROADMAP.md) tracks milestone order.

## November 20: handoff to Claude

- [x] Reconciled the 77 commits since Claude's `b459504` through `bff8a91`: mail/composer parity, production Windows add-on lifecycle and APIs, organization policy, signed managed activation, Settings fixes and typing proofreading. [Review packet](FABLE-REVIEW.md#claude-continuation-review-packet-2026-11-20)
- [x] Recorded the latest full Windows Automation evidence: 3,638 passed, zero failed, four skipped. The last approved combined portable is still `3791df4`; it does not include the later Settings/checker changes.
- [x] Reconciled restart guidance and marked the older managed-activation draft handoff as historical. Paused dialog and scoped-mail work remains local and outside the committed implementation.
- [ ] Finish remaining proofreading APIs, native dialogs, scoped mail reads and production slot evidence, then verify and review the integrated M6b candidate. M6b remains open; M7/M8/M10 follow and Linux/M9 stays deferred.

The records below are historical, not current branch instructions.

## Checkpoint history through November 19

Read the entries below as dated observations. The current work list supersedes their restart prompts.

### First resumed checkpoint

- **Windows x64 portable preview:** `artifacts/portable/Barbican-M6b-preview-36e2d5fc496f-win-x64.zip`
  is available locally, not a GitHub Release. The clean Release publish excluded
  automation components. The extracted app's version matched `36e2d5f` and
  hidden smoke reached shell-ready in 836.3 ms, exit 0. Full ordinary Automation
  suite on source-equivalent `fe147d1`: 3114 passed, zero failed, four skipped;
  both heads compiled without warnings. See [portable evidence](../spec/feel/M6b/windows-preview.json).
  This is a progress preview, not M6b completion or reviewer approval.
- **Explicit Windows mail WebView environment:** integrated at `9166080`
  (`dd0c92c` in the verification worktree). The reader and smoke
  now pass explicit profile-scoped environments, check the actual folder before
  navigation and no longer set process-global WebView2 folder/flags. Four focused
  tests passed; Windows App Automation build passed with zero warnings/errors;
  hidden smoke exited 0 and reached shell-ready in 1365.2 ms with the expected
  isolated profile directory present. Actual plugin factory wiring and
  cross-environment browser isolation remain open.
- **Automation solution build:** `bad3864` copies the fixture from MSBuild's
  resolved project output rather than guessing the configuration directory.
  On the equivalent source in `wt-m6b-webview` (`b2bd1ad`), the Windows Automation
  solution built without warnings or errors and copied the 5632-byte sentinel DLL.
  The standard focused preload filter selected zero tests because the class is Red;
  this verifies fixture packaging, not native activation.
- Read-only runtime and package audits confirmed that neither draft is production
  wiring. Keep pending runtime ingress behind navigation and UI delivery fences.
  Package manager, lease and authenticated store remain a dependent chain without
  production startup or crash recovery. The mediation hook audit identified a
  shared contract blocker: reference transform chains carry intermediate structured
  values and validate at the end, whereas the existing bus requires a CLR type
  after each handler. No implementation landed from that audit. Stream D has
  produced an independently reviewed display-only Add-ons model at `8e066df`
  on `codex/m6b-ui`: 1079 Presentation tests passed, including six new ones.
  The view and authority/consent plumbing remain open; not in the preview.
- **Queued plugin responses:** `86f47f4` on `codex/m6b-delivery` captures
  host-only immutable grant/session checks with a successful dispatcher result.
  `30db863` on `codex/m6b-webview-delivery` runs these checks inside the send
  callback, with a final session check before the synchronous writer. Fake
  native queue tests show account changes withhold result bytes and revoked
  grants write no envelope. At this checkpoint, 16 ingress and 1760 Core tests
  passed and Windows App Automation compiled without warnings or errors.
  Production factory composition and actual WebView delivery remain open.
  The checks are not atomic with concurrent authority changes.
  This work is not integrated or included in the preview.

## Continuation (saved pre-resume checkpoint, 2026-11-19)

- **Resumed integration:** full-range JavaScript Date helper aa746ee passed 108 Node vectors; reviewed composer-only changes through a0a538c are integrated and the complete Core suite passed 1754/1754. Runtime host issuer/parent bridge and package lease/store/storage remain on worker branches for review; no production sandbox/186-method completion is claimed. The latest saved pause table below supersedes earlier checkpoint tables.

At the preceding pause, the user requested progress, commit and push. All stream work was committed/pushed and the three worktrees were clean. Agents reported no remaining builds or app processes they started. No real credential-store/native-browser/printing/OS operation was performed during that implementation interval.

The app goal was subsequently cleared by the user. The cross-chat continuation, prior branch/worktree mapping, source evidence and known races are in the continuation guide at revision `0bc5f0c`. Coordinator checkpoint before that handoff was ad7fd97.

### Latest saved state

Latest user pause saves the resumed interval without integrating worker drafts. Integration source remains **5b2f33e**, with complete Core **1754/1754 passed**, zero warnings/errors. Full-range Date formatting covers 108 JavaScript vectors. The coordinator also saved a verbatim public clsx 2.1.1 license with an exact-path, LF-normalized SHA-256 privacy exception; five isolated scanner regression tests passed (worktree/revision, changed notice, alternate path and unrelated private data). No broad email/domain or license-directory exclusion was added.

| Stream | Pushed checkpoint | Exact pause state and next step |
|---|---|---|
| A runtime (`wt-m6-a11y-quick`, `codex/m6b-runtime`) | `c3982dc` | Clean. Native fake ingress 14/14 passed at 5192802; Windows build zero warnings/errors. New explicit WinRT environment guard is compile-only, disconnected and not browser-tested. Mail still sets a process-global WebView data-folder override, so the new guard deliberately fails closed until coordinated mail/plugin environment wiring. Parent gate, issuer and graph Import remain worker drafts awaiting integration review. Reference SDK bootstrap staging remains outside Git; use the coordinator's hash-bound license solution when resuming. |
| B packages (`wt-m6-set-data`, `codex/m6b-packages`) | `f37b0b5` | Inert native inspection d8118a6: 10 focused native cases, 39 JS inspector cases and 16 manager cases passed, zero warnings/errors. Native activation is test-first WIP: three Red acceptance cases reached the intended PortPending stub; no assembly executed and positive sentinel remains unproven. Resolve atomic launch/revocation barrier before implementation. Authenticated store, cache, lease and storage still require coordinator and production integration review. |
| C mediation (`wt-m6b-protocol`, `codex/m6b-mediation`) | `6367077` | Preview/PDF draft saved for coordinator review: 21 focused fake-host cases plus two rate cases passed; rate RED markers removed after execution. Actual presenter/UI-thread lifetime and aggregate-policy checks, host-owned plugin name, transport budgets and production registration remain unfinished. HTML printing remains Unavailable pending isolated sanitizer/renderer/watermark. Hook fixes through 110193f await integration review. |

Resume priorities: directly inspect current heads before integration; finish actual SDK/bootstrap and isolated native WebView wiring without process-global environment races. Add a host-only captured-account delivery fence so queued UI responses cannot expose a stale account after dispatch. Complete native launch/revocation coordination, package/store crash recovery, all required API/hook/slot surfaces and async-authority-compatible slot mutations. D UI/slots remains queued. M6b, M7, M8 and M10 each require a reviewer review/fix cycle until explicit approval. The hidden UI walkthrough remains required; M9/Linux is excluded. M6 portable is already delivered. No real credential-store, native browser or printing proof is implied by simulated tests.

The integration checkpoint includes reviewed runtime RED baseline f1729fb plus composer/package baselines. Full combined red-check proved **57 expected PortPending failures, zero unexpected passes/wrong reasons** (runtime 26, packages 18, composer 13). Root's complete Core suite passed **1728/1728** on the preceding package/composer baseline. These tests prove intended missing entry points, not completed runtime behavior. The latest fully integrated ordinary Windows suite remains **3088 passed / four skipped** at 9647d85, followed by the passing singleton graph and encoding checks. New implementation drafts listed below are not integrated or claimed verified by those counts.

| Saved pause checkpoint | Worktree / branch / pushed head | Preserved uncommitted work / next step on explicit resume |
|---|---|---|
| A runtime | `wt-m6-a11y-quick`, `codex/m6b-runtime`, `152e664` | `tools/tests/plugin-sandbox-parent.red.test.mjs` readiness/init-issued adjustments, unverified. Review diff, implement real trusted parent bridge, execute source/native tests; real WebView/bootstrap still unfinished. c05a1e9 owned-call registry and 152e664 resource-policy units passed focused tests but are not integrated. |
| B packages | `wt-m6-set-data`, `codex/m6b-packages`, `f28484d` | Four untracked lease files: `Core/Plugins/Storage/IPluginProfileAuthorityLease.cs`, `IPluginProfileAuthorityLeaseFactory.cs`, `PluginProfileAuthorityLeaseFactory.cs`, and `Core.Tests/Plugins/Packages/PluginProfileAuthorityLeaseTests.cs` (under their src/tests project roots). First build had analyzers; rerun interrupted, unverified. Review manager safety corrections through 9138c66 and scoped storage f28484d before integration. Implement/test exclusive host profile lease, authenticated actual store and cached authority; native execution still needs positive consented sentinel proof. |
| C mediation | `wt-m6b-protocol`, `codex/m6b-mediation`, `027a262` | `src/Barbican.Core/Plugins/Hooks/HookBus.cs` interrupted implementation, unverified; inspect diff and run focused hook tests before removing Reds. Corrected composer a0a538c has focused 21+3 passing cases but awaits root integration; hook/value-intercept and preview/print baselines are pending. |

Resume review concerns: preserve reusable callbacks versus one-shot replies/multi-chunk streams; no tier claims from messages; source file/body limits end-to-end; manager invalidation even on persistence failure, no foreign teardown under its gate, prior-generation-bound review, no silent privileged downgrade; authenticated exclusive authority and immediate live checks. Scoped storage still refuses valid extended Date ranges and needs accurate ambiguous-commit error handling; do not call it complete. Reader MailSanitizer alone permits styles/remote images/links and cannot establish network-free HTML printing. HTTP must distinguish declared-origin fetch from authenticated Bulwark sidecar post, verify every redirect and actual connect IP, and expose no ambient credentials. Slot mutations still need an async authority-compatible contract before their implementation. These are remaining work, not permission questions.


- **M6b coordinator revocation foundation:** ten focused Core tests passed with no build warnings/errors. Revocation invalidates all affected bindings before cleanup, shares in-flight work, retains failures for retry and tolerates cancelled callers. Production grant/package/runtime wiring remains pending.

- **M6b production test-first phase (base 9647d85):** stream A `codex/m6b-runtime` in `wt-m6-a11y-quick`, B `codex/m6b-packages` in `wt-m6-set-data`, C `codex/m6b-mediation` in `wt-m6b-protocol`. C composer baseline68194dc is integrated and full red-check proves 13 expected pending failures/zero wrong reasons; implementation of those three adapters is released after direct review. Remaining C families/hooks and A/B checkpoints still follow source tests before implementation. Native metadata/staged-review/atomic-session defaults are D-M6b-02..04. D UI/slots is queued; all later milestone/review/UI requirements remain.

- **Active milestone branch `codex/m6b`:** reviewed/merged structured clone values and browser counterpart with bidirectional golden vectors, generation revocation, binary service seams, live dispatcher fences, atomic production account-session snapshots, all 29 directional schemas, and inert JS ZIP inspection. Latest full Windows suite passed 3088 / zero failures / four skips (3092 total), zero warnings/errors; Node parity/adversarial checks passed. Actual WebView authentication/lifecycle, package consent/persistence, complete API/hook/slot adapters and native mounts remain unfinished. M6 remains stable on `m6` with its verified portable.

- **M6 Windows checkpoint delivered at `583b40d`:** full Automation -Writes suite 2,909 passed / zero failed / four skipped; both heads compiled without warnings. Fresh portable `Barbican-M6-583b40d-win-x64.zip` (149,444,021 bytes) was extracted and passed Release smoke with matching embedded source version. See [M6 report](milestones/M6.md) and `spec/feel/M6/windows-release.json` for checksum, coverage and unverified live/visual checks. Continue to M6b; the broader goal remains active.

- **Earlier integration checkpoint `30f315b`:** archive action/anchors, live verification-code controls, tag row tint, bounded attachment activation, account/stale-result guards, accessibility quick panel/fonts, toolbar labels and all three Compose placements are integrated. Preferences flush before Windows close; three immediate-close restart cycles passed. Windows Automation passed 2,889 of 2,893 tests with four skips and zero failures; the spec gate reported 18/18, no duplicate or dangling catalogue IDs, current hook generation and zero Red markers. The later recovery, harness and final suite are recorded above. The coordinator reviewed the worker code and requested targeted fixes during integration.
- **Preview delivered at the user’s request:** Windows portable `Barbican-M6-preview-aa0e96b-win-x64.zip` is ready (142.4 MiB). Full combined suite passed 2,833 with four skips; extracted Release smoke passed. See [preview evidence](milestones/M6-preview.md) for the exact checkpoint/hash. The restored preview archive has new compression metadata and the hash in the preview evidence identifies it. Time-zone consumers, vacation HTML/state/account guards and Sieve version/acknowledgement guards are implemented after that preview. Live verification remains explicitly incomplete in the M6 report; the fresh verified M6 portable above is now delivered.
- **New close-pass evidence:** 92 nonuniform Settings captures across both existing styles and light/dark were visually inspected. Nested-tag create/rename/confirmed-delete restored the original isolated profile value and verified it on disk; real-service Backup export/import/confirmed-reset restored its original snapshot. No deliberate Sieve/vacation write verification was performed; earlier writable vacation loads were not proven mutation-free. WGC/private-desktop capture remains unproven; XAML captures are visual evidence.

- **User-selected UI guidelines:** Bento Balanced, Nordic Focus and Studio; see `design/UI-GUIDELINES.md` and CLAUDE.md. Nordic Focus now means the integrated sender/context layout with a toggleable panel; the name remains Nordic Focus. The user corrected the third choice to Studio, with horizontal app navigation and a warm editorial workspace; Atlas remains exploratory. Modern WinUI 3 materials, subtle elevation and fluid feedback are requested with accessible platform fallbacks. No blanket production theme replacement has been applied.

- The coordinator checks subagent code during integration, as requested by the user; an independent whole-app review follows. Record exact review scope and evidence in `docs/FABLE-REVIEW.md`.
- The user explicitly authorized pushing milestone commits to `Kaan88/barbican`.
- Set-data checkpoints through `a921d70` are merged; assistant tools, fake-only account writes, offline provider formats and history isolation have executed coverage. Shared mutation locking and async assistant configuration are merged as `38ddd24`; async history and repaired typed schema choices are merged as `b5ba4b7`.
- Srv-data calendar lifecycle checkpoint `89ce45b` merged as `fe2118c`; ownership/lifecycle FakeJmap tests passed, with remaining policy/timezone/live limitations in the review log.
- Set-ui checkpoint `bd50e41` merged as `1d3b614`; the coordinator fixed the Security-page parenting failure and verified repeated hidden visits. Assistant configuration now uses its real service; the guarded composer adapter is implemented.
- Srv-ui checkpoint `1332306` merged as `08331b8`. The same worker is completing UI-keyless controls, catalogue reconciliation, sample real-service wiring and the close pass on port 8929. Narrow worker catalogue triage is complete.
- Historical combined Windows run at `08331b8`: 2,709 passed, four live/Linux skips, zero failed. Subsequent assistant/account/provider checks are merged, including the coordinator calendar-rights regression (nine focused assistant cases passed). Remaining settings reconciliation/locking, complete hidden checks, live checks/cleanup, M6 report and portable zip are pending. Linux stays deferred to M9.
- Active user goal: implement everything in the roadmap except Linux M9: finish M6 -> M6b -> M7 -> M8 -> M10, then work with the reviewer to fix issues until the reviewer is satisfied, then perform a UI walkthrough and fix look, feel and usability issues. This replaces the earlier stop-after-M10 scope; all privacy, build, account and publication rules remain in force. After M6 passes its final integration checks, produce and verify a fresh Windows portable zip as explicitly requested. The pause table below is historical evidence, not current status.

## Milestones
| Milestone | Branch | State |
|---|---|---|
| M2 | `m2` (6012f9f) | Done: hotfix-2 and Fable review 3 fixes merged. Zip `Barbican-6012f9f305d6-win-x64.zip`. |
| M3 | `m3` (2f11adb) | Done: send pipeline, editor core, editor view (Uno 7 / SkiaSharp 4), composer UI, close (quoted HTML sanitised, round trip). 2122 tests green, Linux green. Zip `Barbican-2f11adb86437-win-x64.zip`. Unproven: live round trip on the test account (no agent profile signed in), real IME, mailto from a browser, default-app registration. |
| M4 | `m4` (see git log) | Done 2026-11-18: calendar and tasks (cal-data, cal-edit, cal-views, cal-ui, UI sweep, integration pass). 2370 tests green on Windows, 0 [Red], spec-check M4 clean. Linux deferred to M9. Report: docs/milestones/M4.md. Zip `Barbican-ea1576de1218-win-x64.zip`. |
| M6 | `m6` | **Windows implementation checkpoint delivered, 2026-11-19.** Source 583b40d; 2,909 tests passed / four skipped; fresh portable extracted and smoke-tested. Live and visual limitations remain explicit in M6.md. Continue to M6b; the old pause table is historical. |
| M5 | `m5` (8a629a8) | Done 2026-11-18: contacts and files (con-data, con-ui, files-data, files-ui, webview-bg, integration pass with live checks on the test account). 2618 tests green on Windows, 0 [Red], spec-check M5 clean. Linux deferred to M9. Report: docs/milestones/M5.md. Zip `Barbican-8a629a8e0d74-win-x64.zip`. |

## Pause table (M6, 2026-11-18)
Every stream ends with a "WIP (paused)" commit, pushed. Nothing of M6 is merged into `m6` yet (`m6` = the plan, c31f1df).
No live writes were left on the test account. No app or build process of the streams is left running.

| Branch (worktree `wt-m6-<key>`) | Last commit | State and next step |
|---|---|---|
| `m6-set-data` | c2d9652 | Code, FakeJmap arms, hooks.csv and composer call sites written; **Core does not compile yet**; [Red] removed from its 18 tests, never run. Next: add `AssistantJson.Push(JsonArray, JsonNode)`, fix analyzer errors (HttpClient RS0030 pragma, MA0051 splits, MA0002), build, test, `spec-check --write`, decisions D-M6-A.. |
| `m6-set-ui` | a1e88a6 | Screen, generic row renderer, account/security/assistant/backup pages, assistant column, commands, set-* automation; its 3 red green; Presentation tests and `build -Head win` passed. A hidden run crashed on the theme write (fixed in SetUiPrefsValues, not re-run). Next: Automation build, hidden run (port 8921), screenshots, Fable notes, D-M6-B04..B06, full test. |
| `m6-srv-data` | b87f023 | Pure rules, four services, hook call sites, FakeJmap arms; 11 FilterRulesTests green, the other 5 rule tests, hook test and service rig compile but never ran; [Red] removed from all 17. Next: service tests on `FilterTestRig`, hooks.csv call-site rows, D-M6-C06/C07, Fable notes, spec-check, full test. |
| `m6-srv-ui` | 36efd7d | Rules, view models, six pages and 3 dialogs registered through ISettingsPageFactory, sample-fakes swap, srv-open/srv-dump automation; its 5 red green, Presentation tests 971 pass. Automation build never confirmed (stopped by the pause). Next: Automation build, hidden sample run (port 8922), screenshots, decisions, Fable notes, full test and `-Head win`. |

## How to resume
1. One Sonnet agent per stream in its worktree, "continue from the last commit" (read the WIP subject, the stream's section of docs/milestones/M6-plan.md and this table); `$env:BARBICAN_BUILD_SLOTS = "3"` for every build.
2. Merge the four into `m6` (append-only conflicts in decisions.md / FABLE-REVIEW.md: keep both; status rows in spec CSVs: keep the side that marks the work done; composition counts are separate terms per stream), full test, then one close-pass agent (wire real services, live checks on the test account via `agent-ui`, M6.md, zip), then M6b.

## Handoff (2026-11-18)
**Where things are.** M4 and M5 are done (`m4` 7d1454e, `m5` 13727b4; zips `Barbican-ea1576de1218-win-x64.zip`, `Barbican-8a629a8e0d74-win-x64.zip`).
M6 is paused mid-way (table above). Nothing is merged to `main` or tagged. Every branch and worktree is clean and pushed (checked 2026-11-18).
`m5` contains the `webview-bg` change (mail-body WebView2 hidden + suspended + low memory in the background, closed after
`webviewBackgroundCloseMinutes`, default 5; measured 305 MB foreground, 270 MB suspended, 0 MB after the close; D-WV-01).

**How the milestones were run (keep doing this).** Plan agent (contracts as stubs, FakeJmap arms, red tests, seam and
composition-count terms in the plan commit, unique type and decision prefixes per stream, threaded in-memory fakes) -> one
Sonnet agent per stream in `wt-m<n>-<key>` -> coordinator merges into `m<n>` and runs `tools/build.ps1 test` -> one Sonnet
close-pass agent (wire real services, hidden sample run, live checks on the test account via profile `agent-ui`, report, zip).
Every agent: `$env:BARBICAN_BUILD_SLOTS = "3"`; at most 5 agents alive.

**Needs the user.**
- Hand check of the WebView background release with a real minimise (agents can only simulate it).
- Hand test of the M4/M5 zips: real pointer drag and drop, OS file drop into Files, Ctrl/Shift selection, contact print to a printer.
- `CLAUDE.md` line 31 still calls `dev` the agents' profile (M2 report 6): waiting for the user's OK to edit.
- Worktrees to prune (merged, clean; `git worktree remove` needs the user's permission): `wt-m5-con-data`, `wt-m5-con-ui`,
  `wt-m5-files-data`, `wt-m5-files-ui`, `wt-m5-plan` (detached at 8be3a6a), `wt-webview-bg`; older: `wt-fable-review-3`,
  `wt-m2-fable-fix-3`, `wt-m2-hotfix-2`, `wt-m2-close-a` (if still on disk).
- A `Barbican.exe` from `wt-m2-close-a` (Automation build, started 2026-11-17 17:08) was still running at the pause; no agent of
  this session started it, so it was left alone.

**Known open defects.** Crowded week-view lanes still clip titles and times ("Focu..."); UI sweep S-02 (scheduled banner stays after
Cancel send) and S-03 (reader keeps the old message after a folder switch by click); M3 unproven items (live round trip, real IME,
mailto from a browser, default-app registration); the test account still holds the Rust-era folders `p2prod A` / `p2prod B`.

Open from M5 (docs/milestones/M5.md "What is unproven" and "Not done"): contact activity tiles, blob photos in the contact list, marquee selection in Files, the account scope picker (I-066), an in-app PDF preview, a real-pointer drag test, Linux (M9). Seen in the M5 live run: the test account still holds the two Rust-era folders `p2prod A` and `p2prod B` and the old calendar and mail leftovers of `.local/accounts.md`.

Order after M5: M6, M6b, M7, M8, M10, M9 (Linux last), then the whole-app Fable review.

## History before 2026-11-18

### Branches
| Branch | State |
|---|---|
| `main` | M0 only. |
| `m1` | M1 complete + Fable fixes + sign-in hotfix + look port (41ce3cc). HAND TEST 1 done by the user: sign-in and passkeys work; look/feel ported to the prototype (user retest of the look zip pending). |
| `m2` (7b61525) | **m2-hotfix-2 merged 2026-11-18.** Before that: | M2 streams, verify, hotfix-writes (user profiles writable, real drag and drop wiring, `z` undo, D-233 lite dropped), close-a, close-b merged. Build slots default 4 (cf3b268). |
| `fable-review-3` (d458522) | Fable review of m2 at 91a0741, docs only, not merged. Report: `docs/fable-review-3.md`. |
| `m2-fable-fix-3` | In progress (2026-11-18): R3-01, R3-02, R3-04, R3-05 (WIP finished on top of e27dc3e, m2 merged in). Not merged. |
| `m2-fable-fix-3b` | In progress (2026-11-18): R3-03, R3-07, R3-09, branched from m2 at 7b61525. Not merged. |
| `m2-hotfix-2` (4cb19d4) | Done and merged into m2: reports 1-5 below. |

Worktrees still on disk: `wt-fable-review-3`, `wt-m2-fable-fix-3`, `wt-m2-hotfix-2`.

## User reports from the M2 retest (m2-hotfix-2, done 2026-11-18 except 6; hand test pending)
1. **Data loss** (fixed, 1cd625c): a stale row's mailboxIds made a later Delete a destroy. Permanent delete is now decided by the folder on screen and always asks.
2. (fixed, 516ef1c) **Drag and drop**: the cursor stays "forbidden"; folders and tags never accept the drop. The earlier check only exercised `dnd-sim`, which bypasses the real WinUI drag events.
3. (fixed, 516ef1c) **Drag visual**: make it small and semi-transparent, offset from the cursor, with a drop-target highlight, so the target stays visible.
4. (fixed, 4cb19d4) **Tags**: tagging works, but rows and the reader header show no tag indicator.
5. (fixed, 1cd625c) After Delete then undo, nothing is selected (follow Bulwark).
6. `CLAUDE.md` line 31 still calls "dev" the agents' profile; it is the user's profile now (agents use `auto-*`, `smoke`, `xrun-*`). Waiting for the user's OK to edit.

## User decisions in force
- **D-1**: history rewritten (done).
- **D-2**: no OAuth import.
- **D-7**: destructive assistant actions always ask.
- **D-233**: lite mode dropped, no replacement.
- **Defaults kept**: D-3, D-9, D-10; M2-Q2 agent writes OFF; M2-Q1 SQLite cache in M3; Restore / Move to Inbox rows added; select-all states the 10 000 cap; user plugins off until M6b; undo leaves read state (Bulwark).
- **Speed**:
  - No Linux testing until the end (M9 last).
  - Fewer reviews: separate reviewer only for hard streams; Fable after M2, M4, M6b, M8, M9.
  - Don't wait for hand tests; the next milestone starts right away.
  - Limits: 4 build slots, 5 compiling agents, 4 streams and 2 Opus per wave; Sonnet wherever applicable.
- **Plugins**: Bulwark-compatible JS host + native .NET plugins (M6b); spam score as a native plugin.

## How to resume
1. Finish `m2-fable-fix-3` and `m2-fable-fix-3b`; merge both and `fable-review-3` into `m2`; build a zip for the user. Open user decisions from the review: E-9, E-11, E-12 (docs/fable-review-3.md §3).
2. Then run the remaining milestones in this order: M3, M4, M5, M6, M6b, M7, M8, M10, M9 (Linux last).
   - Workflow script: `barbican-port-rest.js` in the session's workflow scripts folder. Its "M2 finish" stage is done; skip it.
