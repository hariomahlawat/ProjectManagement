param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = (Get-Location).Path
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($ProjectRoot)

function Require-File([string]$RelativePath) {
    $path = Join-Path $root $RelativePath
    if (-not (Test-Path $path)) {
        throw "Missing Phase 7 file: $RelativePath"
    }
    return $path
}

function Require-Text([string]$RelativePath, [string]$Pattern, [string]$Description) {
    $path = Require-File $RelativePath
    $text = Get-Content $path -Raw
    if ($text -notmatch $Pattern) {
        throw "Phase 7 contract missing: $Description ($RelativePath)"
    }
}

function Reject-Text([string]$RelativePath, [string]$Pattern, [string]$Description) {
    $path = Require-File $RelativePath
    $text = Get-Content $path -Raw
    if ($text -match $Pattern) {
        throw "Phase 7 regression: $Description ($RelativePath)"
    }
}

Write-Host "PRISM Publications Phase 7 integration check" -ForegroundColor Cyan
Write-Host "Project root: $root"
Write-Host ""

# Publication profile and reference geometry.
Require-Text "Services\Publications\BrochureContracts.cs" "BrochurePublicationProfile" "dual publication profile model"
Require-Text "Services\Publications\BrochureContracts.cs" "PrintCompact\s*=\s*1" "Print / Compact profile"
Require-Text "Services\Publications\BrochureContracts.cs" "DigitalComfortable\s*=\s*2" "Digital / Comfortable profile"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "ReferenceWidthPoints\s*=\s*423\.23f" "reference CropBox width"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "ReferenceHeightPoints\s*=\s*846\.755f" "reference CropBox height"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "page\.Size\(ReferenceWidthPoints,\s*ReferenceHeightPoints\)" "custom narrow hard-copy page size"
Reject-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "PageSizes\.A4|PageSizes\.A3|PageSizes\.Letter" "compact print compositor must not fall back to a standard paper-size constant"

# Content-bearing first/final page.
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml.cs" "DefaultPrintIntroText" "opening institutional narrative default"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml.cs" "DefaultPrintProcurementText" "procurement guidance default"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml.cs" "DefaultPrintDevelopingAgencyText" "developing agency/contact default"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml.cs" "DefaultPrintManufacturingAgencyText" "manufacturing agency default"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml.cs" "DefaultPrintVisionaryText" "Visionary Horizons default"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml.cs" "DefaultPrintNewSimulatorsText" "New Simulators guidance default"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "data-brochure-print-only" "editable compact-print institutional content"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "ComposeFrontPage" "content-bearing compact-print first page"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "ComposeClosingMatter" "content-bearing compact-print final matter"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "Visionary Horizons & Strategic Objectives" "Visionary Horizons final-page panel"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "New Simulators\." "New Simulators final-page guidance"

# Natural compact packing and digital isolation.
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "ShowEntire\(\)" "whole-project compact modules with natural column flow"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "ComposeProjectModule" "compact project module compositor"
Require-Text "Utilities\Reporting\BrochurePdfReportBuilder.cs" "PublicationProfile == BrochurePublicationProfile\.PrintCompact" "print/digital renderer branch"
Require-Text "Utilities\Reporting\BrochurePdfReportBuilder.cs" "BrochurePrintCompactComposer\.Compose" "dedicated print compositor invocation"
Require-Text "Utilities\Reporting\BrochurePdfReportBuilder.cs" "PageSizes\.A4" "existing A4 digital renderer retained"

# Profile-aware source-image quality and independent Cover B artwork.
Require-Text "Services\Publications\BrochurePublicationService.cs" "DetermineCoverHeroQuality" "cover-specific automatic hero quality"
Require-Text "Services\Publications\BrochurePublicationService.cs" "publicationProfile == BrochurePublicationProfile\.PrintCompact[\s\S]{0,100}1055" "compact-print Cover B source aspect"
Require-Text "Services\Publications\BrochurePublicationService.cs" "compact print project frame" "compact-print image-quality thresholds"
Require-Text "Services\Publications\BrochureContracts.cs" "PrintNarrativeTooLong" "compact-print overlength narrative blocker"

# Phase 6 review/cover architecture remains and Phase 7 fixes the image buttons.
Require-Text "wwwroot\js\pages\projects-brochure.js" "openPhotoEditor\(activeReviewProjectId,\s*\"select\"\)" "Review Change image opens photo selection"
Require-Text "wwwroot\js\pages\projects-brochure.js" "openPhotoEditor\(activeReviewProjectId,\s*\"crop\"\)" "Review Adjust crop opens focal editor"
Require-Text "wwwroot\js\pages\projects-brochure.js" "primaryStage\.focus" "crop action focuses focal editor"
Require-Text "wwwroot\js\pages\projects-brochure.js" "coverHeroChoices\.scrollIntoView" "Cover B chooser is brought into view"
Require-Text "wwwroot\js\pages\projects-brochure.js" "coverHeroCropPanel\.scrollIntoView" "Cover B crop editor is brought into view"
Require-Text "wwwroot\js\pages\projects-brochure.js" "updatePublicationProfileUi" "client switches print/digital settings cleanly"

# Tests supplied with the package.
Require-Text "ProjectManagement.Tests\Publications\BrochurePdfReportBuilderTests.cs" "Build_PrintCompact_GeneratesReferenceFormatHardCopy" "compact-print PDF regression test"
Require-Text "wwwroot\js\projects\publications-brochure-contract.test.js" "phase 7 print compositor uses the reference CropBox dimensions" "browser/source contract regression test"

Write-Host "Phase 7 source/integration contract is present." -ForegroundColor Green
Write-Host ""
Write-Host "Recommended final checks:" -ForegroundColor Yellow
Write-Host "  dotnet build .\ProjectManagement.csproj"
Write-Host "  dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj"
Write-Host "  node --check .\wwwroot\js\pages\projects-brochure.js"
Write-Host "  node --test .\wwwroot\js\projects\publications-brochure-contract.test.js"
