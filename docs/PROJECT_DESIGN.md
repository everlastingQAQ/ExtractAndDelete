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
- .NET 10 / WinUI 3 / Windows App SDK 2.3.1 单项目 packaged GUI，启用 .NET 与 Windows App SDK x64 自包含部署，使用模板 PNG 占位图标。
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

实现规则：

- 源文件扩展名按 `OrdinalIgnoreCase` 判断为 `.zip`；最终目录和父目录在执行前必须不存在/有效。
- ZIP 条目先完整扫描，再检查声明总大小、溢出、目标卷可用空间、绝对路径、盘符路径、`..` 穿越、Windows 大小写重复路径、文件/目录冲突和符号链接/reparse 属性。
- staging 目录在最终目录同级生成随机 GUID 名称，保证与最终目录同卷；文件使用 `FileMode.CreateNew`、异步复制、取消令牌和有限缓冲区，目录条目也被保留。
- 所有条目成功后使用同卷 `Directory.Move` 发布；发布失败不搬运单个文件。staging 清理会拒绝穿越 reparse-point，并在失败时返回准确路径。
- 解压期间源 ZIP 以禁止写入/删除的共享模式打开，并在发布前、回收前比较卷序列号、文件 ID、长度和最后写入时间。

取消窗口只存在于 Scanning 和 Extracting。取消会关闭源句柄、清理 staging、保持最终目录不存在并保留源 ZIP；Publishing 和 Recycling 开始后取消被忽略，已发布目录不会回滚。

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

当前错误码包括：`InvalidArchivePath`、`ArchiveNotFound`、`UnsupportedFormat`、`InvalidDestination`、`DestinationAlreadyExists`、`ArchiveUnreadable`、`UnsafeArchiveEntry`、`DuplicateArchiveEntry`、`ArchiveEntryConflict`、`InsufficientDiskSpace`、`ArchiveSizeOverflow`、`ExtractionIoFailure`、`PublishFailure`、`SourceChanged`、`RecycleUnavailable`、`RecycleFailed`、`StagingCleanupFailed`、`Cancelled` 和 `Unexpected`。结果同时提供只读兼容属性 `Success => Outcome == Completed`。

`SourceDisposition` 为 `Recycled`、`Retained` 或 `MissingOrChanged`；失败状态永远不调用永久删除 API。

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

每次任务必须重新选择父目录；选择 ZIP 会清除旧父目录。最终目录冲突时禁止执行并提示重新选择，不覆盖、不合并、不自动加序号。执行期间路径控件、重复执行和新的任务均禁用；完成后保留路径和结果，成功状态在重新选择 ZIP 前保持禁用。进度按字节显示，零字节 ZIP 按条目显示；回收失败显示黄色警告，不自动打开目录、不发通知、不播放声音。窗口关闭在可取消阶段请求取消并等待清理，在发布/回收阶段阻止关闭。

GUI 通过 `AppInstance` 使用固定主实例。空闲时收到 Explorer 的新 ZIP 激活会清除旧父目录；运行中只前置现有窗口并显示“当前已有解压任务正在进行”，不替换路径、不排队。第二次直接启动同样只前置主窗口。

## 7. Explorer 集成

`ExtractAndDelete.ShellExtension` 是 x64 原生 C++ COM DLL：

- 实现 `IExplorerCommand`。
- 只注册 `.zip`。
- 只允许单选。
- 只读取选中路径并激活 GUI。
- 不解压、不回收、不执行慢 I/O。

Package manifest 使用 `windows.comServer` 和 `windows.fileExplorerContextMenus` 注册 DLL。

Shell Extension 使用固定 CLSID、x64 架构和包身份，只对单个 `.zip` 项显示/启用命令“解压并回收”。`Invoke` 只获取完整 Unicode 路径，通过 `IApplicationActivationManager` 传递 `--archive` 参数；不执行解压、回收或文件写入，所有异常在 COM 边界转为 HRESULT。

回收服务在专用 STA 线程创建 `IFileOperation`，设置 `FOFX_RECYCLEONDELETE`、early-failure、静默/无确认/无错误 UI 标志，检查实际 HRESULT 和 `GetAnyOperationsAborted`。不提权、不调用 `File.Delete`、不提供永久删除 fallback；回收失败时输出和源 ZIP 都保留。

## 8. 测试要求

普通测试不操作真实回收站，使用 fake `ICleanupService` 验证调用边界。真实 `IFileOperation` 测试单独标记为 Windows Integration。

必须覆盖：损坏 ZIP、路径穿越、重复条目、符号链接、目标冲突、staging 失败、复制失败、发布失败、取消、源文件改变、回收失败和单实例激活。

普通测试使用 fake `IArchiveExtractor`/`ICleanupService`，覆盖默认启动、Unicode/空格/`&`/括号参数、Folder Picker 后才能执行、目标冲突、运行中二次激活、各 Outcome 文案和取消按钮状态。Windows Integration 只使用唯一临时文件验证真实回收站路径消失和 aborted/error 状态，失败时绝不尝试永久删除。

Developer RC 的手工验收必须在干净 Windows 11 x64、Developer Mode、无 .NET/Windows App SDK 运行时的虚拟机完成：自包含 GUI 启动、单个 ZIP 现代菜单、非 ZIP/多选过滤、中文和特殊路径传递、单实例激活、损坏 ZIP/大 ZIP 取消、发布后取消、回收失败保留语义，以及注销包后 Explorer 命令和残留注册消失。

## 9. 版本路线

### V1.0

完成 Core 安全工作流、CLI、WinUI GUI、Windows 11 现代 Explorer 菜单和 Developer Mode 部署验收。

### V2.0

保留 V1 接口和 staging 工作流，新增随包分发的官方 7-Zip `7z.exe + 7z.dll` 进程引擎，首批支持 ZIP、7z、RAR、TAR。非零退出码全部按失败处理，许可证和二进制哈希必须随包记录。

V2 不搜索 PATH、不直接 P/Invoke `7z.dll`，使用固定绝对路径、`UseShellExecute=false`、`ArgumentList`、stdout/stderr 上限和进程树取消。新增 `SevenZipArchiveExtractor`、`SevenZipProcessRunner` 与 `SupportedArchiveFormats`，成功后继续复用 V1 的发布、源身份检查和回收逻辑。密码、多卷、复合扩展名、批量、覆盖/跳过/自动改名、Windows 10、ARM64、公开签名安装包和 Store 发布延期。V2 开始固定官方 7-Zip 版本和 SHA-256，随包提供许可证、Third-Party Notices 和源码链接；任何格式失败、进程崩溃或取消仍不得发布最终目录或回收源文件。

## 10. 构建、部署和工具链

普通 C# 构建与测试：

```powershell
dotnet restore .\ExtractAndDelete.slnx
dotnet build .\ExtractAndDelete.slnx --configuration Release --no-restore
dotnet test .\ExtractAndDelete.slnx --configuration Release --no-build --filter "Category!=WindowsIntegration"
```

`scripts/verify.ps1` 会额外用 MSBuild 构建 x64 C++ Shell Extension，再执行相同的 Release 构建和普通测试。它要求 Visual Studio Native Desktop、x64 MSVC 和 Windows SDK；真实回收站测试使用 `Category=WindowsIntegration` 单独运行。

`scripts/deploy-dev.ps1` 在 Developer Mode 下构建并注册准确的 package manifest；`scripts/uninstall-dev.ps1` 只注销固定 `ExtractAndDelete` identity。仓库不包含 `.pfx`、签名包或正式发布配置。

`scripts/acceptance-check.ps1` 检查自包含 x64 输出（无外部 Windows App Runtime 依赖且包含 runtime DLL/PNG 资源）、固定包身份、`.zip` 菜单和仓库中没有证书/签名包。所有脚本均使用精确路径和固定包身份，不搜索或卸载其他包。

## 11. 开发流程

不使用 Issue、PR 或额外开发分支。V0.5 收口后直接在 `main` 上按阶段 commit、测试并 push。禁止 force-push、提交证书或跳过失败测试。

V1 的完成定义：Release 无警告、普通测试和 Windows Integration 全绿；GUI、CLI、Explorer 共用同一 Core 工作流；失败/取消不发布半成品、既有目录不变、回收失败不永久删除；无自动提权；Developer Mode 注册、运行、注销和文档与真实行为一致。当前工作区已完成 C#、Core、CLI、GUI、Shell 源码、脚本和自动检查；本机若未安装 Native Desktop/C++/Windows SDK，只能明确报告 Shell DLL 和 VM 验收待在具备该工具链的 Windows 11 环境完成。
