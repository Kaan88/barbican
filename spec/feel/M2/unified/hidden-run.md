# M2 unified: hidden automation run (Windows, port 8885, profile auto-8885, BARBICAN_SAMPLE=1)

Only through `tools/auto-run.ps1` (the hidden AUTOMATION build: off-screen, non-activating, popup guard on). A sample run has two
in-process FakeJmap logins: the sample one (`SampleActiveMail`) and `sample-2` (`SampleUnifiedLogins`, four messages in its Inbox);
every request of either login is logged per login (`unified log`). No real account, no real input. The first start used
`-PrivateDesktop`; `wshot` cannot capture there (`ArgumentException`), so the run with captures used the normal hidden start.

| Step | Command | Result |
|---|---|---|
| plugin host | `plugins` | `plugins 1 spam-score[on,bundled,email.details] errors=0` (the build copies the plugin to `plugins/spam-score`) |
| unified on | `unified on` | `unified enabled=True cross=True group=True sources=2 open=True badge=9` |
| frame | `wshot artifacts/auto/8885/hidden_unified.png` | 1384x884, not uniform: "All inboxes" header with "9 unread", the four `sample-2` rows (today) merged by time above the sample rows (2 Nov), the "All inboxes 9" sidebar row |
| star a row of the second account | `select #0` (`ok sample-2<FS>e4`), `act mail.star` (`ok mail.star`) | |
| request log | `unified log` | `sample: requests=68 Email/set=0 ... \| sample-2: requests=27 Email/set=1 last=Email/set`: the write went only to the second login |
| import | `import-sim <run>/dup.mbox inbox` (3 messages, the third repeats the first's Message-ID) | `imported 2 already 1 errors 0`; `import-sim state`: `Imported 2 messages, 1 already in the folder` |
| import, 500 messages | `import-sim <run>/big.mbox inbox` | `imported 500 already 0 errors 0`, under 1.5 s on the in-memory fake (too fast for a live `import-sim cancel` to land: cancel is proven by `MailImportServiceTests.A_cancel_stops_between_batches` and `MailTransferTests.Cancel_stops_the_running_import`) |
| spam row | `rx-banners` | `banners sample-2<FS>e4 0`: **not proven here.** The reader loads the open row through the active account with the row id as is, so a unified row of another login shows no body (request to reader: resolve rows with `IMailTargetResolver`); the sample login's seed has no M2 messages (`SeedM2` off in `SampleActiveMail`, not this stream's file), so no seed message carries spam headers. The spam row itself is proven by `SpamScoreRulesTests` and T-206 |
| sign out of the second account | `menu-open ACCOUNT`, `menu-items` | `Set as default \| - \| Sign out \| Sign out of all accounts \| - \| !Manage accounts`; **not picked**: `sample-2` is not an account of the account manager (a sample run has none), so `menu-pick` would sign out nothing. Sign out of one and of all is proven on FakeJmap by `AccountManagerTests.SignOut` (4 tests) |
| popup guard | `key Escape`, `popup-guard` | `open 0 closed 0` |
| focus guard | `focus-guard` | `own-is-foreground=no`; the foreground window and cursor before and after each start unchanged (auto-run's record) |
| close | `close` | `ok` |

A repeated import of the same mbox imports 2 again on the fake: FakeStore's `ImportBlob` gives an imported message a new Message-ID of its own
(`<id>@example.org`) instead of the header's, so the folder's Message-IDs never match an import. Request to the FakeJmap owner; on a real server
(and in `MailImportServiceTests`, where the folder's message has the matching id) the duplicate is counted.

## Feel (port 8885, `python spec/feel/M2/unified/feel.py --port 8885 --modes light,dark`, Bento)

| Scenario | Light | Dark |
|---|---|---|
| `unified_open` (Inbox -> All inboxes) | 13 frames, bad 0, blank 0, flash 0, list changed in 10 frames, first/last frame spread 22.2/24.1 | 20 frames, bad 0, blank 0, flash 0, spread 20.4/22.5 |
| `account_switch` (All inboxes -> Inbox of the account on screen) | 14 frames, bad 0, spread 24.1/22.4 | 30 frames, bad 0, spread 22.5/20.7 |
| `import_progress` (500 messages land in the open Inbox) | 1 frame: **invalid** | 1 frame, reply `timeout`: **invalid** |

The captures are not uniform (spread > 2 on every first and last frame), and no frame is flagged, but every burst stayed under the frame count
`tools/scenarios.py` asks for (1800 ms / 40 = 45) after three tries: the capture thread was starved (the import scenario blocks the UI thread
while the in-memory import runs). **No feel number is claimed**; the integrate stage reruns these on an idle machine. `account_switch` is the
list-source switch the unified row makes; a switch between two signed-in accounts needs real sessions (no sample path).
There is no import progress view yet (MailTransfer exposes IsRunning/Done/Summary; the folder-menu owner shows them).
