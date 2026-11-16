# Behaviours: settings (B-SET)

Area: the schema-driven settings pages, prefs storage, backup, server-backed pages, appearance,
accessibility, desktop and update pages. Per-key wiring is in `spec/settings.csv` (S- rows);
keyless rows are in the same file as `S-keyless-###`.
Rust sources (`rust-final`): `desktop/src/settings.rs`, `server_settings.rs`, `a11y.rs`,
`desktop.rs` (settings rows), `ui/settings-schema.json` (embedded verbatim).

Schema facts (checked by `settings_gen`, see the settings.csv notes): 21 tabs, 275 rows
(section 74, toggle 91, select 63, info 39, text 6, button 2), 137 keyed rows over 136 distinct
keys (`plainTextFont` appears twice), 64 keyless non-section rows. The PORT-PLAN figure "201
keys" does not match the embedded file: the file has 136 distinct keys. The 201 figure is not
reproduced by any count of this schema and is flagged in the Fable log.

| ID | Rule | Source (Rust) | Bulwark ref | C# status |
|---|---|---|---|---|
| B-SET-001 | The schema is embedded verbatim and parsed once per process. | settings.rs:9 `SCHEMA` (include_str); commit 0530d4a (parsed once) | components/settings/*, settings-store.ts | pending (M6) |
| B-SET-002 | Values live in prefs.json under the same keys as Bulwark's settings store. | settings.rs header (lines 1-3) | stores/settings-store.ts | pending (M6) |
| B-SET-003 | Settings are read once per open; CUR_MSG-style caches hold Arcs, not deep clones. | commit 0530d4a | n/a | pending (M6) |
| B-SET-004 | A save writes off the UI thread. `LATEST` serves reads until the write lands; the newest save wins; a superseded write is skipped. | settings.rs:136-158 `save` | n/a | pending (M6) |
| B-SET-005 | On exit the pending background save is flushed first. | settings.rs:122 `flush`; commit 0f8bdf5 | n/a | pending (M6) |
| B-SET-006 | Export writes the Bulwark-keyed prefs plus the desktop and assistant settings file. Import accepts Bulwark exports. Reset restores defaults. | settings.rs:160 `export`, :167 `import`, :178 `reset`; test `import_export_reset_roundtrip` (:581); commits 7593933, e1f5bf5 | settings backup | pending (M6) |
| B-SET-007 | Each key has a default (schema `default`); a missing key reads as the default. | settings.rs:191 `bool`, :195 `str`, :200 `int` | schema | pending (M6) |
| B-SET-008 | Changed settings apply live (no restart), including settings changed by the assistant. | commits 594e4e9, 714ad82, a90b362 | settings-store.ts | pending (M6) |
| B-SET-009 | `inverted` toggles store the opposite value. | settings.rs:548 `is_inverted` | schema `invert` | pending (M6) |
| B-SET-010 | Hover actions are a fixed set of five: delete, star, markRead ("Mark as read"), archive, spam. | settings.rs:75 `HOVER_ACTIONS`, :533 `hover_actions` | settings.hoverActions | pending (M6) |
| B-SET-011 | Working days are read from calendarWorkingDays (weekday numbers 0 to 6, out-of-range values dropped); when absent the default is [1,2,3,4,5] (Monday to Friday). | settings.rs:541 `working_days`; commit 9482bdd | calendar working days | pending (M6) |
| B-SET-012 | 12-hour or 24-hour time comes from `h12()`, which honours a pending save. | settings.rs:627 `h12`; commit 0f8bdf5 | timeFormat | pending (M6) |
| B-SET-013 | Calendar day end offers the hour options; the value is validated. | commit 38fa42d | calendarDayEndHour | pending (M6) |
| B-SET-014 | Search across settings pages matches row labels and descriptions. | settings.rs:442 `search`; commit 3cbd790 | settings search | pending (M6) |
| B-SET-015 | Archive can be set per year or per month; the placeholder warning shows when a template has a placeholder that is not filled. | commit ffb971b | archive settings, compose | pending (M6) |
| B-SET-016 | The window title shows the unread count when the setting is on. | commit ffb971b | unread count | pending (M8) |
| B-SET-017 | Accessibility page (Bulwark ACCESSIBILITY_DEFAULTS) is applied natively: font size, line and letter spacing, word and paragraph spacing, reading width, contrast, focus ring, large targets, underline links, colour-blind palette, visual alerts, toast duration, unread count style, message text scale, message view mode. | a11y.rs:8-25 (settings keys); commit 9482bdd | accessibility-settings.tsx | pending (M6) |
| B-SET-018 | Reduce motion is `system`, `on` or `off`; system reads Windows animation effects. | a11y.rs:11 `reduceMotion`; motion.rs header | accessibility | pending (M6); see B-MOT-001 |
| B-SET-019 | Message text is restyled per setting: mail font, colours override, minimum font size and "fit wide messages". | a11y.rs:14 (`mailFitWidth`, `mailMinFontSize`, `mailOverrideStyles`) | accessibility | pending (M6) |
| B-SET-020 | Single-key shortcuts can be turned off; named keys still work. | a11y.rs:12 `singleKeyShortcuts`; shortcuts.rs test `single_keys_off_leaves_named_keys` (:729) | keyboard-shortcuts | pending (M6) |
| B-SET-021 | Send confirmation is a toggle; when on, the send asks first. | compose.rs:868; a11y.rs (sendConfirmation); commit 714ad82 | sendConfirmation | pending (M6) |
| B-SET-022 | Server-backed pages: Out of office (VacationResponse), Filters (Sieve), Folders (Mailbox), Identities and signatures (Identity). | server_settings.rs header (lines 1-3) | settings sieve/vacation/identities | pending (M6) |
| B-SET-023 | Server edits are written back to the server; text edits are debounced. | server_settings.rs header | settings | pending (M6) |
| B-SET-024 | Appearance: style (bento or nordic, applied live), theme (light, dark, system), more-contrast. | commits 71349b3, 4e77941 | appearance-settings.tsx | pending (M6) |
| B-SET-025 | Calendar colours for calendars are one of 14 fixed colours. | server_settings.rs:48 `CAL_COLORS` | calendar colour picker | pending (M6) |
| B-SET-026 | Email notifications: emailNotificationSound and pushNotifyInboxOnly. | commit aaa3891 | notification settings | pending (M6) |
| B-SET-027 | Unified mailbox ("All inboxes") is switched live with enableUnifiedMailbox. | main.rs:1005; unified.rs; commit 6fa18ef | unified inbox | pending (M2) |
| B-SET-028 | Settings > Desktop app and About > Updates pages exist; the update channel and automatic checks are set there. | commit 440ebb6; updater.rs:293 | desktop-update-settings.tsx | pending (M8) |
| B-SET-029 | Tags: create, rename and delete write the shared settings file. | PARITY-ROUND2 "Mail extras" partial | tags settings | pending (M2); live L-044 |
| B-SET-030 | Schema rows with no key (info, some text and toggle rows: Security, Language, Time zone, Default apps, Contacts import and export, Files display, Storage usage, Vacation audience, forward and keep) are dropped by `schema_rows`. Each must get a handler or an info provider in the port. | GAPS cross-cutting finding 1; settings.csv `S-keyless-###` | settings | pending (M6); G-069 |
| B-SET-031 | 136 keyed schema keys: 110 have a literal read in Rust, 26 have none (listed in settings.csv with status unwired). | settings.csv (`settings_gen`) | n/a | open: G-059 to G-064 |
| B-SET-032 | Mail list settings applied in Rust: group by date, previews, date and time format, emails per page, mark-as-read delay, delete action, bulk-delete confirmation. | commit 82e3890 | message-list settings | pending (M2) |
| B-SET-033 | Density (regular, compact) and roomier compact rows; list density setting. | commits db57ec6, 594e4e9 | density | pending (M2) |
| B-SET-034 | Calendar settings wired: firstDayOfWeek, showWeekNumbers, calendarLimitHours, calendarDayEndHour, hide non-working days, showTimeInMonthView, calendarTintedEvents. | commit e1886a6 | calendar settings | pending (M4) |
| B-SET-035 | Contacts sort by last name is applied live. | commit a90b362 | contacts settings | pending (M5) |
| B-SET-036 | Reading settings applied in Rust: messageSpacing, plainTextFont, hideInlineImageAttachments, mailAttachmentAction (preview or download). | commit 936aaa9 | reader settings | pending (M2) |
| B-SET-037 | Replies under the message (replyUnderMessage) dock the reply under the message; the split is saved as replyDockShare and resets on double-click. | commit 585aea2 | reply settings | pending (M3) |
| B-SET-038 | Unread badge across all accounts is set with unreadBadgeAllAccounts; refreshed with the folders and every 2 minutes. | commit 585aea2 | unread badge | pending (M8) |
| B-SET-039 | Assistant settings and assistant history are per account. | commit a1cb1d6 | assistant | pending (M6) |
| B-SET-040 | Sender avatars (Gravatar, BIMI, favicons) are off by default and cached; senderFavicons and gravatarAvatars are separate toggles. | commit 400fd6e; schema | avatars | pending (M2) |
| B-SET-041 | Tray and desktop rows: `desktopOs` and `desktop_updates`. | desktop.rs:377 `settings_rows`, :415 `setting_changed`, :429 `settings_button` | desktop-os-settings | pending (M8) |
| B-SET-042 | A row button (for example avatars.refresh, a11y.reset, export, import, reset) runs an action; the action list is fixed in the Rust code. | desktop.rs:429; main.rs:1091 (a11y.reset) | settings buttons | pending (M6) |
