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
        --self-contained false `
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

    Write-Host "Publish folder created and validated at $publishRoot" -ForegroundColor Green
}
finally {
    Pop-Location
}
