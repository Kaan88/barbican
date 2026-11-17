# Behaviours: smaller modules the catalogue streams left out

Added at the M0 review (stream m0-review). Modules: `spam.rs`, `plugins.rs`, `zip.rs`, `capture.rs`,
`pim.rs`, `search_ui.rs`, `invite.rs`. Rows cite `rust-final` (`bba9613`).

## Spam verdict (`src/spam.rs`), area `SPAM`, M2
| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-SPAM-001 | Port of Bulwark's spam-score plugin: the verdict comes from headers only. Score: `X-Spam-Status` `score=`, else `X-Spam-Score`, else `X-Rspamd-Score`, else the first number of `X-Spamd-Result [s / r]`; threshold from `required=` or the bracket's second number. | spam.rs:1, :29-37 | Bulwark plugins/spam-score | T-206 covers the native plugin, not this module |
| B-SPAM-002 | Spam when `X-Spam-Flag` is yes/true or the action (`X-Rspamd-Action` / `X-Spam-Action`) contains reject, add header, rewrite subject, quarantine or spam; else spam at score ≥ threshold, suspicious at ≥ 0.6 × threshold, clean otherwise. | spam.rs:38-60 | Bulwark spam-score | none |
| B-SPAM-003 | An `X-Spam-LLM` header starting LEGITIMATE/SPAM/SUSPICIOUS can only raise a clean verdict (to spam or suspicious), never lower one. No score, flag, action or LLM header: no verdict shown. | spam.rs:45-67 | Bulwark spam-score | none |
| B-SPAM-004 | The detail line reads "Score s / r threshold" (two decimals) or "Score s"; the badge on clean mail shows only when "Show for clean mail too" (`spam.showClean`, stored as `showCleanSpam` in the desktop settings file) is on. | spam.rs:68-72; settings.rs:382; main.rs:887 | | none |

## Native plugins (`src/plugins.rs`), area `PLUG`, M2 (host), M6 (settings page)
| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-PLUG-001 | Plugins are dynamic libraries in the app's `plugins` folder, loaded at start-up, with a C ABI: `barbican_plugin_manifest`, `barbican_plugin_call(hook, json)`, `barbican_plugin_free`. API version 1. | plugins.rs:1-25 | Bulwark plugin slots | T-206 |
| B-PLUG-002 | Hooks: `email.details` (rows under the sender, tone neutral/success/warning/danger, like Bulwark's `ui:email-details` slot), `compose.check` (a warning that holds the send once, like the attachment reminder), `settings` (settings rows in the schema's format). | plugins.rs:15-21, :107-125 | Bulwark plugin API | T-206 |
| B-PLUG-003 | A library without a manifest, with an empty manifest or with a duplicate id is skipped and reported; disabled plugins are not called. | plugins.rs:64-105 | | T-206 |
| B-PLUG-004 | C# port: the ABI is kept so the existing fixture (`tests/fixtures/plugins/spam-score`) still loads; the loader must not load from any other folder. | PORT-PLAN §2.2 | | T-206 (port) |

## ZIP of all attachments (`src/zip.rs`), area `ZIP`, M2
| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-ZIP-001 | "Download all attachments (.zip)": entries are STORED (no compression, like JSZip's default), names UTF-8 (flag bit 11), DOS timestamps (2 s resolution, 1980-2107). No ZIP64: under 4 GB and 65,535 entries. | zip.rs:1-5, :13-21, :37 | Bulwark handleDownloadAllAttachments | T-312 |
| B-ZIP-002 | Duplicate names are made unique in order the way Bulwark's createUniqueNamer does: the second `a.pdf` becomes `a (1).pdf`; an empty name takes the fallback ("attachment"). | zip.rs:22-35; reader.rs:833 | Bulwark createUniqueNamer | T-311 |
| B-ZIP-003 | The file is named `attachments_<subject>.zip` (`attachments.zip` without a subject), as Bulwark's attachmentsBundleFilename. The menu item shows in the reader menu when the message has attachments, and in the attachment menu as "Download all (.zip)" when it has more than one. | reader.rs:240-243, :990-996, :1071-1082 | Bulwark attachmentsBundleFilename | none |

## Window capture and PIM probes (`src/capture.rs`, `src/pim.rs`), area `CAP`, M0/M2 (automation)
| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-CAP-001 | Rust captured the window with `PrintWindow(PW_CLIENTONLY or PW_RENDERFULLCONTENT)` so an off-screen window and the WebView2 child are included; frames are RGBA top-down, optionally cropped and downscaled. Used by the flicker burst and to freeze the reader under native overlays. | capture.rs:1-26 | | none |
| B-CAP-002 | Superseded for C#: `PrintWindow` returns black for WinUI 3, so capture is Windows.Graphics.Capture (ADR 0007), and the under-overlay freeze is not ported on Windows (ADR 0005). On Linux the dialog/scrim freeze is still needed (ADR 0005). | ADR 0005, ADR 0007 | | spikes only |
| B-CAP-003 | PIM automation probes (contacts, files, filters, identities) answer one line of JSON and never change state. | pim.rs:1-7 | spec/automation-protocol.md | none |

## Search box (`src/search_ui.rs`), area `SRCH`, M2
| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-SRCH-001 | The dropdown shows recent searches and people as the text changes or the box gets focus; arrows move the highlight; Enter takes the highlighted suggestion over the typed text; Escape closes (module header; the Escape path was not re-read). | search_ui.rs:1-7, :26-89 | Bulwark search-box.tsx, search-suggestions.tsx | ported (M2 search: tests/Barbican.Presentation.Tests/Search, tests/Barbican.Core.Tests/Search) |
| B-SRCH-002 | One removable chip per query operator and per filter-panel field; "Clear all" from two chips (module header); removing a chip re-runs the search. | search_ui.rs:99-141 | Bulwark search-chips.tsx | ported (M2 search: tests/Barbican.Presentation.Tests/Search, tests/Barbican.Core.Tests/Search) |
| B-SRCH-003 | After a result page loads, `SearchSnippet/get` is asked for rows when the filter has a text term; rows with a `<mark>` show the highlighted subject and preview (accent colour), others keep the plain ones. A new list drops the old snippets. | search_ui.rs:143-200 | RFC 8621 5.1; Bulwark search-snippet.ts | ported (M2 search: tests/Barbican.Presentation.Tests/Search, tests/Barbican.Core.Tests/Search) |

## Invitations in mail (`src/invite.rs`), area `INV`, M4
| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-INV-001 | The calendar part is an attachment first, then a body part, of type text/calendar, application/ics or application/icalendar, or named `.ics`/`.ical`. It is read with `CalendarEvent/parse`. | invite.rs:25-46, :191 | Bulwark findCalendarAttachment; JMAP Calendars | T-122 |
| B-INV-002 | The iTIP method comes from the event, then the raw .ics `METHOD` (JMAP drops the MIME parameter), then a guess from the content (a reply-like event). | invite.rs:48-59, :100-116 | Bulwark getInvitationMethod; RFC 5546 | T-123 |
| B-INV-003 | Answering goes through the calendar copy: the stored event with the same uid, else the invitation is imported into the default calendar first; then the user's participant status is patched with `sendSchedulingMessages`, so the server sends the iTIP REPLY. | invite.rs:1-7, :172-243 | Bulwark calendar-rsvp.ts | T-127 |
| B-INV-004 | Trust: a warning when the sender differs from the organizer (or, for REPLY/COUNTER/REFRESH, from the answering attendee) and the message is not verified, or when authentication failed; a caution for a mismatch alone, or for an unverified invitation that is not internal mail. | invite.rs:246-273 `trust` | Bulwark getInvitationTrustAssessment | T-126 |
| B-INV-005 | Summary text: "Mon 17 Nov 2026, 10:00 – 11:00 (zone)"; UTC events in local time, other zones keep their wall clock and name the zone. | invite.rs:139-161 | | T-125 |

## Open questions
- B-SPAM-004: the setting lives in the assistant's desktop settings file (`showCleanSpam`) although it is a
  reader display rule; the C# settings model may move it. Needs a D- entry if it moves.
- B-CAP-003: the C# automation protocol (`spec/automation-protocol.md`) lists the probes it keeps.

## M1 status (stream body)

B-CAP-002 holds as written: Windows has no under-overlay freeze (`InPlaceBodyOverlay`); Linux has `X11BodyOverlay` (XGetImage of the root window,
refused for a rectangle outside the screen), written but not wired to a dialog in M1 and never run (VM unreachable): a Linux hand test.

## M1 status (verify)

Not M1 areas; settled so no row is silently open: B-SPAM-001..004 and B-PLUG-001..004: deferred(M2) with T-206 (the native spam-score plugin; the
plugin hosts are M6b). B-ZIP-001..003: deferred(M2) (T-311, T-312). B-SRCH-001..003: deferred(M2) (search, T-256..T-258).
B-CAP-001: ported differently (m1 F1: Windows.Graphics.Capture, ADR 0007; `PrintWindow` returns a black image for a WinUI window).
B-CAP-003: deferred(M4/M5), with the PIM probes.
