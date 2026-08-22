# Release notes — Phase 42

Date: 22 August 2026  
Build: `CompendiumPdf_2026-08-22_phase42-slot-stable-cover`  
PDF contract: `physical-a4-v42`

## Defect corrected

The earlier Cover Editor treated automatic imagery as a surface-wide derived sequence. Replacing one slot invalidated and rebuilt that sequence, allowing subsequent slots to move. This was a defect, not an intended sequential-editing rule.

Phase 42 makes the resolved project/photo pair part of each automatic slot's persisted state. Allocation now runs in three deterministic passes: reserve manual assignments, retain valid sticky automatic assignments, then allocate only unresolved automatic slots.

## Production safeguards

- The browser editor, preset persistence, readiness evaluation and PDF export use the same slot semantics.
- Export retries cannot duplicate a Portfolio Quartet image after an earlier candidate fails to render.
- An explicit photograph is excluded from every automatic fallback pass.
- Transient preview URL invalidation no longer clears persisted automatic identities.
- Photo-picker requests remain abortable and stale responses cannot mutate a newer slot selection.
- Existing saved Compendiums require no migration; unresolved legacy automatic slots are populated deterministically on their next save/export path.

## Rollback

Restore the previous application source/publish folder. No database rollback is needed because Phase 42 introduces no schema change. Automatic rows populated by Phase 42 remain compatible with the existing nullable columns.
