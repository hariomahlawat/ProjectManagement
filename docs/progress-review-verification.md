# Progress Review implementation verification

Status of the Progress Review pipeline, checked against the code on 2026-09-27.

## Access and page
- Page: `Areas/ProjectOfficeReports/Pages/ProgressReview/Index.cshtml(.cs)`, protected by `[Authorize(Policy = ProjectOfficeReportsPolicies.ViewProgressReview)]`. The policy allows Admin, HoD, Project Office (`Project Office`/`ProjectOffice`) and Comdt (`ProjectOfficeReportsPolicies.ProgressReviewViewerRoles`).
- `IndexModel.OnGetAsync` is the only handler. Export is done in the browser: `wwwroot/js/pages/project-office-reports/progress-review.js` sets default from/to dates and wires `[data-action="export-pdf"]` and `[data-action="print"]` to `window.print()`. There is no copy-link button and no inline script.

## Interface and records
`Services/Reports/ProgressReview/IProgressReviewService.cs` defines `IProgressReviewService` and `ProgressReviewVm(Range, Projects, Visits, SocialMedia, Tot, Ipr, Training, Proliferation, Ffc, FfcDetailedIncompleteGroups, Misc, Totals)`, plus the nested section records. `ProjectSectionVm` contains `FrontRunners`, `WorkInProgress` and `NonMovers`, among others.

## Service loaders (`Services/Reports/ProgressReview/ProgressReviewService.cs`)
- Projects: `LoadStageChangeRowsAsync`, `LoadFrontRunnerProjectsAsync`, `LoadProjectRemarksOnlyAsync`, `LoadProjectNonMoversAsync`, `LoadResolvedProjectStagesAsync` (feeds the movement board).
- Visits and social media: `LoadVisitsAsync`, `LoadSocialMediaAsync`.
- ToT: `LoadTotStageChanges`, which uses stage-change rows for stage code `TOT` rather than `ProjectTot`, and `LoadTotRemarksAsync`.
- IPR: `LoadIprStatusChangesAsync`, `LoadIprRemarksAsync`.
- Training: `LoadTrainingBlockAsync`.
- Proliferation: `LoadProliferationAsync`.
- FFC: `LoadFfcAsync`, `AppendFfcRow`.
- Miscellaneous activities: `LoadMiscActivitiesAsync`.

## Known inconsistency
`LoadTotRemarksAsync` keeps only remarks whose project has `LifecycleStatus == Active`. ToT is applicable only to **Completed** projects (`ProjectTotApplicabilityPolicy`), and every ToT remark path (tracker submit/decide, `OverviewModel` ToT remark handler) requires eligibility. As a result, ToT remarks recorded through the supported flows are normally excluded from the review. The query also does not exclude Repeat Build projects.
