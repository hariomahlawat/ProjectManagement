using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Helpers;

namespace ProjectManagement.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole, string>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<Project> Projects { get; set; } = default!;
        public DbSet<ProjectCategory> ProjectCategories => Set<ProjectCategory>();
        public DbSet<ProjectIpaFact> ProjectIpaFacts => Set<ProjectIpaFact>();
        public DbSet<ProjectSowFact> ProjectSowFacts => Set<ProjectSowFact>();
        public DbSet<ProjectAonFact> ProjectAonFacts => Set<ProjectAonFact>();
        public DbSet<ProjectBenchmarkFact> ProjectBenchmarkFacts => Set<ProjectBenchmarkFact>();
        public DbSet<ProjectCommercialFact> ProjectCommercialFacts => Set<ProjectCommercialFact>();
        public DbSet<ProjectPncFact> ProjectPncFacts => Set<ProjectPncFact>();
        public DbSet<ProjectSupplyOrderFact> ProjectSupplyOrderFacts => Set<ProjectSupplyOrderFact>();
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
        public DbSet<ProjectStage> ProjectStages => Set<ProjectStage>();
        public DbSet<ProjectPhoto> ProjectPhotos => Set<ProjectPhoto>();
        public DbSet<ProjectMetaChangeRequest> ProjectMetaChangeRequests => Set<ProjectMetaChangeRequest>();
        public DbSet<ProjectComment> ProjectComments => Set<ProjectComment>();
        public DbSet<ProjectCommentAttachment> ProjectCommentAttachments => Set<ProjectCommentAttachment>();
        public DbSet<ProjectCommentMention> ProjectCommentMentions => Set<ProjectCommentMention>();
        public DbSet<Remark> Remarks => Set<Remark>();
        public DbSet<RemarkAudit> RemarkAudits => Set<RemarkAudit>();
        public DbSet<RemarkMention> RemarkMentions => Set<RemarkMention>();
        public DbSet<NotificationDispatch> NotificationDispatches => Set<NotificationDispatch>();
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

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

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
                e.Property(x => x.CoverPhotoVersion).HasDefaultValue(1).IsConcurrencyToken();
                e.HasMany(x => x.Photos)
                    .WithOne(x => x.Project)
                    .HasForeignKey(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne<ProjectPhoto>()
                    .WithMany()
                    .HasForeignKey(x => x.CoverPhotoId)
                    .OnDelete(DeleteBehavior.SetNull);
                e.HasOne(x => x.Category)
                    .WithMany(x => x.Projects)
                    .HasForeignKey(x => x.CategoryId)
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
            });

            builder.Entity<ProjectPhoto>(e =>
            {
                e.Property(x => x.StorageKey).HasMaxLength(260).IsRequired();
                e.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
                e.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
                e.Property(x => x.Caption).HasMaxLength(512);
                e.Property(x => x.Version).HasDefaultValue(1).IsConcurrencyToken();
                e.Property(x => x.Ordinal).HasDefaultValue(1);
                e.Property(x => x.CreatedUtc).HasDefaultValueSql("now() at time zone 'utc'");
                e.Property(x => x.UpdatedUtc).HasDefaultValueSql("now() at time zone 'utc'");
                e.HasIndex(x => new { x.ProjectId, x.Ordinal }).IsUnique();

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
                    .IsRequired();
                e.Property(x => x.FileStamp).HasDefaultValue(0).IsRequired();
                e.Property(x => x.UploadedByUserId).HasMaxLength(450).IsRequired();
                e.Property(x => x.IsArchived).HasDefaultValue(false);
                e.Property(x => x.ArchivedAtUtc).IsRequired(false);
                e.Property(x => x.ArchivedByUserId).HasMaxLength(450);
                e.HasIndex(x => new { x.ProjectId, x.StageId, x.IsArchived });
                e.HasIndex(x => x.ProjectId);
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
                e.Property(x => x.PayloadJson).HasMaxLength(4000).IsRequired();
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
                e.Property(x => x.Title).HasMaxLength(200).IsRequired();
                e.Property(x => x.Description).HasMaxLength(2000);
                e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).HasDefaultValue(ProjectDocumentRequestStatus.Draft).IsRequired();
                e.Property(x => x.RequestType).HasConversion<string>().HasMaxLength(32).HasDefaultValue(ProjectDocumentRequestType.Upload).IsRequired();
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
