PRISM Publications — Phase 23.1
Compendium Runtime Hardening & Publication Review Freeze

PURPOSE
Phase 23 established project-by-project review, publication-specific imagery/crop,
review fingerprints and Blocker/Warning/Information readiness. Phase 23.1 hardens
the browser workflow so these capabilities are clear, deterministic and safe under
zero-selection, refresh, review and large-selection conditions before the physical
Compendium PDF is redesigned in Phase 24.

IMPLEMENTED

1. Selection register semantics
- Description, Arm/Service and Cost readiness pills now include explicit state icons
  and accessible explanatory titles; state is no longer communicated by colour alone.
- Photography now reads "1 photo", "N photos" or "No photo" instead of an opaque count.
- Existing compact selection-table density is retained.

2. Deliberate zero-selection state
- Clear selection is hidden/disabled when there is nothing to clear.
- Review navigation is disabled until navigation is meaningful.
- Readiness shows neutral dashes rather than presenting initial setup as "1 blocker".
- Catalogue structure and findings use neutral setup copy until projects are selected.
- Backend no-selection blocking semantics remain intact, so generation is still safe.

3. Review-state clarity
- Per-project state is reduced to four meaningful visual states:
  Blocked / Review required / Warning / Ready.
- Mark Reviewed updates the browser state immediately, then authoritative server
  preflight verifies the review fingerprint.
- Changing publication imagery/crop immediately invalidates review locally and then
  refreshes authoritative readiness.
- Review progress and state use polite live-region updates.

4. Deterministic "Next requiring attention"
Attention navigation now follows publication priority:
  1. Blocker
  2. Non-review publication warning
  3. Stale review / project changed after review
  4. Never reviewed
The button becomes "No further attention" and disables itself when no target remains.

5. Publication-order progress navigation
- The right rail displays a compact status marker for Blocked, Warning,
  Review required or Ready.
- Clicking a project name remains the fast route to that project's Review workspace.
- Drag/reorder/removal behaviour is preserved.

6. Readiness refresh integrity
- Old findings are removed as soon as the working publication changes.
- While authoritative preflight runs, metrics show a pending state and findings show a
  dedicated "checking" panel instead of stale results.
- Severity filters and Current project only are temporarily disabled during refresh.
- Filter context is preserved across normal refreshes and reset only when the selection
  is cleared.
- Preflight failures produce one explicit blocker and an actionable unavailable state.

7. Final-output disabled states
- Preview and Download use synchronized HTML disabled + aria-disabled state.
- Disabled buttons have deliberate muted styling, no hover affordance and explanatory
  title text.
- Output still uses the existing Phase 23 server canGenerate policy; this phase does
  not weaken publication safety.

8. Accessibility / interaction polish
- Review navigation controls have explicit labels and aria-disabled synchronization.
- Readiness spinner has status semantics.
- Focus-visible treatment is added to the principal Compendium navigation controls.
- Reduced-motion preference is respected for the new interaction styling.

NO SCHEMA / BACKEND CHANGE
Phase 23.1 intentionally changes only the Razor view, Compendium page JavaScript,
Publications stylesheet and contract tests. There is no migration and no C# service
replacement in this overlay.

INTENTIONALLY DEFERRED TO PHASE 24
- New Compendium cover design
- Institutional front matter redesign
- Technical-category index/dividers redesign
- Detailed project-page redesign
- Page planner and content-aware physical composition
- Post-compose PDF verification / "PDF verified · N pages"
- Back/closing cover redesign

INSTALLATION
1. Stop the application/debug session.
2. Replace the four files listed in REPLACEMENT-MANIFEST.txt.
3. Copy tools/Test-PrismPublicationsPhase23_1.ps1.
4. Clean bin/obj.
5. Run the Phase 23.1 validator.
6. Start PRISM and perform the runtime checks below.

RECOMMENDED RUNTIME CHECK
- Select 6–8 projects, including completed, ongoing and at least one no-photo project.
- Confirm the selection-table readiness pills and "No photo" state are clear.
- Confirm right-rail project states populate after preflight.
- Mark one project reviewed and confirm it becomes Ready/Warning immediately.
- Adjust that project's crop and confirm it immediately returns to Review required.
- Use Next requiring attention and confirm higher-priority projects are visited first.
- Change filters while preserving selection.
- Save/reload the Compendium and verify membership/order/image/crop restoration.
- Preview the existing PDF and confirm the reviewed publication image/crop is used.

If these checks pass, the web-authoring workflow is ready to freeze and Phase 24 can
focus exclusively on physical Compendium publication design and composition.
