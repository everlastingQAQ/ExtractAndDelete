[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solutionPath = Join-Path $repoRoot 'ExtractAndDelete.slnx'

Push-Location $repoRoot
try {
    & (Join-Path $repoRoot 'scripts\verify-third-party.ps1')

    dotnet restore $solutionPath --force-evaluate
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }

    $vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswherePath)) {
        throw "Visual Studio Installer vswhere.exe was not found. Install the Windows Native Desktop workload."
    }

    $msbuildPath = (& $vswherePath -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($msbuildPath) -or -not (Test-Path -LiteralPath $msbuildPath)) {
        throw "MSBuild with the x64 C++ toolchain was not found. Install the Windows Native Desktop workload."
    }

    $shellProjectPath = Join-Path $repoRoot 'src\ExtractAndDelete.ShellExtension\ExtractAndDelete.ShellExtension.vcxproj'
    & $msbuildPath $shellProjectPath /t:Restore /p:Configuration=Release /p:Platform=x64 /v:minimal
    if ($LASTEXITCODE -ne 0) { throw "Shell extension restore failed with exit code $LASTEXITCODE." }
    & $msbuildPath $shellProjectPath /p:Configuration=Release /p:Platform=x64 /m
    if ($LASTEXITCODE -ne 0) { throw "Shell extension build failed with exit code $LASTEXITCODE." }

    dotnet build $solutionPath --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Release build failed with exit code $LASTEXITCODE." }

    dotnet test $solutionPath --configuration Release --no-build --filter 'Category!=WindowsIntegration'
    if ($LASTEXITCODE -ne 0) { throw "普通测试失败，退出码 $LASTEXITCODE。" }
}
finally {
    Pop-Location
}
