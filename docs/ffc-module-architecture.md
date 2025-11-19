# FFC Proposals Module Architecture

## 1. Purpose and High-Level Responsibilities
The FFC Proposals area under `Areas/ProjectOfficeReports/FFC` lets authorised staff track project-unit delivery milestones, supporting dashboards, drill-down views, and exported reports. Key responsibilities include:

- Persisting per-country/year records, linked projects (with per-project quantities), and attachments.
- Surfacing milestone progress cards, filters, and search integration.
- Managing master data (countries, records, projects, files) under Admin/HoD roles.
- Publishing geospatial rollups for dashboards, maps, and tabular exports based on project-unit counts.
- Feeding other modules (global search, progress review reports, dashboards) with the same project-level data set.

## 2. Data Model & Persistence Layer
### 2.1 Entities
- **`FfcCountry`** - ISO-3166-based country with `IsActive` flag, timestamps, and navigation to records.
- **`FfcRecord`** - Primary milestone record keyed by `CountryId`+`Year`, holding IPA/GSL milestones, overall remarks, soft-delete flag, and relationships to `FfcCountry`, `FfcProject`, and `FfcAttachment`. Delivery/installation progress is derived from child `FfcProject` rows, while legacy record-level flags remain only for historical backfill.
- **`FfcProject`** - Per-record linked project stub capturing `Name`, optional remarks, optional link to a core `Project` entity for lifecycle rollups, `Quantity`, and per-project delivery/installation flags & dates.
- **`FfcAttachment`** - Metadata for PDF/photo uploads (path, MIME, checksum, caption, uploader) grouped by record, with `FfcAttachmentKind` describing the type.

### 2.2 Entity Framework Configuration
`ApplicationDbContext` exposes `DbSet`s for the four entities and configures table names, indexes, cascading rules, timestamps, and check constraints (dates require corresponding "Yes" flags, attachment size >=0). Projects/attachments cascade delete with their record; countries are restricted to protect history. Attachments store `Kind` via a value converter and track `UploadedAt`. Countries enforce unique names/ISO codes.

### 2.3 Storage & File Handling
`FfcAttachmentStorage` enforces Admin/HoD roles, allowed MIME types (PDF/JPEG/PNG/WEBP), max file size, malware scanning via `IFileSecurityValidator`, and persists the file into a configurable upload root before inserting the DB row. Deletion removes both row and disk file; storage paths can be absolute or relative based on `FfcAttachmentOptions`.

## 3. Security, Navigation & Routing
- Razor Pages under `/ProjectOfficeReports/FFC` are decorated with `[Authorize]`, with admin-specific pages (`Records/Manage`, `Countries/Manage`) restricted to the `Admin` or `HoD` roles, while listing pages require any authenticated user.
- Left-hand navigation includes a "FFC Proposals" item, wiring the `/ProjectOfficeReports/FFC/Index` page into the broader reports menu so eligible users can reach the module easily.
- `UrlBuilder` centralises deep links (record edit, attachment view) so services like global search can point users to the correct page with `editId` pre-selected.

## 4. Backend Features by Area
### 4.1 Shared List Model
`FfcRecordListPageModel` powers both the public index and admin manage page. It handles filter/query parsing, milestone filter enums, pagination, dynamic sorting, and provides helper methods to build consistent route dictionaries and toggle sort icons. Queries always include countries, linked projects (with optional linked project entity), and attachments; filtering occurs through scalar filters, milestone filters, and text search. Results are paged at 10 items per request with `AsSplitQuery` for EF efficiency.

### 4.2 Public Index Page
`IndexModel` inherits the base list page to expose the read-only experience. On `OnGetAsync` it:
1. Detects whether the user can manage records to show management actions.
2. Loads year/country/milestone options from active data.
3. Loads records (filtering out `IsDeleted`).
4. Calls `LoadProjectRollupMetadataAsync`, which pulls linked project lifecycle statuses, stage history, and latest external remarks to decorate the cards with live project context.
The Razor partial `_FfcRecordCard` renders each record's milestones, remarks, top three linked projects (with lifecycle badges and stage summaries), attachment counts, and management buttons. It now uses consistent counters (“Projects”, “Project units”, “Attachments”) with tooltips and neutral zero-state messaging per record. It also creates a modal gallery for attachments with inline preview links.

### 4.3 Record CRUD (Admin)
The admin `Records/Manage` page combines the list view with a form sidebar. It binds an `InputModel` covering country/year, IPA, and GSL fields (delivery/installation are now summarised from child projects), tracks `RowVersion` for concurrency, and validates business rules (active country, year range, milestone date dependencies). Create/update handlers map between form and entity, save changes, and emit audit logs via `IAuditService`. Concurrency conflicts reload the latest data and display a friendly error. Listing filters persist via `BuildRoute` so returning to the same filter context is seamless.

### 4.4 Linked Projects Management
`Records/Projects/Manage` provides CRUD over the child `FfcProject` rows. It enforces Admin/HoD roles, ensures the parent record exists, validates required names and quantities, captures delivery/installation flags with optional dates, optionally links to an existing project, and writes audit logs for create/update/delete. The page also preloads selectable projects and displays the current list (including linked `Project` metadata and per-project quantities).

### 4.5 Attachment Management & Delivery
`Records/Attachments/Upload` lists attachments, enforces Admin/HoD roles, and uses `IFfcAttachmentStorage` to persist validated uploads. Successful PDF uploads also flow into the document repository (`IDocRepoIngestionService`) for cross-module search. Users can delete attachments, which removes both the DB row and the file via the storage service. The `/FFC/Attachments/View` page streams files inline with HTTP range support while ensuring the parent record still exists and isn't deleted.

### 4.6 Country Master Data
`Countries/Manage` is a paged, sortable list of `FfcCountry` records with quick search and toggle actions. Only Admin/HoD can change activation state, and changes write audit logs. Sorting supports name/ISO/status columns, and filter state persists through helper route builders.

### 4.7 Map, Tables, and Board Endpoints
All geographic views rely on `FfcCountryRollupDataSource`, which aggregates per-project quantities per active country by grouping `FfcProject` rows (linked or stand-alone). It classifies each row as Installed/Delivered/Planned based on per-project flags, multiplies counts by `Quantity`, and returns ISO3-coded DTOs that represent total project units rather than project counts.

- `/FFC/Map` serves the Leaflet map and exposes a JSON handler returning the rollup DTOs. Popups show ISO3 codes, status totals, and deep links into the detailed table filtered by country/year.
- `/FFC/MapTableDetailed` expands to project-level rows by combining `FfcProject`, linked `Project` snapshots (names + stage history), per-project quantities, and latest external remarks. It buckets rows (Installed/Delivered/Planned), truncates remarks for display, exposes both JSON and XLSX export handlers, and renders grouped country-year headers server-side.
- `/FFC/MapBoard` is a fullscreen board built entirely from the rollup JSON. It supports server-provided sort defaults, “Sort by” UI (total units or country name), screenshot/full-width toggles, and PNG export.

> **Note:** The former `/FFC/MapTable` country summary view now redirects to the board because the map, detailed table, and board cover the required use cases.

### 4.8 Dashboard Widget Integration
The home dashboard loads `FfcCountryRollupDataSource`, filters for countries with any completed work, and builds a `FfcSimulatorMapVm` summarising totals. The `_Widget` partial renders totals, the interactive mini-map, and the top countries strip, delegating front-end behaviour to `wwwroot/js/widgets/ffc-simulator-map.js`.

### 4.9 Global Search & Reports
- `GlobalFfcSearchService` queries both records and PDF attachments using `ILike`, returning `GlobalSearchHit`s that link into record management or attachment viewers via `IUrlBuilder`. This makes FFC content discoverable from the global search box.
- The progress review report service pulls all non-deleted records, emits milestone rows per lifecycle event, and merges them with other proliferation/misc sections for the leadership review deck.

## 5. Front-End Views & Components
- **Index view** (`Areas/ProjectOfficeReports/Pages/FFC/Index.cshtml`) composes toolbar actions (map, detailed table, board, management pages) and renders `ffc-record-card` tiles using `_FfcRecordCard` plus `ViewData` dictionaries for rollup metadata.
- **Manage pages** reuse `_RecordForm` for the edit/create experience; the offcanvas/table layout mirrors the standard admin pattern.
- **Country board** replaces the legacy country table and emphasises screenshot-friendly cards with consistent toolbar controls.
- **Attachment modal** inside `_FfcRecordCard` displays both images and document lists, reusing the viewer endpoint for inline previews.

## 6. Client-Side Modules
### 6.1 Leaflet Map (`ffc-map.js` + `ffc-map-init.js`)
`ffc-map.js` defines a `window.FfcMap` namespace that:
- Builds color scales and popups, wires zoom controls, and caches country aggregates by ISO3.
- Fetches GeoJSON + rollup data, paints the map with Leaflet layers, attaches tooltips, and links popups to the detailed table with country/year parameters.
- Remembers "full-width" UI preferences in `localStorage` and exposes an `init` entrypoint used by `ffc-map-init.js` to bootstrap the map based on data attributes rendered by the Razor view.

### 6.2 Map Board (`ffc-map-board.js`)
Fetches `/FFC/Map?handler=Data` (or a custom data URL provided by the Razor view), renders responsive cards sorted by total project units, and offers controls for full-width mode, screenshot-friendly mode, PNG export via `html2canvas`, and "Sort by" dropdown selections (Total units or Country A→Z). Buttons toggle CSS classes for layout adjustments, and toolbar chrome hides automatically during screenshot mode.

### 6.3 Detailed Table (`ffc-map-table-detailed.js`)
The detailed table script fetches its JSON handler via the data attributes rendered on the Razor page, sorts and renders grouped rows (country-year headers, project rows), and attaches client-side formatting. It prints a friendly "No projects" row when data is unavailable, formats remarks placeholders, and provides Excel export alignment with the visible columns.

### 6.4 Dashboard Widget Script (`ffc-simulator-map.js`)
Uses Leaflet to plot the condensed world map inside the dashboard widget. It parses serialized country data, builds proportional pins, colours features based on completion ratios, and exposes chip interactions + tooltips for accessibility. GeoJSON bounds can be provided to focus on specific regions.

## 7. Downstream Connections
- **Projects module** - Linked projects surface lifecycle status, stages, and external remarks, so changing stage schemas or remark types will affect rollup summaries built in `IndexModel` and `MapTableDetailed`.
- **Document repository** - PDF uploads trigger ingestion, meaning file naming/content-type rules must remain compatible with `IDocRepoIngestionService`.
- **Global search** - Any schema change (new fields) should consider extending `GlobalFfcSearchService` so the new data stays discoverable.
- **Dashboards & reports** - `FfcCountryRollupDataSource` feeds the dashboard widget and all map/table endpoints, so altering its logic impacts every aggregate view simultaneously. Likewise, the progress review service reads raw records for printable reports.

## 8. Extension & Maintenance Guidance
1. **Preserve shared abstractions** - Extend `FfcRecordListPageModel` when introducing new list-based pages so paging/filtering stays consistent. Add new filters via extra query-bound properties and update `HasActiveFilters` and `BuildRoute` accordingly.
2. **Respect audit/logging pattern** - CRUD pages log to `IAuditService`. When adding new mutations (e.g., batch operations), emit before/after dictionaries similar to existing code to keep compliance trails intact.
3. **Coordinate with map data source** - Any additional completion bucket (e.g., "Awaiting funding") should be computed in `FfcCountryRollupDataSource` and then consumed by widget, map, tables, and board in lock-step to avoid inconsistent counts.
4. **Client-side CSP compliance** - All scripts live under `wwwroot/js/...`; Razor views only reference bundled files (no inline `<script>` blocks), maintaining CSP compliance per the repository conventions.
5. **Testing & Authorisation** - Existing integration tests (see `ProjectManagement.Tests/ProjectOfficeReports/...`) assert that non-admins can't access manage pages; mirror those patterns when adding routes to avoid regressions.

With this overview, a new developer should be able to trace a data point (record -> linked project -> map aggregate -> dashboard widget), understand which layers must be updated for new milestones or UI, and extend the module without breaking connected features.
