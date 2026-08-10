param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = (Get-Location).Path
)

$ErrorActionPreference = "Stop"

$projectRootFull = [IO.Path]::GetFullPath($ProjectRoot)
$fontRoot = Join-Path $projectRootFull "wwwroot\fonts\publications"

$expected = @(
    "dm-sans\DMSans-Regular.ttf",
    "dm-sans\DMSans-Medium.ttf",
    "dm-sans\DMSans-SemiBold.ttf",
    "dm-sans\DMSans-Bold.ttf",
    "dm-sans\DMSans-Italic.ttf",
    "dm-sans\DMSans-BoldItalic.ttf",
    "dm-sans\OFL.txt",
    "alatsi\Alatsi-Regular.ttf",
    "alatsi\OFL.txt"
)

$failed = $false

Write-Host "PRISM publication font check" -ForegroundColor Cyan
Write-Host "Root: $fontRoot"
Write-Host ""

foreach ($relative in $expected) {
    $path = Join-Path $fontRoot $relative

    if (-not (Test-Path $path)) {
        Write-Host "[MISSING] $relative" -ForegroundColor Red
        $failed = $true
        continue
    }

    $size = (Get-Item $path).Length
    if ($relative.EndsWith(".ttf") -and $size -lt 4096) {
        Write-Host "[INVALID] $relative ($size bytes)" -ForegroundColor Red
        $failed = $true
        continue
    }

    Write-Host "[OK]      $relative" -ForegroundColor Green
}

Write-Host ""

if ($failed) {
    Write-Host "Font package is incomplete." -ForegroundColor Red
    exit 1
}

Write-Host "Font package is complete." -ForegroundColor Green
exit 0
