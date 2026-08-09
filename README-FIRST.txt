PRISM ERP — Notebook System Note Floating-Surface Fix
Date: 09 Aug 2026

PURPOSE
This is a focused follow-up to the Keep-style direct-drag Notebook package.
It fixes the remaining PRISM Conference Directions system-note interaction issues
without changing Conference data, permissions, database schema, or normal notes.

FIXES INCLUDED
1. Conference Directions colour palette is no longer clipped by the card.
2. The system-note palette is collision-positioned at runtime inside the Notebook
   content viewport, so it does not spill over the left navigation rail or viewport.
3. The palette prefers opening above the trigger, flips below when required, and
   repositions on resize/scroll. Mobile retains the existing fixed picker behaviour.
4. The Conference card releases overflow only while a menu/palette is open.
5. The system-note More menu is wide enough to keep "Remove from My Notebook"
   on one line.
6. Shared with me now shows a subtle "In My Notebook" state when applicable.
7. If the system note is removed from My Notebook while still on Shared with me,
   the action updates immediately back to "Add to My Notebook" without a reload.

FILES TO REPLACE
Pages/Notebook/_NotebookConferenceDigest.cshtml
wwwroot/css/notebook.css
wwwroot/js/notebook/notebook-app.js

OPTIONAL REGRESSION TEST
wwwroot/js/notebook/notebook-system-floating-surfaces-contract.test.js

BASELINE
Apply this on top of the previously supplied Keep-style direct-drag implementation.

BUILD
Because notebook-app.js changed, regenerate the Notebook bundle/assets used by the
application, then rebuild the .NET solution. Typical project commands:

  npm ci
  npm run build:notebook
  npm run check:notebook-assets
  npm test
  dotnet build
  dotnet test

Then hard-refresh Notebook (Ctrl+F5).

NO DATABASE / EF MIGRATION CHANGES
