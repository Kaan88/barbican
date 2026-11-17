# Behaviours: command palette and search everything (B-PAL)

Area: the Ctrl+K command palette, the global "search everything" palette, query syntax,
search history and suggestions, search snippets.
Rust sources (`rust-final`): `desktop/src/palette.rs` (commands, matching, rows, icons; tests
:624-673), `search_ui.rs`, `search_history.rs`, `gsearch.rs`, `shortcuts.rs` (Ctrl+K entry).
Bulwark refs: `components/command-palette/command-palette.tsx`, `lib/command-registry.ts`,
`lib/app-commands.ts`, `components/global-search/global-search-palette.tsx`, `lib/query-parser.ts`.

| ID | Rule | Source (Rust) | Bulwark ref | C# status |
|---|---|---|---|---|
| B-PAL-001 | The palette lists the open app's commands first, then Go to, Actions, Accessibility and Account. | palette.rs header (lines 1-7); test `app_commands_come_first` (:632) | command-palette.tsx | ported (m2 menus: PaletteModel, PaletteController; PaletteModelTests, PaletteControllerTests) |
| B-PAL-002 | A query matches when every word appears in the label or in the keywords. | palette.rs:61 `command_matches`; test `matches_every_word` (:624) | command-registry.ts | ported (m2 menus: PaletteModel, PaletteController; PaletteModelTests, PaletteControllerTests) |
| B-PAL-003 | Commands that need an open message are hidden when no message is open. | palette.rs:92 `commands`; test `mail_commands_need_an_open_message` (:647) | app-commands.ts | ported (m2 menus: PaletteModel, PaletteController; PaletteModelTests, PaletteControllerTests) |
| B-PAL-004 | Account commands and capability-gated commands follow the signed-in accounts and their capabilities. | palette.rs test `accounts_and_capabilities` (:661) | Account group | ported (m2 menus: PaletteModel, PaletteController; PaletteModelTests, PaletteControllerTests) |
| B-PAL-005 | One command per settings page is listed. | palette.rs test `settings_pages` (:673) | Go to group | ported (m2 menus: PaletteModel, PaletteController; PaletteModelTests, PaletteControllerTests) |
| B-PAL-006 | "Search everything" runs one query over mail, contacts, calendar and files. Scope chips choose which kinds; five rows per kind; Enter opens the hit where it lives. | palette.rs header (lines 6-7); commit 8253677 | global-search-palette.tsx | pending (M2) |
| B-PAL-007 | The default search scope follows searchDefaultScope. | commit 045855a | settings | pending (M2) |
| B-PAL-008 | Review fix for the global-search folder choice (commit d16233d; see decisions.md). | commit d16233d | global-search-palette.tsx | pending (M2) |
| B-PAL-009 | Query syntax: `from:`, `to:`, `subject:`, `has:attachment`, `is:`, `before:`, `after:`, `in:`, `account:`; quoted phrases; `word*` prefixes. Split as Bulwark's splitSearchText. | commit 8253677 (query-parser) | query-parser.ts | pending (M2) |
| B-PAL-010 | Each operator and each filter-panel field appears as a removable chip in the search box. | commit 8253677 | search box | pending (M2) |
| B-PAL-011 | Search filters: from, to, subject, body, dates, size, unread, starred, attachment. | commit 045855a | search filters | pending (M2) |
| B-PAL-012 | Search history keeps the ten most recent searches, newest first. | commit 8253677 (search_history.rs) | recent searches | pending (M2) |
| B-PAL-013 | Suggestions under the search box: recent searches, then people from contacts and the loaded senders. Up, Down and Esc keep working in the suggestions. | commits 8253677, bff0eda | suggestions | pending (M2) |
| B-PAL-014 | The palette field takes the arrow keys before the text edit; key caps are single glyphs. | commit bff0eda | palette | ported (m2 menus: PaletteModel, PaletteController; PaletteModelTests, PaletteControllerTests) |
| B-PAL-015 | Matched words of search hits are highlighted using SearchSnippet/get (RFC 8621 5.1). | commit 8253677; gsearch.rs | search snippets | pending (M2) |
| B-PAL-016 | Highlights already on screen do not recolour when the theme changes; they update when the list is rebuilt. | PARITY-ROUND2 "Productivity partial" | n/a | open: L-050 |
| B-PAL-017 | Palette icons are fixed SVG path strings for each command group (mail, calendar, people, folder, settings, admin, compose, search, keys, sun). | palette.rs:16-25 (`I_MAIL` ... `I_SUN`) | n/a | ported (m2 menus: PaletteModel, PaletteController; PaletteModelTests, PaletteControllerTests) |
| B-PAL-018 | Open and close are explicit actions: `open(mode)` and `close`. | palette.rs:246 `open`, :261 `close` | n/a | ported (m2 menus: PaletteModel, PaletteController; PaletteModelTests, PaletteControllerTests) |
| B-PAL-019 | Enter runs the active row; `run_command` dispatches a command id. | palette.rs:437 `enter`, :452 `run_row`, :485 `run_command` | n/a | ported (m2 menus: PaletteModel, PaletteController; PaletteModelTests, PaletteControllerTests) |
| B-PAL-020 | Ctrl+K from inside the composer is passed through (not claimed). | shortcuts.rs test `palette_and_help_everywhere` (:724) | composer | pending (M3) |
| B-PAL-021 | UI-level tests for the palette and search-box key paths are not written in Rust. | PARITY-ROUND2 "Not done" (productivity) | n/a | open: test-plan item for M2 |
