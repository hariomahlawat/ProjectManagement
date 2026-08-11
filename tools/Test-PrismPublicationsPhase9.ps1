param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = (Get-Location).Path
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($ProjectRoot)

Write-Host "PRISM Publications Phase 9 integration check" -ForegroundColor Cyan
Write-Host "Project root: $root"

function Resolve-ProjectPath([string]$RelativePath) {
    return Join-Path $root $RelativePath
}

function Require-File([string]$RelativePath) {
    $path = Resolve-ProjectPath $RelativePath
    if (-not (Test-Path $path)) {
        throw "Required Phase 9 file is missing: $RelativePath"
    }
    return $path
}

function Require-Text([string]$RelativePath, [string]$Pattern, [string]$Description) {
    $path = Require-File $RelativePath
    $text = Get-Content $path -Raw
    if ($text -notmatch $Pattern) {
        throw "Phase 9 contract missing: $Description`nFile: $RelativePath`nPattern: $Pattern"
    }
}

function Reject-Text([string]$RelativePath, [string]$Pattern, [string]$Description) {
    $path = Require-File $RelativePath
    $text = Get-Content $path -Raw
    if ($text -match $Pattern) {
        throw "Obsolete Phase 8 contract is still present: $Description`nFile: $RelativePath`nPattern: $Pattern"
    }
}

# Measured print architecture.
Require-File "Services\Publications\BrochurePrintLayoutMetrics.cs" | Out-Null
Require-File "Services\Publications\BrochurePrintMeasurementService.cs" | Out-Null
Require-File "Services\Publications\BrochurePrintPagePlanner.cs" | Out-Null
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "SKPaint" "font-aware SkiaSharp measurement"
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "MeasureText\(" "actual glyph-width measurement"
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "DMSans-Regular\.ttf" "offline DM Sans measurement source"
Reject-Text "Services\Publications\BrochurePrintMeasurementService.cs" "wordsPerLine" "word-count/words-per-line height heuristic"
Require-Text "Services\Publications\BrochurePrintPagePlanner.cs" "MaximumProjectsPerSheet" "measured maximum four-project sheet planning"
Require-Text "Services\Publications\BrochurePrintPagePlanner.cs" "TryPlanWithSharedClosing" "measured closing-aware final sheet"
Require-Text "Services\Publications\BrochurePrintPagePlanner.cs" "SheetPlan" "per-sheet print plan"

# DI/runtime graph.
Require-Text "Services\Publications\PublicationServiceCollectionExtensions.cs" "AddSingleton<IBrochurePrintMeasurementService, BrochurePrintMeasurementService>" "measurement-service registration"
Require-Text "Services\Publications\PublicationServiceCollectionExtensions.cs" "AddSingleton<IBrochurePrintPagePlanner, BrochurePrintPagePlanner>" "page-planner registration"
Require-Text "Services\Publications\PublicationRuntimeValidationHostedService.cs" "GetRequiredService<IBrochurePrintMeasurementService>" "startup measurement-service validation"
Require-Text "Services\Publications\PublicationRuntimeValidationHostedService.cs" "GetRequiredService<IBrochurePrintPagePlanner>" "startup page-planner validation"

# Renderer and Cover A.
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "BrochurePrintCompactPlan plan" "renderer consumes measured sheet plan"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "frontPlan\.HeroHeightPoints" "measured front-page hero"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "frontPlan\.BodyBlockHeightPoints" "measured front-page body"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "frontPlan\.ContactBlockHeightPoints" "measured front-page contact block"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "ComposeInstitutionalFallbackArtwork" "controlled institutional-artwork fallback"
Reject-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "PaddingTop\(326\)" "fixed Phase 8 front-body spacer"
Reject-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "Height\(98\)" "fixed Phase 8 contact height"

# Preflight / UI plan diagnostics.
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "data-print-sheet-map" "measured sheet-map UI"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "data-print-lowest-fill" "lowest project-sheet fill"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "data-print-final-fill" "final-sheet fill"
Require-Text "wwwroot\js\pages\projects-brochure.js" "result\.printSheetPlan" "client measured sheet plan rendering"
Require-Text "wwwroot\js\pages\projects-brochure.js" 'preferredValue = isPrintCompactProfile\(\) \? "1" : "2"' "Print A / Digital B smart defaults"

# Tests.
Require-File "ProjectManagement.Tests\Publications\BrochurePrintMeasurementServiceTests.cs" | Out-Null
Require-File "ProjectManagement.Tests\Publications\BrochurePrintCompactPlannerTests.cs" | Out-Null
Require-Text "ProjectManagement.Tests\Publications\PublicationsRuntimeIntegrationTests.cs" "IBrochurePrintMeasurementService" "runtime DI regression"
Require-Text "ProjectManagement.Tests\Publications\PublicationsRuntimeIntegrationTests.cs" "IBrochurePrintPagePlanner" "planner DI regression"

Write-Host "Phase 9 source/integration contract is present." -ForegroundColor Green

$node = Get-Command node -ErrorAction SilentlyContinue
if ($node) {
    Write-Host "Running JavaScript syntax and publication contract tests..." -ForegroundColor Cyan
    & node --check (Resolve-ProjectPath "wwwroot\js\pages\projects-brochure.js")
    if ($LASTEXITCODE -ne 0) { throw "projects-brochure.js syntax check failed." }

    Push-Location $root
    try {
        & node --test ".\wwwroot\js\projects\publications-brochure-contract.test.js"
        if ($LASTEXITCODE -ne 0) { throw "Publications browser/source contract tests failed." }
    }
    finally {
        Pop-Location
    }
}
else {
    Write-Warning "Node.js is not available; JavaScript checks were skipped."
}

Write-Host "Recommended final checks:" -ForegroundColor Yellow
Write-Host "dotnet build .\ProjectManagement.csproj"
Write-Host "dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj"
