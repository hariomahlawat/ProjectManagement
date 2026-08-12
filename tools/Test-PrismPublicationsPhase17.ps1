param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = (Get-Location).Path
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($ProjectRoot)

Write-Host "PRISM Publications Phase 17 publication hardening check" -ForegroundColor Cyan
Write-Host "Project root: $root"

function Resolve-ProjectPath([string]$RelativePath) { Join-Path $root $RelativePath }
function Require-File([string]$RelativePath) {
    $path = Resolve-ProjectPath $RelativePath
    if (-not (Test-Path $path)) { throw "Required Phase 17 file is missing: $RelativePath" }
    return $path
}
function Require-Text([string]$RelativePath, [string]$Pattern, [string]$Description) {
    $path = Require-File $RelativePath
    $text = Get-Content $path -Raw
    if ($text -notmatch $Pattern) { throw "Phase 17 contract missing: $Description`nFile: $RelativePath`nPattern: $Pattern" }
}
function Reject-Text([string]$RelativePath, [string]$Pattern, [string]$Description) {
    $path = Require-File $RelativePath
    $text = Get-Content $path -Raw
    if ($text -match $Pattern) { throw "Obsolete publication behaviour is still present: $Description`nFile: $RelativePath`nPattern: $Pattern" }
}

# Editorial terminology: the PDF planner describes pages, not physical duplex sheets.
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "Planned pages" "page-count terminology"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "Final page composition" "final-page terminology"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "<span>Pages</span><strong data-smart-flow-pages>" "Smart Flow page terminology"
Reject-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "Planned sheets|Final-sheet composition" "legacy sheet terminology"
Require-Text "Services\Publications\BrochurePrintPagePlanner.cs" "-page composition is available at 9 pt" "Smart Flow page summary"
Require-Text "Services\Publications\BrochurePublicationService.cs" "dedicated closing page" "closing-page preflight wording"

# Final output: physical verification survives after the preview/download call and is invalidated by geometry changes.
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "data-output-verification" "persistent verification slot"
Require-Text "wwwroot\js\pages\projects-brochure.js" "lastVerifiedPdf = \{ verified: true, pageCount: physicalPageCount \}" "verified PDF state"
Require-Text "wwwroot\js\pages\projects-brochure.js" "PDF verified · \$\{pages\} page" "verified page-count display"
Require-Text "wwwroot\js\pages\projects-brochure.js" "lastVerifiedPdf = null" "verification invalidation"
Require-Text "wwwroot\css\pages\projects-publications.css" "\.brochure-output-verification" "verification status styling"

# Image findings now lead directly to the publication image editor and use effective cropped DPI.
Require-Text "wwwroot\js\pages\projects-brochure.js" "Fix image" "direct image repair action"
Require-Text "wwwroot\js\pages\projects-brochure.js" "openPhotoEditor\(projectId, \"select\"\)" "direct image editor launch"
Reject-Text "wwwroot\js\pages\projects-brochure.js" "Configure image|locate\.textContent = \"Locate\"" "old multi-step image warning workflow"
Require-File "Services\Publications\BrochurePhotoPrintQualityEvaluator.cs" | Out-Null
Require-Text "Services\Publications\BrochurePhotoPrintQualityEvaluator.cs" "EffectiveCropDimensions" "crop-aware quality evaluation"
Require-Text "Services\Publications\BrochurePhotoPrintQualityEvaluator.cs" "PrintCompactRecommendedDpi = 240d" "hard-copy effective DPI threshold"
Require-Text "Services\Publications\BrochurePhotoPrintQualityEvaluator.cs" "DigitalComfortableRecommendedDpi = 180d" "screen-first effective DPI threshold"
Require-Text "Services\Publications\BrochurePublicationService.cs" "BrochurePhotoPrintQualityEvaluator\.Assess" "preflight uses effective DPI evaluator"
Require-Text "Services\Publications\BrochurePublicationService.cs" "effective dpi" "effective-DPI warning copy"

# Readability is now a hard constraint: even emergency compact geometry stays at 9 pt body copy.
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ProjectBodyMinimumFontSize = 9f" "9 pt body-copy floor"
Require-Text "Services\Publications\BrochureContracts.cs" "body copy remains 9 pt" "compact variant contract"
Require-Text "ProjectManagement.Tests\Publications\BrochurePrintCompactPlannerTests.cs" "EmergencyCompactGeometryStillPreservesNinePointBodyCopy" "9 pt emergency regression"
Require-Text "ProjectManagement.Tests\Publications\BrochurePhotoPrintQualityEvaluatorTests.cs" "AcceptsOneThousandTwentyFourPixelSquare" "1024 px compact-frame DPI regression"

# Copy is concise and grammatically correct.
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "Layout is measured using final PDF geometry and verified again after composition" "concise verification explanation"
Require-Text "wwwroot\js\pages\projects-brochure.js" "pendingApprovals === 1 \? \"requires\" : \"require\"" "client singular/plural approval grammar"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml.cs" "unreviewed == 1 \? \"requires\" : \"require\"" "server singular/plural approval grammar"

Write-Host "Phase 17 source/integration contract is present." -ForegroundColor Green

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
Write-Host "Then preview the same nine-project Print / Compact brochure. Confirm: Planned pages matches the physical PDF; Final output shows 'PDF verified · N pages'; only genuinely low effective-DPI images remain as warnings; and project body copy never drops below 9 pt."
