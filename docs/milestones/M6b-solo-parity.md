# Bulwark default-theme parity (2026-11-20)

This is the chronological Windows verification record. The [active work list](M6b-plan.md#current-work) identifies what to do next. Earlier failures and restart steps below belong to the specified builds.

The original pause is recorded below. Work resumed with one additional reviewer/developer at the user's request. Work now continues on
branch `m6b/bulwark-ui`. This checkpoint follows `585056f`; find it with
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
   Check branch, status and upstream.
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
   reviewer approval, new portable or M7 start.

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
· Stopped at the initial checkpoint. Its unchanged entrypoint calls
`composer.registerChecker`. The later [typing checker replay](#typing-checker-on-windows)
exercises that adapter in the Windows composer.

Local logs and images are in `artifacts/auto/9223/` for the API probe
and `artifacts/auto/9213/` for the proofreading fixture. The frame
probe results are in `artifacts/auto/9211/stdout.txt` and
`artifacts/auto/9212/stdout.txt`.

## Typing checker on Windows

The same pinned manifest and entrypoint reached Running in a private synthetic
session. With verified organization policy and the Composing preference off,
the editor made zero checker posts. Turning on the preference produced two
issues for `dem Brief` and `teh` and matching 1280 × 720 light and dark captures.
The request contained a checker-specific draft alias and only the declared
request fields. The subject marker stayed out of the request. The projection
tests also exercise signature and quoted-text exclusion and UTF-16 offsets.

Send displayed the writing-issue warning. Going back kept the draft and the
submission count stayed at zero. Selecting `den` in the issue menu corrected
the draft and left one issue. Withdrawing policy after the next request was
queued left the native post count unchanged and cleared the issues.
See `artifacts/auto/9232/proofreading-checkpoint.txt` and the captures beside it.
The first successful suggestion and queued-denial replay is also recorded
in `artifacts/auto/9231/application-and-queued-denial.txt`.

The Windows Automation suite passed 3,638 tests with four skips. The
strict M6b catalogue and generated hooks checks passed.

The current native path checks one bounded snapshot of authored text.
Long-draft chunking, on-demand text and annotation exchange, checker action
callbacks, direct issue navigation and per-checker controls remain open.

## Windows checkpoint at 3791df4

The reviewed code revision is `3791df4143ad616727721464a032779c5bce2e40`.
Windows Automation passed 3,623 tests, with zero failures and four
skips. Three live read-only checks require opt-in and one Secret Service
test needs a Linux session bus. The Windows Release build completed
with zero warnings and errors. The strict M6b catalogue, generated
hooks, and design-token checks passed.

The integrated composer and quick-reply replays are in
`artifacts/auto/9224/` and `artifacts/auto/9225/`. The light, dark,
compact, and Discard screenshots are named `candidate-composer-*.png`
and `candidate-discard-confirmation.png`. The matched baseline images
are in `artifacts/auto/9191/`. Quick reply's submission counts were
0 after the unfocused press, 0 after the empty focused press, 1 after the
populated focused press, and 1 after another press once focus had left.

The integrated private Settings and signed-send run is in
`artifacts/auto/9227/`. It also invoked the managed composer header
action and observed a toast attributed to the add-on. The durable
disabled state after a package-manager restart is covered by
`ManagedPluginInstallationTests`. The hidden Automation app uses
process-local package stores and secrets.

The Windows x64 ZIP is `Barbican-3791df4143ad-win-x64.zip` under
`artifacts/portable/`. It is 151,747,099 bytes and contains eight
verified runtime assets and nine notices. Its SHA-256 is
`A642DF9D161C79878983E37FAC6B2E90454D3694331730506A898108DF181819`.
The extracted Release assembly reported the candidate revision.
The isolated smoke run reached shell-ready in 933.1 ms and exited 0.
The report is `artifacts/auto/9226/smoke.json`.

The reviewer approved the checkpoint. The M6b acceptance gate remains open.
[Review decision](../FABLE-REVIEW.md#windows-checkpoint-at-3791df4)

## Nested Settings and restart

The private Windows walkthrough used revision `ea8707d` and repeated the
affected paths with the Settings fixes. It exercised Message list, Reading
and Composing through their actual controls in a synthetic profile.

- **Message list.** Turning conversation grouping off exposed individual
  messages and reduced the selected reader's eight-message conversation to
  a single message. Turning previews off removed the list snippet and kept
  the selected message body. The Starred first preset brought the flagged
  sample message to the top. Adding Unread and moving it up and down changed
  the stored order. Group by date showed Off on the baseline profile while
  the inbox displayed headings. The fresh inbox now starts without date
  headings, matching the switch and Bulwark's default. Turning the switch On
  showed Last month and turning it Off removed it. The existing row hover
  strip appeared under simulated hover. Configuring its actions, placement
  and corner remains on I-419 in M6. [Interaction rows](../../spec/interactions.csv)
- **Reading.** Never kept the sample message unread. After 3 seconds showed
  Mark as read before the delay and Mark as unread afterward. On the baseline,
  the open email kept its old display until it was reopened. With the fix,
  Edge to edge, Always show emails in light mode and Monospace changed the
  same selected email from 16 px to 0 px gutters, dark to white background,
  and Segoe UI to the monospace stack. Changing the unrelated Return to list
  setting kept the current body document sequence. [Reading interaction](../../spec/interactions.csv)
- **Composing.** Quick reply Off hid the reply box on the same open message.
  Replies under the message Off opened an undocked draft and On opened a
  docked draft. At `ea8707d`, the proofreading row displayed its label with
  no input control. The proofreading integration has its own acceptance
  evidence. [Composing interaction](../../spec/interactions.csv)

After restarting the test profile, the nine stored settings matched the
values selected before exit. The flagged message still opened first, the
mail body retained its light edge-to-edge display, Quick reply stayed
hidden, and Reply still opened docked. The local Settings captures and
interaction logs are in `artifacts/auto/9242/`. The fixed fresh-profile
captures and transcript are in `artifacts/auto/9245/`. The focused Windows
Automation build completed with zero warnings and errors. Presentation
passed 1,138 tests and the mail-page builder passed 16 tests.

## Windows portable preview at 2b97b82

A progress preview, not M6b completion or reviewer approval. Source revision
`2b97b824681092b4ab62c36a67701a00f9b8c2b9` adds the add-on dialogs, scoped mail
reads and the remaining composer and proofreading methods to the `3791df4`
checkpoint, plus the desktop-head build fix.

The Windows x64 ZIP is `Barbican-2b97b8246810-win-x64.zip` under
`artifacts/portable/`. It is 146,762,887 bytes, with SHA-256
`B64FBA3648585FF1820EB4B1CF0B3CB40C5794501373EB4EB921144356582841`. The add-on
runtime was rebuilt with `tools/plugin-runtime-build/prepare.ps1` from the
Bulwark 1.21.2 tag (Node 26.11.1, npm 11.20.0); all eight runtime assets and
nine notices matched their pins. The extracted Release app reported the source
revision and the hidden smoke run reached shell-ready in 964.3 ms, exit 0.

Tests behind it: the full Windows suite at `69f867c` passed 3,635 with four
skips. The integrated branch at `2b97b82` passed 512 Core plugin tests and 127
Presentation composer tests, and both heads built without warnings. The new
dialogs were later driven hidden; see the next section.

## Windows portable preview at 7445d74

A progress preview, not M6b completion or reviewer approval. Source revision
`7445d7473be09de4fe9926898785772c2b44e8d4` carries the Bulwark parity packages
WP-0 to WP-14 (WP-6, WP-7, WP-10 and WP-11 in part; open items in the
[gap ledger](bulwark-parity-gaps.md)), the account, identity, tag, template,
calendar and task add-on methods, the AI assistant as a bundled native add-on,
the popup-lint and build-race fixes.

The Windows x64 ZIP is `Barbican-7445d7473be0-win-x64.zip` under
`artifacts/portable/`. It is 147,404,054 bytes, with SHA-256
`2423AFC8196810666840C0E3E023CC1AA9579DD1B3590EE0B7D88280F9478435`. The add-on
runtime is the one prepared from the Bulwark 1.21.2 tag for the `2b97b82`
preview; all eight runtime assets and nine notices matched their pins. The
extracted Release app reports `0.1+7445d74…`, ships both bundled add-ons under
`plugins/`, and its hidden smoke run reached shell-ready in 881.4 ms (first
frame 633.2 ms), exit 0, with foreground window and cursor unchanged.

Tests behind it: the full Windows suite at `e19cb5e` (code-identical to
`7445d74`, which only adds a review-log note) passed 3,970 with four skips;
the desktop, Automation and Release builds, `gen-tokens -Check`, `rules`,
`spec-check` and `red-check` (0 red) were clean. Not checked: Linux (M9), real
pointer and keyboard input, and visual comparison of every changed screen
against a fresh Bulwark capture.

## Native add-on dialogs (hidden replay)

Base revision `cd66846` plus the dialog replay commit on `m6b/dialog-replay`. Windows Automation build, port 9311, profile `auto-9311`,
`BARBICAN_SAMPLE=1`, `BARBICAN_SAMPLE_ADDON_ACCOUNT=1`, `BARBICAN_SAMPLE_ORGANIZATION_POLICY=1`. Synthetic data only: the FakeJmap sample account and a
synthetic add-on installed by `addons-fixture dialog-probe` (`src/Barbican.App/Views/Settings/Automation/DialogProbeFixture.cs`). No real input: reader
actions are invoked through automation peers, and the dialog is answered through the form's own press, field and key methods, and the shell key router.

Replay: start the Automation build with the variables above (`tools/auto-run.ps1 start -Port 9311`), then `python tools/dialog-probe-replay.py 9311`, then
`tools/auto-run.ps1 stop -Port 9311`. The script runs `caption` first (the dialog host needs the active window; without it the call fails), sets
`resize 1296 736` (1280 x 720 capture), and repeats the walkthrough in Bulwark dark and light.

Result: 244 assertions passed, 0 failed (`artifacts/auto/9311/dialog-probe/results.json`, `replay-log.txt`). Resolved values reach the script as toasts
from the add-on's own frame. Observed:

- **Alert** resolves `undefined` by OK, Escape (shell key router) and Enter on the focused OK button. Title, "From the add-on <name>" line, two-line message.
- **Confirm** resolves `true` by OK and Enter on the focused OK button, `false` by Cancel and Escape (focused control). Defaults `Confirm`/`Cancel`, title falls
  back to the add-on name; custom labels `Proceed`/`Not now` are honoured.
- **Danger confirm** starts with focus on Cancel. Enter on Cancel resolves `false`. Enter that does not come from a button is ignored and the dialog stays
  open; OK resolves `true`, Cancel and Escape `false`.
- **Prompt** (user name required, secret password required, optional note): focus starts in the first field; Send is disabled and refuses a press while a
  required field is empty; Enter in an empty required field does not submit; filled resolves `{"user":...,"secret":...,"note":""}` (the password field is
  masked on screen); Cancel and Escape resolve `null`; Enter in a field submits.
- **Focus** returns to the control that was focused before the dialog after every close.
- **Second concurrent dialog** from the same add-on fails with the Conflict message ("Data changed; reload before retrying."); the first dialog stays and resolves normally.
- **Disabling the add-on while a dialog is open** closes the dialog, the runtime stops, and the dead frame reports nothing. The failing value cannot be read
  from the dead frame; that outcome (stale-context failure) is covered by the Core tests.

Limits of the method: key events are routed in process (XAML `KeyDown` itself is not raised; Enter on a focused button is simulated by invoking the button);
the 20 per minute limit and the 30 s reader-action hook timeout are not exercised. Toasts from an add-on are limited to 5 per 10 s, so the script paces itself.
Whole-window WGC captures go blank when the machine's display is idle or off; rerun while it is awake. Captures are local only
(`artifacts/auto/9311/dialog-probe/{dark,light}-{alert,confirm,danger,prompt-empty,prompt-filled}.png`, 1280 x 720, synthetic data); none is committed.

Bulwark comparison (`components/plugins/plugin-dialog-host.tsx`): the attribution now reads "From the add-on <name>" (Bulwark 117-160, "From the plugin"), `**bold**`
runs in the message are drawn bold (44-72), field labels are semibold with a red required asterisk and 4/16 px spacing, the password note is a tinted box with
Bulwark's wording (440-480). Not fixed: the puzzle icon beside the attribution, the 18 px title and 12 px button gap (shared dialog host styles are 16 px and 8 px),
the solid destructive button (Barbican has no destructive token; the rose tint of other danger buttons is used), the per-dialog options menu with "Turn off <add-on>",
and the `prompt`-as-own-window behaviour of Bulwark desktop.


WP-8 follow-up (m6b/wp8-dialogs): the puzzle icon, the 18 px title, the 12 px button gap, the solid destructive button and the "Options for <name>" menu with "Turn off <name>" are done
(ledger `bulwark-parity-gaps.md` section 3.7); the replay still passes 244 of 244. Only the `prompt`-as-own-window behaviour of Bulwark desktop is left, by design (in-window host).