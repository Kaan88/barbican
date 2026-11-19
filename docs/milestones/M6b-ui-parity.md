# M6b Bulwark UI parity

Bulwark is the default visual direction; see the current
[UI guidelines](../../design/UI-GUIDELINES.md). Compare the shell and reader
first, then the composer and other screens. The older theme studies remain on
hold; retaining a saved legacy appearance must not change the new default.

The reference walkthrough and native parity fixes are required before M6b goes
to Astra. This is a coverage ledger, not a completion report. Reference
observations come from the signed-in throwaway account. Do not track mailbox
contents, identifiers, credentials, or real-account screenshots.

## Existing parity resources

Use the existing catalogues as the inventory; this page records the differences
found in this pass rather than replacing those tables.

| Coverage | Existing reference |
|---|---|
| Features, routes, user workflows and implementation status | [Port plan](../PORT-PLAN.md), [interaction catalogue](../../spec/interactions.csv), [behavior rules](../../spec/README.md#files) and [known gaps](../../spec/gaps.csv) |
| Context-menu entries, availability and destructive confirmations | [Menu catalogue](../../spec/menus.md), including `MENU-MAIL`, `MENU-THREAD`, and `MENU-READER` |
| Settings keys, defaults and wiring | [Settings catalogue](../../spec/settings.csv) |
| Keyboard and command access | [Shortcut rules](../../spec/behaviours/shortcuts.md) and [command palette rules](../../spec/behaviours/palette.md) |
| Plugin contributions and native verification | [Hook and slot catalogues](../../spec/plugins/README.md), [hidden automation protocol](../../spec/automation-protocol.md) |

The catalogues were originally baselined against the Rust app; their presence
does not prove current Bulwark parity. Use the [upstream source](https://github.com/bulwarkmail/webmail)
to check conditional branches that a throwaway account cannot show. Record the
source revision and symbol where possible. Keep each finding's evidence distinct:
**source** for available branches, **live** for paths actually exercised in Bulwark,
and **native** for behavior observed in Barbican. Do not use a screenshot alone to
claim a command works.

### Source cross-index: priority mail flows

Source references below use the project's pinned [Bulwark 1.21.2 reference](../../spec/README.md#pinned-references).
The live walkthrough confirms only the named observations in the next section.
Existing native tests can support specific rows, but the general preview smoke
and suite pass do not establish parity for this table.

| Surface | Bulwark source entry points | Barbican evidence / gap |
|---|---|---|
| Inbox and list threading | `components/email/email-list.tsx` (`EmailList`), `thread-list-item.tsx` (`ThreadListItem`, `ThreadCountPill`), `components/layout/mailbox-context-menu.tsx` | [List layout](../../spec/layout/mail-list.md) is a historical native baseline. `S-disableThreading`, `S-groupListByDate`, and `S-showPreview` are settings anchors. Verify row collapse/expansion, focus, sidebar, count badges and toolbar placement separately from the state of the open reader. |
| Reader message cards | `components/email/thread-conversation-view.tsx` (`ThreadConversationView`) and `email-viewer.tsx` (`EmailViewer`), plus `conversation-message-menu.tsx` | `I-222` and `I-223` cover per-card disclosure and actions; the native automation can select a thread member but does not prove card expansion, action placement, or the older-message disclosure. |
| Quick reply and reply dock | `components/email/email-viewer.tsx` (quick-reply gating and action row), `components/mail/mail-app.tsx` (handoff and send), `lib/outgoing/quick-reply-envelope.ts`, `components/mail/reply-dock.tsx` (`ReplyDock`, `dockLayout`) | `I-217` and `I-219`–`I-221`, `S-quickReplyEnabled` and `S-replyUnderMessage`. On the quick-reply branch, `542e527` verifies plain-text full-editor seeding and signatures/thread headers in Core only. Native gates, focus/growth, Cancel, ownership-safe handoff, direct send/undo, and Ctrl+Enter still need targeted evidence. |
| Composer and separate windows | `components/email/email-composer.tsx` (`EmailComposer`), `reply-type-menu.tsx`, `schedule-send-dialog.tsx`, `send-warnings-dialog.tsx`, and `components/mail/mail-app.tsx` (window flow) | `I-234`–`I-269` and [compose rules](../../spec/behaviours/compose.md) cover many existing interactions. Verify the dock/full-screen/pop-out round trip and close/save/discard under the actual draft and account variants. |
| Settings and keyboard | `lib/settings-schema.ts`, `stores/settings-store.ts`, `components/email/message-action-shortcuts.ts` | Existing settings and shortcuts catalogues list controls and defaults. Check setting behavior after restart and command availability by focus/selection; don't count a page visit or shortcut label as a passing interaction. |

## Mail, threading, quick reply, and compose

| Surface | Observed in Bulwark | Barbican baseline / next verification |
|---|---|---|
| Inbox | Narrow app rail, collapsible folder/tag sidebar, resizable list, and an independent reader. List toolbar holds select-all, search/filters and folder actions. Conversation rows display message counts; multiple counts were directly observed. | Verify the comparable synthetic inbox with hidden automation; compare column structure, row density, focus, and toolbar placement. |
| Conversation | Single conversation row opens a standalone reader route. In an eight-message thread, newest message body was expanded, two older headers were visible, and a “Show 5 older messages” disclosure expanded the remaining headers. After disclosure, seven collapsed message headers were visible. The reader has a conversation toolbar and per-message Reply, Reply all, Forward, More actions, and sender details. | `ReaderView.xaml` currently renders a compact conversation list above a single body. Confirm selection/body behavior in hidden automation and implement the matching collapsed/expanded semantics without sharing one message's authority with another. |
| Quick reply | Pinned at the bottom with a recipient-specific placeholder. On focus the textarea grows and shows Cancel, Open full editor, Assistant, and Send; empty Send is disabled. Open full editor opens a docked reply composer. | The current reply textbox and Send button in `ReaderView.xaml` are disabled. Verify safe recipient/thread binding, plain-text draft handoff, cancel and send enablement, and draft preservation when the reader changes. |
| Composer | Compose opens an in-page editor. Its header offers assistant, Full screen, Open in new window, and Close. Pop-out opens a distinct browser window and removes the docked composer. The pop-out shows From, recipients/contact picker, Cc/Bcc, subject, formatting toolbar and body; the bottom toolbar exposes Attach, Templates, Options, Tags, Assistant, Proofreading, Discard, Send, and Schedule send. Empty new-message Send/Schedule are disabled. | Compare new mail and reply independently, including dock/full-screen/pop-out transitions, focus, attachment and template menus, quote visibility, and draft lifecycle. |
| Settings | Message list exposes conversation grouping, date grouping, previews, sort levels, and quick actions. Reading exposes mark-read timing and message display. Composing has a “Replies under the message” switch. | Check native settings and restart persistence against actual behavior, not only the labels. |

The conversation and individual-message action menus differ: the conversation
menu offered forwarding as an attachment, rules, pin, message/conversation links,
read-aloud, reader view, plain text, EML export and source; the expanded message's
menu offered Print. Sender details expand inline rather than opening a separate
dialog. Reader fullscreen hides the sidebar/list, retains the app rail, and
offers Exit fullscreen without opening a new window.

## Other reference surfaces entered

- Calendar: month view and Create event entry; editor walkthrough pending.
- Contacts: category drawer and New menu (contact, group, address book, import);
  individual forms still pending.
- Files: list, sorting, details entry, New menu (uploads, folder, text file),
  and New folder dialog with empty Create disabled.
- Settings: all top-level routes entered: Account, Password & sign-in, Privacy
  & security, Sharing, Notifications, General, Offline, Appearance, Accessibility,
  Message list, Reading, Composing, Folders & tags, Filters, Out of office, Recover
  deleted items, Calendar, Contacts, Files, Add-ons, Assistant, Backup & import,
  Troubleshooting and About. Add-ons separates sidebar apps and plugins,
  exposes an install action and shows expandable installed entries with toggles.
  Nested controls and write variants are not fully exercised.

Capture sanitized reference layouts and use synthetic fixtures for any committed
native images. Track unavailable capability variants explicitly rather than
checking off inaccessible or untried controls.

## First native pass

- Reader: actions and subject now precede the collapsed conversation headers.
  An eight-message presentation test verifies two visible earlier headers and
  “Show 5 older messages.” Hidden Windows automation on the five-message
  sample opened `e21`, revealed older headers, activated `e8` through its header
  button, and confirmed `e8 Ready`. There is still only one message body surface.
  Per-message actions and the expanded-card layout still need a parity pass.
- Mail shell: Bulwark uses flush panes with default rail/sidebar/list widths
  of 56/256/384 effective px, without the legacy glance/context panels.
  The Windows title band remains. An integrated hidden capture puts the
  list and reader at x312 and x696, matching the reference's pane starts.
- Composer and quick reply: Bulwark uses a flat header, ruled address and
  subject rows, and a separate footer. Integrated hidden automation verified
  cancelling typed text, switching messages without carrying the previous text,
  and opening the docked reply with the intended recipient and text above
  the quote. Inline send/undo and composer full-screen/pop-out are still open.
- Inbox grouping: the screenshot still shows duplicate rows for a thread.
  `MessageListViewModel` does not pass `CollapseThreads` to `ListPageFetcher`;
  the setting currently controls only the reader. Avoid visual deduplication
  that would break paging and message-action ownership.

## Astra review and follow-up

Astra reviewed `6de166e` and requested changes. Approval remains open.
Priority gaps: expandable inbox conversations with correct paging and action
targets; independently expandable reader cards with per-message actions and
the older-message disclosure between the first and last visible headers;
recipient-aware inline quick reply with send and undo; adaptive dock and window
transitions; and inbox toolbar controls. The prior light/dark captures were
identical, and the docked-reply capture preceded its rendered layout. Neither
is accepted as parity evidence.

Reader fullscreen follow-up: native hidden Windows automation verified the
reader moved from x696 (728 px wide) to x56 (1368 px wide) while the app rail
remained at x0. Exit restored the reader to x696 and the list to x312.
This is an in-app reader mode, not a new window. Keyboard focus restoration
still needs targeted verification.

Inbox chrome follow-up: the default list toolbar places select-all and folder
actions on either side of search; the optional sidebar Compose placement
uses the existing compose command. Hidden automation measured both 36 px
controls, activated the selection bar and opened the folder menu (Refresh,
Mark folder as read, Select all). That menu is still a subset of Bulwark.
This does not change conversation grouping.

Replacement captures were made with the XAML renderer to avoid a stale
compositor frame. At 1440×900 window size, the light and dark reader captures
had different SHA-256 prefixes (`76797478090b` and `00cc55626018`).
The dock capture (`d0c5697e03e7`) was taken only after the subject and Send
controls reported nonzero bounds. These captures verify native chrome, not
WebView message content; they are local synthetic artifacts.

Adaptive reply dock: the short layout now switches between the message and the
same composer instance, while a tall layout gives the composer a 60% initial
share. Seven layout tests passed. Hidden sample automation measured a 508 px
dock in an 846 px pane and preserved the draft subject through the short-layout
round trip. A draggable divider remains open. The Windows x64 preview at this
checkpoint has an [extraction and smoke record](../../spec/feel/M6b/windows-bulwark-preview.json).
