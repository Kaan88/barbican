# Behaviours: drag and drop, resizable panes (`src/dnd.rs`)

Area: `DND`. Owner milestone: M2 (mail and folders, panes), M4 (events), M5 (files).
Added at the M0 review (stream m0-review): the catalogue streams had no row for this module.
Rust tests: T-083..T-090. Interaction rows: see `spec/interactions.csv` (drag rows are `D` by code path,
never driven live; `spec/live-checks.md`).

Bulwark: `lib/folder-tree-dnd.ts`, `stores/email-store.ts` `moveMailbox`, `components/layout/resize-handle.tsx`,
`stores/ui-store.ts` (pane limits), file-browser drop targets, calendar day/week grid drag.

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-DND-001 | Dragging a message row drags the selection when the row is in it, else the row alone. The drag label is the subject ("(no subject)"), or "Move N messages". | dnd.rs:21-50 | Bulwark mail-app drag | none |
| B-DND-002 | Dropping mail on a folder moves it; messages whose only folder is already the target are skipped, and drops on the Scheduled or unified views are ignored. Dropping on a tag adds the tag. | dnd.rs:42 `already_in`, `drop` | Bulwark sidebar drop targets | T-083 |
| B-DND-003 | Folder tree: drop above/below a folder puts it next to that folder (under its parent); drop onto it nests it as the last child. The new parent's children get `sortOrder` 1..n in their new order, and only changed folders are written (`Mailbox/set`); role folders never get a new `sortOrder`. After the write the tree is always re-read, never the optimistic order (Bulwark #855). | dnd.rs:146-186 `plan_folder_drop` | folder-tree-dnd.ts; email-store moveMailbox | T-086, T-087 |
| B-DND-004 | Sibling order as the sidebar shows it: role folders first, then `sortOrder`, then name. | dnd.rs:120-133 | Bulwark sidebar sort | T-086 |
| B-DND-005 | A folder cannot be dropped into itself or a descendant ("A folder can't go inside itself"). Role folders may be reordered among their siblings but never re-parented ("Special folders (Inbox, Sent, …) stay where they are"). | dnd.rs:134-160 | folder-tree-dnd.ts | T-088 |
| B-DND-006 | Files: a node is moved only where "Move to…" would offer it (not into itself or below it); dropping on its own parent is a no-op, anything else toasts "Can't move it there". Dropping on the trash only ever moves to the trash: an item that would be deleted for good toasts "This would be deleted for good. Select it and press Delete instead." | dnd.rs:90-113 | Bulwark file-browser | none |
| B-DND-007 | Events move and resize in 15-minute steps across day columns; a resize never goes below one step, and a tiny drag snaps back to no change. | dnd.rs:188-202 `STEP_MIN`, `moved_event` | Bulwark calendar grid | T-084, T-089 |
| B-DND-008 | A moved event is saved with `sendSchedulingMessages` only when it has attendees, so the server notifies them of the new time. Durations are written as ISO 8601 (`PT1H30M`). | dnd.rs:203-217 `iso_minutes`, `update_args` | RFC 8984; JMAP Calendars | T-085 |
| B-DND-009 | Pane resize limits: sidebar 180-400 px, list 240-600 px; 0 (no width saved) means the style's own width (Bento/Nordic). Non-finite or non-positive values fall back to the default. Widths persist (rounded) in `panes.json`. Double-click on a handle restores the default (module header; the Slint handle code was not re-read). | dnd.rs:241-270 | resize-handle.tsx; ui-store | T-090 |

## Open questions
- Hidden automation cannot drag in-process (ADR 0007, S4 finding): drag rows need a UIA or synthetic
  path, or stay [HAND TEST]. Decide at M2 planning.
