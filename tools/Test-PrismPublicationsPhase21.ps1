param(
    [switch]$SkipDotNet
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $root
try {
    Write-Host 'PRISM Publications Phase 21 - Digital final hardening and design freeze' -ForegroundColor Cyan
    Write-Host "Project root: $root"

    $required = @(
        'Models/Publications/BrochurePreset.cs',
        'Services/Publications/BrochureContracts.cs',
        'Services/Publications/BrochurePresetContracts.cs',
        'Services/Publications/BrochurePresetService.cs',
        'Services/Publications/BrochureReviewFingerprint.cs',
        'Services/Publications/BrochureDigitalPublicationPolicy.cs',
        'Services/Publications/BrochurePhotoPrintQualityEvaluator.cs',
        'Services/Publications/BrochurePublicationService.cs',
        'Utilities/Reporting/BrochurePdfReportBuilder.cs',
        'Pages/Projects/Publications/Brochure/Index.cshtml',
        'Pages/Projects/Publications/Brochure/Index.cshtml.cs',
        'wwwroot/css/pages/projects-publications.css',
        'wwwroot/js/pages/projects-brochure.js',
        'wwwroot/js/projects/publications-brochure-contract.test.js',
        'Migrations/20261208110000_AddBrochureCoverTextControls.cs',
        'Migrations/ApplicationDbContextModelSnapshot.cs',
        'Migrations/immutable-migration-ids.txt'
    )
    foreach ($path in $required) {
        if (-not (Test-Path $path)) { throw "Missing required Phase 21 file: $path" }
    }

    $renderer = Get-Content 'Utilities/Reporting/BrochurePdfReportBuilder.cs' -Raw
    $policy = Get-Content 'Services/Publications/BrochureDigitalPublicationPolicy.cs' -Raw
    $service = Get-Content 'Services/Publications/BrochurePublicationService.cs' -Raw
    $view = Get-Content 'Pages/Projects/Publications/Brochure/Index.cshtml' -Raw
    $pageModel = Get-Content 'Pages/Projects/Publications/Brochure/Index.cshtml.cs' -Raw
    $js = Get-Content 'wwwroot/js/pages/projects-brochure.js' -Raw
    $migration = Get-Content 'Migrations/20261208110000_AddBrochureCoverTextControls.cs' -Raw
    $immutable = Get-Content 'Migrations/immutable-migration-ids.txt' -Raw

    foreach ($needle in @(
        'FrontCoverKicker',
        'FrontCoverDescriptor',
        'ShowFrontCoverTitle',
        'BackCoverKicker',
        'BackCoverStrapline',
        'BackCoverEdition'
    )) {
        if (-not $view.Contains($needle)) { throw "Editable cover-text UI contract missing: $needle" }
        if (-not $pageModel.Contains($needle)) { throw "Cover-text model binding contract missing: $needle" }
    }

    foreach ($forbidden in @(
        'Selected PRISM project imagery',
        'CAPABILITY PUBLICATION · CONTEMPORARY EDITION',
        'OFFICIAL CAPABILITY PUBLICATION',
        'SIMULATORS · AI · AR/VR · ROBOTICS · DRONES',
        'Capability imagery is drawn from',
        'SDD · PRISM'
    )) {
        if ($renderer.Contains($forbidden)) { throw "Uneditable system-owned cover copy remains in renderer: $forbidden" }
    }

    foreach ($needle in @(
        'digitalBodyMinimum = 10.2f',
        '<= 165 => 500f',
        '<= 200 => 470f',
        'galleryItemWidth',
        'Width(174).Height(139.2f)',
        'CoverStyle == BrochureCoverStyle.Contemporary',
        'PaddingBottom(108)'
    )) {
        if (-not $renderer.Contains($needle)) { throw "Digital freeze renderer contract missing: $needle" }
    }

    if (-not $policy.Contains('EditorialPageCount')) { throw 'Digital policy does not expose EditorialPageCount.' }
    if (-not $service.Contains('LowResolutionCoverHero')) { throw 'Cover-specific low-resolution issue routing is missing.' }
    if (-not $view.Contains('>Use automatic hero</')) { throw 'Cover B automatic action is not explicit.' }
    if (-not $view.Contains('data-digital-editorial-pages')) { throw 'Digital Editorial pages KPI is missing.' }
    if (-not $js.Contains('PDF verified ·')) { throw 'Profile-neutral PDF verification status is missing.' }
    if (-not $js.Contains('fixCover.textContent = "Fix cover"')) { throw 'Cover finding does not route to Fix cover.' }

    if (-not $migration.Contains('AddBrochureCoverTextControls')) { throw 'Phase 21 migration contract missing.' }
    if (-not $migration.Contains('SettingsSchemaVersion')) { throw 'Phase 21 migration does not advance preset schema.' }
    $migrationId = '20261208110000_AddBrochureCoverTextControls'
    $idMatches = ([regex]::Matches($immutable, [regex]::Escape($migrationId))).Count
    if ($idMatches -ne 1) { throw "Immutable migration manifest must contain $migrationId exactly once; found $idMatches." }

    if ($service.Contains('.Fragments')) { throw 'Stale BrochurePagePlan.Fragments reference detected.' }

    $activeWebSources = Get-ChildItem -Path 'Pages','Views','wwwroot' -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in '.cshtml','.css','.js','.html' }
    foreach ($file in $activeWebSources) {
        $text = Get-Content $file.FullName -Raw
        if ($text.Contains('fonts.googleapis.com') -or $text.Contains('fonts.gstatic.com')) {
            throw "External Google Fonts dependency found in active web source: $($file.FullName)"
        }
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

    Write-Host 'Phase 21 validation completed successfully.' -ForegroundColor Green
}
finally {
    Pop-Location
}
