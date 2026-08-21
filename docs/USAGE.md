# Extract & Delete 4.1.1 使用文档

`Extract & Delete 4.1.1 Developer Preview` 是一个面向 Windows 11 x64 的单 EXE 安装版本。它仍需要 Windows Developer Mode；安装器未使用生产 Authenticode 签名，因此 SmartScreen 可能显示“未知发布者”警告。4.1.1 修复了 4.1.0 的 package 清单入口错误。

普通用户只需要从 GitHub Release 下载并运行：

```text
ExtractAndDelete-Setup-4.1.1-x64.exe
```

不需要克隆仓库、安装 Visual Studio、.NET、Windows App SDK 或系统 7-Zip。安装器将自包含的 GUI、Shell Extension、Windows App SDK、.NET 运行时和 7-Zip 26.02 放入当前用户目录：

```text
%LOCALAPPDATA%\Programs\ExtractAndDelete
```

## 1. 安装前提

- Windows 11 x64，build 不低于 22000。
- Developer Mode 已开启。
- 当前用户对 `%LOCALAPPDATA%` 有写入权限。
- 安装期间 Extract & Delete 不在运行。

安装器不会自动开启 Developer Mode、导入证书、请求管理员权限、关闭 Explorer 或强制结束解压任务。

### 开启 Developer Mode

打开：

```text
设置 → 系统 → 面向开发人员 → 开发人员模式
```

安装器发现未开启时会停止在预检页，并提供“打开开发者设置”和“重新检测”。开启后回到安装器重新检测即可。

## 2. 下载、校验和安装

1. 在 GitHub Release 下载 `ExtractAndDelete-Setup-4.1.1-x64.exe`。`.sha256` 文件是可选的完整性校验文件。
2. 可选地在 PowerShell 中校验：

   ```powershell
   Get-FileHash .\ExtractAndDelete-Setup-4.1.1-x64.exe -Algorithm SHA256
   Get-Content .\ExtractAndDelete-Setup-4.1.1-x64.exe.sha256
   ```

3. 双击 EXE。若 SmartScreen 显示未知发布者，确认文件来自本项目的 GitHub Release 后选择“更多信息 → 仍要运行”。
4. 安装器显示固定的当前用户目录；目录不可修改，不创建桌面快捷方式，不请求 UAC。
5. 阅读 Developer Preview 提示并开始安装。
6. 完成页默认勾选“运行 Extract & Delete”；取消勾选则只完成安装。

安装成功后：

- 开始菜单显示 `Extract & Delete`。
- Explorer 对单个 ZIP、7Z、RAR 或 TAR 显示“解压并回收”。
- 系统不需要另装 7-Zip、.NET 或 Windows App SDK Runtime。

可用 PowerShell 确认 package：

```powershell
Get-AppxPackage -Name ExtractAndDelete |
    Select-Object Name, PackageFullName, Version, Status, InstallLocation
```

预期为 `Name=ExtractAndDelete`、`Version=4.1.1.0`、`Status=Ok`，安装目录末尾为 `app-4.1.1.0`。

## 3. 升级、修复和迁移

升级没有自动更新。下载更高版本安装器并再次运行即可。

- 已安装更低版本时，安装器先写入新 `app-4.1.1.0`，验证 payload 后再切换 package 注册。
- 如果旧版本的 `InstallLocation` 为空、目录丢失或清单仍有 `$targetnametoken$` 占位符，4.1.1 会将其识别为损坏残留并只修复固定的 ExtractAndDelete package。
- 当前仓库注册的 4.0 开发版会自动迁移；仓库目录、源代码和构建输出不会删除。
- 再次运行同一版本会进入修复流程，不会产生第二个 package 或第二个完整卸载入口。
- 高于安装器版本的 package 不会被降级。
- 新 package 注册失败时，安装器会尽力恢复旧清单，并在 `%LOCALAPPDATA%\Temp\ExtractAndDelete-Setup-*.log` 中记录 HRESULT 和诊断。

安装器执行 package 注册或回滚时暂时不能取消。若 GUI 正在解压，安装器会等待用户先正常结束任务，不会强制结束进程。

## 4. 解压和回收

### 从开始菜单启动

启动 `Extract & Delete` 后先出现 Windows 原生文件选择器。它只显示 `.zip`、`.7z`、`.rar`、`.tar`，只允许选择一个文件；取消选择会直接退出应用。

### 从 Explorer 启动

在 Explorer 中选中一个支持格式，打开 Windows 11 现代右键菜单并点击 **解压并回收**。多选时命令不可用。Shell Extension 只传递完整 Unicode 路径并激活 GUI，不直接读取压缩包或写文件。

### 向导

窗口按 Windows 11 中文“提取压缩(Zipped)文件夹”向导布局工作：

```text
提取压缩(Zipped)文件夹

选择一个目标并提取文件

文件将被提取到这个文件夹(F):
[准确目标路径                         ] [浏览(R)...]

[x] 完成时显示提取的文件(H)

                             [提取并回收(E)] [取消]
```

目标框是唯一的准确目标路径：

- 默认只去掉压缩包最后一个扩展名，例如 `D:\下载\演示.7z` → `D:\下载\演示`。
- 可以直接编辑绝对本地路径或有效 UNC 路径。
- “浏览”选择的目录直接替换文本框，不追加压缩包名称。
- 目标可以不存在，也可以是已有目录；Windows Shell 负责创建、合并、冲突提示、错误和按需 UAC。
- 目标如果是文件，执行按钮会被禁用。

快捷键：`Alt+F` 聚焦路径，`Alt+R` 浏览，`Alt+H` 切换复选框，`Alt+E` 执行，`Enter` 执行，`Esc` 取消或关闭。

### 处理阶段

```text
7-Zip 扫描与安全校验
→ 受控 staging 解压
→ Windows Shell 创建/合并目标并处理冲突
→ 再次确认源文件身份
→ 将源压缩包移入回收站
```

扫描和 staging 阶段可取消；Windows Shell 发布阶段显示原生复制、冲突、错误和 UAC 界面；回收阶段不可取消。

## 5. 源文件和目标行为

- 完整解压且没有跳过条目后，源压缩包才会移入回收站。
- “替换”或“保留两者”并完成全部条目，视为完整发布，可以回收源包。
- 任意条目选择“跳过”时，目标保留已完成内容，但源包保留。
- 发布取消、发布失败或 UAC 被拒绝时，Windows 已写入的部分目标可能保留；应用不回滚这些内容，源包保留。
- 扫描/staging 失败或取消时，不发布目标，staging 尽力清理，源包保留。
- 回收失败时目标和源包都保留；绝不永久删除或自动提权。
- 源文件在开始、发布前或回收前改变时，不执行回收。
- 成功后若勾选“完成时显示提取的文件”，应用尝试打开目标目录并随后自动关闭；打开失败只显示非破坏性警告。

支持 ZIP、7Z、RAR、TAR。密码包、加密包、分卷包、`.tar.gz` 等复合扩展名、批量任务、Windows 10 和 ARM64 不在本版本范围内。

## 6. 卸载

Windows 可能显示两个入口：

```text
Extract & Delete（完整卸载）
Extract & Delete（系统集成组件）
```

优先使用 **Extract & Delete（完整卸载）**：它先精确注销 package，再删除安装器管理的 payload、脚本、许可证和卸载项。若 Shell DLL 被占用，会停止并要求关闭应用或注销登录后重试；它不会强制终止 Explorer，不会删除用户压缩包、解压目录或安装目录外文件。

只移除“系统集成组件”会让开始菜单和 Explorer 菜单消失，但安装文件仍保留；重新运行同版本安装器即可修复注册。

完整卸载后重启 Explorer 或重新登录，以刷新右键菜单缓存。

## 7. 常见问题

### 没有右键菜单

确认 Developer Mode 已开启，检查 package 的 `Status=Ok`、`Version=4.1.1.0` 和非空 `InstallLocation`，然后重启 Explorer。重新运行 4.1.1 安装器可执行修复。

### 安装器显示未知发布者

这是 Developer Preview 的预期现象。安装器、卸载器和应用没有生产 Authenticode 签名；SHA-256 和 GitHub 构建证明只能证明文件来源和完整性，不能替代 Windows 受信任签名。

### 源压缩包还在

只要跳过、取消、发布失败、源身份改变或回收失败，源包就会保留。成功后的动作是移入回收站，而不是永久删除。

### 需要日志

安装预检、迁移、注册和回滚日志位于 `%LOCALAPPDATA%\Temp\ExtractAndDelete-Setup-*.log`。把对应日志中的 HRESULT 和最后一条失败消息用于排查；不要删除或修改用户解压数据来“修复”安装。

开发者构建、测试和发布流程见：[项目总设计](PROJECT_DESIGN.md)、[V4.1.1 验收说明](ACCEPTANCE.md)、[4.1.1 Release Notes](releases/v4.1.1.md)、[4.1.0 历史 Release Notes](releases/v4.1.0.md)、[第三方声明](../THIRD-PARTY-NOTICES.md)。CLI 源码仍保留作历史参考，但已冻结，不属于用户交付。
