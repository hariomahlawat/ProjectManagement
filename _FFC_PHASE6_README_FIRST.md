# FFC Phase 6 — Footprint and Presentation Consolidation

## Apply this package

This package is designed to be applied **after FFC Phase 5 — Transaction and Persistence Reliability**.

Extract the ZIP into the `ProjectManagement` project root, retain the directory structure, and replace matching files.

No database migration is required.

## What this phase implements

### Unified Footprint workspace

A new page consolidates the former World Map and Country Board:

```text
/ProjectOfficeReports/FFC/Footprint
```

It provides two bookmarkable views:

```text
?view=map
?view=cards
```

Both views use the same query service, filters, aggregation rules and country-detail panel.

### Map mode

- Opens in World focus by default.
- Supports World, Africa and South Asia quick focus.
- Colours one selected metric at a time:
  - Total units
  - Installed units
  - Delivered, awaiting installation
  - Planned units
- Generates data-driven legend ranges from the filtered result.
- Keeps countries with FFC activity visible when the selected metric is zero.
- Provides concise hover details and a keyboard-accessible country selection path.
- Opens a structured country panel with year records, project quantities, current position and workspace links.
- Falls back cleanly to Country Cards when Leaflet or GeoJSON cannot be loaded.

### Country-card mode

- Uses the same country totals and drill-down panel as the map.
- Supports sorting by total units, country, installed units, planned units and most recent activity.
- Provides full-card keyboard and pointer interaction.
- Preserves year, country and project-search filters when switching views.

### Presentation mode

- Hides the normal PRISM header, module navigation, breadcrumbs and footer.
- Retains the selected view and filters.
- Removes editing and administrative controls.
- Provides a briefing header, position date, summary totals, display-mode switch, browser full-screen action and Exit Presentation action.
- Uses the same live FFC dataset; no separate presentation cache or duplicate data source is introduced.

### Navigation consolidation

- The Portfolio Overview now exposes one prominent `Footprint` action.
- Duplicate `World footprint`, `Country board` and `Create record` menu entries are removed.
- The remaining More menu is clearly labelled as Administration and contains only:
  - Archived records
  - Country settings
- Country-Year Workspace links open the unified Footprint with the relevant country selected.

### Legacy compatibility

Existing routes remain valid for one compatibility cycle:

```text
/FFC/Map      -> /FFC/Footprint?view=map
/FFC/MapBoard -> /FFC/Footprint?view=cards
```

The former map data handler remains available for older clients during this cycle.

### Analytical read architecture

The new `IFfcFootprintService`:

- Excludes archived records and countries hidden from FFC reporting.
- Applies year, country and country/project search filters in the database.
- Aggregates installed, delivered-awaiting-installation and planned units using the established FFC semantics.
- Batches linked-project stage and canonical progress resolution.
- Avoids per-country and per-project query loops.
- Supplies one shared result to Map and Country Cards.

### Offline map asset

The phase adds:

```text
wwwroot/data/maps/world-countries-simplified.geojson
```

The file contains 248 country features and only the properties required by FFC (`iso3` and `name`). It is approximately 2.15 MB, reduced from the existing high-detail map asset of approximately 23 MB.

It was generated offline from:

```text
wwwroot/ffc/maps/world_india_view.geojson
```

using topology-preserving geometry simplification with tolerance `0.06`, while retaining ISO Alpha-3 identifiers and country names. The original source asset remains unchanged.

## Protected Detailed Table

The following files were not modified:

```text
Areas/ProjectOfficeReports/Pages/FFC/MapTableDetailed.cshtml
Areas/ProjectOfficeReports/Pages/FFC/_DetailedTablePartial.cshtml
wwwroot/js/pages/project-office-reports/ffc/ffc-map-table-detailed-inline-edit.js
```

Their verified SHA-256 values remain:

```text
513d9173fa3593cc6cdbf00e0ef781fb3ce70d0ddc7b3055d9ce44644f2f32a8  MapTableDetailed.cshtml
d977daadc4b15ac33d43b279fee11bc397e4654d1fc4aca67c8fde39ca556e32  _DetailedTablePartial.cshtml
37150acba397898bf73e1cff453aaad2f8fae1b8fe3a1c7d77fe7294b516e20a  ffc-map-table-detailed-inline-edit.js
```

## Verification completed during preparation

- JavaScript test suite: **215 passed, 0 failed**.
- JavaScript syntax validation: passed.
- Project and test-project XML parsing: passed.
- Simplified GeoJSON parsing: passed.
- GeoJSON feature count: 248.
- Required FFC country ISO codes were confirmed in the simplified asset.
- Phase patch application check: passed.
- ZIP archive integrity and package checksums: passed.
- Protected Detailed Table checksums: unchanged.

The preparation environment does not contain the .NET SDK. Therefore, the actual .NET build and test suite must be run on the development computer before deployment.

## Build and test

Run from the `ProjectManagement` project root:

```powershell
Remove-Item -Recurse -Force .\bin, .\obj -ErrorAction SilentlyContinue

npm ci
npm test

dotnet restore
dotnet build ProjectManagement.csproj -c Release
dotnet test ProjectManagement.Tests\ProjectManagement.Tests.csproj -c Release
```

No `dotnet ef database update` command is required for Phase 6.

## Manual acceptance checks

1. Open `/ProjectOfficeReports/FFC/Footprint` and confirm World Map is the default.
2. Switch among Total, Installed, Delivered and Planned metrics and verify the legend and country shades change.
3. Select France, Myanmar or another active country and confirm the year/project panel opens.
4. Switch to Country Cards and confirm totals and selected filters remain consistent.
5. Apply year, country and project-search filters.
6. Enter Presentation mode and confirm normal PRISM chrome is hidden.
7. Exit Presentation and confirm query state is retained.
8. Open the former `/FFC/Map` and `/FFC/MapBoard` URLs and confirm redirection.
9. Confirm the Portfolio Overview contains one Footprint action and no duplicate Create Record item.
10. Confirm the Detailed Table remains visually and functionally unchanged.
