PRISM ARPP FY Project Update — Optional Present Stage Column
=============================================================

READY-TO-PASTE REPLACEMENTS
---------------------------
Replace the files in this package preserving their project paths.

BEHAVIOUR
---------
- Adds an "Optional columns" control to the ARPP FY Project Update workspace.
- "Present Stage" is OFF by default, preserving the prescribed 12-column report.
- When enabled, Present Stage appears immediately after AoN under the Status group.
- The choice is carried through FY refresh and Word / PDF / Excel export links.
- Word, PDF and Excel all use the same option and the same row.StageDisplay value.
- Completed projects display "Completed" from lifecycle status; historical stage records do not override it.
- No new database query, field, migration, authorization rule or DI registration is introduced.

EXPORT LAYOUT
-------------
Default: 12 columns; Status = AoN / SO amt & dt / PDC dt.
With Present Stage: 13 columns; Status = AoN / Present Stage / SO amt & dt / PDC dt.

Word maintains the exact 16,000-twip A4-landscape printable width in both variants.
PDF and Excel use variant-specific widths so Project Name / Remarks surrender most of the extra space, while operational columns remain compact.

EXISTING REFINEMENTS PRESERVED
------------------------------
- Definitive heading: PROJECT UPDATE : ARPP LISTED PROJECTS (FY ...).
- Word 7 pt formal-table typography, no-wrap date cells, institutional footer and blank missing values.
- PDF standard-ligature safeguard and blank missing values.
- Excel formal heading, page setup, freeze panes and institutional footer.

AFTER PASTING
-------------
dotnet build .\ProjectManagement.csproj
dotnet build .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj --filter "FullyQualifiedName~ProjectManagement.Tests.Reports"
