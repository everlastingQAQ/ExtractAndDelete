[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solutionPath = Join-Path $repoRoot 'ExtractAndDelete.slnx'
$guiProjectPath = Join-Path $repoRoot 'src\ExtractAndDelete.Gui\ExtractAndDelete.Gui.csproj'
$shellProjectPath = Join-Path $repoRoot 'src\ExtractAndDelete.ShellExtension\ExtractAndDelete.ShellExtension.vcxproj'

Push-Location $repoRoot
try {
    & (Join-Path $repoRoot 'scripts\check-dev-environment.ps1')

    $programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    $vswherePath = Join-Path $programFilesX86 'Microsoft Visual Studio\Installer\vswhere.exe'
    $msbuildPath = (& $vswherePath -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1)

    & $msbuildPath $shellProjectPath /t:Restore /p:Configuration=Release /p:Platform=x64 /v:minimal
    if ($LASTEXITCODE -ne 0) { throw "Shell extension restore failed with exit code $LASTEXITCODE." }
    & $msbuildPath $shellProjectPath /p:Configuration=Release /p:Platform=x64 /m
    if ($LASTEXITCODE -ne 0) { throw "Shell extension build failed with exit code $LASTEXITCODE." }

    dotnet restore $solutionPath --force-evaluate
    if ($LASTEXITCODE -ne 0) { throw "Restore failed with exit code $LASTEXITCODE." }
    dotnet publish $guiProjectPath --configuration Release --runtime win-x64 --self-contained true --no-restore -p:GenerateAppxPackageOnBuild=false
    if ($LASTEXITCODE -ne 0) { throw "GUI publish failed with exit code $LASTEXITCODE." }

    $outputPath = Join-Path $repoRoot 'src\ExtractAndDelete.Gui\bin\Release\net10.0-windows10.0.22000.0\win-x64'
    $manifestPath = Join-Path $outputPath 'AppxManifest.xml'
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Packaged manifest was not produced: $manifestPath" }

    foreach ($requiredRelativePath in @(
        'ExtractAndDelete.Gui.exe',
        'ExtractAndDelete.ShellExtension.dll',
        'Microsoft.WindowsAppRuntime.dll',
        'ThirdParty\7-Zip\7z.exe',
        'ThirdParty\7-Zip\7z.dll',
        'ThirdParty\7-Zip\licenses\License.txt',
        'THIRD-PARTY-NOTICES.md')) {
        $requiredPath = Join-Path $outputPath $requiredRelativePath
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Required V4 package file is missing: $requiredPath"
        }
    }

    & (Join-Path $repoRoot 'scripts\acceptance-check.ps1') -Configuration Release

    $existingPackages = @(Get-AppxPackage -Name 'ExtractAndDelete' -ErrorAction SilentlyContinue)
    foreach ($package in $existingPackages) {
        if ($package.Name -ne 'ExtractAndDelete') { throw "Refusing to remove an unexpected package identity: $($package.Name)" }
        Remove-AppxPackage -Package $package.PackageFullName
    }

    Add-AppxPackage -Register -Path $manifestPath
    & (Join-Path $repoRoot 'scripts\verify-dev-install.ps1') -Configuration Release
    Write-Host "Developer package registered and verified from $manifestPath"
}
finally {
    Pop-Location
}
