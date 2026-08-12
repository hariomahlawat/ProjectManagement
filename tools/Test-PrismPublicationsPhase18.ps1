$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $projectRoot

Write-Host 'PRISM Publications Phase 18 integration check'
Write-Host "Project root: $projectRoot"

$required = @(
    '.\Models\Publications\BrochurePreset.cs',
    '.\Services\Publications\BrochurePresetContracts.cs',
    '.\Services\Publications\BrochurePresetService.cs',
    '.\Migrations\20261208100000_AddSharedBrochurePresets.cs',
    '.\Pages\Projects\Publications\Brochure\Index.cshtml',
    '.\Pages\Projects\Publications\Brochure\Index.cshtml.cs',
    '.\wwwroot\js\pages\projects-brochure.js',
    '.\wwwroot\js\projects\publications-brochure-contract.test.js',
    '.\ProjectManagement.Tests\Publications\BrochurePresetServiceTests.cs'
)

$missing = $required | Where-Object { -not (Test-Path $_) }
if ($missing.Count -gt 0) {
    throw "Phase 18 source/integration contract is incomplete. Missing: $($missing -join ', ')"
}

$migrationId = '20261208100000_AddSharedBrochurePresets'
$manifest = Get-Content '.\Migrations\immutable-migration-ids.txt'
if (($manifest | Where-Object { $_ -eq $migrationId }).Count -ne 1) {
    throw "Migration manifest must contain exactly one '$migrationId' entry."
}

$view = Get-Content '.\Pages\Projects\Publications\Brochure\Index.cshtml' -Raw
if ($view -match 'Offline PDF') {
    throw 'The retired Offline PDF badge is still present in the Brochure Builder view.'
}
if ($view -notmatch 'data-brochure-preset-control') {
    throw 'The compact Saved brochure workspace control is missing.'
}

Write-Host 'Phase 18 source/integration contract is present.'

if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    throw 'Node.js is required for the brochure client checks.'
}

node --check '.\wwwroot\js\pages\projects-brochure.js'
node --test '.\wwwroot\js\projects\publications-brochure-contract.test.js'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK is required for the server build and test checks.'
}

dotnet build '.\ProjectManagement.csproj'
dotnet test '.\ProjectManagement.Tests\ProjectManagement.Tests.csproj'

Write-Host ''
Write-Host 'Phase 18 validation completed successfully.'
Write-Host 'Database migration is intentionally not applied by this validation script.'
Write-Host 'Apply it through your normal deployment migration process after validation.'
