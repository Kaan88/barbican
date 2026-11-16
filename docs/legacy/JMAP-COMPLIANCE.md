# JMAP compliance review: Barbican

What this covers: Barbican's JMAP client (`src/jmap.rs`) and every caller (`mail.rs`, `compose.rs`,
`contacts.rs`, `calendar.rs`, `files.rs`, `server_settings.rs`, `templates.rs`, `agent.rs`,
`unified.rs`, `admin.rs`, `dashboard.rs`, `main.rs`), checked against:

- RFC 8620 (JMAP Core) and RFC 8621 (JMAP Mail)
- RFC 9007 (MDN), RFC 9425 (Quotas), RFC 9610 (Contacts), RFC 9661 (Sieve) and the JMAP Calendars draft
- Stalwart's extensions: `urn:stalwart:jmap`, `x:ArchivedItem`, FileNode and the Mailbox metadata

For each requirement it compares Barbican with Bulwark (`lib/jmap/client.ts`, `stores/email-store.ts`) and with what the Stalwart server source actually does (`stalwart-main/crates/...`).

**Status values**

- **compliant**: already correct before this branch.
- **fixed**: changed on branch `jmap-audit`.
- **deviation**: a deliberate difference, with the reason.
- **n.a.**: the feature is not used by Barbican.
- **not done**: a gap that is still open.

Line numbers for Bulwark and Stalwart are approximate. They refer to the trees in `webmail-main/` and `stalwart-main.zip`.

## 1. Session and core request (RFC 8620 §2, §3)

| Requirement | RFC | Status | Notes / refs |
|---|---|---|---|
| Session discovery via `/.well-known/jmap`, auth header, redirects followed | 8620 §2.2 | compliant | `fetch_session`; reqwest follows redirects. |
| `apiUrl` / `downloadUrl` / `uploadUrl` / `eventSourceUrl` may be relative | 8620 §2 | compliant | `absolute()`. |
| `primaryAccounts` per capability | 8620 §2 | compliant | `account_for(cap)`; mail account required at sign-in. |
| `accountCapabilities` read at account level | 8620 §2, 8621 §1.3 | **fixed** | `maxDelayedSend` and `submissionExtensions` were read from the session-level `capabilities` object. Stalwart publishes an empty session-level submission object (`common/src/config/mailstore/capabilities.rs` ~185), so delayed send never switched on. Added `account_cap()`. |
| `sessionState` change: re-fetch the session | 8620 §3.4 | **fixed** | `post()` compares `sessionState` with the live session and calls `refresh_session()`. Limits, URL templates and `apiUrl` are read from the live copy. |
| `using` lists the capabilities | 8620 §3.3 | compliant | Sends every capability the server advertised. That is allowed, and Bulwark does the same. Stalwart registry calls use an explicit `request_using`. |
| `maxCallsInRequest` | 8620 §2 | **fixed** | `request()` splits independent calls into several requests (`split_calls`). Calls linked by `#` back-references or creation ids stay in one request. `list_delta` falls back to a full reload when the limit is below 7. Bulwark: `getMaxCallsInRequest`, `itemsPerRequest`. |
| `maxObjectsInGet` | 8620 §5.1 | **fixed** | New `get_all()` chunks the ids. Used by the list (pages larger than the limit), Scheduled, calendar (was one back-referenced `CalendarEvent/get` of up to 5000 ids, which Stalwart answers with requestTooLarge), contacts, files and mailboxes. |
| `maxObjectsInSet` | 8620 §5.3 | compliant | `update()`, `destroy()`, `empty_mailbox()` chunk. |
| `maxSizeRequest` | 8620 §2 | **fixed** | Checked before sending, with a readable error. |
| `maxConcurrentRequests` | 8620 §2 | **fixed** | `Gate` semaphore around each API POST. Barbican starts many background threads at once (folders, list, tag counts, new-mail check), so bursts could hit Stalwart's limit. |
| `maxSizeUpload` / `maxConcurrentUpload` | 8620 §2, §6.1 | **fixed** | Size checked up front (413 mapped). Concurrent uploads gated. |
| Server query limits (a server may cap `limit`) | 8620 §5.5 | **fixed** | New `query_all()` pages by `position` until `total`. Used for contacts (was one query with `limit:5000`), files (`limit:20000`) and calendar. |
| Back-references `#ids` with `resultOf/name/path` | 8620 §3.7 | compliant | List, thread and scheduled requests, plus the new delta request (`/added/*/id`, `/updated`, `/created`). |
| Creation ids (`#d`) across calls | 8620 §5.3 | compliant | `EmailSubmission/set` `emailId: "#d"`, `onSuccessUpdateEmail: {"#s": ...}`, `onSuccessActivateScript: "#n"`. |
| Request-level errors (RFC 7807 problem details, `urn:ietf:params:jmap:error:limit` with `limit`) | 8620 §3.6.1 | **fixed** | `problem_message()`. It used to show only `detail`/`type`, and `request_using` showed only the status code. |
| Method-level errors | 8620 §3.6.2 | **fixed** (mapping) | Added stateMismatch, tooManyChanges, anchorNotFound, serverUnavailable/Fail/PartialFail, unsupportedSort/Filter. Bulwark: `UNREPORTED_METHOD_ERRORS` (client.ts ~218). |
| `serverUnavailable` / HTTP 429 / 503: try again later | 8620 §3.6.1 | **fixed** | Read-only requests (`/get`, `/query`, `/changes`, `/queryChanges`, `/validate`) are retried once after `Retry-After` (at most 10 s). Writes are never retried, since they might be applied twice. |
| SetError types | 8620 §5.3, 8621 §2.3/§4.6/§7.5 | **fixed** (mapping) | Added notFound, forbidden, singleton, rateLimit, forbiddenMailFrom, forbiddenToSend, cannotUnsend, tooManyKeywords, tooManyMailboxes, blobNotFound, willDestroy, invalidPatch. |
| OAuth bearer renewal on 401 | 8620 §3.6 (HTTP) | **fixed** | Was missing on upload, the event source and `request_using`. All now go through the shared renewal. |

## 2. Standard methods, states and sync (RFC 8620 §5)

| Requirement | RFC | Status | Notes / refs |
|---|---|---|---|
| `/get` `state` remembered for later `/changes` | 8620 §5.1 | **fixed** | `mailboxes_with_state()`. `ListPage` carries `queryState`, the Email state and the Thread state, all from the same request as the ids. |
| `Mailbox/changes` instead of re-reading the tree on push | 8620 §5.2, 8621 §2.2 | **fixed** | `mailbox_delta()` and `mail::load_mailboxes`. When `updatedProperties` lists only counts (Stalwart "container-only" changes, `jmap/src/changes/get.rs`), only those properties are fetched. `hasMoreChanges` is paged. The state is stored with its account id, so it never crosses accounts. Bulwark: `getMailboxChanges`, `email-store.ts` ~7479. |
| `Email/queryChanges` + `Email/changes` instead of re-querying the list on push | 8620 §5.2, §5.6 | **fixed** | `list_delta()` sends one request: `Email/changes`, `Email/queryChanges`, `Email/get` of the added and updated messages, `Thread/changes` and `Thread/get`. `apply_list_delta()` follows the §5.6 algorithm: remove every `removed` id, insert `added` by ascending index, refresh rows updated in place. Additions past the loaded prefix are left to paging. Bulwark applies `Email/changes` only to plain folder views (`applyEmailDeltaNow`, `email-store.ts` ~1186). Barbican covers folders, tags and search, because Stalwart's queryChanges re-runs the query (`jmap/src/changes/query.rs`). |
| `canCalculateChanges` respected | 8620 §5.5 | **fixed** | No delta is tried when the query says false. |
| `cannotCalculateChanges` / `tooManyChanges`: full refetch | 8620 §5.2, §5.6 | **fixed** | Any method error or `hasMoreChanges` makes `list_delta`/`mailbox_delta` return `None`, and the caller reloads. `maxChanges` is 256. Stalwart answers a truncated change log with cannotCalculateChanges (`changes/get.rs` ~213). |
| Stalwart states that go backwards | Stalwart | **fixed** | `stalwart_change_id()` / `went_back()` port Bulwark's `lib/jmap/stalwart-state.ts`, tested against its captured vectors. A delta whose new state is below the old one is treated as a full refetch (after a restore, Stalwart would otherwise answer an empty delta). |
| `Email/queryChanges` without `maxChanges` | 8620 §5.6 | deviation | Stalwart's queryChanges would cut off at an intermediate state without saying so (`changes/query.rs`). Barbican bounds the change set with `Email/changes maxChanges` in the same request instead. |
| `upToId` | 8620 §5.6 | n.a. | Optional. Barbican applies the whole delta. |
| `ifInState` / `stateMismatch` on read-modify-write | 8620 §5.3 | **fixed** where it matters | `templates::delete` rewrites the whole tombstone map in the Inbox metadata. It now sends `ifInState` and starts over on `stateMismatch`, up to 3 tries. Flag, move and label patches are single-property and idempotent, so they do not need it (Bulwark sends none either). |
| `ifInState` on Sieve script replace | 8620 §5.3, 9661 | deviation | The script blob is replaced whole, so the last writer wins. Same as Bulwark. |
| `/query` `anchor` / `anchorOffset` paging | 8620 §5.5 | **fixed** | "Load more" anchors on the last loaded message, so mail arriving meanwhile cannot shift the page. `anchorNotFound` falls back to `position`. A page from a different `queryState` drops the sync states, forcing a full refetch next time. |
| `/set` `create`/`update`/`destroy` result handling | 8620 §5.3 | compliant | `created_id`, `update()`, `notUpdated`/`notDestroyed` mapped. |
| Patch paths are JSON Pointers (`~0`, `~1`) | 8620 §5.3 | **fixed** | `keyword_path()`. The assistant's `tag_messages` built `keywords/$label:<tag>` without escaping or validation. |

## 3. Binary data (RFC 8620 §6)

| Requirement | RFC | Status | Notes / refs |
|---|---|---|---|
| Upload: POST to `uploadUrl` with `{accountId}` | 8620 §6.1 | **fixed** | `{accountId}` is now percent-encoded. Content-Type defaults to `application/octet-stream`. 401 is renewed. |
| Download: URI template level 1, percent-encoded `{name}`/`{type}` | 8620 §6.2 | compliant | `pct()`. Download timeouts are per read, so large attachments are not cut off by a total deadline. |
| `Blob/copy`, RFC 9404 `Blob/upload` | 8620 §6.3 | n.a. | No cross-account copy. |

## 4. Push (RFC 8620 §7)

| Requirement | RFC | Status | Notes / refs |
|---|---|---|---|
| EventSource URL `{types}`, `{closeafter}`, `{ping}` | 8620 §7.3 | **fixed** | `types=Email,Mailbox,Thread,EmailDelivery,EmailSubmission` (was `*`), `closeafter=no`, `ping=30`. Stalwart enforces a 30 s minimum ping and sends `{"interval": ms}` (`jmap/src/api/event_source.rs`). |
| Dead-connection detection | 8620 §7.3 | **fixed** | 75 s per-read deadline (2.5 × ping) plus TCP keepalive. Before this, a stalled stream blocked forever. |
| Reconnection with backoff | 8620 §7.3 | **fixed** | Exponential 2 s to 5 min with jitter. Reset after a healthy stream (over 2 min). It was a fixed 20 s with no jitter. |
| Changes made while not listening | 8620 §7 | **fixed** | Every time the stream opens, one sync of all followed types runs (cheap /changes deltas). This covers the window between the first load and the stream opening, and any outage. |
| StateChange parsed per account and type | 8620 §7.1 | **fixed** | `changed_types(change, account)`. Folders sync on Mailbox/Email/Thread, the list on Email/Thread, new-mail checks on Email/EmailDelivery, Scheduled on EmailSubmission. Changes for other accounts or types are ignored. It used to reload everything on any event. |
| SSE framing (multi-line `data`, comments, CRLF, `ping`, unknown events such as Stalwart `calendarAlert`) | WHATWG SSE | **fixed** | `SseParser`, unit-tested. |
| PushSubscription (webhook push) | 8620 §7.2 | n.a. | A desktop client has no public callback URL. |
| WebSocket transport | RFC 8887 | n.a. | EventSource is enough. Bulwark does not use it either. |

## 5. Mail (RFC 8621)

| Requirement | RFC | Status | Notes / refs |
|---|---|---|---|
| Mailbox roles (inbox, drafts, sent, archive, junk, trash) | 8621 §2 | compliant | Archive is created with `role: archive` when missing (as Bulwark does). The role map is now cached for 5 min and refreshed by every full tree read. It used to be one `Mailbox/get` of the whole tree per send, move or push. |
| `isSubscribed` | 8621 §2 | compliant | Unsubscribed folders are shown muted. New folders are created subscribed. |
| `Mailbox/set` destroy with `onDestroyRemoveEmails: false` | 8621 §2.5 | compliant | mailboxHasEmail and mailboxHasChild are mapped. |
| `myRights` enforcement in the UI | 8621 §2 | deviation | Actions are offered and the server's `forbidden` is reported. |
| Thread/get, Thread/changes | 8621 §3 | compliant / **fixed** | Thread sizes come from `Thread/get`. Deltas use `Thread/changes`. |
| Email/query `sort` (`receivedAt` is mandatory to support), `calculateTotal` | 8621 §4.4 | compliant | `list_sort()` is shared by query and queryChanges, as §5.6 requires. |
| `collapseThreads` | 8621 §4.4 | n.a. | Barbican lists messages and shows conversation size. Bulwark does the same. |
| Email/get `bodyProperties`, `fetchHTMLBodyValues`, `maxBodyValueBytes` | 8621 §4.2 | **fixed** | The reader asked for `fetchAllBodyValues`, which includes text attachments' contents. It now asks for `fetchTextBodyValues` + `fetchHTMLBodyValues` only, still with `maxBodyValueBytes` 2 MB. |
| `isTruncated` surfaced | 8621 §4.1.4 | not done | A body over 2 MB is shown truncated without a notice. Bulwark refetches truncated bodies. |
| Header forms (`header:X:asAddresses`, `:all`) | 8621 §4.1.2 | compliant | `Authentication-Results:all`, `Disposition-Notification-To:asAddresses`. |
| Keywords: valid characters, case-insensitive, `$seen` `$flagged` `$answered` `$forwarded` `$draft` `$junk` `$notjunk`, `$label:*` | 8621 §4.1.1 | **fixed** | `valid_keyword()` (%x21-7E, no `( ) { ] % * " \`). Keywords are lower-cased. Invalid ones are skipped or refused. |
| SearchSnippet/get | 8621 §5 | not done | No highlighting of search hits. Bulwark: client.ts ~4792. |
| Email/import | 8621 §4.8 | not done | No mail import feature. Bulwark: client.ts ~6779 (`lib/mail-import`). |
| Email/parse | 8621 §4.9 | not done | Attached `.eml` files are not opened in the reader. Bulwark: `parseEmailBlob`, client.ts ~3565. |
| Email/copy | 8621 §4.7 | n.a. | No cross-account move. |
| Identity/get, Identity/set | 8621 §6 | compliant | `replyTo` from the identity. Name and signature are edited with Identity/set. |
| EmailSubmission: `identityId`, `emailId` creation ref, `onSuccessUpdateEmail` (Drafts to Sent, clear `$draft`) | 8621 §7.5 | compliant | `send_message`. |
| Envelope: derived by the server when null; explicit (with Bcc) when parameters are needed | 8621 §7.5 | compliant | The explicit envelope is used only with `HOLDFOR`. It includes To, Cc and Bcc recipients. |
| Delayed send: `maxDelayedSend` + FUTURERELEASE `HOLDFOR` | 8621 §1.3.2, RFC 4865 | **fixed** | Read at account level (see §1). FUTURERELEASE is required in `submissionExtensions`. Stalwart's advertised 30 days is clamped to its 7-day MTA default (Bulwark `getMaxDelayedSend`, client.ts ~7123). Before this fix, Barbican always fell back to its in-app timer. |
| `undoStatus: canceled` | 8621 §7 | **fixed** | `cancel_submission` checks the result. A message already sent gives "already sent". The canceled record is destroyed, and the message (which `onSuccessUpdateEmail` had already filed in Sent) goes back to Drafts. The composer then reuses that draft id. This mirrors Bulwark's `cancelEmailSubmission` + `restoreSentToDraft` (client.ts ~12812, email-store.ts ~9207). |
| `onSuccessDestroyEmail` | 8621 §7.5 | n.a. | The sent copy is kept and filed in Sent. |
| EmailSubmission/query `undoStatus: pending` (Scheduled view) | 8621 §7.3 | compliant | Email/get of the results is now chunked. |
| VacationResponse (`singleton`) | 8621 §8 | compliant | `server_settings.rs`, `agent.rs`. |

## 6. Other specifications and Stalwart extensions

| Requirement | Spec | Status | Notes / refs |
|---|---|---|---|
| MDN/send, MDN/parse | RFC 9007 | n.a. / not done | Stalwart does not implement `urn:ietf:params:jmap:mdn` (no match in its source). Barbican requests receipts with `Disposition-Notification-To`, but does not answer requests. Bulwark builds an RFC 8098 report itself (`sendReadReceipt`, client.ts ~6811). |
| Quota/get: `resourceType`, `scope`, `types` | RFC 9425 §4 | **fixed** | `pick_quota()` prefers an `octets` quota that covers Mail at account scope, then the tightest one. It used to take the first `octets` quota, whatever its scope. |
| Quota/changes, queryChanges | RFC 9425 | n.a. | Stalwart answers cannotCalculateChanges for Quota queryChanges (`changes/query.rs`). The quota is re-read when the view opens. |
| AddressBook/get, ContactCard query/get/set, `onSuccessSetIsDefault` | RFC 9610 | compliant / **fixed** | Paging and chunking fixed. Category edits send the whole `keywords` map, because Stalwart refuses a `keywords/x` patch on a card without keywords (`contacts.rs` `keywords_with`, as in Bulwark). |
| Calendar/get, CalendarEvent query/get/set, `onSuccessSetIsDefault` | JMAP Calendars draft | compliant / **fixed** | Query and get chunking fixed. Recurrences are expanded on the client (no `expandRecurrences`), so a series is read once. |
| SieveScript/get, /set with `onSuccessActivateScript`, /validate (`error`), `sieveExtensions` at either level | RFC 9661 | compliant | `server_settings.rs` `save_script`. Blobs are uploaded as `application/sieve`. |
| `urn:stalwart:jmap` `x:ArchivedItem` query/get/set (`requestRestore`) | Stalwart | compliant | Explicit `using` (`ARCHIVE_USING`). Errors now go through problem details and OAuth renewal. |
| FileNode (Stalwart file storage draft) | Stalwart | **fixed** | `query_all` + chunked get. Stalwart cannot filter by parent, so the client filters (as Bulwark does). `@type` is sent on create for the draft-18 server. |
| Mailbox `metadata` (`urn:ietf:params:jmap:metadata`, Bulwark templates) | Stalwart / Bulwark | **fixed** | Uses `ifInState` (see §2). |
| Admin registry (`admin.rs`, `dashboard.rs`) | Stalwart | n.a. | A separate client with its own admin API key and the registry `x:*` methods. It is not part of the user session and was not changed. |

## Tests

`cargo test --release` covers:

- **jmap.rs, 14 new tests**: the Stalwart state codec against Bulwark's captured vectors, backwards-state detection, `/changes` parsing, the object delta merge, list delta insert/remove/update/beyond-window, call splitting with back-references and creation ids, read-only detection, problem details, the SSE parser and StateChange per account, keyword validation and escaping, quota choice, and address parsing.
- **templates.rs**: tombstone merge.

## Verified against the live server (hidden automation, read-only)

Run with `BARBICAN_JMAP_TRACE=<file>`, which logs method names only.

- Push connects, and Stalwart sends `ping` every 30 s.
- On open, `Mailbox/changes` replaces the full `Mailbox/get`.
- The 7-call delta request (`Email/changes`, `Email/queryChanges`, `Email/get` ×2, `Thread/changes`, `Thread/get` ×2) runs with no method errors and is applied without a fallback reload.
- Calendar, contacts and files load through `query_all` / `get_all`.

## Not verified live

Writes were blocked in the automation session, so these were not exercised against the server:

- A delta carrying real changes (new mail, a flag set elsewhere)
- Undo of a server-held send and the restore to Drafts
- `ifInState` retries
- OAuth renewal on upload and on the event source
- A `sessionState` refresh
