# Behaviours: rich-text editor (B-RTE)

Area: the native rich-text composer: document model, editing commands and input rules, undo,
HTML in and out, source view, layout and painting, spell checking, composer-level behaviours
that come from the editor (plain-text mode, links, images, quotes).
Rust sources (`rust-final`): `desktop/src/rte/model.rs`, `editor.rs`, `html.rs`, `layout.rs`,
`mod.rs`, `spell.rs` (and `platform/`). Bulwark refs: `lib/email-composer-utils.ts`,
`components/email/email-composer.tsx`, `lib/rte` (TipTap behaviour; PORT-PLAN §3.8).
Layout engine in Rust is parley (the Slint text engine). The C# port uses RichTextKit (spike S3).

| ID | Rule | Source (Rust) | Bulwark ref | C# status |
|---|---|---|---|---|
| B-RTE-001 | The document is a flat list of blocks; each block holds styled text runs. A position is (block index, byte offset). | rte/model.rs header (lines 1-2) | n/a | pending (M3) |
| B-RTE-002 | Tables are consecutive Cell blocks, row-major, sharing a table id, so every editable position is addressed the same way. | model.rs header (lines 3-4); test `delete_keeps_table_shape` (:577) | n/a | pending (M3) |
| B-RTE-003 | Model tests: runs merge on insert; blocks split and merge; a heading Enter at the end makes a paragraph; list ordinals count by level; a multi-block paste; marks at the caret. | model.rs tests `insert_and_merge_runs` (:524), `split_and_merge_blocks` (:536), `heading_enter_at_end_gives_paragraph` (:549), `ordinal_counts_levels` (:556), `paste_multi_block` (:567), `marks_at_caret` (:590) | n/a | pending (M3) |
| B-RTE-004 | Undo history is bounded at 300 steps. | rte/editor.rs:29 `HISTORY` | n/a | pending (M3) |
| B-RTE-005 | Typing groups into one undo step. | editor.rs test `typing_and_undo_groups` (:724) | n/a | pending (M3) |
| B-RTE-006 | Markdown-style input rules at block start (headings, lists, quotes, rules) and inline (bold, italic, code). | editor.rs tests `markdown_block_rules` (:742), `markdown_inline_rules` (:763) | Bulwark TipTap input rules | pending (M3) |
| B-RTE-007 | Toggling a mark applies to the selection, or to the caret for the next typed text. | editor.rs test `toggle_marks_selection_and_caret` (:775) | editor toolbar | pending (M3) |
| B-RTE-008 | Lists: indent and outdent; backspace at the start of an item leaves the list. | editor.rs test `lists_indent_and_backspace` (:790) | email-composer.tsx | pending (M3) |
| B-RTE-009 | Links: insert, edit and remove through the link popover; Esc in the popover cancels only the popover. | editor.rs test `links` (:807); commit 46198f8; 20ae864 | link popover | pending (M3) |
| B-RTE-010 | Tables: insert and edit cells. | editor.rs test `tables` (:819) | table commands | pending (M3) |
| B-RTE-011 | An image insert keeps a paragraph after it, so following text stays after the image. | editor.rs test `image_insert_keeps_following_text` (:835); commit baa6a3e | n/a | pending (M3) |
| B-RTE-012 | Image resize and replace work on the range; widths survive an HTML round trip. | editor.rs test `image_resize_and_replace_range` (:861); html.rs test `image_width_round_trip` (:1044); commit 68a8c94 | image resize handles | pending (M3) |
| B-RTE-013 | Replace keeps the quote block. | editor.rs test `replace_keeps_quote` (:849) | quote handling | pending (M3) |
| B-RTE-014 | HTML in is a tolerant subset parser (replies, drafts, templates, signatures, pasted HTML). | html.rs header (lines 1-2); tests `import_inline_marks` (:762), `import_blocks_and_lists` (:773), `import_table` (:782), `import_whitespace_and_entities` (:791) | n/a | pending (M3) |
| B-RTE-015 | HTML out is email-safe: inline styles only. Link colour is #2563eb. Code is Consolas, Courier New, monospace on #f1f5f9. | html.rs header; :558 `LINK_COLOR`; :559 `CODE_STYLE` | n/a | pending (M3) |
| B-RTE-016 | Export spacing follows Bulwark: paragraphs 4 px, headings, lists, framed code blocks, quote bar. Single spaces between runs are kept. | commits 33e4d03, a9aef40; html.rs:920 `WRAP_OPEN` (font stack) | email-composer.tsx spacing | pending (M3) |
| B-RTE-017 | Plain-text output (the text/plain part) is derived from the same model; export escapes HTML. | html.rs test `plain_text_output` (:811), `export_escapes` (:817) | n/a | pending (M3) |
| B-RTE-018 | Quoted originals: a quote round trips; a quote is cleaned on import. | html.rs tests `quoted_round_trip` (:901), `clean_quote` (:913) | n/a | pending (M3) |
| B-RTE-019 | Source view round-trips the HTML and keeps inline image data; a source edit keeps inline image data; the source view resets on a new message and follows toolbar and AI changes. | html.rs tests `source_round_trip` (:997), `source_edit_keeps_inline_image_data` (:1024); commit 091ec6a | source view | pending (M3) |
| B-RTE-020 | Plain-text mode: switching converts the body to plain paragraphs, hides the formatting UI, ignores formatting commands and shortcuts (Ctrl+B and markdown rules included), and pastes as plain text. | commit 46198f8 | plain text composer | pending (M3) |
| B-RTE-021 | Re-opened drafts keep inline images: cid restored from the reader's data: URIs. | commit b9b3e99 | draft reopen | pending (M3) |
| B-RTE-022 | Reply and forward resend quoted inline images as cid parts (inline, not attachments). | commit 20ae864 | forward/reply | pending (M3) |
| B-RTE-023 | Recipient text in a reply or a draft shows as a bare address when the name equals it; a name is quoted only when it needs quoting. | commit 20ae864 | email-composer-utils.ts | pending (M3) |
| B-RTE-024 | Dropped files: images go inline, other files are attached. | commit baa6a3e | composer drop | pending (M3) |
| B-RTE-025 | Spell checking uses the Windows checker with suggestions, add and ignore; it is cached per text, so only changed paragraphs are checked. | commit 68a8c94; rte/spell.rs header | spell check | pending (M3); see B-PLAT-008 |
| B-RTE-026 | IME pre-edit is shown in a TextInput at the caret. The pre-edit covers the text after the caret instead of pushing it aside. | commit 68a8c94; GAPS "Rich-text composer leftovers" (roadmap) | IME | open: G-091; hand test HT3 |
| B-RTE-027 | Composer right-click menu: clipboard, format, paragraph, link, table, image size, AI rewrite, undo. | commit 68a8c94 | context menu (cat-a menus) | pending (M3) |
| B-RTE-028 | The AI rewrite runs one at a time; the rewrite is on the selection. | commit 091ec6a | assistant | pending (M3/M6) |
| B-RTE-029 | Esc dismisses the selection first, before any other composer action. | commit e3dd7e7 | composer | pending (M3) |
| B-RTE-030 | Rich signatures and templates are inserted through the editor model. | commit 1c9ddec | signatures, templates | pending (M3) |
| B-RTE-031 | Collapsible quoted originals; the selection toolbar sits near the selection. | commits 1c9ddec, 33e4d03 | email-composer.tsx | pending (M3) |
| B-RTE-032 | Layout uses parley with the system font collection. Body font is "Segoe UI"; monospace is "Consolas". | rte/layout.rs:15 `BODY_FONT`, :16 `MONO_FONT` | Bulwark body font | pending (M3) |
| B-RTE-033 | Layout cursor movement that needs geometry (up and down by line) lives in layout, not in the model. | rte/editor.rs header (lines 1-2) | n/a | pending (M3) |
| B-RTE-034 | Text in the editor must match the XAML text under the S3 gate. | PORT-PLAN M3 exit | n/a | pending (M3) |
