# Parity gaps vs Bulwark


Method: I read ROADMAP.md and JMAP-COMPLIANCE.md and walked Bulwark's components/, stores/ and lib/. I compared the 153 data fields of `SettingsState` in stores/settings-store.ts against Barbican's src/, ui/app.slint and settings-schema.json. I also compared the JMAP methods Bulwark calls with the ones Barbican calls. Already-done items (context menus, rich-text editor, unified inbox, accessibility page, Sieve extras, JMAP sync, files trash, admin dashboard and tasks) are left out.

Two cross-cutting findings:
- **Settings rows with no Bulwark key are dropped silently.** settings.rs `schema_rows` skips every `info` row and every keyless row. So whole Bulwark sections never appear: Security (2FA, app lock), Language, Time zone, Default apps, Contacts import/export, Files display options, Storage usage, Vacation audience/forward/keep.
- **About 40 schema settings show in the UI but are read nowhere in the code** (listed in P2-1).

## Done in parity round 2 (branch parity-round2)

These items were in the P0/P1 lists and are now implemented. PARITY-ROUND2.md lists what each one still needs checked live.
- P0-1 recipient chips and autocomplete; P0-2 schedule send; P0-3 draft autosave.
- P0-4 reader actions: view source, unsubscribe banner, read receipts, forward as attachment, quick rules, save to Files, download all.
- P0-5 calendar invitations in mail, with a trust warning.
- P0-6 event editor depth; P0-7 calendar reminders firing.
- P0-8 mailto handling, single-instance handoff and default-app registration.
- P0-9 drag and drop and resizable panes.
- P1-1 command palette and search everything; P1-2 search quality; P1-3 keyboard shortcuts.
- P1-4 calendar: agenda, tasks view and editor, .ics import, webcal, birthdays, find a time.
- P1-5 contacts: vCard import/export, typed entries, photos, duplicates and merge, directory.
- P1-7 filters: edit, reorder, several conditions and actions, header/size/regex, run on existing mail.
- P1-14 (part): .eml/.mbox import and opening an attached .eml.
- P1-17 (part): the notice for truncated bodies.

## P0: daily-use gaps

All nine P0 items are implemented (see above). What is left of them:
1. **Invitations (S):** applying a counter-proposal, and "remove cancelled event" from the banner.
2. **Event editor (S):** the all-day strip in the day/week header has no height cap. An .ics import keeps the file's alerts and does not ask first.
3. **Reminders (S):** an event's own alert set more than 8 days ahead is missed when its calendar's default alerts do not reach that far.
4. **Webcal (S):** feeds on localhost or LAN hosts are not blocked. Plain http is accepted with a warning.
5. **Mailto on Linux (S):** the .desktop registration code has never been compiled.

## P1: feature areas missing or thin

1. **Calendar leftovers (S/M).** Calendar print (calendar-print.tsx). Find-a-time depends on `Principal/getAvailability`, which Stalwart may not support.
2. **Contacts leftovers (S/M).** Contact print (contact-print.ts), contact activity (contact-activity.tsx) and related people. The contacts code (vCard parsing of untrusted files, photo data URI size, export paths) has not had its own review.
3. **Files depth (M).** Bulwark: file-preview-modal.tsx, image-preview-modal.tsx, eml-preview.tsx, file-details-panel.tsx, file-views.tsx (grid/list), upload-conflict-dialog.tsx, lib/files-zip.ts, new-file-dialog.tsx, files-settings.ts. Missing in Barbican:
   - In-app preview (files open externally)
   - Details panel
   - Grid/list view, sort, search and thumbnails (the "Show files as / Default sort / thumbnails / hidden files" rows are dropped)
   - Upload conflict dialog
   - Zip download
   - New text file
   - Filename download rules (`filenameSpaceReplacement` and siblings, `postExportAction`)
4. **Filters leftover (S).** "reject" is offered even when the server does not list the `reject` Sieve extension.
5. **Vacation UI extras (S/M).** Bulwark: vacation-settings.tsx, vacation-template-picker.tsx, vacation-calendar-block.tsx, lib/vacation-audience.ts, vacation-forward.ts. sieve.rs keeps `vacationAudience` and `vacationForward` but no screen edits them. Also missing: reply-to audience, forward while away, keep in inbox, HTML body, templates, and the calendar out-of-office block (`vacationBlockCalendar`).
6. **Identities and signatures (M).** Bulwark: identity-manager-modal.tsx, identity-form.tsx, signature-editor.tsx, signature-details-section.tsx, sub-address-helper.tsx, masked-addresses-section.tsx (`x:MaskedEmail`). Barbican can edit only the name, reply-to and text signature of existing identities. (Sending with a From override is done.) Missing:
   - Create or delete an identity
   - Bcc
   - HTML or rich signature editing (`signatureDetails`)
   - Identity order (`preferredIdentityIds`)
   - Masked addresses
7. **Account security (M).** Stalwart client APIs Bulwark uses (stores/account-security-store.ts, account-security-settings.tsx, app-lock/): `x:AccountPassword` (change password), `x:AccountSettings` (TOTP 2FA), `x:AppPassword`, `x:ApiKey`, `x:PublicKey` (encryption at rest), `x:Account/get`. None are in Barbican, and the whole Security page is dropped. App lock (PIN or Windows Hello, lock after) and session timeout (session/) are also missing.
8. **Templates (S/M).** Bulwark: template-manager-modal.tsx, template-form.tsx, placeholder-fill-modal.tsx, quick-save-template-dialog.tsx, lib/template-merge.ts. Barbican can create (plain text), delete and insert. Missing: edit, rich body, placeholder fill on insert, save the current draft as a template.
9. **Folders (S/M).** Bulwark: organize-settings.tsx, folder-settings.tsx, layout/mailbox-share-dialog.tsx, share-collection-dialog.tsx, icon-picker.tsx. (Moving, re-parenting and reordering folders by drag is done.) Missing:
    - Subscribe/unsubscribe
    - Assign standard roles
    - Folder icons and count modes (`folderIcons`, `folderCountModes`)
    - Pin
    - Mark the whole tree read
    - Per-folder notification mute (`notificationMutes`)
    - Export/import a folder
    - Sharing folders, calendars and address books with other users. Barbican shares Files only.
10. **Live updates outside mail (S/M).** The push `types` cover only mail objects. Calendars, contacts, files and identities do not refresh live, and there is no `/changes` delta for them. Also missing: `ShareNotification` and `CalendarEventNotification` (share-notification-store.ts, calendar-event-notification-store.ts, notifications/notification-center.tsx) and the notification centre itself.
11. **Mail export and copy (M).** .eml/.mbox import is done. Still missing: mbox/eml export of a folder (mail-transfer/), lib/tnef.ts for winmail.dat, and `Email/copy` (copy to another account).
12. **Safety in the reader (M).** Bulwark: lib/phishing-links.ts, phishing-mark-banner.tsx, phishing-report*, external-sender-chip.tsx, brand-verified-badge.tsx with lib/bimi, trusted-senders-modal.tsx (`trustedSenders`, `externalContentPolicy`), external-recipient-confirm.tsx. Barbican has one global "load remote images" choice, the spam badge and the invitation trust warning. (BIMI is used for sender pictures only.) Missing:
    - Link mismatch warnings
    - Phishing report
    - External sender chip
    - BIMI verified badge
    - Per-sender trust for remote content
    - Confirm before sending to external recipients
13. **Pop-out windows (M).** Bulwark: popout/ (message and composer in their own windows). Barbican is a single window.
14. **Small JMAP gaps:**
    - Truncated bodies now show a notice, but are never refetched in full (S)
    - Quota is fetched (`jmap.rs` `quota()`) but never displayed: storage usage on the Account page (S)
    - Images referenced as `cid:` in a re-opened draft: ROADMAP still lists them as placeholders, while commit b9b3e99 says they are restored. Needs a re-check (S).

## P2: settings shown but not wired, plus layout and theming

1. **Unwired settings (S each, M in total).** These rows appear in Settings, but nothing in the code reads them. (Round 2 wired `showAvatarsInJunk`, `unreadBadgeAllAccounts`, `attachmentImagePreviewsEnabled`, `replyUnderMessage`, `nestedTags`, `birthdayCalendarColor`, `senderFavicons` and `emailKeywords`.)
   - `notificationSoundChoice`, `pushRelayUrl`
   - `dateLocale`, `showOnboardingOnNewDevices`
   - `mailLayout` (reading pane right/bottom/off), `toolbarPosition`, `sidebarAccountSwitcher`, `showRailAccountList`, `showSharedMailAccounts`, `compactSharedMailboxes`, `keepAppsLoaded`
   - `listColumns`, `messageListOrderScope`
   - `recipientMentionsEnabled`, `mentionAddsRecipientTo`, `rtlEditingSupport`
   - `debugMode`, `settingsSyncDisabled` (settings sync through Mailbox metadata, lib/settings-sync.ts, is not implemented)

   These settings-store fields have no setting row and no code at all:
   - `defaultReplyMode`, `printTheme`
   - `hiddenCompanyCalendars`
   - `hiddenSharedMailAccounts`, `sharedMailAccountOrder`, `allMailFolderIds`
   - `attachmentReminderKeywords`
   - `sidebarApps`
   - `email/attachment/bundleDownloadTemplate`
   - `protocolOpenMode`, `hideAccountSwitcher`
2. **Themes (M).** Bulwark: themes-settings.tsx, lib/builtin-themes.ts, theme-compiler.ts. Barbican has only light, dark and system: no built-in themes, accent colours or custom themes.
3. **Language and time zone (L for i18n, S for the time zone picker).** Bulwark: language-settings.tsx, time-zone-picker.tsx, locale-store.ts. Barbican is English only and uses the OS time zone. lib/jalali-utils.ts is n/a unless i18n lands.
4. **Message list extras (M).** Bulwark: message-list-tabs.tsx, list-columns.tsx, hover-action tag picker, verification-code copy, `$pinned`, structured-mail cards (structured-mail/: bookings, shipments, pkpass).
5. **Printing (S/M).** Conversation print, contact print, calendar print and print settings (printing-settings.tsx) are missing. Barbican prints a single message.
6. **Composer extras (S/M).** Bulwark: recipient-mention.tsx (@mentions), attach from Files (file-picker-dialog.tsx), lib/message-priority.ts (priority), classification-label-picker.tsx, proofreading/ (next/previous issue, `proofreadingPlugins`), link to Files.

## P3: large and low priority

- **Admin (L).** Barbican's admin is read-mostly: dashboard, directory, domains, queue (delete only), reports, logs, tasks, cluster, actions. Bulwark admin/ and Stalwart webui add create/edit for accounts, groups, lists, domains (DKIM/DNS), API keys, OAuth clients, the settings editor, spam training and queue retry. Per the rules none of this should be tested against the live server.
- **Plugins (L).** Bulwark has a web plugin runtime (lib/plugin-*.ts: slots, hook catalogue, sandbox, OAuth, secrets, settings). Barbican loads native DLLs with 2 hooks (`email_details`, `compose_check`).
- **Offline mode and outbox (L)** (offline/, lib/offline), files sync (files-sync/), WebDAV (lib/webdav), WOPI editing.
- **Delegation and impersonation (M)** (settings/delegates/, impersonation/), tour/onboarding (tour/), Pro interface (pro/), org announcements and disclaimers (org/).

## Roadmap items still open (user-requested)

- **Distribution (M, was L):** .github/workflows ci.yml and release.yml, MSI (WiX), portable zip, .deb and AppImage packaging and the signed manifest tool are written but have never run. The first ci.yml run will be the first MSI compile. Updates need a public asset mirror (`BARBICAN_UPDATE_ASSET_BASE`) plus `BARBICAN_UPDATE_FEED`, `BARBICAN_UPDATE_PUBKEY` and the `BARBICAN_UPDATE_SIGNING_KEY` secret.
- **Auto-updater (S, was M):** the code is done and hardened (signed manifests with expiry, beta follows stable, MSI helper), but download and install have never been run end to end.
- **Tray and notifications:** done on Windows (unread badge, tray menu, close to tray, grouped toasts with actions, calendar reminders). Start at login is implemented but was not exercised, by the no-OS-changes rule.
- **Linux platform layer (M):** a platform layer exists now (src/platform/), but the Linux side, the .deb scripts and the AppImage step have never been compiled (no GTK libraries on this machine). WebKitGTK, Secret Service and the portal colour scheme are unverified.
- **Rich-text composer leftovers (S):** IME is unverified with a real IME, and the pre-edit text covers the text after the caret instead of pushing it aside.
- **Polish font pass:** the memory note marks it done on 2026-11-16 (Skia renderer, Segoe UI, run-through by driving the real window with drive.ps1). Still open: a side-by-side check of font sizes and weights against Bulwark per view, redone through the hidden automation channel since drive.ps1 is now forbidden.
- **ROADMAP.md is out of date:** its "Later" items (unified inbox paging and unread count, accessibility page, calendarMonthTimedEvents, hover-actions corner, Sieve vacation-only scripts and hand-written blocks) were all done in commits 54d75d7, 9482bdd, 8b501d4 and 04f7b96, but are still listed. The "In progress" context-menus line is also stale.

Files read: `<rust-repo>/ROADMAP.md`, `<rust-repo>/JMAP-COMPLIANCE.md`, `<rust-repo>/src/settings.rs`, `<rust-repo>/src/server_settings.rs`, `<rust-repo>/src/main.rs`, `<rust-repo>/src/ctxmenu.rs`, `<rust-repo>/ui/app.slint`, `<rust-repo>/ui/settings-schema.json`, `<bulwark>/stores/settings-store.ts`, `<bulwark>/lib/keyboard-shortcuts/`.
