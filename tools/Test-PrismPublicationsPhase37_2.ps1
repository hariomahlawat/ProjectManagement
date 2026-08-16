param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($ProjectRoot)

Write-Host "PRISM Publications Phase 37.2 - editorial constraint hardening validation" -ForegroundColor Cyan
Write-Host "Project root: $root"
Write-Host ""

$required = @(
    "Services/Compendiums/CompendiumDossierEditorialPolicy.cs",
    "Services/Compendiums/CompendiumDossierNarrativeFlowPlanner.cs",
    "Services/Compendiums/CompendiumDossierPaginationPlanner.cs",
    "Services/Compendiums/CompendiumReadinessPolicy.cs",
    "Services/Compendiums/CompendiumReadService.cs",
    "Services/Compendiums/CompendiumReviewFingerprint.cs",
    "Pages/Projects/Publications/Compendium/Index.cshtml.cs",
    "wwwroot/css/pages/projects-publications.css",
    "wwwroot/js/pages/projects-compendium.js",
    "wwwroot/js/projects/publications-compendium-phase37-2-contract.test.js",
    "ProjectManagement.Tests/Publications/CompendiumPhase37_2CompositionTests.cs"
)

foreach ($path in $required) {
    if (-not (Test-Path (Join-Path $root $path))) {
        throw "Missing Phase 37.2 file: $path"
    }
}

function Require-Text([string]$RelativePath, [string]$Pattern, [string]$Message) {
    $source = Get-Content (Join-Path $root $RelativePath) -Raw
    if ($source -notmatch $Pattern) { throw "$Message ($RelativePath)" }
}

function Forbid-Text([string]$RelativePath, [string]$Pattern, [string]$Message) {
    $source = Get-Content (Join-Path $root $RelativePath) -Raw
    if ($source -match $Pattern) { throw "$Message ($RelativePath)" }
}

Require-Text "Services/Compendiums/CompendiumReviewFingerprint.cs" 'compendium-review-v14-editorial-constraints' "Review fingerprint is not Phase 37.2"
Require-Text "Services/Compendiums/CompendiumReadService.cs" 'CompendiumPdf_2026-08-16_editorial-constraints-v21' "PDF build identity is not Phase 37.2"
Require-Text "Services/Compendiums/CompendiumDossierEditorialPolicy.cs" 'MinimumEditorialFillHeightPoints' "Editorial Fill floor policy is missing"
Require-Text "Services/Compendiums/CompendiumDossierEditorialPolicy.cs" 'AssessSideColumn' "Side-column balance policy is missing"
Require-Text "Services/Compendiums/CompendiumDossierPaginationPlanner.cs" 'editorialCandidates' "Physical/editorial candidate separation is missing"
Require-Text "Services/Compendiums/CompendiumDossierPaginationPlanner.cs" 'protectPrintFidelity:\s*!explicitLayout' "Manual layout DPI semantics are not Phase 37.2"
Require-Text "Services/Compendiums/CompendiumDossierPaginationPlanner.cs" 'SideOverflowHeightPoints' "Side-column overflow measurement is missing"
Forbid-Text "Services/Compendiums/CompendiumDossierPaginationPlanner.cs" 'CompendiumDossierLayout\.Balanced[^\r\n]*96f' "Balanced normal candidates still include token-height Fill imagery"
Require-Text "Services/Compendiums/CompendiumReadinessPolicy.cs" 'dossierCompositionImbalance' "Editorial composition preflight warning is missing"
Require-Text "Services/Compendiums/CompendiumReadinessPolicy.cs" 'ContainsRepeatedLongWordBlock' "Duplicate narrative hardening is missing"
Require-Text "wwwroot/js/pages/projects-compendium.js" 'Layout needs editorial attention' "Review workspace does not surface editorial layout warnings"
Require-Text "wwwroot/css/pages/projects-publications.css" 'Compendium Phase 37\.2 — editorial constraint hardening' "Phase 37.2 UI CSS is missing"
Require-Text "wwwroot/css/pages/projects-publications.css" 'grid-template-columns:\s*1\.12fr 1fr \.9fr \.9fr' "Desktop publication composer is not four-column"

if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    throw "Node.js is required for Compendium JavaScript syntax and contract validation."
}

$syntaxFiles = @(
    "wwwroot/js/pages/projects-compendium.js",
    "wwwroot/js/pages/projects-compendium-cover-editor.js",
    "wwwroot/js/pages/projects-compendium-structure-editor.js",
    "wwwroot/js/projects/compendium-structure-state.js"
)
foreach ($path in $syntaxFiles) {
    node --check (Join-Path $root $path)
    if ($LASTEXITCODE -ne 0) { throw "JavaScript syntax validation failed: $path" }
}

$contracts = Get-ChildItem (Join-Path $root "wwwroot/js/projects") `
    -Filter "publications-compendium*.test.js" `
    | Sort-Object Name `
    | ForEach-Object { $_.FullName }
node --test $contracts
if ($LASTEXITCODE -ne 0) {
    throw "Compendium JavaScript contract tests failed."
}

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    dotnet build (Join-Path $root "ProjectManagement.csproj")
    if ($LASTEXITCODE -ne 0) { throw "Project build failed." }

    $tests = Join-Path $root "ProjectManagement.Tests/ProjectManagement.Tests.csproj"
    if (Test-Path $tests) {
        dotnet test $tests
        if ($LASTEXITCODE -ne 0) { throw "Project test suite failed." }
    }
} else {
    Write-Warning ".NET SDK not found; dotnet build/test skipped. Run this script on the development workstation."
}

Write-Host ""
Write-Host "Phase 37.2 validation complete." -ForegroundColor Green
Write-Host "Mandatory visual regression: regenerate the same nine-project Compendium and inspect pages 6 and 9 first, then the full 12-page publication." -ForegroundColor Yellow
