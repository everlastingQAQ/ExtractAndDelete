# Extract & Delete 4.1.2 Developer Preview 验收说明

本版本是 `4.1.2` / package `4.1.2.0` 的 Windows 11 x64 Developer Preview。交付资产只有一个未签名的 Inno Setup EXE 和同名 SHA-256 文件。它需要 Developer Mode，不提供 Store、MSIX、MSI、AppInstaller、便携版或自动更新。4.1.2 保留自包含运行时，仅打包 `zh-CN` 卫星资源。

## 1. 发布前自动检查

在仓库根目录执行：

```powershell
.\scripts\check-release-environment.ps1
.\scripts\verify.ps1
dotnet test .\ExtractAndDelete.slnx --configuration Release --filter "Category=WindowsIntegration"
.\scripts\acceptance-check.ps1
.\scripts\build-installer.ps1
.\scripts\verify-installer.ps1
```

必须满足：

- x64 Windows 11 build 不低于 22000，.NET SDK feature band 为 10.0.400。
- C++ Shell Extension 和 .NET Release 构建 0 警告、0 错误。
- 普通测试和显式 Windows Integration 测试通过。
- 7-Zip 26.02 x64 哈希验证通过。
- Inno Setup 严格为 6.7.3，编译无警告和错误。
- GUI publish 包含自包含 .NET/Windows App SDK、Shell DLL、7-Zip、许可证、清单和四种 Explorer verb。
- GUI publish 和安装器不包含 CLI、PDB、源码、用户路径、证书、`.msix` 或 `.appinstaller`。
- `verify-installer.ps1` 验证 EXE SHA-256、ProductVersion、ProductName 和 `NotSigned` 状态。

`acceptance-check.ps1` 只检查磁盘上的 payload 布局，不能证明 package 已注册；`verify-installer.ps1` 只检查生成的 EXE，也不启动安装器。

输出固定为：

```text
artifacts\release\4.1.2\
├─ ExtractAndDelete-Setup-4.1.2-x64.exe
└─ ExtractAndDelete-Setup-4.1.2-x64.exe.sha256
```

## 2. 安装器静态契约

检查 `installer\ExtractAndDelete.iss` 和 `release-config.json` 的值一致：

```text
AppId       {E8A892FB-7B98-4400-B316-083DEF0CEA12}
安装范围     当前用户（PrivilegesRequired=lowest）
架构         x64compatible
最低系统     Windows 11 build 22000
安装根目录   %LOCALAPPDATA%\Programs\ExtractAndDelete
payload      app-4.1.2.0
```

安装器不创建桌面快捷方式，不自动关闭应用或 Explorer，不生成签名文件。完成页默认勾选运行应用；启动通过 AUMID `ExtractAndDelete_vyz6krqqgd78c!App`，不直接执行安装目录 EXE。

安装说明页必须明确显示 Developer Mode、未签名 SmartScreen、当前用户安装和固定路径。安装语言为简体中文。

## 3. 当前电脑注册验收

确认 Developer Mode：

```powershell
(Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock').AllowDevelopmentWithoutDevLicense
```

依次执行：

```powershell
.\scripts\deploy-dev.ps1
.\scripts\verify-dev-install.ps1 -ExpectedInstallLocation (Resolve-Path .\src\ExtractAndDelete.Gui\bin\Release\net10.0-windows10.0.22000.0\win-x64)
```

安装器本机验收用真实 EXE：

```powershell
Start-Process .\artifacts\release\4.1.2\ExtractAndDelete-Setup-4.1.2-x64.exe -Wait
.\scripts\verify-dev-install.ps1
```

注册结果必须为：

```text
Name            ExtractAndDelete
Version         4.1.2.0
Status          Ok
PackageFamily   ExtractAndDelete_vyz6krqqgd78c
Application Id  App
Publisher       CN=ExtractAndDelete Developer
InstallLocation ...\Programs\ExtractAndDelete\app-4.1.2.0
```

清单必须保留固定 Shell CLSID `4F4F8F37-B78C-4B3D-90CE-8D16C4483B8E`，并针对 `.zip`、`.7z`、`.rar`、`.tar` 注册 verb。

## 4. 迁移、修复和回滚

在干净测试用户或可恢复 VM 中准备以下场景：

1. 已有仓库注册的 `4.0.0.0`：运行 4.1.2 EXE，确认新 package 注册成功，旧仓库目录和源码完全不变。
2. 已有损坏的 4.1.0/4.1.1 package（InstallLocation 为空或清单含占位符）：运行 4.1.2 EXE，确认只修复固定 package，不删除其他目录。
3. 已有同版本 4.1.2：再次运行 EXE，确认进入修复，不产生第二个 package 或第二个完整卸载项。
4. 已有高于 4.1.2 的 package：确认安装器在任何 package 变更前拒绝降级。
4. 让新清单注册失败：确认新注册被精确清理，旧清单恢复；失败对话框和 `%LOCALAPPDATA%\Temp\ExtractAndDelete-Setup-*.log` 同时包含新错误和恢复错误（如有）。
5. Developer Mode 关闭：确认安装器不写文件、不改变 package，能打开 `ms-settings:developers`，开启后可重新检测。
6. GUI 正在运行：确认安装器不强制结束进程，提供重试/取消并等待任务正常结束。

所有递归清理必须限于固定安装根目录的已验证子目录；不得删除仓库输出、用户压缩包、用户解压目录或其他 package。

## 5. GUI 与 Explorer 手工验收

在 Windows 11 简体中文 x64、普通浅色主题和高对比度模式下执行：

### 安装和入口

- 不安装系统 .NET、Windows App SDK Runtime 或 7-Zip 仍可启动。
- 开始菜单显示 `Extract & Delete`。
- 单个 ZIP/7Z/RAR/TAR 的 Windows 11 现代菜单显示“解压并回收”。
- 非支持格式不显示，多选命令禁用。
- 完成页勾选/取消勾选运行应用的行为正确。
- 不创建桌面快捷方式。

### 向导和清晰度

- 初始窗口与 Windows “提取压缩(Zipped)文件夹”布局一致，主按钮为“提取并回收(E)”。
- 96 DPI 外框目标 `784×585`，固定对话框，不能最大化或自由缩放。
- 路径框、浏览按钮、复选框、底部区域和系统字体清晰，无截图/位图拉伸。
- 100%、125%、150%、200% 缩放和不同 DPI 显示器移动无裁切、重叠或模糊。
- 高对比度使用系统颜色；普通深色主题仍按锁定的参考浅色显示。
- `Alt+F/R/H/E`、Enter、Esc 和 Tab 顺序正确。

### 文件行为

使用唯一临时压缩包副本，成功后源文件会进入回收站：

| 场景 | 必须结果 |
| --- | --- |
| ZIP、7Z、RAR、TAR | 完整目标发布，源包进入回收站 |
| 中文、空格、`&`、括号路径 | 路径和输出正确 |
| 目标不存在 | Windows Shell 创建并发布 |
| 目标已有目录 | Windows Shell 合并 |
| 替换/保留两者 | 全部完成后允许回收 |
| 跳过任一条目 | 目标保留已完成内容，源包保留 |
| 损坏、加密、分卷、危险条目 | 目标不发布，源包保留 |
| 扫描/staging 取消 | 目标不改变，staging 清理，源包保留 |
| Windows 发布取消/失败/UAC 拒绝 | 目标可部分存在，源包保留，不回滚 Shell 输出 |
| 回收失败 | 完整目标和源包都保留，黄色警告 |
| 显示文件勾选 | 完整成功后尝试打开目标并自动关闭 |
| 显示文件未勾选 | 不自动打开目标 |

运行中第二次 Explorer 激活只前置现有窗口并提示任务进行中，不替换当前路径、不排队、不并行。

## 6. 双卸载入口

在“已安装的应用”中确认两个入口名称清晰：

```text
Extract & Delete（完整卸载）
Extract & Delete（系统集成组件）
```

优先执行完整卸载：

```text
确认 GUI 未运行
→ 精确注销当前用户 ExtractAndDelete package
→ 验证 Get-AppxPackage 无输出
→ 删除 Inno 管理的 payload、脚本、许可证和卸载项
→ 验证安装根目录不存在或为空
```

package 注销失败时不得删除文件，不得强制结束 Explorer 或 `dllhost.exe`。Shell DLL 被占用时提示注销登录后重试。完整卸载后重启 Explorer 或重新登录，确认开始菜单和四种右键菜单消失，用户文件不受影响。

单独删除“系统集成组件”后，安装文件应保留；重新运行同版本 EXE 应能修复 package 注册。

## 7. GitHub 发布验收

发布工作流只接受与配置一致的版本 tag；本轮为 `v4.1.2`。它验证 tag commit 属于 `origin/main`、版本配置一致、测试通过、Inno 6.7.3 和构建证明可用。公开 Release 必须：

- 标题为 `Extract & Delete 4.1.2 Developer Preview`。
- 不是 Draft，不使用 GitHub prerelease 标记。
- 只包含 EXE 和 `.sha256` 两项资产。
- 首屏说明 Windows 11 x64、Developer Mode、未签名/SmartScreen、当前用户安装、无自动更新、完整卸载入口、四种格式和回收站行为。

发布后下载真实资产验证：

```powershell
gh release view v4.1.2
gh release download v4.1.2
Get-FileHash .\ExtractAndDelete-Setup-4.1.2-x64.exe -Algorithm SHA256
gh attestation verify .\ExtractAndDelete-Setup-4.1.2-x64.exe --repo everlastingQAQ/ExtractAndDelete
```

再用下载后的 EXE 在干净 Windows 11 x64 VM 完成一次安装、菜单、真实解压、回收和完整卸载验收。

CLI 源码保留在 `src\ExtractAndDelete.Cli` 作为冻结历史，不进入 V4.1.2 安装器、默认构建、测试、发布或使用文档。
