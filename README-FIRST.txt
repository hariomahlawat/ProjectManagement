PRISM ERP — Notebook Card Click Consistency Fix
Date: 09 Aug 2026

Purpose
-------
This focused refinement makes Note and Checklist cards follow one interaction model:

  • Click passive card content -> open the note/checklist editor.
  • Click a checklist checkbox -> toggle only that checklist item.
  • Click checklist item text -> open the checklist editor.
  • Click labels/actions/buttons -> keep their own action; do not open the card.
  • Rearrange mode -> passive card opening is suppressed so drag/reorder remains predictable.

Implementation details
----------------------
1. Checklist preview markup now separates the checkbox control from the item text.
   The previous implementation placed both icon and text inside the toggle button, causing a
   click anywhere on the row text to complete/uncomplete the item.

2. notebook-app.js now uses a single passive card-opening contract for ordinary card content.
   Interactive descendants are explicitly excluded.

3. notebook-utils.js contains getPassiveNotebookCardOpenTarget(), centralising the interaction
   rule rather than duplicating selector logic in the application bootstrap.

4. CSS now lays checklist rows out as a dedicated checkbox column + content column, preserving
   compact PRISM styling and multiline wrapping.

5. Accessibility is retained/improved: checkbox controls have item-specific aria-labels, while
   keyboard users can still open the record via the card title link.

6. Regression tests cover passive content, interactive descendants, rearrange mode, checklist
   rendering, and drag-target semantics.

Files to replace/add
--------------------
See CHANGED-FILES.txt. Copy the package contents over the project root, preserving folders.

Build
-----
No database migration or C# production/service change is required.

After replacing the files, rebuild the Notebook assets and solution:

  npm run build:notebook
  npm run check:notebook-assets
  dotnet build
  dotnet test

If node_modules are not present, run npm ci first.

Then hard-refresh Notebook (Ctrl+F5).

Acceptance checks
-----------------
1. Click a normal Note title/body -> editor opens.
2. Click a Checklist title -> editor opens.
3. Click checklist ITEM TEXT -> editor opens; item state does not change.
4. Click checklist CHECKBOX -> item toggles; editor does not open.
5. Click a label chip -> label view opens; editor does not open.
6. Click Pin/Colour/Labels/Share/More -> only that command executes.
7. Click passive blank content inside a checklist card -> editor opens.
8. Enter Rearrange mode -> clicking/dragging passive content does not open the editor.

Validation performed here
-------------------------
• Modified ES-module sources passed Node syntax checks using module input mode.
• New dependency-free interaction tests: 4/4 passed.
• Existing dependency-free Notebook refinement/contract tests included in the check: 11/11 passed total.
• Full .NET compilation cannot be executed in this runtime because the .NET SDK is unavailable.
