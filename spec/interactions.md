# interactions.csv

The parity checklist (PORT-PLAN.md section 2.2). One row per interaction group from
`docs/legacy/INTERACTIONS.md` (471 rows, ids `I-001` to `I-471` in the order of that file's tables).

| column | meaning |
|---|---|
| id, area, element | from the inventory |
| bulwark_ref | Bulwark file:line (version 1.21.2), as in the inventory |
| rust_status_inventory | the status written on 2026-11-16 19:10 (D done, P partial, M missing, N n/a) |
| rust_status_main | the status re-checked against Rust `main` at `rust-final` (`bba9613`) |
| recheck | `changed` = status moved since the inventory; `confirmed` = re-read and unchanged; `carried` = not re-read, the inventory status and note are kept |
| rust_evidence | for `changed`/`confirmed`: Rust module, function or commit that proves it. For `carried`: the inventory note verbatim (its `app.slint` line numbers are stale; the file grew from about 4300 to 6059 lines) |
| csharp_status | `todo`, later `D`/`P`/`N` with evidence, or `n/a` for rows that are N |
| milestone | the PORT-PLAN milestone that closes the row (M1 to M10); `N/A` for N rows |
| csharp_evidence | the C# test or screenshot that proves it; filled by the milestone that closes the row |

Status meaning is "a code path exists", not "was clicked through"; rows that were never driven
live say so in `rust_evidence` and are mirrored in `live-checks.md`.

Re-baseline result: 84 rows changed (31 M to D, 41 M to P, 12 P to D), 9 confirmed, 378 carried.
Totals on main: D 127, P 148, M 184, N 12.
