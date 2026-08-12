$ErrorActionPreference = 'Stop'

Write-Host 'PRISM Publications Phase 20.3 - Cover B QuestPDF composition fix'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path (Join-Path $root 'ProjectManagement.csproj'))) {
    $root = (Get-Location).Path
}

$renderer = Join-Path $root 'Utilities\Reporting\BrochurePdfReportBuilder.cs'
$pageModel = Join-Path $root 'Pages\Projects\Publications\Brochure\Index.cshtml.cs'
$publicationService = Join-Path $root 'Services\Publications\BrochurePublicationService.cs'
$brochureJs = Join-Path $root 'wwwroot\js\pages\projects-brochure.js'
$brochureTests = Join-Path $root 'wwwroot\js\projects\publications-brochure-contract.test.js'
$csharpTests = Join-Path $root 'ProjectManagement.Tests\Publications\BrochurePdfReportBuilderTests.cs'

foreach ($path in @($renderer, $pageModel, $publicationService, $brochureJs, $brochureTests, $csharpTests)) {
    if (-not (Test-Path $path)) { throw "Missing $path" }
}

$rendererText = Get-Content $renderer -Raw
$start = $rendererText.IndexOf('private static void ComposeContemporaryCover')
$end = $rendererText.IndexOf('private static void ComposeDigitalInstitutionalOpening')
if ($start -lt 0 -or $end -le $start) { throw 'Could not isolate ComposeContemporaryCover().' }
$coverB = $rendererText.Substring($start, $end - $start)
$primaryCount = ([regex]::Matches($coverB, 'layers\.PrimaryLayer\(\)')).Count
if ($primaryCount -ne 1) { throw "Cover B must contain exactly one QuestPDF PrimaryLayer; found $primaryCount." }
if ($coverB -notmatch 'layers\.PrimaryLayer\(\)\.Background\(Forest950\)') {
    throw 'Cover B full-page institutional field is not the QuestPDF PrimaryLayer.'
}

$pageText = Get-Content $pageModel -Raw
if ($pageText -notmatch 'code\s*=\s*"pdfCompositionFailed"') {
    throw 'Structured PDF composition failure code is missing.'
}

$serviceText = Get-Content $publicationService -Raw
if ($serviceText -match '\.Fragments\b') { throw 'Phase 20.1 compile fix regressed: BrochurePagePlan.Fragments remains.' }
if ($serviceText -notmatch 'foreach\s*\(var\s+fragment\s+in\s+page\.Items\)') {
    throw 'Expected BrochurePagePlan.Items enumeration was not found.'
}

$csharpText = Get-Content $csharpTests -Raw
if ($csharpText -notmatch 'Build_DigitalContemporaryCover_WithHeroBytes_ComposesWithoutLayerTopologyFailure') {
    throw 'Targeted Cover B QuestPDF runtime regression test is missing.'
}

Write-Host 'PASS source-contract checks'

if (Get-Command node -ErrorAction SilentlyContinue) {
    & node --check $brochureJs
    if ($LASTEXITCODE -ne 0) { throw 'node --check failed.' }

    Push-Location $root
    try {
        & node --test $brochureTests
        if ($LASTEXITCODE -ne 0) { throw 'Brochure JS/source contract tests failed.' }
    }
    finally { Pop-Location }
}
else {
    Write-Warning 'Node.js not found; JavaScript checks skipped.'
}

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    Push-Location $root
    try {
        & dotnet build .\ProjectManagement.csproj
        if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
        if (Test-Path '.\ProjectManagement.Tests\ProjectManagement.Tests.csproj') {
            & dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj --no-build
            if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }
        }
    }
    finally { Pop-Location }
}
else {
    Write-Warning '.NET SDK not found; run dotnet build/test on the development machine.'
}

Write-Host 'Phase 20.3 checks completed.'
