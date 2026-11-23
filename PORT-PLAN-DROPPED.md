# Barbican port plan: Rust/Slint to C# (WinUI 3 on Windows, Uno on Linux)

> **Dropped 2026-11-23.** The C# port reached Rust parity only after days of work, and theming stayed broken after many passes. Barbican stays on Rust/Slint; the C# repository is archived. This file is kept as a record only.

Written 2026-11-17, after the user's decision to retire Rust/Slint. This is the master plan. It is the first document to move into the new repository, and every milestone report links back to it.

Inputs read for this plan:
- Knowledge documents: ROADMAP.md, GAPS.md, INTERACTIONS.md, PARITY-ROUND2.md, JMAP-COMPLIANCE.md, PLUGINS.md, design/README.md, design/MOTION.md, design/mockups, tools/.
- The header, public API and tests of every module in src/ (42,434 lines, 353 `#[test]`s) and ui/ (13,261 lines including settings-schema.json).
- The whole git log: 194 commits, about 59 KB of commit messages, with 37 branches and 13 worktrees.
- The WinUI prototype at C:/Users/user/Downloads/barbican-winui.
- Bulwark's tree, version 1.21.2 (components/ 70+ areas, stores/ 46, lib/ 368). `lib/jmap/client.ts` alone is 13.5k lines.

---

## 1. Goals, non-goals, decision record

### Decision (2026-11-17)
After using the WinUI 3 prototype, the user said "rust/slint is no more". Barbican moves to C#:
- **WinUI 3** (Windows App SDK) on Windows.
- **Uno Platform** (Skia desktop, X11) on Linux.
- All logic is ported to C#. There is no Rust FFI.
- The C# is strict: nullable reference types, warnings as errors, records, a Result type in the JMAP layer, and analyzers.
- Well-maintained .NET libraries replace hand-written code where they fit (MimeKit, Ical.Net, AngleSharp, NodaTime and others, see §3.12).

### Why the port
- **Feel.** Slint hosts the mail body as a child HWND (WebView2 through wry). It could never reach the feel the user wants, and every overlay needed workarounds:
  - mailview.rs keeps a capture of the body under every menu or dialog;
  - the hidden WebView2 stole keyboard focus (b0dd3bd);
  - keys pressed in the body had to be replayed into the app (7a60ae8).

  WinUI 3 composes WebView2 into the XAML visual tree, so menus, dialogs and toasts draw over the body natively. The prototype already shows this.
- **Text and controls.** WinUI's DirectWrite text, its focus handling, UI Automation and accessibility come with the platform instead of being rebuilt.
- **Libraries.** Mature .NET libraries replace hand-written parsers: MIME, mbox, iCalendar, vCard, HTML, ZIP and TNEF.
- **Linux.** Uno runs the same XAML on Linux, so Linux is a build target rather than a second UI.

### Goals
1. **Parity with Bulwark or better**, measured against the 471 rows of INTERACTIONS.md, the GAPS.md list and the 201-key settings schema. Nothing that works in the Rust app may be missing when the port is done.
2. **JMAP done properly**:
   - RFC 8620 and 8621, plus Quotas (9425), Contacts (9610), Sieve (9661), MDN (9007, where Stalwart has it), and the Calendars and FileNode drafts.
   - Stalwart's extensions (`urn:stalwart:jmap`, `x:*`, Mailbox metadata).
   - Every feature is compared against Bulwark's client and Stalwart's server source **as it is built**, not afterwards.
3. **The Bento × Nordic design** in dark and light, with more-contrast, as approved on 2026-11-16 (design/README.md).
4. **Feel**:
   - no flicker and no blank, white or black frames in any transition;
   - nobody ever waits for an animation;
   - every claim is measured with the probes, never eyeballed (design/MOTION.md).
5. **Distribution**:
   - a CI release workflow for Windows and Linux;
   - sensible package types and an auto-updater with signed manifests;
   - tray icon, notifications, default mail app and start at login.
6. **Admin dashboard** matching Stalwart's WebUI.
7. **A native rich-text composer.** No web editor.
8. **Every context menu Bulwark has** (the 21 menus).
9. **Nothing already learned is lost** (§2). In the user's words: "what we did so far can not be lost. we did so many crawls of bulwark to get to where we are today."

### Non-goals
- No Rust in the new app: no FFI and no sidecar process.
- No web UI. The only web content is the mail body, inside WebView2 on Windows and WebKitGTK on Linux. (This is the user's original rule: "UI all native, WebView only for mail HTML".)
- No macOS, mobile or browser targets for now. Uno keeps the door open; nothing in Core may make them harder.
- No MSIX and no Microsoft Store. The app is unpackaged, like the prototype (§3.10).
- No local Stalwart server for testing. That needs the user's Enterprise licence and was refused.
- No new features beyond parity before M10, except where the port makes one nearly free. Those are marked "better than Rust" in the milestones.

### What is kept, unchanged in meaning
- **Behaviours.** Every behaviour, rule and bug fix recorded in src/, its tests and the commit messages (§2.3).
- **On-disk formats and secret names**, so the user's existing profile carries over:
  - `%APPDATA%\Barbican\prefs.json`, which uses Bulwark's settings keys;
  - `panes.json`, `search-history.json`, `oauth-clients.json`, `calendar.json`, `files.json`, `chat-<user>.json`;
  - the Credential Manager targets `session.Barbican`, `accounts.Barbican`, `stalwart-admin-key.Barbican` and `ai-api-key.Barbican` (the keyring crate's `<user>.<service>` form), and the same attributes in the Linux Secret Service.
- **Bulwark-compatible server data**:
  - Sieve rules in the `@metadata` block;
  - templates in the Inbox metadata (`org.bulwarkmail.template.*` and tombstones);
  - `emailKeywords` tags and `$label:` keywords;
  - the backup and settings export format.
- **The plugin C ABI** (PLUGINS.md), loaded with `NativeLibrary`, so `barbican_spam_score.dll` keeps working.
- **The update manifest format and its Ed25519 key**, so the existing `BARBICAN_UPDATE_SIGNING_KEY` secret stays valid.
- **The hidden automation protocol** (src/automation.rs). tools/auto.py, flicker.py and motion.py keep working unchanged against the C# app.

### Deliberate drops (the user may veto any of them)
- **`lite.rs`**, the Blitz CPU renderer for mail. There is no .NET equivalent. The plain-text render mode stays as the fallback.
- **`tools/drive.ps1` and `tools/scroll.ps1`.** They drive the real window with real input, which is now forbidden. They are not carried over.

---

## 2. Knowledge preservation

### 2.1 The two repositories
- **Rust repo** (this one, `Kaan88/barbican`, local `webmail-main/desktop`):
  - It becomes the read-only reference.
  - In M0, every worktree's work is committed and pushed (§2.6), then `main` gets the annotated tag `rust-final`.
  - The repo is archived on GitHub at the end of M1, once the new repository has imported everything.
  - The local clone stays on disk. Agents read it and never commit to it. A local `pre-commit` hook refuses commits, as a guard.
- **New repo** (proposed `Kaan88/barbican`, after renaming the old one to `barbican-rust`; see Q1):
  - Local folder: `C:/Users/user/Downloads/barbican`.
  - It starts from the prototype's two commits, imported with history, so the prototype is no longer an unpushed local repo.

### 2.2 Where each artefact goes

| Artefact (Rust repo) | In the new repo | How it stays alive |
|---|---|---|
| JMAP-COMPLIANCE.md | `docs/legacy/` (verbatim) and `spec/jmap.csv` | Each table row becomes `J-###` with RFC §, Rust status, C# status and the test that proves it. The C# conformance report is generated from test attributes (§4.3). |
| INTERACTIONS.md (471 rows) | `docs/legacy/` and `spec/interactions.csv` | Rows become `I-001…I-471`. **Re-baselined first:** the inventory was written at 19:10 on 11-16, before parity round 2 merged at 21:28, so many "M" rows are done in Rust. Columns: area, element, Bulwark ref (file:line), Rust status (re-checked), C# status, milestone, evidence. This is **the parity checklist**. |
| GAPS.md | `docs/legacy/` and `spec/gaps.csv` | Each item becomes `G-###` with a milestone. P0–P3 priorities are kept. |
| PARITY-ROUND2.md | `docs/legacy/` and `spec/live-checks.md` | Everything listed as "never driven live" or "not run live" becomes an `L-###` row. These were never verified in Rust either, so the C# port verifies them. |
| ROADMAP.md, PLUGINS.md | `docs/legacy/` | A new ROADMAP.md for the C# app is written in M0. |
| design/README.md, design/MOTION.md | `design/` | Rewritten in XAML terms: token keys, `Motion.*` resources, the PopIn/Scrim/Tip behaviours, ListSync. The rules are copied word for word. |
| design/mockups/*.dc.html, canvas.json, canvas URL | `design/mockups/` (verbatim) | The source of truth for the UI. Screenshots of each milestone are compared against them. |
| ui/theme.slint (tokens) | `src/Barbican.App/Themes/Tokens.xaml` | Generated by `tools/gen-tokens` from theme.slint and BNTokens. A unit test asserts each hex value equals the Slint value for dark, light, more-contrast and both styles. |
| ui/settings-schema.json | Embedded in Core, verbatim | The same schema drives the settings pages. `spec/settings.csv` lists all 201 keys with wired/unwired status. |
| ui/*.slint (layouts) | `spec/layout/*.md` | Per-screen metrics (paddings, radii, row heights, widths at breakpoints) are extracted, so XAML matches the tuned Slint values, not only the mockups. |
| src/email_dark.js and mailview.rs `SHELL_JS` | `src/Barbican.App/Assets/mail/` (verbatim) | CSP hashes are recomputed. |
| src/ module headers, doc comments, tests | `spec/behaviours/*.md` and `spec/rust-tests.csv` | See §2.3. |
| ~59 KB of commit messages | `spec/decisions.md` | See §2.3. |
| tools/auto.py, flicker.py, motion.py, scenarios.py, extract_settings.py | `tools/` | Protocol-compatible (§4.4). motion-layout.json is regenerated for the new layout, keyed by AutomationId where possible. |
| tools/flicker-results, tools/motion-results | `spec/feel/rust-baseline/` | **The bar to beat.** Every C# feel run is compared against these files. |
| .github/workflows, wix/, packaging/ | `packaging/` (icons, .desktop, debian scripts, `install-kind` markers) | The new release.yml reuses the layout and secrets (§3.10). |
| examples/release_manifest.rs, src/release_manifest.rs | `tools/Barbican.ReleaseTool` (C#) | Same manifest JSON, same Ed25519 key format, same channel rules. |
| plugins/spam-score | `tests/fixtures/plugins/` (the DLL and its source) | ABI test fixture. |
| The user's memory notes (push always, test accounts, no real input, model choice, design, feel) | `CLAUDE.md` / `AGENTS.md` at the new repo root | Every agent reads these. |
| Bulwark tree (version 1.21.2) and Stalwart source | Referenced by path and version in `spec/README.md` | Bulwark refs keep their file:line form. Stalwart: `stalwart-0.16.27.zip` is the copy on disk (JMAP-COMPLIANCE.md cites `stalwart-main.zip`, which is no longer there). Re-crawls diff against these pinned versions. |

### 2.3 The behaviour catalogue (`spec/`)
This is built in M0, before any porting starts, and it is the only way the Rust knowledge enters the C# code.

```
spec/
  README.md               ID scheme, statuses, how to update, the pinned Bulwark/Stalwart versions
  rust-tests.csv          T- rows: module, Rust test fn, one-line assertion, C# test name, status
  behaviours/<area>.md    B-<AREA>-### rows: one rule each, with source (file:line or test, commit), Bulwark ref
  decisions.md            D-### rows: decision, reason, commit hash, area, linked B-/I- ids
  interactions.csv        I-### (above)    jmap.csv  J-###    gaps.csv  G-###
  settings.csv            S-<key>: page, kind, Bulwark default, read-by (Rust), read-by (C#)
  menus.md                the 21 context menus, item by item, with enable rules (from ctxmenu.rs)
  shortcuts.md            every shortcut per app (shortcuts.rs, lib/keyboard-shortcuts)
  live-checks.md          L-### rows (never verified live)
  automation-protocol.md  the command set (automation.rs header) plus new commands
  layout/<screen>.md      tuned metrics from ui/*.slint
  feel/                   rust-baseline/, scenarios, C# results per milestone
```

How it is built (model choice per §6.2):
1. **Haiku, one agent per group of about 8 modules.** For each Rust module, extract:
   - the `//!` header, the doc comment of every pub fn, each test (name plus a one-line description of what it asserts);
   - every constant with a reason in its comment (for example bodycache's 120 entries / 48 MB, the 2.5 × ping push deadline, the 150 ms skeleton rule).

   Output goes to `behaviours/<area>.md`, with file:line for each row.
2. **Haiku, 4 agents of about 50 commits each.** Turn every commit subject and body into candidate decisions. One rule per line, with the commit hash.
3. **Sonnet.** Dedupe and classify the decisions into areas. Link them to B- and I- rows. Flag contradictions, for example GAPS P1-14 against b9b3e99 on `cid:` images.
4. **Sonnet.** Re-baseline INTERACTIONS.md against the merged Rust main. This means grepping the code, ctxmenu.rs, shortcuts.rs and settings wiring for each row, and setting Rust status D/P/M/N with evidence.
5. **Opus, sample review.** Check the catalogue for the hard areas (JMAP, sync, push, msgload/mailview, rte, recurrence, OAuth, updater) against the code. Anything missing is added.

Output size, as a sanity check: about 353 T- rows, about 1,000–1,500 B- rows, about 300 D- rows, 471 I- rows, 201 S- rows, about 40 L- rows.

### 2.4 Rust unit tests become the C# spec suite, ported first
- Every one of the 353 Rust tests becomes a C# xUnit test, with the same fixtures and expected values.
- Each one carries `[RustTest("jmap::list_delta_inserts_removes_and_updates")]`.
- They are written **before** the C# code they test, in the milestone that owns the module (Appendix B), and start red.
- A Rust test may be adapted only where the API shape changes. The assertion itself may not change without a D- entry explaining why.
- Library replacements must pass the old tests. For example, recipient parsing on MimeKit must still pass `recipients.rs`'s 8 tests. That is what keeps "replace with a library" from silently changing behaviour.
- Bulwark's captured test vectors (for example the Stalwart state codec) move into `tests/fixtures/` unchanged.

### 2.5 The "nothing lost" checklist
A milestone is not done until each line is ticked in its report (`docs/milestones/M<n>.md`):

- [ ] **Rust tests:** every T- row owned by this milestone has a C# test that passes, or a D- entry that says why not. `tools/spec-check` shows 0 unmapped.
- [ ] **Behaviours:** every B- row of the milestone's areas is `ported`, `deferred(M<k>)` or `dropped(D-###, user OK)`.
- [ ] **Interactions:** I- rows of the milestone's areas have C# status and evidence (a test, an automation script, or a screenshot path).
- [ ] **JMAP:** J- rows touched are re-verified, with a test and Bulwark plus Stalwart refs, and `spec/jmap.csv` is updated.
- [ ] **Settings:** S- keys of the milestone's areas are read by code. Keyless settings rows are not dropped (fixes GAPS cross-cutting finding 1).
- [ ] **Menus and shortcuts:** the milestone's menus and shortcuts match `menus.md` and `shortcuts.md` item by item.
- [ ] **Feel:** motion and flicker probes for the milestone's scenarios are run. Results are committed to `spec/feel/M<n>/` and are no worse than `rust-baseline` (§4.5).
- [ ] **Design:** screenshots for Bento and Nordic, dark and light, are committed next to the matching mockup. There are no unexplained differences.
- [ ] **Live checks:** L- rows of the milestone are verified (read-only on example, writes on example-org) or explicitly carried forward.
- [ ] **Docs:** ROADMAP.md and GAPS.md in the new repo are updated. Every commit is pushed, including every branch.
- [ ] **Reference intact:** the Rust repo's `rust-final` tag is still there, and its working tree is clean.

### 2.6 M0 freeze actions (must happen before anything else)
1. **Three worktrees hold uncommitted Rust work**:
   - `wt-mat-overlay` (ui/app.slint);
   - `wt-mat-polish` (dark_mail.rs, email_dark.js, main.rs, platform/win.rs, ui files);
   - `wt-mat-switching` (mailview.rs, main.rs, msgload.rs, app.slint).

   These are probably another agent's live work, so check with the session owner before touching them. Then commit each to its own `mat-*` branch as "WIP frozen at port start" and push. The `mat-*` branches are not on origin yet. Their intent goes into the catalogue. For example, `mat-switching`: "the message on screen stays whole under the bar, dimmed, until the new one swaps in".
2. Push every local branch (37). Record the branch list and each branch's purpose in `docs/legacy/branches.md`.
3. Tag `rust-final` on `main` and push the tag.
4. Import the prototype `barbican-winui`, which has no remote, into the new repo with its history, and push.
5. Copy the docs, design, tools and packaging as listed in §2.2.

---

## 3. Target architecture

### 3.1 The app head: one Uno single project (recommended)

| Option | What it is | Verdict |
|---|---|---|
| **A. One Uno.Sdk project, two TFMs** | `net10.0-windows10.0.26100.0` builds the **real WinUI 3** app on Windows App SDK (the stack of the prototype the user liked). `net10.0-desktop` builds Uno Skia for Linux X11, and also runs on Windows for quick Skia checks. | **Recommended.** One set of XAML views, Linux compiles on every commit, and the Windows build is still Microsoft's WinUI. |
| B. Separate App.WinUI and App.Uno projects sharing views through a shared project | Plain WinAppSDK project like the prototype, plus a desktop-only Uno project | Fallback only: two bootstraps, clumsy linked XAML, drift between them. |
| C. Uno Skia on both OSes | Desktop TFM on Windows too | Rejected. It gives up WinUI on Windows. Uno's Skia WebView2 on Windows is a native HWND, which brings back the airspace problem the port is meant to escape. |

**Gate at the end of M0 (spike S1).** Option A must:
- build and run the prototype's XAML on both TFMs;
- accept the Windows App SDK version the prototype uses (2.6.5) through Uno.Sdk's WinAppSDK version override;
- publish self-contained and unpackaged;
- keep inner-loop build time acceptable with `-f` for a single TFM.

If any of these fails, use B, with views in a shared project. Core and Presentation do not change either way.

**Rule for XAML.** Views use only what Uno implements on Skia. Windows-only pieces (Mica, some title-bar APIs, Win2D) go behind `win:` / `not_win:` XAML namespaces or platform services.

Facts that matter, from Uno's documentation (§8):
- Uno 6.6 (August 2026) is current; .NET 10 has been supported since 6.4.
- On Linux, `WebView2` is a GTK `WebKit.WebView` hosted in a separate window, and `WebResourceRequested` is not implemented there.
- Linux needs `libwebkit2gtk-4.1` and `GDK_BACKEND=x11` on Wayland.
- `RichEditBox` is not supported on Uno Skia.
- Uno 6.6 completed IME composition on Linux.
- Screen-reader support for Skia on Linux is still "planned".

### 3.2 Solution layout
```
barbican/
  global.json                 .NET SDK 10.0.x + Uno.Sdk pinned
  Directory.Build.props       strictness (§4.1)        Directory.Build.rsp   -maxCpuCount:4 -nodeReuse:false
  Directory.Packages.props    central package versions BannedSymbols.txt     banned APIs (§4.1)
  CLAUDE.md                   standing rules for agents (§6)
  src/
    Barbican.Core/            net10.0, no UI types. Result, models (records), JMAP client, sync, caches,
                              services per area, Bulwark-compat formats, editor document model, settings
    Barbican.Presentation/    net10.0. ViewModels (CommunityToolkit.Mvvm), navigation state, ListSync,
                              KeyRouter + shortcuts, MenuSpec (context menus as data), toasts, IDispatcher
    Barbican.Platform/        interfaces: ISecretStore, INotifier, ITray, ISystemTheme, ISpellChecker,
                              IAutostart, IProtocolRegistration, ISingleInstance, IFileDialogs, IUpdateInstaller,
                              IWindowCapture, IBodyOverlayStrategy
    Barbican.Platform.Windows/  net10.0-windows10.0.26100.0 (CsWin32, Windows App SDK APIs)
    Barbican.Platform.Linux/    net10.0 (Tmds.DBus, WeCantSpell.Hunspell, X11 capture)
    Barbican.App/             Uno.Sdk single project (§3.1): Views, Themes/Tokens.xaml, Controls, MailBody,
                              Editor view, Automation server (AUTOMATION builds only), bootstrap/DI
  tests/
    Barbican.Core.Tests/            the spec suite (ported Rust tests first) + new tests
    Barbican.Presentation.Tests/    ViewModels against FakeJmap, no UI
    Barbican.FakeJmap/              in-process JMAP server (sessions, states, /changes, /queryChanges, push)
    Barbican.Jmap.Live/             opt-in live suites: read-only (example) and write (example-org) (§4.6)
    Barbican.UiTests.Windows/       FlaUI, UIA patterns only (§4.4)
    Barbican.UiTests.Linux/         CI only, Xvfb
  tools/  build.ps1 / build.sh (slot-limited builds), spec-check, gen-tokens, trace-diff, ReleaseTool,
          auto.py, flicker.py, motion.py, scenarios.py
  spec/  docs/  design/  packaging/  .github/workflows/
```
**Lesson from the Rust app.** `ui/app.slint` grew to 6,059 lines, and parity round 2 had to keep one version and hand-insert every stream's hooks back into it. In C#:
- one view per file;
- one ResourceDictionary per feature;
- one DI registration file per feature;
- a shell that only composes regions.

Parallel streams then rarely touch the same file.

### 3.3 Layers and threading
- **Core** is platform-agnostic and async throughout. It has no UI types. `TimeProvider` is injected for time.
- **Presentation** holds ViewModels as `ObservableObject` with `[ObservableProperty]` and `[RelayCommand]`. The UI thread owns all ViewModel state. Services run on the thread pool and marshal results back through `IDispatcher`, which wraps `DispatcherQueue` (the same API on WinUI and Uno).
- **Superseded work is cancelled, not ignored.** Rust used sequence numbers (`OPEN_SEQ`, `DOC_SEQ`, `generation`). In C#, each open, search or folder switch has its own `CancellationTokenSource`, plus a sequence number for late callbacks.
- **No blocking IO on the UI thread.** Rust still breaks this (`Settings::load` and `A11y::load` in `open_mail`). In C# it is enforced: settings are an in-memory snapshot with async debounced save, and a debug assertion fires in the file layer when called on the UI thread.
- **Dependency injection.** Plain `Microsoft.Extensions.Hosting` and `DependencyInjection`. Uno.Extensions navigation and reactive are not used: the app has a fixed shell (rail apps plus overlays), and the extra abstraction would only cost debuggability.
- **Bindings.** `x:Bind` (compiled) everywhere, never `{Binding}` reflection paths, so the build stays AOT and trim friendly.

### 3.4 JMAP layer (Opus-owned)
- **Transport.**
  - One `HttpClient` per server origin, on `SocketsHttpHandler` (pooled, HTTP/2 where offered).
  - Bearer or Basic auth, with OAuth renewal on 401 for the API, upload, download and the event source.
  - Read-only methods (`/get`, `/query`, `/changes`, `/queryChanges`, `/validate`, `/parse`) retry once after `Retry-After` (at most 10 s). Writes never retry.
- **Session.**
  - A `Session` record, re-fetched when `sessionState` changes.
  - Limits read from the live copy, and account-level capabilities through `AccountCapability<T>()`. This was a real bug in Rust: `maxDelayedSend` read at the wrong level.
- **Requests.**
  - A typed request builder with result references (`#ids`, `resultOf/name/path`) and creation ids.
  - `SplitCalls` respects `maxCallsInRequest` and keeps linked calls together.
  - `GetAll` and `QueryAll` chunk by `maxObjectsInGet`/`InSet` and server query limits.
  - The request size is checked up front.
  - A `SemaphoreSlim` gate for `maxConcurrentRequests`, and another for uploads.
- **Errors.** Return `Result<T>` with a closed `JmapError` hierarchy:

```csharp
public readonly record struct Result<T> { /* IsOk, Value, Error, Map, Bind, Match; no implicit throw */ }
public abstract record JmapError(string Message);
public sealed record ProblemError(int Status, string Type, string? Detail, string? Limit) : JmapError(...);  // RFC 7807
public sealed record MethodError(string Method, string Type, string? Description) : JmapError(...);
public sealed record SetError(string Id, string Type, string? Description, IReadOnlyList<string>? Properties) : JmapError(...);
public sealed record TransportError(string Message, bool Retryable) : JmapError(Message);
public sealed record AuthError(string Message) : JmapError(Message);
```

  Every method and set error type in JMAP-COMPLIANCE.md is mapped to a user message (port of `problem_message`, `method_error` and `set_error`). Cancellation stays `OperationCanceledException`. Exceptions never cross the JMAP boundary for expected failures.
- **Models.**
  - `sealed record`s with `System.Text.Json` source generation.
  - `[JsonExtensionData]` on every server object, so unknown properties survive a read-modify-write. This matters for JSContact unknown entries, CalendarEvent extensions and FileNode draft shapes.
  - Patches are built as JSON Pointer paths, using a port of `keyword_path` (`~0`/`~1`).
- **Push.**
  - An EventSource client (port of the tested `SseParser`) with `types=Email,Mailbox,Thread,EmailDelivery,EmailSubmission` (plus calendar, contacts and files types, see "Better than Rust" below), `closeafter=no`, `ping=30`.
  - A 75 s read deadline.
  - Exponential backoff from 2 s to 5 min with jitter.
  - A full delta sync on every (re)open.
  - `StateChange` routed per account and type into a `Channel<PushEvent>`.
- **Sync engine.** One per account:
  - `Mailbox/changes`, with the counts-only fast path;
  - the 7-call list delta (`Email/changes` + `Email/queryChanges` + `Email/get` + `Thread/changes`/`get`), applied with the RFC 8620 §5.6 algorithm (port of `apply_list_delta`);
  - `anchor` paging;
  - a full refetch on `cannotCalculateChanges`, `tooManyChanges`, `hasMoreChanges` or a Stalwart state going backwards (port of `stalwart_change_id` and `went_back`, with Bulwark's vectors).
- **Better than Rust (GAPS P1-10).**
  - The same generic `/changes` engine also follows Calendar, CalendarEvent, AddressBook, ContactCard, FileNode and Identity, so they refresh live.
  - `ShareNotification` and `CalendarEventNotification` are wired in M4 and M5.
- **Admin is a separate client.** The Stalwart registry client (`x:*` with the admin API key, `request_using`) is not part of the user session (as in Rust).
- **Trace.** `BARBICAN_JMAP_TRACE=<file>` logs method names only, never bodies. `tools/trace-diff` compares the call sequences of the Rust app, the C# app (and Bulwark, from browser network logs) for the same scenario (§4.6).

### 3.5 Storage
- **Folders.** The same ones as Rust:
  - `%APPDATA%\Barbican` (config) and `%LOCALAPPDATA%\Barbican` (data) on Windows;
  - `$XDG_CONFIG_HOME/barbican` and `$XDG_DATA_HOME/barbican` on Linux.
- **File formats.**
  - Same names and formats (§1).
  - JSON is read as `JsonNode` where Bulwark keys may be unknown to us, so nothing is dropped on save.
  - Every write is atomic: write a temp file, then replace.
- **Dev profile during the transition (important).** Until M8, C# dev builds use `BARBICAN_PROFILE=dev`, which gives `Barbican-dev` folders and `*.Barbican-dev` credential targets.
  - On first run, the dev profile **imports a copy** of the Rust app's prefs, panes and search history. It never writes the Rust profile.
  - The user signs in to the dev profile by hand. Sharing the Rust app's OAuth refresh token would log one of the two apps out on rotation; commit e9f51c8 shows why.
  - M8 switches release builds to the real profile.
- **Secrets.**
  - Windows: Credential Manager through `CredReadW`/`CredWriteW` (CsWin32).
  - Linux: Secret Service over D-Bus (Tmds.DBus), with the keyring crate's attributes (`service`, `username`, `target`).
  - Same item names as Rust.
- **Cache.** In memory, as in Rust:
  - body cache: 120 entries / 48 MB, LRU;
  - thread cache: 120 / 8 MB;
  - patched by syncs and local actions, cleared on sign-out, with generation guards.
- **Persistent cache (better than Rust; see Q4).** SQLite (`Microsoft.Data.Sqlite`), one database per account in the data folder:
  - mailbox tree, list pages, headers, sync states, and a bounded body store;
  - the app shows the last known Inbox on the first frame after a cold start, then syncs with `/changes`;
  - it is also the base for later offline reading (GAPS P3);
  - it is off for the read-only example automation profile.

### 3.6 Mail body view (port of mailview.rs, msgload.rs, bodycache.rs, dark_mail.rs; Opus-owned)
**The design is kept, because it is what made switching flicker-free:**
- One persistent WebView2 document, the "shell", which never navigates.
- Each message is a **sandboxed `srcdoc` iframe with its own CSP** (the per-message image policy). It loads hidden next to the one on screen.
- The shell posts `ready:N` once the frame is laid out.
- The native header changes in the same dispatcher turn as `reveal(N)`.
- `dim(on)` fades the old body when loading takes over 150 ms.
- Large pages are sent as a web message, not through `NavigateToString`, which avoids its 2 MB limit.

**WebView2 setup:**
- `DefaultBackgroundColor` is the tile colour.
- One WebView is created warm at startup (Rust's `motion-warm` lesson).
- Dev tools are off in release.
- The default context menu is replaced by the app's native one, through `postMessage`.
- New windows and main-frame navigations are blocked and routed to the browser or composer, after the link check.
- Keys are forwarded the same way Rust's `kd()` does.

**Windows (WinUI 3):** XAML overlays compose over WebView2, so the capture-under-overlay workaround and its `FRESH`/`HIDE_PENDING` state machine are **not ported** to Windows. This is spike S2's proof point. `AddWebResourceRequestedFilter` adds defence in depth: remote loads are blocked unless the policy allows them, on top of the CSP.

**Linux (Uno, WebKitGTK in a separate X11 window):** the airspace problem exists here.
- `IBodyOverlayStrategy.Linux` ports the Rust capture trick: before an overlay opens, capture the WebKit window (XGetImage or XComposite), show the image in its place, hide the native view, and restore it afterwards.
- `WebResourceRequested` is not available on Linux, so the shell must not rely on it. `cid:` parts become `data:` URIs before injection (as Rust already does), and remote blocking is by CSP only.
- Spike S2 verifies `ExecuteScriptAsync` and the `postMessage` path on Linux. If either fails, the fallback is plain-text mode on Linux until it is fixed.

**Dark mail:** email_dark.js runs verbatim inside the frame, allowed by its CSP hash, and the page is ready only after the dark pass.

**Accessibility CSS** (a11y.rs: reading font, spacing, CVD palette) is injected the same way.

### 3.7 UI composition: design tokens, motion, lists, menus
- **Tokens.**
  - `Tokens.xaml` holds the palette as `SolidColorBrush` resources.
  - Theme, style and contrast switches **mutate brush colours in place**, as the prototype's `Palette.Apply` does. Nothing is re-templated, so there is no flash.
  - `ThemeResource` switching on the root is not used for the palette.
  - Shape tokens (radii, gaps, paddings) are per style, in two ResourceDictionaries swapped in once.
- **Icons.** The `I` line-icon paths from the mockups become `PathIcon` geometry resources (the prototype's `SIcon`). That also closes the ROADMAP leftover: no Segoe Fluent glyphs in menus or the composer toolbar.
- **Motion tokens.**
  - Resources: `Motion.Fast` 100 ms, `Enter` 150 ms, `Exit` 90 ms, `View` 110 ms, `TipDelay` 350 ms, and `Ease.Enter`/`Exit` as KeySplines.
  - Reduce motion sets every duration to 0. Its sources are the setting and `UISettings.AnimationsEnabled` (Windows) or the GNOME/portal setting (Linux).
  - `Motion.Rapid` is on while the list selection moves faster than an animation.
- **WinUI defaults that break the motion rules are switched off:**
  - `ListView`/`ItemsRepeater` item container transitions (add, delete, reposition, entrance) are cleared, because the default animates rows on refresh;
  - Frame navigation transitions are not used;
  - `ContentDialog` (its animation runs past 200 ms) is replaced by a custom dialog host (Scrim + PopIn);
  - flyouts set `AreOpenCloseAnimationsEnabled="False"` and get the PopIn behaviour (fade plus 0.96 scale from the anchor corner, 150 ms);
  - tooltips use their own `Tip` behaviour, which waits 350 ms.
- **Lists.**
  - `ListSync.SyncInto(ObservableCollection<T>, IReadOnlyList<T>, key, same)` ports listsync.rs with the same thresholds: reset when fewer than half the rows are kept, more than 8 moves, or duplicate keys. Its 13 tests come with it.
  - Lists are never re-assigned on refresh, so hover and hover actions survive a refresh, as Rust measured.
- **Context menus.**
  - The 21 Bulwark menus are data (`MenuSpec` trees with enable/visible rules and commands) in Presentation, ported from ctxmenu.rs.
  - One builder turns a spec into a `MenuFlyout` with `MenuFlyoutSubItem` flyouts. Confirmations swap the items in place, as in Rust.
  - Row height is 30 px, and line icons are used.
- **Keyboard.**
  - Every key goes through a central `KeyRouter` in Presentation: real KeyDown events, keys forwarded from the WebView, and keys from the automation channel.
  - This fixes the Rust early return that gave Calendar, Contacts and Files no shortcuts (INTERACTIONS cause 1).
  - The shortcut tables port shortcuts.rs and its 11 tests.

### 3.8 Rich-text composer (port of src/rte; Opus-owned)
**Options:**

| Option | Assessment |
|---|---|
| WebView editor (TipTap/ProseMirror, as Bulwark) | The best fidelity and the least work, but it breaks the user's "no web UI" rule. **Not chosen.** Never use it as a fallback without asking the user. |
| `RichEditBox` | Native on Windows, with IME, spelling and accessibility built in. But it speaks RTF, so the HTML round trip loses tables, nested lists, the quoted original, `cid:` images and Bulwark's spacing. It is also **not supported on Uno Skia**, which would mean two editors. **Not chosen.** |
| **Custom native editor** (recommended) | Port the tested Rust design: document model, editing commands, undo, input rules, tolerant HTML import and email-safe export, all in **Core**. The view is a custom control that lays text out and paints it itself. |

**Recommended design:**
- **Core/Editor.** Port `model.rs`, `editor.rs` and `html.rs` with their 29 tests. HTML import walks an AngleSharp DOM instead of the hand-written tolerant parser, and must pass the same import tests. Export stays hand-written, because it is Bulwark's exact inline-style output, and it is snapshot-tested with Verify.
- **App/Editor view.**
  - Layout and paint through one text engine on both heads: **Topten.RichTextKit** on SkiaSharp. Version 0.6.173 has mixed styles, BiDi, UAX#14 line breaking, font fallback, hit testing, caret geometry and a document model for editors. This plays the role parley played in Rust.
  - Hosted in `SKCanvasElement` on Uno and `SKXamlCanvas` or `SKSwapChainPanel` on WinUI.
  - Caret, selection, the floating toolbar, the link popover, the table menu and image resize handles are XAML overlays.
- **IME.** The same trick as Rust: a transparent `TextBox` at the caret receives composition events and forwards committed text, while the editor owns every other key. This needs WinUI's TextBox composition events, and Uno 6.6's IME on Linux.
- **Spelling.**
  - Windows: `ISpellChecker` through COM (port of rte/spell.rs and its 4 tests).
  - Linux: **WeCantSpell.Hunspell** (pure managed) with the system dictionaries, which replaces the `hunspell -a` pipe.
- **Accessibility.** A custom `AutomationPeer` with a `TextPattern` provider, so Narrator reads and navigates the composer.
- **Gate (spike S3, then M3).** The same paragraph rendered by the editor and by a XAML `TextBlock`, in Segoe UI at 15 px, must look the same in a side-by-side screenshot. If DirectWrite and Skia differ visibly on Windows, add a second layout backend on **Win2D (DirectWrite)** behind `ITextLayoutEngine` for the Windows head. The model and commands do not change.
- **RichTextKit risk.** It is MIT licensed, and SkiaSharp 3 compatibility is unverified. If the package lags, vendor (fork) it into `src/ThirdParty/`.
- **Interim, until the editor view lands in M3.** The composer pipeline (recipients, drafts, send) is built against the Core document model, with a plain-text view. This is the same plain-text mode Bulwark offers as a setting. No throwaway rich editor is built.

### 3.9 Platform services

| Service | Windows (Barbican.Platform.Windows) | Linux (Barbican.Platform.Linux) |
|---|---|---|
| Secrets | Credential Manager (CsWin32), same targets | Secret Service (Tmds.DBus), same attributes |
| System theme / reduce motion | `UISettings` (colour, `AnimationsEnabled`) | XDG portal `org.freedesktop.appearance color-scheme`, then gsettings |
| Notifications | `Microsoft.Windows.AppNotifications` (works unpackaged): Rust's app id, tag/group per account, Archive / Mark read buttons, click opens the message | `org.freedesktop.Notifications` over D-Bus with actions |
| Tray and unread badge | H.NotifyIcon.WinUI. Badge icon drawn from barbican.svg with stroked digits (port icon.rs using Svg.Skia). Taskbar overlay through `ITaskbarList3`. | StatusNotifierItem + `com.canonical.dbusmenu` over Tmds.DBus. GNOME needs the AppIndicator extension, as with Rust ksni. |
| Single instance, mailto handoff | `AppInstance.FindOrRegisterForKey` + `RedirectActivationToAsync` | Unix domain socket handoff (port instance.rs) |
| Default mail app, start at login | HKCU registration (port; unit-tested, and only written with user consent) | `.desktop` + `xdg-mime`, `~/.config/autostart` |
| Spelling | ISpellChecker | WeCantSpell.Hunspell |
| Window capture (automation, flicker probe) | Windows.Graphics.Capture frame pool (every composed frame, with timestamps); fallback `PrintWindow(PW_RENDERFULLCONTENT)` as in capture.rs | X11 `XGetImage` on Xvfb or the WebKit window |
| File dialogs | WinUI pickers (with the HWND init) | Uno's pickers (portal) |

### 3.10 Packaging, updater, CI
- **Windows: unpackaged and self-contained** (the prototype's `WindowsAppSDKSelfContained`, so no runtime install).
  - **Velopack** builds the per-user `Setup.exe`, needing no admin, with a Start menu entry. It also produces a **portable zip** and full plus delta update packages.
  - Authenticode signing runs through a hook when a certificate exists (Q5).
  - MSIX is not used: it is Store and sandbox oriented and needs signing. MSI (WiX) is only for managed deployment if it is ever asked for.
- **Linux.**
  - **AppImage** through Velopack: self-contained .NET, system WebKitGTK, and it updates in place.
  - **.deb** with `install-kind` = apt, so the app only *notifies* about updates, since apt owns the files (Rust decision kept).
  - **Flatpak** after 1.0: the GNOME runtime brings WebKitGTK, but the sandbox needs portal work.
- **Updater.**
  - Velopack does download, delta, apply and restart.
  - Before Velopack sees a feed, `Core/Updates` downloads `<channel>.json` and `.sig`, verifies them **Ed25519** with the built-in public key (BouncyCastle, managed), and checks expiry and the SHA-256 of each asset.
  - Rules are kept from updater.rs (13 tests): beta follows stable, 180-day manifest expiry, check every 4 h when automatic, "Restart now" or "Install next time".
  - A private repo needs a public mirror (`BARBICAN_UPDATE_ASSET_BASE`, Q6).
- **CI (`.github/workflows/ci.yml`)**, on every push:
  - `windows-latest`: build both TFMs, run Core and Presentation tests, UIA smoke (patterns only), axe-windows scan;
  - `ubuntu-24.04`: build `net10.0-desktop`, run Core tests, Xvfb smoke with screenshots.
  - Path filters and `concurrency` cancel-in-progress keep private-repo minutes down.
- **Release (`release.yml`)**, on tag `v*`:
  - Windows: publish, then `vpk pack` (Setup, portable zip, nupkgs), then sign;
  - Linux: publish, then `vpk pack` (AppImage) and `dpkg-deb` (.deb);
  - then SHA256SUMS, the Ed25519 manifest (`tools/Barbican.ReleaseTool`, the same format and secrets as today), the GitHub Release, and the public mirror.
- **The Rust workflows never ran.** The C# release workflow must be exercised end to end once (a beta tag) before 1.0.

### 3.11 Admin dashboard
- **Charts.** Rust generated SVG paths that are "WebUI-identical" (dashboard.rs: Counter summed per bucket, Gauge averaged and carried forward, Histogram mean, smooth stacked bands, fit-to-fill, thinned x-axis ticks). That geometry ports into Core and is drawn with XAML `Path`/`PathGeometry`, themed by tokens.
  - No chart library: it would not match Stalwart's WebUI exactly, and the geometry is already tested.
  - **LiveCharts2** (SkiaSharp; supports WinUI and Uno) is the fallback if interactive zoom or pan is ever needed.
- **Pages.** Directory, domains, queue, reports, logs, tasks, cluster and actions, ported first. Then the create/edit depth of Stalwart's WebUI (GAPS P3).
- **Safety.** Admin writes are **never** exercised against the live server.

### 3.12 Libraries (and what each replaces)

| Library | Replaces / used for |
|---|---|
| MimeKit | `.eml`/`.mbox` import parsing (mailimport.rs), MDN multipart/report (mdn.rs), view source and forward as attachment, RFC 5322 address and group parsing (part of recipients.rs), **TNEF/winmail.dat** (closes GAPS P1-11) |
| AngleSharp (+ .Css) | rte HTML import, `html_to_text`, link extraction for phishing and mismatch checks (GAPS P1-12), quote detection |
| Ical.Net | `.ics` import/export, webcal feeds. **Not** the main recurrence engine: recurrence.rs is ported as is, and Ical.Net is the oracle in differential tests. |
| NodaTime | Wall-clock and time-zone arithmetic for JSCalendar recurrence, reminders and invitations |
| FolkerKinzel.VCards | vCard 2.1/3.0/4.0 parsing and writing. The JSContact mapping is ported from vcard.rs, with its tests. |
| System.IO.Compression | zip.rs (stored entries, UTF-8 names) |
| DnsClient | BIMI and DMARC TXT lookups (avatars.rs) |
| Microsoft.Extensions.AI | ai.rs and agent.rs: `IChatClient` with function invocation, for OpenAI-compatible and Anthropic providers |
| CommunityToolkit.Mvvm; CommunityToolkit.WinUI (Sizers) | ViewModels; pane splitters (panes.json format kept) |
| Velopack, BouncyCastle | Install and update; Ed25519 manifest verification |
| Tmds.DBus, WeCantSpell.Hunspell, H.NotifyIcon.WinUI, CsWin32 | Platform services (§3.9) |
| Topten.RichTextKit, SkiaSharp, Svg.Skia | Editor text engine; badge icon rendering |
| xUnit v3, Verify, FlaUI (UIA3), Axe.Windows | Tests, snapshots, UIA pattern tests, accessibility scans |

Ported by hand because they are Bulwark-specific: Sieve generator and parser, quick rules, templates in metadata, tags, verification-code detection, the search grammar, schedule presets, mailto (RFC 6068 rules), OAuth for Stalwart (discovery, dynamic registration, loopback `127.0.0.1:8791`), the Stalwart state codec, unified inbox and avatars.

---

## 4. Quality bar and engineering rules

### 4.1 C# strictness (Directory.Build.props, for every project)
```xml
<Nullable>enable</Nullable>  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<AnalysisLevel>latest-recommended</AnalysisLevel>  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
<ImplicitUsings>enable</ImplicitUsings>  <LangVersion>latest</LangVersion>  <Deterministic>true</Deterministic>
<IsAotCompatible>true</IsAotCompatible>  <!-- Core, Presentation: forces source-generated JSON, flags reflection -->
```
- **Analyzers:**
  - .NET analyzers;
  - **Microsoft.VisualStudio.Threading.Analyzers** (no `async void` except event handlers, no `.Result`/`.Wait()`, no fire-and-forget without a tracked task);
  - **Meziantou.Analyzer**;
  - **BannedApiAnalyzers**, with this `BannedSymbols.txt`:
    - `DateTime.Now`/`UtcNow` in Core (use `TimeProvider`);
    - `Thread.Sleep`;
    - `new HttpClient` outside Transport;
    - `File.*` in Presentation;
    - in all test projects: `FlaUI.Core.Input.Mouse`, `FlaUI.Core.Input.Keyboard`, `SendInput`, `InputInjector`;
  - CommunityToolkit.Mvvm and xUnit analyzers.
- **Models:** `sealed record`s, immutable collections, `required` members.
- **Expected failures:** `Result<T>`, never exceptions.
- **Module headers:** every Core file starts with the Bulwark file, RFC section and Stalwart path it mirrors. This keeps the Rust habit that made the code auditable.
- **Commits:**
  - small, with a message that says the behaviour decided and why (the commit log is part of the knowledge base);
  - each ends with the `Co-Authored-By` line;
  - **push after every commit, on every branch.**

### 4.2 Testing pyramid
1. **Core spec suite** (xUnit). The 353 ported Rust tests plus new ones, with RFC-tagged JMAP tests. Fast, and run on every build. Line coverage of at least 85 % on Jmap, Sync, Editor, Sieve and Recurrence.
2. **Presentation tests.** ViewModels against `Barbican.FakeJmap`, an in-process server with real state strings, `/changes`, `/queryChanges`, set errors, limits and push. Flows such as open, switch, delete with undo, delta arrival during paging, and an autosave race all run without UI.
3. **Contract fixtures.** Recorded Stalwart responses for each method shape, including 0.16 against 1.0 FileNode and the empty session-level submission capability.
4. **Live suites (opt-in, §4.6).** Read-only conformance on example; write flows only on example-org.
5. **Hidden in-app automation (§4.4)**, for scenarios, screenshots, motion and flicker. This is the agents' main feedback loop.
6. **UIA pattern tests** (FlaUI). They run locally against the off-screen window and in CI.
7. **Real-input tests** (hover, drag-and-drop, IME, focus). **Only inside CI virtual machines**, never on the user's machine.
8. **Visual review.** Screenshots per screen in style × theme, next to the mockups, at each milestone gate. A human (or Sonnet) reviews them; there is no pixel-diff gate, because WebView and fonts vary.

Every bug fix adds a test at the lowest level that can show it.

### 4.3 Spec traceability
- Tests carry `[RustTest]`, `[Spec("B-…")]`, `[Interaction("I-…")]`, `[Rfc("8620","5.6")]`, `[LiveCheck("L-…")]`.
- `tools/spec-check` joins these with `spec/*.csv` and prints coverage per milestone.
- **CI fails** when a T- row owned by a closed milestone has no passing test, or when a test cites an unknown ID.
- `spec/jmap-conformance.md` is generated from the `[Rfc]` tags. It replaces hand-kept compliance tables.

### 4.4 Hidden UI automation: never touches the user's mouse or keyboard
Rules, carried from the Rust era and enforced:
- No `SendInput`, `InputInjector`, FlaUI `Mouse` or `Keyboard`, `drive.ps1`, or real window focus stealing on the user's desktop. This is a banned-API analyzer error in every test project, and CI greps scripts for these names.

**Layer A: in-process automation server** (AUTOMATION builds only; `BARBICAN_AUTOMATION=1`, `BARBICAN_AUTOMATION_PORT`, loopback only). This is the same line protocol as automation.rs, so `auto.py`, `motion.py`, `flicker.py` and `scenarios.py` work unchanged.
- **Window.** Placed off-screen, shown without activation (`WS_EX_NOACTIVATE`, no taskbar button), with its own profile (`BARBICAN_PROFILE=auto-<port>`) and its own instance key.
- **Input.**
  - `click`/`rclick`/`dclick` by coordinates are resolved to the element under the point, then performed through its AutomationPeer pattern (Invoke, Toggle, SelectionItem, ExpandCollapse), or through the routed command for context menus.
  - `key`/`type` go into the app's `KeyRouter` and the focused element's text pattern.
  - `scroll` uses the scroll pattern or `ScrollViewer.ChangeView`.
  - New commands by AutomationId (`invoke ID`, `select ID`, `text ID VALUE`) make scripts robust to layout changes.
- **Out of scope for Layer A.** Pointer hover, drag gestures and IME cannot be injected in-process in WinUI. They belong to Layer C.
- **Capture.** `shot` uses `RenderTargetBitmap` for the XAML. `wshot` and `burst` use Windows.Graphics.Capture on the window, including WebView2, every composed frame with its timestamp; the fallback is `PrintWindow` as in capture.rs.
- **Probes.** `probe-start`/`probe-stop` record `CompositionTarget.Rendering` frame times and command dispatch times in Rust's JSON format. `every`, `motion reduce|rapid`, `ui bento|nordic dark|light`, `cache-clear`, `rx-banners`, `cal-dump`, `pim`, `notes`, `pick`/`save`, `wv-eval` are all ported.

**Layer B: UIA pattern tests** (FlaUI UIA3). These run against the off-screen app, through patterns only. UIA pattern calls are programmatic and do not move the pointer or take the keyboard. Every interactive control has an `AutomationId` and a name; this doubles as accessibility work.

**Layer C: real input, in throwaway virtual machines only.**
- GitHub Actions Windows runners: FlaUI with real input, hover, drag-and-drop, Narrator-free UIA checks.
- Linux: Xvfb with `xdotool` on its own display.
- Locally, only if the user opts in: Windows Sandbox or WSL2 (both are OS changes; Q2). The default is no local real-input tests at all.

### 4.5 Feel: measuring rather than eyeballing
- **Baselines.** `spec/feel/rust-baseline/` holds:
  - flicker: open_first, open_again, switch_key, switch_click, switch_back, close_reader, thread_switch, ctx_menu, in dark and light, Bento and Nordic;
  - motion: hover sweep, click mail, arrow repeat ×20, context menu, dialog, app switch, and the PIM dialogs.
- **The bar for every milestone gate:**
  - **0 FLASH and 0 BLANK frames** in every flicker scenario;
  - **input to first changed frame ≤ 1 frame** (median ≤ 16 ms at 60 Hz). Rust measured 7–10 ms with an unthrottled hidden window. The C# numbers are normalised to frames, because WinUI is vsync-paced.
  - **No frame gap over 25 ms inside an animation;**
  - **nothing animates over 200 ms;**
  - **0 frames while idle;**
  - reduce motion settles within one frame.
- **Cross-check on Windows.** PresentMon/ETW for dropped presents, on the bigger scenarios.
- **Linux.** The same scenarios on Xvfb in CI (relative numbers only), plus a hand check on real hardware at M9.
- **Results** are committed under `spec/feel/M<n>/`. A regression blocks the gate unless the user accepts it explicitly.

### 4.6 Live-server safety
- **mail@example.com (the user's main account) is read-only.** Automation and live suites against it run with `JmapClient.ReadOnly = true`. Any method outside `/get`, `/query`, `/changes`, `/queryChanges`, `/parse`, `/validate`, `Blob` download and `SearchSnippet/get` returns `ReadOnlyViolation`. Mark-as-read is suppressed, and probes use folders where every message is already read (as Rust used Sent).
- **Writes only on mail@example.org.**
  - Write-capable test commands (`rx-import`, `rx-find`, `rx-destroy` and the like) exist only in builds with `AUTOMATION_WRITES`.
  - They check at runtime that the session user is exactly the allow-listed test account.
  - Every write test cleans up after itself. Known leftovers from the Rust era are listed in PARITY-ROUND2.md ("p2cal test series" event, "MXTEST …" messages) and are removed once in M2.
- **Never read `target/release/api.txt`**, and never use the admin API key yourself. The user enters it in Settings. Admin is tested read-only.
- **No OS changes without the user's OK:** registry associations, autostart, installing WSL or Windows Sandbox, certificates. Code paths are unit-tested; real registration is a hand test.
- **Comparing with Bulwark.** Use the local source, plus the live Bulwark instance read-only when a network trace is needed. `trace-diff` compares method sequences, never payloads with personal data.

### 4.7 Accessibility
- **Windows:** full UI Automation.
  - Every control has a name and an AutomationId.
  - Lists expose selection.
  - The composer exposes a TextPattern (§3.8).
  - Toasts use `RaiseNotificationEvent`.
  - F6 cycles panes.
  - High contrast and the more-contrast tokens are supported, text scale follows the system and the app setting, and reduce motion is honoured.
  - Axe.Windows scans run in CI; a Narrator pass is a hand test at M2 and M10.
- **Linux:** Uno Skia has no AT-SPI support yet (Uno lists it as "planned"). This is documented as a known limitation and re-checked each Uno release.

### 4.8 Performance budgets (checked at the gates)

| Budget | Target |
|---|---|
| Cold start to interactive Inbox | ≤ 800 ms with the persistent cache, ≤ 1.5 s without (release, warm disk) |
| Message switch, cached | Body and header together in ≤ 50 ms, no blank frame |
| Message switch, uncached | Old message kept (dimmed after 150 ms) until the new one is ready; nothing blank |
| List scroll and arrow repeat | No frame over 25 ms; repeat selection snaps (`Motion.Rapid`) |
| Push to visible update | ≤ 1 s after the StateChange, delta only, no list reset |
| Memory | App process ≤ 300 MB with a 50k-message mailbox (WebView processes measured separately) |
| Idle | 0 rendered frames, about 0 % CPU; push is the only network activity |

Publish: ReadyToRun plus trimming first. Native AOT (supported by Uno 6.6 and Windows App SDK) is a stretch goal at M10, if it measurably cuts cold start.

---

## 5. Milestones

Sizes are relative:
- **S**: one or two parallel streams for one session;
- **M**: three or four streams for one or two sessions;
- **L**: four or five streams for two to four sessions.

For calibration, the Rust app went from first commit to parity round 2 in about 20 hours of parallel agent work, without a spec. The port has a spec, which removes discovery work but adds the gates.

**[HAND TEST]** marks points where the user tests by hand. Agents never do those steps.

### M0: Freeze, foundation and spikes (M)
- **Scope:**
  - **Freeze:** the §2.6 actions.
  - **New repo:** layout (§3.2), strict props, CPM, analyzers, BannedSymbols, `tools/build.ps1` (§6.3), CLAUDE.md with the standing rules, CI skeleton green on Windows and Linux with an empty app.
  - **Catalogue:** the whole `spec/` folder (§2.3). `spec-check` lists the 353 T- rows as pending, by milestone.
  - **Tokens:** `gen-tokens` and the equality test.
  - **Spikes**, each time-boxed, with results in `docs/adr/`:
    - **S1** Uno single project: prototype XAML on both TFMs, WinAppSDK version override, self-contained publish, `-f` inner-loop times.
    - **S2** Body shell on WinUI: srcdoc frames, ready/reveal, MenuFlyout and dialog over WebView2 with zero airspace, measured with a WGC burst. On Linux CI: `ExecuteScriptAsync`, `postMessage`, overlay behaviour, XGetImage capture.
    - **S3** Editor engine: RichTextKit on SKXamlCanvas/SKSwapChainPanel and SKCanvasElement against TextBlock side by side; the hidden-TextBox IME path; caret geometry.
    - **S4** Hidden automation: an off-screen, non-activating window; the command server; pattern-based click; WGC capture including the WebView; frame probe output that `motion.py` reads.
    - **S5** Secrets: read (not write) the Rust app's Credential Manager entry names to confirm the format, without printing values.
- **Exit:**
  - CI green on both OSes; spec catalogue complete and reviewed (Opus sample).
  - S1–S5 decided, with fallbacks chosen where needed.
  - `rust-final` tagged; all branches and the prototype pushed.
- **Closes:** the knowledge-preservation part of §2.5 for every artefact.
- **Risks:** Uno.Sdk and WinAppSDK version mismatch (fallback: option B); RichTextKit on SkiaSharp 3 (fallback: vendor it).

### M1: Read-only mail client on the test account (L)
- **Scope:**
  - **JMAP core (§3.4):** session, transport, limits, errors, retries, gates; Mailbox, Email, Thread, Identity, Quota get/query/changes; SSE push; mailbox and list delta sync; Stalwart state codec.
  - **Accounts:** sign-in (password + TOTP, and OAuth with discovery, dynamic registration and loopback); several accounts and switching; dev profile and import (§3.5).
  - **Shell:** rail, sidebar (folders, tags, counts, storage meter), Bento tiles (Today, Flagged, To do from glance.rs).
  - **Message list:** threads count, date grouping, previews, paging with anchor, unread-first Inbox section.
  - **Reader:** body shell (§3.6), conversation strip, attachment cards, authentication results, dark mail, accessibility CSS.
  - **Behaviour:**
    - msgload no-flicker rules: keep until ready, 150 ms dim, header from the row;
    - prefetch in the direction of travel;
    - body cache;
    - j/k and arrow navigation, with `Motion.Rapid`;
    - basic search.
  - **Design and settings:** Bento and Nordic, dark and light, more-contrast; settings read from prefs.json (no settings UI yet).
  - **Tooling:** automation Layer A; feel probes; a portable zip build artifact on each `main` push for dogfooding.
- **Exit:**
  - Browse every folder of both accounts. Push updates arrive as deltas, with `trace-diff` matching Rust's call sequence for open, switch and push.
  - Flicker and motion scenarios (open_first, switch_key/click/back, thread_switch, close_reader) meet §4.5.
  - Cold-start budget met.
- **Spec closed:**
  - 76 T- rows: jmap, listsync, bodycache, msgload, mailview, main, mail, auth, dark_mail, oauth, settings, motion, tags, attachments, a11y; lite.rs is marked dropped.
  - J- sections 1–4 and the read part of 5.
  - I- rows: rail and account switching, sidebar display, list display and keyboard, reader display.
- **[HAND TEST 1]** Sign in to the dev profile by hand on both accounts.
  - On example, browse read-only: switch messages fast, use key repeat, change theme and style, resize.
  - Judge the feel against the Rust app.
  - This is the user's first look at real data.
- **Risks:** WebView2 shell timing on WinUI (S2 de-risks it); list virtualization performance with complex rows (measure; use ItemsRepeater if ListView costs too much).

### M2: Mail actions, menus and reader depth (L)
- **Scope:**
  - **Message actions:** read/unread, flag, tags (tri-state), move (filter, recent folders, choose dialog), archive by role, junk/not junk, delete/restore/delete forever, empty folder.
  - **Feedback and selection:** undo toasts; multi-select (Shift-range, Ctrl, Shift+arrow, select all in folder); selection bar; hover actions overlay.
  - **Drag and drop and panes:** mail to folders and tags, folders reorder and reparent; resizable panes (panes.json).
  - **Menus:** context menus for mail, folders, tags and account (spec/menus.md); the account menu in full (sign out one or all, set default, manage, unread badges).
  - **Reader banners and actions:** unsubscribe (RFC 2369/8058), read receipts (MDN with MimeKit), invitation banner (display and trust; RSVP in M4), view source, forward as attachment, .eml download, download all (zip), save to Files, quick rules, verification-code chips, avatars (BIMI, Gravatar, favicon; off by default).
  - **Search:** syntax chips, suggestions, history, SearchSnippet highlights.
  - **Inbox and import:** unified inbox; .eml/.mbox import (`Email/import`) and opening an attached .eml (`Email/parse`); TNEF (better than Rust).
  - **Palette and shortcuts:** command palette (Ctrl+K), search everything, `?` sheet, all mail shortcuts.
  - **Plugins:** host (C ABI) and built-in spam score.
- **Exit:**
  - Mail I- rows (list, menus, reader, sidebar, palette) are D or explicitly deferred.
  - Rust leftovers on example-org are removed.
  - Feel scenarios include ctx_menu and dialog.
- **Spec closed:** 89 T- rows (reader, quick_rules, mdn, gsearch, search_history, palette, shortcuts, dnd, unified, mailimport, zip, avatars, otp, plugins); L- rows for the reader (one-click POST, Always-send receipts, Block sender, big pages).
- **[HAND TEST 2]**
  - On example-org: drag mail to a folder and a tag; delete and undo; mark junk; run a quick rule; one-click unsubscribe on a test newsletter.
  - On example, read-only: menus, palette, search.
  - First Narrator pass.

### M3: Compose and send, with the native editor (L)
- **Scope:**
  - **Composer layout:** inline in place of the reader, as Bulwark does; folders hidden while composing; assistant panel slot.
  - **Recipients:** chips with autocomplete (contacts, recent), group expansion, IDN, invalid marking; external-recipient confirm (better than Rust, GAPS P1-12).
  - **Identities:** From with identities and From override (catch-all, `forbiddenFrom` retry); Cc/Bcc.
  - **Attachments:** upload gates, inline `cid:` images, drops and paste.
  - **Drafts and replies:** drafts and **autosave** (2 s debounce, only after a real edit, quit waits for an in-flight save); reply, reply-all and forward with quoting and `cid:` resend.
  - **Sending:**
    - **send with undo** via a single `HOLDFOR` (FUTURERELEASE, `maxDelayedSend` clamped to 7 days), with `undoStatus: canceled` restoring the message to Drafts;
    - **schedule send** (presets, the Scheduled view with Reschedule / Cancel and edit / Cancel send, Mod+Shift+Enter).
  - **Templates and signatures:** templates shared with Bulwark (metadata with `ifInState`); signatures (text and HTML).
  - **mailto:** RFC 6068 parsing; single instance (§3.9); default-app registration code.
  - **Editor (§3.8), in full:**
    - model, commands, undo, HTML in and out, input rules;
    - TipTap shortcuts, clipboard (HTML, images, files);
    - tables, images with resize handles, link popover and bubble, floating toolbar;
    - source view, spell check, IME, right-click menu, AI rewrite hook;
    - rich signatures and templates.
- **Streams:** pipeline (Sonnet), editor model and HTML (Opus), editor view, layout and IME (Opus), recipients and identities (Sonnet), mailto and instance (Sonnet).
- **Exit:**
  - A send round trip on example-org: the HTML export matches Bulwark's spacing, and the message renders right in Bulwark and in Barbican's reader.
  - No stray drafts after Esc or Discard on a cold start.
  - The editor's text matches XAML text (S3 gate held).
- **Spec closed:** 66 T- rows (compose, chips, recipients, autosave, schedule, mailto, instance, templates, rte model/editor/html/spell); L- rows for schedule, autosave and From override (PARITY-ROUND2 hand list items 2–4).
- **[HAND TEST 3]** On example-org:
  - send to yourself with the undo window; schedule, reschedule, then cancel and edit;
  - reply with an inline image;
  - **type with a real IME** (only the user can do this);
  - spell-check suggestions;
  - a mailto link from the browser (the default-app registration needs the user's OK).

From this point the user can do daily mail in the C# app.

### M4: Calendar and tasks (L)
- **Scope:**
  - **Views:** Calendar and CalendarEvent with paging and chunking; Day, Week, Month, Agenda and Tasks; mini month.
  - **Editing:**
    - quick create, drag to move, resize and create (with rollback, and scheduling messages when there are attendees);
    - event editor depth (time zone, attendees, alerts, free/busy, custom recurrence, scope dialog for this / following / all);
    - tasks (JSCalendar Task) with editor and filters.
  - **Recurrence and reminders:**
    - client-side recurrence on the event's own wall clock (recurrence.rs on NodaTime, with Ical.Net as the differential oracle);
    - reminders firing across accounts, 8+ days ahead (also fixing the GAPS gap for an event's own alerts).
  - **Invitations:** RSVP from mail (owner account); counter-proposal apply and "remove cancelled event" (GAPS P0-1).
  - **Import and subscriptions:** .ics import with an alerts prompt; webcal with LAN and localhost blocked, and plain http warned (GAPS P0-4); birthdays calendar.
  - **Tools:** find a time (if `Principal/getAvailability` exists); calendar print (GAPS P1-1).
  - **Menus and shortcuts:** calendar menus; shortcuts d/w/m/t and the rest.
  - **Fixes:** the all-day strip height cap.
  - **Live updates:** calendar push (§3.4).
- **Spec closed:** 45 T- rows (calendar, cal_editor, cal_extra, recurrence, reminders, invite); the 49 calendar I- rows.
- **[HAND TEST 4]**
  - On example, read-only: Agenda and Tasks views open with many tasks (the PARITY-ROUND2 crash scenario).
  - On example-org: create a series, edit "this and following", accept an invitation, see a reminder fire.

### M5: Contacts and Files (M)
- **Scope:**
  - **Contacts:**
    - JSContact list, detail and editor with typed entries and photos (size caps);
    - groups and categories (the whole `keywords` map on edit);
    - address books (with the "existing book" check);
    - vCard import and export (FolkerKinzel plus the ported mapping);
    - duplicates and merge; directory;
    - print (GAPS P1-2);
    - **a security review of untrusted vCard and photo input** (open since round 2).
  - **Files:**
    - FileNode 0.16 and draft-18 shapes;
    - tree, favourites, recent, shared with me;
    - upload (files and folders), download, rename, move, cut/copy/paste, duplicate, star, share;
    - Bulwark trash (.Trash with dated batches);
    - **better than Rust (GAPS P1-3):** in-app preview, details panel, grid and list views, sort, search, thumbnails, upload conflict dialog, zip download, new text file.
  - **Menus and shortcuts** for both apps, plus multi-select with bulk actions.
- **Spec closed:** 14 T- rows (contacts_x, contact_merge, vcard, files); the contacts and files I- rows.
- **[HAND TEST 5]** On example-org: import a .vcf, merge duplicates, add a photo to a test contact; upload a folder, trash it, restore it.

### M6: Settings, server settings, assistant (M)
- **Scope:**
  - **Settings pages:**
    - schema-driven pages for all 201 keys, with keyless rows kept (each gets a handler or an info provider);
    - the unwired keys of GAPS P2-1 wired;
    - backup export/import/reset (accepts Bulwark exports);
    - Appearance (style, theme, more-contrast, text scale) and Accessibility.
  - **Server-backed settings:**
    - Out of office (VacationResponse plus the vacation extras of GAPS P1-5);
    - filters with the Bulwark-compatible Sieve generator and parser (`@metadata`, hand-written blocks kept, spam guard, `reject` only when the server advertises it), the rule editor, and run on existing mail (`ifInState`);
    - folders (subscribe, roles, icons, count modes, pin, mute, mark tree read: GAPS P1-9);
    - identities (create, delete, Bcc, HTML signature, order: P1-6).
  - **Templates manager:** edit, rich body, placeholder fill (P1-8).
  - **Account security pages** (`x:AccountPassword`, `x:AccountSettings` TOTP, `x:AppPassword`, `x:ApiKey`: P1-7). These are coded against Stalwart's source; live writes only on example-org, with the user present.
  - **AI assistant** (Microsoft.Extensions.AI) with the Rust tool set and per-account history.
- **Spec closed:** 18 T- rows (sieve, filters_x); the settings I- rows; S- keys at 100 % read-by.
- **[HAND TEST 6]**
  - Make a filter in Barbican and confirm Bulwark's Sieve view reads it (the round trip Rust verified).
  - Set out-of-office on example-org, then turn it off.
  - Change the style and theme and restart: the settings persist.

### M7: Admin (M)
- **Scope:**
  - Admin API key entry (by the user), the dashboard with WebUI-identical charts and tooltips, and the read pages (§3.11).
  - Then create/edit flows for accounts, groups, lists, domains (DKIM/DNS), API keys, OAuth clients, the settings editor, spam training and queue retry.
  - Write paths are tested against FakeJmap only.
- **Spec closed:** 7 T- rows (admin, dashboard); the admin I- rows.
- **[HAND TEST 7]** The user enters the key; compare the dashboard with Stalwart's WebUI side by side. Read-only.

### M8: Windows desktop integration and distribution (M)
- **Scope:**
  - **Desktop integration:**
    - tray (badge, menu, close to tray, start minimised, start at login);
    - notifications (grouped, Archive / Mark read actions, click opens the message; calendar reminders);
    - taskbar overlay;
    - default mail app (register, unregister, repair).
  - **Release:** the Velopack updater with the signed feed; CI release.yml; Authenticode hook.
  - **Profile switch:** release builds use the real `%APPDATA%\Barbican` profile and its credentials (§3.5); the dev profile is retired.
  - **First real release:** a beta tag, run end to end.
- **Spec closed:** 35 T- rows (desktop, notify, updater, release_manifest, platform mod and win, icon); the desktop-host I- rows; the distribution L- rows (the Rust workflows never ran).
- **[HAND TEST 8]** Install from Setup.exe; get an update from the beta channel; use the tray menu; act on a notification; set start at login and default mail app (OS changes, the user's OK).

### M9: Linux (M)
Linux compiles and runs Core tests from M0 on, and the desktop head is smoke-tested in CI from M1 on. M9 completes it:
- **Platform services:** Secret Service, notifications, SNI tray, portal theme, autostart, Hunspell, mailto `.desktop` registration, single-instance socket.
- **Body view:** the WebKitGTK overlay strategy (§3.6).
- **Packages:** AppImage (with updates) and .deb (notify only).
- **CI:** UI tests on Xvfb; feel scenarios on Xvfb.
- **Spec closed:** 3 T- rows (hunspell); the Linux L- rows (GAPS P0-5, the PARITY-ROUND2 distribution items).
- **[HAND TEST 9]** On a real Linux desktop (the user's machine or VM; GNOME needs the AppIndicator extension), run the AppImage and the .deb. Check:
  - the mail body, menus over it, and dark mode;
  - tray, notifications, the keyring and updates.

### M10: Parity sweep, polish, 1.0 (M)
- **Parity:** every I- row is D or N, with evidence; every G- row is done or explicitly declined by the user.
- **Remaining Bulwark extras by priority:**
  - pop-out windows (P1-13);
  - safety in the reader (P1-12: link mismatch, phishing report, external sender chip, BIMI badge, per-sender trust);
  - themes (P2-2), printing (P2-5), composer extras (P2-6);
  - app lock (PIN or Windows Hello), offline outbox;
  - i18n scaffolding (P2-3).
- **Polish:** a font and size pass side by side with Bulwark per view, through hidden automation; performance budgets; Narrator pass; Native AOT trial.
- **Release:** release 1.0, then archive the Rust repo (if not already done in M1) and the prototype folder.
- **[HAND TEST 10]** Release candidate on both OSes, for daily use for a few days before 1.0.

### Spec totals per milestone (Rust tests)
M1 76 · M2 89 · M3 66 · M4 45 · M5 14 · M6 18 · M7 7 · M8 35 · M9 3 = **353**. Appendix B has the per-module list.

---

## 6. Agent and workflow plan

### 6.1 Shape of every milestone
1. **Plan (Opus, one agent).**
   - Split the milestone into 3–5 streams with **file ownership**: no two streams edit the same file, since one view and one DI file per feature makes this possible.
   - Write each stream's brief: the spec IDs to close, the Rust files and Bulwark refs to read, the Stalwart paths to check, and the exit tests.
2. **Spec tests first (Sonnet, or Haiku for straight transcription).** Port the milestone's T- rows into red xUnit tests.
3. **Streams.**
   - One git worktree each: `C:/Users/user/Downloads/wt-m<n>-<stream>`, branch `m<n>/<stream>`, pushed right away and after every commit.
   - Each stream implements until its tests are green, then runs its hidden-automation scenario on its own port and profile.
4. **Review (Sonnet, fresh agent).** The reviewer gets the diff, the spec rows and the Bulwark refs, never the implementer's summary. It checks correctness, the spec, the rules (§4.1, §4.6), and the motion rules. Opus reviews instead for JMAP, sync, body view, editor, recurrence, OAuth and updater diffs.
5. **Integrate (Sonnet).** Merge the streams into `m<n>` in a planned order, building and testing after each merge, as parity round 2 did. Opus takes over only for conflicts in hard areas.
6. **Verify (Haiku runs, Sonnet reads).** Run `spec-check`, the feel probes, the screenshot set and trace-diff. Sonnet interprets and files fixes.
7. **Gate.** The milestone report has the §2.5 checklist and the hand-test list for the user. After the user's OK, merge to `main`, tag `m<n>`, push.

### 6.2 Model per stage

| Work | Model |
|---|---|
| Catalogue extraction (module headers, tests, commit harvesting), CSV conversions, doc moves, running scripts and probes, collecting screenshots, spec-check runs | **Haiku** |
| Porting pure modules with their tests (otp, avatars, mailto, schedule, search history, tags, zip, quick rules, vcard mapping, sieve, filters, files, contacts, settings pages), XAML views from mockups, menus and shortcuts tables, reviews of routine diffs, merges, interpreting probe output | **Sonnet** |
| Architecture and spikes (M0), JMAP transport, session, sync and push, the body view and msgload orchestration, the editor (model, layout, IME, accessibility), recurrence and time zones, OAuth, updater security, hard merge conflicts, live debugging, catalogue sample review, milestone plans | **Opus** |

### 6.3 Concurrency limits (the machine froze under parallel builds)
The machine has 28 logical cores and 32 GB of RAM. Today a global rustc cap of 3 (`~/.cargo/rustc-limit.py`) keeps it usable. The dotnet equivalent:
- **`Directory.Build.rsp`** at the repo root: `-maxCpuCount:4 -nodeReuse:false`. MSBuild reads it for every `dotnet build`/`test` in the repo and in every worktree, so no MSBuild node processes linger.
- **`tools/build.ps1` / `build.sh`** is the only way agents build or test:
  - it takes one of **N machine-wide slots** (the named semaphore `Global\BarbicanDotnetBuild`, `BARBICAN_BUILD_SLOTS`, default **2**);
  - it runs `dotnet` at **BelowNormal** priority, with `-f` for one TFM in the inner loop (`-Head win|desktop`) and both TFMs only in CI;
  - it releases the slot on exit, even on a crash.

  Direct `dotnet build` is forbidden in agent briefs.
- **Tests.** `xunit.runner.json` sets `maxParallelThreads: 4`; UI and automation tests run one app instance per agent.
- **Agents:**
  - at most **3 agents that compile** at once;
  - at most **5 agents alive** in total, with the rest read-only (catalogue, review);
  - at most **1 Opus implementation stream** at a time unless the user says otherwise.
- **Cleanup:**
  - `dotnet build-server shutdown` when an agent finishes;
  - worktrees pruned after merge (each holds about 1–2 GB of `obj`/`bin` with two TFMs);
  - the rustc cap stays for the rare reference run of the Rust app.

### 6.4 Rules repeated in every agent brief (from CLAUDE.md)
- Push after every commit, on every branch.
- Never use real input. Hidden automation only, with your own port and profile.
- example is read-only; writes only on example-org, with cleanup.
- Never read `target/release/api.txt`.
- No OS changes.
- Build only through `tools/build.ps1`.
- The Rust repo is read-only.
- Cite the Bulwark file:line and Stalwart path for every JMAP behaviour.
- Port the spec tests first.
- Update `spec/` status in the same commit as the code.

---

## 7. Risks and open questions

### Risks (with mitigation)
1. **Uno Linux WebView.** WebKitGTK sits in a separate window, so there is an airspace problem and `WebResourceRequested` is missing.
   - The shell needs only `ExecuteScriptAsync` and web messages; `cid:` parts become `data:` URIs; CSP does the blocking.
   - Overlays use the capture trick from Rust.
   - S2 de-risks this in M0. The fallback is plain-text mode on Linux.
2. **Native editor.** The text engine (RichTextKit maintenance and SkiaSharp 3), the IME and the TextPattern work.
   - S3 de-risks it in M0; the package can be vendored; Win2D is the Windows backend if fidelity fails.
   - The pipeline does not depend on the editor view.
3. **Uno.Sdk and Windows App SDK.** Uno may pin an older WinAppSDK than the prototype's 2.6.5. Override it, or fall back to option B (§3.1).
4. **Port drift** (subtle behaviour lost). Covered by the spec-first tests, catalogue status gates, trace-diff against the Rust app and Bulwark, and side-by-side feel baselines.
5. **WinUI list performance** with rich rows. Use compiled bindings, ListSync, no default transitions and ItemsRepeater if needed. The probes measure it from M1.
6. **Accessibility on Linux** is missing in Uno for now. Accept it and document it; Windows accessibility is complete.
7. **Private-repo CI minutes and update hosting.** Keep CI lean; a public mirror is needed (Q6).
8. **Live-server safety.** Covered by the read-only client mode, write builds that check the account, and cleanup.

### Open questions for the user (each with a recommended default)
1. **Repository name.** Default: rename the Rust repo to `Kaan88/barbican-rust` (tag `rust-final`, archive it after M1), and create the C# app as a new private `Kaan88/barbican`.
2. **Local Linux and real-input testing.** Default: no OS changes, so Linux and real-input tests run only in CI (Xvfb, GitHub VMs), and M9 is checked by hand on a Linux machine of yours. Alternative: enable WSL2 and/or Windows Sandbox locally for faster loops.
3. **Windows package type.** Default: Velopack `Setup.exe` (per-user) plus a portable zip; no MSI unless you need managed deployment.
4. **Mail on disk.** Default: yes. A persistent SQLite cache of the mailbox tree, lists, headers and a bounded set of bodies in `%LOCALAPPDATA%`, for instant cold start, with a "Keep mail on this device" switch. It is off for the read-only example automation profile.
5. **Code signing.** Default: unsigned betas, then a certificate (or Azure Trusted Signing) before 1.0, to avoid SmartScreen warnings.
6. **Update hosting while the repo is private.** Default: a small public `barbican-releases` repository (or an R2 bucket) as the mirror for manifests and packages.
7. **Transition.** Default: you keep using the Rust app until M3 lands. C# dev builds use a separate profile and separate sign-ins, because shared OAuth tokens would rotate each other out; M8 moves the real profile over.

---

## Appendix A: Rust module to C# home

| Rust (lines / tests) | C# home | Library help | M |
|---|---|---|---|
| jmap.rs (1910/18) | Core/Jmap: Transport, Session, Requests, Errors, Methods/*, Push, Sync, StalwartState | STJ source-gen | 1 |
| mail.rs (1167/3), glance.rs (226/0) | Core/Mail/MailService, Presentation/Mail/* | | 1 |
| msgload.rs (405/2), mailview.rs (530/1), bodycache.rs (343/10) | Presentation/Mail/MessageLoader, App/MailBody (+ shell.js verbatim), Core/Mail/BodyCache | | 1 |
| dark_mail.rs (145/3) + email_dark.js | Core/Mail/DarkMail, Assets/mail/email_dark.js | | 1 |
| a11y.rs (314/3), motion.rs (88/1) | Core/Settings/Accessibility, App/Motion | | 1 |
| listsync.rs (295/13) | Presentation/Common/ListSync | | 1 |
| tags.rs (258/5), auth.rs (125/4), attachments.rs (144/3) | Core/Mail/Tags, AuthResults, Presentation/Mail/AttachmentCards | | 1 |
| oauth.rs (465/7), settings.rs (640/1) | Core/Auth/OAuth, Core/Settings (+ schema json) | | 1 (UI 6) |
| main.rs (2145/1) | Split: App bootstrap, Presentation shells, Core services | | 1+ |
| automation.rs (518/0), capture.rs (94/0) | App/Automation (AUTOMATION builds), Platform IWindowCapture | WGC | 0–1 |
| lite.rs (76/1) | Dropped (plain-text mode kept) | | – |
| otp.rs (305/2), avatars.rs (466/7) | Core/Mail/VerificationCode, Core/Mail/Avatars | DnsClient | 2 |
| reader.rs (1387/11), mdn.rs (334/7), quick_rules.rs (348/5) | Core/Mail/Reader*, Mdn, Core/Filters/QuickRules | MimeKit | 2 |
| gsearch.rs (791/13), search_history.rs (130/3), search_ui.rs (212/0) | Core/Search, Presentation/Search | | 2 |
| palette.rs (682/5), shortcuts.rs (848/11), ctxmenu.rs (1273/0) | Presentation/Palette, Input/KeyRouter + Shortcuts, Menus/MenuSpec | | 2 (+4,5) |
| dnd.rs (401/8) | Presentation/DragDrop, App panes | CommunityToolkit Sizers | 2 |
| unified.rs (601/7), mailimport.rs (528/7), zip.rs (146/2) | Core/Mail/Unified, Import, (System.IO.Compression) | MimeKit | 2 |
| plugins.rs (145/1), spam.rs (88/0) | Core/Plugins (NativeLibrary C ABI), built-in spam score | | 2 |
| invite.rs (366/6) | Core/Calendar/Invitations | Ical.Net (parse) | 2/4 |
| compose.rs (1263/7), autosave.rs (251/3), schedule.rs (292/3) | Core/Compose/*, Presentation/Compose | | 3 |
| chips.rs (471/3), recipients.rs (530/8) | Core/Compose/Recipients | MimeKit, IdnMapping | 3 |
| mailto.rs (460/5), instance.rs (189/3), templates.rs (98/1) | Core/Protocol/Mailto, Platform ISingleInstance, Core/Templates | | 3 |
| rte/model, editor, html, mod (3362/29) | Core/Editor | AngleSharp (import) | 3 |
| rte/layout.rs (544/0), rte/spell.rs (226/4) | App/Editor (view, IME), Platform ISpellChecker | RichTextKit, SkiaSharp | 3 |
| calendar.rs (1320/4), cal_editor.rs (1680/8), cal_extra.rs (1089/10) | Core/Calendar/*, Presentation/Calendar | Ical.Net (ics, webcal) | 4 |
| recurrence.rs (550/11), reminders.rs (646/6) | Core/Calendar/Recurrence, Reminders | NodaTime | 4 |
| contacts.rs (540/0), contacts_x.rs (1163/5), contact_merge.rs (280/2), vcard.rs (778/5), pim.rs (29) | Core/Contacts/*, automation probes | FolkerKinzel.VCards | 5 |
| files.rs (812/2) | Core/Files | | 5 |
| sieve.rs (804/9), filters_x.rs (892/9), server_settings.rs (563/0) | Core/Sieve, Core/Filters, Core/ServerSettings | | 6 |
| ai.rs (222/0), agent.rs (573/0) | Core/Assistant | Microsoft.Extensions.AI | 6 |
| admin.rs (607/4), dashboard.rs (717/3) | Core/Admin, Presentation/Admin (chart geometry to XAML Path) | | 7 |
| desktop.rs (480/3), notify.rs (283/5), icon.rs (167/4) | Platform.Windows / Linux (tray, notifications, badge) | H.NotifyIcon, Tmds.DBus, Svg.Skia | 8/9 |
| updater.rs (878/13), release_manifest.rs (169/5) | Core/Updates, tools/Barbican.ReleaseTool | Velopack, BouncyCastle | 8 |
| platform/mod.rs (269/4), win.rs (136/1), hunspell.rs (162/3) | Barbican.Platform.* | CsWin32, WeCantSpell.Hunspell | 8/9 |

## Appendix B: Rust tests per milestone (the spec suite order)
- **M1 (76):** jmap 18, listsync 13, bodycache 10, oauth 7, tags 5, auth 4, mail 3, dark_mail 3, attachments 3, a11y 3, msgload 2, mailview 1, main 1, settings 1, motion 1, lite 1 (marked dropped).
- **M2 (89):** gsearch 13, reader 11, shortcuts 11, dnd 8, mdn 7, unified 7, mailimport 7, avatars 7, quick_rules 5, palette 5, search_history 3, zip 2, otp 2, plugins 1.
- **M3 (66):** rte/html 12, rte/editor 10, recipients 8, compose 7, rte/model 7, mailto 5, rte/spell 4, chips 3, autosave 3, schedule 3, instance 3, templates 1.
- **M4 (45):** recurrence 11, cal_extra 10, cal_editor 8, reminders 6, invite 6, calendar 4.
- **M5 (14):** contacts_x 5, vcard 5, contact_merge 2, files 2.
- **M6 (18):** sieve 9, filters_x 9.
- **M7 (7):** admin 4, dashboard 3.
- **M8 (35):** updater 13, notify 5, release_manifest 5, platform/mod 4, icon 4, desktop 3, platform/win 1.
- **M9 (3):** platform/hunspell 3.

## Appendix C: INTERACTIONS areas per milestone
(The counts are the pre-round-2 table rows, before the re-baseline.)

| Area (rows) | Milestone |
|---|---|
| Rail and account menu (32) | M1 (switching), M2 (menu depth, badges) |
| Sidebar and folder menu (34) | M1 (display), M2 (menus, drop targets), M6 (folder settings) |
| Layout, desktop host, session, auth, lock, offline, tour (52) | M1 (session, auth), M8 (desktop host), M10 (lock, offline, tour) |
| Mail list, rows, selection, keyboard (50); menus and actions (25); reader (40) | M1 and M2 |
| Composer (36) | M3 |
| Mail shell, search, transfer, templates, identities (26) | M2 (search, transfer), M3 (templates), M6 (identities) |
| Calendar and tasks (49) | M4 |
| Contacts (26), Files (24) | M5 |
| Settings, notifications, accessibility, protocol (58) | M6, with notifications and protocol in M8 |
| Command palette, global search, shortcuts help (6) | M2 |
| Admin, connector, plugins, Pro shell (13) | M7, M10 (Pro, plugin UI) |

## Appendix D: references checked for this plan
- Uno WebView2 (Linux: GTK WebKit.WebView in a separate window, packages, `WebResourceRequested` not implemented): https://platform.uno/docs/articles/controls/WebView.html
- Uno Skia desktop (hosts: X11, framebuffer, Win32; `net10.0-desktop`): https://platform.uno/docs/articles/features/using-skia-desktop.html
- Uno 6.4 (.NET 10 support): https://platform.uno/blog/uno-platform-6-4/; Uno 6.6 (native AOT, Linux IME): https://infoq.com/news/2026/08/uno-platform-6-6
- Uno accessibility (Linux Skia "planned"): https://platform.uno/docs/articles/features/accessibility/index.html
- RichEditBox on Uno: https://platform.uno/docs/articles/implemented/microsoft-ui-xaml-controls-richeditbox.html
- Topten.RichTextKit: https://www.nuget.org/packages/Topten.RichTextKit
- Local: Bulwark 1.21.2 (`webmail-main/`), Stalwart `stalwart-0.16.27.zip`, the prototype `barbican-winui` (commits da8dcd1, dd9a05c).
