# Barbican roadmap

## In progress
- Context menus: all 21 Bulwark menus (mail, calendar, contacts, files, settings) on one native component.
- Composer layout matches Bulwark (done); the editor is still a markup text box until the rich-text editor below lands.

## Done: native rich-text composer (src/rte)
What you see is what is sent. Document model (paragraph, H1–H3, quote, code block, nested bullet/numbered lists, tables, images, the collapsible quoted original), marks (bold, italic, underline, strike, code, link, colour, highlight, font size), undo/redo, tolerant HTML import and email-safe HTML export (inline styles, Bulwark's spacing) plus text/plain. Layout with parley, the text engine Slint itself uses, so runs measure exactly as drawn. Caret, selection, click/drag/double/triple click, keyboard navigation, TipTap shortcuts, Markdown input rules, clipboard (HTML, text, images, image files), dropped files, inline `cid:` images, link popover and bubble, table menu, floating selection toolbar, rich signatures and templates, AI rewrite of the selection.
Also done: right-click menu (spelling suggestions, clipboard, Format/Paragraph/Table flyouts, link actions, image size, AI rewrite, undo/redo); spell checking with the Windows spell checker (ISpellChecker) in the writing language, dotted red underlines, Add to dictionary / Ignore, setting "Underline misspelled words" (`spellCheck`, on by default); image resize handles (corners, aspect kept, `width` attribute plus CSS width in the sent HTML); HTML source view (`</>` in the toolbar; the quoted original stays out of it and is kept); IME pre-edit: the editor's FocusScope holds a TextInput at the caret that receives the composition events (Slint draws the pre-edit text there and places the candidate window), while `capture-key-pressed` keeps every key press for the editor.
Still open:
- IME is not verified with a real IME (the hidden automation channel cannot inject composition events); the pre-edit text covers the text after the caret instead of pushing it aside.
- Images referenced as `cid:` in a re-opened draft show as placeholders.

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
