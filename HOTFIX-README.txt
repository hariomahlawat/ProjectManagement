PRISM Compendium Phase 25 hotfix

Fixes CS0103 references to CompendiumCoverImagePolicy in:
- Services/Compendiums/CompendiumExportService.cs
- Utilities/Reporting/CompendiumPdfReportBuilder.cs

Cause: Phase 25 CompendiumDtos.cs accidentally omitted the existing CompendiumCoverImagePolicy type.

Action: Replace Services/Compendiums/CompendiumDtos.cs with the included file.
No change is required in CompendiumExportService.cs or CompendiumPdfReportBuilder.cs.

The restored policy preserves the Phase 24.1 contract:
FrameWidthPoints = 491d
FrameHeightPoints = 300d
RenderWidthPixels = 1800
RenderHeightPixels = 1100
