# V3 Developer RC 验收说明

## 1. 自动检查

在 Windows 11 x64 仓库根目录执行：

```powershell
.\scripts\check-dev-environment.ps1
.\scripts\verify.ps1
dotnet test .\ExtractAndDelete.slnx --configuration Release --filter "Category=WindowsIntegration"
.\scripts\acceptance-check.ps1
```

要求：x64、Windows build 不低于 22000、Developer Mode 值为 `1`、.NET SDK 10.0.400 feature band、MSBuild/C++ x64/Windows SDK 可定位、7-Zip 26.02 SHA-256 正确、Release 无警告、普通测试全绿、Windows Integration 全绿。

`acceptance-check.ps1` 只检查输出布局：V3 清单版本 `3.0.0.0`、GUI/Shell 输出、自包含 Windows App SDK、四种 Explorer verb、包内 7-Zip 和没有证书/安装包文件。它不检查 package 是否注册。

## 2. 注册验收

```powershell
.\scripts\deploy-dev.ps1
.\scripts\verify-dev-install.ps1
```

注册结果必须是：

```text
Name            ExtractAndDelete
Version         3.0.0.0
Status          Ok
Application Id  App
Publisher       CN=ExtractAndDelete Developer
```

开始菜单应出现 **Extract & Delete**。机器不需要系统 7-Zip、.NET 10 或 Windows App SDK Runtime；发布目录包含自包含运行时和 7-Zip 文件。

## 3. GUI 视觉验收

在中文 Windows 11 的 100%、150%、200% 缩放和高对比度下，与参考 Windows“提取压缩文件夹”窗口核对：

- 返回箭头禁用、压缩文件夹图标、标题“提取压缩文件夹”。
- 蓝色标题“选择一个目标并提取文件”。
- 只有一个可编辑目标路径框和 `浏览(R)...`。
- 复选框“完成时显示提取的文件(H)”默认勾选。
- 底部按钮“提取并回收(E)”和“取消”。
- 系统字体、主题、高 DPI 和键盘 AccessKey 可用。

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

## 5. CLI 与单实例

CLI 使用准确目标参数验证：不存在目标成功；已有目录、相对/无效目标失败且内容不变。Ctrl+C 在扫描/解压阶段返回退出码 `3`；发布/回收阶段显示不可取消并等待。回收失败返回 `2`，普通错误返回 `1`。

运行 GUI 任务时从 Explorer 激活第二个压缩包：只前置现有窗口，不替换路径，不启动第二个工作流。空闲激活会重置压缩包、默认目标和复选框。

## 6. 注销验收

```powershell
.\scripts\uninstall-dev.ps1
Get-AppxPackage -Name ExtractAndDelete
```

第二条命令无输出。重启 Explorer 或重新登录后，开始菜单入口和四种格式的“解压并回收”菜单消失。仓库构建输出和用户文件不被脚本删除。
