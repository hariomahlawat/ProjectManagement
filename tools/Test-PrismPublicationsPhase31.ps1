$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "PRISM Publications Phase 31 - Adaptive Project Dossier Composer validation"
Write-Host "Project root: $root"

$required = @(
    "Migrations/20261208190000_AddCompendiumAdaptiveDossiers.cs",
    "Models/ProjectTechnicalSpecificationItem.cs",
    "Services/Compendiums/CompendiumDossierLayoutPlanner.cs",
    "Services/Compendiums/CompendiumReadService.cs",
    "Services/Compendiums/CompendiumExportService.cs",
    "Utilities/Reporting/CompendiumPagePlanner.cs",
    "Utilities/Reporting/CompendiumPdfReportBuilder.cs",
    "Pages/Projects/_ProjectContentTabs.cshtml",
    "Pages/Projects/Publications/Compendium/Index.cshtml",
    "wwwroot/js/pages/projects-compendium.js",
    "wwwroot/js/projects/project-content.js",
    "wwwroot/css/pages/projects-publications.css",
    "wwwroot/js/projects/publications-compendium-phase31-contract.test.js"
)

foreach ($path in $required) {
    if (-not (Test-Path (Join-Path $root $path))) {
        throw "Missing Phase 31 file: $path"
    }
}

node --check (Join-Path $root "wwwroot/js/pages/projects-compendium.js")
node --check (Join-Path $root "wwwroot/js/pages/projects-compendium-structure-editor.js")
node --check (Join-Path $root "wwwroot/js/pages/projects-compendium-cover-editor.js")
node --check (Join-Path $root "wwwroot/js/projects/project-content.js")

node --test `
    (Join-Path $root "wwwroot/js/projects/publications-compendium-contract.test.js") `
    (Join-Path $root "wwwroot/js/projects/publications-compendium-phase29-contract.test.js") `
    (Join-Path $root "wwwroot/js/projects/publications-compendium-phase29-1-contract.test.js") `
    (Join-Path $root "wwwroot/js/projects/publications-compendium-phase30-contract.test.js") `
    (Join-Path $root "wwwroot/js/projects/publications-compendium-phase30-1-contract.test.js") `
    (Join-Path $root "wwwroot/js/projects/publications-compendium-phase31-contract.test.js")

$readService = Get-Content (Join-Path $root "Services/Compendiums/CompendiumReadService.cs") -Raw
if ($readService -notmatch [regex]::Escape('CompendiumPdf_2026-08-14_adaptive-dossier-v10')) {
    throw "Phase 31 build stamp is missing."
}

$presetService = Get-Content (Join-Path $root "Services/Publications/CompendiumPresetService.cs") -Raw
if ($presetService -notmatch 'CurrentSchemaVersion\s*=\s*7') {
    throw "Phase 31 Compendium preset schema v7 is missing."
}

$builder = Get-Content (Join-Path $root "Utilities/Reporting/CompendiumPdfReportBuilder.cs") -Raw
foreach ($contract in @(
    'ComposeAdaptiveDossierMain',
    'ComposeProgrammeInformation',
    'ComposeTechnicalSpecifications',
    'TECHNICAL REFERENCE'
)) {
    if ($builder -notmatch [regex]::Escape($contract)) {
        throw "Phase 31 PDF dossier contract is missing: $contract"
    }
}

$view = Get-Content (Join-Path $root "Pages/Projects/Publications/Compendium/Index.cshtml") -Raw
foreach ($contract in @(
    'data-review-layout="Automatic"',
    'data-review-layout="VisualHero"',
    'data-review-layout="Balanced"',
    'data-review-layout="MultiImageEditorial"',
    'data-review-layout="Technical"',
    'data-photo-role="Supporting1"',
    'data-photo-role="Supporting2"',
    'HARDWARE / TECHNICAL SPECIFICATION'
)) {
    if ($view -notmatch [regex]::Escape($contract)) {
        throw "Phase 31 review composer contract is missing: $contract"
    }
}

$migration = Get-Content (Join-Path $root "Migrations/20261208190000_AddCompendiumAdaptiveDossiers.cs") -Raw
if ($migration -notmatch 'ProjectTechnicalSpecificationItems' -or $migration -notmatch 'DossierLayout') {
    throw "Phase 31 migration does not contain the dossier/specification schema."
}

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    dotnet build (Join-Path $root "ProjectManagement.csproj")

    $tests = Join-Path $root "ProjectManagement.Tests/ProjectManagement.Tests.csproj"
    if (Test-Path $tests) {
        dotnet test $tests --filter "FullyQualifiedName~Compendium|FullyQualifiedName~ProjectContent"
    }
} else {
    Write-Warning ".NET SDK not found; dotnet build/test skipped. Run this script again on the development workstation."
}

Write-Host "Phase 31 validation complete. Apply migration 20261208190000_AddCompendiumAdaptiveDossiers before UI/PDF verification."
