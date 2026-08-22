$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot

Write-Host "PRISM Publications Phase 42 - Slot-stable cover imagery validation"
Write-Host "Project root: $root"

$required = @(
    "Pages/Projects/Publications/Compendium/Cover.cshtml",
    "Pages/Projects/Publications/Compendium/Cover.cshtml.cs",
    "Pages/Projects/Publications/Compendium/Index.cshtml.cs",
    "Services/Compendiums/CompendiumCoverSlotAssignmentPolicy.cs",
    "Services/Compendiums/CompendiumExportService.cs",
    "Services/Publications/CompendiumPresetService.cs",
    "Utilities/Reporting/CompendiumBuildIdentity.cs",
    "wwwroot/css/pages/projects-publications.css",
    "wwwroot/js/pages/projects-compendium-cover-editor.js",
    "wwwroot/js/projects/compendium-cover-editor-state.js",
    "wwwroot/js/projects/publications-compendium-phase42-slot-stability.test.js",
    "ProjectManagement.Tests/Publications/CompendiumPhase42SlotStabilityTests.cs"
)

foreach ($relativePath in $required) {
    $absolutePath = Join-Path $root $relativePath
    if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf)) {
        throw "Missing Phase 42 file: $relativePath"
    }
}

if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    throw "Node.js is required to run the Compendium browser-contract tests."
}

& node --check (Join-Path $root "wwwroot/js/projects/compendium-cover-editor-state.js")
if ($LASTEXITCODE -ne 0) {
    throw "The cover-editor state module has a JavaScript syntax error."
}
& node --check (Join-Path $root "wwwroot/js/pages/projects-compendium-cover-editor.js")
if ($LASTEXITCODE -ne 0) {
    throw "The cover-editor page module has a JavaScript syntax error."
}

$jsTests = @(
    Get-ChildItem -LiteralPath (Join-Path $root "wwwroot/js/projects") -Filter "publications-compendium*.test.js" -File |
        Sort-Object Name |
        ForEach-Object FullName
)

if ($jsTests.Count -eq 0) {
    throw "No Compendium JavaScript contract tests were found."
}

& node --test @jsTests
if ($LASTEXITCODE -ne 0) {
    throw "The Compendium JavaScript contract suite failed."
}

$identity = Get-Content -LiteralPath (Join-Path $root "Utilities/Reporting/CompendiumBuildIdentity.cs") -Raw
foreach ($contract in @(
    'Phase = "42"',
    'phase42-slot-stable-cover',
    'compendium-review-v19-cover-identity',
    'physical-a4-v42'
)) {
    if ($identity -notmatch [regex]::Escape($contract)) {
        throw "Phase 42 build identity is missing: $contract"
    }
}

$editor = Get-Content -LiteralPath (Join-Path $root "wwwroot/js/pages/projects-compendium-cover-editor.js") -Raw
foreach ($contract in @(
    'invalidateAutomaticPreviews',
    'resetAutomaticSlot',
    'resetVisibleAutomaticAssignments',
    'refreshAutomaticImages',
    'reservedExplicitPhotos'
)) {
    if ($editor -notmatch [regex]::Escape($contract)) {
        throw "Phase 42 browser contract is missing: $contract"
    }
}

$policy = Get-Content -LiteralPath (Join-Path $root "Services/Compendiums/CompendiumCoverSlotAssignmentPolicy.cs") -Raw
foreach ($contract in @(
    'Pass 1: reserve every manual assignment',
    'Pass 2: retain every valid sticky automatic assignment',
    'Pass 3: allocate only the automatic slots'
)) {
    if ($policy -notmatch [regex]::Escape($contract)) {
        throw "Phase 42 server allocation contract is missing: $contract"
    }
}

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $project = Join-Path $root "ProjectManagement.csproj"
    $testProject = Join-Path $root "ProjectManagement.Tests/ProjectManagement.Tests.csproj"

    & dotnet build $project --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "The PRISM web project did not compile."
    }

    & dotnet test $testProject --no-restore --filter "FullyQualifiedName~CompendiumPhase42SlotStabilityTests"
    if ($LASTEXITCODE -ne 0) {
        throw "The Phase 42 server regression tests failed."
    }
} else {
    Write-Warning ".NET SDK not found; C# build and xUnit execution were skipped. Run this script on the PRISM build workstation before publishing."
}

Write-Host "Phase 42 validation complete. No database migration is required."
