using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Models;

namespace ProjectManagement.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole, string>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<Project> Projects { get; set; } = default!;
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<TodoItem> TodoItems => Set<TodoItem>();
        public DbSet<Note> Notes => Set<Note>();

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
                e.HasIndex(x => new { x.OwnerId, x.Status, x.IsPinned, x.DueAtUtc });
                e.HasIndex(x => new { x.OwnerId, x.OrderIndex });
                e.HasIndex(x => x.DeletedUtc);
                e.Property(x => x.Title).IsRequired().HasMaxLength(160);
                e.Property(x => x.Priority).ValueGeneratedNever();
                e.Property(x => x.Status).HasDefaultValue(TodoStatus.Open);
                e.Property(x => x.IsPinned).HasDefaultValue(false);
                e.Property(x => x.OrderIndex).HasDefaultValue(0);
            });

            builder.Entity<Note>(e =>
            {
                e.HasIndex(x => new { x.OwnerId, x.TodoId, x.IsPinned, x.DeletedUtc });
                e.Property(x => x.Title).IsRequired().HasMaxLength(160);
            });
        }
    }
}
