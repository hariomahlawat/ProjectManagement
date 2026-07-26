PRISM ERP — PROJECT HEADER AND SPACING CORRECTION
=================================================

Source baseline
---------------
ProjectManagement-master (12)(4).zip supplied on 26 Jul 2026.

Purpose
-------
This package corrects the remaining Project Overview header spacing and
surface-rendering defects without changing project data, lifecycle rules,
database schema, application configuration, or repository-card behaviour.

The implementation fixes the verified causes:

1. The generic .project-portfolio .pm-card rule occurred after the command
   header rules and had equal specificity. It could therefore overwrite the
   lifecycle-specific border colours. The shared rule now precedes the
   command-header contract, and all header variants use page-scoped selectors.

2. The header section and its children shared spacing responsibility. The
   outer section now owns only the surface, border and external spacing.
   The identity/commands row and intelligence strip own their own internal
   padding. This produces one continuous header card with a full-width divider.

3. The breadcrumb had two independent bottom-margin owners (Bootstrap mb-3
   and the descendant CSS rule). It now has one explicit page-level spacing
   owner, while the Bootstrap breadcrumb margin is reset with mb-0.

4. Lifecycle modifier class values previously included their own leading
   spaces and were concatenated directly into the class attribute. Class
   composition is now explicit and test-covered.

Files to replace
----------------
Pages/Projects/Overview.cshtml
Pages/Projects/_ProjectCommandHeader.cshtml
wwwroot/css/pages/project-portfolio.css
ProjectManagement.Tests/ProjectCommandHeaderAssetTests.cs
ProjectManagement.Tests/ProjectOverviewPresentationContractTests.cs
REPLACEMENT-MANIFEST.txt

Apply
-----
1. Back up the current application source.
2. Stop the application.
3. Copy the contents of this folder into the project root, preserving the
   relative paths shown above. Allow those six files to be replaced.
4. Verify file integrity from this folder:

       sha256sum -c SHA256SUMS.txt

5. From the project root, run the normal validation pipeline:

       dotnet restore ProjectManagement.sln
       dotnet build ProjectManagement.sln --no-restore
       dotnet test ProjectManagement.sln --no-build
       npm ci
       npm test

6. Publish into a clean output directory. Deploy the complete new publish
   output atomically; do not mix individual source files into an older
   published application directory.
7. Restart the application and hard-refresh the browser.

Deployment verification
-----------------------
The rendered header section must contain:

    card pm-card project-command-header

Completed and cancelled projects must additionally contain the matching
project-command-header--completed or project-command-header--cancelled class.

The deployed stylesheet must expose these declarations:

    .project-portfolio .project-command-header
        margin-bottom: .75rem
        padding: 0

    .project-command-header__main
        padding: .9rem 1rem .8rem

    .project-intelligence-strip
        margin-top: 0
        padding: .85rem 1rem 1rem
        border-top: 1px solid var(--portfolio-border)

With ASP.NET Core asp-append-version enabled, the stylesheet URL generated
from this exact file should end with:

    ?v=U79mjtOaZJao1M88_wBZ-s1Du6korhu3VJlo-_xjQb0

If a different version token is served after replacement, the running
application is not serving this package's project-portfolio.css.

Migration impact
----------------
No database migration or data backfill is required.
