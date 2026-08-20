# Extract & Delete

面向 Windows 的安全压缩包解压工具：完整解压后，将源压缩包移入 Windows 回收站。

核心安全承诺：

```text
解压失败或取消 → 源压缩包保留
解压成功但回收失败 → 输出保留，源压缩包保留
只有解压完整成功且回收成功 → 工作流完成
```

当前状态：V2 的 Core、GUI、CLI、Explorer Shell 和内置 7-Zip 26.02 x64 已实现。项目交付形态仍是 Windows 11 x64 Developer RC；真实 Shell DLL 部署和干净 VM 验收需要 Developer Mode、Native Desktop/Windows SDK。

## 构建和测试

```powershell
dotnet restore .\ExtractAndDelete.slnx
dotnet build .\ExtractAndDelete.slnx --configuration Release --no-restore
dotnet test .\ExtractAndDelete.slnx --configuration Release --no-build --filter "Category!=WindowsIntegration"

# 包含 x64 C++ Explorer DLL 的完整验证（需要 VS Native Desktop + Windows SDK）
.\scripts\verify.ps1
```

## CLI

```powershell
ExtractAndDelete.Cli.exe <压缩包路径> <最终目标目录>
```

开发部署使用 `.\scripts\deploy-dev.ps1`，注册状态检查使用 `.\scripts\verify-dev-install.ps1`，注销使用 `.\scripts\uninstall-dev.ps1`。脚本只处理固定的 `ExtractAndDelete` package identity，不会搜索或删除其他包。`acceptance-check.ps1` 只检查构建输出，不代表 package 已注册。

完整安装、GUI、Explorer 右键菜单、CLI、支持格式和常见问题见 [docs/USAGE.md](docs/USAGE.md)。

## 设计文档

完整架构、边界、测试和版本路线见 [docs/PROJECT_DESIGN.md](docs/PROJECT_DESIGN.md)。
开发部署和干净 VM 验收矩阵见 [docs/ACCEPTANCE.md](docs/ACCEPTANCE.md)。
