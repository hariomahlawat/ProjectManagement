param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = (Get-Location).Path,

    [Parameter(Mandatory = $false)]
    [switch]$Force
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Download-File {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if ((Test-Path $Destination) -and -not $Force) {
        Write-Host "Exists: $Destination" -ForegroundColor DarkGray
        return
    }

    $directory = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Path $directory -Force | Out-Null

    $temp = "$Destination.download"
    if (Test-Path $temp) {
        Remove-Item $temp -Force
    }

    Write-Host "Downloading $(Split-Path -Leaf $Destination)..."
    Invoke-WebRequest -Uri $Url -OutFile $temp -UseBasicParsing

    $length = (Get-Item $temp).Length
    if ($length -lt 4096) {
        Remove-Item $temp -Force -ErrorAction SilentlyContinue
        throw "Downloaded file is unexpectedly small: $Destination ($length bytes)."
    }

    Move-Item $temp $Destination -Force
}

$projectRootFull = [IO.Path]::GetFullPath($ProjectRoot)
$fontRoot = Join-Path $projectRootFull "wwwroot\fonts\publications"
$dmSansRoot = Join-Path $fontRoot "dm-sans"
$alatsiRoot = Join-Path $fontRoot "alatsi"

Write-Step "Preparing PRISM publication font folders"
New-Item -ItemType Directory -Path $dmSansRoot -Force | Out-Null
New-Item -ItemType Directory -Path $alatsiRoot -Force | Out-Null

# DM Sans static TTF files from the official Google Fonts DM Fonts repository.
$dmBase = "https://raw.githubusercontent.com/googlefonts/dm-fonts/main/Sans/fonts/ttf"
$dmFiles = @(
    "DMSans-Regular.ttf",
    "DMSans-Medium.ttf",
    "DMSans-SemiBold.ttf",
    "DMSans-Bold.ttf",
    "DMSans-Italic.ttf",
    "DMSans-BoldItalic.ttf"
)

Write-Step "Downloading DM Sans static TTF files"
foreach ($file in $dmFiles) {
    Download-File `
        -Url "$dmBase/$file" `
        -Destination (Join-Path $dmSansRoot $file)
}

Write-Step "Downloading DM Sans licence"
Download-File `
    -Url "https://raw.githubusercontent.com/googlefonts/dm-fonts/main/Sans/OFL.txt" `
    -Destination (Join-Path $dmSansRoot "OFL.txt")

# Alatsi is available in the official Google Fonts repository.
Write-Step "Downloading Alatsi"
Download-File `
    -Url "https://raw.githubusercontent.com/google/fonts/main/ofl/alatsi/Alatsi-Regular.ttf" `
    -Destination (Join-Path $alatsiRoot "Alatsi-Regular.ttf")

Write-Step "Downloading Alatsi licence"
Download-File `
    -Url "https://raw.githubusercontent.com/google/fonts/main/ofl/alatsi/OFL.txt" `
    -Destination (Join-Path $alatsiRoot "OFL.txt")

Write-Step "Validating installed publication fonts"

$required = @(
    (Join-Path $dmSansRoot "DMSans-Regular.ttf"),
    (Join-Path $dmSansRoot "DMSans-Medium.ttf"),
    (Join-Path $dmSansRoot "DMSans-SemiBold.ttf"),
    (Join-Path $dmSansRoot "DMSans-Bold.ttf"),
    (Join-Path $dmSansRoot "DMSans-Italic.ttf"),
    (Join-Path $dmSansRoot "DMSans-BoldItalic.ttf"),
    (Join-Path $alatsiRoot "Alatsi-Regular.ttf")
)

$missing = @()
foreach ($path in $required) {
    if (-not (Test-Path $path)) {
        $missing += $path
        continue
    }

    $length = (Get-Item $path).Length
    if ($length -lt 4096) {
        $missing += "$path (invalid/small file)"
    }
}

if ($missing.Count -gt 0) {
    Write-Host ""
    Write-Host "Publication font setup FAILED." -ForegroundColor Red
    foreach ($item in $missing) {
        Write-Host "  - $item" -ForegroundColor Red
    }
    exit 1
}

Write-Host ""
Write-Host "Publication fonts are ready." -ForegroundColor Green
Write-Host ""
Write-Host "DM Sans:" -ForegroundColor White
foreach ($file in $dmFiles) {
    $path = Join-Path $dmSansRoot $file
    Write-Host ("  {0,-28} {1,10:N0} bytes" -f $file, (Get-Item $path).Length)
}
Write-Host ""
Write-Host "Alatsi:" -ForegroundColor White
$alatsiPath = Join-Path $alatsiRoot "Alatsi-Regular.ttf"
Write-Host ("  {0,-28} {1,10:N0} bytes" -f "Alatsi-Regular.ttf", (Get-Item $alatsiPath).Length)

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Rebuild/publish PRISM."
Write-Host "  2. Deploy the wwwroot\fonts\publications folder with the application."
Write-Host "  3. Restart the IIS application pool / PRISM process."
