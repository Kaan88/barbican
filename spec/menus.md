# Context menus, item by item (from `src/ctxmenu.rs` and `src/reader.rs` at `rust-final`)

Written at the M0 review (stream m0-review); PORT-PLAN §2.3 lists this file and no catalogue stream
produced it. The source is `ctxmenu.rs` `request` (lines 150-442), `mail_items` (:516), `folder_items`
(:551), `submenu` (:575), `pick` confirmations (:830-852), `rte_items` / `rte_sub` (:1179-1273), and
`reader.rs` `menu` (:986), `submenu` (:1028), `att_items` (:1071).

Conventions, as in Rust (all Bulwark menus: email, mailbox, calendar, contact, file):
- **hint** is the shortcut shown at the right. **[off: X]** means the row is shown but disabled unless X.
- **danger** rows are red. A row whose action starts `ask:` replaces the menu with a confirmation:
  header "Are you sure?", the danger row with the label in the confirmation table below, and "Cancel" (Esc).
- **›** opens a flyout beside the row (a `sub:` action); hovering any other row closes it.
- **header** rows are non-clickable titles (usually the item's name). Separators are shown as `—`.
- C# requirement (ADR 0005/0007): every menu is an in-window, constrained popup
  (`ShouldConstrainToRootBounds=True`), so it draws over the WebView2 body and stays off the real screen.

IDs `MENU-<menu>` are stable; C# status and evidence are tracked in `spec/interactions.csv` (I- rows),
milestone in brackets.

## MENU-MAIL: message row in the list [M2]
Target: the selection when the clicked row is in a multi-selection, else the row.
1. header "N selected" (only when N > 1)
2. Scheduled view only, when the first message has a `submissionId`: Reschedule [off: N = 1], Cancel and edit [off: N = 1], danger Cancel send [off: N = 1], —
3. Reply (R) [off: N = 1], Reply all (Shift+R) [off: N = 1], Forward (F) [off: N = 1], —
4. Archive (E); danger Delete (Del), which asks first when N > 1 and `confirmBulkDelete` is on (default on); Move to ›, —
5. Mark as unread / Mark as read (U), by the first message's `$seen`; Unstar / Star (S), by `$flagged`; Tag ›, —
6. Not spam (in the Junk folder) / Report spam (!), —
7. More actions ›

Flyouts:
- **Move to ›**: every folder except virtual ones (`__…`) and the current one, indented by depth.
- **Tag ›**: every tag with its colour, checked when the first message has `$label:<id>`; "No tags" (disabled) when there are none.
- **More actions ›**: Open (Enter), Print, Download (.eml), View source, Forward as attachment, —, Copy sender address, Find emails from sender; all [off: N = 1].

## MENU-THREAD: a message inside an open thread [M2]
Open; Reply (R); Reply all (Shift+R); Forward (F); —; Mark as read / Mark as unread (U) by the row's unread state; Unstar / Star (S); —; Print; danger Delete (Del).

## MENU-ADDR: an address in the reader header or a chip (`addr`, `addr-to`) [M2]
header (the address); Copy email address; New message; Add to contacts; —; Find emails from this sender. No menu when no address can be parsed.

## MENU-ATT: an attachment chip [M2]
Open; Save as…; then the reader extras: Open message (first, for an attached message), Save to Files › (when Files is available or loading), Download all (.zip) (more than one attachment); —; Copy file name.
- **Save to Files ›**: "Loading folders…" (disabled) until loaded; then My files and every folder, indented.
- **Built (m2-close-b):** Save as…, Download all (.zip) (more than one attachment with content), —, Copy file name. Not built yet, on purpose: Open (opening an untrusted file with the system handler needs a decision, see FABLE-REVIEW m2-close-b), Open message (Email/parse has no reader UI), Save to Files (no Files client in the reader). A click or right-click on an attachment card opens it.

## MENU-READER: the reader's More menu [M2]
Opens from the ⋮ button of the reader's action row (the message on screen is the target). Built rows: View source (a dialog: the raw message cut at 2 MiB with Copy source and Save .eml), Forward as attachment (off until the composer, M3), Download (.eml), Download all attachments (.zip) [off: no attachments], then the Rules section. "Has blob" and "opened .eml" are not tracked yet: a loaded message always counts as having its blob.
View source [off: has blob, not an opened .eml]; Forward as attachment [off: has blob, not a shared-account message, not an opened .eml]; Download (.eml) [off: has blob, not an opened .eml]; Download all attachments (.zip) [off: has attachments].
Then, when a rule subject exists (not for an opened .eml): —; header "Rules";
- Always move messages from <sender> › [off: one sender] (or "…from the sender");
- Always move messages from @<domain> › (when there is a domain);
- Always move messages from this list › (when there is a List-Id);
- Always mark as read from <sender> [off: one sender];
- Always tag with › [off: one sender];
- Block sender [off: one sender] when there is a Junk folder; otherwise disabled with the hint "No Junk folder";
- Manage rules.
- **Move flyouts**: folders that take mail (no Drafts, `mayAddItems` not false), indented; "No folders" when empty.
- **Always tag with ›**: every tag with its colour; "No tags" when empty.

## MENU-BODY: the message body [M2]
Plain-text view only: Copy (Ctrl+C), Select all (Ctrl+A), —. Always: Copy message text; Reply (R); Print.
(In the HTML body, the shell catches `contextmenu` and posts `ctx:X,Y`; the host opens this menu: `src/Barbican.App/Assets/mail/shell.js`, ADR 0005.)

## MENU-FOLDER: a mailbox in the sidebar, or a folder row on the Organize settings page [M2]
header (folder name); Mark folder as read; Refresh; —; New subfolder; Rename [off: not a role folder]; Import messages (.eml, .mbox)… [off: the account cannot write]; Export as mbox…; Export as .eml files… (a folder pick; both read only, so they stay on for a read-only account); Rules ›;
Trash or Junk only: —, danger Empty trash / Empty spam (asks); custom folders only: —, danger Delete folder (asks). Virtual folders (`__…`) have no menu.

## MENU-TAG: a tag in the sidebar [M2]
Show messages; Mark all as read; —; New tag…; New tag inside… (only with nested tags and a valid child id); Rename…; Colour ›; danger Delete tag (asks; messages keep the keyword); —; Manage tags….
- **Colour ›**: the tag palette, each in its colour, the current one checked.

## MENU-LIST: the list background [M2]
Refresh; Mark folder as read; Select all (Ctrl+A).

## MENU-EVENT: an event in the calendar [M4]
When the user is a participant: Accept / Maybe / Decline, each shown only when it is not the current status, then —.
Edit; Duplicate; —; Export as .ics; Copy title; —; danger Delete (Del) (asks).

## MENU-SLOT: an empty time slot [M4]
New event; New all-day event; New task; —; Go to today.

## MENU-TASK: a task [M4]
Mark incomplete / Mark complete; Due date ›; —; danger Delete (Del) (asks).
- **Due date ›**: Today; Tomorrow; Next week; No due date (only when the task has one).

## MENU-CAL: a calendar in the calendar sidebar [M4]
header (name); New event in this calendar; Hide calendar / Show calendar; Rename; Colour ›; Set as default [off: not default]; —; danger Delete calendar [off: not default] (asks; Bulwark never offers to delete the default calendar).
- **Colour ›**: the calendar colours (`server_settings::CAL_COLORS`), current one checked.

## MENU-CALSET: a calendar row on the Calendars settings page [M6]
header; Rename; Colour ›; Hide calendar / Show calendar; Set as default [off: not default]; —; Move up [off: not first]; Move down [off: not last]; —; danger Clear all events (asks); danger Delete calendar [off: not default] (asks).

## MENU-BOOKSET: an address book row on the Contacts settings page [M6]
header; Rename; Set as default [off: not default]; —; danger Delete address book [off: not default] (asks).

## MENU-CONTACT: a contact [M5]
Open; Send email; Edit; —; Add to group ›; Categories ›; —; Copy email address; —; danger Delete (Del) (asks).
- **Add to group ›**: every group, checked when the contact is a member; —; New group….
- **Categories ›**: every category, checked when set; —; New category….

## MENU-GROUP: a contact group (list or sidebar) [M5]
(sidebar: header with the group name) Open; Rename group; Send email to group › (To / Cc / Bcc); —; danger Delete group (asks).

## MENU-CATEGORY: a category in the contacts sidebar [M5]
header; Show contacts; Rename category; —; danger Delete category (no confirmation in Rust; check against Bulwark at M5).

## MENU-BOOK: an address book in the contacts sidebar [M5]
header; New contact in this address book; Rename; Set as default [off: not default]; —; danger Delete address book [off: not default] (asks).

## MENU-FILE: a file or folder in Files [M5]
header (name); Open; Download (files only); — (when either of the next two shows); Share › (when the user may share); Star / Unstar (own folders only, not shared-in); —; Rename (F2); Duplicate; Move to ›; Cut (Ctrl+X); Copy (Ctrl+C); —; danger Delete (Del) (asks: "Move to trash", or "Delete for good" for shared items).
- **Share ›**: Share read-only…; Share with edit rights…; then, when shared: —, header "Shared with", "Stop sharing with <name>" (+ " (edit)") per principal; names are resolved in the background.
- **Move to ›**: valid targets only (My files and folders not inside the item); "No other folders" when none.

## MENU-FILE-TRASH: an item in the Files trash [M5]
header; Restore; —; danger Delete forever (Del) (asks: "Delete for good").

## MENU-FILE-FAV / MENU-FILE-RECENT: Favorites and Recent entries in the Files sidebar [M5]
Favorites: header; Open; —; Unstar. Recent: header; Open; Show in folder; Download (files only); —; Remove from recent.

## MENU-FILES-BG: the Files background [M5]
In the trash: danger Empty trash [off: the trash is not empty] (asks); Refresh. Elsewhere: New folder; Upload files; Upload folder; —; Paste (Ctrl+V) [off: something was cut or copied]; Refresh.
The toolbar's Empty trash button opens the confirmation directly ("Delete everything in the trash").

## MENU-RTE: the composer's editor [M3]
When the caret is on a misspelt word: up to 5 suggestions ("No suggestions", disabled, when none); Add "<word>" to dictionary; Ignore; —.
Cut (Ctrl+X) [off: selection]; Copy (Ctrl+C) [off: selection]; Paste (Ctrl+V); Paste as plain text (Ctrl+Shift+V); —; Select all (Ctrl+A); —; Format ›; Paragraph ›; Table › (in a table only); —;
on a link: Edit link… (Ctrl+K), Open link, Copy link address, Remove link; otherwise Insert link… (Ctrl+K) [off: not on an image];
on an image: —, Original image size, Small (240 px), Medium (480 px);
unless the assistant is hidden: —, Rewrite selection with AI › / Rewrite with AI ›;
—; Undo (Ctrl+Z) [off: can undo]; Redo (Ctrl+Y) [off: can redo].
- **Format ›** (checked by state): Bold (Ctrl+B), Italic (Ctrl+I), Underline (Ctrl+U), Strikethrough (Ctrl+Shift+S), Inline code (Ctrl+E), Highlight (Ctrl+Shift+H), —, Clear formatting (Ctrl+\\).
- **Paragraph ›** (checked by state): Normal text (Ctrl+Alt+0), Heading 1-3 (Ctrl+Alt+1..3), —, Bulleted list (Ctrl+Shift+8), Numbered list (Ctrl+Shift+7), Quote (Ctrl+Shift+B), Code block (Ctrl+Alt+C), —, Align left (Ctrl+Shift+L), Center (Ctrl+Shift+E), Align right (Ctrl+Shift+R), Justify (Ctrl+Shift+J).
- **Table ›**: Add row above, Add row below, Add column left, Add column right, —, Delete row, Delete column, Toggle header row, —, danger Delete table.
- **Rewrite with AI ›**: Improve, Shorten, Formal, Casual, Fix spelling, Translate; all [off: the assistant is not busy].

## Confirmation labels (`ask:` rows, ctxmenu.rs:833-852)
| Menu and action | Danger row in the confirmation |
|---|---|
| folder, empty | Delete all messages permanently |
| folder, delete | Delete this folder |
| calendar (sidebar) | Delete calendar and its events |
| file outside the trash, not shared | Move to trash |
| file in the trash, or shared | Delete for good |
| files background, empty trash | Delete everything in the trash |
| calendar settings, clear | Delete every event in this calendar |
| calendar settings, delete | Delete calendar and its events |
| address book (settings or sidebar) | Delete address book and its contacts |
| group | Delete group |
| tag | Delete this tag (messages keep it) |
| mail (bulk delete) | Delete selected messages |
| anything else | Delete |

## Open questions
- Rust has 25 menu shapes; PORT-PLAN §2.3 says "the 21 context menus" (it counted before the files
  sidebar, settings-page and trash menus were added). The table above is the list to port.
- MENU-CATEGORY Delete category has no confirmation in Rust; check Bulwark's contact sidebar at M5.
- Undo/Redo, Cut/Copy "[off: …]" are evaluated when the menu opens; the C# menu must evaluate at open
  time too (no stale enable state).
