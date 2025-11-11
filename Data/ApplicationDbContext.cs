using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using NpgsqlTypes;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Activities;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Helpers;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Models.Projects;

namespace ProjectManagement.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole, string>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<Project> Projects { get; set; } = default!;
        public DbSet<ProjectCategory> ProjectCategories => Set<ProjectCategory>();
        public DbSet<ProjectIpaFact> ProjectIpaFacts => Set<ProjectIpaFact>();
        public DbSet<TechnicalCategory> TechnicalCategories => Set<TechnicalCategory>();
        public DbSet<ProjectLegacyImport> ProjectLegacyImports => Set<ProjectLegacyImport>();
        public DbSet<ProjectSowFact> ProjectSowFacts => Set<ProjectSowFact>();
        public DbSet<ProjectAonFact> ProjectAonFacts => Set<ProjectAonFact>();
        public DbSet<ProjectBenchmarkFact> ProjectBenchmarkFacts => Set<ProjectBenchmarkFact>();
        public DbSet<ProjectCommercialFact> ProjectCommercialFacts => Set<ProjectCommercialFact>();
        public DbSet<ProjectPncFact> ProjectPncFacts => Set<ProjectPncFact>();
        public DbSet<ProjectSupplyOrderFact> ProjectSupplyOrderFacts => Set<ProjectSupplyOrderFact>();
        public DbSet<ProjectProductionCostFact> ProjectProductionCostFacts => Set<ProjectProductionCostFact>();
        public DbSet<ProjectLppRecord> ProjectLppRecords => Set<ProjectLppRecord>();
        public DbSet<ProjectTechStatus> ProjectTechStatuses => Set<ProjectTechStatus>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<TodoItem> TodoItems => Set<TodoItem>();
        public DbSet<Celebration> Celebrations => Set<Celebration>();
        public DbSet<AuthEvent> AuthEvents => Set<AuthEvent>();
        public DbSet<DailyLoginStat> DailyLoginStats => Set<DailyLoginStat>();
        public DbSet<Event> Events => Set<Event>();
        public DbSet<StageTemplate> StageTemplates => Set<StageTemplate>();
        public DbSet<StageDependencyTemplate> StageDependencyTemplates => Set<StageDependencyTemplate>();
        public DbSet<StageChangeRequest> StageChangeRequests => Set<StageChangeRequest>();
        public DbSet<StageChangeLog> StageChangeLogs => Set<StageChangeLog>();
        public DbSet<StageChecklistTemplate> StageChecklistTemplates => Set<StageChecklistTemplate>();
        public DbSet<StageChecklistItemTemplate> StageChecklistItemTemplates => Set<StageChecklistItemTemplate>();
        public DbSet<StageChecklistAudit> StageChecklistAudits => Set<StageChecklistAudit>();
        public DbSet<PlanVersion> PlanVersions => Set<PlanVersion>();
        public DbSet<StagePlan> StagePlans => Set<StagePlan>();
        public DbSet<PlanApprovalLog> PlanApprovalLogs => Set<PlanApprovalLog>();
        public DbSet<ProjectPlanSnapshot> ProjectPlanSnapshots => Set<ProjectPlanSnapshot>();
        public DbSet<ProjectPlanSnapshotRow> ProjectPlanSnapshotRows => Set<ProjectPlanSnapshotRow>();
        public DbSet<ProjectManagement.Models.Plans.PlanRealignmentAudit> PlanRealignmentAudits { get; set; } = default!;
        public DbSet<ProjectStage> ProjectStages => Set<ProjectStage>();
        public DbSet<ProjectPhoto> ProjectPhotos => Set<ProjectPhoto>();
        public DbSet<ProjectVideo> ProjectVideos => Set<ProjectVideo>();
        public DbSet<ProjectTot> ProjectTots => Set<ProjectTot>();
        public DbSet<ProjectTotRequest> ProjectTotRequests => Set<ProjectTotRequest>();
        public DbSet<ProjectMetaChangeRequest> ProjectMetaChangeRequests => Set<ProjectMetaChangeRequest>();
        public DbSet<ProjectComment> ProjectComments => Set<ProjectComment>();
        public DbSet<ProjectCommentAttachment> ProjectCommentAttachments => Set<ProjectCommentAttachment>();
        public DbSet<ProjectCommentMention> ProjectCommentMentions => Set<ProjectCommentMention>();
        public DbSet<Remark> Remarks => Set<Remark>();
        public DbSet<RemarkAudit> RemarkAudits => Set<RemarkAudit>();
        public DbSet<RemarkMention> RemarkMentions => Set<RemarkMention>();
        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<NotificationDispatch> NotificationDispatches => Set<NotificationDispatch>();
        public DbSet<UserNotificationPreference> UserNotificationPreferences => Set<UserNotificationPreference>();
        public DbSet<UserProjectMute> UserProjectMutes => Set<UserProjectMute>();
        public DbSet<ProjectDocument> ProjectDocuments => Set<ProjectDocument>();
        public DbSet<ProjectDocumentRequest> ProjectDocumentRequests => Set<ProjectDocumentRequest>();
        public DbSet<ProjectScheduleSettings> ProjectScheduleSettings => Set<ProjectScheduleSettings>();
        public DbSet<ProjectPlanDuration> ProjectPlanDurations => Set<ProjectPlanDuration>();
        public DbSet<Holiday> Holidays => Set<Holiday>();
        public DbSet<StageShiftLog> StageShiftLogs => Set<StageShiftLog>();
        public DbSet<Status> Statuses => Set<Status>();
        public DbSet<Workflow> Workflows => Set<Workflow>();
        public DbSet<WorkflowStatus> WorkflowStatuses => Set<WorkflowStatus>();
        public DbSet<SponsoringUnit> SponsoringUnits => Set<SponsoringUnit>();
        public DbSet<LineDirectorate> LineDirectorates => Set<LineDirectorate>();
        public DbSet<ProjectAudit> ProjectAudits => Set<ProjectAudit>();
        public DbSet<VisitType> VisitTypes => Set<VisitType>();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<VisitPhoto> VisitPhotos => Set<VisitPhoto>();
        public DbSet<SocialMediaEventType> SocialMediaEventTypes => Set<SocialMediaEventType>();
        public DbSet<SocialMediaPlatform> SocialMediaPlatforms => Set<SocialMediaPlatform>();
        public DbSet<SocialMediaEvent> SocialMediaEvents => Set<SocialMediaEvent>();
        public DbSet<SocialMediaEventPhoto> SocialMediaEventPhotos => Set<SocialMediaEventPhoto>();
        public DbSet<TrainingType> TrainingTypes => Set<TrainingType>();
        public DbSet<Training> Trainings => Set<Training>();
        public DbSet<TrainingCounters> TrainingCounters => Set<TrainingCounters>();
        public DbSet<TrainingProject> TrainingProjects => Set<TrainingProject>();
        public DbSet<TrainingDeleteRequest> TrainingDeleteRequests => Set<TrainingDeleteRequest>();
        public DbSet<TrainingRankCategoryMap> TrainingRankCategoryMaps => Set<TrainingRankCategoryMap>();
        public DbSet<TrainingTrainee> TrainingTrainees => Set<TrainingTrainee>();
        public DbSet<ProliferationYearly> ProliferationYearlies => Set<ProliferationYearly>();
        public DbSet<ProliferationGranular> ProliferationGranularEntries => Set<ProliferationGranular>();
        public DbSet<ProliferationYearPreference> ProliferationYearPreferences => Set<ProliferationYearPreference>();
        public DbSet<IprRecord> IprRecords => Set<IprRecord>();
        public DbSet<IprAttachment> IprAttachments => Set<IprAttachment>();
        public DbSet<FfcCountry> FfcCountries => Set<FfcCountry>();
        public DbSet<FfcRecord> FfcRecords => Set<FfcRecord>();
        public DbSet<FfcProject> FfcProjects => Set<FfcProject>();
        public DbSet<FfcAttachment> FfcAttachments => Set<FfcAttachment>();
        public DbSet<ActivityType> ActivityTypes => Set<ActivityType>();
        public DbSet<Activity> Activities => Set<Activity>();
        public DbSet<ActivityAttachment> ActivityAttachments => Set<ActivityAttachment>();
        public DbSet<ActivityDeleteRequest> ActivityDeleteRequests => Set<ActivityDeleteRequest>();
        public DbSet<Document> Documents => Set<Document>();
        public DbSet<DocRepoExternalLink> DocRepoExternalLinks => Set<DocRepoExternalLink>();
        public DbSet<Tag> Tags => Set<Tag>();
        public DbSet<DocumentTag> DocumentTags => Set<DocumentTag>();
        public DbSet<DocumentText> DocumentTexts => Set<DocumentText>();
        public DbSet<DocumentDeleteRequest> DocumentDeleteRequests => Set<DocumentDeleteRequest>();
        public DbSet<DocRepoAudit> DocRepoAudits => Set<DocRepoAudit>();
        public DbSet<OfficeCategory> OfficeCategories => Set<OfficeCategory>();
        public DbSet<DocumentCategory> DocumentCategories => Set<DocumentCategory>();

        // SECTION: PostgreSQL text search helpers
        public static string TsHeadline(string config, string text, NpgsqlTsQuery query, string options) => throw new NotSupportedException();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            var systemUserCreatedUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            // SECTION: Database function mappings
            builder
                .HasDbFunction(typeof(ApplicationDbContext).GetMethod(nameof(TsHeadline))!)
                .HasName("ts_headline");

            builder.Entity<ApplicationUser>().HasData(
                new ApplicationUser
                {
                    Id = "system",
                    UserName = "system",
                    NormalizedUserName = "SYSTEM",
                    Email = "system@example.local",
                    NormalizedEmail = "SYSTEM@EXAMPLE.LOCAL",
                    EmailConfirmed = true,
                    MustChangePassword = false,
                    FullName = "System Account",
                    Rank = "System",
                    LastLoginUtc = null,
                    LoginCount = 0,
                    CreatedUtc = systemUserCreatedUtc,
                    IsDisabled = false,
                    DisabledUtc = null,
                    DisabledByUserId = null,
                    PendingDeletion = false,
                    DeletionRequestedUtc = null,
                    DeletionRequestedByUserId = null,
                    ShowCelebrationsInCalendar = true,
                    SecurityStamp = "c3f1e44d-21c7-4cd1-8d3a-2212333e2ef2",
                    ConcurrencyStamp = "bb6d6cb5-52dd-432c-95d4-6b6a92d6a0d3",
                    LockoutEnabled = false,
                    LockoutEnd = null,
                    TwoFactorEnabled = false,
                    AccessFailedCount = 0,
                    PhoneNumberConfirmed = false
                });

            builder.Entity<OfficeCategory>(e =>
            {
                e.Property(x => x.Name).HasMaxLength(120).IsRequired();
                e.Property(x => x.SortOrder).HasDefaultValue(100);
                e.Property(x => x.IsActive).HasDefaultValue(true);
                e.HasIndex(x => x.Name).IsUnique();
            });

            builder.Entity<DocumentCategory>(e =>
            {
                e.Property(x => x.Name).HasMaxLength(120).IsRequired();
                e.Property(x => x.SortOrder).HasDefaultValue(100);
                e.Property(x => x.IsActive).HasDefaultValue(true);
                e.HasIndex(x => x.Name).IsUnique();
            });

            builder.Entity<Tag>(e =>
            {
                e.Property(x => x.Name).HasMaxLength(64).IsRequired();
                e.Property(x => x.NormalizedName).HasMaxLength(64).IsRequired();
                e.HasIndex(x => x.Name).IsUnique();
                e.HasIndex(x => x.NormalizedName).IsUnique();
            });

            builder.Entity<DocumentTag>(e =>
            {
                e.HasKey(x => new { x.DocumentId, x.TagId });
                e.HasOne(x => x.Document)
                    .WithMany(x => x.DocumentTags)
                    .HasForeignKey(x => x.DocumentId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Tag)
                    .WithMany(x => x.DocumentTags)
                    .HasForeignKey(x => x.TagId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasIndex(x => new { x.TagId, x.DocumentId });
            });

            builder.Entity<Document>(e =>
            {
                e.Property(x => x.Subject).HasMaxLength(256).IsRequired();
                e.Property(x => x.ReceivedFrom).HasMaxLength(256);
                e.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
                e.Property(x => x.StoragePath).HasMaxLength(260).IsRequired();
                e.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
                e.Property(x => x.MimeType).HasMaxLength(64).HasDefaultValue("application/pdf").IsRequired();
                e.Property(x => x.CreatedByUserId).HasMaxLength(64).IsRequired();
                e.Property(x => x.UpdatedByUserId).HasMaxLength(64);
                e.Property(x => x.DocumentDate).HasColumnType("date");
                e.Property(x => x.IsActive).HasDefaultValue(true);
                // SECTION: Document soft delete configuration
                e.Property(x => x.IsDeleted).HasDefaultValue(false);
                e.Property(x => x.DeletedByUserId).HasMaxLength(64);
                e.Property(x => x.DeleteReason).HasMaxLength(512);
                e.Property(x => x.OcrStatus).HasDefaultValue(DocOcrStatus.None);
                e.Property(x => x.OcrFailureReason).HasMaxLength(1024);
                e.Property(x => x.SearchVector).HasColumnType("tsvector");
                e.HasIndex(x => new { x.OfficeCategoryId, x.DocumentCategoryId });
                e.HasIndex(x => x.Sha256).IsUnique();
                e.HasIndex(x => x.Subject);
                e.HasIndex(x => x.ReceivedFrom);
                e.HasIndex(x => x.DocumentDate);
                e.HasIndex(x => x.IsDeleted);
                e.HasMany(x => x.ExternalLinks)
                    .WithOne(x => x.Document)
                    .HasForeignKey(x => x.DocumentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<DocRepoExternalLink>(e =>
            {
                e.ToTable("DocRepoExternalLinks");
                e.HasKey(x => x.Id);
                e.Property(x => x.SourceModule).HasMaxLength(64).IsRequired();
                e.Property(x => x.SourceItemId).HasMaxLength(128).IsRequired();
                e.HasIndex(x => new { x.SourceModule, x.SourceItemId });
            });

            builder.Entity<DocumentText>(e =>
            {
                e.ToTable("DocRepoDocumentTexts");
                e.HasKey(x => x.DocumentId);
                e.Property(x => x.OcrText).HasColumnType("text");
                e.Property(x => x.UpdatedAtUtc)
                    .HasColumnType("timestamp without time zone")
                    .HasDefaultValueSql("now() at time zone 'utc'");
                e.HasOne(x => x.Document)
                    .WithOne(x => x.DocumentText)
                    .HasForeignKey<DocumentText>(x => x.DocumentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<DocumentDeleteRequest>(e =>
            {
                e.Property(x => x.Reason).HasMaxLength(512);
                e.HasIndex(x => new { x.DocumentId, x.ApprovedAtUtc })
                    .HasDatabaseName("IX_DeleteReq_Doc_PendingFirst");
                e.HasOne(x => x.Document)
                    .WithMany()
                    .HasForeignKey(x => x.DocumentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<DocRepoAudit>(e =>
            {
                e.Property(x => x.EventType).HasMaxLength(64);
                e.Property(x => x.ActorUserId).HasMaxLength(450).IsRequired();
                e.Property(x => x.DetailsJson).HasColumnType("jsonb");
                e.HasIndex(x => new { x.DocumentId, x.OccurredAtUtc });
            });

            builder.Entity<Project>(e =>
            {
                e.Property(x => x.Name).HasMaxLength(100).IsRequired();
                e.HasIndex(x => x.Name);
                e.Property(x => x.CaseFileNumber).HasMaxLength(64);
                e.HasIndex(x => x.CaseFileNumber)
                    .HasDatabaseName("UX_Projects_CaseFileNumber")
                    .IsUnique()
                    .HasFilter("\"CaseFileNumber\" IS NOT NULL");
                ConfigureRowVersion(e);
                e.Property(x => x.CreatedByUserId).HasMaxLength(64).IsRequired();
                e.Property(x => x.ArmService).HasMaxLength(200);
                e.Property(x => x.YearOfDevelopment);
                e.Property(x => x.CostLakhs).HasColumnType("numeric(18,2)");
                e.Property(x => x.CoverPhotoVersion).HasDefaultValue(1).IsConcurrencyToken();
                e.Property(x => x.FeaturedVideoVersion).HasDefaultValue(1).IsConcurrencyToken();
                e.Property(x => x.LifecycleStatus)
                    .HasConversion<string>()
                    .HasMaxLength(32)
                    .HasDefaultValue(ProjectLifecycleStatus.Active)
                    .HasSentinel(ProjectLifecycleStatus.Active)
                    .IsRequired();
                e.Property(x => x.IsLegacy).HasDefaultValue(false);
                e.Property(x => x.CompletedOn).HasColumnType("date");
                e.Property(x => x.CompletedYear);
                e.Property(x => x.CancelledOn).HasColumnType("date");
                e.Property(x => x.CancelReason).HasMaxLength(512);
                e.HasIndex(x => x.LifecycleStatus);
                e.HasIndex(x => x.IsLegacy);
                e.HasIndex(x => x.CompletedYear);
                e.HasMany(x => x.Photos)
                    .WithOne(x => x.Project)
                    .HasForeignKey(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne<ProjectPhoto>()
                    .WithMany()
                    .HasForeignKey(x => x.CoverPhotoId)
                    .OnDelete(DeleteBehavior.SetNull);
                e.HasMany(x => x.Videos)
                    .WithOne(x => x.Project)
                    .HasForeignKey(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne<ProjectVideo>()
                    .WithMany()
                    .HasForeignKey(x => x.FeaturedVideoId)
                    .OnDelete(DeleteBehavior.SetNull);
                e.HasOne(x => x.Category)
                    .WithMany(x => x.Projects)
                    .HasForeignKey(x => x.CategoryId)
                    .OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.TechnicalCategory)
                    .WithMany(x => x.Projects)
                    .HasForeignKey(x => x.TechnicalCategoryId)
                    .OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.SponsoringUnit)
                    .WithMany(x => x.Projects)
                    .HasForeignKey(x => x.SponsoringUnitId)
                    .OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.SponsoringLineDirectorate)
                    .WithMany(x => x.Projects)
                    .HasForeignKey(x => x.SponsoringLineDirectorateId)
                    .OnDelete(DeleteBehavior.Restrict);
                e.Property(x => x.PlanApprovedByUserId).HasMaxLength(450);
                e.HasOne(x => x.PlanApprovedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.PlanApprovedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);

                e.Property(x => x.IsArchived).HasDefaultValue(false);
                e.Property(x => x.ArchivedAt);
                e.Property(x => x.ArchivedByUserId).HasMaxLength(450);
                e.Property(x => x.IsDeleted).HasDefaultValue(false);
                e.Property(x => x.DeletedAt);
                e.Property(x => x.DeletedByUserId).HasMaxLength(450);
                e.Property(x => x.DeleteReason).HasMaxLength(512);
                e.Property(x => x.DeleteMethod).HasMaxLength(32);
                e.Property(x => x.DeleteApprovedByUserId).HasMaxLength(450);

                e.HasIndex(x => new { x.IsDeleted, x.IsArchived })
                    .HasDatabaseName("IX_Projects_IsDeleted_IsArchived");

                if (Database.IsSqlServer())
                {
                    e.HasIndex(x => x.IsDeleted)
                        .HasDatabaseName("IX_Projects_IsDeleted_Filtered")
                        .HasFilter("[IsDeleted] = 1");
                }
                else if (Database.IsNpgsql())
                {
                    e.HasIndex(x => x.IsDeleted)
                        .HasDatabaseName("IX_Projects_IsDeleted_Filtered")
                        .HasFilter("\"IsDeleted\" = TRUE");
                }
                else
                {
                    e.HasIndex(x => x.IsDeleted)
                        .HasDatabaseName("IX_Projects_IsDeleted");
                }
            });

            builder.Entity<ProjectLegacyImport>(e =>
            {
                e.ToTable("ProjectLegacyImports");
                e.Property(x => x.ImportedAtUtc).HasColumnType("timestamp without time zone");
                e.Property(x => x.ImportedByUserId).HasMaxLength(450).IsRequired();
                e.Property(x => x.SourceFileHashSha256).HasMaxLength(128);
                e.HasIndex(x => new { x.ProjectCategoryId, x.TechnicalCategoryId })
                    .IsUnique()
                    .HasDatabaseName("UX_ProjectLegacyImport_Category_Tech");
                e.HasOne(x => x.ProjectCategory)
                    .WithMany()
                    .HasForeignKey(x => x.ProjectCategoryId)
                    .OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.TechnicalCategory)
                    .WithMany()
                    .HasForeignKey(x => x.TechnicalCategoryId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<ProjectAudit>(e =>
            {
                e.Property(x => x.Action)
                    .IsRequired()
                    .HasMaxLength(32);
                e.Property(x => x.PerformedByUserId)
                    .IsRequired()
                    .HasMaxLength(450);
                e.Property(x => x.PerformedAt)
                    .HasDefaultValueSql("now() at time zone 'utc'");
                e.Property(x => x.Reason)
                    .HasMaxLength(512);
                e.Property(x => x.MetadataJson)
                    .HasMaxLength(4000);
                e.HasIndex(x => new { x.ProjectId, x.PerformedAt })
                    .HasDatabaseName("IX_ProjectAudit_ProjectId_PerformedAt");

                e.HasOne(x => x.Project)
                    .WithMany()
                    .HasForeignKey(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.PerformedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.PerformedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);

                if (Database.IsSqlServer())
                {
                    e.Property(x => x.PerformedAt)
                        .HasDefaultValueSql("GETUTCDATE()");
                }
                else if (!Database.IsNpgsql())
                {
                    e.Property(x => x.PerformedAt)
                        .HasDefaultValueSql("CURRENT_TIMESTAMP");
                }
            });

            builder.Entity<ProjectPhoto>(e =>
            {
                e.Property(x => x.StorageKey).HasMaxLength(260).IsRequired();
                e.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
                e.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
                e.Property(x => x.Caption).HasMaxLength(512);
                e.Property(x => x.TotId).IsRequired(false);
                e.Property(x => x.Version).HasDefaultValue(1).IsConcurrencyToken();
                e.Property(x => x.Ordinal).HasDefaultValue(1);
                e.Property(x => x.CreatedUtc).HasDefaultValueSql("now() at time zone 'utc'");
                e.Property(x => x.UpdatedUtc).HasDefaultValueSql("now() at time zone 'utc'");
                e.HasIndex(x => new { x.ProjectId, x.Ordinal }).IsUnique();
                e.HasIndex(x => new { x.ProjectId, x.TotId });

                if (Database.IsSqlServer())
                {
                    e.HasIndex(nameof(ProjectPhoto.ProjectId))
                        .HasDatabaseName("UX_ProjectPhotos_Cover")
                        .IsUnique()
                        .HasFilter("[IsCover] = 1");
                    e.Property(x => x.CreatedUtc).HasDefaultValueSql("GETUTCDATE()");
                    e.Property(x => x.UpdatedUtc).HasDefaultValueSql("GETUTCDATE()");
                }
                else if (Database.IsNpgsql())
                {
                    e.HasIndex(nameof(ProjectPhoto.ProjectId))
                        .HasDatabaseName("UX_ProjectPhotos_Cover")
                        .IsUnique()
                        .HasFilter("\"IsCover\" = TRUE");
                }
                else
                {
                    e.HasIndex(x => x.ProjectId).HasDatabaseName("IX_ProjectPhotos_ProjectId");
                    e.Property(x => x.CreatedUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                    e.Property(x => x.UpdatedUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                }

                e.HasOne(x => x.Tot)
                    .WithMany()
                    .HasForeignKey(x => x.TotId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<VisitType>(e =>
            {
                ConfigureRowVersion(e);
                e.Property(x => x.Name).HasMaxLength(128).IsRequired();
                e.HasIndex(x => x.Name).IsUnique();
                e.Property(x => x.Description).HasMaxLength(512);
                e.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired();
                e.Property(x => x.LastModifiedByUserId).HasMaxLength(450);
                e.Property(x => x.IsActive).HasDefaultValue(true);
                e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("now() at time zone 'utc'");

                if (Database.IsNpgsql())
                {
                    e.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
                    e.Property(x => x.LastModifiedAtUtc).HasColumnType("timestamp with time zone");
                }

                if (Database.IsSqlServer())
                {
                    e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("GETUTCDATE()");
                }
                else if (!Database.IsNpgsql())
                {
                    e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                }
            });

            builder.Entity<Visit>(e =>
            {
                ConfigureRowVersion(e);
                e.Property(x => x.DateOfVisit).HasColumnType("date").IsRequired();
                e.Property(x => x.VisitorName).HasMaxLength(200).IsRequired();
                e.Property(x => x.Strength).IsRequired();
                e.Property(x => x.Remarks).HasMaxLength(2000);
                e.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired();
                e.Property(x => x.LastModifiedByUserId).HasMaxLength(450);
                e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("now() at time zone 'utc'");
                e.Property(x => x.LastModifiedAtUtc);

                if (Database.IsNpgsql())
                {
                    e.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
                    e.Property(x => x.LastModifiedAtUtc).HasColumnType("timestamp with time zone");
                }

                e.HasIndex(x => x.DateOfVisit).HasDatabaseName("IX_Visits_DateOfVisit");
                e.HasIndex(x => x.VisitTypeId).HasDatabaseName("IX_Visits_VisitTypeId");

                e.HasOne(x => x.VisitType)
                    .WithMany(x => x.Visits)
                    .HasForeignKey(x => x.VisitTypeId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.CoverPhoto)
                    .WithMany()
                    .HasForeignKey(x => x.CoverPhotoId)
                    .OnDelete(DeleteBehavior.ClientSetNull); // avoid circular cascade when deleting a visit with a cover photo

                if (Database.IsSqlServer())
                {
                    e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("GETUTCDATE()");
                }
                else if (!Database.IsNpgsql())
                {
                    e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                }
            });

            builder.Entity<VisitPhoto>(e =>
            {
                e.Property(x => x.StorageKey).HasMaxLength(260).IsRequired();
                e.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
                e.Property(x => x.Caption).HasMaxLength(512);
                e.Property(x => x.VersionStamp).HasMaxLength(64).IsRequired();
                e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("now() at time zone 'utc'");

                if (Database.IsNpgsql())
                {
                    e.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
                }

                e.HasIndex(x => new { x.VisitId, x.CreatedAtUtc }).HasDatabaseName("IX_VisitPhotos_VisitId_CreatedAtUtc");

                e.HasOne(x => x.Visit)
                    .WithMany(x => x.Photos)
                    .HasForeignKey(x => x.VisitId)
                    .OnDelete(DeleteBehavior.Cascade);

                if (Database.IsSqlServer())
                {
                    e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("GETUTCDATE()");
                }
                else if (!Database.IsNpgsql())
                {
                    e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                }
            });

            builder.Entity<ProliferationYearly>(b =>
            {
                b.ToTable("ProliferationYearly");
                b.HasKey(x => x.Id);
                ConfigureRowVersion(b);
                b.Property(x => x.Source).HasConversion<int>();
                b.HasIndex(x => new { x.ProjectId, x.Source, x.Year })
                    .HasDatabaseName("IX_ProlifYearly_Project_Source_Year");
            });

            builder.Entity<ProliferationGranular>(b =>
            {
                b.ToTable("ProliferationGranular");
                b.HasKey(x => x.Id);
                ConfigureRowVersion(b);
                b.Property(x => x.Source).HasConversion<int>();
                b.Property(x => x.UnitName).HasMaxLength(200);
                b.HasIndex(x => new { x.ProjectId, x.Source, x.ProliferationDate });
            });

            builder.Entity<ProliferationYearPreference>(b =>
            {
                b.ToTable("ProliferationYearPreference");
                b.HasKey(x => x.Id);
                b.Property(x => x.Source).HasConversion<int>();
                b.Property(x => x.Mode).HasConversion<int>();
                b.HasIndex(x => new { x.ProjectId, x.Source, x.Year })
                    .IsUnique()
                    .HasDatabaseName("UX_ProlifYearPref_Project_Source_Year");
            });

            builder.Entity<IprRecord>(entity =>
            {
                ConfigureRowVersion(entity);
                entity.ToTable("IprRecords");
                entity.Property(x => x.IprFilingNumber).HasMaxLength(128).IsRequired();
                entity.Property(x => x.Title).HasMaxLength(256);
                entity.Property(x => x.Notes).HasMaxLength(2000);
                entity.Property(x => x.FiledBy).HasMaxLength(128);
                entity.Property(x => x.Type)
                    .HasConversion<string>()
                    .HasMaxLength(32)
                    .IsRequired();
                entity.Property(x => x.Status)
                    .HasConversion<string>()
                    .HasMaxLength(32)
                    .IsRequired();
                entity.HasIndex(x => x.ProjectId);
                entity.HasIndex(x => x.Type);
                entity.HasIndex(x => x.Status);
                entity.HasIndex(x => x.IprFilingNumber);
                entity.HasIndex(x => new { x.IprFilingNumber, x.Type })
                    .HasDatabaseName("UX_IprRecords_FilingNumber_Type")
                    .IsUnique();
                entity.HasOne(x => x.Project)
                    .WithMany()
                    .HasForeignKey(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.SetNull);
                entity.HasMany(x => x.Attachments)
                    .WithOne(x => x.Record)
                    .HasForeignKey(x => x.IprRecordId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<IprAttachment>(entity =>
            {
                ConfigureRowVersion(entity);
                entity.ToTable("IprAttachments");
                entity.Property(x => x.StorageKey).HasMaxLength(260).IsRequired();
                entity.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
                entity.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
                entity.Property(x => x.UploadedByUserId).HasMaxLength(450).IsRequired();
                entity.HasIndex(x => x.IprRecordId);
                entity.HasOne(x => x.UploadedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.UploadedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.ArchivedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.ArchivedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });


            builder.Entity<FfcCountry>(entity =>
            {
                ConfigureRowVersion(entity);
                entity.ToTable("FfcCountries");
                entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
                entity.Property(x => x.IsoCode).HasMaxLength(3).IsRequired();
                entity.Property(x => x.IsActive).HasDefaultValue(true);
                entity.Property(x => x.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
                entity.Property(x => x.UpdatedAt).HasDefaultValueSql("now() at time zone 'utc'");

                if (Database.IsNpgsql())
                {
                    entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
                    entity.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
                }

                entity.HasIndex(x => x.Name)
                    .IsUnique()
                    .HasDatabaseName("UX_FfcCountries_Name");

                entity.HasIndex(x => x.IsoCode)
                    .IsUnique()
                    .HasDatabaseName("UX_FfcCountries_IsoCode");
            });

            builder.Entity<FfcRecord>(entity =>
            {
                ConfigureRowVersion(entity);
                entity.ToTable("FfcRecords");
                entity.Property(x => x.Year).HasColumnType("smallint");
                entity.Property(x => x.IpaYes).HasDefaultValue(false);
                entity.Property(x => x.GslYes).HasDefaultValue(false);
                entity.Property(x => x.DeliveryYes).HasDefaultValue(false);
                entity.Property(x => x.InstallationYes).HasDefaultValue(false);
                entity.Property(x => x.IsDeleted).HasDefaultValue(false);
                entity.Property(x => x.CreatedByUserId).HasMaxLength(450);
                entity.Property(x => x.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
                entity.Property(x => x.UpdatedAt).HasDefaultValueSql("now() at time zone 'utc'");

                if (Database.IsNpgsql())
                {
                    entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
                    entity.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
                }

                entity.HasIndex(x => new { x.CountryId, x.Year })
                    .HasDatabaseName("IX_FfcRecords_CountryId_Year");
                entity.HasIndex(x => new { x.IpaYes, x.GslYes, x.DeliveryYes, x.InstallationYes })
                    .HasDatabaseName("IX_FfcRecords_StatusFlags");

                entity.HasOne(x => x.Country)
                    .WithMany(x => x.Records)
                    .HasForeignKey(x => x.CountryId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasMany(x => x.Projects)
                    .WithOne(x => x.Record)
                    .HasForeignKey(x => x.FfcRecordId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(x => x.Attachments)
                    .WithOne(x => x.Record)
                    .HasForeignKey(x => x.FfcRecordId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.ToTable(tb =>
                {
                    tb.HasCheckConstraint("CK_FfcRecords_IpaDateRequiresFlag", "\"IpaDate\" IS NULL OR \"IpaYes\" = TRUE");
                    tb.HasCheckConstraint("CK_FfcRecords_GslDateRequiresFlag", "\"GslDate\" IS NULL OR \"GslYes\" = TRUE");
                    tb.HasCheckConstraint("CK_FfcRecords_DeliveryDateRequiresFlag", "\"DeliveryDate\" IS NULL OR \"DeliveryYes\" = TRUE");
                    tb.HasCheckConstraint("CK_FfcRecords_InstallationDateRequiresFlag", "\"InstallationDate\" IS NULL OR \"InstallationYes\" = TRUE");
                });
            });

            builder.Entity<FfcProject>(entity =>
            {
                entity.ToTable("FfcProjects");
                entity.Property(x => x.Name).HasMaxLength(256).IsRequired();
                entity.Property(x => x.Remarks).HasColumnType("text");
                entity.Property(x => x.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
                entity.Property(x => x.UpdatedAt).HasDefaultValueSql("now() at time zone 'utc'");

                if (Database.IsNpgsql())
                {
                    entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
                    entity.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
                }

                entity.HasIndex(x => x.FfcRecordId)
                    .HasDatabaseName("IX_FfcProjects_FfcRecordId");
                entity.HasIndex(x => x.LinkedProjectId)
                    .HasDatabaseName("IX_FfcProjects_LinkedProjectId");

                entity.HasOne(x => x.Record)
                    .WithMany(x => x.Projects)
                    .HasForeignKey(x => x.FfcRecordId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.LinkedProject)
                    .WithMany()
                    .HasForeignKey(x => x.LinkedProjectId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<FfcAttachment>(entity =>
            {
                entity.ToTable("FfcAttachments");

                var kindConverter = new ValueConverter<FfcAttachmentKind, string>(
                    static value => value.ToString().ToUpperInvariant(),
                    static value => Enum.Parse<FfcAttachmentKind>(value, true));

                var kindComparer = new ValueComparer<FfcAttachmentKind>(
                    static (left, right) => left == right,
                    static value => value.GetHashCode(),
                    static value => value);

                entity.Property(x => x.Kind)
                    .HasConversion(kindConverter, kindComparer)
                    .HasMaxLength(16)
                    .IsRequired();

                entity.Property(x => x.FilePath).HasMaxLength(1024).IsRequired();
                entity.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
                entity.Property(x => x.SizeBytes).HasColumnType("bigint");
                entity.Property(x => x.ChecksumSha256).HasMaxLength(64);
                entity.Property(x => x.Caption).HasMaxLength(256);
                entity.Property(x => x.UploadedByUserId).HasMaxLength(450);
                entity.Property(x => x.UploadedAt).HasDefaultValueSql("now() at time zone 'utc'");

                if (Database.IsNpgsql())
                {
                    entity.Property(x => x.UploadedAt).HasColumnType("timestamp with time zone");
                }

                entity.HasIndex(x => x.FfcRecordId)
                    .HasDatabaseName("IX_FfcAttachments_FfcRecordId");
                entity.HasIndex(x => x.Kind)
                    .HasDatabaseName("IX_FfcAttachments_Kind");

                entity.HasOne(x => x.Record)
                    .WithMany(x => x.Attachments)
                    .HasForeignKey(x => x.FfcRecordId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.ToTable(tb =>
                {
                    tb.HasCheckConstraint("CK_FfcAttachments_SizeBytes", "\"SizeBytes\" >= 0");
                });
            });


            builder.Entity<SocialMediaEventType>(e =>
            {
                ConfigureRowVersion(e);
                e.ToTable("SocialMediaEventTypes");
                e.Property(x => x.Name).HasMaxLength(128).IsRequired();
                e.HasIndex(x => x.Name).IsUnique();
                e.Property(x => x.Description).HasMaxLength(512);
                e.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired();
                e.Property(x => x.LastModifiedByUserId).HasMaxLength(450);
                e.Property(x => x.IsActive).HasDefaultValue(true);
                e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("now() at time zone 'utc'");

                if (Database.IsNpgsql())
                {
                    e.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
                    e.Property(x => x.LastModifiedAtUtc).HasColumnType("timestamp with time zone");
                }

                if (Database.IsSqlServer())
                {
                    e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("GETUTCDATE()");
                }
                else if (!Database.IsNpgsql())
                {
                    e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                }

                var typeSeedCreatedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
                e.HasData(
                    new SocialMediaEventType
                    {
                        Id = new Guid("9ddf8646-7070-4f7a-9fa0-8cb19f4a0d5b"),
                        Name = "Campaign Launch",
                        Description = "Coverage for new campaign announcements and kick-off posts.",
                        IsActive = true,
                        CreatedAtUtc = typeSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("6f9c44f5-99f2-47f6-9473-d0b6a324b825").ToByteArray()
                    },
                    new SocialMediaEventType
                    {
                        Id = new Guid("fa2f60fa-7d4f-4f60-a84b-e8f64dce0b73"),
                        Name = "Milestone Update",
                        Description = "Highlights of major delivery milestones shared online.",
                        IsActive = true,
                        CreatedAtUtc = typeSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("0bb8e628-9d48-47da-9305-2b6e6f8da5c6").ToByteArray()
                    },
                    new SocialMediaEventType
                    {
                        Id = new Guid("0b35f77a-4ef6-4a0a-85f9-9fa0b1b0c353"),
                        Name = "Community Engagement",
                        Description = "Stories focused on community outreach and engagement.",
                        IsActive = true,
                        CreatedAtUtc = typeSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("6b1a659c-f4cb-4c90-8a36-8ff6b9355e7d").ToByteArray()
                    });
            });

            builder.Entity<SocialMediaPlatform>(e =>
            {
                ConfigureRowVersion(e);
                e.ToTable("SocialMediaPlatforms");
                e.Property(x => x.Name).HasMaxLength(128).IsRequired();
                e.HasIndex(x => x.Name).IsUnique();
                e.Property(x => x.Description).HasMaxLength(512);
                e.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired();
                e.Property(x => x.LastModifiedByUserId).HasMaxLength(450);
                e.Property(x => x.IsActive).HasDefaultValue(true);
                e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("now() at time zone 'utc'");

                if (Database.IsNpgsql())
                {
                    e.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
                    e.Property(x => x.LastModifiedAtUtc).HasColumnType("timestamp with time zone");
                }

                if (Database.IsSqlServer())
                {
                    e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("GETUTCDATE()");
                }
                else if (!Database.IsNpgsql())
                {
                    e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                }
            });

            builder.Entity<SocialMediaEvent>(e =>
            {
                ConfigureRowVersion(e);
                e.ToTable("SocialMediaEvents");
                e.Property(x => x.DateOfEvent).HasColumnType("date").IsRequired();
                e.Property(x => x.Title).HasMaxLength(200).IsRequired();
                e.Property(x => x.Description).HasMaxLength(2000);
                e.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired();
                e.Property(x => x.LastModifiedByUserId).HasMaxLength(450);
                e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("now() at time zone 'utc'");

                if (Database.IsNpgsql())
                {
                    e.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
                    e.Property(x => x.LastModifiedAtUtc).HasColumnType("timestamp with time zone");
                }

                e.HasIndex(x => x.DateOfEvent).HasDatabaseName("IX_SocialMediaEvents_DateOfEvent");
                e.HasIndex(x => x.SocialMediaEventTypeId).HasDatabaseName("IX_SocialMediaEvents_SocialMediaEventTypeId");
                e.HasIndex(x => x.SocialMediaPlatformId).HasDatabaseName("IX_SocialMediaEvents_SocialMediaPlatformId");

                e.HasOne(x => x.SocialMediaEventType)
                    .WithMany(x => x.Events)
                    .HasForeignKey(x => x.SocialMediaEventTypeId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.SocialMediaPlatform)
                    .WithMany()
                    .HasForeignKey(x => x.SocialMediaPlatformId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.CoverPhoto)
                    .WithMany()
                    .HasForeignKey(x => x.CoverPhotoId)
                    .OnDelete(DeleteBehavior.ClientSetNull);

                if (Database.IsSqlServer())
                {
                    e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("GETUTCDATE()");
                }
                else if (!Database.IsNpgsql())
                {
                    e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                }
            });

            builder.Entity<SocialMediaEventPhoto>(e =>
            {
                ConfigureRowVersion(e);
                e.ToTable("SocialMediaEventPhotos");
                e.Property(x => x.StorageKey).HasMaxLength(260).IsRequired();
                e.Property(x => x.StoragePath).HasMaxLength(512).HasDefaultValue(string.Empty).IsRequired();
                e.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
                e.Property(x => x.Caption).HasMaxLength(512);
                e.Property(x => x.VersionStamp).HasMaxLength(64).IsRequired();
                e.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired();
                e.Property(x => x.LastModifiedByUserId).HasMaxLength(450);
                e.Property(x => x.IsCover).HasDefaultValue(false);
                e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("now() at time zone 'utc'");

                if (Database.IsNpgsql())
                {
                    e.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
                    e.Property(x => x.LastModifiedAtUtc).HasColumnType("timestamp with time zone");
                }

                e.HasIndex(x => new { x.SocialMediaEventId, x.CreatedAtUtc })
                    .HasDatabaseName("IX_SocialMediaEventPhotos_EventId_CreatedAtUtc");

                var coverIndex = e.HasIndex(x => new { x.SocialMediaEventId, x.IsCover })
                    .IsUnique()
                    .HasDatabaseName("UX_SocialMediaEventPhotos_IsCover");

                if (Database.IsSqlServer())
                {
                    e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("GETUTCDATE()");
                    coverIndex.HasFilter("[IsCover] = 1");
                }
                else if (Database.IsNpgsql())
                {
                    coverIndex.HasFilter("\"IsCover\" = TRUE");
                }
                else
                {
                    e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                }

                e.HasOne(x => x.SocialMediaEvent)
                    .WithMany(x => x.Photos)
                    .HasForeignKey(x => x.SocialMediaEventId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<ProjectVideo>(e =>
            {
                e.Property(x => x.StorageKey).HasMaxLength(260).IsRequired();
                e.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
                e.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
                e.Property(x => x.Title).HasMaxLength(200);
                e.Property(x => x.Description).HasMaxLength(512);
                e.Property(x => x.PosterStorageKey).HasMaxLength(260);
                e.Property(x => x.PosterContentType).HasMaxLength(128);
                e.Property(x => x.Version).HasDefaultValue(1).IsConcurrencyToken();
                e.Property(x => x.Ordinal).HasDefaultValue(1);
                e.Property(x => x.CreatedUtc).HasDefaultValueSql("now() at time zone 'utc'");
                e.Property(x => x.UpdatedUtc).HasDefaultValueSql("now() at time zone 'utc'");
                e.HasIndex(x => new { x.ProjectId, x.Ordinal }).IsUnique();
                if (Database.IsSqlServer())
                {
                    e.Property(x => x.CreatedUtc).HasDefaultValueSql("GETUTCDATE()");
                    e.Property(x => x.UpdatedUtc).HasDefaultValueSql("GETUTCDATE()");
                }
                else if (!Database.IsNpgsql())
                {
                    e.Property(x => x.CreatedUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                    e.Property(x => x.UpdatedUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                }
            });

            builder.Entity<ProjectTot>(e =>
            {
                var statusBuilder = e.Property(x => x.Status)
                    .HasConversion<string>()
                    .HasMaxLength(32)
                    .IsRequired();

                statusBuilder.ValueGeneratedNever();
                e.Property(x => x.StartedOn).HasColumnType("date");
                e.Property(x => x.CompletedOn).HasColumnType("date");
                e.Property(x => x.MetDetails).HasMaxLength(2000);
                e.Property(x => x.MetCompletedOn).HasColumnType("date");
                e.Property(x => x.FirstProductionModelManufactured).IsRequired(false);
                e.Property(x => x.FirstProductionModelManufacturedOn).HasColumnType("date");
                e.Property(x => x.LastApprovedByUserId).HasMaxLength(450);
                e.HasIndex(x => x.ProjectId).IsUnique();
                e.HasOne(x => x.Project)
                    .WithOne(x => x.Tot)
                    .HasForeignKey<ProjectTot>(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.LastApprovedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.LastApprovedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<ProjectTotRequest>(e =>
            {
                ConfigureRowVersion(e);
                e.Property(x => x.ProposedStatus)
                    .HasConversion<string>()
                    .HasMaxLength(32)
                    .IsRequired();
                e.Property(x => x.DecisionState)
                    .HasConversion<string>()
                    .HasMaxLength(32)
                    .IsRequired();
                e.Property(x => x.ProposedMetDetails).HasMaxLength(2000);
                e.Property(x => x.ProposedMetCompletedOn).HasColumnType("date");
                e.Property(x => x.ProposedFirstProductionModelManufactured).IsRequired(false);
                e.Property(x => x.ProposedFirstProductionModelManufacturedOn).HasColumnType("date");
                e.Property(x => x.SubmittedByUserId).HasMaxLength(450).IsRequired();
                e.HasIndex(x => x.ProjectId).IsUnique();
                e.HasOne(x => x.Project)
                    .WithOne(x => x.TotRequest)
                    .HasForeignKey<ProjectTotRequest>(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.SubmittedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.SubmittedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.DecidedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.DecidedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<ProjectDocument>(e =>
            {
                ConfigureRowVersion(e);
                e.Property(x => x.ProjectId).IsRequired();
                e.Property(x => x.StageId).IsRequired(false);
                e.Property(x => x.RequestId).IsRequired(false);
                e.Property(x => x.Title).HasMaxLength(200).IsRequired();
                e.Property(x => x.Description).HasMaxLength(2000);
                e.Property(x => x.StorageKey).HasMaxLength(260).IsRequired();
                e.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
                e.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
                e.Property(x => x.FileSize).IsRequired();
                e.Property(x => x.Status).HasConversion<string>()
                    .HasMaxLength(32)
                    .HasDefaultValue(ProjectDocumentStatus.Published)
                    .HasSentinel(ProjectDocumentStatus.Published)
                    .IsRequired();
                e.Property(x => x.FileStamp).HasDefaultValue(0).IsRequired();
                e.Property(x => x.DocRepoDocumentId).IsRequired(false);
                e.Property(x => x.UploadedByUserId).HasMaxLength(450).IsRequired();
                e.Property(x => x.IsArchived).HasDefaultValue(false);
                e.Property(x => x.ArchivedAtUtc).IsRequired(false);
                e.Property(x => x.ArchivedByUserId).HasMaxLength(450);
                e.HasIndex(x => new { x.ProjectId, x.StageId, x.IsArchived });
                e.HasIndex(x => x.ProjectId);
                e.HasIndex(x => new { x.ProjectId, x.TotId });
                e.HasIndex(x => x.DocRepoDocumentId);
                e.HasOne(x => x.Project)
                    .WithMany()
                    .HasForeignKey(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Stage)
                    .WithMany()
                    .HasForeignKey(x => x.StageId)
                    .OnDelete(DeleteBehavior.SetNull);
                e.HasOne(x => x.Request)
                    .WithOne(x => x.Document)
                    .HasForeignKey<ProjectDocument>(x => x.RequestId)
                    .OnDelete(DeleteBehavior.SetNull);
                e.HasOne(x => x.DocRepoDocument)
                    .WithMany()
                    .HasForeignKey(x => x.DocRepoDocumentId)
                    .OnDelete(DeleteBehavior.SetNull);
                e.HasOne(x => x.Tot)
                    .WithMany()
                    .HasForeignKey(x => x.TotId)
                    .OnDelete(DeleteBehavior.SetNull);
                e.HasOne(x => x.UploadedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.UploadedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.ArchivedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.ArchivedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);

                if (Database.IsSqlServer())
                {
                    e.Property(x => x.UploadedAtUtc).HasDefaultValueSql("GETUTCDATE()");
                }
                else if (Database.IsNpgsql())
                {
                    e.Property(x => x.UploadedAtUtc).HasDefaultValueSql("now() at time zone 'utc'");
                }
                else
                {
                    e.Property(x => x.UploadedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                }

                e.ToTable(tb =>
                {
                    tb.HasCheckConstraint("ck_projectdocuments_filesize", "\"FileSize\" >= 0");
                });
            });

            builder.Entity<Notification>(e =>
            {
                e.Property(x => x.RecipientUserId).HasMaxLength(450).IsRequired();
                e.Property(x => x.Module).HasMaxLength(64);
                e.Property(x => x.EventType).HasMaxLength(128);
                e.Property(x => x.ScopeType).HasMaxLength(64);
                e.Property(x => x.ScopeId).HasMaxLength(128);
                e.Property(x => x.ProjectId).IsRequired(false);
                e.Property(x => x.ActorUserId).HasMaxLength(450);
                e.Property(x => x.Fingerprint).HasMaxLength(128);
                e.Property(x => x.Route).HasMaxLength(2048);
                e.Property(x => x.Title).HasMaxLength(200);
                e.Property(x => x.Summary).HasMaxLength(2000);
                e.HasIndex(x => new { x.RecipientUserId, x.CreatedUtc });
                e.HasIndex(x => new { x.RecipientUserId, x.SeenUtc, x.CreatedUtc });
                e.HasIndex(x => new { x.RecipientUserId, x.ReadUtc, x.CreatedUtc });
                var fingerprintIndex = e.HasIndex(x => x.Fingerprint);

                if (Database.IsSqlServer())
                {
                    fingerprintIndex.HasFilter("[Fingerprint] IS NOT NULL");
                    e.Property(x => x.CreatedUtc).HasDefaultValueSql("GETUTCDATE()");
                }
                else if (Database.IsNpgsql())
                {
                    fingerprintIndex.HasFilter("\"Fingerprint\" IS NOT NULL");
                    e.Property(x => x.CreatedUtc).HasDefaultValueSql("now() at time zone 'utc'");
                }
                else
                {
                    fingerprintIndex.HasFilter("Fingerprint IS NOT NULL");
                    e.Property(x => x.CreatedUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                }

                e.HasOne(x => x.SourceDispatch)
                    .WithMany()
                    .HasForeignKey(x => x.SourceDispatchId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<NotificationDispatch>(e =>
            {
                e.Property(x => x.RecipientUserId).HasMaxLength(450).IsRequired();
                e.Property(x => x.Kind)
                    .HasConversion<string>()
                    .HasMaxLength(64)
                    .IsRequired();
                e.Property(x => x.Module).HasMaxLength(64);
                e.Property(x => x.EventType).HasMaxLength(128);
                e.Property(x => x.ScopeType).HasMaxLength(64);
                e.Property(x => x.ScopeId).HasMaxLength(128);
                e.Property(x => x.ProjectId).IsRequired(false);
                e.Property(x => x.ActorUserId).HasMaxLength(450);
                e.Property(x => x.Fingerprint).HasMaxLength(128);
                e.Property(x => x.Route).HasMaxLength(2048);
                e.Property(x => x.Title).HasMaxLength(200);
                e.Property(x => x.Summary).HasMaxLength(2000);

                var payloadProperty = e.Property(x => x.PayloadJson).IsRequired();

                if (Database.IsSqlServer())
                {
                    payloadProperty.HasColumnType("nvarchar(max)");
                }
                else if (Database.IsNpgsql())
                {
                    payloadProperty.HasColumnType("text");
                }
                e.Property(x => x.Error).HasMaxLength(2000);
                e.Property(x => x.AttemptCount).HasDefaultValue(0);
                e.HasIndex(x => x.DispatchedUtc);
                e.HasIndex(x => new { x.RecipientUserId, x.Kind, x.DispatchedUtc });
                e.HasIndex(x => new { x.Module, x.EventType, x.DispatchedUtc });
                e.HasIndex(x => new { x.ScopeType, x.ScopeId, x.DispatchedUtc });
                e.HasIndex(x => new { x.ProjectId, x.DispatchedUtc });
                e.HasIndex(x => new { x.ActorUserId, x.DispatchedUtc });
                e.HasIndex(x => x.Fingerprint);
            });

            builder.Entity<ProjectDocumentRequest>(e =>
            {
                ConfigureRowVersion(e);
                e.Property(x => x.ProjectId).IsRequired();
                e.Property(x => x.StageId).IsRequired(false);
                e.Property(x => x.DocumentId).IsRequired(false);
                e.Property(x => x.TotId).IsRequired(false);
                e.Property(x => x.Title).HasMaxLength(200).IsRequired();
                e.Property(x => x.Description).HasMaxLength(2000);
                e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).HasDefaultValue(ProjectDocumentRequestStatus.Draft).IsRequired();
                e.Property(x => x.RequestType)
                    .HasConversion<string>()
                    .HasMaxLength(32)
                    .HasDefaultValue(ProjectDocumentRequestType.Upload)
                    .HasSentinel(ProjectDocumentRequestType.Upload)
                    .IsRequired();
                e.Property(x => x.TempStorageKey).HasMaxLength(260);
                e.Property(x => x.OriginalFileName).HasMaxLength(260);
                e.Property(x => x.ContentType).HasMaxLength(128);
                e.Property(x => x.FileSize).IsRequired(false);
                e.Property(x => x.RequestedByUserId).HasMaxLength(450).IsRequired();
                e.Property(x => x.ReviewedByUserId).HasMaxLength(450);
                e.Property(x => x.ReviewedAtUtc).IsRequired(false);
                e.Property(x => x.ReviewerNote).HasMaxLength(2000);
                e.HasIndex(x => new { x.ProjectId, x.Status });
                e.HasIndex(x => x.ProjectId);
                e.HasIndex(x => new { x.ProjectId, x.TotId });
                e.HasOne(x => x.Project)
                    .WithMany()
                    .HasForeignKey(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Stage)
                    .WithMany()
                    .HasForeignKey(x => x.StageId)
                    .OnDelete(DeleteBehavior.SetNull);
                e.HasOne(x => x.Document)
                    .WithOne(x => x.Request)
                    .HasForeignKey<ProjectDocumentRequest>(x => x.DocumentId)
                    .OnDelete(DeleteBehavior.SetNull);
                e.HasOne(x => x.Tot)
                    .WithMany()
                    .HasForeignKey(x => x.TotId)
                    .OnDelete(DeleteBehavior.SetNull);
                e.HasOne(x => x.RequestedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.RequestedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.ReviewedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.ReviewedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);

                if (Database.IsSqlServer())
                {
                    e.Property(x => x.RequestedAtUtc).HasDefaultValueSql("GETUTCDATE()");
                }
                else if (Database.IsNpgsql())
                {
                    e.Property(x => x.RequestedAtUtc).HasDefaultValueSql("now() at time zone 'utc'");
                }
                else
                {
                    e.Property(x => x.RequestedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                }

                var pendingForDocumentIndex = e.HasIndex(x => x.DocumentId)
                    .HasDatabaseName("ux_projectdocumentrequests_pending_document")
                    .IsUnique();

                if (Database.IsSqlServer())
                {
                    pendingForDocumentIndex.HasFilter("[DocumentId] IS NOT NULL AND [Status] IN ('Draft', 'Submitted')");
                }
                else if (Database.IsNpgsql())
                {
                    pendingForDocumentIndex.HasFilter("\"DocumentId\" IS NOT NULL AND \"Status\" IN ('Draft', 'Submitted')");
                }
                else
                {
                    pendingForDocumentIndex.HasFilter("DocumentId IS NOT NULL AND Status IN ('Draft', 'Submitted')");
                }
            });

            builder.Entity<ProjectPlanSnapshot>(e =>
            {
                e.HasIndex(x => new { x.ProjectId, x.TakenAt });
                e.Property(x => x.TakenByUserId).HasMaxLength(450).IsRequired();
                e.HasOne(x => x.Project)
                    .WithMany()
                    .HasForeignKey(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.TakenByUser)
                    .WithMany()
                    .HasForeignKey(x => x.TakenByUserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<ProjectPlanSnapshotRow>(e =>
            {
                e.Property(x => x.StageCode).HasMaxLength(32).IsRequired();
                e.Property(x => x.PlannedStart).HasColumnType("date");
                e.Property(x => x.PlannedDue).HasColumnType("date");
                e.HasOne(x => x.Snapshot)
                    .WithMany(x => x.Rows)
                    .HasForeignKey(x => x.SnapshotId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<Status>(e =>
            {
                e.Property(x => x.Name).HasMaxLength(100).IsRequired();
                e.Property(x => x.SortOrder).IsRequired();
                e.HasIndex(x => x.Name).IsUnique();
                ConfigureRowVersion(e);
            });

            builder.Entity<Workflow>(e =>
            {
                e.Property(x => x.Name).HasMaxLength(100).IsRequired();
                ConfigureRowVersion(e);
            });

            builder.Entity<WorkflowStatus>(e =>
            {
                e.Property(x => x.Name).HasMaxLength(100).IsRequired();
                e.Property(x => x.SortOrder).IsRequired();
                e.HasOne(x => x.Workflow)
                    .WithMany(x => x.Statuses)
                    .HasForeignKey(x => x.WorkflowId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Status)
                    .WithMany(x => x.WorkflowStatuses)
                    .HasForeignKey(x => x.StatusId)
                    .OnDelete(DeleteBehavior.Restrict);
                ConfigureRowVersion(e);

                if (Database.IsSqlServer())
                {
                    e.HasIndex(nameof(WorkflowStatus.Name), nameof(WorkflowStatus.WorkflowId))
                        .HasFilter("[Name] IS NOT NULL AND [WorkflowId] IS NOT NULL");
                }
                else if (Database.IsNpgsql())
                {
                    e.HasIndex(nameof(WorkflowStatus.Name), nameof(WorkflowStatus.WorkflowId))
                        .HasFilter("\"Name\" IS NOT NULL AND \"WorkflowId\" IS NOT NULL");
                }
                else
                {
                    e.HasIndex(x => new { x.Name, x.WorkflowId });
                }
            });

            builder.Entity<ProjectCategory>(e =>
            {
                e.Property(x => x.Name).HasMaxLength(120).IsRequired();
                e.HasIndex(x => new { x.ParentId, x.Name }).IsUnique();
                e.Property(x => x.SortOrder).HasDefaultValue(0);
                e.Property(x => x.IsActive).HasDefaultValue(true);
                e.Property(x => x.CreatedByUserId).HasMaxLength(64).IsRequired();
                e.Property(x => x.CreatedAt);
                e.HasOne(x => x.Parent)
                    .WithMany(x => x.Children)
                    .HasForeignKey(x => x.ParentId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<TechnicalCategory>(e =>
            {
                e.Property(x => x.Name).HasMaxLength(120).IsRequired();
                e.HasIndex(x => new { x.ParentId, x.Name }).IsUnique();
                e.Property(x => x.SortOrder).HasDefaultValue(0);
                e.Property(x => x.IsActive).HasDefaultValue(true);
                e.Property(x => x.CreatedByUserId).HasMaxLength(64).IsRequired();
                e.Property(x => x.CreatedAt);
                e.HasOne(x => x.Parent)
                    .WithMany(x => x.Children)
                    .HasForeignKey(x => x.ParentId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<SponsoringUnit>(e =>
            {
                e.Property(x => x.Name).HasMaxLength(200).IsRequired();
                e.HasIndex(x => x.Name).IsUnique();
                e.Property(x => x.IsActive).HasDefaultValue(true);
                e.Property(x => x.SortOrder).HasDefaultValue(0);

                if (Database.IsSqlServer())
                {
                    e.Property(x => x.CreatedUtc).HasDefaultValueSql("GETUTCDATE()");
                    e.Property(x => x.UpdatedUtc).HasDefaultValueSql("GETUTCDATE()");
                }
                else if (Database.IsNpgsql())
                {
                    e.Property(x => x.CreatedUtc).HasDefaultValueSql("now() at time zone 'utc'");
                    e.Property(x => x.UpdatedUtc).HasDefaultValueSql("now() at time zone 'utc'");
                }
                else
                {
                    e.Property(x => x.CreatedUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                    e.Property(x => x.UpdatedUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                }
            });

            builder.Entity<LineDirectorate>(e =>
            {
                e.Property(x => x.Name).HasMaxLength(200).IsRequired();
                e.HasIndex(x => x.Name).IsUnique();
                e.Property(x => x.IsActive).HasDefaultValue(true);
                e.Property(x => x.SortOrder).HasDefaultValue(0);

                if (Database.IsSqlServer())
                {
                    e.Property(x => x.CreatedUtc).HasDefaultValueSql("GETUTCDATE()");
                    e.Property(x => x.UpdatedUtc).HasDefaultValueSql("GETUTCDATE()");
                }
                else if (Database.IsNpgsql())
                {
                    e.Property(x => x.CreatedUtc).HasDefaultValueSql("now() at time zone 'utc'");
                    e.Property(x => x.UpdatedUtc).HasDefaultValueSql("now() at time zone 'utc'");
                }
                else
                {
                    e.Property(x => x.CreatedUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                    e.Property(x => x.UpdatedUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                }
            });

            builder.Entity<ProjectMetaChangeRequest>(e =>
            {
                e.Property(x => x.ChangeType).HasMaxLength(64).IsRequired();
                e.Property(x => x.Payload).IsRequired();
                e.Property(x => x.RequestNote).HasMaxLength(1024);
                e.Property(x => x.DecisionStatus).HasMaxLength(32).IsRequired();
                e.Property(x => x.DecisionNote).HasMaxLength(1024);
                e.Property(x => x.RequestedByUserId).HasMaxLength(450);
                e.Property(x => x.DecidedByUserId).HasMaxLength(450);
                e.Property(x => x.RequestedOnUtc).IsRequired();
                e.Property(x => x.OriginalName).HasMaxLength(100).IsRequired();
                e.Property(x => x.OriginalDescription).HasMaxLength(1000);
                e.Property(x => x.OriginalCaseFileNumber).HasMaxLength(50);
                e.Property(x => x.OriginalRowVersion).HasMaxLength(8);
                e.HasOne(x => x.Project)
                    .WithMany()
                    .HasForeignKey(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne<TechnicalCategory>()
                    .WithMany()
                    .HasForeignKey(x => x.TechnicalCategoryId)
                    .OnDelete(DeleteBehavior.Restrict);
                e.HasIndex(x => x.TechnicalCategoryId);

                if (Database.IsSqlServer())
                {
                    e.HasIndex(x => x.ProjectId)
                        .HasDatabaseName("ux_projectmetachangerequests_pending")
                        .HasFilter("[DecisionStatus] = 'Pending'")
                        .IsUnique();
                }
                else if (Database.IsNpgsql())
                {
                    e.HasIndex(x => x.ProjectId)
                        .HasDatabaseName("ux_projectmetachangerequests_pending")
                        .HasFilter("\"DecisionStatus\" = 'Pending'")
                        .IsUnique();
                }
                else
                {
                    e.HasIndex(x => x.ProjectId);
                }
            });

            void ConfigureMoneyFact<T>(EntityTypeBuilder<T> entityBuilder, string amountColumn, string checkName)
                where T : ProjectFactBase
            {
                ConfigureRowVersion(entityBuilder);
                entityBuilder.Property(x => x.ProjectId).IsRequired();
                entityBuilder.Property(x => x.CreatedByUserId).HasMaxLength(64).IsRequired();
                entityBuilder.Property(amountColumn).HasColumnType("decimal(18,2)");
                entityBuilder.HasIndex(x => x.ProjectId);
                entityBuilder.HasOne<Project>()
                    .WithMany()
                    .HasForeignKey(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                entityBuilder.ToTable(tb =>
                {
                    tb.HasCheckConstraint(checkName, $"\"{amountColumn}\" >= 0");
                });
            }

            if (!Database.IsNpgsql())
            {
                var dateOnlyConverter = new ValueConverter<DateOnly, DateTime>(
                    static date => date.ToDateTime(TimeOnly.MinValue),
                    static dateTime => DateOnly.FromDateTime(dateTime));

                var nullableDateOnlyConverter = new ValueConverter<DateOnly?, DateTime?>(
                    static date => date.HasValue ? date.Value.ToDateTime(TimeOnly.MinValue) : null,
                    static dateTime => dateTime.HasValue ? DateOnly.FromDateTime(dateTime.Value) : null);

                var dateOnlyComparer = new ValueComparer<DateOnly>(
                    static (left, right) => left.DayNumber == right.DayNumber,
                    static date => date.GetHashCode(),
                    static date => DateOnly.FromDayNumber(date.DayNumber));

                var nullableDateOnlyComparer = new ValueComparer<DateOnly?>(
                static (left, right) =>
                    left.HasValue && right.HasValue
                        ? left.Value.DayNumber == right.Value.DayNumber
                        : left.HasValue == right.HasValue,
                    static date => date.HasValue ? date.Value.GetHashCode() : 0,
                    static date => date.HasValue ? DateOnly.FromDayNumber(date.Value.DayNumber) : (DateOnly?)null);

                foreach (var entityType in builder.Model.GetEntityTypes())
                {
                    foreach (var property in entityType.GetProperties())
                    {
                        if (property.ClrType == typeof(DateOnly))
                        {
                            property.SetValueConverter(dateOnlyConverter);
                            property.SetValueComparer(dateOnlyComparer);
                            property.SetColumnType("date");
                        }
                        else if (property.ClrType == typeof(DateOnly?))
                        {
                            property.SetValueConverter(nullableDateOnlyConverter);
                            property.SetValueComparer(nullableDateOnlyComparer);
                            property.SetColumnType("date");
                        }
                    }
                }
            }

            ConfigureMoneyFact(builder.Entity<ProjectIpaFact>(), nameof(ProjectIpaFact.IpaCost), "ck_ipafact_amount");
            ConfigureMoneyFact(builder.Entity<ProjectAonFact>(), nameof(ProjectAonFact.AonCost), "ck_aonfact_amount");
            ConfigureMoneyFact(builder.Entity<ProjectBenchmarkFact>(), nameof(ProjectBenchmarkFact.BenchmarkCost), "ck_bmfact_amount");
            ConfigureMoneyFact(builder.Entity<ProjectCommercialFact>(), nameof(ProjectCommercialFact.L1Cost), "ck_l1fact_amount");
            ConfigureMoneyFact(builder.Entity<ProjectPncFact>(), nameof(ProjectPncFact.PncCost), "ck_pncfact_amount");

            builder.Entity<ProjectSowFact>(e =>
            {
                ConfigureRowVersion(e);
                e.Property(x => x.ProjectId).IsRequired();
                e.Property(x => x.CreatedByUserId).HasMaxLength(64).IsRequired();
                e.Property(x => x.SponsoringUnit).HasMaxLength(200).IsRequired();
                e.Property(x => x.SponsoringLineDirectorate).HasMaxLength(200).IsRequired();
                e.HasIndex(x => x.ProjectId);
                e.HasOne<Project>()
                    .WithMany()
                    .HasForeignKey(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<ProjectSupplyOrderFact>(e =>
            {
                ConfigureRowVersion(e);
                e.Property(x => x.ProjectId).IsRequired();
                e.Property(x => x.CreatedByUserId).HasMaxLength(64).IsRequired();
                e.HasIndex(x => x.ProjectId);
                e.Property(x => x.SupplyOrderDate).HasColumnType("date");
                e.HasOne<Project>()
                    .WithMany()
                    .HasForeignKey(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // SECTION: Completed project cost and proliferation tables
            builder.Entity<ProjectProductionCostFact>(e =>
            {
                e.HasKey(x => x.ProjectId);
                e.Property(x => x.ApproxProductionCost).HasColumnType("numeric(18,2)");
                e.Property(x => x.Remarks).HasMaxLength(500);
                e.Property(x => x.UpdatedByUserId).HasMaxLength(64).IsRequired();
                e.HasOne(x => x.Project)
                    .WithOne()
                    .HasForeignKey<ProjectProductionCostFact>(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<ProjectLppRecord>(e =>
            {
                e.HasIndex(x => new { x.ProjectId, x.LppDate, x.CreatedAtUtc });
                e.Property(x => x.LppAmount).HasColumnType("numeric(18,2)");
                e.Property(x => x.SupplyOrderNumber).HasMaxLength(64);
                e.Property(x => x.Remarks).HasMaxLength(500);
                e.Property(x => x.CreatedByUserId).HasMaxLength(64).IsRequired();
                e.HasOne(x => x.Project)
                    .WithMany()
                    .HasForeignKey(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.ProjectDocument)
                    .WithMany()
                    .HasForeignKey(x => x.ProjectDocumentId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<ProjectTechStatus>(e =>
            {
                e.HasKey(x => x.ProjectId);
                e.Property(x => x.TechStatus)
                    .HasMaxLength(32)
                    .HasDefaultValue(ProjectTechStatusCodes.Current)
                    .IsRequired();
                e.Property(x => x.NotAvailableReason).HasMaxLength(500);
                e.Property(x => x.Remarks).HasMaxLength(500);
                e.Property(x => x.MarkedByUserId).HasMaxLength(64).IsRequired();
                e.HasOne(x => x.Project)
                    .WithOne()
                    .HasForeignKey<ProjectTechStatus>(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.ToTable(tb =>
                {
                    tb.HasCheckConstraint(
                        "ck_projecttechstatus_code",
                        $"\"TechStatus\" IN ('{ProjectTechStatusCodes.Current}', '{ProjectTechStatusCodes.Outdated}', '{ProjectTechStatusCodes.Obsolete}')");
                });
            });

            builder.Entity<AuditLog>(e =>
            {
                e.HasIndex(x => x.TimeUtc);
                e.HasIndex(x => x.Action);
                e.HasIndex(x => x.UserId);
                e.HasIndex(x => x.Level);
                e.HasIndex(x => x.UserName);
                e.HasIndex(x => x.Ip);
                e.Property(x => x.Level).HasMaxLength(16);
                e.Property(x => x.Action).HasMaxLength(64);
                e.Property(x => x.Message).HasMaxLength(1024);
            });

            builder.Entity<TodoItem>(e =>
            {
                e.Property<uint>("xmin").IsRowVersion();
                e.HasIndex(x => new { x.OwnerId, x.Status, x.IsPinned, x.DueAtUtc });
                e.HasIndex(x => new { x.OwnerId, x.OrderIndex });
                e.HasIndex(x => x.DeletedUtc);
                e.Property(x => x.Title).IsRequired().HasMaxLength(160);
                e.Property(x => x.Priority).ValueGeneratedNever();
                e.Property(x => x.Status).HasDefaultValue(TodoStatus.Open);
                e.Property(x => x.IsPinned).HasDefaultValue(false);
                e.Property(x => x.OrderIndex).HasDefaultValue(0);
            });

            builder.Entity<Celebration>(e =>
            {
                e.HasIndex(x => x.DeletedUtc).HasFilter("\"DeletedUtc\" IS NULL");
                e.HasIndex(x => new { x.EventType, x.Month, x.Day });
                e.Property(x => x.EventType).ValueGeneratedNever();
                e.Property(x => x.Name).IsRequired().HasMaxLength(120);
                e.Property(x => x.SpouseName).HasMaxLength(120);
            });

            builder.Entity<AuthEvent>(e =>
            {
                e.HasIndex(x => new { x.Event, x.WhenUtc });
                e.Property(x => x.Event).HasMaxLength(32).IsRequired();
            });

            builder.Entity<DailyLoginStat>(e =>
            {
                e.HasIndex(x => x.Date).IsUnique();
            });

            builder.Entity<Event>(e =>
            {
                e.HasQueryFilter(x => !x.IsDeleted);
                e.Property(x => x.Category).HasConversion<byte>();
                e.HasIndex(x => x.StartUtc);
                e.HasIndex(x => x.EndUtc);
                e.Property(x => x.Title).IsRequired().HasMaxLength(160);
                e.Property(x => x.Location).HasMaxLength(160);
            });

            builder.Entity<StageTemplate>(e =>
            {
                e.HasIndex(x => new { x.Version, x.Code }).IsUnique();
                e.Property(x => x.Code).HasMaxLength(16);
                e.Property(x => x.Name).HasMaxLength(128);
            });

            builder.Entity<StageDependencyTemplate>(e =>
            {
                e.HasIndex(x => new { x.Version, x.FromStageCode, x.DependsOnStageCode }).IsUnique();
                e.Property(x => x.FromStageCode).HasMaxLength(16);
                e.Property(x => x.DependsOnStageCode).HasMaxLength(16);
            });

            builder.Entity<StageChecklistTemplate>(e =>
            {
                ConfigureRowVersion(e);
                e.HasIndex(x => new { x.Version, x.StageCode }).IsUnique();
                e.Property(x => x.Version).HasMaxLength(32);
                e.Property(x => x.StageCode).HasMaxLength(16).IsRequired();
                e.Property(x => x.UpdatedByUserId).HasMaxLength(450);
                e.Property(x => x.UpdatedOn).IsRequired(false);
                e.HasMany(x => x.Items)
                    .WithOne(x => x.Template)
                    .HasForeignKey(x => x.TemplateId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasMany(x => x.AuditEntries)
                    .WithOne(x => x.Template)
                    .HasForeignKey(x => x.TemplateId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.UpdatedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.UpdatedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<StageChecklistItemTemplate>(e =>
            {
                ConfigureRowVersion(e);
                e.HasIndex(x => new { x.TemplateId, x.Sequence }).IsUnique();
                e.Property(x => x.Text).HasMaxLength(512).IsRequired();
                e.Property(x => x.UpdatedByUserId).HasMaxLength(450);
                e.Property(x => x.UpdatedOn).IsRequired(false);
                e.HasOne(x => x.UpdatedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.UpdatedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<StageChecklistAudit>(e =>
            {
                e.HasIndex(x => x.TemplateId);
                e.Property(x => x.Action).HasMaxLength(32).IsRequired();
                e.Property(x => x.PayloadJson).HasColumnType("jsonb");
                e.Property(x => x.PerformedByUserId).HasMaxLength(450);
                e.Property(x => x.PerformedOn).IsRequired();
                e.HasOne(x => x.Item)
                    .WithMany()
                    .HasForeignKey(x => x.ItemId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<StageChangeRequest>(e =>
            {
                e.Property(x => x.StageCode).HasMaxLength(32).IsRequired();
                e.Property(x => x.RequestedStatus).HasMaxLength(16).IsRequired();
                e.Property(x => x.RequestedDate).HasColumnType("date");
                e.Property(x => x.Note).HasMaxLength(1024);
                e.Property(x => x.RequestedByUserId).HasMaxLength(450).IsRequired();
                e.Property(x => x.DecisionStatus).HasMaxLength(12).HasDefaultValue("Pending").IsRequired();
                e.Property(x => x.DecidedByUserId).HasMaxLength(450);
                e.Property(x => x.DecisionNote).HasMaxLength(1024);

                var pendingIndex = e.HasIndex(x => new { x.ProjectId, x.StageCode });
                pendingIndex.IsUnique();
                pendingIndex.HasDatabaseName("ux_stagechangerequests_pending");

                if (Database.IsSqlServer())
                {
                    pendingIndex.HasFilter("[DecisionStatus] = 'Pending'");
                    e.ToTable(tb =>
                        tb.HasCheckConstraint("CK_StageChangeRequests_DecisionStatus",
                            "[DecisionStatus] IN ('Pending','Approved','Rejected','Superseded')"));
                }
                else if (Database.IsNpgsql())
                {
                    pendingIndex.HasFilter("\"DecisionStatus\" = 'Pending'");
                    e.ToTable(tb =>
                        tb.HasCheckConstraint("CK_StageChangeRequests_DecisionStatus",
                            "\"DecisionStatus\" IN ('Pending','Approved','Rejected','Superseded')"));
                }
                else
                {
                    pendingIndex.HasFilter("DecisionStatus = 'Pending'");
                    e.ToTable(tb =>
                        tb.HasCheckConstraint("CK_StageChangeRequests_DecisionStatus",
                            "DecisionStatus IN ('Pending','Approved','Rejected','Superseded')"));
                }
            });

            builder.Entity<StageChangeLog>(e =>
            {
                e.Property(x => x.StageCode).HasMaxLength(32).IsRequired();
                e.Property(x => x.Action).HasMaxLength(16).IsRequired();
                e.Property(x => x.FromStatus).HasMaxLength(16);
                e.Property(x => x.ToStatus).HasMaxLength(16);
                e.Property(x => x.FromActualStart).HasColumnType("date");
                e.Property(x => x.ToActualStart).HasColumnType("date");
                e.Property(x => x.FromCompletedOn).HasColumnType("date");
                e.Property(x => x.ToCompletedOn).HasColumnType("date");
                e.Property(x => x.UserId).HasMaxLength(450).IsRequired();
                e.Property(x => x.Note).HasMaxLength(1024);
                e.HasIndex(x => new { x.ProjectId, x.StageCode, x.At });

                const string allowedStageLogActions = "('Requested','Approved','Rejected','DirectApply','Applied','Superseded','AutoBackfill','Backfill')";

                if (Database.IsSqlServer())
                {
                    e.ToTable(tb =>
                        tb.HasCheckConstraint("CK_StageChangeLogs_Action",
                            $"[Action] IN {allowedStageLogActions}"));
                }
                else if (Database.IsNpgsql())
                {
                    e.ToTable(tb =>
                        tb.HasCheckConstraint("CK_StageChangeLogs_Action",
                            $"\"Action\" IN {allowedStageLogActions}"));
                }
                else
                {
                    e.ToTable(tb =>
                        tb.HasCheckConstraint("CK_StageChangeLogs_Action",
                            $"Action IN {allowedStageLogActions}"));
                }
            });

            builder.Entity<PlanVersion>(e =>
            {
                e.HasIndex(x => new { x.ProjectId, x.VersionNo }).IsUnique();

                var draftIndex = e.HasIndex(x => new { x.ProjectId, x.OwnerUserId })
                    .IsUnique();

                if (Database.IsNpgsql())
                {
                    draftIndex.HasFilter("\"Status\" = 'Draft' AND \"OwnerUserId\" IS NOT NULL");
                }
                else if (Database.IsSqlServer())
                {
                    draftIndex.HasFilter("[Status] = 'Draft' AND [OwnerUserId] IS NOT NULL");
                }
                else
                {
                    draftIndex.HasFilter("Status = 'Draft' AND OwnerUserId IS NOT NULL");
                }
                e.Property(x => x.Title).HasMaxLength(64);
                e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
                e.Property(x => x.OwnerUserId).HasMaxLength(450);
                e.Property(x => x.CreatedByUserId).HasMaxLength(450);
                e.Property(x => x.SubmittedByUserId).HasMaxLength(450);
                e.Property(x => x.ApprovedByUserId).HasMaxLength(450);
                e.Property(x => x.RejectedByUserId).HasMaxLength(450);
                e.Property(x => x.RejectionNote).HasMaxLength(512);
                e.Property(x => x.AnchorStageCode).HasMaxLength(16);
                e.Property(x => x.TransitionRule).HasConversion<string>().HasMaxLength(32);
                e.Property(x => x.SkipWeekends).HasDefaultValue(true);
                e.Property(x => x.PncApplicable).HasDefaultValue(true);
                e.HasOne(x => x.OwnerUser)
                    .WithMany()
                    .HasForeignKey(x => x.OwnerUserId)
                    .OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.SubmittedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.SubmittedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);
                e.HasOne(x => x.ApprovedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.ApprovedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);
                e.HasOne(x => x.RejectedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.RejectedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<StagePlan>(e =>
            {
                e.HasIndex(x => new { x.PlanVersionId, x.StageCode }).IsUnique();
                e.Property(x => x.StageCode).HasMaxLength(16);
            });

        builder.Entity<ProjectStage>(e =>
        {
            e.HasIndex(x => new { x.ProjectId, x.StageCode }).IsUnique();
            e.Property(x => x.StageCode).HasMaxLength(16);
            e.Property(x => x.AutoCompletedFromCode).HasMaxLength(16);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.ActualStart).HasColumnType("date").IsRequired(false);
            e.Property(x => x.CompletedOn).HasColumnType("date").IsRequired(false);
            e.Property(x => x.RequiresBackfill).HasDefaultValue(false).IsRequired();
            e.Property(x => x.ForecastStart).HasColumnType("date");
            e.Property(x => x.ForecastDue).HasColumnType("date");
            e.ToTable("ProjectStages", tb =>
                tb.HasCheckConstraint(
                    "CK_ProjectStages_CompletedHasDate",
                    "\"Status\" <> 'Completed' OR (\"CompletedOn\" IS NOT NULL AND \"ActualStart\" IS NOT NULL) OR \"RequiresBackfill\" IS TRUE"));
        });

            builder.Entity<StageShiftLog>(e =>
            {
                e.Property(x => x.StageCode).HasMaxLength(16);
                e.Property(x => x.CauseStageCode).HasMaxLength(16);
                e.Property(x => x.CauseType).HasMaxLength(24);
                e.HasIndex(x => new { x.ProjectId, x.StageCode, x.CreatedOn });
            });

            builder.Entity<ProjectScheduleSettings>(e =>
            {
                e.HasKey(x => x.ProjectId);
                e.Property(x => x.AnchorStart).HasColumnType("date");
                e.Property(x => x.NextStageStartPolicy).HasMaxLength(32).HasDefaultValue(NextStageStartPolicies.NextWorkingDay);
                e.HasOne(x => x.Project)
                    .WithOne()
                    .HasForeignKey<ProjectScheduleSettings>(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<ProjectPlanDuration>(e =>
            {
                e.Property(x => x.StageCode).HasMaxLength(16);
                e.HasIndex(x => new { x.ProjectId, x.StageCode }).IsUnique();
                e.HasOne<Project>()
                    .WithMany()
                    .HasForeignKey(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<Holiday>(e =>
            {
                e.HasIndex(x => x.Date).IsUnique();
                e.Property(x => x.Date).HasColumnType("date");
                e.Property(x => x.Name).HasMaxLength(160);
            });

            builder.Entity<PlanApprovalLog>(e =>
            {
                e.HasIndex(x => x.PlanVersionId);
                e.HasIndex(x => x.PerformedByUserId);
                e.Property(x => x.Action).HasMaxLength(64);
                e.Property(x => x.Note).HasMaxLength(1024);
                e.Property(x => x.PerformedByUserId).HasMaxLength(450);
                e.HasOne(x => x.PlanVersion)
                    .WithMany(p => p.ApprovalLogs)
                    .HasForeignKey(x => x.PlanVersionId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.PerformedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.PerformedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<ProjectComment>(e =>
            {
                e.HasIndex(x => new { x.ProjectId, x.CreatedOn });
                e.Property(x => x.Body).IsRequired().HasMaxLength(2000);
                e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
                e.Property(x => x.CreatedByUserId).HasMaxLength(450);
                e.Property(x => x.EditedByUserId).HasMaxLength(450);
                e.Property(x => x.CreatedOn).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.Property(x => x.Pinned).HasDefaultValue(false);
                e.Property(x => x.IsDeleted).HasDefaultValue(false);
                e.HasOne(x => x.Project)
                    .WithMany()
                    .HasForeignKey(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.ProjectStage)
                    .WithMany()
                    .HasForeignKey(x => x.ProjectStageId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.ParentComment)
                    .WithMany(x => x.Replies)
                    .HasForeignKey(x => x.ParentCommentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<ProjectCommentAttachment>(e =>
            {
                e.HasIndex(x => x.CommentId);
                e.Property(x => x.StoredFileName).HasMaxLength(260);
                e.Property(x => x.OriginalFileName).HasMaxLength(260);
                e.Property(x => x.ContentType).HasMaxLength(128);
                e.Property(x => x.StoragePath).HasMaxLength(512);
                e.Property(x => x.UploadedByUserId).HasMaxLength(450);
                e.HasOne(x => x.Comment)
                    .WithMany(c => c.Attachments)
                    .HasForeignKey(x => x.CommentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<ProjectCommentMention>(e =>
            {
                e.HasIndex(x => new { x.CommentId, x.UserId }).IsUnique();
                e.Property(x => x.UserId).HasMaxLength(450);
                e.HasOne(x => x.Comment)
                    .WithMany(c => c.Mentions)
                    .HasForeignKey(x => x.CommentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<Remark>(e =>
            {
                ConfigureRowVersion(e);
                e.Property(x => x.AuthorUserId).HasMaxLength(450).IsRequired();
                e.Property(x => x.AuthorRole).HasConversion<string>().HasMaxLength(64).IsRequired();
                e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
                e.Property(x => x.Scope)
                    .HasConversion<string>()
                    .HasMaxLength(32)
                    .HasDefaultValue(RemarkScope.General)
                    .IsRequired();
                e.Property(x => x.Body).HasMaxLength(4000).IsRequired();
                e.Property(x => x.StageRef).HasMaxLength(64);
                e.Property(x => x.StageNameSnapshot).HasMaxLength(256);
                e.Property(x => x.DeletedByUserId).HasMaxLength(450);
                e.Property(x => x.DeletedByRole).HasConversion<string>().HasMaxLength(64);
                e.Property(x => x.EventDate).HasColumnType("date").IsRequired();
                e.Property(x => x.CreatedAtUtc).IsRequired();
                e.Property(x => x.LastEditedAtUtc);
                e.Property(x => x.DeletedAtUtc);
                e.HasIndex(x => new { x.ProjectId, x.IsDeleted, x.CreatedAtUtc })
                    .HasDatabaseName("IX_Remarks_ProjectId_IsDeleted_CreatedAtUtc")
                    .IsDescending(false, false, true);
                e.HasIndex(x => new { x.ProjectId, x.IsDeleted, x.Type, x.EventDate })
                    .HasDatabaseName("IX_Remarks_ProjectId_IsDeleted_Type_EventDate");
                e.HasIndex(x => new { x.ProjectId, x.IsDeleted, x.Scope, x.CreatedAtUtc })
                    .HasDatabaseName("IX_Remarks_ProjectId_IsDeleted_Scope_CreatedAtUtc")
                    .IsDescending(false, false, false, true);
                e.HasOne(x => x.Project)
                    .WithMany()
                    .HasForeignKey(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<RemarkMention>(e =>
            {
                e.HasIndex(x => new { x.RemarkId, x.UserId }).IsUnique();
                e.Property(x => x.UserId).HasMaxLength(450);
                e.HasOne(x => x.Remark)
                    .WithMany(r => r.Mentions)
                    .HasForeignKey(x => x.RemarkId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<RemarkAudit>(e =>
            {
                e.HasIndex(x => x.RemarkId);
                e.Property(x => x.Action).HasConversion<string>().HasMaxLength(32).IsRequired();
                e.Property(x => x.SnapshotType).HasConversion<string>().HasMaxLength(32).IsRequired();
                e.Property(x => x.SnapshotScope)
                    .HasConversion<string>()
                    .HasMaxLength(32)
                    .HasDefaultValue(RemarkScope.General)
                    .IsRequired();
                e.Property(x => x.SnapshotAuthorRole).HasConversion<string>().HasMaxLength(64).IsRequired();
                e.Property(x => x.SnapshotDeletedByRole).HasConversion<string>().HasMaxLength(64);
                e.Property(x => x.ActorRole).HasConversion<string>().HasMaxLength(64).IsRequired();
                e.Property(x => x.ActorUserId).HasMaxLength(450);
                e.Property(x => x.SnapshotAuthorUserId).HasMaxLength(450).IsRequired();
                e.Property(x => x.SnapshotDeletedByUserId).HasMaxLength(450);
                e.Property(x => x.SnapshotStageRef).HasMaxLength(64);
                e.Property(x => x.SnapshotStageName).HasMaxLength(256);
                e.Property(x => x.SnapshotBody).HasMaxLength(4000).IsRequired();
                e.Property(x => x.Meta).HasColumnType("jsonb").IsRequired(false);
                e.Property(x => x.SnapshotEventDate).HasColumnType("date");
                e.HasOne(x => x.Remark)
                    .WithMany(x => x.AuditEntries)
                    .HasForeignKey(x => x.RemarkId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<TrainingType>(entity =>
            {
                ConfigureRowVersion(entity);
                entity.ToTable("TrainingTypes");
                entity.Property(x => x.Name).HasMaxLength(128).IsRequired();
                entity.Property(x => x.Description).HasMaxLength(512);
                entity.Property(x => x.DisplayOrder).HasDefaultValue(0);
                entity.Property(x => x.IsActive).HasDefaultValue(true);
                entity.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired();
                entity.Property(x => x.LastModifiedByUserId).HasMaxLength(450);
                entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("now() at time zone 'utc'");
                if (Database.IsNpgsql())
                {
                    entity.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
                    entity.Property(x => x.LastModifiedAtUtc).HasColumnType("timestamp with time zone");
                }
                else if (Database.IsSqlServer())
                {
                    entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("GETUTCDATE()");
                }
                else
                {
                    entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                }

                entity.HasIndex(x => x.Name).IsUnique();

                var trainingTypeSeedCreatedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
                entity.HasData(
                    new TrainingType
                    {
                        Id = new Guid("f4a9b1c7-0a3c-46da-92ff-39b861fd4c91"),
                        Name = "Simulator",
                        Description = "Simulator-based training sessions.",
                        DisplayOrder = 1,
                        IsActive = true,
                        CreatedAtUtc = trainingTypeSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("4d9b6f75-8d96-47d4-9d41-9f4f4a0ea679").ToByteArray()
                    },
                    new TrainingType
                    {
                        Id = new Guid("39f0d83c-5322-4a6d-bd1c-1b4dfbb5887b"),
                        Name = "Drone",
                        Description = "Drone operator and maintenance training.",
                        DisplayOrder = 2,
                        IsActive = true,
                        CreatedAtUtc = trainingTypeSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("d2f391bc-64a4-4c36-9218-1a3ba9bdeaf9").ToByteArray()
                    });
            });

            builder.Entity<TrainingRankCategoryMap>(entity =>
            {
                ConfigureRowVersion(entity);
                entity.ToTable("TrainingRankCategoryMap");
                entity.Property(x => x.Rank).HasMaxLength(64).IsRequired();
                entity.Property(x => x.IsActive).HasDefaultValue(true);
                entity.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired();
                entity.Property(x => x.LastModifiedByUserId).HasMaxLength(450);
                entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("now() at time zone 'utc'");
                if (Database.IsNpgsql())
                {
                    entity.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
                    entity.Property(x => x.LastModifiedAtUtc).HasColumnType("timestamp with time zone");
                }
                else if (Database.IsSqlServer())
                {
                    entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("GETUTCDATE()");
                }
                else
                {
                    entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                }

                entity.HasIndex(x => x.Rank).IsUnique();

                var rankSeedCreatedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
                entity.HasData(
                    new TrainingRankCategoryMap
                    {
                        Id = 1,
                        Rank = "Lt",
                        Category = TrainingCategory.Officer,
                        IsActive = true,
                        CreatedAtUtc = rankSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("5f6f9f3d-22ff-4a8f-9ef2-7cb3f1a90513").ToByteArray()
                    },
                    new TrainingRankCategoryMap
                    {
                        Id = 2,
                        Rank = "Capt",
                        Category = TrainingCategory.Officer,
                        IsActive = true,
                        CreatedAtUtc = rankSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("d6cda0a6-6a56-4b34-9dbe-a690b58cdbdf").ToByteArray()
                    },
                    new TrainingRankCategoryMap
                    {
                        Id = 3,
                        Rank = "Maj",
                        Category = TrainingCategory.Officer,
                        IsActive = true,
                        CreatedAtUtc = rankSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("56658d8a-1d3b-4d9a-bf35-f8f0a7f1da6b").ToByteArray()
                    },
                    new TrainingRankCategoryMap
                    {
                        Id = 4,
                        Rank = "Lt Col",
                        Category = TrainingCategory.Officer,
                        IsActive = true,
                        CreatedAtUtc = rankSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("a79b2734-37ce-4aa0-8fa7-1e43d5c6a6f4").ToByteArray()
                    },
                    new TrainingRankCategoryMap
                    {
                        Id = 5,
                        Rank = "Col",
                        Category = TrainingCategory.Officer,
                        IsActive = true,
                        CreatedAtUtc = rankSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("1efc757a-04fc-4e22-86e5-2cc9be3a81d7").ToByteArray()
                    },
                    new TrainingRankCategoryMap
                    {
                        Id = 6,
                        Rank = "Brig",
                        Category = TrainingCategory.Officer,
                        IsActive = true,
                        CreatedAtUtc = rankSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("a8f6a9c7-5fd1-4c53-8e2a-c2b9f1b0f0d7").ToByteArray()
                    },
                    new TrainingRankCategoryMap
                    {
                        Id = 7,
                        Rank = "Maj Gen",
                        Category = TrainingCategory.Officer,
                        IsActive = true,
                        CreatedAtUtc = rankSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("2d9f1234-338e-4d4a-bf67-3ac13096a4b8").ToByteArray()
                    },
                    new TrainingRankCategoryMap
                    {
                        Id = 8,
                        Rank = "Lt Gen",
                        Category = TrainingCategory.Officer,
                        IsActive = true,
                        CreatedAtUtc = rankSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("4f8b291c-2f26-4b69-9b6f-433b2116b1d9").ToByteArray()
                    },
                    new TrainingRankCategoryMap
                    {
                        Id = 9,
                        Rank = "Gen",
                        Category = TrainingCategory.Officer,
                        IsActive = true,
                        CreatedAtUtc = rankSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("c9c021ba-91ae-42c1-b9e8-930e10b7c47e").ToByteArray()
                    },
                    new TrainingRankCategoryMap
                    {
                        Id = 10,
                        Rank = "Naib Subedar",
                        Category = TrainingCategory.JuniorCommissionedOfficer,
                        IsActive = true,
                        CreatedAtUtc = rankSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("f5d7a678-d2ec-4d4b-a4e5-2c4d56f3f1b4").ToByteArray()
                    },
                    new TrainingRankCategoryMap
                    {
                        Id = 11,
                        Rank = "Subedar",
                        Category = TrainingCategory.JuniorCommissionedOfficer,
                        IsActive = true,
                        CreatedAtUtc = rankSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("a61689b0-44a8-4740-9451-a2a9639f4d9d").ToByteArray()
                    },
                    new TrainingRankCategoryMap
                    {
                        Id = 12,
                        Rank = "Subedar Major",
                        Category = TrainingCategory.JuniorCommissionedOfficer,
                        IsActive = true,
                        CreatedAtUtc = rankSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("e3c2b5d9-12af-47ac-b69f-68e8e6f5d3c1").ToByteArray()
                    },
                    new TrainingRankCategoryMap
                    {
                        Id = 13,
                        Rank = "Sepoy",
                        Category = TrainingCategory.OtherRank,
                        IsActive = true,
                        CreatedAtUtc = rankSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("0a7d2f5e-bbb4-4bd8-b73d-94a5082a4d0c").ToByteArray()
                    },
                    new TrainingRankCategoryMap
                    {
                        Id = 14,
                        Rank = "Lance Naik",
                        Category = TrainingCategory.OtherRank,
                        IsActive = true,
                        CreatedAtUtc = rankSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("9a9f4c8e-0a12-4e5f-901b-9714fcb7d9c2").ToByteArray()
                    },
                    new TrainingRankCategoryMap
                    {
                        Id = 15,
                        Rank = "Naik",
                        Category = TrainingCategory.OtherRank,
                        IsActive = true,
                        CreatedAtUtc = rankSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("b2e5f7c3-7d15-4ee2-9bf1-0c684b27dce9").ToByteArray()
                    },
                    new TrainingRankCategoryMap
                    {
                        Id = 16,
                        Rank = "Havildar",
                        Category = TrainingCategory.OtherRank,
                        IsActive = true,
                        CreatedAtUtc = rankSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("8d96f2bc-12d4-43f4-8d6b-2bb07948f3d9").ToByteArray()
                    });
            });

            builder.Entity<Training>(entity =>
            {
                ConfigureRowVersion(entity);
                entity.ToTable("Trainings");
                entity.Property(x => x.Notes).HasMaxLength(2000);
                entity.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired();
                entity.Property(x => x.LastModifiedByUserId).HasMaxLength(450);
                entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("now() at time zone 'utc'");
                if (Database.IsNpgsql())
                {
                    entity.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
                    entity.Property(x => x.LastModifiedAtUtc).HasColumnType("timestamp with time zone");
                    entity.Property(x => x.StartDate).HasColumnType("date");
                    entity.Property(x => x.EndDate).HasColumnType("date");
                }
                else if (Database.IsSqlServer())
                {
                    entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("GETUTCDATE()");
                    entity.Property(x => x.StartDate).HasColumnType("date");
                    entity.Property(x => x.EndDate).HasColumnType("date");
                }
                else
                {
                    entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                }

                entity.HasIndex(x => x.TrainingTypeId).HasDatabaseName("IX_Trainings_TrainingTypeId");
                entity.HasIndex(x => x.StartDate).HasDatabaseName("IX_Trainings_StartDate");
                entity.HasIndex(x => x.EndDate).HasDatabaseName("IX_Trainings_EndDate");
                entity.HasIndex(x => x.TrainingYear).HasDatabaseName("IX_Trainings_TrainingYear");

                entity.HasOne(x => x.TrainingType)
                    .WithMany(x => x.Trainings)
                    .HasForeignKey(x => x.TrainingTypeId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.Counters)
                    .WithOne(x => x.Training)
                    .HasForeignKey<TrainingCounters>(x => x.TrainingId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(x => x.ProjectLinks)
                    .WithOne(x => x.Training)
                    .HasForeignKey(x => x.TrainingId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(x => x.DeleteRequests)
                    .WithOne(x => x.Training)
                    .HasForeignKey(x => x.TrainingId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<TrainingCounters>(entity =>
            {
                ConfigureRowVersion(entity);
                entity.ToTable("TrainingCounters");
                entity.HasKey(x => x.TrainingId);
                entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("now() at time zone 'utc'");
                if (Database.IsNpgsql())
                {
                    entity.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
                }
                else if (Database.IsSqlServer())
                {
                    entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("GETUTCDATE()");
                }
                else
                {
                    entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                }

                entity.Property(x => x.Source)
                    .HasConversion<string>()
                    .HasMaxLength(32)
                    .IsRequired();
            });

            builder.Entity<TrainingProject>(entity =>
            {
                ConfigureRowVersion(entity);
                entity.ToTable("TrainingProjects");
                entity.HasKey(x => new { x.TrainingId, x.ProjectId });
                entity.Property(x => x.AllocationShare).HasColumnType("numeric(9,4)").HasDefaultValue(0);
                entity.HasIndex(x => x.ProjectId).HasDatabaseName("IX_TrainingProjects_ProjectId");
                entity.HasOne(x => x.Project)
                    .WithMany()
                    .HasForeignKey(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<TrainingTrainee>(entity =>
            {
                ConfigureRowVersion(entity);
                entity.ToTable("TrainingTrainees");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.ArmyNumber).HasMaxLength(32);
                entity.Property(x => x.Rank).HasMaxLength(128).IsRequired();
                entity.Property(x => x.Name).HasMaxLength(256).IsRequired();
                entity.Property(x => x.UnitName).HasMaxLength(256).IsRequired();
                entity.Property(x => x.Category).IsRequired();

                entity.HasIndex(x => x.TrainingId);

                var uniqueArmyNumberIndex = entity.HasIndex(x => new { x.TrainingId, x.ArmyNumber })
                    .IsUnique();

                if (Database.IsNpgsql())
                {
                    uniqueArmyNumberIndex.HasFilter("\"ArmyNumber\" IS NOT NULL");
                }
                else if (Database.IsSqlServer())
                {
                    uniqueArmyNumberIndex.HasFilter("[ArmyNumber] IS NOT NULL");
                }
                else
                {
                    uniqueArmyNumberIndex.HasFilter("ArmyNumber IS NOT NULL");
                }

                entity.HasOne(x => x.Training)
                    .WithMany(x => x.Trainees)
                    .HasForeignKey(x => x.TrainingId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<ActivityType>(entity =>
            {
                ConfigureRowVersion(entity);
                entity.ToTable("ActivityTypes");
                entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
                entity.Property(x => x.Description).HasMaxLength(512);
                entity.Property(x => x.IsActive).HasDefaultValue(true);
                entity.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired();
                entity.Property(x => x.LastModifiedByUserId).HasMaxLength(450);
                entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("now() at time zone 'utc'");
                if (Database.IsNpgsql())
                {
                    entity.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
                    entity.Property(x => x.LastModifiedAtUtc).HasColumnType("timestamp with time zone");
                }
                else if (Database.IsSqlServer())
                {
                    entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("GETUTCDATE()");
                }
                else
                {
                    entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                }

                entity.HasIndex(x => x.Name)
                    .HasDatabaseName("UX_ActivityTypes_Name")
                    .IsUnique();

                entity.HasOne(x => x.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.CreatedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.LastModifiedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.LastModifiedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);

                var activityTypeSeedCreatedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
                entity.HasData(
                    new ActivityType
                    {
                        Id = 1,
                        Name = "Adm Activities",
                        Description = "All types of administrative tasks/ events or activities.",
                        IsActive = true,
                        CreatedAtUtc = activityTypeSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("a9c3d0d8-0ff9-49c0-8b76-5fc11d5c10de").ToByteArray()
                    },
                    new ActivityType
                    {
                        Id = 2,
                        Name = "Inspections",
                        Description = "All internal and external inspections.",
                        IsActive = true,
                        CreatedAtUtc = activityTypeSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("1bcf5f8d-94b6-4fbb-89df-d1df0f9e9c42").ToByteArray()
                    },
                    new ActivityType
                    {
                        Id = 3,
                        Name = "Academia Interaction",
                        Description = "Engagements with academic institutions and partners.",
                        IsActive = true,
                        CreatedAtUtc = activityTypeSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("dc9b1c7a-4f83-4d17-8e37-9fd2665a1e3b").ToByteArray()
                    },
                    new ActivityType
                    {
                        Id = 4,
                        Name = "Industry Interaction",
                        Description = "Collaboration with industry stakeholders and forums.",
                        IsActive = true,
                        CreatedAtUtc = activityTypeSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("70be99f8-1137-4e7f-9e63-2f086b5c684d").ToByteArray()
                    },
                    new ActivityType
                    {
                        Id = 5,
                        Name = "Seminar/ Lecture",
                        Description = "Educational seminars, lectures, and talks.",
                        IsActive = true,
                        CreatedAtUtc = activityTypeSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("c775fa92-432d-49b9-8f62-6f8eea0c7e9b").ToByteArray()
                    },
                    new ActivityType
                    {
                        Id = 6,
                        Name = "Misc",
                        Description = "Activities that do not fit other defined categories.",
                        IsActive = true,
                        CreatedAtUtc = activityTypeSeedCreatedAt,
                        CreatedByUserId = "system",
                        RowVersion = new Guid("5c020d8c-7a4b-4f4f-8822-9f6cf208887f").ToByteArray()
                    });
            });

            builder.Entity<Activity>(entity =>
            {
                ConfigureRowVersion(entity);
                entity.ToTable("Activities");
                entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
                entity.Property(x => x.Description).HasMaxLength(2000);
                entity.Property(x => x.Location).HasMaxLength(450);
                entity.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired();
                entity.Property(x => x.LastModifiedByUserId).HasMaxLength(450);
                entity.Property(x => x.DeletedByUserId).HasMaxLength(450);
                entity.Property(x => x.IsDeleted).HasDefaultValue(false);
                entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("now() at time zone 'utc'");
                if (Database.IsNpgsql())
                {
                    entity.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
                    entity.Property(x => x.LastModifiedAtUtc).HasColumnType("timestamp with time zone");
                    entity.Property(x => x.DeletedAtUtc).HasColumnType("timestamp with time zone");
                    entity.Property(x => x.ScheduledStartUtc).HasColumnType("timestamp with time zone");
                    entity.Property(x => x.ScheduledEndUtc).HasColumnType("timestamp with time zone");
                }
                else if (Database.IsSqlServer())
                {
                    entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("GETUTCDATE()");
                }
                else
                {
                    entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                }

                entity.HasIndex(x => x.ActivityTypeId)
                    .HasDatabaseName("IX_Activities_ActivityTypeId");

                entity.HasIndex(x => x.CreatedAtUtc)
                    .HasDatabaseName("IX_Activities_CreatedAtUtc");

                entity.HasIndex(x => x.ScheduledStartUtc)
                    .HasDatabaseName("IX_Activities_ScheduledStartUtc");

                var uniqueActivityTitle = entity.HasIndex(x => new { x.ActivityTypeId, x.Title })
                    .HasDatabaseName("UX_Activities_ActivityTypeId_Title")
                    .IsUnique();

                if (Database.IsNpgsql())
                {
                    uniqueActivityTitle.HasFilter("\"IsDeleted\" = FALSE");
                }
                else if (Database.IsSqlServer())
                {
                    uniqueActivityTitle.HasFilter("[IsDeleted] = 0");
                }
                else
                {
                    uniqueActivityTitle.HasFilter("IsDeleted = 0");
                }

                entity.HasOne(x => x.ActivityType)
                    .WithMany(x => x.Activities)
                    .HasForeignKey(x => x.ActivityTypeId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.CreatedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.LastModifiedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.LastModifiedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(x => x.DeletedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.DeletedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasMany(x => x.Attachments)
                    .WithOne(x => x.Activity)
                    .HasForeignKey(x => x.ActivityId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<ActivityAttachment>(entity =>
            {
                ConfigureRowVersion(entity);
                entity.ToTable("ActivityAttachments");
                entity.Property(x => x.StorageKey).HasMaxLength(260).IsRequired();
                entity.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
                entity.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
                entity.Property(x => x.UploadedByUserId).HasMaxLength(450).IsRequired();
                entity.Property(x => x.FileSize).HasConversion<long>();
                entity.Property(x => x.UploadedAtUtc).HasDefaultValueSql("now() at time zone 'utc'");

                if (Database.IsNpgsql())
                {
                    entity.Property(x => x.UploadedAtUtc).HasColumnType("timestamp with time zone");
                }
                else if (Database.IsSqlServer())
                {
                    entity.Property(x => x.UploadedAtUtc).HasDefaultValueSql("GETUTCDATE()");
                }
                else
                {
                    entity.Property(x => x.UploadedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                }

                entity.HasIndex(x => x.ActivityId)
                    .HasDatabaseName("IX_ActivityAttachments_ActivityId");

                entity.HasIndex(x => x.UploadedAtUtc)
                    .HasDatabaseName("IX_ActivityAttachments_UploadedAtUtc");

                entity.HasOne(x => x.UploadedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.UploadedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<ActivityDeleteRequest>(entity =>
            {
                ConfigureRowVersion(entity);
                entity.ToTable("ActivityDeleteRequests");
                entity.Property(x => x.RequestedByUserId).HasMaxLength(450).IsRequired();
                entity.Property(x => x.ApprovedByUserId).HasMaxLength(450);
                entity.Property(x => x.RejectedByUserId).HasMaxLength(450);
                entity.Property(x => x.Reason).HasMaxLength(1000);
                entity.Property(x => x.RequestedAtUtc).HasDefaultValueSql("now() at time zone 'utc'");
                if (Database.IsNpgsql())
                {
                    entity.Property(x => x.RequestedAtUtc).HasColumnType("timestamp with time zone");
                    entity.Property(x => x.ApprovedAtUtc).HasColumnType("timestamp with time zone");
                    entity.Property(x => x.RejectedAtUtc).HasColumnType("timestamp with time zone");
                }
                else if (Database.IsSqlServer())
                {
                    entity.Property(x => x.RequestedAtUtc).HasDefaultValueSql("GETUTCDATE()");
                }
                else
                {
                    entity.Property(x => x.RequestedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                }

                entity.HasOne(x => x.Activity)
                    .WithMany(x => x.DeleteRequests)
                    .HasForeignKey(x => x.ActivityId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.RequestedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.RequestedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.ApprovedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.ApprovedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.RejectedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.RejectedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => x.RequestedAtUtc)
                    .HasDatabaseName("IX_ActivityDeleteRequests_RequestedAtUtc");

                var pendingIndex = entity.HasIndex(x => x.ActivityId)
                    .HasDatabaseName("UX_ActivityDeleteRequests_ActivityId_Pending")
                    .IsUnique();

                if (Database.IsNpgsql())
                {
                    pendingIndex.HasFilter("\"ApprovedAtUtc\" IS NULL AND \"RejectedAtUtc\" IS NULL");
                }
                else if (Database.IsSqlServer())
                {
                    pendingIndex.HasFilter("[ApprovedAtUtc] IS NULL AND [RejectedAtUtc] IS NULL");
                }
                else
                {
                    pendingIndex.HasFilter("ApprovedAtUtc IS NULL AND RejectedAtUtc IS NULL");
                }
            });

            builder.Entity<TrainingDeleteRequest>(entity =>
            {
                ConfigureRowVersion(entity);
                entity.ToTable("TrainingDeleteRequests");
                entity.Property(x => x.RequestedByUserId).HasMaxLength(450).IsRequired();
                entity.Property(x => x.DecidedByUserId).HasMaxLength(450);
                entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
                entity.Property(x => x.DecisionNotes).HasMaxLength(1000);
                entity.Property(x => x.Status)
                    .HasConversion<string>()
                    .HasMaxLength(32)
                    .IsRequired();
                entity.Property(x => x.RequestedAtUtc).HasDefaultValueSql("now() at time zone 'utc'");
                if (Database.IsNpgsql())
                {
                    entity.Property(x => x.RequestedAtUtc).HasColumnType("timestamp with time zone");
                    entity.Property(x => x.DecidedAtUtc).HasColumnType("timestamp with time zone");
                }
                else if (Database.IsSqlServer())
                {
                    entity.Property(x => x.RequestedAtUtc).HasDefaultValueSql("GETUTCDATE()");
                }
                else
                {
                    entity.Property(x => x.RequestedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                }

                entity.HasIndex(x => x.TrainingId).HasDatabaseName("IX_TrainingDeleteRequests_TrainingId");
                entity.HasIndex(x => x.Status).HasDatabaseName("IX_TrainingDeleteRequests_Status");
            });

            builder.Entity<UserNotificationPreference>(e =>
            {
                e.HasKey(x => new { x.UserId, x.Kind });
                e.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            });

            builder.Entity<UserProjectMute>(e =>
            {
                e.HasKey(x => new { x.UserId, x.ProjectId });
                e.Property(x => x.UserId).HasMaxLength(450).IsRequired();
                e.HasOne<Project>()
                    .WithMany()
                    .HasForeignKey(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            PrepareRowVersionValues();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            PrepareRowVersionValues();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void PrepareRowVersionValues()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State != EntityState.Added && entry.State != EntityState.Modified)
                {
                    continue;
                }

                foreach (var property in entry.Properties)
                {
                    if (!property.Metadata.IsConcurrencyToken || property.Metadata.ClrType != typeof(byte[]))
                    {
                        continue;
                    }

                    var needsValue = entry.State == EntityState.Added
                        ? property.CurrentValue is not byte[] bytes || bytes.Length == 0
                        : true;

                    if (!needsValue)
                    {
                        continue;
                    }

                    property.CurrentValue = Guid.NewGuid().ToByteArray();

                    if (entry.State == EntityState.Modified)
                    {
                        property.IsModified = true;
                    }
                }
            }
        }

        private static void ConfigureRowVersion<TEntity>(EntityTypeBuilder<TEntity> builder) where TEntity : class
        {
            var rowVersion = builder.Property<byte[]>(nameof(Project.RowVersion));
            rowVersion.HasColumnType("bytea");
            rowVersion.IsRequired();
            rowVersion.IsConcurrencyToken();
            rowVersion.ValueGeneratedNever();
            rowVersion.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Save);
            rowVersion.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Save);
        }
    }
}
