# Rust-era documents

Copied from the Rust repository (`Kaan88/barbican-rust`, tag `rust-final`, commit `bba9613`) at the
M0 freeze (PORT-PLAN.md §2.2). They are the record of what the Rust app did and what was learned;
the catalogue in `spec/` turns them into checkable rows.

| File | What it is |
|---|---|
| GAPS.md | Gaps against Bulwark, with priorities P0–P3 (becomes `spec/gaps.csv`) |
| INTERACTIONS.md | 471 interaction rows (becomes `spec/interactions.csv`, re-baselined first) |
| JMAP-COMPLIANCE.md | JMAP RFC compliance table (becomes `spec/jmap.csv`) |
| PARITY-ROUND2.md | Parity round 2 report; "never driven live" items become `spec/live-checks.md` |
| PLUGINS.md | The plugin C ABI (kept unchanged in the C# app) |
| ROADMAP.md | The Rust app's roadmap |
| branches.md | Every branch of the Rust repo at the freeze, with its purpose |
| mailview-shell.rust-final.js | `SHELL_JS` from `src/mailview.rs` (lines 51-81), verbatim (PORT-PLAN §2.2). The C# shell, `src/Barbican.App/Assets/mail/shell.js`, is a port of it (ADR 0005); this copy is the reference for diffing. Added at the M0 review. |
| ci/ci.yml, ci/release.yml | The Rust app's GitHub workflows (never ran). Reference only: this repository runs no CI on push (docs/adr/0002). |

## What was changed when copying
The texts are verbatim except for privacy substitutions (this repository is public):
real account addresses and names became `<readonly-account>`, `<test-account>`, `<admin-account>`,
`<other-account>`; the mail server became `<server>`; the admin key file became `<admin-key-file>`;
local paths became `<bulwark>`, `<rust-repo>`, `<prototype>`, `<repo>`, `<downloads>`, `<home>`,
`%TEMP%`; the private design canvas link became `<design-canvas-url>`. The real values are in the
gitignored `.local/accounts.md`. The same substitutions were applied to `docs/PORT-PLAN.md`,
`design/` and the copied `tools/`. In the mockups, the sample user's name and address became
"Sam Rivers <sam@example.org>" and a domain in a sample task became `example.org`.
