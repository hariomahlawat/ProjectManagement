# Media Intelligence Phase 8.1 — Stabilisation and Operational Readiness

## Scope

This phase hardens the classification-to-face-processing boundary before People intelligence is enabled.

## Implemented

- One authoritative `IFaceEligibilityPolicy` used by queueing, processing, dashboards and review UI.
- Automatic face eligibility now requires:
  - available still image;
  - completed classification;
  - current classifier version;
  - `Photograph` classification;
  - configured minimum classification confidence.
- Human classification remains authoritative.
- New catalogue images start as `Unknown`, not `Photograph`.
- Changed image content resets stale automatic classification while retaining an intentional manual override.
- Stale classifier versions are requeued automatically during PRISM catalogue synchronisation.
- Administrators can explicitly reclassify all stale automatic records.
- Classification review now includes thumbnails, preview links, analysis state and the precise face-eligibility reason.
- Classifier category switches and category-specific thresholds are now honoured.
- Image analysis now uses two-dimensional Sobel-style edge measurement and local spatial flatness rather than scan-order pixel comparisons.
- People Review renders a controlled disabled state instead of returning HTTP 404.
- Readiness refresh now bypasses the five-minute readiness cache.
- Added eligibility-policy and option-validation tests.

## Configuration

`MediaLibrary:People:MinimumClassificationConfidence` defaults to `0.75`.

Manual `Photograph` classifications do not require automatic confidence. Manual non-photograph classifications always exclude the asset.

## Deployment

1. Back up the application and Media Library database.
2. Replace the files in the ready-to-replace package.
3. Preserve environment-specific connection strings and secrets while merging the new setting.
4. Run `dotnet restore`, Release build and tests.
5. Restart the application.
6. Open **Media Intelligence → Classification**.
7. Select **Reclassify stale assets**.
8. Allow the media worker to complete classification jobs.
9. Review low-confidence and stale records before enabling the face worker.

No database migration is required for this phase.
