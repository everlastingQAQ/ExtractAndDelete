[CmdletBinding()]
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$config = Get-Content -LiteralPath (Join-Path $repoRoot 'release-config.json') -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = [string]$config.semanticVersion }
if ($Version -ne [string]$config.semanticVersion) { throw "版本参数不匹配：$Version" }

$releaseRoot = Join-Path $repoRoot "artifacts\release\$Version"
$installerPath = Join-Path $releaseRoot "ExtractAndDelete-Setup-$Version-x64.exe"
$hashPath = "$installerPath.sha256"
foreach ($path in @($installerPath, $hashPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "安装器发布文件不存在：$path" }
}

$hashLine = (Get-Content -LiteralPath $hashPath | Select-Object -First 1).Trim()
if ($hashLine -notmatch '^([0-9a-fA-F]{64})\s+\*(.+)$') { throw "SHA-256 文件格式无效：$hashLine" }
if ($Matches[2] -ne (Split-Path -Leaf $installerPath)) { throw 'SHA-256 文件名与安装器不匹配。' }
$actualHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $Matches[1].ToLowerInvariant()) { throw '安装器 SHA-256 校验失败。' }

$signature = Get-AuthenticodeSignature -LiteralPath $installerPath
if ([string]$signature.Status -ne 'NotSigned') {
    throw "安装器签名状态不是预期的 NotSigned：$($signature.Status)"
}

$versionInfo = (Get-Item -LiteralPath $installerPath).VersionInfo
$fileVersion = ([string]$versionInfo.FileVersion).Trim()
$productName = ([string]$versionInfo.ProductName).Trim()
if ($fileVersion -notlike "$($config.packageVersion)*") {
    throw "安装器 FileVersion 不匹配：$fileVersion"
}
if ($productName -ne 'Extract & Delete') {
    throw "安装器 ProductName 不匹配：$productName"
}

$unexpected = @(Get-ChildItem -LiteralPath $releaseRoot -File -Force |
    Where-Object { $_.Name -notin @((Split-Path -Leaf $installerPath), (Split-Path -Leaf $hashPath)) })
if ($unexpected.Count -ne 0) {
    throw "Release 根目录存在未允许的文件：$($unexpected.Name -join ', ')"
}

Write-Host 'Installer artifact verification passed.'
Write-Host "Installer: $installerPath"
Write-Host "SHA-256: $actualHash"
Write-Host 'Authenticode: NotSigned (expected for Developer Preview)'
