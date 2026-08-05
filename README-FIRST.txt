PRISM BRIEFING DECK — SDD INSTITUTIONAL PROFILE PLAN FIX
========================================================

Replace the files in this package using the exact project-relative paths.
This package is cumulative with the most recent "Remaining Readability Fixes" package.
No database migration is required.

ROOT CAUSE CORRECTED
--------------------
The Project Update Sheet composition path rebuilt its own cover/summary sequence and did
not insert InstitutionalProfile. The saved setting, profile data and slide estimate were
correct, but BuildProjectUpdateSheetPlans silently omitted the slide.

PRODUCTION CHANGES
------------------
1. Introduces one shared AddIntroductoryPlans(...) method for both deck templates.
2. Guarantees the same introductory sequence for Standard Briefing and Project Update Sheets:
      Cover (when enabled)
      SDD Institutional Profile (when profile data exists)
      Portfolio Summary (when enabled)
3. Retains all recent Project Update Sheet improvements:
      readable Project Brief typography
      continuation slides
      crop-to-fill photographs
      updated fact-table typography
4. Retains the existing ceremonial closing slide as the final slide.
5. Does not create a profile slide when InstitutionalProfile is null.

REGRESSION COVERAGE
-------------------
- Project Update Sheets include the profile immediately after the cover.
- Portfolio Summary follows the profile.
- Project sheets follow the summary.
- Closing slide remains last.
- A null InstitutionalProfile intentionally omits the slide.

FILES
-----
Services/ProjectBriefings/Presentation/ProjectBriefingSlideComposer.cs
Services/ProjectBriefings/Presentation/ProjectBriefingSlideComposer.UpdateSheet.cs
ProjectManagement.Tests/ProjectBriefings/ProjectBriefingSlideComposerTests.cs

VALIDATION AFTER REPLACEMENT
----------------------------
1. Clean and rebuild the solution in Visual Studio.
2. Run the ProjectBriefings tests.
3. Generate a Project Update Sheets deck with the SDD profile enabled.
4. Verify the sequence:
      Cover -> SDD – Growth over the years -> Portfolio at a glance -> Project sheets -> Closing.
5. Use Ctrl+F5 only if browser assets from earlier briefing-deck packages are also being tested;
   this package itself contains no JavaScript or CSS changes.
