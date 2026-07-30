PRISM Procurement Journey — Unified Full-Width Workspace
========================================================

This package is incremental and should be applied after the Procurement Journey
Final Polish package already in use.

Replace these four files in the ProjectManagement project root while preserving
the folder structure:

1. Pages/Process/Index.cshtml
2. wwwroot/css/process-flow.css
3. wwwroot/js/process-flow.js
4. ProjectManagement.Tests/ProcessJourneyPresentationContractTests.cs

What changes
------------
- Removes the permanent hero header from normal use.
- Keeps the cinematic introduction as an optional accessible dialog.
- Requests the PRISM workspace/full-width page shell.
- Merges the toolbar, process canvas and stage guidance into one outer workspace.
- Removes the visual gap, duplicate rounded corners and duplicate card shadows
  between the process and checklist regions.
- Uses a responsive stage-guidance width so ultrawide monitors give most extra
  space to the process canvas.
- Shows an additional contextual stage hop when the actual process viewport is
  at least 1480px wide.
- Recalculates the process camera when the unified workspace changes size.
- Keeps full-screen, Journey, Complete Map, purpose editing and checklist
  management behaviour intact.

No database migration is required.

Verification
------------
1. Stop the running application.
2. Replace the four files.
3. Run:

   dotnet build
   dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj

4. Start the application and hard-refresh /Process using Ctrl+F5.
5. Verify at normal desktop and ultrawide resolutions:
   - no permanent hero banner;
   - the process workspace begins directly below the Projects sub-navigation;
   - canvas and guidance share one continuous surface;
   - the guidance panel remains readable while extra width goes to the canvas;
   - Introduction opens the optional cinematic overlay;
   - Full screen remains edge-to-edge.
