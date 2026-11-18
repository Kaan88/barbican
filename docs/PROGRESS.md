# Progress (M6 resumed by Codex, 2026-11-19)

Completed checkpoints are pushed. Active work is recorded below. Nothing is merged to `main` or tagged.

**Codex takeover (2026-11-19):** read [CODEX-HANDOFF.md](CODEX-HANDOFF.md). Use Terra High for stream work and Luna High for narrow helpers; queue streams within the actual session agent limit.

**Process from 2026-11-18 (the user):** no Fable reviews until one whole-app review at the very end; the user's process rules
(gates, hand-test waits, questions) are dropped, defaults are taken and recorded in spec/decisions.md; Terra or Luna agents
wherever they can. Safety rules stay (no real input, read-only account, admin key, Rust profile, build.ps1, privacy check).

## Codex continuation (active, 2026-11-19)

- **User-selected UI guidelines:** Bento Balanced and Nordic Focus; see `design/UI-GUIDELINES.md` and CLAUDE.md. The same Terra agent is creating two further alternatives, which remain proposals. No blanket production theme replacement has been applied.

- Coordinator: Sol, checking subagent code during integration as requested by the user; the later independent whole-app reviewer is Astra. Keep the historical filename `docs/FABLE-REVIEW.md`, mark new entries as Codex, and record exact review scope and evidence.
- The user explicitly authorized pushing milestone commits to `Kaan88/barbican`.
- Set-data checkpoint `d1f137c` merged into `m6` as `08fe6f5`; missing assistant tools are being completed on the stream branch.
- Srv-data calendar lifecycle checkpoint `89ce45b` merged as `fe2118c`; ownership/lifecycle FakeJmap tests passed, with remaining policy/timezone/live limitations in the Astra log.
- Set-ui checkpoint `bd50e41` merged as `1d3b614`; the coordinator fixed the Security-page parenting failure and verified repeated hidden visits. Assistant configuration now uses its real service; the guarded composer adapter is implemented.
- Srv-ui has resumed in its existing worktree on port 8922. Three Terra High workers remain the active limit; narrow Luna High work will use a free slot when available.
- Coordinator integration bridges, guarded assistant composer and background preference dispatch pass the full Windows suite: 2,695 passed, four live/Linux skips, zero failed. Final server UI merge, remaining settings/tool reconciliation, complete hidden checks, live checks/cleanup, M6 report and portable zip are pending. Linux stays deferred to M9.
- Continue in order: M6 -> M6b -> M7 -> M8 -> M10 -> M9 -> Astra whole-app review. The pause table below is historical evidence, not current status.

## Milestones
| Milestone | Branch | State |
|---|---|---|
| M2 | `m2` (6012f9f) | Done: hotfix-2 and Fable review 3 fixes merged. Zip `Barbican-6012f9f305d6-win-x64.zip`. |
| M3 | `m3` (2f11adb) | Done: send pipeline, editor core, editor view (Uno 7 / SkiaSharp 4), composer UI, close (quoted HTML sanitised, round trip). 2122 tests green, Linux green. Zip `Barbican-2f11adb86437-win-x64.zip`. Unproven: live round trip on the test account (no agent profile signed in), real IME, mailto from a browser, default-app registration. |
| M4 | `m4` (see git log) | Done 2026-11-18: calendar and tasks (cal-data, cal-edit, cal-views, cal-ui, UI sweep, integration pass). 2370 tests green on Windows, 0 [Red], spec-check M4 clean. Linux deferred to M9. Report: docs/milestones/M4.md. Zip `Barbican-ea1576de1218-win-x64.zip`. |
| M6 | `m6` | **In progress, paused 2026-11-18** (see the pause table). Planned ([milestones/M6-plan.md](milestones/M6-plan.md)): four streams (set-data, set-ui, srv-data, srv-ui), the settings seam, contracts as stubs, FakeJmap arms, in-memory fakes, 43 red tests (set-data 18, set-ui 3, srv-data 17, srv-ui 5), decisions D-M6-00.. in spec/decisions.md. Integration branch `m6` is created from `m6-plan`. |
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
1. One Terra agent per stream in its worktree, "continue from the last commit" (read the WIP subject, the stream's section of docs/milestones/M6-plan.md and this table); `$env:BARBICAN_BUILD_SLOTS = "3"` for every build.
2. Merge the four into `m6` (append-only conflicts in decisions.md / FABLE-REVIEW.md: keep both; status rows in spec CSVs: keep the side that marks the work done; composition counts are separate terms per stream), full test, then one close-pass agent (wire real services, live checks on the test account via `agent-ui`, M6.md, zip), then M6b.

## Handoff (2026-11-18)
**Where things are.** M4 and M5 are done (`m4` 7d1454e, `m5` 13727b4; zips `Barbican-ea1576de1218-win-x64.zip`, `Barbican-8a629a8e0d74-win-x64.zip`).
M6 is paused mid-way (table above). Nothing is merged to `main` or tagged. Every branch and worktree is clean and pushed (checked 2026-11-18).
`m5` contains the `webview-bg` change (mail-body WebView2 hidden + suspended + low memory in the background, closed after
`webviewBackgroundCloseMinutes`, default 5; measured 305 MB foreground, 270 MB suspended, 0 MB after the close; D-WV-01).

**How the milestones were run (keep doing this).** Plan agent (contracts as stubs, FakeJmap arms, red tests, seam and
composition-count terms in the plan commit, unique type and decision prefixes per stream, threaded in-memory fakes) -> one
Terra agent per stream in `wt-m<n>-<key>` -> coordinator merges into `m<n>` and runs `tools/build.ps1 test` -> one Terra
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
