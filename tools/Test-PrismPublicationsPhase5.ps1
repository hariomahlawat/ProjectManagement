param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = (Get-Location).Path
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($ProjectRoot)
$failures = New-Object System.Collections.Generic.List[string]

function Require-File([string]$RelativePath) {
    $path = Join-Path $root $RelativePath
    if (-not (Test-Path $path)) {
        $failures.Add("Missing file: $RelativePath")
        return $null
    }
    return $path
}

function Require-Text([string]$RelativePath, [string]$Pattern, [string]$Description) {
    $path = Require-File $RelativePath
    if ($null -eq $path) { return }
    $content = Get-Content $path -Raw
    if ($content -notmatch $Pattern) {
        $failures.Add("$Description ($RelativePath)")
    }
}

function Forbid-Text([string]$RelativePath, [string]$Pattern, [string]$Description) {
    $path = Require-File $RelativePath
    if ($null -eq $path) { return }
    $content = Get-Content $path -Raw
    if ($content -match $Pattern) {
        $failures.Add("$Description ($RelativePath)")
    }
}

Write-Host "PRISM Publications Phase 5 integration check" -ForegroundColor Cyan
Write-Host "Project root: $root"
Write-Host ""

# Phase 5 editorial workflow.
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" 'data-brochure-cover-hero-project' "Explicit Cover B hero input is missing"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" 'Review publication' "Publication Review workspace is missing"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" 'data-review-mark-reviewed' "Publication Review completion control is missing"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" 'Input\.IncludeBackCover' "Back-cover option is missing"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml.cs" 'OnPostProjectStateAsync' "Cross-tab authoritative project refresh endpoint is missing"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml.cs" 'Review all selected projects before final download' "Server final-download review gate is missing"
Require-Text "Services\Publications\BrochurePublicationService.cs" 'GetReviewProjectsAsync' "Authoritative publication-review reader is missing"
Require-Text "Services\Publications\BrochurePublicationService.cs" 'ResolveCoverHeroProjectId' "Cover hero resolver is missing"
Require-Text "Services\Publications\BrochureContracts.cs" 'PrimaryPhotoConfirmed' "Publication image confirmation state is missing"
Require-Text "Services\Publications\BrochureContracts.cs" 'IsReviewed' "Publication review state is missing"

# Renderer maturity.
Require-Text "Utilities\Reporting\BrochurePdfReportBuilder.cs" 'ComposeTwoFeatureBlock' "Dedicated two-project feature renderer is missing"
Require-Text "Utilities\Reporting\BrochurePdfReportBuilder.cs" 'ConstantItem\(205\)' "Two-project feature image frame was not enlarged"
Require-Text "Utilities\Reporting\BrochurePdfReportBuilder.cs" 'ComposeBackCover' "Back-cover renderer is missing"
Require-Text "Utilities\Reporting\BrochurePdfReportBuilder.cs" 'ResolvedCoverHeroProjectId' "Cover B does not use resolved hero selection"
Require-Text "Utilities\Reporting\BrochurePdfReportBuilder.cs" 'AlignBottom\(\)[\s\S]{0,220}PaddingBottom\(92\)' "Cover B hero is not bottom-anchored above its closing band"
Forbid-Text "Utilities\Reporting\BrochurePdfReportBuilder.cs" 'Generated from authoritative PRISM records' "Front-cover PRISM provenance text remains in brochure renderer"

# Runtime client workflow.
Require-Text "wwwroot\js\pages\projects-brochure.js" 'response\.blob\(\)' "Fetch/blob PDF workflow is missing"
Require-Text "wwwroot\js\pages\projects-brochure.js" 'X-PRISM-Publication-FileName' "Download filename contract is missing"
Require-Text "wwwroot\js\pages\projects-brochure.js" 'refreshProjectState' "Cross-tab project-state refresh is missing"
Require-Text "wwwroot\js\pages\projects-brochure.js" 'renderSelected\(false, false\)' "Project-state refresh recursion guard is missing"
Require-Text "wwwroot\js\pages\projects-brochure.js" 'finalReady = previewReady && allReviewed\(\)' "Client final-download review gate is missing"
Require-Text "wwwroot\js\pages\projects-brochure.js" 'Select first.*matching|Select.*matching' "Matching-project selection wording is missing"

# UI contract.
Require-Text "wwwroot\css\pages\projects-publications.css" '\.brochure-cover-hero-panel' "Cover hero styling is missing"
Require-Text "wwwroot\css\pages\projects-publications.css" '\.brochure-review-card' "Publication Review styling is missing"
Require-Text "wwwroot\css\pages\projects-publications.css" '\.brochure-review-nav__item' "Publication Review navigator styling is missing"
Require-Text "wwwroot\css\pages\projects-publications.css" 'grid-template-columns:\s*minmax\(220px, 2fr\)' "Desktop brochure filter widths were not widened"
Require-File "wwwroot\js\projects\publications-brochure-contract.test.js" | Out-Null

if ($failures.Count -gt 0) {
    Write-Host "Phase 5 check FAILED:" -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host "  - $failure" -ForegroundColor Red
    }
    exit 1
}

Write-Host "Phase 5 source/integration contract is present." -ForegroundColor Green
Write-Host ""
Write-Host "Recommended final checks:" -ForegroundColor Yellow
Write-Host "  dotnet build .\ProjectManagement.csproj"
Write-Host "  dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj"
Write-Host "  node --check .\wwwroot\js\pages\projects-brochure.js"
Write-Host "  node --test .\wwwroot\js\projects\publications-brochure-contract.test.js"
