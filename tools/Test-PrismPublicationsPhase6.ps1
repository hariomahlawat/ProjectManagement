param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = (Get-Location).Path
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($ProjectRoot)

function Require-File([string]$RelativePath) {
    $path = Join-Path $root $RelativePath
    if (-not (Test-Path $path)) {
        throw "Missing Phase 6 file: $RelativePath"
    }
    return $path
}

function Require-Text([string]$RelativePath, [string]$Pattern, [string]$Description) {
    $path = Require-File $RelativePath
    $text = Get-Content $path -Raw
    if ($text -notmatch $Pattern) {
        throw "Phase 6 contract missing: $Description ($RelativePath)"
    }
}

function Reject-Text([string]$RelativePath, [string]$Pattern, [string]$Description) {
    $path = Require-File $RelativePath
    $text = Get-Content $path -Raw
    if ($text -match $Pattern) {
        throw "Obsolete Phase 5 contract is still present: $Description ($RelativePath)"
    }
}

Write-Host "PRISM Publications Phase 6 integration check" -ForegroundColor Cyan
Write-Host "Project root: $root"
Write-Host ""

Require-Text "Services\Publications\BrochureContracts.cs" "CoverHeroPhotoId" "independent Cover B hero photo id"
Require-Text "Services\Publications\BrochureContracts.cs" "CoverHeroFocalX" "independent Cover B focal point"
Require-Text "Services\Publications\BrochureContracts.cs" "CoverHeroImage" "independent rendered cover artwork"
Require-Text "Services\Publications\BrochurePublicationService.cs" "SelectMany\(project\s*=>[\s\S]*photosByProject" "automatic cover hero searches all selected project photographs"
Require-Text "Services\Publications\BrochurePublicationService.cs" "EffectiveCropDimensions\([\s\S]*1800d / 1100d" "cover-specific quality crop"
Reject-Text "Services\Publications\BrochurePublicationService.cs" "BrochurePreflightIssueCode\.UnconfirmedPrimaryPhoto[\s\S]{0,160}PublicationIssueSeverity\.Warning" "editorial confirmation counted as a technical warning"

Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "data-cover-hero-approve" "explicit cover approval control"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "data-cover-hero-focal-stage" "independent cover crop editor"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "Approve project" "simplified project review action"
Reject-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "Use this image" "obsolete duplicate project-image confirmation control"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml.cs" "Input\.CoverStyle == BrochureCoverStyle\.Contemporary && !Input\.CoverReviewed" "Cover B final-download approval gate"

Require-Text "Utilities\Reporting\BrochurePdfReportBuilder.cs" "data\.CoverHeroImage\?\.Content" "renderer consumes independent cover artwork"
Require-Text "Utilities\Reporting\BrochurePdfReportBuilder.cs" "Height\(364\)" "final Cover B hero geometry"
Require-Text "Utilities\Reporting\BrochurePdfReportBuilder.cs" "ComposeSingleFeaturePage" "dedicated SingleFeature composer"
Require-Text "Utilities\Reporting\BrochurePdfReportBuilder.cs" "<= 125 => \(225f, 145f, 112f\)" "adaptive TwoFeature large-image geometry"
Require-Text "Utilities\Reporting\BrochurePdfReportBuilder.cs" "<= 155 => \(215f, 132f, 108f\)" "adaptive TwoFeature medium-image geometry"
Require-Text "Utilities\Reporting\BrochurePdfReportBuilder.cs" "featureGap" "Phase 5 CS0136 compile hotfix retained"
Require-Text "Utilities\Reporting\BrochurePdfReportBuilder.cs" "cardGap" "Phase 5 CS0136 compile hotfix retained"

Require-Text "wwwroot\js\pages\projects-brochure.js" "explicitCoverHeroPhotoId" "client tracks hero photo independently"
Require-Text "wwwroot\js\pages\projects-brochure.js" "coverReviewed" "client tracks cover approval"
Require-Text "wwwroot\js\pages\projects-brochure.js" "flatMap" "cover chooser enumerates all project photographs"
Require-Text "wwwroot\js\pages\projects-brochure.js" "const finalReady = previewReady && allReviewed\(\) && coverReady" "final download requires project and cover review"
Require-Text "wwwroot\js\pages\projects-brochure.js" "Approve project" "client uses unified project approval action"

# Reordering must still run preflight but must not invalidate all project reviews.
$clientPath = Require-File "wwwroot\js\pages\projects-brochure.js"
$client = Get-Content $clientPath -Raw
$reorderRegion = [regex]::Match($client, 'const moveSelected[\s\S]*?const renderSelected').Value
if ($reorderRegion -match 'invalidateAllReviews\(') {
    throw "Phase 6 regression: project reorder still invalidates every project review."
}

Write-Host "Phase 6 source/integration contract is present." -ForegroundColor Green
Write-Host ""
Write-Host "Recommended final checks:" -ForegroundColor Yellow
Write-Host "  dotnet build .\ProjectManagement.csproj"
Write-Host "  dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj"
Write-Host "  node --check .\wwwroot\js\pages\projects-brochure.js"
Write-Host "  node --test .\wwwroot\js\projects\publications-brochure-contract.test.js"
