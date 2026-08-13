PRISM Publications Phase 22 — Compile Fix
==========================================

Replace these three files at their exact project paths:

1. Services/Compendiums/CompendiumReadService.cs
2. Services/Compendiums/CompendiumExportService.cs
3. Services/Publications/CompendiumPresetService.cs

Errors fixed
------------
CS0173 at CompendiumReadService: nullable proliferation availability is bool? in the current PRISM model.
- Empty dictionary branches now use Dictionary<int, bool?>.
- UI/PDF bool flags use `availableForProliferation == true`.
- Legacy automatic Compendium includes only explicit true values.
- Explicit false is counted as excluded; null/missing is counted as availability not assessed.

CS1503 at CompendiumReadService:
- BuildIssues receives a non-nullable bool using `availableForProliferation == true`.

CS0103 TimeZoneHelper in CompendiumReadService, CompendiumExportService and CompendiumPresetService:
- Added `using ProjectManagement.Utilities;`, matching the existing PRISM TimeZoneHelper namespace.

Validation performed in this environment
----------------------------------------
- Node syntax check: PASS
- Publications Compendium contract tests: 12/12 PASS
- Static checks for all six reported compiler error patterns: PASS

The .NET SDK is not installed in the generation environment, so run the normal local build after replacement:

    dotnet build .\ProjectManagement.csproj
    dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
    node --check .\wwwroot\js\pages\projects-compendium.js
    node --test .\wwwroot\js\projects\publications-compendium-contract.test.js
