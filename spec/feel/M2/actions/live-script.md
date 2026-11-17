# M2 actions: live script (written, NOT run; the integrate stage runs it)

Sequential, on `<test-account>` only, with the `AUTOMATION_WRITES` build (`tools/build.ps1 build -Configuration Automation -Writes`), the profile the integrator chose, port 8881.
`<readonly-account>` is never written. Every step that creates something removes it again; the last step proves it.
`rx-import`, `rx-find`, `rx-destroy` are stream reader's commands (`src/Barbican.Automation/Writes/`); `act`, `select`, `dnd-sim`, `toast-last` are in `spec/automation-protocol.md`.

1. `rx-import MXACT-1 inbox` and `rx-import MXACT-2 inbox`: two messages with a token in the subject. Expect both rows at the top of the Inbox (push delta, no reload).
2. Star and tag: `select #0`, `act mail.star`, `act mail.tag work`, `toast-last`; check `rx-find MXACT-1` shows `$flagged` and `$label:work`; `act mail.unstar`, `act mail.untag work`. (L-044 tags: create a nested tag through the prefs, rename it, delete it; the messages keep the keyword.)
3. Folder: `act folder.new MXACT-Test`, wait for the sidebar row (Mailbox state push), `dnd-sim #0 folder:MXACT-Test`, expect the row to leave and the toast with Undo; `act mail.undo` brings it back; drop again.
4. L-039, delta after an action: after step 3 no `Email/query` full reload is sent (`tools/jmap-trace`: only `Email/changes` / `Email/queryChanges` after the write).
5. Delete and undo: `select #0`, `act mail.delete` (to Trash), `toast-last`, `act mail.undo`; then `act mail.delete` again, open Trash, `act mail.delete-forever` (a single message asks no question; several do: expect the dialog), `rx-find MXACT-1` finds nothing.
6. Empty a test folder: move both messages into `MXACT-Test`, `act folder.empty-trash` is for Trash only; for the test folder use the folder menu item once menus lands (`act` has no folder argument for it): until then `rx-destroy MXACT-2`.
7. Archive by month: set `archiveMode` to `month` through the prefs, `act mail.archive` on a marked message, expect `Archive/<year>/<month>` created; move it back, delete the two dated folders (`act folder.delete`, confirm).
8. L-057: remove the Rust leftovers "MXTEST ..." from the Inbox: `rx-find MXTEST` then `rx-destroy` each (Rust-created test mail only).
9. Cleanup proof: `rx-find MXACT` returns nothing, the folder `MXACT-Test` is gone, `popup-guard` shows 0, `focus-guard` unchanged, `close`.

Real drag, hover, Ctrl-click and Shift-click on rows, and the pane handles are HAND TEST 2.
