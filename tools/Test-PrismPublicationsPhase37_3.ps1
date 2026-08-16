param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($ProjectRoot)

Write-Host "PRISM Publications Phase 37.3 - final editorial hardening and Additional Notes validation" -ForegroundColor Cyan
Write-Host "Project root: $root"
Write-Host ""

$required = @(
    "Models/Publications/CompendiumPreset.cs",
    "Migrations/20261216110000_AddCompendiumProjectAdditionalNote.cs",
    "Services/Compendiums/CompendiumPublicationNotePolicy.cs",
    "Services/Compendiums/CompendiumDossierEditorialPolicy.cs",
    "Services/Compendiums/CompendiumDossierNarrativeFlowPlanner.cs",
    "Services/Compendiums/CompendiumDossierPaginationPlanner.cs",
    "Services/Compendiums/CompendiumDossierTextMeasurementService.cs",
    "Services/Compendiums/CompendiumReadinessPolicy.cs",
    "Services/Compendiums/CompendiumReadService.cs",
    "Services/Compendiums/CompendiumReviewFingerprint.cs",
    "Utilities/Reporting/CompendiumPagePlanner.cs",
    "Utilities/Reporting/CompendiumPdfReportBuilder.cs",
    "Pages/Projects/Publications/Compendium/Index.cshtml",
    "Pages/Projects/Publications/Compendium/Index.cshtml.cs",
    "Pages/Projects/Publications/Compendium/Structure.cshtml.cs",
    "wwwroot/js/projects/compendium-structure-state.js",
    "wwwroot/js/pages/projects-compendium.js",
    "wwwroot/js/projects/publications-compendium-phase37-3-contract.test.js",
    "ProjectManagement.Tests/Publications/CompendiumPhase37_3FinalHardeningTests.cs"
)

foreach ($path in $required) {
    if (-not (Test-Path (Join-Path $root $path))) {
        throw "Missing Phase 37.3 file: $path"
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

Require-Text "Models/Publications/CompendiumPreset.cs" 'SettingsSchemaVersion\s*\{\s*get;\s*set;\s*\}\s*=\s*(?:10|11)' "Preset schema is not v10"
Require-Text "Models/Publications/CompendiumPreset.cs" 'string\?\s+AdditionalNote' "Per-project Additional Note persistence is missing"
Require-Text "Data/ApplicationDbContext.cs" 'SettingsSchemaVersion\)\.HasDefaultValue\((?:10|11)\)' "Compendium schema database default is not v10"
Require-Text "Migrations/20261216110000_AddCompendiumProjectAdditionalNote.cs" 'SET "SettingsSchemaVersion" = 10' "Schema-v10 migration upgrade is missing"
Require-Text "Migrations/20261216110000_AddCompendiumProjectAdditionalNote.cs" 'name:\s*"AdditionalNote"[\s\S]*type:\s*"text"' "Additional Note migration column is missing"
Require-Text "Services/Compendiums/CompendiumReviewFingerprint.cs" 'compendium-review-v(?:15-additional-note-final-hardening|16-particulars-style)' "Review fingerprint is not Phase 37.3"
Require-Text "Services/Compendiums/CompendiumReviewFingerprint.cs" 'PhotoVersion' "Media-version review identity is missing"
Require-Text "Services/Compendiums/CompendiumReadService.cs" 'CompendiumPdf_2026-08-16_(?:final-editorial-v22|particulars-style-v23)' "PDF build identity is not Phase 37.3"
Require-Text "Services/Compendiums/CompendiumDossierEditorialPolicy.cs" 'MaximumFlowBelowGapPoints\s*=\s*40f' "Flow Below Image gap policy is missing"
Require-Text "Services/Compendiums/CompendiumDossierEditorialPolicy.cs" 'ShallowFitWarning' "Shallow Fit advisory is missing"
Require-Text "Services/Compendiums/CompendiumDossierPaginationPlanner.cs" 'flowBelowBalanced\s*=\s*!side\.HasExcessiveGap' "Measured Flow Below gap is not enforced"
Require-Text "Services/Compendiums/CompendiumDossierPaginationPlanner.cs" 'MeasureAtFontSize[\s\S]*LineCount <= maximumLines' "Technical specification columns are not physically measured"
Require-Text "Services/Compendiums/CompendiumDossierNarrativeFlowPlanner.cs" 'SplitForPhysicalPages' "Physical continuation splitting is missing"
Forbid-Text "Utilities/Reporting/CompendiumPagePlanner.cs" 'CompendiumMarkdownChunker' "Legacy character-budget page chunker remains in the authoritative page planner"
Require-Text "Services/Compendiums/CompendiumDossierTextMeasurementService.cs" 'DMSans-Regular\.ttf' "Bundled DM Sans measurement face is missing"
Forbid-Text "Services/Compendiums/CompendiumDossierTextMeasurementService.cs" 'SKTypeface\.Default' "Authoritative measurement can silently fall back to a host font"
Require-Text "Services/Compendiums/CompendiumReadinessPolicy.cs" 'duplicateNarrativeParagraph' "Duplicate narrative preflight is missing"
Require-Text "Pages/Projects/Publications/Compendium/Index.cshtml" 'data-review-additional-note' "Additional Note editor is missing"
Require-Text "Pages/Projects/Publications/Compendium/Index.cshtml" '>DEFAULT<' "Project Brief default badge is missing"
Require-Text "wwwroot/js/projects/compendium-structure-state.js" 'const VERSION = 4' "Structure handoff is not v4"
Require-Text "wwwroot/js/pages/projects-compendium.js" 'formatDescription\(additionalNote\)' "Additional Note browser proof does not render the supported Markdown subset"

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
Write-Host "Phase 37.3 validation complete." -ForegroundColor Green
Write-Host "Final visual regression: verify a typical rich dossier, a sparse no-specifications dossier, a long-brief continuation dossier, and a long Additional Note continuation dossier before freezing the engine." -ForegroundColor Yellow
