$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "PRISM Publications Phase 30.1 - Cover Composer Fidelity & UX Polish validation"
Write-Host "Project root: $root"

$required = @(
    "Pages/Projects/Publications/Compendium/Cover.cshtml",
    "Pages/Projects/Publications/Compendium/Index.cshtml",
    "Pages/Projects/Publications/Compendium/Index.cshtml.cs",
    "Services/Compendiums/CompendiumExportService.cs",
    "Services/Compendiums/CompendiumReadService.cs",
    "Utilities/Reporting/CompendiumPdfReportBuilder.cs",
    "wwwroot/css/pages/projects-publications.css",
    "wwwroot/js/pages/projects-compendium.js",
    "wwwroot/js/pages/projects-compendium-cover-editor.js",
    "wwwroot/js/projects/publications-compendium-phase30-contract.test.js",
    "wwwroot/js/projects/publications-compendium-phase30-1-contract.test.js"
)

foreach ($path in $required) {
    if (-not (Test-Path (Join-Path $root $path))) {
        throw "Missing Phase 30.1 file: $path"
    }
}

node --check (Join-Path $root "wwwroot/js/pages/projects-compendium.js")
node --check (Join-Path $root "wwwroot/js/pages/projects-compendium-structure-editor.js")
node --check (Join-Path $root "wwwroot/js/pages/projects-compendium-cover-editor.js")

node --test `
    (Join-Path $root "wwwroot/js/projects/publications-compendium-contract.test.js") `
    (Join-Path $root "wwwroot/js/projects/publications-compendium-phase29-contract.test.js") `
    (Join-Path $root "wwwroot/js/projects/publications-compendium-phase29-1-contract.test.js") `
    (Join-Path $root "wwwroot/js/projects/publications-compendium-phase30-contract.test.js") `
    (Join-Path $root "wwwroot/js/projects/publications-compendium-phase30-1-contract.test.js")

$readService = Get-Content (Join-Path $root "Services/Compendiums/CompendiumReadService.cs") -Raw
if ($readService -notmatch [regex]::Escape('CompendiumPdf_2026-08-14_cover-fidelity-v9')) {
    throw "Phase 30.1 build stamp is missing."
}

$coverView = Get-Content (Join-Path $root "Pages/Projects/Publications/Compendium/Cover.cshtml") -Raw
foreach ($contract in @(
    'data-cover-inherited-value',
    'data-cover-override',
    'data-cover-reset',
    'Formation mark',
    'SDD mark'
)) {
    if ($coverView -notmatch [regex]::Escape($contract)) {
        throw "Phase 30.1 cover UX contract is missing: $contract"
    }
}

$coverJs = Get-Content (Join-Path $root "wwwroot/js/pages/projects-compendium-cover-editor.js") -Raw
foreach ($contract in @(
    'usedProjects',
    'usedPhotos',
    'overrideEditing',
    'clearAutomaticResolutions'
)) {
    if ($coverJs -notmatch [regex]::Escape($contract)) {
        throw "Phase 30.1 cover editor behavior contract is missing: $contract"
    }
}

$css = Get-Content (Join-Path $root "wwwroot/css/pages/projects-publications.css") -Raw
if ($css -notmatch 'width:\s*595px' -or $css -notmatch 'height:\s*842px') {
    throw "Phase 30.1 authoritative A4 cover proof geometry is missing."
}

$builder = Get-Content (Join-Path $root "Utilities/Reporting/CompendiumPdfReportBuilder.cs") -Raw
if ($builder -notmatch '44' -or $builder -notmatch '48') {
    throw "Phase 30.1 optical mark sizing contract could not be verified."
}

$migration = Join-Path $root "Migrations/20261208180000_AddCompendiumCoverComposer.cs"
if (-not (Test-Path $migration)) {
    throw "The Phase 30 cover-composer migration is missing. Phase 30.1 itself adds no new database migration."
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

Write-Host "Phase 30.1 validation complete. No new database migration is required for this phase."
