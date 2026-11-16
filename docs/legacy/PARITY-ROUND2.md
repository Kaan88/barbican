# Parity round 2: combined branch

Branch `parity-round2`, cut from `main` (design foundation included). It merges seven parity streams in this order:
1. p2-composer
2. p2-reader
3. p2-calendar
4. p2-productivity
5. p2-pim
6. p2-distribution
7. p2-mailextras

`cargo build --release -j 4` and `cargo test --release -j 4` passed after every merge. The final run had 316 passed and 1 ignored. This branch is **not** merged into main.

| Merge | Commit | Tests |
|---|---|---|
| composer | 27002bb | 115 |
| reader | 18b8308 | 154 |
| calendar | 7e373a5 | 190 |
| productivity | 5787adc | 231 |
| pim | 071b64a | 252 |
| distribution | 8f149aa | 290 (+1 ignored) |
| mailextras | 6f658f3 | 316 (+1 ignored) |
| smoke-test fixes | 55de053 | 316 (+1 ignored) |

## How the merges were resolved

### ui/app.slint
The design version (main) was kept everywhere. Each stream's hooks were then added back as small insertions:
- **Composer:** ScheduledBar.
- **Reader:** RxBanners, "More actions…" in the reader's More menu, and Download all.
- **Calendar:** the Tasks view, the Agenda and Tasks items in the view picker, and a Tasks rail button (shown when the Tasks app setting is on).
- **Productivity:**
  - Drag and drop on folders, tags, tag tiles, mail rows, files and calendar events.
  - Highlighted snippets and the global key hook.
  - Search chips and suggestions, the drag ghost, the command palette and the shortcuts sheet.
  - Resize grips over the shell.
- **PIM:** the contact card, editor and duplicates views, and the filter rule rows and dialogs.
- **Mailextras:**
  - Tag depth and chevrons; mail avatars (photo or logo).
  - Attachment cards, From override with "Edit sender…", and the reply dock and handle.
  - A toast offset over the quick reply.

### Theme
The stream alias globals are now bound to the design `Theme` instead of being synced from Rust. These are `CTheme` (ui/ctheme.slint), `RxTheme` (ui/reader.slint) and `PimTheme` (ui/pim-theme.slint). Every token is an `out property` bound to Theme, so light/dark, Bento/Nordic and more-contrast all follow automatically. src/ctheme.rs is deleted.

### Pane widths
`Pd.side-w` and `Pd.list-w` default to 0, which means "use the style default" (Bento or Nordic). The ResizeHandle is 8 px wide and shows a 2 px accent line only on hover or press. A double-click resets the pane to the style default. panes.json stores 0 for "default".

### Rust
- **Send:** jmap.rs `send_message` uses one HOLDFOR, which covers both a scheduled send and the undo delay. The From override envelope and the retry on `forbiddenFrom` are kept, and so are `reschedule_submission` and `restore_draft_in`.
- **Autosave:** compose.rs autosave keys include the From override fields, so changing the sender counts as an edit. A failed save calls `note_save_failed()`, which a waiting quit needs.
- **Quit:** desktop.rs waits for any autosave still in flight before quitting.
- **Automation `save_file`:** it returns only the path queued by `SAVE`, so a save dialog never opens in an automation run.
- **Calendar:** `duration_of` uses `recurrence::event_duration`; a duplicate `raw_event` was removed.

### Fixes found by the smoke test (55de053)
- **Crash:** the app panicked (`RefCell already mutably borrowed`, src/calendar.rs:81) when switching the calendar to Agenda or Tasks. `render()` held the calendar state while `render_agenda` asked it for colours. The bug came in with p2-calendar, where these views were never opened live. Both views now render after the borrow is released.
- **Search box:** typed text sat about 10 px above the placeholder. This was already the case on main. The TextInput is now full height and vertically centred.

## Per stream

### Composer (p2-composer)
- **Done:**
  - Recipient chips with autocomplete, group expansion, invalid marking and IDN.
  - Draft autosave, including when the composer is reopened during a save.
  - Schedule send: dialog with presets; Reschedule, Cancel and edit, and Cancel send in the Scheduled view; Mod+Shift+Enter.
  - mailto: links open the composer, with single-instance handoff. Default-app register and unregister work on Windows (HKCU) and Linux (.desktop).
- **Partial:**
  - The schedule dialog, Reschedule, Cancel and edit, and Mod+Shift+Enter were never driven live.
  - Default-app register, unregister and repair are unit-tested only. Nothing was written to the registry.
- **Not done:** the Linux build of the mailto registration has never been compiled.
- **Trade-off:** a mailto link with an explicit `target=_self` inside a message no longer opens. This is because mailto from the navigation handler is dropped as unsafe.

### Reader (p2-reader)
- **Done:**
  - Banners: unsubscribe (mailto and RFC 8058 one-click), read receipts, and calendar invitations with RSVP and a trust warning.
  - View source with Copy and Save .eml.
  - Forward as attachment, Download (.eml) and Download all attachments (.zip).
  - Quick rules ("always move / mark read / tag", block sender, manage rules) and save to Files.
  - Dark-mode HTML mail, opening an attached .eml, the truncation notice, and pages over 1.5 MB served from memory.
- **Partial:** none of the stream's fix round was checked live, because its automation port hung. In this branch the More menu, More actions and View source were checked live (see below). The big-page path, RSVP sending, the one-click POST, Always-send receipts and Block sender are still unchecked.
- **Not done:** applying a counter-proposal, and "remove cancelled event".

### Calendar (p2-calendar)
- **Done:**
  - Event editor depth: description, attendees, alerts, time zone, free/busy, custom recurrence and the scope dialog.
  - Reminders firing across accounts, up to 8+ days ahead.
  - Views and tools: Agenda, Tasks view and editor, .ics import, webcal subscriptions, birthdays, and find a time.
  - Sidebar tasks are capped at 6 with "Show all", and overdue tasks get a rose pill.
- **Partial:**
  - Quick-create, the rule builder, the scope dialog, the task editor, .ics import, birthdays and reminder firing were never run live.
  - Find a time depends on `Principal/getAvailability`, which Stalwart may not support.
- **Not done:**
  - The all-day strip has no height cap.
  - Import has no "ask about alerts" step.
  - LAN and localhost feeds are not blocked.
  - An event's own alert more than 8 days ahead is missed when its calendar's defaults do not reach that far.
- **Leftover:** the test event "p2cal test series" (today 18:00) is still on <test-account>.

### Productivity (p2-productivity)
- **Done:**
  - Command palette (Ctrl+K) and search everything.
  - Shortcuts for every app and the `?` sheet.
  - Search syntax chips, suggestions (recent searches and people) and highlighted snippets.
  - Drag and drop: mail to folders and tags, folders, events and files.
  - Resizable panes.
- **Partial:**
  - Drag and drop was never tried live.
  - Search highlights already on screen do not recolour when the theme changes; they update when the list is rebuilt.
- **Not done:** UI-level tests for the overlay and search-box key paths.

### PIM (p2-pim)
- **Done:**
  - Contacts depth: vCard import/export, typed entries, photos, duplicates and merge, directory.
  - Filters depth: edit and reorder rules, several conditions and actions, header/size/regex conditions, and run on existing mail with `ifInState`.
- **Partial:**
  - The contacts commit has not had its own security review: vCard parsing of untrusted files, photo data URI size, export paths.
  - Saving filters and "Apply" were not run live.
- **Not done (never started):**
  - Files depth, vacation extras, identities.
  - The dropped Settings rows and most of the unwired schema settings.

### Distribution (p2-distribution)
- **Done:**
  - Platform layer, tray (unread badge, menu, close to tray) and notifications (grouping and actions).
  - Updater with signed manifests (expiry, beta follows stable, MSI version mapping, MSI helper).
  - Release and CI workflows, MSI/zip/deb/AppImage packaging, and the manifest tool.
  - Quit saves the draft, and a blank composer quits without saving.
- **Partial:**
  - The workflows have never run and the MSI has never been compiled.
  - The update download and install path has never been run end to end. It needs:
    - a public mirror plus `BARBICAN_UPDATE_ASSET_BASE`;
    - `BARBICAN_UPDATE_FEED` and `BARBICAN_UPDATE_PUBKEY`;
    - the `BARBICAN_UPDATE_SIGNING_KEY` secret.
- **Not done:** the Linux side has never been compiled.

### Mail extras (p2-mailextras)
- **Done:**
  - Nested tags with tag editing.
  - Sender avatars from Gravatar, BIMI and favicons. They are off by default, cached, and also shown in Junk.
  - .eml/.mbox import through `Email/import`, with duplicate skipping.
  - From override (catch-all rewrite), attachment preview cards, and replies under the message.
  - All-accounts badge, refresh of the open conversation after sending, delta sync for menu actions, and the toast above the quick reply.
- **Partial:**
  - A From override send was not test-sent live.
  - Tag create, rename and delete were not run live, because they write the shared settings file.
  - Avatar network fetches were not tried.
- **Not done:** listColumns and proInterface.
- **Leftover:** "MXTEST from override + image" and "MXTEST import two" are still in the <test-account> Inbox.

## Smoke test of the combined build (read-only)

The build ran hidden on ports 8935 and 8936 and was driven through `tools/auto.py`. Screenshots are in %TEMP%.

The first run (p2comb_1–21) was signed in to <test-account>. After the crash, the restarted app (p2comb_22–32) came up on <readonly-account>. On <readonly-account> nothing was opened, sent or changed.

| Stream | What was checked | Result | Screenshot |
|---|---|---|---|
| distribution | Startup, `desktop` probe (unread 1, tooltip, kind Dev), `update-check`, `tray quit` | OK, clean exit | p2comb_1.png |
| composer | New message, chips and autosave probe (baseline set, nothing saved) | OK | p2comb_2.png |
| mailextras | From menu with both identities and "Edit sender…" | OK | p2comb_3.png |
| composer | Send with no recipient: "Add at least one recipient." | OK; at 1400 px the Send split arrow is cut off (main design) | p2comb_12.png, p2comb_13.png |
| composer | Esc on an empty composer: no draft kept | OK in 4 attempts | p2comb_11.png, p2comb_14.png |
| reader | Open conversation, sender panel, `rx-banners` (none for this message) | OK | p2comb_16.png |
| reader | More menu, then More actions… (source, forward as attachment, .eml, zip, rules) | OK | p2comb_17.png, p2comb_18.png |
| reader | View source banner (Copy source, Save .eml, Close) | OK | p2comb_19.png |
| calendar | Day view, mini month, sidebar, view picker with Agenda and Tasks | OK | p2comb_20.png, p2comb_21.png |
| calendar | Agenda view | **Crashed**; fixed in 55de053, then OK | p2comb_22.png |
| calendar | Tasks view (filters, overdue pills); sidebar capped at 6 with "Show all (24 more)" | OK | p2comb_23.png |
| productivity | Ctrl+K palette | OK | p2comb_24.png |
| productivity | `?` shortcuts sheet | OK | p2comb_25.png |
| productivity | Search suggestions (recent and people) | OK; text misalignment fixed in 55de053 | p2comb_27.png, p2comb_32.png |
| pim | Contacts: address books, directory, categories, card; `pim dupes` (0 groups) | OK | p2comb_28.png, p2comb_29.png |
| mailextras | Nested tags: Work, then Clients, then Acme | OK | p2comb_30.png, p2comb_24.png |
| design | Nordic light (mail), Bento light (tasks), set in memory only | OK | p2comb_30.png, p2comb_31.png |

Not run here: drag and drop, filters editing, schedule send, import, and anything that writes.

### Other observations
- **Lost keyboard focus:** after switching from Calendar back to Mail with the rail, Ctrl+K and `?` did nothing until the content area was clicked. It looks like the rail click leaves keyboard focus off the main key handler. It is not known whether main has this too.
- **Agenda task rows:** they show both the ring marker and a "○ " prefix in the title. This is a small duplication.
- **Tasks view:** the mini month shades every day.
- **Stray draft:** an empty "(no subject)" draft timestamped 20:42 appeared in <test-account> Drafts right after the first composer smoke test. Four attempts to reproduce it (Esc, Send then Esc, From menu then Esc, resize) saved nothing, and the autosave probe never showed a save. It may have come from one of the workflows running at the same time. I moved it to Trash and deleted it there; Drafts is back to 12.

## What to check by hand

1. Calendar Agenda and Tasks views open without a crash. Do this on the account with many tasks (<readonly-account>), read-only.
2. Do a schedule send to yourself on <test-account>:
   - pick a time;
   - check that it appears in Scheduled;
   - Reschedule it, then Cancel and edit;
   - try Mod+Shift+Enter.
3. Compose on <test-account>: add a recipient chip from autocomplete, type, wait about 3 s, and check that the draft is saved. Then close it and confirm there are no duplicate drafts.
4. Open a fresh composer and press Esc straight away: no draft should appear. Do this right after a cold start.
5. Reader on a newsletter: check the unsubscribe banner. Check an invitation banner with Accept/Maybe/Decline and its trust colour. Save .eml from View source.
6. Drag a message to a folder and to a tag. Drag a calendar event (one with attendees sends updates). Drag the pane grips, and double-click a grip to reset it.
7. Contacts:
   - import a .vcf and export a contact;
   - check the duplicates view;
   - add a photo on a test contact, then delete it.
8. Filters: edit a rule with two conditions, then run it on existing mail with "Apply" in a test folder.
9. Tray: check the menu, close to tray, and the notification actions (Archive, Mark read) on a new mail.
10. Tags: create a nested tag, rename it, then delete it. This writes the settings file.
11. Try both styles (Bento and Nordic), each in light and dark, on the composer footer at 1400 px wide. The Send split arrow is cut off there.
12. Remove the leftovers on <test-account>: the "p2cal test series" event, and the "MXTEST from override + image" and "MXTEST import two" messages.
13. Before release: run ci.yml once, which is the first MSI compile and the first Linux build. Set the update mirror variables and secrets.
