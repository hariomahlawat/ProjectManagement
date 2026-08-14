$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "PRISM Publications Phase 29 - Large-Compendium Structure Composer validation"
Write-Host "Project root: $root"

$required = @(
    "Pages/Projects/Publications/Compendium/Index.cshtml",
    "Pages/Projects/Publications/Compendium/Structure.cshtml",
    "Pages/Projects/Publications/Compendium/Structure.cshtml.cs",
    "wwwroot/js/pages/projects-compendium.js",
    "wwwroot/js/pages/projects-compendium-structure-editor.js",
    "wwwroot/js/projects/compendium-structure-state.js",
    "wwwroot/js/projects/publications-compendium-contract.test.js",
    "wwwroot/js/projects/publications-compendium-phase29-contract.test.js",
    "wwwroot/css/pages/projects-publications.css",
    "ProjectManagement.Tests/Publications/CompendiumPhase29ContractTests.cs"
)

foreach ($path in $required) {
    if (-not (Test-Path (Join-Path $root $path))) {
        throw "Missing Phase 29 file: $path"
    }
}

node --check (Join-Path $root "wwwroot/js/pages/projects-compendium.js")
node --check (Join-Path $root "wwwroot/js/pages/projects-compendium-structure-editor.js")
node --check (Join-Path $root "wwwroot/js/projects/compendium-structure-state.js")
node --test (Join-Path $root "wwwroot/js/projects/publications-compendium-contract.test.js")
node --test (Join-Path $root "wwwroot/js/projects/publications-compendium-phase29-contract.test.js")

$index = Get-Content (Join-Path $root "Pages/Projects/Publications/Compendium/Index.cshtml") -Raw
foreach ($contract in @(
    'role="option"',
    'tabindex="0"',
    'data-open-structure-editor',
    'data-structure-editor-url'
)) {
    if ($index -notmatch [regex]::Escape($contract)) {
        throw "Phase 29 project-selection contract is missing: $contract"
    }
}

$structure = Get-Content (Join-Path $root "Pages/Projects/Publications/Compendium/Structure.cshtml") -Raw
foreach ($contract in @(
    'PUBLICATION COMPOSER',
    'data-editor-project-list',
    'data-editor-canvas',
    'data-editor-section-nav',
    'data-editor-bulk-move',
    'data-editor-bulk-remove',
    'compendiumStructureLeaveModal'
)) {
    if ($structure -notmatch [regex]::Escape($contract)) {
        throw "Phase 29 Structure Editor UI is missing: $contract"
    }
}

$editorJs = Get-Content (Join-Path $root "wwwroot/js/pages/projects-compendium-structure-editor.js") -Raw
foreach ($contract in @(
    'editorSelection',
    'beginAutoScroll',
    'draggedSectionKey',
    'saveStructure',
    'beforeunload',
    'writeHandoff',
    'updateActiveSectionNav'
)) {
    if ($editorJs -notmatch [regex]::Escape($contract)) {
        throw "Phase 29 Structure Editor client contract is missing: $contract"
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

Write-Host "Phase 29 validation complete."
