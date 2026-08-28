$ErrorActionPreference = 'Stop'

Write-Host 'Running PRISM test suite...' -ForegroundColor Cyan
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'Building PRISM application...' -ForegroundColor Cyan
dotnet build .\ProjectManagement.csproj
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'Verification completed successfully.' -ForegroundColor Green
