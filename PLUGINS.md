# Barbican native plugins

Drop a plugin DLL into `%APPDATA%\Barbican\plugins\`. It stays **off** until you switch it on under
Settings → Add-ons; only then is the library loaded (it runs code in Barbican's process, so enable only plugins you trust).
To show its name and hooks before it is enabled, put a `<dll name>.manifest.json` (or `manifest.json` when it is the only
DLL) next to it with the same JSON as `barbican_plugin_manifest`. Without it the plugin is listed by file name.
Each call has a time budget from the hook catalogue (`spec/plugins/hooks.csv`, 2 s for hooks not in it, at most 10 s);
`compose.check` holds the send with a warning if the plugin errors, panics or times out; a plugin that fails
3 times in a row is switched off until Barbican restarts. `email.details` and `compose.check` are aliases of Bulwark's
`onEmailOpen` and `onBeforeEmailSend` (Bulwark's names are a reference vocabulary, not a compatibility promise), and a
manifest may list either name; the plugin is called with the name its manifest declares. Enabling a plugin pins its file
(path + SHA-256). A loaded library is never unloaded while Barbican runs, so updating a plugin DLL requires restarting
Barbican; a different file at a path already loaded this session is refused with "restart Barbican to load the new
version". A plugin is any dynamic library exporting this C ABI (UTF-8, NUL-terminated strings):

```c
const char* barbican_plugin_manifest(void);                      // {"api":1,"id","name","version","description","hooks":[...]}
char*       barbican_plugin_call(const char* hook, const char* input_json);  // returns JSON or NULL
void        barbican_plugin_free(char* s);                         // frees what barbican_plugin_call returned
```

## Hooks (API 1)

| Hook | Input | Output |
|---|---|---|
| `email.details` | full JMAP Email object (with `headers`) | `{"rows":[{"label","value","tone":"neutral\|success\|warning\|danger"}]}` shown under the sender |
| `compose.check` | `{"to","cc","bcc","subject","text","attachments":[names]}` | `{"warning":"…"}` holds the send once |

See `plugins/spam-score` for a complete Rust example (port of Bulwark's Spam Score plugin):
`cargo build --release` there, then copy `target/release/barbican_spam_score.dll` into the plugins folder.

---

## API v2 (design, ROADMAP Phase 5 step B1)

Status: B2 is built (the manifest checks, the consent dialog and `pluginGrants`, package install and uninstall:
`src/plugins.rs`, `src/plugin_package.rs`); the rest is design, and steps B3-B7 build it. Until B3/B4 an installed API 2
plugin is listed and consented to but not loaded. API 1 (above) keeps loading until Barbican
1.0 and is dropped at 1.0 (decided; see "Decisions" below), so API 1 plugins have until then to move to v2.

Bulwark 1.21.2 is the reference vocabulary, not a compatibility target. Hook, slot, host-method and permission names
are Bulwark's, so a port rewrites the logic, not the API. Which catalogue rows v2 takes is the `barbican_status` column
of `spec/plugins/{hooks,slots,host-api}.csv` (adopt / adapt / skip, with a `barbican_note`); the counts are in
`spec/plugins/README.md` and are pinned by `cargo test plugin_catalog`. References below are paths in a Bulwark 1.21.2
checkout.

### Principles

1. **Native code, JSON messages.** A plugin is a dynamic library with a small C ABI. Everything that crosses it is a
   NUL-terminated UTF-8 JSON string: no struct layouts beyond the host table, no callbacks into plugin closures, no
   pointers inside JSON.
2. **The host draws everything.** A plugin never gets a window, webview, iframe or Slint component. A slot takes a
   declarative view (rows, buttons, lists, forms) that the host draws with its own widgets, theme and accessibility.
3. **Sandbox-ready (open decision 1).** The only memory shared is the strings passed in a call, and only for the length
   of that call. Bytes go by handle. The same ABI can therefore run in a helper process or a WebAssembly runtime, with
   the host table backed by IPC, and a v2 plugin does not change.
4. **The UI never waits on a plugin.** v2 dispatch is asynchronous: the Slint event loop posts a message and carries on
   (showing "Sending...", a spinner in the slot and so on). This replaces API 1's blocking call and its 10 s cap.
5. **Permissions are a contract, not yet a wall.** In-process native code can do whatever the user can. The host
   refuses every host call, hook and slot outside the granted permissions, and the consent dialog says plainly that this
   limits what Barbican lets the plugin do, not what the program can do. A sandbox later makes it a boundary.

### Exported symbols

```c
// Required. Static manifest JSON (the same text as the sidecar manifest.json). Owned by the plugin, never freed,
// valid for the life of the process. "api": 2 selects this ABI; "api": 1 (or none) selects API 1 above.
const char* barbican_plugin_manifest(void);

// Required (v2). Called once after loading and consent, on the plugin's host thread. `host` stays valid for the
// life of the process; `init_json` is borrowed for this call only. Returns NULL on success, or an error reply
// envelope allocated by the plugin (freed with barbican_plugin_free); an error leaves the plugin off.
char* barbican_plugin_init(const BarbicanHost* host, const char* init_json);

// Required (v2). Every host -> plugin message (hooks, slot renders, actions, checks, cancels).
// `message_json` is borrowed for this call only. Returns a reply envelope, or NULL for "nothing to say".
char* barbican_plugin_handle(const char* message_json);

// Required. Frees a string this plugin returned from init or handle.
void  barbican_plugin_free(char* s);

// Optional (v2). Called once when the plugin is switched off or the app exits: stop threads, flush.
// The library is still never unloaded while Barbican runs (see the Safety notes in src/plugins.rs).
void  barbican_plugin_shutdown(void);
```

`barbican_plugin_call` is API 1 only; the host never calls it on a v2 plugin. The manifest is still read from the
sidecar before anything is loaded (as today); the library's own copy must agree with it.

### The host table given at load

```c
typedef struct BarbicanHost {
    uint32_t abi;        // 2
    uint32_t size;       // sizeof(BarbicanHost) in this host; fields are only ever appended
    void*    ctx;        // opaque; pass it back on every call
    // Synchronous host call: request and reply are envelopes (below). Never returns NULL.
    // The reply is host-owned: free it with free_str. Thread-safe; may block (network, a dialog).
    char*   (*call)(void* ctx, const char* request_json);
    // Fire-and-forget message to the host (a deferred result, a progress update).
    // Returns 0 when queued, negative when refused (-1 bad envelope, -2 queue full, -3 plugin off).
    // The plugin keeps ownership of its buffer; the host copies it before returning.
    int32_t (*post)(void* ctx, const char* message_json);
    void    (*free_str)(void* ctx, char* s);
    // A line in Barbican's log (0 debug .. 3 error). Never shown to the user; keep mail data out of it.
    void    (*log)(void* ctx, int32_t level, const char* text);
} BarbicanHost;
```

Every capability is a method name inside a `call` request, so new host methods never change the table. A plugin checks
`host->size` before it uses a field appended after v2.0.

### Envelopes

Host -> plugin (`barbican_plugin_handle`):

```json
{ "v": 2, "id": 1842, "type": "hook", "name": "onBeforeEmailSend", "account": "a1b2",
  "deadline_ms": 600000, "payload": { "to": ["..."], "subject": "..." } }
```

| `type` | Sent when | Reply `result` |
|---|---|---|
| `hook` | a catalogued hook fires (`name`; `payload` as in `hooks.csv`, changed as its `barbican_note` says) | the hook's result; ignored for observers |
| `render` | a frame-slot instance appears or its props change (`slot`, `instance`, `props`) | `{ "view": View }`, or null to draw nothing |
| `action` | a button, menu entry, list row or form in a plugin view, dialog or action slot is used (`slot`, `instance`, `target`, `values`, `context`) | optional `{ "view": View }` to replace the view, or `{ "close": true }` to close a dialog |
| `closed` | a dialog the plugin opened was closed by the user or the plugin (`slot`: `plugin-dialog`, `instance`: the dialog id) | none |
| `check` | a registered composer checker should check text (`composer`, `text`, `revision`) | `{ "annotations": [{ "start", "end", "tone", "message", "replacements" }] }` |
| `cancel` | a message passed its deadline or the user cancelled it (`ref`: that message's id) | none |
| `settings` | one of the plugin's settings changed (`values`) | none |

Plugin -> host reply (returned from `handle`; or, after answering `{"v":2,"id":1842,"deferred":true}`, sent later with
`post` as `{"v":2,"type":"result","id":1842,...}`):

```json
{ "v": 2, "id": 1842, "ok": true, "result": { "warning": "No attachment, but the text says 'attached'." } }
{ "v": 2, "id": 1842, "ok": false, "error": { "code": "internal", "message": "model file missing" } }
```

Plugin -> host call (`host->call`):

```json
{ "v": 2, "method": "storage.set", "args": ["lastScan", { "at": "2026-11-24T10:00:00Z" }], "in": 1842 }
```
```json
{ "v": 2, "ok": true, "result": null }
```

- `method` is the catalogue name from `host-api.csv` (Barbican's own additions are rows there too, noted "Barbican
  addition"). `args` is a JSON array in **the same order as Bulwark's
  `api.<method>(...)` arguments**, so ported code keeps its argument lists. A skipped or unknown method answers
  `unsupported`.
- `in` (optional) names the message being handled. Once that message is past its deadline or cancelled, calls made
  `in` it fail with `cancelled`; effects already made are not rolled back.
- `v` is the envelope version. Unknown fields are ignored both ways; a v2.x host only adds fields, methods and types.

Error codes (`error.code`): `bad_request` (malformed JSON, invalid UTF-8, wrong arguments), `denied` (permission not
granted, origin not allowed), `unsupported`, `not_found`, `quota`, `rate_limited`, `user_declined`, `timeout`,
`cancelled`, `busy`, `offline`, `disabled` (the plugin was switched off), `internal`. `message` is for logs; the host never shows it unasked.

### Memory and ownership

- Every string is NUL-terminated UTF-8. Whoever allocates frees: strings the plugin returns are freed by the host with
  `barbican_plugin_free`; strings the host returns from `call` are freed by the plugin with `host->free_str`.
- Arguments (`init_json`, `message_json`, `request_json`, the `post` buffer) are borrowed for the length of the call
  only. Copy what you keep. Neither side ever writes into the other's buffers.
- The manifest string is static and never freed.
- No pointers, host memory or OS handles travel in JSON. Bytes travel as **blob handles** (`"blob:7f3a..."`, opaque,
  valid for the session and only for the plugin that received them), read with `blob.read(handle, offset, length)`
  (base64, at most 1 MiB per read) and made with `blob.create(base64, mimeType, name)` (Barbican additions). One
  envelope is at most 4 MiB; anything larger goes by handle.
- Rust plugins `catch_unwind` at the boundary and C++ plugins must not throw across it: an unwind through `extern "C"`
  aborts the process before the host can catch it.

### Threading, deadlines and failures

- **One host thread per plugin** calls `init`, `handle` and `shutdown`, in order, never two at once and never
  re-entrantly, so those entry points need not be thread-safe. `host->call`, `post`, `free_str` and `log` are
  thread-safe and may be used from any thread the plugin starts. No host call ever waits for a message to the same
  plugin: anything that needs the plugin's answer (a dialog's buttons, a slot's actions) arrives later as a message, so
  a plugin blocked in `host->call` cannot deadlock its own queue.
- A message's deadline is the hook's catalogue `budget_ms` (Bulwark `lib/plugin-hooks.ts:62-72`: 5 s, intercepts 60 s,
  the send intercept and takeovers 10 min, blob upload 15 min); renders and actions get 5 s. Because the UI no longer
  waits, API 1's 10 s cap goes. **The deadline starts when the host calls `handle`, not when the message is queued.**
  Time the plugin spends waiting for the user in a host dialog (`ui.alert`, `ui.confirm`, `ui.prompt`, a permission
  request) does not count. **Hard cap:** no message is held more than 10 minutes in all (or its hook's budget when that is longer, the 15-min blob upload), counted from when it was
  queued (queue time, handling and user waits together; Bulwark `HELD_HOOK_DEADLINE_MS`, `lib/plugin-hooks.ts:79`). The
  user can cancel a long intercept from the UI; the plugin then gets `cancel`. For the pre-send check the send UI shows
  a pending state ("Checking...") with a Cancel button. Cancel stops the send and does not skip the check; the user
  may press Send again, which runs the check again.
- While a plugin is inside `handle`, its later messages wait in its queue. A message that waits longer than its own
  budget is dropped before it is delivered: its action goes on as on a timeout (below), but a dropped message does
  **not** count toward the failure limit, because the plugin never saw it. A plugin that wants concurrency answers
  `deferred` and works on its own threads.
- **Hung plugin.** When `handle` overruns the current message's deadline, that counts as one failure and the plugin is
  marked hung: every message queued or arriving while it is still inside that `handle` fails at once with timeout
  semantics (below), without waiting and without counting further failures. When `handle` returns, the mark is cleared
  and the queue runs again. If `handle` has not returned when the message hits the 10-minute hard cap, the plugin is
  switched off for the session (as for the failure limit), so a plugin that hangs cannot stay on.
- A missed deadline, error, panic or invalid reply means: observers are ignored; intercepts count as "go on"; transforms
  keep the original value; takeovers and `fail_closed` hooks (`onBeforeEmailSend`, `onComposeSend`) stop the action
  and tell the user. Several plugins on one hook run in enable order: transforms chain, the first intercept that answers
  `false` stops the action.
- The failure limit stays: 3 failures in a row (a deadline missed after `handle` was called, an error, a panic, an
  invalid reply; a hung plugin's at-once failures do not add to it) switch the plugin off for the session (Bulwark: 3 errors in 60 s, `lib/plugin-hooks.ts:13-14`). Host
  calls are rate-limited per plugin (50 per second by default: `rate_limited`).
- **Switching off** (by the user, the failure limit, the hung-plugin cap or app exit) takes effect at once: queued messages are dropped,
  every later `host->call` returns `disabled`, `post` returns -3, and deferred results are ignored. `shutdown` is
  called on the plugin thread once it is idle; the host never waits for it, so a plugin stuck in `handle` never gets
  `shutdown` that session (its library stays loaded, as today).
- Host-call timeouts follow Bulwark `lib/plugin-sandbox/api-timeouts.ts:3,9`: 30 s, network calls 120 s. Calls that wait
  for the user (`ui.alert`, `ui.confirm`, `ui.prompt`, `permissions.request`) are exempt; they are bounded by the
  10-minute hold cap of the message they are made in (10 minutes as well when made outside one).

### `init_json`

```json
{ "v": 2,
  "app": { "product": "Barbican", "version": "0.5.0", "os": "windows" },
  "plugin": { "id": "dev.example.spam-score", "version": "2.0.0" },
  "granted": ["email:read", "ui:email-details"],
  "httpOrigins": ["https://api.example.com"],
  "settings": { "threshold": 5, "mode": "warn" },
  "accounts": [{ "id": "a1b2", "kind": "own", "active": true }],
  "regional": { "locale": "en", "timeZone": "Europe/Berlin", "hour12": false, "firstDayOfWeek": 1 },
  "theme": "dark",
  "features": { "hooks": ["onEmailOpen"], "slots": ["email-details-section"], "methods": ["storage.get"] } }
```

`features` lists what this build actually fires, draws and answers (Bulwark's `api.host.features`), so a plugin can
degrade when a screen is not built yet. There is no data directory: a plugin that needs to keep things uses
`storage.*`, blobs or `secrets.*`, so it keeps working in a sandbox. Secret settings are not in `settings`.

### Manifest

`manifest.json` next to the library (read before loading), and the same JSON from `barbican_plugin_manifest`:

```json
{
  "api": 2,
  "id": "dev.example.spam-score",
  "name": "Spam Score",
  "version": "2.0.0",
  "author": "Example",
  "description": "Shows the server's spam verdict under the sender.",
  "homepage": "https://example.dev/spam-score",
  "minAppVersion": "0.5.0",
  "library": { "windows-x86_64": "spam_score.dll", "linux-x86_64": "libspam_score.so" },
  "permissions": ["email:read", "ui:email-details"],
  "optionalPermissions": ["http:fetch"],
  "httpOrigins": ["https://api.example.com", "https://*.example.org"],
  "hooks": ["onEmailOpen"],
  "slots": ["email-details-section"],
  "settings": {
    "threshold": { "type": "number", "label": "Warn above", "default": 5, "min": 0, "max": 20 },
    "mode": { "type": "select", "label": "Show", "default": "warn", "options": ["always", "warn"] },
    "apiKey": { "type": "secret", "label": "API key", "default": "" }
  },
  "locales": { "en": { "title": "Spam score" }, "de": { "title": "Spam-Wert" } }
}
```

Rules, checked at install (B2; Bulwark's are `lib/plugin-types.ts:129-207` and `lib/plugin-validator.ts`):
- `id`: 3-64 characters of `[a-z0-9._-]`, starting with a letter (so never `file:`). `version`: semver.
- `api`: 2. A higher major is refused ("needs plugin API 3").
- `hooks` and `slots`: catalogue names (the old aliases are accepted) whose `barbican_status` is not `skip`
  (`plugin_catalog::Support`). Each needs its catalogue permission in `permissions` or `optionalPermissions` (Bulwark
  `mayRegisterHook`, `lib/plugin-sandbox/permissions.ts:52`), or install fails naming the missing permission. A
  message for an undeclared hook or slot is never sent.
- `httpOrigins`: `https://host[:port]` or `https://*.host`; no paths, no `http:`, no IP literals, no bare `*`.
- `settings`: Bulwark's `SettingFieldSchema` (`lib/plugin-types.ts:209`: boolean, string, number, select; label,
  description, default, options, min, max) plus `secret`, kept in the OS keyring and read with `secrets.get`. The host
  draws them as Settings rows (`ui/settings-schema.json` kinds toggle, text, select) on the plugin's Settings page.
- Dropped from Bulwark's manifest: `entrypoint` (now `library`), `type`, `tier`, `frameOrigins`, `apiPostPaths`,
  `credentials` and the marketplace media. Unknown fields are ignored.

### Permissions

The names are Bulwark's `ALL_PERMISSIONS` (`lib/plugin-types.ts:1636-1722`: 72 with the assistant areas from
`lib/plugin-sandbox/assistant/*.types.ts`). `ui:observe` and `app:lifecycle` are implicit (`lib/plugin-types.ts:1735`).
High-risk ones come first and are marked in the consent dialog; the wording starts from Bulwark's
`lib/plugin-sandbox/permission-text.ts`.

| Bulwark permission | Barbican | Notes |
|---|---|---|
| `email:read`, `email:write`, `email:send` | same | `email:send` covers composer transforms and send intercepts |
| `email:blob-read`, `email:blob-write` | same, high risk | bytes by blob handle; `jmap.fetchBlob` no longer needs a privileged tier |
| `email:raw-send` | same, high risk | `jmap.submitRaw`, `sendRaw`, `importRaw`; the send takeover |
| `email:render-takeover` | same, high risk | returned HTML still goes through the mail-frame sanitiser |
| `email:forward` | same | the host asks once per address |
| `compose:open`, `compose:read`, `compose:write` | same | |
| `compose:check` | same, high risk | sees what the user types in every composer |
| `calendar:read`, `calendar:write`, `calendar:respond` | same | |
| `contacts:read`, `contacts:write`, `files:read`, `files:write` | same | |
| `identity:read`, `identity:write` | same | |
| `filters:read`, `filters:write` | same; `filters:write` high risk | a rule can redirect mail |
| `tasks:*`, `templates:*`, `vacation:*` (read and write) | same | |
| `settings:read`, `settings:write`, `settings:change` | same | keys are Barbican pref names |
| `security:read` | same | the Phase 3 Security page |
| `auth:observe`, `auth:emit`, `auth:impersonation-return` | same | never a token or password |
| `account:read`, `directory:read` | same | |
| `http:fetch` | same, high risk | https only, to `httpOrigins` only, and never to an IP literal. The host resolves the name once and refuses it unless every address is public (no private, loopback, link-local, CGNAT or metadata address, in any spelling), with one exception: a private, loopback or LAN host is allowed only when the manifest lists that exact host (or origin) in `httpOrigins` and the user has accepted it, named separately as a private host, in the consent dialog. That consent is per host and pinned like the rest of the grant; no wildcard covers a private address. Plain http stays refused, even for a declared LAN host. The host connects only to the checked addresses (no DNS rebinding). Every redirect (at most 5) is checked again against `httpOrigins`, the address rules and the private-host consent. No system proxy, no cookies, no credentials added |
| `http:post` | dropped | Bulwark's own `/api/*` routes; Barbican has no app server |
| `http:proxy` | dropped | Bulwark's server-side credential vault; use a `secret` setting |
| `crypto:full` | same, high risk | Stalwart public keys and encryption at rest (Phase 3); no execution tier attached, WebAuthn calls skipped |
| `keystore` | dropped | a native plugin uses the OS certificate store itself; revisit with a sandbox |
| `links:check` | same, high risk | sees every link the user opens |
| `admin:config` | same, high risk | Phase 7 item 3 |
| `ui:observe`, `app:lifecycle` | same, implicit | |
| `ui:notifications`, `ui:keyboard`, `ui:download-file`, `ui:commands`, `ui:message-list-tabs` | same | |
| `ui:toolbar`, `ui:context-menu`, `ui:navigation-rail`, `ui:app-page`, `ui:side-panel`, `ui:sidebar-widget`, `ui:settings-section` | same | |
| `ui:app-top-banner`, `ui:email-banner`, `ui:email-footer`, `ui:email-details`, `ui:message-list` | same | |
| `ui:composer-toolbar`, `ui:composer-sidebar`, `ui:calendar-action`, `ui:contacts`, `ui:contact-cryptokeys`, `ui:tasks`, `ui:files`, `ui:admin-page` | same | |

Every consent dialog starts with a fixed line instead of a permission: "This plugin is a program. It runs with your
Windows rights. The permissions below limit what Barbican lets it do, not what the program itself can do."

### Declarative views

A slot never runs plugin UI code. The plugin returns a **view**: a JSON tree the host validates and draws in Slint.

```text
View     = { "type": "panel", "title"?: string, "items": Node[] }
Node     = Text | Row | Badge | Button | Link | List | Form | Stack | Divider | Progress | Image
Text     = { "type": "text", "text": string, "style"?: "body"|"caption"|"heading"|"mono", "tone"?: Tone }
Row      = { "type": "row", "label": string, "value"?: string, "tone"?: Tone, "icon"?: Icon, "action"?: Id }
Badge    = { "type": "badge", "label": string, "tone"?: Tone, "icon"?: Icon }
Button   = { "type": "button", "id": Id, "label": string, "icon"?: Icon,
             "style"?: "primary"|"secondary"|"danger"|"link", "disabled"?: bool, "tooltip"?: string }
Link     = { "type": "link", "label": string, "url": https-URL }       // opened through the host's link rules
List     = { "type": "list", "id": Id, "empty"?: string, "items": [{ "key": Id, "title": string,
             "subtitle"?: string, "trailing"?: string, "icon"?: Icon, "badge"?: Badge, "tone"?: Tone }] }
Form     = { "type": "form", "id": Id, "fields": Field[], "submit"?: { "label": string }, "cancel"?: { "label": string } }
Field    = { "key": Id, "type": "text"|"textarea"|"number"|"toggle"|"select"|"date"|"time"|"secret",
             "label": string, "value"?: any, "options"?: [{ "value": string, "label": string }],
             "placeholder"?: string, "required"?: bool, "min"?: number, "max"?: number, "help"?: string }
Stack    = { "type": "stack", "direction"?: "vertical"|"horizontal", "items": Node[] }
Divider  = { "type": "divider" }
Progress = { "type": "progress", "value"?: number in 0..1, "label"?: string }     // no value: indeterminate
Image    = { "type": "image", "src": "blob:..." | "data:image/png;base64,..." | "data:image/jpeg;base64,...",
             "alt": string, "maxWidth"?: number }
Tone     = "neutral" | "info" | "success" | "warning" | "danger"
Icon     = a name from the host icon set (Bulwark's Lucide names where one exists); an unknown name draws a dot
Id       = 1-64 characters of [A-Za-z0-9._-]
```

Limits: depth 6, 200 nodes, 4 KiB of text per node, 256 KiB per view, images 256 KiB encoded and at most
2048 x 2048 pixels, read from the PNG/JPEG header before anything is decoded (no SVG). A view
over a limit is not drawn and counts as a failure. There is no HTML, Markdown, colour, font or size: tone and style are
all a plugin chooses, and all text is plain. API 1's `email.details` rows are already `Row` nodes.

Every interaction comes back as an `action` message. `target` is the button, row, list or form id; a list click puts
the row key in `values.key`; a form sends all its field values in `values`. The plugin may answer with a new view.

**Frame slot -> panel** (`email-banner`). When a message opens the host sends a `render`:

```json
{ "v": 2, "id": 77, "type": "render", "slot": "email-banner", "instance": "r-5521",
  "props": { "emailId": "M1a", "deviceType": "desktop" }, "deadline_ms": 5000 }
```

and the plugin answers:

```json
{ "v": 2, "id": 77, "ok": true, "result": { "view": { "type": "panel", "items": [
  { "type": "row", "icon": "shield-alert", "tone": "warning", "label": "Spam score 7.4",
    "value": "Above your threshold of 5" },
  { "type": "stack", "direction": "horizontal", "items": [
    { "type": "button", "id": "report", "label": "Report as spam", "style": "danger" },
    { "type": "button", "id": "details", "label": "Why?", "style": "link" } ] } ] } } }
```

A click on "Report as spam" arrives as
`{"v":2,"id":78,"type":"action","slot":"email-banner","instance":"r-5521","target":"report","context":{"emailId":"M1a"}}`.
A plugin can also push a new view to a live instance with `ui.setPanel(slot, view, instance)`.

**Action slot** (`reader-actions`). Actions are pushed once, as in Bulwark, with `ui.setActions`. The fields kept are
those of Bulwark's `PluginAction` (`lib/plugin-sandbox/actions.ts:10-77`: id, label, icon, order, mailboxRoles,
mimeTypes, accounts, hasKeyword, notKeyword, hasBadge, notBadge, danger, shortcut, checked, pressed, description,
badge, protects):

```json
{ "v": 2, "method": "ui.setActions", "args": ["reader-actions", [
  { "id": "translate", "label": "Translate", "icon": "languages", "shortcut": "Ctrl+Shift+T", "order": 10 },
  { "id": "to-task", "label": "Make a task", "icon": "check-square", "mailboxRoles": ["inbox"] } ]] }
```

A use arrives as
`{"v":2,"id":91,"type":"action","slot":"reader-actions","target":"translate","context":{"emailIds":["M1a"],"accountId":"a1b2"}}`.

**Data slot** (`calendar-day-header`, `mini-calendar-marks`). Data is pushed and the host draws it in its own style
(Bulwark `DayLabel`, `lib/plugin-sandbox/day-labels.ts:5-14`):

```json
{ "v": 2, "method": "ui.setDayLabels", "args": [[
  { "date": "2026-12-24", "text": "Holiday", "title": "Christmas Eve (office closed)" },
  { "date": "2026-12-31", "dot": true, "title": "Year-end close" } ]] }
```

`search-results` is the other data slot: the result of `onProvideSearchResults`, already plain data.

**Dialogs** (`ui.openDialog`, slot `plugin-dialog`) never block. `ui.openDialog({ "title": "Connect", "size":
"small", "view": View })` shows the dialog and returns at once with `{ "dialog": "d-12" }`. Its buttons and form
submits arrive as `action` messages with `slot` `plugin-dialog` and `instance` `d-12`; the plugin answers with a new
`view`, or with `{ "close": true }` to close it. When the dialog closes, by the user (Esc, the close button) or by the
plugin, a `closed` message follows. `ui.setPanel("plugin-dialog", view, "d-12")` replaces the view of an open dialog.
Bulwark's `ui.openDialog` resolves when the dialog closes; a port moves the code after that `await` into the `closed`
or `action` case. Only `ui.alert`, `ui.confirm` and `ui.prompt` block, because they need nothing from the plugin to
finish.

### Consent and storage

- **Install (B2).** A folder or ZIP (size, entry-count and path checks; Bulwark caps a plugin at 5 MB,
  `lib/plugin-types.ts:1742`) is unpacked under `plugins\<id>\` and listed as off. Nothing is loaded.
- **Consent.** Switching it on shows name, author, version, the fixed native-code line, every permission in plain words
  (high-risk first) and every `httpOrigins` entry under `http:fetch` (as Bulwark `lib/plugin-sandbox/consent.ts`);
  entries that are private, loopback or LAN hosts are listed apart, under their own heading, because they let the
  plugin reach machines inside the user's network.
  Accepting records `{ id, path, sha256, granted, origins, privateHosts, author, version, at }` in a `pluginGrants`
  pref, which replaces API 1's `pluginBindings`. For an installed package `sha256` covers the whole package (every
  file's path, size and bytes, in path order, at most 64 MB in all; a larger folder is not listed), not only its
  library. **A package with another hash is always off until the user says yes again.** Any file that appears in the
  package folder later (a plugin writing next to its library, Explorer's `desktop.ini`) changes the hash too, so a
  plugin writes only to `plugin-data\<id>\`. The hash is read again without any cache before the consent dialog and
  before the library is loaded (a file swapped between that read and the load is not caught); a package folder that is
  a link or junction, or holds an entry that is neither a file nor a folder, is not listed.
  Nothing of an old grant carries over to a package by another author, when either names no author, or when the
  grant does not record its author (written by Barbican 0.2.0 or an API 1 binding): the dialog lists everything, as
  for a first install, and says that what was accepted before does not count. For an update by the same (named) author
  the dialog names the old and new version and says "it asks for the same permissions as before" only when it asks for
  exactly the accepted set; otherwise (more, fewer or other) it lists everything it asks for and marks what is new,
  never just the difference.
  An install never replaces anything but a package with the same id, asks first when the new package's author differs
  from the installed one or either names none (and then forgets the old grant), refuses an id that a library dropped
  into the plugins folder already uses, and ids that end in `.dll`, `.so`, `.dylib` or `.json` are refused.
  Uninstalling removes the package's folder, its data and its update backups, and forgets only the grant that pins a
  file inside the package's own folder. Leftover staging folders are removed when the first Barbican of the profile
  starts; when an update stopped between its two renames, the newest backup that holds a readable manifest of that id
  is put back then and the other backups of it are removed.
  Text in an API 2 manifest with control, direction or zero-width characters is refused (the zero-width non-joiner
  and joiner, which Persian and Indic writing and joined emoji need, are allowed). An API 1 manifest (a sidecar or the
  library's own) is read as Barbican 0.2.0 read it, so a plugin that loaded then keeps loading: its text is cleaned
  and shortened for display instead, a number given as the version counts as text, and hooks that are not plain names
  are left out. Error texts are cut to 300 characters.
  An API 1 grant pins the library file (path and SHA-256) but not its sidecar manifest: a sidecar that is edited later
  can change the name and the hooks shown, not the code that runs.
- **Optional permissions** are asked for at run time with `permissions.request([...])` (a consent dialog; the answer
  is remembered; a Barbican addition). Every grant can be revoked under Settings -> Add-ons, which restarts the plugin
  with the smaller set.
- **Storage** (`storage.get/set/remove/keys`): per plugin and per login, as Bulwark
  (`lib/plugin-sandbox/storage-scope.ts`). Keys 1-256 characters, JSON values, 1 MiB per plugin per login (Bulwark
  `PLUGIN_STORAGE_QUOTA_BYTES`, `lib/plugin-sandbox/host-api.ts:425`), kept in `plugin-data\<id>\<login hash>.json`
  with atomic writes.
- **Blob store** (`upfiles.*`): per plugin and login, 16 MiB, kept under `plugin-data\<id>\`.
- **Account data** (`accountData.*`): the plugin's entry in the account's mailbox metadata, synced like the Phase 3
  settings and so shared by every device of the account.
- **Secrets** (`secrets.get/set/remove`, a Barbican addition): one OS keyring entry per plugin and login, never written
  to disk by Barbican, never in `init_json` or the log.
- **One wipe rule for all three** (storage, blob store, secrets). They are keyed by login (server + account), so
  Barbican profiles signed in to the same login share them. Signing out of a profile wipes a login's plugin data only
  when no other profile is still signed in to that login (Phase 0 item 5); removing a plugin wipes its data for every
  login at once. Account data lives on the server and is not wiped by either.

### Decisions

The owner's decisions on the questions v2 left open (2026-11-24):

- **Barbican-only host calls are kept:** `secrets.*`, `blob.read` / `blob.create`, `permissions.request`, and the
  `secret` settings type.
- **`http:fetch` and private hosts.** https only, no IP literals. A private, loopback or LAN address is refused unless
  the manifest declares that host (or origin) explicitly and the user consents to it in the consent dialog, which names
  the private hosts separately. The consent is per host and pinned like other consent; no wildcard covers private
  addresses. The connection goes only to the checked addresses (no DNS rebinding) and every redirect is re-checked,
  including the private-host consent. Plain http stays refused, even for a declared LAN host.
- **API 1 plugins** keep loading until Barbican 1.0 and are dropped at 1.0.
- **A slow pre-send check** (compose intercept): the send UI shows a pending state and the user can cancel. Cancel
  stops the send and does not skip the check; the user may press Send again.
- **Phone-layout hooks** (`onViewChange`, `onDeviceTypeChange`, `onMobileBack`) stay skipped.
- **`crypto:full` is kept**; WebAuthn stays skipped.
- **Package layout is fixed:** `plugins\<id>\` holds the manifest and one library per platform; plugin data lives in
  `plugin-data\<id>\`.

### Porting a Bulwark plugin

1. **Rewrite the code natively.** The JavaScript bundle becomes a dynamic library (Rust recommended; a helper crate
   with the envelope types and a `Plugin` trait is planned for B7). `activate(api)` becomes `barbican_plugin_init`
   plus a `match` on the message `type` in `barbican_plugin_handle`.
2. **Hooks.** `api.hooks.onX(handler)` becomes the name in the manifest's `hooks` plus a `hook` case. Payloads are
   Bulwark's, changed where `hooks.csv` `barbican_note` says (ids instead of DOM, row patches instead of mutated arrays,
   Barbican pref names). Results are the same JSON. Skipped hooks (phone layout, theme CSS) are gone.
3. **UI.** Each React component in a slot becomes a function that returns a view; `onClick` closures become ids
   answered in `action`. State lives in the plugin: send a new view when it changes. No CSS, no `frameOrigins`, no
   iframes; an embedded video or map becomes a link.
4. **Host calls.** `await api.x.y(a, b)` becomes `host->call({"v":2,"method":"x.y","args":[a,b]})`, which blocks the
   plugin's thread, not the app. Same names, same argument order. Skipped methods need another way: `http.post` and
   `proxy.*` become `http.fetch` to your own server with a `secret` setting; `keystore.*` and WebAuthn become the
   plugin's own OS calls; print themes go. `ui.openDialog` no longer waits for the dialog to close: handle its
   `action` and `closed` messages.
5. **Manifest.** Keep `id`, `name`, `version`, `author`, `description`, `permissions`, `httpOrigins`, `locales`, and
   `settingsSchema` as `settings`. Add `"api": 2`, `library`, `hooks`, `slots`. Drop `entrypoint`, `type`, `tier`,
   `frameOrigins`, `apiPostPaths`, `credentials`.
6. **Translations.** `api.i18n.t` has no host side: the plugin gets `regional.locale` in `init_json` and on
   `onLocaleChange`, and translates from its own `locales`.
7. **Bytes.** Files and attachments are blob handles read in chunks, not `File` or `ArrayBuffer` objects.
8. **Time.** Budgets are Bulwark's. Long work answers `deferred` and posts its result before the deadline.
