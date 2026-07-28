PROJECT REPOSITORY — OPERATIONAL ORDER AND SERVER-SIDE SORTING
==============================================================

APPLICATION
-----------
1. Extract this ZIP into the ProjectManagement project root.
2. Preserve the repository-relative folders and replace the listed files.
3. Clean and rebuild the solution.
4. Run the test project.
5. Open /Projects and verify both card and table views.

DEFAULT OPERATIONAL ORDER
-------------------------
The order is applied to the complete filtered result before Skip/Take pagination:

1. Active projects
   - Latest recorded project remark/edit first.
   - Project creation date is the fallback where no remark exists.
   - Content/stage dates and stable name/ID tie-breakers are retained.

2. Completed, non-legacy projects
   - Latest completion year/month/exact date first.

3. Completed legacy projects
   - Kept after normal completed projects, latest completion first.

4. Cancelled projects
   - Latest cancellation date first.

All groups use project name and project ID as deterministic final tie-breakers.
Search results retain relevance as the primary order and use the operational
sequence as the deterministic tie-breaker.

SERVER-SIDE TABLE SORTING
-------------------------
Project, Status, Project Officer, Category and Case file headings now create
server-side sort URLs. Sorting therefore applies to all matching projects,
not only the 25 rows currently rendered. Card view, table view and pagination
share the same authoritative sequence. A visible Reset order action restores
the default operational order.

SCHEMA
------
No database migration is required.

VALIDATION
----------
- JavaScript syntax was validated with node --check.
- The package includes ordering and presentation regression tests.
- ZIP integrity was validated.
- A full .NET build could not be run in this environment because the .NET SDK
  is not installed. Run Clean/Rebuild and the tests in Visual Studio.
