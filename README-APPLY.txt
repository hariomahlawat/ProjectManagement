PRISM test-constructor compatibility fix
========================================

Copy the ProjectManagement.Tests folder from this package into the ProjectManagement project root and allow the five files to be added/replaced.

Changed files:
1. ProjectManagement.Tests/ProjectPhotoPageTests.cs
2. ProjectManagement.Tests/ProjectOverviewLifecycleTests.cs
3. ProjectManagement.Tests/ProjectMetaEditPageTests.cs
4. ProjectManagement.Tests/ProjectBriefings/ProjectBriefingSlideComposerTests.cs
5. ProjectManagement.Tests/Fakes/ThrowingProjectContentService.cs

Corrections:
- Supplies the new IProjectContentService dependency to OverviewModel test constructors.
- Supplies the new IClock dependency to the project metadata EditModel test constructor.
- Corrects ProjectBriefingCostBasis.Aon to the authoritative enum member ProjectBriefingCostBasis.AoN.
- Uses a fail-fast shared project-content test double so unrelated page tests do not silently execute content commands.

No application code or database migration is changed.

Verify:
  dotnet build
  dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj
