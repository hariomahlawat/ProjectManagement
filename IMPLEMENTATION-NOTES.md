# PRISM Photos — Reference Quality Semantics & Matching Usability

This phase closes the remaining gap between technical face-embedding generation and the stricter governance decision to use a face as trusted identity evidence.

## Implemented

- Replaces the misleading use of `Occluded` for new crop-boundary assessments with explicit `CropIncomplete` and `SeverelyCropped` states. The legacy enum value is retained so existing database rows remain readable without a migration.
- Separates embedding-generation eligibility from trusted-reference suitability. `Detected` and `CropIncomplete` faces may receive an embedding when technically possible, while trusted-reference governance remains stricter.
- Introduces `Preferred`, `UsableWithCaution`, and `NotUsable` reference suitability in the authoritative readiness service.
- Legacy `Occluded` rows are interpreted as historical crop-boundary states, not as proof that a real object obscured the face.
- Existing legacy crop-incomplete appearances can be reprocessed. After a valid current embedding is generated, an authorised reviewer may explicitly trust a cautionary reference with a mandatory reason.
- Severe crop, low resolution, blur, poor exposure, extreme pose, suppression and sub-threshold quality remain blocked as trusted references.
- Candidate matching, grouping and review queries accept technically usable soft-quality embeddings while retaining their configured quality thresholds.
- Automatic initial reference selection remains restricted to the preferred `EmbeddingEligible` state; cautionary references are never silently promoted.
- The person identity page now displays cautionary readiness explicitly and uses an amber `Use with caution` governance action when appropriate.
- Reference-trust audit metadata records the evaluated reference suitability.
- `Correct appearance` and matching-reference governance panels now expand inline instead of using viewport-clipped absolute flyouts.
- Matching-reference deep links use the PRISM sticky-header offset through `--pm-header-height`.

## Database

No EF migration is required. `MediaFaces.QualityStatus` is stored as a varchar enum name; the new values fit the existing column. The legacy `Occluded` enum member remains present for existing rows.

## Expected workflow for the current legacy appearance

If the persisted crop-completeness signal is not severely incomplete, the existing `Occluded` row should now show a repairable/cautionary state. `Prepare for matching` re-runs face analysis, stores a current embedding when technically usable, and updates the quality state to `CropIncomplete` or another current state. The reviewer can then explicitly choose `Use with caution` if policy permits.
