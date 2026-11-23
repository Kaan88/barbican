; Inno Setup script for Barbican (Windows x64). Per user: no elevation, nothing outside
; %LOCALAPPDATA% and HKCU. House style of the Hub apps (Hub's packaging/windows/hub.iss), and the
; contract in Hub's docs/COMPLIANCE.md: stable AppId, silent install exits 0 with no window, the
; installer waits for a running Barbican to close and never ends it, DisplayVersion is the version.
;
; Build (what CI and the release do; see packaging/inno/README.md):
;   ISCC.exe /DAppVersion=1.2.0 [/DExeSource=C:\path\barbican.exe] [/O<output dir>] barbican.iss
; AppVersion is the release version without the leading v: 1.2.0 or 1.2.0-beta.1.
;
; Does NOT register file associations, mailto: or start at login: the app does that itself, when
; the user asks (src/desktop.rs, src/mailto.rs). The only registry value it writes besides the
; uninstall key is the toast app id the MSI also wrote (src/platform/win.rs APP_ID), so
; notifications carry Barbican's name and icon. It does remove those app-written entries on
; uninstall (see [Registry]).
;
; It never ends a Barbican process: the app can need up to 15 s to write the open draft when it
; quits, and a force-kill would lose it. Instead setup waits, and gives up with a message when
; Barbican is still running (see StopBarbican below).

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
; Two setups at once (Hub, a double-clicked download and Barbican's updater) would write the same
; files: the second one waits for the first. The Global\ name also covers two users' sessions.
SetupMutex=BarbicanSetup,Global\BarbicanSetup
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
; The entries Barbican itself writes when the user turns them on, deleted with the program so none
; of them can point at an exe that is no longer there. Nothing is created here (ValueType none,
; dontcreatekey): only the uninstaller acts on these lines.
;   start at login: HKCU ...\CurrentVersion\Run value "Barbican"
;     (src/platform/win.rs RUN_KEY, src/platform/mod.rs autostart_plan)
;   mailto: registration: Software\Classes\Barbican.Url.mailto (src/mailto.rs PROG_ID),
;     Software\Clients\Mail\Barbican and the Software\RegisteredApplications value "Barbican"
;     (src/mailto.rs windows_plan / windows_unplan)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Barbican"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\Barbican.Url.mailto"; ValueType: none; Flags: uninsdeletekey dontcreatekey
Root: HKCU; Subkey: "Software\Clients\Mail\Barbican"; ValueType: none; Flags: uninsdeletekey dontcreatekey
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueType: none; ValueName: "Barbican"; Flags: uninsdeletevalue

[Run]
; Skipped by a silent setup (Hub, or Barbican's own updater, which starts the new exe itself).
Filename: "{app}\{#AppExe}"; Description: "Start Barbican"; Flags: nowait postinstall skipifsilent

[Code]
// Ask this install's Barbican to close its window and wait until the process is gone. Barbican
// writes the open draft when it quits (up to 15 s, src/desktop.rs quit_saving_draft), and with
// closeToTray its close button only hides the window, so the process may outlive the request.
// It is never force-killed: a kill would lose the draft. After 30 s of polling this returns a
// message and the caller stops the install (or the uninstall) instead of going on.
// Only the exe in {app} is asked: another copy, such as a development build, is left alone.
// PowerShell: the full path, not PATH, so a stray powershell.exe earlier on PATH cannot run.
// Returns '' when the way is clear, or the message to show and stop for.
function StopBarbican(): String;
var
  Exe, Script, Ps: String;
  Code: Integer;
begin
  Result := '';
  Exe := ExpandConstant('{app}\{#AppExe}');
  if not FileExists(Exe) then Exit;
  StringChangeEx(Exe, '''', '''''', True);
  // 60 x 500 ms = 30 s. 0: gone; 3: still running; anything else: could not tell.
  Script :=
    '$ErrorActionPreference = ''SilentlyContinue''; ' +
    '$e = ''' + Exe + '''; ' +
    'if (-not (Get-Process -Name barbican | Where-Object { $_.Path -eq $e })) { exit 0 }; ' +
    'Get-Process -Name barbican | Where-Object { $_.Path -eq $e } | ForEach-Object { [void]$_.CloseMainWindow() }; ' +
    'for ($i = 0; $i -lt 60; $i++) { ' +
      'Start-Sleep -Milliseconds 500; ' +
      'if (-not (Get-Process -Name barbican | Where-Object { $_.Path -eq $e })) { exit 0 } }; ' +
    'exit 3';
  Ps := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  if not Exec(Ps, '-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -Command "' + Script + '"',
    '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Code := -1;
  if Code = 0 then Exit;
  Log('Barbican is still running after 30 s (exit code ' + IntToStr(Code) + '); not ending it.');
  Result := 'Barbican is still running, so setup stopped instead of ending it and losing an open' + #13#10 +
    'mail draft. Close Barbican (right-click its tray icon and choose Quit), then start again.';
end;

// A non-empty string stops setup before it touches any file; a silent setup exits non-zero with no
// window. Either way Barbican keeps running, with the draft it saved.
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := StopBarbican();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Msg: String;
begin
  if CurUninstallStep = usUninstall then begin
    Msg := StopBarbican();
    if Msg <> '' then begin
      // Abort in usUninstall stops Uninstall, as Abort in Setup's PrepareToInstall stops Setup.
      if not UninstallSilent then MsgBox(Msg, mbError, MB_OK);
      Abort;
    end;
  end;
end;
