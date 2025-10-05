using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services.Notifications;
using ProjectManagement.Services.Remarks;
using Xunit;

namespace ProjectManagement.Tests;

public class RemarkNotificationServiceTests
{
    [Fact]
    public async Task NotifyRemarkCreatedAsync_InternalRemarkTargetsCoreRoles()
    {
        await using var scope = await CreateContextAsync();
        var (service, publisher) = await CreateServiceAsync(scope.Db);

        var remark = CreateRemark(RemarkType.Internal);
        var actor = new RemarkActorContext("author-1", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var project = new RemarkProjectInfo(remark.ProjectId, "Project One", "po-1", "hod-1");

        await service.NotifyRemarkCreatedAsync(remark, actor, project, CancellationToken.None);

        Assert.Single(publisher.Events);
        var notification = publisher.Events[0];
        Assert.Equal(NotificationKind.RemarkCreated, notification.Kind);
        Assert.Equal(new[] { "po-1", "hod-1", "comdt-1" }.OrderBy(x => x), notification.Recipients.OrderBy(x => x));
    }

    [Fact]
    public async Task NotifyRemarkCreatedAsync_ExternalRemarkIncludesMco()
    {
        await using var scope = await CreateContextAsync();
        var (service, publisher) = await CreateServiceAsync(scope.Db);

        var remark = CreateRemark(RemarkType.External);
        var actor = new RemarkActorContext("author-2", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var project = new RemarkProjectInfo(remark.ProjectId, "Project Two", "po-1", "hod-1");

        await service.NotifyRemarkCreatedAsync(remark, actor, project, CancellationToken.None);

        Assert.Single(publisher.Events);
        var recipients = publisher.Events[0].Recipients.OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "comdt-1", "hod-1", "mco-1", "po-1" }, recipients);
    }

    [Fact]
    public async Task NotifyRemarkCreatedAsync_RespectsOptOutPreferences()
    {
        await using var scope = await CreateContextAsync();
        var (service, publisher) = await CreateServiceAsync(scope.Db);

        scope.Db.UserClaims.Add(new IdentityUserClaim<string>
        {
            UserId = "comdt-1",
            ClaimType = NotificationClaimTypes.RemarkCreatedOptOut,
            ClaimValue = NotificationClaimTypes.OptOutValue
        });
        await scope.Db.SaveChangesAsync();

        var remark = CreateRemark(RemarkType.Internal);
        var actor = new RemarkActorContext("author-3", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var project = new RemarkProjectInfo(remark.ProjectId, "Project Three", "po-1", "hod-1");

        await service.NotifyRemarkCreatedAsync(remark, actor, project, CancellationToken.None);

        Assert.Single(publisher.Events);
        var recipients = publisher.Events[0].Recipients.OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "hod-1", "po-1" }, recipients);
    }

    [Fact]
    public async Task NotifyRemarkCreatedAsync_TablePreferenceOverridesClaim()
    {
        await using var scope = await CreateContextAsync();
        var (service, publisher) = await CreateServiceAsync(scope.Db);

        scope.Db.UserClaims.Add(new IdentityUserClaim<string>
        {
            UserId = "comdt-1",
            ClaimType = NotificationClaimTypes.RemarkCreatedOptOut,
            ClaimValue = NotificationClaimTypes.OptOutValue
        });

        scope.Db.UserNotificationPreferences.Add(new UserNotificationPreference
        {
            UserId = "comdt-1",
            Kind = NotificationKind.RemarkCreated,
            Allow = true
        });

        await scope.Db.SaveChangesAsync();

        var remark = CreateRemark(RemarkType.Internal);
        var actor = new RemarkActorContext("author-6", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var project = new RemarkProjectInfo(remark.ProjectId, "Project Six", "po-1", "hod-1");

        await service.NotifyRemarkCreatedAsync(remark, actor, project, CancellationToken.None);

        Assert.Single(publisher.Events);
        var recipients = publisher.Events[0].Recipients.OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "comdt-1", "hod-1", "po-1" }, recipients);
    }

    [Fact]
    public async Task NotifyRemarkCreatedAsync_PayloadIncludesPreviewAndMetadata()
    {
        await using var scope = await CreateContextAsync();
        var (service, publisher) = await CreateServiceAsync(scope.Db);

        var longBody = "<p>" + new string('A', 150) + "</p>";
        var remark = new Remark
        {
            Id = 5,
            ProjectId = 100,
            AuthorUserId = "author-4",
            AuthorRole = RemarkActorRole.ProjectOfficer,
            Type = RemarkType.External,
            Body = longBody,
            EventDate = new DateOnly(2024, 9, 1),
            StageRef = null,
            StageNameSnapshot = "Field Stage",
            CreatedAtUtc = new DateTime(2024, 9, 2, 8, 30, 0, DateTimeKind.Utc)
        };

        var actor = new RemarkActorContext("author-4", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var project = new RemarkProjectInfo(remark.ProjectId, "Project Four", "po-1", "hod-1");

        await service.NotifyRemarkCreatedAsync(remark, actor, project, CancellationToken.None);

        Assert.Single(publisher.Events);
        dynamic payload = publisher.Events[0].Payload;
        Assert.Equal(remark.Id, (int)payload.RemarkId);
        Assert.Equal("Project Four", (string)payload.ProjectName);
        Assert.Equal("Field Stage", (string)payload.Stage);

        string preview = payload.Preview;
        Assert.Equal(121, preview.Length);
        Assert.EndsWith("…", preview, StringComparison.Ordinal);
        Assert.Equal(new string('A', 120), preview[..120]);
    }

    [Fact]
    public async Task NotifyRemarkCreatedAsync_IncludesMentionRecipients()
    {
        await using var scope = await CreateContextAsync();
        var (service, publisher) = await CreateServiceAsync(scope.Db);

        var mentionUser = CreateUser("extra-mention", "extra@unit.test");
        mentionUser.FullName = "Extra Mention";
        scope.Db.Users.Add(mentionUser);
        await scope.Db.SaveChangesAsync();

        var remark = CreateRemark(RemarkType.Internal);
        remark.Mentions.Add(new RemarkMention { RemarkId = remark.Id, UserId = mentionUser.Id });

        var actor = new RemarkActorContext("author", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var project = new RemarkProjectInfo(remark.ProjectId, "Project Five", "po-1", "hod-1");

        await service.NotifyRemarkCreatedAsync(remark, actor, project, CancellationToken.None);

        Assert.Single(publisher.Events);
        Assert.Contains(mentionUser.Id, publisher.Events[0].Recipients);
    }

    private static Remark CreateRemark(RemarkType type)
        => new()
        {
            Id = 1,
            ProjectId = 100,
            AuthorUserId = "author",
            AuthorRole = RemarkActorRole.ProjectOfficer,
            Type = type,
            Body = "<p>Some remark body</p>",
            EventDate = new DateOnly(2024, 9, 1),
            StageRef = "FS",
            StageNameSnapshot = null,
            CreatedAtUtc = new DateTime(2024, 9, 1, 12, 0, 0, DateTimeKind.Utc),
            Mentions = new List<RemarkMention>()
        };

    private static async Task<(RemarkNotificationService service, TestNotificationPublisher publisher)> CreateServiceAsync(ApplicationDbContext db)
    {
        SeedUsers(db);
        await db.SaveChangesAsync();

        var userManager = CreateUserManager(db);
        var publisher = new TestNotificationPublisher();

        var preferences = new NotificationPreferenceService(db);

        var service = new RemarkNotificationService(
            userManager,
            publisher,
            preferences,
            NullLogger<RemarkNotificationService>.Instance);

        return (service, publisher);
    }

    private static void SeedUsers(ApplicationDbContext db)
    {
        if (db.Users.Any())
        {
            return;
        }

        var po = CreateUser("po-1", "po1@unit.test");
        var hod = CreateUser("hod-1", "hod1@unit.test");
        var comdt = CreateUser("comdt-1", "comdt1@unit.test");
        var mco = CreateUser("mco-1", "mco1@unit.test");

        db.Users.AddRange(po, hod, comdt, mco);

        var comdtRole = new IdentityRole("Comdt")
        {
            NormalizedName = "COMDT"
        };
        var mcoRole = new IdentityRole("MCO")
        {
            NormalizedName = "MCO"
        };

        db.Roles.AddRange(comdtRole, mcoRole);

        db.UserRoles.AddRange(
            new IdentityUserRole<string> { RoleId = comdtRole.Id, UserId = comdt.Id },
            new IdentityUserRole<string> { RoleId = mcoRole.Id, UserId = mco.Id });
    }

    private static ApplicationUser CreateUser(string id, string userName)
        => new()
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = userName,
            NormalizedEmail = userName.ToUpperInvariant(),
            FullName = id,
            SecurityStamp = Guid.NewGuid().ToString()
        };

    private static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext db)
    {
        var store = new UserStore<ApplicationUser, IdentityRole, ApplicationDbContext>(db);
        var options = new OptionsWrapper<IdentityOptions>(new IdentityOptions());
        return new UserManager<ApplicationUser>(
            store,
            options,
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    private static async Task<SqliteScope> CreateContextAsync()
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

    private sealed record TestNotification(
        NotificationKind Kind,
        IReadOnlyCollection<string> Recipients,
        object Payload,
        string? Module,
        string? EventType,
        string? ScopeType,
        string? ScopeId,
        int? ProjectId,
        string? ActorUserId,
        string? Route,
        string? Title,
        string? Summary,
        string? Fingerprint);

    private sealed class TestNotificationPublisher : INotificationPublisher
    {
        public List<TestNotification> Events { get; } = new();

        public Task PublishAsync(
            NotificationKind kind,
            IReadOnlyCollection<string> recipientUserIds,
            object payload,
            CancellationToken cancellationToken = default)
            => PublishAsync(
                kind,
                recipientUserIds,
                payload,
                module: null,
                eventType: null,
                scopeType: null,
                scopeId: null,
                projectId: null,
                actorUserId: null,
                route: null,
                title: null,
                summary: null,
                fingerprint: null,
                cancellationToken);

        public Task PublishAsync(
            NotificationKind kind,
            IReadOnlyCollection<string> recipientUserIds,
            object payload,
            string? module,
            string? eventType,
            string? scopeType,
            string? scopeId,
            int? projectId,
            string? actorUserId,
            string? route,
            string? title,
            string? summary,
            string? fingerprint,
            CancellationToken cancellationToken = default)
        {
            Events.Add(new TestNotification(
                kind,
                recipientUserIds.ToArray(),
                payload,
                module,
                eventType,
                scopeType,
                scopeId,
                projectId,
                actorUserId,
                route,
                title,
                summary,
                fingerprint));
            return Task.CompletedTask;
        }
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
