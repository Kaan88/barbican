# Progress (updated 2026-11-18)

Everything is pushed. Nothing is merged to `main` or tagged.

**Process from 2026-11-18 (the user):** no Fable reviews until one whole-app review at the very end; the user's process rules
(gates, hand-test waits, questions) are dropped, defaults are taken and recorded in spec/decisions.md; Sonnet or Haiku agents
wherever they can. Safety rules stay (no real input, read-only account, admin key, Rust profile, build.ps1, privacy check).

## Milestones
| Milestone | Branch | State |
|---|---|---|
| M2 | `m2` (6012f9f) | Done: hotfix-2 and Fable review 3 fixes merged. Zip `Barbican-6012f9f305d6-win-x64.zip`. |
| M3 | `m3` (2f11adb) | Done: send pipeline, editor core, editor view (Uno 7 / SkiaSharp 4), composer UI, close (quoted HTML sanitised, round trip). 2122 tests green, Linux green. Zip `Barbican-2f11adb86437-win-x64.zip`. Unproven: live round trip on the test account (no agent profile signed in), real IME, mailto from a browser, default-app registration. |
| M4 | `m4-plan` | Planned 2026-11-18: docs/milestones/M4-plan.md (four streams cal-data, cal-edit, cal-views, cal-ui), contracts, FakeJmap calendar arms and 43 red tests pushed; `m4` is created from it. |

Order after M4: M5, M6, M6b, M7, M8, M10, M9 (Linux last), then the whole-app Fable review.

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
