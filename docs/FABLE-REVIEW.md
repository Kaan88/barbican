# Implementation review log (Astra)

Astra will review the implementations at the final whole-app review. This file keeps its
historical name so existing links and earlier Fable review records remain valid.

**Codex continuation (2026-11-19, user instruction):** mark new entries as Codex and name
the stream and worker model. Record what Astra should review, including exact paths and
commits, remaining risks, and the limits of the test evidence. Keep earlier entries and
review outcomes intact. Do not run separate milestone reviews before the whole-app review.

**Coordinator code checks (2026-11-19, user instruction):** Sol checks subagent code
during integration. These checks are part of implementation, not a separate review
stage. Codex entries record the inspected paths and source commits, concrete findings,
fixes and executed checks. Astra still performs the later independent whole-app review;
a stream report or green test count alone is not a coordinator code check.

Every agent appends in its own branch, under
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

**Fable review 2 (2026-11-17, `m1` at `4eccc0f`):** items 1 to 23 below carry their verdict as a link; the new findings
(R2-nn), the decisions and the gate blockers are in [fable-review-2.md](fable-review-2.md). Items 24 to 39 were not looked at again.

The M1 sections of the stages, streams and reviews are archived verbatim in
[fable-review/M1-streams.md](fable-review/M1-streams.md). Below they are deduplicated and ordered by risk. Each item names
its sources in brackets:
- plan, f1, f2: the plan and the two fixed stages;
- jmap, mailcore, accounts, shell-ui, body: the five streams;
- jmap-rev, mailcore-rev, accounts-rev, shell-ui-rev, body-rev: their reviews;
- verify: this stage, which adds its own items, marked **[verify]**.

Commits are on `m1`; the per-stream commits are in the archive. Report: [milestones/M1.md](milestones/M1.md).

### 1. Security, privacy and the user's real screen
1. [Fable 2: fixed, see R2-04](fable-review-2.md#verify) **Mail frame sandbox and CSP.**
   - Dark-pass frames run with `allow-same-origin` + `allow-scripts`, as in Rust. F1 kept `allow-same-origin` against
     the plan's text and dropped the popup flags.
   - This is safe only while the frame CSP admits nothing but our hashed scripts, with no inline handlers. Any future
     `'unsafe-hashes'` or plugin script inside frames breaks it.
   - The CSP adds `base-uri 'none'` (Bulwark) to the Rust policy.

   [f1, body-rev] `Core/Mail/Body/MailSandbox.cs`, `MailCsp.cs`, `Assets/mail/shell.js`, ADR 0005 "M1 F1".
2. [Fable 2: open (never ran)](fable-review-2.md#verify) **Linux body security is CSP-only and never ran.**
   - There is no request filter and no frame-navigation event on Uno.
   - `StripRefresh` and the frame CSP are the only guards.
   - The shell lets the first `NavigationStarting` through unconditionally. If Uno raises it twice for
     `NavigateToString`, the shell never comes up.

   [body, body-rev] `App/MailBody/BodyShell.cs`.
3. [Fable 2: fine, untested](fable-review-2.md#verify) **Remote-content permission per page.** Each page now carries its own `Allow`. The Windows request filter passes
   remote requests only while a page that allows them is alive. No automated test covers this (App type), and the
   shell checks in `spec/feel/M1/shell-checks.json` predate it. [body-rev] `BodyShell.cs`.
4. [Fable 2: fine](fable-review-2.md#verify) **OAuth.**
   - The loopback listener reads `state` first, in constant time. A wrong or missing state gets 400 and the wait goes
     on; Rust ended the sign-in. This is a behaviour change for the user to accept.
   - One callback is accepted.
   - AUTOMATION builds use `RedirectFollowingLauncher` and never open a browser.
   - Refresh happens under a file lock that goes stale after 60 s, with adopt-and-retry-once.
   - An agent never refreshes an imported OAuth entry.

   [plan, accounts, accounts-rev] `Core/Auth/LoopbackListener.cs`, `RedirectFollowingLauncher.cs`, `OAuthTokenSource.cs`.
5. [Fable 2: fixed, see R2-06](fable-review-2.md#verify) **Secrets.**
   - Windows: hand-written CredMan `LibraryImport`, `CRED_PERSIST_LOCAL_MACHINE` (roaming is an open question).
   - The blob limit of 2560 bytes caps the single `accounts` entry at about 8 to 10 OAuth accounts; split it per
     account before M2's sign-out.
   - Password strings cannot be zeroed.
   - The Linux `SecretServiceStore`, including the locked-keyring fix, has never run.

   [accounts, accounts-rev] `Platform.Windows/WindowsSecretStore.cs`, `Platform.Linux/SecretServiceStore.cs`.
6. [Fable 2: fine, see R2-14](fable-review-2.md#verify) **ADR 0008 import and read-only accounts.**
   - The agent path is `dev`/`auto-*` only, password kind only, forced read-only, and never reads the admin key.
   - The user path cannot be reached in M1, because `dev` is the default profile until M8.
   - Every account is read-only unless it is listed in `BARBICAN_WRITABLE_ACCOUNTS`.
   - OAuth read-only now follows the real login, not the typed hint. Review this before M2 opens writes.

   [plan, accounts, accounts-rev] `Platform/RustCredentialImport.cs`, `Core/Accounts/ReadOnlyPolicy.cs`, `AccountManager.cs`.
7. [Fable 2: not reviewed](fable-review-2.md#verify) **Hidden automation.**
   - The popup guard is reactive, so a windowed popup could draw for one tick.
   - The deny list comes from private-desktop probes: CommandBarFlyout, ComboBox, ToolTip and AutoSuggestBox open
     outside the window.
   - The App throws when a monitor overlaps the hidden rectangle.
   - Release enforcement is static plus output scans. FakeJmap is now in it.
   - **[verify]** Every launch of this stage kept the foreground window and cursor unchanged, the popup guard read
     0/0, and the App was never foreground.

   [f1, shell-ui-rev, verify] `src/Barbican.Automation/PopupGuard.cs`, `App/Composition/HiddenWindow.cs`,
   `tests/Barbican.Build.Tests/ReleaseContentTests.cs`.
8. [Fable 2: fixed](fable-review-2.md#verify) **Session discovery.** Redirects are followed by hand (at most 5 hops). Credentials go only to same-origin hops, and
   a switch from https to http is refused. Check that a Stalwart behind a cross-host redirecting proxy asks for sign-in
   again rather than failing oddly. [jmap] `Core/Jmap/Transport/SessionFetcher.cs`.
9. [Fable 2: mostly fixed, see R2-08](fable-review-2.md#verify) **PNG decoder on hostile input.** It reads 8-bit, non-interlaced PNG with a 40 MP cap, and a 40 MP picture allocates
   about 360 MB transiently. Other formats wait for an App decoder. [mailcore, mailcore-rev] `Core/Mail/Attachments/PngDecoder.cs`.
10. [Fable 2: not reviewed](fable-review-2.md#verify) **X11 stand-in.** `XGetImage` of an off-screen rectangle (BadMatch) is now refused. The stand-in is unwired and has
    never run. [body-rev] `App/MailBody/X11BodyOverlay.cs`.
11. [Fable 2: decision](fable-review-2.md#verify) **Privacy check vs. provider name.** The check refuses Google's mail provider name anywhere in the repo, so the
    unsupported-provider notice lacks its two domains. The user decides on a narrower pattern. [accounts] `tools/privacy-check.sh`,
    `Core/Accounts/MailServerAddress.cs`.

### 2. Open correctness risks
12. [Fable 2: fixed](fable-review-2.md#verify) **Body cache can mix accounts after a switch.** `BodyCacheSync` files an event under the account active when the
    event is handled. Ids repeat across accounts, so a cached body could show another message's keywords. Fix: carry
    the account key in `MailSyncEventArgs`. [accounts-rev] `Presentation/Accounts/BodyCacheSync.cs`.
13. [Fable 2: fixed](fable-review-2.md#verify) **List delta plumbing.**
    - There are two RFC 8620 §5.6 implementations: `LoadedList.Apply` and `ListDeltaApplier`.
    - Every delta reaches the list twice: `ListDeltaEvent` and `EmailsChangedEvent`, giving two rebuilds.
    - `ListDeltaEvent` has no query id, so the list drops deltas while a first page loads.

    [mailcore, mailcore-rev, jmap-rev] `Presentation/Mail/LoadedList.cs`, `MessageListViewModel.cs`, `Core/Sync/ListDeltaApplier.cs`.
14. [Fable 2: fine](fable-review-2.md#verify) **Who runs the sync loop.** `MailEventHub` runs `IMailSync.RunAsync`. If the accounts session also started it, there
    would be two loops. Check this stays single as M2 adds accounts features. [mailcore] `Presentation/Mail/MailEventHub.cs`.
15. [Fable 2: open, see R2-03](fable-review-2.md#verify) **Sign-in and popover views bypass the accounts ViewModels.** They drive `IAccountManager` directly, so the provider
    notice, OAuth cancel, unread badges and error lines are not on screen. The popover refreshes only on `ActiveChanged`.
    The I- rows stay `P`. [shell-ui, accounts-rev] `App/Views/SignIn/SignInFormModel.cs`,
    `App/Views/Accounts/AccountPopoverModel.cs`.
16. [Fable 2: partly fixed](fable-review-2.md#verify) **The App composition is untested.**
    - A DI cycle (BodyShell → KeyRouter → … → BodyShell) hung the first launch silently. It is broken now with
      `Lazy<IKeyRouter>`.
    - No test resolves `AppComposition`.
    - `ViewHost` captures the provider through an `ILoggerProvider` factory.
    - `SmokeRun` still uses its own probe WebView.

    [body, shell-ui, f1] `App/Composition/*`, `App/Shell/ViewHost.cs`.
17. [Fable 2: open (low)](fable-review-2.md#verify) **Null lists in `EmailBody`.** `ModelReader` leaves null in non-nullable lists, and `BodyShape.Normalize` works
    around it. Fix the reader. [body] `Core/Mail/Body/BodyShape.cs`.
18. [Fable 2: partly fixed, see R2-05](fable-review-2.md#verify) **Keyboard routing.**
    - `KeyRouter.Handle` ignores `KeySource`. The page skips text boxes.
    - Arrow keys are handled for every source, which risks a double move with a focused `ListView`.
    - `KeyChordMapper` does not handle Shift+digit or AltGr.
    - Enter or Space on a focused button also reaches the router.
    - None of this has run on Uno.

    Hand test. [mailcore, mailcore-rev, shell-ui, shell-ui-rev] `Presentation/Input/KeyRouter.cs`, `App/Shell/ShellPage.xaml.cs`.
19. [Fable 2: partly fixed, see R2-10](fable-review-2.md#verify) **Hook semantics.**
    - Sync hooks are awaited inline, where Bulwark fires and forgets.
    - Observers run fire-and-forget on the pool.
    - Interceptors only stop a key whose answer is already complete.
    - Handler errors are swallowed.
    - `onNewEmailReceived` rides the Rust new-mail check, so a message read elsewhere first is not reported, and it
      ignores notification prefs.
    - `onEmailContentRender` and `onThemeChange` were aligned with Bulwark by the reviews.

    [jmap, mailcore, shell-ui-rev, body-rev] `Core/Sync/MailSync.cs`, `NewMailWatcher.cs`, `Core/Plugins/HookBus*.cs`.
20. [Fable 2: not reviewed](fable-review-2.md#verify) **Window events now reach the hooks** **[verify]**. `App.xaml.cs` calls `ShellLifecycle.AttachWindow`. Before this,
    `onWindowFocus/Blur`, `onVisibilityChange` and `onBeforeUnload` had call sites but no caller. A hidden run showed
    no crash and no focus change. The events themselves were not observed, because a hidden window is never activated.
    Hand test. [shell-ui, verify] `src/Barbican.App/App.xaml.cs`, commit 6b506be.
21. [Fable 2: partly fixed](fable-review-2.md#verify) **Body cache and loader.**
    - The cache bounds are global, not per account (as in Rust; the plan said per account).
    - `Weight` counts UTF-16 code units where Rust counted UTF-8 bytes.
    - An open now ignores its own cancelled prefetch. Check that the extra fetch through the cache cannot matter.
    - `MailBodyView` never unsubscribes from `BodyShell.StateChanged`.
    - `IBodyOverlayStrategy` has no caller.

    [accounts-rev, body-rev] `Core/Mail/BodyCache/*`, `Presentation/Mail/Loading/MessageLoader.cs`, `App/MailBody/MailBodyView.cs`.
22. [Fable 2: confirmed, see R2-01 and R2-02](fable-review-2.md#verify) **`AccountManager` design.** The shared-attempt design (`Entry.Connecting`) was where the "failed connect pins the
    account" bug lived. Review the whole design. [accounts-rev] `Core/Accounts/AccountManager.cs`.
23. [Fable 2: fixed, see R2-01](fable-review-2.md#verify) **Push edge cases.**
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


## M1 fix stream: security (stream: m1-fix-security)

Fixes for Fable review 1 (`docs/fable-review-1.md` on `fable/review-1`) F-02, F-03, F-04, F-05, F-06, F-07, F-08, F-09, F-12,
F-13 and user decisions D-2, D-9, D-10, on branch `m1-fix-security` (from `m1` at `056ae3b`). Items below name what to re-check.

- [Fable 2: fixed, see R2-09](fable-review-2.md#f-verdicts) **F-02 session URLs (judgement calls).** `Core/Jmap/Transport/SessionUrls.cs`, `SessionParser.cs`, `SessionFetcher.cs`,
  `AuthorizedHttp.cs`, `JmapSession.CredentialOrigins`. Relative URLs still resolve against the session URL (RFC 8620 §2), then
  move onto the typed origin; Bulwark resolves them against the typed origin's root instead (`client.ts:2329`): the two differ only
  for a relative URL without a leading slash. A session that arrived from another origin keeps no "hosted elsewhere" host (Bulwark
  has no such case: the browser fetch never crosses). The session re-fetch on a `sessionState` change now goes through discovery
  when the session URL is off the typed origin. A cross-origin 401 now says "Enter <origin> as the server address" instead of
  "Wrong username" (FABLE-REVIEW M1 item 8). Check the wording, and whether a Stalwart behind a proxy that redirects
  `/.well-known/jmap` to another host should be followed with a prompt instead.
- [Fable 2: fixed, see R2-09](fable-review-2.md#f-verdicts) **D-10 plain http.** `Core/Jmap/ServerEndpoint.cs` `Read`: http only for `localhost`, 127.0.0.0/8 and `::1`, else
  `InsecureMessage`. A remembered (or imported) http server elsewhere now fails at start-up with that message. There is no
  "I understand" override (the brief said refuse). `*.localhost` names are not treated as loopback.
- [Fable 2: fixed](fable-review-2.md#f-verdicts) **F-04 OAuth loopback and discovery.** `Core/Auth/LoopbackListener.cs`, `CallbackCheck.cs`, `OAuthDiscovery.cs` (`Secure`,
  `MetaFrom`), `OAuthMetadata.Issuer/IssParameterSupported`, `RefreshLock.cs`. Judgement calls: a wrong `iss` **with** the right
  state ends the sign-in (the state was leaked to a second provider, so waiting on gives nothing); without the state it is refused
  like any stranger. An `iss` sent while the metadata names no issuer is ignored unless the provider announced the parameter.
  A plain-http registration endpoint of an external provider is dropped (fallback client id), not followed. The refresh lock no
  longer deletes its file and has no 60 s staleness: verify on Linux that .NET's `FileShare.None` really takes `flock` there (it
  does in .NET on Unix by default; `DOTNET_SYSTEM_IO_DISABLEFILELOCKING` would turn it off). Not changed: bind address (already
  `IPAddress.Loopback`), constant-time state compare, PKCE verifier (48 random bytes, 64 chars).
- [Fable 2: fixed, see R2-06](fable-review-2.md#f-verdicts) **F-05 one credential per account.** `Platform/SecretAccountVault.cs`: `account/<sha256-prefix>` entries plus `account-index`
  (in the secret store, not a profile file: the index names no address and keeps the vault one unit). Judgement: the `session`
  entry was dropped (nothing in the C# app read it). Migration of the old single entry is silent and automatic on first load.
  The live Windows store was not exercised by a test here (no throwaway-target run in this stream); the 2560-byte cap is
  checked with a capped fake store. Hand test: sign in to three OAuth accounts in a dev profile and restart.
- [Fable 2: fixed](fable-review-2.md#f-verdicts) **F-06 / D-2 import.** `RustCredentialImport`: the user import keeps OAuth accounts listed but drops their refresh token
  ("imported without its sign-in; sign in again"). ADR 0008 point 2's "validate by a normal refresh" is withdrawn in an
  amendment section.
- [Fable 2: fixed, see R2-14](fable-review-2.md#f-verdicts) **F-12 read-only fails closed.** `Core/Accounts/ReadOnlyPolicy.For(profile, env, readFile)`: in `dev` (also the default
  profile, so a plain user run is covered too) and `auto-*`, only the file named by `BARBICAN_WRITABLE_ACCOUNTS_FILE` counts,
  and only when its folder is named `.local`; `BARBICAN_WRITABLE_ACCOUNTS` is ignored there. Other profiles keep the variable.
  Registered from `IProfilePaths` (not `ProfilePaths.FromEnvironment`), so the policy follows the profile the app actually uses.
  Judgement: the allow-list holds logins, not account keys (server+login), matching the existing `IsReadOnly(user, …)`; a
  stricter key match can come with M2's writes. Who sets the variable: `tools/auto-run.ps1` (F-08 commit) points it at the main
  checkout's `.local/writable-accounts.txt` when that file exists; nobody creates the file in this stream.
- [Fable 2: partly fixed, see R2-07](fable-review-2.md#f-verdicts) **F-07 / D-9 authserv-id.** `Core/Mail/AuthResults/AuthServTrust.cs`, `AuthenticationResults.Trusted/AuthServId`; the reader
  (`ReaderSnapshots.FromBody`) gets `ReaderOptions.ServerHost` from the account key in `MessageLoader.Present`. Judgement calls:
  the id is not *learned* from Inbox mail (the review's suggestion); it is matched against the server host or its parent domain
  (`mail.example.org` trusts `example.org` and `*.example.org`, never a bare TLD). Risk: a host directly under a two-label public
  suffix (`example.co.uk`) would trust `*.co.uk`; there is no public suffix list. Learning the id per account (and pinning it) is
  deferred to M2 with the trust banner (G-049). Hand test: open a received message on the live account and check the chip still
  shows (Stalwart's authserv-id must sit in the server's parent domain).
- [Fable 2: mostly fixed, see R2-08](fable-review-2.md#f-verdicts) **F-09 PNG bombs.** `Core/Mail/Attachments/PngDecoder.TryThumbnail` streams rows into `ThumbnailCells` (the cells of
  `ThumbnailScaler.Fill`, byte-identical: `PngThumbnailTests.The_streaming_thumbnail_equals_decode_and_fill` over all colour types
  and filters); `IImageDecoder.TryThumbnail` is a default interface method so platform decoders keep working; `TryDecode` capped
  at 16 MP and itself row-streamed (no raw buffer); `AttachmentCards.Thumbnail` holds a static lock; `OutOfMemoryException` is caught.
  Judgement: the streaming cap is 100 MP (time, not memory: about a second of inflate). Nothing in the app calls
  `AttachmentCards.Thumbnail` yet (cards show kinds only in M1); the lock assumes callers are off the UI thread, which the doc says.
- [Fable 2: fixed](fable-review-2.md#f-verdicts) **F-08 automation token.** `Automation/Protocol/AutomationConnection.cs` (UI-free, tested over a real loopback socket in
  `AutomationConnectionTests`), `AutomationServer`, `AutomationHost` (no token: the port stays closed, the popup guard still
  runs). Clients: `tools/auto-run.ps1` (token per run in `artifacts/auto/<port>/token`, also passes
  `BARBICAN_WRITABLE_ACCOUNTS_FILE` when the main checkout has `.local/writable-accounts.txt`), new `tools/autoconn.py` used by
  `auto.py`, `motion.py`, `scenarios.py`, `layout/m1_scenarios.py`, `layout/m1_shots.py`, and `linux-remote.sh auto`.
  Not changed: `spikes/s4-automation` (the spike server has no token; it is reference code, not built into the app).
  Judgement: lines are read with a bound (`CommandLine.MaxLength`) so a peer cannot grow one line without limit. The hidden
  Automation build was not launched in this commit; the F-03 stage runs it through `auto-run.ps1` and so checks the token path
  end to end. Linux pending (VM rule).
- [Fable 2: fixed on Windows](fable-review-2.md#f-verdicts) **F-13 WebView2 folder.** `App/Composition/WebViewEnvironment.cs` (renamed from `HiddenWebView.cs`) runs for every run:
  `WEBVIEW2_USER_DATA_FOLDER` = `<profile DataDir>\WebView2`, `--dns-prefetch-disable` always, the painting flags only when
  hidden. Found on the way: a hidden run without `BARBICAN_PROFILE` named its WebView2 folder after `auto-<port>` while Core
  resolved the `dev` profile (the user's own data); it now sets `BARBICAN_PROFILE` to the run's profile before composition.
  WebKitGTK data directory: Linux pending (the Linux head reads none of this; Uno's WebView there has no data-dir setting we
  use yet).
- [Fable 2: fixed on Windows, see R2-04](fable-review-2.md#f-verdicts) **F-03 the mail frame.** `Core/Mail/Body/MailSanitizer.cs` (AngleSharp 1.9.1, new Core dependency; runs in
  `MessageFetcher.FetchAsync` off the UI thread before the body is cached, and on plugin HTML in
  `MessageLoader.TransformThenShowAsync`); `MailSandbox.Frame` (never `allow-scripts`); `MailCsp.Policy(remote)` always
  `script-src 'none'` plus `x-dns-prefetch-control: off`; `MailPage(Html, Dark, Background, HasRemoteContent) { Sizing }`;
  `A11y.Sizing()` / `MailSizing`; `ShellDocument` builds one inline script (`window.__bbDarkPass=function(document){email_dark.js}`
  + shell.js) allowed by its SHA-256 only; `shell.js` `finish()` (srcdoc check on both the load and the poll path, then size,
  then dark, then ready); `BodyShell` sends `dark` and `size`, cancels every frame navigation but `about:srcdoc`.
  Probe (hidden Automation build, Windows, loopback beacon): a meta refresh with the shell's sandbox (host asked for
  `allow-scripts allow-top-navigation`, both filtered out), with and without the frame CSP, and in hand-made `allow-scripts`
  frames: 0 beacon hits; the dark pass and the sizing applied from the shell (pending class gone, `data-dark-done` set, 10 px ->
  15 px, white table -> rgb(22,22,22)). Foreground unchanged, no popups. Linux pending (VM rule): whether WebKitGTK lets the
  shell's listeners and the dark pass act on a sandboxed frame without `allow-scripts`, and the meta-refresh probe there.
  Judgement calls: `meta` is removed entirely (the page sets its own charset); `form` is unwrapped (content kept), inputs stay
  inert; `noscript` content is kept (it renders in a script-less frame, as in a mail client); a body is reserialised only when
  something was removed. `A11y.MailScript()` stays for its Rust-ported tests but is no longer put in a page. Not done: the
  S2 spike harness probe (the hidden-app probe above replaces it on Windows); link-opener confirmation (point 5) is M2.
- [Fable 2: open](fable-review-2.md#f-verdicts) **Linux.** `tools/linux.ps1 test` tried once at the end of the stream: the VM job failed (ssh exit 255). Linux pending (VM
  unreachable) for every item above: the full test run, F-03 (frame listeners and dark pass without `allow-scripts` under
  WebKitGTK, the meta-refresh probe, WebKitGTK DNS prefetch), F-04 (`FileShare.None` as `flock`), F-08 (`linux-remote.sh auto`
  with the token), F-13 (WebKitGTK data directory).

## m1-fix-core (Fable review 1: F-10, F-11, F-14, F-15, F-16 and the M1 verify open issues)

Review these, in this order of risk:

- [Fable 2: fixed, see R2-13](fable-review-2.md#f-verdicts) **F-14 sync/list, the three judgement calls.** (1) `Presentation/Mail/LoadedList.cs`: `Apply(ListDelta)` now delegates to
  `Core/Sync/ListDeltaApplier`; `Apply(EmailsChangedEvent)` takes fresh copies only and **ignores `Removed`** (the removals of the
  query that was watched). `MailSync` still emits both events and `BodyCache` still evicts bodies by `Removed`. Check you agree
  that a message moved out of the folder keeping its cached body until the cache's LRU drops it is fine. (2)
  `MessageListViewModel.FirstPageAsync`: the unread-first Inbox is now two queries as Rust `fetch_unread_first` (notKeyword
  `$seen` limited to the section size, then hasKeyword `$seen`), with no sync states, so every push resets and re-reads the list
  (as Rust: "a sync re-fetches it in full"). Paging then uses the `$seen` query (`_pagingQuery`). Unread messages beyond the
  section size are not shown (Rust too). (3) a reset reloads `max(page size, rows loaded)` rows and closes the reader when the
  open message is no longer in the list. `Core/Mail/Service/ListPageFetcher.cs`: "more pages" counts the ids returned;
  `ListPage.DroppedIds` lists rows that failed to parse but **nothing logs them yet** (Core has no logger there): wire it in M2.
  `MailService.GetThreadAsync` sorts by the thread's `emailIds`. `Core/Jmap/Transport/RequestSplitter.cs`: a creation id is now
  looked for only in `/set` calls; a `#` string in a query is data. Check no other method takes a creation id (EmailSubmission
  `onSuccessUpdateEmail` keys are handled by the key rule). The "http" host-name part of F-14 was already fixed by the
  security stream (`ServerEndpoint.Parse`, merge a03364a).
- [Fable 2: fixed as asked, see R2-01 and R2-10](fable-review-2.md#f-verdicts) **F-15 hooks and sync.** `Core/Plugins/Hooks/HookBus.cs`: a 5 s per-handler timeout (`HookBus(TimeProvider?, TimeSpan?)`);
  a handler past it is cancelled, left behind and reported through `HandlerFailed`; an interceptor that times out counts as
  "allow", a transform leaves the value. An answer that is already complete stays synchronous (KeyRouter relies on that).
  The abandoned handler task keeps running if it ignores its token. `Core/Sync/MailSync.cs`: `onNewEmailReceived` now runs after
  the gate is released (still awaited, still ordered, bounded by the bus timeout); every hook call swallows non-cancel
  exceptions; `SyncNowAsync` catches everything (CA1031 suppressed with a reason, three places). `MailSync.Push.cs`: debounce
  capped at `MaxCoalesce` 3 s; `AuthError` on push open emits one `SyncProblemEvent` (retry with backoff continues, the flag
  resets when a stream opens); a `TransportError { Retryable: false }` ("No push support") switches to `PollAsync`, a sync of
  all push types every `PollInterval` 60 s. Judgement: 60 s is my number, Bulwark has none. `MessageListViewModel` shows
  `SyncProblemEvent.Message` in `Error` and clears it on the next `PushStateEvent(true)`: a status line only, no sign-in prompt
  (the accounts views are still not bound to the sign-in screen, M1.md open issue).
- [Fable 2: partly fixed, see R2-05 and R2-11](fable-review-2.md#f-verdicts) **F-16 keys.** `Presentation/Input/KeyRouter.cs`: a real (`KeySource.Keyboard`) plain Up/Down is **not** handled (the focused
  `ListView` moves one row); WebView and Automation arrows are. This means: with focus outside the list, an arrow key does
  nothing and the reader's `_motion.Step()` and `OpenReason.Key` are not used for arrow navigation. **Hand test**: one arrow press
  moves one row, and the reader animates as for `j`. `App/Shell/KeyChordMapper.cs`: Shift+digit and Ctrl+Alt (AltGr) map to null;
  the router also refuses any Alt chord. Typing in a text box is still filtered in `ShellPage.OnPreviewKeyDown`, not in the router.
- [Fable 2: fixed](fable-review-2.md#f-verdicts) **Body cache race.** `MailSyncEventArgs.AccountKey` is set by `MailEventHub` from the account whose engine produced the event;
  `BodyCacheSync` files under it (the `IAccountManager` parameter is gone) and the list ignores events of another account.
  `MailEventHub.Publish(e)` without a key uses the active account at publish time (tests).
- [Fable 2: partly fixed](fable-review-2.md#f-verdicts) **DI wiring test.** `tests/Barbican.Presentation.Tests/Composition/CompositionGraphTests.cs` resolves **every** registered
  service of Core + MailCore + Accounts + AccountsPresentation + the loader chain, with a presenter shaped like `BodyShell`
  (`Lazy<IKeyRouter>`). It does not include the Uno-only types (`BodyShell`, `AppComposition`, views): a cycle that runs through them is
  still only caught at launch. Worth a head-level test in an App test project. It found a real bug: `AccountManager.DisposeAsync`
  threw `ObjectDisposedException` when the container disposed the one instance for each of its three registrations; now idempotent.
- [Fable 2: fine, cost not measured](fable-review-2.md#f-verdicts) **MailBodyView / shell.js.** `App/MailBody/MailBodyView.cs` unsubscribes on `Unloaded` and re-subscribes on `Loaded`.
  `Assets/mail/shell.js` posts `ready` from `requestAnimationFrame` with a 34 ms timer fallback (a hidden window may never run a
  frame). **Not measured**: the feel cost (up to one frame per message open) and whether data: images are decoded at reveal. Measure
  with the hidden feel run before the M1 gate; if the frame is too dear, `img.decode()` on the data: images is the alternative.
  Only a static test (`ShellScriptTests.ReadyIsPostedOneFrameAfterTheLayoutWithATimerFallback`) covers the script.
- [Fable 2: fixed](fable-review-2.md#f-verdicts) **F-10, F-11.** Flagged tile is `HasKeyword = "$flagged"` (`Presentation/Shell/MailShellStartup.cs`). `tools/spec-check.py`: a T- row
  whose only tests are `[Red]` is `pending`; `--milestone` also fails while any `[Red]` test exists anywhere (no stream-to-
  milestone map exists, so a red test for a later milestone would block an earlier gate: revisit if that happens) and for a J- row
  that is `done` without `cs_test`. J-009 and J-028 are `partial(M2)` (`spec/jmap.csv`), and `spec/README.md` says so. Verified by a
  temporary `[Red]` on a tagged test (row went pending, gate message printed); not kept as a test (no harness for Python tools).
- **Not covered.** Linux: `tools/linux.ps1 ping` failed once (ssh key not accepted / VM unreachable): Linux pending (VM
  unreachable). No hidden run was launched. Commit hashes: see `git log m1-fix-core` (this stream's commits are the ones after a03364a).

## Fable review 2 (stream: fable-review-2)

Report: [fable-review-2.md](fable-review-2.md), branch `fable-review-2` from `m1` at `4eccc0f` (reading only; nothing built, run or
launched; Linux unverified). For the next Fable pass, in this order:

- **R2-01 after its fix: a refused sign-in must stop all traffic of that account.** Why: by the Stalwart source a failed login
  counts toward a ban of the client address (100 a day by default, no end by default), and a session signed in with a one-time code
  is checked again after the token lifetime. Check the latch in the client, that the push loop, the poll and the unread timer all
  stop, and the test that counts requests against a 401 server. Paths: `Core/Jmap/Transport/StaticAccessTokenSource.cs`,
  `AuthorizedHttp.cs`, `Core/Auth/OAuthTokenSource.cs`, `Core/Sync/MailSync.Push.cs`, `Core/Accounts/AccountManager.cs`. This finding
  is derived from source and was **not** run (running it can block the user's address): confirm the reading of
  `crates/http/src/auth/authenticate.rs` and `crates/common/src/auth/authentication.rs`.
- **R2-04 after its fix, on both OSes.** Named elements against `shell.js` (`name="URL"`, `"body"`, `"querySelectorAll"`,
  `"addEventListener"`, `"__bbd"`), the give-up path, and on Linux whether the shell's `frame-src` really stops a click in an
  unhandled frame. Also the dark pass (`email_dark.js`) reads the same document: check it for the same pattern. Paths:
  `Core/Mail/Body/MailSanitizer.cs`, `App/Assets/mail/shell.js`, `App/MailBody/BodyShell.cs`.
- **Sanitizer parse differences.** The sanitizer parses with scripting off (`MailSanitizer.Clean`), which matches a frame without
  `allow-scripts`. Any later use of sanitized HTML in a context where scripting is on (the M3 editor, a plugin view) re-opens the
  `noscript` difference. `EmailBody.CidBodyValues` holds the unsanitized originals: they must never be shown.
- **R2-02 and R2-03.** Start-up without network and the account screens once they are bound to `SignInViewModel` and
  `AccountsViewModel`: cancel of the OAuth wait, Enter to submit, the row errors.
- **R2-05 and R2-11.** Which focused controls keep Enter and Space, and punctuation shortcuts on a non-US layout. Hand test.
- **R2-06.** The vault under two instances, once `AccountStore.ChangeAsync` reloads inside the refresh lock.
- **R2-07.** The pinned authserv-id and the trace-block rule when M2 builds the trust banner; no code removes forged inbound
  headers on the server side as far as this review found (`crates/smtp/src/inbound/data.rs`).
- **R2-10.** The hook catalogue's per-hook budget and fail-closed flag before the first send guard (M3) and the plugin hosts (M6b).
- **Not reviewed in this pass** (say so at the gate): the shell-ui layout, motion and theme code, `X11BodyOverlay.cs`, the Linux
  secret store at run time, items 24 to 39 of the verify-stage list.

## m1-fix-2 (stream: m1-fix-2)

This stream fixes Fable review 2 R2-01 to R2-05 on branch `m1-fix-2`, taken from `m1` at `1c34da4`, and merges them into `m1`.
- Commits: `8623ecd` (R2-01, R2-02), `a56f335` (R2-03, R2-05, the R2-02 list state) and `675a7c4` (R2-04).
- E-3 and E-4 are implemented as the review's recommended defaults; the user has not confirmed either yet.
- R2-06 to R2-14 are not touched; each is owed before its milestone (M1.md §5).
- Windows: `tools/build.ps1 test` ran 956 tests: 952 passed, 4 skipped, 0 failed. Linux pending (VM unreachable).
- No real account was used. Every account check ran against FakeJmap.

For the next Fable pass, in this order:

- [ ] **R2-01: the latch. Does a refusal really stop every request of that account?**
  - **Why:** Stalwart counts every failed login toward a ban of the client address.
  - **Design:** `Core/Auth/AuthLatch.cs` wraps the token source of every client that `JmapClientFactory` and the sign-in paths
    build.
    - It closes on any `AuthError { NeedsSignIn: true }` from the inner source, which includes a refused refresh token.
    - It also closes when `AuthorizedHttp` gets a 401 after a renewed header (`Core/Jmap/Transport/AuthorizedHttp.cs`).
  - **Check:**
    - that no request path skips the latch;
    - that `MailSync` stops the push stream, the poll and the debounced syncs (`StopForSignIn` in `Core/Sync/MailSync.Push.cs`
      and `MailSync.cs`);
    - that `AccountManager.OnRefused` stops the unread timer for an inactive account.
  - **Known:**
    - requests already in flight when the refusal lands can each still get one 401;
    - the refused session is moved to `_retired` and disposed only with the manager.
  - **Tests:** `tests/Barbican.Core.Tests/Auth/AuthLatchTests.cs`, which counts `FakeJmapServer.Refused`, and
    `Accounts/AccountManagerTests.cs`.
- [ ] **R2-02: start-up without a network, and the retry loop.**
  - **Design:** `AccountManager.RestoreAsync` connects every remembered account at once and activates the first one, in
    remembered order, that connects. When every account failed only because its server could not be reached, the first one
    opens without a session ("connecting", E-3).
  - **Retry:** `RetryAsync` runs on `TimeProvider` (2 s doubling to 60 s) and connects the accounts that are unreachable and not
    refused. When an account connects, `Attach` raises `ActiveChanged(info, info)` for the active one, so the mail reloads.
  - **Check:**
    - that the loop ends when nothing needs it;
    - that it never retries a refused account;
    - that the same-key `ActiveChanged` (also raised with `renewed: true` after a new sign-in) is handled everywhere it is
      listened to;
    - `SwitchToAsync` connecting the active account that has no session.
  - **UI:** `MessageListViewModel.IsConnecting` and `MessageListView` show "Connecting…".
- [ ] **R2-03 and the shell's choice of screen.**
  - **Paths:** `App/Shell/ShellPage.xaml.cs` (`ShowScreen`, `Back`, `WireAccounts`), `App/Views/SignIn/SignInView.xaml(.cs)`,
    `App/Views/Accounts/AccountPopoverView.xaml(.cs)`, and `Presentation/Accounts/SignInViewModel.cs` (`Prefill`, `Submit`,
    `CanLeave`, `LeaveCommand`).
  - The refused account on screen gets the form, filled in once per refusal.
  - A refused row in the popover raises `AccountsViewModel.SignInRequested`.
  - Cancel leaves the form only for "Add account", for a request from a row, or when another account exists.
  - **Judge:** whether `_offered`/`_dismissed` can trap the user on the form or hide a needed one. The views are unproven on
    screen (HAND TEST 1).
- [ ] **R2-04 on both OSes.**
  - **Sanitizer:** `Core/Mail/Body/MailSanitizer.cs` (`Unname`) drops `name` on `img` and its `id`. `<a name>` and a plain
    `<img id>` stay.
  - **Shell:** `App/Assets/mail/shell.js` reads the frame document through `Document.prototype` getters, uses `EventTarget.prototype.addEventListener` and
    `Element.prototype.setAttribute`, keeps its marks in WeakSets (`handled`, `finished`), and `reveal` refuses an unfinished
    frame.
  - **Host:** `App/MailBody/BodyShell.cs` `GiveUp` sends `MailPageBuilder.Error` once; if that is not ready in time either,
    `Ready` runs and the shell refuses the reveal, so the old page stays.
  - **Evidence:** the hidden probe `spec/feel/M1/verify/r2-04-probe.json` sent a page with `img name=URL/body/querySelectorAll/...` and
    `form name=documentElement` straight to `__bb.show`, without the sanitizer: it finished and was revealed.
  - **Not covered:**
    - `email_dark.js` still uses `doc.body`, `doc.documentElement` and `doc.defaultView`, which are safe only because the sanitizer runs
      first. Bringing it to the prototype pattern would mean diverging from the verbatim Rust/Bulwark port. Decide.
    - In `size()`, `b.querySelectorAll` runs on the body element, which named access does not reach.
    - The give-up path was not exercised at run time.
    - Linux / WebKitGTK.
- [ ] **R2-05: the focus heuristic.**
  - **Paths:** `App/Shell/KeyGate.cs` (the tested rule) and `ShellPage.Activates`, which treats a `ButtonBase`, `ComboBox`,
    `ToggleSwitch`, or a `SelectorItem` outside `ListRegion` as owning the key.
  - **Check:** controls that may be missing (`HyperlinkButton` is a `ButtonBase`; an `Expander` header; a `NavigationViewItem`),
    and that Enter on a message row still opens it.
  - The real keyboard path is a hand test.


## M1 hotfix: sign-in (stream: m1-hotfix-signin)

HAND TEST 1 on the portable zip from 6b506be: passkey sign-in failed, the TOTP sign-in on the server's page crashed the app
at the callback (WER: CoreMessagingXP.dll 0xc000027b, combase RPC_E_WRONG_THREAD), and every later start showed an empty
mail screen. Commits 15223c7, aa77c20, 56038a3.

- **Root cause of the crash and of the empty shell: `ActiveChanged` off the UI thread.** `AccountManager` raises it from
  thread-pool continuations (sign-in after the loopback, `RestoreAsync` under `AccountsStartup`'s `Task.Run`, and since R2-02
  the retry loop). `ActiveMailSource` forwarded it synchronously to UI-bound ViewModels. The fix marshals in
  `Presentation/Mail/ActiveMailSource.cs` only; the manager still raises its events on any thread. **Review:** every other
  direct subscriber of `IAccountManager.ActiveChanged` / `IAccountOverview.StatusesChanged` must keep dispatching itself
  (today: `ShellPage`, `AccountsViewModel`); a new subscriber that forgets will bring the crash back. Consider making the
  manager raise its events through an injected context instead. The proof is `SignInThreadTests` (a UI-thread stand-in, not
  WinUI); no hidden-app run signed in, because the AUTOMATION build has no FakeJmap-backed account route (the fake OAuth
  provider still lives in Core.Tests, linked into Presentation.Tests).
- **`Changed` is now posted, not synchronous, when raised off the UI thread.** Subscribers read `Active`/`Mail`/`Sync` when
  the post runs, so a quick second change can be seen twice with the newer account. Check `MailEventHub.Attach` and
  `MessageListViewModel.OnAccountChanged` tolerate a repeated change of the same account.
- **The crash guard swallows UI-thread exceptions** (`App/Composition/CrashGuard.cs`: `Application.UnhandledException` is
  marked handled after logging to `<data dir>/logs/crash-*.log`). This keeps the app alive but can leave a ViewModel half
  updated; it also hides failures from the AUTOMATION build, which only notices them in the logs folder. Decide whether the
  automation build should fail a run when a crash log appears. Logs hold exception messages, which may contain an address the
  user typed (never a password or token by construction; not enforced).
- **The 11:03 and 11:05 automation crashes (wt-m1-fix-security, P7 0x800710dd)** are the same failure mode (an unhandled
  WinRT exception on the UI thread turned into a fail-fast at combase+0xabfa) but not the same bug: a different HRESULT
  (HRESULT_FROM_WIN32 4317, ERROR_INVALID_OPERATION), in hidden runs of the F-03/F-13 WebView2 work with no sign-in. 21 such
  crashes of that automation exe since 04:46 are in WER. No dump existed. The crash guard will now log them; look at the
  logs folder of the next one before guessing.
- **RFC 9207 check relaxed (deviation from F-04).** `CallbackCheck`: an issuer the metadata does not name no longer refuses an
  `iss` it cannot compare (Bulwark browser-sign-in.ts:170 parity). `OAuthDiscovery` takes the issuer of the server's own
  well-known document for an own per-account document without one (Stalwart answers `issuer: ""` on a plain-HTTP connection
  behind a TLS proxy, openid.rs:60). The user's server sends a full issuer, so this did not cause HAND TEST 1; it was latent
  and the login hint now routes every sign-in through `/api/discover/<account>`.
- **Passkeys: cause not proven.** Stalwart 0.16.27's source has no WebAuthn; the user's server runs a newer login page with
  passkeys whose passkey login posts the page's raw query back (`authCodePasskey` with `query`). The only differences
  between the C# and Rust requests were the missing `login_hint` (6b506be's form sent none) and `%20` instead of `+` in
  `scope`; both are now identical to Rust (`OAuthFlow.FormEncode`, test `The_authorize_query_is_written_as_the_Rust_app_writes_it`).
  The same OAuth client id is used by both apps (stateless `swc1.` ids, the cached ids are equal). If passkeys still fail,
  the next step needs a real browser: the page's error text and the server's log for the `authCodePasskey` request.
- **Atomic save:** an account is remembered (vault: entries, then index) before hooks and before it is announced, so the
  crash point of HAND TEST 1 cannot lose it (`A_finished_sign_in_is_remembered_before_anything_can_fail_on_it`). Not changed:
  a vault write that fails still lets the sign-in succeed unremembered (Rust parity, `AccountStore.LastError` only).
- **User state left as found:** `%APPDATA%/Barbican-dev` and the credentials `Barbican.Next/dev/session`, `.../accounts`
  (the pre-F-05 single-entry layout) were only listed. The new build migrates them on the first start (ADR 0008 amendment)
  and should restore the account without the form.

## M1 look (stream: m1-look)

HAND TEST 1: "does not look or feel like the concept winui3 build". The gap, the choices and the numbers are in
docs/milestones/M1-look-gap.md.

- **Icon colour bug (fixed, worth a second look).** `SIcon` bound its stroke to its own `Foreground`. Control.Foreground is not
  inherited into a template's content, so every icon without a local Foreground drew in the theme default. That is whitish in dark
  and almost invisible in light (rail, toolbar, folder icons). SIcon now binds, once it is loaded, to the nearest ContentPresenter's
  Foreground, and the button templates pass their Foreground to it.
  - Check: does an icon that is re-parented after Loaded (virtualised list containers) keep the right source?
- **Prototype preferred over the mockups.** Seven places are listed in the gap file: Bento selection card, the "B" mark, label rows,
  "New message", the reader order, a one-tile sidebar, and Segoe UI. Review them against design/README.md: the design language
  file still describes the mockups.
- **Motion that deviates from the prototype** to stay within design/MOTION.md:
  - one block rise instead of the staggered per-row entrance;
  - 150 ms instead of 220 / 260 ms on a style switch;
  - the header rise runs on every switch (the prototype's ran only on first load, because of a bug in the prototype).

  Check the header rise for feel during slow `j` presses. It is skipped only while keys repeat.
- **Body cross-fade.** shell.js keeps the old frame under the new one for the fade (110 ms + 20 ms) and then removes it.
  - Two frames are alive for that time, which doubles the memory of two large mails for about 130 ms.
  - `print` and `dim` act on the new frame only.
- **The flicker rule gives a false positive on a sparse target folder.** `m1_scenarios` folder_click flags BLANK on the list region
  because Sent in the sample data has one row. flicker.py compares every frame with the first and the last frame, not with the
  settled target. Either pick a folder with as many rows, or give the rule a target baseline. The run before the port showed
  0 only because its `select m3` changed nothing.
- **Sender panel** (new `Views/Context/ContextView`).
  - It reads the sender's other messages from the loaded list rows only; no request is made.
  - It hides below a 420 px reader, which the prototype never does.
  - Its data is display-only, and no action is wired.
- **Body typography is not ported** (prototype 15 / 16 px, line height 1.6 / 1.8, Nordic 600 px measure). It belongs to
  MailPageBuilder / sizing. Decide whether M2 should adopt it.
- **The drift check** (`tools/lookdiff.py check`) compares 12 RenderTargetBitmap pictures against `docs/milestones/M1-look/ref/`
  at 2 %.
  - Sample data that depends on the time of day (the Nordic greeting, relative dates) can move a few pixels. It was 0.00 % on the
    same day.
  - It is a script, not a test in `tools/build.ps1 test`, because it needs a running hidden AUTOMATION build.

## M2, consolidated at the verify stage (stream: m2-verify)

The M2 sections of the plan, stage F, the five streams and their reviews are archived verbatim in
[fable-review/M2-streams.md](fable-review/M2-streams.md). Below they are deduplicated and ordered by risk. Each item names its
sources in brackets: plan, f (stage F), actions, menus, reader, search, unified, their reviews (`-rev`), and **[verify]** for this
stage's own items. Commits are on `m2` (`git log --first-parent m2`); the per-stream commits are in the archive. Report:
[milestones/M2.md](milestones/M2.md).

### 1. Security, privacy and the user's real screen
1. **GuardedHttp (mandatory)** [reader, reader-rev]. `src/Barbican.Core/Net/{GuardedHttp,PublicAddress,GuardedRefusal,OneClickUnsubscribe}.cs`.
   - One `SocketsHttpHandler`: no redirects, cookies, proxy or credentials; https only (http only through the loopback test seam);
     GET/POST; 2 MB cap; 20 s timeout.
   - `ConnectCallback` resolves once, refuses any non-public address and connects to the address it checked (no DNS rebinding).
   - The review widened `PublicAddress` (192.0.0.0/24, 198.18.0.0/15, 64:ff9b:1::/48, Teredo).
   - Check: that the callback is the only way out (no other `HttpClient` in reader code), the embedded-IPv4 rules, and whether
     ISATAP and 2001:2::/48 matter.
   - **Fable review 3: fine** by reading (connects to the checked address, redirects re-checked). Low: three small ranges missing in `PublicAddress` (R3-12).
2. **R2-07 Authentication-Results trace block (mandatory)** [reader, reader-rev, f-rev].
   `src/Barbican.Core/Mail/AuthResults/{AuthTrace,AuthServPins}.cs`, `src/Barbican.Presentation/Mail/Loading/ReaderDepth.cs`.
   - A header counts only inside the server's own block: from the first Received, whose `by` host is pinned per account or trusted
     by D-9.
   - No verdict in Sent, in Drafts or for `$draft`. Stalwart's header order is confirmed (`crates/smtp/src/inbound/data.rs:482-501`).
   - Open:
     - (a) with `add_received` off and `add_auth_results` on, a forged block counts;
     - (b) the first open of an account may still show a verdict in Sent while the roles load;
     - (c) the faithful `dkim_gate` port accepts any authserv-id in its own unit (one-click), so D-9 must hold through the trace
       block only. Confirm that one-click reads `AuthTrace`, not the raw headers.
   - **Fable review 3: partly fixed.** (a) is not accurate: Stalwart writes its Received and Authentication-Results only for port 25, so a submitted, JMAP-sent or imported message can carry a forged first block (R3-06, decision E-9). (b) still open.
3. **R2-14 / M2-Q2 writes in agent profiles (mandatory; M2-Q2 still a proposal)** [reader, reader-rev, verify].
   `src/Barbican.Core/Accounts/ReadOnlyPolicy.cs`, `src/Barbican.App/MailBody/Automation/{WriteTestGuard,Rx*Command}.cs`, `tools/auto-run.ps1`.
   - Agent profiles fail closed without both `.local` lists.
   - The `rx-*` commands exist only in `AUTOMATION_WRITES` builds, re-read both lists each call, and destroy only token-marked mail.
   - [verify] proved it live: with the agent import, the menus greyed every write row on `<test-account>`, and `act mail.star` and
     `act mail.delete` answered `disabled`, sending nothing.
   - Check: that no path makes `<readonly-account>` writable in any profile.
   - **Fable review 3: fine** (`ReadOnlyPolicy`, `ProfilePaths.IsAgentProfile`); notes in R3-12.
4. **The native plugin host is not a security boundary (mandatory)** [unified, unified-rev].
   `src/Barbican.Core/Plugins/Native/{NativePluginHost,PluginLoadContext,PluginHookBridge,LoadedPlugin}.cs`.
   - There is one collectible load context per plugin, but a plugin runs with full trust and can take the process down.
   - Decide: user plugins in `<config>/plugins` stay off until the M6b permission UI.
   - A plugin's `Dispose` runs synchronously at shutdown, and a faulted `Ready` task goes unobserved.
   - The payload JSON is built by reflection with IL2026/IL3050 suppressed: check before any trimmed publish.
   - **Fable review 3: confirmed, open.** The code loads `<config>/plugins` (`UnifiedRegistration.cs:136-137`) although M2.md default 7 says off until M6b (R3-04, decision E-10).
5. **R2-06 two instances on one vault (mandatory)** [unified, unified-rev].
   `Core/Accounts/{IAccountVault,AccountStore}.cs`, `Platform/SecretAccountVault.cs`, `Core/Auth/OAuthTokenSource.cs`.
   - Each change re-reads inside the cross-process lock, then upserts or removes one entry; there is one retry with the in-memory token.
   - Not covered: the lock across two real processes. A second acquire in one process waits 15 s and then proceeds without the lock.
   - **Fable review 3: not reviewed.**
6. **Zip slip, saves and file names** [reader-rev, unified-rev].
   - Download all now sanitizes names (`ReaderNames.SanitizePart`).
   - Open:
     - `SaveTargets` writes into Downloads with no dialog and no check for Windows reserved names (`NUL.txt`, `CON`);
     - TNEF file names reach the saver unsanitised (`TnefExpander`);
     - `rx-save queue PATH` takes any path (automation only).
   - **Fable review 3: partly fixed**; what is left is R3-10 (no dialog, reserved names, IO errors not caught: R3-05).
7. **Trusted senders key on the From address only** [reader-rev]: Bulwark parity (`email-viewer.tsx:3170`), so a spoofed From of a
   trusted sender loads remote content.
8. **Live credential copies** [verify]. The agent import into `auto-8889` wrote the two Rust password entries under this app's own
   `account/<id>` targets of that profile. The Rust entries were only read. No OAuth token was imported or refreshed. Decide whether to
   keep them for the next live stage (M2.md §6 item 10).
9. **No real input; every popup in-window** [menus, menus-rev, verify].
   - The four hosts (menu, dialog, palette, `?` sheet) are each one constrained `Popup` (`PopupLintTests`).
   - Every hidden run of this stage answered `popup-guard open 0` and `own-is-foreground=no`, and the foreground window was
     unchanged at every start.
   - The real right-click path and `CharacterReceived` on a German layout are hand tests.

### 2. Open correctness risks
10. **Most M2 views behind the logic are not built (the exit gap)** [actions-rev, menus, menus-rev, reader-rev, search-rev, unified-rev, verify].
    - 70 M2 interaction rows plus 19 carried M1 rows are `todo` (M2.md §2).
    - The biggest gaps:
      - the account popover view;
      - hover strip states;
      - the sidebar "+", tag management and arrow keys;
      - MENU-READER and MENU-ATT (the reader's More button is disabled), view source, forward as attachment, Save to Files;
      - snippet marks;
      - import and export entries and a file picker;
      - the unified menu, per-account rows and the shared section;
      - J-051 `collapseThreads`, J-074 `x:ArchivedItem`.
    - Recommended: one follow-up stream before the gate.
11. **Unified with two real logins** [verify].
    - Live, `unified on` reported `sources=2`, but only `<test-account>` rows showed, and "Switch to `<readonly-account>`" was disabled.
      Either the second login was not signed in by the import's restore, or its rows were dropped.
    - In the sample run the header stayed "All inboxes" after `unified inbox`, and the sidebar Inbox row stays highlighted.
    - Files: `Core/Mail/Unified/*`, `Presentation/Unified/*`, `App/Views/Unified/Automation/UnifiedCommand.cs`.
    - Also open [unified-rev]:
      - other logins refresh every two minutes;
      - paging is ordered within a page only;
      - a read-only login's row is refused as "not signed in".
    - **Fable review 3: confirmed, gate blocker.** No row of All inboxes can be opened: the composite row id goes to the active account as an email id (R3-01). Row-menu Download and View source send an empty account id (R3-02).
12. **Writes are never retried and a failed chunk stops the rest** [actions]. `MailWriter.SetChunksAsync`; Bulwark `batchMoveEmails`
    :4134 continues. Chunks already applied are not reported.
    - **Fable review 3: confirmed.** A later failed chunk also hides the applied ones: all rows come back and no undo is recorded (R3-03).
13. **Optimistic removal and "a push delta applies once"** [actions]. `LoadedList`, `MessageListViewModel.Actions.cs`,
    `MailActionService.Removal.cs`.
    - A delta whose `Added` re-inserts a message the user just deleted (server race) shows it again until the next delta.
    - [verify] trace: the port sends no `list_delta` after a write (Rust did); it relies on push (`spec/traces/M2/*.rust.txt`, optional line).
14. **Sync delta during a search** [search, search-rev]. `ListFilters.For` now reads the grammar for an unshaped query, but `in:trash`
    and `in:spam` fall back to the open folder. The real fix is one line: `Watch` with the shaped query in `MessageListViewModel`.
15. **Sieve save** [search, search-rev]. There is no `ifInState`, so another client's save in between is overwritten (Rust identical).
    `QuickRuleSaver`, `SieveScriptStore.SaveAsync`. Decide before M6. L-016 (live, byte-for-byte restore) did not run.
16. **MimeKit and DnsClient AOT and trim unproven** [f, f-rev, reader-rev, unified]. No trimmed publish was made. TNEF uses MimeKit
    `TnefPart` with the code-pages provider. Run one trimmed publish (IL2104/IL3053) before M8.
17. **`ActiveListSourceResolver` reads `IActiveMail.Mail` at call time** [f-rev]. Unified replaced the resolver; check that it now
    resolves from the captured service.
18. **Select all in folder is capped at 10 000 ids without saying so** [actions-rev]. `MessageListViewModel.ResolveTargetsAsync`.

### 3. Deviations and judgement calls
19. **Asserts not ported verbatim** [f, f-rev]. D-234 (T-206 through the plugin host), D-235 (T-110), D-236 (`.example` domains:
    check the DKIM and DMARC alignment pairs), D-237 (T-023, T-024), D-238 (T-216..T-218 loopback seam), D-239 (contract renames).
20. **Hook policy (R2-10)** [unified, plan].
    - `hooks.csv` `budget_ms` and `fail_closed`; `HookPolicies.g.cs`.
    - Intercepts get 60 s, so a plugin link check may hold a link that long.
    - In-app handlers still start synchronously; native handlers start on the pool.
    - 14 hooks were re-tagged out of M2 (M2-Q5).
21. **Spam score** [unified]. The port of `spam.rs` is line by line. The clean row is hidden unless `showCleanSpam` is set (B-SPAM-004).
    Detail rows reach the reader as banners until the reader reads `IEmailDetailsSource`.
22. **Menus** [menus, menus-rev].
    - The palette opens on Ctrl+K only, not on `k`.
    - The myRights enable rules are derived from RFC 8621 §2.4.
    - Bulwark's "Restore" (Trash) and "Move to Inbox" (Archive) rows are missing.
    - Escape inside a flyout closes the whole menu.
    - Icons are line-icon stand-ins.
    - ToggleTheme is a preview until M6.
23. **Actions** [actions, actions-rev].
    - Confirmation is a dialog request; it answers no without a host.
    - Archive by year or month creates folders one by one.
    - Restore from Trash guesses the target folder.
    - Undo of `trash-and-read` leaves the messages read.
    - Messages may be dropped onto Drafts or Sent.
    - "Move to" lists Drafts and Sent [verify screens].
24. **Reader** [reader, reader-rev].
    - The MDN and the zip are hand ports.
    - Saves go to Downloads with no dialog.
    - A BIMI SVG counts as missing.
    - `onBeforeExternalLink` is intercept-only.
    - One failed download fails the whole zip; Rust leaves it out.
    - The banner budget is 250 ms (not checked against open-to-shown on screen).
    - A double click is ignored per action and message.
25. **Search** [search, search-rev].
    - The filter panel only writes grammar operators (no size presets, no "No attachment").
    - Snippets are fetched lazily per row (25 ms batch).
    - A global-search hit opens only if it is in the first page of its folder.
    - Rules menus depend on the registration order.
26. **Sign-out** [unified, unified-rev]. onAfterLogout fires per account. The whole body cache is cleared; the avatar cache is not.
    `MailSync.Dispose` is now idempotent.
    - **Fable review 3: partly fixed.** Per-account local state other than the body cache and the vault entry stays (R3-11, decision E-12; not confirmed file by file).
27. **J-028 n/a** [unified]. `upToId` is never sent; a delta is applied whole.
28. **Escape clears ticks** [verify]. `KeyRouter` "close" now also calls `ClearChecked` (Bulwark `hooks/use-keyboard-shortcuts.ts:217-221`).
    M1 kept the ticks and the selection bar after Escape. Test `KeyRouterTests.Escape_closes_the_reader_and_clears_the_ticks_as_Bulwark_does`.
29. **Bundled plugin on publish** [verify]. The App csproj target `PublishBundledPlugins` copies `plugins/spam-score` to `$(PublishDir)`
    and fails the publish if the plugin was not built. The portable zip now carries it.

### 4. Partially verified
30. **Feel** [verify]. WGC gave valid frames for the first time.
    - ctx_menu, dialog, palette, delete and undo: 0 BLANK / 0 FLASH in four looks.
    - Bento dialog open: one 27 to 29 ms frame in 2 of 9 runs.
    - Close transitions are a single swap (2 frames).
    - `search_results` and `chip_remove` are unmeasured (the script stops the probe too early).
    - A 500-message import had one 34.8 ms gap.
    - Files: `tools/feel-m2.py`, `spec/feel/M2/verify/`.
    - Real-input scenarios (select_range, drop_move, pane_resize, hover_strip) and the Narrator pass are HAND TEST 2.
31. **Linux** [f, unified-rev, verify].
    - Green on merged `m2` (1426 tests, 0 failed).
    - `xrun -Script auto-m2` drove the menu, the dialog over the body, the palette, `dnd-sim` and undo.
    - Open:
      - the first X11 capture after an open showed the body as a white block in the dark theme (a possible WebKitGTK white flash);
      - 4 frames in the first 0.5 s of the idle probe;
      - the build still restores `Barbican.Platform.Windows`;
      - Secret Service has never run for real.
    - Files: `tools/linux-remote.sh`, `docs/milestones/M2-screens/linux-*.png`.
    - The streams' "no Linux result" came from `tools/linux.ps1` picking Git's MSYS ssh when started from Git Bash; it now calls
      Windows OpenSSH [verify].
32. **Hand tests only** [menus, actions, reader].
    - The real right-click on rows, the body and folders.
    - Ctrl and Shift clicks.
    - Hover strip, pane drag and its reset.
    - Real drag (there is no `CanDrag`: `dnd-sim` is the only entry).
    - The dialog focus trap (`TabFocusNavigation=Cycle`).
    - R2-11 on a German layout.
    - R2-12 scroll (not measured).
33. **Live** [verify].
    - Ran read-only on `<test-account>` through the agent import: L-047 key path, menus with write rows disabled, writes refused,
      search, unified (inconclusive).
    - Leftovers found and not removed (M2.md §4).
    - Every write live check (L-015, L-016, L-022, L-037, L-038, L-039, L-044, L-057) waits for M2-Q2 or HAND TEST 2.
34. **Traces** [verify]. `ActionTraceTests` writes `spec/traces/M2/{delete_undo,move}.cs.txt`; the Rust side is derived by hand
    (`mail.rs:978/:1032/:1045/:1058`, `jmap.rs:688/:692`). The undo line follows Bulwark `stores/email-store.ts:5878` (Rust had no mail undo).
35. **FakeJmap fidelity** [f, actions, reader, unified].
    - `ImportBlob` drops Message-ID: a repeated import is not a duplicate on the fake. Seen again in [verify]: `imported 500 already 0` twice.
    - Shared accounts are not served.
    - Sample runs use the M1 seed (`SeedM2` off), so banners, TNEF and spam rows never show in hidden runs.
    - The synthetic TNEF must stay readable by MimeKit.
36. **Commit trailers** [f]. `954dcd5` says Opus 5.5 and the later stage F commits say Sonnet 5.5. History is not rewritten (D-1).

### Resolved during M2 (for the record)
- **[f-rev]** The automation `text` command sets `TextBox.Text` in process.
- **[actions-rev]**
  - myRights on moves and drops;
  - a cancelled write restores its rows;
  - R2-12 scrolls only on a changed id.
- **[actions]** A DI cycle (`LiveMailActions` takes `Func<>`).
- **[menus]** A DI cycle (`Lazy<PaletteController>`). **[menus-rev]** The dialog focus trap (unverified on screen).
- **[reader-rev]**
  - R2-14 fails closed;
  - the banner budget;
  - zip slip;
  - double clicks;
  - unfolded unsubscribe headers;
  - more `PublicAddress` ranges.
- **[search-rev]**
  - the sync delta grammar;
  - history IO off the UI thread;
  - a single redraw per change;
  - no self-opening dropdown.
- **[unified-rev]**
  - `MailSync.Dispose` is idempotent;
  - slow unified sources keep their rows;
  - a plugin manifest that throws is contained;
  - transfer IO is off the UI thread;
  - `UnifiedSectionView` re-subscribes;
  - G-046 and G-047 statuses are honest.
- **[verify]**
  - Escape clears ticks;
  - the publish carries the plugin;
  - `linux.ps1` uses OpenSSH;
  - the delete_undo and move traces;
  - the `auto-m2` VM script;
  - 33 interaction rows deferred as the plan lists them.

## m2-close-b (branch `m2-close-b`, Sonnet)
- **Reader menus (MENU-READER, MENU-ATT).** `Presentation/Reader/{ReaderMenus,ReaderCommands}.cs`, `App/Views/Reader/ReaderMenuOpener.cs`, `ReaderView.xaml(.cs)`.
  - Review: the "Open" and "Open message" rows are left out on purpose. Opening an untrusted attachment with the system handler needs a decision (executables, .lnk, .hta); Bulwark's own Open is a browser download. "Open message" needs a reader UI for `Email/parse`.
  - Review: "has blob" and "opened .eml" are not tracked, so a loaded message always counts as having its blob.
  - View source reads the whole raw message through `AttachmentSaver.RawAsync` and cuts the display at 2 MiB. Check the memory use for a 50 MB message.
  - The dialog was not drawn in a hidden run: the sample app has no account session, so the raw fetch answers "not signed in". Visual check is a hand test.
- **Search marks.** `App/Views/List/Snippet.cs` (attached properties on the row's two TextBlocks) and `Core/Search/SearchSnippetSource.cs`. A changed filter now raises `Arrived` with the stale ids, and a batch that returns for an old filter asks again. Review the race: `SetFilter` clears and raises under the lock's release, and the retry in `RunBatchAsync`.
- **Import and export entries.** `Presentation/Unified/TransferCommands.cs`, `App/Shell/SystemFilePicker.cs`, `UnifiedRegistration.cs`. The picker is compiled out of the Automation build (queued picks only). The real FileOpenPicker/FileSavePicker/FolderPicker, the window handle and the Uno/Linux path are untested (no Linux testing yet, no real input). Risk on Linux: Uno pickers need a portal on bare X11; a failure answers "nothing chosen".
- **J-051 / J-074.** `Core/Mail/Archive/ArchivedItems.cs` (client for `x:ArchivedItem`, Bulwark `client.ts:13411-13500`), `ListQuery.CollapseThreads`, `ListPageFetcher`. Not wired to a UI: the trash recovery link (I-230) and a settings page are still open. Review the capability test: `Session.IsStalwart` only says the server is Stalwart; without an Enterprise licence the call answers `forbidden` and the link should hide on that error.
- **Body typography.** `Core/Mail/Body/MailPageBuilder.cs` (15 px/1.6 Bento, 16 px/1.8 Nordic, 600 px measure for prose in Nordic: plain text and HTML without its own colours) from `prototype/winui/MailHtml.cs`. Review: the dark-pass page does not get the measure, only the themed and light pages. A hidden check of the computed style inside the frame was not possible (`wv-eval` runs in the shell page, not the frame), so the proof is `MailPageBuilderTests` and the screenshots.
- **import-sim bg and the import stall (open).** `import-sim bg PATH MAILBOX` returns at once. With it, a 500-message import into FakeJmap showed UI-thread stalls of 1.3 to 6 s (round trips of `rows` and `size`, and gaps in the `rendering` probe), also on the unchanged `m2` build (1.3 s). The earlier "34.8 ms" figure came from a probe that missed it. Cause not found: not the list (the stall stays when the target folder is not on screen). Suspects: state-change handling per delivered message, or the in-process FakeJmap competing for threads. Import time also grows with the folder size in FakeJmap. Needs a profile in M3; a real-server import is a hand test.
- **Bento dialog frame (27-29 ms).** Not reproduced: 6 Bento runs (light and dark, the first on a fresh process): worst gap 7.6 to 9.7 ms. Left open as "not seen again", not as fixed.
- **feel-search.py** now waits until the list is steady for 350 ms before stopping the probe. Results: `spec/feel/M2/search/close-b-results.json` (search_results 51 frames, worst gap 9.6 ms; chip_remove 51 frames, 7.1 ms; the list never emptied).
- **FakeJmap.** `Email/import` keeps the blob's Message-ID (the dedupe test depends on it); `x:ArchivedItem` is implemented behind `FakeJmapOptions.ArchiveCapability`; `collapseThreads` is honoured.

## m2-close-a (stream: m2-close-a)

Branch `m2-close-a` from `m2` (commits `56e9907` and the one after it). Closes interaction rows of the account popover, hover strip, sidebar, tags, unified menu and the Bulwark Restore / Move to Inbox rows. Everything below was checked in the hidden AUTOMATION build on sample data only; real pointer and keyboard paths are hand tests.

- **The list is the one truth for the open folder (new rule).** `SidebarViewModel.OnListChanged` follows `MessageListViewModel.MailboxId` and `TagId`; `UnifiedSectionViewModel.IsOpen` follows `MailboxId`. Before, a command that opened the Inbox left the "All inboxes" header and the old highlight. Why review: it changes who owns `SelectedFolderId` (a folder opened by the palette, a `g` key or `unified inbox` now moves the highlight) and the order of three property notifications in `SelectFolderAsync` and `OpenTagAsync`. Where: `src/Barbican.Presentation/Shell/SidebarViewModel.cs`, `Shell/MailShellStartup.cs`, `Unified/UnifiedSectionViewModel.cs`, `Mail/MessageListViewModel.cs` (`TagId`, `OpenTagAsync`).
- **Restore and Move to Inbox in the row menu (spec first).** Archive ancestry is computed twice: `MailMenus.InArchive` walks the menu folder list by depth, `SidebarViewModel.InArchiveTree` walks `ParentId`. They should agree; the second also decides the hover button and the `mail.move-inbox` command. Bulwark `archiveInboxTarget` also falls back to a non-shared Inbox for a shared account; not ported. Restore sends the messages back by `RestoreAsync` (origin role, else Inbox). Where: `Menus/MailMenus.cs`, `Actions/ActionCommandSet.cs`, `spec/menus.md` MENU-MAIL.
- **Move to now hides Drafts and Sent** (Bulwark `lib/move-menu.ts:117`). The drag-to-folder path (`MailActionService.Moves.cs:24`) checks only `mayAddItems`, so a drop on Drafts is still allowed. Decide whether drops follow the menu.
- **Select-all cap in the confirmation.** A selection of 10 000 messages now always asks, even with the confirm settings off, and says that the folder may hold more. Why review: it is a new always-ask rule next to D-7, and the count is the cap, not a server count. Where: `Actions/MailActionService.Moves.cs` (`CappedMessage`).
- **Tags: filter, mark read, delete.** Tag definitions are `emailKeywords` in the prefs (`Tags/TagService.cs`); deleting a definition leaves `$label:` keywords on messages (as the confirmation says). The tag view leaves Trash and Junk out with `inMailboxOtherThan`. Mark all as read queries `hasKeyword` and `notKeyword $seen` up to 10 000 messages and writes them in one `SetReadAsync` (no per-account split, active account only). Where: `Tags/*.cs`, `Actions/MailActionService.Tags.cs`. Not built: "Show all N tags" (needs the per-tag visibility setting), tag counts, tag drag onto rows is the older `dnd-sim` path.
- **Prompt for folder names.** `folder.new`, `folder.new-sub` and `folder.rename` used to fail with "Give the folder a name" from the menu; they now open the prompt dialog (`ActionCommandSet.NameAsync`). The prompt on the real dialog host was verified hidden (`dialog-state`); typing a name and the write are hand tests (M2-Q2).
- **Account popover actions.** "Set as default", "Sign out of <address>", "Sign out of all accounts" run `IAccountMaintenance` after a confirmation through `IActionConfirmer`; without a dialog host they do nothing (fail closed). Alt+Shift+Up/Down reads modifier state with `InputKeyboardSource.GetKeyStateForCurrentThread` (the same read-only call `ShellPage` uses). Drag reorder is not built. Where: `Accounts/AccountsViewModel.cs`, `Views/Accounts/AccountPopoverView.xaml(.cs)`.
- **Hover strip as icon buttons.** Buttons come from the `hoverActions` setting (default delete, star, read, archive; tag and spam optional). "Mark as read" uses the Check icon and "Mark as unread" the envelope, because the icon set has no open envelope. The Tag button opens the row menu Tag flyout under the button (`MessageListView.OnStripClick`). Floating mode and plugin row actions are not built (I-137, I-136).
- **Unified: per-account rows and Shared section.** Per-account rows reuse `AccountsViewModel.Rows` and its switch command, so a refused account opens the sign-in form. The Shared section only lists the shared accounts that All inboxes reads (names from the JMAP Session); opening one account has no list source yet. `UnifiedSectionView` is now XAML (it was code-built).
- **UNIFIED-ROW menu** is only header and Refresh (Bulwark `mailbox-context-menu.tsx:285-306`); "Empty trash in all accounts" needs a unified Trash row.
- **Look.** The reference pictures of `docs/milestones/M1-look/ref` were re-accepted (the "+" in the Folders header moved 0.3 to 0.8 percent of the pixels). New sample-data pictures: `docs/milestones/M2-close-a-screens/` (unified, tag filter, hover strip in Bento dark and Nordic light). Popups are not in `shot`; the account popover was seen once with `wshot`.
- **Tooltips (m2-close-a finish).** The sidebar "+" buttons, hover-strip buttons and Shared rows used `ToolTipService.ToolTip`, which the popup lint denies (ADR 0007); they now use `motion:Tip.Text` (in-window tip). The lint test caught it; no visual change expected. The hidden Automation build and `lookdiff check` were NOT re-run after this change: a hidden Barbican process (PID 42072, left by an earlier run in this worktree) locked the Automation output and was not stopped (not started by this agent). Re-run `tools/lookdiff.py check` once it is gone.

## M2 hotfix: writes (stream: m2-hotfix-writes)

- **Security boundary moved: the user's profiles are now unrestricted.** `src/Barbican.Core/Accounts/ReadOnlyPolicy.cs`
  (`For`, `Unrestricted`, `IsReadOnly`) and `src/Barbican.Core/Storage/ProfilePaths.cs` (`IsAgentProfile`: `auto-*`, `smoke`,
  `smoke-*`, `xrun-*`, `agent-*`). Before, "dev" and the empty profile were agent profiles, so the user's portable zip was read-only
  on every account (HAND TEST 2). The agents' protection of `<readonly-account>` now rests entirely on agents never running in a
  user profile. Review that every agent launch path is covered: `HiddenWindowOptions.Decide` (an AUTOMATION build or `--smoke` exits
  on a named non-agent profile), `tools/auto-run.ps1` `Assert-AgentProfile`, `tools/linux-remote.sh` (`xrun-<name>`). Not covered by
  code: an agent starting a Release build by hand without `--smoke` (visible window, already forbidden by CLAUDE.md) or running
  `dotnet run` with `BARBICAN_PROFILE=dev`. Commit d779998.
- **Stored read-only flags in "dev" are now ignored.** Earlier agent imports (`BARBICAN_IMPORT_RUST_ACCOUNTS=1` in "dev", before
  this hotfix) may have left `<readonly-account>` in the dev profile's credential set flagged read-only; it is now writable there,
  by design (the user owns it). Any agent that still runs a build in "dev" could write to it. `RustCredentialImport` refuses the
  agent import outside agent profiles now. Commit d779998.
- **CLAUDE.md line "C# dev builds use their own profile (`BARBICAN_PROFILE=dev`)" is stale**: dev is the user's profile; agents use
  auto-*/smoke/xrun-*. Not edited by this stream (CLAUDE.md is the user's rules file); the user or the next review should update it.
- **Real drag and drop was never wired** (no `CanDragItems`/`AllowDrop`/`Drop` anywhere); now the platform's in-app DnD:
  `src/Barbican.App/Views/List/MessageListView.xaml(.cs)` (drag source), `src/Barbican.App/Views/Sidebar/SidebarView.xaml(.cs)`
  (folder list drag source and drop target via `AddHandler(..., handledEventsToo)`, tag rows drop targets with a transparent
  background), state in `DropController.BeginMailDrag(targets)/BeginFolderDrag/CanDropOnFolder/DropOnFolderAsync/DropOnTagAsync/
  CancelDrag`. Unverified: real pointer dragging (hand test), the order of `Drop` and `DragItemsCompleted` (CancelDrag is a no-op
  once a drop consumed the drag), Uno Skia's in-app DnD on Linux (compiles and tests green; never driven), `DragUIOverride` captions
  on Uno. CanDragItems on the message list may interact with press-and-hold on touch. Commit ce83fb8.
- **Plain `z` undo** added (`ShortcutTables.Mail`), Bulwark `hooks/use-undo-shortcut.ts:15-25`; Rust maps only Ctrl+Z. Bulwark parity
  chosen over Rust because the `?` sheet already advertised it. Commit 45ef124.
- **Sample runs hide account-policy bugs.** `SampleActiveMail.CanWrite` is always true and bypasses `AccountManager`, so the M2
  verify could never see the read-only zip. The policy-to-client chain now has a unit test (`AccountManagerTests.
  The_users_own_profile_writes_to_every_account_even_one_flagged_read_only`), but there is still no hidden run that signs into
  FakeJmap through the real `AccountManager`; consider one before M3.
- **`caption` automation command** (`src/Barbican.App/Shell/Automation/CaptionCommand.cs`) calls `ShellPage.AttachWindow` on the
  hidden window (ExtendsContentIntoTitleBar on an off-screen, never-activated window); focus-guard stayed `own-is-foreground=no`.
- **No tag keyboard shortcut** exists in Rust or Bulwark; "tag through the shortcut" was checked through the menus only. The palette
  has no tag rows (`palette Tag` / `palette Red`: 0 rows) — a parity question for the menus stream.
- **Not fixed here, owned by m2-close-a (not merged at the time of this hotfix):** tag menu rows Show messages, Mark all as read,
  New tag, Rename, Delete tag, Manage tags have no registered commands (always disabled, also on a writable account); the sidebar
  "+" (`sidebar.tags.add`), folder settings button and Compose are `IsEnabled="False"` in XAML; right-click on the folders header
  and the avatar opens nothing (the menus exist: `menu-open FOLDERS-HEADER` / `ACCOUNT` work); rail Calendar, Contacts, Files and
  Settings switch to apps that do not exist yet (M4-M6) and show nothing.
- **Flaky test seen once** in Barbican.Presentation.Tests during the m2 merge run (1 failure, not reproduced in 3 later runs; name not captured). Worth a look for a timing dependency.

## Fable review 3 (stream: fable-review-3)

Report: [fable-review-3.md](fable-review-3.md), branch `fable-review-3` from `m2` at `91a0741` (reading only; nothing built, run or
launched; Linux unverified). Verdicts on the M2 verify items 1 to 6, 11, 12 and 26 are marked in that section above
("Fable review 3: ..."). For the next Fable pass, in this order:

- **R3-01 after its fix: reads of unified rows.** Every read that takes a row id (body, thread, blob download, snippets, read
  receipt, the reader's menus, the body cache key) must go to the row's own login and JMAP account with the real id. Check the
  FakeJmap test with two logins that opens a row of each, and that a shared-account row opens too. Paths:
  `Core/Mail/Unified/{UnifiedFetcher,UnifiedRows}.cs`, `Presentation/Mail/Loading/{MessageLoader,MessageFetcher}.cs`,
  `Core/Search/SearchSnippetFetcher.cs`, `Presentation/Reader/ReaderBannerActions.cs`. Derived by reading, **not run**.
- **R3-02: one `MailTarget` contract.** Six constructors fill `AccountId` and `EmailId` differently (list in the report). After the
  fix, grep every `new MailTarget(` and every reader of `AccountId`; run Download (.eml) and View source from the row menu.
- **R3-03: partial results of chunked writes.** `MailWriter.SetChunksAsync`, `DestroyAsync`, `UnifiedMailWriter.RouteAsync`,
  `MailActionService.Removal.cs`: the rows put back must be exactly the rows the server did not change, and undo must cover the rest.
- **R3-04: whether `<config>/plugins` is loaded** must match the decision (E-10). The load context is not a boundary; do not let a
  comment say it is.
- **R3-05: exceptions out of commands.** No runner catches them (`ShortcutCommands.Run` drops the task). After the shared runner
  exists, check what `CrashGuard` does with a UI-thread exception (not read for this point).
- **R3-06: the trust rule is only as good as the server's own header.** Stalwart adds Received and Authentication-Results on port
  25 only (`crates/smtp/src/inbound/data.rs:482-501`, `crates/registry/src/schema/structs_impl.rs:28843-28875`). Re-check when the
  default changes with a server version, and check that the app's own import cannot be used to plant a verdict.
- **R3-08 before the archived-items view**, **R3-09** (search in a tag view), **R3-11** (what a sign-out leaves in the profile:
  list the files per account first).
- **Tests pass on a contract the app does not use.** `AttachmentSaverTests` build the target by hand with the real account id;
  the app builds it through `UnifiedTargetResolver`. Prefer tests that take the target from the same resolver the menus use.
- **No hidden scenario opens a unified row or runs a row-menu command on one.** The sample run has two FakeJmap logins
  (`SampleUnifiedLogins`), so both are cheap to add.
- **Not reviewed in this pass** (say so at the gate): anything running, Linux, R2-06 (the vault), `MailSync` stop paths,
  `PluginHookBridge`, `TagRules`, Sieve, the palette and key router, the views' drag and drop code, the calendar invitation code,
  images other than four M2 screenshots, and the plan documents listed in the report's section 5.
- **Privacy**: the text of all 187 commits on every ref was searched with the private pattern: clean; identities are all noreply.

## m2-fable-fix-3 (branch `m2-fable-fix-3`): Fable review 3 items R3-01, R3-02, R3-04, R3-05

- **R3-01 read routing.** `Presentation/Unified/UnifiedReadRoutes.cs` resolves a unified row once into (login client, JMAP account =
  shared account or the login's `MailAccountId`, server id) and keeps one `MailService` + `ClientBlobSource` per source while the
  login's client stays the same. `MessageFetcher` keys the body cache by `UnifiedRows.SourceKey(login, shared)` and the server id, and
  hands back bodies and threads with row ids again (`Tagged`), so the reader, conversation strip and actions keep speaking in row ids.
  Review: the pinned authserv id and the server host follow the login, the folder roles the source key (`ReaderDepth.Shape` rolesKey).
  Search snippets of unified rows go to their own login (`SearchSnippetFetcher.FetchRowsAsync`); a source whose login is not connected
  or whose server refuses gets no snippets while the others still do. Test: `Presentation.Tests/Unified/UnifiedReadTests.cs` (two
  FakeJmap logins with different account ids). Not done: the hidden scenario step (`unified on`, open row 1, assert body text) the
  review asked for; the read-receipt path (`ReaderBannerActions.ReceiptAsync`) is changed but has no test.
- **Decision (R3-01):** a read receipt for a row of a *shared* account is refused ("Receipts are sent only for mail of your own
  accounts."): the receipt would go out from the user's own identity for mail addressed to the group (not checked against Rust).
- **R3-02 MailTarget contract** (written on `Core/Mail/Model/MailTarget.cs`): `AccountKey` the login whose writer takes the action
  (the account on screen for a unified row), `AccountId` the JMAP account or empty for the login's own, `EmailId` the list id. Code
  that talks to a server asks `Login`, `JmapAccountId(loginMailAccountId)` and `ServerId`. This keeps the writers (which route by the
  row id, `UnifiedMailWriter`) unchanged instead of the review's "EmailId = server id" variant; review whether that is the better
  trade. Constructors: `UnifiedTargetResolver`, `ContextPointerRouter` fallback (empty, no longer the account key), `ReaderMenuOpener`
  and `RxSaveCommand` (`MailTarget.OfOpen`), `LiveMailActions`, `MailActionService.Tags` (empty = own). Consumers moved to the
  contract: `AttachmentSaver`, `EmailParseOpener`, `TnefExpander`, `QuickRuleService` (refuses a row of another login: "Switch to that
  message's account"), `InvitationBannerSource` (reads the invitation through the row's login). Test: `Presentation.Tests/Unified/RowMenuSaveTests.cs`.
- **R3-04 user plugins off.** `NativePluginHostOptions.ForApp` (Core) builds the app's roots without `UserRoot`; `UnifiedRegistration.AddPlugins`
  uses it. The test is in Core.Tests (`Plugins/UserPluginsOffTests.cs`), not `CompositionGraphTests`: the App head's registration is not
  reachable from a test project, so the test pins the factory the registration calls and loads a real plugin copied to
  `<config>/plugins/x` (not loaded; loaded in a control run with the user root named). `PluginLoadContext`'s comment now says it is
  not a security boundary.
- **R3-05 transfers and the command runner.** `MailTransfer` catches IOException/UnauthorizedAccessException into a failed result with
  the reason ("Import failed after N messages: ...") and ends any other run in `finally` (the exception still goes on). One runner,
  `Presentation/Commands/CommandRunner.cs`, registered in `MenusPresentationRegistration`, used by ShortcutCommands, ContextMenuController,
  PaletteController, RailView, SelectionBarView, MessageListView (hover strip), SidebarView and the automation `act` command: an
  exception becomes an error toast ("That did not work: <message>") and a crash-log file (`CrashLog.Write`, source `command <id>`), a
  cancellation says nothing. Decisions: whether a failed *result* is toasted stays as each surface had it (menus, palette, sidebar
  yes; shortcuts, rail, bars no, because the actions toast their own failures); `DelegateAppCommand` itself does not catch (the
  runner does, a catch there would toast twice). The crash-log folder fills with one file per thrown command (pruned at 50); check
  that this is the wanted volume. `AttachmentSaver.WriteAsync` turns IO errors into a failed result (from the WIP commit e27dc3e).

## M3 plan (stream: m3-plan)
- **Uno.Sdk 7 move is on the critical path of stream editor-view** (`global.json` still pins Uno.Sdk 6.7.49 with SkiaSharp 3.119.2; `Barbican.Editor.Layout` needs SkiaSharp 4.156.0; `Directory.Packages.props` lines 53-62, ADR 0006 "M0 review"). The plan makes it step 0 of editor-view with a vendoring fallback (docs/milestones/M3-plan.md section 6.3). Review the move for a changed WinUI/Skia behaviour in the M1/M2 views. Paths: `global.json`, `src/Barbican.App/Barbican.App.csproj`.
- **Loopback TCP as the only single-instance mechanism** (D-241) replaces PORT-PLAN 3.9's `AppInstance` and Unix socket. Security review: the token file permissions and the greeting check (`src/Barbican.Core/Instance/`, contract stub only at this commit).
- **Contract stubs throw `PortPendingException`** until their stream lands; a default registration that throws is only ever reached if a stream forgets to replace it (`src/Barbican.Core/Compose/*.cs`, `SendPipelineRegistration.cs`).
- **Default decisions without a user question** (M3 plan section 9), most worth a look: send warnings and external-recipient confirm live in `Barbican.Core.Compose` (pure) with the dialog in the composer shell; DSN, Require TLS, Encrypt and Classification are deferred (I-258); mentions are built (G-075), the org disclaimer is not (I-268).

## M3 editor-view
- **Uno.Sdk 7.0.22 move (D-246)**: check M1/M2 views for a changed WinUI or Skia behaviour (fonts, popups, flyouts) and the suppressions (`CsWinRT1032`, `CA1416`) in `src/Barbican.App/Barbican.App.csproj` and `src/Barbican.Automation/Barbican.Automation.csproj`. `global.json`, `Directory.Packages.props`.
## M3 stream: editor-core
- **Importer swap (D-M3B-02).** `src/Barbican.Core/Editor/HtmlImporter.cs` replaces Rust's hand tokenizer with an iterative AngleSharp DOM walk that feeds the same builder logic. Review whether browser-style repair of misnested markup can change what a real reply or signature imports as (the ten fixtures in `tests/fixtures/editor/mail` and the six ported import tests agree; no real mail corpus was run). Also the text-preservation test only proves visible text, not marks.
- **Sanitising choices (D-M3B-05).** Scripting-scheme links and images are dropped on import, but `CleanQuotedHtml` and the quoted block's `Html` stay the raw (script/style/head stripped) original, exactly as Rust, and `Export` writes that HTML into the sent mail. Review whether the quote should also lose event-handler attributes and `iframe`/`object` before it is sent (`src/Barbican.Core/Editor/DocHtml.cs`, `HtmlExporter.cs` Quoted case).
- **Plain mode makes formatting commands no-ops (D-M3B-03).** Rust only switched the input rules off; B-RTE-020 and Bulwark say the commands are ignored. Check against the toolbar behaviour of the real composer in plain mode (`EditorSession.cs`).
- **Undo snapshots are whole-document clones** (300 steps). Fine for mail-sized bodies; a very large pasted document (tens of thousands of blocks) makes each step copy it. `EditorSession.Snapshot`.
- **Windows spell checker COM (D-M3B-10).** `src/Barbican.Platform.Windows/Spelling/*`: source-generated COM, hand-declared vtables (slots copied from `spellcheck.h` from memory of the header, verified only by the working `en-US` check on the dev machine: Check, Suggest). `Add` and `Ignore` are not exercised by a test (they would change the user's dictionary). `CoInitializeEx` result is ignored as in Rust. `WindowsSpellCheckerFactory` is registered in `src/Barbican.App/Composition/PlatformServices.cs` (not an editor-core path, one added line).
- **Shortcut table (D-M3B-07)** is TipTap's default keymaps written from the extension sources, not captured from a running Bulwark; compare against the real composer once (`EditorShortcuts.cs`).
- **Export is byte-compared to Rust only through the ported tests and the ten snapshots** that were generated by this implementation (`BARBICAN_SNAPSHOT=update`, `tests/fixtures/editor/export`), then read by hand against `html.rs`; the Rust binary was not run to produce them.

## M3 stream: send-pipeline

- **FakeJmap is a model of Stalwart, not Stalwart.** The held-submission release job, `forbiddenFrom`, HOLDFOR limits (7 days) and `stateMismatch` were written from the Stalwart sources and Bulwark, not observed live; the integrate stage must confirm them on the test account (`tests/Barbican.FakeJmap/FakeApi.SubmissionHold.cs`, `FakeApi.Metadata.cs`).
- **Send, undo and send now (D-M3A-02, -03, -04)**: `MailSender` (`src/Barbican.Core/Compose/MailSender.cs`, `SubmissionOps.cs`). Send now is cancel then resubmit; a race with the release is possible and reported as "already sent".
- **Fail-closed hooks (D-M3A-02)**: onComposeSend modelled as an intercept.
- **Cancel send leaves the copy when Drafts restore fails (D-M3A-05)**, a deviation from Rust.
- **Reply recipients follow Bulwark, not Rust (D-M3A-06)**: `ReplyRecipients.cs`.
- **Inline images of a reopened draft** are downloaded for display (24 images, 12 MB each at most): `ComposeSeeder.RestoreImagesAsync`.
- **Template metadata**: the Inbox carries the entries (`TemplateStore.cs`); size limits of a metadata entry (Bulwark MAX_METADATA_ENTRY_BYTES) are not checked client-side.
- **Additive contract changes**: `IDraftService.SaveAsync(draft, autosave, ct)`, `IScheduledSends.MaxDelayedSend`, `SendReceipt.Draft` and `EnvelopeRewritten`, `ToastRequest.SecondaryLabel/SecondaryAction` (the toast view must draw the second button), `IDocCodec` seam, FakeJmap options and a FakeMail.Cc/MoreTo/ReplyToEmail. AppComposition must call `AddBarbicanSendPipelineViewModels()`.
- **Requests to other streams**: composer-ui implements `IScheduleDialogHost`, renders `WarningsRaised`, the toast second button, and calls `SendCoordinator`/`TemplatePickerViewModel`; `IDraftService.SaveAsync(..., autosave: true, ...)` for the 2 s autosave.

- **Hidden TextBox IME path** (`src/Barbican.App/Views/Editor/EditorView.Input.cs`): composition events are wired and the sequence is tested with synthetic events only (`editor-ime`); the real IME (Windows and IBus/Fcitx) is unproven (G-091). The order of `TextChanged` and `TextCompositionEnded` on a real IME decides whether a commit could double; the code ignores changes while composing and clears the box on commit.
- **Clipboard** (`OsClipboardBackend.cs`): the WinRT clipboard is read as an async snapshot, then pasted synchronously. Never exercised against the real clipboard (hidden runs use `MemoryClipboardBackend`). HTML written without `HtmlFormatHelper` on the Uno head.
- **Text quality gate** (D-251): only dark theme at 100 % measured; check 125/150 % and light theme, and the Uno `SKCanvasElement` host on the VM.
- **Model gaps recorded as decisions** D-247 to D-249, D-252: mention chips, RTL direction, cell colour, image move undo.

## M4 plan (stream: m4-plan)
- **Webcal address check is stricter than Bulwark** (D-M4-07, G-006): LAN, localhost, link-local, unique-local, IPv4-mapped and credentialed addresses are refused, http is allowed with a warning. Review the rule list and that the fetcher repeats it on redirects and on resolved addresses (`src/Barbican.Core/Calendar/Feeds/FeedRules.cs`, contract stub only at this commit).
- **JSCalendar stays `JsonNode` and wall clocks stay `DateTime`** (D-M4-02): review that no stream introduces a typed event model that would drop unknown properties on a write. Paths: `src/Barbican.Core/Calendar/**`.
- **The .ics import drops alerts by default** (D-M4-09) where Bulwark keeps them; the prompt appears only when the file has alerts. `src/Barbican.Core/Calendar/Editing/ImportRules.cs`.
- **Contract stubs throw `PortPendingException`** until their stream lands; a default registration that throws is only reached if a stream forgets to replace it (`CalendarDataRegistration.cs`, `CalendarEditingRegistration.cs`, the six calls in `AppComposition.cs`).
- **FakeJmap calendar arms** (`tests/Barbican.FakeJmap/FakeApi.Calendar.cs`) are written from the draft and Stalwart's source, not recorded from a server: review that their error shapes match Stalwart before cal-edit's write arm is trusted (`Principal/getAvailability` answers from event participants only).

## M4 cal-data (stream: m4-cal-data)
- **Ical.Net is pinned 5.1.1** in `Directory.Packages.props` (test-only oracle for `RecurrenceOracleTests`). cal-edit may pin another version in the same file: on merge keep one version and re-run the oracle test (300 random rules); a version change may move the D-M4-A01 differences.
- **Reminder window and stale rules** (D-M4-A05, `src/Barbican.Core/Calendar/Reminders/ReminderRules*.cs`, `ReminderScheduler*.cs`): 35 day read window, 15 s check, 300 s refetch, acknowledged keys per login. Review the missed-while-closed logic (first check per run) and that a failing shared account never hides the user's own reminders. Tested on a virtual clock over FakeJmap only, never against a real server.
- **Scheduler test timing** (`ReminderSchedulerTests`): the timer test drains the background checks before each manual check because `FakeTimeProvider.Advance` fires every 15 s tick at once; the gate skips an overlapping check by design.
- **FakeJmap `after`/before filter** (`tests/Barbican.FakeJmap/FakeApi.Calendar.cs`) is written from Stalwart's calendar_event/query.rs, not recorded; a task with only a `due` counts at its due.
- **`timeZone` setting** (D-M4-A06, `CalendarSettings.Zone()`): relies on `TimeZoneInfo.FindSystemTimeZoneById` accepting IANA names (ICU on Windows; Linux not run, deferred to M9).
- **Not run: Linux** (the user deferred it to M9). The notification service and sound (B-CAL-035) are M8.

## M4 cal-edit step 3 (invitations, import, feeds, availability)
- **IcsReader** (`src/Barbican.Core/Calendar/Editing/IcsReader.cs`): a hand-written iCalendar reader instead of Ical.Net (D-M4-E01). Review its RRULE, EXDATE, TZID (Windows zone names pass through unchanged) and floating-time handling against real feeds; the server parse fallback is only exercised against the FakeJmap parse arm.
- **Feed fetching** (`Feeds/CalendarSubscriptions.cs`, D-M4-E03): redirects are not followed and plain http is refused by `GuardedHttp`, so the plan's "http warned" is a warning on a feed that then fails. Decide whether `GuardedResponse` should expose `Location` for a per-hop `FeedRules.Check`.
- **Subscription state** (D-M4-E02): kept in `calendar-subscriptions.json` with per-event hashes; nothing in cal-edit runs the refresh interval (no timer), check that cal-ui or cal-data owns it.
- **Invitation answers** (`Invitations/InvitationService.cs`, D-M4-E04): verified only against FakeJmap's `CalendarEvent/set` (it records `sendSchedulingMessages`, Stalwart's iMIP behaviour for a created-with-answer event is from its source, not from a live server). No optimistic store patch for answers; the store is refreshed after the call.
- **Availability** (`Editing/AvailabilityService.cs`): `Principal/query` filter by `email` and `Principal/getAvailability` argument names follow the draft and the fake; a real Stalwart (G-009) must confirm them before "Find a time" is trusted.

## M4 stream: cal-views
- Review `src/Barbican.App/Views/Calendar/**` (gestures, hover preview, print view) against Bulwark's calendar views: judgement calls are D-M4-C05..C10 in `spec/decisions.md`. Verified only through hidden automation on sample data (screens in `docs/milestones/M4-screens/`), never with a real pointer or on a real login; Linux untested (deferred to M9).
- `CalendarGestures.WriteAsync` now resumes on the caller's thread (`ConfigureAwait(true)`); the previous `false` threw a COMException in the toast and rollback path. Check the same pattern in cal-edit's `CalendarWriter` when merging.
- Perf probe numbers (5 000 tasks, 3 000 events): Agenda 21 ms, Month 16-35 ms, Week 68-85 ms, Day 16-41 ms: measures view model and layout, not the first paint of the ListView.

## M4 stream cal-ui
- **Typed date and time fields instead of pickers** (D-M4-D01): `src/Barbican.App/Views/Calendar/Editors/CalendarCardHost.Event.cs`. Review whether a picker is wanted; the card layout was never looked at (no calendar screen host on this branch, so no screenshot was taken).
- **App views compiled, never run**: `CalendarCardHost*.cs` and `Automation/CalendarUiAutomation.cs` build in Debug and Automation but no hidden run happened; cal-views places `CalendarCardHost` (DI singleton) over its grid.
- **Command and menu counts are constants** (`CalendarCommandSet.Count` 40, `CalendarMenus.Count` 6); composition tests assert them. Commit 16e4666 fixed an off-by-one (was 41).
- **Requests to other streams**: cal-views adds `CalendarCardHost` as the top child of the calendar screen and routes the rail Tasks entry to `TasksViewModel`; the Tasks list view and reminder popup views are not written (view models only).

## M5 plan (stream: m5-plan)
- **Hostile-input limits are chosen, not measured** (D-M5-03, G-012): 16 M characters a file, 10 000 cards, 1 M characters a line, about 8 MiB a photo, 500 entries a kind. Review the numbers against what a real phone export contains and that no path allocates more than a few times a limit (`src/Barbican.Core/Contacts/VCardLimits.cs`, `ContactPhoto.cs`; contract and red tests only at this commit).
- **FolkerKinzel is not used for vCard** (D-M5-05): a deviation from the PORT-PLAN wording; the hand-written mapping is pinned by Rust's tests. `src/Barbican.Core/Contacts/VCardParser.cs`.
- **The contacts FakeJmap arms are written from RFC 9610 and Stalwart's source, not recorded from a server** (`tests/Barbican.FakeJmap/FakeApi.Contacts.cs`, `FakeContacts.cs`): review that the error shapes (`requestTooLarge` over `maxObjectsInGet`, `invalidPatch` for a keywords pointer on a card without keywords) match Stalwart before con-data's write arm is trusted.
- **Permanent file deletes** (D-M5-10, D-7): the contract says the UI always asks; review that no `IFileWriter` path other than `PurgeAsync` and a delete inside the trash destroys (`src/Barbican.Core/Files/IFileWriter.cs`).
- **The shell seam** (`src/Barbican.App/Shell/IAppScreenFactory.cs`, D-M5-07): con-ui generalises ShellPage once; review that the Calendar host and the mail screen keep their state while another app is shown.
- **The two additive `MenuContext` members** (`Contact`, `File`, D-M5-08) are in the frozen M2 record; review that no later stream adds a third parallel member.
## M5 stream: files-data

- Review `src/Barbican.Core/Files/FileWriter.Trash.cs`: the trash is client-side (Stalwart keeps no deleted nodes), so a bug here loses user files. `TrashAsync` is a move into a dated batch and refuses (never destroys) what is already in the trash, in a shared account or the trash folder; `PurgeAsync` and `EmptyTrashAsync` are the only destroys. Check the group-by-parent batches (D-M5-C04) and the cleanup of an empty batch when a move failed. Verified against FakeJmap only (`FileWriterTests`); the live checks L-035 and L-036 are the integrate stage's.
- Review `FileWriter.Copy.cs` `CopyTreeAsync`: one `/set` per tree level, creation ids `c0..cn`, `Blob/copy` per level when the account differs (J-036 files part), a visited set and a 100 000 node cap against a looping or huge server tree. A failure half way leaves the part already copied (no rollback); the caller sees the error and the store is refreshed.
- Review `FileTransfer.Upload.cs`: conflict policy (D-M5-C06), `Replace` as an update of `blobId` (never delete and create), the create request deliberately not cancellable (a node is whole or absent), a blob left behind by a cancel (the server forgets unreferenced blobs; not verified live). Also the local walk (`ReadTree`): symlinks and reparse points skipped, depth 64, 100 000 entries.
- Review `FileTransfer.Zip.cs` and `FileNames.ZipSegment` / `LocalName` as the security surface of this stream (zip-slip, Windows device names, bidi override characters, a 255 unit limit). `FileNameTests` pins them; nobody has tried a real archive in an unzip tool.
- Review `FileStore.Push.cs`: a second event stream per login for `FileNode` (D-M5-C02). Backoff is `PushBackoff`; the stream is opened after the first `LoadAsync` and ended on `Dispose`. Untested live. If Stalwart limits event stream connections per user this should move into the mail engine.
- Deviation: the plan's test `Names_are_checked_against_the_server_rules_before_an_upload` was wrong ("console.txt" is 11 bytes, the limit in the test 10); changed to "cons.txt" (D-M5-C01). Bulwark `fileNameProblem` is the oracle.
- Deviation: `postExportAction` and `timeFormat` are in the plan's settings list for files-data but are not read by file transfers (D-M5-C07); `csharp_read_by` is set for the four filename keys only.
- `docs`/`spec` edits outside my rows: `FakeJmapOptions.Files`, two lines of `FakeApi.cs`, two lines of `FakeJmapServer.Routes.cs` (D-M5-C09). J-036 (`Blob/copy`) and G-044 stay as they are: the row is shared with con-data (photo blobs, contacts live), who moves it to `done`; the files side is covered by `FileWriterTests.Copy_goes_across_accounts_with_Blob_copy_in_both_directions`.
- Unproven (needs the integrate stage or a human): a real `<test-account>` run of upload, trash, restore, empty trash, share, copy across accounts (L-035, L-036); which FileNode shape the user's server answers (L-035 records it); the principal lookup for sharing (`ShareAsync` takes a principal id; finding it by e-mail or name is con-data's directory or files-ui, Rust `find_principal`).

### Requests to other streams

- files-ui: `FileEntry.MyRights` is the rights object of a shared node (`mayShare`, `mayAddChildren`, ...); `FileTrash.Items(entries)` and `FileTrash.Folder(entries)` are the trash listing and its folder; `FilePaths.Of(entries, key)` is the breadcrumb path (64 levels at most); `FileNames.CopyName` and `UniqueName` are the names the writer will choose, for a preview. `FileStore.DrainAsync()` and `Dispose()` exist on the class, not on `IFileStore`.
- integrator: `spec/decisions.md` has four streams appending after the row D-M5-19; keep every row when the textual conflict comes. `tests/Barbican.FakeJmap/FakeJmapOptions.cs`, `FakeApi.cs` and `FakeJmapServer.Routes.cs` each have a small additive edit of mine (D-M5-C09), next to con-data's contact lines.
## M5 stream: con-ui
- **The shell seam** (commit c01e61e, `src/Barbican.App/Shell/ShellPage.xaml`, `ShellPage.xaml.cs`, D-M5-07): `ApplyAppAvailability` adds and removes only the apps of the registered factories, so Calendar's own availability is untouched; `ShowAppScreen` builds a factory's screen on the first visit and then only hides it. Review that switching Mail, Calendar and Contacts keeps each screen's state and that a login without the capability falls back to Mail.
- **Contacts code was written against stubs**: con-data had not landed when this stream ended (`ContactFields`, `ContactDetail`, `VCardParser`, `ContactMerge`, `ContactPhoto`, the store, the writer, the lookup and the directory all throw `PortPendingException`). The view model, the commands and the dialogs catch it and show "not available yet"; the list, search, sort, menus, keys, group dialog, directory dialog and print were exercised (tests and a hidden sample run), but the contact form save, the import, the merge, the photo and the mail-side lookup were never run end to end. Re-run L-031, L-032, L-033 and the form after con-data merges: `ContactsFormModel`, `ContactsImportViewModel`, `ContactsMergeViewModel` in `src/Barbican.Presentation/Contacts/`.
- **Form entry rows follow Rust's `fields_of`** (sections email, phone, address, url, im, date, related; `Kind` and `Kind2` index `ContactOptions`): I read the section names from `contacts_x.rs:121`, not from con-data's code, which was not there. If con-data names a section differently the form rows show nothing for it (`ContactsFormModel.Sections`, `ContactsFormDialogContent.Entry`).
- **Patch on save** (D-M5-B11): `ContactsFormModel.Patch` sends whole top-level properties. A server that refuses a whole-property patch of a map (Stalwart refuses pointers into a missing map, not whole maps) was assumed fine; unverified against the FakeJmap set arm.
- **Additive edits to M1/M2/M3 files** (D-M5-19): `src/Barbican.App/Views/Dialogs/{IDialogContent.cs,DialogHostView.xaml,DialogHostView.xaml.cs}` (two default members, a named button row); `src/Barbican.App/Views/Context/ContextView.xaml(.cs)` (the contact card slot of the sender panel); `src/Barbican.App/Views/Compose/ComposerView.xaml.cs` (a Directory toggle beside Cc and Bcc); `src/Barbican.Presentation/Input/{KeyRouter.cs,ShortcutCommands.cs}` (Contacts uses the key tables like Calendar; `ShortcutCommands` takes the contacts view model), `Menus/MenusPresentationRegistration.cs` (passes it), `Menus/MessageMenus.cs` (View contact row), `Commands/CommandIds.cs` (`ViewContact`), `Palette/{PaletteModel.cs,PaletteCommandMap.cs}` (the ct:* rows run contacts commands); tests `PaletteControllerTests`, `MenuItemListTests`, `MenusCompositionTests`, `AppCompositionTests` (counts and one label).
- **Merge risk**: files-ui also edits `KeyRouter.cs` (the same `if (_shell.ActiveApp ...)` line) and `ShortcutCommands.cs` for the Files keys; `spec/decisions.md`, `docs/FABLE-REVIEW.md` and the registration counts in the three composition tests are appended by every stream.
- **Not built, by decision**: the reader name popover (I-204, the card is in the sender panel), tel: links (D-M5-B06), the activity tiles (D-M5-B09), Shift-click ranges and a real-pointer drop on categories (I-350, I-357).
- **Printing** (G-010, D-M5-12): the preview is drawn in the window and tested as a model; the OS path reuses `CalendarPrinter` and was never run. Needs a human with a printer.
- **Hidden-run sample data** (`src/Barbican.App/Views/Contacts/Automation/SampleContacts.cs`, AUTOMATION builds only): made-up people at example.org; screenshots in `docs/milestones/M5-screens/` show only these.

## webview-bg (stream: webview-bg)
- **Background release of the body WebView2** (D-WV-01): `src/Barbican.Presentation/Mail/Loading/BodyBackgroundPolicy.cs`, `src/Barbican.App/MailBody/BodyShell.Power.cs`, `src/Barbican.App/Shell/ShellLifecycle.cs`. Review: (1) the trigger is `Window.VisibilityChanged` plus the presenter's Minimized state; it was verified only through the hidden `body background|foreground` command, never with a real minimise (rule: no real window driving), so check on a real minimise and restore that the body resumes with no flash. (2) Hide is `Visibility = Collapsed` followed by a retried `TrySuspendAsync` (the first call throws ERROR_INVALID_STATE): confirm no better way exists to set `IsVisible` from the WinUI control. (3) The close step drops the shown message into `_restore` and re-sends it on re-create, bypassing the loader (OnReady/OnShown are not called again). (4) New settings-schema row `webviewBackgroundCloseMinutes` changes the verbatim-Rust-schema hash test (SettingsSchemaTests); no settings page renders it yet (M6). (5) Linux/WebKitGTK path is a no-op, not built or run.
## files-ui (stream: files-ui)
- **Partial stream: Presentation only.** `FilesListRules`, `FilesPreviewRules`, `FilesViewModel` (places, tree, breadcrumb, selection, search, sort) are done and tested against `FakeFileStore`/`FakeFileWriter`; no App views, menus, dialogs, preview, commands or screen factory yet (the shell seam and files-data had not landed on their branches when this ran). Review that the unfinished list in the milestone report matches (`src/Barbican.Presentation/Files/`, `tests/Barbican.Presentation.Tests/Files/`).
- **Rule judgement calls** (D-M5-D01, D-M5-D02): preview kinds beyond the test (audio, video, a short extension list) and the sort tie-break by natural name; the trash folder is dropped by name only when it is a directory. `src/Barbican.Presentation/Files/FilesListRules.cs`, `FilesPreviewRules.cs`.
- **View model reads `FileTrash.Items` for the trash place**, which is a files-data stub until its S1 lands (untested for that reason).
- **Files screen is a first cut** (commit after 94561ab): `src/Barbican.App/Views/Files/*` is code-built UI like CalendarScreen; checked only in a hidden sample-data run (list, grid, preview, delete and undo through automation), never with a real pointer, so selection with Ctrl/Shift (GetKeyStateForCurrentThread), drag and drop, inline rename and the OS file drop are unproven. `docs/milestones/m5-files-ui/*.png` are sample data.
- **Not done:** I-228 (reader attachment preview) and the onAttachmentPreview hook, marquee selection, Share rows, a settings page for `filesSettings` (only read), the details panel shows the selection only. Shared test edit: the three composition tests' expected counts in `tests/Barbican.Presentation.Tests/Composition/AppCompositionTests.cs` and `Menus/MenusCompositionTests.cs` now add `FilesCommands.Count` / `FilesMenus.Count` (merge conflict risk with con-ui, which will edit the same lines).
- **Sample store** `src/Barbican.App/Views/Files/Automation/SampleFiles.cs` implements IFileStore, IFileWriter and IFileTransfer in memory (AUTOMATION build, BARBICAN_SAMPLE=1 only); its trash has no dated batches.

## M5 stream: con-data
- **G-012, the hostile-input limits** (D-M5-03, D-M5-A03; `src/Barbican.Core/Contacts/VCardParser.Lines.cs`, `VCardParser.cs`, `ContactPhoto.cs`): review that no path allocates more than a few times a limit. The reader streams logical lines (no copy of the file); a line over `MaxLineChars` is cut, a photo line over `MaxPhotoChars` is dropped and counted, a card keeps at most 5 000 property lines and 500 entries a kind, a BEGIN inside a card starts it over. A picture is refused above 8 MiB and above 40 million pixels read from the header (`SKCodec`), but the full decode of an allowed picture (up to about 160 MB as RGBA) and the EXIF orientation (ignored, as in Rust) are not reviewed; the numbers are chosen, not measured. Tests: `VCardHostileInputTests`, the three hostile tests of `VCardParserTests`. L-034 on Linux is M9.
- **Export cannot forge properties** (D-M5-A04, `VCardWriter.cs` `Clean`): every CR and LF is removed from a line after escaping. Rust wrote addresses, numbers and URIs raw. Check that dropping the break (rather than refusing the card) is the right call.
- **Two Rust bugs fixed on the way** (D-M5-A06, D-M5-A07): editing the department lost every other unit of the organisation on each save, and a 29 February birthday in a common year had a negative "days until". Both are pinned in `ContactFormTests`.
- **`ContactFields.Build` returns a patch, not a card** (D-M5-A02, `ContactFields.Build.cs`): the properties the form owns, null for what the form emptied. con-ui must send it as the patch of an update, and drop the nulls for a create (`ContactWriter.CreateAsync` does). The contract text said "the card after the form"; Rust's `build_card` returned the patch too.
- **Push wiring** (`src/Barbican.Core/Sync/MailSync.cs`, `MailSync.Push.cs`, new `ContactPushTypes.cs`): the event stream also asks for `AddressBook` and `ContactCard` on a login with the contacts capability and the types go through the existing `ICalendarPush` relay (each store reads the types it knows). This is the one edit to M1 code and it changes the types asked of a real Stalwart; verified only against FakeJmap, where `PushHub` does not publish contact changes by itself, so the test sends a raw StateChange (`ContactStoreTests.The_engine_hands_a_contact_state_change_to_the_store_over_the_event_source`).
- **Shared accounts are not tested against FakeJmap**: `FakeJmapOptions` has `SharedCalendars` but no shared contacts and `FakeApi.Method` rejects another account id for contacts (frozen M4 files). The store and writer mirror `CalendarStore` (ids "account~id", `_acc`, `_rid`, `_shared`; a failing shared account is skipped), so review `ContactStore.Load.cs`, `ContactIds.cs` and `ContactWriter.Server.cs` by reading. A move between accounts is refused.
- **The directory is tested against a scripted client only** (`ContactScriptedClient`): FakeJmap has `Principal/query` for availability but no `Principal/get`. The request shape (a back-referenced get, `"#ids"`) follows RFC 9670 and Rust; confirm against the real server in the L-031 session. The fallback triggers only on `forbidden`.
- **Photos** (D-M5-A08, new `IContactPhotos`, `ContactPhotos.cs`): a new interface because adding members to the frozen `IContactWriter` would break con-ui's fakes. Upload is `MailWriter.UploadAsync` into the card's account; the data: URI fallback triggers on a SetError naming `media` or an `invalidPatch`. A photo that is a web link is never fetched (the address would tell its owner who looks); a blob download stops at 8 MiB. J-036 stays `partial(M5)`: files-data's cross-account copy and my photo blobs both need a FakeJmap test before the row can be `done`.
- **Hooks are fire and forget, intercepts are awaited** (`ContactWriter*.cs`, `ContactLookup.cs`): the 10 write hooks follow `CalendarWriter`; `MergeAsync` fires the update hooks for the kept card and the delete hooks for the others (it calls `UpdateAsync` and `DeleteAsync`), and a group created or deleted fires only `onContactGroupChange`, not the create or delete pair. Decide whether a group should fire the card hooks too.
- **`onProvideRecipientSuggestions` fires in `IContactLookup.SuggestAsync` only.** The composer's autocomplete reads `SuggestionSources` through `ContactRecipientSources` and filters with the pure `RecipientText.Suggest`, which has no query at load time and no hook. Plugins therefore shape the reader and address-menu lookups but not the composer's list until composer-ui calls the lookup or the hook itself.
- **`ImportAsync` result**: all created is `Ok(n)`; a card the server refuses makes the result a failure that carries the count (`Result.FailPartial`) after every chunk was tried; a call the server refuses as a whole stops it. con-ui must read `Value` of a failed result.
- **Requests to other streams**: none that block. For the integrator, when FakeJmap is next touched: a `SharedContacts` option and `PushHub.Attach(FakeContacts)` and a `Principal/get` arm would let the three untested paths above be tested. The shared `HookCallSiteTests.cs` got one added test (`Con_data_m5_hooks_have_call_sites`) next to the other streams' M5 tests: expect an adjacent-line merge conflict to resolve by keeping every test. `spec/gaps.csv` G-044 and `spec/jmap.csv` J-036 are rows files-data also edits.

## M5 integration pass
- **Photo wiring and the blob photo** (D-M5-I02; `ContactsFormModel.SaveAsync`, `ContactsDetailView.LoadBlobPhoto`): a picked photo is set through `IContactPhotos` after the write, so a failed upload after a successful card write leaves a card without its photo and shows the error; the list rows do not fetch blob photos (initials only). Review whether the second write should be folded into one call.
- **Data-loss guard in the form's patch** (D-M5-I01; `ContactsFormModel.Patch`): the old loop nulled every stored property the form result lacked (media, localizations, calendars, ...). It now removes only keywords, and media when no photo service is registered. Verify against a card with every JSContact property.
- **tel: links reverse D-M5-B06** (D-M5-I06; `BrowserLaunchRules`, `ContactsJson.TelUri`, both launchers): a click hands plain digits to whatever program owns tel: on the machine. Review whether that is acceptable for a client whose threat model includes a hostile contact card (the number is shown next to the link, validated to digits and a plus, 3 to 20 long).
- **Attachment preview** (D-M5-I08; `AttachmentSaver.ReadForPreviewAsync`, `AttachmentPreviewDialogContent`): the hook is a transform whose `Handled` flag is all the app reads; the plugin host (M6b) decides what else a taking-over plugin may return. The bytes are decoded by the platform image decoder (BitmapImage), the same exposure as the Files preview.
- **Cascaded overlap lanes** (D-M5-I05; `WeekLayout.Slot`): a card can now cover part of an earlier one; the hit test goes top-down in draw order (later lane first). Check drag and resize on the cascaded cards with a real pointer.
- **FakeJmap arms** (D-M5-I09): shared contact accounts, the directory and the contact push are fakes written from RFC 9610/9670 and Stalwart's source, not recorded from a server; the live checks L-031..L-036 are the proof.

## M6 plan (stream: m6-plan)
- **The shared seam and the in-memory fakes are the plan commit's** (`src/Barbican.Presentation/Settings/`, `src/Barbican.App/Settings/ISettingsPageFactory.cs`, `src/Barbican.App/Views/Settings/SetUiScreenFactory.cs`, `.../Settings/Sample/`): check that the fakes still match the real services' threading once set-data and srv-data land (D-M6-00; `SampleFakesThreadingTests` pins only the fakes, not the real classes), and that the minimal `SetUiScreen` was replaced, not left beside set-ui's.
- **`M6NamingTests` is a rule written as a test** (prefix per owned namespace, unique simple names): review whether the namespace table in it still equals M6-plan section 3.3 at the end of the milestone.
- **Composer-side hooks are one-line edits in M3 files** (D-M6-08): onIdentitySelect, onSignatureRender, onSelectReplyIdentity, onTemplateApply, onTemplateRender. Review that no behaviour was changed with them.
- **Account security is coded without a live write** (D-M6-09): the FakeJmap arms are written from Stalwart's source (`crates/jmap/src/registry/mapping/`) and Bulwark's store, not recorded from a server. Review the secret handling (never logged, issued once) and the shape of the set requests against the source.
- **`onSettingsSync` has its call site in import** (D-M6-A04): a deviation from the hook's catalogue meaning (settings sync between devices is not built).
- **43 red tests were written against stub signatures**: where a stream finds a signature wrong it files a request; review that the red tests were not weakened to pass (compare `tests/` at the plan commit).

## Codex — M6 coordination (parent)

- **Assistant service/UI integration:** review the adapter between the settings page and the real assistant configuration and credential store, confirmation dispatch to the in-window dialog, and coverage of the Rust tool set. The paused streams use separate settings stores and a partial tool set; passing pure tests alone does not prove the integrated behavior. Paths: `src/Barbican.Core/Settings/Assistant/`, `src/Barbican.App/Views/Settings/`, and the integration adapters added during the close pass. Integration commits and executed checks will be recorded as they land.
- **Evidence at takeover:** stream branches started at `c2d9652` (set-data), `a1e88a6` (set-ui), `b87f023` (srv-data), and `36efd7d` (srv-ui); integration branch started at `450ec55`. Some WIP commits removed red markers before tests ran. Require executed tests and inspected hidden sample captures before accepting completion. Linux remains deferred to M9; account-security writes remain FakeJmap-only.
## set-data (M6, paused WIP)
- Not compiling yet: AssistantJson.Push helper missing; analyzer fixes pending in AssistantClient, AssistantTools, SettingInfo. Review decisions D-M6-A (identity order key app.barbican.identityOrder, IAssistantKeys/Settings/Confirmation seams, edited FakeVacationAndSecurityArmsTests, FakeApi.cs, ReaderRegistration, ComposeSeeder).

## Codex — M6 set-data (Terra High)

- Review `src/Barbican.Core/Settings/Assistant/AssistantSession.cs`, `AssistantLoop.cs` (based on `c2d9652`): account histories now use per-account gates for ask/clear and deletion in automatic mode requires confirmation. Check cancellation, tool confirmation coverage, and account isolation under concurrent calls.
- Review `src/Barbican.Core/Settings/Assistant/AssistantTools.cs` and `SettingsDataRegistration.cs` (based on `c2d9652`): the Rust tool set is still incomplete; B-AI-008 is not proven. The Windows suite passed 2,642 tests with four live/Linux skips before subsequent tool work; no live write was performed.
- Review `tests/Barbican.FakeJmap/FakeApi.IdentitySet.cs`, `FakeApi.AccountSecurity.cs`, shared routing and `src/Barbican.App/Composition/Features/ReaderRegistration.cs` from `c2d9652`: coordinator approved integration of shared edits; check fake semantics and security data redaction against Stalwart.
- Review `spec/settings.csv` and `SettingsCatalog.cs` from `c2d9652`: rows labelled not ported have not yet all been reconciled against actual readers or marked inert.

- **Sol coordinator code-check scope so far:** inspected set-data checkpoint `d1f137c`, including `AssistantLoop.cs`, `AssistantSession.cs`, `AssistantTools.cs`, `AssistantClient.cs`, `AssistantSettings.cs`, `IAssistantKeys.cs`, `SettingsDataRegistration.cs`, the shared FakeJmap routing changes and `ReaderRegistration.cs`. Findings: automatic destructive tools needed confirmation; tool coverage was partial; UI settings and the client's configuration/key stores were disconnected. The worker added confirmation and session serialization; remaining tool work is still in progress. This is targeted integration checking, not a claim that every changed line has been reviewed.
- **Sol coordinator checks of UI/service seams:** inspected the paused set-ui assistant/security pages and srv-data `VacationService.cs`. Findings: credential deletion needed confirmation; background prefs updates reached older bound consumers without dispatch; vacation's calendar checkbox only saved a flag. The UI worker added confirmations, the server-data worker is implementing calendar lifecycle behavior, and the coordinator fixed background preference dispatch in `ShellViewModel`, `SidebarViewModel`, `MessageListViewModel` and `FilesViewModel`. Regression evidence: 963 Presentation tests pass; 44 Platform tests pass with one Linux-service skip. The real hidden Security page failure and all merged UI/service scenarios remain to verify.
- **Integration bridge review for Astra:** `src/Barbican.Platform/SecretAssistantKeys.cs` shares the settings page's profile-scoped credential entry; `src/Barbican.Presentation/Settings/Integration/AssistantDialogConfirmation.cs` dispatches confirmation and declines without a dialog host. Tests cover credential read/write/delete, sanitized write failures, dispatched acceptance/refusal, cancellation before opening, and the missing-host fallback. The coordinator added these after merging `d1f137c` as `08fe6f5`; actual provider calls remain unproven.
## Codex — M6 srv-data (Terra High)
- **Calendar block** (`src/Barbican.Core/Settings/Vacation/VacationCalendar.cs`, `VacationService.cs`, after `856f4f9`): review event ownership, UTC time conversion, metadata failure and multi-device races against Bulwark `lib/vacation-calendar.ts`. FakeJmap covers creation, update, future removal, started-event shortening, deleted-block preservation on load and a manually moved event; live server behavior, all-day zones, metadata failures and deletion across process restarts remain unproven (D-M6-C06).
- **Sieve and vacation writes** (`b87f023`, `src/Barbican.Core/Filters/Rules/FilterService.cs`, `src/Barbican.Core/Settings/Vacation/VacationService.cs`): inspect rollback, locked scripts, Sieve metadata and multi-step failure behavior against Bulwark and Stalwart; FakeJmap round trips do not prove real-server compatibility. Live checks are deferred to integration.
- **Folder metadata and template updates** (`b87f023`, `src/Barbican.Core/Settings/Folders/FolderAdminMetadata.cs`, `src/Barbican.Core/Settings/Templates/TemplateAdmin.cs`): review concurrent writes and interoperability, including the Barbican-owned pin key (D-M6-C07).
- **Hook payloads and threading** (`b87f023`, `src/Barbican.Core/Plugins/Payloads/`, `src/Barbican.Presentation/Compose/ComposerViewModel.Attachments.cs`): call-site tests establish names only; plugin payload compatibility and all event threads need deeper verification. The composer edit is shared with set-data.
### Codex — M6 set-ui (Terra High)

- Review the sample-only prefs adapter and theme threading in `src/Barbican.Presentation/Settings/Ui/SetUiPrefsValues.cs` and `src/Barbican.App/Composition/Features/SettingsUiRegistration.cs` (WIP `a1e88a6`): `Put` runs on the caller thread to avoid a shell redraw crash; this does not establish the D-M6-00 off-thread-write contract. Integrate the real data service and verify dispatcher handling.
- Review `src/Barbican.App/Views/Settings/SetUiAssistantPage.cs` (`a1e88a6`): its temporary `ai.*` prefs and global secret name do not match set-data's per-account settings and key APIs; wire and retest at integration before claiming a working real assistant. A sample run returned an answer; opening Security raised a caught exception, displayed as "Settings: No installed components were detected." in SetUiUi.GuardAsync. The precise exception origin remains unproven; a temporary stack capture produced no file.
- Review security confirmation and assistant account/cancellation updates in `src/Barbican.App/Views/Settings/SetUiSecurityPage.cs`, `src/Barbican.Presentation/Settings/Ui/SetUiAssistantViewModel.cs`, and `SetUiViewModel.cs` (this follow-up to `a1e88a6`). Deletion confirmation was not exercised by the hidden sample; security writes and real-account switching remain unproven.
- Inspect generic row behaviour and its coverage in `src/Barbican.App/Views/Settings/SetUiRowsView.cs` and `src/Barbican.Presentation/Settings/Ui/SetUiCatalog.cs` (`a1e88a6`): sample screenshots cover only a subset; empty or inert controls and cross-stream pages need integration verification. Linux and human interaction are deferred.

- Review `src/Barbican.App/Views/Settings/SetUiChoice.cs` and its call sites (this follow-up): in-window chips replace prohibited ComboBox popups; visual layout and keyboard accessibility need further verification.

## Codex - M6 assistant tools checkpoint

- Review `src/Barbican.Core/Settings/Assistant/AssistantTools.cs`, `AssistantLoop.cs`, `AssistantSession.cs`, `IAssistantComposer.cs`: new typed tool paths need integrated fake-server coverage for calendar, contacts, attachments, filter edits and send. Drafts require the coordinator's composer adapter; confirm reply prefill survives. Filter rules depend on the srv-data implementation; do not claim B-AI-008 complete until integrated. Core tests passed 1,426 on Windows, including fake-server tag and scripted composer/confirmation tests; Linux and live service behavior are unproven.

## Codex - M6 Sol integration code checks (second pass)

- **Checked server-data calendar step `89ce45b`:** compared `VacationCalendar.cs` and the `VacationService.cs` diff with `<bulwark>/lib/vacation-calendar.ts`, including ownership by marker/last-written times, update, future removal, started-block shortening, and externally deleted blocks. Inspected the four added FakeJmap lifecycle tests. UTC timed output, policy restrictions, metadata-write failures and shared calendars still need Astra's attention; tests do not prove those cases. Merged as `fe2118c`.
- **Checked assistant tool checkpoint `325dfcf`:** inspected the filter, calendar, contact, attachment, send/draft, confirmation and session paths. Requested fixes for omitted draft Cc/Bcc, writable account-scoped calendar selection and configured timezone; further service tests are in progress on the stream. B-AI-008 remains open until integrated execution evidence and the composer adapter pass. Merged as `6c37fa1`.
- **Security UI failure fixed by the coordinator:** `SetUiSecurityPage.cs` recreated row containers around controls still parented to old rows on every refresh. Reusing the complete rows removes the WinUI parenting exception. The hidden sample run on port 8929 opened Security, Account, then Security again; the automation tree contained the TOTP, app-password and API-key controls, and the inspected sample capture is `docs/milestones/M6-screens/integration/security-refreshed.png`. No credential was entered or issued in this check.
- **Assistant configuration and draft bridge:** the coordinator connected `SetUiAssistantPage.cs` to `IAssistantSettings` and `IAssistantKeys`, serializes field saves, and refreshes controls on account changes. `AssistantComposerBridge.cs` dispatches normal composer opening, detects a cancelled replacement, and applies reply prefills without sending or explicitly saving a draft. Its tests exercise dispatched opening and refusal, recipient fields and reply text, and the missing-host case. The complete Windows suite passed after these changes: 2,695 passed, four live/Linux skips, zero failed. Both Windows and desktop heads built on Windows; this is not Linux execution evidence.

## Codex - M6 srv-ui (Terra High)
- Review the server pages, dialogs and view models introduced in 36efd7d (`src/Barbican.App/Views/SettingsServer/`, `src/Barbican.Presentation/Settings/Server/`): sample-only hidden checks cover rendering, not a real account, real pointer or IME. Verify each interaction and service error path after data integration.
- Review threading in `SrvUiPageModel` and its six derived page view models: service results are dispatched before state mutation and discarded if the active account changed. The queued-dispatcher test covers filter load; real service events and overlapping same-account requests remain integration checks.
- Review the selection and text-entry tradeoffs in `SrvUiKit`, `SrvUiVacationPage`, and `SrvUiTemplatesPage` (D-M6-D04/D05): cycling choices require repeated presses, dates are typed, template import/export is pasted text and HTML is edited as source. Validate accessibility and parity.
- Requests to integrator: wire and test real data adapters and all six pages against merged services; inspect `spec/interactions.csv` and `spec/settings.csv` before claiming completion, as this stream has not asserted those rows. The plan's frozen settings seam is required to replace the stub screen.
- The sample-only screenshots in docs/milestones/M6-screens/srv-ui/ show the six pages and three settled dialogs from the Automation build before the dispatcher changes; repeat them after integration. A retry of the apply capture shows the underlying page and dialog in WGC; XAML capture omits the overlay. Identity name retained focus and Sample UserABC after three successive type commands. Draft setters are plain properties, so those changes do not themselves invalidate the page; a later service reload can still replace an in-progress draft.

- **Sol coordinator checks of srv-ui `1332306`:** inspected `SrvUiPageModel`, the filter/folder result paths, `SrvUiIdentitiesPage`, the settings-page factories and feature registration. The dispatched result helper drops a result for a different active account; same-account overlap/draft replacement still needs integration checks. Verified that identity draft setters are plain properties, so no per-keystroke PropertyChanged rebuild was established; the worker also exercised successive typing. Inspected synthetic identity, vacation and settled Apply captures.

## Codex - M6 set-data integration follow-up

- Review assistant tool changes after `325dfcf` in `src/Barbican.Core/Settings/Assistant/AssistantTools.cs`: account-filtered calendars, configured timezone, bounded attachment reads, filter rule validation and composer prefill. Fake-server tests cover ordinary paths; shared-account and task-only calendar selection remain limited because Stalwart does not expose supported components in Calendar/get (Bulwark `lib/jmap/client.ts:9075-9145`). The actual composer adapter and reply prefill need integrated UI verification.
- Review `tests/Barbican.FakeJmap/FakeApi.AccountSecurity.cs`: the request log now redacts OTP URLs as well as codes and passwords. Fake-only account tests cover identities, password, TOTP and credentials, but are not proof of real Stalwart security-write compatibility.
- Review `src/Barbican.Core/Settings/Pages/SettingsCatalog.cs`: the two mention and four filename preferences were removed from the inert list after checking their concrete readers; the UI owner is reconciling the remaining settings CSV.

- **Sol code check of `5ec4e27` found a calendar-rights bug:** `AssistantTools.CreateEventAsync` tested mailbox right `mayAddItems`; calendars use `mayWriteAll` / `mayWriteOwn`. The coordinator corrected the filter and extended the FakeJmap calendar test with a read-only default and writable fallback. All nine assistant-tool integration cases passed on Windows after the correction.

## Codex - M6 integration UI (Terra High)
- Review the Files row mapping and serialized read-modify-write in `SetUiRowRules` and `SetUiViewModel`; `FilesSettings` consumes the values, but `ShowThumbnails` currently has no thumbnail-rendering consumer. The test covers defaults, simultaneous edits, and invalid choices, not live Files rendering.
- Review accessibility reset in `SetUiRowsView`: it resets only keys exclusive to the accessibility page, skips shared keys, asks first, and stops on a refused write; it is sequential and can be partial if a plugin vetoes a later key. Cross-links navigate to target pages, with shortcut help using the existing command.
- Review `AvatarService.RefreshAsync`: it clears in-memory and profile-local image cache, discards pre-refresh jobs by generation, then notifies owners to re-query; guarded HTTP and source preferences remain in effect. A disk error reports failure and may leave a partial clear. Review concurrency and cache-directory trust.
- About refresh runs calendar and contacts refresh and requests mail delta sync for the active account. MailSync swallows its own errors, so the message says requested; this is not a full cache purge and cannot prove server freshness.
- Sample mode now keeps real `SettingValues` on the isolated profile, including validation and hooks. Assistant provider switching replaces only blank or recognized provider endpoints, preserving custom addresses and the selected model; the page requires explicit model and endpoint text.
- These changes use the existing native Settings rows and brushes; the newly approved Bento Balanced and Nordic Focus guidelines remain a broader UI adoption task, not a completed retheme.
- Account and Security pages clear drafts, lists, issued secrets and OTP URI on account switches; delayed service results are rejected using an account generation, and delete confirmation checks the account again. The delayed regression covers A-to-B-to-A for quota and issued app-password secret. Real security writes remain untested by policy.
- Assistant tool results for GetVacation and ListFilters contain server-controlled strings; review their prompt boundary and injection resistance alongside the existing email-data wrapping. No separate review was run.
- The hidden sweep reached 16 dark pages before Assistant initialization asserted on profile file IO on the UI thread; the data owner is fixing initialization. No screenshot after the crash is treated as evidence.
- Sol code check of `f0ca62f` found the avatar refresh lock could hold disk IO while `TryGet` waited. Follow-up separates the background-only disk lock from the short state lock, with disk-before-state lock ordering. The delayed-fetch regression verifies old work cannot repopulate the cache; a deliberately slow filesystem was not simulated.

- **Sol coordinator check of `f0ca62f`:** inspected account-generation guards and the delayed quota/secret tests, generic setting actions, avatar queue/generation behavior and cache clearing. Requested a separate disk-IO lock because the refresh lock was also used by UI `TryGet`/`Request`; the close worker is correcting that UI-stall risk. Same-account overlapping edits and incomplete cache purges remain review considerations.

## Codex - M6 settings concurrency (Terra High)

- Review `src/Barbican.Core/Settings/Prefs/SettingWriteGate.cs`, `SettingValues.cs`, `SettingBackup.cs`, `AssistantSettings.cs` and `src/Barbican.Presentation/Settings/Integration/AssistantSettingsStartup.cs`: the shared gate coordinates the M6 service paths and exports, but does not make the two-file import crash-atomic or cover legacy direct `IPrefsEditor` writers. Interceptors run outside the gate and retry up to three times on changed prior values; notifications run after release and may arrive out of application order under concurrent writes. PrefsStore itself invokes its synchronous Changed event inside the write call; a handler that synchronously waits on another gated write can still deadlock. Tests cover controlled import, retry, production DI and slow profile loading, not those legacy/reentrant paths.

## Codex - M6 assistant history loading (Terra High)

- Review `src/Barbican.Core/Settings/Assistant/AssistantSession.cs` and `IAssistantSession.cs`: `History` is memory-only and returns empty before `InitializeHistoryAsync`; load, ask and clear serialize per account, with file I/O outside the cache lock. The UI must call initialization on account/page changes and observe `Changed`; close integration owns that wiring. Tests cover slow reads, saved history reopening and clear during an in-flight ask; cancellation and I/O failure paths deserve further review.
