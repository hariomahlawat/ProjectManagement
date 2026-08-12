PRISM Capability Brochure — Phase 19A
Print / Compact Finalisation and Design Freeze
==============================================

Purpose
-------
This is the final small hardening pass for Print / Compact before work moves to the
Digital / Comfortable profile. It implements the decisions from the final comparison
between the generated brochure and the original SDD brochure, especially the closing
/back-page identity, while preserving the verified four-stage publication workflow.

Implemented
-----------
1. Modernised heritage closing panel
   - Restores the original brochure's distinctive strategic-closing hierarchy using a
     restrained deep-navy frame and warm cream background.
   - Keeps the current clean, regular body typography; no all-body italics are restored.
   - Keeps the current green New Simulators terminal band unchanged.
   - Does not stretch or artificially fill final-page residual whitespace.

2. Geometry-safe visual change
   - Closing border increases from 1.1 pt to 2.0 pt for stronger separation.
   - Horizontal and vertical padding are reduced by the exact compensating amount.
   - The measured inner width and vertical shell remain unchanged:
       horizontal shell = 20.2 pt
       vertical shell   = 16.2 pt
   - Therefore the Visionary text wrap and planned closing height remain geometrically
     compatible with the existing measurement/planner/compositor contract.

3. Smart Flow state clarity
   - Before application: "Smart Flow opportunity".
   - After application:  "Smart Flow applied".
   - Applied-state explanatory copy no longer describes the order change as merely a
     suggestion.
   - The applied state receives a subtle visual treatment and retains Undo order change.

4. Saved brochure integration
   - Smart Flow order remains part of the durable project-order fingerprint already used
     by Shared Saved Brochures.
   - Applying Smart Flow therefore marks a loaded preset Modified / Modified locally.
   - Undoing back to the saved order returns the preset to clean state when no other
     durable configuration has changed.

5. Saved brochure header polish
   - "Saved brochure" now uses sentence case.
   - Load is disabled in the Unsaved brochure state and when the currently loaded preset
     is already clean.
   - Selecting another saved brochure enables Load explicitly.

Scope boundary
--------------
This phase deliberately does NOT redesign project cards, front-page artwork, project
packing, preflight, approval, image-DPI assessment, PDF verification or Shared Brochure
persistence. Those systems are frozen unless testing reveals a real defect.

Recommended acceptance check
----------------------------
1. Open the Brochure Builder with Unsaved brochure selected: Load must be disabled.
2. Choose a shared saved brochure: Load becomes available; load it.
3. Run a Print / Compact preflight that offers Smart Flow.
4. Confirm the card says Smart Flow opportunity before application.
5. Apply it: confirm the card says Smart Flow applied and the saved brochure becomes
   Modified / Modified locally.
6. Undo: confirm the original order returns and the preset becomes clean when no other
   durable setting changed.
7. Preview the same brochure and confirm planned and physically rendered page counts
   still agree.
8. Inspect the final page: Visionary Horizons must read as a distinctive navy/cream
   strategic closing module, followed by the existing green New Simulators band, with
   natural residual whitespace left untouched.
