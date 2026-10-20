#!/usr/bin/env pwsh

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$publishRoot = Join-Path $repoRoot "artifacts/publish/ProjectManagement"

function Assert-ExitCode([string] $operation) {
    if ($LASTEXITCODE -ne 0) {
        throw "$operation failed with exit code $LASTEXITCODE."
    }
}

function Read-MigrationManifest([string] $manifestPath) {
    return @(Get-Content $manifestPath |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and -not $_.StartsWith("#") })
}

# Validates the application migration manifest against the migration sources so the
# check never needs editing when a migration is added. The manifest must be non-empty,
# free of duplicates, in ascending ordinal order, and identical to the set of
# [Migration("...")] identifiers declared under Migrations/ (so its last entry is the
# newest migration). Assembly-level parity is covered by ApplicationDatabaseMigrationsTests.
function Assert-ApplicationMigrationManifest([string] $manifestPath, [string] $migrationsDirectory) {
    # @() keeps a single-entry manifest an array; PowerShell unrolls returned arrays.
    $manifest = @(Read-MigrationManifest $manifestPath)
    if ($manifest.Count -eq 0) {
        throw "Application migration manifest is incomplete. '$manifestPath' contains no migration IDs."
    }

    $duplicates = @($manifest | Group-Object -CaseSensitive | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name })
    if ($duplicates.Count -gt 0) {
        throw "Application migration manifest is invalid. Duplicate IDs: $($duplicates -join ', ')."
    }

    for ($i = 1; $i -lt $manifest.Count; $i++) {
        if ([string]::CompareOrdinal($manifest[$i - 1], $manifest[$i]) -ge 0) {
            throw "Application migration manifest is invalid. '$($manifest[$i])' is out of order after '$($manifest[$i - 1])'."
        }
    }

    $sourceIds = [string[]]@(Get-ChildItem -Path $migrationsDirectory -Filter "*.cs" -File |
        Select-String -Pattern '\[Migration\("([^"]+)"\)\]' -AllMatches |
        ForEach-Object { $_.Matches } |
        ForEach-Object { $_.Groups[1].Value })

    if ($sourceIds.Count -eq 0) {
        throw "Application migration sources are missing under '$migrationsDirectory'."
    }

    # Reject duplicate declarations before comparing; EF cannot represent two migrations sharing an ID.
    $duplicateSourceIds = @($sourceIds | Group-Object -CaseSensitive | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name })
    if ($duplicateSourceIds.Count -gt 0) {
        throw "Application migration sources are invalid. Duplicate [Migration] IDs: $($duplicateSourceIds -join ', ')."
    }
    [Array]::Sort($sourceIds, [StringComparer]::Ordinal)

    $missingFromManifest = @($sourceIds | Where-Object { $manifest -cnotcontains $_ })
    $missingFromSources = @($manifest | Where-Object { $sourceIds -cnotcontains $_ })
    if ($missingFromManifest.Count -gt 0 -or $missingFromSources.Count -gt 0) {
        throw "Application migration manifest is incomplete. Missing from manifest: [$($missingFromManifest -join ', ')]. Without migration source: [$($missingFromSources -join ', ')]."
    }

    if ($manifest[-1] -cne $sourceIds[-1]) {
        throw "Application migration manifest is incomplete. Expected the last ID to be the newest migration '$($sourceIds[-1])' but found '$($manifest[-1])'."
    }

    return ,$manifest
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

    $manifest = Assert-ApplicationMigrationManifest `
        (Join-Path $repoRoot "Migrations/immutable-migration-ids.txt") `
        (Join-Path $repoRoot "Migrations")
    Write-Host "Application migration manifest: $($manifest.Count) ordered IDs ending in $($manifest[-1])."

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

    $publishedManifest = @(Read-MigrationManifest (Join-Path $publishRoot "Migrations/immutable-migration-ids.txt"))
    if (($publishedManifest -join "`n") -cne ($manifest -join "`n")) {
        throw "Published migration manifest is incomplete. Expected $($manifest.Count) IDs ending in $($manifest[-1]) but found $($publishedManifest.Count)."
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
