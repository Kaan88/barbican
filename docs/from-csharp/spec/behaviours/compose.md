# Behaviours: composer, drafts, scheduling, recipients, mailto, rich text

Area: `COMPOSE`. Owner milestone: M3 for every module here (Appendix B). Rows about the server calls cite the JMAP area (`jmap.md`).

Sources: `src/compose.rs`, `src/autosave.rs`, `src/schedule.rs`, `src/chips.rs`, `src/recipients.rs`, `src/mailto.rs`, `src/instance.rs`, `src/templates.rs`, and `src/rte/` (`model.rs`, `editor.rs`, `html.rs`, `spell.rs`, `layout.rs`, `mod.rs`). Pins as in `jmap.md`. Bulwark references are to version 1.21.2.

`rte/` is the Opus-owned editor (PORT-PLAN 3.8). `rte/layout.rs` has no tests; its behaviour is checked by screenshot and by S3 (spike).

## Composer (`compose.rs`)

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-COMP-001 | The composer covers new, reply, reply-all, forward, and edit draft. It has identities, Cc and Bcc, attachments (uploaded), drafts, and undo-send through EmailSubmission HOLDFOR. | compose.rs:1-2 | Bulwark `email-composer.tsx` | none |
| B-COMP-002 | A draft is blank when it has no recipients, subject, body or user text. A signature alone, or a signature with a quote, is blank. | compose.rs:1158 `is_blank` | Bulwark | `blank_drafts` |
| B-COMP-003 | Recipients format as `Name <addr>`. A name with commas or quotes is quoted and its quotes escaped. A bare address stays bare. | compose.rs:1172 `format_recipient` | RFC 5322 | `recipients_round_trip` |
| B-COMP-004 | Quoted inline images become `cid:` references in the HTML, with an inline part carrying the blob id. | compose.rs:1185 | RFC 2392 | `quoted_inline_parts_use_cid_html` |
| B-COMP-005 | Reply identity: an exact address match first (plus-tags are stripped: `me+shop@example.org` matches `me@example.org`), then the same domain in domain mode. A match is an identity index (with an optional name). No match is `None`. | compose.rs:1202 `resolve_reply_identity` | Bulwark | `reply_identity_order` |
| B-COMP-006 | Delivered-To headers are read in order from four names: `delivered-to`, `x-original-to`, `x-delivered-to`, `x-original-delivered-to`. | compose.rs:192, :1255 `delivery_addresses` | | `delivered_to_headers` |
| B-COMP-007 | A reply window docks to the main window only when the setting, the view and the composer state all allow it. | compose.rs:1236 `should_dock` | | `reply_dock_choice` |
| B-COMP-008 | Placeholders are detected before send: `{{ name }}`, `[TIME]`, `[Your Name]`, `TODO`, `〇〇`, `[insert date here]`. Link text such as `[link](…)`, `[Yours truly]`, `[Anlage 2]`, `[A]` and `xxxl` are not placeholders. | compose.rs:1245 `placeholders` | | `placeholders` |

## Autosave (`autosave.rs`)

Bulwark mapping: `email-composer.tsx`.

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-COMP-009 | A draft is saved 2 s after the last change (`QUIET` = 2000 ms). The timer ticks every 500 ms. | autosave.rs:12-13 | Bulwark | `debounce_rules` |
| B-COMP-010 | A draft is saved only when the user changed something. A fresh composer, the quote, and the signature a reply starts with are not reasons to keep a draft. The content must differ from the last save. | autosave.rs:1-5, :187 `due` | Bulwark | `debounce_rules` |
| B-COMP-011 | A save or send never runs while another is in flight. Changes queue behind it, so two saves cannot both replace the same draft. | autosave.rs:1-5, :204 generations | | `generations` |
| B-COMP-012 | A finished save returns `Send` when a send was queued. A save whose generation is stale is ignored. | autosave.rs:204 `finish` | | `generations` |
| B-COMP-013 | Reopening a draft while a save is in flight keeps the queued save for the right generation (`Leftover::Save`). | autosave.rs:219 `reset_leftover` | | `reopen_during_a_save_keeps_the_queued_work` |

## Scheduled send (`schedule.rs`)

Bulwark mapping: `lib/schedule-presets.ts`, `schedule-send-dialog.tsx`, `scheduled-send-label.ts`, `message-actions.ts`. GAPS P0-2.

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-COMP-014 | The hold is sent as `HOLDFOR` (seconds left, computed just before the request), not `HOLDUNTIL`. Every FUTURERELEASE server takes HOLDFOR. | schedule.rs:5-8 | Bulwark `rescheduleEmailSubmission`; RFC 4865 | none (live: Scheduled view) |
| B-COMP-015 | Presets are local times: "This morning", "This afternoon", "Tomorrow morning", "Tomorrow afternoon" and so on. The first "today" preset must be at least `TODAY_LEAD` (300 s) away. | schedule.rs:17, :252 `preset_times` | Bulwark `schedule-presets.ts` | `preset_times` |
| B-COMP-016 | Date and time parse from ISO (`2026-11-17`), dotted (`17.11.2026`) and 12-hour (`8:30 pm`). | schedule.rs:268 `parse_local` | | `parsing_and_checks` |
| B-COMP-017 | The label shows the day, date and time (`Tue 17 Nov, 08:00`), and the 12-hour form when the setting asks for it (`1:00 PM`). | schedule.rs:285 `label` | Bulwark `scheduled-send-label.ts` | `labels` |
| B-COMP-018 | Reschedule and cancel-and-edit of a scheduled send use the submission's undo path and the restore-to-Drafts fallback (B-JMAP-038, B-JMAP-039). | schedule.rs:1-3 | Bulwark `message-actions.ts` | `reschedule_fallback_order` (jmap) |

## Recipients (`recipients.rs`, `chips.rs`)

Bulwark mapping: `lib/email-composer-utils.ts`, `lib/rfc5322-mailbox.ts`, `lib/idn.ts`, `lib/validation.ts`, `stores/contact-store.ts`.

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-COMP-019 | The field text (`c-to`, `c-cc`, `c-bcc`) is the source of truth. A comma list in which a contact group is written in RFC 5322 group syntax. Chips are rebuilt from the text whenever it changes; edits write back to it. | chips.rs:1-5 | Bulwark `RecipientInput` | none |
| B-COMP-020 | Blurring a field commits a full address, but keeps partial text while suggestions are open. | chips.rs:438 `blur_commits` | | `blur_keeps_partial_text_while_suggestions_are_open` |
| B-COMP-021 | Split on commas and semicolons, honouring quoted names and group syntax (`Team: a@example.org, b@example.net;`). | recipients.rs:466 `split_recipients` | RFC 5322 | `split_and_parse` |
| B-COMP-022 | A mailbox string splits into name and address, with or without a space before `<`. A name is sanitised. | recipients.rs:457 `split_mailbox` | RFC 5322 | `mailbox_split` |
| B-COMP-023 | A pasted list splits into addresses and names. A quoted name with a comma (`"Doe, J"`) stays one name. | recipients.rs:495 `paste` | | `paste` |
| B-COMP-024 | Group recipients expand into their members. Invalid addresses are reported, not dropped. | recipients.rs:486 `expand_recipients`, `invalid_addresses` | Bulwark | `expansion_and_invalid` |
| B-COMP-025 | Email validation accepts plus-tags and internationalised domains. | recipients.rs:447 `is_valid_email` | Bulwark `validation.ts` | `validation` |
| B-COMP-026 | Domains go out in ASCII (punycode, `bücher.de` becomes `xn--bcher-kva.de`). They show in Unicode only when they decode back to the same ASCII form. Local parts keep their case. | recipients.rs:436 `to_ascii_email` | Bulwark `idn.ts` | `idn_round_trip` |
| B-COMP-027 | Suggestions: contact cards become sources, groups come first, and recent recipients come from Sent, de-duplicated by address and keeping the first name. | chips.rs:449, :464 | Bulwark `loadRecentRecipients` | `cards_to_sources`, `recent_dedup` |
| B-COMP-028 | At most 10 suggestions (`MAX_SUGGESTIONS`). Ranking puts groups first, then by name, with a stable order. | recipients.rs:35, :504 | Bulwark `getAutocomplete` (`matchRank`) | `ranking` |
| B-COMP-029 | The default suggestion is the index of the matching address, or -1 when none matches. | recipients.rs:521 `default_suggestion_index` | Bulwark `defaultSuggestionIndex` | `default_pick` |
| B-COMP-030 | A remote (server) query is needed for contact text, and for `is:unread` in mail. It is not needed for empty input or for `is:unread` in contacts. | gsearch.rs:712 (search area) | | `remote_query_needed` (gsearch) |

## Mailto links (`mailto.rs`, `instance.rs`)

Bulwark mapping: `lib/protocol-handlers/mailto.ts`. GAPS P0-8. Registration is checked by plan only in tests.

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-COMP-031 | A mailto link parses with RFC 6068: lenient percent-decoding (`+` stays `+`), quote-aware recipient splitting, control characters stripped. Fragments (`#…`) are ignored. | mailto.rs:1-3, :395 `parse_rfc6068`, :449 | RFC 6068 | `parse_rfc6068`, `fragments_and_pending` |
| B-COMP-032 | Limits: 200 recipients (`MAX_RECIPIENTS`), a 998-character subject (`MAX_SUBJECT`), a 64 KiB body (`MAX_BODY`). Extra recipients are dropped. | mailto.rs:23-25, :412 | RFC 5322 line limit | `limits` |
| B-COMP-033 | Up to 5 pending links are kept until the app is ready (`MAX_PENDING`). | mailto.rs:110, :449 | | `fragments_and_pending` |
| B-COMP-034 | A mailto link clicked in a message opens Barbican's composer, not the OS handler. A link handed over by the OS arrives through `instance.rs` and opens the composer the same way. | mailto.rs:1-6 | | none |
| B-COMP-035 | Registration as the default mail app: Windows uses HKCU (`Barbican.Url.mailto`), triggered by the user in Settings; Windows then asks the user to confirm in Default apps. Linux uses a `.desktop` file with `MimeType=x-scheme-handler/mailto`. Tests check only the plan. | mailto.rs:7-9, :176 `PROG_ID`, :420 | Windows Default apps | `registration_plans` |
| B-COMP-036 | A moved executable is detected: the registered command no longer matches the running path. | mailto.rs:441 `command_matches` | | `moved_exe_is_detected` |
| B-COMP-037 | A second launch hands its mailto to the running instance over loopback, and only when the token and greeting match. A server that does not greet correctly gets nothing. | instance.rs:146-187 | security | `args_and_requests`, `handoff_over_loopback`, `foreign_program_on_a_stale_port_gets_nothing` |

## Templates (`templates.rs`)

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-COMP-038 | Deleting a template writes a tombstone. The tombstone map is versioned (version 1) and keeps other deletions. | templates.rs:91 | JMAP `ifInState` (B-JMAP-029) | `tombstone_keeps_other_deletions` |

## Rich-text editor (`rte/`)

Bulwark mapping: none (native). The model, the editing rules and the HTML subset follow the Rust composer. Layout is in `layout.rs` (no tests).

### Document model (`rte/model.rs`)

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-COMP-039 | A document is a flat list of blocks, each with styled text runs. A position is (block index, byte offset). | rte/model.rs:1-2 | | `insert_and_merge_runs` |
| B-COMP-040 | Inserting text merges adjacent runs with equal marks. | rte/model.rs:524 | | `insert_and_merge_runs` |
| B-COMP-041 | A table is stored as consecutive `Cell` blocks (row-major) sharing a table id. Every editable position stays addressable the same way. | rte/model.rs:3-4 | | `delete_keeps_table_shape` |
| B-COMP-042 | Splitting and merging blocks keep text in the right blocks and return the new position. | rte/model.rs:536 | | `split_and_merge_blocks` |
| B-COMP-043 | Enter at the end of a heading creates a paragraph. | rte/model.rs:549 | | `heading_enter_at_end_gives_paragraph` |
| B-COMP-044 | Ordered-list numbers count per level. | rte/model.rs:556 `ordinal` | | `ordinal_counts_levels` |
| B-COMP-045 | Pasting several blocks splits the caret block and merges the ends. | rte/model.rs:567 | | `paste_multi_block` |
| B-COMP-046 | The marks at a caret come from the run before it. | rte/model.rs:590 `marks_at` | | `marks_at_caret` |

### Editing commands (`rte/editor.rs`)

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-COMP-047 | Typing is grouped for undo. Undo and redo work on the history (300 steps, `HISTORY`). | rte/editor.rs:29, :724 | | `typing_and_undo_groups` |
| B-COMP-048 | Markdown block shortcut: `# ` turns a paragraph into a heading. | rte/editor.rs:742 | | `markdown_block_rules` |
| B-COMP-049 | Markdown inline shortcut: `**bold**` applies bold and removes the markers. | rte/editor.rs:763 | | `markdown_inline_rules` |
| B-COMP-050 | Toggling bold applies to the selection, and to the caret for the next typing. | rte/editor.rs:775 | | `toggle_marks_selection_and_caret` |
| B-COMP-051 | List items indent; Backspace outdents. | rte/editor.rs:790 | | `lists_indent_and_backspace` |
| B-COMP-052 | A link adds a link mark to its run. The current link is read back at the caret. | rte/editor.rs:807 | | `links` |
| B-COMP-053 | Tab moves to the next cell in a table. | rte/editor.rs:819, :824 `cell_tab` | | `tables` |
| B-COMP-054 | Inserting an image keeps the following text (quoted text stays after the image). Replacing the draft keeps the quote. | rte/editor.rs:835, :849 | | `image_insert_keeps_following_text`, `replace_keeps_quote` |
| B-COMP-055 | Images resize with `resize_image`. The boolean flag selects aspect-ratio locking (to confirm the flag's meaning in the source). | rte/editor.rs:861 | | `image_resize_and_replace_range` |

### HTML in and out (`rte/html.rs`)

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-COMP-056 | HTML import is a tolerant subset parser for replies, drafts, templates, signatures and pasted HTML. `strong` and `em` become bold and italic runs. | rte/html.rs:1-2, :762 | | `import_inline_marks` |
| B-COMP-057 | Headings, paragraphs, bullet and numbered lists, and quotes import as blocks. Table cells import with header detection. | rte/html.rs:773, :782 | | `import_blocks_and_lists`, `import_table` |
| B-COMP-058 | Whitespace collapses and HTML entities decode (`&amp;`). | rte/html.rs:791 | | `import_whitespace_and_entities` |
| B-COMP-059 | HTML export is email-safe with inline styles. Text is escaped (`&lt; &gt; &amp;` and `&nbsp;`). Links use `#2563eb`. Code uses a monospace stack. The wrapper uses Segoe UI 14 px, line height 1.5. | rte/html.rs:558-559, :817, :920 | | `export_escapes`, `round_trip` |
| B-COMP-060 | Plain-text export writes links as `<url>`, bullet items as `- `, numbered items as `1. `, and quotes with `> `. | rte/html.rs:811 `to_text` | | `plain_text_output` |
| B-COMP-061 | A quoted reply round-trips, and the attribution line stays. Cleaning removes the quoted tail and leaves the reply text. | rte/html.rs:901, :913 | | `quoted_round_trip`, `clean_quote` |
| B-COMP-062 | The HTML source view has one block per line, and keeps the quote out of the source view. Editing the source keeps inline image `cid:` references. | rte/html.rs:997, :1024 | | `source_round_trip`, `source_edit_keeps_inline_image_data` |
| B-COMP-063 | An image's width survives export, as the `width` attribute and as `width:…px` in the style. | rte/html.rs:1044 | | `image_width_round_trip` |

### Spell checking (`rte/spell.rs`)

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-COMP-064 | The spell language comes from settings: `de` maps to `de-DE`; `auto` uses the system tag. | rte/spell.rs:190 `language_tag` | | `tags` |
| B-COMP-065 | Results are cached per text, so only changed paragraphs are checked again while typing. | rte/spell.rs:1-3 | | none |
| B-COMP-066 | URLs and e-mail addresses are skipped. | rte/spell.rs:206 `skip_word` | | `skips` |
| B-COMP-067 | UTF-16 range offsets from the checker convert to byte ranges (emoji count as two units). | rte/spell.rs:197 | | `utf16_ranges` |
| B-COMP-068 | The Windows checker is `ISpellChecker` (the engine Edge and WebView2 use). It is a Windows-only test. Linux uses Hunspell (M9). | rte/spell.rs:1, :216 | | `windows_checker` |

### Fonts (`rte/layout.rs`)

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-COMP-069 | Body text uses Segoe UI and code uses Consolas. These are Windows fonts; the Linux port needs a substitute and a visual check. | rte/layout.rs:15-16 | | none |

## Open questions

- B-COMP-055: the meaning of the boolean flag in `resize_image` is not confirmed.
- B-COMP-069: font substitutes for Linux (Uno) are not chosen. Recorded for M9.
- B-COMP-015: the exact use of `TODAY_LEAD` is inferred from its name. Confirm in the source before porting.
- B-COMP-030 is a restatement of the gsearch area; it is kept here because the composer calls it.
