param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($ProjectRoot)

Write-Host "PRISM Publications Phase 37.4 - Project Particulars presentation and publication freeze validation" -ForegroundColor Cyan
Write-Host "Project root: $root"
Write-Host ""

$required = @(
    "Models/Publications/CompendiumPreset.cs",
    "Migrations/20261216123000_AddCompendiumProjectParticularsStyle.cs",
    "Services/Compendiums/CompendiumProjectParticularsLayoutPolicy.cs",
    "Services/Compendiums/CompendiumDossierPaginationPlanner.cs",
    "Services/Compendiums/CompendiumReviewFingerprint.cs",
    "Services/Compendiums/CompendiumReadService.cs",
    "Utilities/Reporting/CompendiumPdfReportBuilder.cs",
    "Pages/Projects/Publications/Compendium/Index.cshtml",
    "Pages/Projects/Publications/Compendium/Index.cshtml.cs",
    "Pages/Projects/Publications/Compendium/Structure.cshtml.cs",
    "wwwroot/css/pages/projects-publications.css",
    "wwwroot/js/projects/compendium-structure-state.js",
    "wwwroot/js/pages/projects-compendium.js",
    "wwwroot/js/pages/projects-compendium-structure-editor.js",
    "wwwroot/js/projects/publications-compendium-phase37-4-contract.test.js",
    "ProjectManagement.Tests/Publications/CompendiumPhase37_4ParticularsStyleTests.cs"
)

foreach ($path in $required) {
    if (-not (Test-Path (Join-Path $root $path))) { throw "Missing Phase 37.4 file: $path" }
}

function Require-Text([string]$RelativePath, [string]$Pattern, [string]$Message) {
    $source = Get-Content (Join-Path $root $RelativePath) -Raw
    if ($source -notmatch $Pattern) { throw "$Message ($RelativePath)" }
}

Require-Text "Models/Publications/CompendiumPreset.cs" 'SettingsSchemaVersion\s*\{\s*get;\s*set;\s*\}\s*=\s*11' "Preset schema is not v11"
Require-Text "Models/Publications/CompendiumPreset.cs" 'ProjectParticularsStyle[\s\S]*=\s*"Panel"' "Panel is not the persisted default"
Require-Text "Data/ApplicationDbContext.cs" 'ProjectParticularsStyle\)\.HasMaxLength\(24\)\.HasDefaultValue\("Panel"\)' "Project Particulars style database mapping is missing"
Require-Text "Migrations/20261216123000_AddCompendiumProjectParticularsStyle.cs" 'defaultValue:\s*"Panel"' "Schema-v11 migration does not preserve Panel for legacy presets"
Require-Text "Services/Compendiums/CompendiumProjectParticularsLayoutPolicy.cs" 'ResolveMinimalColumns' "Minimal layout policy is missing"
Require-Text "Services/Compendiums/CompendiumProjectParticularsLayoutPolicy.cs" 'MeasureAtFontSize' "Project Particulars layout is not physically measured"
Require-Text "Services/Compendiums/CompendiumDossierPaginationPlanner.cs" 'programmeHeight\s*=\s*particularsLayout\.HeightPoints' "Pagination does not consume authoritative particulars height"
Require-Text "Services/Compendiums/CompendiumReviewFingerprint.cs" 'compendium-review-v16-particulars-style' "Review fingerprint is not Phase 37.4"
Require-Text "Services/Compendiums/CompendiumReadService.cs" 'CompendiumPdf_2026-08-16_particulars-style-v23' "PDF build identity is not Phase 37.4"
Require-Text "Utilities/Reporting/CompendiumPdfReportBuilder.cs" 'ComposeProjectParticularsPanel' "Panel renderer is missing"
Require-Text "Utilities/Reporting/CompendiumPdfReportBuilder.cs" 'ComposeProjectParticularsMinimal' "Minimal renderer is missing"
Require-Text "Pages/Projects/Publications/Compendium/Index.cshtml" 'data-particulars-style-value="Panel"' "Panel authoring choice is missing"
Require-Text "Pages/Projects/Publications/Compendium/Index.cshtml" 'data-particulars-style-value="Minimal"' "Minimal authoring choice is missing"
Require-Text "wwwroot/js/pages/projects-compendium.js" 'orderedIds\.forEach\(invalidateProjectReview\)' "Changing global particulars style does not invalidate project reviews"
Require-Text "wwwroot/js/projects/compendium-structure-state.js" 'projectParticularsStyle' "Structure handoff does not preserve particulars style"
Require-Text "Pages/Projects/Publications/Compendium/Structure.cshtml.cs" 'ProjectParticularsStyle\s*=\s*ParseProjectParticularsStyle' "Structure save does not preserve unsaved particulars style"

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
Write-Host "Phase 37.4 validation complete." -ForegroundColor Green
Write-Host "Final visual regression: compare Panel and Minimal on a rich four-particular dossier, one-particular sparse dossier, three-particular dossier and a dense dossier with Additional Note." -ForegroundColor Yellow
