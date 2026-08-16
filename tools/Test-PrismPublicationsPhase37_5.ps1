param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($ProjectRoot)

Write-Host "PRISM Publications Phase 37.5 - Editorial rule simplification and review workspace polish" -ForegroundColor Cyan
Write-Host "Project root: $root"
Write-Host ""

$required = @(
    "Services/Compendiums/CompendiumDossierPaginationPlanner.cs",
    "Services/Compendiums/CompendiumReviewFingerprint.cs",
    "Services/Compendiums/CompendiumReadService.cs",
    "Utilities/Reporting/CompendiumPdfReportBuilder.cs",
    "Pages/Projects/Publications/Compendium/Index.cshtml",
    "wwwroot/css/pages/projects-publications.css",
    "wwwroot/js/pages/projects-compendium.js",
    "wwwroot/js/projects/publications-compendium-phase37-5-contract.test.js",
    "ProjectManagement.Tests/Publications/CompendiumPhase37_5EditorialRulesTests.cs"
)
foreach ($path in $required) {
    if (-not (Test-Path (Join-Path $root $path))) { throw "Missing Phase 37.5 file: $path" }
}

function Require-Text([string]$RelativePath, [string]$Pattern, [string]$Message) {
    $source = Get-Content (Join-Path $root $RelativePath) -Raw
    if ($source -notmatch $Pattern) { throw "$Message ($RelativePath)" }
}

Require-Text "Services/Compendiums/CompendiumReviewFingerprint.cs" 'compendium-review-v17-editorial-rules' "Review fingerprint is not Phase 37.5"
Require-Text "Services/Compendiums/CompendiumReadService.cs" 'CompendiumPdf_2026-08-16_editorial-rules-v24' "PDF build identity is not Phase 37.5"
Require-Text "Utilities/Reporting/CompendiumPdfReportBuilder.cs" 'HARDWARE / TECHNICAL SPECIFICATION' "Technical specification renderer is missing"
Require-Text "Utilities/Reporting/CompendiumPdfReportBuilder.cs" 'row\.AutoItem\(\)\.Text\("ADDITIONAL NOTE"\)' "Additional Note heading renderer is missing"
Require-Text "Pages/Projects/Publications/Compendium/Index.cshtml" 'data-review-open-project target="_blank" rel="noopener noreferrer"' "Open project does not preserve the Compendium tab"
Require-Text "Pages/Projects/Publications/Compendium/Index.cshtml" 'data-review-edit-record target="_blank" rel="noopener noreferrer"' "Edit record does not preserve the Compendium tab"
Require-Text "wwwroot/css/pages/projects-publications.css" 'compendium-live-page__additional-note>header::after' "Browser proof does not use heading-rule Additional Note treatment"
Require-Text "wwwroot/css/pages/projects-publications.css" 'compendium-live-page__specifications>header::after' "Browser proof does not use heading-rule specification treatment"

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
if ($LASTEXITCODE -ne 0) { throw "Compendium JavaScript contract tests failed." }

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
Write-Host "Phase 37.5 validation complete." -ForegroundColor Green
Write-Host "Final visual regression: inspect Minimal pages with Particulars + Note, Particulars + Specifications + Note, and a dense four-particular page. Verify Open project and Edit record keep the Compendium tab open." -ForegroundColor Yellow
