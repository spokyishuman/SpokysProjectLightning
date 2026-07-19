; Inno Setup script for Spokys Project Lightning
; Creates a proper Windows installer

#define MyAppName "Spokys Project Lightning"
#define MyAppVersion "4.2.0"
#define MyAppPublisher "Spoky"
#define MyAppURL "https://github.com/spokyishuman/SpokysProjectLightning"
#define MyAppExeName "SpokysProjectLightning.exe"
#define MyAppAssocName "Spokys Project Lightning File"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=.\Release\Installer
OutputBaseFilename=SpokysProjectLightning-Setup-v{#MyAppVersion}
SetupIconFile=SpokysPL\app.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
CreateAppDir=yes
AllowNoIcons=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: checkedonce

[Files]
Source: "SpokysPL\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "SpokysPL\app.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\SpokysPL.Updater.exe"; Parameters: "/uninstall"; Flags: runhidden

[Registry]
Root: HKCU; Subkey: "Software\Spoky\{#MyAppName}"; Flags: uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Spoky\{#MyAppName}"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletevalue

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Register uninstall in Windows Apps & Features
    // (Inno Setup does this automatically via its uninstall registry entries)
  end;
end;
