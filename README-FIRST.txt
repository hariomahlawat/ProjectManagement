PRISM Notebook — System Card Masonry Fix
Date: 09 Aug 2026

PURPOSE
Fixes the overlap seen after "Latest Conference Directions" is added to All Notes.
The PRISM system note was a real visual card but the masonry measurement engine only
measured normal [data-note-id] cards. With an 8px masonry row track, the system card
therefore received no grid-row span and the next note could render on top of it.

FILES TO REPLACE
1. wwwroot/js/notebook/notebook-masonry-grid.js
2. wwwroot/js/notebook/notebook-masonry-grid.test.js

WHAT CHANGED
- The masonry item selector now treats [data-notebook-system-home-card] as a first-class
  masonry item, alongside normal Notebook notes and drag placeholders.
- ResizeObserver observation automatically includes the PRISM system card.
- Existing mutation, explicit refresh, board-view, image-load and window-resize refresh
  paths now remeasure the system card without special-case CSS.
- Added regression tests for system-card masonry span calculation and span clearing in
  list mode.

NO CHANGES REQUIRED
- No CSS workaround/min-height/z-index hack.
- No Razor change.
- No backend/service/controller change.
- No database or EF migration.
- No change to Conference direction query logic.
- No change to drag/reorder persistence logic.

WHY THIS IS THE CORRECT FIX
The system note already participates in visual drag order using
[data-notebook-system-home-card]. The masonry engine now uses the same visual-card
contract. Its grid span is derived from actual rendered height, so it remains correct
when labels, responsive width, content wrapping, pinning, or future card content change.

AFTER REPLACEMENT
npm ci
npm run build:notebook
npm run check:notebook-assets
npm test

dotnet build
dotnet test

Then hard refresh (Ctrl+F5) and verify:
1. Add Latest Conference Directions to My Notebook.
2. No normal note overlaps it.
3. Enter Rearrange and move it between ordinary notes.
4. Refresh — position remains stable.
5. Add/remove a label — masonry reflows without overlap.
6. Pin/unpin — both boards reflow correctly.
7. Switch Grid/List/Grid — no stale row span remains.
