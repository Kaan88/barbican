# Barbican native plugins

Drop a plugin DLL into `%APPDATA%\Barbican\plugins\`. It stays **off** until you switch it on under
Settings → Add-ons; only then is the library loaded (it runs code in Barbican's process, so enable only plugins you trust).
To show its name and hooks before it is enabled, put a `<dll name>.manifest.json` (or `manifest.json` when it is the only
DLL) next to it with the same JSON as `barbican_plugin_manifest`. Without it the plugin is listed by file name.
Each call has a time budget from the hook catalogue (`spec/plugins/hooks.csv`, 2 s for hooks not in it, at most 10 s);
`compose.check` holds the send with a warning if the plugin errors, panics or times out; a plugin that fails
3 times in a row is switched off until Barbican restarts. `email.details` and `compose.check` are aliases of Bulwark's
`onEmailOpen` and `onBeforeEmailSend`, and a manifest may list either name. A plugin is any dynamic library exporting this C ABI (UTF-8, NUL-terminated strings):

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
