; setup.iss - the installer people download, Onsay-Setup.exe, made by release.ps1 with Inno Setup (open source, from
; jrsoftware.org), which passes Version, App (the published app, his agent beside it), Launcher, Node64, Node32 and
; Splash. One installer for 32 and 64-bit Windows 10 and later, the oldest Node runs on, with nothing to click: it shows
; the splash, splash.png, with a bar under it while it puts him in %LocalAppData%\Onsay for this person alone, with no
; admin rights, as the launcher Onsay.exe, version.txt and app-<version> (src/System/Updates.cs), with Node for this
; computer in node\, adds him to the Start menu and the desktop, and opens him. Removing him asks him to clean up first (--uninstalling).

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
MinVersion=10.0
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableWelcomePage=yes
DisableFinishedPage=yes
SetupIconFile=onsay.ico
UninstallDisplayIcon={app}\Onsay.exe
UninstallDisplayName=Onsay
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
OutputBaseFilename=Onsay-Setup

[Files]
Source: "{#Launcher}"; DestDir: "{app}"; DestName: "Onsay.exe"; Flags: ignoreversion
Source: "{#App}\*"; DestDir: "{app}\app-{#Version}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs
Source: "{#Node64}"; DestDir: "{app}\app-{#Version}\node"; DestName: "node.exe"; Flags: ignoreversion; Check: Is64BitInstallMode
Source: "{#Node32}"; DestDir: "{app}\app-{#Version}\node"; DestName: "node.exe"; Flags: ignoreversion; Check: not Is64BitInstallMode
Source: "{#Splash}"; DestName: "splash.bmp"; Flags: dontcopy

[Icons]
Name: "{userprograms}\Onsay"; Filename: "{app}\Onsay.exe"
Name: "{userdesktop}\Onsay"; Filename: "{app}\Onsay.exe"

[Run]
Filename: "{app}\Onsay.exe"; Flags: nowait skipifsilent

[UninstallDelete]
; The versions updates added, and version.txt, go with the rest.
Type: filesandordirs; Name: "{app}"

[Code]
// What Onsay needs that an older Windows 10 may lack: .NET Framework 4.8, from Windows Update. Missing it, it says so and
// opens where to get it.
function InitializeSetup(): Boolean;
var
  release: Cardinal;
  code: Integer;
begin
  Result := True;
  if not (RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', release) and (release >= 528040)) then
  begin
    if MsgBox('Onsay needs .NET Framework 4.8, which this computer doesn''t have yet. It comes free from Microsoft, through Windows Update or from its page.' + #13#10#13#10 + 'Open its page now?', mbInformation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet-framework/net48', '', '', SW_SHOWNORMAL, ewNoWait, code);
    Result := False;
  end;
end;

// The splash in place of the wizard: a window as big as the picture, 560 by 340, with no border, no pages and no
// buttons, and the bar under "Getting him ready for you". Every control of the wizard's own is hidden, since a picture
// draws beneath them.
procedure InitializeWizard();
var
  splash: TBitmapImage;
begin
  ExtractTemporaryFile('splash.bmp');
  WizardForm.BorderStyle := bsNone;
  WizardForm.ClientWidth := ScaleX(560);
  WizardForm.ClientHeight := ScaleY(340);
  WizardForm.Position := poScreenCenter;
  WizardForm.OuterNotebook.Visible := False;
  WizardForm.Bevel.Visible := False;
  splash := TBitmapImage.Create(WizardForm);
  splash.Parent := WizardForm;
  splash.SetBounds(0, 0, WizardForm.ClientWidth, WizardForm.ClientHeight);
  splash.Stretch := True;
  splash.Bitmap.LoadFromFile(ExpandConstant('{tmp}\splash.bmp'));
  WizardForm.ProgressGauge.Parent := WizardForm;
  WizardForm.ProgressGauge.SetBounds(ScaleX(216), ScaleY(230), ScaleX(192), ScaleY(6));
end;

// Any page but the installing itself is gone through at once, like the Welcome and Ready pages Inno Setup shows even when
// told not to. Next is pressed by a message posted to it, so the press comes once the wizard has finished showing the
// page, since one pressed while it still is counts for nothing. Its buttons are moved out of sight rather than hidden,
// since a hidden button can't be pressed.
procedure CurPageChanged(CurPageID: Integer);
begin
  WizardForm.BackButton.Left := -ScaleX(1000);
  WizardForm.NextButton.Left := -ScaleX(1000);
  WizardForm.CancelButton.Left := -ScaleX(1000);
  if CurPageID <> wpInstalling then
    PostMessage(WizardForm.NextButton.Handle, $00F5, 0, 0);   // BM_CLICK
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
