PRISM PROCUREMENT JOURNEY — LIGHT, JOURNEY-ONLY WORKSPACE
=========================================================

BASELINE
--------
Apply this package after PRISM_Procurement_Journey_Unified_Workspace_20260730.

REPLACEMENT
-----------
Copy the four project files into the ProjectManagement project root while
preserving the included folder structure.

IMPLEMENTED
-----------
1. Removed the permanent white command/header area.
2. Removed the user-facing Complete Map mode; Journey is now the sole process view.
3. Added a light operational theme as the default.
4. Preserved the dark immersive theme as a user-selectable option.
5. Added a compact floating utility dock inside the journey canvas:
   - Search/jump
   - Light/dark theme
   - Introduction
   - Full screen
   - Print
6. Search/jump now opens as a command-palette dialog; press / to open it.
7. Theme choice is remembered locally and requires no internet connection.
8. The unified journey/checklist workspace calculates and uses the available
   viewport height instead of leaving a large unused header area.
9. Wide-monitor behaviour remains responsive; additional width is assigned
   primarily to the process canvas while the checklist retains a bounded width.
10. Existing purpose/checklist APIs, permissions, audit and concurrency logic
    are unchanged.

DATABASE
--------
No database migration is required.

VERIFY
------
dotnet build
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj

After replacement, hard-refresh /Process once with Ctrl+F5.
