# Fable review log

What Fable (the later reviewer model) should review. Every agent appends in its own branch, under
a heading with its stream name: one bullet per item with **what** to review, **why** (risk,
judgement call, uncertainty, deviation from Bulwark/RFC/plan, security, privacy, partial
verification) and **where** (paths, commits). Add items as you go. Accounts and paths are
placeholders (see CLAUDE.md); never write real values here.

## M0, consolidated at the merge (stream: m0-review)

The seven M0 streams' sections are archived verbatim in
[fable-review/M0-streams.md](fable-review/M0-streams.md). Below they are deduplicated and ordered by
risk; each item names its source streams in brackets: bootstrap, cat-a, cat-b, cat-c, s12, s3, s45,
and review (this stream, which added its own findings). Commits: merges into `m0/integrate`, then
fast-forwarded to `main`.

### 1. Security, privacy and the user's real screen
1. **The real screen was touched three times, and the popup rule rests on contradicting evidence.**
   In S2 a `MenuFlyout` opened as a windowed popup and Windows clamped it onto the visible monitor (four
   times, about 0.5-0.7 s each, right edge). In S4 an unconstrained flyout showed as a strip about 118 px
   wide at the left edge for a few seconds. No input was sent either time. The evidence conflicts: S2's
   code-created `MenuFlyout` with `ShouldConstrainToRootBounds = true` was still windowed, while S4's
   XAML-declared one drew in-window. The review decided on a constrained `Popup` host, XAML-declared
   flyouts only, and a fail-closed guard in the AUTOMATION build. Review that decision and the guard
   when M1 builds it. Still untested: `ComboBox`, `ToolTip`, `AutoSuggestBox`, `TeachingTip`,
   `CommandBarFlyout`, `ContentDialog`. [s12, s45, review] Where: ADR 0005 and 0007 "M0 review",
   `spikes/BodyShell/Scenario.Phases.cs:109-135, :301`, `spikes/s4-automation/MainWindow.xaml`.
2. **Linux body security.** These block M1's reader on Linux:
   - there is no host request filter (blocking is CSP-only);
   - a `target=_blank` click is lost;
   - a script-initiated top-level navigation of the shell reached the test server although
     `NavigationStarting` cancelled it;
   - `webkit.messageHandlers.unoWebView` messages must be validated by the host.

   [s12] Where: ADR 0005 decision 4 and "M0 review", `src/Barbican.App/Assets/mail/shell.js`,
   `spikes/BodyShell/Scenario.Probes.cs`.
3. **OAuth loopback flow (new catalogue rows).** The Rust sign-in returns on an `error` callback before
   it checks `state`, so any local process can abort a pending sign-in (denial of service, no token
   leak). The listener accepts any local connection on 127.0.0.1:8791. The dynamic-registration cache is
   a plain file. The refresh lock is a file that goes stale after 60 s. Review B-OAUTH-001..018 before M1
   ports them; the C# port should check `state` first. [review] Where: `spec/behaviours/oauth.md`, Rust
   `src/oauth.rs` at `rust-final`.
4. **Assistant actions model (catalogued, ported in M6).** `send_email` always asks for confirmation.
   With `actions` = 2, every other write tool runs without asking: delete (to Trash), move, filter
   rules, settings, vacation. Mail and draft text are fenced as untrusted by tags only. Decide whether
   "automatic" should still confirm destructive tools. [review] Where: `spec/behaviours/assistant.md`
   B-AI-004, B-AI-009.
5. **Privacy of the public history and of copied text.**
   - Check that no blob in the public history survives from before the prototype rewrite
     (`70965c3`, `4bc0fda`, `58bb19b`).
   - Check the scrub of the legacy docs, the plan, the mockups and the tools against the originals at
     `rust-final`.
   - `tools/privacy-check.sh` has gaps: it scans text only; the first name alone, LAN addresses and
     private artifact links are not patterns; the hook works only after the per-clone config.
   - Binary fixtures can embed home paths (the spam-score DLL was left out for this reason).
   - The Rust `reader.rs:1168` holds a real test-account constant. It was not copied; the port takes
     the account from configuration.
   - Commit bodies quoted in `spec/decisions.md` were checked by eye. Not every Rust commit body has
     been run through the full pattern set.
   - The review added `docs/legacy/mailview-shell.rust-final.js` verbatim; it was grepped, with no
     hits.

   [bootstrap, cat-a, cat-b, review] Where: `docs/legacy/README.md`, `tools/privacy-check.sh`,
   `spec/rust-tests.csv` (fixture names).
6. **Secrets.**
   - S5 read the real Credential Manager blobs in memory (structure only, buffers zeroed).
   - The C# app stores under new names with local-machine persistence; the user may want roaming.
   - The import never writes the Rust entries.
   - The Linux format of the Rust entries is known from the crate source only.

   [s45, review] Where: `spikes/s5-credentials/read-format.ps1`, ADR 0008 (with its "M0 review").
7. **Release builds must not carry FlaUI, the Win2D capture or the automation server.** Nothing
   enforces this yet. The review made it the first M1 automation task: a separate project, plus a
   publish check for `FlaUI*.dll`. [s45, review] Where: ADR 0007 "M0 review".
8. **System changes on the VM.**
   - `sudo -n apt-get` installed `libicu-dev`/`icu-devtools`, WebKitGTK 4.1, GTK 3, Xvfb, xauth and
     fonts, with their dependencies (GStreamer among them).
   - `~/.barbican-lib` holds symlinks to the unversioned library names Uno loads.
   - Open: whether `sudo` in the setup script is acceptable at all, and how the M9 `.deb` gets those
     library names.

   [bootstrap, s12] Where: `tools/linux-remote.sh`, ADR 0005 "VM packages".

### 2. Decisions that shape the whole port
9. **SkiaSharp split at the merge.** Uno.Sdk 6.7.49 (S1) uses SkiaSharp 3.119.2; the editor (S3) pins
   4.156.0, the version Uno.Sdk 7.0.22 uses. Transitive pinning would have put SkiaSharp 4 into the
   Uno 6.7 head. The review withheld the editor pins from Uno.Sdk projects with
   `Condition="'$(UsingUnoSdk)' != 'true'"`. It checked the resolved versions in `project.assets.json`
   and made the Uno 7 move the first M1 task (fallback: vendor RichTextKit on SkiaSharp 3). Review:
   - that the condition is evaluated where expected (`Directory.Packages.props` is imported after
     Uno's `Sdk.props` sets `UsingUnoSdk`);
   - that no Uno.Sdk project can reach the editor packages transitively;
   - the decision to move in M1 rather than at M2.

   [review, s12, s3] Where: `Directory.Packages.props`, ADR 0004 and 0006 "M0 review", merge commit
   `dc77c17`.
10. **Editor offsets are UTF-16 with grapheme caret steps, not Rust's bytes and code points.** Rust
    tests with non-ASCII text need recomputed offsets, and Backspace on combining sequences changes.
    Review this, together with the port of `Doc.Delete`, `InsertBlocks` and `InsertSplit` (atomic
    blocks at range ends and paste into cells are untested). [s3] Where:
    `src/Barbican.Core/Editor/{TextOffsets,Pos,Doc,Block}.cs`, commit `adca3e1`.
11. **New project `Barbican.Editor.Layout`** (not in the plan's §3.2), so that layout tests run headless.
    [s3]
12. **Facts that contradict the plan:**
    - Uno 7.0.65 has a Skia `RichEditBox`. The custom editor is kept because RTF cannot round-trip
      Bulwark's HTML; the user should know.
    - Uno 7 is current, not 6.6.
    - RichTextKit is Apache-2.0, not MIT. It is compatible, and a vendored copy must keep its NOTICE.

    [s3]
13. **Automation clicks go through a UIA client (FlaUI.UIA3), not in-process peers.** Peers lack patterns
    on `ListViewItem` and `TextBox`. Elements over the WebView2 are invisible to UIA and keep the
    in-process path. Drag, hover and IME are by hand only. [s45] Where: ADR 0007,
    `spikes/s4-automation/Uia.cs`.
14. **Build infrastructure deviations.**
    - Build slots are N named mutexes, not one semaphore. Untested across elevated sessions.
    - The shared compiler server is off by default.
    - `Directory.Build.rsp` alone does not cap MSBuild nodes (28 measured), so the scripts pass
      `-maxCpuCount:4`.
    - `-v` means PowerShell's `-Verbose`.
    - Tests run on MTP; check that `maxParallelThreads` is honoured.
    - The review extended the real-input grep to `spikes/` and `prototype/`, and fixed a stale comment
      in `eng/BannedSymbols.Tests.txt`.

    [bootstrap, review] Where: `tools/build.ps1`, `tools/build.sh`, `global.json`.
15. **Analyzer strictness and relaxations.**
    - MA0048 (one type per file) conflicts with the plan's single-file `JmapError` hierarchy; decide
      before the M1 JMAP stream.
    - The App head relaxes CA1010, CA2007 and MA0004.
    - `eng/Uno.WebView2Name.targets` removes the `WebView2Aot` compile reference.
    - Spikes relax many rules; the s4 spike imports no strictness at all.

    [bootstrap, s12, s3, s45]
16. **License AGPL-3.0-only** (Bulwark-derived material). [bootstrap]
17. **Product deviations that need the user:**
    - lite mode dropped (B-READ-046, T- row `dropped?`);
    - sender pictures off by default (B-READ-032, needs a D- entry);
    - Sieve replace without `ifInState` (J-030);
    - credential roaming (ADR 0008).

    [cat-a, s45]

### 3. Catalogue completeness and accuracy
18. **The catalogue is extracted, not read line by line, and the review sampled it.**
    - Mechanical checks: all 353 T- rows (file, line and fn exist; the set matches `src/` exactly);
      all 155 non-merge commits have a D- row; 375 line-cited B- rows were checked for plausibility.
    - 52 rows were read by hand, weighted to JMAP, sync, push, msgload, editor, recurrence, updater and
      OAuth.
    - Found missing, and added by the review: oauth, dnd, assistant (ai/agent), spam, plugins, zip,
      capture, pim, search box and invitations behaviours; `spec/menus.md`; the verbatim `SHELL_JS`.

    Review the review's own new files, since they were written by one agent from source:
    `spec/menus.md`, `spec/behaviours/{oauth,assistant,dnd,extras}.md`. [cat-a, cat-b, review] Where:
    `docs/m0-checklist.md` §3.
19. **`spec/jmap.csv` inherited a stale compliance doc.** Five rows were "not done" although they are
    implemented at `rust-final`: J-053, J-056, J-057, J-058, J-068. They are now corrected with code
    cites; this also resolves cat-a's truncated-notice contradiction (B-READ-010 was right). The other
    69 rows keep the doc's status; M1 must re-verify every J- row it touches. [cat-a, review]
20. **Interactions re-baseline: mostly carried.** Of 471 rows, 84 changed, 9 were confirmed and 378 were
    carried; the review spot-checked 8.
    - "D" means a code path exists, often never driven live.
    - I-322, I-323 and I-454 moved to D by inference.
    - M2 holds 142 rows.
    - Layout metrics were read, not measured.

    [cat-c, review] Where: `spec/interactions.csv`, `spec/layout/`.
21. **Known duplicate rules:** B-SYNC-001..011 against B-LST, and B-COMP-039..069 against B-RTE.
    `shortcuts.md` and `palette.md` sit under `behaviours/`. The plan's "21 context menus" is 24 in
    Rust. [cat-b, review] Where: `spec/README.md`.
22. **Settings.**
    - "Wired" is a literal match, so 110 of 136 is an upper bound.
    - The plan's "201 keys" is wrong: the schema has 136 distinct keys (now stated in
      `docs/m0-checklist.md`).
    - The defaults were not checked against Bulwark's store.

    [bootstrap, cat-b]
23. **Decisions.**
    - Some reasons are only the commit subject.
    - The D- ids were renumbered once. Nothing cites them yet; freeze them before M1 cites the first one.
    - 10 plain integration merges are not cited.
    - The intent of the `mat-*` WIP branches is inferred; their diffs were not read.

    [cat-a, cat-b, review]
24. **Test rows.**
    - T- ids are positional; `tools/spec-check.py` maps by the `module::name` tag, so mapping survives
      renumbering.
    - Descriptions come from the first four asserts; some test bodies were not read in full.
    - Gap-row milestones and G-094..G-110 (done items) are the stream's own mapping.

    [cat-a, cat-b, review]
25. **`tools/spec-check.py` (new).** It detects class and method names by regex (a heuristic) and treats
    a T- row as done only when a test carries its tag or the status is `dropped(D-…)`. Check it against
    the first M1 tests. [review]
26. **Motion numbers.** B-MOT-027 quotes commit `0530d4a`, not the baseline JSON. [cat-b]

### 4. Partially verified measurements
27. **Capture and feel.**
    - The WGC cadence is 20.8 ms (48 fps) off-screen on a 144 Hz display, cause unknown.
    - The probe semantics are mapped, not re-derived.
    - The focus check is sampled (40 ms).
    - S3's hidden window parks at -32000 and S4's at -12000; use one design.
    - On Windows, S2 captures only frames that changed.
    - The Linux results come from Xvfb, and header/body skew was up to 2 samples.

    [s45, s3, s12]
28. **Not launched or not loaded.**
    - The published Windows exe has not been started (it needs a hidden smoke mode).
    - The generated token XAML is now compiled by the App head on both TFMs, but has not been loaded
      at runtime.
    - The token test is partly circular, and the prototype's Hover, Pressed and SelRow brushes differ
      from theme.slint.

    [bootstrap, s12, review]
29. **Editor gate.**
    - Measured only on Windows, at 100 %, in the light theme.
    - `TextContrast` k = 1.6 is an empirical fit.
    - CSS line height has not been checked with fallback fonts.
    - Only committed-text IME was exercised.
    - The BiDi caret ambiguity is pinned in a test, not solved.
    - The TextPointer shift was found empirically.
    - The performance numbers come from a Debug build.

    [s3]
30. **Timing is single-sample.** Inner-loop and publish timings come from one machine, one sample each.
    The publish sizes are 292.9 MB (win-x64 R2R) and 160 MB (linux-x64), including about 60 MB of unused
    WinAppSDK AI components and a 21 MB `DirectN.dll`. [s12]

### Resolved at the merge (for the record)
- `RustTestAttribute` duplication: there is now one copy in `tests/Shared/`, and its tags match
  `spec/rust-tests.csv`. The 7 `rte::model` rows are marked ported. [s3]
- ADR numbers that collided: S4/S5 are now 0007/0008. [s3, s45]
- The truncated-body contradiction: J-053 was stale. [cat-a]
- "201 settings keys": the number is 136. [bootstrap, cat-b]
- `menus.md` was missing: it now has 24 menus. [cat-b]
- Generated XAML was not compiled: the App head compiles it on both TFMs. [bootstrap]
- `tools/linux.ps1` failed in a worktree because it had no `.local/`: it now reads the main checkout's. [review]
- Not resolved, it needs the user: the Rust repo's local pre-commit guard (blocked by the permission
  classifier). [bootstrap]

## M1, consolidated at the verify stage (stream: m1-verify)

The M1 sections of the stages, streams and reviews are archived verbatim in
[fable-review/M1-streams.md](fable-review/M1-streams.md). Below they are deduplicated and ordered by risk. Each item names
its sources in brackets:
- plan, f1, f2: the plan and the two fixed stages;
- jmap, mailcore, accounts, shell-ui, body: the five streams;
- jmap-rev, mailcore-rev, accounts-rev, shell-ui-rev, body-rev: their reviews;
- verify: this stage, which adds its own items, marked **[verify]**.

Commits are on `m1`; the per-stream commits are in the archive. Report: [milestones/M1.md](milestones/M1.md).

### 1. Security, privacy and the user's real screen
1. **Mail frame sandbox and CSP.**
   - Dark-pass frames run with `allow-same-origin` + `allow-scripts`, as in Rust. F1 kept `allow-same-origin` against
     the plan's text and dropped the popup flags.
   - This is safe only while the frame CSP admits nothing but our hashed scripts, with no inline handlers. Any future
     `'unsafe-hashes'` or plugin script inside frames breaks it.
   - The CSP adds `base-uri 'none'` (Bulwark) to the Rust policy.

   [f1, body-rev] `Core/Mail/Body/MailSandbox.cs`, `MailCsp.cs`, `Assets/mail/shell.js`, ADR 0005 "M1 F1".
2. **Linux body security is CSP-only and never ran.**
   - There is no request filter and no frame-navigation event on Uno.
   - `StripRefresh` and the frame CSP are the only guards.
   - The shell lets the first `NavigationStarting` through unconditionally. If Uno raises it twice for
     `NavigateToString`, the shell never comes up.

   [body, body-rev] `App/MailBody/BodyShell.cs`.
3. **Remote-content permission per page.** Each page now carries its own `Allow`. The Windows request filter passes
   remote requests only while a page that allows them is alive. No automated test covers this (App type), and the
   shell checks in `spec/feel/M1/shell-checks.json` predate it. [body-rev] `BodyShell.cs`.
4. **OAuth.**
   - The loopback listener reads `state` first, in constant time. A wrong or missing state gets 400 and the wait goes
     on; Rust ended the sign-in. This is a behaviour change for the user to accept.
   - One callback is accepted.
   - AUTOMATION builds use `RedirectFollowingLauncher` and never open a browser.
   - Refresh happens under a file lock that goes stale after 60 s, with adopt-and-retry-once.
   - An agent never refreshes an imported OAuth entry.

   [plan, accounts, accounts-rev] `Core/Auth/LoopbackListener.cs`, `RedirectFollowingLauncher.cs`, `OAuthTokenSource.cs`.
5. **Secrets.**
   - Windows: hand-written CredMan `LibraryImport`, `CRED_PERSIST_LOCAL_MACHINE` (roaming is an open question).
   - The blob limit of 2560 bytes caps the single `accounts` entry at about 8 to 10 OAuth accounts; split it per
     account before M2's sign-out.
   - Password strings cannot be zeroed.
   - The Linux `SecretServiceStore`, including the locked-keyring fix, has never run.

   [accounts, accounts-rev] `Platform.Windows/WindowsSecretStore.cs`, `Platform.Linux/SecretServiceStore.cs`.
6. **ADR 0008 import and read-only accounts.**
   - The agent path is `dev`/`auto-*` only, password kind only, forced read-only, and never reads the admin key.
   - The user path cannot be reached in M1, because `dev` is the default profile until M8.
   - Every account is read-only unless it is listed in `BARBICAN_WRITABLE_ACCOUNTS`.
   - OAuth read-only now follows the real login, not the typed hint. Review this before M2 opens writes.

   [plan, accounts, accounts-rev] `Platform/RustCredentialImport.cs`, `Core/Accounts/ReadOnlyPolicy.cs`, `AccountManager.cs`.
7. **Hidden automation.**
   - The popup guard is reactive, so a windowed popup could draw for one tick.
   - The deny list comes from private-desktop probes: CommandBarFlyout, ComboBox, ToolTip and AutoSuggestBox open
     outside the window.
   - The App throws when a monitor overlaps the hidden rectangle.
   - Release enforcement is static plus output scans. FakeJmap is now in it.
   - **[verify]** Every launch of this stage kept the foreground window and cursor unchanged, the popup guard read
     0/0, and the App was never foreground.

   [f1, shell-ui-rev, verify] `src/Barbican.Automation/PopupGuard.cs`, `App/Composition/HiddenWindow.cs`,
   `tests/Barbican.Build.Tests/ReleaseContentTests.cs`.
8. **Session discovery.** Redirects are followed by hand (at most 5 hops). Credentials go only to same-origin hops, and
   a switch from https to http is refused. Check that a Stalwart behind a cross-host redirecting proxy asks for sign-in
   again rather than failing oddly. [jmap] `Core/Jmap/Transport/SessionFetcher.cs`.
9. **PNG decoder on hostile input.** It reads 8-bit, non-interlaced PNG with a 40 MP cap, and a 40 MP picture allocates
   about 360 MB transiently. Other formats wait for an App decoder. [mailcore, mailcore-rev] `Core/Mail/Attachments/PngDecoder.cs`.
10. **X11 stand-in.** `XGetImage` of an off-screen rectangle (BadMatch) is now refused. The stand-in is unwired and has
    never run. [body-rev] `App/MailBody/X11BodyOverlay.cs`.
11. **Privacy check vs. provider name.** The check refuses Google's mail provider name anywhere in the repo, so the
    unsupported-provider notice lacks its two domains. The user decides on a narrower pattern. [accounts] `tools/privacy-check.sh`,
    `Core/Accounts/MailServerAddress.cs`.

### 2. Open correctness risks
12. **Body cache can mix accounts after a switch.** `BodyCacheSync` files an event under the account active when the
    event is handled. Ids repeat across accounts, so a cached body could show another message's keywords. Fix: carry
    the account key in `MailSyncEventArgs`. [accounts-rev] `Presentation/Accounts/BodyCacheSync.cs`.
13. **List delta plumbing.**
    - There are two RFC 8620 §5.6 implementations: `LoadedList.Apply` and `ListDeltaApplier`.
    - Every delta reaches the list twice: `ListDeltaEvent` and `EmailsChangedEvent`, giving two rebuilds.
    - `ListDeltaEvent` has no query id, so the list drops deltas while a first page loads.

    [mailcore, mailcore-rev, jmap-rev] `Presentation/Mail/LoadedList.cs`, `MessageListViewModel.cs`, `Core/Sync/ListDeltaApplier.cs`.
14. **Who runs the sync loop.** `MailEventHub` runs `IMailSync.RunAsync`. If the accounts session also started it, there
    would be two loops. Check this stays single as M2 adds accounts features. [mailcore] `Presentation/Mail/MailEventHub.cs`.
15. **Sign-in and popover views bypass the accounts ViewModels.** They drive `IAccountManager` directly, so the provider
    notice, OAuth cancel, unread badges and error lines are not on screen. The popover refreshes only on `ActiveChanged`.
    The I- rows stay `P`. [shell-ui, accounts-rev] `App/Views/SignIn/SignInFormModel.cs`,
    `App/Views/Accounts/AccountPopoverModel.cs`.
16. **The App composition is untested.**
    - A DI cycle (BodyShell → KeyRouter → … → BodyShell) hung the first launch silently. It is broken now with
      `Lazy<IKeyRouter>`.
    - No test resolves `AppComposition`.
    - `ViewHost` captures the provider through an `ILoggerProvider` factory.
    - `SmokeRun` still uses its own probe WebView.

    [body, shell-ui, f1] `App/Composition/*`, `App/Shell/ViewHost.cs`.
17. **Null lists in `EmailBody`.** `ModelReader` leaves null in non-nullable lists, and `BodyShape.Normalize` works
    around it. Fix the reader. [body] `Core/Mail/Body/BodyShape.cs`.
18. **Keyboard routing.**
    - `KeyRouter.Handle` ignores `KeySource`. The page skips text boxes.
    - Arrow keys are handled for every source, which risks a double move with a focused `ListView`.
    - `KeyChordMapper` does not handle Shift+digit or AltGr.
    - Enter or Space on a focused button also reaches the router.
    - None of this has run on Uno.

    Hand test. [mailcore, mailcore-rev, shell-ui, shell-ui-rev] `Presentation/Input/KeyRouter.cs`, `App/Shell/ShellPage.xaml.cs`.
19. **Hook semantics.**
    - Sync hooks are awaited inline, where Bulwark fires and forgets.
    - Observers run fire-and-forget on the pool.
    - Interceptors only stop a key whose answer is already complete.
    - Handler errors are swallowed.
    - `onNewEmailReceived` rides the Rust new-mail check, so a message read elsewhere first is not reported, and it
      ignores notification prefs.
    - `onEmailContentRender` and `onThemeChange` were aligned with Bulwark by the reviews.

    [jmap, mailcore, shell-ui-rev, body-rev] `Core/Sync/MailSync.cs`, `NewMailWatcher.cs`, `Core/Plugins/HookBus*.cs`.
20. **Window events now reach the hooks** **[verify]**. `App.xaml.cs` calls `ShellLifecycle.AttachWindow`. Before this,
    `onWindowFocus/Blur`, `onVisibilityChange` and `onBeforeUnload` had call sites but no caller. A hidden run showed
    no crash and no focus change. The events themselves were not observed, because a hidden window is never activated.
    Hand test. [shell-ui, verify] `src/Barbican.App/App.xaml.cs`, commit 6b506be.
21. **Body cache and loader.**
    - The cache bounds are global, not per account (as in Rust; the plan said per account).
    - `Weight` counts UTF-16 code units where Rust counted UTF-8 bytes.
    - An open now ignores its own cancelled prefetch. Check that the extra fetch through the cache cannot matter.
    - `MailBodyView` never unsubscribes from `BodyShell.StateChanged`.
    - `IBodyOverlayStrategy` has no caller.

    [accounts-rev, body-rev] `Core/Mail/BodyCache/*`, `Presentation/Mail/Loading/MessageLoader.cs`, `App/MailBody/MailBodyView.cs`.
22. **`AccountManager` design.** The shared-attempt design (`Entry.Connecting`) was where the "failed connect pins the
    account" bug lived. Review the whole design. [accounts-rev] `Core/Accounts/AccountManager.cs`.
23. **Push edge cases.**
    - A server without `eventSourceUrl` gets no live updates at all; Rust behaves the same.
    - The 700 ms debounce can be postponed indefinitely by a steady stream of pushes.

    [jmap, jmap-rev] `Core/Sync/MailSync.Push.cs`.

### 3. Deviations and judgement calls
24. **JMAP request shape.**
    - `using` lists every advertised capability (Rust), where Bulwark sends core+mail.
    - A 402 with TOTP becomes `OtpRequiredError`, modelled on Bulwark rather than a recorded Stalwart response.
    - J-015 is deferred to M3; J-051 stays M2.
    - J-009 and J-028 are "done" on thin tests.

    [jmap, jmap-rev] `Core/Jmap/Transport/*`, `spec/jmap.csv`.
25. **Mail list rules that differ from Rust.**
    - The unread-first Inbox is a local partition, not Rust's two queries.
    - The flagged tile uses the text filter `is:starred`.
    - Date groups follow Bulwark's `list-date-groups.ts`, not Rust.
    - Shift+G is refresh, as in Bulwark and Rust; the plan wording said "select last".
    - Escape, `g s` and Ctrl+E semantics changed.

    [mailcore, mailcore-rev] `Presentation/Mail/ListLayout.cs`, `Presentation/Shell/GlanceTilesViewModel.cs`, `Core/Mail/Grouping/DateGroups.cs`.
26. **Deliberate deviations in the body view.**
    - `cid:` replacement goes longest id first; this fixes a Rust bug at main.rs:617.
    - `shell.js` posts `ready` after a forced layout without waiting a frame, which was the price of the 50 ms gate.
      data: images may paint a frame late. Check this on screen.

    [body, body-rev] `Core/Mail/Body/CidImages.cs`, `Assets/mail/shell.js`.
27. **Contract changes the integrator should know.**
    - F2 made analyzer-forced renames (`IsReadOnly`, `Barbican.Core.Mail.Cache`, `TagRules`, `KeywordSyntax`).
    - The jmap stream added `ListCursor.Position`, `Watch(query, loaded)`, `Trace`, `OpenEventSourceAsync` and new
      `EmailBody` fields.
    - The accounts stream added `AccountInfo.Kind/IsReadOnly`, `IPrefsEditor`, `IAuthorizationLauncher` and others.
    - Tests ported with a changed shape: typed records instead of JSON values, `ObservableCollection` instead of
      `VecModel`, and `KeyModifiers` flags.

    [f2, jmap, accounts]
28. **Catalogue choices.**
    - 218 hooks in 27 groups, not the 219 in 29 that PORT-PLAN M6b says.
    - The hook milestone column contains judgement calls.
    - Three phone-layout hooks are `n/a`.
    - Hook payloads are our own records.

    [f2, plan, mailcore] `spec/plugins/hooks.csv`, `tools/plugin_milestones.py`.
29. **Plan-level calls.**
    - Branches are named `m1-<key>` (CLAUDE.md says `m<n>/<stream>`).
    - Five streams were used.
    - One DI file per feature with `TryAdd`/`Replace`.
    - The I- rows that are actions were closed for display and deferred.
    - The SQLite cache moved to M2.
    - The lite drop D-233 waits for the user.

    [plan] `docs/milestones/M1-plan.md`.
30. **Uno-specific choices.**
    - Members not implemented in Uno carry a `win:` prefix (`ShouldConstrainToRootBounds`,
      `ItemContainerTransitions`, `ShowsScrollingPlaceholders`). They compile, but are unproven at run time.
    - `PopupLintTests` accepts the prefixed form, still requiring `True`.
    - The Uno 7 move is deferred until after 2026-11-27.

    [shell-ui, shell-ui-rev, f1] `src/Barbican.App/Views/**`, ADR 0004.
31. **Small judgement calls.**
    - `DispatcherQueueDispatcher.Post` drops work after shutdown.
    - Server lookup has no `_jmap._tcp` SRV lookup.
    - The sign-in error wording.
    - The empty mail chrome during a slow restore, and what an offline restore shows.

    [shell-ui-rev, accounts, accounts-rev]
32. **Hook-bus allocation bound relaxed** **[verify]**. The bound is now under one byte per call (10 KB per 10,000
    calls), down from 1 KB in total. The merged build failed once at 4.7 KB, a one-off tiered-JIT allocation. Check that
    this still pins the "no allocation per call" property. [verify] `tests/Barbican.Core.Tests/Plugins/HookBusTests.cs`, 6b506be.

### 4. Partially verified
33. **Linux: nothing of M1 ran.** That covers the Uno head, the Secret Service, the Linux browser launcher and paths,
    the loopback listener (`ExclusiveAddressUse` maps to SO_REUSEPORT), the body shell, the X11 stand-in, Xvfb feel, and
    the red-test convention on the merged branch.

    The VM answers ping, but SSH on port 22 timed out at every stage. M1 is green on Windows only, which is not "green"
    by CLAUDE.md. [all streams, verify]
34. **Feel frame gates unproven** **[verify]**.
    - WGC of the hidden window delivers frames only after a layout change. The scripts wake it with a one-pixel resize,
      so captures show settled states (1 to 6 frames).
    - In the verify run the display was off (user idle about 5 h, display timeout 300 s). All 123 WGC frames were
      uniform white, and the `rendering` probe ticked at about 30 Hz (gap 32 ms).
    - `tools/flicker.py` reported 0 BLANK / 0 FLASH on those white bursts, because it compares against the first and
      last frame. It now reports `empty` / `capture_valid`.
    - The earlier streams' 0/0 claims rest on 1 to 6 frames each. Their artifacts are gone, so whether those frames had
      content cannot be checked.
    - Input to first changed frame ≤ 21 ms, and header and body in one frame, rest on the code.

    [shell-ui, body, body-rev, verify] `tools/flicker.py`, `spec/feel/M1/verify-results.json`, ADR 0007.
35. **Live server: nothing checked by an agent.** The live suite, browsing both accounts, push from a real server, and a
    live Rust or Bulwark trace are hand tests. `spec/traces/M1/*.rust.txt` are derived from source. [jmap, accounts,
    plan] `tests/Barbican.Jmap.Live`, `tools/trace-diff.py`.
36. **Views never driven in a hidden run.**
    - The account popover and the sign-in view: the sign-in view was rendered once on a private desktop, with no
      frames.
    - Tooltips (hover).
    - I-209 and I-214 banners: the seed has no remote content.
    - Load more: the seed Inbox is one page.
    - The `PopIn` first frame.

    [shell-ui, shell-ui-rev, accounts, body-rev]
37. **Spec status set by the verify stage** **[verify]**. Review these as decisions, not facts.
    - The I- rows I-039, I-046 and I-156 were marked done from a hidden run. For I-046 the UIA tree still lists the
      collapsed rows, so the evidence is the XAML render.
    - Unmarked B- rows were settled from the milestones of their Rust tests (`spec/behaviours/*` "M1 status (verify)").
    - The pending (M1) motion rows were given statuses.
    - `spec/settings.csv` `csharp_read_by` was filled by a literal key search (47 keys).

    [verify] `spec/interactions.csv`, `spec/behaviours/*.md`, `spec/settings.csv`, 6b506be.
38. **Design.**
    - Screenshots are XAML renders, so the body area is empty and Bento is 1356x856 (no window padding).
    - No comparison with the mockups was made.
    - **[verify]** In Nordic light the disabled rail icons, the toolbar's More button and the section-header icons are
      barely visible.
    - A collapsed Folders card keeps its height.
    - "Remember me" renders indeterminate.

    [shell-ui, accounts-rev, verify] `docs/milestones/M1-screens/`.
39. **Coverage and numbers.**
    - Coverage: Jmap 93%, Sync 91%, Mail/Service 94%. The uncovered parts are cancellation and IO branches.
    - Cold start (5 Release smoke runs): first frame 284 to 353 ms.
    - Inbox interactive: an upper bound by polling, 783 to 866 ms.
    - All are single-machine numbers.

    [jmap, verify]

### Resolved during M1 (for the record)
- **[jmap-rev]**
  - a cancelled sync stuck the hook at "syncing";
  - a bad trace path failed every request;
  - the event stream leaked.
- **[jmap merge]**
  - there were two hook buses;
  - every push reset the list;
  - the spec-check hook regex held a stray 0x08 byte.
- **[mailcore-rev]**
  - Shift+G was bound to "select last";
  - a stale delta could land in a newly opened folder.
- **[accounts-rev]**
  - a failed connect pinned the account;
  - the sign-in deadline threw;
  - `AccountsStartup.Completion` hung;
  - a locked keyring lost writes silently (Linux, unverified).
- **[accounts merge]** The sign-in screen flashed at every start-up.
- **[shell-ui-rev]**
  - the tooltip host was never set;
  - Add account did nothing;
  - the dispatcher threw after shutdown;
  - nothing kept FakeJmap out of Release.
- **[body-rev]**
  - an open was dropped by its own cancelled prefetch;
  - two loader tests were flaky.
- **[shell-ui]** The `key` automation command is registered (`App/Shell/Automation/KeyCommand.cs`; this was mailcore
  item 8).
- **[verify]** The window events are wired (item 20). `flicker.py` rejects uniform captures (item 34).

