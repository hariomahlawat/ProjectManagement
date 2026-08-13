param(
    [switch]$SkipDotNet
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $root
try {
    Write-Host 'PRISM Publications Phase 21.1 - Cover Editor Final Hardening' -ForegroundColor Cyan
    Write-Host "Project root: $root"

    $required = @(
        'Models/Publications/BrochurePreset.cs',
        'Services/Publications/BrochureContracts.cs',
        'Services/Publications/BrochurePresetContracts.cs',
        'Services/Publications/BrochurePresetService.cs',
        'Services/Publications/BrochurePublicationService.cs',
        'Services/Publications/BrochureReviewFingerprint.cs',
        'Utilities/Reporting/BrochurePdfReportBuilder.cs',
        'Pages/Projects/Publications/Brochure/Index.cshtml',
        'Pages/Projects/Publications/Brochure/Index.cshtml.cs',
        'Data/ApplicationDbContext.cs',
        'Migrations/20261208113000_AddBrochureCoverVisibilityControls.cs',
        'Migrations/ApplicationDbContextModelSnapshot.cs',
        'Migrations/immutable-migration-ids.txt',
        'wwwroot/css/pages/projects-publications.css',
        'wwwroot/js/pages/projects-brochure.js',
        'wwwroot/js/projects/publications-brochure-contract.test.js',
        'ProjectManagement.Tests/Publications/BrochurePdfReportBuilderTests.cs',
        'ProjectManagement.Tests/Publications/BrochurePresetServiceTests.cs',
        'ProjectManagement.Tests/Publications/BrochureReviewFingerprintTests.cs'
    )
    foreach ($path in $required) {
        if (-not (Test-Path $path)) { throw "Missing required Phase 21.1 file: $path" }
    }

    $view = Get-Content 'Pages/Projects/Publications/Brochure/Index.cshtml' -Raw
    $pageModel = Get-Content 'Pages/Projects/Publications/Brochure/Index.cshtml.cs' -Raw
    $contracts = Get-Content 'Services/Publications/BrochureContracts.cs' -Raw
    $presetContracts = Get-Content 'Services/Publications/BrochurePresetContracts.cs' -Raw
    $presetModel = Get-Content 'Models/Publications/BrochurePreset.cs' -Raw
    $presetService = Get-Content 'Services/Publications/BrochurePresetService.cs' -Raw
    $renderer = Get-Content 'Utilities/Reporting/BrochurePdfReportBuilder.cs' -Raw
    $fingerprint = Get-Content 'Services/Publications/BrochureReviewFingerprint.cs' -Raw
    $js = Get-Content 'wwwroot/js/pages/projects-brochure.js' -Raw
    $css = Get-Content 'wwwroot/css/pages/projects-publications.css' -Raw
    $migration = Get-Content 'Migrations/20261208113000_AddBrochureCoverVisibilityControls.cs' -Raw
    $immutable = Get-Content 'Migrations/immutable-migration-ids.txt' -Raw

    $visibilityFields = @(
        'ShowFrontCoverKicker',
        'ShowFrontCoverDescriptor',
        'ShowBackCoverKicker',
        'ShowBackCoverStrapline',
        'ShowBackCoverEdition'
    )
    foreach ($field in $visibilityFields) {
        foreach ($contract in @($view, $pageModel, $contracts, $presetContracts, $presetModel, $presetService, $renderer, $js, $migration)) {
            if (-not $contract.Contains($field)) { throw "Phase 21.1 visibility contract is incomplete for: $field" }
        }
    }

    if (-not $view.Contains('data-cover-text-summary')) { throw 'Cover text collapsed summary is missing.' }
    if (-not $view.Contains('data-cover-edit-strapline')) { throw 'Cover Text -> Edit strapline action is missing.' }
    if (-not $view.Contains('data-cover-edit-marking')) { throw 'Cover Text -> Edit marking action is missing.' }
    if (-not $view.Contains('data-introduction-heading')) { throw 'Additional introduction heading state hook is missing.' }
    if (-not $view.Contains('data-introduction-text')) { throw 'Additional introduction body state hook is missing.' }
    if (-not $css.Contains('.brochure-cover-line.is-suppressed')) { throw 'Suppressed cover-line visual state is missing.' }
    if (-not $js.Contains('renderCoverTextUi')) { throw 'Cover text summary/visibility renderer is missing.' }
    if (-not $js.Contains('updateIntroductionHeadingState')) { throw 'Additional introduction heading state controller is missing.' }
    if (-not $js.Contains('openAdvancedCoverField')) { throw 'Cross-profile cover quick-edit routing is missing.' }

    if (-not $fingerprint.Contains('brochure-cover-review-v3')) { throw 'Cover review fingerprint was not advanced to v3.' }
    if (-not $fingerprint.Contains('context.ShowFrontCoverKicker')) { throw 'Front kicker visibility is not bound to Cover B approval.' }
    if (-not $fingerprint.Contains('context.ShowFrontCoverDescriptor')) { throw 'Front descriptor visibility is not bound to Cover B approval.' }

    if (-not $presetModel.Contains('SettingsSchemaVersion { get; set; } = 3')) { throw 'Preset model schema version is not 3.' }
    if (-not $presetService.Contains('CurrentSchemaVersion = 3')) { throw 'Preset service schema version is not 3.' }
    if (-not $migration.Contains('defaultValue: 3')) { throw 'Phase 21.1 migration does not advance preset schema to 3.' }

    $migrationId = '20261208113000_AddBrochureCoverVisibilityControls'
    $idMatches = ([regex]::Matches($immutable, [regex]::Escape($migrationId))).Count
    if ($idMatches -ne 1) { throw "Immutable migration manifest must contain $migrationId exactly once; found $idMatches." }

    if ($js.Contains('coverHeroFocalStage?.addEventListener("click", setCoverFocalFromEvent);' + [Environment]::NewLine + '    coverHeroFocalStage?.addEventListener("click", setCoverFocalFromEvent);')) {
        throw 'Duplicate Cover B focal-point click listener remains.'
    }

    if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
        throw 'Node.js is required for publication JavaScript validation.'
    }
    node --check '.\wwwroot\js\pages\projects-brochure.js'
    if ($LASTEXITCODE -ne 0) { throw 'node --check failed.' }
    node --test '.\wwwroot\js\projects\publications-brochure-contract.test.js'
    if ($LASTEXITCODE -ne 0) { throw 'publication contract tests failed.' }

    if (-not $SkipDotNet) {
        if (Get-Command dotnet -ErrorAction SilentlyContinue) {
            dotnet build '.\ProjectManagement.csproj'
            if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
            dotnet test '.\ProjectManagement.Tests\ProjectManagement.Tests.csproj'
            if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }
        }
        else {
            Write-Warning '.NET SDK not found. Run dotnet build/test on the development machine before deployment.'
        }
    }

    Write-Host 'Phase 21.1 validation completed successfully.' -ForegroundColor Green
}
finally {
    Pop-Location
}
