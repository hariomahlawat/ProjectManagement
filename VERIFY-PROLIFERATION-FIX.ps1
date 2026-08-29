$ErrorActionPreference = 'Stop'

Write-Host 'Running targeted proliferation regression tests...' -ForegroundColor Cyan
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj --filter 'FullyQualifiedName~ProjectProliferationProfileServiceTests|FullyQualifiedName~ProjectOverviewPresentationContractTests|FullyQualifiedName~CompletedSummaryPresentationContractTests|FullyQualifiedName~ProjectBriefingProliferationCostTests|FullyQualifiedName~CompendiumZeroProliferationCostSemanticsTests'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'Building solution...' -ForegroundColor Cyan
dotnet build .\ProjectManagement.sln
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'Proliferation fix verification completed successfully.' -ForegroundColor Green
