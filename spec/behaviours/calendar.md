# Behaviours: calendar (B-CAL)

Area: calendar, event editor, recurrence, reminders, agenda, tasks, webcal, birthdays.
Rust sources (read-only reference, tag `rust-final`): `desktop/src/calendar.rs`, `cal_editor.rs`,
`cal_extra.rs`, `recurrence.rs`, `reminders.rs`, `invite.rs`, `notify.rs`. Test names are the
Rust `#[test]` functions (ported into the C# suite as `[RustTest(...)]`, see rust-tests.csv).
Line numbers are from the Rust tree at `rust-final`. Rows are extracted from module headers,
doc comments, constants, tests and the commit log; the Opus sample review (PORT-PLAN §2.3 step 5)
is still to do for this area.

Status column: `pending (M<n>)` is the milestone that owns the row (PORT-PLAN §5).

| ID | Rule | Source (Rust) | Bulwark ref | C# status |
|---|---|---|---|---|
| B-CAL-001 | Calendars and events come from JMAP `Calendar` / `CalendarEvent`. Event reads request 34 fixed properties. | calendar.rs:19 `CAL`, :266 `EVENT_PROPS` | lib/calendar (JSCalendar) | ported |
| B-CAL-002 | Calendar colours are taken in order from an 8-colour palette; a calendar shared from another account gets a per-viewer colour. | calendar.rs:63 `PALETTE`; header (sharedCalendarColors) | components/calendar, sharedCalendarColors | ported |
| B-CAL-003 | The contact birthday calendar has the fixed id `__birthday-calendar__`. | calendar.rs:20 `BIRTHDAY_CAL` | lib/birthday-calendar.ts | ported |
| B-CAL-004 | Recurrence is expanded on the client, on the event's own wall clock (RFC 8984 4.3), with overrides and exclusions; a series is not expanded from the server. | recurrence.rs header; calendar.rs test `occurrences_carry_recurrence_ids_and_skip_tasks` (:709) | lib/calendar-recurrence (expansion fallback) | ported |
| B-CAL-005 | Occurrences carry recurrence ids; tasks are not expanded as events. | calendar.rs:709 | as B-CAL-004 | ported |
| B-CAL-006 | Agenda view covers 30 days from the start day. | calendar.rs:206 `AGENDA_DAYS` | calendar-agenda-view.tsx | ported |
| B-CAL-007 | Sidebar shows at most 6 tasks, then "Show all". Overdue tasks get a rose pill. | calendar.rs:752 `SIDEBAR_TASKS`; test `sidebar_tasks_capped_and_overdue` (:692); commit cbaa64a | tasks-sidebar-filters.tsx | pending (M4) |
| B-CAL-008 | Day and week columns follow the loaded range; a narrow pane shows 3 days. | calendar.rs test `day_columns_follow_range` (:702); commit d772e1f | calendar grid | pending (M4) |
| B-CAL-009 | Hour rows are 56 px tall in the day and week grids. | calendar.rs:18 `HOUR_PX` | calendar grid | pending (M4) |
| B-CAL-010 | Views: Day, Week, Month, Agenda, Tasks. Month timed events use the timed-event style; the grid scrolls freely. | calendar.rs header; commit 8b501d4, 5c6e668 | calendar view picker | pending (M4) |
| B-CAL-011 | Event editor fields: title, start and end (with end date), all day, time zone, repeat (custom recurrence editor), calendar, show as (free/busy), visibility, location, guests, alerts, description. | cal_editor.rs header | components/calendar/event-modal.tsx, recurrence-editor.tsx | pending (M4) |
| B-CAL-012 | Visibility values are public, private, secret. | cal_editor.rs:235 `PRIVACY` | event-modal.tsx | pending (M4) |
| B-CAL-013 | Editing one occurrence of a series asks the scope: this occurrence, this and following, or all. Patches are computed per scope. | cal_editor.rs test `scope_patches` (:1613); commit e81c242; header | recurrence-scope-dialog.tsx, lib/recurrence-scope-edit.ts | pending (M4) |
| B-CAL-014 | An occurrence override only carries the edited fields: title, description, locations, virtualLocations, freeBusyStatus, participants, alerts. | cal_editor.rs:366 `OVERRIDE_KEYS` | recurrence-scope-edit.ts | pending (M4) |
| B-CAL-015 | Edits keep ids, answers and unchanged fields. | cal_editor.rs test `edits_keep_ids_answers_and_unchanged_fields` (:1558) | event-modal.tsx | pending (M4) |
| B-CAL-016 | Organizer is detected from the event; scheduling messages are sent only for owned events with attendees. | cal_editor.rs test `organizer_detection` (:1596); commit d16233d ("calendar drag rollback + scheduling") | event-modal.tsx | pending (M4) |
| B-CAL-017 | Guest rules and alert rules and location are validated before save. | cal_editor.rs test `guests_rules_alerts_location` (:1532) | participant-input.tsx | pending (M4) |
| B-CAL-018 | Monthly "nth weekday" supports 1st to 4th and last (-1). | cal_editor.rs:900 `NTH`; recurrence.rs test `monthly_nth_and_last_weekday` (:470) | recurrence-editor.tsx | ported |
| B-CAL-019 | A monthly rule on day 31 skips months that do not have it. | recurrence.rs test `monthly_day_31_skips_short_months` (:484) | recurrence-rule.ts | ported |
| B-CAL-020 | UNTIL is inclusive; the start counts as the first occurrence. | recurrence.rs test `until_is_inclusive_and_start_counts` (:462) | recurrence-rule.ts | ported |
| B-CAL-021 | Weekly BYDAY with interval and count is expanded as specified. | recurrence.rs test `weekly_by_day_interval_count` (:454) | recurrence-rule.ts | ported |
| B-CAL-022 | Yearly rules pick a month and a day. | recurrence.rs test `yearly_in_month_on_day` (:492) | recurrence-rule.ts | ported |
| B-CAL-023 | Durations round-trip; DST changes keep the wall-clock duration. | recurrence.rs test `durations_round_trip` (:439); cal_editor.rs test `dst_aware_duration` (:1523) | recurrence.rs | ported |
| B-CAL-024 | Count left after splitting a series is computed on the split. | recurrence.rs test `count_left_after_split` (:533) | recurrence-rule.ts | ported |
| B-CAL-025 | Time zones convert to the user's clock; the zone search matches names. | recurrence.rs test `zones_convert_and_search` (:539) | time-zone-picker.tsx | ported |
| B-CAL-026 | English recurrence summaries follow Bulwark's buildRecurrenceSummary wording. | recurrence.rs test `summaries` (:515); recurrence.rs:4 | recurrence-editor.tsx `buildRecurrenceSummary` | ported |
| B-CAL-027 | A recurrence rule round-trips as JSON and sameness is judged on the rule, not on text. | recurrence.rs test `rule_json_round_trip_and_sameness` (:524) | recurrence-rule.ts | ported |
| B-CAL-028 | Reminders: an event fires its own alerts; when the event has none (or useDefaultAlerts is on) its calendar's defaults fire. | reminders.rs:105 `calendar_defaults`, :113 `effective_alerts`; test `effective_alerts_prefer_the_event_copy` (:573) | lib/calendar-alerts.ts | ported |
| B-CAL-029 | Alert changes keep unchanged alerts and do not copy calendar defaults into the event. | reminders.rs test `alerts_patch_keeps_unchanged_and_leaves_defaults` (:585); :157 `build_alerts_patch` | event-modal.tsx | pending (M4) |
| B-CAL-030 | Reminders are fetched 8 days ahead, so long default alerts are seen. | reminders.rs:24 `FETCH_AHEAD_MS` (8 * 86400000); test `fetch_ahead_covers_long_default_alerts` (:543); commit cbaa64a | use-calendar-alerts.ts | ported |
| B-CAL-031 | A reminder's state is stale after 10 minutes; calendars are refetched every 300 s. | reminders.rs:19 `STALE_MS`, :32 `REFETCH_SECS` | use-calendar-alerts.ts | ported |
| B-CAL-032 | Reminder alert key is `event:alert:fireMs`; acknowledged keys are stored per owner in calendar.json. | reminders.rs:202 `alert_key`, :297 `acknowledge`, :284 `state_path` | lib/calendar-alerts.ts | ported |
| B-CAL-033 | Reminders that came due while the app was closed (up to 7 days back, not acknowledged) are listed once in the missed-reminders dialog. | reminders.rs:21 `MISSED_WINDOW_MS`; :320 `missed`; test `missed_window` (:637) | missed-reminders-dialog.tsx | ported |
| B-CAL-034 | Due alerts for series and tasks are computed by `due_between`. | reminders.rs:220; test `due_alerts_of_series_and_tasks` (:613) | calendar-alerts.ts | ported |
| B-CAL-035 | Reminder notifications go to the desktop notifier when calendarNotificationsEnabled is on, with sound per calendarNotificationSound. | reminders.rs header; notify.rs:90 `reminder_note` | reminder-notifications.ts | deferred(M8): the scheduler raises Due with Sound; the notification service is M8 |
| B-CAL-036 | Task alert choices: none (-1), at due (0), 5, 15, 30, 60, 1440 minutes before. | cal_extra.rs:312 `TASK_ALERT_MIN` | task-modal.tsx | pending (M4) |
| B-CAL-037 | Agenda titles lose a duplicated "○" marker; the agenda day filter toggles. | cal_extra.rs test `agenda_titles_lose_marker_and_day_filter_toggles` (:943); commit 5c6e668 | calendar-agenda-view.tsx | pending (M4) |
| B-CAL-038 | Tasks: due-date quick dates; labels and filters; field mapping to JSCalendar Task. | cal_extra.rs tests `quick_task_dates` (:984), `task_labels_and_filters` (:997), `task_field_mapping` (:1015) | task-modal.tsx, tasks-sidebar-filters.tsx | pending (M4) |
| B-CAL-039 | Task progress toggles through CalendarEvent/set; "No due date" is shown for tasks without a due date. | commit 0ef6ba5; c5ec09e | task-list-view.tsx | pending (M4) |
| B-CAL-040 | Clicking a task selects it; opening it uses the editor (select vs open). | commit 5c6e668 | task-list-view.tsx | pending (M4) |
| B-CAL-041 | Webcal feeds: at most 5 redirects; body capped at 20 MB, checked while reading and on Content-Length. | cal_extra.rs:721 `FEED_MAX_BYTES`, :723-733 | lib/calendar-subscription-sync.ts | pending (M4) |
| B-CAL-042 | Feed refresh intervals are 15, 30, 60, 360 or 1440 minutes; the default is 60. | cal_extra.rs:606 `INTERVALS`, :658 default index 2 | ical-subscription-modal.tsx | pending (M4) |
| B-CAL-043 | Feed event UIDs are stable across refreshes and only missing UIDs are filled. | cal_extra.rs test `feed_uid_is_stable_and_fills_only_missing` (:959); commit cbaa64a | calendar-subscription-sync.ts | pending (M4) |
| B-CAL-044 | Feed events drop their remote alerts; a subscribed calendar does not ring. | cal_extra.rs test `feed_events_drop_remote_alerts` (:972); commit cbaa64a | calendar-subscription-sync.ts | pending (M4) |
| B-CAL-045 | Imported .ics data is cleaned before it is stored. | cal_extra.rs test `imports_are_cleaned` (:1058) | ical-import-modal.tsx | pending (M4) |
| B-CAL-046 | Birthdays: contact birthdays appear as all-day events in the birthday calendar. | cal_extra.rs test `birthdays` (:1031); header | lib/birthday-calendar.ts | pending (M4) |
| B-CAL-047 | Agenda parts (day heading, items) and the people search for guests are tested as pure functions. | cal_extra.rs tests `agenda_parts` (:1048), `people_search` (:1077) | calendar-agenda-view.tsx | pending (M4) |
| B-CAL-048 | Agenda and Tasks render only after the calendar state borrow is released (a nested RefCell borrow crashed the app). | commit 55de053 | n/a (Rust bug) | pending (M4); live check L-027 |
| B-CAL-049 | Find a time: working days 10 at most, 5 suggestions at most, 30-minute steps. Depends on Principal/getAvailability. | cal_editor.rs:1294-1317 `SEARCH_WORKING_DAYS`, `MAX_SUGGESTIONS`, `GRID_MINUTES`; test `free_slots_skip_busy_and_off_hours` (:1654) | find-a-time.tsx | pending (M4); see G-009 |
| B-CAL-050 | Free slots skip busy time and off-hours. | cal_editor.rs test `free_slots_skip_busy_and_off_hours` (:1654) | participant-availability.tsx | pending (M4) |
| B-CAL-051 | Invitation RSVP (Accept, Maybe, Decline) is sent as the login that owns the message, with sendSchedulingMessages in the same call; nothing is left on failure. | commit 4ad1e78; commit 645004f | calendar invitation banner | pending (M2 display, M4 RSVP); counter-proposal and remove-cancelled open: G-001, G-002 |
| B-CAL-052 | Day and week grids: events move and resize in 15-minute steps; a drop that changes nothing is skipped; a failed drop rolls back. | commits 8253677, d16233d | calendar drag and drop | pending (M4) |
| B-CAL-053 | Calendar header: the title gets smaller when the grid is narrow so dates stay readable. | commit 8f53ca7; 08199d2 | calendar header | pending (M4) |
| B-CAL-054 | Hover preview uses the panel date format; the summary and "Next up" use all events, not only the view. | commit 08199d2 | calendar hover preview | pending (M4) |
| B-CAL-055 | Settings wired to the grid: firstDayOfWeek, showWeekNumbers, calendarLimitHours, calendarDayEndHour, hide non-working days, showTimeInMonthView, calendarTintedEvents, 12-hour times. | commit e1886a6; c5ec09e | settings (calendar tab) | pending (M6) |
| B-CAL-056 | Calendar now line and 12-hour hour labels follow the setting. | commit c5ec09e | calendar grid | pending (M4) |
| B-CAL-057 | The sidebar calendar's colour can be changed; the default calendar cannot be deleted. | commit c5ec09e | calendar sidebar menu | pending (M6) |
| B-CAL-058 | The calendar's name is shown without the account suffix. | commit 08199d2 | calendar sidebar | pending (M4) |
| B-CAL-059 | Past events are dimmed; the selected chip gets a tint-fg outline; more-contrast uses the blue accent family. | commit 08199d2 | calendar grid | pending (M4) |
| B-CAL-060 | The grid has no scrollbar (Flickable) and the summary tiles are equal width. | commit 08199d2 | calendar grid | pending (M4) |
| B-CAL-061 | Webcal and calendar subscription fetches are bounded and report failures. | commit cbaa64a | calendar-subscription-sync.ts | pending (M4) |
| B-CAL-062 | A calendar's own alert more than 8 days ahead is not covered when its defaults do not reach that far. | PARITY-ROUND2 "Not done" (calendar) | n/a | open: G-005 |
