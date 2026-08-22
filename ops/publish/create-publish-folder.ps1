#!/usr/bin/env pwsh

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$publishRoot = Join-Path $repoRoot "artifacts/publish/ProjectManagement"

function Assert-ExitCode([string] $operation) {
    if ($LASTEXITCODE -ne 0) {
        throw "$operation failed with exit code $LASTEXITCODE."
    }
}

Push-Location $repoRoot
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "dotnet CLI is required to publish the application."
    }

    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
        throw "npm is required to build Notebook assets."
    }

    $requiredBaselineFiles = @(
        "Infrastructure/DatabaseStartupMigrator.cs",
        "Infrastructure/ApplicationDatabaseSchemaValidator.cs",
        "Migrations/20261201150000_ReconcileProjectStageCompletionConstraint.cs",
        "Migrations/20261201160000_FinalizeProjectStageCompletionConstraint.cs",
        "Migrations/immutable-migration-ids.txt"
    )

    foreach ($relativePath in $requiredBaselineFiles) {
        if (-not (Test-Path (Join-Path $repoRoot $relativePath) -PathType Leaf)) {
            throw "Source baseline is incomplete. Missing '$relativePath'. Do not publish the older 51-migration source tree."
        }
    }

    $requiredPublicationFonts = @(
        "DMSans-Regular.ttf",
        "DMSans-Medium.ttf",
        "DMSans-SemiBold.ttf",
        "DMSans-Bold.ttf",
        "DMSans-Italic.ttf",
        "DMSans-BoldItalic.ttf"
    )
    $sourceFontRoot = Join-Path $repoRoot "wwwroot/fonts/publications/dm-sans"
    foreach ($fontFile in $requiredPublicationFonts) {
        if (-not (Test-Path (Join-Path $sourceFontRoot $fontFile) -PathType Leaf)) {
            throw "Compendium publication source is incomplete. Missing DM Sans face '$fontFile'."
        }
    }

    Write-Host "Validating application JSON configuration..."
    Get-ChildItem -Path $repoRoot -Filter "appsettings*.json" -File | ForEach-Object {
        try {
            Get-Content $_.FullName -Raw -ErrorAction Stop |
                ConvertFrom-Json -ErrorAction Stop |
                Out-Null
            Write-Host "  Valid: $($_.Name)"
        }
        catch {
            throw "Invalid JSON in '$($_.FullName)': $($_.Exception.Message)"
        }
    }

    $manifest = Get-Content (Join-Path $repoRoot "Migrations/immutable-migration-ids.txt") |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and -not $_.StartsWith("#") }

    if ($manifest.Count -ne 62 -or $manifest[-1] -ne "20261201160000_FinalizeProjectStageCompletionConstraint") {
        throw "Application migration manifest is incomplete. Expected 62 ordered IDs ending in 20261201160000_FinalizeProjectStageCompletionConstraint."
    }

    if (-not (Test-Path (Join-Path $repoRoot "node_modules/esbuild"))) {
        Write-Host "Restoring Node.js dependencies..."
        npm ci --ignore-scripts
        Assert-ExitCode "npm ci"
    }

    if (Test-Path $publishRoot) {
        Remove-Item $publishRoot -Recurse -Force
    }
    New-Item -Path $publishRoot -ItemType Directory -Force | Out-Null

    Write-Host "Publishing clean Release output to $publishRoot..."
    dotnet publish (Join-Path $repoRoot "ProjectManagement.csproj") `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $publishRoot `
        /p:UseAppHost=true
    Assert-ExitCode "dotnet publish"

    $requiredPublishedFiles = @(
        "ProjectManagement.exe",
        "ProjectManagement.dll",
        "ProjectManagement.deps.json",
        "ProjectManagement.runtimeconfig.json",
        "appsettings.json",
        "appsettings.Production.json",
        "web.config",
        "Migrations/immutable-migration-ids.txt"
    )

    foreach ($relativePath in $requiredPublishedFiles) {
        if (-not (Test-Path (Join-Path $publishRoot $relativePath) -PathType Leaf)) {
            throw "Publish validation failed: required file '$relativePath' is missing."
        }
    }

    $requiredSelfContainedRuntimeFiles = @(
        "coreclr.dll",
        "hostfxr.dll",
        "hostpolicy.dll",
        "System.Private.CoreLib.dll"
    )
    foreach ($runtimeFile in $requiredSelfContainedRuntimeFiles) {
        if (-not (Test-Path (Join-Path $publishRoot $runtimeFile) -PathType Leaf)) {
            throw "Publish validation failed: self-contained win-x64 runtime file '$runtimeFile' is missing."
        }
    }

    $publishedFontRoot = Join-Path $publishRoot "wwwroot/fonts/publications/dm-sans"
    foreach ($fontFile in $requiredPublicationFonts) {
        $fontPath = Join-Path $publishedFontRoot $fontFile
        if (-not (Test-Path $fontPath -PathType Leaf)) {
            throw "Publish validation failed: Compendium font '$fontFile' is missing from the offline payload."
        }
        if ((Get-Item $fontPath).Length -le 0) {
            throw "Publish validation failed: Compendium font '$fontFile' is empty."
        }
    }

    if (-not (Get-ChildItem -Path $publishRoot -Filter "libSkiaSharp.dll" -File -Recurse | Select-Object -First 1)) {
        throw "Publish validation failed: the win-x64 SkiaSharp native library is missing."
    }

    $diagnosticRoot = Join-Path $publishRoot "logs/compendium"
    New-Item -Path $diagnosticRoot -ItemType Directory -Force | Out-Null

    Get-Content (Join-Path $publishRoot "appsettings.Production.json") -Raw |
        ConvertFrom-Json -ErrorAction Stop |
        Out-Null

    [xml] $webConfig = Get-Content (Join-Path $publishRoot "web.config") -Raw
    $requestLimit = [long]$webConfig.configuration.location.'system.webServer'.security.requestFiltering.requestLimits.maxAllowedContentLength
    if ($requestLimit -lt 268435456) {
        throw "Published web.config does not contain the required IIS request limit."
    }

    $publishedManifest = Get-Content (Join-Path $publishRoot "Migrations/immutable-migration-ids.txt") |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and -not $_.StartsWith("#") }
    if ($publishedManifest.Count -ne 62 -or $publishedManifest[-1] -ne "20261201160000_FinalizeProjectStageCompletionConstraint") {
        throw "Published migration manifest is incomplete."
    }

    Write-Host "Running Compendium offline PDF dependency self-test..."
    $selfTestOutput = & (Join-Path $publishRoot "ProjectManagement.exe") --compendium-offline-self-test 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Compendium offline self-test failed with exit code $LASTEXITCODE.`n$($selfTestOutput -join [Environment]::NewLine)"
    }
    Write-Host "  $($selfTestOutput -join [Environment]::NewLine)"

    Write-Host "Publish folder created and validated at $publishRoot" -ForegroundColor Green
}
finally {
    Pop-Location
}
