$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "PRISM Publications Phase 22 - Compendium 2.0 foundation validation"
Write-Host "Project root: $root"

$required = @(
    "Pages/Projects/Publications/Index.cshtml",
    "Pages/Projects/Publications/Compendium/Index.cshtml",
    "Pages/Projects/Publications/Compendium/Index.cshtml.cs",
    "Models/Publications/CompendiumPreset.cs",
    "Services/Compendiums/CompendiumDtos.cs",
    "Services/Compendiums/CompendiumReadService.cs",
    "Services/Compendiums/CompendiumExportService.cs",
    "Services/Publications/CompendiumPresetService.cs",
    "Services/Publications/PublicationServiceCollectionExtensions.cs",
    "Migrations/20261208130000_AddSharedCompendiumPresets.cs",
    "wwwroot/js/pages/projects-compendium.js",
    "wwwroot/js/projects/publications-compendium-contract.test.js"
)

foreach ($path in $required) {
    if (-not (Test-Path (Join-Path $root $path))) {
        throw "Missing Phase 22 file: $path"
    }
}

node --check (Join-Path $root "wwwroot/js/pages/projects-compendium.js")
node --test (Join-Path $root "wwwroot/js/projects/publications-compendium-contract.test.js")

$immutable = Get-Content (Join-Path $root "Migrations/immutable-migration-ids.txt")
if ($immutable -notcontains "20261208130000_AddSharedCompendiumPresets") {
    throw "Phase 22 migration id is not present in immutable-migration-ids.txt"
}

$readService = Get-Content (Join-Path $root "Services/Compendiums/CompendiumReadService.cs") -Raw
if ($readService -notmatch "ProjectLifecycleStatus\.Active" -or
    $readService -notmatch "ProjectLifecycleStatus\.Completed") {
    throw "Compendium candidate portfolio is not using the normal Active/Completed publication scope."
}

$presetService = Get-Content (Join-Path $root "Services/Publications/CompendiumPresetService.cs") -Raw
if ($presetService -notmatch "BeginTransactionAsync" -or
    $presetService -notmatch "AddRange\(prepared\.Projects\)") {
    throw "Saved Compendium order replacement is missing its transactional reorder contract."
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

Write-Host "Phase 22 validation complete."
