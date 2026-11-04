using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.IndustryPartners;

namespace ProjectManagement.Tests.IndustryPartners;

// SECTION: Shared relational (SQLite) database for Industry Partner tests
// A single open in-memory connection lets several DbContexts see the same data, which
// is how concurrent requests are simulated.
internal sealed class IndustryPartnerTestDb : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly List<ApplicationDbContext> _contexts = new();

    private IndustryPartnerTestDb(SqliteConnection connection)
    {
        _connection = connection;
    }

    public static async Task<IndustryPartnerTestDb> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var db = new IndustryPartnerTestDb(connection);
        await db.NewContext().Database.EnsureCreatedAsync();
        return db;
    }

    public ApplicationDbContext NewContext(params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection);
        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        var context = new ApplicationDbContext(builder.Options);
        _contexts.Add(context);
        return context;
    }

    public async Task<int> AddProjectAsync(string name, string? leadPoUserId = null, bool isDeleted = false)
    {
        await using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        if (leadPoUserId is not null && !await context.Users.AnyAsync(user => user.Id == leadPoUserId))
        {
            context.Users.Add(new ApplicationUser
            {
                Id = leadPoUserId,
                UserName = leadPoUserId,
                NormalizedUserName = leadPoUserId.ToUpperInvariant()
            });
        }

        var project = new Project
        {
            Name = name,
            CreatedByUserId = "seed",
            CreatedAt = new DateTime(2026, 1, 1),
            LeadPoUserId = leadPoUserId,
            IsDeleted = isDeleted,
            LifecycleStatus = ProjectLifecycleStatus.Active
        };
        context.Projects.Add(project);
        await context.SaveChangesAsync();
        return project.Id;
    }

    public async Task<int> AddPartnerAsync(string name, string createdByUserId = "seed")
    {
        await using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        var partner = new IndustryPartner
        {
            Name = name,
            NormalizedName = name.ToLowerInvariant(),
            CreatedByUserId = createdByUserId,
            CreatedUtc = DateTimeOffset.UtcNow
        };
        context.IndustryPartners.Add(partner);
        await context.SaveChangesAsync();
        return partner.Id;
    }

    public async Task AddLinkAsync(int partnerId, int projectId)
    {
        await using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        context.IndustryPartnerProjects.Add(new IndustryPartnerProject
        {
            IndustryPartnerId = partnerId,
            ProjectId = projectId,
            LinkedByUserId = "seed",
            LinkedUtc = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
    }

    public async Task<bool> LinkExistsAsync(int partnerId, int projectId)
    {
        await using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        return await context.IndustryPartnerProjects.AnyAsync(link =>
            link.IndustryPartnerId == partnerId && link.ProjectId == projectId);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var context in _contexts)
        {
            await context.DisposeAsync();
        }

        await _connection.DisposeAsync();
    }

    public static ClaimsPrincipal User(string userId, params string[] roles) => new(
        new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId) }
                .Concat(roles.Select(role => new Claim(ClaimTypes.Role, role))),
            "Test",
            ClaimTypes.Name,
            ClaimTypes.Role));
}
