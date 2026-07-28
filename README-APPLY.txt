COMPLETED PROJECTS WORKSPACE - COMPILE FIX
==========================================

Apply from the ProjectManagement project root and overwrite the four files below.
No database migration or project-file change is required.

FILES
-----
1. Services/Projects/CompletedProjectPortfolioPolicy.cs
2. Pages/Projects/CompletedSummary/Index.cshtml.cs
3. Utilities/Reporting/CompletedProjectsSummaryExcelBuilder.cs
4. Pages/Projects/Overview.cshtml.cs

FIXES
-----
- Fixes CS0019 in CompletedProjectPortfolioPolicy by returning List<string> and
  string[] through explicit IReadOnlyList<string> return paths.
- Fixes CS8123 and CS1061 tuple errors by declaring the option array as
  (string Value, string Label)[].
- Fixes ClosedXML border API errors by using the individual side-border members
  supported by the project's ClosedXML package.
- Removes the nullable-value warning in Overview.cshtml.cs without changing the
  ARPP/IPA display logic.

AFTER COPYING
-------------
1. Clean the solution.
2. Delete ProjectManagement/bin and ProjectManagement/obj if Visual Studio still
   shows stale diagnostics.
3. Rebuild the solution.
4. Open Completed Projects and test Excel export.
