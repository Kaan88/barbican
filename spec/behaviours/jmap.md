# Behaviours: JMAP client (`src/jmap.rs`)

Area: `JMAP`. Owner milestone: M1 (core, sync, push), M3 (submission, upload), M6 (sieve, metadata), M2 (import/parse/snippets).

Pins (per `spec/README.md`): Rust source as tagged `rust-final` (`bba9613`) in `Kaan88/barbican-rust`; Bulwark 1.21.2 (`lib/jmap/client.ts`, `lib/jmap/stalwart-state.ts`, `stores/email-store.ts`); Stalwart source `stalwart-0.16.27.zip` (the compliance doc cites `stalwart-main.zip`, which is no longer on disk).

Status of each requirement against RFC 8620/8621 is in `spec/jmap.csv` (J-###). This file lists the behaviour rules the code implements, one per row. Rows cite the Rust file and line where the rule lives, and the Rust tests that pin it (`T-` ids are in `spec/rust-tests.csv`).

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-JMAP-001 | Session is fetched from the well-known URL; relative `apiUrl`, `downloadUrl`, `uploadUrl`, `eventSourceUrl` are resolved against the session origin. | jmap.rs `fetch_session`, `absolute()` | RFC 8620 2.2 (J-001, J-002) | none (live) |
| B-JMAP-002 | The account for a capability is the primary account for that capability, else the mail account. The mail account is required at sign-in. | jmap.rs:299 `account_for` | RFC 8620 2 (J-003) | none |
| B-JMAP-003 | Account-level capabilities (`maxDelayedSend`, `submissionExtensions`) are read from the account, not the session. | jmap.rs:1475 `delayed_send_limit` | RFC 8620 2; RFC 8621 1.3 (J-004); Stalwart publishes an empty session-level submission object | `delayed_send_needs_futurerelease` |
| B-JMAP-004 | Delayed send is offered only when `FUTURERELEASE` is listed in `submissionExtensions`. A missing list means no extension. The advertised maximum is clamped to the 7-day MTA default. | jmap.rs:1475 | RFC 4865; Bulwark `hasDelayedSend` (J-063) | `delayed_send_needs_futurerelease` |
| B-JMAP-005 | A response whose `sessionState` differs from the live session triggers one session re-fetch. Limits and URLs come from the live copy. | jmap.rs:216 `refresh_session`; `post()` | RFC 8620 3.4 (J-005) | none (not live) |
| B-JMAP-006 | `using` lists every capability the server advertised. Registry calls use an explicit `request_using`. | jmap.rs:283 `request_using` | RFC 8620 3.3 (J-006) | none |
| B-JMAP-007 | Independent calls are split into requests of at most `maxCallsInRequest`. Calls linked by `#` back-references or creation ids stay in one request, because they only resolve inside their own request. | jmap.rs:104 `split_calls` | RFC 8620 2, 3.7, 5.3 (J-007) | `calls_split_by_limit_unless_linked` |
| B-JMAP-008 | `X/get` with many ids is chunked by `maxObjectsInGet`, several chunks per request. | jmap.rs:371 `get_all` | RFC 8620 5.1 (J-008) | none (live: calendar, contacts, files) |
| B-JMAP-009 | `X/query` is paged by `position` until `total` is reached, because the server may cap `limit`. | jmap.rs:352 `query_all` | RFC 8620 5.5 (J-013) | none (live) |
| B-JMAP-010 | `X/set` updates and destroys are chunked by `maxObjectsInSet`; results report `updated` and `failed` descriptions. | jmap.rs:337 `update`, `destroy` at :753 | RFC 8620 5.3 (J-009) | none |
| B-JMAP-011 | A request larger than `maxSizeRequest` is refused before sending, with a readable message. | jmap.rs `request` | RFC 8620 2 (J-010) | none |
| B-JMAP-012 | API POSTs pass through a counting gate of `maxConcurrentRequests`; uploads through a second gate of `maxConcurrentUpload`. | jmap.rs `Gate`, `Permit` | RFC 8620 2, 6.1 (J-011, J-012) | none |
| B-JMAP-013 | A problem-details error (RFC 7807) is shown with its `type` and `detail`, including the limit value for limit errors, and the status for a body without detail. | jmap.rs:112 `problem_message` | RFC 8620 3.6.1 (J-016) | `problem_details` |
| B-JMAP-014 | Read-only batches (`/get`, `/query`, `/changes`, `/queryChanges`, `/validate`) are retried once after `Retry-After` (at most 10 s) on 429 and 503. Batches with any write are never retried. | jmap.rs:96 `read_only` | RFC 8620 3.6.1 (J-018) | `read_only_calls` |
| B-JMAP-015 | Method-level errors are returned to the caller, not raised. The mapping covers stateMismatch, tooManyChanges, anchorNotFound, unsupportedSort/Filter and the server-unavailable family. | jmap.rs `call` | RFC 8620 3.6.2 (J-017) | none |
| B-JMAP-016 | SetError types map to readable messages (notFound, forbidden, singleton, rateLimit, forbiddenMailFrom, forbiddenToSend, cannotUnsend, tooManyKeywords, tooManyMailboxes, blobNotFound, willDestroy, invalidPatch). | jmap.rs (error mapping) | RFC 8620 5.3; RFC 8621 2.3, 4.6, 7.5 (J-019) | none |
| B-JMAP-017 | A 401 renews the OAuth bearer once and retries, on every request kind (API, upload, download, event source, `request_using`). | jmap.rs (shared renewal) | RFC 8620 3.6 (J-020) | none (not live) |
| B-JMAP-018 | `/get` state strings are kept so a later `/changes` can ask for the delta from that state. | jmap.rs `mailboxes_with_state`, `ListPage` | RFC 8620 5.1 (J-021) | none (live) |
| B-JMAP-019 | A `/changes` response that carries a method error, `cannotCalculateChanges`, `hasMoreChanges` or a Stalwart state that went backwards, gives no delta. The caller then re-reads in full. | jmap.rs:1374 `parse_changes`, :1443 `went_back`; :440 `mailbox_delta`; :592 `list_delta` | RFC 8620 5.2, 5.6; Stalwart state codec (J-025, J-026) | `parse_changes_reads_response`, `went_back_only_for_lower_stalwart_ids` |
| B-JMAP-020 | `maxChanges` is 256 for every delta. The delta is one request: `Email/changes`, `Email/queryChanges`, the `Email/get` of added and updated ids, `Thread/changes` and `Thread/get`. | jmap.rs:592 `list_delta` | RFC 8620 5.2, 5.6 (J-023, J-027) | `list_delta_inserts_removes_and_updates`, `list_delta_leaves_far_additions_to_paging`, `list_delta_refreshes_rows_updated_in_place` |
| B-JMAP-021 | A delta is not attempted when the query says `canCalculateChanges` is false. | jmap.rs `list_delta` | RFC 8620 5.5 (J-024) | none |
| B-JMAP-022 | Additions beyond the loaded window are left to paging, not inserted. The total still moves. | jmap.rs:592 `list_delta` | RFC 8620 5.6 (J-023) | `list_delta_leaves_far_additions_to_paging` |
| B-JMAP-023 | A mailbox delta where `updatedProperties` lists only counts fetches only those properties. A counts-only update for an unknown folder fetches the whole folder. | jmap.rs:440 `mailbox_delta` | RFC 8621 2.2; Stalwart `jmap/src/changes/get.rs` (J-022) | `counts_only_update_of_unknown_folder_fetches_it_whole`, `object_delta_merges_partial_updates` |
| B-JMAP-024 | Stalwart change ids are decoded from the state string (`s` + base32 LEB128). Initial and intermediate states, and other servers' formats, give no id. | jmap.rs:1396 `stalwart_change_id`, :1419 `stalwart_state` | Bulwark `lib/jmap/stalwart-state.ts` (J-026) | `stalwart_state_round_trip` |
| B-JMAP-025 | A state whose change id is lower than the old one is a restore on the server. It is treated as a full refetch. | jmap.rs:1443 `went_back` | Stalwart `changes/query.rs` (J-026) | `went_back_only_for_lower_stalwart_ids` |
| B-JMAP-026 | Email/queryChanges is always bounded with `maxChanges` in the same request, because Stalwart would otherwise cut the change set silently. | jmap.rs:592 | Stalwart `changes/query.rs` (J-027, deviation) | none |
| B-JMAP-027 | `/query` "Load more" anchors on the last loaded message, so mail that arrives meanwhile cannot shift the page. `anchorNotFound` falls back to `position`. A page from another `queryState` drops the stale state. | jmap.rs (list paging) | RFC 8620 5.5 (J-031) | none |
| B-JMAP-028 | Sort for `Email/query` always includes `receivedAt`; the same sort is used by `query` and `queryChanges`. | jmap.rs `list_sort` | RFC 8621 4.4, 5.6 (J-050) | none |
| B-JMAP-029 | Keywords are valid only when they are 1-255 characters of %x21-7E with no `( ) { ] % * " \`. They are stored lower-case. Invalid ones are skipped. | jmap.rs:1627 `valid_keyword` | RFC 8621 4.1.1 (J-055) | `keywords` |
| B-JMAP-030 | Patch paths are JSON Pointers: `~` becomes `~0`, `/` becomes `~1`. `$label:` keywords are escaped the same way. | jmap.rs:1633 `keyword_path` | RFC 8620 5.3 (J-033) | `filters_x::apply_patches_and_if_in_state` (B-JMAP-030 pins the patch shape) |
| B-JMAP-031 | Email/get asks for text and HTML body values only. Text attachments' contents are not fetched. | jmap.rs (body properties) | RFC 8621 4.2 (J-052) | none |
| B-JMAP-032 | Header fields requested as `header:X:asAddresses` or `header:X:all` (Authentication-Results, Disposition-Notification-To). | jmap.rs (properties) | RFC 8621 4.1.2 (J-054) | none |
| B-JMAP-033 | Mailbox roles (inbox, drafts, sent, archive, junk, trash) map to ids. The role map is cached for 5 minutes and refreshed by each full tree read. Archive is created with `role: archive` when missing. | jmap.rs `roles`; mail.rs:254 `load_mailboxes` | RFC 8621 2 (J-045) | none (live) |
| B-JMAP-034 | Unsubscribed folders are shown muted. New folders are created subscribed. | mail.rs (folder rebuild) | RFC 8621 2 (J-046) | none (live) |
| B-JMAP-035 | `Mailbox/set` destroy uses `onDestroyRemoveEmails: false`; `mailboxHasEmail` and `mailboxHasChild` map to readable errors. | jmap.rs `destroy` | RFC 8621 2.5 (J-047) | none |
| B-JMAP-036 | An EmailSubmission is created with `identityId`, an `emailId` creation reference (`#d`), and `onSuccessUpdateEmail` (Drafts to Sent, clear `$draft`). | jmap.rs:916 `send_message` | RFC 8621 7.5 (J-061) | none |
| B-JMAP-037 | The explicit envelope (with Bcc) is sent only when HOLDFOR is used. Otherwise the server derives it. | jmap.rs:916 `send_message` | RFC 8621 7.5 (J-062) | none |
| B-JMAP-038 | Undo of a held send destroys the canceled submission record. The message returns to Drafts. If it was already sent, the caller is told "already sent". | jmap.rs:1064 `cancel_submission` | RFC 8621 7.5 (J-064) | none (not live) |
| B-JMAP-039 | Rescheduling a held send tries a recreate first when the old submission id is known, then restores the draft. | jmap.rs:1654 `reschedule_fallbacks` | RFC 8621 7.5 (J-064) | `reschedule_fallback_order` |
| B-JMAP-040 | The From header uses the identity name. An override address applies only when it is one of the identity's addresses. | jmap.rs:1663 `header_from` | RFC 8621 6 (J-060) | `from_override_header_identity_and_envelope` |
| B-JMAP-041 | Quota: the quota used is the `octets` quota that covers Mail at account scope, else the tightest one. | jmap.rs:1157 `pick_quota` | RFC 9425 4 (J-069) | `quota_prefers_mail_account_octets` |
| B-JMAP-042 | Address lists keep quoted names with commas as one address ("Doe, Jane"). | jmap.rs (addresses) | RFC 5322 | `addresses` |
| B-JMAP-043 | Upload POSTs to `uploadUrl` with a percent-encoded `accountId`. Default Content-Type is `application/octet-stream`. | jmap.rs (upload) | RFC 8620 6.1 (J-034) | none (not live) |
| B-JMAP-044 | Downloads use the URI template with percent-encoded `name` and `type`. Timeouts are per read, not per transfer, so large attachments are not cut off. | jmap.rs (download) | RFC 8620 6.2 (J-035) | none (live) |
| B-JMAP-045 | The event source URL is built with `types=Email,Mailbox,Thread,EmailDelivery,EmailSubmission` (not `*`), `closeafter=no`, `ping=30`. | jmap.rs (push URL) | RFC 8620 7.3 (J-037) | none (live) |
| B-JMAP-046 | A stream read stalled for 75 s (2.5 x ping) is dead. TCP keepalive is also on. | jmap.rs (push read) | RFC 8620 7.3 (J-038) | none (live, ping observed) |
| B-JMAP-047 | Reconnection backs off exponentially from 2 s to 5 min with jitter. The backoff resets after a stream that stayed healthy for over 2 min. | jmap.rs (push loop) | RFC 8620 7.3 (J-039) | none (not live) |
| B-JMAP-048 | Each time the event source opens, one sync of all followed types runs, so changes made while disconnected are caught up. | jmap.rs (push open) | RFC 8620 7 (J-040) | none (live) |
| B-JMAP-049 | A StateChange is read per account and per type. Folders sync on Mailbox, Email and Thread; the list on Email and Thread; the new-mail check on EmailDelivery; Scheduled on EmailSubmission. Other accounts' changes are ignored by the list. | jmap.rs:1614 `changed_types` | RFC 8620 7.1 (J-041) | `sse_parser_events` |
| B-JMAP-050 | SSE framing follows WHATWG: multi-line `data`, comment lines, CRLF, `ping` and unknown events (Stalwart `calendarAlert`) are handled. | jmap.rs:1583 `SseParser` | WHATWG SSE; Stalwart `calendarAlert` (J-042) | `sse_parser_events` |
| B-JMAP-051 | Calendar recurrences are expanded on the client (no `expandRecurrences`), so a series is read once, not once per occurrence. | calendar.rs (client side) | JMAP Calendars draft (J-072) | none |
| B-JMAP-052 | Blob/copy is not used: there is no cross-account copy. Upload and download are the only blob paths. | jmap.rs (no call sites) | RFC 9404 (J-036) | none |
| B-JMAP-054 | Sieve script replace is last-writer-wins (no ifInState), as in Bulwark. | server_settings.rs `save_script` | RFC 9661 (J-030, deviation) | `sieve::*` (see sieve area) |

## Gaps the port must close (not yet in Rust)

- B-JMAP-G01: `isTruncated` is not surfaced. A body over 2 MB is shown truncated with no notice (J-053). Target M1. Bulwark refetches truncated bodies.
- B-JMAP-G02: `SearchSnippet/get` is not used (J-056). Target M2.
- B-JMAP-G03: `Email/import` and `Email/parse` are not used (J-057, J-058). Targets M2 (import dialog, attached .eml).
- B-JMAP-G04: MDN answers are not built on the server side (J-068). The client builds the report itself (see READER area).

## Open questions

- Whether `ifInState` should be added to the Sieve replace (J-030) now that Stalwart supports it. The Rust decision was to match Bulwark; the port should decide with the user.
- The compliance doc's live verification list covers only read-only paths. Items J-005, J-020, J-029, J-034, J-063, J-064 and J-076 are unverified and need the test account.
