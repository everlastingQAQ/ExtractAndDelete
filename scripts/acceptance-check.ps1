[CmdletBinding()]
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$guiOutput = Join-Path $repoRoot "src\ExtractAndDelete.Gui\bin\$Configuration\net10.0-windows10.0.22000.0\win-x64"
$manifestPath = Join-Path $guiOutput 'AppxManifest.xml'
$shellPath = Join-Path $guiOutput 'ExtractAndDelete.ShellExtension.dll'
$guiExePath = Join-Path $guiOutput 'ExtractAndDelete.Gui.exe'
$cliPath = Join-Path $repoRoot "src\ExtractAndDelete.Cli\bin\$Configuration\net10.0-windows10.0.22000.0\ExtractAndDelete.Cli.dll"
$guiSevenZipRoot = Join-Path $guiOutput 'ThirdParty\7-Zip'
$cliSevenZipRoot = Join-Path $repoRoot "src\ExtractAndDelete.Cli\bin\$Configuration\net10.0-windows10.0.22000.0\ThirdParty\7-Zip"

if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Packaged manifest is missing: $manifestPath" }
if (-not (Test-Path -LiteralPath $guiExePath -PathType Leaf)) { throw "GUI executable is missing: $guiExePath" }
if (-not (Test-Path -LiteralPath $cliPath)) { throw "CLI output is missing: $cliPath" }
if (-not (Test-Path -LiteralPath $shellPath)) {
    throw "Shell extension output is missing. Build scripts/verify.ps1 first with the Native Desktop workload."
}
foreach ($root in @($guiSevenZipRoot, $cliSevenZipRoot)) {
    foreach ($fileName in @('7z.exe', '7z.dll')) {
        $enginePath = Join-Path $root $fileName
        if (-not (Test-Path -LiteralPath $enginePath -PathType Leaf)) {
            throw "Bundled 7-Zip engine file is missing: $enginePath"
        }
    }
}
$requiredGuiFiles = @(
    $guiExePath,
    (Join-Path $guiOutput 'Microsoft.WindowsAppRuntime.dll'),
    (Join-Path $guiOutput 'ThirdParty\7-Zip\licenses\License.txt'),
    (Join-Path $guiOutput 'THIRD-PARTY-NOTICES.md')
)
foreach ($requiredFile in $requiredGuiFiles) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required V3 GUI package file is missing: $requiredFile"
    }
}

[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
$namespace = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
$namespace.AddNamespace('foundation', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$namespace.AddNamespace('desktop4', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10/4')
$namespace.AddNamespace('desktop5', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10/5')
$namespace.AddNamespace('com', 'http://schemas.microsoft.com/appx/manifest/com/windows10')

$identity = $manifest.SelectSingleNode('/foundation:Package/foundation:Identity', $namespace)
if ($null -eq $identity -or $identity.Name -ne 'ExtractAndDelete') { throw 'Package identity is not ExtractAndDelete.' }
if ($identity.Version -ne '3.0.0.0') { throw "Package version is not V3 3.0.0.0: $($identity.Version)" }
$runtimeDependency = $manifest.SelectSingleNode('//foundation:PackageDependency[contains(@Name, "WindowsAppRuntime")]', $namespace)
if ($null -ne $runtimeDependency) { throw 'The package still depends on an external Windows App SDK runtime.' }
$runtimeDll = Join-Path $guiOutput 'Microsoft.WindowsAppRuntime.dll'
if (-not (Test-Path -LiteralPath $runtimeDll)) { throw "Self-contained Windows App SDK runtime is missing: $runtimeDll" }
foreach ($assetName in @('StoreLogo.png', 'Square44x44Logo.png', 'Square71x71Logo.png', 'Square150x150Logo.png')) {
    $assetPath = Join-Path $guiOutput "Assets\$assetName"
    if (-not (Test-Path -LiteralPath $assetPath)) { throw "Package asset is missing: $assetPath" }
}
foreach ($extension in @('.zip', '.7z', '.rar', '.tar')) {
    $verb = $manifest.SelectSingleNode("//desktop5:ItemType[@Type='$extension']/desktop5:Verb", $namespace)
    if ($null -eq $verb -or $verb.Id -ne 'ExtractAndDelete') {
        throw "The $extension Explorer command is missing."
    }
}
$comClass = $manifest.SelectSingleNode('//com:Class[@Id="4F4F8F37-B78C-4B3D-90CE-8D16C4483B8E"]', $namespace)
if ($null -eq $comClass) { throw 'The Shell Extension COM class is missing.' }
if ($comClass.Path -ne 'ExtractAndDelete.ShellExtension.dll' -or $comClass.ThreadingModel -ne 'STA') {
    throw 'The Shell Extension COM class path or STA threading model is invalid.'
}

$forbiddenArtifacts = @(Get-ChildItem -LiteralPath $repoRoot -Recurse -File -Include '*.pfx', '*.cer', '*.msix', '*.msixbundle', '*.appinstaller' -ErrorAction SilentlyContinue)
if ($forbiddenArtifacts.Count -ne 0) {
    $paths = $forbiddenArtifacts | ForEach-Object FullName
    throw "Forbidden signing/package artifacts found in the repository: $($paths -join ', ')"
}

Write-Host 'V3 Developer RC output layout checks passed.'
Write-Host 'Package registration was not checked.'
Write-Host 'Run scripts\verify-dev-install.ps1 after deploy-dev.ps1.'
Write-Host "Package identity: $($identity.Name)"
Write-Host "GUI output: $guiOutput"
Write-Host "Shell extension: $shellPath"
