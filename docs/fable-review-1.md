# Fable review 1 (M0 + the M1 parts on `m1` at `32ced0b`)

Reviewer: Fable, 2026-11-17, branch `fable/review-1`. Scope: every item of
[FABLE-REVIEW.md](FABLE-REVIEW.md) against the code on `origin/m1` (M0, F1, F2, mailcore, jmap), plus
security, privacy, JMAP correctness and "lost from Rust" beyond the list.

How it was done: reading only. Nothing was built, run or launched, the mail server was not
contacted, `<admin-key-file>` was not read. The only script run is `python tools/spec-check.py`
(reads CSVs). The full git history (72 commits, all remote branches) was scanned for personal data,
and the GitHub activity log was read for force pushes (none).

Not on `m1` yet, so not reviewable as code: the body host (WebView), accounts (sign-in, OAuth,
secret store, import), shell-ui views. For those, the findings below are **requirements for the
streams that are building them**, derived from the contracts, the plan, the ADRs and the Rust source.

Verdict words: **confirmed** (a real problem, with a fix), **fine** (fine as is), **decision** (the
user must decide), **not reviewed** (outside M1's path; said openly rather than guessed).

## 1. Confirmed problems, most serious first

<a id="f-01"></a>
### F-01 (high, privacy): the privacy check publishes the strings it guards
- **Evidence:** `tools/privacy-check.sh:19`. The `hard=` pattern is the list of the user's private
  tokens in clear: the stems of both account domains, the local part and the provider of a third
  address (enough to rebuild that address exactly), the local Windows user name, the admin key file
  name and a personal name. The file's own header (line 8) calls this intended. It is in every one of
  the 72 public commits. It is the only hit of the history scan.
- **Why it matters:** CLAUDE.md "Privacy" and ADR 0002 decision 3 forbid exactly these values in the
  repository; anyone can read them from the guard itself.
- **Fix:** move the patterns to `.local/privacy-patterns.txt` (already gitignored by `.local/*`), one
  extended regex per line; the script reads that file and **fails closed** when it is missing (a
  clone without it must not push). Keep only generic patterns in the tracked script (home paths,
  LAN addresses, private artifact links, the e-mail allowlist). Add the three gaps FABLE item 5
  names (first name alone, LAN addresses, artifact links) to the local file. Then decide D-1.

<a id="f-02"></a>
### F-02 (high, security and correctness): session URLs are trusted verbatim, so credentials can leave the server the user typed
- **Evidence:** `Core/Jmap/Transport/SessionParser.cs:77-99` (`Absolute`) returns any absolute
  `http://` or `https://` URL unchanged; `JmapClient.cs:286, :97, :138, :210` then send the
  Authorization header (for a password login: the Basic password) to whatever origin `apiUrl`,
  `downloadUrl`, `uploadUrl` and `eventSourceUrl` name. `SessionFetcher.cs:74-78` carefully sends a
  cross-origin redirect hop without credentials, but when that host answers 200 with a session
  document, the very next request posts the credentials to the `apiUrl` it chose. A session read
  over https may also name `http://` URLs (no downgrade check outside the redirect loop).
- **Reference:** Bulwark `lib/jmap/client.ts:2326-2362` (`rewriteSessionUrl(s)`): `apiUrl` is always
  moved onto the origin the user typed; the other URLs too, unless they are on another **https**
  origin than the reported `apiUrl` (a deliberate download host). Rust `jmap.rs:65` `absolute` had the
  C# behaviour, so this is an inherited gap, and it is also the known "Stalwart behind a reverse
  proxy advertises its internal host name" failure (RFC 8620 §2 allows relative and absolute URLs).
- **Fix:** port `rewriteSessionUrls` into `SessionParser` (it needs the `ServerEndpoint` the user
  typed, not only the final session URI); refuse any session URL that is `http` when the session came
  over `https`; make `AuthorizedHttp` attach the header only to the server origin and to https
  origins accepted by that rule; a session delivered by a cross-origin hop is accepted only if its
  URLs are rewritten to the typed origin. Tests: session naming another host for `apiUrl`, `http`
  downgrade, internal host name, a separate https download host. Update J-001/J-002.
- Related, same file family: `JmapHttp.cs:56` creates `SocketsHttpHandler` with the default
  `UseCookies = true`, and one client per origin is shared by every account on that server, so a
  cookie set for one account is sent with another account's requests. Set `UseCookies = false`
  (reqwest in Rust had no cookie store).

<a id="f-03"></a>
### F-03 (high, security; body stream, before the reader shows real mail): the frame sandbox is weaker than it needs to be and nothing strips active markup
What exists on `m1`: `MailCsp`, `MailSandbox`, `ShellDocument`, `ShellMessage` and `shell.js`. They are
careful and the tests are good (allow-list of messages, scheme check, no `location` writes). The
remaining holes are in the design, and they are cheap to close now, before the host is written:

1. **`allow-scripts` together with `allow-same-origin`.** `MailSandbox.For(true)`
   (`Core/Mail/Body/MailSandbox.cs:30-32`) grants both whenever the dark pass (or the accessibility
   script) runs, and `shell.js:35` forces `allow-same-origin` on every frame. The HTML standard warns
   that this pair lets framed content reach its parent; here the parent holds `window.__bb`, the host
   bridge (`chrome.webview` / `webkit.messageHandlers.unoWebView`) and every other message frame. The
   only thing between a mail and that is the frame's meta CSP hash. The shell's own policy is
   `script-src 'unsafe-inline'` (`ShellDocument.cs:14-15`), so if the meta tag is ever missing,
   misplaced after mail markup, or broken by an encoding trick, any inline script in the mail runs
   with full access. Rust had the same shape (`mailview.rs:133`, `:140`).
   **Fix:** never grant `allow-scripts`. `email_dark.js` already ends in `apply(document)`: run it
   from the shell against `f.contentDocument` (the shell is same-origin and already reads the frame
   document in `fix()`), and inject the accessibility changes the same way. Then every frame is
   `script-src 'none'`, `MailSandbox.Scripts` and the hash plumbing go away, and the "ready after the
   dark pass" rule becomes a plain call order in `shell.js`. In addition make the shell policy
   `script-src 'sha256-<shell.js>'` instead of `'unsafe-inline'` (a srcdoc frame inherits its
   embedder's policy, so this is a second, independent lock).
2. **`<meta http-equiv="refresh">` navigates the frame.** The sandbox blocks it only while
   `allow-scripts` is absent (HTML standard: the "automatic features" flag is lifted by
   `allow-scripts`). With the dark pass on, a mail can navigate its own frame to a remote URL when it
   is opened: a read receipt despite "remote content blocked", and remote content inside the reader.
   The shell's `frame-src 'self' about:` should stop it in Chromium; whether WebKitGTK applies the
   embedder's `frame-src` to a later navigation is unverified, and on Linux the CSP is the only
   guard (ADR 0005). `shell.js:109` also posts `ready` from the `load` event without the
   `about:srcdoc` check that the poll at `:116` has, so a navigated frame would be revealed.
   Fix 1 removes the cause; also add the `d.URL === 'about:srcdoc'` check to the `load` path, cancel
   `FrameNavigationStarting` for anything but `about:srcdoc` on Windows, and add a probe to the S2
   harness on both OSes (meta refresh, with and without `allow-scripts`, must not reach the beacon).
3. **No sanitizer.** Rust had none either (CSP and sandbox only); Bulwark runs DOMPurify
   (`lib/email-sanitization.ts`). With CSP as the only guard on Linux, strip at least `meta`
   (except charset), `link`, `base`, `script`, `iframe`, `object`, `embed`, `form` and `on*`
   attributes before the page is built (AngleSharp is already in the plan, §3.12), and put the CSP
   meta first in `<head>`, before any mail byte. `<link rel="dns-prefetch|preconnect">` and
   Chromium's speculative DNS lookups for link hosts are not reliably covered by CSP: pass
   `--dns-prefetch-disable` in `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` (today only the hidden runs
   set that variable, `HiddenWebView.cs:30-32`) and keep WebKitGTK's DNS prefetching off.
4. **Keys from the page.** `key:FLAGS|KEY` is accepted from any shell message. Harmless in M1
   (read-only). In M2 `Delete` and friends become destructive; with fix 1 a mail can never post, so
   this stays safe. Record it as the reason fix 1 must not be undone.
5. **Link opener (M2):** confirm with the full host shown in Unicode and punycode when they
   differ, never open `mailto:` with `attach`/`body` unseen, and keep the http/https/mailto
   allow-list (`ShellMessage.cs:107-116` is right).

<a id="f-04"></a>
### F-04 (medium, security; accounts stream): the OAuth loopback needs more than "state before error"
- **Evidence:** Rust `oauth.rs:164-224`: any request to `/callback` with `error` ends the sign-in, and
  a `code` with a wrong `state` also ends it. M1-plan §6.3 fixes the order ("checks `state` before
  anything else ... accepts exactly one callback").
- **Problem left:** a wrong or missing `state` must **not** end the wait at all. The listener is on a
  fixed port (8791, B-OAUTH-008) and any web page open in the user's browser can request
  `http://127.0.0.1:8791/callback?...` blindly, so "one callback, then fail" lets a page cancel every
  sign-in (RFC 8252 §8.3 treats the loopback as reachable by other local software; RFC 6749 §10.12).
- **Fix (for the accounts stream):** answer 400 to a request whose `state` is not the pending one
  and keep waiting until the 5 minute limit; accept only `GET /callback`; compare `state` in
  constant time; bind `127.0.0.1` only (never `localhost`, RFC 8252 §7.3); one accepted callback
  closes the listener; verify `iss` when the metadata sets
  `authorization_response_iss_parameter_supported` (RFC 9207); require `https` for an external
  provider's authorization and token endpoints (B-OAUTH-003 uses them "as given"); PKCE S256 with a
  43-128 character verifier (RFC 7636 §4.1; Rust's 48 random bytes are fine). Replace the stale
  lock file (60 s, B-OAUTH-015) with a held file handle or named mutex, which the OS releases when
  the process dies. The dynamic-registration cache holds public client ids only: a plain file under
  the profile is fine.

<a id="f-05"></a>
### F-05 (medium, correctness; accounts stream): all accounts in one credential will not fit
- **Evidence:** ADR 0008 decision 1 stores `accounts` as one JSON array in one generic credential.
  Windows caps a credential blob at 2560 bytes (`CRED_MAX_CREDENTIAL_BLOB_SIZE`, 5 × 512). An OAuth
  entry carries a refresh token, a token endpoint and a client id; a handful of accounts exceeds the
  cap and `CredWriteW` fails, at the moment the user adds one more account.
- **Fix:** one credential per account. The contract already allows it:
  `Platform/SecretName.cs` is `(Kind, Account)`. Keep a non-secret index of account keys in the
  profile's settings. Amend ADR 0008.

<a id="f-06"></a>
### F-06 (medium, safety rule): importing an OAuth entry from the Rust app signs the Rust app out
- **Evidence:** ADR 0008 decision 2: the import "validates by a normal token refresh". Stalwart
  rotates refresh tokens (B-OAUTH-015), so the first refresh by the C# app invalidates the token the
  Rust app still holds. That breaks the standing rule "never sign out the Rust app". The M1 plan
  already forbids it for agents (§1 rule 3); it is equally true for the user's own import.
- **Fix:** import password-kind entries only; for an OAuth entry import server and user and ask for a
  fresh sign-in (its own grant). See D-2.

<a id="f-07"></a>
### F-07 (medium, security; inherited from Rust): the "verified sender" chip trusts any topmost Authentication-Results
- **Evidence:** `Core/Mail/AuthResults/AuthenticationResults.cs`, `Parse` skips the authserv-id and
  `Topmost` takes the first header. RFC 8601 §5 (and §7.1) tells a client to trust only the header
  of its own receiving server. Where the server added none (Sent, Drafts, imported or copied mail, a
  listener without checks), the topmost header is the sender's own, and `dmarc=pass` in it yields a
  green chip. Rust `auth.rs:55` did the same.
- **Fix:** learn the server's authserv-id per account (the id on the topmost header of Inbox mail,
  checked against the server host) and ignore headers with another id; show no verdict outside
  received mail. One test with a forged inner header. See D-9 for the policy.

<a id="f-08"></a>
### F-08 (medium, security; AUTOMATION builds only): the automation port has no authentication
- **Evidence:** `Automation/AutomationServer.cs:34, :72-94`: any connection to `127.0.0.1:<port>` may
  send commands, among them `wv-eval` (script in the body shell), `shot PATH` (writes a file) and,
  from M2, write actions. The protocol is line based, so a web page can deliver a command blindly in
  the body of a cross-origin POST to the port (the HTTP header lines are just rejected lines).
- **Fix:** a per-run token (`BARBICAN_AUTOMATION_TOKEN`, generated by `tools/auto-run.ps1`) required
  as the first line of a connection; close the connection on the first line that looks like HTTP.
  Release builds are not affected (the project is not referenced; `Barbican.App.csproj:66-73`).

<a id="f-09"></a>
### F-09 (medium, robustness): a crafted PNG attachment can exhaust memory
- **Evidence:** `Core/Mail/Attachments/PngDecoder.cs:13, :50, :114`: up to 40 MP are accepted, which is
  about 320-360 MB per picture (raw scanlines plus RGBA) from a file of a few kilobytes; nothing
  bounds how many decode at once, and `TryDecode` (`:24`) does not catch `OutOfMemoryException`, so
  a mail with a dozen such attachments can end the process.
- **Fix:** decode scanline by scanline straight into the 2-row window the thumbnail needs (the area
  average in `ThumbnailScaler` only needs the rows under one output row), or lower the cap to about
  16 MP; decode one thumbnail at a time; skip a file whose IHDR says more than the cap before
  inflating anything (already the case).

<a id="f-10"></a>
### F-10 (medium, M1 scope): the Flagged tile searches for the words "is:starred"
- **Evidence:** `Presentation/Shell/MailShellStartup.cs:113-114` sends
  `SearchText = "is:starred"`; `Core/Mail/Service/ListFilters.For` turns that into the JMAP filter
  `{"text": "is:starred"}` (RFC 8621 §4.4.1), a full-text search for the literal. The tile shows
  nothing, or messages that contain that text.
- **Fix:** `new ListQuery { HasKeyword = "$flagged", PageSize = 3 }` (the property exists since the
  jmap merge). Rust used `hasKeyword: $flagged`.

<a id="f-11"></a>
### F-11 (medium, process): the M1 spec gate is green while 27 tests are red
- **Evidence:** `python tools/spec-check.py --milestone M1` prints "M1: 0 T- rows without a C# test"
  and exits 0, and on the same run "red tests: accounts 19, body 7, shell-ui 1". A T- row counts as
  ported when a test carries its tag, also when that test is `[Red]` and excluded from the default
  run (`tools/spec-check.py:141-151` only prints the count).
- **Fix:** a row whose only tests are `[Red]` is `pending`; `--milestone M<n>` fails while any
  `[Red]` test is left for that milestone. Same bookkeeping: J-009 and J-028 are "done" without
  exercising behaviour (jmap review); mark them `partial(M2)`.

<a id="f-12"></a>
### F-12 (medium, safety; accounts stream): read-only must fail closed
- **Evidence:** `JmapClientOptions.ReadOnly` is the only guard (`JmapClient.cs:64, :122`); the guard
  itself is right (`ReadOnlyGuard.cs`: suffix allow-list, uploads refused, unknown methods count as
  writes). Who sets it is not on `m1`; the plan says "a per-account flag that the readonly account
  always carries in agent profiles", and the real account name cannot be in the repository.
- **Fix:** in `dev` and `auto-*` profiles every account is read-only unless its key is on an
  allow-list read from `.local/` (the test account). A missing or unreadable list means read-only.
  Add a test that a profile without the list refuses `Email/set`.

<a id="f-13"></a>
### F-13 (medium; body stream): a normal run has no WebView2 user-data folder of its own
- **Evidence:** `App/Composition/HiddenWebView.cs:16-32` sets `WEBVIEW2_USER_DATA_FOLDER` only for
  hidden runs. A normal run falls back to WebView2's default, a folder next to the executable
  (read-only for an installed app, shared by every profile for the portable zip).
- **Fix:** always set it to `%LOCALAPPDATA%\Barbican[-<profile>]\WebView2` before the first WebView
  exists, and the matching data directory for WebKitGTK.

<a id="f-14"></a>
### F-14 (low, correctness): sync events and list edge cases
- `EmailsChangedEvent` carries the query's `removed` ids (`Core/Sync/MailSync.cs:198`) and no query.
  `Presentation/Mail/LoadedList.cs:89-110` removes those ids from whatever list is shown and lowers
  the total by one. For the same list this is a no-op after the `ListDeltaEvent`; after a folder
  switch it can remove a message that was moved **into** the list now shown. Fix: fill `Removed` from
  `Email/changes` `destroyed`, or carry the query and let the list ignore `Removed`.
- Two implementations of RFC 8620 §5.6 (`LoadedList.Apply` and `Core/Sync/ListDeltaApplier`);
  `LoadedList` already ignores `DestroyedThreads`. Delegate to the Core one.
- `ListPageFetcher.Page` (`ListPageFetcher.cs:143-160`) derives "more pages" from the number of rows
  that parsed, and `ModelReader.Read` drops a row on any `JsonException` without a trace: one odd
  message silently ends paging. Use the count of ids the query returned, and log the dropped id.
- A `ListResetEvent` reloads the first page only (`MessageListViewModel.cs:362-368`): pages loaded
  below are dropped and the selection is cleared while the reader stays open. Reload as many rows
  as were loaded, or close the reader.
- Unread-first Inbox is a partition of loaded rows; `ListQuery.NotKeyword` now exists, so the two
  queries of Rust `fetch_unread_first` can be ported.
- `RequestSplitter.IsLinked` treats any string value starting with `#` as a creation id (a search
  for "#tag"), so such a request is never split. `ServerEndpoint.Parse` adds `https://` only when
  the text does not start with "http", so a host name beginning with those letters is refused.
  `MailService.GetThreadAsync` keeps the server's `Email/get` order (RFC 8620 §5.1 does not
  promise the order of `ids`); sort by the thread's `emailIds`.

<a id="f-15"></a>
### F-15 (low, robustness): the sync engine and hooks
- Hooks are awaited inline inside the sync run (`MailSync.cs:90, :222`, `MailSync.Push.cs:124, :175`)
  and `HookBus` has no timeout: a plugin that never returns stops all later syncs of the account
  (`onNewEmailReceived` runs while the gate is held). Bulwark does not await. Use
  `HookBusExtensions.Fire` here, or give the bus a per-handler timeout before M6b.
- `SyncNowAsync` catches three exception types (`MailSync.cs:101`); anything else (for example an
  index error on a malformed response in `ListDeltaFetcher.Read`) faults the task started from the
  timer, and `DrainAsync` (`MailSync.Push.cs:70-86`) rethrows it out of `RunAsync`'s `finally`.
  Catch broadly at the top of the run and report through `onSyncStateChange`.
- The 700 ms debounce has no upper bound (a steady stream of pushes postpones the sync for ever;
  Rust too). Cap the total wait at about 3 s.
- A push open that fails with `AuthError` is retried for ever and never reaches the UI; a server
  without `eventSourceUrl` gets no updates at all. Emit an event for the first, plan a polling
  fallback for the second.

<a id="f-16"></a>
### F-16 (low): keys
`KeyRouter.Handle` ignores `KeySource` (`Presentation/Input/KeyRouter.cs:58`) and handles plain
arrows, which a focused `ListView` also handles. shell-ui must mark handled keys and must not
route keys typed into a text box. Hand test: one arrow press moves one row.

## 2. Decisions for the user

| # | Decision | Recommendation |
|---|---|---|
| D-1 | F-01: rewrite the public history to remove the pattern line from all 72 commits (force-push every branch; every worktree re-clones), or accept that these strings stay public. | Rewrite now, while there are no forks (GitHub reports 0) and M1 is not merged to `main`. Do it at a moment when no stream has unpushed work. |
| D-2 | F-06: may the import copy OAuth refresh tokens from the Rust app? | No: password entries only; OAuth accounts sign in again. |
| D-3 | Passwords are stored in clear inside the credential (as in Rust), local machine only, no roaming (ADR 0008). | Accept; prefer OAuth where the server offers it; no roaming. |
| D-4 | Lite mode dropped (T-159, D-233, "user OK pending"). | Confirm the drop. |
| D-5 | SQLite cache moved to M2; M1 cold start is judged against the 1.5 s "no cache" budget. | Accept. |
| D-6 | Feel gate uses the measured 21 ms capture quantum instead of 16 ms. | Accept until an on-screen measurement exists; it needs the user's screen once. |
| D-7 | Assistant "automatic" mode runs delete, move, rules and settings without asking (M6). | Destructive tools always confirm. |
| D-8 | Product deviations: sender pictures off by default; Sieve replace without `ifInState`; date groups follow Bulwark, not Rust; `g s` does nothing on Stalwart. | Accept the first and third; add `ifInState` in M6; port the virtual Starred list in M2. |
| D-9 | F-07: policy for Authentication-Results from an unknown authserv-id. | Ignore them (no chip). |
| D-10 | Plain `http://` servers are accepted (`ServerEndpoint.Parse`), sending the password in clear. | Allow loopback only; elsewhere warn and require an explicit choice. |
| D-11 | `sudo -n apt-get` in the VM setup script. | Accept for the VM; the `.deb` must declare the libraries instead (M9). |
| D-12 | Uno 7 move deferred until a 7.0.x is two weeks old (after 2026-11-27). | Accept; do it before wave 2 deepens the views. |
| D-13 | Trace-diff against Rust is derived from source, not from a live Rust trace. | Accept for the gate; one live trace by hand at HAND TEST 1. |
| D-14 | Sample data in `prototype/winui/Data.cs` names real companies and real-looking domains (an airline, a hosting invoice). Not personal data by the scan, but only the user knows whether it mirrors real mail. | Confirm or replace with `.example` data. |

## 3. What blocks the M1 gate
1. **F-01** (and D-1): fix before the next push that matters; every push republishes the line.
2. **Linux has never been green.** F1, F2, mailcore, jmap and both merges report the VM unreachable.
   "Green" needs `tools/linux.ps1 test` (CLAUDE.md).
3. **Three streams are not merged** (accounts, body, shell-ui): 27 red tests, 12 of 41 M1 hooks not
   fired, no sign-in, no body host, no views. F-11 hides this from the gate script.
4. **F-02 and F-03** before the first run against a real account (HAND TEST 1): both concern where
   the password goes and what a mail can do in the reader.
5. **F-04, F-05, F-06, F-12, F-13** are requirements on the accounts and body streams; check them at
   their merge.
6. **F-10** (a visible M1 tile is wrong).
7. No live check has been run by anyone (jmap section); the live suite needs the user or the Verify
   stage. The flicker and motion scenarios and the cold-start number do not exist yet.
8. The user's answers to D-2, D-4, D-5, D-6.

## 4. Lost from the Rust app (M1 scope)
Stated by the streams and checked against the plan; none is silent, each needs a milestone:
unread-first as two queries (F-14), the virtual Starred list (`g s`), one-time code chips on rows,
row tint by tag or account, unified inbox and Scheduled folder, sender pictures, the multi-account
unread badge, JPEG/GIF/WebP thumbnails (Core reads PNG only until the App registers a decoder), the
200-entry thumbnail cache, Rust's date grouping (D-8). Inherited weaknesses that the port should not
keep: F-02, F-03, F-07, and no polling without push (F-15).

## 5. Privacy scan result
- All 72 commits on all remote branches: author and committer are the GitHub noreply identity;
  no hit in commit messages; no hit in content except F-01. No home paths, LAN addresses, tokens,
  keys or private links. E-mail addresses are `.example` ones plus the prototype's sample senders
  (D-14) and public service addresses.
- Binaries in history: six PNGs under `docs/adr/` (looked at three: synthetic UI and sample text),
  the app icons. No text chunks with paths in them.
- The three commits named in FABLE item 5 are the rewritten ones (reachable from `main`); the
  GitHub activity log shows only branch creations and pushes, no force push, so no older objects
  were ever published. The repository has no forks.
- `JmapTrace` writes method names, `limit` values and error types only; `LoggingLinkRouter` logs the
  scheme only. `App.xaml.cs:27` prints unhandled exceptions with their text to stderr: keep message
  bodies out of exception messages.

<a id="verdicts"></a>
## 6. Verdict on every item of FABLE-REVIEW.md
Items are named by their number (M0) or their bold lead words.

**M0 consolidated**
1 popup rule: fine (constrained `Popup`, XAML-only flyouts, lint, reactive guard, private desktop;
the guard was read, `PopupGuard.cs`). 2 Linux body: confirmed, F-03. 3 OAuth loopback: confirmed,
F-04. 4 assistant: decision, D-7. 5 privacy: confirmed, F-01; the rest of the item is clean (§5).
6 secrets: confirmed, F-05 and F-06; D-3. 7 release content: fine (reference only under the
Automation configuration, static test and publish scan). 8 VM changes: decision, D-11.
9 SkiaSharp split: fine, on the recorded `project.assets.json` check (not re-verified, no build);
gate before M3. 10 editor offsets: not reviewed (M3 path). 11 Editor.Layout project: fine.
12 plan facts: fine (the user should read them). 13 UIA client: fine. 14 build infrastructure: fine
(not exercised). 15 analyzers: fine (`JmapError` is one type per file now). 16 licence: fine.
17 product deviations: decision, D-4 and D-8. 18 catalogue: fine for `oauth.md` (checked against
`oauth.rs:164-232`); the other review-written files were not re-read. 19 stale J- rows: fine.
20 interactions: fine (not re-sampled). 21 duplicates: fine. 22 settings: fine. 23 decisions: fine.
24 test rows: fine. 25 spec-check: confirmed, F-11. 26 motion numbers: fine. 27 capture and feel:
decision, D-6. 28 not launched: fine (the smoke run now exists). 29 editor gate: not reviewed (M3).
30 timing: fine.

**m1-plan**
Branch naming: fine (update CLAUDE.md at the gate). Five streams: fine. Contract skeleton: fine.
DI approach: fine, the predicted failure happened once (two hook buses) and is now tested.
F1 owns csproj: fine. I- rows overreach: fine (not re-counted). Live-check safety: confirmed, F-06
and F-12. OAuth in automation: fine. Loopback state: confirmed, F-04. Trace-diff: decision, D-13.
Feel gate: decision, D-6. SQLite deferred: decision, D-5. Lite drop: decision, D-4. S2 re-run: fine.
Hook count: fine (218 in 27). Mark-as-read never sent: fine. J-043/J-044 n/a: fine.

**f2-spec-tests**
Red convention: confirmed, F-11. Linux run: blocker (§3.2). PortPendingException: fine. Contract
deviations: fine. Contract additions: fine. Tests with a changed shape: fine (meaning not re-read
test by test). Hook count: fine. Milestone column: fine (`onBeforeExternalLink` in M2 is right, M1
opens no link). Hook owners: fine. T-159: decision, D-4. MailBodyView stub: fine.
IAutomationCommand: fine. CommunityToolkit.Mvvm: fine (referenced now). Test addresses: fine.
No fixtures: fine.

**f1-foundation**
Uno 7 deferred: decision, D-12. Popup guard reactive: fine. Deny list: fine. Private desktop: fine
(a desktop object is not an OS change). Monitor check: fine (failing closed is right).
Release enforcement: fine. Sandbox deviation: confirmed, F-03. CSP base-uri: fine. `dim`: fine.
Link router placeholder: fine (F-03.5 for M2). Smoke WebView: fine. ShellPage crash: fine (emit
typed resources). `key` stub: fine. MA0051: fine. win-x64: fine. Profile folders: confirmed, F-13.
IStartupTask: fine. Branch name: fine.

**mailcore**
PrefsSnapshot: fine. PNG only: confirmed, F-09. LoadedList copy: confirmed, F-14. Unread-first:
confirmed, F-14. MailEventHub runs the engine: fine, on the rule that accounts never starts it.
Arrow keys: confirmed, F-16. Escape and `g s`: fine (D-8). `key` command: fine (open for shell-ui).
Hook call sites: fine. Zero allocation: fine. ListSync reset: fine. Package reference: fine.
Flagged tile: confirmed, F-10. Not ported: fine (§4). Date groups: decision, D-8. Doubles: fine.
Two settings: fine. Opener never marks read: fine. Attachment index: fine. Shift+G: fine.
Delta dropped while loading: fine (F-14 for the companion event). KeySource: confirmed, F-16.
Linux VM: blocker. PNG memory: confirmed, F-09.

**jmap**
`using`: fine. Redirect policy: confirmed, F-02. 402 TOTP: fine (unverified against a real
response; first live check). Contract additions: fine. Null converters: fine. Hooks inline:
confirmed, F-15. onNewEmailReceived: fine. Notification prefs: fine. ListResetEvent: confirmed,
F-14. Debounce: confirmed, F-15. Session as JsonElement: fine. J-051, J-015: fine. Trace-diff:
decision, D-13. Live checks: blocker (§3.7). Coverage: fine. Files outside: fine. Linux VM:
blocker. Requests to other streams: fine (done at the merge). The three "Fixed" items: fine (read:
`MailSync.cs:83-113`, `JmapTrace.cs`, `MailSync.Push.cs:121-123`). Thin rows: confirmed, F-11.
No push: confirmed, F-15. Delta emitted twice: confirmed, F-14. Merge fixes: fine.

Also read and found correct: the RFC 8620 §5.6 delta application over a loaded prefix with anchor
paging, the `/changes` handling (`cannotCalculateChanges`, `hasMoreChanges`, state going back), the
Stalwart state codec, the SSE parser and the 75 s deadline, the backoff, `ReadOnlyGuard`, the
401 renewal path, the request size and concurrency gates, `ShellMessage` validation.
