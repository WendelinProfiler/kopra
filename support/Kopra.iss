#define AppName "Kopra"
#define AppVersion "1.0.0-beta.5"
[Setup]
AppId={{92C12683-CECF-4BF7-8EED-540EB7A76EE5}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Kopra contributors
DefaultDirName={localappdata}\Programs\Kopra
DefaultGroupName=Kopra
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=Kopra-Setup-Windows-x64-preview
SetupIconFile=..\assets\Kopra.ico
UninstallDisplayIcon={app}\Kopra.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"
[CustomMessages]
en.DeviceSharing=Allow sharing with a Mac on your local network (administrator approval)
de.DeviceSharing=Verbindung mit einem Mac im lokalen Netzwerk erlauben (Administratorfreigabe)
en.LaunchKopra=Open Kopra
de.LaunchKopra=Kopra starten
en.NetworkSetupFailed=Could not allow incoming connections. Kopra still works locally.
de.NetworkSetupFailed=Die Netzwerkfreigabe konnte nicht eingerichtet werden. Kopra funktioniert weiterhin lokal.
[Tasks]
Name: "devices"; Description: "{cm:DeviceSharing}"; Flags: unchecked
[Files]
Source: "..\artifacts\Kopra-Windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Enable-DeviceSyncFirewall.ps1"; DestDir: "{app}\support"; Flags: ignoreversion
[Icons]
Name: "{userprograms}\Kopra"; Filename: "{app}\Kopra.exe"; IconFilename: "{app}\Kopra.exe"
[Run]
Filename: "{app}\Kopra.exe"; Description: "{cm:LaunchKopra}"; Flags: nowait postinstall skipifsilent
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Kopra"; Flags: uninsdeletevalue
[UninstallRun]
Filename: "{app}\Kopra.exe"; Parameters: "--quit-safe"; Flags: runhidden skipifdoesntexist
[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var ResultCode: Integer; Arguments: String;
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('devices') then begin
    Arguments := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\support\Enable-DeviceSyncFirewall.ps1') + '" -InstalledExe "' + ExpandConstant('{app}\Kopra.exe') + '" -Result "' + ExpandConstant('{tmp}\Kopra-network.json') + '"';
    if not ShellExec('runas', ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Arguments, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      MsgBox(CustomMessage('NetworkSetupFailed'), mbInformation, MB_OK);
  end;
end;
