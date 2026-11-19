# Codex takeover and resume guide

Updated 2026-11-19. Read this with [CLAUDE.md](../CLAUDE.md),
[PROGRESS.md](PROGRESS.md), and [the M6 plan](milestones/M6-plan.md).
This is the Codex-specific continuation of the user's Claude handoff.

## Current M6b UI pause (2026-11-20)

The user paused work after reporting that the preview theme does not match
Bulwark. For the current UI checkout, verified evidence, open gaps and resume
order, read the [M6b Bulwark parity handoff](milestones/M6b-parity-handoff.md)
first. The earlier checkpoints and visual direction below are historical;
resume only on explicit user instruction.

## Previous pause and explicit resume (2026-11-19)

The user explicitly resumed M6b after the checkpoint below. See the top of
PROGRESS.md for new work and verification limits. The pre-resume worker checkpoints
are A c3982dc, B f37b0b5 and C 6367077. M6 portable is delivered; M6b and later
milestones, their Astra reviews and the hidden UI walkthrough remain unfinished.

## Continue elsewhere: authoritative checkpoint

This section records the pre-resume checkpoint. The user cleared the app goal
after pausing and then requested the commit/push and handoff. Do not assume an
app goal object exists or recreate one without an explicit request.

Open the **C# Barbican repository**, not the Rust reference checkout. Integration branch is `codex/m6b`, saved coordinator checkpoint `ad7fd97` plus this documentation commit. All three worker heads below were checked against their configured upstreams and all four worktrees were clean before this handoff edit. Worker drafts are deliberately separate from reviewed integration code.

| Owner | Existing branch | Existing worktree under `<downloads>` | Saved head |
|---|---|---|---|
| Coordinator / Sol | `codex/m6b` | `barbican` | `ad7fd97` before this handoff commit |
| A runtime / Terra High | `codex/m6b-runtime` | `wt-m6-a11y-quick` | `c3982dc5de6322b50e22006ba6e830eeba35861c` |
| B packages / Terra High | `codex/m6b-packages` | `wt-m6-set-data` | `f37b0b5ba6ffbb2321df868a154b45474759338e` |
| C mediation / Terra High | `codex/m6b-mediation` | `wt-m6b-protocol` | `63670777f69e6d62df15703a779d4ac8d47bb374` |

D UI/slots remains queued. Reuse these worktrees; inspect status and current refs before assigning ownership. The previous agents stopped and reported no remaining owned app/build process. Those reports are historical, not permission to terminate other processes.

### Evidence and its limits

- M6 is delivered on `m6`: source `583b40d`, branch checkpoint `16cd970`. Portable `artifacts/portable/Barbican-M6-583b40d-win-x64.zip`, 149444021 bytes, SHA-256 `BF4C3DD121545C58A0168EBD0606E01DDA22D2842127BAC1D0A9041C5AFF1DFC`. Extracted Release smoke passed; see `docs/milestones/M6.md` and `spec/feel/M6/windows-release.json`. The ZIP is a local artifact, not a GitHub Release. No tag or main merge is authorized.
- Integration source `5b2f33e` contains reviewed composer GetSnapshot/SetSubject/ReadAttachment adapters, owner/revision/session fences and shared stable-plugin rate limits. Complete Core suite passed **1754/1754**, zero warnings/errors. `aa746ee` adds JavaScript Date TimeClip formatting with 108 native Node vectors. Latest complete ordinary Windows suite was **3088 passed / four skipped at 9647d85**, not at the newer worker drafts. Historical 57 expected RED failures are not the current completion count after composer implementation.
- A: host issuer 8aba99e, bounded parent gate b64d1e0 (42 simulated Node cases), graph Import 8617ff9 (two focused cases), ingress 5192802 (14 fake-native cases), environment guard c3982dc (Windows build zero warnings/errors). Actual SDK bootstrap, native browser lifecycle/transport and environment isolation are unfinished; the guard is not connected or browser-tested.
- B: manager safety 9138c66, cached held lease e5308d0, full-range Date storage eb7342e, authenticated file store ca662d0 (eight temp-folder/fake-secret tests), native inert inspection d8118a6 (10 native, 39 JS inspector and 16 manager focused cases). Three native-loader acceptance tests reached intended PortPending failures at f37b0b5; no assembly executed and positive sentinel is unproven. Store crash recovery and actual OS secret-store/host wiring remain unverified.
- C: reviewed composer-only changes through a0a538c are already integrated; do not accidentally import unfinished families with them. Hook fixes through 110193f had 15 focused cases. Preview/PDF 6367077 had 21 fake-host cases plus two rate cases; actual UI policy enforcement, production registration and source plugin-name fallback remain unfinished. HTML printing intentionally returns Unavailable.
- `ad7fd97` preserves required clsx 2.1.1 attribution with a privacy exemption bound to one exact path and LF-normalized SHA-256. Five isolated scanner tests passed. Never strip required notices or widen the exemption to an email domain or directory. Reference bootstrap staging is outside Git; machine-local `.local/m6b-bootstrap-staging.json` records its location and pinned hashes. This staging is not guaranteed to exist on a different machine.

### First coordinator work on explicit resume

1. **Read and review before merging.** Read the latest PROGRESS table, each worker's FABLE-REVIEW entries, actual diffs and source-derived tests. Sol must directly check subagent code. Keep counts tied to commits; pending tests, mocks and successful compilation do not establish production behavior. Integrate independently verified pieces without importing unrelated stubs. Keep the existing Api/Composer ownership boundary.
2. **Fix WebView environment isolation centrally.** `App/Composition/WebViewEnvironment.cs` sets process-global `WEBVIEW2_USER_DATA_FOLDER`; this overrides explicit environment folders. Inspect `App/MailBody/BodyShell.cs`, `App/Composition/SmokeRun.cs`, `App/App.xaml.cs` and `tests/Barbican.Automation.Tests/WebViewEnvironmentTests.cs`. Coordinate explicit environments for every mail/smoke WebView and each plugin instance; avoid temporarily toggling process-global state. Installed WinUI supports EnsureCoreWebView2Async(environment[, controllerOptions]); WinRT supports CreateWithOptionsAsync and actual UserDataFolder. Fail closed if the returned canonical folder differs before untrusted navigation. Named profiles alone do not establish separate data-folder isolation. No registry changes are needed or authorized.
3. **Fence queued response delivery.** Dispatcher checks before return do not prevent an account switch while native UI posting is queued. Add a host-only immutable captured-session/grant delivery fence to successful outcomes, independent of the closed invocation context; validate at actual UI send. Never serialize that authority object or let adapters mint it. Background runtimes must still follow call-time active accounts; do not permanently pin every instance to one account.
4. **Resolve native launch/revocation coordination before execution.** B's proposed consented loader must reload exact installed verified resources and live grants, then coordinate a host-owned atomic launch barrier with revocation. Consent is explicit full trust; AssemblyLoadContext is not a sandbox. Inert `native-plugin.json` discovery must not load DLLs or invoke module initializers, constructors or Manifest getters. Add real positive sentinel execution only after consent plus mismatch, race and dependency-containment tests using synthetic fixtures.
5. **Review package/store safety.** Preserve invalidation even if durable writes fail, cleanup outside manager gates, generation-bound staged reviews, no silent privileged downgrade, matching held profile-root leases and exact authenticated readback. The file/secret two-resource store deliberately fails closed after interrupted commits and needs a recovery design. Ambiguous storage commits must map to UnknownOutcome rather than invite blind retries.
6. **Complete C and D with actual host adapters.** Preview/print presenters must recheck invocation lifetime and aggregate account policy on the UI thread at issue time. Existing mail sanitizer permits styles/remote images/links and cannot alone secure HTML printing. Finish per-hook projection/selector/result validation and native/JS conversion. Change synchronous slot mutation contracts to accommodate async authority before implementing native mounts. Coordinate shared seams before workers edit them.
7. **Finish the full contract and roadmap.** M6b requires 186 API methods (17 privileged), 218 hooks/27 groups and 58 slots (39 frame/16 action/3 data), with real adapters/mounts rather than catalogue counts or generic Unsupported responses. Then M7 -> M8 -> M10, followed by the hidden UI/usability walkthrough. Each major milestone requires an Astra review/fix cycle until explicit approval. M9/Linux is excluded.

### Compatibility and authority reminders

Structured-clone PluginValue graphs preserve aliases/cycles and binary types; serialize only reachable projections. Conservative default transport limits are not compatibility limits: 25 MiB file payloads and large composer snapshots need coherent operation-specific budgets on both sides. Callbacks are reusable until props replacement, replies one-shot, streams multi-chunk until end. Native bindings, exact installed hash/generation, profile, current grants and host-exposed owner handles provide authority; message tier claims or account/object IDs alone do not.

Keep `http.fetch` (declared-origin HTTPS, no ambient credentials, redirect and actual-connect IP checks) separate from authenticated Bulwark `http.post` sidecar `/api/` operations. Do not expose host credentials or invent Stalwart endpoints. Keystore operations require user presence and never export private keys. Preserve UnknownOutcome after an issued mutation whose result cannot be established.

The user explicitly authorized milestone pushes to **https://github.com/Kaan88/barbican**. Push every commit immediately after privacy checks, using ordinary branch pushes. That does not authorize main merges, tags, GitHub Releases, force-pushes beyond already completed specifically approved sanitation, or OS changes. Use Codex attribution in `docs/FABLE-REVIEW.md`; Astra is the milestone reviewer despite the historical filename.

## Selected UI guidelines (2026-11-19)

The user selected **Bento Balanced**, **Nordic Focus** (integrated sender/context panel,
with an accessible show/hide toggle and expanding reader), and **Studio** (horizontal app navigation
and a warm editorial workspace). Modern WinUI 3 materials, depth and fluid feedback are also requested with
accessibility and platform fallbacks. The user directed all future UI work,
including Claude's, to use them. Read [design/UI-GUIDELINES.md](../design/UI-GUIDELINES.md)
and the updated CLAUDE.md. Additional alternatives by the same Terra agent remain proposals.
The selected images are stored in the repository; do not depend on a machine-local gallery.

## Project and starting state

The active project is the C# Barbican checkout at `<downloads>/barbican`:
WinUI 3 on Windows, Uno/Skia on Linux, JMAP against Stalwart.
`<rust-repo>` is the older Rust app and is a read-only reference. Start the
new Codex chat in the C# project so the sandbox includes that checkout and
obtain access to the existing stream worktrees when needed.

The source session was Claude's **Confirmation to proceed**. Its final
handoff is recorded in PROGRESS.md at `9719835` on `m6`.

- M4 calendar/tasks and M5 contacts/files are complete on their milestone branches.
- M6 settings, server features and assistant implementation is integrated and its Windows portable delivered.
- Current work is M6b on `codex/m6b`; the old M6 stream table below is retained only as historical context.
- Nothing is merged to `main` or tagged. Do not do either without user authorization.
- Treat recorded test counts and cleanup reports as historical evidence. Verify
  current branch heads, uncommitted changes, and relevant processes before acting.

## Agent policy approved by the user

Follow the standing policy in [CLAUDE.md](../CLAUDE.md): Luna for simple work,
Terra for routine implementation, Sol for risky or unusually large implementation,
and Astra for mandatory major milestone review and explicit approval. Run as many
independent agents as practical within current tool and repository limits; queue
remaining streams. Never exceed three concurrent builds. Give each agent a complete
brief with ownership, rules, acceptance criteria and pause state. Hidden in-app
automation remains mandatory for UI work.

## Read before implementation

1. Read AGENTS.md, CLAUDE.md, this guide, and the gitignored `.local/accounts.md`.
   Never read the admin key file named there.
2. Check the Git noreply identity and `.githooks` configuration. Check status,
   upstreams, and branch heads in the main checkout and each stream worktree.
   Preserve any user changes; do not reset, rebase, or force-push them.
3. Read PROGRESS.md, docs/PORT-PLAN.md for the milestone, the M6 plan's ownership,
   contracts, naming/decision prefixes, composition counts and threading rules,
   and the stream's WIP commit. Read its FABLE-REVIEW notes before resuming it.
4. Give each worker its branch/worktree, exact next step, owned files, commands,
   port/profile, cleanup duties, and required final report. Workers never merge
   their own branches into the milestone branch.

## Historical M6 checkpoints (superseded; do not resume these)

| Stream | Branch / worktree | Last recorded commit | Next work |
|---|---|---|---|
| set-data | `m6-set-data` / `wt-m6-set-data` | `c2d9652` | Core does not compile. Add `AssistantJson.Push(JsonArray, JsonNode)`, fix RS0030/MA0051/MA0002 issues, build and run the 18 unproven tests, spec-check, decisions D-M6-A. |
| set-ui | `m6-set-ui` / `wt-m6-set-ui` | `a1e88a6` | Build Automation, recheck the fixed theme-write crash, hidden sample run on port 8921, inspect screenshots, Fable notes, D-M6-B04..B06, full tests. |
| srv-data | `m6-srv-data` / `wt-m6-srv-data` | `b87f023` | Add service tests using FilterTestRig, run the unproven rule/hook tests, complete hooks CSV, D-M6-C06/C07, Fable notes, spec-check, full tests. |
| srv-ui | `m6-srv-ui` / `wt-m6-srv-ui` | `36efd7d` | Finish Automation build, hidden sample run on port 8922, inspect screenshots, decisions/Fable notes, full tests and Windows head build. |

Removing a Red attribute is not proof of a passing test. Run the tests that the
paused streams unmarked without executing. Never claim them green from metadata.

When streams finish, merge into `m6` one at a time and resolve conflicts by
meaning: preserve distinct decision/Fable entries, combine independent
composition-count terms, and retain spec completion only where evidence supports
it. Verify unique decision/type names and generated hook consistency.

The close pass wires real services in place of sample fakes, checks background
thread notifications and UI dispatch, runs the full Windows suite and hidden
sample scenarios, performs the plan's permitted live checks with cleanup,
writes docs/milestones/M6.md and PROGRESS.md, and produces/verifies a portable zip.
Use port 8929 and an isolated automation profile for integration checks.

## Non-negotiable execution rules

- Before every build invocation set `$env:BARBICAN_BUILD_SLOTS = "3"` in that same
  PowerShell command. Build/test only with `tools/build.ps1`; never direct dotnet.
  Run builds in the foreground and shut down your build server when finished.
- Do not touch the user's real input or windows. Use tools/auto-run.ps1 and an
  isolated `auto-<port>` profile. `dev` is the user's profile according to the
  handoff; do not use it for agent checks. Use `agent-ui` only for the explicitly
  permitted live test-account checks. Do not touch the Rust profile.
- Read-only account means no writes, including mark-as-read. Only the test
  account permits writes, with cleanup. Leave no test filter, Sieve script or
  vacation response enabled. Security-setting writes remain FakeJmap-only
  where the M6 plan requires that.
- Never kill an app/build process you did not start. The old M2 process in the
  handoff is historical information, not permission to kill it.
- This repo is public. No real account data, home paths, secrets or real-mail
  screenshots in tracked files. Inspect sample screenshots before committing.
- Run `sh tools/privacy-check.sh --worktree` before every commit. Push every
  commit immediately. Codex commits must not falsely attribute authorship to
  Claude; the Claude-specific coauthor trailer applies to Claude-authored work.
- No OS changes or destructive cleanup without the required user authorization.
  No CI workflows. Preserve project analyzers, test requirements and contracts.

## Process and remaining work

Choose reasonable defaults following Bulwark and record them in spec/decisions.md.
Do not stop at each milestone for hand-test gates or routine questions. Record
human-only checks as unproven. Maintain FABLE-REVIEW notes as work proceeds.

The recorded scope for continuation is **complete the roadmap except M9 (Linux)**: finish
M6 -> M6b -> M7 -> M8 -> M10, then perform a UI walkthrough and fix look, feel and
usability issues. The fresh Windows portable after M6 is already delivered, as
recorded above; the earlier preview is historical. For every major milestone,
record Astra's findings, fixes and explicit approval against the final revision.
The later UI
walkthrough uses hidden native automation and synthetic data under the standing rules.
Windows-only results must be identified as such; Linux is deferred, not passed. On pause, save and push work, stop only your own processes,
clean test-account writes, and update PROGRESS.md with exact commits and next steps.

Known carryovers: crowded calendar event titles clip; scheduled-send banner can
remain after cancellation (S-02); reader can retain the old message after a
folder change (S-03); M3 human/live checks and M5 deferred features remain open.
Use PROGRESS.md and the milestone reports for the full list and evidence.

## Prompt for the new Codex chat

> Continue the C# Barbican project from `codex/m6b`. Read AGENTS.md, CLAUDE.md,
> docs/CODEX-HANDOFF.md (especially "Continue elsewhere"), docs/PROGRESS.md,
> the local account rules, docs/PORT-PLAN.md and the M6b decisions/review notes.
> Resume M6b using the existing runtime, packages and mediation worktrees and
> their saved checkpoints; D UI/slots is queued. Use Luna for simple work, Terra
> for routine implementation and Sol for risky or unusually large implementation.
> Parallelize independent work within the limits; Sol checks all subagent code
> before integration. Respect three concurrent build slots,
> privacy/account rules and hidden-only UI automation. First resolve explicit
> WebView environment isolation, queued-response account delivery fencing and
> the native launch/revocation barrier. Finish M6b -> M7 -> M8 -> M10 (exclude
> M9/Linux), with Astra review and fixes until explicit approval at every major
> milestone. Then fix look, feel and
> usability through a hidden UI walkthrough. Use Bento Balanced, Nordic Focus
> with togglable context panel, and Studio as UI guidelines. Push milestone
> commits to Kaan88/barbican as already authorized. M6 portable is already
> delivered; do not repeat the old M6 pause work or merge/tag/publish a GitHub
> Release without authorization.
