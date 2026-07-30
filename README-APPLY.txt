PRISM Procurement Journey — Cinematic Refinement
================================================

BASELINE
--------
Apply this package after PRISM_Procurement_Journey_Redesign_20260730.
The four files in this package are full replacement files.

INSTALLATION
------------
1. Stop the running PRISM application or IIS application pool.
2. Copy the package contents into the ProjectManagement project root.
3. Preserve the folder structure and replace the four existing files.
4. Rebuild and test:

   dotnet build
   dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj

5. Publish/restart the application.
6. Hard-refresh the Process page once (Ctrl+F5) so the new JavaScript and CSS are loaded.

NO DATABASE MIGRATION
---------------------
This refinement changes only the Process page presentation and interaction layer.
The existing purpose, checklist, audit, concurrency and dependency data remain unchanged.

KEY BEHAVIOUR
-------------
- Journey mode progressively reveals only the active stage, nearby context, and any relevant branch/convergence.
- Branches are detected from the actual dependency graph; TEC/BM are not hard-coded into the presentation logic.
- The active stage is materially larger, while context recedes with scale, opacity and atmospheric blur.
- Active route segments carry an animated directional signal.
- Complete Map uses semantic zoom: compact code-first nodes when fitted, fuller labels when zoomed.
- Search and jump are consolidated into one stage navigator.
- The introductory hero automatically compacts after use and can be reopened with Introduction.
- Full-screen mode includes a persistent in-application Exit full screen control.
- Empty checklists use a compact state and expose Add first checklist item to authorised users.
- All functionality remains offline; no CDN, remote asset or internet dependency was added.
