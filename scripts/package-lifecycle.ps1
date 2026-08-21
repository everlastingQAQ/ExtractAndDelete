[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Preflight', 'Install', 'Uninstall')]
    [string]$Action,

    [string]$ManifestPath,
    [string]$InstallRoot,
    [string]$PayloadPath,
    [string]$ExpectedVersion = '4.1.1.0',
    [string]$ExpectedPackageName = 'ExtractAndDelete',
    [string]$ExpectedPublisher = 'CN=ExtractAndDelete Developer',
    [string]$ExpectedFamilyName = 'ExtractAndDelete_vyz6krqqgd78c',
    [string]$ExpectedApplicationId = 'App',
    [string]$ExpectedClsid = '4F4F8F37-B78C-4B3D-90CE-8D16C4483B8E',
    [Int64]$MinimumFreeBytes = 0,
    [string]$LogPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($LogPath)) {
    $LogPath = Join-Path $env:TEMP ("ExtractAndDelete-Setup-{0:yyyyMMdd-HHmmssfff}.log" -f (Get-Date))
}

$logParent = Split-Path -Parent $LogPath
if (-not [string]::IsNullOrWhiteSpace($logParent)) {
    New-Item -ItemType Directory -Path $logParent -Force | Out-Null
}

function Write-Log {
    param([string]$Message)

    $line = "[{0:O}] {1}" -f (Get-Date), $Message
    Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8
    Write-Host $line
}

function Get-HResultText {
    param([System.Exception]$Exception)

    $hresult = [int64]$Exception.HResult
    if ($hresult -lt 0) {
        $hresult += 0x100000000
    }
    return ('0x{0:X8}' -f $hresult)
}

function Get-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
}

function Get-FileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    $algorithm = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try {
        return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $stream.Dispose()
        $algorithm.Dispose()
    }
}

function Test-SamePath {
    param([string]$Left, [string]$Right)

    return [string]::Equals((Get-FullPath $Left), (Get-FullPath $Right), [StringComparison]::OrdinalIgnoreCase)
}

function Test-PathWithin {
    param([string]$Path, [string]$Root)

    $fullPath = Get-FullPath $Path
    $fullRoot = Get-FullPath $Root
    if (Test-SamePath $fullPath $fullRoot) { return $true }
    return $fullPath.StartsWith($fullRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-NormalDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "目录不存在：$Path"
    }

    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "拒绝操作 reparse point 目录：$Path"
    }
}

function Get-ManifestIdentity {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "AppxManifest.xml 不存在：$Path"
    }

    [xml]$manifest = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    $identity = $manifest.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Identity']")
    if ($null -eq $identity) {
        throw "清单缺少 Identity：$Path"
    }

    $application = $manifest.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Applications']/*[local-name()='Application']")
    if ($null -eq $application) {
        throw "清单缺少 Application：$Path"
    }

    [pscustomobject]@{
        Manifest = $manifest
        Identity = $identity
        Application = $application
    }
}

function Assert-Manifest {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$Version,
        [switch]$AllowLegacyRegistrationEntryPoint
    )

    $parsed = Get-ManifestIdentity $Path
    $identity = $parsed.Identity
    $application = $parsed.Application

    if ($identity.GetAttribute('Name') -ne $ExpectedPackageName) {
        throw "清单 Name 不匹配：$($identity.GetAttribute('Name'))"
    }
    if ($identity.GetAttribute('Publisher') -ne $ExpectedPublisher) {
        throw "清单 Publisher 不匹配：$($identity.GetAttribute('Publisher'))"
    }
    if (-not [string]::IsNullOrWhiteSpace($Version) -and $identity.GetAttribute('Version') -ne $Version) {
        throw "清单 Version 不匹配：$($identity.GetAttribute('Version'))，预期 $Version"
    }
    if ($application.GetAttribute('Id') -ne $ExpectedApplicationId) {
        throw "清单 Application Id 不匹配：$($application.GetAttribute('Id'))"
    }
    if (-not $AllowLegacyRegistrationEntryPoint) {
        if ($identity.GetAttribute('ProcessorArchitecture') -ne 'x64') {
            throw "清单 ProcessorArchitecture 不匹配：$($identity.GetAttribute('ProcessorArchitecture'))"
        }
        if ($application.GetAttribute('Executable') -ne 'ExtractAndDelete.Gui.exe' -or
            $application.GetAttribute('EntryPoint') -ne 'Windows.FullTrustApplication') {
            throw "清单不是有效的 loose-registration 入口：$($application.GetAttribute('Executable')) / $($application.GetAttribute('EntryPoint'))"
        }
        if ($parsed.Manifest.OuterXml -match '\$targetnametoken\$|\$targetentrypoint\$') {
            throw '清单仍包含 MSIX 构建占位符。'
        }
    }

    $comClass = $parsed.Manifest.SelectSingleNode("//*[local-name()='Class' and @Id='$ExpectedClsid']")
    if ($null -eq $comClass) {
        throw "清单缺少 Shell Extension CLSID：$ExpectedClsid"
    }
    if ($comClass.GetAttribute('Path') -ne 'ExtractAndDelete.ShellExtension.dll' -or
        $comClass.GetAttribute('ThreadingModel') -ne 'STA') {
        throw 'Shell Extension COM 清单配置不匹配。'
    }

    foreach ($extension in @('.zip', '.7z', '.rar', '.tar')) {
        $verb = $parsed.Manifest.SelectSingleNode("//*[local-name()='ItemType' and @Type='$extension']/*[local-name()='Verb']")
        if ($null -eq $verb -or $verb.GetAttribute('Id') -ne 'ExtractAndDelete' -or
            $verb.GetAttribute('Clsid') -ne $ExpectedClsid) {
            throw "清单缺少 $extension Explorer verb。"
        }
    }
}

function Get-CurrentPackages {
    return (, @(Get-AppxPackage -Name $ExpectedPackageName -ErrorAction SilentlyContinue))
}

function Get-RegisteredManifestPath {
    param([Parameter(Mandatory = $true)]$Package)

    $location = [string]$Package.InstallLocation
    if ([string]::IsNullOrWhiteSpace($location)) {
        return $null
    }

    try {
        $fullLocation = Get-FullPath $location
    }
    catch {
        return $null
    }

    $manifestPath = Join-Path $fullLocation 'AppxManifest.xml'
    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        return $manifestPath
    }

    return $null
}

function Wait-ForPackageGone {
    param([int]$TimeoutSeconds = 30)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $remaining = Get-CurrentPackages
        if ($remaining.Count -eq 0) {
            return
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    $remaining = Get-CurrentPackages
    throw "package 注销超时，仍然存在：$($remaining.PackageFullName -join ', ')"
}

function Assert-PackageIdentity {
    param([Parameter(Mandatory = $true)]$Package)

    if ($Package.Name -ne $ExpectedPackageName) {
        throw "拒绝操作意外 package：$($Package.Name)"
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$Package.Publisher) -and
        [string]$Package.Publisher -ne $ExpectedPublisher) {
        throw "已注册 package Publisher 不匹配：$($Package.Publisher)"
    }
    if ([string]$Package.PackageFamilyName -ne $ExpectedFamilyName) {
        throw "已注册 package Family Name 不匹配：$($Package.PackageFamilyName)"
    }
}

function Assert-NoGuiProcess {
    $processes = @(Get-Process -Name 'ExtractAndDelete.Gui' -ErrorAction SilentlyContinue)
    if ($processes.Count -ne 0) {
        throw 'Extract & Delete 正在运行。请先正常关闭应用和当前任务，再重试。'
    }
}

function Assert-Environment {
    if (-not [Environment]::Is64BitOperatingSystem) {
        throw '当前系统不是 x64。'
    }

    $os = Get-CimInstance -ClassName Win32_OperatingSystem
    $buildNumber = 0
    if (-not [int]::TryParse([string]$os.BuildNumber, [ref]$buildNumber) -or $buildNumber -lt 22000) {
        throw "当前 Windows build 为 $($os.BuildNumber)，至少需要 22000。"
    }

    $developerModeKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
    $unlockProperties = Get-ItemProperty -LiteralPath $developerModeKey -ErrorAction SilentlyContinue
    $developerMode = 0
    if (($null -ne $unlockProperties) -and
        ($unlockProperties.PSObject.Properties.Name -contains 'AllowDevelopmentWithoutDevLicense')) {
        $developerMode = [int]$unlockProperties.AllowDevelopmentWithoutDevLicense
    }
    if ($developerMode -ne 1) {
        throw 'Windows Developer Mode 未开启。请打开“设置 → 系统 → 面向开发人员 → 开发人员模式”。'
    }

    if ($MinimumFreeBytes -gt 0) {
        if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
            throw '无法检查安装磁盘空间：InstallRoot 为空。'
        }
        if ($MinimumFreeBytes -gt [long]::MaxValue / 2) {
            throw '发布 payload 大小超出可检查范围。'
        }

        $requiredBytes = [Math]::Max($MinimumFreeBytes * 2, [Int64]512MB)
        $driveRoot = [IO.Path]::GetPathRoot((Get-FullPath $InstallRoot))
        $drive = New-Object IO.DriveInfo($driveRoot)
        if (-not $drive.IsReady) {
            throw "安装磁盘不可用：$driveRoot"
        }
        if ($drive.AvailableFreeSpace -lt $requiredBytes) {
            throw "安装磁盘空间不足：至少需要 $requiredBytes 字节，当前可用 $($drive.AvailableFreeSpace) 字节。"
        }
        Write-Log "安装磁盘空间检查通过：可用 $($drive.AvailableFreeSpace) 字节，预留 $requiredBytes 字节。"
    }
}

function Assert-InstallRoot {
    param([Parameter(Mandatory = $true)][string]$Path)

    $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $expectedRoot = Join-Path $localAppData 'Programs\ExtractAndDelete'
    if (-not (Test-SamePath $Path $expectedRoot)) {
        throw "安装根目录不符合固定用户目录：$Path"
    }
}

function Assert-CleanableInstallRoot {
    param([Parameter(Mandatory = $true)][string]$Path)

    Assert-InstallRoot $Path
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return
    }

    Assert-NormalDirectory $Path
    $children = @(Get-ChildItem -LiteralPath $Path -Force -ErrorAction Stop)
    foreach ($child in $children) {
        if (($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "安装目录包含 reparse point，拒绝递归清理：$($child.FullName)"
        }
    }
}

function Assert-Payload {
    param([Parameter(Mandatory = $true)][string]$Path)

    Assert-NormalDirectory $Path
    $manifestPath = Join-Path $Path 'AppxManifest.xml'
    Assert-Manifest $manifestPath $ExpectedVersion

    $required = @(
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
    foreach ($relativePath in $required) {
        $filePath = Join-Path $Path $relativePath
        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
            throw "payload 缺少文件：$relativePath"
        }
    }

    $hashPath = Join-Path $Path 'payload.sha256'
    if (-not (Test-Path -LiteralPath $hashPath -PathType Leaf)) {
        throw "payload SHA-256 清单不存在：$hashPath"
    }

    $expectedHashes = @{}
    foreach ($line in Get-Content -LiteralPath $hashPath) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line -notmatch '^([0-9a-fA-F]{64})\s+\*(.+)$') {
            throw "payload SHA-256 清单格式无效：$line"
        }
        $relativePath = $Matches[2].Replace('/', '\')
        if ([IO.Path]::IsPathRooted($relativePath) -or $relativePath.Contains('..')) {
            throw "payload SHA-256 路径越界：$relativePath"
        }
        $expectedHashes[$relativePath] = $Matches[1].ToLowerInvariant()
    }

    foreach ($entry in $expectedHashes.GetEnumerator()) {
        $filePath = Join-Path $Path $entry.Key
        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
            throw "payload SHA-256 清单引用了不存在的文件：$($entry.Key)"
        }
        $actual = Get-FileSha256 -Path $filePath
        if ($actual -ne $entry.Value) {
            throw "payload SHA-256 不匹配：$($entry.Key)"
        }
    }

    $actualRelativePaths = @(Get-ChildItem -LiteralPath $Path -Recurse -File -Force |
        Where-Object { $_.Name -ne 'payload.sha256' } |
        ForEach-Object { $_.FullName.Substring($Path.Length + 1).Replace('/', '\') } |
        Sort-Object)
    $expectedRelativePaths = @($expectedHashes.Keys | Sort-Object)
    if (($actualRelativePaths -join "`n") -ne ($expectedRelativePaths -join "`n")) {
        throw 'payload SHA-256 清单没有覆盖全部 payload 文件。'
    }

    $unexpectedCli = @(Get-ChildItem -LiteralPath $Path -Recurse -File -Force |
        Where-Object { $_.Name -like 'ExtractAndDelete.Cli*' })
    if ($unexpectedCli.Count -ne 0) {
        throw 'payload 包含冻结 CLI 文件。'
    }
}

function Remove-ExactPackage {
    param([Parameter(Mandatory = $true)]$Package)

    Assert-PackageIdentity $Package
    Write-Log "移除 package $($Package.PackageFullName)"
    Remove-AppxPackage -Package $Package.PackageFullName -Confirm:$false -ErrorAction Stop
}

function Register-AndVerify {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ExpectedLocation,
        [Parameter(Mandatory = $true)][string]$Version,
        [switch]$AllowLegacyRegistrationEntryPoint
    )

    Write-Log "注册清单 $Path"
    Add-AppxPackage -Register -Path $Path -ErrorAction Stop
    $packages = Get-CurrentPackages
    if ($packages.Count -ne 1) {
        throw "注册后发现 $($packages.Count) 个目标 package。"
    }

    $package = $packages[0]
    Assert-PackageIdentity $package
    if ([string]$package.Status -ne 'Ok' -and [int]$package.Status -ne 0) {
        throw "package 状态不是 Ok：$($package.Status)"
    }
    if ([string]$package.Version -ne $Version) {
        throw "package 版本不匹配：$($package.Version)，预期 $Version"
    }
    if ([string]::IsNullOrWhiteSpace([string]$package.InstallLocation) -or
        -not (Test-SamePath ([string]$package.InstallLocation) $ExpectedLocation)) {
        throw "package 安装目录不匹配：$($package.InstallLocation)"
    }

    Assert-Manifest (Join-Path $package.InstallLocation 'AppxManifest.xml') $Version -AllowLegacyRegistrationEntryPoint:$AllowLegacyRegistrationEntryPoint
    Write-Log "package 注册验证通过：$($package.PackageFullName)"
}

function Invoke-Install {
    Assert-Environment
    Assert-InstallRoot $InstallRoot
    Assert-NormalDirectory $PayloadPath
    Assert-Payload $PayloadPath
    Assert-NoGuiProcess

    $packages = Get-CurrentPackages
    if ($packages.Count -gt 1) {
        throw "当前用户存在多个 ExtractAndDelete package，拒绝迁移。"
    }

    $oldPackage = $null
    $oldManifest = $null
    $oldInstallLocation = $null
    if ($packages.Count -eq 1) {
        $oldPackage = $packages[0]
        Assert-PackageIdentity $oldPackage
        $oldVersion = [version]$oldPackage.Version
        $newVersion = [version]$ExpectedVersion
        if ($oldVersion -gt $newVersion) {
            throw "当前已安装版本 $oldVersion 高于 $ExpectedVersion，拒绝降级。"
        }

        $oldInstallLocation = [string]$oldPackage.InstallLocation
        $oldManifest = Get-RegisteredManifestPath $oldPackage
        if ($null -eq $oldManifest) {
            Write-Log "旧 package 安装位置为空或清单不存在，将作为损坏残留修复：版本 $($oldPackage.Version)，位置 '$oldInstallLocation'"
        }
        else {
            try {
                # 4.1.0 used the MSIX build tokens in its loose-registration
                # manifest. Keep it as a rollback candidate, but do not treat
                # it as a valid new payload.
                Assert-Manifest $oldManifest ([string]$oldPackage.Version) -AllowLegacyRegistrationEntryPoint
                Write-Log "记录旧 package：版本 $($oldPackage.Version)，位置 $oldInstallLocation"
            }
            catch {
                Write-Log "旧 package 清单不完整，将作为损坏残留修复：$($_.Exception.Message)"
            }
        }
    }

    if ($null -ne $oldPackage) {
        Remove-ExactPackage $oldPackage
        Wait-ForPackageGone
    }

    $newManifest = Join-Path $PayloadPath 'AppxManifest.xml'
    try {
        Register-AndVerify $newManifest $PayloadPath $ExpectedVersion
    }
    catch {
        $newError = $_.Exception
        Write-Log "新 package 注册失败：$(Get-HResultText $newError) $($newError.Message)"

        foreach ($candidate in (Get-CurrentPackages)) {
            try {
                if (Test-SamePath ([string]$candidate.InstallLocation) $PayloadPath) {
                    Remove-ExactPackage $candidate
                }
            }
            catch {
                Write-Log "清理失败注册 package 时出错：$(Get-HResultText $_.Exception) $($_.Exception.Message)"
            }
        }

        if ($null -ne $oldManifest) {
            try {
                Register-AndVerify $oldManifest $oldInstallLocation ([string]$oldPackage.Version) -AllowLegacyRegistrationEntryPoint
                throw "新 package 注册失败，旧 package 已恢复。原错误 $(Get-HResultText $newError)：$($newError.Message)"
            }
            catch {
                if ($_.Exception.Message -like '新 package 注册失败，旧 package 已恢复。*') {
                    throw
                }
                throw "新 package 注册失败，且旧 package 恢复失败。新错误 $(Get-HResultText $newError)：$($newError.Message)；恢复错误 $(Get-HResultText $_.Exception)：$($_.Exception.Message)"
            }
        }

        throw "新 package 注册失败，且没有可恢复的旧 package。$(Get-HResultText $newError)：$($newError.Message)"
    }
}

function Invoke-Preflight {
    Assert-Environment
    Assert-InstallRoot $InstallRoot
    Assert-NoGuiProcess

    $packages = Get-CurrentPackages
    if ($packages.Count -gt 1) {
        throw '当前用户存在多个 ExtractAndDelete package，拒绝继续。'
    }
    if ($packages.Count -eq 1) {
        $package = $packages[0]
        Assert-PackageIdentity $package
        if ([version]$package.Version -gt [version]$ExpectedVersion) {
            throw "当前已安装版本 $($package.Version) 高于 $ExpectedVersion，拒绝降级。"
        }

        $oldManifest = Get-RegisteredManifestPath $package
        if ($null -eq $oldManifest) {
            Write-Log "检测到损坏的 ExtractAndDelete package 残留，将由新版本修复。InstallLocation='$([string]$package.InstallLocation)'"
        }
        else {
            try {
                Assert-Manifest $oldManifest ([string]$package.Version) -AllowLegacyRegistrationEntryPoint
            }
            catch {
                Write-Log "检测到旧 package 清单包含历史入口问题，将由新版本修复：$($_.Exception.Message)"
            }
        }
    }
}

function Invoke-Uninstall {
    Assert-CleanableInstallRoot $InstallRoot
    Assert-NoGuiProcess
    $packages = Get-CurrentPackages
    if ($packages.Count -gt 1) {
        throw "当前用户存在多个 ExtractAndDelete package，拒绝卸载。"
    }
    if ($packages.Count -eq 0) {
        Write-Log '目标 package 已不存在，继续由 Inno 清理安装文件。'
        return
    }

    Remove-ExactPackage $packages[0]
    Wait-ForPackageGone
    Write-Log 'package 已注销。'
}

try {
    Write-Log "开始执行 $Action 生命周期操作。"
    if ($Action -eq 'Preflight') {
        Invoke-Preflight
    }
    elseif ($Action -eq 'Install') {
        Invoke-Install
    }
    else {
        Invoke-Uninstall
    }
    Write-Log "$Action 生命周期操作成功。"
    exit 0
}
catch {
    Write-Log "生命周期操作失败：$(Get-HResultText $_.Exception) $($_.Exception.Message)"
    Write-Error $_
    exit 1
}
