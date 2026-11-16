# Barbican roadmap

## In progress
- Context menus: all 21 Bulwark menus (mail, calendar, contacts, files, settings) on one native component.
- Composer layout matches Bulwark (done); the editor is still a markup text box until the rich-text editor below lands.

## Next: native rich-text composer (parity or better with Bulwark's TipTap editor)
Goal: what you see is what is sent. No markup symbols while typing.
- **Native editor component**, built on Slint primitives, with no web code:
  - a document model of blocks (paragraph, heading 1–3, quote, code block, list item, table cell, image) holding inline runs (bold, italic, underline, strike, code, link, colour, highlight, font size);
  - our own layout and painting of styled runs, caret, selection, IME composition, clipboard (HTML and plain text in and out), and undo/redo history.
- **Bulwark's full toolbar, working for real:**
  - font size, B, I, U, S, text colour, highlight, H1 and H2;
  - bullet and numbered lists with nesting and Tab/Shift+Tab;
  - quote, code, alignment, link (with an edit popover), table (insert, add/remove row and column), clear formatting, undo and redo.
- **Keyboard shortcuts:** Ctrl+B/I/U/K/Z/Y. Markdown shortcuts as you type (`**`, `# `, `- `, `1. `, `> `, backticks).
- **Inline images:** paste or drop, sent as `cid:` attachments.
- **Signatures and templates:** inserted as rich blocks.
- **Replies:** the quoted message is kept as a collapsible block.
- **Round trip:** HTML in (reply, forward, drafts, templates) → model → clean, email-safe HTML out (inline styles) plus a generated text/plain part. The text/plain-only mode stays.
- **Better than Bulwark:** a selection toolbar that floats over selected text, spell-check underlines through the Windows spell-check API (Hunspell on Linux), AI rewrite of the selection only, and a source view for HTML.

## Next: distribution (Windows + Linux)
- **CI release workflow** (GitHub Actions): on tag `v*`, build and test on `windows-latest` and `ubuntu-latest`, attach artifacts to a GitHub Release, generate checksums and sign them.
- **Package types**
  - Windows: MSI (WiX, per-user install, Start menu entry), plus a portable `.zip`. Code-sign when a certificate is available.
  - Linux: AppImage (one file, works on any distro, can update itself), `.deb` for Debian/Ubuntu, and Flatpak later if wanted. Note: the Linux email view needs WebKitGTK through wry instead of WebView2.
- **Auto updater**: check the GitHub Releases feed for a signed update manifest (minisign/ed25519 key built into the app), download in the background, verify the signature, and install on next start. MSI on Windows; on Linux, replace the AppImage in place. For `.deb`, only notify, since the package manager owns the files. Settings: check automatically on/off, and an update channel (stable or beta).
- **Tray icon** (Windows notification area, Linux StatusNotifierItem/AppIndicator): unread badge; a menu with Open, Compose, Check mail and Quit; "close to tray" and "start minimised" settings; optional start at login.
- **Notifications**: Windows toast with an app ID for grouping and sounds; Linux via freedesktop notifications (D-Bus). Clicking a notification opens the message. Actions where the platform supports them: Archive, Mark read.
- Platform layer: put WebView2-only code, the Credential Manager keyring and the Windows registry theme check behind a `cfg(target_os)` layer. Linux uses Secret Service and the portal colour-scheme setting.

## Later
- Unified inbox: paging, unread count, per-account push.
- Remaining settings: accessibility page, calendarMonthTimedEvents and similar, hover-actions corner mode.
- Sieve: vacation-only scripts without metadata; keep hand-written blocks placed between generated ones.
