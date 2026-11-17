# Progress (paused 2026-11-17 by the user)

Everything is pushed. Nothing is merged to `main` or tagged. Resume from the state below.

## Branches
| Branch | State |
|---|---|
| `main` | M0 only. |
| `m1` | M1 complete + Fable fixes + sign-in hotfix + look port (41ce3cc). HAND TEST 1 done by the user: sign-in and passkeys work; look/feel ported to the prototype (user retest of the look zip pending). |
| `m2` (91a0741) | M2 streams, verify, hotfix-writes (user profiles writable, real drag and drop wiring, `z` undo, D-233 lite dropped), close-a, close-b merged. Build slots default 4 (cf3b268). |
| `fable-review-3` (d458522) | Fable review of m2 at 91a0741, docs only, not merged. Report: `docs/fable-review-3.md`. |
| `m2-fable-fix-3` (e27dc3e) | **WIP, unfinished**: fixes for Fable review 3, stopped mid-way. Not merged. Check the diff, finish, then merge with `fable-review-3`. |
| `m2-hotfix-2` (d94d7de) | **WIP, unfinished**: the user's retest bugs (below), stopped mid-way (drag badge and tag dots in progress). Not merged. |

Worktrees still on disk: `wt-fable-review-3`, `wt-m2-fable-fix-3`, `wt-m2-hotfix-2`.

## Open user reports (highest priority when resuming, m2-hotfix-2)
1. **Data loss**: after Delete and then `z` (undo), a message disappeared: not in Trash or any folder. Root cause not yet confirmed. Until fixed, don't use Delete + `z` on real mail. Tell the user how to look for the message (Bulwark search / All Mail, Stalwart undelete).
2. **Drag and drop**: the cursor stays "forbidden"; folders and tags never accept the drop. The earlier check only exercised `dnd-sim`, which bypasses the real WinUI drag events.
3. **Drag visual**: make it small and semi-transparent, offset from the cursor, with a drop-target highlight, so the target stays visible.
4. **Tags**: tagging works, but rows and the reader header show no tag indicator.
5. After Delete then undo, nothing is selected (follow Bulwark).
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
1. Finish `m2-hotfix-2` (data loss first), then `m2-fable-fix-3`; merge both and `fable-review-3` into `m2`; build a zip for the user.
2. Then run the remaining milestones in this order: M3, M4, M5, M6, M6b, M7, M8, M10, M9 (Linux last).
   - Workflow script: `barbican-port-rest.js` in the session's workflow scripts folder. Its "M2 finish" stage is done; skip it.
