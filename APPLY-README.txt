PRISM PROJECT BRIEFING — RIGHT INSIGNIA VISUAL BALANCE
======================================================

Purpose
-------
This focused refinement improves the visual weight of the right-side SDD insignia
on Project Update Sheet slides. It preserves the approved white header, centred
maroon project title, left formation insignia, and text-only footer.

Apply
-----
1. Copy the contents of this package into the ProjectManagement project root.
2. Preserve the folder structure.
3. Replace the four existing files when prompted.
4. No database migration is required.

Files
-----
Services/ProjectBriefings/Presentation/ProjectBriefingSlideComposer.UpdateSheet.cs
Services/ProjectBriefings/Presentation/ProjectBriefingSlideComposer.cs
ProjectManagement.Tests/ProjectBriefings/ProjectBriefingSlideComposerTests.cs
ProjectManagement.Tests/ProjectBriefings/ProjectBriefingContractTests.cs

Implementation
--------------
- Introduces a dedicated HeaderVariant.ProjectUpdateSheet path so the adjustment
  does not alter Standard PRISM Briefing slides.
- Retains the left formation insignia at its approved size.
- Enlarges the visually slender right SDD insignia from 0.36 x 0.56 in to
  0.46 x 0.68 in, while preserving the same optical vertical centre.
- Keeps the title safe area unchanged and avoids encroachment on long titles.
- Adds regression coverage confirming the update-sheet-specific branding path
  and the increased optical footprint of the right insignia.

Verification
------------
dotnet build
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj

The current packaging environment does not include the .NET SDK, so the full
build and test suite could not be executed here.
