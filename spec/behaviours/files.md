# Behaviours: files (B-FILE)

Area: JMAP File Storage (FileNode): browse, trash, sharing, sidebar, keys.
Rust sources (`rust-final`): `desktop/src/files.rs`, `shortcuts.rs` (files keys), `ui/` file
views (BNContacts/Files restyle, commit 02ab6c7). Rows from headers, constants, tests and commits.

| ID | Rule | Source (Rust) | Bulwark ref | C# status |
|---|---|---|---|---|
| B-FILE-001 | Both FileNode shapes are read: Stalwart 0.16 (`nodeType`, `type`) and 1.0 / draft-18 (`@type`, `mediaType`). | files.rs:1-2 header; commit d1a75e9 | files/file-browser.tsx | pending (M5) |
| B-FILE-002 | A new folder is created on a draft-18 server when the empty drive's supportedNodeTypes says so. | commit 96dfe5c | files | pending (M5) |
| B-FILE-003 | Nodes of every account with the filenode capability are listed. A node key is the bare id in the primary account and `account\|id` in a shared one. | files.rs:7-8 | files-sidebar.tsx | pending (M5) |
| B-FILE-004 | Requested properties (13): id, parentId, name, type, blobId, size, created, modified, nodeType, @type, mediaType, shareWith, myRights. | files.rs:36 `PROPS` | file-browser.tsx | pending (M5) |
| B-FILE-005 | Sidebar: My files (tree), Favorites, Recent, Shared with me. | files.rs:3 header; commit db53d83 | files-sidebar.tsx | pending (M5) |
| B-FILE-006 | Star, share (shareWith to other users), move, cut, copy, paste and duplicate. Blobs are per account. | files.rs:4-5 header; commit db53d83 | file-browser.tsx | pending (M5) |
| B-FILE-007 | Trash is a `.Trash` folder (files.rs:73 `TRASH_NAME`, role location `*trash`, :75). Each trashed batch is a dated folder. | files.rs:73-75; commit 4c1c383 | lib/files trash | pending (M5) |
| B-FILE-008 | Trash listing and batch names are computed by tested pure functions. | files.rs tests `trash_batch_names` (:779), `trash_listing_and_names` (:790) | files trash | pending (M5) |
| B-FILE-009 | Restore, delete forever and empty trash exist. Empty trash asks first; an empty trash shows its own empty state. | commits 4c1c383, c5ec09e, 2fbd691 | files trash | pending (M5) |
| B-FILE-010 | An Upload folder exists alongside trash. | commit 4c1c383 | files | pending (M5) |
| B-FILE-011 | Permanent deletes of files ask for confirmation. | commit d16233d | files | pending (M5) |
| B-FILE-012 | Keys: arrows move, Home and End go to the ends, Enter opens, F2 renames (inline, focused), Delete or Backspace deletes, Ctrl+C, Ctrl+X, Ctrl+V copy, cut and paste, Alt+Up goes up one folder, Shift+F10 opens the menu, Esc clears. | shortcuts.rs:222 `files`; tests `list_keys` (:796), `stepping_through_a_list` (:812) | files-browser key handling | pending (M5) |
| B-FILE-013 | Files drop onto folders and onto the trash (drag and drop). | commit 8253677 | files drag and drop | pending (M5); live check L-022 |
| B-FILE-014 | File star sits beside the name; file dates follow timeFormat. | commit c5ec09e | file list | pending (M5) |
| B-FILE-015 | Narrow Files layout; the places tile wraps; tree indents; category chips in contacts/files. | commit 2fbd691 | files layout | pending (M5) |
| B-FILE-016 | Save to Files copies the attachment from the message's own (possibly shared) account. | commits 645004f, 4ad1e78 | files / reader | pending (M2 reader, M5 files) |
| B-FILE-017 | Files are restyled to the Bento and Nordic designs: Upload button, places tile with line icons, breadcrumb title, type tiles, line-icon row actions. | commit 02ab6c7 | design/mockups | pending (M5) |
| B-FILE-018 | Show-as, sort, thumbnails, hidden files and the other Files display rows are not wired in Rust (dropped keyless rows). | GAPS P1-3; settings.csv | settings files tab | open: G-015, G-019, G-069 |
| B-FILE-019 | In-app preview, details panel, grid view, upload conflict dialog, zip download and new text file are not in Rust. | GAPS P1-3 | files-preview, file-details-panel, upload-conflict-dialog, files-zip | open: G-013 to G-018 |
