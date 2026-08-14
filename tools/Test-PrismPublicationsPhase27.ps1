$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "PRISM Publications Phase 27 - Publication polish and wide-monitor authoring validation"
Write-Host "Project root: $root"

$required = @(
    "Pages/Projects/Publications/Compendium/Index.cshtml",
    "Pages/Projects/Publications/Compendium/Index.cshtml.cs",
    "Pages/Projects/Publications/Brochure/Index.cshtml",
    "Models/Publications/CompendiumPreset.cs",
    "Data/ApplicationDbContext.cs",
    "Services/Compendiums/CompendiumDtos.cs",
    "Services/Compendiums/CompendiumReadService.cs",
    "Services/Compendiums/CompendiumReadinessPolicy.cs",
    "Services/Compendiums/CompendiumReviewFingerprint.cs",
    "Services/Compendiums/CompendiumExportService.cs",
    "Services/Publications/CompendiumPresetContracts.cs",
    "Services/Publications/CompendiumPresetService.cs",
    "Utilities/Reporting/CompendiumPdfReportBuilder.cs",
    "Migrations/20261208170000_AddCompendiumFirstClassSections.cs",
    "Migrations/ApplicationDbContextModelSnapshot.cs",
    "Migrations/immutable-migration-ids.txt",
    "ProjectManagement.Tests/Publications/CompendiumPhase26ContractTests.cs",
    "ProjectManagement.Tests/Publications/CompendiumPhase27ContractTests.cs",
    "wwwroot/js/pages/projects-compendium.js",
    "wwwroot/js/projects/publications-compendium-contract.test.js",
    "wwwroot/css/pages/projects-publications.css"
)

foreach ($path in $required) {
    if (-not (Test-Path (Join-Path $root $path))) {
        throw "Missing Phase 27 file: $path"
    }
}

node --check (Join-Path $root "wwwroot/js/pages/projects-compendium.js")
node --test (Join-Path $root "wwwroot/js/projects/publications-compendium-contract.test.js")

$migrationId = "20261208170000_AddCompendiumFirstClassSections"
$immutable = Get-Content (Join-Path $root "Migrations/immutable-migration-ids.txt")
if ($immutable -notcontains $migrationId) {
    throw "Phase 26 migration id is not present in immutable-migration-ids.txt"
}

$migration = Get-Content (Join-Path $root "Migrations/20261208170000_AddCompendiumFirstClassSections.cs") -Raw
foreach ($contract in @('CompendiumPresetSections', 'CustomSectionId', 'NarrativeSourceOverride', 'SettingsSchemaVersion')) {
    if ($migration -notmatch [regex]::Escape($contract)) {
        throw "Phase 26 migration is missing: $contract"
    }
}

$model = Get-Content (Join-Path $root "Models/Publications/CompendiumPreset.cs") -Raw
foreach ($contract in @('SettingsSchemaVersion { get; set; } = 5', 'CompendiumPresetSection', 'SectionKey', 'NarrativeSourceOverride', 'CustomSectionId')) {
    if ($model -notmatch [regex]::Escape($contract)) {
        throw "Phase 26 persistence model is missing: $contract"
    }
}

$readService = Get-Content (Join-Path $root "Services/Compendiums/CompendiumReadService.cs") -Raw
foreach ($contract in @('BuildPublicationStructure', 'SortProjects', 'NormalizeSections', 'ResolveSectionAssignment', 'NarrativeSourceOverride')) {
    if ($readService -notmatch [regex]::Escape($contract)) {
        throw "Phase 26 read-service pipeline is missing: $contract"
    }
}

$fingerprint = Get-Content (Join-Path $root "Services/Compendiums/CompendiumReviewFingerprint.cs") -Raw
foreach ($contract in @('compendium-review-v3', 'PublicationSectionKey', 'PublicationSectionName')) {
    if ($fingerprint -notmatch [regex]::Escape($contract)) {
        throw "Phase 26 review fingerprint is missing: $contract"
    }
}

$builder = Get-Content (Join-Path $root "Utilities/Reporting/CompendiumPdfReportBuilder.cs") -Raw
if ($builder -notmatch 'CAPABILITY DOSSIER' -or
    $builder -match 'Publication image not selected' -or
    $builder -notmatch 'showGroupHeadings') {
    throw "Phase 26 capability-dossier PDF treatment is incomplete."
}

$razor = Get-Content (Join-Path $root "Pages/Projects/Publications/Compendium/Index.cshtml") -Raw
foreach ($control in @('ViewData["UseFullWidth"] = true', 'data-custom-sections', 'data-custom-section-add', 'compendiumSectionDeleteModal')) {
    if ($razor -notmatch [regex]::Escape($control)) {
        throw "Phase 26 publication workspace UI is missing: $control"
    }
}

$brochure = Get-Content (Join-Path $root "Pages/Projects/Publications/Brochure/Index.cshtml") -Raw
if ($brochure -notmatch [regex]::Escape('ViewData["UseFullWidth"] = true')) {
    throw "Brochure is not opted into the full-width Publications workspace."
}

$phase27Css = Get-Content (Join-Path $root "wwwroot/css/pages/projects-publications.css") -Raw
foreach ($contract in @('grid-template-rows: minmax(0, 1fr) auto', 'compendium-live-page__sheet', 'min-width: 1180px')) {
    if ($phase27Css -notmatch [regex]::Escape($contract)) {
        throw "Phase 27 workspace polish is missing: $contract"
    }
}

$phase27Builder = Get-Content (Join-Path $root "Utilities/Reporting/CompendiumPdfReportBuilder.cs") -Raw
foreach ($contract in @('BuildMetadataRows', 'publicationTitle.ToUpperInvariant()', 'ComposeNoPhotoTreatment')) {
    if ($phase27Builder -notmatch [regex]::Escape($contract)) {
        throw "Phase 27 PDF polish is missing: $contract"
    }
}

$phase27Read = Get-Content (Join-Path $root "Services/Compendiums/CompendiumReadService.cs") -Raw
foreach ($contract in @('TechnicalCategorySortOrder', 'ResolvePublicationYear', 'publication-workspace-v6')) {
    if ($phase27Read -notmatch [regex]::Escape($contract)) {
        throw "Phase 27 publication ordering contract is missing: $contract"
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

Write-Host "Phase 27 validation complete."
