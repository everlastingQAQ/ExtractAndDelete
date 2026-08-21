# Extract & Delete

面向 Windows 11 x64 的安全压缩包工具：完整提取后，将源压缩包移入 Windows 回收站。

当前版本为 `4.1.2` Developer Preview。产品使用 WinForms/Win32 原生控件复刻 Windows 11“提取压缩(Zipped)文件夹”窗口，内置官方 7-Zip 26.02 x64，并通过 Windows 11 Explorer 现代右键菜单提供“解压并回收”。4.1.2 在保留自包含运行时的前提下仅打包简体中文资源，并修正安装向导产品名显示。

## 重要限制

这是未签名的 Developer Mode packaged 应用，不是普通用户可直接安装的生产发行版：

- 只支持 Windows 11 x64，build 22000 或更高。
- 安装前必须手动开启“设置 → 系统 → 面向开发人员 → 开发人员模式”。
- 安装器、应用和卸载器未进行 Authenticode 签名，Windows 可能显示 SmartScreen 或未知发布者警告。
- 安装器只为当前用户安装，不请求管理员权限，不自动修改 Developer Mode。
- 不提供 Store、MSIX、MSI、AppInstaller、便携版或自动更新。

## 安装与发布下载

公开 Release 页面：

<https://github.com/everlastingQAQ/ExtractAndDelete/releases>

下载与版本匹配的 `ExtractAndDelete-Setup-4.1.2-x64.exe`，可选按同名 `.sha256` 文件核对完整性，然后双击安装。安装器会把 payload 写入当前用户的：

```text
%LOCALAPPDATA%\Programs\ExtractAndDelete
```

安装完成后可以从开始菜单启动，也可以在 Explorer 中对单个 `.zip`、`.7z`、`.rar` 或 `.tar` 文件使用“解压并回收”。安装器默认不创建桌面快捷方式；运行新版本 EXE 即可手动升级。

Windows 可能显示两个卸载入口：

- `Extract & Delete（完整卸载）`：推荐使用，会注销 package 并删除安装器管理的文件。
- `Extract & Delete（系统集成组件）`：只移除 package 注册和右键菜单，文件仍保留。

4.1.2 仍是自包含发布，不需要另装 .NET、Windows App SDK 或 7-Zip；为减小安装包，只随包提供 `zh-CN` 卫星资源，不改变运行时和功能。

## 安全承诺

```text
扫描/解压失败或取消 → 目标不发布，源压缩包保留
Windows 发布取消或失败 → 已写入的部分目标可能保留，源压缩包保留
回收失败 → 完整目标和源压缩包都保留，不永久删除
只有完整发布、没有跳过且成功移入回收站 → 工作流完成
```

内置 7-Zip 只写入受控 staging；GUI 使用 Windows Shell 负责目录合并、冲突、错误、进度和按需 UAC。CLI 源码仍保留在 `src/ExtractAndDelete.Cli`，但已冻结，不进入默认构建、测试、安装器或用户交付。

## 从源码构建

开发环境要求 .NET SDK 10.0.400 feature band、Visual Studio x64 C++/Windows SDK、PowerShell 7 和 Inno Setup 6.7.3：

```powershell
.\scripts\verify.ps1
.\scripts\acceptance-check.ps1
.\scripts\build-installer.ps1
.\scripts\verify-installer.ps1
```

普通测试：

```powershell
dotnet test .\ExtractAndDelete.slnx `
  --configuration Release `
  --filter "Category!=WindowsIntegration"
```

真实 Windows Integration 测试只使用唯一临时文件：

```powershell
dotnet test .\ExtractAndDelete.slnx `
  --configuration Release `
  --filter "Category=WindowsIntegration"
```

如果不使用安装器而直接开发部署，依次运行：

```powershell
.\scripts\check-dev-environment.ps1
.\scripts\deploy-dev.ps1
.\scripts\verify-dev-install.ps1 -ExpectedInstallLocation (Resolve-Path .\src\ExtractAndDelete.Gui\bin\Release\net10.0-windows10.0.22000.0\win-x64)
```

开发注销：

```powershell
.\scripts\uninstall-dev.ps1
```

`acceptance-check.ps1` 只验证构建输出；`verify-dev-install.ps1` 才验证当前用户 package 注册状态。

## 文档

- [使用文档](docs/USAGE.md)
- [项目总设计](docs/PROJECT_DESIGN.md)
- [验收说明](docs/ACCEPTANCE.md)
- [4.1.1 Developer Preview 发布说明](docs/releases/v4.1.1.md)
- [4.1.2 Developer Preview 发布说明](docs/releases/v4.1.2.md)
- [4.1.0 历史发布说明](docs/releases/v4.1.0.md)
- [第三方声明](THIRD-PARTY-NOTICES.md)
- [MIT License](LICENSE)
