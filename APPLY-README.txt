PRISM — PROJECT BRIEFING UPDATE SHEET IMPLEMENTATION
====================================================

PURPOSE
-------
This package adds a second presentation template to the Project Briefing Deck Builder:

  Project Update Sheets

The established Standard PRISM Briefing remains available and retains its existing
behaviour. The new template generates native, editable 16:9 PowerPoint project sheets.

IMPLEMENTED BUSINESS RULES
--------------------------
1. Project Cost: authoritative R&D cost only, using the existing L1 -> AoN -> IPA resolver.
2. AoN Date: AoN-stage completion date.
3. PDC Date: Development-stage PlannedDue only when the project's current stage is DEVP;
   deliberately blank for every other current stage.
4. Present Status: latest external remark.
5. Project Officer: rank and full name of the assigned Lead Project Officer.
6. Line Directorate: Sponsoring Line Directorate.
7. Name of Firm: all distinct JDP names linked with the project.
8. ARPP/PPP details: latest authoritative published ARPP row, ordered by latest financial
   year and latest issue/addendum position.
9. Project brief: the dedicated Project Brief field.
10. Photograph: existing PowerPoint-ready cover-photo resolver and loader.
11. Slide size: 16:9.
12. Sequence: optional cover slide, optional portfolio-summary slide, then one editable
    project update sheet for each selected project.

HOW TO APPLY
------------
1. Back up the project source and database.
2. Copy the CONTENTS of this folder into the ProjectManagement project root.
3. Preserve the folder structure and overwrite the listed files.
4. Apply the included EF Core migration through your normal deployment process.
5. Alternatively, the supplied IMPLEMENTATION.patch can be applied from the project root
   with: patch -p1 < IMPLEMENTATION.patch
6. Restore, build and test before production deployment:

   dotnet restore
   dotnet ef database update
   dotnet build
   dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj

The project is configured to run EF migrations at startup in the existing deployment
model, but the migration should still be reviewed and tested against a copy of the
production database before release.

USER WORKFLOW
-------------
1. Open My Workspace -> Project Briefing Deck Builder.
2. Open or create a saved deck.
3. Expand Deck settings.
4. Under Presentation template, choose Project Update Sheets.
5. Select whether to include the cover and portfolio-summary slides.
6. Save settings and generate the PowerPoint.

IMPLEMENTATION NOTES
--------------------
- No second binary .pptx template is required. The layout is generated with native Open
  XML text boxes, editable table cells, shapes and images over the existing 16:9 briefing
  foundation.
- The new factual resolver uses bounded batch queries and avoids an N+1 database pattern.
- Missing information is surfaced as template-specific readiness warnings. Generation is
  still permitted; missing values are rendered as "Not recorded", except PDC outside the
  Development stage, which is intentionally blank.
- Existing saved decks default to StandardBriefing through the migration.
- Existing Standard PRISM Briefing output is preserved.

VALIDATION COMPLETED IN THE DELIVERY ENVIRONMENT
------------------------------------------------
- JavaScript syntax validation passed.
- Changed C# files passed delimiter/static structural checks.
- Project and test .csproj XML parsing passed.
- Migration registration and immutable migration ID were checked.
- The full .NET build and test suite could not be executed in this delivery environment
  because the .NET SDK is not installed. Run the commands above before deployment.
