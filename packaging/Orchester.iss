#ifndef AppVersion
 #define AppVersion "0.3.0-preview.1"
#endif
[Setup]
AppId={{86B1991F-05A6-4A7F-9B76-59CB43797471}
AppName=ORCHESTER
AppVersion={#AppVersion}
AppPublisher=ORCHESTER
DefaultDirName={autopf}\ORCHESTER
DefaultGroupName=ORCHESTER
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19045
PrivilegesRequired=admin
UninstallDisplayIcon={app}\Orchester.App.exe
OutputDir=..\artifacts\Installer
OutputBaseFilename=ORCHESTER-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
CloseApplications=yes
[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked
[Files]
Source: "..\artifacts\Orchester\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\ORCHESTER"; Filename: "{app}\Orchester.App.exe"
Name: "{autodesktop}\ORCHESTER"; Filename: "{app}\Orchester.App.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\Orchester.App.exe"; Description: "{cm:LaunchProgram,ORCHESTER}"; Flags: nowait postinstall skipifsilent runasoriginaluser
[UninstallDelete]
; User downloads, operation journals, installed operating systems and retained partitions are preserved.
Type: dirifempty; Name: "{app}"
[Code]
function InitializeUninstall(): Boolean;
var ExitCode: Integer;
begin
  Result := Exec(ExpandConstant('{app}\Orchester.Helper.exe'), '--can-uninstall', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) and (ExitCode = 0);
  if not Result then begin
    if ActiveLanguage() = 'ru' then
      MsgBox('Сначала откройте «Восстановление» в ORCHESTER и очистите активные ресурсы установщика. Удаление Linux не требуется.', mbError, MB_OK)
    else
      MsgBox('Open Recovery in ORCHESTER and clean up active installer resources before uninstalling. Installed Linux systems do not need to be removed.', mbError, MB_OK);
  end;
end;
