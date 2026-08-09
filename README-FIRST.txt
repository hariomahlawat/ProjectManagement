PRISM ERP — Notebook Direct Reminder UI Refinement
Date: 09 Aug 2026

PURPOSE
-------
This is a focused visual refinement of the dedicated "Create reminder" workflow.
It is based on the latest Keep-inspired Notebook + card-click-consistency implementation.

WHAT CHANGES
------------
1. Removes the nested-card/form-panel appearance from the reminder scheduler.
2. Makes the reminder modal narrower and more like an expanded Notebook card.
3. Keeps title/notes compact while preserving all existing reminder functionality.
4. Keeps Later today / Tomorrow 09:00 / Next Monday 09:00 presets unchanged.
5. Keeps precise Date, Time, IST summary and Low/Normal/High priority unchanged.
6. Makes Priority a compact metadata control rather than a second large panel.
7. Combines Colour, Labels, draft state, Create reminder and Close into one bottom bar.
8. Retains a clear primary Create reminder action.
9. Includes responsive full-screen behavior on small screens.

NO BEHAVIOURAL / DATA CHANGES
-----------------------------
- No API changes.
- No service changes.
- No model/entity changes.
- No EF migration.
- No reminder scheduling logic changes.
- No Note/Checklist/card interaction changes.

READY-TO-PASTE FILES
--------------------
wwwroot/css/notebook.css
wwwroot/js/notebook/notebook-reminder-create-refinement-contract.test.js

APPLICATION
-----------
Copy the two paths above over the same paths in the ProjectManagement project.
The CSS file is the COMPLETE latest notebook.css, not a fragment.

VALIDATION PERFORMED
--------------------
Focused Node test run: 22/22 passed, covering:
- Create editor type/payload contracts
- Direct reminder creation contracts
- IST scheduler serialization/presets/summary
- Card opening regression tests
- New reminder visual contract tests

AFTER REPLACEMENT
-----------------
1. Rebuild the solution normally.
2. Optional but recommended:
      npm test
      npm run check:notebook-assets
      dotnet build
      dotnet test
3. Hard refresh Notebook (Ctrl+F5).
4. Open the bell quick-create action and verify:
   - compact Reminder title + notes area
   - borderless When section
   - quick time chips
   - Date + Time controls
   - compact Priority selector
   - Colour / Labels + Create reminder / Close in one bottom bar

The runtime JavaScript bundle does not require regeneration solely for this change,
because runtime JS was not modified. A normal project build remains recommended.
