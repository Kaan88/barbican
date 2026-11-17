# Fable review 2 (`m1` at `4eccc0f`)

Reviewer: Fable, 2026-11-17, branch `fable-review-2` (from `origin/m1` at `4eccc0f`, not merged). Scope:

1. the accounts, shell-ui and body code that merged after review 1 (`git diff 32ced0b origin/m1`, 340 files);
2. whether F-02 to F-16 of `docs/fable-review-1.md` (on branch `fable/review-1`) are really fixed;
3. the top items (1 to 23) of [FABLE-REVIEW.md](FABLE-REVIEW.md) "M1, consolidated at the verify stage".

How it was done: reading only. Nothing was built, tested or launched, no mail server was contacted, `<admin-key-file>`
was not read. The only script run is `python tools/spec-check.py` (reads CSVs and test sources). Server behaviour is
quoted from the Stalwart 0.16.27 source, Bulwark from `<bulwark>`, the Rust app from `<rust-repo>` at `rust-final`.
**Everything about Linux is unverified**: no stream has run there yet.

Verdict words: **fixed**, **partly fixed**, **open**, **confirmed** (a real problem, with a fix), **fine**,
**decision** (the user must decide), **not reviewed**.

<a id="f-verdicts"></a>
## 1. Are F-02 to F-16 fixed?

| Finding | Verdict | What is left |
|---|---|---|
| F-02 session URLs | **fixed** | Two low leftovers, [R2-09](#r2-09). A session's own "hosted elsewhere" https origins still get the Basic password, as in Bulwark (`lib/jmap/client.ts:2326-2362`): fine. |
| F-03 mail frame | **fixed on Windows**, Linux unproven | Scripts cannot run in a frame (`MailSandbox.Frame`, `MailCsp` `script-src 'none'`, sanitizer). New: [R2-04](#r2-04) (a mail can switch the shell's own handling of its frame off). |
| F-04 OAuth loopback | **fixed** | Low notes only: the listener serves one connection at a time with a 5 s read limit (`Core/Auth/LoopbackListener.cs:103-108, :166`), so a local process can delay the callback; the lock on Linux is unproven. |
| F-05 one credential per account | **fixed** for one running instance | [R2-06](#r2-06): two instances on one profile can overwrite each other. One OAuth account with a refresh token over about 2.3 KB still does not fit one entry (clear error). |
| F-06 import never takes OAuth tokens | **fixed** | `Platform/RustCredentialImport.cs:109` drops the refresh token. |
| F-07 Authentication-Results | **partly fixed** | [R2-07](#r2-07): the authserv-id is text a sender can write; the rule holds only when the server put its own header on top. |
| F-08 automation token | **fixed** | Read: `Automation/Protocol/AutomationConnection.cs`, `tools/auto-run.ps1`, `tools/autoconn.py`. |
| F-09 PNG bombs | **mostly fixed** | [R2-08](#r2-08): the cap is on pixels, not on the row. |
| F-10 Flagged tile | **fixed** | `Presentation/Shell/MailShellStartup.cs:114`: `HasKeyword = "$flagged"`. |
| F-11 spec gate | **fixed** | `tools/spec-check.py:213-228, :270`; J-009 and J-028 are `partial(M2)`. Ran it: M1 75 ported, 1 dropped, 0 pending, 0 red tests. The printed words "passing C# test" promise more than a static scan can know. |
| F-12 read-only fails closed | **fixed** | Note for M2: [R2-14](#r2-14). |
| F-13 WebView2 data folder | **fixed on Windows** | WebKitGTK data directory not done. |
| F-14 sync and list edge cases | **fixed** | Checked: `LoadedList` delegates to `ListDeltaApplier`; `ListPageFetcher.Page` counts ids; `GetThreadAsync` sorts by `emailIds`; `RequestSplitter.IsLinked` looks for creation ids in `/set` only; reset reloads the loaded rows. Leftovers in [R2-13](#r2-13). |
| F-15 sync engine and hooks | **fixed as asked**, with one serious gap | The retry-for-ever part was answered with a message, not with a stop: [R2-01](#r2-01). Hook timeouts: [R2-10](#r2-10). |
| F-16 keys | **partly fixed** | Arrows, AltGr and Shift+digit are handled. Enter on a focused button is still swallowed: [R2-05](#r2-05). Layout: [R2-11](#r2-11). |

<a id="new"></a>
## 2. New confirmed problems, most serious first

<a id="r2-01"></a>
### R2-01 (high, safety of the user's server access): a rejected sign-in is sent again and again
- **What happens.** Once the server answers 401, nothing stops the account from asking again with the same secret.
  - Basic: `Core/Jmap/Transport/StaticAccessTokenSource.cs:29` hands out the same header for ever; `:32-33` only
    returns an error. `AuthorizedHttp.SendAsync` (`AuthorizedHttp.cs:33-58`) sends first and learns afterwards.
  - Push: `Core/Sync/MailSync.Push.cs:156-167` reports the first `AuthError` and then keeps re-opening the stream with
    the 2 s to 300 s backoff (`RunAsync` `:45-57`), about 240 tries a day.
  - Background accounts: `Core/Accounts/AccountManager.cs:26, :268-281, :543` reads the unread count of every connected
    account every 2 minutes, 720 tries a day.
  - Every sync after a push and every click that needs the server adds one more.
  - OAuth: a refused refresh token is posted again on each call (`Core/Auth/OAuthTokenSource.cs:100-106, :115-155`).
- **Why it matters.** Stalwart counts every failed login and blocks the address:
  `crates/common/src/auth/authentication.rs:39-76` calls `is_auth_fail2banned`; the default is 100 failures a day
  (`crates/registry/src/schema/structs_impl.rs:35589-35592`) and the default ban has no end
  (`auth_ban_period` unset, `crates/common/src/network/security.rs:206-215`). A blocked address locks out the Rust app
  and the webmail on the same network too, and only a server administrator can lift it. Agents may not change server
  settings.
- **How it starts in normal use.**
  1. The password is changed, or an app password is revoked, while Barbican runs.
  2. **Sign-in with a one-time code** (HAND TEST 1, step 1). The session's credential is `password$code` for the whole
     run (`StaticAccessTokenSource.cs:17-22`, Rust `src/jmap.rs:124-126`, `src/main.rs:753-765`). Stalwart checks a
     Basic header once and remembers it for the access-token lifetime, one hour by default
     (`crates/http/src/auth/authenticate.rs:32, :86, :94-101`; `structs_impl.rs:30825`). After that the header is
     checked again, the code is long spent (RFC 6238), and every request fails. By the source, a hand-test session
     left open for a few hours reaches 100 failures.
- **Not observed, derived from source.** No agent ran it, and no agent should: the test itself can block the user's
  address. The Rust app has the same design.
- **Fix.**
  1. A latch in the client: after a 401 that the token source cannot renew, or a refused refresh token
     (RFC 6749 §5.2 `invalid_grant`), the client fails every later call locally with `AuthError(NeedsSignIn)` and
     sends nothing. A new sign-in creates a new client.
  2. `MailSync.RunAsync` ends the push loop and the poll on that error; `AccountManager` marks the entry
     ("needs to sign in again"), drops it from the unread timer, and the shell shows the sign-in form with server and
     login filled in.
  3. Tests: a fake server that answers 401 sees exactly one request per call site and none after the latch.

<a id="r2-02"></a>
### R2-02 (medium, M1 scope): the newest account cannot connect at start, so the app shows the sign-in form
- **Evidence.** `AccountManager.RestoreAsync` (`AccountManager.cs:115-146`) connects the first remembered account and
  returns null when that fails; the others connect in the background but none becomes active. `ShellPage.ShowScreen`
  (`App/Shell/ShellPage.xaml.cs:75-81`) then shows the sign-in form, and the account popover lives on the hidden mail
  screen. Nothing retries: a start before the network is up, or with the first server down, looks like a lost
  sign-in until the next restart.
- **Fix.** Activate the first account that connects, in the remembered order. When every failure is a transport
  error, stay on the mail screen in a "connecting" state and retry with backoff (M2's cache needs that state anyway).
  Show the sign-in form only for `NeedsSignIn`, with server and login filled in and the reason on screen.

<a id="r2-03"></a>
### R2-03 (medium, M1 scope): the sign-in and popover views still do not use the accounts ViewModels
Item 15 of the verify stage, unchanged, and now it contradicts the hand test.
- `App/Views/SignIn/SignInFormModel.cs:2` and `App/Views/Accounts/AccountPopoverModel.cs:2` still say the accounts
  ViewModels are "an empty stub". They are not: `Presentation/Accounts/SignInViewModel.cs` (353 lines) has the server
  lookup, the provider notice and Cancel.
- On screen today: no server lookup (HAND TEST 1 step 1 asks the user to check it), no provider notice, and an OAuth
  wait that cannot be cancelled: `SignInFormModel.cs:159, :192` pass `CancellationToken.None` and the listener waits
  5 minutes (`Core/Auth/LoopbackListener.cs:30`). A closed browser tab leaves the form busy that long.
- Enter in the password or code field does nothing (`SignInView.xaml:23-53` has no handler; Bulwark's form submits).
- `async void` click handlers (`SignInView.xaml.cs:31, :35`) around a method with `try`/`finally` and no `catch`: an
  exception from the vault write ends the process.
- The popover listens to `ActiveChanged` only (`AccountPopoverModel.cs:26`), so "needs to sign in again" and unread
  counts never show.
- **Fix.** Bind `SignInView` to `SignInViewModel` and the popover to `AccountsViewModel`, delete the two App models,
  submit on Enter, catch in the handlers. If that waits for M2, change the hand test text so it does not promise it.

<a id="r2-04"></a>
### R2-04 (medium on Linux, low on Windows): a mail can turn off the shell's handling of its own frame
Two parts of one problem. No script runs and nothing leaks on Windows; the cost is a mail that opens slowly, shows
without link handling, or stays blank in dark mode. On Linux the second guard is one CSP directive that never ran.
- **Named elements shadow `document` members.** `Document` gives `<img name="x">` precedence over its own members
  (WHATWG HTML, named access on `Document`). `Core/Mail/Body/MailSanitizer.cs:22-33, :110-125` keeps `name` on `img`.
  `Assets/mail/shell.js` reads the frame's document directly:
  - `:146` `d.URL !== 'about:srcdoc' || !d.body`: `<img name="URL">` or `name="body"` means `finish()` never succeeds
    and the poll at `:166-172` spins until the host gives up;
  - `:85` `d.querySelectorAll`, `:89-99` `d.__bbd`, `d.addEventListener`, `d.defaultView`: no `target=_blank`
    fallback, no link, key or context-menu handlers;
  - `:106-125` `d.body`, `d.documentElement`, `:131`: no text sizing; the dark-pending class stays and hides the body.
  Bulwark runs DOMPurify, whose default `SANITIZE_DOM` removes such names (`lib/email-sanitization.ts:9-57`).
- **The give-up path reveals a frame that never passed `finish()`.** `App/MailBody/BodyShell.cs:353-369` calls
  `Ready(seq)` after 8 s, `:372-395` sends `reveal`, and `shell.js:174-182` only compares the sequence number. The
  comment at `shell.js:144` ("only the frame's own srcdoc is ever finished and revealed") is therefore not true.
- **What still protects.** Windows cancels every frame navigation but `about:srcdoc` (`BodyShell.cs:292-300`). On
  Linux only the shell's `frame-src 'self' about:` (`Core/Mail/Body/ShellDocument.cs:18-19`) stands between a click in
  an unhandled frame and a remote page inside the reader, and WebKitGTK's own context menu shows.
- **Fix.**
  1. Sanitizer: drop `name` on `img` (and on anything else that can name a document member), and `id` on `img` that
     has a `name`. Test with `name="URL"`, `"body"`, `"querySelectorAll"`, `"addEventListener"`, `"__bbd"`.
  2. `shell.js`: call through the prototypes (`Document.prototype.querySelectorAll.call(d, ...)`,
     `EventTarget.prototype.addEventListener.call(d, ...)`, the `URL`, `body` and `documentElement` getters taken from
     `Document.prototype`) and keep the "handlers attached" mark in a `WeakSet`, not on the document.
  3. `finish()` marks the frame; `reveal` refuses an unmarked one; on give-up the host shows
     `MailPageBuilder.Error` instead.
  4. Run the meta-refresh and link probes on Linux before Linux counts as green.

<a id="r2-05"></a>
### R2-05 (medium, keyboard access): Enter on a focused button opens the selected message instead
- **Evidence.** `App/Shell/ShellPage.xaml.cs:92-108` routes every `PreviewKeyDown` whose source is not a text field
  (`:111`). `Presentation/Input/KeyRouter.cs:160, :198-199` turns Enter into "open", which is handled whenever a
  message is selected (`MessageListViewModel.cs:342-351`). `e.Handled = true` on the tunnelling event means the
  button never sees the key. "Load external content", "Show formatted", the popover rows and, during "Add account",
  the Sign in button are affected (WCAG 2.1.1). HAND TEST 1 step 6 will fail on this.
- The router also runs while the sign-in screen is up: `/` moves focus to the hidden search box, `Ctrl+1..5` switch
  the app, `g i` changes the folder.
- **Fix.** Route nothing while the sign-in screen shows. Route Enter and Space only when the focus is inside the
  message list or on nothing that acts on them (`ButtonBase`, `ComboBox`, `ToggleSwitch`, a selector item outside the
  list).

<a id="r2-06"></a>
### R2-06 (medium-low): two instances on one profile can lose an account or a rotated token
- **Evidence.** `Platform/SecretAccountVault.cs:68-107` writes every entry and then deletes `old.Except(ids)`.
  `Core/Accounts/AccountStore.cs:125` (`ChangeAsync`) works from the list cached at first load (`:14, :115-122`). A
  second instance that signed in an account, or rotated a refresh token, is overwritten by the first instance's next
  save. After that `OAuthTokenSource.RefreshAsync` (`:117, :145-152`) uses the stored (stale) token and, on rejection,
  never tries its own newer one: both instances ask for a new sign-in.
- **Fix.** `ChangeAsync` reloads from the vault inside the refresh lock before it changes anything. The vault gets
  "upsert one" and "remove one" and never deletes from a stale view. On rejection, try the in-memory token once when
  it differs from the stored one.

<a id="r2-07"></a>
### R2-07 (low-medium, M2 with the trust banner): the trusted Authentication-Results rule can be met by the sender
- **Evidence.** `Core/Mail/AuthResults/AuthServTrust.cs:26-48` trusts an authserv-id equal to the server host or under
  its parent domain; `AuthenticationResults.Trusted` (`:65-71`) takes the topmost header that passes. The id is the
  first token of a header anyone can write (RFC 8601 §1.6, §5: a border server must remove or rename forged ones).
  Stalwart writes its own header above the message (`crates/smtp/src/inbound/data.rs:322, :482-501`) and I found no
  code that removes inbound headers carrying its id. So a message that gets no server header, such as mail submitted
  by another user of the same server, imported mail, or a listener with the header turned off, can carry a forged
  `dmarc=pass` and show a green chip.
- The parent-domain rule also trusts every host under a two-label public suffix.
- The other direction is a hand test: a server whose own name is not under the typed host's parent domain shows no
  chip at all.
- **Fix (M2).** Accept only a header inside the server's own trace block (below the topmost `Received` whose "by"
  host equals the authserv-id and above the next `Received`), learn and pin that id per account, and give no verdict
  in Sent and Drafts.

<a id="r2-08"></a>
### R2-08 (low): a one-row PNG still allocates gigabytes
`Core/Mail/Attachments/PngDecoder.cs:61` caps width × height (`:18, :21`: 16 MP and 100 MP). `:133-136` allocates
three buffers of `width × channels`. A 100,000,000 × 1 RGBA picture passes the cap and asks for about 1.2 GB. Nothing
calls the thumbnail path yet. **Fix:** refuse a width over 32,768 (or a row over 16 MB) in `Header`, with a test.

<a id="r2-09"></a>
### R2-09 (low): two leftovers in the server address
- `Core/Jmap/ServerEndpoint.cs:62-67` uses `Uri.IsLoopback`, which .NET also answers with true for the host name
  `loopback`. That name is not promised to be this machine, so `http://loopback` sends a password in clear (D-10).
  **Fix:** accept only `localhost`, 127.0.0.0/8 and `::1`.
- `ServerEndpoint.Read` accepts `https://user@host`. The user part ends up in every session URL, and
  `JmapSession.AllowsCredentials` (`Core/Jmap/JmapSession.cs:68`) then refuses every request with "not this account's
  server". **Fix:** refuse user info in `Read` with a plain message.

<a id="r2-10"></a>
### R2-10 (low now, required before M3 and M6b): hook timeouts differ from Bulwark where it matters
- `Core/Plugins/Hooks/HookBus.cs:13` gives every handler 5 s, and an interceptor that runs out of time or throws
  counts as "allow" (`:79-108`). Bulwark gives interceptors 60 s because they wait on a dialog
  (`lib/plugin-hooks.ts:62-66, :290`) and fails closed for actions that cannot be taken back, such as send
  (`:186-188, :293-296`, `takeOver` `:307-325`).
- `RunAsync` (`:146-170`) starts the handler on the caller's thread (`:149`); a handler that blocks before its first
  `await` is not bounded by the timeout.
- **Fix.** A per-hook budget and a per-hook "fail closed" flag in the hook catalogue before the first send guard
  exists; native plugin handlers start on the pool.

<a id="r2-11"></a>
### R2-11 (low): `/` and `?` are read from the key position, not from the character
`App/Shell/KeyChordMapper.cs:41` maps virtual keys 111 and 191. Key 191 is the slash only on a US layout; on a German
layout it types `#`, and the real `/` (Shift+7) is dropped by the Shift+digit rule (`:19-23`). Bulwark lets the typed
character win (`lib/keyboard.ts:96-101`). **Fix:** take punctuation from the character (`ToUnicodeEx`, or
`CharacterReceived`), keep the virtual key for letters.

<a id="r2-12"></a>
### R2-12 (low, feel): a push can scroll the list back to the selected row
`App/Views/List/MessageListView.xaml.cs:83-106`: when a delta replaces the selected row's view model, the `ListView`
drops its selection, `SyncSelection` sets it again and calls `ScrollIntoView`. A user who scrolled away while a
message is open is pulled back. **Fix:** scroll only when the selected id changed.

<a id="r2-13"></a>
### R2-13 (low): smaller points
- `Core/Sync/MailSync.cs:130-150`: `error ??= await ...` skips the list sync and the new-mail check once the folder
  sync failed. Run all three and keep the first error.
- `Core/Sync/MailSync.Push.cs:159-162`: any non-retryable transport error switches to polling for the rest of the
  run, also `AuthorizedHttp.Refused` (an event-source URL on a foreign origin). Fine, but say so in the trace.
- `Presentation/Mail/MailEventHub.cs:70-88`: `Sync` and `AccountKey` are read one after the other; a switch in
  between pairs one account's engine with the other's key. Read both from one snapshot.
- `Core/Accounts/AccountManager.cs:359-386`: `ConnectStoredAsync` starts under `_lock`, so `StatusesChanged` can be
  raised while the lock is held. Raise events outside it.
- `App/MailBody/MailBodyView.cs:57-66`: fine in the usual order; WinUI can raise `Unloaded` after a quick `Loaded`,
  which would leave a visible view unsubscribed. Check `IsLoaded` in both handlers.
- Unread-first Inbox (`MessageListViewModel.cs:445-475`): a conversation with read and unread messages shows twice,
  and unread messages beyond the section size never show. Both as Rust (`src/mail.rs:599-607`).
- `shell.js:125`: "fit to the pane" is measured before images have loaded.
- `Core/Mail/Body/BodyShape.Normalize` still works around null lists from `ModelReader` (verify item 17).
- `Platform.Linux/SecretServiceStore.cs`: the cancellation token is not used, and a locked keyring is an error with
  no way to unlock it. Never ran.

<a id="r2-14"></a>
### R2-14 (note for M2): an imported account can never be opened for writing
`Core/Accounts/ReadOnlyPolicy.cs:72-75`: a stored read-only flag always wins. The agent import sets it on every
account (`Platform/RustCredentialImport.cs:113`), so the `.local` allow-list cannot open an imported `<test-account>`
for M2's write tests. Decide in M2 whether the allow-list may clear the flag in agent profiles.

<a id="verify"></a>
## 3. Verify-stage items 1 to 23

| # | Item | Verdict |
|---|---|---|
| 1 | Mail frame sandbox and CSP | **fixed**: no `allow-scripts`, `script-src 'none'`; the text of the item is out of date. See R2-04. |
| 2 | Linux body security is CSP-only | **open**, never ran. R2-04 makes the Linux run matter more. |
| 3 | Remote-content permission per page | **fine** by reading (`BodyShell.cs:302-312`, `RemoteAllowed`); the filter is shell-wide while two pages are alive, and treats only http and https as remote (the CSP covers the rest). Still no automated test. |
| 4 | OAuth | **fine** (F-04). Wrong state ignored and the wait goes on: recommend accepting it. |
| 5 | Secrets | **fixed** for the blob limit (F-05); R2-06; Linux store never ran. |
| 6 | Import and read-only | **fine** (F-06, F-12); R2-14. |
| 7 | Hidden automation | **not reviewed** again (unchanged since review 1 apart from the token, F-08). |
| 8 | Session discovery | **fixed** (F-02); the cross-origin 401 now names the origin to type. |
| 9 | PNG decoder | **mostly fixed**; R2-08. |
| 10 | X11 stand-in | **not reviewed** (no caller, never ran). |
| 11 | Privacy check vs. provider name | **decision**, unchanged. |
| 12 | Body cache can mix accounts | **fixed**: `MailEventHub.cs:86-97` carries the engine's account key, `BodyCacheSync.cs:22-29` files under it. |
| 13 | List delta plumbing | **fixed**; the delta still arrives as two events (two rebuilds), harmless. |
| 14 | Who runs the sync loop | **fine**: `MailEventHub.cs:92` is the only caller of `IMailSync.RunAsync`. Accounts that are not active have no push; their unread counts come from the 2-minute timer. |
| 15 | Views bypass the accounts ViewModels | **open**: R2-03. |
| 16 | App composition untested | **partly fixed**: `CompositionGraphTests` covers Core and Presentation; a cycle through `BodyShell`, `AppComposition` or a view is still found only at launch. |
| 17 | Null lists in `EmailBody` | **open** (low), R2-13. |
| 18 | Keyboard routing | **partly fixed**: R2-05, R2-11. |
| 19 | Hook semantics | **partly fixed**: R2-10. |
| 20 | Window events reach the hooks | **not reviewed** (hand test). |
| 21 | Body cache and loader | **partly fixed**: `MailBodyView` unsubscribes; the cache bounds stay global; `IBodyOverlayStrategy` still has no caller. |
| 22 | `AccountManager` design | **confirmed**: R2-01 (unread timer), R2-02, R2-13 (events under the lock). The shared-attempt code itself reads right now (`:359-386`, cleared in `finally`). |
| 23 | Push edge cases | **fixed**: 3 s cap, 60 s polling fallback. R2-01 for the auth case. |

Items 24 to 39 (deviations, partially verified measurements) were not looked at again.

<a id="decisions"></a>
## 4. Decisions for the user

| # | Decision | Recommendation |
|---|---|---|
| E-1 | R2-01: do HAND TEST 1 before the fix? | No long session on a real account before the latch exists. If the test must happen first: sign in with the browser (OAuth) or an app password, not with password + one-time code, and close the app when done. |
| E-2 | R2-01: keep "password + one-time code" as a way to sign in? By the server source such a session ends after the token lifetime (one hour by default) and cannot be remembered. | Keep it, but end the session cleanly at the first 401 and steer accounts with a second factor to the browser sign-in. |
| E-3 | R2-02: what the app shows at start without a network until M2's cache exists. | Mail screen in a "connecting" state with retry; never the empty sign-in form for a remembered account. |
| E-4 | R2-03: bind the views to the accounts ViewModels in M1, or move it to M2 and reword the hand test. | In M1: the lookup and Cancel are written and tested, only the binding is missing. |
| E-5 | R2-07 / D-9: accept the parent-domain rule for M1. | Accept for M1; pin the id per account in M2. Check by hand that chips still show on the real account. |
| E-6 | Remote style sheets and web fonts never load, also after "Load external content" (`link` is removed; same as Bulwark `lib/email-sanitization.ts:25-27`, different from Rust). | Accept. |
| E-7 | Linux keyring locked: may a user run (not an agent run) ask the desktop to unlock it? | Yes, in user runs only. |
| E-8 | Still open from review 1 as far as I can see: D-4 (Lite drop), D-5 (SQLite to M2), D-6 (21 ms quantum), D-8, D-11 to D-14. D-1 stays "not approved", so the strings F-01 found remain in the public history. | As in review 1. |

<a id="gate"></a>
## 5. What blocks the M1 gate

1. **R2-01** before any real account stays signed in for hours (HAND TEST 1), and E-1.
2. **Linux has never been green** (unchanged). With it: R2-04 on WebKitGTK, the Secret Service store, `flock`.
3. **R2-03**: the hand test asks for things that are not on screen (server lookup, OAuth cancel).
4. **R2-05**: hand test step 6 fails by reading the code.
5. **R2-02**: a start without network, or with the newest account failing, shows the sign-in form.
6. **Frame gates still unproven**; the cost of the new `requestAnimationFrame` wait in `shell.js:152-157` (up to one
   frame per message, 34 ms timer in a hidden window) was not measured.
7. The decisions above and M1.md §6.

Not blockers for M1, but owed before their milestone: R2-06 (before M2 sign-out), R2-07 (M2 trust banner), R2-08
(before thumbnails are wired), R2-10 (before M3 send guards), R2-14 (before M2 write tests).

## 6. Not reviewed

- Anything at run time: no build, no test run, no launch, no Linux.
- Layout, motion and theme of the shell-ui views (`Styles/Controls.xaml`, `StyleState`, `Motion/*`, `ReaderView.xaml`,
  `SidebarView`, `RailView`); only key handling, the list code-behind and the account screens were read.
- `X11BodyOverlay.cs`, `WindowsLegacyCredentialSource`, the browser launchers, `PrefsStore`/`AtomicFile`,
  `CidImages`, `MessagePrefetcher`: skimmed or not opened.
- The planning documents were used for context only (M1.md and this log in full; PORT-PLAN, ROADMAP, the ADR index
  and M1-plan were not re-read line by line in this pass).
- Whether a one-time-code session really ends after an hour on the user's server (R2-01): derived from source.

## 7. Privacy

This branch adds this file and marks in `FABLE-REVIEW.md`. It names no account, host, user or local path;
`sh tools/privacy-check.sh --worktree` was run before the commit.
