PRISM ERP — Completed Projects Workspace
Proliferation KPI and final interaction refinement
===================================================

BASELINE
--------
Apply this package after the Completed Projects wide-screen refinement package.
It contains only the files changed in this finishing phase.

APPLICATION
-----------
1. Stop the application/IIS site.
2. Back up or commit the current repository state.
3. Extract this ZIP into the ProjectManagement repository root.
4. Allow the repository-relative files to overwrite the existing files.
5. Delete bin and obj only if Visual Studio retains stale diagnostics.
6. Rebuild the solution and run the test project.
7. Start the application and hard-refresh the Completed Projects page (Ctrl+F5).

NO DATABASE MIGRATION IS REQUIRED.

IMPLEMENTED
-----------
• Keeps “Available for proliferation” as the primary headline proliferation KPI.
• Removes “Fully ready” from the headline KPI row.
• Adds “Proliferation assessment pending” as the second headline KPI, using the existing unrecorded-proliferation-decision count.
• Does not add the rejected “ready now / action required” supporting text.
• Retains Fully ready only as supporting analysis in the Overview and as an optional Portfolio position filter.
• Adds a dedicated server-side proliferation-assessment-pending portfolio status so the KPI opens the correct filtered Register and export scope.
• Prevents KPI and Overview links from retaining contradictory direct filters such as Availability, Technology or ToT.
• De-duplicates the concise availability-blocker queue from projects already shown in the Technology and ToT action queues; the total Available-but-blocked count remains unchanged.
• Hides the redundant unfiltered “Showing all …” message; scope text appears only when filters are active.
• Renames “Portfolio register” to “Completed projects register”.
• Measures the actual PRISM top-bar and module-subnav heights for the sticky Register header instead of relying on a hard-coded offset.
• Preserves sticky Register headings on normal laptop widths by hiding supplementary Production and Latest LPP columns before falling back to horizontal scrolling on small screens.
• Adds explicit accessible tooltip text describing the exact missing fields behind every data-quality badge.
• Adds policy and presentation regression tests for the revised KPI hierarchy, status filter, queue allocation and sticky-header behaviour.

EXPECTED HEADLINE KPI ORDER
---------------------------
1. Available for proliferation
2. Proliferation assessment pending
3. Available but blocked
4. Technology review required
5. ToT action pending
6. Records with critical gaps

VALIDATION CHECKLIST
--------------------
1. The second KPI reads “Proliferation assessment pending”; no Fully ready KPI is present.
2. No “23 ready now · 14 require technology/ToT action” supporting line is displayed.
3. Clicking Available for proliferation opens all projects marked available.
4. Clicking Proliferation assessment pending opens only projects where the proliferation decision is unrecorded.
5. Direct filters that conflict with a clicked KPI are cleared; unrelated scope filters remain.
6. Fully ready remains visible only inside Overview supporting analysis and the Portfolio position selector.
7. The Overview availability-blocker queue no longer repeats projects already shown in Technology or ToT queues.
8. On an unfiltered page, the duplicate “Showing all 159 projects” message is absent.
9. The Register heading reads “Completed projects register”.
10. The table header remains aligned immediately below the actual PRISM navigation shell after browser zoom or navigation-height changes.
11. At normal laptop widths, Production and Latest LPP hide before horizontal scrolling is introduced.
12. Hovering a data-quality badge shows the exact critical and supplementary fields missing.

STATIC VALIDATION PERFORMED
---------------------------
• JavaScript syntax check completed with Node.
• CSS parsed without syntax errors.
• KPI presentation contract checked.
• Previous compile-fix patterns remain intact.
• ZIP integrity and repository-relative paths are validated during packaging.

A complete .NET build could not be executed in this environment because the .NET SDK is not installed. Rebuild in Visual Studio/.NET 8 after replacing the files.
