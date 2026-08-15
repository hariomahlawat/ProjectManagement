$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "PRISM Publications Phase 34 - Programme icon system validation"
Write-Host "Project root: $root"

$icons = @(
    "arms-services.svg",
    "proliferation-cost.svg",
    "ipr-filed.svg",
    "ipr-granted.svg",
    "ipr-mixed.svg",
    "technology-transfer.svg"
)

$required = @(
    "Services/Compendiums/CompendiumProgrammeInformation.cs",
    "Services/Compendiums/CompendiumDossierPaginationPlanner.cs",
    "Services/Compendiums/CompendiumReadService.cs",
    "Services/Compendiums/CompendiumReviewFingerprint.cs",
    "Utilities/Reporting/CompendiumPdfReportBuilder.cs",
    "wwwroot/js/pages/projects-compendium.js",
    "wwwroot/js/pages/projects-compendium-structure-editor.js",
    "wwwroot/js/pages/projects-compendium-cover-editor.js",
    "wwwroot/css/pages/projects-publications.css",
    "wwwroot/js/projects/publications-compendium-phase34-contract.test.js"
)
$required += $icons | ForEach-Object { "wwwroot/images/publications/compendium-icons/$_" }

foreach ($path in $required) {
    if (-not (Test-Path (Join-Path $root $path))) {
        throw "Missing Phase 34 file: $path"
    }
}

node --check (Join-Path $root "wwwroot/js/pages/projects-compendium.js")
node --check (Join-Path $root "wwwroot/js/pages/projects-compendium-structure-editor.js")
node --check (Join-Path $root "wwwroot/js/pages/projects-compendium-cover-editor.js")

$contracts = Get-ChildItem (Join-Path $root "wwwroot/js/projects") `
    -Filter "publications-compendium*contract.test.js" `
    | Sort-Object Name `
    | ForEach-Object { $_.FullName }
node --test $contracts
if ($LASTEXITCODE -ne 0) {
    throw "Compendium JavaScript contract tests failed."
}

foreach ($icon in $icons) {
    $iconPath = Join-Path $root "wwwroot/images/publications/compendium-icons/$icon"
    [xml]$svg = Get-Content $iconPath -Raw
    if ($svg.svg.viewBox -ne "0 0 24 24" -or $svg.svg.width -ne "24" -or $svg.svg.height -ne "24") {
        throw "$icon does not use the 24 x 24 programme-icon canvas."
    }

    $source = Get-Content $iconPath -Raw
    $weights = [regex]::Matches($source, 'stroke-width="([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
    if ($weights.Count -eq 0 -or ($weights | Where-Object { $_ -ne "1.8" }).Count -gt 0) {
        throw "$icon does not use the shared 1.8 vector stroke."
    }
    if ($source -match '<(?:image|filter|linearGradient|radialGradient)\b') {
        throw "$icon contains a raster image or unsupported SVG effect."
    }
}

$mainJs = Get-Content (Join-Path $root "wwwroot/js/pages/projects-compendium.js") -Raw
foreach ($contract in @(
    'const programmeIconVersion = "v15"',
    'compendium-live-page__programme-heading',
    'is-compact-single'
)) {
    if ($mainJs -notmatch [regex]::Escape($contract)) {
        throw "Phase 34 browser-proof contract is missing: $contract"
    }
}

$builder = Get-Content (Join-Path $root "Utilities/Reporting/CompendiumPdfReportBuilder.cs") -Raw
foreach ($contract in @(
    'ProgrammeTopRuleHeight = 2.25f',
    'IsCompactSingleProgrammeModule(modules[0])',
    'row.RelativeItem();'
)) {
    if ($builder -notmatch [regex]::Escape($contract)) {
        throw "Phase 34 PDF composition contract is missing: $contract"
    }
}

$planner = Get-Content (Join-Path $root "Services/Compendiums/CompendiumDossierPaginationPlanner.cs") -Raw
if ($planner -notmatch [regex]::Escape('return 30.25f + rows * 38f;')) {
    throw "Phase 34 programme-height estimate is missing."
}

$readService = Get-Content (Join-Path $root "Services/Compendiums/CompendiumReadService.cs") -Raw
if ($readService -notmatch [regex]::Escape('CompendiumPdf_2026-08-15_programme-iconography-v15')) {
    throw "Phase 34 PDF build stamp is missing."
}

$fingerprint = Get-Content (Join-Path $root "Services/Compendiums/CompendiumReviewFingerprint.cs") -Raw
if ($fingerprint -notmatch [regex]::Escape('compendium-review-v9-programme-iconography')) {
    throw "The stable Phase 33 editorial-review fingerprint is missing."
}
if ($fingerprint -match [regex]::Escape('programme-iconography-v15')) {
    throw "A presentation-only icon refinement must not invalidate editorial reviews."
}

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    dotnet build (Join-Path $root "ProjectManagement.csproj")
    if ($LASTEXITCODE -ne 0) {
        throw "Project build failed."
    }

    $tests = Join-Path $root "ProjectManagement.Tests/ProjectManagement.Tests.csproj"
    if (Test-Path $tests) {
        dotnet test $tests
        if ($LASTEXITCODE -ne 0) {
            throw "Project test suite failed."
        }
    }
} else {
    Write-Warning ".NET SDK not found; dotnet build/test skipped. Run this script on the development workstation."
}

Write-Host "Phase 34 validation complete. No database migration or editorial-review reset is required."
