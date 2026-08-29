PRISM ERP — Global Search V2 Relevance, Consistency & Result Quality Hardening
READY-TO-PASTE OVERLAY
Date: 29 Aug 2026

TARGET
------
Apply this overlay to the exact ProjectManagement source baseline supplied for this phase.
Project root = folder containing ProjectManagement.csproj / Program.cs.

APPLY
-----
1. Back up the source tree and database.
2. Stop the development IIS/IIS Express instance if it has locked build output.
3. Copy the CONTENTS of this folder into the ProjectManagement project root.
4. Preserve the directory structure and overwrite the listed files.
5. appsettings.json is included because Search:V2:SuggestionLimit changes from 8 to 6 and ProjectionVersion is made explicit as 4. If your local appsettings.json has changed since the supplied baseline, merge ONLY these Search:V2 values rather than overwriting unrelated configuration:

       "SuggestionLimit": 6,
       "ProjectionVersion": 4

   Keep your existing ServeV2 / ShadowMode / connection-string / environment-specific values.

BUILD / TEST
------------
From the project root:

    npm ci
    dotnet restore ProjectManagement.sln
    dotnet build ProjectManagement.sln --no-restore
    dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj --no-build

The repository also contains VERIFY-SEARCH-V2.ps1; run it on the Windows development machine if that is your normal Search V2 verification entry point.

OPTIONAL REAL POSTGRESQL TESTS
------------------------------
Use a disposable/test database with pg_trgm installed:

    $env:PRISM_SEARCHV2_TEST_CONNECTION = "Host=...;Database=...;Username=...;Password=..."
    dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj --filter SearchV2PostgresIntegrationTests

FIRST START AFTER APPLYING
--------------------------
SearchV2Options.ProjectionVersion is now 4. The existing SearchIndexWorker will detect the version mismatch and build a replacement projection generation atomically. No new EF migration is required by this phase.

Do not judge the new ranking until Admin > Diagnostics > Search index reports active/required projection version 4 / 4.

ACCEPTANCE QUERIES
------------------
1. high tech
   - exact/normalized title phrase matches must rank ahead of loose OCR/body matches.
2. high-tech and HI–TECH
   - should converge with high tech.
3. hyderabad
   - autocomplete: max 6 entity suggestions + See all results.
4. Query ending in tech where a suggested title contains Technology
   - the same record must remain discoverable after committing the query.
5. Partial project/reference identifier shown by autocomplete
   - must remain discoverable after commit.
6. Partial configured alias shown by autocomplete
   - must remain discoverable after commit.
7. Deliberately misspelled title shown through fuzzy autocomplete
   - must remain discoverable in full search; spelling correction may still be offered.
8. Known noisy scanned PDF
   - remains searchable, but poor OCR should be demoted and garbage snippets suppressed.
9. Scroll a long result page
   - search field + tabs must stay below the PRISM top navigation, not disappear underneath it.
10. Admin > Diagnostics > Search index > Ranking inspector
   - inspect high tech and verify top rows expose rank, matched field, tier, retrieval channels and RRF score.

IMPORTANT BEHAVIOUR
-------------------
- Internal category key Trackers remains unchanged for URLs/query compatibility; the user-facing Search UI displays Records.
- Authorization is still applied before ranking/suggestions/facets/snippets/counts.
- Autocomplete and committed search now cover the same candidate families: identifier exact/prefix, alias exact/prefix, title exact/prefix/token-prefix, and title fuzzy.
- Strong title/identifier/alias intent cannot be overtaken by loose narrative/OCR matches merely through term frequency because relevance tier is ordered before RRF score.

VERIFICATION STATUS OF THIS PACKAGE
-----------------------------------
The packaging container does not provide the .NET SDK/MSBuild/PowerShell and cannot download them. Therefore C# compilation/xUnit could not be executed here.
Final-tree static/source verification DID pass; see SEARCH-V2-QUALITY-HARDENING-STATIC-VERIFICATION.txt.
The dotnet build/test commands above remain mandatory before deployment.
