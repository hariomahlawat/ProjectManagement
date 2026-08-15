$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "PRISM Publications Phase 35 - programme semantics and icon polish validation"
Write-Host "Project root: $root"

$icons = @(
    "arms-services.svg",
    "proliferation-cost.svg",
    "ipr-filed.svg",
    "ipr-granted.svg",
    "ipr-mixed.svg",
    "technology-transfer.svg"
)

$required = @(
    "Services/Compendiums/CompendiumProgrammeInformation.cs",
    "Services/Compendiums/CompendiumDtos.cs",
    "Services/Compendiums/CompendiumReadinessPolicy.cs",
    "Services/Compendiums/CompendiumReviewFingerprint.cs",
    "Services/Compendiums/CompendiumReadService.cs",
    "Services/Compendiums/CompendiumExportService.cs",
    "Services/Compendiums/CompendiumDossierPaginationPlanner.cs",
    "Utilities/Reporting/CompendiumPdfReportBuilder.cs",
    "Pages/Projects/Publications/Compendium/Index.cshtml",
    "Pages/Projects/Publications/Compendium/Index.cshtml.cs",
    "Pages/Projects/Publications/Compendium/Structure.cshtml.cs",
    "wwwroot/js/pages/projects-compendium.js",
    "wwwroot/js/pages/projects-compendium-structure-editor.js",
    "wwwroot/js/pages/projects-compendium-cover-editor.js",
    "wwwroot/css/pages/projects-publications.css",
    "wwwroot/js/projects/publications-compendium-phase35-contract.test.js"
)
$required += $icons | ForEach-Object { "wwwroot/images/publications/compendium-icons/$_" }

foreach ($path in $required) {
    if (-not (Test-Path (Join-Path $root $path))) {
        throw "Missing Phase 35 file: $path"
    }
}

if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    throw "Node.js is required for Compendium syntax and contract validation."
}

node --check (Join-Path $root "wwwroot/js/pages/projects-compendium.js")
node --check (Join-Path $root "wwwroot/js/pages/projects-compendium-structure-editor.js")
node --check (Join-Path $root "wwwroot/js/pages/projects-compendium-cover-editor.js")
if ($LASTEXITCODE -ne 0) {
    throw "Compendium JavaScript syntax validation failed."
}

$contracts = Get-ChildItem (Join-Path $root "wwwroot/js/projects") `
    -Filter "publications-compendium*contract.test.js" `
    | Sort-Object Name `
    | ForEach-Object { $_.FullName }
node --test $contracts
if ($LASTEXITCODE -ne 0) {
    throw "Compendium JavaScript contract tests failed."
}

foreach ($icon in $icons) {
    $iconPath = Join-Path $root "wwwroot/images/publications/compendium-icons/$icon"
    [xml]$svg = Get-Content $iconPath -Raw
    if ($svg.svg.viewBox -ne "0 0 24 24" -or $svg.svg.width -ne "24" -or $svg.svg.height -ne "24") {
        throw "$icon does not use the shared 24 x 24 programme-icon canvas."
    }

    $source = Get-Content $iconPath -Raw
    $weights = [regex]::Matches($source, 'stroke-width="([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
    if ($weights.Count -eq 0 -or ($weights | Where-Object { $_ -ne "1.8" }).Count -gt 0) {
        throw "$icon does not use the shared 1.8 vector stroke."
    }
    if ($source -match '<(?:image|filter|linearGradient|radialGradient)\b') {
        throw "$icon contains a raster image or unsupported SVG effect."
    }
}

$readService = Get-Content (Join-Path $root "Services/Compendiums/CompendiumReadService.cs") -Raw
$resolver = Get-Content (Join-Path $root "Services/Compendiums/CompendiumProgrammeInformation.cs") -Raw
$dtos = Get-Content (Join-Path $root "Services/Compendiums/CompendiumDtos.cs") -Raw
$readiness = Get-Content (Join-Path $root "Services/Compendiums/CompendiumReadinessPolicy.cs") -Raw
$fingerprint = Get-Content (Join-Path $root "Services/Compendiums/CompendiumReviewFingerprint.cs") -Raw
$builder = Get-Content (Join-Path $root "Utilities/Reporting/CompendiumPdfReportBuilder.cs") -Raw
$css = Get-Content (Join-Path $root "wwwroot/css/pages/projects-publications.css") -Raw
$mainJs = Get-Content (Join-Path $root "wwwroot/js/pages/projects-compendium.js") -Raw
$planner = Get-Content (Join-Path $root "Services/Compendiums/CompendiumDossierPaginationPlanner.cs") -Raw

foreach ($contract in @(
    'project.SponsoringLineDirectorate != null ? project.SponsoringLineDirectorate.Name : null',
    'SponsoringLineDirectorateDisplay = NormalizeDisplay(project.SponsoringLineDirectorate, "Not recorded")',
    'CompendiumPdf_2026-08-15_programme-semantics-v16'
)) {
    if ($readService -notmatch [regex]::Escape($contract)) {
        throw "Authoritative Sponsoring Line Directorate contract is missing: $contract"
    }
}
if ($readService -match 'project\.ArmService\b') {
    throw "The Compendium must not source Arms / Services from Project.ArmService."
}
if ($resolver -notmatch 'string\? sponsoringLineDirectorate' -or
    $resolver -notmatch '"Arms / Services"\s*,\s*cleanSponsoringLineDirectorate') {
    throw "The programme resolver does not map Arms / Services from Sponsoring Line Directorate."
}

$productionSources = $readService + $resolver + $dtos + $readiness + $fingerprint + $builder
if ($productionSources -match '\b(?:ArmServiceDisplay|HasArmService|MissingArmService|missingArmService)\b') {
    throw "A legacy ArmService Compendium alias remains in the production pipeline."
}
if ($readiness -notmatch 'missingSponsoringLineDirectorate' -or
    $fingerprint -notmatch 'compendium-review-v10-sponsoring-line-directorate') {
    throw "Readiness and review identity do not use the authoritative Sponsoring Line Directorate contract."
}

if ($mainJs -notmatch [regex]::Escape('const programmeIconVersion = "v16"')) {
    throw "The v16 browser icon cache identity is missing."
}

$iconRuleMatch = [regex]::Match(
    $css,
    '\.compendium-live-page__programme-icon\{(?<body>[^}]*)\}',
    [System.Text.RegularExpressions.RegexOptions]::Singleline)
if (-not $iconRuleMatch.Success) {
    throw "The programme icon alignment rule is missing."
}
$iconRule = $iconRuleMatch.Groups['body'].Value
if ($iconRule -notmatch 'width:22px' -or $iconRule -notmatch 'height:22px') {
    throw "The invisible 22 x 22 programme icon alignment column is missing."
}
if ($iconRule -match 'border|background|border-radius') {
    throw "Programme icons must not have visible nested tiles in the browser proof."
}
if ($css -notmatch '\.compendium-live-page__programme-icon img\{[^}]*width:18px;[^}]*height:18px') {
    throw "The browser proof does not use the shared 18 x 18 icon artwork size."
}

$composeIconStart = $builder.IndexOf('private static void ComposeProgrammeIcon')
$composeIconEnd = $builder.IndexOf('private static void ComposeTechnicalSpecifications')
if ($composeIconStart -lt 0 -or $composeIconEnd -le $composeIconStart) {
    throw "The PDF programme-icon composition method could not be inspected."
}
$composeIcon = $builder.Substring($composeIconStart, $composeIconEnd - $composeIconStart)
if ($composeIcon -notmatch 'container\.Padding\(2\)\.Element' -or
    $composeIcon -match '\.(?:Background|Border|BorderColor)\(') {
    throw "PDF programme icons must use an unboxed 18-point field."
}
if ($builder -notmatch 'cell\.ConstantItem\(22\)\.Height\(22\)' -or
    $builder -notmatch 'container\.Background\(Forest50\)\.Border\(1\)\.BorderColor\("#D8E5DF"\)') {
    throw "The PDF must retain the shared alignment column and the outer programme panel."
}

if ($planner -notmatch 'specifications\.Count\s*,\s*programmeModuleCount') {
    throw "The IReadOnlyList pagination fix is missing; ScoreCandidate must use specifications.Count."
}

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    dotnet build (Join-Path $root "ProjectManagement.csproj")
    if ($LASTEXITCODE -ne 0) {
        throw "Project build failed."
    }

    $tests = Join-Path $root "ProjectManagement.Tests/ProjectManagement.Tests.csproj"
    if (Test-Path $tests) {
        dotnet test $tests
        if ($LASTEXITCODE -ne 0) {
            throw "Project test suite failed."
        }
    }
} else {
    Write-Warning ".NET SDK not found; dotnet build/test skipped. Run this script on the development workstation."
}

Write-Host "Phase 35 validation complete. No database migration is required. Existing project reviews are intentionally re-evaluated against the corrected source fact."
