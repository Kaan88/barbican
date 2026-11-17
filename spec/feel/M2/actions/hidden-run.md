# M2 actions: hidden automation run (Windows, port 8881, profile auto-8881, BARBICAN_SAMPLE=1)

Only through `tools/auto-run.ps1` (the hidden AUTOMATION build: off-screen, non-activating, popup guard on). Sample data only (FakeJmap seed, in process). No real input anywhere.

| Step | Command | Result |
|---|---|---|
| select with shift | `select #1 ctrl`, `select #3 shift` | `ok e36 checked=1`, `ok e24 checked=3` (rows 1..3 ticked, header between them skipped) |
| selection bar frame | `wshot` | 1384x884, not uniform (valid), bar shows "All", "3 selected . 2 unread", Clear, and Archive / Delete / Read / Unread / Spam; the three ticked rows show the accent check |
| delete the open message | `select #1`, `act mail.delete` | `ok mail.delete`; the row leaves in place (no reset: `MailActionTests.Delete_then_undo_removes_and_inserts_rows_without_a_reset`); toast "Message deleted" with Undo and Close |
| toast | `toast-last` | `ok Message deleted\|Undo\|info\|visible` |
| undo | `act mail.undo` | `ok mail.undo`; the row returns, "Undone" toast; the toast that offered the Undo closes with it |
| drop a row on a folder | `dnd-sim #1 folder:archive` | `ok folder 1`, toast "Message moved to Archive" with Undo |
| drop a row on a tag | `dnd-sim #1 tag:work` | `ok tag 1` |
| hover strip | `hover-sim #2` | strip (Delete, Star, Read, Archive) drawn on the row in the frame (wshot), `hover-sim off` clears it |
| panes | `pane-set 220 360`, stop, start again | `panes.json` = `{"sidebar":220,"list":360}`; after the restart `bounds list` = 360 wide and the sidebar pane 220 (applied once at start) |
| popup guard | `popup-guard` | `open 0 closed 0` |
| focus guard | `focus-guard` | `own-is-foreground=no`, cursor unchanged |
| close | `close` | `ok` |

A delete of three ticked rows with `confirmBulkDelete` on asks first; with no dialog host yet (stream menus) the answer is no and nothing is sent: `act mail.delete` replied `ok` and the rows stayed.

## Not measured in this stage

`delete_undo`, `select_range`, `drop_move`, `pane_resize` and `hover_strip` frame-by-frame measurements (burst, `tools/flicker.py`, `tools/motion.py`) were not run: the first frames of the run show the commands work, but no number is claimed. Bento and Nordic, dark and light, port 8881: to do in the integrate stage. Real pointer drag, hover, Ctrl-click and Shift-click on rows and the pane handles are hand tests (HAND TEST 2).

## Bugs the run found and fixed

* The first start of the automation build hung: a dependency cycle in the container (`IMailActions` -> action service -> message list -> opener -> reader -> loader -> `IMailActions`). `LiveMailActions` now takes the service and the list lazily.
* The selection bar clipped its last buttons in a 360 px list; the actions are one row of five equal columns now.
* The toast that offered an Undo stayed after an Undo by key or command; it closes now.

## Tests

Windows `tools/build.ps1 test`: 1052 total, 1048 passed, 0 failed, 4 skipped. Linux (VM reachable, `tools/linux.ps1 test -Branch m2-actions` at ea19149): 1049 total, 1043 passed, 0 failed, 6 skipped (live 3, Windows-only 2, Secret Service 1). The Skia/desktop head builds (`tools/build.ps1 build -Head desktop`); it was not run.
