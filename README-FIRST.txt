PRISM ERP — Notebook Keep-Style Direct Drag & Floating-UI Hardening
Ready-to-paste package · 09 Aug 2026

PURPOSE
This phase removes the separate Rearrange workflow and makes Notebook card movement behave like Google Keep while fixing the remaining card-level floating UI issues observed during review.

FINAL USER EXPERIENCE
1. Direct desktop rearrangement
   - No Rearrange / Done button.
   - A normal click still opens the note.
   - Mouse-down + movement beyond 6 px starts card drag immediately.
   - The same applies to the live "Latest Conference Directions" system card.
   - Checkboxes, pin, colour, labels, links, View all and the three-dot menu remain normal controls and do not start a drag.

2. Touch behaviour
   - A stationary 300 ms long-press arms drag.
   - Moving more than 8 px before the long-press cancels drag arming so normal page scrolling remains available.

3. Keyboard accessibility retained
   - The existing drag handle remains in the DOM for keyboard reordering.
   - It is no longer shown on ordinary card hover and cannot cover/intercept the card title.
   - When reached by keyboard focus it becomes visible; Space/Enter picks/drops and arrow keys move the card.

4. Colour palette hardening
   - An open palette raises its parent card above masonry siblings.
   - Card palettes use a compact 4-column layout (max 252 px) instead of a wide strip spilling across neighbouring cards.
   - Opening a palette closes an open three-dot menu, and vice versa.
   - Floating controls are closed before a drag preview is created.

5. Three-dot menu hardening
   - Card menus open upward/inward from the bottom action rail instead of below the card.
   - This prevents a masonry sibling or the lower viewport edge from visually covering the menu.
   - Parent-card stacking is raised while the menu is open.

6. Conference Directions system note
   - Short click continues to open the read-only Conference Directions modal.
   - Click-and-drag now rearranges the card naturally.
   - Its action controls remain protected from drag.
   - Existing colour/label/pin/remove-from-My-Notebook behaviour is preserved.

7. Masonry overlap fix carried forward
   - The latest notebook-masonry-grid.js is included.
   - [data-notebook-system-home-card] remains a first-class measured masonry item, preventing card overlap after adding the live system note to All Notes.

FILES TO REPLACE / ADD
Pages/Notebook/Index.cshtml
Pages/Notebook/_NotebookConferenceDigest.cshtml
wwwroot/css/notebook.css
wwwroot/js/notebook/notebook-app.js
wwwroot/js/notebook/notebook-drag-order.js
wwwroot/js/notebook/notebook-drag-order.test.js
wwwroot/js/notebook/notebook-system-note-personalization-contract.test.js
wwwroot/js/notebook/notebook-masonry-grid.js
wwwroot/js/notebook/notebook-masonry-grid.test.js
wwwroot/js/notebook/notebook-direct-drag-contract.test.js   (new regression test)

NO BACKEND / DATABASE CHANGE IN THIS PHASE
No controller/service/model/EF migration change is required by this direct-drag and floating-UI hardening phase. It is designed to overlay the already implemented Notebook + Conference Digest personalisation state.

IMPORTANT BUILD STEPS
Because Notebook JavaScript source changed, regenerate the committed Notebook bundle after copying the files:

  npm ci
  npm run build:notebook
  npm run check:notebook-assets
  npm test

Then:

  dotnet build
  dotnet test

If your normal Visual Studio/MSBuild flow already regenerates Notebook assets and node_modules/esbuild is installed, Build/Publish should also do this; running npm run build:notebook explicitly is still the safest verification.

BROWSER
After rebuilding, hard refresh the Notebook page (Ctrl+F5) so the previous JS/CSS bundle is not cached.

ACCEPTANCE CHECK
1. In Grid view, click a normal note -> it opens.
2. Mouse-down on passive note content and move >6 px -> card lifts and follows the pointer.
3. Drop between cards -> position changes and remains after refresh.
4. Click checklist checkbox -> only checkbox changes; no drag begins.
5. Click checklist text / passive card body -> note opens on a normal click; drag works when moved.
6. Click Latest Conference Directions -> modal opens.
7. Drag Latest Conference Directions from its passive card surface -> card moves; modal does not open after the drop.
8. Open colour palette -> palette is fully visible above neighbouring cards and remains compact.
9. Open three-dot menu -> "Remove from My Notebook" is clearly visible above the action rail.
10. Open one floating control then another -> previous floating control closes.
11. Pin/unpin the system note and rearrange within the relevant section -> persists after refresh.
12. Switch to List view -> direct rearrangement is disabled.
13. Touch device: long-press a passive card surface, then drag; a normal vertical swipe before the hold threshold scrolls instead.
14. Keyboard: Tab to the drag handle, Space/Enter to pick up, arrows to move, Space/Enter to drop, Escape to cancel.

VALIDATION PERFORMED IN THIS PACKAGE BUILD
- JavaScript syntax checks passed for notebook-app.js, notebook-drag-order.js and notebook-masonry-grid.js.
- 25 dependency-free Notebook contract tests passed (25/25).
- CSS brace-balance and direct-drag structural checks passed.
- System-card masonry contract remains present.
- jsdom-dependent drag/masonry DOM tests were not executable in the packaging environment because jsdom is not installed there.
- .NET SDK is not installed in the packaging environment; run dotnet build/test in the PRISM development machine after overlaying the files.
