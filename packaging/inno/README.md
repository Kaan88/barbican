# Windows installer (Inno Setup)

`barbican.iss` builds `Barbican-Setup-<version>.exe`, the installer Hub and Barbican's own updater
use. The MSI (`wix/`) is on hold: CI and the release build it only when the repository variable
`BUILD_MSI` is `true`.

## Build it

Needs [Inno Setup](https://jrsoftware.org/isinfo.php) 6 or 7 and a release build:

```powershell
cargo build --release
pwsh tools/build-inno.ps1 -Version 0.1.0          # dist\Barbican-Setup-0.1.0.exe
```

`-Version` is the release version without the `v` (`1.2.0`, `1.2.0-beta.1`). `-Exe` names another
`barbican.exe`; a stand-in file is enough to check that the script compiles. Straight with ISCC:
`ISCC.exe /DAppVersion=0.1.0 packaging\inno\barbican.iss`. CI compiles it on every push; the release
workflow builds it from the tag and signs it when the Authenticode secrets are set.

## What it does

- Per user: no administrator rights, installs to `%LOCALAPPDATA%\Programs\Barbican`, x64 only.
- Start menu shortcut, optional desktop shortcut, an entry in Windows' list of installed apps
  (uninstall key `{ACE04D99-4564-4E70-9A43-FE47A6A0E3B5}_is1`, never change the AppId).
- Registers the toast app id `Barbican.Mail` under HKCU (what the MSI did) so notifications carry
  Barbican's name and icon.
- Writes `install-kind` (`inno`) next to `barbican.exe`, so the updater knows this installer owns
  the files.
- Installing over an older version upgrades in place, keeping the folder. Barbican may sit in the
  tray with no window: the installer asks it to close, waits a few seconds, then ends it
  (only the exe in the install folder). The same for uninstall. Drafts are autosaved by the app.
- A silent install (`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`) shows nothing, starts nothing and
  exits 0. Interactive installs offer "Start Barbican" at the end.
- A license page appears only when the repository has a `LICENSE` (or `LICENSE.txt`) file at its
  root; there is none today, so there is no page. Choosing a license is the owner's decision.

## What it does not do

No file associations, no `mailto:` registration, no start at login: Barbican does those itself when
the user asks (`src/desktop.rs`, `src/mailto.rs`). Nothing system-wide. Uninstalling leaves the
user's mail data and settings (`%APPDATA%`, `%LOCALAPPDATA%\Barbican` data) alone.

## Updates

`Barbican-Setup-<version>.exe` is an asset of each release and of the signed update manifest
(`windows-x86_64-inno`, `src/release_manifest.rs`). An install with the `inno` marker downloads the
setup, checks its SHA-256 against the signed manifest, and when the user says so runs a hidden
PowerShell helper (`INNO_SCRIPT` in `src/updater.rs`): it waits for Barbican to exit, runs the setup
silently with `/CLOSEAPPLICATIONS`, and starts Barbican again.

Hub (`Kaan88/mdhub`) reads `hub.txt` from the latest release: see
`tools/write-hub-txt.ps1`, `tools/verify-hub-txt.ps1` and Hub's `docs/COMPLIANCE.md`. Release notes
come from `CHANGELOG.md` (`tools/changelog-notes.ps1`).
