# Behaviours: admin and dashboard (B-ADM)

Area: the Stalwart admin tab (read pages, queue, tasks, cluster, actions) and the dashboard
charts. The admin API key is entered by the user. Agents never read the key file (CLAUDE.md).
Rust sources (`rust-final`): `desktop/src/admin.rs`, `dashboard.rs`. Stalwart webui is the
chart reference (commit 7bf426e, "Charts follow stalwartlabs/webui").

| ID | Rule | Source (Rust) | Bulwark ref | C# status |
|---|---|---|---|---|
| B-ADM-001 | The Admin tab appears only after an admin API key is entered. The key lives in the OS credential store. | admin.rs:14 `key()`, :18 `set_key`; commit 5027a84 | Stalwart admin | pending (M7) |
| B-ADM-002 | Admin calls use JMAP capability `urn:stalwart:jmap`, object types prefixed `x:`, and `Authorization: Bearer` with the key or token. | admin.rs:1-3, :8 `CAP`; :32 `Admin::new` | Stalwart WebUI | pending (M7) |
| B-ADM-003 | Admin list pages: accounts, domains, delivery queue (delete only), DMARC reports, logs, tasks, cluster, actions. | admin.rs:98 `subs`, :107 `load`; commits 5027a84, bb42a58, 19f2628 | Stalwart WebUI | pending (M7) |
| B-ADM-004 | Tasks page: Scheduled, Failed and Index queue; task type labels come from a fixed table. | admin.rs:279 `TASK_TYPES`; test `task_rows` (:549); commit bb42a58 | Stalwart WebUI tasks | pending (M7) |
| B-ADM-005 | Cluster page: nodes, roles and the coordinator. | admin.rs test `nodes_and_roles` (:565); commit bb42a58 | Stalwart WebUI cluster | pending (M7) |
| B-ADM-006 | Actions are x:Action objects with a confirm step, a form and a result. The action list is fixed. Fields named in NULLABLE (mailFrom, message, authenticatedAs) may be null. | admin.rs:363 `ACTIONS`, :378 `form_fields`, :397 `NULLABLE`, :400 `action_payload`; tests `actions` (:574), `action_results` (:596); commit bb42a58 | Stalwart WebUI actions | pending (M7) |
| B-ADM-007 | The last action payload is kept in memory for the result view. | admin.rs:515 `LAST_SEND` | n/a | pending (M7) |
| B-ADM-008 | Deleting a queued message is the only write on the queue page. | admin.rs:503 `delete_queued`; commit 5027a84 | Stalwart WebUI queue | pending (M7); write tests FakeJmap only |
| B-ADM-009 | Domain names are shown for accounts; DMARC report fields are the real ones. | commit 19f2628 | Stalwart WebUI | pending (M7) |
| B-ADM-010 | Dashboard metrics come from `x:Metric` samples. Counter: summed per bucket. Gauge: averaged per bucket and carried forward across empty buckets. Histogram: sum over count. | dashboard.rs header (lines 1-5) | Stalwart WebUI | pending (M7) |
| B-ADM-011 | The % change compares the first and second halves of the window. | dashboard.rs header | Stalwart WebUI | pending (M7) |
| B-ADM-012 | Empty buckets are null: lines and single areas use connectNulls; stacked areas carry forward. Curves are monotoneX. | commit 7bf426e | Stalwart WebUI charts | pending (M7) |
| B-ADM-013 | Stacked series are stacked in legend order; rankings are sorted. | commit 7bf426e | Stalwart WebUI | pending (M7) |
| B-ADM-014 | Y axis: nice five-tick scale; x ticks sit at bucket centres; x ticks are thinned by label width on narrow charts. | commits 7bf426e, f1f33c5 | Stalwart WebUI | pending (M7) |
| B-ADM-015 | Hover: crosshair, series dots and a tooltip card that flips at the right edge; bar-band highlight; a bar-list tooltip sits below the hovered row (beside the cursor when there is no room). | commits 7bf426e, dc05a14 | Stalwart WebUI | pending (M7) |
| B-ADM-016 | A change that rounds to 0% shows a neutral grey "0%" with no arrow. | dashboard.rs test `zero_change_is_neutral` (:694); commit dc05a14 | n/a | pending (M7) |
| B-ADM-017 | Time labels and table timestamps follow timeFormat (12-hour or 24-hour). | dashboard.rs test `time_labels_follow_time_format` (:702); commit dc05a14 | timeFormat | pending (M7) |
| B-ADM-018 | Millisecond values under 10 ms keep two decimals; sub-millisecond ticks are distinct. | dashboard.rs test `sub_millisecond_ticks_are_distinct` (:712); commit dc05a14 | n/a | pending (M7) |
| B-ADM-019 | Cache tab: the stat and hit-rate cards are 110 px like every other tab. | commit dc05a14 | Stalwart WebUI cache | pending (M7) |
| B-ADM-020 | The charts use the app's neutral palette, not Stalwart's warm tones (PINK, TEAL, AMBER, BLUE, VIOLET). | dashboard.rs:12-17 `PINK`..`VIOLET`, :17 `PALETTE`; commit 6539798 | design tokens | pending (M7) |
| B-ADM-021 | Stat cards are tinted: the lead card is rose, a worsening card is amber; values use 28/800 type; the Clean mail card has a ham/spam meter. | commit d2e70a7 | design/mockups admin | pending (M7) |
| B-ADM-022 | Dashboard data is loaded per tab and range: `load(tab, range)`. | dashboard.rs:644 `load` | Stalwart WebUI | pending (M7) |
| B-ADM-023 | Admin loading is full width; a narrow window gives a single-column dashboard; settings search works on admin pages. | commit 3cbd790 | admin layout | pending (M7) |
| B-ADM-024 | The admin dashboard is read-only in the test plan; write paths are tested against FakeJmap only. | PORT-PLAN M7; CLAUDE.md (never test admin on the live server) | n/a | pending (M7) |
| B-ADM-025 | Admin page writes not in Rust: create and edit for accounts, groups, lists, domains (DKIM and DNS), API keys, OAuth clients, the settings editor, spam training, queue retry. | GAPS P3 (Admin) | Stalwart WebUI | open: G-080 |
