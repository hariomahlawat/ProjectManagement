param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path $ProjectRoot).Path

Write-Host "PRISM Publications Phase 37.6 validation" -ForegroundColor Cyan
Write-Host "Project root: $root"

function Require-File([string]$RelativePath) {
    $path = Join-Path $root $RelativePath
    if (-not (Test-Path $path)) { throw "Missing required Phase 37.6 file: $RelativePath" }
}

function Require-Text([string]$RelativePath, [string]$Pattern, [string]$Message) {
    Require-File $RelativePath
    $source = Get-Content (Join-Path $root $RelativePath) -Raw
    if ($source -notmatch $Pattern) { throw "$Message ($RelativePath)" }
}

$required = @(
    "Services/Compendiums/CompendiumNarrativeDocument.cs",
    "Services/Compendiums/CompendiumDossierTextMeasurementService.cs",
    "Services/Compendiums/CompendiumDossierNarrativeFlowPlanner.cs",
    "Utilities/Reporting/CompendiumNarrativePdfRenderer.cs",
    "ProjectManagement.Tests/Publications/CompendiumPhase37_6ProductionHardeningTests.cs",
    "wwwroot/js/projects/publications-compendium-phase37-6-contract.test.js"
)
$required | ForEach-Object { Require-File $_ }

Require-Text "Pages/Projects/Publications/Compendium/Index.cshtml.cs" 'AdditionalNote\s*=\s*project\.AdditionalNote' "Additional Note is not restored during saved Compendium reload"
Require-Text "Pages/Projects/Publications/Compendium/Structure.cshtml.cs" 'AdditionalNote\s*=\s*item\.AdditionalNoteSpecified\s*\?' "Structure Editor cannot distinguish absent note state from an explicit clear"
Require-Text "Services/Compendiums/CompendiumNarrativeDocument.cs" 'CompendiumNarrativeBlockKind' "Controlled semantic narrative model is missing"
Require-Text "Services/Compendiums/CompendiumDossierNarrativeFlowPlanner.cs" 'Never strand a semantic heading' "Minor-heading keep-with-next planning is missing"
Require-Text "Utilities/Reporting/CompendiumPdfReportBuilder.cs" 'CompendiumNarrativePdfRenderer\.Render' "Compendium PDF is not using the controlled semantic renderer"
Require-Text "Services/Compendiums/CompendiumReadinessPolicy.cs" 'placeholderNarrative' "Placeholder narrative preflight is missing"
Require-Text "Services/Compendiums/CompendiumReadinessPolicy.cs" 'duplicateNarrativeParagraph' "Duplicate narrative preflight is missing"
Require-Text "Services/Compendiums/CompendiumReviewFingerprint.cs" 'compendium-review-v18-semantic-narrative' "Review fingerprint is not Phase 37.6"
Require-Text "Services/Compendiums/CompendiumReadService.cs" 'CompendiumPdf_2026-08-16_semantic-narrative-v25' "PDF build identity is not Phase 37.6"
Require-Text "Services/Publications/CompendiumPresetService.cs" 'CurrentSchemaVersion\s*=\s*11' "Phase 37.6 must not change the preset schema"
Require-Text "wwwroot/js/projects/compendium-structure-state.js" 'const VERSION = 4' "Phase 37.6 must retain Structure handoff v4"

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
        dotnet test $tests --filter "FullyQualifiedName~Compendium"
        if ($LASTEXITCODE -ne 0) { throw "Compendium C# test suite failed." }
    }
} else {
    Write-Warning ".NET SDK not found; dotnet build/test skipped. Run this script on the development workstation."
}

Write-Host ""
Write-Host "Phase 37.6 validation complete." -ForegroundColor Green
Write-Host "Final regression: verify Additional Note save/load/save, a brief with ### minor headings and bullets, a long Project Brief continuation, and a long Additional Note continuation in browser proof and PDF." -ForegroundColor Yellow
