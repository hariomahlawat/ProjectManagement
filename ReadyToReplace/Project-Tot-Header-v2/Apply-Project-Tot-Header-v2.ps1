param(
    [string]$ProjectRoot = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'

$base = 'https://raw.githubusercontent.com/hariomahlawat/ProjectManagement/master/ReadyToReplace/Project-Tot-Header-v2'
$files = @(
    'Pages/Projects/_ProjectCommandHeader.cshtml',
    'Pages/Projects/_ProjectWorkspaceHosts.cshtml',
    'Pages/Projects/_ProjectTotDrawer.cshtml',
    'Pages/Projects/Overview.Tot.cs',
    'Pages/Projects/Overview.cshtml',
    'wwwroot/js/projects/overview-tot.js',
    'wwwroot/css/pages/project-tot-drawer.css'
)

$projectFile = Get-ChildItem -Path $ProjectRoot -Filter '*.csproj' -File | Select-Object -First 1
if (-not $projectFile) {
    throw "No .csproj file was found in '$ProjectRoot'. Run this script from the ProjectManagement project directory or pass -ProjectRoot."
}

foreach ($relativePath in $files) {
    $destination = Join-Path $ProjectRoot ($relativePath -replace '/', [IO.Path]::DirectorySeparatorChar)
    $directory = Split-Path -Parent $destination
    if (-not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $url = "$base/$relativePath"
    Write-Host "Downloading $relativePath"
    Invoke-WebRequest -Uri $url -OutFile $destination -UseBasicParsing
}

Write-Host ''
Write-Host 'Transfer of Technology header-card files applied successfully.' -ForegroundColor Green
Write-Host 'Run: dotnet clean; dotnet build; dotnet test'
