; setup.iss - the installer people download, Onsay-Setup.exe, made by release.ps1 with Inno Setup (open source, from
; jrsoftware.org), which passes Version, App (the published app), Launcher, Floor64 and Floor32. One installer for 32 and
; 64-bit Windows 7 and later: it puts him in %LocalAppData%\Onsay for this person alone, with no admin rights, as the
; launcher Onsay.exe, version.txt and app-<version> (src/System/Updates.cs), with the floor built for this computer,
; adds him to the Start menu and the desktop, and opens him. Removing him asks him to clean up first (--uninstalling).

[Setup]
; AppId names his entry in Programs and Features, Onsay_is1, which src/System/Updates.cs opens to remove him.
AppId=Onsay
AppName=Onsay
AppVersion={#Version}
AppPublisher=Onsay
AppPublisherURL=https://onsay.dev
DefaultDirName={localappdata}\Onsay
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=6.1sp1
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
SetupIconFile=onsay.ico
UninstallDisplayIcon={app}\Onsay.exe
UninstallDisplayName=Onsay
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
OutputDir=Releases
OutputBaseFilename=Onsay-Setup

[Files]
Source: "{#Launcher}"; DestDir: "{app}"; DestName: "Onsay.exe"; Flags: ignoreversion
Source: "{#App}\*"; DestDir: "{app}\app-{#Version}"; Excludes: "floor.exe,*.pdb"; Flags: ignoreversion recursesubdirs
Source: "{#Floor64}"; DestDir: "{app}\app-{#Version}"; DestName: "floor.exe"; Flags: ignoreversion; Check: Is64BitInstallMode
Source: "{#Floor32}"; DestDir: "{app}\app-{#Version}"; DestName: "floor.exe"; Flags: ignoreversion; Check: not Is64BitInstallMode

[Icons]
Name: "{userprograms}\Onsay"; Filename: "{app}\Onsay.exe"
Name: "{userdesktop}\Onsay"; Filename: "{app}\Onsay.exe"

[Run]
Filename: "{app}\Onsay.exe"; Description: "Open Onsay"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; The versions updates added, and version.txt, go with the rest.
Type: filesandordirs; Name: "{app}"

[Code]
// What Onsay needs that Windows 7 may lack: .NET Framework 4.8, and the C runtime update KB2999226, which the floor and
// the launcher run on. Both come from Windows Update. Missing either, it says so and opens where to get it.
function InitializeSetup(): Boolean;
var
  release: Cardinal;
begin
  Result := True;
  if not (RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', release) and (release >= 528040)) then
  begin
    if MsgBox('Onsay needs .NET Framework 4.8, which this computer doesn''t have yet. It comes free from Microsoft, through Windows Update or from its page.' + #13#10#13#10 + 'Open its page now?', mbInformation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet-framework/net48', '', '', SW_SHOWNORMAL, ewNoWait, release);
    Result := False;
  end
  else if not FileExists(ExpandConstant('{sys}\ucrtbase.dll')) then
  begin
    if MsgBox('Onsay needs a Windows update this computer doesn''t have yet, the Universal C Runtime (KB2999226). It comes free through Windows Update or from Microsoft''s page.' + #13#10#13#10 + 'Open its page now?', mbInformation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://support.microsoft.com/help/2999226', '', '', SW_SHOWNORMAL, ewNoWait, release);
    Result := False;
  end;
end;

// The version that runs, as version.txt names it, written once he is in.
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SaveStringToFile(ExpandConstant('{app}\version.txt'), '{#Version}', False);
end;

// Before his files go, the version that runs cleans up (Program.cs --uninstalling), started itself rather than through
// the launcher, since it stops everything running from his folder.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  version: AnsiString;
  code: Integer;
begin
  if (CurUninstallStep = usUninstall) and LoadStringFromFile(ExpandConstant('{app}\version.txt'), version) then
    Exec(ExpandConstant('{app}\app-') + Trim(String(version)) + '\Onsay.exe', '--uninstalling', '', SW_SHOWNORMAL, ewWaitUntilTerminated, code);
end;
