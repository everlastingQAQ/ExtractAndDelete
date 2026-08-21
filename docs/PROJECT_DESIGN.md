# Extract & Delete 项目总设计文档

## 1. 当前基线

当前实现是 V3 Developer RC，版本 `3.0.0.0`，只面向 Windows 11 x64。应用是 packaged WinUI 3 single-project app，使用 Windows App SDK 2.3.1、自包含 x64 输出、固定 package identity 和 Windows 11 Explorer 现代右键菜单。Developer Mode 是部署前置条件；项目不生成签名 MSIX、MSIXBundle、AppInstaller、MSI 或便携版。

内置官方 7-Zip 26.02 x64（`7z.exe`、`7z.dll`）负责扫描和安全 staging 解压，支持 ZIP、7Z、RAR、TAR。GUI 和 CLI 不搜索 PATH，不 P/Invoke 用户机器上的 7-Zip。

## 2. 产品不变量

- 每次只处理一个压缩包；单实例、单任务，不排队并行任务。
- GUI 只有一个可编辑的准确目标路径。默认值是压缩包所在目录加去掉最后一个扩展名后的文件名。
- GUI 目标可以不存在，也可以是已有目录。Windows Shell 负责创建、合并和冲突交互。
- CLI 第二个参数就是准确目标目录；目标必须不存在，使用原子目录发布，不显示冲突窗口。
- 压缩引擎永远只写入受控 staging；失败或取消不会发布半成品（Windows 发布阶段本身可能留下已写入的部分目标）。
- 只有完整发布、无跳过、staging 清理成功、源身份未改变且回收站操作成功，工作流才是 `Completed`。
- 自动删除实际是 Windows 回收站操作；任何失败都保留源包，不永久删除、不自动提权。
- 源文件身份在开始、发布前、回收前三次检查；丢失或被替换时不回收。
- 不支持密码/加密、分卷、`.tar.gz` 等复合扩展名、批量、Windows 10 和 ARM64。

## 3. 组件边界

```text
Explorer COM Shell Extension (C++)
        │ --archive 完整 Unicode 路径
        ▼
Packaged WinUI 3 GUI ── WindowsShellDestinationPublisher ── IFileOperation
        │
        └────────────── ExtractionService (Core) ── SevenZipArchiveExtractor
                                      │
                                      └── CleanupService (IFileOperation + recycle)

CLI ── ExtractionService (Core) ── AtomicDirectoryPublisher
```

Shell Extension 只解析单个选中项并激活 GUI，不读取压缩包、不创建文件、不解压、不回收。GUI 和 CLI 共享验证、7-Zip 扫描/解压、源身份检查、staging 清理和回收逻辑，不复制 Core 业务规则。

## 4. Core 公共契约

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

public sealed class ExtractionService
{
    public Task<ExtractAndDeleteResult> ExecuteAsync(
        ExtractAndDeleteRequest request,
        IProgress<ExtractionProgress>? progress,
        CancellationToken cancellationToken);
}
```

发布器策略由宿主注入：`AtomicDirectoryPublisher` 用于 CLI，`WindowsShellDestinationPublisher` 用于 GUI。`IDestinationPublisherPolicy` 仅提供是否允许已有目录和发布阶段是否可取消的宿主能力信息。

状态类型：

```text
WorkflowStage: Validating, Scanning, Extracting, Publishing, Recycling, Completed
WorkflowOutcome: Completed, CompletedWithSkippedItems, Cancelled,
                 ValidationFailed, ExtractionFailed, PublishFailed, CleanupFailed
DestinationPublishOutcome: Completed, CompletedWithSkippedItems, Cancelled, Failed
DestinationState: Unchanged, PartiallyModified, CompletedWithSkippedItems, Completed
SourceDisposition: Recycled, Retained, MissingOrChanged
```

`ExtractAndDeleteResult.DestinationPublished` 是兼容属性，仅在 `DestinationState.Completed` 时为真。`Success` 还要求 `SourceDisposition.Recycled`，因此“解压完整但回收失败”不会被误判为完整成功。

错误码是稳定枚举，至少包括：源不存在、格式不支持、目标无效、目标已有、ZIP 不可读/加密、危险/重复条目、空间不足、解压 I/O、发布失败、发布取消、跳过条目、目标创建失败、源身份改变、回收站不可用/失败、staging 清理失败和未预期错误。用户界面显示简体中文 `UserMessage`，不解析异常文本来判断业务状态。

## 5. 安全解压与 staging

工作流：

```text
验证源与准确目标
→ 记录源文件 ID、卷序列号、大小和最后写入时间
→ 创建随机 GUID staging
→ 7-Zip 列表扫描并验证所有条目
→ 检查溢出、空间、穿越、重复路径、特殊文件和 reparse
→ 逐条异步解压到 staging
→ 再次检查源身份
→ 检查 staging 输出不会覆盖源包
→ 调用宿主发布器
→ 检查发布 HRESULT、逐项回调和 aborted
→ 清理 staging
→ 再次检查源身份
→ 完整且无跳过时移入回收站
```

staging 优先放在目标路径所在卷的最近安全可写祖先；目标父路径缺失或受保护时回退到应用专用临时根。所有清理路径必须是 GUID 前缀目录且拒绝 reparse point、junction 和符号链接。Windows Shell 复制发布会同时占用 staging 和目标空间，因此空间检查采取保守值。

扫描拒绝绝对路径、盘符路径、`..` 穿越、Windows 大小写不敏感重复路径、文件/目录同名冲突、符号链接、硬链接、ADS、设备和 reparse 条目。文件使用 `FileMode.CreateNew`、异步流复制和取消令牌；不继承 ZIP 中的系统、隐藏或可执行权限属性。

扫描/解压取消会终止 7-Zip 进程树、关闭句柄、清理 staging、保持目标不变并保留源包。若引擎无法确认进程停止，返回准确 staging 路径，不扩大删除范围。

## 6. Windows Shell 发布器

`WindowsShellDestinationPublisher` 在专用 STA 线程初始化 COM，创建 `IFileOperation`，调用 `SetOwnerWindow` 绑定 WinUI 主窗口，注册 `IFileOperationProgressSink`，并对 staging 每个顶层项目排队 `CopyItem`。目标缺失目录由 `NewItem` 逐级创建，已有目录直接解析。

不设置 `FOF_SILENT`、`FOF_NOCONFIRMATION`、`FOF_NOERRORUI` 或 `FOF_RENAMEONCOLLISION`。允许 `FOFX_SHOWELEVATIONPROMPT` 和 `FOFX_NOCOPYSECURITYATTRIBS`。应用本身不以管理员运行；只有用户确认的 Windows 文件操作可显示 UAC。

`PerformOperations` 返回后无论 HRESULT 如何都调用 `GetAnyOperationsAborted`。`PostCopyItem.hrCopy` 分类如下：

```text
S_OK、S_FALSE、COPYENGINE_S_MERGE、COPYENGINE_S_KEEP_BOTH、
COPYENGINE_S_COLLISIONRESOLVED、COPYENGINE_S_ALREADY_DONE → 成功
COPYENGINE_S_USER_IGNORED → 跳过
COPYENGINE_E_USER_CANCELLED 或 aborted → 取消
其他 HRESULT → 发布失败
```

所有项目成功才进入回收；跳过返回 `CompletedWithSkippedItems` 并保留源包；取消/错误允许保留 Shell 已写入的部分目标并保留源包。空 staging 仍创建目标并视为完整发布。

不直接调用 `zipfldr.dll` 私有 `extract` 动词：系统提取窗口没有受支持的完整成功/跳过/取消回调，不能安全地把返回当作回收依据。V3 复刻向导外观，但把真正的合并、冲突、错误、进度和 UAC 交给公开的 `IFileOperation`。

## 7. GUI 交互

初始窗口按 Windows 11 中文“提取压缩文件夹”结构复刻：返回箭头、压缩文件夹图标、蓝色标题、一个可编辑目标框、`浏览(R)...`、默认勾选的“完成时显示提取的文件(H)”、`提取并回收(E)` 和 `取消`。使用系统字体、主题、高 DPI 和高对比度；不复制私有 Windows 位图或 DLL。

开始菜单启动先显示原生 File Picker，取消即退出。Explorer 激活填入路径和默认目标。空闲时新激活替换向导状态并恢复勾选；运行中只前置窗口并拒绝新任务。完整成功按勾选打开目标，然后自动关闭；回收失败会打开目标但保留警告窗口；跳过、取消和发布失败保留窗口。

运行中锁定路径和浏览按钮。扫描、解压、GUI Shell 发布可取消；回收阶段不可取消。关闭窗口在可取消阶段询问并等待清理，发布/回收阶段阻止关闭。打开目标使用 `ProcessStartInfo.ArgumentList`，不经 PowerShell、cmd 或字符串 shell。

## 8. CLI 与 Explorer

CLI：

```text
ExtractAndDelete.Cli.exe <压缩包路径> <准确目标目录>
```

CLI 目标必须不存在，不弹冲突/UAC；使用同卷 staging + `Directory.Move`。退出码 `0` 完整成功、`1` 验证/解压/发布失败、`2` 回收失败、`3` 安全取消。

Explorer 清单固定注册 `.zip`、`.7z`、`.rar`、`.tar`，单选启用、多选禁用。Package identity 保持：`ExtractAndDelete`、`CN=ExtractAndDelete Developer`、Application Id `App`、固定 CLSID `4F4F8F37-B78C-4B3D-90CE-8D16C4483B8E`，V3 版本 `3.0.0.0`。

## 9. 测试和验收

普通测试覆盖格式、危险条目、重复路径、空间/溢出、源身份、staging 清理、目标路径、发布结果映射、跳过/取消/部分完成、回收失败和 GUI ViewModel。发布器使用 fake adapter 确定性模拟 `S_OK`、merge、keep-both、skip、cancel、aborted、UAC 拒绝和部分失败。

Windows Integration 测试只使用唯一临时路径，覆盖真实回收站、Unicode/空格/`&`/括号目标、新目录发布和已有目录无冲突合并；不自动触发真实冲突框或 UAC，不删除测试范围外文件。

最终验收顺序：

```powershell
.\scripts\verify.ps1
dotnet test .\ExtractAndDelete.slnx --configuration Release --filter "Category=WindowsIntegration"
.\scripts\acceptance-check.ps1
.\scripts\deploy-dev.ps1
.\scripts\verify-dev-install.ps1
```

必须确认四种格式、冲突替换/跳过/保留两者、两阶段取消、UAC 拒绝、回收失败、单实例激活、开始菜单启动、注销刷新和 package `3.0.0.0 / Ok`。

## 10. Git 与后续路线

直接在 `main` 分阶段提交，不创建 Issue、PR 或额外分支，不 force-push，不提交证书、签名包或用户文件。建议提交边界：

1. `refactor(core): introduce destination publisher contracts`
2. `feat(core): add interactive Windows shell publishing`
3. `feat(gui): mirror the Windows extraction wizard`
4. `test(v3): cover merge skip cancellation and recycling`
5. `docs(build): publish the v3 developer workflow`

V2 的 7-Zip 引擎和 V3 staging/publisher 契约是后续扩展基础；任何未来格式或引擎失败都必须继续满足“不发布半成品、不修改既有目录、不回收源包”。
