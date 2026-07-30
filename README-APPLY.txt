PRISM PROCUREMENT JOURNEY REDESIGN
==================================

Purpose
-------
Replaces the existing six-phase Procurement Process page with one continuous,
graph-driven Procurement Journey designed for offline PRISM deployment.

Application
-----------
1. Copy the package contents into the ProjectManagement project root.
2. Preserve the included folder structure and replace existing files when prompted.
3. Apply the database migration:

   dotnet ef database update

4. Build and run the automated tests:

   dotnet build
   dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj

5. Start PRISM and open /Process.

Important behaviour
-------------------
- No Internet or CDN access is used.
- The page uses native HTML, CSS, JavaScript and SVG.
- SortableJS is loaded from the existing local wwwroot/lib installation.
- The six invented process phases have been removed.
- Technical Evaluation and Benchmarking start after Bid Process and proceed in parallel.
- Commercial Opening requires both Technical Evaluation and Benchmarking.
- The stage panel contains Purpose and Processing Checklist only.
- Admin and HoD may edit Purpose.
- Existing checklist permissions remain MCO and HoD.
- Checklist concurrency, auditing, create/edit/delete and drag-reorder are retained.
- Journey and Complete Map modes are generated from the dependency graph.
- A reduced-motion mode and print fallback are included.

Database migration
------------------
20261207170000_RedesignProcurementJourney

The migration:
- adds editable purpose fields and audit metadata to StageChecklistTemplates;
- backfills default purpose text for existing stage guidance;
- corrects SDD-2.0 TEC/BM/COB dependencies even when startup seeders are disabled.

No external packages are required.
