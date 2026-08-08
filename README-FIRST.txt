PRISM ERP — Notebook Keep-Inspired UI Refinement
Date: 08 Aug 2026

PURPOSE
-------
This package refines the already-stabilised PRISM Notebook UI using the strongest
interaction principles visible in Google Keep while preserving PRISM's enterprise
navigation, reminder semantics, collaboration controls, audit/concurrency model,
and existing backend architecture.

WHAT CHANGES
------------
1. Quick capture now behaves as a compact expanding note surface.
   - Short notes remain compact and auto-grow with content.
   - Manual textarea resizing is removed.
   - Colour and label controls are available directly in quick capture.
   - Selected colour/labels are persisted in the quick-capture draft and sent in
     the normal create payload.
   - Label catalogue counts refresh after a labelled quick-capture note is saved.

2. Existing-note editor is visually consolidated into an enlarged card.
   - Compact title/body spacing.
   - Existing autosizing now uses 64–260px for notes and 46–150px for checklist
     descriptions.
   - Reminder metadata is visually lighter.
   - Colour/label controls, save state and Close share one bottom action bar.
   - Create-mode behaviour is intentionally preserved.

3. Card chrome is reduced.
   - Reminder is no longer duplicated as a top icon when the reminder pill already
     communicates the state.
   - Pin/completed state remains visible.
   - Labels move to a direct one-click hover action alongside colour/share.
   - Card actions remain hover/focus driven on pointer devices and visible on touch.
   - Borders, shadows, labels and metadata are lighter and more content-first.

4. Grid mode now uses deterministic masonry for every board size.
   - The existing DOM-order-safe grid-span masonry engine is retained.
   - Manual SortOrder and drag/keyboard rearrangement remain authoritative.
   - List view remains conventional and clears masonry spans.

5. Rearrange remains available but has reduced visual emphasis.

FILES
-----
See CHANGED-FILES.txt. Copy the package contents over the ProjectManagement
project root, preserving folders.

NO DATABASE / BACKEND MIGRATION
-------------------------------
There are no entity, EF Core, database, API contract or service-layer changes in
this package.

FRONTEND BUILD
--------------
The generated wwwroot/dist/notebook-index.bundle.js is intentionally not shipped.
Your project already generates Notebook assets through the existing build pipeline.
After copying the files, run a normal Visual Studio Rebuild Solution, or:

  npm ci
  npm run build:notebook
  npm run check:notebook-assets
  dotnet build
  dotnet test

Then hard-refresh the Notebook page (Ctrl+F5).

VALIDATION PERFORMED HERE
-------------------------
- All modified Notebook source JavaScript files passed Node syntax validation.
- 10 dependency-free Notebook regression/contract tests passed.
- Full DOM-backed JS tests could not run in this environment because jsdom is not
  installed here. Those tests are included/updated in the package.
- .NET compile/tests could not be executed here because the .NET SDK is unavailable.

RECOMMENDED MANUAL CHECKS
-------------------------
1. Expand quick Note: short body should remain compact; long body should auto-grow.
2. Choose a colour and one or more labels in quick capture, save, and verify card
   colour/labels plus rail label counts.
3. Create/edit a long checklist and confirm multiline rows remain readable.
4. Hover a card: Pin / Colour / Labels / Collaborators / More should appear without
   persistent clutter.
5. Open a note: verify the compact expanded-card editor and unified bottom toolbar.
6. Check 2, 4, 5+ mixed-height cards in Grid view for masonry packing.
7. Enter Rearrange mode and confirm drag + keyboard reorder still persist correctly.
8. Switch to List view and back to Grid view.
