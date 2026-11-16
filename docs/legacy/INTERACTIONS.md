# Bulwark → Barbican interaction inventory

**Method.** Read-only audit, Nov 2026. Each Bulwark component in `components/` (and `lib/keyboard-shortcuts/`) was read for every user interaction (click, right-click, double-click, hover, drag, keys, passive display that the user acts on). For each one, the Barbican main checkout (`desktop/ui/app.slint`, `src/main.rs`, `src/ctxmenu.rs`, `src/mail.rs`, `src/settings.rs`, `ui/settings-schema.json` and siblings) was searched for an equivalent. Nothing was built or run, so "done" means the code path exists, not that it was clicked through.

Status: **D** done, **P** partial (exists but narrower, or a setting that is saved but not read), **M** missing, **N** n/a (phone/touch/browser-only). Rows that bundle several closely related Bulwark interactions are grouped into one line.

Cross-cutting causes behind many rows:
- `src/main.rs:1446` returns early when `app != 0`, so Calendar, Contacts and Files get no keyboard shortcuts at all, and Ctrl+A always selects mail.
- No multi-select, drag-and-drop, resize handles, preview modal, command palette, global search, shortcuts help, notification centre, undo toasts outside mail, or pop-out windows.
- `schema_rows` drops every keyless settings row; about 30 keyed settings are saved but never read (marked P).

On the user's example: the bottom-left avatar opens a popup (`ui/app.slint:1466-1505`) with the account list (switch, check on the active one) and "Add account". Missing: sign out, sign out of all, set as default, manage accounts, help links, unified inbox, storage, per-account avatars/unread badges/default star, reordering, avatar tooltip and other-accounts unread dot, Esc and arrow-key navigation.

## Summary

Counts are table rows (grouped interactions), not single Bulwark handlers.

| area | done | partial | missing | n/a |
|---|---|---|---|---|
| Rail and account menu | 4 | 6 | 21 | 1 |
| Sidebar and folder menu | 8 | 5 | 18 | 3 |
| Layout, desktop host, session, auth, lock, offline, tour | 5 | 9 | 31 | 7 |
| Mail: list, rows, selection, keyboard | 9 | 18 | 23 | 0 |
| Mail: menus and message actions | 2 | 8 | 15 | 0 |
| Mail: reader | 4 | 13 | 23 | 0 |
| Mail: composer | 8 | 9 | 19 | 0 |
| Mail shell, search, transfer, templates, identities | 4 | 6 | 16 | 0 |
| Calendar and tasks | 8 | 14 | 27 | 0 |
| Contacts | 7 | 6 | 12 | 1 |
| Files, sync, file drop | 6 | 5 | 13 | 0 |
| Settings, notifications, accessibility, protocol | 17 | 13 | 28 | 0 |
| Command palette, global search, shortcuts help | 0 | 2 | 4 | 0 |
| Admin, connector, plugins, Pro shell | 2 | 5 | 6 | 0 |
| **Total** | **84** | **119** | **256** | **12** |


## Missing and partial, ranked by everyday importance

### Top 40 overall
1. M Account menu: Sign out of this account / Sign out of all (only Settings > Account)
2. M Account menu: Set as default, Manage accounts, per-account unread badges and avatars, Esc to close
3. M Recipient autocomplete and recipient chips in the composer
4. M Calendar, Contacts and Files keyboard shortcuts (main.rs:1446 early return; Ctrl+A always selects mail)
5. P Hover Tag action is broken: no tag picker opens (main.rs:1593)
6. M Shift-click range select and Shift+arrow selection in the mail list
7. M Drag messages onto folders (and tags)
8. P Close/Discard composer with no confirm dialog (silent save / immediate discard)
9. M Schedule send (and Ctrl+Shift+Enter sends immediately instead)
10. M Restore (Trash), Move to Inbox (Archive), Not spam (Junk) in reader toolbar and hover strip; "Delete forever" label
11. M Calendar invitation banner in the reader (Accept/Maybe/Decline, Add to calendar)
12. M Search clear button, search suggestions and recent searches
13. M Move menu filter, Recent folders and "Choose folder…" dialog
14. M Selection bar Move / Tag / More buttons
15. M Pane resize handles (sidebar, list, reader, other apps)
16. M Native notification click opens the message; notification actions
17. M Command palette (Ctrl+K) and `?` keyboard shortcuts help
18. M In-app attachment/file preview modal
19. M Forward as attachment
20. M Unsubscribe banner
21. M Rules submenu: quick filter / block sender from a message
22. M Calendar drag to move/resize events and drag-to-create; click opens detail popover
23. P Week view hour-slot double-click/right-click lands on the anchor date, not the clicked day
24. M Recurrence scope dialog (this / following / all occurrences)
25. M Event participants, invitations, alerts/reminders
26. M Thread inline expand in the list; conversation card expand/collapse and per-card buttons
27. M Contacts and Files multi-select with bulk actions
28. M Files search, sort, view toggle; OS file drop upload; undo on delete
29. M Edit existing filter rules, multiple conditions, reorder
30. M Tag manager (create, colour, reorder, delete tags)
31. M Notification centre (bell)
32. M Read receipt banner and Trust sender
33. M Reader Prev/Next buttons, Open in window, Fullscreen
34. M Avatar tooltip and other-accounts unread dot; Mail rail unread badge; rail tooltips
35. M Sidebar collapse; unified per-role rows and shared mailboxes
36. M Mail import/export; contacts import/export; calendar import and subscriptions
37. M Pop-out windows for messages and composer
38. M Password & sign-in settings page (change password, TOTP, app passwords)
39. P About 30 settings saved but not read (Pause notifications, mail layout, compose placement…)
40. M App lock (PIN / Windows Hello) and the guided tour

### By area
- **Rail and account menu:** see items 1, 2, 34; plus keyboard menu navigation, account reorder, help links, storage popover, notification bell, accessibility button, Tasks app.
- **Sidebar:** folder drop targets, Folders header right-click, sub-unread dot, tag toggle-off, nested tags, vacation banner, folder menu Share/Rules/Mute/Import/Export/mark-subtree-read.
- **Mail list:** see 5, 6, 7, 14, 26; plus Enter/o, Esc deselect, `a`, Backspace, Space to tick, date-group fold, sort menu, category tabs, select-all-in-folder banner, empty-state actions.
- **Mail menus:** see 10, 13, 19, 21; plus Copy to account, Pin, Report phishing, View source, copy links, batch tri-state tags.
- **Reader:** see 10, 11, 20, 32, 33; plus details panel, name popover card, footer reply buttons, quick-reply Cancel/Open full editor, attachment Save to Files / Download all / rename, link phishing gate.
- **Composer:** see 3, 8, 9; plus attachment chip menu, Save as template, priority/DSN, @mentions, image URL/alt, external-recipient confirm.
- **Calendar:** see 4, 22-25; plus Agenda and Tasks views, quick-create, mini-cal month picker, share, print, task modal.
- **Contacts / Files:** see 4, 18, 27, 28; plus contact form structured fields and photo, merge/duplicates, directory, preview, new file, ZIP download.
- **Settings:** see 29, 30, 38, 39; plus account list in Settings, tag manager, filter editor, vacation forwarding/audience, notification sounds, accessibility presets and quick panel.
- **Shell / global:** see 15, 17; plus global search, desktop menu bar, F6 pane cycling, tray, protocol (mailto/webcal) launch, offline mode, plugins UI and Pro shell (lowest priority).


## Full tables

Bulwark paths are under `components/` unless noted; Barbican paths are under `desktop/`.

### Rail and account menu

| st | element / interaction | Bulwark | Barbican note / location |
|---|---|---|---|
| D | Bottom-left avatar click opens account popover | layout/account-switcher.tsx:311 | ui/app.slint:1466-1505 |
| M | Avatar tooltip + other-accounts unread dot | account-switcher.tsx:318-330 | — |
| P | Popover closes on outside click / Esc | account-switcher.tsx:182-193 | outside click only; no Esc |
| M | Popover arrow/Home/End navigation | account-switcher.tsx:87,377 | — |
| D | Account row click switches account | account-switcher.tsx:453 | app.slint:1487, main.rs:672 |
| P | Account row contents (avatar, check, default star, unread badge, server/error line) | account-switcher.tsx:455-500 | address + check only |
| M | Drag-reorder accounts | account-switcher.tsx:442-445 | — |
| M | Alt+Shift+Up/Down reorder accounts | account-switcher.tsx:454 | — |
| M | "Needs sign-in" waiting account row | account-switcher.tsx:522 | — |
| D | Add account | account-switcher.tsx:618 | app.slint:1498, main.rs:684 |
| M | Manage accounts | account-switcher.tsx:632 | — |
| M | Help / accessibility links | account-switcher.tsx:~640 | — |
| M | Set as default | account-switcher.tsx:657 | — |
| M | Sign out of <address> in menu | account-switcher.tsx:666 | only Settings > Account (settings.rs:263) |
| M | Sign out of all accounts | account-switcher.tsx:675 | — |
| M | MV popover: close X, current-account card, Manage your account pill, storage pill | account-switcher.tsx:383-596 | — |
| M | Unified inbox row in menu | account-switcher.tsx:409 | All inboxes is a folder row (mail.rs:167) |
| M | MV popover add/default/sign-out items | account-switcher.tsx:548-578 | add only |
| P | Rail app links + unsaved-changes leave guard | navigation-rail.tsx:693 | no Tasks app, no leave guard |
| M | Mail rail unread badge | navigation-rail.tsx:712-724 | — |
| M | Rail item tooltips | navigation-rail.tsx:716 | `tip` is accessible label only |
| M | Compose on rail (composePlacement) | navigation-rail.tsx:647 | setting inert |
| P | Search rail button / global search | navigation-rail.tsx:671 | list search box only |
| M | Plugin page links / custom sidebar apps / Add app | navigation-rail.tsx:738,777,808 | — |
| P | Admin link with update dot | navigation-rail.tsx:832 | no update dot |
| M | Notification bell | navigation-rail.tsx:854 | — |
| M | Accessibility button (Alt+Shift+A) | navigation-rail.tsx:859 | — |
| D | Settings gear | navigation-rail.tsx:873 | app.slint:1463 |
| M | Keyboard-shortcuts button | navigation-rail.tsx:892/903 | — |
| P | Storage quota ring popover | navigation-rail.tsx:177 | static sidebar tile |
| M | Rail account circles / rail "+" / rail sign-out menu | navigation-rail.tsx:934-992 | — |
| N | Phone bottom tab bar | navigation-rail.tsx:444-583 | — |

### Sidebar and folder menu

| st | element / interaction | Bulwark | Barbican note / location |
|---|---|---|---|
| D | Compose button | sidebar.tsx:2739 | app.slint:1526 |
| M | Collapse/expand sidebar toggle | sidebar.tsx:2719 | — |
| M | Folded rail hover-peek, Esc, Tab trap, arrow keys, All-folders drawer | sidebar.tsx:2607-2640,3115,1679 | — |
| N | Mobile drawer close | sidebar.tsx:2707 | — |
| M | Account switcher in sidebar header; account line under compose | sidebar.tsx:2735,~2810 | — |
| D | Folder row click | sidebar.tsx:507/579 | app.slint:1564-1572 |
| D | Folder chevron expand | sidebar.tsx:553 | app.slint:339 |
| D | Folder row right-click | sidebar.tsx:508 | ctxmenu.rs:228,511 (smaller item set) |
| N | Long-press / ⋮ folder options (touch) | sidebar.tsx:510,687 | — |
| M | Drop messages on folder (move + undo) | sidebar.tsx:1040 | no mail-row drag |
| M | Drag folder to tab | sidebar.tsx:978 | — |
| P | Muted bell icon | sidebar.tsx:656 | shown for unsubscribed, not for mutes |
| M | Collapsed-parent sub-unread dot; name truncation tooltip | sidebar.tsx:~670,~600 | — |
| P | Section header collapse | sidebar.tsx:827 | Folders and Tags only |
| D | Section header gear | sidebar.tsx:857 | app.slint:1559,1599 |
| P | "+" create top-level folder | sidebar.tsx:848 | only in Settings > Organize |
| M | Folders header right-click (mark all read, new folder, refresh) | sidebar.tsx:2074 | — |
| M | Unified section header toggle | sidebar.tsx:2832 | — |
| P | Unified per-role rows | sidebar.tsx:2866 | All inboxes only |
| M | Unified row right-click (empty trash all, refresh) | sidebar.tsx:2867 | — |
| M | Unified loading tooltip; per-account rows; per-account folder groups | sidebar.tsx:2840,2889,2906 | — |
| M | Shared section: header, account rows, right-click, drag reorder, tags gear | sidebar.tsx:2133-2566,1281 | — |
| P | Tag row click (second click deselects) | sidebar.tsx:1181 | no toggle-off |
| D | Tag row right-click | sidebar.tsx:1181 | ctxmenu.rs:236-241 |
| M | Nested tag chevron; Show all N tags | sidebar.tsx:~1190,1084 | — |
| D | Scheduled row | sidebar.tsx:2042 | mail.rs:158 |
| M | MV Starred row, Show more, Labels "+" | sidebar.tsx:3016,3032,3068 | — |
| M | ArrowRight/Left expand selected folder | sidebar.tsx:2375 | — |
| N | Demo banner Tour / Reset | sidebar.tsx:1346,1349 | — |
| M | Vacation banner label / Turn off | sidebar.tsx:1397,1405 | only via AI agent tool |
| D | Folder menu: Mark read, New subfolder, Rename, Refresh, Empty trash/spam, Delete, header | mailbox-context-menu.tsx | ctxmenu.rs:515-529 |
| M | Folder menu: Mark folder and subfolders read | mailbox-context-menu.tsx:356 | — |
| M | Folder menu: Share…, Rules ▸, Mute notifications, New folder (shared), Import email, Export | mailbox-context-menu.tsx:387-434 | — |
| M | Account-scope picker in Contacts/Files sidebars | account-scope-label.tsx:50,70 | — |

### Layout, desktop host, session, auth, app lock, offline, tour, top-level

| st | element / interaction | Bulwark | Barbican note / location |
|---|---|---|---|
| M | Skip to content link | app-landmarks.tsx:13 | — |
| M | F6 / Shift+F6 pane cycling | pane-cycle.tsx:56 | — |
| M | Focus rescue | focus-rescue.tsx | — |
| N | Drawer scrim | edge-drawer.tsx:26 | — |
| M | Pane resize handles (drag, arrows, double-click reset) | resize-handle.tsx:163-166 | fixed widths |
| M | Inline app close; Sidebar Apps modal (add/edit/delete, icon picker) | inline-app-view.tsx, sidebar-apps-modal.tsx | — |
| M | Desktop menu bar (Alt, keys, hover, run item, outside close) | window-menu-bar.tsx:259-490 | — |
| N | Phone header (menu/back, search, compose, account, More) | mobile-header.tsx, phone-header-actions.tsx | — |
| M | Phone More: notifications, command palette, plugin actions | phone-header-actions.tsx:77-84 | — |
| M | MV top bar: hamburger, logo, bell, shortcuts, apps grid, avatar | mv-top-bar.tsx | — |
| P | MV search field | mv-top-bar.tsx:242 | list search box |
| D | MV search filters toggle | mv-top-bar.tsx:251 | main.rs:1491 |
| P | MV Admin | mv-top-bar.tsx:305 | rail |
| D | MV Settings | mv-top-bar.tsx:324 | rail |
| M | MV side panel (calendar/tasks/contacts mini panels, plugin panel) | mv-side-panel.tsx | — |
| M | Share / calendar-change toasts | share-notification-toaster.tsx, calendar-event-notification-toaster.tsx | — |
| P | File > New message | desktop-host.tsx:79 | Ctrl+N / c, no menu |
| P | Edit > Search | desktop-host.tsx:84 | Ctrl+F/E |
| M | File > New event / task / contact | desktop-host.tsx:86-96 | — |
| M | File > Import mail / calendar / contacts; Change connection | desktop-host.tsx:101-120 | — |
| P | File > Add account | desktop-host.tsx:103 | avatar menu only |
| P | Go > apps | desktop-host.tsx:125 | Ctrl+1..4 |
| M | Help > Keyboard shortcuts | desktop-host.tsx:132 | — |
| M | bulwark:// links, Open link, Search mail for selection | desktop-host.tsx:136-151 | — |
| P | Print / Ctrl+P | desktop-host.tsx:217 | context menu Print, no Ctrl+P |
| N | Spellcheck dictionary download notice | desktop-host.tsx:247 | own spell module |
| M | Quit with unsent mail warning | desktop-host.tsx:303 | — |
| P | Native new-mail notification with actions, click opens | desktop-os-host.tsx:40-60 | plain notification, click does nothing |
| M | Tray / jump list | desktop-os-host.tsx:60 | — |
| M | Edited-attachment cards (save to Files, conflicts, reply, draft, discard…) | edited-files-host.tsx | — |
| M | Session timeout dialog + idle guard | session-timeout-*.tsx | — |
| M | Login: server auto-discovery "Change" | login-server-field.tsx:89 | server field always shown |
| D | Login: server input | login-server-field.tsx:119 | app.slint:1388 |
| N | Login: CORS hint | login-server-field.tsx:142 | — |
| M | Login: unsupported-provider notice, language picker, theme menu | login-*.tsx | — |
| N | Mobile hand-off consent | mobile-handoff-consent.tsx | — |
| P | OAuth status action | oauth-status-card.tsx:103 | status text, no action button |
| D | Remember me / 2FA / client ID (Barbican extras) | — | app.slint:1393-1424 |
| M | App lock: PIN setup, unlock, Windows Hello, passkey, forgot PIN, auto-lock | lock-screen.tsx, app-lock-gate.tsx | schema placeholders only |
| M | Impersonation return bar | impersonation-return-bar.tsx:45 | — |
| M | Org announcement dismiss / link | org-announcement.tsx | — |
| M | Device policy block toasts; watermark | device-policy-host.tsx, watermark.tsx | — |
| M | Offline banner, outbox (retry/delete/conflicts), offline storage settings, wipe device, toasts | offline-*.tsx, outbox-list.tsx, account-storage.tsx | no offline mode |
| N | Persistent storage "Ask again" | offline-settings.tsx:76 | — |
| M | Guided tour (auto-start, keys, overlay, Skip/Back/Next) | tour-*.tsx | — |
| M | Keyboard shortcuts modal (tabs, take tour, close) | keyboard-shortcuts-modal.tsx | — |
| M | Trusted senders modal (add, search, remove + undo) | trusted-senders-modal.tsx | — |
| M | TOTP re-auth dialog | totp-reauth-dialog.tsx | — |
| N | Push notification prompt; PWA install prompt | push-notification-prompt.tsx, pwa-install-prompt.tsx | — |
| M | App update "Reload" | app-update-prompt.tsx:47 | — |
| M | Account archive restore prompt | account-archive-prompt.tsx | — |
| D | Unread badge (favicon) | favicon-badge.tsx | window title |

### Mail: list, rows, selection, keyboard

| st | element / interaction | Bulwark | Barbican note / location |
|---|---|---|---|
| D | Row click opens | email-list.tsx:1209 | app.slint:1834 |
| P | Ctrl-click / Shift-click / checkbox tick | email-list.tsx:1221 | no Shift range, no checkbox |
| M | Row double-click opens in window | email-list.tsx:1222 | — |
| D | Row right-click | email-list.tsx:1225 | app.slint:612 |
| M | Row click in selection mode toggles tick | thread-list-item.tsx:684 | — |
| M | Avatar gutter click ticks / selectable avatar | thread-list-item.tsx:221, selectable-avatar.tsx | — |
| M | Thread inline expand chevron; collapse all | email-list.tsx:1209 | count chip only |
| M | Thread checkbox ticks whole thread | thread-list-item.tsx:1537 | — |
| D | Row "…" actions button | thread-list-item.tsx:771 | app.slint:689 |
| M | Row drag onto folder / tag | thread-list-item.tsx:738 | — |
| P | List attachment chips clickable; tooltip; +N overflow | attachment-chips.tsx | display only |
| P | Verification code chip | verification-code-chip.tsx | list only (app.slint:731), not reader |
| D | Hover strip Delete / Star | email-hover-actions.tsx | app.slint:695 |
| P | Hover Mark read/unread icon swap | email-hover-actions.tsx:218 | icon doesn't swap |
| P | Hover Archive (Move to Inbox in Archive) | email-hover-actions.tsx:221 | archive only |
| P | Hover Tag opens picker | email-hover-actions.tsx:224 | broken: no picker (main.rs:1593) |
| P | Hover Spam / Not spam | email-hover-actions.tsx:232 | no Not spam |
| M | Hover button tooltips; strip on keyboard focus; plugin actions | email-hover-actions.tsx | — |
| D | Floating hover mode + corner | email-hover-actions.tsx:336 | app.slint:760 |
| D | Selection bar Archive / Delete / Read / Unread / Clear | email-list.tsx | app.slint:1815-1821 |
| M | Selection bar Move / Tag / More ⋮ / Report phishing / Remove from folder | email-list.tsx | context menu only |
| P | Selection bar Spam / Not spam | email-list.tsx:1497 | spam only |
| P | Selection count "N selected · M unread" | selection-view.tsx:140 | no unread count |
| M | "Select all N in folder" banner | email-list.tsx:1567 | Ctrl+A loaded rows only |
| P | Empty Trash/Spam banner | email-list.tsx:1608 | folder menu only |
| M | Empty state Clear search / Compose | email-list.tsx:1681 | — |
| M | Pages mode Prev / Next; "1–50 of N" | email-list.tsx:1705 | infinite scroll |
| D | Load more on scroll | email-list.tsx:1294 | app.slint:1828 |
| M | List keyboard: Space tick, Enter/o open, Menu key, roving focus | list-a11y.ts | — |
| M | Date group header fold | list-date-group-header.tsx:22 | callback not wired |
| M | Column headers / column layout | list-columns.tsx | — |
| M | List ⋮ folder menu: Sort submenu, Group by date, Mark all folders read | mail-list-menus.tsx | — |
| D | Folder menu Refresh / Mark folder read / Empty | mail-list-menus.tsx | ctxmenu.rs:243 |
| M | Category tabs (Primary/Promotions…) | message-list-tabs.tsx | — |
| M | MV "Unread"/"Everything else" fold headers | mv-list-section-header.tsx | — |
| P | Select-all toggle (second click clears, tri-state) | mv-list-toolbar.tsx:369 | select only |
| M | Plugin list filter chips / row badges | plugin-list-filters.tsx | — |
| D | Keys j/k/arrows, n, f, s, e, c, /, Shift+G, Ctrl+1..4, Ctrl+A, u/Shift+I, ! | use-keyboard-shortcuts.ts | main.rs:1437-1485 |
| P | Keys r / Shift+R / a (reply all) | mail.ts:201-202 | `a` missing |
| M | Keys Enter / o open focused | mail.ts:192 | — |
| P | Esc closes and deselects | mail.ts:193 | ticks stay |
| P | Delete / # / Backspace | mail.ts:207 | Backspace missing |
| M | ? shortcuts help | index.ts:149 | — |
| M | g-then-letter navigation; x expand thread | mail.ts:195,227 | — |
| M | Shift+J/K / Shift+arrows extend selection | mail.ts:224 | — |
| P | z / Ctrl+Z multi-level undo | mail-app.tsx:1545 | latest toast only |
| P | F5/F9 refresh; Ctrl+Q read; Ctrl+Shift+M compose; Ctrl+Shift+F forward | mail.ts | missing variants |
| M | Ctrl+O open .eml; Ctrl+S save message | mail.ts:221-222 | — |
| P | Layout-aware keys on non-Latin layouts | lib/keyboard.ts:393 | typed char only |
| P | Dialog/menu blocks app keys | lib/keyboard.ts:355 | context menu only |

### Mail: menus and message actions

| st | element / interaction | Bulwark | Barbican note / location |
|---|---|---|---|
| D | Reply, Reply all, Forward, Archive, Read/unread, Star, Tag, Spam, Print, Export .eml, Find from sender | message-actions.ts | ctxmenu.rs:492-563 |
| M | Forward as attachment | message-actions.ts:253 | — |
| M | Copy message link / conversation link | message-actions.ts:260,263 | — |
| M | Restore (in Trash) | message-actions.ts:268 | — |
| M | Move to Inbox (in Archive) | message-actions.ts:276 | Archive always shown |
| P | Delete forever label in Trash | message-actions.ts:278 | logic ok, label not |
| P | Move to submenu (filter, Recent, other accounts, Choose folder…) | email-context-menu.tsx:410 | flat list |
| M | Move-to dialog | move-to-dialog.tsx | — |
| M | Copy to account | email-context-menu.tsx:416 | — |
| P | Tag submenu tri-state for batch | email-context-menu.tsx:422 | first message only |
| M | Rules submenu (quick Sieve rules from sender, block sender, create rule, manage) | rules-menu.tsx | — |
| M | Pin / Unpin | message-actions.ts:300 | — |
| M | Report phishing (dialog, undo, banner) | phishing-report-host.tsx | — |
| M | Open in window / pop-out reader | message-actions.ts:317, popout/* | — |
| M | View source dialog (copy, export) | view-source-dialog.tsx | — |
| M | Remove from folder (labels mode) | message-actions.ts:290 | — |
| M | Scheduled: Send now, Reschedule, Cancel and edit, Cancel send | message-actions.ts:233-243 | — |
| M | Edit draft in menus/toolbar | message-actions.ts:248 | — |
| P | Batch menu | message-actions.ts:464 | no fwd-as-attachments, copy, rules, remove |
| P | Shortcut hints in menus | message-action-shortcuts.ts | wrong read hint; shown with single keys off |
| P | More actions submenu | email-context-menu.tsx:473 | partial item set |
| D | Address menu Copy / New message / Find emails from | address-context-menu.tsx | ctxmenu.rs:169-173 |
| P | Address right-click on Cc/Bcc/details | address-context-menu.tsx:136 | From/To only |
| M | Address menu View contact | address-context-menu.tsx:78 | — |
| P | Address menu Add to contacts | address-context-menu.tsx:84 | opens form, not in place |

### Mail: reader

| st | element / interaction | Bulwark | Barbican note / location |
|---|---|---|---|
| D | Toolbar Reply / Reply all / Forward / Archive / Delete / Print / Star | email-viewer.tsx:4820-5083 | app.slint:1933-1998 |
| P | Toolbar Move (filter, Choose folder) | email-viewer.tsx:4953 | plain popup |
| P | Toolbar Tag (checked state) | email-viewer.tsx:5002 | no checks |
| P | Toolbar Spam / Not spam | email-viewer.tsx:5032,4876 | Report spam only |
| P | Toolbar Read/Unread toggle | email-viewer.tsx:5062 | Mark unread only |
| M | Toolbar Restore / Move to Inbox | email-viewer.tsx:4892,4909 | — |
| P | Toolbar More ⋮ | email-viewer.tsx:5159 | short list |
| M | View in light/dark; Open in window; Fullscreen | email-viewer.tsx:5101-5135 | — |
| M | Prev/Next buttons; Back to list | email-viewer.tsx:4734-4757 | keys only |
| M | Message header click collapse; sender avatar click | email-viewer.tsx:5551,5561 | — |
| P | Sender / recipient name popover card | recipient-popover.tsx:281 | right-click only |
| M | Card View contact; tel: links | recipient-popover.tsx:156,123 | — |
| M | Details panel (to N recipients, Cc, auth chips, Message-ID) | email-viewer.tsx:5700-5960 | spam pill only |
| M | Brand-verified badge; External chip; identity badge; received-via chip; sender tooltip | brand-verified-badge.tsx etc. | — |
| M | Unsubscribe banner | unsubscribe-banner.tsx | — |
| D | Load external content | email-viewer.tsx:6418 | app.slint:2111 |
| M | Trust sender | email-viewer.tsx:6429 | global setting only |
| M | Read receipt banner Send/Ignore | read-receipt-banner.tsx | — |
| M | Calendar invitation banner (Accept/Maybe/Decline, Add to calendar, remove, apply proposal, view) | calendar-invitation-banner.tsx | RSVP only in Calendar menu |
| M | Read aloud bar; reading mask; reader view dialog | read-aloud-bar.tsx etc. | — |
| M | "Shown as plain text" > Show formatted | email-viewer.tsx:6528 | global setting |
| P | Body right-click selection menu | email-viewer.tsx:6563 | lite/plain modes only |
| P | Link click with phishing gate | email-viewer.tsx:6566 | no gate |
| M | Footer Reply / Reply all / Forward | email-viewer.tsx:6614-6622 | — |
| M | Reply draft banner Edit / Discard | email-viewer.tsx:6639 | — |
| D | Quick reply box + Send | email-viewer.tsx:6669,6731 | app.slint:2203-2221 |
| P | Quick reply Ctrl+Enter | email-viewer.tsx:6671 | Enter (single line) |
| M | Quick reply Cancel / Open full editor / From line / draft save | email-viewer.tsx:6692-6710 | — |
| M | Conversation cards: expand/collapse, "N older", per-card buttons | thread-conversation-view.tsx, email-viewer.tsx:927-1214 | thread strip |
| P | Per-message actions via thread context menu | conversation-message-menu.tsx | right-click only |
| M | Draft card Edit / Discard | thread-conversation-view.tsx:584,598 | — |
| D | Attachment tile click, Download, right-click Open / Save as | reader-attachments.tsx | app.slint:2165-2185 |
| M | Attachment Save to Files, Rename, Edit, Download all, Save all, Show N more, drag out | reader-attachments.tsx | — |
| P | Attachment Copy (file to clipboard) | reader-attachments.tsx:314 | file name only |
| M | In-app attachment preview; WOPI edit | mail-app.tsx:7272,7282 | opens in OS |
| M | Tag chip X in reader | tag-badge.tsx:110 | — |
| M | Retention notice; trash recovery link; reader retry | retention-notice.tsx etc. | — |
| P | Contact sidebar (close, copy, edit, phone copy, add) | email-viewer.tsx:587-751 | Message button only |
| P | Search snippet highlights | search-snippet-text.tsx | no highlights |
| M | Plugin reader surfaces / selection plugin actions | reader-plugin-surfaces.tsx | — |

### Mail: composer

| st | element / interaction | Bulwark | Barbican note / location |
|---|---|---|---|
| P | Close / Esc asks save or discard | email-composer.tsx:5111 | saves silently |
| M | Close dialog Cancel / Discard / Save draft | email-composer.tsx:6916-6923 | — |
| P | Discard with confirm | email-composer.tsx:6705 | no confirm |
| M | Pop out / Maximize / Minimize / MV window | email-composer.tsx:5124-5655 | toast "not available" |
| P | Save status | email-composer.tsx:5660 | status text |
| D | Send; Ctrl+Enter; Ctrl+S | email-composer.tsx:5709 | app.slint:2724, main.rs:1428 |
| P | Send arrow menu | email-composer.tsx:5722 | Schedule disabled |
| M | Schedule send dialog + Ctrl+Shift+Enter (currently sends now) | schedule-send-dialog.tsx | main.rs:1428 |
| P | Drop files overlay | email-composer.tsx:5760 | drop works, no overlay |
| D | From select | email-composer.tsx:5813 | not grouped |
| M | Sub-address +tag helper; From override | sub-address-helper.tsx | — |
| M | Address book / directory picker | directory-picker-dialog.tsx | — |
| D | Cc / Bcc toggles | email-composer.tsx:6031,6056 | app.slint:2352 |
| M | Recipient autocomplete (type, keys, click) | email-composer.tsx:7689,2660 | plain text field |
| M | Recipient chips (create, backspace, paste split, drag, chip menu, keyboard) | email-composer.tsx:7314-7762 | — |
| M | Ctrl+Shift+C / Ctrl+Shift+B show Cc/Bcc | email-composer.tsx:5509 | B used for quote |
| D | Subject; plain body; body right-click; cut/copy | email-composer.tsx | ctxmenu.rs:1074 |
| P | Selection plugin actions | composer-selection-menu.tsx:113 | AI rewrite only |
| M | Attachment chip right-click / preview / rename / edit / cancel upload | email-composer.tsx:6293-6419 | remove only |
| D | Attach files | email-composer.tsx:6515 | app.slint:2657 |
| M | Attach from Files / recent files / plugin sources | file-picker-dialog.tsx | — |
| D | Templates > Insert | email-composer.tsx:6566 | app.slint:2662 |
| M | Templates 't' key; Save as template; placeholder fill; picker search | template-picker.tsx etc. | — |
| D | Options Plain text / Read receipt; Tags menu | email-composer.tsx:6600-6680 | app.slint:2678-2689 |
| M | Options DSN, Require TLS, Encrypt, Priority, Classification | email-composer.tsx:6612-6660 | — |
| P | Send warnings dialog | send-warnings-dialog.tsx | inline status, press Send again |
| M | External recipients confirm | external-recipient-confirm.tsx | — |
| M | @mentions | recipient-mention.tsx | — |
| M | Reply-type menu | reply-type-menu.tsx | — |
| D | Formatting basics, lists, align, link, clear, undo, table edits, paste/drop images, image resize | rich-text-editor.tsx | app.slint:2389-2451 |
| P | Font size; text colour; highlight; table grid; insert picture | rich-text-editor.tsx | fewer options, no URL |
| M | RTL; image alt text; cell colour; overflow; Alt+F10 toolbar keys; image drag | rich-text-editor.tsx | — |
| M | Image from URL dialog | image-url-dialog.tsx | — |
| P | Link dialog validation | link-dialog.tsx:96 | no whitespace message |
| M | Plain-text list paste; org disclaimer preview | plain-text-paste.ts | — |
| M | Plugin composer buttons, panels, options | composer-*plugin*.tsx | — |

### Mail app shell, search, transfer, templates, identities, popout, print

| st | element / interaction | Bulwark | Barbican note / location |
|---|---|---|---|
| M | Skip to message list | mail-app.tsx:6328 | — |
| M | Pane resize (sidebar, list, horizontal split) | mail-app.tsx:6502-6945 | fixed |
| D | Search box type / Enter / empty Enter | search-box.tsx | app.slint:1755 |
| M | Search clear X | search-box.tsx:166 | — |
| M | Search suggestions, recent searches, contact from:/to: | search-suggestions.tsx | — |
| M | Removable active-filter chips | search-chips.tsx | — |
| P | Filter toggle count badge | mail-app.tsx:6665 | no badge |
| P | Filter panel live search; Enter submits | search-filter-panel.tsx | Search button only |
| M | Date/size presets; Read filter; Not starred / No attachment | search-filter-panel.tsx | — |
| M | "Search in" folder select | search-filter-panel.tsx:229 | global setting only |
| D | Filter Clear all; MV Clear/Search | search-filter-panel.tsx | app.slint:1799 |
| M | Continue draft banner + discard | mail-app.tsx:6979 | — |
| M | Undo-send "Send now"; scheduled Cancel; "Moved to Drafts" Open | mail-app.tsx:3539-3691 | Undo only |
| M | Mailto account picker | mail-app.tsx:7298 | OS handler |
| M | Mailbox share dialog | mail-app.tsx:7325 | — |
| M | Reply dock (resize, show message) | reply-dock.tsx | composer replaces reader |
| P | Floating compose button | floating-compose-button.tsx | sidebar button |
| M | Mail import / export (mbox, eml, local profiles, jobs) | mail-transfer/* | — |
| D | Single message export .eml | — | ctxmenu.rs:771 |
| P | Template manager | template-manager-modal.tsx, template-form.tsx | name/subject/text + delete |
| D | Template sync | account-template-sync.tsx | templates.rs |
| P | Identities (add, primary, delete, Bcc, HTML signature) | identity-manager-modal.tsx | name, reply-to, text signature |
| P | Print conversation / print header | conversation-print.ts, message-print.tsx | one message, body only |
| M | Pop-out windows (message, compose, saved .eml) | popout/* | — |
| M | Plugin search results | plugin-search-results.tsx | — |
| M | Classification label picker | classification-label-picker.tsx | — |

### Calendar and tasks

| st | element / interaction | Bulwark | Barbican note / location |
|---|---|---|---|
| M | All calendar keys (arrows, t, m/w/d/a/k, n/c, /, e, Ctrl+Enter, F5) | calendar-shortcuts.ts | main.rs:1446 |
| M | Menu commands / palette entries | calendar-app.tsx:1954-1995 | — |
| M | Sidebar resize; swipe; horizontal wheel | calendar-app.tsx | — |
| P | Hover popover (enterable) | calendar-app.tsx:2877 | passive card |
| M | Recurrence scope dialog | recurrence-scope-dialog.tsx | always whole series |
| M | Load error Retry | calendar-load-error-notice.tsx | — |
| D | New event button; mini-cal prev/next/day | mini-calendar.tsx | app.slint:3036-3065 |
| M | Mini-cal month/year picker; keyboard; plugin marks | mini-calendar.tsx | — |
| D | Calendar row click / right-click; New event, Rename, Set default, Delete | calendar-sidebar-panel.tsx | ctxmenu.rs:282-294 |
| P | Change colour; Clear all events | calendar-sidebar-panel.tsx:527,547 | presets; clear in Settings |
| M | Share calendar; reset/random colour; "..." button | calendar-sidebar-panel.tsx | — |
| M | Subscriptions (subscribe, edit, refresh, unsubscribe) | ical-subscription-modal.tsx | — |
| M | Section headers, "+", Tasks entry, account groups | calendar-sidebar-panel.tsx | flat list |
| M | Create calendar modal (colour, kind) | create-calendar-modal.tsx | Settings name field |
| D | Today / Prev / Next | calendar-toolbar.tsx | app.slint:3116-3118 |
| P | View switch | calendar-toolbar.tsx:484 | no Agenda, no Tasks |
| M | More menu (Import, Subscribe, Refresh, Print); sidebar toggle; side panel | calendar-toolbar.tsx | — |
| P | Event search | calendar-search.tsx | loaded only, no results list |
| P | Month cell click quick-create | calendar-month-view.tsx:638 | double-click full dialog |
| D | Month cell right-click; month wheel | calendar-month-view.tsx:645 | app.slint:3147 |
| M | Month drag-range create; day-number opens Day; roving keys | calendar-month-view.tsx | — |
| P | Event click opens detail | calendar-month-view.tsx:903 | edit dialog directly |
| M | Event drag move; resize handles; drag-to-create | calendar-week-view.tsx | — |
| P | "+N more" | calendar-month-view.tsx:941 | not clickable |
| M | Task chips in grid | task-chip.tsx | — |
| M | Week extras (N days, all hours, hidden pill, day header, all-day row) | calendar-week-view.tsx | — |
| M | Hour slot single-click quick-create | calendar-week-view.tsx:832 | — |
| P | Hour slot double-click / right-click | calendar-week-view.tsx:833 | wrong day (anchor date) |
| M | Quick-create popover / inline input | quick-create-popover.tsx | — |
| M | Agenda view | calendar-agenda-view.tsx | — |
| D | Empty-space menu New event / all-day / Today | empty-space-context-menu.tsx | ctxmenu.rs:267-271 |
| P | Empty-space New task | empty-space-context-menu.tsx:63 | title prompt |
| D | Event menu RSVP / Edit / Duplicate / Export .ics / Copy title | event-context-menu.tsx | ctxmenu.rs:249-260 |
| M | Event menu Copy to account / meeting link / deep link / Print | event-context-menu.tsx | — |
| P | Event Delete (scope, notify) | event-context-menu.tsx:178 | confirm only |
| M | Event detail popover | event-detail-popover.tsx | — |
| M | Event modal view mode | event-modal.tsx:1149-1384 | — |
| P | Event modal dirty check; Esc; Ctrl+Enter | event-modal.tsx | none |
| D | Title, All-day, Calendar, Location, Description | event-modal.tsx | app.slint:3762-3781 |
| P | Date-time pickers | event-modal.tsx:1442-1473 | text fields, single date |
| M | Time zone; custom recurrence; show as; visibility; video link | event-modal.tsx | — |
| M | Participants, availability, Find a time, invitations | participant-input.tsx, find-a-time.tsx | — |
| M | Alerts editor; reminders; missed reminders | alert-list-editor.tsx | — |
| P | Edit footer Delete / Duplicate / Save | event-modal.tsx:1807-1826 | no confirm, no scope |
| M | iCal import | ical-import-modal.tsx | — |
| M | Calendar print | calendar-print.tsx | — |
| M | Task filters; task modal; row click/keys; Pick date; Move to list | task-*.tsx | — |
| P | Quick add with NL due date | task-list-view.tsx:95 | separate date field |
| D | Task right-click, checkbox, complete / due / delete | task-context-menu.tsx | ctxmenu.rs:276-279 |

### Contacts

| st | element / interaction | Bulwark | Barbican note / location |
|---|---|---|---|
| P | Sidebar "+ New" menu | contacts-sidebar.tsx:294 | separate + buttons |
| D | New contact; All contacts; book rows and menu | contacts-sidebar.tsx | ctxmenu.rs:322-330 |
| M | New address book; Import; Directory; section collapse; share book | contacts-sidebar.tsx | — |
| P | New / Edit group members | contacts-sidebar.tsx:307,737 | name prompt only |
| D | Group rows, send To/Cc/Bcc, delete; categories | contacts-sidebar.tsx | ctxmenu.rs:314-321,454 |
| M | Sidebar keys; hover "..."; drop contacts on category/book | contacts-sidebar.tsx | — |
| M | Resize; duplicates; merge; bulk add-to-group; toasts | contacts-app.tsx | — |
| D | Search field | contact-list.tsx:444 | app.slint:3329 |
| M | Select mode; filters; print; "/" key | contact-list.tsx | — |
| P | Sort first/last name | contact-list.tsx:587 | Settings only |
| M | Bulk bar | contact-list.tsx:610-663 | — |
| D | Row click / right-click | contact-list.tsx:746,768 | app.slint:3341 |
| M | Ctrl/Shift click, checkbox, drag, list keys | contact-list.tsx | — |
| M | Empty state actions | contact-list.tsx:695-723 | — |
| D | Contact menu Open / Email / Edit / Add to group / Copy email / Delete | contact-context-menu.tsx | ctxmenu.rs:297-306 |
| M | Contact menu Call / Merge / Move / Copy phone / Export vCard / Print; multi-select menu and pane | contact-context-menu.tsx | — |
| D | Detail Email / Edit | contact-detail.tsx | app.slint:3388 |
| P | Detail Delete | — | no confirm |
| M | Detail Print, ⋮, per-email compose/copy, tel:, links, activity | contact-detail.tsx | — |
| D | Form Cancel / Save / Note | contact-form.tsx | app.slint:3411-3416 |
| P | Form structured fields | contact-form.tsx | flat fields |
| M | Form photo, book, kind, categories, more fields | contact-form.tsx | — |
| P | Group detail compose / remove member | contact-group-detail.tsx | To only |
| M | Group form member picker; group ⋮ | contact-group-*.tsx | — |
| M | Import / merge / book picker / directory / recipient picker dialogs | contact-*-dialog.tsx | — |
| N | Mobile FAB / back | contacts-app.tsx | — |

### Files, files-sync, file drop

| st | element / interaction | Bulwark | Barbican note / location |
|---|---|---|---|
| D | New menu Upload files / folder / New folder | files-sidebar.tsx | ctxmenu.rs:397-400 |
| M | New file dialog | new-file-dialog.tsx | — |
| D | Sidebar navigation and tree | files-sidebar.tsx | app.slint:3454-3464 |
| D | Folder / recent menus | files-sidebar.tsx | ctxmenu.rs:347-385 |
| M | Create-inside items; Download as ZIP | files-sidebar.tsx | — |
| M | Drag-drop onto tree / trash / crumbs / rows | files-*.tsx | — |
| D | Breadcrumb; Empty trash | files-header.tsx | app.slint:3487,3495 |
| P | Paste / Refresh buttons | files-header.tsx | menu only |
| M | Search; sort; view toggle; details panel | files-header.tsx | — |
| M | Selection (click, Ctrl/Shift, checkbox, marquee, toolbar, multi menu) | file-views.tsx | — |
| P | Double-click file | file-views.tsx:239 | OS app, no preview |
| D | Item / background right-click; hover download | file-browser.tsx | ctxmenu.rs:336-405 |
| M | OS file drop upload | file-browser.tsx:1457 | — |
| M | Item Preview, Office edit, imports, offline, Details | file-browser.tsx | — |
| D | Item Share, Star, Rename, Duplicate, Move, Cut, Copy, Download | file-browser.tsx | ctxmenu.rs:371-383 |
| P | Delete with undo; trash menu | file-browser.tsx | no undo |
| M | Files keys | file-browser.tsx:847-932 | — |
| M | Undo toasts; upload/ZIP cancel | files-app.tsx | — |
| M | Error retry; empty-state buttons | file-browser.tsx | — |
| M | Preview modal (zoom, rotate, prev/next, .eml) | preview-shell.tsx etc. | — |
| P | Move dialog | folder-picker-dialog.tsx | flat submenu |
| M | Upload conflict dialog | upload-conflict-dialog.tsx | — |
| M | Files sync | files-sync/* | — |
| P | File drop overlay and zones | file-drop-host.tsx | composer attach only |

### Settings, notifications, accessibility, protocol

| st | element / interaction | Bulwark | Barbican note / location |
|---|---|---|---|
| D | Tabs; Back to mail; toggles; selects | settings-app.tsx | app.slint:3842-4000 |
| P | Settings search | settings-app.tsx:1097 | no sub-result jumps |
| M | / and Ctrl+F; clear; back/forward; cross-links; section nav; resize | settings-app.tsx | — |
| P | Show advanced settings | settings-section.tsx:170 | tab only |
| M | Account list in settings (switch, default, reorder, remove, shared) | account-settings.tsx | — |
| D | Sign out | sign-out-row.tsx | settings.rs:263 |
| P | Your name; Manage addresses; delimiter | identity-settings.tsx | inline; broken label |
| M | Account archive; per-account push | account-archive-settings.tsx | — |
| M | Password & sign-in page | account-security-settings.tsx | page empty |
| M | App lock settings | app-lock-settings.tsx | — |
| M | Masked addresses; delegates | masked-addresses-section.tsx, delegates/* | — |
| M | Restart tour; language; time zone; protocol handlers; default mail app | general-settings.tsx etc. | — |
| D | Date/time format; first day of week | language-settings.tsx | — |
| D | Email notifications, sound, inbox only, badge | notification-settings.tsx | main.rs:451-457 |
| P | DND, all-accounts badge, event notifications, push relay | notification-settings.tsx | inert |
| M | Test sound, tone, push devices, quiet hours | notification-settings.tsx | — |
| M | Notification centre | notification-center.tsx | — |
| D | Theme, style, density, layout, labels, icons, counts, unified, tint | layout-settings.tsx | — |
| P | Mail layout, toolbar position, compose placement, rail accounts, shared, pro, keep apps (inert) | layout-settings.tsx | — |
| M | Plugin themes; cross-account views; print style; sidebar apps settings | themes-settings.tsx etc. | — |
| D | Accessibility body text, contrast, motion, focus, targets, confirmations, reset | accessibility-settings.tsx | a11y.rs |
| M | Accessibility presets, quick button, app font, reading mask, cursor, read-aloud, simple mode, others | accessibility-settings.tsx | — |
| M | Accessibility quick panel (Alt+Shift+A) | accessibility-quick-panel*.tsx | — |
| D | Message list settings | message-list-settings.tsx | — |
| P | List columns, swipe, order scope (inert); hover tag | message-list-settings.tsx | — |
| M | Reorganize archive; list order editor | message-list-*.tsx | — |
| D | Reading settings | reading-settings.tsx | — |
| M | Window settings | desktop-window-settings.tsx | — |
| D | Composing settings | composing-settings.tsx | compose.rs |
| P | Mentions, RTL, replies under (inert); signature editor; templates; proofreading | composing-settings.tsx | — |
| M | Reminder keywords; signature details; template export/import; quick-compose shortcut | composing-settings.tsx | — |
| P | External content policy | privacy-settings.tsx:108 | no Block |
| M | Trusted senders; read receipt response; favicons; telemetry | privacy-settings.tsx | — |
| P | Create folder in Organize | folder-settings.tsx | Create button only |
| D | Folder rename / delete / right-click | folder-settings.tsx | server_settings.rs |
| M | Folder bulk move, tree, icon, counter, drag, role | folder-settings.tsx | — |
| M | Tag manager and shared tags | keyword-settings.tsx | — |
| P | Filter add rule; Sieve editor | filter-settings.tsx | one condition/action |
| D | Rule enable/delete/fields; Sieve save | filter-rule-modal.tsx | server_settings.rs |
| M | Edit existing rule; reorder; multiple conditions; apply to existing; validate | filter-*.tsx | — |
| D | Vacation enable, subject | vacation-settings.tsx | — |
| P | Vacation save, dates, body | vacation-settings.tsx | autosave, date only, plain |
| M | Vacation audience, templates, preview, forwarding, calendar block | vacation-settings.tsx | — |
| P | Recover deleted | recover-deleted-settings.tsx | per item, no older |
| D | Calendar display settings | calendar-settings.tsx | calendar.rs |
| M | Calendar default view; organizer identity | calendar-settings.tsx | — |
| D | Calendars management | calendar-management-settings.tsx | — |
| M | Calendar default alerts, share, import, subscribe, CalDAV URLs | calendar-management-settings.tsx | — |
| D | Contacts settings; address book management | contacts-settings.tsx | — |
| M | Contacts import/export; book share; category rename/delete | — | — |
| M | Files settings; download filenames; desktop sync | files-settings.tsx etc. | — |
| P | Plugins install | plugins-settings.tsx:140 | Open folder only |
| D | Plugin enable | plugins-settings.tsx:185 | settings.rs |
| M | Plugin details, settings, remove | plugins-settings.tsx | — |
| M | Debug categories; devtools; about links; refresh cache; Spam Siege | debug-settings.tsx etc. | — |
| D | Export / import / reset settings | about-data-settings.tsx | main.rs:1056-1074 |
| M | Desktop: start at login, tray, global shortcuts, updates | desktop-os-settings.tsx etc. | — |
| M | Protocol launch: mailto, webcal, share target, .ics/.vcf/.eml | protocol/* | — |

### Command palette, global search, shortcuts help

| st | element / interaction | Bulwark | Barbican note / location |
|---|---|---|---|
| M | Command palette (Ctrl+K, filter, keys, commands) | command-palette.tsx | — |
| P | Palette Go-to / compose / search / theme / switch account / sign out | command-palette.tsx:201-279 | outside palette only |
| M | Global search palette and search tab | global-search/* | — |
| M | Keyboard shortcuts modal | keyboard-shortcuts-modal.tsx | — |
| P | Theme toggle | theme-store.ts | Settings only |
| M | Resizable columns; sidebar collapse | ui-store.ts | — |

### Admin, connector, plugins, Pro shell

| st | element / interaction | Bulwark | Barbican note / location |
|---|---|---|---|
| D | Admin rail tab | admin-shell.tsx:458 | app.slint:1211 (Stalwart admin) |
| P | Admin navigation | admin-shell.tsx | Stalwart sections, no unsaved guard |
| P | Admin sign out | admin-shell.tsx:241 | clear key in Settings |
| M | Admin search, change password, config items, save bar, screenshots | admin/* | — |
| M | Org policy pages | admin/org/* | — |
| M | User settings policy; tag defaults | admin/policy/* | — |
| M | Connector deep links | connector/* | — |
| M | Plugin panels, actions, dialogs, consent, iframe UI, pages | plugins/* | native rows only |
| P | Turn off plugin | plugin-dialog-host.tsx:606 | Settings toggle |
| D | Native plugin toggle / folder / compose.check | — | settings.rs:284, compose.rs:629 |
| P | proInterface setting | pro-interface-redirect.tsx | inert |
| M | Pro tab shell (tabs, drag, split panes, compose tabs) | pro/* | — |
| P | Pro tab reader actions | pro/* | main reader only |
