PRISM ERP — SDD Institutional Profile Grouped-Shape Correction
================================================================

PURPOSE
-------
This package removes native PowerPoint tables from the SDD institutional-profile
slide and restores the open, presentation-oriented composition closest to the
authorised reference slide.

WHAT CHANGED
------------
1. The history timeline is no longer a PowerPoint table.
   - It is rendered as one top-level PowerPoint group.
   - The group contains one chronology rule, milestone markers and one editable
     rich-text box per milestone.
   - Each milestone text box contains both the year and authorised description.
   - There are no visible cell borders or spreadsheet grids.

2. Every institutional output module is no longer a PowerPoint table.
   - Each selected module is one top-level PowerPoint group.
   - The group contains a rounded card, coloured header treatment, headline,
     supporting detail and optional highlight.
   - All five selected modules are rendered in the original five-column layout.
   - The Military–Academia–Industry Synergy module remains a proper list module.

3. The Unit Citation strip is one grouped PowerPoint object.

4. SlideCanvas now supports a reusable AddGroup(...) primitive using native
   PresentationML group shapes. This can be reused by future modular custom slides.

5. The SDD profile slide contains no native PowerPoint tables or graphic frames.
   Existing native tables elsewhere in the deck remain unchanged.

6. Original completed projects remain the recommended/default Projects Developed
   scope. Rebuild projects remain excluded from both the headline and the technical-
   category breakdown unless the user explicitly selects the inclusive scope.

7. The corrected briefing-deck JavaScript is included so the Deck Settings drawer
   retains the prior initialization-order fix.

REPLACEMENT
-----------
Copy each file over the file with the same relative path in the ProjectManagement
solution. Do not copy the package's outer folder into the application.

Then:
1. Close the application and Visual Studio debug session.
2. Replace the files.
3. Delete bin and obj folders or run Clean Solution.
4. Rebuild the solution.
5. Run the ProjectBriefings tests.
6. Start the application and use Ctrl+F5 once to bypass cached JavaScript.
7. Generate both Editorial Light and Graphite Dark decks and inspect the SDD profile.

DATABASE
--------
No database migration is required.

VALIDATION PERFORMED IN THE PACKAGING ENVIRONMENT
--------------------------------------------------
- project-briefing-decks.js syntax check passed.
- 27/27 briefing-deck JavaScript tests passed.
- PresentationML group-shape XML was validated by injecting an equivalent group into
  the PRISM PowerPoint template and successfully opening/converting it with
  LibreOffice Impress.
- Institutional-profile C# and test files passed structural delimiter checks.
- Patch generation and ZIP integrity checks passed.

LIMITATION
----------
The .NET SDK is not installed in the packaging environment, so a full C# build and
xUnit execution could not be performed here. A Visual Studio Clean/Rebuild and the
ProjectBriefings test run are required after replacement.
