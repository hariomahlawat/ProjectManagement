# FFC Phase 7 — PowerPoint Briefing Export

## Apply this package

This package is designed to be applied **after FFC Phase 6A — Footprint Runtime Stabilisation**.

Extract the ZIP into the `ProjectManagement` project root, retain the directory structure, and replace matching files.

**No database migration is required.**

## What this phase implements

### Professional PowerPoint export

The Footprint page now provides a prominent **Download PowerPoint** action. The export drawer supports:

- Current filtered portfolio, complete portfolio, or selected countries.
- Executive brief or full portfolio deck.
- Optional project-level slides, current progress, milestone remarks, and attachment register.
- Custom title and subtitle.
- Optional handling/classification marking entered by the authorised user.

The generated file is a native `.pptx` briefing deck with a 16:9 widescreen layout. Text, tables, bars, shapes, and slide content remain editable in PowerPoint. The footprint map is inserted as a high-resolution locally rendered image.

### Executive brief structure

The executive deck contains:

1. Cover.
2. Portfolio at a glance.
3. Global footprint.
4. Country-wise unit position.
5. IPA and GSL position.
6. Operational focus.

Continuation slides are generated automatically when the portfolio is too large for one slide.

### Full portfolio structure

The full deck adds:

- Country summary slides.
- Project-level quantity, position, stage, and progress slides.
- Optional attachment registers.
- Consolidated country-year appendix slides.

Long tables are divided into continuation slides rather than reducing text to an unreadable size.

### Fully offline and server-side

The export uses:

- Open XML SDK for `.pptx` creation.
- Existing SkiaSharp runtime for offline map rendering.
- Existing simplified local GeoJSON.
- A local 16:9 PowerPoint template stored under `Resources/FfcPresentation`.

It does **not** require Microsoft Office, PowerPoint automation, internet access, browser screenshots, or an external rendering service.

### Footprint simplification

The following browser-only presentation features have been removed:

- Africa and South Asia focus controls.
- `focus` query-string handling.
- Browser Presentation mode.
- Full-screen presentation-specific UI and layout logic.

The normal map now fits the countries containing FFC activity and provides a single **Fit footprint** action.

## Business rules preserved

- Linked FFC progress continues to use the linked PRISM Project external remark as the canonical source.
- Installed, delivered-awaiting-installation, and planned quantities use the established shared FFC semantics.
- Archived records and countries hidden from FFC reporting remain excluded.
- The existing Detailed Table layout, grouping, inline editing, and Excel export remain unchanged.

## Files added or replaced

See `_FFC_PHASE7_FILES.txt` for the complete list and `_FFC_PHASE7_SHA256SUMS.txt` for package checksums.

## Build and test

Run from the project root:

```powershell
Remove-Item -Recurse -Force .\bin, .\obj -ErrorAction SilentlyContinue

npm ci
npm test

dotnet restore
dotnet build ProjectManagement.csproj -c Release
dotnet test ProjectManagement.Tests\ProjectManagement.Tests.csproj -c Release
```

No `dotnet ef database update` command is required.

## Manual acceptance checks

1. Open `/ProjectOfficeReports/FFC/Footprint`.
2. Confirm Africa, South Asia, and Presentation controls are no longer present.
3. Confirm **Download PowerPoint** opens the export drawer.
4. Generate an Executive brief for the current filtered portfolio.
5. Open the downloaded `.pptx` and verify the cover, summary, map, country position, milestone, and operational-focus slides.
6. Generate a Full portfolio deck with project details and current progress.
7. Apply year, country, and search filters and confirm Current filtered portfolio respects them.
8. Select specific countries and confirm only those countries appear.
9. Add an optional handling marking and confirm it appears on every slide.
10. Confirm the Detailed Table remains visually and functionally unchanged.

## Verification completed during preparation

- JavaScript suite: **218 tests passed, 0 failed**.
- JavaScript syntax validation: passed.
- Project and test-project XML validation: passed.
- Simplified GeoJSON validation: passed.
- PowerPoint template ZIP/Open XML structure validation: passed.
- Template dimensions: 16:9 (`12192000 × 6858000` EMU).
- New C# source delimiter and lexical-balance checks: passed.
- Protected Detailed Table SHA-256 values remain unchanged.
- Patch application and ZIP integrity checks are completed as part of packaging.

The preparation environment does not contain the .NET SDK. Therefore, the actual .NET build and .NET test suite must be run on the development computer before deployment.
