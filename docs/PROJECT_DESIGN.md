# Extract & Delete 项目总设计文档

## 1. V4.1 基线

V4.1 Developer Preview 版本为 `4.1.0` / package `4.1.0.0`，只支持 Windows 11 x64 和 Developer Mode。应用继续使用 .NET 10、Windows App SDK 2.3.1、single-project MSIX、自包含 x64 输出、固定 package identity 和 Explorer 现代右键菜单。公开交付由 Inno Setup 6.7.3 封装为一个当前用户 EXE 安装器；安装器、卸载器和应用均未使用生产 Authenticode 签名。

可见窗口已经从 WinUI XAML 迁移为 C# WinForms/Win32 Common Controls。Windows App SDK 只保留 packaged 构建、自包含 runtime 和 AppInstance 单实例能力，不参与可见窗口渲染。

内置官方 7-Zip 26.02 x64（`7z.exe`、`7z.dll`）负责扫描和安全 staging 解压，支持 ZIP、7Z、RAR、TAR。CLI 源码仍保留在 `src/ExtractAndDelete.Cli`，但 V4.1 已冻结，不进入默认 solution、测试、发布、安装器或用户交付。

## 2. 产品不变量

- 每次只处理一个压缩包；单实例、单任务，不排队并行任务。
- GUI 只有一个可编辑的准确目标路径，默认值为压缩包所在目录加去掉最后一个扩展名后的文件名。
- 目标可以不存在，也可以是已有目录；Windows Shell 负责创建、合并和冲突交互。
- 压缩引擎永远只写入受控 staging；扫描/解压失败或取消不会发布目标。
- Windows 发布取消或失败时，已写入的部分目标可以保留，不回滚。
- 只有完整发布、无跳过、staging 清理成功、源身份未改变且回收站操作成功，工作流才是 `Completed`。
- 自动删除实际是移入回收站；任何失败都保留源包，不永久删除、不自动提权。
- 不支持密码/加密、分卷、复合扩展名、批量、Windows 10 和 ARM64。

## 3. 组件边界

```text
Explorer COM Shell Extension (C++)
        │ --archive 完整 Unicode 路径
        ▼
Packaged WinForms/Win32 GUI ── WindowsShellDestinationPublisher ── IFileOperation
        │
        └────────────── ExtractionService (Core) ── SevenZipArchiveExtractor
                                      │
                                      └── CleanupService (IFileOperation + recycle)
```

Shell Extension 只解析单个选中项并激活 GUI，不读取压缩包、不创建文件、不解压、不回收。GUI 与 Core 共享验证、7-Zip 扫描/解压、源身份检查、staging 清理和回收逻辑。

`AtomicDirectoryPublisher` 和 `ExtractionService.CreateDefault()` 暂时保留给历史源码和 Core 原子发布测试，不是 V4.1 GUI 交付入口。

## 4. Core 公共契约

Core 公共接口保持不变：

```csharp
public interface IArchiveExtractor
{
    Task<ArchiveExtractionResult> ExtractAsync(
        string archivePath,
        string stagingPath,
        IProgress<ExtractionProgress>? progress,
        CancellationToken cancellationToken);
}

public interface IDestinationPublisher
{
    Task<DestinationPublishResult> PublishAsync(
        string stagingPath,
        string destinationPath,
        IProgress<ExtractionProgress>? progress,
        CancellationToken cancellationToken);
}

public interface ICleanupService
{
    Task<CleanupResult> MoveToRecycleBinAsync(string filePath);
}
```

工作流状态包括 `Validating`、`Scanning`、`Extracting`、`Publishing`、`Recycling` 和 `Completed`。结果区分完整成功、跳过、取消、发布失败、回收失败和源身份变化。只有 `DestinationState.Completed` 时 `DestinationPublished` 才为真；成功还要求 `SourceDisposition.Recycled`。

## 5. 安全解压与发布

```text
验证源与准确目标
→ 记录源身份
→ 创建随机 GUID staging
→ 7-Zip 列表扫描并验证所有条目
→ 检查穿越、重复路径、特殊文件、空间和溢出
→ 逐条解压到 staging
→ 发布前再次检查源身份和源/目标冲突
→ WindowsShellDestinationPublisher 发布
→ 检查逐项 HRESULT 和 aborted 状态
→ 清理 staging
→ 完整且无跳过时移入回收站
```

staging 优先放在目标卷的安全可写位置；不可用时回退到应用专用临时根。所有清理路径拒绝 reparse point、junction 和符号链接。扫描拒绝绝对路径、盘符路径、`..` 穿越、重复路径、文件/目录同名冲突、符号链接、硬链接、ADS 和设备条目。

扫描/解压取消会终止 7-Zip 进程树、关闭句柄、清理 staging、保持目标不变并保留源包。Windows 发布取消或失败允许保留 Shell 已写入的部分目标；应用不自动回滚。

## 6. Windows Shell 发布器

`WindowsShellDestinationPublisher` 在专用 STA 线程初始化 COM，创建 `IFileOperation`，绑定主窗口 HWND，注册 `IFileOperationProgressSink`，并对 staging 顶层项目排队 `CopyItem`。

不设置 `FOF_SILENT`、`FOF_NOCONFIRMATION`、`FOF_NOERRORUI` 或 `FOF_RENAMEONCOLLISION`。允许 `FOFX_SHOWELEVATIONPROMPT` 和 `FOFX_NOCOPYSECURITYATTRIBS`。应用本身不以管理员身份运行；只有用户确认的 Windows 文件操作可显示 UAC。

`PerformOperations` 返回后无论 HRESULT 如何都调用 `GetAnyOperationsAborted`，并通过 `PostCopyItem.hrCopy` 分类成功、跳过、取消和失败。所有项目成功才进入回收；跳过、取消和失败均保留源包。

不调用 `zipfldr.dll` 私有 `extract` 动词：它没有受支持的完整成功/跳过/取消回调。GUI 复刻 Windows 向导外观，真正的目录合并、冲突、错误、进度和 UAC 使用公开 Shell 文件操作。

## 7. 原生 GUI 设计

GUI 工程启用 WinForms，并使用显式 `[STAThread] Program.Main`。入口在任何 HWND 创建前执行：

```text
Application.SetHighDpiMode(PerMonitorV2)
Application.EnableVisualStyles()
Application.SetCompatibleTextRenderingDefault(false)
AppInstance 单实例注册/重定向
```

窗体固定为 `FixedDialog`，不显示图标、最小化和最大化按钮。96 DPI 目标外框为 `784×585`。普通主题固定使用参考浅色界面；高对比度使用 `SystemColors`。文字使用 Segoe UI 与 GDI `TextRenderer`，Shell 图标按当前 DPI 重新获取，不拉伸截图或位图文字。

开始菜单启动使用原生 OpenFileDialog，取消即退出。Explorer 激活填入压缩包和默认目标。运行中第二次激活只前置窗口并提示已有任务。扫描/staging 阶段使用 Shell `IProgressDialog`；Publishing 使用 `IFileOperation` 原生进度和冲突窗口；结果使用原生 MessageBox。

## 8. CLI 冻结与 Explorer

CLI 不在 `ExtractAndDelete.slnx` 中，不参与默认 Restore、Release build、test、publish、acceptance 或用户文档。未来 Core 修改不保证该目录继续独立编译。

Explorer 清单继续注册 `.zip`、`.7z`、`.rar`、`.tar`，单选启用、多选禁用：

```text
Name      ExtractAndDelete
Publisher CN=ExtractAndDelete Developer
Version   4.1.0.0
App ID    App
CLSID     4F4F8F37-B78C-4B3D-90CE-8D16C4483B8E
```

## 9. 单 EXE 安装器与生命周期

发布配置集中在 `release-config.json`，固定以下值：

```text
semanticVersion   4.1.0
packageVersion    4.1.0.0
git tag            v4.1.0
Inno Setup         6.7.3
Installer AppId   {E8A892FB-7B98-4400-B316-083DEF0CEA12}
Install root       %LOCALAPPDATA%\Programs\ExtractAndDelete
Payload            app-4.1.0.0
Package Family     ExtractAndDelete_vyz6krqqgd78c
AUMID              ExtractAndDelete_vyz6krqqgd78c!App
Shell CLSID        4F4F8F37-B78C-4B3D-90CE-8D16C4483B8E
```

`installer\ExtractAndDelete.iss` 以 Inno modern wizard 生成 `ExtractAndDelete-Setup-4.1.0-x64.exe`。安装范围固定为当前用户（`PrivilegesRequired=lowest`），只允许 x64compatible Windows 11，不创建桌面快捷方式，不关闭进程，不导入证书。安装器页面使用仓库内的简体中文 Inno messages，并明确显示 Developer Mode、未签名 SmartScreen、固定路径和两个卸载入口。

`scripts\package-lifecycle.ps1` 是安装器和卸载器唯一的 package 生命周期边界：

```text
Preflight：环境、Developer Mode、运行进程、旧 package 身份、版本降级检查
Install：新 payload 验证 → SHA-256 → 精确注销旧 package → 注册新清单 → 状态验证
         失败时只清理新注册并恢复旧清单
Uninstall：确认 GUI 未运行 → 精确注销 package → 验证无 package → 允许 Inno 清理文件
```

新 payload 在旧 package 验证和新清单注册成功前不会删除旧目录；仓库注册的 4.0 package 只在旧清单身份通过后迁移。高版本阻止降级，同版本运行进入修复。package 注销失败时完整卸载停止文件删除；不强制结束 Explorer 或 `dllhost.exe`。所有失败记录到唯一 `%LOCALAPPDATA%\Temp\ExtractAndDelete-Setup-*.log`，包含 HRESULT。

Windows 会看到两个入口：`Extract & Delete（完整卸载）` 是 Inno 完整卸载器，负责注销 package 并清理其管理文件；`Extract & Delete（系统集成组件）` 是 package 自带入口，只移除注册的开始菜单和 Explorer 集成。文档始终要求优先完整卸载。

`scripts\build-installer.ps1` 先构建 x64 C++ DLL、发布自包含 GUI、复制 package 清单，剔除 PDB/CLI，生成覆盖全部 payload 文件的 `payload.sha256`，再调用 Inno。`scripts\verify-installer.ps1` 检查 EXE 哈希、版本资源、`NotSigned` 状态和 Release 根目录白名单。`check-release-environment.ps1` 定位 VS/MSBuild、.NET 10.0.400、7-Zip 哈希和 Inno 6.7.3；它不自动安装工具或开启 Developer Mode。

`.github/workflows/release.yml` 只接受 `v4.1.0` 这类精确版本 tag，验证 tag 属于 `main`，在 Windows runner 上重建、测试、下载并校验 Inno、生成 EXE 和 GitHub artifact attestation，最后创建公开非 Draft Release。发布资产只有 EXE 和 `.sha256`；attestation/哈希证明来源与完整性，不代替 Authenticode 信任。

## 10. 测试和交付

普通测试覆盖 Core 安全工作流、格式、危险条目、源身份、staging 清理、发布结果、跳过/取消/回收失败和 ViewModel 状态。Windows Integration 使用唯一临时路径，不自动触发真实冲突框或 UAC，不操作测试范围外文件。

GUI 交互验收覆盖开始菜单、Explorer 激活、单实例、路径编辑、默认目标、冲突选择、取消、回收失败、打开目录、高对比度以及 100/125/150/200% DPI。96 DPI 下外框目标为 `784×585`，控件边界误差不超过一个物理像素。

```powershell
.\scripts\verify.ps1
dotnet test .\ExtractAndDelete.slnx --configuration Release --filter "Category=WindowsIntegration"
.\scripts\acceptance-check.ps1
.\scripts\deploy-dev.ps1
.\scripts\verify-dev-install.ps1
```

直接在 `main` 分阶段提交和 push，不创建 Issue、PR 或额外分支，不 force-push，不提交证书、签名包、用户截图或用户文件。
