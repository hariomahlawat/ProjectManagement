$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "PRISM Publications Phase 28 - Proof-first review workspace validation"
Write-Host "Project root: $root"

$required = @(
    "Pages/Projects/Publications/Compendium/Index.cshtml",
    "Services/Compendiums/CompendiumReadService.cs",
    "ProjectManagement.Tests/Publications/CompendiumPhase28ContractTests.cs",
    "wwwroot/js/pages/projects-compendium.js",
    "wwwroot/js/projects/publications-compendium-contract.test.js",
    "wwwroot/css/pages/projects-publications.css"
)

foreach ($path in $required) {
    if (-not (Test-Path (Join-Path $root $path))) {
        throw "Missing Phase 28 file: $path"
    }
}

node --check (Join-Path $root "wwwroot/js/pages/projects-compendium.js")
node --test (Join-Path $root "wwwroot/js/projects/publications-compendium-contract.test.js")

$razor = Get-Content (Join-Path $root "Pages/Projects/Publications/Compendium/Index.cshtml") -Raw
foreach ($contract in @(
    'data-review-focus-toggle',
    'data-live-page-zoom="fit"',
    'data-live-page-zoom="75"',
    'data-live-page-zoom="100"',
    'compendium-review-image-summary',
    'data-structure-collapse-all',
    'data-structure-expand-all',
    'data-output-dock',
    'data-output-dock-preview',
    'data-output-dock-generate'
)) {
    if ($razor -notmatch [regex]::Escape($contract)) {
        throw "Phase 28 review workspace UI is missing: $contract"
    }
}

$js = Get-Content (Join-Path $root "wwwroot/js/pages/projects-compendium.js") -Raw
foreach ($contract in @(
    'applyReviewFocusMode',
    'applyLivePreviewZoom',
    'collapsedGroupKeys',
    'setupOutputDockObserver',
    'activeFindingQueue',
    'nextFindingQueueId',
    'Review affected projects'
)) {
    if ($js -notmatch [regex]::Escape($contract)) {
        throw "Phase 28 client workflow is missing: $contract"
    }
}

$css = Get-Content (Join-Path $root "wwwroot/css/pages/projects-publications.css") -Raw
foreach ($contract in @(
    '.compendium-builder-page.is-review-focus',
    '.compendium-live-page__viewport[data-preview-zoom="100"]',
    '.compendium-review-image-summary',
    '.compendium-output-dock',
    '.compendium-finding-group__queue'
)) {
    if ($css -notmatch [regex]::Escape($contract)) {
        throw "Phase 28 presentation contract is missing: $contract"
    }
}

$readService = Get-Content (Join-Path $root "Services/Compendiums/CompendiumReadService.cs") -Raw
if ($readService -notmatch [regex]::Escape('CompendiumPdf_2026-08-14_publication-review-v7')) {
    throw "Phase 28 build stamp is missing."
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

Write-Host "Phase 28 validation complete."
