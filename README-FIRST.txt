PRISM BRIEFING DECK — SDD PROFILE TABLE-FIRST ARCHITECTURE
==========================================================

Purpose
-------
This package refines the modular “SDD – Growth over the years” slide introduced by
PRISM-Briefing-Deck-Modular-SDD-Profile.

Key changes
-----------
1. Maintainable PowerPoint output
   - Institutional history is one native editable PowerPoint table.
   - Each numeric output module is one native editable table.
   - Military–Academia–Industry Synergy is one full-width native table.
   - Unit citations are one native recognition table.
   - The slide no longer consists of dozens of independent text boxes.

2. Professional slide composition
   - Uses the normal PRISM slide header, maroon top rule, branding and footer.
   - Timeline remains visually styled: alternating milestones, burgundy years,
     gold markers and a restrained chronology rule.
   - All four ERP metric modules remain generous equal panels.
   - The fifth partnership module becomes a full-width band instead of being
     silently omitted or compressed into a narrow fifth column.
   - Proliferation values such as 10,081 receive a dedicated value column and do
     not wrap across lines.
   - Graphite Dark uses high-contrast header and highlight treatments.

3. Correct project scope
   - “Projects Developed” defaults to ORIGINAL COMPLETED PROJECTS.
   - Rebuild projects are excluded from both the headline and category breakdown.
   - Users may explicitly select “All completed projects, including rebuilds”.
   - Existing deck JSON remains compatible; missing scope defaults to OriginalCompleted.

4. Improved Deck Settings UX
   - History milestones use a structured add/edit/reorder/delete editor.
   - MoU/partner entries use a structured add/edit/reorder/delete editor.
   - Training highlight uses the authoritative PRISM technical-category picker.
   - A live layout summary explains the automatic one-slide composition.
   - Partnership selection is validated so a selected module cannot disappear.

5. Reusable foundation
   - Native-table cells now support optional borders, margins, vertical anchoring,
     grid spans and horizontal merges.
   - These primitives can be reused for future data-heavy custom briefing slides.

Replacement
-----------
Copy the files in this package over the same relative paths in the ProjectManagement
solution. Stop IIS / the application pool before replacement if deploying directly.

Then:
1. Clean the solution.
2. Rebuild ProjectManagement.
3. Build and run ProjectManagement.Tests, especially the ProjectBriefings tests.
4. Generate Editorial Light and Graphite Dark decks and inspect the SDD profile slide.

Database
--------
No database migration is required. The new project-scope value is stored in the existing
versioned deck configuration JSON.

Validation completed in the packaging environment
-------------------------------------------------
- JavaScript syntax check: passed.
- project-briefing-decks.js tests: 26/26 passed.
- Patch dry-run and clean application: passed.
- Whitespace / patch validation: passed.
- ZIP and SHA-256 verification: included.

Limitation
----------
The .NET SDK was not available in the packaging environment, so a Visual Studio
Clean/Rebuild and ProjectBriefings test run is required before production deployment.
