# Parity gaps vs Bulwark


Method: I read ROADMAP.md and JMAP-COMPLIANCE.md and walked Bulwark's components/, stores/ and lib/. I compared the 153 data fields of `SettingsState` in stores/settings-store.ts against Barbican's src/, ui/app.slint and settings-schema.json. I also compared the JMAP methods Bulwark calls with the ones Barbican calls. Already-done items (context menus, rich-text editor, unified inbox, accessibility page, Sieve extras, JMAP sync, files trash, admin dashboard and tasks) are left out.

Two cross-cutting findings:
- **Settings rows with no Bulwark key are dropped silently.** settings.rs `schema_rows` skips every `info` row and every keyless row. So whole Bulwark sections never appear: Security (2FA, app lock), Language, Time zone, Default apps, Contacts import/export, Files display options, Storage usage, Vacation audience/forward/keep.
- **About 40 schema settings show in the UI but are read nowhere in the code** (listed in P2-1).

## P0: daily-use gaps

1. **Recipient autocomplete and chips (L).** Bulwark: components/email/email-composer.tsx, recipient-popover.tsx, lib/address-format.ts, rfc5322-mailbox.ts, idn.ts. Barbican's To/Cc/Bcc are plain `BareField` text (app.slint ~1896). There are no suggestions from contacts or recent addresses, no chips, no group expansion and no invalid-address marking.
2. **Schedule send (M).** Bulwark: schedule-send-dialog.tsx, lib/schedule-presets.ts, held-send.ts, scheduled-send-label.ts; menu items cancel_scheduled_send, reschedule_send, cancel_and_edit. The composer item is disabled ("Schedule send (soon)", app.slint ~2287). The Scheduled view has no cancel, reschedule or edit actions. Mod+Shift+Enter is not bound. FUTURERELEASE is already wired, so this is mostly UI.
3. **Draft autosave (S).** Bulwark email-composer.tsx saves drafts on a debounce. Barbican saves only on Ctrl+S, Esc or close, so a crash loses the draft.
4. **Reader actions (M total).** Bulwark: view-source-dialog.tsx, unsubscribe-banner.tsx, read-receipt-banner.tsx with lib/mdn.ts and receipt-answers.ts, lib/forward-as-attachment.ts, rules-menu.tsx with lib/filters/quick-rules.ts, save-to-files-dialog.tsx. Missing in Barbican:
   - View source / show original (S)
   - List-Unsubscribe banner (S)
   - Answering read-receipt requests, settings `readReceiptResponse` (S)
   - Forward as attachment (S)
   - Quick rule from a message, e.g. "always move mail from this sender" (M)
   - Save attachment to Files (S)
   - Download all attachments (S)
5. **Calendar invitations in mail (M).** Bulwark: calendar-invitation-banner.tsx, lib/calendar-invitation.ts, `CalendarEvent/parse`. `calendarInvitationParsingEnabled` is not wired. Barbican offers RSVP only for events already in the calendar.
6. **Event editor depth (L).** Bulwark: event-modal.tsx, participant-input.tsx, alert-list-editor.tsx, recurrence-editor.tsx, recurrence-scope-dialog.tsx, quick-create-popover.tsx. Barbican's editor (app.slint ~3140) has only title, one date, start/end, all-day, 5 repeat presets, calendar and location. Missing:
   - Description
   - Attendees / invitations
   - Alerts (reminders)
   - Time zone and multi-day end date
   - Free/busy status
   - Custom recurrence (interval, byDay, until/count)
   - "This / this and following / all" scope when editing a series
   - Quick-create popover
7. **Calendar reminders firing (M).** Bulwark: lib/calendar-alerts.ts, calendar-reminders/, missed-reminders-dialog.tsx, calendar-event-notification-toaster.tsx, settings `calendarNotificationsEnabled` and `calendarNotificationSound`. Barbican shows no reminder notifications at all.
8. **Mailto handling (S).** Clicking a mailto: link in a message hands it to the OS (main.rs ~1183 and the WebView navigation handler) instead of opening Barbican's composer. There is also no registration as the default mail app (lib/protocol-handlers, protocol-handler-settings.tsx) and no single-instance handoff (M).
9. **Drag and drop (M).** Bulwark: lib/folder-tree-dnd.ts, file-drop/, the calendar views. Missing in Barbican:
   - Dragging mail to a folder or tag
   - Reordering or re-parenting folders
   - Moving or resizing events in day/week view
   - Dragging files into folders
   - Resizable panes (layout/resize-handle.tsx)

## P1: feature areas missing or thin

1. **Command palette and global search (M/L).** Bulwark: command-palette/command-palette.tsx with lib/command-registry.ts and app-commands.ts (Ctrl+K); global-search/ (one search across mail, contacts, events and files, with facets and preview). Barbican has neither.
2. **Search quality (M).** Bulwark: lib/search-snippet.ts and search-snippet-text.tsx (`SearchSnippet/get`), search-suggestions.ts, search-history-store.ts, search/search-chips.tsx. Barbican has no snippet highlighting, no history or suggestions, and no query-syntax chips.
3. **Keyboard shortcuts (S/M).** Bulwark: lib/keyboard-shortcuts/*.ts, keyboard-shortcuts-modal.tsx. Barbican handles only mail shortcuts (main.rs ~1417 returns early whenever app != mail). Missing:
   - `g i/s/t/d/a` folder jumps, `Enter`/`o` to open, `x` for threads
   - Shift+arrow to extend the selection
   - Ctrl+E, F5/F9
   - Composer keys: Ctrl+Shift+C/B, Mod+Shift+Enter
   - Every calendar key (←/→/t/d/w/m/a/k/n/e/`/`), every contacts and files key
   - The `?` shortcuts help sheet
4. **Calendar views and tools (L).** Bulwark: calendar-agenda-view.tsx, task-list-view.tsx, task-modal.tsx, task-toolbar.tsx, tasks-sidebar-filters.tsx, ical-import-modal.tsx, ical-subscription-modal.tsx, lib/calendar-subscription-sync.ts, find-a-time.tsx, participant-availability.tsx (`Principal/getAvailability`), free-busy-share-button.tsx, calendar-print.tsx, lib/birthday-calendar.ts, mini-calendar.tsx. Barbican has Day/Week/Month only and a sidebar task list. Missing:
   - Agenda view
   - Full tasks view and task editor; settings `enableCalendarTasks`, `showTasksOnCalendar`, `showTasksApp` are not wired
   - .ics import
   - Webcal subscriptions
   - Find a time / free-busy
   - Calendar print
   - Birthday calendar (`showBirthdayCalendar` not wired)
   - Shared-calendar colours (`sharedCalendarColors`)
5. **Contacts depth (M/L).** Bulwark: contact-import-dialog.tsx, contact-export.tsx, lib/vcard.ts, contact-duplicates-view.tsx, contact-merge-dialog.tsx, directory-browser.tsx, contact-print.ts, contact-activity.tsx, lib/contact-photo-cache.ts, contact-related.ts. Missing in Barbican:
   - vCard import/export
   - Duplicates and merge
   - Directory (`showContactsDirectory` not wired)
   - Photos
   - Print
   - Contact activity
   - Form fields: the form has one company, one title, one address, one birthday. There are no typed or labelled emails and phones, no multiple addresses, no URLs, IM, anniversaries or related people.
6. **Files depth (M).** Bulwark: file-preview-modal.tsx, image-preview-modal.tsx, eml-preview.tsx, file-details-panel.tsx, file-views.tsx (grid/list), upload-conflict-dialog.tsx, lib/files-zip.ts, new-file-dialog.tsx, files-settings.ts. Missing in Barbican:
   - In-app preview (files open externally)
   - Details panel
   - Grid/list view, sort, search and thumbnails (the "Show files as / Default sort / thumbnails / hidden files" rows are dropped)
   - Upload conflict dialog
   - Zip download
   - New text file
   - Filename download rules (`filenameSpaceReplacement` and siblings, `postExportAction`)
7. **Filters (M).** Bulwark: filter-rule-modal.tsx, lib/jmap/sieve-types.ts, lib/filters/retroactive.ts, quick-rule-dialogs.tsx. Barbican's rule builder (server_settings.rs ~207) allows one condition and one action, and rules can only be added, toggled or deleted. Missing:
   - Editing an existing rule
   - Reordering rules
   - Several conditions with all/any
   - Several actions
   - Header, size, regex and matches conditions; not_is comparator; attachment type
   - reject, keep and stop actions
   - "Run on existing mail" (retroactive)
8. **Vacation UI extras (S/M).** Bulwark: vacation-settings.tsx, vacation-template-picker.tsx, vacation-calendar-block.tsx, lib/vacation-audience.ts, vacation-forward.ts. sieve.rs keeps `vacationAudience` and `vacationForward` but no screen edits them. Also missing: reply-to audience, forward while away, keep in inbox, HTML body, templates, and the calendar out-of-office block (`vacationBlockCalendar`).
9. **Identities and signatures (M).** Bulwark: identity-manager-modal.tsx, identity-form.tsx, signature-editor.tsx, signature-details-section.tsx, sub-address-helper.tsx, masked-addresses-section.tsx (`x:MaskedEmail`). Barbican can only edit name, reply-to and the text signature of existing identities. Missing:
   - Create or delete an identity
   - Bcc
   - HTML or rich signature editing (`signatureDetails`)
   - Identity order (`preferredIdentityIds`)
   - Masked addresses
10. **Account security (M).** Stalwart client APIs Bulwark uses (stores/account-security-store.ts, account-security-settings.tsx, app-lock/): `x:AccountPassword` (change password), `x:AccountSettings` (TOTP 2FA), `x:AppPassword`, `x:ApiKey`, `x:PublicKey` (encryption at rest), `x:Account/get`. None are in Barbican, and the whole Security page is dropped. App lock (PIN or Windows Hello, lock after) and session timeout (session/) are also missing.
11. **Templates (S/M).** Bulwark: template-manager-modal.tsx, template-form.tsx, placeholder-fill-modal.tsx, quick-save-template-dialog.tsx, lib/template-merge.ts. Barbican can create (plain text), delete and insert. Missing: edit, rich body, placeholder fill on insert, save the current draft as a template.
12. **Folders (S/M).** Bulwark: organize-settings.tsx, folder-settings.tsx, layout/mailbox-share-dialog.tsx, share-collection-dialog.tsx, icon-picker.tsx. Missing:
    - Subscribe/unsubscribe
    - Move or re-parent a folder
    - Reorder (sortOrder)
    - Assign standard roles
    - Folder icons and count modes (`folderIcons`, `folderCountModes`)
    - Pin
    - Mark the whole tree read
    - Per-folder notification mute (`notificationMutes`)
    - Export/import a folder
    - Sharing folders, calendars and address books with other users. Barbican shares Files only.
13. **Live updates outside mail (S/M).** The push `types` cover only mail objects. Calendars, contacts, files and identities do not refresh live, and there is no `/changes` delta for them. Also missing: `ShareNotification` and `CalendarEventNotification` (share-notification-store.ts, calendar-event-notification-store.ts, notifications/notification-center.tsx) and the notification centre itself.
14. **Mail import/export and .eml (M).** Bulwark: mail-transfer/ (`Email/import`, mbox/eml export), lib/eml-import.ts, `Email/parse` for attached .eml (eml-preview.tsx), lib/tnef.ts for winmail.dat, `Email/copy` (copy to another account). Barbican has only single-message .eml download.
15. **Safety in the reader (M).** Bulwark: lib/phishing-links.ts, phishing-mark-banner.tsx, phishing-report*, external-sender-chip.tsx, brand-verified-badge.tsx with lib/bimi, trusted-senders-modal.tsx (`trustedSenders`, `externalContentPolicy`), external-recipient-confirm.tsx. Barbican has one global "load remote images" choice plus the spam badge. Missing:
    - Link mismatch warnings
    - Phishing report
    - External sender chip
    - BIMI
    - Per-sender trust for remote content
    - Confirm before sending to external recipients
16. **Pop-out windows (M).** Bulwark: popout/ (message and composer in their own windows). Barbican is a single window.
17. **Small JMAP gaps:**
    - Truncated bodies over 2 MB are shown without a notice and never refetched (S; already "not done" in JMAP-COMPLIANCE.md)
    - Quota is fetched (`jmap.rs` `quota()`) but never displayed: storage usage on the Account page (S)
    - Images referenced as `cid:` in a re-opened draft: ROADMAP still lists them as placeholders, while commit b9b3e99 says they are restored. Needs a re-check (S).

## P2: settings shown but not wired, plus layout and theming

1. **Unwired settings (S each, M in total).** These rows appear in Settings, but nothing in the code reads them:
   - `showAvatarsInJunk`
   - `unreadBadgeAllAccounts`, `notificationSoundChoice`, `pushRelayUrl`
   - `dateLocale`, `showOnboardingOnNewDevices`
   - `mailLayout` (reading pane right/bottom/off), `toolbarPosition`, `sidebarAccountSwitcher`, `showRailAccountList`, `showSharedMailAccounts`, `compactSharedMailboxes`, `keepAppsLoaded`
   - `listColumns`, `messageListOrderScope`
   - `attachmentImagePreviewsEnabled`
   - `recipientMentionsEnabled`, `mentionAddsRecipientTo`, `rtlEditingSupport`, `replyUnderMessage`
   - `nestedTags`
   - `debugMode`, `settingsSyncDisabled` (settings sync through Mailbox metadata, lib/settings-sync.ts, is not implemented)

   These settings-store fields have no setting row and no code at all:
   - `defaultReplyMode`, `printTheme`
   - `hiddenCompanyCalendars`, `birthdayCalendarColor`
   - `senderFavicons`, `hiddenSharedMailAccounts`, `sharedMailAccountOrder`, `allMailFolderIds`
   - `attachmentReminderKeywords`, `emailKeywords`
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

- **Distribution (L):** nothing exists yet. There is no `.github/` folder, so no CI release workflow (tag `v*`, Windows and Ubuntu builds, checksums, signing). No MSI (WiX), portable zip, AppImage or .deb.
- **Auto-updater (M):** no signed manifest check, background download, signature verification, install on next start, or channel/auto-check settings.
- **Tray icon (M):** no unread badge, Open/Compose/Check mail/Quit menu, close to tray, start minimised or start at login. The unread count currently goes only into the window title.
- **Notifications (M):** notify-rust only fires a plain new-mail toast. There is no app ID or grouping, clicking does not open the message, and there are no Archive/Mark read actions and no calendar reminders.
- **Linux platform layer (M/L):** only 7 `cfg` sites. WebView2, Credential Manager, the registry theme check and the Windows spell checker are not behind a platform layer, and there is no Secret Service, WebKitGTK path or portal colour scheme.
- **Rich-text composer leftovers (S):** IME is unverified with a real IME, and the pre-edit text covers the text after the caret instead of pushing it aside.
- **Polish font pass:** the memory note marks it done on 2026-11-16 (Skia renderer, Segoe UI, run-through by driving the real window with drive.ps1). Still open: a side-by-side check of font sizes and weights against Bulwark per view, redone through the hidden automation channel since drive.ps1 is now forbidden.
- **ROADMAP.md is out of date:** its "Later" items (unified inbox paging and unread count, accessibility page, calendarMonthTimedEvents, hover-actions corner, Sieve vacation-only scripts and hand-written blocks) were all done in commits 54d75d7, 9482bdd, 8b501d4 and 04f7b96, but are still listed. The "In progress" context-menus line is also stale.

Files read: `C:/Users/user/Downloads/webmail-main/desktop/ROADMAP.md`, `C:/Users/user/Downloads/webmail-main/desktop/JMAP-COMPLIANCE.md`, `C:/Users/user/Downloads/webmail-main/desktop/src/settings.rs`, `C:/Users/user/Downloads/webmail-main/desktop/src/server_settings.rs`, `C:/Users/user/Downloads/webmail-main/desktop/src/main.rs`, `C:/Users/user/Downloads/webmail-main/desktop/src/ctxmenu.rs`, `C:/Users/user/Downloads/webmail-main/desktop/ui/app.slint`, `C:/Users/user/Downloads/webmail-main/desktop/ui/settings-schema.json`, `C:/Users/user/Downloads/webmail-main/stores/settings-store.ts`, `C:/Users/user/Downloads/webmail-main/lib/keyboard-shortcuts/`.
