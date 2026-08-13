param(
    [switch]$SkipDotNet
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $root
try {
    Write-Host 'PRISM Publications Phase 21.2 - Print Cover Compliance & Final Freeze' -ForegroundColor Cyan
    Write-Host "Project root: $root"

    $required = @(
        'Models/Publications/BrochurePreset.cs',
        'Services/Publications/BrochureContracts.cs',
        'Services/Publications/BrochurePresetContracts.cs',
        'Services/Publications/BrochurePresetService.cs',
        'Services/Publications/BrochurePrintPublicationPolicy.cs',
        'Services/Publications/BrochurePrintLayoutMetrics.cs',
        'Services/Publications/BrochurePrintMeasurementService.cs',
        'Utilities/Reporting/BrochurePrintCompactComposer.cs',
        'Pages/Projects/Publications/Brochure/Index.cshtml',
        'Pages/Projects/Publications/Brochure/Index.cshtml.cs',
        'Data/ApplicationDbContext.cs',
        'Migrations/20261208113000_AddBrochureCoverVisibilityControls.cs',
        'Migrations/20261208120000_AddBrochureInstitutionalSectionLabels.cs',
        'Migrations/ApplicationDbContextModelSnapshot.cs',
        'Migrations/immutable-migration-ids.txt',
        'wwwroot/css/pages/projects-publications.css',
        'wwwroot/js/pages/projects-brochure.js',
        'wwwroot/js/projects/publications-brochure-contract.test.js',
        'ProjectManagement.Tests/Publications/BrochurePresetServiceTests.cs',
        'ProjectManagement.Tests/Publications/BrochurePrintMeasurementServiceTests.cs'
    )
    foreach ($path in $required) {
        if (-not (Test-Path $path)) { throw "Missing required Phase 21.2 file/dependency: $path" }
    }

    $view = Get-Content 'Pages/Projects/Publications/Brochure/Index.cshtml' -Raw
    $pageModel = Get-Content 'Pages/Projects/Publications/Brochure/Index.cshtml.cs' -Raw
    $contracts = Get-Content 'Services/Publications/BrochureContracts.cs' -Raw
    $presetContracts = Get-Content 'Services/Publications/BrochurePresetContracts.cs' -Raw
    $presetModel = Get-Content 'Models/Publications/BrochurePreset.cs' -Raw
    $presetService = Get-Content 'Services/Publications/BrochurePresetService.cs' -Raw
    $policy = Get-Content 'Services/Publications/BrochurePrintPublicationPolicy.cs' -Raw
    $metrics = Get-Content 'Services/Publications/BrochurePrintLayoutMetrics.cs' -Raw
    $measurement = Get-Content 'Services/Publications/BrochurePrintMeasurementService.cs' -Raw
    $renderer = Get-Content 'Utilities/Reporting/BrochurePrintCompactComposer.cs' -Raw
    $js = Get-Content 'wwwroot/js/pages/projects-brochure.js' -Raw
    $migration = Get-Content 'Migrations/20261208120000_AddBrochureInstitutionalSectionLabels.cs' -Raw
    $immutable = Get-Content 'Migrations/immutable-migration-ids.txt' -Raw

    $labelFields = @(
        'PrintProcurementHeading',
        'PrintContactsHeading',
        'PrintDevelopingAgencyHeading',
        'PrintManufacturingAgencyHeading',
        'PrintVisionaryHeading',
        'PrintNewSimulatorsHeading'
    )
    foreach ($field in $labelFields) {
        foreach ($contract in @($view, $pageModel, $contracts, $presetContracts, $presetModel, $presetService, $policy, $renderer, $js, $migration)) {
            if (-not $contract.Contains($field)) { throw "Phase 21.2 editable-label contract is incomplete for: $field" }
        }
    }

    if (-not $view.Contains('Leave a field blank to suppress that label')) { throw 'Print label suppression guidance is missing.' }
    if (-not $metrics.Contains('FrontContemporaryHeroHeightPoints')) { throw 'Cover B physical hero-height contract is missing.' }
    if (-not $metrics.Contains('FrontContemporaryHeroRasterHeight = 1055f')) { throw 'Cover B 1800 x 1055 crop geometry is not bound to the physical frame.' }
    if (-not $measurement.Contains('FrontContemporaryHeroHeightPoints')) { throw 'Cover B front-page measurement does not use the physical crop-matched height.' }

    $forbiddenRendererLiterals = @(
        'text.Span("Procurement: ")',
        '.Text("CONTACTS")',
        '.Text("Developing Agency")',
        '.Text("Manufacturing Agency")',
        '.Text("Visionary Horizons & Strategic Objectives")',
        'text.Span("New Simulators. ")'
    )
    foreach ($literal in $forbiddenRendererLiterals) {
        if ($renderer.Contains($literal)) { throw "A fixed Print Compact publication label remains in the renderer: $literal" }
    }

    if (-not $presetModel.Contains('SettingsSchemaVersion { get; set; } = 4')) { throw 'Preset model schema version is not 4.' }
    if (-not $presetService.Contains('CurrentSchemaVersion = 4')) { throw 'Preset service schema version is not 4.' }
    if (-not $migration.Contains('defaultValue: 4')) { throw 'Phase 21.2 migration does not advance preset schema to 4.' }

    $migrationId = '20261208120000_AddBrochureInstitutionalSectionLabels'
    $idMatches = ([regex]::Matches($immutable, [regex]::Escape($migrationId))).Count
    if ($idMatches -ne 1) { throw "Immutable migration manifest must contain $migrationId exactly once; found $idMatches." }

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

    Write-Host 'Phase 21.2 validation completed successfully.' -ForegroundColor Green
}
finally {
    Pop-Location
}
