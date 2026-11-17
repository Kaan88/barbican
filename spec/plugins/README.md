# spec/plugins/: the plugin hook and slot catalogues

The one catalogue behind both plugin systems (PORT-PLAN.md M6b): the Bulwark-compatible JS host and the native .NET
plugins use the same hook and slot names. Created in M1 (stage F2) from Bulwark 1.21.2; M1 fires the hooks of the areas it
builds through a host-agnostic bus (`IHookBus`), no plugin host yet.

| File | Rows | Source |
|---|---|---|
| `hooks.csv` | 218 hooks in 27 groups (PORT-PLAN said 219 in 29; the file has 218 and 27) | `<bulwark>/lib/plugin-hook-catalog.ts` |
| `slots.csv` | 58 slots | `<bulwark>/lib/plugin-slot-catalog.ts` |

## hooks.csv columns
`name, group, kind, permission, also_permission, payload, when, bulwark_ref, milestone, owner, cs_call_site, status`

- `kind`: observer, intercept, transform or takeover (Bulwark's meaning; `IHookBus.NotifyAsync / InterceptAsync / TransformAsync`).
- `permission`, `also_permission`: the plugin permission a handler needs (the reminder hooks take either of two).
- `bulwark_ref`: `lib/plugin-hook-catalog.ts:<line>` (Bulwark 1.21.2).
- `milestone`: the milestone whose surface fires the hook (`M1`..`M10`, `M6b`, or `n/a(...)` for Bulwark's phone layout).
  Defaults by group and the per-hook overrides are in `tools/plugin_milestones.py`; edit the CSV for a deliberate change
  (the extractor keeps `milestone`, `owner`, `cs_call_site` and `status` of existing rows).
- `owner`: for M1 hooks the stream that fires it (M1-plan §2.4): jmap, accounts, mailcore, shell-ui, body.
- `cs_call_site`: `path:line` of the `HookNames.<Name>` reference that fires it; filled by the owner in the same commit.
- `status`: `pending`, `done`, or `n/a(reason)`.

## slots.csv columns
`name, area, kind, permissions, props, where, bulwark_ref, milestone, status`. Slots are catalogued only in M1 (no slot is rendered);
`milestone` is M6b (the hosts) and `status` is `catalogued`.

## Tools and tests
- `python tools/extract-plugin-catalogue.py <bulwark-root>` regenerates both CSVs (keeps the edited columns).
- `python tools/gen-hooks.py` writes `src/Barbican.Core/Plugins/Hooks/HookNames.g.cs` (one constant per hook); `--check` fails if stale.
- `HookNamesTests` (green) pins the constants to the CSV and the counts; `HookCallSiteTests` (one red test per owner until its stream
  fires its hooks) asserts every `M1` hook has a call site under `src/`, as Bulwark's `lib/__tests__/plugin-hook-catalog.test.ts` does.
- `python tools/spec-check.py` prints hooks per milestone and how many are fired.
