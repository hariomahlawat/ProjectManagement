using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
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

// Approval workflow rules for proliferation records: Project Office amends approved
// records through linked pending rows, only pending records can be decided, and the
// approved figure survives until an amendment is approved.
public sealed class ProliferationApprovalWorkflowTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 8, 0, 0, TimeSpan.Zero);
    private const int ProjectId = 20;

    [Fact]
    public async Task ProjectOfficeEditOfApprovedYearly_FilesPendingAmendment_AndLeavesApprovedFigure()
    {
        await using var scope = await TestScope.CreateAsync();
        var approved = await scope.SeedYearlyAsync(40, ApprovalStatus.Approved);

        var result = await scope.Service.UpdateYearlyAsync(
            approved.Id,
            YearlyUpdate(45, approved.RowVersion, "Corrected count"),
            scope.ProjectOffice,
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.NotEqual(approved.Id, result.EntityId);

        var original = await scope.ReloadYearlyAsync(approved.Id);
        Assert.Equal(ApprovalStatus.Approved, original.ApprovalStatus);
        Assert.Equal(40, original.TotalQuantity);

        var amendment = await scope.ReloadYearlyAsync(result.EntityId!.Value);
        Assert.Equal(ApprovalStatus.Pending, amendment.ApprovalStatus);
        Assert.Equal(45, amendment.TotalQuantity);
        Assert.Equal(approved.Id, amendment.AmendsId);
        Assert.Equal(TestScope.ProjectOfficeUserId, amendment.SubmittedByUserId);
    }

    [Fact]
    public async Task ProjectOffice_CannotDeleteApprovedYearly_AfterSubmittingAmendment()
    {
        await using var scope = await TestScope.CreateAsync();
        var approved = await scope.SeedYearlyAsync(40, ApprovalStatus.Approved);

        var amend = await scope.Service.UpdateYearlyAsync(
            approved.Id,
            YearlyUpdate(45, approved.RowVersion, "Remarks only"),
            scope.ProjectOffice,
            CancellationToken.None);
        Assert.True(amend.Success, amend.Error);

        var original = await scope.ReloadYearlyAsync(approved.Id);
        var delete = await scope.Service.DeleteYearlyAsync(
            approved.Id,
            Convert.ToBase64String(original.RowVersion),
            scope.ProjectOffice,
            CancellationToken.None);

        Assert.False(delete.Success);
        Assert.Contains("Admin or HoD", delete.Error);
        Assert.NotNull(await scope.FindYearlyAsync(approved.Id));
    }

    [Fact]
    public async Task ProjectOffice_CanWithdrawPendingAmendment_WithoutTouchingApprovedRecord()
    {
        await using var scope = await TestScope.CreateAsync();
        var approved = await scope.SeedYearlyAsync(40, ApprovalStatus.Approved);
        var amend = await scope.Service.UpdateYearlyAsync(
            approved.Id,
            YearlyUpdate(45, approved.RowVersion),
            scope.ProjectOffice,
            CancellationToken.None);
        var amendment = await scope.ReloadYearlyAsync(amend.EntityId!.Value);

        var delete = await scope.Service.DeleteYearlyAsync(
            amendment.Id,
            Convert.ToBase64String(amendment.RowVersion),
            scope.ProjectOffice,
            CancellationToken.None);

        Assert.True(delete.Success, delete.Error);
        Assert.Null(await scope.FindYearlyAsync(amendment.Id));
        var original = await scope.ReloadYearlyAsync(approved.Id);
        Assert.Equal(ApprovalStatus.Approved, original.ApprovalStatus);
        Assert.Equal(40, original.TotalQuantity);
    }

    [Fact]
    public async Task RejectingAmendment_KeepsApprovedFigureUnchanged()
    {
        await using var scope = await TestScope.CreateAsync();
        var approved = await scope.SeedYearlyAsync(40, ApprovalStatus.Approved);
        var amend = await scope.Service.UpdateYearlyAsync(
            approved.Id,
            YearlyUpdate(45, approved.RowVersion),
            scope.ProjectOffice,
            CancellationToken.None);
        var amendment = await scope.ReloadYearlyAsync(amend.EntityId!.Value);

        var decision = await scope.Service.DecideYearlyAsync(
            amendment.Id,
            approve: false,
            Convert.ToBase64String(amendment.RowVersion),
            "Count not supported by the unit return",
            scope.HoD,
            CancellationToken.None);

        Assert.True(decision.Success, decision.Error);
        var original = await scope.ReloadYearlyAsync(approved.Id);
        Assert.Equal(ApprovalStatus.Approved, original.ApprovalStatus);
        Assert.Equal(40, original.TotalQuantity);
        Assert.Equal(TestScope.SeedApproverUserId, original.ApprovedByUserId);
        Assert.Equal(ApprovalStatus.Rejected, (await scope.ReloadYearlyAsync(amendment.Id)).ApprovalStatus);
    }

    [Fact]
    public async Task ApprovingAmendment_AppliesItToApprovedRecord_AndRemovesAmendmentRow()
    {
        await using var scope = await TestScope.CreateAsync();
        var approved = await scope.SeedYearlyAsync(40, ApprovalStatus.Approved);
        var amend = await scope.Service.UpdateYearlyAsync(
            approved.Id,
            YearlyUpdate(45, approved.RowVersion, "Corrected count"),
            scope.ProjectOffice,
            CancellationToken.None);
        var amendment = await scope.ReloadYearlyAsync(amend.EntityId!.Value);

        var decision = await scope.Service.DecideYearlyAsync(
            amendment.Id,
            approve: true,
            Convert.ToBase64String(amendment.RowVersion),
            null,
            scope.HoD,
            CancellationToken.None);

        Assert.True(decision.Success, decision.Error);
        Assert.Equal(approved.Id, decision.EntityId);
        Assert.Null(await scope.FindYearlyAsync(amendment.Id));

        var original = await scope.ReloadYearlyAsync(approved.Id);
        Assert.Equal(ApprovalStatus.Approved, original.ApprovalStatus);
        Assert.Equal(45, original.TotalQuantity);
        Assert.Equal("Corrected count", original.Remarks);
        Assert.Equal(TestScope.HoDUserId, original.ApprovedByUserId);
        Assert.Equal(TestScope.ProjectOfficeUserId, original.SubmittedByUserId);
        Assert.Single(scope.Db.ProliferationYearlies.AsNoTracking().Where(y => y.ApprovalStatus == ApprovalStatus.Approved));
    }

    [Fact]
    public async Task AdminSavingPendingAmendment_AppliesItToApprovedRecord()
    {
        await using var scope = await TestScope.CreateAsync();
        var approved = await scope.SeedYearlyAsync(40, ApprovalStatus.Approved);
        var amend = await scope.Service.UpdateYearlyAsync(
            approved.Id,
            YearlyUpdate(45, approved.RowVersion),
            scope.ProjectOffice,
            CancellationToken.None);
        var amendment = await scope.ReloadYearlyAsync(amend.EntityId!.Value);

        var save = await scope.Service.UpdateYearlyAsync(
            amendment.Id,
            YearlyUpdate(47, amendment.RowVersion, "Checked against the unit return"),
            scope.Admin,
            CancellationToken.None);

        Assert.True(save.Success, save.Error);
        Assert.Equal(approved.Id, save.EntityId);
        Assert.Null(await scope.FindYearlyAsync(amendment.Id));
        var original = await scope.ReloadYearlyAsync(approved.Id);
        Assert.Equal(47, original.TotalQuantity);
        Assert.Equal(ApprovalStatus.Approved, original.ApprovalStatus);
        Assert.Equal(TestScope.AdminUserId, original.ApprovedByUserId);
    }

    [Fact]
    public async Task SecondAmendment_IsRefused_WhileFirstIsPending()
    {
        await using var scope = await TestScope.CreateAsync();
        var approved = await scope.SeedYearlyAsync(40, ApprovalStatus.Approved);
        var first = await scope.Service.UpdateYearlyAsync(
            approved.Id,
            YearlyUpdate(45, approved.RowVersion),
            scope.ProjectOffice,
            CancellationToken.None);
        Assert.True(first.Success, first.Error);

        var second = await scope.Service.UpdateYearlyAsync(
            approved.Id,
            YearlyUpdate(50, approved.RowVersion),
            scope.ProjectOffice,
            CancellationToken.None);

        Assert.False(second.Success);
        Assert.Contains("already awaiting approval", second.Error);
        Assert.Equal(1, await scope.Db.ProliferationYearlies.CountAsync(y => y.AmendsId == approved.Id));
    }

    [Theory]
    [InlineData(typeof(ProliferationYearly), "UX_ProlifYearly_PendingAmendment")]
    [InlineData(typeof(ProliferationGranular), "UX_ProlifGranular_PendingAmendment")]
    public async Task Database_AllowsOnlyOnePendingAmendmentPerRecord(Type entityType, string indexName)
    {
        // Concurrent amendments can both pass the service check; this index decides the race.
        await using var scope = await TestScope.CreateAsync();
        var index = scope.Db.Model.FindEntityType(entityType)!
            .GetIndexes()
            .Single(i => i.Name == indexName);

        Assert.True(index.IsUnique);
        Assert.Equal(new[] { "AmendsId" }, index.Properties.Select(p => p.Name));
        Assert.Equal("\"AmendsId\" IS NOT NULL AND \"ApprovalStatus\" = 0", index.GetFilter());
        Assert.Equal(0, (int)ApprovalStatus.Pending);
    }

    [Fact]
    public async Task AmendmentOfStaleApprovedRecord_IsRefused()
    {
        await using var scope = await TestScope.CreateAsync();
        var approved = await scope.SeedYearlyAsync(40, ApprovalStatus.Approved);

        var result = await scope.Service.UpdateYearlyAsync(
            approved.Id,
            YearlyUpdate(45, new byte[] { 9, 9, 9 }),
            scope.ProjectOffice,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("modified by another user", result.Error);
        Assert.Single(scope.Db.ProliferationYearlies.AsNoTracking());
    }

    [Theory]
    [InlineData(ApprovalStatus.Approved, true)]
    [InlineData(ApprovalStatus.Approved, false)]
    [InlineData(ApprovalStatus.Rejected, true)]
    [InlineData(ApprovalStatus.Rejected, false)]
    public async Task DecideYearly_RefusesRecordsThatAreNotPending(ApprovalStatus status, bool approve)
    {
        await using var scope = await TestScope.CreateAsync();
        var record = await scope.SeedYearlyAsync(40, status);

        var decision = await scope.Service.DecideYearlyAsync(
            record.Id,
            approve,
            Convert.ToBase64String(record.RowVersion),
            "Reason",
            scope.HoD,
            CancellationToken.None);

        Assert.False(decision.Success);
        Assert.Contains("no longer pending", decision.Error);
        var reloaded = await scope.ReloadYearlyAsync(record.Id);
        Assert.Equal(status, reloaded.ApprovalStatus);
        Assert.Equal(record.ApprovedByUserId, reloaded.ApprovedByUserId);
    }

    [Theory]
    [InlineData(ApprovalStatus.Approved)]
    [InlineData(ApprovalStatus.Rejected)]
    public async Task DecideGranular_RefusesRecordsThatAreNotPending(ApprovalStatus status)
    {
        await using var scope = await TestScope.CreateAsync();
        var record = await scope.SeedGranularAsync(12, status);

        var decision = await scope.Service.DecideGranularAsync(
            record.Id,
            approve: status == ApprovalStatus.Rejected,
            Convert.ToBase64String(record.RowVersion),
            "Reason",
            scope.HoD,
            CancellationToken.None);

        Assert.False(decision.Success);
        Assert.Contains("no longer pending", decision.Error);
        Assert.Equal(status, (await scope.ReloadGranularAsync(record.Id)).ApprovalStatus);
    }

    [Fact]
    public async Task GranularAmendment_CanChangeUnitAndDate_AndIsAppliedOnApproval()
    {
        await using var scope = await TestScope.CreateAsync();
        var approved = await scope.SeedGranularAsync(12, ApprovalStatus.Approved);

        var amend = await scope.Service.UpdateGranularAsync(
            approved.Id,
            new ProliferationGranularUpdateDto
            {
                ProjectId = ProjectId,
                Source = ProliferationSource.Sdd,
                UnitName = "2 Corps Training Wing",
                ProliferationDateUtc = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
                Quantity = 14,
                RowVersion = Convert.ToBase64String(approved.RowVersion)
            },
            scope.ProjectOffice,
            CancellationToken.None);

        Assert.True(amend.Success, amend.Error);
        var original = await scope.ReloadGranularAsync(approved.Id);
        Assert.Equal(ApprovalStatus.Approved, original.ApprovalStatus);
        Assert.Equal(12, original.Quantity);
        Assert.Equal("1 Corps Training Wing", original.UnitName);

        var amendment = await scope.ReloadGranularAsync(amend.EntityId!.Value);
        Assert.Equal(approved.Id, amendment.AmendsId);

        var decision = await scope.Service.DecideGranularAsync(
            amendment.Id,
            approve: true,
            Convert.ToBase64String(amendment.RowVersion),
            null,
            scope.HoD,
            CancellationToken.None);

        Assert.True(decision.Success, decision.Error);
        Assert.Equal(approved.Id, decision.EntityId);
        Assert.Null(await scope.FindGranularAsync(amendment.Id));
        original = await scope.ReloadGranularAsync(approved.Id);
        Assert.Equal(14, original.Quantity);
        Assert.Equal("2 Corps Training Wing", original.UnitName);
        Assert.Equal(new DateOnly(2026, 7, 15), original.ProliferationDate);
    }

    [Fact]
    public async Task ProjectOffice_CannotDeleteApprovedGranular_AfterSubmittingAmendment()
    {
        await using var scope = await TestScope.CreateAsync();
        var approved = await scope.SeedGranularAsync(12, ApprovalStatus.Approved);
        var amend = await scope.Service.UpdateGranularAsync(
            approved.Id,
            new ProliferationGranularUpdateDto
            {
                ProjectId = ProjectId,
                Source = ProliferationSource.Sdd,
                UnitName = approved.UnitName,
                ProliferationDateUtc = approved.ProliferationDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                Quantity = 12,
                Remarks = "Remarks only",
                RowVersion = Convert.ToBase64String(approved.RowVersion)
            },
            scope.ProjectOffice,
            CancellationToken.None);
        Assert.True(amend.Success, amend.Error);

        var original = await scope.ReloadGranularAsync(approved.Id);
        var delete = await scope.Service.DeleteGranularAsync(
            approved.Id,
            Convert.ToBase64String(original.RowVersion),
            scope.ProjectOffice,
            CancellationToken.None);

        Assert.False(delete.Success);
        Assert.Equal(ApprovalStatus.Approved, (await scope.ReloadGranularAsync(approved.Id)).ApprovalStatus);
    }

    [Fact]
    public async Task AdminDeleteOfApprovedRecord_AlsoRemovesItsAmendments()
    {
        await using var scope = await TestScope.CreateAsync();
        var approved = await scope.SeedYearlyAsync(40, ApprovalStatus.Approved);
        var amend = await scope.Service.UpdateYearlyAsync(
            approved.Id,
            YearlyUpdate(45, approved.RowVersion),
            scope.ProjectOffice,
            CancellationToken.None);
        Assert.True(amend.Success, amend.Error);

        var original = await scope.ReloadYearlyAsync(approved.Id);
        var delete = await scope.Service.DeleteYearlyAsync(
            approved.Id,
            Convert.ToBase64String(original.RowVersion),
            scope.Admin,
            CancellationToken.None);

        Assert.True(delete.Success, delete.Error);
        Assert.Empty(scope.Db.ProliferationYearlies.AsNoTracking());
    }

    [Fact]
    public async Task ApprovingAmendment_StillRefusesDuplicateApprovedYear()
    {
        await using var scope = await TestScope.CreateAsync();
        var approved2025 = await scope.SeedYearlyAsync(40, ApprovalStatus.Approved, year: 2025);
        await scope.SeedYearlyAsync(30, ApprovalStatus.Approved, year: 2024);

        // Amendment moves the 2025 figure onto 2024, which already has its own approved row.
        var amend = await scope.Service.UpdateYearlyAsync(
            approved2025.Id,
            YearlyUpdate(40, approved2025.RowVersion, year: 2024),
            scope.ProjectOffice,
            CancellationToken.None);
        Assert.True(amend.Success, amend.Error);
        var amendment = await scope.ReloadYearlyAsync(amend.EntityId!.Value);

        var decision = await scope.Service.DecideYearlyAsync(
            amendment.Id,
            approve: true,
            Convert.ToBase64String(amendment.RowVersion),
            null,
            scope.HoD,
            CancellationToken.None);

        Assert.False(decision.Success);
        Assert.Contains("already exists", decision.Error);
        Assert.Equal(2025, (await scope.ReloadYearlyAsync(approved2025.Id)).Year);
    }

    [Fact]
    public async Task ApprovingAmendmentOfDeletedRecord_ApprovesItAsStandaloneRecord()
    {
        await using var scope = await TestScope.CreateAsync();
        var amendment = new ProliferationYearly
        {
            Id = Guid.NewGuid(),
            ProjectId = ProjectId,
            Source = ProliferationSource.Sdd,
            Year = 2025,
            TotalQuantity = 45,
            AmendsId = Guid.NewGuid(),
            ApprovalStatus = ApprovalStatus.Pending,
            SubmittedByUserId = TestScope.ProjectOfficeUserId,
            CreatedOnUtc = Now.UtcDateTime,
            LastUpdatedOnUtc = Now.UtcDateTime,
            RowVersion = Guid.NewGuid().ToByteArray()
        };
        scope.Db.ProliferationYearlies.Add(amendment);
        await scope.Db.SaveChangesAsync();
        scope.Db.ChangeTracker.Clear();
        amendment = await scope.ReloadYearlyAsync(amendment.Id);

        var decision = await scope.Service.DecideYearlyAsync(
            amendment.Id,
            approve: true,
            Convert.ToBase64String(amendment.RowVersion),
            null,
            scope.HoD,
            CancellationToken.None);

        Assert.True(decision.Success, decision.Error);
        Assert.Equal(amendment.Id, decision.EntityId);
        var approved = await scope.ReloadYearlyAsync(amendment.Id);
        Assert.Equal(ApprovalStatus.Approved, approved.ApprovalStatus);
        Assert.Null(approved.AmendsId);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("Approved", false)]
    [InlineData("approved", false)]
    [InlineData("unknown", false)]
    [InlineData("All", true)]
    [InlineData("pending", true)]
    [InlineData("Rejected", true)]
    public void LegacyReports_TreatOnlyApprovedAsOpenToViewers(string? status, bool requestsUnapproved)
    {
        Assert.Equal(requestsUnapproved, ProliferationReportsService.RequestsUnapprovedRecords(status));
    }

    [Theory]
    [InlineData("All")]
    [InlineData("Pending")]
    [InlineData("Rejected")]
    public async Task LegacyReportsExport_ForbidsUnapprovedRecordsToViewersWhoCannotSubmit(string status)
    {
        var controller = new ProliferationReportsController(
            svc: null!,
            new DenyingAuthorizationService(),
            new NoOpAuditService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, "viewer") },
                        "Test"))
                }
            }
        };

        var query = new ProliferationReportQueryDto
        {
            Report = ProliferationReportKind.GranularLedger,
            ApprovalStatus = status
        };

        Assert.IsType<ForbidResult>(await controller.Export(query, CancellationToken.None));
        Assert.IsType<ForbidResult>((await controller.Run(query, CancellationToken.None)).Result);
    }

    private static ProliferationYearlyUpdateDto YearlyUpdate(int quantity, byte[] rowVersion, string? remarks = null, int year = 2025)
        => new()
        {
            ProjectId = ProjectId,
            Source = ProliferationSource.Sdd,
            Year = year,
            TotalQuantity = quantity,
            Remarks = remarks,
            RowVersion = Convert.ToBase64String(rowVersion)
        };

    private sealed class TestScope : IAsyncDisposable
    {
        public const string AdminUserId = "workflow-admin";
        public const string HoDUserId = "workflow-hod";
        public const string ProjectOfficeUserId = "workflow-po";
        public const string SeedApproverUserId = "workflow-seed-approver";

        private readonly ServiceProvider _services;

        private TestScope(ApplicationDbContext db, UserManager<ApplicationUser> users, ServiceProvider services)
        {
            Db = db;
            Users = users;
            _services = services;
            Admin = Principal(AdminUserId, "Admin");
            HoD = Principal(HoDUserId, "HoD");
            ProjectOffice = Principal(ProjectOfficeUserId, "ProjectOffice");
            Service = new ProliferationSubmissionService(db, users, new FixedClock(Now), new NoOpAuditService());
        }

        public ApplicationDbContext Db { get; }
        public UserManager<ApplicationUser> Users { get; }
        public ProliferationSubmissionService Service { get; }
        public ClaimsPrincipal Admin { get; }
        public ClaimsPrincipal HoD { get; }
        public ClaimsPrincipal ProjectOffice { get; }

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

            foreach (var userId in new[] { AdminUserId, HoDUserId, ProjectOfficeUserId })
            {
                db.Users.Add(new ApplicationUser
                {
                    Id = userId,
                    UserName = userId,
                    NormalizedUserName = userId.ToUpperInvariant(),
                    FullName = userId
                });
            }

            db.Projects.Add(new Project
            {
                Id = ProjectId,
                Name = "Driving simulator",
                CreatedByUserId = "creator",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                RowVersion = Guid.NewGuid().ToByteArray()
            });
            await db.SaveChangesAsync();

            return new TestScope(db, users, services);
        }

        public async Task<ProliferationYearly> SeedYearlyAsync(int quantity, ApprovalStatus status, int year = 2025)
        {
            var record = new ProliferationYearly
            {
                Id = Guid.NewGuid(),
                ProjectId = ProjectId,
                Source = ProliferationSource.Sdd,
                Year = year,
                TotalQuantity = quantity,
                ApprovalStatus = status,
                SubmittedByUserId = SeedApproverUserId,
                ApprovedByUserId = status == ApprovalStatus.Approved ? SeedApproverUserId : null,
                ApprovedOnUtc = status == ApprovalStatus.Approved ? Now.UtcDateTime.AddDays(-30) : null,
                CreatedOnUtc = Now.UtcDateTime.AddDays(-30),
                LastUpdatedOnUtc = Now.UtcDateTime.AddDays(-30),
                RowVersion = Guid.NewGuid().ToByteArray()
            };
            Db.ProliferationYearlies.Add(record);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return await ReloadYearlyAsync(record.Id);
        }

        public async Task<ProliferationGranular> SeedGranularAsync(int quantity, ApprovalStatus status)
        {
            var record = new ProliferationGranular
            {
                Id = Guid.NewGuid(),
                ProjectId = ProjectId,
                Source = ProliferationSource.Sdd,
                UnitName = "1 Corps Training Wing",
                ProliferationDate = new DateOnly(2026, 6, 1),
                Quantity = quantity,
                ApprovalStatus = status,
                SubmittedByUserId = SeedApproverUserId,
                ApprovedByUserId = status == ApprovalStatus.Approved ? SeedApproverUserId : null,
                ApprovedOnUtc = status == ApprovalStatus.Approved ? Now.UtcDateTime.AddDays(-30) : null,
                CreatedOnUtc = Now.UtcDateTime.AddDays(-30),
                LastUpdatedOnUtc = Now.UtcDateTime.AddDays(-30),
                RowVersion = Guid.NewGuid().ToByteArray()
            };
            Db.ProliferationGranularEntries.Add(record);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return await ReloadGranularAsync(record.Id);
        }

        public Task<ProliferationYearly?> FindYearlyAsync(Guid id)
            => Db.ProliferationYearlies.AsNoTracking().FirstOrDefaultAsync(y => y.Id == id);

        public async Task<ProliferationYearly> ReloadYearlyAsync(Guid id)
            => await FindYearlyAsync(id) ?? throw new InvalidOperationException($"Yearly record {id} not found.");

        public Task<ProliferationGranular?> FindGranularAsync(Guid id)
            => Db.ProliferationGranularEntries.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id);

        public async Task<ProliferationGranular> ReloadGranularAsync(Guid id)
            => await FindGranularAsync(id) ?? throw new InvalidOperationException($"Granular record {id} not found.");

        private static ClaimsPrincipal Principal(string userId, string role)
            => new(new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim(ClaimTypes.Name, userId),
                    new Claim(ClaimTypes.Role, role)
                },
                "Test"));

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
            HttpContext? http = null)
            => Task.CompletedTask;
    }

    private sealed class DenyingAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Failed());
    }
}
