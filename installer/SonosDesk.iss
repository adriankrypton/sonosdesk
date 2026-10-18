; Inno Setup script for SonosDesk (https://jrsoftware.org/isinfo.php, version 7).
; Build with tools/build-installer.ps1, which publishes the app first and passes the defines below.
; Per-user installation into %LOCALAPPDATA%\Programs\SonosDesk – no administrator rights needed.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\SonosDesk-win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

#define AppName "SonosDesk"
#define AppExe "SonosDesk.exe"
#define RepoUrl "https://github.com/adriankrypton/sonosdesk"

[Setup]
; Never change the AppId: it identifies the installation for upgrades and uninstall.
AppId={{584CAC11-2846-450D-9ED1-AA5C6B2830F6}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=adriankrypton and SonosDesk contributors
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
AppCopyright=Copyright (c) adriankrypton and SonosDesk contributors
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=SonosDesk-Setup-{#AppVersion}-win-x64
SetupIconFile=..\assets\SonosDesk.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
; The app holds this mutex while running; Setup and Uninstall ask to close it first.
AppMutex=Local\SonosDesk.SingleInstance
CloseApplications=yes
RestartApplications=no
ShowLanguageDialog=auto

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "nl"; MessagesFile: "compiler:Languages\Dutch.isl"
Name: "pl"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
en.AutostartTask=Start SonosDesk with Windows (in the notification area)
de.AutostartTask=SonosDesk mit Windows starten (im Infobereich)
fr.AutostartTask=Démarrer SonosDesk avec Windows (dans la zone de notification)
es.AutostartTask=Iniciar SonosDesk con Windows (en el área de notificación)
it.AutostartTask=Avvia SonosDesk con Windows (nell’area di notifica)
nl.AutostartTask=SonosDesk met Windows starten (in het systeemvak)
pl.AutostartTask=Uruchamiaj SonosDesk z systemem Windows (w obszarze powiadomień)
ru.AutostartTask=Запускать SonosDesk вместе с Windows (в области уведомлений)
en.DeleteUserData=Also delete your SonosDesk settings, saved stations, logos and logs?
de.DeleteUserData=Sollen auch deine SonosDesk-Einstellungen, gespeicherten Sender, Logos und Protokolle gelöscht werden?
fr.DeleteUserData=Supprimer aussi vos réglages SonosDesk, stations enregistrées, logos et journaux ?
es.DeleteUserData=¿Eliminar también tus ajustes de SonosDesk, emisoras guardadas, logotipos y registros?
it.DeleteUserData=Eliminare anche le impostazioni di SonosDesk, le stazioni salvate, i loghi e i log?
nl.DeleteUserData=Ook je SonosDesk-instellingen, opgeslagen zenders, logo’s en logboeken verwijderen?
pl.DeleteUserData=Usunąć także ustawienia SonosDesk, zapisane stacje, logo i dzienniki?
ru.DeleteUserData=Удалить также настройки SonosDesk, сохранённые станции, логотипы и журналы?

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutostartTask}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same entry the app writes for "Start with Windows", so the setting in the app shows it as enabled.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SonosDesk"; ValueData: """{app}\{#AppExe}"" --autostart"; Tasks: autostart
; Remove the autostart entry on uninstall, also when it was enabled later inside the app.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "SonosDesk"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Settings live outside the program folder; keep them unless the user wants them gone.
    DataDir := ExpandConstant('{localappdata}\SonosDesk');
    if DirExists(DataDir) and not UninstallSilent then
      if SuppressibleMsgBox(CustomMessage('DeleteUserData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
