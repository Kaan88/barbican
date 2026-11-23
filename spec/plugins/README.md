# spec/plugins/: the plugin hook, slot and host-API catalogues

The one catalogue behind both plugin systems of Barbican (ROADMAP Phase 5, track B): the Bulwark-compatible JS host and the
native C-ABI plugins use the same hook and slot names. It comes from Bulwark 1.21.2. The committed CSVs are the source of
truth; `src/plugin_catalog.rs` is generated from them.

| File | Rows | Source |
|---|---|---|
| `hooks.csv` | 218 hooks in 27 groups | `<bulwark>/lib/plugin-hook-catalog.ts` |
| `slots.csv` | 58 slots | `<bulwark>/lib/plugin-slot-catalog.ts` |
| `host-api.csv` | 186 `api.*` host methods | `<bulwark>/lib/plugin-sandbox/host-api.ts` |

## hooks.csv columns
`name, group, kind, permission, also_permission, payload, when, bulwark_ref, phase, rust_call_site, status, budget_ms, fail_closed`

- `kind`: observer, intercept, transform or takeover (Bulwark's meaning).
- `permission`, `also_permission`: the plugin permission a handler needs (the reminder hooks take either of two).
- `bulwark_ref`: `lib/plugin-hook-catalog.ts:<line>` (Bulwark 1.21.2).
- `phase`: the ROADMAP phase and item that fires the hook: `5-B5` (Phase 5, track B, item B5 Hooks), or `n/a` for Bulwark's phone layout.
- `rust_call_site`: `path:line` of the place in `src/` that fires the hook; filled in the same commit that fires it.
- `status`: `pending`, `done`, or `n/a(reason)`.
- `budget_ms`: the time a handler gets (Bulwark `lib/plugin-hooks.ts:62-66`: 5 s, intercepts 60 s, takeovers 10 min).
- `fail_closed`: true when a handler that fails or times out stops the action (the send takeover).

## slots.csv columns
`name, area, kind, permissions, props, where, bulwark_ref, phase, status`. `kind` is frame, action or data; `phase` is `5-B6`;
`status` is `pending` until a host draws the slot, then `done`.

## host-api.csv columns
`method, base_permission, privileged_only, permission_reference, dispatch_reference, additional_gate_or_constraint, rust_candidate_and_gap`.
`rust_candidate_and_gap` is empty until the Rust module that backs the method is chosen. The file is maintained by hand
(the extractor does not produce it); the references are paths in a Bulwark checkout.

## Tools and tests
- `python tools/extract-plugin-catalogue.py <bulwark-root>` refreshes `hooks.csv` and `slots.csv` from a Bulwark checkout (keeps the edited columns).
- `python tools/gen-plugin-catalog.py` writes `src/plugin_catalog.rs` (hook, slot and host-API tables, `lookup_hook`, and the alias table
  from our old native hook names to Bulwark's); `--check` fails if it is stale.
- `src/plugin_catalog_check.rs` (`cargo test plugin_catalog`) compares the generated tables with the CSVs and pins the counts (218 / 58 / 186).

## Aliases
Native plugins written against API 1 use `email.details` and `compose.check`. They map to `onEmailOpen` and `onBeforeEmailSend`
(`HOOK_ALIASES` in `src/plugin_catalog.rs`, defined in `tools/gen-plugin-catalog.py`).

## Deliberate changes
- Moved from `docs/from-csharp/spec/plugins/`. `milestone`, `owner` and `cs_call_site` (C# port) became `phase` and `rust_call_site`;
  every hook and slot restarted at `pending` because the C# call sites do not carry over.
