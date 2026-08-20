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
    exit 0
}

$remainingPackages = @(Get-AppxPackage -Name 'ExtractAndDelete' -ErrorAction SilentlyContinue)
if ($remainingPackages.Count -ne 0) {
    throw "ExtractAndDelete package remains registered after uninstall: $($remainingPackages.PackageFullName -join ', ')"
}

Write-Host 'ExtractAndDelete Developer package unregistered.'
Write-Host 'Restart Windows Explorer or sign out and sign in again to refresh the context menu.'
