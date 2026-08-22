#!/usr/bin/env pwsh

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $PublishRoot
)

$ErrorActionPreference = "Stop"
$resolvedRoot = (Resolve-Path $PublishRoot).Path
$application = Join-Path $resolvedRoot "ProjectManagement.exe"

if (-not (Test-Path $application -PathType Leaf)) {
    throw "ProjectManagement.exe was not found in '$resolvedRoot'."
}

$requiredFonts = @(
    "DMSans-Regular.ttf",
    "DMSans-Medium.ttf",
    "DMSans-SemiBold.ttf",
    "DMSans-Bold.ttf",
    "DMSans-Italic.ttf",
    "DMSans-BoldItalic.ttf"
)
$fontRoot = Join-Path $resolvedRoot "wwwroot/fonts/publications/dm-sans"
foreach ($font in $requiredFonts) {
    $path = Join-Path $fontRoot $font
    if (-not (Test-Path $path -PathType Leaf) -or (Get-Item $path).Length -le 0) {
        throw "Offline Compendium font validation failed for '$path'."
    }
}

$result = & $application --compendium-offline-self-test 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Compendium offline self-test failed with exit code $LASTEXITCODE.`n$($result -join [Environment]::NewLine)"
}

Write-Host ($result -join [Environment]::NewLine)
Write-Host "Compendium offline payload validation passed for '$resolvedRoot'." -ForegroundColor Green
