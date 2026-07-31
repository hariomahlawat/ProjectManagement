using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Workspace;

namespace ProjectManagement.Tests;

public sealed class ConferenceProjectScopeServiceTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(90, true)]
    [InlineData(730, true)]
    [InlineData(731, false)]
    public void ConferenceOptionsValidator_EnforcesSafeRetentionRange(int days, bool expectedValid)
    {
        var result = new ConferenceOptionsValidator().Validate(
            name: null,
            options: new ConferenceOptions { CompletedProjectRetentionDays = days });

        Assert.Equal(expectedValid, result.Succeeded);
    }

    [Theory]
    [InlineData(2026, 7, 25, true)]
    [InlineData(2026, 5, 2, true)]
    [InlineData(2026, 5, 1, false)]
    [InlineData(2026, 8, 1, false)]
    public void TryResolveCarryover_UsesExactCompletionDate(
        int year,
        int month,
        int day,
        bool expected)
    {
        var included = ConferenceProjectScopeService.TryResolveCarryover(
            new DateOnly(year, month, day),
            year,
            (short)month,
            null,
            new DateOnly(2026, 7, 31),
            90,
            out _);

        Assert.Equal(expected, included);
    }

    [Fact]
    public void TryResolveCarryover_RespectsPartialPrecisionAndAuditFallback()
    {
        Assert.True(ConferenceProjectScopeService.TryResolveCarryover(
            null,
            2026,
            5,
            null,
            new DateOnly(2026, 7, 31),
            90,
            out var monthSortDate));
        Assert.Equal(new DateOnly(2026, 5, 31), monthSortDate);

        Assert.True(ConferenceProjectScopeService.TryResolveCarryover(
            null,
            2026,
            null,
            null,
            new DateOnly(2026, 7, 31),
            90,
            out var yearSortDate));
        Assert.Equal(new DateOnly(2026, 7, 31), yearSortDate);

        Assert.True(ConferenceProjectScopeService.TryResolveCarryover(
            null,
            null,
            null,
            new DateTime(2026, 7, 20, 8, 0, 0, DateTimeKind.Utc),
            new DateOnly(2026, 7, 31),
            90,
            out var auditSortDate));
        Assert.Equal(new DateOnly(2026, 7, 20), auditSortDate);
    }

    [Theory]
    [InlineData(2026, 7, 25, 2026, 7, "Completed on 25 Jul 2026")]
    [InlineData(null, null, null, 2026, 7, "Completed in Jul 2026")]
    [InlineData(null, null, null, 2026, null, "Completed in 2026")]
    public void FormatCompletionContext_PreservesRecordedPrecision(
        int? exactYear,
        int? exactMonth,
        int? exactDay,
        int completedYear,
        int? completedMonth,
        string expected)
    {
        DateOnly? completedOn = exactYear.HasValue
            ? new DateOnly(exactYear.Value, exactMonth!.Value, exactDay!.Value)
            : null;

        var result = ConferenceProjectScopeService.FormatCompletionContext(
            completedOn,
            completedYear,
            completedMonth.HasValue ? (short?)completedMonth.Value : null);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task Scope_IncludesActiveAndRecentCompletedButExcludesExpiredCompleted()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new ApplicationDbContext(dbOptions);
        await db.Database.EnsureCreatedAsync();

        var command = CreateUser("command", "Command User", "Colonel");
        var officer = CreateUser("officer", "Conference Officer", "Lt Col");
        db.Users.AddRange(command, officer);
        var poRole = new IdentityRole
        {
            Id = "role-project-officer",
            Name = RoleNames.ProjectOfficer,
            NormalizedName = RoleNames.ProjectOfficer.ToUpperInvariant(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        db.Roles.Add(poRole);
        db.UserRoles.Add(new IdentityUserRole<string>
        {
            UserId = officer.Id,
            RoleId = poRole.Id
        });

        var active = Project("Active project", command.Id, officer.Id, ProjectLifecycleStatus.Active);
        var recent = Project("Recent project", command.Id, officer.Id, ProjectLifecycleStatus.Completed);
        recent.CompletedOn = new DateOnly(2026, 7, 20);
        recent.CompletedYear = 2026;
        recent.CompletedMonth = 7;
        var expired = Project("Expired project", command.Id, officer.Id, ProjectLifecycleStatus.Completed);
        expired.CompletedOn = new DateOnly(2026, 4, 1);
        expired.CompletedYear = 2026;
        expired.CompletedMonth = 4;
        db.Projects.AddRange(active, recent, expired);
        await db.SaveChangesAsync();

        var service = new ConferenceProjectScopeService(
            db,
            new FixedClock(new DateTimeOffset(2026, 7, 31, 8, 0, 0, TimeSpan.Zero)),
            Options.Create(new ConferenceOptions { CompletedProjectRetentionDays = 90 }));

        var carryovers = await service.GetRecentlyCompletedProjectsAsync();

        var carryover = Assert.Single(carryovers);
        Assert.Equal(recent.Id, carryover.ProjectId);
        Assert.Equal("Completed on 20 Jul 2026", carryover.CompletionContext);
        Assert.True(await service.IsProjectInScopeAsync(officer.Id, active.Id));
        Assert.True(await service.IsProjectInScopeAsync(officer.Id, recent.Id));
        Assert.False(await service.IsProjectInScopeAsync(officer.Id, expired.Id));
    }

    private static Project Project(
        string name,
        string createdBy,
        string officerId,
        ProjectLifecycleStatus lifecycleStatus)
        => new()
        {
            Name = name,
            CreatedByUserId = createdBy,
            LeadPoUserId = officerId,
            LifecycleStatus = lifecycleStatus,
            WorkflowVersion = ProcurementWorkflow.VersionV2
        };

    private static ApplicationUser CreateUser(string id, string fullName, string rank)
        => new()
        {
            Id = id,
            UserName = id,
            NormalizedUserName = id.ToUpperInvariant(),
            FullName = fullName,
            Rank = rank,
            SecurityStamp = Guid.NewGuid().ToString()
        };

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
