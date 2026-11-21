# Assistant add-on (reference native add-on)

The AI assistant is a **bundled native add-on** (decision D-M6b-08, user decision 2026-11-21), not part of the app core. It is also the
reference for writing one: it uses every host service a native add-on can get. Read it next to `plugins/Barbican.Plugin.SpamScore`, the smallest
possible add-on (one hook, no services).

A native add-on is trusted, full-trust .NET code: it runs in the app process, in its own collectible `AssemblyLoadContext`. It is **not** sandboxed
(see docs/FABLE-REVIEW.md, "The native plugin host is not a security boundary"). Imported packages need explicit consent per archive; bundled
add-ons ship with the app.

## Structure

```
plugins/Barbican.Plugin.Assistant/
  Barbican.Plugin.Assistant.csproj   references ONLY src/Barbican.Plugins.Abstractions (no Core, Presentation or UI types)
  AssistantPlugin.cs                 the entry point: manifest, settings rows, chat, InitializeAsync
  AssistantRows.cs                   the settings rows the host draws
  AssistantChat.cs                   the add-on side of the host's chat panel (IPluginChat)
  AssistantSession.cs, AssistantLoop.cs, AssistantClient.cs, AssistantPrompt.cs, AssistantHistory.cs, ...   the add-on's own logic
tests/Barbican.Plugin.Assistant.Tests/   tests against fakes of the host services (no host, no live provider)
```

## What a native add-on declares

| Piece | How | Here |
|---|---|---|
| Manifest | `IBarbicanPlugin.Manifest` (`PluginManifest`: api 1, id, name, version, description, hooks) | `AssistantPlugin.Manifest`, id `assistant`, no hooks |
| Start | `IBarbicanPlugin.InitializeAsync(PluginContext, ct)`, called once on the thread pool with a 60 s budget | builds the session from the context's services |
| Settings page | optional `IPluginSettingsProvider`: title, intro, `PluginSettingRow`s (text, multi-line text, choice, toggle, secret) | `AssistantRows` |
| Chat panel | optional `IPluginChatProvider`: an `IPluginChat` (turns, send, clear, changed) | `AssistantChat` |

The host draws the rows with its generic settings rows and the chat with its generic chat panel; the add-on has no UI code. A plugin class that
implements neither optional interface simply has no page, like spam score.

## Host services (`PluginContext`, all additive to API 1)

| Service | Interface | Used for |
|---|---|---|
| Per-account settings | `IPluginSettings` (`Read`, `SetAsync`, `Changed`) | the row values; text values per account, kept in the profile's desktop `settings.json` under `addons.<id>.<account>` |
| Secrets | `IPluginSecrets` | the API key (name `api-key`); scoped to the add-on id by the host and stored in the OS credential store, never in settings or backups |
| Outbound HTTP | `HttpClient Http` | the provider request; the host's client (no redirects, no cookies, infinite timeout: the add-on sets its own, 90 s here) |
| Mail operations | `IPluginHostTools` (`Specs`, `RunAsync`: JSON in, text out) | search and read mail and threads, folders, move/archive/delete/mark/tag, drafts and sends, calendar, contacts, vacation, filters, one setting; calendar and contact tools appear only on accounts that have them. Mail text returns inside `<email>` tags (untrusted data) |
| Confirmation | `IPluginDialogs` | "Allow assistant action?" before a write (Ask mode) and before every send; the host shows it in its own in-window dialog |
| Data folder | `PluginContext.DataDirectory` | the per-account conversation history |
| Hooks | `IPluginHooks` | not used here |

The add-on decides when to ask (action mode Off / Ask / Automatic, `send_email` always asks); the host only shows the dialog and runs the tool.

## Packaging

- `src/Barbican.App/Barbican.App.csproj` builds this project with `ReferenceOutputAssembly="false"` and copies the dll to `<output>/plugins/assistant/` on build and publish (the same way as spam score).
- The native host loads `plugins/<folder>/*.dll` (not `Barbican.Plugins.Abstractions.dll`) into its own load context, finds the single public `IBarbicanPlugin` with a public parameterless constructor, and starts it. It is **enabled by default**.
- Settings > Add-ons lists the bundled add-ons with Disable / Enable (the `disabledPlugins` pref); the change applies at the next start.
- An add-on distributed as a ZIP needs `native-plugin.json` (schema 1, SHA-256 per file) and the user's full-access consent; see M6b-plan.md.

## What the host keeps for the assistant

Pre-add-on data keeps working: `settings.json["assistant"]` is read until the add-on's first save (then copied to `addons.assistant`), the history
files in `<data>/assistant` are copied to the add-on's data folder, and the API key keeps its `ai-api-key` credential-store entry.
