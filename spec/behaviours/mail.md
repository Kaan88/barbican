# Behaviours: mail view state, tags, attachments, dark mail, unified inbox, import

Area: `MAIL`. Owner milestones: M1 (tags, attachments, dark mail, mail state), M2 (unified inbox, import).

Sources: `src/mail.rs`, `src/glance.rs`, `src/tags.rs`, `src/attachments.rs`, `src/dark_mail.rs` (and `src/email_dark.js`), `src/unified.rs`, `src/mailimport.rs`. Pins as in `jmap.md`. Bulwark references are to version 1.21.2.

Sync, list model and message switching rules are in `sync.md`. Reader banners, sender pictures, one-time codes and the body view CSP are in `reader.md`.

## Mail view state (`mail.rs`)

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-MAIL-001 | Mail view state (folders, tags, list with paging and selection) lives on the UI thread. Network work runs on background threads and returns through the event loop. | mail.rs:1-2 | | none |
| B-MAIL-002 | Folder titles and roles come from the mailbox list. Role lookup (`role_id`) and mailbox info are read from the cached list, not from the server each time. | mail.rs:216-250 | RFC 8621 2 | none |
| B-MAIL-003 | Archive folder paths are built from the message's received date: by year (`2024`) or by year and month (`2024`, `06`). | mail.rs:1064 `archive_path` | Bulwark archive settings | `archive_paths` |
| B-MAIL-004 | The window title shows the unread count in brackets, e.g. `(5) Barbican`, and is plain at zero. | mail.rs:203 `badge_title` | | `unread_badge_title` |
| B-MAIL-005 | The forced actions `read!` and `unread!` set the state and never toggle it. Plain `read` and `unread` toggle. | mail.rs:960 `forced`, :965 `resolve_toggle` | | `forced_read_never_toggles` |
| B-MAIL-006 | A confirm step is asked before actions that affect several messages (`confirm_action`). | mail.rs:940 `confirm_action` | | none |
| B-MAIL-007 | Toast after an action can offer undo. | mail.rs:1102 `show_toast` | | none |
| B-MAIL-008 | Mail page size is 50 messages. | mail.rs:52 `PAGE` | | none |
| B-MAIL-009 | Preview text is cleaned before display. | mail.rs:1128 `clean_preview` | | none |

## Tags (`tags.rs`)

Bulwark mapping: `settings-store` `emailKeywords` (`{id, label, color}`), `lib/keyword-nesting.ts`. On a message a tag is the JMAP keyword `$label:<id>` (RFC 8621 4.1.1).

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-MAIL-010 | Tags are stored in settings as `emailKeywords` and applied to messages as `$label:<id>`. | tags.rs:1-4, :36-42 | Bulwark `emailKeywords` | `stored_tags_and_colours` |
| B-MAIL-011 | With `nestedTags` on, a `/` in an id makes a tree: `work/clients` sits under `work`. Off, every tag is flat. | tags.rs:12 `SEPARATOR`, :44 `nested`, :78 `parent_of`, :80 `is_descendant` | Bulwark `keyword-nesting.ts` | `tree_rows` |
| B-MAIL-012 | A tag id is normalised: lower case, runs of characters that are not letters or digits become `-`, leading and trailing `-` trimmed. Example: `  Work Stuff!! ` becomes `work-stuff`; `a/b` becomes `a-b` as a single level. | tags.rs:54 `normalize_level` | Bulwark | `ids_follow_bulwark` |
| B-MAIL-013 | `generated_level(name)` gives `tag-` plus the FNV hash of the name in base 36. The hash matches the JavaScript client, so ids agree across clients. The exact call site (when the normalised name is not usable) is to be confirmed in tags.rs:66. | tags.rs:66 `generated_level` | Bulwark (JS) | `fnv_matches_javascript` (`A` gives `tag-1r9wi7g`) |
| B-MAIL-014 | A tag id is at most 128 characters (`MAX_KEYWORD`) and must pass `valid_id`. | tags.rs:14, :47 | RFC 8621 4.1.1 | none |
| B-MAIL-015 | A new tag under a parent is `parent/normalised-name`. A name already taken under the same parent is refused, and a clash gets a suffix from `choose_id`. | tags.rs:84 `choose_id`, :98 `name_taken`, :147 `add` | | `manage_tags` |
| B-MAIL-016 | Tag colours come from a fixed 13-colour palette (`PALETTE`): red, orange, amber, yellow, lime, green, teal, cyan, blue, indigo, purple, pink, gray. | tags.rs:144 | Bulwark | `stored_tags_and_colours` |
| B-MAIL-017 | Tag tree rows carry depth and a chevron for collapsible parents. A collapsed set hides children. | tags.rs:105-111 `rows` | | `tree_rows` |
| B-MAIL-018 | Renaming and removing a tag are refused for a name that clashes. Colour change does not change ids. | tags.rs:162 `rename`, :173 `remove`, :180 `set_color` | | `manage_tags` |

## Attachments (`attachments.rs`)

Bulwark mapping: `reader-attachments.tsx` with `attachmentImagePreviewsEnabled`.

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-MAIL-019 | Each attachment is a tile with its name and size. Images up to 10 MB (`MAX_PREVIEW_BYTES`) show a picture, fetched and scaled in the background and kept for the session. Other files show their kind. | attachments.rs:1-4, :14, :32 `previewable` | Bulwark | `which_attachments_preview` |
| B-MAIL-020 | SVG and PDF are not previewed, even when small. | attachments.rs:32 | | `which_attachments_preview` |
| B-MAIL-021 | Thumbnails are square, `THUMB` (80) px per side. Bytes that are not an image give no thumbnail. The thumbnail cache holds 200 entries (`CACHE_MAX`). | attachments.rs:16-17, :51 `thumbnail` | | `thumbnails_are_square`; changed (Fable F-09): PNG thumbnails stream scanline by scanline into the 80 px cells (no whole picture in memory), the whole-picture decode is capped at 16 MP, one thumbnail is made at a time and out-of-memory gives no thumbnail: PngThumbnailTests |
| B-MAIL-022 | A name splits at its last dot into stem and kind; the kind is shown upper case (`report.final.pdf` gives `report.final` and `PDF`). Dotfiles and names with no extension have no kind. The kind is taken only when the text after the last dot has no space (`a.tar gz` has no kind). | attachments.rs:24 `split_name` | | `names_and_kinds` |
| B-MAIL-023 | The attachment summary counts the files and their total size. | attachments.rs:45 `summary` | | none |
| B-MAIL-024 | Attachments are shown in the reader only when the setting allows them (`wanted`). | attachments.rs:40 `wanted` | | `which_attachments_preview` |

## Dark mail (`dark_mail.rs`, `email_dark.js`)

Bulwark mapping: `lib/email-dark-mode.ts`. The colour rewrite runs in the WebView (`email_dark.js`, allowed by its CSP hash). This module holds the parts around it.

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-MAIL-025 | A message that carries its own `prefers-color-scheme: dark` query is "native dark" (detected case-insensitively). Its colour scheme reports dark only when the app is dark and the message is native. | dark_mail.rs:20 `has_native_dark`, :32 `color_scheme` | Bulwark | `native_dark_detection` |
| B-MAIL-026 | The message's own `prefers-color-scheme` queries are pinned to the app theme: `(min-width: 0px)` in dark, `(max-width: 0.001px)` in light. Inside the WebView they would otherwise follow the OS. | dark_mail.rs:16-17, :45 `resolve_color_scheme_media` | | `pins_media_queries_to_the_app_theme` |
| B-MAIL-027 | Until the dark pass has run, the body is hidden (class `barbican-dark-pending`), so the light original never flashes. | dark_mail.rs:10, :35 `css` | | `css_hides_until_the_pass_ran` |
| B-MAIL-028 | The dark fallback is text `#e6e6e6` on background `#0a0a0a`, links `#8ab4f8`. | dark_mail.rs:12-14, :35 | | `css_hides_until_the_pass_ran` |
| B-MAIL-029 | The rewrite script is `email_dark.js`, included verbatim, with its CSP hash recomputed in the port. | dark_mail.rs:8 `SCRIPT` | PORT-PLAN 2.2 | `css_hides_until_the_pass_ran` (script contains the colours) |

## Unified inbox (`unified.rs`) (M2)

Bulwark mapping: "All inboxes", `unifiedCrossAccount`, `includeGroupInUnified`, `positionsByAccount`.

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-MAIL-030 | The unified folder has the id `__unified`. | unified.rs:15 `FOLDER_ID` | | none |
| B-MAIL-031 | Scope: `unifiedCrossAccount` (default off) spans every remembered login; otherwise only the active one. `includeGroupInUnified` (default on) adds the shared and group accounts each login can reach. | unified.rs:1-9 | Bulwark settings | `shared_accounts_from_session` |
| B-MAIL-032 | Clients connect lazily on background threads. The active session's client is reused. A failed account is retried after 60 s (`RETRY_AFTER`). Other logins' unread counts refresh every 60 s (`OTHER_EVERY`). | unified.rs:29, :60-120, :136 | | none |
| B-MAIL-033 | Paging is per source: the next page asks each source for the messages after those already loaded from it. The merged list is ordered by parsed timestamp and de-duplicated. | unified.rs:1-9 | Bulwark `positionsByAccount` | `merge_orders_by_parsed_time_and_dedupes`, `positions_count_per_source` |
| B-MAIL-034 | Each account is read once per refresh, even when it appears as both a login and a shared account. | unified.rs:555 | | `dedupe_reads_each_account_once` |
| B-MAIL-035 | Row ids keep sources apart. The id joins the user, the optional shared account and the message id with control characters `\x1c` and `\x1d`, so one message in two accounts is two rows. | unified.rs:34-53 `row_id`, `parse_row` | | `row_ids_keep_sources_apart` |
| B-MAIL-036 | The unread badge for the other logins sums the unread counts of every login except the active one. An active login that is not in the map excludes nothing. | unified.rs:139 `sum_other_logins` | | `other_logins_badge_sum` |
| B-MAIL-037 | A mailbox is matched by role first, then by name. So "archive" matches the archive role even when named differently. | unified.rs:593 `match_mailbox` | | `match_by_role_then_name` |
| B-MAIL-038 | Each account's row colour comes from a fixed 8-colour palette (`C` in unified.rs). | unified.rs:201 | | none |

## Import of .eml and .mbox (`mailimport.rs`) (M2)

Bulwark mapping: `mail-transfer/mail-import-dialog`, `lib/mail-import` (`mbox.ts`, `mbox-status.ts`, `engine.ts`).

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-MAIL-039 | Each message is uploaded as a blob and filed with `Email/import` (RFC 8621 4.8). | mailimport.rs:1-4 | RFC 8621 4.8 (J-057) | `import_loop_created_not_created_and_duplicates` |
| B-MAIL-040 | The read, answered and flagged states come from the mbox `Status` and `X-Status` headers as keywords (`$seen`, `$flagged`, ...). Arrival time comes from the `Date` header. | mailimport.rs:1-4, :429 `states_and_dates` | | `states_and_dates` |
| B-MAIL-041 | An mbox is split at `From ` envelope lines. Both a full date and a short MAILER-DAEMON style envelope line count. | mailimport.rs:407 `splits_mbox`, :420 `is_envelope_line` | | `splits_mbox`, `envelope_lines` |
| B-MAIL-042 | A single .eml is one message with its bytes kept exactly. | mailimport.rs:447 | | `eml_is_one_message_and_entries` |
| B-MAIL-043 | The mbox reader streams one message at a time. A single .eml is capped at 200 MB (`MAX_EML`). | mailimport.rs:14, :514 | | `streaming_reader_and_eml_cap` |
| B-MAIL-044 | The import summary is pluralised and names duplicates already in the folder: "Imported 1 message", "Imported 2 messages, 3 already in the folder". | mailimport.rs:396 `summary` | | `summaries` |
| B-MAIL-045 | The import loop counts created, not-created and duplicate messages. Each message is uploaded once. | mailimport.rs:487 | | `import_loop_created_not_created_and_duplicates` |

## At-a-glance data (`glance.rs`) (M1)

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-MAIL-046 | The mail screen's at-a-glance data is read-only and comes from the account. Nothing is made up: list header (folder title, unread count, greeting), Bento summary tiles (today's events, flagged mail, open tasks), storage meter, sender panel (contact, recent mail from the sender, files they sent, next event with them). | glance.rs:1-5 | design/mockups BNMail, NordMail | none (no Rust tests; the port adds them) |

## Gaps and notes

- The Rust module has no tests for `glance.rs` (0 T- rows). The port must add tests for each tile's query.
- B-MAIL-032 numbers (60 s retry, 60 s refresh) are from constants and were not re-checked against Bulwark's timings.
- Tags use the FNV hash to agree with the JavaScript client (B-MAIL-013). Any change to the hash breaks existing `tag-` ids, so the port must keep the exact algorithm.

## M1 status (stream mailcore)

Ported in `m1-mailcore`: B-MAIL-003..005 (`MailRules`), B-MAIL-010..018 (`TagRules`, `TagRulesTests`), B-MAIL-019, B-MAIL-021 (thumbnail 80 px; Core decodes PNG
itself and takes other formats from a platform `IImageDecoder`; the 200-entry session cache is the body stream's with the fetch), B-MAIL-020, B-MAIL-022..024
(`AttachmentCards`), B-MAIL-046 (list header, flagged and storage tiles; the calendar and task tiles are M4: `GlanceTilesViewModel.SetCalendar`).
B-MAIL-001, B-MAIL-002, B-MAIL-008, B-MAIL-009: `MessageListViewModel`, `FolderRows`, `RowText.CleanPreview`.

## M1 status (stream body)

Ported in `m1-body`: B-MAIL-025..028 (`DarkMail`, T-074..T-076 `DarkMailTests`), B-MAIL-029 (`email_dark.js` verbatim as the App asset and as Core's
string, kept equal by `DarkMailScriptTests`; the frame's CSP allows it by its SHA-256 hash; the asset is byte-equal to the Rust app's
`src/email_dark.js` apart from line ends).

## M1 status (verify)

Rows no stream marked, settled at the M1 verify stage: B-MAIL-006 (confirm before bulk actions) and B-MAIL-007 (undo toast): deferred(M2), mail actions.
B-MAIL-030..038 (unified inbox: id, cross-account scope, lazy clients, paging, dedupe, row ids, badge sum, role match, row colours): deferred(M2); the
Rust tests of unified.rs are M2 rows of `spec/rust-tests.csv`. B-MAIL-039..045 (mbox/eml import): deferred(M2), the import rows of `spec/rust-tests.csv`.
