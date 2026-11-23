# Behaviours: contacts (B-CON)

Area: contacts list, cards, editor, groups, categories, address books, vCard import and export,
duplicates and merge, photos, server directory.
Rust sources (`rust-final`): `desktop/src/contacts.rs`, `contacts_x.rs`, `vcard.rs`,
`contact_merge.rs`, `glance.rs` (contact tiles). Rows come from headers, constants, tests and
commits. Line numbers are in the Rust tree at `rust-final`.

Security note: the vCard parser and photo handling read untrusted files. Their own review is
still open (PARITY-ROUND2 "contacts commit has not had its own security review"): G-012.

| ID | Rule | Source (Rust) | Bulwark ref | C# status |
|---|---|---|---|---|
| B-CON-001 | Contacts come from JMAP AddressBook and ContactCard (JSContact, RFC 9553). The list is grouped by letter, with search, a detail pane, create, edit and delete. | contacts.rs header; capability `urn:ietf:params:jmap:contacts` (contacts_x.rs:15) | components/contacts | ported |
| B-CON-002 | A write-an-email action from a card opens the composer to that contact. | contacts.rs header | contact-detail.tsx | ported (M5: contacts.send-email opens the composer with the contact address; ContactsActionsTests) |
| B-CON-003 | Before a default address book is created, the server is checked for an existing one; no duplicate "Contacts" book is made. | commit 4f2d73b; b0dd3bd (default book on first contact) | contact-store | ported |
| B-CON-004 | List sorts by last name when sortContactsByLastName is on (applied live). | commit a90b362 (Bulwark getContactSortName) | contact-list.tsx | ported (M5: ContactsViewModel applies the setting live; the settings row is M6) |
| B-CON-005 | Groups are JSContact kind=group with members referenced by uid. Opening a group shows its members; "Email group" writes to all of them. | commit e75e107 | contact groups | ported |
| B-CON-006 | Group membership is keyed by uid or id (whichever the card has); categories are sent as the whole keywords map. | commit 4d24260 | contact-store | ported |
| B-CON-007 | Contact contexts: Personal, Work, Other. | contacts_x.rs:59 `CONTEXTS` | contact-form.tsx | ported |
| B-CON-008 | Phone features: voice, mobile, fax, pager, text, video (plus cell) and phone contexts Any, Personal, Work. | contacts_x.rs:60 `PHONE_FEATURES`, :61 `PHONE_CTX` | contact-form.tsx | ported |
| B-CON-009 | Dated entries have kinds birth (Birthday), wedding (Anniversary), death (Date of death), other (Other date). | contacts_x.rs:62 `DATE_KINDS` | contact-form.tsx | ported |
| B-CON-010 | Related people use 13 fixed relation kinds (contact, spouse, sweetheart, child, parent, ...). | contacts_x.rs:63 `RELATIONS` | contact-form.tsx | ported |
| B-CON-011 | Unknown entry properties on a card survive a load-save round trip. | contacts_x.rs test `fields_round_trip_keeps_unknown_properties` (:1056); commit 6170ed5 | contact-form.tsx | ported |
| B-CON-012 | Edits add and remove entries and are validated before save. | contacts_x.rs test `edits_add_remove_and_validate` (:1084) | contact-form.tsx | ported |
| B-CON-013 | Dates show in pretty form; "upcoming dates" are computed for the card. | contacts_x.rs test `pretty_dates_and_upcoming` (:1118) | contact-detail.tsx | ported |
| B-CON-014 | Photos: picked and removed through a blob upload, with a data: URI fallback; blob photos are inlined in exports. | commit 6170ed5 | contact-photo | ported; limits G-012 (D-M5-03, D-M5-A03) |
| B-CON-015 | Photos are shrunk to JPEG before storing. | contacts_x.rs test `shrinks_photo_to_jpeg` (:1153) | contact-photo | ported |
| B-CON-016 | Import: batch import with a target address book; duplicates are skipped; principals are read in the same pass. | contacts_x.rs test `import_batch_and_principals` (:1136); commit 6170ed5 | contact-import-dialog.tsx | ported |
| B-CON-017 | Export writes the whole list or one card (vCard 4.0). | commit 6170ed5 | contact-export | ported |
| B-CON-018 | vCard reader accepts 2.1, 3.0 and 4.0. It keeps types, labels (incl. Apple grouped X-ABLabel), PREF, quoted-printable, PartialDate, photos as data: URIs, related people and X- IM properties. | vcard.rs header; test `parses_vcard3_with_types_and_apple_labels` (:684) | lib/vcard.ts | ported |
| B-CON-019 | Reduced date forms (year only, month-day) parse as PartialDate. | vcard.rs test `dates_reduced_forms` (:720) | lib/vcard.ts | ported |
| B-CON-020 | vCard export writes 4.0 with folded CRLF lines; a round trip is stable. | vcard.rs header; test `round_trip_and_folding` (:734) | lib/vcard.ts | ported |
| B-CON-021 | Quoted-printable is decoded; empty entries are skipped. | vcard.rs test `quoted_printable_and_skips_empty` (:762) | lib/vcard.ts | ported |
| B-CON-022 | Duplicates inside the file and against existing cards are both detected on import. | vcard.rs test `duplicates_in_file_and_existing` (:771) | contact-import-dialog.tsx | ported |
| B-CON-023 | Duplicate groups by name, email and phone. | contact_merge.rs test `finds_groups_by_name_email_phone` (:234) | lib/contacts/merge.ts | ported |
| B-CON-024 | Merge keeps the most recently changed card; it gains every entry the others have and it lacks; single fields are taken only when it has none; the other cards are deleted; their group memberships move to the kept card. | contact_merge.rs:1-4; test `merge_keeps_newest_and_adds_missing` (:250); MULTI (:66), SINGLE (:68) | lib/contacts/merge.ts | ported |
| B-CON-025 | Server directory: Principal/query with a Principal/get fallback; principal properties are fixed (7). | contacts_x.rs:847 `PRINCIPALS`, :848 `PRINCIPAL_PROPS`; commit 6170ed5 | directory-browser.tsx | ported |
| B-CON-026 | Contact tiles (Bento and Nordic) show recent mail, files and the next event from the account; drafts are excluded. | commit 02ab6c7 (glance::contact) | contact detail | deferred(M10): the activity tiles need read access to the mail and file stores for one contact; the detail pane shows the card only |
| B-CON-027 | Contact avatars without a photo show initials. | commit a1cb1d6 | avatar | ported (M5: ContactsListRules.Initials, ContactsAvatar) |
| B-CON-028 | Settings > Contacts has Import and Export rows. They were dropped by the Rust schema reader (keyless rows). | commit 6170ed5; GAPS cross-cutting finding 1 | settings contacts tab | pending (M6); G-069 |
| B-CON-029 | Address book and group management pages in settings, with row context menus. | commit efb754b | settings | pending (M6) |
| B-CON-030 | Contact list and card are restyled to the approved Bento and Nordic designs (people list tile with letter headers, squircle or circle avatars). | commit 02ab6c7 | design/mockups | ported (M5: letter headers, squircle or circle avatars, Bento tiles and the Nordic surface; docs/milestones/M5-screens) |
| B-CON-031 | Contacts list keys: arrows move, Home and End go to the ends, Enter opens, Delete or Backspace deletes, Esc clears, Shift+F10 opens the menu, / searches. | shortcuts.rs:204 `contacts`; test `list_keys` (:796) | lib/keyboard-shortcuts (contact-list.tsx) | ported (M5: contacts keys through ShortcutTables and the contacts.key-* commands; ContactsViewModelTests) |
