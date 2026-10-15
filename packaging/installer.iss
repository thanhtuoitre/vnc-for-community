; Script generated for Inno Setup 6+
; ThanhVNC Windows Server Installer

#define MyAppName "ThanhVNC Server"
#define MyAppVersion "2.0.0"
#define MyAppPublisher "ThanhVNC Community"
#define MyAppURL "https://github.com/thanhtuoitre/vnc-for-community"
#define MyAppExeName "VncServer.App.exe"

[Setup]
AppId={{E68A91F2-72BC-4841-B835-15A6F292B48D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\README.md
OutputDir=..\dist\installer
OutputBaseFilename=ThanhVNC_Server_Setup_v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Automatically start ThanhVNC Server on Windows login"; GroupDescription: "Startup Options:"

[Files]
Source: "..\dist\windows-server\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\windows-server\*.pdb"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "install-service.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "uninstall-service.bat"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ThanhVNC Server"; ValueData: """{app}\{#MyAppExeName}"" --autostart"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
; Add Firewall rules during installation
Filename: "netsh"; Parameters: "advfirewall firewall add rule name=""ThanhVNC Remote Desktop"" dir=in action=allow protocol=TCP localport=5900 profile=any description=""Allows inbound remote desktop connections to ThanhVNC Server"""; Flags: runhidden
Filename: "netsh"; Parameters: "advfirewall firewall add rule name=""ThanhVNC Discovery"" dir=in action=allow protocol=UDP localport=5902 profile=any description=""Allows inbound UDP discovery broadcasts for ThanhVNC Server"""; Flags: runhidden
; Option to launch server after installation
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Remove Firewall rules during uninstallation
Filename: "netsh"; Parameters: "advfirewall firewall delete rule name=""ThanhVNC Remote Desktop"""; Flags: runhidden
Filename: "netsh"; Parameters: "advfirewall firewall delete rule name=""ThanhVNC Discovery"""; Flags: runhidden
Filename: "taskkill"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden
