# Feel results: stream menus (M2)

Run: hidden AUTOMATION build, port 8882, profile `auto-8882`, `BARBICAN_SAMPLE=1`, only through `tools/auto-run.ps1`.

## Hidden scenario (passes)
`menu-open MENU-MAIL 0`, `menu-items`, `menu-pick "Move to"` (the flyout opens beside the row and stays inside the window), `wshot` (1384x884, not uniform, valid), `key Escape` (menu closed),
`rclick folder Inbox` (MENU-FOLDER: header Inbox, role folder: Rename disabled, no danger rows), `palette "go inbox"` (1 row, "Go to Mail"), `palette-pick 0`, `key ?` + `wshot` (the sheet, not uniform),
`dialog-open prompt "New folder"`, `dialog-state` (`open PromptDialogRequest title=New folder`), `wshot`, `dialog-answer cancel`, `popup-guard` (`open 0 closed 0`), `focus-guard` (`own-is-foreground=no`).
Frames: `artifacts/auto/8882/` of the run (not committed: sample data only, but the folder is untracked build output).

Rows show disabled (`!`) in the run because no stream registered the mail commands yet: a command without a handler shows disabled (M2 plan 4.1), as designed.

## Frame feel scenarios (ctx_menu, dialog, palette_open; Bento and Nordic, dark and light)
**Unproven (capture invalid).** The WGC probe of an off-screen, never-presented window produced 0 frames in the F and menus runs (display may be off), so `tools/flicker.py` and `tools/motion.py` have nothing to measure.
In-process facts that bound the cost: the menu card and the flyout are built once per open (a plain `StackPanel` of 13 to 15 rows, no template pass), highlight changes recolour the rows in place, `PopIn.Run` is the
shared 150 ms (`Motion.enter`, 0 with reduce motion) and nothing waits for it; the dialog scrim fades in over the same token and closes instantly; the palette keeps its old rows until the new list is ready.
The frame gate goes to the user's on-screen run (HAND TEST 2): right-click a row, Escape; open the Move-to or a prompt dialog and close it; Ctrl+K, type, Esc.
