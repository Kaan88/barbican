# M2 search: live script (written, NOT run; the integrate stage runs it, one agent at a time)

Sequential, Automation build, port 8884, profile the integrator chose. Search and snippets run on `<readonly-account>` (read-only: Email/query, Email/get, SearchSnippet/get only);
the Sieve steps run on `<test-account>` only. Nothing here changes a Stalwart setting; the Sieve script is the user's own and is restored byte for byte.

## Read-only: search and snippets (<readonly-account>)
1. `search from:<a known sender> has:attachment`: expect rows, `chips` shows the two chips, `tools/jmap-trace` shows one `Email/query` with `from` and `hasAttachment` conditions and then one `SearchSnippet/get` for the page.
2. `chip-remove has`: re-queries (one `Email/query`), rows widen, no blank frame (`python tools/feel-search.py --port 8884`).
3. `search in:trash <word>` sends `inMailbox` = the Trash id; `global-search <word>` leaves Trash and Junk out (`inMailboxOtherThan`), `global-search is:anything <word>` does not.
4. L-050: with a search open and snippet marks in the rows (needs the row template to call `ISnippetSource`, see the Fable log), `theme dark` then `theme light`: the marked runs change colour with the theme and the list is not rebuilt (`Rows` identity unchanged, 0 BLANK in `burst`).

## Write: quick rule on <test-account> (L-016)
Precondition: the account's Sieve script is the Bulwark-managed one (or empty). If `rule block` answers "edited by hand", stop: that is the correct refusal, nothing was written.
1. Back up: `SieveScript/get` the active script, download its blob, save the bytes to `artifacts/live/search-sieve-backup.sieve` (gitignored) and print its SHA-256.
2. Pick a seed message whose sender is `quick-rule-test@example.org` (import one with `rx-import`, from the reader stream) and select it.
3. `rule block`: expect `Rule "Block quick-rule-test@example.org" created` (or "Added to rule ..." / "Already covered ...").
4. Check: `SieveScript/get` shows the rule; the downloaded text contains `# Rule: Block quick-rule-test@example.org`, a `fileinto` of the Junk folder (with `:mailboxid` when the server advertises it) and `stop;`; `SieveScript/validate` of it is clean; every other rule and hand-written block of the old script is still there.
5. Repeat step 3: expect `Already covered by "Block quick-rule-test@example.org"` and no `SieveScript/set` in the trace.
6. Restore: upload the backup bytes, `SieveScript/validate`, `SieveScript/set` update of the same script id with `onSuccessActivateScript`; download again and compare the SHA-256 with step 1 (must be equal). Destroy the seed message (`rx-destroy`).
7. Cleanup proof: the script text equals the backup, `rx-find` finds no seed message, `popup-guard` 0, `focus-guard` unchanged, `close`.
