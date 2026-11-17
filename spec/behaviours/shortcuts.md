# Behaviours: keyboard shortcuts (B-KEY)

Area: the key resolver for every app (mail, calendar, contacts, files, composer), the `g`
sequences, list keys and the `?` help sheet. The per-key table for each app is in
`spec/shortcuts.md` (plan §2.3); this file holds the rules and their tests.
Rust sources (`rust-final`): `desktop/src/shortcuts.rs` (resolver, `help_rows`, key constants at
:18-27, tests :701-848). Bulwark refs: `lib/keyboard-shortcuts/{mail,calendar,contacts,files}.ts`,
`hooks/use-keyboard-shortcuts.ts`, `lib/calendar-shortcuts.ts`, `components/command-palette/command-palette.tsx`,
`components/keyboard-shortcuts-modal.tsx`.

Notation: `Ctrl`, `Shift`, `Alt`. Keys in the table are Rust `resolve()` cases. Keys that
resolve to Pass fall through to the older mail handler in main.rs (shortcuts.rs header, line 10).

| ID | Rule | Source (Rust) | Bulwark ref | C# status |
|---|---|---|---|---|
| B-KEY-001 | Ctrl+K opens the command palette in every app except the composer. | shortcuts.rs:111-120 `resolve`; test `palette_and_help_everywhere` (:716) | command-palette.tsx | pending (M2) |
| B-KEY-002 | Shift+Alt+A toggles the accessibility page in every app. | shortcuts.rs:116 (A11y); test :716 | accessibility | pending (M2) |
| B-KEY-003 | F5 and F9 refresh in every app; Ctrl does not apply to them. | shortcuts.rs:117 (Refresh); test :720 | n/a | pending (M2) |
| B-KEY-004 | Ctrl+Shift+M opens a new message in every app. | shortcuts.rs:118 (Compose) | Bulwark compose | pending (M3) |
| B-KEY-005 | `?` opens the help sheet in every app, except in the calendar event editor. | shortcuts.rs:119 (Help); test `single_keys_off_leaves_named_keys` (:729) | keyboard-shortcuts-modal.tsx | pending (M2) |
| B-KEY-006 | Single-key shortcuts can be switched off (singleKeyShortcuts). Named keys and Ctrl combinations still work when off. | shortcuts.rs:121 `single_keys`; test :729 | keyboard-shortcuts | pending (M2) |
| B-KEY-007 | Composer: Ctrl+Shift+C shows Cc; Ctrl+Shift+B shows Bcc. Every other key passes through to the editor. | shortcuts.rs:112-115; test `composer_keys` (:772) | email-composer.tsx capture handler | pending (M3) |
| B-KEY-008 | Mail: Shift+Down (or Shift+J) extends the selection down; Shift+Up (or Shift+K) extends it up. Plain arrows pass. | shortcuts.rs:138-171 `mail`; test `mail_keys` (:755) | use-keyboard-shortcuts.ts | pending (M2) |
| B-KEY-009 | Mail: Enter or o opens; x toggles selection; g starts a sequence. | shortcuts.rs:150-165; test `mail_keys` (:755) | mail shortcuts | pending (M2) |
| B-KEY-010 | Mail single keys on an open message: r reply; R or a reply-all; f forward; s star; e archive; # or Delete delete; u mark unread; I mark read; ! spam. | shortcuts.rs:172-178 `mail_key_action`; test `mail_keys_never_flip_and_backspace_does_not_delete` (:704) | mail shortcuts | pending (M2) |
| B-KEY-011 | Mail: Ctrl+F forwards when a message is selected, and searches otherwise. Ctrl+Shift+F always forwards (Windows desktop key). Ctrl+E focuses search. | shortcuts.rs:140-145; commit 8253677 | desktopPcKey (Bulwark) | pending (M2) |
| B-KEY-012 | Mail: Ctrl+Q marks as read (Windows desktop key). Ctrl+Z undoes. Ctrl+Q, Shift+I and u never toggle state. | shortcuts.rs:146-147; commit d16233d | desktopPcKey | pending (M2) |
| B-KEY-013 | Mail: `g` followed by i, s, t, d or a goes to Inbox, Starred, Sent, Drafts or All. A `g` followed by a shifted letter passes. The `g` arming is timed (G_AT in shortcuts.rs). | shortcuts.rs:150-158 (`g_pending`); test `g_sequences` (:739) | use-keyboard-shortcuts.ts | pending (M2) |
| B-KEY-014 | Mail: `a` (unadvertised alias of Shift+R) is reply-all when a message is selected. | shortcuts.rs:166-167; comment cites Bulwark | Bulwark alias | pending (M2) |
| B-KEY-015 | Mail: Backspace does not delete a message. | shortcuts.rs test `mail_keys_never_flip_and_backspace_does_not_delete` (:704); commit d16233d | n/a | pending (M2) |
| B-KEY-016 | Calendar: Left and Right move one period; t goes to today; d, w, m switch to Day, Week, Month; n or c make a new event; e edits; / focuses search; Ctrl+R refreshes. | shortcuts.rs:180-202 `calendar`; test `calendar_keys` (:779) | lib/calendar-shortcuts.ts | pending (M4) |
| B-KEY-017 | Calendar editor open: Ctrl+Enter saves; Esc closes; other keys pass (so typing works). | shortcuts.rs:183-188; test `calendar_keys` (:789-792) | event-modal.tsx | pending (M4) |
| B-KEY-018 | Contacts list: Down, Up, Home, End move; Enter opens; Delete or Backspace deletes the contact; Esc clears; Shift+F10 opens the menu; / focuses search. | shortcuts.rs:204-220; test `list_keys` (:796) | contact-list.tsx | pending (M5) |
| B-KEY-019 | Files list: Down, Up, Home, End move; Enter opens; F2 renames; Delete or Backspace deletes; Ctrl+C, Ctrl+X, Ctrl+V copy, cut, paste; Alt+Up goes up a folder; Shift+F10 opens the menu; Esc clears. | shortcuts.rs:222-250; test `list_keys` (:796-807) | file-browser.tsx | pending (M5) |
| B-KEY-020 | Keys that act on an empty selection pass (no delete, no open with nothing selected). | shortcuts.rs test `list_keys` (:807-808) | n/a | pending (M5) |
| B-KEY-021 | List stepping: from no selection, Move(+1) selects the first row and Move(-1) the last; from a missing row Move goes to the first; Last on an empty list selects nothing. | shortcuts.rs:602 `step`; test `stepping_through_a_list` (:812) | n/a | pending (M2) |
| B-KEY-022 | Key constants: arrows, Home, End, Esc, Delete, Backspace, F-keys are private-use codepoints (U+F700 and up) as the platform sends them. | shortcuts.rs:18-27 | n/a | pending (M2) |
| B-KEY-023 | Help sheet rows use Bulwark's notation ("Ctrl + R / r"); alternatives are split on " / ". Rows Barbican does not implement are left out. | shortcuts.rs:370 `alternatives`, :384 `help_rows`, :824 test `help_rows_split_keys`; header comment before MAIL_HELP | keyboard-shortcuts-modal.tsx | pending (M2) |
| B-KEY-024 | Single-key detection: a key is single if it is a printable character without Ctrl or Alt. | shortcuts.rs:105 `is_single_key`; test `single_key_detection` (:841) | n/a | pending (M2) |
| B-KEY-025 | Keys pressed while the email view has focus are replayed in the app. A click on the native UI takes the OS focus back. | commit 7a60ae8 | n/a | pending (M1/M2); live L-047 |
| B-KEY-026 | After an overlay closes, focus returns to the key handler. | commit 7a60ae8 | n/a | pending (M2); live L-047 |
| B-KEY-027 | Known bug: after the rail switches from Calendar back to Mail, Ctrl+K and ? do nothing until the content area is clicked. | PARITY-ROUND2 "Lost keyboard focus" (observed) | n/a | open: L-047 |
| B-KEY-028 | Focus is not stolen by the hidden WebView2 (focus_parent). | commit b0dd3bd | n/a | pending (M1) |
