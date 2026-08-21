[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',
    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$configPath = Join-Path $repoRoot 'release-config.json'
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$semanticVersion = [string]$config.semanticVersion
$packageVersion = [string]$config.packageVersion
$releaseRoot = Join-Path $repoRoot "artifacts\release\$semanticVersion"
$payloadPath = Join-Path $releaseRoot 'payload'
$solutionPath = Join-Path $repoRoot 'ExtractAndDelete.slnx'
$guiProjectPath = Join-Path $repoRoot 'src\ExtractAndDelete.Gui\ExtractAndDelete.Gui.csproj'
$shellProjectPath = Join-Path $repoRoot 'src\ExtractAndDelete.ShellExtension\ExtractAndDelete.ShellExtension.vcxproj'
$issPath = Join-Path $repoRoot 'installer\ExtractAndDelete.iss'

function Remove-GeneratedDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $allowedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\release')).TrimEnd('\')
    if (-not $fullPath.StartsWith($allowedRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝删除发布输出目录之外的路径：$fullPath"
    }
    if (-not (Test-Path -LiteralPath $fullPath)) { return }
    $item = Get-Item -LiteralPath $fullPath -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "发布输出目录不能是 reparse point：$fullPath"
    }
    Remove-Item -LiteralPath $fullPath -Recurse -Force
}

function Get-MsbuildPath {
    $vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $path = (& $vswherePath -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw '未找到带 x64 MSVC 工具链的 MSBuild。'
    }
    return $path
}

Push-Location $repoRoot
try {
    & (Join-Path $repoRoot 'scripts\check-release-environment.ps1') -IsccPath $IsccPath
    if ($LASTEXITCODE -ne 0) { throw 'Release 构建环境检查失败。' }

    $msbuildPath = Get-MsbuildPath
    Remove-GeneratedDirectory $releaseRoot
    New-Item -ItemType Directory -Path $payloadPath -Force | Out-Null

    & $msbuildPath $shellProjectPath /t:Restore /p:Configuration=$Configuration /p:Platform=x64 /v:minimal
    if ($LASTEXITCODE -ne 0) { throw "Shell extension restore failed with exit code $LASTEXITCODE." }
    & $msbuildPath $shellProjectPath /p:Configuration=$Configuration /p:Platform=x64 /m /v:minimal
    if ($LASTEXITCODE -ne 0) { throw "Shell extension build failed with exit code $LASTEXITCODE." }

    dotnet restore $solutionPath --force-evaluate
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }
    dotnet publish $guiProjectPath --configuration $Configuration --runtime win-x64 --self-contained true --no-restore `
        -p:GenerateAppxPackageOnBuild=false -p:PublishDir=$payloadPath
    if ($LASTEXITCODE -ne 0) { throw "GUI publish failed with exit code $LASTEXITCODE." }

    Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $payloadPath 'LICENSE') -Force
    # `dotnet publish` emits the self-contained executable and package assets, but
    # loose-file registration still needs the source package manifest beside them.
    Copy-Item -LiteralPath (Join-Path $repoRoot 'src\ExtractAndDelete.Gui\Package.appxmanifest') `
        -Destination (Join-Path $payloadPath 'AppxManifest.xml') -Force

    $pdbFiles = @(Get-ChildItem -LiteralPath $payloadPath -Recurse -File -Filter '*.pdb' -Force)
    foreach ($pdb in $pdbFiles) {
        Remove-Item -LiteralPath $pdb.FullName -Force
    }

    $manifestPath = Join-Path $payloadPath 'AppxManifest.xml'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "发布清单不存在：$manifestPath"
    }
    [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
    $identity = $manifest.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Identity']")
    if ($null -eq $identity -or $identity.GetAttribute('Name') -ne 'ExtractAndDelete' -or
        $identity.GetAttribute('Publisher') -ne [string]$config.publisher -or
        $identity.GetAttribute('Version') -ne $packageVersion) {
        throw '发布清单身份、Publisher 或版本不符合 release-config.json。'
    }

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
        $path = Join-Path $payloadPath $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "发布 payload 缺少文件：$relativePath"
        }
    }

    $cliFiles = @(Get-ChildItem -LiteralPath $payloadPath -Recurse -File -Force |
        Where-Object { $_.Name -like 'ExtractAndDelete.Cli*' })
    if ($cliFiles.Count -ne 0) {
        throw '发布 payload 包含冻结 CLI 文件。'
    }

    $payloadFiles = @(Get-ChildItem -LiteralPath $payloadPath -Recurse -File -Force |
        Where-Object { $_.Name -ne 'payload.sha256' })
    [Int64]$payloadSizeBytes = 0
    foreach ($payloadFile in $payloadFiles) {
        if ($payloadFile.Length -gt ([Int64]::MaxValue - $payloadSizeBytes)) {
            throw 'payload 大小求和溢出。'
        }
        $payloadSizeBytes += [Int64]$payloadFile.Length
    }

    $hashLines = @($payloadFiles |
        Where-Object { $_.Name -ne 'payload.sha256' } |
        ForEach-Object {
            $relative = $_.FullName.Substring($payloadPath.Length + 1).Replace('\', '/')
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash *$relative"
        } |
        Sort-Object)
    Set-Content -LiteralPath (Join-Path $payloadPath 'payload.sha256') -Value $hashLines -Encoding UTF8

    & (Join-Path $repoRoot 'scripts\acceptance-check.ps1') -Configuration $Configuration -OutputPath $payloadPath
    if ($LASTEXITCODE -ne 0) { throw 'Package layout acceptance check failed.' }

    if ([string]::IsNullOrWhiteSpace($IsccPath)) {
        $IsccPath = Get-ChildItem -LiteralPath @(
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
        ) -File -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
    }
    if ([string]::IsNullOrWhiteSpace($IsccPath)) { throw 'ISCC.exe 路径为空。' }

    & $IsccPath "/DReleaseVersion=$semanticVersion" "/DPackageVersion=$packageVersion" "/DPayloadSizeBytes=$payloadSizeBytes" "/DPayloadDir=$payloadPath" "/DOutputDir=$releaseRoot" $issPath
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE." }

    $installerPath = Join-Path $releaseRoot "ExtractAndDelete-Setup-$semanticVersion-x64.exe"
    if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
        throw "安装器未生成：$installerPath"
    }
    $hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$installerPath.sha256" -Value "$hash *$(Split-Path -Leaf $installerPath)" -Encoding ASCII

    Write-Host "Installer built: $installerPath"
    Write-Host "SHA-256: $hash"
}
finally {
    Pop-Location
}
