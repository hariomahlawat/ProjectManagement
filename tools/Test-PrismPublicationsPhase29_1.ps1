$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "PRISM Publications Phase 29.1 - Structure Composer stabilization validation"
Write-Host "Project root: $root"

$required = @(
    "Pages/Projects/Publications/Compendium/Structure.cshtml",
    "wwwroot/js/pages/projects-compendium.js",
    "wwwroot/js/pages/projects-compendium-structure-editor.js",
    "wwwroot/js/projects/publications-compendium-phase29-contract.test.js",
    "wwwroot/js/projects/publications-compendium-phase29-1-contract.test.js",
    "wwwroot/css/pages/projects-publications.css"
)

foreach ($path in $required) {
    if (-not (Test-Path (Join-Path $root $path))) {
        throw "Missing Phase 29.1 file: $path"
    }
}

node --check (Join-Path $root "wwwroot/js/pages/projects-compendium.js")
node --check (Join-Path $root "wwwroot/js/pages/projects-compendium-structure-editor.js")
node --test (Join-Path $root "wwwroot/js/projects/publications-compendium-phase29-contract.test.js")
node --test (Join-Path $root "wwwroot/js/projects/publications-compendium-phase29-1-contract.test.js")

$structure = Get-Content (Join-Path $root "Pages/Projects/Publications/Compendium/Structure.cshtml") -Raw
foreach ($contract in @(
    'data-editor-save-state',
    'data-editor-select-filtered',
    'data-editor-toggle-sections',
    'data-editor-canvas'
)) {
    if ($structure -notmatch [regex]::Escape($contract)) {
        throw "Phase 29.1 Structure Editor UI is missing: $contract"
    }
}

$mainJs = Get-Content (Join-Path $root "wwwroot/js/pages/projects-compendium.js") -Raw
foreach ($contract in @(
    'setControlDisabled(outputDockGenerate, !canDownload)',
    'setVisible(!Boolean(entry?.isIntersecting))',
    'if (reviewFocusMode) { setVisible(true); return; }'
)) {
    if ($mainJs -notmatch [regex]::Escape($contract)) {
        throw "Phase 29.1 output/focus contract is missing: $contract"
    }
}

$editorJs = Get-Content (Join-Path $root "wwwroot/js/pages/projects-compendium-structure-editor.js") -Raw
foreach ($contract in @(
    'fitEditorViewport',
    'compendium-structure-editor-mode',
    'sectionsNavigatorCollapsed',
    'selectFilteredButton'
)) {
    if ($editorJs -notmatch [regex]::Escape($contract)) {
        throw "Phase 29.1 Structure Editor client contract is missing: $contract"
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

Write-Host "Phase 29.1 validation complete."
