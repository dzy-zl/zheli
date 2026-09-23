#ifndef Product
  #error Product must be Settings, Timetable or Miao
#endif
#if Product == "Settings"
  #define DisplayName "哲里设置"
#elif Product == "Timetable"
  #define DisplayName "哲里课表"
#elif Product == "Miao"
  #define DisplayName "哲喵"
#else
  #error Unknown product
#endif
#ifndef Payload
  #error Missing Payload
#endif
#ifndef AppVersion
  #error Missing AppVersion
#endif
#ifndef BuildId
  #error Missing BuildId
#endif

[Setup]
AppId=Zheli.{#Product}.Development
AppName={#DisplayName}（测试版）
AppVersion={#AppVersion}
AppPublisher=哲里
DefaultDirName={localappdata}\Programs\Zheli
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
WizardStyle=modern
AppMutex=Local\Zheli.Applications.Running
UninstallFilesDir={app}\.uninstall\{#Product}
UninstallDisplayName={#DisplayName}（测试版）
OutputBaseFilename=Zheli-{#Product}-{#AppVersion}-win-x64-Setup
Compression=lzma2
SolidCompression=yes
CloseApplications=no
RestartApplications=no
SetupLogging=yes
Uninstallable=yes

[Languages]
Name: "zhcn"; MessagesFile: "Languages\ChineseSimplified.isl"

[Files]
#if Product == "Settings"
Source: "{#Payload}\Zheli.CoreHost\*"; DestDir: "{app}\Zheli.CoreHost"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Payload}\Zheli.Settings\*"; DestDir: "{app}\Zheli.Settings"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Payload}\build.ini"; DestDir: "{app}"; Flags: ignoreversion
#else
Source: "{#CommonSetup}"; Flags: dontcopy
Source: "{#Payload}\Zheli.{#Product}\*"; DestDir: "{app}\Zheli.{#Product}"; Flags: ignoreversion recursesubdirs createallsubdirs
  #if Product == "Timetable"
Source: "{#Payload}\Zheli.Timetable.Host\*"; DestDir: "{app}\Zheli.Timetable.Host"; Flags: ignoreversion recursesubdirs createallsubdirs
  #else
Source: "{#Payload}\Zheli.PetHost\*"; DestDir: "{app}\Zheli.PetHost"; Flags: ignoreversion recursesubdirs createallsubdirs
  #endif
#endif

[Icons]
Name: "{userprograms}\哲里\{#DisplayName}"; Filename: "{app}\Zheli.{#Product}\Zheli.{#Product}.exe"; WorkingDir: "{app}\Zheli.{#Product}"

[Registry]
Root: HKCU; Subkey: "Software\Zheli\Installed\{#Product}"; ValueType: string; ValueName: "Root"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Zheli\Installed\{#Product}"; ValueType: string; ValueName: "BuildId"; ValueData: "{#BuildId}"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExistingRoot, ExistingBuild: String;
#if Product != "Settings"
  Code: Integer;
  Params: String;
#endif
begin
  Result := '';
  if CheckForMutexes('Local\Zheli.Applications.Running') then begin
    Result := '请先退出所有哲里软件及后台服务，再继续安装。安装程序不会强制结束进程。';
    exit;
  end;
  if RegQueryStringValue(HKCU, 'Software\Zheli\Installed\Settings', 'Root', ExistingRoot) then
    if CompareText(ExistingRoot, WizardDirValue) <> 0 then begin
      Result := '哲里公共服务已安装在其他目录，请使用相同安装目录。';
      exit;
    end;
  ExistingBuild := GetIniString('Build', 'Id', '', ExpandConstant('{app}\build.ini'));
  if (ExistingBuild <> '') and (ExistingBuild <> '{#BuildId}') then begin
    Result := '已有其他构建版本。测试版暂不支持跨版本覆盖：请先卸载哲里课表、哲喵，最后卸载哲里设置，再安装本版。业务数据会保留。';
    exit;
  end;
  if (ExistingBuild = '') and (DirExists(ExpandConstant('{app}\Zheli.CoreHost')) or DirExists(ExpandConstant('{app}\Zheli.Settings')) or DirExists(ExpandConstant('{app}\Zheli.Timetable')) or DirExists(ExpandConstant('{app}\Zheli.Miao'))) then begin
    Result := '此目录已有非本安装器管理的哲里程序。请先按原版本说明移除程序目录，业务数据无需删除。';
    exit;
  end;
#if Product != "Settings"
  if (ExistingBuild <> '{#BuildId}') or
     (not FileExists(ExpandConstant('{app}\Zheli.Settings\Zheli.Settings.exe'))) or
     (not FileExists(ExpandConstant('{app}\Zheli.CoreHost\Zheli.CoreHost.exe'))) then begin
    ExtractTemporaryFile('{#CommonName}');
    Params := '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR="' + WizardDirValue + '"';
    if not Exec(ExpandConstant('{tmp}\{#CommonName}'), Params, '', SW_HIDE, ewWaitUntilTerminated, Code) then
      Result := '无法启动公共服务安装，请先运行哲里设置安装包。'
    else if Code <> 0 then
      Result := '公共服务安装未完成，当前软件尚未安装。请检查哲里设置安装日志。';
    if (Result = '') and (GetIniString('Build', 'Id', '', ExpandConstant('{app}\build.ini')) <> '{#BuildId}') then
      Result := '公共服务构建校验失败，当前软件尚未安装。';
  end;
#endif
end;

function InitializeUninstall(): Boolean;
begin
  Result := not CheckForMutexes('Local\Zheli.Applications.Running');
  if not Result then begin
    if not UninstallSilent then MsgBox('请先退出所有哲里软件及后台服务，再卸载。', mbError, MB_OK);
    exit;
  end;
#if Product == "Settings"
  Result := not (RegKeyExists(HKCU, 'Software\Zheli\Installed\Timetable') or RegKeyExists(HKCU, 'Software\Zheli\Installed\Miao'));
  if not Result then
    if not UninstallSilent then MsgBox('哲里课表或哲喵仍在使用公共服务。请先卸载它们，最后卸载哲里设置。业务数据将保留。', mbError, MB_OK);
#endif
end;
