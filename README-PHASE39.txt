PRISM Compendium — Phase 39 Generation Reliability
===================================================

Purpose
-------
This package contains only the files changed for the Compendium generation-reliability closure pass.
Copy the files over the matching paths in the ProjectManagement project, preserving the directory structure.
No database migration or configuration change is required.

Implemented
-----------
1. Fixes the production JavaScript error: "ReferenceError: page is not defined" in the Final Output dock observer.
2. Makes Structure Editor handoff lossless for unsaved publication identity, handling marking, cover design and photo preferences.
3. Prevents navigation to Structure Editor when unsaved state cannot be safely stored in sessionStorage.
4. Normalises stale explicit cover-image slots when a project is deselected and also blocks stale server-side cover assignments during preflight.
5. Brings automatic cover-image readiness closer to export resolution and adds automatic-cover low-resolution warnings.
6. Makes the PDF composition verifier honour the effective cover identity: hidden/custom front title and hidden/custom back edition no longer cause false verification failures.
7. Converts malformed publication photo-preference state into controlled validation responses instead of unhandled generation/preflight failures.
8. Returns safe, actionable generation error codes plus the ASP.NET TraceIdentifier so production failures can be correlated with server logs.
9. Replaces the unexplained about:blank preview tab with a temporary "Preparing Compendium preview…" state.
10. Debounces comprehensive preflight while users type publication identity fields, reducing repeated database/image probe work.
11. Adds Phase 39 regression coverage for the above contracts and an executable Structure handoff round-trip test.

Changed source files
--------------------
Pages\Projects\Publications\Compendium\Index.cshtml.cs
Utilities\Reporting\CompendiumPdfCompositionVerifier.cs
wwwroot\js\pages\projects-compendium.js
wwwroot\js\pages\projects-compendium-structure-editor.js
wwwroot\js\projects\compendium-structure-state.js
wwwroot\js\projects\publications-compendium-phase39-reliability.test.js

Validation completed in the delivery environment
------------------------------------------------
PASS  node --check wwwroot/js/pages/projects-compendium.js
PASS  node --check wwwroot/js/pages/projects-compendium-structure-editor.js
PASS  node --check wwwroot/js/projects/compendium-structure-state.js
PASS  node --test wwwroot/js/projects/*compendium*.test.js
      227 tests passed; 0 failed.

The delivery environment does not contain the .NET SDK, so C# compilation/tests could not be executed here.
Run the following on the development machine after copying the files:

  dotnet build .\ProjectManagement.csproj
  dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
  node --check .\wwwroot\js\pages\projects-compendium.js
  node --check .\wwwroot\js\pages\projects-compendium-structure-editor.js
  node --check .\wwwroot\js\projects\compendium-structure-state.js
  node --test .\wwwroot\js\projects\*compendium*.test.js

Production smoke test
---------------------
A. Load the same 67-project Compendium used in the reported failure; confirm readiness can remain "Ready with warnings" and Preview PDF no longer throws the Final Output observer error.
B. Cover Editor -> Back -> Clean -> Save -> Preview PDF. The composition verifier must not require a hidden back edition.
C. Set a custom front title -> Save -> Preview PDF. The verifier must validate the rendered custom title, not the base publication title.
D. Make an unsaved title/handling/cover change -> Structure Editor -> return. All unsaved state must still be present and the Compendium must remain marked Modified.
E. Assign an explicit cover image from a selected project, then remove that project. The client should recover that slot to Automatic; stale server state must appear as a preflight blocker rather than fail only at export.
F. If PDF generation still encounters an independent composition/layout fault, the alert now displays the safe failure reason where available and a Reference/TraceId. Use that value to locate the exact server exception in logs.
