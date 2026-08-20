[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$packages = @(Get-AppxPackage -Name 'ExtractAndDelete' -ErrorAction SilentlyContinue)
foreach ($package in $packages) {
    if ($package.Name -ne 'ExtractAndDelete') {
        throw "Refusing to remove an unexpected package identity: $($package.Name)"
    }
    Remove-AppxPackage -Package $package.PackageFullName
    Write-Host "Removed package $($package.PackageFullName)"
}

if ($packages.Count -eq 0) {
    Write-Host 'No ExtractAndDelete Developer package is registered for the current user.'
}
