$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "PRISM Publications Phase 32 - Adaptive Composition & Editorial Polish validation"
Write-Host "Project root: $root"

$required = @(
    "Services/Compendiums/CompendiumDossierLayoutPlanner.cs",
    "Services/Compendiums/CompendiumDossierPaginationPlanner.cs",
    "Services/Compendiums/CompendiumDtos.cs",
    "Services/Compendiums/CompendiumReadService.cs",
    "Services/Compendiums/CompendiumExportService.cs",
    "Services/Compendiums/CompendiumReviewFingerprint.cs",
    "Utilities/Reporting/CompendiumPagePlanner.cs",
    "Utilities/Reporting/CompendiumPdfReportBuilder.cs",
    "Pages/Projects/Publications/Compendium/Index.cshtml",
    "Pages/Projects/Publications/Compendium/Index.cshtml.cs",
    "Pages/Projects/Publications/Compendium/Cover.cshtml",
    "wwwroot/js/pages/projects-compendium.js",
    "wwwroot/js/pages/projects-compendium-cover-editor.js",
    "wwwroot/css/pages/projects-publications.css",
    "wwwroot/js/projects/publications-compendium-phase32-contract.test.js",
    "ProjectManagement.Tests/Publications/CompendiumPhase32CompositionTests.cs"
)

foreach ($path in $required) {
    if (-not (Test-Path (Join-Path $root $path))) {
        throw "Missing Phase 32 file: $path"
    }
}

node --check (Join-Path $root "wwwroot/js/pages/projects-compendium.js")
node --check (Join-Path $root "wwwroot/js/pages/projects-compendium-structure-editor.js")
node --check (Join-Path $root "wwwroot/js/pages/projects-compendium-cover-editor.js")

$contracts = @(
    "wwwroot/js/projects/publications-compendium-contract.test.js",
    "wwwroot/js/projects/publications-compendium-phase29-contract.test.js",
    "wwwroot/js/projects/publications-compendium-phase29-1-contract.test.js",
    "wwwroot/js/projects/publications-compendium-phase30-contract.test.js",
    "wwwroot/js/projects/publications-compendium-phase30-1-contract.test.js",
    "wwwroot/js/projects/publications-compendium-phase31-contract.test.js",
    "wwwroot/js/projects/publications-compendium-phase31-1-contract.test.js",
    "wwwroot/js/projects/publications-compendium-phase32-contract.test.js"
) | ForEach-Object { Join-Path $root $_ }
node --test $contracts

$readService = Get-Content (Join-Path $root "Services/Compendiums/CompendiumReadService.cs") -Raw
if ($readService -notmatch [regex]::Escape('CompendiumPdf_2026-08-15_adaptive-composition-v12')) {
    throw "Phase 32 build stamp is missing."
}

$fingerprint = Get-Content (Join-Path $root "Services/Compendiums/CompendiumReviewFingerprint.cs") -Raw
if ($fingerprint -notmatch [regex]::Escape('compendium-review-v7-adaptive-composition')) {
    throw "Phase 32 review fingerprint contract is missing."
}

$pagination = Get-Content (Join-Path $root "Services/Compendiums/CompendiumDossierPaginationPlanner.cs") -Raw
foreach ($contract in @(
    'ScoreCandidate',
    'ResidualSpacePoints',
    'MaximumImageHeight',
    'NarrativeFontScale',
    'VisualHero => 315f',
    'longest <= 78',
    'longest <= 175'
)) {
    if ($pagination -notmatch [regex]::Escape($contract)) {
        throw "Phase 32 composition contract is missing: $contract"
    }
}

$builder = Get-Content (Join-Path $root "Utilities/Reporting/CompendiumPdfReportBuilder.cs") -Raw
foreach ($contract in @(
    'DossierNarrativeFontScale',
    'FontSize(8.75f)',
    'labelLetterSpacing',
    '9.4f'
)) {
    if ($builder -notmatch [regex]::Escape($contract)) {
        throw "Phase 32 PDF contract is missing: $contract"
    }
}

$coverJs = Get-Content (Join-Path $root "wwwroot/js/pages/projects-compendium-cover-editor.js") -Raw
foreach ($contract in @('applyProofZoom', 'resetProofViewport', 'ResizeObserver', 'sheet.style.zoom')) {
    if ($coverJs -notmatch [regex]::Escape($contract)) {
        throw "Phase 32 cover proof contract is missing: $contract"
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

Write-Host "Phase 32 validation complete. No database migration is required."
