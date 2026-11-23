# Fable review 3 (`m2` at `91a0741`)

Reviewer: Fable, 2026-11-17, branch `fable-review-3` (from `origin/m2` at `91a0741`, not merged). Scope:

1. the top unresolved items of [FABLE-REVIEW.md](FABLE-REVIEW.md) ("M2, consolidated at the verify stage" items 1 to 12 and 26,
   "m2-close-a", "m2-close-b", "M2 hotfix: writes");
2. what merged since review 2 (`docs/fable-review-2.md`, base `m1` at `4eccc0f`): the M2 code (unified inbox, actions, menus,
   reader depth, search, import and export, native plugins, archive, tags, drag and drop);
3. security and privacy, the latter over the whole public history.

How it was done: **reading only**. Nothing was built, tested or launched, no mail server was contacted, `<admin-key-file>` was not
read, no credential store was opened. The only commands run are `git` reads and text searches. Server behaviour is quoted from the
Stalwart 0.16.27 source, Bulwark from `<bulwark>`, the Rust app from `<rust-repo>`. Every finding below is therefore "by reading":
none was reproduced in a running app, and each fix names the test that would prove it. **Everything about Linux is unverified.**

Verdict words: **fixed**, **partly fixed**, **open**, **confirmed** (a real problem, with a fix), **fine**, **decision** (the user
must decide), **not reviewed**.

<a id="carried"></a>
## 1. Carried items: verdicts

| Item | Verdict | What is left |
|---|---|---|
| R2-01 rejected sign-in repeated (`Core/Auth/AuthLatch.cs`, `Core/Jmap/Transport/AuthorizedHttp.cs`) | **fixed** at the transport | The latch closes on "needs sign-in" and on a 401 after one renewal, and every later request of that account fails without touching the server. The stop paths in `MailSync` and `AccountManager.OnRefused` were not re-read. |
| R2-04 a mail can switch off the shell's frame handling | **fixed** by reading | `App/Assets/mail/shell.js` reads the frame document only through prototype getters (`G`, `Q`, `EP.setAttribute.call`) and finishes a frame only when its URL is `about:srcdoc` (`:201`); `MailSanitizer.Unname` drops `name` (and `id`) from `img` (`Core/Mail/Body/MailSanitizer.cs:118-135`); `embed`, `form`, `iframe`, `object` are removed or unwrapped. Elements inside `<template>` are not visited by the sanitizer (AngleSharp keeps them in a separate fragment): inert, so fine. |
| R2-06 / verify item 5: two instances on one vault | **not reviewed** this round | Nothing new was read in `AccountStore`, `SecretAccountVault`. The item stays as written. |
| R2-07 / verify item 2: Authentication-Results trace block | **partly fixed** | [R3-06](#r3-06): a message that never passed the server's port 25 can carry a forged block. Item 2(b) (verdict in Sent, roles loaded late) is still open. |
| R2-10 hook budgets on the pool | **fixed** | `NativePluginHost.RunAsync` starts every call with `Task.Run` and races it against the budget (`Core/Plugins/Native/NativePluginHost.cs:180-222`); a late call is cancelled, observed and reported. |
| R2-14 / verify item 3 / hotfix-writes: writes in agent profiles only | **fine** | `ReadOnlyPolicy` plus `ProfilePaths.IsAgentProfile` (auto-*, smoke, smoke-*, xrun-*, agent-*); a user profile is never restricted. Notes in [R3-12](#r3-12). |
| Verify item 1: GuardedHttp | **fine** | The connect callback connects to the address that was checked, redirects are re-checked, the body is capped. `PublicAddress` leaves out three small ranges ([R3-12](#r3-12)). No other `HttpClient` exists besides `Core/Jmap/Transport/JmapHttp.cs`; `Process.Start` only in the two browser launchers, which open only http and https (`Platform.Windows/WindowsBrowserLauncher.cs:19`). |
| Verify item 4: native plugin host | **confirmed**, see [R3-04](#r3-04) | The decision recorded as "off until M6b" is not what the code does. |
| Verify item 6: zip slip, saves, names | **partly fixed** | "Download all" sanitizes every name before the archive is built (`Core/Mail/Attachments/AttachmentSaver.cs:76`). Left: [R3-10](#r3-10). |
| Verify item 11: unified with two logins | **confirmed** and worse than recorded | [R3-01](#r3-01): no row of "All inboxes" can be opened. |
| Verify item 12: a failed chunk stops the rest | **confirmed** | [R3-03](#r3-03). |
| Verify item 26: sign-out | **partly fixed** | [R3-11](#r3-11). |
| m2-close-a item 1: "the list is the truth" (notification order) | **fine** | Read in `MessageListViewModel`, `SidebarViewModel.OnListChanged`. |
| m2-close-a: tags (`Presentation/Tags/TagService.cs`, `Core/Jmap/KeywordSyntax/KeywordRules.cs`) | **fine** | Keyword syntax is RFC 8621 4.1.1, patch paths are escaped per RFC 6901. `TagRules` itself was not read. |
| m2-close-b: snippet race, snippet marks | **fine** | The sequence check drops a late answer. Not for unified rows ([R3-01](#r3-01)). |
| hotfix-writes: real drag and drop (`Presentation/DragDrop/DropController.cs`) | **fine** in the controller | Folder ids are mapped per account for rows of another login (`Core/Mail/Unified/UnifiedMailWriter.cs:30-31`), as Rust `main.rs:424-432`. The view side (`SidebarView.xaml.cs`, `MessageListView.xaml.cs`) was not read; real dragging stays a hand test. |
| J-051 `collapseThreads`, `ListFilters` flattening, import method errors | **fine** | As read in `Core/Mail/Service/{ListPageFetcher,ListFilters}.cs`, `Core/Mail/Import/MailImportLoop.cs`. |

<a id="new"></a>
## 2. New confirmed problems, most serious first

<a id="r3-01"></a>
### R3-01 (high, correctness): no message of "All inboxes" can open in the reader

**What.** Every row of the unified inbox, also the rows of the active login, carries a composite id
(login, shared account, server id). The write path takes it apart again; the read path does not. Opening a row sends the composite
id to the active account's server as an email id, which answers "not found". The same root breaks the thread view, the search
snippets, the read receipt and the body cache key for these rows.

**Evidence.**
- `Core/Mail/Unified/UnifiedFetcher.cs:88-97` (`Tag`): `Id` and `ThreadId` become `UnifiedRows.RowId(login, shared, id)` for every source.
- `Presentation/Mail/Loading/MessageLoader.cs:249`: `_fetcher.FetchAsync(context.AccountKey, context.Mail, row.Id, ...)` (the active account, the row id as is).
- `Presentation/Mail/Loading/MessageFetcher.cs:50, :71` to `Core/Mail/Service/MailService.cs:139-165`: `Email/get` and `Thread/get` with `accountId = client.Session.MailAccountId` and that id.
- `UnifiedRows.ParseRow` and `RealId` are used only by `UnifiedListSource`, `UnifiedMailWriter` and `UnifiedTargetResolver`: nothing on the read side.
- `Core/Search/SearchSnippetFetcher.cs:27-33` (active client, composite `emailIds`); `Presentation/Reader/ReaderBannerActions.cs:85` (the receipt goes through the active account's session).
- No test and no hidden scenario opens a unified row: `spec/feel/M2/unified/feel.py` only runs `unified on`; the `unified` automation command has no open step.
  The screenshot `docs/milestones/M2-close-a-screens/bento-dark-unified.png` shows a message in the reader under "All inboxes" with an empty body area; it does not settle the question either way (the message may have been opened before the switch).
- Rust keeps the owner next to the id and routes reads by it (`<rust-repo>/src/main.rs:426-427` `mail::owner`, `mail::shared_account`).

**Fix.** Resolve a row once into (login client, JMAP account id = the shared account or that login's `MailAccountId`, real id) and
use that triple for body, thread, blobs, snippets and the receipt; key the body cache by login, account and real id. Add a FakeJmap
test with two logins that opens a row of each through `MessageLoader`, and a hidden step (`unified on`, open row 1, assert body text).

<a id="r3-02"></a>
### R3-02 (medium): "Download (.eml)" and "View source" from the message row menu send an empty account id

**What.** `MailTarget` has `AccountKey`, `AccountId`, `EmailId` and `RowId`, but its constructors fill them differently, and
`AttachmentSaver` trusts `AccountId`. The resolver that is always registered puts the shared account or an empty string there.

**Evidence.**
- `Presentation/Unified/UnifiedTargetResolver.cs:19-20`: `AccountId = shared ?? ""`, `EmailId = row.Id` (the row id), `AccountKey` = the active login (wrong for a row of another login).
- `Core/Mail/Attachments/AttachmentSaver.cs:109, :128, :146`: `accountId = message.AccountId` in `Email/get` and in the download URL.
- `Presentation/Menus/MailMenus.cs:129-130`: the row menu offers both commands.
- Other constructors: `App/MailBody/Automation/RxSaveCommand.cs:32` and `App/Views/Reader/ReaderMenuOpener.cs:23-24` (the real id, which is why the reader's own menu and the automation path work), `App/Views/Menus/ContextPointerRouter.cs:213` (the account key as account id), `Presentation/Mail/Loading/LiveMailActions.cs:29` and `Presentation/Actions/MailActionService.Tags.cs:32` (empty).
- Consumers that already cope with an empty id: `Presentation/Rules/QuickRuleService.cs:38`, `Core/Mail/Import/MailExportService.cs:45`, `Core/Mail/Parse/EmailParseOpener.cs:50`, `Core/Mail/Tnef/TnefExpander.cs:58`.
- `tests/Barbican.FakeJmap/FakeApi.cs:77-80` answers `accountNotFound` for a wrong id; `AttachmentSaverTests` build the target with the real id, so the tests pass.

**Fix.** One contract, written on `MailTarget`: `AccountKey` the row's login, `AccountId` the JMAP account id (never empty),
`EmailId` the server id, `RowId` the list id; writers route by `RowId`. Until then: `AttachmentSaver` falls back to
`client.Session.MailAccountId` and strips a row id. Test: run both commands with the target the row menu builds.

<a id="r3-03"></a>
### R3-03 (medium): a later chunk that fails hides the chunks that were applied (verify item 12)

**What.** A write over more ids than the server's `maxObjectsInSet` is sent in chunks. When chunk 2 fails, the method returns only
the error. The caller then puts every removed row back and records no undo, although the rows of chunk 1 were moved or destroyed on
the server. The list is wrong until the next sync, and for a move there is nothing to undo with.

**Evidence.** `Core/Mail/Write/MailWriter.cs:115-139` (`SetChunksAsync`: `return Result<SetReport>.Fail(r.Error!)` inside the loop;
`DestroyAsync` likewise); `Core/Mail/Unified/UnifiedMailWriter.cs:94` onwards (the same for groups of logins);
`Presentation/Actions/MailActionService.Removal.cs:41-47, :77-100` (restore of the whole edit on failure, undo only on success).
Bulwark reports per chunk (`<bulwark>/lib/jmap/client.ts` `batchMoveEmails`, about `:4134`).

**Fix.** Return the partial `SetReport` together with the error (ids applied, ids refused, ids not sent). Restore only rows that
were not applied, record undo for the applied ones, and say "N of M moved". Test with FakeJmap failing the second request.

<a id="r3-04"></a>
### R3-04 (medium, a recorded decision the code does not follow): user plugins are loaded

**What.** `docs/milestones/M2.md:229` (default 7): "User plugins in `<config>/plugins`. Default: off until the M6b permission UI.
Only the bundled spam score is on." The code passes the user folder to the host, and the host loads every library folder it finds
there at start, with full trust, in the app's process.

**Evidence.** `App/Composition/Features/UnifiedRegistration.cs:136-137` (`BundledRoot` and `UserRoot` are both set, no setting
in between); `Core/Plugins/Native/NativePluginHost.cs:73-85` (both roots are walked); `PluginStartup` loads at start. The load
context is not a boundary: `Core/Plugins/Native/PluginLoadContext.cs:70-73` refuses a reference to the app's assemblies by name,
which stops accidents only (reflection over the default context reaches everything, and the whole framework is available).

**Fix.** Leave `UserRoot` null unless a setting (default off) says otherwise, or until M6b; a test in `CompositionGraphTests`
that a library in `<config>/plugins/x` is not loaded. Correct the comment in `PluginLoadContext` ("a plugin sees the API, never the
app's internals" reads like a guarantee).

<a id="r3-05"></a>
### R3-05 (medium-low): an IO error leaves import or export "running" for ever, and commands that throw are not caught

**What.** `MailTransfer` catches only cancellation; `Finish` is not in a `finally`. A full disk, a file in use or a path that went
away during an export or import throws, the state stays "running", and the entries stay disabled until the app restarts.
More general: no command runner catches what a command throws.

**Evidence.** `Presentation/Unified/MailTransfer.cs:67-72, :111-116, :137`. Runners: `Presentation/Input/ShortcutCommands.cs:58`
(`_ = command.ExecuteAsync(...)`: the exception is lost, nothing is shown), `App/Views/Rail/RailView.xaml.cs:47` (same),
`Presentation/Menus/ContextMenuController.cs:285-297` (only `OperationCanceledException`), `Presentation/Palette/PaletteController.cs:217`,
`App/Views/Actions/SelectionBarView.xaml.cs:34`, `App/Views/List/MessageListView.xaml.cs:263`, `App/Views/Sidebar/SidebarView.xaml.cs:163`
(awaited without a catch). `Presentation/Commands/DelegateAppCommand.cs` has no catch either.
Throwing sources that exist today: `Core/Mail/Attachments/AttachmentSaver.cs` `WriteAsync` (no IO handling), `App/MailBody/SaveTargets.cs:35`
(`FileMode.CreateNew` after an `Exists` loop), `Core/Mail/Import/MailExportService.cs:29`.

**Fix.** `try { ... } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Finish(message) } finally`
in `MailTransfer`; turn IO errors into `Result.Fail` in `AttachmentSaver.WriteAsync`; one shared `RunAsync(command, context)` used
by every runner that turns an exception into an error toast and a crash-log line.

<a id="r3-06"></a>
### R3-06 (low-medium, security, what is left of R2-07): a message that never passed port 25 can forge the trace block

**What.** `AuthTrace` trusts an Authentication-Results header only directly below the topmost `Received` whose "by" host is the
authserv-id. That holds when the server wrote both. Stalwart writes them only for a message that came in on port 25. A message
submitted on the submission port by another user of the same server, sent through JMAP, or added with `Email/import` (also by the
app's own mbox import) keeps whatever its sender wrote on top: `Received: ... by <server>` and
`Authentication-Results: <server>; dkim=pass; dmarc=pass` then form the first block. Result: a green verdict, and the one-click
unsubscribe gate opens (`Presentation/Reader/UnsubscribeBannerSource.cs:31` uses `DkimGate.ServerDkimPass(snapshot.ServerResults, from)`).
The first such message an account ever opens also chooses the pin.

**Evidence.** Stalwart 0.16.27 `crates/smtp/src/inbound/data.rs:482-501` (the two headers are written only when `add_received`
and `add_auth_results` are true), `crates/registry/src/schema/structs_impl.rs:28843-28875` (their defaults: true only
`if local_port == 25`). `Core/Mail/AuthResults/AuthTrace.cs:1-12` (the header comment says the server's Received "comes first";
that is true for inbound SMTP only) and FABLE-REVIEW "M2 verify" item 2(a).

**Fix.** There is no complete client-side fix (every marker in a header is text the sender can write). Proposed: accept it for a
server where every account is the user's own (decision E-9), correct the comment and item 2(a), give no verdict for messages whose
only folders are Sent or Drafts (item 2(b): today the roles are loaded after the first open and then kept for the whole run,
`Presentation/Mail/Loading/ReaderDepth.cs:122-149`), and never learn a pin from a message outside the Inbox.

<a id="r3-07"></a>
### R3-07 (low): "is this row in Trash" is answered with the active account's folder tree

`Presentation/Actions/MailActionService.Moves.cs:128, :174-179` (`IsPermanent`, `InRole`) look a unified row's folder ids up in the
active account's tree. Between two Stalwart accounts with the default folders this gives the right answer by coincidence
(`crates/email/src/mailbox/mod.rs:13-18`: Inbox 0, Trash 1, Junk 2 in every account). On another server, or with reassigned roles,
a Delete can become a permanent destroy. Fix: judge by the row's own account tree (the writer already has the per-account map);
never destroy on a role read from another account.

<a id="r3-08"></a>
### R3-08 (low-medium, before the I-230 view is built): archived items

`Core/Mail/Archive/ArchivedItems.cs:82-84` compared with `<bulwark>/lib/jmap/client.ts:13411-13500` and
`<bulwark>/lib/recovery/archived-items.ts:1-60`:
- no `calculateTotal: true`: Bulwark's comment says Stalwart then answers with the oldest items of the window, not the newest;
- no filter on `@type == "Email"` and no drop of items of other accounts after the unfiltered retry (`:32-35`);
- the doc comment names the wrong restore target (the server restores to the Inbox, unread, without keywords);
- `IsStalwart` (`:24`) is not "Enterprise": hide the entry when the method answers `forbidden` or `unknownMethod`.

<a id="r3-09"></a>
### R3-09 (low-medium): searching inside a tag view leaves the tag, and unified search skips the grammar

- `Presentation/Mail/MessageListViewModel.cs:233-235`: `SearchAsync` takes only `_query?.MailboxId`. In a tag view the search
  runs without the tag filter, the header still names the tag, and clearing the box does not bring the tag list back.
  Fix: carry `TagId` (and the excluded folders) into the search query and restore the tag view on clear.
- `Core/Mail/Unified/UnifiedFetcher.cs:99-104`: the text goes to the server as typed; `from:`, `has:attachment`, `in:` are not read
  as in the folder search (`Core/Search/GlobalSearch.cs`). Fix: build the filter with the same shaper.

<a id="r3-10"></a>
### R3-10 (low): saves

- "Save as…" opens no dialog: `App/Composition/Features/ReaderRegistration.cs:53-55` wires `AttachmentSaver` to `SaveTargets.PickAsync`,
  which writes straight into `UserProfile/Downloads` (`App/MailBody/SaveTargets.cs:29`), a hard-coded path that ignores a moved
  Downloads folder. Rust did the same (`reader.rs:803`), but the row promises a choice. Decision E-11.
- Windows reserved names (`NUL.txt`, `CON`, `COM1.pdf`) pass `ReaderNames.SanitizePart` and `MailExportService.UniquePath`
  (`Core/Mail/Import/MailExportService.cs:136`).
- TNEF names: `Core/Mail/Tnef/TnefExpander.cs:96` hands `file.FileName` on; it is safe only where the saver sanitizes
  (Download all does; check the single save).
- "View source" decodes the whole message before the 2 MiB cut (`Core/Mail/Reader/SourceCut.cs`).

<a id="r3-11"></a>
### R3-11 (low, privacy on a shared computer): what a sign-out leaves behind

`Core/Accounts/AccountManager.Maintenance.cs:113-130` disposes the sessions, clears the body cache and removes the vault entry.
Nothing listens to `onAccountRemove` in the app itself, so, as far as read, the pinned authserv-id, the trusted senders, the
avatar cache, the WebView2 profile (cached remote images) and plugin data of that account stay in the profile. Bulwark removes
such per-account state (`<bulwark>/lib/sign-out-cleanup.ts:26-75`). **Not confirmed file by file**: list what the profile holds
per account, then decide (E-12). Also: `MoveAsync` (`:77-92`) changes the order in memory and raises `Changed` before the store
saved; a failed save leaves the two different.

<a id="r3-12"></a>
### R3-12 (low): smaller points

- `App/Composition/CrashGuard.cs:30` computes the log folder from the environment before `WebViewEnvironment.Prepare` sets
  `BARBICAN_PROFILE` for a hidden run that was started without it: crash logs of such a run land in the user's data folder.
  `tools/auto-run.ps1` always sets the profile, so this needs a hand-started hidden run.
- `App/Composition/HiddenWindowOptions.cs` calls the non-hidden default profile "default"; `Core/Storage/ProfilePaths.cs` calls it "dev".
- `ReadOnlyPolicy.InLocalFolder` checks only the parent folder's name.
- `MailWriter.EmptyMailboxAsync` stops when a page returns fewer ids than asked (`Core/Mail/Write/MailWriter.cs:177`):
  a server that caps a page below the limit ends the loop early.
- `MarkTagReadAsync` includes Trash and Junk and only the active account.
- `MailImportService.MayAddAsync` ignores a shared account id; `WriterImportServer.MessageIdsInAsync` reads every Message-ID
  of the target folder with no limit, so the start of an import grows with the folder (a likely part of the reported slowness;
  the cause of the import stall itself was **not found**).
- Export pages by position; a delivery during the export can skip or repeat a message. Use `anchor`.
- `Core/Net/PublicAddress.cs` leaves out ISATAP, `2001:2::/48` and `100::/64`.
- Mail page: the Nordic gutter is 44 px in the prototype (`prototype/winui/MailHtml.cs:23`) and 16 px in the port
  (`Core/Mail/Body/MailPageBuilder.cs:47-52`), and the 600 px measure is on `body` instead of a centred child; text and link
  colours of the themed page are literals (`#d4d4d4`, `#f0577a`, `:59-61`), not the tokens. Check against `M1-look-gap.md` (not read).
- `PluginLoadContext` takes the first library by name as the plugin's main assembly (`NativePluginHost.cs:261`): a dependency
  that sorts before the plugin breaks dependency resolution.
- CLAUDE.md still says agents use the `dev` profile and that at most three agents compile (the user's file; E-13).
- The Bento dialog frame issue from the hand test could not be reproduced by reading.

<a id="decisions"></a>
## 3. Decisions for the user

| # | Question | Proposed |
|---|---|---|
| E-9 | R3-06: a sender on the same server (or an imported message) can forge a "passed" verdict. Accept? | Accept while every account on `<server>` is the user's own; say so in the code and in the trust banner's help; no verdict in Sent and Drafts. |
| E-10 | R3-04: load libraries from `<config>/plugins` now, or only after the M6b permission UI (the recorded default)? | Off until M6b. |
| E-11 | R3-10: "Save as…" writes to Downloads without asking. | Show the system save dialog (`App/Shell/SystemFilePicker.cs` exists), or rename the row "Save to Downloads". |
| E-12 | R3-11: remove an account's local state (pins, trusted senders, avatars, WebView2 cache) at sign-out? | Yes, as Bulwark; keep device settings. |
| E-13 | CLAUDE.md lines about the `dev` profile and "at most 3 agents compile" are stale. | The user edits the file. |

D-1 (rewriting public history) stays not approved; nothing found here needs it (section 6).

<a id="gate"></a>
## 4. What blocks the M2 gate

1. [R3-01](#r3-01): the unified inbox is M2 scope and its rows cannot be read.
2. [R3-02](#r3-02): two menu commands of the message row fail on every account.
3. [R3-03](#r3-03): a partly applied bulk write is shown as not applied.
4. [R3-04](#r3-04): either the code or the recorded decision must change (E-10).
5. [R3-05](#r3-05), the `MailTransfer` part: one IO error disables import and export until restart.

Not blocking: R3-06 to R3-12 (R3-08 before the archived-items view ships).

<a id="not-reviewed"></a>
## 5. Not reviewed

- Anything running: no build, no test run, no hidden run, no live check, no feel numbers. Linux as a whole.
- JSCalendar and JSContact: the calendar and contacts apps are not built (M4 to M6); `Core/Calendar/Invitations` was not read.
- `AccountStore`, `SecretAccountVault` (R2-06), the OAuth flow (review 2), `MailSync` stop paths, `PluginHookBridge`, `TagRules`,
  Sieve and rules, the palette, the key router, hover strip, the views' drag and drop code, `motion:Tip`.
- The documents `PORT-PLAN.md` sections 3 to 6, `ROADMAP.md`, `adr/README.md`, `M1-plan.md`, `M1.md`, `spec/README.md`, `M2-plan.md`,
  `M1-look-gap.md`: not read this round, so statements here are checked against the code and `M2.md`, not against those.
- Test quality and the tools scripts (only `tools/privacy-check.sh` and parts of `tools/auto-run.ps1`).
- Verify item 8 (credential copies in an agent profile): credential stores are not opened by a reviewer.

<a id="privacy"></a>
## 6. Privacy

- **History, text**: the patches and messages of all 187 commits on every ref were searched with the private pattern of
  `.local/privacy-patterns.txt`: no hit. No Windows home path. Every e-mail address in any patch is on the allowlist of
  `tools/privacy-check.sh` (fictional and public-service domains). All author and committer identities are the GitHub noreply address.
- **Images**: 4 of the screenshots added in M2 were looked at (`M2-close-a-screens/bento-dark-unified.png`,
  `M2-screens/{reader-more-menu,search-snippet-marks,reader-body}-bento-dark.png`): sample data only. The other images, the icons
  and the ADR pictures were not looked at this round; `privacy-check.sh` does not read images.
- This branch adds two text files; `sh tools/privacy-check.sh --worktree` was run before the commit.
