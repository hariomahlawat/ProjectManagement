PRISM ERP — MODULAR SDD INSTITUTIONAL PROFILE SLIDE
===================================================

PURPOSE
-------
This package adds an optional, modular “SDD – Growth over the years” slide to the
Project Briefing Deck Builder. The slide is available in both Standard PRISM
Briefing and Project Update Sheet decks and is inserted immediately after the
cover slide and before the portfolio/project content.

FINAL BEHAVIOUR
---------------
1. History timeline
   - User-maintained authorised milestones.
   - Default milestone set: 1986, 1991, 1998, 2001, 2016 and 2024.
   - Milestone wording remains exactly as entered; PRISM does not infer or rewrite it.

2. Simulators/Projects Developed
   - Read-only ERP data.
   - Headline: completed projects.
   - Detail: technical-category-wise project counts.

3. Proliferated
   - Read-only ERP data from approved proliferation aggregates.
   - Headline: combined approved proliferation total.
   - Detail: technical-category-wise quantities.
   - The slide deliberately does NOT show an SDD/515 ABW split.

4. Assistance to Field Formations
   - Read-only ERP data from the Training Tracker.
   - Headline: total trainees.
   - Detail: financial-year-wise trainee figures.
   - Optional highlight: distinct units and individuals trained for the configured
     technical category (AR/VR by default).

5. Intellectual Property
   - Read-only ERP data from the IPR Register.
   - Headline: protected IPR total.
   - Detail: patents granted, copyrights registered and patents filed.

6. Military–Academia–Industry Synergy
   - User-maintained MoU/institutional-partner lines.
   - No figures are inferred from JDP or activity records.

7. Institutional recognition
   - Optional user-maintained citation label and count.

MODULAR CONFIGURATION
---------------------
Deck Settings now allows the user to:
- enable/disable the SDD profile slide;
- edit the slide title;
- enable/disable the history timeline;
- maintain history milestones using “YEAR | text” lines;
- include, exclude and reorder the five profile modules;
- set the maximum number of detail rows;
- choose the training-highlight technical category;
- maintain MoU/partner lines;
- enable an optional unit-citation strip and set its authorised label/count.

ERP-backed metrics are not editable in the deck configuration.

COMPATIBILITY
-------------
- Existing decks remain unchanged: the SDD profile is disabled by default.
- The configuration is stored in the existing versioned SelectionRulesJson.
- No database migration is required.
- All previous briefing-deck functionality, adaptive layouts, status strip,
  maroon institutional styling and closing slide are retained.

REPLACEMENT METHOD
------------------
1. Back up the current solution.
2. Copy the folders/files from this package over the ProjectManagement project root,
   preserving the included relative paths.
3. Allow files with the same names to be replaced.
4. Clean the solution, delete stale bin/obj output if necessary, and rebuild.
5. Run the ProjectBriefings test suite.
6. Open an existing deck, enable “SDD institutional profile” under Deck Settings,
   review the authorised milestone/MoU/citation content, save, and generate a PPTX.

PATCH ALTERNATIVE
-----------------
A unified patch is included under _PATCH and is also supplied separately. Apply it
from the ProjectManagement repository root only when the source exactly matches the
preceding cumulative briefing-deck implementation.

VALIDATION PERFORMED
--------------------
- Targeted briefing-deck JavaScript tests: 26/26 passed.
- JavaScript syntax check passed.
- ProjectManagement.Tests.csproj XML validation passed.
- git diff whitespace/error check passed.
- Patch dry-run, clean application and file-by-file comparison passed.
- ZIP integrity and SHA-256 manifest checks are included.

ENVIRONMENT LIMITATION
----------------------
The .NET SDK/compiler is not installed in the packaging environment. Therefore a
C# build and the .NET ProjectBriefings test suite could not be executed here.
Perform a Visual Studio Clean/Rebuild and run the ProjectBriefings tests before
production deployment.
