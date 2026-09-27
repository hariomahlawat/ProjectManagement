# Razor Pages catalogue

Catalogue of every routable Razor Page in PRISM, grouped by feature area. For each page it gives the route, what the page is for, its main handlers, and the authorization that actually gates it. Where this document and the code disagree, the code wins.

## How authorization is applied

- **There is no global fallback policy.** `Program.cs` calls `AddAuthorization` without a `FallbackPolicy`, so a page is only protected if it has an `[Authorize]` attribute (on the page model, or `@attribute [Authorize]` in the `.cshtml`) or is covered by a Razor Pages convention. Every page has an explicit `[Authorize...]` attribute except the anonymous ones listed below.
- **Conventions** (the `AddRazorPages` options in `Program.cs`):
  - `AuthorizeFolder("/Dashboard")` and `AuthorizeFolder("/Projects/Publications")` require an authenticated user.
  - `AuthorizeAreaFolder("Admin", "/")` requires an authenticated user for the whole Admin area. Each page then adds its own capability policy.
  - `AuthorizeAreaFolder("ProjectOfficeReports", "/Visits", ViewVisits)` and `(..., "/Training", ViewTrainingTracker)`.
  - `AuthorizeAreaPage` puts `ManageVisits` on `/Visits/New` and `/Visits/Edit`, and `ManageSocialMediaEvents` on `/SocialMedia/Create`, `/SocialMedia/Edit` and `/SocialMedia/Delete`.
  - `AddPageRoute` adds `/ProjectOfficeReports/Patent` and `/ProjectOfficeReports/Patent/Manage` as aliases for the IPR pages.
  - `AllowAnonymousToPage("/Index")`, `AllowAnonymousToPage("/Privacy")` (no `/Privacy` page exists) and `AllowAnonymousToAreaPage("Identity", "/Account/Login")`.
- **Anonymous pages**: `/` (`Pages/Index`, public landing page; signed-in users are redirected by `DefaultLandingPageResolver`), `/Identity/Account/Login`, `/Identity/Account/AccessDenied`, `/Error`, and `/Developer`.
- **MVC filter**: `EnforcePasswordChangeFilter` is added globally, so a user flagged `MustChangePassword` is sent to Change Password first.
- **Antiforgery**: Razor Pages validate the antiforgery token on every POST by default. Only `Pages/Error` uses `[IgnoreAntiforgeryToken]`.
- **Where policies are defined**: `Configuration/Policies.cs` (`Policies.*`), `Configuration/AdminPolicies.cs` with `Services/Admin/AdminCapabilityCatalog.cs` (Admin capabilities), and `Areas/ProjectOfficeReports/Application/ProjectOfficeReportsPolicies.cs`. Role names are in `Configuration/RoleNames.cs`.
- **Page access vs action access**: many pages are open to any signed-in user but check permissions inside each handler (for example with `ProjectAccessGuard`, `ActivityAuthorizationPolicy`, `ProjectIdeaPermissionService`, `ProjectOfficeReportsPolicies.CanManageFfc`, or `IAuthorizationService`). Those pages are marked "Authenticated (+ handler checks)".

### Policy → role quick reference

| Policy | Roles |
| --- | --- |
| `Project.Create` | Admin, HoD |
| `ERP.Usage.View` | Admin, Comdt, HoD |
| `Calendar.ManageEvents` | Admin, HoD, TA, Comdt, MCO, Project Officer, Project Office |
| `Calendar.ManageCelebrations` / `ManageBirthdays` / `ManageAnniversaries` | Admin, TA, Main Office clerk |
| `Checklist.Edit` / `Checklist.PurposeEdit` | MCO, HoD / Admin, HoD |
| `ActionTracker.Access` | Comdt, HoD, Project Officer, MCO, TA, ITO (Admin is **not** included) |
| `ProjectBriefingDecks.Manage`, `ConferenceRemarks.Manage` | Comdt, HoD |
| `IndustryPartners.View` / `Contact.Add` | Any authenticated user |
| `IndustryPartners.Create` | Admin, HoD, Comdt, Project Officer, Project Office, MCO, TA, ITO |
| `IndustryPartners.EditAny` / `Contact.ManageAny` / `Delete` | Admin, HoD, Comdt / Admin, HoD, Comdt / Admin, HoD |
| `DocRepo.View` | Any authenticated user |
| `DocRepo.Upload`, `DocRepo.SoftDelete` | Project Office, Main Office clerk, MC Cell clerk, IT Cell clerk, Admin, HoD |
| `DocRepo.EditMetadata` | Admin, TA, ITO, MCO, HoD |
| `DocRepo.DeleteApprove` | Admin, HoD |
| `DocRepo.ManageCategories`, `DocRepo.Purge` | Admin |
| `Ipr.View` / `Ipr.Edit` | Any authenticated user / Admin, HoD, Project Office |
| Shared publication presets (`Policies.Publications.CanManageSharedPublications`) | Comdt, HoD, ITO |
| Admin capabilities: `Admin.Access`, `Users.Manage`, `AccessGovernance.View`, `Security.View`, `Logs.View`, `Recovery.Manage`, `MasterData.Manage`, `MasterData.Integrity.Manage`, `Ingestion.Manage` | Admin |
| Admin capabilities: `ActivityTypes.Manage`, `Holidays.Manage`, `Media.*` | Admin, HoD |
| PO Reports: `ViewVisits`, `ViewTotTracker`, `ViewProliferationTracker` | Any authenticated user |
| PO Reports: `ManageVisits`, `ManageSocialMediaEvents`, `SubmitProliferationTracker`, `ManageProliferationPreferences`, `ManageArpp`, `ManageTrainingTracker` | Admin, HoD, Project Office |
| PO Reports: `ManageTotTracker` | Admin, HoD, Project Office, Project Officer |
| PO Reports: `ApproveTotTracker`, `ApproveProliferationTracker`, `ApproveTrainingTracker`, `UnlockArpp` | Admin, HoD |
| PO Reports: `ViewTrainingTracker` | Admin, HoD, Project Office, Project Officer, Comdt, MCO, TA, Main Office clerk |
| PO Reports: `ViewProgressReview` | Admin, HoD, Project Office, Comdt |
| PO Reports: `ViewArpp` | Admin, HoD, Comdt, Project Office, MCO, Project Officer |
| PO Reports: `VerifyArpp` | Admin, HoD, Comdt |
| PO Reports: `ManageFfc` / `InlineEditFfc` | Admin, HoD, Comdt, ITO / Admin, HoD, Comdt |

"Project Office" also matches the legacy alias `ProjectOffice`, and "Main Office clerk" (`Main_Office_Clerk`) also matches `Main Office`, wherever the role arrays list both.

---

## Public, identity and shell

| Route | Purpose | Main handlers | Access |
| --- | --- | --- | --- |
| `/` (`Pages/Index`) | Public landing page. Signed-in users are redirected to `/Dashboard` (Comdt/HoD, others) or `/Workspace` (Project Officer). | `OnGetAsync` | Anonymous |
| `/Developer` | Credits and contact card with the app version (`App:Version`). Linked from the layout footer as "System information". | `OnGet` | Anonymous (no attribute) |
| `/Error` | Error page. | `OnGet` | Anonymous, `[IgnoreAntiforgeryToken]` |
| `/Identity/Account/Login` | Username/password sign-in. Honours only local `returnUrl` values and otherwise uses the role landing page. | `OnPostAsync` | Anonymous |
| `/Identity/Account/Logout` | Sign-out confirmation. Signing out happens on POST only. | `OnPostAsync` | No attribute, `[AutoValidateAntiforgeryToken]` |
| `/Identity/Account/AccessDenied` | 403 page. | `OnGet` | Anonymous |
| `/Identity/Account/Manage` | Account settings: choose a Photos portrait or initials as your avatar, or report a wrong photo identity. | `OnPostUsePhotosPortraitAsync`, `OnPostUseInitialsAsync`, `OnPostReportPhotoIdentityAsync` | Authenticated |
| `/Identity/Account/Manage/ChangePassword` | Change password. Clears `MustChangePassword` and redirects to the role landing page. | `OnPostAsync` | Authenticated |
| `/Common/Search` | Global search, with facets, suggestions and click telemetry. | `OnGetFacetsAsync`, `OnGetSuggestionsAsync`, `OnPostClickAsync` | Authenticated |

**Layout and navigation** (`Pages/Shared/_Layout.cshtml`):
- **Top tabs**, shown to every signed-in user: Dashboard, My Workspace, Calendar, Notebook, Photos, Projects (`/Projects/Ongoing`), FFC (`/ProjectOfficeReports/FFC/MapTableDetailed`), Documents (with the AOTS unread badge) and Search.
- **Navigation drawer**: `NavigationDrawerViewComponent` renders `Services/Navigation/RoleBasedNavigationProvider`. It trims each item by `RequiredRoles` and/or `AuthorizationPolicy`, and adds the Administration branch (`AdminNavigationCatalog.BuildAdminPanel`) for Admins only. HoDs who are not Admins get only the "Activity types" admin item.
- **Project module sub-navigation**: `ProjectModuleNavDefinition`, rendered by `ModuleSubNavViewComponent`.
- **Admin sidebar**: `AdminSidebarViewComponent`.
- **Other view components**: `NotificationBell`, `PendingApprovalsBadge`, `AotsUnreadBadge`, `ProjectTotCommandCard`, and `TrainingApprovalsBadge` (in the ProjectOfficeReports area).

## Dashboard and workspace

| Route | Purpose | Main handlers | Access |
| --- | --- | --- | --- |
| `/Dashboard` | Home page: notebook/to-do widget, upcoming events, my projects, Project Pulse, ops signals, FFC map, search health, and activity and idea summaries. | `OnGetAsync` | Authenticated (folder convention + `[Authorize]`) |
| `/Workspace` | Role workspace. **Command mode** (Comdt/HoD) has these views: officers, portfolio, adoption, usage-pattern, my-activity. **Project Officer mode** has these views: overview, actions, projects, tasks, ideas, conference, follow-ups, documents, activity. Users with none of those roles are redirected to `/Dashboard`. | `OnGetDirectionHistoryAsync`, `OnPostSaveOfficerOrderAsync` (Comdt/HoD only) | Authenticated (+ handler checks) |
| `/Workspace/Conference/{officerUserId?}` | Officer conference review: record directions, and turn them into tasks or ideas. | `OnPostAddAsync`, `OnPostCreateTaskAsync`, `OnPostCreateIdeaAsync` | `ConferenceRemarks.Manage` |
| `/Workspace/BriefingDecks/{deckId?}` | Project briefing deck builder (decks, institutional profile, role charter, FFC footprint, extra slides, project membership and order, export). | `OnPostCreateAsync`, `OnPostDuplicateAsync`, `OnPostDeleteAsync`, `OnPostSave*`, `OnPostReorder*` and others | `ProjectBriefingDecks.Manage` |
| `/Tasks` | Personal to-do list (tabs: all, today, upcoming, completed). | `OnPostAdd/Toggle/Undo/Edit/Snooze/Reorder/Pin/Delete/ClearCompleted`, bulk done/delete/pin | Authenticated (items are scoped to their owner) |
| `/Notebook`, `/Notebook/Edit/{id?}` | Personal notebook with reminders and the shared conference digest (Comdt/HoD view). Writes go through `api/notebook/*` (`Controllers/Api/NotebookController`). | `OnGet*` only | Authenticated |
| `/Notifications` | Notification centre. Reads and updates go through `/api/notifications` and the SignalR hub. | `OnGetAsync` | Authenticated |

## Calendar and celebrations

| Route | Purpose | Main handlers | Access |
| --- | --- | --- | --- |
| `/Calendar` | FullCalendar view of events, holidays and celebrations. Event create, edit and delete go through the `/calendar/events` minimal APIs in `Program.cs`, which require `Calendar.ManageEvents`. The page computes `CanEdit`, `CanManageBirthdays` and `CanManageAnniversaries`. | `OnGetAsync` | Authenticated |
| `/Celebrations`, `/Celebrations/Edit/{id?}` | Birthday and anniversary registry: list, soft-delete, create and edit. Linked from Calendar for managers. | `OnPostDeleteAsync`, `OnPostAsync` | `Calendar.ManageCelebrations` (the whole page, not only edits) |
| `/Settings/Holidays` (+ `Create`, `Edit/{id}`, `Delete/{id}`, `Observe/{id}`, `WithdrawObservance/{id}`) | Gazetted and restricted holidays, and declaring or withdrawing office observance of a restricted holiday. | `OnPostAsync` on each sub-page | `Admin.Holidays.Manage` (Admin, HoD) |

## Projects

| Route | Purpose | Main handlers | Access |
| --- | --- | --- | --- |
| `/Projects` | Projects repository with filters and a live search endpoint. | `OnGetLiveAsync` | Authenticated |
| `/Projects/Create` | Register a project (with a name check). | `OnGetCheckNameAsync`, `OnPostAsync` | `Project.Create` |
| `/Projects/Overview/{id}` | Project command page: stages, procurement, timeline, content (brief, capabilities, specifications, description), JDPs, ToT, proliferation, and lifecycle actions. The page model is split across the partial files `Overview.Content.cs`, `Overview.MultiJdp.cs` and `Overview.Tot.cs`. | `OnPostCompleteAsync`, `OnPostEndorseAsync`, `OnPostCancelAsync`, `OnPostReactivateAsync` (Admin/HoD), `OnPostProliferationAsync`, `OnPostSaveProject*` (Admin/HoD), `OnPostAddProjectJdpAsync`/`OnPostRemoveProjectJdpAsync`, `OnPostTotAsync`/`OnPostTotRemarkAsync` | Authenticated (+ handler checks) |
| `/Projects/AssignRoles/{id}` | Change the HoD/PO assignment. | `OnPostAsync` | Admin, HoD |
| `/Projects/Meta/Request/{id}` · `Meta/Edit/{id}` · `Meta/Decide/{id}` | PO requests a change to project details · Admin/HoD edits directly · Admin/HoD decides a request. | `OnPostAsync` (Edit also `OnPostPreview`) | Project Officer · Admin, HoD · Admin, HoD |
| `/Projects/Procurement/Edit/{id}` | Procurement facts editor (posted from the overview panel). | `OnPostAsync` | Admin, HoD, Project Officer |
| `/Projects/Stages/RequestChange` · `DecideChange` · `ApplyChange` · `BackfillApply` | Stage update proposals (POST only; GET returns 404) · decision · direct apply · backfill. | `OnPostAsync` | Project Officer · Admin, HoD · **HoD only** · Admin, HoD, Project Officer |
| `/Projects/Timeline/EditPlan/{id}` · `EditActuals/{id}` · `Review/{id}` · `Historical/{id}` | Draft plan (save, submit, validate, delete draft) · record actual dates · approve or reject a plan · enter historical dates. | `OnPostAsync`, `OnGetValidateAsync`, `OnPostDeleteDraftAsync` | Admin, HoD, Project Officer · same · Admin, HoD · Admin, HoD |
| `/Projects/{id}/Documents` · `/Projects/Documents/Preview` | Project document library and inline preview. | `OnGetAsync` | Authenticated (+ project access guard) |
| `/Projects/Documents/UploadRequest` · `ReplaceRequest` · `DeleteRequest` · `RetryOcr` | Raise moderated document requests, or retry OCR. | `OnPostAsync` | Admin, HoD, Project Officer |
| `/Projects/Documents/Approvals` · `Approvals/Review` | Pending document requests for a project, and the approve/reject decision. | `OnPostApproveAsync`, `OnPostRejectAsync` | Admin, HoD |
| `/Projects/{id}/Photos` (+ `Upload`, `Reorder`, `{photoId}/Edit`, `{photoId}/View/{size?}`, `{photoId}/Download/{size?}`) | Project photo gallery. | Index: `OnPostReorderAsync`, `OnPostRemoveAsync` (with a media-manage check). Upload/Reorder/Edit: `OnPostAsync` | Gallery/View/Download: Authenticated (+ `ProjectAccessGuard`). Upload/Reorder/Edit: Admin, HoD, Project Officer |
| `/Projects/{id}/Videos` (+ `Upload`, `{videoId}/Stream`, `{videoId}/Poster`) | Project videos. | Index: `OnPostRemoveAsync`, `OnPostSetFeaturedAsync` (with `CanManageProjectMedia`). Upload: `OnPostAsync` | Index/Stream/Poster: Authenticated (+ guard). Upload: Admin, HoD, Project Officer |
| `/Projects/Remarks/{projectId}` | Project remarks view. Data comes through `MapRemarkApi`. | `OnGetAsync` | Authenticated |
| `/Projects/Tot/Edit/{id}` | Edit the project's Transfer of Technology details and remarks. | `OnPostAsync`, `OnPostAddRemarkAsync` | Authenticated (+ Admin/HoD/assigned-PO check) |
| `/Projects/Ongoing` | Ongoing projects board and export. Only HoD can edit external remarks inline. | `OnGetExportAsync` | Authenticated |
| `/Projects/CompletedSummary` · `CompletedSummary/Edit/{id}` | Completed projects summary and export · edit technology and proliferation details. | `OnGetExportAsync` · `OnPostAsync` | Authenticated · Admin, HoD, Project Office |
| `/Projects/Arpp` (+ `History`, `Print`) | Read-only library of **published** ARPP/PPP issues, with Excel export and attachment download. | `OnGetAttachmentAsync`, `OnGetExcelAsync` | Authenticated |
| `/Projects/Reports` · `Reports/ArppFyUpdate` · `Reports/FfcProjectsUpdate` | Project report catalogue · ARPP FY update · FFC projects update (Word, PDF and Excel exports). | `OnGetWordAsync`, `OnGetPdfAsync`, `OnGetExcelAsync` | `ViewArpp` (all three pages) |
| `/Projects/Publications` | Publications hub. | — | Authenticated (folder convention) |
| `/Projects/Publications/Brochure` | Capability brochure builder (presets, preflight, preview, generate). | `OnPostSavePreset/RenamePreset/DuplicatePreset/DeletePresetAsync`, `OnPostPreflightAsync`, `OnPostPreviewAsync`, `OnPostGenerateAsync` | Authenticated. Shared presets are limited to Comdt, HoD, ITO |
| `/Projects/Publications/Compendium` (+ `Cover`, `Structure`) | Simulators compendium: review, preview, generate, cover editor, structure editor. | `OnPostPreflight/Review/Preview/GenerateAsync`, preset handlers, `OnPostSaveAsync` | Authenticated. Saving shared presets, cover and structure is limited to Comdt, HoD, ITO |
| `/Projects/Compendium` | Legacy entry. GET redirects to `/Projects/Publications/Compendium`. The drawer item "Proliferation compendium" still points here. | `OnPostGenerateAsync` | Authenticated |
| `/Process` | Procurement process (stage template) viewer. Checklist editing goes through `/api/processes/...` minimal APIs. | `OnGetAsync` (computes `CanEditChecklist` and `CanEditPurpose`) | Authenticated (edits need `Checklist.Edit` / `Checklist.PurposeEdit`) |
| `/Analytics` | Portfolio analytics (data from `MapProjectAnalyticsApi`). | `OnGetAsync` | Authenticated |
| `/IndustryPartners` | Industry directory: partners, contacts, attachments, project links, duplicate suggestions. | `OnPostCreatePartner/UpdatePartner/DeletePartnerAsync`, `OnPostAdd/Update/DeleteContactAsync`, `OnPostUpload/DeleteAttachmentAsync`, `OnGetDownloadAttachmentAsync`, `OnPostLink/UnlinkProjectAsync` | `IndustryPartners.View` (+ per-action policies: Create, EditAny or owner, Delete, Contact.*) |

## Decision Centre (approvals)

| Route | Purpose | Main handlers | Access |
| --- | --- | --- | --- |
| `/Approvals/Pending` | Central approval queue. Types: StageChange, ProjectMeta, PlanApproval, DocRequest, TotRequest, ProliferationYearly, ProliferationGranular, ActivityDelete, TrainingDelete, RepositoryDocumentDelete. | `OnGetAsync` | Admin, HoD |
| `/Approvals/Pending/{type}/{id}` | Approval review. | `OnGetAsync` | Admin, HoD |
| `/Approvals/Pending/decide` | Decision POST. | `OnPostAsync` | Admin, HoD |

These compatibility routes redirect to the Decision Centre: `/Activities/Approvals` (Admin, HoD), `/ProjectOfficeReports/Training/Approvals` (`ApproveTrainingTracker`) and `/DocumentRepository/Admin/DeleteRequests` (`DocRepo.DeleteApprove`).

## Task management, activities and ideas

| Route | Purpose | Main handlers | Access |
| --- | --- | --- | --- |
| `/ActionTasks` | Task management views: overview (command centre), my tasks, planning board with sprints, register, reports. Tasks can be created, assigned, submitted, returned, closed and moved between sprint and backlog. | `OnPostCreate*`, `OnPostCreate/Update/Activate/Close*SprintAsync`, `OnPostSubmit/ReturnForAction/Close/UpdateStatusAsync`, `OnPostReassign/ChangePriority/ChangeTaskDateAsync`, remark handlers | `ActionTracker.Access` |
| `/ActionTasks/Details/{id}` | Single task: remarks, status, dates, reassignment, sprint moves. | Same family of handlers | `ActionTracker.Access` |
| `/Activities` | Institutional ("miscellaneous") activities list and export. | `OnPostRequestDeleteAsync`, `OnPostExportAsync` | Authenticated (+ `ActivityAuthorizationPolicy`) |
| `/Activities/Edit/{id?}`, `/Activities/Details/{id}` | Create or edit an activity; details and attachments. | `OnPostAsync`; `OnPostUploadAsync`, `OnPostRemoveAttachmentAsync` | Authenticated. Create: Admin, HoD, Project Officer, Project Office, TA. Edit: those roles or the creator. Delete approval: Admin, HoD (via the Decision Centre) |
| `/ProjectIdeas` · `Create` · `Details/{id}` · `Edit/{id}` · `Deleted` | Ideation board. Any signed-in user can view ideas and comment on them. Details handlers: comments, notes, documents (preview and download), archive, restore, delete. | `OnPostCommentAsync`, `OnPostNoteAsync`, `OnPostUploadAsync`, `OnPostArchive/Restore/DeleteAsync`; `OnPostRestoreAsync` on Deleted | Authenticated (+ `ProjectIdeaPermissionService`): create, archive, restore, delete and the deleted list are limited to Admin, HoD, Comdt. Edit: assigned PO, HoD or Comdt. Conference comments: Comdt, HoD |

## Photos (media library)

| Route | Purpose | Main handlers | Access |
| --- | --- | --- | --- |
| `/Photos` | Media library with timeline, albums, "My photos" and person discovery. The page model is split into `Index.cshtml.cs` and `Index.People.cs`. | `OnGetRevisionAsync`, `OnGetPersonDiscoveryStatusAsync`, `OnPostConfirm/RejectPersonCandidate(s)Async` (with a person-review check) | Authenticated |
| `/Photos/Albums/Actions` | POST-only album commands. The album service enforces the owner or a privileged actor (Admin, HoD, Comdt). | `OnPostCreate/Update/Archive/Restore/AddItems/RemoveItems/SetCover/Reorder/UpdateCaptionAsync` | Authenticated |
| `/Photos/Media/{id}/{variant?}`, `/Photos/Download` | Stream a derivative; bulk download (POST). | `OnGetAsync`, `OnPostAsync` | Authenticated |
| `/Photos/People`, `/Photos/People/Portrait/{id}` | People directory and portraits. | `OnGetAsync` | Authenticated |
| `/Photos/People/Details/{id}`, `/Photos/People/Review`, `/Photos/FaceThumbnail` | Identity management: link or unlink a user, merge, references, suppress faces; review face clusters. | Many `OnPost*` | Admin, HoD |
| `/Admin/MediaSources` | Media source configuration, scans, catalogue sync, retries, availability reconciliation. | `OnPostSave/Test/Scan/SetState/Disconnect/Retry*/Reconcile*/Recheck*Async` | `Admin.Media.View` (Admin, HoD) |
| `/Admin/MediaIntelligence`, `/Admin/MediaIntelligence/Classifications` | Media processing queue, identity candidate refresh, classification review. | `OnPostQueueAsync`, `OnPostRefresh*`, `OnPostSet/SetBatch/ApproveFace/RevokeFace/Reset/ReclassifyStaleAsync` | Admin, HoD |

The media admin pages live in the root `Pages/Admin/` folder, not in the Admin area, so the Admin area convention does not apply to them. They are reached from links on the Photos pages, not from the drawer.

## ERP usage

| Route | Purpose | Main handlers | Access |
| --- | --- | --- | --- |
| `/Usage` | ERP adoption and usage analytics, with export. Linked from the Admin drawer and from the command workspace. | `OnGetExportAsync` | `ERP.Usage.View` (Admin, Comdt, HoD) |

## Document repository area (`/DocumentRepository`)

| Route | Purpose | Main handlers | Access |
| --- | --- | --- | --- |
| `/Documents` | Repository search and browse, with favourites. | `OnPostToggleFavouriteAsync` | `DocRepo.View` |
| `/Documents/View`, `/Documents/Reader/{id}`, `/Documents/Download` | Viewer, reader (marks AOTS documents as read), download. | `OnGetAsync` | `DocRepo.View` |
| `/Documents/Upload` | Upload a PDF. | `OnPostAsync` | `DocRepo.Upload` |
| `/Documents/Manage/{id}` | Activate or deactivate a document, request deletion, retry OCR. | `OnPostDeactivate/Activate/RequestDelete/RetryOcrAsync` | `DocRepo.SoftDelete` |
| `/Documents/Edit/{id}`, `/Documents/AotsViews/{id}` | Edit metadata; see who has viewed an AOTS document. | `OnPostAsync` | `DocRepo.EditMetadata` |
| `/Admin/OCRFailures/OcrFailures`, `/Admin/MissingFiles` | Requeue failed OCR; list documents whose files are missing. | `OnPostRequeueAsync`, `OnPostRequeueAllAsync` | `DocRepo.DeleteApprove` |
| `/Admin/DocumentCategories`, `/Admin/OfficeCategories` | Category maintenance. | `OnPostCreate/Update/ToggleAsync` | `DocRepo.ManageCategories` |
| `/Admin/Trash` | Restore or purge deleted documents. | `OnPostRestoreAsync`, `OnPostPurgeAsync` | `DocRepo.Purge` |
| `/Admin/DeleteRequests` | Redirects to the Decision Centre. | `OnGet` | `DocRepo.DeleteApprove` |

## Project office reports area (`/ProjectOfficeReports`)

This area has no `Index` page. The drawer group header is rendered as plain text because its link cannot be resolved.

| Route | Purpose | Main handlers | Access |
| --- | --- | --- | --- |
| `/Visits`, `/Visits/All`, `/Visits/Details/{id}`, `/Visits/ViewPhoto/...` | Visits to SDD: dashboard, full list, details, photos, Excel/PDF export. | `OnPostDeleteAsync` (manager check), `OnPostExportAsync`, `OnPostExportPdfAsync` | `ViewVisits` (authenticated) |
| `/Visits/New`, `/Visits/Edit/{id}` | Record or edit a visit and manage its photos (upload, delete, set cover). | `OnPostAsync`, `OnPostUpload/DeletePhoto/SetCoverAsync` | `ManageVisits` |
| `/VisitTypes` (+ `New`, `Edit/{id}`) | Visit type master data. | `OnPostToggleAsync`, `OnPostDeleteAsync`, `OnPostAsync` | Admin |
| `/SocialMedia`, `/SocialMedia/Details/{id}`, `/SocialMedia/ViewPhoto/...` | Social media activities with Excel/PDF export. | `OnPostExportAsync`, `OnPostExportPdfAsync` | Authenticated |
| `/SocialMedia/Create`, `Edit/{id}`, `Delete/{id}` | Manage activities and their photos. | `OnPostAsync`, `OnPostUpload/DeletePhoto/SetCoverAsync` | `ManageSocialMediaEvents` |
| `/Admin/SocialMediaTypes` (+ `New`, `Edit`, `Platforms/*`) | Event types and platforms. | Toggle, delete and save handlers | Admin, HoD |
| `/Tot`, `/Tot/Summary` | Transfer of Technology tracker (submit, decide, export) and summary (yearly). | `OnPostSubmitAsync` (`ManageTotTracker`), `OnPostDecideAsync` (`ApproveTotTracker`), `OnPostExportAsync`, `OnGetYearlyAsync` | `ViewTotTracker` (authenticated) |
| `/Ipr` (alias `/Patent`), `/Ipr/Download`, `/Ipr/Manage` (alias `/Patent/Manage`) | IPR register: records, attachments, summary, export. `Manage` redirects to the Index create/edit mode. Record and attachment handlers are in the partial files `Index.RecordCommands.cs` and `Index.AttachmentCommands.cs`. | `OnPostCreate/Edit/DeleteAsync`, `OnPostAttachAsync`, `OnPostRemoveAttachmentAsync` (each checks `Ipr.Edit`), `OnGetSummaryAsync`, `OnGetExportAsync` | `Ipr.View`. Manage: `Ipr.Edit` |
| `/Proliferation/Summary`, `/Proliferation`, `/Proliferation/Project/{id}`, `/Proliferation/Reports` | Proliferation overview (with exports), records, per-project view, reports. Writes go through `api/proliferation` controllers. | `OnGetExportProjectsAsync`, `OnGetExportYearBreakdownAsync` | `ViewProliferationTracker` (authenticated) |
| `/Proliferation/Manage` | Submission workspace. | `OnGetAsync` | `SubmitProliferationTracker` |
| `/Training`, `/Training/Records`, `/Training/View` | Training tracker, records list, details, export. | `OnPostExportAsync` | `ViewTrainingTracker` (folder convention + attribute) |
| `/Training/Manage/{id?}` | Create or edit training, including the **Legacy record** toggle (totals without a roster), and request deletion. | `OnPostSaveAsync`, `OnPostRequestDeleteAsync` | `ManageTrainingTracker` |
| `/ProgressReview` | Progress review report. | `OnGetAsync` | `ViewProgressReview` |
| `/FFC`, `/FFC/Map`, `/FFC/MapBoard`, `/FFC/MapTable`, `/FFC/Footprint`, `/FFC/Attachments/View` | FFC proposals: world map, country board, tables, footprint PowerPoint export, attachment viewer. | `OnGetDataAsync`, `OnPostExportPowerPointAsync` | Authenticated |
| `/FFC/MapTableDetailed` | Detailed FFC table with Word/Excel export and inline remarks and progress editing. | `OnPostUpdateOverallRemarksAsync`, `OnPostUpdateProgressAsync` (`CanInlineEditFfc`), `OnPostExportWordAsync`, `OnGetExport*` | Authenticated (+ handler checks) |
| `/FFC/Records/Details/{id}` | Record workspace: record, projects, attachments, archive. | `OnPostUpdateRecord/SaveProject/DeleteProject/UploadAttachment/DeleteAttachment/ArchiveAsync` (each checks `CanManageFfc`) | Authenticated (+ handler checks) |
| `/FFC/Records/Projects/Manage`, `/FFC/Records/Attachments/Upload` | Project rows and attachments for a record. | Create, update and delete handlers (each checks `CanManageFfc`) | Authenticated (+ handler checks) |
| `/FFC/Records/Create`, `/FFC/Records/Manage`, `/FFC/Records/Archived`, `/FFC/Countries/Manage` | Create and manage records, restore archived ones, activate or deactivate countries. | `OnPostAsync`, `OnPostCreate/UpdateAsync`, `OnPostRestoreAsync`, `OnPostToggleActiveAsync` | `ManageFfc` |
| `/ARPP`, `/ARPP/Details`, `/ARPP/Print`, `/ARPP/ProjectHistory` | ARPP/PPP administration: issue list, issue details (PDF upload and delete, verify, unlock), print, project history. | `OnPostUploadPdfAsync`, `OnPostDeletePdfAsync`, `OnPostVerifyAsync`, `OnPostUnlockAsync`, `OnGetExcelAsync` | `ViewArpp` (verify: `VerifyArpp`; unlock: `UnlockArpp`) |
| `/ARPP/Create`, `/ARPP/Manage`, `/ARPP/Reconcile` | Create, edit and reconcile ARPP issues. | `OnPostAsync`, `OnGetSuggestionAsync` | `ManageArpp` |
| `/Projects/LegacyImport` | Legacy project import (preview, commit, cancel, template). | `OnPostPreview/Commit/CancelAsync`, `OnGetTemplate` | `Admin.Ingestion.Manage` |

## Admin area (`/Admin`)

The folder convention requires an authenticated user. Every page adds an `AdminPolicies.*` capability, defined in `AdminCapabilityCatalog`.

| Route | Purpose | Main handlers | Policy (roles) |
| --- | --- | --- | --- |
| `/Admin`, `/Admin/Help` | Administration overview and guide. | — | `Access` (Admin) |
| `/Admin/Users` (+ `Create`, `Details`, `Edit`, `Disable`, `Enable`, `Reset`, `Delete` with undo) | User lifecycle and role assignment; CSV export. | `OnPostAsync`, `OnPostUndoAsync`, `OnGetExportAsync` | `Users.Manage` (Admin) |
| `/Admin/AccessGovernance` | Privileged users, role holdings, policy coverage; export. | `OnGetExportAsync` | `AccessGovernance.View` (Admin) |
| `/Admin/Analytics` (redirects to `Logins`), `/Admin/Analytics/Logins`, `/Admin/Diagnostics/DbHealth`, `/Admin/Diagnostics/SearchIndex` | Login activity (CSV), system health, search index rebuild and retry. | `OnGetExportCsvAsync`, `OnPostRebuild/RetryAll/RetryAsync` | `Security.View` (Admin) |
| `/Admin/Logs` | Audit logs (CSV). | `OnGetExportCsvAsync` | `Logs.View` (Admin) |
| `/Admin/Recovery`, `/Admin/Projects/Trash`, `/Admin/Projects/Archived`, `/Admin/Documents/Recycle`, `/Admin/Calendar/Deleted` | Recovery centre; project trash (execute purge); restore archived projects; document recycle bin; restore deleted events. | `OnPostExecuteAsync`, `OnPostRestoreAsync` | `Recovery.Manage` (Admin) |
| `/Admin/MasterData`, `/Admin/Categories/*`, `/Admin/TechnicalCategories/*`, `/Admin/Lookups/{ProjectTypes,SponsoringUnits,LineDirectorates}/*`, `/Admin/MasterData/ArppReferences` | Master data: taxonomies and lookups (create, edit, toggle, move, deactivate, delete), ARPP reference values. | `OnPostAsync`, `OnPostToggleAsync`, `OnPostMoveAsync`, `OnPostSaveAsync`, `OnPostSetActiveAsync` | `MasterData.Manage` (Admin) |
| `/Admin/MasterData/Integrity` | Configuration integrity; normalise ordering. | `OnPostNormaliseOrderAsync` | `MasterData.Integrity.Manage` (Admin) |
| `/Admin/ActivityTypes` (+ `Create`, `Edit`) | Activity type maintenance. | `OnPostToggleAsync`, `OnPostAsync` | `ActivityTypes.Manage` (Admin, HoD) |
| `/Admin/Maintenance`, `/Admin/Documents/IngestExternalPdfs` | Maintenance centre; external PDF ingestion (with failure report). | `OnPostRunAsync`, `OnGetFailureReport` | `Ingestion.Manage` (Admin) |

The Admin drawer (`AdminNavigationCatalog`) also links `/Usage`, `/Settings/Holidays`, `/Celebrations` and `/ProjectOfficeReports/Projects/LegacyImport`, each gated by its own policy.
