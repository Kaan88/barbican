# Files kept from the dropped C# port

These files were copied on 2026-11-23 from the archived `Kaan88/barbican-csharp` repository, which is private. They come from branch `m6b/bulwark-ui`. `spec/plugins/` and `docs/milestones/M6b-plugin-slots.md` come from the newer `m6b/plugin-slots-2`. The folder layout matches the C# repository. Paths written inside the files, such as `src/Barbican.Core/...`, refer to C# code that is **not** copied here.

Read CSHARP-LESSONS.md at the repository root first; it says what to adopt from each file.

| Path | What it is |
|---|---|
| `spec/plugins/` | Bulwark 1.21.2 plugin catalogue: 218 hooks with time budgets and fail-closed flags, 58 slots, 186 host-API methods |
| `tools/extract-plugin-catalogue.py`, `tools/gen-hooks.py` | Rebuild the catalogue from Bulwark source; generate hook-name constants |
| `src/Barbican.App/Assets/plugins/`, `tools/tests/` | The parent-page and bridge scripts that ran unmodified Bulwark plugins in WebView2, with their Node tests |
| `tools/plugin-runtime-build/` | Reproducible build of Bulwark's sandbox runtime from the pinned commit, plus the origin patch |
| `docs/milestones/M6b-{package-authority,runtime-transport,api-inventory,contracts,account-routing,plugin-slots}.md` | Plugin security contracts and slot placement |
| `docs/milestones/M6b-feature-parity.md`, `bulwark-parity-gaps.md` | Parity backlog with Bulwark file:line references |
| `spec/settings.csv`, `jmap.csv`, `gaps.csv`, `live-checks.md`, `behaviours/`, `decisions.md` | Checklists, behaviour rules and the decision log |
| `docs/adr/` | Architecture decisions |
| `docs/FABLE-REVIEW.md`, `fable-review-2.md`, `fable-review-3.md` | Security review findings |
| `design/legacy-slint/theme.slint` | Our theme.slint, extended in C# with the Bulwark palette and tuned feedback/contrast tokens |
| `tools/privacy-check.sh` | Pre-push personal-data check, to adapt for this repository |
