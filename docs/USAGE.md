# Extract & Delete V3 使用文档

V3 是 Windows 11 x64 的 Developer Mode packaged 应用。它复刻 Windows 11“提取压缩文件夹”向导的单页交互，使用 Windows Shell 文件操作处理目录创建、合并、冲突、错误和 UAC；只有完整发布且没有跳过项目时，才会把源压缩包移入回收站。

当前版本不是签名安装包，不提供 `.msix`、`.appinstaller`、MSI 或便携 EXE。开发部署前必须打开 Developer Mode。

## 1. 安全语义

一次任务只处理一个压缩包：

```text
选择压缩包
→ 填入或编辑一个准确目标路径
→ 7-Zip 扫描并验证
→ 写入受控 staging
→ Windows Shell 创建/合并目标并处理冲突
→ 检查逐项结果和 aborted 状态
→ 完整发布且无跳过时验证源文件身份
→ 将源压缩包移入回收站
```

- staging 解压失败或扫描/解压阶段取消：目标不改变，staging 尽力清理，源压缩包保留。
- Windows 发布取消或失败：Shell 已写入的部分目标内容可以保留，不回滚，不回收源包。
- 用户跳过任何文件：目标保留已完成内容，源包保留。
- 替换或“保留两者”完成全部项目：视为完整发布，可以回收源包。
- 回收失败：目标完整保留，源包保留；不永久删除、不自动提权。
- 源文件在开始、发布前或回收前身份变化：停止回收并保留源文件。

## 2. 支持范围

支持 `.zip`、`.7z`、`.rar`、`.tar`，扩展名大小写不敏感。压缩引擎为包内官方 7-Zip 26.02 x64（`7z.exe` 和 `7z.dll`），不读取系统 PATH，也不要求用户另装 7-Zip。

暂不支持密码/加密包、分卷包、`.tar.gz` 等复合扩展名、批量任务、ARM64、Windows 10 和公开签名发布。

## 3. 开发部署

要求：Windows 11 x64、Developer Mode、.NET SDK 10.0.400 feature band、Visual Studio WinUI/C++ x64/Windows SDK 工具链。

在仓库根目录执行：

```powershell
.scriptscheck-dev-environment.ps1
.scriptserify.ps1
.scriptsdeploy-dev.ps1
.scriptserify-dev-install.ps1
```

脚本边界：

1. `check-dev-environment.ps1` 只读检查系统架构、Windows build、Developer Mode、SDK、MSBuild、x64 MSVC、Windows SDK 和 7-Zip 哈希，不修改系统。
2. `verify.ps1` 还原并构建 Core、CLI、WinUI GUI 和 x64 Shell DLL，运行普通测试，不操作真实回收站。
3. `acceptance-check.ps1` 只检查 V3 发布输出布局和清单，不代表 package 已注册。
4. `deploy-dev.ps1` 在验证新输出后，只注销并注册固定 `ExtractAndDelete` package。
5. `verify-dev-install.ps1` 只读检查当前用户 package：`Name=ExtractAndDelete`、`Version=3.0.0.0`、`Status=Ok`、`App Id=App`、固定 COM CLSID、四种 Explorer verb 和实际注册目录文件。

开始菜单中应出现 **Extract & Delete**。注销时运行：

```powershell
.scripts\uninstall-dev.ps1
```

脚本只处理本项目 package；注销后重启 Explorer 或重新登录以刷新右键菜单缓存。

## 4. GUI 使用

### 4.1 开始菜单启动

直接启动应用会先打开 Windows 原生文件选择器。取消选择会退出应用。

选中压缩包后，窗口显示：

```text
提取压缩文件夹
选择一个目标并提取文件
文件将被提取到这个文件夹(F): [准确目标路径] [浏览(R)...]
[x] 完成时显示提取的文件(H)
                                  [提取并回收(E)] [取消]
```

目标框是唯一的、可编辑的准确目录路径，不再有“目标父目录”和“最终目录”两个字段。默认路径只去掉压缩包最后一个扩展名：

```text
D:\下载\演示 archive.7z → D:\下载\演示 archive
```

可直接编辑路径，或点击 **浏览(R)...** 使用 Windows 原生 Folder Picker；浏览选中的目录会直接替换文本框，不会再次追加压缩包名。目标可以是不存在的目录，也可以是已有目录；已有文件会被拒绝。

### 4.2 Explorer 右键

在 Explorer 中选中恰好一个 `.zip`、`.7z`、`.rar` 或 `.tar`，打开 Windows 11 现代菜单并点击 **解压并回收**。Shell Extension 只传递完整 Unicode 路径并激活 GUI，不解压、不回收、不写文件。多选或不支持的格式不会启用命令。

### 4.3 执行和冲突

点击 **提取并回收(E)** 后，路径和按钮会锁定。扫描/提取阶段显示应用进度并可取消；发布阶段显示 Windows Shell 原生进度、冲突、错误和按需 UAC。可按 Windows 习惯选择替换、跳过或保留两者，并可应用到全部项目。

目标发布阶段取消或失败时，已写入的部分目标内容可能保留。应用不会自动删除或回滚这些内容。只要跳过过任何项目，源包一定保留。

### 4.4 完成、打开和关闭

- **完整成功**：没有跳过，所有内容发布成功，源包进入回收站；若勾选“完成时显示提取的文件”则打开目标目录，然后窗口自动关闭。
- **回收失败**：目标已完整发布；若勾选则尝试打开目标，窗口保留并显示黄色警告，源包保留。
- **跳过/取消/发布失败**：窗口保留，显示稳定错误码和文件结果；部分目标内容按 Windows 行为保留。
- **打开目录失败**：核心任务仍算完成，但窗口保留并显示非破坏性警告。

运行中再次从 Explorer 激活时，现有窗口只会前置并提示已有任务，不替换路径、不排队。关闭窗口在可取消阶段会确认“取消提取并退出”；发布和回收期间会阻止关闭。

## 5. CLI 使用

CLI 保持严格、非交互的原子安全模式：

```powershell
ExtractAndDelete.Cli.exe <压缩包路径> <准确目标目录>
```

第二个参数就是完整目标目录，必须不存在。CLI 不合并、不覆盖、不弹冲突框或 UAC；staging 完成后使用同卷 `Directory.Move` 原子发布。GUI 和 CLI 共用扫描、解压、源身份和回收逻辑，只替换发布器。

退出码：`0` 完整成功，`1` 验证/解压/发布失败，`2` 发布成功但回收失败，`3` 用户安全取消。扫描/解压阶段第一次 Ctrl+C 会取消并等待清理；发布或回收阶段提示不可取消并继续等待。

## 6. 常见问题

### Developer Mode 未开启

打开“设置 → 系统 → 面向开发人员 → 开发人员模式”，再运行部署脚本。脚本不会自动修改注册表。

### 目标已有目录

GUI 会交给 Windows Shell 合并并显示冲突选项；CLI 会拒绝已有目标且不修改原内容。

### 密码、分卷或复合扩展名

当前不支持。任务失败，目标不会发布，源包保留。

### 源包为什么还在

只要发生跳过、取消、发布错误、staging 清理错误、源身份变化或回收失败，源包都会保留。成功后的删除动作是移入 Windows 回收站，而不是永久删除。

### Explorer 没有菜单

确认 `deploy-dev.ps1` 和 `verify-dev-install.ps1` 都通过，并重启 Explorer。`acceptance-check.ps1` 只表示输出布局正确。

相关文档：[项目总设计](PROJECT_DESIGN.md)、[V3 验收说明](ACCEPTANCE.md)、[第三方声明](../THIRD-PARTY-NOTICES.md)。
