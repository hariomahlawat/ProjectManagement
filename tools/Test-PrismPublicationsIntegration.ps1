param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = (Get-Location).Path
)

$ErrorActionPreference = "Stop"

function Resolve-ProjectPath([string]$RelativePath) {
    return Join-Path ([IO.Path]::GetFullPath($ProjectRoot)) $RelativePath
}

function Assert-File([string]$RelativePath) {
    $path = Resolve-ProjectPath $RelativePath
    if (-not (Test-Path $path)) {
        Write-Host "[FAIL] Missing $RelativePath" -ForegroundColor Red
        $script:failed = $true
        return $null
    }

    Write-Host "[OK]   $RelativePath" -ForegroundColor Green
    return $path
}

function Assert-Contains([string]$Path, [string]$Text, [string]$Description) {
    if (-not $Path) { return }

    $content = Get-Content -Raw -Path $Path
    if ($content.Contains($Text)) {
        Write-Host "[OK]   $Description" -ForegroundColor Green
        return
    }

    Write-Host "[FAIL] $Description" -ForegroundColor Red
    $script:failed = $true
}

$failed = $false
Write-Host "PRISM Publications integration check" -ForegroundColor Cyan
Write-Host "Project root: $([IO.Path]::GetFullPath($ProjectRoot))"
Write-Host ""

$program = Assert-File "Program.cs"
$nav = Assert-File "Services\Navigation\ModuleNav\ProjectModuleNavDefinition.cs"
Assert-File "Services\Publications\PublicationServiceCollectionExtensions.cs" | Out-Null
Assert-File "Services\Publications\PublicationRuntimeValidationHostedService.cs" | Out-Null
Assert-File "Services\Publications\BrochurePublicationService.cs" | Out-Null
Assert-File "Services\Publications\BrochurePhotoService.cs" | Out-Null
Assert-File "Utilities\Reporting\BrochurePdfReportBuilder.cs" | Out-Null
Assert-File "Utilities\Reporting\PublicationFontRegistry.cs" | Out-Null
Assert-File "Pages\Projects\Publications\Index.cshtml" | Out-Null
Assert-File "Pages\Projects\Publications\Brochure\Index.cshtml" | Out-Null
Assert-File "Pages\Projects\Publications\Brochure\Index.cshtml.cs" | Out-Null
Assert-File "Pages\Projects\Publications\Compendium\Index.cshtml" | Out-Null
Assert-File "Pages\Projects\Publications\Compendium\Index.cshtml.cs" | Out-Null

Write-Host ""
Write-Host "Critical registration contracts" -ForegroundColor Cyan
Assert-Contains $program "using ProjectManagement.Services.Publications;" "Program.cs imports Publications services"
Assert-Contains $program "builder.Services.AddProjectPublications();" "Program.cs registers Publications services"
Assert-Contains $program 'options.Conventions.AuthorizeFolder("/Projects/Publications");' "Publications route family is authorized"
Assert-Contains $nav 'Text = "Publications"' "Projects navigation contains Publications"
Assert-Contains $nav 'Page = "/Projects/Publications/Index"' "Projects navigation targets Publications workspace"
Assert-Contains $nav 'ActivePagePrefix = "/Projects/Publications/"' "Publications navigation remains active on child pages"

Write-Host ""
Write-Host "Offline font status" -ForegroundColor Cyan
$fontRoot = Resolve-ProjectPath "wwwroot\fonts\publications"
$dm = @(
    "dm-sans\DMSans-Regular.ttf",
    "dm-sans\DMSans-Medium.ttf",
    "dm-sans\DMSans-SemiBold.ttf",
    "dm-sans\DMSans-Bold.ttf",
    "dm-sans\DMSans-Italic.ttf",
    "dm-sans\DMSans-BoldItalic.ttf"
)

$missingFonts = @($dm | Where-Object { -not (Test-Path (Join-Path $fontRoot $_)) })
if ($missingFonts.Count -eq 0) {
    Write-Host "[OK]   DM Sans static publication font set is present." -ForegroundColor Green
} else {
    Write-Host "[WARN] DM Sans is incomplete. Brochure will use QuestPDF Lato fallback." -ForegroundColor Yellow
    foreach ($font in $missingFonts) {
        Write-Host "       Missing: $font" -ForegroundColor Yellow
    }
}

$alatsi = Join-Path $fontRoot "alatsi\Alatsi-Regular.ttf"
if (Test-Path $alatsi) {
    Write-Host "[OK]   Alatsi display font is present." -ForegroundColor Green
} else {
    Write-Host "[INFO] Alatsi is optional; Cover A will use the primary publication family." -ForegroundColor DarkGray
}

Write-Host ""
if ($failed) {
    Write-Host "PUBLICATIONS INTEGRATION CHECK FAILED" -ForegroundColor Red
    Write-Host "Do not deploy until every [FAIL] item is corrected." -ForegroundColor Red
    exit 1
}

Write-Host "PUBLICATIONS INTEGRATION CHECK PASSED" -ForegroundColor Green
Write-Host "Next: clean/build/test PRISM, restart IIS, then open /Projects/Publications/Brochure." -ForegroundColor Green
exit 0
