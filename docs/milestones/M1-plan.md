# M1 plan: read-only mail client on the test account

- Written 2026-11-17 by the M1 plan stage (Opus, PORT-PLAN.md §6.1 step 1), on branch `m1`.
- Scope and exit: [PORT-PLAN.md](../PORT-PLAN.md) §5 "M1"; start list: [ROADMAP.md](../ROADMAP.md)
  "M1: what it starts with"; decisions: [adr/](../adr/README.md) 0004-0008 with their "M0 review"
  sections; open review items: [FABLE-REVIEW.md](../FABLE-REVIEW.md).
- Integration branch: **`m1`** (from `main` at `fb6c3ab`). Nothing merges to `main` until the user's
  M1 hand test and OK (§6.1 step 7).
- Accounts and paths are placeholders (CLAUDE.md, `.local/accounts.md`): `<readonly-account>`,
  `<test-account>`, `<server>`, `<rust-repo>`, `<bulwark>`, `<stalwart-zip>` (Stalwart 0.16.27 source,
  paths below are inside it), `<downloads>`.

## 0. Summary

| Order | Key | Title | Model | Hard | Wave | Depends on |
|---|---|---|---|---|---|---|
| 1 | `f1-foundation` | Uno.Sdk 7, automation foundation, Linux shell security | Opus | yes | fixed stage F1 | m1 |
| 2 | `f2-spec-tests` | 74 red M1 tests, contract skeleton, hooks.csv and slots.csv | Sonnet | no | fixed stage F2 | F1 |
| 3 | `jmap` | JMAP core: transport, session, requests, methods, sync, push, state codec, FakeJmap, trace | Opus | yes | 1 | F2 |
| 4 | `mailcore` | Pure mail modules, ViewModels, ListSync, KeyRouter, hook bus | Sonnet | no | 1 | F2 |
| 5 | `accounts` | Accounts, sign-in (password + TOTP, OAuth), secrets, profiles, prefs, body cache | Opus | yes | 2 | wave 1 |
| 6 | `shell-ui` | XAML: tokens and design in XAML, shell, rail, sidebar, Bento tiles, list, reader, sign-in views; design docs | Sonnet | no | 2 | wave 1 |
| 7 | `body` | Mail body view, msgload orchestration, prefetch, dark mail, a11y CSS, feel gates | Opus | yes | 3 | wave 2 |

- **Merge order into `m1`:** `f1-foundation`, `f2-spec-tests`, `jmap`, `mailcore`, `accounts`,
  `shell-ui`, `body`. Each wave starts from `m1` **after** the previous wave is merged, built and green
  on both OSes, so a stream consumes real code from earlier waves, never a moving branch.
- **Limits held:** at most 2 streams per wave, at most 1 Opus stream per wave (Opus-only implementation
  limit, §6.3), so at most 2 agents compile at once (plus a reviewer that does not compile). One Linux
  VM build at a time: streams queue on `tools/linux.ps1`.
- **Branch names: `m1-<key>`, not `m1/<key>`.** The integration branch `m1` exists, and git cannot
  hold `refs/heads/m1` and `refs/heads/m1/<key>` at once. Worktree: `<downloads>/wt-m1-<key>`. Push the
  branch at creation and after every commit.

## 1. Rules every M1 agent repeats (CLAUDE.md, §6.4, and the M1 hard rules)
1. Read CLAUDE.md, AGENTS.md, `.local/accounts.md`, this plan, and the PORT-PLAN sections your brief
   names. `git config user.email` is the noreply address; `git config core.hooksPath .githooks`.
2. **Never touch the user's real screen, mouse or keyboard.** M0 put app windows on the real screen three
   times; it must not happen again.
   - Any window you launch is created off-screen and non-activating **from its very first frame**
     (ADR 0007; after F1 this is the AUTOMATION build or `--smoke`), and every popup or flyout is
     constrained to the window (the F1 popup guard fails closed).
   - If you cannot guarantee that for a check, do not launch it: record it as a hand test in your
     section of the M1 report.
   - No `SendInput`, `SendKeys`, `mouse_event`, `keybd_event`, `SetForegroundWindow`, `SetCursorPos`,
     FlaUI `Mouse`/`Keyboard`, xdotool. The `net10.0-desktop` head is launched only under the VM's
     private Xvfb (`tools/linux.ps1 xrun`), never on Windows.
   - **Never open a real browser** (OAuth): automation builds replace `IBrowserLauncher` with the fake
     that follows the authorize URL itself (§5 `accounts`).
3. **Accounts.** `<readonly-account>` is strictly read-only (`JmapClient.ReadOnly = true`, mark-as-read
   suppressed). M1 is a read-only client: **every M1 live run is read-only on both accounts.** Writes
   (only on `<test-account>`, cleaned up) are not part of M1. Never read `<admin-key-file>`. Never type
   passwords; never sign in, sign out or clear the Rust app's credentials.
   - Live checks only through the ADR 0008 one-time read-only credential import into a dev or
     automation profile, **and only for `kind: password` entries**. An imported OAuth refresh token is
     never refreshed by an agent: a refresh rotates the token and would sign the Rust app out
     (B-OAUTH-015). Where that is not possible, test against FakeJmap or recorded fixtures and record
     the live check as a hand test.
   - Never change Stalwart server settings.
4. Don't kill processes you didn't start. No OS changes.
5. **Builds** only through `tools/build.ps1` (2 machine-wide slots; never bypass them) and
   `tools/linux.ps1` (one VM build at a time). Never `dotnet build`/`test` directly. At the end:
   `tools/build.ps1 shutdown` (it runs `dotnet build-server shutdown`).
6. **Feel:** no flicker, no blank, white or black frame; old content stays until new is ready; nobody
   waits for an animation; measure with the probes (`wgc` probe, `burst` + `tools/flicker.py`,
   `tools/motion.py`), never by eye.
7. **Commits:** `sh tools/privacy-check.sh --worktree` before every commit; the message says the
   behaviour decided and why and ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`;
   push after every commit. Update `spec/` status in the same commit as the code
   (`python tools/spec-check.py --write`).
8. **Code rules:** one type per file (MA0048; this also settles FABLE #15: the `JmapError` hierarchy is
   one record per file), one view per file, one ResourceDictionary and one registration file per
   feature, records for models, `Result<T>` for expected failures, `TimeProvider` for time, `x:Bind`
   only. Every Core file starts with the Bulwark file:line, RFC section and Stalwart path it mirrors.
   Ported Rust tests carry `[RustTest("<rust_test_tag>")]` exactly as `spec/rust-tests.csv` has it.
9. **Fable log:** append to `docs/FABLE-REVIEW.md` under `## M1 stream: <key>` as you go, one bullet per
   item (what, why, exact paths and commits), and list the items in your final return.
10. **Plugins:** no plugin host in M1. Fire your area's hooks through `IHookBus` (§3.4); every hook row
    with `milestone = M1` in `spec/plugins/hooks.csv` gets a call site in its owner's stream.
11. **Stay inside your owned files (§3).** If you need a change in a file you do not own, put a note
    under `## Requests to other streams` in your Fable section and work around it behind your own
    interface; the integrator applies it.

## 2. What M1 must close

### 2.1 Rust tests (T-): 76 rows (`python tools/spec-check.py --milestone M1`)
| Owner | Rows |
|---|---|
| F1 | T-176 `main::blocked_remote_content_includes_stylesheets_and_fonts` (the CSP builder, ported with the Linux shell security) |
| F2 | T-159 `lite::renders_email_html`: no C# test; a D- entry for the drop (PORT-PLAN §1 "Deliberate drops", Appendix A, B-READ-046) with status `dropped(D-###, user OK pending)`; the user confirms at the M1 gate |
| jmap | T-128..T-145 (18: `jmap::*`, including the M3-facing pure functions `reschedule_fallback_order`, `from_override_header_identity_and_envelope`, `delayed_send_needs_futurerelease`) |
| mailcore | T-146..T-158 (13 `listsync::*`), T-280..T-284 (5 `tags::*`), T-011..T-014 (4 `auth::*`), T-160..T-162 (3 `mail::*`), T-008..T-010 (3 `attachments::*`), T-001..T-003 (3 `a11y::*`), T-184 (`motion::setting_wins_over_system`) |
| accounts | T-025..T-034 (10 `bodycache::*`), T-192..T-198 (7 `oauth::*`), T-259 (`settings::import_export_reset_roundtrip`) |
| body | T-074..T-076 (3 `dark_mail::*`), T-185, T-186 (`msgload::*`), T-175 (`mailview::forwarded_keys_parse`) |

F2 writes all of them except T-176 (F1) and T-159 (no test) as red tests in the owner's test folder
(§3.1); each stream turns its rows green.

### 2.2 JMAP requirements (J-): sections 1-4 and the read part of 5
| Owner | Rows |
|---|---|
| jmap | J-001..J-003, J-005..J-019, J-021..J-028, J-031, J-032 (result handling only; no writes in M1), J-035, J-037..J-042, J-045 (role map; creating the archive mailbox is M2), J-049, J-050, J-052, J-054, J-055 |
| jmap + accounts | J-020 (OAuth bearer renewal on 401 for API, download and event source: jmap's transport calls `IAccessTokenSource`, accounts implements the renewal) |
| mailcore + shell-ui | J-046 (unsubscribed folders shown muted) |
| body | J-053 (isTruncated surfaced in the reader) |
| jmap: `n/a` with the Rust reason kept | J-043 PushSubscription (no public callback URL), J-044 WebSocket (EventSource is enough; Bulwark does not use it) |

Each row moves to `cs_status = done` with `cs_test` naming the test, and with the Bulwark and Stalwart
refs re-verified (FABLE #19: the J- table inherited a stale document).

### 2.3 Interactions (I-): rail and account switching, sidebar display, list display and keyboard, reader display
The catalogue tags 55 I- rows M1, but several are actions that belong to later milestones. Rule: the M1
owner closes the **display** part (the control is present, styled from the mockup, has an AutomationId
and a name, and is disabled or hidden where its action is not built yet) and sets the row to
`deferred(M<k>)` for the action, with the reason. Nothing is left silently `todo`.

| Rows | Closes in M1 (owner) | Action deferred to |
|---|---|---|
| I-001, I-002, I-005, I-006, I-010 (account popover, switching, rows, add account) | full (accounts VM, shell-ui view) | M2: set default, sign out one or all, manage |
| I-020, I-021, I-028, I-030 (rail badge, tooltips, settings gear, storage ring) | full; the gear opens nothing yet (shell-ui) | M6: settings pages |
| I-038, I-039, I-044, I-045, I-046, I-059 (folder click, expand, muted, sub-unread dot, section collapse, starred/show more) | full (mailcore VM, shell-ui) | I-059 Labels "+": M2 |
| I-033 Compose button, I-047 section gear, I-058 Scheduled row, I-062 vacation banner | display only | M3, M6, M3, M6 |
| I-040, I-056, I-063, I-122, I-215, I-223 (context menus) | none | M2 (menus stream; the constrained `Popup` host) |
| I-098, I-099, I-101, I-103, I-104 (login screen, discovery, OAuth status, remember me, 2FA, client id) | full (accounts VM, shell-ui view) | I-101 language picker: M10 (i18n) |
| I-113 trusted senders modal | none | M2 (reader images trust) |
| I-119, I-146, I-156 (row click opens, load more on scroll, keys) | open, paging, j/k/arrows, `/` (basic search), Shift+G (mailcore, shell-ui, body) | the action keys of I-156 (`s`, `e`, `!`, `u`/Shift+I, Ctrl+A): M2 |
| I-129 attachment chips in the list | display (mailcore, shell-ui) | click to download: M2 |
| I-130 verification code chip | none | M2 (otp) |
| I-194..I-199 reader toolbar, I-217 footer buttons | display, disabled (shell-ui) | M2 (actions), M3 (reply/forward) |
| I-203, I-206, I-209, I-214, I-222, I-230 (header collapse, details panel with auth chips, load external content, plain-text notice, conversation cards, retention/retry) | full (body, shell-ui); I-209 per message only | I-209 trust per sender: M2 |
| I-204, I-205, I-231 (sender card, contact sidebar) | sender name and address card, read-only | M5 (contact data) |
| I-225 attachment tiles | display, open through the shell link router is M2 | M2 |
| I-229 tag chip X | display | M2 |
| I-233 plugin reader surfaces | none | M6b |

### 2.4 Plugin hooks and slots
F2 creates `spec/plugins/hooks.csv` and `spec/plugins/slots.csv` from `<bulwark>/lib/plugin-hook-catalog.ts`
and `lib/plugin-slot-catalog.ts` (a quick count finds 218 hook entries in 27 groups; PORT-PLAN says 219,
so F2 reconciles and records the true number). F2 sets a `milestone` column per hook. M1 owners by group:

| Group | Owner |
|---|---|
| `mail-sync` | jmap |
| `auth` (the sign-in and session events M1 has) | accounts |
| `mail-list`, `mail-mailboxes` (select and display, not create/rename), `mail-search`, `navigation`, `keyboard`, `app`, `ui` (the M1 surfaces) | mailcore |
| `theme` | shell-ui |
| `mail-read` (open, render transforms) | body |

Slots are catalogued only; no slot is rendered in M1.

### 2.5 Behaviour rows (B-)
Each stream reads the `spec/behaviours/*.md` files of its area (named in its brief) and marks the rows it
implements `ported` in the same commit, or `deferred(M<k>)`. Known overlap: B-SYNC-001..011 equal B-LST
rows; mailcore closes both ids.

## 3. Parallel work without shared files

### 3.1 File ownership
After F1 and F2, every file in the repository has exactly one M1 owner. Folders (all files below them) go
to their owner; files not listed stay with the integrator.

| Owner | Source | Tests |
|---|---|---|
| f1-foundation (then frozen; the integrator owns them afterwards) | `global.json`, `Directory.Packages.props`, `Directory.Build.props`, `Barbican.slnx`, every `*.csproj` (creating the new projects of §3.2), `src/Barbican.App/App.xaml(.cs)`, `src/Barbican.App/Composition/AppComposition.cs`, `src/Barbican.App/Composition/HiddenWindow*.cs`, `src/Barbican.App/Platforms/**`, `src/Barbican.Automation/**`, `tools/build.ps1`, `tools/build.sh`, `tools/linux*.{ps1,sh}`, `eng/**`, `BannedSymbols.txt`, `tools/package-portable.ps1` | `tests/Barbican.Automation.Tests/**`, `tests/Barbican.Build.Tests/**` |
| f2-spec-tests (then each file passes to the owner named in its folder) | `spec/plugins/**`, the D- entry for lite, the contract skeleton of §4 (files land in the owners' folders below) | the red tests, in the owners' folders below |
| jmap | `src/Barbican.Core/Common/**` (Result, Unit), `src/Barbican.Core/Jmap/**`, `src/Barbican.Core/Mail/Model/**`, `src/Barbican.Core/Mail/IMailService.cs`, `src/Barbican.Core/Mail/IMailSync.cs`, `src/Barbican.Core/Mail/Service/**`, `src/Barbican.Core/Sync/**`, `tools/trace-diff.py`, `spec/traces/**` | `tests/Barbican.Core.Tests/Jmap/**`, `tests/Barbican.Core.Tests/Sync/**`, `tests/Barbican.FakeJmap/**`, `tests/Barbican.Jmap.Live/**`, `tests/fixtures/jmap/**` |
| mailcore | `src/Barbican.Core/Mail/{Tags,AuthResults,Attachments,Rules,Glance,Grouping}/**`, `src/Barbican.Core/Settings/{Accessibility,Motion}/**`, `src/Barbican.Core/Plugins/**`, `src/Barbican.Presentation/{Common,Input,Shell,Mail,Composition}/**` except the body files below | `tests/Barbican.Core.Tests/{Mail,Settings/Accessibility,Settings/Motion,Plugins}/**`, `tests/Barbican.Presentation.Tests/{Common,Input,Shell,Mail}/**` |
| accounts | `src/Barbican.Core/{Accounts,Auth,Storage}/**`, `src/Barbican.Core/Settings/{Prefs,SettingsSchema.cs,settings-schema.json}`, `src/Barbican.Core/Mail/BodyCache/**`, `src/Barbican.Platform/**`, `src/Barbican.Platform.Windows/**`, `src/Barbican.Platform.Linux/**`, `src/Barbican.Presentation/Accounts/**`, `src/Barbican.App/Composition/PlatformServices.cs` | `tests/Barbican.Core.Tests/{Accounts,Auth,Storage,BodyCache,Settings/Prefs}/**`, `tests/Barbican.Core.Tests/Settings/SettingsSchemaTests.cs`, `tests/Barbican.Platform.Tests/**`, `tests/Barbican.Presentation.Tests/Accounts/**` |
| shell-ui | `src/Barbican.App/{Shell,Views,Theme,Motion,Controls,Styles}/**`, `src/Barbican.App/Themes/*.xaml` that are not generated, `src/Barbican.App/Composition/ViewServices.cs`, `design/README.md`, `design/MOTION.md`, `spec/layout/**` (C# metrics), `tools/layout/**` (flicker/motion layouts for the C# app) | `tests/Barbican.App.Tests/{Shell,Views,Theme}/**` (if F1 creates the project; otherwise Presentation tests for the view logic) |
| body | `src/Barbican.App/MailBody/**`, `src/Barbican.App/Assets/mail/**`, `src/Barbican.Core/Mail/Body/**` (MailDocument, CSP, dark mail, a11y CSS injection, cid to data:, shell message parser), `src/Barbican.Presentation/Mail/Loading/**` (MessageLoader, Prefetch), `spec/feel/M1/**` | `tests/Barbican.Core.Tests/Mail/Body/**`, `tests/Barbican.Presentation.Tests/Mail/Loading/**`, `tests/fixtures/mail/**` |

F1 creates `src/Barbican.Core/Mail/Body/` (CSP builder, shell message parser) and `Assets/mail/shell.js`
changes before the body stream exists; both pass to `body`.

Generated files (`*.g.cs`, generated `Themes/*.xaml`) are never edited by hand; `tools/build.ps1
gen-tokens` regenerates them (shell-ui runs it if the generator input changes, which M1 does not plan).

### 3.2 Projects F1 creates up front (so no stream edits a csproj or the solution)
| Project | TFM | Purpose | Referenced by |
|---|---|---|---|
| `src/Barbican.Platform.Windows` | `net10.0-windows10.0.26100.0` | Credential Manager (`CredReadW`/`CredWriteW`), system theme, later the rest of §3.9 | App (windows TFM) |
| `src/Barbican.Platform.Linux` | `net10.0` | Secret Service (Tmds.DBus), portal theme | App (desktop TFM) |
| `src/Barbican.Automation` | both App TFMs | the command server, UIA client (FlaUI.UIA3), WGC capture (Win2D), probes, popup guard; **only under `Configuration=Automation`** | App, only when `$(Configuration)` is `Automation` |
| `tests/Barbican.FakeJmap` | `net10.0` library | in-process JMAP server as an `HttpMessageHandler` (session, states, `/changes`, `/queryChanges`, limits, errors, EventSource, OAuth endpoints, synthetic seed data) | test projects; Automation (for `BARBICAN_FAKE_JMAP`) |
| `tests/Barbican.Jmap.Live` | `net10.0` tests | opt-in live read-only suite (`BARBICAN_LIVE=1`), skipped otherwise | none |
| `tests/Barbican.Platform.Tests` | both TFMs as needed | secret store against a throwaway target, fake store tests | none |
| `tests/Barbican.Automation.Tests`, `tests/Barbican.Build.Tests` | `net10.0` | the Release-has-no-automation test, popup-guard tests, smoke-mode test | none |

F1 also pins every package version M1 is expected to need in `Directory.Packages.props`
(Microsoft.Extensions.Hosting/DependencyInjection, CommunityToolkit.Mvvm, CommunityToolkit.WinUI
Sizers, Tmds.DBus, Microsoft.Windows.CsWin32, FlaUI.UIA3, Win2D, xUnit/Verify already there). A stream
that needs an unforeseen package adds it inside its own marked block
`<!-- m1-<key> -->` … `<!-- /m1-<key> -->` at the end of the file and names it in its Fable section;
the integrator merges the blocks.

### 3.3 Dependency injection: one registration module per feature
- **Composition root:** `src/Barbican.App/Composition/AppComposition.cs` (F1, then frozen). It builds a
  `Microsoft.Extensions.Hosting` host and calls exactly these extension methods, in this order:
  `AddBarbicanCore()` (jmap), `AddBarbicanMailCore()` (mailcore), `AddBarbicanAccounts()` (accounts),
  `AddBarbicanPlatform()` (accounts, `PlatformServices.cs`, per TFM), `AddBarbicanViews()` (shell-ui),
  `AddBarbicanMailBody()` (body), and under `#if AUTOMATION` `AddBarbicanAutomation()` (F1).
- **Each method lives in one file owned by its stream:** `Core/Jmap/JmapRegistration.cs`,
  `Presentation/Composition/MailPresentationRegistration.cs`, `Core/Accounts/AccountsRegistration.cs`,
  `App/Composition/PlatformServices.cs`, `App/Composition/ViewServices.cs`,
  `App/MailBody/MailBodyRegistration.cs`, `Automation/AutomationRegistration.cs`. F1 creates them as empty
  methods, so the root compiles from day one and no stream ever edits the root.
- **Replaceable defaults:** a stream that ships a placeholder for a service another stream completes
  registers it with `TryAdd*`; the completing stream registers with `Replace` (example:
  `IMessageOpener`, §4.3). Registration order in the root makes the later feature win.
- **Multi-registrations instead of shared lists:** automation commands (`IAutomationCommand`), hook
  observers in tests, startup tasks (`IStartupTask`): each stream adds its own implementations in its own
  registration file; consumers take `IEnumerable<T>`.
- **Views:** only shell-ui edits `ShellPage.xaml` and the region views. The body view is the type
  `Barbican.App.MailBody.MailBodyView` (a stub from F2, owned by body) that `ReaderView.xaml` places in its
  body slot. Views get their ViewModels from DI (`ViewServices.cs`), never `new` in code-behind.

### 3.4 Shared spec files
- `spec/rust-tests.csv` columns `csharp_test`/`status` are generated by `python tools/spec-check.py
  --write`. Streams run it and commit; on a merge conflict the integrator takes either side and re-runs
  `--write` (the result depends only on the tests). Never hand-merge these columns.
- `spec/jmap.csv`, `spec/interactions.csv`, `spec/behaviours/*.md`, `spec/plugins/hooks.csv`: each stream
  edits only the rows assigned to it in §2. Line conflicts are resolved by keeping both sides' row edits.
- `docs/FABLE-REVIEW.md`: each stream appends only under its own heading.

## 4. Contracts (written by F2 as compiling stubs, owned afterwards as in §3.1)
The signatures below are the cross-stream interfaces. F2 writes them as stubs (`throw new
NotImplementedException()` or empty records) so the red tests compile. The owning stream may refine a
contract during its wave only in a way that keeps consumers compiling (add members, never rename or
remove), and records every change in its Fable section. Breaking changes go through the integrator.

### 4.1 Core (owner: jmap unless noted)
```csharp
// Core/Common
public readonly record struct Result<T> { /* IsOk, Value, Error (JmapError), Map, Bind, Match; static Ok/Fail */ }
public readonly record struct Unit;
// Core/Jmap/Errors, one record per file (MA0048)
public abstract record JmapError(string Message);
public sealed record ProblemError(int Status, string Type, string? Detail, string? Limit);      // RFC 7807
public sealed record MethodError(string Method, string Type, string? Description);
public sealed record SetError(string Id, string Type, string? Description, IReadOnlyList<string>? Properties);
public sealed record TransportError(string Message, bool Retryable);
public sealed record AuthError(string Message, bool NeedsSignIn);
public sealed record ReadOnlyViolation(string Method);

// Core/Jmap
public interface IJmapClient {
    JmapSession Session { get; }                 // the live copy; refreshed when sessionState changes
    bool ReadOnly { get; }
    Task<Result<JmapResponse>> SendAsync(JmapRequest request, CancellationToken ct);
    Task<Result<Stream>> DownloadAsync(string accountId, string blobId, string name, string type, CancellationToken ct);
    T? AccountCapability<T>(string accountId, string capability) where T : class;
}
public interface IJmapClientFactory {
    Task<Result<IJmapClient>> ConnectAsync(ServerEndpoint server, IAccessTokenSource auth, JmapClientOptions options, CancellationToken ct);
}
// Core/Auth (interface owned by jmap, implemented by accounts)
public interface IAccessTokenSource {
    ValueTask<Result<AuthenticationHeaderValue>> GetAsync(CancellationToken ct);
    // Called by the transport after a 401 with the header that was rejected (B-OAUTH-015: adopt a token
    // rotated elsewhere, retry once, never delete the stored one).
    ValueTask<Result<AuthenticationHeaderValue>> RenewAfterRejectAsync(AuthenticationHeaderValue rejected, CancellationToken ct);
}

// Core/Mail (read side of M1; per account)
public interface IMailService {
    Task<Result<MailboxTree>> GetMailboxesAsync(CancellationToken ct);
    Task<Result<ListPage>> QueryAsync(ListQuery query, ListCursor? after, CancellationToken ct);  // anchor paging
    Task<Result<EmailBody>> GetBodyAsync(string emailId, BodyFetchOptions options, CancellationToken ct);
    Task<Result<ConversationThread>> GetThreadAsync(string threadId, CancellationToken ct);
    Task<Result<IReadOnlyList<Identity>>> GetIdentitiesAsync(CancellationToken ct);
    Task<Result<QuotaInfo?>> GetQuotaAsync(CancellationToken ct);
}
public interface IMailSync {
    ChannelReader<MailSyncEvent> Events { get; }   // MailboxesChanged, ListDelta, ListReset, EmailsChanged, PushState
    void Watch(ListQuery query);                   // the open list the engine keeps in sync
    Task RunAsync(CancellationToken ct);           // push + delta loop until cancelled
}
// Core/Mail/Model: records ported from the Rust jmap.rs/mail.rs value shapes, with [JsonExtensionData]:
// Mailbox, MailboxRole, MailboxTree, EmailAddress, Keywords, EmailSummary (row data incl. threadCount,
// preview, hasAttachment, keywords, receivedAt), EmailBody (headers, parts, bodyValues, attachments,
// isTruncated), Attachment, ConversationThread, Identity, QuotaInfo, ListQuery, ListCursor, ListPage,
// ListDelta, MailSyncEvent (closed hierarchy, one record per file).

// Core/Plugins/Hooks (interface owned by mailcore)
public interface IHookBus {
    bool HasHandlers(string hook);                                    // the no-plugin fast path: no allocation
    ValueTask NotifyAsync<TPayload>(string hook, TPayload payload, CancellationToken ct);               // observer
    ValueTask<bool> InterceptAsync<TPayload>(string hook, TPayload payload, CancellationToken ct);      // intercept: false stops
    ValueTask<T> TransformAsync<T>(string hook, T value, CancellationToken ct);                          // transform chain
}
// Hook names are constants generated from spec/plugins/hooks.csv by F2 (Core/Plugins/Hooks/HookNames.g.cs).
```

### 4.2 Accounts, storage, secrets (owner: accounts)
```csharp
// Core/Accounts
public interface IAccountManager {
    IReadOnlyList<AccountInfo> Accounts { get; }
    AccountInfo? Active { get; }
    event EventHandler<ActiveAccountChangedEventArgs>? ActiveChanged;
    Task<Result<AccountInfo>> SignInWithPasswordAsync(PasswordSignIn request, CancellationToken ct);   // incl. TOTP
    Task<Result<AccountInfo>> SignInWithOAuthAsync(OAuthSignIn request, CancellationToken ct);
    Task<Result<Unit>> SwitchToAsync(string accountKey, CancellationToken ct);
    IAccountSession? SessionFor(string accountKey);
}
public interface IAccountSession {           // one per signed-in account
    AccountInfo Account { get; }
    IJmapClient Client { get; }
    IMailService Mail { get; }
    IMailSync Sync { get; }
}
// Core/Storage
public interface IProfilePaths { string Profile { get; } string ConfigDir { get; } string DataDir { get; } }
public interface IPrefsStore { PrefsSnapshot Current { get; } event EventHandler? Changed; }    // prefs.json, Bulwark keys, JsonNode-preserving
// Core/Mail/BodyCache
public interface IBodyCache {
    long Generation { get; }
    bool TryGet(string accountKey, string emailId, [NotNullWhen(true)] out EmailBody? body);
    void Put(string accountKey, EmailBody body, long fetchedAtGeneration);          // dropped when a clear happened since
    bool TryGetThread(string accountKey, string threadId, [NotNullWhen(true)] out ConversationThread? thread);
    void PutThread(string accountKey, ConversationThread thread, long fetchedAtGeneration);
    void ApplySync(string accountKey, MailSyncEvent change);                        // patches keywords, drops removed
    void Clear();
}
// Barbican.Platform
public interface ISecretStore {
    Task<Result<string?>> ReadAsync(SecretName name, CancellationToken ct);
    Task<Result<Unit>> WriteAsync(SecretName name, string value, CancellationToken ct);
    Task<Result<Unit>> DeleteAsync(SecretName name, CancellationToken ct);
}
public interface IBrowserLauncher { Task<Result<Unit>> OpenAsync(Uri uri, CancellationToken ct); }  // fake in AUTOMATION builds
```

### 4.3 Presentation (owner: mailcore unless noted)
```csharp
public interface IDispatcher { bool HasThreadAccess { get; } void Post(Action action); Task InvokeAsync(Func<Task> action); }
// ViewModels (CommunityToolkit.Mvvm, ObservableObject):
//   ShellViewModel   (ActiveApp, Style Bento|Nordic, Theme, MoreContrast, ReduceMotion, IsRapid)
//   RailViewModel    (apps, mail unread badge, storage ring, account button; account popover content from accounts)
//   SidebarViewModel (Folders: ObservableCollection<FolderRowViewModel>, Tags, sections, Storage)
//   GlanceTilesViewModel (Today, Flagged, To do; glance.rs)
//   MessageListViewModel (Rows: ObservableCollection<ListItemViewModel> = MessageRowViewModel | GroupHeaderViewModel,
//                         Selected, Total, LoadMoreCommand, SearchText, unread-first Inbox section)
//   ReaderViewModel  (Header, Conversation strip, Attachments, AuthResults, BodyState, Apply(ReaderSnapshot))
//   SignInViewModel, AccountsViewModel (owner: accounts)
public interface IMessageOpener {   // list → reader. mailcore registers a header-only placeholder (TryAdd);
                                    // body replaces it with MessageLoader (msgload.rs) in wave 3.
    void Open(MessageRowViewModel row, OpenReason reason, ListDirection direction);
    void Close();
}
public interface IKeyRouter { bool Handle(KeyChord chord, KeySource source); }   // real KeyDown, WebView-forwarded, automation
```

### 4.4 App
- `Barbican.App.MailBody.MailBodyView` (UserControl stub from F2 that shows the tile colour; owner body).
- `Barbican.Automation.IAutomationCommand { string Name { get; } Task<string> RunAsync(string args, CancellationToken ct); }`
  (owner F1; streams add commands in their own folders under `#if AUTOMATION`).

## 5. Fixed stages (run before the streams; not streams)

### F1 `f1-foundation` (Opus): Uno 7, automation foundation, Linux shell security
**Branch** `m1-f1-foundation`, worktree `<downloads>/wt-m1-f1-foundation`. Merged into `m1` before F2.

1. **Uno.Sdk 7.0.x** (ADR 0004 "M0 review"). Move `global.json` to the newest Uno.Sdk 7.0.x that is at
   least two weeks old; keep `WinAppSdkVersion` 2.6.5. Re-run the S1 gate: both heads build, the
   prototype XAML runs 45 s under Xvfb on the VM without exceptions, publish win-x64 and linux-x64
   (sizes and times recorded), inner-loop times (no-op and one-line XAML edit, both heads). Re-run the S2
   harness (`spikes/BodyShell`) on both OSes **with its code-created `MenuFlyout` phase disabled**
   (`Scenario.Phases.cs:301` opened a windowed popup on the real screen in M0) and the popup guard of
   step 3 active; the remaining 11 Windows checks and the Linux checks must hold. Then remove the
   `Condition="'$(UsingUnoSdk)' != 'true'"` block in `Directory.Packages.props` and check in
   `project.assets.json` that the App head and `Barbican.Editor.Layout` both resolve SkiaSharp 4.156.0.
   Update ADR 0004 with a "M1 Uno 7" section. **Fallback** (ADR 0004): if Uno 7 fails a gate, stay on
   6.7.49, keep the condition, record why, and plan RichTextKit vendoring for M3.
2. **AUTOMATION configuration.** A solution configuration `Automation` (Debug-based) that defines
   `AUTOMATION`; `src/Barbican.Automation` (port of `spikes/s4-automation`: line server on
   `127.0.0.1:$BARBICAN_AUTOMATION_PORT`, the commands marked "spike" in `spec/automation-protocol.md`,
   UIA-client clicks on the server thread, WGC `wshot`/`burst`, `wgc` and `rendering` probes, `every`,
   `motion`, `theme`, `ui`, `resize`, `wv-eval`, `close`), referenced by the App **only** when
   `$(Configuration) == 'Automation'`. `tools/build.ps1` gains `-Configuration Automation`.
   **Enforced:** `tests/Barbican.Build.Tests` asserts the Release App project graph has no reference to
   `Barbican.Automation`, FlaUI or Win2D; `tools/build.ps1 publish` fails when the publish folder holds
   `FlaUI*.dll`, `Microsoft.Graphics.Canvas*.dll` or `Barbican.Automation.dll`; the AUTOMATION symbol is
   absent from Release (a test reads the compiled assembly's metadata).
3. **Hidden window, fail-closed popup guard.**
   - `HiddenWindow` (App/Composition): before the first frame, `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, no
     title bar, `MoveAndResize` to (-12000, 200), `ShowWindow(SW_SHOWNOACTIVATE)`, never `Activate()`.
     One design for every hidden run (FABLE #27: S3 used -32000, S4 -12000; pick -12000 and change the S3
     harness note). In AUTOMATION builds the window is **always** hidden, whatever the environment says.
   - Today `App.xaml.cs` calls `_window.Activate()` unconditionally; after F1 the App decides the window
     mode once at start (normal / hidden) and a test covers that AUTOMATION and `--smoke` never take the
     normal path.
   - **Popup guard (AUTOMATION builds):** after any `Popup`/flyout opens (a `VisualTreeHelper.GetOpenPopupsForXamlRoot`
     check on `Opened` events and on every automation command), if `IsConstrainedToRootBounds` is false
     the guard closes it and the command returns `error unconstrained-popup <type>`. Also add a XAML lint
     test that every `Flyout`/`MenuFlyout`/`ToolTip`/`ComboBox` in `src/Barbican.App/**/*.xaml` sets
     `ShouldConstrainToRootBounds="True"`. Test the controls M0 left untested (`ComboBox`, `ToolTip`,
     `AutoSuggestBox`, `TeachingTip`, `CommandBarFlyout`, `ContentDialog`) **only off-screen with the
     guard on**; any that cannot be constrained goes on a deny list enforced by the lint test.
   - **Hidden smoke mode** for the published exe: `Barbican.exe --smoke <report.json>` (also in Release)
     creates the window hidden from its first frame, waits for the first rendered frame and the shell
     WebView's `shell` message, writes timings (process start to first frame, to shell ready) and exits
     with 0. This also gives the cold-start number for the gate. Verify the published win-x64 exe with it
     (ADR 0004 open point). Linux: the published linux-x64 build under `tools/linux.ps1 xrun`.
4. **Linux body-shell security** (ADR 0005 decision 4 and "M0 review"; blocks the M1 reader on Linux):
   - `shell.js`: a capturing click handler in every frame document routes links itself (`link:URL`
     posted to the host) on both OSes; the host validates scheme (http, https, mailto) and passes them to
     an `ILinkRouter` (placeholder: log only; M2 opens them after the link check).
   - A test (`tests/Barbican.Core.Tests/Mail/Body/ShellScriptTests.cs`) asserts that `shell.js` never
     assigns `location`, `location.href`, `document.location`, `window.location`, never calls
     `location.assign/replace`, `window.open` or `document.write`, and that frames are created without
     `allow-top-navigation`, `allow-popups` or `allow-same-origin` (port of the Rust `sandbox()`
     mailview.rs:140).
   - Host-side message validation: `Core/Mail/Body/ShellMessage.cs` parses every page → host message
     (`shell`, `ready:N`, `dim`, `link:URL`, `ctx:X,Y`, `key:…`, `dirty`) with an allow-list and a length
     cap, from `chrome.webview` or `webkit.messageHandlers.unoWebView`; unknown messages are dropped and
     counted. Tests for malformed and oversized input.
   - **CSP builder in Core** (`Core/Mail/Body/MailCsp.cs`), port of Rust `main.rs:89` `csp_with` with
     T-176 and new tests: images off blocks `img-src`, `style-src` remote, `font-src`, `media-src`,
     background images; the dark script runs only by hash; no `unsafe-inline` script. On Linux CSP is the
     only remote-content guard: assert the Linux path builds the same CSP.
5. **Portable zip:** `tools/package-portable.ps1` zips the win-x64 publish into
   `artifacts/portable/Barbican-<sha>-win-x64.zip` (PORT-PLAN M1 "a portable zip build artifact on each
   `main` push" is a local script per ADR 0003; it is run at the gate, not on push).

**Exit:** green on both OSes; the Release publish check passes; `--smoke` of the published win-x64 exe
and the linux-x64 build exits 0 with timings; the S2 harness re-run report and the S1 numbers are in ADR
0004; the popup guard has a test that opens an unconstrained flyout off-screen and sees it closed
(Windows only, guard on). **Hidden-automation scenario:** start the AUTOMATION build on its own port and
profile, `wshot`, `probe-start wgc`, 2 s idle, `probe-stop` (0 frames), `close`; under Xvfb on the VM the
same through `xrun`. Fable items: the Uno 7 move and anything left on the 6.7 fallback, the popup guard
coverage, the deny list, the Release-content enforcement, the link router allow-list, the CSP tests.

### F2 `f2-spec-tests` (Sonnet): red tests, contract skeleton, plugin catalogue
**Branch** `m1-f2-spec-tests` from `m1` after F1. Merged before wave 1.

1. **Contract skeleton (§4):** every type in §4 as a compiling stub in its owner's folder (§3.1), with
   the module header naming the Rust file and Bulwark ref it will mirror. Models are ported field by field
   from the Rust value shapes (`<rust-repo>/src/jmap.rs`, `mail.rs`, `msgload.rs`) and the Bulwark types
   (`<bulwark>/lib/jmap/types.ts`). No behaviour.
2. **Red tests:** the 74 rows of §2.1 owned by jmap, mailcore, accounts and body, each a faithful port of
   the Rust test body (same inputs, same asserts; any changed assertion needs a D- entry, §2.4 of the
   plan), tagged `[RustTest("<tag>")]`, in the owner's test folder. They compile and fail (the stubs
   throw). Fixtures that the Rust tests embed go to `tests/fixtures/<area>/` with `example.org` addresses
   only. Then `python tools/spec-check.py --write` and `--milestone M1` (expected: 74 rows mapped, T-176
   from F1, T-159 dropped). Run the suites once to prove they are red, not erroring at build.
3. **Lite drop:** a new D- entry (next free number in `spec/decisions.md`) "lite mode dropped: no .NET
   Blitz equivalent; plain-text mode kept" citing PORT-PLAN §1 and Appendix A, B-READ-046; T-159 status
   `dropped(D-###, user OK pending)`. Flag it for the gate (§8). If `spec-check.py` does not accept that
   status form, adjust the regex in the same commit and say so in the Fable log.
4. **`spec/plugins/hooks.csv`** from `<bulwark>/lib/plugin-hook-catalog.ts`: `name, group, kind,
   permission, also_permission, payload, when, bulwark_ref (file:line), milestone, owner, cs_call_site,
   status`. **`spec/plugins/slots.csv`** from `lib/plugin-slot-catalog.ts`: `name, area, kind,
   permissions, props, where, bulwark_ref, milestone, status`. Milestones by the area that builds them
   (§2.4 for M1; the rest by PORT-PLAN §5). Count and reconcile with the plan's 219 hooks and 29 groups.
   Generate `src/Barbican.Core/Plugins/Hooks/HookNames.g.cs` (constants) with a small script
   `tools/gen-hooks.py` and a test that the constants equal the CSV. Add `spec/plugins/README.md` and the
   two files to `spec/README.md`. Extend `tools/spec-check.py` with a hooks summary (rows per milestone,
   rows with a call site).
5. **Call-site test (red):** `tests/Barbican.Core.Tests/Plugins/HookCallSiteTests.cs` asserts every
   `milestone = M1` hook has at least one call site in `src/` (scan for `HookNames.<Name>`), as Bulwark's
   `lib/__tests__/plugin-hook-catalog.test.ts` does. Red until the streams fire them.

**Red convention (as built, 2026-11-17):** a red test carries `[Red("<stream>")]` (`tests/Shared/RedAttribute.cs`), is excluded from
the default run by `tests/Directory.Build.props` so `tools/build.ps1 test` stays green, and fails with `PortPendingException` from its
stub. `python tools/red-check.py` runs only the red tests (`BARBICAN_RED=1`) and checks every one fails for that reason. A stream
removes `[Red]` from a test in the commit that makes it pass; `HookCallSiteTests` has one red test per owning stream. The branch is
`m1-f2-spec-tests` (git cannot hold `m1` and `m1/f2`).

**Exit:** both OSes build; the new tests are red for the right reason; spec-check maps 75 of 76 M1 rows
(T-159 dropped pending). No production behaviour. Fable items: any Rust test whose assert could not be
ported verbatim, the hook count, the milestone column choices.

## 6. Stream briefs

### 6.1 `jmap` (Opus, hard, wave 1): JMAP core, sync, push, state codec
**Spec to close:** T-128..T-145; the J- rows of §2.2 (J-020 jointly); `mail-sync` hooks; B- rows of
`spec/behaviours/jmap.md` and `sync.md` (the non-listsync part).

**Read:** PORT-PLAN §3.4, §3.5 (cache rules only), §4.6; Rust `src/jmap.rs` (all: `fetch_session` :73,
`split_calls` :104, `request` :272, `request_using` :283, `stalwart_change_id` :1396, `went_back` :1443,
`apply_list_delta` :1540, the SSE parser and the delta structs), `src/mail.rs` (fetch and states parts,
`states_of` :34), `src/main.rs` (push wiring), `docs/legacy/JMAP-COMPLIANCE.md`. Bulwark
`lib/jmap/client.ts` (`connect` :2150, `request` :2384, `getQuota` :2518, `getMailboxes` :2635,
`getEmailChanges`/`getMailboxChanges` :2915-2920, `getEmails` :3085, `getEmail` :3517, `searchEmails`
:4746, `getThread` :5073, `getIdentities` :5177, `getBlobDownloadUrl` :6876, maxCallsInRequest :7103,
`getEventSourceUrl` :7391), `lib/jmap/stalwart-state.ts`, `request-limits.ts`, `jmap-error.ts`,
`lib/mail-push-listener.ts`, `lib/push-events.ts`, `lib/jmap/patch-pointer.ts`, `server-keywords.ts`,
`lib/quota.ts`, `stores/email-store.ts` (list delta and paging). Stalwart: `crates/jmap/src/api/session.rs`,
`api/request.rs`, `api/event_source.rs`, `changes/{get,query,state,mod}.rs`,
`crates/jmap-proto/src/method/query_changes.rs`, `crates/jmap-proto/src/types/state.rs`,
`crates/types/src/type_state.rs`, `crates/jmap/src/{mailbox,email,thread,identity,quota}/get.rs`,
`tests/src/jmap/core/event_source.rs`, `tests/src/jmap/mail/query_changes.rs`.

**Owns:** see §3.1 (`Core/Common`, `Core/Jmap`, `Core/Mail/Model`, `Core/Mail/Service`, `Core/Sync`,
FakeJmap, Jmap.Live, `tools/trace-diff.py`, `spec/traces/`).

**Builds:**
- Transport: one `HttpClient` per origin on `SocketsHttpHandler` (HTTP/2 where offered; the only place
  `new HttpClient` is allowed), Bearer/Basic through `IAccessTokenSource` with one renewal and one retry
  on 401 for API, download and EventSource; read-only methods retry once after `Retry-After` (≤ 10 s),
  writes never; `ReadOnly` mode returns `ReadOnlyViolation` for anything outside the allow-list of
  PORT-PLAN §4.6 (tested per method name); `SemaphoreSlim` gates for `maxConcurrentRequests` and uploads;
  request size checked up front.
- Session: discovery through `/.well-known/jmap` with redirects, relative URLs resolved, primary accounts,
  account capabilities through `AccountCapability<T>()` (the Rust `maxDelayedSend` level bug is a test),
  re-fetch when `sessionState` changes.
- Requests: typed builder with result references and creation ids; `SplitCalls` (keeps linked calls);
  `GetAll`/`QueryAll` chunking by `maxObjectsInGet` and server limits; System.Text.Json source
  generation; `[JsonExtensionData]`; JSON Pointer patch paths (`~0`/`~1`, port of `keyword_path`).
- Methods for M1: Mailbox/get+changes (counts-only fast path; T-145), Email/query (+ `text` filter for
  basic search, sort `receivedAt`, `collapseThreads`, `calculateTotal`, anchor paging), Email/get
  (`bodyProperties`, `fetchHTMLBodyValues`, `maxBodyValueBytes`, header forms), Email/changes,
  Email/queryChanges (no `maxChanges` on queryChanges; J-027 deviation kept), Thread/get+changes,
  Identity/get, Quota/get (T-142: mail account octets), Blob download (URI template level 1, J-035). The
  pure M3-facing functions of T-128, T-129, T-144 as tested pure functions only.
- Sync engine per account: the 7-call list delta applied with the RFC 8620 §5.6 algorithm (port of
  `apply_list_delta`, T-134..T-136), full refetch on `cannotCalculateChanges`, `tooManyChanges`,
  `hasMoreChanges` or a Stalwart state going backwards (T-130, T-131 with Bulwark's vectors), emitting
  `MailSyncEvent`s on a channel; the body cache patch event (consumed by accounts' `IBodyCache.ApplySync`
  in wave 2).
- Push: EventSource client (port of the tested SSE parser, T-140; multi-line data, comments, CRLF,
  unknown events), `types=Email,Mailbox,Thread,EmailDelivery,EmailSubmission`, `closeafter=no`,
  `ping=30`, 75 s read deadline, backoff 2 s → 5 min with jitter (through `TimeProvider`), full delta
  sync on every (re)open, `StateChange` routed per account and type into a `Channel<PushEvent>`.
- Errors: the closed hierarchy; every method and set error type of JMAP-COMPLIANCE.md mapped to a user
  message (port of `problem_message`, `method_error`, `set_error`; T-139).
- Trace: `BARBICAN_JMAP_TRACE=<file>` writes method names and call order only (never bodies, ids or
  addresses). `tools/trace-diff.py` compares two traces. `spec/traces/M1/{open,switch,push}.rust.txt`:
  the Rust call sequences for those scenarios **derived from the Rust source** with file:line per call
  (an agent must not run the Rust app signed in); `{…}.cs.txt` produced from FakeJmap runs.
- FakeJmap: real state strings, `/changes`, `/queryChanges` (including `cannotCalculateChanges`,
  `tooManyChanges`, state going back), limits (`maxCallsInRequest` etc.), RFC 7807 problems, 401 then
  success, an EventSource stream with `StateChange` injection, synthetic seed mail (`example.org`, sample
  text, a thread of 5, attachments, dark-native HTML, a 3 MB body, `isTruncated`), and OAuth discovery,
  registration, authorize and token endpoints (for accounts). Recorded Stalwart response shapes as
  contract fixtures in `tests/fixtures/jmap/` (synthetic values, real shapes).
- Jmap.Live (opt-in, read-only, skipped by default): session, Mailbox/get, Email/query+get on Sent,
  changes since a fresh state; run only by the Verify stage or the user (§1 rule 3).

**Provides:** `IJmapClientFactory`, `IJmapClient`, `IMailService`, `IMailSync`, the models, FakeJmap.
**Consumes:** `IAccessTokenSource` (a static-token implementation for tests; the real one is accounts'),
`IHookBus` (stub until mailcore merges; fire through the interface only).

**Exit tests:** the 18 T- rows green; J- rows with tests (RFC-tagged `[Rfc("8620","5.6")]` etc.);
transport tests (401 renewal once, `Retry-After` cap, writes never retried, gates, read-only violations
for every write method); push tests on a virtual `TimeProvider` (deadline, backoff with jitter bounds,
catch-up on reopen); a FakeJmap integration test of open → push → delta with no list reset; coverage ≥ 85 %
on Jmap and Sync. **Trace:** `open`, `switch`, `push` traces from FakeJmap match the Rust-derived
sequences in `trace-diff` (differences documented as decisions). **Hidden-automation scenario:** none
with a window (no UI yet); a headless scenario runner test drives `IMailService`/`IMailSync` against
FakeJmap through open, page, push and reconnect and writes the trace.

### 6.2 `mailcore` (Sonnet, wave 1): pure mail modules, ViewModels, ListSync, KeyRouter, hook bus
**Spec to close:** T-146..T-158, T-280..T-284, T-011..T-014, T-160..T-162, T-008..T-010, T-001..T-003,
T-184; J-046 (logic); hooks of `mail-list`, `mail-mailboxes` (display), `mail-search`, `navigation`,
`keyboard`, `app`, `ui`; B- rows of `listsync.md`, `sync.md` B-SYNC-001..011, `mail.md` (list, sidebar,
tags, glance parts), `motion.md` (setting resolution), `shortcuts.md` (j/k/arrows, `/`, Shift+G, Escape).

**Read:** PORT-PLAN §3.3, §3.7 (lists, keyboard); Rust `src/listsync.rs` (`sync_by` :42, `sync_into`
:122), `tags.rs`, `auth.rs`, `attachments.rs`, `mail.rs` (`rebuild_folders` :115, `badge_title` :203,
roles :90-108), `glance.rs`, `a11y.rs` (`A11y` :74, `cvd_color` :231), `motion.rs` (`reduce_for` :23),
`shortcuts.rs` (mail keys only), `msgload.rs` `prefill` :175 (header from the row). Bulwark
`components/email/email-list.tsx` (unread-first section :158, :355, :370), `lib/list-date-groups.ts`,
`components/email/list-date-group-header.tsx`, `thread-list-item.tsx`, `attachment-chips.tsx`,
`lib/keyword-format.ts`, `lib/keyword-nesting.ts`, `lib/tag-hex-color.ts`, `lib/shared-tags.ts`,
`lib/server-auth-status.ts`, `lib/mailbox-label.ts`, `lib/quota.ts`, `components/layout/sidebar.tsx`,
`navigation-rail.tsx`, `lib/reduced-motion.ts`, `lib/next-unread-email.ts`, `lib/plugin-mail-hooks.ts`,
`lib/plugin-hook-catalog.ts`.

**Owns:** see §3.1.

**Builds:**
- `ListSync.SyncInto(ObservableCollection<T>, IReadOnlyList<T>, key, same)` with the Rust thresholds
  (reset when fewer than half the rows are kept, more than 8 moves, duplicate keys) and its 13 tests;
  lists are never re-assigned on refresh.
- Tags (ids follow Bulwark, FNV like JavaScript, tree rows, stored tags and colours), authentication
  results parsing and alignment, attachment names/kinds/preview rules, mail rules (forced read, unread
  badge title, archive paths), Glance tiles data (Today, Flagged, To do), date grouping, accessibility
  settings resolution and the mail CSS string with CVD palette (the body stream injects it), motion
  setting resolution (setting wins over system).
- `HookBus : IHookBus` (no plugin host: dispatch to registered in-process handlers for tests; zero
  allocations when nothing is registered, measured by a test), and the call sites of its groups.
- `IDispatcher` over `DispatcherQueue` (interface here; the App implementation is shell-ui's).
- ViewModels of §4.3 except accounts': list paging with anchor (load more), thread counts, previews,
  date groups, unread-first Inbox section, selection and `Motion.Rapid` (on while the selection moves
  faster than an animation), basic search (`text` filter through `IMailService.QueryAsync`), sidebar
  (folders tree with roles, counts, collapse, muted unsubscribed folders, tags, storage meter), rail
  (badges, storage ring), Glance tiles, `ReaderViewModel` (header, conversation strip, attachment cards,
  auth chips; `Apply(ReaderSnapshot)` changes everything in one call), the header-only `IMessageOpener`
  placeholder, `KeyRouter` with the M1 mail keys (j/k/arrows, Enter, Escape, `/`, Shift+G, Ctrl+1..4 app
  switch for the apps that exist).
- Everything consumes `IMailService`/`IMailSync` events and applies deltas through ListSync (push to
  visible update without a list reset).

**Provides:** `IHookBus`, `IDispatcher`, `IKeyRouter`, the ViewModels, `IMessageOpener` placeholder.
**Consumes:** jmap's contracts (stubs during wave 1, real after merge), `IPrefsStore` (stub; accounts in
wave 2), `IAccountManager.ActiveChanged` (stub).

**Exit tests:** the 32 T- rows green; Presentation tests against fake `IMailService`/`IMailSync`: open
folder, page (anchor), push delta inserts on top as one insert (no reset), switch folder cancels the
previous load (`CancellationTokenSource` + sequence), j/k across a page boundary, rapid selection sets
`IsRapid`, search; the hook call-site test green for the mailcore groups. **Hidden-automation scenario:**
none with a window; a headless VM scenario (open Inbox, j ×20 at 33 ms, push arrives, load more) asserts
collection change counts (no `Reset` events) and UI-thread work per step under budget.

### 6.3 `accounts` (Opus, hard, wave 2): accounts, sign-in, OAuth, secrets, profiles, prefs, body cache
**Spec to close:** T-025..T-034, T-192..T-198, T-259; J-020 (token side); `auth` hooks; I-001, I-002,
I-005, I-006, I-010, I-098, I-099, I-101 (minus language), I-103, I-104 (logic); B- rows of `oauth.md`
(B-OAUTH-001..018), `settings.md` (prefs load/save/import), `platform.md` (secrets), `reader.md` body
cache rows.

**Read:** PORT-PLAN §3.5, §3.9 (secrets row), §4.6; ADR 0008 (all, with "M0 review"); FABLE #3 and #6;
Rust `src/oauth.rs` (`discover` :87, `client_id` :132, `open_browser` :159, `sign_in` :164, the refresh
decision and its tests), `bodycache.rs` (`BodyCache` :23, `weight` :45, `thread_weight` :40,
`generation` :214), `settings.rs`, `main.rs` (`remember_session`, sign-in handlers, account switching),
`platform/`; `spikes/s5-credentials/read-format.ps1`. Bulwark `lib/oauth/discovery.ts`, `tokens.ts`,
`token-exchange.ts`, `slot-renewal.ts`, `pkce.ts`, `scopes.ts`, `sign-in-servers.ts`,
`stores/auth-store.ts` (sign-in, TOTP, renewal), `stores/account-store.ts`, `stores/client-registry.ts`,
`lib/account-inbox-unread.ts`, `lib/account-state-manager.ts`, `components/auth/login-server-field.tsx`,
`oauth-status-card.tsx`, `components/layout/account-switcher.tsx`, `stores/settings-store.ts` (keys and
defaults). Stalwart: `crates/http/src/auth/oauth/{auth,token,registration,openid}.rs`,
`crates/common/src/auth/oauth/{registration,token,oidc,config}.rs`.

**Owns:** see §3.1.

**Builds:**
- Profiles and storage: `%APPDATA%\Barbican-<profile>` / `%LOCALAPPDATA%\…` and XDG equivalents
  (`BARBICAN_PROFILE=dev` → `Barbican-dev`, `auto-<port>` for automation); atomic writes (temp + replace);
  a debug assertion when file IO runs on the UI thread; prefs.json read as `JsonNode` so unknown Bulwark
  keys survive; in-memory snapshot with debounced async save (no settings UI in M1); first-run import of
  a **copy** of the Rust app's prefs, panes and search history into the dev profile (never writes the Rust
  profile).
- Secrets: `ISecretStore` on Windows (`CredReadW`/`CredWriteW` P/Invoke or CsWin32, target
  `Barbican.Next/<profile>/<name>`, `CRED_PERSIST_LOCAL_MACHINE`, UTF-8) and Linux (Secret Service over
  Tmds.DBus, `schema=barbican-next`, `profile`, `name`); a fake store for tests; the real-store test uses
  `Barbican.Next/test-<guid>/` and deletes it. The ADR 0008 import ("Import accounts from the previous
  Barbican"): reads `accounts.Barbican` / `session.Barbican`, decodes UTF-16LE, maps password and OAuth
  kinds, writes only new entries, never touches the Rust entries, zeroes buffers, logs no values. Offered
  only for the default profile in the UI; an **agent-only** path imports into `dev`/`auto-<port>` for live
  read-only checks, **password kind only** (§1 rule 3; an OAuth entry is skipped with "sign in by hand").
- Sign-in: password, password + TOTP (the one-time code is never remembered), OAuth with discovery,
  dynamic registration (registration cache as in Rust, but under the profile), PKCE, loopback on
  `127.0.0.1:8791` that **checks `state` before anything else** (FABLE #3: Rust returned on `error` first)
  and accepts exactly one callback; the refresh decision ported with T-192..T-198 (adopt a token rotated
  elsewhere, retry once, never delete; network errors keep the token and retry later); `IAccessTokenSource`
  per account for jmap's transport. `IBrowserLauncher`: the system browser in normal builds; in AUTOMATION
  builds a fake that GETs the authorize URL from FakeJmap and delivers the redirect to the loopback.
- Accounts: `IAccountManager` with several accounts, the most-recent-first list, switching (the inactive
  accounts keep a cheap unread watch per Bulwark `account-inbox-unread.ts`; decide from Rust `main.rs` and
  document), one `IAccountSession` per signed-in account, sign-out is M2. Read-only enforcement: a
  per-account flag that the readonly placeholder account always carries in agent profiles.
- Body cache (in memory): 120 entries / 48 MB LRU per account, thread cache 120 / 8 MB, weights counting
  headers and body values, generation guard (a fetch from before a clear is not stored), patched by
  `MailSyncEvent`s and local actions; cleared on sign-out.
- `SignInViewModel`, `AccountsViewModel` (popover rows: avatar, check, default star, unread badge,
  server/error line; add account).
- **Persistent SQLite cache (PORT-PLAN §3.5, Q4) is deferred to M2** (decision of this plan: M1's
  cold-start gate uses the ≤ 1.5 s "without cache" budget; the cache needs the M2 action paths to stay
  consistent). Record it in the Fable log.

**Provides:** `IAccountManager`, `IAccountSession`, `IAccessTokenSource`, `IProfilePaths`, `IPrefsStore`,
`ISecretStore`, `IBrowserLauncher`, `IBodyCache`, the two ViewModels. **Consumes:** jmap
(`IJmapClientFactory`, `IMailService`, `IMailSync`, FakeJmap with OAuth endpoints), mailcore (`IHookBus`,
`IDispatcher`).

**Exit tests:** the 18 T- rows green; OAuth flow end to end against FakeJmap with the fake launcher
(state mismatch rejected before error handling; a second callback refused; PKCE verifier checked by the
fake); 401 → renewal → retry once through the real transport; import tests against a fake store with
odd-length, non-JSON and both kinds; prefs round trip keeps unknown keys; the Windows secret store test on
a throwaway target (Linux on the VM, skipped where no Secret Service runs, recorded). **Hidden-automation
scenario:** AUTOMATION build, profile `auto-<port>`, `BARBICAN_FAKE_JMAP` seed: sign in with password +
TOTP via `text`/`invoke` by AutomationId, add a second (fake) account through OAuth, switch accounts,
`wshot` of the popover (constrained, in-window); probes show no blank frame on switch. **Live (optional,
read-only):** if the password-kind import is possible, sign in both accounts in `auto-<port>` with
read-only on, list folders of both, then delete the imported dev entries; otherwise hand test.

### 6.4 `shell-ui` (Sonnet, wave 2): XAML design system and every M1 view except the body
**Spec to close:** the display parts of the I- rows of §2.3 (rail, sidebar, list, reader chrome,
sign-in, account popover); J-046 (muted folders shown); `theme` hooks; the `design/` carry-over from M0
(README.md and MOTION.md in XAML terms).

**Read:** `design/README.md`, `design/MOTION.md`, `design/mockups/BN*.dc.html` and `Nord*.dc.html`
(source of truth; the private canvas link is in `.local/accounts.md`), `spec/layout/{shell,mail-list,reader,overlays}.md`,
PORT-PLAN §3.7, §4.7; ADR 0005 (popups), 0007 (AutomationIds, constrained popups); `prototype/winui`
(Palette.Apply, SIcon); Rust `ui/theme.slint` via `design/legacy-slint/theme.slint`; Bulwark
`components/layout/{navigation-rail,sidebar,account-switcher}.tsx`, `components/email/{email-list,thread-list-item,list-date-group-header,email-viewer,thread-conversation-view,reader-attachments,attachment-chips,tag-badge}.tsx`,
`components/auth/*`.

**Owns:** see §3.1.

**Builds:**
- Tokens in XAML: the generated `Tokens.xaml` and `Shape.Bento/Nordic.xaml` loaded once; `Palette.Apply`
  mutates brush colours in place for theme, style and more-contrast (no re-templating, no flash);
  shape dictionaries swapped once per style change; icons as `PathIcon` geometry resources from the
  mockups' `I` paths; motion resources (`Motion.Fast` 100, `Enter` 150, `Exit` 90, `View` 110, `TipDelay`
  350, KeySplines) with reduce motion → 0; the PopIn, Scrim and Tip behaviours; every `ListView`/
  `ItemsRepeater` item-container transition cleared; no Frame navigation transitions.
- Views (one per file, `x:Bind`, AutomationId and name on every interactive control): `ShellPage`
  (regions only), rail, sidebar (folders, tags, counts, storage meter), Bento tiles (Today, Flagged, To do),
  message list (rows with thread count, preview, attachment chips, date group headers, unread-first
  section, load more on scroll), reader chrome (header, details panel with auth chips, conversation strip,
  attachment cards, toolbar shown disabled, the `MailBodyView` slot), sign-in view and account popover,
  Nordic and Bento layouts from the same views. The App `IDispatcher` and the `KeyDown` → `IKeyRouter`
  wiring. Reads uiStyle/theme/contrast from `IPrefsStore`.
- `design/README.md` and `design/MOTION.md` rewritten in XAML terms (resources, behaviours, `ListSync`,
  the `wgc` probe), keeping every rule; `spec/layout/*.md` gets the C# metrics column.
- `tools/layout/m1-bento.json`, `m1-nordic.json`: region rectangles for `tools/flicker.py` and the
  `tools/motion.py` layout, and the scenario coordinates for `tools/scenarios.py` (or AutomationId-based
  steps), so the feel scenarios run against the C# app.
- The rail settings gear and Compose are present and inert in M1.

**Provides:** all views, App `IDispatcher`, the layout files. **Consumes:** mailcore and accounts
ViewModels, `MailBodyView` stub (body), `IPrefsStore`.

**Exit tests:** a XAML lint test (constrained popups, AutomationIds on interactive controls); token and
palette tests (in-place mutation, same brush instances after a theme flip); screenshots with sample data
(FakeJmap seed) for Bento/Nordic × dark/light × normal/more-contrast at 1400×900 in
`docs/milestones/M1-screens/` (sample data only); motion probes: `theme_flip` and `resize` with 0 BLANK
and 0 FLASH, nothing animating over 200 ms, 0 frames idle. **Hidden-automation scenario:** AUTOMATION
build with the FakeJmap seed on its own port and profile: `ui bento|nordic dark|light [more]`, `resize`,
folder click, j ×20, `wshot` per state, `burst` for `theme_flip`, `probe-start wgc` idle check.

### 6.5 `body` (Opus, hard, wave 3): body view, message loading, prefetch, feel gates
**Spec to close:** T-074..T-076, T-185, T-186, T-175; J-053; `mail-read` hooks; I-203, I-206, I-209,
I-214, I-222, I-230 (with shell-ui's chrome); B- rows of `reader.md` (body, msgload, dark mail, a11y CSS,
inline images, truncated notice), `extras.md` CAP rows; the M1 feel exit.

**Read:** PORT-PLAN §3.6, §4.5, §4.8; ADR 0005 (all), 0007 (probes, quantum); FABLE #1, #2, #27;
`docs/legacy/mailview-shell.rust-final.js`; Rust `src/mailview.rs` (`SHELL_JS` :51, `shell_html` :127,
`sandbox` :140, `show` :158, `slow` :178, `cancel_pending` :190, `dim` :199), `msgload.rs` (`open` :100,
`prefill` :175, `mark_read` :202 (suppressed: read-only), `present` :221, `prefetch` :320,
`prefetch_top` :351, `fetch_ahead` :357, `was_unseen` :45, `inline_bytes` :50), `dark_mail.rs`,
`a11y.rs` (mail CSS), `main.rs` `csp_with` :89 (F1 ported it), `email_dark.js`; `spikes/BodyShell/`.
Bulwark `components/email/email-viewer.tsx`, `components/mail/stable-email-viewer.tsx`,
`thread-conversation-view.tsx`, `lib/email-prefetch.ts`, `lib/email-dark-mode.ts`,
`lib/email-sanitization.ts`, `lib/email-frame-height.ts`, `email-frame-sizer.ts`, `email-frame-keys.ts`,
`email-fit-width.ts`, `lib/external-mail.ts`, `components/email/retention-notice.tsx`,
`reader-open-failure.tsx`. Stalwart `crates/jmap/src/email/get.rs` (body values, truncation).

**Owns:** see §3.1 (`App/MailBody`, `Assets/mail`, `Core/Mail/Body`, `Presentation/Mail/Loading`,
`spec/feel/M1`).

**Builds:**
- The shell host: one persistent WebView2 document created warm at start-up, `DefaultBackgroundColor` =
  tile colour, dev tools off outside Debug/Automation, default context menu replaced (`ctx:X,Y` to the
  app; the menu itself is M2), new windows and main-frame navigations blocked, links through F1's
  router, keys forwarded (`mailview::forwarded_keys_parse`) into `IKeyRouter`.
- Per message: a sandboxed `srcdoc` iframe with its own CSP (F1's builder; images off by default,
  "Load external content" per message), loaded hidden next to the visible one; `ready:N` after layout
  and the dark pass; the native header changes in the same dispatcher turn as `reveal(N)`; `dim(on)`
  after 150 ms; large pages as a web message on Windows and `ExecuteScriptAsync` on Linux; `cid:` parts
  to `data:` URIs before injection (inline image size, T-186); `isTruncated` notice; plain-text mode
  ("Shown as plain text" › Show formatted); `email_dark.js` verbatim by CSP hash (T-074..T-076);
  accessibility CSS from mailcore injected the same way; `AddWebResourceRequestedFilter` on Windows as
  defence in depth.
- Linux: `IBodyOverlayStrategy.Linux` with the XGetImage stand-in for dialogs and full-window scrims
  (menus need none on Uno); `ExecuteScriptAsync` for all host → page traffic.
- `MessageLoader : IMessageOpener` (port of msgload.rs): keep the old body until the new one is ready;
  header from the row first (`prefill`); unread state from the list first (T-185); body cache and
  thread cache through `IBodyCache` with generation guards; superseded opens cancelled
  (`CancellationTokenSource` + sequence); mark-as-read **never sent** in M1 (read-only client; the
  `markAsReadDelay` logic is ported and tested, the write is behind a disabled `IMailActions` that M2
  supplies); prefetch in the direction of travel (and the top of the list), bounded, cancelled when the
  direction changes; conversation strip opens the thread messages through the same path
  (`thread_switch`).
- `mail-read` hooks: open observers after reveal; body transform hooks run before the document is built
  (no-op fast path when unregistered).

**Provides:** `MailBodyView`, `MessageLoader`, the feel results. **Consumes:** jmap (`IMailService`,
`IMailSync`), accounts (`IBodyCache`, `IPrefsStore`), mailcore (`ReaderViewModel.Apply`, `IKeyRouter`,
`IHookBus`, a11y CSS), shell-ui (`ReaderView` slot, layout files), F1 (CSP, shell message parser, link
router, automation server).

**Exit tests:** the 6 T- rows green; loader tests on fakes (cached switch applies header and body in one
`Apply`; uncached keeps the old body, dims after 150 ms on a virtual clock; a late callback from a
superseded open is ignored; prefetch direction); shell tests on both OSes through the automation build
(ready/reveal, CSP blocks remote with a loopback beacon, links routed, no top navigation). **Feel
(sample data from the FakeJmap seed, AUTOMATION build, own port and profile):** `open_first`,
`open_again`, `switch_key`, `switch_click`, `switch_back`, `close_reader`, `thread_switch`,
`light_switch` in Bento and Nordic, dark and light: **0 FLASH, 0 BLANK** (`tools/flicker.py` on `burst`),
input to first changed frame ≤ 1 frame quantum (the measured ~21 ms WGC quantum, ADR 0007), no frame gap
over 25 ms inside an animation, nothing animating over 200 ms, 0 frames idle, reduce motion settles in
one frame; cached switch ≤ 50 ms header+body. Results into `spec/feel/M1/` next to
`spec/feel/rust-baseline/`. Linux: the same scenarios under Xvfb (relative numbers only; the header/body
skew re-measured, ADR 0005 open point). **Trace:** `open`, `switch`, `push` traces of the full app on
FakeJmap through `tools/trace-diff.py` against `spec/traces/M1/*.rust.txt`.

## 7. Waves, merges and integration (§6.1 steps 3-5)
1. F1 → review (Opus: Uno move, automation, security) → merge into `m1` → green on both OSes.
2. F2 → review (Sonnet) → merge → green (red tests expected red; build green).
3. **Wave 1:** `jmap` (Opus) and `mailcore` (Sonnet) in parallel, each from `m1`. Reviews: Opus for
   jmap, Sonnet for mailcore. Merge `jmap`, build and test; merge `mailcore`, build and test (mailcore
   replaces its stubs of jmap types with the real ones at this merge; conflicts only in contract files,
   resolved in favour of jmap's version plus mailcore's added members).
4. **Wave 2:** `accounts` (Opus) and `shell-ui` (Sonnet) from the updated `m1`. Reviews: Opus for
   accounts (OAuth, secrets), Sonnet for shell-ui. Merge `accounts`, then `shell-ui`.
5. **Wave 3:** `body` (Opus) from the updated `m1`. Review: Opus. Merge.
6. After each merge: `tools/build.ps1 test`, `tools/linux.ps1 test`, `python tools/spec-check.py
   --milestone M1` (rows only go down), `tools/build.ps1 rules`, privacy check. The integrator is Sonnet;
   Opus only for conflicts in jmap, accounts or body files.
7. Prune each worktree after its merge; `tools/build.ps1 shutdown` at the end of every agent.

## 8. Verify and gate (§6.1 steps 6-7)
- `python tools/spec-check.py --milestone M1` exits 0 (T-159 counts as dropped; the gate report asks the
  user to confirm the D- entry).
- The hooks call-site test is green for every `milestone = M1` hook.
- Feel results of §6.5 in `spec/feel/M1/` meet §4.5 with the ~21 ms quantum; a regression against the
  Rust baseline blocks the gate unless the user accepts it.
- `trace-diff`: open, switch and push match the Rust-derived sequences (documented differences only).
- Cold start (`--smoke` on the published win-x64 Release, warm disk): ≤ 1.5 s to the first frame of the
  shell with the Inbox requested (no persistent cache in M1); the Inbox-interactive time with FakeJmap
  recorded.
- Release publish has no automation content; the portable zip is built by `tools/package-portable.ps1`.
- Screenshots (sample data) for the visual review next to the mockups.
- `docs/milestones/M1.md`: the §2.5 checklist, deferred I- rows with their milestones, the Fable items,
  and the hand-test list:
  - **[HAND TEST 1]** (PORT-PLAN M1): the user signs in to the dev profile by hand on both accounts
    (password or OAuth in the real browser; OAuth is never driven by an agent); on `<readonly-account>`,
    browse read-only: fast message switching, key repeat, theme and style changes, resize; judge the feel
    against the Rust app.
  - Live read-only checks that agents could not run (§1 rule 3).
  - The WGC on-screen cadence check (ADR 0007), if the user wants it.
  - Confirm the lite drop (D- entry) and the SQLite cache deferral to M2.
- After the user's OK: merge `m1` to `main`, tag `m1`, push; archive the Rust repo (ROADMAP carry-over),
  which needs the user.

## 9. Risks and decisions of this plan
1. **Contract stubs by a Sonnet stage.** F2 writes the §4 signatures as stubs; the jmap stream (Opus) may
   extend them but not break them during wave 1. Risk: mailcore builds on a model shape that jmap later
   wants different. Mitigation: models are ported from the Rust value shapes the tests already pin; the
   wave-1 merge resolves contract conflicts in favour of jmap.
2. **The 21 ms WGC quantum** replaces the plan's "median ≤ 16 ms" for input-to-frame until the on-screen
   cadence is measured (hand test or virtual display; both need the user).
3. **Trace-diff against Rust is source-derived**, not a live Rust trace, because an agent may not run the
   Rust app signed in. A live Bulwark or Rust trace is a hand-test item.
4. **I- rows tagged M1 that are actions** are closed for display and deferred for the action (§2.3); the
   catalogue's milestone tags overreach here.
5. **Persistent cache deferred to M2** (accepted default Q4 moved by one milestone).
6. **Live checks** depend on the password-kind import; OAuth entries are never refreshed by agents. If the
   user's Rust entries are OAuth only, every live check is a hand test.
7. **Uno 7 move** may fail a gate; the fallback keeps the SkiaSharp split and moves the problem to M3
   (ADR 0004).
8. **Wave 3 is one Opus stream** and the feel gate depends on it: the body stream starts from a merged,
   green wave 2 and owns the gate measurements.
