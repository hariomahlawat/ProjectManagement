PRISM ERP — FFC Sticky Header Gap Fix
======================================

ISSUE
-----
The FFC long-register table header was using:

    --reports-sticky-top: 106px

That offset intentionally included an 8px breathing gap for floating report
controls:

    52px global navigation
  + 46px Projects module navigation
  +  8px breathing room
  = 106px

A sticky table header should not use that floating-card gap. The 8px opening
allowed scrolling table content to remain visible between the Projects module
navigation and the sticky column headings.

FIX
---
The navigation stack and floating-control offset are now separate:

    --reports-nav-stack: 98px;
    --reports-sticky-top: calc(var(--reports-nav-stack) + 8px);

Existing non-FFC floating report controls therefore retain their exact current
106px behaviour.

FFC gets its own table-heading offset:

    --ffc-table-sticky-top: var(--reports-nav-stack);

and the sticky header now uses:

    top: var(--ffc-table-sticky-top);

This makes the FFC table header flush with the bottom of the PRISM navigation
without changing other Reports pages.

A top inset rule was also added to the sticky header so the junction remains
visually sealed while rows scroll underneath.

FILES TO REPLACE
----------------
wwwroot/css/pages/projects-reports.css

ProjectManagement.Tests/Reports/
    FfcProjectsUpdatePresentationContractTests.cs

NO CHANGES TO
-------------
- FFC data/business logic
- Browser report columns or widths
- Word/PDF/Excel builders
- Overall-status behaviour
- Country-Year selection
- Database / DI / Program.cs

AFTER PASTING
-------------
dotnet build .\ProjectManagement.csproj
dotnet build .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj `
    --filter "FullyQualifiedName~FfcProjectsUpdatePresentationContractTests"

MANUAL CHECK
------------
Open FFC Projects Update on the same wide monitor and scroll down.

Expected:
- Projects module navigation remains at the top.
- FFC column headings sit directly beneath it.
- No 8px strip of scrolling row content is visible between the two.
- Header bottom shadow remains subtle.
