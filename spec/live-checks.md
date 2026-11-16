# Live checks (L-###): never verified live in the Rust app

Source: `desktop/PARITY-ROUND2.md` (Rust, `rust-final`). Every item that the round-2 report lists as
"never driven live", "not run live", "unchecked", "not tried", "observed" or in its hand-check list
becomes one row. None of these was verified in Rust either, so the C# port verifies them
(PORT-PLAN §2.2). The hand-check list (the report's section "What to check by hand", items 1 to 13)
is mapped in the last table.

Account placeholders: `<readonly-account>` (read-only; reads only) and `<test-account>` (writes
allowed; every write cleans up after itself). The real mapping is in `.local/accounts.md`. The
Rust report's leftovers are on `<test-account>`, because they were created by writes there.

Milestone column is the milestone that owns the check (PORT-PLAN §5). `hand` means the user runs it
(agents never do hand tests, CLAUDE.md). `hidden` means it can run through the hidden automation
channel.

| ID | Area | Check | Account | Run by | Milestone | Source |
|---|---|---|---|---|---|---|
| L-001 | desktop | Default mail app: register, unregister and repair on Windows (HKCU). Nothing has been written to the registry; unit tests only. | none (OS change) | hand (user OK needed) | M8 | PARITY "Composer partial" |
| L-002 | composer | Schedule send dialog: presets offered; a time is picked and the message is scheduled. | `<test-account>` | hidden | M3 | PARITY hand item 2 |
| L-003 | composer | The scheduled message appears in the Scheduled view. | `<test-account>` | hidden | M3 | PARITY hand item 2 |
| L-004 | composer | Reschedule a scheduled message. | `<test-account>` | hidden | M3 | PARITY hand item 2 |
| L-005 | composer | Cancel and edit a scheduled message. | `<test-account>` | hidden | M3 | PARITY hand item 2 |
| L-006 | composer | Mod+Shift+Enter opens the schedule dialog. | `<test-account>` | hidden | M3 | PARITY hand item 2 |
| L-007 | composer | Draft autosave: after a recipient chip is added from autocomplete and about 3 s pass, the draft is saved; closing leaves no duplicate drafts. | `<test-account>` | hidden | M3 | PARITY hand item 3 |
| L-008 | composer | A fresh composer closed with Esc straight after a cold start leaves no draft. Four warm attempts passed; cold start not tried. | `<test-account>` | hidden | M3 | PARITY hand item 4; smoke test |
| L-009 | composer | A stray "(no subject)" draft appeared once in Drafts after the first composer smoke test. Four attempts did not reproduce it. Watch for it in the port (it may have come from a parallel workflow). | `<test-account>` | hidden | M3 | PARITY "Stray draft" |
| L-010 | platform | The mailto `.desktop` registration on Linux compiles and registers. Never compiled. | VM | VM run | M9 | PARITY "Composer not done"; GAPS P0-5 (G-007) |
| L-011 | reader | Unsubscribe banner on a newsletter. The RFC 8058 one-click POST has never been sent. | `<readonly-account>` for the banner; `<test-account>` for the POST | hidden (banner), hand (POST) | M2 | PARITY hand item 5; "one-click POST" |
| L-012 | reader | Invitation banner with Accept, Maybe and Decline; the trust colour; the RSVP is sent (never sent). | `<test-account>` for RSVP | hidden | M2/M4 | PARITY hand item 5; reader partial |
| L-013 | reader | "Save .eml" from View source writes the raw bytes to a file. | `<readonly-account>` (file write to a temp folder only) | hidden | M2 | PARITY hand item 5 |
| L-014 | reader | Big-page path: a message over 1.5 MB loads (served from memory) and "Show whole message" works. | `<readonly-account>` | hidden | M2 | PARITY reader "partial" |
| L-015 | reader | Always-send read receipts sends an MDN only to the Return-Path or From address. | `<test-account>` | hidden | M2 | PARITY reader "partial" |
| L-016 | reader | Block sender (quick rule) creates or merges a rule. | `<test-account>` | hidden | M2 | PARITY reader "partial" |
| L-017 | reader | Forward as attachment and Download all (.zip) produce files that open. Only the More menu and View source were checked live. | `<readonly-account>` (download to temp) | hidden | M2 | PARITY reader "done" vs "checked live" |
| L-018 | mail | A From override send is test-sent and arrives with the override as From. | `<test-account>` | hidden | M3 | PARITY mailextras "partial" |
| L-019 | mail | Sender avatar network fetches (Gravatar, BIMI, favicon) run and cache. | `<readonly-account>` (remote requests) | hidden | M2 | PARITY mailextras "partial" |
| L-020 | calendar | Quick-create popover creates an event. | `<test-account>` | hidden | M4 | PARITY calendar "never run live" |
| L-021 | calendar | Rule builder (custom recurrence editor) produces the expected rule. | `<test-account>` | hidden | M4 | PARITY calendar |
| L-022 | productivity | Drag and drop: mail to a folder and to a tag; a calendar event with attendees (sends updates); files onto folders; pane grips drag, and double-click resets. Never tried live. | `<test-account>` for writes | hidden | M2 (mail), M4 (events), M5 (files) | PARITY productivity; hand item 6 |
| L-023 | calendar | Scope dialog: edit "this and following" and "all" on a series. | `<test-account>` | hidden | M4 | PARITY calendar; hand test 4 |
| L-024 | calendar | Task editor: create a task; toggle progress. | `<test-account>` | hidden | M4 | PARITY calendar |
| L-025 | calendar | .ics import: the file's alerts are kept; the "ask about alerts" step is absent (G-004). | `<test-account>` | hidden | M4 | PARITY calendar |
| L-026 | calendar | Birthdays calendar shows contact birthdays as all-day events. | `<readonly-account>` | hidden | M4 | PARITY calendar |
| L-027 | calendar | Agenda and Tasks views open without a crash on the account with many tasks. Crash fixed in 55de053 and smoke-tested OK after the fix; repeat it in the port. | `<readonly-account>` | hidden | M4 | PARITY hand item 1; smoke test p2comb_22/23 |
| L-028 | calendar | Reminder fires for an own alert and for an alert 8 or more days ahead. | `<test-account>` | hidden | M4 | PARITY calendar "reminder firing never run live" |
| L-029 | calendar | Find a time: does Stalwart support Principal/getAvailability? Open question. | `<readonly-account>` | hidden | M4 | PARITY calendar "partial"; G-009 |
| L-030 | cleanup | Remove the test calendar event "p2cal test series" from `<test-account>`. | `<test-account>` | hidden (delete own test data) | M4 | PARITY "Leftover" (calendar) and hand item 12 |
| L-031 | contacts | Import a .vcf file and export one card. | `<test-account>` | hidden | M5 | PARITY hand item 7 |
| L-032 | contacts | Duplicates view and merge on test cards. Group count was 0 in the smoke test. | `<test-account>` | hidden | M5 | PARITY hand item 7; smoke test |
| L-033 | contacts | Add a photo to a test contact, then delete it. | `<test-account>` | hidden | M5 | PARITY hand item 7 |
| L-034 | contacts | Hostile vCard and photo fixtures (oversized data URI, bad encodings) do not crash or grow memory; the contacts security review (G-012) is done on them. | none (fixtures) | CI-equivalent unit tests (VM and Windows) | M5 | PARITY "contacts security review not done" |
| L-035 | files | Upload, trash and restore a file on the test account; Empty trash asks first. | `<test-account>` | hidden | M5 | Not in the Rust report; Files live was not run (the smoke test did not cover Files) |
| L-036 | files | Share, move, cut and paste, and duplicate a file on the test account. | `<test-account>` | hidden | M5 | Not in the Rust report; not run live |
| L-037 | files | Save to Files copies an attachment from the message's account. | `<test-account>` | hidden | M2 | Reader "Save to Files" (D- row); not run live |
| L-038 | mail | Mail export of a folder and import of an .mbox. | `<test-account>` | hidden | M2 | PARITY mailextras "import" (only partial) |
| L-039 | mail | Delta sync after a menu action updates the list without a full reload. | `<test-account>` | hidden | M2 | Commit 5217a5c; not in the report |
| L-040 | filters | Edit a rule with two conditions, then run it on existing mail with "Apply" in a test folder. | `<test-account>` | hidden | M6 | PARITY hand item 8; PIM "Partial" |
| L-041 | filters | Saving filters writes a script that Bulwark's Sieve view reads (round trip). | `<test-account>` | hand (Bulwark view) | M6 | PARITY PIM "Partial"; Bulwark round trip in 1972552 |
| L-042 | tray | Tray menu, close to tray, and the notification actions Archive and Mark as read on a new mail. | `<test-account>` | hand | M8 | PARITY hand item 9 |
| L-043 | tray | Tray items Open, Compose, Check mail and Quit each act once. | none | hidden (automation tray probe) | M8 | PARITY distribution (desktop probe OK; items not all tried) |
| L-044 | settings | Tags: create a nested tag, rename it, then delete it (this writes the shared settings file). | `<test-account>` | hidden | M2 | PARITY hand item 10; mailextras "not run live" |
| L-045 | settings | Settings save survives an app restart (prefs written off the UI thread). | none | hidden | M6 | commit 0f8bdf5; not in the report |
| L-046 | design | Both styles (Bento and Nordic), each in light and dark, on the composer footer at 1400 px wide. | `<readonly-account>` (screen only) | hidden | M3 | PARITY hand item 11 |
| L-047 | shortcuts | After the rail switches from Calendar back to Mail, Ctrl+K and ? do nothing until the content area is clicked (keyboard focus left off the main key handler). | `<readonly-account>` | hidden | M2 | PARITY "Lost keyboard focus" (observed) |
| L-048 | calendar | Agenda task rows show both the ring marker and a "○ " prefix in the title (small duplication). Check it is gone in the port. | `<readonly-account>` | hidden | M4 | PARITY "Agenda task rows" (observed) |
| L-049 | calendar | Tasks view: the mini month shades every day (observed). Check the port shades only the filter day. | `<readonly-account>` | hidden | M4 | PARITY "Tasks view" (observed) |
| L-050 | palette | Search highlights recolour when the theme changes, without rebuilding the list. | `<readonly-account>` | hidden | M2 | PARITY productivity "partial" |
| L-051 | desktop | Start at login (HKCU Run key) runs at sign-in and starts hidden in the tray. OS change: needs the user's OK. | none (OS change) | hand | M8 | PARITY "Tray: start at login not exercised"; G-089 |
| L-052 | design | Composer footer at 1400 px: the Send split arrow is cut off (seen in the Rust main design; still open in the port's check). | `<readonly-account>` (screen only) | hidden | M3 | PARITY hand item 11; smoke test p2comb_12/13 |
| L-053 | motion | Dark settings_open shows a 3-frame identities fill-in (the known flicker exception in the Rust probe). The port must not regress it. | none | hidden (probe) | M6 | commit 0530d4a |
| L-054 | distribution | The Rust release and CI workflows: run once (first MSI compile, first Linux build). The C# equivalent is the M8 release and the manual-only release workflow. | none | hand (later) | M8 | PARITY hand item 13; G-087 |
| L-055 | updater | Update download and install end to end: needs a public asset mirror, `BARBICAN_UPDATE_FEED`, `BARBICAN_UPDATE_PUBKEY` and the signing secret. | none | hand (later) | M8 | PARITY "Distribution partial"; G-088 |
| L-056 | platform | Linux: the .deb and the AppImage run; the mail body, menus over it, dark mode, tray, notifications, keyring and updates work. | VM | hand (VM) | M9 | PORT-PLAN M9 hand test; GAPS roadmap (G-090) |
| L-057 | cleanup | Remove the leftover test messages "MXTEST from override + image" and "MXTEST import two" from `<test-account>` Inbox. | `<test-account>` | hidden (delete own test data) | M2 | PARITY "Leftover" (mail extras) and hand item 12 |
| L-058 | composer | Reply and forward with a quoted inline image resend it as a cid part; the recipient receives it inline. | `<test-account>` | hidden | M3 | Commit 20ae864; not in the report |

## Hand-check list mapping (PARITY-ROUND2 "What to check by hand")

| Hand item | Rows |
|---|---|
| 1. Agenda and Tasks open without a crash | L-027 |
| 2. Schedule send to yourself: pick a time, Scheduled, Reschedule, Cancel and edit, Mod+Shift+Enter | L-002 to L-006 |
| 3. Compose: recipient chip from autocomplete, wait, draft saved, no duplicate | L-007 |
| 4. Fresh composer Esc straight after a cold start: no draft | L-008 |
| 5. Reader: newsletter unsubscribe banner; invitation banner; Save .eml from View source | L-011, L-012, L-013 |
| 6. Drag mail to a folder and a tag; drag an event with attendees; pane grips and reset | L-022 |
| 7. Contacts: import a .vcf and export; duplicates view; photo add and delete | L-031, L-032, L-033 |
| 8. Filters: edit a two-condition rule and Apply in a test folder | L-040 |
| 9. Tray: menu, close to tray, notification actions | L-042 |
| 10. Tags: create nested, rename, delete (writes settings) | L-044 |
| 11. Both styles and both modes on the composer footer at 1400 px | L-046, L-052 |
| 12. Remove the leftovers on the test account | L-030, L-057 |
| 13. Before release: run the workflows once; set the update mirror and secrets | L-054, L-055 |

## Notes for the port

- Rows marked `hidden` run through the hidden automation channel (PORT-PLAN §4.4). Rows marked `hand`
  are for the user; agents never do them.
- Writes happen only on `<test-account>`, and each write test removes what it created (CLAUDE.md).
- L-034 is a fixture test, not a live check, but it is kept here because the Rust report lists the
  contacts security review as open.
