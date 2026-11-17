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

## M1 stream: f2-spec-tests

Branch `m1-f2-spec-tests` (the plan says `m1/f2`, but git cannot hold `m1` and `m1/f2` as refs at once). F2 wrote no
production behaviour: contract stubs, 74 red tests, the plugin catalogue. Every item below is for review at M1.

- **Red-test convention (new, review the mechanism).** `[Red("stream")]` (`tests/Shared/RedAttribute.cs`) gives the
  traits `Red=expected` and `RedStream=<stream>`. `tests/Directory.Build.props` sets MTP's
  `TestingPlatformCommandLineArguments` to `--filter-not-trait "Red=expected"`, so `tools/build.ps1 test` and
  `tools/linux.ps1 test` stay green; with `BARBICAN_RED=1` it is `--filter-trait "Red=expected"` and the run is red.
  `python tools/red-check.py` runs the red tests through `tools/build.ps1` and proves all 79 fail with
  `PortPendingException` ("Port pending: ...") or a call-site assertion, and reports one that passes ("remove [Red]").
  Why: no build-script change (F1 owns it), and a stream cannot silently turn a test green: red-check names it. Risk: if
  F1 or a later edit changes how `dotnet test` takes MTP arguments, the exclusion stops working (the default suite would then
  show 79 failures: loud, not silent). Where: `tests/Directory.Build.props`, `tests/Shared/RedAttribute.cs`,
  `tools/red-check.py`; verified on Windows (65 default tests pass, 79 red fail, 0 wrong reason).
- **Linux run of the red convention.** The Linux VM run of the default suite and of `BARBICAN_RED=1` is recorded in the
  F2 merge commit message. Where: `tools/linux.ps1 test`.
- **`PortPendingException` instead of `NotImplementedException`.** Meziantou MA0025 rejects `NotImplementedException`
  as an error under warnings-as-errors; a dedicated exception also lets red-check tell "stub not implemented" from any
  other failure. Streams delete the throw when they implement. Where: `src/Barbican.Core/Common/PortPendingException.cs`.
- **Contract deviations from M1-plan §4 forced by the analyzers (renames and suppressions; the integrator should know):**
  `IJmapClient.ReadOnly` is `IsReadOnly` (CA1716: `ReadOnly` is a VB keyword); `Result<T>.Ok/Fail` stay static on the generic
  type with one local `#pragma warning disable CA1000` and its reason; the BodyCache namespace is `Barbican.Core.Mail.Cache`
  (the folder stays `Mail/BodyCache`) because MA0049 forbids a type named like its namespace; the tag helpers are
  `TagRules` (namespace `Barbican.Core.Mail.Tags`, same MA0049); the keyword helpers are in `Barbican.Core.Jmap.KeywordSyntax`
  (a `Jmap.Keywords` namespace would hide the `Keywords` model type). Stub classes carry a local `CA1822` suppression with
  its reason where an instance member has no state yet. Where: the files named, `src/Barbican.Core/**`.
- **Contract additions (not in §4, needed so the 74 tests compile):** `ScheduleError : JmapError`; `EmailHeader`,
  `BodyValue`, `BodyPart`; `Keywords.Of(...)`; `ListDelta.Added` as `(string Id, int Index)` tuples; event records
  `MailboxesChangedEvent`, `ListDeltaEvent`, `ListResetEvent`, `EmailsChangedEvent`, `PushStateEvent`; `BodyCache` members
  beyond `IBodyCache` (list-id keyed `Put`, `PatchLocal`, `RemoveLocal`, `OnSync`, `Patch`, `Remove`, `DropThread`,
  `Count`, `Bytes`, `ThreadBytes`, `Weight`, `ThreadWeight`); `SettingsStore` (accounts) with `TryImport`/`Export`/`Reset`;
  `A11y`/`SystemAccessibility`; `ListSync`/`SyncStats`. The owning streams may refine them (add-only).
- **Tests ported with a changed shape (no assert dropped; review that the meaning held).**
  - Rust `serde_json::Value` inputs became typed records (`EmailSummary`, `EmailBody`, `ConversationThread`, `Keywords`) in
    the body cache, list delta, `was_unseen` and `inline_bytes` tests; the pure jmap functions on raw JSON
    (`parse_changes`, `apply_object_delta`, `split_calls`, `read_only`, quota, delayed send, SSE) use `JsonNode`/`JsonObject`
    because the C# typed layer sits above them. The identity JSON of `from_override...` is the `Identity` record.
  - `listsync::*`: the Slint `VecModel` is `ObservableCollection<T>`; "current model is not a `VecModel`" (the `ModelRc::default()`
    case of `sync_into`) is a null collection. `nested_rows_compare_by_content` is no longer about Slint (Slint's `==` on
    `ModelRc` compared identity); it is now a plain content comparison, so treat it as a regression test of
    `ListSync.RowsEqual` only.
  - `msgload::unread_state_comes_from_the_list_first`: `None` and `Some(Value::Null)` both become a null `Keywords` (a copy
    that is not an object is skipped either way), so the C# test cannot tell them apart. All five assertions are kept.
  - `mailview::forwarded_keys_parse`: Slint `Key::Control/Shift` vectors became a `KeyModifiers` flags enum, so order
    (`[Control, Shift]`) is no longer asserted. Escape and Delete keep their code points (U+001B, U+007F).
  - `jmap::from_override_header_identity_and_envelope`, `jmap::delayed_send_needs_futurerelease`: JSON and `Err(String)`
    results became `EmailAddress`, `SubmissionEnvelope` and `Result<long>` with a `ScheduleError`; the assertions are the same.
  - `attachments::thumbnails_are_square`: the image crate's PNG encoder is replaced by a small PNG writer in the test
    (`TestPng`); the mailcore stream picks the decoder (SkiaSharp is already pinned) and must keep Core free of UI types.
  - `settings::import_export_reset_roundtrip`: the `APPDATA` override becomes a throwaway `IProfilePaths`; the desktop file
    path is `<ConfigDir>/settings.json` as in Rust. The accounts stream decides where `prefs.json` lives relative to it.
  - `dark_mail::css_hides_until_the_pass_ran` reads `DarkMail.Script` (the Rust `include_str!` of `email_dark.js`): the body
    stream must embed that file so the two script assertions are checked against the real script.
  - `oauth::*`: the Rust file has two test modules; the seven tests keep their names and asserts, so four duplicate each other
    in meaning (`token_changed_retries_with_stored` = `rotated_elsewhere_retries_with_stored`, ...). Kept, because
    `spec/rust-tests.csv` has one row per Rust test.
- **Hook catalogue count.** The extractor finds **218 hooks in 27 groups** in `lib/plugin-hook-catalog.ts` (Bulwark 1.21.2),
  not 219 in 29 as PORT-PLAN M6b says (`HookGroup` in the file lists 27). 58 slots. PORT-PLAN M6b should read 218 and 27.
  Where: `spec/plugins/hooks.csv`, `tools/extract-plugin-catalogue.py`, `HookNamesTests.Catalogue_has_the_bulwark_count` (pins 218/27/58).
- **Milestone column choices (judgement calls).** 41 hooks are M1, 50 M2, 22 M3, 35 M4, 30 M5, 31 M6, 1 M6b, 3 M8, 2 M10, 3 n/a.
  Group defaults are in `tools/plugin_milestones.py`; per-hook overrides move out of M1 the hooks whose surface comes later:
  reply/forward interceptors (M3), external link and avatars (M2), folder create/rename/delete/empty (M2: context menus),
  the search-everything provider (M2), logout and account removal (M2: "sign out one or all"), column resize, text selection and
  drag and drop (M2), custom theme and locale (M10), the plugin OAuth callback (M6b). The three phone-layout hooks
  (`onViewChange`, `onMobileBack`, `onDeviceTypeChange`) are `n/a(...)`: the desktop app has one layout. Review both the M1 set
  (is any missing, for example `onBeforeExternalLink` if the reader opens a link in M1?) and the n/a set.
  All slot milestones are M6b (catalogued only).
- **M1 hook owners** follow M1-plan §2.4 by group. `HookCallSiteTests` has one red test per owner (jmap, accounts, mailcore,
  shell-ui, body) so each stream turns its own green; `HookNamesTests.Every_m1_hook_has_an_owner...` is green and guards the
  owner column. `tools/spec-check.py` now prints hooks per milestone with a "fired" count (a `HookNames.<Name>` under `src/`).
- **T-159 lite dropped: D-233, status `dropped(D-233, user OK pending)`.** The user confirms at the M1 gate. `spec-check.py`
  already accepted the status form (`startswith("dropped(D-")`); no regex change was needed. Where: `spec/decisions.md`,
  `spec/rust-tests.csv`, B-READ-046.
- **`MailBodyView` stub** (`src/Barbican.App/MailBody/MailBodyView.cs`) is an empty `UserControl`: it does not show the tile
  colour the plan describes (no token resource exists in the head yet); body or shell-ui adds it. It builds on both heads.
- **`IAutomationCommand` (plan §4.4, owner F1) is not here.** F1 was not merged when F2 ran; F1 creates `Barbican.Automation`.
- **`CommunityToolkit.Mvvm` is not referenced yet.** The ViewModel stubs are plain classes: adding the package reference is a
  csproj edit (F1/integrator owned). mailcore needs `ObservableObject`; request to the integrator: add the package to
  `src/Barbican.Presentation/Barbican.Presentation.csproj` (the version is pinned in `Directory.Packages.props`).
- **Test addresses moved to `*.example.*` subdomains (privacy check).** The Rust tests use `a.com`, `b.org`, `other.net`,
  `shop.com`, ... as domains; the privacy allowlist wants fictional domains, so `@a.com` became `@a.example.com`, `shop.com`
  `shop.example.com`, and so on, consistently inside each test (parent/child relations are unchanged, so every assert keeps
  its meaning). Bare domain strings that are not addresses (`Aligned("a.com", "b.a.com")`) are untouched.
- **No fixtures were needed.** None of the 74 Rust tests embeds a file; the one binary (the PNG) is built in the test.
  `tests/fixtures/<area>/` stays empty.

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

## M1 stream: mailcore

Branch `m1-mailcore` (worktree `wt-m1-mailcore`), commits listed in the final return. Windows: `tools/build.ps1 test` is green; the red set of this stream
(74 ported Rust tests plus the hook call-site test) is empty. **Linux: not run** (the VM did not answer on `tools/linux.ps1 ping`, as in F1 and F2); run
`tools/linux.ps1 test -Branch m1-mailcore` when it is back. The headless scenario is a test (`HeadlessScenarioTests`), so that run covers it.

- **`PrefsSnapshot` implemented by mailcore in an accounts-owned file** (`src/Barbican.Core/Storage/PrefsSnapshot.cs`). `A11y.From` (T-001..T-003) cannot
  pass without `FromJson`, `Str`, `Bool` and `Number`. Same semantics as Rust `Settings::{str,bool,int}`; `Bool` also accepts "true" and "false" strings (the
  stub's doc said so; Rust does not). Expect a merge conflict with `accounts`: keep accounts' version if it behaves the same.
- **Thumbnails: Core reads PNG only** (`Core/Mail/Attachments/PngDecoder.cs`, 8-bit, non-interlaced, a 40 MP cap against decompression bombs). The Rust app
  used the `image` crate (JPEG, GIF, WebP and BMP too). Other formats return null (the card shows its kind) until the App registers a decoder with
  `ThumbnailDecoders.Register` (SkiaSharp is already in the App). Chosen over adding ImageSharp (Six Labors split licence) to a public repo. The 200-entry
  session thumbnail cache (B-MAIL-021) is not here: it goes with the fetch (body stream). Review the PNG reader on hostile input.
- **`LoadedList.Apply(ListDelta)` is a local copy of `ListDeltaApplier.Apply`** (jmap's stub throws `PortPending` in wave 1, and the exit test needs a push
  against fakes). Same algorithm as Rust `apply_list_delta` (`Presentation/Mail/LoadedList.cs`). After the jmap merge either keep it (it also owns the cursor
  and the keyword patch) or delegate; the two must not drift.
- **Unread-first Inbox is a local partition of the loaded rows** (`ListLayout.Build`), not Rust's two queries (`fetch_unread_first`: newest N unread, then the
  first page of read ones). `ListQuery` has no unread filter and adding one is jmap's file. Difference: an unread message older than the loaded page is not in
  the Unread section; unread messages beyond N go to "Everything else" instead of being hidden. Active only with `interfaceLayout = mountain-view` and
  `mvInboxType = unread-first`, in the Inbox, without a search (as Rust). Needs `ListQuery.UnreadSection` from jmap to be exact.
- **`MailEventHub` also runs `IMailSync.RunAsync` for the active account** (`Presentation/Mail/MailEventHub.cs`): the plan never says who starts the engine.
  If accounts' session also starts it there will be two loops; one must go, and the hub is the one that follows account switches.
- **Arrow keys are handled for every source**, including the real keyboard (`KeyRouter`), because the brief lists "j/k/arrows". Rust left plain arrows to the
  Slint list (B-KEY-008 "Plain arrows pass"). In XAML a focused `ListView` also moves on arrows: shell-ui must mark the key handled from `Handle`'s result or
  route only WebView and automation arrows here. Review the double-move risk on the first hand test.
- **Escape clears the search first, then closes the reader.** `g` then `s` goes to a mailbox with the role `flagged`; Stalwart has none, so it does nothing
  there (Rust opened a virtual Starred list). Ctrl+E focuses search; Ctrl+F semantics (forward when a message is selected) are M2 (B-KEY-011).
- **The `key` automation command is not registered.** It needs `Barbican.Automation`, which only the Automation configuration references, so it has to live in
  the App. `Presentation/Input/KeyChordParser.cs` parses the chord (tests) and `IKeyRouter.Handle(chord, KeySource.Automation)` does the rest. shell-ui or the
  integrator adds the `IAutomationCommand` named `key` (it overrides F1's stub).
- **Hook call sites:** 26 hooks of mail-list, mail-mailboxes (display), mail-search, navigation, keyboard, app and ui; `spec/plugins/hooks.csv` has
  `cs_call_site` and `status=done` for them. Judgement calls: payloads are my records in `Core/Plugins/Payloads/` (Bulwark's are TS object literals; for example
  `onNavigate`'s `RouteContext` is just `{route}` here); observers are fire-and-forget on the thread pool (`HookBusExtensions.Fire`); an interceptor can only
  stop a key press when its answer is already available (`KeyRouter` cannot await on the UI thread); a handler that throws is swallowed and reported through
  `HookBus.HandlerFailed` (Bulwark logs it). M6b must decide whether the real hosts want async interception on the key path. `onEmailListItemRender` runs per
  visible row (first 50) after a rebuild, only when someone listens. `onInterval` is a 60 s timer in `MailShellStartup`. The window events (`onWindowFocus`,
  `onVisibilityChange`, `onOnline`, `onBeforeUnload`, `onAppError`) are methods on `ShellViewModel` that the App window must call (shell-ui): no real window
  calls them yet.
- **The zero-allocation claim is measured** by `HookBusTests.Without_handlers_nothing_listens_and_a_call_allocates_nothing` (under 1 KB for 10 000 calls of all
  three kinds, `GC.GetAllocatedBytesForCurrentThread`). The first version used an `ImmutableDictionary` and allocated about 400 bytes per call; the bus now swaps a
  plain dictionary copy-on-write.
- **ListSync resets once only on `BulkObservableCollection<T>`.** On any other `IList<T>` a reset is `Clear` plus `n` adds. Every ViewModel collection is a
  `BulkObservableCollection`; a view must not wrap them in a copy. The ported test `empty_and_clear` accepts either (as Rust did).
- **The Presentation test project got a package reference** (`Microsoft.Extensions.TimeProvider.Testing`, already pinned) in
  `tests/Barbican.Presentation.Tests/Barbican.Presentation.Tests.csproj`: a csproj the plan reserves for the integrator. One line, marked `m1-mailcore`.
- **The flagged tile asks `IMailService.QueryAsync` with the text filter `is:starred`.** Rust used a custom `Email/query` with `hasKeyword $flagged`. This works
  only if jmap's text filter understands `is:starred`; otherwise the tile stays empty. The calendar and task tiles are M4 (`GlanceTilesViewModel.SetCalendar`).
- **Not ported in M1 (scope):** one-time code chips on rows (`showVerificationCodes`, Rust `otp.rs`), row tint by tag or account, the unified inbox and Scheduled
  folders, sender pictures, clicking a tag to filter (needs a keyword filter in `ListQuery`; I-059 is M2), the multi-account unread badge
  (`unreadBadgeAllAccounts`). Row text is English only (`RowText`, `ListDateGroup.Label`): Bulwark's `date_groups.*` en strings and English day and month names.
- **Date groups follow Bulwark (`lib/list-date-groups.ts`), not Rust's `group_label`** (Today, Yesterday, weekday, then month names). The brief names Bulwark's
  file. A user used to the Rust grouping will see Last week, Last month and Older instead.
- **`A11y.FontScaleFactor`, `TextScale` and `MinFontPx` are `double`** where Rust had `f32`; the CSS and script text are identical for the values the tests use
  (`1.25`, `16`), a value like `0.1+0.2` would print differently. Low risk.
- **`ShellViewModel` reads two settings the plan does not name:** `uiStyle` (`bento` or `nordic`, Rust `main.rs:2093`) and `theme` (`light`, `dark`, `system`,
  Bulwark theme-store). The platform layer must call `ShellViewModel.SetSystemDark` for "system".
- **The header-only `IMessageOpener` never marks a message read** (M1 is read-only; the body stream's loader decides, behind the account guard).
- **Reader `Attachments` are indexed by position in the visible list** (inline images hidden), as Rust `show` did; the body stream's download must use
  `AttachmentCard.BlobId`, not an index into `EmailBody.Attachments`.

### mailcore review (reviewer pass)

- **Shift+G is refresh, not "select last message".** The plan and brief listed "Shift+G" among the M1 list keys and the implementer bound it to "last message". Bulwark
  `lib/keyboard-shortcuts/mail.ts:34` (`Shift + G` = `shortcuts.global.refresh`) and Rust `shortcuts.rs:750` ("Shift+G stays refresh") both make it a refresh. Rebound in
  `Presentation/Input/KeyRouter.cs` to `MessageListViewModel.ReloadAsync` (rows stay until the page is ready); `SelectLast` stays as a method with no key. Review whether
  the plan's wording meant something else. Commit in the final return.
- **A `ListDeltaEvent` that arrives while a first page is loading (or with no list open) is dropped** (`MessageListViewModel.OnSyncEvent`). The event carries no query identity,
  so a delta computed for the folder being left could be applied to the new folder's rows. Dropping is safe because the page being loaded is current. The proper fix is a
  query id on `ListDeltaEvent` (jmap's file, additive); until then `EmailsChangedEvent` is still applied while loading (only fresh copies of ids already shown).
- **`KeyRouter.Handle` ignores `KeySource`.** A printable key typed into the search box or the composer must not reach `Handle`; shell-ui has to filter on the focused element
  before calling it (j in the search box would move the selection). No test can cover this until the real key handler exists: hand-test it.
- **Linux VM unreachable again** during this review (`tools/linux.ps1 ping`: key not authorized); Linux build/test of this stream is still unverified.
- **PNG reader memory:** a 40 MP PNG allocates about 360 MB transiently (raw plus RGBA). Left as is (Rust's `image` crate has a comparable limit); lower `MaxPixels` if the
  first hand test shows pressure.

## M1 stream: jmap

Branch `m1-jmap` (from `m1` at fa7cc17; commits 6b9d58f, 088b7d3, af867f4, 93e69db, 828c2cc, 8a46011, 847aeac and the
Fable commit), not merged. Transport, session, request builder, mail service, sync and push engine, FakeJmap, trace-diff.
- **`using` sends every capability the server advertised** (Rust jmap.rs:283, B-JMAP-006), where Bulwark sends core+mail
  (lib/jmap/client.ts:2466). Kept Rust's choice; an unknown capability in a hand-built `using` is a problem error. Review
  whether a server that rejects an advertised-but-unexpected capability is worth guarding. Where: `Core/Jmap/Transport/JmapClient.cs`.
- **Manual redirect policy for session discovery.** Redirects are followed by hand (at most 5 hops), the Authorization
  header goes only to same-origin hops, https to http is refused. Rust let reqwest follow redirects (which drops the header
  cross-origin too). Review the hop limit and that a Stalwart behind a proxy that redirects to another host still works
  (it asks for sign-in again rather than leaking the password). Where: `Core/Jmap/Transport/SessionFetcher.cs`.
- **402 with TOTP/MFA in the problem = `OtpRequiredError`** (Bulwark client.ts:2165); `password$code` as in Rust jmap.rs:125.
  The fake mimics Stalwart's shape from Bulwark's handling, not from a recorded Stalwart response. Where: `SessionFetcher.cs`,
  `tests/Barbican.FakeJmap/FakeJmapServer.cs` (`Otp`).
- **Contract additions outside the F2 stubs** (all additive): `ListCursor.Position`, `IMailSync.Watch(query, loaded)` (default
  forwards to `Watch(query)`), `IJmapClient.Trace` and `OpenEventSourceAsync` (default members), `[JsonExtensionData] Extra`
  made settable (STJ cannot bind it on init-only records), `EmailBody` gained SentAt, Size, HasAttachment, Sender, MessageId,
  InReplyTo, References; `EmailSummary.ThreadCount` is `[JsonIgnore]`. mailcore and body consume these; review the shapes.
- **Keywords and MailboxRole converters read JSON null** (`HandleNull`): a null role is an ordinary folder, null keywords none.
- **Hooks are awaited inline** in the sync run (onSyncStateChange, onNewEmailReceived) and on connect/drop
  (onPushConnectionChange): a slow plugin delays the next step of that run. Bulwark emits without awaiting
  (`void emailHooks...emit`). Review against the hook bus's own timeout once mailcore's HookBus lands.
- **onNewEmailReceived payload is the `EmailSummary` model**, inbox only and never a draft (Bulwark stores/email-store.ts:1346).
  Bulwark keeps its own Email/changes baseline for plugins; here it rides the Rust new-mail check (unread, newest 10, the
  first run seeds), so a message read on another device before the check runs is not reported. Deviation to review.
  Where: `Core/Sync/MailSync.cs`, `NewMailWatcher.cs`.
- **The new-mail check ignores notification prefs** (`InboxOnly` is a switch for the notifier stream to set). Where: `NewMailWatcher.cs`.
- **ListResetEvent leaves the re-read to the view**: when no delta can be had the engine drops the list states and emits
  `ListResetEvent`; the list view must query again and call `Watch(query, page)`, or later pushes keep resetting. Where:
  `MailSync.cs` `SyncListAsync`; mailcore's `MessageListViewModel`.
- **700 ms coalesce is an `ITimer` debounce** (each push restarts it), like Rust main.rs:538; a steady stream of pushes under
  700 ms apart would postpone the sync indefinitely. Rust has the same property. Where: `MailSync.Push.cs` `Schedule`.
- **Session kept as `JsonElement`/JsonObject inside `JmapSession`** for capability reads (`AccountCapability<T>` via the
  source-generated context). AOT-safe, but review the shape before M3 adds submission and M4 calendars.
- **J-051 collapseThreads stays M2** (Rust and Bulwark list messages and show the thread size).
- **J-015 creation ids deferred to M3** (`JmapRequestBuilder.CreationRef` exists; first real use is EmailSubmission/set).
- **trace-diff is source-derived** (PORT-PLAN §4.6, M1-plan risk 3): `spec/traces/M1/*.rust.txt` were written by hand from
  `desktop/src` at rust-final with file:line per line. Glance tiles, tag counts, body prefetch and calendar calls are marked
  optional (`~`), because they belong to other streams or milestones. A live Rust or Bulwark trace is a hand test. Review the
  derivation, especially push: one Mailbox/get for the counts-only fetch (jmap.rs:465). Where: `tools/trace-diff.py`,
  `tests/Barbican.Core.Tests/Sync/TraceScenarioTests.cs`.
- **Live checks are hand tests.** `tests/Barbican.Jmap.Live` (session, folders, a Sent page and its delta, read-only
  client) is skipped unless `BARBICAN_LIVE=1` and `BARBICAN_LIVE_SERVER`/`_USER`/`_SECRET` are supplied by the Verify stage
  or the user from a password-kind ADR 0008 import. No agent ran it: the import is the accounts stream's and is not on `m1`.
- **Coverage (Microsoft.Testing.Extensions.CodeCoverage, cobertura):** Jmap 93.2%, Sync 90.7%, Mail/Service 94.1%,
  Mail/Model 100%. Uncovered: mostly cancellation and IO-exception branches in `JmapClient` and `EventSourceReader`.
- **Files edited outside my owned paths:** `Directory.Packages.props` (an `m1-jmap` block: CodeCoverage 18.12.0),
  `tests/Barbican.Core.Tests/Barbican.Core.Tests.csproj` (FakeJmap reference, TimeProvider.Testing, CodeCoverage),
  `spec/plugins/hooks.csv` and `HookCallSiteTests.cs` (the three jmap hook rows and the `[Red("jmap")]` removal, as the
  brief asked), `spec/jmap.csv`, `spec/behaviours/jmap.md`, `sync.md` (B-SYNC-036, 037 only).
- **Linux VM unreachable** again (ssh connect timed out, 2026-11-17); green on Windows only (`tools/build.ps1 test`:
  432 tests, 429 passed, 3 live skipped). The Linux run is open.

### Requests to other streams
- **mailcore:** register the real `HookBus` in DI. `AddBarbicanCore` registers `NullHookBus.Instance` with `TryAdd`, so
  your registration must come first or replace it. MailSync calls only `HasHandlers` and `NotifyAsync`.
- **integrator:** own the `Barbican.Core.Tests.csproj` and `Directory.Packages.props` edits above at the merge.
- **integrator / F2 owner:** `tools/spec-check.py:119` has a literal backspace byte (0x08) in the hook regex
  (`r"<BS>HookNames\.(\w+)"`, probably a `\b` written through a non-raw string), so "fired" is 0 for every milestone.
  Replace it with `\b` or drop it. Not fixed here (not my file).
- **accounts:** the live suite reads its secret from the environment for now; once the dev-profile import and the
  secret store are on `m1`, switch `tests/Barbican.Jmap.Live/LiveAccount.cs` to read the imported password-kind entry.

### Review (Opus reviewer of the jmap stream)
Read the whole diff `origin/m1...m1-jmap` against Rust `desktop/src` (jmap.rs, mail.rs, main.rs), the plan and the spec rows.
The transport, session, list paging, 7-call delta, Mailbox/changes merge, SSE parser, backoff and trace are faithful ports;
citations spot-checked (jmap.rs:73/104/139/228/246/272/406/440/465/524/592/1108/1146, main.rs:486/499/538-547/560-578/2015-2058,
mail.rs:34/254-292/759-772 all point at the code they name).
- **Fixed: a sync cancelled while it waited for the gate left `onSyncStateChange` stuck at "syncing".** `SyncNowAsync` took
  the gate outside its `try`, so a cancellation skipped `EndRunAsync` and the overlapping-run counter never fell back to 0;
  every later run then reported neither "syncing" nor "idle". Now the gate and the "syncing" hook are inside the `try`.
  Test `MailSyncTests.A_run_cancelled_before_it_starts_still_ends_the_syncing_cycle`. Where: `Core/Sync/MailSync.cs`.
- **Fixed: `BARBICAN_JMAP_TRACE` pointing at a folder that does not exist failed every request** (the `File.AppendAllText`
  exception escaped the trace sink into `SendAsync`). The trace is best effort now. Test
  `MailSyncTests.A_trace_file_that_cannot_be_written_never_fails_the_caller`. Where: `Core/Jmap/Transport/JmapTrace.cs`.
- **Fixed: the event-stream `Stream` leaked when the connect hook threw before reading began** (only the reader disposed it).
  Where: `Core/Sync/MailSync.Push.cs` `PushOnceAsync`.
- **Thin rows to look at:** J-009 (maxObjectsInSet) is "done" on a test that only reads the limit (M1 has no /set; the
  chunking is M2's), J-028 (upToId) is "done" as "not sent, whole delta applied" (Rust n.a.). Both defensible, neither
  exercises behaviour. Where: `spec/jmap.csv`.
- **No push = no live updates at all.** A server without `eventSourceUrl` makes `RunAsync` retry the open forever on the
  backoff (5 min cap) and nothing else syncs; Rust behaves the same. Stalwart always has push, so M1 is unaffected; a
  polling fallback is a later decision. Where: `MailSync.Push.cs` `RunAsync`.
- **One delta is emitted twice to the list view**: `ListDeltaEvent` and `EmailsChangedEvent` for the same delta, and
  mailcore's `MessageListViewModel` applies both (idempotent, two rebuilds). The second exists for the body cache
  (Rust bodycache on_sync). Review whether the list view should skip `EmailsChangedEvent` right after a `ListDeltaEvent`.
- **Merge into `m1` (after mailcore), integration fixes in the merge commit:**
  - **Two hook buses.** `AddBarbicanCore` runs first in `AppComposition` and registered `NullHookBus` with `TryAdd`, so
    mailcore's `TryAdd<IHookBus>(HookBus)` was ignored: the sync engine and the ViewModels got the null bus while plugins
    (tests today, M6b) register on `HookBus`. The jmap module now registers the real `HookBus` the same way mailcore does
    (either order gives one bus). `NullHookBus` stays as a test double. Tests: `WiringTests.The_hook_bus_is_the_real_one_that_plugins_register_on`,
    Presentation `RegistrationTests.The_jmap_module_first_and_mailcore_second_still_share_one_hook_bus`.
    Where: `Core/Jmap/JmapRegistration.cs`.
  - **Every push reset the list.** `MessageListViewModel` called `Watch(query)` (the F2 contract), which keeps no states,
    so the engine answered every Email push with `ListResetEvent` and a full reload: the opposite of the "open, push,
    delta, no reset" exit test, which only held inside Core. It now calls `Watch(query, page)`. The view also drops a
    `ListDeltaEvent`/`ListResetEvent` whose `Query` is not the list it shows. Tests:
    `MessageListViewModelTests.Opening_a_folder_shows_the_first_page_under_a_date_header` (asserts the page is handed over),
    `A_delta_or_reset_of_another_watched_list_is_ignored`. Where: `Presentation/Mail/MessageListViewModel.cs` (mailcore's file,
    changed by the integrator as the jmap Fable section asked).
  - **`tools/spec-check.py` hook count fixed** (the 0x08 byte in the regex the jmap section reported): M1 now shows 29 of
    41 hooks fired; the 12 open ones are body, accounts and shell-ui rows of later waves.
  - **Mailcore keeps its own `LoadedList.Apply`** (a copy of `ListDeltaApplier` written before jmap landed). Two
    implementations of RFC 8620 §5.6 now exist; review whether `LoadedList` should call `Core/Sync/ListDeltaApplier`.
  - **Linux not run for this merge either:** the VM answers ping but SSH (port 22) timed out on 2026-11-17; Windows
    `tools/build.ps1 test` is green (564 tests, 561 passed, 3 live skipped). Run `tools/linux.ps1 test` on `m1` when the VM is back.

## M1 stream: shell-ui
- **Linux not run.** `tools/linux.ps1 ping` fails (key not authorized or host down, 2026-11-17). Only the `net10.0-desktop` head was compiled on Windows (`tools/build.ps1 build -Head desktop`, 0 errors); no Uno run, no `xrun`. Run `tools/linux.ps1 test -Branch m1-shell-ui` and an `xrun ... -Script auto` when the VM is back. The Uno-only gaps found by that compile are fixed (below).
- **Uno0001 members written with the `win:` prefix** (`src/Barbican.App/Views/{Rail,List,Sidebar}/*.xaml`): `ShouldConstrainToRootBounds`, `ItemContainerTransitions`, `ShowsScrollingPlaceholders` are not implemented in Uno and Uno0001 is an error here. I did not verify that the `win:` prefix on a **property element** (`<win:ListView.ItemContainerTransitions>`) and on attributes is ignored by Uno's generator at run time (it compiles). Risk: on Linux a flyout is not told to constrain (Skia draws popups inside the single window anyway; unverified).
- **Popup lint changed in F1's test** (`tests/Barbican.Automation.Tests/PopupLintTests.cs`): it now accepts a `win:`-prefixed `ShouldConstrainToRootBounds`. Review that this did not weaken the rule (the value must still be `True`).
- **Test project edits outside my folders:** `tests/Barbican.Presentation.Tests/Barbican.Presentation.Tests.csproj` links five UI-free App files (PaletteWriter, ShellLayout, StyleMetrics, MotionScale, KeyChordMapper) instead of a new `Barbican.App.Tests` project (which would need a WinUI test host). `tests/Barbican.Core.Tests/Plugins/HookCallSiteTests.cs`: `[Red("shell-ui")]` removed (onThemeChange / onThemeBeforeApply fired in `ThemeController.cs:114,126`; `spec/plugins/hooks.csv` rows set to done).
- **csproj edit (not mine):** `src/Barbican.App/Barbican.App.csproj` references `tests/Barbican.FakeJmap` in the Automation configuration only, for `BARBICAN_SAMPLE=1` sample data (`Shell/Automation/SampleActiveMail.cs`: real `MailService` + `MailSync` over the in-process fake, never a real mailbox). `tests/Barbican.Build.Tests` does not forbid it in Release; the reference is conditional so Release has none, but add FakeJmap to the forbidden list there.
- **WGC does not capture a palette-only change of the hidden window.** After `ui ...` (brush colours, style) the XAML tree is right (`shot` shows it) but WGC (`burst`, `wshot`) delivers no new frame until a layout change (`resize`) wakes it; `wshot` returned a white picture once. So: screenshots in `docs/milestones/M1-screens/` are XAML renders (`shot`; Bento 1356x856 because the render omits the 14 px window padding, the body area is empty), and `m1_scenarios.py` measured theme_flip / resize / folder_click / key_j20 with only 1-3 frames each (0 BLANK, 0 FLASH is therefore weak evidence). The flip itself is atomic by construction (one UI-thread turn, same brush instances: `PaletteWriterTests`), but nobody has seen the frames between. This is F1 territory (`src/Barbican.Automation`); needs a look, e.g. whether the hidden window is occluded so DWM stops composing.
- **Idle probe:** `probe-start wgc`, 2 s, `probe-stop`: 0 frames on both layouts. Popup guard 0 open / 0 closed; own-is-foreground=no.
- **`select` / `invoke` peers fail on list items ("pattern not supported")** although `uia-select` works: the in-process `ISelectionItemProvider` cast from `ListViewItemAutomationPeer.GetPattern` seems to fail under CsWinRT. Scenarios use `uia-select ID`. Item containers carry the AutomationId (set in `ContainerContentChanging`), not the template root.
- **Key handling:** `ShellPage.OnPreviewKeyDown` maps keys with `KeyChordMapper` and calls `IKeyRouter.Handle(..., KeySource.Keyboard)`, ignoring `TextBox`, `PasswordBox`, `RichEditBox` sources (mailcore's note: `Handle` ignores the source). It uses `InputKeyboardSource.GetKeyStateForCurrentThread` for modifiers; **not run on Uno**. A key the router handles is marked handled (so the focused `ListView` does not also move).
- **Window events need one line in `App.xaml.cs` (F1/integrator):** `ViewHost.Get<ShellLifecycle>().AttachWindow(_window)` after the window is created. Until then `onWindowFocus/Blur`, `onVisibilityChange` and `onBeforeUnload` never fire from the real window (`onAppReady`, `onInterval` and online/offline are wired by the page). Windows "Animation effects" is therefore not re-read on activation either.
- **`ViewHost` captures the service provider through an `ILoggerProvider` factory** (`ViewServices`), because `App.xaml.cs` builds the host and does not expose it. Fragile: it depends on `Host.Build` resolving logger providers. A cleaner fix is a public `Services` on the app (integrator).
- **`SignInFormModel` and `AccountPopoverModel` read `IAccountManager?` directly** (the accounts stream's `SignInViewModel` / `AccountsViewModel` are stubs in wave 2). Switch the views to the accounts ViewModels when `accounts` merges; the Windows sign-in view was never shown (no hidden-run path to it: the sample run has no account manager).
- **I- rows:** only what I could evidence is marked (`spec/interactions.csv`: I-020, I-028, I-033, I-038, I-047, I-119, I-129, I-194, I-217; J-046 display). The other display rows of §2.3 (I-001/002/005/006/010 popover, I-021 tooltips, I-030 ring popover, I-039/044/045/046/059 sidebar details, I-098..I-104 sign-in, I-146 paging, I-156 keys beyond j, I-203..I-230 reader details) are built but not verified in a hidden run and stay `todo`. The account popover and the sign-in screen are not in any screenshot.
- **Compose, tags "+", folder gear, quick reply, toolbar buttons** are present and disabled (display only), as the plan says; `Print` and `Star` toolbar buttons of I-194 are not built.
- **Design docs** (`design/README.md`, `design/MOTION.md`) rewritten in XAML terms; the Rust measurements are kept as the baseline. `spec/layout/*.md` got a "C# metrics" section each, not a column per table row.
- **Window padding shows in `burst` frames but not in `shot`**, and the Bento layout draws list rows as separate cards with a 1 px border: compare against `design/mockups/BN*.dc.html` by eye; no pixel comparison was done.
- **[review of shell-ui] Fixed by the reviewer, check the fixes.**
  - `Motion/Tip.SetHost` was never called, so no tooltip ever showed (I-021 stays `todo`); `ShellPage` now has an `Overlay` canvas and calls it. Not seen on screen (hidden run not repeated).
  - "Add account" in the account popover did nothing (`AddAccountRequested` had no subscriber) and the flyout stayed open; the shell now shows the sign-in view over the mail screen with a Cancel button (`SignInFormModel.CanCancel/Cancel`), and the popover closes its flyout on a choice. Not driven live (no account manager in the sample run).
  - `onThemeChange` fired only when light/dark flipped; Bulwark `theme-store.ts:703-710` also fires when the theme setting changes with the same resolved scheme. `ThemeController` now tracks the setting; a stale `onThemeBeforeApply` answer is dropped when the variant moved on.
  - `DispatcherQueueDispatcher.Post` threw after the window's queue shut down; a late sync result on a thread-pool thread would have crashed the process on exit. It now drops the work. Judgement call: a dropped UI update after shutdown is harmless.
  - `tests/Barbican.Build.Tests/ReleaseContentTests.cs`: new test that only the App's Automation group references FakeJmap, and `Barbican.FakeJmap.dll` is a forbidden file in non-Automation outputs. `tools/build.ps1 publish`'s own file check was not extended.
- **[review of shell-ui] Not verified by the reviewer.** Linux VM still unreachable (SSH key not authorized), so Uno was not built or run; the `win:` prefix on property elements is unproven on Uno. No hidden window was launched in this review (the author's screenshots are XAML renders with an empty body area; the feel claim "0 BLANK / 0 FLASH" rests on 1 to 3 frames). `PopIn` sets opacity 0 from the flyout's `Opened` event, which may show one full-opacity frame first; measure with the feel probes on the account popover. The `ViewHost` logger-provider capture, the `KeyChordMapper` (Shift+digit gives the digit, not the symbol; AltGr not handled) and `ShellPage.OnPreviewKeyDown` (Enter or Space on a focused button also reaches the router) need a hand test.

## M1 stream: accounts

Branch `m1-accounts` (from `m1` at 32ced0b; commits 6e7c61b, 0b2639c, a385afd and the Fable commit), not merged. Profiles
and prefs, body cache, OAuth (discovery, registration, PKCE, loopback, refresh), secret stores and the ADR 0008 import,
the account manager, the sign-in and account-popover ViewModels.
- **FABLE #3 resolved, behaviour changed.** `LoopbackListener` reads `state` before `error` or `code`. A callback with a
  missing or wrong state gets 400 and the wait goes on (Rust ended the sign-in with "Sign-in state mismatch"); state is
  compared in constant time; the first callback with the right state ends the wait and the listener closes, so a replay
  finds nobody listening. Review that "ignore and keep waiting" is wanted over "fail at once" (a forged callback can no
  longer cancel the sign-in, but a user who reloads an old tab waits for the timeout). Where: `Core/Auth/LoopbackListener.cs`.
- **AUTOMATION builds never open a browser.** `RedirectFollowingLauncher` GETs the authorize URL through the app's
  transport, requires a 3xx whose Location is an http loopback IP, and writes the GET to the listener over a raw socket.
  Normal builds use the OS launcher (`UseShellExecute` / `xdg-open`, http(s) only). Where: `Core/Auth/RedirectFollowingLauncher.cs`,
  `App/Composition/PlatformServices.cs`.
- **Refresh (FABLE #3, B-OAUTH-013/015).** `OAuthTokenSource` renews ahead of expiry and after a 401, one renewal at a time
  per account; under `oauth-refresh.lock` it adopts the stored token, retries once with a token rotated elsewhere, writes a
  rotation back, never deletes. The lock is still a file going stale after 60 s, as in Rust. An imported OAuth entry is
  never refreshed by an agent: the agent import skips OAuth entries ("sign in by hand").
- **Secrets (FABLE #6).** Windows: hand-written `LibraryImport` of CredReadW/CredWriteW/CredDeleteW/CredFree (not CsWin32;
  four functions did not justify the generator), `Barbican.Next/<profile>/<kind>[/<account>]`, `CRED_PERSIST_LOCAL_MACHINE`
  (the user may want roaming: open), UTF-8, buffers zeroed. The blob limit is 2560 bytes, and the `accounts` entry holds
  every account as one JSON array: about 8-10 OAuth accounts fit. Review splitting it per account before M2's sign-out.
  Linux: `SecretServiceStore` over Tmds.DBus.Protocol (plain session, `schema=barbican-next`, `profile`, `name`, `account`)
  is written but **never ran** (VM unreachable; its test skips without a Secret Service). Automation profiles (`auto-*`)
  keep secrets in memory and never touch the OS store. Strings that held a password cannot be zeroed (.NET strings); only
  the byte buffers are.
- **ADR 0008 import.** `RustCredentialImport` reads `accounts.Barbican`, then `session.Barbican`, through
  `ILegacyCredentialSource` (read only by construction; it refuses `Barbican.Next/` targets). Odd-length or invalid UTF-16
  and non-JSON blobs are skipped with a reason, only new entries are added, blobs are zeroed. User path: default profile
  only, both kinds, plus the admin and assistant keys. Agent path: `dev` or `auto-*` only, password kind only, forced
  read-only, never reads the admin-key entry. Agents trigger it with `BARBICAN_IMPORT_RUST_ACCOUNTS=1` (`AccountsStartup`).
  No Linux import (the Rust Linux format is known from the crate source only). The UI entry ("Import accounts from the
  previous Barbican") is shell-ui's to show; the call is `ILegacyAccountImport.ImportAsync(ImportMode.User)`.
- **Read-only.** Every account is read-only unless its address is listed in `BARBICAN_WRITABLE_ACCOUNTS`
  (`ReadOnlyPolicy`); M1 lists none, imported agent entries also carry the stored flag. The flag reaches
  `JmapClientOptions.ReadOnly`. Review before M2 adds the first writes: the user's own profile must get a way to opt in.
- **Accounts.** Several accounts stay signed in (one `AccountSession` each); the list is most recently used first (Rust),
  the default star is the first account added (Bulwark). Inactive accounts get a 2-minute inbox-unread poll
  (a mailbox read through their own session, Bulwark account-inbox-unread.ts) rather than push; their sync engine is not
  started. A failed switch keeps the current account ("Could not switch to ..."). With a one-time code the account is not
  remembered (Rust main.rs:762); the code goes as `password$code` (Rust), not Bulwark's token exchange. `onAccountAdd`
  fires only for a second or later account. Sign-out and forgetting are M2.
- **Server lookup** (I-098): `ServerDiscovery` probes `/.well-known/jmap` on the email domain and `mail.<domain>` (200,
  3xx or 401 count). Bulwark's `_jmap._tcp` SRV lookup is not ported (.NET has no SRV resolver without a package).
- **Unsupported-provider notice, one provider missing.** `tools/privacy-check.sh` refuses the name of Google's mail
  service anywhere in the repo (it guards a private address), so its two domains are not in `MailServerAddress`'s list
  and no notice shows for them. Owner of the privacy check: decide whether a narrower pattern can allow the provider
  name, then add the two entries.
- **Profiles and prefs.** `Barbican-<profile>` (default `dev`) beside the Rust `Barbican` folder; `auto-*` for automation.
  `prefs.json` is kept as a `JsonObject` (unknown keys survive), saved atomically 300 ms after the last change on the
  thread pool. The dev profile copies the Rust prefs, panes and search history once (marker file); the Rust folder is
  only read. `IoThreadCheck` counts profile IO on the UI thread and asserts in Debug. The exit flush is not wired (below).
- **Persistent SQLite cache deferred to M2** (M1-plan §6.3): M1's cold start uses the "without cache" budget.
- **Contract additions** (all additive): `AccountInfo.Kind`, `AccountInfo.IsReadOnly`, `OAuthSignIn.LoginHint`,
  `IPrefsEditor` (`Put`, `FlushAsync`), `IAccountOverview`, `IAccountVault`, `IRefreshTokenStore`, `IAuthorizationLauncher`
  (Core cannot see Platform's `IBrowserLauncher`), `ILegacyAccountImport`, `SignedInEventArgs`.
- **Tests:** Core 375, Platform 19 (1 skipped: Secret Service), Presentation 140; full `tools/build.ps1 test` 691, 0 failed,
  4 skipped. OAuth end to end runs on FakeJmap behind a test-side `FakeOAuthProvider` (authorize, registration, code grant
  with PKCE, refresh rotation), so FakeJmap itself still has no OAuth endpoints.
- **Hidden automation:** the Automation build ran on a private desktop (`tools/auto-run.ps1 start -Port 8837
  -PrivateDesktop`): it started, ran the accounts start-up with an empty in-memory vault, popup guard 0/0, foreground and
  cursor unchanged, closed. `wshot` and `probe-start wgc` fail on a private desktop, so there are **no frame numbers**.
  The brief's scenario (password + TOTP by AutomationId, OAuth second account, switch, popover `wshot`, no blank frame on
  switch) is a **hand test / later run**: the sign-in and popover views are shell-ui's and `BARBICAN_FAKE_JMAP` is not
  wired. **Live checks** (import, both accounts read-only, list folders, delete the dev entries): hand test, not run.
- **Files edited outside my owned paths:** `tests/Barbican.Core.Tests/Plugins/HookCallSiteTests.cs` (`[Red("accounts")]`
  removed) and the spec files (`hooks.csv`, `oauth.md`, `settings.md`, `platform.md`, `interactions.csv`, `jmap.csv`
  J-020), as the brief asked. The I- rows are `P`: the logic is done, the views are shell-ui's.
- **Linux VM unreachable** (ssh refused the key, 2026-11-17): `SecretServiceStore`, `LinuxBrowserLauncher` and the Linux
  profile paths ran nowhere. Run `tools/linux.ps1 test -Branch m1-accounts`.

### Requests to other streams
- **jmap / integrator:** fold `tests/Barbican.Core.Tests/Auth/FakeOAuthProvider.cs` into `tests/Barbican.FakeJmap`
  (authorize, registration, code grant with PKCE) and reference FakeJmap from the App in the Automation configuration
  behind `BARBICAN_FAKE_JMAP`, so the hidden scenario can sign in without a server.
- **jmap:** switch `tests/Barbican.Jmap.Live/LiveAccount.cs` to the imported password-kind entry: `SecretAccountVault` over
  `WindowsSecretStore("dev")` after `BARBICAN_IMPORT_RUST_ACCOUNTS=1`.
- **shell-ui:** bind `SignInViewModel` and `AccountsViewModel` (both singletons in DI; `SignedIn` and
  `AddAccountRequested` drive the screen change; `Reset(AccountsViewModel.AddAccountStatus)` for Add account); give the
  fields AutomationIds (server, user, password, totp, client id, remember, sign-in, oauth, cancel, change-server; the
  avatar, rows by account key, add-account); offer the ADR 0008 import in the default profile; call
  `IPrefsEditor.FlushAsync` on exit (B-SET-005).
- **mailcore:** `BodyCacheSync` subscribes to `MailEventHub.EventReceived` and patches the cache for the active account;
  local actions (read, flag, move) must also reach `IBodyCache` when M2 adds them.
- **privacy-check owner:** see the unsupported-provider item above.

## M1 review: accounts

Fresh review of `m1-accounts` (diff `m1...m1-accounts`, implementer commits 0b2639c, a385afd, 2eda0a2), fixes in the review commit on
`m1-accounts`. Windows `tools/build.ps1 test` green before and after (692 then 695 tests, 0 failed, 4 skipped). **Linux not run:**
the VM timed out on port 22 again during this review (2026-11-17), so `SecretServiceStore`, `LinuxBrowserLauncher`, the Linux profile
paths and `LoopbackListener` (`ExclusiveAddressUse` maps to SO_REUSEPORT on Unix) ran nowhere but Windows.
- **Fixed: a connect that failed synchronously pinned the account.** `AccountManager.ConnectAsync` stored the attempt task after the
  attempt's own `finally` had already cleared it (re-entrant `Lock`), so an account whose first connect failed without awaiting could never
  be retried until restart. Test `AccountManagerTests.A_connect_that_fails_at_once_can_be_retried`. Review the "share one attempt" design
  as a whole (`Entry.Connecting`, `src/Barbican.Core/Accounts/AccountManager.cs`).
- **Fixed: OAuth read-only decided on the typed hint.** The OAuth client was opened with the read-only flag of `LoginHint` (may be empty
  or another name); the real login is only known from the session. Now a client that is writable for a login the policy makes read-only
  is replaced by a read-only connect. Test `An_oauth_login_that_is_not_writable_gets_a_read_only_client_whatever_the_hint`. M1 lists no
  writable account, so this was latent; review before M2 opens `BARBICAN_WRITABLE_ACCOUNTS`.
- **Fixed: the sign-in deadline during a read threw.** A browser (or any local process) holding a connection open past the 5-minute
  deadline made `WaitForCodeAsync` throw `OperationCanceledException` instead of "Sign-in timed out." Test
  `LoopbackListenerTests.The_wait_times_out_while_a_silent_connection_is_open`.
- **Fixed (unverified, Linux): a locked keyring lost writes silently.** `SecretServiceStore.WriteAsync` ignored `CreateItem`'s prompt
  path; with a locked collection nothing was stored and Ok was returned. It now reports "The keyring is locked." (the prompt is never
  shown: it would open a window). Needs a run on the VM.
- **Fixed: `AccountsStartup.Completion` never completed after an unexpected exception** (vault or store failure), which would hang the
  automation build and tests that wait on it; it now faults and rethrows.
- **Open: `BodyCacheSync` keys events by the account active when the event is handled**, not by the account whose engine sent it.
  `MailEventHub` posts to the dispatcher, so an event of the old account handled just after a switch patches the new account's entries;
  Stalwart ids are per-account sequences and can collide, so a cached body could show another message's keywords until it is reopened.
  Request to mailcore: carry the account key in `MailSyncEventArgs`. Where: `src/Barbican.Presentation/Accounts/BodyCacheSync.cs`.
- **Open: the user path of the ADR 0008 import is unreachable in M1.** `RustCredentialImport` refuses `ImportMode.User` in `dev` and
  `auto-*`, and `dev` is the default profile until M8 (`ProfilePaths.DefaultProfile`). Consistent with ADR 0008 ("only the default,
  non-dev profile"), but M1 has no such profile. Decide at M8 how the real profile is named.
- **Open: `BodyCache` bounds are global, not per account** (M1-plan §6.3 says "per account"); Rust bodycache.rs was global too, so the
  port follows Rust. `Weight` counts UTF-16 code units where Rust counted UTF-8 bytes (non-ASCII mail weighs less here). Judgement call.
- **Open: wrong password at sign-in for a password account** surfaces jmap's mapped "Wrong username, password or one-time code."
  (SessionFetcher maps every AuthError at session time); an OAuth account whose refresh is rejected at restore shows the same text on its
  row before `Describe` turns it into "needs to sign in again". Review the wording with the user.
- **Open: shell-ui's views drive `IAccountManager` directly** (`App/Views/SignIn/SignInFormModel.cs`,
  `App/Views/Accounts/AccountPopoverModel.cs`) instead of `SignInViewModel`/`AccountsViewModel`: the server lookup, provider notice,
  OAuth cancel, unread badge and error line of the accounts ViewModels are not on screen, and the popover refreshes on `ActiveChanged`
  only (not `StatusesChanged`). The I- rows stay `P`. Integrator: bind the views to the accounts ViewModels (or fold the models).
- **Not done (hand test):** the brief's hidden-automation scenario (password + TOTP by AutomationId on FakeJmap, OAuth second account,
  switch, popover `wshot`, no blank frame on switch) and the optional live read-only import run. `BARBICAN_FAKE_JMAP` sign-in routing does
  not exist yet (shell-ui's `BARBICAN_SAMPLE` replaces `IActiveMail`, bypassing sign-in).
- **Integration fix at the merge (shell-ui file, by the reviewer as integrator): no sign-in screen while the accounts restore.** With the
  real `IAccountManager` in the container, `ShellPage.ShowScreen` showed the sign-in view at start-up until the remembered account had
  connected (a flash of the wrong screen on every start), and the sample-data run (`BARBICAN_SAMPLE=1`, the M1 screenshots) showed only
  the sign-in view. Now the mail screen stays while `AccountsStartup.Completion` is pending, the sign-in view shows after it when no
  account is active, and a sample-data run ignores the account manager. Where: `src/Barbican.App/Shell/ShellPage.xaml.cs`. Review: an
  empty mail chrome during a slow restore (network) is a judgement call; measure the start with the feel probes, and decide what an
  offline restore should show (now: the sign-in view with the account's error).
  Checked once on the merged `m1`: Automation build started with `tools/auto-run.ps1 start -Port 8853 -PrivateDesktop` (empty in-memory
  vault): the XAML render (`shot`) shows the sign-in view, popup guard 0 open / 0 closed, foreground window and cursor unchanged, closed.
  No frame probe (WGC does not run on a private desktop), so "no flash of the sign-in view" is by construction, not measured. The render
  shows "Remember me" with an indeterminate glyph although the model defaults to true: shell-ui binding, not checked further.
