PRISM PROJECT BRIEFING - PROJECT UPDATE SHEET HEADER POLISH
==========================================================

BASELINE
--------
Apply this package after the previously supplied Project Update Sheets implementation
and the Total IPA Cost / white-header refinement dated 29 Jul 2026.

HOW TO APPLY
------------
1. Stop the running application or IIS application pool.
2. Copy the contents of this package into the ProjectManagement project root.
3. Preserve the folder structure and replace the five existing files.
4. Rebuild and run the automated tests.
5. Generate a Project Update Sheets deck with Header branding = All slides.

RECOMMENDED COMMANDS
--------------------
dotnet build
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj

IMPLEMENTED
-----------
- Removes the repeated "PROJECT UPDATE SHEET" label.
- Restores both insignia to project-sheet headers when branding is enabled.
- Centres the project name between the two insignia.
- Uses a restrained maroon project-title treatment on a white header.
- Removes the redundant footer insignia; the footer is text-only again.
- Stacks Fund, DFPDS and CFA on separate lines.
- Labels SO Date and Firm consistently whenever either value is present.
- Shows only one "Not recorded" when both SO Date and Firm are absent.
- Gives long external remarks more row height and enforces a readable minimum font.
- Dynamically aligns the photograph and project-brief panels with the facts table.
- Uses two-decimal precision for the R&D and IPA totals on the Project Update Sheets
  portfolio summary, without changing project-level cost formatting.

DATABASE
--------
No database migration is required.
