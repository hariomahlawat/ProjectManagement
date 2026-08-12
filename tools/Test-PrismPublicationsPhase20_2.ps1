$ErrorActionPreference = 'Stop'

Write-Host 'PRISM Publications Phase 20.2 - Cover B reliability check'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path (Join-Path $root 'ProjectManagement.csproj'))) {
    $root = (Get-Location).Path
}

$view = Join-Path $root 'Pages\Projects\Publications\Brochure\Index.cshtml'
$pageModel = Join-Path $root 'Pages\Projects\Publications\Brochure\Index.cshtml.cs'
$publicationService = Join-Path $root 'Services\Publications\BrochurePublicationService.cs'
$photoService = Join-Path $root 'Services\Publications\BrochurePhotoService.cs'
$brochureJs = Join-Path $root 'wwwroot\js\pages\projects-brochure.js'
$brochureCss = Join-Path $root 'wwwroot\css\pages\projects-publications.css'
$brochureTests = Join-Path $root 'wwwroot\js\projects\publications-brochure-contract.test.js'

foreach ($path in @($view, $pageModel, $publicationService, $photoService, $brochureJs, $brochureCss, $brochureTests)) {
    if (-not (Test-Path $path)) { throw "Missing $path" }
}

$js = Get-Content $brochureJs -Raw
if ($js -notmatch 'const\s+isCurrentCoverApproved\s*=') { throw 'Canonical Cover B approval predicate is missing.' }
if ($js -notmatch 'coverReviewFingerprint\s*===\s*serverFingerprint') { throw 'Cover B approval is not bound to the current server fingerprint.' }
if ($js -notmatch 'let\s+preflightRevision\s*=\s*0') { throw 'Preflight revision guard is missing.' }
if ($js -notmatch 'preflightAbort\?\.abort\(\)') { throw 'Immediate superseded-preflight cancellation is missing.' }
if ($js -notmatch '!preview\s*&&\s*isContemporaryCover\(\)\s*&&\s*!isCurrentCoverApproved\(\)') { throw 'Final Cover B generation guard is missing.' }
if ($js -notmatch 'publicationErrorFromResponse') { throw 'Structured publication error handling is missing.' }
if ($js -notmatch 'CoverReviewStale') { throw 'CoverReviewStale recovery handling is missing.' }

$pageText = Get-Content $pageModel -Raw
if ($pageText -notmatch 'code\s*=\s*"publicationStateChanged"') { throw 'Structured 409 publication-state response is missing.' }
if ($pageText -notmatch 'code\s*=\s*issue\.Code\.ToString\(\)') { throw 'Structured publication issue codes are missing.' }

$photoText = Get-Content $photoService -Raw
if ($photoText -notmatch 'IsRecoverableImageException') { throw 'Recoverable publication-image processing boundary is missing.' }

$serviceText = Get-Content $publicationService -Raw
if ($serviceText -match '\.Fragments\b') { throw 'Phase 20.1 compile fix regressed: BrochurePagePlan.Fragments remains.' }
if ($serviceText -notmatch 'foreach\s*\(var\s+fragment\s+in\s+page\.Items\)') { throw 'Expected BrochurePagePlan.Items enumeration was not found.' }

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

Write-Host 'Phase 20.2 checks completed.'
