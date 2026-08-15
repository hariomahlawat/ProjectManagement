$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "PRISM Publications Phase 31.1 - Adaptive Pagination & Authoring Fidelity validation"
Write-Host "Project root: $root"

$required = @(
    "Services/Compendiums/CompendiumDossierLayoutPlanner.cs",
    "Services/Compendiums/CompendiumDossierPaginationPlanner.cs",
    "Services/Compendiums/CompendiumDtos.cs",
    "Services/Compendiums/CompendiumReadService.cs",
    "Services/Compendiums/CompendiumExportService.cs",
    "Utilities/Reporting/CompendiumPagePlanner.cs",
    "Utilities/Reporting/CompendiumPdfReportBuilder.cs",
    "Pages/Projects/Publications/Compendium/Index.cshtml",
    "Pages/Projects/Publications/Compendium/Index.cshtml.cs",
    "wwwroot/js/pages/projects-compendium.js",
    "wwwroot/css/pages/projects-publications.css",
    "wwwroot/js/projects/publications-compendium-phase31-contract.test.js",
    "wwwroot/js/projects/publications-compendium-phase31-1-contract.test.js",
    "ProjectManagement.Tests/Publications/CompendiumPhase31_1PaginationTests.cs"
)

foreach ($path in $required) {
    if (-not (Test-Path (Join-Path $root $path))) {
        throw "Missing Phase 31.1 file: $path"
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
    (Join-Path $root "wwwroot/js/projects/publications-compendium-phase30-1-contract.test.js") `
    (Join-Path $root "wwwroot/js/projects/publications-compendium-phase31-contract.test.js") `
    (Join-Path $root "wwwroot/js/projects/publications-compendium-phase31-1-contract.test.js")

$readService = Get-Content (Join-Path $root "Services/Compendiums/CompendiumReadService.cs") -Raw
if ($readService -notmatch [regex]::Escape('CompendiumPdf_2026-08-14_adaptive-pagination-v11')) {
    throw "Phase 31.1 build stamp is missing."
}

$pagePlanner = Get-Content (Join-Path $root "Utilities/Reporting/CompendiumPagePlanner.cs") -Raw
if ($pagePlanner -match 'ResolveDossierNarrativeBudget') {
    throw "Legacy fixed dossier narrative budgeting remains in CompendiumPagePlanner."
}
if ($pagePlanner -notmatch 'DossierFirstPageNarrativeBudget') {
    throw "Shared adaptive pagination budget is not wired into CompendiumPagePlanner."
}

$builder = Get-Content (Join-Path $root "Utilities/Reporting/CompendiumPdfReportBuilder.cs") -Raw
foreach ($contract in @(
    'DossierPrimaryImageHeightPoints',
    'BuildIprProgrammeValue',
    'ResolveTechnicalSpecificationColumns',
    'CONTINUED · PART'
)) {
    if ($builder -notmatch [regex]::Escape($contract)) {
        throw "Phase 31.1 PDF contract is missing: $contract"
    }
}

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    dotnet build (Join-Path $root "ProjectManagement.csproj")

    $tests = Join-Path $root "ProjectManagement.Tests/ProjectManagement.Tests.csproj"
    if (Test-Path $tests) {
        dotnet test $tests --filter "FullyQualifiedName~Compendium"
    }
} else {
    Write-Warning ".NET SDK not found; dotnet build/test skipped. Run this script again on the development workstation."
}

Write-Host "Phase 31.1 validation complete. No database migration is required for this hardening phase."
