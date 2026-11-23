# Changelog

Each release of Barbican is listed here, newest first. The section for a version is the text of its
GitHub release, its first line is the note Hub shows, and a release is refused when the version
has no section here. Before tagging, rename "Unreleased" to the version and the date, for
example "## 0.1.0 — 2026-12-01", and start a new "Unreleased" above it.

## Unreleased

- The window reopens where you left it: its size, position and whether it was maximised are remembered on this computer (not synced with your settings). When the monitor it was on is gone or the screen changed so the window would be off screen, Barbican opens at the default size and place instead.

## 0.1.0 — 2026-11-23

- First installable build: a per-user Windows installer (Inno Setup, Barbican-Setup-<version>.exe), next to the portable zip, with a Start menu shortcut, an optional desktop shortcut and upgrade in place. It needs no administrator rights, and it never ends a running Barbican (including one only in the tray): it asks it to close, waits up to 30 seconds, and when it is still there it asks you to close Barbican first instead of installing, so an open draft is not lost. Uninstalling also removes the registry entries the app wrote for itself (start at login, the mailto: registration).
- barbican.exe carries a Windows version resource and the application icon, so Explorer and Hub show the right version.
- Updates: an install made with the new installer can update itself like the MSI one does (signed manifest, SHA-256 check, installed only when you say so).
- Mail, calendar and contacts in the Bento × Nordic design, with a native rich-text composer (spelling, tables, images), a unified inbox, schedule send, invitations, filters and Sieve extras, a command palette and search, drag and drop, and .eml/.mbox import.
- Settings include an accessibility page; the app can start in the tray, close to the tray and show an unread badge.
