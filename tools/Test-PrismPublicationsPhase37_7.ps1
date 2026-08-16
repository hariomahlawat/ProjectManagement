param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path $ProjectRoot).Path

Write-Host "PRISM Publications Phase 37.7 validation" -ForegroundColor Cyan
Write-Host "Project root: $root"

function Require-File([string]$RelativePath) {
    $path = Join-Path $root $RelativePath
    if (-not (Test-Path $path)) { throw "Missing required Phase 37.7 file: $RelativePath" }
}

function Require-Text([string]$RelativePath, [string]$Pattern, [string]$Message) {
    Require-File $RelativePath
    $source = Get-Content (Join-Path $root $RelativePath) -Raw
    if ($source -notmatch $Pattern) { throw "$Message ($RelativePath)" }
}

$required = @(
    "Services/Compendiums/CompendiumCoverIdentityPolicy.cs",
    "Migrations/20261216160000_AddCompendiumCoverIdentity.cs",
    "ProjectManagement.Tests/Publications/CompendiumPhase37_7CoverIdentityTests.cs",
    "wwwroot/js/projects/publications-compendium-phase37-7-contract.test.js"
)
$required | ForEach-Object { Require-File $_ }

Require-Text "Services/Publications/CompendiumPresetService.cs" 'CurrentSchemaVersion\s*=\s*12' "Preset schema is not Phase 37.7 v12"
Require-Text "Models/Publications/CompendiumPreset.cs" 'PublicationTheme' "Publication theme persistence is missing"
Require-Text "Models/Publications/CompendiumPreset.cs" 'CoverBackgroundTreatment' "Cover background persistence is missing"
Require-Text "Services/Compendiums/CompendiumCoverIdentityPolicy.cs" 'TopographicContours' "Topographic treatment is missing"
Require-Text "Services/Compendiums/CompendiumCoverIdentityPolicy.cs" 'Camouflage' "Camouflage treatment is missing"
Require-Text "Pages/Projects/Publications/Compendium/Cover.cshtml" 'data-cover-theme="DeepNavy"' "Theme controls are missing from Cover Editor"
Require-Text "Pages/Projects/Publications/Compendium/Cover.cshtml" 'data-cover-background="Camouflage"' "Camouflage control is missing from Cover Editor"
Require-Text "Pages/Projects/Publications/Compendium/Cover.cshtml.cs" 'BuildSurfaceSvg' "Browser proof pattern endpoint is not using the authoritative identity policy"
Require-Text "Utilities/Reporting/CompendiumPdfReportBuilder.cs" 'CompendiumCoverIdentityPolicy\.BuildSurfaceSvg' "QuestPDF is not using the authoritative cover identity policy"
Require-Text "Services/Compendiums/CompendiumReviewFingerprint.cs" 'compendium-review-v19-cover-identity' "Review fingerprint is not Phase 37.7"
Require-Text "Services/Compendiums/CompendiumReadService.cs" 'CompendiumPdf_2026-08-16_cover-identity-v26' "PDF build identity is not Phase 37.7"
Require-Text "Migrations/immutable-migration-ids.txt" '20261216160000_AddCompendiumCoverIdentity' "Phase 37.7 migration is not immutable-manifested"

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
Write-Host "Phase 37.7 validation complete." -ForegroundColor Green
Write-Host "Final regression: save/reload Green+Solid, Navy+Contours and Graphite+Camouflage; compare browser/PDF cover geometry; verify Clean Back first-use defaults and non-destructive return behavior." -ForegroundColor Yellow
