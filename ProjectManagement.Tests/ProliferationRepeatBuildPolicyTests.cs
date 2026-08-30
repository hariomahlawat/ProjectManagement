using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Api;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProliferationRepeatBuildPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateYearlyAsync_RejectsRepeatBuildProject()
    {
        await using var scope = await TestScope.CreateAsync();
        scope.Db.Projects.Add(Project(10, "Repeat build", isBuild: true));
        await scope.Db.SaveChangesAsync();

        var result = await scope.Service.CreateYearlyAsync(
            new ProliferationYearlyCreateDto
            {
                ProjectId = 10,
                Source = ProliferationSource.Sdd,
                Year = 2026,
                TotalQuantity = 4
            },
            scope.Principal,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("repeat-build", (result.Error ?? string.Empty).ToLowerInvariant());
        Assert.Empty(scope.Db.ProliferationYearlies);
    }

    [Fact]
    public async Task CreateGranularAsync_RejectsRepeatBuildProject()
    {
        await using var scope = await TestScope.CreateAsync();
        scope.Db.Projects.Add(Project(11, "Repeat build granular", isBuild: true));
        await scope.Db.SaveChangesAsync();

        var result = await scope.Service.CreateGranularAsync(
            new ProliferationGranularCreateDto
            {
                ProjectId = 11,
                Source = ProliferationSource.Sdd,
                UnitName = "Test Unit",
                ProliferationDateUtc = new DateTime(2026, 8, 1),
                Quantity = 2
            },
            scope.Principal,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("repeat-build", (result.Error ?? string.Empty).ToLowerInvariant());
        Assert.Empty(scope.Db.ProliferationGranularEntries);
    }

    [Fact]
    public async Task UpdateYearlyAsync_AllowsExistingLegacyRepeatBuildLinkToRemain()
    {
        await using var scope = await TestScope.CreateAsync();
        scope.Db.Projects.Add(Project(20, "Legacy repeat build", isBuild: true));
        var record = new ProliferationYearly
        {
            Id = Guid.NewGuid(),
            ProjectId = 20,
            Source = ProliferationSource.Sdd,
            Year = 2025,
            TotalQuantity = 1,
            ApprovalStatus = ApprovalStatus.Approved,
            SubmittedByUserId = scope.UserId,
            ApprovedByUserId = scope.UserId,
            ApprovedOnUtc = Now.UtcDateTime,
            CreatedOnUtc = Now.UtcDateTime,
            LastUpdatedOnUtc = Now.UtcDateTime,
            RowVersion = new byte[] { 1 }
        };
        scope.Db.ProliferationYearlies.Add(record);
        await scope.Db.SaveChangesAsync();

        var result = await scope.Service.UpdateYearlyAsync(
            record.Id,
            new ProliferationYearlyUpdateDto
            {
                ProjectId = 20,
                Source = ProliferationSource.Sdd,
                Year = 2025,
                TotalQuantity = 3,
                RowVersion = Convert.ToBase64String(record.RowVersion)
            },
            scope.Principal,
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        var saved = await scope.Db.ProliferationYearlies.SingleAsync(x => x.Id == record.Id);
        Assert.Equal(20, saved.ProjectId);
        Assert.Equal(3, saved.TotalQuantity);
    }

    [Fact]
    public async Task UpdateYearlyAsync_RejectsChangingLegacyRecordToAnotherRepeatBuildProject()
    {
        await using var scope = await TestScope.CreateAsync();
        scope.Db.Projects.AddRange(
            Project(30, "Legacy repeat build", isBuild: true),
            Project(31, "Another repeat build", isBuild: true));
        var record = new ProliferationYearly
        {
            Id = Guid.NewGuid(),
            ProjectId = 30,
            Source = ProliferationSource.Sdd,
            Year = 2025,
            TotalQuantity = 1,
            ApprovalStatus = ApprovalStatus.Approved,
            SubmittedByUserId = scope.UserId,
            ApprovedByUserId = scope.UserId,
            ApprovedOnUtc = Now.UtcDateTime,
            CreatedOnUtc = Now.UtcDateTime,
            LastUpdatedOnUtc = Now.UtcDateTime,
            RowVersion = new byte[] { 1 }
        };
        scope.Db.ProliferationYearlies.Add(record);
        await scope.Db.SaveChangesAsync();

        var result = await scope.Service.UpdateYearlyAsync(
            record.Id,
            new ProliferationYearlyUpdateDto
            {
                ProjectId = 31,
                Source = ProliferationSource.Sdd,
                Year = 2025,
                TotalQuantity = 1,
                RowVersion = Convert.ToBase64String(record.RowVersion)
            },
            scope.Principal,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("repeat-build", (result.Error ?? string.Empty).ToLowerInvariant());
        Assert.Equal(30, (await scope.Db.ProliferationYearlies.SingleAsync(x => x.Id == record.Id)).ProjectId);
    }

    [Fact]
    public async Task SetYearPreferenceAsync_RejectsNewRepeatBuildRule_ButAllowsExistingLegacyRule()
    {
        await using var scope = await TestScope.CreateAsync();
        scope.Db.Projects.Add(Project(40, "Legacy repeat build rule", isBuild: true));
        scope.Db.ProliferationYearPreferences.Add(new ProliferationYearPreference
        {
            Id = Guid.NewGuid(),
            ProjectId = 40,
            Source = ProliferationSource.Sdd,
            Year = 2025,
            Mode = YearPreferenceMode.UseYearly,
            SetByUserId = scope.UserId,
            SetOnUtc = Now.UtcDateTime
        });
        await scope.Db.SaveChangesAsync();

        var legacyUpdate = await scope.Service.SetYearPreferenceAsync(
            new ProliferationYearPreferenceDto
            {
                ProjectId = 40,
                Source = ProliferationSource.Sdd,
                Year = 2025,
                Mode = YearPreferenceMode.UseGranular,
                Reason = "Reviewing retained historical counting rule."
            },
            scope.Principal,
            CancellationToken.None);

        Assert.True(legacyUpdate.Success, legacyUpdate.Error);

        var newRule = await scope.Service.SetYearPreferenceAsync(
            new ProliferationYearPreferenceDto
            {
                ProjectId = 40,
                Source = ProliferationSource.Sdd,
                Year = 2026,
                Mode = YearPreferenceMode.UseGranular,
                Reason = "Attempted new rule."
            },
            scope.Principal,
            CancellationToken.None);

        Assert.False(newRule.Success);
        Assert.Contains("repeat-build", (newRule.Error ?? string.Empty).ToLowerInvariant());
        Assert.DoesNotContain(scope.Db.ProliferationYearPreferences, x => x.ProjectId == 40 && x.Year == 2026);
    }

    [Fact]
    public async Task DataQuality_ReportsEachLegacyRepeatBuildRecordForReview()
    {
        await using var scope = await TestScope.CreateAsync();
        scope.Db.Projects.Add(Project(50, "Legacy repeat-build records", isBuild: true));
        scope.Db.ProliferationYearlies.Add(new ProliferationYearly
        {
            Id = Guid.NewGuid(),
            ProjectId = 50,
            Source = ProliferationSource.Sdd,
            Year = 2025,
            TotalQuantity = 3,
            ApprovalStatus = ApprovalStatus.Approved,
            SubmittedByUserId = scope.UserId,
            CreatedOnUtc = Now.UtcDateTime,
            LastUpdatedOnUtc = Now.UtcDateTime,
            RowVersion = new byte[] { 1 }
        });
        scope.Db.ProliferationGranularEntries.Add(new ProliferationGranular
        {
            Id = Guid.NewGuid(),
            ProjectId = 50,
            Source = ProliferationSource.Sdd,
            UnitName = "Test Unit",
            ProliferationDate = new DateOnly(2025, 6, 1),
            Quantity = 2,
            ApprovalStatus = ApprovalStatus.Approved,
            SubmittedByUserId = scope.UserId,
            CreatedOnUtc = Now.UtcDateTime,
            LastUpdatedOnUtc = Now.UtcDateTime,
            RowVersion = new byte[] { 1 }
        });
        await scope.Db.SaveChangesAsync();

        var service = new ProliferationDataQualityService(scope.Db, scope.Users, scope.Clock, scope.Audit);
        var result = await service.GetIssuesAsync(
            new ProliferationDataQualityQuery(null, "repeat_build_link", null, 1, 25),
            CancellationToken.None);
        var summary = await service.GetSummaryAsync(CancellationToken.None);

        Assert.Equal(2, result.RepeatBuildLinkCount);
        Assert.Equal(2, result.Total);
        Assert.All(result.Items, item =>
        {
            Assert.Equal("repeat_build_link", item.IssueType);
            Assert.Equal("medium", item.Severity);
            Assert.False(item.CanCorrect);
        });
        Assert.Equal(2, summary.RepeatBuildLinkCount);
        Assert.Equal(0, summary.CorrectionRequiredCount);
    }

    private static Project Project(int id, string name, bool isBuild)
        => new()
        {
            Id = id,
            Name = name,
            CreatedByUserId = "creator",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            IsBuild = isBuild,
            RowVersion = Guid.NewGuid().ToByteArray()
        };

    private sealed class TestScope : IAsyncDisposable
    {
        private readonly ServiceProvider _services;

        private TestScope(
            ApplicationDbContext db,
            UserManager<ApplicationUser> users,
            FixedClock clock,
            NoOpAuditService audit,
            ServiceProvider services,
            string userId)
        {
            Db = db;
            Users = users;
            Clock = clock;
            Audit = audit;
            _services = services;
            UserId = userId;
            Principal = new ClaimsPrincipal(new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim(ClaimTypes.Name, "proliferation-test"),
                    new Claim(ClaimTypes.Role, "Admin")
                },
                "Test"));
            Service = new ProliferationSubmissionService(db, users, clock, audit);
        }

        public ApplicationDbContext Db { get; }
        public UserManager<ApplicationUser> Users { get; }
        public FixedClock Clock { get; }
        public NoOpAuditService Audit { get; }
        public ProliferationSubmissionService Service { get; }
        public ClaimsPrincipal Principal { get; }
        public string UserId { get; }

        public static async Task<TestScope> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new ApplicationDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<ILookupNormalizer, UpperInvariantLookupNormalizer>()
                .BuildServiceProvider();
            var users = new UserManager<ApplicationUser>(
                new UserStore<ApplicationUser>(db),
                Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(),
                services.GetRequiredService<ILookupNormalizer>(),
                new IdentityErrorDescriber(),
                services,
                NullLogger<UserManager<ApplicationUser>>.Instance);

            const string userId = "proliferation-test-user";
            db.Users.Add(new ApplicationUser
            {
                Id = userId,
                UserName = "proliferation-test",
                NormalizedUserName = "PROLIFERATION-TEST",
                FullName = "Proliferation Test User"
            });
            await db.SaveChangesAsync();

            return new TestScope(
                db,
                users,
                new FixedClock(Now),
                new NoOpAuditService(),
                services,
                userId);
        }

        public async ValueTask DisposeAsync()
        {
            Users.Dispose();
            await Db.DisposeAsync();
            await _services.DisposeAsync();
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class NoOpAuditService : IAuditService
    {
        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            Microsoft.AspNetCore.Http.HttpContext? http = null)
            => Task.CompletedTask;
    }
}
