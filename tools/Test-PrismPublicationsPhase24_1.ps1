$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "PRISM Publications Phase 24.1 - Compendium publication freeze validation"
Write-Host "Project root: $root"

$required = @(
    "Pages/Projects/Publications/Compendium/Index.cshtml",
    "Pages/Projects/Publications/Compendium/Index.cshtml.cs",
    "Models/Publications/CompendiumPreset.cs",
    "Services/Compendiums/CompendiumDtos.cs",
    "Services/Compendiums/CompendiumReadinessPolicy.cs",
    "Services/Compendiums/CompendiumReadService.cs",
    "Services/Compendiums/CompendiumExportService.cs",
    "Services/Publications/CompendiumPresetContracts.cs",
    "Services/Publications/CompendiumPresetService.cs",
    "Utilities/Reporting/CompendiumLayoutMetrics.cs",
    "Utilities/Reporting/CompendiumPagePlanner.cs",
    "Utilities/Reporting/CompendiumPdfReportBuilder.cs",
    "Utilities/Reporting/CompendiumPdfCompositionVerifier.cs",
    "Utilities/Reporting/CompendiumPublicationTextSanitizer.cs",
    "Migrations/20261208150000_AddCompendiumCoverHeroControls.cs",
    "Migrations/ApplicationDbContextModelSnapshot.cs",
    "Migrations/immutable-migration-ids.txt",
    "wwwroot/js/pages/projects-compendium.js",
    "wwwroot/js/projects/publications-compendium-contract.test.js",
    "wwwroot/css/pages/projects-publications.css"
)

foreach ($path in $required) {
    if (-not (Test-Path (Join-Path $root $path))) {
        throw "Missing Phase 24.1 file: $path"
    }
}

node --check (Join-Path $root "wwwroot/js/pages/projects-compendium.js")
node --test (Join-Path $root "wwwroot/js/projects/publications-compendium-contract.test.js")

$migrationId = "20261208150000_AddCompendiumCoverHeroControls"
$immutable = Get-Content (Join-Path $root "Migrations/immutable-migration-ids.txt")
if ($immutable -notcontains $migrationId) {
    throw "Phase 24.1 migration id is not present in immutable-migration-ids.txt"
}

$migration = Get-Content (Join-Path $root "Migrations/20261208150000_AddCompendiumCoverHeroControls.cs") -Raw
if ($migration -notmatch '\[Migration\("20261208150000_AddCompendiumCoverHeroControls"\)\]' -or
    $migration -notmatch '\[DbContext\(typeof\(ApplicationDbContext\)\)\]') {
    throw "Phase 24.1 migration is missing EF Core discovery metadata."
}

$presetModel = Get-Content (Join-Path $root "Models/Publications/CompendiumPreset.cs") -Raw
if ($presetModel -notmatch 'SettingsSchemaVersion\s*\{[^}]*\}\s*=\s*3' -or
    $presetModel -notmatch 'CoverImageMode' -or
    $presetModel -notmatch 'CoverHeroProjectId' -or
    $presetModel -notmatch 'CoverHeroPhotoId' -or
    $presetModel -notmatch 'CoverFocalX' -or
    $presetModel -notmatch 'CoverFocalY') {
    throw "Saved Compendium v3 cover configuration is incomplete."
}

$readiness = Get-Content (Join-Path $root "Services/Compendiums/CompendiumReadinessPolicy.cs") -Raw
foreach ($obsolete in @('automaticImageSelected', 'proliferationNotAssessed', 'notAvailableForProliferation')) {
    if ($readiness -match [regex]::Escape($obsolete)) {
        throw "Routine readiness noise remains: $obsolete"
    }
}

$builder = Get-Content (Join-Path $root "Utilities/Reporting/CompendiumPdfReportBuilder.cs") -Raw
if ($builder -match 'ComposeCoverImageMosaic' -or $builder -notmatch 'ComposeCoverHero') {
    throw "Compendium cover has not moved to the single-hero publication contract."
}

$planner = Get-Content (Join-Path $root "Utilities/Reporting/CompendiumPagePlanner.cs") -Raw
if ($planner -notmatch 'PhotoLong' -or $planner -notmatch 'PhotoMedium' -or $planner -notmatch 'PhotoShort') {
    throw "Adaptive project-page geometry is incomplete."
}

$js = Get-Content (Join-Path $root "wwwroot/js/pages/projects-compendium.js") -Raw
if ($js -notmatch 'Review & next' -or $js -notmatch 'Finish review' -or $js -notmatch 'event\.ctrlKey && event\.key === "Enter"') {
    throw "Continuous Compendium review flow is incomplete."
}

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    dotnet build (Join-Path $root "ProjectManagement.csproj")

    $tests = Join-Path $root "ProjectManagement.Tests/ProjectManagement.Tests.csproj"
    if (Test-Path $tests) {
        dotnet test $tests
    }
} else {
    Write-Warning ".NET SDK not found; dotnet build/test skipped. Run this script again on the development workstation."
}

Write-Host "Phase 24.1 validation complete."
