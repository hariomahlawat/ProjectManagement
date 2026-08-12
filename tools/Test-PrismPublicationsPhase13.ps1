param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = (Get-Location).Path
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($ProjectRoot)

Write-Host "PRISM Publications Phase 13 deterministic print-composition check" -ForegroundColor Cyan
Write-Host "Project root: $root"

function Resolve-ProjectPath([string]$RelativePath) { Join-Path $root $RelativePath }
function Require-File([string]$RelativePath) {
    $path = Resolve-ProjectPath $RelativePath
    if (-not (Test-Path $path)) { throw "Required Phase 13 file is missing: $RelativePath" }
    return $path
}
function Require-Text([string]$RelativePath, [string]$Pattern, [string]$Description) {
    $path = Require-File $RelativePath
    $text = Get-Content $path -Raw
    if ($text -notmatch $Pattern) { throw "Phase 13 contract missing: $Description`nFile: $RelativePath`nPattern: $Pattern" }
}
function Reject-Text([string]$RelativePath, [string]$Pattern, [string]$Description) {
    $path = Require-File $RelativePath
    $text = Get-Content $path -Raw
    if ($text -match $Pattern) { throw "Obsolete publication behaviour is still present: $Description`nFile: $RelativePath`nPattern: $Pattern" }
}

# Curated institutional cover library remains available offline.
$coverAssets = @(
    "wwwroot\img\publications\covers\cover-a-reference-original.jpg",
    "wwwroot\img\publications\covers\cover-a-premium-green-gold.jpg",
    "wwwroot\img\publications\covers\cover-a-cinematic-cyber.jpg",
    "wwwroot\img\publications\covers\cover-a-executive-teal.jpg",
    "wwwroot\img\publications\covers\cover-a-luminous-halo.jpg"
)
$coverAssets | ForEach-Object { Require-File $_ | Out-Null }

# Canonical reference geometry and normal publication quality floor.
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ReferenceWidthPoints\s*=\s*423\.23f" "reference sheet width"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ReferenceHeightPoints\s*=\s*846\.755f" "reference sheet height"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ProjectBodyPreferredFontSize\s*=\s*9f" "9 pt normal project body"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ProjectParagraphSpacingPoints\s*=\s*2\.25f" "compact measured paragraph rhythm"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ResidualMaximumImageWidthPoints\s*=\s*160f" "reference image-width cap"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ResidualMaximumImageExpansionPoints\s*=\s*12f" "bounded residual image expansion"
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "Math\.Max\(\s*18f" "18 pt single-line title band floor"

# Planner and QuestPDF renderer consume one exact geometry contract.
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "\.Height\(plannedProject\.Measurement\.TotalHeightPoints\s*\+\s*sheet\.ExtraModuleVerticalPaddingPoints\)" "fixed measured module height"
Reject-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "\.MinHeight\(plannedProject\.Measurement\.TotalHeightPoints" "old planner/renderer height mismatch"
Require-Text "Services\Publications\BrochurePrintPagePlanner.cs" "WorstResidualFraction" "worst-dead-tail optimisation"
Require-Text "Services\Publications\BrochurePrintPagePlanner.cs" "pageCount\s*<\s*existing\.PageCount" "page count minimised after hard quality constraints"
Require-Text "Services\Publications\BrochurePrintPagePlanner.cs" "requiresEmergencyCompact" "Compact restricted to individually oversized projects"
Require-Text "Services\Publications\BrochurePrintPagePlanner.cs" "itemCount\s*==\s*1\s*&&\s*requiresEmergencyCompact" "8.5 pt cannot be used to pack a normal multi-project sheet"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "if \(projectOffset > 0\)" "explicit project-to-project spacer contract"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "Height\(BrochurePrintLayoutMetrics\.ClosingGapPoints\)" "explicit project-to-closing spacer contract"
Reject-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "column\.Spacing\(BrochurePrintLayoutMetrics\.InterModuleSpacingPoints" "global sheet spacing that would add hidden closing gaps"
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "newSimulatorInnerWidth\s*=\s*outerWidth\s*-\s*16f" "closing New Simulators width matches 8 pt horizontal renderer padding"

# Semantic float continuation and exact paragraph spacing.
Require-Text "Services\Publications\BrochureContracts.cs" "BrochureFloatSplitKind" "explicit float split semantics"
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "BrochureFloatSplitKind\.Paragraph" "paragraph boundary split"
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "BrochureFloatSplitKind\.Sentence" "sentence boundary split"
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "BrochureFloatSplitKind\.Word" "word fallback split"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "layout\.RemainderGapPoints" "measured continuation gap consumed by renderer"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "ComposeNarrativeText" "explicit paragraph compositor"

# Formal identity is overlaid from exact PRISM assets for generated alternatives.
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "ComposeOfficialInstitutionalMarks" "official identity overlay for generated institutional artwork"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "InstitutionalCoverArtwork\s*!=\s*BrochureInstitutionalCoverArtwork\.ReferenceOriginal" "reference artwork left untouched"

# Regression coverage for the exact Phase 12 false page-break geometry.
Require-Text "ProjectManagement.Tests\Publications\BrochurePrintCompactPlannerTests.cs" "Plan_Phase12ObservedThreeProjectCombination_RemainsOnOneSheetAtNinePoint" "observed three-project false-break regression"
Require-Text "ProjectManagement.Tests\Publications\BrochurePrintMeasurementServiceTests.cs" "MeasureProject_NormalImageWidthNeverExceedsReferenceQualityCap" "image cap regression"
Require-Text "ProjectManagement.Tests\Publications\BrochurePrintMeasurementServiceTests.cs" "MeasureProject_ParagraphSpacingIsExplicitAndCompact" "paragraph rhythm regression"
Require-Text "ProjectManagement.Tests\Publications\BrochurePrintMeasurementServiceTests.cs" "MeasureProject_RepeatedBlankLinesDoNotReserveFullTextLines" "blank-line overmeasurement regression"

Write-Host "Phase 13 source/integration contract is present." -ForegroundColor Green

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
Write-Host "Then regenerate the same nine-project brochure and compare project-page fill, 9 pt copy, image scale, Gallery 2 and closing composition against the original reference brochure."
