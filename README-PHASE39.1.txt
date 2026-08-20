PRISM Simulators Compendium — Phase 39.1 Focus Review Reliability
================================================================

Purpose
-------
Correct the Focus Review regression visible after Phase 39. The Focus Review button
was toggling correctly in JavaScript, but its proof-first layout only became active at
1600 CSS px. On common 1920x1080 Windows desktops using 125% display scaling the
browser viewport is approximately 1536 CSS px, so the button changed to "Exit focus"
without reclaiming the Publication Structure / Final Output rail.

Production change
-----------------
1. wwwroot/css/pages/projects-publications.css
   - Aligns Focus Review with the existing desktop/output-dock breakpoint: >= 1200px.
   - Hides the canonical Publication Structure / Final Output rail while focused.
   - Expands the main review surface to the full workspace width.
   - Restores proof + inspector side-by-side even where normal review intentionally
     stacks them below 1550px.
   - Uses progressive proof/inspector sizing at 1200, 1400, 1700 and 1900px.
   - Restores bounded independent proof/inspector scrolling in focused mode.
   - Reserves bottom space for the fixed output dock so it cannot cover review controls.
   - Removes the obsolete Phase 28 focus-layout rules that conflicted with the later
     proof-first design.

Regression tests
----------------
2. wwwroot/js/projects/publications-compendium-contract.test.js
   - Updates the older Phase 28 contract so it verifies the current proof-first focus
     behaviour rather than the superseded 350px-rail layout.

3. wwwroot/js/projects/publications-compendium-phase39-1-focus-review.test.js
   - Verifies that the JS desktop threshold (1200px) and CSS focus threshold agree.
   - Verifies that the rail is hidden in focus mode.
   - Verifies that the focus layout overrides the ordinary <=1549.98px stacked review.
   - Verifies reserved output-dock space and progressive proof sizing.
   - Prevents reintroduction of the obsolete "keep the rail in Focus Review" contract.

No backend / schema change
--------------------------
- No EF Core migration.
- No database change.
- No Razor Page handler change.
- No PDF builder/verifier change.
- No production JavaScript logic change is required for this regression; the Phase 39
  toggle and output-dock logic are already correct.

Validation performed
--------------------
node --check wwwroot/js/pages/projects-compendium.js
node --test wwwroot/js/projects/*compendium*.test.js

Result: 232 tests passed, 0 failed.

Recommended smoke test after paste
----------------------------------
1. Load a Compendium containing reviewed/unreviewed projects.
2. At a normal 1920-class Windows desktop (including 125% display scaling), navigate
   to Review publication and click Focus review.
3. Confirm:
   - button changes to Exit focus;
   - Publication Structure / Final Output rail disappears;
   - proof + review inspector sit side-by-side;
   - proof gets materially more width;
   - compact output dock remains available at bottom-right;
   - no controls are hidden under the dock.
4. Click Exit focus and confirm the rail returns and the floating dock again follows
   normal Final Output visibility.
5. Retest Preview PDF and Download Compendium PDF. Phase 39 generation behaviour
   should remain unchanged.
6. Resize below 1200 CSS px and confirm Focus review is not offered.
