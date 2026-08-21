[CmdletBinding()]
param(
    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$configPath = Join-Path $repoRoot 'release-config.json'
if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
    throw "统一发布配置不存在：$configPath"
}

$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$expectedInnoVersion = [string]$config.innoVersion
$propsPath = Join-Path $repoRoot 'Directory.Build.props'
$manifestPath = Join-Path $repoRoot 'src\ExtractAndDelete.Gui\Package.appxmanifest'
$issPath = Join-Path $repoRoot 'installer\ExtractAndDelete.iss'
if (-not (Test-Path -LiteralPath $propsPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $issPath -PathType Leaf)) {
    throw '发布版本一致性检查所需的 props、manifest 或 Inno 文件不存在。'
}

[xml]$props = Get-Content -LiteralPath $propsPath -Raw
$propertyGroup = @($props.Project.PropertyGroup | Where-Object { $_.PSObject.Properties.Name -contains 'VersionPrefix' }) | Select-Object -First 1
if ($null -eq $propertyGroup) {
    throw 'Directory.Build.props 缺少统一版本 PropertyGroup。'
}
foreach ($property in @('VersionPrefix', 'Version', 'AssemblyVersion', 'FileVersion')) {
    $value = [string]$propertyGroup.$property
    $expected = if ($property -in @('AssemblyVersion', 'FileVersion')) { [string]$config.packageVersion } else { [string]$config.semanticVersion }
    if ($value -ne $expected) {
        throw "Directory.Build.props 的 $property 为 $value，预期 $expected。"
    }
}

[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8
$identity = $manifest.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Identity']")
if ($null -eq $identity -or $identity.GetAttribute('Name') -ne [string]$config.packageName -or
    $identity.GetAttribute('Publisher') -ne [string]$config.publisher -or
    $identity.GetAttribute('Version') -ne [string]$config.packageVersion) {
    throw 'Package manifest 与 release-config.json 的 Name、Publisher 或 Version 不一致。'
}
$application = $manifest.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Applications']/*[local-name()='Application']")
if ($null -eq $application -or $application.GetAttribute('Id') -ne [string]$config.applicationId) {
    throw 'Package manifest 的 Application Id 与 release-config.json 不一致。'
}

$issText = Get-Content -LiteralPath $issPath -Raw
$issChecks = @{
    'DefaultReleaseVersion' = [string]$config.semanticVersion
    'DefaultPackageVersion' = [string]$config.packageVersion
}
foreach ($entry in $issChecks.GetEnumerator()) {
    $pattern = '#define\s+' + [regex]::Escape($entry.Key) + '\s+"([^"]+)"'
    $match = [regex]::Match($issText, $pattern)
    if (-not $match.Success -or $match.Groups[1].Value -ne $entry.Value) {
        throw "Inno 配置的 $($entry.Key) 与 release-config.json 不一致。"
    }
}
if ($issText -notmatch ([regex]::Escape([string]$config.installerAppId))) {
    throw 'Inno AppId 与 release-config.json 不一致。'
}

if (-not [Environment]::Is64BitOperatingSystem) {
    throw '发布构建必须在 x64 Windows 上执行。'
}

$os = Get-CimInstance -ClassName Win32_OperatingSystem
$buildNumber = 0
if (-not [int]::TryParse([string]$os.BuildNumber, [ref]$buildNumber) -or $buildNumber -lt 22000) {
    throw "当前 Windows build 为 $($os.BuildNumber)，发布构建至少需要 22000。"
}

$dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
if ($null -eq $dotnet) {
    throw '未找到 dotnet。请安装 .NET SDK 10.0.400 feature band。'
}
$dotnetVersion = (& $dotnet.Source --version).Trim()
$parsedDotnetVersion = $null
if (-not [version]::TryParse($dotnetVersion, [ref]$parsedDotnetVersion) -or
    $parsedDotnetVersion.Major -ne 10 -or $parsedDotnetVersion.Minor -ne 0 -or
    $parsedDotnetVersion.Build -ne 400) {
    throw "当前 .NET SDK 为 $dotnetVersion，需要 10.0.400 feature band 的补丁版本。"
}

if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($IsccPath) -or -not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
    throw "未找到 Inno Setup $expectedInnoVersion 的 ISCC.exe。请安装 Inno Setup 6.7.3。"
}

$innoUninstaller = Join-Path (Split-Path -Parent $IsccPath) 'unins000.exe'
$innoVersion = $null
if (Test-Path -LiteralPath $innoUninstaller -PathType Leaf) {
    $innoVersion = ([string](Get-Item -LiteralPath $innoUninstaller).VersionInfo.ProductVersion).Trim()
}
if ([string]::IsNullOrWhiteSpace($innoVersion) -or $innoVersion -ne $expectedInnoVersion) {
    throw "Inno Setup 版本不匹配。实际版本：$innoVersion；预期：$expectedInnoVersion；ISCC：$IsccPath。"
}

$vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswherePath -PathType Leaf)) {
    throw "未找到 vswhere.exe：$vswherePath"
}
$msbuildPath = (& $vswherePath -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($msbuildPath) -or -not (Test-Path -LiteralPath $msbuildPath -PathType Leaf)) {
    throw '未找到 x64 MSVC 工具链对应的 MSBuild。'
}

& (Join-Path $repoRoot 'scripts\verify-third-party.ps1')

$forbidden = @(Get-ChildItem -LiteralPath $repoRoot -Recurse -File -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension.ToLowerInvariant() -in @('.pfx', '.cer', '.msix', '.msixbundle', '.appinstaller') })
if ($forbidden.Count -ne 0) {
    throw "仓库中存在禁止的签名或安装包文件：$($forbidden.FullName -join ', ')"
}

Write-Host 'Release 构建环境检查通过。'
Write-Host "Windows build: $buildNumber"
Write-Host "dotnet SDK: $dotnetVersion"
Write-Host "ISCC: $IsccPath"
Write-Host "MSBuild: $msbuildPath"
