PRISM PROJECT BRIEFING DECK — UPDATE SHEET REFINEMENT
=====================================================

Apply this package on top of the already implemented Project Update Sheets feature.
Copy the folders into the ProjectManagement project root and replace the listed files.

Implemented changes
-------------------
1. Portfolio summary now shows two separate financial cards for Project Update Sheets:
   - Total R&D Cost (existing L1 → AoN → IPA resolution)
   - Total IPA Cost (authoritative IPA position only)

2. Authoritative IPA positions are resolved once in the briefing cost pipeline and reused
   for both the R&D fallback and the independent IPA summary. This avoids duplicate database
   queries and preserves the existing ARPP/legacy IPA authority rules.

3. Project-sheet header has been redesigned:
   - Removed the solid red title panel.
   - Preserved only a restrained maroon top accent.
   - Uses original project-name casing on a clean white header.
   - Removed the two corner insignia from project-sheet headers.

4. Branding behaviour remains meaningful:
   - Cover and summary slides retain normal header branding.
   - With "All slides", project sheets receive one compact footer insignia instead of two
     corner insignia.
   - With "Cover and summary", project sheets remain clean and text-branded through the footer.

5. The SO Date / Firm field no longer displays "Not recorded" twice when both values are absent.

Database migration
------------------
None required.

Recommended verification
------------------------
dotnet build
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj

The .NET SDK was not available in the patch-generation environment, so the full build and test
suite could not be executed here. Structural, manifest, patch and static source checks passed.
