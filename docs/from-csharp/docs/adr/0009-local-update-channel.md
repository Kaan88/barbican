# ADR 0009: Daily build with a signed local update channel

- Status: accepted (2026-11-21)
- Deciders: the user (owner), implemented on `m6b/auto-update`
- Supersedes for now: ADR 0003's "portable zip only" for the owner's own daily use (the zip is still the artifact)
- Related: ADR 0008 (profiles and secrets), `spec/behaviours/updater.md` (the Rust updater this reduces)

## Context
The owner now uses the Windows build every day on the real account, as alpha software, and wants each new build to reach the
installed copy by itself. Until now a run without `BARBICAN_PROFILE` used the profile `dev`, the same one dev builds use. The updates
come from a **folder on the same PC**: nothing is published and nothing touches the network.

## Decision
1. **A separate profile for the installed build.** `tools/install-daily.ps1` writes the marker `install-kind` = `daily` next to
   `Barbican.exe` (the same marker mechanism `packaging/*/install-kind` uses for msi/deb). `ProfilePaths.ChooseProfile` picks, in this
   order: `BARBICAN_PROFILE` when set (dev, smoke and automation runs name theirs), else `daily` for a `daily` marker, else `dev`.
   The daily profile is never `dev` and never the Rust app's folders; it is not an agent profile, so the read-only policy does not apply.
2. **Channel.** `tools/publish-daily.ps1` runs `build.ps1 publish` and `package-portable.ps1`, then writes into the channel folder
   (default `%LOCALAPPDATA%\Barbican-channel`) the zip, `manifest.json` `{version, commit, file, size, sha256, published}` and the detached
   signature `manifest.json.sig`. The version is `0.6.<git commit count>`, handed to MSBuild as `Version` and `ApplicationDisplayVersion`
   (no file is edited). The newest three zips are kept.
3. **Install.** `tools/install-daily.ps1` verifies signature and hash, unpacks the newest build into `%LOCALAPPDATA%\Programs\Barbican`
   (resolved at run time), writes the marker and, with `-Shortcut`, one Start menu shortcut. No registry, no admin rights.
4. **Updater in the app.** An installed daily build (marker present, not an AUTOMATION build, not a hidden run) checks the channel about five
   seconds after start and every 4 hours (`UpdateService`). A newer build that verifies is copied into the profile's `updates/staged/<version>/`
   with its manifest and signature, and the in-window toast "Update ready (version). Restart to update." appears with "Restart now".
   Settings > About shows the version, the state, the channel folder (editable) and "Check now". A restart (or the next start, at most twice per
   version) extracts the staged zip next to the install folder and starts the helper; the app closes through its normal window close path
   (settings and drafts are flushed).
5. **The helper** is Windows PowerShell passed as an encoded command (no script file to tamper with), every value in an environment variable
   (nothing is interpolated into the script). It waits for the app's process to exit, renames the install folder to `<install>.prev` (the
   rollback folder), renames `<install>.new` into place and starts the new build. The new build creates the file `updates/started` once its
   window is up. If that file does not appear within 60 seconds the helper kills the new build, renames the install folder to
   `<install>.failed`, puts `<install>.prev` back, writes `updates/staged/failed-<version>` (that version is never offered again) and starts
   the previous build again.

## Trust model
- The channel folder is **untrusted input**: anybody who can write to it can drop files there. Only a manifest that carries a valid
  **ECDSA P-256 / SHA-256** signature of the key embedded in the app (`UpdatePublicKey.Value`, a committed constant) is ever read further.
- The signature covers the manifest's exact bytes; the manifest holds the zip's size and SHA-256. The zip is hashed **while it is copied**
  into staging, so what is checked is what is kept. The zip name must be a plain `.zip` file name (no path, no drive, no traversal).
- The staging folder keeps the manifest and signature next to the zip, and **applying re-verifies all of it** (signature, size, hash, newer
  than the running version): a file is not trusted because it sits in the profile. Extraction refuses entries that leave the folder
  (zip-slip), caps entry count and size, and requires `Barbican.exe`.
- A signed manifest of an **older** version never installs (no downgrade by replay): the version must be strictly newer than the running build.
- The **private key** is generated once by `publish-daily.ps1` into `.local/update-signing-key.pkcs8.b64` of the main checkout (gitignored;
  a worktree finds it through `git rev-parse --git-common-dir`) and is never printed or committed. Whoever can read that file can sign
  builds the installed app will run: it is as protected as the user's profile, no more. The public key is rewritten into
  `src/Barbican.Core/Updates/UpdatePublicKey.cs` when the key changes (a rebuild with the new key must precede publishing).
- A failed verification installs and stages nothing, and is logged (`<data>/updates/update.log`) and shown in About.
- Hidden, smoke and automation runs never update: a profile that is an agent profile stays out of the updater, an AUTOMATION build is never
  eligible, and a hidden run is not eligible unless it is the headless self-test (`--update-selftest`, installed daily build in an agent profile).

## Rollback
The previous build stays as `<install>.prev` after every successful update (the next update replaces it). The automatic rollback is the
started-marker timeout above. A manual rollback is: close Barbican, rename `Barbican` to `Barbican.bad`, rename `Barbican.prev` to `Barbican`.
The profile is shared by both builds, so a build that migrates data forward is the one risk rollback does not undo.

## What this does not cover
- No code signing (Authenticode) of the executable; SmartScreen is not involved for a locally copied build.
- The helper trusts the extracted folder it is told about (same user, same machine): an attacker who can already write the user's
  profile and `Programs` folder can do anything the user can.
- A build that starts, writes the marker and then misbehaves is not rolled back.
- Windows only. Linux gets the AppImage and the MSI/AppImage updaters of ADR 0003 at M8 to M10.
