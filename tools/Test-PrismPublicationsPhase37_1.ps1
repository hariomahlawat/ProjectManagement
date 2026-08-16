param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($ProjectRoot)

Write-Host "PRISM Publications Phase 37.1 - PDF parity and physical composition validation" -ForegroundColor Cyan
Write-Host "Project root: $root"
Write-Host ""

$required = @(
    "Services/Compendiums/CompendiumNarrativeTypographyPolicy.cs",
    "Services/Compendiums/CompendiumDossierTextMeasurementService.cs",
    "Services/Compendiums/CompendiumDossierImageGeometryPolicy.cs",
    "Services/Compendiums/CompendiumDossierNarrativeFlowPlanner.cs",
    "Services/Compendiums/CompendiumDossierPaginationPlanner.cs",
    "Services/Compendiums/CompendiumReadService.cs",
    "Services/Compendiums/CompendiumExportService.cs",
    "Services/Compendiums/CompendiumReviewFingerprint.cs",
    "Utilities/Reporting/MarkdownPdfRenderer.cs",
    "Utilities/Reporting/CompendiumPdfReportBuilder.cs",
    "wwwroot/css/pages/projects-publications.css",
    "wwwroot/js/pages/projects-compendium.js",
    "wwwroot/js/projects/publications-compendium-phase37-1-contract.test.js",
    "ProjectManagement.Tests/Publications/CompendiumPhase37_1CompositionTests.cs"
)

foreach ($path in $required) {
    if (-not (Test-Path (Join-Path $root $path))) {
        throw "Missing Phase 37.1 file: $path"
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

Require-Text "Services/Compendiums/CompendiumReviewFingerprint.cs" 'compendium-review-v13-physical-measurement' "Review fingerprint is not Phase 37.1"
Require-Text "Services/Compendiums/CompendiumReadService.cs" 'CompendiumPdf_2026-08-16_physical-composition-v20' "PDF build identity is not Phase 37.1"
Require-Text "Services/Compendiums/CompendiumNarrativeTypographyPolicy.cs" 'MaximumScale\s*=\s*1\.10f' "Narrative maximum scale is not centralised"
Require-Text "Services/Compendiums/CompendiumDossierTextMeasurementService.cs" 'DMSans-Regular\.ttf' "Publication font measurement is missing"
Require-Text "Services/Compendiums/CompendiumDossierTextMeasurementService.cs" 'MeasureText' "Physical text measurement is missing"
Require-Text "Services/Compendiums/CompendiumDossierImageGeometryPolicy.cs" 'RenderedHeightPoints' "Fit image geometry contract is missing"
Require-Text "Services/Compendiums/CompendiumDossierPaginationPlanner.cs" 'PhysicalContentHeightPoints\s*=\s*748f' "Physical A4 content envelope is missing"
Require-Text "Services/Compendiums/CompendiumDossierPaginationPlanner.cs" '(?:measurementSession\.Measure|CompendiumDossierTextMeasurementService\.Measure)' "Pagination is not using physical narrative measurement"
Require-Text "Services/Compendiums/CompendiumDossierPaginationPlanner.cs" 'CompendiumDossierImageGeometryPolicy\.Resolve' "Pagination is not using source-aware image geometry"
Require-Text "Services/Compendiums/CompendiumDossierNarrativeFlowPlanner.cs" 'sentence-by-sentence' "Measured sentence-fill flow is missing"
Forbid-Text "Services/Compendiums/CompendiumDossierNarrativeFlowPlanner.cs" 'Substring\(' "Narrative flow must never character-slice text"
Require-Text "Utilities/Reporting/MarkdownPdfRenderer.cs" 'MarkdownPdfTypography' "Caller-controlled Markdown typography is missing"
Require-Text "Utilities/Reporting/CompendiumPdfReportBuilder.cs" 'narrativeAlignment:\s*flow\.SideAlignment' "Narrow-column resolved alignment is not used by the PDF"
Forbid-Text "wwwroot/js/pages/projects-compendium.js" 'Math\.min\(1\.08' "Browser still owns an obsolete narrative scale cap"
Require-Text "wwwroot/css/pages/projects-publications.css" 'Phase 37\.1 — server-owned physical narrative proof parity' "Browser proof parity CSS is missing"

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
Write-Host "Phase 37.1 validation complete." -ForegroundColor Green
Write-Host "Mandatory final visual check: regenerate the same nine-project test Compendium and compare pages 4, 5, 6, 7, 10 and 11 at print scale." -ForegroundColor Yellow
