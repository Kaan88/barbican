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
| v09-u0-list | 96317a7 | U0: mail list work moved off the UI thread, cached previews and avatars | APPROVED; low fixes half done, uncommitted (worker session ses_deae0c70affec0QDHfigs2hO6J, target-list) |
| v09-u0-reader | ce0afab | U0: reader body prepared on the fetch thread, bodycache `prepared` map | CHANGES NEEDED; fixes edited, uncommitted, tests not yet passed (session ses_deaf3775affePa7M2MqsXTBi0N, target-reader) |
| v09-u0-views | 89c989d | U0: calendar expansion on the worker, Files index, contacts/calendar skip reload when current, livesync holds | CHANGES NEEDED (round 1); no fix started |
| v08-ai-m5 | 11c6ccd | AI M5: prompts, context, markdown | waiting for review |

Paused 2026-11-26 by the owner, with the jobs cancelled. On resume, continue the two worker sessions above.

### Fixes still owed

- **list (approved, lows):**
  1. Rows are written by position without checking the id: `refresh_marked_of`, `append_rows_of`. Sign-out does not reset the list or bump its generation.
  2. The avatars `HASHES` cache is never cleared or bounded.
  3. Add a test that pins `mail_row`'s exact field values.
- **reader:**
  1. (medium) prefill no longer clears the old message's reader state, so A's full body, source, banners and unsubscribe can act under B's header.
  2. The settings snapshot is taken at open(); take it when the fetch lands.
  3. The plain-text view of an HTML-only message must come from the raw HTML.
  4. The tests can't fail: the off-thread test, and links_off through the real paths.
  5. Closing the source view after a phishing report restores the body with links_off false.
- **views:** skipping the reload on a cached visit broke what relied on it.
  1. (high) The today glance tile isn't refreshed when the worker result lands.
  2. (high) Turning on the birthday calendar with a cached calendar shows no birthdays.
  3. A birthday added elsewhere never arrives.
  4. A Calendar visit no longer refreshes due iCal subscriptions.
  5. `cache_current` counts a view as current when livesync can't follow it: no states, a failed account read, an errored delta, a refused FileNode push.
  6. `patch_local` bumps GEN without starting a rebuild.
  7. An account switch doesn't `forget_all`; the contacts CLIENT can stay on the other account.
  8. Weak tests.
  9. `summary` still expands a whole year on the UI thread on every render.
- **Workers:** a cargo run in a long foreground tool call drops the worker's connection (ECONNRESET). Tell workers to run cargo in the background to a log file and check it with short calls.

## Next

1. Review, fix and merge U0 list, reader and views. Merging may conflict with the M4 changes on main.
2. Review, fix and merge M5. Then AI M6-M15.
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
