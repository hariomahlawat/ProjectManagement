PRISM ERP — NOTEBOOK UI REFINEMENT
Ready-to-paste files
Date: 08 Aug 2026

PURPOSE
This is the final UI refinement pass following the Notebook stabilisation work.
It addresses the issues visible in runtime testing without changing the Notebook domain model or backend architecture.

IMPLEMENTED
1. Checklist items are now multiline auto-growing textareas instead of single-line inputs.
   - Long text wraps while editing.
   - Rows grow automatically up to a controlled maximum height.
   - Excessively long rows scroll vertically rather than horizontally.
   - Enter creates the next checklist item.
   - Shift+Enter remains available for an intentional line break.
   - IME composition Enter is not intercepted.

2. Quick composer controls are fully styled.
   - Checklist, Reminder and Pin controls use one consistent icon-button treatment.
   - Pin exposes aria-pressed and updates its accessible label/title.
   - Close is a clean PRISM text action rather than a native browser button.
   - Footer alignment is corrected so status remains left and Close remains right.

3. Existing-item Note editor is content-responsive.
   - Short notes use a compact 84 px baseline.
   - Checklist description uses a compact 52 px baseline.
   - Note body grows with content to 280 px, then scrolls.
   - Checklist description grows to 180 px, then scrolls.
   - Autosizing is refreshed after load, draft restore and runtime input.
   - Create-mode editor retains its existing manual-resize behaviour.

4. Regression tests added/extended for textarea sizing, checklist wrapping/keyboard behaviour,
   and quick-composer control contracts.

FILES TO REPLACE / ADD
Pages/Notebook/Index.cshtml
wwwroot/css/notebook.css
wwwroot/js/notebook/notebook-composer.js
wwwroot/js/notebook/notebook-checklist-editor.js
wwwroot/js/notebook/notebook-editor.js
wwwroot/js/notebook/notebook-textarea-autosize.js                 [NEW]
wwwroot/js/notebook/notebook-checklist-editor.test.js
wwwroot/js/notebook/notebook-editor.test.js
wwwroot/js/notebook/notebook-textarea-autosize.test.js            [NEW]
wwwroot/js/notebook/notebook-composer-contract.test.js             [NEW]

AFTER COPYING
No EF migration is required.
No C# production file is changed.

Your ProjectManagement.csproj already runs `npm run build:notebook` before Build/Publish when Notebook JS inputs change.
Therefore a normal Visual Studio rebuild will regenerate wwwroot/dist/notebook-index.bundle.js provided node_modules/esbuild exists.

Recommended verification:
  npm test
  npm run build:notebook
  dotnet build

If dependencies are missing:
  npm ci

Then hard-refresh the Notebook page (Ctrl+F5) and verify:
- long checklist item wraps while editing;
- Enter creates next row and Shift+Enter does not;
- quick checklist/pin/close controls have no native black border;
- short Note modal is visibly more compact;
- long Note body grows and then scrolls;
- Reminder, labels, colour, sharing and autosave still work normally.

Patch file:
PRISM-Notebook-UI-Refinement.patch
