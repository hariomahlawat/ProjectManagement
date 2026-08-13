$ErrorActionPreference = 'Stop'

Write-Host 'PRISM Publications Phase 24 integration check' -ForegroundColor Cyan

$projectRoot = Split-Path -Parent $PSScriptRoot
Write-Host "Project root: $projectRoot"

$required = @(
    'Utilities\Reporting\CompendiumLayoutMetrics.cs',
    'Utilities\Reporting\CompendiumPagePlanner.cs',
    'Utilities\Reporting\CompendiumPdfCompositionVerifier.cs',
    'Utilities\Reporting\CompendiumPdfReportBuilder.cs',
    'Services\Compendiums\CompendiumDtos.cs',
    'Services\Compendiums\CompendiumExportService.cs',
    'Services\Compendiums\ICompendiumExportService.cs',
    'Services\Compendiums\CompendiumReadinessPolicy.cs',
    'Services\Publications\PublicationServiceCollectionExtensions.cs',
    'Pages\Projects\Publications\Compendium\Index.cshtml',
    'Pages\Projects\Publications\Compendium\Index.cshtml.cs',
    'wwwroot\js\pages\projects-compendium.js',
    'wwwroot\css\pages\projects-publications.css',
    'wwwroot\js\projects\publications-compendium-contract.test.js',
    'ProjectManagement.Tests\Publications\CompendiumPhase24ContractTests.cs'
)

foreach ($relative in $required) {
    $path = Join-Path $projectRoot $relative
    if (-not (Test-Path $path)) {
        throw "Required Phase 24 file is missing: $relative"
    }
}

$planner = Get-Content (Join-Path $projectRoot 'Utilities\Reporting\CompendiumPagePlanner.cs') -Raw
$metrics = Get-Content (Join-Path $projectRoot 'Utilities\Reporting\CompendiumLayoutMetrics.cs') -Raw
$verifier = Get-Content (Join-Path $projectRoot 'Utilities\Reporting\CompendiumPdfCompositionVerifier.cs') -Raw
$builder = Get-Content (Join-Path $projectRoot 'Utilities\Reporting\CompendiumPdfReportBuilder.cs') -Raw
$export = Get-Content (Join-Path $projectRoot 'Services\Compendiums\CompendiumExportService.cs') -Raw
$dto = Get-Content (Join-Path $projectRoot 'Services\Compendiums\CompendiumDtos.cs') -Raw
$page = Get-Content (Join-Path $projectRoot 'Pages\Projects\Publications\Compendium\Index.cshtml.cs') -Raw
$js = Get-Content (Join-Path $projectRoot 'wwwroot\js\pages\projects-compendium.js') -Raw
$registrations = Get-Content (Join-Path $projectRoot 'Services\Publications\PublicationServiceCollectionExtensions.cs') -Raw

$contracts = @(
    @{ Name = 'A4 physical geometry'; Text = $metrics; Pattern = 'ProjectImageHeightPoints = 214f' },
    @{ Name = 'Deterministic page planner'; Text = $planner; Pattern = 'CompendiumPageKind.ProjectContinuation' },
    @{ Name = 'Multi-page index planning'; Text = $planner; Pattern = 'IndexPageRowUnits' },
    @{ Name = 'Physical PDF verification'; Text = $verifier; Pattern = 'PdfDocument.Open' },
    @{ Name = 'Verified project placement'; Text = $verifier; Pattern = 'Project section' },
    @{ Name = 'Text-led no-photo layout'; Text = $builder; Pattern = 'ComposeNoPhotoTreatment' },
    @{ Name = 'Continuation composition'; Text = $builder; Pattern = 'Project description · continued' },
    @{ Name = 'Reviewed image render geometry'; Text = $dto; Pattern = 'RenderWidthPixels = 1800' },
    @{ Name = 'Planner before renderer'; Text = $export; Pattern = '_pagePlanner.Plan(context)' },
    @{ Name = 'Verifier after renderer'; Text = $export; Pattern = '_compositionVerifier.Verify(pdfBytes, context, plan)' },
    @{ Name = 'Final issue review gate'; Text = $page; Pattern = 'RequireAllReviewed: !preview' },
    @{ Name = 'Verification response headers'; Text = $page; Pattern = 'X-PRISM-Publication-Composition-Verified' },
    @{ Name = 'Preview/download parity'; Text = $js; Pattern = 'requestPdf' },
    @{ Name = 'Verified page-count UI'; Text = $js; Pattern = 'PDF verified ·' },
    @{ Name = 'Planner DI registration'; Text = $registrations; Pattern = 'AddSingleton<ICompendiumPagePlanner, CompendiumPagePlanner>' },
    @{ Name = 'Verifier DI registration'; Text = $registrations; Pattern = 'AddSingleton<ICompendiumPdfCompositionVerifier, CompendiumPdfCompositionVerifier>' }
)

foreach ($contract in $contracts) {
    if ($contract.Text -notlike "*$($contract.Pattern)*") {
        throw "Phase 24 source contract is missing: $($contract.Name)"
    }
}

Write-Host 'Phase 24 source/integration contract is present.' -ForegroundColor Green

$node = Get-Command node -ErrorAction SilentlyContinue
if ($null -eq $node) {
    Write-Warning 'Node.js is not installed; JavaScript checks were skipped.'
}
else {
    Push-Location $projectRoot
    try {
        Write-Host 'Running Compendium JavaScript syntax check...'
        & node --check '.\wwwroot\js\pages\projects-compendium.js'
        if ($LASTEXITCODE -ne 0) { throw 'projects-compendium.js syntax check failed.' }

        Write-Host 'Running Compendium publication contract suite...'
        & node --test '.\wwwroot\js\projects\publications-compendium-contract.test.js'
        if ($LASTEXITCODE -ne 0) { throw 'Compendium publication contract tests failed.' }
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
        if (-not (Test-Path '.\ProjectManagement.csproj')) {
            throw 'ProjectManagement.csproj was not found.'
        }

        Write-Host 'Running application build...'
        & dotnet build '.\ProjectManagement.csproj'
        if ($LASTEXITCODE -ne 0) { throw 'ProjectManagement build failed.' }

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
Write-Host 'Phase 24 validation complete.' -ForegroundColor Green
Write-Host 'No EF Core migration or database change is required for Phase 24.'
Write-Host 'Runtime check: Preview a mixed-project Compendium, verify the new cover/index/project/back-cover design, then confirm the UI reports PDF verified with the same physical page count.'
