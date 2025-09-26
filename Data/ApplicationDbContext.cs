using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;

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
        public DbSet<PlanVersion> PlanVersions => Set<PlanVersion>();
        public DbSet<StagePlan> StagePlans => Set<StagePlan>();
        public DbSet<PlanApprovalLog> PlanApprovalLogs => Set<PlanApprovalLog>();
        public DbSet<ProjectStage> ProjectStages => Set<ProjectStage>();
        public DbSet<ProjectComment> ProjectComments => Set<ProjectComment>();
        public DbSet<ProjectCommentAttachment> ProjectCommentAttachments => Set<ProjectCommentAttachment>();
        public DbSet<ProjectCommentMention> ProjectCommentMentions => Set<ProjectCommentMention>();
        public DbSet<StageShiftLog> StageShiftLogs => Set<StageShiftLog>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Project>(e =>
            {
                e.Property(x => x.Name).HasMaxLength(100).IsRequired();
                e.HasIndex(x => x.Name);
                e.Property(x => x.ProjectNumber).HasMaxLength(64);
                e.HasIndex(x => x.ProjectNumber)
                    .IsUnique()
                    .HasFilter("\"ProjectNumber\" IS NOT NULL");
                e.Property(x => x.RowVersion).IsRowVersion();
                e.Property(x => x.CreatedByUserId).HasMaxLength(64).IsRequired();
                e.HasOne(x => x.Category)
                    .WithMany(x => x.Projects)
                    .HasForeignKey(x => x.CategoryId)
                    .OnDelete(DeleteBehavior.Restrict);
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

            void ConfigureMoneyFact<T>(EntityTypeBuilder<T> entityBuilder, string amountColumn)
                where T : ProjectFactBase
            {
                entityBuilder.Property(x => x.RowVersion).IsRowVersion();
                entityBuilder.Property(x => x.ProjectId).IsRequired();
                entityBuilder.Property(x => x.CreatedByUserId).HasMaxLength(64).IsRequired();
                entityBuilder.Property(amountColumn).HasColumnType("decimal(18,2)");
                entityBuilder.HasIndex(x => x.ProjectId);
                entityBuilder.HasOne<Project>()
                    .WithMany()
                    .HasForeignKey(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
            }

            ConfigureMoneyFact(builder.Entity<ProjectIpaFact>(), nameof(ProjectIpaFact.IpaCost));
            ConfigureMoneyFact(builder.Entity<ProjectAonFact>(), nameof(ProjectAonFact.AonCost));
            ConfigureMoneyFact(builder.Entity<ProjectBenchmarkFact>(), nameof(ProjectBenchmarkFact.BenchmarkCost));
            ConfigureMoneyFact(builder.Entity<ProjectCommercialFact>(), nameof(ProjectCommercialFact.L1Cost));
            ConfigureMoneyFact(builder.Entity<ProjectPncFact>(), nameof(ProjectPncFact.PncCost));

            builder.Entity<ProjectSowFact>(e =>
            {
                e.Property(x => x.RowVersion).IsRowVersion();
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
                e.Property(x => x.RowVersion).IsRowVersion();
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

            builder.Entity<PlanVersion>(e =>
            {
                e.HasIndex(x => new { x.ProjectId, x.VersionNo }).IsUnique();
                e.Property(x => x.Title).HasMaxLength(64);
                e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
                e.Property(x => x.CreatedByUserId).HasMaxLength(450);
                e.Property(x => x.SubmittedByUserId).HasMaxLength(450);
                e.Property(x => x.ApprovedByUserId).HasMaxLength(450);
                e.Property(x => x.Reason).HasMaxLength(512);
                e.Property(x => x.AnchorStageCode).HasMaxLength(16);
                e.Property(x => x.TransitionRule).HasConversion<string>().HasMaxLength(32);
                e.Property(x => x.SkipWeekends).HasDefaultValue(true);
                e.Property(x => x.PncApplicable).HasDefaultValue(true);
                e.HasOne(x => x.SubmittedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.SubmittedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);
                e.HasOne(x => x.ApprovedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.ApprovedByUserId)
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
                e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
                e.Property(x => x.ForecastStart).HasColumnType("date");
                e.Property(x => x.ForecastDue).HasColumnType("date");
                e.ToTable("ProjectStages", tb =>
                    tb.HasCheckConstraint("CK_ProjectStages_CompletedHasDate",
                        "NOT(\"Status\" = 'Completed' AND \"CompletedOn\" IS NULL)"));
            });

            builder.Entity<StageShiftLog>(e =>
            {
                e.Property(x => x.StageCode).HasMaxLength(16);
                e.Property(x => x.CauseStageCode).HasMaxLength(16);
                e.Property(x => x.CauseType).HasMaxLength(24);
                e.HasIndex(x => new { x.ProjectId, x.StageCode, x.CreatedOn });
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
        }
    }
}
