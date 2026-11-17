# M2 search: hidden-automation run (port 8884, BARBICAN_SAMPLE=1, Automation build, profile auto-8884)

Commands sent with `tools/auto-run.ps1 send` (no input is produced; the commands drive the model and services the views draw):

| Command | Reply |
|---|---|
| `search from:carol has:attachment` | `ok 1 rows (total 1) chips=2` |
| `chips` | `from=From: carol \| has=Attachments: Yes` |
| `suggestions ca` | `recent: from:carol has:attachment \| person: Carol <carol@example.org>` |
| `chip-remove from` | `ok ... chips=0` (the text is rewritten and the search runs again) |
| `wshot <abs path>` | `saved 1384x884`, non-uniform: [search-chips.png](search-chips.png) (field with clear X and filter badge 2, two chips, Clear all) |
| `rule block` (twice) | `ok Rule "Block carol@example.org" created`, then `ok Already covered by "Block carol@example.org"` (against FakeJmap's Sieve; the sample fake now advertises Sieve) |
| `global-search report` | `1 rows: Report with attachments · Carol · Inbox` (the palette's Search scope) |
| `popup-guard` | `open 1 closed 0: Grid constrained` with the palette open (menus' in-window overlay), `open 0 closed 0` after `key Escape` |
| `focus-guard` | `own-is-foreground=no` |
| close | `ok` |

Not run: the feel scenarios `search_type`, `search_results`, `chip_remove` (no scenario scripts exist for them yet), the filter panel and the suggestion dropdown as frames, snippet marks in rows (actions renders them),
the Linux build. The automation `text` command still answers "pattern not supported" for TextBoxes, so typing into `list.search` is not scripted; `search TEXT` drives the same model.
