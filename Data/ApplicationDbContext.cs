using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using ProjectManagement.Models;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole, string>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<Project> Projects { get; set; } = default!;
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

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

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
        }
    }
}
