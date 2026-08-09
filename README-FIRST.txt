PRISM ERP — Project Officer Conference Review header refinement
Date: 09 Aug 2026

Purpose
-------
This focused patch applies two final refinements to the Project Officer read-only
Conference Review:

1. Removes the explanatory information strip below the header.
2. Corrects ERP activity strip overflow in the read-only header.

Implementation notes
--------------------
- The PO header now uses an explicit two-region CSS Grid instead of inheriting
  the Command Conference three-column header geometry.
- The Read only badge and ERP activity strip share the right-hand region.
- The 30-day activity cells are distributed responsively inside the available
  activity-strip width; all 30 days remain visible.
- At medium widths the tools move to a second row. At narrow widths the badge
  and activity strip stack cleanly.
- No server logic, authorization, conference semantics, database schema, or EF
  migration is changed.

Files to replace
----------------
Pages/Workspace/_ProjectOfficerConference.cshtml
wwwroot/css/officer-conference.css
wwwroot/js/pages/officer-conference.test.js

After replacement
-----------------
Run:
  dotnet build
  dotnet test

If your normal frontend build/test pipeline includes Node tests, run that as
usual. Then hard-refresh the PO Conference Review (Ctrl+F5).
