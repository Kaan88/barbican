# M5 plan: contacts and files

- Written 2026-11-18 by the M5 plan stage (PORT-PLAN.md §6.1 step 1), on branch `m5-plan`, taken from `m4` (finished M4, 7d1454e).
- Scope and exit: [PORT-PLAN.md](../PORT-PLAN.md) §5 "M5" (line 774), §3.4 (push types), Appendix A/B/C rows for M5; the model for this plan's shape:
  [M4-plan.md](M4-plan.md) (and [M3-plan.md](M3-plan.md)).
- Integration branch: **`m5`**, created from `m5-plan` once this plan is pushed (the plan commit carries the contracts, the FakeJmap contacts arms and the red
  tests, so `m5` starts with them). `m4` is merged into `m5` by the integrator whenever `m4` changes (never a rebase, never a force-push). Nothing merges to
  `main` and nothing is tagged until the user says so.
- Accounts and paths are placeholders (CLAUDE.md, `.local/accounts.md`): `<readonly-account>`, `<test-account>`, `<server>`, `<rust-repo>`, `<bulwark>`,
  `<stalwart-zip>`, `<downloads>`.
- **Process (user decision 2026-11-18):** no review stages, no hand-test gates, no questions back to the user. Every open decision is taken with the default
  that follows Bulwark and is listed in §9 and in `spec/decisions.md` ("M5 decisions": `D-M5-01..19` for the plan, `D-M5-A01..` (con-data), `D-M5-B01..` (con-ui),
  `D-M5-C01..` (files-data), `D-M5-D01..` (files-ui) for the streams). What only a human can do (a real-pointer drag of a contact or a file, printing on paper,
  an OS file drop from Explorer) is written down as "unproven, needs a human" in the milestone report and never blocks a merge. Agents are Sonnet (Haiku for
  mechanical work); each brief in §6 is written so one Sonnet agent can do it alone.

## 0. Summary

| Key (branch `m5-<key>`) | Letter | Scope | Model | T rows | Red tests | Wave |
|---|---|---|---|---|---|---|
| `con-data` | A | vCard reader and writer (hostile-input limits, G-012), JSContact form rows (typed entries, unknown properties kept), dates, photos (shrink, caps), duplicates and merge rules, import batches, address books (the "existing book" check), the contact store (AddressBook and ContactCard load in chunks, shared accounts, push and /changes), the writer (create, patch, delete, move, groups, merge, import in chunks), the directory (principals), the lookup for mail (recipient suggestions, View contact), the FakeJmap contact set arm | Sonnet | 12 | 17 | 1 |
| `con-ui` | B | The Contacts app in the shell (the `IAppScreenFactory` seam, S0), sidebar (books, groups, categories, directory, duplicates), list, detail, form, group and merge and import and book-picker and directory dialogs, bulk bar, menus, keys, print, contact activity, the mail-side surfaces (reader popover, address menu, contact sidebar, composer directory picker), commands, palette, hooks | Sonnet | 0 | 3 | 1 |
| `files-data` | C | FileNode shapes (0.16 and draft 18), the store (every account, shared nodes, push and /changes), favourites and recent (device-local), the Bulwark trash (`.Trash`, dated batches), writer (folder, rename, move, copy across accounts, duplicate, trash, restore, purge, share), transfers (upload files and folders with conflict policy, download, zip, new text file, read for preview), name rules of the server, download filename rules, FakeJmap FileNode set and changes | Sonnet | 2 | 4 | 1 |
| `files-ui` | D | The Files app in the shell (sidebar tree, Favourites, Recent, Shared, Trash), list and grid views, sort, search, thumbnails, details panel, in-app preview (image, text, eml; pdf and media open outside, D-M5-15), upload and conflict dialog, move dialog, new file dialog, menus, keys, undo toasts, drag and drop in-window, attachment preview in the reader, commands, hooks | Sonnet | 0 | 3 | 1 |

- **All four streams start together** (one wave): the contracts in §4 are compiled code with stubs and every stream works against interfaces first. §5 says what
  each stream needs from the others and when it merges into `m5`.
- **Limits held:** at most 3 agents compile at once (CLAUDE.md; `tools/build.ps1` holds `BARBICAN_BUILD_SLOTS` slots and queues the rest), at most 5 agents alive.
  con-data and files-data build Core and Presentation tests most of the time; con-ui and files-ui build the App head and queue on the slots. Linux is deferred to M9.
- **Branches `m5-<key>`**, worktrees `<downloads>/wt-m5-<key>`, push at creation and after every commit.
- **Ports and profiles** (hidden AUTOMATION runs, `BARBICAN_PROFILE=auto-<port>`): con-ui 8911, files-ui 8912, integrate/verify 8919. con-data and files-data have no UI
  and need none (their scenarios run in-process against FakeJmap).
- **M5 T rows:** 14 (`python tools/spec-check.py --milestone M5`; contacts_x 5, vcard 5, contact_merge 2, files 2). This plan commit ports all 14 as **red tests**
  and adds 13 red tests for behaviour that is not a Rust row (G-012 limits and hostile shapes, the photo cap, the default address book rule, both FileNode shapes,
  the server's name rules, the contact list rules, the file list and preview rules): **27 red in all** (`python tools/red-check.py --list`: con-data 17, con-ui 3,
  files-data 4, files-ui 3). Green tests in this commit: the FakeJmap contact arms (2) and the contact option lists (1).

## 1. Rules every M5 agent repeats

Everything in [M4-plan.md](M4-plan.md) §1 and [M3-plan.md](M3-plan.md) §1 still holds (CLAUDE.md, `.local/accounts.md`, hidden automation only, captures that are
uniform are invalid and never a pass, `<readonly-account>` strictly read-only, never read `<admin-key-file>`, builds only through `tools/build.ps1`, push every
commit, privacy check before every commit, Fable log, code rules, contract stubs throw `PortPendingException` and the owner removes `[Red]` in the same commit as
the body, never change a contract signature without a note under "Requests to other streams"). The additions and changes for M5:

1. **No reviews, no hand-test gates, no questions.** Choose the default (Bulwark wins), write it in your Fable section and in `spec/decisions.md` under your own
   prefix (`D-M5-<letter>NN`, letters in §3.3; check the file for duplicates before you append), move on.
2. **Unique names, decided now** (M4's merges collided on these; §3.3 is the rule): every type a stream adds carries its stream's prefix, every test helper has a
   stream-unique name, and no two streams put a type with the same name in parent and child namespaces. A name this plan did not assign and that another stream
   could also want is prefixed with the stream's.
3. **Live checks** run only in the integrate stage (§7), one agent at a time. `<readonly-account>` reads only. Everything that writes (L-031..L-036) runs on
   `<test-account>`, and every write is cleaned up (test cards, groups, address books, files and folders destroyed, the trash emptied of what the check made; the
   checks list what to remove). A stream writes its FakeJmap equivalent and its live script and runs nothing live itself. **A contact is never emailed.**
4. **Hostile input is a design input.** vCard files, photos, FileNode names and file bytes are untrusted: nothing reads or writes outside the path the user picked
   through the system picker, an exported name is sanitised, no input makes a parser loop or allocate more than a few times its limit (D-M5-03; L-034 is the
   unit-test form of the security review).
5. **JSContact stays `JsonNode`** (D-M5-04): an unknown property on a card, an entry or a group survives every read-modify-write; writes send JSON-pointer patches of
   the changed fields (categories and the other maps the server's card may lack are sent whole, B-CON-006). **FileNode is normalised once** into `FileEntry`.
6. **Time is injected** (`TimeProvider`; trash batch names use its UTC milliseconds).
7. **Bulwark and Stalwart references.** Every Core file starts with the Rust file:line, the Bulwark file, the RFC section and the Stalwart path it mirrors (`<bulwark>`,
   `<stalwart-zip>`: `crates/jmap/src/{addressbook,contact,file}/`, `crates/jmap-proto/src/object/{addressbook,contact,file_node}.rs`); the contract stubs name them,
   **you verify the lines**.
8. **Stay inside your owned files (§3).** A change you need elsewhere goes under `## Requests to other streams` in your Fable section; work around it behind your own
   interface; the integrator applies it.
9. **User decisions in force:** D-2, D-3, D-7, D-9, D-10 of M2-plan §1 item 14; D-1 (rewriting public history) is not approved; permanent deletes always ask (D-7).

## 2. What M5 must close

### 2.1 Rust tests (T-): 14 rows

| Stream | Rows | Test files |
|---|---|---|
| con-data (12) | T-067, T-068 `contact_merge::*` (groups by name, email and phone; merge keeps the newest); T-069..T-073 `contacts_x::*` (fields round trip, edits and validation, pretty dates, import batch and principals, photo shrink); T-306..T-310 `vcard::*` (v3 with Apple labels, reduced dates, round trip and folding, quoted-printable, duplicates) | `tests/Barbican.Core.Tests/Contacts/{ContactMergeTests,ContactFieldsTests,ContactImportTests,VCardParserTests,VCardWriterTests}.cs` |
| files-data (2) | T-091, T-092 `files::*` (trash batch names; trash listing and unique names) | `tests/Barbican.Core.Tests/Files/FileTrashTests.cs` |
| con-ui (0), files-ui (0) | no Rust rows; their red tests are not Rust rows (§0) | `tests/Barbican.Presentation.Tests/{Contacts/ContactsListRulesTests,Files/FilesListRulesTests}.cs` |

### 2.2 JMAP requirements (J-)

J-071 (AddressBook/get, ContactCard query/get/set, `onSuccessSetIsDefault`; `fixed` in Rust) gets its C# evidence: con-data's load and writes against FakeJmap, `cs_test` naming
them; the Stalwart 1.0 refusal of a `keywords/x` pointer patch on a card without keywords is a FakeJmap arm (B-CON-006). J-075 (FileNode: `query_all` and chunked get, client-side
parent filter, `@type` on create for a draft-18 server) gets files-data's evidence the same way. J-036 (`Blob/copy`, `partial(M5)`): files-data's cross-account copy and con-data's photo
blobs use it; the row moves to `done` when a FakeJmap test covers both. `Principal/query` and `Principal/get` (directory, B-CON-025, RFC 9670) are con-data's: it adds rows J-1xx
(next free ids; check the file) as M4 did for availability, `done` against FakeJmap and `partial` until L-031 reads the directory.

### 2.3 Interactions (I-): 48 rows

Owner = the stream that builds the visible control; a data stream supplies the view model. Rows a stream closes need evidence (a test, an automation script, or a screenshot path
with sample data); everything else is `deferred(M<k>)` with a reason.

| Owner | Rows |
|---|---|
| con-ui (30) | I-066 (the account-scope picker of the Contacts and Files sidebars: con-ui builds it once, files-ui reuses it), I-345..I-369 (sidebar + New menu, books, groups, categories, search, select mode, sort, bulk bar, row click and right-click, keys, empty state, contact menus, detail, form with structured fields and photo and categories, group detail and form, import, merge, book picker, directory and recipient picker dialogs), I-192, I-193 (address menu View contact, Add to contacts), I-204, I-205 (reader name popover card, tel: links), I-231 (contact sidebar of the reader), I-245 (composer address book and directory picker) |
| files-ui (22) | I-371..I-392 (New menu, new file dialog, sidebar tree and Favourites and Recent and Shared and Trash, menus, drag and drop in-window, breadcrumb and Empty trash, Paste and Refresh, search and sort and view toggle and details panel, selection and marquee, double-click, item menus, OS file drop (the drop target and the upload: the real Explorer drag is unproven), preview and details, share and star and rename and duplicate and move and cut and copy and delete, delete with undo and the trash menu, Files keys, undo toasts and upload cancel, error retry and empty states, preview modal with zoom and rotate and prev/next and .eml, move dialog, upload conflict dialog), I-228 (the reader's attachment preview card, through files-ui's preview) |
| con-data, files-data | no visible controls; they supply the view models the rows above read |
| deferred | I-350 "drop contacts on a category or book" with a real pointer (unproven), I-347 "share book" (M6 sharing and ACLs, with calendars' I-306), I-393 Files sync (M10), I-394 file drop overlay (M3, done), I-370 (N/A), I-443 and I-444 (Settings > Contacts pages are M6; M5 supplies the commands they call) |

### 2.4 Plugin hooks (31 rows with `milestone = M5` in `spec/plugins/hooks.csv`; the `owner` column is filled by this commit)

Each stream gives its hooks a call site through `IHookBus` and sets `cs_call_site` / `status` in the CSV for its own rows only (row-level edits merge cleanly; run
`python tools/gen-hooks.py --check`). Payload types are new files under `src/Barbican.Core/Plugins/Payloads/` named after the hook, one file per type. Intercepts run before the
action and can stop it.

| Owner | Hooks |
|---|---|
| con-data (11) | onBeforeContactCreate, onAfterContactCreate, onBeforeContactUpdate, onAfterContactUpdate, onBeforeContactDelete, onAfterContactDelete, onContactsImport, onContactMove, onContactGroupChange, onContactGroupMemberChange, onProvideRecipientSuggestions |
| con-ui (2) | onContactOpen, onContactSelectionChange |
| files-data (14) | onBeforeFileUpload, onAfterFileUpload, onFileUploadCancel, onFileDownload, onDirectoryCreate, onBeforeFileDelete, onAfterFileDelete, onBeforeFileRename, onFileRename, onBeforeFileMove, onFileMove, onFileCopy, onFileDuplicate, onFileFavoriteToggle |
| files-ui (4) | onFileNavigate, onFileSelectionChange, onFileUndo, onAttachmentPreview (mail-attachments, I-228) |

`HookCallSiteTests` gets one test per owner (as M3 and M4): every row of the owner has a call site that a FakeJmap-driven test reaches.

### 2.5 Behaviour rows (B-), live checks (L-), gaps (G-), settings

- **B-:** `spec/behaviours/contacts.md` B-CON-001..031 and `files.md` B-FILE-001..019, each stream marks the rows of its area `ported` or `deferred(M<k>)` in the same commit.
  con-data: B-CON-001, 003, 005..025; con-ui: 002, 004, 026, 027, 030, 031 (028 and 029 are M6); files-data: B-FILE-001..004, 006..011, 016; files-ui: 005, 012..015, 017..019.
- **L- (run in the integrate stage, §7):** L-031 (import a .vcf and export one card), L-032 (duplicates and merge on test cards; the group count), L-033 (photo added and deleted),
  L-034 (hostile fixtures: unit-test form, no account), L-035 (upload, trash, restore a file; Empty trash asks first), L-036 (share, move, cut and paste, duplicate), L-022's files part
  (drag onto folders, with the real pointer unproven). L-037 (Save to Files from a message) stays M2's.
- **G-:** G-010 (contact print: con-ui, in-app print model D-M5-12), G-011 (contact activity and related people: con-ui), G-012 (security review of vCard and photo input: con-data,
  recorded in the milestone report with L-034), G-013 (preview), G-014 (details panel), G-015 (grid and list, sort, search, thumbnails): files-ui; G-016 (upload conflict dialog:
  files-data policy, files-ui dialog), G-017 (zip: files-data service, files-ui menu), G-018 (new text file: files-data, files-ui), G-019 (download filename rules): files-data; G-044
  (live updates for contacts and files; identities stay with M6): con-data and files-data; G-107 stays done.
- **Settings read** (`spec/settings.csv`; each owner sets `csharp_read_by`):

| Stream | Keys |
|---|---|
| con-data | showContactsDirectory (the directory loads only when on and the principals capability is present), timeZone (dates), timeFormat (none) |
| con-ui | groupContactsByLetter, sortContactsByLastName (applied live), showContactsDirectory (the sidebar entry), `keyless` rows S-keyless-052, 053 (Import and Export: M6 builds the rows, con-ui supplies the commands `contacts.import` and `contacts.export`) |
| files-data | filenameCollapseSeparators, filenameLowercase, filenameSpaceReplacement, filenameStripDiacritics, postExportAction (the download filename rules: files-data adds them to `FileNames` in step 3), timeFormat |
| files-ui | `filesSettings` (one object as Bulwark's `lib/files-settings.ts`: show files as, default sort, sort direction, show file icons, colored icons, thumbnails, hidden files; D-M5-11) read by the `keyless` rows S-keyless-054..060, timeFormat |

## 3. Parallel work without shared files

### 3.1 File ownership

Every file has exactly one owner for the whole milestone. Folders go with all files below them. Files not listed stay with the integrator (frozen): `Barbican.slnx`, `Directory.*`
(except the package lines named here), `global.json`, `AppComposition.cs`, `CompositionGraphTests.cs`, `MenuContext.cs` (the two additive members are already in it), `SlotNames.cs`,
every M2, M3 and M4 file (except the exceptions named here).

| Owner | Source | Tests |
|---|---|---|
| **con-data** | `src/Barbican.Core/Contacts/**`; new files in `src/Barbican.Core/Sync/` and `src/Barbican.Core/Jmap/Push/` for AddressBook and ContactCard (additions; the push type list line of the M1 code is the one allowed edit there); its hook payload files in `src/Barbican.Core/Plugins/Payloads/`; the SkiaSharp package line of `Barbican.Core.csproj` (D-M5-02); `src/Barbican.Core/Calendar/Editing/ContactCardReader.cs` (exception: it reads the store, D-M5-06) | `tests/Barbican.Core.Tests/Contacts/**`; `tests/Barbican.FakeJmap/{FakeContacts,FakeApi.Contacts,FakeApi.ContactsSet,FakeSeed.M5Contacts}.cs` (new) |
| **con-ui** | `src/Barbican.Presentation/Contacts/**`; `src/Barbican.Presentation/Composition/ContactsUiPresentationRegistration.cs`; `src/Barbican.App/Views/Contacts/**` (including `Mail/`, the mail-side surfaces); `src/Barbican.App/Composition/Features/ContactsUiRegistration.cs`; **`src/Barbican.App/Shell/ShellPage.xaml`, `ShellPage.xaml.cs` and `IAppScreenFactory.cs` for the whole milestone** (S0, D-M5-07); `src/Barbican.Automation/CommandRouter.Contacts.cs` (new: `con-open`, `con-select`, `con-form-set`, `con-form-save`, `con-dump`); the contacts branch of `ShortcutTables.cs` and the contacts routing in `KeyRouter.cs` / `ShortcutCommands.cs` (exception: no other part of those files); additive edits, named in the Fable section, to the M1/M2/M3 files that host the mail-side surfaces (reader name popover, address menu, reader contact sidebar, composer directory button) | `tests/Barbican.Presentation.Tests/Contacts/**` |
| **files-data** | `src/Barbican.Core/Files/**`; new files in `src/Barbican.Core/Sync/` and `src/Barbican.Core/Jmap/Push/` for FileNode (additions); its hook payload files; `tests/Barbican.FakeJmap/FakeApi.FileNode.cs` (exception: the M2 file gets update, `FileNode/changes`, `onExists`, move, shareWith and the draft-18 shape; the reader's save arm keeps working) | `tests/Barbican.Core.Tests/Files/**`; `tests/Barbican.FakeJmap/FakeSeed.M5Files.cs` (new) |
| **files-ui** | `src/Barbican.Presentation/Files/**`; `src/Barbican.Presentation/Composition/FilesUiPresentationRegistration.cs`; `src/Barbican.App/Views/Files/**`; `src/Barbican.App/Composition/Features/FilesUiRegistration.cs`; `src/Barbican.Automation/CommandRouter.Files.cs` (new: `files-open`, `files-nav`, `files-select`, `files-dump`, `files-drop-sim`); the Files branch of `ShortcutTables.cs` and its routing (exception as con-ui's); registers an `IAppScreenFactory` and never edits `ShellPage`; additive edit, named in the Fable section, to the M2 reader's attachment chip for the in-app preview (I-228) | `tests/Barbican.Presentation.Tests/Files/**` |

Shared and frozen by this plan: `AppComposition.cs` already calls the four registrations (§4.10); `MenuContext.Contact` and `MenuContext.File` exist (§4.5, §4.11); `IAppScreenFactory`
exists (§4.12). `tests/Barbican.Core.Tests/Barbican.Core.Tests.csproj` and the other test projects are frozen except package lines a stream needs (named here: none).

### 3.2 Shared spec files

`spec/rust-tests.csv` (`python tools/spec-check.py --write`), `spec/interactions.csv`, `spec/jmap.csv`, `spec/settings.csv`, `spec/gaps.csv`, `spec/live-checks.md`,
`spec/behaviours/{contacts,files}.md`, `spec/plugins/hooks.csv`, `spec/decisions.md`: edit only your own rows (one line per row merges cleanly; a new decision is appended at the end of
"M5 decisions" under your own prefix). `docs/FABLE-REVIEW.md`: append under `## M5 stream: <key>` (a section already exists for the plan).

### 3.3 Names, decided up front (the M4 collisions)

| | con-data (A) | con-ui (B) | files-data (C) | files-ui (D) |
|---|---|---|---|---|
| Decision IDs | `D-M5-A01..` | `D-M5-B01..` | `D-M5-C01..` | `D-M5-D01..` (the plan's are `D-M5-01..19`, never reused) |
| Type-name prefix | singular: `Contact*`, `VCard*`, `AddressBook*` | plural: `Contacts*` (and the `ContactMenuTarget` of §4.5) | singular: `File*` | plural: `Files*` (and the `FileMenuTarget` of §4.11) |
| Source namespaces | `Barbican.Core.Contacts[.*]` | `Barbican.Presentation.Contacts[.*]`, `Barbican.App.Views.Contacts[.*]` | `Barbican.Core.Files[.*]` | `Barbican.Presentation.Files[.*]`, `Barbican.App.Views.Files[.*]` |
| Test namespaces | `Barbican.Core.Tests.Contacts[.*]` | `Barbican.Presentation.Tests.Contacts[.*]` | `Barbican.Core.Tests.Files[.*]` | `Barbican.Presentation.Tests.Files[.*]` |
| Test helper names | `ContactTestData` | `ContactsFakes` | `FileTestData` | `FilesFakes` |
| Menu id constants | none | `ContactsMenuIds` (MENU-CONTACT, MENU-GROUP, MENU-CATEGORY, MENU-BOOK) | none | `FilesMenuIds` (MENU-FILE, MENU-FILE-TRASH, MENU-FILE-FAV, MENU-FILE-RECENT, MENU-FILES-BG) |
| Registration | `ContactsDataRegistration` | `ContactsUiRegistration`, `ContactsUiPresentationRegistration` | `FilesDataRegistration` | `FilesUiRegistration`, `FilesUiPresentationRegistration` |

Rules that follow: (1) the singular prefix is data, the plural prefix is UI, so `ContactsRow` can never collide with `ContactRow`-like data types, and a stream asked for a name outside its
prefix asks the owner; (2) no two public interfaces share a name: the interfaces are exactly the ones in §4 and a stream adds none without its prefix; (3) shared records have one owner who
adds properties, and a second stream that needs one files a request (the two shared records are `FileEntry`, files-data's, and `ContactsRow`, con-ui's); (4) no test class in a child
namespace repeats the name of a class in a parent namespace of another stream's folder (the folders in §3.1 are disjoint, and a helper name is on the list above); (5) automation commands
have the prefix of their stream (`con-*`, `files-*`).

## 4. Contracts (compiling code in this commit set)

All stubs below are in the repository now, build with warnings as errors, and are wired into the composition (§4.10). A stub that throws does so with
`PortPendingException("Type.Member")`; the 27 red tests prove the stubs are reached. JSContact cards, address books and principals are `JsonNode`/`JsonObject` (D-M5-04); instants are UTC.

### 4.1 Contacts, pure rules (owner con-data; con-ui consumes the detail lines, the options and the form rows)

`ContactOptions` (real data: contexts, phone features and contexts, date kinds, 13 relations, the two capability URNs), `ContactField`, `ContactForm`, `ContactFields.Of/Build/DateText/ParseDateText`
(T-069, T-070), `PrettyDate`, `ContactDates.Pretty` (T-071), `ContactLine`, `ContactDetail.Lines`, `ContactPhoto.Shrink` + `ShrunkPhoto` (T-073, G-012 cap), `AddressParts`, `VCardLimits` (G-012),
`VCardParseResult`, `VCardParser.Parse/ParseChecked/ParseDate/ParseDataUri/AddressPartsOf` (T-306, T-307, T-309, T-310, G-012), `VCardWriter.Generate/DateToVCard` (T-308),
`ContactImport.DetectDuplicates/Batch/NewUid/PrincipalCard` (T-072, T-310), `ContactMerge.NameKey/PhoneKey/FindGroups/Plan/MemberKeys/GroupMoves` + `ContactMergePlan` (T-067, T-068),
`AddressBooks.DefaultBook` (B-CON-003).

### 4.2 Contact data (owner con-data; consumed by con-ui and mail)

`IContactStore` / `ContactStore`, `ContactSnapshot` (books, cards, state strings), `ContactChangedEventArgs`: loaded per login in chunks of `maxObjectsInGet`, shared accounts namespaced by the
`ContactIds`-style key con-data chooses (the same scheme as `CalendarIds`: account id and id; D-M5-A01 records it), `Patch`/`Restore` for the optimistic category change.

### 4.3 Contact writes (owner con-data)

`IContactWriter` / `ContactWriter`: `CreateAsync`, `UpdateAsync` (pointer patches; whole `keywords`), `DeleteAsync`, `MoveAsync`, `CreateGroupAsync`, `SetMembersAsync`, `MergeAsync` (patch, group moves,
then deletes, in that order), `ImportAsync` (chunks). `IAddressBooks` / `AddressBooks`: `EnsureDefaultAsync` (asks the server again before it creates "Contacts"), `CreateAsync`, `RenameAsync`, `DeleteAsync`.

### 4.4 Directory (owner con-data)

`IContactDirectory` / `ContactDirectory`: `IsAvailable(accountKey)`, `LoadAsync` (Principal/query paged at most 500, with the Principal/get fallback when the server refuses the query; the flag says which).

### 4.5 Contacts and mail, contacts UI (owners con-data for the seam, con-ui for the screen)

`IContactLookup` / `ContactLookup` + `ContactMatch` (con-data): `SuggestAsync` and `FindByEmailAsync`; the reader's popover and sidebar and the address menu read this and never `IContactStore`; the composer
keeps its M3 seam `IRecipientSources` and con-data decorates the registered source with contacts and groups (D-M5-06). `ContactsPlace`, `ContactsRow`, `ContactsListRules` (`FullName`, `SortName`, `LetterOf`,
`Initials`, `Matches`), `IContactsViewModel` / `ContactsViewModel` (con-ui), `ContactMenuTarget` and `MenuContext.Contact` (frozen, in this commit).

### 4.6 Files, pure rules (owner files-data; files-ui consumes)

`FileEntry` (a record: key, account, shared flag, id, parent key, name, directory flag, blob id, size, modified, media type, `shareWith`; `FromJmap` reads both shapes, `MakeKey` the key scheme: bare id in the primary account,
"account|id" in a shared one), `TrashedItem`, `FileTrash` (`TrashName`, `TrashLocation`, `BatchName`, `ParseBatch`, `Subtree`, `Items`, `EmptiedBatches`: T-091, T-092), `FileNames.UniqueName` (T-092),
`FileNameRules`, `FileNameProblem`, `FileNameChecks.RulesFrom/ProblemOf` (the server's rules before an upload).

### 4.7 File data (owner files-data)

`IFileStore` / `FileStore` (load, snapshot, refresh, favourites and recent in the profile's `files.json`, device-local), `FileSnapshot`, `FileChangedEventArgs`.

### 4.8 File writes (owner files-data)

`IFileWriter` / `FileWriter`: `CreateFolderAsync`, `RenameAsync`, `MoveAsync`, `CopyAsync` (across accounts with Blob/copy), `DuplicateAsync`, `TrashAsync`, `RestoreAsync`, `PurgeAsync`, `EmptyTrashAsync`, `ShareAsync`.

### 4.9 Transfers (owner files-data)

`IFileTransfer` / `FileTransfer`, `FileConflictChoice` (KeepBoth, Replace, Skip): `UploadFilesAsync`, `UploadFolderAsync` (a callback asks once per taken name), `DownloadAsync` (download filename rules of settings), `DownloadZipAsync`,
`CreateTextFileAsync`, `ReadAsync` (preview bytes under a cap).

### 4.10 Composition (DI skeletons, compile and resolve today)

`AppComposition.Build` now calls, after the M4 features: `AddBarbicanContactsData()` (Core, con-data), `AddBarbicanFilesData()` (Core, files-data), `AddBarbicanContactsUi()` (`Features/ContactsUiRegistration.cs`, App; calls
`AddBarbicanContactsUiViewModels()`), `AddBarbicanFilesUi()` (`Features/FilesUiRegistration.cs`, App; calls `AddBarbicanFilesUiViewModels()`). `CompositionGraphTests.Compose` registers the four portable parts (`AddM5`), so "every registered
service resolves" covers them. Rules as M3 §4.7 and M4 §4.6: defaults use `TryAddSingleton`; a stream that implements a service changes the registration in its own file (`Replace` when the default belongs to another feature);
stub classes have no constructor parameters, so the graph test passes while they exist; a stream that adds dependencies keeps the graph test green.

### 4.11 Files UI (owner files-ui)

`FilesSortKey`, `FilesViewMode`, `FilesPreviewKind`, `FilesListRules.Sort/Visible`, `FilesPreviewRules.KindOf`, `IFilesViewModel` / `FilesViewModel`, `FileMenuTarget` and `MenuContext.File` (frozen, in this commit).
Special locations of `IFilesViewModel.Location`: a folder key, `*favourites`, `*recent`, `*shared`, `FileTrash.TrashLocation`.

### 4.12 The shell seam (owner con-ui; consumed by files-ui)

`src/Barbican.App/Shell/IAppScreenFactory.cs` (`App`, `IsAvailable()`, `Create()`, `OnShown()`): ShellPage keeps its Calendar host and gets one generic host that shows the screen of the registered factory whose `App` is the
rail's active app, built on first visit and then only hidden. con-ui generalises `ShellPage` once (S0) and sets `SetAvailableApps` from the factories; files-ui only registers its factory (`services.AddSingleton<IAppScreenFactory, FilesScreenFactory>()`).

### 4.13 FakeJmap contacts arms (in this commit set)

`tests/Barbican.FakeJmap/FakeContacts.cs` (an in-memory contacts account: `AddBook`, `AddCard`, state strings per type, a change log for /changes, `ExpireStates`, `Fails`), `FakeApi.Contacts.cs` (**real**: AddressBook/get,
AddressBook/changes, ContactCard/query with `inAddressBook`, `uid`, `kind`, `text` and `email` filters and paging, get with the `maxObjectsInGet` limit, changes), `FakeApi.ContactsSet.cs` (stub: AddressBook/set and ContactCard/set answer
unknownMethod until con-data fills it). Options: `FakeJmapOptions.ContactsCapability` and `Contacts`. Green tests: `FakeContactArmsTests`. con-data adds `FakeSeed.M5Contacts` (a Contacts book and a Work book, cards with typed entries, a group, a duplicate pair, a
photo, a shared account's book, 600 cards for the chunking) and fills the set arm in its first step (create with uid and `addressBookIds`, update with pointer patches and Stalwart's `invalidPatch` for a pointer into a missing map, destroy, `notFound`, `invalidProperties`, `ifInState`, a request log and
`FailNextSet` as `FakeApi.CalendarSet.cs` has). files-data extends the existing `FakeApi.FileNode.cs` (§3.1) with update, `changes`, `onExists` rename, move and `shareWith`, and the draft-18 shape behind an option.

## 5. Order, sync points and merges

All four start on day one, from `m5`. **Merge early:** a stream merges into `m5` after every green step (`tools/build.ps1 test`; Linux is M9), and every other stream merges `m5` into its branch at the start of each step. The sync points:

| Point | Who | What lands in `m5` | Who needs it |
|---|---|---|---|
| **S0** | con-ui | Step 1: `ShellPage` generalised to the `IAppScreenFactory` host, the rail's `SetAvailableApps` from the factories (Contacts when the login has the contacts capability, Files with filenode), an empty contacts screen over a real `ContactsViewModel` | files-ui (registers its factory), the automation scenarios |
| **S1** | con-data | Step 1: vCard reader and writer with limits, `ContactFields`, dates, merge, import, photo (T-067..T-073, T-306..T-310, the 4 G-012 and photo tests), the FakeJmap set arm | con-ui (form, detail, import and merge dialogs) |
| **S1** | files-data | Step 1: `FileEntry`, `FileTrash`, `FileNames`, `FileNameChecks` (T-091, T-092, both shapes), the FakeJmap FileNode extensions | files-ui (tree, trash, list) |
| **S2** | con-data | Step 2: store and writer and address books (load, `/changes`, writes with hooks) | con-ui (real data and saves) |
| **S2** | files-data | Step 2: store, writer, favourites and recent | files-ui (real data, menus) |
| **S3** | con-data | Step 3: directory, lookup, the recipient source decorator, the store-backed `ContactCardReader` | con-ui (directory dialog and the mail-side surfaces), the composer, the calendar's birthdays |
| **S3** | files-data | Step 3: transfers (upload with conflicts, folder upload, download, zip, new text file, preview read) | files-ui (upload, dialogs, preview) |

What **con-ui needs from con-data**: `ContactFields`, `ContactDetail`, `ContactMerge`, `VCardParser`/`VCardWriter` (S1), `IContactStore` and `IContactWriter` (S2), `IContactDirectory` and `IContactLookup` (S3); until then it works against fakes in
`tests/Barbican.Presentation.Tests/Contacts/ContactsFakes.cs` that implement the interfaces. What **files-ui needs from files-data**: `FileTrash` and `FileEntry` (S1), the store and writer (S2), the transfer (S3); fakes in `FilesFakes.cs`. What con-ui needs from
files-ui and the other way round: nothing (the contact "files" tile, B-CON-026, reads `IFileStore` read-only once S2 of files-data has landed, and shows no files before). What con-data and files-data need from the others: nothing.

**Merge order into `m5` for the milestone close:** con-data, files-data, con-ui, files-ui (each already merged incrementally); the integrator then runs §7.

## 6. Stream briefs

Common to all: read CLAUDE.md, `.local/accounts.md`, this plan §1 to §5 and the PORT-PLAN sections named in your brief; create the worktree `<downloads>/wt-m5-<key>` on branch `m5-<key>` from `m5`; push at creation and after
every commit; Fable section `## M5 stream: <key>`.

### 6.1 `con-data` (letter A, Sonnet)

**Scope.** Everything between JMAP and a card the UI can show, and the file format at the border. No XAML. Reads Rust `src/vcard.rs`, `contacts_x.rs` (`fields_of` :121, `build_card` :190, `pretty_date` :352, `detail_lines` :380, `shrink_photo` :565, `import_batch` :752,
`fetch_directory` :858, `principal_card` :909), `contact_merge.rs`, `contacts.rs` (load, groups, suggestions); Bulwark `lib/vcard.ts`, `lib/contacts/`, `lib/contact-dates.ts`, `lib/contact-media.ts`, `lib/contact-related.ts`, `stores/contacts-store.ts`,
`components/contacts/{contact-form,contact-import-dialog,contact-merge-dialog,directory-browser}.tsx`; Stalwart `crates/jmap/src/{addressbook,contact}/`, `crates/jmap-proto/src/object/{addressbook,contact}.rs`, `crates/jmap/src/principal/`.

**Owned:** §3.1 row "con-data". **T rows:** T-067..T-073, T-306..T-310 (12). **J rows:** J-071, J-036 (photo blobs), directory rows. **Hooks:** 11 (§2.4). **Settings:** §2.5. **G rows:** G-012, G-044 (contacts). **B rows:** §2.5.

**Steps (each ends green and merged to `m5`):**
1. *Format and rules.* Make the 12 Rust red tests and the 5 others that need no network pass: `VCardParser` (2.1, 3.0, 4.0; folding; quoted-printable; RFC 6868; Apple grouped labels; reduced dates; photos as data URIs; limits and `ParseChecked`, the hostile fixtures of L-034 are the
   three tests in `VCardParserTests` and you add more shapes: nested BEGIN without END, 100 000 folded continuation lines, NULs, lone CR, a 1 MB line, 10 000 entries in one card), `VCardWriter` (4.0, 75-octet folding by UTF-8 bytes), `ContactFields`, `ContactDates`, `ContactDetail`,
   `ContactMerge`, `ContactImport`, `ContactPhoto` (SkiaSharp, D-M5-02; decode with a pixel cap before allocating), `AddressBooks.DefaultBook`. Add the SkiaSharp package line to `Core.csproj` (and `SkiaSharp.NativeAssets.Linux` only if the Linux build needs it: M9). Fill `FakeApi.ContactsSet.cs` and `FakeSeed.M5Contacts`.
2. *Store and writes.* `ContactStore.LoadAsync` (AddressBook/get, ContactCard/query paged through `QueryAllAsync` and ContactCard/get in `maxObjectsInGet` chunks; shared accounts with the contacts capability; a failure in a shared account is skipped, in the primary one is returned; state strings for `/changes`), `RefreshAsync`
   (push type list line, `AddressBook` and `ContactCard` through the generic /changes engine as cal-data did, with a full-load fallback), `Patch`/`Restore`; `ContactWriter` (every call applies its hooks, patches with pointers, sends `keywords` whole, rolls back and confirms by the push delta; `MergeAsync` order; `ImportAsync` in chunks of 100 with partial count on failure), `AddressBooks` (the server asked
   again before "Contacts" is created, B-CON-003). Mark J-071 `done` with FakeJmap evidence.
3. *Directory, lookup, mail.* `ContactDirectory`, `ContactLookup` (diacritics-insensitive ranking, groups excluded, `onProvideRecipientSuggestions`), the decorator over `IRecipientSources` (contacts and groups join the Sent-based recent recipients; group expansion as `RecipientGroup` has), the store-backed `ContactCardReader` for the calendar's birthdays and guest search (D-M5-06),
   `HookCallSiteTests` for con-data's 11 rows, settings, B- and G- rows, the G-012 note for the milestone report (what was reviewed, the limits, the tests).

### 6.2 `con-ui` (letter B, Sonnet)

**Scope.** The Contacts app and the mail-side contact surfaces. Reads Rust `contacts.rs`, `ctxmenu.rs:314-330, :454`, `app.slint` contacts sections, `shortcuts.rs:204` (`contacts`, `list_keys` :796); Bulwark `components/contacts/*` (the 21 files listed under §2.3), `components/email/recipient-popover.tsx`, `address-context-menu.tsx`, `email-viewer.tsx:587-751`, `components/contacts/contact-print*`;
`design/mockups/BNContacts.dc.html` and `spec/layout/contacts.md` (the look: people list tile with letter headers, squircle or circle avatars, B-CON-030); `spec/menus.md` MENU-CONTACT, MENU-GROUP, MENU-CATEGORY, MENU-BOOK.

**Owned:** §3.1 row "con-ui". **T rows:** none. **I rows:** §2.3. **Hooks:** onContactOpen, onContactSelectionChange. **Settings:** §2.5. **G rows:** G-010, G-011. **B rows:** §2.5.

**Steps:**
1. *S0 shell seam and empty screen* (first, a few hours; files-ui waits for it): `IAppScreenFactory` host in `ShellPage`, rail availability from the factories, `ContactsScreen` over a real `ContactsViewModel` (list, search, place, selection) against `ContactsFakes` until S2. Make the three red tests pass (`ContactsListRules`).
2. *Sidebar, list, detail, form.* Sidebar sections (All contacts, books with the "existing book" rule's UI, groups, categories, Directory when available, Duplicates), the list with letter headers (`groupContactsByLetter`) and sort (`sortContactsByLastName`, live), select mode, bulk bar, keys (B-CON-031), empty states, detail (lines from `ContactDetail`, activity tiles B-CON-026 read-only from the mail store and `IFileStore`, drafts excluded),
   form (structured entries from `ContactFields`, the photo picker through the system picker and `ContactPhoto.Shrink`, categories, book, kind, more fields), delete with confirm. All popups in-window (ADR 0007).
3. *Dialogs, menus, print, mail side.* Import (the preview, duplicates list and skip, book picker, progress), export (whole list or one card through the system save picker, sanitised name), merge and duplicates view, group form with member picker, directory and recipient picker dialogs, address-book dialogs, MENU-CONTACT, MENU-GROUP, MENU-CATEGORY, MENU-BOOK through `MenuContext.Contact`, commands and palette entries (`contacts.import`, `contacts.export`,
   `contacts.new`...), the in-app print model (D-M5-12), the reader's name popover with the contact card and tel: links, the address menu's View contact and Add to contacts, the reader contact sidebar, the composer's directory button; automation commands and a scenario; hooks; B-, I- and G- rows.

### 6.3 `files-data` (letter C, Sonnet)

**Scope.** FileNode, the trash and the transfers. No XAML. Reads Rust `src/files.rs` (`file_node` in `reader.rs:949`, `PROPS` :36, `is_dir` :38, trash :73-:127, `open` :185, `download` :355, `upload` :387, `delete` :465, `restore` :515, `upload_folder` :546, `paste` :652, `share` :743), Bulwark `lib/files-trash.ts`, `lib/files-upload-conflicts.ts`, `lib/files-zip.ts`, `lib/file-name-rules.ts`, `lib/download-filename.ts`, `lib/files-sync/`, `stores/files-store.ts`; Stalwart `crates/jmap/src/file/{get,query,set,copy}.rs`, `crates/jmap-proto/src/object/file_node.rs`, `crates/groupware/src/file/`.

**Owned:** §3.1 row "files-data". **T rows:** T-091, T-092. **J rows:** J-075, J-036. **Hooks:** 14. **Settings:** §2.5. **G rows:** G-016 (policy), G-017, G-018, G-019, G-044 (files). **B rows:** §2.5.

**Steps:**
1. *Rules and shapes.* The four red tests: `FileEntry.FromJmap` for both shapes and the key scheme, `FileTrash`, `FileNames.UniqueName`, `FileNameChecks` (UTF-8 byte length, control characters, reserved stems). Extend `FakeApi.FileNode.cs` (§4.13) and add `FakeSeed.M5Files` (a tree with a trash batch, a shared account's tree, 3 000 nodes for the chunking, a draft-18 variant).
2. *Store and writes.* `FileStore.LoadAsync` (query paged, get in chunks, the parent filter on the client, shared accounts), `RefreshAsync` (`FileNode` in the push types), favourites and recent in `files.json` (atomic write), `FileWriter` (every operation with its hooks; a delete is a move into a dated batch, `TrashAsync` creates `.Trash` when missing; restore to the batch's parent or the root; purge; empty trash; `onExists` rename for a copy; `CopyAsync` across accounts with `Blob/copy`; `ShareAsync`).
3. *Transfers.* `FileTransfer` (blob upload then FileNode/set, the conflict callback, progress and cancel with no half-made node, folder upload folders first, download through the download filename rules (a new member of `FileNames`) with the filename settings, zip with a streaming writer and a size cap, new text file, `ReadAsync` under a cap), `HookCallSiteTests`, settings, B- and G- rows; mark J-075 `done`.

### 6.4 `files-ui` (letter D, Sonnet)

**Scope.** The Files app. Reads Rust `files.rs` (`render` :236 and the menus), `ctxmenu.rs`, `shortcuts.rs` (files keys); Bulwark `components/files/*` (27 files), `lib/file-preview.ts`, `lib/files-settings.ts`; `design/mockups/` (Files restyle, B-FILE-017), `spec/menus.md` MENU-FILE and the others.

**Owned:** §3.1 row "files-ui". **T rows:** none. **I rows:** §2.3. **Hooks:** onFileNavigate, onFileSelectionChange, onFileUndo, onAttachmentPreview. **Settings:** §2.5. **G rows:** G-013, G-014, G-015 (and the UI of G-016..G-018). **B rows:** §2.5.

**Steps:**
1. *Presentation first.* The three red tests (`FilesListRules`, `FilesPreviewRules`), `FilesViewModel` over `FilesFakes`, the tree and the special locations. When S0 of con-ui has landed: `FilesScreenFactory`, the empty screen, Files in the rail.
2. *Browser.* Sidebar (My files tree, Favorites, Recent, Shared with me, Trash with Empty trash asking first), breadcrumb, list and grid views, sort, search, thumbnails and icons (settings object `filesSettings`, D-M5-11), selection (click, Ctrl, Shift, marquee), double-click, F2 inline rename, keys (B-FILE-012), menus through `MenuContext.File`, details panel, move dialog, in-window drag and drop onto folders and the trash, undo toasts.
3. *Transfers, preview, mail.* Upload (files, folder, the in-window drop target), the conflict dialog (KeepBoth default, apply to all, D-M5-14), new file dialog, download and zip, the preview modal (image with zoom and rotate, text, .eml through the reader's own parser; pdf, audio and video open outside, D-M5-15), the reader's attachment preview (I-228), commands, palette entries, automation commands and a scenario, hooks, B-, I- and G- rows.

## 7. Integration, live checks and verify (§6.1 steps 3 to 7 of the PORT-PLAN)

The integrator (one agent, after the last stream merges) runs: `tools/build.ps1 test` green (Linux is M9); `python tools/red-check.py --list` shows nothing left (a leftover is a stream's bug); `python tools/spec-check.py --milestone M5` exit 0; `tools/auto-run.ps1 smoke`; the contacts and files automation scenarios; then the **live checks one agent at a time** on
`<test-account>` as §2.5 lists (L-031, L-032, L-033, L-035, L-036; L-034 is unit tests), `<readonly-account>` only for reading the directory and listing contacts and files, and the screenshots (sample data only, never a real mailbox). The milestone report `docs/milestones/M5.md` lists: what was proven, what is **unproven and needs a human** (a real-pointer drag of a contact or a file, an
Explorer file drop, printing on paper, a vCard file from a real phone or another client, whether the user's server offers the directory), the G-012 review (what was reviewed, limits, tests, residual risk), the J-071, J-075 and G- rows closed, and every default of §9. No hand-test gate follows: the user may run HAND TEST 5 (PORT-PLAN: import a .vcf, merge duplicates, add a photo to a test contact; upload a folder, trash it, restore it on `<test-account>`) whenever they wish.

## 8. Order of work inside the milestone (summary)

`m5` created from `m5-plan` → four worktrees → S0 (con-ui's shell seam, a few hours) and S1 (con-data's format and rules, files-data's rules, a day each) while the UI streams do their presentation code against fakes → stores and writers (S2) → directory, lookup, transfers (S3) → dialogs, menus, preview, mail-side surfaces → integrate. If con-data is late, con-ui keeps its fakes
and builds against the rules; if S0 is late, files-ui keeps working in Presentation and registers its factory when the seam lands. The only hard serial dependency: S0 before any App-head merge of files-ui's screen.

## 9. Decisions this plan took (defaults; Bulwark wins) (spec/decisions.md D-M5-01..19)

1. **Four streams, split by layer and by app** (D-M5-01): con-data, con-ui, files-data, files-ui; Contacts and Files are of similar size and share nothing but the shell seam, so each gets a data and a UI stream; mail-side contact surfaces are con-ui's, the reader's attachment preview is files-ui's.
2. **Photos are decoded and encoded with SkiaSharp** (D-M5-02), which both heads already carry; the codec sits behind `ContactPhoto.Shrink` so the choice can change.
3. **Hostile input has limits** (D-M5-03): the vCard reader refuses a file over 16 M characters, reads at most 10 000 cards, cuts a line at 1 M characters, drops a photo above about 8 MiB (the card stays), keeps at most 500 entries of a kind; a picked image or data URI above 8 MiB is refused before decoding. Rust had no limits; the numbers are tests, not guesses, and a stream may tighten them with a decision.
4. **JSContact stays JSON, FileNode becomes `FileEntry`** (D-M5-04): a card must keep every property it has (B-CON-011); a file tree needs typed access and both server shapes normalised once.
5. **The vCard mapping is hand-written and ported from Rust and Bulwark** (D-M5-05), not FolkerKinzel: PORT-PLAN named FolkerKinzel "plus the ported mapping", but the ported mapping carries every behaviour the 5 vcard tests pin (Apple labels, PartialDate, quoted-printable, unknown properties) and FolkerKinzel adds a dependency that does not. A stream may use FolkerKinzel in the test project as a differential oracle (one package line, a decision) if it wants one.
6. **The calendar's contact reader and the composer's recipient source are replaced, not duplicated** (D-M5-06): `IContactCardReader` (M4) reads the contact store; `IRecipientSources` (M3) is decorated so contacts and groups join the recent recipients.
7. **The shell seam is `IAppScreenFactory`, owned by con-ui** (D-M5-07): ShellPage is edited once for the milestone; files-ui registers a factory. The Calendar host is not moved (M10).
8. **Menu targets are additive members of `MenuContext`, added by this plan** (D-M5-08), as M4 did for the calendar: `Contact` and `File`; the menu ids are each UI stream's own constants.
9. **Favourites and recent files are device-local** (D-M5-09, `files.json` in the profile, as Rust and Bulwark's local store): not synced, 30 recent entries.
10. **The trash is Bulwark's `.Trash` with dated batches** (D-M5-10): a delete is a move; a permanent delete (purge, a delete inside the trash, a delete in a shared account that has no trash) always asks (D-7); Empty trash asks first and says what it will remove.
11. **Files display settings are one object `filesSettings`** (D-M5-11), as Bulwark; the keyless rows S-keyless-054..060 read it; defaults from `lib/files-settings.ts`.
12. **Contact print is an in-app print model** (D-M5-12), as D-M4-14 for the calendar: pure model, in-window preview, the OS print path built but not run by an agent.
13. **The directory is shown only with the principals capability and `showContactsDirectory`** (D-M5-13); a server that refuses the query falls back to Principal/get (B-CON-025).
14. **The upload conflict dialog defaults to Keep both** (D-M5-14), with Replace and Skip and "apply to all"; no write happens before the user chose; Replace is a FileNode update of the blob, never a delete and create.
15. **The in-app preview covers images, text and .eml** (D-M5-15); pdf, audio and video are downloaded to a temp file under the profile and opened by the OS default app (Bulwark's pdf viewer needs a web engine: an in-app pdf preview is `deferred(M10)` with the reason in the row).
16. **Live checks run only in the integrate stage, one agent at a time** (D-M5-16); contacts and files written on `<test-account>` are cleaned up, nothing is emailed to a contact, `<readonly-account>` is read only.
17. **A real-pointer drag, an Explorer drop and paper printing are "unproven, needs a human"** (D-M5-17), never gates; the automation `drop-sim` and `con-select` commands drive the same handlers in-process.
18. **Export goes through the system save picker only** (D-M5-18): the file name is sanitised, photos stay inlined as data URIs (blob photos are fetched and inlined, B-CON-014), one file for the whole list or one card.
19. **The mail-side contact surfaces are con-ui's and edit M1/M2/M3 files only additively** (D-M5-19): every touched file is named in con-ui's Fable section; the contact data they read comes only through `IContactLookup`.

## 10. Risks

| Risk | Mitigation |
|---|---|
| A hostile vCard or photo crashes the app or grows memory (the review has been open since round 2) | `ParseChecked` limits, a pixel cap before decoding, 4 red tests and more written by con-data in step 1, L-034 as unit tests on Windows (Linux: M9) |
| The vCard mapping loses data (unknown properties, Apple labels, partial dates) | Rust's 5 vcard and 5 contacts_x tests are the oracle of record; round-trip tests on every shape of `FakeSeed.M5Contacts`; the writer's folding is by UTF-8 byte |
| A merge loses a contact or a group membership | `MergeAsync` writes the patch and the group moves before it deletes, so a failure loses nothing; L-032 on test cards only |
| A permanent file delete without asking (the 2026 data-loss report in M2) | The destructive paths are `PurgeAsync` and a delete inside the trash: the UI always confirms (D-M5-10), the writer has a test that `TrashAsync` never destroys |
| Thousands of contacts or files freeze the list | Virtualised lists, a 600-card and a 3 000-node seed, a measured open time in the report |
| Two UI streams contend for `ShellPage.xaml` and `ShortcutTables` | One stated owner each (con-ui for ShellPage, each stream for its own shortcut branch); the integrator resolves a textual conflict, never a behavioural one |
| Contact photos (blobs) leak into the profile or logs | Photos are cached under the profile's cache folder only, never logged; no personal data in tests: fixtures are generated |
| The Stalwart 1.0 and 0.16 FileNode shapes diverge further | Both shapes are normalised in `FileEntry.FromJmap` with tests for both; the FakeJmap draft-18 option; L-035 records the shape the user's server answers |
