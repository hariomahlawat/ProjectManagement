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

Write-Host "PRISM Publications Phase 4 integration check" -ForegroundColor Cyan
Write-Host "Project root: $root"
Write-Host ""

Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" 'mode\s*=\s*"thumb"' "Brochure thumbnail must use publication-photo handler"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml" 'mode\s*=\s*"source"' "Focal preview must use publication source handler"
Forbid-Text "Pages\Projects\Publications\Brochure\Index.cshtml" '/Projects/Photos/View' "Fixed Project Photo derivative URL remains in Brochure Builder"
Require-Text "Pages\Projects\Publications\Brochure\Index.cshtml.cs" 'OnGetPhotoAsync' "Publication photo endpoint is missing"
Require-Text "Services\Publications\BrochurePhotoService.cs" 'GetPreviewAsync' "Publication photo preview service is missing"
Require-Text "Services\Publications\BrochurePhotoService.cs" 'IMemoryCache' "Publication photo probe cache is missing"
Require-Text "Services\Publications\PublicationServiceCollectionExtensions.cs" 'AddMemoryCache\(' "Publications DI does not self-register memory cache"
Require-Text "wwwroot\css\pages\projects-publications.css" '\.brochure-filter-toolbar' "Current filter toolbar styling is missing"
Require-Text "wwwroot\css\pages\projects-publications.css" '\.brochure-project-table-wrap' "Bounded project register styling is missing"
Require-Text "wwwroot\css\pages\projects-publications.css" '\.brochure-preflight-grid\s*\{[^}]*display\s*:\s*grid' "Preflight metrics are not a CSS grid"
Forbid-Text "wwwroot\css\pages\projects-publications.css" '\.brochure-photo-choice img\s*\{\s*\.brochure-photo-choice img' "Malformed duplicate photograph CSS rule remains"
Require-Text "wwwroot\js\pages\projects-brochure.js" 'data-preflight-show-all' "Expandable preflight findings are missing"
Require-Text "wwwroot\js\pages\projects-brochure.js" 'Missing selected narrative|missing-copy' "Readiness filtering is missing"
Require-Text "Utilities\Reporting\BrochurePdfReportBuilder.cs" 'SplitIntroduction' "Adaptive introduction pagination is missing"
Require-File "wwwroot\js\projects\publications-brochure-contract.test.js" | Out-Null

if ($failures.Count -gt 0) {
    Write-Host "Phase 4 check FAILED:" -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host "  - $failure" -ForegroundColor Red
    }
    exit 1
}

Write-Host "Phase 4 source/integration contract is present." -ForegroundColor Green
Write-Host ""
Write-Host "Recommended final checks:" -ForegroundColor Yellow
Write-Host "  dotnet build .\ProjectManagement.csproj"
Write-Host "  dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj"
Write-Host "  node --check .\wwwroot\js\pages\projects-brochure.js"
Write-Host "  node --test .\wwwroot\js\projects\publications-brochure-contract.test.js"
