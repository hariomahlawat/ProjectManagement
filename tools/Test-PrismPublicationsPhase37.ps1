param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($ProjectRoot)

Write-Host "PRISM Publications Phase 37 - composition hardening and professional typesetting validation" -ForegroundColor Cyan
Write-Host "Project root: $root"
Write-Host ""

$required = @(
    "Services/Compendiums/CompendiumNarrativeTypographyPolicy.cs",
    "Services/Compendiums/CompendiumImageQualityPolicy.cs",
    "Services/Compendiums/CompendiumDossierNarrativeFlowPlanner.cs",
    "Services/Compendiums/CompendiumDossierPaginationPlanner.cs",
    "Services/Compendiums/CompendiumDtos.cs",
    "Services/Compendiums/CompendiumReadService.cs",
    "Services/Compendiums/CompendiumExportService.cs",
    "Services/Compendiums/CompendiumReviewFingerprint.cs",
    "Services/Publications/CompendiumPresetContracts.cs",
    "Services/Publications/CompendiumPresetService.cs",
    "Models/Publications/CompendiumPreset.cs",
    "Migrations/20261215170000_AddCompendiumNarrativeAlignment.cs",
    "Utilities/Reporting/MarkdownPdfRenderer.cs",
    "Utilities/Reporting/CompendiumPagePlanner.cs",
    "Utilities/Reporting/CompendiumPdfReportBuilder.cs",
    "Pages/Projects/Publications/Compendium/Index.cshtml",
    "Pages/Projects/Publications/Compendium/Index.cshtml.cs",
    "Pages/Projects/Publications/Compendium/Cover.cshtml",
    "Pages/Projects/Publications/Compendium/Structure.cshtml.cs",
    "wwwroot/js/pages/projects-compendium.js",
    "wwwroot/js/pages/projects-compendium-cover-editor.js",
    "wwwroot/js/pages/projects-compendium-structure-editor.js",
    "wwwroot/js/projects/compendium-structure-state.js",
    "wwwroot/js/projects/publications-compendium-phase37-contract.test.js",
    "ProjectManagement.Tests/Publications/CompendiumPhase37CompositionTests.cs"
)

foreach ($path in $required) {
    if (-not (Test-Path (Join-Path $root $path))) {
        throw "Missing Phase 37 file: $path"
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

Require-Text "Services/Compendiums/CompendiumDtos.cs" 'enum CompendiumNarrativeAlignment' "Narrative-alignment contract is missing"
Require-Text "Services/Publications/CompendiumPresetService.cs" 'CurrentSchemaVersion\s*=\s*9' "Preset schema is not v9"
Require-Text "Services/Compendiums/CompendiumReviewFingerprint.cs" 'compendium-review-v(?:12-professional-typesetting|13-physical-measurement)' "Review fingerprint is not Phase 37 or its 37.1 successor"
Require-Text "Services/Compendiums/CompendiumReadService.cs" 'CompendiumPdf_2026-08-(?:15_composition-hardening-v19|16_physical-composition-v20)' "PDF build identity is not Phase 37 or its 37.1 successor"
Require-Text "Services/Compendiums/CompendiumDossierNarrativeFlowPlanner.cs" 'sentence-by-sentence' "Sentence-fill Balanced flow is missing"
Require-Text "Services/Compendiums/CompendiumDossierNarrativeFlowPlanner.cs" 'SideRemainingHeightPoints' "Balanced side-utilisation metrics are missing"
Forbid-Text "Services/Compendiums/CompendiumDossierNarrativeFlowPlanner.cs" 'Substring\(' "Narrative flow must not character-slice text"
Require-Text "Services/Compendiums/CompendiumImageQualityPolicy.cs" 'MinimumLargeImageDpi\s*=\s*120' "Hard low-DPI threshold is missing"
Require-Text "Services/Compendiums/CompendiumDossierPaginationPlanner.cs" 'IsAutomaticLayoutAllowed' "Automatic layout DPI gating is missing"
Require-Text "Utilities/Reporting/MarkdownPdfRenderer.cs" 'text\.Justify\(\)' "QuestPDF paragraph justification is missing"
Require-Text "wwwroot/js/projects/compendium-structure-state.js" 'const VERSION = 3' "Structure handoff schema is not v3"
Require-Text "wwwroot/css/pages/projects-publications.css" '\.cover-proof-quartet\{[^}]*top:338px;[^}]*height:488px' "Corrected Quartet proof geometry is missing"
Require-Text "Pages/Projects/Publications/Compendium/Cover.cshtml" '>Fit page<' "Cover proof Fit page terminology is missing"
Require-Text "Pages/Projects/Publications/Compendium/Index.cshtml" 'data-narrative-alignment-value="Justified"' "Publication justification control is missing"
Require-Text "Pages/Projects/Publications/Compendium/Index.cshtml" 'data-review-narrative-alignment="default"' "Per-project alignment inheritance control is missing"
Require-Text "wwwroot/js/pages/projects-compendium.js" 'flow\.sideAlignment' "Browser proof is not consuming server side-alignment policy"
Require-Text "wwwroot/js/pages/projects-compendium.js" 'flow\.belowAlignment' "Browser proof is not consuming server full-width alignment policy"

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
Write-Host "Phase 37 validation complete." -ForegroundColor Green
Write-Host "Final workstation check: generate representative Balanced/Justified/low-DPI/Quartet pages and visually review the PDF at print scale before freezing composition." -ForegroundColor Yellow
