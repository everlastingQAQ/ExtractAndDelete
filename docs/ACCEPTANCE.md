# Extract & Delete V4 Developer RC 验收说明

V4 版本为 `4.0.0.0`，交付形式是 Windows 11 x64 Developer Mode packaged 应用。可见界面由 WinForms/Win32 原生控件创建；CLI 源码保留但已冻结，不属于默认构建、测试、发布或用户交付。

## 1. 自动检查

在仓库根目录执行：

```powershell
.\scripts\check-dev-environment.ps1
.\scripts\verify.ps1
dotnet test .\ExtractAndDelete.slnx --configuration Release --filter "Category=WindowsIntegration"
.\scripts\acceptance-check.ps1
```

检查必须满足：

- 系统为 Windows 11 x64，build 不低于 22000。
- Developer Mode 注册表值为 `1`。
- .NET SDK 10.0.400 feature band、MSBuild、x64 MSVC、Windows SDK 可定位。
- 内置 7-Zip 26.02 x64 的 SHA-256 正确。
- Release 构建 0 警告、0 错误，普通测试和 Windows Integration 测试通过。
- GUI 包含自包含 Windows App SDK、Shell DLL、四种 Explorer verb 和包内 7-Zip。
- 输出中没有 `.pfx`、`.cer`、`.msix`、`.msixbundle` 或 `.appinstaller`。

`acceptance-check.ps1` 只检查磁盘上的 V4 构建输出和布局，不代表 package 已注册；注册状态必须由 `verify-dev-install.ps1` 检查。

## 2. 注册与安装验收

```powershell
.\scripts\deploy-dev.ps1
.\scripts\verify-dev-install.ps1
```

注册结果必须是：

```text
Name            ExtractAndDelete
Version         4.0.0.0
Status          Ok
Application Id  App
Publisher       CN=ExtractAndDelete Developer
```

开始菜单应出现 **Extract & Delete**，Explorer 对单个 ZIP、7Z、RAR 或 TAR 显示“解压并回收”。机器不需要系统 7-Zip、.NET 10 或 Windows App SDK Runtime；发布目录包含自包含运行时和 7-Zip 文件。

## 3. 原生窗口与 DPI 验收

在中文 Windows 11、浅色主题、100%、125%、150% 和 200% 缩放下，对照参考 Windows“提取压缩(Zipped)文件夹”窗口检查：

- 窗口为固定对话框，96 DPI 外框目标为 `784×585`，不能最大化或自由缩放。
- 返回箭头禁用、压缩文件夹图标、标题“提取压缩(Zipped)文件夹”。
- 蓝色标题“选择一个目标并提取文件”。
- 只有一个可编辑目标路径框和 `浏览(R)...`。
- 复选框“完成时显示提取的文件(H)”默认勾选。
- 底部按钮“提取并回收(E)”和“取消”。
- 使用系统字体和 Common Controls；窗口为 Per-Monitor DPI V2。
- 在不同 DPI 显示器之间移动时，文字、边框、复选框和按钮保持清晰，不出现整窗位图拉伸。
- 高对比度模式使用系统颜色；普通深色主题仍保持锁定的参考浅色外观。

键盘验收：路径框、浏览、复选框、主按钮、取消按钮按顺序获得焦点；Alt+F/R/H/E、Enter 和 Esc 行为正确。

开始菜单启动先弹原生 File Picker，取消后退出；Explorer 激活直接填入压缩包并计算默认目标。

## 4. 文件行为验收

为每次测试创建唯一压缩包副本，避免成功回收原始样本：

| 场景 | 必须结果 |
| --- | --- |
| ZIP、7Z、RAR、TAR | 完整目标发布，源包进入回收站 |
| 中文、空格、`&`、括号路径 | 路径传递和输出正确 |
| 目标不存在 | Windows Shell 创建目录并发布 |
| 目标已有空目录/无冲突内容 | Windows Shell 合并 |
| 文件冲突选择替换 | 全部完成后允许回收源包 |
| 文件冲突选择保留两者 | 新名称落地，允许回收源包 |
| 文件冲突选择跳过 | 目标保留已完成内容，源包保留 |
| 损坏、加密、分卷、危险路径 | 目标不发布，源包保留 |
| 扫描/解压阶段取消 | 无目标写入，staging 清理，源包保留 |
| Windows 发布阶段取消 | 允许部分目标，源包保留，不回滚 |
| UAC 拒绝 | 源包保留，显示稳定发布错误 |
| 回收失败 | 完整目标和源包均保留，黄色警告 |
| 勾选显示文件 | 目标发布后打开目标；完整成功随后关闭 |
| 取消勾选 | 不自动打开目标 |
| 打开目标失败 | 任务结果仍完成，窗口保留警告 |

发布完成后检查 staging 是否清理。若清理失败，窗口必须给出准确路径且不得回收源包。

## 5. 单实例与 CLI 冻结验收

CLI 目录 `src/ExtractAndDelete.Cli` 仅作为历史源码保留，不在 `ExtractAndDelete.slnx` 中，不参与默认 Restore、Release build、test、publish、acceptance 或文档。不要执行单独构建 CLI 的命令，也不要把旧 CLI 输出当作 V4 交付物。

运行 GUI 任务时从 Explorer 激活第二个压缩包：只前置现有窗口，不替换路径，不启动第二个工作流。空闲激活会重置压缩包、默认目标和复选框。

## 6. 注销验收

```powershell
.\scripts\uninstall-dev.ps1
Get-AppxPackage -Name ExtractAndDelete
```

第二条命令无输出。重启 Explorer 或重新登录后，开始菜单入口和四种格式的“解压并回收”菜单消失。仓库构建输出和用户文件不被脚本删除。
