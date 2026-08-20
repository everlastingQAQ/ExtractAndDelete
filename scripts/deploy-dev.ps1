[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solutionPath = Join-Path $repoRoot 'ExtractAndDelete.slnx'
$guiProjectPath = Join-Path $repoRoot 'src\ExtractAndDelete.Gui\ExtractAndDelete.Gui.csproj'
$shellProjectPath = Join-Path $repoRoot 'src\ExtractAndDelete.ShellExtension\ExtractAndDelete.ShellExtension.vcxproj'

$developerModeKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
$developerMode = 0
$unlockProperties = Get-ItemProperty -LiteralPath $developerModeKey -ErrorAction SilentlyContinue
if ($null -ne $unlockProperties -and $unlockProperties.PSObject.Properties.Name -contains 'AllowDevelopmentWithoutDevLicense') {
    $developerMode = [int]$unlockProperties.AllowDevelopmentWithoutDevLicense
}
if ($developerMode -ne 1) {
    throw 'Windows Developer Mode is disabled. Enable it in Settings before deploying the Developer package.'
}

Push-Location $repoRoot
try {
    $vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswherePath)) { throw 'vswhere.exe was not found.' }
    $msbuildPath = (& $vswherePath -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($msbuildPath) -or -not (Test-Path -LiteralPath $msbuildPath)) { throw 'x64 MSBuild/C++ toolchain was not found.' }

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

    $existingPackages = @(Get-AppxPackage -Name 'ExtractAndDelete' -ErrorAction SilentlyContinue)
    foreach ($package in $existingPackages) {
        if ($package.Name -ne 'ExtractAndDelete') { throw "Refusing to remove an unexpected package identity: $($package.Name)" }
        Remove-AppxPackage -Package $package.PackageFullName
    }

    Add-AppxPackage -Register -Path $manifestPath -DisableDevelopmentMode
    Write-Host "Developer package registered from $manifestPath"
}
finally {
    Pop-Location
}
