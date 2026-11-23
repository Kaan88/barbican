# Changelog

Each release of Barbican is listed here, newest first. The section for a version is the text of its
GitHub release, its first line is the note Hub shows, and a release is refused when the version
has no section here. Before tagging, rename "Unreleased" to the version and the date, for
example "## 0.1.0 — 2026-12-01", and start a new "Unreleased" above it.

## Unreleased

- Clicking a new-mail notification now shows the folder the message is in, not only the message: the folder is selected in the list on the left (and unfolded when it sits inside a collapsed folder), its messages are listed and the message is highlighted. Before, the folder that was open before stayed selected, so the message and the folder disagreed. A message that was filed to another folder since the notification is found where it is now, a deleted one only says it is gone, a search or tag is cleared so the message is in the list, and the "All inboxes" view gives way to the message's own folder (as Bulwark does). Notifications for several new messages select the Inbox of the account that got them the same way. A notification of another signed-in account switches to that account first (as Bulwark does) and then opens the message in its folder (or the Inbox); accounts are matched by the login you signed in with, also when the server's own name for the account differs from it. If you open another folder or message while the message is being looked up on the server, you stay where you are. A search that already lists the message is kept. The Archive, Mark as read and Delete buttons on a notification of another account now say that it belongs to another account instead of doing nothing.

## 0.1.0 — 2026-11-23

- First installable build: a per-user Windows installer (Inno Setup, Barbican-Setup-<version>.exe), next to the portable zip, with a Start menu shortcut, an optional desktop shortcut and upgrade in place. It needs no administrator rights, and it never ends a running Barbican (including one only in the tray): it asks it to close, waits up to 30 seconds, and when it is still there it asks you to close Barbican first instead of installing, so an open draft is not lost. Uninstalling also removes the registry entries the app wrote for itself (start at login, the mailto: registration).
- barbican.exe carries a Windows version resource and the application icon, so Explorer and Hub show the right version.
- Updates: an install made with the new installer can update itself like the MSI one does (signed manifest, SHA-256 check, installed only when you say so).
- Mail, calendar and contacts in the Bento × Nordic design, with a native rich-text composer (spelling, tables, images), a unified inbox, schedule send, invitations, filters and Sieve extras, a command palette and search, drag and drop, and .eml/.mbox import.
- Settings include an accessibility page; the app can start in the tray, close to the tray and show an unread badge.
