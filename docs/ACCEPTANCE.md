# V2 Developer RC 验收说明

## 自动检查

在 Windows 11 x64、Developer Mode 已启用的开发机上执行：

```powershell
.\scripts\verify.ps1
.\scripts\acceptance-check.ps1
```

`verify.ps1` 会验证内置 7-Zip 26.02 哈希，还原 .NET 和 C++ 项目、构建 Release Core/CLI/WinUI/Shell Extension，并运行不接触真实回收站的普通测试。`acceptance-check.ps1` 检查 x64 自包含输出（没有外部 Windows App SDK framework dependency 且包含 runtime DLL）、GUI/CLI 中的 `7z.exe`/`7z.dll`、固定 package identity、`.zip`/`.7z`/`.rar`/`.tar` Shell 注册和仓库中没有证书/签名包。若开发机缺少 Native Desktop/C++/Windows SDK，`verify.ps1` 会在明确的工具链检查处停止。

真实回收站测试显式运行：

```powershell
dotnet test .\ExtractAndDelete.slnx --configuration Release --filter "Category=WindowsIntegration"
```

## Developer Mode 部署

```powershell
.\scripts\deploy-dev.ps1
```

验证开始菜单启动、单个 ZIP/7Z/RAR/TAR 的 Windows 11 现代右键菜单“解压并回收”、路径包含中文/空格/`&`/括号、运行中第二次激活被拒绝、取消清理 staging、损坏/加密/分卷压缩包不发布目录、内置 7-Zip 缺失或篡改时 fail closed、回收失败保留源文件。结束后注销：

```powershell
.\scripts\uninstall-dev.ps1
```

注销后重启 Explorer 或重新登录，确认命令不再显示。

## 干净 VM 手工验收矩阵

| 场景 | 预期 |
| --- | --- |
| 无 .NET/Windows App SDK 运行时 | 自包含 packaged GUI 可启动 |
| 非支持格式或多选 | 命令不显示/禁用 |
| 正常 ZIP/7Z/RAR/TAR | 完整目录一次性发布，源压缩包进入回收站 |
| 损坏、加密、分卷或危险路径压缩包 | 无最终目录，源压缩包保留 |
| 扫描/复制中取消 | 无最终目录，staging 清理，源压缩包保留 |
| Publishing/Recycling 中取消 | 按当前阶段完成，不回滚已发布目录 |
| 7-Zip 引擎缺失/篡改 | 不启动外部引擎，不发布目录，源压缩包保留 |
| 回收站不可用/回收失败 | 输出和源压缩包均保留，UI 黄色警告 |
| 注销 package | Explorer 命令消失且无残留 COM 注册 |

本仓库只交付 Developer Mode 开发部署包；不包含 `.pfx`、正式签名、Store 配置或公开安装说明。
