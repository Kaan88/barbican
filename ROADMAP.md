# Barbican roadmap

**Goal: 1:1 feature parity with Bulwark webmail 1.21.2, including a plugin system with a comparable API.** Barbican has its own native plugin system (C ABI, no web runtime). Bulwark's hooks, slots and host API are the reference vocabulary, so a Bulwark plugin can be ported by hand. Running Bulwark plugins unchanged is **not** a goal (decided 2026-11-23: a web-based plugin runtime is too much complexity).

Updated 2026-11-25 (order from here set by the owner); 2026-11-23 (0.1.0 released, schedule added), after the C# port was dropped (PORT-PLAN-DROPPED.md).

**Inputs:**
- GAPS.md: Bulwark gaps, as P0–P3.
- CSHARP-LESSONS.md: what the C# port leaves us.
- docs/from-csharp/: Bulwark-referenced specs and the plugin catalogue (the web plugin bridge there is archived; we do not use it).

**A row is done** when it works end to end in the real app, matches Bulwark's behaviour (cite the Bulwark file:line), and is checked through the hidden automation channel. Server writes are checked live only on the test account.

Sizes: S = up to 2 days, M = up to a week, L = several weeks. Two tracks run side by side:
- **A** builds the features.
- **B** builds the plugin platform. B reaches a slot or hook only once A has built the screen behind it.

---

## Order from here (owner's decision, 2026-11-25)

This order replaces the release table below wherever they disagree. Phases 0-4 and plugin steps B1-B3 are merged.

1. **Finish what is in flight, then stop the plugin track.** B4 part 1 (mail and composer hooks) and the notification tone plus "Refresh cached data" (S-notificationSoundChoice, S-keyless-063) are merged. No new plugin work until step 3. Calendar and contact print (G-008, G-010) is parked at step 6.
2. **UI push (track A, the main work).** In this order:
   1. **Jank, everywhere.** The audit is done: docs/JANK-AUDIT.md (ranked issues and work batches; do batch B, the settings cache, first, then A, the WebView, overlays and focus). Start from an audit that ranks every problem found in these four areas:
      - **Slow:** work on the UI thread, folder/app/message switches, scrolling long lists, freezes while loading.
      - **Layout glitches:** jumps, overlaps, misalignment, flicker and size changes while content loads.
      - **Focus and keyboard:** focus returning after every overlay, shortcuts working without a click, Escape and Enter behaving the same in every dialog.
      - **Looks off against Bulwark:** spacing, sizes, icons, colours.
      Fix them worst first.
   2. **Mail layout:**
      - `mailLayout` (reading pane right or off, S-mailLayout; bottom cut 2026-11-25);
      - `toolbarPosition` (S-toolbarPosition);
      - list columns (S-listColumns, G-062);
      - density checked against Bulwark;
      - the sidebar account switcher and shared accounts rows (G-061).
   3. **Visual polish:**
      - **Decided (2026-11-25):** Bulwark's look becomes the default theme. The current Bento × Nordic look stays as a built-in alternative. Both get fixed: contrast, the selection, hover, pressed, destructive, success and warning tokens, and one type scale.
      - merge the Bulwark palette and tuned contrast tokens (`docs/from-csharp/design/legacy-slint/theme.slint`, G-070 part 1);
      - the font pass (G-092);
      - icons and density side by side with Bulwark.
   4. **Shell:**
      - a sidebar that folds to icons;
      - a phone-width layout;
      - pop-out reader and composer windows (G-055);
      - sidebar apps.
   5. **Mail list and composer extras:**
      - list: tabs, hover tag picker, `$pinned`, structured-mail cards (G-073);
      - composer: @mentions, attach from Files, priority and classification, right-to-left blocks (G-075 to G-079).
      Then Bulwark's built-in themes, theme import and the tour (G-070, G-085).
3. **Plugin track resumes:** the rest of B4, then B5 slots on the finished screens, B6 host calls, B7 ported proof plugins.
4. **Phase 7 large areas**, in their value order: offline and outbox, i18n and time zone, admin writes, delegation and Pro, Files sync and WOPI.
5. **Last: the settings that show but do nothing** (G-059 to G-069, the desktop window options, swipe, push relay). Each one is wired, or marked `wont-do` with a reason (for example, the mobile-only swipe actions and the web push relay).
6. **Printing, after everything else (owner, 2026-11-25: "I never use it").** Calendar and contact print sits unmerged on branch v05-print (round 2 review: two medium fixes left, a narrow-window week print loses days and a search left over from another view hijacks the Tasks print; plus two low ones). Conversation print (G-074) comes after it.

## Schedule

Week 0 is the week of 2026-11-23. Estimates are rough and assume one coordinator, one worker and one reviewer running the branch loop (worker edits in a worktree, Opus reviews, the coordinator merges). A phase starts when the one before it in the same track is merged.

**Status at 0.1.0:** Phase 0 items 1, 2, 6, 7 and 8 are merged (privacy hook, mail frame, plugin safety, plugin catalogue, `spec/parity.csv`). Items 3, 4 and 5 are not. The Inno installer, Hub release files and changelog-driven release workflow are in. Not yet done anywhere: the installer has never been run, the updater has never run end to end, there is no LICENSE, and the Hub catalog entry is an open PR.

| Release | Weeks | Contents | Done when |
|---|---|---|---|
| **0.1.1** | 1 | **First: remember the window** (size, position, maximised state, and the monitor it was on; restore on start, fall back to the default when the saved rectangle is off-screen). Then the Gravatar crash (turning `gravatarAvatars` on crashes, and the app then crashes on every start until the pref is reset; the app should also survive a pref that crashes it). Then Phase 0 items 3, 4, 5 (sign-in safety, per-profile credentials, sign-out cleanup). Dependabot alert. LICENSE decision. Hub catalog entry merged. Installer and updater run once by the owner on a VM. | The window reopens where it was; Gravatars can be switched on without a crash; fresh install, sign-in, sign-out, upgrade 0.1.0 to 0.1.1 and uninstall all work on a clean Windows VM; Hub offers the update. |
| **0.2** | 2-4 | Phase 1 (P0 leftovers). Phase 2 (reader safety and mail moves). B1: plugin API v2 design, the `barbican_status` column filled for all 462 catalogue rows. | Every Phase 1 row and every Phase 2 row is `done` in `spec/parity.csv`; PLUGINS.md describes API v2. |
| **0.3** | 5-8 | Phase 3 (server settings and account, settings sync first). B2 manifest, permissions, consent, package format. B3 core host API (storage, settings schema, toast, dialog, fetch). | Barbican's settings follow the account to another PC (its own namespace in Stalwart mailbox metadata), and templates and shared tags are shared with Bulwark; a plugin can be installed, shown a consent dialog and given storage. |
| **0.4** | 9-11 | Phase 4 (Files depth). B4 hooks for mail and composer, send hooks fail closed. B7 first proof: the spam-score plugin ported. | Files rows `done`; the spam-score plugin runs through the public API only. |
| **0.5** | 12-17 | Phase 6 (shell, list, composer, printing, the unwired settings, themes). B5 declarative slots on those screens. B6 `email.*`, `compose.*`, `tabs.*`, `search.*` host calls. | The roughly 40 dead settings are wired or labelled; a plugin adds a reader slot and a list tab. |
| **0.6** | 18-21 | B6 `calendar.*`, `contacts.*`, `files.*`. B7 second proof plugin (slot plus storage) and the porting guide. Phase 7 item 1: offline mode and outbox. | Two ported plugins; outbox sends exactly once in the kill-the-app test. |
| **1.0** | 22-28 | Phase 7 items 2-5 (i18n and time zone, admin writes, delegation and Pro, Files sync and WOPI). Plugin sandbox decision made and recorded. Track C: Linux build and updater rollback marker. | `spec/parity.csv` has no open row except those marked `wont-do` with a reason; clean-VM install, upgrade and uninstall pass; Linux package installs and signs in. |

**Rules that gate every release:**
1. `cargo test` on `main` and `bash tools/test-privacy-check.sh` pass; `python -I tools/parity-check.py` is OK.
2. Every row moved to `done` cites the Bulwark file:line and the Rust call site.
3. The owner runs the installer on a VM before the tag (no installers on the development PC).
4. The changelog has the version section before the tag; the push goes through the privacy hook.

**Parallelism:** track A and track B run side by side, but never two writers on the same files, and only one cargo command at a time on this machine. B4-B6 follow the screens track A has built.

**Decisions needed by week:** LICENSE (week 1); installer decisions from the 0.1.0 handoff (week 1); Linux builds on or off for releases (week 4); plugin distribution format (before 0.3); sandbox (before 1.0).

## Phase 0: foundations (about 1 week, do first)

The repository is public, and every later phase builds on these.

1. **Privacy pre-push hook (S).** Adapt `docs/from-csharp/tools/privacy-check.sh`. The patterns stay in the gitignored `.local/`.
2. **Mail frame hardening (S).**
   - Add `base-uri 'none'` to the frame policy.
   - Strip meta refresh.
   - Filter navigation in wry so only `about:srcdoc` loads.
   - Fix the named-element shadowing bug (C# R2-04).
   - Decide on `allow-scripts` for the dark and accessibility scripts. C# D-071 says never; run them from the shell instead.
3. **Sign-in and session safety (S). Done on `v011-signin`.**
   - Follow redirects by hand: credentials go only to same-origin hops, with no switch from https to http (`src/jmap.rs:72`).
   - Stop retry loops after a rejected sign-in.
   - Two instances on one profile must not lose a rotated refresh token.
4. **Credentials (S). Done on `v011-signin`.** Keyring entries named per profile (`BARBICAN_PROFILE`), local persistence for tokens (C# ADR 0008), and dev and automation runs kept away from the real sign-in.
5. **Sign-out cleanup (S).** Remove per-account caches, trusted senders, WebView data and plugin data.
6. **Native plugin safety (S). Done on `p0-plugins`.**
   - User plugins are off until enabled (`enabledPlugins`; the manifest is read from a sidecar `manifest.json`, so nothing is loaded before that).
   - Each hook call runs on a worker thread with a time budget.
   - `compose.check` fails closed.
   - A plugin that fails 3 times in a row is switched off for the session and the user is told.
7. **Plugin catalogue in Rust (S). Done on `p0-plugins`.** (Bulwark's names are our reference vocabulary, not a compatibility promise.)
   - `src/plugin_catalog.rs` (hook, slot and host-API names, kinds, permissions, budgets) is generated by `tools/gen-plugin-catalog.py` from `spec/plugins/*.csv`.
   - `spec/plugins/` has a `rust_call_site` column and a `phase` column.
   - Old hook names are aliases (`email.details` → `onEmailOpen`, `compose.check` → `onBeforeEmailSend`).
8. **Tracking (S).** One parity table, `spec/parity.csv`, built from GAPS.md and the C# `gaps.csv` and `settings.csv`. Each row gets an ID, a Bulwark reference, a status and the Rust call site. GAPS.md stays as the narrative.

## Phase 1: P0 leftovers (about 1 week; all S, specs in the C# decisions)

| Item | Spec |
|---|---|
| Apply a counter-proposal; remove a cancelled event from the banner | D-M4-10, Bulwark `lib/calendar-cancellation.ts` |
| All-day strip capped at 3 rows with "show more" | D-M4-08 |
| .ics import asks before keeping the file's alerts | D-M4-09 |
| An event's own alert more than 8 days ahead | D-M4-07 |
| Webcal: block localhost and LAN addresses | D-M4-11 |
| Filters: offer "reject" only when the server lists it | GAPS P1-4 |
| Refetch a truncated body in full; show quota on the Account page | C# G-056, G-057 |
| Re-check `cid:` images in a re-opened draft | GAPS P1-14 |
| Contacts data bugs: the `units` list lost on save; the 29 February birthday | D-M5-A06, D-M5-A07 |
| vCard limits on untrusted files | D-M5-03, D-M5-A03, D-M5-A04 |
| Mailto on Linux | GAPS P0-5 (needs a Linux build) |

## Phase 2, track A: reader safety and mail moves (2–3 weeks)

- **Per-sender trust (M):** `trustedSenders` and `externalContentPolicy`.
- **External-sender chip (S).**
- **Ask before sending to external recipients (S).**
- **Phishing report (M):** report to the policy address (ARF or forward), move to Junk with `$phishing`, turn the links off. Bulwark has no link-mismatch warning, so we don't add one.
- **BIMI verified badge (M)**, with certificate checks.
- **Trust headers (S):** refuse a forged Authentication-Results header that arrived through submission (C# R3-06).
- **Mail moves:**
  - copy to another account with `Email/copy` (M);
  - export a folder as mbox or .eml (S);
  - decode winmail.dat (S).
- **Reader extras:** retention banner and renaming an attachment (S).

## Phase 3, track A: server settings and account (3–4 weeks)

**Settings sync first.** Correction (2026-11-24): Bulwark does not keep its settings on Stalwart. It stores them as an encrypted blob on its own web server (lib/settings-sync.ts, app/api/settings/route.ts). Only templates and shared tags live in JMAP mailbox metadata (lib/jmap/bulwark-metadata.ts, namespace `org.bulwarkmail.*`). Owner decision: Barbican syncs its own settings in mailbox metadata under its own namespace, so they follow the account between PCs but are not shared with Bulwark. Templates and shared tags use Bulwark's namespaces, so those are shared. The C# choice to keep identity order and pinned folders under its own keys is **not** followed.

- **Identities and signatures (M):**
  - create and delete an identity;
  - Bcc;
  - rich signatures with a sanitiser;
  - identity order (`preferredIdentityIds`).
- **Templates (S/M):** editing, rich body, filling placeholders, saving a draft as a template.
- **Vacation (S/M):**
  - choosing who gets the reply;
  - forwarding while away;
  - keeping mail in the Inbox;
  - an HTML body;
  - templates;
  - blocking out the calendar.
- **Folders (M):**
  - subscribe;
  - roles;
  - icons;
  - count modes;
  - pin;
  - mark the whole tree read;
  - mute notifications per folder.
- **Sharing (M):** folders, calendars and address books, not only Files.
- **Account security (M):**
  - change password;
  - TOTP;
  - app passwords;
  - API keys;
  - encryption at rest.

  Then masked addresses (S) and app lock with session timeout (M).
- **Live updates outside mail, and the notification centre (M):**
  - push and `/changes` for calendars, contacts, files and identities;
  - `ShareNotification` and `CalendarEventNotification`;
  - the bell in the rail.

## Phase 4, track A: Files depth (2 weeks)

Port the C# pure-logic parts first (`FileTransfer.Zip`/`Upload`, the `FileNames` rules). Then build:
- an in-app preview of images, text and .eml (no PDF, D-M5-15);
- a details panel;
- a grid and list view, with sort, search and thumbnails;
- an upload conflict dialog that defaults to Keep both;
- zip download;
- a new text file;
- the filename download rules.

## Phase 5, track B: our own plugin system (5–7 weeks, starts after Phase 0)

**Decision (2026-11-23):** no Bulwark plugin compatibility and no web or JavaScript plugin runtime. Plugins are native (C ABI, as today, `src/plugins.rs`). Barbican offers comparable hooks, slots and host calls under Bulwark's names where that is sensible, so porting a Bulwark plugin means rewriting its logic, not reinventing its API.

What Bulwark offers, as the reference (`spec/plugins/*.csv`):
- 218 hooks of four kinds: observer, intercept, transform, takeover.
- 58 slots: frame, action and data.
- 186 host methods, about 68 permissions, per-login storage.

What we change: no iframes and no postMessage. A plugin cannot draw its own UI. Instead a slot takes a **declarative description** (rows, buttons, lists, forms as JSON, like the settings schema today) that the host draws in Slint. That keeps plugins simple and the look consistent.

| Step | Work | Size |
|---|---|---|
| B1 API design | For each row of the three catalogue CSVs decide adopt, adapt or skip, and write the result into a `barbican_status` column. Define plugin API v2: JSON over the C ABI, plus a table of host functions given to the plugin at load. Document it in PLUGINS.md. | S/M |
| B2 Manifest, permissions, consent | A manifest (sidecar file, already read by the Phase 0 work) with the permissions the plugin needs. A consent dialog listing them. A hash-pinned record per plugin that starts disabled. Package format: a folder or a ZIP with size, entry-count and path checks. | M |
| B3 Host API, core | Per-login storage with a quota, settings schema, toast and dialog, HTTP fetch limited to origins the manifest declares (every redirect checked, private addresses refused), read access to the account. | M |
| B4 Hooks | Fire the adopted hooks from the Rust event points, area by area, as track A builds each area. Each hook has a time budget. Send hooks fail closed. Send checks run again when a transform changes the recipients. | L (spread out) |
| B5 Declarative slots | Reader, composer, sidebar, calendar, settings and admin slots, rendered from plugin data and filtered by permission. Action slots (menus, toolbars) and data slots (search results, day labels, calendar marks). | L |
| B6 Rest of the host API | `email.*`, `calendar.*`, `contacts.*`, `files.*`, `tabs.*`, `search.*`, `compose.*`, in the order the screens exist. | L |
| B7 Proof | Port the spam-score plugin, and one plugin that uses a slot and storage (for example the proofreading one), as the test of the API. Write a short porting guide from Bulwark's API to ours. | M |

Risk: native plugins run in the app's process with its rights, so enabling one is a trust decision. A later step can add a sandbox (see the open decisions).

## Phase 6, track A: shell, list and composer parity (4–6 weeks)

Many of the plugin slots live on these screens.

- **Shell:**
  - pop-out reader and composer windows (M);
  - phone layout and a sidebar that folds to icons (M);
  - sidebar apps (S);
  - protocol open mode (S);
  - the tour (S).
- **Mail list:**
  - category tabs, which are plugin-driven (`tabs.set`);
  - list columns;
  - the hover tag picker;
  - `$pinned`;
  - structured-mail cards: bookings, shipments, pkpass;
  - swipe;
  - `mailLayout` (reading pane right, bottom or off);
  - `toolbarPosition`.
- **Composer:**
  - @mentions;
  - right-to-left blocks;
  - attach from Files;
  - priority;
  - classification label;
  - next and previous proofreading issue (proofreading plugins);
  - an organisation disclaimer.
- **Printing:** conversation, contact and calendar print, plus print settings.
- **The roughly 40 settings that show but are read nowhere (GAPS P2-1):** wire each one, or label it as not yet working (C# D-M6-A03).
- **Themes:**
  - merge the Bulwark palette and tuned contrast tokens from `docs/from-csharp/design/legacy-slint/theme.slint`;
  - then Bulwark's built-in themes;
  - then theme import.

## Phase 7, track A: large areas (L each, ordered by value)

1. Offline mode and outbox: send exactly once, keyed by Message-ID. A 5xx response means the outcome is unknown, so never re-send blind.
2. Language (i18n) and a time zone picker.
3. Admin writes: accounts, groups, lists, domains with DKIM and DNS, API keys, OAuth clients, the settings editor, queue retry. None of these are tested against the live server.
4. Delegation and impersonation; the Pro interface; organisation announcements.
5. Files sync, WebDAV and WOPI editing.

## Track C: distribution (alongside, when a release is wanted)

- Run CI for the first time: Inno installer, portable zip, .deb, AppImage. The MSI is on hold (built only with the repository variable `BUILD_MSI` = true). `packaging/inno/README.md` and Hub's `docs/COMPLIANCE.md` say what a release must carry; `CHANGELOG.md` drives the notes.
- Run the updater end to end, and add the started-marker rollback (C# ADR 0009).
- A local signed daily channel for the user's own build.
- First Linux compile: WebKitGTK, Secret Service, the portal colour scheme, mailto handling.

---

## Open decisions

1. **Plugin sandbox.** Native plugins run with full trust. Is that acceptable, or do we later move to a sandboxed format (for example WebAssembly) with the same API? Not needed to start; the API in B1 should not rule it out.
2. **Plugin distribution.** Loose folders and ZIPs only, or also a signed bundle format and a catalogue of plugins?
3. **JMAP extensions.** Several features use Stalwart-only `x:` extensions (account security, masked addresses). Hide them on other servers, as Bulwark does.

## Polish backlog (from earlier passes)

- **Design:**
  - some menus and composer buttons still use Segoe Fluent glyphs;
  - two address books both named "Contacts" cannot be told apart;
  - narrow-window squeeze in the calendar title and the Bento Tags tile;
  - views not yet seen with real data: contact detail, file sizes, invitation chips, the admin Queue.
- **Rich-text editor:** IME not checked with a real IME; the pre-edit text covers the text after the caret.
- **Motion:**
  - inserted and removed list rows, and the selection highlight, do not animate;
  - context panel and hover overlays snap;
  - `Settings::load` and `A11y::load` still run on the UI thread;
  - dashboard chart models are replaced whole;
  - the calendar Tasks view hides the view switcher.
- **Fonts:** side-by-side check of font sizes and weights against Bulwark, done through the hidden automation channel.

## Done (summary)

Details are in git history and earlier versions of this file.
- Bento × Nordic design language.
- Native rich-text composer, with spelling, tables and images.
- Motion pass.
- Context menus.
- Parity round 2: recipients, schedule send, autosave, reader actions, invitations, event editor and reminders, mailto, drag and drop, command palette and search, shortcuts, calendar and contacts depth, filters, .eml/.mbox import.
- Unified inbox; accessibility page.
- Sieve extras.
- Tray and toasts on Windows.
- A hardened updater (not yet run end to end).
