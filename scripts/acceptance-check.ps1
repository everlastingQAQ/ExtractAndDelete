[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$releaseConfig = Get-Content -LiteralPath (Join-Path $repoRoot 'release-config.json') -Raw | ConvertFrom-Json
$expectedPackageVersion = [string]$releaseConfig.packageVersion
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $guiOutput = Join-Path $repoRoot "src\ExtractAndDelete.Gui\bin\$Configuration\net10.0-windows10.0.22000.0\win-x64"
}
else {
    $guiOutput = [IO.Path]::GetFullPath($OutputPath)
}
$manifestPath = Join-Path $guiOutput 'AppxManifest.xml'
$shellPath = Join-Path $guiOutput 'ExtractAndDelete.ShellExtension.dll'
$guiExePath = Join-Path $guiOutput 'ExtractAndDelete.Gui.exe'
$guiSevenZipRoot = Join-Path $guiOutput 'ThirdParty\7-Zip'
$guiProjectPath = Join-Path $repoRoot 'src\ExtractAndDelete.Gui\ExtractAndDelete.Gui.csproj'

if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Packaged manifest is missing: $manifestPath" }
if (-not (Test-Path -LiteralPath $guiExePath -PathType Leaf)) { throw "GUI executable is missing: $guiExePath" }
if (-not (Test-Path -LiteralPath $shellPath)) {
    throw "Shell extension output is missing. Build scripts/verify.ps1 first with the Native Desktop workload."
}
foreach ($root in @($guiSevenZipRoot)) {
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
    (Join-Path $guiOutput 'THIRD-PARTY-NOTICES.md'),
    (Join-Path $guiOutput 'LICENSE')
)
foreach ($requiredFile in $requiredGuiFiles) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required V4 GUI package file is missing: $requiredFile"
    }
}

[xml]$guiProject = Get-Content -LiteralPath $guiProjectPath -Raw
$propertyGroup = $guiProject.Project.PropertyGroup | Where-Object { $_.UseWindowsForms -eq 'true' } | Select-Object -First 1
if ($null -eq $propertyGroup) { throw 'GUI project is not configured to use WinForms native controls.' }
if ($propertyGroup.ApplicationHighDpiMode -ne 'PerMonitorV2') {
    throw 'GUI project must opt into PerMonitorV2 before creating any window.'
}
if ($propertyGroup.SatelliteResourceLanguages -ne 'zh-CN') {
    throw 'GUI project must package only the zh-CN satellite resources for the lightweight release.'
}

$cliArtifacts = @(Get-ChildItem -LiteralPath $guiOutput -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like 'ExtractAndDelete.Cli*' })
if ($cliArtifacts.Count -ne 0) {
    $paths = $cliArtifacts | ForEach-Object FullName
    throw "Frozen CLI artifacts leaked into the GUI package output: $($paths -join ', ')"
}

[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
$namespace = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
$namespace.AddNamespace('foundation', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$namespace.AddNamespace('desktop4', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10/4')
$namespace.AddNamespace('desktop5', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10/5')
$namespace.AddNamespace('com', 'http://schemas.microsoft.com/appx/manifest/com/windows10')

$identity = $manifest.SelectSingleNode('/foundation:Package/foundation:Identity', $namespace)
if ($null -eq $identity -or $identity.Name -ne 'ExtractAndDelete') { throw 'Package identity is not ExtractAndDelete.' }
if ($identity.Version -ne $expectedPackageVersion) { throw "Package version is not ${expectedPackageVersion}: $($identity.Version)" }
$application = $manifest.SelectSingleNode('/foundation:Package/foundation:Applications/foundation:Application', $namespace)
if ($null -eq $application) { throw 'The package Application node is missing.' }
if ($identity.ProcessorArchitecture -ne 'x64') { throw "Package architecture is not x64: $($identity.ProcessorArchitecture)" }
if ($application.Executable -ne 'ExtractAndDelete.Gui.exe' -or
    $application.EntryPoint -ne 'Windows.FullTrustApplication') {
    throw "Loose-registration entry point is invalid: $($application.Executable) / $($application.EntryPoint)"
}
if ($manifest.OuterXml -match '\$targetnametoken\$|\$targetentrypoint\$') {
    throw 'The loose-registration manifest still contains MSIX build placeholders.'
}
if (-not (Test-Path -LiteralPath (Join-Path $guiOutput $application.Executable) -PathType Leaf)) {
    throw "The manifest executable is missing from the package output: $($application.Executable)"
}
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

Write-Host "V$($releaseConfig.semanticVersion) Developer Preview output layout checks passed."
Write-Host 'Package registration was not checked.'
Write-Host "For a repository registration, run scripts\verify-dev-install.ps1 -ExpectedInstallLocation `"$guiOutput`"."
Write-Host "Package identity: $($identity.Name)"
Write-Host "GUI output: $guiOutput"
Write-Host "Shell extension: $shellPath"
Write-Host 'CLI is source-frozen and is not part of the default build or delivery.'
