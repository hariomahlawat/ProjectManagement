using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Storage;
using ProjectManagement.Tests.Fakes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// Concurrent uploads to one project used to read MAX(Ordinal) and the cover before either
/// saved, then collide on the unique (ProjectId, Ordinal) index, so all but one failed.
/// Uploads now lock the project row (FOR UPDATE) and decide the ordinal and the cover under
/// that lock.
/// </summary>
[Collection("Upload root environment")]
public sealed class ProjectPhotoConcurrentUploadPostgresTests
{
    private const int ProjectId = 7;
    private const int Uploads = 4;

    [PostgresMigrationFact]
    public async Task Concurrent_uploads_to_one_project_all_succeed_with_distinct_ordinals_and_one_cover()
    {
        var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")!);
        var databaseName = $"prism_test_photolock_{Guid.NewGuid():N}"[..40];
        var maintenance = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = "postgres" }.ConnectionString;
        var target = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = databaseName }.ConnectionString;
        var previousUploadRoot = Environment.GetEnvironmentVariable("PM_UPLOAD_ROOT");
        var uploadRoot = Path.Combine(Path.GetTempPath(), "pm-photo-lock-tests", Guid.NewGuid().ToString("N"));

        await ExecuteAsync(maintenance, $"CREATE DATABASE \"{databaseName}\"");
        try
        {
            Directory.CreateDirectory(uploadRoot);
            Environment.SetEnvironmentVariable("PM_UPLOAD_ROOT", uploadRoot);

            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(target).Options;
            await using (var setup = new ApplicationDbContext(options))
            {
                await setup.Database.MigrateAsync();
                setup.Users.Add(new ApplicationUser { Id = "creator", UserName = "creator", FullName = "creator" });
                setup.Projects.Add(new Project { Id = ProjectId, Name = "Lock test", CreatedByUserId = "creator" });
                await setup.SaveChangesAsync();
            }

            var image = await CreatePngAsync();

            // Hold the project row so every upload reaches the point where it decides its
            // ordinal before any of them can commit. Without the upload lock, each reads
            // MAX(Ordinal) = 0 and then waits only on its insert's foreign-key check.
            await using var blocker = new NpgsqlConnection(target);
            await blocker.OpenAsync();
            await using var blockerTransaction = await blocker.BeginTransactionAsync();
            await using (var lockRow = new NpgsqlCommand($"SELECT 1 FROM \"Projects\" WHERE \"Id\" = {ProjectId} FOR UPDATE", blocker, blockerTransaction))
            {
                await lockRow.ExecuteNonQueryAsync();
            }

            var contexts = Enumerable.Range(0, Uploads).Select(_ => new ApplicationDbContext(options)).ToList();
            try
            {
                var uploads = contexts
                    .Select((db, index) => Task.Run(() => CreateService(db).AddAsync(
                        ProjectId,
                        new MemoryStream(image),
                        $"photo-{index}.png",
                        "image/png",
                        "creator",
                        setAsCover: false,
                        caption: null,
                        CancellationToken.None)))
                    .ToList();

                await WaitForBlockedSessionsAsync(target, Uploads, uploads);
                await blockerTransaction.CommitAsync();

                var photos = await Task.WhenAll(uploads);
                Assert.Equal(Enumerable.Range(1, Uploads), photos.Select(photo => photo.Ordinal).OrderBy(ordinal => ordinal));
            }
            finally
            {
                foreach (var db in contexts)
                {
                    await db.DisposeAsync();
                }
            }

            await using var check = new ApplicationDbContext(options);
            var stored = await check.ProjectPhotos.AsNoTracking().Where(photo => photo.ProjectId == ProjectId).ToListAsync();
            Assert.Equal(Uploads, stored.Count);
            var cover = Assert.Single(stored, photo => photo.IsCover);
            var project = await check.Projects.AsNoTracking().SingleAsync(p => p.Id == ProjectId);
            Assert.Equal(cover.Id, project.CoverPhotoId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PM_UPLOAD_ROOT", previousUploadRoot);
            if (Directory.Exists(uploadRoot))
            {
                Directory.Delete(uploadRoot, recursive: true);
            }

            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(maintenance, $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)");
        }
    }

    private static async Task WaitForBlockedSessionsAsync(string connectionString, int expected, IReadOnlyList<Task<ProjectPhoto>> uploads)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            // Surface an upload that failed before reaching the lock.
            var faulted = uploads.FirstOrDefault(upload => upload.IsFaulted);
            if (faulted != null)
            {
                await faulted;
            }

            var waiting = await CountAsync(
                connectionString,
                "SELECT COUNT(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'");
            if (waiting >= expected)
            {
                return;
            }

            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(60), $"Only {waiting} of {expected} uploads reached the project lock.");
            await Task.Delay(50);
        }
    }

    private static ProjectPhotoService CreateService(ApplicationDbContext db)
    {
        var options = Options.Create(new ProjectPhotoOptions
        {
            AllowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/webp" },
            Derivatives = new Dictionary<string, ProjectPhotoDerivativeOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["sm"] = new ProjectPhotoDerivativeOptions { Width = 800, Height = 600, Quality = 80 }
            }
        });
        var environment = new TestWebHostEnvironment { ContentRootPath = Path.Combine(Path.GetTempPath(), "pm-photo-lock-tests") };
        var uploadRoot = new UploadRootProvider(options, Options.Create(new ProjectDocumentOptions()), environment, NullLogger<UploadRootProvider>.Instance);
        return new ProjectPhotoService(db, new FixedClock(), new RecordingAudit(), options, uploadRoot, NullLogger<ProjectPhotoService>.Instance);
    }

    private static async Task<byte[]> CreatePngAsync()
    {
        using var image = new Image<Rgba32>(1200, 900, new Rgba32(200, 80, 60, 255));
        using var stream = new MemoryStream();
        await image.SaveAsync(stream, new PngEncoder());
        return stream.ToArray();
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    }
}
