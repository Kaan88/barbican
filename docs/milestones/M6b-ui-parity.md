# M6b Bulwark UI parity

The reference walkthrough and native parity fixes are required before M6b goes
to Astra. This is a coverage ledger, not a completion report. Reference
observations come from the signed-in throwaway account. Do not track mailbox
contents, identifiers, credentials, or real-account screenshots.

## Mail, threading, quick reply, and compose

| Surface | Observed in Bulwark | Barbican baseline / next verification |
|---|---|---|
| Inbox | Narrow app rail, collapsible folder/tag sidebar, resizable list, and an independent reader. List toolbar holds select-all, search/filters and folder actions. Conversation rows display message counts; multiple counts were directly observed. | Verify the comparable synthetic inbox with hidden automation; compare column structure, row density, focus, and toolbar placement. |
| Conversation | Single conversation row opens a standalone reader route. In an eight-message thread, newest message body was expanded, two older headers were visible, and a “Show 5 older messages” disclosure expanded the remaining headers. After disclosure, seven collapsed message headers were visible. The reader has a conversation toolbar and per-message Reply, Reply all, Forward, More actions, and sender details. | `ReaderView.xaml` currently renders a compact conversation list above a single body. Confirm selection/body behavior in hidden automation and implement the matching collapsed/expanded semantics without sharing one message's authority with another. |
| Quick reply | Pinned at the bottom with a recipient-specific placeholder. On focus the textarea grows and shows Cancel, Open full editor, Assistant, and Send; empty Send is disabled. Open full editor opens a docked reply composer. | The current reply textbox and Send button in `ReaderView.xaml` are disabled. Verify safe recipient/thread binding, plain-text draft handoff, cancel and send enablement, and draft preservation when the reader changes. |
| Composer | Compose opens an in-page editor. Its header offers assistant, Full screen, Open in new window, and Close. Pop-out opens a distinct browser window and removes the docked composer. The pop-out shows From, recipients/contact picker, Cc/Bcc, subject, formatting toolbar and body; the bottom toolbar exposes Attach, Templates, Options, Tags, Assistant, Proofreading, Discard, Send, and Schedule send. Empty new-message Send/Schedule are disabled. | Compare new mail and reply independently, including dock/full-screen/pop-out transitions, focus, attachment and template menus, quote visibility, and draft lifecycle. |
| Settings | Message list exposes conversation grouping, date grouping, previews, sort levels, and quick actions. Reading exposes mark-read timing and message display. Composing has a “Replies under the message” switch. | Check native settings and restart persistence against actual behavior, not only the labels. |

## Other reference surfaces entered

- Calendar: month view and Create event entry; editor walkthrough pending.
- Contacts: category drawer and New menu (contact, group, address book, import);
  individual forms still pending.
- Files: list, sorting, details entry, New menu (uploads, folder, text file),
  and New folder dialog with empty Create disabled.
- Settings: Message list, Reading, Composing, Folders & tags, Filters, Add-ons
  and Assistant routes entered. Add-ons separates sidebar apps and plugins,
  exposes an install action and shows expandable installed entries with toggles.
  Remaining settings routes and nested controls are pending.

Capture sanitized reference layouts and use synthetic fixtures for any committed
native images. Track unavailable capability variants explicitly rather than
checking off inaccessible or untried controls.
