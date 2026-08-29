PRISM — PROLIFERATION VALIDATION + ZERO-COST SEMANTICS FIX
=========================================================

Apply from the ProjectManagement project root.

1. Back up the current files (or commit your working tree).
2. Copy the contents of this package to the project root, preserving folders.
3. Overwrite the listed existing files. The two new regression-test files may be added.
4. No EF Core migration is required.
5. Run VERIFY-PROLIFERATION-FIX.ps1, or run the commands shown below manually.

WHAT THIS FIXES
---------------
- Proliferation POST no longer fails because unrelated Project Content RowVersion fields are absent.
- Proliferation validation is scoped to ProliferationInput.* only.
- 0 is a valid, explicit proliferation cost.
- blank/null still means "cost not recorded".
- negative proliferation cost remains invalid.
- Project Overview, Completed Summary and legacy Meta editors use the same zero-cost rule.
- Project Overview displays zero as "₹0 lakh" rather than "Cost not recorded".
- Project Briefing Deck cost resolution preserves zero as a recorded value and displays it as ₹0.
- Compendium retains the zero-cost distinction but reports it as information rather than a data-quality warning.

TARGETED VERIFICATION
---------------------
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj --filter "FullyQualifiedName~ProjectProliferationProfileServiceTests|FullyQualifiedName~ProjectOverviewPresentationContractTests|FullyQualifiedName~CompletedSummaryPresentationContractTests|FullyQualifiedName~ProjectBriefingProliferationCostTests|FullyQualifiedName~CompendiumZeroProliferationCostSemanticsTests"
dotnet build .\ProjectManagement.sln

EXPECTED BUSINESS SEMANTICS
---------------------------
blank  = no proliferation cost recorded
0      = explicit zero proliferation cost
> 0    = recorded proliferation cost
< 0    = invalid
