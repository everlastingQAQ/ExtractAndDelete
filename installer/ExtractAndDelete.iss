#define DefaultReleaseVersion "4.1.0"
#define DefaultPackageVersion "4.1.0.0"
#define DefaultPayloadSizeBytes "0"
#define DefaultPayloadDir "..\\artifacts\\release\\4.1.0\\payload"
#define DefaultOutputDir "..\\artifacts\\release\\4.1.0"

#ifndef ReleaseVersion
  #define ReleaseVersion DefaultReleaseVersion
#endif
#ifndef PackageVersion
  #define PackageVersion DefaultPackageVersion
#endif
#ifndef PayloadDir
  #define PayloadDir DefaultPayloadDir
#endif
#ifndef PayloadSizeBytes
  #define PayloadSizeBytes DefaultPayloadSizeBytes
#endif
#ifndef OutputDir
  #define OutputDir DefaultOutputDir
#endif

[Setup]
AppId={{E8A892FB-7B98-4400-B316-083DEF0CEA12}
AppName=Extract & Delete（完整卸载）
AppVersion={#ReleaseVersion}
AppVerName=Extract & Delete（完整卸载） {#ReleaseVersion}
AppPublisher=everlasting
AppPublisherURL=https://github.com/everlastingQAQ/ExtractAndDelete
AppSupportURL=https://github.com/everlastingQAQ/ExtractAndDelete
AppUpdatesURL=https://github.com/everlastingQAQ/ExtractAndDelete/releases
DefaultDirName={localappdata}\Programs\ExtractAndDelete
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableWelcomePage=no
Uninstallable=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
CloseApplications=no
RestartIfNeededByRun=no
SignedUninstaller=no
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir={#OutputDir}
OutputBaseFilename=ExtractAndDelete-Setup-{#ReleaseVersion}-x64
VersionInfoVersion={#PackageVersion}
VersionInfoDescription=Extract & Delete Developer Preview Installer
VersionInfoProductName=Extract & Delete
VersionInfoCompany=everlasting
VersionInfoCopyright=Copyright (c) 2026 everlasting
UninstallDisplayIcon={app}\app-{#PackageVersion}\ExtractAndDelete.Gui.exe

[Languages]
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}\app-{#PackageVersion}"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "..\scripts\package-lifecycle.ps1"; DestDir: "{app}\installer"; Flags: ignoreversion
Source: "..\scripts\package-lifecycle.ps1"; Flags: dontcopy noencryption

[Run]
Filename: "{win}\explorer.exe"; Parameters: "shell:AppsFolder\ExtractAndDelete_vyz6krqqgd78c!App"; Description: "运行 Extract & Delete"; Flags: postinstall nowait skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Messages]
WelcomeLabel1=Extract & Delete {#ReleaseVersion}
WelcomeLabel2=Developer Preview 安装程序%n%n此版本需要 Windows 11 x64 Developer Mode。%n安装器未签名，Windows 可能显示 SmartScreen 或未知发布者警告。%n%n安装范围：当前用户%n安装目录：{localappdata}\Programs\ExtractAndDelete
ClickNext=单击“下一步”继续，或单击“取消”退出。
FinishedLabelNoIcons=Extract & Delete 已安装。%n%n可以从开始菜单启动，也可以在 Explorer 中使用“解压并回收”。
FinishedLabel=Extract & Delete 已安装。%n%n可以从开始菜单启动，也可以在 Explorer 中使用“解压并回收”。
FinishedHeadingLabel=安装完成

[Code]
const
  DeveloperModeKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock';
  DeveloperModeValue = 'AllowDevelopmentWithoutDevLicense';
  ExpectedPackageVersion = '{#PackageVersion}';
  ExpectedPublisher = 'CN=ExtractAndDelete Developer';
  ExpectedPackageName = 'ExtractAndDelete';
  ExpectedFamilyName = 'ExtractAndDelete_vyz6krqqgd78c';
  ExpectedApplicationId = 'App';
  ExpectedClsid = '4F4F8F37-B78C-4B3D-90CE-8D16C4483B8E';
  ExpectedPayloadSizeBytes = '{#PayloadSizeBytes}';

function IsDeveloperModeEnabled: Boolean;
var
  Value: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM64, DeveloperModeKey, DeveloperModeValue, Value) and (Value = 1);
end;

function RunLifecycle(const Action: String; const UseTemporaryScript: Boolean; var ErrorText: String): Boolean;
var
  PowerShellPath: String;
  ScriptPath: String;
  LogPath: String;
  Params: String;
  WorkingDirectory: String;
  ResultCode: Integer;
begin
  Result := False;
  ErrorText := '';
  { Inno Setup is a 32-bit process even on x64 Windows. Use Sysnative so
    package lifecycle checks run in 64-bit Windows PowerShell and see the
    native HKLM Developer Mode view. }
  PowerShellPath := ExpandConstant('{win}\Sysnative\WindowsPowerShell\v1.0\powershell.exe');
  if not FileExists(PowerShellPath) then
  begin
    ErrorText := '未找到 Windows PowerShell：' + PowerShellPath;
    Exit;
  end;

  if UseTemporaryScript then
    ScriptPath := ExpandConstant('{tmp}\package-lifecycle.ps1')
  else
    ScriptPath := ExpandConstant('{app}\installer\package-lifecycle.ps1');

  WorkingDirectory := ExpandConstant('{tmp}');
  if (not UseTemporaryScript) and DirExists(ExpandConstant('{app}')) then
    WorkingDirectory := ExpandConstant('{app}');

  LogPath := ExpandConstant('{localappdata}\Temp\ExtractAndDelete-Setup-' + Action + '.log');
  Params := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ' + AddQuotes(ScriptPath) +
    ' -Action ' + AddQuotes(Action) +
    ' -ExpectedVersion ' + AddQuotes(ExpectedPackageVersion) +
    ' -ExpectedPackageName ' + AddQuotes(ExpectedPackageName) +
    ' -ExpectedPublisher ' + AddQuotes(ExpectedPublisher) +
    ' -ExpectedFamilyName ' + AddQuotes(ExpectedFamilyName) +
    ' -ExpectedApplicationId ' + AddQuotes(ExpectedApplicationId) +
    ' -ExpectedClsid ' + AddQuotes(ExpectedClsid) +
    ' -LogPath ' + AddQuotes(LogPath);

  if (Action = 'Install') or (Action = 'Preflight') or (Action = 'Uninstall') then
  begin
    Params := Params +
      ' -InstallRoot ' + AddQuotes(ExpandConstant('{app}'));

    if (Action = 'Install') or (Action = 'Preflight') then
    begin
      Params := Params +
        ' -MinimumFreeBytes ' + AddQuotes(ExpectedPayloadSizeBytes) +
        ' -PayloadPath ' + AddQuotes(ExpandConstant('{app}\app-' + ExpectedPackageVersion)) +
        ' -ManifestPath ' + AddQuotes(ExpandConstant('{app}\app-' + ExpectedPackageVersion + '\AppxManifest.xml'));
    end;
  end;

  Log('执行 package 生命周期操作：' + Action);
  if not Exec(PowerShellPath, Params, WorkingDirectory, SW_HIDE,
    ewWaitUntilTerminated, ResultCode) then
  begin
    ErrorText := '无法启动 package 生命周期脚本。日志：' + LogPath;
    Exit;
  end;

  if ResultCode <> 0 then
  begin
    ErrorText := 'package 生命周期脚本失败，退出码 ' + IntToStr(ResultCode) +
      '。日志：' + LogPath;
    Exit;
  end;

  Result := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if CurPageID <> wpWelcome then
    Exit;

  if not IsDeveloperModeEnabled then
  begin
    if MsgBox(
      'Windows Developer Mode 尚未开启。请在“设置 → 系统 → 面向开发人员”中开启开发人员模式。' +
      Chr(13) + Chr(10) + Chr(13) + Chr(10) + '点击“重试”将打开开发者设置；开启后请再次点击“下一步”。',
      mbError, MB_RETRYCANCEL) = IDRETRY then
    begin
      if not ShellExec('', 'ms-settings:developers', '', '', SW_SHOWNORMAL,
        ewNoWait, ErrorCode) then
        MsgBox('无法打开开发者设置，请手动打开“设置 → 系统 → 面向开发人员”。', mbError, MB_OK);
    end;
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ErrorText: String;
begin
  Result := '';
  NeedsRestart := False;
  if not RunLifecycle('Preflight', True, ErrorText) then
    Result := ErrorText + Chr(13) + Chr(10) + Chr(13) + Chr(10) + '请解决问题后点击“重试”。';
end;

procedure InitializeWizard;
begin
  ExtractTemporaryFile('package-lifecycle.ps1');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ErrorText: String;
begin
  if CurStep = ssPostInstall then
  begin
    if not RunLifecycle('Install', False, ErrorText) then
      RaiseException(ErrorText);
  end;
end;

function InitializeUninstall: Boolean;
var
  ErrorText: String;
begin
  Result := RunLifecycle('Uninstall', False, ErrorText);
  if not Result then
    MsgBox(
      ErrorText + Chr(13) + Chr(10) + Chr(13) + Chr(10) +
      '未删除安装文件。请关闭应用；如果 Shell DLL 仍被占用，请注销登录后重新运行“完整卸载”。',
      mbError, MB_OK);
end;
