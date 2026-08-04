PRISM BRIEFING DECK — CEREMONIAL CLOSING SLIDE REFINEMENT
==========================================================

PURPOSE
-------
This package refines the closing slide introduced by the earlier
"Professional Closing Slide" implementation. It applies to both Standard PRISM
Briefing decks and Project Update Sheet decks.

IMPLEMENTED DESIGN
------------------
1. Removes the generic "PROJECT BRIEFING DECK" descriptor.
2. Places "SIMULATOR DEVELOPMENT DIVISION" below the tricolour accent.
3. Uses a wider, near-rectangular ceremonial maroon field with substantially
   reduced corner curvature.
4. Removes the two horizontal divider rules from the closing slide.
5. Shortens and thins the equal saffron, white and green segments so that they
   read as a ceremonial accent rather than a progress bar.
6. Editorial Light places the two logos directly on the light canvas, without
   application-style backing tiles.
7. Graphite Dark retains compact dark-neutral logo plates, but removes their
   visible outline.
8. Keeps the exact selected closing text only: JAI HIND or THANK YOU.
9. Keeps the closing slide free from slide numbers, footers, project metadata,
   classification lines and generated remarks.

READY-TO-REPLACE FILES
----------------------
Replace these project-relative files:

Services/ProjectBriefings/Presentation/ProjectBriefingSlideComposer.cs
ProjectManagement.Tests/ProjectBriefings/ProjectBriefingSlideComposerTests.cs
ProjectManagement.Tests/ProjectBriefings/ProjectBriefingContractTests.cs

PRE-REQUISITE
-------------
The earlier PRISM professional closing-slide implementation must already be
present. This package is a focused refinement of that implementation.

DATABASE
--------
No migration or data update is required.

AFTER REPLACEMENT
-----------------
1. Clean the solution.
2. Rebuild in Visual Studio.
3. Run the ProjectBriefings test group.
4. Generate one Editorial Light and one Graphite Dark deck.
5. Confirm that the final slide contains:
   - JAI HIND or THANK YOU;
   - the short tricolour accent;
   - SIMULATOR DEVELOPMENT DIVISION below it;
   - no PROJECT BRIEFING DECK descriptor;
   - direct logos in light theme;
   - compact borderless dark plates in Graphite Dark.

VALIDATION PERFORMED IN THIS PACKAGE
------------------------------------
- Unified patch dry-run and clean application verification.
- Byte-for-byte comparison of patched files against packaged files.
- Git whitespace validation.
- Structural delimiter and string-balance checks for all modified C# files.
- ZIP integrity and SHA-256 manifest generation.

The .NET SDK is not installed in the packaging environment; therefore a full
C# compilation and xUnit execution must be completed in Visual Studio.
