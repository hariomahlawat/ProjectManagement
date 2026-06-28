# PRISM Media Intelligence Phase 7B

This package implements the media-intelligence foundation and deterministic multi-class image classification.

## Included

- Explainable CPU-only classifier for Photograph, Screenshot, ScannedDocument, Diagram, PresentationSlide, Graphic and Unknown.
- Filename, EXIF/camera, dimensions, edge-density, flat-colour, background and colour-diversity signals.
- Granular future-safe media job types.
- Classification eligibility policy that excludes unavailable, deleted, archived, oversized, tiny and manually classified assets.
- Manual classification governance fields.
- Append-only classification audit records.
- Manual override and reset-to-automatic service.
- Additive EF Core migration.
- No proprietary library, hosted API or model weights.

## Deployment

1. Copy this folder's contents into the ProjectManagement root and replace matching files.
2. Build and run the application.
3. Open Photos Administration and initialise/apply the media catalogue migration if AutoMigrate is disabled.
4. Run Synchronize PRISM, then queue/retry classification jobs as required.

## Optional configuration

```json
"MediaLibrary": {
  "Classification": {
    "Enabled": true,
    "ScreenshotDetectionEnabled": true,
    "DocumentDetectionEnabled": true,
    "DiagramDetectionEnabled": true,
    "MinimumConfidence": 0.55,
    "AnalysisMaxDimension": 512,
    "ScreenshotThreshold": 0.62,
    "DocumentThreshold": 0.66,
    "DiagramThreshold": 0.64
  }
}
```

Existing manual classifications are never overwritten. Resetting a manual classification creates a new ClassifyMedia job and preserves the audit trail.
