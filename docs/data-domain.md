# Data and Domain

This page describes the persistence layer and domain model as implemented in code. Where the
code and this page disagree, the code wins: `Data/ApplicationDbContext.cs`,
`Features/MediaLibrary/Data/MediaLibraryModelConfiguration.cs` and the two model snapshots
are the sources of truth.

## Databases and contexts

| Context | Location | Tables | Migrations | History table |
| --- | --- | --- | --- | --- |
| `ApplicationDbContext` (`IdentityDbContext<ApplicationUser, IdentityRole, string>`) | `Data/ApplicationDbContext.cs` | Identity plus every business module (146 `DbSet`s) | `Migrations/`: 115 migrations, from `20251102161908_InitialMigrations` to `20261216230000_AllowDuplicateActivityTitles` | `__EFMigrationsHistory` |
| `MediaLibraryDbContext` | `Features/MediaLibrary/Data/` | Media catalogue (`MediaLibrarySources`, `MediaAssets`, jobs, classification, faces/people, albums, audits) | `Features/MediaLibrary/Data/Migrations/`: 13 migrations, latest `20260820103000_HardenMediaPersonUserLinkExperience` | `__EFMigrationsHistory_MediaLibrary` |

- Both contexts use **the same PostgreSQL database** (Npgsql EF Core provider 8.0.x) and the default `public` schema. Neither context sets a schema.
- Design-time factories are `Data/ApplicationDbContextFactory.cs` and `Features/MediaLibrary/Data/MediaLibraryDbContextFactory.cs`. The application factory reads `ConnectionStrings:DefaultConnection` from `appsettings*.json` or environment variables.
- `Program.cs` registers `ApplicationDbContext` with `PrismMediaOutboxSaveChangesInterceptor`, which writes `PrismMediaOutboxMessages` rows in the same transaction as media-producing changes. `AddMediaLibrary(...)` registers the media context.
- `Npgsql.EnableLegacyTimestampBehavior` and `Npgsql.DisableDateTimeInfinityConversions` are switched on in `Program.cs` and in the design-time factory. As a result, `DateTime` maps to `timestamp without time zone` unless a column type is set explicitly, and `DateTimeOffset` maps to `timestamp with time zone`. Most `*Utc` columns on newer modules are `DateTimeOffset`/`timestamptz`. Older columns such as `Project.CreatedAt`, `ApplicationUser.CreatedUtc` and `DocRepoDocumentTexts.UpdatedAtUtc` are `DateTime`/`timestamp` and hold UTC by convention.
- Migrations are applied by the startup gate. `MIGRATIONS-POLICY.md` covers the advisory lock, immutable IDs in `Migrations/immutable-migration-ids.txt` and the add-migration helper script. Many recent migrations are written by hand and have no `.Designer.cs` file, so the model snapshot is maintained by hand as well.
- Seeding: `Data/IdentitySeeder.cs` seeds the roles and bootstrap admin. `Data/StageFlowSeeder.cs` seeds `StageTemplates` and `StageDependencyTemplates` for workflow versions V1 and V2. `Data/Seed/iso3166.json` is loaded by `Services/Startup/IsoCountrySeedData.cs` for FFC countries. `HasData` seeds the `system` service user, the default social-media event types, training types, the training rank-to-category map and activity types.

### Tables outside the EF model

`20261216200000_AddSearchV2Foundation` creates tables, functions and triggers with raw SQL only. None of them is mapped in the EF model:
- Tables: `SearchEntries`, `SearchEntryTerms`, `SearchEntryPrincipals`, `SearchAliases`, `SearchIndexWorkItems`, `SearchIndexState`, `SearchQueryLogs`, `SearchClickLogs`, `SearchShadowComparisons`. The migration also enables the `pg_trgm` extension and adds GIN full-text and trigram indexes.
- Row triggers named `TR_SearchV2_*` enqueue `SearchIndexWorkItems` on insert, update or delete. They sit on `Projects`, `ProjectCapabilityStatements`, `ProjectTechnicalSpecificationItems`, `ProjectStages`, `ProjectDocuments`, `ProjectDocumentTexts`, `Documents`, `DocRepoDocumentTexts`, `FfcRecords`, `FfcProjects`, `FfcAttachments`, `IprRecords`, `IprAttachments`, `Activities`, `Visits`, `SocialMediaEvents`, `Trainings`, `TrainingProjects`, `TrainingTrainees`, `ProjectTots`, `ProliferationGranular`, `ArppIssues` and `ArppEntries`.
- Earlier migrations add `tsvector` columns and triggers for full-text search on `Documents` (DocRepo) and `ProjectDocuments`: `docrepo_*_search_vector_*` and `project_document*_search_vector_*`.

## Cross-cutting conventions

### Concurrency

| Mechanism | Where | How it works |
| --- | --- | --- |
| `RowVersion` (`bytea`) via `ConfigureRowVersion()` | Most mutable aggregates, including `Project`, the procurement fact tables, `ProjectDocument`/`ProjectDocumentRequest`, `ProjectTotRequest`, `Remark`, `ProjectCategory`/`TechnicalCategory`/`ProjectType`/`SponsoringUnit`/`LineDirectorate`, `Holiday`, stage checklist templates, the IPR/FFC/Visit/Social media/Training/Proliferation/Activity entities, the ARPP entities, `IndustryPartner*`, `ProjectIdea`/`ProjectIdeaComment`, `ActionTaskItem`/`ActionSprint`, brochure and compendium presets, and `ProjectBriefingDeck` | This is not a database-generated rowversion. `ApplicationDbContext.PrepareRowVersionValues()` runs from both `SaveChanges` overrides and writes a new random 16-byte value (from a `Guid`) to every `byte[]` concurrency token on each Added or Modified entry. Services enforce the check by setting `Entry(x).Property(p => p.RowVersion).OriginalValue` to the value the client submitted, then handling `DbUpdateConcurrencyException`. |
| PostgreSQL `xmin` | `TodoItem` only | `Property<uint>("xmin").IsRowVersion()` |
| Integer `Version` tokens | `ProjectPhoto.Version`, `ProjectVideo.Version`, `Project.CoverPhotoVersion`, `Project.FeaturedVideoVersion` | Application-incremented `IsConcurrencyToken()` |
| `Guid Version` tokens | `NotebookItem`, `NotebookSystemItemPreference` | `IsConcurrencyToken()` |
| Media library tokens | `MediaAsset.EditorialConcurrencyToken` and `ClassificationConcurrencyToken`, `MediaFace`, `MediaPerson`, `MediaPersonFace`, `MediaPersonUserLink`, `MediaFaceReviewDecision` and `MediaAlbum` (`ConcurrencyToken`) | `IsConcurrencyToken()` |

### Soft delete, archive and retention

Only one global query filter exists: `Event` has `HasQueryFilter(x => !x.IsDeleted)`. Every other soft-delete flag must be filtered explicitly in queries. A call to `IgnoreQueryFilters()` on `Projects` has no effect.

| Entity | Soft-delete / archive fields | Hard delete / retention |
| --- | --- | --- |
| `Project` | `IsArchived`, `ArchivedAt`, `ArchivedByUserId`; `IsDeleted`, `DeletedAt`, `DeletedByUserId`, `DeleteReason`, `DeleteMethod` (`"Trash"`), `DeleteApprovedByUserId` | Handled by `ProjectModerationService` and `ProjectRetentionWorker`. See [archive-trash-plan.md](archive-trash-plan.md). |
| `ProjectDocument` | `Status` (`Published`/`SoftDeleted`) together with `IsArchived`, `ArchivedAtUtc`, `ArchivedByUserId` | `DocumentService.HardDeleteAsync` |
| `Document` (DocRepo) | `IsDeleted`, `DeletedAtUtc`, `DeletedByUserId`, `DeleteReason`; `IsActive` | Purged from the DocRepo admin Trash page (`Areas/DocumentRepository/Pages/Admin/Trash`) |
| `Remark`, `ProjectComment`, `ProjectIdea` (+ `Comment`/`Note`/`Document`), `Activity`, `ActionTaskItem`/`ActionSprint`/`ActionTaskUpdate`/`ActionTaskAttachment`, `FfcRecord`, `Event` | `IsDeleted` (plus deleted-by/at metadata on most) | No purge |
| `IprAttachment` | `IsArchived`, `ArchivedAtUtc`, `ArchivedByUserId` | none |
| `NotebookItem` | `DeletedAtUtc` (trash), `ArchivedAtUtc` | `Hosted/NotebookTrashRetentionWorker.cs` |
| `TodoItem` | `DeletedUtc` | `Services/TodoPurgeWorker.cs` (`ExecuteDeleteAsync` after `RetentionDays`) |
| `Celebration` | `DeletedUtc` (index filtered on `DeletedUtc IS NULL`) | none |
| `ApplicationUser` | `IsDisabled`; `PendingDeletion` with undo state | `UserPurgeWorker` hard-deletes users after the undo window via `UserManager.DeleteAsync` |
| `MediaLibrarySource`, `MediaAsset` | `IsDeleted`, `IsArchived` (asset), `IsAvailable`/`AvailabilityStatus` | Media library workers |

Other retention workers: `Hosted/AuditRetentionWorker.cs` (opt-in through `Audit:Retention:Enabled`), `Services/Usage/UserActivityRetentionWorker.cs` (30–1095 days) and `Services/Notifications/NotificationRetentionService.cs`.

### Delete behaviour worth knowing

- Deleting a `Project` row cascades to its stages, plan versions (and `StagePlans` and approval logs), photos, videos, documents and document requests, remarks, comments, ToT and ToT requests, procurement facts, the LPP/production-cost/tech-status rows, capability statements, specification items, schedule settings and durations, `ProjectAudits`, `ProjectMetaChangeRequests`, `TrainingProjects`, `IndustryPartnerProjects` and `UserProjectMutes`.
- The same delete sets `ProjectId` to NULL on `IprRecords`, `FfcProjects.LinkedProjectId`, `ArppEntries`, `ArppPublishedEntries`, `BrochurePresetProjects` and `CompendiumPresetProjects`.
- `ProjectBriefingDeckItems.ProjectId` is **Restrict**: a project that belongs to any briefing deck cannot be deleted until the deck item is removed.
- `ProliferationYearly`, `ProliferationGranular`, `ProliferationYearPreference`, `StageChangeRequests`, `StageShiftLogs`, `PlanRealignmentAudits` and `Notifications`/`NotificationDispatches` carry a `ProjectId` with **no foreign key**. `MediaAssets.ProjectId` also has no foreign key, because it lives in the other context.
- User foreign keys are mostly `Restrict` or `SetNull`. The exceptions are by convention or explicit `Cascade`: `PlanVersions.CreatedByUserId`, `ProjectComments.CreatedByUserId`, `ProjectCommentAttachments.UploadedByUserId`, the comment and remark mention tables, and the notebook tables (`NotebookItems.OwnerId`, tags, preferences, collaborators).

## Entity groups by module

### Identity and users

| Entity | Notes |
| --- | --- |
| `ApplicationUser` (`Models/ApplicationUser.cs`) | Extends `IdentityUser` with `MustChangePassword`, `FullName`, `Rank`, `LastLoginUtc`, `LoginCount`, `CreatedUtc`, `AccountKind` (`UserAccountKind`: Human=1, Service=2, Test=3; check constraint `CK_AspNetUsers_AccountKind`), `IsDisabled`/`DisabledUtc`/`DisabledByUserId`, `PendingDeletion`/`DeletionRequestedUtc`/`DeletionRequestedByUserId`/`DeletionPreviousStateJson`, `DefaultUserRoleId`, `ShowCelebrationsInCalendar` and `ComdtOfficerWorkloadOrderJson`. A `system` service user is seeded. |

### Projects core

| Entity | Notes |
| --- | --- |
| `Project` | `Name` (100), `Description`, `ProjectBrief`, `CaseFileNumber` (unique when not null: `UX_Projects_CaseFileNumber`), `LifecycleStatus` (`ProjectLifecycleStatus`: Active, Completed, Cancelled; stored as string), `IsLegacy`, `IsBuild` (repeat build), completion date fields `CompletedOn`/`CompletedYear`/`CompletedMonth` with three check constraints, `CancelledOn`/`CancelReason`, `CostLakhs`, `ArmService`, `YearOfDevelopment` |
| | Foreign keys: `CategoryId`, `TechnicalCategoryId`, `ProjectTypeId`, `SponsoringUnitId`, `SponsoringLineDirectorateId` (all Restrict); `HodUserId`, `LeadPoUserId`, `PlanApprovedByUserId`; `CoverPhotoId`, `FeaturedVideoId` (SetNull). Also `ActivePlanVersionNo`, `WorkflowVersion`, plus the archive and trash fields, `RowVersion`, and indexes `IX_Projects_IsDeleted_IsArchived` and the filtered `IX_Projects_IsDeleted_Filtered`. |
| `ProjectCategory`, `TechnicalCategory` | Self-referencing hierarchies (`ParentId`, Restrict), unique `(ParentId, Name)` |
| `ProjectType`, `SponsoringUnit`, `LineDirectorate` | Lookups with `IsActive` and `SortOrder` |
| `ProjectCapabilityStatement`, `ProjectTechnicalSpecificationItem` | Ordered child lists, unique `(ProjectId, DisplayOrder)`, `DisplayOrder >= 1` |
| `ProjectMetaChangeRequest` | Pending edits to name, description, case file number, category or build flag. One pending request per project (filtered unique index). Snapshots `Original*` values, including `OriginalRowVersion`. |
| `ProjectAudit` | Moderation audit (`Archive`, `RestoreArchive`, `Trash`, `RestoreTrash`) with metadata JSON. Cascades on project delete. |
| `ProjectLegacyImport` | One row per imported (category, technical category) pair |
| Procurement facts (`Models/ProjectFacts.cs`) | `ProjectIpaFact`, `ProjectAonFact`, `ProjectBenchmarkFact`, `ProjectCommercialFact` (L1), `ProjectPncFact` (all `decimal(18,2)` with `>= 0` checks), `ProjectSowFact`, `ProjectSupplyOrderFact`. Each has a `RowVersion` and cascades with the project. |
| Completed-project data (`Models/Projects`) | `ProjectProductionCostFact` (PK = `ProjectId`), `ProjectLppRecord` (optional link to a `ProjectDocument`), `ProjectTechStatus` (PK = `ProjectId`; `TechStatus` is `Current`, `Outdated` or `Obsolete`) |

### Stages, plans and timeline

| Entity | Notes |
| --- | --- |
| `StageTemplate`, `StageDependencyTemplate` | Stage catalogue for each workflow version (`Version`, `Code`), seeded by `StageFlowSeeder` |
| `ProjectStage` (`Models/Execution`) | One row per `(ProjectId, StageCode)`, unique. `Status` (`StageStatus`: NotStarted, InProgress, Completed, Skipped, Blocked); planned, forecast and actual dates; `RequiresBackfill`; `AutoCompletedFromCode`. Check constraint `CK_ProjectStages_CompletedHasDate`. |
| `StageChangeRequest`, `StageChangeLog` | Stage status and date change requests: one pending request per `(ProjectId, StageCode)`; `DecisionStatus` is `Pending`, `Approved`, `Rejected` or `Superseded`. The log's `Action` is limited by a check constraint. |
| `PlanVersion`, `StagePlan`, `PlanApprovalLog` | Timeline plan drafts and approvals. `Status` (`PlanVersionStatus`: Draft, PendingApproval, Approved); unique `(ProjectId, VersionNo)`; at most one Draft per `(ProjectId, OwnerUserId)`. Also stores schedule options (`AnchorStageCode`, `TransitionRule`, `SkipWeekends`, `PncApplicable`) and rejection metadata (`RejectedByUserId`, `RejectionNote`). |
| `ProjectPlanSnapshot`, `ProjectPlanSnapshotRow` | Immutable snapshot of the dates taken when a plan is approved |
| `PlanRealignmentAudit`, `StageShiftLog` | Realignment and shift history (no project foreign key) |
| `ProjectScheduleSettings` (PK = `ProjectId`), `ProjectPlanDuration` | Inputs for the duration-based scheduler |
| `StageChecklistTemplate`, `StageChecklistItemTemplate`, `StageChecklistAudit` | Process checklist designer. Versioned per stage code, with row versions and an audit log whose payload is `jsonb`. |
| `Status`, `Workflow`, `WorkflowStatus` | Legacy workflow lookup tables |

### Documents, photos and videos (project)

| Entity | Notes |
| --- | --- |
| `ProjectDocument` | `StorageKey`, `OriginalFileName`, `ContentType`, `FileSize`, `FileStamp`, `Status`, archive fields, OCR fields (`OcrStatus`: None, Pending, Succeeded, Failed, Skipped), a `SearchVector` (`tsvector`), and optional links to `StageId`, `TotId`, `RequestId` and `DocRepoDocumentId` |
| `ProjectDocumentRequest` | Upload, Replace or Delete workflow (`Status`: Draft, Submitted, Approved, Rejected, Cancelled). At most one pending request per document. |
| `ProjectDocumentText` (`Data/Projects`) | OCR text, one row per document |
| `ProjectPhoto` | Derivative set under a `StorageKey`; unique `(ProjectId, Ordinal)`; at most one `IsCover` per project; optional `TotId`; integer `Version` |
| `ProjectVideo` | Video and poster storage keys, `Ordinal`, integer `Version` |

### Remarks and comments

| Entity | Notes |
| --- | --- |
| `Remark`, `RemarkMention`, `RemarkAudit` | `Type` (Internal, External, Conference), `Scope` (General, TransferOfTechnology), `AuthorRole` (`RemarkActorRole`), `EventDate`, stage reference, soft delete, and a `jsonb` audit snapshot |
| `ProjectComment`, `ProjectCommentAttachment`, `ProjectCommentMention` | Threaded comments (`ParentCommentId`, cascade), `Type` (Update, Risk, Blocker, Decision, Info), `Pinned`, `IsDeleted` |

### Project Office Reports and related modules

| Module | Entities | Notes |
| --- | --- | --- |
| Transfer of Technology | `ProjectTot` (1:1 with project), `ProjectTotRequest` (1:1 pending request) | `ProjectTotStatus`: NotRequired, NotStarted, InProgress, Completed. Date precision fields. Request `DecisionState`: Pending, Approved, Rejected. |
| IPR | `IprRecord`, `IprAttachment` (`Infrastructure/Data`) | `IprType`: Patent, Copyright. `IprStatus`: FilingUnderProcess, Filed, Granted, Rejected, Withdrawn. Unique `(IprFilingNumber, Type)`; optional project link (SetNull). |
| Visits | `VisitType`, `Visit`, `VisitPhoto` | Optional cover photo (`ClientSetNull`) |
| Social media | `SocialMediaEventType`, `SocialMediaPlatform`, `SocialMediaEvent`, `SocialMediaEventPhoto` | At most one cover photo per event |
| Training | `TrainingType`, `Training`, `TrainingCounters` (1:1), `TrainingProject` (M:N with allocation share), `TrainingTrainee`, `TrainingDeleteRequest`, `TrainingRankCategoryMap` | `TrainingCounterSource`: Legacy, Roster |
| Proliferation | `ProliferationYearly`, `ProliferationGranular`, `ProliferationYearPreference` | `ProliferationSource`: Sdd, Abw515. `YearPreferenceMode`: Auto, UseYearly, UseGranular, UseYearlyAndGranular. No foreign key to `Projects`. |
| FFC | `FfcCountry`, `FfcRecord` (unique active `(CountryId, Year)`), `FfcProject` (optional `LinkedProjectId`), `FfcAttachment` (`Kind`: PDF or PHOTO, stored upper-case) | Check constraints tie dates to their yes/no flags |
| ARPP / PPP register | `ArppIssue`, `ArppEntry`, `ArppAttachment` (one PDF per issue), `ArppCfaOption`, `ArppFundOption`, `ArppDfpdsSchedule`, `ArppPublishedIssue`/`ArppPublishedEntry` (published snapshot) | `ArppIssueKind`: Original=1, Addendum=2. `ArppCategory`: New, CommittedLiability, CarryForward, Delisted. Extensive check constraints. |
| Activities | `ActivityType`, `Activity`, `ActivityAttachment`, `ActivityDeleteRequest` | At most one pending delete request per activity |
| Industry partners | `IndustryPartner`, `IndustryPartnerContact` (phone or email required), `IndustryPartnerAttachment`, `IndustryPartnerProject` (M:N) | Unique normalized name, or name plus location |

### Project ideas

`ProjectIdea` (status is `Active`, `OnHold` or `Archived`; assigned PO and HoD; soft delete with reason), plus `ProjectIdeaComment` (typed, `RowVersion`), `ProjectIdeaNote` and `ProjectIdeaDocument`. Children cascade with the idea.

### Action tracker

`ActionTaskItem` (optional `SprintId`, Restrict; `DueDate` is `date`), `ActionSprint` (`ActionSprintStatus`: Planned, Active, Closed), `ActionTaskUpdate`, `ActionTaskAttachment`, `ActionTaskAuditLog` and `ActionSprintAuditLog`. All carry soft-delete flags where relevant.

### Notebook and to-do

| Entity | Notes |
| --- | --- |
| `NotebookItem` | Types: Note, Sticky, Checklist, Reminder, Idea, Draft. Statuses: Active, Completed, Archived. Owner cascade, `Guid Version`, `LegacyTodoItemId` for migrated to-dos, idempotent `ClientRequestId`. |
| `NotebookChecklistItem`, `NotebookTag`, `NotebookItemTag`, `NotebookAttachment`, `NotebookItemCollaborator`, `NotebookSystemItemPreference`, `NotebookSystemItemTag`, `NotebookMigrationState` | Notebook children and preferences |
| `TodoItem` | Legacy personal to-dos (`TodoPriority`, `TodoStatus`), `xmin` concurrency |

### Publications and briefings

| Entity | Notes |
| --- | --- |
| `BrochurePreset`, `BrochurePresetProject` | Shared capability brochure presets (`Models/Publications/BrochurePreset.cs`) |
| `CompendiumPreset`, `CompendiumPresetSection`, `CompendiumPresetProject`, `CompendiumPresetCoverImage`, `CompendiumPresetPhotoPreference` | Simulators compendium presets. Project links use SetNull and keep `ProjectNameSnapshot`. |
| `ProjectBriefingDeck`, `ProjectBriefingDeckItem` | Briefing decks with enum-valued layout and presentation settings stored as strings, and `SelectionRulesJson` (`jsonb`). Items restrict project deletion. |

### Calendar, celebrations and holidays

| Entity | Notes |
| --- | --- |
| `Event` | Calendar events. `EventCategory`: Visit, Insp, Conference, Other. The only entity with a global soft-delete query filter. |
| `Celebration` | Birthdays and anniversaries (`CelebrationType`) |
| `Holiday` | `HolidayType`: Gazetted=1, Restricted=2. Gazetted holidays must be observed (check constraint). Records observance changes. |

### Notifications, audit and analytics

| Entity | Notes |
| --- | --- |
| `NotificationDispatch` | Outbound queue: payload, attempts, lock token and expiry, dead-letter timestamp |
| `Notification` | Per-recipient rows. Unique `(RecipientUserId, Fingerprint)` and `SourceDispatchId` when not null. |
| `UserNotificationPreference`, `UserProjectMute` | Composite-key preferences and per-project mutes |
| `AuditLog` | Global audit trail (`Level`, `Action`, `UserId`, `Ip`, `TimeUtc`). Project purges are recorded here as `Projects.Purge`. |
| `AuthEvent`, `DailyLoginStat` | Login events and daily aggregates |
| `UserActivityBucket`, `UserActivityDailySummary` | Usage analytics buckets and daily summaries (keyed by IST date) |
| `DocRepoAudit` | Document repository audit (`DetailsJson` is `jsonb`; no foreign key, so rows survive a purge) |

### Document repository (`Data/DocRepo`)

`Document` (unique `Sha256`, `StoragePath`, `OfficeCategoryId`/`DocumentCategoryId`, `DocumentDate`, `IsAots`, `IsExternal`, OCR status (`DocOcrStatus`), `tsvector` `SearchVector`, soft delete), `DocumentText` (table `DocRepoDocumentTexts`), `Tag`/`DocumentTag`, `OfficeCategory`, `DocumentCategory`, `DocumentDeleteRequest`, `DocRepoExternalLink` (`SourceModule`/`SourceItemId`), `DocRepoFavourite` and `DocRepoAotsView`.

### Media library (`Features/MediaLibrary`)

A separate context in the same database. `MediaLibrarySource` (`SourceType`: Prism or FileSystem) produces `MediaAsset` rows (`Origin`: ProjectPhoto, ProjectVideo, VisitPhoto, SocialMediaEventPhoto, ExternalFile, ActivityPhoto; `Kind`: Photo or Video; unique `(SourceId, SourceEntityId)`).

Other tables are `MediaProcessingJob`, classification runs and audits, face intelligence (`MediaFace`, `MediaFaceEmbedding`, `MediaPerson`, `MediaPersonFace`, `MediaPersonUserLink`, `MediaFaceReviewDecision`, `MediaIdentityAudit`; dormant unless the People feature is enabled), albums (`MediaAlbum`, `MediaAlbumItem`) and `MediaCurationAudit`.

PRISM-side changes reach the catalogue through `PrismMediaOutboxMessages` in `ApplicationDbContext`.

## File storage

See [storage-hardening.md](storage-hardening.md) and [storage-migration-plan.md](storage-migration-plan.md) for the upload root, storage keys and download endpoints. In short:
- Project and office-report files are stored under the upload root (`IUploadRootProvider`) with relative storage keys.
- DocRepo PDFs are stored under `DocRepo:RootPath` by `LocalDocStorageService`, at `yyyy/MM/{guid}.pdf`.
