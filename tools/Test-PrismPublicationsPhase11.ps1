param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = (Get-Location).Path
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($ProjectRoot)

Write-Host "PRISM Publications Phase 11 integration check" -ForegroundColor Cyan
Write-Host "Project root: $root"

function Resolve-ProjectPath([string]$RelativePath) {
    return Join-Path $root $RelativePath
}

function Require-File([string]$RelativePath) {
    $path = Resolve-ProjectPath $RelativePath
    if (-not (Test-Path $path)) {
        throw "Required Phase 11 file is missing: $RelativePath"
    }
    return $path
}

function Require-Text([string]$RelativePath, [string]$Pattern, [string]$Description) {
    $path = Require-File $RelativePath
    $text = Get-Content $path -Raw
    if ($text -notmatch $Pattern) {
        throw "Phase 11 contract missing: $Description`nFile: $RelativePath`nPattern: $Pattern"
    }
}

function Reject-Text([string]$RelativePath, [string]$Pattern, [string]$Description) {
    $path = Require-File $RelativePath
    $text = Get-Content $path -Raw
    if ($text -match $Pattern) {
        throw "Obsolete print behaviour is still present: $Description`nFile: $RelativePath`nPattern: $Pattern"
    }
}

# Measured architecture and reference geometry.
Require-File "Services\Publications\BrochurePrintLayoutMetrics.cs" | Out-Null
Require-File "Services\Publications\BrochurePrintMeasurementService.cs" | Out-Null
Require-File "Services\Publications\BrochurePrintPagePlanner.cs" | Out-Null
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "SKPaint" "font-aware SkiaSharp measurement"
Require-Text "Services\Publications\BrochurePrintPagePlanner.cs" "TryPlanWithSharedClosing" "closing-aware measured planner"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ReferenceWidthPoints\s*=\s*423\.23f" "reference sheet width"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ReferenceHeightPoints\s*=\s*846\.755f" "reference sheet height"

# 16:9 project imagery and normal 9 pt print body.
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "SingleImageAspectRatio\s*=\s*16f\s*/\s*9f" "16:9 single-image geometry"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "GalleryImageAspectRatio\s*=\s*16f\s*/\s*9f" "16:9 Gallery 2 geometry"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ProjectBodyPreferredFontSize\s*=\s*9f" "9 pt preferred project body"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "BrochurePrintLayoutVariant\.Balanced[\s\S]{0,520}BodyFontSize:\s*ProjectBodyPreferredFontSize" "Balanced layout retains 9 pt body"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ImageWidthPoints:\s*150f\s*\+\s*imageAdjustment" "reference-scale Visual image"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ImageWidthPoints:\s*140f\s*\+\s*imageAdjustment" "reference-scale Balanced image"
Reject-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "SingleImageAspectRatio\s*=\s*1\.45f" "legacy tall single-image frame"

# Editorial float split and quality-first page planning.
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "BuildEditorialBoundaries" "sentence/paragraph-aware float split"
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "EditorialBoundaryKind\.Paragraph" "paragraph split preference"
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "EditorialBoundaryKind\.Sentence" "sentence split preference"
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "EditorialBoundaryKind\.Word" "word fallback split"
Require-Text "Services\Publications\BrochurePrintPagePlanner.cs" "typographyPenalty\s*<\s*existing\.TypographyPenalty" "typography outranks page count"
Require-Text "Services\Publications\BrochurePrintPagePlanner.cs" "ApplyResidualImageExpansion" "post-plan residual image expansion"
Require-Text "Services\Publications\BrochurePrintPagePlanner.cs" "ResidualMaximumImageExpansionPoints" "bounded residual expansion"
Require-Text "Services\Publications\BrochurePrintPagePlanner.cs" "sharedPenalty\s*<\s*dedicatedPenalty" "shared closing cannot force typography reduction"

# Cover A footer and project image frame rendering.
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "FrontContactCentreWidthPoints" "reserved CONTACTS centre lane"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "header\.ConstantItem\(BrochurePrintLayoutMetrics\.FrontContactCentreWidthPoints\)" "structural CONTACTS lane"
Reject-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "ComposeImage[\s\S]{0,220}\.Padding\(1\)" "white image-frame inset"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "ComposeImage[\s\S]{0,260}\.FitArea\(\)" "bounded publication image rendering"

# Regression coverage.
Require-Text "ProjectManagement.Tests\Publications\BrochurePrintMeasurementServiceTests.cs" "PublicationImageFramesUseExactSixteenByNineGeometry" "16:9 measurement regression"
Require-Text "ProjectManagement.Tests\Publications\BrochurePrintMeasurementServiceTests.cs" "FloatSplitPrefersSentenceBoundaryNearImageHeight" "editorial float regression"
Require-Text "ProjectManagement.Tests\Publications\BrochurePrintCompactPlannerTests.cs" "NinePointTypographyOutranksSavingOneSheet" "quality-first planner regression"
Require-Text "ProjectManagement.Tests\Publications\BrochurePrintCompactPlannerTests.cs" "ResidualPassExpandsImagesWithoutChangingProjectOrder" "residual expansion regression"
Require-Text "wwwroot\js\projects\publications-brochure-contract.test.js" "phase 11 locks 16:9 print imagery" "browser/source Phase 11 contract"

Write-Host "Phase 11 source/integration contract is present." -ForegroundColor Green

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
Write-Host "dotnet restore .\ProjectManagement.csproj"
Write-Host "dotnet build .\ProjectManagement.csproj"
Write-Host "dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj"
