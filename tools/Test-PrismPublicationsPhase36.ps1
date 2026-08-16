param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($ProjectRoot)

Write-Host "PRISM Publications Phase 36 - final composition and image fidelity validation" -ForegroundColor Cyan
Write-Host "Project root: $root"
Write-Host ""

$required = @(
    "Services/Compendiums/CompendiumCoverTemplatePolicy.cs",
    "Services/Compendiums/CompendiumDossierNarrativeFlowPlanner.cs",
    "Services/Compendiums/CompendiumDossierPaginationPlanner.cs",
    "Services/Compendiums/CompendiumDtos.cs",
    "Services/Compendiums/CompendiumReadService.cs",
    "Services/Compendiums/CompendiumExportService.cs",
    "Services/Compendiums/CompendiumReviewFingerprint.cs",
    "Services/Publications/BrochureContracts.cs",
    "Services/Publications/BrochurePhotoService.cs",
    "Services/Publications/CompendiumPresetService.cs",
    "Models/Publications/CompendiumPreset.cs",
    "Migrations/20261208200000_AddCompendiumBalancedTextFlow.cs",
    "Utilities/Reporting/CompendiumPagePlanner.cs",
    "Utilities/Reporting/CompendiumPdfReportBuilder.cs",
    "Pages/Projects/Publications/Compendium/Index.cshtml",
    "Pages/Projects/Publications/Compendium/Index.cshtml.cs",
    "Pages/Projects/Publications/Compendium/Cover.cshtml",
    "Pages/Projects/Publications/Compendium/Cover.cshtml.cs",
    "Pages/Projects/Publications/Compendium/Structure.cshtml.cs",
    "wwwroot/js/pages/projects-compendium.js",
    "wwwroot/js/pages/projects-compendium-cover-editor.js",
    "wwwroot/js/pages/projects-compendium-structure-editor.js",
    "wwwroot/js/projects/compendium-structure-state.js",
    "wwwroot/js/projects/publications-compendium-phase36-contract.test.js",
    "ProjectManagement.Tests/Publications/CompendiumPhase36CompositionTests.cs"
)

foreach ($path in $required) {
    if (-not (Test-Path (Join-Path $root $path))) {
        throw "Missing Phase 36 file: $path"
    }
}

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

function Require-Text([string]$RelativePath, [string]$Pattern, [string]$Message) {
    $source = Get-Content (Join-Path $root $RelativePath) -Raw
    if ($source -notmatch $Pattern) { throw "$Message ($RelativePath)" }
}

function Forbid-Text([string]$RelativePath, [string]$Pattern, [string]$Message) {
    $source = Get-Content (Join-Path $root $RelativePath) -Raw
    if ($source -match $Pattern) { throw "$Message ($RelativePath)" }
}

Require-Text "Services/Compendiums/CompendiumDtos.cs" 'PortfolioQuartet\s*=\s*5' "Portfolio Quartet enum is missing"
Require-Text "Services/Compendiums/CompendiumDtos.cs" 'FlowBelowImage\s*=\s*1' "Balanced Flow below image enum is missing"
Require-Text "Services/Publications/CompendiumPresetService.cs" 'CurrentSchemaVersion\s*=\s*8' "Preset schema is not v8"
Require-Text "Services/Compendiums/CompendiumReviewFingerprint.cs" 'compendium-review-v11-balanced-text-flow' "Review fingerprint is not Phase 36"
Require-Text "Services/Compendiums/CompendiumReadService.cs" 'CompendiumPdf_2026-08-15_final-composition-v18' "PDF build identity is not Phase 36"
Require-Text "Services/Compendiums/CompendiumCoverTemplatePolicy.cs" 'Secondary3.*720.*540.*true.*true' "Quartet fourth Fill-only slot is missing"
Require-Text "Services/Compendiums/CompendiumDossierNarrativeFlowPlanner.cs" 'Prefer moving an intact paragraph to the next page' "Paragraph-first narrative flow policy is missing"
Forbid-Text "Services/Compendiums/CompendiumDossierNarrativeFlowPlanner.cs" 'Substring\(' "Narrative flow must not character-slice text"
Require-Text "Services/Publications/BrochureContracts.cs" 'PadFitToTarget\s*\{\s*get;\s*init;\s*\}\s*=\s*true' "Brochure compatibility default is missing"
Require-Text "Services/Compendiums/CompendiumExportService.cs" 'PadFitToTarget\s*=\s*false' "Compendium frameless Fit opt-out is missing"
Require-Text "Services/Compendiums/CompendiumDossierPaginationPlanner.cs" 'EstimatedLines\(item, 24\) <= 2' "Conservative three-column technical-spec policy is missing"
Require-Text "Utilities/Reporting/CompendiumPdfReportBuilder.cs" 'SDD SIMULATORS COMPENDIUM' "Index running header refinement is missing"
Require-Text "Utilities/Reporting/CompendiumPdfReportBuilder.cs" '#A97712' "Darker IPR gold fallback is missing"
Require-Text "wwwroot/js/projects/compendium-structure-state.js" 'const VERSION = 2' "Structure handoff schema is not v2"
Require-Text "wwwroot/js/pages/projects-compendium.js" 'review\.narrativeFlow' "Browser proof is not consuming server narrative flow"
Forbid-Text "wwwroot/js/pages/projects-compendium.js" 'function resolveSpecificationColumns' "Browser must not duplicate the technical-column heuristic"

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
Write-Host "Phase 36 validation complete." -ForegroundColor Green
Write-Host "Final workstation check: generate a Compendium containing Quartet, Balanced Flow below image, Fit and Fill examples, then review every rendered PDF page at print zoom." -ForegroundColor Yellow
