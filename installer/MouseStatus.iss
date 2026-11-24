[Setup]
AppId=MouseStatus.Desktop
AppName=Mouse Status
AppVersion={#AppVersion}
AppPublisher=Mouse Status contributors
DefaultDirName={localappdata}\Programs\Mouse Status
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0.22000
OutputBaseFilename={#AssetName}
SetupIconFile=..\src\app.ico
UninstallDisplayIcon={app}\MouseStatus.exe
CloseApplications=yes
RestartApplications=no
WizardStyle=modern
Compression=lzma2

[Files]
Source: "{#AppBinary}"; DestDir: "{app}"; DestName: "MouseStatus.exe"
Source: "..\LICENSE"; DestDir: "{app}"; Flags: skipifsourcedoesntexist

[Icons]
Name: "{userprograms}\Mouse Status"; Filename: "{app}\MouseStatus.exe"

[Run]
Filename: "{app}\MouseStatus.exe"; Description: "Launch Mouse Status"; Flags: nowait postinstall skipifsilent
