# Behaviours: platform layer (B-PLAT)

Area: everything that differs between Windows and Linux: data folders, open_url, clipboard,
system light or dark, start at login, spell checking, secret storage, notifications, the email
view engine.
Rust sources (`rust-final`): `desktop/src/platform/mod.rs`, `platform/win.rs`,
`platform/hunspell.rs`, `rte/spell.rs`, `icon.rs`. The Linux side has never been compiled in
Rust (no GTK libraries on the machine; GAPS "Linux platform layer", G-090).

| ID | Rule | Source (Rust) | Bulwark ref | C# status |
|---|---|---|---|---|
| B-PLAT-001 | One table defines each platform service. Email view: WebView2 (Windows), WebKitGTK as a child window (Linux X11), Blitz where WebKitGTK cannot be used (Wayland). Secrets: Credential Manager / Secret Service. Theme: registry / XDG portal. Spell: ISpellChecker / hunspell. Notifications: WinRT toasts / freedesktop. | platform/mod.rs:1-10 (table) | n/a | pending (M8/M9) |
| B-PLAT-002 | Config and data folders: APPDATA and LOCALAPPDATA on Windows; XDG on Linux. Folder selection is tested. | commit 440ebb6; platform/mod.rs test `folders` (:265) | n/a | pending (M8/M9) |
| B-PLAT-003 | System light or dark: Windows reads `AppsUseLightTheme` via the Win32 API (not reg.exe); Linux reads the XDG portal `color-scheme`, then gsettings. | commit 440ebb6; platform/win.rs:8 `PERSONALIZE`; tests `portal_reply` (mod.rs:229), `gsettings_reply` (:237) | n/a | pending (M8/M9) |
| B-PLAT-004 | Windows registry reads do not write; a test reads without writing. | platform/win.rs test `reads_without_writing` (:131) | n/a | pending (M8) |
| B-PLAT-005 | Start at login is built as a plan: Windows HKCU Run value (`Software\Microsoft\Windows\CurrentVersion\Run`); Linux `~/.config/autostart`. The plan is tested. | platform/mod.rs:177 `AUTOSTART_ARG`, test `autostart_plans` (:244); platform/win.rs:7 `RUN_KEY` | n/a | pending (M8/M9); live L-051 |
| B-PLAT-006 | Spell checking on Linux is hunspell's ispell pipe (`hunspell -a`). Without hunspell, or without a dictionary for the language, the checker is absent and nothing is underlined. | platform/hunspell.rs header (lines 1-4); `create` returns None | n/a | pending (M9) |
| B-PLAT-007 | Hunspell -a answers are parsed (test `answers`); whole-word ranges are located in UTF-16 (test `locates_whole_words_in_utf16`). | hunspell.rs tests `answers` (:140), `locates_whole_words_in_utf16` (:150), `names` (:159) | n/a | pending (M9) |
| B-PLAT-008 | Spell checking has one interface on both platforms; Windows uses ISpellChecker in the writing language from settings, and results are cached per text. | rte/spell.rs header (lines 1-3); tests `tags` (:190), `utf16_ranges` (:197), `skips` (:206), `windows_checker` (:216) | Edge / WebView2 spell | pending (M3) |
| B-PLAT-009 | Secrets: Credential Manager on Windows, Secret Service (zbus) on Linux. | platform/mod.rs table (line 7); commit 440ebb6 | n/a | pending (M8/M9) |
| B-PLAT-010 | rfd (file dialogs) uses GTK 3 on Linux. | commit 440ebb6 | n/a | pending (M9) |
| B-PLAT-011 | Notifications: WinRT toasts with the app id and a group per account; freedesktop notifications with the same actions on Linux. | platform/mod.rs table line 10; notify.rs | mail-notifications.ts | pending (M8/M9) |
| B-PLAT-012 | The Windows app id that groups toasts is `Barbican.Mail`. | platform/win.rs:12 `APP_ID` | wix/main.wxs | pending (M8) |
| B-PLAT-013 | Taskbar unread overlay is a Windows-only call with a 32 px icon. | desktop.rs:187-210; platform/win.rs `set_overlay` | n/a | pending (M8) |
| B-PLAT-014 | Icons (tray, overlay, installer) are drawn from `ui/barbican.svg`. | commit 440ebb6 (icon.rs) | n/a | pending (M8) |
| B-PLAT-015 | The mailto `.desktop` registration (Linux) has never been compiled. | GAPS P0-5; PARITY-ROUND2 "Not done" (composer) | n/a | open: G-007 |
| B-PLAT-016 | Linux: the WebKitGTK view, Secret Service, portal colour scheme and the tray are unverified in Rust. | GAPS "Linux platform layer (M)" | n/a | open: G-090 |
| B-PLAT-017 | Linux packages: AppImage (self-updating) and .deb (notify only). | commits b7b4777, d4d2fad; PORT-PLAN M9 | packaging/ | pending (M9) |
| B-PLAT-018 | Single-instance and handoff socket behave the same on both platforms (instance file removed on clean exit). | commit 110ff28; PORT-PLAN M9 list | n/a | pending (M9) |
