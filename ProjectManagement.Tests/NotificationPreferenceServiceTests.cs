using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services.Notifications;

namespace ProjectManagement.Tests;

public class NotificationPreferenceServiceTests
{
    [Fact]
    public async Task AllowsAsync_ReturnsFalse_WhenProjectMuteExists()
    {
        await using var scope = await CreateScopeAsync();
        var service = new NotificationPreferenceService(scope.Db);

        scope.Db.UserProjectMutes.Add(new UserProjectMute
        {
            UserId = "user-1",
            ProjectId = 5
        });
        await scope.Db.SaveChangesAsync();

        var allowed = await service.AllowsAsync(NotificationKind.RemarkCreated, "user-1", 5, CancellationToken.None);

        Assert.False(allowed);
    }

    [Fact]
    public async Task AllowsAsync_ReturnsPreferenceValue_WhenTableEntryExists()
    {
        await using var scope = await CreateScopeAsync();
        var service = new NotificationPreferenceService(scope.Db);

        scope.Db.UserNotificationPreferences.Add(new UserNotificationPreference
        {
            UserId = "user-2",
            Kind = NotificationKind.RemarkCreated,
            Allow = false
        });
        await scope.Db.SaveChangesAsync();

        var allowed = await service.AllowsAsync(NotificationKind.RemarkCreated, "user-2", null, CancellationToken.None);

        Assert.False(allowed);
    }

    [Fact]
    public async Task AllowsAsync_TablePreferenceOverridesClaim()
    {
        await using var scope = await CreateScopeAsync();
        var service = new NotificationPreferenceService(scope.Db);

        scope.Db.Set<IdentityUserClaim<string>>().Add(new IdentityUserClaim<string>
        {
            UserId = "user-3",
            ClaimType = NotificationClaimTypes.RemarkCreatedOptOut,
            ClaimValue = NotificationClaimTypes.OptOutValue
        });

        scope.Db.UserNotificationPreferences.Add(new UserNotificationPreference
        {
            UserId = "user-3",
            Kind = NotificationKind.RemarkCreated,
            Allow = true
        });
        await scope.Db.SaveChangesAsync();

        var allowed = await service.AllowsAsync(NotificationKind.RemarkCreated, "user-3", null, CancellationToken.None);

        Assert.True(allowed);
    }

    [Fact]
    public async Task AllowsAsync_FallsBackToClaimOptOut()
    {
        await using var scope = await CreateScopeAsync();
        var service = new NotificationPreferenceService(scope.Db);

        scope.Db.Set<IdentityUserClaim<string>>().Add(new IdentityUserClaim<string>
        {
            UserId = "user-4",
            ClaimType = NotificationClaimTypes.RemarkCreatedOptOut,
            ClaimValue = NotificationClaimTypes.OptOutValue
        });
        await scope.Db.SaveChangesAsync();

        var allowed = await service.AllowsAsync(NotificationKind.RemarkCreated, "user-4", null, CancellationToken.None);

        Assert.False(allowed);
    }

    [Fact]
    public async Task AllowsAsync_DefaultsToTrueWithoutPreference()
    {
        await using var scope = await CreateScopeAsync();
        var service = new NotificationPreferenceService(scope.Db);

        var allowed = await service.AllowsAsync(NotificationKind.RemarkCreated, "user-5", null, CancellationToken.None);

        Assert.True(allowed);
    }

    private static async Task<SqliteScope> CreateScopeAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        return new SqliteScope(db, connection);
    }

    private sealed class SqliteScope : IAsyncDisposable
    {
        public SqliteScope(ApplicationDbContext db, SqliteConnection connection)
        {
            Db = db;
            _connection = connection;
        }

        public ApplicationDbContext Db { get; }

        private readonly SqliteConnection _connection;

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
