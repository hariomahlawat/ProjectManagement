using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
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
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

// Write-path integrity on a relational provider: a proliferation change and its audit entry
// commit together, and the database allows one approved annual figure per project, source
// and year.
public sealed class ProliferationWriteIntegrityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 8, 0, 0, TimeSpan.Zero);
    private const string AdminUserId = "integrity-admin";

    [Fact]
    public async Task FailedAuditWrite_RollsBackTheRecord()
    {
        await using var scope = await Scope.CreateAsync(new ThrowingAudit());

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Service.CreateYearlyAsync(
            new ProliferationYearlyCreateDto { ProjectId = 1, Source = ProliferationSource.Sdd, Year = 2025, TotalQuantity = 4 },
            scope.Admin,
            CancellationToken.None));

        // Before, the record was committed and the user was shown an error for it; a retry
        // then created a duplicate.
        Assert.Equal(0, await scope.Db.ProliferationYearlies.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task SuccessfulWrite_CommitsRecordAndAudit()
    {
        var audit = new CountingAudit();
        await using var scope = await Scope.CreateAsync(audit);

        var result = await scope.Service.CreateYearlyAsync(
            new ProliferationYearlyCreateDto { ProjectId = 1, Source = ProliferationSource.Sdd, Year = 2025, TotalQuantity = 4 },
            scope.Admin,
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(1, await scope.Db.ProliferationYearlies.AsNoTracking().CountAsync());
        Assert.Equal(1, audit.Count);
        Assert.Null(scope.Db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Database_RefusesSecondApprovedAnnualRowForTheSameYear()
    {
        await using var scope = await Scope.CreateAsync(new CountingAudit());
        scope.Db.ProliferationYearlies.Add(Yearly(ApprovalStatus.Approved));
        await scope.Db.SaveChangesAsync();

        // Pending and rejected rows for the same year are allowed; a second approved one is not.
        scope.Db.ProliferationYearlies.AddRange(Yearly(ApprovalStatus.Pending), Yearly(ApprovalStatus.Rejected));
        await scope.Db.SaveChangesAsync();

        scope.Db.ProliferationYearlies.Add(Yearly(ApprovalStatus.Approved));
        await Assert.ThrowsAsync<DbUpdateException>(() => scope.Db.SaveChangesAsync());
    }

    private static ProliferationYearly Yearly(ApprovalStatus status) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = 1,
        Source = ProliferationSource.Sdd,
        Year = 2025,
        TotalQuantity = 4,
        ApprovalStatus = status,
        SubmittedByUserId = AdminUserId,
        CreatedOnUtc = Now.UtcDateTime,
        LastUpdatedOnUtc = Now.UtcDateTime,
        RowVersion = Guid.NewGuid().ToByteArray()
    };

    private sealed class Scope : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _services;
        private readonly UserManager<ApplicationUser> _users;

        private Scope(SqliteConnection connection, ApplicationDbContext db, ServiceProvider services, UserManager<ApplicationUser> users, IAuditService audit)
        {
            _connection = connection;
            _services = services;
            _users = users;
            Db = db;
            Service = new ProliferationSubmissionService(db, users, FakeClock.AtUtc(Now), audit);
            Admin = new ClaimsPrincipal(new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, AdminUserId),
                    new Claim(ClaimTypes.Name, AdminUserId),
                    new Claim(ClaimTypes.Role, "Admin")
                },
                "Test"));
        }

        public ApplicationDbContext Db { get; }
        public ProliferationSubmissionService Service { get; }
        public ClaimsPrincipal Admin { get; }

        public static async Task<Scope> CreateAsync(IAuditService audit)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options);
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

            db.Users.Add(new ApplicationUser
            {
                Id = AdminUserId,
                UserName = AdminUserId,
                NormalizedUserName = AdminUserId.ToUpperInvariant(),
                FullName = "Integrity Admin"
            });
            db.Projects.Add(new Project
            {
                Id = 1,
                Name = "Simulator",
                CreatedByUserId = AdminUserId,
                LifecycleStatus = ProjectLifecycleStatus.Completed
            });
            await db.SaveChangesAsync();

            return new Scope(connection, db, services, users, audit);
        }

        public async ValueTask DisposeAsync()
        {
            _users.Dispose();
            await Db.DisposeAsync();
            await _services.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class ThrowingAudit : IAuditService
    {
        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null,
            string? userName = null, IDictionary<string, string?>? data = null, HttpContext? http = null)
            => throw new InvalidOperationException("Audit store unavailable.");
    }

    private sealed class CountingAudit : IAuditService
    {
        public int Count { get; private set; }

        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null,
            string? userName = null, IDictionary<string, string?>? data = null, HttpContext? http = null)
        {
            Count++;
            return Task.CompletedTask;
        }
    }
}
