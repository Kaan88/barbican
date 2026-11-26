# Barbican working plan

The long plan is `ROADMAP.md`. The UI milestones are U0-U12 in `docs/UI-TOOLKIT-EVAL.md`, and
`docs/FEATURE-CUT.md` lists what is cut. This file is the short record for the current push: what is
in flight, what has been decided, and what is still open. Keep it current. A chat history is not the
record.

## How work is run

- Implementers are `deepseek-flash` workers, two at a time. Reviews are done by `cc-reviewer`. A
  milestone is merged only after the review approves it.
- Each worker has its own worktree in `..\barbican-wt\<branch>` and its own
  `CARGO_TARGET_DIR` (`..\barbican-wt\target-<name>`). A shared target slows
  every build down.
- Rules for workers: `cargo test -j 4` only, never `cargo check`; one cargo command at a time; never
  launch the GUI or touch a real profile or the Credential Manager; no live servers; no secrets. Do
  not edit `spec/parity.csv`, `ROADMAP.md`, `build.rs` or the installer. Keep CRLF. Commit with the
  noreply identity and a `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` trailer. Run
  `sh tools/privacy-check.sh HEAD main`; it must say clean.
- After a merge: run the full tests, the privacy check (`sh tools/privacy-check.sh HEAD origin/main`),
  then push main. No tags and no releases without the owner.
- UI screens are reviewed against `docs/mockups/`. The owner approved the mockups and wants the UI to
  match them.

## State (2026-11-26)

main is at 20d1222 (pushed), plus the local PLAN.md commits. R1-R3, the second cut and AI M1-M4 are merged.

| Branch | Commit | What | State |
|---|---|---|---|
| v09-u0-list | b0c6d8c | U0 mail list | merged and pushed (2273570) |
| v09-u0-reader | 4a2a792 | U0 reader | approved in round 3, merged locally (1b4ae3a) |
| v09-u0-views | 73c7afc | U0 calendar, files, contacts | approved in round 5, merged locally (402cf37) |
| v08-ai-m5 | 071886c | AI M5: prompts, context, markdown | approved in round 5, merged locally (ef9eaf5) |

The merged main has not had a full test run: the low-memory guard stopped it twice. It must pass
`cargo test -j 2` and the privacy check before the push. Each branch passed its own tests.

### Decisions made in review
- M5 departs from a strict 1:1 clone, for safety:
  - closing tags in inserted mail text are neutralised;
  - header values are collapsed to one line;
  - only the quote is cut, never the user's own text.
  The spec's 2.0 and 2.13 notes record this.
- The AI login id is a random 128-bit id in `ai-logins.json` beside the settings, keyed by
  `assistant::logins::login_key(base, user)`. It is never stored in the account store: that is one
  Credential Manager blob, capped at about 2.5 KB and written under RefreshLock. M6 must pass
  `login_key(&c.base, &c.username)`. M13 must call `restore_tags` on answers and escape the text.

### Left low (not blocking)
- Reader:
  - when the same message is reopened while its "Load full body" is running, the folder count can lag;
  - the old body is not dimmed on the slow-page path;
  - "Download all" can take the new message's attachments during a cache-hit window. This is
    pre-existing.
- Views: a failed full load that a delta asked for ignores pushes for up to 90 s (`LOAD_STALE`). This
  is pre-existing.
- M5:
  - `ai-logins.json` is never pruned;
  - two instances making an id at the same instant may differ until a restart.

Paused 2026-11-26 by the owner, who also stopped DeepSeek delegation. M6 (v08-ai-m6) and U1 (v09-u1)
were cancelled right after they started; their worktrees hold only whatever edits were uncommitted at
that point. Ask the owner who implements next (the cc-* workers or this session) before resuming.

## Next

1. Test and push main (U0 plus M5).
2. AI M6 onwards, and U1-U3.
3. U1-U3 (wrappers, the gen-viewmodel stand-in behind `ui-mdshared`, bindings in Rust).
4. U4, the first visible UI (read-only). It needs toolkit BT1/BT2/BT3 from the shared-toolkit session.
   On 2026-11-26 the toolkit session reported that P8b (the handover, PR #29) is merged at ff4b484, that
   the toolkit main tip is 09b01c1 (the version to pin), and that BT1 comes next, starting with K5
   (the layout helper). K5 is merged (toolkit PR #34, main at ac4e4e5). K11, K7 and K14 are still to come for BT1.
5. Small R3 fixes, not done yet:
   - the 1.5 s watchdog can be shorter than a slow page;
   - the wrong message's capture can show briefly in redraw_stale;
   - an undimmed old body can show when a parked reader comes back during a fetch.
6. Dependabot alert 1, glib < 0.20 (moderate). glib is pulled in only on Linux, through the GTK side of wry, so it does not affect the Windows build. It clears when wry/tao move to glib 0.20.

## Waiting on the owner

- Whether to start a read-only U4 preview now, using temporary stand-ins behind `ui-mdshared`.
- The form of K9. It must be decided before U6.
- Older open points:
  - guest Free/Busy pills;
  - allow-modals;
  - plugin-data cleanup;
  - the "Sonnet 4.20250514" display quirk;
  - the push-device replacement in the mockups;
  - deleting the `v05-print` branch.

## Not verified

- Nothing in the U0 branches has been run in the GUI. Only the tests have run.
