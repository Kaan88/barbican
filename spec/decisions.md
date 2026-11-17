# Decisions and fixes harvested from the Rust git log (D-###)

Source: every non-merge commit of the Rust repository (`Kaan88/barbican-rust`, all refs, tag
`rust-final` = `bba9613`), 158 commits in all. Each commit is given one or more rows. Where a
commit message gives no reason, the reason is taken from the change summary in the subject and
is marked "(subject only)". Merge commits (the parity-round merges) are listed in the last
section. The "Links" column names the B- rows (behaviours/*.md) and G-/L- rows that cover the
same behaviour.

Columns: ID | commit | area | decision or fix | reason (from the body) | links

Areas: `foundation`, `jmap`, `mail`, `reader`, `composer`, `rte`, `sieve`, `settings`,
`calendar`, `contacts`, `files`, `admin`, `search`, `shortcuts`, `desktop`, `updater`,
`distribution`, `motion`, `design`, `docs`, `wip`.

## foundation and product start

| ID | commit | area | decision or fix | reason | links |
|---|---|---|---|---|---|
| D-001 | 1d5fce2 | foundation | Native Rust and Slint client for Stalwart: JMAP mail, WebView2 or Blitz rendering with remote-image blocking, OAuth/SSO, spam score, AI assistant, Bulwark-parity settings. | First commit of the app. | B-SET-022, B-SIEVE-001 |
| D-002 | 5eb95d9 | jmap | JMAP core: per-call errors, set errors, limits, blobs, identities, HOLDFOR send, push, quota. | Stalwart needs HOLDFOR for delayed send; per-call and set errors must surface. | B-SET-027 |
| D-003 | 0311a49 | plugins | Native plugins are drop-in DLLs with a JSON C ABI (email.details, compose.check); Add-ons page; a Spam Score example plugin. | Extension without a web runtime (subject only). | G-081 |
| D-004 | d5a1e96 | jmap | Verified on Stalwart: create the Archive folder automatically; role display names; Scheduled folder; in-app undo send when the server lacks delayed send. | Verified against the server (commit subject). | D-005 |
| D-006 | ca21476 | desktop | Assistant panel docked by default; quick reply box; dark plain-text mail; "me" for own identities; pink primary buttons; server settings load on open. | Found by running the app (subject). | B-SET-024 |
| D-007 | 3245990 | shortcuts | Bulwark keyboard shortcuts; admin probe script; window driver for testing. | Parity with Bulwark's key map. | B-KEY-001 |
| D-008 | 79cd118 | design | Font pass: Skia renderer (Chrome's engine) with Segoe UI like Bulwark; invisible-character cleanup in previews; filled tag icons. | Match Bulwark's font rendering (subject). | G-092 |
| D-009 | 42e690d | automation | Hidden test-automation channel (BARBICAN_AUTOMATION=1): TCP command port, dispatch_event input, take_snapshot PNGs, off-screen window; tools/auto.py client. | Test without touching the user's input. | B-MOT-024 |
| D-010 | b6a37f1 | automation | The test driver refuses input unless Barbican is the foreground window. | Safety: no input reaches another window. | CLAUDE.md standing rule |
| D-011 | b0dd3bd | foundation | Keyboard focus is not stolen by the hidden WebView2 (focus_parent); a default address book is created on first contact; a clicktype helper for UI tests. | Fix: the hidden WebView took focus. | B-KEY-028, B-CON-003 |
| D-012 | 7593933 | settings | Unit test for settings import, export and reset. | Backup must round-trip. | B-SET-006 |
| D-013 | e1f5bf5 | settings | Backup and import: export, import, reset; accepts Bulwark exports; assistant tools read_attachment, list_filters, update_filters, change_setting. | Bulwark settings files must be importable. | B-SET-006 |
| D-014 | 594e4e9 | settings | Assistant-changed settings apply immediately; compact rows are roomier. | Verified change_setting and list_filters end to end. | B-SET-008, B-SET-009 |
| D-015 | 714ad82 | settings | More settings applied: text size, quick reply, toolbar labels, tag chips, colourful sidebar icons, total counts, send confirmation, read-receipt default, calendar day start; theme select shows dark by default; appName placeholder. | Make the Bulwark keys do what they say. | B-SET-021, B-SET-008 |
| D-016 | 4e77941 | design | Light, dark and system themes from Bulwark's Appearance setting; theme tokens; a runtime Fluent palette. | Bulwark parity. | B-SET-024 |
| D-017 | 9e11657 | jmap | Keep stored credentials on a failed sign-in restore and on an account switch. Only an auth rejection forgets an account. | Fix: a timeout or server hiccup signed every instance out, and a failed switch deleted the session in use. | B-PLAT-009 |
| D-018 | e9f51c8 | oauth | OAuth survives refresh-token rotation across instances: refreshes are serialised with a cross-process lock file; a rotated token is adopted; rejected accounts are marked as needing sign-in and never auto-deleted. | Rotation by another instance must not sign the account out. | plan M1 (accounts); none in B- rows yet |
| D-019 | 769e631 | oauth | Dynamic client registration uses only 127.0.0.1 or [::1] loopback redirect URIs (localhost dropped). | Stalwart accepts only those; verified by registering against the server. | D-020 |
| D-020 | 48b28f0 | oauth | Loopback redirect on the fixed port 8791 for pre-registered clients. | Pre-registered clients need a fixed redirect. | D-019 |
| D-021 | 6fa18ef | mail | Unified mailbox: "All inboxes" virtual folder across the remembered accounts; one client per account; the merged Inbox sorted by receivedAt; account dot on rows; actions and replies routed to the owning account; live toggle through enableUnifiedMailbox. | Bulwark unified inbox. | B-SET-027 |
| D-022 | d78c187 | settings | Multiple accounts: account switcher menu, add account, remembered sessions per account in the OS credential store. | Bulwark multi-account. | D-017 |
| D-023 | 8fee9dc | composer | Email templates shared with Bulwark (Inbox metadata org.bulwarkmail.template.*, tombstones); composer template picker; settings rows no longer overlap (measured scroll layout). | Templates must round-trip with Bulwark. | G-032 |
| D-024 | a1cb1d6 | mail | Refuse to send without recipients before the undo window; no virtual folder in the Move menu; assistant history per account; contact avatar initials. | E2E fixes. | B-CON-027, D-021 |
| D-025 | 3cbe629 | composer | The composer defaults to the signed-in identity. | Bulwark default sender. | B-RTE-023 |
| D-026 | 82e3890 | mail | Bulwark list and reading settings applied: group by date, previews, date and time format, emails per page, mark-as-read delay, delete action, bulk-delete confirmation. | Bulwark parity. | B-SET-032 |
| D-027 | 4169faf | reader | Conversation strip in the reader (Thread/get); honours disableThreading. | Bulwark conversations. | B-SET-032 |
| D-028 | db57ec6 | desktop | Desktop notifications for new inbox mail, push-driven, gated by emailNotificationsEnabled; list density. | Bulwark mail notifications. | B-DSK-009 |
| D-029 | 045855a | search | Search filters like Bulwark (from, to, subject, body, dates, size, unread, starred, attachment); scope per searchDefaultScope. | Bulwark search. | B-PAL-011, B-PAL-007 |
| D-030 | b5621bf | files | Recover deleted items: x:ArchivedItem query and get; restore via status requestRestore; a request_using helper. | Stalwart archived items. | B-FILE-009 |
| D-031 | 0ef6ba5 | calendar | Calendar tasks (JSCalendar Task via CalendarEvent/set, progress toggle), like Bulwark. | Bulwark tasks. | B-CAL-039 |
| D-032 | e75e107 | contacts | Contact groups (JSContact kind=group, members by uid), a group view and "Email group". | Bulwark groups. | B-CON-005 |
| D-033 | 1972552 | sieve | Visual filter rules compatible with Bulwark: @metadata block, same Sieve generation, external rules preserved; round trip verified in Bulwark. | Bulwark must read Barbican's rules. | B-SIEVE-001, B-SIEVE-014 |
| D-034 | d1a75e9 | calendar | Calendar (day, week, month, mini month, recurrence expansion, event editor), Contacts (JSContact list, detail, edit), Files (FileNode 0.16 and draft 18: browse, upload, download, rename, delete). | First server-backed PIM. | B-CAL-004, B-CON-001, B-FILE-001 |
| D-035 | c6b57c9 | settings | Server-backed settings: Out of office, Sieve filters with validation, folder create, rename and delete, identities and signatures (inserted in the composer per Bulwark options). | Bulwark's server settings. | B-SET-022 |
| D-036 | 5027a84 | admin | Stalwart admin tab: API key in the credential store enables dashboard, accounts, domains, delivery queue (delete), logs, DMARC reports over urn:stalwart:jmap. | Admin is read-mostly by plan. | B-ADM-001, B-ADM-003 |
| D-037 | 19f2628 | admin | Admin tab redesign (title bar, tiles with accents, tables with header, zebra rows and equal columns); domain names for accounts; real DMARC fields; local timestamps; log store message; the rail closes settings. | Stalwart WebUI layout. | B-ADM-009, B-ADM-017 |

## mail, reader and the JMAP sync

| ID | commit | area | decision or fix | reason | links |
|---|---|---|---|---|---|
| D-038 | 5531574 | mail | Mail parity: Bulwark sidebar and tags; list rows with threads; hover actions; multi-select; paging; reader toolbar; attachments; inline images; composer with identities, Cc, Bcc, attachments, drafts and undo send; push updates. | Core mail. | B-LST-001 |
| D-039 | 54d75d7 | mail | Unified inbox: per-source paging; unread badge; cross-account and group-inbox settings; push from every account; account dot rules; move-to by role or name. | Bulwark unified inbox. | D-021 |
| D-040 | cf3bad4 | jmap | Incremental sync: Email/queryChanges, Email/changes and Thread/changes in one request; folders with Mailbox/changes (counts-only updatedProperties); a state regression falls back to a full fetch (as Bulwark). | Avoid re-querying on every push. | B-LST-017 |
| D-041 | cf3bad4 | jmap | EventSource: explicit types; ping 30, read deadline 75 s; exponential backoff with jitter; OAuth renewal; sync after reconnect; per-type refresh. | Push robustness. | B-DSK-022 |
| D-042 | cf3bad4 | jmap | maxCallsInRequest splitting; maxObjectsInGet chunking; query paging past server caps; maxSizeRequest check; maxConcurrentRequests and Upload gates. | Stalwart limits. | D-040 |
| D-043 | cf3bad4 | jmap | A sessionState change re-fetches the session (RFC 8620 3.4). | RFC 8620 3.4. | D-040 |
| D-005 | cf3bad4 | jmap | Delayed send is read from the submission accountCapabilities, not the session level (it was never enabled on Stalwart); FUTURERELEASE is checked; undo destroys the cancelled submission and restores the draft. | Stalwart reports the capability per account. | B-SET-027 |
| D-044 | cf3bad4 | jmap | ifInState and a stateMismatch retry for template tombstones; anchor paging for load-more; text and HTML body values only. | Template sync conflicts. | D-023 |
| D-045 | cf3bad4 | jmap | Request-level problem details; more method and SetError types; keyword validation and JSON-pointer escaping; upload template encoding; 401 renewal; quota pick by scope and types. | Error fidelity. | D-042 |
| D-046 | f04be69 | jmap | Sync on every push (re)open: one /changes round trip on open closes the gap between the first load and the event stream. Opt-in request trace via BARBICAN_JMAP_TRACE. | Changes between first load and stream opening were never pushed. | B-LST-017 |
| D-047 | f7e059f | jmap | JMAP-COMPLIANCE.md (RFC 8620, 8621, 9007, 9425, 9610, 9661 and Stalwart review); the mailbox state is read before paging. | Compliance record (JMAP-COMPLIANCE.md is the source of the J- rows in cat-a's jmap.csv). | G-056, G-057 |
| D-048 | 66e1e05 | jmap | No total deadline on downloads; a size-scaled upload deadline; sync states keyed by server, user and account; stale mailbox deltas dropped; load_list resets list states; FUTURERELEASE is required for delayed send; undo send never leaves a fake sent copy; unknown folders fetched whole. | Review fixes. | D-005 |
| D-049 | 5217a5c | mail | After a send (server hold or local undo window) or an undo, the list syncs and the open conversation is fetched again. Email/Thread changes also refresh the conversation. Mark folder as read and folder operations use Email/queryChanges, not a full reload. The Undo toast sits above the quick reply. | The reply shows at once. | B-LST-017 |
| D-050 | 9147c25 | mail | Import streams .mbox message by message; caps a single .eml; skips messages whose Message-ID is already in the folder; refuses up front when myRights.mayAddItems is missing. | No duplicates on a retried import; no whole file in memory. | G-046 |
| D-051 | 9147c25 | mail | Avatars: queue and worker count under one lock; BIMI logo URLs only to public hostnames; capped streaming read with a Content-Length check; Authentication-Results authserv-id must be the account's server; transient failures not cached as missing; refresh drops in-flight answers by generation. | Security and correctness of remote avatars. | B-SET-040, G-052 |
| D-052 | 9147c25 | mail | choose_identity: an override no identity covers is refused, instead of silently sending through the first identity. | Never send from an identity the user did not choose. | D-053 |
| D-054 | 400fd6e | mail | Nested tags with tag editing; sender avatars (Gravatar, BIMI, favicons), off by default, cached, also in Junk; .eml and .mbox import through Email/import. | Bulwark tags and avatars. | D-051, D-050 |
| D-055 | 585aea2 | mail | From override (catch-all rewrite): header From through the composer's identity; choice as Bulwark (the override's identity, the picked one, a *@domain wildcard); MAIL FROM is the override only when it differs; a forbiddenMailFrom or forbiddenFrom refusal resubmits the Drafts copy with the identity's address. | Bulwark From override. | D-053 |
| D-056 | 585aea2 | mail | attachmentImagePreviewsEnabled: image attachments up to 10 MB get 80 px thumbnails, fetched and cached. | Bulwark attachment previews. | B-SET-036 |
| D-057 | 585aea2 | mail | replyUnderMessage: a reply docks under its message (60 % of the pane, at least 320 px); a divider drags the share (replyDockShare, double-click resets); under 700 px the reply takes the pane with a "Show the message" bar. | Bulwark reply under message. | B-SET-037 |
| D-058 | 585aea2 | mail | unreadBadgeAllAccounts: the title badge adds other accounts' inbox unread, read-only, refreshed every 2 minutes. | Bulwark badge. | B-SET-038 |
| D-059 | 5217a5c | mail | Toast (Undo send) sits above the quick-reply bar rather than covering it. | Layout fix. | D-049 |
| D-060 | 8b501d4 | mail | Reply identity follows Bulwark: self-sent, Delivered-To, catch-all domain match. | Bulwark reply identity. | D-055 |
| D-061 | 47ab9ad | composer | Reply From follows the own identity the mail was addressed to, honouring subAddressDelimiter. | Bulwark reply From. | B-RTE-023 |
| D-062 | aaa3891 | desktop | emailNotificationSound and pushNotifyInboxOnly. | Bulwark notification settings. | B-DSK-015 |
| D-053 | 585aea2 | composer | "Edit sender..." turns From into name and address fields with a pill back to the identities; undo send restores the sender; a re-opened draft keeps its sender. | Bulwark sender override. | D-055 |
| D-063 | 936aaa9 | reader | messageSpacing, plainTextFont, hideInlineImageAttachments, mailAttachmentAction (preview or download). | Bulwark reading settings. | B-SET-036 |
| D-064 | a5037c4 | mail | Verification-code chips (port of Bulwark's detector); attachment chips; tintListRowsByTag; configurable hoverActions; permanentlyDeleteJunk; returnToListAfterAction; clearSearchOnFolderChange. | Bulwark list features. | B-SET-010 |
| D-065 | 7f2d706 | motion | The email view background follows the theme so opening a message does not flash white. | Flicker. | B-MOT-018 |
| D-066 | 0530d4a | reader | Mark read: the unread state is taken before the local mark, so opening an unread message tells the server again (markAsReadDelay 0 never did). | Bug fix. | B-SET-032 |
| D-067 | 0530d4a | reader | Opening an uncached message cancels the page still loading for the previous message; the ready callback refuses a page whose message is no longer open. No body of one message under another's header. | Bug fix: mismatched frames. | B-MOT-014 |
| D-068 | 0530d4a | reader | While a message loads, reply, forward and attachments wait; prefetch goes 2 ahead (3 when moving one row at a time) in the direction of travel. | Prefetch behaviour. | B-MOT-014 |
| D-069 | 0530d4a | reader | Links: every a, area and form in a message is retargeted to a new window; target=_self, _top and _parent no longer navigate; data: dropped from frame-src; mailto opens the composer. | Security: no navigation out of the frame. | B-DSK-019 |
| D-070 | 0530d4a | reader | Large mail: no reveal of a half-parsed page after 600 ms; the bar shows, the page appears when laid out (8 s last resort). | Flicker. | B-MOT-014 |
| D-071 | 0530d4a | reader | Frames get allow-scripts only when the accessibility script is in the page, not when the mail contains "<script>". | Security. | B-PLAT-001 |
| D-072 | 0530d4a | reader | Cache: local read, flag and tag patch cached bodies; moves and deletes drop them; a fetch started before sign-out stores nothing (generation); conversations bounded by bytes; prefetch skips messages over 4 MB of inline images. | Cache correctness. | B-MOT-014 |
| D-073 | 0530d4a | reader | Privacy: with remote content blocked, remote stylesheets and fonts are blocked too. | Privacy. | B-SET-036 |
| D-074 | 0530d4a | settings | Perf: settings schema parsed once per process; settings read once per open; CUR_MSG holds the cached Arc instead of a deep clone. | Perf. | B-SET-001 |
| D-075 | 0530d4a | motion | The view is placed at the body's new rect before the reveal (no one-frame reflow). | Flicker. | B-MOT-014 |
| D-076 | b72743d | reader | Reader without flicker: opening keeps the message on screen until the new one is laid out; a 2 px progress bar after 150 ms; the header fills from the list row. | Feel. | B-MOT-014 |
| D-077 | b72743d | reader | bodycache: bounded per-account LRU (120 entries, 48 MB) of bodies and conversations, patched by list syncs; neighbours prefetched. | Instant switching. | B-MOT-014 |
| D-078 | b72743d | reader | mailview: the WebView loads one shell page once and never navigates; each message is a sandboxed srcdoc frame, loaded hidden and swapped in when ready; links open externally. | Single WebView document. | B-MOT-016 |
| D-079 | b72743d | motion | Overlays: the body is captured before the WebView goes; the WebView is parked outside the window, not hidden, so it keeps painting. | Flicker: no stale frame on return. | B-MOT-016 |
| D-080 | b72743d | motion | The WebView follows layout changes at once (webview-sync), not on the 50 ms poll. A refresh no longer inserts a Loading row above the list. | Flicker. | B-MOT-030 |
| D-081 | 9dc15d8 | motion | The WebView host window is painted in the page colour; theme change with the reader closed also re-colours the shell. | White strip for a frame. | B-MOT-017 |
| D-082 | b660c07 | reader | Pages over 1.5 MB load from a file (later in-memory, D-083); NavigateToString stops at 2 MB. | WebView2 limit. | D-083 |
| D-083 | 4ad1e78 | reader | Large pages are served from memory over the app's own scheme; CSP lists http: and https: instead of *; file: navigations refused. | Replaces the temp file of D-082; no file: origin. | B-PLAT-001 |
| D-084 | 645004f | reader | Reader banners: List-Unsubscribe (RFC 2369, RFC 8058 one-click POST when DKIM passed, mailto sent from the account); read-receipt request (RFC 8098 MDN, $MDNSent, readReceiptResponse ask, always or never); calendar invitation (Accept, Maybe, Decline with sendSchedulingMessages); long-body notice (isTruncated, refetch without limit). | Bulwark banners. | B-CAL-051, G-056 |
| D-085 | 645004f | reader | More menu: view source, forward as attachment (message/rfc822), download .eml, download all attachments (zip), quick rules. | Bulwark More menu. | B-SIEVE-024 |
| D-086 | 645004f | reader | Attachment menu: Save to Files, open an attached message (Email/parse), Download all. | Bulwark attachments. | B-FILE-016 |
| D-087 | 645004f | reader | Dark-mode HTML mail: Bulwark email-dark-mode.ts recolouring in the WebView (email_dark.js). | Bulwark dark mail. | B-MOT-018 |
| D-088 | 4ad1e78 | reader | Invitation answers go out as the login that owns the message; a new copy carries the answer and sendSchedulingMessages in one call (nothing left on failure). | Owner-account RSVP. | B-CAL-051 |
| D-089 | 4ad1e78 | reader | Port of Bulwark's invitation trust assessment (aligned DMARC, SPF and DKIM; sender vs organizer), shown on the banner; auth.rs reads Authentication-Results. | Bulwark invite trust. | G-049, G-051 |
| D-090 | 4ad1e78 | reader | Read receipts: Always sends only to the Return-Path or From address; first address of a list; real id for $MDNSent plus an in-run memo. | MDN hardening. | D-084 |
| D-091 | 4ad1e78 | reader | Save .eml from the source view writes the raw bytes. | Exact bytes. | D-085 |
| D-092 | 4ad1e78 | reader | One-click unsubscribe: no redirects; public addresses only (resolved and pinned); the DKIM pass must align with From. | Security (SSRF and phishing). | D-084 |
| D-093 | 4ad1e78 | reader | Save to Files copies from the message's own (shared) account. | Correct account. | B-FILE-016 |
| D-094 | 4ad1e78 | automation | rx-import, rx-find and rx-destroy exist only in builds with --features automation-writes. | Writes only in an explicit test build. | CLAUDE.md standing rule |
| D-095 | 0827d55 | sieve | Filter review: drop stale run-on-mail scans; ifInState apply with partial counts; restore the list on a failed save; validate regex and header names. | Review fixes. | B-SIEVE-023, B-SIEVE-020 |
| D-096 | d772e1f | mail | Unified rows get a row id (login, shared JMAP account, server id); sources deduped per account (dedupeUnifiedAccounts); group-inbox rows read through a client scoped to the shared account; replies from them do not mark the login's own message answered. | Bulwark unified rules. | D-021 |
| D-097 | d772e1f | mail | apply_routed confirms and resolves toggles once for the whole selection. | One confirm per selection. | B-SET-032 |
| D-098 | d772e1f | calendar | Calendar Day and Week columns follow the loaded range (3 days in a narrow pane). | Narrow windows. | B-CAL-008 |
| D-099 | d772e1f | sieve | The Sieve scanner keeps if, elsif and else chains and text: multi-line strings as one block. | Parser fidelity. | B-SIEVE-008 |
| D-100 | d772e1f | mail | load_mailboxes refreshes unified counts on its own thread; failed logins are retried after 60 s, not on every call; unified watchers stop when their token changes. | Avoid retry storms. | D-021 |
| D-101 | 0f8bdf5 | settings | Flush the pending background save on exit; h12 honours a pending save. | No lost settings. | B-SET-005, B-SET-012 |

## composer and the native editor

| ID | commit | area | decision or fix | reason | links |
|---|---|---|---|---|---|
| D-102 | 3d4591b | composer | Bulwark-style inline composer replaces the reading pane; formatting toolbar inserts lightweight markup (src/markup.rs); From, To, Cc, Bcc and Subject rows; split Send; read receipt, plain text and tag keywords on outgoing mail. | Bulwark composer layout. | B-RTE-001 |
| D-103 | 931cc20 | wip | WIP composer redesign (no body in the commit; superseded by 3d4591b). | WIP. | D-102 |
| D-104 | 3892397 | wip | WIP context menus (no body; superseded by 06a7332 and later). | WIP. | B-KEY-001 |
| D-105 | 258f07b | menus | Context menus hide the WebView2 body while a menu is open; an outside click closes the menu. | Overlays over the WebView. | B-MOT-016 |
| D-106 | 06a7332 | menus | The context menu overlay lives at the window root. | Layering. | D-105 |
| D-107 | e00bad5 | menus | The context-menu popup layer covers the window and closes on an outside click. | Outside click. | D-105 |
| D-108 | 09501fb | automation | automation: rclick (right-click command, no body). | Test input. | D-009 |
| D-109 | 435f2e0 | menus | Context-menu flyout submenus, Escape, row highlight, tag colours; event RSVP and .ics export; scrolling calendar sidebar. | Menu parity. | B-CAL-051 |
| D-110 | 5aa1bec | rte | Rich-text composer: document model, editing commands, undo, HTML import and export. | Native editor (plan §3.8). | B-RTE-001, B-RTE-002, B-RTE-011 |
| D-111 | 1c9ddec | rte | Native WYSIWYG editor (parley layout) replaces the markup TextEdit: caret, selection, hit-testing, keyboard and mouse editing, clipboard (HTML, text, images), toolbar with active states, link popover, tables, inline cid images, rich signatures, templates, collapsible quoted originals, AI on selection. | Native editor. | B-RTE-032 |
| D-112 | 290e430 | composer | Focus the editor on reply and draft; icon chevrons; bold header cells; automation key modifiers and drag. | Keyboard focus. | B-KEY-007 |
| D-113 | baa6a3e | composer | Dropped files: images inline, others attached; image insert keeps a paragraph after it. | Bulwark drops. | B-RTE-024, B-RTE-011 |
| D-114 | 33e4d03 | rte | Bulwark spacing: paragraphs 4 px, headings, lists, framed code, quote bar; selection toolbar placement. | Bulwark HTML spacing. | B-RTE-016 |
| D-115 | 18d5bb5 | composer | Composer restyle (BNCompose and Nordic): header with title, draft state, Save, Assistant, Pop out, Close; recipient rows (56 px labels); formatting toolbar in one tile; More menu for narrow composers; attachment chips with file-type badge; bottom bar with 42 px split Send; assistant panel opens beside the composer; undo toast rose tint. | Approved design. | B-SET-024 |
| D-116 | 34d3192 | composer | Recipients (after Bulwark RecipientInput and rfc5322): quote, angle and group-aware splitting; Name <addr> and RFC 5322 group parsing; group expansion with dedup; isValidEmail on the ASCII form; IDN to ASCII for sending and back for display; invalid entries are amber and block sending; groups go out as members. Suggestions from contacts, groups and the last 300 Sent messages. | GAPS P0-1. | B-RTE-023, G-054 |
| D-117 | 34d3192 | composer | Autosave: 2 s after the last change, once the user edited something and the content differs from the saved draft; saves, sends and discards queue behind a save in flight; an untouched composer closed with Esc leaves no draft; the header shows Saving and Draft saved HH:MM. | GAPS P0-3; Bulwark 2 s debounce. | L-007, L-008 |
| D-118 | 20ae864 | composer | Link popover keeps its Remove, Cancel and Apply actions in the popover; Bulwark recipient formatting; reply and forward resend quoted inline images as cid parts; automation wv-eval and wv-result. | Actions sat far from the popover. | B-RTE-009, B-RTE-022 |
| D-119 | 46198f8 | composer | Esc typed in the link URL or text field cancels only the popover; plain-text-only converts the body to plain paragraphs and drops formatting controls; link popover a little wider so Apply is not clipped. | Fix: Esc closed and saved the whole message; formatting was silently dropped on send. | B-RTE-020, B-RTE-009 |
| D-120 | 091ec6a | composer | Source view keeps inline image data, resets on a new message, follows toolbar and AI changes; the Alt flag is cleared after its key; one AI rewrite at a time. | Source view fixes. | B-RTE-019, B-RTE-028 |
| D-121 | f2f81b3 | composer | Source view fills the body; neutral Ignore item. | Layout. | B-RTE-019 |
| D-122 | 32152e9 | composer | Neutral Ignore menu item (spell check). | Neutral wording. | B-RTE-025 |
| D-123 | 68a8c94 | composer | Right-click menu (clipboard, format, paragraph, link, table, image size, AI rewrite, undo); Windows spell checking with suggestions, add and ignore; image resize handles; HTML source view; IME pre-edit via a TextInput at the caret. | Composer parity. | B-RTE-025, B-RTE-027, G-091 |
| D-124 | a9aef40 | rte | Keep single spaces between runs in the HTML export. | Whitespace fidelity. | B-RTE-016 |
| D-125 | b9b3e99 | composer | Re-opened drafts keep inline images (cid restored from the reader's data: URIs). | Drafts must not lose images. | B-RTE-021 |
| D-126 | 110ff28 | composer | Reopening the composer while a save is in flight keeps the queued work (snapshot at reset, saved over the draft); a queued discard removes it. | No lost work. | B-DSK-007, D-117 |
| D-127 | 110ff28 | composer | Top-level mailto navigations (meta refresh in mail HTML) are dropped; only clicked links open the composer. | Security. | B-DSK-020 |
| D-128 | 110ff28 | composer | Unclosed group colon in a recipient list is plain text. | Parser rule. | D-116 |
| D-129 | e3dd7e7 | composer | Hide the folders while composing; From fallback; draft-saved time; Esc dismisses the selection first; contrast accent; 15 px body; menu alignment. | Design review. | B-RTE-029 |
| D-130 | 11d9c90 | composer | Confirmations keep the scheduling intent; resize handles hidden with their pane and dragged from window coordinates; partial recipient text kept on blur while suggestions are open. | Fix: lost scheduling intent. | D-116 |
| D-131 | 3d8a7de | composer | Schedule send: dialog with Bulwark presets; Reschedule, Cancel and edit, Cancel send in the Scheduled view; Mod+Shift+Enter. Holds are sent as FUTURERELEASE HOLDFOR, worked out just before the request (HOLDUNTIL formats differ between servers). Reschedule cancels the original, resubmits, and restores the original (or the draft) when that fails. | GAPS P0-2; HOLDFOR works everywhere. | L-002 to L-006 |
| D-132 | 110ff28 | composer | Reschedule: the fallback order is a tested pure function; restore to Drafts in the submission account; destroy the cancelled record last. | Safe reschedule. | D-131 |
| D-133 | 184438a | composer | The composer opens with From filled in from the last identities read (cached list paints the first frame). | Feel: no empty From. | B-MOT-013 |

## calendar

| ID | commit | area | decision or fix | reason | links |
|---|---|---|---|---|---|
| D-134 | e1886a6 | calendar | Wire firstDayOfWeek, showWeekNumbers, calendarLimitHours and calendarDayEndHour, hide non-working days, showTimeInMonthView, calendarTintedEvents; 12-hour times. | Bulwark settings. | B-CAL-055 |
| D-135 | e81c242 | calendar | Event editor depth, recurrence rules, reminders firing, agenda, tasks, .ics import, webcal, birthdays. | GAPS P0-6 and P0-7 and P1-4. | B-CAL-011, B-CAL-028 |
| D-136 | cbaa64a | calendar | Review fixes: day header height; capped sidebar tasks with overdue tint; reminders across accounts and 8+ days ahead; webcal stable UIDs; failure reporting; no remote alerts; bounded fetch. | Review. | B-CAL-030, B-CAL-043, B-CAL-044 |
| D-137 | 158e645 | calendar | Bento and Nordic restyle of the calendar (sidebar, summary tiles, grid, event context panel, editor, hover preview); values come from loaded data. | Approved design. | B-CAL-010 |
| D-138 | 08199d2 | calendar | Design review fixes: no grid scrollbar; equal summary tiles; single-letter mini-month weekdays; compact header in tight windows; summary and Next up from all events. | Review. | B-CAL-054, B-CAL-060 |
| D-139 | 8b501d4 | calendar | Month timed-event styles; free scroll; hover preview; narrow week; unread-first Inbox section size. | Calendar review. | B-CAL-010 |
| D-140 | c5ec09e | calendar | Verify fixes: calendar now line and 12-hour hour labels; task "No due date"; sidebar calendar Colour and no delete of the default; file dates follow timeFormat; Move to trash label; confirm Empty trash. | Live verification. | B-CAL-056, B-CAL-057, B-CAL-039 |
| D-141 | 5c6e668 | calendar | View switcher in Tasks; mini-month day filter; select versus open for tasks; agenda marker fix; compact search. | Calendar review. | B-CAL-037, B-CAL-040 |
| D-142 | 55de053 | calendar | Fix: Agenda and Tasks panicked (nested RefCell borrow); both now render after the borrow is released; the mail search TextInput is full height and centred. | Crash found by smoke test. | B-CAL-048 |
| D-143 | 8f53ca7 | calendar | Calendar header: smaller title when the grid is narrow. | Readable dates. | B-CAL-053 |
| D-144 | d16233d | calendar | Calendar drag rollback and scheduling; skip no-op drops. | Drag correctness. | B-CAL-052, B-CAL-016 |

## contacts and files

| ID | commit | area | decision or fix | reason | links |
|---|---|---|---|---|---|
| D-145 | a90b362 | contacts | Sort contacts by last name (Bulwark getContactSortName), applied live. | Bulwark sort. | B-CON-004 |
| D-146 | f2cc3ac | contacts | Contacts sidebar (address books, groups, categories) with group, category and book menus; prefixed row highlight. | Bulwark contacts layout. | B-CON-001 |
| D-147 | 4d24260 | contacts | Membership keyed like Bulwark (uid or id); categories sent as the whole keywords map. | Bulwark compatibility. | B-CON-006 |
| D-148 | 4f2d73b | contacts | Check the server for an existing address book before creating a default one. | Avoid duplicate "Contacts" books. | B-CON-003 |
| D-149 | 6170ed5 | contacts | Contacts depth: vCard 2.1, 3.0 and 4.0; typed entries; photos; duplicates and merge; directory; import and export; Settings > Contacts Import and Export rows. | GAPS P1-5. | B-CON-018 to B-CON-025, G-010 to G-012 |
| D-150 | 02ab6c7 | contacts | Contacts and Files restyled to Bento and Nordic (BNContacts): sidebar, letter headers, header tile and tiles for recent mail, next meeting, shared files and notes; Files: Upload, places tile, trash and empty states. | Approved design. | B-CON-030, B-FILE-017 |
| D-151 | efb754b | settings | Calendar and address book management pages with row context menus. | Bulwark settings parity. | B-SET-024 |
| D-152 | db53d83 | files | Files sidebar (My files tree, Favorites, Recent, Shared with me), star, share via shareWith, move, cut, copy, paste, duplicate; per-account blobs. | Bulwark files. | B-FILE-005, B-FILE-006 |
| D-153 | 96dfe5c | files | Draft-18 create is detected from supportedNodeTypes (empty drive). | Empty drive on draft 18. | B-FILE-002 |
| D-154 | 4c1c383 | files | Bulwark trash (.Trash with dated batches; restore, delete forever, empty) and an Upload folder. | Bulwark trash. | B-FILE-007, B-FILE-010 |
| D-155 | 2fbd691 | files | People and Files review fixes: narrow Files layout; menu placement; empty-trash state; wrapping places tile; tree indents; category chips. | Review. | B-FILE-015 |
| D-156 | c5ec09e | files | File star beside the name; focused inline rename; Favorites and Recent sidebar menus; trash column alignment. | Verify fixes. | B-FILE-014, B-FILE-012 |
| D-157 | d16233d | files | Confirm permanent file deletes; drag and drop of files. | Data safety. | B-FILE-011 |

## admin and dashboard

| ID | commit | area | decision or fix | reason | links |
|---|---|---|---|---|---|
| D-158 | 186eb0c | admin | Stalwart-style metric tabs, cards and native charts (WIP). | Stalwart WebUI parity. | B-ADM-010 |
| D-159 | d304727 | admin | Smooth stacked bands, fit-to-fill chart paths, layout polish. | Charts. | B-ADM-012 |
| D-160 | 6539798 | admin | Dashboard uses the app's neutral palette, not Stalwart's warm tones. | Design system. | B-ADM-020 |
| D-161 | 7bf426e | admin | Dashboard charts follow stalwartlabs/webui: null for empty buckets; connectNulls or carry-forward; d3 monotoneX; series stacked in legend order; nice five-tick y axis; ticks at bucket centres; compact formatting; hover crosshair and tooltip card. | Chart parity with the WebUI. | B-ADM-010 to B-ADM-015 |
| D-162 | bb42a58 | admin | Tasks (Scheduled, Failed, Index queue), Cluster (nodes, roles, coordinator) and Actions (x:Action with confirm, form and result) pages as in Stalwart's WebUI. | Read pages in M7. | B-ADM-004 to B-ADM-006 |
| D-163 | f1f33c5 | admin | Thin x-axis ticks by label width; a gauge with one sample is carried across empty buckets. | Narrow charts; gauge spike bug. | B-ADM-014, B-ADM-018 |
| D-164 | dc05a14 | admin | Cache cards 110 px; bar-list tooltip below the row; neutral grey 0% badge; dashboard times follow timeFormat; sub-ms values keep two decimals. | Review fixes. | B-ADM-016 to B-ADM-019 |
| D-165 | d2e70a7 | admin | Admin dashboard, settings, sign-in and dialogs in the Bento and Nordic design: tinted stat cards (lead rose, worsening amber), a Clean mail meter. | Approved design. | B-ADM-021 |
| D-166 | 3cbd790 | admin | Full-width loading; narrow dashboard; Nordic hairlines; aligned columns; Escape; settings search. | Review. | B-ADM-023 |

## search, palette, shortcuts

| ID | commit | area | decision or fix | reason | links |
|---|---|---|---|---|---|
| D-167 | 8253677 | search | Command palette (Ctrl+K) and search everything; query syntax; SearchSnippet highlights; search history of ten; suggestions; keyboard shortcuts for every app; drag and drop; resizable panes. | GAPS P1-1, P1-2, P1-3 and P0-9. | B-PAL-001, B-PAL-009, B-KEY-011 |
| D-168 | bff0eda | search | Palette field takes arrows before the edit; search-box suggestions keep Up, Down and Esc; larger single-glyph key caps. | Keyboard fixes. | B-PAL-013, B-PAL-014 |
| D-169 | 8253677 | shortcuts | Drag and drop: mail to folders and tags; folders reordered and re-parented (sortOrder plan); files onto folders and the trash; events moved and resized in 15-minute steps. | Bulwark drag and drop. | B-CAL-052 |
| D-170 | d16233d | shortcuts | Ctrl+Q, Shift+I and u never toggle; no Backspace delete in mail; calendar drag rollback and scheduling; skip no-op drops; keep role sortOrder; theme accent highlights; global-search folder choice. | Review fixes. | B-KEY-012, B-KEY-015 |

## desktop, platform, updater, distribution

| ID | commit | area | decision or fix | reason | links |
|---|---|---|---|---|---|
| D-171 | 63fa780 | desktop | mailto: links open the composer; single-instance handoff; default mail app registration (HKCU on Windows, .desktop on Linux). | GAPS P0-8. | B-DSK-018, B-DSK-019 |
| D-172 | 440ebb6 | platform | Platform layer: config and data folders; open_url; clipboard; system light and dark (Win32 API, XDG portal, gsettings); start at login as a testable plan; hunspell for Linux behind ISpellChecker; GTK pump for WebKitGTK; keyring on Credential Manager or Secret Service. | One place for OS differences. | B-PLAT-001 to B-PLAT-011 |
| D-173 | 440ebb6 | desktop | Notifications: WinRT toasts with the app id, tag and group per account, Archive, Mark as read and Delete buttons; freedesktop notifications with the same actions; mail that arrives together is one notification; Do not disturb honoured. | Bulwark mail notifications. | B-DSK-009 to B-DSK-014 |
| D-174 | 440ebb6 | desktop | Tray with Open, Compose, Check mail and Quit; unread badge; taskbar overlay; close to tray; start in the tray at sign-in; start at login; Settings > Desktop app. | Bulwark desktop OS host. | B-DSK-001 to B-DSK-006 |
| D-175 | 440ebb6 | updater | Ed25519-signed per-channel manifest; background download with SHA-256; install only after the user's OK (Restart now or Install next time); MSI and AppImage replace themselves; zip, deb and dev only say a version exists. | Safe updates. | B-UPD-001 to B-UPD-020 |
| D-176 | 440ebb6 | desktop | Linux email view: wry child WebKitGTK; on Wayland, the Blitz renderer shows mail. | Linux rendering. | B-PLAT-001 |
| D-177 | b7b4777 | distribution | Release and CI workflows; per-user MSI; portable zip; .deb (cargo-deb); AppImage; SHA256SUMS; signed update manifest; Authenticode and GPG hooks behind secrets; quit saves an open draft. | Packaging. | B-UPD-029, G-087 |
| D-178 | b7b4777 | distribution | install-kind markers for msi, zip and deb so the updater knows what it may replace. | Updater safety. | B-UPD-008 |
| D-179 | b7b4777 | distribution | Quitting (window close, tray Quit, Restart to update) saves an open draft first. | No lost draft. | B-DSK-007 |
| D-180 | d4d2fad | updater | MSI ProductVersion from the tag: 1.2.0-beta.1 gives 1.2.301; 1.2.0 gives 1.2.999; the beta channel gets stable builds; stable tags write and sign stable.json and beta.json. | MSI upgrade ordering. | B-UPD-026, B-UPD-014 |
| D-181 | d4d2fad | updater | Asset base URL is a repository variable (private-repo assets 404); manifests carry published and expires; expired or older-than-seen manifests refused; notes and asset URLs https only; no loopback in release builds. | Security and availability. | B-UPD-011, B-UPD-015, B-UPD-016 |
| D-182 | d4d2fad | updater | MSI installs through a PowerShell helper that waits for Barbican's PID; pending.json paths must be .msi or .AppImage inside the updates folder with no shell characters; an update installs only when no other Barbican runs. | Installer safety. | B-UPD-021, B-UPD-024, B-UPD-025 |
| D-183 | d4d2fad | distribution | Quit with a blank composer saves no draft; a failed or impossible save is known at once; a second quit within a minute closes without saving. | Avoid stray drafts. | B-DSK-007 |
| D-184 | 110ff28 | desktop | Handoff: the server greets first, so a stale port owned by another program never receives the token or link; per-connection threads with short deadlines; no listening without randomness; instance file removed on clean exit. | Security of the single-instance channel. | B-DSK-022 |
| D-185 | 110ff28 | desktop | Links that arrive while signed out are kept until sign-in, with a toast; registration is repaired at startup when the exe moved; unregister is in Settings; is_default cached. | Robustness. | B-DSK-018, B-DSK-021 |

## design, motion, docs and tooling

| ID | commit | area | decision or fix | reason | links |
|---|---|---|---|---|---|
| D-186 | 1a28f7a | design | Approved Bento and Nordic mockup sources imported. | Design source of truth. | design/ |
| D-187 | 71349b3 | design | Design foundation: Bento and Nordic tokens; Style setting (bento or nordic) applied live; line icons; shell and mail screen in both styles. | Approved mockups. | B-SET-024 |
| D-188 | c99e27c | design | Line icons in the settings nav; assistant rail state; Nordic text-only CTAs; admin nav alignment; dialog shell; context-menu height. | Design integration. | B-SET-024 |
| D-189 | 301b129 | motion | Flicker baseline on the Bento and Nordic layout (main and probe), for after-bento.json. | Feel baseline. | B-MOT-026 |
| D-190 | a675246 | motion | Frame-burst flicker probe: whole-window capture including the WebView (wshot); burst capture every few ms; tools/flicker.py reports blank and flash frames; tools/scenarios.py runs the transitions. | Measure, do not eyeball. | B-MOT-024, B-MOT-025 |
| D-191 | f7c216b | tooling | Python bytecode ignored (probe tools). | Hygiene. | n/a |
| D-192 | ea190d4 | motion | Motion foundation: tokens (Motion and Ease globals), shared building blocks, incremental lists, frame probe; hover, press and selection feedback; contexts pop in; tooltips wait then fade; toast slides in and fades out. | Feel. | B-MOT-001 to B-MOT-013, B-LST-001 |
| D-193 | 38bf81f | motion | Composer fade-in; popover PopIn; attachment chips; assistant fade; incremental compose models. | Feel. | B-MOT-019 |
| D-194 | b7dc2a8 | tooling | Stray pycache untracked. | Hygiene. | D-191 |
| D-195 | b3c8cdf | motion | Admin and settings: in-place models; async settings save; view fade-in; nav, dialog and login feedback. | Feel. | B-MOT-020 |
| D-196 | 1a1eaa5 | motion | Mail fade-in for async panels, summary tiles, selection bar and thread strip; focus and hover feedback; in-place thread strip, context menu and glance lists. | Feel. | B-MOT-022 |
| D-197 | 464e518 | motion | Calendar, contacts and files incremental models; view fade-in; hover feedback; calendar probe scenarios. | Feel. | B-MOT-021 |
| D-198 | 358bb57 | motion | Late-content fades are instant while keys repeat (Motion.late). | Key repeat must not lag. | B-MOT-009 |
| D-199 | e7a74c9 | motion | Motion section in ROADMAP; motion-layout.json for the merged calendar toolbar. | Layout data. | B-MOT-028 |
| D-200 | 7a60ae8 | motion | Mail list hover actions are a clickable overlay (no text shift); first-frame overlays; undo and redo icons; keys pressed in the email view are replayed in the app; focus returns to the key handler when overlays close. | Hover survives the pointer. | B-MOT-023, B-KEY-025, B-KEY-026 |
| D-201 | ea190d4 | motion | src/motion.rs: Motion.reduce from Settings or Windows animation effects; Motion.rapid on key repeat. | Accessibility. | B-MOT-001, B-MOT-002 |
| D-202 | ea190d4 | listsync | src/listsync.rs: diff a VecModel by key with set_row_data, insert and remove (reset when mostly new). | Keep hover and avoid rebuilds. | B-LST-001 |
| D-203 | 57d7c24 | docs | Roadmap: native rich-text composer, parity or better with Bulwark (subject). | Plan. | D-111 |
| D-204 | fec54e9 | docs | Roadmap: multiplatform CI, releases, packages, auto updater, tray and notifications. | Plan. (CI is not used in the C# repo; D-204 is historical.) | D-177 |
| D-205 | 9482bdd | settings | Accessibility page (Bulwark ACCESSIBILITY_DEFAULTS) applied natively; hover-action rows, mode and corner; working days; plain-text font row. | Parity. | B-SET-017, B-SET-011 |
| D-206 | ffb971b | settings | Settings sweep: archive per year or month; placeholder warning; plain-text-only composer; unread count in the window title. | Parity. | B-SET-015, B-SET-016 |
| D-207 | 38fa42d | settings | Hour options for calendarDayEndHour. | Parity. | B-SET-013 |
| D-208 | 9e11657 | settings | Back to mail returns to mail; the trash row spacer sits before the restore action. | Fix. | B-KEY-013 |
| D-209 | 3eee208 | docs | GAPS.md: remaining Bulwark parity gaps after the JMAP and parity round. | Gap list (the source of gaps.csv). | spec/gaps.csv |
| D-210 | cd0dc2f | docs | Bulwark interaction inventory (INTERACTIONS.md). | Parity checklist (plan §2.2: interactions.csv, cat-a). | D-211 |
| D-211 | d679c23 | docs | INTERACTIONS.md: summary counts and ranked missing list. | Parity checklist. | D-210 |
| D-212 | b14e593 | docs | PARITY-ROUND2.md added and round-2 work dropped from GAPS.md. | Per-stream done, partial, not done. | spec/live-checks.md |
| D-213 | 04f7b96 | sieve | Vacation-only scripts without metadata read as the auto-reply; hand-written blocks anywhere after the metadata are kept (Bulwark scans every block). | Bulwark parity. | B-SIEVE-005, B-SIEVE-006 |
| D-214 | 846b17b | sieve | Sieve parity for active periods and vacation extras: rule periods (currentdate tests, date and relational requires), the vacation include with audience, auto-reply and vacation forwarding exactly as Bulwark's generator; the whole metadata is carried through saves; only scripts Bulwark would treat as hand-edited are locked; metadata key order kept. | Bulwark generator parity. | B-SIEVE-004, B-SIEVE-010, B-SIEVE-012 |
| D-215 | ceb2327 | sieve | Spam guard for move and copy rules (spamtestplus), verified in Bulwark's Sieve view. | Bulwark spam guard. | B-SIEVE-009 |
| D-216 | e5837df | sieve | Filters depth: edit and reorder rules; multiple conditions and actions; header, size and regex conditions; run on existing mail. | GAPS P1-7. | B-SIEVE-015, B-SIEVE-023 |
| D-217 | b6a37f1 | sieve | Assistant filter tools add_filter_rule and remove_filter_rule (Bulwark-compatible, replace raw update_filters). | Safer assistant. | B-SIEVE-027 |
| D-218 | c6b57c9 | sieve | Sieve filters with validation (see D-035). | Parity. | B-SIEVE-001 |

## WIP branches (stopped when the Slint app was retired)

| ID | commit | area | decision or fix | reason | links |
|---|---|---|---|---|---|
| D-219 | 96747db | wip | WIP mat-overlay: maturity pass on ui/app.slint, stopped when Slint was retired. Intent (catalogue): overlays and the message stays whole under the bar. | Preserved for reference; Slint retired. | branch `mat-overlay` (frozen) |
| D-220 | fae5fc9 | wip | WIP mat-polish: dark_mail.rs, email_dark.js, main.rs, platform/win.rs and UI files; stopped at the port start. Intent: dark-mode polish of mail. | Preserved. | branch `mat-polish`; B-MOT-018 |
| D-221 | 20f4fd5 | wip | WIP mat-switching: mailview.rs, main.rs, msgload.rs, app.slint; the message on screen stays whole under the bar, dimmed, until the new one swaps in. | Preserved; switching feel. | branch `mat-switching`; B-MOT-014 |
| D-222 | 96747db / fae5fc9 / 20f4fd5 | wip | The three mat-* branches were not on origin before the freeze; they are pushed in M0 (PORT-PLAN §2.6) and their intent recorded here. | Freeze. | docs/legacy/branches.md |

## Merge commits (parity round 2, `parity-round2`; not merged into main at the time)

Taken from `desktop/PARITY-ROUND2.md`. Each merge had `cargo test` green; the test counts are the
merge-time counts.

| ID | commit | area | decision or fix | reason | links |
|---|---|---|---|---|---|
| D-223 | 27002bb | merge | Composer merge (115 tests). | Parity round 2 stream. | D-116, D-117, D-131 |
| D-224 | 18b8308 | merge | Reader merge (154 tests). | Parity round 2 stream. | D-084 |
| D-225 | 7e373a5 | merge | Calendar merge (190 tests). | Parity round 2 stream. | D-135 |
| D-226 | 5787adc | merge | Productivity merge (231 tests). | Parity round 2 stream. | D-167 |
| D-227 | 071b64a | merge | PIM merge (252 tests). | Parity round 2 stream. | D-149 |
| D-228 | 8f149aa | merge | Distribution merge (290 tests, +1 ignored). | Parity round 2 stream. | D-177 |
| D-229 | 6f658f3 | merge | Mail extras merge (316 tests, +1 ignored). | Parity round 2 stream. | D-054 |
| D-230 | 55de053 | merge | Smoke-test fixes (316 tests, +1 ignored); the agenda crash fix (see D-142). | Smoke test. | D-142 |

## Late additions (commits found on the final pass)

| ID | commit | area | decision or fix | reason | links |
|---|---|---|---|---|---|
| D-231 | e634a45 | automation | The automation "pick" command answers the next file-open picker (attach, upload) without a dialog. | Tests must not open a native dialog (no real input). | D-009 |
| D-232 | bba9613 | docs | PORT-PLAN.md: the master plan for the C# port (WinUI 3 on Windows, Uno on Linux). It is the plan this catalogue serves. | Plan. | docs/PORT-PLAN.md |

## M1 decisions (C# port)

| ID | commit | area | decision or fix | reason | links |
|---|---|---|---|---|---|
| D-233 | m1-f2 | reader | Lite mode (`lite.rs`, the Blitz CPU renderer for mail) is dropped: there is no .NET Blitz equivalent. The plain-text render mode stays as the fallback. User OK pending: the user confirms at the M1 gate. T-159 `lite::renders_email_html` gets no C# test. | PORT-PLAN §1 "Deliberate drops", Appendix A and B (M1: "lite 1, marked dropped"). | B-READ-046, T-159, docs/PORT-PLAN.md |
