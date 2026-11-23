# Barbican roadmap

**Goal: 1:1 feature parity with Bulwark webmail 1.21.2, including its plugin system.** A Bulwark plugin ZIP should install and run in Barbican unchanged. Our own native (C ABI) plugins stay, using the same hook and slot names.

Updated 2026-11-23, after the C# port was dropped (PORT-PLAN-DROPPED.md).

**Inputs:**
- GAPS.md: Bulwark gaps, as P0–P3.
- CSHARP-LESSONS.md: what the C# port leaves us.
- docs/from-csharp/: Bulwark-referenced specs, the plugin catalogue and the bridge scripts.

**A row is done** when it works end to end in the real app, matches Bulwark's behaviour (cite the Bulwark file:line), and is checked through the hidden automation channel. Server writes are checked live only on the test account.

Sizes: S = up to 2 days, M = up to a week, L = several weeks. Two tracks run side by side:
- **A** builds the features.
- **B** builds the plugin platform. B reaches a slot or hook only once A has built the screen behind it.

---

## Phase 0: foundations (about 1 week, do first)

The repository is public, and every later phase builds on these.

1. **Privacy pre-push hook (S).** Adapt `docs/from-csharp/tools/privacy-check.sh`. The patterns stay in the gitignored `.local/`.
2. **Mail frame hardening (S).**
   - Add `base-uri 'none'` to the frame policy.
   - Strip meta refresh.
   - Filter navigation in wry so only `about:srcdoc` loads.
   - Fix the named-element shadowing bug (C# R2-04).
   - Decide on `allow-scripts` for the dark and accessibility scripts. C# D-071 says never; run them from the shell instead.
3. **Sign-in and session safety (S).**
   - Follow redirects by hand: credentials go only to same-origin hops, with no switch from https to http (`src/jmap.rs:72`).
   - Stop retry loops after a rejected sign-in.
   - Two instances on one profile must not lose a rotated refresh token.
4. **Credentials (S).** Keyring entries named per profile (`BARBICAN_PROFILE`), local persistence for tokens (C# ADR 0008), and dev and automation runs kept away from the real sign-in.
5. **Sign-out cleanup (S).** Remove per-account caches, trusted senders, WebView data and plugin data.
6. **Native plugin safety (S).**
   - User plugins are off until enabled.
   - Each hook call runs on a worker thread with a time budget.
   - `compose.check` fails closed.
7. **Plugin catalogue in Rust (S).**
   - Generate `src/plugin_catalog.rs` (hook, slot and host-API names, kinds, permissions, budgets) from `docs/from-csharp/spec/plugins/*.csv`.
   - Move `docs/from-csharp/spec/plugins/` to `spec/plugins/` and add a Rust call-site column.
   - Old hook names become aliases.
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

**Settings sync first.** Settings Bulwark keeps in mailbox metadata go there too, so Bulwark and Barbican share them. The C# choice to keep identity order and pinned folders under its own keys is **not** followed.

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

## Phase 5, track B: the plugin platform (8–10 weeks, starts after Phase 0)

Bulwark's model:
- **Package:** a ZIP holding `manifest.json` and `index.js`.
- **Hooks:** 218, of four kinds (observer, intercept, transform, takeover).
- **Slots:** 58, of three kinds (frame, action, data).
- **Host API:** 186 methods called by postMessage RPC.
- **Sandbox:** null-origin iframes.
- **Permissions:** about 68.
- **Storage:** per login.

The C# port showed that the unmodified Bulwark runtime runs inside a desktop web view.

| Step | Work | Size |
|---|---|---|
| B1 Spike | One wry web view per plugin frame, placed over Slint rectangles: background frame, z-order, resize, focus, teardown. **Risk check:** at most two frames starting at once; never move a live web view to another parent (C# lesson). | M |
| B2 Runtime and bridge | Build Bulwark's sandbox runtime from the pinned commit (`docs/from-csharp/tools/plugin-runtime-build/`). Port the parent page and bridge scripts and their Node tests. On the Rust side: message dispatch with source and origin checks, size caps, reply IDs used only once, and teardown when navigation starts. | M |
| B3 Packages and consent | ZIP inspection (size, entry count, unsafe paths). Manifest checks, including `minAppVersion`. A consent dialog listing the permissions, web origins and POST paths. A hash-pinned record that starts disabled. Spec: `M6b-package-authority.md`. | M |
| B4 Core host API | `storage` (per login, 1 MiB), `ui` (toasts, dialogs, openDialog), `http` fetch (declared origins only, every redirect checked, private addresses refused), `account`/`user` read access, settings schema. Spec: `M6b-api-inventory.md`, `M6b-runtime-transport.md`. | L |
| B5 Hooks | Fire the 218 hooks from the Rust event points, area by area, as track A builds each area. Each hook has its time budget. Send hooks fail closed. Re-run the send checks when a transform changes the recipients. A plugin that keeps failing is turned off. The native C-ABI plugins get the same hooks. | L (spread out) |
| B6 Slots | Frame slots in the reader, composer, sidebar, calendar, settings and admin. Action slots: menus and toolbars drawn by the host. Data slots: search results, day labels, calendar marks. Props are filtered by permission and pushed as updates without restarting the frame. Placement table: `M6b-plugin-slots.md`. | L |
| B7 Remaining host API | The rest of the 186 methods: `email.*`, `calendar.*`, `contacts.*`, `files.*`, `tabs.*`, `search.*`, `compose.*`, `frame-escape`. | L |
| B8 Server-side services | Bulwark runs a secrets proxy, plugin OAuth, admin approval and marketplace on its server. Barbican must provide them locally: secrets in the OS keyring scoped per plugin, OAuth through a loopback redirect, and a managed tier with Ed25519-signed bundles. **Needs a design decision** (see the open decisions below). | M–L |
| B9 Conformance | Bulwark's `integration/fixtures/plugin-api-probe`, the proofreading plugin and the spam-score plugin run unchanged. The catalogue status reads `done` for every hook and slot. | M |

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

- Run CI for the first time: MSI, portable zip, .deb, AppImage.
- Run the updater end to end, and add the started-marker rollback (C# ADR 0009).
- A local signed daily channel for the user's own build.
- First Linux compile: WebKitGTK, Secret Service, the portal colour scheme, mailto handling.

---

## Open decisions

1. **Server-side plugin services (B8).** Do secrets, OAuth and admin-managed plugins run locally in Barbican, or through a Bulwark server when there is one?
2. **Frame sandbox.** Bulwark gives plugin frames `allow-forms` as well as scripts. Do we match it, or allow scripts only?
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
