$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $projectRoot

Write-Host 'PRISM Publications Phase 19A — Print / Compact finalisation check'
Write-Host "Project root: $projectRoot"

$required = @(
    '.\Pages\Projects\Publications\Brochure\Index.cshtml',
    '.\Services\Publications\BrochurePrintLayoutMetrics.cs',
    '.\Services\Publications\BrochurePrintMeasurementService.cs',
    '.\Utilities\Reporting\BrochurePrintCompactComposer.cs',
    '.\wwwroot\css\pages\projects-publications.css',
    '.\wwwroot\js\pages\projects-brochure.js',
    '.\wwwroot\js\projects\publications-brochure-contract.test.js',
    '.\ProjectManagement.Tests\Publications\BrochurePrintCompactPlannerTests.cs'
)

$missing = $required | Where-Object { -not (Test-Path $_) }
if ($missing.Count -gt 0) {
    throw "Phase 19A source/integration contract is incomplete. Missing: $($missing -join ', ')"
}

$view = Get-Content '.\Pages\Projects\Publications\Brochure\Index.cshtml' -Raw
$client = Get-Content '.\wwwroot\js\pages\projects-brochure.js' -Raw
$metrics = Get-Content '.\Services\Publications\BrochurePrintLayoutMetrics.cs' -Raw
$composer = Get-Content '.\Utilities\Reporting\BrochurePrintCompactComposer.cs' -Raw

if ($view -notmatch 'data-smart-flow-title') { throw 'Smart Flow state title hook is missing.' }
if ($view -notmatch 'data-smart-flow-note') { throw 'Smart Flow state note hook is missing.' }
if ($view -notmatch 'data-preset-load disabled') { throw 'Unsaved brochure Load control is not initially disabled.' }
if ($client -notmatch 'Smart Flow applied') { throw 'Applied Smart Flow state is missing.' }
if ($client -notmatch 'updatePresetLoadState') { throw 'Saved brochure Load-state logic is missing.' }
if ($metrics -notmatch 'ClosingVisionBorderPoints = 2f') { throw 'Heritage closing-frame metric is missing.' }
if ($composer -notmatch 'ClosingCream = "#FBF4D8"') { throw 'Heritage closing cream is missing.' }
if ($composer -notmatch 'ClosingNavy = "#173F63"') { throw 'Heritage closing navy is missing.' }

Write-Host 'Phase 19A source/integration contract is present.'

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
Write-Host 'Phase 19A validation completed successfully.'
Write-Host 'No database migration is required for this phase.'
