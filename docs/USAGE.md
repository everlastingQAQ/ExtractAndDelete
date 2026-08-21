# Extract & Delete V4 使用文档

V4 是 Windows 11 x64 Developer Mode packaged 应用，版本 `4.0.0.0`。可见窗口使用 WinForms/Win32 原生控件，按 Windows 11 中文“提取压缩(Zipped)文件夹”向导布局绘制；真正的目录合并、冲突、错误、进度和 UAC 由 Windows Shell 文件操作处理。

当前没有正式签名安装包，也没有 MSI、AppInstaller、便携 EXE 或 CLI 交付物。使用前必须在 Windows 设置中开启 Developer Mode。

## 1. 安全行为

```text
选择压缩包
→ 计算或编辑准确目标路径
→ 7-Zip 扫描并验证
→ 写入受控 staging
→ Windows Shell 创建/合并目标并处理冲突
→ 检查逐项结果和 aborted 状态
→ 完整发布且没有跳过时验证源文件身份
→ 将源压缩包移入回收站
```

- 扫描或 staging 解压失败/取消：目标不改变，staging 尽力清理，源包保留。
- Windows 发布取消/失败：Shell 已经写入的部分目标可以保留，不回滚，不回收源包。
- 任意条目选择跳过：目标保留已完成内容，源包保留。
- 替换或保留两者并完成所有条目：视为完整发布，可以回收源包。
- 回收失败：目标和源包都保留，不永久删除、不自动提权。
- 源文件身份在开始、发布前或回收前发生变化：不执行回收。

## 2. 支持范围

支持 `.zip`、`.7z`、`.rar`、`.tar`，扩展名大小写不敏感。压缩引擎为包内官方 7-Zip 26.02 x64，不读取系统 PATH，也不要求系统另外安装 7-Zip。

不支持密码/加密包、分卷包、`.tar.gz` 等复合扩展名、批量任务、Windows 10 和 ARM64。

## 3. 安装和开发部署

要求：

- Windows 11 x64，build 不低于 22000。
- Developer Mode 已开启。
- .NET SDK 10.0.400 feature band。
- Visual Studio C++ x64、Windows SDK 和 Windows App SDK/WinUI 构建工具。

在仓库根目录按顺序执行：

```powershell
.\scripts\check-dev-environment.ps1
.\scripts\verify.ps1
.\scripts\deploy-dev.ps1
.\scripts\verify-dev-install.ps1
```

脚本职责：

1. `check-dev-environment.ps1` 只读检查系统、Developer Mode、SDK、MSBuild、x64 MSVC、Windows SDK 和 7-Zip 哈希。
2. `verify.ps1` 构建 Core、WinForms GUI、x64 Shell DLL，并运行普通测试；不构建 CLI。
3. `acceptance-check.ps1` 只检查 Release 输出布局、V4 清单、包内 7-Zip、Shell DLL 和自包含运行时；不检查 package 是否注册。
4. `deploy-dev.ps1` 在构建和输出检查完成后，只注销并注册固定 `ExtractAndDelete` package。
5. `verify-dev-install.ps1` 只读检查当前用户 package：`Name=ExtractAndDelete`、`Version=4.0.0.0`、`Status=Ok`、`App Id=App`、固定 CLSID、四种 Explorer verb 和注册目录文件。

注销：

```powershell
.\scripts\uninstall-dev.ps1
```

注销后重启 Explorer 或重新登录，以刷新开始菜单和现代右键菜单缓存。

## 4. GUI 使用

### 4.1 开始菜单启动

从开始菜单启动 **Extract & Delete**，应用首先显示 Windows 原生文件选择器：

- 过滤 `.zip`、`.7z`、`.rar`、`.tar`。
- 只允许选择一个文件。
- 取消选择会退出应用。

### 4.2 Explorer 右键启动

在 Explorer 中选中恰好一个支持格式，打开 Windows 11 现代右键菜单，点击 **解压并回收**。Shell Extension 只传递完整 Unicode 路径并激活 GUI，不直接解压、回收或写文件。多选时命令禁用。

### 4.3 原生向导

窗口包含：

```text
提取压缩(Zipped)文件夹

选择一个目标并提取文件

文件将被提取到这个文件夹(F):
[准确目标路径                         ] [浏览(R)...]

[x] 完成时显示提取的文件(H)

                             [提取并回收(E)] [取消]
```

普通主题下窗口使用参考图的浅色系统外观；高对比度模式服从系统颜色。窗口固定大小，不支持最大化和自由缩放。文本、边框和按钮由 Win32 Common Controls/GDI 绘制，不使用缩放后的截图或位图文字。

默认目标路径只去掉压缩包最后一个扩展名：

```text
D:\下载\演示 archive.7z
→ D:\下载\演示 archive
```

路径框是唯一的准确目标路径：

- 可以直接编辑。
- 可以输入不存在的绝对本地路径或有效 UNC 路径。
- 点击 **浏览(R)...** 后，所选目录直接替换文本框内容。
- 不会再次追加压缩包名称。
- 目标可以是已有目录；Windows Shell 负责合并和冲突提示。
- 目标如果是文件，执行按钮会被禁用。

快捷键：

```text
Alt+F  聚焦并选择目标路径
Alt+R  浏览目标文件夹
Alt+H  切换“完成时显示提取的文件”
Alt+E  执行“提取并回收”
Enter  执行
Esc    取消或关闭
```

### 4.4 执行、取消和冲突

点击 **提取并回收(E)** 后，路径、浏览按钮和复选框锁定。

- 扫描/staging 阶段使用 Windows 原生进度框，可以取消。
- Publishing 阶段使用 Windows Shell 原生进度、冲突、错误和 UAC 界面。
- 回收阶段不可取消。
- 发布取消或失败后，已写入的部分目标内容可能保留。
- 应用不会自动删除或回滚 Windows 已经写入的目标内容。

冲突时可以使用 Windows 提供的替换、跳过、保留两者和应用到全部选项。只有没有跳过并且所有项目完成，源压缩包才会进入回收站。

### 4.5 完成结果

- 完整成功：源包移入回收站；若复选框勾选则打开目标目录，然后窗口自动关闭。
- 回收失败：目标已完整发布，源包保留，窗口显示黄色警告。
- 跳过项目：目标保留部分结果，源包保留，窗口保留。
- 发布取消/失败：目标可能部分存在，源包保留，窗口保留。
- staging 解压失败/取消：目标不改变，源包保留。
- 打开目录失败：核心任务仍算完成，但窗口显示非破坏性警告。

运行中再次从 Explorer 激活时，只前置当前窗口并提示已有任务，不替换路径、不排队、不并行。关闭窗口在可取消阶段会确认取消；发布或回收阶段会阻止关闭。

## 5. 常见问题

### Developer Mode 未开启

打开“设置 → 系统 → 面向开发人员 → 开发人员模式”，再运行部署脚本。脚本不会自动修改注册表。

### 右键菜单没有出现

依次确认 `deploy-dev.ps1` 和 `verify-dev-install.ps1` 都成功，然后重启 Explorer。`acceptance-check.ps1` 成功只表示构建输出正确，不表示 package 已注册。

### 源压缩包为什么还在

只要出现跳过、取消、发布失败、staging 清理失败、源身份变化或回收失败，源包都会保留。成功后的删除动作是移入回收站，不是永久删除。

### CLI 在哪里

CLI 源码仍保留在 `src/ExtractAndDelete.Cli` 作为历史参考，但 V4 已冻结，不进入默认 solution、构建、测试、发布和使用文档。

相关文档：[项目总设计](PROJECT_DESIGN.md)、[V4 验收说明](ACCEPTANCE.md)、[第三方声明](../THIRD-PARTY-NOTICES.md)。
