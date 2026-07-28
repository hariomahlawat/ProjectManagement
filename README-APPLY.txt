PRISM ERP — Completed Projects workflow finalisation
====================================================

PURPOSE
-------
This replacement package completes the current Completed Projects redesign:

1. “Available for proliferation” remains the authoritative primary proliferation KPI.
2. The derived “Available but blocked” and “Fully ready” classifications are removed from the user-facing workspace, policy filters and overview.
3. The project drawer remains a concise, read-only inspection surface.
4. Substantive editing is performed on a dedicated, responsive edit page.
5. Saving or cancelling returns the user to the same filtered/sorted workspace, restores the prior tab and scroll position, and reopens the same project drawer when session storage is available.

IMPLEMENTED WORKFLOW
--------------------
Register / Overview
    -> Completed project details drawer
    -> Edit details
    -> Dedicated edit page
    -> Return to the original workspace context

KEY CHANGES
-----------
- Five evenly distributed headline KPIs:
  * Available for proliferation
  * Proliferation assessment pending
  * Technology review required
  * ToT action pending
  * Records with critical gaps
- Removed “Available but blocked” from KPI cards, portfolio-focus filters, service policy and overview presentation.
- Removed “Fully ready” wording from the drawer and overview.
- Overview now presents availability posture and independent proliferation, technology and ToT action queues.
- Drawer renamed to “Completed project details”.
- Drawer project identity and actions remain visible while its content scrolls.
- Drawer shows an “Actions required” section only where an actual action exists.
- Data-quality gaps identify the exact missing critical and supplementary fields.
- Dedicated edit page uses a responsive two-column workspace with:
  * Technology assessment
  * Proliferation decision
  * Production information
  * Existing LPP records
  * Collapsible new-LPP entry
- “Reason for non-availability” is shown and required only when “Not available” is selected; contradictory stored text is cleared safely on save.
- Server-side length, amount, date, document-ownership and project-ownership validation is retained/enhanced.
- Sticky Save/Cancel controls, duplicate-submit prevention and unsaved-change protection are included.
- Development cost and ToT are shown as read-only authoritative information managed elsewhere.
- Export metadata now uses the same “Portfolio focus” terminology.

APPLICATION
-----------
1. Stop the application/IIS site.
2. Back up the current source tree.
3. Extract this ZIP into the ProjectManagement repository root.
4. Allow the repository-relative files to overwrite the existing files.
5. Delete the solution's bin and obj folders if Visual Studio shows stale diagnostics.
6. Restore NuGet packages.
7. Rebuild the solution.
8. Run the ProjectManagement.Tests project.
9. Start the application and hard-refresh the browser to reload versioned CSS/JavaScript.

DATABASE
--------
No database migration is required.

VALIDATION PERFORMED IN THE GENERATION ENVIRONMENT
--------------------------------------------------
- JavaScript syntax checks passed for both Completed Projects scripts.
- Both CSS files parsed without syntax errors.
- Static presentation and workflow contracts passed.
- C# delimiter and replacement-contract checks passed.
- ZIP integrity was verified.

A full .NET build could not be executed in the generation environment because the .NET SDK is not installed. Run the normal clean/build/test cycle in Visual Studio after replacement.
