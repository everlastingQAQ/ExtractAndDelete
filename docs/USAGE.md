# Extract & Delete 使用文档

本文档说明当前 V2 Developer RC 的安装、启动和日常使用方式。

当前版本面向 **Windows 11 x64**，交付形式是启用 Developer Mode 后注册的开发部署包，不是带正式签名的公开安装程序。GUI、CLI 和 Explorer 右键菜单共用同一套 Core 工作流。

## 1. 先了解它会做什么

一次任务只处理一个压缩包：

```text
选择压缩包
→ 选择目标父目录
→ 在父目录下计算最终目录
→ 扫描并验证压缩包
→ 解压到同级临时目录
→ 原子发布最终目录
→ 再次确认源文件没有被替换
→ 将源压缩包移入 Windows 回收站
```

安全行为固定如下：

- 解压失败、验证失败或用户取消时，不发布最终目录，源压缩包保留。
- 目标目录已经存在时，不覆盖、不合并、不自动改名。
- 只有完整解压并发布成功后，才会尝试将源压缩包移入回收站。
- 回收失败时，已解压的目录保留，源压缩包也保留；不会永久删除，也不会自动提权。
- 应用内置官方 7-Zip 26.02 x64（`7z.exe` 和 `7z.dll`），不需要另行安装 7-Zip，也不依赖系统 `PATH`。

## 2. 支持范围

| 支持 | 说明 |
| --- | --- |
| `.zip` | 支持，扩展名大小写不敏感 |
| `.7z` | 支持，使用包内 7-Zip 引擎 |
| `.rar` | 支持解压，使用包内 7-Zip 引擎 |
| `.tar` | 支持，使用包内 7-Zip 引擎 |

当前不支持：

- 密码或加密压缩包（没有密码输入界面）。
- 分卷压缩包。
- `.tar.gz`、`.tgz` 等复合扩展名。
- 批量选择、批量解压。
- 覆盖、跳过、合并或自动加序号策略。
- Windows 10、ARM64 和正式签名/Store 安装包。

## 3. 第一次安装（开发部署）

### 3.1 环境要求

- Windows 11 x64。
- Windows Developer Mode。
- .NET SDK 10.0.400（同一 feature band 的补丁版本可以升级）。
- Visual Studio 的 WinUI、C++ x64 和 Windows SDK 工具链：
  - WinUI application development。
  - C++ WinUI/桌面开发工具。
  - Windows SDK。
  - x64 MSVC 工具链。

仓库中的 `deploy-dev.ps1` **不会替你开启 Developer Mode**，也不会下载或安装 Visual Studio。请先在 Windows 设置中打开：

```text
设置 → 系统 → 面向开发人员（For developers）→ 开发人员模式
```

### 3.2 验证、注册和启动

在仓库根目录 `D:\project\ExtractAndDelete` 打开 PowerShell，按顺序执行：

```powershell
.\scripts\verify.ps1
.\scripts\deploy-dev.ps1
.\scripts\acceptance-check.ps1
```

三个脚本的作用：

1. `verify.ps1`：验证内置 7-Zip 文件哈希，Restore，构建 Release 的 Core、CLI、WinUI 和 x64 Explorer DLL，并运行普通测试。
2. `deploy-dev.ps1`：重新构建 x64 Shell DLL，发布自包含 WinUI GUI，并注册准确的 `ExtractAndDelete` package identity。
3. `acceptance-check.ps1`：检查自包含输出、内置引擎、四种格式清单和签名文件排除情况。

注册完成后，从开始菜单启动 **Extract & Delete**。也可以在资源管理器中对一个支持的压缩包打开 Windows 11 现代右键菜单，选择 **解压并回收**。

如果只想构建和运行普通测试、不注册应用：

```powershell
.\scripts\verify.ps1
```

### 3.3 卸载开发部署

在仓库根目录执行：

```powershell
.\scripts\uninstall-dev.ps1
```

该脚本只处理固定的 `ExtractAndDelete` package identity，不搜索、不卸载其他应用。注销后如果资源管理器仍显示旧菜单，重启资源管理器或重新登录 Windows。

## 4. GUI 使用方法

### 4.1 从开始菜单启动

直接启动应用会打开空白窗口。每次新任务按以下步骤操作：

1. 点击 **选择压缩包**，选择一个 `.zip`、`.7z`、`.rar` 或 `.tar` 文件。
2. 点击 **选择文件夹**，使用 Windows 11 原生文件夹选择器选择目标父目录。
3. 查看只读的 **最终目录** 路径，确认无误。
4. 确认提示“仅在完整解压成功后，源压缩包才会移入回收站”。
5. 点击 **解压并回收**。

最终目录的计算规则是：

```text
最终目录 = 所选父目录\压缩包文件名（去掉最后一个扩展名）
```

例如：

```text
压缩包：D:\资料\演示 archive.7z
父目录：D:\输出
最终目录：D:\输出\演示 archive
```

每次新任务都必须重新选择目标父目录，应用不会记忆上一次的父目录。若计算出的最终目录已经存在（无论是文件还是目录），执行按钮会被禁用；请重新选择其他父目录，不要手工让应用覆盖它。

### 4.2 运行中、取消和关闭窗口

- 扫描和解压阶段可以点击 **取消**。应用会停止继续读取、关闭句柄、清理临时目录，然后保留源压缩包。
- **发布** 和 **移入回收站** 阶段不可取消，取消按钮会禁用；此时请等待安全操作完成。
- 在可取消阶段关闭窗口，会询问“取消解压并退出”；确认后要等待临时目录清理完成。
- 在发布或回收阶段关闭窗口会被阻止，并显示正在完成安全操作的提示。
- 任务运行中再次从 Explorer 激活另一个压缩包时，现有窗口会被前置，但当前任务和路径不会被替换，也不会排队执行第二个任务。

### 4.3 结果状态

| 状态 | 含义 | 文件结果 |
| --- | --- | --- |
| 成功 | 解压、验证、发布和回收全部完成 | 最终目录存在，源压缩包已进入回收站 |
| 已取消 | 在扫描/解压阶段安全取消 | 最终目录不存在，临时目录已清理，源压缩包保留 |
| 解压/验证失败 | 压缩包损坏、危险路径、空间不足、引擎失败等 | 不发布最终目录，源压缩包保留 |
| 发布失败 | 临时目录完成但无法原子发布 | 源压缩包保留；临时目录若无法清理会显示准确路径 |
| 回收失败 | 解压完整，但回收站不可用、文件被锁定或操作被系统拒绝 | 最终目录和源压缩包均保留，界面显示黄色警告 |

成功后窗口会保留路径和结果。执行按钮会保持禁用，直到重新选择一个压缩包。

## 5. Explorer 右键菜单使用方法

完成开发部署后：

1. 在资源管理器中选中**恰好一个** `.zip`、`.7z`、`.rar` 或 `.tar` 文件。
2. 右键打开 Windows 11 现代菜单。
3. 点击 **解压并回收**。
4. GUI 会被激活并填入压缩包路径；仍然需要在 GUI 中重新选择目标父目录。
5. 按照 [GUI 使用方法](#4-gui-使用方法) 完成任务。

以下情况不会启用该命令：

- 选中多个文件，即使它们都是支持的格式。
- 选中的项目不是支持的四种扩展名。
- 应用尚未通过 `deploy-dev.ps1` 注册 package。

Shell Extension 只负责读取完整 Unicode 路径并激活 GUI，不会在右键菜单进程中解压、回收或写入文件。因此中文、空格、`&` 和括号路径可以直接使用。

## 6. CLI 使用方法

CLI 的第二个参数是**最终完整目标目录**，与 GUI 的“目标父目录”不同。CLI 不会自动在第二个参数下再拼接压缩包文件名。

### 6.1 使用 Release 输出

先在仓库根目录构建：

```powershell
dotnet restore .\ExtractAndDelete.slnx
dotnet build .\ExtractAndDelete.slnx --configuration Release --no-restore
```

CLI 输出通常位于：

```text
src\ExtractAndDelete.Cli\bin\Release\net10.0-windows10.0.22000.0\
```

从仓库根目录运行示例：

```powershell
& ".\src\ExtractAndDelete.Cli\bin\Release\net10.0-windows10.0.22000.0\ExtractAndDelete.Cli.exe" `
  "D:\资料\演示 archive.7z" `
  "D:\输出\演示 archive"
```

路径含空格、中文、`&` 或括号时必须使用引号。CLI 输出目录中会自动带有包内的 `ThirdParty\7-Zip\7z.exe` 和 `7z.dll`，不需要系统安装 7-Zip。

也可以直接使用 .NET CLI：

```powershell
dotnet run --project .\src\ExtractAndDelete.Cli --configuration Release -- `
  "D:\资料\演示 archive.zip" `
  "D:\输出\演示 archive"
```

CLI 项目当前是 .NET 10 框架依赖输出；使用 `ExtractAndDelete.Cli.exe` 或 `dotnet run` 的机器需要可用的 .NET 10 运行时/SDK。GUI 则是自包含的 Windows App SDK x64 packaged 输出。

### 6.2 CLI 语法和退出码

```text
ExtractAndDelete.Cli.exe <压缩包路径> <最终目标目录>
```

| 退出码 | 含义 |
| ---: | --- |
| `0` | 解压、发布和回收全部成功 |
| `1` | 验证、扫描、解压、校验或发布失败 |
| `2` | 解压和发布成功，但源压缩包移入回收站失败 |
| `3` | 用户安全取消 |

按 `Ctrl+C` 的行为：

- 扫描或解压阶段第一次按下：请求安全取消，等待临时目录清理完成。
- 发布或回收阶段按下：提示当前阶段不可取消，继续等待完成。
- 重复按下不会强制删除文件，也不会触发永久删除 fallback。

## 7. 常见问题

### “Windows Developer Mode is disabled”

在 Windows 设置中打开开发人员模式后，重新运行 `deploy-dev.ps1`。脚本不会自动修改这项系统设置。

### `vswhere.exe`、MSBuild、C++ 或 Windows SDK 找不到

通过 Visual Studio Installer 补齐 WinUI application development、C++ WinUI/桌面工具、Windows SDK 和 x64 MSVC 工具链，然后重新运行 `verify.ps1`。这不是通过安装系统 7-Zip 可以解决的问题。

### 提示内置 7-Zip 引擎不可用或完整性校验失败

应用会故意 fail closed：不启动外部引擎、不发布最终目录、不回收源文件。请不要从 PATH 中安装或替换一个同名 7-Zip 来绕过检查；先重新执行 `verify.ps1`，确认仓库中的 `third_party\7zip\26.02` 文件未被修改。

### 目标目录已经存在

这是预期的保护行为。选择另一个目标父目录，或先由用户自行处理已有目录；应用不会覆盖、合并或自动改名。

### 压缩包需要密码、是分卷包，或文件名是 `.tar.gz`

这些场景当前不支持。应用会返回明确失败，保留源文件，不生成半成品最终目录。

### 界面显示“文件已完整解压，但源压缩包无法移入回收站”

这是 `CleanupFailed`，不是解压失败。最终目录已经完整保留，源压缩包也保留。常见原因包括回收站不可用、网络盘不支持回收、文件被锁定或系统拒绝 Shell 回收操作。应用不会改为永久删除；如需清理源文件，请由用户在确认后自行处理。

### Explorer 中没有“解压并回收”

确认已运行 `deploy-dev.ps1` 且没有报错。该菜单只在已注册 packaged app 后出现，并且只针对单个四格式压缩包。注销或升级后若菜单缓存未刷新，重启 Windows Explorer 或重新登录。

### 应用运行中再次右键另一个压缩包

V2 是单实例、单任务设计。现有窗口会前置并提示已有任务正在进行，不会替换当前路径，也不会排队第二个任务。等待当前任务结束后再开始下一次操作。

## 8. 相关文档和脚本

- [项目总设计文档](PROJECT_DESIGN.md)：架构、边界、安全语义和 V2 技术路线。
- [Developer RC 验收说明](ACCEPTANCE.md)：自动检查、真实回收站测试和干净 VM 验收矩阵。
- [`scripts/verify.ps1`](../scripts/verify.ps1)：构建与普通测试入口。
- [`scripts/deploy-dev.ps1`](../scripts/deploy-dev.ps1)：Developer Mode 注册入口。
- [`scripts/uninstall-dev.ps1`](../scripts/uninstall-dev.ps1)：精确注销本项目 package。
- [`scripts/verify-third-party.ps1`](../scripts/verify-third-party.ps1)：验证内置 7-Zip 二进制与许可证哈希。

内置引擎为官方 7-Zip 26.02 x64，许可证和第三方声明见仓库根目录的 [`THIRD-PARTY-NOTICES.md`](../THIRD-PARTY-NOTICES.md) 及 `third_party/7zip/26.02/licenses/`。
