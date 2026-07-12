# Photos Phase 8.2 — Classification Calibration and Review Operations

## Purpose

This phase corrects the over-conservative classification behaviour observed after Phase 8.1 without weakening the fail-closed face-eligibility policy.

## Changes

- Upgraded deterministic classifier to `deterministic-media-v4`.
- Added natural-image entropy, luminance variance, spatial texture, colour-diversity and portrait-geometry signals.
- Strengthened diagram filename detection, including Draw.io assets.
- Kept diagrams, documents and screenshots subject to their own configured thresholds.
- Split dashboard reporting into eligible photographs, unknown/review-required assets and confirmed non-photographs.
- Added reason-specific review badges instead of a generic `Excluded` label.
- Added bulk manual classification for up to 500 selected assets in one audited transaction.
- Added filters for unknown/review-required and confirmed non-photographic assets.
- Corrected catalogue-consistency wording so catalogue presence and source availability are not conflated.
- Renamed shared worker telemetry to `Media processing worker` to distinguish it from the face queue worker.
- Added classifier tests for a natural portrait without EXIF and a named flow chart.

## Deployment

1. Replace the supplied files.
2. Build and run tests.
3. Restart the application.
4. Open **Media Intelligence → Classification**.
5. Select **Reclassify stale assets**. Every automatic v3 classification will be reprocessed by v4; manual decisions remain untouched.
6. Review `Unknown / needs review` and `Low confidence` sets before enabling face processing.

No database migration is required.
