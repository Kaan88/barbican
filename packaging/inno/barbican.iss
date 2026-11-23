; Inno Setup script for Barbican (Windows x64). Per user: no elevation, nothing outside
; %LOCALAPPDATA% and HKCU. House style of the Hub apps (Hub's packaging/windows/hub.iss), and the
; contract in Hub's docs/COMPLIANCE.md: stable AppId, silent install exits 0 with no window, the
; installer stops a running Barbican itself, DisplayVersion is the version.
;
; Build (what CI and the release do; see packaging/inno/README.md):
;   ISCC.exe /DAppVersion=1.2.0 [/DExeSource=C:\path\barbican.exe] [/O<output dir>] barbican.iss
; AppVersion is the release version without the leading v: 1.2.0 or 1.2.0-beta.1.
;
; Does NOT register file associations, mailto: or start at login: the app does that itself, when
; the user asks (src/desktop.rs, src/mailto.rs). The one registry entry besides the uninstall key is
; the toast app id the MSI also wrote (src/platform/win.rs APP_ID), so notifications carry
; Barbican's name and icon.

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#define Dash Pos("-", AppVersion)
#if Dash > 0
  #define NumVer Copy(AppVersion, 1, Dash - 1)
#else
  #define NumVer AppVersion
#endif
#ifndef ExeSource
  #define ExeSource "..\..\target\release\barbican.exe"
#endif
#define AppName "Barbican"
#define AppExe "barbican.exe"
#define ToastId "Barbican.Mail"
; Only when the repository has a license file. None is chosen here: that is the owner's decision.
#define LicenseSource ""
#if FileExists(SourcePath + "..\..\LICENSE")
  #define LicenseSource "..\..\LICENSE"
#elif FileExists(SourcePath + "..\..\LICENSE.txt")
  #define LicenseSource "..\..\LICENSE.txt"
#endif

[Setup]
; The uninstall key is {ACE04D99-4564-4E70-9A43-FE47A6A0E3B5}_is1 (Hub's catalog names it). Never change the AppId.
AppId={{ACE04D99-4564-4E70-9A43-FE47A6A0E3B5}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Kaan88
AppPublisherURL=https://github.com/Kaan88/barbican
VersionInfoVersion={#NumVer}.0
VersionInfoProductName={#AppName}
VersionInfoDescription=Barbican setup
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
SetupIconFile=..\icons\barbican.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
; Barbican can sit in the tray with no visible window, which Restart Manager cannot always close:
; [Code] stops it too (Hub never ends a process, so the installer must).
CloseApplications=yes
CloseApplicationsFilter={#AppExe}
RestartApplications=no
OutputDir=..\..\dist
OutputBaseFilename=Barbican-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
#if LicenseSource != ""
LicenseFile={#LicenseSource}
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#ExeSource}"; DestDir: "{app}"; Flags: ignoreversion
; the updater reads this to know which installer owns the files (src/updater.rs detect_kind)
Source: "install-kind"; DestDir: "{app}"; Flags: ignoreversion
#if LicenseSource != ""
Source: "{#LicenseSource}"; DestDir: "{app}"; Flags: ignoreversion
#endif

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Mail, calendar and contacts"; AppUserModelID: "{#ToastId}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; AppUserModelID: "{#ToastId}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\AppUserModelId\{#ToastId}"; ValueType: string; ValueName: "DisplayName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\AppUserModelId\{#ToastId}"; ValueType: string; ValueName: "IconUri"; ValueData: "{app}\{#AppExe}"

[Run]
; Skipped by a silent setup (Hub, or Barbican's own updater, which starts the new exe itself).
Filename: "{app}\{#AppExe}"; Description: "Start Barbican"; Flags: nowait postinstall skipifsilent

[Code]
// Ask this install's Barbican to close its window, give it a few seconds, then end what is left
// (a tray-only process has no window to ask). Only the exe in {app}: another copy, such as a
// development build, is left alone. Mail drafts are autosaved by the app.
procedure StopBarbican();
var
  Exe, Script: String;
  Code: Integer;
begin
  Exe := ExpandConstant('{app}\{#AppExe}');
  if not FileExists(Exe) then Exit;
  StringChangeEx(Exe, '''', '''''', True);
  Script :=
    '$e = ''' + Exe + '''; ' +
    '$find = { Get-Process barbican -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $e } }; ' +
    '& $find | ForEach-Object { [void]$_.CloseMainWindow() }; ' +
    'for ($i = 0; $i -lt 10 -and (& $find); $i++) { Start-Sleep -Milliseconds 500 }; ' +
    '& $find | Stop-Process -Force -ErrorAction SilentlyContinue';
  Exec('powershell.exe', '-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -Command "' + Script + '"',
    '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopBarbican();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    StopBarbican();
end;
