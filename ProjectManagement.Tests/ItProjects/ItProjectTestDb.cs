using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services.ItProjects;
using ProjectManagement.Tests.Fakes;

namespace ProjectManagement.Tests.ItProjects;

// SECTION: Shared relational (SQLite) database for IT project tests
// One open in-memory connection lets several DbContexts see the same rows, which is how a stale
// row version from another request is simulated.
internal sealed class ItProjectTestDb : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly List<ApplicationDbContext> _contexts = new();

    public static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 30, 0, TimeSpan.Zero);

    private ItProjectTestDb(SqliteConnection connection)
    {
        _connection = connection;
    }

    public RecordingAudit Audit { get; } = new();

    public FakeClock Clock { get; } = FakeClock.AtUtc(Now);

    public static async Task<ItProjectTestDb> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var db = new ItProjectTestDb(connection);
        await db.NewContext().Database.EnsureCreatedAsync();
        return db;
    }

    public ApplicationDbContext NewContext()
    {
        var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        _contexts.Add(context);
        return context;
    }

    public ItProjectCommandService Commands(ApplicationDbContext? context = null) =>
        new(context ?? NewContext(), Audit, Clock);

    public ItProjectReadService Reads(ApplicationDbContext? context = null) =>
        new(context ?? NewContext());

    public ItProjectRemarkService Remarks(ApplicationDbContext? context = null) =>
        new(context ?? NewContext(), Audit, Clock);

    public static ItProjectRemarkActor RemarkActor(string userId, string? role, bool isAdmin = false) => new(userId, userId, role, isAdmin);

    public static ItProjectActor Actor(string userId = "ito-1") => new(userId, userId);

    public static ItProjectDetailsInput Details(string title = "Core switches, Admin block", string reference = "IT PPP 09/2025", int fy = 2025) =>
        new(title, reference, fy, "Replacement of the two end-of-life core switches.");

    public async Task<(int Id, byte[] RowVersion)> SeedAsync(string title = "Core switches, Admin block", string reference = "IT PPP 09/2025", int fy = 2025)
    {
        var (result, id) = await Commands().CreateAsync(Details(title, reference, fy), Actor());
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors));
        }

        return (id, await RowVersionAsync(id));
    }

    public async Task<byte[]> RowVersionAsync(int id)
    {
        await using var context = NewContext();
        return await context.ItProjects.Where(project => project.Id == id).Select(project => project.RowVersion).SingleAsync();
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
