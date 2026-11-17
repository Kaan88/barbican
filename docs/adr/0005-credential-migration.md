# ADR 0005: Secrets in the C# app and migration from the Rust app's Credential Manager entries (spike S5)

- Status: accepted (2026-11-17)
- Deciders: M0 spike S5 (agent `port-s45`)
- Plan sections: PORT-PLAN.md §3.5 (profiles), §3.8 (secrets row), §4.4 (profiles for automation)
- Evidence: `spikes/s5-credentials/read-format.ps1` (read-only, `CredEnumerateW` only, prints structure and never a value)
  and the Rust sources `src/oauth.rs`, `src/admin.rs`, `src/ai.rs` and `keyring 3.6.3` (`windows.rs`, `secret_service.rs`).

## What the Rust app stores (Windows)
The `keyring` crate v3.6.3 with `windows-native` writes **generic credentials** (`CRED_TYPE_GENERIC`):

| Field | Value |
|---|---|
| TargetName | `<user>.<service>`, with service `Barbican` |
| UserName | the keyring "user" (`session`, `accounts`, ...) |
| Comment | `keyring v3.6.3` |
| Persist | `CRED_PERSIST_ENTERPRISE` (3: roams with the profile when policy allows) |
| CredentialBlob | the secret string as **UTF-16LE, no terminator** (blob size is even) |

The four entries (all four exist on the dev machine; `cmdkey /list` shows them as `LegacyGeneric:target=<TargetName>`):

| TargetName | Written by | Content |
|---|---|---|
| `session.Barbican` | `oauth::remember` | JSON object, the last signed-in session, below |
| `accounts.Barbican` | `oauth::remember`, `forget_account` | JSON **array** of the same objects, most recently used first, de-duplicated by (user, server) |
| `stalwart-admin-key.Barbican` | `admin::set_key` | the admin API key as a plain string (trimmed) |
| `ai-api-key.Barbican` | `ai::Config::save` | the AI provider key as a plain string |

Session/account object, from `main.rs::remember_session` and the sign-in handlers:
- password sign-in: `{"kind":"password","server":<server>,"user":<user>,"pass":<password>}` (a one-time code is never
  remembered; with a TOTP code the session is not stored);
- OAuth/SSO sign-in: `{"kind":"oauth","server":<server>,"user":<user>,"refresh":<refresh token>,"endpoint":<token endpoint>,"client":<client id>}`.
Observed on the dev machine by the read-only probe: `session.Barbican` is a JSON object with the keys `kind, pass, server, user`
(kind `password`), `accounts.Barbican` is a JSON array of objects with the same keys, the other two are single opaque strings.
Passwords are therefore stored in clear inside the blob (protected only by the Windows credential store, as any
generic credential).

Not read by this spike, from the source only (unverified on a live Linux system): on Linux the crate writes Secret Service
items with the attributes `service=Barbican`, `username=<user>`, `target=default`, `application=rust-keyring`, in the default
collection, label `<user>@Barbican`-style (not asserted), value = the same UTF-8 strings.

## Decision
1. **The C# app never reuses the Rust target names.** It stores under its own names and profile, so a C# run can never
   overwrite or delete the Rust app's sign-in (standing rule: never sign in, sign out or clear the Rust app's credentials).
   Target name: `Barbican.Next/<profile>/<name>` with `<name>` in `session`, `accounts`, `admin-key`, `ai-key`, generic
   credential, `CRED_PERSIST_LOCAL_MACHINE`, UTF-8 JSON in the blob (the Rust UTF-16 choice is a `keyring` crate detail),
   written with `CredWriteW`/`CredReadW` through P/Invoke in `Barbican.Platform` (the WinRT `PasswordVault` is a different
   store and does not see generic credentials). Linux: libsecret with `schema=barbican-next`, `profile`, `name`.
   `BARBICAN_PROFILE=dev` or `auto-<port>` selects a separate set, so agents' runs never see the user's entries.
2. **Migration is an explicit, one-time, read-only import** ("Import accounts from the previous Barbican"), offered only for the
   default (non-dev, non-automation) profile and never run implicitly:
   - read `accounts.Barbican` (fall back to `session.Barbican`), decode UTF-16LE, parse JSON, map `kind`/`server`/`user`/
     `pass` or `refresh`/`endpoint`/`client` to the new account record; read `stalwart-admin-key.Barbican` and
     `ai-api-key.Barbican` as plain strings;
   - **write only new-format entries; never modify or delete the Rust entries** (the user may still run the Rust app);
   - a refresh token that has rotated since is the Rust app's problem; the import validates by a normal token refresh
     and shows "sign in again" when it fails, without touching the old entry;
   - if the blob is odd-length or not UTF-16/JSON, skip that entry and report which one (no values in logs).
3. The migration code and the Windows credential store wrapper land in M1 (`Barbican.Platform`, `ISecretStore`), with
   tests against a fake store; the real-store test uses a throwaway target name under `Barbican.Next/test-<guid>/` and
   deletes it. The S5 probe script stays as the read-only inspection tool.

## Consequences and open points
- A user who has the Rust app and the C# app side by side keeps two independent sign-ins until the import is run.
- The import must treat the blob as secret: zero the buffers (the probe does), never log it, never put it in telemetry or
  crash reports.
- Linux format of the Rust entries is unverified (no Linux Rust data was available); confirm on the VM at M2 or drop the
  Linux import (Linux users of the Rust app are expected to be none; it was a Windows-first app).
- `CRED_PERSIST_ENTERPRISE` entries can roam; the new local-machine persistence is a deliberate narrowing (secrets
  such as refresh tokens should not roam by default). Decide with the user whether roaming is wanted.
