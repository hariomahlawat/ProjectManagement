using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ProjectManagement.Data;
using ProjectManagement.Features.Collage;
using ProjectManagement.Services;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Imaging;
using ProjectManagement.Services.Storage;
using ProjectManagement.Tests.Fakes;
using SkiaSharp;
using Xunit;

namespace ProjectManagement.Tests.Tools;

/// <summary>
/// Collage Studio "Crests and logos" on a real PostgreSQL server: every sticker change and its
/// audit entry are one transaction under one advisory lock. Two uploads at once never pass the
/// limit of 60, and a failed audit write rolls the change back and removes the picture. The test
/// creates, migrates and drops its own prism_test_collage_* database on the
/// PRISM_TEST_POSTGRES_CONNECTION server.
/// </summary>
public sealed class CollageStickerPostgresIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 8, 0, 0, TimeSpan.Zero);

    [PostgresMigrationFact]
    public async Task TwoUploadsAtOnceNeverPassTheLimit_AndAFailedAuditRollsTheChangeBack()
    {
        var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")!);
        var databaseName = $"prism_test_collage_{Guid.NewGuid():N}"[..40];
        var maintenance = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = "postgres" }.ConnectionString;
        var target = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = databaseName }.ConnectionString;
        var root = Path.Combine(Path.GetTempPath(), "collage-pg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        await ExecuteAsync(maintenance, $"CREATE DATABASE \"{databaseName}\"");
        try
        {
            await using (var db = Context(target))
            {
                await db.Database.MigrateAsync();
            }

            // 59 in use: room for exactly one more.
            for (var i = 1; i <= CollageStickerService.MaxActive - 1; i++)
            {
                await ExecuteAsync(target,
                    "INSERT INTO \"CollageStickers\" (\"Name\", \"StorageKey\", \"Width\", \"Height\", \"Version\", \"SortOrder\", \"IsRetired\", \"CreatedAtUtc\", \"UpdatedAtUtc\") " +
                    $"VALUES ('S{i}', 'collage-stickers/s{i}.png', 10, 10, 1, {i}, false, now(), now())");
            }

            // 1. Two administrators upload at the same moment: one gets the last place, one is refused.
            // The stickers' lock is held from another connection until both uploads are waiting on
            // it, so they truly meet at the count; without the lock in the service they would never
            // wait, and this test would fail instead of passing by luck.
            await using var first = Context(target);
            await using var second = Context(target);
            await using var blocker = new NpgsqlConnection(target);
            await blocker.OpenAsync();
            await using var blockerTransaction = await blocker.BeginTransactionAsync();
            await using (var hold = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext('collage-stickers'))", blocker, blockerTransaction))
            {
                await hold.ExecuteNonQueryAsync();
            }
            var uploads = new[]
            {
                Service(first, root, RealAudit(first)).AddAsync(new MemoryStream(Png()), "a.png", "First", null, "admin-1"),
                Service(second, root, RealAudit(second)).AddAsync(new MemoryStream(Png()), "b.png", "Second", null, "admin-2")
            };
            await WaitForWaitingSessionsAsync(target, 2, uploads);
            await blockerTransaction.CommitAsync();
            var outcomes = await Task.WhenAll(uploads);
            Assert.Single(outcomes, outcome => outcome.Succeeded);
            Assert.Single(outcomes, outcome => !outcome.Succeeded);
            Assert.Equal(CollageStickerService.MaxActive, await CountAsync(target, "SELECT COUNT(*) FROM \"CollageStickers\" WHERE NOT \"IsRetired\""));
            Assert.Equal(1, await CountAsync(target, "SELECT COUNT(*) FROM \"AuditLogs\" WHERE \"Action\" = 'CollageStickerAdded'"));
            // The refused upload's picture is not left behind; nothing half-written either.
            var folder = Path.Combine(root, CollageStickerService.Folder);
            Assert.Single(Directory.GetFiles(folder, "*.png"));
            Assert.Empty(Directory.GetFiles(folder, "*.tmp"));

            // 2. The audit write fails: the rename is rolled back with it.
            await using (var db = Context(target))
            {
                var id = await CountAsync(target, "SELECT MIN(\"Id\") FROM \"CollageStickers\"");
                var failing = Service(db, root, new FailingAudit());
                await Assert.ThrowsAsync<InvalidOperationException>(() => failing.RenameAsync(id, "Renamed", null, "admin-1"));
                Assert.Equal(0, await CountAsync(target, "SELECT COUNT(*) FROM \"CollageStickers\" WHERE \"Name\" = 'Renamed'"));
            }

            // 3. The audit write fails on an upload: no row, and its picture is removed.
            await ExecuteAsync(target, "UPDATE \"CollageStickers\" SET \"IsRetired\" = true WHERE \"Name\" = 'S1'");
            await using (var db = Context(target))
            {
                var failing = Service(db, root, new FailingAudit());
                await Assert.ThrowsAsync<InvalidOperationException>(() => failing.AddAsync(new MemoryStream(Png()), "c.png", "Third", null, "admin-1"));
            }
            Assert.Equal(0, await CountAsync(target, "SELECT COUNT(*) FROM \"CollageStickers\" WHERE \"Name\" = 'Third'"));
            Assert.Single(Directory.GetFiles(folder, "*.png"));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(maintenance, $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)");
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static ApplicationDbContext Context(string connection) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options);

    private static IAdminAuditService RealAudit(ApplicationDbContext db)
    {
        var clock = FakeClock.AtUtc(Now);
        return new AdminAuditService(new AuditService(db, new HttpContextAccessor(), clock, NullLogger<AuditService>.Instance), new HttpContextAccessor());
    }

    private static CollageStickerService Service(ApplicationDbContext db, string root, IAdminAuditService audit)
    {
        var roots = new Root(root);
        return new CollageStickerService(db, roots, new UploadPathResolver(roots), audit, FakeClock.AtUtc(Now));
    }

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(40, 40, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(SKColors.DarkRed);
        return PrismImageCodec.EncodePng(bitmap);
    }

    /** Waits until this many sessions of the test database are waiting on a lock. */
    private static async Task WaitForWaitingSessionsAsync(string connectionString, int expected, IReadOnlyList<Task<CollageStickerOutcome>> uploads)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            // An upload that failed before reaching the lock is shown, not waited on.
            var faulted = uploads.FirstOrDefault(upload => upload.IsFaulted);
            if (faulted != null) await faulted;
            Assert.False(uploads.Any(upload => upload.IsCompleted), "An upload finished without waiting for the stickers' lock.");

            var waiting = await CountAsync(connectionString,
                "SELECT COUNT(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'");
            if (waiting >= expected) return;
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(60), $"Only {waiting} of {expected} uploads reached the stickers' lock.");
            await Task.Delay(50);
        }
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed class FailingAudit : IAdminAuditService
    {
        public Task RecordAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The audit log could not be written.");
    }

    private sealed class Root(string root) : IUploadRootProvider
    {
        public string RootPath { get; } = root;
        public string GetProjectRoot(int projectId) => RootPath;
        public string GetProjectPhotosRoot(int projectId) => RootPath;
        public string GetProjectDocumentsRoot(int projectId) => RootPath;
        public string GetProjectCommentsRoot(int projectId) => RootPath;
        public string GetProjectVideosRoot(int projectId) => RootPath;
        public string GetSocialMediaRoot(string storagePrefix, Guid eventId) => RootPath;
    }
}
