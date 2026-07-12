# PRISM Photos Phase 8.3 Completion

This package completes the Phase 8.3 classification and review pipeline.

## Delivered

- `hybrid-media-v5` classifier with deterministic evidence and local YuNet face-presence assistance.
- Separate prediction, effective classification, decision status, reason code, scores, metrics and processing duration.
- Central classification decision policy.
- Append-only successful and failed classification runs.
- Concurrency-safe individual and bulk review.
- Transactional bulk review with mandatory reason and 500-item hard limit.
- Complete automatic-reset/reclassification state clearing.
- Corrected migration backfill and reconciled EF snapshot.
- Air-gapped model provenance fields and local approved-model manifest.
- Updated review UI, warning/error states, selection count and safe Razor option rendering.
- Updated classifier tests.

## Required deployment checks

1. Preserve environment-specific connection strings and secrets while merging appsettings files.
2. Run `dotnet clean`, `dotnet restore`, `dotnet build -c Release`, and `dotnet test -c Release`.
3. Apply the MediaLibraryDbContext migration if not already applied.
4. Restart the application.
5. Open Classification Review and select **Reclassify stale assets**. Existing `deterministic-media-v4` records will be queued because the new version is `hybrid-media-v5`.
6. Keep People and the face worker disabled until the live classification results are reviewed.
7. Validate the two portraits, Draw.io flow chart, floral graphic, worksheets, chart and drone photograph.

## Air-gapped operation

The production configuration no longer requires a public model URL. Models are loaded from the local model root and identified through `App_Data/media-models/approved-models.manifest.json`, configured artifact IDs, licences and SHA-256 values.
