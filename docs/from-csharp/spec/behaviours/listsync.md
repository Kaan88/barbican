# Behaviours: incremental list updates (B-LST)

Area: how a shown list model is turned into fresh rows with the fewest notifications, so a
refresh keeps hover and press states and does not rebuild the list.
Rust source (`rust-final`): `desktop/src/listsync.rs` (295 lines; tests :175-288) and the
callers: mail list, folder list and tags (commit ea190d4), PIM incremental models (commit 464e518),
compose models (commit 38bf81f). Design rule: `design/MOTION.md` "Lists".

| ID | Rule | Source (Rust) | Bulwark ref | C# status |
|---|---|---|---|---|
| B-LST-001 | `sync_into`, `sync_by`, `sync_model` and `rows_eq` turn the model on screen into the new rows. They match rows by a key (message or folder id). | listsync.rs header (lines 1-10); functions in the module | n/a | ported (m1 mailcore: ListSync, BulkObservableCollection, MessageListViewModel; tests ListSyncTests, MessageListViewModelTests) |
| B-LST-002 | A row whose content is unchanged is not touched at all. | listsync.rs test `unchanged_rows_are_not_touched` (:175) | n/a | ported (m1 mailcore: ListSync, BulkObservableCollection, MessageListViewModel; tests ListSyncTests, MessageListViewModelTests) |
| B-LST-003 | A changed row gets `set_row_data` in place. | listsync.rs test `changed_row_is_set_in_place` (:182) | n/a | ported (m1 mailcore: ListSync, BulkObservableCollection, MessageListViewModel; tests ListSyncTests, MessageListViewModelTests) |
| B-LST-004 | A new row is an insert; a gone row is a remove. A new message on top is one insert; a deleted row is one remove. | listsync.rs tests `new_mail_on_top_is_one_insert` (:190), `deleted_row_is_one_remove` (:198) | n/a | ported (m1 mailcore: ListSync, BulkObservableCollection, MessageListViewModel; tests ListSyncTests, MessageListViewModelTests) |
| B-LST-005 | Load-more appends the new rows. | listsync.rs test `load_more_appends` (:206) | n/a | ported (m1 mailcore: ListSync, BulkObservableCollection, MessageListViewModel; tests ListSyncTests, MessageListViewModelTests) |
| B-LST-006 | Mixed insert, remove, update and move are applied as row notifications. | listsync.rs test `mixed_insert_remove_update_and_move` (:214) | n/a | ported (m1 mailcore: ListSync, BulkObservableCollection, MessageListViewModel; tests ListSyncTests, MessageListViewModelTests) |
| B-LST-007 | MAX_MOVES (8) bounds the moves applied row by row; a larger reorder resets (test `big_reorder_resets`). | listsync.rs:38 `MAX_MOVES` (8); test `big_reorder_resets` (:237) | n/a | ported (m1 mailcore: ListSync, BulkObservableCollection, MessageListViewModel; tests ListSyncTests, MessageListViewModelTests) |
| B-LST-008 | A mostly new list (another folder, a new search) is one reset. | listsync.rs test `mostly_new_list_resets_once` (:228) | n/a | ported (m1 mailcore: ListSync, BulkObservableCollection, MessageListViewModel; tests ListSyncTests, MessageListViewModelTests) |
| B-LST-009 | Filtering that keeps only a few rows resets. | listsync.rs test `filter_keeping_few_rows_resets` (:248) | n/a | ported (m1 mailcore: ListSync, BulkObservableCollection, MessageListViewModel; tests ListSyncTests, MessageListViewModelTests) |
| B-LST-010 | Duplicate keys make the sync reset (rows cannot be matched). | listsync.rs test `duplicate_keys_reset` (:257) | n/a | ported (m1 mailcore: ListSync, BulkObservableCollection, MessageListViewModel; tests ListSyncTests, MessageListViewModelTests) |
| B-LST-011 | An empty list and a clear are handled as their own cases. | listsync.rs test `empty_and_clear` (:265) | n/a | ported (m1 mailcore: ListSync, BulkObservableCollection, MessageListViewModel; tests ListSyncTests, MessageListViewModelTests) |
| B-LST-012 | `sync_into` installs the model once, then syncs in place on later calls. | listsync.rs test `sync_into_installs_once_then_syncs_in_place` (:277) | n/a | ported (m1 mailcore: ListSync, BulkObservableCollection, MessageListViewModel; tests ListSyncTests, MessageListViewModelTests) |
| B-LST-013 | Nested rows (rows that contain lists) compare by content. | listsync.rs test `nested_rows_compare_by_content` (:288) | n/a | ported (m1 mailcore: ListSync, BulkObservableCollection, MessageListViewModel; tests ListSyncTests, MessageListViewModelTests) |
| B-LST-014 | Used for the mail list, the folder list and tags (refreshes keep hover, do not rebuild rows). | commit ea190d4 | n/a | ported (m1 mailcore: ListSync, BulkObservableCollection, MessageListViewModel; tests ListSyncTests, MessageListViewModelTests) |
| B-LST-015 | Used for the calendar, contacts and files models and the composer's compose models. | commits 464e518, 38bf81f | n/a | pending (M4/M5/M3) |
| B-LST-016 | A list refresh does not show a Loading row above the list. | commit b72743d | n/a | ported (m1 mailcore: ListSync, BulkObservableCollection, MessageListViewModel; tests ListSyncTests, MessageListViewModelTests) |
| B-LST-017 | Mail list: delta sync for menu actions uses Email/queryChanges and Email/changes, not a full reload. | commit 5217a5c | n/a | partial (M1 mailcore: list applies ListDeltaEvent in place; the engine is jmap; menu actions M2) |
