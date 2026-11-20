# Bulwark default-theme parity (2026-11-20)

The original pause is recorded below. Work resumed with one additional Astra reviewer/developer at the user�s request. Use the existing
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

## Composer maximize and restore

The Bulwark composer expands below the native title bar. Restoring it returns
the same editor to the message pane or its reply dock. At a 1280 x 920 client
size new mail grew from 584 x 882 to 1280 x 882, and a docked reply from
584 x 529 to 1280 x 882. Both returned to their original bounds after a window
resize. Hidden sample runs 8971 and 8972 compared the saved draft ID, recipient,
subject, body and attachment before and after, and captured each visible state.
Full Windows Automation verification passed 3,157 tests with four skips.
The portable at c3ca11e predates this change. Pop-out remains open.

## Reply dock divider

The dock remembers the reader/composer split. Arrow keys adjust it in 16 px
steps. Home or a double tap restores the default. Hidden sample run 8974
measured 529 px for the default composer and 662 px after reopening with a
75% preference. The 10 px divider spanned the composer width. Both layouts
recovered their original bounds after shrinking to takeover height. Windows
Automation passed 3,161 tests with four skips. The hidden run exercised
preference restoration and layout; pointer dragging awaits hands-on review.

## Inline reply delivery

The reader's Send button and Ctrl+Enter run the composer checks and confirmation
dialog. An inline reply uses the selected message's account, threading headers,
own identities and signature. It omits quoted content, inline attachments from
the original and catch-all From overrides. A declined confirmation keeps the
text available for revision or transfer into the full editor.

Windows Automation passed 3,165 tests, with four skipped. Hidden sample runs
8977 and 8978 exercised cancellation followed by transfer, and confirmation
followed by composing another reply. The portable at e716ccf contains the
verified dock and maximize changes and predates inline delivery.

## Windows interaction reference and November 20 verification

Bulwark remains the default theme. The original native WinUI mockup is a
reference for interaction feedback, animation and transitions only. Keep
the existing appearance, layout, controls and settings choices in both
Bulwark and Bento. Judge motion by opening mail, switching folders, returning
to the list and reversing direction before an animation finishes. Keep input
available during transitions and respect reduced motion.

The narrow inbox now fills the list pane, shows dates and search actions,
and lets the user go through Folders to Sent. The mobile Reply all icon was
replaced. Reader actions include Move and Tag in the overflow on smaller
screens. A hidden Windows walkthrough returned from Settings, Calendar,
Contacts and Files to the same selected message across thirteen fast changes.
The pop-out composer now protects the draft when a second open races the
first. Independent checks accepted maximize, restore and reply-dock resizing.

The Windows run through the account flyout change passed 3,178 tests with
four skipped. A private sample run opened the account switcher and verified
that Escape reduced the number of open popups from one to zero. Later visual
changes require a fresh combined run and portable launch check.

### Native interaction check

Independent Windows review accepted the M6b consumer reader, conversation
and compact navigation at 7e53be1. The tested flows include independently
expanded conversation details, message-specific Reply all and Forward,
live text-size changes, visible card height, and rapid list, reader and folder
navigation with reduced motion enabled and disabled. The native composer
maximize, restore, dock and detached-window checks passed separately.
The organization print policy remains tracked under M10/I-108.

Compact navigation uses a 110 ms incoming transition with input available
throughout. In Bulwark, the reader heading now stays fully legible while
rising two pixels. Three paired Windows capture runs showed no dimmed heading
frames after this change across 76 frames. Short sample messages opened
in 11–18 ms. The large synthetic export took 185–241 ms; the list highlight
appeared within 40 ms even on that message. These timings cover the local
sample account in hidden Windows runs. They do not measure a live mail server
or physical pointing-device input.

## Windows verification at 06ad154

The full Windows Automation solution built without warnings or errors.
It passed 3,179 tests with zero failures and four skips. Three skips require
a live JMAP account and one requires the Linux session bus.

Hidden synthetic sample run 9073 captured the rendered five-message
conversation in both Bulwark themes at 1280 × 720. It also captured new mail
at 1280 × 720 with the folder sidebar and at 844 × 720 with the sidebar folded.
The run used the saved default pane widths. The four captures are local
under `artifacts/auto/9073/`; the runner closed the sample process.

The extracted Windows x64 portable reported source revision
`06ad154ae817814e8a83eeca06712e0cbcdb09b6`. Its isolated Release smoke
reached shell-ready in 978 ms and exited 0. The archive SHA-256 is
`1528786F81F2589CA9A0A49460077A5DE6E1E4E602FFA44D993E39F27A95F853`.
The smoke report is local at `artifacts/auto/9071/release-smoke.json`.
This verification covers the UI checkpoint. M6b plugin acceptance remains open.

## Windows portable verification at 004b9f0

The Windows x64 Release archive was built from a clean checkout of
`004b9f08e40f3e2fc8c4e12cf45db02ba8d03342`. The extracted app reported
that source revision, reached shell-ready in 855 ms in an isolated
profile, and exited 0. Foreground window and cursor position were
unchanged during the hidden run. The archive has the base Windows app
and requires separately packaged sandbox assets for JavaScript add-ons.
Its SHA-256 is
`CA6BB73FFB3D0950C6D542D64DC90B1D607C492BE5BE2CE0D9E8C80C915A338A`.

## Windows portable preview at 4c46940

`Barbican-4c46940318d3-win-x64.zip` was built from a clean detached
checkout of `4c46940318d3f77c38a48c2d1ac106b447cd37f1`. The extracted
Windows x64 Release app reported that revision and passed the isolated
launch check with exit code 0. Foreground and cursor position were
unchanged. The archive includes eight verified sandbox runtime assets
and nine license notices. Managed installation is available in the
disabled state while production managed activation is still in progress.

The ZIP is 151,701,278 bytes. Its SHA-256 is
`A910A72B110CFFBB74948FCAB7019B5815031827D5F9DCB680C66B4F312C42F3`.
