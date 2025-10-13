# Visit Excel Export Plan

## Goals
- Allow authorised users to export visit records to a structured Excel workbook without breaking existing visit management flows.
- Provide a date-range selector (and optional filters that already exist) to scope the export.
- Deliver a readable worksheet with serial numbers and key visit metadata (type, date, visitor, strength, remarks, cover photo status, etc.).
- Follow existing modular patterns in the Project Office Reports area so the implementation is maintainable and testable.

## Implementation Summary
- Added `VisitExportService` with an accompanying `VisitExportRequest`/`VisitExportResult` workflow that validates filters, relies on `VisitService.ExportAsync`, emits audit events, and streams Excel bytes back to Razor pages. 【F:Areas/ProjectOfficeReports/Application/VisitExportService.cs†L1-L86】
- Extended `VisitService` with reusable filtering and a dedicated export projection so both search and export paths stay in sync. 【F:Areas/ProjectOfficeReports/Application/VisitService.cs†L26-L124】
- Introduced `VisitExcelWorkbookBuilder` under `Utilities/Reporting` to craft offline-ready workbooks (header styling, wrapped remarks, metadata footer) using ClosedXML. 【F:Utilities/Reporting/VisitExcelWorkbookBuilder.cs†L1-L94】
- Wired the visits index page with an “Export visits” modal that reuses filters, posts to the new handler, and returns a spreadsheet without inline scripts. 【F:Areas/ProjectOfficeReports/Pages/Visits/Index.cshtml†L83-L155】
- Registered supporting services in DI and documented behaviour for administrators in ProjectOfficeReports directions. 【F:Program.cs†L21-L238】【F:docs/ProjectOfficeReports_Directions.md†L79-L97】

## Current Visit Experience
- Visits are managed under `/ProjectOfficeReports/Visits` using Razor Pages (`Areas/ProjectOfficeReports/Pages/Visits`). The index page already exposes filtering by type, date range, and free-text search. 【F:Areas/ProjectOfficeReports/Pages/Visits/Index.cshtml†L1-L132】
- Data access is handled through `VisitService`, which exposes a `SearchAsync` method returning `VisitListItem` projections. 【F:Areas/ProjectOfficeReports/Application/VisitService.cs†L26-L74】
- The UI includes a hero card with "Record a visit" and "Preview latest visit" actions that should remain unchanged. 【F:Areas/ProjectOfficeReports/Pages/Visits/Index.cshtml†L39-L86】
- There is no existing infrastructure for exporting data or generating spreadsheets.

## Proposed Solution Overview
1. **Backend export service** – build a dedicated service class that consumes existing query services to fetch visits and renders them to Excel.
2. **Excel rendering utility** – encapsulate spreadsheet generation in a helper so future exports (e.g., visit types) can reuse it.
3. **Razor Page endpoint** – add a handler to the visits index page that accepts date range inputs, invokes the export service, and streams the resulting file.
4. **UI enhancements** – introduce an "Export visits" control next to the existing hero actions. Clicking it opens a modal with date range selectors (pre-populated from current filters) and an "Export" submit button.
5. **Authorization & validation** – reuse the same permissions that gate `Model.CanManage` for the export action and validate user-provided dates before triggering the export.

## Backend Plan

### 1. Define export request model
- Create a new record `VisitExportRequest` (in `Areas/ProjectOfficeReports/Application`) that captures:
  - Optional `VisitTypeId`
  - Optional `StartDate` / `EndDate`
  - Optional free-text query (to match the search box)
  - Requesting user identifier (for auditing)
- This mirrors `VisitQueryOptions` so we can reuse the same filtering logic.

### 2. Extend `VisitService`
- Add a method `Task<IReadOnlyList<VisitExportRow>> ExportAsync(VisitExportRequest request, CancellationToken token)`.
- Internally, reuse `SearchAsync` (or factor shared query-building logic into a private method) to avoid duplicating filters.
- Introduce a new projection type `VisitExportRow` containing all fields needed for Excel (serial number can be generated during rendering).
- Ensure the method uses `AsNoTracking()` and includes visit type data to minimise memory usage.

### 3. Spreadsheet generation helper
- Add a helper class under `Utilities/Reporting` (new namespace if needed) responsible for turning a list of `VisitExportRow` into a byte array.
- Use a well-supported Excel library:
  - **Preferred**: `ClosedXML` due to its developer-friendly API and permissive license.
  - Add `ClosedXML` to the project as a NuGet dependency.
- Helper responsibilities:
  - Create a worksheet named e.g. "Visits".
  - Write a header row with styling (bold, background shading).
  - Iterate through rows, writing serial numbers, visit type, date, visitor, strength, photos count, remarks, created/modified timestamps, and cover photo flag.
  - Auto-fit columns (with width caps) to improve readability.
  - Return a `MemoryStream`/byte array for the Razor Page handler to stream.

### 4. Export service abstraction
- Define an interface `IVisitExportService` registered in DI. Implementation orchestrates:
  - Validating the request (e.g., start date <= end date).
  - Calling `VisitService` to fetch data.
  - Passing rows to the spreadsheet helper.
  - Recording an audit event (e.g., `VisitExported`) for traceability.
- Keep business logic out of the Razor Page to maintain separation of concerns.

### 5. Auditing and logging
- Add a new audit event under `VisitService.Audit.Events` or a related static helper to log exports with the user ID, optional filters, and count of rows exported.
- Use structured logging (ILogger) inside the export service to capture success/failure.

## Razor Page Changes

### 1. Handler method
- In `Visits/Index.cshtml.cs`, add an `OnPostExportAsync` handler that:
  - Binds `From`, `To`, `VisitTypeId`, `Q` fields (reusing existing properties).
  - Calls `IVisitExportService.ExportAsync` with the parsed values and the current user ID.
  - Returns `FileContentResult` with `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` content type and a filename like `visits-2024-04-01_to_2024-04-30.xlsx`.
  - Handles validation errors by adding `ModelState` errors and redisplaying the page with toasts.

### 2. UI entry point
- In the hero card action stack, add an "Export visits" button only when `Model.Items.Any()` (avoid empty export) and the user has permission.
- Trigger a Bootstrap modal (or existing modal component) containing:
  - Two date inputs (defaulted to currently selected filters or blank).
  - Optional quick range presets (last 30/90 days) if desired.
  - Submit button posting to `OnPostExportAsync`.
  - Include anti-forgery token.
- Provide inline help text about Excel formatting.

### 3. Accessibility & responsiveness
- Ensure button uses accessible labels (`aria-label`) and modal is keyboard friendly.
- Keep styling consistent with `visit-hero__actions` and existing SCSS partials.

## Testing Strategy
- **Unit tests** for the export service verifying:
  - Date validation logic (invalid ranges fail fast).
  - Empty result sets still produce a valid Excel file with headers.
  - Correct audit event emission with expected payload.
- **Integration tests** for Razor Page handler (using existing pattern in tests project) ensuring:
  - Authorized users receive a file with the correct content type.
  - Filter parameters are honoured (mock data with specific dates/types).
- **Manual QA**
  - Export using overlapping filters, verifying resulting spreadsheet in Excel/LibreOffice.
  - Large dataset smoke test to confirm performance and memory usage.

## Deployment & Rollout Considerations
- Adding ClosedXML increases deployment size; confirm it is acceptable and supported by hosting environment (.NET 8 compatible).
- Ensure any new configuration (e.g., maximum export size) is captured in app settings with sensible defaults.
- Document the feature in `docs/ProjectOfficeReports_Directions.md`, including instructions for administrators.

## Timeline & Incremental Delivery
1. Introduce export data structures and service (backend only) with unit tests.
2. Wire Razor Page handler and return a basic Excel file (no UI yet) to validate plumbing.
3. Add modal UI and styling, ensure no regressions in existing hero actions.
4. Enhance formatting (auto-fit, remarks wrapping) and final QA.

Delivering incrementally allows validation at each layer and reduces the risk of disrupting the existing visit management experience.
