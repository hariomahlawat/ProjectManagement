PRISM — SDD Institutional Profile Header & Readability Polish
=============================================================

Replace the two files below at the same project-relative paths:

1. Services/ProjectBriefings/Presentation/ProjectBriefingSlideComposer.InstitutionalProfile.cs
2. ProjectManagement.Tests/ProjectBriefings/ProjectBriefingSlideComposerTests.cs

Implemented refinements
-----------------------
- Uses the same standard PRISM slide header as the rest of the briefing deck:
  thin maroon top rule, centred normal title, standard branding and divider.
- Removes the special filled maroon title banner from the SDD profile slide.
- Moves and strengthens the timeline to fit beneath the standard header.
- Slightly enlarges timeline years, milestone text, line and markers.
- Increases module heading, KPI, detail-row, value and partnership-list typography.
- Improves sparse IPR/partnership module spacing while retaining top alignment.
- Gives the training highlight a readable two-line treatment where required.
- Makes the configurable Profile Footer Strip adaptive:
  compact and centred for short text, widening only for longer content.
- Preserves grouped-shape architecture, authoritative PRISM data, five-module
  layout, original-completed-project default and existing configuration JSON.
- No database migration is required.

Deployment
----------
1. Back up the existing files.
2. Replace both files.
3. Clean and rebuild the solution in Visual Studio.
4. Run the ProjectBriefings tests.
5. Refresh the browser with Ctrl+F5 before generating the presentation.

The unified patch is included under _PATCH for review or patch-based application.
