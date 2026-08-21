[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Fail([string]$message) {
    throw "开发部署环境检查失败：$message"
}

if (-not [Environment]::Is64BitOperatingSystem) {
    Fail '当前系统不是 x64。Extract & Delete 4.1 Developer Preview 只支持 Windows 11 x64。'
}

$os = Get-CimInstance -ClassName Win32_OperatingSystem
$buildNumber = 0
if (-not [int]::TryParse([string]$os.BuildNumber, [ref]$buildNumber) -or $buildNumber -lt 22000) {
    Fail "当前 Windows build 为 $($os.BuildNumber)，至少需要 22000。"
}

$developerModeKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
$unlockProperties = Get-ItemProperty -LiteralPath $developerModeKey -ErrorAction SilentlyContinue
$developerMode = 0
if (($null -ne $unlockProperties) -and ($unlockProperties.PSObject.Properties.Name -contains 'AllowDevelopmentWithoutDevLicense')) {
    $developerMode = [int]$unlockProperties.AllowDevelopmentWithoutDevLicense
}
if ($developerMode -ne 1) {
    Fail 'Windows Developer Mode 未开启。请打开“设置 → 系统 → 面向开发人员 → 开发人员模式”，然后重新运行。脚本不会自动修改系统设置。'
}

$dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand) {
    Fail '未找到 dotnet。请安装 .NET SDK 10.0.400 或同一 feature band 的补丁版本。'
}

Push-Location $repoRoot
try {
    $dotnetVersionText = (& $dotnetCommand.Source --version).Trim()
    $dotnetVersion = $null
    $dotnetIsSupported = [version]::TryParse($dotnetVersionText, [ref]$dotnetVersion)
    if (-not $dotnetIsSupported -or $dotnetVersion.Major -ne 10 -or $dotnetVersion.Minor -ne 0 -or $dotnetVersion.Build -ne 400) {
        Fail "当前 .NET SDK 为 $dotnetVersionText，需要 10.0.400 feature band 的补丁版本。"
    }
}
finally {
    Pop-Location
}

$programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
if ([string]::IsNullOrWhiteSpace($programFilesX86)) {
    Fail '无法定位 Program Files (x86)。'
}

$vswherePath = Join-Path $programFilesX86 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswherePath -PathType Leaf)) {
    Fail "未找到 vswhere.exe：$vswherePath。请安装 Visual Studio C++/WinUI 工具。"
}

$msbuildPath = (& $vswherePath -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($msbuildPath) -or -not (Test-Path -LiteralPath $msbuildPath -PathType Leaf)) {
    Fail '未找到带 x64 MSVC 工具链的 MSBuild。请安装 C++ WinUI/桌面开发工具和 x64 MSVC。'
}

$windowsKitInclude = Join-Path $programFilesX86 'Windows Kits\10\Include'
$windowsSdkHeaders = @()
if (Test-Path -LiteralPath $windowsKitInclude -PathType Container) {
    $windowsSdkHeaders = @(Get-ChildItem -LiteralPath $windowsKitInclude -Directory | Where-Object {
            $_.Name -match '^\d+\.\d+\.\d+$' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'um\Windows.h') -PathType Leaf)
        })
}

$windowsSdkSource = $null
if ($windowsSdkHeaders.Count -gt 0) {
    $windowsSdkSource = "系统 Windows SDK $($windowsSdkHeaders[0].Name)"
}
else {
    $nugetRoot = if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
        Join-Path $env:USERPROFILE '.nuget\packages'
    }
    else {
        $env:NUGET_PACKAGES
    }
    $cppSdkRoot = Join-Path $nugetRoot 'microsoft.windows.sdk.cpp'
    $cppSdkX64Root = Join-Path $nugetRoot 'microsoft.windows.sdk.cpp.x64'
    $cppSdkHeader = Get-ChildItem -LiteralPath $cppSdkRoot -File -Recurse -Filter 'Windows.h' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\c\\Include\\[^\\]+\\um\\Windows\.h$' } |
        Select-Object -First 1
    $cppSdkLibrary = Get-ChildItem -LiteralPath $cppSdkX64Root -File -Recurse -Filter 'WindowsApp.lib' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\c\\um\\x64\\WindowsApp\.lib$' } |
        Select-Object -First 1
    if ($null -ne $cppSdkHeader -and $null -ne $cppSdkLibrary) {
        $windowsSdkSource = "NuGet Microsoft.Windows.SDK.CPP.x64 ($($cppSdkLibrary.Directory.Parent.Parent.Parent.Name))"
    }
}
if ($null -eq $windowsSdkSource) {
    Fail "未找到系统 Windows SDK 或已还原的 Microsoft.Windows.SDK.CPP.x64。请安装 Windows SDK 后重新运行 verify.ps1。"
}

& (Join-Path $repoRoot 'scripts\verify-third-party.ps1')

Write-Host 'Developer Mode 开发部署环境检查通过。'
Write-Host "Windows build: $buildNumber"
Write-Host "dotnet SDK: $dotnetVersionText"
Write-Host "MSBuild: $msbuildPath"
Write-Host "Windows SDK: $windowsSdkSource"
