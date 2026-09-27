# Reports Workspace, Workspace (Command and Project Officer) and Dashboard

## 1. Reports workspace

**Purpose.** The Reports workspace holds cross-module, FY-oriented management reports that can be exported to Word, PDF and Excel.

| Route | Page model | Policy |
|---|---|---|
| `/Projects/Reports` | `Pages/Projects/Reports/Index.cshtml.cs` (report catalogue and available FYs) | `ProjectOfficeReports.ViewArpp` |
| `/Projects/Reports/ArppFyUpdate` (`?handler=Word\|Pdf\|Excel&financialYearStart=`) | `ArppFyUpdate.cshtml.cs` | `ViewArpp` |
| `/Projects/Reports/FfcProjectsUpdate` (`?handler=Word\|Pdf\|Excel`, `SelectionMode`, `CountryYears`, `IncludeOverallStatus`) | `FfcProjectsUpdate.cshtml.cs` | `ViewArpp` |

`ViewArpp` covers Admin, HoD, Comdt, Project Office (both aliases), MCO and Project Officer. The FFC Projects Update report uses the ARPP view policy even though the FFC area pages are open to every authenticated user.

**Services**

- `Services/Reports/ArppFyProjectUpdate`:
  - `ArppFyProjectUpdateService` builds the report from the published ARPP position and live project facts.
  - `ArppFyProjectUpdateExportService` dispatches to `ArppFyProjectUpdateWordBuilder` (OpenXML), `ArppFyProjectUpdatePdfBuilder` (QuestPDF) and `ArppFyProjectUpdateExcelBuilder` (ClosedXML).
  - Presentation options: `IncludePresentStage` and `ListingDateMode`.
  - See [arpp.md](arpp.md) for the membership rules.
- `Services/Reports/FfcProjectsUpdate`:
  - Static builders: `FfcProjectsUpdateWordBuilder`, `FfcProjectsUpdatePdfBuilder`, `FfcProjectsUpdateExcelBuilder`.
  - Data comes from `IFfcQueryService.GetDetailedGroupsAsync`.
  - Export returns 400 when no selected country-year contains projects.
  - File name: `FFC_Projects_Update_{yyyyMMdd_HHmm IST}`.
- `Services/Reports/ProgressReview/ProgressReviewService` backs `/ProjectOfficeReports/ProgressReview`, which requires policy `ViewProgressReview`: Admin, HoD, Project Office (both aliases), Comdt.

**Shared report builders.** `Utilities/Reporting` is a shared library, not a module:

- Excel/PDF builders for Visits, Social Media, Training, ToT, Proliferation, IPR, Completed Projects Summary and the legacy-import template.
- `MarkdownPdfRenderer`.
- The Brochure and Compendium PDF pipeline; see [publications-and-compendiums.md](publications-and-compendiums.md).

Every builder produces a `byte[]` in memory and uses no temp files.

## 2. Workspace

| Route | Page model | Guard |
|---|---|---|
| `/Workspace` | `Pages/Workspace/Index.cshtml.cs` | `[Authorize]`. Users without the Comdt, HoD or Project Officer role are redirected to `/Dashboard` |
| `/Workspace/Conference/{officerUserId?}` | `Pages/Workspace/Conference.cshtml.cs` (handlers: `DirectionHistory`, `Add`, `CreateTask`, `CreateIdea`) | `Policies.ConferenceRemarks.Manage` (Comdt, HoD) |
| `/Workspace/BriefingDecks` | see [briefings-and-presentations.md](briefings-and-presentations.md) | `ProjectBriefingDecks.Manage` |

### Command lens

Comdt and HoD get the command lens by default. A dual-role user can choose `mode=project-officer`.

- Views: `officers` (default), `portfolio`, `adoption`, `usage-pattern`, `my-activity`.
- The data comes from `Services/Workspace/CommandWorkspaceService`, `OfficerWorkloadReadService` and the ERP usage query services.
- `OnPostSaveOfficerOrderAsync` (reorders officer cards) requires Comdt or HoD.

### Project Officer lens

- Views (`ProjectOfficerWorkspaceView`): overview, actions, conference, projects, tasks, ideas, follow-ups, documents, activity.
- Services:
  - `ProjectOfficerWorkspaceService`
  - `WorkspaceActionQueueBuilder` (unified operational queue)
  - `ProjectRecordHealthService`
  - `WorkspaceNudgeService`
  - `WorkspaceUpcomingEventQuery` (calendar preview)
  - `ProjectOfficerConferenceActionQuery` (directions the officer has not yet followed)
  - `OfficerConferenceReadService.GetForProjectOfficerAsync` (the officer's own conference view only)
- The documents view requires `DocRepo.View`, otherwise it returns `Forbid`. `DirectionHistory` requires the Project Officer role.

### Conference review (command)

- `OfficerConferenceReadService` builds the review from projects, ideas and action tasks using bounded batch queries.
- `ConferenceProjectScopeService` defines the project scope. It includes completed projects for `Conference:CompletedProjectRetentionDays` days (default 90, validated by `ConferenceOptionsValidator`).
- Writes:
  - `Services/ConferenceRemarks/ConferenceRemarkCommandService.AddAsync` writes a direction into the item's own remark stream: project remarks via `IRemarkService`, idea Conference comments via `IProjectIdeaCommandService`, or task collaboration. Text must be non-empty and at most 4,000 characters. The service also checks the Comdt/HoD role itself and throws `UnauthorizedAccessException` otherwise.
  - `ConferenceTaskCommandService` creates a normal Action Tracker task.
  - `ConferenceIdeaCommandService` creates a normal Project Idea.

## 3. Dashboard

- Route `/Dashboard` → `Pages/Dashboard/Index.cshtml.cs`. The folder has `AuthorizeFolder("/Dashboard")` and the page has `[Authorize]`.
- Each widget loads inside its own try/catch. A failure is logged as `"Dashboard widget failed: …"` and the widget renders empty.
- Widgets:
  - Notebook (top 5)
  - upcoming events and holidays (next 30 days; events capped at 15)
  - celebrations
  - My Projects: shown to Project Officer, HoD, Comdt and MCO; the empty-state message is shown only to Project Officer and HoD
  - Project Pulse
  - Ops Signals
  - Search Health
  - FFC simulator map
- Services: `Services/Dashboard/ProjectPulseService`, `OpsSignalsService`, `SearchHealthService`.
- View models and partials: `Areas/Dashboard/Components/{ProjectPulse,OpsSignals,FfcSimulatorMap,ProjectDue}`.

## Configuration

| Key | Used by |
|---|---|
| `Conference:CompletedProjectRetentionDays` | conference project scope |
| `ErpUsage:*` | command lens adoption/usage views (see [admin-usage-activities.md](admin-usage-activities.md)) |

There are no report-specific keys.
