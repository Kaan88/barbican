# M6 plan: settings, server settings, assistant

- Written 2026-11-18 by the M6 plan stage (PORT-PLAN.md §6.1 step 1), on branch `m6-plan`, taken from `m5` (finished M5, 13727b4).
- Scope and exit: [PORT-PLAN.md](../PORT-PLAN.md) §5 "M6" (line 794); the model for this plan's shape: [M5-plan.md](M5-plan.md) (and [M4-plan.md](M4-plan.md), [M3-plan.md](M3-plan.md)). M6b (plugins) is a separate later milestone and is not planned here.
- Integration branch: **`m6`**, created from `m6-plan` once this plan is pushed (the plan commit carries the contracts, the seam, the FakeJmap arms, the in-memory fakes and the red tests, so `m6` starts with them). `m5` is merged into `m6` by the integrator whenever `m5` changes (never a rebase, never a force-push). Nothing merges to `main` and nothing is tagged until the user says so.
- Accounts and paths are placeholders (CLAUDE.md, `.local/accounts.md`): `<readonly-account>`, `<test-account>`, `<server>`, `<rust-repo>`, `<bulwark>`, `<stalwart-zip>`, `<downloads>`.
- **Process (user decision 2026-11-18):** no review stages, no hand-test gates, no questions back to the user. Every open decision is taken with the default that follows Bulwark and is listed in §9 and in `spec/decisions.md` ("M6 decisions": `D-M6-00..12` for the plan, `D-M6-A..` set-data, `D-M6-B..` set-ui, `D-M6-C..` srv-data, `D-M6-D..` srv-ui for the streams). What only a human can do (typing in a real IME, a real-pointer column resize, a password change on a real account) is written down as "unproven, needs a human" in the milestone report and never blocks a merge. Agents are Sonnet (Haiku for mechanical work); each brief in §6 is written so one agent can do it alone. Linux is skipped (M9).

## 0. Summary

| Key (branch `m6-<key>`) | Letter | Scope | Model | T rows | Red tests | Wave |
|---|---|---|---|---|---|---|
| `set-data` | A | The settings catalog (the 21 schema tabs as pages and rows, ids, keyless rows kept), the values service (typed, validated, hooks, live change), backup export/import/reset (Bulwark exports accepted), search, info-row providers, the unwired keys wired or marked inert; the assistant core (providers, errors, prompt guard, agent loop, tools, per-account history); identities (create, update, delete, Bcc, HTML signature, order), account security (password, two-step sign-in, app passwords, API keys, display name, encryption at rest), quota; FakeJmap `Identity/set` and the `x:` account-security set arms | Sonnet | 0 | 18 | 1 |
| `set-ui` | B | The Settings screen in the shell (page list, search, back/forward, the generic row renderer), the pages: Account, Password & sign-in, Privacy, Notifications, General, Appearance, Accessibility, Message list, Reading, Composing, AI assistant, Calendar, Contacts, Files, Backup, Troubleshooting, About; the assistant column; commands and palette entries; theme and style live apply | Sonnet | 0 | 3 | 1 |
| `srv-data` | C | Filters (`FilterRules` pure rules, the rule store over the Sieve script, run on existing mail with `ifInState`), out of office (VacationResponse plus the Sieve extras: audience, forward, HTML body, calendar block), folder management (create, rename, move, delete, subscribe, roles, pin, mute, icon, count mode, mark tree read), the templates manager (edit, rich body, import, export, placeholders); FakeJmap `VacationResponse/set` and the Mailbox arms they need | Sonnet | 9 | 17 | 1 |
| `srv-ui` | D | The pages Identities, Templates, Folders and tags (the tree, the tag manager), Filters (the list, the rule editor dialog, the Sieve view, Apply to existing mail), Out of office, Recover deleted items; each registers an `ISettingsPageFactory` | Sonnet | 0 | 5 | 1 |

- **All four streams start together** (one wave): the seam in §4.1, the contracts in §4 and the fakes in §3.5 are compiled code, so the UI streams work against the fakes while the data streams fill the stubs. §5 says what each stream needs from the others and when it merges into `m6`.
- **Limits held:** at most 3 agents compile at once (CLAUDE.md; `tools/build.ps1` holds `BARBICAN_BUILD_SLOTS` slots and queues the rest), at most 5 agents alive. set-data and srv-data build Core and test projects most of the time; set-ui and srv-ui build the App head and queue on the slots.
- **Branches `m6-<key>`**, worktrees `<downloads>/wt-m6-<key>`, push at creation and after every commit.
- **Ports and profiles** (hidden AUTOMATION runs, `BARBICAN_PROFILE=auto-<port>`): set-ui 8921, srv-ui 8922, integrate/verify 8929. The data streams need none (their scenarios run in-process against FakeJmap).
- **M6 T rows:** 18 (`python tools/spec-check.py --milestone M6`): the 9 `sieve` rows were ported and are green since M2 (`SieveScriptTests`); the 9 `filters_x` rows are ported by this plan commit as **red tests**. The plan commit adds 34 more red tests for behaviour that is not a Rust row (settings catalog, search, backup, assistant, identities, security, quota, filter comparators and `reject`, vacation, folders, placeholders, the two UI streams' pure rules, one hook call-site test per data stream): **43 red in all** (`python tools/red-check.py --list`: set-data 18, set-ui 3, srv-data 17, srv-ui 5). Green in this commit: the navigation seam (3), the naming rules (2), the fake threading (4), the FakeJmap arms (2).

## 1. Rules every M6 agent repeats

Everything in [M5-plan.md](M5-plan.md) §1 and [M4-plan.md](M4-plan.md) §1 still holds (CLAUDE.md, `.local/accounts.md`, hidden automation only, captures that are uniform are invalid and never a pass, `<readonly-account>` strictly read-only, never read `<admin-key-file>`, builds only through `tools/build.ps1`, push every commit, privacy check before every commit, Fable log, code rules, contract stubs throw `PortPendingException` and the owner removes `[Red]` in the same commit as the body, never change a contract signature without a note under "Requests to other streams"). The additions and changes for M6:

1. **No reviews, no hand-test gates, no questions.** Choose the default (Bulwark wins), write it in your Fable section and in `spec/decisions.md` under your own prefix (`D-M6-<letter>NN`; check the file for duplicates before you append), move on.
2. **Unique names, enforced.** §3.3 is the table; `M6NamingTests` fails when a public type of an owned namespace lacks its stream's prefix or shares a simple name with any other public Core or Presentation type. A name this plan did not assign goes to the integrator (a request), never into another stream's namespace. Test helpers carry the stream's name too (`AssistantTestFakes`, `FilterTestData`).
3. **The threading contract (D-M6-00) is binding** on the real services, the fakes and the views: a service call completes off the UI thread, its `Changed` is raised from a pool thread, and a view marshals to its dispatcher before it touches a control. A UI stream tests on the `Sample*` fakes (§3.5) and its first hidden run is on them; it never writes a stand-in that raises on the UI thread.
4. **Live checks** run only in the integrate stage (§7), one agent at a time. `<readonly-account>` reads only. Writes (L-040, L-045, L-044) run on `<test-account>` and are cleaned up. Account security writes are not run live (D-M6-09). A stream writes its FakeJmap equivalent and its live script and runs nothing live itself.
5. **Hostile and sensitive input.** A password, an app-password or API-key secret, an otpauth URI and the assistant's API key are never logged, never written to a profile file in clear, never put in a test artefact or a screenshot; the assistant's mail text is untrusted data (B-AI-004). A Sieve script, a filter rule and a template are untrusted: nothing evaluates them but the generator and the matcher of the plan.
6. **Time is injected** (`TimeProvider`); vacation days are `DateOnly` compared with the injected clock.
7. **Bulwark and Stalwart references.** Every Core file starts with the Rust file:line, the Bulwark file, the RFC section and the Stalwart path it mirrors (`<bulwark>`, `<stalwart-zip>`: `crates/jmap/src/{vacation,identity,mailbox,sieve}/`, `crates/jmap/src/registry/mapping/`); the contract stubs name them, **you verify the lines**.
8. **Stay inside your owned files (§3).** A change you need elsewhere goes under `## Requests to other streams` in your Fable section; work around it behind your own interface; the integrator applies it.
9. **User decisions in force:** D-2, D-3, D-7, D-9, D-10 of M2-plan §1 item 14; D-1 (rewriting public history) is not approved; permanent deletes always ask (D-7): deleting a folder, an identity, a filter rule, a template, an app password and Reset settings all ask first.

## 2. What M6 must close

### 2.1 Rust tests (T-): 18 rows

| Stream | Rows | Test files |
|---|---|---|
| srv-data (9) | T-093..T-101 `filters_x::*` (multi-condition rule build and round trip, validation, reorder and summary, Sieve match semantics, the retroactive plan, stale scan dropped, apply patches with `ifInState`, invalid regex refused, odd header names quoted) | `tests/Barbican.Core.Tests/Filters/Rules/FilterRulesTests.cs` |
| (done, M2) | T-271..T-279 `sieve::*` | `tests/Barbican.Core.Tests/Sieve/SieveScriptTests.cs` |

### 2.2 JMAP requirements (J-)

J-073 (Sieve) keeps its M2 evidence; srv-data adds the save-and-activate and the `ifInState` apply evidence against FakeJmap. New rows, next free ids (check the file), `done` against FakeJmap and `partial` until the live pass reads them: **VacationResponse/get and set** (srv-data), **Identity/set** (set-data), **the Stalwart management objects** `x:AccountPassword`, `x:AccountSettings`, `x:AppPassword`, `x:ApiKey` (set-data; reads live, writes FakeJmap only, D-M6-09), **Mailbox/set** subscribe, role, move and rename and **Mailbox metadata** writes for pin, mute, icon and count mode (srv-data).

### 2.3 Interactions (I-): 55 rows

| Owner | Rows |
|---|---|
| set-ui (39) | I-022 (Compose on the rail), I-395..I-403 (tabs, search, keys and cross-links, show advanced, account list, sign out, name and addresses, archive and push, password page), I-406..I-408, I-412..I-427 (general, notifications, appearance, accessibility, message list, reading, window, composing, privacy), I-439..I-445 (calendar, contacts, files settings), I-449, I-450 (about and troubleshooting, backup), I-457 (theme toggle) |
| srv-ui (14) | I-289..I-291 (templates manager, template sync, identities), I-428..I-438 (folders, tags, filters, out of office, recover) |
| set-data, srv-data | no visible controls; they supply the view models and services the rows above read |
| deferred | I-404 (app lock: M8), I-458 (resizable columns, sidebar collapse: M10); the parts of I-414, I-441, I-442 and I-444 that are plugin themes (M6b) or sharing (M10) |

Rows a stream closes need evidence (a test, an automation script, or a screenshot path with sample data); everything else is `deferred(M<k>)` with a reason.

### 2.4 Plugin hooks (36 rows with `milestone = M6` in `spec/plugins/hooks.csv`; the `owner` column is filled by this commit)

Each stream gives its hooks a call site through `IHookBus` and sets `cs_call_site` / `status` in the CSV for its own rows only (`python tools/gen-hooks.py --check`). Payload types are new files under `src/Barbican.Core/Plugins/Payloads/` named after the hook, one file per type. Intercepts run before the action and can stop it.

| Owner | Hooks |
|---|---|
| set-data (21) | onBeforeSettingChange, onSettingChange, onSettingsExport, onSettingsImport, onSettingsReset, onSettingsSync (D-M6-A04), onKeywordChange, onTrustedSenderChange, onIdentitiesLoaded, onBeforeIdentityChange, onIdentityCreate, onIdentityUpdate, onIdentityDelete, onIdentitySelect, onSignatureRender, onSelectReplyIdentity, onPasswordChange, onTotpChange, onAppPasswordChange, onEncryptionChange, onDisplayNameChange |
| srv-data (15) | onFiltersLoaded, onFilterRuleChange, onBeforeFiltersSave, onFiltersSave, onSieveScriptChange, onSieveScriptGenerate, onVacationLoaded, onBeforeVacationUpdate, onVacationUpdate, onTemplateCreate, onTemplateUpdate, onTemplateDelete, onTemplatesImport, onTemplateApply, onTemplateRender |
| set-ui, srv-ui | none |

`SettingHookCallSiteTests` (set-data) and `FilterHookCallSiteTests` (srv-data) are `[Red]` call-site tests; each stream removes its `[Red]` when every row of the owner has a call site. The composer-side hooks (onIdentitySelect, onSignatureRender, onSelectReplyIdentity, onTemplateApply, onTemplateRender) are one-line call sites in the M3 composer files, the only M3 edit M6 allows (D-M6-08). onKeywordChange fires from the reminder-keywords setting, onTrustedSenderChange from the trusted-senders list of `Core/Mail/Trust` (one line there).

### 2.5 Behaviour rows (B-), live checks (L-), gaps (G-), settings

- **B-:** `spec/behaviours/settings.md` B-SET, `sieve-filters.md` B-SIEVE-013..023, 027..031, `assistant.md` B-AI-001..011, each stream marks the rows of its area `ported` or `deferred(M<k>)` in the same commit. set-data: B-SET, B-AI; srv-data: B-SIEVE-013..023, 027, 028, 030, 031; set-ui and srv-ui mark the interaction side of the same rows.
- **L- (run in the integrate stage, §7):** L-040 (edit a two-condition rule and Apply in a test folder), L-041 (Bulwark's Sieve view reads what Barbican saved: a hand check the report states as unproven unless a Bulwark is reachable), L-044 (tags: nested create, rename, delete), L-045 (settings survive a restart), L-053 (settings open without the identities flicker; a probe), the out-of-office on and off on `<test-account>`.
- **G-:** G-020..G-025 (reject, vacation extras, calendar block, identities create and delete, HTML signatures, order), G-032..G-035 (templates), G-036..G-041 (folders), G-057 (quota), G-059..G-068 (the unwired keys, with D-M6-A03), G-069 (keyless rows), G-027..G-030 (account security: code and FakeJmap, not live); G-026 masked addresses, G-042 and G-064 are deferred (D-M6-10).
- **Settings read** (`spec/settings.csv`; each owner sets `csharp_read_by` for the rows it wires, 124 rows are `not ported`):

| Stream | Keys |
|---|---|
| set-data | the info rows: `ISettingInfo` supplies every keyless `info` row's text; the services that read keys: `sendConfirmation`, `settingsSyncDisabled` (inert, D-M6-A04), the notification keys' storage (the behaviour is M8), the privacy keys |
| set-ui | every `not ported` row of a page set-ui owns (appearance, accessibility, notifications, privacy, general, message list, reading, composing, calendar, contacts, files, about, troubleshooting): it applies the value live (theme, style, density, text scale, more contrast, toolbar and rail options) or marks the row inert with its reason; composing keys are read through `ComposeSettings` (one allowed edit in that M3 file) |
| srv-data | none of its own (its data has no keys) |
| srv-ui | the keyless rows of its pages (`S-keyless-041..048` of out of office, the Folders rows), read by its views |

## 3. Parallel work without shared files

### 3.1 File ownership

Every file has exactly one owner for the whole milestone. Folders go with all files below them. Files not listed stay with the integrator (frozen): `Barbican.slnx`, `Directory.*`, `global.json`, `AppComposition.cs`, `CompositionGraphTests.cs`, `AppCompositionTests.cs`, `MenusCompositionTests.cs`, `MenuContext.cs`, `SlotNames.cs`, `src/Barbican.Presentation/Settings/{SettingsPageIds,SettingsPageOwner,SettingsPageInfo,SettingsNav,ISettingsNavigation,SettingsNavigation}.cs`, `src/Barbican.Presentation/Composition/SettingsSeamRegistration.cs`, `src/Barbican.Presentation/Settings/Sample/**`, `src/Barbican.App/Settings/ISettingsPageFactory.cs`, `tests/Barbican.FakeJmap/FakeApi.cs`, `FakeApi.ServerSettings.cs`, `FakeJmapOptions.cs`, `FakeJmapServer.Routes.cs`, `tests/Barbican.Presentation.Tests/Settings/{SettingsNavigationTests,SampleFakesThreadingTests,M6NamingTests,FakeVacationAndSecurityArmsTests}.cs`, every M2-M5 file (except the exceptions named here).

| Owner | Source | Tests |
|---|---|---|
| **set-data** | `src/Barbican.Core/Settings/{Pages,Backup,Assistant,Account}/**`, `SettingsDataRegistration.cs`; its hook payload files in `src/Barbican.Core/Plugins/Payloads/`; one-line call sites in the M3 composer files and `Core/Mail/Trust` (§2.4); `src/Barbican.Core/Settings/SettingsSchema.cs` and `settings-schema.json` (it may fix the schema, never reorder it) | `tests/Barbican.Core.Tests/Settings/{Pages,Backup,Assistant,Account}/**`, `Settings/SettingHookCallSiteTests.cs`, `tests/Barbican.Presentation.Tests/Settings/SettingsCatalogNavTests.cs`; `tests/Barbican.FakeJmap/{FakeApi.IdentitySet,FakeApi.AccountSecurity,FakeAccountSecurity,FakeSeed.M6Settings}.cs` |
| **set-ui** | `src/Barbican.Presentation/Settings/Ui/**`, `Composition/SettingsUiPresentationRegistration.cs`; `src/Barbican.App/Views/Settings/**` (including the assistant column), `Composition/Features/SettingsUiRegistration.cs`; `src/Barbican.Automation/` commands `set-*` as an `IAutomationCommand` file under `Views/Settings/Automation/`; the settings branch of `ShortcutTables.cs` and the palette entries; the one allowed edit in `ComposeSettings.cs` | `tests/Barbican.Presentation.Tests/Settings/Ui/**` |
| **srv-data** | `src/Barbican.Core/Filters/Rules/**`, `Settings/{Vacation,Folders,Templates}/**`; its hook payload files; one-line call sites in the composer's template insert path (§2.4) | `tests/Barbican.Core.Tests/Filters/Rules/**`, `Settings/{Vacation,Folders,Templates}/**`; `tests/Barbican.FakeJmap/{FakeApi.Vacation,FakeVacation,FakeSeed.M6Server}.cs`, and its Mailbox/set additions in `FakeApi.MailboxSet.cs` (the M2 file, an allowed exception) |
| **srv-ui** | `src/Barbican.Presentation/Settings/Server/**`, `Composition/SrvUiPresentationRegistration.cs`; `src/Barbican.App/Views/SettingsServer/**`, `Composition/Features/SrvUiRegistration.cs`; its `srv-*` automation commands under `Views/SettingsServer/Automation/` | `tests/Barbican.Presentation.Tests/Settings/Server/**` |

### 3.2 Who builds which page

`SettingsNav.Pages` (frozen) carries the owner of each page: **set-ui** Account, Security, Privacy, Notifications, General, Appearance, Accessibility, MessageList, Reading, Composing, Assistant, Calendar, Contacts, Files, Backup, Troubleshooting, About; **srv-ui** Identities, Templates, Organize, Filters, Vacation, Recover; **Later** Addons (M6b). set-ui's `SetUiScreenFactory` shows a page from the `ISettingsPageFactory` registered for its id, else from its catalog rows; srv-ui registers one factory per page it owns and nothing else in the screen. A cross-link between pages is `ISettingsNavigation.Open(pageId, anchor)` (Account's "Manage addresses" opens Identities).

### 3.3 Names, decided up front (the M4 and M5 collisions)

| | set-data (A) | set-ui (B) | srv-data (C) | srv-ui (D) |
|---|---|---|---|---|
| Decision IDs | `D-M6-A01..` | `D-M6-B01..` | `D-M6-C01..` | `D-M6-D01..` (the plan's are `D-M6-00..12`, never reused) |
| Type-name prefix | `Setting*`, `Assistant*`, `Account*` | `SetUi*` | `Filter*`, `Vacation*`, `FolderAdmin*`, `TemplateAdmin*` | `SrvUi*` |
| Source namespaces | `Barbican.Core.Settings.{Pages,Backup,Assistant,Account}` | `Barbican.Presentation.Settings.Ui`, `Barbican.App.Views.Settings` | `Barbican.Core.Filters.Rules`, `Barbican.Core.Settings.{Vacation,Folders,Templates}` | `Barbican.Presentation.Settings.Server`, `Barbican.App.Views.SettingsServer` |
| Test namespaces | `Barbican.Core.Tests.Settings.{Pages,Backup,Assistant,Account}` | `Barbican.Presentation.Tests.Settings.Ui` | `Barbican.Core.Tests.Filters.Rules`, `Barbican.Core.Tests.Settings.{Vacation,Folders,Templates}` | `Barbican.Presentation.Tests.Settings.Server` |
| Test helper names | `AssistantTestFakes` (add `Setting*` names) | `SetUi*` | `FilterTestData` | `SrvUi*` |
| Command ids and counts | none | `settings.*`, `SetUiCommands.Count`, `SetUiMenus.Count` (MENU-CALSET, MENU-BOOKSET) | none | `SrvUiCommands.Count`, `SrvUiMenus.Count` |
| Registration | `SettingsDataRegistration` | `SettingsUiPresentationRegistration`, `SettingsUiRegistration` | `FilterDataRegistration` | `SrvUiPresentationRegistration`, `SrvUiRegistration` |
| Automation commands | none | `set-*` | none | `srv-*` |

Frozen shared names (the integrator's, not subject to a stream prefix): `SettingsPageIds`, `SettingsPageOwner`, `SettingsPageInfo`, `SettingsNav`, `ISettingsNavigation`, `SettingsNavigation`, `SettingsSeamRegistration`, `ISettingsPageFactory`, `Sample*` and `SampleThreading` (namespace `Barbican.Presentation.Settings.Sample`).

Rules that follow: (1) a stream asked for a name outside its prefix asks the owner; (2) no two public interfaces share a name: the interfaces are exactly the ones in §4 and a stream adds none without its prefix; (3) shared records have one owner who adds properties in the plan commit; a stream that needs one files a request; (4) no test class in a child namespace repeats the name of a class in a parent namespace of another stream's folder; (5) automation commands have the prefix of their stream; (6) a new namespace is asked for, not taken.

### 3.4 What the plan commit lands so no stream has to

- The **seam** (§4.1): the page ids, the navigation list with owners, `ISettingsNavigation` and its real implementation, `ISettingsPageFactory`, a minimal `SetUiScreenFactory` and `SetUiScreen` registered as the Settings app's `IAppScreenFactory`.
- The **composition counts**: `AppCompositionTests` and `MenusCompositionTests` now add `SetUiCommands.Count + SrvUiCommands.Count` and `SetUiMenus.Count + SrvUiMenus.Count`; each is a constant `0` in the owner's own file. A stream sets its constant in the commit that registers its commands or menu providers and never touches the test.
- The **AppComposition** and `CompositionGraphTests.AddM6` calls (five registrations, §4.9).
- The **FakeJmap options and routing** (§4.12), the **hook owners** in `hooks.csv`, the **shared records**.
- The **in-memory fakes** of every data service the UI streams consume (§3.5).

### 3.5 In-memory fakes that behave like the real services (D-M6-00)

`src/Barbican.Presentation/Settings/Sample/` holds one fake per data interface the UI reads: `SampleSettingValues`, `SampleSettingBackup`, `SampleSettingInfo`, `SampleAccountIdentities`, `SampleAccountSecurity`, `SampleAccountQuota`, `SampleAssistantSession`, `SampleFilterService`, `SampleVacationService`, `SampleFolderAdmin`, `SampleTemplateAdmin`, on the helper `SampleThreading` (`OffThreadAsync` runs and completes on the pool, `RaiseOnPool` raises from the pool). They are what the view-model tests and the hidden sample-data runs (`BARBICAN_SAMPLE=1`) register; the data streams' real classes never use them. `SampleFakesThreadingTests` pins that each raises `Changed` on a pool thread. A fake keeps the behaviour that matters for a view (a created identity shows in the next list, a saved vacation is what the next load returns, a filter save raises `Changed`); where a real service fails (a refused address, a state mismatch), the UI stream asks the integrator to extend the fake, it does not wrap it. The data streams read the same contract and may extend a fake only for a behaviour they implemented (an additive change the integrator merges).

## 4. Contracts (compiling code in this commit set)

All stubs below are in the repository now, build with warnings as errors, and are wired into the composition (§4.9). A stub that throws does so with `PortPendingException("Type.Member")`; the 43 red tests prove the stubs are reached. Rules, rows and prefs are `JsonNode` where Bulwark's JSON must survive an edit (D-M6-C01); instants are UTC; days are `DateOnly`.

### 4.1 The settings seam (frozen; owner integrator)

`SettingsPageIds` (24 ids), `SettingsPageOwner` (SetUi, SrvUi, Later), `SettingsPageInfo`, `SettingsNav.Pages/Find`, `ISettingsNavigation` / `SettingsNavigation` (Current, Anchor, Open, Back, Forward, Changed; real, with `SettingsNavigationTests`), `ISettingsPageFactory` (`PageId`, `Create`, `OnShown`, `Reveal`), `SetUiScreenFactory` + `SetUiScreen` (the page list and the page host; set-ui replaces the body, keeps the two constructor inputs).

### 4.2 Pages, values, search, info (set-data; consumed by set-ui and srv-ui)

`SettingRowKind`, `SettingRowStatus`, `SettingOption`, `SettingRow`, `SettingPage`, `SettingCheck`, `SettingsCatalog.Pages/RowOf` (D-M6-A01: 21 pages, 276 rows, 64 keyless), `SettingRules.Coerce`, `SettingSearchEntry`, `SettingSearchHit`, `SettingSearch.EntriesOf/Search`, `ISettingValues` / `SettingValues` (`Stored`, `SetAsync`, `Changed`; D-M6-A02), `SettingChangedEventArgs`, `ISettingInfo` / `SettingInfo` (`Provides`, `TextAsync`).

### 4.3 Backup (set-data)

`SettingBackupPlan`, `SettingBackupRules.Plan` (what an import would do: Bulwark flat or wrapped or a Barbican file, known and unknown keys), `ISettingBackup` / `SettingBackup` (`ExportAsync`, `ImportAsync`, `ResetAsync`, `Changed`); the file side is the existing `SettingsStore` (T-259).

### 4.4 Assistant (set-data; consumed by set-ui's column and page)

`AssistantProviderKind`, `AssistantActionMode`, `AssistantConfig`, `AssistantMessage`, `AssistantToolSpec`, `AssistantErrors.Message` (B-AI-003), `AssistantPrompt.Clip/WrapEmail/WrapDraft` (B-AI-004), `AssistantHistory.ToJson/FromJson` (B-AI-011), `IAssistantClient` / `AssistantClient`, `IAssistantTools` / `AssistantTools`, `AssistantLoop.RunAsync` (16 turns, confirmations; B-AI-007, B-AI-009), `IAssistantSession` / `AssistantSession` (`History`, `AskAsync`, `ClearAsync`, `Changed`).

### 4.5 Account-level data (set-data)

`AccountIdentity`, `AccountIdentityEdit`, `AccountIdentityRules.Problem/Ordered`, `IAccountIdentities` / `AccountIdentities` (list, create, update, delete, order, `Changed`); `AccountAppPassword`, `AccountApiKey`, `AccountIssuedSecret`, `AccountTotp`, `AccountSecurityEncryption`, `AccountSecurityRules.PasswordProblem/IsTotpCode`, `IAccountSecurity` / `AccountSecurity` (password, TOTP enrol, confirm, disable, app passwords, API keys, display name, encryption at rest); `AccountQuotaUsage` (`Fraction`, `Text`), `IAccountQuota` / `AccountQuota`.

### 4.6 Filters (srv-data; consumed by srv-ui and the assistant tools)

`FilterCond`, `FilterAct`, `FilterFolder`, `FilterBuilt`, `FilterPlanStep`, `FilterApplied`, `FilterAppliedKind`, `FilterRules` (Fields, Comparators, ActionKinds, ConditionsOf, ActionsOf, BuildRule, Summary, Moved, SieveMatches, RetroUnsupported, QueryFilter, RetroProperties, RetroMessage, RuleMatches, Plan, PlanCount, StepPatch, ApplyAsync, Current, AppliedText: T-093..T-101), `FilterState`, `FilterScan`, `FilterChangedEventArgs`, `IFilterService` / `FilterService` (load, save with the Bulwark-compatible generator, scan, apply).

### 4.7 Out of office (srv-data)

`VacationAudience`, `VacationForward`, `VacationSettings`, `VacationRules.Problem/OwnDomains`, `VacationChangedEventArgs`, `IVacationService` / `VacationService` (D-M6-C02).

### 4.8 Folders and templates (srv-data)

`FolderAdminNode`, `FolderAdminPref`, `FolderAdminPrefs`, `FolderAdminRules` (names, roles, moves), `FolderAdminChangedEventArgs`, `IFolderAdmin` / `FolderAdmin`; `TemplateAdminPlaceholders.Find/Fill`, `TemplateAdminChangedEventArgs`, `ITemplateAdmin` / `TemplateAdmin` (wraps the M3 `ITemplateStore`, D-M6-C04).

### 4.9 Composition (DI skeletons, compile and resolve today)

`AppComposition.Build` now calls, after the M5 features: `AddBarbicanSettingsSeam()` (Presentation, integrator), `AddBarbicanSettingsData()` (Core, set-data), `AddBarbicanServerSettingsData()` (Core, srv-data), `AddBarbicanSettingsUi()` (App, set-ui; calls `AddBarbicanSettingsUiViewModels()`), `AddBarbicanServerUi()` (App, srv-ui; calls `AddBarbicanServerUiViewModels()`). `CompositionGraphTests.AddM6` registers the five portable parts, so "every registered service resolves" covers them. Rules as M5 §4.10: defaults use `TryAddSingleton`; a stream that implements a service changes the registration in its own file; stub classes have no constructor parameters until their owner adds them (and keeps the graph test green). A sample-data run (BARBICAN_SAMPLE=1, Automation build) replaces the data services with the `Sample*` fakes in set-ui's and srv-ui's registration files.

### 4.10 Settings screen state (set-ui)

`SetUiRow`, `SetUiRowRules.Visible/EffectiveValue/SelectText`, `ISetUiViewModel` / `SetUiViewModel` (search text and hits, show advanced, visible pages, rows of a catalog page with values, set a value, refresh info rows), `SetUiCommands`, `SetUiMenus` (counts).

### 4.11 Server-backed pages (srv-ui)

`SrvUiValueKind`, `SrvUiFilterRow`, `SrvUiFolderRow`, `SrvUiFilterFormRules.FieldChanged/ValueKind`, `SrvUiFolderTreeRules.Flatten`, `SrvUiVacationRules.Status`, `ISrvUiFiltersViewModel` / `SrvUiFiltersViewModel` (the example contract; the other pages' view models are srv-ui's additions under its prefix), `SrvUiCommands`, `SrvUiMenus` (counts).

### 4.12 FakeJmap arms (in this commit set)

`FakeApi.ServerSettings.cs` (frozen) routes the M6 methods. **Real:** `VacationResponse/get` (singleton `"singleton"`; Stalwart `crates/jmap/src/vacation/get.rs`; Bulwark `lib/jmap/client.ts:5310`), `x:AccountPassword/get` (`otpAuth.otpUrl` when two-step sign-in is on), `x:AccountSettings/get` (`description`, `encryptionAtRest`), `x:AppPassword` and `x:ApiKey` query and get (Bulwark `stores/account-security-store.ts:220-403`; Stalwart `crates/jmap/src/registry/mapping/`). **Stubs (answer unknownMethod until their stream fills them):** `VacationResponse/set` (srv-data: update only, `ifInState`, a `toDate` before `fromDate` is invalidProperties, create and destroy are forbidden; Stalwart `vacation/set.rs`), `Identity/set` (set-data: create, update, destroy, an address that is not the account's is invalidProperties, a default identity cannot be destroyed; Stalwart `identity/set.rs`; Bulwark `client.ts:5213-5287`), the `x:` set arms (set-data: a wrong current password is invalidProperties, secrets are issued once and never echoed again). Options: `VacationCapability`, `Vacation` (`FakeVacation`), `AccountSecurity` (`FakeAccountSecurity`). Green tests: `FakeVacationAndSecurityArmsTests`. Each stream adds its seed (`FakeSeed.M6Settings`, `FakeSeed.M6Server`) and fills its stubs in its first step with a request log and `FailNextSet` as `FakeApi.CalendarSet.cs` has; srv-data extends `FakeApi.MailboxSet.cs` for subscribe, role (with the previous holder losing it) and move.

## 5. Order, sync points and merges

All four start on day one, from `m6`. **Merge early:** a stream merges into `m6` after every green step (`tools/build.ps1 test`; Linux is M9), and every other stream merges `m6` into its branch at the start of each step. The sync points:

| Point | Who | What lands in `m6` | Who needs it |
|---|---|---|---|
| **S1** | set-data | Step 1: `SettingsCatalog`, `SettingRules`, `SettingSearch`, `SettingBackupRules`, `SettingValues`, `SettingBackup`, `SettingInfo` (the 7 catalog, rules, search and backup red tests) | set-ui (real rows instead of an empty page; until then it works on `SampleSettingValues` and a catalog it builds from the schema in its own test data) |
| **S1** | srv-data | Step 1: `FilterRules` (T-093..T-101 and the two filter rules), `VacationRules`, `FolderAdminRules`, `TemplateAdminPlaceholders`, `FakeApi.Vacation` set arm | srv-ui (rule editor, tree, vacation form) |
| **S2** | set-data | Step 2: identities, account security, quota, `Identity/set` and `x:` set arms | srv-ui's Identities page, set-ui's Account and Security pages |
| **S2** | srv-data | Step 2: `FilterService`, `VacationService`, `FolderAdmin`, `TemplateAdmin` with hooks and the Mailbox arms | srv-ui (real data and saves) |
| **S3** | set-data | Step 3: the assistant (client, loop, tools, session) | set-ui's assistant column and page |
| **S4** | all | Hook call sites and the `[Red]` hook tests removed; `spec` rows; Fable log | the integrate pass |

Until S1 and S2 land, the UI streams run on the `Sample*` fakes; the switch to the real services is one registration line each and is the first thing a UI stream does after a data merge, followed by a hidden run (the M5 live run found its two bugs exactly there).

## 6. Briefs

Each brief: read the sections named, port or build in the order given, remove your `[Red]` attributes in the commit that makes them pass, tick spec rows in the same commit, append your Fable section, push.

### 6.1 set-data (A)

1. Catalog, rules, search, backup rules (`SettingsCatalog` from the embedded schema, 276 rows, ids per D-M6-A01; `SettingRules.Coerce`; `SettingSearch`; `SettingBackupRules.Plan`), then `SettingValues` (single writer, hooks, pool `Changed`) and `SettingBackup` over `SettingsStore` (T-259 stays green). Wire or mark inert the 124 `not ported` rows that are yours (§2.5).
2. `AccountIdentities` (Identity/set, the order in the Inbox metadata `preferredIdentityIds`), `AccountSecurity` (Stalwart objects, FakeJmap set arms first), `AccountQuota`; the hook call sites (composer-side as one-liners).
3. The assistant: `AssistantErrors`, `AssistantPrompt`, `AssistantHistory`, `AssistantClient` (both providers, 90 s timeout, key from `ISecretStore`), `AssistantTools` (the Rust tool set, B-AI-008, over typed services), `AssistantLoop`, `AssistantSession` (per-account history file).
4. FakeJmap seeds, the spec rows, `SettingHookCallSiteTests` green.

### 6.2 set-ui (B)

1. The screen: `SetUiViewModel`, the page list grouped as the schema, the search box (hits open `ISettingsNavigation.Open(page, rowId)`), Back and Forward, Show advanced, the generic row renderer for the catalog (toggle, select, text, button, info, section) with live apply; first hidden run on the samples.
2. The pages in §3.2 that are set-ui's, in the order Appearance and Accessibility (live theme, style, density, text scale, contrast, motion), Account and Security (account list, sign out, quota, password, TOTP, app passwords, API keys: on `SampleAccountSecurity` until S2), General, Notifications, Privacy, Message list, Reading, Composing, Calendar, Contacts, Files, Backup, Troubleshooting, About.
3. The assistant page and column on `SampleAssistantSession` until S3; commands, palette entries and shortcuts; MENU-CALSET and MENU-BOOKSET.
4. Switch to the real services after each data merge and re-run hidden; every dialog (Reset settings, password) is an in-window `IDialogContent`.

### 6.3 srv-data (C)

1. `FilterRules` first: the 9 Rust tests in `FilterRulesTests` (Rust `filters_x.rs` is the source, Bulwark `lib/filters/retroactive.ts` the oracle), then comparators, action kinds, `VacationRules`, `FolderAdminRules`, `TemplateAdminPlaceholders` (the other 8 pure red tests).
2. `FilterService` over `SieveScriptStore` (save, validate, activate; locked scripts refused; a failed save restores the list), the scan and `ApplyAsync` with `ifInState`; `VacationService` (D-M6-C02) and the FakeJmap `VacationResponse/set` arm; `FolderAdmin` and `TemplateAdmin` with their FakeJmap Mailbox arms.
3. Hook call sites and payloads, `FilterHookCallSiteTests` green; spec rows, live scripts for L-040 and the out-of-office check.

### 6.4 srv-ui (D)

1. Register an `ISettingsPageFactory` per page, on the `Sample*` fakes first: Filters (list, enable, reorder, delete, lock notice), the rule editor dialog (conditions and actions following `SrvUiFilterFormRules`, active period, spam opt-in, `reject` only when advertised), Apply to existing mail (scan, count, confirm, progress, result text), the read-only Sieve view.
2. Out of office (status line, dates, body, audience, forward, calendar block), Identities (list, order, create, edit, signature, delete asks), Templates (manager, rich body, placeholders, import and export), Folders and tags (the tree from `SrvUiFolderTreeRules`, menus, icon, count mode, pin, mute, mark tree read; the tag manager on `TagService`), Recover (on `ArchivedItems`).
3. Commands and menu providers (`SrvUiCommands.Count`, `SrvUiMenus.Count` set in the commit that registers them); switch to the real services after each data merge, hidden run.

## 7. Integration pass and live checks

One agent after the four streams merge: `tools/build.ps1 test` green, `python tools/red-check.py` finds 0 `[Red]`, `python tools/spec-check.py --milestone M6` clean, all 36 hooks fire, hidden screens of every settings page (sample data), then the live checks of §2.5 on `<test-account>` with the agent profile, each cleaned up; the report `docs/milestones/M6.md` lists what is unproven (a real IME in the rule editor, a real-pointer drag of a folder, Bulwark's reading of the saved Sieve script unless a Bulwark is reachable, any account security write) and the deferrals of D-M6-10. A publish and a smoke run as M5.

## 8. Deferred

D-M6-10 lists them: settings sync (G-064), sharing and ACLs, masked addresses (Stalwart enterprise), folder export and import, shared tags, app lock and the protocol-handler rows (M8), plugin themes (M6b), Spam Siege, resizable columns (M10).

## 9. Decisions

`spec/decisions.md`, "M6 decisions": D-M6-00..12 (plan), the seeds D-M6-A01..A09, B01..B03, C01..C05, D01..D03 that this plan commit already relies on; the streams append theirs at the end of the section.

## 10. Risks the plan knows about

- **The filter port is the longest single piece** (the Rust file is 892 lines and the generator in `SieveScript` is shared with quick rules): srv-data starts with it and `FilterRules` is pure so it can land alone at S1.
- **The Account security objects are Stalwart-specific** and tested against a FakeJmap written from the source, not a server: the live pass reads them only (D-M6-09).
- **The generic renderer carries 201 rows**: set-ui keeps it table-driven; a row that needs more than the kinds offer asks for an additive `SettingRowKind` through the integrator, never a one-off control.
- **Two streams edit one M3 area** (composer hooks): both are one-line call sites, listed in §2.4, applied by the integrator if they collide.
