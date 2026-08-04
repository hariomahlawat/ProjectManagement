PRISM STANDARD BRIEFING — MAROON HEADER REFINEMENT
===================================================

Replace the enclosed files at the same project-relative paths.
This package is based on the previously delivered Standard Briefing status-strip implementation and retains all of those changes.

IMPLEMENTED
-----------
1. Project Brief slides no longer render the redundant subtitle line:
   "PROJECT BRIEF · <stage> · <category> · <technical category>".
   The in-panel PROJECT BRIEF heading remains unchanged.

2. The thin top rule used by the Standard PRISM Briefing now uses a dedicated institutional maroon accent:
   - Editorial Light: #7A263A
   - Graphite Dark:  #A33A4E

3. The cover top rule and all Standard briefing slide headers use the same maroon family for visual consistency.

4. Operational/status blue, narrative teal and proliferation green remain unchanged. The maroon colour is isolated through a new HeaderAccent theme semantic and is not reused for data meaning.

5. Theme previews in Deck Settings now show the same maroon header colour as the generated PowerPoint.

UNCHANGED
---------
- Project title, logos, divider, panels, photograph, Project Brief content and bottom status/cost strip.
- Project Update Sheet header colours.
- Present Status and cost semantics.
- Data sources and PowerPoint generation workflow.

DATABASE
--------
No database migration is required.

VALIDATION
----------
- JavaScript regression suite: 25/25 passed.
- Unified patch dry-run and application verification passed.
- Archive integrity and replacement-file checks passed.
- .NET compilation could not be executed because the .NET SDK is not installed in the execution environment.

After replacement, perform Clean Solution, Rebuild Solution and run the ProjectBriefings tests in Visual Studio.
