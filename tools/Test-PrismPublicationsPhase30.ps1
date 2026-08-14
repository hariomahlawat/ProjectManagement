$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "PRISM Publications Phase 30 - Cover Composer & Publication Imagery validation"
Write-Host "Project root: $root"

$required = @(
    "Pages/Projects/Publications/Compendium/Cover.cshtml",
    "Pages/Projects/Publications/Compendium/Cover.cshtml.cs",
    "Pages/Projects/Publications/Compendium/Index.cshtml",
    "Pages/Projects/Publications/Compendium/Index.cshtml.cs",
    "Services/Compendiums/CompendiumDtos.cs",
    "Services/Compendiums/CompendiumExportService.cs",
    "Services/Compendiums/CompendiumReadinessPolicy.cs",
    "Services/Publications/CompendiumPresetService.cs",
    "Utilities/Reporting/CompendiumPdfReportBuilder.cs",
    "Migrations/20261208180000_AddCompendiumCoverComposer.cs",
    "wwwroot/js/pages/projects-compendium-cover-editor.js",
    "wwwroot/js/pages/projects-compendium.js",
    "wwwroot/js/projects/publications-compendium-phase30-contract.test.js",
    "wwwroot/css/pages/projects-publications.css"
)

foreach ($path in $required) {
    if (-not (Test-Path (Join-Path $root $path))) {
        throw "Missing Phase 30 file: $path"
    }
}

node --check (Join-Path $root "wwwroot/js/pages/projects-compendium.js")
node --check (Join-Path $root "wwwroot/js/pages/projects-compendium-structure-editor.js")
node --check (Join-Path $root "wwwroot/js/pages/projects-compendium-cover-editor.js")
node --test `
    (Join-Path $root "wwwroot/js/projects/publications-compendium-contract.test.js") `
    (Join-Path $root "wwwroot/js/projects/publications-compendium-phase29-contract.test.js") `
    (Join-Path $root "wwwroot/js/projects/publications-compendium-phase29-1-contract.test.js") `
    (Join-Path $root "wwwroot/js/projects/publications-compendium-phase30-contract.test.js")

$builder = Get-Content (Join-Path $root "Utilities/Reporting/CompendiumPdfReportBuilder.cs") -Raw
foreach ($hardCodedCoverText in @(
    'Detailed Project Reference',
    'Capability Edition ·',
    'Simulators Compendium'
)) {
    if ($builder -match [regex]::Escape($hardCodedCoverText)) {
        throw "Phase 30 PDF builder still contains hard-coded cover text: $hardCodedCoverText"
    }
}

$manifest = Get-Content (Join-Path $root "Migrations/immutable-migration-ids.txt") -Raw
if ($manifest -notmatch '20261208180000_AddCompendiumCoverComposer') {
    throw "Phase 30 migration is missing from immutable-migration-ids.txt"
}

$coverView = Get-Content (Join-Path $root "Pages/Projects/Publications/Compendium/Cover.cshtml") -Raw
foreach ($contract in @(
    'data-cover-template="InstitutionalHero"',
    'data-cover-template="FullBleedHero"',
    'data-cover-template="EditorialSplit"',
    'data-cover-template="Triptych"',
    'data-cover-template="Minimal"',
    'data-cover-template="ImageEcho"',
    'data-cover-template="PortfolioStrip"',
    'data-cover-logo-placement',
    'data-cover-slot-list'
)) {
    if ($coverView -notmatch [regex]::Escape($contract)) {
        throw "Phase 30 cover editor contract is missing: $contract"
    }
}

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    dotnet build (Join-Path $root "ProjectManagement.csproj")

    $tests = Join-Path $root "ProjectManagement.Tests/ProjectManagement.Tests.csproj"
    if (Test-Path $tests) {
        dotnet test $tests
    }
} else {
    Write-Warning ".NET SDK not found; dotnet build/test skipped. Run this script again on the development workstation."
}

Write-Host "Phase 30 validation complete."
