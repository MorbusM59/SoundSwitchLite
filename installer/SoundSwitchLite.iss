; SoundSwitch Lite – Inno Setup installer script
; -----------------------------------------------
; Prerequisites
;   • Inno Setup 6.x  https://jrsoftware.org/isinfo.php
;   • A self-contained publish of the app (run build.bat or the dotnet publish
;     command below before compiling this script).
;
; How to build the installer:
;   1. From the repo root, publish the app:
;        dotnet publish SoundSwitchLite/SoundSwitchLite.csproj ^
;          -c Release -r win-x64 --self-contained true ^
;          -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
;          -o installer\publish
;   2. Open this .iss file in Inno Setup Compiler (or run iscc.exe):
;        iscc installer\SoundSwitchLite.iss
;   3. The resulting SoundSwitchLiteSetup.exe will be placed in installer\Output\.

#define MyAppName      "SoundSwitch Lite"
#define MyAppVersion   "1.0.0"
#define MyAppPublisher "SoundSwitch Lite"
#define MyAppExeName   "SoundSwitchLite.exe"
#define MyAppURL       "https://github.com/MorbusM59/SoundSwitchLite"

; Path to the published output folder (relative to the location of this .iss file)
#define PublishDir "publish"

[Setup]
AppId={{B3A12F47-9C5E-4E1F-A8D6-7F2C3B4E5D6A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
; Allow the user to choose the install directory
DisableDirPage=no
; Don't ask for an install directory confirmation page
DisableProgramGroupPage=yes
; Output settings
OutputDir=Output
OutputBaseFilename=SoundSwitchLiteSetup
SetupIconFile=..\SoundSwitchLite\Assets\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Minimum Windows 10 (required for .NET 8 self-contained apps)
MinVersion=10.0
; 64-bit only
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon";    Description: "{cm:CreateDesktopIcon}";    GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupentry";   Description: "Start {#MyAppName} when Windows starts"; GroupDescription: "Windows startup:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}";           Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";     Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Optional auto-start on Windows login (only when the user checked the task above)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "{#MyAppName}"; \
  ValueData: """{app}\{#MyAppExeName}""" --minimized; Flags: uninsdeletevalue; Tasks: startupentry

[Run]
; Offer to launch the app after installation finishes
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; \
  Flags: nowait postinstall skipifsilent
