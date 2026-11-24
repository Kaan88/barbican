# spec/plugins/: the plugin hook, slot and host-API catalogues

**B1 decisions (`barbican_status`, 2026-11-24).** Plugin API v2 (PLUGINS.md) takes each row as Bulwark has it
(adopt), changed (adapt, what changes is in `barbican_note`), or not at all (skip). Pinned by `cargo test plugin_catalog`.

| File | adopt | adapt | skip | Total |
|---|---|---|---|---|
| `hooks.csv` | 194 | 20 | 4 | 218 |
| `slots.csv` | 19 | 37 | 2 | 58 |
| `host-api.csv` | 144 | 28 | 14 | 186 |
| **All** | **357** | **85** | **20** | **462** |

- Hooks skipped: Bulwark's phone layout (`onViewChange`, `onDeviceTypeChange`, `onMobileBack`) and theme CSS
  (`onThemeBeforeApply`). Adapted: payloads that carried DOM, React state or browser concepts (row patches instead of
  mutated arrays, ids instead of DOM, Barbican pref names, window instead of tab, loopback OAuth).
- Slots: every frame slot except two becomes a declarative view (adapt); action and data slots are adopted. Skipped:
  `reader-above-quote` (it would sit inside the mail webview) and `composer-attachment-source` (superseded in Bulwark).
- Host API skipped: `http.post` and `proxy.*` (need Bulwark's server), `keystore.*`, WebAuthn and print themes.
  Adapted: UI calls that took components or callbacks (`ui.setPanel`, `ui.openDialog`, the composer checker), the
  privileged-tier raw calls, settings keys, OAuth redirect, plugin files.

The reference vocabulary for Barbican's native (C ABI) plugin system (ROADMAP Phase 5, track B). Hook, slot and host-call
names are borrowed from Bulwark 1.21.2 so that porting a Bulwark plugin means rewriting its logic, not inventing its API.
This is a reference vocabulary, not compatibility: Bulwark plugins do not run in Barbican. The committed CSVs are the source
of truth; `src/plugin_catalog.rs` is generated from them.

| File | Rows | Source |
|---|---|---|
| `hooks.csv` | 218 hooks in 27 groups | `<bulwark>/lib/plugin-hook-catalog.ts` |
| `slots.csv` | 58 slots | `<bulwark>/lib/plugin-slot-catalog.ts` |
| `host-api.csv` | 186 `api.*` host methods | `<bulwark>/lib/plugin-sandbox/host-api.ts` |

## hooks.csv columns
`name, group, kind, permission, also_permission, payload, when, bulwark_ref, phase, rust_call_site, status, budget_ms, fail_closed, barbican_status, barbican_note`

- `kind`: observer, intercept, transform or takeover (Bulwark's meaning).
- `permission`, `also_permission`: the plugin permission a handler needs (the reminder hooks take either of two).
- `bulwark_ref`: `lib/plugin-hook-catalog.ts:<line>` (Bulwark 1.21.2).
- `phase`: the ROADMAP phase and item that fires the hook: `5-B5` (Phase 5, track B, item B5 Hooks), or `n/a` for Bulwark's phone layout.
- `rust_call_site`: `path:line` of the place in `src/` that fires the hook; filled in the same commit that fires it.
- `status`: `pending`, `done`, or `n/a(reason)`.
- `budget_ms`: the time a handler gets (Bulwark `lib/plugin-hooks.ts:62-66`: 5 s, intercepts 60 s, takeovers 10 min).
- `fail_closed`: true when a handler that fails or times out stops the action (the send takeover).
- `barbican_status`: `adopt`, `adapt` or `skip` (B1, the same in all three files); `barbican_note` says why, and for adapt what changes.
  A skipped row has `status` `n/a(...)`. A manifest may not name a skipped hook, slot or method.

## slots.csv columns
`name, area, kind, permissions, props, where, bulwark_ref, phase, status, barbican_status, barbican_note`. `kind` is frame, action or data; `phase` is `5-B6`;
`status` is `pending` until a host draws the slot, then `done`.

## host-api.csv columns
`method, base_permission, privileged_only, permission_reference, dispatch_reference, additional_gate_or_constraint, rust_candidate_and_gap, barbican_status, barbican_note`.
`rust_candidate_and_gap` is empty until the Rust module that backs the method is chosen. The file is maintained by hand
(the extractor does not produce it); the references are paths in a Bulwark checkout.

## Tools and tests
- `python tools/extract-plugin-catalogue.py <bulwark-root>` refreshes `hooks.csv` and `slots.csv` from a Bulwark checkout (keeps the edited columns, including `barbican_status`; a new row has none, and the generator refuses it until it is decided).
- `python tools/gen-plugin-catalog.py` writes `src/plugin_catalog.rs` (hook, slot and host-API tables with their `Support`, `lookup_hook`, `lookup_host_method`, and the alias table
  from our old native hook names to Bulwark's); `--check` fails if it is stale.
- `src/plugin_catalog_check.rs` (`cargo test plugin_catalog`) compares the generated tables with the CSVs and pins the counts (218 / 58 / 186) and the adopt/adapt/skip counts above.

## Aliases
Native plugins written against API 1 use `email.details` and `compose.check`. They map to `onEmailOpen` and `onBeforeEmailSend`
(`HOOK_ALIASES` in `src/plugin_catalog.rs`, defined in `tools/gen-plugin-catalog.py`).

## Deliberate changes
- Moved from `docs/from-csharp/spec/plugins/`. `milestone`, `owner` and `cs_call_site` (C# port) became `phase` and `rust_call_site`;
  every hook and slot restarted at `pending` because the C# call sites do not carry over.
