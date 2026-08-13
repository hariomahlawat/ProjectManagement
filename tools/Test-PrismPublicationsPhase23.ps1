$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "PRISM Publications Phase 23 - Compendium review, imagery and readiness validation"
Write-Host "Project root: $root"

$required = @(
    "Pages/Projects/Publications/Compendium/Index.cshtml",
    "Pages/Projects/Publications/Compendium/Index.cshtml.cs",
    "Models/Publications/CompendiumPreset.cs",
    "Services/Compendiums/CompendiumDtos.cs",
    "Services/Compendiums/CompendiumReviewFingerprint.cs",
    "Services/Compendiums/CompendiumReadinessPolicy.cs",
    "Services/Compendiums/CompendiumReadService.cs",
    "Services/Compendiums/CompendiumExportService.cs",
    "Services/Publications/CompendiumPresetContracts.cs",
    "Services/Publications/CompendiumPresetService.cs",
    "Services/Publications/PublicationServiceCollectionExtensions.cs",
    "Migrations/20261208140000_AddCompendiumPublicationImagery.cs",
    "Migrations/ApplicationDbContextModelSnapshot.cs",
    "wwwroot/js/pages/projects-compendium.js",
    "wwwroot/js/projects/publications-compendium-contract.test.js",
    "wwwroot/css/pages/projects-publications.css"
)

foreach ($path in $required) {
    if (-not (Test-Path (Join-Path $root $path))) {
        throw "Missing Phase 23 file: $path"
    }
}

node --check (Join-Path $root "wwwroot/js/pages/projects-compendium.js")
node --test (Join-Path $root "wwwroot/js/projects/publications-compendium-contract.test.js")

$migrationId = "20261208140000_AddCompendiumPublicationImagery"
$immutable = Get-Content (Join-Path $root "Migrations/immutable-migration-ids.txt")
if ($immutable -notcontains $migrationId) {
    throw "Phase 23 migration id is not present in immutable-migration-ids.txt"
}

$migration = Get-Content (Join-Path $root "Migrations/20261208140000_AddCompendiumPublicationImagery.cs") -Raw
if ($migration -notmatch '\[Migration\("20261208140000_AddCompendiumPublicationImagery"\)\]' -or
    $migration -notmatch '\[DbContext\(typeof\(ApplicationDbContext\)\)\]') {
    throw "Phase 23 migration is missing EF Core discovery metadata."
}

$dto = Get-Content (Join-Path $root "Services/Compendiums/CompendiumDtos.cs") -Raw
if ($dto -notmatch 'FrameWidthPoints\s*=\s*198' -or
    $dto -notmatch 'FrameHeightPoints\s*=\s*152' -or
    $dto -notmatch 'GoodDpi\s*=\s*180' -or
    $dto -notmatch 'AcceptableDpi\s*=\s*150') {
    throw "Compendium publication-image geometry/DPI contract is incomplete."
}

$presetModel = Get-Content (Join-Path $root "Models/Publications/CompendiumPreset.cs") -Raw
if ($presetModel -notmatch 'PrimaryPhotoId' -or
    $presetModel -notmatch 'PrimaryFocalX' -or
    $presetModel -notmatch 'ImageSelectionMode') {
    throw "Saved Compendium publication-image fields are missing."
}
if ($presetModel -match 'ReviewFingerprint') {
    throw "Review fingerprints must not be persisted in saved Compendium entities."
}

$readiness = Get-Content (Join-Path $root "Services/Compendiums/CompendiumReadinessPolicy.cs") -Raw
foreach ($code in @("missingPhoto", "lowResolutionPhoto", "reviewRequired", "projectChangedAfterReview")) {
    if ($readiness -notmatch [regex]::Escape($code)) {
        throw "Missing readiness code: $code"
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

Write-Host "Phase 23 validation complete."
