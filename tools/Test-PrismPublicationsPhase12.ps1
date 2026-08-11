param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = (Get-Location).Path
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($ProjectRoot)

Write-Host "PRISM Publications Phase 12 reference-quality integration check" -ForegroundColor Cyan
Write-Host "Project root: $root"

function Resolve-ProjectPath([string]$RelativePath) { Join-Path $root $RelativePath }
function Require-File([string]$RelativePath) {
    $path = Resolve-ProjectPath $RelativePath
    if (-not (Test-Path $path)) { throw "Required Phase 12 file is missing: $RelativePath" }
    return $path
}
function Require-Text([string]$RelativePath, [string]$Pattern, [string]$Description) {
    $path = Require-File $RelativePath
    $text = Get-Content $path -Raw
    if ($text -notmatch $Pattern) { throw "Phase 12 contract missing: $Description`nFile: $RelativePath`nPattern: $Pattern" }
}
function Reject-Text([string]$RelativePath, [string]$Pattern, [string]$Description) {
    $path = Require-File $RelativePath
    $text = Get-Content $path -Raw
    if ($text -match $Pattern) { throw "Obsolete publication behaviour is still present: $Description`nFile: $RelativePath`nPattern: $Pattern" }
}

# Curated Cover A library.
$coverAssets = @(
    "wwwroot\img\publications\covers\cover-a-reference-original.jpg",
    "wwwroot\img\publications\covers\cover-a-premium-green-gold.jpg",
    "wwwroot\img\publications\covers\cover-a-cinematic-cyber.jpg",
    "wwwroot\img\publications\covers\cover-a-executive-teal.jpg",
    "wwwroot\img\publications\covers\cover-a-luminous-halo.jpg"
)
$coverAssets | ForEach-Object { Require-File $_ | Out-Null }
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "data-brochure-institutional-artwork-panel" "Cover A artwork selector"
Require-Text "Services\Publications\BrochureContracts.cs" "BrochureInstitutionalCoverArtwork" "institutional artwork enum"
Require-Text "Utilities\Reporting\BrochurePdfReportBuilder.cs" "TryLoadInstitutionalArtwork" "selected Cover A asset loader"

# Reference geometry and quality floor.
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ReferenceWidthPoints\s*=\s*423\.23f" "reference sheet width"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ReferenceHeightPoints\s*=\s*846\.755f" "reference sheet height"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ProjectBodyPreferredFontSize\s*=\s*9f" "9 pt normal project body"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ImageWidthPoints:\s*154f\s*\+\s*imageAdjustment" "reference-scale Visual image"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ImageWidthPoints:\s*148f\s*\+\s*imageAdjustment" "reference-scale Balanced image"
Require-Text "Services\Publications\BrochurePrintPagePlanner.cs" "CandidatesForSegment\(itemCount\)" "normal pages exclude Compact typography"
Require-Text "Services\Publications\BrochurePrintPagePlanner.cs" "if \(itemCount == 1\)" "Compact typography is single-project emergency only"

# Residual composition and float continuation.
Require-Text "Services\Publications\BrochurePrintPagePlanner.cs" "ResidualMaximumExtraModuleVerticalPaddingPoints" "residual module breathing"
Require-Text "Services\Publications\BrochurePrintPagePlanner.cs" "ResidualMaximumExtraInterModuleSpacingPoints" "residual inter-module spacing"
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "ContinuationNarrative" "mid-sentence continuation model"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "layout\.ContinuationNarrative" "non-justified continuation rendering"

# Cover A composition and contact integrity.
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "ComposeInstitutionalCentreOverlay" "Centre statement integrated into Cover A hero"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "FrontContactBadgeHeightPoints" "dedicated CONTACTS row"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "FrontContactDevelopingFraction" "asymmetric developing agency column"
Reject-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "FrontContactCentreWidthPoints" "obsolete overlapping CONTACTS centre lane"
Reject-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "ClosingStraplineFontSize" "redundant final-page strapline"

Write-Host "Phase 12 source/integration contract is present." -ForegroundColor Green

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
Write-Host "Then regenerate the same brochure and compare Cover A, page fill, Gallery 2 and long-title cases against the original reference brochure."
