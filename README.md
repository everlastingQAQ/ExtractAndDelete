# Extract & Delete

面向 Windows 11 的安全压缩包解压工具：完整发布后，将源压缩包移入 Windows 回收站。

安全承诺：

```text
扫描/解压失败或取消 → 目标不发布，源压缩包保留
Windows 发布取消或失败 → 已写入的部分目标可能保留，源压缩包保留
回收失败 → 完整目标和源压缩包都保留，不永久删除
只有完整发布、没有跳过且成功移入回收站 → 工作流完成
```

当前版本为 V4 `4.0.0.0` Developer RC：可见 GUI 使用 C# WinForms/Win32 原生控件复刻 Windows 11“提取压缩(Zipped)文件夹”窗口，包内使用官方 7-Zip 26.02 x64，Explorer 右键菜单显示“解压并回收”。

这是 Developer Mode packaged 应用，不是正式签名安装包；不提供公开 MSIX、AppInstaller、MSI 或便携版。部署前必须开启 Windows Developer Mode。

## 构建和测试

```powershell
dotnet restore .\ExtractAndDelete.slnx
dotnet build .\ExtractAndDelete.slnx --configuration Release --no-restore
dotnet test .\ExtractAndDelete.slnx --configuration Release --no-build --filter "Category!=WindowsIntegration"

# 包含 x64 Explorer DLL、7-Zip 哈希和完整 Release 检查
.\scripts\verify.ps1
```

开发部署顺序：

```powershell
.\scripts\check-dev-environment.ps1
.\scripts\deploy-dev.ps1
.\scripts\verify-dev-install.ps1
```

注销：

```powershell
.\scripts\uninstall-dev.ps1
```

`acceptance-check.ps1` 只检查构建输出布局，不代表 package 已注册；注册状态必须使用 `verify-dev-install.ps1` 检查。

## 使用方式

- 开始菜单启动：先选择一个 `.zip`、`.7z`、`.rar` 或 `.tar`。
- Explorer 中选中恰好一个支持格式，打开 Windows 11 现代菜单，点击“解压并回收”。
- 向导只有一个“提取到”准确目标路径；默认值为压缩包所在目录加去掉最后扩展名后的文件名。
- 目标可以是不存在的目录，也可以是已有目录；Windows Shell 负责合并和冲突提示。
- 只有完整发布且没有跳过文件时，源压缩包才会移入回收站。

CLI 源码保留为历史参考，但 V4 已冻结，不进入默认 solution、测试、发布或用户交付。

## 设计和验收文档

- [使用文档](docs/USAGE.md)
- [项目总设计](docs/PROJECT_DESIGN.md)
- [V4 验收说明](docs/ACCEPTANCE.md)
- [第三方声明](THIRD-PARTY-NOTICES.md)
