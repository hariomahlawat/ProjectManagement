using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectManagement.Data;

namespace ProjectManagement.Areas.Admin.Pages.Diagnostics;

[Authorize(Roles = "Admin")]
public sealed class DbHealthModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public DbHealthModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public bool IsRelational { get; private set; }

    public string? DatabaseName { get; private set; }

    public string? Host { get; private set; }

    public string LatestMigration { get; private set; } = "(not available)";

    public IReadOnlyList<string> PendingMigrations { get; private set; } = Array.Empty<string>();

    public bool HasPendingMigrations => PendingMigrations.Count > 0;

    public async Task OnGetAsync()
    {
        IsRelational = _db.Database.IsRelational();
        if (!IsRelational)
        {
            return;
        }

        ResolveConnectionMetadata();

        var applied = (await _db.Database.GetAppliedMigrationsAsync()).ToList();
        LatestMigration = applied.Count > 0 ? applied[^1] : "(none)";

        PendingMigrations = (await _db.Database.GetPendingMigrationsAsync())
            .OrderBy(m => m)
            .ToList();
    }

    private void ResolveConnectionMetadata()
    {
        var connectionString = _db.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            DatabaseName = builder.Database;
            Host = builder.Host;
        }
        catch (ArgumentException)
        {
            var connection = _db.Database.GetDbConnection();
            DatabaseName = connection.Database;
            Host = connection.DataSource;
        }
    }
}
