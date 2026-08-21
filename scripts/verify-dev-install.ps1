[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$ExpectedInstallLocation
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$releaseConfig = Get-Content -LiteralPath (Join-Path $repoRoot 'release-config.json') -Raw | ConvertFrom-Json
$expectedPackageVersion = [string]$releaseConfig.packageVersion
function Fail([string]$message) {
    throw "Developer package 注册检查失败：$message"
}

$packages = @(Get-AppxPackage -Name 'ExtractAndDelete' -ErrorAction SilentlyContinue)
if ($packages.Count -eq 0) {
    Fail '当前用户没有注册 ExtractAndDelete。请先运行 scripts\deploy-dev.ps1。'
}
if ($packages.Count -ne 1) {
    Fail "当前用户发现 $($packages.Count) 个 ExtractAndDelete package，拒绝继续。"
}

$package = $packages[0]
if ($package.Name -ne 'ExtractAndDelete') {
    Fail "发现了意外的 package identity：$($package.Name)。"
}
if ([string]$package.Version -ne $expectedPackageVersion) {
    Fail "package 版本不匹配，预期 $expectedPackageVersion：$($package.Version)。"
}
if ([string]$package.PackageFamilyName -ne [string]$releaseConfig.packageFamilyName) {
    Fail "package Family Name 不匹配，预期 $($releaseConfig.packageFamilyName)：$($package.PackageFamilyName)。"
}
if ([string]$package.Status -ne 'Ok') {
    Fail "package 状态不是 Ok：$($package.Status)。"
}

$installLocationText = [string]$package.InstallLocation
if ([string]::IsNullOrWhiteSpace($installLocationText)) {
    Fail "package InstallLocation 为空，当前注册是损坏或残留状态。请运行 $($releaseConfig.semanticVersion) 安装器修复。"
}
$registeredPath = [IO.Path]::GetFullPath($installLocationText).TrimEnd('\')
$guiOutput = $registeredPath
if (-not [string]::IsNullOrWhiteSpace($ExpectedInstallLocation)) {
    $expectedPath = [IO.Path]::GetFullPath($ExpectedInstallLocation).TrimEnd('\')
    if (-not [string]::Equals($registeredPath, $expectedPath, [StringComparison]::OrdinalIgnoreCase)) {
        Fail "package 安装目录不匹配。实际：$registeredPath；预期：$expectedPath。"
    }
}
else {
    $expectedRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\ExtractAndDelete')).TrimEnd('\')
    if (-not $registeredPath.StartsWith($expectedRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        Fail "package 安装目录不在固定安装根目录内：$registeredPath"
    }
}
$manifestPath = Join-Path $registeredPath 'AppxManifest.xml'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    Fail "注册目录清单不存在：$manifestPath。"
}

[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
$namespace = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
$namespace.AddNamespace('foundation', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$namespace.AddNamespace('desktop4', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10/4')
$namespace.AddNamespace('desktop5', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10/5')
$namespace.AddNamespace('com', 'http://schemas.microsoft.com/appx/manifest/com/windows10')

$identity = $manifest.SelectSingleNode('/foundation:Package/foundation:Identity', $namespace)
if ($null -eq $identity) {
    Fail 'AppxManifest 缺少 Identity。'
}
if ($identity.Name -ne 'ExtractAndDelete') {
    Fail "清单 Name 不匹配：$($identity.Name)。"
}
if ($identity.Publisher -ne 'CN=ExtractAndDelete Developer') {
    Fail "清单 Publisher 不匹配：$($identity.Publisher)。"
}
if ($identity.Version -ne $expectedPackageVersion) {
    Fail "清单 Version 不匹配，预期 $expectedPackageVersion：$($identity.Version)。"
}
$application = $manifest.SelectSingleNode('/foundation:Package/foundation:Applications/foundation:Application', $namespace)
if ($null -eq $application -or $application.Id -ne 'App') {
    Fail '清单 Application Id 不是 App。'
}
if ($identity.ProcessorArchitecture -ne 'x64' -or
    $application.Executable -ne 'ExtractAndDelete.Gui.exe' -or
    $application.EntryPoint -ne 'Windows.FullTrustApplication') {
    Fail '清单不是有效的 x64 loose-registration 清单。'
}
if ($manifest.OuterXml -match '\$targetnametoken\$|\$targetentrypoint\$') {
    Fail '注册清单仍包含 MSIX 占位符。'
}
if (-not (Test-Path -LiteralPath (Join-Path $registeredPath $application.Executable) -PathType Leaf)) {
    Fail "注册目录缺少清单声明的 GUI EXE：$($application.Executable)"
}
$displayName = $manifest.SelectSingleNode('/foundation:Package/foundation:Properties/foundation:DisplayName', $namespace)
if ($null -eq $displayName -or [string]$displayName.InnerText -ne 'Extract & Delete（系统集成组件）') {
    Fail 'package Properties DisplayName 不是“Extract & Delete（系统集成组件）”。'
}

$expectedClsid = '4F4F8F37-B78C-4B3D-90CE-8D16C4483B8E'
$comClass = $manifest.SelectSingleNode("//com:Class[@Id='$expectedClsid']", $namespace)
if ($null -eq $comClass) {
    Fail "清单缺少固定 Shell Extension CLSID：$expectedClsid。"
}
if ($comClass.Path -ne 'ExtractAndDelete.ShellExtension.dll' -or $comClass.ThreadingModel -ne 'STA') {
    Fail 'Shell Extension COM class 的 DLL 路径或 STA 线程模型不匹配。'
}

foreach ($extension in @('.zip', '.7z', '.rar', '.tar')) {
    $verb = $manifest.SelectSingleNode("//desktop5:ItemType[@Type='$extension']/desktop5:Verb", $namespace)
    if ($null -eq $verb -or $verb.Id -ne 'ExtractAndDelete') {
        Fail "清单缺少 $extension 的 ExtractAndDelete Explorer verb。"
    }
}

$requiredFiles = @(
    'ExtractAndDelete.Gui.exe',
    'ExtractAndDelete.ShellExtension.dll',
    'Microsoft.WindowsAppRuntime.dll',
    'ThirdParty\7-Zip\7z.exe',
    'ThirdParty\7-Zip\7z.dll',
    'ThirdParty\7-Zip\licenses\License.txt',
    'THIRD-PARTY-NOTICES.md',
    'LICENSE',
    'Assets\StoreLogo.png',
    'Assets\Square44x44Logo.png',
    'Assets\Square71x71Logo.png',
    'Assets\Square150x150Logo.png'
)
foreach ($relativePath in $requiredFiles) {
    $filePath = Join-Path $guiOutput $relativePath
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
        Fail "注册目录缺少文件：$filePath。"
    }
}

Write-Host 'Developer package 注册检查通过。'
Write-Host "Package: $($package.PackageFullName)"
Write-Host "InstallLocation: $registeredPath"
