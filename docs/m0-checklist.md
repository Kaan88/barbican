# M0 "nothing lost" checklist, with evidence

Written by the M0 review (stream m0-review) at the merge of all M0 branches into `main`
(integration branch `m0/integrate`). M0 closes the knowledge-preservation part of PORT-PLAN §2.5 for
every artefact (§5, M0 "Closes"). The porting lines of §2.5 (C# tests, C# status, feel, screenshots)
start with M1; for M0 they are answered as "catalogued, nothing ported yet" with the catalogue as evidence.

Rust reference: `Kaan88/barbican-rust` (private), tag `rust-final` = `bba9613`, working tree clean.

## 1. §2.5 checklist, applied to M0

- [x] **Rust tests.** All 353 `#[test]` functions at `rust-final` are rows T-001..T-353 in
  `spec/rust-tests.csv`. Checked by script: every row's `rust_file:rust_line` holds `fn <rust_test>`
  (353/353), and the set of (file, fn) pairs equals the set of tests in `src/` (0 missing, 0 extra,
  0 duplicates). Milestones match Appendix B (M1 76, M2 89, M3 66, M4 45, M5 14, M6 18, M7 7, M8 35,
  M9 3). `python tools/spec-check.py` (new) lists them; 7 are already ported (`rte::model`, spike S3),
  346 pending, 1 awaiting the lite-mode decision.
- [x] **Behaviours.** 729 B- rows in 23 files (cat-a 257, cat-b 412, M0 review 60). Every Rust module now
  has rows. Before the review these had none: `oauth.rs`, `dnd.rs`, `ai.rs`, `agent.rs`, `spam.rs`,
  `plugins.rs`, `zip.rs`, `capture.rs` and `pim.rs`; and `ctxmenu.rs` had one. The review added
  `behaviours/oauth.md`, `assistant.md`, `dnd.md`, `extras.md` and `spec/menus.md`.
  The count is below the plan's 1,000-1,500 estimate. The rows are rule-level, not line-level, and the
  T- rows hold the test-level detail.
- [x] **Interactions.** I-001..I-471 in `spec/interactions.csv`, re-baselined against Rust `main`
  (84 changed, 9 confirmed, 378 carried). C# status is `todo` or `n/a`; there is no C# evidence yet
  (the milestone streams fill it).
- [x] **JMAP.** J-001..J-077 in `spec/jmap.csv`. The review found JMAP-COMPLIANCE.md stale for
  features added after it. J-053 (truncated notice), J-056 (SearchSnippet), J-057 (Email/import),
  J-058 (Email/parse) and J-068 (receipts) were "not done" there but are implemented at `rust-final`.
  They are corrected, with code cites. The other rows keep the doc's status, and M1 re-verifies every
  J- row it touches (§2.5).
- [x] **Settings.** `spec/settings.csv` has 200 rows: 136 schema keys (110 wired, 26 unwired; "wired"
  is a literal match, an upper bound) and 64 keyless rows, so keyless rows are not dropped. The schema
  is embedded verbatim in Core (hash test). The plan's "201 keys" is wrong: the schema has 136 distinct
  keys in 275 rows and 21 tabs.
- [x] **Menus and shortcuts.** `spec/menus.md` (24 menus, item by item, with enable rules and
  confirmation labels; from `ctxmenu.rs` and `reader.rs`), `spec/behaviours/shortcuts.md` (28 rows) and
  `palette.md` (21 rows).
- [x] **Feel.** `spec/feel/rust-baseline/flicker` (6 files) and `motion` (5 files) were copied verbatim.
  They are the bar to beat. There are no C# feel runs in M0. The S4 probe output was checked against
  `motion.py`/`flicker.py` on the spike.
- [x] **Design.** `design/mockups/` copied (sample identity scrubbed); `design/legacy-slint/theme.slint`
  is the token source; `Tokens.xaml` / `Tokens.g.cs` are generated and tested. **Carried to M1:**
  rewriting `design/README.md` and `design/MOTION.md` in XAML terms (rules kept word for word).
- [x] **Live checks.** L-001..L-058 in `spec/live-checks.md`, each with an account placeholder and a
  milestone. None is run in M0 (no app yet).
- [x] **Docs.** `docs/ROADMAP.md` updated (M0 done, M1 next). There is no GAPS.md in the new repo; gaps
  live in `spec/gaps.csv` (G-001..G-110). Every M0 branch is pushed and merged; see section 4.
- [x] **Reference intact.** `rust-final` exists on origin and points at `bba9613`; the Rust working
  tree is clean; the 3 `mat-*` WIP branches are on origin; 44 local branches match origin.

## 2. §2.2 artefacts, one by one

| Artefact | Where now | Evidence |
|---|---|---|
| JMAP-COMPLIANCE.md | `docs/legacy/` + `spec/jmap.csv` | 77 rows; 5 corrected at review |
| INTERACTIONS.md | `docs/legacy/` + `spec/interactions.csv` | 471 rows, re-baselined |
| GAPS.md | `docs/legacy/` + `spec/gaps.csv` | 110 rows (93 open, 17 done kept) |
| PARITY-ROUND2.md | `docs/legacy/` + `spec/live-checks.md` | 58 L- rows; hand-check items mapped |
| ROADMAP.md, PLUGINS.md | `docs/legacy/` | new `docs/ROADMAP.md` written |
| design/README.md, MOTION.md | `design/` | copied; XAML rewrite carried to M1 |
| mockups, canvas | `design/mockups/` | copied; canvas link is `<design-canvas-url>` (private) |
| ui/theme.slint tokens | `src/Barbican.App/Themes/Tokens.xaml`, `Presentation/Theme/Tokens.g.cs` | `tools/Barbican.GenTokens`, TokenTests |
| ui/settings-schema.json | embedded in `Barbican.Core` | schema hash test |
| ui/*.slint layouts | `spec/layout/*.md` | 10 screens, Bento and Nordic, file:line |
| email_dark.js, mailview.rs SHELL_JS | `src/Barbican.App/Assets/mail/` | `email_dark.js` verbatim; `shell.js` is the port (ADR 0005); `SHELL_JS` verbatim in `docs/legacy/mailview-shell.rust-final.js` (added at review) |
| module headers, doc comments, tests | `spec/behaviours/`, `spec/rust-tests.csv` | sections 1 and 3 |
| commit messages | `spec/decisions.md` | all 155 non-merge commits of `rust-final` cited (script); 3 `mat-*` commits cited; 10 plain integration merges not cited (their content is in the merged commits) |
| tools/*.py | `tools/` | copied; `drive.ps1`, `scroll.ps1` (real input) and `admin_probe.sh` (reads the admin key) left out on purpose |
| flicker/motion results | `spec/feel/rust-baseline/` | verbatim |
| workflows, wix, packaging | `packaging/`, `docs/legacy/ci/` | reference only; no CI on push |
| release_manifest | `tools/Barbican.ReleaseTool` | **not yet**: M8 (B-UPD rows and T- rows keep the format) |
| plugins/spam-score | `tests/fixtures/plugins/spam-score` | source only; the DLL was left out (it embeds a home path). Rebuild it from source for the M2 ABI test. |
| memory notes | `CLAUDE.md`, `AGENTS.md` | standing rules, placeholders |
| Bulwark / Stalwart pins | `spec/README.md` | Bulwark 1.21.2, Stalwart 0.16.27 |

## 3. Sample review of the catalogue (Opus, §2.3 step 5)

Mechanical checks (scripts in the review's scratchpad, results above): all T- rows; all D- commit
coverage; plausibility of all 375 B- rows that cite `file:line` (357 matched the cited lines; the 18
flagged were all read by hand: line ranges and test lines, all correct).

Rows read by hand against `rust-final` (52 in all, across every area, weighted to the hard areas):
- **JMAP, sync, push, msgload, body cache:** B-JMAP-005 (sessionState re-fetch, `jmap.rs:263`),
  B-JMAP-046 (75 s = 2.5 × 30 s ping), B-SYNC-004, B-SYNC-005, B-SYNC-013 (120 entries / 48 MiB),
  B-SYNC-017 (120 threads / 8 MiB), B-SYNC-025 (150 ms bar), B-LST-004. All correct.
- **Editor:** B-COMP-040, B-COMP-042, B-RTE-003. All correct.
- **Recurrence and calendar:** B-CAL-004, B-CAL-005, B-CAL-061, I-319 ("+N more" is not clickable:
  confirmed), I-323, I-325, I-342. Correct.
- **Updater:** B-UPD-007 (`detect_kind`), B-UPD-008 (`decide`, Older on the stable channel). Correct.
- **OAuth:** none existed; the whole module (465 lines) was read and catalogued (B-OAUTH-001..018).
- **Reader, mail, compose:** B-READ-015, B-READ-018, B-READ-021, B-MAIL-020, B-MAIL-024, B-COMP-020,
  B-DSK-021 (commit `110ff28` body). All correct.
- **Other areas:** B-ADM-011, B-CON-013, B-FILE-002, B-MOT-019, B-PAL-002, B-PLAT-017, B-SET-006,
  B-KEY-014, B-KEY-022, B-SIEVE-014, I-365, I-375, I-106, I-109. All correct.
- **JMAP table:** J-013, J-024, J-061 (correct); J-053, J-056, J-057, J-058, J-068 (wrong, corrected).
- **Settings:** S-signatureSeparatorEnabled (`compose.rs:336`), S-toastDuration (`a11y.rs:15`),
  S-keepAppsLoaded (unwired: no code reference). Correct.
- **Missing and added:** the modules listed in section 1, the context-menu file, the verbatim `SHELL_JS`.

Findings left open (in `docs/FABLE-REVIEW.md`): duplicate rules across B-SYNC/B-LST and B-COMP/B-RTE;
the 378 carried I- rows are sampled (8), not re-read; J- rows other than the 8 above keep the doc's
status; behaviour rows are extracted from headers, comments, tests and commits rather than read line by
line (the T- rows cover the tested logic).

## 4. Merge record

Integration branch `m0/integrate` from `main` at `7f0a3ea`, merged in this order: m0-cat-a (`4a68b82`),
m0-cat-b (`72421a9`), m0-cat-c (`66c9ba6`), m0-s12 (`943232b`), m0-s3 (`3196fb6`), m0-s45 (`e9f8cc9`).
Conflicts:
- `docs/FABLE-REVIEW.md` in every merge: union of the sections, consolidated afterwards.
- `Directory.Packages.props`: S1 logging pins and S3 editor pins were both kept. The editor pins are
  withheld from Uno.Sdk projects (ADR 0004 "M0 review").
- `spikes/Directory.Build.props`: S1/S2's version was kept. It is a superset that imports the root props,
  as S3's does.
- ADR numbers: S4/S5's 0004/0005 were renamed 0007/0008.

Green after the merge: `tools/build.ps1 test` 62/62 on Windows; `tools/linux.ps1 test` 62/62 on the VM.
Worktrees removed after the merge: `wt-port-cat-a`, `wt-port-cat-b`, `wt-port-cat-c`, `wt-port-s12`,
`wt-port-s3`, `wt-port-s45`, `wt-m0-integrate`. The merged `m0-*` branches stay on origin.

## 5. M0 exit criteria (PORT-PLAN §5)

| Criterion | Status |
|---|---|
| CI green on both OSes (here: local Windows + Linux VM, ADR 0002) | met: 62/62 on both at the merge |
| Spec catalogue complete and reviewed (Opus sample) | met, with the open items in section 3 |
| S1-S5 decided, with fallbacks where needed | met: `docs/adr/README.md`; one cross-spike task moves to M1 (Uno 7) |
| `rust-final` tagged; all branches and the prototype pushed | met |
| Carried to M1 | design docs in XAML terms; the Uno.Sdk 7 move; the hidden smoke mode for the published exe; archiving the Rust repo (end of M1); the Rust pre-commit guard (blocked by the permission classifier: needs the user) |
