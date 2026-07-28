PRISM COMPLETED PROJECTS — PROLIFERATION COST TERMINOLOGY FINALISATION
=====================================================================

APPLICATION
-----------
1. Close the running application.
2. Extract this package into the ProjectManagement project root.
3. Preserve the folder structure and replace the listed files.
4. Clean the solution. If Visual Studio retains stale diagnostics, delete bin and obj.
5. Rebuild the solution and run the tests.

WHAT THIS PATCH DOES
--------------------
- Uses “Proliferation cost” consistently in the Completed Projects register,
  drawer, Data Quality view, Edit page, legacy Project Meta edit page and Excel export.
- Renames the completed-project edit form boundary to semantic properties:
    ProliferationCostLakhs
    ProliferationCostRemarks
- Adds ProliferationCostLakhs as the presentation/service alias on
  CompletedProjectSummaryDto while retaining ApproxProductionCost internally.
- Updates supplementary-gap wording to “Proliferation cost”.
- Updates sorting and Excel output to use the semantic alias.
- Adds presentation-contract and policy regression coverage.

DATA AND DATABASE
-----------------
No database migration is required.

ProjectProductionCostFact and its ApproxProductionCost property are retained as
legacy persistence names for database compatibility. The business and user-facing
term is now Proliferation cost.

VALIDATION CHECKLIST
--------------------
- Completed Projects register heading shows “Proliferation cost”.
- Project drawer shows “Proliferation cost”.
- Data Quality lists “Proliferation cost” as supplementary information.
- Edit page shows “Proliferation cost (lakh)” and “Proliferation cost remarks”.
- Legacy Project Meta edit page shows “Proliferation cost (lakh)”.
- Excel export heading is “Proliferation cost (lakh)”.
- Existing stored cost values load and save unchanged.
- Sorting by the proliferation-cost column works.

BUILD NOTE
----------
Static source and package checks were completed. A full .NET build could not be
run in the packaging environment because the .NET SDK is not installed.
