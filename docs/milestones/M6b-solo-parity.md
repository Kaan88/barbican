# Bulwark default-theme parity (2026-11-20)

This is the chronological Windows verification record. The [active work list](M6b-plan.md#current-work) identifies what to do next. Earlier failures and restart steps below belong to the specified builds.

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

## Bulwark composer footer and form walkthrough

The populated synthetic composer matches the reference's desktop placement
of Discard just before Send (`components/email/email-composer.tsx:6690–6754`).
At 1280 × 720 and 844 × 720, the Discard button measured 36 × 36 effective
pixels with an 8 px gap before Send. The available actions fit at both widths.
Tab moved from the editor to Attach, Templates, Options, Discard, Send and
Send options at both sizes. Shift+Tab from Attach returned to the editor.
Bento retained its header Discard and its Send-first footer order.
The confirmation's Keep editing action preserved the open draft, including
its recipient, subject, body, saved status and draft ID (I-236).

Light and dark screenshots use scale 1, the same synthetic mailbox and
draft, and default pane widths. The original dark capture exposed stale rich
text colors when switching themes with a draft open. Opening a fresh draft
in dark mode showed readable text. The editor now refreshes styled block
layouts when the palette changes and keeps the draft open. The initial
light-to-dark reproduction and compact comparison use the same draft.
The before captures are in `artifacts/auto/9191/`. Run `artifacts/auto/9196/`
has the after captures, the discard confirmation and restored draft, and the
keyboard transcripts. The isolated Windows Automation build had zero warnings
and errors. The focused composer tests passed 77 of 77 and editor layout tests
passed 46 of 46.

In FakeJmap quick reply, `key Ctrl+Enter` went through the same focused shell
handler as Windows KeyDown. With focus outside quick reply and with an empty
focused draft, submission count stayed at zero. Pressing it with the populated
focused draft submitted once to the synthetic recipient. A second press after
focus left kept the count at one (I-220). The final isolated run and captures
are in `artifacts/auto/9197/`.

Calendar's New event button opened the editor with the title, calendar,
all-day, location and description fields visible. Cancel opened the dirty
check. Keep editing kept the typed title and Discard closed the editor.
The two plain date/time field pairs differ from Bulwark's date/time inputs,
and the calendar choices appear as chips. The capture set is
`artifacts/auto/9194/calendar-*.png`. The remaining comparison follows M4,
I-333–I-335 (`components/calendar/event-modal.tsx:1401–1474,1587–1596`).

Contacts' New contact action opened the form, accepted a synthetic first
name and Cancel returned to the same 12 contacts with no saved edit. See
`artifacts/auto/9194/contacts-new-form.png` and the private-desktop transcript
`artifacts/auto/9198/contacts-private-run.txt`. The form
uses a centered card with a scroll area and visible Cancel and Save. Bulwark's
form uses a scrollable panel with the address book and contact type near the top.
The reference also combines contact, group, address book and import in the
New menu, which are separate actions in the native app. These differences
follow M5, I-345 and I-364–I-366 (`components/contacts/contacts-sidebar.tsx:294`,
`components/contacts/contact-form.tsx:873–971`).
## Managed add-on activation and takeover

Windows Automation run 9210 exercised the signed sample through the
production Add-ons page on a private desktop. It approved the synthetic
OAuth route and the install independently. The package started disabled.
An outdated runtime asset set caused startup to fail and Settings offered
Retry while the package remained disabled. With all eight pinned assets
in place, Retry reached Running. Disable stopped it. After a fresh Enable,
withdrawing the synthetic account stopped the running add-on and blocked
another Enable. The ordinary JavaScript fixture reached Running and
stopped through the same Settings page.

The approved managed package contributed a composer header button. With
the sample organization policy connected, the classified composer sent
one raw message to the approved synthetic recipient. FakeJmap recorded
the retained Sent copy, the selected classification and clean marking
header, and deletion of the original draft. The fixture declined a
second recipient outside its permitted example. That draft kept its
recipient, subject, body and saved ID. FakeJmap recorded no new upload
or submission.

Local evidence is in `artifacts/auto/9210/`. The Settings images are
`installed-disabled.png`, `managed-startup-refused.png`,
`managed-running.png`, `managed-disabled.png`, and
`managed-owner-revoked.png`. `managed-composer-action.png` shows the
header contribution. `managed-takeover-send.txt` contains the accepted
send readback. `managed-takeover-denied.txt` and
`managed-takeover-refused.png` capture the retained draft.

The private WebView probes on ports 9211 and 9212 used the same pinned
runtime. The current proof released the entrypoint and completed two
hook round trips. Revoking the proof at the first dispatcher write
after frame readiness produced zero initialization writes and zero
API dispatches. Both processes exited successfully.

## Unchanged Bulwark fixtures

The two fixture files for each add-on matched their SHA-256 pins.
The `plugin-api-probe` fixture changed the subject through a composer
header action. Its action, send check and transform shared the same
composer ID. FakeJmap recorded one submission with the expected
keywords. The compose-link hook used the first duplicate parameter and
ignored an unrelated add-on parameter.

The reader action carried the selected message ID and opened a
root-constrained PDF preview in both the toolbar and compact overflow
menu. Escape dismissed the preview. After explicit policy consent the
in-memory print sink received one watermarked page. Revoking the
provider during a second print left the count at one. Disable removed
the contributed actions. Physical printing was outside the run.

The `proofreading-checker` fixture installed disabled and became Enabled
· Stopped after Enable. Its unchanged entrypoint calls
`composer.registerChecker`, which still needs a native adapter and the
composer's proofreading UI. The generic dialog and scoped mail-read
APIs also remain open. [API evidence and open methods](M6b-api-inventory.md#windows-execution-evidence-for-the-november-20-continuation)

Local logs and images are in `artifacts/auto/9223/` for the API probe
and `artifacts/auto/9213/` for the proofreading fixture. The frame
probe results are in `artifacts/auto/9211/stdout.txt` and
`artifacts/auto/9212/stdout.txt`.
