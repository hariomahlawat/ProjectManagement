PRISM Project Briefing Dependency-Injection Registration Fix
=============================================================

Root cause
----------
The latest Program.cs replacement omitted the scoped registration for
IProjectBriefingUpdateSheetFactsResolver. ProjectBriefingDataService requires that
resolver, so ASP.NET Core failed while validating the service graph at startup.

Apply
-----
Copy the contents of this package into the ProjectManagement project root while
preserving the folder structure. Replace Program.cs and add the test file.

The required registration is:

builder.Services.AddScoped<IProjectBriefingUpdateSheetFactsResolver,
    ProjectBriefingUpdateSheetFactsResolver>();

It is placed in the Project briefing decks registration section before
IProjectBriefingDataService.

No database migration is required.

Verification
------------
dotnet build
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj

Then start the application. The service-provider validation error should no longer occur.
