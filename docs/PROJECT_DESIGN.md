# Extract & Delete 项目总设计文档

## 1. 当前基线

V0.5 已完成并已合入 `main`：

- ZIP 解压。
- 源 ZIP 移入 Windows 回收站。
- Core 工作流。
- CLI 入口。
- V0.5 Core 测试。

V1 当前已落地：

- Core 异步契约、稳定错误码、源文件身份校验和 staging 工作流。
- 真正可取消的 ZIP 扫描/复制、同卷原子发布和回收失败保留语义。
- `IFileOperation` 回收站实现和新的 CLI 退出码。
- .NET 10 / WinUI 3 / Windows App SDK 2.3.1 单项目 packaged GUI。
- 简体中文 ViewModel、Explorer 激活参数解析、单实例状态入口和 Windows 原生文件夹选择器。
- x64 `IExplorerCommand` C++ DLL 源码、package manifest 注册项和 Developer Mode 脚本。

V1.0 的交付定义是 Windows 11 x64 Developer RC。它通过 Windows Developer Mode 部署 packaged 自包含应用，不承诺公开签名安装包。

## 2. 已确定的产品边界

- UI、CLI 和 Explorer 菜单使用简体中文。
- V1 只支持单个 ZIP。
- V1 只支持 Windows 11 x64。
- 每次使用 Windows 原生 Folder Picker 选择目标父目录。
- 最终目录为“父目录\\ZIP 文件名（去扩展名）”。
- 同名目标目录存在时失败并要求重新选择，不覆盖、不合并、不自动改名。
- 单实例、单任务。
- 支持安全取消；Publishing 和 Recycling 开始后取消按钮禁用。
- 解压失败、取消或回收失败时，源 ZIP 永久保留。
- 不自动提权，不提供永久删除回退。
- 不做批量、密码、其他压缩格式、设置页、自动更新、主题和多语言。

## 3. 总体架构

```text
Windows Explorer
        ↓  IExplorerCommand / AppUserModelID
Packaged WinUI 3 GUI
        ↓
ExtractionService
      ↙   ↘
IArchiveExtractor  ICleanupService
      ↓                   ↓
ZipArchiveExtractor  Windows IFileOperation
```

CLI 和 GUI 都是入口层，业务安全边界只有 `ExtractionService`。

Core 不依赖 WinUI、Explorer 或 CLI。

## 4. Core 安全工作流

```text
验证请求
  ↓
打开 ZIP 并扫描条目
  ↓
检查路径、重复项、符号链接和空间
  ↓
创建同卷 staging 目录
  ↓
逐条异步解压
  ↓
失败/取消 → 清理 staging，保留源 ZIP
  ↓
全部成功 → Directory.Move(staging, final)
  ↓
确认源文件身份没有改变
  ↓
IFileOperation + FOFX_RECYCLEONDELETE
  ↓
成功：Completed
失败：CleanupFailed，输出和源 ZIP 均保留
```

最终目录必须在全部解压成功后一次性发布，避免损坏 ZIP 留下用户可见的半成品目录。

## 5. 公共契约

```csharp
public interface IArchiveExtractor
{
    Task<ArchiveExtractionResult> ExtractAsync(
        string archivePath,
        string stagingPath,
        IProgress<ExtractionProgress>? progress,
        CancellationToken cancellationToken);
}

public interface ICleanupService
{
    Task<CleanupResult> MoveToRecycleBinAsync(string filePath);
}

public sealed class ExtractionService
{
    public Task<ExtractAndDeleteResult> ExecuteAsync(
        ExtractAndDeleteRequest request,
        IProgress<ExtractionProgress>? progress,
        CancellationToken cancellationToken);
}
```

工作流结果至少区分：

```text
Completed
Cancelled
ValidationFailed
ExtractionFailed
PublishFailed
CleanupFailed
```

结果还必须包含稳定错误码、简体中文用户信息、源文件处理状态和目标目录是否已发布。调用方不得解析异常文本判断业务状态。

## 6. GUI 设计

GUI 使用 C#、.NET 10、WinUI 3、Windows App SDK 2.3.1、自包含 x64 packaged deployment。

窗口包含：

- ZIP 选择。
- 目标父目录选择。
- 只读最终目录预览。
- 解压并回收按钮。
- 取消按钮。
- 进度、当前文件和状态。

Explorer 激活只预填 ZIP，目标父目录仍必须由用户选择。

## 7. Explorer 集成

`ExtractAndDelete.ShellExtension` 是 x64 原生 C++ COM DLL：

- 实现 `IExplorerCommand`。
- 只注册 `.zip`。
- 只允许单选。
- 只读取选中路径并激活 GUI。
- 不解压、不回收、不执行慢 I/O。

Package manifest 使用 `windows.comServer` 和 `windows.fileExplorerContextMenus` 注册 DLL。

## 8. 测试要求

普通测试不操作真实回收站，使用 fake `ICleanupService` 验证调用边界。真实 `IFileOperation` 测试单独标记为 Windows Integration。

必须覆盖：损坏 ZIP、路径穿越、重复条目、符号链接、目标冲突、staging 失败、复制失败、发布失败、取消、源文件改变、回收失败和单实例激活。

## 9. 版本路线

### V1.0

完成 Core 安全工作流、CLI、WinUI GUI、Windows 11 现代 Explorer 菜单和 Developer Mode 部署验收。

### V2.0

保留 V1 接口和 staging 工作流，新增随包分发的官方 7-Zip `7z.exe + 7z.dll` 进程引擎，首批支持 ZIP、7z、RAR、TAR。非零退出码全部按失败处理，许可证和二进制哈希必须随包记录。

## 10. 构建、部署和工具链

普通 C# 构建与测试：

```powershell
dotnet restore .\ExtractAndDelete.slnx
dotnet build .\ExtractAndDelete.slnx --configuration Release --no-restore
dotnet test .\ExtractAndDelete.slnx --configuration Release --no-build --filter "Category!=WindowsIntegration"
```

`scripts/verify.ps1` 会额外用 MSBuild 构建 x64 C++ Shell Extension，再执行相同的 Release 构建和普通测试。它要求 Visual Studio Native Desktop、x64 MSVC 和 Windows SDK；真实回收站测试使用 `Category=WindowsIntegration` 单独运行。

`scripts/deploy-dev.ps1` 在 Developer Mode 下构建并注册准确的 package manifest；`scripts/uninstall-dev.ps1` 只注销固定 `ExtractAndDelete` identity。仓库不包含 `.pfx`、签名包或正式发布配置。

## 11. 开发流程

不使用 Issue、PR 或额外开发分支。V0.5 收口后直接在 `main` 上按阶段 commit、测试并 push。禁止 force-push、提交证书或跳过失败测试。
