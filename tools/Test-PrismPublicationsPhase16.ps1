param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = (Get-Location).Path
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($ProjectRoot)

Write-Host "PRISM Publications Phase 16 render-verified compact publication check" -ForegroundColor Cyan
Write-Host "Project root: $root"

function Resolve-ProjectPath([string]$RelativePath) { Join-Path $root $RelativePath }
function Require-File([string]$RelativePath) {
    $path = Resolve-ProjectPath $RelativePath
    if (-not (Test-Path $path)) { throw "Required Phase 16 file is missing: $RelativePath" }
    return $path
}
function Require-Text([string]$RelativePath, [string]$Pattern, [string]$Description) {
    $path = Require-File $RelativePath
    $text = Get-Content $path -Raw
    if ($text -notmatch $Pattern) { throw "Phase 16 contract missing: $Description`nFile: $RelativePath`nPattern: $Pattern" }
}
function Reject-Text([string]$RelativePath, [string]$Pattern, [string]$Description) {
    $path = Require-File $RelativePath
    $text = Get-Content $path -Raw
    if ($text -match $Pattern) { throw "Obsolete publication behaviour is still present: $Description`nFile: $RelativePath`nPattern: $Pattern" }
}

# Physical PDF verification: the resulting bytes, not the preflight estimate, are the final authority.
Require-File "Utilities\Reporting\BrochurePdfCompositionVerifier.cs" | Out-Null
Require-Text "Utilities\Reporting\BrochurePdfCompositionVerifier.cs" "PdfDocument\.Open" "physical PDF inspection"
Require-Text "Utilities\Reporting\BrochurePdfCompositionVerifier.cs" "ExpectedPageCount" "planned page count diagnostic"
Require-Text "Utilities\Reporting\BrochurePdfCompositionVerifier.cs" "ActualPageCount" "physical page count diagnostic"
Require-Text "Utilities\Reporting\BrochurePdfCompositionVerifier.cs" "page membership changed after rendering" "project-to-page membership verification"
Require-Text "Utilities\Reporting\BrochurePdfReportBuilder.cs" "BrochurePdfCompositionVerifier\.Verify\(pdfBytes, data, printPlan\)" "verification before PDF bytes are returned"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml.cs" "printCompositionMismatch" "409 composition mismatch response"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml.cs" "X-PRISM-Publication-Composition-Verified" "verified-render response header"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml.cs" "X-PRISM-Publication-Page-Count" "physical page-count response header"

# Planner and compositor share conservative geometry instead of planning to the final point of the page.
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ProjectComposerSafetyReservePoints\s*=\s*12f" "physical compositor safety reserve"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ProjectContentCapacity" "shared project-page capacity"
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "ClosingVisionHorizontalPaddingPoints" "shared closing horizontal geometry"
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "ClosingVisionVerticalPaddingPoints" "shared closing vertical geometry"
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "ClosingSectionSpacingPoints" "shared closing section rhythm"

# Compact project pages retain the Phase 16 semantic float safeguard while current brochure typography is configurable.
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "BrochureNarrativeTypographyPolicy\.ShouldJustify" "alignment-aware project typography"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "BrochureNarrativeSegment\.Continuation" "ragged-right forced continuation safeguard"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "ClosingPaper\s*=\s*\"#F3F1E8\"" "neutral closing panel"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "Background\(Forest900\)" "institutional closing heading"
Reject-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "VisionBlue|VisionPaper" "legacy blue/yellow closing treatment"

# UI tells the truth about Cover A, makes publication order quieter, and separates editorial edits from sources.
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "data-cover-a-identity-note" "Cover A identity explanation"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "approved institutional artwork unchanged" "Cover A non-overlay copy"
Require-Text "wwwroot\js\pages\projects-brochure.js" "coverAUsesFullArtworkIdentity" "Cover A metadata-field visibility logic"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "Clear selection" "explicit selection-clearing action"
Require-Text "wwwroot\js\pages\projects-brochure.js" "window\.confirm" "clear-selection confirmation"
Require-Text "wwwroot\js\pages\projects-brochure.js" "dataset\.reorderButton" "quiet reorder controls"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "Publication image" "publication-local image action group"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "Authoritative source" "source-maintenance action group"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "Average page fill" "editorial preflight metric wording"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "Lowest page fill" "editorial preflight lowest-fill wording"
Require-Text "wwwroot\js\pages\projects-brochure.js" "composition verified" "browser confirmation of physical verification"

# Regression coverage for the exact failure class observed in the six-project brochure.
Require-Text "ProjectManagement.Tests\Publications\BrochurePdfReportBuilderTests.cs" "Build_PrintCompact_MixedSixProjectRegression_MatchesPhysicalPlannerExactly" "six-project physical pagination regression"
Require-Text "wwwroot\js\projects\publications-brochure-contract.test.js" "phase 16 post-compose verification" "browser/source verification contract"

Write-Host "Phase 16 source/integration contract is present." -ForegroundColor Green

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
Write-Host "dotnet build .\ProjectManagement.csproj"
Write-Host "dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj"
Write-Host "node --check .\wwwroot\js\pages\projects-brochure.js"
Write-Host "node --test .\wwwroot\js\projects\publications-brochure-contract.test.js"
Write-Host "Then regenerate the same six-project Print / Compact brochure and confirm the Final output status reports composition verified with the same page count shown by Publication readiness."
