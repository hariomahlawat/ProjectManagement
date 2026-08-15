$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "PRISM Publications Phase 33 - Production Hardening validation"
Write-Host "Project root: $root"

$required = @(
    "Services/Compendiums/CompendiumDossierPaginationPlanner.cs",
    "Services/Compendiums/CompendiumReadService.cs",
    "Services/Compendiums/CompendiumReviewFingerprint.cs",
    "Utilities/Reporting/CompendiumPdfReportBuilder.cs",
    "Pages/Projects/Publications/Compendium/Cover.cshtml",
    "wwwroot/js/pages/projects-compendium.js",
    "wwwroot/js/pages/projects-compendium-cover-editor.js",
    "wwwroot/css/pages/projects-publications.css",
    "ProjectManagement.Tests/Compendiums/CompendiumPublicationTests.cs",
    "wwwroot/js/projects/publications-compendium-phase33-contract.test.js"
)

foreach ($path in $required) {
    if (-not (Test-Path (Join-Path $root $path))) {
        throw "Missing Phase 33 file: $path"
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
    "wwwroot/js/projects/publications-compendium-phase32-contract.test.js",
    "wwwroot/js/projects/publications-compendium-phase33-contract.test.js"
) | ForEach-Object { Join-Path $root $_ }
node --test $contracts

$planner = Get-Content (Join-Path $root "Services/Compendiums/CompendiumDossierPaginationPlanner.cs") -Raw
if ($planner -notmatch [regex]::Escape('ResolveIdealResidualSpace(specifications.Length, programmeModuleCount)')) {
    throw "The Phase 32 CS1503 pagination planner hotfix is not present."
}

$builder = Get-Content (Join-Path $root "Utilities/Reporting/CompendiumPdfReportBuilder.cs") -Raw
foreach ($contract in @(
    'IPublicationFontService _fontService',
    '_fontService.EnsureRegistered()',
    '.FontFamily(Volatile.Read(ref s_primaryFontFamily))',
    'LineHeight(1.08f)',
    'LetterSpacing(.14f)'
)) {
    if ($builder -notmatch [regex]::Escape($contract)) {
        throw "Phase 33 PDF typography contract is missing: $contract"
    }
}

$coverJs = Get-Content (Join-Path $root "wwwroot/js/pages/projects-compendium-cover-editor.js") -Raw
foreach ($contract in @(
    'portalBy',
    'pinResolvedAutomaticSlot',
    "slot.imageMode === 'Automatic'",
    'data-cover-project-select',
    'data-cover-crop-image'
)) {
    if ($coverJs -notmatch [regex]::Escape($contract)) {
        throw "Phase 33 cover interaction contract is missing: $contract"
    }
}

$readService = Get-Content (Join-Path $root "Services/Compendiums/CompendiumReadService.cs") -Raw
if ($readService -notmatch [regex]::Escape('CompendiumPdf_2026-08-15_production-hardening-v13')) {
    throw "Phase 33 PDF build stamp is missing."
}

$fingerprint = Get-Content (Join-Path $root "Services/Compendiums/CompendiumReviewFingerprint.cs") -Raw
if ($fingerprint -notmatch [regex]::Escape('compendium-review-v8-production-hardening')) {
    throw "Phase 33 review fingerprint identity is missing."
}

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    dotnet build (Join-Path $root "ProjectManagement.csproj")

    $tests = Join-Path $root "ProjectManagement.Tests/ProjectManagement.Tests.csproj"
    if (Test-Path $tests) {
        dotnet test $tests
    }
} else {
    Write-Warning ".NET SDK not found; dotnet build/test skipped. Run this script on the development workstation."
}

Write-Host "Phase 33 validation complete. No database migration is required."
