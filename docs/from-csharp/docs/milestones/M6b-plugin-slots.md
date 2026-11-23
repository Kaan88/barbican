# M6b: add-on slot hosts and the mail hook bridge

Owner report: "I have Bulwark plugins loaded, but they don't show up in the UI at all." Root cause: the sandbox could start a slot frame, but only two screens drew one (the add-on dialog and the add-on page), and the mail hooks
of the app (`onEmailOpen`, `onNewEmailReceived`, `onAvatarResolve`) never reached a sandboxed add-on. This branch (`m6b/plugin-slots`) adds one shared slot host, places it at Bulwark's positions, and bridges the mail hooks.
Windows only (`#if WINDOWS`; the Skia head compiles empty placeholders, Linux verification is M9).

## What draws a slot

| Piece | File | Mirrors |
|---|---|---|
| Slot catalogue (kind, any-of permissions, props and the read permission each needs, inline, height limits, fillable) | `src/Barbican.Core/Plugins/Slots/PluginSlotCatalog.cs` (kept in step with `spec/plugins/slots.csv` by `PluginSlotPolicyTests.CatalogueMatchesTheSpecCsv`) | Bulwark `lib/plugin-slot-catalog.ts:50-128`, `lib/plugin-sandbox/slot-layout.ts:15-96`, `assistant/f.types.ts:60-68` |
| Policy: `CanMount` (any one of the slot's permissions), `ProjectProps` (drops props the catalogue does not name or the add-on may not read; the side panel's `context` per field) | `src/Barbican.Core/Plugins/Slots/PluginSlotPolicy.cs` | `lib/plugin-sandbox/permissions.ts:78-118` (`slotPropsFor`, `fieldsFor`, `mayOfferSlot`) |
| Offers: permission filter, first offer per slot, `fill` only in fillable slots, sorted by `order` then id | `PluginSlotOffers.cs` | `lib/plugin-sandbox/loader.ts:195-215` (`permittedOffers`), `components/plugins/plugin-slot.tsx` |
| Host props (`email` is `PluginMailProjection.ReadView`, named fields only, never raw HTML or tokens) | `PluginSlotProps.cs` | `email-viewer.tsx:2143-2175`, `email-list.tsx:752-757` |
| The host control | `src/Barbican.App/Views/Plugins/PluginSlotHost.cs` | `plugin-slot.tsx`, `plugin-iframe-slot.tsx:331-360` |
| Collapsible sidebar sections | `src/Barbican.App/Views/Plugins/PluginSidebarSections.cs` | `plugin-sidebar-sections.tsx:20-95` |
| `slot-should-show`, `props-update`, `theme-change`, `locale-change` host messages on a live session | `src/Barbican.App/Plugins/Sandbox/PluginSandboxSession.HostMessages.cs`, wired in `PluginSandboxRuntimeInstance` (`ShouldShowAsync`, `SendAsync`) and routed in `PluginSandboxSession.ReceiveAsync` | `lib/plugin-sandbox/host-bridge.ts:643-657`, `plugin-iframe-slot.tsx:485-487,535-545` |

`PluginSlotHost` lists the running add-ons through `PluginBackgroundRuntimes.Running()`, filters them with the policy, and for each offer (in order): asks the add-on's background instance `shouldShow` with the projected props when the
offer declared it (a failure, a timeout or a closed frame counts as "do not show"; the check reruns whenever the projected props change), starts one sandboxed slot frame through `PluginSlotFrames` (the add-on's own runtime, grant rechecked at
start, the same factory chain as the dialog and the page), sizes it from `slot-resize` (clamped per slot, inline slots take the reported width up to 480 px; fill slots stretch, shared fills keep 280 px), pushes `props-update` when the open message
changes instead of restarting the frame, pushes `theme-change` when the theme changes, and tears the frame down when the offer, the grant or the add-on goes away or the view unloads. A collapsed section mounts no frame. With no offer the host is `Collapsed` (zero layout cost).
At most two frames start at a time (every frame is a browser process set). The panel of a closing frame stays in the tree until its web view is closed.

Security properties are those of the approved dialog and page hosts (FABLE-REVIEW "add-on dialogs", "add-on pages"): one sandboxed frame per add-on and slot, the grant (any-of slot permission) rechecked at every start,
props only through `ProjectProps` (no prop the catalogue does not name, none without its read permission), no secret beyond what Bulwark passes, frames die with their binding. Not changed: the sandbox, the API dispatcher, `PluginSlotFrames.StartAsync`'s authority recheck.
`PluginSlotFrames.StartAsync` takes `permission: null` for "use the catalogue" (the dialog and the page still pass their one permission).

## Where each slot is placed

| Slot | Host site | Bulwark |
|---|---|---|
| `sidebar-section`, `sidebar-section-actions` | `Views/Sidebar/SidebarView.xaml` after the shared section (`PluginSidebarSections`; heading, chevron and name, fold per add-on for the session) | `plugin-sidebar-sections.tsx:20-95`, `sidebar.tsx:3090` |
| `sidebar-widget` | `SidebarView.xaml` after the sections | `sidebar.tsx:3091` |
| `app-top-banner` | `Shell/ShellPage.xaml` `TopStack`, above every screen; props `{username, serverUrl, impersonated, canReturn}` through the policy | `app-top-banner-slot.tsx:39`, `plugin-slot.tsx:106-140` |
| `message-list-header`, `message-list-empty` | `Views/List/MessageListView.xaml` after the list tabs; under the empty-state text | `email-list.tsx:1577,1632` |
| `reader-header`, `reader-below-subject` | `Views/Reader/ReaderView.xaml` top and end of the subject block | `email-viewer.tsx:5310,5848` |
| `email-banner` | `ReaderView.xaml` above the attachments (Barbican's own banners stay below them) | `email-viewer.tsx:6265` |
| `email-footer` | `ReaderView.xaml` below the body | `email-viewer.tsx:6529` |
| `email-details-section` | `ReaderView.xaml` end of the expanded details | `email-viewer.tsx:6225` |
| `toolbar-actions` | `ReaderView.xaml` toolbar row, before the add-on action buttons | `email-viewer.tsx:4812` |
| `email-detail-sidebar` | `ReaderView.xaml` right column (280 px, rule at its start, open until folded with the toggle button at the end of the toolbar row; one persistent frame across messages) | `email-viewer.tsx:2131-2262,4425-4470` |
| Already hosted before | `plugin-dialog`, `app-page`, `reader-actions`, `composer-header-actions`, `reader-selection-menu`, `calendar-day-header`, `mini-calendar-marks` | see FABLE-REVIEW |

`ReaderView.Slots.cs` builds the open message's props once per message and only while some add-on offers one of the reader slots.

### Not placed (follow-up, same host)

- Frames: `navigation-rail-bottom` (the 64 px rail, in the area the chrome workflow restyles), `side-panel` (needs `ui.setSidePanel`/`openPanel`), `app-side-panel` (needs a right column in each of Calendar, Contacts, Files and Settings; the per-field `context` policy is done), `context-menu-email` (a frame inside a transient menu: WebView start latency), `reader-above-quote` (needs an anchor in the body document), `thread-summary`, `attachment-actions` (one frame per attachment), and every composer, calendar, contacts, tasks, files, settings and admin slot (`spread-batch`: the same control, one site each).
- Actions (host-drawn buttons and menu entries, `PluginActionRegistration`): `top-bar-actions`, `message-row-actions`, `message-bulk-actions`, `command-palette`, `attachment-menu`, the composer menus. The allowlist in `PluginActionRegistration.Parse` still covers `composer-header-actions`, `reader-actions`, `reader-selection-menu` only; switching it to the catalogue is the next step.
- Data: `search-results` (needs the sandbox bridge for `onProvideSearchResults`).
- Open add-on APIs the slots depend on: `ui.setSectionTitle`, `ui.setSidePanel`/`openPanel`/`togglePanel`, `setListFilters`. The three installed add-ons call none of them.
- The fold state of a sidebar section and of the detail panel lasts for the session (Bulwark: `localStorage`); `locale-change` is wired in the session but no host sends it yet. `frame-escape` and the floating dropdowns of the UI kit are handled nowhere.

## Second batch (branch `m6b/plugin-slots-2`): composer, calendar, tasks, contacts, files, settings

Same host, same policy (`PluginSlotPolicy`, `PluginSlotOffers`, `PluginSlotFrames.StartAsync` are unchanged). New in Core: `PluginRecordProps` (the host props of a draft, an event, a task, a contact, a file, built with the projections `api.calendar` / `api.contacts` / `api.tasks` / `api.files` return to an add-on; the policy still drops every prop the add-on may not read).
Changed in the host: `HasOffers` reports what is on offer even while the host is inactive (a folded panel must still know it exists; the earlier code made the detail-panel toggle of the first batch vanish once folded), `ReleaseAsync` (stop and wait until every frame is closed and its panel is out of the host), `Clear` (empty a panel but keep host children in place), a host that leaves the tree keeps its live frames (closing a web view that already left the tree fails fast) but cancels a start under way, and picks them up again when it is back. After the Fable review (see [Plugin slots: Fable review](../FABLE-REVIEW.md#plugin-slots-fable-review)) `ReleaseAsync` stops and disposes every frame whether or not the host is in the tree, every owner releases before it drops or moves a subtree (`ReleaseAllAsync`, `ReleasedForMove`), and the props carry the login of their record (`SetProps(props, accountKey)`).

**The rule the second batch found (new, applies to every host).** A web view that is alive cannot be moved to another parent, and taking it out of the tree while it is visible fails fast (`E_INVALIDARG` stowed exception, the process ends). So a host that owners rebuild around is never moved: the contact pane, the file details and the event details keep it as a permanent child and insert the rebuilt content next to it; the calendar card holds one holder per slot, waits for `ReleaseAsync` of the old card's hosts before it swaps cards, and adopts the hosts into the kept card only (`CalendarCardHost.SwapCard`). Hosts of Settings sit in the add-on's card and are mounted only while the card is open.

| Slot | Host site | Bulwark |
|---|---|---|
| `composer-above-body`, `composer-below-body` | `ComposerView.xaml`: end of the header fields (above the editor) and above the footer; props refreshed 2 s after the last change, at once when the host appears (`ComposerView.Slots.cs`) | `email-composer.tsx:6128,6232,3626-3650` |
| `composer-toolbar`, `composer-attachment-source` | one row of inline frames above the footer, border-t | `email-composer.tsx:6396-6407` |
| `composer-sidebar`, `composer-sidebar-right` | 260 px columns left and right of the composer; a panel folds when the composer would drop under 560 px | `composer-plugin-sidebars.tsx:31-40,516-517` |
| `calendar-event-detail` | end of `EventDetailPanel` (events with a stored object) | `event-detail-popover.tsx:674` |
| `calendar-event-actions`, `calendar-event-form` | event editor card after Location and after Description (existing events only) | `event-modal.tsx:1598,1734` |
| `task-detail-section` | end of the task editor (existing tasks only) | `task-modal.tsx:403` |
| `contact-detail-section`, `contact-cryptokeys` | Bulwark contact pane: after the sections, and an "Encryption keys" section when the contact has keys and an add-on offers the slot | `contact-detail.tsx:442-448,510` |
| `file-preview-section` | Files details panel, one selected file | `file-details-panel.tsx:174` |
| `settings-section`, `settings-page` | inside the add-on's card on Settings > Add-ons, mounted only while the card is open | `settings-app.tsx:1009-1029` (Barbican has one fixed page list, so no Settings page of its own per add-on) |

Not offered: the function props (`insertText`, `getSelectedText`, `onAttach`, `setVirtualLocation`: they write into the open draft or event and need their own reviewed callback path), `draft.htmlBody` (empty: the editor keeps a document model), the occurrence of a recurring event in the event form (the stored event is projected), a new event or task (nothing stored to project), the Bento and Nordic contact panes (Bulwark pane only).

### Evidence (hidden run: Automation build, synthetic profile, private port, hidden capture; the add-on is `tests/fixtures/plugins/slot-probe`, synthetic code that draws its slot name and the props it received)

- Composer: above and below the editor, the plugin row, and the two 260 px panels (composer maximised) drew frames; the props were `composerId`, `draft`, `selection` (and `focusRequest` on the panels), the toolbar and the attachment source only `composerId`. Light and dark captures: `s2-cap/compose-max-light.png` and `compose-max-dark.png` in the session scratchpad (sample data only, not in Git). Dark was captured for the composer only; the frames re-themed live (`theme-change`).
- Calendar: `calendar-event-detail` on selecting a stored event, `calendar-event-actions` and `calendar-event-form` in the event editor, `task-detail-section` in the task editor; closing both editors with live frames (Esc) is clean (frames released, then the card swapped). Captures `cal-detail-light.png`, `cal-form-light.png`, `task-light.png` (light only).
- Contacts: the section followed the selection from contact 1 to 2 through `props-update` (no frame restart); `contact-light.png` (light only). The sample contacts have no encryption keys, so `contact-cryptokeys` is covered by `PluginRecordPropsTests` only.
- Files: the section followed the selection (`f1`, `f2`), went away with no selection; `file-light.png` (light only).
- Settings: expanding the add-on's card mounted `settings-section` and `settings-page` (frames reported 694x24); the card sits below the fold of the Add-ons page and the capture tooling cannot scroll, so there is no picture of it.
- Denied (same run, the add-on's manifest without `ui:composer-sidebar`, `ui:calendar-action`, `ui:contacts`, `ui:settings-section`, `email:send`, `compose:read`, `calendar:read`, `files:read`): `addon-zip slots` is empty for `composer-sidebar`, `composer-sidebar-right`, `calendar-event-detail`, `calendar-event-form`, `contact-detail-section`, `settings-section`, `settings-page`; `file-preview-section` is offered (it still holds `ui:files`) but its frame received no `file` prop (`denied-file.png`). `PluginRecordPropsTests` covers the draft, event, task, contact and file props without their read permission and the slot permissions.
- Unit: `PluginRecordPropsTests` (6).

### Open

`context-menu-email`, `reader-above-quote`, `thread-summary`, `attachment-actions`, `app-side-panel`, `navigation-rail-bottom` (the rail is in the area the chrome workflow restyles), `side-panel` (needs `ui.setSidePanel`), `calendar-task-view`, `admin-plugin-page` (no admin extension page exists yet, M7), every action slot (`PluginActionRegistration.Parse` still lists three), `search-results`, the composer menus; the contact-keys picture and a Settings picture; Linux (M9). `composer-above-body` and `composer-below-body` did not start in one denied run on a loaded machine (the 30 s start budget; the frame is retried when the props change). The first add-on start on a fresh profile can time out the same way (`addon-zip disable` and `enable` brings it up).

## Mail hook bridge (why Email Stats counted nothing and the Quick Notes banner never showed)

`src/Barbican.App/Plugins/PluginMailHookBridge.cs` (started with the add-on runtimes in `ShellPage.AddOns.cs`) registers one handler on the in-process hook bus per hook while some running add-on lists it: `onEmailOpen`
(`MessageLoader`), `onNewEmailReceived` (`MailSync`) and `onAvatarResolve` (`AvatarService`). Delivery is the fenced invocation the send hooks use (`IPluginFencedHookRuntime.InvokeHookWhileCurrentAsync`). Every payload names the login it came from (`EmailReadView.AccountKey`, `NewEmailHookPayload`, `AvatarRequest.AccountKey`); a payload without one reaches nobody. `PluginMailHookRouting.Hears` decides who hears it: `email:read` granted for that login (plus `http:fetch` for avatars), never while the app is locked, and the avatar hook not at all while add-on web requests are off. The grant and that login's session are rechecked at the write (Fable plugin-slots review 4, 5 and 8; the earlier "new mail only to add-ons that may read every account" rule is gone). Arguments are named fields (`PluginMailHookPayloads`: the same read view `api.mail.get` returns, without raw HTML). Quick Notes' banner `shouldShow` reads a cache that its `onEmailOpen` hook refreshes, so the banner needed this bridge as much as the slot host.

### Gravatar, end to end

1. The plugin (hook, `email:read` + `http:fetch`, origin `https://gravatar.com`) starts and lists `onAvatarResolve` (unchanged).
2. Before: the avatar service called `HookBus.TransformAsync` only when a handler existed, and a sandboxed add-on registered none, so the plugin was never asked. Second blocker: avatars are off by default (`senderFavicons`, `gravatarAvatars`), `AvatarRules.Get` returned null and queued nothing. Bulwark asks add-ons whenever an avatar shows (`avatar.tsx:305-314`).
3. Now: the bridge answers the hook; `AvatarPrefs.Plugins` (true while a handler exists) adds a `plugin:<address>` candidate per address (never a domain-wide key, so one sender's answer cannot be cached for the whole domain) and makes `Any` true; the reader header asks `TryGet` after the BIMI logo.
4. The answer is held to the add-on: kept only as an https address matching `grant.ApprovedHttpOrigins` while `http:fetch` is held (`PluginMailHookPayloads.AcceptedAvatarAddress`, Bulwark `hook-payload.ts:38-77`); the plugin's own `http.fetch` was already limited to its manifest origins (`PluginHttpFetch`, HEAD allowed, redirects rechecked, blocked by the no-third-party-traffic policy).
5. Evidence: the hidden run logged `onAvatarResolve` for the sender addresses of three opened sample messages and the plugin answered (undefined: sample addresses have no Gravatar); unit tests cover the answer guard and the candidate. Where the picture is drawn: only the reader header today; the message list and conversation cards draw initials only (no consumer of `AvatarService.TryGet` exists there: a separate parity gap, not part of this change).

## Evidence

Unit: `PluginSlotPolicyTests` (11: catalogue equals the CSV, any-of permission, denied cases, props dropped without the read permission, uncatalogued props never pass, side-panel context per field, offers filtered/ordered/once/fill, disabled grant, height limits), `PluginSandboxSlotMessagesTests` (4: should-show posted and answered, no answer and a stray id, unavailable before init, props/theme updates and refused types), `PluginMailHookPayloadsTests` (5: avatar answer guard, arguments, no raw HTML, the per-address candidate).

Hidden run (Automation build, synthetic profile `auto-*`, sample account, private desktop, no real input; the three real add-on packages were read from the scratchpad and installed with the test-only command `addon-zip`, never copied into the repository; the pinned bootstrap taken from `BARBICAN_BOOTSTRAP_DIR`):

- Email Stats (`sidebar-section`, order 10) draws "Email Stats" with Opened/Sent/Received in the mail sidebar (frame 255x60); its Opened counter went 3 to 4 when a message was opened.
- Opening a message shows the Quick Notes panel in the reader's right column (279 px, frame 279x168 before a message, 279x290 with one: the same frame, `props-update`, no restart). A note was typed into the frame through the DevTools protocol (no system input) and saved ("Saved 5:45:37 PM"); after opening another message and returning, the panel showed the saved note and the `email-banner` frame appeared ("This email has a note attached.", 360x37) above the attachments.
- Light and dark: switching the theme re-themes live frames (`theme-change`; the frame reports colour scheme light, background `#ffffff`).
- Denied: an add-on installed without `ui:email-banner` (Quick Notes with the permission removed from its manifest, scratchpad copy) and one without `ui:sidebar-widget` (Email Stats): no offer, no frame, even with a note present (`addon-zip slots` empty, `addon-zip frames` lists only the permitted frame). Disabling an add-on removed its frames (`frames` empty, state `enabled=False:running=False`), re-enabling brought the frame back.
- Captures are in the session scratchpad (sample data only), not in Git.

Checks: `tools/build.ps1` (both heads) 0 warnings 0 errors; `tools/build.ps1 test`, `tools/build.ps1 rules`, `python tools/spec-check.py`: see the result in the commit message of the final revision.

Known issues seen in the hidden run: the first add-on start on a fresh profile sometimes exceeds the 25 s background budget on a loaded machine (the start is retried on the next props change for slot frames; a background runtime that timed out stays down until re-enabled). A disable with a slot frame open once answered "cleanup remains pending" before the panel was kept in the tree until its web view closed; not seen after that change.

## For Fable (changes that run add-on code)

Bring: `PluginSlotHost`/`PluginSlotPolicy` (new consumer of `PluginSlotFrames`), the `slot-should-show` and host-message paths in `PluginSandboxSession`, and `PluginMailHookBridge` (new route from mail events to add-on code). Denied cases above; the per-login routing of the mail hooks; the avatar answer guard. The review and its fixes: [Plugin slots: Fable review](../FABLE-REVIEW.md#plugin-slots-fable-review).
