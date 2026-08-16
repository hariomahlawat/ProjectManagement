$ErrorActionPreference = 'Stop'

Write-Host 'PRISM Publications Phase 37.8 integration check' -ForegroundColor Cyan
$projectRoot = Split-Path -Parent $PSScriptRoot
Write-Host "Project root: $projectRoot"

$required = @(
    'Pages\Projects\Publications\Index.cshtml',
    'Pages\Projects\Publications\Compendium\Index.cshtml',
    'wwwroot\js\pages\projects-compendium.js',
    'wwwroot\js\pages\projects-compendium-cover-editor.js',
    'wwwroot\js\pages\projects-compendium-structure-editor.js',
    'wwwroot\css\pages\projects-publications.css',
    'wwwroot\js\projects\publications-compendium-phase37-8-contract.test.js'
)

foreach ($relative in $required) {
    $path = Join-Path $projectRoot $relative
    if (-not (Test-Path $path)) { throw "Missing Phase 37.8 file: $relative" }
}

Write-Host 'Phase 37.8 source/integration contract is present.' -ForegroundColor Green
Write-Host 'Recommended final checks:'
Write-Host 'dotnet build .\ProjectManagement.csproj'
Write-Host 'dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj'
Write-Host 'node --check .\wwwroot\js\pages\projects-compendium.js'
Write-Host 'node --check .\wwwroot\js\pages\projects-compendium-cover-editor.js'
Write-Host 'node --check .\wwwroot\js\pages\projects-compendium-structure-editor.js'
Write-Host 'node --test .\wwwroot\js\projects\publications-compendium-*.test.js'
