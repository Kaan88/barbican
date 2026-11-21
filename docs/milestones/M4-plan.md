# M4 plan: calendar and tasks

- Written 2026-11-18 by the M4 plan stage (PORT-PLAN.md §6.1 step 1), on branch `m4-plan`, taken from `m4` (finished M3).
- Scope and exit: [PORT-PLAN.md](../PORT-PLAN.md) §5 "M4" (line 753), §3.4 (push types for calendars), Appendix A/B/C rows for M4; the model for this
  plan's shape: [M3-plan.md](M3-plan.md) (and [M2-plan.md](M2-plan.md)).
- Integration branch: **`m4`**, created from `m4-plan` once this plan is pushed (the plan commit set carries the contracts, the FakeJmap calendar
  arms and the red tests, so `m4` starts with them). `m3` is merged into `m4` by the integrator whenever `m3` changes (never a rebase, never a
  force-push). Nothing merges to `main` and nothing is tagged until the user says so.
- Accounts and paths are placeholders (CLAUDE.md, `.local/accounts.md`): `<readonly-account>`, `<test-account>`, `<server>`, `<rust-repo>`,
  `<bulwark>`, `<stalwart-zip>`, `<downloads>`.
- **Process (user decision 2026-11-18):** no review stages, no hand-test gates, no questions back to the user. Every open decision in this plan is
  taken with the default that follows Bulwark and is listed in §9 and in `spec/decisions.md` ("M4 decisions", D-M4-01..16). What only a human can do
  (drag with a real pointer, a reminder notification on the real desktop, printing on paper) is written down as "unproven, needs a human" in the
  milestone report; it never blocks a merge. Agents are Sonnet (Haiku for mechanical work); each brief in §6 is written so one Sonnet agent can do it alone.

## 0. Summary

| Key (branch `m4-<key>`) | Letter | Scope | Model | T rows | Red tests | Wave |
|---|---|---|---|---|---|---|
| `cal-data` | A | Calendar store (Calendar/get, CalendarEvent query and get in chunks, shared accounts, namespaced ids), live updates (push and /changes for Calendar and CalendarEvent), the recurrence engine and time zones (NodaTime, Ical.Net as the differential oracle), occurrences, reminders (rules, scheduler across logins, state, missed list, popup view model), `CalendarSettings`, the FakeJmap calendar reads | Sonnet | 19 | 19 | 1 |
| `cal-edit` | B | Event and task writes (create, update by scope, delete, drag move with rollback), scope patches, guests, organiser rules, invitations (RSVP, add, counter, cancel) and the banner actions, .ics import and export, webcal subscriptions (LAN and localhost blocked, http warned), birthdays, guest search, find a time and availability, the FakeJmap calendar writes | Sonnet | 14 | 17 | 1 |
| `cal-views` | C | The Calendar app in the shell (rail, screen host), toolbar, sidebar (mini month, calendar list, capped task list slot), Day, Week, Month, Agenda grids, the all-day strip cap, hover preview, drag move, resize and create with rollback, search results, print model and view | Sonnet | 3 | 3 | 1 |
| `cal-ui` | D | Event editor (all fields, recurrence editor, alerts, guests, availability, find a time), quick-create popover, scope dialog, task editor and Tasks view, calendar and subscription dialogs, import dialog, reminder popup and missed dialog (views), event, slot, task and calendar menus, calendar commands, palette entries and shortcuts | Sonnet | 4 | 4 | 1 |

- **All four streams start together** (one wave), because the contracts in §4 are compiled code with stubs and every stream works against interfaces
  first. §5 says what each stream needs from the others and when it merges into `m4`.
- **Limits held:** at most 3 agents compile at once (CLAUDE.md; `tools/build.ps1` holds 4 slots and queues the rest), at most 5 agents alive, one Linux
  VM build at a time. Streams A and B build Core and Presentation tests most of the time; C and D build the App head and queue on the slots.
- **Branches `m4-<key>`**, worktrees `<downloads>/wt-m4-<key>`, push at creation and after every commit.
- **Ports and profiles** (hidden AUTOMATION runs, `BARBICAN_PROFILE=auto-<port>`): cal-views 8901, cal-ui 8902, integrate/verify 8909. cal-data and cal-edit
  have no UI and need none (their scenarios run in-process against FakeJmap).
- **M4 T rows:** 45 (`python tools/spec-check.py --milestone M4`: 5 `invite::*` rows were ported with the reader in M2, 40 were pending). This plan
  commit ports the 40 as **red tests**, plus 3 red tests for behaviour that is not a Rust row (webcal address rules, the import alerts prompt): 43 red in
  all (`python tools/red-check.py --list`: cal-data 19, cal-edit 17, cal-views 3, cal-ui 4). Green tests in this commit: the FakeJmap calendar arms,
  `CalendarSettings` defaults, the all-day strip cap (3 tests).

## 1. Rules every M4 agent repeats

Everything in [M3-plan.md](M3-plan.md) §1 and [M2-plan.md](M2-plan.md) §1 still holds (CLAUDE.md, `.local/accounts.md`, hidden automation only, captures
that are uniform are invalid and never a pass, `<readonly-account>` strictly read-only, never read `<admin-key-file>`, builds only through
`tools/build.ps1` / `tools/linux.ps1`, push every commit, privacy check before every commit, Fable log, code rules, feel rules, contract stubs throw
`PortPendingException` and the owner removes `[Red]` in the same commit as the body, never change a contract signature without a note under "Requests to
other streams"). The additions and changes for M4:

1. **No reviews, no hand-test gates, no questions.** Choose the default (Bulwark wins), write it in your Fable section and in `spec/decisions.md` (next free
   `D-M4-nn`; check the file for duplicates before you append), move on.
2. **Live checks** run only in the integrate stage (§7), one agent at a time. `<readonly-account>` reads only (Agenda and Tasks with many tasks, L-027, L-026,
   L-029). Everything that writes (L-012 RSVP, L-020..L-025, L-028) runs on `<test-account>`, invites only itself or addresses that do not deliver, and every
   write is cleaned up (events, tasks, subscriptions, calendars destroyed; L-030 lists what to remove). A stream writes its FakeJmap equivalent and its live
   script and runs nothing live itself. **Never send an invitation to a real third party.**
3. **Never touch the user's screen, mouse or keyboard.** Drag, click and keys are driven in-process by the hidden automation build (`CommandRouter.Calendar*.cs`),
   never by OS input. Printing never reaches a printer in a test: the print model is pure and the OS print path is built but not run by an agent.
4. **Time is injected.** `TimeProvider` for now and the local zone (`TimeProvider.LocalTimeZone`), never `DateTime.Now`; tests that read zones build instants
   from UTC offsets or `TimeZoneInfo.Local` (D-M4-05). A calendar test never depends on the machine's zone: use `UTC` or a named zone and the day of the year that
   makes the DST case explicit.
5. **JSCalendar stays `JsonNode`** (D-M4-02): an unknown property on an event, calendar or task survives every read-modify-write; writes send JSON-pointer
   patches of the changed fields only (as Rust and Bulwark do), never the whole object back.
6. **Bulwark and Stalwart references.** Every Core file starts with the Rust file:line, the Bulwark file, the RFC section and the Stalwart path it mirrors
   (`<bulwark>`, `<stalwart-zip>`: `crates/jmap/src/calendar*/`, `calendar_event/`, `principal/`); the contract stubs name them, **you verify the lines**.
7. **Stay inside your owned files (§3).** A change you need elsewhere goes under `## Requests to other streams` in your Fable section; work around it behind
   your own interface; the integrator applies it.
8. **User decisions in force:** D-2, D-3, D-7, D-9, D-10 of M2-plan §1 item 14; D-1 (rewriting public history) is not approved.

## 2. What M4 must close

### 2.1 Rust tests (T-): 45 rows

| Stream | Rows | Test files |
|---|---|---|
| cal-data (19) | T-231..T-237, T-240 `recurrence::*` (8: durations, weekly, until, monthly, day 31, yearly, overrides, count left); T-238, T-239 (summaries, JSON round trip and sameness); T-241 (zones and search); T-247..T-252 `reminders::*` (6); T-055 `calendar::occurrences_carry_recurrence_ids_and_skip_tasks`; T-056 `calendar::namespaced_ids` | `tests/Barbican.Core.Tests/Calendar/Recurrence/{RecurrenceEngineTests,RecurrenceRuleTests,TimeZonesTests}.cs`, `Reminders/ReminderRulesTests.cs`, `CalendarOccurrencesTests.cs`, `CalendarIdsTests.cs` |
| cal-edit (14) | T-035..T-040 `cal_editor::*` (new event data, DST duration, guests and rules, edits keep ids and answers, organiser detection, scope patches); T-041 free slots; T-042 people parsing; T-052 people search; T-044, T-045 feeds; T-049 birthdays; T-051 imports cleaned; T-127 `invite::import_and_patch_shapes` | `tests/Barbican.Core.Tests/Calendar/Editing/{EventEditorTests,FindATimeTests,CalendarPeopleTests,BirthdayCalendarTests,ImportRulesTests}.cs`, `Feeds/FeedRulesTests.cs`, `Invitations/InvitationActionsTests.cs` |
| cal-views (3) | T-043 agenda titles and the task-day filter; T-050 agenda parts; T-054 day columns | `tests/Barbican.Presentation.Tests/Calendar/Grid/{AgendaRulesTests,CalendarGridRulesTests}.cs` |
| cal-ui (4) | T-046 quick task dates; T-047 task labels and filters; T-048 task field mapping; T-053 sidebar tasks capped and overdue | `tests/Barbican.Core.Tests/Calendar/Tasks/TaskRulesTests.cs` |

Counts: 19 + 14 + 3 + 4 = 40, plus the 5 `invite::*` rows already ported = 45. **Assertions that changed** (D- entries): D-M4-05 (T-043: the thread-local day
filter is an instance; tests that read the zone build their own instants), D-M4-06 (T-052: the cards are an argument). All other assertions are Rust's, value for
value. **Extra red tests** (not Rust rows, written from the GAPS rows): cal-edit `FeedRulesTests.Lan_and_localhost_feeds_are_blocked` and
`Public_https_is_allowed_and_http_is_allowed_with_a_warning` (G-006), `ImportRulesTests.The_alerts_prompt_asks_only_when_the_file_has_alerts` (G-004).

**Rows that are not unit tests but must be covered** (Rust has them without a test; port them with C# tests): the reminder scheduler (15 s check, 300 s refetch,
snooze, acknowledge, stale threshold, missed once), the calendar store's chunked load and `Patch`/`Restore`, drag move with rollback (`CalendarWriter.MoveAsync`),
`IcsWriter.Event` (folding at 75 octets, escaping), `FeedRules.Check` on redirect hops, the invitation answer calls, `CalendarSettings` (done), the recurrence
**differential test** against Ical.Net (§6.1). They are in the stream acceptance lists of §6.

### 2.2 JMAP requirements (J-)

J-072 (Calendar/get, CalendarEvent query/get/set, `onSuccessSetIsDefault`; currently `partial(M4)` with the parse read in M2) moves to `done` when cal-data's load
and cal-edit's writes pass against FakeJmap, with `cs_test` naming both; the Bulwark and Stalwart references are re-verified. J-042 (SSE `calendarAlert`, done in
M1) gets its consumer: cal-data. J-008 and J-013 (chunked get, paged query) are the rules the calendar load follows (a back-referenced get of thousands of ids is
over Stalwart's limit; chunks of `maxObjectsInGet`). `Principal/query` and `Principal/getAvailability` are new in `spec/jmap.csv`: cal-edit adds rows J-100.. (next
free ids; check the file) and marks them `done` against FakeJmap and `partial` until L-029 runs.

### 2.3 Interactions (I-): 49 calendar rows (I-296..I-344) and I-212

Owner = the stream that builds the visible control; a data stream supplies the view model. Rows a stream closes need evidence (a test, an automation script, or a
screenshot path with sample data); everything else is `deferred(M<k>)` with a reason.

| Owner | Rows |
|---|---|
| cal-views (22) | I-298 (sidebar resize, swipe, horizontal wheel), I-299 (hover popover, enterable), I-301 (load error Retry), I-302, I-303 (mini month: new event, prev/next/day, month and year picker, keyboard, plugin marks), I-304, I-305 (calendar row click and right-click, change colour: the control; the menu items are cal-ui's), I-308 (section headers, plus, Tasks entry, account groups), I-310 (Today, Prev, Next), I-311 (view switch), I-312 (More menu: the toolbar and Refresh; Import and Subscribe open cal-ui's dialogs), I-313 (event search and results), I-314..I-316 (month: quick create click, right-click, wheel, drag range, day number, roving keys), I-317, I-318 (event click, drag move, resize handles, drag-to-create), I-319 (+N more), I-320 (task chips in the grid), I-321 (week extras: N days, all hours, hidden pill, day header, all-day row), I-322, I-323 (hour slot click, double-click, right-click), I-325 (Agenda), I-331 (event detail panel and popover), I-341 (print) |
| cal-ui (22) | I-296 (all calendar keys and their routing), I-297 (menu commands and palette entries), I-300 (scope dialog), I-307 (subscriptions: subscribe, edit, refresh, unsubscribe), I-309 (create calendar modal), I-324 (quick-create popover), I-326..I-330 (the menus: empty space, empty-space New task, event menu with RSVP, Edit, Duplicate, Export .ics, Copy title, Copy to account, meeting link, deep link, Print; delete with scope and notify), I-332..I-336 (event modal view mode, dirty check, fields, pickers, zone, custom recurrence, show as, visibility, video link), I-337 (guests, availability, Find a time), I-338 (alerts editor, reminders, missed reminders), I-339 (edit footer), I-340 (import dialog with the alerts prompt), I-342..I-344 (tasks: filters, modal, row keys, Pick date, Move to list, quick add with a natural-language date, task menu) |
| cal-edit (1) | I-212 (invitation banner: Accept, Maybe, Decline, Add to calendar, remove cancelled, apply counter-proposal, view; the banner source and its actions through `IReaderBannerSource` and `IBannerActionHandler`) |
| deferred | I-306 share calendar and the "..." sharing items (M6, sharing and ACLs), the cal-ui parts that need contacts proper (address-book picker, M5), I-329 "Copy to account" (needs a second login with calendars; built when two logins have the capability, else `deferred(M10)` with the reason in the row), live drag with a real pointer (unproven, needs a human) |

### 2.4 Plugin hooks (35 rows with `milestone = M4` in `spec/plugins/hooks.csv`; the `owner` column is filled by this plan)

Each stream gives its hooks a call site through `IHookBus` and sets `cs_call_site` / `status` in the CSV for its own rows only (row-level edits merge cleanly; run
`python tools/gen-hooks.py --check`; the integrator regenerates `HookNames.g.cs` if asked). Payload types are new files under
`src/Barbican.Core/Plugins/Payloads/` named after the hook, one file per type. Intercepts run before the action and can stop it.

| Owner | Hooks |
|---|---|
| cal-data (6) | onBeforeFetchEvents, onFetchingEvents, onBeforeCalendarAlert, onCalendarAlert, onCalendarAlertAcknowledge, onTasksLoaded |
| cal-edit (21) | onCheckEventConflicts, onBeforeEventCreate, onCreatingEvent, onAfterEventCreate, onBeforeEventUpdate, onUpdatingEvent, onAfterEventUpdate, onBeforeEventDelete, onAfterEventDelete, onBeforeEventRsvp, onEventRsvp, onBeforeEventsImport, onEventsImport, onICalSubscriptionChange, onBeforeTaskCreate, onTaskCreate, onBeforeTaskUpdate, onTaskUpdate, onBeforeTaskDelete, onTaskDelete, onTaskToggleComplete |
| cal-views (5) | onCalendarEventOpen, onCalendarDateChange, onCalendarViewChange, onCalendarChange, onCalendarVisibilityToggle |
| cal-ui (3) | onCalendarEventFormOpen, onCalendarEventFormSave, onTaskFilterChange |

`HookCallSiteTests` gets one test per owner (as M3): every row of the owner has a call site that a FakeJmap-driven test reaches.

### 2.5 Behaviour rows (B-), live checks (L-), gaps (G-), settings

- **B-:** `spec/behaviours/calendar.md` B-CAL-001..062, each stream marks the rows of its area `ported` or `deferred(M<k>)` in the same commit. cal-data:
  001..006, 008 (the range rule, with cal-views), 018..028 (rules and summaries), 030..035, 048 (the borrow rule, as a test that reads the store while it
  changes), 062 (G-005); cal-edit: 011..017, 029, 036 (rule), 038, 041..047, 049..051, 061; cal-views: 007 (with cal-ui), 009, 010, 037, 052 (gestures), 053..056,
  058..060; cal-ui: 036, 039, 040, 057 (colour change and default rule: the menu; the settings page rows are M6). B-CAL-057's settings page part is M6.
- **L- (run in the integrate stage, §7):** L-012 (RSVP sent), L-020 (quick create), L-021 (rule builder), L-022 (drag with attendees sends updates: the
  calendar part), L-023 (scope dialog), L-024 (task editor), L-025 (.ics import: the file's alerts are kept unless the user says no in the alerts prompt), L-026 (birthdays), L-027 (Agenda
  and Tasks with many tasks: the PARITY-ROUND2 crash scenario on `<readonly-account>`), L-028 (reminder fires, own alert and 8 or more days ahead), L-029
  (availability supported?), L-030 (cleanup).
- **G-:** G-001 (counter-proposal), G-002 (cancelled event removed): cal-edit; G-003 (all-day strip cap): cal-views; G-004 (import alerts prompt): cal-edit service,
  cal-ui dialog; G-005 (an event's own alerts beyond 8 days): cal-data; G-006 (webcal LAN and localhost): cal-edit; G-008 (print): cal-views; G-009 (find a time):
  cal-edit, with the capability result in the report; G-044 (live updates for calendars; contacts, files and identities stay M5): cal-data; G-100 stays done and
  gets its live row L-028.
- **Settings read** (`spec/settings.csv`; each owner sets `csharp_read_by`):

| Stream | Keys |
|---|---|
| cal-data | calendarNotificationsEnabled, calendarNotificationSound, doNotDisturb (already read by A11y: the popup respects it), enableCalendarTasks, showBirthdayCalendar (loads cards), calendarInvitationParsingEnabled (reader banner, with cal-edit), timeFormat (labels), timeZone |
| cal-edit | showBirthdayCalendar (the occurrences), calendarInvitationParsingEnabled, timeZone (the form's default zone), firstDayOfWeek (recurrence `firstDayOfWeek` default) |
| cal-views | firstDayOfWeek, showWeekNumbers, calendarLimitHours, calendarDayEndHour, calendarHideNonWorkingDays, showTimeInMonthView, calendarMonthTimedEvents, calendarTintedEvents, calendarFreeScroll, calendarHoverPreview, showTasksOnCalendar, timeFormat |
| cal-ui | showTasksApp (the Tasks entry in the rail sidebar), enableCalendarTasks (the Tasks view and task menus), timeFormat, `keyless` rows S-keyless-049..051 (calendar opens in, organize invitations as, working days: Bulwark has no key; read from Bulwark's defaults and recorded as dropped like Rust) |

## 3. Parallel work without shared files

### 3.1 File ownership

Every file has exactly one owner for the whole milestone. Folders go with all files below them. Files not listed stay with the integrator (frozen):
`Barbican.slnx`, `Directory.*` (except the one-line package additions of D-M4-03), `global.json`, `AppComposition.cs`, `CompositionGraphTests.cs`, every M2 and M3 file
(except the exceptions named here).

| Owner | Source | Tests |
|---|---|---|
| **cal-data** | `src/Barbican.Core/Calendar/{CalendarIds,CalendarOccurrence,CalendarOccurrenceKind,CalendarOccurrences,CalendarSnapshot,CalendarChangedEventArgs,ICalendarStore,CalendarStore,CalendarSettings,CalendarDataRegistration}.cs`; `src/Barbican.Core/Calendar/Recurrence/**`; `src/Barbican.Core/Calendar/Reminders/**`; new files in `src/Barbican.Core/Sync/` and `src/Barbican.Core/Jmap/Push/` for the calendar types (additions; the push type list line of the M1 code is the one allowed edit there); its hook payload files in `src/Barbican.Core/Plugins/Payloads/`; `src/Barbican.Presentation/Calendar/Reminders/**`; `src/Barbican.Presentation/Composition/CalendarDataPresentationRegistration.cs`; the NodaTime line of `Core.csproj` and `Directory.Packages.props` | `tests/Barbican.Core.Tests/Calendar/{Recurrence,Reminders}/**`, `Calendar/{CalendarIds,CalendarOccurrences,CalendarSettings}Tests.cs`; `tests/Barbican.Presentation.Tests/Calendar/Reminders/**`; FakeJmap: `FakeCalendars.cs`, `FakeApi.Calendar.cs`, `FakeSeed.M4.cs` (new), `FakeCalendarArmsTests.cs`; `tests/fixtures/calendar/recurrence/**` |
| **cal-edit** | `src/Barbican.Core/Calendar/Editing/**`; `src/Barbican.Core/Calendar/Feeds/**`; in `src/Barbican.Core/Calendar/Invitations/` the files `InvitationActions`, `ParsedInvitation`, `IInvitationService`, `InvitationService` (the M2 files `InvitationModel`, `InviteMethod`, `InviteOrganizer`, `InviteParticipant`, `InviteTrust` stay frozen unless a T-/I- row needs a fix); its hook payload files; `src/Barbican.Presentation/Reader/InvitationBannerSource.cs` (exception: the banner gets its actions); `src/Barbican.Presentation/Calendar/Invitations/**`; `src/Barbican.Presentation/Composition/CalendarEditingPresentationRegistration.cs`; the Ical.Net line of `Core.csproj`, `Directory.Packages.props` and `Barbican.Core.Tests.csproj` | `tests/Barbican.Core.Tests/Calendar/{Editing,Feeds,Invitations}/**`; `tests/Barbican.Presentation.Tests/Calendar/Invitations/**`, `Reader/InvitationBannerTests.cs`; FakeJmap: `FakeApi.CalendarSet.cs`, `FakeApi.CalendarEventParse.cs` (counter and cancel parts only); `tests/fixtures/calendar/{ics,feeds}/**` |
| **cal-views** | `src/Barbican.Presentation/Calendar/Grid/**`; `src/Barbican.Presentation/Composition/CalendarViewsPresentationRegistration.cs`; `src/Barbican.App/Views/Calendar/**` **except** the sub-folders `Editors`, `Tasks`, `Reminders`, `Menus`, `Dialogs` (cal-ui's); `src/Barbican.App/Composition/Features/CalendarViewsRegistration.cs`; `ShellPage.xaml` and `ShellPage.xaml.cs` for the calendar screen host only (the rail switches the surface; `SetAvailableApps` is called from here when the login has Calendars); `src/Barbican.Automation/CommandRouter.Calendar.cs` (new: `cal-open`, `cal-view`, `cal-dump`, `cal-drag`, `cal-click`) | `tests/Barbican.Presentation.Tests/Calendar/Grid/**` |
| **cal-ui** | `src/Barbican.Core/Calendar/Tasks/**`; `src/Barbican.Presentation/Calendar/{Editors,Tasks,Commands}/**`; `src/Barbican.Presentation/Composition/CalendarUiPresentationRegistration.cs`; `src/Barbican.App/Views/Calendar/{Editors,Tasks,Reminders,Menus,Dialogs}/**`; `src/Barbican.App/Composition/Features/CalendarUiRegistration.cs`; the calendar branch of `src/Barbican.Presentation/Input/ShortcutTables.cs` and the calendar routing in `KeyRouter.cs` / `ShortcutCommands.cs` (exception: no other part of those files); `src/Barbican.Automation/CommandRouter.CalendarUi.cs` (new: `cal-editor-open`, `cal-editor-set`, `cal-editor-save`, `cal-task-add`); its hook payload files | `tests/Barbican.Core.Tests/Calendar/Tasks/**`; `tests/Barbican.Presentation.Tests/Calendar/{Editors,Tasks,Commands}/**` |

Shared and frozen by this plan: `AppComposition.cs` already calls the six registrations (§4.6); `SlotNames.cs` already has `CalendarTasks` and `CalendarSidebarTasks`.
`tests/Barbican.Core.Tests/Barbican.Core.Tests.csproj` and the other test projects are frozen except the package line above.

### 3.2 Shared spec files

`spec/rust-tests.csv` (`python tools/spec-check.py --write`), `spec/interactions.csv`, `spec/jmap.csv`, `spec/settings.csv`, `spec/gaps.csv`,
`spec/live-checks.md`, `spec/behaviours/calendar.md`, `spec/plugins/hooks.csv`, `spec/decisions.md`: edit only your own rows (one line per row merges cleanly; a
new decision is appended at the end of "M4 decisions"). `docs/FABLE-REVIEW.md`: append under `## M4 stream: <key>` (a section already exists for the plan).

## 4. Contracts (compiling code in this commit set)

All stubs below are in the repository now, build with warnings as errors, and are wired into the composition (§4.6). A stub that throws does so with
`PortPendingException("Type.Member")`; the 43 red tests prove the stubs are reached. Wall-clock values are `DateTime` (Kind Unspecified), instants are UTC
milliseconds (`long`), JSCalendar objects are `JsonNode` (D-M4-02).

### 4.1 Recurrence, time zones, reminders (owner cal-data; consumed by cal-edit, cal-views, cal-ui)

`src/Barbican.Core/Calendar/Recurrence/`, namespace `Barbican.Core.Calendar.Recurrence`:

| Type | Contract |
|---|---|
| `RecurrenceRule` (record; equality real) | `Freq`, `Interval`, `ByDay` (`ByDayEntry(DayOfWeek, long? Nth)`), `ByMonthDay`, `ByMonth`, `BySetPos`, `Count`, `Until`, `FirstDayOfWeek`; `Simple`, `Parse(JsonNode)`, `ToJson`, `IsSimple`, `Summary` (Bulwark's English), `NthDay` (stubs) |
| `Occurrence` | Start, End, `RecurrenceId`, `Patch` (the override) |
| `RecurrenceEngine` (static stub) | `ParseLocal`, `FormatLocal`, `DurationSeconds`, `DurationString`, `EventDuration`, `RuleStarts(start, rule, to, max)`, `Occurrences(event, from, to)` (overrides, excluded rules, moves), `OccurrencesBefore`, `SameRules` |
| `TimeZones` (static stub) | `ZoneToLocal`, `ZoneToUtcMs`, `LocalToZone`, `All`, `Search` over NodaTime's tzdb; the local zone is an optional argument |

`src/Barbican.Core/Calendar/Reminders/`: `ReminderRow(Value, Unit, Action)`, `AlertEditRow(Row, Source)` with `AlertSource`, `DueReminder`, `ReminderRules` (static stub:
`FetchAhead`, `OffsetMs`, `OffsetToUnit`, `RowOffset`, `RowLabel`, `Describe`, `AlertsLabel`, `CalendarDefaults`, `EffectiveAlerts`, `RowsOf`, `WithOwnIds`,
`BuildAlertsPatch`, `AlertKey`, `DueBetween`, `WhenLabel`, `Missed`; constants `StaleMs`, `MissedWindowMs`), `IReminderStateStore` + `ReminderStateStore` (stub: calendar.json in
the profile), `IReminderScheduler` + `ReminderScheduler` (stub: `Due` event, `Start`, `Shutdown`, `DismissAsync`, `Snooze`, `MissedAsync`), `ReminderEventArgs`.
`src/Barbican.Presentation/Calendar/Reminders/IReminderPopup` + `ReminderPopup` (stub): shown reminders, the missed list, dismiss, snooze, open (cal-data writes it,
cal-ui draws the popup and the dialog over it).

### 4.2 Calendar data (owner cal-data; consumed by all)

`src/Barbican.Core/Calendar/`: `CalendarOccurrence` (record) and `CalendarOccurrenceKind` (Event, Task, Birthday), `CalendarIds` (static stub: `Ns`, `SplitNs`, `BaseId`,
`RecurrenceIdOf`, `NamespaceEvent`, `ServerCalendarIds`), `CalendarOccurrences` (static stub: `In`, `Covers`, `IsTask`; `AgendaDays`), `CalendarSnapshot`,
`CalendarChangedEventArgs`, `ICalendarStore` + `CalendarStore` (stub: `Changed`, `LoadAsync`, `Snapshot`, `RefreshAsync`, `Patch` and `Restore` for an optimistic local change and its
rollback), `CalendarSettings` (**real**, `Read(PrefsSnapshot)`; defaults are Rust's, tested green), `CalendarDataRegistration.AddBarbicanCalendarData()`.

### 4.3 Editing, invitations, feeds (owner cal-edit; consumed by cal-views, cal-ui)

`src/Barbican.Core/Calendar/Editing/`, namespace `Barbican.Core.Calendar.Editing`:

| Type | Contract |
|---|---|
| `CalendarPerson`, `PersonMatch`, `CalendarPeople` (static stub) | guests as text: `Parse`, `Initials`, `Matching(cards, query, have, max)` |
| `EventForm` (record, real), `EventData`, `EventEditor` (static stub) | `BuildData(form, event, occurrence)` returns the fields and whether scheduling messages go out; `UserOrganizes`, `EventOrganizer`, `AttendeesOf`, `AskedAgain`, `DescriptionText`, `OccurrenceOverride`, `OverridePatch`, `TruncatePatch`, `FollowingSeries`, `SeriesUpdate` |
| `EditScope` (WholeEvent, Occurrence, Following, All), `EventWrite`, `ICalendarWriter` + `CalendarWriter` (stub) | `SaveEventAsync` (create or update by scope, hooks, two-step "following" with restore), `DeleteEventAsync` (scope, notify), `MoveAsync` (drag and resize with store rollback), generic `CreateAsync`, `UpdateAsync`, `DestroyAsync` (tasks, quick create, duplicate, import) |
| `ICalendarAdmin` + `CalendarAdmin` (stub) | create, update (rename, colour, visibility, alerts defaults), set default, delete calendar |
| `IAvailabilityService` + `AvailabilityService` (stub), `FindATime` (static stub), `FreeSlot`, `AvailabilityStatus` | `IsSupported`, `BusyAsync` (Principal/query and getAvailability); `AvailabilityFor`, `FindFreeSlots` (10 working days, 5 suggestions, 30-minute grid) |
| `IContactCardReader` + `ContactCardReader` (stub), `BirthdayCalendar` (static stub) | read-only cards; `PartsOf`, `In` (id `bday:card:entry:year`, calendar `__birthday-calendar__`) |
| `ImportRules` (static stub), `IIcsImporter` + `IcsImporter` (stub), `IcsWriter` (static stub) | clean, `HasAlerts`; `ParseAsync` (Ical.Net, then the server parse), `ImportAsync(…, keepAlerts)`; `.ics` export of one event |
| `Feeds/FeedRules` (static stub), `FeedUrlCheck`, `CalendarSubscription`, `ICalendarSubscriptions` + `CalendarSubscriptions` (stub) | `NormalizeUrl`, `Check` (G-006), `StableUid`, `FillUids`, `SanitizeFeed`, `RefreshError`; list, save, refresh, remove |
| `Invitations/InvitationActions` (static stub), `ParsedInvitation`, `IInvitationService` + `InvitationService` (stub) | `Importable`, `RsvpPatch`; parse, stored, answer, add, apply counter, remove cancelled |
| `CalendarEditingRegistration.AddBarbicanCalendarEditing()` | registers the defaults with `TryAddSingleton` |

### 4.4 Tasks (owner cal-ui)

`src/Barbican.Core/Calendar/Tasks/TaskRules` (static stub): `PriorityLevel`, `IsOverdue`, `DueLabel` (label and tone), `Matches` (filters 0 all, 1 pending, 2 overdue, 3 completed),
`ParseDue` (quick add), `Fields` (the save fields, `Result<JsonObject>`), `Sidebar`, `Overdue`; constants `SidebarTasks` = 6 and `AlertMinutes`. The task day filter
of the mini month is cal-views' `TaskDayFilter` (instance; `IsDueOn` static).

### 4.5 Views and editors (owners cal-views and cal-ui; the seam between them)

`src/Barbican.Presentation/Calendar/Grid/` (cal-views): `CalendarViewKind` (Day 0, Week 1, Month 2, Agenda 3, Tasks 4), `CalendarListRow`, `ICalendarViewModel` + `CalendarViewModel`
(stub; `PropertyChanged`, view, anchor, range, title, occurrences, calendar rows, selection, search text, loading and error, `SetView`, `Today`, `GoPrevious`, `GoNext`, `ShowDay`,
`SelectOccurrence`, `ToggleCalendar`, `LoadAsync`), `CalendarGridRules` (**real** all-day strip numbers, stub `DayColumns`), `AgendaRules` (stub), `TaskDayFilter` (stub),
`ICalendarGestures` + `CalendarGestures` (stub: `Snap`, `MoveAsync`, `ResizeAsync`, `CreateAsync`). `src/Barbican.Presentation/Calendar/Editors/` (cal-ui): `NewEventRequest`,
`ICalendarEditors` + `CalendarEditors` (stub: `OpenQuickCreateAsync`, `OpenNewEventAsync`, `OpenAsync(occurrenceId)`, `OpenNewTaskAsync`, `AskScopeAsync`). **cal-views calls
`ICalendarEditors` for every click and drag-to-create; cal-ui reads `ICalendarViewModel` for the anchor, range and selection.** The Tasks view and the sidebar task list are
cal-ui controls put into cal-views' screen through the slots `SlotNames.CalendarTasks` and `SlotNames.CalendarSidebarTasks` (`ISlotContent`, as M2's sidebar slots).
Menus: cal-views raises the context menu through the M2 `IContextMenuService` with the menu ids `MENU-EVENT`, `MENU-SLOT`, `MENU-TASK`, `MENU-CAL` (`spec/menus.md`); the
`IMenuProvider`s are cal-ui's. cal-views never builds a menu item.

### 4.6 Composition (DI skeletons, compile and resolve today)

`AppComposition.Build` now calls, after the M3 features: `AddBarbicanCalendarData()` (Core, cal-data), `AddBarbicanCalendarEditing()` (Core, cal-edit),
`AddBarbicanCalendarDataViewModels()` and `AddBarbicanCalendarEditingViewModels()` (Presentation, `Composition/Calendar{Data,Editing}PresentationRegistration.cs`),
`AddBarbicanCalendarViews()` (`Features/CalendarViewsRegistration.cs`, App; calls `AddBarbicanCalendarViewsViewModels()`), `AddBarbicanCalendarUi()` (`Features/CalendarUiRegistration.cs`, App; calls
`AddBarbicanCalendarUiViewModels()`). `CompositionGraphTests.Compose` registers the six portable parts, so "every registered service resolves" covers them. Rules as M3 §4.7: defaults
use `TryAddSingleton`; a stream that implements a service changes the registration in its own file (`Replace` when the default belongs to another feature); stub classes have no
constructor parameters, so the graph test passes while they exist; a stream that adds dependencies keeps the graph test green.

### 4.7 FakeJmap calendar arms (in this commit set)

`tests/Barbican.FakeJmap/FakeCalendars.cs` (an in-memory calendar account: `AddCalendar`, `AddEvent`, state strings per type, a change log for /changes, `SchedulingSent`, `ApplyPatch`),
`FakeApi.Calendar.cs` (**real**: Calendar/get, Calendar/changes, CalendarEvent/query with `inCalendars` and `uid` filters, get with the `maxObjectsInGet` limit, changes,
Principal/query, Principal/getAvailability from the events a guest takes part in), `FakeApi.CalendarSet.cs` (stub: `Calendar/set` and `CalendarEvent/set` answer unknownMethod until cal-edit
fills it). Options: `FakeJmapOptions.Calendars` (the data; needs `CalendarsCapability`) and `AvailabilityCapability`. Green tests: `FakeCalendarArmsTests`. cal-data adds a
`FakeSeed.M4` (calendars for Work, Personal, a shared one; a series with overrides; tasks; an event with guests; a long-default-alert calendar) in its first step; cal-edit fills the set arm
in its first step (create, update with pointer patches, destroy, `sendSchedulingMessages` recorded, `invalidProperties`, `notFound`).

## 5. Order, sync points and merges

All four start on day one, from `m4`. **Merge early:** a stream merges into `m4` after every green step (build + tests on Windows, Linux VM when it touched shared Core), and every
other stream merges `m4` into its branch at the start of each step. The sync points:

| Point | Who | What lands in `m4` | Who needs it |
|---|---|---|---|
| **S0** | cal-views | Step 1: the Calendar app in the shell: `CalendarScreen` host in `ShellPage`, `SetAvailableApps` with Calendar when the login has the capability, the rail switches, an empty month grid over a real `CalendarViewModel` | cal-ui (hosts its slots and editors), the automation scenario |
| **S1** | cal-data | Step 1: recurrence engine, `TimeZones`, `CalendarIds`, `CalendarOccurrences` (T-231..T-241, T-055, T-056) and the NodaTime package | cal-edit (`BuildData`, scope patches), cal-views (grids), cal-ui (recurrence editor summary) |
| **S1** | cal-data | Step 2: `CalendarStore.LoadAsync`/`Snapshot`, `FakeSeed.M4` | cal-views (real data instead of a fake store), cal-edit (writes patch the store) |
| **S1** | cal-edit | Step 1: pure rules that need nothing from cal-data (guests, feeds, import, birthdays, find a time, invitation shapes, `IcsWriter`), the FakeJmap set arm | cal-ui (guest field, availability, import dialog) |
| **S2** | cal-edit | Step 2: `EventEditor` complete (T-035..T-040 need S1 of cal-data), `CalendarWriter` (save by scope, delete, `MoveAsync`, generic writes), `CalendarAdmin` | cal-views (drag), cal-ui (editor save, tasks) |
| **S2** | cal-data | Step 3: reminders (rules, scheduler, state, popup view model), push and `RefreshAsync` | cal-ui (popup and dialog views), everyone (live updates) |
| **S3** | cal-edit | Step 3: invitations and the banner actions, subscriptions, `IcsImporter`, availability service | cal-ui (dialogs), reader banner |
| **S3** | cal-views | Step 3: gestures (drag move, resize, create with rollback), hover preview, print model | cal-ui (quick create after a drag) |

What **cal-views needs from cal-data**: `ICalendarStore` and `CalendarOccurrences` (S1); until then it paints a fake `ICalendarViewModel` over occurrence literals. What it needs from
**cal-edit**: `ICalendarWriter.MoveAsync` (S2; until then a fake writer that records). What it needs from **cal-ui**: `ICalendarEditors` (a fake that records; the real one lands whenever).
What **cal-ui needs from cal-views**: `ICalendarViewModel` (S0) and the slots; from **cal-data**: `ReminderRules` (S1 for the alert rows, S2 for the scheduler), `RecurrenceRule` (S1); from
**cal-edit**: `EventEditor` and `ICalendarWriter` (S2), `IAvailabilityService`, `IIcsImporter`, `ICalendarSubscriptions` (S3). Until then cal-ui works against fakes in
`tests/Barbican.Presentation.Tests/Calendar/` that implement the interfaces. What **cal-edit needs from cal-data**: the recurrence engine for `BuildData` duration and `FollowingSeries` (S1); it
starts with the rules that do not (the sync table). What **cal-data needs from the others**: nothing.

**Merge order into `m4` for the milestone close:** cal-data, cal-edit, cal-views, cal-ui (each already merged incrementally); the integrator then runs §7.

## 6. Stream briefs

Common to all: read CLAUDE.md, `.local/accounts.md`, this plan §1 to §5 and the PORT-PLAN sections named in your brief; create the worktree `<downloads>/wt-m4-<key>` on branch `m4-<key>` from `m4`;
push at creation and after every commit; Fable section `## M4 stream: <key>`.

### 6.1 `cal-data` (letter A, Sonnet)

**Scope.** The calendar's data and its clock: everything between JMAP and a list of occurrences, and between occurrences and a reminder. No XAML. Reads Rust `src/recurrence.rs`, `reminders.rs`,
`calendar.rs` (load :273, occurrences :355-:423, ids :226-:252, patch_local :1308), Bulwark `lib/recurrence-rule.ts`, `recurrence-expansion.ts`, `recurrence-instances.ts`, `calendar-alerts.ts`,
`lib/calendar-reminders/{runner,state,claims,channel}.ts`, `hooks/use-calendar-alerts.ts`, `stores/calendar-store.ts` (load, changes), Stalwart `crates/jmap/src/calendar*/`, `calendar_event/{query,get,changes}.rs`.

**Owned:** §3.1 row "cal-data". **T rows:** T-055, T-056, T-231..T-241, T-247..T-252 (19). **J rows:** J-072 (with cal-edit), J-042 consumer. **Hooks:** 6 (§2.4). **Settings:** §2.5.
**G rows:** G-005, G-044 (calendars). **B rows:** §2.5.

**Steps (each ends green and merged to `m4`):**
1. *Recurrence and zones.* Add NodaTime (D-M4-03). Make T-231..T-241 pass: `RecurrenceRule.Parse`/`ToJson`/`Summary` (English as Rust and Bulwark `buildRecurrenceSummary`), `RuleStarts` (daily, weekly with
   interval and byDay and count, monthly by day and nth weekday and bySetPosition and day 31 skipping, yearly with byMonth, until inclusive, a 100 000 starts and 50 000 periods guard as Rust),
   `Occurrences` (overrides, excluded overrides, moved occurrences, RDATE-like override keys, `excludedRecurrenceRules`), `OccurrencesBefore`, `SameRules`, `TimeZones` over NodaTime (an unknown zone is floating).
   Then `CalendarIds` and `CalendarOccurrences` (T-055, T-056). Add the **differential test** `RecurrenceOracleTests`: for 300 seeded random rules (daily, weekly, monthly, yearly; interval 1 to 3; byDay with and without nth;
   count or until) compare `RuleStarts` with Ical.Net's occurrence set (add Ical.Net to `Barbican.Core.Tests.csproj` only, one line, D-M4-03); documented differences (Ical.Net's handling of day 31 and of
   `bySetPosition` with several byDay) are listed in the test and in D-M4-nn, never silently skipped.
2. *Store and FakeJmap.* `CalendarStore.LoadAsync` (Calendar/get with the 11 properties of Rust `calendar.rs:273`, then per account `CalendarEvent/query` through the paged `QueryAll` and `CalendarEvent/get` in `maxObjectsInGet`
   chunks with the 34 `EVENT_PROPS`; shared accounts with the Calendars capability namespaced by `CalendarIds`; a failure in a shared account is skipped, in the primary one is returned), `Snapshot`, `Patch`/`Restore`,
   `Changed`. `FakeSeed.M4` and the `FakeApi.Calendar.cs` additions you need (a `canCalculateChanges` query, shared-account arms if you add one). `onBeforeFetchEvents`, `onFetchingEvents`, `onTasksLoaded`.
3. *Reminders and push.* `ReminderRules` (T-247..T-252), `ReminderStateStore` (calendar.json under `IProfilePaths.ConfigDir`, atomic write, per owner: acknowledged keys with their fire time, last check), `ReminderScheduler` (every
   signed-in login, not only the active one; 15 s check on `TimeProvider`, refetch every 300 s and on a calendar change, an event's own alerts read from the event: G-005; snooze in memory; the stale threshold; missed once per
   start; `onBeforeCalendarAlert`/`onCalendarAlert`/`onCalendarAlertAcknowledge`; honours `calendarNotificationsEnabled`, sound and `doNotDisturb`), `ReminderPopup` (view model). Push: add `Calendar`,
   `CalendarEvent` (and `CalendarEventNotification` for the reminder refetch) to the EventSource types and the /changes engine; a push for a calendar type calls `RefreshAsync`, which applies `/changes`
   (updated ids fetched in chunks) and falls back to `LoadAsync` on `cannotCalculateChanges`; FakeCalendars raises its `Changed` event into the push hub.

**Acceptance tests** (FakeJmap or pure, no network): the 19 ported tests green; the differential oracle test; `RuleStarts` for 10 more shapes (weekly across a year end, the 5th weekday of a month, byMonthDay -1, interval 2 monthly, a leap day yearly,
`until` before the start); DST: a daily 02:30 event in Europe/Berlin keeps its wall clock across both transitions and `ZoneToUtcMs` of the missing and the doubled hour is deterministic; occurrences of an all-day multi-day series; the store loads 1200 events
from FakeJmap with `maxObjectsInGet` 500 in three gets and never one over the limit; a shared account failing is skipped; `Patch` then `Restore` returns the original and raises `Changed` twice; a push state change of `CalendarEvent` makes one
`/changes` call and the store shows the new event (and a `cannotCalculateChanges` makes a full load); reminders: an own alert 9 days ahead fires (G-005), one a hundred days ahead fires at once and only once, a series fires once per occurrence, two logins fire both, a dismissed key never returns after a
restart (state file), snooze returns after the minutes on a fake clock, a reminder older than 10 minutes at first sight is not shown but is listed as missed once; `HookCallSiteTests` for the 6 hooks.

**Live script (integrate stage):** on `<test-account>`: create an event with an alert 9 days ahead and one 2 minutes ahead, see both computed and the near one fire (L-028); delete both. Read-only: load the calendars of `<readonly-account>` and expand
a year of every series without an exception (L-027's data part).

### 6.2 `cal-edit` (letter B, Sonnet)

**Scope.** Every write and every rule about what to write: events, tasks, calendars, invitations, imports, feeds, guests and availability. No XAML. Reads Rust `src/cal_editor.rs` (pure parts :22-:510 and :1247-:1330), `cal_extra.rs`
(:27-:181 people and birthdays, :430-:760 import and feeds), `calendar.rs` (:815-:975, :981-:1065 ics), `invite.rs`, Bulwark `lib/calendar-participants.ts`, `calendar-rsvp.ts`, `calendar-invitation.ts`, `calendar-cancellation.ts`,
`calendar-ics-export.ts`, `calendar-subscription-sync.ts`, `calendar-feed-diff.ts`, `recurrence-scope-edit.ts`, `recurrence-overrides.ts`, `stores/calendar-store.ts` (writes), `components/calendar/{event-modal,find-a-time,
participant-availability,ical-import-modal,ical-subscription-modal}.tsx`, `components/email/calendar-invitation-banner.tsx`, Stalwart `crates/jmap/src/calendar_event/set.rs`, `calendar/set.rs`, `principal/`.

**Owned:** §3.1 row "cal-edit". **T rows:** T-035..T-042, T-044, T-045, T-049, T-051, T-052, T-127 (14) plus the 3 extra red tests. **I rows:** I-212. **J rows:** J-072 (writes), the new availability rows. **Hooks:** 21 (§2.4). **Settings:** §2.5.
**G rows:** G-001, G-002, G-004 (service), G-006, G-009.

**Steps:**
1. *Pure rules and the set arm.* Add Ical.Net (D-M4-03). Make pass: `CalendarPeople` (T-042, T-052), `BirthdayCalendar` (T-049), `ImportRules`, `FeedRules` including `Check` (the 3 extra red tests; use `System.Net.IPAddress` parsing, IPv4-mapped unwrap),
   `InvitationActions` (T-127), `FindATime` (T-041), `IcsWriter` (with its own tests: fold at 75 octets on UTF-8 boundaries, escape comma, semicolon, backslash and newline, DTSTART with TZID, all-day VALUE=DATE).
   FakeJmap: fill `FakeApi.CalendarSet.cs` (Calendar/set create, update, destroy with `onSuccessSetIsDefault` and `onDestroyRemoveEvents`; CalendarEvent/set create, update with `FakeCalendars.ApplyPatch`, destroy, `sendSchedulingMessages`
   recorded, `invalidProperties`, `notFound`, `ifInState`).
2. *EventEditor and the writer.* `EventEditor` (T-035..T-040: needs S1 of cal-data; build the new-event object, participant ids and answers kept, location ids and coordinates kept, unchanged fields left out, a removed field is a null,
   the organiser asks guests again only when the time moved, `OccurrenceOverride` with only the override keys, `OverridePatch` as a whole map or a pointer, `TruncatePatch` with its restore, `FollowingSeries` with the COUNT left, `SeriesUpdate`).
   `CalendarWriter`: `SaveEventAsync` by scope with the hooks (`onBeforeEventCreate`/`Update`, `onCreatingEvent`, `onAfterEvent*`, `onCheckEventConflicts`), "following" as two calls with the master restored when the second fails, the store patched
   optimistically and rolled back on failure; `DeleteEventAsync` by scope (excluded override, truncation, destroy) with `notify`; `MoveAsync` (patch start and duration, an occurrence of a series as an override, skipped when nothing changes, scheduling messages
   only when the user organises and there are guests, rollback and error on refusal: B-CAL-052); generic `CreateAsync`/`UpdateAsync`/`DestroyAsync` with the task hooks (`onBeforeTaskCreate`, `onTaskCreate`, `onBeforeTaskUpdate`, `onTaskUpdate`, `onBeforeTaskDelete`,
   `onTaskDelete`, `onTaskToggleComplete` when `progress` flips); `CalendarAdmin`; `ICalendarWriter` ids may carry a shared account's namespace (`CalendarIds.SplitNs`) and are written to that account.
3. *Invitations, import, feeds, availability.* `InvitationService` (parse = `CalendarEvent/parse` + the METHOD from the raw blob; answer on the stored copy or add-with-answer in one call with `sendSchedulingMessages`; counter apply; cancel removal by
   `calendar-cancellation.ts`), the banner: `InvitationBannerSource` gets Accept, Maybe, Decline, Add to calendar, Apply proposal, Remove from calendar through an `IBannerActionHandler` (registered in `CalendarEditingPresentationRegistration`; the M2 "Replies arrive with
   Calendar (M4)" note goes away; RSVP runs as the login that owns the message; hooks `onBeforeEventRsvp`, `onEventRsvp`). `IcsImporter` (Ical.Net reading VEVENT and VTODO; the server parse as fallback; at most 5 MB; chunks of `maxObjectsInSet`; alerts kept unless
   the user says no: D-M4-09; `onBeforeEventsImport`, `onEventsImport`). `CalendarSubscriptions` (fetch with an `HttpClient` seam: at most 5 redirects with `FeedRules.Check` on every hop and on the resolved addresses, 20 MB cap while reading and on Content-Length, 30 s timeout;
   refresh by the stable UID diff of Bulwark `calendar-feed-diff.ts` so an unchanged feed writes nothing; subscriptions kept in the calendar's own properties when the server has them, else in calendar.json; `onICalSubscriptionChange`).
   `AvailabilityService` (Principal/query by address, Principal/getAvailability per principal, an error type per guest; `IsSupported` from the session capability). `ContactCardReader` (ContactCard/query + get read only; empty without the capability).

**Acceptance tests** (all FakeJmap, no network): the 14 ported tests and the 3 extra green; save a new event with two guests: one `CalendarEvent/set` with `sendSchedulingMessages` true and `FakeCalendars.SchedulingSent` shows it; edit one occurrence: the master gets
a `recurrenceOverrides/<rid>` patch and nothing else; edit following: two calls, the second failing restores the master; delete this occurrence adds an excluded override; drag with a failing server restores the store and returns the error; drag that changes
nothing makes no call; a task tick sends `progress` and fires `onTaskToggleComplete`; RSVP on a stored event patches `participants/<id>/participationStatus` with scheduling messages, and on an unstored one creates the copy with the answer in one call; apply
counter moves the stored event; a cancel removes it and an outdated cancel does not; import: 3 events and a VTODO from a fixture, alerts kept by default (Bulwark) and dropped when the user says no, a 6 MB file refused; a feed refresh with one changed event writes one update and an
unchanged feed writes nothing; a redirect to `http://127.0.0.1/` is refused; the Ical.Net reader and `IcsWriter` round-trip a fixture; availability with the capability off reports unsupported; `HookCallSiteTests` for the 21 hooks.

**Live script (integrate stage):** `<test-account>`: create an event, edit this occurrence and this and following of a 5-day series, delete; accept an invitation sent to itself (L-012), import a 2-event .ics, subscribe to a public https feed and unsubscribe, set up and delete a calendar; ask
Principal/getAvailability for itself (L-029: the result goes in the report). Cleans up.

### 6.3 `cal-views` (letter C, Sonnet)

**Scope.** The Calendar app as the user sees it, minus the dialogs: the shell host, toolbar, sidebar, the four grids, the Agenda, drag and print. Rust `src/calendar.rs` (render :445-:620, nav, summary, detail, drag), `app.slint` calendar parts and `calendar_ext.slint` for the shapes the user knows;
Bulwark `components/calendar/{calendar-app,calendar-toolbar,calendar-sidebar-panel,mini-calendar,calendar-day-view,calendar-week-view,calendar-month-view,calendar-agenda-view,event-card,task-chip,event-detail-popover,calendar-search,calendar-load-error-notice,calendar-print}.tsx`,
`lib/calendar-print.ts`, `calendar-scroll-window.ts`, `calendar/event-drag-grab.ts`; the design in `design/` (the calendar mockups if any; else the mail screens' tokens) and `docs/milestones/M1-look-gap.md` for the look rules.

**Owned:** §3.1 row "cal-views". **T rows:** T-043, T-050, T-054 (3). **I rows:** the 22 of §2.3. **Hooks:** 5 (§2.4). **Settings:** §2.5. **G rows:** G-003, G-008. **B rows:** §2.5.

**Steps:**
1. *The app in the shell (S0).* `CalendarScreen` beside `MailScreen` in `ShellPage` (rail | calendar sidebar | surface), shown when `ShellViewModel.ActiveApp` is Calendar; `SetAvailableApps` with Calendar when the login advertises `urn:ietf:params:jmap:calendars`; the rail state follows; the Mail screen
   keeps its state while hidden (no reload on return). `CalendarViewModel` (view, anchor, range, title, loading, load error, selection, search text, calendar rows from the store, `onCalendarDateChange`, `onCalendarViewChange`, `onCalendarVisibilityToggle`, `onCalendarChange`). Pure rules:
   T-054 `DayColumns`, T-050 `AgendaRules.TimeLabel`, T-043 `AgendaRules.TaskTitle` and `TaskDayFilter`. The automation commands `cal-open`, `cal-view`, `cal-dump` (view, range, occurrences, selection as JSON).
2. *Grids.* Toolbar (Today, Prev, Next, title, view switch, search, More menu), sidebar (mini month with the week numbers and the task-day filter, calendar list with colours and visibility checkboxes, the two slots, account groups, resize handle, I-298), Week and Day (56 px hours, now line, past dimming, tinted events, the all-day strip with the 3-row cap and the show-more
   toggle: D-M4-08, N days and hidden-days pill, 12-hour labels, the `calendarLimitHours` window, free scroll), Month (6-week grid, week numbers, "+N more" opening the day, timed events as bars or dots, task chips, roving keys), Agenda (30 days, day headings, the task marker rule, B-CAL-048: no state borrow held while rendering), hover preview (enterable,
   `calendarHoverPreview`), the event detail panel (I-331), search results (I-313), load-error notice with Retry. Virtualise the Month and Agenda lists: **L-027's scenario (thousands of tasks) must open in under a second and never throw**; measure with an automation probe on a FakeSeed of 5 000 tasks and 3 000 events and write the numbers in the report.
3. *Gestures and print (S3).* `CalendarGestures`: 15-minute snap, move (day and week by time, month by day), resize, drag-to-create (week, day, month range), the drag ghost and drop-target mark, `ICalendarWriter.MoveAsync` with the rollback toast; the "skip a drop that changes nothing" rule; events with guests that the user does not
   organise do not move (Bulwark `calendar-editability.ts`); `ICalendarEditors` for the quick-create popover after a drag. Print: `CalendarPrintModel` (D-M4-14) and the print view (an in-window preview; the OS path behind a button, never run by an agent). Automation `cal-drag`, `cal-click` through the same entry as the pointer handlers.

**Acceptance tests:** the 3 ported tests; `CalendarViewModel` tests over a fake store (every view's range: Day 1, Week 7 or the shown days, Month 42, Agenda 30; Prev and Next move by the view's unit; Today; the month boundary; first day of week 0 and 1; hidden non-working days); selection is not opening; calendar toggle
hides occurrences; search; the load error and Retry; gesture tests (snap, a move to the same slot is a no-op, a failed move restores, resize minimum 15 minutes, drag-to-create range ordering, a guest event the user does not organise refuses a move); layout tests of the week grid (lanes for overlapping events, the all-day rows, the cap); `CalendarPrintModel` pages per
view; the automation scenario (open, switch the four views, navigate, drag an event on FakeJmap and see the store change and a failed drag roll back); screenshots with sample data at 100 % and 150 %, Bento and Nordic, light and dark, on both heads when the VM answers (captures that are uniform are "unproven"). `HookCallSiteTests` for the 5 hooks.

### 6.4 `cal-ui` (letter D, Sonnet)

**Scope.** Everything the user types into or chooses from in the calendar, the tasks, the reminders' views, and the commands that reach them. Rust `src/cal_editor.rs` (UI parts :512-:1200, :1331-:1480 quick), `cal_extra.rs` (tasks :183-:430, import and subscription UI :506-:860), `reminders.rs` (popup :412-:533), `calendar_ext.slint`, `ctxmenu.rs`; Bulwark
`components/calendar/{event-modal,quick-create-popover,quick-event-input,recurrence-editor,recurrence-scope-dialog,alert-list-editor,participant-input,participant-availability,find-a-time,time-zone-picker,task-modal,task-list-view,task-toolbar,tasks-sidebar-filters,task-context-menu,event-context-menu,empty-space-context-menu,
create-calendar-modal,calendar-select,ical-import-modal,ical-subscription-modal,missed-reminders-dialog}.tsx`, `lib/calendar-shortcuts.ts`, `lib/calendar-tasks.ts`; `spec/menus.md` (MENU-EVENT, MENU-SLOT, MENU-TASK, MENU-CAL), `spec/behaviours/shortcuts.md`.

**Owned:** §3.1 row "cal-ui". **T rows:** T-046, T-047, T-048, T-053 (4). **I rows:** the 22 of §2.3. **Hooks:** 3 (§2.4). **Settings:** §2.5. **G rows:** G-004 (dialog). **B rows:** §2.5.

**Steps:**
1. *Tasks (pure and view).* `TaskRules` (T-046..T-048, T-053). The Tasks view (a control in `SlotNames.CalendarTasks`): filters (all, pending, overdue, completed), sort by due then priority, overdue pills, row click selects and Enter or double-click opens, space toggles progress, quick add with the natural-language date, Pick date, Move to list, the task menu
   (MENU-TASK); the capped sidebar list ("Show all"). The task editor (title, notes, due date and time, priority, progress, repeat, alert, list) through `TaskRules.Fields` and `ICalendarWriter`. **L-027's scenario applies here too: the Tasks view with thousands of tasks is virtualised.** `onTaskFilterChange`.
2. *Event editor and quick create.* `ICalendarEditors` real: the quick-create popover (title with the natural-language start, time, calendar, More options), the full event editor as an in-window card (not a modal window): title, all-day, start and end with date and time pickers and the end date, time zone picker with search (`TimeZones.Search`), repeat (None, daily, weekly, monthly,
   yearly, custom with the recurrence editor: frequency, interval, weekday chips, month day or nth weekday, end never or on or after, live `Summary()`), calendar, show as, visibility, location with the video-link detection, description, guests (chips, contact suggestions from `CalendarPeople.Matching` over `IContactCardReader`, the organiser line), availability per guest and **Find a time**
   (only when `IAvailabilityService.IsSupported`: D-M4-13), alerts (`ReminderRules.RowsOf`/`BuildAlertsPatch`: use calendar defaults or customise, rows with amount, unit and action), the dirty check and discard question, Esc closes, Ctrl+Enter saves (`ShortcutAction.SaveEvent`), Delete with the **scope dialog** (`AskScopeAsync`) and "notify guests", Duplicate, view mode for an event the user cannot edit. Saves build `EventForm`
   then `EventEditor.BuildData` then `ICalendarWriter.SaveEventAsync`. Hooks `onCalendarEventFormOpen`, `onCalendarEventFormSave`.
3. *Dialogs, popup, menus, commands.* Create calendar modal, subscription dialog (address check and the http warning from `FeedRules.Check`, interval 15/30/60/360/1440, name, colour, refresh and unsubscribe), the import dialog (file picker through the shell's `SystemFilePicker` seam, a list of the events found, calendar choice, the alerts prompt when `ImportRules.HasAlerts`, result count), the reminder popup and the missed-reminders dialog (views over `IReminderPopup`, in-window),
   the menus as `IMenuProvider`s item by item from `spec/menus.md` (MENU-EVENT with the participant RSVP rows, Edit, Duplicate, Export as .ics through `IcsWriter` and the file picker, Copy title, meeting link, deep link, Delete; MENU-SLOT; MENU-TASK with the Due date submenu; MENU-CAL with the colour submenu and "Set as default" and "Delete calendar" asking), the calendar commands in `ICommandRegistry` (`calendar.today`, `.prev`, `.next`, `.view.day`..`.tasks`, `.new-event`, `.new-task`, `.search`, `.refresh`, `.import`, `.subscribe`, `.print`) with the palette entries
   (I-297), and the key routing: `ShortcutTables.Calendar` gets `a` (Agenda) and `k` (Tasks) and the existing `ShortcutAction.Cal`, `CalView`, `NewEvent`, `EditEvent`, `FocusSearch`, `Refresh`, `SaveEvent`, `CloseEditor` are routed to those commands in `KeyRouter` (I-296). Automation `cal-editor-open`, `cal-editor-set`, `cal-editor-save`, `cal-task-add`.

**Acceptance tests:** the 4 ported tests; view-model tests with fakes for the writer, store and availability: the editor's save builds the right `EventForm` for a new, a timed, an all-day, a recurring and a guest event; the dirty check; scope dialog flow (Occurrence, Following, All, Cancel) for edit and for delete; the custom recurrence editor produces the rule of the Rust tests and a summary equal to `Summary()`; alert rows round trip; Find a time hidden without
the capability and showing at most 5 suggestions with it; the task editor field mapping and the quick-add words; Tasks view filters, selection versus open, the day filter from the mini month; the import dialog's alerts prompt only when the file has alerts; every menu's item list equals `spec/menus.md` for each state; key-routing tests (the table to commands, plus the editor-open context); the automation scenario on FakeJmap (create a series, edit this and following, delete this occurrence,
add a task and tick it, import a fixture .ics, a reminder popup appears for an alert due in a minute on a fake clock). Screenshots as cal-views. `HookCallSiteTests` for the 3 hooks.

## 7. Integration, live checks and verify (§6.1 steps 3 to 7 of the PORT-PLAN)

The integrator (one agent, after the last stream merges) runs: `tools/build.ps1 test` and `tools/linux.ps1 test` green; `python tools/red-check.py --list` shows nothing left (a leftover is a stream's bug); `python tools/spec-check.py --milestone M4` exit 0; `tools/auto-run.ps1 smoke` on Windows and the VM smoke;
the calendar automation scenarios of cal-views and cal-ui; then the **live checks one agent at a time** on `<test-account>` and `<readonly-account>` as §2.5 lists (L-012, L-020..L-030), the Linux gate (Month and Week painting, time zones with the embedded tzdb, a reminder on the VM clock), and the screenshots. The milestone report `docs/milestones/M4.md` lists: what was proven,
what is **unproven and needs a human** (a real-pointer drag, a desktop reminder notification, printing on paper, a second login with calendars for "Copy to account", whether the user's server offers availability if L-029 could not run), the J-072 and G- rows closed, and every default of §9. No hand-test gate follows: the user may run HAND TEST 4 (PORT-PLAN: Agenda and Tasks with many tasks on
`<readonly-account>`; create a series, edit "this and following", accept an invitation, see a reminder fire on `<test-account>`) whenever they wish.

## 8. Order of work inside the milestone (summary)

`m4` created from `m4-plan` → four worktrees → S0 (cal-views' shell host, a few hours) and S1 (cal-data recurrence and zones, a day) while cal-edit does its pure rules and cal-ui does tasks → store and set arm → writer and editors → reminders, push, invitations, gestures → integrate. If cal-data is late, cal-edit keeps its pure rules and
cal-views paints occurrence literals; if cal-edit is late, cal-views and cal-ui keep a recording fake writer. The only hard serial dependencies: S0 before any App-head merge of cal-ui's views, S1 (recurrence) before cal-edit's `EventEditor`.

## 9. Decisions this plan took (defaults; Bulwark wins) (spec/decisions.md D-M4-01..16)

1. **Four streams, split by layer** (data and clock; writes and rules; the screen; the editors and tasks); the Tasks rules and view belong with the editors (cal-ui), the Agenda and its day filter with the screen (cal-views); the invitation banner and its RSVP belong to cal-edit (the data stream that already owns the answer call), not to the reader.
2. **JSCalendar stays JSON** and wall-clock values stay `DateTime`: no typed event model (D-M4-02).
3. **NodaTime and Ical.Net are added by the streams, not by the plan** (D-M4-03); Ical.Net is the oracle and the .ics reader, never the recurrence engine.
4. **Recurrence on the event's own wall clock**, expanded on the client, as Rust and Bulwark do (J-072 note: no `expandRecurrences`).
5. **Reminders fire for every login** from one scheduler on `TimeProvider`; the OS notification is M7's (D-M4-11).
6. **Webcal: LAN, localhost and credentials are blocked, http is warned** (D-M4-07, stricter than Bulwark on purpose); **the .ics import asks about alerts, default keep as Bulwark** (D-M4-09, overridden by the user's rule that Bulwark's behaviour wins).
7. **The all-day strip is capped at 3 rows with a toggle** (D-M4-08); **print is an in-app model** (D-M4-14).
8. **Find a time and availability only with the capability** (D-M4-13); L-029 records what the server does.
9. **Sharing a calendar (I-306) is M6**, contacts proper are M5 (the calendar reads cards through a replaceable reader, D-M4-06); "Copy to account" is built only when two logins have calendars.
10. **Calendar push uses the generic /changes engine** with a full-load fallback (D-M4-12); the drag rollback is the writer's.
11. **Shortcuts:** `a` and `k` are added to the calendar table, the routing is cal-ui's (D-M4-16).
12. **The Calendar app is available when the login has the Calendars capability**; without it the rail icon stays disabled (as the other not-yet apps).
13. **A drag with a real pointer, the desktop reminder notification and paper printing are "unproven, needs a human"**, never gates.

## 10. Risks

| Risk | Mitigation |
|---|---|
| The recurrence engine and Ical.Net disagree on an edge (day 31, bySetPosition with several byDay, DST gaps) | The differential test lists known differences with a decision each; Rust's engine is the port source and its 11 tests are the oracle of record |
| DST and zone bugs hide behind the machine's zone | Tests use named zones and explicit dates (§1 item 4); NodaTime's tzdb is embedded, so Linux behaves like Windows |
| Thousands of tasks or events freeze the Month, Agenda or Tasks view (the PARITY-ROUND2 crash) | Virtualised lists, a 5 000-task seed and a measured open time in the report, B-CAL-048 as a test |
| The Uno head paints the week grid slowly or drags badly | Gestures are tested as pure rules; the drag runs through automation on both heads; the report states what only a human can try |
| A calendar write reaches a real third party (invitations) | Live scripts invite only the test account; `sendSchedulingMessages` is asserted against FakeJmap everywhere else; the read-only account never writes |
| The webcal fetcher is used to reach the LAN | `FeedRules.Check` on every hop and on resolved addresses, tested with a fake resolver (D-M4-07) |
| Four streams contend for `ShellPage.xaml` and `ShortcutTables` | The two files have one stated owner each for the calendar parts (cal-views, cal-ui); the integrator resolves a textual conflict, never a behavioural one |
| cal-edit's `EventEditor` waits for cal-data's engine | S1 is the first merge of cal-data; cal-edit's pure rules and the set arm need nothing from it |
