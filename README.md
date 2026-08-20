# Extract & Delete

面向 Windows 的 ZIP 解压工具：完成解压后，将源 ZIP 移入 Windows 回收站。

核心安全承诺：

```text
解压失败或取消 → 源 ZIP 保留
解压成功但回收失败 → 输出保留，源 ZIP 保留
只有解压完整成功且回收成功 → 工作流完成
```

当前状态：V0.5 Core、CLI 和测试已完成；V1.0 正在实现 Windows 11 x64 Developer RC。

## 构建和测试

```powershell
dotnet restore .\ExtractAndDelete.slnx
dotnet test .\ExtractAndDelete.slnx --configuration Release
```

## CLI

```powershell
ExtractAndDelete.Cli.exe <zip路径> <最终目标目录>
```

## 设计文档

完整架构、边界、测试和版本路线见 [docs/PROJECT_DESIGN.md](docs/PROJECT_DESIGN.md)。
