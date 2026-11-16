# Barbican native plugins

Drop a plugin DLL into `%APPDATA%\Barbican\plugins\` and restart Barbican. Enable or disable it under
Settings → Add-ons. A plugin is any dynamic library exporting this C ABI (UTF-8, NUL-terminated strings):

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
