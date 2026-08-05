PRISM ERP — SDD Institutional Profile: Original Visual Restoration + Settings Fix
=================================================================================

Purpose
-------
This package corrects the visual and functional regressions introduced by the
native-table-only institutional profile implementation.

Implemented changes
-------------------
1. Restores the authorised/original SDD profile composition:
   - maroon institutional title band;
   - open alternating history timeline;
   - five ordered institutional output cards;
   - compact unit-citations strip;
   - PRISM source/date line.

2. Preserves maintainability without exposing spreadsheet-style grids:
   - milestone text is maintained in one borderless native PowerPoint table;
   - each output card uses one borderless native table for heading, KPI and rows;
   - card styling remains rounded, colour-coded and presentation-oriented;
   - no alternating grey table rows or heavy black cell borders.

3. Displays every selected institutional module in the configured order.
   The Military–Academia–Industry Synergy module is no longer silently omitted.

4. Retains the corrected default project scope:
   - Original completed projects (rebuild projects excluded) remains the default;
   - headline and technical-category breakdown use the same scope.

5. Fixes Deck Settings not opening:
   - removes the premature syncInstitutionalLayoutSummary() call from
     syncUpdateRowOrder();
   - institutional initialization now occurs only after institutional functions
     have been declared;
   - removes a duplicated drag-state assignment.

6. Updates regression tests for the restored visual architecture and adds a
   JavaScript test guarding against the initialization-order failure.

Replacement
-----------
Copy the five files from this package into the matching paths in the
ProjectManagement solution and overwrite the existing files.

No database migration is required.

Validation completed
--------------------
- JavaScript syntax validation: passed
- Briefing-deck JavaScript tests: 27/27 passed
- Patch dry-run/application comparison: passed
- Package integrity/checksums: passed

After replacement
-----------------
1. Clean the solution in Visual Studio.
2. Rebuild the solution.
3. Run the ProjectBriefings tests.
4. Hard-refresh the browser (Ctrl+F5) to invalidate the old JavaScript.
5. Open Deck Settings and generate both Editorial Light and Graphite Dark decks.
