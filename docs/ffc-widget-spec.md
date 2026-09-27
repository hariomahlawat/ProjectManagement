# FFC Simulators Map – Dashboard Widget Spec

Scope: the dashboard FFC widget (`Areas/Dashboard/Components/FfcSimulatorMap/_Widget.cshtml`, `wwwroot/js/widgets/ffc-simulator-map.js`, data from `FfcCountryRollupDataSource` via `Pages/Dashboard/Index.cshtml.cs`) and the full map page (`/ProjectOfficeReports/FFC/Map`). Each item below is marked with its implementation status as of 2026-09-27.

## A. Pins and hover behaviour
| Requirement | Status |
| --- | --- |
| Teardrop count pins that do not scale with zoom | Implemented as Leaflet `divIcon` pins. The size is fixed per pin but grows slightly with the number of digits (32 px base + 5 px per extra digit, up to +10). Mixed pins show a "+N planned" badge. `data-deconflict-markers="true"` spreads overlapping pins |
| One tooltip per country, shared by the pin and the polygon | Implemented: `showCountryTooltip(iso, anchor)` in `ffc-simulator-map.js` |
| Tooltip content: country, Installed, Delivered, totals | Implemented. The payload carries `Installed`, `Delivered`, `Planned` and `TotalUnits` per country |
| Delayed hide (150–200 ms) to prevent flicker | Implemented: `scheduleHideTooltip()` waits 190 ms. There is no function named `hideCountryTooltip` |
| Hover highlight of pin and polygon | Implemented (active style and reset) |

## B. Below-map country list
The shipped design is a **chip strip**, not the compact table this spec originally proposed.
- `_Widget.cshtml` renders `.ffc-country-strip` with one `.ffc-country-chip` per country in the rollup. Each chip shows the name, the completed count and a "+N planned" note when planned units exist.
- Sort order: completed units descending, then name.
- Every chip links to `/ProjectOfficeReports/FFC/MapTableDetailed?countryId=…`. Chips carry `data-country-chip="{ISO3}"` so the script can link each chip to its map feature.
- The list is built from the same `Model.Countries` data as the markers; there is no extra API call.
- The header shows totals for installed units, delivered units awaiting installation, and completed units (with planned units as a note). It also has a **View full map** link to `/FFC/Map`, and a link to the detailed table sits below the strip.

## C. Dashboard vs full-page map
| Requirement | Status |
| --- | --- |
| Dashboard: pins, unified tooltip, country list, one tooltip at a time | Implemented |
| Full map page with the same pin and tooltip behaviour | Implemented (`ffc-map.js` + `ffc-map-init.js`). Popups offer "View projects in detailed table" |
| Full map page with year/category filters and a full-height side table | **Not implemented.** Tabular views are `MapTableDetailed` and `MapBoard` instead |

## Notes
- No inline scripts (CSP). Map behaviour lives in `wwwroot/js/widgets/ffc-simulator-map.js`.
- Widget and page contract tests: `ProjectManagement.Tests/DashboardFfcWidgetContractTests.cs`.
