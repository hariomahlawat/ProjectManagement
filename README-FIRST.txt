PRISM ERP — SDD Institutional Profile Final Refinements
=======================================================

Purpose
-------
This package refines the optional “SDD – Growth over the years” briefing slide without returning to PowerPoint tables.
It is designed to be applied over the latest grouped-shapes implementation.

Implemented refinements
-----------------------
1. Readability and visual hierarchy
   • Larger title, timeline, module-heading, KPI, detail-row and highlight typography.
   • Long module headings use controlled two-line titles instead of shrinking excessively.
   • Timeline remains the open alternating original-style composition with larger authorised milestone text.

2. Reliable module detail rendering
   • Removed tab-stop based label/value composition that caused values such as 10,081 to split.
   • Every numeric module now uses one multi-paragraph label box and one aligned multi-paragraph value box.
   • Each module remains one selectable top-level PowerPoint group.
   • No PowerPoint tables or graphic-frame tables are used by this slide.

3. Partnership module
   • Retains a dedicated institutional-list treatment rather than pretending to be a numeric KPI card.
   • Uses larger list text and controlled spacing.

4. Reusable Profile Footer Strip
   • Replaces the hard-coded Unit Citations configuration with a general user-authored footer strip.
   • User controls exact footer text, optional highlighted value, alignment and style.
   • Available styles: Outline, Solid maroon and Subtle neutral.
   • Available alignment: Centred or Text left / value right.
   • Existing legacy Unit Citation JSON is read automatically and migrated in memory to the new footer-strip configuration.
   • PRISM does not infer, rewrite or append footer-strip content.

5. Footer and data source
   • “Data as on … · Source: PRISM ERP” now appears in the normal slide footer rather than floating above it.
   • The optional profile footer strip remains a separate user-controlled presentation element.

6. Data integrity
   • “Projects Developed” continues to default to original completed projects only.
   • Rebuild projects remain excluded from both the headline and technical-category breakdown unless explicitly included.
   • ERP-backed figures remain read-only.

Replacement procedure
---------------------
1. Back up the current solution.
2. Copy the contents of this folder into the ProjectManagement solution root, preserving paths.
3. Replace the existing files when prompted.
4. Clean the solution and rebuild in Visual Studio.
5. Run the ProjectBriefings test suite.
6. Refresh the browser with Ctrl+F5 so the revised JavaScript and CSS are loaded.
7. Open Deck Settings, review the new Profile Footer Strip options, save, and generate a fresh PowerPoint.

Database
--------
No database migration is required. The new settings remain in the existing versioned deck-configuration JSON.

Validation performed during packaging
--------------------------------------
• project-briefing-decks.js syntax check passed.
• project-briefing-decks.test.js passed 27/27 tests.
• Unified patch dry-run and clean application passed.
• Replacement-file comparison and ZIP integrity checks passed.

Environment limitation
----------------------
The .NET SDK is not available in the packaging environment. A Visual Studio Clean/Rebuild and the .NET ProjectBriefings tests are therefore required after replacement.
