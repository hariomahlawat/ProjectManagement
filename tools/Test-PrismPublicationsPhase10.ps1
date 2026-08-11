param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = (Get-Location).Path
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($ProjectRoot)

Write-Host "PRISM Publications Phase 10 integration check" -ForegroundColor Cyan
Write-Host "Project root: $root"

function Resolve-ProjectPath([string]$RelativePath) {
    return Join-Path $root $RelativePath
}

function Require-File([string]$RelativePath) {
    $path = Resolve-ProjectPath $RelativePath
    if (-not (Test-Path $path)) {
        throw "Required Phase 10 file is missing: $RelativePath"
    }
    return $path
}

function Require-Text([string]$RelativePath, [string]$Pattern, [string]$Description) {
    $path = Require-File $RelativePath
    $text = Get-Content $path -Raw
    if ($text -notmatch $Pattern) {
        throw "Phase 10 contract missing: $Description`nFile: $RelativePath`nPattern: $Pattern"
    }
}

function Reject-Text([string]$RelativePath, [string]$Pattern, [string]$Description) {
    $path = Require-File $RelativePath
    $text = Get-Content $path -Raw
    if ($text -match $Pattern) {
        throw "Obsolete compact-print behaviour is still present: $Description`nFile: $RelativePath`nPattern: $Pattern"
    }
}

# Phase 9 measured architecture must remain intact.
Require-File "Services\Publications\BrochurePrintLayoutMetrics.cs" | Out-Null
Require-File "Services\Publications\BrochurePrintMeasurementService.cs" | Out-Null
Require-File "Services\Publications\BrochurePrintPagePlanner.cs" | Out-Null
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "SKPaint" "font-aware SkiaSharp measurement"
Require-Text "Services\Publications\BrochurePrintPagePlanner.cs" "TryPlanWithSharedClosing" "closing-aware measured planner"
Require-Text "Services\Publications\PublicationServiceCollectionExtensions.cs" "AddSingleton<IBrochurePrintMeasurementService, BrochurePrintMeasurementService>" "measurement-service DI registration"
Require-Text "Services\Publications\PublicationServiceCollectionExtensions.cs" "AddSingleton<IBrochurePrintPagePlanner, BrochurePrintPagePlanner>" "page-planner DI registration"

# Phase 10 reference-format project composition.
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ProjectBodyPreferredFontSize\s*=\s*9f" "9 pt preferred print body"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ProjectBodyMinimumFontSize\s*=\s*8\.5f" "8.5 pt minimum print body"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ProjectTitlePreferredFontSize\s*=\s*10f" "10 pt preferred project title"
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "SplitNarrativeForFloat" "measured right-image text-wrap split"
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "LeadingNarrative" "measured leading narrative"
Require-Text "Services\Publications\BrochurePrintMeasurementService.cs" "TrailingNarrative" "full-width remainder narrative"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "layout\.LeadingNarrative" "leading text beside image"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "layout\.TrailingNarrative" "remainder text below image"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "ConstantItem\(layout\.ImageWidthPoints\)\.AlignTop" "upper-right image anchor"
Reject-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "plannedProject\.ProjectIndex\s*%\s*2" "alternating hard-copy imagery"
Reject-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "imageOnRight" "legacy alternating hard-copy image parameter"

# Final-sheet and institutional visual fidelity.
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ClosingVisionBodyFontSize\s*=\s*10\.4f" "stronger Visionary body typography"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ClosingVisionHeadingFontSize\s*=\s*11\.2f" "stronger Visionary heading typography"
Require-Text "Services\Publications\BrochurePrintLayoutMetrics.cs" "ClosingNewSimulatorsFontSize\s*=\s*8\.8f" "restored New Simulators typography"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" 'Forest800 = "#156656"' "reference project green"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" 'Text\("CONTACTS"\)' "reference contact identifier"
Require-Text "Utilities\Reporting\BrochurePrintCompactComposer.cs" "ComposeInstitutionalFallbackArtwork" "institutional fallback retained"

# Regression tests.
Require-Text "ProjectManagement.Tests\Publications\BrochurePrintMeasurementServiceTests.cs" "UsesReferenceFloatAndFullWidthRemainder" "float measurement regression"
Require-Text "ProjectManagement.Tests\Publications\BrochurePrintMeasurementServiceTests.cs" "RespectPrintTypographyFloor" "typography-floor regression"
Require-Text "wwwroot\js\projects\publications-brochure-contract.test.js" "phase 10 print compact restores reference float composition" "browser/source Phase 10 contract"

Write-Host "Phase 10 source/integration contract is present." -ForegroundColor Green

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
Write-Host "dotnet build .\ProjectManagement.csproj"
Write-Host "dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj"
