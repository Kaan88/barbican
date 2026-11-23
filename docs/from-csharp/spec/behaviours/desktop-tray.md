# Behaviours: desktop, tray and notifications (B-DSK)

Area: tray icon and menu, unread badge (tray and taskbar overlay), close to tray, start at
login, quit with draft, notifications and their actions, mailto registration and the
single-instance handoff.
Rust sources (`rust-final`): `desktop/src/desktop.rs`, `notify.rs`, `main.rs` (handoff),
`platform/win.rs` (APP_ID, HKCU keys). Bulwark refs: `components/desktop/desktop-os-host.tsx`,
`lib/desktop/os-summary.ts`, `lib/desktop/mail-notifications.ts`, `lib/desktop/reminder-notifications.ts`.

| ID | Rule | Source (Rust) | Bulwark ref | C# status |
|---|---|---|---|---|
| B-DSK-001 | Tray icon with an unread badge. Menu: Open, Compose, Check mail, Quit. | desktop.rs header (lines 1-5); commit 440ebb6; `tray_command` :218 | desktop-os-host.tsx | pending (M8) |
| B-DSK-002 | Tooltip text: "Barbican" (0 unread), "Barbican: 1 unread message", "Barbican: N unread messages". | desktop.rs:195 `tooltip`; test `tooltips` (:460) | desktop-os-host.tsx | pending (M8) |
| B-DSK-003 | Taskbar overlay badge (Windows) shows the unread count as a 32 px overlay; it is refreshed only when the count changes. | desktop.rs:187-210 `inbox_unread`, `refresh_badge`; `UNREAD` cell | n/a | pending (M8) |
| B-DSK-004 | Close to tray: closing the window hides it; Quit from the tray exits. | commit 440ebb6 | desktop-os-settings | pending (M8) |
| B-DSK-005 | Start in the tray at sign-in: the window starts hidden only when started at login with the tray on. | desktop.rs:50 `start_hidden_for`, :55 `start_hidden`; test `hidden_start_only_at_login_with_tray` (:447) | desktop-os-settings | pending (M8) |
| B-DSK-006 | Start at login: Windows HKCU Run value; Linux autostart file; the argument `--autostart` marks a login start. | platform/mod.rs:177 `AUTOSTART_ARG`; platform/win.rs:7 `RUN_KEY`; test `autostart_plans` (platform:244) | desktop-os-settings | pending (M8); live L-051 |
| B-DSK-007 | Quit saves an open draft first. A blank composer saves no draft. A second quit within a minute closes without saving when the save failed or is impossible. | desktop.rs:145 `quit_saving_draft`; commits d4d2fad, 110ff28; b7b4777 | composer quit | pending (M8) |
| B-DSK-008 | Settings rows for the desktop app are built here (`settings_rows`), and a changed setting is applied in `setting_changed`. | desktop.rs:377, :415, :429 | desktop-os-settings | pending (M8) |
| B-DSK-009 | Mail that arrives together becomes one notification, grouped per account (group `mail:<account>`). | notify.rs:52 `mail_note`; tests `one_message` (:227), `batch_opens_inbox` (:252) | mail-notifications.ts | pending (M8) |
| B-DSK-010 | Notification buttons: Archive, Mark as read, Delete. A click opens the message. | notify.rs:32 `encode`, :36 `decode`; test `activation_round_trip` (:262); commit 440ebb6 | mail-notifications.ts | pending (M8) |
| B-DSK-011 | Toast XML escapes all text. | notify.rs:204 `toast_xml`; test `toast_xml_escapes` (:243) | n/a | pending (M8) |
| B-DSK-012 | Activations come back on the UI thread through the handler installed by main. | notify.rs header; :110 `set_handler`, :114 `activate` | n/a | pending (M8) |
| B-DSK-013 | Reminder notifications for calendar alerts use the same notifier. | notify.rs:90 `reminder_note`; desktop.rs:29 `set_event_handler`; commit 440ebb6 | reminder-notifications.ts | pending (M8) |
| B-DSK-014 | Do not disturb is honoured for notifications. | commit 440ebb6 | mail-notifications.ts | pending (M8) |
| B-DSK-015 | Sound: emailNotificationSound and calendar notification sound are separate; pushNotifyInboxOnly limits push notifications to the inbox. | commit aaa3891; reminders.rs header | notification settings | pending (M8) |
| B-DSK-016 | Automation records notifications instead of showing them. | commit 440ebb6; `SHOWN` notify.rs:107 | n/a | pending (M8) |
| B-DSK-017 | The Windows toast app id is `Barbican.Mail`. | platform/win.rs:12 `APP_ID`; notify.rs:147 `app_id` | wix (Start menu shortcut) | pending (M8) |
| B-DSK-018 | Default mail app: register and unregister write HKCU keys; the registration is repaired at startup when the exe moved; unregister is in Settings. | commits 63fa780, 110ff28; test list in PARITY-ROUND2 | mailto handling | pending (M8); live L-001 |
| B-DSK-019 | mailto: links open the composer with to, cc and subject; fragments are stripped; `is_default` is cached. | commit 63fa780; commit 110ff28; commit 0530d4a | mailto | pending (M3) |
| B-DSK-020 | A top-level mailto navigation (meta refresh in mail HTML) is dropped; only a clicked link opens the composer. | commit 110ff28 | mailto | pending (M3) |
| B-DSK-021 | Links that arrive while signed out are kept until sign-in, with a toast. | commit 110ff28 | mailto | pending (M3) |
| B-DSK-022 | Single-instance handoff: the server greets first, so a stale port owned by another program never receives the token or the link; per-connection threads with short deadlines; no listening without randomness; the instance file is removed on clean exit. | commit 110ff28 | n/a | pending (M8) |
| B-DSK-023 | The unread count in the title can include other accounts (unreadBadgeAllAccounts). | commit 585aea2 | unread badge | pending (M8) |
| B-DSK-024 | Windows default-app and autostart changes are OS changes: they run only with the user's OK. | CLAUDE.md "No OS changes without the user's OK" | n/a | pending (M8); hand test 8 |
| B-DSK-025 | Start at login was implemented but not exercised in Rust. | GAPS "Tray and notifications"; PARITY-ROUND2 | n/a | open: G-089 |
