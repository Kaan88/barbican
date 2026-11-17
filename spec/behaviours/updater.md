# Behaviours: updater (B-UPD)

Area: the signed-manifest auto-updater, install kinds, pending updates, MSI install helper.
Rust sources (`rust-final`): `desktop/src/updater.rs`, `release_manifest.rs`,
`examples/release_manifest.rs` (release tool), `wix/main.wxs`, `.github/workflows/release.yml`
(reference only; the C# release workflow is manual-only, see docs/adr/0003). Bulwark refs:
`lib/desktop/types.ts`, `components/settings/desktop-update-settings.tsx`,
`components/app-update-prompt.tsx`.

Status: the Rust updater's download and install path has never run end to end (PARITY-ROUND2).
The C# port must run it (M8, hand test 8). Gaps and open config are G-087 and G-088.

| ID | Rule | Source (Rust) | Bulwark ref | C# status |
|---|---|---|---|---|
| B-UPD-001 | One manifest per channel: `stable.json` and `beta.json`, each with a detached signature `<channel>.json.sig` (base64 of the raw 64-byte Ed25519 signature over the file's bytes). | updater.rs:4-6 (header); `verify_manifest` :201 | desktop_updates | pending (M8) |
| B-UPD-002 | The public key (base64 of 32 bytes) and the feed address (with `{channel}`) are built into the app. A build without them never checks. | updater.rs:7-8; consts :20 `BUILT_PUBKEY`, :21 `BUILT_FEED`; `feed_url` :295 | desktop_updates | pending (M8) |
| B-UPD-003 | Automatic checks run every 4 hours. | updater.rs:25 `CHECK_EVERY` | desktop_updates | pending (M8) |
| B-UPD-004 | A check runs on a background thread; a second check while one runs is ignored. | updater.rs:328 `check` (BUSY) | n/a | pending (M8) |
| B-UPD-005 | `desktop.autoUpdate` defaults to on. | updater.rs:293 `auto_check` | settings desktop | pending (M8) |
| B-UPD-006 | Install kinds: msi, appimage, zip, deb, dev. Only MSI and AppImage can replace themselves; the others only say a version exists. | updater.rs:111 `can_install`; :115 `detect_kind`; :126 `current_kind`; test `kinds` (:656) | Bulwark desktop_updates | pending (M8) |
| B-UPD-007 | Install-kind markers (install-kind files) tell the updater what it may replace. | updater.rs:115 `detect_kind(exe, appimage, marker)`; commit b7b4777 | packaging/install-kind | pending (M8) |
| B-UPD-008 | Decision: a newer version installs on MSI or AppImage and notifies otherwise. On the stable channel, a pre-release install whose stable feed is older gets Older (no downgrade is offered). | updater.rs:321 `decide`; test `decisions` (:644); commit d4d2fad (beta gets stable) | channels | pending (M8) |
| B-UPD-009 | Version comparison orders pre-releases below their release. | updater.rs:37-58 `Version`; test `versions` (:629) | n/a | pending (M8) |
| B-UPD-010 | Freshness: a manifest is refused when its `expires` is 0 or past, or when it is older (by version or by `published`) than the one this install has accepted. | updater.rs:164 `check_fresh`; test `freshness` (:785); `Seen` :181-197 | n/a | pending (M8) |
| B-UPD-011 | The newest accepted manifest per channel is kept beside the downloads folder (the folder is deleted after install). | updater.rs:181 `seen_file`, :185 `load_seen`, :190 `save_seen` | n/a | pending (M8) |
| B-UPD-012 | Manifests carry `published` and `expires`; release manifests are valid for 180 days. | release_manifest.rs:33 `VALID_SECS`; test `manifest_freshness_fields` (:130); commit d4d2fad | n/a | pending (M8) |
| B-UPD-013 | A stable release writes and signs both stable.json and beta.json. | commit d4d2fad; release_manifest test `stable_release_reaches_beta_channel` (:140) | n/a | pending (M8) |
| B-UPD-014 | notes_url and asset URLs are https only; loopback is not allowed in release builds. | updater.rs:196 `https_only`; commit d4d2fad | n/a | pending (M8) |
| B-UPD-015 | The asset base URL is a repository variable (BARBICAN_UPDATE_ASSET_BASE); private-repo assets return 404, so a public mirror is needed. | commit d4d2fad; GAPS "Roadmap items still open" | n/a | open: G-088 |
| B-UPD-016 | A download goes to `<updates>/<version>/` and is written as `.part` then renamed. | updater.rs:391-400 `download`; `file_name` :381 | n/a | pending (M8) |
| B-UPD-017 | Downloads verify the SHA-256; a mismatch deletes the file. The download is checked against the manifest size; an oversize or truncated body is an error (test). | updater.rs:428 `sha256_file`; test `download_verifies_hash` (:813), `download_size_limits` (:843) | n/a | pending (M8) |
| B-UPD-018 | Timeouts: HTTP client 60 s; a download 30 minutes. | updater.rs:306-307 `http`; :398 | n/a | pending (M8) |
| B-UPD-019 | Install happens only after the user's OK: "Restart now" (install_now) or "Install next time I start" (approve_next_start). | updater.rs:500 `approve_next_start`, :528 `install_now`; commit 440ebb6 | app-update-prompt.tsx | pending (M8) |
| B-UPD-020 | pending.json paths must be a .msi or .AppImage inside the updates folder, with no shell characters. | updater.rs:557 `pending_path_ok`; test `pending_paths` (:764); commit d4d2fad | n/a | pending (M8) |
| B-UPD-021 | A pending update is discarded when its version is not newer than the running one or its hash no longer matches. | updater.rs:491 `start_action`; test `pending_round_trip_and_replace` (:734) | n/a | pending (M8) |
| B-UPD-022 | An approved update installs at start-up before the UI; the installer relaunches Barbican. | updater.rs:508 `on_start`; :565 `install` | n/a | pending (M8) |
| B-UPD-023 | An update installs only when no other Barbican runs (named mutex). | commit d4d2fad | n/a | pending (M8) |
| B-UPD-024 | MSI installs through a PowerShell helper that waits for Barbican's PID, takes paths from environment variables and relaunches after msiexec. | updater.rs:543 `MSI_SCRIPT`, :549 `msi_command`; tests `msi_cmd` (:750) | n/a | pending (M8) |
| B-UPD-025 | MSI ProductVersion is mapped from the tag: 1.2.0-beta.1 gives 1.2.301; 1.2.0 gives 1.2.999. | release_manifest.rs test `msi_versions` (:114); commit d4d2fad | wix | pending (M8) |
| B-UPD-026 | A replace of a running file goes through `replace_file`. | updater.rs:589 `replace_file` | n/a | pending (M8) |
| B-UPD-027 | Status text for the UI comes from one function over (state, kind). | updater.rs:601 `status_text` | update-banner.slint | pending (M8) |
| B-UPD-028 | A manifest built by the release tool verifies in the updater. | release_manifest.rs test `built_manifest_verifies_in_updater` (:155); updater test `signed_manifest` (:685) | n/a | pending (M8) |
| B-UPD-029 | Release workflow signing hooks (Authenticode, GPG) are behind secrets. | commit b7b4777 | n/a | open: G-087 |
