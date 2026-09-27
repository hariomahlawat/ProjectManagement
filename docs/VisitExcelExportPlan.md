# Visit export (Excel and PDF)

**Status: Implemented.** This was originally a plan. It now describes the shipped behaviour, checked against the code on 2026-09-27. Where the shipped behaviour differs from the original plan, the difference is noted.

## Behaviour
- Page: `/ProjectOfficeReports/Visits` (`Areas/ProjectOfficeReports/Pages/Visits/Index.cshtml(.cs)`). An **Export** button opens `#exportVisitsModal`. The button is rendered only when the current page has at least one visit (`Model.Items.Count > 0`).
- Handlers: `IndexModel.OnPostExportAsync` (Excel) and `IndexModel.OnPostExportPdfAsync` (PDF). Both reuse the current filters (visit type, from/to date, free-text `Q`) through `BuildQuery()` and export the **full filtered set**, not only the visible page.
- Authorisation: any authenticated user. The Visits folder uses the `ViewVisits` policy, which requires only an authenticated user. **Deviation from the plan:** export is not limited to `CanManage` users.
- Validation errors (for example an invalid date range) are added to `ModelState`, shown via `TempData["ToastError"]`, and the page is redisplayed.

## Components
| Component | Role |
| --- | --- |
| `VisitExportRequest` / `VisitExportResult` / `VisitExportFile` | Request DTO (type, start, end, query, requesting user id) and result DTO. |
| `IVisitExportService` / `VisitExportService` (`Areas/ProjectOfficeReports/Application`) | Validates the request, calls `VisitService.ExportAsync`, builds the file and writes the `Audit.Events.VisitExported` audit event. `ExportPdfAsync` also loads each visit's cover photo (`md` derivative) through `IVisitPhotoService.OpenAsync`. |
| `VisitService.ExportAsync` | Shared filter logic with search; returns export rows. |
| `VisitExcelWorkbookBuilder` (`Utilities/Reporting`, ClosedXML) | "Visits" worksheet with the columns S.No., Visit type, Date of visit, Visitor, Strength, Photo count, Has cover photo, Remarks, plus a metadata footer (export generated, from, to). |
| `VisitPdfReportBuilder` (`Utilities/Reporting`) | PDF report with a section per visit, including the cover photo when one exists. |
| DI (`Program.cs`) | Builders are registered as singletons and `IVisitExportService` as scoped. |

## File naming
`visits-{range}-{yyyyMMddTHHmmssZ}.xlsx` or `.pdf`, where `{range}` comes from the filter dates (`VisitExportService.BuildFileName` / `BuildPdfFileName`).

## Open items from the original plan
- No maximum-export-size setting exists; exports run synchronously in the request.
- Quick range presets (last 30/90 days) were not implemented.
