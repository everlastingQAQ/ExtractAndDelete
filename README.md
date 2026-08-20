# Extract & Delete

面向 Windows 的 ZIP 解压工具：完成解压后，将源 ZIP 移入 Windows 回收站。

核心安全承诺：

```text
解压失败或取消 → 源 ZIP 保留
解压成功但回收失败 → 输出保留，源 ZIP 保留
只有解压完整成功且回收成功 → 工作流完成
```

当前状态：V1 Core、CLI、WinUI 3 packaged GUI、Explorer COM 源码和开发部署脚本已进入 `main`；Developer RC 的真实 Shell DLL 构建和 VM 验收仍需本机安装 Native Desktop/Windows SDK 后完成。

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
ExtractAndDelete.Cli.exe <zip路径> <最终目标目录>
```

开发部署使用 `.\scripts\deploy-dev.ps1`，注销使用 `.\scripts\uninstall-dev.ps1`。脚本只处理固定的 `ExtractAndDelete` package identity，不会搜索或删除其他包。

## 设计文档

完整架构、边界、测试和版本路线见 [docs/PROJECT_DESIGN.md](docs/PROJECT_DESIGN.md)。
