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

## Review run (Opus 5.5, m2-search at the review commit, Automation build, port 8884, BARBICAN_SAMPLE=1, no input produced)
- `search from:alice has:attachment` -> `ok 0 rows chips=2`, `chips` -> `from=From: alice | has=Attachments: Yes`, `suggestions al` -> `recent: from:alice has:attachment`, `wshot` saved 1384x884, `chip-remove` re-queried, `global-search report` -> 1 row, `popup-guard` open 1 closed 0 (the palette's constrained Grid), `focus-guard` own-is-foreground=no, close ok.
- `rule block` on the first sample row: `Rule "Block dave@example.org" created`, then `Already covered by "Block dave@example.org"` (FakeJmap Sieve).
- `tools/feel-search.py` (frame probe `rendering`, five keystrokes at 80 ms, `suggestions` per key): `search_type` worst frame gap inside motion 9.7-10.4 ms in three warm runs, 36 ms in the first run after the app started (cold caches), 11 ms and 28 ms in two earlier runs while the Linux VM job was running; each key's command took 1.8-5.5 ms. Verdict: within 25 ms when warm, NOT proven under load. `search_results` and `chip_remove`: the probe saw 0-1 frames (the in-process result settles in ~2 ms), so the frame verdict is "unproven (capture invalid: no frames)". A WGC burst over search + chip removal (7 frames, `capture_valid=True`, `blank=0`, `flash=0`, `list_changes=5`) is too sparse to count as the 0-BLANK proof; the user's on-screen run has to confirm it.
