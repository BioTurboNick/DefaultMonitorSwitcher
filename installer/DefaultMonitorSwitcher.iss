#define AppName      "DefaultMonitorSwitcher"
#define AppVersion   "1.2.0"
#define AppPublisher "Nicholas"
#define AppExeName   "DefaultMonitorSwitcher.exe"
#define SourceDir    "..\publish"
#define DotNetUrl    "https://dotnet.microsoft.com/en-us/download/dotnet/10.0"

[Setup]
AppId={{A3F2B8C1-4D7E-4F9A-B2C3-D5E6F7A8B9C0}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\installer-output
OutputBaseFilename=DefaultMonitorSwitcher-Setup-{#AppVersion}
SetupIconFile=..\UI\Resources\Icons\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
UsedUserAreasWarning=no
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
MinVersion=10.0.17763
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start automatically when Windows starts"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"

[Registry]
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExeName}"""; \
  Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill"; Parameters: "/F /IM {#AppExeName}"; Flags: runhidden; RunOnceId: "KillApp"

[Code]
// Check for .NET 10 Windows Desktop Runtime (x64)
function IsDotNet10Installed(): Boolean;
var
  Key: String;
begin
  Key := 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
  Result := RegKeyExists(HKLM, Key) or RegKeyExists(HKCU, Key);
  if not Result then
  begin
    // Fallback: check for any 10.x entry under the shared framework path
    Result := RegKeyExists(HKLM,
      'SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App');
  end;
end;

function InitializeSetup(): Boolean;
var
  ResultCode2: Integer;
begin
  Result := True;
  if not IsDotNet10Installed() then
  begin
    if MsgBox('.NET 10 Windows Desktop Runtime (x64) is required but was not detected.'
      + #13#10#13#10
      + 'Please download and install it from:'
      + #13#10 + '{#DotNetUrl}'
      + #13#10#13#10
      + 'Click OK to open the download page, or Cancel to abort setup.',
      mbConfirmation, MB_OKCANCEL) = IDOK then
    begin
      ShellExec('open', '{#DotNetUrl}', '', '', SW_SHOW, ewNoWait, ResultCode2);
    end;
    Result := False;
  end;
end;

// Kill any running instance before upgrading
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
    Exec('taskkill', '/F /IM {#AppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
