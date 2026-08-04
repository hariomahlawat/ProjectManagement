PRISM ERP — Professional Closing Slide
======================================

Purpose
-------
Adds one professional ceremonial closing slide as the final slide of every
Project Briefing Deck, for both Standard PRISM Briefing and Project Update
Sheets.

User choices
------------
1. Jai Hind — default and recommended for command/internal military briefings.
2. Thank You — for industry, academic and external audiences.

Implementation behaviour
-----------------------
- One closing slide is always appended after every project, table, summary and
  continuation slide.
- The closing slide uses the selected Editorial Light or Graphite Dark theme.
- The design uses a deep institutional maroon field, large centred message,
  restrained tricolour rule and editable native PowerPoint shapes.
- No slide number, footer line, project data or generated remarks are shown.
- Branding follows the deck's Presentation Branding setting:
    None              -> no insignia on closing slide
    Cover and summary -> insignia on cover, summary and closing slides
    All slides        -> insignia throughout
- Existing decks automatically default to Jai Hind.
- The selected closing message is stored in the existing versioned deck JSON
  configuration. No database migration is required.
- Estimated and generated slide counts include the closing slide.

Replacement
-----------
Copy the files from this package into the ProjectManagement solution, preserving
all relative paths, and replace the existing files.

Then:
1. Clean the solution.
2. Rebuild the solution.
3. Run ProjectManagement.Tests, especially the ProjectBriefings tests.
4. Open Deck settings -> Appearance and verify the Closing slide choices.
5. Generate one Editorial Light and one Graphite Dark presentation and inspect
   the final slide.

Validation performed in the packaging environment
--------------------------------------------------
- JavaScript syntax check passed.
- Project briefing JavaScript suite passed: 25/25.
- Patch dry-run and clean application verification passed.
- Replacement package checksum and ZIP integrity checks passed.

The .NET SDK was not available in the packaging environment, so a final Visual
Studio Clean/Rebuild and .NET test run remains required on the development
machine.
