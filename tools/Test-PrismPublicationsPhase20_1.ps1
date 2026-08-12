$ErrorActionPreference = 'Stop'

Write-Host 'PRISM Phase 20.1 compile/nullability hotfix check'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path (Join-Path $root 'ProjectManagement.csproj'))) {
    $root = (Get-Location).Path
}

$publicationService = Join-Path $root 'Services\Publications\BrochurePublicationService.cs'
$actionIndex = Join-Path $root 'Pages\ActionTasks\Index.cshtml.cs'
$brochureJs = Join-Path $root 'wwwroot\js\pages\projects-brochure.js'
$brochureTests = Join-Path $root 'wwwroot\js\projects\publications-brochure-contract.test.js'

if (-not (Test-Path $publicationService)) { throw "Missing $publicationService" }
if (-not (Test-Path $actionIndex)) { throw "Missing $actionIndex" }

$serviceText = Get-Content $publicationService -Raw
if ($serviceText -match '\.Fragments\b') {
    throw 'BrochurePublicationService still references BrochurePagePlan.Fragments.'
}
if ($serviceText -notmatch 'foreach\s*\(var\s+fragment\s+in\s+page\.Items\)') {
    throw 'Expected BrochurePagePlan.Items enumeration was not found.'
}

$actionText = Get-Content $actionIndex -Raw
if ($actionText -notmatch 'public\s+string\s+ResolveActorName\(string\?\s+performedByUserId\)') {
    throw 'ResolveActorName must accept nullable string to satisfy Func<string?, string>.'
}
if ($actionText -notmatch 'string\.IsNullOrWhiteSpace\(performedByUserId\)') {
    throw 'ResolveActorName nullable guard is missing.'
}

Write-Host 'PASS source-contract checks'

if (Get-Command node -ErrorAction SilentlyContinue) {
    if (Test-Path $brochureJs) {
        & node --check $brochureJs
        if ($LASTEXITCODE -ne 0) { throw 'node --check failed.' }
    }
    if (Test-Path $brochureTests) {
        Push-Location $root
        try {
            & node --test $brochureTests
            if ($LASTEXITCODE -ne 0) { throw 'Brochure JS contract tests failed.' }
        }
        finally { Pop-Location }
    }
}

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    Push-Location $root
    try {
        & dotnet build .\ProjectManagement.csproj
        if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
        if (Test-Path '.\ProjectManagement.Tests\ProjectManagement.Tests.csproj') {
            & dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj --no-build
            if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }
        }
    }
    finally { Pop-Location }
}
else {
    Write-Warning '.NET SDK not found; run dotnet build/test on the development machine.'
}

Write-Host 'Phase 20.1 checks completed.'
