[CmdletBinding()]
param(
    [string]$Root = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path -LiteralPath $Root).Path
$assetRoot = Join-Path $repoRoot 'third_party\7zip\26.02'
$sumFile = Join-Path $assetRoot 'SHA256SUMS'

if (-not (Test-Path -LiteralPath $sumFile -PathType Leaf)) {
    throw "Missing third-party hash manifest: $sumFile"
}

$expected = @{}
foreach ($line in Get-Content -LiteralPath $sumFile) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    if ($line -notmatch '^([0-9a-fA-F]{64})\s+(.+)$') {
        throw "Invalid SHA-256 manifest line: $line"
    }

    $relative = $Matches[2].TrimStart('*')
    $expected[$relative] = $Matches[1].ToLowerInvariant()
}

foreach ($entry in $expected.GetEnumerator()) {
    $path = Join-Path $assetRoot ($entry.Key -replace '/', '\')
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing third-party asset: $path"
    }

    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $entry.Value) {
        throw "SHA-256 mismatch for $path. Expected $($entry.Value), got $actual."
    }
}

$allowed = @(
    'x64\7z.exe',
    'x64\7z.dll',
    'licenses\License.txt',
    'licenses\copying.txt',
    'licenses\unRarLicense.txt',
    'SHA256SUMS',
    'SOURCE.md'
)
$actualFiles = Get-ChildItem -LiteralPath $assetRoot -Recurse -File |
    ForEach-Object { $_.FullName.Substring($assetRoot.Length + 1) }
foreach ($file in $actualFiles) {
    if ($allowed -notcontains $file) {
        throw "Unexpected third-party file: $file"
    }
}

Write-Output "7-Zip 26.02 x64 third-party assets verified."
