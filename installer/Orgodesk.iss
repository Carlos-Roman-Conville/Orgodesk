; Orgodesk installer (Inno Setup 6), from crc-appshell's installer template, adapted: Orgodesk is a paid app with its own
; legal texts, a tray app that hides the real desktop icons, and a data folder that holds the person's Orgos.
; Build with build-installer.bat (publishes the app, makes release\legal-txt, runs ISCC).
; Installs for the current user only (no admin prompt), into the same folder the app's own --install uses.
; Uninstall keeps %APPDATA%\orgodesk (layouts, presets, snapshots, settings) unless the person says yes to deleting
; it, and never touches HKCU\Software\Orgodesk (trial start and activation hash: a reinstall must not restart the trial).

#define AppName "Orgodesk"
#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#ifndef Suffix
  #define Suffix ""
#endif
#define AppExe "Orgodesk.exe"
#define DataDir "{userappdata}\orgodesk"

[Setup]
; Never change the AppId after the first release: Windows uses it to find the installed copy to upgrade.
AppId={{F3FD4A7E-5137-4FEA-BE79-9BE6ADF2C86C}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=CRC Solutions
AppContact=support@crc-solutions.org
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; A folder left by an earlier install or by the exe's own --install is the same program, so no "folder exists" question.
DirExistsWarning=no
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename={#AppName}-Setup-{#AppVersion}{#Suffix}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}
LicenseFile=..\release\legal-txt\EULA.txt
; Orgodesk is asked to exit first (PrepareToInstall): it puts the real desktop icons back as it leaves. Restart Manager
; only finds windows, and Orgodesk is a tray app, so this is the safety net for a copy that did not leave when asked.
CloseApplications=force
RestartApplications=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "startup"; Description: "Start {#AppName} when Windows starts"; GroupDescription: "Startup:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\release\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\release\legal-txt\EULA.txt"; DestDir: "{app}\legal"; Flags: ignoreversion
Source: "..\release\legal-txt\PRIVACY.txt"; DestDir: "{app}\legal"; Flags: ignoreversion
Source: "..\release\legal-txt\REFUNDS.txt"; DestDir: "{app}\legal"; Flags: ignoreversion
Source: "..\release\legal-txt\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}\legal"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Legal"; Filename: "{app}\legal"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; The choice is made by the app itself: it sets the Run entry and settings.json together (--autostart), because at its
; next start the app makes the Run entry follow settings.json.
Filename: "{app}\{#AppExe}"; Parameters: "--autostart on"; Flags: runhidden waituntilterminated; Tasks: startup
Filename: "{app}\{#AppExe}"; Parameters: "--autostart off"; Flags: runhidden waituntilterminated; Check: StartupNotChosen
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName} now"; Flags: nowait postinstall skipifsilent

[Code]
function StartupNotChosen: Boolean;
begin
  Result := not WizardIsTaskSelected('startup');
end;

// Asks a running Orgodesk to leave (it restores the real desktop icons itself), then waits for the file to be free.
procedure StopOrgodesk(const Exe: String);
var
  Code, I: Integer;
begin
  if not FileExists(Exe) then Exit;
  Exec(Exe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, Code);
  for I := 1 to 20 do
  begin
    if RenameFile(Exe, Exe) then Break;      // renaming onto itself fails while the exe is still running
    Sleep(250);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopOrgodesk(ExpandConstant('{app}\{#AppExe}'));
  Result := '';
end;

// A copy installed by the exe's own --install (zip users) left a Settings > Apps entry and a Start menu shortcut of
// its own: this installer replaces them, so there is one entry, not two.
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\Orgodesk');
    DeleteFile(ExpandConstant('{userprograms}\Orgodesk.lnk'));
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Exe: String;
  Code: Integer;
  Delete: Boolean;
begin
  Exe := ExpandConstant('{app}\{#AppExe}');
  if CurUninstallStep = usUninstall then
  begin
    StopOrgodesk(Exe);
    if FileExists(Exe) then
    begin
      Exec(Exe, '--restore', '', SW_HIDE, ewWaitUntilTerminated, Code);        // the real desktop icons, in case it was killed
      Exec(Exe, '--autostart off', '', SW_HIDE, ewWaitUntilTerminated, Code);  // no start entry left behind, in the Run key or in settings.json
    end;
  end;
  if CurUninstallStep = usPostUninstall then
  begin
    // The person's Orgos, presets, snapshots and settings are kept unless they say yes (or pass /DELETEDATA). A silent
    // uninstall keeps them.
    Delete := ExpandConstant('{param:DELETEDATA|0}') = '1';
    if (not Delete) and (not UninstallSilent) then
      Delete := MsgBox('Also delete your Orgos, presets, snapshots and settings?' + #13#10 + #13#10 +
        'Yes: delete ' + ExpandConstant('{#DataDir}') + #13#10 +
        'No: keep them, so installing Orgodesk again brings everything back.',
        mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
    if Delete then DelTree(ExpandConstant('{#DataDir}'), True, True, True);
  end;
end;
