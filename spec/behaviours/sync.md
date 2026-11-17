# Behaviours: list sync, body cache and message switching

Area: `SYNC`. Owner milestone: M1 (all rows except where marked).

Sources: `src/listsync.rs`, `src/bodycache.rs`, `src/msgload.rs`, `src/mailview.rs` (header), `src/mail.rs`, and the push path in `src/jmap.rs` (rules B-JMAP-045 to B-JMAP-049 are in `jmap.md`). Pins as in `jmap.md`.

Feel rules are measured, not eyeballed (PORT-PLAN section 4.5): every row marked **feel** needs a probe result in `spec/feel/M1/` no worse than `spec/feel/rust-baseline/`.

## List model updates (`listsync.rs`)

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-SYNC-001 | Replacing a list model wholesale rebuilds every row (flash, lost hover and press states). New rows are instead computed as a diff against the model on screen, so the fewest row notifications are sent. | listsync.rs header (design/MOTION.md "Lists") | design/MOTION.md | `unchanged_rows_are_not_touched` (feel) |
| B-SYNC-002 | Rows are matched by a key: message id, folder id, or the mailbox id. | listsync.rs:42 `sync_by` | | `changed_row_is_set_in_place` |
| B-SYNC-003 | An unchanged row is not touched at all. | listsync.rs:42 `sync_by` | | `unchanged_rows_are_not_touched` |
| B-SYNC-004 | A changed row is set in place (`set_row_data`), one notification. | listsync.rs:42 | | `changed_row_is_set_in_place` |
| B-SYNC-005 | A new row is one insert; a gone row is one remove; load-more appends. | listsync.rs:42 | | `new_mail_on_top_is_one_insert`, `deleted_row_is_one_remove`, `load_more_appends`, `mixed_insert_remove_update_and_move` |
| B-SYNC-006 | Duplicate keys in the new list cause one reset. A list is never corrupted by duplicates. | listsync.rs:42 (`wanted.len() != new_keys.len()`) | | `duplicate_keys_reset` |
| B-SYNC-007 | More than `MAX_MOVES` (8) moves, or more than 8 rows gone and more gone than kept, is a reset. A reorder with moves above 8 is a reset. | listsync.rs:38, :64, :89 | | `big_reorder_resets`, `mostly_new_list_resets_once`, `filter_keeping_few_rows_resets` |
| B-SYNC-008 | The first fill, a clear, another folder, or a new search is a single reset (nothing on screen is worth keeping). | listsync.rs:42 | | `empty_and_clear`, `filter_keeping_few_rows_resets` |
| B-SYNC-009 | `sync_into` installs the model once, then syncs in place on each later call. | listsync.rs:122 `sync_into` | | `sync_into_installs_once_then_syncs_in_place` |
| B-SYNC-010 | Nested row values compare by content, not by identity. | listsync.rs:160 `rows_eq` | | `nested_rows_compare_by_content` |
| B-SYNC-011 | A sync with nothing in and nothing shown is untouched. | listsync.rs:42 | | `empty_and_clear` |

## Body and conversation cache (`bodycache.rs`)

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-SYNC-012 | Bodies are cached per account (`state_key`) and message id. A body never changes in JMAP; only keywords and mailboxes do. | bodycache.rs:1-11 | | `per_account` |
| B-SYNC-013 | The body cache holds at most 120 entries and 48 MiB (`MAX_ENTRIES`, `MAX_BYTES`), least recently used out first. | bodycache.rs:16-17 | | `get_put_and_lru_by_count`, `bounded_by_bytes_keeps_newest` |
| B-SYNC-014 | A `get` refreshes recency. | bodycache.rs:87 `get` | | `get_put_and_lru_by_count` |
| B-SYNC-015 | Replacing an entry keeps the byte total exact. Clearing returns the total to zero. | bodycache.rs:98 `put` | | `replacing_an_entry_keeps_the_byte_count` |
| B-SYNC-016 | Weight of an entry counts headers and body values (so a large header counts). | bodycache.rs:40 `thread_weight`, :45 `weight` | | `weight_counts_headers`, `weight_counts_body_values` |
| B-SYNC-017 | Conversations (the thread strip) are cached per thread id: at most 120 threads and 8 MiB (`MAX_THREADS`, `MAX_THREAD_BYTES`). | bodycache.rs:18-19, :136 `put_thread` | | `conversations_are_bounded_by_bytes` |
| B-SYNC-018 | A list sync patches keywords and mailbox ids into cached bodies, keeps the body text, drops removed messages and drops any thread the sync touches. | bodycache.rs:114 `patch`, :123 `remove`, :150 `drop_thread`, :187 `on_sync` | | `sync_patches_keywords_and_drops_removed_and_threads` |
| B-SYNC-019 | Local actions (read, flag, tag, move, delete) patch or drop the cached copies in every account and thread. The strip's row follows. A conversation that lost a message is re-read. | bodycache.rs:159 `patch_local`, :175 `remove_local` | | `local_actions_reach_bodies_and_conversations` |
| B-SYNC-020 | A fetch started before a sign-out or clear (generation check) stores nothing. | bodycache.rs:69 `generation`, :72 `put_gen` | | `a_fetch_from_before_a_clear_is_not_stored` |
| B-SYNC-021 | The cache is cleared at sign-out. | bodycache.rs:196 `clear` | | none |

## Message open and switching (`msgload.rs`, `mailview.rs`)

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-SYNC-022 | **feel.** The message on screen (header and body) stays until the next message is ready, then both change together, when the body view reports the new page is laid out. | msgload.rs:1-4 | `mat-switching` WIP intent (PORT-PLAN 2.6): the old message stays whole under the bar, dimmed, until the new one swaps in | none |
| B-SYNC-023 | **feel.** The mailview is one persistent document that never navigates. Each message is a sandboxed frame loaded hidden beside the one shown, then swapped in. Switching never shows a blank or white page. | mailview.rs:1-5 | | none |
| B-SYNC-024 | **feel.** The shell reports `ready:N` before the swap, so the native header changes in the same moment as the body. | mailview.rs:4-5 | | none |
| B-SYNC-025 | While loading takes more than 150 ms, a thin progress bar appears (not earlier). | msgload.rs:7, :136 | | none |
| B-SYNC-026 | While a message is loading, reply, forward and attachments wait for the new message. The list's actions already target the selected row. | msgload.rs:5-6 | | none |
| B-SYNC-027 | From an empty reader the header is filled from the list row at once. | msgload.rs:13, :175 `prefill` | | none |
| B-SYNC-028 | An open response that arrives after the user moved on is dropped (sequence check `OPEN_SEQ` against the current id). | msgload.rs:100-175 | | none |
| B-SYNC-029 | If a fetch fails, nothing of the previous message stays: header and reply target are cleared. | msgload.rs:150-160 | | none |
| B-SYNC-030 | Neighbours in the list are prefetched, further ahead in the direction of travel, and the top message is prefetched when the list fills. Prefetch waits up to 5 s for an in-flight prefetch of the same id. | msgload.rs:320 `prefetch`, :351 `prefetch_top`, :357 `fetch_ahead`, :151 | | none |
| B-SYNC-031 | Inline images count toward a budget; a message whose inline bytes exceed `PREFETCH_INLINE_MAX` (4 000 000 bytes) is not prefetched. The reason is in the source comment; confirm before porting. | msgload.rs:32, :50 `inline_bytes` | | `inline_image_size` (5 MB case) |
| B-SYNC-032 | Unread state of an opened message comes from the list's copy first, then the fetched copy. The list is the newer source. | msgload.rs:45 `was_unseen` | | `unread_state_comes_from_the_list_first` |
| B-SYNC-033 | Mark as read follows the `markAsReadDelay` setting (default 0). | msgload.rs:36 `mark_delay`, :202 `mark_read` | | none |
| B-SYNC-034 | Threading can be turned off with `disableThreading`; then no conversation is fetched. | msgload.rs:34 `threading` | | none |
| B-SYNC-035 | The forced actions `read!` and `unread!` set the state and never toggle it. | mail.rs:960 `forced` | | `forced_read_never_toggles` |

## List paging and refresh (`mail.rs`)

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-SYNC-036 | The message list is paged 50 at a time (`PAGE`). Load more anchors on the last loaded message (B-JMAP-027). | mail.rs:52 `PAGE` | | none (live) · **C#:** ported (pages of 50, PageSize; load more anchors on the last row: MailServiceTests.Load_more_pages_by_anchor_so_new_mail_does_not_shift_it) |
| B-SYNC-037 | Push (EventSource) changes refresh the list through the delta (B-JMAP-020); a full reload only when the delta is refused (B-JMAP-019). | mail.rs `sync`, `reload`; jmap.rs push | | `list_delta_*` · **C#:** ported (MailSync: push → coalesced delta, ListResetEvent only when no delta: PushTests.Open_push_delta_never_resets_the_list, MailSyncTests.No_delta_resets_the_list) |
| B-SYNC-038 | Window title shows the unread count in brackets, or plain "Barbican" at zero. | mail.rs:203 `badge_title` | | `unread_badge_title` |

## Open questions

- B-SYNC-031: the reason for `PREFETCH_INLINE_MAX` is in a source comment that was not re-read in this pass. Confirm before porting.
- B-SYNC-022 cites the intent of the `mat-switching` WIP branch. That branch is not on `main`; it is pushed to the Rust repository as a `mat-*` branch (see `docs/legacy/branches.md`). Its diff was not read in this pass. The port should read it before M1 switching work.

## M1 status (stream mailcore)

Ported in `m1-mailcore`: B-SYNC-001..011 (`ListSync`, `ListSyncTests`, `MessageListViewModelTests`), B-SYNC-035 (`MailRules.Forced`, `MailRulesTests`),
B-SYNC-036 (50 per page, anchor paging in `MessageListViewModel.LoadMoreAsync`; the page size follows `emailsPerPage`), B-SYNC-037 (list side: a pushed
delta is one insert; the engine is the jmap stream), B-SYNC-038 (`MailRules.BadgeTitle`, `ShellViewModel.SetUnreadBadge`). B-SYNC-022..034 belong to the
body stream (`MessageLoader`); `ReaderViewModel.Apply` gives them the one-step swap.

## M1 status (stream body)

Ported in `m1-body` (`MessageLoader`, `MessagePrefetcher`, `PrefetchPlan`, `MessageFetcher`, `BodyShell`; tests `MessageLoaderTests`, `PrefetchPlanTests`,
`MessageLoadRulesTests`): B-SYNC-022 and B-SYNC-024 (the header is applied in the body view's ready callback, in the same UI turn as `reveal(N)`:
`A_cached_switch_applies_header_and_body_in_one_apply`; on screen this rests on the code, the hidden WGC bursts cannot show it, spec/feel/M1/README.md),
B-SYNC-023 (one shell document that never navigates, sandboxed srcdoc frames loaded hidden: `BodyShell`, `shell.js`, spec/feel/M1/shell-checks.json),
B-SYNC-025 (150 ms, once per burst), B-SYNC-027 (prefill), B-SYNC-028 (sequence + cancellation; a superseded page is refused at ready), B-SYNC-029,
B-SYNC-030 (two ahead, three while moving, one behind, conversation neighbours, the list's top two; an open waits up to 5 s for its own prefetch, and a
prefetch cancelled by a turn makes the open fetch itself), B-SYNC-031 (`PrefetchInlineMax`, T-186), B-SYNC-032 (T-185), B-SYNC-033 (the delay logic is
ported and tested with writable actions; M1 registers `ReadOnlyMailActions`, so nothing is sent), B-SYNC-034 (`disableThreading`).
B-SYNC-026: deferred(M2): reply, forward and attachment actions do not exist in M1 (the reader's buttons are disabled), so there is nothing to hold back yet.

## M1 status (verify)

Rows no stream marked, settled at the M1 verify stage from the code and the ported Rust tests:
B-SYNC-012..018 and B-SYNC-020: ported (m1 accounts: `BodyCache`, `BodyCacheSync`; T-025..T-034 `BodyCacheTests`). The bounds are global for all
accounts, as in Rust, and `Weight` counts UTF-16 code units where Rust counted UTF-8 bytes (Fable log, accounts review).
B-SYNC-019: partial (the cache side is ported and tested, `local_actions_reach_bodies_and_conversations`); the local actions themselves: deferred(M2).
B-SYNC-021: deferred(M2): sign-out is M2; the generation guard it relies on is ported (B-SYNC-020).
