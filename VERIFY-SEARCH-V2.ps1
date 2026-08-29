param(
    [switch]$FullTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Require-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found in PATH."
    }
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

Write-Host "PRISM Search V2 convergence verification" -ForegroundColor Cyan
Write-Host "Project root: $root"

Require-Command dotnet

if (Get-Command node -ErrorAction SilentlyContinue) {
    Write-Host "[1/6] Search V2 source contract" -ForegroundColor Yellow
    node tools/test-search-v2-contract.mjs
    if ($LASTEXITCODE -ne 0) { throw "Search V2 source contract failed." }

    Write-Host "[2/6] Search JavaScript/tool syntax" -ForegroundColor Yellow
    node --check wwwroot/js/pages/search.js
    if ($LASTEXITCODE -ne 0) { throw "Search page JavaScript syntax check failed." }

    $moduleTemp = Join-Path $env:TEMP "prism-global-search-$PID.mjs"
    Copy-Item wwwroot/js/navigation/global-search.js $moduleTemp -Force
    try {
        node --check $moduleTemp
        if ($LASTEXITCODE -ne 0) { throw "Global search module JavaScript syntax check failed." }
    }
    finally {
        Remove-Item $moduleTemp -Force -ErrorAction SilentlyContinue
    }

    node --check tools/search-v2-relevance-evaluator.mjs
    if ($LASTEXITCODE -ne 0) { throw "Search relevance evaluator syntax check failed." }
} else {
    Write-Warning "Node.js not found; JavaScript/source-contract checks were skipped."
}

Write-Host "[3/6] Restore" -ForegroundColor Yellow
dotnet restore ProjectManagement.csproj
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }

Write-Host "[4/6] Build application" -ForegroundColor Yellow
dotnet build ProjectManagement.csproj --no-restore
if ($LASTEXITCODE -ne 0) { throw "application build failed." }

Write-Host "[5/6] Build tests" -ForegroundColor Yellow
dotnet build ProjectManagement.Tests/ProjectManagement.Tests.csproj --no-restore
if ($LASTEXITCODE -ne 0) { throw "test-project build failed." }

Write-Host "[6/6] Search V2 regression tests" -ForegroundColor Yellow
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj --no-build --filter "FullyQualifiedName~SearchV2"
if ($LASTEXITCODE -ne 0) { throw "Search V2 regression tests failed." }

if ($env:PRISM_SEARCHV2_TEST_CONNECTION) {
    Write-Host "Real PostgreSQL Search V2 tests were enabled through PRISM_SEARCHV2_TEST_CONNECTION." -ForegroundColor Green
} else {
    Write-Host "Real PostgreSQL smoke tests were not activated. Set PRISM_SEARCHV2_TEST_CONNECTION to exercise FTS/pg_trgm against a test DB." -ForegroundColor DarkYellow
}

if ($FullTests) {
    Write-Host "Running complete .NET test suite..." -ForegroundColor Yellow
    dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj --no-build
    if ($LASTEXITCODE -ne 0) { throw "Full .NET test suite failed." }
}

Write-Host "Search V2 convergence verification completed successfully." -ForegroundColor Green
