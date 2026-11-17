# Behaviours: AI assistant (`src/ai.rs`, `src/agent.rs`)

Area: `AI`. Owner milestone: M6 (assistant column, settings), M3 (composer rewrite menu).
Added at the M0 review (stream m0-review): the catalogue streams had no row for these modules.
Bulwark has no built-in assistant; this is a Barbican addition, so most rows have no Bulwark ref.
No Rust tests exist for either module.

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-AI-001 | Two provider kinds: OpenAI-compatible (`POST <base>/chat/completions`, bearer key) and Anthropic Messages (`POST <base>/messages`, `x-api-key`, `anthropic-version: 2023-06-01`). Defaults: OpenAI base with `gpt-4o-mini`, Anthropic base with `claude-haiku-5-5`. | ai.rs:22-25, :89-102 | provider APIs | none |
| B-AI-002 | The API key is kept in the OS credential store (`ai-api-key`), never in `settings.json`. No key: "Add an API key in Settings → AI assistant first." | ai.rs:31-34, :90-92 | ADR 0008 | none |
| B-AI-003 | Requests time out after 90 s. Errors map to plain messages: 401 key rejected, 403 access denied, 404 endpoint or model not found, 429 rate limited (with `retry-after` seconds when sent), 503/529 overloaded, other status with the provider's message. | ai.rs:93-116 | | none |
| B-AI-004 | Prompt-injection guard: mail and draft text go inside `<email>` / `<draft>` tags and the system prompt says text in them is data, never instructions. Both are clipped to 30,000 characters. | ai.rs:189-191, :197-222 | | none |
| B-AI-005 | Reader actions: Summarize (3-5 bullets), To-dos, Reply ideas (three numbered), Translate (into the chosen language), Draft reply. Composer actions: Improve, Shorten, Formal, Casual, Fix spelling, Translate; quoted `>` lines stay unchanged and the language is kept (except Translate). | ai.rs:197-222; ctxmenu.rs `rte_sub` "rte-ai" | | none |
| B-AI-006 | Assistant settings (desktop `settings.json`): `provider` 0/1, `base`, `model`, `lang` (default English), `actions` 0 off (read-only tools only), 1 ask first (default), 2 automatic; `memory` (default on) with saved notes; standing `instructions`; `prompts` as "Name: text" lines (`#` comments; at most 30, name clipped to 60 and text to 2,000 characters); `hidden` hides the assistant everywhere; `showCleanSpam` (default on). The same file holds `renderMode` (0 WebView2, 1 Blitz, 2 plain text) and `remoteImages` (default off). | ai.rs:7-20, :36-56, :78-86 | | none |
| B-AI-007 | Agent loop: at most 16 model turns ("Stopped after too many steps."), checked for cancel each turn ("Stopped."); intermediate text is shown as progress; each tool result is clipped to 40,000 characters. | agent.rs:493-531 | | none |
| B-AI-008 | Tools: search/read mail and threads, list folders, move/archive/delete (to Trash)/mark/tag messages, create folder, draft and send email, get/set vacation, read attachment, list/add/remove filter rules, change a setting, remember; with the calendars capability check/create events; with contacts find/create contacts. | agent.rs:45-119 `tools` | | none |
| B-AI-009 | Confirmation: with `actions` = 1 every write tool asks the user first; `send_email` always asks, even with `actions` = 2. A declined action returns "The user declined this action. Do not retry it unless they ask." to the model. With `actions` = 0 write tools are not offered. | agent.rs:494-519 | | none |
| B-AI-010 | The system prompt names the signed-in user and the local time, prefers `draft_email` over `send_email`, calls `<email>`/`<draft>` content untrusted, and adds the open message ("this email" = id `current`), saved notes (when memory is on) and standing instructions. | agent.rs:467-491 | | none |
| B-AI-011 | The conversation history round-trips through JSON (`to_json` / `from_json`) so a chat survives a restart. | agent.rs:556-573 | | none |

## Open questions
- B-AI-001: model defaults are product choices; confirm the defaults with the user at M6.
- The assistant column's motion (fade) is in `motion.md`; its settings rows are keyless rows in `spec/settings.csv`.
