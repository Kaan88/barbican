# spec/: the behaviour catalogue

The only way the Rust app's knowledge enters the C# code (PORT-PLAN.md §2.3). Built in M0 by the
streams m0-cat-a, m0-cat-b and m0-cat-c, then completed and sample-reviewed by m0-review
(`docs/m0-checklist.md` has the review evidence).

## Files
| File | Ids | Rows (M0) | What it holds |
|---|---|---|---|
| `rust-tests.csv` | T-001..T-353 | 353 | every Rust `#[test]` at `rust-final`: module, fn, file:line, one-line assertion, milestone, C# test, status, the `[RustTest]` tag |
| `behaviours/<area>.md` | B-<AREA>-### | 729 | one rule per row with its source (file:line, test or commit) and the Bulwark/RFC ref |
| `decisions.md` | D-### | 232 | every non-merge commit at `rust-final`, the parity-round merges and the three `mat-*` WIP branches |
| `interactions.csv` (+ `interactions.md`) | I-001..I-471 | 471 | the parity checklist, re-baselined against Rust `main` |
| `jmap.csv` | J-001..J-077 | 77 | JMAP-COMPLIANCE.md, one row per requirement |
| `gaps.csv` | G-001..G-110 | 110 | GAPS.md (P0-P3) plus round-2 items kept for traceability |
| `settings.csv` | S-<key>, S-keyless-### | 200 | 136 schema keys (wired or unwired in Rust) and 64 keyless rows |
| `menus.md` | MENU-<name> | 24 | every context menu item by item, with enable rules and confirmations |
| `behaviours/shortcuts.md`, `behaviours/palette.md` | B-KEY, B-PAL | 28, 21 | shortcuts and the command palette (under behaviours/; PORT-PLAN lists `shortcuts.md` at the root) |
| `plugins/hooks.csv`, `plugins/slots.csv` | | 218, 58 | the plugin hook and slot catalogues from Bulwark's `lib/plugin-hook-catalog.ts` and `lib/plugin-slot-catalog.ts`, with milestone, owner and call site (`plugins/README.md`) |
| `live-checks.md` | L-001..L-058 | 58 | what was never verified live in Rust |
| `automation-protocol.md` | | | the hidden-automation command set (ADR 0007) |
| `layout/<screen>.md` | | | tuned Slint metrics per screen, Bento and Nordic |
| `feel/rust-baseline/` | | | the Rust flicker and motion probe results: the bar to beat (§4.5) |
| `feel/M<n>/` | | | C# feel results per milestone (from M1) |

Behaviour areas: ADM admin-dashboard, AI assistant, CAL calendar, CAP capture and probes, COMP compose,
CON contacts, DND drag and drop, DSK desktop-tray, FILE files, INV invitations (extras.md), JMAP jmap,
KEY shortcuts, LST listsync, MAIL mail, MOT motion, OAUTH oauth, PAL palette, PLAT platform, PLUG plugins,
READ reader, RTE rte-editor, SET settings, SIEVE sieve-filters, SPAM spam, SRCH search box, SYNC sync,
UPD updater, ZIP attachment zip. CAP, INV, PLUG, SPAM, SRCH and ZIP live in `behaviours/extras.md`.

Known overlaps (the same rule under two ids; reconcile when the owning milestone ports it, and keep both
ids resolvable): B-SYNC-001..011 (sync.md, listsync part) with B-LST (listsync.md); B-COMP-039..069
(compose.md, rte part) with B-RTE (rte-editor.md).

## Statuses
- T- `status`: `pending` (no C# test yet), `ported` (a C# test carries the tag), `dropped(D-###, user OK)`.
  T-159 (lite) is `dropped(D-233, user OK pending)` until the user confirms at the M1 gate (PORT-PLAN Appendix A, B-READ-046).
- B- rows: `ported`, `deferred(M<k>)` or `dropped(D-###, user OK)` once their milestone closes (§2.5).
- I- `csharp_status`: `todo` or `n/a`, then `done` with evidence (a test, an automation script, a screenshot path).
- J- `cs_status`: `pending` until re-verified with a test and Bulwark plus Stalwart refs.

## How to update
- Port a Rust test: put `[RustTest("module::name")]` on the C# test, with the value exactly as the
  `rust_test_tag` column has it (no `tests::` segment). Then `python tools/spec-check.py --write` fills
  `csharp_test` and `status` in `rust-tests.csv`. Commit that with the code.
- Milestone gate (§2.5, first line): `python tools/spec-check.py --milestone M<n>` exits 0 only when every
  T- row of M<n> is ported, or dropped with a D- entry. `--strict` also fails on duplicate ids and
  dangling references across the catalogue.
- An assertion may change only with a D- entry saying why (§2.4). New decisions take the next D- number.
- Update `spec/` in the same commit as the code (§6.4).

## Pinned references
- Rust app: `Kaan88/barbican-rust` (private) at tag `rust-final` (`bba9613`), locally `<rust-repo>`.
  Line numbers in the catalogue are at that tag.
- Bulwark 1.21.2: `<bulwark>` (see `.local/accounts.md`). Bulwark refs keep their `file:line` form.
- Stalwart 0.16.27: `stalwart-0.16.27.zip` in `<downloads>` (JMAP-COMPLIANCE.md cites `stalwart-main.zip`,
  which is no longer on disk).

## Privacy
This repository is public. Catalogue rows cite accounts by placeholder (`<readonly-account>`,
`<test-account>`), never by address, and never quote real mail content. Fixture addresses use
`example.org`, `example.com` or `example.net` only.
