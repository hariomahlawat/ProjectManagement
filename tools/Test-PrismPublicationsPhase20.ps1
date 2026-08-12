param(
    [switch]$SkipDotNet
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $root
try {
    Write-Host 'PRISM Publications Phase 20 - Digital / Comfortable validation' -ForegroundColor Cyan
    Write-Host "Project root: $root"

    $required = @(
        'Services/Publications/BrochureDigitalPublicationPolicy.cs',
        'Services/Publications/BrochureLayoutPlanner.cs',
        'Services/Publications/BrochurePublicationService.cs',
        'Services/Publications/BrochurePhotoPrintQualityEvaluator.cs',
        'Utilities/Reporting/BrochurePdfReportBuilder.cs',
        'Utilities/Reporting/BrochurePdfCompositionVerifier.cs',
        'Pages/Projects/Publications/Brochure/Index.cshtml',
        'Pages/Projects/Publications/Brochure/Index.cshtml.cs',
        'wwwroot/js/pages/projects-brochure.js',
        'wwwroot/js/projects/publications-brochure-contract.test.js'
    )
    foreach ($path in $required) {
        if (-not (Test-Path $path)) { throw "Missing required Phase 20 file: $path" }
    }

    $renderer = Get-Content 'Utilities/Reporting/BrochurePdfReportBuilder.cs' -Raw
    $policy = Get-Content 'Services/Publications/BrochureDigitalPublicationPolicy.cs' -Raw
    $service = Get-Content 'Services/Publications/BrochurePublicationService.cs' -Raw
    $js = Get-Content 'wwwroot/js/pages/projects-brochure.js' -Raw

    foreach ($needle in @(
        'ComposeDigitalInstitutionalOpening',
        'ComposeDigitalInstitutionalClosing',
        'BrochurePdfCompositionVerifier.VerifyDigital',
        'Height(410)',
        'Future capability & engagement'
    )) {
        if (-not $renderer.Contains($needle)) { throw "Renderer contract missing: $needle" }
    }
    foreach ($needle in @('PlanDigitalComfortable', 'InstitutionalOpeningMaximumWords', 'InstitutionalClosingMaximumWords')) {
        if (-not $policy.Contains($needle)) { throw "Digital policy contract missing: $needle" }
    }
    foreach ($needle in @('BuildDigitalPhotoPlacements', 'PhotoPlacement.EditorialSplit', '1360d')) {
        if (-not $service.Contains($needle)) { throw "Digital quality contract missing: $needle" }
    }
    if (-not $js.Contains('1800 / (isPrintCompactProfile() ? 1055 : 1360)')) {
        throw 'Cover B crop editor is not aligned with the Digital premium hero aspect.'
    }

    if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
        throw 'Node.js is required for publication JavaScript validation.'
    }
    node --check '.\wwwroot\js\pages\projects-brochure.js'
    if ($LASTEXITCODE -ne 0) { throw 'node --check failed.' }
    node --test '.\wwwroot\js\projects\publications-brochure-contract.test.js'
    if ($LASTEXITCODE -ne 0) { throw 'publication contract tests failed.' }

    if (-not $SkipDotNet) {
        if (Get-Command dotnet -ErrorAction SilentlyContinue) {
            dotnet build '.\ProjectManagement.csproj'
            if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
            dotnet test '.\ProjectManagement.Tests\ProjectManagement.Tests.csproj'
            if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }
        }
        else {
            Write-Warning '.NET SDK not found. Run dotnet build/test on the development machine before deployment.'
        }
    }

    Write-Host 'Phase 20 validation completed successfully.' -ForegroundColor Green
}
finally {
    Pop-Location
}
