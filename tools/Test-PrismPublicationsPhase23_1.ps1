$ErrorActionPreference = 'Stop'

Write-Host 'PRISM Publications Phase 23.1 integration check' -ForegroundColor Cyan

$projectRoot = Split-Path -Parent $PSScriptRoot
Write-Host "Project root: $projectRoot"

$required = @(
    'Pages\Projects\Publications\Compendium\Index.cshtml',
    'wwwroot\js\pages\projects-compendium.js',
    'wwwroot\css\pages\projects-publications.css',
    'wwwroot\js\projects\publications-compendium-contract.test.js'
)

foreach ($relative in $required) {
    $path = Join-Path $projectRoot $relative
    if (-not (Test-Path $path)) {
        throw "Required Phase 23.1 file is missing: $relative"
    }
}

$view = Get-Content (Join-Path $projectRoot 'Pages\Projects\Publications\Compendium\Index.cshtml') -Raw
$js = Get-Content (Join-Path $projectRoot 'wwwroot\js\pages\projects-compendium.js') -Raw
$css = Get-Content (Join-Path $projectRoot 'wwwroot\css\pages\projects-publications.css') -Raw

$contracts = @(
    @{ Name = 'Semantic selection badges'; Text = $view; Pattern = 'Description available' },
    @{ Name = 'No-photo selection state'; Text = $view; Pattern = 'No photo' },
    @{ Name = 'Neutral empty readiness'; Text = $view; Pattern = 'Select projects to build the catalogue structure.' },
    @{ Name = 'Accessible disabled controls'; Text = $js; Pattern = 'setControlDisabled' },
    @{ Name = 'Deterministic attention priority'; Text = $js; Pattern = 'attentionPriority' },
    @{ Name = 'Pending preflight findings'; Text = $js; Pattern = 'compendium-readiness-pending' },
    @{ Name = 'Finding toolbar refresh lock'; Text = $js; Pattern = 'setFindingToolbarAvailability' },
    @{ Name = 'Disabled-output styling'; Text = $css; Pattern = '.compendium-output-actions .btn:disabled' }
)

foreach ($contract in $contracts) {
    if ($contract.Text -notlike "*$($contract.Pattern)*") {
        throw "Phase 23.1 source contract is missing: $($contract.Name)"
    }
}

Write-Host 'Phase 23.1 source/integration contract is present.' -ForegroundColor Green

$node = Get-Command node -ErrorAction SilentlyContinue
if ($null -eq $node) {
    Write-Warning 'Node.js is not installed; JavaScript checks were skipped.'
}
else {
    Push-Location $projectRoot
    try {
        Write-Host 'Running JavaScript syntax check...'
        & node --check '.\wwwroot\js\pages\projects-compendium.js'
        if ($LASTEXITCODE -ne 0) { throw 'projects-compendium.js syntax check failed.' }

        Write-Host 'Running Compendium contract tests...'
        & node --test '.\wwwroot\js\projects\publications-compendium-contract.test.js'
        if ($LASTEXITCODE -ne 0) { throw 'Compendium contract tests failed.' }
    }
    finally {
        Pop-Location
    }
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnet) {
    Write-Warning '.NET SDK is not installed; dotnet build/test were skipped.'
}
else {
    Push-Location $projectRoot
    try {
        if (Test-Path '.\ProjectManagement.csproj') {
            Write-Host 'Running application build...'
            & dotnet build '.\ProjectManagement.csproj'
            if ($LASTEXITCODE -ne 0) { throw 'ProjectManagement build failed.' }
        }

        if (Test-Path '.\ProjectManagement.Tests\ProjectManagement.Tests.csproj') {
            Write-Host 'Running test project...'
            & dotnet test '.\ProjectManagement.Tests\ProjectManagement.Tests.csproj'
            if ($LASTEXITCODE -ne 0) { throw 'ProjectManagement.Tests failed.' }
        }
        else {
            Write-Warning 'ProjectManagement.Tests.csproj was not found; dotnet test was skipped.'
        }
    }
    finally {
        Pop-Location
    }
}

Write-Host ''
Write-Host 'Phase 23.1 validation complete.' -ForegroundColor Green
Write-Host 'No database migration is required for this phase.'
Write-Host 'Recommended runtime: select mixed projects, review one, change crop, verify review invalidation, then save/reload and Preview PDF.'
