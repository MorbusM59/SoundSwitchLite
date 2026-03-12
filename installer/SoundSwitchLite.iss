; SoundSwitch Lite — Inno Setup 6 installer script
;
; Prerequisites
;   1. Install Inno Setup 6 from https://jrsoftware.org/isinfo.php
;   2. Publish the app first:
;        dotnet publish SoundSwitchLite/SoundSwitchLite.csproj /p:PublishProfile=win-x64
;      This produces a single self-contained EXE at:
;        SoundSwitchLite/publish/win-x64/SoundSwitchLite.exe
;   3. Open this script in the Inno Setup IDE (or run iscc.exe SoundSwitchLite.iss)
;      to produce:  installer/Output/SoundSwitchLite-Setup-1.0.0.exe

#define AppName      "SoundSwitch Lite"
#define AppVersion   "1.0.0"
#define AppPublisher "MorbusM59"
#define AppURL       "https://github.com/MorbusM59/SoundSwitchLite"
#define AppExeName   "SoundSwitchLite.exe"
#define AppIcon      "..\SoundSwitchLite\Assets\app.ico"
#define SourceExe    "..\SoundSwitchLite\publish\win-x64\SoundSwitchLite.exe"

[Setup]
; Unique identifier — do NOT change after first release
AppId={{F3A2C8D1-7E4B-4F9A-B6C3-2D1E5A8F0C7B}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
; Require Windows 10 or later (version 10.0 build 17763+)
MinVersion=10.0.17763
; Output setup EXE
OutputDir=Output
OutputBaseFilename=SoundSwitchLite-Setup-{#AppVersion}
SetupIconFile={#AppIcon}
Compression=lzma2/ultra64
SolidCompression=yes
; Require administrator for system-wide install
PrivilegesRequired=admin
; Modern wizard style
WizardStyle=modern
WizardResizable=no
; Run silently in 64-bit mode
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon";     Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupregistry"; Description: "Start {#AppName} automatically when Windows starts";               GroupDescription: "Startup:"; Flags: unchecked

[Files]
; Self-contained single-file EXE — no .NET runtime required on target machine
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "{#AppExeName}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}";         Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Auto-start with Windows (optional task)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "{#AppName}"; \
  ValueData: "{app}\{#AppExeName}"; \
  Flags: uninsdeletevalue; Tasks: startupregistry

[Run]
; Launch the app after installation completes
Filename: "{app}\{#AppExeName}"; \
  Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; \
  Flags: nowait postinstall skipifsilent

[UninstallRun]
; Terminate the app (if running) before uninstall
Filename: "taskkill.exe"; Parameters: "/F /IM {#AppExeName}"; \
  Flags: runhidden; RunOnceId: "KillSoundSwitchLite"
