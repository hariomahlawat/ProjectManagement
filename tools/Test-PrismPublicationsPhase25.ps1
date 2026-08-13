$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "PRISM Publications Phase 25 - Compendium editorial composer validation"
Write-Host "Project root: $root"

$required = @(
    "Pages/Projects/Publications/Compendium/Index.cshtml",
    "Pages/Projects/Publications/Compendium/Index.cshtml.cs",
    "Models/Publications/CompendiumPreset.cs",
    "Services/Compendiums/CompendiumDtos.cs",
    "Services/Compendiums/CompendiumReadService.cs",
    "Services/Compendiums/CompendiumReadinessPolicy.cs",
    "Services/Compendiums/CompendiumReviewFingerprint.cs",
    "Services/Compendiums/CompendiumExportService.cs",
    "Services/Publications/CompendiumPresetContracts.cs",
    "Services/Publications/CompendiumPresetService.cs",
    "Utilities/Reporting/CompendiumPdfReportBuilder.cs",
    "Migrations/20261208160000_AddCompendiumEditorialComposer.cs",
    "Migrations/ApplicationDbContextModelSnapshot.cs",
    "Migrations/immutable-migration-ids.txt",
    "wwwroot/js/pages/projects-compendium.js",
    "wwwroot/js/projects/publications-compendium-contract.test.js",
    "wwwroot/css/pages/projects-publications.css"
)

foreach ($path in $required) {
    if (-not (Test-Path (Join-Path $root $path))) {
        throw "Missing Phase 25 file: $path"
    }
}

node --check (Join-Path $root "wwwroot/js/pages/projects-compendium.js")
node --test (Join-Path $root "wwwroot/js/projects/publications-compendium-contract.test.js")

$migrationId = "20261208160000_AddCompendiumEditorialComposer"
$immutable = Get-Content (Join-Path $root "Migrations/immutable-migration-ids.txt")
if ($immutable -notcontains $migrationId) {
    throw "Phase 25 migration id is not present in immutable-migration-ids.txt"
}

$migration = Get-Content (Join-Path $root "Migrations/20261208160000_AddCompendiumEditorialComposer.cs") -Raw
if ($migration -notmatch '\[Migration\("20261208160000_AddCompendiumEditorialComposer"\)\]' -or
    $migration -notmatch 'NarrativeSource' -or
    $migration -notmatch 'GroupingMode' -or
    $migration -notmatch 'SortMode' -or
    $migration -notmatch 'CustomSectionName') {
    throw "Phase 25 migration is incomplete."
}

$dtos = Get-Content (Join-Path $root "Services/Compendiums/CompendiumDtos.cs") -Raw
foreach ($contract in @('CompendiumNarrativeSource', 'ProjectBrief', 'CapabilityOverview', 'ProjectDescription', 'CompendiumGroupingMode', 'CustomSections', 'CompendiumSortMode', 'LatestFirst', 'Alphabetical')) {
    if ($dtos -notmatch [regex]::Escape($contract)) {
        throw "Phase 25 publication contract is missing: $contract"
    }
}

$readService = Get-Content (Join-Path $root "Services/Compendiums/CompendiumReadService.cs") -Raw
if ($readService -notmatch 'LoadCapabilityStatementsAsync' -or
    $readService -notmatch 'ResolveNarrative' -or
    $readService -notmatch 'ApplySortMode' -or
    $readService -notmatch 'GroupInPublicationOrder') {
    throw "Phase 25 read-service editorial pipeline is incomplete."
}

$builder = Get-Content (Join-Path $root "Utilities/Reporting/CompendiumPdfReportBuilder.cs") -Raw
if ($builder -notmatch 'CAPABILITY DOSSIER' -or
    $builder -notmatch 'NarrativeLabel' -or
    $builder -notmatch 'TechnicalCategoryDisplay') {
    throw "Phase 25 capability-dossier PDF treatment is incomplete."
}

$razor = Get-Content (Join-Path $root "Pages/Projects/Publications/Compendium/Index.cshtml") -Raw
foreach ($control in @('data-narrative-value', 'data-grouping-value', 'data-sort-value', 'data-cover-choose', 'data-cover-hero-picker')) {
    if ($razor -notmatch [regex]::Escape($control)) {
        throw "Phase 25 editorial UI control is missing: $control"
    }
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

Write-Host "Phase 25 validation complete."
