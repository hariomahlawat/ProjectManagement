param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = (Get-Location).Path
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($ProjectRoot)

function Require-File([string]$RelativePath) {
    $path = Join-Path $root $RelativePath
    if (-not (Test-Path $path)) {
        throw "Missing Phase 8 file: $RelativePath"
    }
    return $path
}

function Require-Text([string]$RelativePath, [string]$Pattern, [string]$Description) {
    $path = Require-File $RelativePath
    $text = Get-Content $path -Raw
    if ($text -notmatch $Pattern) {
        throw "Phase 8 contract missing: $Description ($RelativePath)"
    }
}

function Reject-Text([string]$RelativePath, [string]$Pattern, [string]$Description) {
    $path = Require-File $RelativePath
    $text = Get-Content $path -Raw
    if ($text -match $Pattern) {
        throw "Phase 8 regression: $Description ($RelativePath)"
    }
}

Write-Host "PRISM Publications Phase 8 integration check" -ForegroundColor Cyan
Write-Host "Project root: $root"
Write-Host ""

# Original-format physical contract remains isolated from the digital profile.
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "ReferenceWidthPoints\s*=\s*423\.23f" "approved brochure width"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "ReferenceHeightPoints\s*=\s*846\.755f" "approved brochure height"
Reject-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "PageSizes\.A4|PageSizes\.A3|PageSizes\.Letter" "compact compositor must remain independent of standard paper constants"
Require-Text "Utilities\Reporting\BrochurePdfReportBuilder.cs" "PageSizes\.A4" "digital comfortable profile retains A4"

# Closing-aware deterministic pagination.
Require-File "Services\Publications\BrochurePrintCompactPlanner.cs" | Out-Null
Require-Text "Services\Publications\BrochurePrintCompactPlanner.cs" "TryPlanSharedClosingPage" "closing-aware compact planner"
Require-Text "Services\Publications\BrochurePrintCompactPlanner.cs" "finalPageReservedHeight" "reserved final-page institutional height"
Require-Text "Services\Publications\BrochurePrintCompactPlanner.cs" "AverageContentUtilizationPercent" "estimated sheet utilisation"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "BrochurePrintCompactPlanner\.Plan" "renderer uses compact sheet plan"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "ComposeProjectSheet" "explicit planned sheet composition"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "sheet\.IncludesClosingMatter" "closing content may share final project sheet"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "MinHeight\(minimumHeight\)" "modest residual-height balancing"

# Official-style institutional Cover A, with no arbitrary project-photo fallback.
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "ComposeInstitutionalFallbackArtwork" "institutional fallback treatment"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "ComposeInstitutionalFrontPage" "dedicated official-style Cover A"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "ComposeContemporaryFrontPage" "Cover B remains a distinct contemporary alternative"
Reject-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "project montage as fallback" "UI must not promise a montage that is not rendered"

# Publication-level first/final matter participates in the same preflight as project/photo checks.
Require-File "Services\Publications\BrochurePrintPublicationPolicy.cs" | Out-Null
Require-Text "Services\Publications\BrochurePrintPublicationPolicy.cs" "ApprovedReference" "single approved institutional copy source"
Require-Text "Services\Publications\BrochurePrintPublicationPolicy.cs" "PrintInstitutionalContentMissing" "missing print matter blocker"
Require-Text "Services\Publications\BrochurePrintPublicationPolicy.cs" "PrintInstitutionalContentTooLong" "overlength print matter blocker"
Require-Text "Services\Publications\BrochurePublicationService.cs" "ApplyPublicationLevelPreflight" "unified publication-level preflight"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml.cs" "ToPrintMatter\(\)" "page sends print matter to authoritative preflight"
Reject-Text "Pages\Projects\Publications\Brochure\Index.cshtml.cs" "DefaultPrintIntroText" "approved copy must not be duplicated in the page model"

# Operational print editing and planning feedback.
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "data-print-restore-approved" "restore approved institutional text action"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "data-print-word-limit" "live print-matter fit limits"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "data-print-plan-summary" "compact pagination estimate panel"
Require-Text "wwwroot\js\pages\projects-brochure.js" "updatePrintMatterWordCounts" "live print word counters"
Require-Text "wwwroot\js\pages\projects-brochure.js" "estimatedPageCount" "preflight page estimate rendering"
Require-Text "wwwroot\js\pages\projects-brochure.js" "closingMatterSharesFinalPage" "final sheet estimate rendering"

# Direct Gallery 2 review and print typography fidelity.
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" "data-review-image-mode" "image treatment control in publication review"
Require-Text "wwwroot\js\pages\projects-brochure.js" 'openPhotoEditor\(activeReviewProjectId,\s*"secondary"\)' "Gallery 2 review opens second-image chooser"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "ProjectName\.ToUpperInvariant\(\)[\s\S]{0,180}\.AlignCenter\(\)" "centred hard-copy project headings"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "Text\(project\.Narrative\)[\s\S]{0,160}\.Justify\(\)" "justified hard-copy project narrative"

# Regression tests supplied with Phase 8.
Require-File "ProjectManagement.Tests\Publications\BrochurePrintCompactPlannerTests.cs" | Out-Null
Require-File "ProjectManagement.Tests\Publications\BrochurePrintPublicationPolicyTests.cs" | Out-Null
Require-Text "wwwroot\js\projects\publications-brochure-contract.test.js" "phase 8 uses an explicit compact-sheet planner" "Phase 8 browser/source regression tests"

Write-Host "Phase 8 source/integration contract is present." -ForegroundColor Green
Write-Host ""
Write-Host "Recommended final checks:" -ForegroundColor Yellow
Write-Host "  dotnet build .\ProjectManagement.csproj"
Write-Host "  dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj"
Write-Host "  node --check .\wwwroot\js\pages\projects-brochure.js"
Write-Host "  node --test .\wwwroot\js\projects\publications-brochure-contract.test.js"
