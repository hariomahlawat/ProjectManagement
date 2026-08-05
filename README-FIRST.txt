PRISM ERP — SDD Institutional Profile: Final Projection Polish
===============================================================

Replace the files in this package at the exact relative paths shown below.
The code is based on the previously supplied “SDD Profile Header Polish” version.

Replacement files
-----------------
1. Services/ProjectBriefings/Presentation/ProjectBriefingSlideComposer.InstitutionalProfile.cs
2. ProjectManagement.Tests/ProjectBriefings/ProjectBriefingSlideComposerTests.cs

Implemented refinements
-----------------------
- Slightly larger timeline years, descriptions and milestone markers.
- Stronger chronology line for projection.
- Larger module headings, KPI values, detail labels and detail values.
- Larger, better-spaced institutional-partnership entries.
- Sparse IPR and partnership content starts higher and uses improved row spacing.
- Training highlight is taller and always splits “... trained ...” into a controlled two-line message.
- Light-theme module header fills are modestly more saturated while remaining restrained.
- Profile Footer Strip is shorter and uses a quieter divider-colour outline so it remains supporting content.
- Existing grouped-shape architecture, module selection, project scope and ERP-backed values remain unchanged.
- Original completed projects continue to exclude rebuilds by default.
- No database migration is required.

Deployment
----------
1. Back up the two existing files.
2. Replace them with the files in this package.
3. Clean and rebuild the solution in Visual Studio.
4. Run the ProjectBriefings test suite.
5. Restart the application and generate both Editorial Light and Graphite Dark decks for visual verification.

Validation performed in packaging environment
---------------------------------------------
- Unified patch dry-run and clean application: passed.
- Patched-file byte comparison against packaged replacements: passed.
- C# delimiter/structure checks: passed.
- ZIP and checksum verification: passed.

The .NET SDK was not available in the packaging environment, so compilation and xUnit execution must be performed in Visual Studio.
