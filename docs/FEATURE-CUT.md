# Feature cut-down

Decided by the owner on 2026-11-25, in one review session. Barbican stays a functional Bulwark
clone, but only for the features the owner uses. Every feature cut now is UI the move to
markdown-shared-rs (`docs/UI-TOOLKIT-EVAL.md`) does not have to port.

- **Cut**: the parity rows are `wont-do` in `spec/parity.csv`, with the reason in `rust_call_site`.
  Made code is removed in follow-up work (below), not in this session.
- **Keep**: stays on the plan. Unmade parts are still to be built.
- **Later**: no decision yet; revisit after the UI rewrite. Rows stay open.
- Rows where only part was cut keep their status, and `rust_call_site` says which part goes.

Sizes: lines of Rust in `src/` and Slint in `ui/` for made code; S/M/L effort (ROADMAP.md) for
unmade work. "UI refs" counts Slint binding calls (`.set_*`, `.get_*`, `.on_*`, models, globals),
a rough measure of the porting work.

## Decisions

### Large units

| Unit | Status before | Size | Decision | Reason | Parity ids | Code that would go |
|---|---|---|---|---|---|---|
| Plugin platform | partial (B1-B4 part 1) | ~7,700 Rust; B4 rest, B5, B6, B7 unmade (L+L+L+M) | **Cut** | Not used; the biggest porting and maintenance cost. The AI assistant and the spam score stay as built-in features. | G-081 | `src/plugins.rs`, `plugin_host.rs`, `plugin_package.rs`, `plugin_hooks.rs`, `plugin_catalog.rs`, `plugin_catalog_check.rs`; `plugins/spam-score/`; `spec/plugins/`; `PLUGINS.md`; plugin wiring in `main.rs` (~136 refs), `compose.rs` (~98), `settings.rs` (~73) and the hooks in `mail.rs`, `msgload.rs`, `reader.rs`, `autosave.rs`, `avatars.rs`, `cachedata.rs`, `files_x.rs`, `livesync.rs`, `notices.rs`, `settings_sync.rs`, `signout.rs`; the plugin settings page in `ui/app.slint`. `msgload.rs` then always shows the built-in `spam::analyze` verdict. |
| Admin tab and dashboard | partial (read-mostly) | ~2,000 incl. `ui/dashboard.slint` | **Keep** read-only views and the Actions tab | Used. | - | - |
| Admin writes | missing | L: many new forms, each tested against a server that can safely break | **Cut** (final round) | The read-only views plus the Actions tab are enough; changes go through Stalwart's web admin. | G-080 | none (unmade) |
| AI assistant | done (not a Bulwark core feature) | ~800 Rust + ~50 refs in `ui/app.slint` | **Keep** (built in), ported as it is | Used; stays built in, not a plugin. The owner will supply a Thunderbird extension to model a later redesign on. | - | - |
| Files app | done, preview and zip partial | ~3,300 Rust + ~725 Slint, ~180 UI refs | **Keep** | Used, with the mail links to Files (attach from, link to, save to). Files keeps its own share flyout. | G-013, G-017, G-076, G-079 | - |
| Files sync, WebDAV, WOPI editing | missing | L, plus a sync engine to maintain and an Office server for WOPI | **Cut** (final round) | Not worth the cost. | G-083 | none (unmade) |
| Light renderer (Blitz) | done | ~80 lines, but the `blitz-*` dependency tree (Stylo, Taffy) | **Cut** (final round) | Slower builds and version bumps for a view not used. The Files preview of .eml and HTML files moves to WebView2. | part of G-013 | `src/lite.rs`, the `blitz-*` crates in `Cargo.toml`, the light mode in `src/main.rs` (around line 262) and `mailview.rs`, the "Show messages with" setting's light choice; `render_doc` in `src/files_view.rs` replaced by a WebView2 preview |
| Offline mode and outbox | missing | L | **Cut** | Not needed; Barbican needs a connection. | G-082 | none (unmade) |
| Language and time zone | missing | i18n L, picker S | **Cut** | English only; time zone and date format follow the OS. | G-060, G-071, G-072, S-timeZone, S-dateLocale, S-keyless-021; part of G-069 | none (unmade) |
| Delegation, impersonation, Pro interface, organisation announcements | missing | M + M | **Cut** | Not used. | G-084, G-086, S-proInterface | none (unmade) |
| App lock | made (row said missing) | ~1,760 Rust + 254 Slint, ~120 UI refs | **Cut** | The Windows lock screen covers it; one of the heaviest files to port. | G-031, S-keyless-015 | `src/applock.rs`, `ui/applock.slint`, the Security settings rows and the idle and session-timeout wiring in `main.rs` |
| Sharing folders, calendars, address books | done | ~750 Rust + 271 Slint | **Cut** | Not used. The Files share flyout (`src/files.rs`) stays. Share notices from others still show in the notification centre. | G-043 | `src/share.rs`, `ui/share.slint`, the Share entries in `src/ctxmenu.rs` and the settings pages |
| Several accounts and shared mailboxes | done; switcher and shared-account settings missing | ~1,200 (`unified.rs`, `copymsg.rs`) | **Keep all** | Used. | G-061, G-066 | - |
| In-app updater | unverified | ~1,300 (`updater.rs`, `release_manifest.rs`, `ui/update-banner.slint`) | **Keep** | Kept alongside Hub. | G-088 | - |
| Linux support | unverified, never compiled | platform layer, hunspell, packaging | **Later** | Decide after the UI rewrite. | G-007, G-090 | - |
| Packaging | unverified | Inno, MSI, portable zip, deb, AppImage | **Keep all** | All formats stay. | G-087 | - |
| Pop-out windows | missing | M, plus multi-window toolkit work | **Cut** | Single window. | G-055, S-desktopOpenOnDoubleClick, S-desktopComposeInWindow, S-desktopRestoreWindows | none (unmade) |
| Themes (built-in, accents, import) | missing | M | **Later** | Barbican gets its own look in the rewrite; decide then. | G-070 | - |
| Tour and onboarding | missing | M | **Cut** | Not needed. | G-085, S-keyless-020, S-showOnboardingOnNewDevices | none (unmade) |
| Mobile and web-only items | missing | S each | **Cut** | Desktop only: swipe actions, push relay, phone-width layout, freeing the mail view's memory in the background. | S-swipeLeftAction, S-swipeRightAction, S-pushRelayUrl, S-webviewBackgroundCloseMinutes; part of G-059 | none (unmade) |
| Printing | partial (one message prints) | small; calendar and contact print on branch `v05-print` | **Cut all** | Never used. Single-message print goes too; `v05-print` is dropped. | G-008, G-010, G-074; printTheme in G-065 | the print paths in `src/mailview.rs` and `src/platform/mod.rs`, the Print menu entries and shortcut; branch `v05-print` |
| Notification centre | partial | ~760 Rust + 132 Slint | **Keep** | Used. | G-045 | - |
| Account security and masked addresses | done; missing two-step and account rows | ~1,400 + ~380 | **Keep all** | Used; missing rows stay planned. | S-keyless-002, -006, -010, -011, -012 | - |
| Mail import and export (.eml, .mbox) | done | ~1,500 | **Keep** | | - | - |
| Settings sync | done | ~1,500 | **Keep** | | - | - |
| Mail trust extras: report phishing, BIMI, sender pictures, TNEF, Authentication-Results | done | ~3,300 together | **Keep all** | | - | - |

### Mail list, composer and reader

| Unit | Status before | Size | Decision | Reason | Parity ids | Code that would go |
|---|---|---|---|---|---|---|
| List as columns; message order and "apply to all folders" | missing | S | **Cut** | The list stays two-line rows, newest first. | G-062, S-listColumns, S-messageListOrderScope, S-keyless-039 | none (unmade) |
| Pinned messages (`$pinned`), hover tag picker | missing | part of G-073 | **Keep** | | G-073 (kept part) | - |
| Category tabs | missing | part of G-073 | **Cut** | Plugin-driven in Bulwark; no built-in categories wanted. | G-073 (cut part) | none (unmade) |
| Structured-mail cards (bookings, shipments, pkpass) | missing | part of G-073 | **Cut** | Not used. | G-073 (cut part) | none (unmade) |
| Composer: priority and classification, @mentions, proofreading | missing | S/M each | **Keep** | Proofreading becomes built in (it was plugin-based in Bulwark). | G-075, G-077, G-078, G-063, S-recipientMentionsEnabled, S-mentionAddsRecipientTo, S-keyless-040 | - |
| Composer: right-to-left editing | missing | S | **Cut** | | S-rtlEditingSupport | none (unmade) |
| Rich signature editor | partial (HTML source editor) | M | **Later** | | G-024 | - |
| Command palette and Search everything, schedule send, read receipts | done or unverified | ~2,100 | **Keep all** | | G-095, G-103, G-104 | - |
| At-a-glance mail header | done (Barbican's own) | ~260 + UI | **Keep** | | - | - |
| Mail layout: reading pane position, toolbar position, Compose placement | missing / unverified | S each | **Keep** | | S-mailLayout, S-toolbarPosition, S-composePlacement | - |

### Calendar and contacts

| Unit | Status before | Size | Decision | Reason | Parity ids | Code that would go |
|---|---|---|---|---|---|---|
| Tasks, webcal and .ics import, birthdays, agenda | done / unverified | most of `cal_extra.rs` (~1,200) and `ui/calendar_ext.slint` | **Keep** | | G-106 (kept part) | - |
| Find a time ("Suggest times") | partial | part of `src/cal_editor.rs` | **Cut** | Not used, and Stalwart's availability support is uncertain. | G-009; part of G-106 | Suggest times and the `Principal/getAvailability` lookup in `src/cal_editor.rs` (around line 1227) and its UI |
| Contacts: merge duplicates, vCard import/export, directory | done | ~1,450 | **Keep** | | G-107 | - |
| Contact activity and related people | missing | S/M | **Later** | | G-011 | - |
| Templates; out of office extras | done; extras partial | ~900; extras S/M | **Keep both** | | G-021 | - |
| Filters (Sieve editor) and quick rules | done | ~2,200 + 219 Slint | **Keep both** | | - | - |

### Settings and desktop

| Unit | Decision | Parity ids |
|---|---|---|
| Tray with "keep running in the tray", start at login, the link handling rows ("open email/calendar links in Barbican", "how links open") | **Keep** | S-keepRunningInTray, G-089, S-keyless-022, -023, -024; protocolOpenMode in G-068 |
| "Turn on for this device" (per-PC notification switch) | **Keep** | S-keyless-019 |
| Default reply mode, "Calendar opens in" | **Keep** | defaultReplyMode in G-065, S-keyless-049 |
| Attachment reminder keywords, sidebar apps | **Cut** | G-067 |
| Hide company calendars, download name templates, detailed logging switch | **Cut** | hiddenCompanyCalendars in G-065, download templates in G-068, S-debugMode |
| "Organize invitations as" (organiser identity) | **Cut**: the organiser stays the login address | S-keyless-050 |
| Keep apps loaded (web tab memory) | **Cut** | S-keepAppsLoaded; part of G-061 |
| Font pass side by side with Bulwark | **Cut**: Barbican no longer copies Bulwark's look | G-092 |

Core mail, calendar and contacts (reading, writing, folders, events, cards, reminders,
invitations, live updates, search, shortcuts, accessibility settings) were not up for review and
stay.

### Final round: costly but useful

The owner weighed these last, with cost against worth: Files sync and WOPI (cut), admin writes
(cut), offline mode and outbox (stays cut), pop-out windows (stay cut), the AI assistant (kept),
the in-app updater (kept), the Blitz light renderer (cut), BIMI badges (kept).

## Effect on `spec/parity.csv`

41 rows moved to `wont-do` (42 with the earlier passkey row). Ten rows where only part was cut, or
that a cut touches, carry a note in `rust_call_site`: G-013, G-045, G-059, G-061, G-063, G-065,
G-068, G-069, G-073, G-106. Open rows now: 32 missing, 18 partial, 24 unverified.

## Follow-up removals, by what they save

Removal is separate work, one branch per item, each with `cargo test` and the privacy check.

1. **Plugin platform.** ~7,700 lines, plus 3-4 months of unbuilt work (B4 rest to B7) and the
   slot renderer the new UI would have needed. Keep `src/spam.rs` and show its verdict always
   (drop the `spam_plugin` switch in `msgload.rs`). Check that `ai.rs` and `agent.rs` don't depend on
   plugin code (they don't today). Remove the plugin settings page, the consent dialog, the plugin
   secrets and storage handling in `signout.rs` and `settings_sync.rs`, and the plugin column of
   the catalogue docs.
2. **App lock.** ~2,000 lines and ~120 UI refs, among the heaviest to port. Remove the lock
   settings rows, idle tracking and session timeout wiring; check sign-out and start-up paths that
   ask whether the app is locked.
3. **Sharing (folders, calendars, address books).** ~1,000 lines. Keep the Files share flyout in
   `src/files.rs`. Remove the Share entries from the context menus and settings pages.
4. **Printing.** Small in code, but it removes the print paths from the WebView shell and the
   platform layer. Delete branch `v05-print` (owner's call).
5. **Blitz light renderer.** Little code, but four pinned beta crates and their dependency tree
   leave the build. Needs a WebView2 preview for .eml and HTML files in Files first.
6. **Find a time.** A few hundred lines in `src/cal_editor.rs` and the event editor UI.

Unmade work no longer planned (saved effort, nothing to delete): Files sync and WOPI (L), admin
writes (L), offline mode and outbox (L), i18n (L), delegation and Pro (M + M), pop-out windows (M, plus multi-window support in the toolkit),
tour (M), category tabs and structured-mail cards (M), time zone picker, list columns and order,
RTL editing, swipe, push relay and the cut settings (S each).

Also to update after removal: ROADMAP.md (plugin track B, Phase 7 items 1, 2, 4, the print step and
the shell pop-outs), GAPS.md and CHANGELOG.md.
