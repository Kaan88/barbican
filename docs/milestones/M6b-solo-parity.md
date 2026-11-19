# Paused: Bulwark default-theme pass (2026-11-20)

Resume only when asked; do the work yourself, without subagents. Open the existing
`wt-m6b-bulwark-ui` worktree, branch `codex/m6b-bulwark-ui`. In a new clone use
that remote branch. This checkpoint follows `585056f`; find it with
`git log -1 -- docs/milestones/M6b-solo-parity.md`. The `barbican` coordinator
worktree is older and has unrelated test deletions. Do not restore them, merge
to main, or build from the old preview ZIP.

## What changed

- Reader: replaced legacy action pills with a flat toolbar; made toolbar,
  subject and sender full-width ruled sections. Removed the empty inter-row
  spacing, added the active account's avatar to quick reply, and replaced the
  legacy empty-state mark with the reference envelope and copy in Bulwark mode.
- Inbox: flush selected row and separators; denser folders; functional sidebar
  account switcher using the existing constrained popover and the
  `sidebarAccountSwitcher` preference.
- Last edits after the rendered comparison: unread row wash, reader background
  token, and keep the folder sidebar beside the composer when the saved pane
  widths leave at least 480 px. **These three edits need fresh captures.**

## Evidence to inspect first

Use the pinned `<bulwark>/public/screenshot-1280x720.png`,
`screenshots/split-inbox.webp`, and `screenshots/light-composer-mention.webp`.
Compare against `components/email/{email-viewer,thread-list-item,email-composer}.tsx`,
`components/layout/sidebar.tsx` and `stores/settings-store.ts`. The published
picture shows sidebar Compose and unlabeled buttons. The source default is
floating Compose (`lib/compose-placement.ts`) and labels enabled. Compare like
settings before changing those defaults.

In this worktree, `artifacts/auto/8955/` holds **local-only** sample evidence:
`reference-above-native.png` (reference above native, both 1280×720),
`final-light.png`, `final-dark.png`, `final-empty.png`, and `compose-comparison.jpg`.
Despite the filenames, these predate the last three edits. The composer comparison
shows the sidebar disappearing; that is why the last layout change was made.
No live account was used. Earlier Windows Automation builds passed; on the
private desktop `invoke sidebar.account` opened one constrained flyout, and the
fullscreen control toggled twice. Whole-window capture failed on that desktop;
the PNGs came from ordinary **hidden** automation with WGC.

## First actions in the next harness

1. Read `CLAUDE.md`, this file and [the earlier handoff](M6b-parity-handoff.md).
   Check branch, status and upstream. Leave unrelated `.cortexkit/` alone.
2. Port 8955 is stopped and the build server shut down. The Windows Automation
   suite finished on this source: **3150 passed, 2 failed, 4 skipped**. See
   `artifacts/auto/8955/final-test.log`. The view accessibility lint names five
   controls: older-message disclosure, quick-reply Cancel and Open full editor,
   sidebar Compose, and dock disclosure. The embedded settings-schema digest also
   differs from the reviewed catalog. Diagnose against the prior revision before
   changing the hash. Run the focused tests then the suite with
   `BARBICAN_BUILD_SLOTS=3` and `tools/build.ps1 test -Configuration Automation -Head win`.
3. Start hidden sample automation with `BARBICAN_SAMPLE=1` and a free port.
   `resize 1296 736` gave 1280×720 WGC at scale 1 on this machine. Use
   `ui bulwark light`, `select #0`, then `wshot` with an **absolute** output
   path. Repeat dark. `invoke list.compose` opens the editor; `key Ctrl+N`
   returned unhandled. Verify wide/narrow compose, changed pane widths and a
   theme round trip. Use a private desktop for popup checks. Stop only the
   process you started; shut down the wrapper build server after it finishes.
4. Next visual gaps: the Windows title strip, reader sender/date hierarchy and
   authentication badge, folder selection and role colors, new-message
   formatting toolbar, and reference actions missing from the reader. Compare
   focus, text scaling and high contrast. Structural conversation, quick-reply
   and pop-out gaps are tracked in the earlier handoff. No parity sign-off,
   Astra approval, new portable or M7 start.

## Solo verification after 084ff0f

Full Windows Automation suite passed with 3,152 successes and four skips.
The five accessibility metadata failures are fixed. The reviewed schema
matches the Bulwark appearance and colourful-folder defaults. Icons follow
the saved preference and existing colour-blind mapping. The reader separates
the date, address and recipients and suppresses an empty authentication badge.
Quick reply resolves its placeholder from the reply recipients and identities.

Local synthetic captures in `artifacts/auto/8967/` show light and dark at
1280 x 720, a narrow composer at 844 x 720, and the increased-contrast theme.
A pane change while composing now updates the sidebar immediately. At 1280
wide the 320/520 pane sizes fold it, then 220/330 restore it. The earlier
conversation, inline delivery and window-flow gaps remain open.

## Grouped inbox preview

The regular mailbox and unified list request one representative per thread,
with identical grouping for pagination and incremental sync. Disabling
threading restores individual rows. The two-query unread-first layout still
uses individual messages. Folder-wide actions enumerate every matching
message through an independent cursor. The representative retains selection
when a reply arrives and keeps its thread count through keyword updates.

The synthetic Inbox showed five rows, including one five-message conversation
(`artifacts/auto/8967/grouped-thread.png` and its dark counterpart). Windows
Automation passed 3,156 tests with four skips, including grouped/ungrouped
paging, folder-wide actions and replacement of a selected representative.
The user requested a portable preview at this checkpoint.
