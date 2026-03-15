#define AppName      "DefaultMonitorSwitcher"
#define AppVersion   "1.0.0"
#define AppPublisher "Nicholas"
#define AppExeName   "DefaultMonitorSwitcher.exe"
#define SourceDir    "..\publish"

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
; Run on startup (mirrors what the app itself manages via its Settings UI)
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExeName}"""; \
  Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill"; Parameters: "/F /IM {#AppExeName}"; Flags: runhidden; RunOnceId: "KillApp"

[Code]
// Kill any running instance before upgrading
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    Exec('taskkill', '/F /IM {#AppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
