# Branches of the Rust repository (frozen 2026-11-17)

The Rust/Slint app lives on in the private repository `Kaan88/barbican-rust` as a read-only
reference. At the freeze (PORT-PLAN.md §2.6) every local branch was pushed (44 of 44 on origin) and
`main` got the annotated tag **`rust-final`** (commit `bba9613`).

Purpose is taken from the commit messages (tip subject, plus the work stream the name stands for).
"merged" means every commit of the branch is in `main`; only the three `mat-*` branches hold work
that is not in `main`. All branch tips are dated 2026-11-16 or 2026-11-17.

## Branches with work outside `main`

| Branch | Tip | What it holds |
|---|---|---|
| `mat-overlay` | `96747db` | WIP of the "maturity" pass, stopped when Slint was retired: overlay handling in `ui/app.slint` (+70/−115). Preserved for reference only. |
| `mat-polish` | `fae5fc9` | WIP maturity pass: polish across dark_mail.rs, email_dark.js, main.rs, platform/win.rs and ui files (reader, update banner). |
| `mat-switching` | `20f4fd5` | WIP maturity pass on message switching (mailview.rs, main.rs, msgload.rs, app.slint). Intent: the message on screen stays whole under the bar, dimmed, until the new one swaps in. |

The intent of these three goes into the behaviour catalogue (`spec/`), not into code.

## Merged branches (all in `main`)

| Branch | Tip | Stream and last change |
|---|---|---|
| `main` | `bba9613` | Tagged `rust-final`. Last commit: PORT-PLAN.md, the master plan for the C# port. |
| `automation` | `7593933` | Hidden in-app automation (src/automation.rs, tools/auto.py). Last: unit test for settings import/export/reset. |
| `unified-inbox` | `6fa18ef` | Unified mailbox: "All inboxes" virtual folder across remembered accounts. |
| `sieve-extras` | `846b17b` | Sieve: Bulwark parity for active periods and vacation extras. |
| `settings-wiring` | `38fa42d` | Wiring settings keys to behaviour. Last: hour options for calendarDayEndHour. |
| `composer-redesign` | `3d4591b` | Bulwark-style inline composer replacing the reading pane. |
| `context-menus` | `e00bad5` | Context menus (ctxmenu.rs). Last: full-window popup layer closes on outside click. |
| `parity-gaps` | `96dfe5c` | Parity round 1 gaps. Last: Files draft-18 create detected from supportedNodeTypes (empty drive). |
| `admin-dashboard` | `7bf426e` | Admin dashboard: hover tooltips and WebUI-identical chart generation. |
| `rich-text` | `b9b3e99` | Native rich-text composer (src/rte). Last: re-opened drafts keep inline images (cid restored from the reader's data: URIs). |
| `jmap-audit` | `66e1e05` | JMAP review fixes: no total deadline on downloads, size-scaled upload deadline, sync states keyed by server+user+account, stale mailbox deltas dropped, FUTURERELEASE required for delayed send, undo send never leaves a fake sent copy, unknown folders fetched whole. |
| `editor-admin-gaps` | `091ec6a` | Composer source view keeps inline image data, resets on a new message, follows toolbar/AI changes; one AI rewrite at a time. |
| `remaining-parity` | `d772e1f` | Review fixes: unified rows keyed by source, per-account dedupe, one confirm per selection, Sieve elsif/else and text: blocks, stoppable watchers. |
| `verify-pass` | `f1f33c5` | Verification pass. Last: dashboard x-axis tick thinning, gauge levels held in sparklines. |
| `verify-compose` | `46198f8` | Composer verification: Esc in the link popover cancels only the popover; plain text drops formatting like Bulwark. |
| `verify-mail` | `9e11657` | Mail verification: keep stored credentials on failed sign-in restore and account switch. |
| `design-foundation` | `71349b3` | Bento/Nordic tokens, Style setting, line icons, mail screen (the approved design language). |
| `design-compose` | `e3dd7e7` | Compose design review fixes (hide folders while composing, From fallback, draft-saved time, 15px body). |
| `design-calendar` | `08199d2` | Calendar design review fixes. |
| `design-admin` | `3cbd790` | Admin design review fixes (full-width loading, Nordic hairlines, aligned columns, settings search). |
| `design-people` | `2fbd691` | People/Files design review fixes (narrow Files layout, menu placement, tree indents, category chips). |
| `parity-round2` | `b14e593` | Integration branch of parity round 2: adds PARITY-ROUND2.md, drops round-2 work from GAPS.md. |
| `p2-reader` | `4ad1e78` | Round 2 reader: in-memory large pages, owner-account RSVP, invite trust, MDN and one-click hardening. |
| `p2-pim` | `0827d55` | Round 2 PIM/filters: stale run-on-mail scans dropped, ifInState apply with partial counts, regex and header validation. |
| `p2-distribution` | `d4d2fad` | Round 2 distribution: MSI versions, beta gets stable, update hardening, quit-with-draft. |
| `p2-productivity` | `d16233d` | Round 2 productivity: shortcut fixes, confirm permanent file deletes, calendar drag rollback, global-search folder choice. |
| `p2-composer` | `110ff28` | Round 2 composer: no lost work on reopen during a save, mailto hardening, unregister. |
| `p2-mailextras` | `9147c25` | Round 2 mail extras: import, sender pictures, From override. |
| `p2-calendar` | `cbaa64a` | Round 2 calendar: reminders across accounts and 8+ days ahead, webcal stable UIDs, bounded fetch. |
| `smoothness` | `0530d4a` | Feel/smoothness work on the reader: mark read on the server, no stale reveal, loading state. |
| `motion` | `ea190d4` | Motion foundation: tokens, shared building blocks, incremental lists, frame probe. |
| `motion-compose` | `b7dc2a8` | Motion for the composer. Last: untrack stray pycache. |
| `motion-admin` | `0f8bdf5` | Motion for settings/admin. Last: flush pending background save on exit. |
| `motion-pim` | `464e518` | Motion for calendar/contacts/files: incremental models, view fade-in, hover feedback, probe scenarios. |
| `motion-mail` | `358bb57` | Motion for mail: late-content fades are instant while keys repeat (Motion.late). |
| `oauth-rotation` | `e9f51c8` | OAuth: survive refresh-token rotation across instances, never auto-delete accounts. |
| `bug-compose` | `11d9c90` | Compose bug fixes: confirmations keep the scheduling intent; resize handles; partial recipient text kept on blur. |
| `bug-mail` | `7a60ae8` | Mail bug fixes: hover actions as a clickable overlay, first-frame overlays, keys from the email view. |
| `bug-calendar` | `8f53ca7` | Calendar bug fix: smaller title when the grid is narrow. |
| `bug-merge` | `929733a` | Merge of the bug-* branches into main. |
| `mat-audit` | `929733a` | Start of the maturity pass (same commit as bug-merge; no own work). |

## Worktrees at the freeze
Worktrees `wt-mat-audit`, `wt-mat-overlay`, `wt-mat-polish`, `wt-mat-switching`, `wt-p2-*` and
`wt-p2-combined` were attached to the branches above. Four folders named like worktrees
(`wt-bug-compose`, `wt-editor2`, `wt-scripts`, `wt-verify2`) are not git worktrees (build output and
scratch files only) and hold nothing to preserve.
