# M6b host API and executable-policy inventory

Codex — M6b foundation preparation. Source: local Bulwark v1.21.2. `<bulwark>` and `<csharp>` denote the respective source roots. This is an inventory and implementation contract, not an implemented host or a security review.

`spec/plugins/host-api.csv` enumerates 186 unique permission-table method names, matched to case labels in the corresponding dispatcher files; 17 are privileged-only. Four keystore calls and 27 assistant calls are included. Three assistant names also occur in the main table (`calendar.deleteEvent`, `vacation.get`, `vacation.set`); delegated dispatch takes precedence. `NO_STATIC_GATE` means the source table explicitly contains null, not that an unknown call is authorized. The C# references are family-level candidates, not evidence of a compatible bridge or complete method behavior. Methods without a verified adapter must fail closed.

The dispatcher rejects unknown names and checks the table and privileged tier before dispatch (`<bulwark>/lib/plugin-sandbox/host-api.ts:2067-2099`). `pluginHasPermission` has implicit observe/lifecycle permissions and a managed-legacy exception; keystore additionally needs user consent. The privileged set is not a substitute for bundle verification: tier requires managed plus `crypto:full` (`<bulwark>/lib/plugin-sandbox/tier.ts`), while M6b must also implement its own signature, trust and consent policy. Grant checks must be repeated at use time, scoped to active account and plugin, and deny unknown methods, properties and result shapes.

## Conditional checks beyond the static table

- `files.attach` needs `files:read` plus `compose:write` for an existing composer or `compose:open` for a new draft; `files.saveAttachments` needs both `files:write` and `email:read` (`<bulwark>/lib/plugin-sandbox/assistant/d.ts:355-370,506-515`).
- Composer checker calls additionally depend on checker type, owner, current permission and background registration (`<bulwark>/lib/plugin-sandbox/host-api-checker.ts:27-95`). UI actions and panels require the target slot's permissions; signing/encryption actions add crypto/raw-send checks (`<bulwark>/lib/plugin-sandbox/host-api.ts:2105-2115,2130-2134`). Calendar and task navigation use view-dependent permissions and rate limits (`<bulwark>/lib/plugin-sandbox/calendar-navigation.ts:21-53`; `open-task.ts:17-45`).
- HTTP fetch is constrained to declared origins, HTTPS, host and port matching; wildcard matching is one DNS label. Reference follows redirects and verifies the final URL: M6b should validate every redirect hop and reject private destinations. HTTP post has same-origin API path allowlisting and a deny list. Proxy credentials are separate from normal fetch; proxy buffering/upload and concurrency limits are in `<bulwark>/lib/plugin-sandbox/proxy-client.ts:19-22,47-52,139-180`. No raw JMAP client, general preference store or unrestricted network client is a plugin adapter.

## Hook selectors and result policy

The 218 hook names and primary permissions remain in the existing hook catalog. Apply per-call selectors in `<bulwark>/lib/plugin-hook-catalog.ts:50-68,185-187`: `onBeforeCalendarAlert` selects `tasks:write` when the `taskId` property exists and `calendar:write` otherwise; `onCalendarAlert` and `onCalendarAlertAcknowledge` select `tasks:read` or `calendar:read` by the same presence rule, including a present null. `onComposeFromLink` has an alternate `compose:write` registration permission and targeted routing (`:105`). Do not reduce these to a blanket any-of grant.

`<bulwark>/lib/plugin-sandbox/hook-payload.ts:8-27` removes outgoing attachment text excerpts without `email:blob-read`; lines 33-85 validate avatar result schemes and declared HTTPS origins, and lines 87-112 preserve composer identity and protect outgoing keywords without `email:write`. `<bulwark>/lib/plugin-hooks.ts:178-403` distinguishes observer results, intercept vetoes, value intercepts, transforms and takeovers; compose/send and upload have extended deadlines, send and takeover errors fail closed, and repeated errors disable the plugin. Treat these as separate result types, with bounded sizes and operation-specific schemas. The current CSV prose is not executable; a complete transitive payload/result schema and every call-site validation still require implementation-time extraction and tests. Until a schema exists, do not expose that hook.

## Slot projection

The following 58 rows are extracted from `<bulwark>/lib/plugin-slot-catalog.ts`; permissions are any-of within the array. Each property maps to its permission, while `null` means no additional property-specific grant after the slot check. Project objects per invocation and current grants; never hand an object reference to a plugin. `app-side-panel` context has the nested allowlist in `<bulwark>/lib/plugin-sandbox/assistant/f.types.ts:43-52`: event requires calendar:read; contact and selectedCount contacts:read; files, fileCount and folder files:read; page has no extra gate. The reference helper only projects one level; M6b must recursively validate typed nested values. `plugin-dialog` is `*` in the reference: M6b must bound this to a plugin-owned typed dialog model rather than expose arbitrary host objects. Unknown fields and slots fail closed.

| Slot | Kind | Permissions (any) | Allowed properties and gates | Source |
|---|---|---|---|---|
| `app-top-banner` | app | `'ui:app-top-banner'` | `{ username: 'account:read', serverUrl: 'account:read', impersonated: 'account:read', canReturn: 'auth:impersonation-return' }` | `<bulwark>/lib/plugin-slot-catalog.ts:52` |
| `plugin-dialog` | dialog | `'ui:observe'` | `'*'` | `<bulwark>/lib/plugin-slot-catalog.ts:53` |
| `command-palette` | app | `'ui:commands'` | `{}` | `<bulwark>/lib/plugin-slot-catalog.ts:54` |
| `app-side-panel` | app | `'ui:side-panel'` | `{ view: null, context: null, focusRequest: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:55` |
| `navigation-rail-bottom` | navigation | `'ui:navigation-rail'` | `{}` | `<bulwark>/lib/plugin-slot-catalog.ts:58` |
| `sidebar-widget` | navigation | `'ui:sidebar-widget'` | `{}` | `<bulwark>/lib/plugin-slot-catalog.ts:59` |
| `sidebar-section` | navigation | `'ui:sidebar-widget'` | `{ mailboxId: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:60` |
| `side-panel` | navigation | `'ui:sidebar-widget'` | `{ focusRequest: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:61` |
| `sidebar-section-actions` | navigation | `'ui:sidebar-widget'` | `{}` | `<bulwark>/lib/plugin-slot-catalog.ts:62` |
| `top-bar-actions` | navigation | `'ui:toolbar'` | `{}` | `<bulwark>/lib/plugin-slot-catalog.ts:63` |
| `app-page` | navigation | `'ui:app-page'` | `{ pageId: null, path: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:64` |
| `message-list-header` | mail-list | `'ui:message-list'` | `{ mailboxId: null, mailboxRole: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:67` |
| `message-list-empty` | mail-list | `'ui:message-list'` | `{ mailboxId: null, mailboxRole: null, searching: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:68` |
| `message-row-actions` | mail-list | `'ui:message-list'` | `{ emailId: 'email:read', accountId: 'email:read' }` | `<bulwark>/lib/plugin-slot-catalog.ts:69` |
| `message-bulk-actions` | mail-list | `'ui:message-list'` | `{ emailIds: 'email:read' }` | `<bulwark>/lib/plugin-slot-catalog.ts:70` |
| `context-menu-email` | mail-list | `'ui:context-menu'` | `{}` | `<bulwark>/lib/plugin-slot-catalog.ts:71` |
| `toolbar-actions` | mail-list | `'ui:toolbar'` | `{}` | `<bulwark>/lib/plugin-slot-catalog.ts:72` |
| `search-results` | mail-list | `'email:read'` | `{ query: 'email:read' }` | `<bulwark>/lib/plugin-slot-catalog.ts:73` |
| `reader-header` | reader | `'ui:email-banner'` | `{ email: 'email:read' }` | `<bulwark>/lib/plugin-slot-catalog.ts:76` |
| `reader-below-subject` | reader | `'ui:email-banner'` | `{ email: 'email:read' }` | `<bulwark>/lib/plugin-slot-catalog.ts:77` |
| `email-banner` | reader | `'ui:email-banner'` | `{ email: 'email:read', deviceType: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:78` |
| `reader-above-quote` | reader | `'ui:email-banner'` | `{ email: 'email:read' }` | `<bulwark>/lib/plugin-slot-catalog.ts:79` |
| `email-footer` | reader | `'ui:email-footer'` | `{ emailId: 'email:read' }` | `<bulwark>/lib/plugin-slot-catalog.ts:80` |
| `thread-summary` | reader | `'ui:email-banner'` | `{ threadId: 'email:read', emailIds: 'email:read', deviceType: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:81` |
| `reader-actions` | reader | `'ui:toolbar'` | `{ emailId: 'email:read', accountId: 'email:read' }` | `<bulwark>/lib/plugin-slot-catalog.ts:82` |
| `email-detail-sidebar` | reader | `'ui:email-details'` | `{ email: 'email:read', selection: 'email:read', listSelection: 'email:read', focusRequest: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:83` |
| `email-details-section` | reader | `'ui:email-details'` | `{ email: 'email:read', category: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:84` |
| `attachment-actions` | reader | `'email:read'` | `{ attachment: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:85` |
| `reader-selection-menu` | reader | `'ui:context-menu', 'ui:toolbar'` | `{ emailId: 'email:read', selection: 'email:read' }` | `<bulwark>/lib/plugin-slot-catalog.ts:86` |
| `attachment-menu` | reader | `'email:read'` | `{ emailId: 'email:read', blobId: 'email:read', name: 'email:read', type: 'email:read', size: 'email:read' }` | `<bulwark>/lib/plugin-slot-catalog.ts:87` |
| `composer-toolbar` | composer | `'ui:composer-toolbar'` | `{ insertText: 'compose:open', getSelectedText: 'email:send', composerId: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:90` |
| `composer-above-body` | composer | `'ui:composer-toolbar', 'ui:composer-sidebar'` | `{ draft: 'email:send', selection: 'compose:read', insertText: 'compose:open', getSelectedText: 'email:send', composerId: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:91` |
| `composer-below-body` | composer | `'ui:composer-toolbar', 'ui:composer-sidebar'` | `{ draft: 'email:send', selection: 'compose:read', insertText: 'compose:open', getSelectedText: 'email:send', composerId: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:92` |
| `composer-sidebar` | composer | `'ui:composer-sidebar'` | `{ draft: 'email:send', selection: 'compose:read', composerId: null, focusRequest: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:93` |
| `composer-sidebar-right` | composer | `'ui:composer-sidebar'` | `{ draft: 'email:send', selection: 'compose:read', composerId: null, focusRequest: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:94` |
| `composer-attachment-source` | composer | `'ui:composer-toolbar', 'ui:composer-sidebar'` | `{ onAttach: null, insertText: 'compose:open', composerId: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:95` |
| `composer-send-menu` | composer | `'ui:composer-toolbar'` | `{ draft: 'email:send', composerId: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:96` |
| `composer-attach-menu` | composer | `'ui:composer-toolbar'` | `{ composerId: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:97` |
| `composer-options-menu` | composer | `'ui:composer-toolbar'` | `{ composerId: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:98` |
| `composer-toolbar-actions` | composer | `'ui:composer-toolbar'` | `{ composerId: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:99` |
| `composer-header-actions` | composer | `'ui:composer-toolbar'` | `{ composerId: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:100` |
| `composer-selection-menu` | composer | `'ui:composer-toolbar'` | `{ composerId: null, selection: 'compose:read' }` | `<bulwark>/lib/plugin-slot-catalog.ts:101` |
| `calendar-event-actions` | calendar | `'ui:calendar-action'` | `{ eventData: 'calendar:read', setVirtualLocation: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:104` |
| `calendar-event-detail` | calendar | `'ui:calendar-action'` | `{ event: 'calendar:read' }` | `<bulwark>/lib/plugin-slot-catalog.ts:105` |
| `calendar-event-form` | calendar | `'ui:calendar-action'` | `{ event: 'calendar:read' }` | `<bulwark>/lib/plugin-slot-catalog.ts:106` |
| `calendar-day-header` | calendar | `'ui:calendar-action'` | `{ date: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:107` |
| `mini-calendar-marks` | calendar | `'ui:calendar-action'` | `{ date: null }` | `<bulwark>/lib/plugin-slot-catalog.ts:108` |
| `contact-cryptokeys` | contacts | `'ui:contact-cryptokeys'` | `{ cryptoKeys: 'contacts:read' }` | `<bulwark>/lib/plugin-slot-catalog.ts:111` |
| `contact-detail-section` | contacts | `'ui:contacts'` | `{ contact: 'contacts:read' }` | `<bulwark>/lib/plugin-slot-catalog.ts:112` |
| `contact-list-actions` | contacts | `'ui:contacts'` | `{ contactIds: 'contacts:read' }` | `<bulwark>/lib/plugin-slot-catalog.ts:113` |
| `task-detail-section` | tasks | `'ui:tasks'` | `{ task: 'tasks:read' }` | `<bulwark>/lib/plugin-slot-catalog.ts:116` |
| `task-list-actions` | tasks | `'ui:tasks'` | `{ taskIds: 'tasks:read' }` | `<bulwark>/lib/plugin-slot-catalog.ts:117` |
| `calendar-task-view` | tasks | `'ui:tasks'` | `{ filter: null, calendarIds: 'tasks:read' }` | `<bulwark>/lib/plugin-slot-catalog.ts:118` |
| `file-actions` | files | `'ui:files'` | `{ files: 'files:read' }` | `<bulwark>/lib/plugin-slot-catalog.ts:121` |
| `file-preview-section` | files | `'ui:files'` | `{ file: 'files:read' }` | `<bulwark>/lib/plugin-slot-catalog.ts:122` |
| `settings-section` | settings | `'ui:settings-section'` | `{}` | `<bulwark>/lib/plugin-slot-catalog.ts:125` |
| `settings-page` | settings | `'ui:settings-section'` | `{}` | `<bulwark>/lib/plugin-slot-catalog.ts:126` |
| `admin-plugin-page` | admin | `'ui:admin-page', 'admin:config'` | `{}` | `<bulwark>/lib/plugin-slot-catalog.ts:127` |


Action lists are bounded and validated (`<bulwark>/lib/plugin-sandbox/actions.ts:85-177`); list filters, pages and day labels have separate limits (`list-filters.ts:38-70`, `pages.ts:17-25`, `day-labels.ts:21-27`). Implement these as per-operation validators, not as a shared permissive dictionary. Automated tests should cover missing/revoked grants, selectors, nested projection, invalid result types, timeouts, origin redirects, account switches, ownership and tier boundaries.

Extraction confidence: all 186 literal permission entries matched case labels in their defining files, after deduplicating three equal entries; 58 slot declarations matched the catalog. This does not prove that nested helper checks or every hook payload schema have been fully catalogued. Resolve those gaps before exposing affected operations. No plugin host was built or executed as part of this inventory.
