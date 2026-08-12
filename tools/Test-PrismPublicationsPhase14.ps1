param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = (Get-Location).Path
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($ProjectRoot)

Write-Host "PRISM Publications Phase 14 adaptive editorial-pagination check" -ForegroundColor Cyan
Write-Host "Project root: $root"

function Resolve-ProjectPath([string]$RelativePath) { Join-Path $root $RelativePath }
function Require-File([string]$RelativePath) {
    $path = Resolve-ProjectPath $RelativePath
    if (-not (Test-Path $path)) { throw "Required Phase 14 file is missing: $RelativePath" }
    return $path
}
function Require-Text([string]$RelativePath, [string]$Pattern, [string]$Description) {
    $path = Require-File $RelativePath
    $text = Get-Content $path -Raw
    if ($text -notmatch $Pattern) { throw "Phase 14 contract missing: $Description`nFile: $RelativePath`nPattern: $Pattern" }
}
function Reject-Text([string]$RelativePath, [string]$Pattern, [string]$Description) {
    $path = Require-File $RelativePath
    $text = Get-Content $path -Raw
    if ($text -match $Pattern) { throw "Obsolete publication behaviour is still present: $Description`nFile: $RelativePath`nPattern: $Pattern" }
}

# Offline cover library and explicit full-artwork/background-only identity contract.
$coverAssets = @(
    "wwwroot\img\publications\covers\cover-a-reference-original.jpg",
    "wwwroot\img\publications\covers\cover-a-premium-green-gold.jpg",
    "wwwroot\img\publications\covers\cover-a-cinematic-cyber.jpg",
    "wwwroot\img\publications\covers\cover-a-executive-teal.jpg",
    "wwwroot\img\publications\covers\cover-a-luminous-halo.jpg"
)
$coverAssets | ForEach-Object { Require-File $_ | Out-Null }
Require-File "Services\Publications\BrochureInstitutionalCoverArtworkCatalog.cs" | Out-Null
Require-Text "Services\Publications\BrochureInstitutionalCoverArtworkCatalog.cs" "FullArtwork" "reference artwork identity mode"
Require-Text "Services\Publications\BrochureInstitutionalCoverArtworkCatalog.cs" "BackgroundOnly" "generated background-only identity mode"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "ComposeOfficialInstitutionalMarks" "exact official identity overlay"

# Reference geometry and non-negotiable typography floor.
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ReferenceWidthPoints\s*=\s*423\.23f" "reference sheet width"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ReferenceHeightPoints\s*=\s*846\.755f" "reference sheet height"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ProjectBodyPreferredFontSize\s*=\s*9f" "9 pt normal project body"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "BrochurePrintLayoutVariant\.Dense" "genuine dense 9 pt geometry"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "AdaptiveImageMinimumPoints\s*=\s*132f" "adaptive image minimum"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "AdaptiveImageMaximumPoints\s*=\s*156f" "adaptive image maximum"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "MaximumParetoCandidatesPerProject" "bounded Pareto candidate frontier"

# Adaptive candidate measurement, Automatic Single/Gallery choice and semantic float continuation.
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "GenerateProjectCandidates" "adaptive project candidate generation"
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "ParetoFilter" "Pareto filtering"
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "new\[\]\s*\{\s*false,\s*true\s*\}" "Automatic single/gallery alternatives"
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "BrochureFloatSplitKind\.Paragraph" "paragraph float boundary"
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "BrochureFloatSplitKind\.Sentence" "sentence float boundary"
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "BrochureFloatSplitKind\.Word" "word fallback boundary"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "layout\.UsesSecondaryImage" "renderer consumes planner-selected image treatment"

# Smart Flow is advisory only and user order stays authoritative until explicitly applied.
Require-Text "Services\Publications\BrochurePrintPagePlanner.cs" "PlanWithSmartFlow" "Smart Flow comparison planner"
Require-Text "Services\Publications\BrochurePrintPagePlanner.cs" "SmartFlowMaximumMoveDistance" "bounded local reordering"
Require-Text "Services\Publications\BrochurePrintPagePlanner.cs" "TotalPositionShift" "order movement penalty"
Require-Text "Services\Publications\BrochurePrintPagePlanner.cs" "EstimatedTotalPageCount\s*\*\s*1_000_000d" "page count dominates Smart Flow objective"
Require-Text "Services\Publications\BrochurePublicationService.cs" "PrintSmartFlowAvailable" "actionable preflight recommendation"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "data-smart-flow-apply" "explicit Apply suggested order action"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "data-smart-flow-undo" "Undo order action"
Require-Text "wwwroot\js\pages\projects-brochure.js" "applySmartFlow" "client applies recommendation only after user action"
Require-Text "wwwroot\js\pages\projects-brochure.js" "undoSmartFlow" "client restores prior order"
Require-Text "wwwroot\js\pages\projects-brochure.js" "adaptiveTreatmentSummary" "transparent adaptive-treatment explanation"

# 8.5 pt remains emergency-only; residual pass may polish spacing but cannot repaginate or enlarge images.
Require-Text "Services\Publications\BrochurePrintPagePlanner.cs" "requiresEmergency" "emergency compact gate"
Require-Text "Services\Publications\BrochurePrintPagePlanner.cs" "ApplyResidualPolish" "final residual polish"
Reject-Text "Services\Publications\BrochurePrintPagePlanner.cs" "ResidualImageExpansionStepPoints" "post-plan image resizing"
Reject-Text "Services\Publications\BrochurePrintPagePlanner.cs" "ResidualMaximumImageExpansionPoints" "post-plan image expansion"

# Planner/renderer physical geometry remains deterministic.
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "\.Height\(plannedProject\.Measurement\.TotalHeightPoints\s*\+\s*sheet\.ExtraModuleVerticalPaddingPoints\)" "exact planned module height"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "if \(projectOffset > 0\)" "explicit project spacer"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "Height\(BrochurePrintLayoutMetrics\.ClosingGapPoints\)" "explicit closing spacer"
Reject-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "column\.Spacing\(BrochurePrintLayoutMetrics\.InterModuleSpacingPoints" "hidden global sheet spacing"

# Regression coverage for dense 9 pt packing, Smart Flow, Automatic mode and current 9-project stress class.
Require-Text "ProjectManagement.Tests\Publications\BrochurePrintMeasurementServiceTests.cs" "GenerateProjectCandidates_AutomaticMayUseSingleOrGallery" "Automatic image-mode measurement regression"
Require-Text "ProjectManagement.Tests\Publications\BrochurePrintMeasurementServiceTests.cs" "MeasureProject_DenseCompactsGeometryWithoutReducingBodyFont" "dense 9 pt regression"
Require-Text "ProjectManagement.Tests\Publications\BrochurePrintCompactPlannerTests.cs" "Plan_DenseNinePointCandidates_EnableFourUpWithoutReducingTypography" "four-up dense 9 pt planner regression"
Require-Text "ProjectManagement.Tests\Publications\BrochurePrintCompactPlannerTests.cs" "PlanWithSmartFlow_ReturnsSuggestionWithoutMutatingCurrentPlan" "advisory Smart Flow regression"
Require-Text "ProjectManagement.Tests\Publications\BrochurePrintCompactPlannerTests.cs" "Plan_NineProjectStressFixture_UsesDenseNinePointGeometryBeforeAddingAProjectSheet" "nine-project stress regression"

Write-Host "Phase 14 source/integration contract is present." -ForegroundColor Green

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
    finally { Pop-Location }
}
else { Write-Warning "Node.js is not available; JavaScript checks were skipped." }

Write-Host "Recommended final checks:" -ForegroundColor Yellow
Write-Host "dotnet restore .\ProjectManagement.csproj"
Write-Host "dotnet build .\ProjectManagement.csproj"
Write-Host "dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj"
Write-Host "Then regenerate the same nine-project brochure. Compare current order and Smart Flow recommendation; apply the recommendation only if the editorial sequence remains acceptable."
