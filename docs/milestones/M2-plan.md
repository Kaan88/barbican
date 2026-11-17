# M2 plan: mail actions, menus and reader depth

- Written 2026-11-17 by the M2 plan stage (Opus, PORT-PLAN.md §6.1 step 1), on branch `m2`.
- Scope and exit: [PORT-PLAN.md](../PORT-PLAN.md) §5 "M2"; rules: §3 (architecture), §4 (quality, hidden automation, feel,
  live safety), §6 (workflow, models, concurrency); decisions: [adr/](../adr/README.md); open review items:
  [FABLE-REVIEW.md](../FABLE-REVIEW.md) (M1 consolidated list, Fable review 2, m1-fix-2) and
  [fable-review-2.md](../fable-review-2.md) R2-06 to R2-14; the M1 report [M1.md](M1.md) §5, §6.
- Integration branch: **`m2`**, taken from `origin/m1` at `112c9f9` (m1 with m1-fix-2 merged). M2 builds on `m1` because the
  user has not done HAND TEST 1 yet. If `m1` changes before the M1 gate (hand-test fixes), the integrator merges `m1` into `m2`
  (never a rebase, never a force-push). Nothing merges to `main` and nothing is tagged until the user's M2 hand test and OK.
- Accounts and paths are placeholders (CLAUDE.md, `.local/accounts.md`): `<readonly-account>`, `<test-account>`, `<server>`,
  `<rust-repo>`, `<bulwark>`, `<stalwart-zip>` (Stalwart 0.16.27 source; paths below are inside it), `<downloads>`.
- **Linux VM: reachable again.** `tools/linux.ps1 ping` answered at plan time (one try, 2026-11-17). M1 has never run on Linux;
  stage F runs the first Linux baseline (§5 F step 0).

## 0. Summary

| Order | Key | Title | Model | Hard | Wave | Depends on |
|---|---|---|---|---|---|---|
| 1 | `f-spec-tests` | 103 red tests, shared contracts and seams, packages, projects, registration files, slots, FakeJmap arms and seed | Sonnet | no | 0 (fixed stage F) | m2 |
| 2 | `actions` | Mail and folder actions, write layer, undo and toasts, multi-select, selection bar, hover actions, drag and drop, panes, tags | Sonnet | no | 1 | F |
| 3 | `menus` | Context menus and dialog host, account menu, rail, command palette, shortcuts and `?` sheet, key routing | Sonnet | no | 1 | F |
| 4 | `reader` | Reader depth: unsubscribe (RFC 2369/8058), read receipts (MDN), invitation banner, trust and remote images, view source, .eml, zip, Save to Files, attachments, OTP chips, avatars, guarded external HTTP, write-test guard | Opus | yes | 1 | F |
| 5 | `search` | Search syntax, chips, suggestions, history, snippets, filter panel, global search, quick rules with the Sieve core | Sonnet | no | 2 | wave 1 |
| 6 | `unified` | Unified inbox, shared accounts, account maintenance (sign out, default, order), .eml/.mbox import and export, Email/parse, TNEF, native .NET plugin host, spam score, hook budgets | Opus | yes | 2 | wave 1 |

- **Merge order into `m2`:** `f-spec-tests`; wave 1: `actions`, `menus`, `reader`; wave 2: `search`, `unified`. Each wave starts
  from `m2` **after** the previous wave is merged, built and green on both OSes, so a stream consumes real code from earlier
  waves, never a moving branch.
- **Limits held:** at most 3 streams per wave, at most 1 Opus stream per wave (§6.3), so at most 3 agents compile at once (plus
  reviewers, which do not compile); `tools/build.ps1` enforces 2 build slots; one Linux VM build at a time (streams queue on
  `tools/linux.ps1`).
- **Branches `m2-<key>`, not `m2/<key>`:** git cannot hold `refs/heads/m2` and `refs/heads/m2/<key>` at once (M1 decision,
  M1.md §6 item 7). Worktree `<downloads>/wt-m2-<key>`. Push at creation and after every commit.
- **Ports and profiles** (hidden AUTOMATION runs, `BARBICAN_PROFILE=auto-<port>`): F 8880, actions 8881, menus 8882, reader 8883,
  search 8884, unified 8885, integrate/verify 8889.
- **M2 T- rows:** 89 (`python tools/spec-check.py --milestone M2`: 89 pending at plan time). Plus 14 rows of later milestones
  ported early because M2 features need them: sieve T-271..T-279 (M6; quick rules call the Sieve parser and generator) and invite
  T-122..T-126 (M4; the invitation banner's display and trust). F writes all 103 as red tests.

## 1. Rules every M2 agent repeats (CLAUDE.md, PORT-PLAN §6.4, the M2 hard rules)
1. Read CLAUDE.md, AGENTS.md, `.local/accounts.md`, this plan, and the PORT-PLAN sections and spec files your brief names.
   `git config user.email` is the noreply address; `git config core.hooksPath .githooks`.
2. **Never touch the user's real screen, mouse or keyboard.** Every window you launch is the hidden AUTOMATION build (ADR 0007:
   off-screen at (-12000, 200), `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, non-activating from the first frame, the fail-closed popup
   guard on), started with `tools/auto-run.ps1 start -Port <your port>`. If you cannot guarantee that for a check, do not launch
   it: write it as a hand test. No `SendInput`, `SendKeys`, `mouse_event`, `keybd_event`, `SetForegroundWindow`, `SetCursorPos`,
   FlaUI `Mouse`/`Keyboard`, xdotool. The `net10.0-desktop` head runs only under the VM's private Xvfb (`tools/linux.ps1 xrun`).
   Every popup, flyout, menu, dialog and suggestion list is in-window and constrained (`ShouldConstrainToRootBounds="True"`);
   the controls on the deny list (`src/Barbican.Automation/Protocol/PopupControls.cs`: ComboBox, ToolTip, AutoSuggestBox,
   CommandBarFlyout, pickers) are not used. `popup-probe` only with `-PrivateDesktop`.
3. **Frame captures.** The display may be off (M1 got only uniform white WGC frames). `tools/flicker.py` reports uniform frames
   (`empty`, `capture_valid`): a uniform capture is **invalid, never a pass**. Report "unproven (capture invalid)" and keep the
   in-process timings; the frame gate then goes to the user's on-screen run.
4. **Accounts.** `<readonly-account>` strictly read-only. Writes only on `<test-account>`, sent to itself, cleaned up after.
   Never read `<admin-key-file>`. Never type passwords; never sign in, sign out or clear credentials of the Rust app. Live checks
   only with **password-kind** entries from the ADR 0008 agent import; never refresh an imported OAuth token. Never change
   Stalwart server settings (quick rules write the user's own Sieve script on `<test-account>` only, and restore it). **Live checks
   run only in the integrate stage after a wave's merge, one agent at a time** (§7); a stream writes its live script and its
   FakeJmap equivalent, and runs nothing live itself. If live access is not possible, the live check becomes a hand test.
5. Don't kill processes you didn't start. No OS changes (no file associations, no registry, no certificates).
6. **Builds** only through `tools/build.ps1` (2 machine-wide slots; never bypass them) and `tools/linux.ps1`. Never `dotnet
   build`/`test` directly. At the end: `tools/build.ps1 shutdown` (if it exits 1, run it once more).
7. **Linux.** Try `tools/linux.ps1 ping` once per stage with a short timeout. If it fails, write "Linux pending (VM unreachable)"
   in your report and move on; no retry loops. When it answers, `tools/linux.ps1 test -Branch m2-<key>` after your last commit.
8. **Feel:** no flicker, no blanking, no white or black flash; old content stays until the new is ready; nobody waits for an
   animation (nothing over 200 ms, `Motion.*` tokens only); lists are synced with `ListSync`, never re-assigned; measure with
   the probes (`burst` + `tools/flicker.py`, `probe-start wgc|rendering` + `tools/motion.py`), never by eye.
9. **Commits:** `sh tools/privacy-check.sh --worktree` before every commit (its private patterns live in the main checkout's
   gitignored `.local/privacy-patterns.txt`, never in a tracked file); noreply identity; the message says the behaviour decided
   and why and ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` (Fable agents: `Co-Authored-By: Claude
   Fable 5.2 <noreply@anthropic.com>`); push after every commit. Update `spec/` status in the same commit as the code
   (`python tools/spec-check.py --write`). Never force-push, never rewrite history (D-1 is not approved).
10. **Code rules:** one type per file (MA0048; partial files `Type.Part.cs` are fine), one view per file, one ResourceDictionary
    and one registration file per feature, records for models, `Result<T>` for expected failures, `TimeProvider` for time,
    `x:Bind` only, `IDispatcher` for UI-thread work, no file IO on the UI thread. Every Core file starts with the Bulwark
    file:line, the RFC section and the Stalwart path it mirrors. Ported Rust tests carry `[RustTest("<rust_test_tag>")]` exactly
    as `spec/rust-tests.csv` has it; an assertion may change only with a D- entry. Remove `[Red]` in the commit that makes a test
    pass.
11. **Fable log:** append to `docs/FABLE-REVIEW.md` under `## M2 stream: <key>` as you go, one bullet per item (what, why, exact
    paths and commits), and list the items in your final return.
12. **Plugins:** two systems over one catalogue (`spec/plugins/hooks.csv`, `slots.csv`): the Bulwark-compatible JS host (M6b) and
    native .NET plugins (this milestone, stream `unified`). The Rust C ABI is dropped. Fire your area's hooks through `IHookBus`;
    every hook row with `milestone = M2` and your stream as `owner` gets a call site (`cs_call_site`) in your stream.
13. **Stay inside your owned files (§3).** If you need a change in a file you do not own, write it under `## Requests to other
    streams` in your Fable section and work around it behind your own interface; the integrator applies it.
14. **User decisions in force** (Fable review defaults; the user may still override): D-2 never import OAuth refresh tokens from
    the Rust app; D-3 secrets local only, no roaming; D-7 the assistant's destructive tools always ask first (M6); D-9 ignore
    Authentication-Results from an authserv-id that is not the account's server; D-10 plain `http://` only for loopback. D-1
    (rewriting public history) is **not** approved.

## 2. What M2 must close

### 2.1 Rust tests (T-): 89 M2 rows + 14 early rows
| Owner | Rows |
|---|---|
| actions | T-083..T-090 (8 `dnd::*`; T-085 and T-089 are calendar-event moves, ported as pure Core functions that M4 consumes; T-090 pane limits) |
| menus | T-201..T-205 (5 `palette::*`), T-260..T-270 (11 `shortcuts::*`; the composer and calendar key tables are ported as data now, their commands land in M3/M4) |
| reader | T-212..T-222 (11 `reader::*`), T-177..T-183 (7 `mdn::*`), T-199, T-200 (2 `otp::*`), T-018..T-024 (7 `avatars::*`), T-311, T-312 (2 `zip::*`); early: T-122..T-126 (5 `invite::*`, display and trust; T-127 RSVP stays M4) |
| search | T-102..T-114 (13 `gsearch::*`), T-256..T-258 (3 `search_history::*`), T-207..T-211 (5 `quick_rules::*`); early: T-271..T-279 (9 `sieve::*`, M6 rows the quick rules depend on) |
| unified | T-286..T-292 (7 `unified::*`), T-163..T-169 (7 `mailimport::*`), T-206 (`plugins::loads_spam_score_plugin`, assertion changed: see D-234 below) |

Totals: actions 8, menus 16, reader 29 (+5), search 21 (+9), unified 15 = 89 (+14).

**Assertions F must not port verbatim** (each gets a D- entry, next free numbers after D-233; F writes them in
`spec/decisions.md` under "M2 decisions (C# port)"):
- **D-234, T-206:** the Rust test loads `barbican_spam_score.dll` through the C ABI and skips when it is not built. The C# test loads
  the spam-score **native .NET plugin** through the plugin host from the test output (never skipped) and keeps the rest of the
  assertions (id `spam-score`, handles the email-details hook, `X-Spam-Status: Yes, score=7.5 required=5.0` gives one row, tone
  danger, text contains `7.50 / 5.00`). `tests/fixtures/plugins/spam-score/README.md` is rewritten: the Rust source stays as the
  reference for the port; no DLL is ever committed.
- **T-023** (Rust: a worker thread counted out under the queue lock): ported as "the avatar worker's busy count is zero after the
  queue drains" on the C# worker (a `Channel` reader), same meaning, D- entry only if the wording changes.
- **T-024** (Rust `SharedPixelBuffer`): ported against the Core raster type the avatars use (`Raster`, F stub), same sizes and the
  1-pixel rejection.
- **T-216..T-218** (one-click): the Rust tests post to a loopback listener through a pinned address. The C# tests use a loopback
  listener on port 0 only, through a test seam of `GuardedHttp` that pins the address after the guard (never a real network).
- Any other assert that cannot be ported verbatim: D- entry, named in F's Fable section.

### 2.2 JMAP requirements (J-)
| Owner | Rows |
|---|---|
| actions | J-009 (maxObjectsInSet: writes chunk by it; `partial(M2)` → done), J-047 (Mailbox/set destroy, `mailboxHasEmail`/`mailboxHasChild` mapped), J-048 (myRights in the UI, with menus' enable rules), J-051 (collapseThreads), J-057 (Email/import, the call; the import loop is unified's), J-059 (Email/copy to another account), J-074 (`urn:stalwart:jmap` `x:ArchivedItem` query/get/set `requestRestore`: the trash recovery link; hidden when the capability is absent), J-034 (upload; `partial(M3)`: the composer's upload paths are M3) |
| reader | J-068 (receipts: Stalwart 0.16.27 has no `MDN/send` (`crates/jmap-proto/src/request/method.rs` lists none), so `n/a` for MDN/send with the Rust path kept: upload + Email/import into Sent + EmailSubmission/set with an explicit envelope, destroy on failure, `$MDNSent`, Rust `mdn.rs:202`), J-036 (Blob/copy: `partial(M3)`, Blob/upload is M3), J-075 (FileNode get/query/set for Save to Files: `partial(M5)`), J-072 (CalendarEvent/parse for the invitation banner: `partial(M4)`) |
| search | J-056 (SearchSnippet/get), J-073 (SieveScript get/set/validate with `onSuccessActivateScript`: `partial(M6)`, only what quick rules use) |
| unified | J-058 (Email/parse), J-028 (upToId: `partial(M2)` → done or a written reason) |

Each row moves to `cs_status = done` (or `partial(M<k>)` with what is left) with `cs_test` naming the test, Bulwark and Stalwart refs
re-verified (`<stalwart-zip>/crates/jmap/src/{email/{set,copy,import,parse,snippet},mailbox/set,blob/{copy,upload},submission/set,sieve/{get,set,validate},file/{get,query,set},calendar_event/parse}.rs`).

### 2.3 Interactions (I-)
The catalogue tags 141 I- rows M2, plus 10 M1 rows deferred to M2 and 24 M1 rows still `todo`. The M1 rule stays: the owner closes
what M2 builds, sets everything else to `deferred(M<k>)` with a reason, and leaves nothing silently `todo`. Rows a stream closes
need evidence (a test, an automation script, or a screenshot path with sample data).

| Owner | Rows |
|---|---|
| actions | I-042, I-048, I-055, I-057, I-060, I-071, I-271 (panes), I-120, I-123, I-124, I-125, I-126, I-127 (row "…" opens MENU-MAIL), I-128, I-129 (chip click through reader's `IAttachmentSaver`), I-130 (list chip, data from reader's OTP finder), I-131..I-137 (hover strip; real hover is a hand test), I-138..I-144 (selection bar, counts, select all, empty Trash/Spam banner, empty state; Compose part M3), I-145 (pages mode, if Bulwark's setting exists; else deferred with the reason), I-146, I-147, I-148, I-153, I-154, I-160, I-163, I-164, I-172, I-173, I-174, I-175, I-176, I-177, I-178, I-180, I-184, I-187, I-232 (row rendering of snippet marks; data from search), I-034 (sidebar collapse toggle), I-040, I-056, I-122 (raising the folder, tag and row menus), I-063, I-064 (folder menu commands), I-151, I-044, I-045 (M1 `todo`, seen with sample data), I-230 (trash recovery link, J-074; the reader shows it) |
| menus | I-003, I-004, I-012 (help links: display; targets M10), I-013, I-014, I-015, I-016, I-018, I-031 (UI; logic is unified's `IAccountMaintenance`), I-007, I-008 (account reorder UI), I-021 (tooltips; hover is a hand test), I-029, I-037 (account header slot in the sidebar), I-049, I-150 (MENU-LIST and the list ⋮ menu), I-157, I-158, I-159, I-161, I-162, I-165, I-166 (keys; their commands belong to their owners), I-167 (R2-11), I-168, I-169, I-171, I-188, I-189, I-190, I-191, I-215 (MENU-BODY), I-223 (MENU-THREAD), I-270, I-453, I-454, I-456 |
| reader | I-113, I-209 (trust per sender), I-183, I-194..I-200 (toolbar and More: the view; commands are actions' and its own), I-202, I-203, I-206, I-208, I-211, I-212 (display and trust; Accept/Maybe/Decline, add to calendar M4), I-214, I-222, I-225, I-226 (Save to Files, Download all, Save all, Show N more; Rename/Edit M5; drag out: hand test), I-227, I-229, I-288 |
| search | I-023, I-272..I-280, I-455, I-179, I-065 (Rules ▸ part) |
| unified | I-017, I-050..I-054, I-287 (import and export of mbox/eml; local profiles and jobs as far as Rust had them), I-065 (Import/Export part), I-468 (native plugin host and the toggle in prefs; the settings UI M6) |
| deferred | I-011 Manage accounts (M6, settings page), I-019 leave guard (M3, unsaved composer), I-024 plugin pages (M6b), I-025 admin link (M7), I-026 notification bell (M8), I-027 accessibility button (M6), I-035 folded rail and drawer (M10), I-043 drag folder to tab and I-152 category tabs (M10, with the `tabs` hooks), I-121 open in window (M8), I-149 list columns (M10, G-062), I-155 plugin list chips and row badges (M6b), I-170 forward as attachment (M3, the composer opens it; the payload is built in M2), I-185, I-186 (M3), I-192, I-193, I-204, I-205, I-231 (M5, contacts), I-219..I-221, I-285 (M3, quick reply and dock), I-228 (M5, file preview), I-446..I-448, I-466, I-467 (M6b, plugin install UI), I-058 (M3), I-062 (M6), I-065 Share and Mute (M6, M8), I-059 Starred/Show more (actions closes it if the seed shows it; otherwise M10) |

### 2.4 Plugin hooks
`spec/plugins/hooks.csv` tags 50 hooks M2. F re-tags 14 whose surface M2 does not build (the CSV allows a deliberate change,
`spec/plugins/README.md`), and sets the owner of the other 36:

| Hooks | Owner / new milestone |
|---|---|
| `mail-organize`: onBeforeEmailDelete, onAfterEmailDelete, onBeforeEmailMove, onAfterEmailMove, onBeforeEmailArchive, onEmailArchive, onEmailUnarchive, onBeforeEmailSpamToggle, onEmailSpamToggle, onBeforeEmailReadStateChange, onEmailReadStateChange, onBeforeEmailStarToggle, onEmailStarToggle, onBeforeEmailKeywordChange, onEmailKeywordChange (15) | actions |
| `mail-mailboxes`: onBeforeMailboxCreate, onMailboxCreate, onBeforeMailboxRename, onMailboxRename, onBeforeMailboxDelete, onMailboxDelete, onBeforeMailboxEmpty, onMailboxEmpty (8) | actions |
| `ui`: onColumnResize, onDragStart, onDragEnd, onEmailDrop, onTagDrop (5) | actions |
| `mail-read`: onBeforeExternalLink, onAvatarResolve; `mail-attachments`: onAttachmentDownload; `ui`: onTextSelectionChange (4) | reader |
| `mail-search`: onProvideSearchResults (1) | search |
| `auth`: onAccountRemove, onBeforeLogout, onAfterLogout (3) | unified |
| onBeforeEmailCategorize, onEmailCategorize, onTabsChange, onTabActivate, onTabCountsRefresh (5) | → M10 (category tabs deferred, I-152) |
| onBeforeAttachmentUpload, onBeforeBlobUpload, onAfterAttachmentUpload (3) | → M3 (Bulwark fires them in the composer) |
| onPasswordChange, onTotpChange, onAppPasswordChange, onEncryptionChange, onDisplayNameChange (5) | → M6 (account security pages) |
| onAttachmentPreview (1) | → M5 (file preview) |

Intercept hooks run before the write and can stop it; observers after the server accepted it. `HookCallSiteTests` gets one red test
per M2 owner (F). Hook budgets and fail-closed flags (R2-10) are unified's, and they apply to every hook, so the intercepts of
`actions` (wave 1) run on the M1 bus semantics until wave 2 lands; the integrator re-runs actions' hook tests after `unified` merges.

### 2.5 Behaviour rows (B-), live checks (L-), gaps (G-)
- B-: each stream marks the rows of its area `ported` or `deferred(M<k>)` in the same commit: actions `dnd.md`, `mail.md`
  (actions, tags); menus `shortcuts.md`, `palette.md`, the menu rows; reader `reader.md` (actions and banners, MDN, OTP, avatars,
  quick-rules display), `extras.md` ZIP and INV (display); search `extras.md` SRCH, `sieve-filters.md` (the parser/generator rows
  quick rules need; the rest stays M6), `reader.md` quick rules; unified `mail.md` (unified, import), `extras.md` SPAM and PLUG.
- L- (run in the integrate stage, §7): L-011, L-012 (banner only), L-013, L-014, L-015, L-017, L-019, L-037 (reader); L-016, L-050
  (search); L-022 (mail part), L-039, L-044, L-057 (actions); L-038 (unified); L-047 (menus).
- G-: G-046 export, G-047 TNEF (unified); G-048 Email/copy (actions); G-056 fetch the full body of a truncated message (reader);
  G-062, G-073 list columns and tabs → M10 (verification-code copy and `$pinned` of G-073 are M2: reader, actions).

### 2.6 Owed items from the M1 reviews
| Item | Owner | Why here |
|---|---|---|
| R2-06 vault under two instances (reload inside the lock, upsert/remove one, try the in-memory token once) | unified | before sign-out exists |
| R2-07 Authentication-Results: only the server's own trace block, pin the authserv-id per account, no verdict in Sent/Drafts (D-9) | reader | with the trust banner |
| R2-08 PNG row cap (width > 32,768 or row > 16 MB refused) | reader | before avatars and thumbnails decode rasters |
| R2-09 `loopback` host name and user info in the server address | unified | owns accounts and the server address in M2 |
| R2-10 per-hook budget and fail-closed flag, native handlers start on the pool | unified | the native plugin host lands here; required before M3 send guards |
| R2-11 `/` and `?` from the character, not the key position | menus | owns key routing |
| R2-12 a push scrolls the list back to the selected row | actions | owns the list view |
| R2-13 Core points (MailSync errors, push trace, MailEventHub snapshot, events outside the AccountManager lock, Secret Service token) | unified | owns Core/Sync and Core/Accounts in wave 2 |
| R2-13 App points (MailBodyView `IsLoaded`, `shell.js` fit after images, `BodyShape.Normalize`) | reader | owns the body view |
| R2-14 the imported `<test-account>` can never be opened for writing | reader | first: wave-1 write tests need it (decision M2-Q2, §9) |
| M1.md §5: `BodyCacheSync` files events under the wrong account (carry the account key in `MailSyncEventArgs`) | unified | multi-account |
| M1.md §5: `IBodyOverlayStrategy` has no caller | menus | the dialog host and the palette scrim are its first callers on Linux |
| M1.md §5: no test builds the App composition | F | the five new registrations make a composition smoke test necessary |

## 3. Parallel work without shared files

### 3.1 File ownership
After F, every file has exactly one M2 owner **per wave**. Ownership may pass between waves (noted); within a wave never. Folders
go with all files below them. Files not listed stay with the integrator (frozen).

| Owner | Source | Tests |
|---|---|---|
| f-spec-tests (then frozen; the integrator owns them) | `Directory.Packages.props`, every `*.csproj`, `Barbican.slnx`, `src/Barbican.App/Composition/AppComposition.cs`, `tools/build.ps1` (the `-Writes` switch), `src/Barbican.App/Shell/ShellOverlayLayer` files and `IShellOverlay.cs`, the slot edits in shared XAML (§3.3), `tests/Barbican.FakeJmap/{FakeApi.cs,FakeSeed.cs}`, `spec/plugins/hooks.csv` (milestone/owner columns), `spec/decisions.md` (the D- rows F adds), `tests/fixtures/plugins/spam-score/README.md`, `tests/fixtures/mail/m2/**` | `tests/Barbican.Build.Tests/**` (new asserts), `tests/Barbican.Presentation.Tests/Composition/AppCompositionTests.cs` (new), the red tests (they pass to the owners below) |
| actions | `src/Barbican.Core/Mail/Write/**`, `src/Barbican.Core/Mail/Archive/**` (x:ArchivedItem), `src/Barbican.Core/DragDrop/**`, `src/Barbican.Core/Layout/**` (panes.json), `src/Barbican.Core/Mail/Tags/**` (tag create/rename/colour/delete), `src/Barbican.Presentation/Actions/**` (commands, undo, toasts, selection), `src/Barbican.Presentation/DragDrop/**`, `src/Barbican.Presentation/Mail/{MessageListViewModel,MessageRowViewModel,FolderRowViewModel,FolderRows,ListItemViewModel,GroupHeaderViewModel,LoadedList,RowContext,MailListSettings}*.cs`, `src/Barbican.Presentation/Shell/{SidebarViewModel,TagRowViewModel,PanesViewModel}.cs`, `src/Barbican.Presentation/Mail/Loading/{IMailActions,ReadOnlyMailActions}.cs` (the real `IMailActions`), `src/Barbican.App/Shell/ShellPage.xaml(.cs)` (panes and splitters; not the overlay layer), `src/Barbican.App/Views/{List,Sidebar}/**`, `src/Barbican.App/Views/Actions/**` (selection bar, hover strip, toast host, Move-to dialog content, tag picker), `src/Barbican.App/Composition/Features/ActionsRegistration.cs`, `tests/Barbican.FakeJmap/FakeApi.{Set,MailboxSet,EmailImport,EmailCopy,ArchivedItem}.cs`, `spec/feel/M2/actions/**` | `tests/Barbican.Core.Tests/{Mail/Write,DragDrop,Layout,Mail/Tags}/**`, `tests/Barbican.Presentation.Tests/{Actions,DragDrop}/**`, `tests/Barbican.Presentation.Tests/Mail/{MessageListViewModel,Sidebar}*Tests.cs` |
| menus | `src/Barbican.Presentation/{Menus,Commands,Palette,Dialogs}/**`, `src/Barbican.Presentation/Input/**` (KeyRouter, shortcut tables), `src/Barbican.Presentation/Accounts/AccountsViewModel.cs` and its row types, `src/Barbican.Presentation/Shell/RailViewModel.cs`, `src/Barbican.App/Shell/{KeyChordMapper,KeyGate}.cs`, `src/Barbican.App/Views/{Rail,Accounts}/**`, `src/Barbican.App/Views/{Menus,Palette,Dialogs,Shortcuts}/**`, `src/Barbican.App/Composition/Features/MenusRegistration.cs`, `spec/feel/M2/menus/**` | `tests/Barbican.Presentation.Tests/{Menus,Commands,Palette,Dialogs,Input}/**`, `tests/Barbican.Presentation.Tests/Accounts/AccountsViewModel*Tests.cs`, `tests/Barbican.Presentation.Tests/ShellUi/KeyGateTests.cs`, `tests/Barbican.Automation.Tests/PopupLintTests.cs` additions (append only) |
| reader | `src/Barbican.Core/Mail/Reader/**` (headers, unsubscribe, names, truncation, source cut, message attachments, Blob/copy, FileNode nodes), `src/Barbican.Core/Mail/Mdn/**`, `src/Barbican.Core/Mail/VerificationCode/**`, `src/Barbican.Core/Mail/Avatars/**`, `src/Barbican.Core/Mail/Zip/**`, `src/Barbican.Core/Calendar/Invitations/**` (display part), `src/Barbican.Core/Net/**` (`GuardedHttp`, DNS), `src/Barbican.Core/Mail/Trust/**` (trusted senders), `src/Barbican.Core/Mail/AuthResults/**` (R2-07), `src/Barbican.Core/Mail/Attachments/**` (R2-08 and the saver), `src/Barbican.Core/Mail/Body/**`, `src/Barbican.Core/Accounts/ReadOnlyPolicy.cs` (**wave 1 only**, R2-14), `src/Barbican.Presentation/Mail/{ReaderViewModel,ReaderSnapshot,ReaderSnapshots,ReaderHeader,ReaderOptions,AttachmentCard,AuthChip,AuthVerdict,BodyState,ConversationItem}*.cs`, `src/Barbican.Presentation/Reader/**` (banners, menu providers MENU-READER and MENU-ATT, attachment commands), `src/Barbican.Presentation/Mail/Loading/**` except `IMailActions`/`ReadOnlyMailActions`, `src/Barbican.App/Views/Reader/**`, `src/Barbican.App/MailBody/**`, `src/Barbican.App/Assets/mail/**`, `src/Barbican.App/Composition/Features/ReaderRegistration.cs`, `src/Barbican.Automation/Writes/**` (`rx-import`, `rx-find`, `rx-destroy` and their guard, `#if AUTOMATION_WRITES`), `tests/Barbican.FakeJmap/FakeApi.{EmailSubmission,BlobCopy,FileNode,CalendarEventParse}.cs`, `spec/feel/M2/reader/**` | `tests/Barbican.Core.Tests/{Mail/Reader,Mail/Mdn,Mail/VerificationCode,Mail/Avatars,Mail/Zip,Calendar/Invitations,Net,Mail/Trust,Mail/AuthResults,Mail/Attachments,Mail/Body}/**`, `tests/Barbican.Core.Tests/Accounts/ReadOnlyPolicyTests.cs` (wave 1), `tests/Barbican.Presentation.Tests/{Reader,Mail/Loading}/**`, `tests/Barbican.Automation.Tests/Writes/**` |
| search (wave 2) | `src/Barbican.Core/Search/**` (gsearch, history, snippets), `src/Barbican.Core/Sieve/**`, `src/Barbican.Core/Filters/QuickRules/**`, `src/Barbican.Core/Mail/Service/ListFilters.cs`, `src/Barbican.Presentation/Search/**`, `src/Barbican.Presentation/Rules/**`, `src/Barbican.Presentation/Mail/MessageListViewModel.Search.cs` (the partial F creates), `src/Barbican.App/Views/Search/**` (search box, chips, suggestions, filter panel, global-search palette section), `src/Barbican.App/Composition/Features/SearchRegistration.cs`, `tests/Barbican.FakeJmap/FakeApi.{SearchSnippet,Sieve}.cs`, `spec/feel/M2/search/**` | `tests/Barbican.Core.Tests/{Search,Sieve,Filters}/**`, `tests/Barbican.Presentation.Tests/{Search,Rules}/**` |
| unified (wave 2) | `src/Barbican.Core/Accounts/**` (incl. `ReadOnlyPolicy.cs` from wave 2), `src/Barbican.Core/Auth/**`, `src/Barbican.Core/Jmap/ServerEndpoint.cs` (R2-09), `src/Barbican.Core/Sync/**` (R2-13), `src/Barbican.Core/Mail/Unified/**`, `src/Barbican.Core/Mail/Import/**` (mbox, eml, import loop, export), `src/Barbican.Core/Mail/Parse/**` (Email/parse), `src/Barbican.Core/Mail/Tnef/**`, `src/Barbican.Core/Plugins/**` (host, budgets; `HookNames.g.cs` stays generated), `src/Barbican.Plugins.Abstractions/**`, `plugins/Barbican.Plugin.SpamScore/**`, `src/Barbican.Platform/**`, `src/Barbican.Platform.Windows/**`, `src/Barbican.Platform.Linux/**` (R2-06, R2-13), `src/Barbican.Presentation/Unified/**`, `src/Barbican.Presentation/Mail/{MailEventHub,MailSyncEventArgs,ActiveMailSource,IActiveMail,NoActiveMail}.cs`, `src/Barbican.Presentation/Mail/MessageListViewModel.Sources.cs` (the partial F creates), `src/Barbican.App/Views/Unified/**` (the sidebar sections), `src/Barbican.App/Composition/{PlatformServices.cs,Features/UnifiedRegistration.cs}`, `tests/Barbican.FakeJmap/{FakeJmapServer.Routes.cs,FakeJmapOptions.cs,FakeApi.EmailParse.cs}` (wave 2), `spec/feel/M2/unified/**` | `tests/Barbican.Core.Tests/{Accounts,Auth,Sync,Mail/Unified,Mail/Import,Mail/Parse,Mail/Tnef,Plugins}/**`, `tests/Barbican.Platform.Tests/**`, `tests/Barbican.Presentation.Tests/{Unified,Accounts/AccountMaintenance*}/**` |

Generated files (`*.g.cs`, generated `Themes/*.xaml`) are never edited by hand. `HookNames.g.cs` is regenerated by F only
(`python tools/gen-hooks.py`); the M2 hook names already exist.

### 3.2 Projects, packages and build switches F creates up front
| What | Detail |
|---|---|
| `src/Barbican.Plugins.Abstractions` (net10.0, `IsAotCompatible`) | the native plugin API: `IBarbicanPlugin`, `PluginManifest`, `PluginContext`, hook payload views, `EmailDetailRow`. Plugins reference only this. Referenced by Core. |
| `plugins/Barbican.Plugin.SpamScore` (net10.0 class library) | references only Abstractions; not referenced by the App. An MSBuild target in the App project builds it and copies it to `<output>/plugins/spam-score/` (bundled plugin). Added to the solution under `/plugins/`. |
| Core.Tests → plugin | `ProjectReference` with `ReferenceOutputAssembly="false"` plus a copy step into `<test output>/plugins/spam-score/`, so T-206 loads the real build. |
| Packages (`Directory.Packages.props`) | **MimeKit** (MDN multipart/report, view source, forward as attachment, .eml/.mbox parsing, TNEF), **DnsClient** (BIMI and DMARC TXT). F picks the newest version at least two weeks old and checks that Core still builds under `IsAotCompatible` with warnings as errors. If MimeKit raises trim/AOT warnings, F puts the MimeKit users behind `src/Barbican.Core.Mime` (net10.0, not AOT-flagged, referenced by Core's consumers through interfaces) instead of suppressing, and records it. No Ical.Net: invitations are parsed by the server (`CalendarEvent/parse`, Rust `invite.rs:191`). |
| `AUTOMATION_WRITES` | `tools/build.ps1 build -Configuration Automation -Writes` passes `-p:AutomationWrites=true`, which defines `AUTOMATION_WRITES` **only together with** `AUTOMATION`. `tests/Barbican.Build.Tests` asserts that Release and plain Automation builds do not define it and that the publish check rejects `Barbican.Automation.dll` as before. |
| Composition smoke test | `tests/Barbican.Presentation.Tests/Composition/AppCompositionTests.cs` builds the Core and Presentation part of every `Add<Feature>()` with fakes and resolves every registered ViewModel and command (M1.md §5: a DI cycle hung the first body launch). The App-level part is checked by the hidden smoke run. |

A stream that needs an unforeseen package adds it inside its own marked block `<!-- m2-<key> -->` … `<!-- /m2-<key> -->` at the end of
`Directory.Packages.props` and names it in its Fable section; the integrator merges the blocks.

### 3.3 Composition: one `Add<Feature>()` per stream, slots instead of shared XAML edits
- **Composition root:** `AppComposition.cs` (F edits it once, then frozen) calls, after the M1 calls and before `#if AUTOMATION`:
  `AddBarbicanMailActions()` (actions), `AddBarbicanMenus()` (menus), `AddBarbicanReaderDepth()` (reader), `AddBarbicanSearch()`
  (search), `AddBarbicanUnified()` (unified). Each lives in `src/Barbican.App/Composition/Features/<Feature>Registration.cs` and
  registers that stream's Core, Presentation and App services; it may call a Core/Presentation helper in the stream's own folder so
  tests can compose without the App. F creates all five as empty methods.
- **Replaceable defaults:** F registers every seam of §4 with `TryAdd*` and a behaviour-preserving default; the owner registers
  the real one with `Replace`. Registration order makes the later feature win.
- **Multi-registrations:** `IAppCommand`, `IMenuProvider`, `IPaletteProvider`, `IShellOverlay`, `IReaderBannerSource`,
  `IEmailDetailsSource`, `IAttachmentExpander`, `IAutomationCommand`, `IStartupTask`: each stream adds its own, consumers take
  `IEnumerable<T>`.
- **Slots (F edits the shared XAML once, then the view's owner keeps the slot):**
  - `ShellPage.xaml`: `OverlayLayer` (a `Grid` above the regions and below the sign-in screen) filled at start-up from
    `IEnumerable<IShellOverlay>` ordered by `ZIndex`: menus' menu host, dialog host, palette and shortcuts sheet; actions' toast
    host. `Overlay` (the tooltip canvas) stays on top.
  - `MessageListView.xaml`: the M1 search box moves into `Views/Search/SearchBoxView.xaml` (F moves it with its handler, behaviour
    unchanged; the M1 tests stay green) and the header hosts it in `SearchSlot`; a `SelectionBarSlot` above the rows (actions fills
    it in wave 1).
  - `SidebarView.xaml`: `AccountHeaderSlot` (menus, I-037) and `UnifiedSectionSlot` and `SharedSectionSlot` (unified, wave 2).
  - `RailView.xaml` and `ReaderView.xaml` need no slot: menus and reader own them.
  - A slot is a `ContentPresenter` whose content comes from DI through `ViewHost` (no `new` in code-behind).
- **Partial files as seams in the list ViewModel:** F creates empty `MessageListViewModel.Search.cs` (search, wave 2) and
  `MessageListViewModel.Sources.cs` (unified, wave 2), and routes the existing query path through two injected seams
  (`IListQueryShaper`, `IListSourceResolver`, §4.4) with identity defaults, so wave 2 never edits actions' files.

### 3.4 Shared spec files
- `spec/rust-tests.csv` `csharp_test`/`status`: generated by `python tools/spec-check.py --write`; on conflict the integrator takes
  either side and re-runs `--write`. Never hand-merged.
- `spec/jmap.csv`, `spec/interactions.csv`, `spec/live-checks.md`, `spec/gaps.csv`, `spec/settings.csv` (`csharp_read_by`),
  `spec/behaviours/*.md`, `spec/plugins/hooks.csv` (`cs_call_site`, `status`): each stream edits only the rows §2 assigns it.
  Line conflicts are resolved by keeping both sides' row edits.
- `docs/FABLE-REVIEW.md`: each stream appends only under its own heading.

## 4. Contracts (written by F as compiling stubs; owned afterwards as in §3.1)
F writes these as stubs (`throw new PortPendingException(...)`, empty records, identity defaults) so the red tests compile. An
owner may refine a contract during its wave only in a way that keeps consumers compiling (add members, never rename or remove) and
records every change in its Fable section. Breaking changes go through the integrator.

### 4.1 Commands (owner menus; every stream registers commands)
```csharp
// Presentation/Commands. Mirrors Bulwark lib/command-registry.ts:53-96, lib/app-commands.ts, components/email/message-actions.ts,
// Rust ctxmenu.rs actions, palette.rs:92 commands, shortcuts.rs tables.
public interface IAppCommand {
    string Id { get; }                                   // a CommandIds constant
    bool CanExecute(CommandContext context);             // evaluated when a menu opens or a key arrives (no stale state)
    Task<Result<Unit>> ExecuteAsync(CommandContext context, CancellationToken ct);
}
public interface ICommandRegistry {
    bool TryGet(string id, [NotNullWhen(true)] out IAppCommand? command);
    IReadOnlyList<IAppCommand> All { get; }
}
public sealed record CommandContext(CommandSurface Surface, IReadOnlyList<MailTarget> Targets, MailboxRef? Mailbox,
                                    string? Argument, string? AccountKey);
public enum CommandSurface { Keyboard, ContextMenu, Palette, Toolbar, HoverStrip, SelectionBar, Banner, Automation }
// Core/Mail/Model (F adds): one message an action applies to; RowId is the list row id (unified rows encode the source).
public sealed record MailTarget(string AccountKey, string AccountId, string EmailId, string ThreadId, string RowId,
                                IReadOnlyDictionary<string, bool> MailboxIds, Keywords Keywords);
public sealed record MailboxRef(string AccountKey, string AccountId, string MailboxId, string? Role);
public static class CommandIds { /* F: the complete list, one constant per action in Rust ctxmenu.rs `pick` (:830),
   reader.rs `menu`/`pick` (:986, :1084), palette.rs `commands` (:92), shortcuts.rs tables, Bulwark message-actions.ts,
   mailbox-context-menu.tsx, mail-list-menus.tsx, account-switcher.tsx; the owner stream of each id in a comment. */ }
```
A command id without a registered handler shows **disabled** in menus and the palette and is a no-op for keys (so wave-1 menus can
name wave-2 commands such as quick rules and import).

### 4.2 Menus, dialogs, overlays (owner menus)
```csharp
// Presentation/Menus. spec/menus.md; Rust ctxmenu.rs (it/sub/danger/off/sep/header :34-41, confirm :697, request :150).
public enum MenuItemKind { Item, Separator, Header, Submenu }
public sealed record MenuItemSpec(MenuItemKind Kind, string Label, string? CommandId = null, string? Argument = null,
    string? Hint = null, string? Icon = null, bool Danger = false, string? ConfirmLabel = null, bool Enabled = true,
    bool? Checked = null, string? SubmenuId = null, int Indent = 0);
public sealed record MenuContext(string MenuId, IReadOnlyList<MailTarget> Targets, MailboxRef? Mailbox, string? TagId,
    string? Address, int? AttachmentIndex, CommandSurface Surface);
public interface IMenuProvider { string MenuId { get; } IReadOnlyList<MenuItemSpec> Build(MenuContext context); }  // multi
public interface IContextMenuService { void Show(MenuContext context, MenuAnchor anchor); void Close(); bool IsOpen { get; } }
public readonly record struct MenuAnchor(double X, double Y, string? ElementAutomationId);
public static class MenuIds { /* MENU-MAIL, -THREAD, -ADDR, -ATT, -READER, -BODY, -FOLDER, -TAG, -LIST (spec/menus.md),
   plus ACCOUNT, FOLDERS-HEADER, UNIFIED-ROW (Bulwark account-switcher.tsx, sidebar.tsx:2074, :2867) */ }
// Presentation/Dialogs. PORT-PLAN §3.7: a custom host (Scrim + PopIn), never ContentDialog.
public interface IDialogService { Task<TResult?> ShowAsync<TResult>(DialogRequest<TResult> request, CancellationToken ct); bool IsOpen { get; } }
public abstract record DialogRequest<TResult>(string Title);       // content records per dialog live with their owner
// App/Shell (F): an overlay the shell places in OverlayLayer.
public interface IShellOverlay { UIElement View { get; } int ZIndex { get; } }
```
Providers: menus provides MENU-MAIL, -THREAD, -ADDR, -BODY, -FOLDER, -TAG, -LIST, ACCOUNT, FOLDERS-HEADER; reader provides
MENU-READER and MENU-ATT; unified provides UNIFIED-ROW. Commands come from their owners.

### 4.3 Writes, undo, toasts (owner actions)
```csharp
// Core/Mail/Write. RFC 8621 §4.6 (Email/set), §4.7 (Email/copy), §4.8 (Email/import), §2.5 (Mailbox/set); RFC 8620 §5.3, §6.1;
// Bulwark lib/jmap/client.ts batchUpdateKeywords :3859, batchDeleteEmails :4090, batchMoveEmails :4134, batchArchiveEmails :4162,
// removeEmailsFromMailbox :4360, restoreEmails :4375, emptyMailbox :4389, markMailboxAsRead :4474, markAsSpam :4577, undoSpam :4616,
// createMailbox :4636, updateMailbox(es) :4686/:4702, deleteMailbox :4729, uploadBlob :6662, importEmail :6770,
// copyEmailAcrossAccounts :12278, importUploadedEmails :12368; Stalwart crates/jmap/src/{email/{set,copy,import},mailbox/set,blob/upload}.rs.
public interface IMailWriter {                              // one per account session; chunks by maxObjectsInSet (J-009)
    Task<Result<SetReport>> UpdateKeywordsAsync(IReadOnlyList<string> emailIds, IReadOnlyDictionary<string, bool> patch, CancellationToken ct);
    Task<Result<SetReport>> MoveAsync(IReadOnlyList<string> emailIds, string? fromMailboxId, string toMailboxId, CancellationToken ct);
    Task<Result<SetReport>> SetMailboxIdsAsync(IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> mailboxIdsById, CancellationToken ct);
    Task<Result<SetReport>> DestroyAsync(IReadOnlyList<string> emailIds, CancellationToken ct);
    Task<Result<int>> EmptyMailboxAsync(string mailboxId, CancellationToken ct);
    Task<Result<int>> MarkMailboxReadAsync(string mailboxId, bool withChildren, CancellationToken ct);
    Task<Result<Mailbox>> CreateMailboxAsync(string name, string? parentId, string? role, CancellationToken ct);
    Task<Result<SetReport>> UpdateMailboxesAsync(IReadOnlyDictionary<string, MailboxPatch> patches, CancellationToken ct);
    Task<Result<Unit>> DestroyMailboxAsync(string mailboxId, bool removeEmails, CancellationToken ct);
    Task<Result<UploadedBlob>> UploadAsync(ReadOnlyMemory<byte> data, string type, CancellationToken ct);
    Task<Result<EmailImportReport>> ImportAsync(IReadOnlyList<EmailImportEntry> entries, CancellationToken ct);
    Task<Result<SetReport>> CopyToAccountAsync(string fromAccountId, IReadOnlyList<EmailCopyEntry> entries, CancellationToken ct);
}
public interface IMailWriterFactory { IMailWriter For(IAccountSession session, string? accountId = null); }  // accountId: shared accounts
// SetReport (updated/destroyed/created ids, notUpdated/... as SetError), MailboxPatch, EmailImportEntry, EmailImportReport,
// EmailCopyEntry: records, one per file.
// Presentation/Actions
public interface IUndoService {                             // Bulwark lib/undo-history.ts:20-65 (10 entries), mail-app.tsx:1545 (z, Ctrl+Z)
    UndoEntry Record(string label, Func<CancellationToken, Task<Result<Unit>>> undo);
    Task<bool> UndoLatestAsync(CancellationToken ct);
    IReadOnlyList<UndoEntry> History { get; }
}
public interface IToastService { ToastHandle Show(ToastRequest toast); void Dismiss(ToastHandle handle); }  // lib/email-action-toast.ts, toast-duration.ts
public interface IMailSelection {                           // the list's selection as targets (menus, palette, toolbar read it)
    IReadOnlyList<MailTarget> Current { get; }
    event EventHandler? Changed;
}
```
`IMailActions` (the reader's mark-read path from M1) is replaced by actions with a real implementation that writes only when the
account is writable (`ReadOnlyPolicy`); `<readonly-account>` keeps `CanWrite = false`.

### 4.4 List seams (F creates with identity defaults; owners in wave 2)
```csharp
public interface IListQueryShaper { ListQuery Shape(ListQuery query); }                       // search: gsearch filter, sort, collapseThreads
public interface IListSourceResolver { IListSource Resolve(ListQuery query); }                // unified: __unified and shared sources
public interface IListSource {                              // default: the active account's IMailService/IMailSync, exactly as in M1
    Task<Result<ListPage>> QueryAsync(ListQuery query, ListCursor? after, CancellationToken ct);
    ChannelReader<MailSyncEvent>? Events { get; }
}
public interface IMailTargetResolver { MailTarget? Resolve(MessageRowViewModel row); }       // unified: unified row ids → real account and id
// Core/Mail/Model/ListQuery (F adds init-only members): JsonObject? Filter (AND-ed by ListFilters), IReadOnlyList<SortComparator>? Sort,
// bool? CollapseThreads.
public interface ISnippetSource {                           // search → list rows (actions renders the marks, I-232)
    IReadOnlyList<TextSegment>? SubjectFor(string emailId);
    IReadOnlyList<TextSegment>? PreviewFor(string emailId);
    event EventHandler<SnippetsArrivedEventArgs>? Arrived;
}
```

### 4.5 Reader-facing seams
```csharp
// owner reader
public interface IGuardedHttp {                             // Core/Net: the only outbound HTTP outside the JMAP transport
    Task<Result<GuardedResponse>> SendAsync(GuardedRequest request, CancellationToken ct);
}   // https only (loopback allowed in tests only), every resolved address public (Rust reader.rs:150 public_ip), connect pinned to the
    // checked address, no redirects, no cookies, no credentials, size and time caps; GuardedRequest has Method, Uri, form body, Accept.
public interface IAvatarService { AvatarImage? TryGet(string email, string? brandDomain, bool junk); event EventHandler<AvatarArrivedEventArgs>? Arrived; }
public interface IVerificationCodeFinder { string? Find(string? subject, string? text); }   // Rust otp.rs:282, Bulwark lib/verification-code.ts:275
public interface IAttachmentSaver {                         // download, save as, .eml, zip, Save to Files; Bulwark reader-attachments.tsx
    Task<Result<Unit>> SaveAsync(MailTarget message, AttachmentRef attachment, CancellationToken ct);
    Task<Result<Unit>> SaveAllAsZipAsync(MailTarget message, CancellationToken ct);
}
public interface ITrustedSenders { bool IsTrusted(string address); Task<Result<Unit>> AddAsync(string address, CancellationToken ct); Task<Result<Unit>> RemoveAsync(string address, CancellationToken ct); IReadOnlyList<string> All { get; } }
public interface IReaderBannerSource { IAsyncEnumerable<ReaderBanner> BannersFor(ReaderSnapshot snapshot, CancellationToken ct); }  // multi
// owner unified (reader consumes)
public interface IAttachedMessageOpener { bool IsAvailable { get; } Task<Result<EmailBody>> OpenAsync(MailTarget container, AttachmentRef attachment, CancellationToken ct); }  // Email/parse
public interface IAttachmentExpander { bool Handles(AttachmentRef attachment); Task<Result<IReadOnlyList<AttachmentRef>>> ExpandAsync(MailTarget message, AttachmentRef attachment, CancellationToken ct); }  // TNEF; multi
public interface IEmailDetailsSource { Task<IReadOnlyList<EmailDetailRow>> RowsFor(EmailBody email, CancellationToken ct); }  // plugins → reader-below-subject; multi
// Core/Raster (F stub, owner reader): Raster(int Width, int Height, byte[] Rgba) for avatars (T-024).
```

### 4.6 Accounts and import (owner unified; menus and actions consume)
```csharp
public interface IAccountMaintenance {                      // Bulwark components/layout/account-switcher.tsx:442-675, lib/sign-out-cleanup.ts
    Task<Result<Unit>> SignOutAsync(string accountKey, CancellationToken ct);   // onBeforeLogout, onAfterLogout, onAccountRemove; R2-06 first
    Task<Result<Unit>> SignOutAllAsync(CancellationToken ct);
    Task<Result<Unit>> SetDefaultAsync(string accountKey, CancellationToken ct);
    Task<Result<Unit>> MoveAsync(string accountKey, int newIndex, CancellationToken ct);
}
public interface IUnifiedUnread { long Total { get; } event EventHandler? Changed; }        // Rust unified.rs:139, :400
public interface IMailImport {                               // Rust mailimport.rs:281, Bulwark lib/mail-import/engine.ts
    Task<Result<ImportOutcome>> ImportFilesAsync(MailboxRef target, IReadOnlyList<string> paths, IProgress<int> progress, CancellationToken ct);
}
// Core/Plugins (unified): INativePluginHost { IReadOnlyList<PluginInfo> Plugins { get; } } and the budgets of R2-10.
```

### 4.7 Automation (owners as named)
Each stream adds `IAutomationCommand`s under `#if AUTOMATION` in its own folder (`spec/automation-protocol.md` gets one row per
command, appended by the owner): actions `dnd-sim SRC DST`, `hover-sim ROW|off`, `pane-set SIDE LIST`, `select ROWID [shift|ctrl]`,
`toast-last`; menus `menu-open MENU TARGET`, `menu-pick LABEL`, `menu-items`, `palette TEXT`, `palette-pick N`, `dialog-state`,
`key CHORD` (the full table); reader `rx-banners`, `banner-act ID ACTION`, `source-text`, `avatar-state EMAIL`; search
`search TEXT`, `suggestions`, `chips`; unified `unified on|off`, `import-sim PATH MAILBOX` (FakeJmap only), `plugins`. Write-capable
commands (`rx-import`, `rx-find`, `rx-destroy`) exist only under `AUTOMATION_WRITES` (reader, §6.3).

## 5. Fixed stage F `f-spec-tests` (Sonnet): red tests, contracts, seams, shared files
**Branch** `m2-f-spec-tests` from `m2`, worktree `<downloads>/wt-m2-f-spec-tests`. Merged into `m2` before wave 1.

0. **Linux baseline (first, before any change).** `tools/linux.ps1 ping` (short timeout, one try). If it answers:
   `tools/linux.ps1 test -Branch m2` (m2 is m1 at this point: the **first Linux run of M1**). Record the result in F's Fable section
   and in a short `spec/feel/M2/linux-baseline.md` (counts, failures with test names; no fixing in F: failures are listed as
   "M1 on Linux" items for the integrator and the user). If it fails: "Linux pending (VM unreachable)".
1. **Red tests (§2.1):** the 89 M2 rows and the 14 early rows (T-271..T-279, T-122..T-126), each a faithful port of the Rust test
   body (same inputs, same asserts), tagged `[RustTest("<tag>")]` and `[Red("<owner>")]`, in the owner's test folder (§3.1). The D-
   entries of §2.1 (D-234 and any other). Fixtures from the Rust tests go to `tests/fixtures/mail/m2/` (`example.org` addresses,
   sample text only). Then `python tools/spec-check.py --write`, `--milestone M2` (expected: 89 mapped to red tests, status still
   `pending`), `python tools/red-check.py` (every red test fails for the right reason).
2. **Contracts and seams (§4)** as compiling stubs in the owners' folders, each file with its module header (Rust file:line,
   Bulwark ref, RFC/Stalwart path). `CommandIds` and `MenuIds` complete (from the sources named in §4.1, §4.2), each id with its
   owner in a comment. The list seams of §4.4 wired into `MessageListViewModel` with identity defaults (behaviour unchanged: the M1
   list tests stay green), the two empty partial files, the new `ListQuery` members (unused by default).
3. **Shared files:** the projects, packages and switches of §3.2; the five registration files and the `AppComposition` calls (§3.3);
   `OverlayLayer` + `IShellOverlay`; the slots in `MessageListView`, `SidebarView`; the search box moved to `SearchBoxView` with its
   handler (no behaviour change); `AppCompositionTests`.
4. **FakeJmap:** dispatch arms in `FakeApi.cs` for `Mailbox/set`, `Email/import`, `Email/copy`, `Email/parse`, `Blob/copy`,
   `SearchSnippet/get`, `EmailSubmission/set`, `SieveScript/get|set|validate`, `FileNode/get|query|set`, `CalendarEvent/parse`,
   `x:ArchivedItem/get|query|set`, each calling a partial method in its own file (`FakeApi.<Name>.cs`) that F leaves as
   `Error("unknownMethod")` for the owner to fill (§3.1). Session capabilities those methods need (`submission`, `sieve`,
   `filenode`, `calendars`, `urn:stalwart:jmap`), off by a `FakeJmapOptions` flag each so M1 tests see the M1 session. **Seed
   (`FakeSeed.cs`, then frozen):** a newsletter with `List-Unsubscribe` + `List-Unsubscribe-Post` + a DKIM-pass
   Authentication-Results under the server's id; a message with `Disposition-Notification-To` equal to its Return-Path, and one with
   a third-party requester; a message with a `text/calendar` invitation part; a verification-code mail and a promo-code mail; a
   message with a `message/rfc822` attachment and one with an `.eml` attachment; a `winmail.dat` (TNEF) attachment built from a
   synthetic TNEF fixture; a three-attachment message with duplicate names; `X-Spam-Status` headers; Trash, Junk, Archive, Sent and
   Drafts with messages; a nested folder tree with a role folder; two tags and a nested tag; a 3 MB message and a 1.6 MB HTML body
   (L-014); a second account and a shared account (data only; the session part is unified's in wave 2). All synthetic, `example.org`.
5. **Hooks:** `hooks.csv` milestone and owner columns per §2.4 (14 re-tags with the reason in the README's "deliberate changes");
   `HookCallSiteTests` one red test per M2 owner; `python tools/gen-hooks.py --check`.
6. **Spec bookkeeping:** `spec/README.md` lists `spec/feel/M2/`; `spec/automation-protocol.md` gets an "M2" section with the command
   names of §4.7 marked "planned" (owners fill the meaning).

**Exit:** both OSes build (Linux if reachable); `tools/build.ps1 test` green (red tests excluded); `red-check` passes with 103 red
tests plus the call-site reds; `spec-check --milestone M2` lists 89 rows mapped (pending); the composition test resolves everything;
`tools/build.ps1 rules` passes; no production behaviour change (the M1 suite unchanged). **Hidden-automation scenario:** AUTOMATION
build on port 8880, profile `auto-8880`, `BARBICAN_SAMPLE=1`: `wshot`, `tree` shows the empty `OverlayLayer` and the slots, search box
still works (`text list.search "seed"`, Enter), `probe-start wgc`, 2 s idle, `probe-stop` (0 frames), `focus-guard` unchanged,
`close`. **Fable items:** every assert not ported verbatim, the hook re-tags, MimeKit's AOT result, the list seams (that the default
path is byte-for-byte the M1 behaviour), the Linux baseline.

## 6. Stream briefs

### 6.1 `actions` (Sonnet, wave 1): mail and folder actions, undo, multi-select, selection bar, hover actions, drag and drop, panes, tags
**Spec to close:** T-083..T-090; J-009, J-034 (partial), J-047, J-048, J-051, J-057 (call), J-059, J-074; the actions I- rows of §2.3;
the 28 hooks of §2.4; B- rows of `dnd.md`, `mail.md` (actions and tags); L-022 (mail), L-039, L-044, L-057 (scripts); G-048; R2-12.

**Read:** PORT-PLAN §3.3, §3.7 (lists, motion), §4.5, §4.6; `spec/menus.md` (the actions every menu calls); `spec/behaviours/dnd.md`,
`mail.md`; Rust `src/mail.rs` (`apply` :925, `confirm_action` :940, `forced` :960, `resolve_toggle` :965, `apply_confirmed` :978,
`archive_path` :1064, `archive_dated` :1074, `patch_local` :887, `remove_local` :901, `next_after` :871, `toggle_check` :829,
`select_all` :837, `show_toast` :1102), `src/dnd.rs` (`already_in` :42, `drop` :52, `plan_folder_drop` :149, `moved_event` :193,
`clamp_panes` :251, `load_panes`/`save_panes` :256-264, pane limits :242-247), `src/tags.rs` (`add` :147, `rename` :162, `remove`
:173, `set_color` :180), `src/ctxmenu.rs` (`mailbox_op` :751, `mark_read_query` :724, `edit_tags` :777). Bulwark
`lib/jmap/client.ts` (the methods in §4.3), `stores/email-store.ts` (optimistic updates and rollback), `lib/undo-history.ts:20-65`,
`lib/email-action-toast.ts`, `lib/selection-toast.ts`, `lib/toast-duration.ts`, `lib/mail-removal-confirm.ts`, `lib/archive-gate.ts`,
`lib/archive-inbox.ts`, `lib/spam-applicable.ts`, `lib/trash-origin.ts`, `lib/trash-restore-target.ts`, `lib/move-menu.ts`,
`lib/move-recent.ts:26-31`, `lib/move-targets.ts`, `lib/folder-tree-dnd.ts:41-134`, `lib/list-step.ts`, `lib/account-archive*.ts`
(J-074), `components/email/{email-list,thread-list-item,email-hover-actions,selection-view,selectable-avatar,mv-list-toolbar,mv-list-section-header,move-to-dialog,move-target-list,tag-picker,batch-tag-button,trash-recovery-link,list-a11y}.tsx`,
`components/layout/{sidebar,resize-handle}.tsx`, `stores/ui-store.ts` (pane sizes). Stalwart `crates/jmap/src/email/{set,copy,import}.rs`,
`mailbox/set.rs` (`mailboxHasEmail`, `mailboxHasChild`, `onDestroyRemoveEmails`), `blob/upload.rs`,
`crates/jmap/src/registry/mapping/archived_item.rs`.

**Owns:** §3.1.

**Builds:**
- `MailWriter : IMailWriter` and its factory: patches as JSON Pointers (the M1 `keyword_path` port), chunking by `maxObjectsInSet`,
  every `notUpdated`/`notDestroyed` mapped to a user message (M1's `set_error` port), writes never retried (M1 transport rule),
  `ReadOnlyViolation` passed through as "This account is read-only." Email/import and upload (the call; the loop is unified's),
  Email/copy across accounts (I-177, G-048), Mailbox/set create/rename/move/destroy (J-047), empty folder, mark folder (and
  subfolders) read, `x:ArchivedItem` restore (J-074; hidden without the capability).
- Commands (one `IAppCommand` per id, registered in `ActionsRegistration`): read/unread, star, tags (tri-state for a batch:
  checked when all have it, mixed when some), move (filter, recent folders per account, other accounts, "Choose folder…" dialog
  through `IDialogService`), archive by role and `archiveMode` (single, year, month: `archive_path`; creates the archive folder
  when missing, J-045 remainder), junk/not junk (keywords `$junk`/`$notjunk` as Bulwark), delete (to Trash; in Trash or with
  `deleteAction` permanent: destroy, asking first when N > 1 and `confirmBulkDelete`), restore (from Trash to the origin folder),
  move to Inbox (in Archive), delete forever, remove from folder (labels mode), pin/unpin (`$pinned`), empty Trash/Spam (asks),
  mark folder read, new folder/subfolder, rename, delete folder (asks), refresh, select all in folder, list navigation and
  selection commands (next, previous, extend up/down, open, close, select all, clear), undo. Each fires its intercept hook before
  the write (false stops it, no write) and its observer after the server accepted it.
- **Optimistic local state with rollback:** the list and the sidebar counts change at once (rows leave through `ListSync`: no reset,
  no flash), the reader moves to the next message (`next_after`), and a failed write restores the rows and shows the error toast.
  The push delta that follows must not double-apply (L-039: the delta after a menu action updates the list without a reload).
- `UndoService` (10 entries, z and Ctrl+Z, the toast's Undo button), `ToastService` and the toast host overlay (in-window, PopIn,
  `RaiseNotificationEvent` for Narrator), `IMailSelection`.
- Multi-select: Ctrl-click, Shift-range, Shift+arrows/Shift+J/K, the row tick and avatar gutter (I-120, I-123, I-124, I-126 whole
  thread), select all with the "Select all N in folder" banner (I-142, resolved with `Email/query` ids when acting), the selection
  bar (archive, delete, read, unread, move, tag, more, spam, clear, count "N selected · M unread"), the tri-state select-all toggle.
- Hover actions: the strip on the row (delete, star, read/unread swap, archive or "Move to Inbox" in Archive, tag picker, spam/not
  spam), shown on keyboard focus too (I-136), floating mode and corner (I-137). Hover itself is not injectable: the VM state is
  driven by `hover-sim` in automation; real hover is a hand test.
- Drag and drop: rows to folders and tags (move + undo, tag add; `already_in` skip, T-083), folders reorder and reparent
  (`plan_folder_drop`, cycles and role folders refused, T-086..T-088), the drop-target highlight, the drag ghost in-window; tag
  dropped onto messages (onTagDrop). The pure event moves (T-085, T-089) as Core functions for M4. Real drag is Layer C (hand
  test); `dnd-sim` calls the same drop path.
- Panes: sidebar and list widths and the horizontal split with `GridSplitter` (CommunityToolkit Sizers, already pinned), arrows and
  double-click reset (I-071), clamped (`clamp_panes`, T-090, NaN to defaults), stored in `panes.json` in the Rust format
  (`Core/Layout`), applied once at start (no resize animation, no relayout flash), onColumnResize.
- Tags: create (nested with `choose_id`), rename, colour, delete (messages keep the keyword) through `IPrefsEditor`
  (`emailKeywords`), tag row click filters and a second click clears (I-055), nested chevron and "Show all N tags" (I-057).
- Sidebar: "+" new top-level folder (I-048), ArrowRight/Left expand (I-060), right-click raising MENU-FOLDER / MENU-TAG through
  `IContextMenuService`, drop targets, collapse toggle (I-034), myRights: no drop and disabled items where `mayAddItems`,
  `mayRemoveItems`, `mayRename`, `mayDelete` are false (J-048, shared with menus' enable rules).
- List: row right-click and the row "…" button raise MENU-MAIL with the selection (Bulwark: the selection when the row is in it,
  else the row); list background raises MENU-LIST; snippet marks rendered from `ISnippetSource` (I-232; plain text until search
  lands); the verification-code chip from `IVerificationCodeFinder` (I-130, `showVerificationCodes`); avatars from
  `IAvatarService` when the setting is on; date group fold (I-148); the empty Trash/Spam banner (I-143); R2-12 (scroll only when
  the selected id changed).
- Settings read (`csharp_read_by`): `archiveMode`, `confirmBulkDelete`, `deleteAction`, `confirmArchive`, `showVerificationCodes`,
  the hover-actions and list keys Bulwark has.

**Provides:** `IMailWriter(Factory)`, `IUndoService`, `IToastService`, `IMailSelection`, the action and list commands, the real
`IMailActions`, panes. **Consumes:** menus (`IContextMenuService`, `IDialogService`, `ICommandRegistry`), reader
(`IVerificationCodeFinder`, `IAvatarService`, `IAttachmentSaver`), search (`ISnippetSource`, wave 2), unified
(`IMailTargetResolver`, wave 2), all as F stubs during the wave.

**Exit tests:** the 8 T- rows green; `MailWriter` against FakeJmap (each method, chunking at a `maxObjectsInSet` of 2, set errors
mapped, read-only refused locally with no request sent: count requests); Presentation tests against FakeJmap: delete N with undo
(rows leave and come back as inserts, no `Reset`), move with rollback on a `notUpdated` error, archive by month creating the
folder, junk/not junk keywords, mark folder read, empty Trash with the confirmation, folder reparent refused for a cycle, tag
tri-state for a mixed batch, push delta after an action applies once, intercept hook false stops the write (zero requests),
Shift-range across a date group header, select all in folder acts on the query ids; hook call-site test green for actions' 28
hooks. **Feel scenarios** (AUTOMATION build, port 8881, sample data, Bento and Nordic, dark and light): `delete_undo` (Delete on the
open message, then z: the row leaves and the next message shows with 0 BLANK/FLASH, input to first changed frame ≤ 1 quantum),
`select_range` (Shift+Down ×5 at 33 ms: no frame over 25 ms), `drop_move` (`dnd-sim`), `pane_resize` (`pane-set`, 0 BLANK),
`hover_strip` (`hover-sim` sweep: strip appears in one frame, nothing animates over 200 ms), `move_dialog` (the Move-to dialog:
part of the **dialog** scenario, measured with menus' dialog host after the wave merge). **Hidden-automation scenario:** start on
8881 with `BARBICAN_SAMPLE=1`; `select` rows with shift, `wshot` of the selection bar; `key Delete`, `toast-last`, `key z`;
`dnd-sim` a row onto a folder, then onto a tag; `pane-set 220 360`, restart, check `panes.json` applied; `popup-guard` 0 open;
`focus-guard` unchanged; `close`. **Live (integrate stage, `<test-account>`, §7):** import two marked test messages (`rx-import`),
star/unstar, tag/untag, move to a test folder created and deleted by the script, delete and undo, delete forever, empty a test
folder; L-039 (delta after an action, no reload); L-044 tags (create nested, rename, delete); L-057 remove the Rust leftovers
"MXTEST …" from the Inbox; cleanup: `rx-find` returns nothing marked, the test folder is gone.

### 6.2 `menus` (Sonnet, wave 1): context menus, dialog host, account menu, rail, palette, shortcuts
**Spec to close:** T-201..T-205, T-260..T-270; the menus I- rows of §2.3; B- rows of `shortcuts.md`, `palette.md`; `spec/menus.md`
items for MENU-MAIL, -THREAD, -ADDR, -BODY, -FOLDER, -TAG, -LIST and the account menu; L-047 (script); R2-11; the
`IBodyOverlayStrategy` caller.

**Read:** PORT-PLAN §3.7 (menus, keyboard, `ContentDialog` replaced), §4.4, §4.7; ADR 0005 (menus over the body, the Linux
stand-in for dialogs and scrims), ADR 0007 (constrained popups, the deny list); `spec/menus.md` (all); Rust `src/ctxmenu.rs`
(`row_h` :94, `fill` :98, `show` :107, `reopen` :113, `hover` :134, `request` :150, `mail_items` :516, `folder_items` :551, `submenu`
:575, `confirm` :697, `pick` :830, confirmation labels :833-852), `src/palette.rs` (`Cmd` :47, `command_matches` :61, `Ctx` :70,
`commands` :92, `filter` :178, `open` :246, `rebuild` :269, `move_active` :423, `enter` :437, `run_command` :485), `src/shortcuts.rs`
(tables, `resolve` :108, `mail` :138, `is_single_key` :105, `GO_TO_SEQUENCE` :31, `step` :602, help rows; tests :704-841). Bulwark
`components/email/{email-context-menu,conversation-message-menu,address-context-menu,mail-list-menus,reader-selection-menu}.tsx`,
`components/email/message-actions.ts`, `message-action-shortcuts.ts`, `components/layout/{mailbox-context-menu,account-switcher,navigation-rail,sidebar}.tsx`,
`components/command-palette/`, `components/keyboard-shortcuts-modal.tsx`, `lib/command-registry.ts:53-96`, `lib/app-commands.ts`,
`lib/keyboard-shortcuts/{index,mail,calendar,contacts,files,settings,types}.ts`, `lib/keyboard.ts` (:96-101 the typed character wins,
:355 dialogs block app keys, :393 layout-aware keys), `lib/shortcut-keys.ts`, `lib/single-key-shortcuts.ts`, `lib/menu-placement.ts`,
`lib/popover-shift.ts`, `components/layout/pane-cycle.tsx`, `focus-rescue.tsx`, `app-landmarks.tsx`.

**Owns:** §3.1.

**Builds:**
- `CommandRegistry` over the registered `IAppCommand`s. A missing handler means disabled (§4.1).
- **Menu host** (App): one in-window, constrained `Popup` (never a windowed `MenuFlyout` popup; ADR 0005/0007) with 30 px rows, 9 px
  separators, 24 px headers (Rust `row_h` :94), line icons (`SIcon`), shortcut hints, danger rows, `›` submenus beside the row
  (hovering another row closes it), confirmations swapped in place ("Are you sure?" + the danger label + Cancel, Esc), PopIn from the
  anchor corner (150 ms, reduce motion 0), placement kept inside the window (Bulwark `lib/menu-placement.ts`), keyboard (arrows,
  Home/End, Enter, Esc, Left/Right for submenus, type-ahead), enable state evaluated when the menu opens (no stale state, spec
  "Open questions"). Over the WebView2 body on Windows it composes directly (ADR 0005); on Linux menus need no stand-in (ADR 0005)
  but the check is in the Linux run.
- **Menu providers** MENU-MAIL (with the "N selected" header, Move to ›, Tag ›, More actions ›), MENU-THREAD, MENU-ADDR, MENU-BODY,
  MENU-FOLDER, MENU-TAG (Colour ›), MENU-LIST, FOLDERS-HEADER (mark all read, new folder, refresh; I-049), ACCOUNT, exactly as
  `spec/menus.md` item by item, with myRights in the enable rules (J-048). Submenu content (folders indented by depth, virtual and
  current folder left out, tags with colours and checks) is built when opened.
- **Dialog host** (App): Scrim + PopIn, focus trapped and restored, Esc and Enter, in-window; on Linux it calls
  `IBodyOverlayStrategy` before showing the scrim (the X11 stand-in; M1.md §5 "no caller"). Prompt dialog (new folder, rename, new
  tag) and confirm dialog for the owners to use.
- **Account menu** (the popover in full): closes on outside click and Esc (I-003), arrows/Home/End (I-004), drag reorder and
  Alt+Shift+Up/Down (I-007, I-008, through `IAccountMaintenance.MoveAsync`), set as default (I-013), sign out of one or all (I-014,
  I-015, asking first), MV popover parts (I-016, I-018), help links (display), unread badges per row, the unified inbox row (I-017,
  shown when unified's `IUnifiedUnread` is registered), rail account circles, "+" and sign-out menu (I-031). Manage accounts is
  shown disabled with "Settings arrive in M6".
- **Rail:** keyboard-shortcuts button (I-029), search button (I-023 command id; the command is search's), tooltips in-window
  (`Tip`, 350 ms; hover is a hand test).
- **Command palette** (Ctrl+K and k): the palette.rs model (T-201..T-205: every word matches, app commands first, mail commands only
  with an open message, account switch for other accounts only, capability-gated commands, "Go to <page> settings" rows that say
  the settings pages come in M6), keys (arrows, Enter, Esc, Tab between scopes), providers through `IPaletteProvider` (search adds
  the global-search scope in wave 2), Go-to/compose (M3: disabled)/theme/switch account/sign out (I-454).
- **Shortcuts:** the shortcuts.rs tables as data (mail, list, composer, calendar, palette and help everywhere), g-sequences, single
  keys off leaves named keys (T-262), Backspace never deletes (T-260), `?` sheet built from the help rows (T-269), the KeyRouter
  extended to run commands by id through `ICommandRegistry` while **every M1 key test stays green unchanged**; dialogs and open
  menus block app keys (I-168, `KeyGate`); R2-11: punctuation (`/`, `?`) from the typed character (`CharacterReceived` or
  `ToUnicodeEx` on Windows, the Uno equivalent on Linux), letters from the virtual key; Shift+digit and AltGr rules kept (M1 F-16).
- Skip to message list (I-270) and F6 pane cycling (PORT-PLAN §4.7, Bulwark `pane-cycle.tsx`).

**Provides:** `ICommandRegistry`, `IContextMenuService`, `IDialogService`, the menu host, the palette, the shortcut tables.
**Consumes:** every stream's commands; unified's `IAccountMaintenance`, `IUnifiedUnread` (stubs until wave 2); actions'
`IMailSelection`.

**Exit tests:** the 16 T- rows green; menu spec tests (each MENU-* item list against `spec/menus.md` for N = 1 and N > 1, in
Trash/Junk/Archive, role and custom folders, with and without tags, read-only rights); confirmation swap; enable state evaluated at
open (a command that turns disabled between two opens); palette ranking and gating; key table tests (M1 `KeyRouter` tests unchanged
and green; `/` on a German layout through the mapper's character path); a popup lint test that the menu host and dialog host are
constrained. **Feel scenarios** (the Rust baseline names, `spec/feel/rust-baseline/`): **`ctx_menu`** (right-click a row, menu shows,
Escape; over the list and over the body area: 0 BLANK/FLASH, the menu's first frame within 1 quantum, PopIn ≤ 150 ms) and
**`dialog`** (open the Move-to dialog or the prompt dialog and close it: scrim and PopIn ≤ 150 ms, no frame gap over 25 ms, the body
under the scrim never blanks), plus `palette_open` (Ctrl+K, type, Esc), in Bento and Nordic, dark and light; uniform captures are
invalid (§1 rule 3). **Hidden-automation scenario:** port 8882, sample data: `menu-open MENU-MAIL <row>`, `menu-items`, `menu-pick
"Move to"`, `wshot` (the submenu inside the window), `key Escape`; `rclick` on a folder; `palette "go inbox"`, `palette-pick 0`;
`key ?`, `wshot` of the sheet; `dialog-state`; `popup-guard` 0 open (any unconstrained popup fails the run); `focus-guard` unchanged;
`close`. **Live (integrate stage):** L-047 on `<readonly-account>` (after switching apps back to Mail, Ctrl+K and ? work without a
click), read-only; menus and palette on `<readonly-account>` open but trigger no write (HAND TEST 2 repeats it by hand).

### 6.3 `reader` (Opus, hard, wave 1): reader depth, external HTTP, receipts, trust, write-test guard
**Spec to close:** T-212..T-222, T-177..T-183, T-199, T-200, T-018..T-024, T-311, T-312, early T-122..T-126; J-068, J-036 (partial),
J-075 (partial), J-072 (partial); the reader I- rows of §2.3; the 4 hooks of §2.4; B- rows of `reader.md` (actions and banners, MDN,
OTP, avatars), `extras.md` ZIP and INV (display); L-011..L-015, L-017, L-019, L-037 (scripts); G-056; R2-07, R2-08, R2-13 (App
points), R2-14.

**Read:** PORT-PLAN §3.6, §3.12 (MimeKit, DnsClient), §4.6; ADR 0005, 0007; FABLE-REVIEW "M1 consolidated" §1 items 1-3 (frame
sandbox, CSP-only Linux, remote content per page) and fable-review-2 R2-04, R2-07, R2-08, R2-14; Rust `src/reader.rs` (`header` :27,
`headers_all` :33, `unfold` :38, `Unsub` :54, `valid_unsub_url` :74, `parse_unsubscribe` :83, `unsubscribe_mailto` :117, `dkim_pass`
:143, `public_ip` :150, `one_click_post` :169, `post_form` :179, `strip_subject_prefixes` :194, `forward_subject` :213, `sanitize_part`
:219, `eml_export_name` :233, `bundle_name` :241, `displayed_truncated` :247, `source_shown` :252, `banners` :412, `unsub_banner`
:451, `invite_banner` :473, `banner_action` :530, `unsubscribe` :593, `load_full_body` :635, `view_source` :682,
`is_message_attachment` :728, `save_eml` :803, `download_all` :824, `load_files` :855, `folder_tree` :887, `blob_copy_request` :907,
`copied_blob` :912, `save_to_files` :919, `file_node` :949, `menu` :986, `submenu` :1027, `att_items` :1071, `pick` :1084), `src/mdn.rs`
(all; `build` :80, `auto_send_ok` :168, `should_offer` :185, `pick_identity` :195, `send` :202), `src/otp.rs` (`find` :282),
`src/avatars.rs` (`root_domain` :84, `candidates` :109, `get` :122, worker :147-172, `get_capped` :231, `raster` :281,
`bimi_logo_url` :289, `safe_https_host` :298, `txt_records` :308), `src/zip.rs` (all), `src/invite.rs` (`find_part` :36,
`method_from_ics` :49, `organizer` :76, `my_participant` :88, `when` :141, `location` :163, `parse` :191, `trust` :250). Bulwark
`components/email/{unsubscribe-banner,read-receipt-banner,calendar-invitation-banner,view-source-dialog,reader-attachments,save-to-files-dialog,verification-code-chip,brand-verified-badge,external-sender-chip,email-viewer,retention-notice,trash-recovery-link,reader-plugin-surfaces}.tsx`,
`components/trusted-senders-modal.tsx`, `lib/email-headers.ts:625-660`, `lib/validation.ts:71`, `lib/mdn.ts:43-121`,
`lib/receipt-answers.ts`, `lib/calendar-invitation.ts`, `lib/email-source.ts`, `lib/forward-as-attachment.ts:43,80`,
`lib/files-save.ts`, `lib/files-zip.ts`, `lib/download-filename.ts`, `lib/download-guard.ts`, `lib/attachment-name.ts`,
`lib/verification-code.ts:275-346`, `lib/bimi/*`, `lib/root-domain.ts`, `lib/sender-icon-access.ts`, `lib/external-mail.ts`,
`lib/email-sanitization.ts`, `lib/subject-prefix.ts`. Stalwart `crates/jmap/src/{email/import,submission/set,blob/copy,file/{get,query,set},calendar_event/parse}.rs`,
`crates/smtp/src/inbound/data.rs:322,:482-501` (R2-07: where Stalwart writes its Authentication-Results).

**Owns:** §3.1.

**Builds (in this order):**
1. **R2-14 and the write-test guard** (first commit; decision M2-Q2): `ReadOnlyPolicy` lets the `.local` allow-list clear the
   import's read-only flag in the agents' profiles for the logins it names, **except** any login listed in the second `.local` file
   named by `BARBICAN_READONLY_ACCOUNTS_FILE` (fail closed: if that file is missing or unreadable, nothing becomes writable). Tests
   for every combination. Then `src/Barbican.Automation/Writes/` under `#if AUTOMATION_WRITES`: `rx-import` (imports a synthetic
   message marked with a run token into the Inbox of the session account), `rx-find TOKEN`, `rx-destroy ID…` (only ids that carry
   the token); each refuses unless the session's login is on the writable allow-list **and** not on the read-only list, checked at
   run time per command.
2. **`GuardedHttp` (Core/Net):** the only outbound HTTP besides the JMAP transport (the banned-symbol rule for `new HttpClient`
   gets one local suppression with a reason in this file). `SocketsHttpHandler` with a `ConnectCallback` that resolves the host,
   refuses if **any** address is not public (Rust `public_ip` :150: loopback, private, link-local, CGNAT, multicast, unique-local,
   v4-mapped), and connects to the checked address (no second resolution); `AllowAutoRedirect = false`; no cookies, no
   credentials, no proxy; https only (tests may enable loopback http through an internal seam); response size and time caps.
3. **Unsubscribe (RFC 2369, RFC 8058):** header unfolding and multiples (T-212), HTTP and mailto targets (T-213), the banner with
   the steps of Rust `unsub_banner`; **one-click POST** only when `List-Unsubscribe-Post: List-Unsubscribe=One-Click` is present,
   the URL is https, and DKIM passes for the list's domain under the server's trusted Authentication-Results (T-215, R2-07); POST
   `List-Unsubscribe=One-Click` as `application/x-www-form-urlencoded` (T-218), no redirect followed (T-217), private networks
   refused (T-216); always after the user's click (no automatic POST). The mailto route opens nothing until M3 (it shows the
   address and "copy"; I-208 partial note) or sends through EmailSubmission like the receipt path if Bulwark does (check
   `lib/jmap/client.ts:5617`; record the choice). Links go through `onBeforeExternalLink` and the M1 link router.
4. **Read receipts (RFC 8098, J-068):** the requester and offer rules (T-177, T-182), automatic only to the Return-Path (else From)
   (T-178), the two-part report with MimeKit (T-179; `multipart/report; report-type=disposition-notification`), header injection
   neutralised (T-180), base64 at 76 (T-181), identity by recipient (T-183); send = upload + Email/import into Sent + EmailSubmission
   with explicit envelope, destroy the imported copy on failure, then `$MDNSent` (Rust `send` :202); the banner Send/Ignore and
   "always/never" answers (`receipt-answers.ts`); never on `<readonly-account>` (`CanWrite`).
5. **Trust and remote images (I-113, I-209, R2-07):** trusted senders (add, search, remove with undo) in prefs as Bulwark; "Load
   external content" per message (M1) and "always for this sender"; R2-07: an Authentication-Results header counts only inside the
   server's own trace block (below the topmost `Received` whose `by` host equals the authserv-id, above the next), the authserv-id
   is learned and pinned per account, no verdict in Sent and Drafts; D-9 kept.
6. **Invitation banner (display and trust):** find the calendar part (T-122), method (T-123), people (T-124), summary (T-125), trust
   (T-126) via `CalendarEvent/parse` (J-072 partial); RSVP buttons shown disabled with "Replies arrive with Calendar (M4)".
7. **View source** (dialog through `IDialogService`, `source_shown` cut at `SOURCE_DISPLAY_LIMIT`, T-220, copy, "Save .eml"),
   **Download .eml** (`eml_export_name`), **Download all (.zip)** (T-311, T-312 with `System.IO.Compression`, stored entries, UTF-8
   names, unique names), **forward as attachment** (the payload builder, Bulwark `forward-as-attachment.ts:43`; opening it is M3,
   I-170 deferred), **Save to Files** (`folder_tree` T-221, `file_node`, Blob/copy between accounts T-214, J-036/J-075 partial;
   "Loading folders…" until loaded; hidden without the `filenode` capability), the attachment saver for the list chips and the
   reader tiles (Open, Save as through the platform file picker in normal builds and the queued `save` answers in automation, Copy
   file to clipboard, Save all, Show N more), `onAttachmentDownload`, message attachments (T-222) opening through unified's
   `IAttachedMessageOpener` (disabled until wave 2), names (T-219), truncation and full-body fetch (T-220, G-056, L-014).
8. **Verification codes** (T-199, T-200): the finder (Rust `otp.rs` port, Bulwark wording rules), the reader chip with copy, fresh
   for 24 h (`VERIFICATION_CODE_MAX_AGE_MS`), the list chip's data.
9. **Avatars** (off by default: `gravatarAvatars`, `senderFavicons`, `showAvatarsInJunk` false; T-018..T-024): BIMI (DMARC pass from
   the trusted Authentication-Results, `l=` https, the SVG/raster checks of `lib/bimi/`), Gravatar (SHA-256 of the normalised
   address), favicon of the root domain; fetched only through `GuardedHttp`, DNS TXT through DnsClient, a bounded worker queue
   (T-023), a disk cache in the profile's data folder, rasters fitted to a square and 1-pixel images refused (T-024), R2-08 row cap
   in `PngDecoder`; `onAvatarResolve` (transform); the initials stay until the picture is ready (no flicker), and the change is one
   frame.
10. **Reader view:** toolbar actions bound to command ids (I-194..I-199; Reply/Forward disabled until M3), More ⋮ = MENU-READER
    (with the Rules section naming search's commands), MENU-ATT on tiles, MENU-BODY from the shell's `ctx:X,Y`, MENU-ADDR on
    addresses, prev/next and back (I-202), header collapse and avatar click (I-203), details panel (I-206), plain-text notice
    (I-214), conversation cards expand/collapse and "N older" (I-222), tag chip X (I-229, actions' command), retention notice and
    trash recovery link (I-230, actions' J-074 command), the plugin detail rows from `IEmailDetailsSource` (reader-below-subject,
    filled by unified in wave 2), banners from `IReaderBannerSource` in one stack. **Banners appear in the same UI turn as the
    reveal** (no layout jump after the body shows) or, when they arrive later, without moving the body by more than their own
    height in one frame. `onTextSelectionChange` from the shell (debounced).
11. R2-13 App points: `MailBodyView` checks `IsLoaded` in both handlers; `shell.js` fits after images load; `BodyShape.Normalize`
    replaced by non-null lists at the source (coordinate with unified through the integrator if `ModelReader` must change).

**Provides:** `IGuardedHttp`, `IAvatarService`, `IVerificationCodeFinder`, `IAttachmentSaver`, `ITrustedSenders`, the banners, MENU-READER
and MENU-ATT, the AUTOMATION_WRITES commands. **Consumes:** actions (`IMailWriter` for import/destroy/keywords, commands), menus
(`IDialogService`, `IContextMenuService`), unified (`IAttachedMessageOpener`, `IAttachmentExpander`, `IEmailDetailsSource`; stubs),
search (quick-rule commands; disabled until wave 2).

**Exit tests:** the 29 + 5 T- rows green; `GuardedHttp` tests (every private range refused, a DNS answer with one private address
refused, redirect not followed, connect pinned to the checked address, caps) on loopback listeners only; one-click end to end against
a loopback listener; MDN build snapshot (MimeKit parses it back: two parts, the disposition fields) and send against FakeJmap (import
into Sent, submission with envelope, destroy on failure, `$MDNSent`, nothing on the read-only account); R2-07 trace-block tests with a
forged header above and below; R2-14 policy matrix; write-guard tests (refuses a login not on the list, a login on both lists, a
missing read-only list); avatars with a fake HTTP/DNS; zip reads back; banners for each seed message. **Feel scenarios** (port 8883,
sample data): `banner_open` (open the newsletter: banner and body revealed together, 0 BLANK/FLASH), `view_source` (dialog),
`load_images` (trust a sender: the body re-renders with the old one kept until ready), `avatar_arrive` (initials to picture in one
frame, no row flicker), `att_menu` (MENU-ATT over the reader); uniform captures invalid. **Hidden-automation scenario:** port 8883,
`BARBICAN_SAMPLE=1`: open each seed message, `rx-banners`, `banner-act unsubscribe one-click` against FakeJmap's loopback receiver,
`source-text`, `save` queued answers for .eml and zip into the run's temp folder (then checked and deleted), `avatar-state`,
`wv-eval` that the frame CSP still blocks remote loads after "load images" is refused, `popup-guard`, `focus-guard`, `close`.
**Live (integrate stage):** L-011 banner on `<readonly-account>` (read-only) and the one-click POST **only by hand** (HAND TEST 2: a
test newsletter on `<test-account>`); L-013, L-017 downloads to a temp folder (read-only account, deleted after); L-014 big page;
L-015 an MDN from `<test-account>` to itself (requester = Return-Path), then delete the receipt from Sent and the request message;
L-019 avatars with the settings on, on `<readonly-account>` (remote requests only through `GuardedHttp`); L-037 Save to Files on
`<test-account>` into a test folder, deleted after; L-012 the invitation banner (display only).

### 6.4 `search` (Sonnet, wave 2): search, suggestions, history, snippets, global search, quick rules and Sieve core
**Spec to close:** T-102..T-114, T-256..T-258, T-207..T-211, early T-271..T-279; J-056, J-073 (partial); the search I- rows of §2.3;
`onProvideSearchResults`; B- rows of `extras.md` SRCH, `reader.md` quick rules, `sieve-filters.md` (parser/generator rows); L-016,
L-050 (scripts).

**Read:** PORT-PLAN §3.12 (Sieve, quick rules, search grammar by hand); Rust `src/gsearch.rs` (`Scope` :14, `tokenize` :78, `parse`
:133, `with_scope` :181, `without_operator` :190, `matches_terms` :195, `split_search_text` :206, `mail_conditions` :266,
`mailbox_search_filter` :283, `global_mail_filter` :296, `has_remote_query` :308, `chips` :314, `snippet_filter` :339,
`snippet_segments` :365), `src/search_history.rs` (all), `src/search_ui.rs` (`edited` :27, `accept` :67, `remove_chip` :117,
`fetch_snippets` :145, `styled` :189), `src/quick_rules.rs` (all), `src/sieve.rs` (parse, generate, `generate_meta`, `with_rules`),
`src/server_settings.rs` (`save_script`), `src/palette.rs` `search` :355 and `open_hit` :550. Bulwark `lib/global-search/{query-parser,rank,run-global-search,open-hit,accounts}.ts`
and `providers/`, `components/global-search/`, `components/search/`, `lib/search-suggestions.ts:15-107`, `stores/search-history-store.ts`,
`lib/search-snippet.ts:46-161`, `components/email/{search-snippet-text,search-result-count,rules-menu,plugin-search-results}.tsx`,
`lib/mail-search-request.ts`, `lib/search-default-scope.ts`, `lib/search-scope-folders.ts`, `lib/plugin-search-results.ts`,
`lib/filters/{quick-rules,quick-rule-flow,quick-rule-target,retroactive}.ts` (`quick-rules.ts:208, :398`, `quick-rule-flow.ts:345`),
`stores/quick-rule-store.ts`, `lib/sieve/{parser,generator,period}.ts`, `lib/jmap/client.ts` `searchEmails` :4746, Sieve :7533-7708.
Stalwart `crates/jmap/src/email/{query,snippet}.rs`, `crates/jmap/src/sieve/{get,set,validate}.rs`.

**Owns:** §3.1 (from the merged wave-1 tree).

**Builds:**
- The search grammar (operators, unknown keys and phrases as text, `in:` scopes, chip removal rewriting the text, split like Bulwark,
  the mailbox filter excluding Trash and Junk unless chosen; T-102..T-114) as `IListQueryShaper` (the list) and as the global search
  model (mail now; contacts, calendar and files providers return nothing until M4/M5 but their pure filters are ported and tested:
  T-108, T-113).
- `SearchBoxView`: typing, Enter, empty Enter, clear X (I-272, I-273), suggestions (recent searches, contacts from senders for
  `from:`/`to:`, I-274; an in-window constrained list, never `AutoSuggestBox`), removable chips (I-275), filter toggle with count
  (I-276), filter panel (live search, date/size presets, read filter, not starred, no attachment, "Search in" folder, Clear all,
  I-277..I-280). History (`search-history.json` format kept, 10 newest first without duplicates, T-256..T-258).
- Snippets: `SearchSnippet/get` after each search page (J-056), marks escaped (T-110), loosened filter (T-111), `ISnippetSource` for
  the list rows; L-050: marks recolour with the theme without rebuilding the list (brushes, not baked colours).
- Global search: the palette scope (`IPaletteProvider`) and the rail search button command (I-023, I-455), ranking (T-112), opening a
  hit in its folder (T-102), `onProvideSearchResults` (plugins can add entries).
- **Sieve core** (`Core/Sieve`, port of `sieve.rs` with T-271..T-279): parse the Bulwark metadata block, keep hand-written blocks,
  generate Bulwark's shape; SieveScript get/set/validate (J-073 partial) with `onSuccessActivateScript`.
- **Quick rules** (T-207..T-211): subject from a message (sender, domain, List-Id), presets (move from sender/domain/list, always mark
  read, always tag, Block sender = move to Junk), apply = create, merge or "already covered", a hand-edited script is refused (no
  silent overwrite); the Rules commands MENU-READER and MENU-MAIL name (I-179), the folder menu's Rules ▸ (I-065 part); "Manage
  rules" shows "Filters arrive in M6"; the script is validated before it is saved.

**Provides:** `IListQueryShaper`, `ISnippetSource`, the search views, the quick-rule commands, `Core/Sieve`. **Consumes:** actions
(list VM through the shaper and the partial file), menus (palette providers, commands, dialogs), reader (MENU-READER rule items).

**Exit tests:** the 21 + 9 T- rows green; FakeJmap tests: an operator search sends the right `Email/query` filter and then
`SearchSnippet/get` for the page; chip removal re-queries; history persists; quick rule against FakeJmap's Sieve (create, merge,
covered, hand-edited refused, validate error shown); hook call site. **Feel scenarios** (port 8884): `search_type` (type five letters
at 80 ms: suggestions update within one quantum per key, no frame over 25 ms), `search_results` (Enter: the old list stays until the
results are ready, then one swap; 0 BLANK), `chip_remove`. **Hidden-automation scenario:** port 8884, sample data: `search "from:alice
has:attachment"`, `chips`, `suggestions`, `wshot`, remove a chip, `palette "/ invoice"` global scope, quick rule "Block sender" on a seed
message against FakeJmap, `popup-guard`, `focus-guard`, `close`. **Live (integrate stage):** search and snippets on `<readonly-account>`
(read-only); L-050 theme flip with highlights; L-016 Block sender on `<test-account>`: back up the current Sieve script text first,
apply the rule to a test sender address at `example.org`, check it, then restore the saved script byte for byte (the user's own script;
never a server setting).

### 6.5 `unified` (Opus, hard, wave 2): unified inbox, accounts, import/export, Email/parse, TNEF, native plugins
**Spec to close:** T-286..T-292, T-163..T-169, T-206; J-058, J-028; the unified I- rows of §2.3; the 3 `auth` hooks; B- rows of
`mail.md` (unified, import), `extras.md` SPAM and PLUG; L-038 (script); G-046, G-047; R2-06, R2-09, R2-10, R2-13 (Core), the
`BodyCacheSync` account-key fix.

**Read:** PORT-PLAN §3.4 (sync, push per account), §3.5 (secrets), §3.12, M6b (the two plugin systems); ADR 0008; fable-review-2
R2-06, R2-09, R2-10, R2-13; Rust `src/unified.rs` (all: `row_id` :38, `parse_row` :43, `real_id` :50, `sum_other_logins` :139,
`refresh_other_logins` :151, settings :190-196, `Source` :213, `shared_accounts` :231, `sources` :251, `dedupe` :270, `source_page` :298,
`merge_sorted` :342, `positions` :349, `fetch` :361, `unread_total` :400, `watch` :413, `match_mailbox` :456, `shared_apply` :474),
`src/mailimport.rs` (all), `src/plugins.rs` (all), `src/spam.rs` (all), `tests/fixtures/plugins/spam-score/src/*.rs`, `src/main.rs`
(sign-out paths). Bulwark `lib/unified-mailbox.ts` (:67-829), `lib/account-inbox-unread.ts`, `lib/account-state-manager.ts`,
`lib/shared-mail-order.ts`, `lib/shared-mail-visibility.ts`, `lib/compact-shared-mailbox.ts`, `components/layout/{sidebar,account-switcher}.tsx`
(unified :2832-2906, shared :2133-2566, sign-out :657-675), `lib/sign-out-cleanup.ts`, `lib/sole-login.ts`, `stores/account-store.ts`,
`lib/eml-import.ts:17`, `lib/mail-import/{mbox,engine,dedupe,checkpoints,mbox-status,sources}.ts`, `lib/mail-export/{engine,mbox-writer,zip-writer,crc32,sinks}.ts`,
`stores/mail-transfer-store.ts`, `components/mail-transfer/`, `lib/tnef.ts:247,405`, `lib/jmap/client.ts` `parseEmailBlob` :3564,
`importRawEmail` :12326, `lib/plugin-hooks.ts:62-66,:186-188,:290-325`, `lib/plugin-hook-result.ts`, `lib/plugin-loader.ts`,
`lib/plugin-runtime.ts`, `lib/plugin-types.ts`, `lib/plugin-mail-hooks.ts`, `lib/plugin-slot-catalog.ts`. Stalwart
`crates/jmap/src/email/{parse,import}.rs`, the session's `accounts` for shared accounts (`crates/jmap/src/api/session.rs`).

**Owns:** §3.1 (from the merged wave-1 tree).

**Builds:**
- **Account maintenance first** (R2-06 before sign-out): `AccountStore.ChangeAsync` reloads from the vault inside the refresh lock,
  the vault upserts and removes one entry and never deletes from a stale view, a rejected refresh tries the in-memory token once
  when it differs from the stored one; then `IAccountMaintenance`: sign out of one (stop push/poll/unread timer, clear its body cache
  and avatars cache entries, delete only Barbican's own credential entries, never the Rust app's; hooks onBeforeLogout, onAfterLogout,
  onAccountRemove; the next account becomes active, or the sign-in screen when none is left), sign out of all, set default, reorder.
  R2-09 (`localhost`, 127.0.0.0/8 and `::1` only; user info refused with a plain message). R2-13 Core points.
- **Unified inbox** (`enableUnifiedMailbox`, off by default; `unifiedCrossAccount`, `includeGroupInUnified`): sources from every
  signed-in account and the shared accounts of each session (T-291, `includeGroupInUnified`), deduped so each account is read once
  (T-289), per-source paging with positions (T-290), merge by parsed time with duplicates removed (T-287), row ids that keep sources
  apart (T-288) through `IMailTargetResolver`, per-role rows (Inbox, Sent, Archive…; match by role then name, T-292), the sidebar
  sections (unified and shared, I-050..I-054) in the slots, the other-logins badge sum (T-286, `unreadBadgeAllAccounts`),
  `IUnifiedUnread` for the account menu (I-017), push from every source (a watch per account) applied as deltas, actions on unified
  rows going to the right account (`shared_apply`). `IListSourceResolver` replaced; the M1 single-account path stays the default.
  The `BodyCacheSync` account key fix.
- **Import and export:** `.eml` (one message, bytes kept, 200 MB cap, T-167, T-169) and `.mbox` (streaming splitter on From lines with
  envelope recognition, T-164, T-165, T-169), states and dates to keywords and `receivedAt` (T-166), the import loop (upload once,
  `Email/import`, created/not created/duplicates by Message-ID, T-168) through actions' `IMailWriter`, summaries (T-163), progress and
  cancel, "You can't add messages to this folder" from myRights; commands for MENU-FOLDER "Import messages (.eml, .mbox)…", Ctrl+O
  (open .eml) and folder export to mbox/eml (G-046, Bulwark `lib/mail-export/`). File pickers through the platform service (queued
  `pick` answers in automation).
- **Email/parse** (J-058): `IAttachedMessageOpener` for a `message/rfc822` part or an `.eml` attachment (reader shows it with the
  blob actions off, as MENU-READER says for an opened .eml).
- **TNEF** (G-047, better than Rust): `IAttachmentExpander` for `winmail.dat`/`application/ms-tnef` with MimeKit's TNEF reader (Bulwark
  `lib/tnef.ts:247` as the behaviour reference): the contained files listed as attachments, downloaded from the decoded bytes, the RTF
  body offered as an attachment.
- **Native .NET plugin host** (`Core/Plugins`, `Barbican.Plugins.Abstractions`): plugins in `<config>/plugins/<id>/` and the bundled
  folder, each loaded in its own collectible `AssemblyLoadContext` sharing only the Abstractions assembly; a manifest (id, name,
  version, description, hooks; the same hook and slot names as the catalogue); enable/disable in prefs (`disabledPlugins` as Rust;
  the settings UI is M6/M6b); handlers subscribe to `IHookBus` by catalogue name; a throwing or slow plugin never takes the app
  down. **R2-10:** a per-hook budget and fail-closed flag in `hooks.csv` (new columns, `gen-hooks.py` extended), interceptors 60 s
  when they wait on a dialog and fail closed where Bulwark does (`lib/plugin-hooks.ts:186-188, :293-296`), every native handler
  started on the thread pool so a blocking handler is bounded by the budget. The JS host is M6b; nothing here may assume it.
- **Spam score as a native plugin** (D-234, T-206): port of `spam.rs` and the fixture plugin into `plugins/Barbican.Plugin.SpamScore`
  (X-Spam-Status, X-Spam-Score, X-Rspamd-Score, bracket scores; the verdict tones), shipped bundled and enabled (decision M2-Q3),
  feeding `IEmailDetailsSource` (the reader-below-subject rows).

**Provides:** `IAccountMaintenance`, `IUnifiedUnread`, `IListSourceResolver`, `IMailTargetResolver`, `IMailImport`, `IAttachedMessageOpener`,
`IAttachmentExpander`, `IEmailDetailsSource`, the plugin host. **Consumes:** actions (`IMailWriter`, commands, list VM through the
partial file), menus (account menu, commands, dialogs), reader (banners, saver), search (the shaper composes with unified sources:
search inside the unified inbox sends the filter to each source).

**Exit tests:** the 15 T- rows green; FakeJmap with two accounts and a shared account: unified page merges and dedupes, paging per
source, a push on one source inserts one row (no reset), an action on a unified row writes to its own account only (request log per
account); sign out of one account (its push stops: count requests after; its credential entry gone, the others kept; hooks fired)
and of all (sign-in screen); R2-06 two-instance test on a fake vault; R2-09 tests; import of an mbox fixture with a duplicate
(created 2, duplicate 1), cancel mid-way; Email/parse open; TNEF fixture expands; plugin host loads the spam-score plugin (T-206),
a throwing plugin and a sleeping plugin are contained by the budget, a fail-closed intercept stops the action; hook call site for
the 3 auth hooks. **Feel scenarios** (port 8885): `unified_open` (switch to the unified inbox: the old list stays until the merged
page is ready, one swap, 0 BLANK), `account_switch` (M1 regression after sign-out exists), `import_progress` (a 500-message mbox from
fixtures: the UI thread stays under budget, progress without layout jumps). **Hidden-automation scenario:** port 8885, FakeJmap with
two accounts: `unified on`, `wshot`, open a row from the second account, star it, check the request log; `import-sim` an mbox into a
test folder; `plugins` lists spam-score; open the spam seed message, `rx-banners` shows the detail row; sign out of the second account
through `menu-pick`; `popup-guard`, `focus-guard`, `close`. **Live (integrate stage):** L-038 on `<test-account>`: import a small mbox
fixture (two synthetic messages marked with a run token) into a test folder, export the folder to a temp file, compare, delete the
folder and the temp file; unified inbox with both accounts signed in (read-only on `<readonly-account>`) only if both are available
to the automation profile, else hand test.

## 7. Waves, merges, integration and live checks (§6.1 steps 3-5)
1. **F** → review (Sonnet; Opus reads the contracts of §4 and the list-seam change) → merge into `m2` → green on Windows, and on
   Linux if reachable.
2. **Wave 1:** `actions` (Sonnet), `menus` (Sonnet), `reader` (Opus) in parallel from `m2`. Reviews: Sonnet for menus; Opus for
   reader; actions gets a Sonnet review plus an Opus read of `Core/Mail/Write` (JMAP writes). Merge order `actions`, `menus`,
   `reader`, building and testing after each merge (`tools/build.ps1 test`, `tools/linux.ps1 test`, `spec-check --milestone M2`
   rows only go down, `tools/build.ps1 rules`, `red-check`, privacy check).
3. **Integrate wave 1 (one agent, Sonnet; Opus for conflicts in reader files):** after the three merges, run the cross-stream
   checks that need real code on both sides: the dialog and ctx_menu feel scenarios with actions' dialogs and reader's menus; the
   `AppCompositionTests`; the hidden smoke. Then the **live checks of wave 1** (§6.1-§6.3 "Live"), one agent, sequentially, through
   `tools/build.ps1 build -Configuration Automation -Head win -Writes` and port 8889, only if a password-kind import into
   `auto-8889` works and decision M2-Q2 is in force; every script ends with its cleanup and a `rx-find` that returns nothing.
   Otherwise each becomes a HAND TEST 2 item.
4. **Wave 2:** `search` (Sonnet) and `unified` (Opus) from the updated `m2`. Reviews: Sonnet for search (Opus reads the quick-rule
   save path, which writes the user's script), Opus for unified. Merge `search`, then `unified`; re-run actions' hook tests after
   unified (R2-10 changes the bus).
5. **Integrate wave 2:** the same checks; live checks of wave 2.
6. After each merge: prune the worktree; `tools/build.ps1 shutdown` at the end of every agent.

## 8. Verify and gate (§6.1 steps 6-7)
- `python tools/spec-check.py --milestone M2` exits 0; the 14 early rows show ported; `--strict` 0.
- Hook call-site tests green for every `milestone = M2` hook (36 after the re-tags).
- Mail I- rows (list, menus, reader, sidebar, palette) are `done` or `deferred(M<k>)` with a reason (PORT-PLAN M2 exit).
- Feel results in `spec/feel/M2/` (per stream) meet PORT-PLAN §4.5 with the ~21 ms quantum, **including `ctx_menu` and `dialog`**;
  scenarios whose captures were uniform are reported "unproven (capture invalid)", never as passes; a regression against the Rust
  baseline blocks the gate unless the user accepts it.
- trace-diff: open, switch and push unchanged from M1; new traces for `delete_undo` and `move` against Rust-derived sequences
  (`mail.rs` `apply_confirmed` :978).
- Cold start (`--smoke`, Release) still ≤ 1.5 s; the plugin host's start-up cost measured (bundled spam score loaded off the UI thread).
- Release publish has no automation content and no `AUTOMATION_WRITES`; the bundled plugin is in the publish folder.
- Rust leftovers on `<test-account>` removed (L-057 "MXTEST …" messages through the live script; the calendar event "p2cal test
  series" needs CalendarEvent/set, which is M4: HAND TEST 2 asks the user to delete it, or it waits for M4).
- Linux: `tools/linux.ps1 test` green on the merged `m2`, plus an `xrun` of the AUTOMATION build with menus, a dialog over the body
  (the X11 stand-in) and DnD simulation; if the VM is unreachable, "Linux pending".
- `docs/milestones/M2.md`: the §2.5 checklist, deferred rows, the Fable items, the decisions, and **[HAND TEST 2]**:
  - On `<test-account>`: drag mail to a folder and to a tag; delete and undo (z and the toast); mark junk and not junk; run a quick
    rule (Block sender on a test address, then remove it); one-click unsubscribe on a test newsletter sent to `<test-account>`;
    send a read receipt to yourself; import a small .mbox; sign out of `<test-account>` and sign in again; delete "p2cal test series".
  - On `<readonly-account>`, read-only: every context menu (open, look, Escape), the account menu, the palette, search with chips and
    suggestions, the `?` sheet, unified inbox with both accounts.
  - Real hover (hover strip, tooltips), real drag (folders reorder, pane splitters, attachment drag-out), the keyboard on a non-US
    layout (`/`, `?`), focus after a menu or dialog closes.
  - **First Narrator pass** (PORT-PLAN §4.7): list, menus, dialogs, palette, toasts announced.
  - Optional on-screen feel run for the frame gates (needs the user's OK, ADR 0007).
- After the user's OK (and after M1's): merge `m2` to `main`, tag `m2`, push.

## 9. Risks and decisions of this plan
**Decisions for the user (each with the default this plan follows):**
- **M2-Q1 Persistent SQLite cache** (M1 moved it to M2; M1.md §6 item 2, Fable D-5). Default: **move it to M3 wave 1 as its own Opus
  stream**. Reason: M2 already has two Opus streams (one per wave), and the cache must follow M2's optimistic updates, undo and
  multi-account rows, which are only stable after M2 merges. Cold start stays measured without a cache (well under budget in M1).
- **M2-Q2 R2-14, writes in agent profiles.** Default: the `.local` allow-list may clear the import's read-only flag for the logins
  it names, except any login on a second `.local` read-only list; both files gitignored; a missing read-only list means nothing is
  writable (§6.3 step 1). Without this, every M2 write check is a hand test.
- **M2-Q3 Spam score bundled and enabled** as a native .NET plugin (parity with Rust, where the built-in verdict showed unless the
  plugin replaced it); the C ABI is dropped, T-206 changed (D-234).
- **M2-Q4 Forward as attachment** builds its payload in M2 and opens in the composer in M3 (I-170 deferred).
- **M2-Q5 Hook re-tags** (§2.4): 14 hooks moved to M3, M5, M6 and M10.
- **M2-Q6 Category tabs, list columns, folded rail, open in window** deferred (M10, M10, M10, M8).
- **M2-Q7 One-click unsubscribe** posts only after the user's click, only over https, only with DKIM pass for the list's domain under
  the server's own Authentication-Results, never to a private address, never following redirects (Rust behaviour; Bulwark only opens
  the link). Avatars stay off by default.
- **M2-Q8 Early ports:** Sieve (9 M6 rows) and invitation display (5 M4 rows) are ported in M2 because quick rules and the banner need
  them.
- **M2-Q9 Live checks only in the integrate stage**, one agent at a time, never by the streams (avoids two agents writing to
  `<test-account>` at once).

**Risks:**
1. **Contract stubs by a Sonnet stage.** Mitigation: the Opus review of F reads §4; owners may add but not break members; conflicts at
   the wave merges go in favour of the owner.
2. **The list ViewModel is shared by three streams** (actions owns it; search and unified in wave 2 through partial files and seams).
   Risk: a seam F placed in the wrong spot forces wave 2 to touch actions' file. Mitigation: F's seams are reviewed against Rust
   `mail.rs` `load_list` :665, `load_more` :706 and `unified.rs` `fetch` :361; the integrator applies any extra seam.
3. **MimeKit under `IsAotCompatible`** may warn: F isolates it in `Barbican.Core.Mime` rather than suppressing (§3.2).
4. **`GuardedHttp`** is new security surface (SSRF, DNS rebinding, redirect). Opus owns it, Opus reviews it, and Fable gets it.
5. **Drag and drop and hover cannot be driven in-process** (ADR 0007 Layer C): their logic is tested through simulation commands; the
   gestures are HAND TEST 2. Uno DnD on Linux is unproven; if it fails, Linux keeps the menu paths (Move to, Tag) and DnD is recorded as
   a Linux gap for M9.
6. **Frame gates may stay unproven** (display off): reported, never passed silently; the on-screen run needs the user.
7. **Wave 1 has three compiling agents** (the maximum); the 2 build slots serialise builds, so expect queueing. No stream may bypass
   the slots.
8. **Live access** depends on a password-kind import and M2-Q2; if either is missing, every L- row of M2 becomes a hand test.
9. **M1 is not gated yet.** Hand-test fixes on `m1` are merged into `m2` by the integrator; a large M1 change (for example from the
   Linux baseline) may disturb a wave. F's Linux baseline surfaces this early.
