using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ApplicationDatabaseMigrationsTests
{


    [Fact]
    public async Task NormalizeActionTaskBacklogRowsMigration_UnassignedOpenNoSprintTasksBecomeBacklog()
    {
        // SECTION: Arrange
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var context = new ApplicationDbContext(options))
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20261125190000_AddActionSprintAuditAndConcurrency");
        }

        await using (var context = new ApplicationDbContext(options))
        {
            context.ActionTasks.Add(new ActionTaskItem
            {
                Title = "Legacy backlog-style task",
                Description = "Old unassigned no-sprint row",
                CreatedByUserId = "creator",
                AssignedToUserId = string.Empty,
                CreatedByRole = RoleNames.HoD,
                AssignedToRole = string.Empty,
                AssignedOn = DateTime.UtcNow,
                DueDate = DateTime.UtcNow.Date.AddDays(2),
                Priority = "Normal",
                Status = ActionTaskStatuses.Assigned,
                SubmittedOn = DateTime.UtcNow,
                ClosedOn = DateTime.UtcNow,
                SprintId = null,
                RowVersion = Array.Empty<byte>()
            });
            await context.SaveChangesAsync();
        }

        // SECTION: Act
        await using (var context = new ApplicationDbContext(options))
        {
            await context.Database.MigrateAsync();
        }

        // SECTION: Assert
        await using (var context = new ApplicationDbContext(options))
        {
            var normalizedTask = await context.ActionTasks.SingleAsync(t => t.Title == "Legacy backlog-style task");
            Assert.Equal(ActionTaskStatuses.Backlog, normalizedTask.Status);
            Assert.Null(normalizedTask.SubmittedOn);
            Assert.Null(normalizedTask.ClosedOn);
            Assert.Null(normalizedTask.SprintId);
            Assert.True(string.IsNullOrWhiteSpace(normalizedTask.AssignedToUserId));
        }
    }

    [Fact]
    public async Task ApplyingMigrations_LeavesNoPendingMigrations()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var context = new ApplicationDbContext(options))
        {
            await context.Database.MigrateAsync();
        }

        await using (var context = new ApplicationDbContext(options))
        {
            var pending = await context.Database.GetPendingMigrationsAsync();
            Assert.Empty(pending);
        }
    }
}
