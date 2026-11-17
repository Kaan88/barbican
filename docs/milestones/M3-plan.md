# M3 plan: compose and send, with the native editor

- Written 2026-11-18 by the M3 plan stage (PORT-PLAN.md §6.1 step 1), on branch `m3-plan`, taken from `m2`.
- Scope and exit: [PORT-PLAN.md](../PORT-PLAN.md) §5 "M3" (line 720), §3.8 (the editor), §3.9 (single instance, default app),
  Appendix A/B/C rows for M3; engine decision: [adr/0006-editor-engine.md](../adr/0006-editor-engine.md); the model for this
  plan's shape: [M2-plan.md](M2-plan.md).
- Integration branch: **`m3`**, created from `m3-plan` once this plan is pushed (the plan commit set carries the contracts and the
  red tests, so `m3` starts with them). `m2` is merged into `m3` by the integrator whenever `m2` changes (never a rebase, never a
  force-push). Nothing merges to `main` and nothing is tagged until the user says so.
- Accounts and paths are placeholders (CLAUDE.md, `.local/accounts.md`): `<readonly-account>`, `<test-account>`, `<server>`,
  `<rust-repo>`, `<bulwark>`, `<stalwart-zip>`, `<downloads>`.
- **Process (user decision 2026-11-18):** no review stages, no hand-test gates, no questions back to the user. Every open
  decision in this plan is taken with the default that follows Bulwark and is listed in §9. What only a human can do (typing with a
  real IME, a mailto link from a browser, the default-app registration) is written down as "unproven, needs a human" in the
  milestone report; it never blocks a merge. Agents are Sonnet (Haiku for mechanical work); each brief in §6 is written so one
  Sonnet agent can do it alone.

## 0. Summary

| Key (branch `m3-<key>`) | Letter | Scope | Model | T rows | Wave |
|---|---|---|---|---|---|
| `send-pipeline` | A | Drafts and autosave, reply/reply-all/forward seeding and quoting, attachments and `cid:`, identities and From override, send with undo (HOLDFOR), schedule send and the Scheduled view, templates and signatures, send warnings, plugin send hooks | Sonnet | 12 | 1 |
| `editor-core` | B | Editor document model commands, undo, input rules, HTML in (AngleSharp) and out (Bulwark spacing), source view round trip, spell-check engine and the Windows checker (pure .NET, no XAML) | Sonnet | 26 | 1 |
| `editor-view` | C | The XAML editor control on the layout engine: paint, caret, selection, hit test, IME, clipboard, toolbar, bubble, link popover, tables and images UI, source view, context menu, mentions; step 0 is the Uno 7 move | Sonnet | 0 | 1 |
| `composer-ui` | D | The composer shell (inline in place of the reader), recipients chips and autocomplete, From/Cc/Bcc, attachments UI, send flow dialogs, schedule and template dialogs (views over A's view models), leave guard, compose commands and shortcuts, mailto (RFC 6068), single instance, default-app registration code (not executed) | Sonnet | 21 | 1 |

- **All four streams start together** (one wave), because the contracts in §4 are compiled code with stubs and every stream
  works against interfaces first. §5 says what each stream needs from the others and when it merges into `m3`.
- **Limits held:** at most 3 agents compile at once (CLAUDE.md; `tools/build.ps1` holds 4 slots and queues the rest), at most 5
  agents alive, one Linux VM build at a time. Streams B and A build only Core and Presentation tests most of the time; C and D
  build the App head and queue on the slots.
- **Branches `m3-<key>`**, worktrees `<downloads>/wt-m3-<key>`, push at creation and after every commit.
- **Ports and profiles** (hidden AUTOMATION runs, `BARBICAN_PROFILE=auto-<port>`): send-pipeline 8891, editor-view 8892,
  composer-ui 8893, integrate/verify 8899. editor-core has no UI and needs none.
- **M3 T rows:** 66 (`python tools/spec-check.py --milestone M3`: 7 `rte::model` rows were ported in the S3 spike, 59 were
  pending). This plan commit ports 59 as red tests (§2.1).

## 1. Rules every M3 agent repeats

Everything in [M2-plan.md](M2-plan.md) §1 still holds (CLAUDE.md, `.local/accounts.md`, hidden automation only, frame captures that
are uniform are invalid and never a pass, `<readonly-account>` strictly read-only, never read `<admin-key-file>`, builds only
through `tools/build.ps1` / `tools/linux.ps1`, push every commit, privacy check before every commit, Fable log, code rules, feel
rules). The additions and changes for M3:

1. **No reviews, no hand-test gates, no questions.** Choose the default (Bulwark wins), write it in your Fable section, move on.
2. **Live checks** run only in the integrate stage (§7), one agent at a time, on `<test-account>`, sending only to itself, and
   every write is cleaned up (drafts destroyed, scheduled sends cancelled, templates deleted). A stream writes its FakeJmap
   equivalent and its live script and runs nothing live itself.
3. **Never touch the user's screen, mouse or keyboard**, and never run the IME, the clipboard of the real desktop or the default-app
   registration. The composer is driven in-process by the hidden automation build (editor automation commands are stream
   editor-view's, §6.3). The default-app registration code is written and unit-tested as a pure plan (`DefaultMailApp.WindowsPlan`);
   **nothing writes the registry, `~/.local/share/applications` or runs `xdg-mime` in any test or run** (CLAUDE.md: no OS changes
   without the user's OK). The Settings button that would execute the plan is built but its handler asks the user first and is
   never exercised by an agent.
4. **Contract stubs** throw `PortPendingException` (`src/Barbican.Core/Common/PortPendingException.cs`). The stream that owns a
   stub replaces its body (and, where the plan says so, the whole class and its registration) and removes the `[Red("...")]`
   attributes of the tests that now pass **in the same commit** (`python tools/red-check.py` reports "GREEN, remove [Red]").
   Never change a contract signature without telling the integrator in your Fable section under "Requests to other streams";
   adding members and files is free.
5. **Ported Rust tests** carry `[RustTest("module::name")]` exactly as `spec/rust-tests.csv` has it; an assertion may change only
   with a D- entry (D-240..D-245 already exist for the ones this plan changed). Run `python tools/spec-check.py --write` in the same
   commit.
6. **Bulwark and Stalwart references.** Every Core file starts with the Bulwark file:line, the RFC section and the Stalwart path it
   mirrors (`<bulwark>`, `<stalwart-zip>`); the contract stubs name the file and function, **you verify the line numbers** when you
   replace a stub.
7. **Typing latency and paint cost** are measured with the in-process probes, never by eye; the editor budget is in §6.3.
8. **Stay inside your owned files (§3).** A change you need elsewhere goes under `## Requests to other streams` in your Fable
   section; work around it behind your own interface; the integrator applies it.
9. **User decisions in force:** D-2, D-3, D-7, D-9, D-10 of M2-plan §1 item 14, and D-1 (rewriting public history) is not approved.

## 2. What M3 must close

### 2.1 Rust tests (T-): 66 rows

The 59 pending rows are already **red tests** in this commit set (`python tools/red-check.py --list`: composer-ui 21, editor-core
26, send-pipeline 12). A stream's first job is to make its red tests pass; the test files are at the paths below.

| Stream | Rows | Test files |
|---|---|---|
| editor-core (26) | T-321..T-330 `rte::editor::*` (10); T-331..T-342 `rte::html::*` (12); T-350, T-351, T-352 `rte::spell::*`; T-353 `rte::spell::windows_checker` | `tests/Barbican.Core.Tests/Editor/EditorSessionTests.cs`, `DocHtmlTests.cs`, `SpellTextTests.cs`; `tests/Barbican.Platform.Tests/WindowsSpellCheckerTests.cs` |
| send-pipeline (12) | T-015, T-016, T-017 `autosave::*`; T-060 `blank_drafts`, T-062 `quoted_inline_parts_use_cid_html`, T-063 `reply_identity_order`, T-065 `placeholders`, T-066 `delivered_to_headers`; T-253, T-254, T-255 `schedule::*`; T-285 `templates::tombstone_keeps_other_deletions` | `tests/Barbican.Core.Tests/Compose/{AutosaveStateTests,ComposeRulesTests,CidHtmlTests,ReplyIdentityTests,ScheduleRulesTests,TemplateTombstoneTests}.cs` |
| composer-ui (21) | T-223..T-230 `recipients::*` (8); T-057, T-058, T-059 `chips::*`; T-061 `compose::recipients_round_trip`, T-064 `compose::reply_dock_choice`; T-170..T-174 `mailto::*` (5); T-119, T-120, T-121 `instance::*` | `tests/Barbican.Core.Tests/Compose/Recipients/{RecipientTextTests,ChipSourcesTests}.cs`, `tests/Barbican.Core.Tests/Mailto/MailtoTests.cs`, `tests/Barbican.Core.Tests/Instance/InstanceHandoffTests.cs`, `tests/Barbican.Presentation.Tests/Compose/ComposerDockTests.cs` |
| editor-view (0) | none: Rust's `rte/layout.rs` has no tests; its behaviour is checked by the layout tests, the S3 gate and the automation probes | |

Counts: 26 + 12 + 21 = 59, plus the 7 `rte::model` rows already ported = 66. **Assertions that changed** (D- entries, all in
`spec/decisions.md` under "M3 decisions"): D-240 `utf16_ranges` (indices instead of bytes), D-241 typed inputs instead of JSON and
thread-locals, loopback TCP as the single instance mechanism, D-242 schedule takes a zone, D-243 the Windows checker test, D-244 renamed
types, D-245 contacts source. The `rte::editor` tests that read time (undo grouping within 1200 ms) use `EditorSession(doc, TimeProvider)`.

**Rows that are not unit tests but must be covered** (the Rust code has them without a test; port them with C# tests): the spell
cache (B-COMP-065), the send warnings (`SendWarnings`), `ComposeSettings.Read` defaults, `DocHtml.ImportWithQuotes` of a draft with a
signature, `DefaultMailApp` desktop-entry quoting. They are in the stream acceptance lists of §6.

### 2.2 JMAP requirements (J-): all owned by send-pipeline

J-004 (account-level `maxDelayedSend`/`submissionExtensions`: `pending` → done), J-015 (creation ids `#d`: `deferred(M3)` → done),
J-034 (upload percent-encoding: `partial(M3)` → done), J-036 (Blob/upload; Blob/copy only for a forward from another account:
decide in §9), J-061 (EmailSubmission with `onSuccessUpdateEmail`), J-062 (explicit envelope with Bcc when HOLDFOR needs it),
J-063 (FUTURERELEASE, HOLDFOR, the 30-day Stalwart advertisement clamped to 7 days: `DelayedSend` exists in M1), J-064 (`undoStatus:
canceled`, message back to Drafts), J-065 (`onSuccessDestroyEmail` is `n/a`, kept Sent copy), J-066 (`EmailSubmission/query`
undoStatus pending for the Scheduled view). Each row moves to `done` (or `partial(M<k>)` with what is left) with `cs_test` naming the
test, Bulwark and Stalwart references re-verified (`<stalwart-zip>/crates/jmap/src/{submission/set,email/set,blob/upload,mailbox/set}.rs`).

### 2.3 Interactions (I-): 42 rows tagged M3

Owner = the stream that builds the visible control; a data stream supplies the view model. Rows a stream closes need evidence (a
test, an automation script, or a screenshot path with sample data); everything else is `deferred(M<k>)` with a reason.

| Owner | Rows |
|---|---|
| composer-ui (28) | I-218, I-224 (draft banner and draft card Edit / Discard through `IComposerService`), I-234, I-235, I-236 (close, Esc, Discard, save-or-discard dialog), I-238 (save status), I-239 (Send, Ctrl+Enter, Ctrl+S), I-240 (send arrow menu), I-241 (schedule dialog, Ctrl+Shift+Enter, Reschedule; view over send-pipeline's view model), I-242 (drop overlay), I-243 (From select), I-244 (sub-address helper, From override), I-246, I-249 (Cc/Bcc toggles and keys), I-247, I-248 (autocomplete, chips), I-252 (attachment chip: remove, cancel upload; rename, edit, preview are M5), I-253 (attach files), I-255, I-256 (templates: insert, `t` key, save as template, placeholder fill, picker search; view over send-pipeline's view model), I-257 (Plain text, Read receipt, Tags menu), I-258 (Priority and Read receipt; DSN, Require TLS, Encrypt, Classification deferred), I-259 (send warnings dialog), I-260 (external recipients confirm), I-262 (reply-type menu), I-281 (continue draft banner), I-283 (mailto account picker), I-286 (floating compose button) |
| editor-view (8) | I-250 (body, right-click, cut/copy), I-261 (@mentions), I-263 (formatting, lists, align, link, clear, undo, tables, paste/drop images, resize), I-264 (size, colour, highlight, table grid, picture), I-265 (RTL, alt text, cell colour, overflow, Alt+F10 toolbar keys, image drag), I-266 (image from URL), I-267 (link dialog validation), I-268 (plain-text list paste) |
| send-pipeline (1) | I-282 (undo-send toast with "Send now", scheduled Cancel, "Moved to Drafts" Open, through `IToastService`) |
| deferred | I-245 address-book picker (M5, contacts), I-251 selection plugin actions (M6b; the AI rewrite rows are M6 through `IRewriteProvider`), I-254 attach from Files (M5), I-284 mailbox share dialog (M6), I-394 file drop zones outside the composer (M5), I-268 org disclaimer preview (M7, admin setting), the DSN/Require TLS/Encrypt/Classification part of I-258 (M10) |

### 2.4 Plugin hooks (25 rows with `milestone = M3` in `spec/plugins/hooks.csv`)

Each stream gives its hooks a call site through `IHookBus` and sets `cs_call_site` / status in the CSV for its own rows only (row-level
edits merge cleanly; run `python tools/gen-hooks.py --check`, and if it asks for a regeneration the integrator regenerates
`HookNames.g.cs`). Payload types are new files under `src/Barbican.Core/Plugins/Payloads/` named after the hook, one file per type.
Intercepts run before the action and can stop it; `onBeforeEmailSend` and `onComposeSend` are fail-closed with a 10 minute budget
(R2-10: the budgets and flags exist since M2-unified).

| Owner | Hooks |
|---|---|
| send-pipeline (15) | onBeforeDraftAutoSave, onDraftAutoSave, onBeforeEditDraft, onBuildQuoteHeader, onBeforeComposeOpenToReply, onBeforeComposeOpenToReplyAll, onBeforeComposeOpenToForward, onBeforeComposeOpenToForwardAsAttachment, onBeforeEmailSend, onTransformOutgoingEmail, onComposeSend, onAfterEmailSend, onBeforeAttachmentUpload, onBeforeBlobUpload, onAfterAttachmentUpload |
| composer-ui (10) | onBeforeReply, onBeforeReplyAll, onBeforeForward, onBeforeCompose, onComposerOpen, onComposerClose, onDraftChange, onRecipientChipsChange, onMailtoIntercept, onComposeFromLink |

`HookCallSiteTests` gets one test per owner (like M2): every row of the owner has a call site that a FakeJmap-driven test reaches.

### 2.5 Behaviour rows (B-), live checks (L-), gaps (G-), settings

- **B-:** every stream marks the rows of its area `ported` or `deferred(M<k>)` in the same commit: `spec/behaviours/compose.md`
  B-COMP-001..069 (send-pipeline 001..018, 038; composer-ui 019..037; editor-core 039..068 minus layout; editor-view 069),
  `spec/behaviours/rte-editor.md` B-RTE-001..034 (editor-core 001..019, 023..025, 030; editor-view 020..022 as noted in §6.3,
  026..029, 031..034).
- **L- (run in the integrate stage, §7):** L-002..L-006 (schedule, Scheduled view, reschedule, cancel and edit, Mod+Shift+Enter),
  L-007 (autosave after a chip from autocomplete, no duplicate drafts), L-008 (cold-start Esc leaves no draft), L-009 (stray
  "(no subject)" draft: watch for it), L-018 (From override arrives as sent), L-046 (both styles on the composer footer: screen only).
- **G-:** G-054 (external confirm: `SendWarnings` + composer-ui dialog), G-058 (`cid:` images in a reopened draft: send-pipeline
  re-checks and writes the result in the row), G-063 (mentions and RTL settings are wired: editor-view; `recipientMentionsEnabled`,
  `mentionAddsRecipientTo`, `rtlEditingSupport`), G-075 (@mentions: editor-view with composer-ui's `IMentionSource`), G-076 (attach
  from Files: `deferred(M5)`, Files is M5), G-091 (IME: built and unit-tested up to the hidden `TextBox`; the real-IME check is
  "unproven, needs a human" in the report).
- **Settings read** (`spec/settings.csv`, status wired in Rust; each owner sets `csharp_read_by`):

| Stream | Keys |
|---|---|
| send-pipeline | sendDelaySeconds, autoSelectReplyIdentity, replyIdentityMatch, subAddressDelimiter, signatureNewMailEnabled, signatureReplyForwardEnabled, signaturePosition, signatureSeparatorEnabled, plainTextMode (default for a seed), timeFormat (labels) |
| editor-core | writingLanguage, plainTextMode (`EditorSession.Plain`) |
| editor-view | rtlEditingSupport, recipientMentionsEnabled, mentionAddsRecipientTo, writingLanguage (spell on a language), plainTextMode (hides the toolbar) |
| composer-ui | sendConfirmation, attachmentReminderEnabled, emptySubjectWarningEnabled, placeholderWarningEnabled (the dialog's checks; the rules are `SendWarnings`), replyUnderMessage (`ComposerDock`), composePlacement, plainTextMode (the option), quickReplyEnabled is **not** built (the reader's quick reply box is M10: not an I- row of M3) |

## 3. Parallel work without shared files

### 3.1 File ownership

Every file has exactly one owner for the whole milestone. Folders go with all files below them. Files not listed stay with the
integrator (frozen): `Barbican.slnx`, `Directory.*`, `global.json` (except the Uno move, §6.3), `AppComposition.cs`,
`CompositionGraphTests.cs`, `ShellPage.xaml` (except composer-ui's change in §6.4), every M2 file.

| Owner | Source | Tests |
|---|---|---|
| **send-pipeline** | `src/Barbican.Core/Compose/*` (the files directly in the folder and `Schedule/`, **not** `Recipients/`); `src/Barbican.Core/Jmap/Submission/*` (additions only: new files; the M1 files are frozen unless a J- row needs a fix); `src/Barbican.Core/Plugins/Payloads/` (its hook payload files); `src/Barbican.Presentation/Compose/Pipeline/**` (view models: send coordinator, schedule dialog, template picker, Scheduled view source and banner source); its lines of `SendPipelineRegistration.cs` | `tests/Barbican.Core.Tests/Compose/*.cs` (not `Recipients/`), `tests/Barbican.Presentation.Tests/Compose/Pipeline/**`; FakeJmap files `tests/Barbican.FakeJmap/FakeApi.SubmissionHold.cs`, `FakeApi.Metadata.cs`, `FakeSeed.M3.cs` (new) |
| **editor-core** | `src/Barbican.Core/Editor/**` (including `Spell/`); `src/Barbican.Platform.Windows/Spelling/**` and its lines in `NativeMethods.txt`; `src/Barbican.Platform.Linux/Spelling/**` only for the empty default; `EditorCoreRegistration.cs` | `tests/Barbican.Core.Tests/Editor/**`, `tests/Barbican.Platform.Tests/WindowsSpellCheckerTests.cs`, `tests/fixtures/editor/**` |
| **editor-view** | `src/Barbican.Editor.Layout/**`; `src/Barbican.Presentation/Editor/**`; `src/Barbican.App/Views/Editor/**` (the control, overlays, popovers, menus), `src/Barbican.App/Themes` is generated (never edit; tokens come from `design/`); `src/Barbican.App/Composition/Features/EditorViewRegistration.cs`; `src/Barbican.Automation/CommandRouter.Editor.cs` (new, editor probes); `Barbican.App.csproj`, `global.json`, `Directory.Packages.props` for the Uno 7 move and the Skia packages **only** (§6.3) | `tests/Barbican.Editor.Layout.Tests/**`, `tests/Barbican.Presentation.Tests/Editor/**` |
| **composer-ui** | `src/Barbican.Core/Compose/Recipients/**`; `src/Barbican.Core/Mailto/**`; `src/Barbican.Core/Instance/**`; `src/Barbican.Platform/Mailto/**` and `src/Barbican.Platform.{Windows,Linux}/Mailto/**` (the executors of the registration plans); `src/Barbican.Presentation/Compose/*` (the files directly in the folder, **not** `Pipeline/`); `src/Barbican.App/Views/Compose/**`; `src/Barbican.App/Composition/Features/ComposerRegistration.cs`; its lines of `M3PresentationRegistration.cs`; `ShellPage.xaml` and `ShellPage.xaml.cs` for the composer host only; `src/Barbican.Core/Plugins/Payloads/` (its hook payload files) | `tests/Barbican.Core.Tests/Compose/Recipients/**`, `tests/Barbican.Core.Tests/Mailto/**`, `tests/Barbican.Core.Tests/Instance/**`, `tests/Barbican.Presentation.Tests/Compose/*.cs`, `tests/Barbican.Platform.Tests/Mailto*.cs` |

`src/Barbican.Presentation/Composition/M3PresentationRegistration.cs` has two methods: `AddBarbicanEditorViewModels` (editor-view)
and `AddBarbicanComposerViewModels` (composer-ui); each stream edits only its method. `tests/Barbican.Core.Tests/Barbican.Core.Tests.csproj`
and the other test projects are frozen: no new packages are needed by design (snapshot tests are plain files, §6.2).

### 3.2 Shared spec files

`spec/rust-tests.csv` (`--write`), `spec/interactions.csv`, `spec/jmap.csv`, `spec/settings.csv`, `spec/gaps.csv`, `spec/live-checks.md`,
`spec/behaviours/*.md`, `spec/plugins/hooks.csv`: edit only your own rows (one line per row merges cleanly). `docs/FABLE-REVIEW.md`:
append under `## M3 stream: <key>` (a section already exists for the plan itself).

## 4. Contracts (compiling code in this commit set)

All stubs below are in the repository now, build with warnings as errors, and are wired into the composition (§4.7). A stub that
throws does so with `PortPendingException("Type.Member")`; the red tests prove the stubs are reached.

### 4.1 Editor core (owner editor-core; consumed by editor-view, send-pipeline, composer-ui)

`src/Barbican.Core/Editor/` (the model files from the S3 spike are unchanged: `Doc`, `Block`, `BlockKind`, `Run`, `Marks`, `Mark`,
`Pos`, `Sel`, `Align`, `ImageData`, `BlockSpan`, `TextOffsets`):

| Type | What it is |
|---|---|
| `IEditorSession` | The whole command surface of Rust's `Editor`: `Doc`, `Sel`, `Stored`, `GoalX`, `Version`, `Plain`, `CanUndo`/`CanRedo`, `InlineImages`, `Changed`; `Load`, `Checkpoint(EditKind)`, `BreakGroup`, `Undo`/`Redo`, `SetCaret`, `SelectRange`, `SelectAll`, `SelectedText`/`SelectedBlocks`, `SelectWord`, `SelectBlock`, `CurrentMarks`, `InsertText`, `Enter(shift)`, `Backspace(word)`, `DeleteForward(word)`, `ToggleMark`, `SetColor`/`SetHighlight`/`SetSize`, `SetBlock`, `SetAlign`, `Indent`, `ClearFormat`, `LinkRange`/`CurrentLink`/`SetLink`, `InsertBlocks`/`InsertImage`/`InsertRule`/`InsertTable`, `ApplyTable(TableOp)`, `CellTab`, `ReplaceText(text, whole)`, `ResizeImage`, `ReplaceDoc`, `ReplaceRange`, `ToggleQuoted`, `SetImageUploaded(cid, blobId, mime, name)`. Layout-dependent cursor movement (up/down, home/end of a visual line) is **not** here: the view computes the target position with the layout and calls `SetCaret`/`SelectRange`, keeping `GoalX` (B-RTE-033). |
| `EditorSession` | The implementation (stub today; the 10 `rte::editor` red tests call it). Constructor `(Doc, TimeProvider?)`. History is 300 steps; typing and deleting merge within 1200 ms by the injected clock. |
| `EditKind`, `TableOp` | Enums (`TableOp`: RowAbove, RowBelow, ColLeft, ColRight, DelRow, DelCol, DelTable, ToggleHeader). |
| `DocHtml` | Static: `Import`, `ImportText`, `ImportWithQuotes`, `Export`, `ToText`, `Esc`, `DecodeEntities`, `NormColor`, `CleanQuotedHtml`, `HtmlText`, `EditableEnd`, `Source`, `Pretty`, `ApplySource` (12 red tests). |
| `IEditorSessionFactory`, `EditorSessionFactory` | `Create(Doc, bool plain)`; the default builds an `EditorSession`. Real (not a stub). |
| `EditorCoreRegistration.AddBarbicanEditorCore()` | `ISpellCheckerFactory` (default: none), `ISpellEngine` (default: none), `IEditorSessionFactory`. |

### 4.2 Spell checking (owner editor-core; consumed by editor-view)

`src/Barbican.Core/Editor/Spell/`: `SpellRange(Start, Length)` (UTF-16), `ISpellChecker` (`Check`, `Suggest`, `Add`, `Ignore`),
`ISpellCheckerFactory` (`UserLocale`, `Create(languageTag)`: null when there is no dictionary), `SpellText` (static stub:
`LanguageTag`, `ToTextRanges`, `SkipWord`, `Token`: 3 red tests), `ISpellEngine` (what the view asks per paragraph:
`IsAvailable`, `Misspelled(text)`, `Suggestions`, `Add`, `Ignore`, `Reload`; cached per text), and real defaults `NoSpellCheckerFactory`,
`NoSpellEngine`. `src/Barbican.Platform.Windows/Spelling/WindowsSpellCheckerFactory.cs` is the stub of the COM checker (1 red test).
**Linux has no engine in M3** (Hunspell is M9, PORT-PLAN §5): the default `NoSpellEngine` stays registered there and the composer
shows no spelling marks; the plan does not hide this.

### 4.3 Send pipeline (owner send-pipeline; consumed by composer-ui)

`src/Barbican.Core/Compose/`, namespace `Barbican.Core.Compose`:

| Type | Contract |
|---|---|
| `ComposeKind` | New, Reply, ReplyAll, Forward, ForwardAsAttachment, EditDraft |
| `ComposeRequest(AccountId, Kind)` + `SourceEmailId`, `Prefill`, `Plain` | What composer-ui asks for |
| `ComposePrefill` | To, Cc, Bcc (field text), Subject, Body (plain text): a mailto link or a plugin's `onBeforeCompose` |
| `ComposeSeed` | What a composer starts with: fields, `Doc Body` (quote and signature included), `IdentityId`, `FromOverride`, `Attachments`, `DraftId`, `SourceEmailId`, `InReplyTo`, `References`, `SignatureTexts`, `IsSaved` |
| `ComposeDraft` | What the composer holds: `AccountId`, `Kind`, `DraftId`, `SourceEmailId`, `IdentityId`, `FromOverride`, `To`/`Cc`/`Bcc` (field text is the source of truth), `Subject`, `Doc Body`, `PlainText`, `Attachments`, `InReplyTo`, `References`, `ReadReceipt`, `Priority` |
| `DraftAttachment`, `AttachmentUpload`, `MessagePriority` | Attachment record (name, type, size, blob id, cid), the bytes to upload, Normal/High/Low |
| `IComposeSeeder.SeedAsync(ComposeRequest)` | Prepares a composer (reply recipients and subject, quote with `cid:` images resent, identity, signature; draft reopen with inline images restored: G-058) |
| `IDraftService` | `SaveAsync(ComposeDraft)` (one `Email/set` creates the new draft and destroys the previous one: two saves never leave two drafts), `DiscardAsync` |
| `IMailSender` + `SendOptions(UndoSeconds, ScheduledAt)` + `SendReceipt` | `SendAsync` (explicit envelope when held, HOLDFOR, `onSuccessUpdateEmail`), `UndoAsync` (returns the seed to reopen), `SendNowAsync` |
| `IScheduledSends` + `ScheduledMessage` | `ListAsync` (EmailSubmission/query pending), `RescheduleAsync`, `CancelAsync` |
| `IAttachmentUploader` | `UploadAsync(accountId, AttachmentUpload, existing)`: the gates (account upload limit, per-message total), the plugin hooks, the blob id |
| `IIdentityService` + `ComposeIdentity` | Identities with text and HTML signatures |
| `ITemplateStore` + `MailTemplate`; `TemplateTombstone.Mark` | List, create, delete with tombstone and `ifInState` (3 tries) |
| `ComposeSettings` (**real**, `Read(PrefsSnapshot)`) | The 16 composing settings with Rust's defaults |
| `ComposeRules` (`IsBlank`, `DeliveryAddresses`, `FindPlaceholders`), `CidHtml` (`Of`, `InlineParts`), `ReplyFrom`, `ReplyIdentityChoice`, `ReplyIdentity.Resolve` | Pure rules (stubs, red tests) |
| `SendWarnings.Check(draft, settings, ownDomains)` + `SendWarning`, `SendWarningKind` | Empty subject, unfilled placeholders, missing attachment, external recipients (stub; no red test yet: send-pipeline ports it as its own tests) |
| `AutosaveState`, `Queued`, `Leftover` | The autosave bookkeeping of one composer (stub, 3 red tests) |
| `Schedule/ScheduleRules`, `SchedulePreset` | Presets, parsing, validation, labels (stub, 3 red tests) |
| `SendPipelineRegistration.AddBarbicanSendPipeline()` | Registers the default (stub) class of each interface with `TryAddSingleton`; send-pipeline replaces the registrations in this file (constructor dependencies: `IMailWriterFactory`/the session source of M2, `IHookBus`, `TimeProvider`, `IPrefsStore`) |

The view models that sit between these services and composer-ui's views are **specified here and written by send-pipeline in its
first step** (so composer-ui can build XAML against them early; names are fixed, members as listed, extra members are free):
`Barbican.Presentation.Compose.Pipeline.SendCoordinator` (`RunAsync(ComposeDraft, SendMode)`: pending-image check, address check,
warnings, then `IMailSender`; raises `WarningsRaised(IReadOnlyList<SendWarning>)` and expects a `Confirm`/`Cancel` back; shows the undo toast with
Undo and Send now through `IToastService`; the Moved-to-Drafts toast with Open; I-282), `ScheduleDialogViewModel` (mode Compose or Reschedule, `Presets`, `Date`,
`Time`, `Error`, `Choose(int)`, `ChooseTyped()`, `Cancel()`), `TemplatePickerViewModel` (`Items` filtered by a search text over name, subject and
category; `Insert(MailTemplate)` returns the blocks and subject; `SaveAs(...)`; `Delete`), `ScheduledBannerSource : IReaderBannerSource` ("Scheduled to send
at …", Reschedule, Cancel and edit, Cancel send) and the Scheduled folder's list source.

### 4.4 Recipients, mailto, instance, composer service (owner composer-ui; consumed by send-pipeline, editor-view, the other streams)

| Namespace and type | Contract |
|---|---|
| `Barbican.Core.Compose.Recipients`: `Recipient` (value equality including group members), `RecipientGroup`, `Suggestion`, `SuggestionSources` | The recipient field model and autocomplete input |
| `RecipientText` (static stub, 22 members, 9 red tests incl. T-061) | Port of `recipients.rs`: IDN, validation, mailbox split, `SplitRecipients` with group syntax, `ParseRecipient`/`ParseList`/`FormatList`, `Expand`, `ExpandForSend`, `InvalidAddresses`, `SplitPasted`, `Suggest`, `MatchRank`, `DefaultSuggestionIndex`, `Initials` |
| `ChipSources` (static stub, 3 red tests) | `BlurCommits`, `SourcesFromCards`, `RecentFromSent` |
| `IRecipientSources` / `EmptyRecipientSources` (**real default**) | `LoadAsync(accountId)`: contacts, groups, recent. Default empty; composer-ui registers the Sent-based source, M5 adds contacts (D-245) |
| `Barbican.Core.Mailto`: `MailtoFields`, `MailtoLink` (stub, T-170/171/174), `DefaultMailApp` + `RegistryWrite` (stub, T-172/173) | RFC 6068 parse with the 200/998/64 KiB limits; the Windows registry plan, the `.desktop` entry, `CommandMatches`; **plans only, nothing executes** |
| `Barbican.Core.Instance`: `HandoffEndpoint`, `InstanceHandoff` (stub, T-119..121) | Loopback TCP hand-off: token line, URL line, greeting check, 8 connections, 256 KiB, deadline |
| `Barbican.Presentation.Compose.IComposerService` + `ComposerService` (stub) | `IsOpen`, `OpenChanged`, `OpenAsync(ComposeRequest)`, `OpenSeedAsync(ComposeSeed)`, `TryCloseAsync` (the leave guard). **Everyone who opens a composer uses it**: the reply/forward commands (`CommandIds.Reply`, `ReplyAll`, `Forward`, `ForwardAsAttachment`, `Compose`, `NewMessageTo`), the Scheduled view's Cancel and edit, undo send, mailto |
| `Barbican.Presentation.Compose.ComposerDock.ShouldDock` (stub, T-064) | The reply docking rule |

### 4.5 Editor view (owner editor-view; consumed by composer-ui)

`src/Barbican.Presentation/Editor/`: `IEditorViewModel` (`Session`, `Toolbar`, `SourceOpen`, `ToggleSource`, `RequestFocus`,
`Mentions`, `FocusRequested`, `ToolbarChanged`), `ToolbarState` (record: marks, link, colour, size, block kind, align, undo/redo, in
table, on image, has selection, on misspelt word), `IEditorViewModelFactory` + `EditorViewModelFactory` (stub), `IMentionSource` +
`MentionCandidate` (implemented by composer-ui: candidates from To/Cc/Bcc, and the add-to-field action), `IRewriteProvider` +
`NoRewriteProvider` (**real default**: the "Rewrite with AI" rows of MENU-RTE are disabled in M3; the assistant is M6).
composer-ui never touches the control: it creates the view model, puts the `EditorView` in its layout through a `ContentPresenter`,
reads `Session` for the document and watches `Session.Changed` for dirty keys and `InlineImages` that still need an upload.

### 4.6 What was **not** coded and why

The view models of §4.3 (`SendCoordinator`, `ScheduleDialogViewModel`, `TemplatePickerViewModel`, `ScheduledBannerSource`) are agreed in prose
because their members depend on `IToastService`/`IDialogService` details of M2 that send-pipeline reads first. The composer
view model, the chips view model and the layout contracts inside the editor view are internal to their streams.

### 4.7 Composition (DI skeletons, compile and resolve today)

`AppComposition.Build` now calls, after the M2 features: `AddBarbicanSendPipeline()` (`SendPipelineRegistration`, Core),
`AddBarbicanEditorCore()` (`EditorCoreRegistration`, Core), `AddBarbicanEditorView()` (`Features/EditorViewRegistration.cs`, App; calls
`AddBarbicanEditorViewModels()` of `M3PresentationRegistration`), `AddBarbicanComposer()` (`Features/ComposerRegistration.cs`, App; calls
`AddBarbicanComposerViewModels()`). `CompositionGraphTests.Compose` registers the four portable parts, so "every registered service
resolves" covers them. Rules: default registrations use `TryAddSingleton`; a stream that implements a service changes the registration in
its own file (`Replace` when the default was registered by another feature); stub classes have no constructor parameters, so the graph
test passes while they exist. A stream that adds a dependency to a replaced class keeps the graph test green.

## 5. Order, sync points and merges

All four start on day one, from `m3`. **Merge early:** a stream merges into `m3` after every green step (build + tests on Windows, Linux
VM when it touched shared Core), and every other stream merges `m3` into its branch at the start of each step. The sync points:

| Point | Who | What lands in `m3` | Who needs it |
|---|---|---|---|
| **S0** | editor-view | The **Uno 7 move** (§6.3 step 0): `global.json`, packages, `Barbican.App.csproj` referencing `Barbican.Editor.Layout`, both heads building and the M1/M2 smoke runs green | everyone who builds the App head: composer-ui and editor-view; Core-only work of A and B does not wait |
| **S1** | editor-core | Step 1: `EditorSession` basics (T-321..T-325, T-329, T-330) and `DocHtml.Export`/`Import`/`ToText` (T-331..T-338) | editor-view (paints and drives a real session instead of a fake); send-pipeline (draft HTML and text parts) |
| **S1** | send-pipeline | Step 1: pure rules (all 12 red tests), `ComposeSettings` callers, the four view-model files named in §4.3 | composer-ui (send flow, schedule dialog, template picker views) |
| **S2** | editor-core | Step 2: links, tables, `ImportWithQuotes`, `CleanQuotedHtml`, source view (`Source`/`ApplySource`), `ISpellEngine` with the cache | editor-view (source view, spell marks, table and link UI) |
| **S2** | send-pipeline | Step 2: `IDraftService`, `IMailSender`, `IAttachmentUploader` real, with the FakeJmap submission arms | composer-ui (replaces its fakes with the real services) |
| **S3** | editor-view | The `EditorView` control hosted in a test page with a real `EditorSession` (typing, caret, selection, paint, hit test) and a real `EditorViewModelFactory` | composer-ui (embeds it; until then composer-ui tests use a fake `IEditorViewModelFactory` and an automation-only plain `TextBox` that is **not** shipped) |
| **S3** | send-pipeline | Step 3: seeder (reply, reply-all, forward, draft reopen), scheduled sends, templates | composer-ui (compose commands, Scheduled view actions) |

What **C needs from B** (editor-view from editor-core): the command semantics of `IEditorSession` (S1), `DocHtml.Source`/`ApplySource` (S2),
`ISpellEngine` (S2). Until S1, C codes against `IEditorSession` with a local fake and paints `Doc` literals (the layout engine already
takes a `Doc`).
What **D needs from A**: `ComposeSettings` (real today), `ScheduleRules` and `SendWarnings` (S1), the pipeline view models (S1), real
`IDraftService`/`IMailSender`/`IAttachmentUploader` (S2), `IComposeSeeder` (S3). Until then D works against fakes in
`tests/Barbican.Presentation.Tests/Compose/` that implement the interfaces.
What **D needs from B**: nothing directly (A turns the `Doc` into HTML); the `Doc` model exists today. What **D needs from C**: the
view model (S3). What **A needs from B**: `DocHtml.Export` and `ToText` (S1) for the draft parts; until then A tests with a fake that
returns fixed strings through a one-line seam (`Func<Doc, string>` fields in the draft service constructor are acceptable).
What **C needs from D**: `IMentionSource` (a fake in C's tests). What **C needs from A**: nothing.

**Merge order into `m3` for the milestone close:** editor-core, send-pipeline, editor-view, composer-ui (each already merged
incrementally); the integrator then runs §7.

## 6. Stream briefs

Common to all: read CLAUDE.md, `.local/accounts.md`, this plan §1 to §5 and the PORT-PLAN sections named in your brief; create the worktree
`<downloads>/wt-m3-<key>` on branch `m3-<key>` from `m3`; push at creation and after every commit; Fable section `## M3 stream: <key>`.

### 6.1 `send-pipeline` (letter A, Sonnet)

**Scope.** Everything between "the composer holds a `ComposeDraft`" and "the server holds the right thing", plus the rules around it.
No XAML. Reads the Rust `src/compose.rs`, `autosave.rs`, `schedule.rs`, `templates.rs`, `jmap.rs` (send_message, cancel_submission,
reschedule_submission), Bulwark `components/email/email-composer.tsx`, `lib/jmap/client.ts` (createDraft/updateDraft, sendEmail,
cancelEmailSubmission, rescheduleEmailSubmission, uploadBlob :6662), `lib/schedule-presets.ts`, `lib/reply-identity.ts`,
`components/email/message-actions.ts`, Stalwart `crates/jmap/src/{submission/set,email/set,blob/upload,mailbox/set}.rs`.

**Owned:** §3.1 row "send-pipeline". **T rows:** T-015, T-016, T-017, T-060, T-062, T-063, T-065, T-066, T-253, T-254, T-255, T-285.
**I rows:** I-282. **J rows:** J-004, J-015, J-034, J-036, J-061..J-066. **Hooks:** 15 (§2.4). **Settings:** §2.5.

**Steps (each ends green and merged to `m3`):**
1. *Pure rules and the view models.* Make the 12 red tests pass (`AutosaveState` with the clock and `Queued`, `ComposeRules`, `CidHtml`,
   `ReplyIdentity.Resolve` with the sub-address delimiter, `ScheduleRules`, `TemplateTombstone.Mark`). Implement `SendWarnings.Check` with
   its own tests (the words of Rust `pre_send_warning` plus external recipients by identity domain, G-054). Write the four view-model
   files of §4.3 against `IToastService`, `IDialogService`, `IReaderBannerSource`. Mark the B- and J- rows you finished.
2. *Drafts, send, attachments.* FakeJmap first (`FakeApi.SubmissionHold.cs`: HOLDFOR parsing and clamp, `undoStatus` pending, canceled and
   final, a release job on a `FakeTimeProvider`, `forbiddenFrom`; `FakeApi.Metadata.cs`: Inbox metadata with `ifInState` and
   `stateMismatch`; `FakeSeed.M3.cs`: identities with signatures, a reply thread with an inline image). Then `DraftService` (create the new
   draft with `$draft` in Drafts and destroy the previous in one `Email/set`; `DraftId` returned; discard), `MailSender`
   (`Email/set` + `EmailSubmission/set` with creation ids `#d`, explicit envelope with Bcc only when held, `onSuccessUpdateEmail` that
   drops `$draft` and moves to Sent, HOLDFOR from `SendOptions` clamped by `DelayedSend.Limit`, From override and the `forbiddenFrom` retry
   of `SubmissionRules`, `$answered`/`$forwarded` on the source), `UndoAsync` (cancel, the message returns to Drafts, return a seed with
   its inline images restored), `SendNowAsync` (cancel and resubmit without a hold: record in §9), `AttachmentUploader` (gates: the
   account's `maxSizeUpload`, a total cap from `maxSizeAttachmentsPerEmail` when advertised, the three plugin hooks), the HTML and text
   parts from `DocHtml.Export`/`ToText` (until S1 of editor-core, through the `Func<Doc, string>` seam).
3. *Seeder, scheduled sends, templates.* `ComposeSeeder` (reply: recipients from Reply-To or From, "Re:" once; reply-all: To+Cc minus own
   identities, deduplicated, case-insensitive; forward: "Fwd:", the original's attachments, forward as attachment as an `.eml` part;
   the quote HTML cleaned by `DocHtml.CleanQuotedHtml`, `Quoted` block with `Cite` true for replies and false for forwards; the
   quoted `cid:` parts resent inline with their blob ids (`CidHtml.InlineParts`); `onBuildQuoteHeader`; the signature placement from
   `signaturePosition`, `signatureSeparatorEnabled` and the identity's text or HTML signature, into the `Doc` through `DocHtml.Import`;
   draft reopen through `DocHtml.ImportWithQuotes` with `cid:` images restored, G-058), `ScheduledSends` (`EmailSubmission/query`
   `undoStatus: pending` + `Email/get` in chunks; reschedule with `ReschedulePlanner`'s fallbacks; cancel), `TemplateStore` (Bulwark's
   metadata shape under `org.bulwarkmail.template.<id>`; list sorted by lower-case name; create; delete with the tombstone and the
   3-try `ifInState` loop), the Scheduled folder's list source and `ScheduledBannerSource`.

**Acceptance tests** (all FakeJmap-based, no network): the 12 ported tests green; two saves in a row leave one draft; a send with
`UndoSeconds` 5 creates a submission with `HOLDFOR` 5 and an explicit envelope that contains the Bcc, and a send with 0 creates none and
leaves no explicit envelope; a server without FUTURERELEASE sends at once and `CanUndo` is false; `maxDelayedSend` above the Stalwart
30 days clamps to 7; `UndoAsync` ends with the message in Drafts with `$draft`; `forbiddenFrom` retries once with the identity address and
reports the rewrite; reply and reply-all recipient tables (own address removed, Reply-To honoured, duplicates folded); forward
carries the attachments; a quoted inline image stays inline in the reply and in a reopened draft; `ComposeSettings.Read` defaults equal
Rust's; `SendWarnings` for each kind and with each setting off; attachment over the limit refused before any request; the three
attachment hooks and `onBeforeEmailSend` (fail-closed: a handler that throws stops the send); reschedule falls back to restoring the draft
when the recreate fails; templates: create, sorted list, delete twice with a `stateMismatch` in between keeps the other tombstones.
`HookCallSiteTests` for the 15 hooks.

**Live script (integrate stage):** send to itself with a 5 s hold, undo, send again; schedule 1 h ahead, reschedule, cancel and edit,
cancel; a template round trip; From override send arrives with the override (L-018). Cleans up.

### 6.2 `editor-core` (letter B, Sonnet)

**Scope.** The editing engine without a view. Port of Rust `src/rte/model.rs` (done), `editor.rs`, `html.rs`, `spell.rs`
(PORT-PLAN §3.8, ADR 0006 "Decision": UTF-16 offsets, grapheme caret steps through `TextOffsets`).

**Owned:** §3.1 row "editor-core". **T rows:** T-321..T-330, T-331..T-342, T-350..T-353 (26). **B rows:** B-RTE-001..019, 023..025, 030,
B-COMP-039..068. **Settings:** `writingLanguage`, `plainTextMode`.

**Steps:**
1. *`EditorSession` and export.* Port `editor.rs` command by command onto `IEditorSession` (the Rust file is 700 lines of code and 150 of
   tests): history (300, grouping by `TimeProvider`), selection, `InsertText` with the markdown block rules (`# `, `- `, `1. `, `> `, ``` ``` ```,
   `---`) and inline rules (`**`, `*`, `` ` ``) off in `Plain`, `Enter` (list continuation, empty item leaves the list, heading to
   paragraph), `Backspace`/`DeleteForward` by grapheme and by word, marks and stored marks, `SetBlock`, `Indent`, `ClearFormat`,
   images (the paragraph after an image, B-RTE-011), `ReplaceText` (keeps the quote), `ResizeImage` (one undo step per drag),
   `SetImageUploaded`. Then `DocHtml.Export` (Bulwark's exact inline styles: paragraphs 4 px, headings, lists, framed code, quote bar, link
   colour `#2563eb`, code style of Rust `CODE_STYLE`, the wrapper div; **keep Rust's output byte for byte**: the Rust code is the oracle),
   `ToText`, `Esc`, `Import`/`ImportText` over an **AngleSharp** DOM walk (`AngleSharp` is already a Core dependency): must pass the same
   six import tests and the round trip, never throws on any HTML, drops `style`, `script`, comments, collapses whitespace like the
   Rust importer, converts `&nbsp;` to a space (T-334 expects it).
2. *Links, tables, quotes, source, spell.* The remaining commands (`SetLink` adds `https://` to a bare host, `CellTab`, `ApplyTable`,
   `InsertTable`), `ImportWithQuotes`, `CleanQuotedHtml`, `HtmlText`, `Source`/`Pretty`/`ApplySource` keeping the uploaded image data, the
   spell layer: `SpellText`, a real `SpellEngine : ISpellEngine` (cache per text with a size bound, skips, `Reload` on a setting change,
   `LanguageTag` from `writingLanguage` and `ISpellCheckerFactory.UserLocale`), registered in `EditorCoreRegistration` with `Replace`, and the
   Windows factory over `ISpellCheckerFactory` COM through CsWin32 (`NativeMethods.txt`: `SpellCheckerFactory`, `ISpellChecker`,
   `ISpellingError`, `GetUserDefaultLocaleName`; unpackaged apps can create the factory). The Windows test returns when the machine has no
   dictionary.

**Acceptance tests** beyond the ports: the 26 red tests green; **snapshot tests without a package** (`tests/fixtures/editor/export/*.html`
expected files; a helper that compares, and rewrites them when `BARBICAN_SNAPSHOT=update`) for ten documents (every block kind, nested
lists, a table with a header row, a quote, an image with width, a code block, the wrapper, entity escaping, a link, coloured and
sized spans); a differential test: every `.html` body in `tests/fixtures/mail` imports without an exception and its text is preserved;
a fuzz test (seeded, 2000 random command sequences over a small document) never breaks `Doc.Fix`'s invariants (an editable last block,
no runs in atomic blocks, cells consecutive) and `Undo` x n restores the original document; surrogate pairs and combining marks never
split by `InsertText`, `Backspace`, `DeleteForward`, `SelectWord`; the spell cache returns the stored result for an unchanged text and
calls the checker once; `SpellText.ToTextRanges` bounds.
**Not B's:** painting, key handling, clipboard (C), the HTML of signatures and templates is imported through `DocHtml.Import` by A.

### 6.3 `editor-view` (letter C, Sonnet)

**Scope.** The control and everything drawn or clicked in the editor, hosted over `Barbican.Editor.Layout` (ADR 0006): `SKXamlCanvas`
on Windows, `SKCanvasElement` on Uno, caret and IME box as XAML overlays, selection painted in Skia behind the text. Rust
`rte/layout.rs` (544 lines) and `rte/mod.rs` (832 lines: refresh, ctx_info, on_key, on_pointer, on_cmd) are the behaviour source;
Bulwark `components/email/rich-text-editor.tsx`, `link-dialog.tsx`, `image-url-dialog.tsx`, `plain-text-paste.ts`,
`recipient-mention.tsx`.

**Owned:** §3.1 row "editor-view". **T rows:** none. **I rows:** I-250, I-261, I-263, I-264, I-265, I-266, I-267, I-268 (paste part).
**G rows:** G-063, G-075, G-091 (built; the real-IME check is unproven). **B rows:** B-RTE-020..022, 026..029, 031..034, B-COMP-069.
**Settings:** `rtlEditingSupport`, `recipientMentionsEnabled`, `mentionAddsRecipientTo`, `writingLanguage`, `plainTextMode`.

**Step 0: the Uno 7 move (first, time-boxed to one working session; merges to `m3` as S0).** `global.json` still pins Uno.Sdk 6.7.49
(SkiaSharp 3.119.2) while `Barbican.Editor.Layout` needs SkiaSharp 4.156.0 (`Directory.Packages.props` lines 53 to 67 withhold the
pins from Uno projects; ADR 0006 "M0 review"). Move `global.json` to the Uno.Sdk 7.x whose Skia is 4.156 (7.0.65 is the version the
spike read), drop the withholding condition, add `ProjectReference Barbican.Editor.Layout` and `SkiaSharp.Views.WinUI` (Windows) to
`Barbican.App.csproj`, fix what the move breaks in M1/M2 views (the owner of a broken file is C for the duration of this step; record
each touched file), run `tools/build.ps1 test`, `tools/auto-run.ps1 smoke` and `tools/linux.ps1 test` + the smoke on the VM. **Fallback
(ADR 0006 "Fallbacks"), taken if the move is not green after the time box:** vendor Topten.RichTextKit into `src/ThirdParty/RichTextKit`
(Apache-2.0: keep LICENSE and NOTICE, update `NOTICE.md`), compile it against the App head's SkiaSharp 3.119.2, keep the Uno SDK, and
add a D- entry. Either way record the decision in §9 of the milestone report.

**Steps after step 0:**
1. *Layout completeness* (`Barbican.Editor.Layout`, headless tests on both OSes): list markers and indents (`Doc.Ordinal`), table grid
   with cell padding and header style, images (decoded `ImageData.Bytes`, width and height, a placeholder while loading), rules, the
   collapsed quote block ("show quoted text" toggle, `ToggleQuoted`), code block box, block spacing identical to the export spacing
   (paragraph 4 px), viewport culling and a per-block layout cache (re-layout of one edited block under 1 ms), BiDi caret affinity at
   direction boundaries (ADR 0006 "Pending"), `TextContrast` 1.6 on the Windows host and 1 on Uno, colour and highlight runs.
2. *The control* (`Views/Editor`): `EditorView` with `SKXamlCanvas`/`SKCanvasElement`, the caret overlay (blink by `DispatcherTimer`,
   no blink under reduce-motion), pointer selection (click, double-click word, triple-click block, shift-extend, drag), keyboard
   (the table of MENU-RTE shortcuts in `spec/menus.md`, arrows by grapheme and visual line with `GoalX`, Home/End, PageUp/Down, Ctrl+arrows
   by word, Tab in cells and lists, Esc dismisses the selection first, B-RTE-029), the hidden `TextBox` IME path with
   `TextCompositionStarted/Changed/Ended` and the preedit drawn inline with RichTextKit's IME underline styles (ADR 0006 section 6),
   clipboard (copy as HTML and text, paste HTML through `DocHtml.Import`, plain text, images from the clipboard and dropped files as
   inline images, "Paste as plain text", plain-text list paste I-268), image resize handles (one undo step per drag), the spell
   marks from `ISpellEngine` (squiggle underline, painted per visible block) and the suggestion rows in the context menu, an
   `AutomationPeer` with a `TextPattern` provider (Narrator).
3. *Overlays and menus* (in-window only, `ShouldConstrainToRootBounds`, never a `Popup` outside the window): the toolbar (marks, size,
   colour, highlight, block kinds, align, lists, link, image, table grid picker, clear, undo/redo, source, RTL when `rtlEditingSupport`),
   the bubble toolbar near a selection, the link popover (Esc closes only the popover) and the link dialog with validation I-267, the
   image-from-URL dialog I-266 and alt text, the table menu and cell colour, the source view (`IEditorViewModel.ToggleSource`), the
   context menu **item by item from MENU-RTE in `spec/menus.md`** through the M2 `IContextMenuService` (an `IMenuProvider` for
   `MENU-RTE`; the "Rewrite with AI" rows come from `IRewriteProvider` and are disabled), @mentions popup from `IMentionSource`,
   Alt+F10 toolbar keys, overflow, plain-text mode (formatting hidden, B-RTE-020).
4. *Automation and gates.* `CommandRouter.Editor.cs`: `editor-open <fixture>`, `editor-type <text>` (goes through the same entry as the
   hidden `TextBox`, never injects OS input), `editor-key <chord>`, `editor-select`, `editor-dump` (document, selection, caret rect,
   spell marks as JSON), `editor-menu`, `editor-latency` (keystroke to painted frame, Windows and Linux). **S3 gate on the actual
   control:** the same paragraph as the XAML `TextBlock` in Segoe UI 15 px side by side at 100, 125 and 150 % and in dark theme on
   Windows, and `SKCanvasElement` next to Uno's `TextBlock` on the VM; captures that are uniform are "unproven", never a pass.

**Budgets** (measured with the probes, written in the milestone report): keystroke to painted frame under 16 ms on a 60-paragraph
draft at 150 %; full paint of the visible blocks under 8 ms; nothing allocates per frame in steady state; no flicker on typing
(`tools/flicker.py`).

**Acceptance tests:** layout tests for every block kind (heights, caret rects, hit tests, selection rectangles) on Windows and Linux;
key-map tests (pure table: chord and state to command, MENU-RTE shortcuts, plain-text mode ignores formatting keys and markdown);
`ToolbarState` tests; clipboard conversion tests (HTML to blocks, blocks to HTML and text, image bytes to `ImageData`); a command
script that types, formats, inserts a table and an image, toggles source and undoes, run in the hidden automation build with the
`editor-dump` result compared; IME composition event sequence tests with synthetic events (the real IME is unproven and recorded as
such); context menu item list equals MENU-RTE for each state (caret on a word, on a link, on an image, in a table, selection).
No test touches the OS clipboard of the desktop: a fake `IDataTransfer` wrapper.

### 6.4 `composer-ui` (letter D, Sonnet)

**Scope.** The composer as a user sees it, and the application entry points that open it. Rust `src/compose.rs` (UI parts),
`chips.rs`, `recipients.rs`, `mailto.rs`, `instance.rs`; Bulwark `components/email/email-composer.tsx`,
`recipient-input.tsx`-style components named in `spec/interactions.csv` (I-234..I-286), `reply-type-menu.tsx`,
`send-warnings-dialog.tsx`, `external-recipient-confirm.tsx`, `schedule-send-dialog.tsx`, `template-picker`, `lib/validation.ts`,
`lib/idn.ts`; PORT-PLAN §3.9 (single instance, default app), RFC 6068.

**Owned:** §3.1 row "composer-ui". **T rows:** T-057..T-059, T-061, T-064, T-119..T-121, T-170..T-174, T-223..T-230 (21).
**I rows:** the 28 of §2.3. **Hooks:** 10 (§2.4). **Settings:** §2.5. **G rows:** G-054 (dialog), G-075 (the `IMentionSource`).

**Steps:**
1. *Pure Core, no UI (can be done by a Haiku or Sonnet side agent in parallel; folders do not overlap with step 2).* Make the 21 red tests
   pass: `RecipientText`, `ChipSources`, `MailtoLink`, `DefaultMailApp`, `InstanceHandoff`, `ComposerDock`. `RecipientText` is the largest
   (530 Rust lines): IDN through `System.Globalization.IdnMapping` (UseStd3AsciiRules off, the "only when it decodes back to the same
   ASCII" rule of B-COMP-026), validation per `validation.ts`, the group-aware splitter with the quote and angle-bracket repair rules of the
   Rust tests. `InstanceHandoff`: loopback `TcpListener`, a token and a greeting from a random 128-bit value, the endpoint file in the
   profile's config dir (`IProfilePaths.ConfigDir`, user-only permissions on Linux), 8 concurrent connections, 3 s deadline per connection,
   256 KiB. Add `Platform` executors for the registration plans **without calling them from anywhere an agent can trigger** (§1 item 3).
2. *Shell and recipients.* `IComposerService` real implementation in `Presentation/Compose` (the composer view model, one per open
   composer, dock or whole pane through `ComposerDock`, plugin hooks `onBeforeCompose`/`onBeforeReply*`/`onBeforeForward`/`onComposerOpen`/
   `onComposerClose`/`onDraftChange`), `ShellPage` host: the composer replaces the reader (the folders column collapses while composing,
   PORT-PLAN scope; the list stays), `Views/Compose/`: header (From select with identities and the sub-address helper and From
   override I-243/I-244, To/Cc/Bcc with chips and autocomplete I-246..I-249, subject, the status line I-238), the footer (Send, the
   arrow menu I-240, Options: Plain text, Read receipt, Priority, Tags I-257/I-258; Templates; Attach), attachments row with upload
   progress and cancel (I-252/I-253), the drop overlay (I-242), floating compose button and the compose commands registered in
   `ICommandRegistry` (`CommandIds.Compose`, `Reply`, `ReplyAll`, `Forward`, `ForwardAsAttachment`, `NewMessageTo`, `ComposeShowCc`,
   `ComposeShowBcc`) with the key table that already exists (`ShortcutTables`, `ShortcutPlace.Composer`: Ctrl+Shift+C and B, plus Ctrl+Enter,
   Ctrl+S, Ctrl+Shift+Enter, Esc, the `t` key for templates), the reply-type menu (I-262), the draft banner and the draft card
   (`IReaderBannerSource`, I-218/I-224/I-281). The chips: **the field text is the source of truth** (B-COMP-019) and chips are rebuilt from
   it; Backspace turns a chip back into text, comma and semicolon commit, paste splits through `SplitPasted`, drag between fields,
   chip menu (MENU-ADDR), keyboard navigation, the autocomplete list (`Suggest`, `DefaultSuggestionIndex`, Enter and Tab pick, 10 rows) fed by
   `IRecipientSources` (Sent-based source registered here; contacts later, D-245), `onRecipientChipsChange`.
3. *Send flow and dialogs.* Wire the views to `SendCoordinator`, `ScheduleDialogViewModel`, `TemplatePickerViewModel` (send-pipeline's, §4.3) and to
   the real services as they land (S2/S3): the send-warnings dialog (I-259, lists every warning at once; **improvement over Rust's
   "press Send again"**, recorded in §9), external-recipient confirm (I-260), the schedule dialog with presets, typed date and time and the
   Reschedule mode (I-241), the template picker, insert, save-as and placeholder fill (I-255/I-256), the leave guard: Esc or Close on a
   blank composer closes without asking and leaves **no** draft (L-008, L-009), on edited content the Cancel/Discard/Save draft dialog
   (I-234..I-236), quit waits for an in-flight save (`AutosaveState`), the 500 ms autosave tick with `AutosaveState.Due` over a content key
   (a stable 64-bit hash of the fields and `DocHtml.Export(body)` that is cheap when nothing changed: keep a version-based fast path
   from `IEditorSession.Version`), uploads of inline images found in `Session.InlineImages` (`IAttachmentUploader`, then
   `SetImageUploaded`), "Wait for the images to finish uploading".
4. *Mailto, single instance, default app.* `App` start-up: parse the command line with `InstanceHandoff.MailtoArg`; if an instance
   answers the hand-off, exit; else listen and route each link to `onMailtoIntercept` then the composer (`MailtoFields` to `ComposePrefill`),
   the account picker when several accounts (I-283), links kept while no account is signed in (`KeepPending`, `SignInMessage`), a mailto
   link clicked in a message opens the composer, never the OS handler (B-COMP-034), `onComposeFromLink` with the prefixed parameters, the
   Settings "Make Barbican the default mail app" row built and wired to the plan **behind a confirmation dialog**, never run by an agent.

**Acceptance tests:** the 21 ported tests green; view-model tests (fakes for the pipeline and the editor): open/close matrix of the leave
guard (blank, edited, saved, sending, scheduled), chip commit and edit sequences, autocomplete keyboard flow, From/identity selection and
the sub-address helper, Cc/Bcc visibility and keys, attachment upload states and the images-pending guard, the send flow order (pending
images, invalid addresses, at least one recipient, warnings dialog, external confirm, then send), the schedule dialog's errors,
`onMailtoIntercept` cancels, the pending-link queue; mailto and instance tests as ported plus hand-offs while no account exists; a hidden
automation scenario for the composer (open, type recipients and body through the editor probes of stream editor-view, autosave,
discard, scheduled and undone sends against FakeJmap) when the editor control has landed; `HookCallSiteTests` for the 10 hooks.

## 7. Integration, live checks and verify (§6.1 steps 3 to 7 of the PORT-PLAN)

The integrator (one agent, after the last stream merges) runs: `tools/build.ps1 test` and `tools/linux.ps1 test` green; `python
tools/red-check.py --list` shows nothing left (a leftover is a stream's bug); `python tools/spec-check.py --milestone M3` exit 0;
`tools/auto-run.ps1 smoke` on Windows and the VM smoke; the composer automation scenario; then the **live checks one agent at a time**
on `<test-account>` only (L-002..L-009, L-018 as scripts written by the streams), the Linux editor gate, and the S3 side-by-side
screenshots. The milestone report `docs/milestones/M3.md` lists: what was proven, what is **unproven and needs a human** (real IME on both
OSes, a mailto link from a browser, the default-app registration, Narrator), and every default of §9. No hand-test gate follows: the user
may run HAND TEST 3 whenever they wish.

## 8. Order of work inside the milestone (summary)

`m3` created from `m3-plan` → four worktrees → S0 (editor-view's Uno move, a few hours) while A, B and the Core part of D proceed →
S1 (B step 1, A step 1) → S2 → S3 (editor control hosted, seeder and schedule) → integrate. If B is late, C keeps painting literals and
composer-ui keeps its fake factory; if A is late, D keeps fakes. The only hard serial dependency is S0 before any App-head merge of C or D.

## 9. Decisions this plan took (defaults; Bulwark wins)

1. **Four streams, split by layer** (as the brief suggested): the data side of sending (A), the editing engine (B), the control (C), the
   shell and entry points (D). mailto, single instance and the default-app code stay with D (they are independent folders; the integrator
   may hand step 1's mailto and instance part to a Haiku side agent without touching D's UI).
2. **Dialogs and views of the send flow belong to D, their view models to A** (I-241, I-255, I-256, I-259, I-260): one owner per visible
   control, data streams ship testable view models.
3. **Single-instance mechanism:** loopback TCP with a token and a greeting on both OSes, as the Rust code and its tests do (D-241); the
   `AppInstance` and Unix-socket plan of PORT-PLAN §3.9 is dropped.
4. **Contacts are M5:** the autocomplete uses Sent-based recents now and `IRecipientSources` later (D-245); I-245 and G-076 are `deferred(M5)`.
5. **Send warnings are one dialog** listing all warnings (Bulwark's `send-warnings-dialog`), replacing Rust's "press Send again"; external
   recipient confirm (G-054) is part of it. The checks are `SendWarnings.Check`, pure and tested.
6. **Send now** on the undo toast = cancel the held submission and resubmit with no hold (EmailSubmission has no "release now"); if the
   cancel fails because the message was already released the toast says so (Bulwark behaviour).
7. **Schedule and undo use HOLDFOR only**, as Rust and Bulwark do, clamped to `maxDelayedSend` (7 days for Stalwart's advertised 30);
   no FUTURERELEASE means send now and no undo.
8. **Priority and Read receipt are built; DSN, Require TLS, Encrypt, Classification are deferred (M10)** (I-258): Stalwart exposes none
   through JMAP submission and Rust never had them. The org disclaimer preview (I-268) is deferred to M7.
9. **@mentions are built** (G-075) behind `recipientMentionsEnabled` (default on) with `mentionAddsRecipientTo` (default "to").
10. **The "Rewrite with AI" rows exist but are disabled** until the assistant (M6) provides `IRewriteProvider`.
11. **Spelling is Windows-only in M3**; Linux shows no marks until M9's Hunspell; the user is told in the report, not asked.
12. **Plain-text mode is the same control** with the formatting UI hidden (Bulwark's mode); there is no separate throwaway text view. The
    PORT-PLAN §3.8 "interim plain-text view" is not built because the editor lands in this milestone.
13. **The Uno 7 move is the first task of the editor stream**, with the RichTextKit vendoring fallback of ADR 0006, because the App head
    cannot reference `Barbican.Editor.Layout` on Uno.Sdk 6.7 (SkiaSharp 3 against 4).
14. **Snapshot tests are plain approved files**, not the Verify package (no csproj edits in a shared test project).
15. **`quickReplyEnabled` is not built** (M10): it is the reader's quick-reply box, not a composer row.
16. **The real IME check and the mailto-from-browser check are "unproven, needs a human"**, never gates; so are the default-app
    registration (never executed by an agent) and Narrator.
17. **Autosave key:** a 64-bit hash of the fields and the exported body, with a `Session.Version` fast path; the hash algorithm is the stream's choice
    but must be stable within a process.

## 10. Risks

| Risk | Mitigation |
|---|---|
| The Uno 7 move breaks M1/M2 views or the Linux head | Time box, then the vendoring fallback; S0 merges alone so the diff is reviewable by the integrator's test run |
| `SKXamlCanvas` text differs from `TextBlock` at 125/150 % or in dark theme | The gate runs in step 4; `TextContrast` per host; the Win2D backend of ADR 0006 is the next fallback and does not touch the model |
| BiDi caret affinity and the hidden-`TextBox` IME cannot be proven without a human | Unit-test the event sequences; record "unproven"; no gate |
| `RecipientText` (530 lines of edge cases) is the largest port of D | Done first and alone in step 1, with its 9 red tests as the oracle |
| Send path regressions touch real mail | Writes only on `<test-account>` to itself, FakeJmap for everything else, cleanup in every live script |
| Drafts left behind (L-008, L-009) | The leave guard matrix test and the `AutosaveState` queue tests; the live check lists Drafts before and after |
| Four streams contend for build slots | `tools/build.ps1` queues; A and B build Core and Presentation only (fast) |
