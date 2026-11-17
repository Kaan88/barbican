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

## M1 stream: m1-plan

Plan: `docs/milestones/M1-plan.md` on branch `m1` (this commit).
- **Branch naming deviates from CLAUDE.md** (`m<n>/<stream>`): stream branches are `m1-<key>`, because the
  integration branch `m1` exists and git cannot hold `refs/heads/m1` and `refs/heads/m1/<key>` together.
  Worktrees keep `<downloads>/wt-m1-<key>`. Update CLAUDE.md at the M1 gate or rename the integration
  branch in M2. Where: M1-plan §0.
- **Five streams instead of the suggested four:** a Sonnet `mailcore` stream (pure mail modules,
  ViewModels, ListSync, KeyRouter, hook bus) was split from the views so that views (wave 2) build on real
  ViewModels; the body cache sits with `accounts` (storage), not with `body`. Judgement call on load and
  dependencies; review whether `accounts` (Opus) is overloaded. Where: M1-plan §0, §6.
- **Contract skeleton written by a Sonnet stage (F2) from signatures in the plan.** The cross-stream
  interfaces of the JMAP layer are designed in the plan, not by the jmap stream; the jmap stream may only
  extend them during wave 1. Risk of a wrong model shape that mailcore builds on. Where: M1-plan §4, §9.1.
- **DI approach:** one frozen composition root (F1) calling one `Add<Feature>()` per stream, each in a file
  the stream owns; placeholders registered with `TryAdd`, completed with `Replace` (`IMessageOpener`).
  Review that registration order and `Replace` cannot silently keep a placeholder. Where: M1-plan §3.3.
- **F1 owns every csproj, the solution and `Directory.Packages.props`** and pre-creates the M1 projects
  (`Barbican.Platform.Windows/Linux`, `Barbican.Automation`, FakeJmap, Jmap.Live, Platform/Automation/Build
  tests) and package pins; later needs go into per-stream marked blocks. Review that nothing a stream needs
  is missing. Where: M1-plan §3.2.
- **I- rows tagged M1 overreach:** about 25 of the 55 are actions (menus, toolbar actions, contact data,
  plugin surfaces). The plan closes their display part and defers the action with a milestone. Review the
  table against INTERACTIONS and the catalogue tags. Where: M1-plan §2.3, `spec/interactions.csv`.
- **Live-check safety:** live read-only checks only through the ADR 0008 import, password-kind entries only;
  an imported OAuth refresh token is never refreshed by an agent (it would rotate and sign the Rust app
  out). ADR 0008 itself says the import "validates by a normal token refresh", which is unsafe while the
  Rust app is in use; the plan overrides it for agent runs. Security and privacy: an agent handles a real
  password in memory. Where: M1-plan §1 rule 3, §6.3.
- **OAuth in automation never opens a real browser:** `IBrowserLauncher` is faked in AUTOMATION builds and
  follows the authorize URL against FakeJmap. Real OAuth sign-in is a hand test only. Where: M1-plan §6.3.
- **Loopback `state` checked before `error`** (fixes FABLE #3) and a single accepted callback. Where:
  M1-plan §6.3.
- **Trace-diff against Rust is source-derived** (`spec/traces/M1/*.rust.txt` with file:line per call), not
  a live Rust trace: partial verification of the "trace-diff matching Rust" exit. Where: M1-plan §6.1, §9.3.
- **Feel gate uses the measured ~21 ms WGC quantum** instead of PORT-PLAN's "median ≤ 16 ms", until an
  on-screen cadence measurement (needs the user). Where: M1-plan §6.5, §9.2, ADR 0007.
- **Persistent SQLite cache deferred from M1 to M2** (PORT-PLAN §3.5, accepted default Q4); M1's cold-start
  gate uses the ≤ 1.5 s "without cache" budget. Needs the user's acknowledgement at the gate. Where:
  M1-plan §6.3, §8.
- **Lite drop (T-159) has no explicit user OK:** F2 writes a D- entry with "user OK pending" and the gate asks
  the user; `spec-check.py` may need to accept that status form. Where: M1-plan §2.1, §5 F2.
- **S2 harness re-run with the code-created `MenuFlyout` phase disabled** (it put a popup on the real
  screen in M0); the reconciliation of ADR 0005 vs 0007 on `MenuFlyout` stays open (needs a run that may
  draw on screen). Where: M1-plan §5 F1.
- **Hook catalogue count:** a quick regex over `<bulwark>/lib/plugin-hook-catalog.ts` finds 218 hooks in 27
  groups; PORT-PLAN says 219 hooks in 29 groups. F2 reconciles. Where: M1-plan §2.4.
- **Mark-as-read is never sent in M1** (read-only client); the `markAsReadDelay` logic is ported behind a
  disabled actions interface that M2 supplies. Where: M1-plan §6.5.
- **J-043 and J-044 set to n/a** with the Rust reasons (desktop client has no callback URL; EventSource is
  enough, Bulwark does not use WebSocket). Where: M1-plan §2.2.

## M1 stream: f1-foundation

Branch `m1-f1-foundation`, merged into `m1`. Report: the stage's hand-back (Uno 7, smoke, automation).
- **Uno 7 move deferred (fallback for now).** No Uno.Sdk 7.0.x was two weeks old (7.0.5: 2026-11-10,
  7.0.22: 2026-11-13); note the existing 6.7.49 pin is also from 2026-11-10, and the VM was unreachable,
  so the both-OS gate could not have closed. Retry after 2026-11-27, before wave 2 if possible; the
  SkiaSharp split stays until then. Review the judgement and that nothing in wave 1 deepens the Uno 6.7
  dependence. Where: ADR 0004 "M1 Uno 7".
- **Popup guard is reactive, not preventive.** It polls every 10 ms and around commands and closes
  unconstrained popups; a windowed popup can still draw for up to one tick on the user's screen if the
  App ever opens one off the private desktop. Prevention is the XAML lint plus the deny list. Review
  whether a WH_CBT/`WM_WINDOWPOSCHANGING` jail is worth it. Where: `src/Barbican.Automation/PopupGuard.cs`.
- **Deny list from measurements on a private desktop.** CommandBarFlyout opens outside the window even with
  `ShouldConstrainToRootBounds="True"`; ComboBox, ToolTip, AutoSuggestBox open outside; ContentDialog stays
  in. Pickers, SplitButton, DropDownButton, MenuBar, NumberBox were not probed and are denied. M2's menus
  and any tooltip design must live with this (custom in-window tooltip, MenuFlyout instead of
  CommandBarFlyout). Where: ADR 0007 "M1 F1", `Protocol/PopupControls.cs`, `PopupLintTests`.
- **Private desktop for risky probes.** `tools/auto-run.ps1 -PrivateDesktop` uses `CreateDesktop` +
  `CreateProcess(lpDesktop)` and never `SwitchDesktop`. A desktop is a session object that disappears with
  the App; judged not an OS change. Review that judgement and the launcher (handle lifetime, no handle
  inheritance unless `-CaptureOutput`). WGC does not work there, so frames are measured off-screen only.
- **Monitor check before showing the hidden window.** `HiddenWindow.Show` now throws when any monitor
  reaches (-12000,200)+size, before and after the move. Review the failure mode (the App crashes instead of
  showing). Where: `src/Barbican.App/Composition/HiddenWindow.cs`.
- **Release enforcement is static plus output checks, not an MSBuild evaluation.** Build.Tests parse the
  project files and the built `Barbican.dll` metadata; `build.ps1 publish` and `package-portable.ps1` scan the
  folder. A conditional reference through a new path (e.g. a `.props` import) would need a new test.
  Where: `tests/Barbican.Build.Tests/ReleaseContentTests.cs`, `tools/build.ps1` `Assert-NoAutomationContent`.
- **Sandbox deviation:** `allow-same-origin` kept (the shell needs the frame document), `allow-popups` and
  `allow-popups-to-escape-sandbox` dropped, against the plan text ("drop allow-same-origin as in Rust",
  which misreads Rust). Security review: same-origin frames plus hash-only scripts. Where: ADR 0005 "M1 F1",
  `Core/Mail/Body/MailSandbox.cs`, `Assets/mail/shell.js`.
- **CSP adds `base-uri 'none'`** (Bulwark) to the Rust policy; Rust differences kept (remote styles allowed
  with remote content on, no `media-src`, no `blob:`). T-176 ported. Where: `Core/Mail/Body/MailCsp.cs`.
- **`dim` is not accepted from the page** (plan listed it among messages; it is host-to-page). Where:
  `Core/Mail/Body/ShellMessage.cs`.
- **Link router is a logging placeholder;** a link click in a message does nothing visible in M1 until the
  reader stream adds the opener (with confirmation). Where: `App/MailBody/LoggingLinkRouter.cs`.
- **Smoke run has its own shell WebView** (a 64x64 WebView2 in the corner) until the body stream's host
  exists; replace it with the real body host's `shell` signal. Where: `App/Composition/SmokeRun.cs`.
- **ShellPage crash found by the smoke run:** `x:Double` tokens bound to Thickness/CornerRadius/GridLength
  crash WinUI (Uno accepted them). The placeholder applies them in code; the token generator should emit
  typed resources. Where: `App/Shell/ShellPage.xaml(.cs)`, ADR 0004 "M1 Uno 7".
- **`key` automation command is a stub** replying `error no key router yet`; the KeyRouter owner registers
  `key` as an `IAutomationCommand`. Where: `src/Barbican.Automation/CommandRouter.Ui.cs`.
- **MA0051 relaxed for `src/Barbican.Automation`** (command tables, spike port), like `spikes/`. Where:
  `.editorconfig`.
- **Automation WinUI build is `win-x64`** (Win2D needs a platform); the Automation library sets
  `Win2DWarnNoPlatform=false` and relies on the App's RID for the native DLL. Where: App and Automation csproj.
- **Profile folders:** hidden runs set `WEBVIEW2_USER_DATA_FOLDER` to `%LOCALAPPDATA%/Barbican-<profile>/WebView2`
  (profiles `smoke`, `auto-<port>`), never the user's default profile. Review against ADR 0008 profiles.
  Where: `App/Composition/HiddenWebView.cs`.
- **`IStartupTask` lives in `Presentation/Composition`** (so feature streams can implement it without the App).
- **Branch name:** `m1-f1-foundation` (the task text said `m1/f1`, which git cannot create next to `m1`).
